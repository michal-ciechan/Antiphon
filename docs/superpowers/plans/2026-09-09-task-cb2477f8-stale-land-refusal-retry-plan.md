# Explicit retries after a refused land operation

Plan task: `cb2477f8`; baseline: `9157a181` (2026-09-09).
The brief supplied no board-scoped card identifier, so this artifact uses the task identifier.

## Outcome and scope

An explicit land retry after a terminal `LandPhase.Refused` operation may allocate a fresh
operation even when the source and target ref names and source SHA are unchanged. The new
operation reads current target state and follows the complete inspection, preparation,
verification, publication and cleanup protocol. A still-dirty or unreadable target refuses
again with fresh operation evidence; cleaning the target does not itself authorize publication.

This is a small policy correction inside the existing landing protocol, with regression
coverage across its durable replacement boundary. No API, entity, enum or migration is needed.
It supersedes the changed-source/destination restriction in CARD-0448 D-2 and generalizes its
F1 interrupted-rebase exception to eligible terminal refusals. The other CARD-0448 safeguards
remain requirements. Treat verification as a separate TestDesign stage because this gate
controls recovery and preservation of publication evidence; no complexity label was supplied.

## Ground truth

| Brief assumption or possible fix | Current code at the baseline | Design consequence |
|---|---|---|
| A repeated land request starts over. | `AgentTaskLandService.RequestAsync` sets `LandRequestedAt`, filter and attempt fields but retains `ActiveLandingId`. `ClearPending`, used by refusal settlement, also retains that pointer. | Retaining the pointer is necessary for recovery and audit; do not clear it as the fix. |
| Target cleanliness might be cached. | `LandingGit.RunAsync` starts a new Git invocation. `AgentTaskLandingProtocol.CheckTargetAsync` runs target status with untracked files and submodules included and requires success plus empty output. | Leave Git I/O and the target status predicate intact. Reach them through a fresh operation. |
| Valid fresh source inspection is enough for an explicit retry. | `AgentTaskLandingState.CanReplaceRefused` additionally requires an interrupted-rebase reason or changed source ref/SHA, target ref name, common directory or worktree path. It observes no target cleanliness or target commit change. | Remove this final reason/identity-difference disjunction. Eligibility need not prove which external condition changed. |
| The old operation must be deleted or reset. | `PersistNewOperationAsync` deactivates the previous row, inserts the new row and changes the task pointer in one transaction, with the old-row save first for the unique active-operation index. | Reuse this mechanism; retain the previous row and its recovery refs. |
| Every `LandRefused` event means a replaceable operation. | Refusals from early phases become `LandPhase.Refused`; refusals after `Verified` or target-advance/push intent can retain their acknowledged phase. Publication and cleanup have separate statuses. | Gate on `LandPhase.Refused`, never event text, `LastReason` alone, or `Publication == Refused`. |
| Restart recovery is another explicit request. | The protocol uses `task.LandRequestedAt > op.UpdatedAt`; the sweep keeps the original request timestamp and enforces its attempt budget. | Keep the explicit-request guard; elapsed time, a restart, or target cleanup alone must not reopen a refusal. |
| Interrupted rebases and completed publications need the new behavior. | Reload/schema/active/owned-child checks precede replacement. `RebaseStarted` refuses for inspection; valid publication enters cleanup on the same operation. `Verified` has a separate changed-preparation rule. | Preserve these branches and their order. |
| Existing recovery tests reproduce unchanged-source target cleanup. | State tests cover the interrupted-rebase exception. `C448_V31_FailedReplacementKeepsThePreviousOperationRetryable` creates an empty source commit before retry. Target-dirtiness tests cover refusal at a mutation boundary. | Add a target-only repair regression without manufacturing source evidence. |

## Decisions

### D-1. An eligible explicit retry is the reason to create a new attempt

Retain the signature and these conjuncts in `AgentTaskLandingState.CanReplaceRefused`:

```csharp
explicitRequest && leaseHeld && previous.SchemaVersion == 1
    && previous.Phase == LandPhase.Refused && !HasPublication(previous)
    && freshInspection.Accepted
    && freshInspection.Snapshot!.Coordinates.TaskId == previous.TaskId
```

