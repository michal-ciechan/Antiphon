# Review a45b2ddc: CARD-1514 S1 round 4 (subject 00b93ae2 @ 9f9a5aaa7)

Verdict: **not landable. 3 defects.** Two are small: a cheap missing witness and evidence that only needs re-anchoring. The third is a timestamp corruption that S1 introduced in its own setup-phase evidence.

- Round-3 D-1, D-2 and D-3 are closed.
- All 19 round-4 mutations are killed when I re-run them. M1 to M10, N1 to N3, N7 and N8 are inside that set.
- The admitted planning SUCCESS-write survivor (M8d) is real, and a witness for it is cheap.
- The OS inventory does not hold at the slice base.

Scope: **Interim**, as commissioned, even though the profile says Final. These are portable Linux rows only. Every native row and every PC stays pending.

- Reviewed `9f9a5aaa73c8dc80b57420ff6b129d33684a6088` (code `ffa12a9c177c97d53e8d1b672d5d66ac93e98574`).
- Slice base: `8edda999623a473be2d1970727c172bfb5088d59`.
- Subject: `00b93ae2-42c3-4b35-b290-6b42dca3724a`. The prior Code landing owner named in round 3 was b2f1fed3-fca9-42ea-8699-5c604ece9e7b.
- The tree was clean before and after.

Standing authority used: the rc-run timing question. I asked for no other approval.

## Fresh runs (server2/Linux, build-slot gated)

The build ran as `build-slot.ps1 -Label review-a45b2ddc-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rva4/ --property:UseAppHost=false`: lease b7069544, waited 0 s, held 165 s, succeeded. Every run after that was `dotnet exec bin-rva4/Antiphon.Tests.dll --treenode-filter ...` inside a lease:
- mutations: lease 3b23c4a2, held 186 s
- rows and M4b: lease 8ddbbc16, held 164 s

All 34 `bin-rva4` directories were deleted one at a time by literal path.

| Selection | total | passed | failed | skipped |
|---|---|---|---|---|
| CP-1 `RcFullTimingContractTests/*` | 10 | 10 | 0 | 0 |
| CP-5 `(TestTimingRecorderTests*)\|(TestPhaseSummaryTests*)/*` | 26 | 25 | 0 | 1 (`C1514T_PS51AndRerun`, owed native) |
| `RcRosterRepairTests/*` | 3 | 3 | 0 | 0 |
| `WindowsUnitPlatformAdmissionTests/*` | 5 | 4 | 0 | 1 (`C1210_Windows_linux_only_outcomes_are_exact`, owed native) |
| `C1514LandingSplitBudgetTests/*` (C1404, C1410, C1577, C1584, G2865) | 5 | 5 | 0 | 0 |

- CP-5 is red/incomplete, not green: the plan wants 26 with 0 skipped.
- `check-evidence-diff.ps1 -BaseRef 8edda999 -HeadRef 9f9a5aaa7`: commits=22, entries=6, violations=0, exit 0.

## Mutations

Setup:
- Scratch copy: `git archive ffa12a9c1`, plus the bin-rva4 output copied in, so RepoRoot resolves to the copy.
- Each mutation was restored by `git checkout`, and the copy ended with `git diff --quiet`.
- Baseline: 10/10.

| # | Mutation | Witness | Result |
|---|---|---|---|
| M1 | delete `Add-NightlyOwnerObservation` in Publish | Owner | killed (owner sidecar missing; ...) |
| M2 | `if ($rows.Count -eq 0)` -> `if ($false)` | Owner | killed (retained-image-missing; blank-image-not-unknown; rerun-overwrote-owner) |
| M3 | delete both missed->unknown lines | Owner | killed (missed-live-image-not-unknown) |
| M4b | `Test-QualificationHostRuntime`: `$null = $Loader` -> `$null = & $Loader` | HostSamples | killed |
| M5 | summary re-read disabled | Counts | killed (late rows dropped) |
| M6 | `Update-TestPhasePlannedIndex` call removed | Plans | killed (cross-suite-stale-not-joined; index-digest-missing; index-assembly-mismatch) |
| M7a | helper chunk = suite constant | Discovery | killed (1.5 vs 21.377) |
| M7b | :1324 copies the suite constant | CallSites | killed (discovery-not-null) |
| M7c | :1457 `[double]$suiteDiscoverySeconds` | CallSites | killed (supplemental-not-marker) |
| M8a | 4 helper phase writes removed | Setup | killed (counted-more-than-once; setup-seconds=0; per-chunk-build-sum=0) |
| M8b | :1149 prerequisite call removed | CallSites | killed (missing-npm-ci/npm-build/lint/dotnet-build) |
| M8c | both planning writes (:1251, :1263) removed | CallSites | killed (planning-suite-missing; planning-seconds-zero) |
| M9 | `Write-TestPhaseOwnerObservation` call removed | Owner | killed |
| M10 | orphan Passed stays Passed | Counts | killed (orphan-outcome=Passed; orphan-not-incomplete) |
| N1 | loader fault swallowed without `loader-failed` | HostSamples | killed (loader-fault-escaped) |
| N2 | owner sidecar written to `GetTempPath()` | Owner | killed (owner sidecar missing) |
| N3 | constant attempt key and no exists-guard | Owner | killed |
| N7 | `-BudgetState $null` per shard | Budget | killed (second-shard-reread) |
| N8 | index read disabled | Plans | killed (cross-suite-stale-not-joined) |
| **M8d** | delete only the SUCCESS write at :1251 | CallSites | **survived (1/1 green)** |
| X4 (new) | wrap the witness call in `if ($false) { ... }` | Shape | survived |
| X5 (new) | `$false -and` on the images, missed-pids and job-empty asserts | Shape | survived |
| X6 (new) | Start redirected to a nonexistent type, literal kept as a string | Shape | survived |

