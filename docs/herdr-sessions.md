# Herdr sessions

**Herdr** is a terminal multiplexer the operator runs themselves, with a JSON-RPC control socket.
Antiphon can host an agent's interactive child in a **herdr pane** instead of its own detached
pty-host process — so the session is a real, visible, natively attachable pane in the operator's
own terminal rather than a headless process you can only see through Antiphon's screen mirror.

That is the whole benefit, and it is a real one. Everything else on this page is the price.

> **Status.** Herdr is an **opt-in spike**, not the default. `SessionBackend.PtyHost` is `0` and is
> what every agent that existed before the column still has. Herdr is something an operator opts
> *into*, per agent, never something a migration does to a working agent.

## Source of truth

| Fact | Owner |
|---|---|
| The lane, its constraints, why `PtyHost` is `0` | `server/Domain/Enums/SessionBackend.cs` |
| The Kind refusal gate (create / PATCH / channel-bind / launch) | `AgentService.ValidateSessionBackendPairing` |
| Pane placement | `src/Antiphon.SessionRunner/HerdrPaneAllocator.cs` |
| Launch, input, kill, status refresh | `src/Antiphon.SessionRunner/HerdrPaneChild.cs` |
| Restart adoption bar | `SessionRunnerRuntime.AdoptHerdrSessionsAsync` |
| Delivery ceilings | `SessionDeliveryProfile` + `DelegationSettings.HerdrCeilings` |
| Event pump | `src/Antiphon.SessionRunner/HerdrEventPumpService.cs` |
| Live probe results | `.antiphon/card-0160-probe-results.md` |

## 1. When to choose it — and when you cannot

Choose herdr when you want to **watch and take over** a session by hand: a pane you can attach to
in your own terminal, next to your other work.

You **can** choose it for:

| Kind | Why |
|---|---|
| **`ClaudeCode`** | spiked (CARD-0160); launch script (CARD-0187) |
| **`Grok`** | spiked (CARD-0187 K1) — same launch script, Grok transcript tailer |
| **`Codex`** | spiked (CARD-0187 K5) — through `codex.cmd`, never `agent.start` |

You **cannot** choose it for:

| Refused | Why |
|---|---|
| **`OpenCode` / `Raw`** | no structured transcript; screen-only lanes are not hosted |

That is a **refusal, never a silent remap** — `409 herdr_refused`, with the reason naming the
kind and the supported list. The Kind gate runs at create, PATCH (over the request-resolved
final state, so a Kind change in the same PATCH is caught), channel bind, and launch time
(`AgentSessionService`), which also marks the session `Failed` with
`"Herdr launch refused: {kind} is not supported on herdr."`

Always-on and channel-bound agents are allowed on the lane (CARD-0186). A herdr restart does not
survive the **exit** (the session is still `Exited(HerdrRestartPresumedDead)`); the **next launch**
of that session id relaunches into the restored empty pane when herdr kept the pane id (CARD-0224),
or allocates a new tab when the pane is unknown.

## 2. Turning it on

Herdr is off in code and on in this checkout:

```jsonc
// src/Antiphon.SessionRunner/appsettings.json
"SessionRunner": {
  "PtyBackend": "modern",
  "Herdr": { "Enabled": true }
}
```

`HerdrSettings.Enabled` **defaults to `false`** — a fresh installation has the lane disabled — and
this deployment's runner appsettings turns it on. Other settings:

| Setting | Default | Meaning |
|---|---|---|
| `Session` | null | explicit named herdr session; same precedence as herdr's own `--session`, ahead of `HERDR_SOCKET_PATH` and `HERDR_SESSION` |
| `ConnectTimeoutMs` | 5 000 | bound on opening the named-pipe connection |
| `ExpectedProtocol` | 20 | the wire protocol this client was compiled and tested against |
| `EventsReconnectMinSeconds` / `MaxSeconds` | 1 / 30 | event-pump reconnect backoff, doubling |

Socket resolution order: `SessionRunner:Herdr:Session` → `HERDR_SOCKET_PATH` →
`HERDR_SESSION` → `%APPDATA%\herdr\herdr.sock`.

**The runner advertises `herdr` in `GET /capabilities` only when `Enabled` is true.** That is not
cosmetic: the server's capability gate would otherwise green-light a launch that `HerdrClient` then
refuses. A missing, stopped, unreachable or protocol-incompatible herdr is **always an explicit
exception — never a silent fallback to the pty lane.**

