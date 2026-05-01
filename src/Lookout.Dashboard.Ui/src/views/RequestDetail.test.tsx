import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type {
  CacheEntryContent,
  DumpEntryContent,
  EntryDto,
  ExceptionEntryContent,
  JobEnqueueEntryContent,
  JobExecutionEntryContent,
  LogEntryContent,
  OutboundHttpEntryContent,
} from '../api/types';
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

  it('hides the DB section entirely when no db entries exist', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('db-section')).not.toBeInTheDocument();
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

// ── Outbound HTTP section ─────────────────────────────────────────────────────

function httpOutEntry(
  id: string,
  overrides: Partial<OutboundHttpEntryContent> = {},
  tagOverrides: Record<string, string> = {},
): EntryDto {
  const content: OutboundHttpEntryContent = {
    method: 'GET',
    url: 'https://api.example.com/v1/data',
    statusCode: 200,
    durationMs: 45.6,
    requestHeaders: { Accept: 'application/json' },
    responseHeaders: { 'Content-Type': 'application/json' },
    requestBody: null,
    responseBody: null,
    errorType: null,
    errorMessage: null,
    ...overrides,
  };
  return {
    id,
    type: 'http-out',
    timestamp: 1_700_000_000_300,
    requestId: 'req-42',
    durationMs: 45.6,
    tags: {
      'http.method': content.method,
      'http.out': 'true',
      'http.url.host': 'api.example.com',
      'http.url.path': '/v1/data',
      'http.status': String(content.statusCode ?? ''),
      ...tagOverrides,
    },
    content,
  };
}

describe('RequestDetail — Outbound HTTP section', () => {
  it('renders section with rows for http-out entries', () => {
    render(<DetailBody entries={[httpEntry, httpOutEntry('out-1')]} />);
    expect(screen.getByTestId('http-out-section')).toBeInTheDocument();
    expect(screen.getAllByTestId('http-out-row')).toHaveLength(1);
  });

  it('does not render section when no http-out entries', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('http-out-section')).not.toBeInTheDocument();
  });

  it('renders HTTP source badge on each row', () => {
    render(<DetailBody entries={[httpEntry, httpOutEntry('out-1')]} />);
    expect(screen.getByTestId('http-source-badge')).toHaveTextContent('HTTP');
  });

  it('shows method and status on each row', () => {
    render(<DetailBody entries={[httpEntry, httpOutEntry('out-1')]} />);
    const row = screen.getByTestId('http-out-row');
    expect(row).toHaveTextContent('GET');
    expect(row).toHaveTextContent('200');
  });

  it('toggles expanded body when row header is clicked', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, httpOutEntry('out-1')]} />);
    expect(screen.queryByTestId('http-out-row-body')).not.toBeInTheDocument();
    const btn = screen.getByTestId('http-out-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('http-out-row-body')).toBeInTheDocument();
    expect(screen.getByTestId('http-out-row-body')).toHaveTextContent('https://api.example.com/v1/data');
  });

  it('shows ERR badge when http.error tag is present', () => {
    render(
      <DetailBody
        entries={[
          httpEntry,
          httpOutEntry(
            'out-err',
            { statusCode: null, errorType: 'System.Net.Http.HttpRequestException', errorMessage: 'timeout' },
            { 'http.error': 'System.Net.Http.HttpRequestException' },
          ),
        ]}
      />,
    );
    expect(screen.getByTestId('http-out-error-badge')).toBeInTheDocument();
  });

  it('shows "View full details" link pointing to #/http-clients/:id when expanded', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, httpOutEntry('out-1')]} />);
    const btn = screen.getByTestId('http-out-row').querySelector('button')!;
    await user.click(btn);
    const link = screen.getByTestId('http-out-view-link');
    expect(link).toHaveAttribute('href', '#/http-clients/out-1');
    expect(link).toHaveTextContent('View full details');
  });

  it('shows error details in expanded row', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[
          httpEntry,
          httpOutEntry(
            'out-err',
            { statusCode: null, errorType: 'System.Net.Http.HttpRequestException', errorMessage: 'connection refused' },
            { 'http.error': 'System.Net.Http.HttpRequestException' },
          ),
        ]}
      />,
    );
    const btn = screen.getByTestId('http-out-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('http-out-row-body')).toHaveTextContent('connection refused');
  });

  it('renders multiple rows sorted by timestamp', () => {
    const entries: EntryDto[] = [
      httpEntry,
      { ...httpOutEntry('out-late'), timestamp: 1_700_000_003_000 },
      { ...httpOutEntry('out-early'), timestamp: 1_700_000_001_000 },
    ];
    render(<DetailBody entries={entries} />);
    const rows = screen.getAllByTestId('http-out-row');
    expect(rows).toHaveLength(2);
  });
});

