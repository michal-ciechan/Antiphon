# CARD-0804 / CARD-0805: owned checkpoint test roots and safe tool-copy recovery

Plan stage, 2026-09-29. Source base: `f4d86c88949d3b3bab704cfc9e1007b9988323d0`.
Investigation: [CARD-0805 temp roots](../../investigations/2026-09-29-card-0805-c723-temp-roots.md), task `86fe6165`.
Complexity: hard (destructive filesystem operations, detached-process custody, concurrent worktrees).
Next stage: separate TestDesign; this document is the fix design, not a claim of executed verification.

## Shared ownership and completion

One Code task owns all slices below and one reviewed landing publishes them. Bind that task to
CARD-0804 (the direct fixture defect), and reference CARD-0805 in its brief, commits and evidence.
CARD-0805 is the incident/acceptance companion and owns the additional tool launch/crash recovery
requirements; it must not commission a second implementation against these files. If the caller
keeps an existing CARD-0805 Code owner instead, use that one owner for both cards, without splitting
the fixture work. Both cards consume the same Review, publication SHA and disk measurements.

Read both full descriptions through `scripts/card.ps1 get ... -Board Antiphon -Json` on this
dispatch. CARD-0804 requests per-test disposal, small fixtures, marked/live-aware bounded orphan
cleanup, peak/residual measurements and repeated full-suite acceptance. CARD-0805 records the
disk incident and a one-time manual age-based deletion; that historical mitigation does not
authorize this implementation to repeat it. Correct its attribution through the shared report:
`c723-*` is a test fixture namespace, not the production tool's normal results directory.

Completion requires per-test disposal, bounded recovery of *eligible* orphans, the production
recovery cases, Linux disk acceptance and Windows process/filesystem qualification. Ordinary
Review precedes land. Track safety positive controls through the normal post-land Mutation
companion; do not describe unexecuted controls as clean. No server restart is needed for a
test/tool source change, but newly invoked tools and test hosts must be built from the landed SHA;
an already running shadow copy keeps its old binary and is never removed to activate this fix.

## Ground truth

| Card/investigation premise | Source at the pinned base | Consequence |
|---|---|---|
| Production checkpoint runs directly create `/tmp/c723-*`. | `CheckpointFixtures.TempDir()` in `tests/Antiphon.Tests/Checkpoints/CheckpointTestSupport.cs` creates the name. `CreateRun` uses the manifest's results root under the supplied repo. | Fix the test owner first; do not add a production `/tmp` janitor. |
| There are 73 callers across 18 files. | Exact census: 73 matching **lines**, 78 `CheckpointFixtures.TempDir(` invocations in 17 caller files; the support file makes 18 files including the definition. No caller owns disposal. | Migrate every invocation, including multiple roots on one line and helper-created roots. |
| Hundreds of MB are needed for a small round-trip test. | `StartAsync` always passes `AppContext.BaseDirectory` to `ShadowCopy.CopyToolOutput`. Fake launchers in `CheckpointTaskOwnershipTests` still copy the entire test host output. | Inject a tool-source directory for fake-launch tests; keep real-copy/native evidence separately. |
| Red runs necessarily retain the tool image. | `WaitCommand` deletes the tool directory after `done` and PID exit for either verdict. `KeepOutputs` controls executor-side output cleanup. | Preserve red reports/TRX/logs, not redundant executable images. |
| The next start recovers all abandoned copies. | `EvidenceFolder.SweepFinishedToolCopies` only visits the selected results root and terminal phases; `starting`/`running` dead executors and distinct test roots are missed. | Add identity-aware recovery at the actual ownership boundaries. |
| PID absence is safely distinguished from uncertainty. | `ProcessLiveness.IsAlive` catches every exception and returns false; state stores only a PID. `RunStateStore.Write` may swallow an I/O failure. | A boolean liveness result and best-effort progress state cannot authorize deletion. |
| `StartAsync` leaves a reliable state before work starts. | It creates files, copies, launches, writes `latest`, then writes `starting`; an exception can precede all state, and a fast child can publish before the starter overwrites it. | Persist a separate launch/ownership journal before copying; never overwrite executor progress after launch. |
| Stop/age cleanup already protects every live executor. | `Program.Stop` kills by PID without start identity or waiting; `OutputCleanup.RemoveOlderRuns` deletes whole old run directories without liveness checks. | Route these overlapping destructive paths through the same protection, so they cannot bypass safe tool cleanup. |
| Disk figures are a new census. | CARD-0804 records 20,541 roots / 189 GB and a three-copy ~1.2 GB sample. CARD-0805 records 20,600+ roots / ~233 GB and manual mitigation. | Treat these as incident evidence, not measurements made by this Plan dispatch. |

## Decisions

### D-1. Explicit per-test ownership, with an actual teardown boundary

Add `CheckpointTestScope` and a `CheckpointTestBase` with an instance scope initialized in
`[Before(Test)]` and disposed in `[After(Test)]`, following the existing hook shape in
`TestHelpers/TestDbFixture.cs`. Every root allocation requires that scope. Replace the unowned
static `TempDir()` with an instance `TempDir()` delegating to it; helper methods that allocate
roots become instance methods or take the scope explicitly. Pure sample/TRX helpers stay static.
No static mutable registry, ambient AsyncLocal owner, class-shared root or assembly-end-only cleanup.
The allocation API must fail without an owner rather than silently creating an untracked root.

The scope registers a root **before returning it**, and rolls back its own just-created empty
directory if marker publication fails. Roots keep the exact `c723-<32 lowercase hex>` shape for
continuity. A versioned marker records root ID, test/attempt ID, assembly invocation ID, creation
time, normalized root path, local host/boot/PID-namespace identity, owner PID and process start
identity. Never store task tokens, environment dumps or credentials. Test names alone are not
unique ownership; parameterized attempts and concurrent hosts have separate IDs.

Teardown seals allocation, awaits registered async work and joins test-owned children before
deleting roots. Cancellation uses a fresh bounded cleanup token, not the already canceled test
token. Native tests retain handles/start identities and use `try/finally` even when their first
assertion fails. Only the fixture's own child may be stopped; a sweeper never kills anything.
If a child cannot be proved gone, keep its root, emit a teardown failure with the marker/path and
preserve the original assertion/cancellation outcome in the diagnostic. Attempt the other roots
too. Best effort must not silently turn residue into a passing teardown.

Copy failure diagnostics and any needed failed-test evidence to the ordinary results directory
before deleting a fixture root. Red assertions are not a blanket retention switch. Hard process
death cannot execute a hook and is handled by D-5. TestDesign must prove the *real TUnit hook*
on pass, assertion failure and cancellation, not merely call `DisposeAsync` in a unit test.

Rejected: global end-of-suite deletion (late and unsafe across worktrees), mutable static ownership
(parallel tests share it), and inserting `Directory.Delete` after assertions (failure skips it).

### D-2. Remove test-host amplification without weakening detached production copies

Add `Runtime.ToolDirectory` (or the equivalent explicit I/O seam) to `CheckpointApp.StartAsync`,
defaulting to `AppContext.BaseDirectory` for production. Fake-launch tests supply a tiny directory
with a sentinel DLL and companion metadata; `ShadowCopy.CopyToolOutput` still performs the real
copy. Import/state/round-trip tests which need no launch use `CreateRun`/the importer directly.
Keep assertions about copied contents and the launched DLL path so substituting a no-op copy
cannot pass. Add a source-only sentinel to prove production exclusion behavior where required.

The native case uses the checkpoint tool's own build output, staged via `GetTargetPath` from its
project (the existing child-staging targets in `Antiphon.Tests.csproj` are the pattern), not the
parent test host output. Preserve DLL, deps/runtimeconfig, dependency and resource content needed
to actually launch. Do not hard-code an assumed YamlDotNet-only dependency closure. Stage once
per isolated build; each actual run retains its private copy. Prove the staged copy starts outside
the source output directory. No shared writable executable image, hardlink cache or production
file-exclusion expansion is required for this fix.

### D-3. One conservative process/ownership model authorizes cleanup

Introduce a concrete ownership journal/store and a process identity I/O seam in
`tools/Antiphon.Checkpoints`. The process probe returns explicit `AliveSame`, `Dead`, `ReusedPid`
or `Unknown`, plus a safe reason. Record PID **and OS process start identity**, host, boot and
PID namespace; `RunState.StartedAt` is a run clock, not a process identity. On Linux use the local
process namespace/boot identity with the process start value; on Windows use the corresponding
local machine/boot and process start evidence. Lack of access or an unsupported probe is Unknown.
Foreign host/namespace evidence never becomes a local dead verdict. Do not use wall age as proof.

Only `Dead` permits automatic deletion. Conservatively retain a reused PID even when its recorded
generation has ended; never signal that replacement process. An owner registered by the current
scope can explicitly dispose its sealed roots after proving its children ended; that is distinct
from sweeping a live test host. The current executor image is always a veto, including after it
publishes `done`. A native exit observation is required; a completion marker is insufficient.

The cleanup journal is independent of lossy progress snapshots. Critical writes are atomic and
checked, with bounded retries; they do not use the current swallow-on-failure `RunStateStore.Write`
as custody evidence. Make journals versioned and path/run-ID bound. Keep separate starter and
executor records so a parent never replaces child progress. Old state lacking identity remains
readable for status/report; missing cleanup authority retains the image and explains why.
This compatibility cost is intentional. No retroactive marker stamping of legacy artifacts.

All cleanup paths acquire a short exclusive per-run/root mutation lock, re-read the marker and
identity under it, and verify lexical and physical containment before destructive operations.
Allocation/launch registration uses the same lock; sealed/deleting roots cannot admit new work.
No following symlinks/junctions/reparse points at roots, ancestors or descendants. Unexpected
links or incomplete enumeration retain the candidate with a reason. Lock failure is a skip,
not permission to proceed. Locking is not itself proof a process has ended.

Rejected: `catch => false`, a PID alone, stale `done`, age thresholds, or a manifest mentioning
another worktree as evidence that a root is abandoned.

### D-4. Journal launch before copying, then recover by known outcome

Use this custody sequence; progress/report phases and public exit meanings remain separate:

