# CARD-0599: Release recovery, publication authority and release cards

Date: 2026-09-23. Stage: Plan addendum. Task: `97e91f92`.
Inspected baseline: `25564530cd51ff9b02e5b8c9a5ac4793225d2541`.
Next: **Code, B1**. Round-2 TestDesign is appended below; its executable specifications
are commissioned work, not a claim that unimplemented tests have passed.

This addendum resolves A-1..A-4 from TestDesign `3c8db8ad` and the operator's
release-card requirement. It is the controlling activation design and checkpoint
proposal. The [original plan](2026-09-22-card-0599-release-gate-activation-plan.md)
retains the historical implementation, inventories and outstanding controls.

Precedence: these decisions replace the RC portions of old D-3/D-4/D-7/D-8,
S3/S6, CP-19, DL-6..8 and Q-1..4 where they differ. RC scheduling is Hangfire on
the main instance. No Windmill RC registration, SSH bridge or Scheduled Task.
Existing master/nightly/readiness behavior and S5's separate Interim qualification
remain unchanged. Dormant implementation and live activation are separate: this
dispatch changes documentation only; subsequent Code rounds ship disabled.

## Ground truth

Read the live CARD-0599 on board `8988ca03-7414-47ad-b0b6-51556c701703`, card GUID
`e09ed000-9ea5-4f5e-865f-aaf299a20089`, revision count 13, updated
`2026-09-23T22:28:59.001292Z`. Its latest requirement is one release card per RC,
an independent release workflow, coordinator-owned moves and normal linked Backlog
fix cards. It also explicitly replaces RC Windmill scheduling with main-instance
Hangfire. No production settings, schedules, cards, credentials or releases changed.

| Assumption / blocker | Inspected code does | Design consequence |
|---|---|---|
| A-1: a queued Hangfire job survives restart | `server/Program.cs:722` uses `UseInMemoryStorage`; `Infrastructure/Agents/HangfireConfiguration.cs` only registers census/residue. `HangfireSettings` documents process-lifetime history. | PostgreSQL owns durable intents/outbox; Hangfire is a replaceable wake-up transport. |
| A-2: real GitHub readback matches the tests | `scripts/lib/release-gate.ps1`, `Invoke-ReleaseGateGitHub`, discards stdout and returns no Body; uploads use `--clobber`. `publish-release.ps1` defaults missing final draft state to false. | Replace the actual process adapter; strict schema and independently downloaded bytes are mandatory. |
| A-3: publication already enforces pinned policy | `release-cut.ps1` supplies no required-suite/hash authority; publication accepts optional values and any passing row per suite. | Load the policy blob at the pinned SHA, freeze discovery/execution membership, validate every chunk/UID; reports confer no authority. |
| A-4: cut/test/publish share one lock | `release-cut.ps1` cuts before `nightly-run` acquires its lock and returns early without a reporter. The runner releases the shared lock before publication. | One coordinator-owned lock and child lifetime spanning all phases, with durable outcomes even before native start. |
| Correlation is scheduler-neutral | `release-cut.ps1` writes its JobId as `windmillJobId` and does not forward it to native execution. | Version the RC envelope and carry intent, attempt, Hangfire job, candidate and run IDs end to end. |
| `tracker:` defines release execution stages | `docs/workflow-tracker-block.md` and `IssueTrackerConfigParser` describe external issue synchronization. `CardWorkflowRunFactory` instead creates agent/template stages and requires AgentId. | Use a Release workflow on the existing Antiphon board; neither tracker synchronization nor agent workflows execute releases. |
| Renaming standard columns is sufficient | `BoardService.CreateAsync` creates default Code columns. `BoardColumn` can share CardStatus values, but `CardService.ApplyAutomatedMoveAsync` selects by status and ignores same-status moves. | Explicit workflow-owned columns and column-ID transitions are required. |
| A release card will naturally stay outside Code | `OrchestratorService` selects active columns; manual spawn/task binding and `CardWorkTransitionService` have their own paths. A hold alone can be cleared. | Add persisted card/column workflow types and enforce them at pickup, depth, concurrency, binding and transition boundaries. |
| Failure reporting already supplies one durable release card | `nightly-report.ps1` manages lane incidents, not one immutable candidate/card relation or a transactional stage outbox. | RC outcomes project onto their exact release card; master incident behavior stays intact. |
| Full RC fits one test invocation | Existing policy supports disjoint class chunks and expanded-UID reconciliation. TestDesign estimates 120 minutes for Antiphon.Tests versus a 60-minute per-process watchdog. | Freeze bounded chunks from discovery; never raise the watchdog or drop slow cases to fit. |

Owners read: project context, orchestration loop, card lifecycle, workflow tracker,
HTTP operations, testing/build, release gates and the Hangfire section of bootstrap.
These are source findings, not reproduced failing tests. Historical live census
claims still require a fresh activation-time check.

## Decisions

These are selected implementation decisions, with reasons and rejected alternatives.
They do not require a new product decision before TestDesign. Values marked defaults
are configurable deployment defaults, not permission to activate production.

### D-11: PostgreSQL owns scheduling intents; Hangfire owns wake-ups (A-1)

Keep the existing Hangfire storage provider. Do not migrate every unrelated job to
a new provider as part of this card. Add CLI-generated EF migrations and entities:

| Durable row | Required fields / uniqueness |
|---|---|
| `ReleaseGateRegistration` | Id; canonical repository/root, ProjectId, BoardId (release and fixes share it), ReleaseWorkflowVersion and exact stage column IDs; enabled-window history and scan cursor; configuration version; persisted ScheduleEnabled/AdmissionEnabled/PublicationEnabled; recurring ID, timezone/cron; last observed main host identity. Unique repository + project. |
| `ReleaseGateIntent` | Id; registration; provenance (`scheduled`/`manual`); trigger key; due UTC and London local slot when scheduled; created/updated; state/version; candidate ref/id/SHA, policy authority digest, native RunId; ReleaseCardId; optional predecessor/successor; attempt owner/fence; outcome. Unique registration + trigger key, unique non-null candidate ref, unique ReleaseCardId. |
| `ReleaseGateAttempt` | Id; intent; generation; Hangfire job ID, host boot ID, PID/start identity, named native Job Object, start/exit/cleanup facts, imported evidence digests and paths, failure. Unique intent + generation. Hangfire job IDs are correlation only. |
| `ReleaseGateOutbox` | Id; intent; kind (`Execute`, `ProjectCard`, `FileFix`, `ReconcilePublication`); immutable sequence/body/digest; attempts, next due, last error; receipt. Unique intent + kind + sequence. |
| `ReleaseGateFixLink` | Intent, finding fingerprint, actual fix CardId/BoardId, evidence identity, disposition. Unique intent + fingerprint. |

Use the application's PostgreSQL/AppDbContext, not a second database or a JSON file
as queue authority. JSON journals remain native evidence and remote-write recovery
records. A DB/file mismatch is held for reconciliation, never resolved by choosing
the newer timestamp. No transaction stays open across a queue, process, git or HTTP call.

Admission commits intent, its initial Cut card and Execute outbox in **one DB
transaction**. Allocate identifiers with `CardIdentifierAllocator`, under a board
row lock; retry a transaction conflict against fresh rows. An uncommitted intent
has no enqueue or process side effects. Only after commit enqueue `Execute(intentId)`.
Queue response loss and in-memory queue loss are normal duplicate-delivery cases.
Do not mark the outbox complete just because enqueue returned an ID.

`ReleaseGateRecoveryJob` runs through Hangfire once per minute, plus an enqueued
startup pass. It scans at most 100 due obligations per invocation, ordered by due
time/id, with a 30-second cooperative scan budget and keyset pagination. It records
next-attempt time before another enqueue (bounded 1/2/5-minute transport backoff).
Each worker atomically claims the expected intent version/generation. Duplicate
jobs return the already recorded state. SQL claiming prevents duplicate logical
work; the native lock and verified child ownership prevent overlapping side effects.
An expired heartbeat alone never permits taking over a live process.
The pump skips a claimed attempt while its owner is demonstrably live, so it does
not enqueue one duplicate per minute during a five-hour run. Claim/start receipts
retire the Execute delivery obligation; the intent remains recoverable until a
durable terminal or held outcome. Unknown ownership is a hold, not another enqueue.

Register two small jobs on the existing default worker: `antiphon:release-slots`
(`30 8,16 * * *`, Europe/London) and `antiphon:release-recovery` (`* * * * *`, UTC).
Put the long `ReleaseGateExecutionJob` on `release-gates`, with one additional
main-instance worker restricted to that queue. Keep existing maintenance jobs able
to run while the full RC occupies that worker. `Hangfire:ServerEnabled=false`
starts neither worker. All release jobs use `AutomaticRetry(Attempts=0)`; retries
come from durable state, not Hangfire replay of arbitrary whole-run methods.

The slot job and recovery job call the same durable due-slot scanner. It computes
08:30/16:30 Europe/London occurrences from enabled windows and TimeProvider; stores
UTC due instant, local date/time and zone as the unique scheduled trigger key.
Only occurrences inside an enabled window are eligible. Default late-start grace
is 15 minutes: an unadmitted older slot becomes a durable `missed-slot` outcome,
not a backlog of full runs. A committed intent is always reconciled even after
that grace expires. Startup records missed slots since the persisted cursor;
initial enablement starts at its actual effective time, with no historical catch-up.

The manual API requires an idempotency key and always creates `manual:<key>`.
It cannot supply a scheduled provenance/slot. Dashboard invocation of the scanner
only scans legitimately due slots; it is not the manual cut front door and cannot
manufacture an occurrence. September and DST-boundary UTC expectations from V-10
remain. Same slot/key always yields the same intent and card.

Reject: trusting in-memory job IDs, filesystem-only intents, holding a DB transaction
over enqueue, a new global Hangfire provider migration, and using Orchestrator ticks.
The durable outbox limits this change to the release feature and exposes recovery
directly to tests with fresh hosts and fresh Hangfire stores against the same DB.

### D-12: Main-instance admission and explicit controls (A-1)

New `ReleaseGates` typed settings ship `Enabled=false`, with null canonical root,
project and board identifiers. Defaults: roots `C:\Antiphon\releases` and
`C:\Antiphon\verification`, the cron/zone above, late grace 15 minutes. The static
Enabled flag is the master safety gate; persisted registration flags provide
auditable enable/disable without rewriting config. Defaults for all three flags
are false. Do not infer main identity from localhost, environment name or port.
Setup validates root/project/the existing Antiphon BoardId first and adds the Release
workflow columns and registration association in one transaction. It creates no board.
Operational admission requires the persisted workflow association and full shape
validation; setup needs neither invented column GUIDs nor enabled scheduling.

Admission validates: server worker enabled; feature enabled; git top-level resolved
from the actual server ContentRootPath equals the configured canonical main checkout
after resolved-path/reparse checks (not a root supplied by an API caller);
not a linked worktree; expected remote repository; configured project owns that
repository; the shared board belongs to that project, with disjoint Code and Release
column identities. Resolve actual IDs during setup, never use a board ID as ProjectId.
Record code/configuration version and host boot ID. Recheck gates at dequeue, before
each new native phase and before each GitHub write. The production child receives
no inherited task token, live-test approvals or production runner override.

Add the following explicit front doors, using existing HTTP exception/DTO patterns:

| Route | Contract |
|---|---|
| `GET /api/release-gates` | Effective gates, identities, recurring-job readback, last scanned slots and pending counts; no credentials. |
| `POST /api/release-gates/setup` | Preview by default; `apply=true` plus expected configuration version adds Release workflow columns/registration to the existing board. Never converts Code columns. Same matching setup is idempotent; foreign name/shape collision refuses. |
| `POST /api/release-gates/control` | Versioned admission/schedule/publication changes with reason. Returns persisted state and actual registration readback. Removing a recurrence is distinct from disabling new admission. |
| `POST /api/release-gates/runs` | Manual request with requestId and optional predecessor intent; returns durable intent/card IDs. No arbitrary script/command/root/ref/SHA overrides. |
| `GET /api/release-gates/runs/{id}` | Intent, attempts, card, evidence/report/publication statuses and fix links. |
| `POST /api/release-gates/runs/{id}/resume` | Idempotent recovery of transport/report/publish-pending work only; never retries a terminal failed test run or repins a candidate. |
| `POST /api/release-gates/runs/{id}/abandon` | Reasoned coordinator command; queued work cancels, owned work is joined before terminal Canceled. Remote publication must be reconciled before declaring abandonment. |

Setup/control/manual operations are local operator front doors, guarded consistently
with existing loopback operational endpoints; delegated capabilities do not acquire
arbitrary release-control rights. The recovery job uses internal services. A CLI
`scripts/release-gates.ps1` exposes `status`, `setup`, `enable`, `disable`, `run`,
`resume`, `abandon`, file-based setup input, explicit apply/version and JSON results.
This is commissioned implementation, not a claim that these routes exist now.

Disable removes future slot admission; queued/unstarted work becomes held-disabled.
An already running phase finishes under ownership; no next native phase or remote
write begins while disabled. Receipt import/card projection continues, including
recognition of a publication that already happened. Re-enabling does not automatically
restart held work; explicit resume is recorded for both provenances. Missed scheduled slots remain
missed. Graceful shutdown cancels the owned native job and joins it; a forced shutdown
is covered by D-16. Disabling never deletes journals, releases or pending reports.

### D-13: Release workflow on the same Antiphon board (operator correction)

Release cards and normal fix cards share the existing **Antiphon** BoardId and its
identifier allocator. Add `CardWorkflowKind` (`Code=0`, `Release=1`) persisted on
`Card.WorkflowKind` and `BoardColumn.WorkflowKind`, exposed as `workflowKind` in
card/column DTOs. Existing rows migrate to Code. A release card's type is immutable;
only the coordinator can create Release cards. No `BoardPurpose` or second board.
These workflow types are distinct from agent `CardWorkflowRun` and tracker YAML.

Setup adds Release columns with namespaced StateKeys (`release-cut`,
`release-full-test`, `release-fix`, `release-publish`, `release-released`,
`release-closed`); the short keys in the table below are stage keys. Registration
persists their exact IDs and workflow version. Code columns, tracker settings and
active agent workflow definitions remain unchanged. Release cards use existing
`CardFileVisibility.Private`; setup does not change board-wide export settings.

| StateKey / column | Existing CardStatus | IsActive / IsTerminal | Release meaning |
|---|---|---|---|
| `cut` / Cut | Backlog | false / false | Durable accepted attempt; SHA/ref may still be absent. |
| `full-test` / Full test | InProgress | false / false | Remote cut read back at pinned SHA; full execution in progress. |
| `fix` / Fix | NeedsDecision | false / false | Tests/setup/evidence failed; linked normal Backlog work or explicit disposition required. |
| `publish` / Publish | Review | false / false | Test evidence and pre-publication board receipt accepted; publication pending/recovering. |
| `released` / Released | Done | false / true | Strict remote receipt and final card projection both persisted. |
| `closed` / Closed | Canceled | false / true | Failed, abandoned, superseded, missed, no-new-SHA or deferred-busy verdict, never release success. |

