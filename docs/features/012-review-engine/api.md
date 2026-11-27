# Review engine API contract (v0.5)

Status: working contract for the first implementation slice, 2026-11-22; v0.4 (failure policy) and v0.5 (polling and
manual rounds) 2026-11-23. The client builds against
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
  | 'FailedContinued' // v0.4: failed, and its onFailure: continue policy let the run go on (design 3.4.1)
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
  trigger: string /* 'opened' (polling found the PR) | 'manual' | 'push' (Refresh saw a new head) | 'recheck' (Refresh of a failed run, or a requested round) | 'comment:<id>' (a PR comment command) | 'another-round' | 'base-moved' … */
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
  edges: { from: string; to: string; label: string | null; maxIterations: number | null; isBackEdge: boolean
           overflow: string | null /* gate opened past maxIterations; required on limited back edges */; description: string | null }[]
}

interface ReviewRoundRequest { steerPrompt?: string /* at most 4,000 characters */ }

interface ReviewPollingDto {
  enabled: boolean /* the timer; a manual poll works either way */
  intervalSeconds: number /* at least 30 */
  commandPrefix: string /* '/antiphon' */
  repos: { repo: string; host: ReviewHost; baseBranches: string[] /* empty = all */; includeDrafts: boolean
           skipLabels: string[] /* default ['antiphon:skip'] */; requireLabel: string | null }[]
  running: boolean /* a pass is running now */
  lastPass: ReviewPollingPassDto | null /* in memory: null until the first pass since the server started */
}

interface ReviewPollingPassDto {
  trigger: 'timer' | 'manual'; startedAt: string; finishedAt: string
  ok: boolean /* no repo reported an error */
  repos: { repo: string; openPullRequests: number; runsStarted: number; runsRefreshed: number; commandsHandled: number
           error: string | null /* the last error the pass hit for this repo: rate limit, 403, network */ }[]
}

