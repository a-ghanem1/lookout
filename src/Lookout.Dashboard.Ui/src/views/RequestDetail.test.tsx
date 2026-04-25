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

function efEntry(id: string, sql: string, duration = 1.23, tags: Record<string, string> = {}): EntryDto {
  return {
    id,
    type: 'ef',
    timestamp: 1_700_000_000_100,
    requestId: 'req-42',
    durationMs: duration,
    tags: { 'db.system': 'ef', ...tags },
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

function sqlEntry(id: string, sql: string, duration = 2.34, tags: Record<string, string> = {}): EntryDto {
  return {
    id,
    type: 'sql',
    timestamp: 1_700_000_000_200,
    requestId: 'req-42',
    durationMs: duration,
    tags: { 'db.system': 'ado', ...tags },
    content: {
      commandText: sql,
      parameters: [{ name: '@id', value: '7', dbType: 'Int32' }],
      durationMs: duration,
      rowsAffected: null,
      commandType: 'Text',
      stack: [
        { method: 'WebApp.OrdersController.Get', file: 'OrdersController.cs', line: 18 },
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

  it('renders one row per db entry (ef and sql combined)', () => {
    const entries: EntryDto[] = [
      httpEntry,
      efEntry('ef-1', 'SELECT * FROM products'),
      efEntry('ef-2', 'SELECT * FROM orders'),
      sqlEntry('sql-1', 'SELECT * FROM customers'),
    ];
    render(<DetailBody entries={entries} />);
    const rows = screen.getAllByTestId('ef-query-row');
    expect(rows).toHaveLength(3);
    expect(screen.getByTestId('db-section')).toHaveTextContent('Database queries');
  });

  it('toggles a db row body when the header is clicked', async () => {
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

  it('shows an empty-state DB section when no db entries exist', () => {
    render(<DetailBody entries={[httpEntry]} />);
    const section = screen.getByTestId('db-section');
    expect(section).toHaveTextContent(/No queries captured/);
  });

  it('renders not-found when no http entry is present', () => {
    render(<DetailBody entries={[efEntry('ef-1', 'SELECT 1')]} />);
    expect(screen.getByTestId('detail-not-found')).toBeInTheDocument();
  });
});

describe('RequestDetail — EF vs SQL source badges', () => {
  it('renders EF badge for ef-type entries', () => {
    render(<DetailBody entries={[httpEntry, efEntry('ef-1', 'SELECT 1')]} />);
    const badges = screen.getAllByTestId('db-source-badge');
    expect(badges[0]).toHaveTextContent('EF');
  });

  it('renders SQL badge for sql-type entries', () => {
    render(<DetailBody entries={[httpEntry, sqlEntry('sql-1', 'SELECT 1')]} />);
    const badges = screen.getAllByTestId('db-source-badge');
    expect(badges[0]).toHaveTextContent('SQL');
  });

  it('renders mixed EF and SQL badges side by side', () => {
    const entries: EntryDto[] = [
      httpEntry,
      efEntry('ef-1', 'SELECT * FROM products'),
      sqlEntry('sql-1', 'SELECT * FROM customers'),
    ];
    render(<DetailBody entries={entries} />);
    const badges = screen.getAllByTestId('db-source-badge').map((b) => b.textContent);
    expect(badges).toContain('EF');
    expect(badges).toContain('SQL');
  });
});

describe('RequestDetail — timestamp sort', () => {
  it('renders mixed EF and SQL entries sorted by timestamp (ascending)', () => {
    const entries: EntryDto[] = [
      httpEntry,
      { ...efEntry('ef-late', 'SELECT * FROM orders'), timestamp: 1_700_000_003_000 },
      { ...sqlEntry('sql-early', 'SELECT * FROM customers'), timestamp: 1_700_000_001_000 },
      { ...efEntry('ef-mid', 'SELECT * FROM products'), timestamp: 1_700_000_002_000 },
    ];
    render(<DetailBody entries={entries} />);
    const badges = screen.getAllByTestId('db-source-badge');
    // sorted: sql-early (SQL), ef-mid (EF), ef-late (EF)
    expect(badges[0]).toHaveTextContent('SQL');
    expect(badges[1]).toHaveTextContent('EF');
    expect(badges[2]).toHaveTextContent('EF');
  });
});

describe('RequestDetail — N+1 banner', () => {
  const N1_GROUP = 'abc12345';

  let _n1Seq = 0;
  function makeN1Entries(count: number, group = N1_GROUP): EntryDto[] {
    return Array.from({ length: count }, () =>
      efEntry(`ef-n1-${_n1Seq++}`, 'SELECT * FROM orders WHERE id = @p0', 1, {
        'n1.group': group,
        'n1.count': String(count),
      }),
    );
  }

  it('does not render banner when no N+1 is detected', () => {
    render(<DetailBody entries={[httpEntry, efEntry('ef-1', 'SELECT 1')]} />);
    expect(screen.queryByTestId('n1-banner')).not.toBeInTheDocument();
  });

  it('renders banner when entries carry n1.group tags', () => {
    const entries: EntryDto[] = [httpEntry, ...makeN1Entries(3)];
    render(<DetailBody entries={entries} />);
    expect(screen.getByTestId('n1-banner')).toBeInTheDocument();
  });

  it('banner text reflects query count and group count', () => {
    const entries: EntryDto[] = [httpEntry, ...makeN1Entries(5)];
    render(<DetailBody entries={entries} />);
    const banner = screen.getByTestId('n1-banner');
    expect(banner).toHaveTextContent(/5 queries/);
    expect(banner).toHaveTextContent(/1 group/);
  });

  it('banner shows correct group count for two distinct groups', () => {
    const entries: EntryDto[] = [
      httpEntry,
      ...makeN1Entries(3, 'group-a'),
      ...makeN1Entries(3, 'group-b'),
    ];
    render(<DetailBody entries={entries} />);
    const banner = screen.getByTestId('n1-banner');
    expect(banner).toHaveTextContent(/6 queries/);
    expect(banner).toHaveTextContent(/2 groups/);
  });

  it('dismisses banner when dismiss button is clicked', async () => {
    const user = userEvent.setup();
    const entries: EntryDto[] = [httpEntry, ...makeN1Entries(3)];
    render(<DetailBody entries={entries} />);

    expect(screen.getByTestId('n1-banner')).toBeInTheDocument();

    await user.click(screen.getByLabelText('Dismiss N+1 warning'));
    expect(screen.queryByTestId('n1-banner')).not.toBeInTheDocument();
  });

  it('clicking banner body toggles aria-pressed (highlight state)', async () => {
    const user = userEvent.setup();
    const entries: EntryDto[] = [httpEntry, ...makeN1Entries(3)];
    render(<DetailBody entries={entries} />);

    const banner = screen.getByTestId('n1-banner');
    const bodyBtn = banner.querySelector('button[aria-pressed]')!;

    expect(bodyBtn).toHaveAttribute('aria-pressed', 'false');
    await user.click(bodyBtn);
    expect(bodyBtn).toHaveAttribute('aria-pressed', 'true');
    await user.click(bodyBtn);
    expect(bodyBtn).toHaveAttribute('aria-pressed', 'false');
  });
});
