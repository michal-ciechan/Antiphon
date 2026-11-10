# CARD-1821 Review round 1 (Final): BLOCKED, 1 blocker

Subject: origin/feat/card-task-e7a6c7a9 @ f34da71338a916e39b1a061e0be02e0377589dda (Grok Code author), base master 72fada66545c2dc96a0710827fbc57558700dd61.

## D-1 (blocker): a genuine final after a housekeeping-span sibling phantom is never attributed, so the task is stuck with no settle and no nudge

- **Where:** server/Application/Services/TaskReportTurnSelector.cs:150-178 (housekeeping branch), together with `RealOwnerOfSkippedTurn` (:322) / `NewestRealBetween` (:390). The owner search is bounded below by `olderEnd`, the next older TurnEnd.
- **Failure:** rows #1 marked brief, #2 CompactBoundary (auto), #3 continuation UserPrompt, #4 TurnEnd end_turn msg_b, #5 text msg_b, #6 ToolCall msg_b, #7 text msg_c ending with `[antiphon-report:<id> done]`, #8 TurnEnd end_turn msg_c. At f34da7133 `SelectAsync` returns `None` with skipped `8:inert-housekeeping,4:sibling-tool`. #8's housekeeping lookup finds no real prompt between olderEnd=#4 and #8, so it skips as inert. #4 is then skipped as a sibling, and nothing older remains. The local-command shape gives the same result: #1 brief, #2 `<command-name>/status</command-name>`, #3 TurnEnd end_turn msg_b, #4 ToolCall msg_b, #5 text+token msg_c, #6 TurnEnd end_turn msg_c → `None` (`6:inert-housekeeping,3:sibling-tool`). At base 72fada665 both shapes select the phantom (#4 / #3). That is the old false nudge, and it at least recovers through the nudge reply. Every report path (ReplyService ExtractMarkedTurn, dispatcher deferred sweep incl. CARD-0288 marked re-hand, LateFailedReportForwarder) is selection-only. The unmarked-waiting watchdog needs ReportNudgedAt, which is never set. So the marked report sits in the transcript and the task stays Dispatched until a human acts.
- **Why it matters:** the card's own premise is that long tasks compact routinely and the CARD-0282 phantom follows. "Compaction → phantom → genuine final in the same turn" is the normal shape of exactly the tasks this card targets. The fix turns a false nudge into a silent permanent hang, which is the stuck direction the brief asked about in (a). None of the 9 new tests seeds a phantom followed by a final, so the suite cannot see it. `Text_only_final_after_compaction_continuation_still_settles` has no prior phantom.
- **Fix:** a skipped sibling phantom must not act as the `olderEnd` floor for a newer housekeeping boundary in the same span. Either compute `olderEnd` past end_turn boundaries already proven siblings, or carry the skipped phantom's owner forward to resolve the newer inert-housekeeping boundary. Add both sequences above as red witnesses first: Selected at #8 / #6, settled Succeeded with the msg_c text, no nudge. Scratch probe source: /tmp/claude-1654/-work-worktrees-task-86bd65be/f55e0c96-0023-4866-9ec7-388ff69b2860/scratchpad/ZzReview1821ProbeTests.cs (P1, P5).

## Advisories (do not hold a land; file as cards if not folded into the D-1 fix)

- **A-1 (barrier asymmetry):** the new housekeeping and notification skips `continue` without setting `phantomBarrier`, unlike the real-prompt path. Probe P2: #1 brief, #3 end_turn msg_a (no token), #4 marked nudge prompt, #5 continuation, #6 phantom msg_b + tool. It selects Selected@#3, while the real-path analogue (P2b, no continuation) selects None. Today this is held by `NudgeStillOutstandingAsync` (boundary ≤ ReportNudgedSequence). The unmarked-waiting watchdog, though, accepts boundary == nudgedSeq, so an idle-looking session could Block on the older boundary. The D-1 fix is the natural place to align it.
- **A-2 (efficiency):** `LoadSiblingToolsAsync` is one query per SelectAsync, floored at the first time-eligible prompt and range-scanned on (AgentSessionId, Kind, Sequence). There is no N+1 (LateFailedReportForwarder's ≤32-window loop is pre-existing). The `needed` gate is session-wide (any end_turn with an ApiCallId), so in practice it always runs, not only on the narrow path. An eligible null-Timestamp prompt lowers the floor. Neither is a defect.
- Notification path with an older same-task boundary at the fix (P4 with a token on its own line) was not measured at f34da7133. At base it false-settles (Succeeded, "Folded."), which the new skip addresses.

## Checks (a)-(e)

- (a) Existing tests confirm a text-only final after a continuation settles, max_tokens stays terminal (settles), and cancelled records the interrupted-turn warning (1). Own probes: P3 (same-id tool before the owner/pre-dispatch does not hide a final) passed. P1 and P5 found D-1. P2 found A-1.
- (b) See A-2.
- (c) CARD-1821 text matches the change: the housekeeping-owned path, the notification path "where safe", max_tokens/cancelled terminal, and the optional floor. Nothing broader.
- (d) A None next real prompt leaves the cap open to session end. That is safe because ApiCallIds are per response. Two owners: A-1.
- (e) See the runs below.

## Runs (all through build-slot, UseAppHost=false, native output, TEMP=/tmp)

| Row | SHA | Result |
|---|---|---|
| build Antiphon.Tests | f34da7133 | 0 errors (lease bda22ba7) |
| SiblingToolReportBoundaryTests + TaskReportHousekeepingTests | f34da7133 | 25/25 (14 + 11) |
| guard batch TestClassificationPolicy*/Guard*/SlowTestRegistryMerge*/SlowTestTripwire*/NativeLinuxPolicyWitnessTests*/TestLaneCategoryGuardTests* | f34da7133 | 53/53 (41/1/5/2/3/1); with the row above = 78/78 |
| scratch probes P1-P5 (+P2b) | f34da7133 + probe file | 4/6; P1, P5 red = D-1 |
| mutant: selector restored to base (skip removed + no floor) | f34da7133 tests, base selector | SiblingTool 10/14: the 4 new witnesses red (Compaction, Sibling_tool_before_continuation, Local_command, Claude_notification), matching Code's cc9f02ef1 4/14 |

The C714 partials (AgentTaskReplyIntegrationTests 230, AgentTaskDeliveryWatchdogTests 82) were not run. They are not touched, and the blocker makes a broad regression run moot this round.
Owner-floor mutant (housekeeping owner → raw continuation prompt): not run separately. `Sibling_tool_before_the_continuation_still_hides_the_end_turn` is the witness for it, and it was red in the base-selector mutant.
check-evidence-diff 72fada665..f34da7133: commits=2 entries=0 violations=0. merge-tree vs origin/master 72fada665: clean (tree bd602b75). tests/test-execution-policy.json unchanged.
Worktree was restored to f34da7133 (selector checked out, probe file removed). Its bin/obj currently hold the base-selector build.
