# CARD-1514 M1+M2 Review (task ea1c8636)

Subject: `feat/card-task-695a5150` tip `190d9c17ed5baebd99bfd7834ffbbe878a5f39d5` (code `3dd236bb3`, M1 `597848127`, red parents `c214e6715` / `28df13d78`), owner task `695a5150-a1b0-4b62-a145-3f669f56b1e7`. Read-only: no subject edits. All probes ran on `git archive` scratch copies under the session scratchpad.

**Verdict: the protocol is sound where it matters most.** I found no path by which the journal, projection, state file or recovery output can change the qualification verdict. Crash-resume is byte-exact. Commit-time settling is timezone-proof. Per-chunk cost is O(1). Three defects remain: one piece of D-14 has no witness, the summary join regresses on a successor generation, and V-12 cannot go red on its named claim. Next: code.

## Checkpoints (rerun at the tip, one run)

Run `20261030-103728-f822`, verdict GREEN exit=0, `validate`: `CHECKPOINT SOURCE VALID source=190d9c17e... rows=9`. All rows sourceState=clean, buildSource=verified, dirty=0. unlisted: none. Wall 42m37s (CP-1 build 946 s, which includes the slot wait; CP-2 build 580 s), 3 builds, max-concurrent 1.

| CP | executed/passed | failed/skipped | build |
|---|---|---|---|
| CP-1 | 8/8 | 0/0 | ok |
| CP-2 | 6/6 | 0/0 | ok |
| CP-3 | 6/6 | 0/0 | reused |
| CP-4 | 1/1 | 0/0 | reused |
| CP-5 | 3/3 | 0/0 | reused |
| CP-6 | 10/10 | 0/0 | reused |
| CP-7 | 2/2 | 0/0 | reused |
| CP-8 | 1/1 | 0/0 | reused |
| CP-9 | 1/1 | 0/0 | reused (the test asserts PASS=17, FAIL=0 and `passed=17 failed=0` itself) |

CP-10 and CP-11 (native Windows) were not run and stay owed, as do the historical replay, p95, positive controls and the deployed-watcher inventory. No test was removed or relaxed: the only test-file deletions in `192f15240..HEAD` are the replaced flat-writer body, and every pre-existing consumer class is green.

## (1) Verdict invariant: holds

- **Readers of `groups-executed.json`:** in production these are only `scripts/lib/test-phase-evidence.ps1` (seed import at :2121, `Get-TestPhasePublicationQuality` at :2561, `ConvertTo-TestPhaseSummaryRows` at :2832) and `scripts/summarize-test-phases.ps1:29`.
- **Readers of `groups-executed.state.json`:** only `Get-TestPhasePublicationQuality` at :2566.
- **Readers of the journal:** the same library plus the summarizer.
- **Coverage, release and gate libraries:** there are no hits in `nightly-coverage.ps1`, `release-authority.ps1`, `release-gate.ps1` or any C# server or tool.
- **How the verdict is computed:** `coverageComplete`, `testsPassed`, `succeeded`, `outcome` and `reasons` are computed in `nightly-tests-impl.ps1:1640-1655` and `:1712-1716` from suite results. `Get-QualificationTimingReport` (:4393) reads only `*.timing.json` sidecars.
- **Probe:** I ran `Invoke-AntiphonNightlyTests` through the harness seams, 6 chunks each, in five configurations: baseline; no journal (every context refused); commit faults on chunks 2-5; a pre-existing journal with a record torn mid-file; and the writer lock held by another handle for the whole run. Exit code, outcome, succeeded, coverageComplete, testsPassed, reasons, policyHash and the normalised suite results were identical in all five. Only the timing quality changed: `publication-gap` for the faults and `publication-corruption` for the torn record.
- **State file deleted:** `publication-state-missing` appears and `complete=false`. `Invoke-TestPhaseExecutedRecovery` (a sealed generation with matching digests) then restores a clean pair.

## (2) Crash-resume: byte-identical

The author's tests inject crashes in a child process. A `Wait-TestPhaseCommitBarrier` seam parks the child at one of four stages and the parent `Stop-MpProcess` kills it. The stages are BeforeTempComplete, AfterFlushBeforeRename, AfterRenameBeforeAck and AfterAckBeforeProjection. V-5 compares only chunk ids after the resume.

