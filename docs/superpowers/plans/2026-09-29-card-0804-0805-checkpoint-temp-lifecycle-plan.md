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
