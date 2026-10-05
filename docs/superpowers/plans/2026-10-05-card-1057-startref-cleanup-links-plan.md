# CARD-1057: keep StartRefGit cleanup inside its owned directory

Plan with folded verification design, 2026-10-05. Inspected source:
`82f39bca49efac3633d68839eae9af1272fac2df`. One **55-minute implementation
slice**, plus observed queue/build-slot waits. Next stage: **Code**. The brief
commissions the PC matrix and checkpoint design here; no separate TestDesign
dispatch or product decision is needed.

Replace the recursive attribute enumeration in `StartRefGit.DeleteDirectory`
with a shallow, attribute-classified walk. Treat reparse points as leaves:
remove the link itself without enumerating or normalizing its target. Preserve
ordinary read-only Git-object cleanup and the helper's best-effort contract.

## Ground truth

| Card assumption | What the inspected code does | Plan consequence |
|---|---|---|
| Cleanup changes attributes outside its root through a junction. | `tests/Antiphon.Tests/TestHelpers/StartRefGit.cs:DeleteDirectory` calls `Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)` and sets every returned file to `FileAttributes.Normal` before recursive deletion. There is no reparse-point check. | The premise matches the source. Remove the recursive enumeration; checking an enumerated file after descent is too late. |
| Both child and root junctions matter. | The only entry guard is `Directory.Exists(path)`, which admits a live directory junction. The card records a Windows probe: child-junction cleanup changed outside attributes then left residue; root-junction cleanup removed the link after changing outside attributes. | Separate native Windows tests for these two boundaries. Planning did not rerun that historical probe and claims no runtime reproduction. |
| The helper is shared by fixture teardown. | `EvidenceGitFixture.DisposeAsync` already delegates to this helper and then asserts root absence. `WorktreeRetirementRaceTests` uses it directly and in `RaceWorld.DisposeAsync`; start-ref tests also import it statically. | Change the helper, not those call sites. Preserve the fixture's root-absence assertion and test representative consumers. |
| Existing tests protect the link boundary. | `WorktreeRetirementRaceTests.C694_DeleteDirectory_RemovesReadOnlyFile` covers a real nested directory. `EvidenceGitFixtureTests.C1051_DisposeAsync_RemovesReadOnlyGitObject` covers a real loose Git object and repeated disposal. Neither creates links. | Keep both exact methods as regressions and add filesystem tests with outside sentinels. A helper that merely stops deleting must fail. |
| Windows symlink creation may be unavailable. | `LandRecoveryIgnoredObstructionTests.C970_IgnoredDirectorySymlinkParentOfReviewedChildRefused` catches Windows creation denial and calls `Skip.Test` with a reason. `DirectoryLink.TryCreate` uses `mklink /J` on Windows and a symlink elsewhere. | Require real Windows junctions without a skip allowance. Implement reasoned Windows symlink skips, but execute the symlink acceptance row on Linux. |
| An existing link factory can supply the decisive proof unchanged. | `DirectoryLink.TryCreate` converts creation errors into null, and synchronously drains child output before its timed wait. `Checkpoints/DirectoryLinkHelper` tries symlinks first and has an unbounded junction wait. | Use a small bounded junction factory local to the new test class. Do not silently substitute a symlink or broaden shared-helper repairs. |
| Any platform pass establishes acceptance. | Windows read-only deletion and junction behavior are the reported failures; Linux permits privilege-free symlink fixtures. Live defaults revision 2 selects a Linux runner, and the inventory includes an available, eligible Windows runner. | Require the two named lanes below. Do not infer Windows qualification from a Linux result. Re-read live placement before dispatch. |

## Decisions

- **D-1: Classify before descending or changing attributes.** Inspect the root
  first. If it is a directory reparse point, unlink it with nonrecursive
  `Directory.Delete` and return. For a real root, enumerate immediate entries
  only, read each entry's attributes, and handle the reparse bit before the
  directory/file split. Recursively process only ordinary directories. Clear
  attributes on ordinary files and delete them; remove empty real directories
  nonrecursively after processing their entries. Reject `AllDirectories`, a
  lexical path-prefix check, and filtering only returned filenames: each can
  enter the outside tree before deciding it is unsafe.
