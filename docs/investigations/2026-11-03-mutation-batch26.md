# Mutation sweep: merge batch 26 (CARD-1674 alarm no-spin guard, CARD-1676 round guard, CARD-1677 S3 disk alarm)

Task bb924129 (Mutation, waived). This was an ordinary-worktree sweep on master
`bb0947982a3c170c14ee93d72b4a4f635436bb40` (L), the batch 26 range `bcfe77b3d..bb0947982` (merge report: task 6f3ab7aa).
It ran on server2 (Linux) on 2026-11-03 from 09:17 to 10:12 UTC, inside the 90-minute budget.

**These results are NOT CARD-0478 evidence.** Merge-role lands have no land operation id, so there is no
SourceLanding. This sweep was waived to an ordinary worktree. No product or test code was changed, and nothing was landed.

- **Subject.** It covers three areas:
  - CARD-1674 (`2fefc6a6b`, `41ae58b9c`): `RunnerAlarmHostedService.NextDue` ignores a drain expiry at or before the last pass.
  - CARD-1676 (`858947795`, `074e8497d`): the `ReviewRoundGuard` round-line regex and its round/count bounds.
  - CARD-1677 S3 (`c5eca8748`, `bb0947982`): `BuildWorkspaceDiskLowItems`, `IsWorkspaceDiskLow`, `WorkspaceRetentionMode`, `WorkspaceDiskAlarmSettingsValidator` and the settings defaults.
- **Skipped.** CARD-1673 and CARD-1678 are test-only, so they were not mutated. One CARD-1671 coordinator line (A7) was added because the CARD-1674 loop witness asserts on it.
- **No hysteresis.** The disk alarm has no hysteresis or rearm state. It is computed on every read from `HostStatsCache.Latest`, so the "rearm" surface is the clear-on-recovery behaviour, which C2 and V-66 witness.
- **No code for round 3.** The 'round 3 blockers only' rule exists only as owner text. No code reads it, so the only code bounds are `MaxRounds` and the declared/count compares (B7-B10).

## Outcome

**53 mutants were run: 37 were killed and 16 survived.**
- Of the 16 survivors, 3 are equivalent or masked by an invariant (C6, A3, A4).
- The other 13 are coverage gaps. Each is listed with the assertion that would kill it.
- None of the survivors is in a security path.

- **Baselines.** The gated build was made with every mutant compiled in and `B26_MUTANT` unset. Each lane ran green on that build:
  - s00a (alarm lane) 26/26
  - s00g (round-guard lane) 48/48
  - s00d (disk lane) 41/41
- **Combined baseline red under load.** One combined run of all 115 tests at load ~25 was red 26/115. All 24 `C1677DiskAlarmTests` hit their class `[Timeout(20_000)]`, and C1671/C1674 hit the 5 s FirstTimer wait. These are load reds, not product reds. So the three lanes ran separately from then on.
- **Restored green.** After a rebuild at L with every file restored:
  - s99g 48/48
  - s99d 41/41
  - s99a 25/26 on the first run and 26/26 on the rerun. The first-run red was `the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal` at line 54, the inherited FirstTimer wait. See "Load flakes" below.

## Method

- **The gate.** Every mutant was compiled in behind `B26Mut.On(id)`, which reads `B26_MUTANT` (comma-separated). The helper was the untracked file `server/B26Mutant.cs`, deleted afterwards. With the variable unset, the gated code is L.
- **Regex mutants.** These were extra `[GeneratedRegex]` methods, selected through a `MutRound()` switch.
- **Builds.** There were two builds of `tests/Antiphon.Tests`, each under its own build-slot lease, with native output, `-p:UseAppHost=false` and `TEMP=/tmp`:
  - the gated build, held 254 s
  - the restore build at L, held 138 s
