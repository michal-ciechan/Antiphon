# CARD-0449 Code verification report

Implemented the effort-default startup fix in both Claude adapters. Mandatory scripted verification passes: **180/180 focused regression tests**, **13/13 positive controls** with **25 expected assertion failures across 38 red test rows and 38/38 restored-green rows**. Real-picker acceptance is **unverified**, under the plan's allowed API-mode/no-dialog condition.

Final implementation/test commit: `15652113179c4fa84e9668d9cc7ba5a008ee6e66`. Controls used `4f9a2204` (PC-1..5, PC-7..13) and `15652113` (PC-6 assertion ordering refinement). Production code is identical across those control tips and the final regression tip. This report is a subsequent documentation-only commit.

Authority: [implementation and TestDesign plan](2026-09-08-card-0449-claude-effort-dialog-readiness-plan.md), decisions D-1..D-6. No deployment/restart was requested or performed.

## Changed behavior and files

- `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs`: coherent row parser, shared argv intent reader, matching option selection, fresh per-key checks, bounded navigation/Enter, positive two-frame dismissal, visible current-effort contradiction check.
- `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs`: distinct EffortChoice before legacy marker gate; generic-answer refusal; narrow trust-to-effort clearance transition.
- `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs` and `ComposerInputProbe.cs`: shared gate, delayed-modal interruptions, fresh post-modal round trip, one deadline and cumulative write cap; legacy callers retain optional-hook compatibility.
- Both Claude adapters retain resolved launch intent and map known effort failure to false/EffortDialogNotCleared. `AgentRegistrySettings.cs` adds the default 15,000ms settle budget.
- `AgentLaunchBlock.cs`, `SessionLaunchBlock.cs`, `GrokSignInIncident.cs`: appended value 3 and existing-path persistence mapping.
- Helper, runner, local-child, launch-failure and isolated-canary tests; linked hash-verified capture resources in both test projects. No fixture calls the production parser/selector to decide the applied effort.
- `ClaudeLaunchArgs.cs` comment correction only; `docs/agent-kinds.md` and `docs/session-runtime-invariants.md` document the narrow exception and bounds.

## Verification items

Abbreviations E/S/P/A/F/L/C are the exact classes defined by the TestDesign plan. Counts overlap where one assertion covers several requirements. All mandatory items below have executable results; V-21 alone is conditional.

