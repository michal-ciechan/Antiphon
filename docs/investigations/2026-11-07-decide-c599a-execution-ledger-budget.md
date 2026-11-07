# Decide: C599A_ExecutionLedger 300 s overrun

Date: 2026-11-07. Branch `feat/card-task-60d37923`. Source measured below is `b6052173f4b8483b886bd63750ef730eb22467dc`. Those ledger files match the failing rc pin `0e7aef0379a073c206e8398dcaed539bc33eaf9b` (`git diff` empty for `scripts/test-release-gate.ps1`, `scripts/lib/release-authority.ps1`, `ReleaseGatePublicationAuthorityTests.cs`, and `ScriptHarnessProcess.cs`).

The Windows failed-first log directory `C:\Antiphon\releases\candidates\rc-2026-11-09-0e7aef0379a0\logs\2026-11-07-1608-c3f65ddb` is not on this runner. Durations below come from the batch-112 report on `origin/feat/card-task-ac04d0ad` (`.antiphon/task-ac04d0ad.md`, tip `c2c9a153c464dc84714bcefb30f1a540d99c5432`) and from one completed Linux run of the same case.

## Outcome

`C599A_ExecutionLedger` is a real failure of a 300 s child budget, not a logic regression in batch 112. CARD-1731 already removed the redundant snapshot restore. What remains is one full production publication-gate per variant. On this host that gate is 107 of 131 seconds. Under the pre-cut smoke's load the same unchanged method was killed at 300.202 s after four of its five rows had passed. Raising the shared 300 s default is not justified. The residual belongs on open CARD-1731.

## What one row does

`ReleaseGatePublicationAuthorityTests` is `[Category("Integration")]` and `[Category("Slow")]`, with `ParallelLimiter<ProcessSpawnLimit>` (`tests/Antiphon.Tests/Scripts/ReleaseGatePublicationAuthorityTests.cs:32-35`). It is not a Unit test. `C599A_ExecutionLedger` (`:80-85`) launches one `pwsh` child on `scripts/test-release-gate.ps1 -Case C599A_ExecutionLedger`. The child budget is `ScriptHarnessOptions.Default`, 300 s execution plus a separate 10 s cleanup and 30 s post-exit drain (`tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:18-24`). There is no per-method `[Timeout]`. There is no sleep and no poll loop.

`Test-C599A_ExecutionLedger` (`scripts/test-release-gate.ps1:2893-2982`) does this once:

1. `New-C599PublishFx` builds one scratch git repo (one policy file, one commit), freezes a 13-chunk / 19-UID ledger, and starts a loopback card stub.
2. `Save-C599ASnapshot` reads that release tree once.
3. One control `Get-C599AGate` must accept the fixture. That is row V-12.
4. Each later variant mutates one fact, runs either `Get-C599AGate` or the real `publish-release.ps1`, then `Restore-C599ASnapshot` in `finally` (`:2386-2408`).

The five named rows and their variant counts, from the case body (the completed run recorded 47 restores, which matches):

| Row | What it mutates | Variants |
|---|---|---:|
| V-12 | none; the valid control gate | 0 (one control gate) |
| G-226 | fail the first and last chunk of each of the 6 native suites, plus one publisher fail | 13 |
| G-287 | parent-relative path, absolute path, and a link via both gate and publisher | 4 |
| G-288 | 3 prerequisites × 7 bad shapes, plus one publisher | 22 |
| G-289 | bad chunk/roster exits, a zero-execution TRX, plus one publisher | 8 |

47 variants. Each gate re-enters production `Test-ReleaseGateAuthority` (`scripts/lib/release-authority.ps1:894-914`), which:

- runs real `git` for `remote get-url origin` (`:190-196`) and `rev-parse` plus `cat-file` of the pinned policy blob (`:249-262`), each child capped at 30 s (`:38`, `:109`);
- re-hashes the seven named production scripts (`:49-57`, `:597`);
- reads the ledger, plan, discovery, and all 13 TRXs;
- walks every evidence path for escape and reparse points (`Resolve-ReleaseAuthorityEvidencePath`, `:410-432`);
- GETs the fixture card on `127.0.0.1`.