My first M4 inserted `& $Loader` in `New-QualificationHostStatsProvider`, which the witness never drives with an incompatible host, so it survived. That was my placement error. M4b is the author's mutation and it dies.

## Defects (Code landing owner: 00b93ae2)

**D-1 The planning SUCCESS write is unwitnessed (the admitted survivor M8d).**
- Where:
  - `scripts/lib/nightly-tests-impl.ps1:1251` is the success-path `Write-NightlyMeasuredPhase -Name $planningName`.
  - `RcFullTimingContractTests.cs` CallSiteScript drives only the catch write at :1263, through the timed-out seam.
- Failure:
  - With :1251 deleted, every *healthy* rc run (profile discovery succeeds) writes no `planning-<suite>` row.
  - That is the 3.2-minute planning gap, which the plan says to record (plan:97, "record planning and npm/build/lint start/end").
  - Only failed discoveries would be recorded. CP-1 stays 10/10.
- Why this counts: it is a plan-named A5 deliverable on the production path, and its mutation survives.
- The fix is cheap, and I proved it:
  - The existing `supplemental-seams.ps1` run with `-Profile 'rc'` on `antiphon` takes the success path (`lastPhase=complete`, `planning-antiphon` 0.526 s), and takes about 3 s.
  - With M8d applied, that same probe shows `planning-antiphon` absent.
- Fix:
  - Add a fourth `Invoke-AntiphonNightlyTests` (rc, antiphon, supplemental seam) with its own TimingRoot.
  - Assert that `planning-antiphon` is present with seconds > 0.
  - Re-run M8d.

**D-2 The OS-witness inventory does not hold at the slice base.**
- Where: the ignored `.antiphon/os-witnesses/card-1514-s1.json` (sha256 `2b6c5e58...`) declares `baseSha` 6394a4e53, the round base.
- Failure:
  - I copied it and set `baseSha` to `8edda999`.
  - `check-os-witnesses.ps1 -BaseRef 8edda999... -HeadRef ffa12a9c1...` then exits 1 with `obligations=4`:
    - candidate mismatch at `nightly-owned-process.cs` base:80. The real anchor is base:74.
    - candidate mismatch at `nightly-tests-impl.ps1` base:25. The real anchor is base:22.
    - The other two obligations are the same two anchors reported as unclassified.
  - The changed-path set from 8edda999 is the same 7 source paths, plus 3 more report .md files.
- Why: the slice is S1 from 8edda999. D-21 discloses the round-base limitation, but it does not close it.
- Fix:
  - Regenerate the ignored JSON with `baseSha` 8edda999 and base anchors 74 and 22, then re-run the checker.
  - I verified that this exact edit gives exit 0 (`complete syntax/inventory`, NATIVE pending W-A1-owner and restartImpact).
  - No code change is needed.

**D-3 Setup-phase start/end stamps are corrupted on re-write (new in S1).**
- Where: `scripts/lib/test-phase-evidence.ps1:2741-2753` `Write-QualificationRunPhase`.
  - It re-reads earlier rows with `ConvertFrom-Json`, and pwsh 7 turns ISO strings into `[datetime]`.
  - It then writes them back with `[string]$row.startedAt`.
- Failure:
  - Every row except the last is rewritten as invariant `MM/dd/yyyy HH:mm:ss`, with no zone and only whole seconds. This run's `setup-phases.json` showed `"startedAt":"10/30/2026 00:54:05"` for npm-ci next to ISO `planning-antiphon`.
  - `Get-QualificationSetupAccounting` (:2789) parses that string with `[datetimeoffset]::Parse` as *local* time.
  - Under `TZ=America/New_York` I reproduced a shift: `2026-10-30T00:54:05.5698141Z` became `2026-10-30T04:54:05Z`.
  - On any non-UTC Windows host, the npm-ci, npm-build, lint and dotnet-build intervals shift by the UTC offset relative to the last row. That breaks the A5 start/end record and the `overlapped` check.
  - Linux UTC runners hide it, so `SetupPhasesAreNonoverlapping` cannot go red here.
