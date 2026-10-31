# Final Review 460ee5a4: failed-first digest fix + stopped-run seed fix + combination (rc top-up 52)

Verdict: NOT clean. Each branch is sound on its own, but merging them breaks the stopped-run seed. On the combined tree the operator seed is silently replaced by canonical order (D-1, HIGH), and three committed seed tests go red. There are also three Low defects: two missing witnesses and one Windows-only link-cache gap. Route: code.

Subjects:
- (A) seed fix, `origin/feat/card-task-d63efd0a`, report `9418c99f2e2747174ef804e4606213055f4ef8f3`, code `ac2dd388850d336818850609537f088b289a99dc`. It sits on f6aabaef `a00f1b46753542a8721a111fb4c26ed1d542cf2a`.
- (B) digest fix, `origin/feat/card-task-79ae55d8`, report `39488ad52ce95d26bab9089ea8e559a33c1abc2c`, code `6a09884a8b236727c41c20b3b2c780e20b82a688`, on chain `3693d53b6`.
- Base: `origin/rc/2026-10-27` = `e0d49d221513b467f8c853f47587e688e04255e2`.

Host: server2/Linux, load average 30-34. Standing caller authority covered this review, and nothing else needed approval.

## (C) Combination

Scratch branch: `feat/card-task-460ee5a4-combined` = `4390c697e2a7255587264514260d9903fea8d719` (tree `956c521c93cd98aaa7ce2199c9809bcd4434cedf`). It was pushed. Nothing was pushed to rc, master or another task's branch.
- `c50605895` merges 79ae55d8 (`39488ad5`) onto rc `e0d49d22`.
- `4390c697e` merges d63efd0a (`9418c99f`) on top of that.
- **Textual conflicts: none.** merge-tree reported 0 conflicts in both orders (digest+seed and seed+digest give the same tree, `e9cd0f25`). The merge kept the seed early return above the try (dispatch.ps1:5) and took 79ae's replacement of the old liveness line inside the try. No hand resolution was needed.
- **Semantic conflict: D-1, below.** Do not fast-forward rc to this branch as it stands.

## Defects

### D-1 (HIGH, combination): the digest guard overrides every operator stopped-run seed with canonical order
- **Where:** `scripts/lib/nightly-dispatch.ps1:105-116` on the combined tree, inside `New-NightlyDispatchSchedule`, introduced by 79ae55d8 `6a09884a`. The block is `if($null -ne $PreviousRun){ $recordedDigest... previous-run-plan-digest-missing }`.
- **Failure:** for a seed, `Initialize-NightlyDispatchHistory` sets `PreviousRun` to the seed document (nightly-failure-history.ps1, `$previous=$history`). A seed is cross-plan and has no `canonicalPlanDigest`. Resolve returns the seed order (dispatch.ps1:5 early return). The new guard then sees an empty digest and resets `dispatchOrdered` to raw canonical ordinal order with `orderingReason=previous-run-plan-digest-missing`. The `priorFailure` partition is dropped too. The old guard skipped a truthy-absent digest, so a seed passed through it.
- **Probe:** the fixture seed was exported and ingested with the ack, then run through `Resolve-NightlyPriorFailures` and `New-NightlyDispatchSchedule`.
  - Seed branch alone: reason `operator-seed-order...`, first chunks `antiphon/new-Red:True session-runner/new-Red:True ...`.
  - Combined: reason `previous-run-plan-digest-missing`, first chunks `antiphon/new-Passed:False antiphon/new-Never:False antiphon/new-Red:True ...`.
  - The committed tests go red on the combined tree: CP-1 run `20261031-172803-8df2` gave 125 executed, 122 passed, 3 failed. The failures were `RcStoppedRunSeedTests.FailedFirstMapping`, `InvalidSeedFallsBack` and `ScheduleConservesAllChunks`. The same `FailedFirstMapping` failure reproduced in a scratch harness control.
- **Why it matters:** top-up 52 exists to run d47848cc's failed chunks first. With this tree, the operator's `-PriorFailuresPath <seed> -AcceptCrossCandidateSeed` launch is accepted and nothing fails visibly, but the ordering is canonical. This is a silent loss of the feature.
- **Fix (Code, owner 79ae55d8; it applies on top of the combination):** exempt operator seeds from the digest guard. Either use `if($null -ne $PreviousRun -and $PreviousRun.seedKind -cne 'operator-seed-from-stopped-run')`, or move the digest check into `Resolve-NightlyPreviousRunOrder` below the seed early return. Then re-run CP-1 on the combined tree. The three seed methods above are the existing witnesses, and they go red today.

