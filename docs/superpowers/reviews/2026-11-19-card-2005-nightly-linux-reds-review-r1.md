# CARD-2005 Linux nightly reds fix: Review round 1 (Final, Full)

Review task `9e184134`. Subject `feat/card-task-e5626886` (Code owner `e5626886`, Grok author), tip
`21c26aa931edd08b87ab49a0075a64a1a36321a7`, range `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8..21c26aa93`
(89f3a6902 the fix, 21c26aa93 the derived index). Master `3021acaf531d64cdb04fcba51d48d2e68f342977`.
Reviewer company differs from the author (Claude vs Grok).

**Verdict: CLEAN. No blockers. Advisories only.**

## Judgements

### 1. Ordinal tie-break against the old Windows rule

The removed body (`95d43ee27da2:scripts/lib/nightly-coverage.ps1:650-660`):

```
$files = @(Get-ChildItem -LiteralPath $Directory -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
$diag = @($files | Where-Object { [string]$_.Extension -eq '.diag' })
```

So on equal `LastWriteTimeUtc` it kept whatever position `Sort-Object` left. Two facts:

- `Get-ChildItem` on NTFS lists in NTFS collation (upper-case table, case-insensitive). The predecessor C#
  (04cc74f43, "first in directory order") reproduced that on Windows exactly.
- `Sort-Object` without `-Stable` is **not** stable beyond 16 items. Measured here on pwsh 7.5.4 with equal keys
  and `-Descending`: n=5 stable, n=17, 40 and 200 **not** stable (introsort, insertion sort below 16). So the old
  rule was "NTFS name order for up to 16 files, unspecified above". It was never a full deterministic rule.

Ordinal against NTFS order: identical for the realistic names (MTP `log_<digits>.diag`, digits, `.`, `_`
against digits, the same case). They differ only when two names diverge at a case difference (`B.diag` vs
`a.diag`: ordinal picks `B`, NTFS picks `a`) or at `[ \ ] ^ _` `` ` `` against a letter (`log_x` vs `loga`:
ordinal picks `log_x`, NTFS picks `loga`). That requires two files in one diagnostic directory with
LastWriteTimeUtc equal to 100 ns *and* such a name pair. That does not happen for real MTP logs, so **no real verdict
reads a different file**. The Windows golden cases: `diagTie` (`a.diag`/`b.diag`, ordinal and NTFS both give `a.diag`),
`upperExt` (`A.DIAG` -10 s vs `b.log` 0 s: not a tie, `.DIAG` matched OrdinalIgnoreCase as PowerShell `-eq` did),
`hidden` (not a tie). The ordinal rule cannot turn the Windows golden red. Not a blocker.

### 2. Test changes

- **Separator fold.** `scripts/fixtures/nightly/core-golden/diagnosticLogs.json` contains exactly four `\\`
  sequences, and all four are `<root>\\nc-<name>.diag`. The fold `Replace('<root>\\', '<root>/')` is applied to
  both sides, so every escaped separator in the golden is the placeholder one. Filenames, messages and order stay
  compared. The only thing it can hide is the separator itself (a Windows run that printed `<root>/`). That is
  cosmetic, not a semantic difference. The witnesses are checked against the raw golden (`$expectedRaw`). Not a blocker.
- **Hidden row.** The skip condition is `$hiddenSupported = Test-NCHiddenAttributePersists $root; if (-not $hiddenSupported) { ... }`.
  The probe writes one file, calls `SetAttributes(Hidden)` and reads `GetAttributes() -band Hidden`. On NTFS that
  persists, so **Windows runs the hidden row** and compares the full golden. If the probe's write throws, the
  function throws out (try/finally, no catch) and the check goes red rather than skipping. Only a throwing
  `SetAttributes` returns false. When skipped, the comparison still has teeth: the stripped golden must differ from
  the raw one (`,"hidden=a.diag"` present), the live payload must contain no `"hidden=`, and the rest must match
  `-ceq`. The witness list still requires `hidden=a.diag` in the golden (`coverageParts.json` contains it once).
  This run printed `NOTE NC CoreMatchesFallback.coverageParts.hidden skipped: FileAttributes.Hidden did not persist`
  on Linux, as designed. Not a blocker.