My probe uses the real persistent-context path:

1. Publish a, then park while committing b.
2. `kill -9` at each of the four stages.
3. Resume in a new process: re-publish b and c, then seal.

The resumed `groups-executed.json` was byte-identical to an uninterrupted a,b,c run (sha `8b5d1a96df8b11c3...`) at all four stages. There was 1 generation and `publicationComplete=true`. Before the resume there were 1/1/2/2 committed records, as expected.

There is no barrier at the projection-then-state boundary. That case reduces to a missing or mismatched state file, which is flagged (see (1)).

## (3) D-1/D-2/D-3 closures

- **Timezone matrix:** v3-write and v3-reduce over 4 writer TZs × 4 reducer TZs (UTC, Asia/Kolkata, America/New_York, Pacific/Chatham +13:45): 16/16 green. The production bytes equal the legacy oracle per writer TZ.
- **Corpus:** it covers offset dates, soft hyphen, composed and decomposed accents, case-different keys and Turkish I.
- **Oracle:** I checked it against `192f15240`; it is a verbatim copy of the old writer.
- **Successor and gaps:** torn and malformed input gets a successor from the valid prefix. In my torn probe the projection was `pre-1` plus the new chunks, with `publication-corruption` flagged. A gap gives `complete=false` (V-11).

## (4) Cost: O(1) per chunk

**From the code**, each chunk does the following in `Add-TestPhaseExecutedChunk` (:2339):

- reads `current.json` (pointer only);
- serialises the current chunk only;
- writes one record: temp file, `Flush(true)`, rename, then reads back its own record;
- adds the record to an in-memory list.

There is no prefix read except `Invoke-TestPhasePrefixRescan`, which runs once per outage (`RescanAttempted`). V-11 asserts at most 2 prefix reads over a 32-chunk outage. The O(N·M) reduction and projection run once, at the terminal boundary. `Complete-TestPhaseLaunch` still projects per call, but no production caller uses it.

**Measured** on this host (server2, load average about 45 on 24 cores): 200 chunks of 90 outcomes each, through `Write-TestPhaseExecutedManifest` with the persistent context.

- Median per chunk: 364 ms for chunks 21-60 and 243 ms for chunks 161-200.
- Linear slope from chunk 20 on: −0.56 ms per chunk, i.e. flat.
- 200 chunks took 84-94 s in total. The terminal projection took 1.1-2.7 s, and the flat file was 1,305,992 bytes.
- For comparison, the old model predicts about 20-26 s per chunk at a 200-chunk prefix.

## (5) Witnesses

**Red at the parents:** I ran the harness cases directly with pwsh against an archive of each parent. Every case is assertion-red, not fixture-red.

- `c214e6715`: v1 `prior-rows-or-cumulative-bytes`, v2 `linear-bound`, v4 `settled-keys-absent`, v5 `barrier-not-observed`, v6 `corruption-successor-absent`, v7 `exclusive-lock-absent`, v8 `live-reader-barrier-absent`.
- `28df13d78`: v9, v11, v12, v13, v14 `flat-present-before-terminal`; v10 `journal-only-lost-cross-suite-join`.

**My mutations on the tip:**

| Mutation | Result |
|---|---|
| MA: project on every chunk in the persistent path (:2678 `if ($true)`) | RED: v9, v12, v13 |
| MC: settle at reduce time (`Json = ConvertTo...(-Item (FreshJson \| ConvertFrom-Json))`), Kolkata writer → New York reducer | RED: `reducer-timezone-bytes-differ` |
| MD: gaps do not mark the publication incomplete | RED: v11 `outage-marked-publication-complete` |
| MB: skip successor seeding (:2023 `if ($true)` → empty generation) | **SURVIVED**: v1, v4, v5, v6, v7, v8, v10 all green (D-1) |

## (6) S3 and rc interplay

I cherry-picked `de4863ac3 c214e6715 597848127 28df13d78 3dd236bb3 190d9c17e` onto `origin/rc/2026-10-27` (`e4c9547e1`) in a scratch worktree, since removed.

