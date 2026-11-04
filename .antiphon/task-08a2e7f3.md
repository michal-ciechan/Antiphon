# Task 08a2e7f3 (Code, test-only): witnesses for the Mutation f2976027 survivors (CARD-1698 slice 1, CARD-1706)

Branch feat/card-task-08a2e7f3, base master 84e19071e25abc7a595fddfac145b187dc5f2d11.
Tested source SHA: b2b25299265bdf41f9da96d983898ddd6d6203ce (tests only; no production file changed).
Worktree: runner mirror /work/worktrees/task-08a2e7f3 (server2, Linux, pwsh 7).

## Witnesses (each red against the report's mutant, green restored)

| ID | Witness | Mutant | Red | Green |
|---|---|---|---|---|
| B2 | `AgentTaskReplyIntegrationTests.C1706_a_reply_merge_conflict_at_the_task_cap_writes_a_bounded_cap_header` (task at MaxDepth: no Merge child, Blocked, caller note contains exactly `MERGE CONFLICT in <first 20>, ... and 60 more — run at task cap, resolve by hand`, not names[20]) | cap header `{conflictSummary}` -> `{string.Join(", ", outcome.ConflictFiles)}` (AgentTaskReplyService.cs:2567), rebuilt | 1/2 failed (new test, "should contain") | 2/2, and 364/364 class run |
| A1 | `Test-C1698_UnreadableVersion` +3 rows `C1698 Unreadable message carries floor, impact and fix: <case>` (exact message, impact/fix from the manifest) | A8f drop `Fix:`, A8g drop impact, A8h drop floor | 3 new rows fail for each | 6/6 |
| A2 | `C1698_RestartServerLibraryLoadFailure`, `C1698_RestartRunnerLibraryLoadFailure` (test-toolchain-floor), `C1698_RunLibraryLoadFailure` (test-nightly-run, child harness from a copied scripts tree): lib that throws or cannot parse -> same exit, same stop/build/start or result, `check failed` line, hop still runs | A9pRS / A9pSRH / A9pNR: each wrapper try/catch removed alone | 2/2 rows fail each (throw: exit 1; unparsable: no check-failed line) | 2/2 each |
| A3 | `C1698_SummaryJsonRecordsToolchain` (test-nightly-tests): real Invoke-AntiphonNightlyTests run, clone carries the policy universe projects + floor sources, reads `logs/summary.json` from disk | A11b `if ($false) { $preflight.toolchain = ... }` | 2/2 fail (summary present, toolchain missing) | 2/2 |
| A4 | `NightlyScriptsTests.The_three_scripts_are_ascii_only` lists lib/toolchain-floor.ps1, session-runner-restart-health.ps1, ../restart-server.ps1 | A12 em dash in the git impact text | failed: non-ASCII byte at offset 1019 | passed (72/72 run) |
| A5 | `C1698_WindowsPowerShellSyntax`: token/AST scan (ternary, ??, ??=, ?., ?[, &&, ||, ForEach-Object/% -Parallel) of toolchain-floor.ps1, restart-server.ps1, restart-session-runner.ps1, session-runner-restart-health.ps1, nightly-run.ps1, nightly-run-impl.ps1, nightly-tests.ps1, nightly-tests-impl.ps1; 9 flag controls + 1 clean control | A13 ternary, A13b `??` in the lib; extra A13c `&&` in session-runner-restart-health | 1 file row fails each | 18/18 |

Every mutant applied by exact single-occurrence replacement, restored with `git checkout -- <exact path>`, tree clean after each.
A real powershell.exe 5.1 parse on Windows is still owed (the scan is a guard, not a 5.1 parse; nothing ran on the desktop).
No defect found; no production change.

## Runs (one build-slot lease each)

- Build Antiphon.Tests (UseAppHost=false): ok, slot waited 271 s; B2 mutant build ok; restored build ok (waited 165 s).
- C# `(ToolchainFloorWarningTests*)|(NightlyScriptsTests*)|(ReleaseGatePlatformTests*)|(NightlyPrerequisiteTests*)` + guard classes `(EvidenceApprovedFixtureGuardTests*)|(EvidenceDeletionGuardTests*)|(EvidenceDiffGuardTests*)|(EvidenceSupplementalDeletionGuardTests*)|(GitMinimumVersionGuardTests*)|(TestLaneCategoryGuardTests*)`: 72/72 passed, 0 skipped (14+5+6+2 and 45 guard tests; I read "guard classes (47)" as these six classes, TRX holds 45 - name the intended set if different). Held 814 s.
- C# `(AgentTaskReplyIntegrationTests*)|(AgentTaskReplyOverlayTests*)`: 364/364 passed (343+21, includes the new B2 test). Held 238 s.
- Harnesses at the tip: test-toolchain-floor.ps1 all cases 59/0 (34 + 25 new); test-nightly-run C1698_RunPrintsToolchainWarning 4/0, C1698_RunLibraryLoadFailure 2/0; test-nightly-tests C1698_SummaryRecordsToolchain 4/0, C1698_SummaryJsonRecordsToolchain 2/0. (The final tip harness runs and A1-A13 batch: the batch ran in one lease, no builds; the tip harness reruns ran unleased, pwsh only.)
- Unlisted: A12 red run and a pre-mutant C1706 green check (2/2) to avoid wasting a mutant build.
- Pins unchanged: -PrintInputDigest 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7, Get-NightlyPolicyHash 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4; no re-record commit.
- check-evidence-diff base..HEAD: commits=1 entries=0 violations=0. merge-tree origin/master (03bb1e5d1) HEAD: clean.
- No plan/### Checkpoints table for this task: runs are the brief's named selections; no checkpoint tool run.

## Pending

- PCs: none in a plan; every new witness's red/green above is Code-stage evidence, Mutation still owns method-scoped confirmation.
- Owed: real powershell.exe 5.1 parse of the eight files on Windows (desktop excluded while b19a9880 runs).
- Not added: a load-failure witness for nightly-tests-impl's Get-NightlyToolchainPreflight wrapper (not in the brief's three; same copied-tree pattern would cover it).
