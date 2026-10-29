# Review b19c825e: CARD-1514 S1 round 3 (subject b2f1fed3 @ d12ccc80c)

Verdict: **not landable. 3 defects.**

- Every round-2 defect is closed except two residues:
  - D-6 call sites: a second discovery call site and the planning-phase writes still survive mutation.
  - D-7 OS witness: the declared witness is real code, but it runs only conditionally and asserts behaviour that did not change in S1.
- A4 is truly unwired. The native image fix is in the tip.
- All 23 author mutations (M1-M10, M7a/b, M8a/b, N1-N3, N7, N8) are killed. My 5 new mutations, covering the 3 commissioned scenarios, are all killed.
- The join probe holds.

Scope: **Interim**, as the brief commissioned, even though the profile says Final. These are portable Linux rows only. Every native row and every PC stays pending.

- Reviewed `d12ccc80c3d75b9bc5d235a3dc44dcc9f12faf9d` (code `e885398117c035156d33d5011584538c7adb1ac5`).
- Slice base rc/2026-10-27 `8edda999623a473be2d1970727c172bfb5088d59`. Round base `6394a4e53`.
- The tree was clean before and after.
- Code landing owner: **b2f1fed3** (b2f1fed3-fca9-42ea-8699-5c604ece9e7b).

## Fresh runs (Debian runner, build-slot gated)

The build ran as `build-slot.ps1 -Label review-b19c825e-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rv19/ --property:UseAppHost=false`: lease 182395be, waited 0 s, held 264 s, succeeded.

Every run then used `dotnet exec bin-rv19/Antiphon.Tests.dll --treenode-filter ...` inside a lease:
- rows: 5cd4f7c0 and 17e586af
- mutations: a19798a5, 978181e3 and 155e1ba0

All 33 `bin-rv19` directories were deleted one at a time by literal path.

| Selection | total | passed | failed | skipped |
|---|---|---|---|---|
| CP-1 `RcFullTimingContractTests/*` | 9 | 9 | 0 | 0 |
| CP-5 `(TestTimingRecorderTests*)\|(TestPhaseSummaryTests*)/*` | 26 | 25 | 0 | 1 (`C1514T_PS51AndRerun`) |
| `RcRosterRepairTests/*` | 3 | 3 | 0 | 0 |
| `WindowsUnitPlatformAdmissionTests/*` (C1210) | 5 | 4 | 0 | 1 (`C1210_Windows_linux_only_outcomes_are_exact`) |
| `C1514LandingSplitBudgetTests/*` (C1404, C1577, C1584, C1410, G2865) | 5 | 5 | 0 | 0 |

Notes on these runs:
- **CP-5 is red/incomplete, not green.** The plan needs 26 with 0 skipped, and the author reports this correctly.
- The first RcRoster/census attempt (lease 5cd4f7c0) failed 3/3 and 5/5. I caused that: my long scratch `TMPDIR` overflowed the Unix-socket path limit (`UnixDomainSocketEndPoint` ArgumentOutOfRange). They were re-run with the default temp directory and both were green.
- `check-evidence-diff.ps1 -BaseRef 8edda999 -HeadRef HEAD`: commits=18, entries=4, violations=0.

## Mutations

Setup:
- Scratch copy: `git archive` of the tip, plus the bin-rv19 build copied in, so `RepoRoot()` resolves to the copy.
- Each mutation was restored by `git checkout` and checked with `git diff --quiet`.
- Baseline: 9/9.
- Each mutation ran its exact witness method. M7c, M8c and X1b ran the whole class.