- Why: it is a plan-named A5 deliverable (start/end), and it is wrong on the hosts the rc runs use. It does not change any outcome, because there is no gate consumer.
- Fix:
  - On re-write, emit `[datetime]` values with `.ToUniversalTime().ToString('o')`, and keep strings unchanged. This stays 5.1-safe.
  - Add a witness that writes two phases and asserts the first row still round-trips as ISO with `Z`.

## Checked and sound

- **Round-3 D-1:** closed. M7c (:1457) and M8c (:1251+:1263) now die, with the texts above. One residue remains: D-1 here.
- **Round-3 D-2:** closed in source.
  - `scripts/test-nightly-native.ps1:204-208` calls `Invoke-RcWindowsOwnerExitWitness` first in `C1039_DescendantExit`, outside any `$packaged` condition.
  - The function is at `scripts/test-rc-speed-os-witness.ps1:6-35` and is dot-sourced unconditionally at :25.
  - It drives `NativeProcessOwner.Start` with the fixture's argv control form (`Program.cs:7-9`), which accepts `(directory, 'descendant')`.
  - It asserts `DescendantImages`, `RootExitedAtUtc`, a non-null `MissedDescendantPids` and `JobEmptyObserved`.
  - Its waits are bounded: 10 s for the pid and the 30 s timeout for completion. The root is registered in `$script:roots`, so the finally releases the child.
  - On Linux, `NightlyNativeOwnershipTests.RunAsync` skips by the repo idiom (`SkipTestException`, "This native checkpoint requires Windows (OwnedChild.exe, pwsh.exe, and a Windows job).").
  - Native execution stays pending.
- **Round-3 D-3:** closed.
  - Plan D-21 (plan:58) and the plan text (plan:238) are accurate. They name the unconditional witness, the `faea17c7a`/`ac4bebc11` over-claims and the round-base limitation, and CP-1 is 10/10.
  - The author's report is accurate. It admits M8d, the round base and the CP-5 skip, and its digest matches the JSON on disk (`2b6c5e58...`).
  - Only commit subject `ffa12a9c1` ("witness ... planning phases") over-states: only the catch write is witnessed. Pushed commits are not rewritten.
- **The whole S1 diff since 8edda999:**
  - Round 4 changed no production file: only the plan, test-nightly-native, the witness script and the test.
  - New production writes are total:
    - `Write-NightlyMeasuredPhase` catches everything.
    - `Write-TestPhaseOwnerObservation` catches everything.
    - Publish, plan and summary calls sit inside the existing catches that write only `phase-summary-failed`.
  - There are no new unbounded waits.
  - Persisted fields hold no command lines and no environment.
  - Files go through `Get-TestPhaseTimingDirectory` (TimingRoot).
  - A4 is absent from production. The only reference to `qualification-host-stats.ps1` is the file itself, which is headed NOT WIRED.
  - The native image fix 22ced2b4b is present: `3e8c8ca18` has an identical patch-id (`2b7122ce...`) and is an ancestor of HEAD.
  - The 6 changed script files contain no non-ASCII characters.
- **Merge:**
  - `origin/rc/2026-10-27` is `ade54222`. Since 8edda999 it has 6 commits, and none touches an S1 file.
  - `merge-tree` HEAD into rc is clean. `merge-tree --merge-base 8edda999` (a cherry-pick of the range) is clean onto both rc and master.
  - A whole-branch `merge-tree` into master `0e7dd3905` conflicts on the same 6 paths. The S1 files at 8edda999 are byte-identical to master, so a clean port is the range pick: master's content plus exactly this branch's S1 hunks. The inherited rc-vs-master `.antiphon`, card-1584, Capacity* paths are not part of S1.

## Advisories

- **A-1:** `NativeOwnerExitWitnessIsUnconditional` checks text only.
  - X4 (`if ($false)` around the call), X5 (asserts neutralised) and X6 survive it, and X4 and X5 would also pass natively.
  - Treat it as a Linux proxy, not as the witness.
  - Consider a check based on the AST: the call is a direct statement of the case scriptblock, and each `throw` is reachable.
- **A-2:** these carry over:
  - Supplemental `discoverySeconds` = suite + supplemental, which double-counts discovery against `planning-<suite>`.
  - The 200 ms inline read has no aggregate.
  - A corrupt planned index drops the diagnostic summary.
  - The Windows ≤2% persistence cost is unmeasured.
  - A non-empty `MissedDescendantPids` kill is owed on Windows.
- **Owed native rows, all pending:**
  - CP-5 `C1514T_PS51AndRerun`
  - CP-2 (`RcFullNativeTimingTests` not authored)
  - CP-3 and CP-4 (not authored)
  - `C1210` Windows
  - `test-nightly-native.ps1` C1039, including the new witness
  - OS-witness execution and restartImpact
  - CP-6 is deferred with A4.
- **Reviewer side effects:**
  - A 667 MB scratch copy and the probe roots are left in this session's scratchpad.
  - Mutation temp files went to scratch via `TMPDIR`.
  - Nothing outside scratch was deleted. The old `/tmp/owner-*.json` files were left alone.
