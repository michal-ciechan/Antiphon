# Windows rc 5a126f6fc qualification reds

Debug `c2afd605`, 2026-10-23. **The rc is not releasable.** The inspected results contain
19 failed cases: seven in completed groups 3/13/14 and twelve in the incomplete group 15.
They reduce to six causes. C544 is the CARD-1529 product defect, not the C1205 fixture defect.
The unregistered cleanup retry exposes a separate inherited product admission problem.
The remaining failures are fixture defects. No observed failure is newly attributable to
the delta from the previous Windows candidate `0714a9a89`.

Only this diagnosis, ignored local evidence/card drafts, and three requested Backlog cards
were written. No builds, tests, dotnet/MSBuild, process signals/kills, stack operations, or
writes beneath `C:\Antiphon\releases` or `C:\Antiphon\verification` were performed.
Supervisor PID 209072 remained untouched. No fresh baseline execution is claimed.

## Evidence boundary and coverage

Current candidate: `rc-2026-10-20-5a126f6fc5ae`, source
`5a126f6fc5ae4e16d803b628716b8f0485a37174`, invocation
`6bd38cbb9604482e96a4854957cead05`. Candidate and selection journals bind the source/run.

Current evidence root **R**:
`C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae\logs\2026-10-23-0201-6bd38cbb`.
Previous root **P**:
`C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b\logs\2026-10-22-1606-d2777e21`,
source `0714a9a89e4bff65aae7e0b4b94098694aeb931c`, invocation
`d2777e21ef4c4a3e9b1fb315ff8f5602`.

At the first inventory, R contained TRX files for groups 1 through 14. Those files contain
2,198 passed and 7 failed results, zero skipped. Group 3 is 75/1, group 13 is 328/5,
group 14 is 149/1 (passed/failed). The other completed groups are green.
R's group 15 console and `antiphon-all-group-15-execution/log_261023055003445.diag`
add 12 failures. Its last per-test states are 312 Passed, 12 Failed and one InProgress;
there is no final TRX or completed group summary. These are observed results, not a
completed 325-case gate. Group 16 has started its execution directory. Final inventory
and cutoff are recorded at the end of this report.

**The brief's previous-run cutoff was stale.** P did not stop at group 10: its invocation
headers and diagnostic files establish that it reached group 22. Completed TRX files
exist for 1-9, 11-14, 16 and 18-21. Groups 10, 15, 17 and 22 have execution evidence but
no completed TRX. No group 23-or-later artifacts were found. Thus none of today's 19
failed cases was in an unreached previous group: all were found by exact test identity
in P's TRX or group-15 diagnostic results. Group numbers changed between rosters.
P's `last-run.json` says `coverageComplete=false`, `testsPassed=false`; it is not a green
baseline. R's corresponding state is also incomplete and false.

## Per-case disposition

Class names: **D** = `DirectSessionRunnerClientDisposalTests`, **T** =
`TurnEndGapRestartDeliveryTests`, **V** = `VerificationRoundDeliveryTests`, **A** =
`AgentTaskLandCleanupSafetyTests`, **L** = `LandNonCodeSkipTests`.
Cause letters refer to the source analysis below. Linux evidence labels are expanded in
the next section; none is a current-candidate pass certified by this Debug.

