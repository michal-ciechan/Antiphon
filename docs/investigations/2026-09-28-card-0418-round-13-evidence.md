# CARD-0418 round 13: isolated dispatcher route and fallback

This continues the [round-12 ledger](2026-09-28-card-0418-round-12-evidence.md). Product/test source for this sweep is `b833dc33b718b156a9c657c466b7f77892f7feee`. This is a Final-profile sweep of the plan's closed checkpoint list, but it is not completion of the ordinary V/R matrix, V-25, or any positive control.

## V-24 isolated result

`ChannelOutboundIsolatedTests.Four_sources_convert_only_for_the_selected_conversation` ran with the three isolated opt-ins on Linux against its owned PostgreSQL, SessionRunner, Redpanda, browser container, real server graph, FakeGrok worker and fake Slack. It passed **1/1** in `.antiphon/v24-checkpoints/V24-20260928-152617-fad0/run.trx` (9m01s, granted build slot). The unlisted E2E row is justified by V-24: CP-1–CP-13 contain no E2E selection.

The fixture now takes a real settled four-source task, seeds a marked inbound correlation and transcript turn, and invokes the production `ChannelReplyDispatcher` instead of calling `ChannelOutboundService` directly. The X reply defers with four exact Markdown source bytes, sends no broker record before conversion output, then survives owned-host restart and source-worktree removal. Its catalog handle changes to T2 while the frozen inbound envelope still names T1. Real FakeGrok invokes the standalone PDF tool; the producer, broker, gateway and fake Slack carry four byte-identical Markdown uploads and the PDF on T1. The PDF is independently extracted for all four headings plus middle/end sentinels. Publication stamps the X correlation. An unbound Y turn with the same four source files publishes directly, with four byte-identical uploads on Y's thread and no additional conversion intent. A second X turn uses a browser wrapper that exits 42: a separate real worker attempt publishes only the four originals on its inbound thread, with `ConversionOutcome=Fallback` and the degraded note. Both additional correlations stamp only after publication.

The first unlisted E2E attempt was **0/1** in `.antiphon/v24-checkpoints/V24-20260928-152143-9848/run.trx`: the fixture used a hyphenated GUID in its synthetic channel marker, so the real dispatcher correctly returned `NotPublished`. The corrected N-format marker made the same row green. This red is a fixture mistake, not a production failure. A leased E2E compile succeeded. Two direct PowerShell `dotnet run`/`dotnet exec` attempts stalled in the shell's wildcard expansion before starting a child; their owned wrappers were stopped. The `run-checkpoint.ps1` E2E row uses `ProcessStartInfo.ArgumentList` and completed. The checkpoint tool bootstrap build was the other unlisted build; it succeeded with the existing CS8602 warning in `TaskOwnerGuard.cs`. A `dotnet run --no-build` tool launcher failed because the tool was built with `UseAppHost=false`; direct `dotnet <tool.dll>` ran the same checkpoint app. These attempts did not add product test executions.

This **narrows but does not close V-24**. The X/Y/fallback turns use fixture-seeded queue and transcript rows after the normal source task settles; they do not exercise an inbound gateway message being typed to the source agent. Q/Z and a separate no-channel source task are not present. The failing-tool control reuses the same four-source task. An E2E that drives those remaining paths is still required before claiming the full V-24 oracle. The fake Slack receipt is isolated evidence only; V-25 requires the separately authorized real destination and receipt.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the complete plan list against the committed source in `20260928-153628-7860`. Its native report, TRX, commands, and CP-8 failures are under `.antiphon/checkpoints/20260928-153628-7860/`. All listed builds held granted slots; the tool reported no unlisted row command.

| Row | Executed | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3483 | 3483 | 0 | 33 platform skips | Green |
| CP-2 source settlement | 340 | 340 | 0 | 0 | Green |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals, identical to round 11/12. |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green |
| CP-12 client | 26 | 26 | 0 | 0 | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall exit 1: twelve green rows and CP-8's two inherited failures. CP-1's 33 skips and Linux CP-9 do not establish Windows browser descendant termination.

## Remaining ordinary gates

The [round-8 open-case table](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope) and [round-10 narrowing](2026-09-28-card-0418-round-10-evidence.md#remaining-gates) still apply except for the specific round-11 through round-13 assertions. Full P/Q four-source X/Y/Z and no-channel settlement, source provenance through all read paths, all main/trailing/machine and withheld/control caller cases, runtime release, worker-purpose/refusal/capacity/deadline companions, file/ZIP and serialized fallback faults, complete T1/T2/C-1–C-8/multi-target/attention/TTL/policy-race oracles, monitor and near-default wire publication, and an R-14 evidence-index/validator invocation still need named complete evidence. V-21 and V-22 also remain wider than the individual fresh-host warning and wire cases run here.

Run `MarkdownPdfRendererTests.A_real_browser_timeout_stops_its_child_process` and `Caller_cancellation_stops_the_real_browser_process_tree` on a Windows host through CP-9 on this committed source. Those tests launch a Windows fake-browser executable and descendant and assert both identities stop after timeout/cancellation; Linux cannot prove that path. Use the plan's CP-9 row with the checkpoint tool in a Windows-pinned dispatch.

V-25 remains the later authorized live destination/model/receipt gate. PC-1–PC-30 remain pending method-scoped SourceLanding Mutation; no checkpoint or nightly green discharges one. This branch was not landed, no shared stack was restarted, and no live destination received a message.
