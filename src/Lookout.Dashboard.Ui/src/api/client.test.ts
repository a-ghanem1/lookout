import { afterEach, describe, expect, it, vi } from 'vitest';
import { deleteAllEntries } from './client';

describe('deleteAllEntries', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    // Clear cookies set during the test
    document.cookie = '__lookout-csrf=; max-age=0';
  });

  it('attaches X-Lookout-Csrf-Token from the __lookout-csrf cookie', async () => {
    document.cookie = '__lookout-csrf=abc123testtoken';

    let capturedInit: RequestInit | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: unknown, init?: RequestInit) => {
        capturedInit = init;
        return new Response(JSON.stringify({ deleted: 3 }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as unknown as typeof fetch,
    );

    const result = await deleteAllEntries();

    expect(result.deleted).toBe(3);
    const headers = new Headers(capturedInit?.headers);
    expect(headers.get('x-lookout-csrf-token')).toBe('abc123testtoken');
  });

  it('sends an empty X-Lookout-Csrf-Token when the cookie is absent', async () => {
    // Ensure no csrf cookie is set
    document.cookie = '__lookout-csrf=; max-age=0';

    let capturedInit: RequestInit | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: unknown, init?: RequestInit) => {
        capturedInit = init;
        return new Response(JSON.stringify({ deleted: 0 }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as unknown as typeof fetch,
    );

    await deleteAllEntries();

    const headers = new Headers(capturedInit?.headers);
    expect(headers.get('x-lookout-csrf-token')).toBe('');
  });

  it('includes the Origin header from window.location.origin', async () => {
    let capturedInit: RequestInit | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_url: unknown, init?: RequestInit) => {
        capturedInit = init;
        return new Response(JSON.stringify({ deleted: 0 }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }) as unknown as typeof fetch,
    );

    await deleteAllEntries();

    const headers = new Headers(capturedInit?.headers);
    expect(headers.get('origin')).toBe(window.location.origin);
  });
});
