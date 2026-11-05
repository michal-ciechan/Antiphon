# Why the Windows rc run has incomplete coverage

Debug task: d8f70e4c. Investigation only; no product, test, policy, or runner changes.

The repository proves two recurring admission defects: required Windows discovery
includes tests that deliberately skip on Windows, and it includes the Explicit
LiveLane parity consumer without its separate producer/context workflow. Keep the
coverage checks strict and repair those dispositions. The three retention exclusions
on the cited repair branch prevent **new** gaps; none can explain the historical 58,
because their methods did not exist in the failed candidate.

An exact partition of the 58 and attribution of the two process exits require the
desktop evidence. No desktop files were accessed and no Windows result is claimed.

## Source and evidence boundary

| Item | Identity |
|---|---|
| Assigned base | `1527c29fef2737bf3eaa49f2293d82d85402265a` |
| Failed candidate, resolved from the supplied prefix | `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` |
| Supplied run | `b19a9880`, `rc/2026-11-06`, completed 2026-11-05 13:31Z |
| Historical evidence, supplied location only | `C:\Antiphon\releases\candidates\rc-2026-11-06-5a95ae7a5611\logs\2026-11-04-1808-b19a9880\summary.json` |
| Repair branch inspected | `origin/feat/card-task-340f1d0a`, tip `cc4ab8e31349db759181a2931e8840ec6d79805f` |

The candidate and assigned base have identical versions of
`scripts/lib/nightly-coverage.ps1`, `nightly-policy.ps1`, `nightly-tests-impl.ps1`
and `tests/test-execution-policy.json` (`git diff` across those paths is empty).
Test sources have advanced, so current source counts must not be substituted for
historical executed counts.

Read owners: `docs/nightly-watchdog.md`, `docs/release-gates.md` (RC coverage),
`docs/testing-and-build.md` (Nightly and platform markers),
`docs/orchestration-loop.md` (including D-2), and
`.claude/skills/antiphon-rc-qualification/SKILL.md`. The watchdog explicitly leaves
tests-red/coverage-incomplete to qualification/card handling; its heartbeat is not
execution evidence. No watchdog, supervisor, lock, or release operation is needed
for this investigation.

## How the verdict is calculated

`scripts/nightly-run.ps1` delegates to `scripts/lib/nightly-run-impl.ps1`.
The test producer is `nightly-tests.ps1` / `scripts/lib/nightly-tests-impl.ps1`;
the bootstrap reads its `summary.json` and propagates its booleans. The calculations
below are in the checked-in code, rather than inferred from the supplied summary.

1. Policy loading validates the hash over canonical JSON excluding only the
   `policyHash` property (`nightly-policy.ps1:53-99`). The selected profile supplies
   required suites. RC resolves platform-specific exclusions for the executor OS;
   frozen release authority resolves the Windows obligations.
2. Each discovered expanded UID gets a required/excluded disposition, including
   exclusion owner and reason (`nightly-coverage.ps1:728-846`). RC does **not** treat
   raw `OptIn`, `Explicit`, or a skip attribute as an exclusion. Only a declared
   class or class/method policy row does that. Platform omission means all platforms.
3. The native planner removes excluded nodes before launch. A partially excluded
   class becomes exact method chunks, preserving portable siblings
   (`nightly-tests-impl.ps1:48-88`, `Prepare-NightlySuiteExecution`). An exclusion
   is an admission disposition, not permission to reinterpret an executed skip.
4. `Read-NightlyTrxIdentities` joins every result to its test definition and retains
   class/method identity, display, and raw outcome. Missing definitions, duplicate
   test IDs, malformed/stale evidence, or missing TRX fail closed.
5. `ConvertTo-NightlyCoverageVerdict` (`nightly-coverage.ps1:999-1085`) starts with
   coverage/tests true. Zero results makes both false. `NotExecuted`, `NotRunnable`
   and `Pending` normalize to `NotExecuted`; **any** such TRX row makes coverage
   false and emits `unexecuted-outcome:<Class.Method,...>`. A raw `Skipped` also
   makes coverage false. These reasons do not themselves set testsPassed false.
   Failed rows and nonzero child exit set testsPassed false. Unknown outcomes make
   both false. SHA/ref/policy mismatch and discovery-as-execution invalidate coverage.
