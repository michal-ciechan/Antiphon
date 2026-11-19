# Operator nightly perf commits on master fb6dc4e5

Date: 2026-11-19. Investigate task 20293d8d. Read-only on server2. No source edits. Tip verified: `fb6dc4e5a36b4ecbc827afb29ed7039a2d74fdc2` (branch `feat/card-task-20293d8d` at that commit before this document). Range `95d43ee27da2..fb6dc4e5a36b`: 36 commits, 63 files. Desktop PowerShell 7 is UNOBSERVED from this host. The real run `a6278259` summary was not rewritten here.

## Verdict

The CARD-2005 duplicated-exclusion shape is gone from the production writer, and a synthetic 1,274-row document of that shape writes in 0.154 s at 154 MiB peak under a 768 MiB GC heap cap. Master compiles, `policyHash` and `PinnedInputDigest` recompute to the pins, and `scripts/audit-land-verification-graph.ps1 -Check` prints `approved`. Master is not a green Linux tip: `NightlyCoreTests` fails two of 35 harness rows, and `DerivedImpactIndexTests.committed_index_matches_the_builder` fails because `tests/derived-impact-index.json` was not regenerated. Do not cut the next rc from `fb6dc4e5` until those two reds are accepted or fixed, and until the desktop commands below have succeeded. Those desktop commands are what would prove the writer on the real `a6278259` logs. This investigation did not run them.

## 1. CARD-2005 writer

The failing line in run `a6278259` was `ConvertTo-Json` of the whole summary in `nightly-tests-impl.ps1` (then line 2282). On this tip the summary is written at `scripts/lib/nightly-tests-impl.ps1:2137` by `Write-NightlyJsonFile -Path $summaryPath -Value $summary -Depth 8`.

`Write-NightlyJsonFile` (`scripts/lib/nightly-core.ps1:182-186`) throws `nightly-core-required Write-NightlyJsonFile` when the core is absent, then calls `NightlyJson.WriteFile`. That streams a `Utf8JsonWriter` (`scripts/lib/Antiphon.Nightly/NightlyJson.cs:210-228`). `OutOfMemoryException` is not caught, so an OOM does not fall through to `ConvertTo-Json`. The `ConvertTo-Json` fallback runs only when the path is not fully qualified or `WriteFile` returns false (`GiveUp`, `IOException`, `UnauthorizedAccessException`).

D-5 (`nightly-tests-impl.ps1:2089-2124`) keeps each suite's exclusion list once on `summary.suiteExclusions[suiteId]` and removes it from the rows when every row's list is the same object (`ExclusionDedupe.SameElementReferences`) or the same JSON. Production assigns `$row.exclusions = @($census.Excluded)` (`nightly-tests-impl.ps1:1415`), so the lists are the same reference and the per-list `ConvertTo-Json` at lines 2105-2106 does not run. `dispatchProgress.suites` (lines 2116-2124) still copies the light chunk rows and always drops `exclusions`. Chunk identity is duplicated. The exclusion arrays are not.

### Residual whole-document paths

| Path | What is serialized whole | When |
|---|---|---|
| `nightly-core.ps1:185` | `ConvertTo-Json -Depth 8` of the value | Core is loaded and `WriteFile` returns false, or the path is not fully qualified |
| `nightly-common.ps1:401` `Write-NightlyAtomicJson` | `ConvertTo-Json -Depth 12` of the whole object | Callers below |
| `nightly-run-impl.ps1:803-810` | Reads the whole `summary.json` (no `-Omit`), sets `testsPassed` false, then `Write-NightlyAtomicJson` | `testExit -ne 0` and the summary still says `testsPassed` |
| `nightly-dispatch.ps1:153` | `Write-NightlyAtomicJson` of the dispatch schedule | Schedule entries are `suite,chunk,canonicalOrdinal,dispatchOrdinal,priorFailure`, not exclusion rows |
| `nightly-tests-impl.ps1:2018` | `ConvertTo-Json -Depth 8` of the selection object | Selection snapshot, not the chunk list |
| `nightly-tests-impl.ps1:2105-2106` | `ConvertTo-Json -Depth 8` of one suite's exclusion list | Only when reference equality fails inside a suite |
| `test-phase-evidence.ps1:2295` | `TestPhaseSummaryWrite.SummaryJson` builds one string (`TestPhaseSummaryWrite.cs:148-152`) | `timing/summary.json`, a different document from the run summary |
| `nightly-report.ps1:711` | `Get-Content -Raw \| ConvertFrom-Json` of the whole run summary | Every report. Safe at the deduped size. A failed-dedupe hundreds-of-MB file still OOMs this reader |