| Group / test | Exact signature (full recorded stack below) | Cause / classification | Previous Windows / Linux | Card / owner next step |
|---|---|---|---|---|
| 3 D.`Dispose_awaits_exit_of_its_running_host_and_descendants` | `UnauthorizedAccessException: Access to the path 'Antiphon.Agents.Pty.dll' is denied.` | A: inherited fixture/filesystem-release timing; original holder unknown | Passed, P group 3 / L1 | CARD-1515; native fixture verification and rc inclusion |
| 13 T.`C1205_Watchdog_delivers_while_event_stream_remains_healthy` | `RecoveryOutcomes.Last()` expected `"CheckedWorking"`, actual `"Unknown"` | B: test DI composition bug | Same red, P group 12 / L2 | CARD-1492; fixture coordinator registration |
| 13 T.`C1205_Busy_tail_keeps_completion_obligations_pending` | `RecoveryOutcomes.Last()` expected `"CheckedWorking"`, actual `"Unknown"` | B: test DI composition bug | Same red, P group 12 / L2 | CARD-1492; same repair |
| 13 T.`C1205_Restart_delivers_five_gap_held_completions_once(False)` | `Caller.SubmittedBodies.Count` expected `5`, actual `0` | B: test DI composition bug | Same red, P group 12 / L2 | CARD-1492; same repair, both cache modes |
| 13 T.`C1205_Restart_delivers_five_gap_held_completions_once(True)` | `Caller.SubmittedBodies.Count` expected `5`, actual `0` | B: test DI composition bug | Same red, P group 12 / L2 | CARD-1492; same repair, both cache modes |
| 13 V.`C544_BriefHandoffRecovery` | `Dispatched` expected, `Blocked`; `warm inline busy=False: dispatch_brief_evidence_uncertain: current brief evidence is not a complete unattempted row or a complete receipt.` | C: inherited product regression, compact mistaken for uncertain brief evidence | Same red, P group 13 / L3 | CARD-1529; producer/brief identity repair |
| 14 A.`C448_V18_CleanupRetryPreservesChangedWork(unregistered)` | `retry.Mode` expected `CleanupRetry`, actual `Fresh` | D: inherited product admission/liveness regression; preservation intact | Same red, P group 13 / L4 | **CARD-1536**, High; confirmed-publication cleanup admission |
| 15 L.`C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff` | `h.Verifier.Calls` expected `0`, actual `1` | E: Windows graph-seeding test bug | Same red, P group 15 / L5 | **CARD-1537**; preserve committed blob bytes |
| 15 L.`C1385_UnrelatedScriptChangeStillSkips` | `h.Verifier.Calls` expected `0`, actual `1` | E: same | Same red, P group 15 / L5 | CARD-1537 |
| 15 L.`C1380_RegularDocumentationChangesSkip(empty)` | `h.Verifier.Calls` expected `0`, actual `1` | E: same | Same red, P group 15 / L5 | CARD-1537 |
| 15 L.`C1380_RegularDocumentationChangesSkip(rename)` | `h.Verifier.Calls` expected `0`, actual `1` | E: same | Same red, P group 15 / L5 | CARD-1537 |
| 15 L.`C1380_RegularDocumentationChangesSkip(README)` | `h.Verifier.Calls` expected `0`, actual `1` | E: same | Same red, P group 15 / L5 | CARD-1537 |
| 15 L.`C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` | `h.Verifier.Calls` expected `0`, actual `1` | E: same | Same red, P group 15 / L5 | CARD-1537 |
| 15 L.`C1390_UpdateRefusesDocsMarkdownInput` | access denied to `5436f699aebcbc4936bd97772b608fc52136f5` during recursive delete | F: Windows read-only Git-object teardown test bug | Same red, P group 15 / L6 | **CARD-1538**; owned Git-root cleanup helper |
| 15 L.`C1400_UpdateRefusesCaseVariantDocsInputs` | access denied to `cab8569e60cd993ff6c927fa838ff67fdf533f` | F: same | Same red, P group 15 / L6 | CARD-1538 |
| 15 L.`C1400_UpdateRefusesImportedPropsDocsInclude` | access denied to `68d84c466e9d2101aef415c4d2dd91bf3acb52` | F: same | Same red, P group 15 / L6 | CARD-1538 |
| 15 L.`C1400_UpdateRefusesPercentEncodedDocsInclude` | access denied to `44f9908057c3e71945558b2bce8ee1c9e1e7b6` | F: same | Same red, P group 15 / L6 | CARD-1538 |
| 15 L.`C1400_UpdateRefusesAdditionalFilesDocsInclude` | access denied to `ee67f20a0ff13c65b896c855cddc7d8a0f7cc4` | F: same | Same red, P group 15 / L6 | CARD-1538 |
| 15 L.`C1400_RepairAnchoredImportRefusesDocsInclude` | `UnauthorizedAccessException` in shared audit-root teardown; exact object and stack below | F: same | Same red, P group 15 / L6 | CARD-1538 |

## Linux/server2 evidence, without accessing /work/worktrees

These are retained board task reports and investigation documents, not locally revalidated
raw Linux receipts. Whole-class passes establish membership only where the retained filter
or report names it. No absent Windows-only execution is called green.

