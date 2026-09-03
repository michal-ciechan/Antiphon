# CARD-0226 — HerdrAlwaysOnChannelParityTests CARD-0211+0225 (+0224) asserts verified

**Date:** 2026-09-03
**Status:** confirmation only — no code change, no execute follow-up
**Card:** CARD-0226 (`3f093407-6fca-4a51-abfa-a9c4f9c7aac0`)
**Tree:** `1697420a` (master at plan time)

The card was parked because this class hung (near-zero CPU, force-kill) every time CARD-0211+0225 and CARD-0224 S1 tried to run it. Those hangs were CARD-0222 (frozen `MutableTimeProvider` deadlocking `SessionMessageQueueService`), fixed in `cf1029b`. CARD-0165 (the full-suite "silent stall") closed separately as not reproduced. This pass re-runs the class now that both blockers are Done.

## Verdict

**Close CARD-0226 as verified.** The class completes; the naming/title asserts and the pane-reuse assert all pass. Nothing further to build.

## Run

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0226/ -- --treenode-filter "/*/Antiphon.Tests.Application/HerdrAlwaysOnChannelParityTests/*" --output Detailed --no-progress --no-ansi
```

| | |
|---|---|
| Result | **5/5 passed, 0 failed, 0 skipped** |
| Test duration | 22.728 s |
| Wall (build + run) | 104 s |
| Hang | none |

| Test | Time |
|---|---|
| `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr)` | 5.782 s |
| `AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost)` | 899 ms |
| `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)` | 408 ms |
| `Herdr_launch_definition_starts_adopts_and_exits(Grok)` | 367 ms |
| `Herdr_launch_definition_starts_adopts_and_exits(Codex)` | 499 ms |

CARD-0222's closer already had 5/5 in 27 s / 28 s after the clock fix. This is a second independent run on current master, after CARD-0165 closed and after CARD-0323 changed first-launch pane allocation.

## Asserts that passed (as they stand on this tree)

Plan §6 of `docs/superpowers/plans/2026-08-28-card-0211-0225-herdr-agent-name-and-pane-title-plan.md` named three checks on `Herdr_launch_definition_starts_adopts_and_exits`. They still exist; CARD-0323 retargeted the tab label from `tab.create` to `tab.rename` because first launch now uses `workspace.create`'s root pane.

In `HerdrAlwaysOnChannelParityTests.cs` (~332–347), for each of ClaudeCode / Grok / Codex:

1. **Tab label is `agent.Name`, not the TUI `DefinitionName`.** `tab.create` is asserted *absent* (`CARD-0323: first launch uses workspace.create's root pane`); `tab.rename` `label` == `agent.Name` and ≠ `definitionName`.
2. **`pane.rename` `label` == `agent.Name`** (and ≠ `definitionName`).
3. **Exactly one `agent.rename`:** `target` == the launched pane id; `name` == `agent.Slug` (already the sanitised form for these names; the runner still runs `SanitizeAgentName`).

CARD-0226 also gated on CARD-0224 S1's flipped resume check, in `AlwaysOn_channel_bound_survives_child_death_and_replies(Herdr)` (~169–171):

- resumed pane id == first pane id (`CARD-0224: resume reuses the standing pane`)
- `CountAgentPanes` unchanged (`resume reuses the pane; no new tab is allocated`)

Both of those passed on the Herdr arm.

The harness clock is the CARD-0222 offset-over-real-clock `MutableTimeProvider` (`GetUtcNow() => DateTimeOffset.UtcNow + _offset`). That is why this class no longer wedges in `SettlePostEvidenceAsync`.

## Not proposed

No execute slice. The CARD-0211+0225 wiring (`PaneTitleFor` + `AgentSlug`) and the CARD-0224 pane-reuse path are already on master; this card only needed a live run through `AgentSessionService` → DB → `DirectSessionRunnerClient` → `FakeHerdrServer`. That run is now green.

Isolated `bin-c0226/` outputs were deleted after the run.