| Boundary | Durable evidence / action | Recovery |
|---|---|---|
| Allocate run | Create a unique run directory and checked ownership record with starter identity and `preparing`, before manifest/request/copy I/O. | A current starter's prelaunch failure removes its exact partial run. Later recovery needs a dead starter and proof launch was never attempted. |
| Copy tool | Copy while still `preparing`; register the run in an enclosing test root before copying. | A partial copy failure cannot leave an unowned large image. Preserve the exception/cleanup diagnostic outside a removed run. |
| About to invoke OS | Durably publish `launch-attempted` before invoking the native launcher. | If this write fails, do not launch. An interrupted attempt is uncertain, not `not-started`. |
| Native return | Launcher returns a typed `NotStarted`, `Started(identity)` or `Unknown` result. Capture identity from the owned process/handle before disposing it. Persist it without rewriting executor progress. | A definite OS launch refusal removes the partial run; `Unknown`, or failure after possible launch, retains it. |
| Executor entry | Write its own checked identity/ack before reading request, opening logs, checking owner HTTP or launching rows; validate run ticket and sealed status. | Later death before any `phase=done` is recoverable using this identity. Entry failure does not launch rows. |
| Executor terminal | Flush logs, write reports and terminal state as today; do not delete its own image. | `wait`, `stop` after verified exit, or the next start removes only eligible `tool/`. |

Keep `latest` as a convenience pointer. Failure writing it is a post-launch bookkeeping failure,
not a license to roll back a running image. A fast executor publishing `running`/`done` before
the parent returns must stay in that phase. The starter owns only starter custody metadata.

If the starter dies after launch intent but before persisting PID and the executor dies before
its own checked entry record, there is no safe proof of whether a child still uses the image.
Retain and report `launch-outcome-unknown`; do not turn this tiny ambiguity window into age-based
deletion. An executor ack that arrives later resolves it. Fully reclaiming such unknown custody
would require a separate native custody protocol; it is outside this bounded fix. Acceptance
must list these roots separately and must never claim they are proven orphans.

Route completed green/red `wait`, dead-before-done `wait`, confirmed `stop`, and next-start sweep
through one `ToolCopyCleanup` policy using D-3. A dead nonterminal executor still returns exit 6;
its report, request, manifest, TRX and log survive. `KeepOutputs` affects build outputs only and
cannot retain an otherwise eligible image. A live or Unknown executor stays intact (`wait` may
return 75 at its existing deadline). Cleanup does not invent a green report or conceal the
original failure. Return structured `Removed`, `AlreadyAbsent`, `Retained(reason)` or
`Failed(reason)` receipts and print a concise line including run/path and retryability.

Recovery reads the checked ownership journal even when `state.json` is absent or corrupt; it
must not depend on the executor surviving long enough to publish its first progress snapshot.
Make the raw recursive remover private to the validated cleanup operation (or require its
unforgeable in-process claim). `EvidenceFolder.Write` and executor-side cleanup cannot remain
alternate unguarded deletion entry points. Existing direct-call tests gain explicit owned
fixtures; do not preserve a bypass solely to keep them unchanged.

`stop` validates identity before signaling, awaits exit within a bounded 10-second cleanup wait,
then applies the policy. A timeout/refusal retains image and records incomplete stop; do not
write `stopped` or print successful deletion while the process is alive. It never kills a
reused/unknown PID. `clean --older-than` must use the same custody veto before its existing
explicit whole-run retention deletion; automatic recovery deletes only `tool/`, not evidence.
Do not expand `CleanOwnedOutputs` into a temp-root sweep.

Next-start recovery stays within the selected results root and has the same work limits as D-5.
A failed deletion leaves marker/receipt evidence, is observable, and can be retried by the next
wait/start/explicit cleanup. Do not count an attempted deletion as reclaimed bytes.

### D-5. Bounded orphan collection is a test-owned safety net

Implement the test-root coordinator under `tests/Antiphon.Tests/Checkpoints`, reusing D-3's
process/contained-deletion primitives. Invoke once at assembly startup and opportunistically at
root allocation, at most once per five minutes across hosts sharing the same local temp directory.
Use a filesystem lock/cursor, not a static in-memory clock or a background task. Skip production
runner interactions. The assembly hook can be static as required by TUnit; its state is a passed
scope or validated filesystem record, not new process-global mutable state.

Registration writes an independent per-root index file and never waits for the
coordinator lock. Successful disposal removes that file, keeping historical disposed
roots out of the 512-entry sweep budget. The old shared JSONL index is migrated
under the sweep lock, dropping disposed entries. The sweep checks its persisted interval
before acquiring the lock, then rechecks under it. Failed index registration is
best effort; the root's own marker remains available for operator inspection.

Only direct children of `Path.GetTempPath()` with the exact name and a valid supported marker
are candidates. Require a ten-minute creation grace, a proven dead owner, and a complete check
that every nested/registered checkpoint launch is either not attempted with a dead starter or
has a proven dead executor. A live/unknown child vetoes the **whole root**, even if its test host
died. Inspect custom contained results roots too; do not assume only `.antiphon/checkpoints`.
Unknown run directories, malformed/missing custody, or an incomplete inventory veto deletion.

Defaults, named options on the coordinator (inject reduced values in tests):

| Limit | Default | Meaning |
|---|---:|---|
| Grace | 10 minutes | Minimum age in addition to ownership proof; never a substitute. |
| Sweep interval | 5 minutes | Cross-process admission rate; no timer service. |
| Scan | 512 direct temp entries / 10,000 descendant entries | Stop and persist progress; never materialize the whole temp tree. |
| Deletions | 16 roots or 256 MiB per pass | Reclaim oldest verified candidates first; deletion work is incremental. |
| Time | 2 seconds per pass | Monotonic cooperative deadline checked between filesystem/process operations. |
| Eligible backlog target | 32 roots and 256 MiB | Report either exceeded target; this is not authority to delete unsafe roots. |

A cursor avoids revisiting only the first entries in a large temp directory. Order verified
candidates by marker creation time/root ID; incomplete pages carry forward, and unknown sizes
do not count as zero. Persist bounded pending work so a root larger than a deletion budget can
be drained over later passes. Retain its marker until deletion completes; mark it as deleting
and prohibit a new launch, preserve any run custody needed for retries, and recheck the vetoes
on every pass. Never escape budgets by falling back to an unbounded recursive `Directory.Delete`.
The time bound is cooperative: a single blocked OS I/O call cannot be preempted safely; record
overruns rather than promising a hard real-time deadline.

Emit examined/deleted/remaining-eligible counts and measured bytes, skips by reason, failures,
elapsed time and budget exhaustion. Put receipts outside candidates; ordinary test output and
an explicit external results file suffice. Live/foreign/unmarked/unknown roots are a separate
retained inventory and are excluded from the eligible-backlog bound, visibly rather than silently.
Process death followed by sufficient admitted sweeps must drain a known eligible fixture below
both targets. With no subsequent test run, there is no scheduled cleanup guarantee.

Add `scripts/inspect-checkpoint-temp.ps1`, ASCII-only, **read-only/dry-run**, to inventory matching
roots and explain legacy/unknown exclusions with bounded count/byte sampling. It must not call
the deleting old `clean --older-than` path or offer a force-delete switch. Legacy unmarked roots
need an operator's separately reviewed ownership investigation; name and age alone never suffice.

Rejected: Hangfire/scheduled tasks, scanning `/tmp` on every production checkpoint run, deleting
unmarked historical roots, or treating every matching name as owned by this checkout.

## Implementation slices

Commit and push each slice. Run one checkpoint-tool invocation per committed slice group after
TestDesign supplies the closed manifest. S2 and S3 depend on S1; S4 depends on S1-S3. They are
serial parts of one implementation, not parallel card owners.

| Slice | Files / concrete change | Required test homes and decisive outcomes |
|---|---|---|
| S1: ownership primitives | New `tools/Antiphon.Checkpoints/Cleanup/ProcessIdentity.cs`, `RunOwnership.cs`, `ContainedCleanup.cs` (exact class layout may vary); checked journal store; additive identity support beside `State/RunState.cs`. | New `CheckpointProcessIdentityTests`, `CheckpointRunOwnershipTests`: dead vs permission-denied, same/reused PID, foreign host/boot/namespace, checked-write refusal, corrupt/legacy marker, path binding, lock race, links, bounded traversal. |
| S2: fix fixture lifetime and amplification | `CheckpointTestSupport.cs`; new `CheckpointTestScope.cs` / `CheckpointTestBase.cs`; all 17 caller files listed below; `CheckpointApp.Runtime`/tool source selection; `Antiphon.Tests.csproj` tool staging. | New `CheckpointTempScopeTests`; real-host `CheckpointTempLifecycleTests`; existing `ShadowCopyTests`, `CheckpointImportTests`, `CheckpointTaskOwnershipTests`, `CheckpointExecutorLogTests`: multiple roots, thrown assertion/cancellation, failed setup, child join, tiny fake copies, real dependency copy/launch. |
| S3: production recovery | `CheckpointApp.cs`, `Execution/DetachedLauncher.cs`, `Commands/WaitCommand.cs`, `Evidence/EvidenceFolder.cs`, `Program.cs`, `Cleanup/OutputCleanup.cs`; new shared `ToolCopyCleanup`. Keep executor report/log ordering. | New `CheckpointLaunchCleanupTests`, `CheckpointRecoveryProcessTests`; extend `CheckpointAppTests`, `WaitCommandTests`, `EvidenceFolderTests`, `DetachedLauncherTests`, `RunStateStoreTests`, `OutputCleanupTests`. Fault each launch boundary; dead-before-done; live done; stop exit/timeout; next start; retention; fast-child race; real starter/executor death on both OSes. |
| S4: orphan budget and acceptance | New `CheckpointTempRootSweep.cs`/assembly hook; new `CheckpointTempRootSweepTests`, `CheckpointTempUsageTests`; `scripts/inspect-checkpoint-temp.ps1`; evidence sampler/harness; update `docs/testing-and-build.md` lifecycle/recovery/inspection contract. | Bounded/fair concurrent sweep; 0 lost live sentinels; oldest eligible reclamation; budget/deletion failure retry; simulated test-host death; measured repeated class/full-suite runs and dry-run immutability. |

