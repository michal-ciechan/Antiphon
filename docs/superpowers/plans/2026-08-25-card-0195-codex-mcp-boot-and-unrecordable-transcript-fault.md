# CARD-0195 — Codex's MCP bootstrap is not the hang, and a transcript fault that could not be recorded

**Date:** 2026-08-25 · **Card:** CARD-0195 (`37fe7da3-ca5d-41b4-bb4a-87e004f21cd9`) · **Status:**
investigation complete; item 3 implemented, item 2 deliberately not wired in ·
**Verified against:** `master` @ `e187db4`. Every number below was measured on this machine on
2026-08-25 — nine real Codex TUI launches through a modern ConPTY, the incident's own ansi log, and
the live dev database (`antiphon-postgres`).

---

## Verdict up front

| # | Question | Answer |
|---|---|---|
| 1 | Is "Starting MCP servers (0/2): codex_apps, node_repl" a real multi-minute hang? | **No.** It is real and recurring, and it is over in **under 4 seconds** — worst of 6 baseline launches was **3.34 s**, and in the CARD-0194 incident itself it cleared while its own elapsed counter still read `(0s`. |
| 2 | Can Antiphon suppress those two servers? | **Yes, and it is verified to work** — but it is **not wired in**, because the precondition the card set ("if it is a real, recurring hang") is false, and the mechanism trades a real capability for ~3 seconds. Recommended as a follow-up card only if a future measurement changes. |
| 3 | Why did `TranscriptBindingIncidentService` swallow the fault? | Postgres **23503** on `FK_AgentIncidents_Agents_AgentId`: `SessionOwnerLookup` handed back an `AgentTasks.AgentId` whose agent row had been deleted. Fixed — see §3. |
| 4 | codex-cli 0.147.0 → 0.149.1 | **Noted, not acted on.** `~/.codex/version.json` shows 0.149.1 available and already `dismissed_version`. Nothing here touches it. |
| — | So what *did* kill session `8be1afc5`? | **Still open, and it is not the MCP boot.** Enter produced *zero* bytes of pty output for the remaining ~9 minutes and no rollout was ever created. Evidence in §4; recommend folding into CARD-0190. |

---

## 1. Item 1 — the MCP bootstrap, measured

### 1.1 The incident's own log already says it was under a second

Re-read of `C:\logs\antiphon\session-runner\8be1afc5ba154d8bbe79fe9625d5196a.ansi.log` (11 993 B,
complete), ANSI-stripped, around the boot status:

```
]0;⠋ Antiphon •Booting MCP server: codex_apps (0s • esc to interrupt) ›Write tests for @filename
]0;⠙ Antiphon  Starting MCP servers (0/2): codex_apps, node_repl (0s • esc to interrupt)
]0;⠼ Antiphon  Start1 (0s • esc to interrup)
]0;⠴ Antiphon  ti
]0;Antiphon   ›Write tests for @filename    gpt-5.6-terra high · C:\src\Antiphon
```

Two things the card's reconstruction missed:

- The elapsed counter **never leaves `(0s`**. Codex re-renders that line every spinner frame; four
  frames (`⠋ ⠙ ⠼ ⠴`) at ~100 ms is the entire lifetime of the status.
- The window title goes from `⠴ Antiphon` (spinner) back to plain **`Antiphon`**, and the composer
  redraws with its **placeholder** (`›Write tests for @filename`) — i.e. empty and idle. The
  bootstrap **completed**, before Antiphon typed anything.

The `Start1 (0s • esc to interrup)` / `ti` garbling the card read as "our typing corrupting the
status line" is the status line being *torn down* mid-repaint, not blocked.

### 1.2 Nine fresh launches, timed

New probe `tests/Antiphon.Agents.Pty.Tests/CodexMcpBootProbeTests.cs` — headed, `[Explicit]`,
`ANTIPHON_CODEX_HEADED_TESTS=1`, **spends no model turns** (it observes the boot and types a marker;
nothing is ever submitted). It launches the real `codex.cmd` through `PtyAgentRunner("modern")` with
the production arg shape (`--no-alt-screen --dangerously-bypass-approvals-and-sandbox`) and samples
the rendered screen every 60 ms.

