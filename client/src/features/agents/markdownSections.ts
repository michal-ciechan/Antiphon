/**
 * Markdown section engine (feature 009): heading-delimited splitting with stable keys, content
 * hashes for review staleness, subtree ranges for hierarchical collapse, and section/block diffs
 * for the rendered diff modes. The client is the single authority on section structure — the
 * server stores keys and hashes as opaque strings.
 */

export interface MarkdownSection {
  /** Slugified heading + `-<n>` occurrence suffix for duplicates; the preamble is `__intro`. */
  key: string
  /** 1–6 for headings, 0 for the preamble. */
  level: number
  /** Heading text as written (inline markdown intact); '' for the preamble. */
  heading: string
  /** 1-based line of the heading (or first line for the preamble) — the comment anchor. */
  startLine: number
  /** Direct content: heading line through the line before the next heading (any level). */
  content: string
  hash: string
}

const HEADING = /^(#{1,6})\s+(.*?)\s*#*\s*$/
const FENCE = /^(```|~~~)/

/** FNV-1a 32-bit, seedable — two passes concatenated give the stored 16-hex-char hash. */
function fnv1a32(text: string, seed: number): number {
  let hash = seed >>> 0
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return hash >>> 0
}

export function sectionHash(text: string): string {
  const a = fnv1a32(text, 0x811c9dc5)
  const b = fnv1a32(text, 0x9747b28c)
  return a.toString(16).padStart(8, '0') + b.toString(16).padStart(8, '0')
}

export function slugify(heading: string): string {
  const slug = heading
    .toLowerCase()
    .replace(/[`*_~[\]()]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80)
  return slug || 'section'
}

/**
 * Split at every ATX heading outside fenced code blocks. Content before the first heading becomes
 * a synthetic `__intro` section (omitted when blank).
 */
export function splitSections(markdown: string): MarkdownSection[] {
  const lines = markdown.split('\n')
  const boundaries: Array<{ line: number; level: number; heading: string }> = []
  let inFence: string | null = null
  for (let i = 0; i < lines.length; i++) {
    const fence = FENCE.exec(lines[i])
    if (fence) {
      if (inFence === null) inFence = fence[1]
      else if (fence[1] === inFence) inFence = null
      continue
    }
    if (inFence) continue
    const m = HEADING.exec(lines[i])
    if (m) boundaries.push({ line: i, level: m[1].length, heading: m[2] })
  }

  const sections: MarkdownSection[] = []
  const slugCounts = new Map<string, number>()
  const push = (start: number, end: number, level: number, heading: string) => {
    const content = lines.slice(start, end).join('\n')
    let key: string
    if (level === 0) {
      if (!content.trim()) return
      key = '__intro'
    } else {
      const slug = slugify(heading)
      const n = (slugCounts.get(slug) ?? 0) + 1
      slugCounts.set(slug, n)
      key = n === 1 ? slug : `${slug}-${n}`
    }
    // Hash on trimmed content: a section's trailing blank lines depend on whether it sits
    // mid-file or at the end, and moving it must not read as a content change.
    sections.push({
      key,
      level,
      heading,
      startLine: start + 1,
      content,
      hash: sectionHash(content.trimEnd()),
    })
  }

  if (boundaries.length === 0) {
    push(0, lines.length, 0, '')
    return sections
  }
  push(0, boundaries[0].line, 0, '')
  for (let b = 0; b < boundaries.length; b++) {
    const end = b + 1 < boundaries.length ? boundaries[b + 1].line : lines.length
    push(boundaries[b].line, end, boundaries[b].level, boundaries[b].heading)
  }
  return sections
}

/**
 * End index (exclusive) of the subtree rooted at `index`: every following section with a deeper
 * heading level. The preamble is always a leaf.
 */
export function subtreeEnd(sections: MarkdownSection[], index: number): number {
  const root = sections[index]
  if (root.level === 0) return index + 1
  let end = index + 1
  while (end < sections.length && sections[end].level > root.level) end++
  return end
}

