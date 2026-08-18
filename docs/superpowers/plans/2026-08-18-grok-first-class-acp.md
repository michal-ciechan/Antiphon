# Grok first-class follow-up: structured signals over PTY heuristics

**Date:** 2026-08-18
**Status:** planned (task 5fb42616; follows `2026-08-17-grok-tui.md` / commit `5754e02`)

## Verdict

Grok Build TUI is **ACP-native**, verified against the real binary — not inferred from the
plan doc's phrase. The right "first class" plan is NOT porting the Claude heuristic family
(compaction/interrupt predicates, trust auto-answer, remote-control). It is: tail Grok's
**structured ACP transcript** (`updates.jsonl`) the way we tail Claude's JSONL, which gives
Grok explicit turn ends, real reply text, and transcript-confirmed delivery — and pilot a
full **ACP stdio adapter** as the strategic escape from PTY scraping for delegate sessions.

## Measured facts (grok.exe 1.0.4, 2026-08-18, this machine)

1. **`grok agent stdio` speaks real ACP JSON-RPC.** An `initialize` (protocolVersion 1)
   probe answered with full `agentCapabilities`: `loadSession: true`,
   `sessionCapabilities: {list, resume, close}`, promptCapabilities, authMethods
   (`cached_token` from `~/.grok/auth.json`), and complete `modelState` — model ids, names,
   context sizes (500k), reasoning-effort menus. Structured model discovery for free,
   versus `GrokModelListParser` scraping `grok models` prose.
2. **`--output-format streaming-json`** self-describes as "NDJSON of the agent native ACP
   session updates". `grok agent` also offers `serve` (WebSocket), `headless` (relay), and
   `leader` (shared backend on `~/.grok/leader.sock`, multiple clients).
3. **The TUI persists the ACP update stream live** to
   `~/.grok/sessions/<url-enc-cwd>/<session-id>/updates.jsonl`: `session/update`
   notifications — `user_message_chunk`, `agent_message_chunk`, `agent_thought_chunk`,
   `tool_call`, `tool_call_update`, `plan`, `task_backgrounded`/`task_completed`,
   `session_recap` (auto-compaction analog) — plus `_x.ai/session/update` with
   **`turn_completed` carrying `stop_reason` and full usage/cost**. The session that built
   `5754e02` shows 3 user messages ↔ 3 `turn_completed`: an explicit, structured turn end,
   the exact signal the entire Claude working/idle machinery approximates from text markers.
4. **The transcript path is deterministic.** We pass `--session-id`, so the file's location
   is known before launch. The CARD-0006 hazard class (discovery binding a stranger's
   conversation) does not exist for Grok — no claim registry heuristics, no C1–C4 evidence
   rules, no candidate probing.
5. **Today's Grok integration has zero structured signal.** `TranscriptEnabled` is
   Claude-only (`SessionRunnerHttpClient.cs:52`). `RunnerGrokAdapter` detects turn-complete
   by quiet time + a `" for \d+s"` regex + an idle-title OSC, and extracts replies via
   `CodexResponseAnalyzer` screen-scraping. Consequences: CARD-0055 delivery verification
   permanently degrades to the screen-only verdict it exists to forbid; working/idle,
   turn-end queue flush, and channel reply dispatch have no transcript rows to run on. A
   channel-bound Grok agent today has none of the safety the CARD-0027/48/52/55/67 line
   built — this, not feature parity, is the gap that matters.

## The deferred list, judged

| Deferred item | Verdict |
|---|---|
| Claude JSONL tailing | **Replace, don't port**: build a Grok tailer on `updates.jsonl` (S2). The Claude tailer's hard parts (discovery, claims, fork-follow) aren't needed. |
| `/remote-control` | **Don't build.** It is a Claude-plugin mechanism; CARD-0056 already treats RC as optional monitoring. `updates.jsonl` (now) and ACP (later) subsume its value. |
| Trust-dialog auto-answer | **Probably nothing to build.** Grok auth is global (`~/.grok/auth.json`), not per-cwd; `--always-approve` covers permissions. S1 canary confirms whether a fresh-cwd/fresh-auth launch ever blocks on a modal; if only the login/welcome screen can block, that's a fail-fast + incident, not an auto-answer. |
| Compaction/interrupt working-idle rules | **Don't port.** With explicit `turn_completed`, the whole `TranscriptKinds` predicate family is unnecessary for Grok. One rule on `stop_reason` (measure the interrupt/Esc and recap shapes in S1). |
| Headed canaries | **Yes, and first.** fakegrok's PTY contract ("same as FakeClaude") and turn-end markers ("Crunched for 1s") are currently unverified assumptions — the exact gap CARD-0030/0037 canaries closed for Claude. |

