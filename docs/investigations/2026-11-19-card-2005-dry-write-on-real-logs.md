# CARD-2005 summary writer: dry write on the real a6278259 logs

Date: 2026-11-19. Debug task 220f064b, desktop (Windows 10 19045), read-mostly. Scripts and core
at master tip `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8` (this task worktree, clean). Source
artifact: verification 20293d8d,
`docs/investigations/2026-11-19-operator-nightly-perf-commits-review.md` at `0e9763240fb0`, section 4.
The retained log directory
`C:\Antiphon\releases\candidates\rc-2026-11-28-997f9b8d9ffc\logs\2026-11-18-1823-a6278259` was read
only. All output went to `%TEMP%\card-2005-a6278259\`. No AppHost, runner, or Postgres contact. No Full run.
`C:\src\Antiphon` and the rc release checkouts were not touched.

## Verdict

The tip writer handles the real a6278259 data. Two inputs were run. The 20293d8d dry script as
written succeeds. A production-faithful variant also succeeds: profile-aware census, all 1,272
joinable timing rows, and the tip's D-5 and `dispatchProgress` code. Its write is 1,675,486 bytes
in 0.036 s, and the process peak working set is 360 MB, most of it from parsing the 11.9 MB
discovery files before the write. The same real census in the pre-fix shape (per-row exclusions plus
a second `dispatchProgress` copy) is 1,112,973,646 bytes and peaks at 3.54 GB even when streamed.
That is the CARD-2005 scale. The `ConvertTo-Json` string the old writer built for it ran out of
memory. The readers accept the new file: the `testsPassed` rewrite, the failure-history
`dispatchProgress` rebuild, and `nightly-report.ps1` all ran. The dry script as written
under-represents the real document, for the two reasons below. The faithful variant is the proof.

## Shell the real run uses (previously UNOBSERVED)

- `scripts/qualification/launch-rc.ps1:1` is `#requires -Version 7.0`. Lines 62-65 re-run the *current*
  process image (`[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName`) for `build-slot.ps1`
  and `nightly-run.ps1`.
- `scripts/lib/nightly-run-impl.ps1:778`: `Invoke-NightlyWatchedCommand -FilePath 'pwsh'` for the tests
  hop. `scripts/lib/nightly-owned-process.ps1:39-48` resolves `pwsh` through `Get-Command` and remaps a
  WindowsApps alias to `Program Files\PowerShell\7\pwsh.exe`.
- Desktop, observed: `Get-Command pwsh -All` gives `C:\Program Files\PowerShell\7\pwsh.exe` (7.6.10.0)
  first, then two WindowsApps aliases. `C:\Program Files\PowerShell\7\pwsh.exe` exists:
  PSVersion `7.6.10`, CLR `10.0.15`. The tests hop therefore runs the summary writer under
  **PowerShell 7.6.10 at `C:\Program Files\PowerShell\7\pwsh.exe`**. Every run below used that binary.
  Windows PowerShell 5.1 does not run the writer.

## Runs

Each run was a child `C:\Program Files\PowerShell\7\pwsh.exe -NoProfile -File <script>`, sampled every
100 ms from the parent (`Process.PeakWorkingSet64`). Each script also printed its own
`PeakWorkingSet64` at the end. Desktop: 31.9 GB RAM, 8.9 GB free at the old-shape run.

| Run | Exit | Wall | Peak WS | Output | Result |
|---|---|---|---|---|---|
| Preflight `Test-NightlyCoreAvailable` (artifact command, checkout = this worktree) | 0 | 9.4 s | n/a | `core-ok ...\Temp\antiphon-nightly-core\Antiphon.Nightly-D2D41807C0F1993FFA2AC443\Antiphon.Nightly.dll` | Core built fresh for the tip (DLL 11:15:38Z) |
| Artifact dry write, verbatim (`-File` instead of `-Command`) | 0 | 4.33 s | 282.9 MB | `summary.json` 1,236,923 bytes, write 0.046 s | `suites=1044 timingFiles=1274 rowExclusions=0 suiteExclusionKeys=5` |
| Faithful, tip shape | 0 | 7.56 s | 360.1 MB (343 MB right after the write, 341 MB before it) | `summary-faithful-tip.json` 1,675,486 bytes, write 0.036 s | `suites=1272 rowExclusions=0 suiteExclusionKeys=11`, dedupe by reference for all 11 suites, 0 JSON-fallback comparisons, dedupe 0.155 s |
| Faithful, pre-fix shape, streamed by the tip writer (sizing only) | 0 | 14.51 s | 3,540 MB (private 4,555 MB) | 1,112,973,646 bytes, write 8.615 s | Deleted after it was measured |
| Readers on the tip-shape file | 0 | 12.61 s | 199.3 MB | see below | all three readers ran |

### Why the artifact script under-represents the real document

