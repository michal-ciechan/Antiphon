import type { NodeKind, WorkflowEdgeDto, WorkflowGraphDto, WorkflowNodeDto } from '../../../api/reviewEngine'

/**
 * The default `pr-review` workflow from design.md §3.3, as the server would serve it from
 * GET /api/review-workflows/default. Descriptions are the human one-liners from the design (never
 * sent to models). Section `collapsed` is the workflow's default display state. Fake data only.
 */

const node = (
  id: string,
  kind: NodeKind,
  run: string | null,
  description: string,
  params: Record<string, unknown> = {},
): WorkflowNodeDto => ({ id, kind, run, description, params })

const edge = (from: string, to: string, label: string | null = null, back?: { max: number | null }): WorkflowEdgeDto => ({
  from,
  to,
  label,
  maxIterations: back?.max ?? null,
  isBackEdge: !!back,
})

export const DEFAULT_WORKFLOW: WorkflowGraphDto = {
  name: 'pr-review',
  revisionId: '5eed0007-0000-4000-8000-000000000001',
  hash: 'sha256:9c41e2b07d3a5f18c6e0b2a4d97f13e85a0c6b2d4e1f7a9038b5c2d6e4f1a7b3',
  sections: [
    { id: 'intake', title: 'Intake', nodeIds: ['intake', 'delta', 'categorise', 'select', 'group', 'packs'], collapsed: false },
    { id: 'review', title: 'Review', nodeIds: ['review', 'reviewer', 'collate', 'verify', 'publish'], collapsed: false },
    {
      id: 'decide',
      title: 'Decide',
      nodeIds: ['route', 'await-fixes', 'record-drift', 'needs-human-approval', 'bot-approve', 'human-approve', 'human-review'],
      collapsed: false,
    },
    // branch maintenance only matters after an approval, so the workflow shows it folded by default
    { id: 'branch', title: 'Branch maintenance', nodeIds: ['base-moved', 'base-drift', 'update-branch', 'await-merge'], collapsed: true },
  ],
  nodes: [
    node('intake', 'Code', 'Intake', 'Pins base and head SHAs, fetches the mirror, reads policy and checks at the base SHA, and adds code-only tags.'),
    node('delta', 'Code', 'Delta', 'On a recheck, narrows work to what changed since the last reviewed head and re-anchors open findings. Full on the first iteration.'),
    node('categorise', 'Decision', 'Categorise', 'One batched Jev call: change kind, risk areas and the six blast-radius dimensions.', { chain: 'decisions-default' }),
    node('select', 'Decision', 'SelectChecks', "Prefilters the check catalogue, then asks Jev \"applies?\" per candidate; the round's posting bar drops checks that cannot clear it."),
    node('group', 'Code', 'GroupChecks', 'Bin-packs the selected checks into reviewer groups by category and token budget; bypass categories get their own group.'),
    node('packs', 'Code', 'BuildPacks', "Builds one context file per reviewer: a byte-identical shared prefix plus the group's check prompts."),
    node('review', 'FanOut', null, 'Fans out one reviewer per group on runner seats and waits for all of them.', { over: 'groups', node: 'reviewer', policy: 'all' }),
    node('reviewer', 'Agent', null, 'A read-only agent session on a runner seat that reviews its pack and returns review-findings/1.', {
      role: 'Reviewer',
      output: 'review-findings/1',
      runner: { labels: ['review'] },
    }),
    node('collate', 'Join', 'CollateFindings', "Gathers every reviewer's findings, collapses duplicates and re-anchors them at the reviewed SHA.", { from: 'review' }),
    node('verify', 'Completion', 'VerifyFindings', 'A cheaper model tries to disprove each finding; disproved ones are dropped and recorded.', { chain: 'completions-cheap' }),
    node('publish', 'Code', 'Publish', "Posts findings that clear the round's bar as one PR review, edits the summary comment, and sets commit statuses."),
    node('route', 'Code', 'RouteAfterRound', 'Decides what happens after the round: findings → wait for changes, clean → needs-human-approval, bot-round limit → human review.', {
      outputs: ['bot-round-limit', 'findings', 'clean'],
    }),
    node('await-fixes', 'Wait', null, 'Parks the run, with no seat held, until the author pushes fixes or someone asks for a recheck. Times out after 14 days.', {
      on: ['push', 'recheck', 'close', 'base-moved', 'update-requested'],
      timeout: '14d',
    }),
    node('record-drift', 'Code', 'RecordBaseDrift', 'Notes that the base moved while fixes are awaited. No model call, no re-review and no branch update.'),
    node('needs-human-approval', 'Code', 'NeedsHumanApproval', 'For a clean round, applies the policy rules to decide whether a human must approve. The answer is sticky for the rest of the PR.', {
      outputs: ['yes', 'no'],
    }),
    node('bot-approve', 'Code', 'BotApprove', 'Sets antiphon/approval to success for the reviewed SHA when every auto-approve condition holds.'),
    node('human-approve', 'Gate', null, 'A team member approves a clean round, sends it back for another round with a steer, or closes.', {
      queue: 'owning-team',
      decisions: ['approve', 'another-round', 'close'],
    }),
    node('human-review', 'Gate', null, 'Opened after the bot-only round limit: a human approves, steers another round, or closes.', {
      queue: 'owning-team',
      decisions: ['approve', 'another-round', 'close'],
    }),
    node('base-moved', 'Code', 'CheckBaseMoved', 'After approval, checks in code whether the approved branch is behind the base tip. No model call.', {
      outputs: ['unchanged', 'moved', 'behind'],
    }),
    node('base-drift', 'Decision', 'BaseDriftCheck', 'A code-only prefilter, then one cheap Jev call only on overlap: could the new base commits affect this PR?', {
      chain: 'decisions-default',
      outputs: ['affected', 'unaffected'],
    }),
    node('update-branch', 'Code', 'UpdateBranch', 'Brings the branch up to date by rebase or merge and carries the approval forward when patch ids are unchanged.', {
      outputs: ['approved', 'open'],
    }),
    node('await-merge', 'Wait', null, "Holds the approved PR until it's merged or closed. Keeps the approval while only the base branch moves.", {
      on: ['merge', 'close', 'push', 'base-moved'],
    }),
  ],
  edges: [
    edge('intake', 'delta'),
    edge('delta', 'categorise'),
    edge('categorise', 'select'),
    edge('select', 'group'),
    edge('group', 'packs'),
    edge('packs', 'review'),
    edge('review', 'reviewer'),
    edge('reviewer', 'collate'),
    edge('collate', 'verify'),
    edge('verify', 'publish'),
    edge('publish', 'route'),
    edge('route', 'human-review', 'bot-round-limit'),
    edge('route', 'await-fixes', 'findings'),
    edge('route', 'needs-human-approval', 'clean'),
    edge('needs-human-approval', 'bot-approve', 'no'),
    edge('needs-human-approval', 'human-approve', 'yes'),
    edge('bot-approve', 'base-moved'),
    edge('human-approve', 'base-moved', 'approve'),
    edge('human-review', 'base-moved', 'approve'),
    edge('base-moved', 'await-merge', 'unchanged'),
    edge('base-moved', 'base-drift', 'moved | behind'),
    edge('update-branch', 'await-merge', 'approved'),
    edge('update-branch', 'await-fixes', 'open'),
    edge('await-fixes', 'record-drift', 'base-moved'),
    edge('record-drift', 'await-fixes', null, { max: null }),
    edge('await-fixes', 'update-branch', 'update-requested'),
    edge('await-merge', 'base-drift', 'base-moved'),
    edge('base-drift', 'update-branch', 'unaffected'),
    edge('human-approve', 'intake', 'another-round', { max: null }),
    edge('human-review', 'intake', 'another-round', { max: null }),
    edge('await-fixes', 'intake', 'push | recheck', { max: 20 }),
    edge('await-merge', 'intake', 'push', { max: 20 }),
    edge('base-drift', 'intake', 'affected', { max: 10 }),
  ],
}

export const NODE_KIND_BY_ID: Record<string, NodeKind> = Object.fromEntries(DEFAULT_WORKFLOW.nodes.map((n) => [n.id, n.kind]))