/** 1-based inclusive line range covered by a section (comment-thread membership test). */
export function sectionLineRange(
  sections: MarkdownSection[],
  index: number,
): { start: number; end: number } {
  const start = sections[index].startLine
  const lineCount = sections[index].content.split('\n').length
  return { start, end: start + lineCount - 1 }
}

// ---- Diff ----

export type SectionDiffStatus = 'unchanged' | 'changed' | 'added' | 'removed'

export interface SectionDiffEntry {
  key: string
  status: SectionDiffStatus
  base: MarkdownSection | null
  work: MarkdownSection | null
}

/** Longest common subsequence over two string arrays; returns index pairs of the matches. */
function lcs(a: string[], b: string[]): Array<[number, number]> {
  const n = a.length
  const m = b.length
  const table: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0))
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      table[i][j] = a[i] === b[j] ? table[i + 1][j + 1] + 1 : Math.max(table[i + 1][j], table[i][j + 1])
    }
  }
  const pairs: Array<[number, number]> = []
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      pairs.push([i, j])
      i++
      j++
    } else if (table[i + 1][j] >= table[i][j + 1]) i++
    else j++
  }
  return pairs
}

/**
 * Align base and work sections by key (LCS, so moves and duplicate keys resolve sanely) and
 * classify each: removed sections appear at their old position between the surrounding matches.
 */
export function diffSections(base: MarkdownSection[], work: MarkdownSection[]): SectionDiffEntry[] {
  const pairs = lcs(
    base.map((s) => s.key),
    work.map((s) => s.key),
  )
  const entries: SectionDiffEntry[] = []
  let bi = 0
  let wi = 0
  const emitGap = (bEnd: number, wEnd: number) => {
    while (bi < bEnd) {
      entries.push({ key: base[bi].key, status: 'removed', base: base[bi], work: null })
      bi++
    }
    while (wi < wEnd) {
      entries.push({ key: work[wi].key, status: 'added', base: null, work: work[wi] })
      wi++
    }
  }
  for (const [b, w] of pairs) {
    emitGap(b, w)
    entries.push({
      key: work[w].key,
      status: base[b].hash === work[w].hash ? 'unchanged' : 'changed',
      base: base[b],
      work: work[w],
    })
    bi = b + 1
    wi = w + 1
  }
  emitGap(base.length, work.length)
  return entries
}

// ---- Block diff (within a changed section) ----

export interface BlockDiffEntry {
  status: 'unchanged' | 'added' | 'removed'
  text: string
}

/**
 * Split markdown into blank-line-separated blocks, keeping fenced code blocks intact even when
 * they contain blank lines.
 */
export function splitBlocks(markdown: string): string[] {
  const lines = markdown.split('\n')
  const blocks: string[] = []
  let current: string[] = []
  let inFence: string | null = null
  const flush = () => {
    if (current.length > 0) {
      blocks.push(current.join('\n'))
      current = []
    }
  }
  for (const line of lines) {
    const fence = FENCE.exec(line)
    if (fence) {
      if (inFence === null) inFence = fence[1]
      else if (fence[1] === inFence) inFence = null
      current.push(line)
      continue
    }
    if (!inFence && line.trim() === '') flush()
    else current.push(line)
  }
  flush()
  return blocks
}

/**
 * Block-level diff of one section's base vs work content: fine enough to point at the changed
 * paragraph, coarse enough that every fragment still renders as real markdown (no raw-HTML
 * injection).
 */
export function diffBlocks(baseText: string, workText: string): BlockDiffEntry[] {
  const base = splitBlocks(baseText)
  const work = splitBlocks(workText)
  const pairs = lcs(base, work)
  const entries: BlockDiffEntry[] = []
  let bi = 0
  let wi = 0
  const emitGap = (bEnd: number, wEnd: number) => {
    while (bi < bEnd) entries.push({ status: 'removed', text: base[bi++] })
    while (wi < wEnd) entries.push({ status: 'added', text: work[wi++] })
  }
  for (const [b, w] of pairs) {
    emitGap(b, w)
    entries.push({ status: 'unchanged', text: work[w] })
    bi = b + 1
    wi = w + 1
  }
  emitGap(base.length, work.length)
  return entries
}