Caller files to migrate: `BaselineComparerTests`, `CheckpointAppTests`, `CheckpointExecutorLogTests`,
`CheckpointImportTests`, `CheckpointManifestTests`, `CheckpointTaskOwnershipTests`,
`DetachedLauncherTests`, `EvidenceFolderTests`, `OutputCleanupTests`, `RerunPolicyTests`,
`RowRunnerTests`, `RunSchedulerTests`, `RunStateStoreTests`, `ShadowCopyTests`, `TimeoutTests`,
`TrxReportTests`, `WaitCommandTests` (all under `tests/Antiphon.Tests/Checkpoints`).
Re-run the source census before Code: no surviving call to the unowned allocator is acceptable.

For real TUnit pass/fail/cancel hook evidence, add a small dedicated
`tests/Antiphon.Checkpoints.LifecycleHost` referencing the pinned TUnit and checkpoint tool plus
the same fixture-lifetime implementation. Stage it once through the parent build, analogous to
the existing child targets. Its deliberately red/canceled scenarios are launched only by the
parent integration test and their expected result is asserted; they must not join the ordinary
suite. Avoid recursively running the full parent test host just to check a teardown hook. The
parent tests assert files are absent after child completion, and that no child is left running.
Use the assembly-local `ParallelLimiter<ProcessSpawnLimit>` for every real process-spawning class.

## Verification requirements for TestDesign

Append the repository-standard `## Verification design` to this plan; preserve the fix decisions.
Read the touched helper/test bodies before fixing method names, argument expansion and execution
floors. The inherited source is the one pinned above; new test names in the slice table are
implementation targets. No test or build was run by this Plan task.

1. **Lifetime:** zero owned roots after green, assertion failure, cancellation and multiple-root
   cases; actual hook execution as well as scope unit tests. Early assertion in a native test
   still joins the exact child. A failed delete remains visible and retryable.
2. **Amplification:** fake-launch/import cases copy only the purpose-built small fixture (target
   under 64 KiB per fake source); native staged image contains real dependencies and works after
   detaching. Keep a real `ShadowCopy` content test. Removing the copy call must make a test red.
3. **Recovery matrix:** copy partially then throw; native definite-not-started; native maybe-started;
   post-launch `latest`/identity persistence failure; executor crashes before progress/terminal
   publication; terminal green/red/KeepOutputs; completed-but-live; stop/next-start recovery.
   Assert exact exit/receipt, copy existence and evidence preservation, not just an exception.
4. **Non-deletion matrix:** live test host in another worktree; dead host/live nested executor;
   reused PID; same PID/changed generation; unknown access; foreign namespace; unmarked/malformed
   or legacy root; root/descendant link; lock held; image-in-use; registration racing sweep;
   launch intent with no authoritative child outcome. Include active sentinels checked *after*
   attempted cleanup, on real Linux and Windows processes for the native boundary.
5. **Budgets:** fixed clock and scripted identities for grace, rate, count, bytes, ordering, scan
   cursor fairness, failed size enumeration, partial large-root deletion and resumable receipts.
   Two concurrent sweepers cannot delete a newly registered/live root. New arrivals cannot starve
   an old eligible candidate indefinitely. Bound actual walk/delete operations, not just log counts.
6. **Disk acceptance:** an external observer records invocation ID, source SHA, copied bytes,
   created/deleted roots, 1-second sampled peak count/bytes, final residual count/bytes and reasons.
   Allocation/copy instrumentation supplies event totals because sampling alone can miss peaks.
   Use a fresh OS temp sandbox for acceptance (set test child `TMPDIR`/`TEMP`/`TMP` before startup)
   so other worktrees and legacy roots do not contaminate attribution. Keep reports outside it.
   Run the checkpoint namespace twice, then the supported full `Antiphon.Tests` lane twice at
   the same committed SHA. Require complete TRX/roster each time, zero roots retained by finished
   healthy scopes, and zero retained GB-scale growth. Separately inject owner death, exercise
   sufficient bounded sweeps, and assert eligible residue <=32 roots **and** <=256 MiB, while all
   excluded live/unknown sentinels remain. Record absolute peak and pass-to-pass deltas; do not
   substitute sparse logical file lengths for allocated-disk measurements in the incident proof.

The full-suite runs are an explicit cross-cutting exception requested by CARD-0804: per-test
fixture lifetime under actual assembly concurrency and failure/cancellation must not regress
outside the checkpoint namespace. They are acceptance evidence, not permission for repeated
unlisted rebuilds. Preserve unrelated failures, prove inherited failures with exact base methods,
and never claim an incomplete/ENOSPC run establishes residual acceptance. TestDesign must put both
repetitions in the checkpoint manifest, optionally partitioned by a complete namespace roster
when required by the row deadline. They cannot be replaced by two synthetic scope loops.

Use `/*/Antiphon.Tests.Checkpoints/*/*` for the repeated checkpoint-namespace acceptance group;
use exact named class groups for development and separate native Linux/Windows groups. Include
the default Unit lane and classification guards as required by `docs/testing-and-build.md`.
MinExecuted counts actual TUnit executions, not internal assertion rows or root counts. Native
OS-specific cases must not be credited from skips on the other OS. All row commands go through
the checkpoint tool/build-slot gate; keep the checkpoint launcher bootstrap outside nested
held slots, and use its built DLL/no-build entry once bootstrapped. Wait again on exit 75.

Budget for planning dispatches: 15 minutes for primitive/fixture groups, 20 for recovery/native
Linux, 20 for Windows qualification, 10 for repeated checkpoint/budget groups, and 90 for two
full-suite passes including available build reuse: **155 minutes ordinary estimated validation**,
plus implementation. These are estimates, not measured timings or Min floors. TestDesign owns
the final row estimates/deadlines and their numeric sum. Reducing whole-output copies should
materially cut peak bytes; measure it rather than promising a speedup from the incident sample.

Every independently bypassable destructive guard in D-1/D-3/D-4/D-5 needs its own executable
positive control and exact failure assertion: ownership/marker validation, path/link containment,
identity/Unknown/reuse handling, nested-owner veto, image protection, launch classification,
critical persistence, stale-state ordering, registration/cleanup serialization, evidence boundary,
and work-budget enforcement. The test-design stage supplies the complete one-to-one guard map,
method-scoped mutation recipes and a separate numeric Mutation floor. A compile error, zero-test
run or self-comparison is not a positive control. Ordinary Code/Review runs V/R; Mutation follows
confirmed publication using the standard companion workflow.

### Platform selection

This Plan read `/api/runner-defaults` and `/api/session-runners`: a Linux default and an eligible
Windows lane are available. That is a dispatch-time observation, not a host pin. Default Code
work uses current routing without `-Runner`; use a Windows platform requirement for native
Windows qualification and Linux for incident measurements. Re-read eligibility at dispatch.
Do not embed a particular fleet location in checkpoints or silently substitute a Linux fake for
Windows file locking/native launch evidence. TestDesign must separate these lane manifests/rows
so each runs only its admitted OS scope.

## Exclusions and residual limits

- No server/Hangfire janitor, scheduled task, live `/tmp` deletion, legacy marker backfill, or
  change to shared `obj` collision handling (CARD-0773) or row-grandchild containment (CARD-0774).
  A still-live descendant remains a cleanup veto; sweeps do not repair process containment.
- No change to production shadow-copy dependency selection, build-output red retention, task
  owner HTTP semantics, delivery queues, transcript custody or session runtime behavior.
- No guarantee that unmarked, foreign, malformed or launch-unknown artifacts can be reclaimed
  automatically. Report them separately with safe reasons; they never consume a deletion license
  merely because the disk budget is exceeded. Recovery of known dead executors and current test
  roots is required; unknown custody is retained deliberately.
- Legacy runs remain inspectable and may need explicit operator disposition. Rollback does not
  delete markers or loosen deletion guards; stopping the new sweep is safe, age-only fallback is not.

## Plan-stage evidence

Read the investigation, both full live card descriptions, required owners and the relevant
fixture, launcher, state, wait, cleanup and test bodies. Confirmed the source base and allocator
census. This dispatch changes only this plan; it performs no build, test, process termination,
card mutation or cleanup. The next stage must complete the executable verification design and
checkpoint table before Code is dispatched.


## Verification design

TestDesign task `60161398`, 2026-09-29, inspected at plan commit
`48dd480d74cf8a221c337b843dc496834eb45848` (Plan task `423562de`). This appendix
is the executable specification for Code, not executed green or disk evidence. D-1 through
D-5 and the single implementation/landing owner above remain authoritative. New class/method
names below are implementation targets; existing test methods keep their names and assertions.

### Inspection

| Bodies/artifacts inspected | Boundary and resulting obligation |
|---|---|
| Both live card descriptions through `scripts/card.ps1 get CARD-0804 -Board Antiphon -Json` and the equivalent CARD-0805 command; investigation; full shared plan | CARD-0804 `e476a3f0-c43f-43c2-9a6e-fb6d58e7b3e9` and CARD-0805 `dfa18428-0734-414f-8b94-fcf5c813f58c`, board `8988ca03-7414-47ad-b0b6-51556c701703`, share V-1..V-9. Historic age-based mitigation supplies no deletion authority. |
| `CheckpointTestSupport.cs`, allocator-bearing `CheckpointAppTests`, `WaitCommandTests`, `OutputCleanupTests`, `EvidenceFolderTests`, `ShadowCopyTests`, `RunStateStoreTests`; fake-launch portions of `CheckpointImportTests` and `CheckpointTaskOwnershipTests`; `CheckpointExecutorLogTests` | Scope teardown, helpers returning several roots, fixture copy amplification and dangerous direct remover entry points: V-1..V-4, R-1..R-3. Count all 78 allocator invocations, not 73 matching lines. |
| `DetachedLauncherTests`, `TimeoutTests` native setup, `RerunFilterHostTests`, `Antiphon.Tests.csproj` staging targets, `TestDbFixture` assembly hooks | V-5/V-6 need real child processes and hooks. The existing detach test lacks a finally around early assertions; preserve its assertion but add exact-child custody. The existing filter-host test builds inside its test body; stage its small host once through the parent build and execute the staged DLL for the two filter checks. This is a prerequisite to the repeated namespace lane, not an extra unleased nested build. |
| `CheckpointApp.StartAsync/CreateRun/ExecuteCoreAsync/Finish`, `DetachedLauncher`, `WaitCommand/ProcessLiveness`, `EvidenceFolder`, `RunStateStore`, `OutputCleanup`, `Program.Stop/Clean` | Checked launch boundaries; state PID is insufficient; file-share failure is swallowed today; Linux unlink can succeed against an open file: V-2..V-6. |
| `PlanTableImporter`, `CheckpointManifest`, `RowTimeout`, `AfterSelector`; test classification/limiter guards and testing/build owner | Table parser has no OS column, only one Checkpoints table, class OR must escape pipes, reused output requires identical After. Use explicit row lists; `--after all` includes every OS row. The importer warns at 45 minutes but `RowTimeout` does not clamp to 45: full-pass deadline below is deliberately explicit. |
| `tests/linux-test-roster.json` header/schema and `scripts/test-docker-container.ps1` admission | Frozen CARD-0590 roster predates this source; its 4657 included source methods are not current executed counts. V-8 requires fresh complete compiled accounting, never a stale roster masquerading as the full suite. |

