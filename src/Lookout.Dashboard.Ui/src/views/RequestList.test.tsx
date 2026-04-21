import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListResponse } from '../api/types';
import { RequestList } from './RequestList';

function makeResponse(): EntryListResponse {
  return {
    entries: [
      {
        id: '11111111-1111-1111-1111-111111111111',
        type: 'http',
        timestamp: Date.now() - 2000,
        durationMs: 42.3,
        tags: {
          'http.method': 'GET',
          'http.path': '/weatherforecast',
          'http.status': '200',
        },
        content: {
          method: 'GET',
          path: '/weatherforecast',
          queryString: '',
          statusCode: 200,
          durationMs: 42.3,
          requestHeaders: {},
          responseHeaders: {},
        },
      },
      {
        id: '22222222-2222-2222-2222-222222222222',
        type: 'http',
        timestamp: Date.now() - 1000,
        durationMs: 1200,
        tags: {
          'http.method': 'POST',
          'http.path': '/orders',
          'http.status': '500',
        },
        content: {
          method: 'POST',
          path: '/orders',
          queryString: '',
          statusCode: 500,
          durationMs: 1200,
          requestHeaders: {},
          responseHeaders: {},
        },
      },
    ],
    nextBefore: null,
  };
}

describe('RequestList', () => {
  const urls: string[] = [];

  beforeEach(() => {
    urls.length = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        urls.push(url);
        return new Response(JSON.stringify(makeResponse()), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as unknown as typeof fetch,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders captured requests', async () => {
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getAllByTestId('request-row')).toHaveLength(2);
    });
    expect(screen.getByText('/weatherforecast')).toBeInTheDocument();
    expect(screen.getByText('/orders')).toBeInTheDocument();
  });

  it('sends filter values to the API', async () => {
    const user = userEvent.setup();
    render(<RequestList />);
    await waitFor(() => expect(screen.getAllByTestId('request-row')).toHaveLength(2));

    await user.selectOptions(screen.getByLabelText(/method/i), 'POST');
    await user.type(screen.getByLabelText(/path/i), 'orders');

    await waitFor(() => {
      const last = urls[urls.length - 1]!;
      expect(last).toContain('method=POST');
      expect(last).toContain('path=orders');
    });
  });

  it('shows an empty state when no entries come back', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ entries: [], nextBefore: null }), {
            status: 200,
            headers: { 'content-type': 'application/json' },
          }),
      ) as unknown as typeof fetch,
    );
    render(<RequestList />);
    await waitFor(() => {
      expect(screen.getByTestId('empty-state')).toBeInTheDocument();
    });
  });
});