- **Runs.** Every run was `dotnet run --no-build --project tests/Antiphon.Tests -p:UseAppHost=false -- --treenode-filter <F> --report-trx --timeout 30m` under its own lease. Runs went one at a time, never concurrently.
- **Lane filters.** Each class name has a trailing `*`, and no other class shares these prefixes (checked by grep):
  - alarm: `/*/*/(RunnerAlarmHostedServiceTests*)|(RunnerAlarmCoordinatorTests*)|(RunnerAlarmWiringTests*)/*` (26 tests)
  - round guard: `/*/*/(ReviewRoundsPolicyTests*)|(AgentTaskCardBindingTests*)/*` (48 tests)
  - disk: `/*/*/(C1677DiskAlarmTests*)|(AttentionKindWireTests*)/*` (41 tests)
- **Round grouping.** Each round set at most one mutant per area. A mutant only runs its own area's lane.
  - In the disk lane, a builder mutant (C) was paired only with a validator mutant (V), or with another C whose witnesses are disjoint. The validator tests and the builder tests are different methods.
  - Pairings were checked against each round's red list.
- **Restore.** Files were restored with `git checkout -- <exact path>`, and the helper was deleted by literal path. The worktree is clean at L.

| Round | B26_MUTANT (alarm / guard / disk) | Alarm failed /26 | Guard failed /48 | Disk failed /41 |
|---|---|---|---|---|
| s00 | none (gated build) | 0 | 0 | 0 |
| r01 | A1 / B1 / C1,V1 | 1 | 1 | 3 |
| r02 | A2 / B2 / C2,V2 | 2 (1 is the FirstTimer flake) | 5 | 16 |
| r03 | A3 / B3 / C3,V3 | 0 | 0 | 14 |
| r04 | A4 / B4 / C4,V4 | 1 (FirstTimer flake) | 0 | 1 |
| r05 | A5 / B5 / C5,V5 | 6 (1 is the FirstTimer flake) | 9 | 2 |
| r06 | A7 / B6 / C6,V6 | 1 | 0 | 0 |
| r07 | A9 / B7 / C7,V7 | 1 (FirstTimer flake) | 4 | 1 |
| r08 | - / B8 / C8,V8 | - | 17 | 1 |
| r09 | - / B9 / C9 | - | 14 | 1 |
| r10 | - / B10 / C10 | - | 8 | 0 |
| r11 | - / B11 / C11 | - | 0 | 3 (2 are 20 s class timeouts) |
| r12 | - / B12 / C12 | - | 9 | 1 |
| r13 | - / B13 / C13 | - | 1 | 0 |
| r14 | - / B14 / C14 | - | 0 | 1 |
| r15 | - / B16 / C15 | - | 6 | 3 |
| r16 | - / B17 / C16 | - | 6 | 3 |
| r17 | - / - / C17,C18 | - | - | 4 |
| r18 | - / - / C19,C20 | - | - | 1 |
| r19 | - / - / C21,C22 | - | - | 3 |
| x-A4 | A4 alone | 1 (FirstTimer flake) | - | - |
| x-A9 / x-A9-1,-2 / x-A9-c3 | A9 alone (class), A9 on the two FirstTimer methods twice, then A9 class again | 2 (both FirstTimer) / 2, 2 (FirstTimer) / 0 | - | - |
| x-none | gate off, `the_loop_raises*` + `a_failing_pass*` | 2/2 (both at the FirstTimer wait: inherited) | - | - |
| s99 | none, rebuilt at L | 1, then 0 on rerun | 0 | 0 |

### Load flakes (not kills)

- **FirstTimer waits.** The flaking lines are `RunnerAlarmHostedServiceTests` `clock.FirstTimer.Task.WaitAsync(5 s)` at lines 54, 135 and 207.
  - They go red in a cold process because the first pass takes about 6 s on loaded server2.
  - They are red with the gate off (x-none 2/2) and at the restored L (s99a). That makes them inherited, as the earlier CARD-1674 and Review c3ef4038 runs also found.
  - No mutant can change the first pass. A red at those lines was never counted as a kill.
  - A4 and A9 were judged on runs where every other alarm test was green. A9 was also judged on a fully green class run (x-A9-c3, 26/26).
