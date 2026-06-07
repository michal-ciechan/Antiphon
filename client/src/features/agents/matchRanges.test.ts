import { describe, expect, it } from 'vitest'
import { matchRanges } from './matchRanges'

describe('matchRanges', () => {
  it('highlights a substring within the leaf (the reported case)', () => {
    // "C:/src/lea" vs "C:/src/torquay-leander": "lea" sits at "...torquay-[lea]nder".
    const ranges = matchRanges('C:/src/lea', 'C:/src/torquay-leander')
    expect(ranges).toHaveLength(1)
    const { start, length } = ranges[0]
    expect('C:/src/torquay-leander'.slice(start, start + length)).toBe('lea')
  })

  it('highlights a prefix match', () => {
    const ranges = matchRanges('C:/sr', 'C:/src')
    expect(ranges).toEqual([{ start: 3, length: 2 }]) // "sr" at the leaf start
  })

  it('handles backslash input against forward-slash suggestions', () => {
    const ranges = matchRanges('C:\\src\\lea', 'C:/src/torquay-leander')
    expect(ranges).toHaveLength(1)
    const { start, length } = ranges[0]
    expect('C:/src/torquay-leander'.slice(start, start + length)).toBe('lea')
  })

  it('coalesces a scattered subsequence into contiguous ranges', () => {
    // "ace" matches a-b-c-d-e as a/c/e → three single-char ranges.
    const ranges = matchRanges('C:/x/ace', 'C:/x/abcde')
    const matched = ranges.map((r) => 'C:/x/abcde'.slice(r.start, r.start + r.length)).join('')
    expect(matched).toBe('ace')
    expect(ranges).toHaveLength(3)
  })

  it('returns nothing when the leaf does not match', () => {
    expect(matchRanges('C:/src/zzz', 'C:/src/other')).toEqual([])
  })

  it('returns nothing for an empty leaf (e.g. trailing slash / drive roots)', () => {
    expect(matchRanges('C:/src/', 'C:/src/alpha')).toEqual([])
    expect(matchRanges('', 'C:/')).toEqual([])
  })
})