| # | Mutation | Witness | Result |
|---|---|---|---|
| M1 | delete `Add-NightlyOwnerObservation` in Publish | Owner | killed (owner sidecar missing ...) |
| M2 | `if ($rows.Count -eq 0)` -> `if ($false)` | Owner | killed (retained-image-missing; blank-image-not-unknown; rerun-overwrote-owner) |
| M3 | delete both missed->unknown lines | Owner | killed (missed-live-image-not-unknown) |
| M4 | incompatible runtime does `& $Loader` | HostSamples | killed (loader-fault-escaped) |
| M5 | disable the summary re-read | Counts | killed (late rows dropped) |
| M6 | drop `Update-TestPhasePlannedIndex` | Plans | killed (cross-suite-stale-not-joined; index-digest-missing; index-assembly-mismatch) |
| M7a | helper copies the suite constant | Discovery | killed (ordinary-chunk-copied-suite-constant) |
| M7b | call site :1324 copies the suite constant | ProductionCallSites | killed (discovery-not-null) |
| M8a | delete the 4 `Write-NightlyMeasuredPhase` calls in the helper | Setup | killed (counted-more-than-once; setup-seconds=0) |
| M8b | delete the `Write-NightlyPrerequisitePhases` call :1149 | ProductionCallSites | killed (missing-npm-ci/npm-build/lint/dotnet-build) |
| M9 | drop the `Write-TestPhaseOwnerObservation` call | Owner | killed |
| M10 | orphan Passed stays Passed | Counts | killed (orphan-outcome=Passed; orphan-not-incomplete) |
| N1 | swallow the loader fault without `loader-failed` | HostSamples | killed (loader-fault-escaped) |
| N2 | owner sidecar to `GetTempPath()` | Owner | killed (sidecar missing) |
| N3 | constant attempt key and no exists-guard | Owner | killed |
| N7 | fresh stopwatch per shard | Budget | killed (second-shard-reread) |
| N8 | per-suite plan selection disabled | Plans | killed (cross-suite-stale-not-joined) |
| X1a (new) | owner sidecar written to `$LogRoot` (outside the timing root) | Owner | killed (sidecar missing) |
| X1b (new) | `setup-phases.json` written to `$LogRoot` | class | killed (Setup and ProductionCallSites) |
| X2a (new) | rerun overwrites the first attempt's owner file (attempt dropped from the name, guard removed) | Owner | killed (retained-image-missing; rerun-overwrote-owner) |
| X2b (new) | attempt dropped from the name, guard kept (second attempt lost) | Owner | killed (rerun-overwrote-owner) |
| X3 (new) | per-suite plan file shared (`groups-planned-all.json`), second suite overwrites it | Plans | killed (cross-suite-stale-not-joined) |
| **M7c** (new) | **supplemental call site :1457** `$chunkDiscoverySeconds = [double]$suiteDiscoverySeconds` | whole class | **survived (9/9)** |
| **M8c** (new) | delete both `Write-NightlyMeasuredPhase -Name $planningName` (:1251, :1263) | whole class | **survived (9/9)** |

## Probes

**P1: the three-suite production-like join**
- Setup (tip scripts, scratch output):
  - antiphon->Antiphon.Tests, scripts->Antiphon.Tests and e2e->Antiphon.E2E, planned in production order through `Write-TestPhasePlannedManifest`. The singleton is e2e.
  - The index carries all three suites.
  - One genuinely stale antiphon shard (`run-OLD`), with a TRX row for `stale-case`.
  - Executed via `Write-TestPhaseExecutedManifest`, then `Invoke-TestPhaseRunSummary`.
- Results:
  - antiphon-case, scripts-case and e2e-case are each `Passed joined=True`.
  - stale-case is `unknown joined=False incomplete=True`.
  - Quality is `stale-input-not-joined, missing-phase-unknown`, `complete=False`.
- **The antiphon row does not drop, and the stale shard is flagged.**

**P2: the OS-witness checker**
- With the author's ignored JSON (copied read-only from the author's mirror into scratch; sha256 `2e454caf...`, head d12ccc80c, base 6394a4e5): exit 0, `NATIVE pending W-A1-owner`, `NATIVE pending restartImpact`.
- With the JSON missing: exit 2.
- With `-BaseRef 8edda999` (slice base): refused, `SHA mismatch`.
- The untracked JSON is the repo convention (docs/testing-and-build.md:652) and is not a defect.

