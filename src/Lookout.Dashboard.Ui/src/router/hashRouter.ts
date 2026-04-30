import { useEffect, useState } from 'react';

export type Route =
  | { name: 'list' }
  | { name: 'detail'; id: string }
  | { name: 'job'; id: string }
  | { name: 'queries' }
  | { name: 'query-detail'; id: string }
  | { name: 'exceptions' }
  | { name: 'exception-detail'; id: string }
  | { name: 'logs' }
  | { name: 'cache' }
  | { name: 'http-clients' }
  | { name: 'http-client-detail'; id: string }
  | { name: 'jobs' }
  | { name: 'dump' }
  | { name: 'not-found' };

export function parseHash(hash: string): Route {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash;
  const clean = raw.startsWith('/') ? raw.slice(1) : raw;

  // Root or /requests (no id) → list
  if (clean === '' || clean === '/' || clean === 'requests') return { name: 'list' };

  const parts = clean.split('/').filter(Boolean);

  if (parts[0] === 'requests') {
    if (parts.length === 2 && parts[1]) return { name: 'detail', id: decodeURIComponent(parts[1]) };
    return { name: 'not-found' };
  }
  if (parts[0] === 'queries') {
    if (parts.length === 1) return { name: 'queries' };
    if (parts.length === 2 && parts[1]) return { name: 'query-detail', id: decodeURIComponent(parts[1]) };
  }
  if (parts[0] === 'exceptions') {
    if (parts.length === 1) return { name: 'exceptions' };
    if (parts.length === 2 && parts[1]) return { name: 'exception-detail', id: decodeURIComponent(parts[1]) };
  }
  if (parts[0] === 'logs' && parts.length === 1) return { name: 'logs' };
  if (parts[0] === 'cache' && parts.length === 1) return { name: 'cache' };
  if (parts[0] === 'http-clients') {
    if (parts.length === 1) return { name: 'http-clients' };
    if (parts.length === 2 && parts[1]) return { name: 'http-client-detail', id: decodeURIComponent(parts[1]) };
  }
  if (parts[0] === 'jobs') {
    if (parts.length === 1) return { name: 'jobs' };
    if (parts.length === 2 && parts[1]) return { name: 'job', id: decodeURIComponent(parts[1]) };
  }
  if (parts[0] === 'dump' && parts.length === 1) return { name: 'dump' };

  return { name: 'not-found' };
}

export function useHashRoute(): Route {
  const [route, setRoute] = useState<Route>(() => parseHash(window.location.hash));
  useEffect(() => {
    const handler = () => setRoute(parseHash(window.location.hash));
    window.addEventListener('hashchange', handler);
    return () => window.removeEventListener('hashchange', handler);
  }, []);
  return route;
}

export function href(route: Route): string {
  switch (route.name) {
    case 'list':
      return '#/requests';
    case 'detail':
      return `#/requests/${encodeURIComponent(route.id)}`;
    case 'job':
      return `#/jobs/${encodeURIComponent(route.id)}`;
    case 'queries':
      return '#/queries';
    case 'query-detail':
      return `#/queries/${encodeURIComponent(route.id)}`;
    case 'exceptions':
      return '#/exceptions';
    case 'exception-detail':
      return `#/exceptions/${encodeURIComponent(route.id)}`;
    case 'logs':
      return '#/logs';
    case 'cache':
      return '#/cache';
    case 'http-clients':
      return '#/http-clients';
    case 'http-client-detail':
      return `#/http-clients/${encodeURIComponent(route.id)}`;
    case 'jobs':
      return '#/jobs';
    case 'dump':
      return '#/dump';
    case 'not-found':
      return '#/requests';
  }
}
