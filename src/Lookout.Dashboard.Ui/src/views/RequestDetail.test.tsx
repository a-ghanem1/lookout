import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { EntryDto } from '../api/types';
import { DetailBody } from './RequestDetail';

const entry: EntryDto = {
  id: '11111111-1111-1111-1111-111111111111',
  type: 'http',
  timestamp: 1_700_000_000_000,
  requestId: 'req-42',
  durationMs: 123.4,
  tags: {
    'http.method': 'GET',
    'http.path': '/weatherforecast',
    'http.status': '200',
  },
  content: {
    method: 'GET',
    path: '/weatherforecast',
    queryString: '?q=1',
    statusCode: 200,
    durationMs: 123.4,
    requestHeaders: { 'User-Agent': 'vitest' },
    responseHeaders: { 'Content-Type': 'application/json' },
    requestBody: null,
    responseBody: '{"ok":true}',
    user: 'alice',
  },
};

describe('RequestDetail', () => {
  it('renders method, path, status and metadata from a mock entry', () => {
    render(<DetailBody entry={entry} />);
    expect(screen.getByTestId('request-detail')).toBeInTheDocument();
    expect(screen.getByTestId('method-badge')).toHaveTextContent('GET');
    expect(screen.getByTestId('status-badge')).toHaveTextContent('200');
    expect(screen.getByText(/weatherforecast/)).toBeInTheDocument();
    expect(screen.getByText('alice')).toBeInTheDocument();
    expect(screen.getByText('req-42')).toBeInTheDocument();
  });

  it('pretty-prints a JSON response body', () => {
    render(<DetailBody entry={entry} />);
    // Pretty-printed JSON contains a newline+indent.
    const preText = document.querySelector('pre')?.textContent ?? '';
    expect(preText).toContain('"ok": true');
  });
});