- **D-2: Delete links without target metadata writes.** For a directory link
  use nonrecursive `Directory.Delete`; for a file link use `File.Delete`.
  Do not call `ResolveLinkTarget`, set attributes on a link, or enumerate its
  target in the implementation. Reject simply skipping links: it leaves the
  owned root behind and breaks strict fixture disposal. Classify file links
  too, so replacing the file enumeration does not leave a metadata escape.
- **D-3: Preserve existing cleanup behavior.** Retain the public signature,
  missing-directory no-op and best-effort catch. Preserve ordinary-file
  `FileAttributes.Normal` normalization, then deletion. Keep all actual
  traversal inside the best-effort boundary. Do not remove the stronger
  `EvidenceGitFixture.DisposeAsync` postcondition. Reject retries, permission
  changes, a filesystem abstraction and production cleanup refactoring.
- **D-4: Prove outside state before cleanup success.** Every link test owns a
  fresh scratch parent containing sibling `owned` and `outside` trees. Capture
  outside sentinel bytes and full attributes after explicitly setting and
  confirming ReadOnly. After the call, assert outside existence, exact
  attributes and bytes before asserting the owned root/link is gone. An
  exception swallowed by the helper must not hide an escaped attribute walk.
- **D-5: Use real native fixtures and independent teardown.** Windows tests
  create junctions explicitly with `cmd.exe /d /c mklink /J`, bounded async
  output drains and process wait; on timeout kill/join the exact child before
  reporting setup failure. Use the assembly-local
  `ParallelLimiter<ProcessSpawnLimit>` and `Category("Integration")`.
  Required junction creation failure is a failure, never a skip. Test teardown
  unlinks known links first, then clears/deletes only known fixture-owned real
  files/directories. It must not use the helper under test or perform a second
  recursive walk while a link remains.
- **D-6: Route by capability, not fleet location.** Read
  `GET /api/runner-defaults` and `GET /api/session-runners` again before each
  dispatch. CP-1 through CP-3 and the Windows PCs require `-Platform Windows`;
  CP-4 requires `-Platform Linux` for its privilege-free symlink proof. Omit
  `-Runner`; omit platform for planning/other OS-independent work, and use
  `-Platform Any` only to remove an inherited constraint. No host, address or
  checkout location is embedded in execution instructions.
- **D-7: One slice, bounded verification.** All implementation and tests ship
  together at one committed SHA. The caller can commission each lane serially
  against that SHA; a delegate does not sub-delegate or reach into another
  host. If only one lane is complete, hand back its evidence with `next: code`
  naming the remaining lane. Ordinary Review starts after both lanes complete.
  Reject whole-Unit, namespace, assembly and full RaceWorld runs. The six PCs
  below are post-land work, not extra pre-land build loops.

## Implementation slice

### S1: safe cleanup traversal and native regression tests (55 minutes)

| File | Change | Verification |
|---|---|---|
| `tests/Antiphon.Tests/TestHelpers/StartRefGit.cs` | Replace only `DeleteDirectory`'s traversal/deletion implementation as D-1 through D-3 describe. A private recursive core is acceptable; no new public API or shared state. | V-1 through V-5; R-1/R-2; all PCs. |
| `tests/Antiphon.Tests/Infrastructure/StartRefGitDirectoryCleanupTests.cs` (new) | Add five nonparameterized methods named below, a local bounded junction factory, and link-aware independent fixture teardown. Use namespace `Antiphon.Tests.Infrastructure`. | Windows CP-1; Linux CP-4. |

Read-only references are the existing tests/fixtures listed in Inspection.
No caller, workflow, generated card file, production source, dependency or
test-policy file needs changing. Existing helper regression assertions remain.

Commit and push S1 before checkpoint builds. Keep tracked source unchanged
through both lanes. A genuine repair is another commit/push followed by reruns
of affected rows; qualify the final candidate SHA before Review. Tests planned
here must assert outcomes, not source text or the presence of a reparse check.

## Verification design

### Inspection

Bodies read and their boundaries:

