import { describe, expect, it } from 'vitest'
import table from './cardRanking.table.json'
import { effectiveUrgency, quadrant, rank, type CardImportance, type CardUrgency } from './cardRanking'

const NOW = new Date('2026-09-02T12:00:00Z')

describe('cardRanking', () => {
  it('matches the server 12-cell rank table', () => {
    for (const row of table) {
      expect(rank(row.importance as CardImportance, row.urgency as CardUrgency, null, NOW))
        .toBe(row.rank)
    }
  })

  it('escalates a due date within three days to Now and fourteen days to Soon', () => {
    expect(effectiveUrgency('Normal', '2026-09-05T12:00:00Z', NOW)).toBe('Now')
    expect(effectiveUrgency('Normal', '2026-09-16T12:00:00Z', NOW)).toBe('Soon')
    expect(effectiveUrgency('Normal', '2026-09-16T12:00:00.001Z', NOW)).toBe('Normal')
  })

  it('maps the four Eisenhower cells', () => {
    expect(quadrant('Critical', 'Now')).toBe('DoFirst')
    expect(quadrant('High', 'Normal')).toBe('Schedule')
    expect(quadrant('Normal', 'Now')).toBe('Clear')
    expect(quadrant('Low', 'Normal')).toBe('Someday')
  })
})
