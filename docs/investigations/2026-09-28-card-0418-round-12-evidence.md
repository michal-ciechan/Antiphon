# CARD-0418 round 12: master integration and remaining route assertions

This continues the [round-11 ledger](2026-09-28-card-0418-round-11-evidence.md). The tested source commit is `cddaf2ccd38df49c082c4724fa561262fa6944fd`. This is a Final-profile checkpoint sweep, not completion of the ordinary V/R matrix, V-25 or the positive controls.

## Integrated source and new assertions

`a5e6b928` merges `origin/master` through `f259593a1` and includes CARD-0784's landed F-5 fixture and owned-host logging repair. The conflicting outbound service, pump and file-store files retained this branch's later admission, lease and byte-integrity guards; the master pipeline/schema changes were combined in `AppDbContext` and its snapshot. CARD-0784's fixture remains scoped to an admitted outbound reply. It does not prove the named V-24 automatic dispatcher routing, second conversation or failing-tool fallback. This round did not rerun that separate E2E fixture.

The merge exposed two identical legacy renderer warnings at server boot. `ChannelOutboundEndpointTests.Legacy_renderer_keys_warn_once_on_fresh_host_without_browser` boots the real guarded test host with obsolete browser/timeout keys and a nonexistent browser, asserts successful health, zero runner launches and exactly one warning in its owned host log. CP-3 failed on the committed test-only slice `8a532120` at 29/30 because it observed two warnings (`20260928-143136-bb1c`); after removing the duplicate warning, CP-3 passed 30/30 on `c5a724f7` (`20260928-143444-ef64`). This closes the one-warning boot assertion and narrows V-21/R-12. The already existing source settlement and structural tests provide the separate no-renderer checks; this is not a full V-21 verdict.

`ChannelOutboundDeliveryTests.Deferred_intent_freezes_route_and_only_publication_stamps_correlation` now accepts a plain trailing reply after a conversion is pending, while the policy's trigger is temporarily MarkdownSources. That reply is stored as a Ready passthrough intent, stays behind the frozen T1/T2 replies, and publishes on its own thread only after the head completes. A Markdown control notice publishes immediately while the head is pending and creates no intent. CP-5 passed 6/6 on `cddaf2cc` (`20260928-143809-1e20`). These are specific V-7/V-14/V-18 and R-4/R-10 ordering assertions, not the full named dispatcher, policy-race or control-caller matrices.

The checkpoint tool bootstrap build was the only unlisted build: it used `scripts/build-slot.ps1` because this worktree had no built checkpoint executable. It succeeded with one existing CS8602 warning in `TaskOwnerGuard.cs`. All checkpoint row builds had granted host slots; the tool reported no unlisted row commands.

## CP-1 through CP-13 Final-profile sweep

The checkpoint tool ran the plan's complete closed list against the committed source in `20260928-144120-87c5`. Native report, TRX, command logs and CP-8 failure details are under `.antiphon/checkpoints/20260928-144120-87c5/`.

| Row | Executed | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3483 | 3483 | 0 | 33 platform skips | Green |
| CP-2 source settlement | 340 | 340 | 0 | 0 | Green |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals, identical to round 11 and the earlier baseline. |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green |
| CP-12 client | 26 | 26 | 0 | 0 | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

The overall tool exit was 1: twelve green rows and CP-8 red. CP-1's 33 platform skips and the Linux CP-9 result do not establish Windows browser process-tree behavior.

## Remaining gates

The [round-8 open-case table](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope) and [round-10 narrowing](2026-09-28-card-0418-round-10-evidence.md#remaining-gates) remain the ordinary-case ledger, except for the specific round-11 and round-12 assertions above. In particular, full P/Q four-source X/Y/Z settlement and source provenance; all main/trailing/machine and real withheld/control caller rows; runtime release; worker-purpose/refusal/capacity/deadline companions; file/ZIP and serialized fallback faults; complete T1/T2, C-1–C-8, multi-target, attention/TTL and final policy-race oracles; full monitor and near-default wire publication paths; and an actual R-14 evidence index/validator invocation still need named complete evidence. CARD-0784 supplies part of F-5, but V-24 remains open at its own documented scope.

The browser process-tree fixture in `MarkdownPdfRendererTests.A_real_browser_timeout_stops_its_child_process` and `Caller_cancellation_stops_the_real_browser_process_tree` must run from this committed source on a Windows host, through CP-9 (`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-07-card-0418-channel-outbound-agent-plan.md --rows CP-9`). Its Windows path launches the fake browser executable and child, then asserts both process identities stop on timeout/cancellation. Linux execution cannot prove Windows process creation or descendant termination. The Linux CP-9 pass is retained only for its executed platform path.

V-25 remains the later authorized live destination/model/receipt gate. PC-1–PC-30 remain pending method-scoped SourceLanding Mutation; no checkpoint or nightly green discharges one. This branch was not landed, no shared stack was restarted, and no live destination was sent a message.