## 3. Choosing it for an agent

`SessionBackend` is a field on the agent (`sessionBackend: "Herdr"` on create or PATCH), and it is
**snapshotted onto the session row** at launch. Delivery ceilings key on the session's snapshot,
not on the agent's current value, so changing the agent mid-flight does not retroactively change a
running session's contract.

Where the ids live is deliberate: **workspace / tab / pane ids are in a runner-side sidecar file,
not in database columns.**

```
<SessionLogPath>/herdr/<sessionId:N>.json
```

An exit that **leaves the pane standing** (pid loss, herdr restart with layout restore) moves that
sidecar to a **last-pane record** at `<SessionLogPath>/herdr/last-pane/<sessionId:N>.json`
(CARD-0224) so the next launch of the same session id — or a fresh fallback with
`ReusePaneOfSessionId` — can target it. `pane.close` success and `PaneLeftOpen` (a foreign process
owns the pane) still plain-delete. Last-pane records older than 7 days are pruned on adoption.
`LoadAll`, the allocator, and the event pump never read `last-pane/`.

The sidecar's `Origin` is `launched` (Antiphon created the pane) or `attached` (CARD-0213: the
operator bound a standing agent to a pane Antiphon did not launch). An attached-origin exit
writes **no** last-pane record: Antiphon never types a launch script into a pane it did not create.
Attached panes are also excluded from the allocator census (they are never a slot to split).

Adoption (Layer A) runs *before* the runner's HTTP API listens, so it structurally cannot read the
server's database; and the server has no herdr client at all, so DB-resident pane ids would have no
reader. Herdr's own metadata tokens (`pane.report_metadata`, `antiphon-session`) are best-effort
identity only — TTL is capped at 24 h and restart survival is unverified.

The server does supply the *placement context* on the launch request (`HerdrLaunchOptions`), since
the runner has no DB: a `WorkspaceKey` (`project:<guid>`, or `none`), a `WorkspaceLabel`, a
`WorkspaceCwd`, the `PaneTitle` the operator should see — **the agent's name, never the TUI
profile id** (CARD-0225; fallback is `Agent.Slug`, then `DefinitionName`, then `"agent"`) — and
`AgentSlug` (CARD-0211), the sanitised Antiphon slug applied as the herdr agent name after
detection. `PaneTitle` and `AgentSlug` are independent: tab `PM-Orchestrator-Grok` can answer to
`herdr agent get pm-orchestrator-grok`. Null `AgentSlug` means do not rename (old-server compat,
or a session with no owning agent).

Workspace matching (CARD-0323) is token-first, then one untagged exact label:

1. A workspace whose `tokens["antiphon-ws"]` exactly equals `WorkspaceKey` — Antiphon-owned
   identity, even if a human later changed the label. That match also re-reports the token
   (best-effort TTL refresh).
2. Otherwise, **exactly one** workspace whose `Label` equals `WorkspaceLabel` **and which has no
   non-empty `antiphon-ws` token**. That is an operator-visible placement match, not ownership.
   Antiphon places a new tab there and **never** writes `antiphon-ws` to it, now or on later
   launches.
3. Otherwise create a workspace: no label match, two or more untagged same-label candidates, or a
   same-label workspace already tagged to a different key.

There is no cwd-token fallback and no live-pane-cwd heuristic. Exact label is useful only when
unique; ambiguity creates a new managed workspace. A reused operator workspace is not evidence
that Antiphon owns its other tabs, panes, or processes.

Renaming an agent in Antiphon mid-life does not rename its live herdr agent or tab; the next
launch does. Herdr forgets the agent name when that occupant exits.

## 3a. Attaching an operator pane

`POST /api/agents/{id}/attach-herdr` with `{ "paneId": "w2:p3" }` binds a **standing** Herdr agent
(`CardId == null`) to a pane Antiphon did not launch. The session row id **is** the pane's native
session id (Grok `--session-id` / herdr `agent_session`, argv first). Inspect is read-only; the
DB row is written `Starting` before the runner binds anything.

What attach does **not** do:

- Rename the pane label or herdr agent name (the operator already addresses it that way).
- Type a launch note, bootstrap prompt, or `/remote-control`.
- Kill the process on Stop — Stop **detaches** (sidecar gone, metadata cleared, pane left running).
- Survive a herdr restart with no sidecar (P7: the child dies with herdr). Re-attach by hand; a
  same-id Stopped/Failed row restamps.

