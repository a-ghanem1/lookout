import { useCallback, useEffect, useRef, useState } from 'react';

interface UseListKeyboardNavOptions {
  count: number;
  onEnter?: (index: number) => void;
  onEsc?: () => void;
  inline?: boolean; // true = Enter expands/collapses instead of navigating
}

export function useListKeyboardNav({
  count,
  onEnter,
  onEsc,
  inline = false,
}: UseListKeyboardNavOptions) {
  const [focusedIndex, setFocusedIndex] = useState<number>(-1);
  const ggPendingRef = useRef(false);
  const ggTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const isInputFocused = () => {
      const el = document.activeElement;
      if (!el) return false;
      const tag = el.tagName.toLowerCase();
      return tag === 'input' || tag === 'textarea' || tag === 'select' || (el as HTMLElement).isContentEditable;
    };

    const handler = (e: KeyboardEvent) => {
      // Bindings are no-ops when an input is focused (except Esc)
      if (isInputFocused() && e.key !== 'Escape') return;
      if (count === 0) return;

      switch (e.key) {
        case 'j':
        case 'ArrowDown':
          e.preventDefault();
          setFocusedIndex((i) => Math.min(i + 1, count - 1));
          ggPendingRef.current = false;
          break;
        case 'k':
        case 'ArrowUp':
          e.preventDefault();
          setFocusedIndex((i) => Math.max(i - 1, 0));
          ggPendingRef.current = false;
          break;
        case 'Enter':
          if (focusedIndex >= 0 && focusedIndex < count) {
            e.preventDefault();
            onEnter?.(focusedIndex);
          }
          ggPendingRef.current = false;
          break;
        case 'Escape':
          onEsc?.();
          ggPendingRef.current = false;
          break;
        case 'G':
          e.preventDefault();
          setFocusedIndex(count - 1);
          ggPendingRef.current = false;
          break;
        case 'g':
          if (ggPendingRef.current) {
            // Second g — go to top
            e.preventDefault();
            setFocusedIndex(0);
            ggPendingRef.current = false;
            if (ggTimerRef.current) clearTimeout(ggTimerRef.current);
          } else {
            ggPendingRef.current = true;
            ggTimerRef.current = setTimeout(() => {
              ggPendingRef.current = false;
            }, 500);
          }
          break;
      }
    };

    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [count, focusedIndex, onEnter, onEsc, inline]);

  const resetFocus = useCallback(() => setFocusedIndex(-1), []);

  return { focusedIndex, setFocusedIndex, resetFocus };
}
