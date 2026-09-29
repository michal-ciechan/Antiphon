# CARD-0418 round 19: completion matrix and channel routing

This continues the [round-18 ledger](2026-09-28-card-0418-round-18-evidence.md). The tested source tip is `0bf6a54b81b46b2c745a14c6fc11403cca6a4d5e`. No shared-stack restart, live destination, land, or Mutation positive control was attempted.

## New ordinary evidence

**V-1 closed for its local ordinary assertions.** `AgentTaskReplyIntegrationTests.completed_sources_reach_the_parent_as_exact_inline_or_zip_attachments` now executes 12 P/Q × Plan/Docs/Custom × four/six-source cases. Each case settles a dispatched task, types the completion note into the parent's fake terminal, and requires one transcript-confirmed `UserPrompt` carrying the exact queued body, task/root/digest identity, correct `N md`/`N md, sources zip` header, attach markers, source bytes, manifest path/count/length/SHA-256/complete flag, null PDF/error, and no PDF/render log. The parent has no channel binding. The six-source cases inspect every extracted ZIP member. `completion_receipt_respects_legacy_renderer_keys_and_source_kill_switch` adds actual parent receipts for legacy Enabled=true plus stale browser/timeout keys and for Enabled=false; the valid executable fake browser's invocation log stays absent. The enabled receipt carries exact source bytes and no converter task; the disabled receipt has no bundle or attach marker. Existing `DeliverableBundleServiceTests` supplies the Markdown-only Code worktree positive case, mixed Code/docs with no named document negative, failed/canceled negatives, and the six role/project source collection cases. These are named, executed native cases; they do not imply V-3's historical reply custody.

**V-4 closed for its local ordinary assertions.** `AgentTaskReplyIntegrationTests.actual_task_done_turn_dispatches_implied_sources_by_channel_policy` executes X/four, Y/four, Z/four and X/six in isolated PostgreSQL schemas. Actual task settlement creates the task-done note; the parent queue delivers it with a transcript-confirmed verdict. The parent's reply prose has no attach markers. The real `ChannelReplyDispatcher` discovers the implied bundle: Y/Z publish four byte-exact Markdown sources directly, with no PDF or conversion intent. X remains Deferred, with null correlation/source-delivery stamps and no producer call, until the normal conversion task linked to its intent settles and the pump publishes the original sources plus its PDF; both stamps then appear. The duplicate `shared.md` basenames remain distinct. X/six proves the generated complete-source ZIP expands into six manifest-listed Markdown files in the worker request, byte for byte. `actual_completion_routes_source_bytes_directly_or_through_conversion` independently checks the same facade's direct/deferred source bytes, source hash and PDF outcomes after a real completion receipt. This is isolated fake-producer publication evidence, not provider receipt.

The committed-slice CP-2 runs, in source order, passed **357/357** (`64986f8a9d388507dceafc6585ca0ff6f5ae8957`, `20260929-000708-d64f`), **361/361** (`decf81980772ad1a2f6fda6d2845c07965283222`, `20260929-001631-f706`), **365/365** (`b386495616fcda174153c4f9278262fc7bbb2a61`, `20260929-002703-043a`), and **367/367** (`0bf6a54b81b46b2c745a14c6fc11403cca6a4d5e`, `20260929-003623-0207`), zero skips. All CP-2 rows held a granted host build slot. The first checkpoint-tool bootstrap used a separately leased `dotnet run` build and emitted the existing `TaskOwnerGuard.cs` CS8602 warning; this is the sole unlisted build. Later checkpoint launches used the already-built tool with `--no-build`.

## Remaining ordinary done criterion

The round-18 open roster is reduced by **V-1 and V-4 only**. Still open: **V-3, V-5–V-18, V-21–V-23, R-1–R-14**. The exact missing assertions for those IDs remain in the [round-17 table](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions), excluding the V-1/V-4 rows. R-1 still needs V-21's structural contract; R-2 still needs V-3/V-16. R-14 needs a per-sub-assertion actual-run index and a validator that refuses broad-ID tokens. Previously closed local V-2, V-19, V-20 and V-24 stay closed. **V-25** needs an authorized live destination receipt, and **PC-1–PC-30** remain for method-scoped SourceLanding Mutation. No checkpoint green substitutes for those gates.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the plan's complete closed list on the committed source tip in `.antiphon/checkpoints/20260929-004748-82ea/` (Debian 12, 27m01s). The native report, TRX, failed-row detail and host/slot records are there. Every row held a granted host slot; the tool ran no unlisted row command. Its exit was **1: 11 green, 2 red**. CP-10's exact row was then rerun alone on the same commit in `.antiphon/checkpoints/20260929-011502-af39/` and passed **1/1** with no code change; the first run's Docker process had exceeded its 20-second startup limit under concurrent sweep load. CP-8's two `codex_desktop_unqualified` failures are the identical inherited `PinnedAgentKindTests.T1`/`T2` pair from rounds 17–18; they were not chased in this branch.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3484 | 3484 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 367 | 367 | 0 | 0 | Green, including the new V-1/V-4 cases |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux; Windows descendant evidence remains round 15 |
| CP-10 real browser | 1 | 0 → 1 | 1 → 0 | 0 | Full sweep Docker 20s timeout; exact same-commit rerun green 1/1 |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1` |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

The sweep verifies this slice's ordinary executed cases. It does not close the remaining V/R assertions, V-25, or any PC. Do not land from this evidence.
