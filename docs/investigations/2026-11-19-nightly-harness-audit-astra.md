| Rank | ID | Severity | Confirmed mechanism | Scope and live-run qualification |
| --- | --- | --- | --- | --- |
| 1 | F3 | CAN-MISREPORT | A same-size replacement retaining the old timestamp reuses the old discovery digest and required UIDs; the expanded-coverage check accepts the wrong UID. | New in the audited series. Requires a replacement/edit preserving size and timestamp; not observed in the desktop run. |
| 2 | F1 | LOSES-EVIDENCE | The Windows bounded reader discards flush failures, drains the queue's failure record, and can return the old file as a complete successful read. | Present before the requested comparison baseline. Applies when an asynchronous evidence replacement fails. |
| 3 | F2 | LOSES-EVIDENCE | A completed chunk's journal append is acknowledged from memory before disk publication; closing its context releases the writer lock before publication. | Present before the comparison baseline. Forced termination in the measured interval loses that acknowledged record; normal flush completed in the probe. |
| 4 | F4 | CAN-MISREPORT | An already loaded older core is accepted without checking the current source identity; a current verdict helper subsequently fails with a missing type. | Present before the comparison baseline, exposed by the newer mandatory helpers. Requires a reused PowerShell process containing another core. |
| 5 | F5 | CAN-MISREPORT | Discovery files accepted by the old reader become `discovery-unreadable`: UTF-16, comments, and trailing commas. | New regression when removing the fallback. Can produce false incomplete/failed coverage for previously accepted inventories; normal current UTF-8 output passed. |

The five mechanisms above were reproduced locally. None establishes that the running desktop qualification has produced a wrong verdict. There is no WILL-MISREPORT finding for an untouched pinned checkout and a fresh process. The ordinary core checks passed 35/35; the pinned Windows skip roster gave identical background and inline results with zero gaps. This is a bounded investigation, not a certification of the entire harness.

## Provenance and scope

- Task: `a5e3ce5c-ad60-4037-8954-74ad8493fe70`, Investigate, assigned branch `feat/card-task-a5e3ce5c`.
- Audited source and all current-code probes: `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`.
- Requested old-code comparison: `95d43ee27da2272a34eca5ae51d6e5b98743df2a`. Also inspected the requested scripts history from `997f9b8d9ffc` to the audited cut. Three findings already exist at the old comparison point; they are not attributed to the later 36-commit batch.
- Live run named by the brief, **not accessed**: `e7f92f66f30b495bab0683f765c0f91c`, started 2026-11-19 14:30Z, desktop directory `C:\Antiphon\releases\candidates\rc-2026-11-29-26a99f1a747d\logs\2026-11-19-1430-e7f92f66`.
- Local platform: Linux, non-root UID 1654, PowerShell 7.5.4, runtime 9.0.10. The brief says the desktop uses PowerShell 7.6.10. Windows file sharing, PowerShell 5.1, Ctrl+C, and the desktop's loaded DLLs were not executed or observed here.
- No application source or existing tracked file was edited. Only this report is committed. Temporary fixtures, extracted baseline sources, binaries and harness results remain under gitignored `logs/a5e3ce5c-nightly-audit/` or the projects' native `bin/obj` directories. No production database, desktop access, deployment, restart or message to another service was used. The brief authorized the read-only audit and report publication, so no additional approval was requested.

All source citations below refer to the audited SHA, unless explicitly marked as the old revision. Transcript excerpts are local probe output, not live-run output.

## F3: cached hashes stop being content checks after timestamp-preserving replacement

**Mechanism.** `scripts/lib/Antiphon.Nightly/NightlyCore.cs:134` implements `CachedFileSha256`; its hit at line 142 checks only path, length and last-write ticks. The three-second age test at line 147 controls initial admission and does not establish file identity or detect a subsequent copy/restore retaining mtime. `scripts/lib/Antiphon.Nightly/EvidenceFiles.cs:142` exposes this as `CachedSha256`.