| Item | Outcome | Evidence |
|---|---|---|
| V-1 | Pass | E.Captured_screens_are_effort_choices: 3/3; original UTF-8 hashes checked, full capture IDs/hashes emitted to TRX output, literal model/efforts/highlight checked. |
| V-2 | Pass | E.Synthetic_layout_variants_preserve_option_structure: 6/6 (marker, CRLF, borders, case/space, wrapped question, wrapped Switch). |
| V-3 | Pass | E.Launch_effort_reader_preserves_explicit_intent: 7/7; Requested_effort_selects_the_matching_option: 8/8, including all five supported efforts on Nimble 9, Switch, absent, equal values. |
| V-4 | Pass | E.Ambiguous_or_unmatched_intent_types_nothing: 5/5; A.Attach_without_launch_effort_preserves_current: 1/1, including logged absent/Keep provenance. |
| V-5 | Pass | E.Requested_effort_is_applied: 3/3; An_unmovable_wrong_highlight_withholds_Enter: 3/3; Navigation_candidates_are_observed_before_confirmation: 2/2. Independent accepted option/applied effort oracle. |
| V-6 | Pass | E.A_changed_dialog_identity_stops_further_input: 2/2; Generic_answering_cannot_confirm_an_effort_choice: 1/1. Actual UI changes after returning the selected frame, before mandatory pre-key read. |
| V-7 | Pass | S.Redraw_is_not_clearance_before_the_probe and E.Malformed_effort_remnants_do_not_count_as_clearance: 2/2. Blank/one-clear/returning-dialog frames reset clearance; accumulated raw capture remains present. |
| V-8 | Pass | E.A_swallowed_Enter_is_retried_only_while_the_same_target_is_selected, Retries_respect_settle_and_attempt_limits, Retry_revalidates_the_target_and_withholds_later_Enter: 4/4. Three attempts maximum, initial/gap >=1500 ms, independent watchdog; changed highlight and identity refuse retry. |
| V-9 | Pass | S.A_contradictory_resulting_effort_fails_readiness and A_scrolled_away_effort_banner_does_not_block_confirmed_clearance: 2/2. |
| V-10 | Pass | A.Each_captured_dialog_clears_before_the_composer_probe: 3/3. Keep xhigh accepted, two clear observations before token, one round trip, empty composer and null block. |
| V-11 | Pass | A.A_dialog_appearing_during_the_minimum_floor_is_resolved: 1/1; P.A_modal_interrupts_before_each_probe_write_or_verdict: 6/6. All six planned checkpoints execute, with literal allowed-input arrays. |
| V-12 | Pass | S.A_late_dialog_requires_a_fresh_post_clearance_round_trip: 2/2, after echo and before echo. Two total token writes and a fresh post-dialog render/clear required. |
| V-13 | Pass | S.Startup_dialog_chains_preserve_each_gate: 4/4 (trust-effort, effort-trust, effort-permission, effort-choice). Unrelated modals receive no keys/probe. |
| V-14 | Pass | A.An_effort_dialog_that_never_clears_fails_within_its_budget: 1/1, false plus EffortDialogNotCleared and requested/current/suggested/selected/Enter fields; no probe; independent 9s watchdog. |
| V-15 | Pass | S.Recurring_dialogs_cannot_renew_the_readiness_deadline and Interrupted_probes_share_the_token_write_limit: 2/2; A.Disabling_the_probe_does_not_disable_effort_resolution: 2/2. 5.5s total deadline, cumulative cap, disabled success/failure. |
| V-16 | Pass | S.Cancellation_and_exit_stop_startup_input: 4/4; A.Process_exit_during_the_effort_dialog_stops_readiness_input: 1/1. Initial settle, navigation, clearance, helper exit and owning-adapter exit covered. |
| V-17 | Pass | E.Legacy_choice_markers_keep_their_existing_classification: 5/5; Unrelated_or_inconsistent_screens_are_not_effort_choices: 9/9; existing trust classes 16/16 and 8/8. |
| V-18 | Pass | F.Interactive_effort_dialog_block_persists_reason_and_cleanup and Card_effort_dialog_block_withholds_boot_and_cleans_up: 2/2 within F 48/48. Stored enum/reason, SystemRequest, Kill then Dispose, no boot, failed card RunAttempt. |
| V-19 | Pass | L.Local_adapter_keeps_requested_effort_and_probes_after_clearance: 2/2 (requested xhigh and high); Local_adapter_names_an_uncleared_effort_dialog: 1/1. Real local PowerShell child through ClaudeAdapter/modern ConPTY, wrong initial highlight, JSON applied-effort/key trace, owned-child exit checked. |
| V-20 | Pass | All P 15/15, existing runner trust/healthy/disabled 8/8, ClaudeLaunchArgsTests 7/7. Diff against 04c1b1c0: ClaudeLaunchArgs.cs comment-only; ModelLevelAliases.cs empty. Runtime and agent-kind owners updated. |
| V-21 | Unverified (allowed conditional result) | One isolated real CLI attempt: Claude 2.1.263, ModernConPty, --model fable --effort xhigh. Normal empty composer and current xhigh/API Usage Billing banner, no effort picker in 45s. Zero keys, zero stub chat requests. Child killed/exit awaited, private scratch deleted, opt-in flags restored. TUnit: 1 skipped, 0 failed, exit 8 (the body ran 45.417s; not a discovery failure). No live picker dismissal or applied-selection acceptance is claimed. |

## Regression guards

| Guard | Executed evidence |
|---|---|
| R-1 | V-1, V-2, V-3; PC-1 |
| R-2 | V-6, V-17; PC-2, PC-6, PC-13 |
| R-3 | V-3, V-4, V-5; PC-3, PC-4, PC-5 |
| R-4 | V-6, V-7; PC-6, PC-7 |
| R-5 | V-9; PC-8; V-21 explicitly unverified |
| R-6 | V-11, V-12; PC-9, PC-10 |
| R-7 | V-8, V-14, V-15, V-16; PC-11 |
| R-8 | V-14, V-18, V-19; PC-12 |
| R-9 | V-13, V-17, V-20 |
| R-10 | V-20 comment/alias diff and 7 launch-argument tests; isolated V-21 limitation retained |