Publisher variants add one `publish-release.ps1` invocation. Remote git and GitHub are file seams. The fixture checkout is not the Antiphon repo.

## Where the time went

### This run (completed)

Lease `4e721951-850e-4171-a4d4-13fb59f8d0c5`, waited 0 s, held 136 s. Host `6f03ea7dd3f9`, 24 CPUs. At completion the load averages were 7.77, 14.57, 23.38. That is not a quiet host. Case `C599A_ExecutionLedger` at `b6052173`, 5 passed, 0 failed. Phase file written because `C1731_TIMING_ROOT` was set (the rc smoke does not set it):

| Phase | Seconds | Share of 131.028 s body |
|---|---:|---:|
| Fixture, including real git init | 2.793 | 2% |
| Save snapshot | 0.182 | under 1% |
| Restore, 47 calls | 7.735 | 6% |
| of which directory scan | 7.658 | |
| of which byte compare | 0.047 | 1,175 reads, 1,114 unchanged, 61 writes |
| Gate (`Get-C599AGate` only) | 106.595 | 81% |
| Teardown | 0.270 | |
| Remainder (mutations, 4 publisher runs, assertions) | 13.722 | 10% |

Remainder is body minus the four measured phases. It is not zero. Git, HTTP, and process-startup inside the gate were not split today. The timing record says those spans stay unknown.

### Pre-fix probe, still the inner split

CARD-1731 Debug at `0dbd8295a` (docs in commit `11cdf6ecbe`, case `C599A_ExecutionLedgerMarkers`, censored at 300.048 s) attributed the gate's ledger half:

- evidence-path walks: 611 calls, 106.776 s (about 92% of ledger validation);
- TRX parse: 611 calls, 1.966 s;
- snapshot restore then: 91 calls, 160.276 s, because every variant restored twice and rewrote every file;
- real git's own exit time: 0.242–0.254 s across 114–150 commands. Spawn and pipe time on the PowerShell side was not separated, and must not be called zero.

`Resolve-ReleaseAuthorityEvidencePath` is unchanged since that probe. Today's 106.595 s gate total matches that probe's path-walk figure. The snapshot half did not: restore is now 7.735 s for 47 calls.

### The smoke that failed

rc/2026-11-09 pre-cut, server2, seven other tasks, pin `0e7aef0379a`. Six methods, one serial seat, lease `ef193da2-970c-4b63-b97c-e9fca49d82f3` held 1222 s. The TRX `.antiphon/batch112-ledger/batch112-ledger.trx` was not readable here.

| Method | Result | Duration |
|---|---|---:|
| `C599A_ExecutionLedgerCounts` | passed | 76.721 s |
| `C599A_ExecutionLedgerJoins` | passed | 160.389 s |
| `C599A_ExecutionLedgerMarkers` | passed | 212.985 s |
| `C599A_ExecutionLedgerReceipt` | passed | 216.313 s |
| `C599A_ExecutionLedgerChunks` | passed | 247.750 s |
| `C599A_ExecutionLedger` | failed, budget | 300.202 s |

Rows V-12, G-226, G-287, and G-288 had passed. The budget expired before G-289. Batch 112 did not edit this method.

CARD-1731 Code, after the snapshot repair, recorded a completed pass of the three incident methods at 167.552 s (Ledger), 213.381 s (Joins), and 143.469 s (Markers), with Markers restore at 47 calls / 8.404 s (CARD-1733 description). Same repair, three different hosts, Ledger at 131 s, 168 s, and a kill at 300 s.

## Intrinsic, load, or repo size

The floor is intrinsic and already paid: 47 full gates. At the sample above that is about 2.3 s of gate plus a cheap restore, 131 s before load gets worse. It does not grow with the Antiphon checkout. The fixture repo is one commit and one policy file. CARD-1731 measured the release tree at 21 files / 33,338 bytes. This run compared about 25 files per restore (1,175 reads / 47). The seven script hashes are seven named files, not a repo walk.