Stages are a `ReleaseGateStage` state machine, not `AgentTaskRole` additions. Happy
path is Cut -> Full test -> Publish -> Released. Failure path is Cut/Full test ->
Fix -> Closed with `failed` or `superseded` and linked successor. Publish stays Publish
on uncertain remote state. Explicit abandon closes only after remote reconciliation.
Fix is a visible triage stop: once failure and repair-card receipts exist it may close
with `failed`; a successor may be linked later without reopening or changing that verdict.

Create a card for every accepted intent, including a pre-cut failure, so there is
always a recipient. A no-new-SHA/busy attempt closes with `not cut` stated explicitly;
it is not counted as an RC. Every actual RC has exactly one card. Before SHA allocation
title it `Release attempt <short intent>`; after cut use `Release RC <candidate>`;
after CalVer reservation use `Release <version>`. The source SHA never changes on
that card. Candidate identity reservation retains the existing second-resolution
ref format under a DB uniqueness constraint. Collision with another intent refuses
as `candidate-name-conflict`; never adopts that intent's ref or invents a future cut time.

Implement a dedicated `ReleaseGateCardProjector` with an internal column-ID move
primitive that shares `CardRevisionLog` and normal timestamps/concurrency handling.
Do not call the status-selecting `ApplyAutomatedMoveAsync`. Exact prior stage/version,
column ownership and intent/card relation guard every transition. Coordinator writes
reason/evidence IDs, actor `release-coordinator`, new concurrency token and revision
in the same transaction as the projection receipt. Events publish via IEventBus
after commit. Replaying an event creates no second revision.
Set StartedAt on first Full test entry explicitly: these columns are deliberately
not agent-active, so the generic IsActive-based timestamp rule is insufficient.

Block Release card creation/content/stage/type edits through generic card APIs;
discussion remains available. Move/reopen/spawn/assign/enqueue, scheduled card actions,
task binding (including inherited/title binding) and agent workflows refuse the release
card with `release_coordinator_owned`. Code pickup explicitly requires both card and
column WorkflowKind=Code, even if a release hold is cleared or a column is marked
active. Code pipeline depth and per-stage concurrency projections filter Code cards;
release intents/jobs never create AgentTask roles or occupy a Code stage slot. Code
candidate/status-column queries, transition/retry sweeps and tracker synchronization
all filter the workflow, not the board. Generic move targets must match the card's
workflow. Task binding checks explicit GUID/identifier, inherited and title paths;
no task gets bound to a Release card. Fix cards are Code/Backlog on this same board.
Physical process/provider limits still count real running sessions, including corrupt
legacy bindings; workflow isolation must not weaken resource safety. Ordinary unbound
delegates retain their existing capacity rules.

Guard release-column edits/deletion and shared-board deletion/archive while a
registration/evidence relation exists; ordinary Code workflow edits remain possible.
Never cascade release history. No agent/session starts from a release-card move.
Reject a label-only lane because status-based automation would mix owners; reject
tracker YAML because it controls issue sync; reject CardWorkflowRun because it
assumes an agent/executor lifecycle.
No new general workflow engine, release agent role or frontend route is needed.
The existing board renders columns and descriptions; read-only release-specific API
facts are linked from the generated description. UI mutations may receive the clear
ownership refusal until a later UX improvement; they cannot change release state.

### D-14: Reports and fix cards are durable projections (A-4)

Persist every outcome event and its immutable body digest before enqueueing a
projection. Recipient is the exact GUID bound to this intent, never title/label search.
The projector updates the card and records `(intent, sequence, card, revision, digest)`
atomically. A fresh DbContext readback must match the complete generated body and
revision before delivery acknowledgement. Tests also read through the real GET API.
Queue success, an accepted HTTP response or IEventBus delivery is insufficient.

The release card contains repository/project/intent/provenance/slot/job attempts;
candidate ref and full SHA; previous release and commit range; included change/card
links; authority/policy/roster hashes; suite/chunk counts and failure identities; local
evidence references; fix/successor links; publication tag/id/URL; final verdict.
Included commits come from the frozen previous published SHA..candidate range.
Resolve included cards from confirmed landing-operation/card GUID relationships;
retain unmapped commits explicitly. A textual CARD-nnnn reference without its board
is not an authoritative included-card link. Non-ancestor previous release means the
range is unknown and requires explicit disposition, not a guessed changelog.

`FileFix` creates normal Code-workflow Backlog cards on the same **Antiphon board**,
without spawn/assignment/Release. Each failure group gets a stable fingerprint from
suite + class/method or infrastructure cause + failure kind, within that intent.
Persist the fix link with card creation in one transaction, guarded by unique keys
and the board identifier allocator. Replays return the same GUID; distinct RCs may
link an explicitly selected existing repair after validation, never merge by title.
Bodies name failing SHA/run/chunk, evidence, release GUID/board and required repair.
Do not infer a flaky waiver or blame an inherited red without targeted base evidence.

Source fixes land through normal Code/Review/master. A new request or scheduled cut
then pins new master, creates a new release card and runs all eight suites. Link the
old failed card, fix cards and successor; mark old outcome failed/superseded, not Done.
The release coordinator files work but never dispatches Code or waits inside a native
lock for humans. No publication is licensed by a fix card reaching Done.

Two receipts avoid a circular gate:

1. Full test finishes into `TestCompletedAwaitingReport`, **without** complete-green.
   Import evidence; project the full test result onto the card; obtain its readback
   receipt. Finalize the immutable sanitized summary and `complete-green.json` with
   that exact pre-publication receipt. The Publish stage can then start.
2. After remote publication, import the remote receipt, project final ID/tag/URL and
   Released verdict, then acknowledge final delivery. If projection fails the intent
   remains `PublishedAwaitingReport`; recovery cannot republish or allocate another tag.

Keep these receipt kinds distinct. RC `nightly-report.ps1` routes to the coordinator
event contract, not the old lane-wide incident create/close path. Standalone RC
invocation without a durable intent cannot mint report-delivered or publication credit.
Master reporting remains unchanged. Pre-native clone/identity/lock failures take the
same outbox path with null native IDs; no early return may bypass it. Missing/assigned/
foreign card, conflicting digest, API/DB outage or unknown revision holds delivery;
preserve any legacy assigned RC/nightly incident rather than adopting or closing it.
Startup/recovery drains pending obligations after an outage. No session/chat/email
delivery is added or claimed, and main-server outage detection remains external.

### D-15: Authority is the pinned policy and full execution ledger (A-3)

During Cut preparation, resolve `origin/master` once, then extract
`<full-sha>:tests/test-execution-policy.json` through git into the candidate's private
authority directory. Verify the blob against that commit, compute canonical policy
hash with `Get-NightlyPolicyHash` (excluding the policyHash field), and validate the
stated hash. Record both canonical hash and raw-file digest/blob identity. Freeze
repository/ref/SHA, profile=rc, coordinator version and executable script hashes.

The required suite set is exactly `antiphon, session-runner, pty-host, agents-pty,
messaging, client, scripts, e2e` for this activation. Missing/unknown profile/schema,
empty required set or any missing member refuses. A future suite-set change needs a
reviewed policy/design update, not a summary override. Persist policy exclusions with
owners, full discovery roster and its digest, and deterministic chunk manifest before
execution. New eligible tests remain required. Diagnostic subsets never earn credit.

`publish-release.ps1` loads and verifies this authority itself. Remove report fallbacks
and optional-empty bypasses; legacy `ExpectedPolicyHash`/`RequiredSuites`, if retained
for compatibility, are additional equality assertions only. An old candidate with no
authority cannot publish; recut after the repair rather than synthesizing authority.

Validate all evidence paths under the resolved owned candidate root with no reparse
escape. Before each publication attempt recheck checkout SHA, remote candidate SHA,
tag target if present, policy blob/hash and evidence digests. Require:

- Every expected build/prerequisite, client lint/build/Vitest and script-census entry
  succeeded, with native child exit 0 and nonzero relevant execution counts.
- Exactly the frozen chunk set, disjoint membership and exact required expanded-UID
  union; every required UID has exactly one terminal pass. A passing sibling never
  hides fail/skip/missing/unknown/duplicate/stale evidence. Required count is nonzero.
- Exclusions match the pinned policy, never post-result edits. Raw discovery and TRX/
  execution records agree with recomputed counts. Client/script evidence uses its
  own declared entry roster, not invented native UIDs.
- All files/receipts join the same intent, candidate, SHA, native RunId and authority;
  fresh start/end order and teardown/cleanup success; true booleans for credit;
  no seams, NoReport, diagnostics or partial selection; accepted pre-publish report.

Build a frozen `publication-authority.json` digest over these inputs and the sanitized
summary digest. Journals and GitHub asset manifest record it. The summary is an output
of validation, never a source for required suites or success. Missing evidence fails
before the first remote publication write. A network-only publication recovery uses
the same authority/tests; a failed or interrupted test run gets no automatic full rerun.

### D-16: One lock and one owned native lifetime across phases (A-1/A-4)

The C# `ReleaseGateCoordinator` owns orchestration. Refactor `release-cut.ps1` into
explicit phase calls (`PrepareCut`, `PushCut`, `FullTest`, `FinalizeEvidence`,
`Publish`) behind that owner. The external manual front door is the API/CLI in D-12;
direct script calls without a valid owner/intent envelope are diagnostic only and
cannot cut/publish. It must not continue through phases after an ambiguous result.

Acquire `C:\Antiphon\verification\native-run.lock` **before clone/fetch/cut**.
Hold through remote receipt, receipt import and all child joins; on report failure,
persist the pending obligation and join before releasing, without waiting indefinitely
for board recovery. Publication recovery reacquires the same lock and validates
authority. No-new-SHA or contention yields an auditable terminal not-cut outcome.
Skip only when exact SHA + current policy hash already has a verified published
receipt; a pending release is reconciled, not treated as published or recut.

Use shared-lock schema v2: repository, lane, intent, native RunId, generation, host
boot ID, PID/start identity, native Job Object name, continuation token and acquired
time. Master and RC acquire locks in one order: shared lock, then lane/state lock;
release in reverse order. Master v1 records are recognized as occupied; unknown
ownership refuses. A child only borrows the verified outer owner's lock; it never
deletes it in nightly-run's finally. Carry ownership through wrapper/core/self-reexec
and explicit RunId before the first child; scheduler fields are separate from Trigger=rc.

Include an owner kind: `release-job` requires the job/attempt facts below; the master
lane remains `nightly-process` with its existing PID/start/continuation custody and
does not invent an RC intent/job. RC recovery never deletes a stale master lock.
Freeze the controller script closure into an owned per-intent tool directory from
the recorded deployed commit before launch; verify those digests at every phase.
After pinning, candidate test scripts come from the immutable candidate checkout.
Record controller and candidate script identities separately. Advancing the canonical
checkout while a full run is in flight must not change its later phase code.

Use a Windows-specific `ReleaseGateNativeProcess` infrastructure adapter, with an
Application I/O interface. Create a named kill-on-close Job Object with breakaway
disabled; launch each native root suspended, assign it to the job, durably record
PID/start/attempt, then resume. The host owns the job handle and does not inherit it
into children. Failure to establish containment refuses launch. Reuse the established
suspended-assignment pattern; the existing internal memory-limit WindowsJobObject
class is not a ready general-purpose launcher. Do not introduce a Pty session or use
the production runner for this noninteractive execution.

Graceful shutdown cancels and joins the owned tree. Abrupt host death closes the
job handle, killing only its owned descendants. Persisted attempt identity supports
startup verification of root exit and an empty/absent named job before removing a
stale matching lock. PID reuse, inaccessible job state, inconsistent boot/start IDs
or unknown custody holds recovery and preserves the lock. Never delete on age or
heartbeat expiry. Keep the job handle until all descendants are accounted for even
if the immediate process exited. Do not report terminal completion with live children.

Recovery phase rules:

| Crash/loss cut | Recovery |
|---|---|
| Intent/card commit before enqueue, queue lost, worker before claim | Requeue the same intent; no new card. |
| Claimed before any native start | Verify dead owner/custody, allocate next attempt generation, reuse all reserved identities. |
| PrepareCut before DB pin acknowledgement | Import exact owned preparation journal; persist same SHA/ref/authority; never refetch to replace a recorded pin. |
| Pin persisted, push response lost | Read remote ref. Equal SHA resumes; absent may push same ref; different/unknown holds. |
| Child started before start acknowledgement | Examine job/PID/start and phase receipts. Live owner is not relaunched. Dead incomplete test phase becomes interrupted, not automatically retested. |
| Full evidence complete before native/queue ack | Import and validate complete evidence for the same RunId, then deliver pre-publish report and continue. |
| Test/build/cleanup failure or incomplete interrupted run | Freeze evidence, Fix/failed card and linked Backlog work; no automatic test retry. A new cut needs a new request/slot. |
| Remote publication succeeded before local/queue ack | Strict remote readback plus asset hashes resumes the same publication journal. |
| Card commit before local acknowledgement | Read exact event/card/revision; acknowledge once, no second revision or card. |

Queue/transport attempts may increase; candidate, source SHA and completed native
RunId never change during recovery. Reject both orphan-detached child adoption based
on PID alone and automatic restart of a failed full test disguised as queue retry.

### D-17: Capture and validate real GitHub receipts and bytes (A-2)

Replace `Invoke-ReleaseGateGitHub`'s Start-Process-only result with a bounded process
adapter that captures stdout and stderr separately, drains both while running, waits
for exit and returns exit/status plus parsed JSON only when appropriate. Binary asset
downloads go directly to fresh owned files/streams, never through PowerShell string
encoding. Nonzero exit, timeout, truncated/oversize output or malformed JSON is a
failed read, not an empty successful object. Keep credentials out of arguments/logs;
use the already authorized `gh` identity, explicit repository and github.com host.

