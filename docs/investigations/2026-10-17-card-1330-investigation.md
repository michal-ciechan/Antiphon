# CARD-1330 — orchestrator worst-case composition is 190 over the 500-char argv reserve

Confirmed. On `origin/master` (`f01104c85c2c862ff9ca6e5388c58c415fb62590`, this checkout) the shipped Orchestrator composition's command-line estimate is 29,690 against the test's 29,500 headroom. The same bytes are at `016b6c3aa9a904bec34aeacf274874bbbc49464b`. `origin/rc/2026-10-15` (`5e5d5cc728949420647d15a932766e70755a6f01`) does not contain the extra line and estimates 29,499, which passes. Nothing after either tip repairs it.

Not done, noted: no source edit; the trim below is the proposed Code change.

## What fails

`InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` (`tests/Antiphon.Tests/Application/InstructionBundleTests.cs:669-691`) does two checks for every non-specialist `AgentTaskRole` and both `AgentTaskKind` values:

1. `composed.Text.Length < DelegationSettings.CommandLineBudgetChars` (30,000). This passes. Orchestrator text is 29,566.
2. `EnsureWithinCommandLineBudget` with `budget - 500` (29,500), other args `--name task-1a2b3c4d --model opus --session-id <guid D>`, and `ChannelPreamble.TelegramPresetTemplate` forced to CRLF. This throws. The exception text is `{total} chars against a budget of {budgetChars}` (`InstructionBundleComposer.cs:290-292`), which is the card's "29,690 vs 29,500".

`InstructionFragmentTests.C1213_ExpandedBudgetIsEnforced` (`InstructionFragmentTests.cs:240-251`) asserts the same estimate for the same loop. It is the same red, not a second cause.

Specialists are excluded. `ForDelegate` for `Orchestrator` ignores role (`InstructionBundles.cs:265-266`), so every Orchestrator role is one composition: `orchestrator`, `delegate-basics`, `board-api`, `style-explanatory`, then the preset. Shipped `RolePolicyEntry.Bundles` is empty (`DelegationSettings.cs:1152-1155`), so no tool bundle is added.

## Measurement

Reconstructed from the catalog rules (LF + trim, SHA-256 version, `[bundle:key vxxxxxxxx]` header, `{{GIT-COMMON}}` expanded once) and `EnsureWithinCommandLineBudget` (`InstructionBundleComposer.cs:270-283`): other args charged `length + 3`, append charged `text + "--append-system-prompt".Length + 6`. The reconstruction hits the card's 29,690, and the raw `orchestrator.md` SHA-256 is the pin `d05f063b3d21f5898c0f9bcf36913dd0ed84fd6a66cf57c973207efd698f7ce0` (`CheckpointRepeatDocumentationTests.cs:37-38`). The test process was not run.

Worst case, master / `016b6c3aa` / HEAD, Orchestrator/Custom (every Orchestrator role is identical):

| Piece | Chars in the composition |
|---|---:|
| `orchestrator` rendered (`[bundle:orchestrator v3a0f40cd]` + trimmed text 13,650) | 13,682 |
| `delegate-basics` rendered, before expansion | 6,345 |
| `git-common` expansion (`{{GIT-COMMON}}` is 14; fragment render is 1,193) | +1,179 |
| `delegate-basics` expanded | 7,524 |
| `board-api` rendered | 5,347 |
| `style-explanatory` rendered | 619 |
| four `\n\n` separators | 8 |
| Telegram preset, CRLF (`ChannelPreamble.cs:41-44`, `60-81`) | 2,386 |
| composed text | 29,566 |
| `--append-system-prompt` plus 6 quote chars | 28 |
| six other args at length+3 | 96 |
| estimate | 29,690 |

29,690 - 29,500 = 190 over the reserve. 30,000 - 29,690 = 310 under the transport ceiling.

Other shapes, same attachments and preset, all under 29,500: Worker/TestDesign 18,523 (worst worker), Worker/Review 18,515, Worker/Mutation 18,478, Worker/Code 18,436, Worker/Plan 17,316, Worker/Investigate 16,746, helper Worker/Custom 16,006. Only the orchestrator bundle is near the ceiling. Its rendered header is fixed width, so one catalog character moves the estimate by one.

## The line that crosses it

`d68e6cfd252742c3f2436aec4c6c0cfef1e764b8` (`feat(CARD-1320): point keep-six docs at the server setting`, cherry-picked from `bb9ecc1b`) inserts `server/Bundles/orchestrator.md:97`:

> A keep-six reminder names the server setting's shortfall. Dispatch up to that shortfall under the same pipeline rules. Declare a hold with scripts/keep-six.ps1. The server does not dispatch.

That line plus its newline is 191 characters. Catalog length goes 13,459 to 13,650. Estimate goes 29,499 to 29,690. Parent `d68e6cfd2^` and `origin/rc/2026-10-15` share raw SHA `cf14d2c5261929b555d8416105428dce46a8f7dd8658a8907e4f8559d71caa00` (the previous pin). `git diff origin/rc/2026-10-15 HEAD -- server/Bundles` is that one insertion. 29,499 <= 29,500, so rc passes by one.

`58c11c2714e6021b9eec4467f7527d022bd397a3` does not edit the bundle. It adds 14 lines to `tests/Antiphon.Tests/Api/KeepSixReminderTests.cs`. It is a descendant of `d68e6cfd2`, which is why the card can call 58c11c27 the land while the bytes changed in `d68e6cfd2`.

`016b6c3aa` is after both. Its `orchestrator.md` matches HEAD. The card's "already 29,690 at 016b6c3aa, before the S4 bundle edit" is the right number and the wrong order: that commit already contains the line, and the tree without the line is not red.

## Why the budgets are what they are