| Label | Retained evidence and limit |
|---|---|
| L1 | CARD-1515 Code `b1c2f922` and Review `56b83abb` at `c7b0899cbf6ad2b6965ab47055a37cc9007468e6`: portable cleanup CP-2 4/4 and trace CP-4 1/1. These are **different methods**, not a pass of today's disposal method. Native CP-5 through CP-14 remain owed. No recent exact-method Linux pass established. |
| L2 | CARD-1205 retained closure records S5 CP-5 31/31, CP-6 4/4 and restored 34/34 before the later dependency change. More recent CARD-1464 Code `5b7d64fd-1f21-48b2-81cf-6d8b63f6cab8` reports C1205 x4 red at base `08765c68c` on server2 Linux, among 44 plausible cases with 13 failures. CARD-1492 records the same family. The green CARD-1464 CP-1/2/3 filters exclude these cases. No recent exact same-source green. |
| L3 | The same CARD-1464 Code report includes C544 in its Linux base reds. Its then-attribution to the missing coordinator is too broad for this assertion; the current exact reason establishes C below. CARD-1529's separate Linux witness `C1491_matching_effort_reuses(True,xhigh)` was also red, but is **not the same test**. Its later same-root green does not verify unrelated-root handoff. |
| L4 | [CARD-0688 R1 repair 1](2026-09-25-card-0688-r1-repair-1.md) and [repair 2](2026-09-25-card-0688-r1-repair-2.md) explicitly retain this exact unregistered/Fresh red on Linux. Repair 2 CP-6: 185 executed, 174 passed, 11 failed, 1 skipped; source `de65f70c8769a5a465b22b7877516299a9661e93`. No recent pass established. |
| L5 | CARD-1385 Linux Final Review `b675029e` records CP-2 44/44 at `0add2862768c9612909345034fe65b736ecba3a8`, both C1385 tests and all 42 C1380 cases. Rc backport Code `b292971e` and Review `41f0874b` record 49/49 at `dc47eb4833519dd734ea44c67e3d6b8faa10e14c`, selecting the entire then-existing `LandNonCodeSkipTests`. These are historical Linux passes, consistent with LF source files. |
| L6 | Linux rc-backport Code `0f30ca55-1737-4d12-b1a7-8ce47cb41cc1` records `CHECKPOINT CP-2 commit=795ee168e52f7de8bd325e8a93087dd11d10dbb7 build=ok filter=/*/*/LandNonCodeSkipTests/(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*) executed=14 passed=14 failed=0 skipped=0 slot=granted waited=60s dirty=0 sourceState=clean buildSource=verified`. Receipt path in the board report: `/work/worktrees/task-0f30ca55/.antiphon/checkpoints/20261021-205200-4860/rows/CP-2/run.trx`. The report's prose counts are inconsistent with 14; preserve the receipt count and filter, not its prose arithmetic. This host did not open that path. |

## Source attribution and smallest repairs

Git log/blame was inspected on fetched `origin/master`
`3d234eda960feb72b74db4c6b4fc7b1ccfa9cc97` and `origin/rc/2026-10-20`
`5a126f6fc5ae4e16d803b628716b8f0485a37174`. Common SHAs below are present in both histories;
different cherry-picked origins are listed separately. This is source attribution, not bisect
or a newly executed baseline.

### A. Disposal succeeds before private-tree deletion fails

The failure is `OwnedPtyFixture.DeleteRoot:366`, called by the test at line 167,
after `DisposeAsync`, independent exit assertions and the less-than-10-second assertion.
Test/one-shot teardown origin on both histories:
`75704a29be2ab347897661e6a19664a9d0faad6c` (CARD-1020). The test file is the same blob
`58cbdc568f82357648afff7e2d8a91217d4b3705` at both Windows candidates.

R's session `e81b6fbd-2f42-46ad-a2ba-53fbacea5d97` owned host 47556, fakegrok 83884
and console 232756. Host acknowledged shutdown at `02:24:18.3958267Z`;
cleanup observed all exits at `.46740Z`, and independent witnesses were all true at
`.4931077-.4931403Z`. The test failed at `.5238722Z`. All captured generations had exited.
P's same method passed at `16:14:12.4064217-16:14:16.7724739Z`; P's sibling
`Dispose_is_idempotent` instead failed on `OpenConsole.exe` after its exit witnesses.
That sibling passes in R. See the [previous diagnosis](2026-10-22-rc-group-3-reds-debug.md).

Exact refused DLL:
`C:\Users\lndco\AppData\Local\Temp\c1020-pty-6a095f21a7804fd8b37749b4ca52c5ac\logs\pty-hosts\bin\20261023-022413-10b93634\Antiphon.Agents.Pty.dll`.
This is the fixture's copied image, not the AppHost/session-runner checkout DLL or the
qualification build output. `ShadowCopyStore` copies files; it does not make these images
hard links. Later metadata: Archive, 246272 bytes, created `02:24:13Z`, no ReadOnly bit.
At `06:52:47Z`, a read-only Restart Manager resource query returned success and zero
holders; the three witness PIDs were absent. No shutdown/restart API was called.

