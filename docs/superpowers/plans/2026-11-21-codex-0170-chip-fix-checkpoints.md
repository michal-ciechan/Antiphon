# Codex 0.170 warning-chip pointer evidence

Code task: `589abe80`. Base: `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`.
Branch: `feat/card-task-589abe80`. RC fix 3 stacked on the rc tip. No timeout change.
Mutation is waived (operator 2026-11-12). No PC rows. Restart: none.
`TryReadBottomComposer` is unchanged.

## What changed

`CodexPointerComposerEvidence.TryReadComposer` stepped back only for
`IsAgentShortcutsRow`. A measured Codex 0.170.0 composer that holds the boot
pointer drops `for agents` / `for shortcuts` and leaves the last row as the
warning chip (`⚠ 2 warnings · f2 to view`, 94 leading spaces, 119 columns).
That row passes `IsModelEffortCwdFooter` and fails `TryReadFooterOnlyModel`,
so the prefix stayed 0 and Enter was withheld.

The reader now also steps back one non-empty row when the trimmed last row
contains `f2 to view` and `warning` and does not contain `GPT-`. A 0.160
footer that keeps the chip on the `GPT-*` line stays the footer.

The grids are the measured with-text shape from
`docs/investigations/2026-11-21-codex-0170-delivery-verification-failed.md`
(commit `243f9924bc7333a4f7fc07b5eae4bf1ddfb72101`): a `›` row, one indented
wrap row, the `GPT-6.2-Sol` or `GPT-6.2-Astra` footer, then the chip-only row.
Sol session `dd111a14-2eb5-47ed-b27f-f9b7d15dadb7`. Astra session
`2aa3f72e-28eb-4fce-98f7-73e7fc4ab92a`.

## Performance budget

CP-1's 15 minutes is the cold Pty build plus the five named classes. CP-2 is the
nine-class guard batch in its own cold output. CP-3, CP-4 and CP-5 do not build.
No repeat block after green. No whole Unit lane. Cost floor is the sum of
`EstimatedMinutes` (47).

## Unlisted runs

The red witness is not a checkpoint row. It ran at
`94950a17cea7fde8b0040b042c1420842f3b288c` before the fix, one leased native build
(`lease=96caf8d7-5e1e-4b52-b88a-dbd5ca121d1c`, `waited=0s`, load `2.34`) and one
leased `--no-build` run (`lease=a3d6ee28-2362-4fa6-a82f-7e844fe401ec`, `waited=0s`,
load `7.32`) of
`/*/*/CodexAccountBootstrapTests/Codex_0170_typed_pointer_with_warning_chip_row_is_complete*`,
`--timeout 570s`, `ANTIPHON_LINUX_COMPILER_POLICY=0`, native output. Result: total 1,
failed 1, succeeded 0. The assertion was prefix expected 176, actual 0. Driver exit 2.
TRX `/tmp/c0170-chip-red/c0170-chip-red.trx`.

A four-method green smoke of `/*/*/CodexAccountBootstrapTests/Codex_0170_*` at the
fix `b963a3e8b7b96cf83ad89979aa61ed20adcb884a`, before this plan was committed, is
also unlisted. One leased native rebuild (`lease=295dde15-67c2-4a13-a4e3-b286f8cb82f7`,
`waited=0s`, load `4.21`) and one leased `--no-build` run
(`lease=612c38c8-aa81-48f5-9ec8-e04dff7bd1ad`, `waited=0s`, load `6.72`), same
policy and `--timeout 570s`. Result: total 4, failed 0, succeeded 4. It does not
replace CP-1. TRX `/tmp/c0170-chip-green/c0170-chip-green.trx`.

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

- V-1: `CodexAccountBootstrapTests`, including `Codex_0170_typed_pointer_with_warning_chip_row_is_complete` (Sol and Astra measured chip rows; prefix equals the compact pointer) and `Codex_0170_warning_chip_row_without_composer_text_is_not_complete` (empty composer and the chip row alone; prefix 0). `Codex_0170_typed_pointer_with_shortcuts_row_is_complete`, `Codex_0170_shortcuts_row_without_composer_text_is_not_complete`, and the existing 0.160 composer-evidence tests in this class stay green.
- V-2: `CodexStartupReadinessTests`, `CodexV0160StartupTests`, `CodexReadyTrackerTests` and `CodexReadyWaitTests` stay green on the same build.
- V-3: the nine-class guard batch. Floor 78.
- R-1: pinned input digest equals the script pin. No reseal unless that check fails. This rc tree has no `scripts/lib/nightly-policy.ps1`, so the row does not print `policyHash`.
- R-2: `scripts/audit-land-verification-graph.ps1 -Check` exits 0. Drift refreshes only through `scripts/land-graph-land-refresh.ps1 -Base <merge-base>`.
- R-3: `scripts/check-evidence-diff.ps1` over `b02b4ea7ae80ad8eff3445a82d5477b80107bf44..HEAD` exits 0.
- R-4: `git merge-base --is-ancestor b02b4ea7ae80ad8eff3445a82d5477b80107bf44 HEAD` exits 0, and merge-tree against `origin/master` reports no conflicts.

No PC variants. Every PC stays pending for method-scoped SourceLanding Mutation; this task defines none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | `tests/Antiphon.Agents.Pty.Tests -> bin-c0170-chip/` | c0170-chip | `/*/*/(CodexAccountBootstrapTests*)\|(CodexStartupReadinessTests*)\|(CodexV0160StartupTests*)\|(CodexReadyTrackerTests*)\|(CodexReadyWaitTests*)/*` | V-1, V-2 | all listed, 0 failed | 82 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | `tests/Antiphon.Tests -> bin-c0170-chip-guards/` | c0170-chip-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-3 | all listed, 0 failed | 78 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | n/a | c0170-chip-pins | `pwsh -NoProfile -Command '$d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | R-1 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | n/a | c0170-chip-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | c0170-chip-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef b02b4ea7ae80ad8eff3445a82d5477b80107bf44 -HeadRef HEAD` | R-3 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