### Readers and keys

The written document still has the keys those readers require. Exclusions moved to `suiteExclusions`.

- `scripts/nightly-report.ps1:711` loads the whole file. `:772-776` walks `summaryObject.suites` for `failed` / `failedTests`. It does not read `exclusions`, `dispatchProgress`, or `suiteExclusions`. `:705-708` refuses a missing summary.
- `scripts/lib/nightly-failure-history.ps1:468` reads with `-Omit 'suites[].exclusions','dispatchProgress.suites[].exclusions','suiteExclusions'`. `:471` requires `runId`, `sha`, `profile`, `platform`, `policyHash`, `suites`, `completedAt`. `:491-497` walks `summary.suites` for `id`, `chunk`, `trx`, `execution`. `scripts/export-nightly-prior-failures.ps1:10` calls `Export-NightlyPriorFailures` and on refusal writes a compressed error object (`:14`).
- `scripts/lib/nightly-stopped-run-seed.ps1:182-184`: `summary.json` is optional. `Read-NightlyStoppedSummaryPrefix` keeps at most 32 MB and cuts at the first depth-1 key `timingChunks`, `suiteTimeline`, or `suites` (`nightly-failure-history.ps1:41-58`). The writer emits `timingChunks` and `suiteTimeline` before `suites` (`nightly-tests-impl.ps1:2069-2070`) and `suiteExclusions` after the rows (`:2113`), so `runId` / `sha` / `profile` / `policyHash` stay in the prefix.
- `scripts/release-selection.ps1` has no `summary.json`, `suiteExclusions`, or `dispatchProgress` references.
- `scripts/lib/qualification-supervisor-heartbeat.ps1` does not read `summary.json`.
- `merge-nightly-seeds.ps1` serializes seed envelopes. It does not rebuild the run summary.
- The CARD-1908 historical extractor was not re-run. The new document stores each suite's exclusion census once, so it is not the million-entry duplicated document that extractor was written against.

### Measurement (server2, pwsh 7, synthetic)

No `systemd-run` on this host. Each mode was a child `pwsh` with `DOTNET_GCHeapHardLimit=30000000` (768 MiB), set before that process started. Peak RSS is `VmHWM`. Shape: 1,274 chunk rows (antiphon 1100, session-runner 40, pty-host 20, agents-pty 30, messaging 20, e2e 40, client 12, scripts 12). Antiphon's shared exclusion list was 500 census-shaped objects (`uid`, `class`, `method`, `categories`, `disposition`, `reason`, `owner`); other suites had 8. The new mode stores that list once and strips it from rows and from `dispatchProgress`, then calls `Write-NightlyJsonFile`. The old modes put the same shared list on every row and on the dispatch copies.

| Mode | Exit | Peak RSS | File | Time |
|---|---|---|---|---|
| New writer, 768 MiB cap | 0 | 161,087,488 (154 MiB) | 1,598,339 bytes | 0.154 s |
| Old shape, streamed `WriteFile`, 768 MiB cap | 1 | 690,368,512 | none | OOM inside `WriteFile` at `nightly-core.ps1:184`. No `ConvertTo-Json` fallback |
| Old shape, one `ConvertTo-Json -Depth 8`, 768 MiB cap | 1 | 919,027,712 | none | `OutOfMemoryException` in `ConvertTo-Json` |
| `ANTIPHON_NIGHTLY_CORE=off` | child printed the throw (parent exit 0) | 113,508,352 | none | `nightly-core-required Write-NightlyJsonFile` |
| Old shape, streamed, no heap cap | 0 | 2,848,509,952 (2.65 GiB) | 541,548,445 bytes | 6.967 s (repeat 7.799 s) |

