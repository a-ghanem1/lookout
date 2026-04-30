import { describe, expect, it } from 'vitest';
import { href, parseHash } from './hashRouter';

describe('parseHash', () => {
  it('maps the root hashes to the list route', () => {
    expect(parseHash('')).toEqual({ name: 'list' });
    expect(parseHash('#')).toEqual({ name: 'list' });
    expect(parseHash('#/')).toEqual({ name: 'list' });
    expect(parseHash('#/requests')).toEqual({ name: 'list' });
  });

  it('extracts the id from a request detail hash', () => {
    expect(parseHash('#/requests/abc')).toEqual({ name: 'detail', id: 'abc' });
  });

  it('decodes ids', () => {
    expect(parseHash('#/requests/foo%2Fbar')).toEqual({ name: 'detail', id: 'foo/bar' });
  });

  it('extracts the id from a job detail hash', () => {
    expect(parseHash('#/jobs/abc-123')).toEqual({ name: 'job', id: 'abc-123' });
  });

  it('decodes job ids', () => {
    expect(parseHash('#/jobs/foo%2Fbar')).toEqual({ name: 'job', id: 'foo/bar' });
  });

  it('maps #/jobs (no id) to jobs list', () => {
    expect(parseHash('#/jobs')).toEqual({ name: 'jobs' });
  });

  it('maps #/queries to queries list', () => {
    expect(parseHash('#/queries')).toEqual({ name: 'queries' });
  });

  it('maps #/queries/:id to query-detail', () => {
    expect(parseHash('#/queries/abc')).toEqual({ name: 'query-detail', id: 'abc' });
  });

  it('maps #/exceptions to exceptions list', () => {
    expect(parseHash('#/exceptions')).toEqual({ name: 'exceptions' });
  });

  it('maps #/exceptions/:id to exception-detail', () => {
    expect(parseHash('#/exceptions/xyz')).toEqual({ name: 'exception-detail', id: 'xyz' });
  });

  it('maps #/logs to logs list', () => {
    expect(parseHash('#/logs')).toEqual({ name: 'logs' });
  });

  it('maps #/cache to cache list', () => {
    expect(parseHash('#/cache')).toEqual({ name: 'cache' });
  });

  it('maps #/http-clients to http-clients list', () => {
    expect(parseHash('#/http-clients')).toEqual({ name: 'http-clients' });
  });

  it('maps #/http-clients/:id to http-client-detail', () => {
    expect(parseHash('#/http-clients/eid')).toEqual({ name: 'http-client-detail', id: 'eid' });
  });

  it('maps #/dump to dump list', () => {
    expect(parseHash('#/dump')).toEqual({ name: 'dump' });
  });

  it('returns not-found for unknown hashes', () => {
    expect(parseHash('#/unknown')).toEqual({ name: 'not-found' });
  });
});

describe('href', () => {
  it('generates list href', () => {
    expect(href({ name: 'list' })).toBe('#/requests');
  });

  it('generates detail href', () => {
    expect(href({ name: 'detail', id: 'abc' })).toBe('#/requests/abc');
  });

  it('generates job href', () => {
    expect(href({ name: 'job', id: 'abc-123' })).toBe('#/jobs/abc-123');
  });

  it('encodes special characters in job href', () => {
    expect(href({ name: 'job', id: 'foo/bar' })).toBe('#/jobs/foo%2Fbar');
  });

  it('generates queries href', () => {
    expect(href({ name: 'queries' })).toBe('#/queries');
  });

  it('generates query-detail href', () => {
    expect(href({ name: 'query-detail', id: 'qid' })).toBe('#/queries/qid');
  });

  it('generates exceptions href', () => {
    expect(href({ name: 'exceptions' })).toBe('#/exceptions');
  });

  it('generates exception-detail href', () => {
    expect(href({ name: 'exception-detail', id: 'eid' })).toBe('#/exceptions/eid');
  });

  it('generates logs href', () => {
    expect(href({ name: 'logs' })).toBe('#/logs');
  });

  it('generates cache href', () => {
    expect(href({ name: 'cache' })).toBe('#/cache');
  });

  it('generates http-clients href', () => {
    expect(href({ name: 'http-clients' })).toBe('#/http-clients');
  });

  it('generates http-client-detail href', () => {
    expect(href({ name: 'http-client-detail', id: 'hid' })).toBe('#/http-clients/hid');
  });

  it('generates jobs href', () => {
    expect(href({ name: 'jobs' })).toBe('#/jobs');
  });

  it('generates dump href', () => {
    expect(href({ name: 'dump' })).toBe('#/dump');
  });

  it('generates not-found href as requests', () => {
    expect(href({ name: 'not-found' })).toBe('#/requests');
  });
});
