import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { LogsPage } from './LogsPage';

function makeLogEntry(
  id: string,
  opts: {
    level?: string;
    category?: string;
    message?: string;
    requestId?: string;
    scopes?: string[];
    eventId?: { id: number; name?: string };
  } = {},
) {
  return {
    id,
    type: 'log',
    timestamp: Date.now() - 1000,
    requestId: opts.requestId,
    tags: {
      'log.level': opts.level ?? 'Information',
      'log.category': opts.category ?? 'MyApp.OrderService',
    },
    content: {
      level: opts.level ?? 'Information',
      category: opts.category ?? 'MyApp.OrderService',
      message: opts.message ?? 'Processing order 42',
      eventId: opts.eventId ?? { id: 0, name: null },
      scopes: opts.scopes ?? [],
      exceptionType: null,
      exceptionMessage: null,
    },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

describe('LogsPage', () => {
  const capturedUrls: string[] = [];

  beforeEach(() => {
    capturedUrls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        capturedUrls.push(url);
        if (url.includes('histogram')) {
          return new Response(JSON.stringify([]), { status: 200, headers: { 'content-type': 'application/json' } });
        }
        return new Response(
          JSON.stringify(
            makeResponse([
              makeLogEntry('id-1', {
                level: 'Information',
                category: 'MyApp.OrderService',
                message: 'Order created',
                requestId: 'req-abc',
              }),
              makeLogEntry('id-2', {
                level: 'Warning',
                category: 'MyApp.PaymentService',
                message: 'Payment retry attempt 2',
              }),
              makeLogEntry('id-3', {
                level: 'Error',
                category: 'MyApp.NotificationService',
                message: 'Failed to send email',
              }),
            ]),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders log entries after fetch', async () => {
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByText('Order created')).toBeInTheDocument();
    });
    expect(screen.getByText('Payment retry attempt 2')).toBeInTheDocument();
    expect(screen.getByText('Failed to send email')).toBeInTheDocument();
  });

  it('renders level chip for Warning entries', async () => {
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByText('Warning')).toBeInTheDocument();
    });
  });

  it('renders level chip for Error entries', async () => {
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByText('Error')).toBeInTheDocument();
    });
  });

  it('shows empty state with correct copy when no entries', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify(makeResponse([])), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );

    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
    expect(screen.getByText(/No logs captured/)).toBeInTheDocument();
  });

  it('sends type=log in every fetch', async () => {
    render(<LogsPage />);

    await waitFor(() => expect(capturedUrls.some((u) => u.includes('type=log'))).toBe(true));
  });

  it('filters by Warn+ chip — hides Information entries', async () => {
    const user = userEvent.setup();
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByText('Order created')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /^Warn\+$/i }));

    await waitFor(() => {
      expect(screen.queryByText('Order created')).not.toBeInTheDocument();
      expect(screen.getByText('Payment retry attempt 2')).toBeInTheDocument();
      expect(screen.getByText('Failed to send email')).toBeInTheDocument();
    });
  });

  it('filters by Error+ chip — shows only Error and above', async () => {
    const user = userEvent.setup();
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByText('Payment retry attempt 2')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /^Error\+$/i }));

    await waitFor(() => {
      expect(screen.queryByText('Order created')).not.toBeInTheDocument();
      expect(screen.queryByText('Payment retry attempt 2')).not.toBeInTheDocument();
      expect(screen.getByText('Failed to send email')).toBeInTheDocument();
    });
  });

  it('expands row to show scopes when clicked', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify(
            makeResponse([
              makeLogEntry('id-1', {
                level: 'Information',
                message: 'Order created',
                scopes: ['RequestId: abc', 'OrderId: 42'],
              }),
            ]),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ) as unknown as typeof fetch,
    );

    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByText('Order created')).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('Order created'));
    if (entryRow) await user.click(entryRow);

    await waitFor(() => {
      expect(screen.getByTestId('expand-id-1')).toBeInTheDocument();
    });
    expect(screen.getByText(/RequestId: abc/)).toBeInTheDocument();
  });

  it('renders request link for entries with requestId', async () => {
    render(<LogsPage />);

    await waitFor(() => {
      const link = screen.getByRole('link', { name: /view parent request/i });
      expect(link).toHaveAttribute('href', '#/requests/req-abc');
    });
  });

  it('renders Background for entries without requestId', async () => {
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });

  it('shows footer with showing count', async () => {
    render(<LogsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-list-footer')).toBeInTheDocument();
    });
  });
});