### D-2 (Low, test adequacy): nothing committed witnesses the D-2 speed fix
- **Where:** `scripts/test-nightly-stopped-seed.ps1`. Every case uses the ~40-file fixture, and nothing asserts a time bound. The earlier review asked for one ("an 800-file synthetic directory under a stated budget").
- **Failure:** reviewer mutant SM2 restored the per-call, every-ancestor `Get-Item` walk (`$recheck=$true` plus `Get-Item`, nightly-failure-history.ps1:153). It survived every case:
  - SiblingTimingFromSummary green, 56 s (control 13 s).
  - LinkCacheRefusesReplacement green, 42 s.
  - ChunkUnfinishedTolerance green, 173 s (control 6 s). Its 180 s C# Timeout would trip only by accident on a loaded host.
- **Fix:** add a scale case, for example 3,300 run files plus 1,700 timing files, asserting the export takes under 60 s (measured 9.3 s, see below). Alternatively, count lstat/Get-Item calls per file through a seam.

### D-3 (Low, test adequacy): nothing committed witnesses the summary-prefix tamper recheck
- **Where:** `scripts/lib/nightly-stopped-run-seed.ps1`, the `seed-summary-tampered` line before ConvertTo-SeedEvidencePath.
- **Failure:** mutant SM7 deleted that line and survived SiblingTimingFromSummary, StoppedTimingRootAndRefusals and ChunkUnfinishedTolerance. The worker's "tampered summary" (ChunkUnfinishedTolerance `tamper=`) is really a summary naming a different runId, which is refused by run-id mismatch, not by the recheck.
- The behaviour itself is correct: my probe rewrote the summary prefix mid-export and the export refused with `seed-summary-tampered`.
- **Fix:** add a case that rewrites the prefix through `$script:NightlySeedReadBarrier` and expects `seed-summary-tampered`.

### D-4 (Low, Windows-only, fails open): on Windows the link cache cannot detect a cached ancestor swapped for a link to forged content
- **Where:** `scripts/lib/nightly-failure-history.ps1:98-111`. On Windows, `Get-NightlyHistoryNodeIdentity` returns `Dev=0` and `Ino=0` for every non-link node. `Assert-NightlyHistoryNoLink` (`$recheck = ($i -le 1)`, :153) never re-examines ancestors above the parent, and its cached-directory comparison (Dev/Ino) is always equal.
- **Failure:** probe `probe-link.ps1`, which replays the Windows branch on Linux by forcing `$windows=$true`. File one is checked, then:
  - `logs` is replaced by a link to a forged copy. File two is ACCEPTED with content `FORGED`.
  - The parent `run` is replaced by a different real directory. File two is ACCEPTED with content `FORGED`.
  - On the real Linux branch both are refused with `evidence-link`, because the parent's inode changes. A same-directory link swap of the grandparent is accepted, but it serves the same content.
  - The base code before D-2 caught a junctioned ancestor on every call.
- The worker's `swap=` witness only swaps the immediate parent (i=1), which both branches recheck.
- **Exposure:** needs a concurrent writer during a ~10 s export of a finished run, so it is unlikely in practice. But the real export runs on Windows, and the code comment claims inode protection that Windows does not have.
- **Fix:** on Windows, recheck every ancestor with `[IO.File]::GetAttributes` (microseconds; the D-2 cost was `Get-Item`), or use a real file ID (`GetFileInformationByHandle`). Add a grandparent-swap case.
- Owner of D-2..D-4: d63efd0a Code.

## Advisories (no Review rerun needed for these alone)

- **A-1 Orphan TRX is not tolerated.** A TRX with no plan row (`antiphon-all-group-105.trx`) that changes during export refuses the whole export (`seed-source-changed`): nightly-stopped-run-seed.ps1:305 reads it without `-Tolerate`, and :393 refuses any changed input missing from `$chunkInputs`. This was seen both with a live appender and through the barrier. Planned-chunk TRX appends are tolerated correctly. d47848cc is finished, so this is not hit. It fails closed.
- **A-2 Prefix close heuristic.** Read-NightlyStoppedSummaryPrefix (:91) adds a closing `}` only when the prefix does not already end in `}`. When the value just before the first bulk key is an object (a summary with no top-level `timingRoot`, so `selection` precedes `timingChunks`), the JSON is unterminated and the export refuses ("Unexpected end ... Path 'selection'"). The current producer always writes `timingRoot` (a string) immediately before `timingChunks` (nightly-tests-impl.ps1:1768), so d47848cc is unaffected. It fails closed.
- **A-3 Fallback drops the S2 partition.** Each new 79ae fallback returns raw canonical order, which also drops the S2 prior-failure partition that a run with no PreviousRun keeps. So the first run after 52 that uses a pre-52 history gets no failed-first ordering at all, because pre-52 `dispatchProgress` has no liveness or schema. This is what the brief asked for ("original order"); make it a deliberate choice.
- **A-4 SM5 survives by redundancy.** Accepting a symlinked timing root in `Resolve-NightlyStoppedTimingRoot` still refuses `evidence-link` from the walk. The behaviour holds, but the case cannot tell which layer refused.
- **A-5 Locale-dependent proc-stat check.** `IsVanishedProc` matches IOException message text. It is locale-dependent, though fine on server2. `io.HResult` carries the raw errno (3 = ESRCH) and would be locale-independent. The fix is otherwise correct and minimal: one helper, the scan uses it, `UnauthorizedAccessException` still propagates, and a red-then-green witness exists.
- **A-6 Windows path acceptance not witnessed.** On Linux a backslash `timingRoot` fails closed (`seed-timing-missing`). Acceptance of a real Windows path needs the Windows lane: the OrdinalIgnoreCase parent compare and `C:\...\logs\timing-<id>`.
- **A-7 My process slips.** I used `rm -rf` with a variable once, on my own scratch mutant trees. It was confined to the scratchpad. All later cleanup used literal paths.