`scripts/lib/nightly-coverage.ps1:731` uses that result as a content key and line 732 returns the previous discovery entry without reading the replacement. Consequently the required-UID set used by the coverage check at `scripts/lib/nightly-coverage.ps1:1446` can describe the previous file. The same cache feeds the planned-file snapshot at `scripts/lib/qualification-supervisor-heartbeat.ps1:182` and the assembly digest at `scripts/lib/nightly-common.ps1:367`. The supervisor's separate uncached plan-digest check at `scripts/lib/qualification-supervisor-heartbeat.ps1:68` remains a defense; this report does not claim that check was bypassed.

**Exact scenario and reproduction.** Read a valid discovery file containing one required UID `u1`, whose mtime is one minute old. Replace it atomically with an equal-length valid discovery file containing `u2`, preserving mtime. Ask the same discovery index for required UIDs, then compare the expanded-coverage result for a terminal `Passed` row for `u1` with a fresh file read. The test changes only a temporary fixture. It does not assume ordinary writes preserve timestamps.

```text
F3 preserved-mtime cachedOld=True actualChanged=True
F3 sha=26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248 cachedUid=u1 diskUid=u2 cachedOk=True diskOk=False missing=u2 sameEntry=True
```

The second row is the production index and production expanded-coverage predicate, not a reimplementation of their comparison. The full native verdict also checks TRX/diagnostic class and method bags. Those identities can remain `N.C.M` in this scenario; changing only the expanded UID is precisely what that predicate is meant to detect. A whole qualification false pass was not run.

**History.** The digest cache was added in `2ebdd083f048635d2e14b8ea944057a52cd1e8f9`; the later series extends its use to evidence snapshots. The old `Get-NightlyDiscoveryIndexEntry` at `95d43ee27da2` calls `Get-NightlyFileSha256`, which reads bytes rather than this stat cache.

**Read-only desktop check after completion.** Independently hash the on-disk `*-profile.discovery.json`, `groups-planned*.json` and relevant native assemblies, and compare their recorded hashes/bindings; reconstruct the required UID set from the on-disk discovery and exclusions and compare it with execution diagnostics. Use fresh byte reads, not the in-process cached helper. Establish whether any copy/restore/editor rewrote those files while preserving their timestamps. A clean final tree alone cannot reconstruct an intermediate replacement that was subsequently undone.

**Remaining uncertainty.** No timestamp-preserving replacement was observed in this run; the pinned checkout convention materially lowers this risk. The confirmed defect is the cache's weaker-than-content identity and the resulting wrong expanded-coverage answer under the stated trigger.

## F1: bounded reads consume write failures and can accept stale evidence

**Mechanism.** The Windows branch of `Read-TestPhaseBoundedBytes` calls the core at `scripts/lib/test-phase-evidence.ps1:335`. `scripts/lib/Antiphon.Nightly/TestPhaseFiles.cs:90` calls `EvidenceFiles.Flush(path)` and ignores its returned failure array, then opens the existing file and can return `Complete=true, Refused=false` at line 113. `EvidenceFiles.Flush` catches `AggregateException` and converts it to strings (`scripts/lib/Antiphon.Nightly/EvidenceFiles.cs:74`). The underlying queue's path flush calls a **global** `ThrowFailures` (`src/Antiphon.IO.WriteBehind/WriteBehindQueue.cs:87`); that clears the stored failures before throwing at lines 115-117. A subsequent general flush cannot report them again. This can consume a failure for another path as well.

**Exact scenario and reproduction.** An existing evidence file says `sequence=1, publicationComplete=true`. Queue replacement text saying `sequence=2, publicationComplete=false`, but prevent creation of the atomic replacement in its parent directory. After the background write fails, call the Windows bounded reader's managed implementation directly. The Linux fixture used a parent mode of 0500 under the unprivileged task UID; the corresponding Windows trigger can be an ACL/write denial or a replacement denied by a reader's sharing mode. The Windows trigger itself was not executed.

