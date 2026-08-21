# CARD-0111 — herdr as a visible+controllable terminal proxy: investigation

**Date:** 2026-08-21
**Status:** investigated (no implementation; a build pass would follow separately)
**Card:** CARD-0111 (`06128227-730d-4295-9151-77d9cff13920`) — investigate herdr as a terminal
proxy so sessions stay human-visible while Antiphon also drives them programmatically.
**Direction (user, decided, not re-litigated here):** the operator runs herdr themselves; Antiphon
is purely a CLIENT of an already-running instance; one herdr WORKSPACE per Antiphon PROJECT; within
it roughly a tab per agent, ideally a way to view up to 4 agents together.
**Evidence:** herdr's real repo and docs, read directly 2026-08-21 — `github.com/herdrdev/herdr`
@ `master` (metadata via GitHub API; `Cargo.toml`, `LICENSE` history, `src/ipc.rs`,
`src/app/api/agents.rs` read from source), the machine-readable API schema
`docs/next/api/herdr-api.schema.json` (91 methods, parsed locally), and the docs pages
`socket-api.mdx`, `windows-beta.mdx`, `concepts`, `agents`, `install`, `session-state` from the same
repo / herdr.dev. Every claim below cites which of these it came from. The card's own summary was
secondary-coverage-only and this document corrects it in three places (§7).

## Verdict

**Adopt — as an optional, opt-in session backend for visibility, never a replacement for the
pty-host stack.** herdr is real, current (v0.8.2, pushed 2026-08-20), far more mature than the card
assumed (31.2k stars, 76 contributors, Apache-2.0 since 2026-07-22 — not AGPL), runs natively on
Windows over the same app-local ConPTY runtime trick Antiphon shipped in CARD-0037, and its socket
API is fully bidirectional over a Windows named pipe: it can create workspaces/tabs/panes, send
text and keys, read the screen, and push agent-state events. Its `agent.prompt` even implements the
same delivery contract Antiphon measured into CLAUDE.md by hand — bracketed-paste body, then a
separate `\r` 300 ms later.

Two facts bound the adoption shape, and both are structural, not maturity concerns:

1. **herdr can only show terminals it owns.** There is no API to adopt an external pty — nothing in
   the 91 methods attaches a pane to an existing process. A session living in a detached
   `Antiphon.PtyHost` today can never appear in herdr. So "herdr as a viewing layer over existing
   sessions" is impossible; the only integration is "launch *this* session inside a herdr pane
   instead of a pty-host," which makes herdr an alternate PTY BACKEND per session, chosen at launch.
2. **herdr's persistence is weaker than the pty-host split.** Pane processes survive client detach
   but **die with the herdr server** ("When the Herdr server restarts, original pane processes are
   lost… the underlying agent process does not survive — it must be relaunched", session-state
   docs). Antiphon's detached pty-hosts survive runner restarts by design. A herdr-hosted session
   trades that away and leans on the existing dead-mid-turn machinery (`SessionRestartBoundary` +
   `--resume` auto-continue) instead.

So: a per-agent (or per-launch) opt-in — "run this agent in herdr" — where the operator's own herdr
instance hosts the terminal, the operator sees and types into it natively, and Antiphon drives it
over the named pipe with the ENTIRE existing transcript pipeline unchanged (binding C1–C4,
working/idle, CARD-0055 submit-and-verify all read JSONL + DB, none of it touches the pty layer).
herdr's own agent-state stream becomes an additional supervision signal, never a kill authority.