- **r11 disk-lane timeouts.** Two r11 disk tests (`Repeated_samples_keep_one_row_per_runner`, `Unlisted_runner_raises_nothing`) hit the class 20 s timeout. They are not counted. C11 is killed by `Stale_sample_raises_nothing` at its assertion.

## Results

Files are given at L:
- `HS` = `server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs`
- `RC` = `server/Application/Services/RunnerAlarmCoordinator.cs`
- `RG` = `server/Application/Services/ReviewRoundGuard.cs`
- `AA` = `server/Application/Services/AttentionService.Alarms.cs`
- `VA` = `server/Application/Settings/WorkspaceDiskAlarmSettingsValidator.cs`
- `WS` = `server/Application/Settings/WorkspaceDiskAlarmSettings.cs`

Test classes:
- `HST` = `RunnerAlarmHostedServiceTests`
- `RRP` = `ReviewRoundsPolicyTests`
- `ACB` = `AgentTaskCardBindingTests`
- `DA` = `C1677DiskAlarmTests`

### CARD-1674 alarm loop no-spin guard

| ID | file:line | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| A1 | HS:80 | `overrunAt > lastPass` guard dropped (the pre-fix code) | killed | HST `C1674_a_drain_expiry_not_after_the_last_pass_never_schedules_a_wake` (`due - lastPass` should be 15 min) |
| A2 | HS:80 | `overrunAt > lastPass` -> `>=` | killed | the same test (the `at-the-pass` expiry, `lastPass - 1 s`, is admitted) |
| A3 | HS:80 | compares `expires > lastPass` instead of `overrunAt` | **survived (equivalent at the loop)** | none; see survivors |
| A4 | HS:57 | call site passes `DateTimeOffset.MinValue` as `lastPass` | **survived (masked by the coordinator invariant)** | none; see survivors |
| A5 | HS:79 | `DrainWakeSlack` 1 s -> 0 | killed | HST `C1678_NextDue_is_one_second_past_the_earliest_drain_bound`, `C1674_a_drain_expiry_not_after_…` (`future` due should be `+5 min +1 s`), `C1678_one_pass_arms_one_timer…`, `C1678_the_loop_wakes_one_second_past…`, `C1671_…` |
| A7 | RC:77 | coordinator keeps publishing a drain expiry after `DrainOverran` (`overran is null` dropped) | killed | HST `C1674_a_reachable_drain_past_its_bound_publishes_no_expiry_and_the_loop_does_not_wake_again` (`state.Current.DrainExpiresAt` should be empty) |
| A9 | HS:68 | raised episodes no longer skipped, so the past `DownSince + grace` is re-scheduled | **survived** | none; see survivors |

### CARD-1676 ReviewRoundGuard

