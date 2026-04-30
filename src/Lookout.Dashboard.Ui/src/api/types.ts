/**
 * Wire types mirroring Lookout.AspNetCore.Api DTOs (camelCase, nulls omitted).
 * Keep in sync with Lookout.Core.Schemas.HttpEntryContent and EntryDto / EntryListResponse.
 */

export type EntryType = 'http' | 'ef' | 'sql' | 'cache' | 'http-out' | 'log' | 'exception' | 'job' | 'job-enqueue' | 'job-execution' | 'dump';

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

export interface InnerException {
  type: string;
  message: string;
}

export interface ExceptionEntryContent {
  exceptionType: string;
  message: string;
  stack: EfStackFrame[];
  innerExceptions: InnerException[];
  source?: string | null;
  hResult?: number | null;
  handled: boolean;
}

export interface LogEventId {
  id: number;
  name?: string | null;
}

export interface LogEntryContent {
  level: string;
  category: string;
  message: string;
  eventId?: LogEventId | null;
  scopes: string[];
  exceptionType?: string | null;
  exceptionMessage?: string | null;
}

export interface DumpEntryContent {
  label?: string | null;
  json: string;
  jsonTruncated: boolean;
  valueType: string;
  callerFile: string;
  callerLine: number;
  callerMember: string;
}

export interface JobArgument {
  name: string;
  type: string;
  value: string;
}

export interface JobEnqueueEntryContent {
  jobId: string;
  jobType?: string | null;
  methodName: string;
  arguments: JobArgument[];
  queue: string;
  state: string;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface JobExecutionEntryContent {
  jobId: string;
  jobType?: string | null;
  methodName: string;
  enqueueRequestId?: string | null;
  state: string;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface EntryListResponse {
  entries: EntryDto[];
  nextBefore?: number | null;
}

export interface TagFilter {
  key: string;
  value: string;
}

export interface EntryListQuery {
  type?: string;
  method?: string;
  status?: string;
  path?: string;
  q?: string;
  before?: number;
  limit?: number;
  sort?: 'duration' | 'timestamp';
  minDurationMs?: number;
  maxDurationMs?: number;
  host?: string;
  errorsOnly?: boolean;
  minLevel?: string;
  handled?: boolean;
  tags?: TagFilter[];
}

export interface CacheSummary {
  hits: number;
  misses: number;
  sets: number;
  removes: number;
  hitRatio: number;
}

export interface EntryCounts {
  requests: number;
  queries: number;
  exceptions: number;
  logs: number;
  cache: number;
  httpClients: number;
  jobs: number;
  dump: number;
}

export interface SearchResultDto {
  id: string;
  type: string;
  timestamp: number;
  requestId?: string | null;
  snippet: string;
}

export interface HostInfo {
  os: 'windows' | 'macos' | 'linux';
}

export interface LogHistogramBucket {
  from: number;
  to: number;
  byLevel: {
    trace: number;
    debug: number;
    information: number;
    warning: number;
    error: number;
    critical: number;
  };
}

export interface CacheByKeyStats {
  key: string;
  hits: number;
  misses: number;
  sets: number;
  hitRatio: number;
}
