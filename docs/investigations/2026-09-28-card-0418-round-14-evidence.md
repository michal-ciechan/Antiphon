# CARD-0418 round 14: real inbound V-24 and Final sweep

This continues the [round-13 ledger](2026-09-28-card-0418-round-13-evidence.md). Source for the passing isolated run and the complete checkpoint sweep is `d179d2f048837c2be9897cf8ab99dfbc52ce1767`. No live destination, shared stack, or land was used.

## V-24 isolated result

`ChannelOutboundIsolatedTests.Four_sources_convert_only_for_the_selected_conversation` passed **1/1** in `.antiphon/v24-r14-checkpoints/V24-r14d-20260928-180414-f31d/run.trx` (15m47s, granted host build slot). The unlisted E2E row is required for V-24: CP-1–CP-13 contain no E2E filter. `client/dist` was rebuilt before E2E. The fixture owns its PostgreSQL, SessionRunner, Redpanda, browser container, fake Grok processes, and fake Slack.

The fixture now creates P and Q as separate projects and repositories. P has source agent A, converter C, opted-in X and unbound Y; Q has agent B and unbound Z. Three normally dispatched Docs tasks each settle with four byte-checked Markdown files: P's source, a separate no-channel task, and Q's source. The no-channel task has no PDF or outbound intent. The source task's worktree is removed before X sends. The fake model's channel answer references the settled bundle files; Q's standing agent uses Q's own answer file. This is a fake-model E2E, not real-model behavior.

Real serialized `ChannelMessage` records enter the owned Kafka inbound topic. The hosted `ChannelBridgeService` accepts them, starts the bound standing agent, enqueues and types a marked prompt, and leaves a complete `UserPrompt` plus `TurnEnd` in the transcript. The E2E helper waits for that receipt and the production dispatcher's durable outcome; it no longer seeds queue/transcript rows or invokes the dispatcher directly. X's T1 turn creates one deferred conversion delivery. A second real X inbound at T2 answers `NO_REPLY`: its own correlation settles, X's T1 correlation stays open, and no second delivery is created. Before conversion output, no broker record or fake Slack message exists. After the owned-host restart and tool-gate release, the standalone tool creates a readable PDF; the broker/gateway/fake Slack carry four exact source uploads plus the PDF on T1, with no root post or duplicate. The PDF independently extracts all four headings and middle/end sentinels. Y publishes four unchanged originals directly. Z publishes four unchanged Q originals directly, and its provider text names Q's answer. Neither creates another converter delivery. A second X inbound uses a browser wrapper that exits 42; a separate real worker attempt publishes only the four originals with `ConversionOutcome=Fallback` and the degraded note. Correlations stamp after publication.

The fake worker was extended to accept Markdown `attachments` when the frozen request has no `sourceFiles`; the prior synthetic queue fixture always supplied `sourceFiles`. This matches the real direct-attachment request shape without changing production validation. The first round-14 attempt was stopped after the owned broker accepted X but the WebApplicationFactory registration order left no bridge consumer group; it wrote no TRX. The fixture explicitly registers the hosted bridge now. The next attempt ran **0/1** in `.antiphon/v24-r14-checkpoints/V24-r14b-20260928-173444-d62c/run.trx` because the fake worker refused the valid `attachments`-only request; that was a fixture gap. The corrected P/Q/X/Y/Z run passed **1/1** at `.antiphon/v24-r14-checkpoints/V24-r14c-20260928-174616-afe6/run.trx` before the additional real T2/Q-provenance assertions were added. The final `r14d` run above passed with those assertions. These attempts did not exercise a live model or destination.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the plan's closed list once on committed `d179d2f0` in `.antiphon/checkpoints/20260928-182118-aebc/`. The native `report.md`, TRX, commands, CP-8 failures and host/slot records are there. All build and test drivers held granted slots. Outside the closed list, the V-24 E2E attempts and their builds, the prerequisite client bundle rebuild, and the checkpoint-tool bootstrap build were justified above; client dependencies were installed with `npm ci`. The tool itself ran no unlisted row command.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3483 | 3483 | 0 | 33 platform skips | Green |
| CP-2 source settlement | 340 | 340 | 0 | 0 | Green |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals, identical to rounds 11–13. |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green |
| CP-12 client | 26 | 26 | 0 | 0 | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall: twelve green rows, one red inherited row, tool exit 1. CP-8's two failures are not a branch regression, but CP-8 is not green. The Windows fake-browser descendant tests `MarkdownPdfRendererTests.A_real_browser_timeout_stops_its_child_process` and `Caller_cancellation_stops_the_real_browser_process_tree` still need a Windows-pinned CP-9 run on this pushed source; Linux cannot establish Windows process-tree termination.

## Remaining gates

V-24's isolated inbound path is now exercised end to end. The broader ordinary V/R matrix remains open as listed in the [round-8 table](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope) and [round-13 ledger](2026-09-28-card-0418-round-13-evidence.md#remaining-ordinary-gates): full source-provenance/read-path, completion-note, send-shape/control, worker-purpose/refusal/deadline, file/ZIP/fault, complete routing/crash/multi-target/TTL/policy-race, monitor and near-default wire oracles still need named evidence. R-14's evidence-index/validator invocation is not complete. V-25 is the later authorized live destination/model/receipt gate. PC-1–PC-30 remain pending method-scoped SourceLanding Mutation; neither the isolated fake Slack receipt nor this sweep discharges them. No land or live message was performed.