// ── Cache section ─────────────────────────────────────────────────────────────

function cacheEntry(
  id: string,
  operation: string,
  hit: boolean | null = null,
  system: 'memory' | 'distributed' = 'memory',
): EntryDto {
  const content: CacheEntryContent = {
    operation,
    key: `cache-key-${id}`,
    hit: operation === 'Get' ? hit : null,
    durationMs: 0.12,
    valueType: system === 'memory' ? 'System.String' : null,
    valueBytes: system === 'distributed' ? 42 : null,
  };
  const tags: Record<string, string> = {
    'cache.system': system === 'memory' ? 'memory' : 'distributed',
    'cache.key': content.key,
  };
  if (operation === 'Get' && hit !== null) tags['cache.hit'] = hit ? 'true' : 'false';
  if (system !== 'memory') tags['cache.provider'] = 'MemoryDistributedCache';

  return {
    id,
    type: 'cache',
    timestamp: 1_700_000_000_400,
    requestId: 'req-42',
    durationMs: 0.12,
    tags,
    content,
  };
}

describe('RequestDetail — Cache section', () => {
  it('renders section with rows for cache entries', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Get', false)]} />);
    expect(screen.getByTestId('cache-section')).toBeInTheDocument();
    expect(screen.getAllByTestId('cache-row')).toHaveLength(1);
  });

  it('does not render section when no cache entries', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('cache-section')).not.toBeInTheDocument();
  });

  it('renders MEM source badge for memory cache entries', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Get', true, 'memory')]} />);
    expect(screen.getByTestId('cache-source-badge')).toHaveTextContent('MEM');
  });

  it('renders DIST source badge for distributed cache entries', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Get', true, 'distributed')]} />);
    expect(screen.getByTestId('cache-source-badge')).toHaveTextContent('DIST');
  });

  it('renders operation badge', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Set')]} />);
    expect(screen.getByTestId('cache-operation-badge')).toHaveTextContent('Set');
  });

  it('renders hit dot for a Get hit', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Get', true)]} />);
    expect(screen.getByTestId('cache-hit-dot')).toBeInTheDocument();
    expect(screen.queryByTestId('cache-miss-dot')).not.toBeInTheDocument();
  });

  it('renders miss dot for a Get miss', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Get', false)]} />);
    expect(screen.getByTestId('cache-miss-dot')).toBeInTheDocument();
    expect(screen.queryByTestId('cache-hit-dot')).not.toBeInTheDocument();
  });

  it('renders no hit/miss dot for non-Get operations', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Set')]} />);
    expect(screen.queryByTestId('cache-hit-dot')).not.toBeInTheDocument();
    expect(screen.queryByTestId('cache-miss-dot')).not.toBeInTheDocument();
  });

  it('shows hit ratio chip when Get entries exist', () => {
    render(
      <DetailBody
        entries={[
          httpEntry,
          cacheEntry('c1', 'Get', true),
          cacheEntry('c2', 'Get', true),
          cacheEntry('c3', 'Get', true),
          cacheEntry('c4', 'Get', true),
          cacheEntry('c5', 'Get', false),
        ]}
      />,
    );
    const chip = screen.getByTestId('hit-ratio-chip');
    expect(chip).toBeInTheDocument();
    expect(chip).toHaveTextContent('H 4 / M 1 (80%)');
  });

  it('does not show hit ratio chip when no Get entries', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Set')]} />);
    expect(screen.queryByTestId('hit-ratio-chip')).not.toBeInTheDocument();
  });

  it('toggles expanded body on click and shows key', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Get', true)]} />);
    expect(screen.queryByTestId('cache-row-body')).not.toBeInTheDocument();
    const btn = screen.getByTestId('cache-row').querySelector('button')!;
    await user.click(btn);
    const body = screen.getByTestId('cache-row-body');
    expect(body).toBeInTheDocument();
    expect(body).toHaveTextContent('cache-key-c1');
  });
});

