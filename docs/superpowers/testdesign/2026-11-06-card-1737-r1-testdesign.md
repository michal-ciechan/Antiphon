# CARD-1737 R1 custody/CAS verification design

Date: 2026-11-06. TestDesign task `a43ac839`, branch `feat/card-task-a43ac839`.
Inspected checkout and Plan base: `d83a52d8c320583bcd191e250be73f2ce04923e2`.

This appends the verification contract for **R1 only** to the unchanged
[fix design](../plans/2026-11-06-card-1737-s4a-witness-repairs-plan.md), following
the [S5b-2 format](2026-11-05-card-0824-s5b-2-testdesign.md). It refines the Plan's
R1 CP-1..3, classification and shared-policy ownership; it does not rewrite its
dispositions or authorize a production fix. Ledger rows **01/U9, 06/M-7 and
18/A23-final-stat** become three methods and three independent positive controls.
The other 45 ledger rows retain their Plan dispositions. No historical waived
kill becomes SourceLanding evidence. This design ran **zero builds/tests/PCs**.

## Verification design

### Inspection

Abbreviations: `G/` = `server/Infrastructure/Git/`, `I/` =
`tests/Antiphon.Tests/Infrastructure/`, `H/` = `tests/Antiphon.Tests/TestHelpers/`.
PC-file = `I/LandingRemovalPolicyControlTests.cs`; GC-file =
`I/WorktreeGuardedCleanupTests.cs`; NF-file = `I/WorktreeNoFollowDeleteTests.cs`.
These abbreviations never mean a positive-control ID.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Plan decisions D-1..D-8, 48-row ledger, R1 recipes, ownership map (also via `git show origin/feat/card-task-85a330bf:<plan>`), Windows, handoff and performance sections; investigation `e10e6f30073a247c248c413d62a4ea0c977c8d48:docs/investigations/2026-11-06-investigate-s4a-witness-gaps.md`; Debug `c318d8edfc97e046bb9a332ec34c76973f9a4f4a:docs/investigations/2026-11-05-debug-s5b-1-performance.md`, including P3-P7 | Three repaired witnesses only -> V-1..3; historical evidence and excluded obligations preserved |
| `docs/project-context.md`; testing-and-build classification, checkpoint schema/runner, delivery and restoration; test-performance-standards R1-R9; orchestration-loop stage order and D-2 | Test-only scope, ordinary Review before land, separate pending Mutation; numeric budgets below |
| PC-file: class metadata, `C1717_D4_RetryKeepsAReplacementDirectory`, `C1717_D4_RetryKeepsBytesAddedAfterThePartialRelease`, `C1717_D4_UnsealedReceiptIsNotDeleted`, `C1717_B4_ForeignDirectoryIsNotRestored`, `C1717_B5_PartialUnlinkOfApprovedFileResumes`, `RecordingRetention`, complete `RemovalFixture` | Actual receipt/native filesystem with modeled Git registration -> V-1, R-1/R-2; not real Git-unregister proof |
| GC-file: class metadata, content-boundary examples, `C443_BranchDeleteUsesOldSha`, `C443_BranchDeleteNoDeref`, `C443_BranchNotRetried`, complete `RemovalHarness`, diagnostics/probe/clock doubles | Final real Git command boundary -> V-2, R-3; diagnostics doubles are not Windows sharing evidence |
| NF-file complete, including `WalkWorld.Approve`, inotify writer and same-inode tests | Real Linux descriptor/stat/hash/unlink -> V-3; existing probabilistic walkers excluded from this deterministic repair |
| `G/GuardedWorktreeRemoval.cs`: removal admission, pending-record resume, first-pass move/unregister/delete, `CompleteBranchAsync`, `DeleteApproved`, `ApprovalListing`, `RestoreCustody`, `CustodyOf`; `G/WorktreeSetAside.cs` record/write/read/clear | U9 is custody equality at :909, distinct from restore equality at :887; M-7 delete argv at :408 -> G-1/G-2 |
| `G/WorktreeNoFollowDelete.cs`: Delete/DeleteSettled, Linux caller, `UnlinkSameNode`, `HashAt`, `TryStat`, `SameNode`, `SameContent`, `OpenLinuxDirectory`, stat layout and RemovalApproval | Post-hash stat refusal at :319 -> G-3; Windows and final-stat-to-unlink residual excluded |
| `H/LandingGitFixture.cs` initialization/template, RequiredAsync, independent reader example, FixtureGit command hooks/process tracking; `H/LandingSafetyHarness.cs` initialization/DI, DB context, RunAsync, AddSourceAsync/disposal; `H/TestDbFixture.cs` and `H/SharedStoreWarmup.cs` | Existing real Git + cloned DB and genuine lease; immutable Git template/model/container shared -> V-2; no helper edits |
| `tests/Shared/TestClassificationMetadata.cs`, TestClassificationGuardTests, NativeLinuxOnlyAttribute, NativeLinuxPolicyWitnessTests and witness forward/reverse/hash checks; `H/TestLaneCategoryGuardTests.cs`; execution-policy rc exclusion schema | Class lane XOR forbids method Integration on Unit; native marker requires an exact policy row -> R-4 |
| `Scripts/LiveLaneRunnerTests.WrapperOnlyPaidManifest`, ScriptHarness Run/Validate; `scripts/test-live-lane-run.ps1` protected inputs, full-inputs and wrapper test; `scripts/lib/nightly-policy.ps1` hash and exclusion readers | Policy change must update policyHash and PinnedInputDigest together; existing offline sentinel witness -> R-4 |
| `tests/Antiphon.Tests/Antiphon.Tests.csproj`, `server/Antiphon.Server.csproj` ProjectReference entries | Test-only Git-operation changes; server references Pty, messaging/client, runner contracts, resilience, observability. Test project also builds runner, fake-provider, watchdog, checkpoint/audit and fixture children. No changed production downstream consumer; no mandate to execute those assemblies |