It does grow with host load. The same bytes passed in 131 s at a 1-minute load of 7.8 and were killed at 300 s with seven other tasks. Pre-fix, git's own time was a quarter-second, while PowerShell CPU for the censored Ledger sample was 311 s. The stretch is scheduler time on the PowerShell gate, not a larger git pack.

Sibling order is not a stable ranking. On the smoke, Joins was 160 s and Ledger crossed 300 s. On the CARD-1731 Code host, Joins was 213 s and Ledger was 168 s. Counts is shorter because it has about 20 variants, not because its gate is a different kind of work. Chunks has the same 47-variant shape and passed the smoke with 52 s left.

The class-cost row is a scheduler hint, not this cap: `scripts/lib/nightly-class-costs.json` gives the 15-method class 600 s, basis `observed-bound`, from a truncated Windows run (CARD-1577). The child dies at 300 s anyway.

## Budget history

| When | What changed |
|---|---|
| 2026-09-25 `c171fcc799` (CARD-0589) | Default script-child budget moved from a 120 s argument to a fixed 300 s. |
| 2026-10-04 `d0eb274148` (CARD-0806) | Owned harness kept 300 s as `ScriptHarnessOptions.Default`. |
| Until 2026-11-06 | The class comment still said 120 s, and that the split matrix "stays near 30 s". |
| 2026-11-06 `1038d07e66` (CARD-1731, batch 91) | Comment corrected to the real 300 s limit. Budget not raised. Snapshot pre-restore removed, unchanged bytes skipped, class marked Slow. Allowlist reason at `tests/Antiphon.Tests/slow-tests-allowlist.txt:9-10`. Plan text: keep 300 s until a completed post-fix sample supports a smaller cap. The Debug note refused a 600 s raise from censored samples. |

`ChangeAwareReleaseBudgetTests` still requires `ScriptHarnessOptions.Default` to stay 300 s. CARD-1722 raised only `C1313_BaselineAuthority` to 720 s and `C1313_ParameterHops` to 600 s, from named samples, via a per-case override (`tests/Antiphon.Tests/Scripts/ChangeAwareReleaseTests.cs:125-137`). No such override exists for C599A.

R5 in `docs/test-performance-standards.md` wants about 2× measured p95. Post-fix completed Ledger samples are n=2: 131.028 s (this run, load 7.8) and 167.552 s (CARD-1731 Code). Two times those figures is 262–336 s. The smoke already died at 300.202 s, so that multiple does not cover the failure it would be asked to absorb. There is no completed post-fix duration from the loaded smoke. A 39/47 extrapolation (death at the G-289 boundary) is about 362 s and is not a measurement.

## Options

### Raise the budget

Saving: this method would be allowed to finish a ~6 minute loaded run. Wall time of a passing rc seat does not shrink. Chunks (247.750 s), Receipt (216.313 s), and Markers (212.985 s) on the same smoke stay inside 52–87 s of the same cap.

Risk: 2× the completed samples is already below the observed kill. A cap that would have survived this smoke has to sit above an unknown loaded duration. CARD-1731 Debug already refused treating a censored 300 s as permission for 600 s. Widening `ScriptHarnessOptions.Default` would loosen every other script case and fails `ChangeAwareReleaseBudgetTests`.

Smallest change: a per-case override, Ledger only, Default left at 300 s, with a launch-boundary witness like CARD-1722. Still a provisional exception, not a measured p95.

### Cut the work

Saving: the gate is 106.595 of 131.028 s. The 2026-11-06 probe put ~107 s of that kind of gate in `Resolve-ReleaseAuthorityEvidencePath`. A cheaper walk that still refuses escape, absolute paths, and links would take the body from the two-minute floor toward the fixture-plus-restore neighborhood (about 11 s plus mutations), which stays under 300 s even at a 2× load stretch. That is the change that also gives Chunks headroom.

Risk: G-287 is the containment contract. Caching a "path was fine" answer across variants would hide a mutation. Faking git does not pay: git's own time was ~0.25 s. Sharing the fixture across methods saves the 2.8 s setup, about 14 s across five siblings, against a 1222 s seat. Batching refusals into one gate lets a deleted check hide behind another.

