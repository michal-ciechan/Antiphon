# Review 12a27dfe: manifest publication plan, round 2 (CARD-1514)

Subject: `docs/superpowers/plans/2026-10-30-card-1514-incremental-manifest-publication-plan.md` on
`feat/card-task-9fe15cf1` tip `636fdce7bc10801c58bf0506bcdc0917b57a7a7c` (task
`9fe15cf1-2d15-4a0f-b600-7a496fec053d`). This was a read-only design re-review on server2/Linux.

**Verdict: not yet sound for Code. Three defects (D-1..D-3) and five advisories.**

Round 1 found five defects. All five are substantively addressed:

| Round-1 defect | Status |
|---|---|
| D-1 | Closed |
| D-2 | Mostly closed; residual is D-2 below |
| D-3 | Mostly closed; residual is D-1 below |
| D-4 | Closed, with advisory A-3 |
| D-5 | Closed |

Two residuals are precise and small: the timezone and comparer gaps in the equivalence rule, and the invalid or torn committed state. A third gap is that the new gap/recovery rows have no named oracle for the summary `complete` flag.

## Checks performed

### Round-1 D-1: regression rows (closed)

The plan adds R-4..R-8 as CP-6..CP-9, plus CP-11. I re-verified each count against the source at S3 `5548074c83f3` (which contains S1 `cbdf5f76`):

| Row | Test | Source count | Namespace / notes |
|---|---|---|---|
| CP-6 | `RcFullTimingContractTests` | 10 `[Test]` | `Antiphon.Tests.Scripts`; same 10 at S1 |
| CP-7 | `TestPhaseSupervisorTests` | 2 | `TestHelpers` |
| CP-8 | `PosixHarnessToolTests.Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path` | 1, at `:30` | |
| CP-9 | `RcFullWatchdogHarnessTests.HeartbeatExistingContract` | 1, at `:37` | Present at both fb6c8661 and 5548074c |
| CP-3 | `C1514T_MergeAndQuality` | 6 `[Arguments]` | |
| CP-4 | `C487_G059` | 1 | |
| CP-5 | `ReleaseGateAuthorityContractTests` | 3 | |
| CP-11 | `C1514T_PS51AndRerun` | 1 | Skips off Windows (`:98-100`), so it is correctly Windows-only and OWED |

### Importer run (mine)

- I built the tool through the build slot (lease `de0f0fb1…`, waited 0 s, held 7 s), then ran the import through the slot (lease `0095dd39…`, waited 0 s, held 2 s).
- Result: `imported 11 rows`.
- Totals from the manifest: 3 builds, 41 min results, 49 estimated minutes, all whole-minute. That splits into 32 portable and 17 native.
- Output went to the ignored `.antiphon/12a27dfe-review/`.
- I removed the owned `bin-12a27dfe-rev` outputs from `tools/Antiphon.Checkpoints` and `src/Antiphon.HostCleanup`.

### Round-1 D-2: generations (closed except the residual in D-2 below)