Grok without `--session-id` and with nothing usable in `agent_session` is refused
(`herdr_native_id_unknown`); a known id whose `GROK_HOME/sessions/*/{id}/` directory is missing
is refused (`herdr_transcript_not_found`). The live smoke against `D:\src\maven.dropcopy` /
`D:\src\mav-ref` could not run on the machine that built this card (no herdr, no those homes).
P-A1 (what herdr fills in `agent_session` for a bare launch) is likewise unmeasured; attach tries
argv first, then `agent_session` when `source != antiphon`, then refuses Grok.

A pane picker (`GET /herdr/panes` list) is a follow-up card.

## 4. Where a pane lands

`HerdrPaneAllocator` is pure and deterministic. Within a workspace: **fill the lowest-numbered
Antiphon tab that has fewer than 4 live Antiphon panes; if none has a free slot, create a new tab.**

| Live Antiphon panes in the target tab | Decision |
|---|---|
| 0 (no such tab) | `tab.create` |
| 1 | split that pane **right**, ratio 0.5 |
| 2 | split the **first** pane **down**, 0.5 |
| 3 | split the last pane (stable `PaneId` order) **down**, 0.5 — converging on a 2×2 |
| 4 | `tab.create` |

**Operator tabs — tabs with no Antiphon panes — are never split into.** Gaps left by stopped panes
are not reflowed; the next launch refills them. **An existing workspace root pane is not an
allocator slot.** The allocator only sees live Antiphon panes; operator tabs stay unsplittable.

When Antiphon **creates** a workspace, the first launch uses `workspace.create`'s returned root
tab and root pane instead of the allocator's `tab.create` branch, then `tab.rename`s that new tab
to `PaneTitle`. Env is passed on `workspace.create` (same as `tab.create` / `pane.split`); if the
session cwd differs from the workspace cwd, the launch script prepends a quoted
`Set-Location -LiteralPath`. A later launch into that same owned workspace goes through the
allocator as usual. Reusing an untagged operator workspace never consumes its existing root: it
runs ordinary `tab.create` in that workspace.

Launch sequence: ensure workspace → **resolve the target pane** (CARD-0224: last-pane record for
this session id, or `ReusePaneOfSessionId`) → then either relaunch/adopt in place, use a freshly
created root tab/pane, or allocate (`tab.create` / `pane.split`, env on both) → `tab.rename` of a
created root only → `pane.rename` → `pane.report_metadata` → check the
pane shell is PowerShell → write
`<SessionLogPath>/herdr/<sessionId:N>.launch.ps1` (UTF-8 with BOM; `'exe' @(args)`, optional
`Set-Location` prelude on a created root) → type
`& '<path>'` via `pane.send_text` + `pane.send_keys ["enter"]` → poll `pane.get` until
`Agent` matches the expected kind (`claude` / `grok` / `codex`) → `agent.list` → `agent.rename
<paneId> <slug>` (suffixed `-2`… if a live agent holds it; skipped, Warning, if the list or
rename fails) → `pane.process_info` for the child pid → write the sidecar → delete the script.

**Target resolution** (before the allocator; operator tabs are still never split into):

