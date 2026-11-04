# task-0e8a1b50: land-graph re-record + rc/2026-11-06 cut

Outcome: both parts done. Master re-recorded and fast-forwarded to M2; rc/2026-11-06 cut at M2.

## SHAs
- Master before Part A: 1ef88240b3c5ee2c759db958e5fb0b635b1248cc (pinned via ls-remote at start; unchanged at push time)
- Master after Part A (M2): 5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a (plain fast-forward, first push attempt)
- Cut SHA: 5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a
- Candidate id: rc-2026-11-06-5a95ae7a5611 (from Test-ReleaseGateCandidateRef -Ref rc/2026-11-06 -Sha <M2>)
- ls-remote after cut: `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a refs/heads/rc/2026-11-06` (equals M2); rc/2026-11-05 still 1470340559889c2985ea722173181ea25c51b47c (untouched)

## Part A evidence
1. Why it was stale: the digest is SHA-256 over the ls-tree rows of definition paths (Test-DefinitionPath). I diffed those rows between 919d8d42f, where 266595CB was recorded, and 1ef88240b. 69 rows each side, and exactly one row differs: `server/Antiphon.Server.csproj` blob 171f017d3 -> 7e9e56d7b. The blob diff is the single line `<PackageReference Include="LibGit2Sharp" Version="0.32.0" />` from 40c2ab63c, the only commit touching that file in the range. No other csproj, props, targets, global.json or Aspire input changed. Precheck -Check printed `digest=6536D70E936DB342C7254DF59E08062E1E20D968AD4FBC1829839D29B6E53D20 count=69` / `manifest does not match the selected graph`.
2. Evaluator build: `dotnet build tools/Antiphon.LandGraphAudit -c Release -p:UseAppHost=false`, one lease, 0 warnings and 0 errors, 4 s.
3. `-Update -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` printed `phase=pass2` / `approved` and exited 0 after 33 s.
4. git status showed only LandVerificationBuildGraph.manifest.txt and LandVerificationBuildGraph.evaluation.txt, both in server/Application/Services/. Line 1 of each changed 266595CB.. -> 6536D70E..; SDK 10.0.403, the pins blob 97151190 and count 69 are unchanged.
5. -Check was approved twice, exit 0: once before the commit and once at the committed M2.
6. Commit 5a95ae7a5 `chore: land graph digest for the LibGit2Sharp reference (operator-approved re-record)` touches only those two files, and its message ends with the operator quote `Operator: 'Re-record it'`.
7. Guards, --no-build after one build, one lease each, with tests/Antiphon.E2E/TestOutput/Logs/run.log present. C1385_RegistryMatchesNarrowGraph, C1483_S4_RealRepositoryApproves and C1483_S5_CheckRequiresEvaluationRecord each passed 1/1. They ran twice: once in Part A and again in Part B. run.log and the two directories I created were removed afterwards by literal path.
8. Pushed 1ef88240b..5a95ae7a5 HEAD -> master with no retry needed.

## Part B prechecks
- A0 name: Test-ReleaseGateCandidateRef in scripts/lib/release-gate.ps1, used by release-cut.ps1 and release-candidate.ps1, accepts rc/2026-11-06 with Ok=True and Kind=long-lived. No script checks the date against today or refuses a future date. Per docs/release-gates.md the pre-release tag is dated by the publisher's UTC clock, not by the branch name. No script refuses the new name.
- A1: ls-remote rc/2026-11-06 was empty before the push.
  - git cherry M2 origin/rc/2026-11-05: 0 '+', 0 '-'; it is an ancestor of M2.
  - rc/2026-11-04: 0 '+', 0 '-'; ancestor.
  - rc/2026-11-03: 0 '+', 0 '-'; ancestor.
  - rc/2026-11-01: 0 '+', 24 '-'. Every rc-only commit there is already on master by patch-id, so the cut loses nothing.
- A4:
  - Land-graph -Check at M2 is approved at digest 6536D70E..5D20, count 69.
  - The live-lane `-PrintInputDigest` pin is 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7, as expected.
  - Get-NightlyPolicyHash through Read-NightlyExecutionPolicy on tests/test-execution-policy.json gives 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4, as expected.
  - test-rc-platform -Case LinuxOnlyWindowsExclusionPolicy: 6/6 PASS. -Case C1686_LinuxOnlyCensus: 49/49 PASS.