### 3. Derived index

The regeneration is mechanical: `AppDbContext.cs`/`HostLoadModel.cs` cap rows 608->609 and owed 583->584.
`origin/master` moved 0822e8bbc..3021acaf5 by four commits that touch only docs (three new `.md` under
docs/investigations and docs/superpowers/plans). They are not index inputs. The merged tree differs from HEAD only by
those three docs files. **The land need not regenerate as things stand.** If any test-class or source-map change lands on
master first, the land must rerun the builder and `scripts/regenerate-derived-impact-index.ps1 -NoBuild`, then
`DerivedImpactIndexTests.committed_index_matches_the_builder`, on the rebased tree.

### 4. Pins

Nightly `policyHash` comes from the policy JSON (`release-authority.ps1` path). No policy, nightly-common or pinned
census input is in the diff, which is only `NightlyCoverageParts.cs`, `test-nightly-core.ps1`, the index and the plan.
No re-pin is owed.

## Checkpoint run (one tool run, plan `docs/superpowers/plans/2026-11-19-nightly-linux-reds-checkpoints.md`)

Run `20261119-120746-ed68` at `21c26aa931edd08b87ab49a0075a64a1a36321a7`, source clean, buildSource verified,
1 build, `unlisted: none`. Wall 17m20s. Verdict GREEN, exit 0, 7 green, 0 red.

| CP | Result |
|---|---|
| CP-1 NightlyCoreTests | executed=1 passed=1 failed=0 (build 346 s, tests 26 s) |
| CP-2 DerivedImpactIndexTests | executed=27 passed=27 failed=0 (156 s) |
| CP-3 guard union (11 classes) | executed=114 passed=114 failed=0 skipped=1 (`C1210_Windows_linux_only_outcomes_are_exact` NotExecuted, the planned Linux skip) |
| CP-4 test-nightly-core.ps1 -Case CoreMatchesFallback | exit 0; `C487: 35 passed, 0 failed, 35 rows`; PASS diagnostics, PASS coverageParts, hidden NOTE printed |
| CP-5 land-graph -Check | exit 0, `approved` |
| CP-6 check-evidence-diff 0822e8bbc..HEAD | exit 0 |
| CP-7 merge-tree origin/master (3021acaf5) | exit 0, tree `b0f61992e6965614db9a8428083140951ef76e1e` |

`validate` over the whole run returns `row_failed` only because CP-3 carries its planned skip. That is the known
validator rule: skipped>0 counts as row_failed. `validate --rows CP-1,CP-2,CP-4,CP-5,CP-6,CP-7` returns
`CHECKPOINT SOURCE VALID source=21c26aa93... rows=6`.

## Advisories (cards, never hold the land)

- A-1. CP-3 Expect says 113 passed. The run passed 114, plus the C1210 skip. The guard union gained a case after the
  plan's count was taken. The Min (113) holds. This is a plan-count drift only.
- A-2. `Test-NCHiddenAttributePersists` could assert that it never returns false on Windows (`$IsWindows -and -not $hiddenSupported` -> fail).
  Then a host whose `SetAttributes` throws could not quietly drop the Windows-only row.
- A-3. The doc comment on `LatestDiagnosticLog` says Sort-Object "keeps that order for equal times". That holds only
  up to 16 files, as measured above. The ordinal rule is a deterministic tightening, not a byte-for-byte replica.
- A-4. No test pins a mixed-case or punctuation tie (for example `B.diag`/`a.diag`), the one place where ordinal and NTFS order differ.
  The choice of ordinal is deliberate but unwitnessed.
- A-5. Other consumers of `Get-NightlyLatestDiagnosticLog` were not rerun (`test-change-aware-release.ps1:114`,
  `test-test-selection.ps1:298`, `C1514NativeProbe.cs:50`). They read single-log directories, where the tie-break
  cannot act.

No PC is named. Mutation is waived (operator 2026-11-12) and stays pending, not discharged.
