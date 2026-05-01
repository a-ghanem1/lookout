import { useCallback, useEffect, useState } from 'react';
import type { TagFilter } from '../api/types';

function readTagsFromHash(): TagFilter[] {
  const hash = window.location.hash;
  const qIdx = hash.indexOf('?');
  if (qIdx === -1) return [];
  const params = new URLSearchParams(hash.slice(qIdx + 1));
  const tags: TagFilter[] = [];
  for (const tv of params.getAll('tag')) {
    const colon = tv.indexOf(':');
    if (colon < 1) continue;
    tags.push({ key: tv.slice(0, colon), value: tv.slice(colon + 1) });
  }
  return tags;
}

function writeTagsToHash(tags: TagFilter[]): void {
  const hash = window.location.hash;
  const qIdx = hash.indexOf('?');
  const path = qIdx === -1 ? hash : hash.slice(0, qIdx);
  const params = new URLSearchParams(qIdx === -1 ? '' : hash.slice(qIdx + 1));

  params.delete('tag');
  for (const { key, value } of tags) {
    params.append('tag', `${key}:${value}`);
  }

  const qs = params.toString();
  const newHash = qs ? `${path}?${qs}` : path;
  if (newHash !== window.location.hash) {
    window.history.pushState(null, '', newHash);
    window.dispatchEvent(new HashChangeEvent('hashchange'));
  }
}

export function useTagFilter(): {
  activeTags: TagFilter[];
  addTag: (tag: TagFilter) => void;
  removeTag: (key: string, value: string) => void;
  clear: () => void;
} {
  const [activeTags, setActiveTags] = useState<TagFilter[]>(readTagsFromHash);

  useEffect(() => {
    const handler = () => setActiveTags(readTagsFromHash());
    window.addEventListener('hashchange', handler);
    return () => window.removeEventListener('hashchange', handler);
  }, []);

  const addTag = useCallback((tag: TagFilter) => {
    setActiveTags((prev) => {
      const exists = prev.some((t) => t.key === tag.key && t.value === tag.value);
      if (exists) return prev;
      const next = [...prev, tag];
      writeTagsToHash(next);
      return next;
    });
  }, []);

  const removeTag = useCallback((key: string, value: string) => {
    setActiveTags((prev) => {
      const next = prev.filter((t) => !(t.key === key && t.value === value));
      writeTagsToHash(next);
      return next;
    });
  }, []);

  const clear = useCallback(() => {
    setActiveTags([]);
    writeTagsToHash([]);
  }, []);

  return { activeTags, addTag, removeTag, clear };
}
