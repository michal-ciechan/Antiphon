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
- Errors are Problem Details from the `HttpException` hierarchy: 404 unknown id, 409 stale or illegal
  transition, 422 validation.
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
  baseRef: string; headRef: string; baseSha: string; headSha: string
  owners: string[] /* code-owner teams/users of the changed paths */
}

interface BlastRadiusDto { reach: number; externalContract: number; data: number; rollbackAtRelease: number; reversalAfterMonths: number; exposure: number } // 1-5 each, or null when not categorised

interface ReviewRunSummaryDto {
  id: string; subject: ReviewSubjectDto; status: ReviewRunStatus; stage: ReviewStage
  round: number; botOnlyRounds: number; needsHumanApproval: boolean | null
  openFindings: number; openBlockers: number
  models: string[] /* distinct resolved models used, e.g. 'jev-1.14.0', 'gpt-5.6-luna' */
  costUsd: number; startedAt: string; updatedAt: string
  waitingOn: 'Author' | 'Humans' | 'Bot' | null
}

interface NodeExecutionDto {
  id: string; nodeId: string; iteration: number; kind: NodeKind; status: NodeStatus
  provider: string | null; model: string | null; transport: Transport | null
  inputTokens: number; outputTokens: number; costUsd: number
  startedAt: string | null; completedAt: string | null; summary: string | null
}

interface ReviewRunDetailDto extends ReviewRunSummaryDto {
  blastRadius: BlastRadiusDto | null; categories: string[]
  workflowRevisionId: string | null
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
  status: FindingStatus; roundRaised: number; posted: boolean; syncState: SyncState
  externalThreadUrl: string | null; events: FindingEventDto[]
}

interface FindingResponseRequest {
  action: FindingAction; reasonCategory?: ReasonCategory; scope?: DismissScope; text?: string
}

interface ReviewGateDto {
  id: string; runId: string; kind: GateKind; status: QueueItemStatus
  teamKey: string | null; assigneeLogin: string | null; openedAt: string; decidedAt: string | null
  decision: GateDecision | null; steerPrompt: string | null; reasons: string[] /* why a human is needed */
  passedBy: string[]
}

interface GateDecisionRequest { decision: GateDecision; steerPrompt?: string }

interface QueueItemDto {
  gate: ReviewGateDto; run: ReviewRunSummaryDto; ageWorkingMinutes: number; slaState: 'Ok' | 'Warn' | 'Breached'
}

interface ReviewKpisDto {
  inReview: number; waitingOnHumans: number; averageRounds: number; costTodayUsd: number
  costPerAcceptedFindingUsd: number | null
  modelUsage: { model: string; provider: string; transport: Transport; calls: number; costUsd: number }[]
}

interface WorkflowGraphDto {
  name: string; revisionId: string | null; hash: string
  sections: { id: string; title: string; nodeIds: string[] }[]
  nodes: { id: string; kind: NodeKind; run: string | null; description: string | null; params: Record<string, unknown> }[]
  edges: { from: string; to: string; label: string | null; maxIterations: number | null; isBackEdge: boolean }[]
}

interface WorkflowValidationDto { valid: boolean; diagnostics: { code: string; message: string; nodeIds: string[]; edge: { from: string; to: string } | null }[] }
```

## Routes

| Method | Route | Body / query | Returns |
|---|---|---|---|
| GET | `/api/review-runs` | `?status=&repo=&stage=&q=` | `{ items: ReviewRunSummaryDto[], total }` |
| GET | `/api/review-runs/kpis` | | `ReviewKpisDto` |
| POST | `/api/review-runs` | `{ host, repo, prNumber?, baseSha?, headSha?, title? }` (manual intake) | `ReviewRunDetailDto` (201) |
| GET | `/api/review-runs/{id}` | | `ReviewRunDetailDto` |
| GET | `/api/review-runs/{id}/findings` | `?status=` | `{ items: ReviewFindingDto[], total }` |
| POST | `/api/review-runs/{id}/refresh` | | 202; asks the code host for new events (the manual Refresh) |
| POST | `/api/review-findings/{id}/responses` | `FindingResponseRequest` | `ReviewFindingDto` |
| POST | `/api/review-runs/{id}/responses` | `{ responses: (FindingResponseRequest & { findingId })[] }` (one submitted review) | `{ items: ReviewFindingDto[] }` |
| GET | `/api/review-queue` | `?scope=Mine\|Teams\|All&login=` | `{ items: QueueItemDto[], total }` |
| POST | `/api/review-gates/{id}/assign` | `{ login? }` (omit = random, excluding the author and anyone who passed) | `ReviewGateDto` |
| POST | `/api/review-gates/{id}/pass` | `{ reason }` ("not me") | `ReviewGateDto` |
| POST | `/api/review-gates/{id}/decision` | `GateDecisionRequest` | `ReviewRunDetailDto` |
| GET | `/api/review-workflows/default` | | `WorkflowGraphDto` |
| POST | `/api/review-workflows/validate` | `WorkflowGraphDto` | `WorkflowValidationDto` |

## SignalR

`ReviewRunChanged { runId }` → invalidates `['reviewRuns']`, `['reviewRun', runId]`,
`['reviewRun', runId, 'findings']`, `['reviewQueue']`, `['reviewKpis']`.

## Not in this slice

Trust scores, teams/org sync, checks catalogue, pack viewer, stats rollups, prompt layers, the learn
loop and seeding. Their screens stay as Storybook proposals until their APIs exist.
