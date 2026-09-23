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

### Planning validation

Source and owner-document inspection, live card read, GitHub primary API contract
review, and document/diff consistency checks only. No builds/tests or live actions
performed. TestDesign must certify methods/fixtures/controls and costs before B1.

--- next stage ---
next: code
handoff: Implement B1 only: pinned policy and full chunk/UID publication authority plus its commissioned tests. Run CP-19 from the appended Verification design; ship disabled, commit/push and obtain separate Review. D-13 now uses typed Release cards on the same Antiphon board. Q and S5 remain separate.
artifact: docs/superpowers/plans/2026-09-23-card-0599-release-recovery-and-cards-plan.md