// ── Mixed entries ─────────────────────────────────────────────────────────────

describe('RequestDetail — mixed DB + HTTP-out + cache entries', () => {
  it('renders all three sections when all entry types present', () => {
    const entries: EntryDto[] = [
      httpEntry,
      efEntry('ef-1', 'SELECT 1'),
      httpOutEntry('out-1'),
      cacheEntry('c1', 'Get', true),
    ];
    render(<DetailBody entries={entries} />);
    expect(screen.getByTestId('db-section')).toBeInTheDocument();
    expect(screen.getByTestId('http-out-section')).toBeInTheDocument();
    expect(screen.getByTestId('cache-section')).toBeInTheDocument();
  });

  it('shows side panel when only http-out entries present (no db)', () => {
    render(<DetailBody entries={[httpEntry, httpOutEntry('out-1')]} />);
    expect(screen.queryByTestId('db-section')).not.toBeInTheDocument();
    expect(screen.getByTestId('http-out-section')).toBeInTheDocument();
  });

  it('shows side panel when only cache entries present (no db or http-out)', () => {
    render(<DetailBody entries={[httpEntry, cacheEntry('c1', 'Set')]} />);
    expect(screen.queryByTestId('db-section')).not.toBeInTheDocument();
    expect(screen.queryByTestId('http-out-section')).not.toBeInTheDocument();
    expect(screen.getByTestId('cache-section')).toBeInTheDocument();
  });

  it('EF, SQL, HTTP, MEM, DIST source badges all render with correct labels', () => {
    const entries: EntryDto[] = [
      httpEntry,
      efEntry('ef-1', 'SELECT 1'),
      sqlEntry('sql-1', 'SELECT 2'),
      httpOutEntry('out-1'),
      cacheEntry('c-mem', 'Get', true, 'memory'),
      cacheEntry('c-dist', 'Get', false, 'distributed'),
    ];
    render(<DetailBody entries={entries} />);
    const dbBadges = screen.getAllByTestId('db-source-badge').map((b) => b.textContent);
    expect(dbBadges).toContain('EF');
    expect(dbBadges).toContain('SQL');
    expect(screen.getByTestId('http-source-badge')).toHaveTextContent('HTTP');
    const cacheBadges = screen.getAllByTestId('cache-source-badge').map((b) => b.textContent);
    expect(cacheBadges).toContain('MEM');
    expect(cacheBadges).toContain('DIST');
  });
});

// ── Exception section ─────────────────────────────────────────────────────────

function exceptionEntry(
  id: string,
  handled = true,
  overrides: Partial<ExceptionEntryContent> = {},
): EntryDto {
  const content: ExceptionEntryContent = {
    exceptionType: 'System.InvalidOperationException',
    message: 'Something went wrong',
    stack: [{ method: 'WebApp.OrdersController.GetOrder', file: 'OrdersController.cs', line: 42 }],
    innerExceptions: [],
    source: 'WebApp',
    hResult: -2146233079,
    handled,
    ...overrides,
  };
  return {
    id,
    type: 'exception',
    timestamp: 1_700_000_000_500,
    requestId: 'req-42',
    durationMs: 0,
    tags: {
      'exception.type': content.exceptionType,
      'exception.handled': handled ? 'true' : 'false',
    },
    content,
  };
}