## Final focused regressions

| Class | Passed | Failed | TRX |
|---|---:|---:|---|
| ClaudeEffortPromptTests | 59 | 0 | `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-final-ClaudeEffortPromptTests.trx` |
| ClaudeStartupReadinessTests | 15 | 0 | `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-final-ClaudeStartupReadinessTests.trx` |
| ComposerInputProbeTests | 15 | 0 | `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-final-ComposerInputProbeTests.trx` |
| ClaudeStartupTrustPromptTests | 16 | 0 | `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-final-ClaudeStartupTrustPromptTests.trx` |
| RunnerClaudeAdapterEffortPromptTests | 9 | 0 | `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-final-RunnerClaudeAdapterEffortPromptTests.trx` |
| RunnerClaudeAdapterTrustPromptTests | 8 | 0 | `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-final-RunnerClaudeAdapterTrustPromptTests.trx` |
| ClaudeAdapterEffortPromptTests | 3 | 0 | `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-final-ClaudeAdapterEffortPromptTests.trx` |
| AgentSessionLaunchFailureTests | 48 | 0 | `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-final-AgentSessionLaunchFailureTests.trx` |
| ClaudeLaunchArgsTests | 7 | 0 | `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-final-ClaudeLaunchArgsTests.trx` |

Exact commands, run serially after all control mutations were restored:

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/* --report-trx --report-trx-filename c449-final-ClaudeEffortPromptTests.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/* --report-trx --report-trx-filename c449-final-ClaudeStartupReadinessTests.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ComposerInputProbeTests/* --report-trx --report-trx-filename c449-final-ComposerInputProbeTests.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupTrustPromptTests/* --report-trx --report-trx-filename c449-final-ClaudeStartupTrustPromptTests.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/RunnerClaudeAdapterEffortPromptTests/* --report-trx --report-trx-filename c449-final-RunnerClaudeAdapterEffortPromptTests.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/RunnerClaudeAdapterTrustPromptTests/* --report-trx --report-trx-filename c449-final-RunnerClaudeAdapterTrustPromptTests.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeAdapterEffortPromptTests/* --report-trx --report-trx-filename c449-final-ClaudeAdapterEffortPromptTests.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/AgentSessionLaunchFailureTests/* --report-trx --report-trx-filename c449-final-AgentSessionLaunchFailureTests.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeLaunchArgsTests/* --report-trx --report-trx-filename c449-final-ClaudeLaunchArgsTests.trx
```

## Positive-control evidence

Every red and green invocation was scoped to the exact method. Each mutation was restored from the saved original bytes, its timestamp refreshed, then rebuilt by `dotnet run` before green. There were no zero-test, skipped, fixture-error, compiler-error or framework-timeout substitutes for a PC. All red failures were Shouldly assertions. All temporary mutations are absent from the delivered tree.

PC-6 was repeated after moving its no-subsequent-key assertion ahead of the result assertion; the final red explicitly records `[j, Enter]` instead of `[j]` for both changed-menu rows. The previous run also failed, but the final evidence below is the stronger assertion.

PC-11 equivalent mutation renews the shared stopwatch and cancellation deadline at modal recovery as well as supplying the full remaining budget. Merely changing the inner settle argument would be masked by the coordinator's independent fixed cancellation deadline. PC-10 substitutes pre-modal proof after successful modal clearance at the restart decision.

| Control | Red failed / executed | Restored green | Source / line |
|---|---:|---:|---|
| PC-1 | 3 / 3 | 3 / 3 | `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs:146` |
| PC-2 | 1 / 9 | 9 / 9 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:94` |
| PC-3 | 1 / 3 | 3 / 3 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:39` |
| PC-4 | 4 / 5 | 5 / 5 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:37` |
| PC-5 | 1 / 3 | 3 / 3 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:170` |
| PC-6 | 2 / 2 | 2 / 2 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:176` |
| PC-7 | 1 / 1 | 1 / 1 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:181` |
| PC-8 | 1 / 1 | 1 / 1 | `src/Antiphon.Agents.Pty/ClaudeEffortPrompt.cs:154` |
| PC-9 | 6 / 6 | 6 / 6 | `src/Antiphon.Agents.Pty/ComposerInputProbe.cs:119` |
| PC-10 | 2 / 2 | 2 / 2 | `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs:65` |
| PC-11 | 1 / 1 | 1 / 1 | `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs:60` |
| PC-12 | 1 / 1 | 1 / 1 | `server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:176` |
| PC-13 | 1 / 1 | 1 / 1 | `src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs:328` |

### PC-1

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc01.json`.

```diff
- if (ClaudeEffortPrompt.Parse(screen) is { } effort)
+ if (false && ClaudeEffortPrompt.Parse(screen) is { } effort)
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Captured_screens_are_effort_choices --report-trx --report-trx-filename c449-pc01-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Captured_screens_are_effort_choices --report-trx --report-trx-filename c449-pc01-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc01-red.trx`; exit 2; 0 passed / 3 failed / 3 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc01-green.trx`; exit 0; 3 passed / 0 failed / 3 total.
- Expected red row `Captured_screens_are_effort_choices(2)`: ShouldAssertException: detected     should not be null but was
- Expected red row `Captured_screens_are_effort_choices(0)`: ShouldAssertException: detected     should not be null but was
- Expected red row `Captured_screens_are_effort_choices(1)`: ShouldAssertException: detected     should not be null but was

### PC-2

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc02.json`.

```diff
- || question.Groups[1].Value != change.Groups[1].Value
+ || false
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Unrelated_or_inconsistent_screens_are_not_effort_choices --report-trx --report-trx-filename c449-pc02-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Unrelated_or_inconsistent_screens_are_not_effort_choices --report-trx --report-trx-filename c449-pc02-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc02-red.trx`; exit 2; 8 passed / 1 failed / 9 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc02-green.trx`; exit 0; 9 passed / 0 failed / 9 total.
- Expected red row `Unrelated_or_inconsistent_screens_are_not_effort_choices(model)`: ShouldAssertException: prompt?.Kind     should not be ClaudeBlockingPromptKind.EffortChoice     but was

### PC-3

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc03.json`.

```diff
- return intent.Value == Suggested ? ClaudeEffortOption.Switch : ClaudeEffortOption.Unknown;
+ return ClaudeEffortOption.Keep;
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Requested_effort_is_applied --report-trx --report-trx-filename c449-pc03-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Requested_effort_is_applied --report-trx --report-trx-filename c449-pc03-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc03-red.trx`; exit 2; 2 passed / 1 failed / 3 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc03-green.trx`; exit 0; 3 passed / 0 failed / 3 total.
- Expected red row `Requested_effort_is_applied(Fable 5·1, xhigh, high, 1, 2)`: ShouldAssertException: fake.AppliedEffort     should be "high"     but was "xhigh"     difference Difference     |  |    |    |    |    |                   | \|/  \|/  \|/  \|/  \|/   Index          | 0    1    2    3    4     Expected Valu...

### PC-4

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc04.json`.

```diff
- if (intent.Invalid) return ClaudeEffortOption.Unknown;
+ if (intent.Invalid) return ClaudeEffortOption.Keep;
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Ambiguous_or_unmatched_intent_types_nothing --report-trx --report-trx-filename c449-pc04-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Ambiguous_or_unmatched_intent_types_nothing --report-trx --report-trx-filename c449-pc04-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc04-red.trx`; exit 2; 1 passed / 4 failed / 5 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc04-green.trx`; exit 0; 5 passed / 0 failed / 5 total.
- Expected red row `Ambiguous_or_unmatched_intent_types_nothing(empty)`: ShouldAssertException: result.Cleared     should be False     but was True
- Expected red row `Ambiguous_or_unmatched_intent_types_nothing(conflict)`: ShouldAssertException: result.Cleared     should be False     but was True
- Expected red row `Ambiguous_or_unmatched_intent_types_nothing(missing)`: ShouldAssertException: result.Cleared     should be False     but was True
- Expected red row `Ambiguous_or_unmatched_intent_types_nothing(unsupported)`: ShouldAssertException: result.Cleared     should be False     but was True

### PC-5

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc05.json`.

```diff
- if (menu.Highlight != target) return Result(false, "intended highlight not reached; Enter withheld");
+ if (false) return Result(false, "intended highlight not reached; Enter withheld");
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/An_unmovable_wrong_highlight_withholds_Enter --report-trx --report-trx-filename c449-pc05-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/An_unmovable_wrong_highlight_withholds_Enter --report-trx --report-trx-filename c449-pc05-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc05-red.trx`; exit 2; 2 passed / 1 failed / 3 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc05-green.trx`; exit 0; 3 passed / 0 failed / 3 total.
- Expected red row `An_unmovable_wrong_highlight_withholds_Enter(immovable)`: ShouldAssertException: fake.Writes.Select(w => w.Key)     should not contain " "     but was actually ["j", "\u001b[B", "\u000e", " "]

### PC-6

Implementation SHA: `15652113179c4fa84e9668d9cc7ba5a008ee6e66`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc06.json`.

```diff
- var fresh = Parse(await snapshotScreen(token));
+ var fresh = menu;
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/A_changed_dialog_identity_stops_further_input --report-trx --report-trx-filename c449-pc06-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/A_changed_dialog_identity_stops_further_input --report-trx --report-trx-filename c449-pc06-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc06-red.trx`; exit 2; 0 passed / 2 failed / 2 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc06-green.trx`; exit 0; 2 passed / 0 failed / 2 total.
- Expected red row `A_changed_dialog_identity_stops_further_input(different-effort)`: ShouldAssertException: fake.Writes.Select(w => w.Key)     should be ["j"]     but was (case sensitive comparison) ["j", " "]     difference ["j", *" "*]
- Expected red row `A_changed_dialog_identity_stops_further_input(permission)`: ShouldAssertException: fake.Writes.Select(w => w.Key)     should be ["j"]     but was (case sensitive comparison) ["j", " "]     difference ["j", *" "*]

### PC-7

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc07.json`.

```diff
- if (key == "\r") { enters++; nextEnter = clock.Elapsed + ClaudeTrustDialogKeys.HighlightSettle; }
+ if (key == "\r") { enters++; return Result(true, "unverified Enter accepted"); }
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/Redraw_is_not_clearance_before_the_probe --report-trx --report-trx-filename c449-pc07-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/Redraw_is_not_clearance_before_the_probe --report-trx --report-trx-filename c449-pc07-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc07-red.trx`; exit 2; 0 passed / 1 failed / 1 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc07-green.trx`; exit 0; 1 passed / 0 failed / 1 total.
- Expected red row `Redraw_is_not_clearance_before_the_probe`: ShouldAssertException: premature     should be False     but was True

### PC-8

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc08.json`.

```diff
- if (observed is not null && observed != desired)
+ if (false)
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/A_contradictory_resulting_effort_fails_readiness --report-trx --report-trx-filename c449-pc08-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/A_contradictory_resulting_effort_fails_readiness --report-trx --report-trx-filename c449-pc08-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc08-red.trx`; exit 2; 0 passed / 1 failed / 1 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc08-green.trx`; exit 0; 1 passed / 0 failed / 1 total.
- Expected red row `A_contradictory_resulting_effort_fails_readiness`: ShouldAssertException: (await fake.ReadyAsync()).Outcome     should be ClaudeReadinessOutcome.EffortFailed     but was ClaudeReadinessOutcome.Ready

### PC-9

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc09.json`.

```diff
- bool Blocked(string screen) => isBlocked?.Invoke(screen) == true;
+ bool Blocked(string screen) => false;
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ComposerInputProbeTests/A_modal_interrupts_before_each_probe_write_or_verdict --report-trx --report-trx-filename c449-pc09-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ComposerInputProbeTests/A_modal_interrupts_before_each_probe_write_or_verdict --report-trx --report-trx-filename c449-pc09-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc09-red.trx`; exit 2; 0 passed / 6 failed / 6 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc09-green.trx`; exit 0; 6 passed / 0 failed / 6 total.
- Expected red row `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeFirstClear)`: ShouldAssertException: result.Outcome     should be ComposerProbeOutcome.InterruptedByModal     but was ComposerProbeOutcome.Responsive
- Expected red row `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeResponsive)`: ShouldAssertException: result.Outcome     should be ComposerProbeOutcome.InterruptedByModal     but was ComposerProbeOutcome.Responsive
- Expected red row `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeClearRetry)`: ShouldAssertException: result.Outcome     should be ComposerProbeOutcome.InterruptedByModal     but was ComposerProbeOutcome.Responsive
- Expected red row `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeFirstToken)`: ShouldAssertException: result.Outcome     should be ComposerProbeOutcome.InterruptedByModal     but was ComposerProbeOutcome.NeverAppeared
- Expected red row `A_modal_interrupts_before_each_probe_write_or_verdict(AwaitingToken)`: ShouldAssertException: result.Outcome     should be ComposerProbeOutcome.InterruptedByModal     but was ComposerProbeOutcome.NeverAppeared
- Expected red row `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeRetype)`: ShouldAssertException: result.Outcome     should be ComposerProbeOutcome.InterruptedByModal     but was ComposerProbeOutcome.NeverAppeared

### PC-10

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc10.json`.

```diff
- if (!result.Cleared) return new(active, detail, writes);
+ if (!result.Cleared) return new(active, detail, writes); if (writes > 0) return new(ClaudeReadinessOutcome.Ready, "reused pre-modal evidence", writes);
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/A_late_dialog_requires_a_fresh_post_clearance_round_trip --report-trx --report-trx-filename c449-pc10-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/A_late_dialog_requires_a_fresh_post_clearance_round_trip --report-trx --report-trx-filename c449-pc10-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc10-red.trx`; exit 2; 0 passed / 2 failed / 2 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc10-green.trx`; exit 0; 2 passed / 0 failed / 2 total.
- Expected red row `A_late_dialog_requires_a_fresh_post_clearance_round_trip(before-echo)`: ShouldAssertException: fake.TokenWrites     should be 2     but was 1
- Expected red row `A_late_dialog_requires_a_fresh_post_clearance_round_trip(after-echo)`: ShouldAssertException: fake.TokenWrites     should be 2     but was 1

### PC-11

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc11.json`.

```diff
- var remaining = budget - clock.Elapsed;
+ clock.Restart(); bounded.CancelAfter(budget); var remaining = budget;
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/Recurring_dialogs_cannot_renew_the_readiness_deadline --report-trx --report-trx-filename c449-pc11-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeStartupReadinessTests/Recurring_dialogs_cannot_renew_the_readiness_deadline --report-trx --report-trx-filename c449-pc11-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc11-red.trx`; exit 2; 0 passed / 1 failed / 1 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc11-green.trx`; exit 0; 1 passed / 0 failed / 1 total.
- Expected red row `Recurring_dialogs_cannot_renew_the_readiness_deadline`: ShouldAssertException: await Task.WhenAny(task, Task.Delay(8000))     should be System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1[Antiphon.Agents.Pty.ClaudeReadinessResult,Antiphon.Agents.Pty.ClaudeStartupReadi...

### PC-12

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc12.json`.

```diff
- _launchBlock = new(AgentLaunchBlockKind.EffortDialogNotCleared, result.Detail);
+ return true;
```

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/RunnerClaudeAdapterEffortPromptTests/An_effort_dialog_that_never_clears_fails_within_its_budget --report-trx --report-trx-filename c449-pc12-red.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/RunnerClaudeAdapterEffortPromptTests/An_effort_dialog_that_never_clears_fails_within_its_budget --report-trx --report-trx-filename c449-pc12-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-pc12-red.trx`; exit 2; 0 passed / 1 failed / 1 total.
green: `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-pc12-green.trx`; exit 0; 1 passed / 0 failed / 1 total.
- Expected red row `An_effort_dialog_that_never_clears_fails_within_its_budget`: ShouldAssertException: await task     should be False     but was True

### PC-13

Implementation SHA: `4f9a220447a26935e6ea7bffe650725f59ed66a2`. Machine-readable run record: `C:\src\Antiphon\.antiphon\c449-evidence\pc13.json`.

```diff
- if (prompt.Kind == ClaudeBlockingPromptKind.EffortChoice)
+ if (false)
```

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Generic_answering_cannot_confirm_an_effort_choice --report-trx --report-trx-filename c449-pc13-red.trx
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c449/ -- --treenode-filter /*/*/ClaudeEffortPromptTests/Generic_answering_cannot_confirm_an_effort_choice --report-trx --report-trx-filename c449-pc13-green.trx
```

red: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc13-red.trx`; exit 2; 0 passed / 1 failed / 1 total.
green: `C:\src\Antiphon\tests\Antiphon.Agents.Pty.Tests\bin-c449\TestResults\c449-pc13-green.trx`; exit 0; 1 passed / 0 failed / 1 total.
- Expected red row `Generic_answering_cannot_confirm_an_effort_choice`: ShouldAssertException: result.Cleared     should be False     but was True