## Verification evidence

### Seed checks (A), against the combined scripts
`probe-seed.ps1` builds the real layout:
- `logs/2026-10-30-0824-d47848cc` (run, no owner.json) beside `logs/timing-d47848cce896471db1cc8ca346707cad` (plans).
- A producer-ordered `summary.json` with decoy `selection.timingRoot`, `selection.timingChunks` and `selection.suites`.
- Bulk `timingChunks` holding test names and outputs that contain `"timingRoot"`, plus a top-level duplicate `timingRoot` after the cut.

| Case | Result |
|---|---|
| Scale: 3,296 run + 1,700 timing files, 246,599,014-byte summary | export 9.275 s (< 60 s); same chunk rows as the small layout |
| Read-only: before/after `find` (type, size, mtime, ctime, inode, mode) + sha256 of every file | identical |
| `timingRoot` only after the cut (test names/output/duplicate key) | ignored, correct seed |
| Duplicate top-level `timingRoot` before the cut | last value wins, `seed-timing-missing` (refuses) |
| Points at a different run id | `seed-timing-run-mismatch` |
| Outside the logs parent | `seed-timing-outside` |
| Symlink timing root / symlinked logs parent | `evidence-link` / `evidence-link` |
| Equal to the run dir | `seed-timing-outside` |
| Windows backslash path, on Linux | `seed-timing-missing` (fails closed) |
| `..` in the path | normalised and accepted |
| No summary.json | sibling discovered, runId correct |
| Wrong `-SourceRunId` / `-SourceSha` / summary runId | `seed-summary-run-mismatch` / `seed-summary-sha-mismatch` / `seed-timing-run-mismatch` |
| Summary prefix rewritten mid-export / plan file changed mid-export | `seed-summary-tampered` / `seed-source-changed` (refuse) |
| chmod-000 TRX | only that chunk: `trx-unreadable`, completed=false, its failures dropped; export ok |
| chmod-000 timing.json | only that chunk: `timing-unreadable`; export ok |
| TRX closed, no ResultSummary | only that chunk: `trx-result-open`, completed=false; export ok |
| Live appender on a planned TRX (closed / open) | only that chunk: `trx-unreadable,trx-changed` / `trx-open,trx-changed`; export ok |
| Live appender on an orphan TRX | whole export refused (A-1) |

### Reviewer mutants
Scratch copies of the combined scripts. Each mutant ran in its own build-slot lease.

Seed controls, all green: SiblingTimingFromSummary, StoppedTimingRootAndRefusals, ChunkUnfinishedTolerance and LinkCacheRefusesReplacement. FailedFirstMapping is RED on the combined tree (D-1).

| Mutant | Cases | Result |
|---|---|---|
| SM1 plans taken from the run dir again (`$planRoot=$root`) | Sibling, StoppedTiming | killed (both) |
| SM2 per-file every-ancestor Get-Item walk | Sibling, LinkCache, Tolerance | **survived** (D-2) |
| SM3 per-chunk tolerance removed | Tolerance | killed |
| SM4 = M9, drop `$closed -and` | Tolerance / BuilderReadOnly | killed (`open=completed=True`) / survived |
| SM5 symlink timing root accepted | StoppedTiming | survived, redundant layer (A-4) |
| SM6 timing-name run-identity check skipped | StoppedTiming, Tolerance | killed (both) |
| SM7 summary-prefix tamper recheck removed | Sibling, StoppedTiming, Tolerance | **survived** (D-3) |
| SM8 cache recheck leaf only (`$i -eq 0`) | LinkCache | killed (`swap=` empty) |