New-mode read-back: `suites=1274 progress=1274 rowExclusions=False progressExclusions=False liveness=closed runId=a627825956de493e891768878a814ba4 coverageComplete=True streamed=1`. The fixture reason text was ASCII, so a non-ASCII escape check was not evidence of streaming. Streaming is the `WriteFile` return plus this size and RSS.

The 768 MiB OOM is the cap biting the old duplicated shape. Uncapped streaming of that shape succeeds and costs about 2.65 GiB. The production change that matters is the dedupe: the written file is 1.6 MB and the peak is 154 MiB. This does not prove the writer against the retained `a6278259` logs.

## 2. Linux health

`dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false` through `scripts/build-slot.ps1 -Label investigate-antiphon-tests`. `ANTIPHON_LINUX_COMPILER_POLICY=0`, `TEMP=/tmp`, native output. Lease `90f461b8-d32a-4a0c-881b-b73e78aaec59`. Build succeeded. 2203 warnings, 0 errors.

Pins, recomputed on this tip:

- `tests/test-execution-policy.json` `policyHash` and `Read-NightlyExecutionPolicy` both `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542` (`match=True`, `core=True`).
- `scripts/test-live-lane-run.ps1:28` `PinnedInputDigest` and `-PrintInputDigest` both `a98bc1c3ab779f78025ac336102383bceb557f9fa306a4862c6130de13d42764`. Pin inputs include `nightly-core.ps1` and `NightlyCore.cs` (lines 24-27).

Land-graph: `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` through the build slot. Load 3.92. Lease `45eb49e2-08cf-41ed-9bb2-af6342ebe515` held 36 s. Output includes `digest=C5B7C0CEC63A241101BD6ED60C1C293CF797CB50F4B111FFED169B4529EC43FC count=80` and `approved`. Exit 0.

| Run | Filter / case | Result |
|---|---|---|
| NightlyCoreTests | `/*/*/NightlyCoreTests/*` `--timeout 570s` | total 1, failed 1, succeeded 0. Harness: 33 passed, 2 failed, 35 rows. Exit 2. Lease `05021670-5efb-438d-9dff-cf631f7adbf9` held 34 s. Duration 29 s |
| Guard union | nine-class parenthesized OR from `docs/testing-and-build.md` | total 81, failed 0, succeeded 81. Exit 0. Lease `6e016273-19ec-41d1-94cb-f460f9bf4c37` held 73 s |
| DerivedImpactIndexTests | `/*/*/DerivedImpactIndexTests/*` | total 27, failed 1, succeeded 26. Exit 2. Lease `8a81fc75-c185-42f6-95d3-a1bd2cdc5ef6` |
| RcFailedFirstExecutionTests | `ImportedAuthorityFixtureInitializesTiming` | total 1, failed 0, succeeded 1 |
| RcFailedFirstExecutionTests | `C1590_ArchiveKeepsGitNamedFiles` | total 1, failed 0, succeeded 1 |
| `scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` | 6 rows | C487: 6 passed, 0 failed. Exit 0. Lease `7345bad2-b0eb-4914-aa55-df80cc5512ca` held 9 s |

Not run, because of the 570 s session cap and the time box: unfiltered `scripts/test-nightly-tests.ps1` (empty `-Case` expects 105 rows), unfiltered `scripts/test-release-gate.ps1` (every `C599_` case), `scripts/test-change-aware-release.ps1` (throws without `-Case`), `C1686_LinuxOnlyCensus`, and the long `RcFailedFirstExecutionTests` methods `ActualLoopFollowsSchedule` (1080 s harness), `JournalCustodySurvivesInterleaving` (481 s harness), and `C1590_ArchiveHoldsNoGitRepository` (481 s harness). `NightlyCoreTests` already runs `scripts/test-nightly-core.ps1 -Case CoreMatchesFallback`, so that script was not repeated.

### NightlyCoreTests exact failure

`ShouldAssertException: result.ExitCode should be 0 but was 1` at `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`. `C487 HARNESS EXIT CODE: 1`.

