import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen } from '../../test/utils'
import { SessionContextBadge } from './SessionContextBadge'

describe('SessionContextBadge', () => {
  it('normal fullness renders a percentage', () => {
    renderWithProviders(<SessionContextBadge fullness={0.42} />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('42%')
    expect(badge).toHaveAttribute('data-tone', 'normal')
  })

  it('null renders the awaiting next turn state', () => {
    renderWithProviders(<SessionContextBadge fullness={null} />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('awaiting next turn')
    expect(badge).toHaveAttribute('data-tone', 'awaiting')
    expect(badge).toHaveAccessibleName(/awaiting next turn/i)
  })

  it('high fullness (>=80%) gets the warning treatment', () => {
    renderWithProviders(<SessionContextBadge fullness={0.8} />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('80%')
    expect(badge).toHaveAttribute('data-tone', 'warning')
  })

  it('fullness at 90% and above is danger, still below 80% stays normal', () => {
    const { unmount } = renderWithProviders(<SessionContextBadge fullness={0.9} />)
    expect(screen.getByTestId('session-context-badge')).toHaveAttribute('data-tone', 'danger')
    unmount()

    renderWithProviders(<SessionContextBadge fullness={0.79} />)
    expect(screen.getByTestId('session-context-badge')).toHaveAttribute('data-tone', 'normal')
    expect(screen.getByTestId('session-context-badge')).toHaveTextContent('79%')
  })
})