**Historical lock owner is unknown.** The late sample cannot prove which process held
the DLL at `.523Z`, nor rule out a scanner or transient mapped-image release. The evidence
does rule out an observed surviving captured child and a failure during build/copy. There
is no basis to blame the live AppHost, session-runner, memory load, fixture ordering, or a
new production disposal regression. The missing boundary is filesystem release after
process exit, the same CARD-1515 repair scope. Master already includes fixture fixes
`4440d7a03e0b77389650b1b4ef4192331b5bb13e` and
`1f55bfc32df6aac32a48f9246748d5786cbe9e35`; this rc does not. Preserve all exit/deletion
assertions and persistent-denial controls; native qualification remains owed. Do not add
another blind retry or change production linger/timeouts.

### B. Four C1205 cases lack a required fixture service

`TurnEndGapDeliveryHarness.Configure` registers `SessionStateStore`; `C544World.BuildServices`
and the harness register no `SessionRuntimeActionCoordinator`. Production runtime now commits,
publishes to the store, then calls `Actions().Hint`, whose `GetRequiredService` throws.
The enclosing catch records a persistence failure and returns Empty. Working recovery then
returns `Unknown/persist_failure_retained`; restart catch-up cannot execute the action/wakeup
path even though SQL contains the recovered end. Both restart cases log
`ends=1 sqlWorking=False pulls=3 submissions=0` plus
`Transcript state unavailable ... exception=InvalidOperationException` and skipped sync.
This accounts for both signatures, with cache enabled and disabled. The original exception
message/stack is not retained by the recording logger; the precise DI cause is source-derived.

CARD-1205 introduced the five-note witness at
`1e8c2a84f45ba03221e6e5da70540c87c92716ca`, watchdog/busy witnesses at
`aa647fc2ca9adcad52250c82b348b2ae592001a6`. CARD-1259 S2 introduced the required coordinator
at `dd039a405c9425d7cffc83b64d60a1032d940636` on both master and rc, without updating this
hand-built world. The world, harness and test files are unchanged between P and R.
Register the coordinator and its proper owned processing path in the fixture, preserve
real commit/store/receipt behavior and all four assertions, and verify under CARD-1492.
Increasing waits, adding delivery retries or changing production recovery would not fix DI.

### C. C544 is CARD-1529, before fixture transcript recovery

The first failing cell is `warm inline busy=False` at the initial `task.Status` assertion
line 171. Its warm Claude seed has a different root; it uses the real producer. The producer
persists the refocus `/compact` with the new task's execution identity. Ensure sees that
markerless row as other current evidence and blocks before inserting the marked dispatch
brief. The later restart/raw-transcript fixture operations have not run for this cell.
Thus the missing coordinator in B can coexist but does not explain this observed block.

Test origin: `3f5fecee090ee4fde8087c1cc74feb8742d95946` (CARD-0544), both histories.
Product regression origin: `dbe4e4071f52adb17706aba499a66690bf0394bc` (CARD-1150), both.
The two brief classification files and test are unchanged between P and R. The full producer
and recovery analysis is [CARD-1529 diagnosis](2026-10-23-card-1529-warm-handoff-compact-debug.md),
landed as `6d847b85b`. Give positively identified refocus rows a durable non-brief purpose;
align dispatch admission and recovery consumers while keeping uncertain-evidence holds,
execution/deadline ownership and complete UserPrompt receipts. Do not change this fixture
to same-root merely to avoid the failing behavior. Use existing CARD-1529, no duplicate.

### D. Plain replacement directory never reaches published cleanup retry

The test first publishes and refuses cleanup, removes the registered source worktree, then
recreates the path with `new.txt`. `AgentTaskLandService.RunLeasedAsync` probes source index
locks before the protocol reaches the existing published operation. The source directory
exists but has no repository. `LandingGit.InspectIndexLockAsync` therefore cannot resolve
`rev-parse --git-path index.lock`; `index_lock_path_error` maps to `git_index_lock_held`.
The service returns Held before the protocol assigns `CleanupRetry`. The prior operation
remains Fresh. File bytes, operation ID and remote-publication assertions all passed first.
There is no deletion or republishing demonstrated by this red.

This mechanism is source-derived; the test does not assert/capture the request's hold reason.
It explains why merely allowing more time is ineffective. Test/mode assertion origin:
`2f3b9dae994e5b899d13d3716bd2e60dc9d31379` (CARD-0448). Source admission probe origin:
`2339af1c40f4adb350d1923d524862b2fa9879a7` (CARD-0543); missing-directory exception
`8eee96b5025b95c0fb423702bf15e1334a378945` excludes absent paths but not plain replacements.
All remain in both histories. CARD-0688's later dedicated-worktree changes did not close it.