```text
F1 stale-read Complete=True Refused=False remainingFailures=0 bytes={"sequence":1,"publicationComplete":true}
```

The fixture was restored to its original permission mode in `finally`. This proves silent failure consumption and a successful read of old bytes. It does not prove the top-level release gate accepts an inconsistent journal: `Get-TestPhasePublicationQuality` separately compares state sequence and prefix at `scripts/lib/test-phase-evidence.ps1:1835` and can identify that mismatch. Timing sidecars are explicitly diagnostic, not pass-credit inputs (`scripts/lib/test-phase-evidence.ps1:4`).

**History.** The ignored flush return is already present at `95d43ee27da2`, introduced by `51d3879144a2c9f7eef80c70968a2e18ffeda4e5`. It is a defect in the live core, not a new regression in the later comparison range.

**Read-only desktop check after completion.** Search existing run output for `deferred-evidence-write-failed`, but do not treat absence as proof: the bounded reader can consume it. Compare the contiguous numbered journal prefix with `groups-executed.state.json.sequence`, the flat projection digest, the seal and the completed chunk/TRX inventory. Inspect timing summary quality for `publication-state-mismatch`, missing phase data and stale chunk sidecars. Resolve the timing root from top-level `summary.json.timingRoot` or the launch record; do not assume it is always named `timing`.

**Remaining uncertainty.** No real desktop write failure or stale read is known. The read-back behavior is proven in the same managed implementation on Linux, not under Windows sharing rules.

## F2: acknowledgement and lock lifetime precede durable journal publication

**Mechanism.** The deferred path in `Write-TestPhaseCommitFile` (`scripts/lib/test-phase-evidence.ps1:1430`) queues the append, reads it back through the same in-memory facade and returns. `Add-TestPhaseJournalEnvelope` returns success at line 1485, and `Add-TestPhaseExecutedChunk` advances `LastCommitted` at line 1631. No disk barrier occurs at those acknowledgements. `Close-TestPhaseExecutedContext` releases `writer.lock` at line 1390 without first flushing queued writes. The queue owns a background thread (`src/Antiphon.IO.WriteBehind/WriteBehindQueue.cs:50`).

The normal command-line wrapper does flush in `finally` (`scripts/nightly-tests.ps1:114`), and an ordinary process-exit hook exists (`scripts/lib/Antiphon.Nightly/EvidenceFiles.cs:46`). Neither guarantees execution after a hard process kill or crash. A new child launch also synchronizes evidence at `scripts/lib/nightly-tests-impl.ps1:868`, limiting the exposure interval in ordinary sequential chunk execution. This is not a claim that all completed chunks remain buffered for the entire run.

**Exact scenario and reproduction.** Open a real temporary executed context and flush its initial metadata. Pause the queue at its existing internal `BeforeWrite` test seam, append a real `chunk-1` envelope, read both the in-memory and disk prefixes, close the context and acquire its writer lock again. The pause has a 15-second safety deadline and is always released in `finally`; no process was left running or killed.

```text
F2 ack=True LastCommitted=1 memoryRecords=1 diskRecords=0 diskCorrupt=False lockReacquired=true pendingBytes=1101
F2 after-flush diskRecordExists=True
```

This directly measures the acknowledged-but-not-published state and the lock-release window. Hard-kill loss is reconstructed from that measured state plus the background-thread/process-exit implementation, not claimed as a performed SIGKILL experiment. A second process can acquire the lock before the first writer's queue has finished; the probe establishes this admission window, not a full two-writer corruption sequence.

**History.** The deferred journal acknowledgement is already at `95d43ee27da2`, introduced by `3cc2a4c7e4a2076d77cc212ac27a7e542b7420f9`. The source comment in `EvidenceFiles.cs:9` also explicitly acknowledges crash loss of queued writes.

