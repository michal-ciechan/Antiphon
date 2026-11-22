# Review engine API contract (v0)

Status: working contract for the first implementation slice, 2026-11-22. The client builds against
it with a fake implementation first. The server then implements the same routes. Change this file
first when the contract changes. Owner design: [design.md](design.md).

## Conventions

- Routes follow [project-context.md](../../project-context.md): `/api/review-*`, kebab-case plural,
  camelCase JSON.
- Enums serialise as **PascalCase strings** (the server's `JsonStringEnumConverter` has no naming
  policy).
- Ids are GUID strings. Times are ISO-8601 UTC. Money is `costUsd: number`; the client formats it
  (under $1 → rounded cents, otherwise rounded dollars).
- Errors are Problem Details from the `HttpException` hierarchy: 404 unknown id, 403 not allowed
  (e.g. the author deciding their own gate), 409 stale or illegal transition (e.g. dismissing an open
  blocker), 422 validation. Intake and Refresh can also return 503 when the code host fails, with a
  code of `review_code_host_rejected`, `review_code_host_rate_limited`,
  `review_code_host_unavailable` or `review_code_host_too_large`.
- Lists return `{ items: T[], total: number }`.

## Enums

```ts
type ReviewHost = 'GitHub' | 'Local'
type ReviewRunStatus = 'Queued' | 'Running' | 'AwaitingFixes' | 'AtGate' | 'Approved' | 'AwaitingMerge' | 'Merged' | 'Closed' | 'Failed'
type ReviewStage = 'Intake' | 'Categorise' | 'Select' | 'Review' | 'Collate' | 'Verify' | 'Publish' | 'AwaitingFixes' | 'HumanGate' | 'Approval' | 'BranchMaintenance' | 'Done'
type NodeKind = 'Code' | 'Decision' | 'Completion' | 'Agent' | 'FanOut' | 'Join' | 'Gate' | 'Wait'
type NodeStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped' | 'Waiting' | 'NotReached'
type Severity = 'Nit' | 'Minor' | 'Major' | 'Blocker'
type FindingStatus = 'Open' | 'WillFix' | 'Fixed' | 'Disputed' | 'Dismissed' | 'Deferred' | 'Outdated' | 'Accepted'
type SyncState = 'NotApplicable' | 'Pending' | 'Synced' | 'Failed'
type FindingAction = 'WillFix' | 'Fixed' | 'Dismiss' | 'Dispute' | 'Question' | 'Defer' | 'NotABlocker' | 'Reply'
type ReasonCategory = 'FalsePositive' | 'ByDesign' | 'OutOfScope' | 'AcceptedRisk' | 'FixLater' | 'Duplicate' | 'StylePreference'
type DismissScope = 'Instance' | 'Path' | 'Repo' | 'Team'
type GateKind = 'ReviewGate' | 'ApprovalGate' | 'DisputedFindings' | 'LearnProposal'
type GateDecision = 'Approve' | 'AnotherRound' | 'Close'
type QueueItemStatus = 'Unassigned' | 'Assigned' | 'InProgress' | 'Done'
type Transport = 'Http' | 'Seat' | 'OneShot'
```

## DTOs

