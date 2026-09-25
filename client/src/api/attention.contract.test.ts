import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import type { AttentionItemDto } from './attention'

/**
 * CARD-0672 D-3 (review 3488192e (4)): the client's AttentionItemDto names every field the server's
 * AttentionItemDto record serialises. The server adds its fields as trailing optional positional
 * parameters, so a new one (HoldClass) compiles and ships while the client type silently lacks it.
 */
const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '..', '..', '..')

function recordFields(source: string): string[] {
  const record = source.match(/public sealed record AttentionItemDto\(([\s\S]*?)\);/)
  expect(record, 'server AttentionItemDto record').not.toBeNull()
  // A Windows checkout reads the record with CRLF endings; '.' stops at CR, so normalise first.
  return record![1]
    .replace(/\r\n?/g, '\n')
    .split('\n')
    .map((line) => line.replace(/\/\/.*$/, '').trim())
    .filter((line) => line.length > 0)
    .map((line) => line.replace(/\s*=.*$/, '').replace(/,$/, '').trim())
    .map((parameter) => parameter.split(/\s+/).at(-1)!)
    .map((name) => name[0].toLowerCase() + name.slice(1))
}

function serverFields(): string[] {
  return recordFields(readFileSync(path.join(repoRoot, 'server', 'Application', 'Dtos', 'AttentionDtos.cs'), 'utf8'))
}

function clientFields(): string[] {
  const source = readFileSync(path.join(here, 'attention.ts'), 'utf8')
  const body = source.match(/export interface AttentionItemDto \{([\s\S]*?)\n\}/)
  expect(body, 'client AttentionItemDto interface').not.toBeNull()
  return [...body![1].matchAll(/^\s*([A-Za-z]+)\??:/gm)].map((match) => match[1])
}

describe('AttentionItemDto contract', () => {
  it('names every field the server record serialises, and nothing it does not', () => {
    expect([...clientFields()].sort()).toEqual([...serverFields()].sort())
  })

  // Review c1c0dd1a (1): a Windows checkout reads AttentionDtos.cs with CRLF endings. The same
  // record in LF and in CRLF must parse to the same fields, trailing comments included.
  it('parses the server record the same with LF and CRLF line endings', () => {
    const lf = [
      'public sealed record AttentionItemDto(',
      '    string Kind, // the attention kind',
      '    Guid? TaskId,',
      '    // CARD-0672 D-3: the dominant hold class (lease, capacity, ...).',
      '    string? HoldClass = null);',
    ].join('\n')
    const expected = ['kind', 'taskId', 'holdClass']
    expect(recordFields(lf)).toEqual(expected)
    expect(recordFields(lf.replaceAll('\n', '\r\n'))).toEqual(expected)
  })

  it('carries the DispatchHeld dominant hold class as an optional string', () => {
    const source = readFileSync(path.join(here, 'attention.ts'), 'utf8')
    expect(source).toMatch(/^\s*holdClass\?: string \| null$/m)
    const item: Pick<AttentionItemDto, 'kind' | 'holdClass'> = { kind: 'DispatchHeld', holdClass: 'lease' }
    expect(item.holdClass).toBe('lease')
  })
})
