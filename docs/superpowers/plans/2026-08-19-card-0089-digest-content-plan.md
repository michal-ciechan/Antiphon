# CARD-0089 — The fallback check-in digest's CONTENT: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0089 (`ee6a9223-fe5e-49c2-aa68-4e9e54643605`) — the raw check digest is noisy and
stale: byte-identical already-PARKED incidents re-printed every check with no timestamp, a
transcript tail of bare tool names, and a `/compact` handoff rendering identically to the task's
own brief.
**Precedent:** CARD-0047 slice 2 (`DelegateCheckProbe` — the digest is the FLOOR, deliverable when
the interpreter is down), CARD-0055 (parking: still Pending, `DeliveryAttempts >= MaxDeliveryAttempts`,
nothing retries it), CARD-0035 slice 5 (`AgentTaskEvent` of type `Check` stores the digest).
**Do not steal:** CARD-0074 owns the digest's own **capture time** and reconcile-at-delivery.
CARD-0079 owns **why** the interpreter was unavailable (fixed today: `3da054c`, `43c9d25`).

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**All four complaints are real, all four are in ONE file, and none of them needs a migration.**

Everything the card describes is rendered by `DelegateCheckProbe.RenderDigest`
(`server/Application/Services/DelegateCheckProbe.cs:310-422`) from facts gathered by
`GatherAsync` (`:149`). The data the card wants already exists and is already read, or is one
`AsNoTracking` query away:

| What the card wants | Where it already is |
|---|---|
| incident timestamps | `CheckIncident.CreatedAt` is **gathered** (`:134`, `:251-260`) and simply never rendered (`:416-419`) |
| "new since the last check" | `AgentTaskEvents` where `Type == Check` carries `At`, written **after** the digest is gathered (`AgentTaskCheckService.cs:169-176`), so `MAX(At)` at gather time *is* the previous check |
| parked-ness of a queued message | `SessionQueuedMessage.DeliveryAttempts` + `LastDeliveryStartedAt`, against `SupervisionSettings.DeliveryVerification.MaxDeliveryAttempts` (precedent: `AttentionService.cs:422`) |
| tool call arguments | `TranscriptEntry.ToolInput` — raw JSON, already truncated at ingestion — is not even selected today (`:219`) |
| control-plane vs brief | both are `QueuedMessageOrigin.Delegation` (`AgentTaskDispatcher.cs:1605` and `:1618`), but the `/compact` body **begins with `/`** and a brief never can |

So this is **ONE Code slice**: `DelegateCheckProbe.cs`, its test class, and one constant bump in
`AgentTaskCheckService.cs` that the change would otherwise regress (§3.5). Do not split it.

The read-only guarantee in the class doc (`:22-28`) survives: the one new dependency is
`IOptions<SupervisionSettings>`, which is configuration, not a write surface. Say so in that
paragraph when adding it — it is load-bearing, and a reviewer must not have to re-derive it.

---

## 1. What to decide — the card's four questions

### Q1. Should already-PARKED delivery failures stop repeating after the first check? — **No. Do not suppress.**

Suppression is the wrong shape three ways:

1. **The digest is the floor, not a feed.** CARD-0047's whole premise is that a reader who sees
   only *this* check must be able to act on it. An orchestrator that takes over at check #4, or a
   human reading the card thread, would see a session with a parked message and no sign of it.
2. **It needs state that does not exist** — a per-incident "already shown" watermark, i.e. a column
   and a migration, bought in exchange for an omission.
3. **It treats a symptom.** The complaint is not "I saw it twice", it is "I could not tell it was
   old **without diffing two check messages by hand**". That is a rendering defect, and rendering
   is where it should be fixed.

Three cheaper changes dissolve it completely, and they generalise to every incident kind rather
than special-casing `DeliveryVerificationFailed`:

- **age on every incident** (Q2), so staleness is legible in the line itself;
- **a NEW-since-your-last-check split**, exact and free (`AgentTaskEvents`, above), so
  "3 incidents, none new since check #2 at 15:12Z" is a header, not an inference;