| ID | file:line | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| B1 | RG:27 | widened: the anchor `(?:^[ \t]*\|\breview[ \t]+)` removed | killed | RRP `C1676_a_colon_less_round_keeps_the_ascii_and_same_line_rules("Review 5 files around 6 call sites")` (`declared` should be null) |
| B2 | RG:27 | `[ \t]*` -> `\s*` around the colon (crosses a newline) | killed | RRP `C1647_a_round_label_never_reads_the_next_lines_number`, `C1676_…same_line_rules(Round\n7 of 3 / REVIEW ROUND\n7 OF 3 / Review round\r\n7)` (should be null); ACB `C1676_a_colon_less_fourth_round_is_flagged…` (`malformed.Warning` should not contain the code) |
| B3 | RG:27 | `[0-9]` -> `\d` | **survived** | none; see survivors |
| B4 | RG:27 | trailing `\b` removed | **survived** | none; see survivors |
| B5 | RG:27 | colon required again (the pre-fix shape) | killed | RRP `C1676_each_real_brief_round_shape_declares_its_round` (6 rows), `C1676_a_colon_less_fourth_round_warns` (2 rows); ACB `C1676_…flagged_at_creation…` |
| B6 | RG:27 | `\breview` -> `review` (no word boundary) | **survived** | none; see survivors |
| B7 | RG:45 | `declared > MaxRounds` -> `>=` | killed | RRP `C1647_a_declared_fourth_round_is_flagged_and_round_three_is_not` and `C1676_each_real_brief_round_shape…` round-3 rows (should be null) |
| B8 | RG:19 (in use) | `MaxRounds` 3 -> 4 (declared and count) | killed | RRP `C1676_a_colon_less_fourth_round_warns(ROUND 4 OF 3…)`, `C1647_three_completed_reviews…`, `C1676_…same_line_rules` (17 rows; `counted` should not be null) |
| B9 | RG:46 | `Count < MaxRounds` -> `<=` | killed | RRP `C1676_…same_line_rules` (8), `C1647_a_non_ascii_round_digit…` (2), `C1647_three_completed…` (`counted` should not be null) |
| B10 | RG:44-45 | a declared round 3 or below falls through to the count rule | killed | RRP `C1676_each_real_brief_round_shape_declares_its_round` (should be null "whatever the count"), `C1647_a_declared_fourth_round…` |
| B11 | RG:36 | last match instead of first | **survived** | none; see survivors |
| B12 | RG:27 | `IgnoreCase` dropped | killed | RRP `C1676_a_colon_less_fourth_round_warns(ROUND 4 OF 3)`, `C1676_each_real_brief…(Round 2 of 3)`, `C1647_a_round_label…` (`Round:\t4` should be 4) |
| B13 | RG:27 | `Multiline` dropped | killed | RRP `C1676_each_real_brief_round_shape_declares_its_round("CLEANUP RULE…\r\n  round 2 of 3…", 2)` |
| B14 | RG:27 | `{1,4}` -> `{1}` digits | **survived** | none; see survivors |
| B16 | RG:27 | the line-start alternative removed (only `REVIEW ROUND` reads) | killed | RRP `C1676_each_real_brief…(Round 2 of 3 / round: 3 / CRLF row)`, `C1647_a_declared_fourth_round…` |
| B17 | RG:27 | the `REVIEW ` alternative removed (line start only) | killed | RRP `C1676_each_real_brief…(REVIEW ROUND 2 OF 3 / Review round 2 / CODE REVIEW ROUND 3 / CLEANUP…\nREVIEW ROUND 3)` |

### CARD-1677 S3 WorkspaceDiskLow

