import { describe, expect, it } from 'vitest';
import { href, parseHash } from './hashRouter';

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

  it('extracts the id from a job hash', () => {
    expect(parseHash('#/jobs/abc-123')).toEqual({ name: 'job', id: 'abc-123' });
  });

  it('decodes job ids', () => {
    expect(parseHash('#/jobs/foo%2Fbar')).toEqual({ name: 'job', id: 'foo/bar' });
  });
});

describe('href', () => {
  it('generates job href', () => {
    expect(href({ name: 'job', id: 'abc-123' })).toBe('#/jobs/abc-123');
  });

  it('encodes special characters in job href', () => {
    expect(href({ name: 'job', id: 'foo/bar' })).toBe('#/jobs/foo%2Fbar');
  });
});
