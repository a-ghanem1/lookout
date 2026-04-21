import type { EntryDto, EntryListQuery, EntryListResponse } from './types';

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

export async function getRequestEntries(
  requestId: string,
  signal?: AbortSignal,
): Promise<EntryDto[]> {
  const resp = await fetch(`${API_BASE}requests/${encodeURIComponent(requestId)}`, { signal });
  if (!resp.ok) throw new Error(`request: ${resp.status}`);
  return (await resp.json()) as EntryDto[];
}