New **CARD-1536**, ID `33097e3d-6e7d-4097-94cc-bc1e654c8f57`, Backlog High/Soon:
admit proven published cleanup far enough to record the guarded identity refusal for a
positively replaced/unregistered source. Preserve real-lock/unknown-identity refusals and
all new-file/publication protections. Capture the request Held reason in the regression.
This is a liveness/reporting defect, not evidence of destructive cleanup.

### E. Graph fixture commits CRLF blobs against an LF manifest

`SeedGraphAsync` uses `File.Copy` from the Windows checkout. `LandingGitFixture.FixtureGit`
sets `GIT_CONFIG_NOSYSTEM=1` and an empty global config; the scratch tree has no source
`.gitattributes`. Therefore it commits the copied CRLF bytes rather than the original LF
blobs. Production's exact graph comparison correctly falls back to a build.

Read-only hashing of 58 selected R files established this without invoking the audit or tests:

| Representation | SHA-256 of ordinal-sorted, NUL-terminated ls-tree-style rows |
|---|---|
| R committed tree and checked-in manifest | `637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E` |
| Raw copied working-tree bytes, fixture modes | `84BBB5CD6D56D3D3160BB59A25983B73A22094B5B23ACCC8FF3D9CB81A3E5456` |
| Same bytes with CRLF normalized to LF | `637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E` |

All 58 raw blobs differ, and all 58 normalized blobs match. P's 57-file committed digest
also matches its manifest (`F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F`).
This excludes stale checked-in manifests as the explanation. It is distinct from the older
CARD-1385 overbroad script inventory. Original failed scratch graph trees were not retained
as operation receipts; the copied-byte digest is a read-only reconstruction from the source.

Seed/docs-only witness origin: `7b6e444d7a1b30da43e13f173cd211eec88a8394` (CARD-1380);
target-contained witness: `895eebe1877b55cb94f086b3a08c796a0544a3d5` (CARD-1380).
C1385 witness origin is master `ed75caa740f06cf05a07ff138a9270a11fb63213`, rc
`0fa5da4f127952dd726a4521e84a89d3c2eef4ea`. The defective seeding remains on both.

New **CARD-1537**, ID `83fee76a-3027-4ecd-8087-d564b9d0d472`, Backlog Normal/Soon:
seed committed blob bytes/modes, or explicitly equivalent normalization, and establish graph
identity before testing a skip. Keep the production fingerprint and fail-closed policy.

### F. New audit fixtures repeat the read-only Git-object teardown bug

The six paths create/commit scratch Git repositories, then call raw recursive
`Directory.Delete` in `finally`. Windows loose objects carry ReadOnly; deletion throws.
Five exact named leaves were found in this run's residual `c1390-audit-*`/`c1400-audit-*`
roots with attributes `0x21` (ReadOnly|Archive), including
`C:\Users\lndco\AppData\Local\Temp\c1390-audit-rhav0vcm.bzz\.git\objects\0f\5436f699aebcbc4936bd97772b608fc52136f5`
(root creation `06:28:30Z`). The imported-props object also occurs in the shared anchored
repair fixture. This is deterministic Windows teardown, not the transient DLL issue A.
A finally exception can mask an earlier assertion, so these logs alone do not prove every
audit assertion passed.

Origins (master / rc): CARD-1390 test and raw delete,
`4f0a2aafcedf398808857094d58370718d78e1dc` /
`7f7f61aebe0d58553ae3e9d0b54153f0f72ec62e`; CARD-1400 initial case/import/escape/additional
tests, `8856d4cb1ca95f0d6b44165b8dcc5135231a6743` /
`ab68fa6e05f0ee1a918587d958e32b072005604a`; anchored-import/shared helper,
`cfcb9955d14499f93fb0f80df21d47472640bbb4` /
`c81cc3e6bd3ddafc3a7bb78eea671d05e32fee0e`.

New **CARD-1538**, ID `52d1d81f-1f83-426a-a1a6-8f4bea4fc3e1`, Backlog Normal/Soon:
use the established owned-Git-root cleanup helper at the direct/shared audit sites. Clear
ReadOnly only within proven fixture-owned, non-reparse scratch roots; preserve body errors
and permanent cleanup failures. CARD-1344 is Done and repaired other explicitly named
fixtures; these later audit sites were outside its scope. No production audit change needed.

## Duplicate checks and evidence integrity

