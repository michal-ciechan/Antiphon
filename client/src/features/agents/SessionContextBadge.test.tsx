import { describe, expect, it } from 'vitest'
import { renderWithProviders, screen } from '../../test/utils'
import { SessionContextBadge } from './SessionContextBadge'

describe('SessionContextBadge', () => {
  it('Known fullness renders a percentage', () => {
    renderWithProviders(<SessionContextBadge fullness={0.42} state="Known" />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('42%')
    expect(badge).toHaveAttribute('data-tone', 'normal')
    expect(badge).toHaveAttribute('data-state', 'Known')
    expect(badge).toHaveAccessibleName('Context 42% full')
  })

  it('NoUsageYet renders no turns yet', () => {
    renderWithProviders(<SessionContextBadge fullness={null} state="NoUsageYet" />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('no turns yet')
    expect(badge).toHaveAttribute('data-tone', 'awaiting')
    expect(badge).toHaveAttribute('data-state', 'NoUsageYet')
    expect(badge).toHaveAccessibleName('No turns yet — context unknown')
  })

  it('Compacted renders awaiting next turn', () => {
    renderWithProviders(<SessionContextBadge fullness={null} state="Compacted" />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('awaiting next turn')
    expect(badge).toHaveAttribute('data-tone', 'awaiting')
    expect(badge).toHaveAttribute('data-state', 'Compacted')
    expect(badge).toHaveAccessibleName('Compacted — awaiting next turn')
  })

  it('Cleared renders cleared', () => {
    renderWithProviders(<SessionContextBadge fullness={null} state="Cleared" />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('cleared')
    expect(badge).toHaveAttribute('data-tone', 'awaiting')
    expect(badge).toHaveAttribute('data-state', 'Cleared')
    expect(badge).toHaveAccessibleName('Conversation cleared — awaiting next turn')
  })

  it('Suppressed renders nothing', () => {
    renderWithProviders(<SessionContextBadge fullness={0.42} state="Suppressed" />)
    expect(screen.queryByTestId('session-context-badge')).not.toBeInTheDocument()
  })

  it('absent state with null fullness renders unknown', () => {
    renderWithProviders(<SessionContextBadge fullness={null} />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('unknown')
    expect(badge).toHaveAttribute('data-tone', 'awaiting')
    expect(badge).toHaveAttribute('data-state', 'absent')
    expect(badge).toHaveAccessibleName('Context unknown')
  })

  it('high fullness (>=80%) gets the warning treatment', () => {
    renderWithProviders(<SessionContextBadge fullness={0.8} state="Known" />)
    const badge = screen.getByTestId('session-context-badge')
    expect(badge).toHaveTextContent('80%')
    expect(badge).toHaveAttribute('data-tone', 'warning')
  })

  it('fullness at 90% and above is danger, still below 80% stays normal', () => {
    const { unmount } = renderWithProviders(<SessionContextBadge fullness={0.9} state="Known" />)
    expect(screen.getByTestId('session-context-badge')).toHaveAttribute('data-tone', 'danger')
    unmount()

    renderWithProviders(<SessionContextBadge fullness={0.79} state="Known" />)
    expect(screen.getByTestId('session-context-badge')).toHaveAttribute('data-tone', 'normal')
    expect(screen.getByTestId('session-context-badge')).toHaveTextContent('79%')
  })
})