Missing setup is implementation work in this one Code task: checked-journal/fault seams;
instance fixture scopes; small staged lifecycle/filter/tool hosts; native pause/exit handshakes;
a counted filesystem seam; external usage observer and read-only inspection script. New seams
are at I/O boundaries, not alternate production cleanup algorithms. Use real cleanup against
small on-disk sentinel trees with a recording I/O wrapper. Scripted probes make PID reuse and
permission failures deterministic; they cannot qualify the native adapters.

No stateful production globals, sleeps for race ordering, host-wide process termination or
real shared-temp deletion. Every test owns an outer sandbox that survives until assertions and
evidence export complete. Synthetic c723 candidates are children of that sandbox. Before starting
any checkpoint slice, allocate a unique group-owned OS-temp sandbox and set TMPDIR/TEMP/TMP in
the tool and all row/test children before process startup. Thus even assembly-startup and ordinary
allocation sweeps cannot inspect the real shared temp directory. Before starting each native
child, give it its own contained sandbox; clear inherited
task/session authorization variables through child environment overrides without logging values.
Use local fake owner/slot endpoints where a checkpoint child needs them. The outer ordinary
checkpoint driver still obtains its real build slot. All children and registered tasks are
awaited in finally with fresh bounded cleanup tokens, even after a failed first assertion.

### Delivery inventory

This change adds filesystem/process handoffs, not a session-message queue or channel delivery.
No UserPrompt receipt is claimed. Existing task-owner HTTP semantics stay covered by the 23
`CheckpointTaskOwnershipTests`; no paid model, live runner session or messaging broker is used.

| Producer -> recipient | Durable identity/persistence | Recovery and observed receipt |
|---|---|---|
| Fixture allocator -> test body -> After(Test) | assembly invocation + test attempt + root ID; checked marker before return | V-1/V-5 observe allocation, actual TUnit teardown and absent roots after native host exit. Hook entry alone is not cleanup proof. |
| Starter -> native executor | run/root ticket; preparing, launch-attempted, captured process generation, independent executor entry ack | V-3/V-6 pause/crash each boundary, exercise delayed ack and a child already at done before parent return; assert journal, final process identity, bytes and preserved report. A returned PID is not an entry receipt. |
| Executor -> waiter/next start/stop | run ID; checked custody independent of progress; report/log/TRX flushed before terminal state | V-4 observes actual child exit plus cleanup receipt and evidence hashes. Busy/live waiter returns 75; dead nonterminal returns 6. Done alone is insufficient. |
| Ownerless marked root -> admitted sweeper -> external observer | root ID + marker generation + mutation lock + persisted cursor/partial-delete receipt | V-7/V-9 re-enter after interrupted deletion, registration race and failed receipt write. External before/after allocated bytes and surviving sentinel hashes are the receipt. |

The native lifecycle host has nonce-bound file/pipe gates outside candidates. Keep an audit of
which boundary was reached before signaling the exact owned process. A test awaiting an
unreached gate fails by its deadline; it cannot count a sleep followed by process absence as a
crash-boundary proof. D-4's no-ack launch ambiguity is intentionally retained and reported.

### Proves it works now

| ID | Exact test homes/layer | Decisive outcome |
|---|---|---|
| V-1 | Scope tests and actual lifecycle-host integration | Pass, assertion failure, cancellation, setup failure after allocation and multiple roots leave zero finished-owned roots. Teardown joins children; failures export outside roots and identify unreclaimed paths. |
| V-2 | Process identity / run ownership primitives, real sentinel filesystem | Marker, process, path, inventory and lock guards refuse destruction; same-host/boot/namespace confirmed-dead control removes the exact allowed target. |
| V-3 | Launch cleanup fault injection | Copy writes some bytes then throws; definite launch refusal rolls back its own partial run. Launch-intent failure launches zero children. Unknown/post-launch write failure retains image. Ack precedes all work and fast child progress survives parent return. |
| V-4 | Tool copy cleanup plus existing wait/app/evidence/output tests | Known dead nonterminal wait == 6; completed green/red == original 0/1; live/Unknown wait == 75. Tool-only cleanup preserves hashes of all evidence, independent of KeepOutputs; retry is idempotent. Stop awaits exact-generation exit and age-clean observes same veto. |
| V-5 | Six lifecycle parent tests, real TUnit 1.44 child | Actual Before/After(Test), separately selected expected-red/canceled child cases, complete child TRX, native child exit and external residual census; no parent self-call of DisposeAsync as the hook proof. |
| V-6 | Separate Linux/Windows native recovery classes | Real process generation, detached staged image, starter/executor death cuts, live sentinels, native links and Windows file sharing. Each OS row executes all its cases with zero skips. |
| V-7 | Sweep tests plus usage/inspection harness tests | Counted I/O proves limits and fair resumption, not merely reported counters; two coordinators share admission. Inventory command changes no file, including valid dead candidates. |
| V-8 | CP-9..CP-12 external measured repetition | Two checkpoint-namespace passes then two full Antiphon.Tests passes at one committed SHA, complete rosters/TRX, measured peaks, event totals and zero finished-owned residual count/allocated bytes each time. |
| V-9 | CP-13 native owner-death acceptance | Known eligible backlog drains below both 32 roots and 256 MiB, then to zero with sufficient admitted bounded passes; all live/unknown/legacy exclusions remain intact and separately measured. |

All new single methods below are one TUnit execution; their internal variants/loops are not
extra executions. Only `completed_wait_removes_only_tool` has four explicit Arguments rows:
`(exit=0,keep=false)`, `(0,true)`, `(1,false)`, `(1,true)`. Each asserts exact original
wait exit, Removed receipt, absent tool and byte-identical evidence. Do not replace these with
four assertions and retain a floor of four.

**Target roster (100 new executions).** The guard/PC tables name 57 methods, including two native
methods and one lifecycle method also listed below. Supplemental methods complete these totals:

| Class under Antiphon.Tests.Checkpoints | New executions | Additional methods beyond the guard table |
|---|---:|---|
| CheckpointProcessIdentityTests | 7 | `confirmed_dead_local_identity_permits_cleanup` |
| CheckpointRunOwnershipTests | 12 | none |
| CheckpointTempScopeTests | 9 | `marker_failure_rolls_back_empty_root`, `parallel_scopes_have_distinct_attempt_ownership`, `owned_disposal_does_not_require_dead_test_host` |
| CheckpointToolSourceTests | 2 | `default_source_is_the_production_tool_directory` |
| CheckpointLaunchCleanupTests | 11 | `partial_copy_failure_rolls_back_owned_run`, `definite_native_refusal_rolls_back_owned_run`, `dead_executor_ack_recovers_without_progress`, `late_executor_ack_resolves_launch_unknown` |
| CheckpointToolCopyCleanupTests | 16 | `completed_wait_removes_only_tool` (4 argument executions); `already_absent_is_idempotent` |
| CheckpointTempRootSweepTests | 11 | `eligible_backlog_drains_without_touching_exclusions` |
| CheckpointTempUsageTests | 5 | `incomplete_trx_never_accepts_residue`, `allocated_bytes_include_written_payload`, `repeated_pass_manifest_is_exact`, `assembly_and_allocation_invoke_bounded_sweep` |
| CheckpointTempLifecycleTests | 6 | `passing_test_runs_teardown`, `assertion_failure_runs_teardown` (PC-57), `cancellation_runs_teardown`, `setup_failure_runs_teardown`, `multiple_roots_run_teardown`, `early_assertion_still_joins_owned_child` |
| CheckpointRecoveryLinuxTests | 10 | Native inventory below (including PC-55 method) |
| CheckpointRecoveryWindowsTests | 14 | Native inventory below (including PC-56 method); Review 953ac836 fix round adds `allocation_sample_fails_on_access_denied_subfolder`, `allocation_sample_does_not_follow_junction_root`, `drifted_boot_reading_is_still_the_local_boot` |

Scope/probe/policy/launch/sweep/source classes are Unit. Lifecycle, usage script/observer tests
and both native classes are Integration with the assembly-local ProcessSpawnLimit; register
Slow on the classes needing it and in slow-tests-allowlist. Existing categories remain unless
classification requires a correction. Children are prebuilt by the parent target, never built
from a test while holding its parent slot. LifecycleHost has its own limiter if it spawns.

Native Linux and Windows classes each implement these ten exact methods (separate OS-gated
classes; unsupported OS is an explicit Skip, not a passing return):

1. `native_identity_keeps_live_then_removes_dead`: real identity adapter + real policy; check
   sentinel after cleanup while held live, then after awaited exit. While live require
   Retained(identity-alive) and zero destructive I/O attempts, so Windows file locking cannot
   mask a defective live-process probe. Capture PID/start/host/boot/
   namespace and prove no replacement PID is signaled using the scripted identity variant.
2. `detached_staged_copy_survives_starter_exit`: staged tool's complete output outside source;
   child reads a dependency/resource after starter exit. Assert launched image path is private.
   Windows must retain DETACHED_PROCESS/NEW_PROCESS_GROUP without BREAKAWAY; Linux proves setsid.
