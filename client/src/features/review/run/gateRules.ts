import type { GateDecision, GateKind, ReviewGateDto, ReviewRunDetailDto } from '../../../api/reviewEngine'

/**
 * Why the viewer cannot decide this gate, or null when they can. The server computes it for the
 * current viewer (`viewerCanDecide` and `viewerCannotDecideReason`, contract v0.1) and refuses a
 * decision with the same reason, so the client never works it out itself.
 */
export function viewerBlockReason(gate: ReviewGateDto): string | null {
  if (gate.viewerCanDecide) return null
  return gate.viewerCannotDecideReason ?? "You can't decide this gate."
}

const DECISION_ORDER: GateDecision[] = ['Approve', 'AnotherRound', 'Close']

/** The decisions this gate offers (`allowedDecisions`), in display order. */
export function gateDecisions(gate: ReviewGateDto): GateDecision[] {
  return DECISION_ORDER.filter((d) => gate.allowedDecisions.includes(d))
}

/** The most recently decided gate, for "last decision" when none is open. */
export function lastDecidedGate(run: ReviewRunDetailDto): ReviewGateDto | undefined {
  return run.gates
    .filter((g) => g.status === 'Done' && g.decidedAt)
    .sort((a, b) => Date.parse(b.decidedAt!) - Date.parse(a.decidedAt!))[0]
}

export function openGate(run: ReviewRunDetailDto): ReviewGateDto | undefined {
  const open = run.gates.filter((g) => g.status !== 'Done')
  // a gate the run is parked on comes before a pre-assigned one or an escalated dispute
  return open.find((g) => g.kind === 'ReviewGate' || g.kind === 'ApprovalGate' || g.kind === 'MergeGate') ?? open[0]
}

export const DECISION_COPY: Record<'default' | 'DisputedFindings' | 'MergeGate', Record<GateDecision, { label: string; description: string }>> = {
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
  MergeGate: {
    Approve: { label: 'Merge', description: 'Antiphon brings the branch up to date with the base, then merges it by rebase at the approved head.' },
    AnotherRound: { label: 'Another round', description: 'Resets the bot-only counter. The optional steer goes into every reviewer pack.' },
    Close: { label: "Don't merge", description: 'Closes the review. The PR stays open and approved; anyone can still merge it on GitHub.' },
  },
}

export function decisionCopy(kind: GateKind) {
  return kind === 'DisputedFindings' ? DECISION_COPY.DisputedFindings : kind === 'MergeGate' ? DECISION_COPY.MergeGate : DECISION_COPY.default
}
