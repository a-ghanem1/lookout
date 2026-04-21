import { useEffect, useState } from 'react';

export type Route =
  | { name: 'list' }
  | { name: 'detail'; id: string }
  | { name: 'not-found' };

export function parseHash(hash: string): Route {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash;
  const clean = raw.startsWith('/') ? raw.slice(1) : raw;
  if (clean === '' || clean === '/') return { name: 'list' };

  const parts = clean.split('/').filter(Boolean);
  if (parts[0] === 'requests' && parts.length === 2 && parts[1]) {
    return { name: 'detail', id: decodeURIComponent(parts[1]) };
  }
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
      return '#/';
    case 'detail':
      return `#/requests/${encodeURIComponent(route.id)}`;
    case 'not-found':
      return '#/';
  }
}
