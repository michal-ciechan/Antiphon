# CARD-0574 — First Codex/Astra dispatch after unhold wedged twice at boot-prompt delivery

**Status:** root cause confirmed from live session snapshots, transcript sidecars, pty-host logs, and the shipped BootWedged path. Not reproduced by launching another Codex child (would spend quota). Investigate task: `6ab3c5ac`. Card: CARD-0574.

**Date:** 2026-09-19. **Incident task:** `1925f7be` (CARD-0567 Plan, Codex Frontier / gpt-6-astra).
**Sessions:** first `e1f1ddf9-7582-41cf-9374-dcd4d8a6a5a9`; relaunch `bf8a3565-1bbd-4bff-83c7-baf27578efdc` (the id on the card).

## Verdict

The harness did what CARD-0299 S2 shipped: cold Codex first-delivery `NoSubmitOutput` → `BootWedged` → kill → one relaunch → fail at the limit. The 39 s Failed row with zero tokens is that backstop, not a new detector bug.

What it caught is the CARD-0133/0299 **not-ready TUI** class, not CARD-0562 and not a stale Codex home. Both cold starts after the three-day kind hold typed the spilled brief before Codex would accept a submit:

| Attempt | Last frame | Brief | Transcript | Outcome |
|---|---|---|---|---|
| 1 `e1f1ddf9` | banner `model: loading`; empty composer `Ask Codex to do anything` | **Queued follow-up inputs** | unbound `missing`; `transcriptPath: null` | BootWedged, killed at 10:49:31Z |
| 2 `bf8a3565` | `model: gpt-6-astra xhigh`; `Starting MCP servers (1/3): cua_repl, node_repl (2s)` | **in composer**, `tab to queue message` | unbound `missing`; `transcriptPath: null` | BootWedged at limit, Failed 10:50:11Z |

No `Working (` on either ANSI log. No rollout under `CODEX_HOME/sessions/2026/09/19` (directory does not exist). Auth was live (usage-reset banner, model name on attempt 2). `grokRulesReceipt` is null.

| Ask | Answer |
|---|---|
| Same family as CARD-0562 (Grok rules-ack case)? | **No.** Different CLI and different delivery path. CARD-0562 is `GrokRulesRefreshService.Judge` matching `ANTIPHON_RULES_ACK`. Codex standing instructions are `-c developer_instructions=` at launch (`docs/agent-kinds.md`). The runner snapshot for `bf8a3565` has `grokRulesReceipt: null`. The screens show the **task brief**, not a rules ACK prompt. |
| Stale/incompatible Codex session dir from the hold? | **No.** Both launches are `resumeLaunch: false`. Sidecars never bind a rollout. No 2026-09-19 session files were created — Codex still creates the JSONL on first submit (`docs/agent-kinds.md` Transcript). |
| Expired/rotated auth during the hold? | **No.** `auth.json` exists (last write 2026-09-16 20:50 local, during the hold, not missing). Both last frames show `You have 2 usage limit resets available`. Attempt 2 loaded `gpt-6-astra xhigh`. This is not `ProviderSignInRequired`. |
| CLI version mismatch? | Installed `@openai/codex` **0.153.4**. `~/.codex/version.json` already has `latest_version`/`dismissed_version` `0.154.0` from **2026-09-16T00:13:30Z**. The update banner is on these screens **and** on the 2026-09-16 06:49Z **successful** Codex Merge `5e68cf83`. Not the discriminator. |
| One-off vs will recur? | **Not a one-off.** 2/2 cold starts of this dispatch failed the same way (no submit, no transcript). The ready gate still returns after 1 s quiet + trust-only, and `CodexMcpBoot.WaitUntilAbsentAsync` **returns immediately if the MCP line is not yet painted**. After a multi-day idle that is enough to type into `model: loading` or MCP `(1/3)`. Historical CARD-0299 rate was 3/55 (5.5 %) with later same-day successes, so a *later* warm dispatch today might still work — but the next **cold** Codex dispatch can hit the same hole until the ready gate waits for a submittable composer. |

## Timeline (UTC)

Europe/London = UTC+1. Local last-write on the ANSI files is +1 h.