All proposed test bodies, reflection adapters, selector patches and assertion
locations **remain binding required after Code**. Record full author C and landed
L, exact path/symbol/diff, snippet/patch SHA-256, producer and staged DLL hashes,
Class.Method, variant, reached marker and decisive assertion. The snippets below
are specified C# defects, not a claim of a successful compilation or execution.

### Delivery inventory

**No new or changed asynchronous delivery path.** R1 changes tests and test
classification only. Awaiting filesystem/Git calls does not create a queue or
recipient. No SessionMessageQueue, Hangfire, phone-home or session-input contract
is changed or credited. Consequently there is no new producer-to-recipient
queue test or delivery/recovery guard in R1. A future scope expansion into such a
path requires its real queue, busy/already-eligible recipient and crash/enqueue
handoff tests, ending in recipient evidence (matching complete UserPrompt for
session input), before it can be accepted.

The synchronous recovery observation in V-1 joins task/request coordinates,
common directory, source/aside/admin paths, receipt DirectoryIdentity and entry/
evidence manifests. The real receipt is written before rename; the fixture drops
its registration then throws; a fresh remover reads the disk receipt on retry.
Receipt presence or `BranchPresent` alone is insufficient: the independent
directory identity and bytes are the oracle. This is an exception-cut recovery
witness, not process-kill/power-loss or database-transaction durability proof.

Substitutes and limits: RemovalFixture models Git/lease/authority/registration but
uses real receipt writes, renames, hashes and native deletion. It proves directory
preservation at that boundary, not real Git admin recovery. RemovalHarness uses
real Git, isolated PostgreSQL and production removal/lease services; its earlier
verifier and sharing diagnostics are controlled and are not under test. The NF
reflection adapter enters the real helper after hashing; it proves the helper's
last content check, not that every production caller passes `hashed` or that all
concurrent writer schedules are safe. None is a delivery substitute or ack oracle.

### Proves it works now

All three new methods have one `[Test]`, no Arguments and **one execution each**.
Their names are frozen below. Case IDs, preparation checks and refusal diagnostics
do not count as additional TUnit results. Check fixture activation before the
call; after the call assert preservation before any expected reason/exception.

| ID / case / exact method | Recipe / variant / layer / classification | Expected oracle and reachability |
|---|---|---|
| V-1 / W-U9 / `LandingRemovalPolicyControlTests.C1737_MatchingContentReplacementIsNotDeleted` | `RemovalFixture`, same-content/different-directory, two invocations over one disk receipt; filesystem integration, portable Linux/Windows | Replacement identity and every original byte remain; rescue and retained evidence remain; record remains and modeled branch remains. Pending-unregistered path reaches CustodyOf; all manifests/hash guards would pass. CP-1 |
| V-2 / W-M7 / `WorktreeGuardedCleanupTests.C1737_AdvancedBranchSurvivesDeleteCas` | `RemovalHarness`, advance after successful current-OID read and before exact delete; real Git/DB Integration + existing Slow/process limiter | Independent real `show-ref --verify --hash` yields intervening OID T, and its real branch reflog still exists. All earlier checks see S; only expected-old CAS distinguishes late T. CP-2 |
| V-3 / W-A23 / `WorktreeNoFollowDeleteTests.C1737_PostHashWriteSurvivesFinalStat` | Private-boundary adapter, genuine hash-ending stat then different-length write to same inode; Integration + method NativeLinuxOnly | `File.Exists` and actual new bytes survive. Captured listed/hashed stats and fresh stat name the same node; HashAt completed before the write. Final SameContent refusal is reached with SameNode true. CP-3 |

#### BOUNDARY WIRING: W-U9

1. In PC-file use the existing private RemovalFixture with its default coherent
   publication request, evidence, modeled lease and `WorktreeRenames.System`.
   Add `.antiphon/checkpoint-note.md` with fixed bytes to the owned source and
   `IgnoredPaths`; use `RemoveAsync(gate: true)` so RecordingRetention makes a
   genuine retained copy. Keep `keep.txt` and this evidence file as the entire
   small file roster. No injected delete/rename delegate is needed.
2. Arm `AfterUnregister` once to throw IOException. The fixture sets Registered
   false before invoking it. Require first residue `worktree_removal_incomplete`,
   source absent, registration false and non-null disk receipt with nonempty
   identity/entry manifest and populated evidence pins. Read and retain the
   receipt's exact values and the approved bytes. This first cut occurs before
   the later CustodyOf, not after a delete.