- All six picks were clean.
- The resulting `nightly-tests-impl.ps1`, `test-phase-evidence.ps1`, `summarize-test-phases.ps1` and harness are byte-identical to the branch, so the S3 launch hunks are kept.
- rc normally carries no plan docs, so the port may drop `de4863ac3`.
- `git merge-tree --write-tree origin/master HEAD` (master `698bf1b73`) exits 1. The conflicts are in `docs/testing-and-build.md`, `nightly-tests-impl.ps1` and `test-phase-evidence.ps1`; the author already reported these. I did not resolve them.
- `check-evidence-diff.ps1 -BaseRef 192f15240 -HeadRef HEAD`: commits=6 entries=1 violations=0, exit 0.

## Defects

**D-1: D-14 valid-prefix successor seeding has no witness** (`scripts/lib/test-phase-evidence.ps1:2023`; `ManifestPublicationHarness.ps1:660-676` (V4Successor) and `:729` (V6)).

- **Where:** the successor path in `Import-TestPhaseExistingGeneration`.
- **Failure:** replacing it with `Initialize-TestPhaseEmptyGeneration` for every corrupt, sealed or identity-changed generation leaves every witness green. In that state a torn record at sequence k, followed by a resume, silently drops all chunks committed before k from the projection.
- **Why:** V4Successor asserts only `generations>=2`. V6 asserts only that the malformed bytes are preserved and the flags are set.
- **Fix:** add an assertion-red witness that the successor's seed rows and projection bytes equal the parent's valid-prefix projection. Cover a torn record at k>1, a sealed resume and a source or writer-environment change.

**D-2: the summary join drops seeded rows after a successor** (`scripts/lib/test-phase-evidence.ps1:2808-2833`).

- **Where:** when `current.json` exists, `ConvertTo-TestPhaseSummaryRows` builds `trxGroups` and `trxSuites` only from the current generation's chunk records and skips the flat file.
- **Failure:** rows seeded from a legacy flat file, a D-14 successor, a sealed resume or a writer-environment change lose their TRX→chunk/suite mapping. Probe: context 1 publishes `chunk-a` (suite-a) and seals `orchestration-error`; context 2 publishes `chunk-b`. The resulting `tests.csv` has groupId `""` for `id-a` from the journal reader, but `chunk-a` from the flat reader on the same files. With an empty suite, the cross-suite filter (`bothSuites`) is also disabled for those rows. This affects the timing summary only, not the verdict.
- **Why:** the plan requires that legacy rows and joins remain supported. The flat projection itself is correct.
- **Fix:** derive the maps from `Get-TestPhaseReduction` (seed rows plus records), or from the seed cache plus records. Extend V-10 with a successor and a legacy-seed case.

**D-3: V-12's ledger/receipt claim cannot go red** (`tests/Antiphon.Tests/TestHelpers/ManifestPublicationHarness.ps1:1333-1350`).

- **Where:** `authority.json`, `ledger.json` and `receipt.json` are fixtures the test writes into `$Work/candidate`. That directory is outside the qualification's LogRoot and TimingRoot, and `Test-ReleaseGateExecutionLedger` re-validates the same fixture.
- **Failure:** before/after is a self-compare. Only the `flat-at-barrier` assertion in V-12 is live. Apart from V-11's gap case, no witness shows that the verdict is unchanged under corruption, a torn record, a missing state file or a refused writer.
- **Why:** this is the CARD's central invariant.
- **Fix:** compare the returned object and the verdict fields of LogRoot `summary.json` between a no-journal run (refused context) and runs with a gap, a torn record and a held lock. My probe shows the shape.

## Advisories

- **A-1:** when the writer is refused (lock held or unsafe root) and no journal exists, the timing summary carries no publication token. Only the LogRoot `phase-summary-failed` marker records the refusal (probe `held`). Also, if `Open-TestPhaseExecutedContext` fails in `Invoke-AntiphonNightlyTests`, every chunk falls back to the non-persistent open-and-project branch (:2663/:2678), which is O(n) per chunk again for that run.
- **A-2:** `summarize-test-phases.ps1` and `-RecoverExecutedManifest` resolve only `RunDirectory/timing` and have no `-TimingRoot`. A run with a separate TimingRoot is invisible to them: no publication line is printed and nothing is recovered.
- **A-3:** a `-ProjectOnly` projection takes the exclusive writer lock. Two concurrent explicit projections leave the loser silently refused (seen when v3-reduce ran in parallel against one source).
- **A-4:** V-5 compares chunk ids only. My byte-exact resume probe passes; consider asserting bytes.
