import { createContext, useContext, useEffect, useState } from 'react';

export type IdePreference = 'none' | 'vscode' | 'rider';

export const IDE_STORAGE_KEY = 'lookout:ide';
export const IDE_CHANGED_EVENT = 'lookout:ide-changed';

export const IdeContext = createContext<IdePreference>('none');

export function readIde(): IdePreference {
  try {
    const v = localStorage.getItem(IDE_STORAGE_KEY);
    if (v === 'vscode' || v === 'rider') return v;
  } catch { /* ignore */ }
  return 'none';
}

export function ideUrl(ide: IdePreference, file: string, line: number | null | undefined): string | null {
  const l = line ?? 1;
  if (ide === 'vscode') return `vscode://file/${file}:${l}`;
  if (ide === 'rider') return `idea://open?file=${encodeURIComponent(file)}&line=${l}`;
  return null;
}

export function useIde(): IdePreference {
  const [ide, setIde] = useState<IdePreference>(readIde);
  useEffect(() => {
    const handler = (e: Event) => setIde((e as CustomEvent<IdePreference>).detail);
    window.addEventListener(IDE_CHANGED_EVENT, handler);
    return () => window.removeEventListener(IDE_CHANGED_EVENT, handler);
  }, []);
  return ide;
}

export function useIdeContext(): IdePreference {
  return useContext(IdeContext);
}