describe('RequestDetail — Exception section', () => {
  it('renders section when exception entries present', () => {
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1')]} />);
    expect(screen.getByTestId('exception-section')).toBeInTheDocument();
  });

  it('does not render section when no exception entries', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('exception-section')).not.toBeInTheDocument();
  });

  it('renders EXC source badge', () => {
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1')]} />);
    expect(screen.getByTestId('exc-source-badge')).toHaveTextContent('EXC');
  });

  it('renders exception type in each row', () => {
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1')]} />);
    expect(screen.getByTestId('exception-type')).toHaveTextContent(
      'System.InvalidOperationException',
    );
  });

  it('renders handled chip when exception.handled=true', () => {
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1', true)]} />);
    expect(screen.getByTestId('handled-chip')).toHaveTextContent('handled');
    expect(screen.queryByTestId('unhandled-chip')).not.toBeInTheDocument();
  });

  it('renders unhandled chip when exception.handled=false', () => {
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1', false)]} />);
    expect(screen.getByTestId('unhandled-chip')).toHaveTextContent('unhandled');
    expect(screen.queryByTestId('handled-chip')).not.toBeInTheDocument();
  });

  it('toggles expanded body on click and shows stack trace', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1')]} />);
    expect(screen.queryByTestId('exception-row-body')).not.toBeInTheDocument();
    const btn = screen.getByTestId('exception-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('exception-row-body')).toBeInTheDocument();
    expect(screen.getByTestId('exception-stack')).toBeInTheDocument();
  });

  it('shows inner exceptions when expanded', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[
          httpEntry,
          exceptionEntry('exc-1', true, {
            innerExceptions: [
              { type: 'System.ArgumentNullException', message: 'param was null' },
            ],
          }),
        ]}
      />,
    );
    const btn = screen.getByTestId('exception-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('inner-exceptions')).toBeInTheDocument();
    expect(screen.getByTestId('inner-exceptions')).toHaveTextContent(
      'System.ArgumentNullException',
    );
    expect(screen.getByTestId('inner-exceptions')).toHaveTextContent('param was null');
  });

  it('shows side panel when only exception entries present', () => {
    render(<DetailBody entries={[httpEntry, exceptionEntry('exc-1')]} />);
    expect(screen.getByTestId('exception-section')).toBeInTheDocument();
  });
});

// ── Log section ───────────────────────────────────────────────────────────────

function logEntry(
  id: string,
  level: string,
  message = 'Test log message',
  category = 'WebApp.Controllers.OrdersController',
  overrides: Partial<LogEntryContent> = {},
): EntryDto {
  const content: LogEntryContent = {
    level,
    category,
    message,
    eventId: null,
    scopes: [],
    exceptionType: null,
    exceptionMessage: null,
    ...overrides,
  };
  return {
    id,
    type: 'log',
    timestamp: 1_700_000_000_600,
    requestId: 'req-42',
    durationMs: 0,
    tags: { 'log.level': level, 'log.category': category },
    content,
  };
}