interface WorkflowValidationDto { valid: boolean; diagnostics: { severity: 'Error' | 'Warning'; code: string; message: string; nodeIds: string[]; edge: { from: string; to: string } | null; field?: string }[] }
```

The `Publish` node accepts `params.inputs` as an array of case-sensitive producer node IDs. An
explicit list requires one `CollateFindings` and one `VerifyFindings` producer and accepts one
`Categorise` and one `SelectChecks` producer each. `params.summaryTemplate` is an optional string
with bounded, named slots. Validation uses `invalid-input`, `unavailable-input`, or
`invalid-summary-template` with the affected Publish node and its `field`. See the
[worked Publish example](getting-started.md#configuring-the-pr-review-summary).

## Routes

| Method | Route | Body / query | Returns |
|---|---|---|---|
| GET | `/api/review-runs` | `?status=&repo=&stage=&q=` | `{ items: ReviewRunSummaryDto[], total }` |
| GET | `/api/review-runs/kpis` | | `ReviewKpisDto` |
| POST | `/api/review-runs` | `{ host, repo, prNumber?, baseSha?, headSha?, title? }` (manual intake through `ICodeHost`). `GitHub` needs `prNumber`. `Local` needs `baseSha` (any ref); `headSha` defaults to `HEAD`. The title defaults to the PR or commit title. | `ReviewRunDetailDto` (201) |
| POST | `/api/review-runs/demo` | | `{ items: ReviewRunSummaryDto[], total }`. Seeds about 10 realistic runs. Enabled only when `Review:DemoSeedEnabled` is true (on in Development); otherwise 404, which the UI uses to hide "Load demo data". The executor never picks up demo runs. |
| GET | `/api/review-runs/{id}` | | `ReviewRunDetailDto` |
| GET | `/api/review-runs/{id}/findings` | `?status=` | `{ items: ReviewFindingDto[], total }` |
| POST | `/api/review-runs/{id}/refresh` | | 202. Re-reads the PR (the manual Refresh): a moved head starts a `push` iteration (next round); a failed run with an unchanged head starts a `recheck` iteration; demo runs are only re-announced. |
| POST | `/api/review-runs/{id}/rounds` | `ReviewRoundRequest` (body optional) | `ReviewRunDetailDto`. A manual review round, same round, the steer stored on the run for the packs. A run waiting at `await-fixes` completes it with `recheck`; an approved, awaiting-merge or failed run starts a `recheck` iteration at the current head (the parked wait is superseded and `antiphon/approval` goes back to pending, as a push revokes it). 409 `review_run_at_gate` at a gate (decide it with `AnotherRound` and a steer instead), `review_run_finished` (merged or closed), `review_run_busy` (queued or running), `review_run_not_walked` (demo data); 422 for a steer over 4,000 characters; 503 when the code host fails. |
| POST | `/api/review-findings/{id}/responses` | `FindingResponseRequest` | `ReviewFindingDto` |
| POST | `/api/review-runs/{id}/responses` | `{ responses: (FindingResponseRequest & { findingId })[] }` (one submitted review) | `{ items: ReviewFindingDto[] }` |
| GET | `/api/review-queue` | `?scope=Mine\|Teams\|All&login=` | `{ items: QueueItemDto[], total }` |
| POST | `/api/review-gates/{id}/assign` | `{ login? }` (omit = random, excluding the author and anyone who passed) | `ReviewGateDto` |
| POST | `/api/review-gates/{id}/pass` | `{ reason }` ("not me") | `ReviewGateDto` |
| POST | `/api/review-gates/{id}/decision` | `GateDecisionRequest` | `ReviewRunDetailDto` |
| GET | `/api/review-workflows/default` | | `WorkflowGraphDto` |
| GET | `/api/review-workflows/revisions/{id}` | | `WorkflowGraphDto` (the snapshot a run used) |
| POST | `/api/review-workflows/validate` | `WorkflowGraphDto` | `WorkflowValidationDto` |
| GET | `/api/review-polling` | | `ReviewPollingDto` (design 8.5 Polling). |
| POST | `/api/review-polling/poll` | | `ReviewPollingDto` after one pass over every configured repo, run now even when the timer is off. Waits for a pass already running instead of overlapping it; a pass cut short at 90 s records the repos it reached. A repo's failure is in its `error`, not the status code. |
| GET | `/api/review-me` | | `ReviewViewerDto`. With sign-in on (`Review:Auth:Mode` GitHub), the signed-in GitHub login. With it off, the server resolves the viewer from `Review:DefaultViewerLogin`, overridable by the `X-Review-Viewer` header when `Review:AllowViewerHeader` is true (dev only). Mutations record this viewer as the actor. |
| GET | `/api/review-auth/status` | | `{ mode: 'None' \| 'GitHub', signedIn, login, displayName }`. Always open. |
| GET | `/api/review-auth/login?returnUrl=` | | 302 to GitHub's authorize page, with a one-time state in a 10-minute cookie. 404 while sign-in is off. Always open. |
| GET | `/api/review-auth/callback?code=&state=` | | Checks the state, reads the login, and sets the session cookie (302 to `returnUrl`, a path on this site). 400 for an expired or forged state, 403 (an HTML page) when the person may not sign in, 502 when GitHub fails. Always open. |
| POST | `/api/review-auth/logout` | | Clears the session; answers with a "Signed out" page. |

With sign-in on, every other `/api` route and `/hubs` answers **401** `{ error: 'sign_in_required', signInUrl }` without a session; the client sends the browser to `signInUrl` with `returnUrl` set to the current page. A gate on a repo where the viewer lacks `Review:Auth:RequiredRepoPermission` has `viewerCanDecide: false` and says why, and deciding it is a 403.

## SignalR

`ReviewRunChanged { runId }` → invalidates `['reviewRuns']`, `['reviewRun', runId]`,
`['reviewRun', runId, 'findings']`, `['reviewQueue']`, `['reviewKpis']`.

Polling has no SignalR event of its own: each run a pass starts or moves publishes
`ReviewRunChanged`, and a pass that finds nothing new publishes nothing. The client refetches
`['reviewPolling']` every minute and takes the poll route's result.

## Changelog

| Version | Change |
|---|---|
| v0 | First contract |
| v0.1 | Client-track proposals accepted:<br>- `itemKey` and `outcome` on node executions<br>- `iterations[]`<br>- gate: `allowedDecisions`, `viewerCanDecide` and reason, `decidedBy`, `passes[]` (replaces `passedBy`), `assigneeDisplayName`<br>- section `collapsed`, and a GET by revision<br>- blast radius and the actual/estimated cost split on the summary<br>- `slaWorkingMinutes`<br>- owners as `{kind, name}`, and `openedByLogin`<br>- finding `hunk`<br>- diagnostic `severity`<br>- `GET /api/review-me` |

| v0.2 | Server-track increment:<br>- iteration `baseSha` and `headSha`, and the `manual`, `push` and `recheck` triggers<br>- intake rules for `GitHub` and `Local`<br>- Refresh behaviour<br>- 403 and 503 codes<br><br>Planned, not served yet:<br>- a run-level `failureReason` (today it is in the failed node's `summary`)<br>- the all-repos `POST /api/review/refresh` (design §8.5) |

| v0.3 | The executor now walks the whole workflow to merge:<br>- a gate decision returns the run while the executor resumes it, so Approve on a walked run reads `Queued` until the next node runs<br>- Refresh is asynchronous: its 202 can return before the new `push` iteration exists, and the client follows `ReviewRunChanged`<br>- a response's `syncState` moves from `Pending` to `Synced` once the executor posts it, to `NotApplicable` on hosts without threads, or to `Failed`<br><br>Planned, not served yet:<br>- node-execution `outputs` (JSON)<br>- a finding `placement` (`Inline`, `SummaryOnly` or `BelowBar`)<br>- update-branch opt-in and recheck routes<br>- a run-level `failureReason` |

| v0.4 | Failure policy (design 3.4.1):<br>- node status `FailedContinued`<br>- node params `onFailure` (`continue` or `stop`) and `fallback`<br>- a refused commit status is a line in the step's `summary`, not a failure<br>- a failed run sets `antiphon/review` to `error`<br>- `Review:Policy:AuthorMayDecideRepos` lets a listed repo's PR author decide its own gates (`viewerCanDecide`) |

| v0.5 | Polling and manual rounds:<br>- `POST /api/review-runs/{id}/rounds` and `ReviewRoundRequest`<br>- `GET /api/review-polling`, `POST /api/review-polling/poll` and `ReviewPollingDto`<br>- the `opened` trigger for a run polling started, and `comment:<id>` for one a PR comment command started or re-rounded |

| v0.6 | Sign-in with GitHub (`Review:Auth`):<br>- `/api/review-auth/status`, `login`, `callback` and `logout`<br>- 401 `sign_in_required` on every other route while sign-in is on<br>- `viewerCanDecide` also needs repo access while sign-in is on |

## Not in this slice

Trust scores, teams/org sync, checks catalogue, pack viewer, stats rollups, prompt layers, the learn
loop and seeding. Their screens stay as Storybook proposals until their APIs exist.