Delete only the final parenthesized reason/identity-difference condition. Update the method
summary to explain explicit retry after terminal refusal and fresh inspection. The caller
continues to verify the active operation and actual repository lease ownership; the policy
does not replace either check.

This applies to every refusal reason that reaches this phase and passes the existing gates,
including transient target status errors, remote observation errors and verification failures.
It does not assert that the refusal was repaired: the fresh protocol establishes that or
refuses again. Source identity is still validated against current task coordinates, even
though it need not differ from the previous operation.

Rejected: add only `target_dirty_or_unknown` to the exception list. That fixes the example
but leaves the same replay defect for transient remote failures and other target-state changes.
Rejected: compare target HEAD/cleanliness inside `CanReplaceRefused`. This duplicates I/O,
cannot cover all transient causes and still needs the protocol's boundary checks.

### D-2. Preserve request and recovery semantics

Keep `RequestAsync`, `SweepAsync`, `ClearPending` and the existing strict timestamp comparison
unchanged. The request still queues before Git runs. Duplicate active requests still conflict;
the same pending request after a crash remains recovery, with the same attempt limit.

| Loaded operation | Explicit retry behavior |
|---|---|
| `Refused`, supported schema, no publication, fresh source accepted | Eligible for transactional fresh operation after the existing admission checks. |
| `Refused` with stale/equal/absent request marker, rejected source inspection, missing lease or wrong task | No replacement. Preserve the active operation and remaining work. |
| `RebaseStarted` or unresolved owned child | Existing inspection/ownership refusal applies before replacement. No auto-abort or overlapping mutation. |
| `Verified` | Keep the separate changed-preparation rule and fresh verification requirements. |
| `TargetAdvanceStarted`, `LocalTargetAdvanced`, `PushStarted` | Resume/refuse against recorded identity and checkpoints; never discard unresolved publication intent. |
| Confirmed publication or cleanup residue | Same-operation guarded cleanup; no new publication claim. |

Rejected: clear `ActiveLandingId` in the request endpoint, change an old row back to `Inspected`,
or make all unconfirmed operations replaceable. Each loses the distinction between a terminal
refusal and unresolved mutation/publication evidence. There is no automatic retry loop or new
operator override in this change.

### D-3. Reuse the transaction and acquire all new evidence

Keep the refused branch in `AgentTaskLandingProtocol.RunAsync`: reload, inspect, call the
policy, set `previousToReplace`, and prepare the replacement through the existing fresh path.
An explanatory comment may change; no new replacement path is needed.

The replacement receives a new ID and recovery-ref namespace, reads the current source SHA,
target commit, destination, target checkout and requested verification filter, and defaults to
`Fresh` mode. It inherits no verification, push, publication or cleanup authority. The old row
and all existing pins survive; only its active flag and concurrency token change when the
replacement commits. Failed preparation or a failed replacement transaction must leave the
old row active and the durable task pointer intact.

After commit, the normal pinning, remote observation and target checks run. In the regression
fixture the source must not already be remotely contained, so this reaches `CheckTargetAsync`
before rebase. Existing exact remote containment may still produce `AlreadyPresent` and guarded
cleanup without preparing/advancing the local target; do not turn a target cleanliness check
into a new prerequisite for that established publication proof.

Keep repeated target checks around verification and target mutation. Never clean, stash,
commit, reset or otherwise repair the operator's dirty checkout as part of retry.

### D-4. Update the living contract with the implementation

Add a short paragraph in `docs/orchestration-loop.md` section 5 when Code lands: an explicit
retry of an eligible terminal refusal creates a new operation even with unchanged source;
it repeats validation, keeps prior evidence and may refuse again. Distinguish this from
same-operation publication recovery and cleanup. The historical CARD-0448 plan remains a
historical artifact; this plan records the amended decision.

## Implementation slices