3. `done_live_executor_retains_image`: gate child after terminal publication; wait == 75 and
   image intact, then release/await exit and wait == original exit with Removed receipt.
4. `starter_death_before_launch_reclaims_partial_copy`: stop only the captured starter at the
   preparing gate, prove no native launch attempted, recover exact owned partial root.
5. `executor_death_after_ack_before_progress_recovers`: real entry ack, no state snapshot, exact
   child death; wait == 6 and only tool removed, with crash diagnostic outside tool.
6. `dead_test_host_live_nested_executor_retains_root`: kill captured lifecycle owner while its
   child remains gated; another host's sweep leaves whole root intact; after child exit it drains.
7. `other_worktree_live_owner_survives_sweep`: two linked worktrees in a test-owned Git repository share a
   temp sandbox; sweep in one never alters the other's marker/image/sentinel or stops its child.
8. `concurrent_sweepers_cannot_delete_new_registration`: two actual coordinator processes and
   barrier-controlled registration; winner/loser receipts, no duplicate deletion and live sentinel.
9. `native_links_keep_targets_unchanged`: root, ancestor and descendant symlink variants on
   Linux; directory junction/reparse variants on Windows. Hash targets after each attempted sweep.
10. `launch_unknown_survives_until_late_ack`: stop exact starter after intent/OS launch before
    parent identity save; held child has not acked. No deletion, then real ack/exit enables recovery.

Windows additionally implements `locked_tool_delete_is_reported_then_retried`: a child holds
FileShare.Read without Delete, so a real sharing violation leaves a Failed/Retained retryable
receipt; after release and confirmed holder exit, retry removes it. Linux open-file deletion
success is not evidence for this method. Add a native failure gate for unsafe symlink permissions;
unavailable junction privileges count as missing Windows evidence, not a completed row.

The lifecycle parent asserts an exact selected child method and its execution record. Pass
child exits zero; assertion/setup child has a nonzero native exit and the unique intentional
failure message; cooperative cancellation has a recorded canceled/aborted or cancellation-failed
TRX outcome, not an empty TRX. Record the pinned runner's actual cancellation exit without
inventing a new public tool exit. All six parent tests themselves pass and contribute six,
not the number of child assertions. A killed child is the V-9 orphan case, not V-5 cancellation.

### Guards the regression

- **R-1 (allocator census):** all 78 old calls migrate, including helper calls. A source census
  must find no unowned `CheckpointFixtures.TempDir(`; compiled discovery must show every allocating
  test uses the instance scope. The instance scope's post-return external census is authoritative.
- **R-2 (copy/content):** preserve `ShadowCopyTests.copies_the_dll_set_never_the_source_tree`,
  move its excluded source sentinel *inside* the copied input (currently its `src` is a sibling),
  include dependency/resource bytes, and assert exact byte identity. Fake sources each <64 KiB;
  default production source selection and real detached launch stay covered separately.
- **R-3 (public behavior):** preserve all existing assertions in App (3), Wait (8), Evidence (6),
  Output (5), RunStateStore (1), Import (20), TaskOwnership (23), ExecutorLog (3) and ShadowCopy (1).
  Replace guessed dead PID setup/direct removal calls with explicit checked owned fixtures; never
  add a test-only bypass to keep their current setup. Existing failure/report/KeepOutputs/owner
  exit behavior must not change while custody gets stricter.
- **R-4 (complete selection):** base has 24 checkpoint test classes, 144 Test methods and 150
  argument-expanded cases (ExitCodeTests contributes seven). Four TimeoutTests are Windows-only;
  DetachedLauncherTests has one Linux-only case. With this exact 100-case addition, namespace
  selection is 250 cases: Linux 235 executed + 15 declared OS skips, Windows 239 + 11. The fix
  round after Review 953ac836 adds four (three Windows-native, one BuildSlotClient), and
  this repair adds two sweep tests, so it is 256: Linux 238 + 18, Windows 245 + 11. Reclassifying
  Unit/Integration does not change the namespace total. Any count/name delta needs reconciliation
  before the checkpoint runs; do not silently lower these floors.
- **R-5 (test infrastructure):** run the full default Unit category plus
  TestClassificationGuardTests (1), TestLaneCategoryGuardTests (1), ProcessSpawnLimitTests (3).
  Confirm new native/host classes have the limiter; full-suite runs retain production-runner
  guards and require available test-owned PostgreSQL/Docker. No Antiphon.Agents.Pty.Tests overlap.

### Guard inventory

Every row is independently bypassable. One G maps to one PC; repeated use of a method is avoided.
Foreign host/boot/namespace, links at three positions, and each cleanup entry point are deliberately
separate controls. Each negative fixture makes all guards other than its target eligible, so an
unrelated veto cannot mask the mutant. Include a valid dead removable sibling so a policy that
always refuses cleanup cannot pass the ordinary suite. Budget tests drive both the test-root
coordinator and selected-results-root next-start recovery through the shared bounded walker;
neither caller may bypass the work limits.

| Guard | Plan invariant | Positive control |
|---|---|---|
| G-1 | D-1: allocation requires an explicit owner | PC-1 |
| G-2 | D-1: publish and register before exposing a root | PC-2 |
| G-3 | D-1: sealing rejects new allocation and awaits existing async work | PC-3 |
| G-4 | D-1: join owned children before deletion | PC-4 |
| G-5 | D-1: cleanup survives cancellation of the test token | PC-5 |
| G-6 | D-1: failed deletion is observable without hiding the assertion or abandoning siblings | PC-6 |
| G-7 | D-5: exact c723 lowercase-hex name and direct temp child | PC-7 |
| G-8 | D-3/D-5: supported, complete, parseable ownership marker | PC-8 |
| G-9 | D-3: marker identity binds its actual root/run/path | PC-9 |
| G-10 | D-3: AliveSame never authorizes automatic deletion | PC-10 |
| G-11 | D-3: access denied, unsupported and probe error are Unknown | PC-11 |
| G-12 | D-3: same PID with a different start identity is ReusedPid | PC-12 |
| G-13 | D-3: host identity equality | PC-13 |
| G-14 | D-3: boot identity equality | PC-14 |
| G-15 | D-3: PID-namespace identity equality | PC-15 |
| G-16 | D-3: normalized lexical containment with a separator boundary | PC-16 |
| G-17 | D-3: candidate root cannot be a link/reparse point | PC-17 |
| G-18 | D-3: no linked/reparse ancestor | PC-18 |
| G-19 | D-3: no linked/reparse descendant | PC-19 |
| G-20 | D-3/D-5: failed enumeration is uncertainty | PC-20 |
| G-21 | D-3: exclusive mutation lock is mandatory | PC-21 |
| G-22 | D-3: re-read marker and process identity after acquiring the lock | PC-22 |
| G-23 | D-3/D-5: registration and cleanup share serialization/sealed state | PC-23 |
| G-24 | D-3: current image veto independent of phase/probe | PC-24 |
| G-25 | D-5: live nested executor protects entire test root | PC-25 |
| G-26 | D-5: complete nested inventory including custom roots | PC-26 |
| G-27 | D-4: checked preparing custody before request/manifest/copy | PC-27 |
| G-28 | D-4: durable launch-attempted before invoking OS | PC-28 |
| G-29 | D-4: Unknown is distinct from definite NotStarted | PC-29 |
| G-30 | D-4: failure after possible launch cannot roll back | PC-30 |
| G-31 | D-4: executor entry ack before request/log/owner HTTP/rows | PC-31 |
| G-32 | D-4: starter never overwrites executor running/done progress | PC-32 |
| G-33 | D-3: bounded checked atomic custody writes | PC-33 |
| G-34 | D-4: wait cannot bypass cleanup eligibility | PC-34 |
| G-35 | D-4: next-start recovery obeys custody and selected results root | PC-35 |
| G-36 | D-4: stop never signals reused/unknown PID | PC-36 |
| G-37 | D-4: stop completion waits at most 10s then retains if still live | PC-37 |
| G-38 | D-4: explicit whole-run age cleanup still requires custody | PC-38 |
| G-39 | D-4: EvidenceFolder/executor have no unguarded removal route | PC-39 |
| G-40 | D-4: automatic cleanup deletes tool only | PC-40 |
| G-41 | D-1/D-5: a sweeper cannot kill to manufacture eligibility | PC-41 |
| G-42 | D-5: ten-minute creation grace | PC-42 |
| G-43 | D-5: five-minute cross-process admission | PC-43 |
| G-44 | D-5: max 16 root completions per pass | PC-44 |
| G-45 | D-5: max 256 MiB removed per pass, unknown sizes not zero | PC-45 |
| G-46 | D-5: max 512 direct entries, lazy enumeration | PC-46 |
| G-47 | D-5: max 10000 descendant entries per pass | PC-47 |
| G-48 | D-5: two-second cooperative time budget | PC-48 |
| G-49 | D-5: partial deletion grants no future unconditional authority | PC-49 |
| G-50 | D-5: marker/custody survives until last owned file is removed | PC-50 |
| G-51 | D-5: persistent bounded cursor and oldest verified work first | PC-51 |
| G-52 | D-4/D-5: failed delete never counts as reclaimed | PC-52 |
| G-53 | D-5: operator inventory never grants deletion authority | PC-53 |
| G-54 | D-2: small fixture seam preserves real production copy | PC-54 |
| G-55 | D-3: Linux native identity adapter distinguishes live from confirmed exit | PC-55 |
| G-56 | D-3: Windows native identity adapter distinguishes live from confirmed exit | PC-56 |
| G-57 | D-1: actual TUnit After(Test) registration guarantees teardown on assertion failure | PC-57 |

### Positive controls

All methods here are authored/run green in Code's ordinary rows. After separate ordinary Review
and confirmed publication, Mutation runs the following one-at-a-time compiling source defects at
landed L. Filter is exactly `/*/Antiphon.Tests.Checkpoints/<Class>/<method>*`, MinExecuted=1,
Expect=`Class.method` (no class-wide PC runs). For internal variants, record each named assertion
case; still only one TUnit result. Mutate the real implementation branch named by the guard,
never its test double or expected value. A failing compile, wrong gate, fixture exception or zero
executions is invalid. Red must contain the specified assertion in a fresh complete TRX.