| Pane state | Decision |
|---|---|
| No last-pane record, or pane unknown to `pane.get` | allocator (today's path) |
| Empty PowerShell pane that was ours (`Origin = launched`) | **relaunch in place** — type the launch script into that pane; no `tab.create` / `pane.split` / `tab.rename` |
| Live process whose argv names **our** session id (`--session-id` / `--resume` / `-s`/`-r`) and `pane.Agent` matches | **adopt in place** — bind the pid, type nothing |
| Occupied by a different id, no id, a different kind, or more than one foreground process | **refuse** (`pane_occupied`) — never steal, never fall back to the allocator. The last-pane record is kept so a later backoff can retry once the pane is free. Codex never carries a session id in argv, so an occupied Codex pane is always refused. |
A wrong detected kind, a non-PowerShell shell, or a detection timeout fails the launch
(existing catch kills then disposes); the script is left in place for diagnosis. A rename
failure never fails the launch. **Never `agent.start`.** Target the rename by pane id, never
by name — a name target could resolve to another live agent.

**Never call `tab.close`.** Herdr auto-removes empty tabs, and closing one ourselves was measured
to be the wrong move (probe P3). `KillAsync` also refuses to close a pane that has *unexpected*
foreground processes — anything that is not our recorded child or shell pid — and leaves it open.

## 5. Delivery

Herdr is **a different transport**, so it gets its own ceilings — not a `PtyBackend` value.
`SessionDeliveryProfile` keys them on the session's `SessionBackend` snapshot:

| Ceiling | Herdr pane | (modern ConPTY, for comparison) |
|---|---|---|
| Brief inline max | 43 200 bytes | 43 200 bytes |
| Reply inline max | 14 400 chars | 14 400 chars |
| Oversize tripwire | 86 400 bytes | 86 400 bytes |

86 400 bytes is the largest body measured **exact, byte-for-byte, with zero ESC bytes in the
transcript record**, through one `pane.send_text` — single-write *and* paced (herdr 0.8.2 +
Claude 2.1.241, 2026-08-23). It is the edge of the evidence, not a measured cliff. CARD-0187
S3 probe D1 (2026-08-25) sent the same 43 200 B multi-line body into a **Grok** composer on
this lane: the UserPrompt record was **complete** (whitespace-free match), **joined newlines**
(CARD-0084: 43 200 sent, 42 666 recorded, zero ESC bytes), so the herdr numbers stay as they
are and `ForAgentKind` already zeroes Grok/Codex briefs. 86 400 B on Grok produced no record
within 60 s after that turn (inconclusive, not a measured lower cliff). Codex D1 did not run
this pass (CARD-0195 boot-prompt swallow after a 3.5 s launch-detect). No per-kind herdr
ceiling.

Two guards sit in front of that:

- The runner must still be **advertising `herdr`** in its live capabilities. No answer at all is
  *no evidence*, and falls back to the conservative inbox-conhost set (900 / 3 000 / 1 024) — over-
  spill and over-warn, never over-type. A runner that answers with a list that lacks `herdr`
  downgrades and logs why.
- Writes are `pane.send_text` for the body and `pane.send_keys ["enter"]` for the submit.
  **`agent.prompt` is deliberately never called** — probe S1 produced a false `agent_prompt_stalled`
  through it, and `HerdrClient` does not even wrap it.

The verdict is unchanged from the pty lane: **CARD-0055 transcript confirmation.** A delivery is
`Sent` only when a matching `UserPrompt` transcript row exists — a redraw is not evidence.

**`blocked` only defers.** When herdr's literal `agent_status` is `"blocked"` (a permission or
approval UI has the pane), the flush returns `Nothing` — **no attempt is charged, nothing is
parked, killed or retyped** — and the confirm loop withholds its re-press Enter. A `Mode:"Now"`
send gets a 409 telling the caller to try again when idle. Note that **`done` is not `idle`**: the
normal post-turn `agent_status` is `done`, and treating it as a fault is a mistake this lane
invites.

Herdr's own `pane.revision` measurably **stays flat across real turns** on 0.8.2 (0 of 3), so the
runner owns a **content-delta counter**: it bumps whenever the stripped visible `pane.read` text
differs from the last observation, and folds that into `LastSequence` alongside the revision
(`Math.Max`). Nothing may *require* `revision` to move. The single-session `GET /sessions/{id}`
refreshes both via `pane.get` + `pane.read`; without that refresh a screen-only fallback fails
`NoSubmitOutput` deterministically.

## 6. Restarts — the constraint that shapes everything

**A herdr session does not survive a herdr restart** as a live process. Antiphon's own `--resume`
path owns repopulation. When herdr restores the layout, the empty pane is still ours: the next
launch **relaunches in place** into that pane (CARD-0224). When the pane id is gone (a restart
that dropped the layout), the allocator opens a new tab — today's behaviour, unchanged. The
**exit** is still `Exited(HerdrRestartPresumedDead)`; only the subsequent launch changed.

**A *runner* restart is different** — the pane is still there, and adoption re-binds it, but only
on positive evidence. The bar (CARD-0056 transposed) is:

1. `pane.get` answers for the sidecar's `PaneId` — an unknown pane is a verdict, not a retry.
2. `pane.process_info` still lists our recorded `ChildPid` among the foreground processes.
3. `pane.read` actually answers.

All three ⇒ re-adopt. **Pane exists but our child pid is gone ⇒ `Exited("HerdrRestartPresumedDead")`**
— that is the restored-but-empty trap, where herdr restarted underneath us and recreated the pane
shell with nothing in it. A false adoption there would badge a dead session as live.