**Read-only desktop check after completion or interruption.** Compare each completed chunk's TRX/diagnostic/summary entry with numbered journal records and the terminal seal. A missing final record can look like a clean shorter prefix: the measured disk reader returned `Corrupt=false`. Check actual sequence continuity and seal/state counts rather than using that flag alone. Do not run `summarize-test-phases.ps1 -RecoverExecutedManifest` as an inspection step; it writes evidence.

**Remaining uncertainty.** The live run has not been observed to crash in this interval. Ctrl+C/finally behavior, forced Windows supervisor termination, OS power-loss durability and a concurrent recovery writer were not executed.

## F4: an older loaded core passes availability for a newer checkout

**Mechanism.** `scripts/lib/nightly-core.ps1:141` trusts the memoized flag; even after clearing it, line 145 accepts any loaded `Antiphon.Nightly.TestPhaseJson`. It returns before `Get-NightlyCoreBuildIdentity` at line 146. There is no comparison between the assembly already loaded in that process and the requested checkout's identity.

**Exact scenario and reproduction.** Extract only the core project, its write-behind dependency and `Directory.Build.props` from `95d43ee27da2` into the ignored audit directory, build it there, and load that real older DLL in a fresh probe process. Dot-source the current loader and coverage module. The loader accepts the older assembly; the current roster-digest helper later tries to use a type not present in it.

```text
F4 oldSource=95d43ee27da2 currentSource=26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248 accepted=True differentBinary=True expectedIdentity=7B6BA878F6ACF63476B0A5FD
F4 late-call error=Unable to find type [Antiphon.Nightly.NightlyCoverageParts].
```

The identity value is for this Linux PowerShell/runtime, not the desktop. The older build's provenance is the Git archive input; its generated informational-version stamp is not proof of that input when building inside a nested directory of another checkout. A preliminary probe also substituted an identity function which would throw if called; availability returned true with `identityCalled=False`.

This is a confirmed stale-load acceptance and delayed failure, not evidence that the current fresh `pwsh` launch loaded a stale core. Two independent processes loading different immutable cache keys do not by themselves trigger it. It matters for same-process library reuse across checkouts/updates, and for a preloaded different assembly. Compatible older types can retain older behavior; that silent semantic variant was not reproduced.

**History.** The type-only shortcut predates the comparison baseline (`34b750e0f42c7175a02199f6c232eceab48cd8b8`). Removal of fallbacks increases the consequences of accepting a core missing newer helpers.

**Read-only desktop check after completion.** Establish from the launch records whether nightly-tests ran in a fresh PowerShell process. Search output for `nightly-core-unavailable`, `nightly-core-required`, missing type/method exceptions and `preflight.unhandledError`. If already captured, compare the loaded assembly location/hash with the source identity. `summary.sha` names the checkout, not the loaded core. The presence of the correct DLL in the shared cache after the run does not prove which DLL the historical process loaded; that may be unresolved from existing artifacts alone.

## F5: discovery parsing lost the old tolerant path

**Mechanism.** `Read-NightlyDiscoveryDocument` directly calls `NightlyJson.ReadFile` at `scripts/lib/nightly-coverage.ps1:179` and throws at line 182 when it returns null. Unlike `Read-NightlyJsonFile` (`scripts/lib/nightly-core.ps1:168`), it no longer falls back to `ReadAllText | ConvertFrom-Json`. The compiled parser accepts UTF-8 JSON with strict syntax (`scripts/lib/Antiphon.Nightly/NightlyJson.cs:68`); its null result also covers unsupported encoding/syntax, not just corrupt inventory content.

**Exact scenario and reproduction.** The same one-node inventory with valid format, TUnit/MTP versions, assembly hash and identity was passed to the exact old file-reader function extracted with PowerShell's AST, and to the current file reader. Both used the current unchanged node-conversion semantics to isolate the file-reader change.