describe('RequestDetail — Log section', () => {
  it('renders section when log entries present', () => {
    render(<DetailBody entries={[httpEntry, logEntry('log-1', 'Information')]} />);
    expect(screen.getByTestId('log-section')).toBeInTheDocument();
  });

  it('does not render section when no log entries', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('log-section')).not.toBeInTheDocument();
  });

  it('renders one row per log entry', () => {
    const entries: EntryDto[] = [
      httpEntry,
      logEntry('log-1', 'Information'),
      logEntry('log-2', 'Warning'),
      logEntry('log-3', 'Error'),
    ];
    render(<DetailBody entries={entries} />);
    expect(screen.getAllByTestId('log-row')).toHaveLength(3);
  });

  it('renders LOG source badge on each row', () => {
    render(<DetailBody entries={[httpEntry, logEntry('log-1', 'Information')]} />);
    expect(screen.getByTestId('log-source-badge')).toHaveTextContent('LOG');
  });

  it('renders level badge with correct level text', () => {
    render(<DetailBody entries={[httpEntry, logEntry('log-1', 'Warning')]} />);
    expect(screen.getByTestId('log-level-badge')).toHaveTextContent('Warning');
  });

  it('Warn+ filter hides Information entries', async () => {
    const user = userEvent.setup();
    const entries: EntryDto[] = [
      httpEntry,
      logEntry('log-1', 'Information'),
      logEntry('log-2', 'Warning'),
      logEntry('log-3', 'Error'),
    ];
    render(<DetailBody entries={entries} />);
    expect(screen.getAllByTestId('log-row')).toHaveLength(3);

    await user.click(screen.getByTestId('log-filter-warn+'));
    expect(screen.getAllByTestId('log-row')).toHaveLength(2);
  });

  it('Error+ filter shows only Error and Critical entries', async () => {
    const user = userEvent.setup();
    const entries: EntryDto[] = [
      httpEntry,
      logEntry('log-1', 'Information'),
      logEntry('log-2', 'Warning'),
      logEntry('log-3', 'Error'),
    ];
    render(<DetailBody entries={entries} />);

    await user.click(screen.getByTestId('log-filter-error+'));
    expect(screen.getAllByTestId('log-row')).toHaveLength(1);
  });

  it('All filter restores all entries', async () => {
    const user = userEvent.setup();
    const entries: EntryDto[] = [
      httpEntry,
      logEntry('log-1', 'Information'),
      logEntry('log-2', 'Warning'),
    ];
    render(<DetailBody entries={entries} />);
    await user.click(screen.getByTestId('log-filter-warn+'));
    expect(screen.getAllByTestId('log-row')).toHaveLength(1);
    await user.click(screen.getByTestId('log-filter-all'));
    expect(screen.getAllByTestId('log-row')).toHaveLength(2);
  });

  it('expanded row shows category, message, and scopes', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[
          httpEntry,
          logEntry('log-1', 'Information', 'order loaded', 'WebApp.OrdersController', {
            scopes: ['RequestId:abc', 'OrderId:7'],
          }),
        ]}
      />,
    );
    const btn = screen.getByTestId('log-row').querySelector('button')!;
    await user.click(btn);
    const body = screen.getByTestId('log-row-body');
    expect(body).toHaveTextContent('order loaded');
    expect(body).toHaveTextContent('RequestId:abc');
    expect(body).toHaveTextContent('OrderId:7');
  });

  it('expanded row shows event id when present', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[
          httpEntry,
          logEntry('log-1', 'Information', 'msg', 'Cat', {
            eventId: { id: 1001, name: 'OrderLoaded' },
          }),
        ]}
      />,
    );
    const btn = screen.getByTestId('log-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('log-row-body')).toHaveTextContent('1001');
    expect(screen.getByTestId('log-row-body')).toHaveTextContent('OrderLoaded');
  });

  it('expanded row shows exception type/message when log carried an exception', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[
          httpEntry,
          logEntry('log-1', 'Error', 'failed', 'Cat', {
            exceptionType: 'System.Exception',
            exceptionMessage: 'bang',
          }),
        ]}
      />,
    );
    const btn = screen.getByTestId('log-row').querySelector('button')!;
    await user.click(btn);
    const body = screen.getByTestId('log-row-body');
    expect(body).toHaveTextContent('System.Exception');
    expect(body).toHaveTextContent('bang');
  });
});

// ── Dump section ──────────────────────────────────────────────────────────────

function dumpEntry(
  id: string,
  overrides: Partial<DumpEntryContent> = {},
): EntryDto {
  const content: DumpEntryContent = {
    label: 'my-dump',
    json: '{"id":1,"name":"Alice"}',
    jsonTruncated: false,
    valueType: 'WebApp.Models.Order',
    callerFile: '/src/WebApp/OrdersController.cs',
    callerLine: 55,
    callerMember: 'Get',
    ...overrides,
  };
  return {
    id,
    type: 'dump',
    timestamp: 1_700_000_000_700,
    requestId: 'req-42',
    durationMs: 0,
    tags: { dump: 'true', 'dump.type': content.valueType },
    content,
  };
}