Completed `scripts/card.ps1 search <exact method> -Board Antiphon -All` for every distinct
method in the table, then searches for `C1205`, `TurnEndGapRestartDeliveryTests`,
`CleanupRetry`, `unregistered`, `index_lock_path_error`, `index lock`, `LandNonCodeSkipTests`,
`graph manifest`, `read-only Git` and `Git objects`. Read full relevant records:
CARD-1492/1464/1515/1529/1487/1205/1385/1390/1400/1344/0543. Existing mechanisms A/B/C
remain on 1515/1492/1529. New cards cover D/E/F only; creation returned Backlog and no card
file sync. CARD-1464's and CARD-1492's broad C544 attribution is superseded for this exact cell
by the observed uncertain-brief reason and the producer ordering in C.

| Current evidence file under R | SHA-256 |
|---|---|
| `antiphon-all-group-3.trx` | `16333bcaf2b0408bb5e7d823d5286036f9aed662fd7aad57683cc691d99009c7` |
| `antiphon-all-group-13.trx` | `8b2e8fa53fef9c9c93ab568e3670924bad324141867e63f61096ff3bb98a7c14` |
| `antiphon-all-group-14.trx` | `ebe1dc34b6562f68b1662ac02a032badde2489ad1848df0b14187e7f029c0cdd` |
| `antiphon-all-group-15-tests.log` | `b88fc8f5fc04cfc68ec74465e26236d8052d2284287e9e753cbf6ab82d5c2201` |
| `antiphon-all-group-15-execution/log_261023055003445.diag` | `b44bcb510add34704c9d05afd216d28094a56e8e1a6a0fb215589662a0c68ef5` |

Ignored board snapshots/card drafts are local working evidence in
`.antiphon/debug-c2afd605/`; durable conclusions and receipt provenance are preserved here.

## Exact recorded failures and stacks

The following extracts preserve ErrorInfo from the three TRX files and failed-case blocks
from the group-15 console. TUnit itself omits internal frames. They are historical test
output, not executions performed by this task.

### Group 3: Dispose_awaits_exit_of_its_running_host_and_descendants

UTC 2026-10-23T02:24:13.0305977+00:00 to 2026-10-23T02:24:18.5238722+00:00.

```text
UnauthorizedAccessException: Access to the path 'Antiphon.Agents.Pty.dll' is denied.
at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
   at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
   at Antiphon.Tests.Agents.OwnedPtyFixture.DeleteRoot() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\DirectSessionRunnerClientDisposalTests.cs:line 366
   at Antiphon.Tests.Agents.DirectSessionRunnerClientDisposalTests.Dispose_awaits_exit_of_its_running_host_and_descendants() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\DirectSessionRunnerClientDisposalTests.cs:line 167
   at Antiphon.Tests.Agents.DirectSessionRunnerClientDisposalTests.Dispose_awaits_exit_of_its_running_host_and_descendants() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\DirectSessionRunnerClientDisposalTests.cs:line 167
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 13: C1205_Watchdog_delivers_while_event_stream_remains_healthy

UTC 2026-10-23T04:53:24.5030270+00:00 to 2026-10-23T04:53:44.0159144+00:00.

```text
ShouldAssertException: await h.ProbeAsync(); h.RecoveryOutcomes.Last()
    should be
"CheckedWorking"
    but was
"Unknown"
    difference
Difference     |  |    |    |    |    |    |    |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    9    10   11   12   13   
Expected Value | C    h    e    c    k    e    d    W    o    r    k    i    n    g    
Actual Value   | U    n    k    n    o    w    n                                       
Expected Code  | 67   104  101  99   107  101  100  87   111  114  107  105  110  103  
Actual Code    | 85   110  107  110  111  119  110
at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Watchdog_delivers_while_event_stream_remains_healthy() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 85
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Watchdog_delivers_while_event_stream_remains_healthy() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 96
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 13: C1205_Busy_tail_keeps_completion_obligations_pending

UTC 2026-10-23T04:54:01.8878125+00:00 to 2026-10-23T04:54:10.3320601+00:00.

```text
ShouldAssertException: h.RecoveryOutcomes.Last()
    should be
"CheckedWorking"
    but was
"Unknown"
    difference
Difference     |  |    |    |    |    |    |    |    |    |    |    |    |    |    |   
               | \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  
Index          | 0    1    2    3    4    5    6    7    8    9    10   11   12   13   
Expected Value | C    h    e    c    k    e    d    W    o    r    k    i    n    g    
Actual Value   | U    n    k    n    o    w    n                                       
Expected Code  | 67   104  101  99   107  101  100  87   111  114  107  105  110  103  
Actual Code    | 85   110  107  110  111  119  110
at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Busy_tail_keeps_completion_obligations_pending() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 168
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Busy_tail_keeps_completion_obligations_pending() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 176
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 13: C1205_Restart_delivers_five_gap_held_completions_once(False)

UTC 2026-10-23T04:54:58.3893287+00:00 to 2026-10-23T04:55:01.4804631+00:00.

```text
ShouldAssertException: Caller.SubmittedBodies.Count
    should be