3. Clear the hook. Move aside to `Path.Combine(f.Root, "rescued-original")`;
   **not** `aside + ".rescued"`, which also matches SetAsideTrees. Keep the original
   alive so inode reuse cannot be the identity experiment. Create a new directory
   at the exact recorded aside path, copying only those approved directories and
   regular files with identical bytes. No different-content sentinel, link,
   repository marker, unapproved name or edited receipt. Retained evidence stays
   at its original path and hash. Independently enumerate names/kinds and SHA-256
   the replacement against WorktreeApprovedEntry and EvidencePin; do not use
   `ContentSeal` as deletion authority.
4. Require replacement identity != receipt identity, rescue identity == receipt
   identity, all manifests/content equal, source still absent and Registered
   false. Emit/assert `reached:W-U9:pending-unregistered-equal-content` once.
   `RemoveAsync(gate: true)` constructs a fresh remover and reads the real record.
   There is no registered restore path and no later receipt-root-identity check
   inside the no-follow walker; its own SameNode compares its new observations.
5. First decisive assertion:
   `Directory.Exists(aside).ShouldBeTrue("preserved:W-U9:replacement-directory")`.
   Then independently check its identity is the captured replacement identity,
   all bytes in replacement/rescue/retained copy, and modeled branch. Last check
   nonclean + `set_aside_identity_mismatch` and unchanged retained receipt.
   PC-1 must remove the replacement and fail the existence assertion, not a reason.

#### BOUNDARY WIRING: W-M7

1. Use `RemovalHarness.CreateAsync()` unchanged; set `CleanFirst=true` to avoid
   the unrelated simulated sharing retry. It supplies published S, a persisted
   cleanup request/operation, genuine repository lease and already-valid authority.
   Keep the production cleanup service and every command/read real.
2. Install `h.OtherCommand`, not a replacement for the harness BeforeCommand.
   Intercept only repository == fixture.Repository, `args[0]=="update-ref"`,
   `-d` present and the exact fixture.SourceRef present. Do **not** require the
   expected-old operand in the match: that would disarm the witness under PC-2.
   Count this hook; a second hit is fixture failure. Capture AfterCommand for the
   successful source `show-ref --verify --hash` of S, so the schedule proves
   that the earlier OID guard actually passed. Do not synthesize its output.
3. At the delete hook assert source/aside absent, registration dropped and the
   last successful current-ref read was S. Create a separate
   `LandingGitFixture.FixtureGit(Path.Combine(f.Root,"home"), f.TaskId)` reader/
   writer with no hooks. Through that reader obtain S's tree, run real
   `commit-tree <tree> -p <S> -m c1737-late-child` and capture valid new OID T,
   then `update-ref --create-reflog <SourceRef> <T> <S>`. T is a real child of S
   and not on origin; this needs no checkout or dirty canonical tree. Assert
   independent read == T and `merge-base --is-ancestor T <remote target OID>`
   exits 1. Existing ambient destructive authorization stays satisfied.
4. Emit/assert `reached:W-M7:advanced-after-current-read` with hook count 1;
   return null so the originally intercepted **real** delete runs. No second
   source-OID check occurs after this hook. Keep --no-deref and all live gates.
5. Read with the independent reader after return. First require
   `actual.Succeeded.ShouldBeTrue("preserved:W-M7:advanced-ref-present")`, then
   `actual.Output.Trim().ShouldBe(T,"preserved:W-M7:advanced-ref-oid")`.
   Resolve `rev-parse --git-path logs/<SourceRef>` against the real repository;
   require the existing log and final new OID T. Only then assert
   `branch_delete_failed`, BranchDeleted=false, directory gone and one delete.
   PC-2 deletes the real advanced ref and its log; the independent existence
   assertion is red even if trace/diagnostics also change. Do not assert loss of
   the object itself: other object/recovery roots can retain the commit object.

#### BOUNDARY WIRING: W-A23

Use only a private test-local adapter in NF-file. Resolve exact nonpublic static
signatures on `typeof(WorktreeNoFollowDelete)`, including nested `LinuxStatx`:
`OpenLinuxDirectory(string)`, `Fd(SafeFileHandle)`,
`TryStat(int,string,int,out LinuxStatx)`,
`HashAt(SafeFileHandle,string,in LinuxStatx,out Nullable<LinuxStatx>)`,
`SameNode(in LinuxStatx,in LinuxStatx)`,
`SameContent(in LinuxStatx,in LinuxStatx)`, and
`UnlinkSameNode(SafeFileHandle,string,LinuxStatx,Nullable<LinuxStatx>)`.
Use MakeByRefType/Nullable<> when looking up in/out parameters; retain the
object[] out slot returned by Invoke (nullable with a value boxes as LinuxStatx).
Do not hand-marshal/fabricate a stat or copy its layout into a test struct.

Create one small regular file with fixed original bytes in an owned empty parent;
open its real parent descriptor via OpenLinuxDirectory. Get listed via TryStat
with the production AtSymlinkNoFollow constant (0x100 at this source). Invoke
HashAt; require nonempty SHA-256 equal to an independent hash of original bytes
and non-null ending stat. Change the file through FileMode.Open on the existing
inode, set a different length, write fixed new bytes and flush/close the writer.
Get a fresh real stat; require same node, different Size and SameContent=false
against the hash-ending stat. No sleeps, inotify, large file or racing thread.