```
FAIL NC CoreMatchesFallback.diagnostics - compiled 317141 chars, fallback 317141; logs golden False actual 2863 chars
FAIL NC CoreMatchesFallback.coverageParts - latest actual ["empty=null","diagNewest=b.diag","noDiag=b.txt","diagTie=b.diag","upperExt=A.DIAG","hidden=h.diag"]
  expected ["empty=null","diagNewest=b.diag","noDiag=b.txt","diagTie=a.diag","upperExt=A.DIAG","hidden=a.diag"]
```

Digests and verdicts in that `coverageParts` object matched the golden. The record comparison (`compiled` vs `fallback`, both 317141 chars) matched. The two failures are the diagnostic-log golden and the latest-file names.

Mechanism, measured on this host, not a runtime bisect:

- `scripts/fixtures/nightly/core-golden/diagnosticLogs.json` embeds Windows paths (`<root>\\nc-....diag`). The file is 2871 bytes; the UTF-8 text is 2867 chars; replacing each `\\` path separator with `/` yields 2863 chars, which is the actual length the harness reported. Witnesses `discovery-count-mismatch` and `empty diagnostic` are present, so the miss is the `-ceq` against that Windows golden. The golden files were added in this range (`git diff --stat` shows both goldens as new).
- `NightlyCoverageParts.LatestDiagnosticLog` (`NightlyCoverageParts.cs:63-76`) keeps the first `.diag` in `Directory.GetFiles` order on an equal `LastWriteTimeUtc` (strict `>`), and skips `FileAttributes.Hidden`. Commit `04cc74f43` added that method. The golden expects `diagTie=a.diag` and `hidden=a.diag` (`scripts/test-nightly-core.ps1:399-427`). On this Linux host `SetAttributes(Hidden)` leaves `Attributes=Normal`, and `GetFiles` order for the tie was `b.diag` then `a.diag`, so the newest visible file is `h.diag` and the tie is `b.diag`.

`LatestDiagnosticLog` did not exist at `95d43ee27da2`. These two rows are red because this range's Windows goldens and Windows filesystem assumptions do not hold on Linux. Whether the same test passes on the desktop is UNOBSERVED. A `git bisect` was not run: each step would rebuild.

### DerivedImpactIndexTests exact failure

`failed committed_index_matches_the_builder`. `committed.ShouldBe(index.Utf8)` at `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs:177`. The assertion dumps both files as byte arrays (about 3.8 million characters of decimal bytes in the log). The earlier contains-checks on `windowsOnly` / `linuxOnly` passed. Stdout: `derived-impact windowsOnly count=31 linuxOnly count=1`.

A rebuild with `scripts/regenerate-derived-impact-index.ps1` (build slot, then `-NoBuild` to diff) printed `derived-impact-index changed`. The committed file was copied aside and restored; `git status` was clean afterward. Both files are 859,131 bytes, 2,412 edges, `windowsOnly` 31, `linuxOnly` 1, schema 1, cap 25. No edge path was added or removed, and no edge's class list changed. Two capped rows changed, and nothing else in `cappedPaths`:

- `server/Infrastructure/Data/AppDbContext.cs`: `classes` 608 to 609, `owed` 583 to 584, reason stays `cap`. The owed `first` sample is unchanged.
- `server/Infrastructure/Data/HostLoadModel.cs`: the same 608 to 609 and 583 to 584.

First differing byte is 49387, inside the `AppDbContext.cs` capped object. The kept 25 classes for those paths do not change. The index file is not in the 36-commit diff. It was last written by `59ae9ce6a` (`test(card-1995): regenerate the derived impact index`). The only commit between that and `95d43ee27da2` is `docs(plan): incremental rc qualification...`, which does not touch C#. The builder reads tracked C# (`git ls-files`). The +1 class was not named. No runtime bisect.

## 3. Missing core

`Test-NightlyCoreAvailable` (`nightly-core.ps1:140-160`) returns false when `ANTIPHON_NIGHTLY_CORE=off`, PowerShell major is below 7, the project identity is missing, or `Install-NightlyCoreBuild` throws. A build failure warns `nightly-core-unavailable` and returns false. `Assert-NightlyCoreAvailable` (`:202-204`) throws `nightly-core-required <Caller>`. Measured: `ANTIPHON_NIGHTLY_CORE=off` printed `COREOFF nightly-core-required Write-NightlyJsonFile`. That is a hard refusal, not a silent summary.

