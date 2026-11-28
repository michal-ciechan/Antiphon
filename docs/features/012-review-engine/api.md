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
  existing?: boolean                       // intake responses only
  concurrencyToken: number                 // opaque PostgreSQL row version
  closure: { actor: string; reason: string; at: string } | null
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

interface WorkflowValidationDto { valid: boolean; diagnostics: { severity: 'Error' | 'Warning'; code: string; message: string; nodeIds: string[]; edge: { from: string; to: string } | null }[] }
```

## Routes

| Method | Route | Body / query | Returns |
|---|---|---|---|
| GET | `/api/review-runs` | `?status=&repo=&stage=&q=` | `{ items: ReviewRunSummaryDto[], total }` |
| GET | `/api/review-runs/kpis` | | `ReviewKpisDto` |
| POST | `/api/review-runs` | `{ host, repo, prNumber?, baseSha?, headSha?, title? }`. GitHub accepts `owner/name` with `prNumber`, `owner/name/number` or a github.com PR URL in `repo`. If both forms supply a number, they must match. Local requires `baseSha` and defaults `headSha` to `HEAD`. | `ReviewRunDetailDto` with `existing: false` and `Location` (201), or the same detail with `existing: true` (200) for an open run with the verified GitHub identity. 409 `legacy_review_run_open` includes `legacyRunId` and `recovery`. |
| POST | `/api/review-runs/demo` | | `{ items: ReviewRunSummaryDto[], total }`. Seeds about 10 realistic runs. Enabled only when `Review:DemoSeedEnabled` is true (on in Development); otherwise 404, which the UI uses to hide "Load demo data". The executor never picks up demo runs. |
| GET | `/api/review-runs/{id}` | | `ReviewRunDetailDto` |
| GET | `/api/review-runs/{id}/findings` | `?status=` | `{ items: ReviewFindingDto[], total }` |
| POST | `/api/review-runs/{id}/refresh` | | 202. Re-reads the PR (the manual Refresh): a moved head starts a `push` iteration (next round); a failed run with an unchanged head starts a `recheck` iteration; demo runs are only re-announced. |
| POST | `/api/review-runs/{id}/close` | `{ concurrencyToken, reason }`. Use the run detail token and a reason of up to 4,000 characters. When GitHub sign-in is enabled, the viewer needs the repository write grant. | 200 `ReviewRunDetailDto` with `Closed`, stage `Done` and the closure audit. Repeat closes return the existing detail and preserve the original audit. A stale token on a run that is still open returns 409 `review_run_changed`. |
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

## GitHub run lifecycle

GitHub identity consists of the configured REST API root, the numeric ID of the PR's base repository and the PR number. Intake confirms that identity with GitHub before reusing a run. A rename or transfer on the same authority preserves the ID, and reassigning the old slug to a different repository gives it a separate run. A different head SHA or title keeps the same run. Demo records and local commit-range reviews use independent intake. A GitHub run in Merged continues to occupy its identity until Closed.

Manual intake returns the existing run without queuing a round. Use Refresh to retry a failure or Request review round to start another round. The GitHub poller uses the same intake service and keeps working on the existing run. A poll through the current repository binding can refresh and update its stored slug after a rename or transfer. An ordinary poll does not automatically restart a deliberately closed run while the PR remains open. A trusted PR comment or manual intake can start the next run. The existing code-host close event also transitions the run to Closed.

An explicit close removes open gates from the queue, skips outstanding workflow steps and records the viewer, reason and time. Reviewer task creation saves the run, node and attempt link with the task before dispatch. The queue takes a shared lock on the linked run for each submitting Enter, including recovery and retries. On a closed run it keeps any confirmed transcript receipt and cancels an unsubmitted brief. A generation-bound clear removes an identifiable draft. If composer ownership is uncertain, the canceled attempt holds the composer until existing evidence or session termination clears it.

Close uses the delegated-task cancellation path, including tasks linked to legacy runs. The server worker starts a pass every ten seconds, scans at most 16 linked tasks per pass, and budgets ten seconds for the pass and up to eight seconds per release attempt. The scan advances past an unreachable runner and returns to it on a later cycle. Closed and blocked reviewers are eligible immediately. Other reviewers become eligible at their recorded execution deadline, which defaults to 90 minutes. These limits apply independently of workflow polling and parking. A persisted `Stopping` intent stays with session reconciliation if the runner has yet to acknowledge exit. Manual intake can proceed once the operator's close commits, while reconciliation can still be releasing a seat.

Each GitHub write bound to a run takes a shared lock on that run and checks its status before sending. This includes individual merge retries and poller replies about a specific run. Close and workflow saves take the run lock before touching its child rows. A confirmed GitHub review POST records its local receipt and finding links before the executor checks whether close superseded the publishing step. If GitHub cannot return the comment details, the accepted review URL is retained. Writes require a verified numeric repository ID.

### Database activation

Before starting a server build that includes `EnforceOneOpenReviewRunPerPullRequest`, pass the PostgreSQL snapshot and restore-drill gate. For production on server2, take a fresh snapshot with `scripts\antiphon.cmd postgres-snapshot --execute --plain --target server2` and verify its restore using `scripts\antiphon.cmd postgres-snapshot --execute --plain --restore-drill --target server2`. Record both receipts for the activation, and drain old review writers before starting the new version. [Production PostgreSQL backup and restore](../../docker-stack.md#server2-host-postgres-card-1406)

The startup migration adds GitHub identity columns and installs partial unique indexes for live and historical keys. Existing rows have no repository ID. The migration groups them by the lowercased slug, PR number and historical authority inferred from the PR URL or the legacy input URL. GitHub.com and missing URLs use the public API root. An enterprise web origin supplies its standard `/api/v3` root because historical data contains no proxy API path. The migration identifies historical demos by their completed intake node and unpinned first iteration, which the demo seeder used for its synthetic graphs.

At activation, historical GitHub runs without a verified repository ID remain visible with their original state and history. The server logs their count at startup. The executor leaves unresolved runs parked. Polling reports their IDs and states in the repository's `error`.

In the same transaction the migration keeps the newest non-Closed run per key by start time and ID and closes older duplicates with the reason `superseded by duplicate-intake cleanup`. All runs, subjects, findings and workflow history remain. The audit names the surviving run and captures its predecessor's prior status, stage, waiting state and timestamp. A five-second lock timeout aborts the transaction if a lock cannot be acquired. Rehearse the migration on the restored copy to measure the review-table lock window. A disposable PostgreSQL 16 rehearsal with 200,000 synthetic tasks and 2,000 review runs measured about 510 ms from the first review-table DDL to commit and about 507 ms on the task table. Measure both windows against the restored production shape before activation.

The migration also adds reviewer task linkage and backfills handles that can be matched to the run and a recorded task brief. Rehearse lock acquisition on both the review and task tables.

Down removes the identity and reviewer task link columns, indexes and foreign key. The closure records and run history survive the downgrade. Use the pre-activation snapshot for a full data rewind.

### Recover an unresolved GitHub run

Intake first reuses any open run with the verified authority, numeric repository ID and PR number. If it would create a run, it checks unresolved history on the same authority and PR number. It compares the historical slug with the requested `owner/repo` and the current slug returned by GitHub, ignoring case. If a slug was reused after a rename, the current lookup verifies only the current occupant. Matching unresolved history still holds intake until the historical identity is established or the operator closes the old run. Once both repository IDs are verified, separate IDs can have separate runs for the same slug and PR number.

An open historical match returns 409 `legacy_review_run_open` with its `legacyRunId` and this recovery location. Manual intake receives the HTTP error. Trusted comment commands report the same code in the polling pass's repository `error`. The hold creates no run or task and makes no GitHub write. Automatic intake remains held for matching closed legacy history as well.

1. Inspect the run at `GET /api/review-runs/{id}` using the reported ID.
2. To recover the original identity, obtain independent evidence of the original authority, numeric repository ID and PR number from records such as a historical webhook or repository audit export. Use that evidence in a reviewed data repair.
3. To close the historical run, use `POST /api/review-runs/{id}/close` with the current token and a reason, or `scripts/antiphon review-run close <id> --reason "<reason>"`. With GitHub sign-in enabled, the operator needs the repository write grant. After an authorized operator closes it, manual intake can create a run (201) with a verified repository ID. A trusted comment can also request a new run. Scheduled and on-demand polling continue to hold automatic creation for the closed history.

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
