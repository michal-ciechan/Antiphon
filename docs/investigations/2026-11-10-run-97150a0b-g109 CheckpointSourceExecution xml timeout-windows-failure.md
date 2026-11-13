# Run 97150a0b group 109: `validators_agree_on_xml_character_references` child timeout (Windows)

Task d2f28df2. This was a read-only desktop read while run 97150a0b was still live on
`rc/2026-11-19` = `ab7af12c699039270af7e22a5d7fd794035bc325`. No test, build or process was
started on the desktop.

## Verdict

**This is a load flake on a test whose budget was never re-derived. It is not a regression.**

- The child is one `pwsh` host that runs the real `scripts/validate-checkpoint-receipt.ps1` over a
  **326-case** corpus. It has a fixed 30 s `CancellationTokenSource`.
- That deadline dates from CARD-0835 (`01f1a2bd9`, 2026-10-01), when the corpus held a handful
  of cases. C1217 (`0da4b71ad`, 2026-11-06) added 297 character-reference cases. The test comment
  says so outright: *"Keep the existing 30-second child deadline; one PowerShell host for the
  corpus."* `72ec6d5e1` and `e83f9873b` (2026-11-07) then added 28 DTD cases.
- The child takes about 50-80 ms per case, so a quiet host already runs close to the deadline.
  The whole test passed in 25.9-34.1 s in the six earlier Full runs. On the saturated desktop
  every sibling test ran 10-46% slower, and the child crossed 30 s.
- **Nothing in the cut touched this path.** `git diff --stat 56e96eabf ab7af12c6` over
  `scripts/lib/CheckpointProcessRunner.cs`, `scripts/validate-checkpoint-receipt.ps1`,
  `scripts/lib/checkpoint-*.ps1`, `tests/Antiphon.Tests/Checkpoints/` and
  `tools/Antiphon.Checkpoints/` is empty for all of them. The only changed files are
  `scripts/lib/Antiphon.Nightly/*`, `nightly-*.ps1`, `rc-*.ps1/json` and `release-impact.ps1`, and
  none of them is loaded by the validator.

## The failure