| PC | Exact Class.method | Compiling defect in guarded implementation | Intended red assertion |
|---|---|---|---|
| PC-1 | `CheckpointTempScopeTests.allocation_requires_an_owner` | Remove the absent-owner refusal and allocate through the ordinary path | unowned allocation throws; createdRoots == 0 |
| PC-2 | `CheckpointTempScopeTests.root_is_registered_before_return` | Move scope registration after the injected return boundary | registeredRoots contains returnedRoot at that boundary |
| PC-3 | `CheckpointTempScopeTests.sealed_scope_awaits_registered_work` | Skip the registered-work await in disposal | deleteCalls == 0 while work gate is held; after release residualRoots == 0 |
| PC-4 | `CheckpointTempScopeTests.live_child_prevents_scope_deletion` | Treat the child join timeout as successful exit | childSentinel unchanged; retainedRoots == 1; teardown failure identifies child |
| PC-5 | `CheckpointTempScopeTests.canceled_test_gets_a_fresh_cleanup_token` | Pass the canceled test token to cleanup instead of the bounded fresh token | cleanup operation sees IsCancellationRequested == false; residualRoots == 0 |
| PC-6 | `CheckpointTempScopeTests.teardown_preserves_failure_and_attempts_other_roots` | Swallow the root deletion exception without recording teardown failure | diagnostic contains original-assertion and delete-denied; unaffected root absent; failed root retained |
| PC-7 | `CheckpointRunOwnershipTests.only_exact_direct_child_names_are_candidates` | Remove candidate name/depth validation | wrong-prefix, uppercase, short-ID and nested lookalike sentinels unchanged; deleteCalls == 0 |
| PC-8 | `CheckpointRunOwnershipTests.unsupported_or_missing_markers_are_retained` | Accept an absent, malformed or unknown-version marker as owned | each candidate retained with marker reason; deleteCalls == 0 |
| PC-9 | `CheckpointRunOwnershipTests.marker_binds_root_run_and_normalized_path` | Ignore journal-to-candidate identity/path equality | copied marker and wrong run ID both retained; outside sentinel unchanged |
| PC-10 | `CheckpointProcessIdentityTests.same_live_generation_is_a_veto` | Map AliveSame to Dead in cleanup eligibility | live sentinel exists and deleteCalls == 0 |
| PC-11 | `CheckpointProcessIdentityTests.unknown_probe_is_a_veto` | Map the probe exception/Unknown branch to Dead | each injected uncertainty retains sentinel; receipt reason is identity-unknown |
| PC-12 | `CheckpointProcessIdentityTests.reused_pid_is_retained_and_never_signaled` | Treat ReusedPid as Dead in eligibility | replacement sentinel unchanged; deleteCalls == 0; signalCalls == 0 |
| PC-13 | `CheckpointProcessIdentityTests.foreign_host_is_not_local_dead` | Remove host equality check before the local missing-PID probe | foreign host retained; local probeCalls == 0 |
| PC-14 | `CheckpointProcessIdentityTests.foreign_boot_is_not_local_dead` | Remove boot equality check before the local probe | old-boot marker retained; local probeCalls == 0 |
| PC-15 | `CheckpointProcessIdentityTests.foreign_pid_namespace_is_not_local_dead` | Remove namespace equality check before the local probe | foreign-namespace marker retained; local probeCalls == 0 |
| PC-16 | `CheckpointRunOwnershipTests.lexical_escape_is_rejected` | Remove the normalized containment check | sibling-prefix and dot-dot escapes retained; outside sentinel hash unchanged |
| PC-17 | `CheckpointRunOwnershipTests.linked_root_is_rejected` | Skip root link-attribute inspection | linked candidate retained; deleteCalls == 0; target hash unchanged |
| PC-18 | `CheckpointRunOwnershipTests.linked_ancestor_is_rejected` | Skip ancestor link inspection | ancestor-link candidate retained; deleteCalls == 0; target hash unchanged |
| PC-19 | `CheckpointRunOwnershipTests.linked_descendant_is_rejected` | Skip descendant link inspection during the complete preflight | candidate retained with descendant-link; no file in it deleted |
| PC-20 | `CheckpointRunOwnershipTests.incomplete_walk_is_not_empty` | Convert an enumeration IOException into an empty successful inventory | candidate retained; deleteCalls == 0; reason inventory-incomplete |
| PC-21 | `CheckpointRunOwnershipTests.busy_mutation_lock_refuses_cleanup` | Continue cleanup after lock acquisition fails | busy receipt; zero marker writes, deletes and process signals |
| PC-22 | `CheckpointRunOwnershipTests.custody_is_reread_under_the_lock` | Use the pre-lock snapshot as the deletion claim | replace marker or make owner live at gate; sentinel survives; fresh probe observed |
| PC-23 | `CheckpointRunOwnershipTests.deleting_root_rejects_registration` | Allow launch registration when root is marked deleting | registration refused; launchCalls == 0; no new child in deleting root |
| PC-24 | `CheckpointRunOwnershipTests.current_executor_image_is_always_retained` | Remove current-image containment veto while other identities are scripted Dead | image remains for done and running; deleteCalls == 0 |
| PC-25 | `CheckpointToolCopyCleanupTests.nested_live_executor_vetoes_whole_root` | Ignore nested AliveSame when owner is Dead | whole root including non-tool sibling remains; deleteCalls == 0 |
| PC-26 | `CheckpointToolCopyCleanupTests.unknown_nested_custody_vetoes_whole_root` | Skip an unknown/unregistered run when building nested custody | custom-results, missing-journal and unknown-child sentinels all survive |
| PC-27 | `CheckpointLaunchCleanupTests.preparing_journal_precedes_all_run_io` | Move preparing journal after the first copy/request write | journal write failure yields copyCalls == 0 and requestWrites == 0 |
| PC-28 | `CheckpointLaunchCleanupTests.launch_intent_failure_prevents_native_launch` | Ignore failed intent publication and invoke launcher | launchCalls == 0; exact injected persistence failure returned |
| PC-29 | `CheckpointLaunchCleanupTests.unknown_launch_outcome_is_never_rolled_back` | Classify the maybe-started launcher result as NotStarted | tool and launch intent remain; receipt launch-outcome-unknown |
| PC-30 | `CheckpointLaunchCleanupTests.post_launch_bookkeeping_failure_keeps_image` | Run prelaunch rollback on starter-identity or latest write failure | started image remains; original failure diagnostic retained; child still observable |
| PC-31 | `CheckpointLaunchCleanupTests.executor_ack_is_checked_before_work` | Continue entry after ack publication fails | requestReads == 0; logOpens == 0; ownerCalls == 0; rowLaunches == 0; executor exit == 6 |
| PC-32 | `CheckpointLaunchCleanupTests.starter_cannot_overwrite_child_progress` | Restore the parent write of phase=starting after launch returns | child running/done state and marker unchanged after StartAsync |
| PC-33 | `CheckpointLaunchCleanupTests.custody_write_failure_is_not_success` | Swallow the final atomic-replace IOException and return success | write fails after configured retry limit; old complete journal unchanged; no new deletion authority |
| PC-34 | `CheckpointToolCopyCleanupTests.wait_uses_custody_not_progress_pid` | Route wait tool removal directly to the raw remover | done plus Unknown journal keeps tool; wait exit == 75; report hash unchanged |
| PC-35 | `CheckpointToolCopyCleanupTests.next_start_uses_the_shared_veto` | Delete a terminal sibling solely from state PID absence | unknown sibling and other results-root sentinel unchanged; eligible sibling tool removed |
| PC-36 | `CheckpointToolCopyCleanupTests.stop_validates_generation_before_signaling` | Signal the numeric PID before validating start identity | signalCalls == 0 for ReusedPid and Unknown; stop cannot report success |
| PC-37 | `CheckpointToolCopyCleanupTests.stop_waits_for_confirmed_exit` | Publish stopped and delete immediately after Kill returns | before exit gate phase != stopped and tool exists; timeout reports incomplete stop |
| PC-38 | `CheckpointToolCopyCleanupTests.age_clean_cannot_bypass_live_custody` | Remove custody validation from RemoveOlderRuns | old live/unknown/reused runs survive; only eligible old run removed |
| PC-39 | `CheckpointToolCopyCleanupTests.evidence_write_has_no_raw_delete_bypass` | Restore direct recursive tool deletion from EvidenceFolder.Write | writing green/red evidence with missing authority keeps the tool |
| PC-40 | `CheckpointToolCopyCleanupTests.automatic_recovery_preserves_all_evidence` | Change automatic deletion target from tool to run directory | request/manifest/report/TRX/log hashes unchanged; tool absent |
| PC-41 | `CheckpointToolCopyCleanupTests.sweep_never_signals_a_process` | Signal the known child before returning a live-owner skip | sweep signalCalls == 0; sentinel remains; explicit owner teardown still joins its own child |
| PC-42 | `CheckpointTempRootSweepTests.grace_is_additional_to_dead_ownership` | Skip the age/grace predicate for an otherwise eligible root | at grace minus one tick root remains; at grace it is removed |
| PC-43 | `CheckpointTempRootSweepTests.sweep_interval_is_shared_across_hosts` | Ignore persisted admission timestamp after acquiring coordinator lock | second coordinator before interval performs zero candidate scans/deletes |
| PC-44 | `CheckpointTempRootSweepTests.root_delete_count_is_bounded` | Ignore configured root-deletion cap | with cap 2 and 3 eligible roots completedRoots == 2 and third survives |
| PC-45 | `CheckpointTempRootSweepTests.actual_deleted_bytes_are_bounded` | Ignore remaining deletion-byte budget | measured delete work <= injected byte budget; excess file and marker remain |
| PC-46 | `CheckpointTempRootSweepTests.direct_entry_scan_is_bounded` | Materialize full direct directory enumeration before applying cap | MoveNextCalls <= injected scan limit plus one bounded lookahead |
| PC-47 | `CheckpointTempRootSweepTests.descendant_scan_is_bounded` | Ignore descendant walk counter | visited descendants <= injected limit; incomplete candidate retained |
| PC-48 | `CheckpointTempRootSweepTests.monotonic_deadline_stops_new_operations` | Remove the monotonic deadline check between operations | after scripted expiry no additional walk/probe/delete starts; overrun is reported |
| PC-49 | `CheckpointTempRootSweepTests.resumed_deletion_rechecks_every_veto` | Resume a pending deletion without fresh identity/inventory checks | make owner/child live or introduce link between passes; second-pass deletes == 0 |
| PC-50 | `CheckpointTempRootSweepTests.marker_survives_partial_deletion` | Delete marker as the first incremental operation | after one budgeted pass marker and nested custody still parse; next pass resumes safely |
| PC-51 | `CheckpointTempRootSweepTests.cursor_prevents_new_arrival_starvation` | Reset cursor to first page on every sweep | old eligible root beyond page one removed within frozen-roster page bound despite arrivals |
| PC-52 | `CheckpointToolCopyCleanupTests.failed_delete_receipt_is_truthful_and_retryable` | Return Removed and charge full bytes after an injected delete failure | receipt Failed; reclaimedBytes == observed deletion only; later retry removes remainder |
| PC-53 | `CheckpointTempUsageTests.inspection_is_read_only` | Invoke validated deletion from the inspection command for one eligible root | before/after tree and content hashes equal; every eligible and legacy sentinel survives |
| PC-54 | `CheckpointToolSourceTests.tiny_source_is_really_copied_to_the_launched_image` | Remove ShadowCopy.CopyToolOutput in StartAsync | destination sentinel bytes match source; launched DLL is under destination, not source |
| PC-55 | `CheckpointRecoveryLinuxTests.native_identity_keeps_live_then_removes_dead` | Return Dead from the Linux probe for the held live child | while live receipt is Retained(identity-alive), destructiveIoAttempts == 0 and sentinel intact; after handle-observed exit removal succeeds |
| PC-56 | `CheckpointRecoveryWindowsTests.native_identity_keeps_live_then_removes_dead` | Return Dead from the Windows probe for the held live child | while live receipt is Retained(identity-alive) and destructiveIoAttempts == 0; sharing-violation retention cannot satisfy those assertions; after handle-observed exit removal succeeds |
| PC-57 | `CheckpointTempLifecycleTests.assertion_failure_runs_teardown` | Remove the actual After(Test) attribute from the shared base hook, retaining compilable disposal code | parent observes completed expected-red child but residualRoots != 0; the zero-residue assertion fails |

