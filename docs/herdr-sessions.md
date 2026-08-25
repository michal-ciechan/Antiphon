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

Always-on and channel-bound agents are allowed on the lane (CARD-0186): a herdr restart does not
survive, and an always-on agent is resumed into a new pane by supervision.

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

Adoption (Layer A) runs *before* the runner's HTTP API listens, so it structurally cannot read the
server's database; and the server has no herdr client at all, so DB-resident pane ids would have no
reader. Herdr's own metadata tokens (`pane.report_metadata`, `antiphon-session`) are best-effort
identity only — TTL is capped at 24 h and restart survival is unverified.

The server does supply the *placement context* on the launch request (`HerdrLaunchOptions`), since
the runner has no DB: a `WorkspaceKey` (`project:<guid>`, or `none`), a `WorkspaceLabel`, a
`WorkspaceCwd`, and the `PaneTitle` the operator should see.

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
are not reflowed; the next launch refills them.

Launch sequence: ensure workspace → allocate pane (`tab.create` / `pane.split`, env on both) →
`pane.rename` → `pane.report_metadata` → check the pane shell is PowerShell → write
`<SessionLogPath>/herdr/<sessionId:N>.launch.ps1` (UTF-8 with BOM; `'exe' @(args)`) → type
`& '<path>'` via `pane.send_text` + `pane.send_keys ["enter"]` → poll `pane.get` until
`Agent` matches the expected kind (`claude` / `grok` / `codex`) → `pane.process_info` for the
child pid → write the sidecar → delete the script. A wrong detected kind, a non-PowerShell
shell, or a detection timeout fails the launch (existing catch kills then disposes); the script
is left in place for diagnosis. **Never `agent.start`.**

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

**A herdr session does not survive a herdr restart.** Antiphon's own `--resume` path owns
repopulation. An always-on agent is resumed into a new pane by supervision (CARD-0186).

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

**Every herdr event is a verification TRIGGER, never evidence.** Herdr **replays historical
`pane_closed` events to every new subscriber** (measured, probe E5), so a pump that trusted the
event would re-kill sessions on stale closes at every single reconnect. The pump therefore re-runs
the full §6 adoption bar before recording any `Exited`.

A `blocked` → not-blocked transition may only **nudge** `FlushIfIdleAsync`, which re-checks
`IsWorkingAsync` and the blocked gate for itself.

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
| Sessions `Exited(HerdrRestartPresumedDead)` in a batch | herdr restarted | expected; relaunch/resume from Antiphon |
| Deliveries silently deferring | `agent_status == "blocked"` — an approval UI has the pane | answer it in the pane, or attach and clear it |
| Ceilings suddenly 900/3 000/1 024 | the runner is not advertising `herdr` | check `SessionRunner:Herdr:Enabled` and `GET :17204/capabilities` |
| `HerdrStatusDisagreement` Warning | corroboration hint only | look at the pane; nothing is auto-corrected |
| Empty tabs accumulating | not ours — herdr auto-removes them | do not add a `tab.close` |

## 9. Deferred / out of scope

`pane.report_agent` and the UI badges that would consume it are deferred (S4b). Pane/workspace/tab
ids stay out of the database.

## See also

- [agent-kinds.md](agent-kinds.md) — `SessionBackend` is a **separate dimension** from
  `PtyBackend`; never touch `PtyBackendPolicy` to change lanes.
- `docs/investigations/2026-08-21-herdr-s1-spike-CARD-0120.md` — the original spike.
- `docs/superpowers/plans/2026-08-23-card-0160-*`, `*-0161-*`, `2026-08-24-card-0162-*`,
  `2026-08-24-card-0164-*` — the build slices, with their measurements.
- `.antiphon/card-0160-probe-results.md` — live probes P1–P6 against a real herdr.
