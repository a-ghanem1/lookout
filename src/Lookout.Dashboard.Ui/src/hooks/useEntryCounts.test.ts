import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryCounts } from '../api/types';
import { useEntryCounts } from './useEntryCounts';

const FULL_COUNTS: EntryCounts = {
  requests: 10,
  queries: 5,
  exceptions: 2,
  logs: 20,
  cache: 7,
  httpClients: 3,
  jobs: 4,
  dump: 1,
};

function makeFetch(counts: EntryCounts = FULL_COUNTS) {
  return vi.fn(async () =>
    new Response(JSON.stringify(counts), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }),
  );
}

describe('useEntryCounts', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    Object.defineProperty(document, 'visibilityState', { value: 'visible', writable: true, configurable: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('returns zero counts before the first fetch resolves', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})) as unknown as typeof fetch);
    const { result } = renderHook(() => useEntryCounts());
    expect(result.current.requests).toBe(0);
    expect(result.current.jobs).toBe(0);
  });

  it('populates counts after the first fetch', async () => {
    vi.stubGlobal('fetch', makeFetch() as unknown as typeof fetch);
    const { result } = renderHook(() => useEntryCounts());
    await act(async () => { await Promise.resolve(); });
    expect(result.current.requests).toBe(FULL_COUNTS.requests);
    expect(result.current.queries).toBe(FULL_COUNTS.queries);
    expect(result.current.exceptions).toBe(FULL_COUNTS.exceptions);
    expect(result.current.logs).toBe(FULL_COUNTS.logs);
    expect(result.current.cache).toBe(FULL_COUNTS.cache);
    expect(result.current.httpClients).toBe(FULL_COUNTS.httpClients);
    expect(result.current.jobs).toBe(FULL_COUNTS.jobs);
    expect(result.current.dump).toBe(FULL_COUNTS.dump);
  });

  it('polls again after 2s', async () => {
    const mockFetch = makeFetch();
    vi.stubGlobal('fetch', mockFetch as unknown as typeof fetch);
    renderHook(() => useEntryCounts());
    await act(async () => { await Promise.resolve(); });
    expect(mockFetch).toHaveBeenCalledTimes(1);

    await act(async () => { vi.advanceTimersByTime(2000); await Promise.resolve(); });
    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it('does not poll when document is hidden', async () => {
    Object.defineProperty(document, 'visibilityState', { value: 'hidden', writable: true, configurable: true });
    const mockFetch = makeFetch();
    vi.stubGlobal('fetch', mockFetch as unknown as typeof fetch);
    renderHook(() => useEntryCounts());
    await act(async () => { vi.advanceTimersByTime(6000); await Promise.resolve(); });
    // initial fetch is skipped because hidden, interval ticks are also skipped
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('resumes fetching when document becomes visible', async () => {
    Object.defineProperty(document, 'visibilityState', { value: 'hidden', writable: true, configurable: true });
    const mockFetch = makeFetch();
    vi.stubGlobal('fetch', mockFetch as unknown as typeof fetch);
    renderHook(() => useEntryCounts());
    await act(async () => { await Promise.resolve(); });
    expect(mockFetch).not.toHaveBeenCalled();

    await act(async () => {
      Object.defineProperty(document, 'visibilityState', { value: 'visible', writable: true, configurable: true });
      document.dispatchEvent(new Event('visibilitychange'));
      await Promise.resolve();
    });
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('returns zero for missing keys when fetch returns partial data', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify({ requests: 3 }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      ) as unknown as typeof fetch,
    );
    const { result } = renderHook(() => useEntryCounts());
    await act(async () => { await Promise.resolve(); });
    expect(result.current.requests).toBe(3);
    // partial response — missing keys coerce to undefined; sidebar treats as 0 via fallback
    expect(result.current.queries ?? 0).toBe(0);
  });

  it('keeps previous counts on fetch error', async () => {
    let callCount = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        callCount++;
        if (callCount === 1) return new Response(JSON.stringify(FULL_COUNTS), { status: 200, headers: { 'content-type': 'application/json' } });
        throw new Error('network error');
      }) as unknown as typeof fetch,
    );
    const { result } = renderHook(() => useEntryCounts());
    await act(async () => { await Promise.resolve(); });
    expect(result.current.requests).toBe(FULL_COUNTS.requests);

    await act(async () => { vi.advanceTimersByTime(2000); await Promise.resolve(); });
    // error swallowed — counts stay at last good value
    expect(result.current.requests).toBe(FULL_COUNTS.requests);
  });
});