| Slice | Files | Work and acceptance |
|---|---|---|
| S-1: replacement eligibility | `server/Application/Services/AgentTaskLandingState.cs`; `tests/Antiphon.Tests/Application/AgentTaskLandingStateTests.cs` | Apply D-1. Cover identical accepted source with dirty-target, transient and interrupted-rebase reasons; keep all negative admission cases. |
| S-2: request-to-settlement regressions | New `tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs`; minimal additions to `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs` | Add V-1 through V-7 below using real fixture Git and isolated PostgreSQL. Add a harness method calling actual `AgentTaskLandService.RequestAsync` for the public retry path; the existing `RepostAsync` directly writes columns and alone does not cover that path. |
| S-3: recovery regression and documentation | Existing landing recovery/request/publication tests named below; `docs/orchestration-loop.md`; optional comment in `server/Application/Services/AgentTaskLandingProtocol.cs` | Run relevant regressions, amend the living contract and report evidence. Do not refactor the replacement transaction or Git implementation. |

## Verification design

These are the required behavior and evidence for the TestDesign handoff. TestDesign should
finalize method names, deterministic timestamp setup, fault points and positive-control
commands before Code. No tests were executed during Plan.

Use `LandingSafetyHarness`/`LandingGitFixture`, which provide private local remotes, an
independent observer, isolated DB schemas, command hooks and save-failure injection. Mark the
new integration class with `[ParallelLimiter<ProcessSpawnLimit>]`. Run each retry with a fresh
scope/context, including a service restart in the primary regression. Query saved rows with a
separate observer context. Hook commands by repository path as well as arguments: source and
target both execute `status`. Do not infer a fresh target read from total status-command count.

| ID | Setup and action | Required assertions |
|---|---|---|
| V-1 | Add an unpublished source commit; dirty the fixture's target checkout with a known untracked sentinel. Run to refusal A. Remove only that fixture-owned sentinel; leave source SHA/ref/path and target commit/ref unchanged. Call actual `RequestAsync`, restart services, run. | A is `Refused` with `target_dirty_or_unknown` and no publication. Request retains A's pointer until execution. Retry commits B with `B.Id != A.Id`, same original source and target-before SHAs, `Fresh` mode, different pin prefix, and fresh target status read. Exactly two rows, only B active, task points to B. A's reason/evidence and pins persist. B has valid independently confirmed remote containment and completes cleanup in the clean fixture. Outcome event points to B; pending request is cleared. |
| V-2 | Repeat V-1 but keep the dirty target; explicit retry. | B is a new operation that again refuses `target_dirty_or_unknown`; fresh target status is observed. Target sentinel bytes and source checkout/ref survive; no rebase, target advance, push or cleanup occurs on this attempt. Remote target and protected remote source are unchanged. A is retained inactive and B is the only active row. |
| V-3 | After dirty-target refusal A, remove the sentinel and create a nonconflicting target commit; source identity stays unchanged. Retry with a selected verification filter. | B records the new target-before SHA, re-evaluates/rebases against it and invokes fresh verification with that filter. It cannot reuse A's authority. Final remote contains B's verified SHA and the new target commit. A's old target/source pins still resolve. |
| V-4 | Inject failure only for the target's status command, producing `target_dirty_or_unknown`; then restore the real command and retry unchanged source. Also cover unchanged-source retry after a transient remote-observation failure. | Each accepted retry gets a new ID and succeeds only through fresh checks. Continuing target status failure refuses again. The remote-failure case prevents a reason-specific dirty-target exception from satisfying D-1. |
| V-5 | Exercise a persisted refused operation with the original pending request timestamp (at or before its `UpdatedAt`) after restart; also exercise a normally settled refusal with no pending request. | Original pending recovery reaches the refused-operation gate but does not replace or mutate source/target/publish/clean up. Normal settled refusal is not queued by sweep. Check operation count, active pointer, pins and absent mutations. Merely calling `RunAsync` with null `LandRequestedAt` is insufficient coverage of the protocol guard. |
| V-6 | Start with an unchanged-source refusal eligible under D-1. Fail destination/target preparation before replacement persistence; separately inject a save failure during the transaction after old-row deactivation and before replacement commit. Restore the fault and issue another explicit request. | Independent DB reads after failure show one old active row and its original task pointer, no committed replacement, preserved pins/work. The subsequent explicit retry can commit B. This extends the existing replacement-failure test without requiring an empty source commit. |
| V-7 | State-policy matrix: explicit/automatic, lease held/missing, schema supported/unknown, accepted/rejected inspection, same/different task, Refused/other phases, publication absent/valid. Include unchanged and changed source identity. | Eligibility depends on D-1's conjuncts, not refusal text or source differences. Use a genuinely valid publication receipt for the published negative case so `!HasPublication` is exercised. The predicate never mutates either input. |