```text
F5 utf8 old=nodes=1 current=nodes=1
F5 utf8-bom old=nodes=1 current=nodes=1
F5 utf16 old=nodes=1 current=throw:discovery-unreadable
F5 comment old=nodes=1 current=throw:discovery-unreadable
F5 trailing-comma old=nodes=1 current=throw:discovery-unreadable
```

`Get-NightlyNativeCoverageVerdict` catches discovery-reader exceptions and returns both `coverageComplete=false` and `testsPassed=false` at `scripts/lib/nightly-coverage.ps1:1386`. Thus a previously accepted inventory, for example a UTF-16 inventory saved by Windows PowerShell's default `Out-File`, can produce a false failed/incomplete result. Comments/trailing commas are compatibility cases accepted by the old parser, not claims about strict JSON syntax.

**History.** The direct compiled read arrived in `d0e5208ed`; removing its tolerant fallback in `20bf32214ae72bb09cfe7320848d963c119ad059` creates this regression relative to `95d43ee27da2`.

**Read-only desktop check after completion.** Search `summary.suites[].evidenceReasons`, top-level reasons and the existing run output for `discovery-unreadable`. Inspect the first bytes and syntax of the named discovery file: UTF-16 LE begins `FF FE`; ordinary UTF-8 and a single UTF-8 BOM passed both controls. Parse the file with `ConvertFrom-Json` in a separate read-only inspection to establish whether this is the compatibility case. Current harness-produced UTF-8 inventories do not need to trigger this finding.

## Other requested paths: what was established

| Area | Evidence and limit |
| --- | --- |
| Chunk pass/fail/skip and empty/null collections | Existing `CoreMatchesFallback` verdict, coverage, outcome-row, TRX and discovery checks passed. No additional clean-input verdict divergence was reproduced. This does not enumerate all possible inputs. |
| Background expected-skip scan | `NightlySkipScanTask` stores each stage exception (`scripts/lib/Antiphon.Nightly/SkipScanTask.cs:105`) and wrappers rethrow it at the corresponding join (`scripts/lib/nightly-coverage.ps1:1020`, `1044`, `1103`). The normal tail joins even an unused scan at `scripts/lib/nightly-tests-impl.ps1:2065`. On this pinned source, Windows roster background and inline results were equal: source gaps 0, reason gaps 0, drift mismatches 0; background scan 2,372 ms, total comparison 2,727 ms. This proves neither Windows latency nor completion after hard termination. |
| End/timeout/exception while scanning | A chunk timeout does not cancel the source scan; normal completion still joins it. The join is outside the suite-dispatch try/catch. A scan error can therefore prevent the final summary, as the old inline scan error could. An abnormal exit before joining has no persisted scan result. No silently swallowed stage exception was found on the normal joined path. |
| Hangs and heartbeat | `SkipScanTask.cs:91` uses unbounded `Task.Wait`; queue flush uses unbounded `Monitor.Wait` (`WriteBehindQueue.cs:86`, `96`), including inside a nominally deadline-bounded read. No stuck filesystem call or nonterminating scan was reproduced. The heartbeat uses CPU, TRX/log growth and diagnostic growth, not task completion (`qualification-supervisor-heartbeat.ps1:377`). A fresh heartbeat alone is not evidence that a pending join will finish; no confirmed HANG finding is asserted. |
| Core cache publication | `nightly-core.ps1:60` acquires an exclusive per-key file lock and line 77 moves a completed output directory into the cache. This avoids ordinary readers observing an in-progress build output. Cache hits check DLL existence, not a recorded binary hash. Identity hashes project `.cs/.csproj` files plus PowerShell/runtime versions, not all imported build inputs (`nightly-core.ps1:15`). Concurrent cleanup/build/load and interrupted cache publication were not stress-tested; F4 is the independently reproduced loader issue. |
| General JSON readers | Unlike the discovery exception in F5, `Read-NightlyJsonFile` retains a fallback for unsupported strict JSON. Existing `jsonRead`, `jsonOmit`, `jsonWrite`, active-marker and summary serialization checks passed, including their type/refusal cases. No independent live-sized heap test or full 7.6.10/5.1 compatibility test was performed. |
| Failed-first seed and dispatch progress | Inspected the exclusion-index substitution and the summary reader/writer pair. The writer records `suitesFromSummary=true` (`nightly-tests-impl.ps1:1640`); the history reader reconstructs chunk rows (`nightly-failure-history.ps1:333`, called at line 776). No missing in-repository reader adaptation was established. The four-thread archive/UID optimization in `4099d4408` is in `scripts/test-nightly-failed-first.ps1`, a fixture, not the production dispatcher. The brief's exact `DiscoveryUidIndex` symbol was not present. Full failed-first replay was not run. |
| Summary writer and ordering | The compiled sorts, census, planned partition and JSON checks passed. No second summary row array is written by the new `Write-NightlyRunSummary`. Existing tests are finite goldens; their success does not cover F1-F5's fault/reuse inputs. The live end-of-run memory peak and top-level summary write were not observed. |

