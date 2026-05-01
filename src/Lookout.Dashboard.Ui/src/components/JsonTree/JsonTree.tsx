import { useState } from 'react';
import styles from './JsonTree.module.css';

type JsonValue = string | number | boolean | null | JsonValue[] | { [k: string]: JsonValue };

const COLLAPSE_THRESHOLD = 10;

function copyText(text: string) {
  try {
    void navigator.clipboard.writeText(text);
  } catch {
    const el = document.createElement('textarea');
    el.value = text;
    document.body.appendChild(el);
    el.select();
    document.execCommand('copy');
    document.body.removeChild(el);
  }
}

interface JsonNodeProps {
  value: JsonValue;
  depth: number;
  label?: string;
  isLast?: boolean;
}

function JsonNode({ value, depth, label, isLast }: JsonNodeProps) {
  const isArray = Array.isArray(value);
  const isObject = value !== null && typeof value === 'object';
  const childCount = isObject ? (isArray ? (value as JsonValue[]).length : Object.keys(value as object).length) : 0;
  const startExpanded = childCount <= COLLAPSE_THRESHOLD;
  const [expanded, setExpanded] = useState(startExpanded);

  const comma = isLast ? '' : ',';

  if (!isObject) {
    return (
      <div className={styles.line}>
        {label !== undefined && <span className={styles.key}>{JSON.stringify(label)}: </span>}
        <span className={valueClass(value)}>{JSON.stringify(value)}</span>
        <span className={styles.punct}>{comma}</span>
      </div>
    );
  }

  const openBracket = isArray ? '[' : '{';
  const closeBracket = isArray ? ']' : '}';

  if (childCount === 0) {
    return (
      <div className={styles.line}>
        {label !== undefined && <span className={styles.key}>{JSON.stringify(label)}: </span>}
        <span className={styles.punct}>{openBracket}{closeBracket}</span>
        <span className={styles.punct}>{comma}</span>
      </div>
    );
  }

  const serialized = JSON.stringify(value, null, 2);

  return (
    <div className={styles.node}>
      <div className={styles.line}>
        <button
          type="button"
          className={styles.toggle}
          onClick={() => setExpanded((v) => !v)}
          aria-expanded={expanded}
          aria-label={expanded ? 'Collapse' : 'Expand'}
          onKeyDown={(e) => {
            if (e.key === 'ArrowRight') setExpanded(true);
            if (e.key === 'ArrowLeft') setExpanded(false);
          }}
        >
          {expanded ? '▼' : '▶'}
        </button>
        {label !== undefined && <span className={styles.key}>{JSON.stringify(label)}: </span>}
        <span className={styles.punct}>{openBracket}</span>
        {!expanded && (
          <span className={styles.collapsed}>
            {isArray ? `[${childCount} items]` : `{${childCount} keys}`}
          </span>
        )}
        {!expanded && <span className={styles.punct}>{closeBracket}{comma}</span>}
        <button
          type="button"
          className={styles.copyBtn}
          onClick={() => copyText(serialized)}
          title="Copy subtree"
          aria-label="Copy subtree JSON"
        >
          ⧉
        </button>
      </div>

      {expanded && (
        <div className={styles.children}>
          {isArray
            ? (value as JsonValue[]).map((v, i) => (
                <JsonNode
                  key={i}
                  value={v}
                  depth={depth + 1}
                  isLast={i === (value as JsonValue[]).length - 1}
                />
              ))
            : Object.entries(value as { [k: string]: JsonValue }).map(([k, v], i, arr) => (
                <JsonNode
                  key={k}
                  value={v}
                  depth={depth + 1}
                  label={k}
                  isLast={i === arr.length - 1}
                />
              ))}
          <div className={styles.line}>
            <span className={styles.punct}>{closeBracket}{comma}</span>
          </div>
        </div>
      )}
    </div>
  );
}

function valueClass(value: JsonValue): string {
  if (typeof value === 'string') return styles.string;
  if (typeof value === 'number') return styles.number;
  if (typeof value === 'boolean' || value === null) return styles.boolNull;
  return '';
}

interface JsonTreeProps {
  json: string;
}

export function JsonTree({ json }: JsonTreeProps) {
  let parsed: JsonValue;
  try {
    parsed = JSON.parse(json) as JsonValue;
  } catch {
    return <pre className={styles.raw}>{json}</pre>;
  }

  return (
    <div className={styles.root} data-testid="json-tree">
      <JsonNode value={parsed} depth={0} isLast />
    </div>
  );
}