| ID | file:line | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| C1 | AA:123 | `>=` -> `>` at the 85 % boundary | killed | DA `Work_at_85_percent_raises_one_warning` (should have single item), `Evidence_names_bytes_and_retention_mode` |
| C2 | AA:123 | comparison inverted (`<`) | killed | DA 15 tests, e.g. `Work_below_85_percent_raises_nothing` (should be empty), `Row_clears_below_the_threshold`, `Work_at_85…` |
| C3 | AA:123 | free-space percent compared instead of used | killed | DA 13 tests, e.g. `Work_at_85…` (should have single item), `Stale_sample…`, `Row_clears…` |
| C4 | AA:122 | `totalBytes > 0` dropped | killed | DA `Missing_or_unusable_reading_raises_nothing(zero-total)` (DivideByZeroException out of the feed read) |
| C5 | AA:122 | `freeBytes >= 0` dropped | killed | DA `Missing_or_unusable…(negative-free)` (should be empty) |
| C6 | AA:122 | `freeBytes <= totalBytes` dropped | **survived (equivalent)** | none; see survivors |
| C7 | AA:123 | `Int128` -> unchecked `long` arithmetic | **survived** | none; see survivors |
| C8 | AA:93 | freshness `>` -> `>=` (15 min boundary) | **survived** | none; see survivors |
| C9 | AA:93 | freshness check dropped | killed | DA `Stale_sample_raises_nothing` ("stale-16-min-no-row" should be empty) |
| C10 | AA:93 | freshness measured from the runner's sample `At` instead of the server's `ObservedAt` | **survived** | none; see survivors |
| C11 | AA:93 | `FromMinutes` -> `FromSeconds` | killed | DA `Stale_sample_raises_nothing` ("fresh-control-14-min-raises" should have single item) |
| C12 | AA:88 | builder desktop exclusion dropped | killed | DA `Unlisted_runner_raises_nothing` ("desktop-listed-still-no-row" should be empty) |
| C13 | AA:95 | volume matched by `StartsWith` instead of exact `Ordinal` equality | **survived** | none; see survivors |
| C14 | AA:80 | `Enabled` ignored | killed | DA `Disabled_alarm_raises_nothing` (should be empty) |
| C15 | AA:112 | `ConditionKey` without the runner id | killed | DA `Work_at_85…` ("boundary-85-key"), `Unlisted_runner…`, `Repeated_samples…` |
| C16 | AA:99 | displayed `usedPercent` from free bytes | killed | DA `Evidence_names…` (`usedPercent=85`), `Only_the_configured_volume_counts` (`usedPercent=90`), `Repeated_samples…` ("latest-sample-wins") |
| C17 | AA:133 | `Execute` sense inverted | killed | DA `Retention_mode_is_read_from_configuration(true,false,DryRun / true,null,DryRun / True,true,Execute)` (should end with) |
| C18 | AA:103 | Severity Warning -> Critical | killed | DA `Work_at_85…` ("boundary-85-warning") |
| C19 | AA:111 | `SinceUtc` = read time instead of `ObservedAt` | **survived** | none; see survivors |
| C20 | AA:112 | stop after the first raised runner | killed | DA `Repeated_samples_keep_one_row_per_runner` (`rows.Count` should be 2) |
| C21 | WS:19 | default `UsedPercent` 85 -> 80 | killed | DA `Work_below_85_percent_raises_nothing` ("below-85-no-row"), `Evidence_names…` (`threshold=85`) |
| C22 | WS:22 | default `SampleFreshMinutes` 15 -> 20 | killed | DA `Stale_sample_raises_nothing` ("stale-16-min-no-row") |
| V1 | VA:11 | `UsedPercent < 50` -> `<= 50` | killed | DA `Used_percent_bounds(50, True)` |
| V2 | VA:11 | `UsedPercent > 99` -> `>= 99` | killed | DA `Used_percent_bounds(99, True)` |
| V3 | VA:13 | `SampleFreshMinutes < 1` -> `< 0` | killed | DA `Settings_validator_refuses_unsafe_values(fresh-0, False)` |
| V4 | VA:13 | `SampleFreshMinutes > 1440` -> `>= 1440` | **survived** | none; see survivors |
| V5 | VA:20 | desktop refusal dropped | killed | DA `Settings_validator…(desktop-listed, False)` |
| V6 | VA:20 | desktop compare `OrdinalIgnoreCase` -> `Ordinal` | **survived** | none; see survivors |
| V7 | VA:22 | absolute-path check dropped | killed | DA `Settings_validator…(relative-path, False)` |
| V8 | VA:17 | blank runner id check dropped | killed | DA `Settings_validator…(blank-runner, False)` |

## Survivors

### Equivalent or invariant-masked (no witness needed)

- **C6 (equivalent).** If `freeBytes > totalBytes`, then `100 * (total - free)` is negative. `usedPercent * total` is non-negative for any `UsedPercent >= 0`, and the validator enforces 50..99. So the compare is already false without the `free <= total` clause.
- **A3 (equivalent at the loop).** The mutant differs only for an expiry in `(lastPass - 1 s, lastPass]`, and in that window both versions avoid a spin. The original makes one wake at `expires + 1 s`; the mutant makes none.
  - The coordinator publishes an expiry only while `DrainOverran(now)` is null, which means `expires >= now = lastPass`. So the loop never sees that window.
  - A `NextDue` row with `expires = lastPass - 500 ms` expecting `due = lastPass + 500 ms` would kill it, but no requirement says which choice is right.