`CommandLineBudgetChars` defaults to 30,000 (`DelegationSettings.cs:303-330`). CreateProcessW's command line is about 32,767 UTF-16 characters. The comment reserves about 2,700 for the executable, base args, `--session-id` / `--resume`, and quoting the composer does not see. CARD-0497 forbids a configured value from raising the runner ceiling: `CodexWindowsLaunchPolicy.EffectiveCeiling` is `Math.Min(configured, 30000)` (`CodexWindowsLaunchPolicy.cs:285-286`, ceiling `CodexLaunchProblemTypes.TransportCeilingChars`), and `InstructionBundleComposer.BuildPinPayload` refuses above `Math.Min(commandLineBudgetChars, 30000)` (`InstructionBundleComposer.cs:224-226`). The Grok runner default is the same 30,000 (`SessionRunnerRuntime.cs:568`).

The failing 29,500 is not that setting. The test subtracts 500 (`InstructionBundleTests.cs:677-690`). `EnsureWithinCommandLineBudget` charges three characters per argument (one space, two quotes) and says so: an estimate, not CRT proof (`InstructionBundleComposer.cs:260-266`, `276`). CARD-0884 put the 500 there so quote expansion and args the fixture does not list still fit. CARD-0479, CARD-0822, CARD-1065, and CARD-1213 each shortened `orchestrator.md` and re-pinned the raw SHA rather than move the 500. The pin comment says to recompute the hash when the file changes (`CheckpointRepeatDocumentationTests.cs:28-36`).

Production does not use 29,500. `AgentTaskDispatcher` calls `EnsureWithinCommandLineBudget` with `_settings.CommandLineBudgetChars` and skips it for Grok (`AgentTaskDispatcher.cs:7072-7074`). 29,690 <= 30,000, so this composition does not fail a launch. Grok writes the body to a rules file and does not charge it on argv. The red is the reserve, paid on every orchestrator because `orchestrator` plus `delegate-basics` is the Orchestrator role map.

Raising `CommandLineBudgetChars` above 30,000 does not raise the runner ceiling. Lowering the test's 500 to 310 would make 29,690 pass and would spend the reserve those cards kept. The bundles are delivered to every orchestrator (delegate-basics to every non-specialist delegate). Shorter text is the repair that matches the reserve's purpose.

## Smallest fix

Keep line 97. `KeepSixDocumentationTests.bundle_says_the_server_does_not_dispatch_and_names_the_hold_script` pins `scripts/keep-six.ps1` and `The server does not dispatch` (`KeepSixDocumentationTests.cs:16-20`). The line is 191 characters and those two pins are the rule S4 added. It cannot shrink by 191 without losing the rule.

Delete 197 characters of unpinned restatement from `server/Bundles/orchestrator.md`. Header width stays 8 hex digits, so the estimate falls by 197, from 29,690 to 29,493 (7 under 29,500). Trimmed length falls from 13,650 to 13,453, still under the 14,310 cap (`InstructionBundleTests.cs:644`).

1. `orchestrator.md:9`, 90 characters including the newline: `Your context is scarce for the whole run; each file read consumes irreplaceable capacity.` Lines 7-8 and 10 keep the rule: delegate the reading, do not read it even one grep away or via another frontier-tier delegate, read directly only to quote exactly or judge personally. Pins `Delegate the reading.`, `frontier-tier`, and `quote exactly or must judge personally` stay (`InstructionBundleTests.cs:610-612`).
2. `orchestrator.md:93`, 51 characters: `On every completion dispatch the named next stage. ` Lines 32-36 already say to dispatch the named stage from the parsed `next=` / `handoff:` on the completion header. `Land a stage's work as soon as it is confirmed.` stays. No test pins the deleted sentence.
3. `orchestrator.md:19-20`, 56 characters: `; this paragraph is only how to choose the next provider`. Leave `The blocked row is the retry barrier.` The paragraph still says StoppedBeforeFirstPrompt is a launch incident, do not re-dispatch the same agent kind, surface the blocked item, and offer a ClaudeCode delegate. No test pins the deleted clause.

A simulation of those three deletions, and no other bytes, produces raw SHA-256 `7b34e63a29128753e2da3667af497d19c687aebe7a13943fe130a83394f6fd41` and content version `521b5918`. Recompute both from the committed file if the edit differs by a newline. Put the raw hash in `CheckpointRepeatDocumentationTests.cs:38` in place of `d05f063b3d21f5898c0f9bcf36913dd0ed84fd6a66cf57c973207efd698f7ce0`.

`C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders` (`InstructionBundleTests.cs:984-1005`) hashes the LF-trimmed source and compares it to the embedded resource and `[bundle:{key} v{8}]`. It has no hardcoded orchestrator version. A rebuild embeds the new text; do not edit that test. `PolicyRefreshDeltaTests` uses fictional stamps (`v26dea68f`, `v3c1f0a9e`), not the live hash.

Do not change `CommandLineBudgetChars`, the `- 500`, or the keep-six line.

## Red-first witness

The failure already exists on master, so a new test cannot go red first. One filter, the method the card names:

`/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget`

Run it through `scripts/build-slot.ps1` as `dotnet run --project tests/Antiphon.Tests`. It is red before the trim and green after. The same trim makes `C1213_ExpandedBudgetIsEnforced` green; that method is not a second filter. Re-pin the raw SHA in the same change or `repeat_budget_reaches_code_briefs_without_bundle_growth` goes red.

## Remaining uncertainty

The 29,690 and 29,499 figures are a source reconstruction of the composer, not a TUnit process. They match the card's failure number and the checked-in raw SHA, and the rc delta is exactly the inserted line. The preset text was rebuilt from `BuildPreset`; it sits inside the sum that lands on 29,690.