For PC-7 (name/depth), PC-8 (missing/malformed/version), PC-9 (root/run/path), PC-11 (probe
uncertainty), PC-22 (marker/probe), PC-26 (inventory/child), PC-30 (latest/identity), PC-36
(reuse/Unknown), PC-38 (live/Unknown/reuse), and PC-53 (eligible/legacy), one common predicate
controls the listed variants. If Code implements independently bypassable predicates instead,
record additional mutation variants under that PC and increase its cost before handoff; one
variant may not be credited for untouched guards. Mutation discovery audits this explicitly.

Use the copied unchanged local run-checkpoint driver and lib/build-slot helper outside the
SourceLanding snapshot, per testing-and-build. Each PC has baseline green, separate compiling
red, exact restoration, fresh build/restored green, three unique bin/ and results identities.
Refresh source timestamps after restoring. PCs 1..54 use Linux local inherited execution;
PC-55 and PC-57 are Linux native and PC-56 Windows native. The caller commissions platform-bound native
verification at the same L through supported SourceLanding custody; one snapshot never exports
its access to another executor. Serialize same-operation attempts if admission requires it and
preserve both reports on the same post-land companion. No second Code/landing owner is created.
Unavailable native Mutation stays pending; ordinary native acceptance cannot substitute for it.

Control total: **57 guards, 57 mapped PCs, 0 unmapped, 0 duplicate PC mappings**. Mutation also
checks omission/bypass variants revealed by the final implementation and reports survivors.
PC-57 uses the real shared hook source compiled into LifecycleHost; rebuild/stage that child in
all three phases. Disposing a scope directly cannot substitute for its parent residual assertion.

### Disk-usage acceptance protocol

Implement ASCII-only `scripts/verify-checkpoint-temp-usage.ps1` as the V-8/V-9 external
observer/driver. Its exact commands are CP-9..CP-13. This script is part of S4; no executable
script or acceptance data is claimed by TestDesign. Command rows already own a checkpoint build
slot. The script executes the prebuilt Antiphon.Tests DLL (resolved from CP-4 output), records
that inherited enclosing driver lease, and does **not** acquire a nested slot or invoke another
build/checkpoint launcher. A direct operator invocation outside a row uses build-slot.ps1 once.
It owns and awaits its one test child, the sampler and any V-9 host children. It fails if the
prebuilt output does not match the acceptance commit. No builds or code edits between passes.

Usage harness tests launch only the small staged probe or validate supplied observer/TRX inputs;
they never recursively launch Namespace/Full from inside those same selections.

Inputs: `-Phase Namespace|Full|Orphans`, `-Pass 1|2`, `-OutputPath bin-c804-final/`.
Reject other values. Fixed filters: Namespace = `/*/Antiphon.Tests.Checkpoints/*/*`;
Full = `/*/*/*/*`. The Full selection means all non-explicit Antiphon.Tests cases eligible for
that OS, with declared platform/opt-in skips accounted by method/reason; it is not Unit alone
or the old Docker class list. Keep the actual host's normal supported concurrency, bounded by
existing limiters. Requiring a serial outer row does not mean maximum-parallel-tests=1.
If a known unsupported Linux native case cannot skip safely, record it in a committed complete
lane manifest before running: reconcile every compiled class/method to selected or an existing
platform/opt-in exclusion, name the owning card, and get that plan change reviewed. Never drop
an inherited red test to complete the disk proof. A required full-pass failure remains red;
exact-method baseline diagnostics may classify it but cannot turn this row green.

The observer creates a unique absolute temp sandbox outside the checkout, keeps a validated
ownership marker for its own shell, and sets TMPDIR/TEMP/TMP only in the test child before
startup. Both passes of a pair use the same sandbox so residue cannot be hidden by changing
directories; namespace and full pairs have different sandboxes. Invocation IDs differ, SHA and
DLL digest do not. Metadata/events/reports live at
`.antiphon/c804-usage/<sha>/<phase>/<pass>/<invocation>/` outside these candidates. Persist the
pair/sandbox binding; refuse an existing unrelated binding or results directory. Do not remove
the sandbox between passes. Inspect checkpoint roots before any observer-owned final cleanup.

The script captures the actual TUnit compiled selection roster from a test-only assembly hook,
including expanded argument IDs/categories/skip eligibility, and final TRX results. Discovery
is a completeness inventory, never execution proof. Join every eligible selected test to a
terminal TRX result, assert no missing/duplicate/wrong-SHA result, preserve all skips and require
zero unexplained skips. A numeric floor alone does not establish full selection. The Unit/full
floor of 1000 is the conservative existing checkpoint lane floor, not a fresh full-suite count;
the compiled roster equality is the stricter acceptance gate. Namespace uses the exact 238
Linux execution floor and 256 selected cases derived above. Full must contain every eligible
checkpoint case on the Windows compiled roster, all other eligible compiled cases, and at least 1000 executions. Both full
passes must have the same eligible roster and argument expansion. Record actual counts, not 1000
as if it were measured. `--list-tests` output is not an executed roster.

Take baseline, 1-second samples, and a final snapshot after test-host exit and all child joins.
Record root/marker IDs, invocation/attempt IDs, source SHA, native identities, create/copy/delete
operation events, logical copied bytes, allocated bytes, root counts, eligible and retained
inventories, cleanup receipts, duration and free disk. On Linux use stat allocated blocks
(`st_blocks * 512`, or equivalent non-apparent du) without following symlinks, deduplicating
inodes if necessary. Never create sparse payloads for the byte acceptance. Event high-water
counts/bytes supplement sampled peaks; mark sampled peaks explicitly as lower bounds. An I/O
error, permission-denied sample, ENOSPC, missing event tail, incomplete child/host exit or TRX
makes the pass inconclusive/red, never zero usage. No credential/env dump goes into evidence.

Required report fields per pass:

| Measurement | Acceptance |
|---|---|
| created/deleted roots, copied logical bytes, sampled and event high-water root counts | Counts reconcile by root ID; each fake source <65536 bytes; no unknown event gap. |
| baseline / peak allocated bytes / final residual allocated bytes | Absolute values and pass-2 minus pass-1 deltas recorded, separately for checkpoint roots and ordinary build/results outputs. Peaks have no invented universal cap; residual has the strict zero gate below. |
| finished healthy scopes, including expected assertion/cancel lifecycle probes | **0 retained roots and 0 allocated bytes** after joins; created roots all have terminal removal evidence. No GB-scale retained growth is possible under this stronger gate. |
| complete roster, TRX and native process completion | Full joined roster, original process exit, executed/passed/failed/skipped counts and hashes; 0 test failures for acceptance. Ordinary deliberate child red/cancel is asserted by its passing parent. |
| noneligible artifacts | Named reason, count and allocated bytes, no unexplained new unmarked or unknown roots from a healthy run; deliberate injected fixtures are removed by their owning test after assertions. |

CP-11/CP-12 are Windows-pinned full-suite rows. The Linux full suite contains inherited
Windows-only failures, and its test child inherits the runner container's `ASPNETCORE_URLS`
and `SessionRunner__*` settings; one attempt went idle after binding `http://+:8080`.
Linux qualification uses the complete CP-8 Unit lane and CP-9/CP-10/CP-13 instead.
Windows Final Review owns CP-11/CP-12. Clearing those inherited settings is a
separate card and is outside this change. The earlier commit's final sentence is
superseded by this explicit platform assignment.

CP-13 is separate from normal zero-residue acceptance. A real child creates 48 marked roots
with 8 MiB written payload each and one 300 MiB root, publishes external inventory and is stopped
at a known gate by its owner. Record observed native owner exit. Also create six protected
fixtures: live owner, dead owner/live child, Unknown identity, reused PID, unmarked legacy and
launch-attempted/no-ack. Their payload hashes are checked after **every** pass. Inject zero
grace/interval only in this isolated acceptance coordinator to avoid wall-clock waits; count,
byte, scan and time limits remain 16 / 256 MiB / 512 / 10000 / 2s, and deterministic tests prove
the production 10-minute/5-minute defaults. No production temp coordinator gets these overrides.