**Herdr unreachable is not a verdict.** The sidecar is left in place, the session is left
unadopted, and liveness retries. Deciding "dead" from "I could not ask" is exactly the failure this
codebase has paid for elsewhere.

Herdr adoption runs *after* transcript claims are restored and *before* pty-host adoption, all
inside the sweep that must finish before the runner's HTTP API starts listening.

## 7. Events

`HerdrEventPumpService` holds a long-lived `events.subscribe` stream (registered always, inert
unless `Enabled`; reconnects with 1→30 s backoff; recycles when the live pane set changes).
The measured `pane.agent_status_changed` stream event is **dotted** (CARD-0163 R9, 2026-08-26);
the pump also accepts the schema's legacy `pane_agent_status_changed` spelling.

**Every herdr event is a verification TRIGGER, never evidence.** Herdr **replays historical
`pane_closed` events to every new subscriber** (measured, probe E5), so a pump that trusted the
event would re-kill sessions on stale closes at every single reconnect. The pump therefore re-runs
the full §6 adoption bar before recording any `Exited`.

A `blocked` → not-blocked transition may only **nudge** `FlushIfIdleAsync`, which re-checks
`IsWorkingAsync` and the blocked gate for itself.

CARD-0163 also pushes the runner's file-ordered transcript verdict as display-only
`pane.report_metadata`: all sidebar state labels read `<herdr> · antiphon: <verdict>`, with
`antiphon-state` and `antiphon-as-of` tokens, TTL renewal, and an explicit exit clear. It never
calls `pane.report_agent`, so this label cannot change herdr's `agent_status`, delivery gate,
session state, or disagreement evidence.

**`HerdrStatusDisagreement` (incident kind 34)** fires when herdr's `agent_status` disagrees with
Antiphon's transcript-derived `IsWorkingAsync` for at least 10 minutes. It is **Warning, always** —
timeline-only unless the agent is channel-bound, with a pull-before-raise and no re-raise inside
the hysteresis window. There is no Error/Critical ladder, because herdr's detection is the same
screen-heuristic class as our own probes: disagreement is corroboration for a human, not a fact.
**It never kills, retypes, escalates or corrects.**

## 8. Failure modes at a glance

