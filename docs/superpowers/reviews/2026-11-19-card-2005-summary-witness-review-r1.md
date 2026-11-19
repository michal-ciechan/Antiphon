# CARD-2005 summary writer witness: Review round 1 (Final, Full)

Review task `8a114bb9`. Subject: Code task `77ec2b4f`, branch `feat/card-task-77ec2b4f`, tip
`ed0c1a5c928bbe0e0c0595fc4fa6335a42b5443b`. Range `3021acaf531d64cdb04fcba51d48d2e68f342977..ed0c1a5c928bbe0e0c0595fc4fa6335a42b5443b`
(fbcc4b913 witness, d74cf8d7f index, ed0c1a5c9 using import). Author Grok; reviewer Claude Opus.

Verdict: CLEAN. No blockers. Advisories below do not hold the land.

## 1. Extraction preserves behaviour

`Write-NightlyRunSummary` (scripts/lib/nightly-tests-impl.ps1:1600) is the old tail of
`Invoke-AntiphonNightlyTests` moved verbatim: the removed and added bodies are identical line for line
(D-5 dedupe, dispatchProgress with `suitesFromSummary=$true`, the phase-summary try, and
`Write-NightlyJsonFile -Path $summaryPath -Value $summary -Depth 8`).

Every variable the body reads, and where it now comes from:

| Variable | Source in the function | Caller value (Invoke-AntiphonNightlyTests) |
|---|---|---|
| `$summary` | parameter | the `[ordered]` summary built just above the call (same reference, mutated in place) |
| `$suiteResults` | parameter | `$suiteResults` (the same array is `summary.suites`) |
| `$dispatchSchedule` | parameter | `$dispatchSchedule` (init `$null`) |
| `$Sha` | `[string]` parameter | `[string]$Sha` parameter of the caller |
| `$completedAt` | parameter (untyped) | `Get-NightlyUtcNow` result |
| `$summaryPath` | `[string]` parameter | `Join-Path $LogRoot 'summary.json'` |
| `$LogRoot` | `[string]` parameter | `[string]$LogRoot`, canonicalised |
| `$exclusionGroup`, `$groupRows`, `$firstList`, `$firstText`, `$sameList`, `$groupRow`, `$list`, `$suiteExclusions`, `$phaseDir`, `$phasePlanned`, `$phaseIndex`, `$_` | locals | none of them is read after the call in the caller (checked 2149-2178) |

The one call site (nightly-tests-impl.ps1:2149) passes all seven. The callees read only their own parameters
or script scope (`Write-NightlyJsonFile`: Path/Value/Depth; `Assert-NightlyCoreAvailable`: Caller;
`Test-NightlyEvidencePath`: `$script:NightlyDeferEvidenceWrites`; `Get-TestPhaseTimingDirectory`:
`$script:QualificationTimingRoot`; `Write-TestPhaseFailureMarker`: Code/RunDirectory). Production still calls
it from inside `Invoke-AntiphonNightlyTests`, so any dynamic-scope read a callee might make resolves through
the same chain as before. Text-reading harnesses (test-nightly-tests.ps1 C1698 ordering, group4,
test-expected-platform-skips, test-release-gate C599) use whole-file `Contains`/`IndexOf` on strings the move
does not touch.

## 2. The witness exercises production

`scripts/test-nightly-summary-witness.ps1` dot-sources the real nightly-common.ps1 and nightly-tests-impl.ps1
and calls `Write-NightlyRunSummary -summary $summary -suiteResults $summary.suites -dispatchSchedule $schedule
-Sha $sha -completedAt $completedAt -summaryPath $summaryPath -LogRoot $dir`: the same seven parameters, with
the same aliasing (`suites` and `suiteResults` are one array) as the production call. V-1 additionally pins
exactly one `Write-NightlyRunSummary -summary $summary -suiteResults $suiteResults` call in the file.

## 3. Heap cap

`DOTNET_GCHeapHardLimit=0x30000000` is hexadecimal 805,306,368 bytes = 768 MiB. The script refuses any other
string and refuses `TotalAvailableMemoryBytes` outside 512 MiB..805,306,368, so a cap the runtime ignored
would be red. Observed `available=805306368`.