## Local validation record

Every build and probe driver ran through `scripts/build-slot.ps1`; all leases were released and all child commands were awaited. The PowerShell probe wrappers emitted the expected non-dotnet maxcpucount warning; they did not launch hidden builds. Builds used each project's native default output, with no `-o`, `--output` or `OutputPath` argument.

| Driver | Outcome |
| --- | --- |
| Current `Antiphon.Nightly.csproj`, Release, `CopyLocalLockFileAssemblies=true` | Build succeeded, 0 warnings, 0 errors, 4.70 s; lease `68ca2dbc-f0d8-4cf4-8c7a-70df9a503d3c`. |
| `probe.ps1` | F1/F2/F3/F4 trigger observations above; exit 0; lease `c95ed9ea-751d-4e70-affd-4c92014da622`. |
| `semantics.ps1` | Five parser comparisons and Windows-roster background/inline comparison completed; exit 0; lease `64bd31c5-c6bb-452a-b891-226900bff9b0`. |
| `scripts/test-nightly-core.ps1 -Case CoreMatchesFallback`, with the current locally built DLL preloaded | **35 passed, 0 failed, 35 rows**; exit 0; lease `56dc47b2-1843-42e2-b6b2-76279ae3befb`. Hidden-attribute subcase explicitly not exercised on this Linux filesystem. |
| `discovery-cache.ps1` | Production expanded-coverage predicate returned true for cached `u1`, false for current on-disk `u2`; exit 0; lease `451c6509-cbd4-4983-a1d2-dac30124584c`. |
| Initial baseline extraction/build attempt | Setup failed: `python3` unavailable, then MSBuild `MSB1009` because the archive had not been extracted. No tests ran. Lease `e97cb5a2-6142-4df9-adf4-4f7b6cf0785a`. Retained here rather than described as green. |
| Baseline core after successful `git archive`/`tar` extraction | Build succeeded, 0 warnings, 0 errors, 4.92 s; lease `7d304a51-4fc4-43c1-8b1f-b0432d424d49`. |
| `stale-core.ps1` using that actual baseline DLL | Current loader accepted the different DLL; later helper reported missing `NightlyCoverageParts`; exit 0; lease `6b56a9f9-3ee7-4aa3-a328-f677aea97bca`. |

The exact existing-check completion transcript was:

```text
C487: 35 passed, 0 failed, 35 rows
C487 HARNESS EXIT CODE: 0  (PASS)
```

A probe's exit 0 means the described defect trigger was observed and the probe completed. It does not mean the affected behavior is correct. No full Antiphon suite or live Full qualification was run by this task.

## Replay notes

