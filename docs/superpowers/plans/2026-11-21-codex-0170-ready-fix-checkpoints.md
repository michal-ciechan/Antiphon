# Codex 0.170 ready gate

Code task: `0a8d69b6`. Base: `493bee0ee3af2052575bc7a26e9a313669b2fc2d`.
Branch: `feat/card-task-0a8d69b6`. RC fix for `rc/2026-11-30`. No timeout change.
Mutation is waived (operator 2026-11-12). No PC rows. Restart: none.

## What changed

`TryReadBottomComposer` treats the last non-empty row as the footer. Codex 0.170.0
draws `for agents` / `for shortcuts` under the model footer, so the model line was
read as the composer and `Classify` stayed `Unknown`. When that last row contains
both phrases, the reader steps to the previous non-empty row and uses it as the
footer. A 0.160 footer that keeps the warning chip on the model line does not match
and stays the last line.

## Performance budget

CP-1's 15 minutes is the cold Pty build plus `CodexStartupReadinessTests`. CP-2
reuses that build. CP-3 is the nine-class guard batch in its own cold output.
No repeat block after green. No whole Unit lane. Cost floor is the sum of
`EstimatedMinutes` (52).

## Unlisted runs

The red witness is not a checkpoint row. It ran at `f459e06e559c7dca6cc9f22bf3543d7bc09e2b84`
before this fix, one leased native build and one leased `--no-build` run of
`/*/*/CodexStartupReadinessTests/Codex_0170_shortcuts_row_under_the_footer_is_ready*`,
`--timeout 570s`, `ANTIPHON_LINUX_COMPILER_POLICY=0`. Result: total 1, failed 1,
succeeded 0. TRX `/tmp/c0170-red/c0170-red.trx`.

`scripts/regenerate-derived-impact-index.ps1` runs after these rows, outside the
tool, because it writes `tests/derived-impact-index.json`. It is committed only
when the bytes change, and that commit is last. A second run must print unchanged.

`git fetch origin master` and `git merge-tree --write-tree origin/master HEAD`
run after the rows. Fetch updates refs, so it is not a checkpoint command.

## Verification design

- V-1: `CodexStartupReadinessTests`, including `Codex_0170_shortcuts_row_under_the_footer_is_ready`. The measured Sol and Astra 0.170 grids are Ready and the region names the model. The 0.160 idle fixture stays Ready. The sign-in chooser is SignIn. The update modal is BlockingUpdate. A shortcuts row with no model footer is not Ready. A grid whose `for agents` text has no row under the model footer stays Ready.
- V-2: `CodexV0160StartupTests` stays green on the same build.
- V-3: the nine-class guard batch. Floor 78.
- R-1: pinned input digest equals the script pin. No reseal unless that check fails.
- R-2: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-3: `scripts/check-evidence-diff.ps1` over `493bee0ee3af2052575bc7a26e9a313669b2fc2d..HEAD` exits 0.
- R-4: `git merge-base --is-ancestor 493bee0ee3af2052575bc7a26e9a313669b2fc2d HEAD` exits 0, and merge-tree against `origin/master` reports no conflicts.

No PC variants. Every PC stays pending for method-scoped SourceLanding Mutation; this task defines none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | `tests/Antiphon.Agents.Pty.Tests -> bin-c0170-pty/` | c0170-ready | `/*/*/CodexStartupReadinessTests/*` | V-1 | all listed, 0 failed | 32 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | c0170-v0160 | `/*/*/CodexV0160StartupTests/*` | V-2 | all listed, 0 failed | 4 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | `tests/Antiphon.Tests -> bin-c0170-guards/` | c0170-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-3 | all listed, 0 failed | 78 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | n/a | c0170-pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $p = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $d = @(& pwsh -NoProfile -File ./scripts/test-live-lane-run.ps1 -PrintInputDigest)[-1].Trim(); $q = [char]39; $m = [regex]::Match((Get-Content -Raw ./scripts/test-live-lane-run.ps1), ("PinnedInputDigest = " + $q + "([0-9a-f]{64})" + $q)); if (-not $m.Success) { throw "pin line missing" }; $pin = $m.Groups[1].Value; $quote = [char]34; $legacyPattern = "LegacyExclusionDigest = ${quote}([0-9a-f]{64})${quote}"; $legacy = [regex]::Match((Get-Content -Raw ./tests/Shared/NativeLinuxPolicyWitness.cs), $legacyPattern).Groups[1].Value; Write-Output ("policyHash=" + $p.Hash); Write-Output ("inputDigest=" + $d); Write-Output ("pinnedInputDigest=" + $pin); Write-Output ("legacyExclusionDigest=" + $legacy); if (-not $legacy) { throw "legacy pin line missing" }; if ($d -cne $pin) { throw ("input digest drift computed=" + $d) }'` | R-1 | exit 0, printed digest equals the script pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | c0170-land | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | c0170-evidence | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 493bee0ee3af2052575bc7a26e9a313669b2fc2d -HeadRef HEAD` | R-3 | exit 0 | n/a | 2 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