| Inspected source | Boundary covered |
|---|---|
| `tests/Antiphon.Tests/TestHelpers/StartRefGit.cs` | Unsafe attribute walk, existing missing-root/catch contract -> V-1 through V-5, R-1/R-2. |
| `tests/Antiphon.Tests/Scripts/EvidenceGitFixture.cs` and `EvidenceGitFixtureTests.cs` | Real locally written Git objects, assertion-bearing disposal, second disposal -> R-2. |
| `tests/Antiphon.Tests/Application/WorktreeRetirementRaceTests.cs` (`C694_DeleteDirectory_RemovesReadOnlyFile`, cleanup call sites) | Ordinary nested read-only file -> R-1, PC-6; other RaceWorld behavior excluded. |
| `tests/Antiphon.Tests/TestHelpers/DirectoryLink.cs`; `tests/Antiphon.Tests/Checkpoints/DirectoryLinkHelper.cs` | Existing junction/symlink creation and link-only disposal conventions -> native fixture setup, D-5. |
| `tests/Antiphon.Tests/Infrastructure/LandRecoveryIgnoredObstructionTests.cs` | Windows creation-denial skip and independent unlink convention -> V-3/V-4/V-5. |
| `tests/Antiphon.Tests/Infrastructure/WorktreeManagerStartRefLocalOnlyTests.cs` | Additional helper consumers are Git fixtures, not a different link policy; no network/dispatch regression run needed. |
| `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`, `ManifestValidator.cs`, `docs/testing-and-build.md` | Literal row schema, build reuse, OS is dispatch/preflight rather than a manifest column -> checkpoint execution. |

### Delivery inventory

No application asynchronous delivery path changes. The only new child is the
test-owned junction creation command. Its exit, drained output and observed
reparse point establish fixture setup; no queue, session, transcript or live
provider is involved.

### Proves it works now

All five new methods are in `StartRefGitDirectoryCleanupTests`. Use exactly
these names so checkpoint and PC selection remain narrow.

| ID | Exact test | Required observation |
|---|---|---|
| V-1 | `StartRefGitDirectoryCleanupTests.C1057_ChildJunction_PreservesOutsideAndRemovesRoot` | Windows: a junction under an ordinary nested child points at sibling outside. Outside has top-level and nested read-only sentinels; owned also has a regular nested read-only file. Capture and assert outside attributes/bytes unchanged, then owned root absent. Labels: `c1057-child-outside-exists`, `c1057-child-outside-attributes`, `c1057-child-outside-bytes`, `c1057-child-root-removed`. |
| V-2 | `StartRefGitDirectoryCleanupTests.C1057_RootJunction_PreservesOutsideAndRemovesLink` | Windows: the argument itself is a junction to outside. Both outside sentinels and their attributes/bytes survive; only the link disappears. Labels: `c1057-root-outside-exists`, `c1057-root-outside-attributes`, `c1057-root-outside-bytes`, `c1057-root-link-removed`. |
| V-3 | `StartRefGitDirectoryCleanupTests.C1057_ChildDirectorySymlink_PreservesOutsideAndRemovesRoot` | Same child-boundary oracle with a real directory symlink. Labels: `c1057-dirlink-outside-exists`, `c1057-dirlink-outside-attributes`, `c1057-dirlink-outside-bytes`, `c1057-dirlink-root-removed`. |
| V-4 | `StartRefGitDirectoryCleanupTests.C1057_RootDirectorySymlink_PreservesOutsideAndRemovesLink` | Same root-boundary oracle with a real directory symlink. Labels: `c1057-rootlink-outside-exists`, `c1057-rootlink-outside-attributes`, `c1057-rootlink-outside-bytes`, `c1057-rootlink-removed`. |
| V-5 | `StartRefGitDirectoryCleanupTests.C1057_FileSymlink_PreservesOutsideAndRemovesRoot` | An ordinary owned root contains a file symlink to an outside read-only sentinel, plus a regular owned file. Outside file exists with exactly captured attributes and bytes; owned root disappears. Labels: `c1057-filelink-outside-exists`, `c1057-filelink-outside-attributes`, `c1057-filelink-outside-bytes`, `c1057-filelink-root-removed`. |

Assert every fixture link has `FileAttributes.ReparsePoint` before invoking the
helper and every outside sentinel has ReadOnly. Inspect link absence through
the real scratch parent's immediate entries, as well as the appropriate
existence check; a dangling link must not satisfy the unlink assertion.
Do not inspect the outside tree through its link for postconditions.

