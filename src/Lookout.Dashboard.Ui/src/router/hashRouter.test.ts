import { describe, expect, it } from 'vitest';
import { parseHash } from './hashRouter';

describe('parseHash', () => {
  it('maps the root hashes to the list route', () => {
    expect(parseHash('')).toEqual({ name: 'list' });
    expect(parseHash('#')).toEqual({ name: 'list' });
    expect(parseHash('#/')).toEqual({ name: 'list' });
  });

  it('extracts the id from a request detail hash', () => {
    expect(parseHash('#/requests/abc')).toEqual({ name: 'detail', id: 'abc' });
  });

  it('decodes ids', () => {
    expect(parseHash('#/requests/foo%2Fbar')).toEqual({ name: 'detail', id: 'foo/bar' });
  });

  it('returns not-found for unknown hashes', () => {
    expect(parseHash('#/unknown')).toEqual({ name: 'not-found' });
  });
});
