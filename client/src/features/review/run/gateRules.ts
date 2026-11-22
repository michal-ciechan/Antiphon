import type { GateDecision, GateKind, ReviewGateDto, ReviewRunDetailDto } from '../../../api/reviewEngine'
import { RUN_STATUS_META } from '../reviewMeta'
import { prRef } from '../reviewFormat'
import type { ReviewViewer } from '../useReviewViewerStore'

/**
 * Why the viewer cannot decide this gate, or null when they can. A UI hint only: the server is
 * the authority (design §11.10). Trust thresholds are not in this slice, so they are not checked.
 */
export function gateBlockReason(run: ReviewRunDetailDto, gate: ReviewGateDto, viewer: ReviewViewer): string | null {
  if (viewer.login === run.subject.authorLogin)
    return `You're the author of ${prRef(run.subject)}: authors can't decide their own gate. Your replies still count as human involvement.`
  if (gate.kind !== 'DisputedFindings' && run.status !== 'AtGate')
    return `Pre-assigned while the run is ${RUN_STATUS_META[run.status].label.toLowerCase()}: the decision opens when the run reaches the gate.`
  if (gate.assigneeLogin && gate.assigneeLogin !== viewer.login) return `Assigned to @${gate.assigneeLogin}. Take it from the queue to decide it.`
  return null
}

export function openGate(run: ReviewRunDetailDto): ReviewGateDto | undefined {
  const open = run.gates.filter((g) => g.status !== 'Done')
  // a gate the run is parked on comes before a pre-assigned one or an escalated dispute
  return open.find((g) => g.kind === 'ReviewGate' || g.kind === 'ApprovalGate') ?? open[0]
}

export const DECISION_COPY: Record<'default' | 'DisputedFindings', Record<GateDecision, { label: string; description: string }>> = {
  default: {
    Approve: { label: 'Approve', description: 'antiphon/approval → success at the reviewed head. Merge is left to the author.' },
    AnotherRound: { label: 'Another round', description: 'Resets the bot-only counter. The optional steer goes into every reviewer pack.' },
    Close: { label: 'Close run', description: 'Stops reviewing this PR. Open findings stay on the PR.' },
  },
  DisputedFindings: {
    Approve: { label: 'Uphold the dispute', description: 'The disputed findings are dismissed.' },
    AnotherRound: { label: 'Keep the finding', description: 'It stays open and is re-reviewed in the next round.' },
    Close: { label: 'Close the item', description: 'No change to the findings.' },
  },
}

export function decisionCopy(kind: GateKind) {
  return kind === 'DisputedFindings' ? DECISION_COPY.DisputedFindings : DECISION_COPY.default
}