Run up to 32 admitted bounded passes, stopping early only when eligible residue is zero;
resumption rechecks custody and retains markers. Require threshold crossing to <=32 roots AND
<=256 MiB, then zero; log number of passes, both byte measurements and any cooperative overruns.
Fail if not drained within that declared workload bound. Inspection of unknown retained sizes
must label unknown, never credit them as zero/reclaimed. Count only actually removed allocated
bytes. Keep the six excluded fixtures until all assertions/evidence finish, then their explicit
owner joins its children and disposes only its own sandbox; no sweeper is allowed to kill.

CP-13 emits exactly 18 named acceptance checks: owner-exit, inventory-complete, payload-allocated,
root-count-cap, byte-cap, direct-scan-cap, descendant-scan-cap, time-budget-accounted,
partial-marker-retained, resumed-custody-rechecked, count-target, bytes-target, eligible-drained,
live-owner-intact, live-child-intact, uncertain-identities-intact (Unknown/reuse),
legacy-intact, launch-unknown-intact. These are harness assertions, **not 18 TUnit executions**.
Namespace/Full emit the five measurement gates above plus their exact TRX/roster counters.

### Out of scope

No live shared-temp cleanup, legacy backfill, production janitor/schedule, server restart,
paid-agent launch, broker traffic, client/E2E build, unrelated backend fix or shared-obj/process-
grandchild repair. No public-session delivery was changed. Production output dependency selection
stays intact. Tests must retain a live descendant even where CARD-0774 containment is unresolved.
Launch-unknown, foreign/malformed/unmarked custody remains visible residue; neither age nor disk
pressure turns it into deletion authority. Lack of native evidence is reported as incomplete.

### Cost

Estimates, not measurements: ordinary V/R **295 minutes**, the sum of CP-1..CP-13 below
(Linux 90, Windows 205). This replaces the provisional 155-minute Plan estimate: complete full
passes have no current measured duration, so reserve 90 minutes each instead of assuming the
historical 25-minute suite. Exact-scope development builds total 42 minutes; lifecycle/native,
Unit, measured namespace and orphan rows add 48, Windows native 25, full passes 180. No claim of a
runtime saving until measurements exist; output reuse eliminates rebuilding per native/pass row.

Launcher bootstrap allowance **3 minutes per platform = 6** outside row leases. Mutation floor
**713 minutes**: PC-1..PC-54 at 12 each (three 4-minute build/method phases = 648); Linux PC-55/
PC-57 and Windows PC-56 at 15 each (=45); discovery/restoration/reporting 20. All 57 filters
are specified by Class.method above, Min=1 each
phase. Controls sharing files run separately; no speculative batching discount. Increase the
floor for added independent variants, never count compile failures as red controls. Total
estimated verification **1014 minutes = 295 + 6 + 713**, excluding implementation/authoring,
ordinary Review, slot queue time and platform dispatch waits. The dispatcher sets Code
ExpectAbout to 295 plus authoring/bootstrap, Mutation to its own floor, not the total twice.

### Execution and platform routing

Code commits each closed slice before verification. Required row order:
Linux runs CP-1, CP-2, CP-3, CP-4, CP-5, CP-6, CP-8, CP-9, CP-10, CP-13
in that order. Windows runs CP-7, then CP-11, then CP-12. Submit each row
separately: the current multirow tool eagerly starts independent builds and a
command row before its reused output is ready, despite `Serial=true`. The
single-row `scripts/run-checkpoint.ps1` driver is the repair-round path for
test rows; command rows run through `scripts/build-slot.ps1`. Preserve each
row's exact filter, output path, minimum and fresh results directory.
Platform qualifier checks at native test entry fail/skip visibly; the selected native row
requires zero skips. Re-read dispatch eligibility; request the OS, not a hard-coded runner.
Windows is qualification of the same Code owner's committed SHA, not another implementation.

The original tool bootstrap was run under `scripts/build-slot.ps1`; the tool
reported `build=failed` when the new test had a compile error. Its multirow
invocation also started CP-9 before `bin-c804-final/` was built. For this repair,
run each test row with `pwsh -NoProfile -File scripts/run-checkpoint.ps1` using the
table's exact filter (Markdown's `\|` cell escape is passed as `|`), and `-NoBuild`
only after the same output's build succeeds. Wrap CP-9/10/13 commands in
`scripts/build-slot.ps1`. Keep their reports and TRX under `.antiphon/`.
Slot exit 4 is not permission to run unleased.

All rows serial at the outer scheduler to avoid shared-obj races, two acceptance samplers at
once, or overlap with another row during a native boundary. TUnit's own limiters/concurrency
remain active. Deadlines derive as max(15,3*estimate): respectively 24,30,36,36,24,36,75,24,18,18,
270,270,24 minutes. CP-11/12 intentionally exceed the importer's warning-only 45-minute ceiling;
this is the explicit Windows full-suite exception, not a silent timeout widening after a hang.
Report a timed-out full pass as incomplete, not disk acceptance. Every row produces the standard CHECKPOINT line, with
reruns and non-TUnit child counts attached. Extra commands need a reason; baseline diagnosis
is exact failing methods, not another full suite. No retries are silently labeled flaky.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c804-s1/` | ownership-primitives | `/*/Antiphon.Tests.Checkpoints/(CheckpointProcessIdentityTests*)\|(CheckpointRunOwnershipTests*)\|(RunStateStoreTests*)/*` | V-2, R-3 | all 20 executions, 0 failed/skipped | 20 | 8 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c804-s2/` | fixture-and-copy | `/*/Antiphon.Tests.Checkpoints/(CheckpointTempScopeTests*)\|(CheckpointToolSourceTests*)\|(ShadowCopyTests*)\|(CheckpointImportTests*)\|(CheckpointTaskOwnershipTests*)\|(CheckpointExecutorLogTests*)/*` | V-1, V-3, R-1, R-2, R-3 | all 58 executions, 0 failed/skipped | 58 | 10 | true |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c804-s3/` | launch-and-recovery | `/*/Antiphon.Tests.Checkpoints/(CheckpointLaunchCleanupTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointAppTests*)\|(WaitCommandTests*)\|(EvidenceFolderTests*)\|(OutputCleanupTests*)/*` | V-3, V-4, R-3 | all 49 executions, 0 failed/skipped | 49 | 12 | true |
| CP-4 | all | `tests/Antiphon.Tests -> bin-c804-final/` | sweep-and-classification | `/*/*/(CheckpointTempRootSweepTests*)\|(CheckpointTempUsageTests*)\|(TestClassificationGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-7, R-1, R-4, R-5 | all 23 executions, 0 failed/skipped | 23 | 12 | true |
| CP-5 | all | CP-4 | tunit-lifecycle-linux | `/*/Antiphon.Tests.Checkpoints/CheckpointTempLifecycleTests/*` | V-1, V-5 | all 6 parent executions, complete expected child outcomes, 0 parent failed/skipped | 6 | 8 | true |
| CP-6 | all | CP-4 | native-linux | `/*/Antiphon.Tests.Checkpoints/(CheckpointRecoveryLinuxTests*)\|(DetachedLauncherTests*)/*` | V-2, V-3, V-4, V-6 | Linux only; all 11 executions, 0 failed/skipped | 11 | 12 | true |
| CP-7 | all | `tests/Antiphon.Tests -> bin-c804-windows/` | native-and-policy-windows | `/*/*/(CheckpointProcessIdentityTests*)\|(CheckpointRunOwnershipTests*)\|(CheckpointTempScopeTests*)\|(CheckpointToolSourceTests*)\|(CheckpointLaunchCleanupTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointTempRootSweepTests*)\|(CheckpointTempUsageTests*)\|(CheckpointTempLifecycleTests*)\|(CheckpointRecoveryWindowsTests*)\|(EvidenceFolderTests*)\|(TestClassificationGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, V-7, R-2, R-5 | Windows only; all 106 executions, 0 failed/skipped | 106 | 25 | true |
| CP-8 | all | CP-4 | unit-linux | `/*/*/*/*[Category=Unit]` | R-3, R-5 | >= 1000 executed, 0 failed; complete Unit roster; declared OS skips accounted | 1000 | 8 | true |
| CP-9 | all | n/a | namespace-usage-pass1 | `pwsh -NoProfile -File scripts/verify-checkpoint-temp-usage.ps1 -Phase Namespace -Pass 1 -OutputPath bin-c804-final/` | V-8, R-1, R-2, R-4 | exit 0; all 5 measurement gates; child 238 executed / 256 selected; 0 failed; exact 18 OS skips | n/a | 6 | true |
| CP-10 | all | n/a | namespace-usage-pass2 | `pwsh -NoProfile -File scripts/verify-checkpoint-temp-usage.ps1 -Phase Namespace -Pass 2 -OutputPath bin-c804-final/` | V-8, R-1, R-2, R-4 | exit 0; all 5 gates and pass delta; child 238 executed / 256 selected; 0 failed; exact 18 OS skips | n/a | 6 | true |
| CP-11 | all | n/a | full-usage-pass1 | `pwsh -NoProfile -File scripts/verify-checkpoint-temp-usage.ps1 -Phase Full -Pass 1 -OutputPath bin-c804-windows/` | V-8, R-4, R-5 | Windows only, after CP-7; exit 0; all 5 gates; child >=1000 executed including every eligible checkpoint case; full compiled roster equality; 0 failed | n/a | 90 | true |
| CP-12 | all | n/a | full-usage-pass2 | `pwsh -NoProfile -File scripts/verify-checkpoint-temp-usage.ps1 -Phase Full -Pass 2 -OutputPath bin-c804-windows/` | V-8, R-4, R-5 | Windows only, after CP-11; exit 0; all 5 gates and pass delta; same full roster as pass1; child >=1000 executed; 0 failed | n/a | 90 | true |
| CP-13 | all | n/a | owner-death-usage | `pwsh -NoProfile -File scripts/verify-checkpoint-temp-usage.ps1 -Phase Orphans -Pass 1 -OutputPath bin-c804-final/` | V-7, V-9 | exit 0; 18/18 named harness checks; eligible <=32 roots AND <=256 MiB then zero; 6 excluded fixtures intact | n/a | 8 | true |
