const KEYWORDS = [
  'LEFT OUTER JOIN',
  'RIGHT OUTER JOIN',
  'FULL OUTER JOIN',
  'INNER JOIN',
  'LEFT JOIN',
  'RIGHT JOIN',
  'CROSS JOIN',
  'JOIN',
  'UNION ALL',
  'UNION',
  'GROUP BY',
  'ORDER BY',
  'INSERT INTO',
  'DELETE FROM',
  'SELECT',
  'FROM',
  'WHERE',
  'HAVING',
  'LIMIT',
  'OFFSET',
  'VALUES',
  'UPDATE',
  'SET',
  'RETURNING',
];

const JOIN_SET = new Set([
  'LEFT OUTER JOIN',
  'RIGHT OUTER JOIN',
  'FULL OUTER JOIN',
  'INNER JOIN',
  'LEFT JOIN',
  'RIGHT JOIN',
  'CROSS JOIN',
  'JOIN',
]);

const KEYWORD_PATTERN = new RegExp(
  '\\b(' + KEYWORDS.map(escapeRegex).join('|') + ')\\b',
  'gi',
);

export function formatSql(sql: string): string {
  if (!sql) return '';

  const collapsed = sql.replace(/\s+/g, ' ').trim();

  const matches: Array<{ index: number; text: string }> = [];
  KEYWORD_PATTERN.lastIndex = 0;
  let m: RegExpExecArray | null;
  while ((m = KEYWORD_PATTERN.exec(collapsed)) !== null) {
    matches.push({ index: m.index, text: m[0] });
  }

  if (matches.length === 0) return collapsed;

  const parts: string[] = [];
  if (matches[0].index > 0) {
    const prefix = collapsed.slice(0, matches[0].index).trim();
    if (prefix) parts.push(prefix);
  }

  for (let i = 0; i < matches.length; i++) {
    const start = matches[i].index;
    const end = i + 1 < matches.length ? matches[i + 1].index : collapsed.length;
    const segment = collapsed.slice(start, end).trim();
    const upper = matches[i].text.toUpperCase();
    parts.push(JOIN_SET.has(upper) ? `  ${segment}` : segment);
  }

  return parts.join('\n');
}

export function sqlPreview(sql: string, maxLen = 140): string {
  if (!sql) return '';
  const firstLine = sql.replace(/\s+/g, ' ').trim();
  return firstLine.length > maxLen ? `${firstLine.slice(0, maxLen - 1)}…` : firstLine;
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