| Field | Value |
|---|---|
| Chunk | `antiphon-all-group-109` (190 tests: 189 passed, 1 failed; chunk wall 22m21s; build 665.8 s) |
| Test | `Antiphon.Tests.Checkpoints.CheckpointSourceExecutionTests.validators_agree_on_xml_character_references` |
| Duration | 36.761 s (16:17:40.324Z to 16:18:17.086Z, 2026-11-13) |
| Kind | Timing. The test's own 30 s token cancelled the child. **No assertion on returned state ran.** |
| Message | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] OperationCanceledException: A task was canceled.` inner `System.Threading.Tasks.TaskCanceledException: A task was canceled.` |
| Expected / actual | None. The failure is at the `await` of the child (line 469), before `result.Exit.ShouldBe(0, ...)` (line 470). |
| Child stdout/stderr | **Not captured.** The cancel path throws before `Output` is built. The driver writes `results.json` only at the end, so the report cannot say whether pwsh started or how many of the 326 cases finished. |
| Test stdout | `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Checkpoints.CheckpointSourceExecutionTests.1.1.validators_agree_on_xml_character_references.1.1.0` |

Top stack frames (from `antiphon-all-group-109-tests.log`):

```
at Antiphon.Checkpoints.CheckpointProcessRunner.RunCoreAsync[T](...)            scripts\lib\CheckpointProcessRunner.cs:206
at Antiphon.Checkpoints.CheckpointProcessRunner.RunCaptured[T](...)             scripts\lib\CheckpointProcessRunner.cs:104
at Antiphon.Checkpoints.CheckpointProcessRunner.RunObserved(...)                scripts\lib\CheckpointProcessRunner.cs:48 / :42
at Antiphon.Checkpoints.CheckpointProcessRunner.Run(start, timeoutMs, git, ct)  scripts\lib\CheckpointProcessRunner.cs:37
at Antiphon.Tests.Checkpoints.CheckpointFixtureProcess.Run(start, timeoutMs, ct) CheckpointFixtureProcess.cs:32 -> rethrown :39
at Antiphon.Tests.Checkpoints.CheckpointFixtureProcess.RunAsync -> Run(start, Timeout.Infinite, ct)  :45
at CheckpointSourceExecutionTests.ValidateCorpusWithScriptAsync(root, corpus, expectedRepeat)  :469
at CheckpointSourceExecutionTests.validators_agree_on_xml_character_references()               :512
```

Line 206 is `await Task.WhenAll(process.WaitForExitAsync(ct), stdout, stderr).WaitAsync(ct)`.
`RunAsync` passes `Timeout.Infinite`, so the runner's own `TimedOut` path (which would read
"fixture step exceeded N ms") is bypassed. The caller's token is the only deadline, and its
cancellation is rethrown as a bare `OperationCanceledException`.

The diag log (`antiphon-all-group-109-execution\log_261113161011740.diag`) has only the TUnit
node updates around 16:17:40-16:18:17. There are no extra lines.

### Time split

- The failure ran 36.76 s, of which 30.0 s was the token. So about **6.8 s of in-host setup**
  ran before the child started: 326 TRX/receipt writes, plus one
  `RepeatEvidenceValidator.Validate(TrxReport.Parse(control))` per case.
- The stored evidence cannot split a passing run into setup, child and post-loop. The timing
  root keeps only chunk aggregates (`processSpawnSeconds 1156.8`, `processWaitSeconds 413.5`).
  `TestTiming.BeginProcess` records no per-test record.
- Estimate for a passing run: child about 15-22 s out of 26 s total, which is 1.4-2x headroom.
  In 6591e378 the test passed at 34.1 s total, so headroom that time was closer to 1.1-1.3x.

## What the child does

`ValidateCorpusWithScriptAsync` (`CheckpointSourceExecutionTests.cs:448-473`) writes
`validate.ps1` and starts `pwsh -NoProfile -NonInteractive -File validate.ps1 corpus.json
results.json scripts/validate-checkpoint-receipt.ps1 <sha> 2`. The driver then does this:

1. It starts one PowerShell 7 host, which takes 0.3-1 s quiet and 5-10 s under heavy load.
2. It loops over **326 cases**. For each case it calls `& $Validator ... *>&1 | Out-String`. Each
   call re-dot-sources `scripts/lib/checkpoint-source.ps1` (148 lines, which dot-sources
   `checkpoint-process-runner.ps1` and `checkpoint-file-reader.ps1`) and `checkpoint-repeat.ps1`
   (141 lines). It then parses the receipt JSON, runs `Assert-NoDuplicateProperties`, makes an
   `XmlReader` CheckCharacters/Prohibit pass and a `[xml]` cast over the TRX, and runs the repeat
   summary checks.
3. On the first case of a fresh TEMP, `checkpoint-process-runner.ps1` compiles
   `CheckpointProcessRunner.cs` through `Add-Type` into
   `%TEMP%\antiphon-checkpoint-runner\CheckpointProcessRunner-<hash>.dll`, then reuses the cached
   dll. This test does not override TEMP, so the run's TEMP cache normally hits.
4. It runs `ConvertTo-Json` over all records and writes `results.json` once.

The 326 cases are 1 ordinary case, plus 99 references from
`C1217ConditionalSkipFixture.CharacterReferences()` x 3 contexts, plus 28 `DtdCases`. The 99
references are 37 illegal values x decimal/hex, 7 malformed references and 9 legal values x 2.

## History: same chunk position, every earlier Full run (read-only TRX)

| Run (rc candidate) | Chunk | Outcome | Test duration |
|---|---|---|---:|
| 4e0d1159 (rc-2026-11-11) | group-106 | Passed | 28.5 s |
| 231f573c (rc-2026-11-13) | group-108 | Passed | 27.1 s |
| dce78ee0 (rc-2026-11-14) | group-108 | Passed | 31.1 s |
| 6591e378 (rc-2026-11-15) | group-108 | Passed | 34.1 s |
| ae84478d (rc-2026-11-17) | group-109 | Passed | 25.9 s |
| 0b292fa4 (rc-2026-11-18) | group-109 | Passed | 26.2 s |
| **97150a0b (rc-2026-11-19)** | group-109 | **Failed** | **36.8 s** |

There is no occurrence before rc-2026-11-11, because the test was introduced on 2026-11-06. The
passing durations range from 25.9 to 34.1 s, a 1.3x spread, against a deadline of 30 s plus
about 5-7 s of setup. **Its margin was always below the 2x-p95 budget rule in
`docs/test-performance-standards.md`.**

## Siblings in the same class (all passed; they bound the cause)

| Test | 97150a0b | 0b292fa4 | ae84478d | Ratio vs 0b292fa4 |
|---|---:|---:|---:|---:|
| validators_refuse_malformed_receipt_encoding (same helper, 7 cases) | 3.4 | 3.0 | 3.0 | 1.13 |
| validators_refuse_changed_count_tokens (same helper, ~10 cases) | 4.2 | 3.5 | 3.5 | 1.19 |
| validation_requires_complete_consistent_source | 33.6 | 30.0 | 30.7 | 1.12 |
| changed_admission_and_driver_boundaries_refuse | 27.6 | 24.6 | 24.6 | 1.12 |
| host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green | 20.4 | 15.9 | 14.7 | 1.28 |
| red_run_kept_outputs_are_not_recorded_as_cleanup_refusals | 19.4 | 13.3 | 14.4 | 1.46 |
| clean_and_dirty_runs_publish_bound_source | 18.2 | 16.6 | 16.8 | 1.10 |
| red_run_still_refuses_output_it_did_not_create | 16.6 | 15.1 | 15.7 | 1.10 |
| terminal_paths_do_not_invent_clean_evidence | 13.2 | 12.7 | 12.7 | 1.04 |
| command_only_selection_beside_built_row_receipt_passes_both_validators | 12.2 | 9.2 | 9.6 | 1.33 |
| cleanup_refusals_disqualify_green_rows_in_both_validators | 10.8 | 9.2 | 9.2 | 1.17 |
| strict_cli_and_reuse_require_clean_binding | 10.7 | 10.6 | 10.6 | 1.01 |
| C1734_Failure_receipt_preserves_observed_source_history(changed / unknown) | 4.8 / 5.6 | 3.6 / 4.2 | 4.1 / 4.3 | 1.33 |
| merge_cannot_launder_source_identity | 0.0 | 0.0 | 0.0 | - |
| **validators_agree_on_xml_character_references** | **36.8 F** | 26.2 | 25.9 | **>=1.40** (capped by the deadline) |

The two sibling tests that use the same `ValidateCorpusWithScriptAsync` helper and validator, on
7-10 cases, passed. So the pwsh driver, validator script and runner custody all work. The only
difference is the 30x larger corpus under the same 30 s. Every sibling was slower in this run,
which points to a uniform host slowdown rather than a code path that changed.

## What ran at the same time (chunk-internal, from TRX start/end)

These are 16:17:40-16:18:17Z overlaps. `ProcessSpawnLimit` is 1 per its own key, while
`CheckpointToolSpawnLimit` and the ownership tests run beside it.

- `CheckpointRecoveryWindowsTests.detached_staged_copy_survives_starter_exit`: 121.4 s, against
  91.8 s in 0b292fa4, x1.32.
- `CheckpointSelectedOutputTests.selected_run_excludes_earlier_commit_unused_outputs(False)`:
  89.0 s, against 65.0 s, x1.37.
- `selected_run_excludes_earlier_commit_unused_outputs(True)`: 77.4 s, against 63.5 s, x1.22.
- `selected_retained_output_stamp_mismatch_is_still_refused`: 55.4 s, against 41.0 s, x1.35.
  These are dotnet builds.
- About 18 `CheckpointTaskOwnershipTests` (0-23 s each).

**0b292fa4 co-scheduled the same tests in the same window**, so the chunk's make-up did not
change. Its neighbours were also 22-37% slower, which matches the host-wide load the caller
reported at that hour: 91-99% CPU, 42% privileged, 5.7 GB free, 41.9 GB committed, csc 1.3-1.6
cores, vmmem 1.3, Postgres 1.3-3.4, Defender 0.4.

## Why it was missed before rc (standing rule 2026-11-08)

1. **The deadline was explicitly kept when the work grew 30x.** C1217 added 297 cases and wrote
   "Keep the existing 30-second child deadline" without re-measuring. No Plan or Review row
   checked the 2x-p95 rule for that helper.
2. **The pre-rc evidence came from quiet or Linux hosts.** server2 Linux, and the desktop when
   it was not saturated, finish the child well inside 30 s. The rc Full run is the first place it
   meets about 99% host CPU.
3. **The failure is unattributable by construction.** Cancellation discards child output and
   the driver writes results only at the end. Six passing runs at 26-34 s therefore gave no
   warning that the child itself was at about 70-90% of its deadline.

## Proposed fix (smallest; for the Debug stage)

**Do not raise the 30 s.** Instead, make the deadline track progress and make the failure name
its phase. Everything below stays in `CheckpointSourceExecutionTests.ValidateCorpusWithScriptAsync`.
No production code changes.

1. **Driver emits progress.** Write `READY <ms since start>` to a `progress.txt` file
   (append + flush) before the loop, then one line per case: `<index> <key> <exit>`. Keep the
   single `results.json` write.
2. **Progress-based deadline, not a corpus deadline.** Replace
   `new CancellationTokenSource(TimeSpan.FromSeconds(30))` with a watchdog that polls
   `progress.txt` about every 250 ms. It fails when:
   - no `READY` line arrives within `startupBound` (30 s, the existing number, now spent only
     on pwsh start), or
   - no new case line arrives within `stallBound` (30 s per case, about 400x a case's quiet
     cost, so a hung validator still fails in 30 s), or
   - an absolute ceiling is passed: `READY + k x firstCaseMs x caseCount`, with k = 4 and the
     first-case cost measured in the same child, so the calibration is the run's own load.

   Cancel the token only on those conditions.
3. **Phase-naming failure message.** On cancel, read `progress.txt` and throw
   `pwsh validator corpus timed out: phase=<startup|case|ceiling> ready=<ms|never>
   done=<n>/<326> last=<key> elapsed=<ms>`. This replaces the bare `A task was canceled.`

**Windows red witness** (commit it red first). Add an optional `string? preamble` parameter to
`ValidateCorpusWithScriptAsync`, injected into the driver. A new test,
`corpus_driver_deadline_tracks_progress_not_corpus_size`, uses the 7-case encoding corpus
because it is cheap:

- **(a) Slow but progressing.** The preamble is `$global:Delay = 6000` and each loop iteration
  runs `Start-Sleep -Milliseconds $Delay`, so 7 x 6 s = 42 s > 30 s. On current code this is
  red with `OperationCanceledException` at 30 s. After the fix it is green.
- **(b) Delayed start.** The preamble is `Start-Sleep -Seconds 12`, which simulates a slow pwsh
  start under load. It is green after the fix, and the message or record shows `ready>=12000`.
- **(c) Hang.** The preamble sleeps 60 s after case 3. It must stay red after the fix, with
  `phase=case done=3/7 last=<key>` within about 30 s. This shows the deadline did not just get
  bigger.

Verification: `--treenode-filter "/*/*/CheckpointSourceExecutionTests/corpus_driver_deadline_tracks_progress_not_corpus_size"`
then `/validators_agree_on_xml_character_references`, on Windows.

Alternatives, in order, if the Debug stage prefers them:

- **Cheaper child.** Dot-source the two libs once in the driver and call the validator body as
  a function. This is fewer per-case parses, but it stops exercising the script's real entry
  point, which is what the parity test is for, so it is not recommended.
- **Shard by context.** Run text, attribute and reason as three children of about 110 cases each,
  each with 30 s. This is simple, but it is effectively a 3x blind budget.

The same "token discards output" gap affects every `CheckpointFixtureProcess.RunAsync` caller.
A follow-up card could make `RunAsync` return or attach partial stdout/stderr on cancellation,
as the `TimedOut` branch already names the step.

## Common cause with the other two load failures of this run

The caller lists three failures: g76 channel recovery (8 s wait), profile-16 sampler (25 ms),
and this 30 s child. **Only this one was read here.** The other two are taken from the brief and
were not re-verified.

All three are fixed wall-clock budgets on a host at 91-99% CPU with 42% privileged time. Each
budget was set or kept from quiet-host measurements and sits below the 2x-p95 rule. The common
cause is **host CPU saturation during the qualification**. The test code is the place to fix
this: progress-based and self-calibrated budgets.

What could run less on the desktop during a qualification run, from the CPU attribution report
`docs/investigations/2026-11-13-card-1886-antiphon-cpu-attribution.md` (master):

1. **Postgres JIT** (CARD-1886). The runtime-action claim query is about 1 s per call, about
   1-3 times a second, almost all of it JIT. JIT is 55% of DB time, about 0.4-0.56 core. Set
   `jit=off` on the production database, or raise `jit_above_cost`. The production Postgres
   reached 1.3-3.4 cores in `docker stats`.
2. **Delegate builds on the desktop** (csc 1.3-1.6 cores). Route Code, Debug and Review builds
   to server2 while a Windows Full run is live, and defer desktop-only work until it ends.
3. **Antiphon.Server** (about 0.95 core). Cut the `GET /api/agent-tasks/{id}` polling (about 40
   per minute; one task was polled 5,616 times) and the per-request INF logging (45% of 37k
   lines per hour). The session-state seed/pins queries take 1-2.2 s each (CARD-1887).
4. **Defender** (0.4 core). Confirm exclusions for `C:\Antiphon\releases\checkout-*`, the
   candidate log roots and the test TEMP roots.
5. Idle Claude sessions and pty hosts are small (about 0.25 core), and the runner is negligible
   (0.05 core).

Items 1-3 are about 2.5-3.5 cores of Antiphon-owned load. On an 8-core host, that is the
difference between saturated and having headroom.