The rc/nightly launcher is `scripts/nightly-run.ps1` (ASCII, `#requires 5.1`). Windmill calls it. RC callers pass `-Ref rc/YYYY-MM-DD`. There is no separate core-preflight switch. `nightly-run-impl.ps1:778` always starts the long tests as `Invoke-NightlyWatchedCommand -FilePath 'pwsh'`. A missing `pwsh` fails that hop. `scripts/lib/nightly-owned-process.ps1:44-48` remaps a WindowsApps `pwsh.exe` to `Program Files\PowerShell\7\pwsh.exe` and throws `Native process custody requires unpackaged PowerShell 7 in Program Files\PowerShell\7` when that file is absent. `scripts/summarize-test-phases.ps1:25-31` re-executes under bare `pwsh` when the major version is below 7 (commit `db8e75e1b`).

The first hard require in the tests process is `Enable-NightlyDeferredEvidenceWrites` at `scripts/nightly-tests.ps1:114`, which asserts the core before `Invoke-AntiphonNightlyTests`. That is before `npm ci`, `dotnet build`, and the chunk loop (`nightly-tests-impl.ps1:1840-1943`). Suite planning calls `Get-NightlyProfileExecutionChunks` (`:57`), which asserts again, from `Prepare-NightlySuiteExecution` after the build (`nightly-dispatch.ps1:175`). A missing core stops the run at tests-process start. It does not produce a wrong summary after 16 hours. Policy load (`Read-NightlyExecutionPolicy`) does not require the core. Desktop presence of `pwsh` 7 is UNOBSERVED. Seeded checkouts copying the core library (`db8e75e1b`) were not inspected on the desktop.

## 4. Desktop commands before the 16-hour run

Run these on the Windows candidate checkout that is exactly `fb6dc4e5a36b4ecbc827afb29ed7039a2d74fdc2`, under PowerShell 7, not from this Linux worktree. Do not write into the retained log directory.

PowerShell 7:

```powershell
& "$env:ProgramFiles\PowerShell\7\pwsh.exe" -NoProfile -Command "$PSVersionTable.PSVersion.ToString()"
Get-Command pwsh | Format-List Source,Version
```

Core preflight (builds the core into the user temp cache; a throw means do not start the 16-hour run):

```powershell
pwsh -NoProfile -Command "Set-Location '<candidate-checkout>'; . .\scripts\lib\nightly-common.ps1; . .\scripts\lib\nightly-core.ps1; if (-not (Test-NightlyCoreAvailable)) { throw 'nightly-core-required preflight' }; 'core-ok ' + [Antiphon.Nightly.NightlyJson].Assembly.Location"
```