PC-9 checkpoint outcomes separately: BeforeFirstToken, AwaitingToken and BeforeRetype incorrectly returned NeverAppeared under mutation; BeforeFirstClear, BeforeClearRetry and BeforeResponsive incorrectly returned Responsive. All six returned InterruptedByModal with their exact permitted input arrays after restoration.

## Conditional real-picker acceptance

`ClaudeEffortPromptCanaryTests.Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer` was invoked once with both opt-in flags set to 1 and restored afterward. The actual executable, private CLAUDE_CONFIG_DIR seeded only for onboarding/trust and a generated synthetic key, local random-port FakeLlmApiServer, and ModernConPty were used. No live preferences or provider credentials were copied or changed.

The actual target version **2.1.263** showed **Fable 5.1 with xhigh effort · API Usage Billing** and the normal empty composer. The startup effort menu did not appear within 45 seconds. Result: **V-21 unverified**, 1 skipped, 0 failures, TUnit exit 8; no model/effort-picker acceptance pass is claimed. Zero input keys and zero `/v1/messages` chat requests were recorded. The test killed its own child, awaited exit, disposed it and removed its private scratch directory. This observation is not a claim that a live subscription picker uses the tested navigation bindings.

Sanitized snapshots, CLI version/backend, key timestamps and chat-request census: `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-real-picker-evidence.json`.
Canary TRX: `C:\src\Antiphon\tests\Antiphon.Tests\bin-c449\TestResults\c449-real-picker.trx`.
Invocation log: `C:\src\Antiphon\.antiphon\c449-evidence\real-picker.log`.