5
    but was
0
at Antiphon.Tests.TestHelpers.TurnEndGapDeliveryHarness.AssertReceiptsAsync(List`1 before, List`1 queued, Int32 expectedAttempts) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\TestHelpers\TurnEndGapDeliveryHarness.cs:line 312
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once(Boolean cache) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 37
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once(Boolean cache) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 45
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 13: C1205_Restart_delivers_five_gap_held_completions_once(True)

UTC 2026-10-23T04:55:06.6944522+00:00 to 2026-10-23T04:55:09.8396496+00:00.

```text
ShouldAssertException: Caller.SubmittedBodies.Count
    should be
5
    but was
0
at Antiphon.Tests.TestHelpers.TurnEndGapDeliveryHarness.AssertReceiptsAsync(List`1 before, List`1 queued, Int32 expectedAttempts) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\TestHelpers\TurnEndGapDeliveryHarness.cs:line 312
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once(Boolean cache) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 37
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once(Boolean cache) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\TurnEndGapRestartDeliveryTests.cs:line 45
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 13: C544_BriefHandoffRecovery

UTC 2026-10-23T04:57:08.2364857+00:00 to 2026-10-23T04:57:23.2656157+00:00.

```text
ShouldAssertException: task.Status
    should be
AgentTaskStatus.Dispatched
    but was
AgentTaskStatus.Blocked

Additional Info:
    warm inline busy=False: dispatch_brief_evidence_uncertain: current brief evidence is not a complete unattempted row or a complete receipt.
at Antiphon.Tests.Application.VerificationRoundDeliveryTests.C544_BriefHandoffRecovery() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\VerificationRoundDeliveryTests.cs:line 171
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.VerificationRoundDeliveryTests.C544_BriefHandoffRecovery() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\VerificationRoundDeliveryTests.cs:line 251
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 14: C448_V18_CleanupRetryPreservesChangedWork(unregistered)

UTC 2026-10-23T05:21:12.7763011+00:00 to 2026-10-23T05:22:31.7313511+00:00.