## Defects (Code landing owner: b2f1fed3)

**D-1 Two production call sites still survive mutation (residue of round-2 D-6).**
- Where:
  - `scripts/lib/nightly-tests-impl.ps1:1456-1457` is the supplemental-launch discovery site. Round 2 named it ":1732" beside ":1599".
  - `:1251` and `:1263` are the `planning-<suite>` setup-phase writes.
- Failure:
  - M7c makes a supplemental-launch chunk record the suite constant, which is the round-1 bug on the fallback path.
  - M8c drops every `planning-<suite>` phase, so the 3.2-minute planning gap (plan:96, "record planning and npm/build/lint start/end") is never persisted.
  - CP-1 stays 9/9 for both. `ProductionCallSitesPersistDiscoveryAndSetup` drives only `-Suites @('scripts')` without profile discovery or the supplemental fallback. `DiscoveryIsNotCopiedFromSuite` tests only the helper.
- Why: both are A5/A6 deliverables (plan:74 and :96), and the brief requires call-site mutations to be killed.
- Fix:
  - Through the existing `-SeamsPath` harness, drive a suite that takes the profile-discovery path, plus a chunk that takes the supplemental discovery fallback.
  - Assert that `setup-phases.json` contains `planning-<suite>` with nonzero seconds.
  - Assert that the supplemental chunk's `discoverySeconds` is the marker value, not the suite constant.
  - Re-run M7c and M8c.

**D-2 The declared OS witness is conditional and does not test any S1 change (residue of round-2 D-7).**
- Where:
  - `scripts/test-nightly-native.ps1:272-284`: `Invoke-RcWindowsNativeProcessOwner` is called only inside `if ($packaged.Count)` in `C1039_PowerShellDescendantCustody`.
  - The declaration's W-A1-owner binds that function. Its nativeObligation command runs only `-Case C1039_PowerShellDescendantCustody`.
- Failure:
  - On a Windows host without the Store-packaged pwsh alias, the function is never called and the case passes. That is a passed skip of the declared witness.
  - When it does run, it asserts the packaged-root refusal, which already existed at 8edda999.
  - None of the S1 OS-gated changes is asserted by the declared witness:
    - `StampOwnerObservation` (`GetProcessTimes` -> `RootExitedAtUtc`, `DescendantExitedAtUtc`, `JobEmptyObserved`; nightly-owned-process.cs:335-374)
    - the image cache and `RecordMissed` in `CaptureDescendants` (:393-403)
  - A mutation of those leaves the witness green.
  - The only S1 native assertion, "descendant image survives process exit" (:217-222), is in `C1039_DescendantExit`, which the declared command never runs.
  - No authored test drives a real `NativeProcessOwner` for `RootExitedAtUtc`, `MissedDescendantPids` or `JobEmptyObserved` (CP-2 `RcFullNativeTimingTests` is unauthored).
- Why:
  - docs/testing-and-build.md:630-636 requires the witness to drive the changed object and assert its observable result.
  - The plan now names this call "S1's OS witness".
- Fix:
  - Bind W-A1 to an unconditional, declared function called from `C1039_DescendantExit`, or from CP-2 `RcFullNativeTimingTests` once authored. It must assert `DescendantImages`, `RootExitedAtUtc` and `MissedDescendantPids`/`JobEmptyObserved` from a real `NativeProcessOwner` run.
  - Point the nativeObligation command at that case.
  - Until then, report the S1 OS witness as pending/incomplete.

**D-3 Over-claims.**
- Commit `faea17c7a` says "bind discovery and setup call sites". Only one of two discovery sites and the prerequisite call are bound (D-1).
- Commit `ac4bebc11` ("from a live witness") and the plan's new S1 OS-witness sentence overstate D-2.
- The report's OS-witness digest `38f8e729...` (head e885398) no longer identifies the ignored JSON on disk (`2e454caf...`, head d12ccc80c). The declaration's base is the round base 6394a4e5, not the slice base, so it does not check against 8edda999.
- Accurate claims: CP-5 is correctly not called green, A4 is correctly not an S1 acceptance, and the owed-native list is complete.
- Fix: correct these in the next report and plan note. Do not amend pushed commits.

