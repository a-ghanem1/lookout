import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { EntryDto } from '../api/types';
import { DetailBody } from './RequestDetail';

const httpEntry: EntryDto = {
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

function efEntry(id: string, sql: string, duration = 1.23): EntryDto {
  return {
    id,
    type: 'ef',
    timestamp: 1_700_000_000_100,
    requestId: 'req-42',
    durationMs: duration,
    tags: { 'db.system': 'ef' },
    content: {
      commandText: sql,
      parameters: [{ name: '@p0', value: '1', dbType: 'Int32' }],
      durationMs: duration,
      rowsAffected: 3,
      dbContextType: 'SampleDbContext',
      commandType: 'Reader',
      stack: [
        { method: 'WebApp.Program.<Main>$', file: 'Program.cs', line: 42 },
      ],
    },
  };
}

describe('RequestDetail', () => {
  it('renders method, path, status and metadata from a mock HTTP entry', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.getByTestId('request-detail')).toBeInTheDocument();
    expect(screen.getByTestId('method-badge')).toHaveTextContent('GET');
    expect(screen.getByTestId('status-badge')).toHaveTextContent('200');
    expect(screen.getByText(/weatherforecast/)).toBeInTheDocument();
    expect(screen.getByText('alice')).toBeInTheDocument();
    expect(screen.getByText('req-42')).toBeInTheDocument();
  });

  it('pretty-prints a JSON response body', () => {
    render(<DetailBody entries={[httpEntry]} />);
    const preText = document.querySelector('pre')?.textContent ?? '';
    expect(preText).toContain('"ok": true');
  });

  it('renders one EF row per ef entry', () => {
    const entries: EntryDto[] = [
      httpEntry,
      efEntry('ef-1', 'SELECT * FROM products'),
      efEntry('ef-2', 'SELECT * FROM orders'),
      efEntry('ef-3', 'SELECT * FROM customers'),
    ];
    render(<DetailBody entries={entries} />);
    const rows = screen.getAllByTestId('ef-query-row');
    expect(rows).toHaveLength(3);
    expect(screen.getByTestId('ef-section')).toHaveTextContent('EF queries');
  });

  it('toggles an EF row body when the header is clicked', async () => {
    const user = userEvent.setup();
    const entries: EntryDto[] = [httpEntry, efEntry('ef-1', 'SELECT * FROM products WHERE id = @p0')];
    render(<DetailBody entries={entries} />);

    expect(screen.queryByTestId('ef-query-body')).not.toBeInTheDocument();

    const header = screen.getAllByTestId('ef-query-row')[0].querySelector('button')!;
    await user.click(header);
    expect(screen.getByTestId('ef-query-body')).toBeInTheDocument();

    await user.click(header);
    expect(screen.queryByTestId('ef-query-body')).not.toBeInTheDocument();
  });

  it('shows an empty-state EF section when no ef entries exist', () => {
    render(<DetailBody entries={[httpEntry]} />);
    const section = screen.getByTestId('ef-section');
    expect(section).toHaveTextContent(/No queries captured/);
  });

  it('renders not-found when no http entry is present', () => {
    render(<DetailBody entries={[efEntry('ef-1', 'SELECT 1')]} />);
    expect(screen.getByTestId('detail-not-found')).toBeInTheDocument();
  });
});