V-1 must prove success with no source-identity change between A and B; V-2 must prove a fresh
refusal, not a replay of A. Pin the exact command trace and independently saved operation/event
IDs rather than relying on formatted outcome text. Successful cleanup can remove the local
source; every refusal must preserve remaining source work and the fixture's pre-published
remote source (`AssertRemoteSourceAsync`). Keep target commit/ref observations separate from
source identity so a target-only repair cannot accidentally become new source evidence.

For timestamp tests, use deliberate ordering relative to the persisted `UpdatedAt`; equal and
older markers remain automatic. If a controllable clock is needed, scope it to the harness and
use an advancing/real-offset clock per `docs/testing-and-build.md`. Do not freeze a live queue
graph or rely on arbitrary sleeps. No changes to production request identity are proposed.

### Regression set

| ID | Existing coverage to retain |
|---|---|
| R-1 | `AgentTaskLandRecoveryTests`: unsafe/automatic repost, resolved interrupted rebase, failed replacement, unknown schema and durable recovery boundaries. |
| R-2 | `AgentTaskLandPreparationIdentityTests`: `C448_V15_ExplicitRepostCannotReplaceTargetAdvanceIntent`, changed `Verified` preparation and missing rebase result. |
| R-3 | `AgentTaskLandPublicationTests`: remote confirmation and `C448_V33_CleanupRetriesDoNotEmitAnotherPublication`; `AgentTaskLandCleanupSafetyTests`: cleanup refuses changed work. |
| R-4 | `AgentTaskLandRequestTests`, `AgentTaskLandSweepTests`: pointer/request behavior, active-request 409, no-pending no-op and automatic-attempt limit. |
| R-5 | `AgentTaskLandConcurrencyTests`: repository/writer holds and target/source changes during verification; `AgentTaskLandingPersistenceTests`: active-operation constraint and persistence. |

### Positive controls for TestDesign to make executable

| ID | Temporary mutation | Test that must fail for the intended assertion |
|---|---|---|
| PC-1 | Restore the original final reason/source-difference condition in `CanReplaceRefused`. | V-1: cleaned target with identical source fails the fresh-ID/publication expectation. |
| PC-2 | Remove only the `explicitRequest` conjunct from the fixed policy. | V-5's original-pending-request case: recovery wrongly creates a replacement. The test must reach the protocol, not stop at the service's no-pending return. |
| PC-3 | Bypass only the `CheckTargetAsync` requirement for successful empty target status. | V-2: a still-dirty target is incorrectly accepted beyond the refusal boundary. Fail on phase/reason or forbidden mutation, not on fixture setup. |

Run each control red then restored green with the exact method filter. Mutations sharing
files/methods run separately. Refresh restored timestamps or rebuild to avoid stale DLLs.
Report every V/R/PC with nonzero executed counts and failures. A build error, fixture failure
or zero-test run does not establish a positive control.

Example Code commands (run classes sequentially; use exact methods for positive controls):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-refused-retry/ -- --treenode-filter "/*/*/AgentTaskLandingStateTests/*" --report-trx --report-trx-filename refused-retry-state.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-refused-retry/ -- --treenode-filter "/*/*/AgentTaskLandRefusedRetryTests/*" --report-trx --report-trx-filename refused-retry-integration.trx
```

Use the same single-class form for R-1 through R-5 with distinct fresh result filenames.
Inspect executed method names and counters in TRX. Use `dotnet run`, never `dotnet test`;
do not run `Antiphon.Agents.Pty.Tests` alongside this assembly. No production runner, live
repository landing, shared-stack restart, browser/E2E run or full nightly run is needed to
validate this policy change. Code must leave every temporary mutation restored before reporting.

## Delivery and remaining work

This dispatch delivers only the committed plan. TestDesign finalizes the verification section;
Code implements S-1 through S-3 and executes the checks. The caller should land this plan branch
through the normal delegation landing mechanism before dispatching a stage that needs it.
Implementation, tests, landing and deployment have not been performed by Plan.