| UTC | What | Source |
|---|---|---|
| 09-16 ~06:49–10:49 | Last Codex tasks before the hold (Succeeded / then Canceled). CLI already 0.153.4; update banner already in ANSI. | `GET /api/agent-tasks` agentKind=Codex since 09-16; `5e68cf83….ansi.log` |
| 09-16 (manual) | Kind-wide Codex hold ("out of credits"). | CARD-0574 description |
| 09-16 20:50 local | `auth.json` last write (token store touched during the hold). | `C:\Users\lndco\.codex\auth.json` mtime |
| 09-19 10:48:27 | Task `1925f7be` created. Worker/Plan Frontier on Codex, pin gpt-6-astra, bound CARD-0567. | task events |
| 09-19 10:48:31 | Dispatched; worktree `C:\Antiphon\worktrees\card-task-1925f7be`. | task events |
| 10:48:47.605 | Pty-host start session `e1f1ddf9`, host pid 34796. | `pty-hosts/logs/e1f1ddf9….log` |
| 10:48:48.641 | Launched `C:\Program Files\nodejs\node.exe` child pid 47704, ModernConPty 1.24.260710001. | same |
| 10:48:54.782 | `firstInputAtUtc` (brief typed, +6.1 s after child start). | `transcripts/e1f1ddf9….json` |
| 10:49:31.391 | Child `KilledByRequest` (BootWedged relaunch). Last frame still `model: loading`, brief in **Queued follow-up**. | pty-host; runner snapshot |
| 10:49:31 | Task event `boot-wedge relaunch 1/1`. `DispatchedAt` restamped. New session `bf8a3565`. | task events; `AgentTaskDispatcher.RelaunchWedgedAsync` |
| 10:49:32.869 | Pty-host start `bf8a3565`, host pid 24136. | `pty-hosts/logs/bf8a3565….log` |
| 10:49:33.407 | Launched node.exe child pid 48904. | same |
| 10:49:35.549 | `firstInputAtUtc` (**+2.14 s** after child start). | `transcripts/bf8a3565….json` |
| 10:50:11.108 | Child `KilledByRequest`. Last frame MCP `(1/3)` still visible, brief still in composer. | pty-host; `GET :17204/sessions/bf8a3565/snapshot` |
| 10:50:11.172 | Task Failed: `boot prompt could not be delivered; TUI stopped reading after the brief rendered, twice; relaunched once and wedged again.` Tokens 0/0. | `AgentTaskDispatcher.BootWedgeFailedReason`; task row |

Server file log `C:\src\Antiphon\server\logs\antiphon-20260919.log` last write is 10:26:55 (before this dispatch; post-restart lines are not in that file). Verdict does not depend on them: snapshots, sidecars, pty-host and the Failed reason are enough.

## What the screens showed

Pulled 2026-09-19 ~12:00 local from the still-listed Exited runner sessions (`status=Exited`, `exit=1/KilledByRequest`, `transcriptBound=false`, `unbound=missing`). `GET /api/sessions/{id}/transcript` is `{ entries: [], lastSequence: 0 }` for both.

### Attempt 1 — `e1f1ddf9` rendered last frame

```
Update available! 0.153.4 -> 0.154.0
Run npm install -g @openai/codex to update.

OpenAI Codex (v0.153.4)
model:       loading   /model to change
directory:   C:\Antiphon\worktrees\card-task-1925f7be
permissions: YOLO mode

You have 2 usage limit resets available. Run /usage to use one.

Queued follow-up inputs
  ↳ [antiphon-task:1925f7be] role=Plan … 'C:\src\Antiphon\.antiphon\task-1925f7be-brief.md' …
    alt + ↑ edit last queued message

> Ask Codex to do anything
  gpt-6-astra default · C:\Antiphon\worktrees\card-task-1925f7be
```

No MCP line. No trust dialog (`Do you trust` count 0 in the ANSI). Composer empty; the brief is queued, not submitted. Banner still `loading` ~43 s after spawn.

### Attempt 2 — `bf8a3565` rendered last frame

```
Run npm install -g @openai/codex to update.

OpenAI Codex (v0.153.4)
model:       gpt-6-astra xhigh   /model to change
directory:   C:\Antiphon\worktrees\card-task-1925f7be
permissions: YOLO mode

Tip: New Use /fast to enable our fastest inference …

You have 2 usage limit resets available. Run /usage to use one.

Starting MCP servers (1/3): cua_repl, node_repl (2s  esc to interrupt)

> [antiphon-task:1925f7be] role=Plan … task-1925f7be-brief.md … [antiphon-task:1925f7be]
  tab to queue message                                                                               100% context left
```