```powershell
# Preserve/restore ANTIPHON_HEADED_TESTS and ANTIPHON_REAL_CLI_STUB_TESTS around this opt-in invocation.
$env:ANTIPHON_HEADED_TESTS = '1'
$env:ANTIPHON_REAL_CLI_STUB_TESTS = '1'
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c449/ -- --treenode-filter "/*/*/ClaudeEffortPromptCanaryTests/Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer" --report-trx --report-trx-filename c449-real-picker.trx
```

## Limits and handoff

An initial probe regression run had 1 failed checkpoint (BeforeClearRetry) because the test scheduled its modal after the retry write. The fixture now waits until that write is eligible before publishing the blocker on the fresh pre-write snapshot. The scoped six-row rerun passed, its mutation failed all six rows, and the final complete probe class passed 15/15. Initial test compilation errors were corrected before executable verification; none was counted as a positive control.

No mandatory verification remains blocked. Review should judge the parser/authorization boundaries and the conditional live-acceptance limitation. No deployment or live subscription retry is implied. The launch-argument diff is comment-only and the model-alias diff is empty against `04c1b1c0`; `git diff --check` passes. Existing unrelated untracked `.perfmon/` and `[]` were left alone.

Local evidence index: `C:\src\Antiphon\.antiphon\c449-evidence\regressions.json`; PC runner: `C:\src\Antiphon\.antiphon\c449-pcs.py`; serial regression runner: `C:\src\Antiphon\.antiphon\c449-regressions.py`. Source was committed before long runs and was never edited during a running test.