Dry summary from the `a6278259` logs. There is no production function that rebuilds `summary.json` from a finished log directory. This builds the heavy part the writer stores (one census exclusion list per suite, shared by reference across that suite's timing rows), applies the same D-5 removal, and calls `Write-NightlyJsonFile`. Output: `$env:TEMP\card-2005-a6278259\summary.json`. A successful run prints the byte size, the suite-row count, and confirms no row still has `exclusions`. It does not by itself prove a later `testsPassed` rewrite (`nightly-run-impl.ps1:803-810`) or `nightly-report.ps1:711`.

```powershell
pwsh -NoProfile -Command @'
$ErrorActionPreference = "Stop"
$checkout = "<candidate-checkout>"
$log = "C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs\2026-11-18-1823-a6278259"
Set-Location $checkout
. .\scripts\lib\nightly-common.ps1
. .\scripts\lib\nightly-core.ps1
. .\scripts\lib\nightly-coverage.ps1
if (-not (Test-NightlyCoreAvailable)) { throw "nightly-core-required preflight" }
$outDir = Join-Path $env:TEMP "card-2005-a6278259"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir "summary.json"
$discoveries = @(Get-ChildItem -LiteralPath $log -Filter "*-profile.discovery.json" -File)
if ($discoveries.Count -lt 1) { throw "no *-profile.discovery.json under $log" }
$censusBySuite = @{}
foreach ($file in $discoveries) {
    $suite = $file.Name.Substring(0, $file.Name.Length - "-profile.discovery.json".Length)
    $doc = Read-NightlyJsonFile -Path $file.FullName
    $nodes = @($doc.Nodes)
    if ($nodes.Count -lt 1) { throw "no Nodes in $($file.Name)" }
    $censusBySuite[$suite] = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes
}
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($timing in @(Get-ChildItem -LiteralPath $log -Filter "*.timing.json" -File)) {
    $name = $timing.BaseName
    $dash = $name.IndexOf("-")
    if ($dash -lt 1) { continue }
    $suite = $name.Substring(0, $dash)
    $chunk = $name.Substring($dash + 1)
    if (-not $censusBySuite.ContainsKey($suite)) { continue }
    $census = $censusBySuite[$suite]
    $rows.Add([ordered]@{
        id = $suite; chunk = $chunk; name = $suite; log = $timing.FullName
        exitCode = 0; timedOut = $false; skipped = $false; result = "pass"
        requiredUidCount = [int]$census.RequiredCount; excludedUidCount = [int]$census.ExcludedCount
        censusDigest = [string]$census.Digest; exclusions = @($census.Excluded); coverageComplete = $true
    })
}
if ($rows.Count -lt 1) { throw "no timing rows joined to a profile discovery census" }
$suiteExclusions = [ordered]@{}
foreach ($group in @($rows | Group-Object { [string]$_["id"] })) {
    $groupRows = @($group.Group)
    $first = @($groupRows[0]["exclusions"])
    $same = $true
    foreach ($row in $groupRows) {
        $list = @($row["exclusions"])
        if ($list.Count -ne $first.Count -or -not [Antiphon.Nightly.ExclusionDedupe]::SameElementReferences($list, $first)) { $same = $false; break }
    }
    if (-not $same) { throw "dedupe refused for suite $($group.Name)" }
    $suiteExclusions[$group.Name] = $first
    foreach ($row in $groupRows) { $row.Remove("exclusions") }
}
$progress = @(foreach ($row in $rows) { $copy = [ordered]@{}; foreach ($key in @($row.Keys)) { if ($key -cne "exclusions") { $copy[$key] = $row[$key] } }; $copy })
$summary = [ordered]@{
    runId = "a627825956de493e891768878a814ba4"; sha = "997f9b8d9ffc"; profile = "rc"; platform = "windows"
    policyHash = "0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542"
    completedAt = [datetime]::UtcNow.ToString("o"); coverageComplete = $true; testsPassed = $true
    timingChunks = @(); suiteTimeline = @(); suites = @($rows); suiteExclusions = $suiteExclusions
    dispatchProgress = [ordered]@{ schemaVersion = 1; liveness = "closed"; suites = $progress }
}
$sw = [Diagnostics.Stopwatch]::StartNew()
Write-NightlyJsonFile -Path $out -Value $summary -Depth 8
$sw.Stop()
$back = Read-NightlyJsonFile -Path $out
$still = @($back.suites | Where-Object { $_.PSObject.Properties.Name -contains "exclusions" }).Count
"WROTE $out bytes=$((Get-Item -LiteralPath $out).Length) seconds=$([math]::Round($sw.Elapsed.TotalSeconds,3)) suites=$(@($back.suites).Count) timingFiles=$(@(Get-ChildItem -LiteralPath $log -Filter '*.timing.json' -File).Count) rowExclusions=$still suiteExclusionKeys=$(@($back.suiteExclusions.PSObject.Properties.Name).Count)"
'@
```

Replace `<candidate-checkout>` with the checkout path. Expect `rowExclusions=0` and a file on the order of a few megabytes, not hundreds. If `SameElementReferences` throws `dedupe refused`, the real census lists are not one shared reference and the production writer would keep per-row lists; stop and do not start the 16-hour run.

## Not done, noted

One line: teach `LatestDiagnosticLog` an ordinal name tie-break and stop treating the Windows hidden bit as the Linux witness, and regenerate `tests/derived-impact-index.json` with `scripts/regenerate-derived-impact-index.ps1` so `committed_index_matches_the_builder` matches the builder.