The local drivers are in `/work/worktrees/ws-08/logs/a5e3ce5c-nightly-audit/`; they are ignored generated evidence and are not part of the pushed deliverable. The durable descriptions and transcripts above contain the inputs and observations. To reconstruct the probes in a disposable directory, build/load the audited `Antiphon.Nightly` and its write-behind dependency, dot-source `nightly-common.ps1`, `nightly-policy.ps1`, `nightly-coverage.ps1` and `test-phase-evidence.ps1`, then use these schedules:

1. **F1:** Write an old file, make its parent non-writable, enable deferred evidence writes, queue new text, wait for `PendingLength(path)` to become negative without calling `Flush`, call `TestPhaseFiles.BoundedRead(path,4096,5000,Stopwatch.StartNew())`, then inspect `EvidenceFiles.Flush().Length`. Restore permissions and disable buffering in `finally`.
2. **F2:** Open a context and synchronize its metadata. Access `EvidenceFiles`' static `buffered` field, its nonpublic `Queue` property and that queue's nonpublic `BeforeWrite` property. Bind an `Action<string>` which signals entry and waits on a `ManualResetEventSlim` for at most 15 seconds. Append with `Add-TestPhaseExecutedChunk`; compare `Read-TestPhaseGenerationPrefix` with `TestPhaseJournalRead.ReadPrefix(dir,1,false,list)`. Close the context and acquire/release `writer.lock` with `FileShare.None`. Always clear the seam, signal release and disable buffering in `finally`.
3. **F3:** Use the inventory below. Set its timestamp to one minute ago, obtain an index entry, then atomically replace it with equal-length text changing only `u1` to `u2`, and restore that timestamp. Compare required UIDs from the reused entry with `Read-NightlyDiscoveryDocument`; pass a terminal `Uid=u1, Outcome=Passed, ClassName=N.C, Type=C, Method=M` to `Test-NightlyExpandedRowsPresent` for each set.
4. **F4:** Build the two source revisions separately. In a fresh process load the old dependency DLLs/core with `Add-Type -Path`, dot-source the current loader, call `Test-NightlyCoreAvailable`, then dot-source current coverage and call `Get-NightlyExpectedSkipRosterDigest -Entries @()`. Record the loaded assembly path and hashes separately from checkout SHA.
5. **F5:** Write the inventory below with UTF-8, UTF-8 BOM, UTF-16 LE, an inserted `/* retained inventory */` comment after the first brace, and a comma before the final brace. Compare the old function body from `git show 95d43ee27da2:scripts/lib/nightly-coverage.ps1` with the current reader.

```json
{"format":"antiphon-tunit-discovery-v1","tunitVersion":"1.44.0","mtpVersion":"2.2.2","assemblyHash":"hash","nodes":[{"uid":"u1","type":"C","namespace":"N","method":"M","categories":[],"state":"Discovered"}]}
```

## Not done, noted

Minimal fix ideas only, not designed or implemented: F3 require trustworthy content identity for verdict-bearing cache hits; F1 preserve and surface flush failures; F2 bind acknowledgement/lock release to the required publication boundary; F4 validate the loaded core against the requested build identity; F5 retain equivalent decoding/parser behavior or explicitly version the narrower discovery format.

## Disposition

Root-cause mechanisms are confirmed under the stated local scenarios. Live incidence remains unknown and requires the caller's read-only desktop artifact checks; an end-to-end false Full pass was not demonstrated. Plan should triage F3/F1/F2 first, with the inherited/new distinction preserved. F4 and F5 have narrower triggers and should not be represented as known failures of this fresh, pinned run.

--- next stage ---
next: plan
handoff: Triage the five reproduced nightly harness mechanisms, prioritizing stale discovery-cache coverage and deferred-evidence loss; preserve inherited/new distinctions and check the live Windows run artifacts before claiming incidence. No production fix was made.
artifact: docs/investigations/2026-11-19-nightly-harness-audit-astra.md
