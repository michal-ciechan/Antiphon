import type { ProjectDeletionImpactDto } from '../../api/projects'

/**
 * Turns the impact counts into the sentence fragments the warning lists. Lives outside
 * ProjectDeleteDialog.tsx so the component file only exports components (react-refresh) —
 * getting "1 board" vs "2 boards" right matters more than it looks when the text is the only
 * thing standing between a click and an irreversible delete. The tests import from here.
 */
export function describeImpact(impact: ProjectDeletionImpactDto): string[] {
  const lines: string[] = []
  if (impact.boardCount > 0) lines.push(plural(impact.boardCount, 'board'))
  if (impact.cardCount > 0) {
    lines.push(
      impact.openCardCount > 0
        ? `${plural(impact.cardCount, 'card')} — ${impact.openCardCount} still open`
        : plural(impact.cardCount, 'card'),
    )
  }
  if (impact.runningSessionCount > 0) {
    lines.push(`${plural(impact.runningSessionCount, 'running session')} will be killed`)
  }
  if (impact.detachedAgentCount > 0) {
    // Agents survive — say so explicitly, or the list reads like they are being deleted too.
    lines.push(`${plural(impact.detachedAgentCount, 'agent')} will be detached, not deleted`)
  }
  return lines
}

function plural(count: number, noun: string): string {
  return count === 1 ? `1 ${noun}` : `${count} ${noun}s`
}