Emit/assert `reached:W-A23:hashed-then-same-inode-write`; invoke UnlinkSameNode
with the **original listed and actual hash-ending stat**, not the fresh stat.
Capture and unwrap TargetInvocationException.InnerException. Check
`File.Exists(path).ShouldBeTrue("preserved:W-A23:post-hash-file")` and actual new
bytes first; only afterwards require ApprovedTreeRejectedException with reason
`set_aside_content_changed`. Unexpected reflection/native/setup failures are
fixture failures, never mutation kills. Dispose every acquired SafeFileHandle,
writer and any altered ambient approval in finally before fixture cleanup.

The earlier hash/manifest checks are **satisfied before** the new write and are
outside this legitimate helper entry; they are not disabled. SameNode is true;
size alone makes SameContent false without trusting timestamp resolution. PC-3
then reaches the real unlinkat and the existence assertion fails. No production
accessibility change, hook, authorization relaxation or exception swallowing is
permitted. If reflection cannot bind/reach this boundary, return `next: plan`.

### Guards the regression

| ID | Existing exact test / decisive assertion / layer |
|---|---|
| R-1 | PC-file `C1717_D4_RetryKeepsAReplacementDirectory`: foreign-only bytes and rescued original survive pending retry; retains the different-content regression, not independent U9 kill credit |
| R-2 | PC-file `C1717_B4_ForeignDirectoryIsNotRestored`: foreign file absent at original source, both trees' bytes and receipt retained; restore A11 remains a separate historical obligation, not PC-1 |
| R-3 | GC-file `C443_BranchDeleteUsesOldSha`: exact real command trace equals `update-ref --no-deref -d <SourceRef> <S>`; argv compatibility only, V-2 supplies preservation |
| R-4 | `TestClassificationGuardTests.Registry_matches_compiled_metadata` and `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`: no classification errors; `NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute`: real compiled policy/marker/hash report Success; `LiveLaneRunnerTests.WrapperOnlyPaidManifest`: real protected-input digest plus unchanged Required/Excluded sentinels, all 21 named harness assertions, 1 TUnit result |

No existing test body or assertion is weakened. The three new methods are also
the regression tests for their respective V-n/G-n. These are witness repairs to
currently guarded behavior: base-green is expected. Their required red is the
single compiling selected defect below, not a fabricated unmutated failure.

### Guard inventory

| Guard | Plan ref + safety-critical invariant | Distinct control |
|---|---|---|
| G-1 | R1 W-U9 / ledger 01: CustodyOf equality binds deletion to the receipt's actual directory identity, even when all content is identical | PC-1 |
| G-2 | R1 W-M7 / ledger 06: CompleteBranchAsync expected-old SHA must atomically protect a source ref advanced after its current-OID read | PC-2 |
| G-3 | R1 W-A23 / ledger 18: UnlinkSameNode must compare hash-ending content stat to fresh stat after a write on the same inode | PC-3 |

These are all three independently bypassable safety guards repaired by R1; none
is omitted as redundant. Inherited registration/lease/authority, manifests,
retained hashes, no-follow, SameNode, restore A11 and live authorization remain
intact prerequisites or preservation bindings owned by the parent ledger. R1
does not claim their individual mutation completion. R-2's separate restore
guard is outside the repaired-witness inventory and retains its historical A11
mapping; no new PC or repeated historical sweep is commissioned for it.

### Positive controls

All three controls and every named variant run; each has one variant/result.
Plan D-6 explicitly commissions an **additional author-side packed proof**.
Ordinary Review judges V/R evidence and pending controls before land. Formal
Mutation then repeats baseline/break/red/restore/green on published L. Author
proof never replaces that sourced pass. No execution has occurred in TestDesign.

| Control / selector | Break only | Exact method filter | First intended preservation red |
|---|---|---|---|
| PC-1 / `PC-1` | G-1: omit CustodyOf identity equality rejection only; keep RestoreCustody and unsealed/read/hash/listing checks | `/*/*/LandingRemovalPolicyControlTests/C1737_MatchingContentReplacementIsNotDeleted` | `preserved:W-U9:replacement-directory` true becomes false |
| PC-2 / `PC-2` | G-2: omit expected-old argument only from CompleteBranchAsync delete; keep --no-deref and current/live checks | `/*/*/WorktreeGuardedCleanupTests/C1737_AdvancedBranchSurvivesDeleteCas` | `preserved:W-M7:advanced-ref-present` true becomes false |
| PC-3 / `PC-3` | G-3: omit hashed-vs-now SameContent refusal only; keep TryStat, symlink and SameNode checks | `/*/*/WorktreeNoFollowDeleteTests/C1737_PostHashWriteSurvivesFinalStat` | `preserved:W-A23:post-hash-file` true becomes false |

#### MUTATION SELECTOR contract

Temporary pack only: append this helper in the same Git namespace to
GuardedWorktreeRemoval.cs. No selector/helper/production change is committed.
The local driver validates the environment against the same allowlist **before
launching** each test host. Absent/empty is off; one exact string is active.
Unknown, whitespace, comma-separated or multiple values refuse before execution.
Do not set/unset the selector inside a test or infer it from a test name.

