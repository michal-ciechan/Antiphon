import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen } from '../../test/utils'
import { Sparkline } from './Sparkline'

describe('Sparkline', () => {
  it('draws 360 points with one M, 359 L, and newest at the right edge', () => {
    const points = Array.from({ length: 360 }, (_, i) => ({ t: new Date(2026, 0, 1, 0, 0, i * 5).toISOString(), v: i }))
    renderWithProviders(<Sparkline points={points} title="CPU history" />)
    const path = screen.getByTitle('CPU history').closest('svg')?.querySelector('path')
    const d = path?.getAttribute('d') ?? ''
    expect(d.match(/M/g)).toHaveLength(1)
    expect(d.match(/L/g)).toHaveLength(359)
    expect(d).toMatch(/L100(?:\.0+)?[, ]/)
  })

  it('shows a placeholder for an empty series', () => {
    renderWithProviders(<Sparkline points={[]} title="CPU history" />)
    expect(screen.getByText('No history')).toBeInTheDocument()
    expect(screen.queryByTitle('CPU history')).not.toBeInTheDocument()
  })
})
