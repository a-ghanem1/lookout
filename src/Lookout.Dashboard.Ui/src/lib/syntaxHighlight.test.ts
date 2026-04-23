import { describe, expect, it } from 'vitest';
import { tokenizeJson, tokenizeSql } from './syntaxHighlight';

describe('tokenizeJson', () => {
  it('marks strings before a colon as keys', () => {
    const tokens = tokenizeJson('{"id":1}');
    const stringToks = tokens.filter((t) => t.type === 'key' || t.type === 'string');
    expect(stringToks[0]).toEqual({ text: '"id"', type: 'key' });
  });

  it('marks strings after a colon as string values', () => {
    const tokens = tokenizeJson('{"name":"Alice"}');
    const stringToks = tokens.filter((t) => t.type === 'key' || t.type === 'string');
    expect(stringToks[0]).toEqual({ text: '"name"', type: 'key' });
    expect(stringToks[1]).toEqual({ text: '"Alice"', type: 'string' });
  });

  it('handles numbers', () => {
    const tokens = tokenizeJson('{"count":42}');
    expect(tokens.find((t) => t.type === 'number')).toEqual({ text: '42', type: 'number' });
  });

  it('handles booleans', () => {
    const tokens = tokenizeJson('{"ok":true,"fail":false}');
    const bools = tokens.filter((t) => t.type === 'bool');
    expect(bools.map((t) => t.text)).toEqual(['true', 'false']);
  });

  it('handles null', () => {
    const tokens = tokenizeJson('{"x":null}');
    expect(tokens.find((t) => t.type === 'null')).toEqual({ text: 'null', type: 'null' });
  });

  it('marks braces/brackets/colons/commas as punct', () => {
    const tokens = tokenizeJson('{"a":1}');
    const puncts = tokens.filter((t) => t.type === 'punct').map((t) => t.text);
    expect(puncts).toContain('{');
    expect(puncts).toContain('}');
    expect(puncts).toContain(':');
  });

  it('handles whitespace in pretty-printed JSON', () => {
    const json = '{\n  "key": "val"\n}';
    const tokens = tokenizeJson(json);
    const key = tokens.find((t) => t.type === 'key');
    const val = tokens.find((t) => t.type === 'string');
    expect(key?.text).toBe('"key"');
    expect(val?.text).toBe('"val"');
  });

  it('does not mark array string elements as keys', () => {
    const tokens = tokenizeJson('["a","b"]');
    const strings = tokens.filter((t) => t.type === 'string' || t.type === 'key');
    expect(strings.every((t) => t.type === 'string')).toBe(true);
  });

  it('returns empty array for empty string', () => {
    expect(tokenizeJson('')).toEqual([]);
  });
});

describe('tokenizeSql', () => {
  it('marks SELECT, FROM, WHERE as keywords', () => {
    const sql = 'SELECT *\nFROM t\nWHERE id = 1';
    const tokens = tokenizeSql(sql);
    const kws = tokens.filter((t) => t.type === 'keyword').map((t) => t.text);
    expect(kws).toContain('SELECT');
    expect(kws).toContain('FROM');
    expect(kws).toContain('WHERE');
  });

  it('marks INNER JOIN as a single keyword token', () => {
    const sql = 'SELECT *\nFROM orders\n  INNER JOIN customers ON c.id = o.cid';
    const kws = tokenizeSql(sql)
      .filter((t) => t.type === 'keyword')
      .map((t) => t.text);
    expect(kws).toContain('INNER JOIN');
    expect(kws).not.toContain('INNER');
    expect(kws).not.toContain('JOIN');
  });

  it('uppercases keyword text', () => {
    const sql = 'select * from t';
    const kws = tokenizeSql(sql)
      .filter((t) => t.type === 'keyword')
      .map((t) => t.text);
    expect(kws).toContain('SELECT');
    expect(kws).toContain('FROM');
  });

  it('preserves non-keyword text as plain tokens', () => {
    const sql = 'SELECT *\nFROM t';
    const plains = tokenizeSql(sql)
      .filter((t) => t.type === 'plain')
      .map((t) => t.text);
    expect(plains.join('')).toContain('*');
    expect(plains.join('')).toContain('t');
  });

  it('returns empty array for empty string', () => {
    expect(tokenizeSql('')).toEqual([]);
  });

  it('reconstructs the original string from tokens', () => {
    const sql = 'SELECT a, b\nFROM orders\n  INNER JOIN customers ON c.id = o.cid\nWHERE a = 1\nORDER BY b';
    const tokens = tokenizeSql(sql);
    // keywords are uppercased, so compare case-insensitively
    const reconstructed = tokens.map((t) => t.text).join('');
    expect(reconstructed.toUpperCase()).toBe(sql.toUpperCase());
  });
});
