import type { TagFilter } from '../../api/types';
import styles from './TagChip.module.css';

interface TagChipProps {
  tag: TagFilter;
  onClick?: (tag: TagFilter) => void;
  onRemove?: (tag: TagFilter) => void;
  active?: boolean;
}

export function TagChip({ tag, onClick, onRemove, active = false }: TagChipProps) {
  const label = `${tag.key}=${tag.value}`;
  return (
    <span className={`${styles.chip} ${active ? styles.chipActive : ''}`}>
      <button
        type="button"
        className={styles.chipLabel}
        onClick={() => onClick?.(tag)}
        aria-label={`Filter by ${label}`}
        title={`Filter by ${label}`}
      >
        <span className={styles.chipKey}>{tag.key}</span>
        <span className={styles.chipSep}>=</span>
        <span className={styles.chipValue}>{tag.value}</span>
      </button>
      {onRemove && (
        <button
          type="button"
          className={styles.chipRemove}
          onClick={(e) => {
            e.stopPropagation();
            onRemove(tag);
          }}
          aria-label={`Remove filter ${label}`}
        >
          ×
        </button>
      )}
    </span>
  );
}

interface ActiveTagsBarProps {
  tags: TagFilter[];
  onRemove: (key: string, value: string) => void;
  onClear: () => void;
}

export function ActiveTagsBar({ tags, onRemove, onClear }: ActiveTagsBarProps) {
  if (tags.length === 0) return null;
  return (
    <div className={styles.activeBar} data-testid="active-tags-bar">
      {tags.map((t) => (
        <TagChip
          key={`${t.key}:${t.value}`}
          tag={t}
          active
          onRemove={(tag) => onRemove(tag.key, tag.value)}
        />
      ))}
      {tags.length >= 2 && (
        <button type="button" className={styles.clearAll} onClick={onClear}>
          Clear all
        </button>
      )}
    </div>
  );
}