## Tree verification at M2
- Whole-solution build: `dotnet build Antiphon.sln -p:UseAppHost=false`, one lease. 0 errors and 1831 warnings. **NETSDK1047: 0 occurrences.** Build wall time was 3m28s (lease held 209 s).
- Smoke runs, all --no-build and one lease each. Every run had 0 failures:
  - The three land-graph tests: 1/1 each.
  - ReleaseGatePlatformTests + LiveLaneRunnerTests + LiveLaneFingerprintTests: 17/17.
  - WindowsUnitPlatformAdmissionTests/C1210_*: 6 total, 5 passed (C1210_Roster_closure_matches_source_gates among them) and 1 skipped. The skip is C1210_Windows_linux_only_outcomes_are_exact, which requires Windows placement by design.
  - C1514LandingSplitBudgetTests* (C1584 roster): 5/5.
  - TestClassificationPolicy* | TestClassificationGuard* | SlowTestRegistryMerge* | SlowTestTripwire*: 47/47.
  - C0824DesktopLegacyReleasePolicyTests* + C0824DesktopLegacyReleaseSettingsTests*: 152/152.
  - TestOwnedPtyHost*: 42/42.
  - LandingRemovalPolicyControlTests*: 72 total, 70 passed and 2 skipped. C721 and C665 skip because they test Windows sharing-mode locks.
  - RcQualificationWatcherScriptTests*: 2/2.
  - Nothing else was run; no whole Unit lane.

## What the new candidate has that rc/2026-11-05 (1470340559) lacked
68 commits, 72 files, +4153/-171. By area:
- server: 17 files (+479/-33)
- src: 2 files (+17/-10)
- tests: 38 files (+3026/-87)
- scripts: 11 files (+602/-31)
- docs: 3 files (+21/-10)
- client and tools: nothing

Product-affecting commits:
- **Operator in-process libgit2 reads.** 40c2ab63c (LibGit2Sharp PackageReference, InProcessGitReads, LandingDtos), 4a4945b6c (per-call reads, git-style real paths, LandingGit) and 5449f8f50 (packed-refs sharing on delete, commondir). This is their first rc; Windows qualification will exercise them for the first time.
- dd30b167a: the land resolves the sibling tip before `git cherry` (DelegationWorktreeService).
- **CARD-1637 merge-branch refusal.** e23a6a10b refuses a merge-commit branch whose plain rebase conflicts via the Merge-role path. cc1f4cc1e caps the conflicted list in the event Detail at 3400 characters. f38c2c829 keeps the clip from splitting a surrogate pair.
- **CARD-1705 scanner lock wait.** b48989fa6 and d4c88ddde bound the land-notification scan's wait on a held caller queue lock to at most 1 s (AgentTaskLandNotification*, SessionMessageQueueService, LegacyCheckNotePublicationService).
- **CARD-1706.** d6fac5e7d bounds the conflict summary in the reply-merge Conflicted event and the cap-hit header (AgentTaskReplyService).
- **V32 headline wording.** ccd7bd7d3 makes an unenqueued land notice read 'not yet enqueued' instead of 'missing queue row' (AttentionService; rc-766e3df5).
- **CARD-1701.** 6b0e2f9da (DesktopLegacyReleasePolicyService): a refused filing backs off, base-ref spellings are normalised, and an unknown key refuses at startup.
- **C1702** (SessionRunner). 04022c3ec: park proofs run Git in the checkout the ownership check admitted. HerdrPaneChild refuses an unresolvable workspace.
- **CARD-1698 toolchain floor warnings.** 28f2c5302 adds scripts/lib/toolchain-floor.ps1 and wires it into nightly-run-impl, nightly-tests-impl and session-runner-restart-health. It only warns and never refuses, so the rc qualification path will print warnings.
- **Land-graph re-record.** 5a95ae7a5.

Already in rc/2026-11-05, not new here: the CARD-0824 S2 rows and the S4a feature (3eb9d0027, automatic desktop worktree release, ON by default). The S1c/S2/S4a commits in this range are tests only.

Test-only: CARD-0824 S2/S4a mutation witnesses and timeouts, group-79 TestOwnedPtyHost manifest-read retry (e718850bd is in the test project), CARD-1707 census fixtures, CARD-1694 C1323 census fix (scripts/test-rc-platform.ps1), group-125 C1584 roster rows, CARD-1620 timing labels and slow-class registry, CARD-1703 verifier write sequence, CARD-1677, group-128 C1667, c448 V36.

The full list is in `git log --oneline 1470340559..5a95ae7a5`.

## Not done (not mine)
No tag, no publish or promote, no -Land, and no host activation. The Windows run restart is a separate task.