| Question (from the card) | Answer, on this evidence |
|---|---|
| Canonical source? | `github.com/herdrdev/herdr`, docs at herdr.dev/docs. Created 2026-03-27, v0.8.2, pushed 2026-08-20, 31 193 stars, 2 226 forks, 217 open issues, 76 contributors. Rust: `portable-pty =0.9.0` (vendored+patched), `ratatui 0.30`, `tokio`, `interprocess 2.4.2`. |
| Client-server, persistent? | Yes. Background server owns the panes; clients (TUI) attach/detach; sessions survive detach. **But not server restart** — only layout/cwd/focus are restored, processes are relaunched (session-state docs). |
| Does the API do real INPUT? | **Yes.** `pane.send_text` / `pane.send_keys` / `pane.send_input`, plus `agent.prompt` (bracketed paste + delayed `\r`, refuses a `blocked` agent without writing, optional atomic wait-until-state). Verified in the schema AND in `src/app/api/agents.rs` including its tests (`agent_prompt_sends_text_then_delays_enter`). |
| Windows? | **Native, "generally available"** (windows-beta docs — the URL slug is the only "beta" left). ConPTY panes via a **bundled app-local ConPTY runtime** (system ConPTY on older Win10 drops Kitty-keyboard sequences; `HERDR_WINDOWS_CONPTY=system` opts out). Control API is a **named pipe** with DACL set at creation (`src/ipc.rs`, `interprocess` GenericNamespaced). Windows Terminal / PowerShell attach supported. Not supported on Windows: `herdr terminal attach`, Windows as `--remote` target, live server handoff, fd handoff, Unix process groups. Known cosmetic caveats: cursor flicker (drawn cursor default), IME anchoring, modified-key reporting. |
| License? | **Apache-2.0.** Relicensed from the AGPL/commercial dual license on 2026-07-22 (`chore: relicense herdr under apache-2.0`, LICENSE history; GitHub SPDX and Cargo.toml agree). The card's AGPL concern is moot — and Antiphon only *connects to* an operator-run binary anyway, which even AGPL would have permitted. No commercial license needed. |
| Agent-state detection real? | Real but heuristic for Claude: three tiers — lifecycle hooks (authoritative, for Pi/OpenCode/etc.), **screen-manifest matching for Claude Code** (TOML rules over the live bottom-buffer snapshot, auto-updated from herdr.dev, local overrides win), and foreground-process inspection. `blocked` is deliberately strict (known approval/permission UI only; novel prompts show idle). This is the same signal class as Antiphon's own probes — it does NOT collapse the CARD-0047/0048/0103/0108 defect ladder, it outsources manifest maintenance to a 76-contributor project. Useful as corroboration, not authority. |

## 1. The control surface, verified

Transport: newline-delimited JSON over a local socket — Unix domain socket at
`~/.config/herdr/herdr.sock` (per-session variants under `sessions/<name>/`), **named pipe on
Windows** with the same path structure, resolution via `--session` / `HERDR_SOCKET_PATH` /
`HERDR_SESSION`. Requests are `{"id","method","params"}`; the full schema is queryable at runtime
(`herdr api schema --json`) and versioned (`protocol: 20, schema_version: 1` today) — the docs tell
clients to ignore unknown fields for forward compatibility.

The 91 methods, grouped (all confirmed in `herdr-api.schema.json`, not prose):