```ts
interface ReviewSubjectDto {
  id: string; host: ReviewHost; repo: string /* owner/name */; prNumber: number | null
  title: string; url: string | null; authorLogin: string; authorDisplayName: string | null
  authorResolvedBy: string | null /* e.g. 'Co-authored-by' when the PR author was a service account */
  openedByLogin: string /* the account that opened the PR; differs from authorLogin when the author was resolved */
  baseRef: string; headRef: string; baseSha: string; headSha: string
  owners: { kind: 'Team' | 'User'; name: string }[] /* code owners of the changed paths */
}

interface BlastRadiusDto { reach: number; externalContract: number; data: number; rollbackAtRelease: number; reversalAfterMonths: number; exposure: number } // 1-5 each, or null when not categorised

interface ReviewRunSummaryDto {
  id: string; subject: ReviewSubjectDto; status: ReviewRunStatus; stage: ReviewStage
  round: number; botOnlyRounds: number; needsHumanApproval: boolean | null
  openFindings: number; openBlockers: number
  models: string[] /* distinct resolved models used, e.g. 'jev-1.14.0', 'gpt-5.6-luna' */
  costUsd: number /* actual + estimated */; costActualUsd: number /* API calls */; costEstimatedUsd: number /* seat API-equivalent */
  blastRadius: BlastRadiusDto | null
  startedAt: string; updatedAt: string
  waitingOn: 'Author' | 'Humans' | 'Bot' | null
}

interface NodeExecutionDto {
  id: string; nodeId: string; iteration: number; kind: NodeKind; status: NodeStatus
  itemKey: string | null /* fan-out item, e.g. the reviewer group 'security' */
  outcome: string | null /* the outgoing edge label taken, e.g. 'moved', 'clean' */
  provider: string | null; model: string | null; transport: Transport | null
  inputTokens: number; outputTokens: number; costUsd: number
  startedAt: string | null; completedAt: string | null; summary: string | null
}

interface ReviewIterationDto {
  iteration: number; round: number; startedAt: string
  trigger: string /* 'opened' | 'manual' | 'push' (Refresh saw a new head) | 'recheck' (Refresh of a failed run, same head) | 'another-round' | 'base-moved' … */
  baseSha: string | null; headSha: string | null /* what this iteration reviewed; the next delta starts from headSha */
}

interface ReviewRunDetailDto extends ReviewRunSummaryDto {
  categories: string[]
  workflowRevisionId: string | null
  iterations: ReviewIterationDto[]
  nodes: NodeExecutionDto[]          // every execution across iterations
  gates: ReviewGateDto[]
}

interface FindingEventDto {
  id: string; action: FindingAction; actorLogin: string; actorDisplayName: string | null
  reasonCategory: ReasonCategory | null; scope: DismissScope | null; text: string | null
  syncState: SyncState; createdAt: string
}

interface ReviewFindingDto {
  id: string; runId: string; key: string /* F1 */; checkId: string | null; checkWhy: string | null /* @humans text */
  severity: Severity; flags: string[] /* 'security', 'data-loss' */; bypassesRoundBar: boolean
  confidence: number; filePath: string; lineStart: number | null; lineEnd: number | null
  anchor: string; title: string; body: string; suggestion: string | null
  hunk: string | null /* unified-diff hunk around the anchor at the reviewed head */
  status: FindingStatus; roundRaised: number; posted: boolean; syncState: SyncState
  externalThreadUrl: string | null; events: FindingEventDto[]
}

interface FindingResponseRequest {
  action: FindingAction; reasonCategory?: ReasonCategory; scope?: DismissScope; text?: string
}

interface ReviewGateDto {
  id: string; runId: string; kind: GateKind; status: QueueItemStatus
  teamKey: string | null; assigneeLogin: string | null; assigneeDisplayName: string | null
  openedAt: string; decidedAt: string | null; decidedBy: string | null
  decision: GateDecision | null; steerPrompt: string | null; reasons: string[] /* why a human is needed */
  allowedDecisions: GateDecision[]
  viewerCanDecide: boolean; viewerCannotDecideReason: string | null /* e.g. 'You are the author' */
  passes: { login: string; reason: string; at: string }[]
}

interface GateDecisionRequest { decision: GateDecision; steerPrompt?: string }

interface QueueItemDto {
  gate: ReviewGateDto; run: ReviewRunSummaryDto
  ageWorkingMinutes: number; slaWorkingMinutes: number; slaState: 'Ok' | 'Warn' | 'Breached'
}

interface ReviewViewerDto { login: string; displayName: string | null; teams: string[] }

interface ReviewKpisDto {
  inReview: number; waitingOnHumans: number; averageRounds: number; costTodayUsd: number
  costPerAcceptedFindingUsd: number | null
  modelUsage: { model: string; provider: string; transport: Transport; calls: number; costUsd: number }[]
}

interface WorkflowGraphDto {
  name: string; revisionId: string | null; hash: string
  sections: { id: string; title: string; nodeIds: string[]; collapsed: boolean /* default display state */ }[]
  nodes: { id: string; kind: NodeKind; run: string | null; description: string | null; params: Record<string, unknown> }[]
  edges: { from: string; to: string; label: string | null; maxIterations: number | null; isBackEdge: boolean }[]
}

interface WorkflowValidationDto { valid: boolean; diagnostics: { severity: 'Error' | 'Warning'; code: string; message: string; nodeIds: string[]; edge: { from: string; to: string } | null }[] }
```