6. `Test-NightlyExpandedRowsPresent` (`:1095`) compares required discovery UIDs
   against terminal execution-diagnostic UIDs whose normalized outcomes are Passed,
   Failed, or Skipped. A required UID absent from that set emits
   `missing-expanded-row <uid,...>`. This name applies to **every discovered leaf**,
   including parameterless tests; it does not diagnose parameter expansion drift.
   A failed result can satisfy presence while failing testsPassed. A diagnostic
   Skipped UID can satisfy presence while its TRX skip still invalidates coverage.
7. `ConvertTo-NightlyNativeSuiteVerdict` also requires discovery and execution
   diagnostics, checks duplicate/zero required UIDs, class membership, and the TRX
   versus diagnostic class/method multiset. Suite completion checks the required
   UID union across all chunks (`nightly-tests-impl.ps1:1325`). Any incomplete chunk
   or union poisons overall coverage. Required suites, preflight/build evidence,
   native overlap and host scope add their own failure conditions.

The producer records `succeeded = !overallFailed && coverageComplete`; a failed
native verdict sets overallFailed. Run state additionally binds success to exit 0
and testsPassed. `coverageComplete`, `testsPassed`, success and report delivery are
separate gates. A green assertion roster alone cannot manufacture a complete rc.

## Windows skips: confirmed defects and protected obligations

The supplied message gives a count, not the 58 identities or skip reasons. Therefore
the following is a **source-derived repair inventory**, not a claim to have matched
58 historical rows. Expected non-Windows tests should be excluded before launch;
their current coverage failures are correct against an incomplete policy.

These current tests deliberately skip on Windows and have no matching Windows RC
exclusion. Their runtime gates also lack the pinned markers, so the existing
marker/row census cannot discover them merely by checking marked tests:

| Suite/class | Exact scope and source evidence | Small disposition repair |
|---|---|---|
| `pty-host / LinuxCustodyReleaseHostTests` | All 13 tests start with `!OperatingSystem.IsLinux()` and the inbox-launch skip. Source `tests/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests.cs:21` onward. | Windows-only class row, pinned class marker with the retained reason; owner CARD-1614 (related CARD-1617/1681/1685). |
| `agents-pty / Card1685LinuxCustodyRunnerTests` | `C1685_Direct_throwing_termination_still_backs_off`, source line 19. | Windows-only method row/marker, CARD-1685. |
| `agents-pty / LinuxCgroupContainmentTests` | Only `C1614_Helper_written_tree_is_read_by_production_reader`, line 205, uses Linux `unshare`. Other methods exercise an operations seam and stay required on Windows. | Windows-only **method** row/marker, CARD-1614. |
| `antiphon / TempRootSweepAnchoredDeleteTests` | Nine methods, each gates Linux native symlink/FIFO/socket/link/unlinkat/device behavior, source line 18 onward. | Windows-only class row/marker, CARD-1596. |
| `antiphon / LandingGitTests` | POSIX/native process stand-ins listed below; each explicitly skips Windows. | One Windows-only method-list row plus markers for those methods; keep actual Windows landing acceptance tests required. |
| `antiphon / OwnedProcessTreeTests` | `C448_SnapshotFailureKillsTheChildAndLeavesTheGrandchild` and `C448_ProductionKillPassesTheServerPid`, lines 474/530. | Windows-only method-list row/markers, CARD-0448. |
| `antiphon / CheckpointReceiptStallBudgetTests` | `healthy_receipts_complete_with_every_pool_worker_occupied(bool embedded)`, two argument rows, line 52; Linux scheduling witness. | Windows-only method row/marker, CARD-1601; other reader/budget tests remain required. |
| `antiphon / PosixHarnessToolTests` | `Capture_identity_resolves_git_sh_instead_of_hard_coded_bin_sh` and `Cutover_window_completes_without_cat_grep_or_tr_on_path`, lines 98/124. Both call `NativeLinuxGate.Require`. | Windows-only method-list row/markers; preserve Windows snapshot/timing/cutover siblings. |
| `antiphon / AgentReportStoreBudgetTests` | `Slow_git_response_does_not_spend_write_budget`, line 56; delayed Linux executable. | Windows-only method row/marker, CARD-1605; retain gate-wait/write-stall/IO deadline siblings. |
| `antiphon / CardRelationshipScriptTests` | `Block_404_detail_stays_intact_when_concise_view_wraps_at_120_columns`, line 188; Linux PTY used to reproduce Windows rendering input. | Windows-only method row/marker; keep `Block_fails_loudly_when_relationships_returns_404` required. |