describe('RequestDetail — Dump section', () => {
  it('renders section when dump entries present', () => {
    render(<DetailBody entries={[httpEntry, dumpEntry('dump-1')]} />);
    expect(screen.getByTestId('dump-section')).toBeInTheDocument();
  });

  it('does not render section when no dump entries', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('dump-section')).not.toBeInTheDocument();
  });

  it('renders DUMP source badge', () => {
    render(<DetailBody entries={[httpEntry, dumpEntry('dump-1')]} />);
    expect(screen.getByTestId('dump-source-badge')).toHaveTextContent('DUMP');
  });

  it('renders label when present', () => {
    render(<DetailBody entries={[httpEntry, dumpEntry('dump-1', { label: 'after-lookup' })]} />);
    expect(screen.getByTestId('dump-label')).toHaveTextContent('after-lookup');
    expect(screen.queryByTestId('dump-no-label')).not.toBeInTheDocument();
  });

  it('renders (no label) placeholder when label absent', () => {
    render(<DetailBody entries={[httpEntry, dumpEntry('dump-1', { label: null })]} />);
    expect(screen.getByTestId('dump-no-label')).toHaveTextContent('(no label)');
    expect(screen.queryByTestId('dump-label')).not.toBeInTheDocument();
  });

  it('expanded row shows pretty-printed JSON', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[httpEntry, dumpEntry('dump-1', { json: '{"id":1,"name":"Alice"}' })]}
      />,
    );
    const btn = screen.getByTestId('dump-row').querySelector('button')!;
    await user.click(btn);
    const body = screen.getByTestId('dump-row-body');
    expect(body).toHaveTextContent('"id"');
    expect(body).toHaveTextContent('"Alice"');
  });

  it('shows truncation marker when jsonTruncated is true', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[httpEntry, dumpEntry('dump-1', { jsonTruncated: true })]}
      />,
    );
    const btn = screen.getByTestId('dump-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('dump-truncation-marker')).toBeInTheDocument();
  });

  it('does not show truncation marker when jsonTruncated is false', async () => {
    const user = userEvent.setup();
    render(
      <DetailBody
        entries={[httpEntry, dumpEntry('dump-1', { jsonTruncated: false })]}
      />,
    );
    const btn = screen.getByTestId('dump-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.queryByTestId('dump-truncation-marker')).not.toBeInTheDocument();
  });

  it('shows side panel when only dump entries present', () => {
    render(<DetailBody entries={[httpEntry, dumpEntry('dump-1')]} />);
    expect(screen.getByTestId('dump-section')).toBeInTheDocument();
  });
});

// ── Job helpers ───────────────────────────────────────────────────────────────

function jobEnqueueEntry(
  id: string,
  overrides: Partial<JobEnqueueEntryContent> = {},
): EntryDto {
  const content: JobEnqueueEntryContent = {
    jobId: `hangfire-${id}`,
    jobType: 'WebApp.Jobs.SendEmailJob',
    methodName: 'Execute',
    arguments: [{ name: 'email', type: 'System.String', value: '"alice@example.com"' }],
    queue: 'default',
    state: 'Enqueued',
    errorType: null,
    errorMessage: null,
    ...overrides,
  };
  return {
    id,
    type: 'job-enqueue',
    timestamp: 1_700_000_000_800,
    requestId: 'req-42',
    durationMs: 0.5,
    tags: {
      'job.id': content.jobId,
      'job.type': content.jobType ?? '',
      'job.method': content.methodName,
      'job.enqueue': 'true',
    },
    content,
  };
}

function jobExecutionEntry(
  id: string,
  state: 'Succeeded' | 'Failed' = 'Succeeded',
  overrides: Partial<JobExecutionEntryContent> = {},
): EntryDto {
  const content: JobExecutionEntryContent = {
    jobId: `hangfire-${id}`,
    jobType: 'WebApp.Jobs.SendEmailJob',
    methodName: 'Execute',
    enqueueRequestId: 'req-42',
    state,
    errorType: state === 'Failed' ? 'System.InvalidOperationException' : null,
    errorMessage: state === 'Failed' ? 'Email failed' : null,
    ...overrides,
  };
  return {
    id,
    type: 'job-execution',
    timestamp: 1_700_000_001_000,
    requestId: 'req-42',
    durationMs: 120.5,
    tags: {
      'job.id': content.jobId,
      'job.type': content.jobType ?? '',
      'job.method': content.methodName,
      'job.state': state,
      'job.execute': 'true',
    },
    content,
  };
}

// ── Section ordering ──────────────────────────────────────────────────────────