- D-14 defines immutable generations, a `current.json` pointer, and seeded successors.
- Rerun into a sealed root, traced: the successor's seed is the predecessor's v1 bytes, and settlement is deferred to the first new successful chunk. That is exactly what the old writer does when it reads the existing file, so rows are neither refused nor lost.
- Crash recovery:
  - Unsealed with the same identity (the qualification loop's `RunId` = `$invocationId`, `nightly-tests-impl.ps1:1001`): resume.
  - Different identity: successor carrying gaps.
- Kill boundaries: a kill between temp write, flush, rename and ack is covered (V-5). Only a renamed file counts as committed.

### Round-1 D-3: byte equivalence (closed except the residual in D-1 below)

I probed pwsh 7.5.4 (read-only scratch copy of the S3 serializer), running every case under TZ=UTC, Asia/Kolkata and America/New_York:

- **Fixed point holds after one settlement** for all of these:
  - `Z`, `+05:00`, fractional-second, `/Date(…)/` and already-settled strings.
  - Doubles: `1/3`, `1.2345678901234567`, `1E-05`, `1E+21`.
  - `-0` → `0`: it parses as Int64.
  - `3` → `3`: Int64.
- **TZ dependence:** offset-bearing strings settle to TZ-dependent text, for example `2026-10-30T03:02:00+05:00` becomes:

  | TZ | Settled text |
  |---|---|
  | UTC | `10/29/2026 22:02:00` |
  | Asia/Kolkata | `10/30/2026 03:32:00` |
  | America/New_York | `10/29/2026 18:02:00` |

  See D-1.
- **The old replacement test uses PowerShell `-eq`, which is `InvariantCultureIgnoreCase`:**
  - `"a<U+00AD>b" -eq "ab"` → True.
  - Composed é `-eq` decomposed é → True.
  - Under `tr-TR`, `"i" -eq "I"` → True.
  - A PowerShell `@{}` index (OrdinalIgnoreCase) and `OrdinalIgnoreCase` both say False.

  See D-1.

### Verdict, ledger and receipt impact

- `groups-executed.json` is read only by `ConvertTo-TestPhaseSummaryRows` (`test-phase-evidence.ps1:1562-1573`), which uses it to map TRX to group and suite.
- `timing/summary.json` has no consumer outside the summarizer.
- `release-authority.ps1` and `nightly-coverage.ps1` take no timing input. There is no recursive Get-ChildItem in the run, coverage, release or tests libraries.
- The run `summary.json` (`nightly-tests-impl.ps1:1657-1688`) takes `timingChunks` from the qualification-timing JSONL, not from the manifest.
- **So a projection difference, including a gap, cannot change the coverage ledger, the receipt or the qualification verdict. Its effect is limited to diagnostic group/suite labels and summary quality.**

### Round-1 D-4: retries and gaps (closed)

- D-16 allows two commit attempts, then a gap envelope, then a single next-write rescan; later chunks continue.
- V-11 covers cases (a)–(d).
- A gap never changes the test or coverage verdict. That is correct.
- It is loud in `groups-executed.state.json` (`publicationComplete=false`).
- The v1 file itself silently lacks the chunk, exactly as the old writer did on a failed publish. That is acceptable, given the byte oracle.
- See advisories A-2 and A-3.

### Round-1 D-5: liveness (closed)

- V-13 now runs the real loop, and both of its mutations really go red:
  - Removing the stamp: `Test-NightlyFreshEvidence` checks the log content for the invocation ID (`nightly-coverage.ps1:673-677`), so it fails with `previous-invocation`.
  - Dropping the journal commit: no sequence growth.
- The live signal is named: `(generation, lastCommittedSequence, recordKind, publicationQuality)`.

Readers I confirmed:

- The S3 heartbeat reads TestNodeUid and state from `.diag` at a byte offset (`qualification-supervisor-heartbeat.ps1:366-420`). It never reads an mtime.
- `Get-NightlyLatestDiagnosticLog` sorts by mtime only within per-chunk diag directories (`nightly-coverage.ps1:585-595`).
- `test-qualification-timing.ps1:953` reads `*-tests.log` recursively, which the journal's `.json` files do not match.
- `test-phase-evidence.ps1:2285` takes the mtime of qualification-phase shards, not of the manifest.
- No checked-in watch-qual or supervise-rc implementation exists.

The operator handoff list at plan `:168` is adequate. **Hand the operator:** the deployed `supervise-rc.ps1` source digest and its invocation, any orchestrator watch command lines, and Windmill live definitions. They must check each for `groups-executed`/timing-directory size, mtime, chunk-count or recursive reads, and for timer-reset use.

### Round-1 D-1 consumers and the advisories (closed)

- Round-1 A-1 (sequence differential): now the V-4 64-event differential, plus the V-16 full replay (OWED).
- Round-1 A-2: protocol step 6 now says "each terminal outcome category".
- Round-1 A-3: kept.
- Round-1 A-4: re-anchored to S1/S3. See A-1 below for the new S3 drift.

### Branch checks

- `check-evidence-diff.ps1 -Repository . -BaseRef 0e7dd390520a43ab330396a72e9085a8691ac332 -HeadRef HEAD`: 19 commits, 0 violations.
- `git merge-tree --write-tree origin/master HEAD` against master `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`: clean.
- Changed files: the plan, the investigation, and the carried round-1 report.

## Defects

**D-1: The D-15 equivalence precondition omits host timezone, and leaves the replacement comparer underspecified.**

- Where: D-15 table row 2 and `:117-121` ("same pwsh 7 runtime/culture", "existing case-insensitive PowerShell equality"); protocol step 5 ("indexed ordered multimap"); V-3.
- Failure 1, timezone:
  - The old writer settles a row in the *writer* process at the next publish.
  - The candidate defers settlement to reduction: at finalization, at `-RecoverExecutedManifest`, or on a recovery host.
  - Newtonsoft converts an offset-bearing date-shaped string to local time, so the settled bytes depend on the TZ of whichever process reduces.
  - So a root recovered on another host, or a retained V-3 expected output generated on server2 (UTC) and checked under another TZ, gives different bytes. The plan's equivalence condition says only runtime/culture, not TZ.
- Failure 2, comparer:
  - An `@{}` or OrdinalIgnoreCase index gives a different result from the old `-eq` (InvariantCultureIgnoreCase) on soft-hyphen or composed/decomposed suite and chunk keys.
  - The old writer *replaces* in that case; the candidate would *retain both* rows. That is a byte and row-count difference that V-3's "Unicode" inputs do not necessarily hit.
- Production impact: none on verdict, ledger or receipt (diagnostic only). It does break the stated exact-byte guarantee and makes V-3 non-deterministic across hosts.
- Fix, either of two options:
  - Preferred, because it removes the reducer's dependence on runtime and TZ, matching the old writer exactly: at commit time, the writer also stores the row's settled form and settled keys. That is one parse of the current row, constant per chunk.
  - Otherwise, add host TZ to the homogeneity precondition and to the provenance.

  Also:
  - Name the comparer: `StringComparer.InvariantCultureIgnoreCase`, or PowerShell `-eq` itself.
  - Add these V-3 cases: an offset date under two pinned TZ values, plus soft-hyphen and composed/decomposed key pairs.

**D-2: Invalid or torn committed state permanently disables the journal for the root and every later invocation, with no recovery rule.**

- Where: D-14 table, last row ("Invalid committed state … Refuse diagnostic initialization"); protocol step 4; V-6; `-RecoverExecutedManifest` (`:141`).
- Failure:
  - Step 3 promises process-kill durability only, not power loss.
  - So after a host crash, the highest record can be torn or zero-length, or a middle rename can be missing.
  - V-6 then makes the root refuse. That is correct for reads.
  - But a rerun into that root (a continued `-RunId` or the shared selection-attempt `TimingRoot`, `nightly-run-impl.ps1:481/588`) is also refused for its entire up-to-23-hour run. All its chunks get only `phase-summary-failed`.
  - The explicit recovery command also refuses. The plan specifies no way for an operator to restore journaling short of a manual delete.
  - Malformed legacy v1 input (the "Legacy projection only" row) is likewise unspecified.
  - This is the root-level form of the poisoning that round-1 D-4 removed. The brief asked for a torn-last-record recovery rule, and none is given.
- Fix:
  - For an unsealed generation whose committed state is invalid, a new authorized invocation creates a successor. Its seed is either the longest valid contiguous prefix or empty.
  - Record the invalid predecessor's identity, digest and reason as explicit unresolved provenance (`publicationComplete=false`).
  - Never adopt stale flat JSON, never mutate the predecessor, and never seal it.
  - Specify the same handling for malformed legacy v1.
  - Add a V-6/V-7 case: a torn last record, then a second process publishes its own chunks into a successor, and the summary and state report the predecessor corruption.

**D-3: The gap witnesses do not state the summary `complete`/`quality` outcome, and the plan's text on `complete` is ambiguous.**

- Where: `:87` ("legacy timing-summary `complete` … semantics stay unchanged"), D-16 `:135` ("summary exposes `publication-gap` quality"), V-10 and V-11.
- Failure:
  - `ConvertTo-TestPhaseSummaryRows` sets `complete = (quality.Count -eq 0)` (`test-phase-evidence.ps1` ~`:1764`).
  - If `publication-gap` enters `quality`, `complete` turns false. That is loud and correct, but `:87` reads as if it must not change.
  - If `publication-gap` is reported elsewhere, `complete` stays true, and a chunk's TRX rows just lose their group/suite labels. That is silent.
  - Neither V-10 nor V-11 asserts which. A missing companion, or a state/projection digest mismatch, likewise has no named summary outcome.
- Fix:
  - State that `publication-gap` (and `publication-state-mismatch`/missing companion) joins `quality`, making `complete=false` whenever state has gaps or fails validation.
  - Assert `complete=false` plus the quality token in V-11 (cases b and c) and in V-10 (stale or mismatched companion).
  - Note that a v1 `groups-executed.json` alone is never evidence of publication completeness.

## Advisories

- **A-1, S3 drift.** S3 `ee7d0d23` has moved from `fb6c8661` to `5548074c83f3`. The one new commit removes `if ($run.TimedOut) { $coverageComplete = $false }` at `nightly-tests-impl.ps1:1584` and adds a watchdog witness. Anchors before `:1584` are unchanged; the final summary moves from `:1695` to `:1694`. There is no overlap with the publication hunks. Re-anchor at commissioning, as D-10 already requires. S1 is still `cbdf5f76`.
- **A-2.** Give the per-gap wording an explicit oracle (see D-3). Otherwise D-16's behaviour is sound: a gap never alters a verdict, which is correct.
- **A-3.** D-16 and the performance budget allow "repeated exceptional scans" during a continuing outage. That is O(prefix) per chunk, which during a long outage reproduces the quadratic cost (up to the 40–80 s rescan per chunk). Fix: one rescan per episode, then reuse the validated in-memory boundary and probe only the next intended name until a commit succeeds.
- **A-4.** The legacy oracle's "retained expected outputs" (V-3) should record the generator's runtime, culture and TZ next to each digest, or run the pinned function live on the same host. This follows from D-1.
- **A-5.** `slow-tests-allowlist.txt` (M3) is accepted as a document by `audit-land-verification-precheck.ps1:242`, so D-12 ("no definition files") holds. Still, confirm the allowlist is not an input to the policy hash before CP-10, since `tests/test-execution-policy.json:1128` names it.

## Mutations against the witness set (in my head)

| Mutation | Result |
|---|---|
| Cumulative rewrite restored | V-1 and V-2 red |
| Projection dropped | V-2 and V-9 red |
| Projection between chunks | V-9 and V-13 barrier red |
| Stamp removed | V-13 red (`previous-invocation`) |
| Journal commit dropped | V-13 and V-2 red |
| Context poisoned forever | V-11 red |
| Attempt in the replacement key | V-4 red |
| OrdinalIgnoreCase index | Not discriminated (D-1) |
| Deferred-settlement TZ | Not discriminated (D-1) |
| Torn record, then rerun | Specified as refusal (D-2) |
| Gap with `complete=true` | Not asserted (D-3) |

## Verification profile

This is a Final profile on a docs-only plan. Its CP rows name tests that do not exist yet. No ordinary V/R row or Unit-lane run applies, and there are no checkpoint receipts. Therefore:

- ordinaryScopeCompleted is None.
- reviewedSourceClean is false.
- Every PC stays pending, as do CP-10/CP-11 (Windows), the full-run replay and the private watcher inventory.
- The importer run, the pwsh probes and the static greps are plan validation, not test receipts.