MCP spinner is still painting at kill (ANSI tail is the `(1/3): cua_repl, node_repl` line rewriting). Unique vs CARD-0299's successful same-day controls: last frame still has MCP **and** `tab to queue message`. Same last-frame pair as CARD-0299's fail (`docs/investigations/2026-09-01-card-0299-codex-plan-unsubmitted.md`).

Stripped ANSI tails: `C:\logs\antiphon\investigations\card-0574\first-tail.txt`, `relaunch-tail.txt`.

## Mechanism

### Delivery path (not Grok rules, not `SendPromptAsync`)

`AgentTaskDispatcher` launches the Codex child and enqueues the spilled brief as `WhenIdle` / `QueuedMessageOrigin.Delegation`. The queue types it; `RunnerCodexAdapter.SendPromptAsync` / `CodexSubmitConfirmation` is not this path (CARD-0299). Queue messages on both corpses are empty (`GET /api/sessions/{id}/messages` → `messages: []`) because BootWedged **cancels** the row (`SessionMessageQueueService.TryHandleBootWedgeAsync`).

`disable_paste_burst=true` is still a launch `-c` (`CodexLaunchArgs.DisablePasteBurst`; `CodexDelegateDispatchTests`). Pty-host launched `node.exe` (CARD-0497), not `codex.cmd`.

### Ready returns before the TUI can submit

