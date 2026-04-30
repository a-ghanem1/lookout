import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../../api/types';
import { DumpPage } from './DumpPage';

function makeDumpEntry(
  id: string,
  opts: {
    label?: string;
    json?: string;
    callerFile?: string;
    callerLine?: number;
    callerMember?: string;
    requestId?: string;
  } = {},
) {
  const json = opts.json ?? '{"value":42}';
  return {
    id,
    type: 'dump',
    timestamp: Date.now() - 1000,
    requestId: opts.requestId,
    tags: {},
    content: {
      label: opts.label ?? null,
      json,
      jsonTruncated: false,
      valueType: 'System.Int32',
      callerFile: opts.callerFile ?? 'C:\\Projects\\App\\OrderService.cs',
      callerLine: opts.callerLine ?? 42,
      callerMember: opts.callerMember ?? 'ProcessOrder',
    },
  };
}

function makeResponse(entries: unknown[] = []): EntryListResponse {
  return { entries: entries as EntryListResponse['entries'], nextBefore: null };
}

describe('DumpPage', () => {
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
              makeDumpEntry('id-1', {
                label: 'order',
                json: '{"id":1,"total":99.99}',
                callerFile: 'C:\\Projects\\App\\OrderService.cs',
                callerLine: 42,
                requestId: 'req-x',
              }),
              makeDumpEntry('id-2', {
                label: undefined,
                json: '{"items":[1,2,3]}',
                callerFile: '/home/app/CartService.cs',
                callerLine: 17,
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

  it('renders labeled entry with label badge', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByText('order')).toBeInTheDocument();
    });
  });

  it('renders unnamed entry with (unnamed) badge', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByText('(unnamed)')).toBeInTheDocument();
    });
  });

  it('renders caller file:line in summary', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByText('OrderService.cs:42')).toBeInTheDocument();
    });
    expect(screen.getByText('CartService.cs:17')).toBeInTheDocument();
  });

  it('renders JSON preview in summary', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByText('{"id":1,"total":99.99}')).toBeInTheDocument();
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

    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-empty-state')).toBeInTheDocument();
    });
    expect(screen.getByText(/Lookout\.Dump\(\)/)).toBeInTheDocument();
  });

  it('sends type=dump in every fetch', async () => {
    render(<DumpPage />);

    await waitFor(() => expect(capturedUrls.length).toBeGreaterThan(0));

    expect(capturedUrls[0]).toContain('type=dump');
  });

  it('expands row to show pretty-printed JSON when clicked', async () => {
    const user = userEvent.setup();
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByText('order')).toBeInTheDocument();
    });

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('order') && r.textContent?.includes('OrderService.cs'));
    if (entryRow) await user.click(entryRow);

    await waitFor(() => {
      expect(screen.getByTestId('expand-id-1')).toBeInTheDocument();
    });
    const expandPanel = screen.getByTestId('expand-id-1');
    const pre = expandPanel.querySelector('pre');
    expect(pre).toBeInTheDocument();
    // pretty-printed JSON has "key": value spacing
    expect(pre!.textContent).toContain('"id": 1');
    expect(pre!.textContent).toContain('"total": 99.99');
  });

  it('shows C# valueType in expand panel', async () => {
    const user = userEvent.setup();
    render(<DumpPage />);

    await waitFor(() => expect(screen.getByText('order')).toBeInTheDocument());

    const rows = screen.getAllByRole('button');
    const entryRow = rows.find((r) => r.textContent?.includes('order') && r.textContent?.includes('OrderService.cs'));
    if (entryRow) await user.click(entryRow);

    await waitFor(() => {
      const panel = screen.getByTestId('expand-id-1');
      expect(panel).toHaveTextContent('System.Int32');
    });
  });

  it('renders type badge for object JSON', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      const badges = screen.getAllByTestId('dump-kind-badge');
      expect(badges.length).toBeGreaterThan(0);
      // both entries have object JSON starting with {
      expect(badges[0]).toHaveTextContent('{ }');
    });
  });

  it('filters by label when label search is filled', async () => {
    const user = userEvent.setup();
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByText('order')).toBeInTheDocument();
      expect(screen.getByText('(unnamed)')).toBeInTheDocument();
    });

    await user.type(screen.getByRole('searchbox', { name: /search label/i }), 'order');

    await waitFor(() => {
      expect(screen.getByText('order')).toBeInTheDocument();
      expect(screen.queryByText('(unnamed)')).not.toBeInTheDocument();
    });
  });

  it('renders request link for entries with requestId', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      const link = screen.getByRole('link', { name: /view parent request/i });
      expect(link).toHaveAttribute('href', '#/requests/req-x');
    });
  });

  it('renders Background for entries without requestId', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getAllByText('Background').length).toBeGreaterThan(0);
    });
  });

  it('shows footer with showing count', async () => {
    render(<DumpPage />);

    await waitFor(() => {
      expect(screen.getByTestId('entry-list-footer')).toBeInTheDocument();
    });
  });
});
