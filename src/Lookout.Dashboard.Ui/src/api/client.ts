import type {
  CacheByKeyStats,
  CacheSummary,
  EntryCounts,
  EntryDto,
  EntryListQuery,
  EntryListResponse,
  HostInfo,
  LogHistogramBucket,
  SearchResultDto,
} from './types';

/**
 * API URLs are relative to `<base href>`, which the server injects into index.html
 * so the browser resolves them against the mount prefix (default `/lookout/`).
 * No mount path is hard-coded in the bundle.
 */
const API_BASE = 'api/';

function qs(query: EntryListQuery): string {
  const params = new URLSearchParams();
  if (query.type) params.set('type', query.type);
  if (query.method) params.set('method', query.method);
  if (query.status) params.set('status', query.status);
  if (query.path) params.set('path', query.path);
  if (query.q) params.set('q', query.q);
  if (query.before !== undefined) params.set('before', String(query.before));
  if (query.limit !== undefined) params.set('limit', String(query.limit));
  if (query.sort) params.set('sort', query.sort);
  if (query.minDurationMs !== undefined) params.set('min_duration_ms', String(query.minDurationMs));
  if (query.maxDurationMs !== undefined) params.set('max_duration_ms', String(query.maxDurationMs));
  if (query.host) params.set('host', query.host);
  if (query.errorsOnly) params.set('errors_only', 'true');
  if (query.minLevel) params.set('min_level', query.minLevel);
  if (query.handled !== undefined) params.set('handled', String(query.handled));
  if (query.tags) {
    for (const { key, value } of query.tags) {
      params.append('tag', `${key}:${value}`);
    }
  }
  const s = params.toString();
  return s ? `?${s}` : '';
}

export async function listEntries(
  query: EntryListQuery,
  signal?: AbortSignal,
): Promise<EntryListResponse> {
  const resp = await fetch(`${API_BASE}entries${qs(query)}`, { signal });
  if (!resp.ok) throw new Error(`entries: ${resp.status}`);
  return (await resp.json()) as EntryListResponse;
}

export async function getEntry(id: string, signal?: AbortSignal): Promise<EntryDto> {
  const resp = await fetch(`${API_BASE}entries/${encodeURIComponent(id)}`, { signal });
  if (!resp.ok) throw new Error(`entry: ${resp.status}`);
  return (await resp.json()) as EntryDto;
}

export async function getCacheSummary(signal?: AbortSignal): Promise<CacheSummary> {
  const resp = await fetch(`${API_BASE}entries/cache/summary`, { signal });
  if (!resp.ok) throw new Error(`cache/summary: ${resp.status}`);
  return (await resp.json()) as CacheSummary;
}

export async function getEntryCounts(signal?: AbortSignal): Promise<EntryCounts> {
  const resp = await fetch(`${API_BASE}entries/counts`, { signal });
  if (!resp.ok) throw new Error(`counts: ${resp.status}`);
  return (await resp.json()) as EntryCounts;
}

export async function getRequestEntries(
  requestId: string,
  signal?: AbortSignal,
): Promise<EntryDto[]> {
  const resp = await fetch(`${API_BASE}requests/${encodeURIComponent(requestId)}`, { signal });
  if (!resp.ok) throw new Error(`request: ${resp.status}`);
  return (await resp.json()) as EntryDto[];
}

export async function searchEntries(
  q: string,
  limit = 50,
  signal?: AbortSignal,
): Promise<SearchResultDto[]> {
  const params = new URLSearchParams({ q, limit: String(limit) });
  const resp = await fetch(`${API_BASE}search?${params}`, { signal });
  if (!resp.ok) throw new Error(`search: ${resp.status}`);
  return (await resp.json()) as SearchResultDto[];
}

export async function getHostInfo(signal?: AbortSignal): Promise<HostInfo> {
  const resp = await fetch(`${API_BASE}host`, { signal });
  if (!resp.ok) throw new Error(`host: ${resp.status}`);
  return (await resp.json()) as HostInfo;
}

function getCsrfToken(): string {
  const match = document.cookie.match(/(?:^|;\s*)__lookout-csrf=([^;]+)/);
  return match ? match[1] : '';
}

export async function deleteAllEntries(signal?: AbortSignal): Promise<{ deleted: number }> {
  const resp = await fetch(`${API_BASE}entries`, {
    method: 'DELETE',
    headers: {
      Origin: window.location.origin,
      'X-Lookout-Csrf-Token': getCsrfToken(),
    },
    signal,
  });
  if (!resp.ok) throw new Error(`delete: ${resp.status}`);
  return (await resp.json()) as { deleted: number };
}

export async function getLogHistogram(
  bucketCount = 12,
  signal?: AbortSignal,
): Promise<LogHistogramBucket[]> {
  const params = new URLSearchParams({ bucket_count: String(bucketCount) });
  const resp = await fetch(`${API_BASE}entries/logs/histogram?${params}`, { signal });
  if (!resp.ok) throw new Error(`histogram: ${resp.status}`);
  return (await resp.json()) as LogHistogramBucket[];
}

export async function getCacheByKey(
  limit = 10,
  signal?: AbortSignal,
): Promise<CacheByKeyStats[]> {
  const params = new URLSearchParams({ limit: String(limit) });
  const resp = await fetch(`${API_BASE}entries/cache/by-key?${params}`, { signal });
  if (!resp.ok) throw new Error(`cache/by-key: ${resp.status}`);
  return (await resp.json()) as CacheByKeyStats[];
}