- **A4 (masked by the coordinator invariant).** The guard is defence in depth. With the real coordinator, every published expiry has `overrunAt = expires + 1 s > now`, so the call-site `lastPass` is never decisive.
  - The static `NextDue` witness kills A1 and A2 but cannot see the call-site argument.
  - Killing witness: a loop test with a fake `IRunnerAlarmExclusion` whose `DrainOverran` stays null while `DrainExpiresAt` is in the past. Assert that `source.Calls` does not grow over 200 ms without the clock moving, as the C1674 loop witness does.

### Coverage gaps (findings)

| ID | Missing assertion that would kill it |
|---|---|
| **A9** (HS:68) | After a raise, the loop must not wake again until the sweep. Either: (a) in a `NextDue` row, a snapshot with a raised episode whose `DownSince + grace` is past gives `due == nextSweep`; or (b) after `the_loop_raises…` raises, `source.Calls` stays constant over 200 ms without the clock moving. This is the episode-side twin of the CARD-1674 stale-drain spin, and nothing pins it today. |
| **C7** (AA:123) | `huge-total` uses only a non-raising shape: `free = MaxValue/2` gives `0 >= 2^63-85` after wrap, so the answer is false either way. Add a raising huge reading, `("/work", long.MaxValue / 10, long.MaxValue)`: one row with `usedPercent=90`. Unchecked `long` gives no row. |
| **C10** (AA:93) | Freshness must come from the server's first-observed time, not the runner's clock. Record a sample whose `At` is 30 min behind the server clock but newly observed now: one row is expected. With a skewed runner clock the mutant says stale. |
| **C8** (AA:93) | The 15-minute boundary is untested; the tests use 14 and 16 min only. Add a sample observed exactly `SampleFreshMinutes` ago and pin inclusive or exclusive. The code is inclusive (`>`), so expect one row. |
| **C19** (AA:111) | `SinceUtc` should be `ObservedAt`. Record, advance the clock 5 min, read, and assert `SinceUtc == ObservedAt`, not the read time. V-60 records and reads at the same instant. |
| **C13** (AA:95) | Exact volume match: list `("/workspace", 1 GiB, 100 GiB)` before `("/work", 50 GiB, 100 GiB)` and expect no row. |
| **V4** (VA:13) | Add the `fresh-1440` (true) upper-boundary row. Only 1441 (false) is tested. |
| **V6** (VA:20) | Add a validator row `Runners["Desktop"] = "/work"` that is refused, since desktop matching ignores case. The builder side (AA:88) has the same case-only gap. |
| **B14** (RG:27) | A multi-digit round, `"REVIEW ROUND 10 OF 3"`, must warn with `declares round 10`. |
| **B4** (RG:27) | `"round 12345"` and `"Round 3rd pass"` must declare nothing (null), which pins the trailing `\b`. |
| **B6** (RG:27) | `"PREVIEW ROUND 4 OF 3"` must declare nothing, which pins `\breview`. |
| **B3** (RG:27) | `"REVIEW ROUND ４ OF 3\nround: 4"` must declare 4. With `\d`, the fullwidth match shadows the valid later line and returns null. |
| **B11** (RG:36) | `"round: 2\n…\nround: 5"` must declare 2, which pins the first-match rule the doc comment states. |

**Severity.** A9 is the most significant gap: it is the same spin class CARD-1674 fixed, on the episode path. C7 and C10 are correctness gaps in the disk alarm's stated S-7 and freshness invariants. The rest are boundary or shape pins.

## Evidence

- The logs and TRX files are in the session scratchpad `…/scratchpad/ev/` (`build-gated.log`, `build-restore.log`, and `<round>/console.log` plus a TRX per run). They are not committed.
- The final state is the worktree at `bb0947982a3c170c14ee93d72b4a4f635436bb40`, with clean tracked source and index.
- This report is the only committed file.
