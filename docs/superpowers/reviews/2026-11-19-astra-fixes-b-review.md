# Astra fixes B review, round 1 (CARD-2005)

Review task `4bd63eb8`. Subject `origin/feat/card-task-7e8fa6a4`, code owner `7e8fa6a4`. Reviewed tip `b137f78fdbb9edc125a30e43c4cd8fed7b59b012`. Range `d86d0f39422b152faf8c39e362a58b745eb95267..b137f78fdbb9edc125a30e43c4cd8fed7b59b012`, five commits. Round 1, Final, Full scope. Host: server2 (Linux). Master `11d21a183d91463eb39c2f1585e3a26ebba5f127`.

**Verdict: CLEAN. There are no blockers.** The advisories below go to cards and do not hold the land.

## Checkpoint rerun (run `20261119-173810-35f1`, all 12 rows, at the tip)

| CP | Result |
|---|---|
| CP-1 AstraFixesBTests | 3 executed, 3 passed |
| CP-2 WriteBehindFileSystemTests | 8 executed, 8 passed |
| CP-3 NightlyCoreTests | 1 executed, 1 passed |
| CP-4 NightlyScriptsTests | 7 executed, 7 passed |
| CP-5 guard union | 114 passed, 0 failed, 1 skipped. The skip is `C1210_Windows_linux_only_outcomes_are_exact`, which the plan expects to skip on Linux. |
| CP-6 derived-impact-index driver build | exit 0 |
| CP-7 DerivedImpactIndexTests | 27 executed, 27 passed |
| CP-8 land-graph audit build | exit 0 |
| CP-9 land graph `-Check` | exit 0, `approved` |
| CP-10 nightly pins | `inputDigest` and `pinnedInputDigest` are both `09b9424eaaa8387c5b4285739665de59221448653b750bbe7a1547b2eb2d4842`. `policyHash` is `0396d6a5...`. |
| CP-11 check-evidence-diff | 5 commits, 0 entries, 0 violations |
| CP-12 merge-tree against master | exit 1. The only conflict is `scripts/test-live-lane-run.ps1`, on the pin line. `tests/derived-impact-index.json` merges automatically. |

The run's verdict is RED only because of CP-12, the pin conflict the author predicted, and because CP-5's expected skip counts as a failed row. There is no validated receipt. Every executed row in run 1 records `dirty=0`, `sourceState=clean` and `source=b137f78f...`, and every test row records `buildSource=verified`. `validate` still refuses the whole run with `report_failed` because CP-12 is red. A second run (`20261119-175355-885f`, rows CP-1 to CP-4, CP-10 and CP-11) passed CP-10 and CP-11. Its four test rows were refused with `pre-existing-build-source`, because run 1 kept `tests/Antiphon.Tests/bin-astra-b/` after its red verdict. As a result, `reviewedSourceClean` is false.

## Judgements

1. **F-3 ordering.** In a nightly run, `Write-TestPhaseCommitFile` takes the deferred branch. It queues the record and then calls `EvidenceFiles.Flush(FinalPath)`. That call waits until the path is neither pending nor being written, and it raises only that path's failures. The record is then read back from disk with `File.ReadAllText`, not from the queue, and the method returns. Only after that return does `Add-TestPhaseExecutedChunk` add the record and advance `LastCommitted`.
   - **On failure:** `commit-unpublished` and `commit-unreadable` both throw before the acknowledgement. `Add-TestPhaseJournalEnvelope` retries once and then writes a gap record.
   - **Retry is idempotent:** if a record with the same publication id is already on disk, the retry returns true. A duplicate is detected rather than written twice.
   - **No deadlock:** `Monitor.Wait` releases the queue gate, and the caller holds no other lock.
   - **Kill or Ctrl+C before the flush** leaves no acknowledgement and no record. The resumed run has a shorter prefix.
   - **No fsync.** "On disk" means the rename has completed at the OS level. The record is not forced to the device. The inline path has the same documented semantics: it flushes to the OS, and a crash reads as a shorter prefix. A process crash cannot lose an acknowledged record, but a power loss can.
   - **Cost:** one wait per chunk for a single queued file. There is no fsync, so the cost on 1,289 chunks is negligible.
   - **Mutant:** I restored the base read-back from the queue and removed `Flush(FinalPath)`. `-Case JournalPublish` then failed 4 of 5 rows (ack-waits-for-disk, held-write-not-acked, throw-does-not-ack, kill-keeps-acked-record). I reverted it.
2. **F-2 flush failures.**
   - **`Flush(path)` no longer clears other paths' failures.** It raises only its own path's failures. `EvidenceFiles` keeps every raised failure in `retainedFailures`.
   - **The bounded read refuses a failed write.** `BoundedRead` and `Read-TestPhaseBoundedBytes` wait, and when a failure is stored they refuse with `deferred-write-failed`. They no longer report stale bytes as complete.
   - **The run summary sees the failure.** `Get-QualificationTimingReport` calls `Sync-NightlyDeferredEvidenceWrites` (a full `Flush()`) after the final projection, state and seal writes are queued. The verdict is computed after that, and `PeekFailures` then contains every failure. `Invoke-AntiphonNightlyTests` sets `coverageComplete` false, `testsPassed` false and outcome `TESTS`. It also adds a `deferred-evidence-write-failed:` reason before `summary.json` is written.
   - **`summary.json` itself is written directly**, not through the queue.
   - **Background-thread failures are caught** and stored by the queue, so they cannot crash the process.
   - **Duplicates are removed.** `PeekFailures` drops exact duplicates, and the reasons are added with `-notcontains`.
   - **In-flight probe:** I queued 400 writes of 200 KB each and then one failing write, and called the verdict immediately. The verdict was `coverageComplete=False` both before and after the flush.