For V-1/V-2 use a Windows-only runtime skip on other operating systems with a
clear reason; their acceptance checkpoint is Windows and permits no skip.
For V-3/V-4/V-5 use the actual .NET symlink APIs. Catch only creation-time
`IOException`/`UnauthorizedAccessException` on Windows and call `Skip.Test`
with the operation, exception and privilege-denial reason, matching the existing
convention. Do not catch an operation/assertion failure as a skip. Their required
Linux row must execute all three, so Windows privilege denial cannot silently
remove this coverage. Windows symlink execution is not an additional required
row; do not run it merely to create a skip receipt.

The tests' `finally` unlinks all surviving known links nonrecursively before
resetting fixture-owned sentinels and deleting real fixture roots. Capture
assertion results before this repair; teardown must never make a mutant appear
green. Join the junction child before removing its scratch directory. If a
safe unlink fails, preserve the scratch path and surface the teardown error
rather than recursively traversing it.

### Guards the regression

| ID | Exact existing method | Decisive assertion |
|---|---|---|
| R-1 | `WorktreeRetirementRaceTests.C694_DeleteDirectory_RemovesReadOnlyFile` | Root containing an ordinary nested read-only file is absent after cleanup. Windows is decisive for the attribute-normalization regression. |
| R-2 | `EvidenceGitFixtureTests.C1051_DisposeAsync_RemovesReadOnlyGitObject` | Real Git object's ReadOnly bit is seeded; actual fixture disposal does not throw, root is absent, and second disposal still succeeds. Preserve `c1051-dispose-read-only` and `c1051-missing-root-tolerated`. |

These two methods bound the shared-helper regression surface. The earlier
CARD-1051 workflow tests exercise the same fixture disposal; repeating the
whole workflow or deletion-policy suites adds no distinct cleanup behavior.

### Guard inventory

| Guard | Invariant / decision | Positive control |
|---|---|---|
| G-1 | Root directory link is rejected from the attribute walk before enumeration (D-1). | PC-1 |
| G-2 | Child directory link is rejected from the attribute walk before descent (D-1). | PC-2 |
| G-3 | File link never causes target attribute normalization (D-2). | PC-3 |
| G-4 | Root directory link is actually unlinked (D-2). | PC-4 |
| G-5 | Child directory link is actually unlinked so the ordinary root can be removed (D-2). | PC-5 |
| G-6 | Ordinary owned read-only files remain deletable (D-3). | PC-6 |

Guards=6, mapped=6, missing=0, duplicate PC maps=0. The root and child
classification/unlink paths are separately bypassable. Missing-root tolerance
and best-effort error suppression are retained, not newly introduced guards;
R-2 and source inspection protect that unchanged contract.

### Positive controls

Each mutation changes only `StartRefGit.DeleteDirectory` or its private core,
compiles, and leaves the test/fixture intact. Unsafe walks below are temporary
mutants over test-owned sentinels, never the intended implementation.

| PC | Compiling mutation | Exact detecting filter | Expected red and lane |
|---|---|---|---|
| PC-1 | In the root-directory-reparse branch, insert the former unguarded `EnumerateFiles(path, "*", SearchOption.AllDirectories)` / `SetAttributes(..., Normal)` walk before the existing unlink/return. | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/C1057_RootJunction_PreservesOutsideAndRemovesLink` | Windows: `c1057-root-outside-attributes` fails; outside bytes/files remain, proving an escaped metadata write rather than relying on deletion failure. |
| PC-2 | Insert that unguarded attribute walk over the child directory-reparse entry immediately before its unlink. Leave the root guard and unlink in place. | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/C1057_ChildJunction_PreservesOutsideAndRemovesRoot` | Windows: `c1057-child-outside-attributes` fails. The owned-root check occurs later, so a cleanup error cannot hide the escaped write. |
| PC-3 | In the file-reparse branch, resolve its final target with `File.ResolveLinkTarget(entry, true)` and set that target's attributes to Normal before the unchanged link deletion. | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/C1057_FileSymlink_PreservesOutsideAndRemovesRoot` | Linux: `c1057-filelink-outside-attributes` fails. This deliberate target-following mutant avoids assuming whether a platform's SetAttributes-on-link itself follows the target. |
| PC-4 | In the root-directory-reparse branch, return without performing its unlink. Preserve the no-enumeration behavior. | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/C1057_RootJunction_PreservesOutsideAndRemovesLink` | Windows: outside preservation passes, then `c1057-root-link-removed` fails. |
| PC-5 | In the child-directory-reparse branch, continue without unlinking it. Leave nonrecursive real-root removal unchanged. | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/C1057_ChildJunction_PreservesOutsideAndRemovesRoot` | Windows: outside preservation passes, then `c1057-child-root-removed` fails because the link keeps the root nonempty. |
| PC-6 | Remove ordinary-file attribute normalization but retain ordinary-file deletion and all reparse handling. | `/*/Antiphon.Tests.Application/WorktreeRetirementRaceTests/C694_DeleteDirectory_RemovesReadOnlyFile` | Windows: the existing `Directory.Exists(root).ShouldBeFalse()` assertion fails on the retained read-only file. |