1. **Census without profile exclusions.** It calls `Get-NightlyDiscoveryCensus -DiscoveryNodes`. Production
   uses `Get-NightlyDiscoveryIndexView -Entry ... -ProfileExclusions <profile rows> -ProfileAware`
   (`nightly-tests-impl.ps1:1022,1122`), and its `.Census.Excluded` is what goes on each row (`:1408-1415`).
   Antiphon excluded: artifact 8, profile-aware 904. Other suites: agents-pty 82 vs 144, e2e 175 vs 7,
   session-runner 6 vs 193, pty-host 0 vs 68, markdown-pdf 0 vs 4. The faithful run used the cut's own
   policy (`git show 997f9b8d9ffc:tests/test-execution-policy.json`, profile `rc`, platform `windows`,
   policyHash `5c478a13...`). The artifact hard-codes `0396d6a5...`.
2. **Suite join splits on the first `-`.** The artifact maps `session-runner-...`, `agents-pty-...`,
   `pty-host-...`, `git-in-process-...`, `server-git-...` and `markdown-pdf-...` to `session`, `agents`
   and so on, finds no census for them, and drops them (`continue`). Result: 1,044 of 1,274 rows across 5 suites. The
   faithful run matches the longest suite-id prefix: 1,272 rows across 11 suites. The 2 unmatched,
   `client-all` and `scripts-all`, have no profile discovery, as in production. Both scripts keep
   `.timing` in the chunk name (`BaseName` of `x.timing.json`). That is cosmetic.
3. **Pre-tip `dispatchProgress`.** The artifact writes `dispatchProgress.suites` as a second copy of every row.
   Since `0822e8bbc`, production writes `{schemaVersion, liveness, sha, completedAt, canonicalPlanDigest,
   suitesFromSummary=true}` (`nightly-tests-impl.ps1:2119`). The faithful run uses the tip shape.

The faithful run copies the tip's D-5 block (`nightly-tests-impl.ps1:2092-2113`) verbatim and calls
the same `Write-NightlyJsonFile -Depth 8` as `:2132`. Rows carry the fields the artifact used plus
`trx` and `evidenceReasons`. They do not carry real per-chunk counts, `preflight`, `builds`,
`timingChunks` or `suiteTimeline`. The real header and timeline add some size. The deduped exclusion
lists were the heavy part, and they are now stored once per suite.

### Readers (tip-shape file, copies in `%TEMP%\card-2005-a6278259\`)

- **`nightly-run-impl.ps1:803-810` `testsPassed` rewrite** (whole read, then `Write-NightlyAtomicJson`, which is
  `ConvertTo-Json -Depth 12`), run on a copy: 0.859 s, 1,675,485 bytes, read back `testsPassed=False`,
  peak 138 MB. This residual whole-document path is safe at the deduped size.
- **`Read-NightlyJsonFile -Omit 'suites','dispatchProgress','suiteExclusions'`** (the `nightly-run-impl.ps1:785`
  verdict read): keys `runId,sha,profile,platform,policyHash,completedAt,coverageComplete,testsPassed,timingChunks,suiteTimeline`.
- **`Get-NightlySummaryDispatchProgress`** (`nightly-failure-history.ps1`, added by `0822e8bbc`) on the
  failure-history `-Omit` read: `rebuiltSuites=1272 liveness=closed`.
- **`nightly-report.ps1`** (`:711` whole `Get-Content -Raw | ConvertFrom-Json`, `:772-776` suite walk), run under pwsh 7
  as `-Summary <copy> -DryRun -PassThru -HttpShim { throw }`, so no HTTP left the process. It read
  and walked the file in 10.1 s, rendered 1,272 suite rows into a 72,148-byte `card.md` beside the copy, then hit
  the refused `GET /api/boards` and returned `ExitCode 3, Action reporting-failed` (the designed dead-API
  path). The title `Release-candidate red ...: failed` and `RED (TESTS)` follow from the dry header,
  which has no `succeeded` or `outcome` field (`nightly-report.ps1:500-504`). They are not a writer
  fault. Every row reads `counts unparsed`, because the dry rows carry no counts.

## Not proven here

- A real Full run writing its own summary: the dry header lacks `preflight`, `builds`, real
  `timingChunks`/`suiteTimeline` and real per-chunk counts. The next rc run is that proof.
- `nightly-report.ps1` against the live API, and a green verdict path.
- Memory under a hard cap. The runs were uncapped on a 32 GB host. The 20293d8d synthetic run covered the 768 MiB cap.

## Reproduce

Scripts are in `%TEMP%\card-2005-a6278259\scripts\` on the desktop: `dry-write-artifact.ps1`,
`dry-write-faithful.ps1 -Mode tip|oldshape`, `readers.ps1`, `run-sampled.ps1 -Script <s> -Tag <t> [-Mode <m>]`.
Run each under `C:\Program Files\PowerShell\7\pwsh.exe`. Each run's stdout and stderr are in `%TEMP%\card-2005-a6278259\<tag>.stdout.txt`.