## Routes

| Method | Route | Body / query | Returns |
|---|---|---|---|
| GET | `/api/review-runs` | `?status=&repo=&stage=&q=` | `{ items: ReviewRunSummaryDto[], total }` |
| GET | `/api/review-runs/kpis` | | `ReviewKpisDto` |
| POST | `/api/review-runs` | `{ host, repo, prNumber?, baseSha?, headSha?, title? }` (manual intake through `ICodeHost`). `GitHub` needs `prNumber`. `Local` needs `baseSha` (any ref); `headSha` defaults to `HEAD`. The title defaults to the PR or commit title. | `ReviewRunDetailDto` (201) |
| GET | `/api/review-runs/{id}` | | `ReviewRunDetailDto` |
| GET | `/api/review-runs/{id}/findings` | `?status=` | `{ items: ReviewFindingDto[], total }` |
| POST | `/api/review-runs/{id}/refresh` | | 202. Re-reads the PR (the manual Refresh): a moved head starts a `push` iteration (next round); a failed run with an unchanged head starts a `recheck` iteration; demo runs are only re-announced. |
| POST | `/api/review-findings/{id}/responses` | `FindingResponseRequest` | `ReviewFindingDto` |
| POST | `/api/review-runs/{id}/responses` | `{ responses: (FindingResponseRequest & { findingId })[] }` (one submitted review) | `{ items: ReviewFindingDto[] }` |
| GET | `/api/review-queue` | `?scope=Mine\|Teams\|All&login=` | `{ items: QueueItemDto[], total }` |
| POST | `/api/review-gates/{id}/assign` | `{ login? }` (omit = random, excluding the author and anyone who passed) | `ReviewGateDto` |
| POST | `/api/review-gates/{id}/pass` | `{ reason }` ("not me") | `ReviewGateDto` |
| POST | `/api/review-gates/{id}/decision` | `GateDecisionRequest` | `ReviewRunDetailDto` |
| GET | `/api/review-workflows/default` | | `WorkflowGraphDto` |
| GET | `/api/review-workflows/revisions/{id}` | | `WorkflowGraphDto` (the snapshot a run used) |
| POST | `/api/review-workflows/validate` | `WorkflowGraphDto` | `WorkflowValidationDto` |
| GET | `/api/review-me` | | `ReviewViewerDto`. Until identity lands, the server resolves the viewer from `Review:DefaultViewerLogin`, overridable by the `X-Review-Viewer` header when `Review:AllowViewerHeader` is true (dev only). Mutations record this viewer as the actor. |

## SignalR

`ReviewRunChanged { runId }` → invalidates `['reviewRuns']`, `['reviewRun', runId]`,
`['reviewRun', runId, 'findings']`, `['reviewQueue']`, `['reviewKpis']`.

## Changelog

| Version | Change |
|---|---|
| v0 | First contract |
| v0.1 | Client-track proposals accepted:<br>- `itemKey` and `outcome` on node executions<br>- `iterations[]`<br>- gate: `allowedDecisions`, `viewerCanDecide` and reason, `decidedBy`, `passes[]` (replaces `passedBy`), `assigneeDisplayName`<br>- section `collapsed`, and a GET by revision<br>- blast radius and the actual/estimated cost split on the summary<br>- `slaWorkingMinutes`<br>- owners as `{kind, name}`, and `openedByLogin`<br>- finding `hunk`<br>- diagnostic `severity`<br>- `GET /api/review-me` |

| v0.2 | Server-track increment:<br>- iteration `baseSha` and `headSha`, and the `manual`, `push` and `recheck` triggers<br>- intake rules for `GitHub` and `Local`<br>- Refresh behaviour<br>- 403 and 503 codes<br><br>Planned, not served yet:<br>- a run-level `failureReason` (today it is in the failed node's `summary`)<br>- the all-repos `POST /api/review/refresh` (design §8.5) |

## Not in this slice

Trust scores, teams/org sync, checks catalogue, pack viewer, stats rollups, prompt layers, the learn
loop and seeding. Their screens stay as Storybook proposals until their APIs exist.