describe('RequestDetail — section ordering', () => {
  it('exception section appears before db section', () => {
    const entries: EntryDto[] = [
      httpEntry,
      exceptionEntry('exc-1'),
      efEntry('ef-1', 'SELECT 1'),
    ];
    render(<DetailBody entries={entries} />);
    const exc = screen.getByTestId('exception-section');
    const db = screen.getByTestId('db-section');
    expect(exc.compareDocumentPosition(db) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('log section appears after cache section', () => {
    const entries: EntryDto[] = [
      httpEntry,
      cacheEntry('c1', 'Get', true),
      logEntry('log-1', 'Information'),
    ];
    render(<DetailBody entries={entries} />);
    const cache = screen.getByTestId('cache-section');
    const log = screen.getByTestId('log-section');
    expect(cache.compareDocumentPosition(log) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('dump section appears after log section', () => {
    const entries: EntryDto[] = [
      httpEntry,
      logEntry('log-1', 'Information'),
      dumpEntry('dump-1'),
    ];
    render(<DetailBody entries={entries} />);
    const log = screen.getByTestId('log-section');
    const dump = screen.getByTestId('dump-section');
    expect(log.compareDocumentPosition(dump) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('all six sections render in correct order with all entry types', () => {
    const entries: EntryDto[] = [
      httpEntry,
      exceptionEntry('exc-1'),
      efEntry('ef-1', 'SELECT 1'),
      httpOutEntry('out-1'),
      cacheEntry('c1', 'Get', true),
      logEntry('log-1', 'Information'),
      dumpEntry('dump-1'),
    ];
    render(<DetailBody entries={entries} />);
    const sections = ['exception-section', 'db-section', 'http-out-section', 'cache-section', 'log-section', 'dump-section']
      .map((id) => screen.getByTestId(id));

    for (let i = 0; i < sections.length - 1; i++) {
      expect(
        sections[i].compareDocumentPosition(sections[i + 1]) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
    }
  });

  it('all seven sections render in correct order including jobs', () => {
    const entries: EntryDto[] = [
      httpEntry,
      exceptionEntry('exc-1'),
      efEntry('ef-1', 'SELECT 1'),
      httpOutEntry('out-1'),
      cacheEntry('c1', 'Get', true),
      logEntry('log-1', 'Information'),
      dumpEntry('dump-1'),
      jobEnqueueEntry('enq-1'),
    ];
    render(<DetailBody entries={entries} />);
    const sections = [
      'exception-section', 'db-section', 'http-out-section',
      'cache-section', 'log-section', 'dump-section', 'job-section',
    ].map((id) => screen.getByTestId(id));

    for (let i = 0; i < sections.length - 1; i++) {
      expect(
        sections[i].compareDocumentPosition(sections[i + 1]) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
    }
  });

  it('EXC/LOG/DUMP source badges all render with correct labels', () => {
    const entries: EntryDto[] = [
      httpEntry,
      exceptionEntry('exc-1'),
      logEntry('log-1', 'Information'),
      dumpEntry('dump-1'),
    ];
    render(<DetailBody entries={entries} />);
    expect(screen.getByTestId('exc-source-badge')).toHaveTextContent('EXC');
    expect(screen.getByTestId('log-source-badge')).toHaveTextContent('LOG');
    expect(screen.getByTestId('dump-source-badge')).toHaveTextContent('DUMP');
  });
});

// ── Jobs section ──────────────────────────────────────────────────────────────

describe('RequestDetail — Jobs section', () => {
  it('renders section when job-enqueue entries present', () => {
    render(<DetailBody entries={[httpEntry, jobEnqueueEntry('enq-1')]} />);
    expect(screen.getByTestId('job-section')).toBeInTheDocument();
  });

  it('renders section when job-execution entries present', () => {
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1')]} />);
    expect(screen.getByTestId('job-section')).toBeInTheDocument();
  });

  it('does not render section when no job entries', () => {
    render(<DetailBody entries={[httpEntry]} />);
    expect(screen.queryByTestId('job-section')).not.toBeInTheDocument();
  });

  it('renders ENQ badge for enqueue rows', () => {
    render(<DetailBody entries={[httpEntry, jobEnqueueEntry('enq-1')]} />);
    expect(screen.getByTestId('job-enq-badge')).toHaveTextContent('ENQ');
  });

  it('renders EXEC badge for execution rows', () => {
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1')]} />);
    expect(screen.getByTestId('job-exec-badge')).toHaveTextContent('EXEC');
  });

  it('renders Succeeded state badge on succeeded execution row', () => {
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1', 'Succeeded')]} />);
    expect(screen.getByTestId('job-execution-state')).toHaveTextContent('Succeeded');
  });

  it('renders Failed state badge on failed execution row', () => {
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1', 'Failed')]} />);
    expect(screen.getByTestId('job-execution-state')).toHaveTextContent('Failed');
  });

  it('renders enqueue state badge on enqueue row', () => {
    render(<DetailBody entries={[httpEntry, jobEnqueueEntry('enq-1')]} />);
    expect(screen.getByTestId('job-enqueue-state')).toHaveTextContent('Enqueued');
  });

  it('shows queue chip on enqueue row', () => {
    render(<DetailBody entries={[httpEntry, jobEnqueueEntry('enq-1', { queue: 'critical' })]} />);
    expect(screen.getByTestId('job-queue-chip')).toHaveTextContent('critical');
  });

  it('shows failed chip in header when execution entry failed', () => {
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1', 'Failed')]} />);
    expect(screen.getByTestId('job-failed-chip')).toHaveTextContent('1 failed');
  });

  it('does not show failed chip when all executions succeeded', () => {
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1', 'Succeeded')]} />);
    expect(screen.queryByTestId('job-failed-chip')).not.toBeInTheDocument();
  });

  it('renders both enqueue and execution rows together', () => {
    render(
      <DetailBody
        entries={[httpEntry, jobEnqueueEntry('enq-1'), jobExecutionEntry('exec-1')]}
      />,
    );
    expect(screen.getByTestId('job-enqueue-row')).toBeInTheDocument();
    expect(screen.getByTestId('job-execution-row')).toBeInTheDocument();
  });

  it('toggles expanded enqueue row and shows job id and arguments', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, jobEnqueueEntry('enq-1')]} />);
    expect(screen.queryByTestId('job-enqueue-row-body')).not.toBeInTheDocument();
    const btn = screen.getByTestId('job-enqueue-row').querySelector('button')!;
    await user.click(btn);
    const body = screen.getByTestId('job-enqueue-row-body');
    expect(body).toBeInTheDocument();
    expect(body).toHaveTextContent('hangfire-enq-1');
    expect(body).toHaveTextContent('email');
  });

  it('toggles expanded execution row and shows error info when failed', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1', 'Failed')]} />);
    const btn = screen.getByTestId('job-execution-row').querySelector('button')!;
    await user.click(btn);
    const body = screen.getByTestId('job-execution-row-body');
    expect(body).toBeInTheDocument();
    expect(body).toHaveTextContent('System.InvalidOperationException');
    expect(body).toHaveTextContent('Email failed');
  });

  it('expanded execution row shows enqueue request link', async () => {
    const user = userEvent.setup();
    render(<DetailBody entries={[httpEntry, jobExecutionEntry('exec-1')]} />);
    const btn = screen.getByTestId('job-execution-row').querySelector('button')!;
    await user.click(btn);
    expect(screen.getByTestId('enqueue-request-link')).toBeInTheDocument();
    expect(screen.getByTestId('enqueue-request-link')).toHaveTextContent('req-42');
  });

  it('job section appears after dump section', () => {
    const entries: EntryDto[] = [
      httpEntry,
      dumpEntry('dump-1'),
      jobEnqueueEntry('enq-1'),
    ];
    render(<DetailBody entries={entries} />);
    const dump = screen.getByTestId('dump-section');
    const job = screen.getByTestId('job-section');
    expect(dump.compareDocumentPosition(job) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('shows side panel when only job entries present', () => {
    render(<DetailBody entries={[httpEntry, jobEnqueueEntry('enq-1')]} />);
    expect(screen.getByTestId('job-section')).toBeInTheDocument();
  });
});