## Checked and sound

- **Round-2 D-1 (native image fix):**
  - 63f0b8529 and 3e8c8ca18 are `-x` cherry-picks with patch-ids identical to dbbd3ff0a and 22ced2b4b.
  - test-nightly-native.ps1 gained only the 43rd assertion plus the wrapper call. The existing assertions are unchanged.
- **Round-2 D-2/D-3/D-4 (A4):** removed.
  - No production script references `Start-/Stop-NightlyHostSampling`, `qualification-host-stats.ps1`, `Antiphon.SessionRunner.dll` or host-samples.
  - The only `Add-Type -Path` in the lib is the pre-existing `$PSHome` Newtonsoft load.
  - `qualification-host-stats.ps1` is headed NOT WIRED.
- **Round-2 D-5:** `SummaryReReadSharesOneBudget` kills N7.
  - The budget is one stopwatch plus 8 MiB total, bounded at 5 s plus one parse overshoot (test-phase-evidence.ps1:1122-1161, 1483-1489).
- **Outcome safety:**
  - Every new production call is either inside an existing try/catch that writes only `phase-summary-failed` (Publish :1394, plan manifest :1290, summary :1664), or is internally total:
    - `Write-NightlyMeasuredPhase` catches everything.
    - `Write-TestPhaseOwnerObservation` catches everything.
    - `Resolve-NightlyProductionChunkDiscovery` is pure arithmetic.
  - The planning write sits inside the discovery try but cannot throw.
  - `discoverySeconds` null reaches only the timing JSON, and no other script reads it.
  - timing/summary.json has no gate consumer.
- **Bounds:** no new waits or loops.
- **Secrets:** persisted fields are pids, image basenames, timestamps, numbers, digests and filter strings. There are no command lines or environment.
- **Files:** everything resolves through `Get-TestPhaseTimingDirectory`, which honours `$script:QualificationTimingRoot`. X1a and X1b confirm that.
- **PowerShell rules:** the 6 changed script files have 0 non-ASCII bytes. The added .ps1 lines have no `??`, `?.`, `&&`/`||`, ternary, `-AsHashtable` or `HashData`.

## Merge notes

- **rc/2026-10-27 = ade54222.** It contains 8edda999 plus 6 commits that touch no S1 file. `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` is clean.
- **master = 0e7dd3905.**
  - There are the same 6 conflicts as before:
    - nightly-tests-impl.ps1 and test-phase-evidence.ps1
    - 4 inherited rc-vs-master paths: `.antiphon/task-af2b78c8.md`, the card-1584 investigation, CapacityRecoveryService.cs and CapacityLockOrderTests.cs
  - The S1 files are byte-identical between 8edda999 and master, so a clean port is master's content plus exactly this branch's S1 hunks.

## Advisories

- Supplemental discovery still records suite + supplemental in one number (plan:74 says "separately"), which double-counts suite discovery against `planning-<suite>`.
- These carry over from round 2:
  - The 200 ms inline read has no aggregate.
  - A corrupt index throws to the callers' catch, so diagnostic `summary.json` is lost.
  - The Windows +0.34 s per-chunk persistence cost against the plan's <=2% check is owed natively.
- Owed native rows, all pending:
  - CP-5 `C1514T_PS51AndRerun`
  - CP-2 (14; `RcFullNativeTimingTests` not authored)
  - CP-3 and CP-4 (not authored)
  - the `C1210` Windows case
  - `test-nightly-native.ps1` C1039 (43 assertions)
  - OS witness execution and restart activation
  - CP-6 is deferred with A4.
- Reviewer side effects:
  - A 667 MB scratch copy is left in this session's scratchpad.
  - Mutation temp files went to scratch via `TMPDIR`.
  - Nothing outside scratch was touched. Round 2's `/tmp/owner-*.json` was left alone.