Use `gh api` for repository-scoped release/asset JSON, and keep existing draft/upload
CLI operations only behind the same checked adapter. The documented REST release
fields are `id`, `tag_name`, `draft`, `html_url`, `url` and `assets_url`; normalize them
once, without boolean coercion. Pin the API version in the adapter/fixtures. The
current official examples specify `X-GitHub-Api-Version: 2026-03-10`.
[GitHub release API](https://docs.github.com/en/rest/releases/releases),
[gh api](https://cli.github.com/manual/gh_api).

Strict readback requires success, positive numeric release ID, exact reserved tag,
real boolean draft state, expected repository-scoped API URLs, and HTTPS github.com
HTML URL for that repository/tag. Reject foreign origins, userinfo, mismatched IDs,
missing fields and string `"false"`. Independently verify the remote **peeled tag SHA**;
`target_commitish` is not that proof. For drafts use authenticated repository release
listing with pagination and exact tag match if tag lookup cannot find drafts; once
known, use release ID and require it remain equal. Zero matches is absent only after
a successful complete read, never after authentication/network failure.

Enumerate the release's assets with pagination. Require exactly one each of
`release-manifest.json` and the journaled sanitized summary name, with matching release
association, uploaded state, IDs and expected sizes. Download each by asset ID with
`Accept: application/octet-stream`, following the documented redirect through the
authorized CLI without logging or persisting signed redirect URLs. Compare the actual
bytes' SHA-256 to the pre-journaled local bytes; a reported digest alone is insufficient.
[GitHub asset API](https://docs.github.com/en/rest/releases/assets#get-a-release-asset).

Persist tag/SHA/manifest/summary intent before writes. Create/push a tag only after
all authority checks. Read recipient before retrying any ambiguous create/upload/
publish: adopt only exact matching repository/tag/SHA/asset identity. Remove
`--clobber`. Existing equal bytes count as completed upload; unequal/duplicate assets
remain untouched and block. After both downloads verify, publish the draft. Then
perform a **fresh final release and both-asset readback**, requiring draft=false,
before setting the journal's published bit, reservation status or release-card verdict.

A nonzero write followed by a strictly matching readback may recover success.
Unknown readback remains pending. An already published release with mismatching assets
is recorded as a remote conflict, not repaired destructively or accepted. Freeze the
manifest and summary once journaled; final card acknowledgement is a separate record.
Never reserve another N or recreate a release because a response was lost.

### D-18: Bounded full-suite chunks and bounded Code rounds

Full RC retains all eight suites, slow/native cases, automated E2E and existing named
exclusions. At discovery time materialize an immutable `execution-plan.json`: one
native class per chunk by default, sequential projects and chunks. Compare class
roster and expanded UID union to full discovery before and after execution. Existing
explicit class chunks can be used only if bounded and disjoint. Every process retains
its existing policy watchdog. Do not run the entire Antiphon.Tests assembly in a
single chunk, change deadlines or remove required tests after a duration failure.

If one class cannot fit, qualification fails as a capacity finding with its measured
class/case costs. A subsequent reviewed policy change can split it into exact methods/
UIDs using a supported selector, with identical total required UID membership. No
unsupported selector or runtime timeout escalation is assumed in this plan. Estimate
283 test minutes plus setup/readback as before; actual one-class overhead must be
measured. Native assembly limiter rules and isolated DB/runner/broker remain mandatory.

Ordinary per-change verification remains approximately **three minutes per Code
round**, one isolated build plus its one filter. This is a scoped exception, not
activation of Interim and not permission to bypass Final Review or exact-SHA land.
Broad recovery matrices, full suites and PCs are separately budgeted release/qualification
obligations. Each round below is an independently reviewed, disabled slice of at most
about 90 active minutes. A round that grows stops at a committed checkpoint and gets
a revised scope; it does not roll into an unbounded activation dispatch.

## Implementation slices and bounded Code rounds

Paths below marked new are proposed. Code must implement production behavior and its
tests together, commit/push before verification, report the actual CP counts and then
obtain separate ordinary Review/land. Later rounds start from the landed predecessor.
No live registration/publication inside these rounds. No all-round CP replay per change.

| Round / budget including CP | Deliverable and files | Named tests / exit boundary |
|---|---|---|
| B1 / 75 min | A-3 authority and all-chunk gate: `scripts/publish-release.ps1`, `scripts/lib/release-gate.ps1`, `nightly-policy.ps1`, `nightly-coverage.ps1`; new `scripts/lib/release-authority.ps1`; extend `scripts/test-release-gate.ps1`. | New `Scripts/ReleaseGateAuthorityContractTests.cs` (ordinary); `ReleaseGatePublicationAuthorityTests.C599A_PinnedPolicy` (full matrix). Missing authority cannot write remotely; legacy candidates refuse. |
| B2 / 85 min | A-2 real stdout/JSON/binary adapter and immutable upload/recovery: `scripts/lib/release-gate.ps1`, new `release-github.ps1`, `scripts/publish-release.ps1`, process-boundary fixture under `scripts/fixtures/release-gate/`. | New `Scripts/ReleaseGateRecipientContractTests.cs` (ordinary); `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback/C599A_AssetReadback` plus inherited recovery methods. Valid real process output accepted, missing receipt/unequal bytes refuse. |
| B3 / 85 min | D-11 durable entities/outbox/claims and atomic initial Cut-card allocation; new Domain release entities/enums, `Application/Services/ReleaseGateIntentService.cs`, `ReleaseGateOutboxDispatcher.cs` with a queue I/O boundary, `Infrastructure/Data/AppDbContext.cs`, CLI-generated migration. Add persisted card/column WorkflowKind with Code defaults here. | New `Infrastructure/ReleaseGateIntentTests.cs` (ordinary); persistence/restart cases in `ReleaseGateQueueTests`. Seed an isolated shared board with Code and Release columns until B4 adds setup. DB constraints and commit-before-enqueue; no production worker enabled. |
| B4 / 85 min | D-13 shared-board workflow setup/stage projector and dispatch boundaries: new `ReleaseGateBoardService.cs`, `ReleaseGateCardProjector.cs`; `BoardService`, `CardService`, `CardWorkTransitionService`, `OrchestratorService`, `AgentTaskService`, `ScheduleService`/scheduled-action boundary, `WorkflowDefinitionLoader`, `ExternalTrackerSyncService`; related DTOs. | New `Application/ReleaseGateBoardTests.cs` (ordinary), full guards in `ReleaseGateBoardBoundaryTests.cs`. Same-status column moves and generic Code isolation; regular Code board behavior retained. |
| B5 / 85 min | D-14 outcome delivery/fix links: new `ReleaseGateReportService.cs`, `ReleaseGateFixService.cs`, outbox consumers; `scripts/nightly-report.ps1` RC routing; generated body/changelog; fix/link DTOs. | New `Application/ReleaseGateReportContractTests.cs` (ordinary); update `E2E/ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery`. Pre-native failure, idempotent fix creation, pre/post-publish receipts; master incident untouched. |
| B6 / 85 min | D-16 phase coordinator and owned native lifetime: new `Application/Services/ReleaseGateCoordinator.cs`, `Application/Interfaces/IReleaseGateNativeProcess.cs`, `Infrastructure/Releases/ReleaseGateNativeProcess.cs` and lock adapter; `release-cut.ps1`, `release-candidate.ps1`, `nightly-run.ps1`, `lib/nightly-run-impl.ps1`, `release-gate.ps1`. | New `Infrastructure/ReleaseGateOwnershipContractTests.cs` (ordinary); `ReleaseGateQueueTests.C599A_LockScope/C599A_DisableAndShutdown/C599A_HandoffRecovery`. Own short native child, no production runner; lock before cut through join/publish. |
| B7 / 85 min | Main-instance Hangfire/admission and API: new `ReleaseGatesSettings`/validator, `ReleaseGateSlotJob`, `ReleaseGateRecoveryJob`, `ReleaseGateExecutionJob`; `HangfireConfiguration`, `Program`, `appsettings.json`; new `Api/Endpoints/ReleaseGateEndpoints.cs`, `scripts/release-gates.ps1`. | `Infrastructure/ReleaseGateSchedulerTests` five existing commissioned names (ordinary); queue ReadyAndBusy/restart matrix. One dedicated worker, durable recovery and false defaults. Normal test Program never launches it. |
| B8 / 85 min | D-18 chunk plan and end-to-end envelopes: `scripts/lib/nightly-tests-impl.ps1`, `nightly-coverage.ps1`, `tests/test-execution-policy.json` if census disposition changes; update `release-status.ps1`, `register-release-gates.ps1` to refuse RC Windmill activation; owner docs `release-gates.md`, `testing-and-build.md`, `bootstrap.md`, `ops-http.md`, `agent-card-lifecycle.md`, `workflow-tracker-block.md`, `scripts/windmill/README.md`. | New `Scripts/ReleaseGateActivationContractTests.cs` (ordinary); full queue/report/authority integration and Q rehearsal. Executable operator commands, no RC Windmill path, exact chunks/correlation, still disabled. |

Round estimates total **670 active minutes**, including 24 minutes of ordinary CP
verification; they are unmeasured authoring bounds. Migration generation is a required
B3 authoring operation, not an extra test suite. Report a required generation build
separately and use isolated output. Scope expansion or a cold build overrun is explicit;
do not hide a broad run inside the three-minute estimate.

## TestDesign handoff and checkpoint proposal

The earlier activation verification section is input to TestDesign, not already
certified for this new architecture. Retain G/PC-207..242 where their guards survive;
bind them to these concrete store/phase/process seams and add controls for D-13/D-14,
the separate report phases, binary downloads, fenced claims and chunk authority.
Do not claim the prior 36 controls cover release-card ownership or every new guard.

Mandatory real boundaries: fresh host + fresh in-memory Hangfire against the same
private PostgreSQL schema; committed intent before queue loss; real short owned
process including host-death/descendant cleanup; bare Git remote; real `gh` process
fixture emitting stdout/status/binary files; real board GET and revision readback.
Production predicates may not be replaced. Source-string tests do not prove these paths.
Existing Program test guards, process-spawn limiter, global Hangfire isolation and
real-clock-offset test rules apply. A future Mutation dispatch stays method-scoped.

Update the old matrices as follows:

- V-11/DL-6: same DB intent/card across fresh queues; include committed slot cursor,
  late/missed slots, no enqueue-before-commit, duplicate claims, disabled reentry,
  named-job ownership uncertainty and interrupted tests refusing automatic rerun.
- V-12: pin policy blob independently, all eight suites, all chunks and expanded UIDs;
  shrink the summary while valid authority remains required; mutate one guard at a time.
- V-13/V-14: production process adapter, strict JSON types, correct URL/tag/peeled SHA,
  paginated drafts/assets, real binary bytes, both pre/post-publish downloads and every
  commit-before-response-loss cut. No `--clobber` and no new tag on recovery.
- V-15/DL-8: queue -> projector -> actual persisted release card/revision; receipt and
  same-transaction writes rather than legacy incident POST as implementation authority.
  API outage/readback tests still use the real GET boundary. Test pre-native null IDs,
  busy worker, response loss, wrong card/body, assigned legacy incident preservation,
  two distinct RCs, successor/fix links and Code cards/columns on the shared board untouched by stage projection.
- New V-17: complete shared-board release-workflow path plus failed/abandoned path, exactly one card
  per candidate, zero agent tasks/sessions, no tracker/tick mutation, normal fix Backlog
  admission, explicit failed verdict, PublishedAwaitingReport recovery without republish.

### Historical checkpoint proposal (superseded below)

**Historical proposal. The appended Verification design owns the final CP-19..26 manifest.** Each
round runs only its own row. CP-19's former single scheduler-only manifest is replaced
by CP-19..26 below; historical CP-1..18 remain historical. One isolated build and one
exact filter per row. All listed methods must execute, with zero failures/skips.
Methods perform small decisive matrices; internal rows are not TUnit execution counts.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-19 | B1 committed | `tests/Antiphon.Tests -> bin-c599b1/` | authority | `/*/*/ReleaseGateAuthorityContractTests/C599B_*` | A-3 / V-12 | `C599B_PinnedPolicy`, `C599B_AllChunks`; 2 passed | 2 | 3 |
| CP-20 | B2 committed | `tests/Antiphon.Tests -> bin-c599b2/` | recipient | `/*/*/ReleaseGateRecipientContractTests/C599B_*` | A-2 / V-13, V-14 | `C599B_ProcessReceipt`, `C599B_AssetBytes`; 2 passed | 2 | 3 |
| CP-21 | B3 committed | `tests/Antiphon.Tests -> bin-c599b3/` | durable-intent | `/*/*/ReleaseGateIntentTests/C599B_*` | A-1 / DL-6 | `C599B_AtomicIntent`, `C599B_RecoverQueueLoss`; 2 passed | 2 | 3 |
| CP-22 | B4 committed | `tests/Antiphon.Tests -> bin-c599b4/` | release-workflow | `/*/*/ReleaseGateBoardTests/C599B_*` | V-17 ownership/stages | `C599B_StageMoves`, `C599B_CodeIsolation`; 2 passed | 2 | 3 |
| CP-23 | B5 committed | `tests/Antiphon.Tests -> bin-c599b5/` | report-receipts | `/*/*/ReleaseGateReportContractTests/C599B_*` | A-4 / DL-8, V-15 | `C599B_PreNativeReport`, `C599B_FixAndFinalReceipt`; 2 passed | 2 | 3 |
| CP-24 | B6 committed | `tests/Antiphon.Tests -> bin-c599b6/` | owned-coordinator | `/*/*/ReleaseGateOwnershipContractTests/C599B_*` | A-4 / V-11 lock/join | `C599B_LockBeforeCut`, `C599B_JoinBeforeRelease`; 2 passed | 2 | 3 |
| CP-25 | B7 committed | `tests/Antiphon.Tests -> bin-c599b7/` | scheduler | `/*/*/ReleaseGateSchedulerTests/C599A_*` | V-10 / R-5, R-6 | `C599A_Admission`, `C599A_Slots`, `C599A_Provenance`, `C599A_DisabledReentry`, `C599A_RepairDisposition`; 5 passed | 5 | 3 |
| CP-26 | B8 committed | `tests/Antiphon.Tests -> bin-c599b8/` | activation-contract | `/*/*/ReleaseGateActivationContractTests/C599B_*` | D-18 / V-12, R-7 | `C599B_ChunkUnion`, `C599B_CorrelationAndMasterIsolation`; 2 passed | 2 | 3 |

Command template, substituting exactly the chosen row (example B1):

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-19 -Project tests/Antiphon.Tests -OutputPath bin-c599b1/ -Filter '/*/*/ReleaseGateAuthorityContractTests/C599B_*' -MinExecuted 2 -Expect C599B_PinnedPolicy,C599B_AllChunks -ResultsRoot .antiphon/c599b-checkpoints
```

Expected total is 19 TUnit methods across eight separate rounds, not 19 assertions
or minutes. Estimate per row: 1.5-minute incremental build + 1.5-minute tests. Cold
build/Postgres/process-fixture cost must be measured and reported; exceeding the
estimate does not authorize killing a valid test or weakening its oracle. TestDesign
must split an expensive case into a fast contract and a retained broad qualification
case if needed. The three-minute lane never claims the full recovery matrix passed.
Cleanup uses the verified producer-owned output inventory, within this checkout.

## Post-land activation and completion

Q-1..4 remain separate, explicitly commissioned activation, outside bounded Code
rounds. Accepted reviewed Code is not operational qualification or S5 acceptance.

| Q | Concrete operation / required receipt |
|---|---|
| Q-1 setup | Confirm landing/canonical checkout/running `/api/version`; place operator configuration and verify actual roots, project, remote and authorized GitHub identity without printing credentials. Run `release-gates.ps1 setup -Profile <operator-json>` preview, then `-Apply` with expected version. Read back the existing Antiphon BoardId, typed Code/Release columns, disabled admission/schedule/publication, and actual worker/recurrence registration. Repeat setup to prove no duplicates. Check no enabled Windmill RC/Scheduled Task. |
| Q-2 rehearsal | At the landed SHA run certified exact-method queue/ownership/authority/board-delivery matrices in isolated stores/processes. Include abrupt host death, both unavailable and available recipients, all ambiguous-write cuts, guard negatives and normal Code regressions. TestDesign supplies the executable matrix and updated cost; old 20-minute estimate is not presumed sufficient. |
| Q-3 manual | Via versioned `control`, enable admission/publication but leave ScheduleEnabled=false. `release-gates.ps1 run -RequestId <guid>` creates a manual intent/card through real Hangfire. Require all eight suites, accepted reports, remote tag/release and both downloaded asset digests. Read the release card's complete history, included cards, exact SHA and Released verdict. Measure chunk costs and capacity. No fixture seams/subset/NoReport/live approvals. |
| Q-4 scheduled | Enable ScheduleEnabled with version/reason and read back `30 8,16 * * *` London and recovery recurrence. Observe both actual slots; each has a durable outcome/card or missed-slot record. At least one real scheduled full green with strict publication and Released card is required. A second no-new-SHA/busy observation supplies no green. Wait for genuine reviewed master changes, never manufacture a commit for a release. |

CLI verbs in Q map exactly to D-12; implementation must provide help/examples and
JSON status readback before this operator dispatch. Q-1's old demand for a disabled
Hangfire recurrence becomes **no slot recurrence while ScheduleEnabled=false**;
the durable registration expresses disabled state. Recovery may remain registered
to reconcile previously admitted work. Manual Q-3 does not simulate a scheduled slot.

Qualification retains full SHA, authority/policy/script/roster/chunk digests, all
counts, actual durations, intent/job/attempt/card/revision/run IDs, remote tag and
release identity, both independent asset digests, failure/repair links and teardown
receipts. Artifact: `docs/investigations/<date>-card-0599-release-gate-qualification.md`;
raw evidence: `C:\Antiphon\releases\qualification\card-0599`. No credentials/raw
transcripts in git or public release assets. Full runs remain foreground-owned and
source-frozen, with commits before starting them.

Publication acceptance is pending until Q-4. If tests fail, preserve the failed
release card, file/link fixes, land them on master and cut a new candidate. If GitHub
or board delivery is unknown, keep a recoverable pending intent; never call it released.
Rollback disables admission/scheduling/publication while receipt recovery and child
ownership remain active. Do not delete tags, assets, cards, journals or constraints.
S5 master/watchdog/Interim pilot remains independently open and cannot use RC credit.

### Planning validation (historical Plan-stage evidence)

Source and owner-document inspection, live card read, GitHub primary API contract
review, and document/diff consistency checks only. No builds/tests or live actions
performed. TestDesign must certify methods/fixtures/controls and costs before B1.


## Verification design

Round 2, TestDesign `c4a636a4`, 2026-09-24. Inspected checkout
`145f953d`; controlling Plan addendum `35b320a5` was present. The operator's
shared-board correction in D-13 supersedes separate-board isolation everywhere,
including inherited V-17 and DL-8. D-11..D-18 otherwise retain their fix design.

This is an executable specification for Code to implement, not executed evidence.
The C599A/C599B classes below do not yet exist except the older report fixture.
B1 may proceed with CP-19. Each round implements its tests and required fixture
parts alongside production behavior; cross-round composition is completed in B8.
No dormant slice may enable production before Q-1..Q-4. G/PC-207..242 are retargeted
here; the earlier versions are superseded. Historical G/PC-1..206 remain recorded
in the activation plan and must be repaired if touched, not counted as newly passed.

### Inspection

- `Scripts/ScriptHarness.cs`, all bodies in `ReleaseGatePublicationTests.cs`,
  `ReleaseGateRunTests.cs`, `ReleaseGatePolicyTests.cs`; corresponding
  `scripts/test-release-gate.ps1` policy/parser/census, credit, lock/continuation,
  GitHub-store, publication fixture and gate bodies | policy/report independence,
  process-versus-function adapter, missing/failed sibling and required UID,
  response-loss recovery -> V-12..14, V-18..19, R-9..10. Existing four-suite
  `New-C599Policy`, string release IDs and name-only asset lists cannot be valid
  D-15/D-17 controls. Source-text parameter-hop assertions are not execution proof.
- All nine `Infrastructure/HangfireStartupSafetyTests.cs` and all three
  `Application/WorktreeResidueRegistrationTests.cs` bodies, including its
  `ScopeJobActivator`; `TestHelpers/TestDbFixture.cs`,
  `AntiphonWebAppFactory` configuration/disposal and `ProductionRunnerGuard`
  | real background client/server, fresh storage, disabled normal Program boots,
  PostgreSQL receipt -> V-10..11, V-20, V-24, R-11/R-15. Despite its historical
  name, `CreateIsolatedSchemaAsync` clones a database; use that connection across
  fresh hosts, never the shared store and never the development DB.
- `CardIdentifierAllocatorTests` all four bodies; `CardWorkTransitionServiceTests`
  dispatched/settled/failed cases and read/move/revision helpers;
  `OrchestratorServiceIntegrationTests` global/column cap and two-claim bodies,
  `BuildHarness` and `CreateGraph`; explicit/title `AgentTaskCardBindingTests`
  bodies; `ScheduleCardActionTests` none/release/spawn bodies;
  `AgentTaskPipelineStatusTests` empty/limits/in-flight bodies and HTTP tests;
  `AgentTaskConcurrencyLimitTests` absolute/role refusal bodies |
  shared-board identifier allocation, same-status columns, independent entry
  points and Code depth/capacity -> V-17/V-21, R-12. New boundary tests use these
  fixture patterns; existing unrelated cases are not added to ordinary CP scope.
- `OrchestratorService.PollTickAsync/LoadEligibleCandidatesAsync`,
  `AgentTaskPipelineStatusService.GetAsync/BuildReady` entry,
  `DelegationOpenGate` count-and-insert and `AgentTaskService` create gate |
  pickup, Ready/Queued/InFlight projection and role cap are distinct boundaries.
  B4's file scope also includes `AgentTaskPipelineStatusService`,
  `DelegationOpenGate`, retry/lifecycle column selectors and card DTOs.
- `Antiphon.E2E/ReleaseGateReportDeliveryTests.cs` setup, both tests, summary,
  process and GET helpers; `Fixtures/AntiphonAppFixture` Initialize/StartHost,
  guarded restart/suspend and Dispose bodies | old title/label checks versus full GUID/body/
  revision receipt -> V-15/V-22, R-13. Retain the master-report control; replace
  standalone RC success expectations with durable-intent reporting.
- `Antiphon.PtyHost.Tests/HostCustodyTests` tracking-write-failure,
  receipt-before-shutdown, producer-store-failure, seal/drain and unsupported
  backend bodies; `Antiphon.CustodyTestChild/Program.cs`;
  `scripts/fixtures/nightly/c487-probe/Probe.cs` and project |
  suspended child, persisted start, descendant accounting and argument-expanded
  discovery -> V-11/V-23/V-25, R-14/R-16. These are nearest fixtures, not reuse of
  the Pty transport for release execution. Linux text-only custody tests are not
  Windows containment evidence.
- Owners read: project conventions, orchestration/lifecycle, release gates,
  card-file privacy, bootstrap Hangfire section, testing/build checkpoint,
  isolated-store/clock/process rules. Source/fixture inspection only; no test,
  build, mutation, live setup, schedule, release or notification was run.

**Missing setup commissioned with the rounds.** Add a shared
`TestHelpers/ReleaseGateTestWorld.cs` with an owned bare Git remote, isolated
database, deterministic barrier hooks at external I/O/transaction handoffs, fresh
DI scopes, and private real `InMemoryStorage`/`BackgroundJobServer` pairs.
No decision predicate is replaced. Use production services for claims,
projection, gates and recovery. Store B3's claim-only receipt through the production
claim service using a fixture job; it proves the B3 queue/store contract only.
B6/B7/B8 replace that probe in the full queue matrix with
`ReleaseGateExecutionJob -> ReleaseGateCoordinator -> ReleaseGateNativeProcess`.
A test-only worker is never started by changing the normal Program guard.

For E2E, add assembly-local release-delivery composition using
`AntiphonAppFixture.OwnedDatabase` and its isolated runner; do not reference the
Antiphon.Tests executable as a fixture library. The current host restart helpers
are restricted to DistillerCanary/LandDelivery. Add a similarly validated,
internal release-delivery option and suspend/restart helper retaining only that
fixture's DB/workspace/runner. Start a fresh private Hangfire host beside it.
This is missing B5/B8 fixture setup, not permission to bypass the existing helper
guards or launch against production. Import the native outcome journal through
the production ReleaseGateReportService before its real outbox handoff.

Every host/store and named event/job/root gets a unique identity. Dispose both
BackgroundJobServers before discarding a host; recovery keeps only the cloned DB
and owned journals, not a static client/storage/service instance. Save/restore
Hangfire global configuration under `[NotInParallel]`; prefer explicit storage
constructors and scoped activators. All child-spawning Antiphon.Tests/E2E classes
carry their own assembly's `ParallelLimiter<ProcessSpawnLimit>`. Use an offset
over real time for running poll loops; pure slot/scan-budget arithmetic may use
a deterministic clock. Never leave a frozen clock in a real queue poll loop.

B1 adds full eight-suite policy/blob and ledger fixtures under
`scripts/fixtures/release-gate/`; commit the authority policy in a scratch Git
repository, capture its actual blob/SHA, and independently compute expected
roster/byte digests. Raw discovery/TRX samples may be synthetic in authority
predicate tests; they do not prove actual execution. B8 runs the existing C487
probe through the real discovery/chunk executor, using its two argument/data rows
and Slow/OptIn boundaries; this proves selector/UID expansion, not the eight full
production suites. Include the probe build in the owning CP's build graph.

B2 adds an executable `gh` fixture in the same fixture directory (compiled small
process or PowerShell launcher through the adapter's executable I/O parameter),
with a file-backed recipient store, true stdout/stderr/exit codes, paginated REST
JSON and binary downloads containing NUL/non-ASCII bytes. The adapter itself
constructs/parses the command and captures files; no `GitHub` scriptblock returns
a ready Body. Neither local nor remote expected digest is supplied by the
publisher's return value. A separate observer reads remote store bytes. Strip
live credentials; all fixture identities/remotes are private and cannot be
promoted as operational receipts. No network request reaches GitHub.

Extend ScriptHarness case selection to C599A/C599B and require exact named
assertion inventories for each case, retaining its 120-second per-child limit
and C487 exit trailer. On timeout join the owned fixture child before cleanup;
do not widen the limit. Large pure-data matrices run in one PowerShell process,
not one process per datum. B6 adds a short controller child that hosts the real
native adapter and uses the existing custody child/event pattern. The observer
owns controller and descendants by exact PID/start/job identity and joins them
even on assertion failure; a separate sentinel confirms foreign work survives.
Report full-suite fixture repair as authoring, not extra ordinary verification.

**Boundary combinations.** Start every negative from an independently valid
control, change only its specified field/handshake and require the named guard
to be reached. For gates, enumerate all static Enabled x ServerEnabled x persisted
AdmissionEnabled combinations; schedule/publication bits are independently varied.
For slots, cover both daily times at just-before/exact/just-after, exactly grace
and grace+one tick; September 23 UTC 07:30/15:30, March 29 UTC 07:30/15:30 and
October 25 UTC 08:30/16:30 (2026), enable/disable windows, cursor restart and manual
key replay. Slots occur outside the DST ambiguous hour, so duplicated 01:xx local
time is excluded by the fixed cron, not guessed away.

For each authority field cover absent/null/wrong type/wrong identity plus the
valid value; remove each of eight suites, each chunk and each required expanded
UID independently. Fail/skip/unknown/stale/duplicate/zero counts cannot be hidden
by a passing sibling. Validate clients/scripts by their own declared rosters.
For workflow boundaries cross Code/Release card and Code/Release column, active
flag and cleared hold; seed two RCs, Code controls and a foreign board. Use
explicit service/API entry points rather than mock predicates. Test corrupted
legacy release bindings separately from legal states. Pairwise sampling of
ownership, receipt identity or remote-write cuts is not sufficient. Combinations
of two already-invalid independent fields are excluded: they add no oracle
beyond the one-field controls and can mask a removed guard.

### Delivery inventory

Durable join key is registration repository/project/shared BoardId + IntentId +
AttemptId/generation, then CandidateId/ref/full SHA + NativeRunId + authority digest
when allocated. Delivery projections additionally join outbox kind/sequence,
complete body digest, exact CardId and CardRevisionId. A Hangfire ID is transport
correlation only. A request, enqueue, event, Sent flag, job success or ack never
proves delivery. No session input is commissioned; any future session input
requires the matching complete UserPrompt transcript from its actual recipient.

| Path | Producer -> destination | Persistence boundary and recovery | Observable receipt |
|---|---|---|---|
| DL-6a | scheduled due-slot scanner/manual API -> Execute outbox -> real Hangfire worker | intent + initial Cut card + outbox commit atomically; restart fresh host and empty queue against same DB; fenced claim; retry transport only | matching durable claim/start receipt, then actual short native terminal evidence; enqueue alone remains pending |
| DL-6b | worker -> owned native controller/phase child | attempt/start facts persist before resume; prepare journal before DB pin; remote cut pin before ack | independently observed root and descendant state, phase output, exact preparation/terminal journal imported into attempt |
| DL-7a | PushCut/publisher -> bare Git and GitHub process fixture | journal before candidate/tag/create/upload/publish; ambiguous write reads recipient before retry | peeled ref/tag SHA, strict numeric release ID/repository/tag/draft state and independently downloaded manifest AND summary bytes |
| DL-8a | pre-native/native outcome -> ProjectCard outbox -> card projector | immutable body/sequence commit before enqueue; card/revision/receipt one transaction; fresh-context read before ack | real GET by CardId and revision contains whole generated body/digest and matching identities, including null native IDs before cut |
| DL-8b | failed outcome -> FileFix outbox -> Code Backlog on same board | card + fingerprint link atomic; repeat/response loss returns exact same GUID; successor is separate identity | actual Code card GET, full failure body/link and no task/session/assignment; release card GET contains reciprocal fix link |
| DL-8c | remote publication receipt -> final ProjectCard outbox | persist remote receipt before final projection; PublishedAwaitingReport resumes projection only | real final card/revision GET contains tag/id/URL and Released; prior prepublication receipt cannot substitute |
| DL-8d | committed card projection -> IEventBus/read-model consumers | event after commit; duplicate cannot create another revision | persistence/GET remains the delivery verdict; an event is only invalidation, not evidence a human saw it |

Every queue path has a producer-to-recipient case through a real
BackgroundJobClient/BackgroundJobServer, once with an already eligible worker
and once with its queue occupied by an owned barrier job. While busy, no downstream
receipt exists. Release the barrier, require the complete receipt, restart, replay
twice and require the same durable identity with no second logical side effect.
The default maintenance queue must still deliver its own persisted probe receipt
while release-gates is occupied.

For Execute and each ProjectCard/FileFix/ReconcilePublication handoff, cut at:
before DB commit (rollback/no effects), commit before enqueue, enqueue throws
before storage, queue commits but caller loses response, committed job lost with
in-memory storage, dequeue before claim, claim before native/projector action,
recipient commits before response, valid readback before local acknowledgement.
Use barriers/interceptors at I/O, never bypass business checks. Before/after cuts
run with both free and busy workers; deterministic gates avoid sleep races.
A fresh context independently reads receipts after every cut. A committed
obligation must reach its intended recipient or a durable explicit held/failed
outcome naming why; the successful-recovery controls always remove the injected
transport failure and require recipient evidence. Merely remaining pending is
not a passing delivery test.

Native-specific cuts cover suspended assignment failure, start-record failure,
root resumed before ack, root exit with descendant alive, graceful cancellation
and abrupt owner death. Verify exact job/PID/start/boot/generation before takeover;
unknown or live custody holds. Interrupted FullTest becomes terminal interrupted,
while complete evidence imports without another test start. Remote cuts cover
candidate push, tag push, draft creation, each asset upload and publish, both
before-write and commit-then-response-loss. Reconcile with fresh release/asset
pages and binary downloads after final publication, not cached prepublish data.

Board-specific cuts cover outcome save, projection transaction, fresh-DB readback
outage/wrong body, observer GET outage, ack save, fix card/link transaction and
final publication projection.
`C599A_QueuedReportRecovery` drives all outcome producers and ready/busy cases;
`C599A_ProjectionCommitCuts`, `C599A_FixDeliveryCuts` and
`C599A_PublishedReportCuts` own their named crash matrices. Keep legacy assigned
RC/nightly incidents unchanged; two RCs never coalesce by title. An API unavailable
during the observer GET prevents a test from claiming recipient evidence until
the API recovers. Production acknowledgement requires D-14's matching fresh-DB
readback; a DB read failure leaves it pending. This adds no self-HTTP dependency
to the projector. Native lock may be released only after pending report persistence and
child join, without waiting for the API to recover.

Substitutes and limits: the short native probe proves containment/receipt wiring,
not full test success; bare Git proves ref semantics, not GitHub authorization;
the executable gh fixture proves process/JSON/pagination/binary handling, not
GitHub uptime; private Hangfire proves store recovery, not production registration;
DB/API GET proves card persistence, not human reading; fixture ledgers prove
authority validation, not actual suite execution. Q-3/Q-4 are the only live
publication/operational delivery acceptance. Designs stopping at any producer ack
are rejected.

### Proves it works now

These are required observations, not a report that the commissioned methods passed.

- V-18: D-15 authority | script contract, B1 | `ReleaseGateAuthorityContractTests.C599B_PinnedPolicy` and `C599B_AllChunks` | valid eight-suite authority accepts; summary shrink, stale blob hash and failing sibling block before remote write. Full V-12 matrix: `C599A_PinnedPolicy/ExecutionLedger`.
- V-19: D-17 recipient | real process contract, B2 | `ReleaseGateRecipientContractTests.C599B_ProcessReceipt` and `C599B_AssetBytes` | valid stdout accepted, missing draft rejected, independent corrupt manifest/summary bytes block. Full V-13/14: GitHubReadback, AssetReadback, RemoteWriteRecovery.
- V-20: D-11 durable intent | PostgreSQL plus real queue, B3 | `ReleaseGateIntentTests.C599B_AtomicIntent` and `C599B_RecoverQueueLoss` | rollback produces no card/intent/outbox; fresh queue claims same committed intent after enqueue failure. Claim probe limit above applies; full native DL-6 belongs to V-11.
- V-21: D-13 same-board stages/isolation | production services/DB, B4 | `ReleaseGateBoardTests.C599B_StageMoves` and `C599B_CodeIsolation` | one board, typed columns, complete legal release path, same-status ID move, real Code pickup/projection/cap control with release present and zero release launches. Full V-17 uses six boundary methods below.
- V-22: D-14 outcome/fix/final receipt | services and fresh-context readback, B5 | `ReleaseGateReportContractTests.C599B_PreNativeReport` and `C599B_FixAndFinalReceipt` | null-native failure reaches exact release card; one linked Code fix; final receipt loss stays PublishedAwaitingReport and recovers without republish. Full V-15 uses four real API/queue methods below.
- V-23: D-16 ownership | real short Windows process, B6 | `ReleaseGateOwnershipContractTests.C599B_LockBeforeCut` and `C599B_JoinBeforeRelease` | contender cannot clone, descendant barrier prevents terminal receipt/lock release until joined. Full V-11 includes containment and abrupt-death matrices.
- V-24: D-11/12 main scheduling | production admission/scanner, B7 | five `ReleaseGateSchedulerTests.C599A_*` methods in CP-25 | exact slots, no manual scheduled credit, dormant refusals, disabled reentry and failed-test repair disposition. Private DB and queue only.
- V-25: D-18 chunk/correlation | actual small probe and process boundary, B8 | `ReleaseGateActivationContractTests.C599B_ChunkUnion` and `C599B_CorrelationAndMasterIsolation` | expanded UID union preserved in bounded chunks; controller/native envelope joins exactly; master state bytes unchanged; RC Windmill command refuses. Full matrix below.
- V-16 remains Q-1..Q-4 live acceptance, separate from V-18..25 and S5. All new full matrix methods must be included by the RC census; none may become a new blanket OptIn exclusion.

### Guards the regression

- R-9: old candidates/report-selected suites cannot publish | CP-19 methods | missing authority and failed sibling each produce zero remote writes.
- R-10: false publication receipt/asset overwrite | CP-20 methods | missing draft leaves journal unpublished; bad downloaded bytes leave original remote bytes unchanged.
- R-11: orphan/duplicate admission | CP-21 methods | initial transaction rollback is empty; recovered claim retains same intent/card after fresh queue.
- R-12: Release mixes into Code on shared board | CP-22 methods | Code Ready/Queued/InFlight depth and role count unchanged; Code control still eligible, release card neither picked nor bound.
- R-13: reports become incidents or acknowledge early | CP-23 methods | master incident untouched, same-board Code fix idempotent and two distinct pre/final receipt kinds.
- R-14: cut outside lock or root-only cleanup | CP-24 methods | zero cut while contended and no receipt with live owned descendant.
- R-15: dormancy/provenance/retry regression | CP-25 methods | zero starts while gated, repeated slot one ID, manual slot absent, failed tests never automatic rerun.
- R-16: partial/chunk/transport identity regression | CP-26 methods | exact UID union, no overlap, exact envelope and unchanged four master files.
- Full V-10..17/R-5..8 matrices are separately budgeted Q-2/RC obligations. Their completion is never inferred from the 19 ordinary methods.

### Guard inventory

The active D-11..D-18 inventory below includes unimplemented guards.
Each independently removable guard has one distinct PC; no missing or shared PC
mapping is permitted. Multiple invalid data shapes under one validation predicate
are variants of its PC, each starting from a valid control. Historical guards are
not waived by this delta.

| Guard | Plan reference and safety-critical invariant | Positive control |
|---|---|---|
| G-207 | D-12 static Enabled admission gate | PC-207 |
| G-208 | D-12 Hangfire ServerEnabled registration gate | PC-208 |
| G-209 | D-12 resolved main ContentRoot identity | PC-209 |
| G-210 | D-12 expected repository | PC-210 |
| G-211 | D-12 project repository identity | PC-211 |
| G-212 | D-12 shared board belongs to project | PC-212 |
| G-213 | D-11 London slot calculation | PC-213 |
| G-214 | D-11 manual provenance | PC-214 |
| G-215 | D-11 repeated slot uniqueness | PC-215 |
| G-216 | D-11 commit before enqueue | PC-216 |
| G-217 | D-11 durable queue-loss recovery | PC-217 |
| G-218 | D-16 saved native identity | PC-218 |
| G-219 | D-16 failed tests terminal | PC-219 |
| G-220 | D-16 shared lock before cut | PC-220 |
| G-221 | D-16 no live lock stealing | PC-221 |
| G-222 | D-16 lock through publish and join | PC-222 |
| G-223 | D-16 shutdown custody | PC-223 |
| G-224 | D-15 policy suite authority | PC-224 |
| G-225 | D-15 pinned policy hash authority | PC-225 |
| G-226 | D-15 every chunk succeeds | PC-226 |
| G-227 | D-17 actual process stdout | PC-227 |
| G-228 | D-17 successful final read | PC-228 |
| G-229 | D-17 structured final body | PC-229 |
| G-230 | D-17 boolean draft=false | PC-230 |
| G-231 | D-17 positive numeric release ID | PC-231 |
| G-232 | D-17 repository-scoped release API URL | PC-232 |
| G-233 | D-17 exact reserved tag | PC-233 |
| G-234 | D-17 downloaded manifest bytes | PC-234 |
| G-235 | D-17 downloaded summary bytes | PC-235 |
| G-236 | D-17 no unequal asset overwrite | PC-236 |
| G-237 | D-14 report commit before enqueue | PC-237 |
| G-238 | D-14 complete recipient readback | PC-238 |
| G-239 | D-14 idempotent projection recovery | PC-239 |
| G-240 | D-14 RC versus master reports | PC-240 |
| G-241 | D-12 disabled dequeue recheck | PC-241 |
| G-242 | D-11 enqueue is not completion | PC-242 |
| G-243 | D-12 persisted AdmissionEnabled gate | PC-243 |
| G-244 | D-11 enabled schedule windows | PC-244 |
| G-245 | D-12 publication disable before writes | PC-245 |
| G-246 | D-12 disable before each phase | PC-246 |
| G-247 | D-12 explicit resume after re-enable | PC-247 |
| G-248 | D-12 operator-only front doors | PC-248 |
| G-249 | D-12 setup preview has no effects | PC-249 |
| G-250 | D-12 versioned controls | PC-250 |
| G-251 | D-13 idempotent setup and shape ownership | PC-251 |
| G-252 | D-11 late-start grace | PC-252 |
| G-253 | D-11 persisted cursor and initial enable time | PC-253 |
| G-254 | D-11 recovery scan count bound | PC-254 |
| G-255 | D-11 durable transport backoff | PC-255 |
| G-256 | D-11 atomic generation claim | PC-256 |
| G-257 | D-11 atomic admission card and outbox | PC-257 |
| G-258 | D-11 unique registration identity | PC-258 |
| G-259 | D-11 no DB transaction across external I/O | PC-259 |
| G-260 | D-11 Execute receipt retirement | PC-260 |
| G-261 | D-11 live attempt suppresses pump replay | PC-261 |
| G-262 | D-11 unknown ownership holds | PC-262 |
| G-263 | D-11 retries only from durable state | PC-263 |
| G-264 | D-11 dedicated release worker | PC-264 |
| G-265 | D-11 DB and journal disagreement | PC-265 |
| G-266 | D-13 candidate-name collision | PC-266 |
| G-267 | D-16 contain before runnable | PC-267 |
| G-268 | D-16 start identity durable before resume | PC-268 |
| G-269 | D-16 descendants cannot break away | PC-269 |
| G-270 | D-16 host death kills contained tree | PC-270 |
| G-271 | D-16 children cannot retain job handle | PC-271 |
| G-272 | D-16 root exit is not tree exit | PC-272 |
| G-273 | D-16 exact stale owner identity | PC-273 |
| G-274 | D-16 unknown named job state | PC-274 |
| G-275 | D-16 master lock is never RC residue | PC-275 |
| G-276 | D-16 verified lock borrowing | PC-276 |
| G-277 | D-16 frozen controller closure | PC-277 |
| G-278 | D-16 candidate scripts independently pinned | PC-278 |
| G-279 | D-16 direct script calls need owner | PC-279 |
| G-280 | D-16 preparation journal recovery | PC-280 |
| G-281 | D-16 ambiguous push readback | PC-281 |
| G-282 | D-16 interrupted tests cannot rerun | PC-282 |
| G-283 | D-16 complete native evidence imported once | PC-283 |
| G-284 | D-16 no-new-SHA needs published current-policy receipt | PC-284 |
| G-285 | D-15 policy blob at pinned commit | PC-285 |
| G-286 | D-15 schema and full eight-suite profile | PC-286 |
| G-287 | D-15 resolved evidence containment | PC-287 |
| G-288 | D-15 all build and prerequisite evidence | PC-288 |
| G-289 | D-15 native exit and actual counts | PC-289 |
| G-290 | D-15 exact frozen chunk set | PC-290 |
| G-291 | D-15 disjoint expanded UID membership | PC-291 |
| G-292 | D-15 terminal pass for every UID | PC-292 |
| G-293 | D-15 nonzero required UID set | PC-293 |
| G-294 | D-15 independently recomputed execution counts | PC-294 |
| G-295 | D-15 pinned exclusion dispositions | PC-295 |
| G-296 | D-15 intent correlation | PC-296 |
| G-297 | D-15 candidate correlation | PC-297 |
| G-298 | D-15 SHA correlation | PC-298 |
| G-299 | D-15 native RunId correlation | PC-299 |
| G-300 | D-15 authority and script digest binding | PC-300 |
| G-301 | D-15 timestamp ordering and freshness | PC-301 |
| G-302 | D-15 teardown success | PC-302 |
| G-303 | D-15 no seam credit | PC-303 |
| G-304 | D-15 no NoReport credit | PC-304 |
| G-305 | D-15 full nondiagnostic selection | PC-305 |
| G-306 | D-14 prepublication receipt kind | PC-306 |
| G-307 | D-15 frozen summary and authority bytes | PC-307 |
| G-308 | D-17 release HTML origin and tag path | PC-308 |
| G-309 | D-17 remote peeled tag SHA | PC-309 |
| G-310 | D-17 release pagination and complete absence | PC-310 |
| G-311 | D-17 asset pagination | PC-311 |
| G-312 | D-17 exact asset multiplicity | PC-312 |
| G-313 | D-17 asset belongs to release | PC-313 |
| G-314 | D-17 uploaded asset state | PC-314 |
| G-315 | D-17 asset numeric identity | PC-315 |
| G-316 | D-17 expected asset size | PC-316 |
| G-317 | D-17 fresh final release read | PC-317 |
| G-318 | D-17 fresh final asset downloads | PC-318 |
| G-319 | D-17 journal before remote writes | PC-319 |
| G-320 | D-17 identical existing asset is adopted | PC-320 |
| G-321 | D-17 one tag reservation across recovery | PC-321 |
| G-322 | D-17 unknown recipient state is pending | PC-322 |
| G-323 | D-17 published conflicts are not repaired | PC-323 |
| G-324 | D-13 persisted workflow defaults | PC-324 |
| G-325 | D-13 release workflow type immutable | PC-325 |
| G-326 | D-13 move target workflow matches | PC-326 |
| G-327 | D-13 setup remains on existing board | PC-327 |
| G-328 | D-13 expected projection version | PC-328 |
| G-329 | D-13 exact intent/card relation | PC-329 |
| G-330 | D-13 release column ownership | PC-330 |
| G-331 | D-13 stage revision and receipt atomic | PC-331 |
| G-332 | D-13 first Full test timestamp | PC-332 |
| G-333 | D-13 legal release transitions | PC-333 |
| G-334 | D-13 generic release content edit | PC-334 |
| G-335 | D-13 generic release move | PC-335 |
| G-336 | D-13 generic release reopen | PC-336 |
| G-337 | D-13 generic release spawn | PC-337 |
| G-338 | D-13 generic release assignment | PC-338 |
| G-339 | D-13 generic release enqueue | PC-339 |
| G-340 | D-13 scheduled action creation | PC-340 |
| G-341 | D-13 scheduled action firing | PC-341 |
| G-342 | D-13 explicit task binding | PC-342 |
| G-343 | D-13 inherited task binding | PC-343 |
| G-344 | D-13 title task binding | PC-344 |
| G-345 | D-13 agent workflow execution | PC-345 |
| G-346 | D-13 Code pickup isolation | PC-346 |
| G-347 | D-13 Code pipeline depth isolation | PC-347 |
| G-348 | D-13 Code stage concurrency isolation | PC-348 |
| G-349 | D-13 generic transition sweep isolation | PC-349 |
| G-350 | D-13 generic retry isolation | PC-350 |
| G-351 | D-13 tracker sync isolation | PC-351 |
| G-352 | D-13 referenced shared-board retention | PC-352 |
| G-353 | D-13 release column retention | PC-353 |
| G-354 | D-13 release export privacy | PC-354 |
| G-355 | D-14 immutable report body | PC-355 |
| G-356 | D-14 pre-native outcomes still delivered | PC-356 |
| G-357 | D-14 distinct final acknowledgement | PC-357 |
| G-358 | D-14 PublishedAwaitingReport recovery | PC-358 |
| G-359 | D-14 atomic idempotent fix link | PC-359 |
| G-360 | D-14 same-board Code fix target | PC-360 |
| G-361 | D-14 filing a fix cannot dispatch | PC-361 |
| G-362 | D-14 failed identity immutable across successor | PC-362 |
| G-363 | D-14 included-card GUID authority | PC-363 |
| G-364 | D-14 previous release ancestry | PC-364 |
| G-365 | D-14 recipient ownership refusals | PC-365 |
| G-366 | D-14 standalone RC report has no credit | PC-366 |
| G-367 | D-18 immutable bounded chunk plan | PC-367 |
| G-368 | D-18 deadline capacity failure | PC-368 |
| G-369 | D-18 sequential native projects | PC-369 |
| G-370 | D-12 sanitized child environment | PC-370 |
| G-371 | D-18 script and client roster authority | PC-371 |
| G-372 | D-18 no RC Windmill activation | PC-372 |
| G-373 | D-18 master state isolation | PC-373 |
| G-374 | D-11 cooperative recovery time budget | PC-374 |
| G-375 | D-11 unique intent trigger identity | PC-375 |
| G-376 | D-11 unique candidate ref | PC-376 |
| G-377 | D-11 unique release card relation | PC-377 |
| G-378 | D-11 unique attempt generation | PC-378 |
| G-379 | D-11 unique outbox sequence | PC-379 |
| G-380 | D-11 unique fix fingerprint | PC-380 |
| G-381 | D-11 receipt belongs to Execute obligation | PC-381 |
| G-382 | D-16 controller digests rechecked per phase | PC-382 |
| G-383 | D-13 generic release creation | PC-383 |
| G-384 | D-13 Code column predicate at pickup | PC-384 |
| G-385 | D-13 physical capacity retained | PC-385 |
| G-386 | D-14 fix completion is not publication authority | PC-386 |
| G-387 | D-15 boolean coverage credit | PC-387 |
| G-388 | D-15 boolean tests credit | PC-388 |
| G-389 | D-15 boolean report credit | PC-389 |
| G-390 | D-13 expected prior release stage | PC-390 |
| G-391 | D-16 shared-before-lane lock order | PC-391 |
| G-392 | D-17 release API URL origin | PC-392 |
| G-393 | D-17 release API URL ID | PC-393 |
| G-394 | D-17 stable known release ID | PC-394 |
| G-395 | D-17 redirect secret custody | PC-395 |

### Positive controls

Each row specifies a compiling production defect, exact TUnit method and exact
assertion message (`C599 G-n: ...`) that must turn red. Code implements the
assertion over the real boundary; printing a PASS string, testing source text,
calling a no-op, returning a canned verdict or asserting fixture input against
itself is not an implementation. The valid arm must reach the recipient before
the single-invalid-field arm is accepted as coverage. Guards masked by a second
gate need a direct owning-boundary test plus the end-to-end control, not removal
of two guards at once. Build errors, fixture errors, timeouts or zero executions
are not a red control.

**Execution ownership:** Code runs ordinary V/R. Review judges the implementation
and control executability before land. Mutation runs break/red/restore/green after
land under SourceLanding, externally retains evidence and never commits the
snapshot. Do not run these controls during this TestDesign or bounded Code round.
Every numbered PC uses only the exact method in its row:
`--treenode-filter '/*/*/ClassName/ExactMethod'`; never a class wildcard.
Retain per-variant red assertion and restored-green evidence. Restore source
timestamps or force the isolated rebuild to avoid a mutated cached DLL.
Unique-index mutants change the EF model and CLI-generated migration in the
isolated snapshot, then create a fresh migrated fixture DB; do not drop indexes
from a shared/live database. Their six-minute cycle estimate includes generation.

| PC | Compiling defect applied to its matching G | Exact method that must turn red | Exact assertion after the prefix C599 G-n: | Cycle EstimatedMinutes |
|---|---|---|---|---:|
| PC-207 | remove Enabled check with persisted admission true | `ReleaseGateSchedulerTests.C599A_Admission` | disabled feature creates zero intents | 4 |
| PC-208 | register release workers despite ServerEnabled=false | `ReleaseGateSchedulerTests.C599A_Admission` | both release workers absent and zero native starts | 4 |
| PC-209 | compare supplied root instead of actual ContentRoot | `ReleaseGateSchedulerTests.C599A_Admission` | linked or reparse-aliased worktree refuses admission | 4 |
| PC-210 | omit normalized remote comparison | `ReleaseGateSchedulerTests.C599A_Admission` | foreign remote produces zero intents | 4 |
| PC-211 | omit configured project repository comparison | `ReleaseGateSchedulerTests.C599A_Admission` | foreign project produces zero intents | 4 |
| PC-212 | omit Board.ProjectId check | `ReleaseGateSchedulerTests.C599A_Admission` | foreign-project BoardId produces zero intents | 4 |
| PC-213 | use UTC zone for slot calculation | `ReleaseGateSchedulerTests.C599A_Slots` | September fires at 07:30 and 15:30 UTC | 4 |
| PC-214 | copy caller scheduled provenance into manual intent | `ReleaseGateSchedulerTests.C599A_Provenance` | manual key has no slot and no scheduled credit | 4 |
| PC-215 | append a nonce to the scheduled trigger key | `ReleaseGateSchedulerTests.C599A_Slots` | repeat returns identical intent and card IDs | 4 |
| PC-216 | enqueue Execute before transaction commit | `ReleaseGateQueueTests.C599A_HandoffRecovery` | a blocked commit exposes no job or native start | 6 |
| PC-217 | omit pending Execute scan on fresh host | `ReleaseGateQueueTests.C599A_HandoffRecovery` | fresh queue reaches same-intent native terminal receipt | 6 |
| PC-218 | clear CandidateId and RunId on lost acknowledgement | `ReleaseGateQueueTests.C599A_HandoffRecovery` | recovery retains both original IDs | 6 |
| PC-219 | change failed outcome to executable retry state | `ReleaseGateSchedulerTests.C599A_RepairDisposition` | second scan/resume has zero extra test starts | 4 |
| PC-220 | move acquisition after PrepareCut | `ReleaseGateQueueTests.C599A_LockScope` | contender has zero clone fetch or push calls | 6 |
| PC-221 | classify old live owner as stale | `ReleaseGateQueueTests.C599A_LockScope` | aged live owner retains lock and contender is deferred-busy | 6 |
| PC-222 | dispose shared lease on FullTest return | `ReleaseGateQueueTests.C599A_LockScope` | contender cannot start during publish or descendant barrier | 6 |
| PC-223 | return shutdown completion before cancellation join | `ReleaseGateQueueTests.C599A_DisableAndShutdown` | shutdown receipt exists only after owned descendant exit | 6 |
| PC-224 | read requiredSuites from summary | `ReleaseGatePublicationAuthorityTests.C599A_PinnedPolicy` | seven-suite report cannot publish eight-suite policy | 4 |
| PC-225 | treat absent ExpectedPolicyHash as no hash requirement | `ReleaseGatePublicationAuthorityTests.C599A_PinnedPolicy` | wrong pinned hash blocks before any remote write | 4 |
| PC-226 | accept first passing chunk per suite | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | passing sibling cannot hide failed chunk | 4 |
| PC-227 | discard gh stdout and return empty Body | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | valid fixture stdout yields numeric release ID 701 | 4 |
| PC-228 | ignore final read command nonzero exit | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | nonzero read leaves published unset | 4 |
| PC-229 | default malformed or missing Body to success | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | empty and malformed JSON leave published unset | 4 |
| PC-230 | coerce missing draft to false | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | missing and string-false draft refuse | 4 |
| PC-231 | accept string or nonpositive ID | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | string R-701 and zero ID refuse | 4 |
| PC-232 | omit API repository path comparison | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | foreign repository API URL refuses | 4 |
| PC-233 | omit tag_name equality | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | different tag refuses | 4 |
| PC-234 | replace manifest byte digest equality with true | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | corrupt manifest blocks publish | 4 |
| PC-235 | replace summary byte digest equality with true | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | corrupt summary blocks publish | 4 |
| PC-236 | allow clobber when existing digest differs | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | conflicting remote bytes and asset ID remain unchanged | 4 |
| PC-237 | enqueue ProjectCard before saving outcome/outbox transaction | `ReleaseGateReportDeliveryTests.C599A_ProjectionCommitCuts` | consumer cannot observe uncommitted report | 8 |
| PC-238 | acknowledge on projection response without fresh DB read | `ReleaseGateReportDeliveryTests.C599A_ProjectionCommitCuts` | wrong persisted body leaves obligation pending; independent GET exposes mismatch | 8 |
| PC-239 | create a new card after committed projection response loss | `ReleaseGateReportDeliveryTests.C599A_ProjectionCommitCuts` | recovery retains one card and one sequence revision | 8 |
| PC-240 | route RC event to legacy nightly incident updater | `ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery` | nightly card body revision assignment and state are unchanged | 8 |
| PC-241 | omit persisted admission reread at dequeue | `ReleaseGateSchedulerTests.C599A_DisabledReentry` | disabled queued intent has zero native starts | 4 |
| PC-242 | set native-complete on BackgroundJobClient return | `ReleaseGateQueueTests.C599A_ReadyAndBusy` | busy queue has no terminal native receipt | 6 |
| PC-243 | ignore registration AdmissionEnabled with static Enabled true | `ReleaseGateSchedulerTests.C599A_Admission` | disabled registration creates no intent | 4 |
| PC-244 | ignore ScheduleEnabled window membership | `ReleaseGateSchedulerTests.C599A_Slots` | slot outside enabled window has no runnable intent | 4 |
| PC-245 | cache PublicationEnabled at worker start | `ReleaseGateSchedulerTests.C599A_DisabledReentry` | disable between uploads prevents the next GitHub write | 4 |
| PC-246 | check enabled only at dequeue | `ReleaseGateSchedulerTests.C599A_DisabledReentry` | disable after PrepareCut blocks PushCut | 4 |
| PC-247 | automatically enqueue held-disabled work on enable | `ReleaseGateSchedulerTests.C599A_DisabledReentry` | held intent stays held until explicit resume | 4 |
| PC-248 | accept delegated capability as release operator | `ReleaseGateSchedulerTests.C599A_Admission` | capability and nonlocal requests change no control state | 4 |
| PC-249 | save registration from preview path | `ReleaseGateBoardBoundaryTests.C599A_SetupAndTypes` | preview leaves board columns registration and jobs unchanged | 6 |
| PC-250 | omit expected configuration version comparison | `ReleaseGateSchedulerTests.C599A_Admission` | stale control version refuses without changing flags | 4 |
| PC-251 | adopt conflicting release StateKey column | `ReleaseGateBoardBoundaryTests.C599A_SetupAndTypes` | foreign column collision refuses without converting Code column | 6 |
| PC-252 | admit an uncommitted slot after grace | `ReleaseGateSchedulerTests.C599A_Slots` | 15 minutes plus one tick is missed-slot with zero native starts | 4 |
| PC-253 | reset scan cursor to start of prior day | `ReleaseGateSchedulerTests.C599A_Slots` | initial enable has no historical runs and restart preserves missed outcomes | 4 |
| PC-254 | remove Take(100) from due obligation query | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | 101 due rows process at most 100 in one invocation | 6 |
| PC-255 | persist next-attempt only after enqueue returns | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | enqueue failure still records due time and 1 2 5 minute capped progression | 6 |
| PC-256 | remove expected version from claim update | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | two competing workers produce one winning generation and native start | 6 |
| PC-257 | save initial card in an earlier committed transaction | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | injected outbox insert failure leaves zero intent card and outbox rows | 6 |
| PC-258 | remove registration repository/project unique index from model and generated migration | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | direct duplicate registration insert fails with unique violation | 6 |
| PC-259 | hold admission transaction while enqueue waits | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | independent context can update admitted row while queue response is blocked | 6 |
| PC-260 | mark Execute outbox delivered on enqueue ID | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | enqueue-only state leaves delivery obligation open until matching claim/start receipt | 6 |
| PC-261 | enqueue while owner is demonstrably live | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | three recovery scans add no jobs for held live attempt | 6 |
| PC-262 | treat unknown owner probe as dead | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | unknown owner preserves lock and produces zero new attempts | 6 |
| PC-263 | set AutomaticRetry Attempts=1 on release jobs | `ReleaseGateQueueTests.C599A_ReadyAndBusy` | faulted release job has no Hangfire retry and durable outcome stays authoritative | 6 |
| PC-264 | route long release execution to default queue | `ReleaseGateQueueTests.C599A_ReadyAndBusy` | default maintenance receipt arrives while release-gates worker is busy | 6 |
| PC-265 | prefer newest timestamp on identity conflict | `ReleaseGateQueueTests.C599A_HandoffRecovery` | mismatched journal stays held with no remote write | 6 |
| PC-266 | adopt another intent candidate ref on uniqueness conflict | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | second intent reports candidate-name-conflict and original ref remains owned | 6 |
| PC-267 | resume native root before AssignProcessToJobObject succeeds | `ReleaseGateOwnershipContractTests.C599A_Containment` | forced assignment failure leaves no payload marker | 6 |
| PC-268 | resume before saving PID/start/attempt | `ReleaseGateOwnershipContractTests.C599A_Containment` | attempt-save failure leaves no payload marker and child is joined | 6 |
| PC-269 | enable BREAKAWAY_OK on release Job Object | `ReleaseGateOwnershipContractTests.C599A_Containment` | breakaway child fails escape and is included in owned cleanup | 6 |
| PC-270 | omit KILL_ON_JOB_CLOSE | `ReleaseGateOwnershipContractTests.C599A_Containment` | abrupt owner death leaves zero live owned descendants | 6 |
| PC-271 | make controller Job Object handle inheritable | `ReleaseGateOwnershipContractTests.C599A_Containment` | host death kills descendant even when child remains waiting | 6 |
| PC-272 | accept root exit without ActiveProcesses zero | `ReleaseGateQueueTests.C599A_DisableAndShutdown` | root exited with child alive has no terminal cleanup receipt | 6 |
| PC-273 | compare only PID when removing stale RC lock | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | reused PID or mismatched boot/start/generation preserves lock | 6 |
| PC-274 | treat access denied during job census as empty | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | inaccessible job holds recovery and preserves lock | 6 |
| PC-275 | delete stale nightly-process lock from RC recovery | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | master v1 and v2 lock bytes remain unchanged | 6 |
| PC-276 | let nightly child own/delete borrowed outer lease | `ReleaseGateQueueTests.C599A_LockScope` | child exit retains outer shared lock | 6 |
| PC-277 | launch later phase from current canonical checkout | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | canonical edit cannot change later child marker from frozen controller | 6 |
| PC-278 | use controller tool directory for candidate test script | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | test phase records candidate script digest distinct from controller digest | 6 |
| PC-279 | allow missing intent/owner envelope in PrepareCut | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | direct script call is diagnostic with zero cut or publish writes | 6 |
| PC-280 | refetch master when DB pin acknowledgement is missing | `ReleaseGateQueueTests.C599A_HandoffRecovery` | saved preparation imports original SHA after master advances | 6 |
| PC-281 | assume push failure means remote ref absent | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | lost push response reads same remote SHA before any repeat write | 6 |
| PC-282 | requeue dead incomplete FullTest as a fresh test phase | `ReleaseGateQueueTests.C599A_DisableAndShutdown` | interrupted phase has zero replacement test starts | 6 |
| PC-283 | rerun FullTest after terminal receipt acknowledgement loss | `ReleaseGateQueueTests.C599A_HandoffRecovery` | valid complete receipt imports same RunId with test start count one | 6 |
| PC-284 | skip using a pending release or old policy hash | `ReleaseGateSchedulerTests.C599A_RepairDisposition` | pending or old-policy same SHA never gets no-new-SHA credit | 4 |
| PC-285 | read policy from working tree instead of git blob | `ReleaseGatePublicationAuthorityTests.C599A_PinnedPolicy` | changed working policy cannot alter frozen suite authority | 4 |
| PC-286 | accept unknown schema or empty rc profile | `ReleaseGatePublicationAuthorityTests.C599A_PinnedPolicy` | unknown schema profile and each absent suite refuse | 4 |
| PC-287 | accept evidence symlink outside candidate root | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | outside and reparse-escaped evidence refuse before publication | 4 |
| PC-288 | ignore failed required build row | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | each failed or absent required prerequisite blocks remote writes | 4 |
| PC-289 | infer success from TRX when native exit is nonzero | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | nonzero child exit or zero relevant executions blocks | 4 |
| PC-290 | ignore missing or unexpected chunk IDs | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | deleted or extra chunk blocks authority | 4 |
| PC-291 | deduplicate UIDs before checking coverage | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | duplicate UID in sibling chunk refuses | 4 |
| PC-292 | count skipped or unknown UID as pass | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | required skip unknown missing and stale rows refuse | 4 |
| PC-293 | accept empty required roster | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | zero-required ledger cannot publish | 4 |
| PC-294 | trust summary counts instead of raw discovery/TRX | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | inflated pass count refuses even with plausible summary | 4 |
| PC-295 | reload exclusions after test results | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | post-result exclusion edit cannot remove a failed UID | 4 |
| PC-296 | omit intent equality when joining receipts | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | foreign intent evidence refuses | 4 |
| PC-297 | omit candidate ID/ref equality | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | foreign candidate evidence refuses | 4 |
| PC-298 | omit full SHA equality on ledger entries | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | foreign or abbreviated SHA refuses | 4 |
| PC-299 | omit native RunId equality | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | foreign run evidence refuses | 4 |
| PC-300 | accept changed script or authority digest | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | modified input digest blocks publication | 4 |
| PC-301 | ignore end-before-start or stale evidence timestamps | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | out-of-order or stale evidence refuses | 4 |
| PC-302 | coerce unknown cleanup to success | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | missing false or string cleanup cannot publish | 4 |
| PC-303 | ignore seam-driven marker in RC authority | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | seam-driven ledger creates no release | 4 |
| PC-304 | ignore NoReport marker | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | NoReport ledger creates no release | 4 |
| PC-305 | allow subset or diagnostic selection in authority | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | subset and diagnostic ledgers create no release | 4 |
| PC-306 | accept final receipt where prepublication receipt is required | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | wrong receipt kind cannot mint complete-green | 4 |
| PC-307 | rewrite sanitized summary after journaling its digest | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | modified summary leaves publication pending and original digest unchanged | 4 |
| PC-308 | accept arbitrary html_url | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | foreign origin userinfo http or wrong repository/tag URL refuses | 4 |
| PC-309 | trust target_commitish instead of remote peel | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | matching target_commitish with wrong peeled SHA refuses | 4 |
| PC-310 | treat incomplete draft listing as absent | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | draft on page two is adopted and failed page creates no new release | 4 |
| PC-311 | stop asset enumeration after first page | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | required asset on page two is found and later duplicate refuses | 4 |
| PC-312 | accept first matching name among duplicates | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | two assets with required name block publish | 4 |
| PC-313 | omit asset release/URL association check | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | foreign release asset cannot supply receipt | 4 |
| PC-314 | accept new/uploading asset state | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | not-uploaded asset blocks publish | 4 |
| PC-315 | download by name without validating asset ID | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | missing or invalid asset ID cannot supply receipt | 4 |
| PC-316 | ignore byte size mismatch | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | unexpected size blocks even if reported digest matches | 4 |
| PC-317 | reuse pre-publish release object as final receipt | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | post-publish changed release ID prevents published bit | 6 |
| PC-318 | reuse pre-publish asset digests | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | asset changed after publish leaves remote-conflict pending | 6 |
| PC-319 | write draft before persisting publication intent | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | blocked journal commit permits zero remote writes | 6 |
| PC-320 | upload again when same bytes already exist | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | equal remote bytes cause zero repeat upload | 6 |
| PC-321 | reserve next N on response loss | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | all write cuts retain original tag and one reservation | 6 |
| PC-322 | treat auth/network read failure as not-found | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | unavailable readback creates no duplicate release | 6 |
| PC-323 | delete mismatched published asset then upload | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | published unequal asset remains unchanged and outcome is remote-conflict | 6 |
| PC-324 | default migrated card or column WorkflowKind to Release | `ReleaseGateBoardBoundaryTests.C599A_SetupAndTypes` | old rows and ordinary newly created cards remain Code | 6 |
| PC-325 | allow generic edit of WorkflowKind | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | attempt to convert either card type is refused | 6 |
| PC-326 | select target column by status without workflow | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | Code card cannot enter Release column and inverse also refuses | 6 |
| PC-327 | create a new Releases board during setup | `ReleaseGateBoardBoundaryTests.C599A_SetupAndTypes` | setup retains BoardId and board count is unchanged | 6 |
| PC-328 | omit optimistic projection version comparison | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | stale version changes no card revision even with legal next stage | 6 |
| PC-329 | project using caller-supplied card GUID | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | foreign card remains unchanged and receipt absent | 6 |
| PC-330 | accept target column from another workflow/registration | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | foreign column refuses without move | 6 |
| PC-331 | commit card move before receipt insert | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | receipt insert failure rolls back card revision and token | 6 |
| PC-332 | derive StartedAt only from IsActive | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | first Full test sets StartedAt once despite inactive column | 6 |
| PC-333 | allow Cut directly to Released | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | illegal edge has zero revisions and no Released verdict | 6 |
| PC-334 | omit release check in content edit path | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | edit refuses and original body and private visibility remain | 6 |
| PC-335 | omit release check in MoveAsync | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | manual same-workflow move refuses without revision | 6 |
| PC-336 | omit release check in ReopenAsync | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | reopen refuses without revision | 6 |
| PC-337 | omit release check in SpawnAsync | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | spawn refuses with zero launch calls | 6 |
| PC-338 | omit release check in assignment path | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | assignment refuses with no assigned agent | 6 |
| PC-339 | omit release check in agent queue path | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | enqueue refuses with no queue position | 6 |
| PC-340 | omit workflow check at schedule admission | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | release schedule creation refuses even with acceptSpend | 6 |
| PC-341 | omit workflow check at schedule execution | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | seeded legacy schedule fire changes no release card | 6 |
| PC-342 | omit workflow check after explicit resolution | `ReleaseGateBoardBoundaryTests.C599A_TaskBinding` | GUID and identifier binding refuse with zero new tasks | 6 |
| PC-343 | omit workflow check for inherited card | `ReleaseGateBoardBoundaryTests.C599A_TaskBinding` | parent follow-up and merge inheritance refuse release binding | 6 |
| PC-344 | omit workflow check after title resolution | `ReleaseGateBoardBoundaryTests.C599A_TaskBinding` | release identifier in title cannot create a bound task | 6 |
| PC-345 | allow CardWorkflowRunFactory on Release card | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | release card creates no workflow run | 6 |
| PC-346 | remove card WorkflowKind predicate from eligible query | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | active-looking Release card never dispatches while Code control does | 6 |
| PC-347 | include Release cards in Code depth projection | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | adding two release intents leaves Code depth unchanged | 6 |
| PC-348 | count Release workflow as Code stage occupant | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | release execution does not block second Code slot and third Code still refuses | 6 |
| PC-349 | remove release exclusion from CardWorkTransitionService | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | seeded legacy succeeded task cannot move Release card | 6 |
| PC-350 | include Release card in RetryScheduler eligible query | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | due legacy retry cannot launch Release card | 6 |
| PC-351 | allow external tracker lookup/update on Release card | `ReleaseGateBoardBoundaryTests.C599A_ProtectionAndTracker` | tracker update preserves Release card and still updates Code control | 6 |
| PC-352 | allow archive/delete with live release registration | `ReleaseGateBoardBoundaryTests.C599A_ProtectionAndTracker` | board archive and delete refuse and release history remains | 6 |
| PC-353 | allow generic edits/removal of registered release columns | `ReleaseGateBoardBoundaryTests.C599A_ProtectionAndTracker` | release column edit/delete refuses while Code column edit succeeds | 6 |
| PC-354 | create release card with Inherit visibility | `ReleaseGateBoardBoundaryTests.C599A_SetupAndTypes` | release card is Private while board export configuration is unchanged | 6 |
| PC-355 | replace existing outbox sequence body on conflict | `ReleaseGateReportDeliveryTests.C599A_ProjectionCommitCuts` | same sequence with different digest holds and original bytes remain | 8 |
| PC-356 | return early when candidate or RunId is null | `ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery` | clone identity and lock failures reach exact card body with null native IDs | 8 |
| PC-357 | set Released when only prepublication receipt exists | `ReleaseGateReportDeliveryTests.C599A_PublishedReportCuts` | prepublication receipt alone leaves stage Publish | 8 |
| PC-358 | rerun publisher when final projection acknowledgement is lost | `ReleaseGateReportDeliveryTests.C599A_PublishedReportCuts` | final report recovery performs zero additional publish writes | 8 |
| PC-359 | save fix card outside link transaction | `ReleaseGateReportDeliveryTests.C599A_FixDeliveryCuts` | lost response and link-save failure leave one linked fix or no partial row | 8 |
| PC-360 | choose first Backlog column without WorkflowKind | `ReleaseGateReportDeliveryTests.C599A_FixDeliveryCuts` | fix BoardId equals release BoardId and column is Code Backlog | 8 |
| PC-361 | clear auto-dispatch hold and enqueue new fix | `ReleaseGateReportDeliveryTests.C599A_FixDeliveryCuts` | fix creation has no assignment task session or launch | 8 |
| PC-362 | reuse failed intent/card for new master SHA | `ReleaseGateReportDeliveryTests.C599A_FixDeliveryCuts` | successor has new intent/card and old failed SHA and verdict stay unchanged | 8 |
| PC-363 | resolve changelog CARD-number by title | `ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery` | same-number foreign board card is never attributed | 8 |
| PC-364 | generate range without ancestor check | `ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery` | nonancestor previous SHA is explicit unknown pending disposition | 8 |
| PC-365 | overwrite assigned or foreign release card on readback mismatch | `ReleaseGateReportDeliveryTests.C599A_ProjectionCommitCuts` | missing assigned and foreign recipients remain held with no acknowledgement | 8 |
| PC-366 | mint report-delivered without durable intent | `ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery` | standalone RC script cannot create publication credit | 8 |
| PC-367 | execute one entire native assembly instead of frozen class chunks | `ReleaseGateActivationContractTests.C599A_ChunkExecution` | two-class probe runs two disjoint chunks matching frozen roster | 4 |
| PC-368 | increase watchdog after a timed-out chunk | `ReleaseGateActivationContractTests.C599A_ChunkExecution` | timeout preserves policy deadline and blocks full credit | 4 |
| PC-369 | launch agents-pty while Antiphon.Tests child is held | `ReleaseGateActivationContractTests.C599A_ChunkExecution` | observed active native process maximum is one | 4 |
| PC-370 | inherit task token or live-test approval into child | `ReleaseGateActivationContractTests.C599A_EnvelopeIsolation` | child sees none of the forbidden credential/approval overrides | 4 |
| PC-371 | drop an eligible script/client entry from execution manifest | `ReleaseGateActivationContractTests.C599A_ChunkExecution` | missing declared entry blocks full credit | 4 |
| PC-372 | allow register-release-gates EnableSchedule rc | `ReleaseGateActivationContractTests.C599A_EnvelopeIsolation` | RC request creates no Windmill script or schedule | 4 |
| PC-373 | write RC complete-green into master state root | `ReleaseGateActivationContractTests.C599A_EnvelopeIsolation` | all four master sentinel files remain byte-identical | 4 |
| PC-374 | omit elapsed-time/cancellation check between scan items | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | after 30-second fake elapsed boundary no next obligation is visited | 6 |
| PC-375 | remove registration/trigger unique index from model and generated migration | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | direct duplicate slot/manual key insert fails with unique violation | 6 |
| PC-376 | remove non-null candidate ref unique index | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | second intent cannot persist same candidate ref | 6 |
| PC-377 | remove ReleaseCardId unique index | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | two intents cannot bind one release card | 6 |
| PC-378 | remove intent/generation unique index | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | duplicate generation insert fails with unique violation | 6 |
| PC-379 | remove intent/kind/sequence unique index | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | duplicate delivery sequence insert fails with unique violation | 6 |
| PC-380 | remove intent/fingerprint unique index | `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | concurrent duplicate finding cannot create two links | 6 |
| PC-381 | retire Execute from another generation start receipt | `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | foreign generation receipt leaves original delivery pending | 6 |
| PC-382 | omit frozen tool directory digest recheck | `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | tampered frozen script blocks next phase before child start | 6 |
| PC-383 | honor caller WorkflowKind=Release in generic create | `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | generic create refuses Release with no card row | 6 |
| PC-384 | remove column WorkflowKind predicate only | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | Code-typed corrupt card in Release column is not eligible | 6 |
| PC-385 | exclude all Release-bound sessions from physical capacity count | `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | live legacy-bound session still consumes real process capacity | 6 |
| PC-386 | let linked fix Done bypass failed RC outcome | `ReleaseGateReportDeliveryTests.C599A_FixDeliveryCuts` | fix Done alone cannot publish failed candidate | 8 |
| PC-387 | coerce coverageComplete string true to true | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | string coverage flag cannot publish | 4 |
| PC-388 | coerce testsPassed string true to true | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | string test flag cannot publish | 4 |
| PC-389 | coerce reportDelivered string true to true | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | string report flag cannot publish | 4 |
| PC-390 | accept reordered event with current version but wrong prior stage | `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | wrong prior stage changes no card revision | 6 |
| PC-391 | acquire lane lock before shared lock | `ReleaseGateQueueTests.C599A_LockScope` | recorded acquisition order is shared then lane for master and RC | 6 |
| PC-392 | accept arbitrary API URL origin while preserving expected path | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | foreign origin http or userinfo API URL refuses | 4 |
| PC-393 | omit numeric release ID equality with API URL ID | `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | release body ID 701 with API URL ID 702 refuses | 4 |
| PC-394 | accept a different release ID on recovery readback | `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | same tag with changed known release ID stays remote-conflict | 6 |
| PC-395 | persist raw signed download redirect URL in evidence | `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | fixture signed query marker is absent from logs journals and public bytes | 4 |

### Out of scope

- No production configuration, Hangfire registration, Windmill RC activation,
  manual release, scheduled slot observation, publication or local deployment
  occurs in B1..B8. Q-1..Q-4 are explicitly commissioned operations after land.
- S5 master/nightly readiness, independent watchdog notices and Interim pilot stay
  separately open with their original receipts and controls. RC green earns no
  master readiness. No new Telegram/email/session delivery is in this design.
- Full Unit/all-integration/native/Slow/client/E2E runs are absent from each small
  ordinary CP by operator policy, not treated as passed. All eligible full suites
  remain required by every publishable RC. Current profile exclusions retain
  their candidate-pinned owners/reasons; no duration-based exclusions are added.
- Linux/QEMU/provider/headed qualification, new workflow UI and a general workflow
  engine are excluded. Existing board display gets typed DTOs; backend mutation
  refusals protect release ownership. No live foreign process is killed by a PC.
- The fixed times avoid the ambiguous DST hour. Exhaustive combinations of two
  independently invalid fields are excluded as described above; every single
  boundary and real handoff cut remains covered.

### Checkpoints

This is the **only authoritative ordinary manifest** for B1..B8. The historical
proposal above and CP-1..18 are not extra rows to run. Each round runs its own
row once after commit, repairs/reruns that row if red, and reports the exact SHA,
counts, failures and reruns. A later round does not replay all prior CPs.
Union = V-18..25/R-9..16, the whole operator-approved ordinary scope. All 19 methods
must exist and exercise production behavior; no skips. Full matrix methods are
outside this ordinary filter except the five bounded scheduler methods in CP-25.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-19 | B1 committed | `tests/Antiphon.Tests -> bin-c599b1/` | authority | `/*/*/ReleaseGateAuthorityContractTests/C599B_*` | V-18, R-9 | C599B_PinnedPolicy, C599B_AllChunks; 2 pass, 0 fail/skip | 2 | 3 |
| CP-20 | B2 committed | `tests/Antiphon.Tests -> bin-c599b2/` | recipient | `/*/*/ReleaseGateRecipientContractTests/C599B_*` | V-19, R-10 | C599B_ProcessReceipt, C599B_AssetBytes; 2 pass, 0 fail/skip | 2 | 3 |
| CP-21 | B3 committed | `tests/Antiphon.Tests -> bin-c599b3/` | durable-intent | `/*/*/ReleaseGateIntentTests/C599B_*` | V-20, R-11 | C599B_AtomicIntent, C599B_RecoverQueueLoss; 2 pass, 0 fail/skip | 2 | 3 |
| CP-22 | B4 committed | `tests/Antiphon.Tests -> bin-c599b4/` | release-workflow | `/*/*/ReleaseGateBoardTests/C599B_*` | V-21, R-12 | C599B_StageMoves, C599B_CodeIsolation; 2 pass, 0 fail/skip | 2 | 3 |
| CP-23 | B5 committed | `tests/Antiphon.Tests -> bin-c599b5/` | report-receipts | `/*/*/ReleaseGateReportContractTests/C599B_*` | V-22, R-13 | C599B_PreNativeReport, C599B_FixAndFinalReceipt; 2 pass, 0 fail/skip | 2 | 3 |
| CP-24 | B6 committed | `tests/Antiphon.Tests -> bin-c599b6/` | owned-coordinator | `/*/*/ReleaseGateOwnershipContractTests/C599B_*` | V-23, R-14 | C599B_LockBeforeCut, C599B_JoinBeforeRelease; 2 pass, 0 fail/skip | 2 | 3 |
| CP-25 | B7 committed | `tests/Antiphon.Tests -> bin-c599b7/` | scheduler | `/*/*/ReleaseGateSchedulerTests/C599A_*` | V-24, R-15 | C599A_Admission, C599A_Slots, C599A_Provenance, C599A_DisabledReentry, C599A_RepairDisposition; 5 pass, 0 fail/skip | 5 | 3 |
| CP-26 | B8 committed | `tests/Antiphon.Tests -> bin-c599b8/` | activation-contract | `/*/*/ReleaseGateActivationContractTests/C599B_*` | V-25, R-16 | C599B_ChunkUnion, C599B_CorrelationAndMasterIsolation; 2 pass, 0 fail/skip | 2 | 3 |

Run the chosen row through `scripts/run-checkpoint.ps1`, preserving the table's
OutputPath, filter, Min and complete Expect roster. B1's concrete command is:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-19 -Project tests/Antiphon.Tests -OutputPath bin-c599b1/ -Filter '/*/*/ReleaseGateAuthorityContractTests/C599B_*' -MinExecuted 2 -Expect C599B_PinnedPolicy,C599B_AllChunks -ResultsRoot .antiphon/c599b1-checkpoints
```

Use a fresh ResultsRoot for any rerun. OutputPath ends in a forward slash.
Record the producer-owned output inventory before cleanup and resolve each path
within this checkout before deleting it. A cold build overrun is reported, never
hidden by weakening tests, raising timeouts or calling an incomplete run green.
B3's required CLI migration generation is a separately reported authoring build,
not permission for an extra test run. Final Review/land remains exact-SHA; this
ordinary-scope exception does not enable Interim.

#### Full Q-2 / RC control-method roster (not ordinary CP rows)

These are the 30 exact filters for the isolated rehearsal, with one executed
TUnit result per method, all named assertions/variants required and no skips.
They are also full RC census obligations. Build the Antiphon.Tests and
Antiphon.E2E graphs once into `bin-c599q/` and `bin-c599qe/`, including fixture
children; prepare current client bundle/browser for the E2E fixture. Run the
methods sequentially using the indicated project with `--no-build`, their exact
filter and a fresh TRX directory. This explicit rehearsal costs ten setup/build
minutes plus the method times below; it is not concealed in a three-minute CP.

| Project | Exact filter | Expected TUnit executions | Estimated run minutes |
|---|---|---:|---:|
| Antiphon.Tests | `/*/*/ReleaseGateSchedulerTests/C599A_Admission` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGateSchedulerTests/C599A_Slots` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGateSchedulerTests/C599A_Provenance` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGateSchedulerTests/C599A_DisabledReentry` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGateSchedulerTests/C599A_RepairDisposition` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGateQueueTests/C599A_HandoffRecovery` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateQueueTests/C599A_LockScope` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateQueueTests/C599A_DisableAndShutdown` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateQueueTests/C599A_ReadyAndBusy` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateQueueTests/C599A_ClaimsAndRecovery` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_PinnedPolicy` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_ExecutionLedger` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_GitHubReadback` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_AssetReadback` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_RemoteWriteRecovery` | 1 | 2 |
| Antiphon.E2E | `/*/*/ReleaseGateReportDeliveryTests/C599A_QueuedReportRecovery` | 1 | 3 |
| Antiphon.E2E | `/*/*/ReleaseGateReportDeliveryTests/C599A_ProjectionCommitCuts` | 1 | 3 |
| Antiphon.E2E | `/*/*/ReleaseGateReportDeliveryTests/C599A_FixDeliveryCuts` | 1 | 3 |
| Antiphon.E2E | `/*/*/ReleaseGateReportDeliveryTests/C599A_PublishedReportCuts` | 1 | 3 |
| Antiphon.Tests | `/*/*/ReleaseGateBoardBoundaryTests/C599A_SetupAndTypes` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateBoardBoundaryTests/C599A_StageTransitions` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateBoardBoundaryTests/C599A_GenericMutations` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateBoardBoundaryTests/C599A_TaskBinding` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateBoardBoundaryTests/C599A_CodePipeline` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateBoardBoundaryTests/C599A_ProtectionAndTracker` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateIntentTests/C599A_AtomicityAndConstraints` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateOwnershipContractTests/C599A_Containment` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateOwnershipContractTests/C599A_IdentityAndTools` | 1 | 2 |
| Antiphon.Tests | `/*/*/ReleaseGateActivationContractTests/C599A_ChunkExecution` | 1 | 1 |
| Antiphon.Tests | `/*/*/ReleaseGateActivationContractTests/C599A_EnvelopeIsolation` | 1 | 1 |

No single method may exceed its fixture/process timeout. Factor shared setup and
pure-data rows rather than extending deadlines. If real measurements exceed this
reservation, report the measured capacity finding and revise the scope before an
additional run; do not silently drop a cut. The rehearsal must finish before Q-3.

### Cost

All times below are **estimates**, not measured outcomes. This TestDesign ran zero
builds/tests/PCs and generated no alternate output directories.

- **Ordinary Code V/R floor = 24 minutes**, CP-19..26 at 3 each: 12 minutes of
  isolated incremental builds plus 12 of scoped V/R. Filters and Min are the table
  above. One-time fixture/tool setup and B3 migration generation/build reserve
  another 6 minutes, so ordinary verification/setup totals **30 minutes** across
  eight rounds. The plan's 670 active authoring-plus-CP minutes become **676**
  including that explicit setup allowance; measure cold-build differences.
- **Mutation floor = 1010 minutes for 189 PCs**,
  the exact PC-row cycle times summed once each. Each four-minute cycle reserves
  1.5 red build + 0.5 red execution + 1.5 restore/rebuild + 0.5 green execution;
  six-minute cycles use 1.5-minute red and green executions (index controls
  allocate their equivalent extra time to CLI generation); eight-minute API
  cycles use 2.5-minute red and green executions. No batch saving is assumed.
  Add 10 minutes for sourced snapshot discovery/baseline/fixture setup:
  **1020 minutes Mutation budget**, separate from Code.
  Matrix variants within a row must each hit the named assertion; if a mutation
  changes only one shape, run and retain that shape's red/green rather than
  claiming the other shapes were killed.
- **Combined ordinary setup/build + V/R + post-land Mutation = 1050 minutes**.
  This excludes authoring, Review time, inherited PC-1..206 and live qualification.
  The per-method PC subtotals below identify where the Mutation floor is spent.

| Exact method (use /*/*/Class/Method) | PCs | Estimated cycle minutes subtotal |
|---|---:|---:|
| `ReleaseGateSchedulerTests.C599A_Admission` | 9 | 36 |
| `ReleaseGateSchedulerTests.C599A_Slots` | 5 | 20 |
| `ReleaseGateSchedulerTests.C599A_Provenance` | 1 | 4 |
| `ReleaseGateSchedulerTests.C599A_DisabledReentry` | 4 | 16 |
| `ReleaseGateSchedulerTests.C599A_RepairDisposition` | 2 | 8 |
| `ReleaseGateQueueTests.C599A_HandoffRecovery` | 6 | 36 |
| `ReleaseGateQueueTests.C599A_LockScope` | 5 | 30 |
| `ReleaseGateQueueTests.C599A_DisableAndShutdown` | 3 | 18 |
| `ReleaseGateQueueTests.C599A_ReadyAndBusy` | 3 | 18 |
| `ReleaseGateQueueTests.C599A_ClaimsAndRecovery` | 8 | 48 |
| `ReleaseGatePublicationAuthorityTests.C599A_PinnedPolicy` | 4 | 16 |
| `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedger` | 24 | 96 |
| `ReleaseGatePublicationAuthorityTests.C599A_GitHubReadback` | 12 | 48 |
| `ReleaseGatePublicationAuthorityTests.C599A_AssetReadback` | 11 | 44 |
| `ReleaseGatePublicationAuthorityTests.C599A_RemoteWriteRecovery` | 9 | 54 |
| `ReleaseGateReportDeliveryTests.C599A_QueuedReportRecovery` | 5 | 40 |
| `ReleaseGateReportDeliveryTests.C599A_ProjectionCommitCuts` | 5 | 40 |
| `ReleaseGateReportDeliveryTests.C599A_FixDeliveryCuts` | 5 | 40 |
| `ReleaseGateReportDeliveryTests.C599A_PublishedReportCuts` | 2 | 16 |
| `ReleaseGateBoardBoundaryTests.C599A_SetupAndTypes` | 5 | 30 |
| `ReleaseGateBoardBoundaryTests.C599A_StageTransitions` | 7 | 42 |
| `ReleaseGateBoardBoundaryTests.C599A_GenericMutations` | 12 | 72 |
| `ReleaseGateBoardBoundaryTests.C599A_TaskBinding` | 3 | 18 |
| `ReleaseGateBoardBoundaryTests.C599A_CodePipeline` | 7 | 42 |
| `ReleaseGateBoardBoundaryTests.C599A_ProtectionAndTracker` | 3 | 18 |
| `ReleaseGateIntentTests.C599A_AtomicityAndConstraints` | 10 | 60 |
| `ReleaseGateOwnershipContractTests.C599A_Containment` | 5 | 30 |
| `ReleaseGateOwnershipContractTests.C599A_IdentityAndTools` | 7 | 42 |
| `ReleaseGateActivationContractTests.C599A_ChunkExecution` | 4 | 16 |
| `ReleaseGateActivationContractTests.C599A_EnvelopeIsolation` | 3 | 12 |

- **Q-2 isolated rehearsal = 63 minutes**:
  10 setup/build plus 53 method-execution minutes
  from its named filters above, 30 executions. This is unmutated recipient/recovery
  rehearsal at the final landed SHA, separately commissioned; PC cycles are extra.
- **Live Q reservation = 686 active minutes**:
  Q-1 setup/readback 15, Q-2 63, Q-3 manual
  303 (283 suites + 20 setup/readbacks), Q-4 scheduled 305 (one full 303-minute
  green plus a 2-minute no-new-SHA/busy second-slot observation). If the second
  slot also cuts a new SHA, reserve another 301 minutes. Actual slot waiting is
  additional wall time, not test execution. These are capacity estimates, not
  authority to weaken watchdogs; a too-large class is a blocking capacity finding.
  S5 remains separately budgeted in the original activation plan.
- **Estimated verification saving:** replaying the full 24-minute ordinary
  manifest in all eight rounds would cost 192 minutes; one applicable row per
  round costs 24, saving **168 minutes (87.5%)** of repeated ordinary verification.
  Nothing is saved by dropping full release suites or PCs. Operational savings
  measured so far remain **0 minutes** until real comparable qualification evidence.

**Handoff audit:** test/fixture bodies read as listed; active addendum guards=189,
mapped=189, missing=0, duplicate PC maps=0. All 189 PCs have a concrete
compiling defect, exact method, independent fixture setup and decisive assertion;
they are executable specifications for the owning Code rounds. Runtime PC
executions=0; no guard is represented as empirically killed. All delivery paths end
at recipient evidence, not an ack. Ordinary manifest has 8 isolated builds,
8 exact filters, 19 expected TUnit executions and numeric 24-minute floor.
No unresolved product choice or unverifiable seam remains for B1.

--- next stage ---
next: code
handoff: Implement B1 only: pinned policy and full chunk/UID publication authority plus its commissioned tests. Run CP-19 from the appended Verification design; ship disabled, commit/push and obtain separate Review. D-13 now uses typed Release cards on the same Antiphon board. Q and S5 remain separate.
artifact: docs/superpowers/plans/2026-09-23-card-0599-release-recovery-and-cards-plan.md