- **collapse byte-identical incidents** into one line with `×N` — the card's own example had two
  identical `returned to the queue` lines, so that is two thirds of the noise gone within a
  single check.

And the parked fact itself moves to where it is **actionable**: the `DELEGATE QUEUE` block, on the
row that is parked (§3.3). An incident is a past event; a parked message is a present condition.
Today the digest states the past event and stays silent about the present condition.

### Q2. Should incidents carry a timestamp? — **Yes. Absolute UTC and relative age, both.**

`CreatedAt` is already on `CheckIncident` and already gathered. Rendering it is two lines of
`StringBuilder`. Absolute is what correlates with logs and with the other agent's transcript;
relative is what a reader acts on. The digest already pairs them this way for the task row
(`dispatched=<u> elapsed=<n>m`), so this is consistency, not a new convention.

Explicitly **not** the digest's capture time — that is CARD-0074, and this change must not
pre-empt or contradict it. Incident age is measured against `facts.At`, which is the same clock
CARD-0074 will later expose; when it does, these ages stay correct by construction.

### Q3. Should the transcript tail show more than bare tool names? — **Yes, and the repeat-collapse matters more than the arguments.**

Two changes, in order of value:

1. **Collapse consecutive identical entries** into `×N`. `#101 ToolCall: read_file` ten times and
   `#101 ToolCall: read_file ×10` carry the same information, but only the second one *answers the
   question the card is asking* — productive investigation vs a stuck loop. It also buys back the
   budget the arguments cost.
2. **Show a truncated `ToolInput`.** Flatten to one line, truncate at a new
   `ToolInputChars = 120`. Ten `Read` calls against ten different files and ten against the same
   file are indistinguishable today and unmistakable after.

Deliberately simple: **no JSON parsing, no key-preference list.** `ToolInput` is already truncated
at ingestion (`TranscriptNormalizer`), the first key of a real tool call is the identifying one in
practice (`file_path`, `command`, `pattern`), and a parser is a thing that can throw inside a probe
whose entire value is that it always answers. Also add an `ERROR` marker for
`ToolIsError == true` on `ToolResult` rows — a failing tool call repeated is the strongest stuck
signal there is, and the flag is already stored.

**Exposure note, so it is a decision and not an accident:** tool inputs will now appear in a body
typed into the caller's terminal and stored in `AgentTaskEvent.Detail`. This is not a new class of
exposure — `ToolResult` text has been rendered at 200 chars since CARD-0047 — and 120 chars of a
tool's *input* is a smaller window than the 200 chars of its *output* already carried.

### Q4. Should a control-plane message be distinguished from the task's own brief? — **Yes, and the rule is structural, not a heuristic.**

`Origin` already separates `Ui` / `Channel` / `System` / `Check` / `Supervision`. The only
conflation is inside `Delegation`, which carries both the `/compact` warm-pool handoff
(`AgentTaskDispatcher.cs:1605`) and the brief itself (`:1618`). Thirteen lines apart, same enum value.

The discriminator: **a body whose first non-whitespace character is `/` is a slash command, and a
slash command is plumbing.** A brief is prose built by `FitBriefForTyping`; when CARD-0025 spills
it, the pointer begins `YOUR MESSAGE IS NOT IN THIS MESSAGE.` (`TypedBodySpill.cs:21`), never `/`.
Checked, not assumed — a spilled brief must not be mislabelled as plumbing.

Classification, keeping `Origin` visible in every case so nothing is *replaced* by a label:

| Row | Label |
|---|---|
| `Delegation`, body does not start with `/` | `BRIEF` |
| `Delegation`, body starts with `/` | `control-plane` |
| `System`, `Supervision`, `Check` | `control-plane` |
| `Ui`, `Channel` | `human` |

### Q5 (the card's overlap question). Does this still matter once CARD-0079 ships? — **Yes, and CARD-0079 has already shipped without touching any of it.**