```text
ShouldAssertException: retry.Mode
    should be
LandOperationMode.CleanupRetry
    but was
LandOperationMode.Fresh
at Antiphon.Tests.Application.AgentTaskLandCleanupSafetyTests.C448_V18_CleanupRetryPreservesChangedWork(String change) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\AgentTaskLandCleanupSafetyTests.cs:line 162
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Application.AgentTaskLandCleanupSafetyTests.C448_V18_CleanupRetryPreservesChangedWork(String change) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\AgentTaskLandCleanupSafetyTests.cs:line 165
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff (36s 988ms)

```text
failed C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff (36s 988ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Verifier.Calls
      should be
  0
      but was
  1
    at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:2709
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:259
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:261
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1385_UnrelatedScriptChangeStillSkips (58s 183ms)

```text
failed C1385_UnrelatedScriptChangeStillSkips (58s 183ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Verifier.Calls
      should be
  0
      but was
  1
    at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:2709
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1385_UnrelatedScriptChangeStillSkips() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:271
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1385_UnrelatedScriptChangeStillSkips() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:275
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1390_UpdateRefusesDocsMarkdownInput (4s 908ms)

```text
failed C1390_UpdateRefusesDocsMarkdownInput (4s 908ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path '5436f699aebcbc4936bd97772b608fc52136f5' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1390_UpdateRefusesDocsMarkdownInput() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:352
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1390_UpdateRefusesDocsMarkdownInput()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1400_UpdateRefusesCaseVariantDocsInputs (2s 522ms)

```text
failed C1400_UpdateRefusesCaseVariantDocsInputs (2s 522ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path 'cab8569e60cd993ff6c927fa838ff67fdf533f' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesCaseVariantDocsInputs() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:379
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesCaseVariantDocsInputs()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1400_UpdateRefusesImportedPropsDocsInclude (3s 222ms)

```text
failed C1400_UpdateRefusesImportedPropsDocsInclude (3s 222ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path '68d84c466e9d2101aef415c4d2dd91bf3acb52' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesImportedPropsDocsInclude() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:413
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesImportedPropsDocsInclude()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1400_UpdateRefusesPercentEncodedDocsInclude (2s 315ms)

```text
failed C1400_UpdateRefusesPercentEncodedDocsInclude (2s 315ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path '44f9908057c3e71945558b2bce8ee1c9e1e7b6' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesPercentEncodedDocsInclude() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:465
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesPercentEncodedDocsInclude()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1400_UpdateRefusesAdditionalFilesDocsInclude (3s 719ms)

```text
failed C1400_UpdateRefusesAdditionalFilesDocsInclude (3s 719ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path 'ee67f20a0ff13c65b896c855cddc7d8a0f7cc4' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesAdditionalFilesDocsInclude() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:490
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_UpdateRefusesAdditionalFilesDocsInclude()
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1380_RegularDocumentationChangesSkip(empty) (37s 060ms)

```text
failed C1380_RegularDocumentationChangesSkip(empty) (37s 060ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Verifier.Calls
      should be
  0
      but was
  1
    at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:2709
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1380_RegularDocumentationChangesSkip(rename) (1m 23s 415ms)

```text
failed C1380_RegularDocumentationChangesSkip(rename) (1m 23s 415ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Verifier.Calls
      should be
  0
      but was
  1
    at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:2709
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1400_RepairAnchoredImportRefusesDocsInclude (2s 412ms)

```text
failed C1400_RepairAnchoredImportRefusesDocsInclude (2s 412ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path '68d84c466e9d2101aef415c4d2dd91bf3acb52' is denied.
    at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.AssertUpdateRefusesWithEntriesAsync(IReadOnlyDictionary`2 files, IReadOnlyList`1 entries, String[] needles) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:2788
    at Antiphon.Tests.Application.LandNonCodeSkipTests.AssertUpdateRefusesWithEntriesAsync(IReadOnlyDictionary`2 files, IReadOnlyList`1 entries, String[] needles)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1400_RepairAnchoredImportRefusesDocsInclude() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:497
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1380_DocsOnlyRebasedLandRecordsNonCodeSkip (34s 973ms)

```text
failed C1380_DocsOnlyRebasedLandRecordsNonCodeSkip (34s 973ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Verifier.Calls
      should be
  0
      but was
  1
  
  Additional Info:
      a docs-only rebased candidate must not invoke the verifier or its build slot
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_DocsOnlyRebasedLandRecordsNonCodeSkip() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:33
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_DocsOnlyRebasedLandRecordsNonCodeSkip() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:44
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Group 15: C1380_RegularDocumentationChangesSkip(README) (1m 15s 193ms)

```text
failed C1380_RegularDocumentationChangesSkip(README) (1m 15s 193ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Verifier.Calls
      should be
  0
      but was
  1
    at Antiphon.Tests.Application.LandNonCodeSkipTests.LandAndAssertAsync(LandingSafetyHarness h, Boolean build, String filter, String target) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:2709
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Application.LandNonCodeSkipTests.C1380_RegularDocumentationChangesSkip(String variant) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Application\LandNonCodeSkipTests.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

## Final snapshot and release verdict

Final read-only inventory at **2026-10-23 07:11:26 UTC** still contains TRX groups 1-14
only. Execution directories exist through group 16 and none later. Group 15 remains
312 Passed / 12 Failed / 1 InProgress in its retained diagnostic; group 16 has 302 Passed /
0 Failed / 1 InProgress at this cutoff, with no final TRX. No additional red was found.
PID 209072 was still present at the closing process observation. Future results from this
live run require a later evidence update; incomplete groups are not counted as passed.

**Release verdict: NOT RELEASABLE.** All 19 observed reds block the Windows qualification
as recorded: group 3 (1), group 13 (5), group 14 (1), group 15 (12). Group 15's absent
completion, group 16 still running, and later unexecuted/unobserved selections also leave
coverage incomplete. Product gates specifically require CARD-1529 warm-handoff admission
and CARD-1536 cleanup admission to be resolved; fixture reds require CARD-1515, CARD-1492,
CARD-1537 and CARD-1538 repairs with their appropriate exact native/portable evidence.
Historical Linux greens, source equality and safe preservation do not waive the red gate.
Land this diagnosis only; commission the repairs separately and keep the protected run
untouched. No production timeout increases or retries are proposed.

--- next stage ---
next: land
handoff: Land this docs-only diagnosis. Use existing CARD-1515/1492/1529 and new Backlog CARD-1536 (High), CARD-1537 and CARD-1538 for separate focused repair tasks. Preserve the live qualification; its 19 reds and incomplete coverage do not authorize release.
artifact: docs/investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md
