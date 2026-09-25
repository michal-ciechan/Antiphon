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

function serverFields(): string[] {
  const source = readFileSync(path.join(repoRoot, 'server', 'Application', 'Dtos', 'AttentionDtos.cs'), 'utf8')
  const record = source.match(/public sealed record AttentionItemDto\(([\s\S]*?)\);/)
  expect(record, 'server AttentionItemDto record').not.toBeNull()
  return record![1]
    .split('\n')
    .map((line) => line.replace(/\/\/.*$/, '').trim())
    .filter((line) => line.length > 0)
    .map((line) => line.replace(/\s*=.*$/, '').replace(/,$/, '').trim())
    .map((parameter) => parameter.split(/\s+/).at(-1)!)
    .map((name) => name[0].toLowerCase() + name.slice(1))
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

  it('carries the DispatchHeld dominant hold class as an optional string', () => {
    const source = readFileSync(path.join(here, 'attention.ts'), 'utf8')
    expect(source).toMatch(/^\s*holdClass\?: string \| null$/m)
    const item: Pick<AttentionItemDto, 'kind' | 'holdClass'> = { kind: 'DispatchHeld', holdClass: 'lease' }
    expect(item.holdClass).toBe('lease')
  })
})
