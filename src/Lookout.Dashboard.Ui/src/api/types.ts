/**
 * Wire types mirroring Lookout.AspNetCore.Api DTOs (camelCase, nulls omitted).
 * Keep in sync with Lookout.Core.Schemas.HttpEntryContent and EntryDto / EntryListResponse.
 */

export type EntryType = 'http' | 'ef' | 'sql' | 'cache' | 'http-out' | 'log' | 'exception' | 'job' | 'dump';

export interface EntryDto {
  id: string;
  type: EntryType | string;
  timestamp: number; // unix ms
  requestId?: string;
  durationMs?: number;
  tags: Record<string, string>;
  content: unknown;
}

export interface HttpEntryContent {
  method: string;
  path: string;
  queryString: string;
  statusCode: number;
  durationMs: number;
  requestHeaders: Record<string, string>;
  responseHeaders: Record<string, string>;
  requestBody?: string;
  responseBody?: string;
  user?: string;
}

export type EfCommandType = 'Query' | 'NonQuery' | 'Reader' | 'Scalar';

export interface EfParameter {
  name: string;
  value?: string | null;
  dbType?: string | null;
}

export interface EfStackFrame {
  method: string;
  file?: string | null;
  line?: number | null;
}

export interface EfEntryContent {
  commandText: string;
  parameters: EfParameter[];
  durationMs: number;
  rowsAffected?: number | null;
  dbContextType?: string | null;
  commandType: EfCommandType;
  stack: EfStackFrame[];
}

/** Raw ADO.NET / Dapper entry content — mirrors EfEntryContent minus dbContextType and the typed commandType. */
export interface SqlEntryContent {
  commandText: string;
  parameters: EfParameter[];
  durationMs: number;
  rowsAffected?: number | null;
  commandType?: string | null;
  stack: EfStackFrame[];
}

export interface OutboundHttpEntryContent {
  method: string;
  url: string;
  statusCode?: number | null;
  durationMs: number;
  requestHeaders: Record<string, string>;
  responseHeaders: Record<string, string>;
  requestBody?: string | null;
  responseBody?: string | null;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface CacheEntryContent {
  operation: string;
  key: string;
  hit?: boolean | null;
  durationMs: number;
  valueType?: string | null;
  valueBytes?: number | null;
}

export interface EntryListResponse {
  entries: EntryDto[];
  nextBefore?: number | null;
}

export interface EntryListQuery {
  type?: string;
  method?: string;
  status?: string;
  path?: string;
  q?: string;
  before?: number;
  limit?: number;
}