| Launch shape | run 1 | run 2 | run 3 | composer accepted input |
|---|---|---|---|---|
| Baseline, scratch cwd | seen 3.34 s | seen, ≤1 sample | seen, ≤1 sample | 3/3 |
| Baseline, `C:\src\Antiphon` + `-c model_reasoning_effort=high` + `-c developer_instructions=…` (the incident's shape) | seen 0.39 s | **not seen** | seen 0.06 s | 3/3 |
| Suppressed (§2) | **not seen** | **not seen** | **not seen** | 3/3 |

Header (`>_ OpenAI Codex`) rendered at **0.52–1.68 s** in every run. Raw logs:
`tests/Antiphon.Agents.Pty.Tests/bin-<out>/TestOutput/CodexCanary/*.log`.

**Conclusion: not a hang.** The bootstrap is real, it fires on most launches, and its worst observed
cost is ~3 s — three orders of magnitude short of the 10-minute failure it was suspected of.

### 1.3 Version, for the record

`codex-cli 0.147.0` (npm shim `%APPDATA%\npm\codex.cmd`). `~/.codex/version.json`:
`{"latest_version":"0.149.1", … ,"dismissed_version":"0.149.1"}`. **Not upgraded** (card item 4).
The probe answers the in-TUI "Update available" modal with `2` (not Enter) precisely so a canary can
never upgrade the CLI out from under a session.

---

## 2. Item 2 — the suppression mechanism exists, works, and is not being wired in

### 2.1 The real mechanism (confirmed against the CLI, not guessed)

Two different subsystems produce the two servers, so it takes two switches:

- **`node_repl`** is a genuine external MCP server, declared in `~/.codex/config.toml` under
  `[mcp_servers.node_repl]` by the Codex **Desktop app** (its command is
  `…\OpenAI\Codex\runtimes\cua_node\…\node_repl.exe`). `codex mcp get node_repl` prints
  `enabled: true` and `startup_timeout_sec: 120`, so the key exists:
  **`-c mcp_servers.node_repl.enabled=false`**.
- **`codex_apps`** is not in `mcp_servers` at all (`codex mcp list` shows only `node_repl`). It is
  the plugin/apps surface — `codex features list` reports `apps  stable  true`, and `codex --help`
  documents `--disable <FEATURE>` as "Equivalent to `-c features.<name>=false`":
  **`--disable apps`**.

### 2.2 It verifiably works

With both applied, the boot status did not appear in **3/3** launches (§1.2, row 3), against 4/6
baseline launches where it did. The composer still accepted typed input in all three.

### 2.3 Why it is NOT being wired into `CodexLaunchArgs` today

1. **The card's precondition is false.** It says "If it is a real, recurring hang: investigate
   whether Antiphon's Codex launch args can suppress…". It is recurring but it is not a hang. The
   whole benefit is ~1–3 s off a launch that already waits 30 s for delivery confirmation.
2. **The risk is not zero and is not measured.** `--disable apps` turns off the entire plugin
   surface (`browser`, `documents`, `pdf`, `spreadsheets`, `presentations`, `chrome`,
   `computer-use`, … all currently `enabled = true` in `~/.codex/config.toml`), and `node_repl` is
   the runtime behind Codex's JS/browser tooling. Whether a Codex *code* delegate ever reaches for
   any of that is unknown; nothing here measured it. Trading an unmeasured capability for three
   seconds is the wrong side of that trade.
3. **Which flag does what was not isolated.** The two were only ever tested together.
4. It would also mean Antiphon overriding the operator's own `~/.codex/config.toml` on every
   delegate launch — a policy decision, not a bug fix.

**Recommendation:** file a follow-up card *only* if launch latency ever becomes the complaint. It
would need: each flag isolated, a Codex delegate run end-to-end with them on, and a decision about
overriding operator config. The two-line change itself is trivial (`CodexLaunchArgs` already exists
for exactly this kind of `-c` argument, and both launch sites — `AgentTaskDispatcher.BuildLaunchSpec`
and `AgentControlService` — share it).

---

## 3. Item 3 — the swallowed transcript fault (implemented)

### 3.1 What actually threw

The card reports `server/logs/antiphon-20260825.log:89212` as "logged … with no underlying exception
message". The message *line* carries no cause, but Serilog did write the exception on the following
lines — line 89213 onward is the full stack, and it names the failure exactly:

```
Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes.
 ---> Npgsql.PostgresException (0x80004005): 23503: insert or update on table "AgentIncidents"
      violates foreign key constraint "FK_AgentIncidents_Agents_AgentId"
```

So: `SessionOwnerLookup.ResolveOwningAgentIdAsync` returned an agent id, and that agent row **did
not exist**.

Confirmed directly against the live database for this exact session:

```
SELECT t."Id", t."AgentId", (a."Id" IS NOT NULL) AS agent_exists …
 5a458f99-… | e54d3d20-3326-4d0f-8d86-06724f13190f | f
```

### 3.2 Why, and why it is not a rare race

`AgentTask.AgentId` is a bare `Guid?` with **no foreign key** to `Agents` (`AppDbContext.cs`, the
`AgentTask` block declares FKs for `ParentTaskId` and `ProjectId` and none for `AgentId`) —
deliberately, so retiring a warm delegate or reaping an ephemeral one does not cascade the
delegation history away. `AgentIncident.AgentId`, by contrast, is a **required FK with cascade
delete**.

The consequence is that a settled delegate task normally holds a dangling `AgentId`. Measured
2026-08-25 on the dev database:

```
dangling: 447        of        539 tasks with a non-null AgentId
```

**83 % of them.** So this is the ordinary end state of a finished delegate, not a cleanup race — any
transcript fault (or `DeliveryUnverified` incident) raised for a settled delegate session was
guaranteed to throw and be swallowed. It fired seven times in the retained logs: six on 2026-08-21
and the CARD-0194 one on 2026-08-25.

### 3.3 The fix

**`server/Application/Services/SessionOwnerLookup.cs`** — the task arm now requires the agent to
still exist:

```csharp
return await db.AgentTasks
    .Where(t => t.AgentSessionId == sessionId
        && t.AgentId != null
        && db.Agents.Any(a => a.Id == t.AgentId))
    .OrderByDescending(t => t.DispatchedAt ?? t.CreatedAt)
    .Select(t => t.AgentId)
    .FirstOrDefaultAsync(ct);
```

Strictly narrowing, and fixed once rather than at each of the three call sites
(`TranscriptBindingIncidentService` ×2, `SessionMessageQueueService.RecordDeliveryUnverifiedAsync`) —
a dead id is never a usable answer for any of them. The callers' existing "nobody owns this session"
branch then raises the standalone alert CARD-0101 added, so the fault reaches a surface instead of
disappearing.

**`server/Application/Services/TranscriptBindingIncidentService.cs`** — the backstop, for whatever
breaks the write next:

- The bare `LogWarning(ex, "Recording a transcript fault for session {SessionId} failed")` becomes
  `ReportWriteFailureAsync`, which logs at **Error** and names what the database said via
  `AgentService.DescribeDbFailure` (AGENTS.md: *never report a DB failure without the DB's own
  message*; CARD-0056's precedent).
- The fault is then raised as a **standalone alert** in a fresh scope (the scope that threw may hold
  a poisoned change tracker), under its own dedup key `…:unwritable:{sessionId}` so it never dedups
  against the `…:unclaimed:` one — they need different fixes. The alert detail is clipped to 3 900
  chars, because `Alerts.Detail` is `varchar(4000)` too and a backstop that dies on the same
  oversize value is not a backstop.
- The backstop's own failure is caught and logged; it can never take down its caller.
- `OnHeuristicBindAsync` gets the same cause-naming at Warning (it is an Info timeline row with no
  alert; losing it degrades the record rather than hiding a live fault).

### 3.4 Tests

`tests/Antiphon.Tests/Application/TranscriptBindingIncidentTests.cs`, two added:

- `Fault_for_a_task_whose_agent_row_is_gone_alerts_instead_of_swallowing_a_foreign_key_error` — the
  CARD-0194 shape exactly: a task row pointing at an agent id that was never inserted. Asserts no
  incident row, **no** caught-and-logged failure, and the unclaimed standalone alert. Red before the
  `SessionOwnerLookup` change (23503, swallowed).
- `A_fault_whose_incident_cannot_be_written_names_the_database_error_and_still_alerts` — forces a
  different failure (an over-length `Message`, `varchar(4000)`, nothing truncates it) and asserts the
  Error log carries Postgres's own `22001` and that the `…:unwritable:` alert exists.

`TranscriptBindingIncidentTests`: **15/15 pass.**

---

## 4. What is still open — and it is not the MCP bootstrap

Session `8be1afc5`'s actual failure, from three independent logs:

1. The boot prompt was typed and **rendered in full and correctly** in the composer (ansi log).
2. `SessionMessageQueueService` pressed Enter once and polled 30 s
   (`antiphon-20260825.log:85372`, `…confirmed by degraded screen-only verdict after 30s with no
   transcript row … 1 Enter(s) sent`).
3. **The pty produced not one further byte** — the ansi log's last write is 03:40 BST and the
   session was killed at 03:50. Not a repaint, not a spinner, not a newline insertion.
4. No rollout was ever created — `session-runner-20260825.log`: *"the child exited without ever
   producing a Codex rollout we could identify … although input was delivered to it."*

That is Enter reaching a Codex TUI that neither submitted nor inserted a newline, with the composer
holding the body. It is the same family as **CARD-0190** ("Codex never binds without a prompt") and
should be tracked there rather than under this card, whose stated hypothesis it falsifies. Note also
that `codex.exe` PID 13816 has been alive since **2026-08-17 21:59** with an open rollout — that
stale rollout is the C3-refused candidate in every one of these runner-log lines, and leaked Codex
processes are worth a card of their own.

## 5. Known-red, inherited

`SessionMessageQueueDeliveryVerificationTests` — `Verified_delivery_types_body_then_submits_and_leaves_no_incident`
and `Claude_auto_compact_still_enqueues_and_delivers_through_the_normal_path` fail (2 of 86). Both
were re-run at the base commit with these changes stashed and **fail there identically**
(`ShouldAssertException: await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)`).
Pre-existing, not touched here.