For new native exclusions use `platform: windows`, a reason beginning
`Linux-only by construction:`, the real owning `CARD-nnnn`, and an exact canonical
`[NativeLinuxOnly("<retained runtime reason>")]` on the selected methods/class.
Do not rewrite a skip reason or add a frozen migration-baseline record merely to
make these newly declared identities look legacy. The permanent compiled witnesses
require reverse coverage, and the source census requires matching rows/markers.

One additional known skip has a separate cause:
`AgentReportStoreLoadTests.Store_samples_record_normal_and_contended_phases`
first requires `ANTIPHON_REPORT_STORE_MEASUREMENTS=1` and then native Linux
(`tests/Antiphon.Tests/Application/AgentReportStoreLoadTests.cs:24-29`). It has no
RC exclusion. Declare this opt-in cost sample as a separate manual obligation
(method-scoped, all platforms, owner CARD-1605), or explicitly commission its
environment/workflow. Merely enabling it on Windows still hits the Linux gate.

The rows above already existed in the failed candidate (the inspected file diff
to base is empty except LandingGit additions). This demonstrates real recurring
admission gaps independently of identifying their entries in the historical 58.
It is not a complete count of all runtime-gated tests.

Do **not** equate every platform/capability skip with an allowable exclusion:

- Windows sharing handles, junctions, symlink privileges and case-sensitive directory
  capability are required Windows evidence where policy says so. Missing tools,
  staging, provider context on a required test, or a fixture refusal are real gaps.
- The 35 CARD-1199 checkpoint obligations remain required on both hosts. Their gates
  were removed; `C1199_WindowsRequiredRoster` explicitly asserts that NotExecuted
  cannot be complete. `RunnerWorkspaceBindingTests.C1032_StoreIdentityRefused` now
  gates only hosts that are **neither** Linux nor Windows; its old-looking skip
  message is not grounds for excluding its Windows arguments.
- `test-rc-platform.ps1:741-827` deliberately protects decision classes/methods
  (C680, checkpoint output/source readers, workspace identity, etc.). Do not add
  blanket rows for them based on old summaries or their source mentioning Linux.
