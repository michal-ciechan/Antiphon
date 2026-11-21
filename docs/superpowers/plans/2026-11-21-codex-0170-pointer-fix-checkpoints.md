# Codex 0.170 pointer composer evidence

Code task: `0a49db09`. Base: `2f5e1721900dcecb365d4a90c120a0cf6ad83cc2`.
Branch: `feat/card-task-0a49db09`. RC fix stacked on the ready-gate fix for `rc/2026-11-30`
(`493bee0ee3af2052575bc7a26e9a313669b2fc2d` stays an ancestor). No timeout change.
Mutation is waived (operator 2026-11-12). No PC rows. Restart: none.

## What changed

`CodexPointerComposerEvidence.TryReadComposer` treated the last non-empty row as the
model footer and required `TryReadFooterOnlyModel`. Codex 0.170.0 draws
`for agents` / `for shortcuts` under that footer, so a typed pointer was incomplete
(`prefix 0`) and `SubmitConsumedPointerAsync` ended at `NoComposerEvidence` without
Enter. The reader now calls `CodexStartupScreen.IsAgentShortcutsRow` and steps back
one non-empty row, the same step `TryReadBottomComposer` already takes. A 0.160
footer that keeps the warning chip on the model line does not match and stays the
last line.

The with-text grid is derived from the measured `149a0bb6` Sol frame in
`docs/investigations/2026-11-21-codex-0170-not-ready-on-server2.md` (commit
`25b83a250c962280a10ebcfeaa9cd5d1742813d5`) by replacing the idle composer line.
It is not a measured typed-composer capture. The live canary is the measurement.

## Other last-row readers

Grep of `src/Antiphon.Agents.Pty` and `server` for a Codex screen whose last
non-empty row, or a fixed bottom row, is the footer or composer boundary:

- `CodexStartupReadiness.cs` `TryReadBottomComposer` (the ready gate) already steps off the shortcuts row (`fd16cd4ca`). Not a remaining bug.
- `CodexStartupReadiness.cs:225` `IsLiveSession` reads the last non-empty row only after `TryReadBottomComposer` returns false. The shortcuts row passes `IsModelEffortCwdFooter`, so a banner plus that row still counts as live. It does not call `TryReadFooterOnlyModel` and it is not the pointer boundary. The measured-grid note says to leave it. Not changed.
- `CodexReadyTracker.Observe` (`CodexStartupReadiness.cs:392`) consumes `Classify`. No row walk.
- `CodexTurnScreenTracker.Observe` (`CodexDetectors.cs:134`) and `CodexWorkingIndicator.IsVisible` (`CodexDetectors.cs:82`) scan every line for `Working (` plus `esc to interrupt)`. Not a footer boundary.
- `CodexReadyWait` trust and update dismissal (`CodexStartupReadiness.cs:542` and `:579`) use whole-screen phrase matches (`CodexTrustPromptDetector`, `IsMeasuredUpdatePicker`). Not a last-row footer.
- `SessionMessageQueueService.CodexPointers.cs:69` calls `CodexPointerComposerEvidence` and does not walk rows. `CodexSubmitConfirmation.ComposerStillShows` (`CodexSubmitConfirmation.cs:240`) squashes the whole screen. Not a footer boundary.
- `CodexStartupCaptureStore.cs:52` calls `Classify` and stores the screen. No row walk.

## Performance budget

CP-1's 15 minutes is the cold Pty build plus the five named classes. CP-2 is the
nine-class guard batch in its own cold output. CP-3, CP-4 and CP-5 do not build.
No repeat block after green. No whole Unit lane. Cost floor is the sum of
`EstimatedMinutes` (47).

## Unlisted runs

The red witness is not a checkpoint row. It ran at
`41415c404439c3241b923a5bed54c45ace94b499` before the fix, one leased native build
(`lease=c165413e-901b-4d18-bcc4-27a2259825c7`, `waited=0s`, load `3.95`) and one
leased `--no-build` run (`lease=3d4a6e67-ae5d-412e-8900-eb24d3536eab`, `waited=0s`,
load `7.20`) of
`/*/*/CodexAccountBootstrapTests/Codex_0170_typed_pointer_with_shortcuts_row_is_complete*`,
`--timeout 570s`, `ANTIPHON_LINUX_COMPILER_POLICY=0`, native output. Result: total 1,
failed 1, succeeded 0. The assertion was prefix expected 65, actual 0. TRX
`/tmp/c0170-pointer-red/c0170-pointer-red.trx`.

A two-method green smoke of `/*/*/CodexAccountBootstrapTests/Codex_0170_*` at the
fix, before this plan was committed, is also unlisted. One leased native rebuild
and one leased `--no-build` run, same policy and `--timeout 570s`. Result: total 2,
failed 0, succeeded 2. It does not replace CP-1.

`scripts/regenerate-derived-impact-index.ps1` runs outside the tool, before the
rows, because it writes `tests/derived-impact-index.json`. It is committed only
when the bytes change, and that commit is last, so the rows verify it. A second
run must print unchanged.

`git fetch origin master` and `git merge-tree --write-tree origin/master HEAD`
run after the rows. Fetch updates refs, so it is not a checkpoint command.

The checkpoint tool's test command has no TUnit `--timeout` slot. Direct `dotnet run`
commands in this task pass `--timeout 570s`. Checkpoint rows are bounded by the
tool's row deadline.

## Verification design

- V-1: `CodexAccountBootstrapTests`, including `Codex_0170_typed_pointer_with_shortcuts_row_is_complete` and `Codex_0170_shortcuts_row_without_composer_text_is_not_complete`. The derived typed grid is complete and the prefix equals the compact pointer. The same grid with an empty composer, and the shortcuts row alone, are not complete and the prefix is 0. The existing 0.160 composer-evidence tests in this class stay green.
- V-2: `CodexStartupReadinessTests`, `CodexV0160StartupTests`, `CodexReadyTrackerTests` and `CodexReadyWaitTests` stay green on the same build.
- V-3: the nine-class guard batch. Floor 78.
- R-1: pinned input digest equals the script pin. No reseal unless that check fails. This rc tree has no `scripts/lib/nightly-policy.ps1`, so the row does not print `policyHash`.
- R-2: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-3: `scripts/check-evidence-diff.ps1` over `2f5e1721900dcecb365d4a90c120a0cf6ad83cc2..HEAD` exits 0.
- R-4: `git merge-base --is-ancestor 493bee0ee3af2052575bc7a26e9a313669b2fc2d HEAD` exits 0, and merge-tree against `origin/master` reports no conflicts.

No PC variants. Every PC stays pending for method-scoped SourceLanding Mutation; this task defines none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | `tests/Antiphon.Agents.Pty.Tests -> bin-c0170-pointer/` | c0170-pointer | `/*/*/(CodexAccountBootstrapTests*)\|(CodexStartupReadinessTests*)\|(CodexV0160StartupTests*)\|(CodexReadyTrackerTests*)\|(CodexReadyWaitTests*)/*` | V-1, V-2 | all listed, 0 failed | 81 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | `tests/Antiphon.Tests -> bin-c0170-pointer-guards/` | c0170-pointer-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-3 | all listed, 0 failed | 78 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | n/a | c0170-pointer-pins | `pwsh -NoProfile -Command '$d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | R-1 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | n/a | c0170-pointer-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | c0170-pointer-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 2f5e1721900dcecb365d4a90c120a0cf6ad83cc2 -HeadRef HEAD` | R-3 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