Smallest change: replace the `FileInfo` / `Get-Item` component walk in `release-authority.ps1:410-432` with a direct reparse check, keep every G-287 token, do not cache acceptance. Re-time the six methods before touching the 300 s cap.

A test-only split (G-289 in its own method) gives that slice its own 300 s. On a 362 s extrapolation the two halves might pass. It does not move Chunks off 248 s, and it multiplies fixture startup. It is containment, not the cut.

### Split classes for Unit shards

Saving: none for this kill. The class is Integration and Slow. The Unit lane filter is `Category=Unit`. `ProcessSpawnLimit` is already one process-spawning test at a time inside the assembly, so two classes still run one pwsh at a time. The 300 s cap is per child, not per shard.

Risk: allowlist, classification guards, and checkpoint filters move for no change in the timeout.

Smallest change: do not.

### Slow registry

Already done at `1038d07e66`. The allowlist reason cites the pre-repair 300 s exhaustion. Slow keeps the class out of Unit-only runs. The rc publication selection still runs it, which is where it failed. Saving: none.

## Recommendation

Keep `ScriptHarnessOptions.Default` at 300 s. Do not add a provisional Ledger override to paper over this smoke. Continue CARD-1731: the snapshot repair landed and is confirmed (restore 160 s down to 7.7 s); the deferred evidence-path walk is now the measured remainder. Next code replaces that walk, keeps G-287, and re-measures all six `C599A_ExecutionLedger*` methods on a loaded host before anyone sets a new budget. If a number is required before that lands, it is provisional, per-case, and above the unknown loaded duration — not 2× 131 s, and not a new default.

## Cards

Searches on board Antiphon (`scripts/card.ps1 search -All`): `C599A_ExecutionLedger`, `publication ledger budget`, `CARD-1731 ledger`, `redundant C599`, `CARD-1731`, `evidence-path`, `harness budget`.

No new card. CARD-1731 is InProgress (id `352d4fa7-c3aa-4e79-9042-3353d7f32038`): "Publication ledger tests (ExecutionLedger, Joins, Markers) sit at their 300 s timeout under load." Its snapshot slice landed; this overrun is the same card's unmet remainder. CARD-1733 is the allocator matrices that share the snapshot helper; it is not this method. CARD-1722 is the per-case budget pattern and stays the pattern if a later measured override is warranted.

## Plain English

- This test builds a tiny fake release and then checks 47 broken versions of it, one at a time, through the real publish rules.
- Almost all of the time is those 47 rule checks. Rebuilding the fake release is about 3 seconds. Putting the files back after each check is about 8 seconds.
- The check spends its time walking every evidence path in PowerShell. Git itself is a fraction of a second. The test does not scan the Antiphon repo, and it does not get slower just because the repo grew.
- On a moderately busy 24-core host the whole method finished in 2 minutes 11 seconds. On the pre-cut smoke, with seven other tasks, the same code was cut off at 5 minutes, late in the last row.
- The last fix already removed the wasteful file rewriting. The 5-minute limit was left as it was. Two times the finished runs we have (about 4.5 to 5.5 minutes) is still shorter than a bad-load run that could not finish.
- Raising the limit only hides the next spike, and three sibling methods are already past 3.5 minutes. The useful change is a faster path check, on the card that already owns this, without dropping any refusal.

## Uncertainties

- No quiet-host sample. This run's 15-minute load was 23.
- No completed post-fix duration for Ledger on the smoke host. 300.202 s is a kill.
- The gate's internal split (path walk versus git spawn versus HTTP) was not re-probed on this run. The path-walk figure is the 2026-11-06 Markers probe.
- The five sibling methods were not re-timed here. Their smoke durations are envelopes only.
- The Windows log directory and the batch-112 TRX were not readable from this runner.

## Not done, noted

No change to `Resolve-ReleaseAuthorityEvidencePath` and no change to the 300 s budget.