- Existing declared native exclusions (UnixPtyArgvTests, Linux host/runner exit
  tests, FileUseCapture's marked methods, the statx OutputCleanup methods, etc.)
  already stay outside Windows RC. If those exact excluded identities appear in
  the actual run's TRX, inspect the discovery identity and launched filter before
  adding duplicate policy rows or ignoring terminal evidence.

### LandingGit method list for the Code owner

These thirteen native stand-ins without data arguments were already present in the failed
candidate and remain uncovered by Windows policy at base:

```text
C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder
C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget
C1076_UpdateRefsStdinBreakClearsTheJournal
C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits
C1076_StrangerPipeHolderDoesNotKeepTheJournal
C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal
C1076_UnreadableEnvironOfALaterProcessKeepsTheJournal
C1076_UnreadableEnvironOfAnEarlierProcessDoesNotKeepTheJournal
C1076_LateJobAssignmentWithNoMembersKeepsTheJournal
C1076_JobAssignedBeforeSpawnWithNoMembersReleasesTheJournal
C448_ParentDeathGrandchildDoesNotKeepTheJournal
C448_JournalStaysWhileTheRootLives
C448_LateInTreeHolderIsKilledAndTheJournalIsRemoved
```

Current base also adds `LandingGitTests.C1714_Status_that_cannot_open_a_directory_is_untrusted`
(two scope arguments, Windows POSIX-permission skip), and
`C0824DesktopLegacyReleasePolicyTests.Status_that_cannot_read_the_whole_tree_holds_it`
has one Windows skip for argument `unreadable-directory` while
`long-path-config` is required. Neither is a reason to exclude the whole class or
the mixed method. Split the latter's Unix argument into a distinct marked test
method and keep the Windows-capable argument required. Inspect these newer rows
when preparing the next tip; they cannot be blindly attributed to the old 58.

## What batch 85 actually covers

`19b9e69513551259b7c56eec03b77ef3a4cc861c` adds two methods to the existing
`antiphon / C1677RetentionServerTests` Windows row:

- `Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay`: nine arguments.
- `Ignored_nested_repository_secret_survives_real_transport`: one result.

`88ed031df17ad9559d830e7cf120faaaddf05b27` refreshes the live-lane input pin.
`cc4ab8e31349db759181a2931e8840ec6d79805f` adds the Windows-only
`session-runner / C1677RetentionEngineTests.Doubt_inside_collapsed_ignored_repository_keeps_the_tree`
row (four arguments) and refreshes both digests. These three exclusions therefore
cover **14 potential new Windows NotExecuted results**, not three expanded results.

`git grep` at `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` finds none of those
three names under tests. The server tests gained them after that candidate (66
added lines); the engine file also advanced (233 added lines). Consequently these
fixes remove **zero of the historical 58**, while remaining necessary for the next
candidate if it contains those new methods.

Read-only inspection of the real source-census function against the assigned tree:

```text
base policyHash=5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4
markerKeys=75
problems=marker-without-row for the expanded C1677RetentionServerTests method list;
         marker-without-row for the C1677RetentionEngineTests method;
         row-without-marker for the stale C1677RetentionServerTests method list.

repair policyHash=0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b
computedHash=0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b
markerKeys=75; problems=[]
repair PinnedInputDigest=f5151a2a95ed14a8de0a50f9a475d0d45853949ce61c1ed83304c04178bfd73f
```

This was a direct call to parsed inspection functions with `Catalog=$null`, not a
test run or a compiled-marker proof. It proves row/marker agreement for that source
and policy combination. It says nothing about the **unmarked** runtime gates above,
routing, Windows execution, or eventual qualification success.

## The missing LiveLane UID

`LiveLaneIsolationTests.StartupArgvMatchesProductionApplyOff` is `[Test, Explicit]`,
**parameterless**, at `tests/Antiphon.Tests/Scripts/LiveLaneIsolationTests.cs:301`.
The failed candidate has the same attributes/signature/body. Its internal model and
role loops are not separate TUnit argument expansions. `tests/live-lanes.json` also
records `arguments: []` in the additive inventory.

`scripts/test-live-lane-isolation.ps1:26-43` states the precise runner behavior:
TUnit omits Explicit under the class wildcard. That harness lowers CP-24's count
from five to four and adds `CP-24-PARITY` with this exact method filter:

```text
/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/StartupArgvMatchesProductionApplyOff
```

RC currently has no exclusion for this class/method, so discovery requires its UID,
while the class wildcard skips its Explicit body. The class contains ordinary
tests, so this omission does not by itself imply the whole chunk ran zero tests.
There is no `Environment.Exit` in this method. It is not evidence of process death
or unstable argument generation.

Simply forcing that filter into nightly is insufficient. The body immediately
calls `LiveClaudePortableContext.Required()`; without
`ANTIPHON_LIVE_PORTABLE_CONTEXT` it throws `missing-portable-context`
(`tests/Shared/LiveClaude/LiveClaudeArgvCapture.cs:11-22`). Its capture loader then
requires source/run/build-bound captures from the PTY producer. The dedicated
script runs the producer first, writes its receipt identity into the context,
and only then runs the consumer. Its context is owned and inert; this is a
separate portable parity workflow, not permission to call a live provider.

The smallest correction is one **method-only**, all-platform RC exclusion, owner
CARD-1545, explicitly naming the separately commissioned producer/consumer parity
workflow and its required owned context/receipt. Keep every ordinary isolation test
required. Retain the exact-filter parity obligation in its dedicated workflow.
If the intended acceptance is to include that obligation inside Full instead,
commission the producer/context/receipt integration; removing Explicit or changing
the missing-UID check alone cannot deliver that acceptance.

This diagnosis is source-supported. To prove the historical manifestation, compare
the actual discovery UID and terminal diagnostic in the affected chunk as below.

## The two process exits

`process-exit-N` is constructed by `ConvertTo-NightlyCoverageVerdict` from the
**native test host's** exit code. It is not the nightly bootstrap's exit-code table
or the checkpoint tool's different meanings for 2/8. The runner invokes the built
host with native TRX/diagnostic/filter arguments and records its exit verbatim.

The [MTP exit-code table](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-troubleshooting#exit-codes)
and the [upstream named constants](https://github.com/microsoft/testfx/blob/v4.0.2/src/Platform/Microsoft.Testing.Platform/Helpers/ExitCodes.cs)
identify 2 as at least one test failure, 8 as ZeroTests. Crash/non-graceful exit is
the distinct code 7. Current MTP also documents strict all-skipped handling for 8;
the repository parser pins MTP 2.2.2, so consult that run's console/counters rather
than assigning newer zero-tests options to it.

- **2:** normally the consequence of failed tests in its chunk. Identify the failed
  TRX rows before treating it as independent infrastructure failure. Repairing
  admission alone does not fix a failed assertion.
- **8:** an empty effective selection/zero-test host result, potentially related to
  Explicit filtering or an all-skipped selection, rather than an ordinary assertion
  failure. Inspect that chunk's filter, console counters and skip reasons. It may
  have the same admission root cause as unexecuted rows but is not proved to be the
  LiveLane chunk. Do not ignore exit 8 globally or accept zero required tests.

A nonzero exit directly makes **testsPassed** false, not coverageComplete in this
helper. Missing/NotExecuted rows and evidence checks separately make coverage false.
The producer sets overall failure and usually returns 1; that is a different layer.

## Smallest repair sequence, ordered by value

| Order | Concrete change | Blast radius / hashes |
|---|---|---|
| 1 | Reconcile the old summary's 58 identities against its TRX reasons; implement the confirmed Windows-only admission rows/markers above, preserving portable siblings. Include the manual load-sample disposition. | Test admission only; `policyHash` **changes**, and the live-lane census pin **must refresh in the same slice**. No production behavior change. |
| 2 | Add only the CARD-1545 LiveLane parity consumer RC exclusion; retain its dedicated exact-filter producer/consumer workflow. | One UID becomes excluded in RC. `policyHash` **changes**; refresh the census pin **together**. This can share order 1's atomic policy slice. |
| 3 | Integrate/retain the cited batch-85 repair at its complete tip; do not duplicate its three rows. On the final combined tree recalculate hashes instead of copying a branch's old values. | Prevents 14 new skips. Existing repair changes policyHash and `scripts/test-live-lane-run.ps1` pin together. No new migration baseline needed. |
| 4 | Fix newly added mixed platform test shapes (e.g. split the C0824 unreadable-directory argument) and any actual required Windows capability/fixture gaps identified in the residual 58. Keep current C1714 POSIX scope cases Windows-excluded by precise method/marker after confirming ownership. | Narrow test fixes. A source split alone does not change policyHash; adding/changing the resulting policy rows does, and then requires the paired census-pin refresh. |
| 5 | Map the exit-2 failures and exit-8 selection to chunks, fixing assertions or filter/fixture admission as the evidence requires. | No blanket runner accounting relaxation. Test-only fixes need neither digest unless policy/protected reader inputs also change. |

No change to `nightly-coverage.ps1` is established as necessary. In particular,
do not normalize NotExecuted to Passed, ignore every skip with a platform-sounding
message, count discovery as execution, or remove required-UID reconciliation.
If actual TRX shows a policy-excluded method was still launched, investigate the
specific identity/filter planning defect; that is a conditional runner fix, not a
reason to preemptively weaken the verdict.

`scripts/test-live-lane-run.ps1:12-34` pins five normalized inputs: nightly common,
policy, coverage, the policy JSON and the discovery fixture. Any change to those
readers also requires the census-pin refresh, even when policyHash itself stays
unchanged. Marker-only test edits do not independently alter those five inputs.
Review actual live inventory disposition expectations if a new policy row touches
a declared gate; do not regenerate historical discovery/migration evidence wholesale.

## Exact evidence the caller must read on the desktop

Read the supplied run's `summary.json` **read-only**:

1. Bind `runId`/`invocationId`, `sha`, `gitRef`, `profile`, `platform`, `policyHash`,
   `coverageComplete`, `testsPassed`, `outcome`, `reasons` to b19a9880 and the pinned
   candidate. Use `suites[].evidenceReasons` to locate each unexecuted/missing UID
   reason without double-counting duplicates copied to top-level `reasons`.
2. For each affected suite row retain `id`, `chunk`, `exitCode`, `timedOut`, `result`,
   `log`, `trx`, `evidenceReasons`, `requiredUidCount`, `excludedUidCount`,
   `censusDigest`, and `exclusions`. These fields locate which chunks emitted
   process-exit-8 and process-exit-2; that association is unavailable in this brief.
3. Open each row's exact `trx` file. Join
   `Results/UnitTestResult@testId` to `TestDefinitions/UnitTest@id`, then
   `TestMethod@className`/`@name`. Retain raw `@outcome`, `@testName`, and
   `Output/DebugTrace`, `Output/ErrorInfo/Message`, `Output/StdOut` for the skip/fail
   reason. Parameter expansions can share Class.Method; do not collapse them.
4. Match LiveLane's required UID in `antiphon-profile.discovery.json` (`nodes[]`:
   `uid`, `class`/`type`, `method`, `categories`), or the chunk's
   `*-selection.discovery.json` when its summary/planning used a leaf selection.
   Compare it with terminal TestNode UID/state in the `.diag` selected from
   `<id>-<chunk>-execution/`, and the row's `log` for actual test counters/filter
   diagnostics. Equal method names with different UIDs would warrant an expansion
   investigation; no terminal UID under the wildcard supports Explicit omission.
5. If the filter is not in the summary, use that run's timing planned-group files
   (`timing/groups-planned.json` or its index/shards) and matching executed-group
   evidence to recover class/method selection. Never infer the filter from a newer
   source tree. Counters/diagnostics decide whether exit 8 was zero discovery or an
   all-skipped selection. Exit 2's failed test messages decide its repair owner.

I cannot determine the exact 58-row partition, which skips were environmental
failures versus legitimate Windows exclusions, the exit-to-chunk mapping, the
historical failed assertions, or whether the missing UID also had a skipped TRX row
without those files. This is an explicit evidence limit, not a claim that all 58
are acceptable skips or that the next run is already green.

## Verification and handoff

No builds/tests were run: this deliverable is a read-only Debug investigation.
The direct policy hash/census inspection above is not a test receipt. No bin, obj,
test-result directories or temporary evidence were created, so none need deletion.
The only changed file is this report. Retain the old run's failure and cost history.

The Code task should first use the supplied desktop extraction to close the exact
row list, then make one coupled policy/marker/LiveLane fix slice on its assigned
branch. Validate the shipped policy hash and `-PrintInputDigest` on the final tree;
use the existing `LinuxOnlyWindowsExclusionPolicy`, four compiled
`NativeLinuxPolicyWitnessTests`, `C599_ProfileSchema`, and
`LiveLaneRunnerTests.WrapperOnlyPaidManifest` at their exact filters, with at most
three rows per run and each build/run leased. Include a narrow regression for the
LiveLane disposition and required portable siblings; do not run the separately
commissioned parity consumer without its producer receipt. A policy exclusion must
leave the Linux required roster intact. Do not rerun the broad suite for this report.

The caller then integrates the fixes and starts the already-intended fresh pinned
Windows Full qualification after the native lock is released. Only its new summary,
complete required UID ledger, successful required tests and release receipts can
establish coverageComplete=True. No tags, publication, activation, land-graph Update,
desktop action or approval request was undertaken here.

--- next stage ---
next: code
handoff: Add the exact Windows-only rows and matching pinned markers in the repair inventory; declare AgentReportStoreLoadTests' manual sample and only LiveLaneIsolationTests.StartupArgvMatchesProductionApplyOff as separate RC obligations; retain batch-85's three existing exclusions; split mixed Windows/Unix arguments instead of dropping portable siblings; refresh policyHash and scripts/test-live-lane-run.ps1 PinnedInputDigest atomically. Reconcile the residual historical identities and exit chunks from the named summary/TRX/diagnostics before selecting further assertion or runner fixes. Keep all coverage checks strict.
artifact: docs/investigations/2026-11-05-debug-rc-coverage-incomplete.md
