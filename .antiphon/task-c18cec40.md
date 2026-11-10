# CARD-1821 Review round 2 (Final, diff only): CLEAN

Subject: origin/feat/card-task-6d107e28 @ 3cd4fe4bd5a99eb451211d4b453c8e15864381a8 (Grok Code author). The code under test is 01d1a848146efb506ddfbc709797cc1004cf8424, and the tip adds only the report. The range is f34da71338a916e39b1a061e0be02e0377589dda..3cd4fe4bd (round 1 reviewed f34da7133, Review 86bd65be). Original Code landing owner: 6d107e28.

Round-1 D-1 is fixed, and no new blocker or regression was found.

## D-1 re-check (own round-1 probes)

The probes were scratch tests added uncommitted to tests/Antiphon.Tests/Application at 3cd4fe4bd and removed afterwards.

| Probe | Sequence | Result at 3cd4fe4bd |
|---|---|---|
| P1 / P1n | brief, auto CompactBoundary, continuation, end_turn msg_b, text msg_b, ToolCall msg_b, text+done msg_c, end_turn msg_c | Selected@8, owner 1. After OnTurnEnd: Succeeded "Real final.", ReportNudgedAt null |
| P5 / P5s | brief, `<command-name>/status`, end_turn msg_b, ToolCall msg_b, text+done msg_c, end_turn msg_c | Selected@6. Succeeded "LC final.", not nudged |

## Opposite-failure hunt (phantom wrongly selected, premature settle, nudge on a phantom)

| Probe | Sequence | Result |
|---|---|---|
| N1 | compaction phantom alone (with done token in its text) | None (5:sibling-tool). Dispatched, not nudged |
| N1b | local-command phantom alone, then later text with no boundary | None. Dispatched, not nudged |
| N2 | two phantoms (msg_b, msg_c), then final msg_d | Selected@9. Succeeded, not nudged |
| N2b | two phantoms, no final (token in the 2nd) | None (7,4 sibling-tool). Not nudged |
| N3 | final msg_c, then phantom msg_d | Selected@5 (6:inert-housekeeping). Succeeded "Final first.", not nudged |
| N4 | brief phantom msg_a, notification, notification phantom msg_b with token | None (7,3 sibling-tool). Not settled, not nudged |
| N5 (= r1 P2) | brief, end_turn msg_a, marked nudge, continuation, phantom msg_b | None (was Selected@3 at f34da7133). Not nudged. A-1 fixed |
| N6 | compaction phantom, then max_tokens msg_c | Selected@7 (terminal); nudged as expected for a tokenless terminal |
| N6b | local-command phantom, then cancelled | Interrupted@5, not settled |
| N7 | end_turn msg_b with no stored ToolCall (other-id tool earlier) | Succeeded "No-tool final." |
| N8 | session B has phantom ToolCall msg_b; session A has final end_turn msg_b | A Succeeded; B None, not nudged (tools are session-scoped) |
| N9 | compaction phantom, then unmarked real prompt, compaction, end_turn | Uncorrelated@10, owner 6. Not settled |
| P4 (r1) | brief's own tokenless end_turn msg_a, notification, notification phantom with token | Selected@3, which nudges on the brief's genuine tokenless end_turn. This is the same as the real-prompt analogue and as f34da7133 (the barrier owner is the brief itself). It is no longer a false settle as at base. Not a defect |

## Query shape

The diff adds no DB access. PhantomTransparentFloor, IsTransparentSiblingPhantom and RememberPhantomBarrier are in memory over the rows already loaded. LoadSiblingToolsAsync is unchanged: one gated range scan per SelectAsync on (AgentSessionId, Kind, Sequence), floored at the first time-eligible prompt and open to session end. So there is no N+1.

## Runs (build-slot, UseAppHost=false, native output, TEMP=/tmp)

| Row | Lease | Result |
|---|---|---|
| Build Antiphon.Tests at 3cd4fe4bd + probe file | de028f2c-284e-431c-9a59-301a9611b9b9 (300s) | 0 errors |
| Probes (20) + SiblingToolReportBoundaryTests 26 + TaskReportHousekeepingTests 11 + guard union 78 (TestClassificationPolicy 41, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, TestLaneCategoryGuard 1, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 9) | 23ea72b5-6b7d-4133-a038-ed5253a9faef (142s) | 135/135, 0 skipped (37/37 touched, 78/78 guards) |
| Mutant build (env-gated, one build): floor = olderEnd; barrier = RememberPhantomBarrier no-op | 63ba9ab9-46fe-4a35-b535-b7872afdb6e0 | 0 errors |
| ZZ_M=floor: probes + SiblingTool (46) | c54cb632-4a89-4516-ab6f-3cb11c1cc7d7 | 13 red: Code's 6 (Compaction_/Local_command_phantom_then_genuine_final, Phantom_then_final_twice, Tool_stored_before_the_phantom, Max_tokens_final_after/Cancelled_stop_after_compaction_phantom) + own P1, P1n, P5, P5s, N2, N6, N6b |
| ZZ_M=barrier: same 46 | bf6a4d16-5f3c-4d2c-9a2a-df89977838ec | 3 red: Marked_nudge_then_compaction_phantom, Notification_phantom_does_not_select_the_older_marked_boundary, own N5 |
| Rebuild at restored 3cd4fe4bd (probe removed, selector restored) | c2162898-a5c0-43ff-8abb-c5ae8fa41eb9 (181s) | 0 errors |
| Selector users: TaskReportTurnSelectorTests 2, ChannelOutboundRecoveryTests 18, AgentTaskDeliveryWatchdogTests 86 (C714 partial) + SiblingTool 26 + Housekeeping 11 | c78fa25e-0eb3-47ca-b649-d0e53c0f761d (564s) | 143/143, 0 skipped |

AgentTaskReplyIntegrationTests (~230, C714 partial) was not run because of the 40-minute budget. The diff stays inside the selector, the product users are selection-only, and the SiblingTool probes exercise ReplyService.OnTurnEndAsync end to end.

- check-evidence-diff f34da7133..3cd4fe4bd: commits=3, entries=1 (the .antiphon report md), violations=0. Over 72fada665..3cd4fe4bd: commits=5, entries=1, violations=0.
- merge-tree vs origin/master 52e13fdca8dcb780ef4afd3ca9247bc32d28bfe4: exit 0, tree 9c0e5e98d4bd0e89ed75d0fb7d137b680a2a021a.
- tests/test-execution-policy.json is unchanged.
- Input waits: none changed.
- PCs: no PC-n rows. Every new witness listed in the Code report stays pending for method-scoped SourceLanding Mutation. The mutants here were local, are restored, and are not PC evidence.

## Advisory (does not hold the land)

- A-3 (efficiency): for each housekeeping boundary it visits, PhantomTransparentFloor walks every older TurnEnd in the session, back to session start rather than to the first eligible prompt. Each step is NewestRealBetween + NextRealPromptSequence + a tool scan, so the cost is O(B·(R+T)) in memory. That is harmless at today's sizes. A floor at the first eligible prompt's sequence would bound it.