```csharp
internal static class C1737R1Mutation
{
    internal static bool Is(string id)
    {
        var selected = System.Environment.GetEnvironmentVariable("ANTIPHON_C1737_R1_PC");
        if (selected is not (null or "" or "PC-1" or "PC-2" or "PC-3"))
            throw new System.InvalidOperationException("c1737-r1-invalid-selector");
        return string.Equals(selected, id, System.StringComparison.Ordinal);
    }
}
```

PC-1 replaces only the equality `if` in **CustodyOf**, not its identical-looking
RestoreCustody sibling:

```csharp
if (!C1737R1Mutation.Is("PC-1")
    && !string.Equals(identity, record.DirectoryIdentity, StringComparison.Ordinal))
    return "set_aside_identity_mismatch";
```

PC-2 replaces only the delete call inside the existing using gate:

```csharp
deleted = await git.RunAsync(source.RepositoryPath,
    C1737R1Mutation.Is("PC-2")
        ? new[] { "update-ref", "--no-deref", "-d", source.SourceFullRef }
        : new[] { "update-ref", "--no-deref", "-d", source.SourceFullRef, request.ExpectedSourceSha }, ct);
```

PC-3 replaces only the final content `if` in UnlinkSameNode:

```csharp
if (!C1737R1Mutation.Is("PC-3") && hashed is { } proof && !SameContent(proof, now))
    throw new ApprovedTreeRejectedException("set_aside_content_changed");
```

Snippet SHA-256 bindings (UTF-8, LF, exactly the contents inside each csharp fence,
including final LF):

| Snippet | SHA-256 |
|---|---|
| Helper | `1e261dee9e0aba597ddd31a95a717f38d33fb26d359e229c4b58923ee051e0b1` |
| PC-1 | `0f8b0a23b53774ff160a81b921ac4f06fee19131c001de8166f48c96adc5005f` |
| PC-2 | `6f08d2ecd172bc17ce436322b4cce8d5a70ecb09c7ad30734d18cac08ef7ca53` |
| PC-3 | `a658bc6d31c7f2bc45094a7ee9504725f44535d0c8f705e81784e0dfd5e76a7d` |

Actual applied patches and final C/L source bindings remain required after Code;
digesting a snippet does not prove its application or compilability against
changed source.

Author execution: commit/push clean tests and metadata first; preserve originals
and patch/digests in ignored task evidence. Build one logical pack in native
default output, `UseAppHost=false`. Run exactly one method selector-off then its
selector-on red, each as its own child/lease/TRX. PC-1 and PC-2 touch the same
production file, so run them sequentially, with only one active selector; no
compound bypass. Packet A is PC-1 off/red, packet B PC-2 off/red, packet C PC-3
off/red (at most three invocations per packet). Restore both production files
byte-for-byte, remove helper, refresh timestamps, build restored source once,
verify producer/staged binaries and run the three exact methods green (packet D).
Then run clean ordinary CP-1..3. Do not run checkpoint clean-source certificates
against the temporary pack. No source edit while a driver is in flight.

Each PC receipt retains reached marker, intended assertion red, exact result
identity, source/patch/binary hashes, selector, counts, timings and restoration.
Min=1 in every method leg; 3 off + 3 red + 3 restored = **9 observations**.
Build/fixture errors, zero/skipped cases, timeout or reason-only red are invalid.
A survivor returns the exact seam to Plan without another guard bypass.
Post-land Mutation uses the same method filters but its own fresh C/O/L binding,
allowed local inherited children, external evidence root, fresh phase builds and
restoration rules. No snapshot commit/push or assumed author-pack waiver.

### Out of scope

- Production changes and all delivery/queue/session work; R2/R3 salvage and R4
  switch/stderr witnesses; whole Unit/assembly/native suites. Changes to shared
  runtime or fixture code require a revised selection before implementation.
- Historical A11 and other completed waived kills, A24 obsolete seal, X-26
  diagnostic-only binding and the Plan's remaining closed/correction rows. Their
  historical outcomes are neither erased nor recounted as these three kills.
- A23's stat-to-unlink/coarse-clock same-size residual and caller wiring; this
  helper witness cannot establish an atomic content lease.
- Windows acceptance and U8/U10 are separately reserved below. No desktop run,
  S4a enablement, cleanup, deployment, tracker write or land-graph update.

#### Classification and file-overlap constraints

R1 adds **no test class**: retain the three Plan class/method identities. Change
the existing PC-file and NF-file class labels from Unit to Integration; both
exercise real filesystem/native deletion. Do not put Integration on a method
under Unit (TestLaneCategoryGuard rejects that), add method-level Slow, or move
all those tests to a new class. Existing GC Integration/Slow and
ParallelLimiter<ProcessSpawnLimit> stay. NF/PC acquire no spawned-child limiter
because these selected methods spawn no process. Reclassification changes lane
membership of their existing methods deliberately, not bodies; nightly/rc full
discovery remains responsible for both lanes. No whole-class execution is needed
to prove a metadata-only move; CP-3 checks compiled/source classification.