Code runs only ordinary V/R. Independent Review precedes land. After confirmed
land, the caller commissions SourceLanding Mutation in the required lanes.
Run each exact method green, apply its mutant, rebuild/run red at the named
assertion, restore exact source bytes and rebuild/run green. All six controls
share the implementation file, so they run sequentially, never batched. V-3
and V-4 are symlink variants of G-2/G-1; their ordinary Linux execution adds
type coverage without inventing another independently bypassable guard.

Every phase needs a fresh TRX and the exact nonzero executed roster. Skips,
fixture failures, build errors and zero tests are not a PC red. Use fresh
alternate builds or refresh restored timestamps so green cannot reuse mutant
binaries. Mutation runs only local inherited children, makes no commits and
keeps full PC/restoration evidence in its assigned external root. Do not send
a SourceLanding snapshot to another executor; the caller commissions each
required platform separately with its own provenance.

### Out of scope

- Hostile concurrent replacement of an ordinary directory by a link between
  attribute read and use, linked ancestor components, hard links, dangling
  root links, mount/ACL recovery and arbitrary reparse types. This is cleanup
  of quiescent test-owned fixtures, not a race-proof production sandbox. Do
  not claim handle-based containment from an attribute check.
- Changes to production worktree deletion, source-landing cleanup, evidence
  retention, Git alternates, fixture callers or unrelated link factories.
- Database, live provider/session, whole-Unit/namespace/assembly, workflow and
  broad landing verification. The selected tests need filesystem and local
  Git only; they do not boot real Program or contact the production runner.

### Execution and evidence

One candidate source SHA and two local lane executions: Windows selects
CP-1,CP-2,CP-3; Linux selects CP-4. The checkpoint manifest has no platform
column; enforce that selection in the dispatch and preflight. Do not run the
entire table on one OS and count skipped tests as success. Each row is serial.
Build reuse is only within the Windows lane; Linux creates its own output.

Each lane gets one explicitly authorized leased tool bootstrap outside the
test rows. From that lane's assigned candidate checkout, after S1 is committed:

```powershell
$plan = 'docs/superpowers/plans/2026-10-05-card-1057-startref-cleanup-links-plan.md'
$sha = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1057-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1057-tool/ --nologo
$tool = 'tools/Antiphon.Checkpoints/bin-c1057-tool/net9.0/Antiphon.Checkpoints.dll'
dotnet $tool coverage --plan $plan
```

On Windows, assert `$IsWindows`, then:

```powershell
dotnet $tool run --plan $plan --rows CP-1,CP-2,CP-3 --expected-source-sha $sha --max-wait 50s
```

On Linux, assert `$IsLinux`, then:

```powershell
dotnet $tool run --plan $plan --rows CP-4 --expected-source-sha $sha --max-wait 50s
```

This built-DLL invocation is the no-implicit-build equivalent of the repository
checkpoint-tool `dotnet run ... -- run --plan` entry point. The tool takes each
row's build/test slot itself; do not wrap it in another slot. Every other build
or TUnit driver goes through `scripts/build-slot.ps1`. Use `dotnet run
--project tests/Antiphon.Tests`, never `dotnet test`, for direct PC drivers.
After exit 75, continue `dotnet $tool wait --run <returned-id> --max-wait 50s`
until a terminal result. Own and await all children before settlement. Slot
timeout means not run; never retry unleased or use `-NoSlot`.

