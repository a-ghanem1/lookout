import { describe, expect, it } from 'vitest';
import { formatSql, sqlPreview } from './sqlFormatter';

describe('formatSql', () => {
  it('breaks SELECT / FROM / WHERE onto separate lines', () => {
    const out = formatSql('SELECT a, b FROM t WHERE a = 1 ORDER BY b');
    expect(out).toBe('SELECT a, b\nFROM t\nWHERE a = 1\nORDER BY b');
  });

  it('indents JOIN clauses', () => {
    const out = formatSql('SELECT * FROM orders INNER JOIN customers ON c.id = o.customer_id');
    expect(out.split('\n')).toEqual([
      'SELECT *',
      'FROM orders',
      '  INNER JOIN customers ON c.id = o.customer_id',
    ]);
  });

  it('collapses whitespace and trims', () => {
    const out = formatSql('  SELECT\n\n  a\tFROM    t  ');
    expect(out).toBe('SELECT a\nFROM t');
  });

  it('returns empty string for empty input', () => {
    expect(formatSql('')).toBe('');
  });

  it('handles UPDATE ... SET ... WHERE', () => {
    const out = formatSql("UPDATE t SET x = 1 WHERE id = 2");
    expect(out).toBe('UPDATE t\nSET x = 1\nWHERE id = 2');
  });
});

describe('sqlPreview', () => {
  it('returns a single-line trimmed preview', () => {
    expect(sqlPreview('SELECT\n  *\nFROM t')).toBe('SELECT * FROM t');
  });

  it('truncates long previews', () => {
    const long = 'SELECT ' + 'a,'.repeat(200);
    const out = sqlPreview(long, 50);
    expect(out.length).toBe(50);
    expect(out.endsWith('…')).toBe(true);
  });
});