Add `[NativeLinuxOnly("Linux-only by construction: real statx, no-follow directory
descriptors and unlinkat are required; Windows has a separate native witness.")]`
to W-A23 only (one single-line literal in C#). The pinned attribute's reverse
coverage check requires an rc exclusion in `tests/test-execution-policy.json`:
suite `antiphon`, class `WorktreeNoFollowDeleteTests`, methods containing only
`C1737_PostHashWriteSurvivesFinalStat`, platform `windows`, owner `CARD-1737`,
reason beginning `Linux-only by construction:` with that same explanation.
It remains Required on Linux; do not add a broad NF class exclusion or change
existing skip obligations. Adding a new class is unnecessary, but **the new
marker still requires a policy edit**.

In the same Code commit update the policy row, its `policyHash` using
Get-NightlyPolicyHash from `scripts/lib/nightly-policy.ps1`, and
`$script:PinnedInputDigest` in `scripts/test-live-lane-run.ps1` using that script's
`-PrintInputDigest`. Do not copy these hashes from this earlier source, change
the digest algorithm, or weaken sentinels. R-4 selects their existing witnesses.
No live-lane historical full-census re-seeding or paid/native launch is required.
If any later slice introduces a class/marker or otherwise changes the policy,
the row/classification, policyHash and live-lane PinnedInputDigest refresh together
at its integrated source too; a class addition alone is not permission to exclude it.

| Owner / files | Overlap contract |
|---|---|
| R1: PC-file, GC-file, NF-file, helpers private in those files | R2 owns only `Application/C1708ReflogSalvageTests.cs`; test-body authoring is disjoint. R3 follows R2; N1 follows R1 because NF overlaps |
| R1 metadata: `tests/test-execution-policy.json`, `scripts/test-live-lane-run.ps1` | **Refinement of Plan D-5's provisional overlap map:** R2's U5R marker can need these same files. Serialize metadata edits/integration/pinning; do not describe complete R1/R2 Code branches as file-disjoint. R1 owns this metadata change first; R2 adds its rows/hashes against the integrated R1 source before its own ordinary certificates. If that ordering cannot be supplied, serialize the Code dispatches, not the test-design documents |
| Shared helpers/DI/schema/project files | No LandingGitFixture, LandingSafetyHarness, DesktopLegacyWorld, shared marker or project edit. A need to edit them returns to Plan/TestDesign for caller closure and sequencing; no silent scope expansion |

Do not edit an R1 branch under Review to accommodate R2. Landing owns rebasing;
no target merge/rebase/reset/force-push by the delegates. No concurrency cap is
inferred from this source map; caller refreshes pipeline and runner eligibility.

#### Windows obligations (three ledger rows, separate commission)

| Ledger obligation | Reservation, not R1 acceptance |
|---|---|
| 03 native receipt/walker identity, per-entry/listing and restore | N-ID reruns W-U9 on real Windows; N-RESTORE uses C1717_B4_ForeignDirectoryIsNotRestored; N1 authors the Plan's two Windows per-entry methods alongside existing open-writer witness. Linux receipt/stat tests cannot close these native checks |
| 05 PC-68/N-3 alias interaction | Native Windows_aliases_and_junctions_are_refused, all three arguments; require a real 8.3 alias for short-name. A synthetic fallback/skip earns no native credit; classify masks at the actual reached guard |
| 25 PC-67/N-2 move/order/length | Native Long_path_never_leaves_an_unregistered_orphan, Long_path_untracked_file_holds_the_tree and Long_path_ignored_tree_is_held_not_partial. Keep path limit; its early refusal cannot earn the old unregister-first kill |

These are reservations by reference to the inspected Plan, not new CP rows or
native budgets. N1 must inspect its own bodies/fixtures before freezing execution.
Nothing runs on the desktop under this commission.

#### Operator decisions (separate from R1)

- **U8 / O-1:** authorization across an already-started native sharing/open retry
  needs a product semantics choice; currently no inner retry live-read exists to
  mutate. If required, separately design both retry loops and partial-byte custody.
- **U10 / O-2:** name the unspecified concrete guard/control or retire the placeholder.

Neither decision blocks R1 or permits counting another control. This design uses
the Plan's 150-210 minute R1 Code reservation; a hard 90-minute cap instead needs
separately integrated packets commissioned by the caller, not truncated proof.

### Checkpoints

This is the **closed ordinary R1 scope: 3 new + 3 retained + 4 metadata/policy
methods = 10 TUnit executions**, with zero failures/skips on Linux. One run,
one isolated build, exactly three serial rows; CP-2/3 reuse CP-1 at the same
committed SHA and properties. No seven-row serial run. Each row has one exact
filter and one source-bound isolated build binding as the owner schema permits.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | R1 | tests/Antiphon.Tests -> bin-c1737-r1/ | r1-custody | /*/Antiphon.Tests.Infrastructure/LandingRemovalPolicyControlTests/(C1737_MatchingContentReplacementIsNotDeleted*)\|(C1717_D4_RetryKeepsAReplacementDirectory*)\|(C1717_B4_ForeignDirectoryIsNotRestored*) | V-1,R-1,R-2 | exactly these 3 methods, 3 passed, 0 failed/skipped | 3 | 14 | true |
| CP-2 | R1 | CP-1 | r1-branch-cas | /*/Antiphon.Tests.Infrastructure/WorktreeGuardedCleanupTests/(C1737_AdvancedBranchSurvivesDeleteCas*)\|(C443_BranchDeleteUsesOldSha*) | V-2,R-3 | exactly these 2 methods, 2 passed, 0 failed/skipped | 2 | 3 | true |
| CP-3 | R1 | CP-1 | r1-stat-classification | /*/*/(WorktreeNoFollowDeleteTests*)\|(TestClassificationGuardTests*)\|(TestLaneCategoryGuardTests*)\|(NativeLinuxPolicyWitnessTests*)\|(LiveLaneRunnerTests*)/(C1737_PostHashWriteSurvivesFinalStat*)\|(Registry_matches_compiled_metadata*)\|(every_test_class_is_tagged_unit_xor_integration*)\|(Policy_rows_require_pinned_attribute*)\|(WrapperOnlyPaidManifest*) | V-3,R-4 | exactly the 5 named class/method pairs in the roster below, 5 passed, 0 failed/skipped | 5 | 4 | true |

CP-3 exact pairs: WorktreeNoFollowDeleteTests.C1737_PostHashWriteSurvivesFinalStat;
Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata;
Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration;
Antiphon.TestSupport.NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute;
Antiphon.Tests.Scripts.LiveLaneRunnerTests.WrapperOnlyPaidManifest. The NF namespace
is Antiphon.Tests.Infrastructure. This five-class Cartesian filter intentionally
selects only these five methods, not five whole classes.
The ScriptHarness method has 21 internal checks, still **Min contributes 1**.
Re-audit all OR suffixes/cross-class homonyms after Code and compare actual fresh
TRX Class.Method/arguments to this roster. A widening requires a revised filter;
Min alone cannot detect it. No proposed C1737 method existed at inspection.

#### PERFORMANCE MANIFEST

Use the checkpoint tool to import this document, then pin the ignored YAML;
execute the **manifest positional argument**, since `run --plan` would reimport
and lose these pins. Tool bootstrap, only if unavailable, is a separately reported
build-slot-gated native-default-output build (3 minute planning allowance).

```text
dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false -- import --plan docs/superpowers/testdesign/2026-11-06-card-1737-r1-testdesign.md --out .antiphon/c1737-r1-checkpoints.yaml
```

Pins: `build.slotWaitMinutes: 10`; `build.properties.UseAppHost: false`;
`timeouts.rowMinutes: 28` (build ceiling); per-row `timeoutMinutes` CP-1=3,
CP-2=3, CP-3=4; all serial. `TEMP=/tmp` on the Linux child, selector unset.
Set each row's `ANTIPHON_TEST_PHASE_GROUP_ID` to its actual CP ID and enable the
existing CARD-1514 recorder with run/attempt/suite/root identity. Other build/test
drivers use `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <label>
-SlotWaitMinutes 10 -- <command>`. The no-build checkpoint launcher does not hold
an extra loop-wide slot: its child build and rows acquire their own leases.

```text
dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false -- run .antiphon/c1737-r1-checkpoints.yaml --rows CP-1,CP-2,CP-3 --expected-source-sha <C> --total-timeout 38m --hard-wall-timeout 78m --total-timeout-override-reason "CARD-1737 R1: Debug 5ae98d31 phase budget, 28 build + 3/3/4 rows; admission separate"
```

38 minutes charged execution = 28+3+3+4. Hard wall 78 = 38 + four acquisitions
x10; caps are not expected runtimes. The override explicitly replaces the default
derived 52 minutes (2x21+10), and the imported 15min/3x row floor is not accepted
as measured policy. Wait to terminal; exit 75 is still running, exit 4 admission
timeout is not-run, never permission for NoSlot/unleased retry. Join owned runs
before returning; use <=60s wait observations. No tracked-source edits in flight.

| Phase | Provenance and bound |
|---|---|
| Admission | Debug n=39, mean 46.5s, p95 255s, max 405s; 10min finite per acquisition, separate from execution |
| Isolated/packed build | Debug n=11 successful cold builds p50 431.6s, empirical p95/max 817.1s under varying load; 28min cap rounds 2x that maximum. 12min conservative estimate in CP-1, plus 2min host/body/teardown |
| Restored native build | B2/C2 comparators 144.2/119.4s, tiny sample; estimate 3min, provisional 5min cap, CARD-1737. Direct builds use native bin/obj, no -o/OutputPath |
| Ordinary hosts | CP-1 estimate 2min/cap 3, CP-2 3/cap 3, CP-3 4/cap 4. Estimates include startup/teardown allowance; caps are provisional ceiling allocations, not new measured p95. Debug short-host max 42-85s, loaded startup up to 67s; new rosters measured in their first required run |
| Exact PC method host | Estimate 1min; cap 3min, provisional from 2x nearby 85s loaded maximum. Independent of 10min admission |
| New body budgets | W-U9 10s (two passes/retention), W-M7 60s (Git/DB multi-pass), W-A23 5s (one small file); provisional CARD-1737, collect first off/red/green samples, no widening/retry to hide failures |
| Setup/isolation | CP-2 uses existing shared TestDbFixture container/model/migration template once per host (start/readiness <=60s, migration/seed <=30s expected) and cloned database per case. LandingGitFixture reuses its immutable Git seed, private writable trees/refs. CP-1/3 need no DB; SharedStoreWarmup follows calls, not Integration label. No new web/AppHost/runner/host startup |
| Calls/cleanup | Bound new fixture Git calls with cancellation at 10s; DB/barriers 5s where exposed; child exit/drain 10s, teardown 15s provisional. No new child in U9/A23. Existing ScriptHarness uses its 30s execution/10s cleanup and GC retains its assembly-local limiter. Preserve stricter subject budgets |

Per-method limits are planned test budgets; Code binds the supported timeout/
cancellation mechanism and does not change shared fixture deadlines. Record
filesystem/hash/stat/copy, DB and Git process spans with existing TestTiming,
plus setup/readiness/template/clone/body/teardown. Missing subphase measurements
stay **unknown**; HTTP/web is not applicable, not a measured zero. Retain wall,
admission per acquisition, execution, nested spans and unattributed remainder,
run/source/build/host/OS/load/category/roster under ignored
`.antiphon/checkpoints/<run-id>/`; packed records use a separate ignored task root.
Code/Review run `scripts/check-evidence-diff.ps1` over assigned base..HEAD.

The measured Debug explains the packing, not an exemption from R1 performance
policy. Normal 21min ordinary estimate plus mean admission ~3.1min stays below
30. Loaded build/queue bursts can exceed 30: preserve run/source/load/admission
evidence and request the required performance Debug disposition; do not repeat
the same expensive run to diagnose it. No full suite, extra measurement battery,
project split or shared-fixture refactor is commissioned. First required runs
provide bounded calibration under CARD-1737/1404/1531.

### Cost

All following numbers are **estimated**, informed by the cited measurements;
this TestDesign measured no execution. Costs count minutes, never assertion or
TUnit counts. Admission is separate; the caps above are not planning floors.

| Work | Numeric floor / reservation |
|---|---|
| Ordinary V/R (Code) | **21min = CP-1 14 + CP-2 3 + CP-3 4**. Includes 12min isolated build + 9min host/setup/body/teardown; no build double count |
| Additional commissioned author proof | **25.5min = 12 packed build + 3 restored build + 9 exact-method invocations x1 + 3 x0.5 patch/binding/restoration accounting**. Filters are precisely PC-1/2/3 above, each off/red/restored |
| Tool setup if needed | **3min**, one gated native bootstrap; 0 if a proven matching tool is already available. Not repeated per row |
| Post-land Mutation baseline | **15min = 12 native baseline build + 3 exact-method baseline greens x1** |
| Post-land PC-1 cycle | **16.5min = 7 red build + 1 red method + 0.5 restoration/binding + 7 restored build + 1 green method**, exact PC-1 filter |
| Post-land PC-2 cycle | **16.5min**, same components, exact PC-2 filter |
| Post-land PC-3 cycle | **16.5min**, same components, exact PC-3 filter |

**Mutation floor = 64.5min = 15 + 3x16.5.** Its seven build estimates are
conservative reservations despite native incremental outputs; no unverified
author-pack reuse is credited to SourceLanding. A 14min build scenario instead
of 12/7 raises that floor to **108.5min** (7x14 + 9 methods + 1.5 accounting).
Run one PC cycle per bounded packet; each red/green build and method re-leases.
Fresh-build/restore evidence is required for each PC. Unknown discovery/repairs
are additional, not silently funded by deleting a control.

Combined verification reservation **114min = 3 setup + 21 ordinary V/R + 25.5
author proof + 64.5 post-land Mutation** (111min with no bootstrap). Ordinary
Review of the same scope adds 21min, giving **135min** before authoring/admission.
Code alone reserves 49.5min verification/setup plus 90-130min authoring from the
Plan, approximately 150-210min including routine admission/analysis. It does not
promise completion in a 90min task or include the independent Mutation stage.

Acquisitions: ordinary 4 (1 build + 3 rows), author proof 11 (2 builds + 9 hosts),
Mutation 16 (7 builds + 9 hosts); optional bootstrap 1. Ordinary wait ceiling
40min; author 110; Mutation 160; bootstrap 10. At Debug's historical 46.5s mean,
their admission scenarios are 3.1, 8.525, 12.4 and 0.775min respectively,
**24.8min total with bootstrap**; this is a load scenario, not a measured forecast.
No loop holds a lease and a timeout is reported not-run.

Savings: one ordinary build instead of three saves **2x12=24min**; packing author
selectors once plus one restored build instead of three separate pack/restored
pairs saves **2x(12+3)=30min**. Combining four tiny metadata/policy methods with
the native row avoids a fourth row/host and its admission (at least 1min host
plus the 0.775min historical mean wait scenario). No full-suite percentage or
post-land packed saving is invented. These choices retain every named method
and the three independent preservation assertions.

### Binding and handoff

Before handoff: **guards=3, mapped=3, missing=0, duplicate PC maps=0**.
**All three PCs must run**, author off/red/restored plus formal post-land cycles;
executed here=0, pending=3. Numeric floors: Code ordinary **21min**, additional
author proof **25.5min**, Mutation **64.5min**, optional setup **3min**.

Code commits/pushes test-only work and required metadata before long verification;
final tracked diff has no production selector or safety change. It binds the
proposed payloads to C, supplies all nine author observations and all three clean
CP receipts with exact roster, preserves unedited CHECKPOINT/provenance lines,
and returns **next: review** with all three formal PCs pending. Review judges the
witnesses and ordinary source-bound evidence before land; no PC-clean/native-clean
claim is made by this document. Under D-2 an absent required run is incomplete
verification, not an advisory green. Unverifiable seam -> Plan; only a real human
choice -> Decide. Authorized R1 witness repairs need no further permission.