`RunnerCodexAdapter.WaitForReadyAsync` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:139-157`):

1. Quiet after visible, 1000 ms (`CodexReadyQuietPeriodMs`), observing only `AcceptTrustPromptIfVisibleAsync` (Enter on the trust dialog; **not** the update banner, **not** `model: loading`).
2. Then `CodexMcpBoot.WaitUntilAbsentAsync` with `CodexBootStatusMaxWaitMs` 10_000.

`CodexMcpBoot.WaitUntilAbsentAsync` (`src/Antiphon.Agents.Pty/CodexDetectors.cs:68-80`): if the **first** snapshot does not contain `Starting MCP server` / `Booting MCP server`, it **returns immediately**. A 1 Hz MCP line that paints *after* that snapshot is not waited. Bound expiry logs a warning and types anyway; never fails ready.

Attempt 1 typed at +6.1 s into `model: loading` — MCP had not appeared, so step 2 was a no-op. `followUpQueueMode = "queue"` is set in `~/.codex/config.toml` `[desktop]`; the last frame is exactly that: brief queued, composer empty.

Attempt 2 typed at **+2.14 s**. CARD-0195 measured header at 0.52–1.68 s and MCP visible up to 3.34 s. +2.14 s is inside that window: step 2 can return because the MCP line is not on the first snapshot yet, then MCP paints, then the queue types. CARD-0195 / CARD-0299 S3: typing during the MCP line is MCP-interrupt / queued-input. Last frame: brief still in the composer, MCP `(1/3)` frozen at 2 s, zero further `Working`.

`node_repl` in `config.toml` has `startup_timeout_sec = 120`. Antiphon's MCP wait cap is 10 s and is skipped entirely when the line is not yet visible.

### BootWedged recovery matched the shipped sentence

`TryHandleBootWedgeAsync` (`SessionMessageQueueService.cs:4096-4177`): Codex + Running + Delegation origin + attempts 1 + null baseline + Dispatched task + `NoSubmitOutput` → incident 44, cancel queue rows, kill regardless of AlwaysOn, relaunch once (`BootWedgeRelaunchLimit` 1). Second wedge calls `FailWedgedAtLimitAsync` with `BootWedgeFailedReason` (`AgentTaskDispatcher.cs:3856-3858`) — byte-identical to the task's `failureReason`.

The ephemeral agent `70d08372` is already gone (GET 404), so `GET /api/agents/{id}/incidents` cannot be re-read; the Warning event `boot-wedge relaunch 1/1` and the Failed reason are the durable stand-ins.

## What was ruled out

- **CARD-0562 / standing-rules ACK.** Grok-only `ANTIPHON_RULES_ACK` line in `GrokRulesRefreshService`. Codex does not run that handshake. `grokRulesReceipt: null`. Screens contain the CARD-0567 Plan pointer, not a rules file.
- **Stale cached Codex session / resume of a pre-hold conversation.** `resumeLaunch: false`; no 09-19 rollouts; sidecars `transcriptPath: null` for the whole life.
- **Expired auth / out-of-credits wall still in force.** Usage-reset banner present; attempt 2 selected `gpt-6-astra xhigh`; YOLO; not the sign-in detector screen.
- **Update-available modal as the blocker.** Banner is on these frames **and** on 16 Sep success `5e68cf83` (`Update available! 0.153.4 -> 0.154.0`). `version.json` already dismissed `0.154.0` on 09-16. No `Press enter to continue` in either ANSI (the headed canary's modal shape). Production still has no skip-handler (`CxSession.WaitForComposerAsync` types `2` then Enter; `AcceptTrustPromptIfVisibleAsync` does not). That gap is real but not what distinguished fail from the last pre-hold success.
- **Trust dialog.** Zero `Do you trust` in both ANSI logs. YOLO on screen. Worktree cwd is new; if a trust dialog fired, it was cleared (or never painted) before the last frame.
- **`/usage` redemption.** Forbidden in `ProviderContractCatalog`. The text is a banner (`Run /usage to use one`), not the numbered redeem picker. Not typed.
- **PasteBurst as the remaining primary.** `-c disable_paste_burst=true` still ships. CARD-0299 already measured residual wedges with that flag on.
- **Wrong process / `cx.ps1`.** Pty-host: `Launched C:\Program Files\nodejs\node.exe`.

## Recurrence

Fleet Codex since 2026-09-16: 18 list rows; `1925f7be` is the **only** Codex task after the hold. There is no later control dispatch.

CARD-0299's census: 3/55 Codex sessions (5.5 %) matched 0 transcript rows after `disable_paste_burst`; four later Codex Plans that same day succeeded. This incident is 2/2 cold starts after three idle days, with MCP stuck at `(1/3): cua_repl, node_repl` on the relaunch. 16 Sep success `5e68cf83` did paint `Starting MCP servers (2/3): codex_apps` — MCP *progressed*. Here it did not.

Until ready withholds typing while `model: loading` is on the banner **or** MCP boot is present/about to paint, a cold Codex dispatch can wedge again and will Fail in ~40 s (BootWedged limit 1), not 10 minutes. A later same-day dispatch *might* succeed if model/MCP caches are warm (`models_cache.json` was rewritten during this window). That would not make this a flake: both attempts of the first post-unhold dispatch never created a UserPrompt.

## Artifacts

- ANSI: `C:\logs\antiphon\session-runner\e1f1ddf9758241cf9374dcd4d8a6a5a9.ansi.log` (148,258 B), `bf8a35651bbd4bff83c7baf27578efdc.ansi.log` (175,335 B)
- Sidecars: `C:\logs\antiphon\session-runner\transcripts\e1f1ddf9….json`, `bf8a3565….json`
- Pty-host: `C:\logs\antiphon\session-runner\pty-hosts\logs\e1f1ddf9….log`, `bf8a3565….log`
- Stripped tails: `C:\logs\antiphon\investigations\card-0574\first-tail.txt`, `relaunch-tail.txt`
- Task: `GET /api/agent-tasks/1925f7be-79c1-41b8-84fa-a88e67aeb874` (`failureReason` = `BootWedgeFailedReason`)
- Live server SHA at investigation time: `1044eef74db59426d69356a2d0d9ec03d2b9a967` (`GET /api/version`, `land-v2`)

## Uncertainties

- Post-restart server/session-runner file logs do not contain 10:48–10:50Z, so the "MCP boot line still visible after 10000ms; typing anyway" warning cannot be confirmed vs the first-snapshot early return. The +2.14 s first-input on attempt 2 fits the early-return hole better than a 10 s expiry.
- `AgentIncidents` for BootWedged (44) cascaded with the ephemeral agent; not re-read. The relaunch event and Failed reason remain.
- Whether a third Codex dispatch *today* would submit is unmeasured (not launched).
- Whether `cua_repl` (computer-use) is slower after idle than `codex_apps` was on 09-16 is suggested by the `(1/3)` vs `(2/3)` last-MCP lines, not proven as a hang inside that binary.

## Not done, noted

Ready must not report true while the Codex banner still says `model: loading` or MCP boot is present/not-yet-painted; do not treat a wider 10 s cap as the fix.
