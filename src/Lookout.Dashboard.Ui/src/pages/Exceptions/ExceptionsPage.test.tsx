import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { ExceptionsPage } from './ExceptionsPage';

function makeExceptionEntry(
  id: string,
  opts: {
    exceptionType?: string;
    message?: string;
    handled?: boolean;
    requestId?: string;
    stack?: { method: string; file?: string; line?: number }[];
    innerExceptions?: { type: string; message: string }[];
  } = {},
) {
  const handled = opts.handled ?? true;
  return {
    id,
    type: 'exception',
    timestamp: Date.now() - 1000,
    requestId: opts.requestId,
    tags: {
      'exception.type': opts.exceptionType ?? 'System.InvalidOperationException',
      'exception.handled': handled ? 'true' : 'false',
    },
    content: {
      exceptionType: opts.exceptionType ?? 'System.InvalidOperationException',
      message: opts.message ?? 'Something went wrong',
      stack: opts.stack ?? [{ method: 'MyApp.OrderService.Process', file: 'OrderService.cs', line: 42 }],
      innerExceptions: opts.innerExceptions ?? [],
      source: null,
      hResult: -2146233088,
      handled,
    },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

describe('ExceptionsPage', () => {
  const capturedUrls: string[] = [];

  beforeEach(() => {
    capturedUrls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        capturedUrls.push(typeof input === 'string' ? input : input.toString());
        return new Response(
          JSON.stringify(
            makeResponse([
              makeExceptionEntry('id-1', {
                exceptionType: 'System.InvalidOperationException',
                message: 'Order not found',
                handled: true,
                requestId: 'req-x',
              }),
              makeExceptionEntry('id-2', {
                exceptionType: 'System.NullReferenceException',
                message: 'Object reference not set',
                handled: false,
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

  it('renders exception type names after fetch', async () => {
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByText('System.InvalidOperationException')).toBeInTheDocument();
    });
    expect(screen.getByText('System.NullReferenceException')).toBeInTheDocument();
  });

  it('renders Handled chip for handled exceptions', async () => {
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Handled').length).toBeGreaterThan(0);
    });
  });

  it('renders Unhandled chip for unhandled exceptions', async () => {
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByText('Unhandled')).toBeInTheDocument();
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

    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
    expect(screen.getByText(/InvalidOperationException/)).toBeInTheDocument();
  });

  it('sends type=exception in every fetch', async () => {
    render(<ExceptionsPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));

    expect(capturedUrls[0]).toContain('type=exception');
  });

  it('filters to Handled only when Handled chip is clicked', async () => {
    const user = userEvent.setup();
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByText('System.NullReferenceException')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /^Handled$/i }));

    await waitFor(() => {
      expect(screen.getByText('System.InvalidOperationException')).toBeInTheDocument();
      expect(screen.queryByText('System.NullReferenceException')).not.toBeInTheDocument();
    });
  });

  it('filters to Unhandled only when Unhandled chip is clicked', async () => {
    const user = userEvent.setup();
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByText('System.InvalidOperationException')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /^Unhandled$/i }));

    await waitFor(() => {
      expect(screen.getByText('System.NullReferenceException')).toBeInTheDocument();
      expect(screen.queryByText('System.InvalidOperationException')).not.toBeInTheDocument();
    });
  });

  it('filters by type search', async () => {
    const user = userEvent.setup();
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByText('System.NullReferenceException')).toBeInTheDocument();
    });

    await user.type(screen.getByRole('searchbox', { name: /search exception type/i }), 'InvalidOperation');

    await waitFor(() => {
      expect(screen.getByText('System.InvalidOperationException')).toBeInTheDocument();
      expect(screen.queryByText('System.NullReferenceException')).not.toBeInTheDocument();
    });
  });

  it('renders request link for entries with requestId', async () => {
    render(<ExceptionsPage />);

    await waitFor(() => {
      const link = screen.getByRole('link', { name: /view parent request/i });
      expect(link).toHaveAttribute('href', '#/requests/req-x');
    });
  });

  it('renders Background for entries without requestId', async () => {
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });

  it('navigates to detail on row click', async () => {
    const user = userEvent.setup();
    render(<ExceptionsPage />);

    await waitFor(() => {
      expect(screen.getByText('System.InvalidOperationException')).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('Order not found'));
    if (entryRow) await user.click(entryRow);

    expect(window.location.hash).toContain('/exceptions/id-1');
  });

  it('renders detail view with stack trace when id prop is provided', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify(
            makeExceptionEntry('id-1', {
              exceptionType: 'System.InvalidOperationException',
              message: 'Order not found',
              handled: true,
              stack: [{ method: 'MyApp.OrderService.Process', file: 'OrderService.cs', line: 42 }],
            }),
          ),
          { status: 200, headers: { 'content-type': 'application/json' } },
        ),
      ) as unknown as typeof fetch,
    );

    render(<ExceptionsPage id="id-1" />);

    await waitFor(() => {
      expect(screen.getByTestId('exception-stack')).toBeInTheDocument();
    });
    expect(screen.getByText('MyApp.OrderService.Process')).toBeInTheDocument();
  });
});
