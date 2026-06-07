export interface MatchRange {
  start: number
  length: number
}

/**
 * Computes which characters of a directory `suggestion` matched the leaf segment the user is
 * typing, so the dropdown can highlight them. The server ranks/includes suggestions via fuzzy
 * (substring + subsequence) matching on the leaf name; this re-derives the matched positions
 * presentationally — there is no need to ship match ranges over the wire.
 *
 * Matching is performed only within the suggestion's leaf (the part after the last separator),
 * against the leaf of the typed `input`. A contiguous substring is preferred; otherwise the
 * subsequence positions are returned, coalesced into contiguous ranges. Returns `[]` when there
 * is nothing to highlight.
 *
 * Ranges are in `suggestion`-string coordinates (offset past the parent path).
 */
export function matchRanges(input: string, suggestion: string): MatchRange[] {
  const query = leafOf(input).toLowerCase()
  if (query.length === 0) return []

  const leafStart = suggestion.lastIndexOf('/') + 1
  const leaf = suggestion.slice(leafStart).toLowerCase()

  // Prefer a contiguous substring (partial) match — it reads best when highlighted.
  const sub = leaf.indexOf(query)
  if (sub >= 0) {
    return [{ start: leafStart + sub, length: query.length }]
  }

  // Fall back to a greedy subsequence (fuzzy) match, coalescing adjacent indices into ranges.
  const indices: number[] = []
  let ti = 0
  for (const ch of query) {
    let found = false
    while (ti < leaf.length) {
      if (leaf[ti] === ch) {
        indices.push(ti)
        ti++
        found = true
        break
      }
      ti++
    }
    if (!found) return [] // not a subsequence — nothing to highlight
  }

  const ranges: MatchRange[] = []
  for (const idx of indices) {
    const last = ranges[ranges.length - 1]
    if (last && idx === last.start - leafStart + last.length) {
      last.length++
    } else {
      ranges.push({ start: leafStart + idx, length: 1 })
    }
  }
  return ranges
}

/** The leaf segment of a typed path, handling both '/' and '\' separators. */
function leafOf(path: string): string {
  const slash = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'))
  return slash < 0 ? path : path.slice(slash + 1)
}