CARD-0079's slices landed today (`3da054c` occupancy/settlement, `43c9d25` the
`CheckInterpreterUnavailable` incident) and explicitly **rejected** withholding the digest
("the specialist is garnish, the digest is the floor"). Neither commit touches
`DelegateCheckProbe`. And the interpreter reads *this same digest* as its input, so incident
staleness and a bare-name transcript tail degrade the interpreted reading too — a specialist told
"3 Error incidents" with no ages will escalate a 40-minute-old parked message exactly as a human
would.

---

## 2. Rejected

- **A "shown before" flag per incident** (Q1's literal reading) — a column and a migration, to buy
  an omission from the surface that exists to omit nothing.
- **`AgentTask.LastCheckAt`** — considered and dropped: `AgentTaskEvents` `Type == Check` already
  answers it exactly, with no schema change.
- **Parsing `ToolInput` into named arguments** — a throw inside the probe, for cosmetics.
- **Raising `TranscriptTailSize` past 10** — the card asks for richer lines, not more of them; the
  repeat-collapse already widens the effective window over a looping session.
- **Dropping incidents older than N minutes** — same objection as Q1: the floor shows everything.
- **Any change to `HeaderPrefix`, the `(unverified digest — …)` line, or `InterpreterDownMarker`** —
  settlement, the client, and `AgentTaskCheckInterpreterTests` all key on them.

---

## 3. The slice — a dated, de-duplicated, labelled digest (Code)

All in `server/Application/Services/DelegateCheckProbe.cs` unless stated.

### 3.1 Gathering

- `GatherAsync`: read `previousCheckAt` —
  `AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Check).MaxAsync(e => (DateTime?)e.At, ct)`.
  Null on the first check. Carry it on `CheckFacts`.
- `CheckTranscriptLine`: add `string? ToolInput` and `bool? IsError`; select `e.ToolInput` and
  `e.ToolIsError` in `GatherTranscriptTailAsync` (`:219`).
- `CheckQueuedMessage`: add `int DeliveryAttempts`, `DateTime? LastDeliveryStartedAt`, `bool Parked`.
  `Parked` is computed in the probe from
  `Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts)` — read the cap, never
  re-define it (`AttentionService.cs:422` is the precedent).
- Constructor takes `IOptions<SupervisionSettings>`; update the read-only paragraph in the class
  doc to name it as configuration.

### 3.2 Incident block

```
INCIDENTS: 3 on this session — none NEW since check #2 (15:12:04Z, 23m ago):
  · 14:47:31Z (28m ago)  Error DeliveryVerificationFailed: Message delivery could not be verified…
  · 14:47:12Z (28m ago)  Error DeliveryVerificationFailed ×2: …The message has been returned to the queue.
```

- Collapse on `(Severity, Kind, Excerpt(Message))` — byte-identical only; render `×N` and the
  **newest** timestamp of the group.
- Header arms: no previous check → `(first check — all are new to you)`; some newer than
  `previousCheckAt` → `N NEW since check #<n-1> (<time>)`, with those lines prefixed `NEW `; none
  newer → `none NEW since …`.
- `IncidentLimit` stays 5 — but apply it **after** collapsing, so five *distinct* incidents survive
  where five identical ones used to fill the block.

### 3.3 Queue block

```
DELEGATE QUEUE: 2 message(s) still Pending:
  · #3 BRIEF (Delegation, 41m old) PARKED 3/3 attempts, last tried 38m ago: CARD-0089: read the…
  · #2 control-plane (Delegation, 41m old): /compact This session is being handed NEW, unrelated…
```

Parked rows first, then sequence order — the row nothing will ever retry is the one that decides
what the reader does next.

### 3.4 Transcript tail

```
TRANSCRIPT TAIL (last 10):
  #101 ToolCall Read ×4: {"file_path":"C:\\src\\Antiphon\\server\\Application\\Services\\Delegate…
  #105 ToolResult ERROR: The file does not exist.
```

- Collapse **consecutive** entries identical on `(Kind, ToolName, rendered detail)`; show the first
  sequence number and `×N`.
- `ToolInput` flattened through the existing `Excerpt` shape at a new `ToolInputChars = 120`.
- `ERROR` marker from `IsError == true`.
- A row with no `ToolInput` (older rows, non-Claude sessions) renders exactly as today — the change
  must degrade to the current output, never to a blank.

### 3.5 The one change outside the probe

`AgentTaskCheckService.EventDetailChars` is **900** (`:35`) and `ComposeEventDetail` stores the
digest's HEAD 900 chars; `CardThreadService` then shows the **last 6 lines** of that
(`CheckDigestTailLines`, `:48`). The digest already exceeds 900, so growing per-line content moves
which section the card-thread tail lands in. Raise `EventDetailChars` to **1800** in the same slice
and say why in the commit. This is not scope creep — leaving it regresses an existing surface.

The typed note itself is not at risk: growth is bounded (≤ 120 chars × 10 tail lines, ≤ ~40 chars
× 5 incidents, ≤ ~45 chars × 5 queue rows ≈ +1.6 KB worst case) and the collapse gives most of it
back on exactly the sessions that would otherwise be largest. It stays far under the modern
14 400-char reply ceiling, and CARD-0025's spill is the backstop below that.

### 3.6 Tests

`tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs` — existing class, existing
`SeedAsync` / `SeedTranscriptAsync` / `SeedPendingMessageAsync` / `SeedIncidentAsync` helpers.
Extend those seeders for `ToolInput` / `ToolIsError` / `DeliveryAttempts` rather than adding new ones.

1. `an_incident_carries_its_time_and_age` — a 28-minute-old incident renders both, and the digest
   does **not** render it as if it were current.
2. `incidents_older_than_the_previous_check_are_not_marked_new` — seed a `Check` `AgentTaskEvent`
   after the incidents; the header says none NEW. The card's exact shape.
3. `an_incident_after_the_previous_check_is_marked_new` — the other arm.
4. `the_first_check_says_every_incident_is_new_to_the_reader` — `previousCheckAt` null.
5. `identical_incidents_collapse_to_one_line_with_a_count` — two byte-identical rows → one `×2`.
6. `a_parked_message_says_so_in_the_queue_block` — `DeliveryAttempts == MaxDeliveryAttempts`
   renders `PARKED` with the cap read from settings, and sorts first.
7. `a_slash_command_is_labelled_control_plane_and_the_brief_is_not` — one `/compact` Delegation row
   and one prose Delegation row; the labels differ.
8. `a_spilled_brief_is_still_labelled_brief` — body starts with `TypedBodySpill.PointerHeadline`.
   The CARD-0025 regression guard; without it Q4's rule is a heuristic.
9. `a_tool_call_shows_its_input`, and `a_tool_call_without_input_renders_as_it_does_today`.
10. `a_repeated_tool_call_collapses_to_one_line_with_a_count` — the stuck-loop signal.
11. `a_failed_tool_result_is_marked` — `ToolIsError == true`.

Plus, wherever `ComposeEventDetail` is covered: the 1800-char budget still truncates and still puts
the interpreter's reading above the digest.

```
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*DelegateCheckProbeTests/* --property:OutputPath=bin-card0089/
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*AgentTaskCheck*/* --property:OutputPath=bin-card0089/
```

Forward slash on `OutputPath`; delete `bin-card0089/` afterwards
(`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0089 | Remove-Item -Recurse -Force`).
`DelegateCheckProbeTests` is `[NotInParallel("AgentQueue")]`. Do not co-schedule with
`Antiphon.Agents.Pty.Tests`.

---

## 4. Out of scope

- CARD-0074: the digest's capture time, reconcile-at-delivery, superseded marking.
- CARD-0079: why the interpreter was down; the `CheckInterpreterUnavailable` incident; `BuildNote`'s
  header, `HeaderPrefix`, or the `(unverified digest — …)` line.
- The interpreter's prompt. It reads the digest; a better digest is the whole delivery.
- The client. `CardThreadCheckDto` already distinguishes reading from digest and needs no change.
- Any new incident kind, any migration, any change to when checks fire or how many there are.

---

## 5. Commit

`fix(delegation): CARD-0089 - date, de-duplicate and label the check-in digest`