- **Hierarchy:** Server → session → **workspace** (`workspace.create {cwd, env, label}` — "the
  top-level project container… one workspace per repo, task, or investigation") → **tab**
  (`tab.create {workspace_id, cwd, label}` — "a layout inside a workspace") → **pane** ("a real
  terminal"; `pane.split {direction: right|down, ratio, env}`). Plus `layout.export` /
  `layout.apply` (declarative tab trees) and `layout.set_split_ratio`.
- **Input:** `pane.send_text`, `pane.send_keys` (named keys — `"enter"`, `"esc"`, `"ctrl+h"`…,
  validated before any byte is written), `pane.send_input` (both). `agent.prompt {target, text,
  wait{until[], timeout_ms}}` — from the socket-api docs: *"this submits the prompt and starts the
  wait in one request, avoiding a race between separate calls. If the resolved agent is already
  `blocked`, `agent.prompt` returns `agent_blocked` without sending input or starting the wait."*
  Implementation (`src/app/api/agents.rs`): bracketed-paste-wrapped body, `\r` scheduled 300 ms
  later (`AGENT_PROMPT_SUBMIT_DELAY`), plus a focus-event workaround for Copilot.
- **Output:** `pane.read` / `agent.read` (`source: visible|recent|recent_unwrapped|detection`,
  `format: text|ansi`, `strip_ansi`), `pane.wait_for_output {match: substring|regex, timeout_ms}`,
  `pane.process_info` (shell PID + foreground process), `pane.layout`, `session.snapshot` (one-shot
  full state for client bootstrap).
- **Agent state:** `agent.list/get/explain`, `agent.wait {until[], timeout_ms}` — *"server-owned and
  event-driven. It pins the resolved pane occupant so a replacement cannot satisfy the wait"* —
  states `idle|working|blocked|done|unknown`; `pane.report_agent` lets an external authority (us)
  SET the semantic state; `agent.start {name, kind, pane_id}` launches a known agent kind into a
  pane.
- **Events:** `events.subscribe` push stream — `pane.agent_status_changed`, `pane.output_matched`,
  `pane.created/closed/focused`, workspace/tab/layout/worktree lifecycle.
- Also present, noted and not needed: worktree management, plugins/marketplace, Kitty graphics
  (experimental; Windows Terminal doesn't support it), notifications, window title.

**What the API does NOT have — and it's the load-bearing gap:** any way to attach a pane to an
existing external process or pty. `pane.split`/`tab.create` spawn fresh shells; `agent.start`
spawns a fresh agent. herdr shows only what it spawned. (Also no method resembling CARD-0055's
transcript confirm — `agent.prompt`'s wait settles on herdr's *detected state*, which for Claude is
the screen-manifest heuristic. Submission verification stays ours.)

## 2. What this can and cannot be for Antiphon

**Cannot be: a viewing layer over today's sessions.** Because of the ownership gap, existing
pty-host sessions can never be mirrored into herdr. Any "make sessions visible in herdr" feature is
necessarily "launch the session IN herdr."

**Can be: a third, per-session PTY backend.** Today `PtyAgentRunner` spawns via inbox conhost or
`ModernConPtyConnection` (both in a detached pty-host). A herdr-backed session instead does
`workspace.*`/`tab.create`/`agent.start` against the operator's herdr, and delivery becomes
`pane.send_text` + `pane.send_keys ["enter"]` (or `agent.prompt`) instead of a pty write. Everything
above the pty layer is untouched by construction:

- **Transcript binding (C1–C4), working/idle, compaction/interrupt rules** — all read Claude's JSONL
  and DB rows, not the pty. `pane.process_info` even supplies the child PID/start-time evidence C3
  wants.
- **CARD-0055/0024 submit-and-verify** — the verdict is a `UserPrompt` transcript row matching the
  body's head window. That stays the authority verbatim. herdr's `agent.prompt` acceptance and
  `pane.wait_for_output` echo checks are extra *pre-signals*, never the verdict. Its
  `agent_blocked` refusal is a new failure mode the queue must map (deliver-when-idle already
  models "not now").
- **Delivery ceilings** — herdr always wraps in bracketed paste over its bundled modern ConPTY
  runtime, i.e. the `modern` profile's regime (CARD-0037's measured 43 200 B single-write world),
  but the ceilings for a herdr-hosted pane must be established the same way the modern ones were:
  measured, then pinned. `PtyDeliveryProfile` already knows how to resolve per-backend ceilings and
  refuses to trust one process's opinion (CARD-0056) — a `herdr` arm slots in.
- **What is genuinely lost:** pty-host-split restart survival. herdr server restart/update kills
  pane processes (layout restored, processes relaunched). Mitigated, not solved, by the existing
  dead-mid-turn path (`SessionRestartBoundary`, `--resume` + auto-continue) — and it's the
  operator's own herdr, restarted on the operator's schedule. This is why herdr stays opt-in per
  session and the pty-host backend stays the default for always-on/channel-bound agents.

**What the operator gains:** the thing CARD-0111 actually asked for — the session is a real,
native, attachable terminal (Windows Terminal attach is supported), with herdr's own sidebar
rolling every agent up to working/blocked/idle/done, panes clickable, split, zoomable — instead of
Antiphon's read-only `SessionTerminal` web snapshot.

## 3. The project→workspace / agent→tab mapping

Confirmed model: workspace = top-level project container owning tabs; tab = a layout; pane = one
terminal; panes split right/down with ratios; `layout.apply` builds a declarative tree; no
documented pane-per-tab cap.

Proposed mapping (closest honest fit to the user's "tab per agent… tab per 4 agents if possible"):

- **One workspace per Antiphon `Project`** — `workspace.create {cwd: project.LocalRepositoryPath,
  label: project.Name}`, tagged via `workspace.report_metadata` with the project id so
  reconciliation can find it again after a herdr restart.
- **One pane per agent, four panes per tab in a 2×2 grid** — `tab.create` per group of 4, then
  `layout.apply` (or three `pane.split`s at 0.5) for the quad; pane labelled with the agent name
  (`pane.rename`), agent launched with `agent.start`. This IS the user's "tab per 4 agents": herdr
  has no "show 4 tabs at once" — a tab is the unit you view, a pane is the unit an agent owns, so
  the grouping lives one level down from the literal phrasing. `pane.zoom` gives any pane the full
  tab temporarily, which covers "focus one of the four."
- A strict tab-per-agent alternative (one pane per tab) also works and herdr's sidebar makes many
  tabs navigable — but it gives up the at-a-glance quad, so the 2×2 grouping is the recommendation.
  The `agent.view.set` API is NOT this — verified to be a sidebar filter projection only ("does not
  change `agent.list`, notifications, detection").

## 4. CARD-0105 is not this card's use case — read, corrected

CARD-0105, read in full, is a **context-menu** card: right-click the terminal icon on
`AgentsPage.tsx` to reach settings/files/board. The terminal *view* it mentions already exists and
works (left-click → `AgentCliModal` → `SessionTerminal`). Nothing in CARD-0105 needs or wants
herdr; herdr is not its mechanism, and this plan does not couple to it. The genuinely-new user
surface herdr provides — a native attachable terminal — has no existing card; if built, the natural
UI hook is one more item in exactly that menu ("Open in herdr") shown only when the session's
backend is herdr, which is a one-line follow-up to CARD-0105, not a dependency.

## 5. Slices, if/when a build pass is commissioned

All additive, all behind opt-in config, mirroring how `ANTIPHON_PTY_BACKEND=modern` was landed
(flag default-off in code, enabled deliberately in this deployment's settings).

- **S1 — spike + client:** operator installs herdr (`irm https://herdr.dev/install.ps1 | iex`);
  `HerdrClient` in the session-runner (named-pipe NDJSON, request/response + event subscription,
  schema-version check via `ping`/`herdr api schema`, "not running" = backend unavailable, loudly —
  CARD-0112's stale-capability precedent). Measure on this machine (Win10 19045): named-pipe
  round-trip, `pane.send_text` ceilings, whether a Claude launched via `agent.start` writes its
  JSONL where discovery + C1–C4 expect (it must — cwd-keyed, herdr doesn't wrap the process).
  **This slice is the go/no-go gate: if the spike falsifies any §1 claim on Windows, stop here.**
- **S2 — launch path:** `SessionBackend=herdr` per agent; workspace-per-project + quad-tab mapping
  (§3); reconciliation arm that re-finds workspaces/panes by reported metadata after a herdr
  restart and routes dead panes into the existing relaunch/resume path.
- **S3 — delivery adapter:** queue delivery via `pane.send_text` + separate enter (preserving our
  own LF-normalize/wrap/`\r` contract rather than trusting `agent.prompt`'s, so CARD-0055's
  Enter-only-retry semantics carry over unchanged); CARD-0055 transcript confirm as the unchanged
  verdict; `agent_blocked` mapped to deliver-when-idle; ceilings measured and pinned in a
  `PtyDeliveryProfile` herdr arm.
- **S4 — state mirror:** subscribe `pane.agent_status_changed` as a supervision *corroboration*
  signal (e.g. herdr says `blocked` while we say working → surface an incident for a human;
  optionally `pane.report_agent` to push our transcript-derived state INTO herdr's sidebar, where
  we are the better authority). Never a kill trigger — CARD-0055/0056's rule that destructive
  action needs positive evidence stands.

## 6. Deliberately not in scope

- Replacing or migrating the pty-host split, the inbox/modern backends, or any measured ceiling —
  herdr-hosted sessions are a parallel opt-in lane; always-on/channel-bound agents stay on
  pty-hosts until herdr-lane restart behaviour has real mileage.
- Antiphon spawning, supervising, updating, or bundling the herdr binary/server (user decision:
  operator-run; also sidesteps every redistribution question).
- Trusting herdr's agent-state as authority for anything destructive, or replacing Antiphon's
  working/idle computation with it.
- herdr's worktree API, plugin/marketplace system, Kitty graphics, `--remote` (Windows can't be a
  remote target anyway), and multi-session herdr topologies.
- Auto-updating agent-detection manifests from herdr.dev is herdr's own behaviour on the operator's
  box; noted as a (mild) supply-chain consideration, not ours to manage.

## 7. Card housekeeping

- CARD-0111's summary needs three corrections from primary sources: **Apache-2.0 since 2026-07-22**
  (not AGPL/commercial — LICENSE history read directly); **not single-maintainer scale** (76
  contributors, 31.2k stars, 2.2k forks, daily pushes); **Windows is native and GA** (bundled
  app-local ConPTY runtime, named-pipe control API) rather than "check for WSL-only."
- The card's hope that herdr "could collapse the quiet-is-not-done defect class" is answered: no —
  for Claude Code herdr uses the same screen-heuristic class we do (§ Verdict table); the honest
  win is visibility + outsourced manifest maintenance as corroboration.
- CARD-0105 stands unmodified (§4); no dependency created in either direction.
- Next step if the user wants the build: commission S1 as its own card (the spike is the decision
  gate), sized small; S2–S4 only after S1's measurements hold.
