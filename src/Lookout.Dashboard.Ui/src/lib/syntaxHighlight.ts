export type TokenType = 'keyword' | 'key' | 'string' | 'number' | 'bool' | 'null' | 'punct' | 'plain';

export interface SyntaxToken {
  text: string;
  type: TokenType;
}

// ─── JSON tokenizer ──────────────────────────────────────────────────────────

type JsonRawCat = 'string' | 'number' | 'bool' | 'null' | 'punct' | 'other';

// Groups: 1=string, 2=number, 3=bool, 4=null, 5=punct, 6=catch-all (ws or any char)
const JSON_TOKEN_RE =
  /("(?:[^"\\]|\\.)*")|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|(\btrue\b|\bfalse\b)|(\bnull\b)|([{}[\],:])|(\s+|[\s\S])/g;

export function tokenizeJson(json: string): SyntaxToken[] {
  const raw: Array<{ text: string; cat: JsonRawCat }> = [];
  const re = new RegExp(JSON_TOKEN_RE.source, JSON_TOKEN_RE.flags);
  let m: RegExpExecArray | null;
  while ((m = re.exec(json)) !== null) {
    if (m[1] !== undefined) raw.push({ text: m[1], cat: 'string' });
    else if (m[2] !== undefined) raw.push({ text: m[2], cat: 'number' });
    else if (m[3] !== undefined) raw.push({ text: m[3], cat: 'bool' });
    else if (m[4] !== undefined) raw.push({ text: m[4], cat: 'null' });
    else if (m[5] !== undefined) raw.push({ text: m[5], cat: 'punct' });
    else raw.push({ text: m[0], cat: 'other' });
  }

  const catToType: Record<JsonRawCat, TokenType> = {
    string: 'string',
    number: 'number',
    bool: 'bool',
    null: 'null',
    punct: 'punct',
    other: 'plain',
  };

  return raw.map((tok, i) => {
    if (tok.cat === 'string') {
      // Skip whitespace ahead to check if ':' follows → object key
      let j = i + 1;
      while (j < raw.length && raw[j].text.trim() === '') j++;
      const isKey = j < raw.length && raw[j].text === ':';
      return { text: tok.text, type: isKey ? 'key' : 'string' } as SyntaxToken;
    }
    return { text: tok.text, type: catToType[tok.cat] };
  });
}

// ─── SQL tokenizer ───────────────────────────────────────────────────────────

// Superset of sqlFormatter.ts keywords + common inline keywords
const SQL_KEYWORDS = [
  'LEFT OUTER JOIN',
  'RIGHT OUTER JOIN',
  'FULL OUTER JOIN',
  'INNER JOIN',
  'LEFT JOIN',
  'RIGHT JOIN',
  'CROSS JOIN',
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
  'JOIN',
  'LIMIT',
  'OFFSET',
  'VALUES',
  'UPDATE',
  'SET',
  'RETURNING',
  'AS',
  'ON',
  'AND',
  'OR',
  'NOT',
  'IN',
  'IS',
  'DISTINCT',
  'ASC',
  'DESC',
];

function escapeRe(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export function tokenizeSql(formattedSql: string): SyntaxToken[] {
  if (!formattedSql) return [];
  const kwAlt = SQL_KEYWORDS.map(escapeRe).join('|');
  const kwRe = new RegExp(`\\b(${kwAlt})\\b`, 'gi');
  const tokens: SyntaxToken[] = [];
  let lastIndex = 0;
  let m: RegExpExecArray | null;
  while ((m = kwRe.exec(formattedSql)) !== null) {
    if (m.index > lastIndex) {
      tokens.push({ text: formattedSql.slice(lastIndex, m.index), type: 'plain' });
    }
    tokens.push({ text: m[0].toUpperCase(), type: 'keyword' });
    lastIndex = m.index + m[0].length;
  }
  if (lastIndex < formattedSql.length) {
    tokens.push({ text: formattedSql.slice(lastIndex), type: 'plain' });
  }
  return tokens;
}
