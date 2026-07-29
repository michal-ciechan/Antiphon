import { describe, expect, it } from 'vitest'
import type { AgentFileDto } from '../../api/review'
import { buildTree, isUnviewed } from './FilesReviewPanel'

function file(path: string, overrides: Partial<AgentFileDto> = {}): AgentFileDto {
  return {
    path,
    gitStatus: 'Modified',
    external: false,
    agentEdits: 0,
    lastAgentEditAt: null,
    contentHash: 'abc',
    reviewLevel: null,
    reviewStale: false,
    sizeBytes: 10,
    isMarkdown: path.endsWith('.md'),
    ...overrides,
  }
}

describe('isUnviewed', () => {
  it('unmarked files need attention', () => {
    expect(isUnviewed(file('a.md'))).toBe(true)
  })

  it('a fresh mark clears the file', () => {
    expect(isUnviewed(file('a.md', { reviewLevel: 'Viewed', reviewStale: false }))).toBe(false)
    expect(isUnviewed(file('a.md', { reviewLevel: 'Reviewed', reviewStale: false }))).toBe(false)
  })

  // The hash anchor: an edit after the mark makes it stale — the file is unviewed again.
  it('a stale mark needs attention again', () => {
    expect(isUnviewed(file('a.md', { reviewLevel: 'Viewed', reviewStale: true }))).toBe(true)
  })
})

describe('buildTree', () => {
  it('nests folders and keeps files as leaves', () => {
    const tree = buildTree([file('docs/a.md'), file('docs/deep/b.md'), file('top.md')])
    expect([...tree.children.keys()].sort()).toEqual(['docs', 'top.md'])
    const docs = tree.children.get('docs')!
    expect(docs.file).toBeUndefined()
    expect(docs.children.get('a.md')!.file!.path).toBe('docs/a.md')
    expect(docs.children.get('deep')!.children.get('b.md')!.file!.path).toBe('docs/deep/b.md')
    expect(tree.children.get('top.md')!.file!.path).toBe('top.md')
  })

  it('folder nodes carry their full prefix path (the context-menu mark target)', () => {
    const tree = buildTree([file('a/b/c.md')])
    expect(tree.children.get('a')!.children.get('b')!.path).toBe('a/b')
  })
})