## 4. Mutation reproduced

Scratch `git archive` copies (scripts/, src/Antiphon.IO.WriteBehind, root build files), one build-slot lease
per pwsh run, `DOTNET_GCHeapHardLimit=0x30000000`:

- control (tip bytes): exit 0, `WROTE bytes=684288 suites=1274 progress=1274`, `UID-ONCE`, `REPORT-ACCEPTED`,
  `PEAK-RSS-KB 191884`, `C2005-OK`, lease held 7 s.
- mutant (`suitesFromSummary=$true;suites=@($suiteResults)}` on dispatchProgress): exit 1,
  `C2005-FAIL dispatchProgress embeds a second suites array`. The same text is also rejected by V-1
  (`ShouldNotContain("suites=@(")`).

## 5. Short and cross-platform

CP-1 hostWall 21.1 s for both tests. Nothing in the C# wrapper or the script is Windows-only: `pwsh` by name,
`GetTempPath`, and the `/proc/<pid>/status` peak read is guarded by `File.Exists` (prints `PEAK-RSS-KB 0` on
Windows). The class carries `[ParallelLimiter<ProcessSpawnLimit>]` and `[Category("Integration")]`.
Windows was not run here (server2 only, nothing on the desktop).

## 6. Checkpoint runs at ed0c1a5c9

Run `20261119-131432-16b6` (rows CP-1, CP-3..CP-10): verdict GREEN exit 0, 1 build, `unlisted: none`.

| CP | Result |
|---|---|
| CP-1 | executed=2 passed=2 failed=0 skipped=0 (build 437.6 s) |
| CP-3 | executed=87 passed=87 failed=0 skipped=1 (planned Linux skip C1210_Windows_linux_only_outcomes_are_exact) |
| CP-4 | regenerate driver build exit 0 |
| CP-5 | DerivedImpactIndexTests executed=27 passed=27 |
| CP-6 | LandGraphAudit build exit 0 |
| CP-7 | land-graph `-Check`: `approved` |
| CP-8 | policyHash=0396d6a5...; inputDigest = pinnedInputDigest = a08d61c1...050c |
| CP-9 | check-evidence-diff d57edfc2c..HEAD: commits=4 entries=0 violations=0 |
| CP-10 | merge-tree against origin/master exit 0 |

`validate --rows CP-1,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10`: `CHECKPOINT SOURCE VALID source=ed0c1a5c9... rows=8`.
CP-3 is green but does not validate because of its planned skip.

Run `20261119-133949-4734` (CP-2 alone): RED exit 1, executed=1 failed=1. The only failing sub-rows are
`CoreMatchesFallback.diagnostics` (logs golden) and `CoreMatchesFallback.coverageParts` (`diagTie`/`hidden`
latest-log tie). 33 of 35 pass. These are the two inherited rows. The fix is master 3fda36aca ("make the Linux
diagnostic golden and latest-log tie deterministic", task e5626886), which changes NightlyCoverageParts.cs and
test-nightly-core.ps1. This branch touches neither, so the land rebase picks the fix up.

Merge-tree (`git merge-tree --write-tree`) is clean against the brief's master 36d0c7582 (tree 55c729aa),
3fda36aca (beb206d7) and the current origin/master c93364585 (exit 0).

## Advisories (non-blocking)

- A-1. The synthetic summary is 684 KB after D-5, so the 768 MiB cap alone would not catch a
  non-streamed `ConvertTo-Json` writer at this size. V-1's text guard (no `| ConvertTo-Json`, no
  `-InputObject` except `$firstList`/`$list`) covers that regression. The heap witness proves the
  dedupe and single-copy shape, not streaming at the real tens-of-MB size.
- A-2. tests/derived-impact-index.json: the branch and master both change `AppDbContext.cs`/`HostLoadModel.cs`
  from 608 to 609 on identical lines. The textual merge is clean, but the true merged count is likely 610. The
  Merge step's regenerate-derived-impact-index.ps1 run (CARD-1838) regenerates it before DerivedImpactIndexTests.
