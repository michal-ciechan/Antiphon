import { describe, expect, it } from 'vitest'
import {
  diffBlocks,
  diffSections,
  sectionHash,
  sectionLineRange,
  slugify,
  splitBlocks,
  splitSections,
  subtreeEnd,
} from './markdownSections'

const DOC = `intro paragraph

# Title

opening words

## Setup

install things

## Setup

again (duplicate heading)

### Details

deep content

## Deploy

ship it`

describe('splitSections', () => {
  it('splits at headings, keys by slug with occurrence suffixes, preamble is __intro', () => {
    const sections = splitSections(DOC)
    expect(sections.map((s) => s.key)).toEqual([
      '__intro',
      'title',
      'setup',
      'setup-2',
      'details',
      'deploy',
    ])
    expect(sections.map((s) => s.level)).toEqual([0, 1, 2, 2, 3, 2])
  })

  it('a section owns its heading line and direct content only — subsections are separate', () => {
    const sections = splitSections(DOC)
    const setup = sections.find((s) => s.key === 'setup')!
    expect(setup.content).toBe('## Setup\n\ninstall things\n')
    const title = sections.find((s) => s.key === 'title')!
    expect(title.content).not.toContain('Setup')
  })

  it('start lines are 1-based and match the raw document', () => {
    const sections = splitSections(DOC)
    expect(sections.find((s) => s.key === '__intro')!.startLine).toBe(1)
    expect(sections.find((s) => s.key === 'title')!.startLine).toBe(3)
    const range = sectionLineRange(sections, 1) // title
    expect(range.start).toBe(3)
    expect(range.end).toBe(6)
  })

  it('ignores # lines inside fenced code blocks', () => {
    const doc = '# Real\n\n```sh\n# not a heading\necho hi\n```\n'
    const sections = splitSections(doc)
    expect(sections.map((s) => s.key)).toEqual(['real'])
  })

  it('a document with no headings is one intro section; blank preamble is omitted', () => {
    expect(splitSections('just text').map((s) => s.key)).toEqual(['__intro'])
    expect(splitSections('# H\n\nbody').map((s) => s.key)).toEqual(['h'])
  })
})

describe('sectionHash', () => {
  it('is stable for identical content and differs on any change', () => {
    expect(sectionHash('abc')).toBe(sectionHash('abc'))
    expect(sectionHash('abc')).not.toBe(sectionHash('abd'))
    expect(sectionHash('abc')).toHaveLength(16)
  })
})

describe('slugify', () => {
  it('strips inline markdown and collapses punctuation', () => {
    expect(slugify('Set-up & `run` (v2)')).toBe('set-up-run-v2')
    expect(slugify('§§§')).toBe('section')
  })
})

describe('subtreeEnd', () => {
  it('covers deeper-level sections until a peer heading; the preamble is a leaf', () => {
    const sections = splitSections(DOC)
    // title (h1) owns everything after it.
    expect(subtreeEnd(sections, 1)).toBe(sections.length)
    // setup-2 (h2) owns details (h3) but not deploy (h2).
    const setup2 = sections.findIndex((s) => s.key === 'setup-2')
    expect(subtreeEnd(sections, setup2)).toBe(setup2 + 2)
    expect(subtreeEnd(sections, 0)).toBe(1)
  })
})

describe('diffSections', () => {
  const base = splitSections('# A\n\none\n\n# B\n\ntwo\n\n# C\n\nthree')

  it('classifies unchanged, changed, added, and removed — removed keep their old position', () => {
    const work = splitSections('# A\n\none\n\n# B\n\ntwo CHANGED\n\n# D\n\nnew section')
    const diff = diffSections(base, work)
    expect(diff.map((d) => [d.key, d.status])).toEqual([
      ['a', 'unchanged'],
      ['b', 'changed'],
      ['c', 'removed'],
      ['d', 'added'],
    ])
  })

  it('a moved section stays matched by key, not treated as remove+add of content', () => {
    const work = splitSections('# C\n\nthree\n\n# A\n\none\n\n# B\n\ntwo')
    const diff = diffSections(base, work)
    // LCS keeps the longest stable run (a, b); c falls out as removed+added around it.
    const statuses = Object.fromEntries(diff.map((d) => [`${d.key}:${d.status}`, true]))
    expect(statuses['a:unchanged']).toBe(true)
    expect(statuses['b:unchanged']).toBe(true)
  })

  it('identical documents are fully unchanged', () => {
    const diff = diffSections(base, splitSections('# A\n\none\n\n# B\n\ntwo\n\n# C\n\nthree'))
    expect(diff.every((d) => d.status === 'unchanged')).toBe(true)
  })
})

describe('splitBlocks / diffBlocks', () => {
  it('splits on blank lines but keeps fenced code intact', () => {
    const blocks = splitBlocks('para one\n\n```\ncode\n\nstill code\n```\n\npara two')
    expect(blocks).toEqual(['para one', '```\ncode\n\nstill code\n```', 'para two'])
  })

  it('marks the changed paragraph as removed+added and leaves the rest alone', () => {
    const diff = diffBlocks('same\n\nold paragraph\n\ntail', 'same\n\nnew paragraph\n\ntail')
    expect(diff.map((d) => [d.status, d.text])).toEqual([
      ['unchanged', 'same'],
      ['removed', 'old paragraph'],
      ['added', 'new paragraph'],
      ['unchanged', 'tail'],
    ])
  })
})