3. **F-4 core identity.**
   - **Identity:** a SHA-256 of the `Antiphon.Nightly` and `Antiphon.IO.WriteBehind` `.cs` and `.csproj` sources plus the PowerShell and runtime versions, truncated to 24 hex characters.
   - **Comparison:** `Assembly.Location` of the loaded `TestPhaseJson` must equal `<TEMP>/antiphon-nightly-core/Antiphon.Nightly-<hash>/Antiphon.Nightly.dll`, and the four required types must resolve. Both checks run before the availability flag is set to true, and therefore before any chunk.
   - **Cache install is atomic:** a per-hash lock, a build into a `build-<guid>` directory, then `Directory.Move`. A partial DLL cannot appear at the keyed path.
   - **Refusal is a named throw:** `nightly-core-identity-mismatch: required=<hash> loaded=<path> missing=<types>`.
   - **Probe:** I copied the current, complete core to a different hash directory and loaded it first. It was refused with `loaded=<copy path>`.
4. **Witnesses.** I ran the red commit `24165460` in a scratch worktree:
   - FlushFailure: 0/7. The read returned `Complete=True Refused=False Kind=regular` (stale bytes reported as complete), and the verdict function was absent.
   - StaleCore: 0/4. The stub core was accepted, and the later call failed with "Unable to find type `NightlyCoverageParts`".
   - JournalPublish: red under the equivalent mutant in item 1.

   All three are green at the tip (CP-1). Commit `b137f78f` changes only the four here-string delimiters to plain single-quoted strings, and neither string body contains `'` or `$`.
5. **Pin.** `-PrintInputDigest` at the tip gives `09b9424e...`, which equals the pin. Master `ff1859b50` (Astra A) changed `NightlyCore.cs`, `NightlyJson.cs` and `nightly-coverage.ps1`, so the pin has to be recomputed on the merged tree. That is the only merge issue. The four required types still exist on master.

## Advisories (cards, not blockers)

- **A-1. The path-identity half of the F-4 check has no witness that can fail.**
  - **Where:** `scripts/test-astra-fixes-b.ps1` `Test-AstraStaleCore`.
  - **Failure:** I mutated `$pathOk = $true` in `Assert-NightlyLoadedCoreIdentity`. StaleCore still passed 4/4, because the stub also lacks `NightlyCoverageParts`.
  - **Why it matters:** a stale core that has all four required types but old behaviour is refused only by the path check. My probe shows the code does refuse it, but no test protects that.
  - **Fix:** add a case that loads a complete core copied to another hash directory and asserts `loaded=`.
- **A-2. A single deferred-write failure leaks `writer.lock` in per-chunk contexts.**
  - **Where:** `Close-TestPhaseExecutedContext` returns without disposing the lock when `Flush()` reports any failure, including a failure on an unrelated path. In the non-persistent per-chunk path (`Complete-TestPhaseLaunch`), the leaked `FileStream` means every later `Open` in the process is refused with `writer-lock-held`.
  - **Failure:** the later chunk records are not written (a `phase-summary-failed` marker is written instead). The run verdict is already false through the retained failure, so this loses evidence but does not misreport the run.
  - **Fix:** keep the lock only for a failure on a journal path, or release it once the failure is retained.
- **A-3. The verdict does not flush.** `Get-NightlyDeferredEvidenceWriteVerdict` only peeks, so its completeness depends on the earlier `Sync` in `Get-QualificationTimingReport`. **Fix:** call `[EvidenceFiles]::Flush()` inside the verdict before peeking.
- **A-4. Some failures are reported but never cleared.**
  - A failure raised by `Flush(path)` moves into the retained list, and `PathFailed(path)` no longer sees it. A later bounded read of the same path could then return the on-disk bytes as complete; the run verdict still fails.
  - A retry that succeeds leaves the run failed. This is the safe direction.
- **A-5. Smaller witness and message gaps.**
  - `AB FlushFailure.summary-calls-verdict` and `phase-quality-token` are source-text `Contains` checks.
  - The mismatch message does not name the fix: start a fresh `pwsh` process.
- **A-6. A flag set to true is never checked again.** `$script:NightlyCoreAvailable` stays true when a long-lived session dot-sources a newer checkout, so that session skips the identity check. A fresh nightly process is not affected.

## Unobserved, check on the Windows desktop

- `Assembly.Location` against `GetTempPath()` under 8.3 short-name TEMP forms.
- Sharing denial on `Antiphon.Nightly.dll` while another process holds it loaded, and the seven-day cleanup of loaded directories.
- Moving the target directory during `Install-NightlyCoreBuild` while antivirus scans it.
- Timing of `FlushFileBuffers` and of the rename under Defender.
- PowerShell 5.1 fallback (the core stays off).
- Ctrl+C during `Flush(FinalPath)`.