Keep complete coverage-lint output and unedited CHECKPOINT lines in the stored
report, including actual SHA, lane, counts, slot fields and source/build
provenance. Reconcile static findings; static coverage is not PC execution.
Validate each lane's report with `scripts/validate-checkpoint-receipt.ps1`,
the exact candidate SHA and that lane's row list. Both reports must have clean
stable source, verified build provenance, zero failed/skipped and the named
roster. Review repeats the same selections against its exact reviewed source
and sets `reviewedSourceClean` only from validated receipts.

Run `scripts/check-evidence-diff.ps1 -BaseRef <task-base> -HeadRef <pushed-sha>`
over the complete Code/Review history. Generated logs/TRX/JSON/checkpoint output
remain ignored. Confirm any purported inherited failure by running only its
failing method at the task base, and report that extra diagnostic run with its
reason. Remove task-owned `bin-c1057-win/`, `bin-c1057-linux/` and
`bin-c1057-tool/` outputs across projects after all owned processes exit.

### Cost

All figures are estimates, not measured results. S1 implementation/new fixtures:
**25 minutes**. Ordinary checkpoint floor: **19 minutes** (9 + 1 + 1 + 8).
Two leased tool bootstraps/static coverage: **6 minutes**. Receipt review,
evidence guard, commit/push and output cleanup: **5 minutes**. Total **55
minutes**, plus live queue/slot waits and the caller's lane handoff. Code may
be split into serial lane dispatches without changing the one-slice source
scope. Ordinary Review has the same 19-minute checkpoint floor plus its own
bootstrap/evidence work.

Post-land Mutation is separate: **48 minutes**, estimating seven minutes for
each of six method-scoped baseline/red/restored-green cycles plus six minutes
for evidence and restoration. Budget host handoff/slot waits separately. Two
ordinary test builds support four rows, saving two redundant full project
builds. Seven total ordinary executions replace an unnecessary whole-Unit run;
no repeat-proof run is required after green.

### Checkpoints

Closed list for S1. Windows rows execute **4 results**; Linux executes **3**.
Union: **7 results**, all named methods, zero failed/skipped. The two new class
filters select only the methods declared above via their name prefixes. Reject
unexpected additional matches; these prefixes are not permission to widen scope.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1057-win/` | windows-junction-boundaries | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/(C1057_ChildJunction_*)\|(C1057_RootJunction_*)` | V-1,V-2 | exactly 2 named executed/passed, 0 failed/skipped | 2 | 9 | true |
| CP-2 | S1 | CP-1 | windows-owned-readonly | `/*/Antiphon.Tests.Application/WorktreeRetirementRaceTests/C694_DeleteDirectory_RemovesReadOnlyFile` | R-1 | exactly 1 named executed/passed, 0 failed/skipped | 1 | 1 | true |
| CP-3 | S1 | CP-1 | windows-real-git-fixture | `/*/Antiphon.Tests.Scripts/EvidenceGitFixtureTests/C1051_DisposeAsync_RemovesReadOnlyGitObject` | R-2 | exactly 1 named executed/passed, 0 failed/skipped | 1 | 1 | true |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1057-linux/` | linux-symlink-boundaries | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/(C1057_ChildDirectorySymlink_*)\|(C1057_RootDirectorySymlink_*)\|(C1057_FileSymlink_*)` | V-3,V-4,V-5 | exactly 3 named executed/passed, 0 failed/skipped | 3 | 8 | true |

## Publication and handoff

This task commits/pushes only the plan on `feat/card-task-78ef07b4`; no test or
implementation result is claimed. The caller publishes the plan through normal
landing after settlement, then commissions S1 with this artifact's checkpoint
section pinned to the plan commit. Prefer authoring plus CP-4 on the default
Linux lane, followed by CP-1 through CP-3 on Windows against the same committed
implementation SHA. No delegate needs access to another host's checkout.

Next is Code, then ordinary independent Review, land, and the six platform-scoped
SourceLanding PCs. A missing lane is pending work, not evidence of success.