Digest: the control `ActualLoopFollowsSchedule -SmallLoopProbe` was 18/18 green on the combined tree.

| Mutant | Result |
|---|---|
| DM1 malformed-digest check removed | killed: short/long/nonhex became `plan-mismatch`, `chainIngress` failed |
| DM2 absent liveness trusted | killed: liveness/liveness-blank became `previous-run-order` |
| DM3 absent schema trusted | killed: schema/schema-blank became `previous-run-order` |
| DM4 producer drops `liveness='closed'` | killed: `chainPreviousOrder` failed (two-run producer to recipient) |
| DM5 blank sha falls through to the schema check | killed: candidate/candidate-blank became `previous-run-schema` |

### Digest checks (B)
- **Property probe** (`probe-digest.ps1`): real `New-NightlyDispatchSchedule` on random 1-3 suites x 1-6 chunks, random previous rows, 16 fault kinds.
  - 300 cases on the 79ae tip and 200 on the combined tree: 0 failures.
  - Every output was a permutation (no drop, no duplicate), deterministic across two calls, carried the exact expected reason, and was exactly canonical on every fallback.
- **Valid records order exactly as before:** 200 random valid records (109 with priority members), 79ae tip vs base `3693d53b6`. Output, reason, order, priorFailure flags and lanes were byte-identical.
- The ingress case table in the 79ae report matches the code and the committed `chainIngress` rows.

### Checkpoints on the combined tree
Run `20261031-172803-8df2` on source `4390c697e2a7255587264514260d9903fea8d719`. Plan: the seed layout plan with `bin-rv460-*` outputs. It made one Antiphon.Tests build, plus one each for session-runner, pty-host and agents-pty. Wall 70m08s, execution 3458 s, admission 750 s. `unlisted: none`. Verdict RED exit=1.

| Row | Executed | Passed | Failed | Note |
|---|---:|---:|---:|---|
| CP-1 (seed CP-1..CP-8 + sibling/tolerance/link + chain CP-6/CP-7 + RcRosterRepair + census C1404/C1577/C1584/C1410/G2865 + neighbours + Antiphon.Tests policy witness) | 125 | 122 | 3 | FailedFirstMapping, InvalidSeedFallsBack, ScheduleConservesAllChunks (D-1) |
| CP-2 chain CP-5 CapFitsAdmittedPlannedCost | 1 | 1 | 0 | |
| CP-3 chain CP-8 LedgerAndReceiptBytesAreUnchanged | 1 | 1 | 0 | |
| CP-4 session-runner ProcessSpawnLimit + NativeLinuxPolicyWitness | 5 | 5 | 0 | |
| CP-5 pty-host NativeLinuxPolicyWitness | 3 | 3 | 0 | |
| CP-6 agents-pty ProcessSpawnLimit + NativeLinuxPolicyWitness | 5 | 5 | 0 | |

The tool kept the four `bin-rv460-*` outputs (`refused: still-present`). I removed them by literal path along with the scratch worktree.

### Policy witnesses on clean rc, without either branch
Run `20261031-172810-588d` on `e0d49d221513b467f8c853f47587e688e04255e2`, a clean detached worktree. `CHECKPOINT SOURCE VALID rows=3`.
- CP-4 session-runner: 5/5.
- CP-5 pty-host: 3/3.
- CP-6 agents-pty: 5/5.

**`Markers_do_not_expand_policy_exclusions` and `Policy_rows_require_pinned_attribute` are GREEN on rc/2026-10-27 and on the combined tree.** The worker's red CP-4/5/6 (run `20261031-144658-1d70` at `9222d590`) belongs to the seed branch's own base (3fb3c113 lineage, before rc's CARD-1619 policy rows), not to rc. Nothing about them blocks top-up 52.

### Other gates
- Land graph `-Check` on the combined tree: Release audit build `bin-land-audit` in a separate detached worktree; lease waited 150 s, held 15 s; check lease held 51 s. Result: `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69 phase=pass2 approved`. No `-Update`.
- `check-evidence-diff.ps1`:
  - rc..combined: commits=71, entries=16, violations=0.
  - 3693d53b6..39488ad5: commits=3, entries=1, violations=0.
  - a00f1b46..9418c99f: commits=7, entries=1, violations=0.
- The d47848cc run and C:\Antiphon were not touched or read.

### Unlisted runs, all in build-slot leases
- Checkpoint tool build (`bin-rv460-tool`).
- Audit tool build and land `-Check`.
- 15 mutant/control harness leases (one per mutant).

The property, layout, link and combination probes were pure pwsh diagnostics on scratch copies, with no dotnet driver.

### PCs
All PCs remain pending for the SourceLanding Mutation.