## Slices

**S1 — Headed Grok canaries (S, ~1 day).** `GrokCanaryTests` (headed, `[Explicit]`, pattern
of `ClaudeSubmitConfirmCanaryTests`): composer submit contract on the modern backend
(paste, clip, swallowed-Enter), turn-end markers vs `turn_completed`, **`updates.jsonl`
write latency** (is it flushed per-update or buffered? Claude's 45 s flush stall — the
CARD "pull before kill" miss — must be measured, not assumed absent), Esc/interrupt shape,
`session_recap` shape, fresh-cwd + fresh-`GROK_HOME` launch (trust/welcome blocking).
Update fakegrok where measurements disagree. Risk: low; cost is a few real Grok turns.

**S2 — Grok structured transcript tailing (M, ~2–3 days).** The core slice.
- Runner: `GrokTranscriptTailer` — tail the known path (no discovery), normalize ACP
  updates → the existing `TranscriptKinds` strings: `user_message_chunk`→`UserPrompt`,
  `agent_message_chunk` coalesced per `promptId`→`AssistantText`,
  `turn_completed`→`TurnEnd` (`stop_reason` recorded; a cancelled stop is still a turn
  END). Publish through the same event pipeline; reuse the sidecar for restart re-tail.
- Server: `TranscriptEnabled: spec.Kind is ClaudeCode or Grok`; working/idle, turn-end
  queue flush, CARD-0055 transcript-confirmed delivery, and channel reply dispatch then
  work unchanged — they are format-agnostic once rows exist. `PromptSubmissionMatch`
  confirms against the `UserPrompt` rows.
- `RunnerGrokAdapter.WaitForTurnCompleteAsync` keeps the screen heuristic only as
  fallback; reply text comes from `AssistantText` rows, not `CodexResponseAnalyzer`.
- Pinned by: fakegrok gaining `updates.jsonl` emission (per S1 measurements), a Grok
  normalizer test suite, and a queue→PTY integration test proving transcript-confirmed
  delivery end to end.

**S3 — ACP stdio adapter prototype for delegates (L, ~1 week, decide after S2).**
`AcpAgentAdapter : IAgentProtocolAdapter` speaking ACP over `grok agent stdio`:
`session/new` → `session/prompt` → stream updates → response `stopReason` IS turn
complete. For **delegate/worker sessions only** — no composer, so the entire delivery
verification, ceilings, clipping, DA1, quiet-period apparatus (CARD-0027/28/30/37/48/52/55)
is structurally impossible to need. Open design points: restart survival via
`loadSession`/`session/resume` (state lives in Grok's session store — arguably better than
detached pty-hosts) or a detached acp-host; client rendering for screenless sessions
(render the update stream as a chat/log view); permission requests arrive as JSON-RPC
`session/request_permission` (moot under `--always-approve`).

**S4 — strategic direction (no code now).** ACP is the shape `IAgentProtocolAdapter`
wants to be. Recommended end-state: two adapter families — PTY for operator-facing
interactive sessions (the co-driving/remote-control product is the point of the TUI), ACP
for delegates. Grok pilots ACP because it is native; Claude (via its ACP bridge / Agent
SDK) and Codex (app-server) can follow if S3 proves out. Do NOT migrate Claude now — the
PTY machinery is load-bearing, pinned, and paid for.

## Order and effort

S1 → S2 ship together as "Grok is safe to bind to a channel" (~3–4 days total, low risk,
known shapes). S3 is a separate decision after S2 lands (~1 week, medium risk, high
strategic value). Remote-control, trust auto-answer, and the Claude predicate ports are
explicitly **not built** unless S1 measurements contradict the facts above.