| Symptom | What it is | What to do |
|---|---|---|
| `409 herdr_refused` on create/PATCH/bind | `OpenCode` / `Raw` (or any unmapped kind) on herdr | pick `PtyHost`, or ClaudeCode / Grok / Codex |
| Session `Failed` with "Herdr launch refused…" | same Kind gate, hit at launch | the agent kind changed under the session; fix the pairing |
| Launch fails: detected `{actual}` where `{expected}` was expected | profile exe and agent Kind disagree | fix the pairing; the pane is torn down, script left for diagnosis |
| Launch fails: pane shell is not PowerShell | herdr default_shell is not `powershell`/`pwsh` | set herdr `default_shell`, or use PtyHost |
| Launch fails: detection timeout | `pane.get.agent` never became the expected kind | check the pane / script left on disk; raise `LaunchDetectTimeoutMs` only after measuring |
| Launch throws instead of falling back | herdr missing/stopped/wrong protocol | start herdr, or set `Enabled: false` and relaunch on `PtyHost` |
| Sessions `Exited(HerdrRestartPresumedDead)` in a batch | herdr restarted | expected; the next launch relaunches into the restored pane if it still exists, else allocates |
| Launch throws `pane_occupied` | last-pane is held by a foreign / unidentifiable process | free the pane (or attach, CARD-0213); the always-on backoff will retry the same pane, never a new tab |
| `409 herdr_refused` on attach | agent not on Herdr, kind unmapped, or runner lacks `herdr`/`herdr-attach` | pick another backend/kind, or restart the runner |
| `409 session_active` on attach | the agent already has a live session | Stop (or Detach) first |
| `404 herdr_pane_not_found` | pane id unknown to herdr | `herdr pane list` |
| `409 herdr_pane_unoccupied` | `pane.get.agent` is null | the pane has no detected agent |
| `409 herdr_kind_mismatch` | pane's agent ≠ the Antiphon agent's kind | attach a grok pane to a Grok agent |
| `409 herdr_pane_foreign` | 0, ≥2, or a non-family foreground process | same kill-safety as `KillAsync` |
| `409 herdr_pane_bound` | live session, sidecar, or another id's last-pane claims it | Stop/detach the holder |
| `409 herdr_native_id_unknown` | Grok with no `--session-id` and silent `agent_session` | relaunch with `--session-id` |
| `409 herdr_transcript_not_found` | Grok id known but no `sessions/*/{id}/` under `GROK_HOME` | the grok is on another home/machine |
| `409 herdr_pane_changed` | pid or native id changed between inspect and attach | inspect again |
| `409 session_id_taken` | native id is another agent's session or a card session | pick another pane |
| `503 herdr_unreachable` | herdr is down | start herdr |
| Deliveries silently deferring | `agent_status == "blocked"` — an approval UI has the pane | answer it in the pane, or attach and clear it |
| Ceilings suddenly 900/3 000/1 024 | the runner is not advertising `herdr` | check `SessionRunner:Herdr:Enabled` and `GET :17204/capabilities` |
| `HerdrStatusDisagreement` Warning | corroboration hint only | look at the pane; nothing is auto-corrected |
| Empty tabs accumulating | not ours — herdr auto-removes them | do not add a `tab.close` |
| herdr agent is `<slug>-2` | another live agent (often the previous incarnation's pane) holds `<slug>`; nothing is stolen | look at `herdr agent list`; the Warning names the holder pane. Close or rename the holder if you want the unsuffixed name back on the next launch |

## 9. Deferred / out of scope

S4b shipped display-only `pane.report_metadata` labels and client badges for herdr's existing
screen-derived status. `pane.report_agent` remains explicitly rejected: its lifecycle-authority
takeover would overwrite S3's blocked signal and turn S4 disagreement into a tautology. Pane/
workspace/tab ids stay out of the database.

## See also

- [agent-kinds.md](agent-kinds.md) — `SessionBackend` is a **separate dimension** from
  `PtyBackend`; never touch `PtyBackendPolicy` to change lanes.
- `docs/investigations/2026-08-21-herdr-s1-spike-CARD-0120.md` — the original spike.
- `docs/superpowers/plans/2026-08-23-card-0160-*`, `*-0161-*`, `2026-08-24-card-0162-*`,
  `2026-08-24-card-0164-*` — the build slices, with their measurements.
- `.antiphon/card-0160-probe-results.md` — live probes P1–P6 against a real herdr.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #28

- **An attached herdr pane is never counted, never closed and never pid-killed — Stop detaches.** CARD-0213 binds a standing agent to a pane Antiphon did not launch (`POST /api/agents/{id}/attach-herdr`). The sidecar `Origin` is `attached`. The allocator skips those panes (they are not a slot to `pane.split`), `KillAsync` / `TryKillOrphanedChild` / `KillPendingHerdr` never `pane.close` or kill `ChildPid`, and an attached exit writes no last-pane record. Stop on that agent drops the sidecar and clears metadata; the operator's TUI keeps running.

### Preserved Gotcha #60

- **Herdr agent name is `Agent.Slug` applied at every launch, tab/pane title is `Agent.Name`, and the two are independent** (CARD-0211 / CARD-0225): herdr forgets the agent name when that occupant exits, so the runner re-applies the sanitised slug (`[a-z][a-z0-9_-]{0,31}`) via `agent.rename` after detection. The title the operator sees is `agent.Name` (never the TUI profile `DefinitionName` — one profile serves many agents). A live holder of that slug is never renamed out from under; the new pane is suffixed `-2`… and a Warning names the holder. `agent.list` failure skips the rename (cannot prove the name is free). An agentless session is not renamed and falls back to `DefinitionName` for the title. No schema / sidecar field.

### Preserved Gotcha #61

- **A herdr relaunch after pid loss targets the pane we already had** (CARD-0224): exits that leave the pane standing retire the sidecar to `<SessionLogPath>/herdr/last-pane/<sessionId:N>.json` instead of deleting it. The next launch of that id (supervisor resume, or a `Fresh` fallback via `ReusePaneOfSessionId`) relaunches into an empty PowerShell pane or adopts a live process whose argv names our session id. A foreign occupant **refuses** the launch (`pane_occupied`) — never falls back to the allocator, never steals. `pane.close` success and `PaneLeftOpen` still plain-delete. Codex never carries a session id in argv, so an occupied Codex pane is always refused. Do not put pane ids in the database.

### Preserved Gotcha #62

- **Herdr session backend is opt-in and does not survive a herdr restart** (CARD-0160): `SessionBackend.Herdr` is a separate dimension from `PtyBackend` (never touch `PtyBackendPolicy`). Default is `PtyHost`. Refused at create/PATCH, channel-bind, and launch-time for unmapped kinds — never silently remapped. CARD-0186 lifted the AlwaysOn and channel-bound refusals. CARD-0187 lifted the Kind refusal for Grok and Codex; OpenCode/Raw stay refused; herdr launches type a launch script, never `agent.start`. Pane/workspace/tab ids live in the runner sidecar `<SessionLogPath>/herdr/<sessionId:N>.json`, not DB columns. On runner restart: adopt only when `pane.process_info` still lists our `ChildPid` AND `pane.read` answers; a restored-but-empty pane (herdr restarted underneath) is Exited(`HerdrRestartPresumedDead`) — never false-adopted. Empty tabs are auto-removed by herdr (do not `tab.close`). `SessionRunner:Herdr:Enabled` defaults false; capabilities advertise `"herdr"` only when enabled. Live probes P1–P6 recorded in `.antiphon/card-0160-probe-results.md`.

### Preserved Gotcha #63

- **Herdr delivery uses the same CARD-0055 transcript-confirm verdict; ceilings are per-session; `blocked` only defers** (CARD-0161): `SessionDeliveryProfile` keys ceilings on `AgentSession.SessionBackend` (`DeliveryBackend.HerdrPane` — never a `PtyBackend` value). Herdr envelope measured 86 400 B exact via `pane.send_text` (2026-08-23). Never call `agent.prompt` (S1 false `agent_prompt_stalled`). Literal `agent_status=="blocked"` → `FlushResult.Nothing` (no attempt charged); confirm-loop re-Enter withheld while blocked (CARD-0141). Note **`done` ≠ `idle`** — normal post-turn state is `done`. Single-session GET refreshes herdr `LastSequence` via `pane.get` (otherwise screen-only fallback fails `NoSubmitOutput` deterministically). Event pump is S4 (CARD-0162).

### Preserved Gotcha #64

- **Herdr events are verification triggers, never evidence; disagreement is a Warning row only** (CARD-0162): herdr REPLAYS historical `pane_closed` to every new `events.subscribe` (measured E5), so a pump that trusted the event would re-kill on stale closes at every reconnect — `HerdrEventPumpService` re-runs the §6A bar (`pane.get` + `process_info` vs sidecar `ChildPid`) before any Exited. The measured status wire name is `pane.agent_status_changed` (dotted, CARD-0163 R9, 2026-08-26; legacy underscored schema spelling accepted); S4b pushes `state_labels` only, never `pane.report_agent`. `HerdrStatusDisagreement` (34) is Warning, timeline-only unless channel-bound, 10-minute hysteresis + pull-before-raise; it never kills, retypes, or escalates. Blocked-exit may only nudge `FlushIfIdleAsync` (which re-checks `IsWorkingAsync` and the S3 blocked gate). The status badge is a corroboration hint, never a verdict.

### Preserved Gotcha #65

- **Unobservable-baseline delivery: transcript-first with a wall-clock floor; herdr advance is the runner content-delta counter, never herdr's revision** (CARD-0164): a session with zero `TranscriptEntries` no longer skips CARD-0055's confirm loop for a sequence-only verdict — it polls for the FIRST matching `UserPrompt` past `UtcNow − UnobservableBaselineConfirmClockToleranceSeconds` (CARD-0056's BootConfirmClockTolerance shape; null timestamps never confirm), with `CatchUpTranscriptAsync` pulls interleaved, and only at the deadline falls back to today's screen-advance → `Delivered` / nothing → `NoSubmitOutput`. Herdr's own `pane.revision` stays sticky on 0.8.2 across full turns; `HerdrPaneChild` owns a monotonic content-delta counter folded into `LastSequence` alongside revision. `LateConfirmAttemptedMessagesAsync` covers null-baseline attempts via the same wall-clock floor (closes the WhenIdle double-type). Mode:Now gets a `PostFailureConfirmGraceSeconds` pull-and-recheck before 409 for `NoSubmitOutput`/`NoTranscriptRecord` only (never `NoComposerEvidence`). **Never-weaken:** no fix here may make an actually-failed delivery easier to mark Sent — `PromptSubmissionMatch` identity/completeness stay untouched.
<!-- CARD-0254 preserved source ends -->
