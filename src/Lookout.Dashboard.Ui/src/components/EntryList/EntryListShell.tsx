import { useRef, type ReactNode } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { EmptyState } from './EmptyState';
import { ErrorState } from './ErrorState';
import { LoadingState } from './LoadingState';
import styles from './EntryListShell.module.css';

interface EntryListShellProps<T> {
  title: string;
  total: number;
  loading: boolean;
  error?: Error;
  items: T[];
  estimatedRowHeight?: number;
  emptyMessage?: string;
  filterSlot?: ReactNode;
  renderRow: (item: T, index: number) => ReactNode;
  onRetry?: () => void;
}

export function EntryListShell<T>({
  title,
  total,
  loading,
  error,
  items,
  estimatedRowHeight = 40,
  emptyMessage,
  filterSlot,
  renderRow,
  onRetry,
}: EntryListShellProps<T>) {
  const scrollRef = useRef<HTMLDivElement>(null);

  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => estimatedRowHeight,
    overscan: 10,
  });

  const showLoading = loading && items.length === 0;
  const showError = !loading && !!error && items.length === 0;
  const showEmpty = !loading && !error && items.length === 0;

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <div className={styles.headerRow}>
          <h1 className={styles.title}>{title}</h1>
          <span className={styles.totalCount} data-testid="entry-list-total">
            {total.toLocaleString()}
          </span>
        </div>
        {filterSlot && <div className={styles.filters}>{filterSlot}</div>}
      </header>

      <div className={styles.listWrap} ref={scrollRef}>
        {showLoading && <LoadingState />}
        {showError && <ErrorState message={error!.message} onRetry={onRetry} />}
        {showEmpty && <EmptyState message={emptyMessage} />}

        {items.length > 0 && (
          <div style={{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }}>
            {virtualizer.getVirtualItems().map((virtualItem) => (
              <div
                key={virtualItem.key}
                data-index={virtualItem.index}
                ref={virtualizer.measureElement}
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  width: '100%',
                  transform: `translateY(${virtualItem.start}px)`,
                }}
              >
                {renderRow(items[virtualItem.index]!, virtualItem.index)}
              </div>
            ))}
          </div>
        )}
      </div>

      {items.length > 0 && (
        <footer className={styles.footer} data-testid="entry-list-footer">
          Showing {items.length.toLocaleString()} of {total.toLocaleString()}
        </footer>
      )}
    </div>
  );
}
