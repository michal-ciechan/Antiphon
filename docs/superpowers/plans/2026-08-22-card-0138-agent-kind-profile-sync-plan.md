# CARD-0138 — an agent's `Kind` may disagree with its TUI profile's `Kind`: plan

**Date:** 2026-08-22 · **Card:** CARD-0138 (`612c1673-ff08-4c74-83f8-0fe908b0b211`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `1faa2fb`; every line/behaviour claim below was read out of the code on
that commit, and every row count was read out of the live dev database (`antiphon-postgres`, port
17280) on 2026-08-22.

---

## Verdict

The card's *observation* is real and reproduces in the live database. The card's *stated
consequence* is not, and the plan has to say so plainly before it designs anything, because the
whole shape of the fix turns on it.

1. **The mismatch is real, and it is not drift — it is a column the user-agent write path has never
   written.** `CreateAgentRequest` and `UpdateAgentRequest` (`server/Application/Dtos/AgentDtos.cs:170`,
   `:204`) have **no `Kind` field at all**, and neither does the client
   (`client/src/api/agents.ts:241`, `:263`). `AgentService.ApplyTuiSelectionAsync`
   (`server/Application/Services/AgentService.cs:935`) — the one place a `TuiProfileId` is persisted
   for a user-created agent, called from `CreateAsync` (`:273`) and `UpdateAsync` (`:373`) — sets
   `agent.TuiProfileId` (`:986`) and `agent.ModelId` (`:987`) and **nothing else**. So
   `Agents.Kind` on a user-created agent is *always* the entity initializer's `AgentKind.ClaudeCode`
   (`server/Domain/Entities/Agent.cs:101`) backed by the column default
   (`server/Infrastructure/Data/AppDbContext.cs:776`), no matter which profile is attached. The
   answer to the card's root-cause question — "does the creation/edit path ever sync `Kind` from the
   profile today?" — is **never, and it has no way to: there is no `Kind` on the wire, in either
   direction.**

2. **The launch-argument composer does NOT branch on `agent.Kind`, and never has.**
   `AgentControlService.StartInteractiveSessionAsync` derives `isClaudeCode`/`isGrok`/`isCodex`
   (`server/Application/Services/AgentControlService.cs:162`, `:163`, `:170`) from
   `PeekProfileKindAsync` (`:161`, defined `:377`), which reads **the attached profile's own
   `Kind`** first, then the installation-default profile's, then the legacy registry definition's.
   `agent.Kind` is not consulted. `git log -S "agent.Kind" -- server/Application/Services/AgentControlService.cs`
   returns **nothing**: that file has never referenced the column. So the flag choice at `:251-253`
   (`CodexLaunchArgs.ConfigFlag` + `DeveloperInstructions` / `--rules` / `--append-system-prompt`)
   is already correct for the live `Codex` agent, and the card's "a second, independent way this
   agent's launch was broken" is not so. The one broken thing about that launch was the profile's
   own pwsh `-Command`/`-File` issue (CARD-0136/CARD-0137), exactly as the card warns not to
   conflate. The same holds for the card path (`CardService.cs:596-615` → `AgentLaunchResolution`)
   and for the session row itself (`AgentControlService.cs:328` and `OrchestratorService.cs:127`,
   `:155` all stamp `AgentKind = spec.Kind`, and `spec.Kind` **is** `profile.Kind` —
   `AgentTuiLaunchResolver.cs:375`).

3. **`Agents.Kind` has exactly one writer and three readers, and all three readers are
   pool-delegate-only.** Whole-server grep for reads/writes of the property:
   - write — `AgentTaskDispatcher.ResolveAgentAsync:1653` `existing.Kind = task.AgentKind`
     (and the fresh-delegate initializer `:1675`);
   - read — `TryReuseWarmAgentAsync:1802` (`pinned.Kind != claimed.AgentKind`, reached only after
     `:1793` has already returned for `!pinned.IsPoolDelegate`);
   - read — the warm-pool predicate `:1843` (`a.IsPoolDelegate && … a.Kind == claimed.AgentKind`);
   - read — `RetireIdleWarmAgentsAsync:2140` (grouping warm rows by directory+kind).

   It is exposed in **no DTO** (`AgentSummaryDto`/`AgentDetailDto` carry no `Kind`) and therefore on
   no screen. The entity's own XML doc already says this out loud
   (`server/Domain/Entities/Agent.cs:96-99`: *"only the dispatcher writes it today, so a
   user-created agent keeps the default and the pool keeps ignoring it"*), and so does
   `AgentTaskDispatcher.cs:1941` (*"nothing but the dispatcher writes `Agent.Kind`, so a user's Grok
   agent still reads ClaudeCode"*).

**So the defect to fix is not a wrong flag today. It is a column on the `Agents` table, named
`Kind`, that reads `ClaudeCode` for a Codex agent and a Grok agent, with two live writers that can
make it lie and no invariant anywhere that says it must not.** That is a trap, not an outage — and
the plan below is sized as a trap-closing change, with the two genuine *latent* wrong-launch routes
it also closes named explicitly in §"Two routes that really do launch the wrong program".

---

## The audit, run for real

```sql
SELECT a."Id", a."Name", a."Kind" AS agent_kind, p."Kind" AS profile_kind,
       p."SourceDefinitionName", a."ModelId", a."IsPoolDelegate"
FROM   "Agents" a
JOIN   "AgentTuiProfiles" p ON p."Id" = a."TuiProfileId"
WHERE  a."Kind" <> p."Kind"
ORDER  BY a."Name";
```

**Result — 2 rows, both standing user agents, neither a pool delegate:**

| Id | Name | agent_kind | profile_kind | profile | ModelId |
|----|------|-----------|--------------|---------|---------|
| `06a847ea-e300-4ce6-9ba6-a917aac64888` | Codex | 1 (ClaudeCode) | 2 (Codex) | `codex` | `gpt-5.6-terra` |
| `cbbb38fc-2c39-42db-913c-7093b58c2a1f` | Grok 4.6 | 1 (ClaudeCode) | 4 (Grok) | `grok` | `grok-4.6` |

The card names the first. The second — **`Grok 4.6`, `Status = Running` at audit time** — is new and
was not in the card. Both are exactly the shape §Verdict 1 predicts: every agent ever created
through the API carries `Kind = 1`, so the audit's answer is fully determined by "which agents were
given a non-Claude profile", and it will be for every agent created from now on too.

The remaining 24 rows agree only because their profile is the `claude` one (11 standing agents and
11 pool delegates) or because they have no profile at all (2 pool delegates). **No pool delegate
carries a mismatch today** — which matters, because for a pool delegate the column is load-bearing
and a "fix" that rewrote it would be the actual outage.

---

## Two routes that really do launch the wrong program

Neither is what the card describes, both are real on `1faa2fb`, and the fix has to account for both
or it makes one of them worse.

### R1 — the importer's backfill hands pool delegates a profile that contradicts their kind

`AgentTuiProfileImporter.BackfillAgentsAsync` (`server/Application/Services/AgentTuiProfileImporter.cs:267`)
runs on **every** server start (`AgentTuiSettings.ImportProfilesOnStartup` defaults `true`,
`server/Application/Settings/AgentTuiSettings.cs:22`; called from `Program.cs:526`) and assigns the
**installation-default** profile to *every* agent whose `TuiProfileId` is null (`:271-276`) — with no
`IsPoolDelegate` filter. The live database shows this has already happened: 11 `task-*` pool rows
created on or before 2026-08-20 carry `TuiProfileId = 904d6f65…` (the `claude` profile), which
nothing in `AgentTaskDispatcher` ever put there.

Today that is inert, because a pool delegate's launch goes through
`AgentTaskDispatcher.BuildLaunchSpec:1522` → `_agentRegistry.Resolve(DefinitionNameForKind(kind))`
(`:1600-1603`) — the **appsettings registry keyed by the session's kind**, never the profile. But
`task-01f75022` exists right now with `Kind = 4` (Grok) and `TuiProfileId = NULL`: at the next
server restart the backfill gives it the `claude` profile, producing a **third** mismatch row that
Antiphon wrote itself. And if an operator presses Start on that row in the UI,
`AgentControlService.PeekProfileKindAsync` reads the `claude` profile and composes
`--append-system-prompt` for a Grok process — the card's feared failure, arriving by a route the
card does not describe.

### R2 — the dispatcher restamps a standing agent's `Kind`, and launches it off the registry, ignoring its profile

`AgentTaskDispatcher.ResolveAgentAsync:1641` writes `existing.Kind = task.AgentKind` for **any**
pinned agent (`:1653`), pool or standing. A task pinned to a standing agent reaches the spawn path
whenever `PlaceOnStandingAgentAsync:1934` finds no live session on a non-`AlwaysOn` agent (`:1937-1938`).
`AgentTask.AgentKind` defaults to `ClaudeCode` when the caller omits it
(`AgentTaskDtos.cs:18-23`), so delegating one task to the `Codex` agent while it is stopped would
(a) stamp its row `Kind = ClaudeCode` — undoing any sync fix — and (b) launch it via
`DefinitionNameForKind(ClaudeCode)`, i.e. **`claude` in the Codex agent's directory, its Codex
profile ignored entirely**.

R2's second half (the dispatcher's spawn path ignoring a standing agent's profile) is a larger
defect than CARD-0138 and is **explicitly out of scope** — see §"Deliberately not done", where it is
written up for its own card. The half that *is* in scope is the restamp, because it is a writer of
the column this card is about.

---

## Design decisions

### D1 — the invariant

> **If `Agent.TuiProfileId` is not null, `Agent.Kind` equals that profile's `Kind`.
> If it is null, `Agent.Kind` is the row's own truth and nothing derives it.**

Both halves are needed. The second half is what keeps the fix from breaking the warm pool: a fresh
pool delegate is born with `Kind = task.AgentKind` and **no profile**
(`AgentTaskDispatcher.cs:1663-1680` (`Kind` at `:1675`)), and its kind is the only fact standing between a Grok task and
a warm Claude process (`:1843`). Any rule of the form "derive `Kind` from the profile, always" would
have to answer "from *which* profile?" for a row that has none, and the honest answer — the
installation default — is `ClaudeCode`, which is precisely the value that would let a Grok task
claim a Claude delegate. So `Kind` **cannot** simply be deleted or made always-derived: it is
load-bearing exactly where `TuiProfileId` is null.

### D2 — the fix shape: sync from the profile at every write, not reject-on-conflict

The card offers three shapes. Two are unavailable and one is right:

- **Reject a request whose explicit `Kind` conflicts with the chosen profile** — *impossible*. No
  request carries a `Kind` (§Verdict 1). Adding a `Kind` field to `CreateAgentRequest`/
  `UpdateAgentRequest` purely so it can be validated against the profile would invent the
  independently-settable field the card is worried about, in order to police it.
- **Drop the column / always derive** — *rejected*, D1.
- **Sync `Kind` from the profile whenever `TuiProfileId` is set or changed** — *chosen*. It is the
  only shape that is a strict repair: it makes an already-untouched field truthful, changes no API
  surface, changes no UI, and by construction cannot change any row where the two already agree
  (24 of 26 live rows, and every pool delegate).

### D3 — four write paths, one helper

Syncing at `ApplyTuiSelectionAsync` alone leaves three writers able to re-break the invariant, so
"the one or two write paths that matter" is really four. All four take the same one-line rule
(`agent.Kind = profile.Kind`), and the plan introduces `AgentProfileKind.Sync(Agent, AgentTuiProfile)`
(a static in `server/Application/Services/`) so the rule is stated once and greppable:

| # | Path | Change |
|---|------|--------|
| W1 | `AgentService.ApplyTuiSelectionAsync:986` (create + edit) | set `agent.Kind = profile.Kind` alongside `TuiProfileId`. Also in the `profile is null` early-return at `:957-961`, which clears `TuiProfileId` — leave `Kind` alone there (D1's second half). |
| W2 | `AgentTuiProfileImporter.BackfillAgentsAsync:276` | **skip pool delegates** (`.Where(a => a.TuiProfileId == null && !a.IsPoolDelegate)`), and set `Kind` from `installationDefault.Kind` for the rest. Closes R1. |
| W3 | `AgentTuiProfileService.UpdateAsync:387` (`profile.Kind = request.Kind`) | when the kind actually changes, `ExecuteUpdateAsync` every agent with that `TuiProfileId` to the new kind. A profile edit is the only way an *existing* pair can drift after W1. |
| W4 | `AgentTaskDispatcher.ResolveAgentAsync:1653` (`existing.Kind = task.AgentKind`) | guard with `if (existing.IsPoolDelegate)`. Closes the restamp half of R2. The comment above it already says the row must follow the session "or the pool would go on offering it as the kind it used to be" — that reason is pool-only, and the line was pool-only in intent. |

W2's pool filter is not cosmetic: without it, W2's own sync would set a Grok delegate's `Kind` to
`ClaudeCode` and hand its warm slot to Claude tasks. This is the single most dangerous edit in the
card and it needs its own test (T5).

### D4 — no database constraint

Rejected, for a reason stronger than "cross-table is awkward". The clean relational form does exist:
a `UNIQUE (Id, Kind)` index on `AgentTuiProfiles` plus a composite
`FOREIGN KEY (TuiProfileId, Kind) REFERENCES AgentTuiProfiles (Id, Kind)`. Postgres `MATCH SIMPLE`
means a NULL `TuiProfileId` satisfies it regardless of `Kind`, which is D1 exactly, and
`ON UPDATE CASCADE` would make W3 automatic. It is genuinely attractive and is written down here so
the next reader does not have to rediscover it.

It is not what this card should ship, because of what it does on violation: `ResolveAgentAsync`'s
restamp (W4) currently writes a wrong-but-harmless value on a standing agent, and under the FK it
would instead throw `23503` inside the dispatch transaction — turning a silent lie into a failed
dispatch, in a code path whose failure mode is a delegate that never launches. Land the application
invariant and its tests first; the FK is then a cheap, separately-revertible hardening slice once
W1–W4 have been running long enough that a violation genuinely means a bug. Recorded as a follow-up,
not scheduled here.

### D5 — what to do about the two rows that are already wrong

Backfill them, in a migration, and change nothing else about them.

```sql
UPDATE "Agents" a
SET    "Kind" = p."Kind"
FROM   "AgentTuiProfiles" p
WHERE  p."Id" = a."TuiProfileId"
  AND  a."Kind" <> p."Kind"
  AND  NOT a."IsPoolDelegate";
```

- **`NOT IsPoolDelegate`** for the same reason as W2, and it costs nothing today (the audit shows
  zero mismatched pool rows) — it is there so a *future* backfilled pool row cannot be rewritten by
  a re-run.
- **No session is touched, no agent is restarted.** The two rows' *live* launches are already
  correct (§Verdict 2), so there is nothing wrong in flight to repair; writing the column is the
  whole remedy. `Grok 4.6` is Running at audit time and must stay running.
- **Nothing needs re-launching afterwards either.** The only readers are pool-only (§Verdict 3), so
  the corrected value changes no behaviour for these two rows at all. That is the honest answer to
  the card's "what should be done about already-launched agents that are silently wrong today":
  **nothing beyond the column**, and the plan should not manufacture a remediation the evidence does
  not support.

Migration name: `20260822HHMMSS_SyncAgentKindWithTuiProfile`, hand-written with no `.Designer.cs`,
matching `20260731220000_RenameModelFamilyToLevel.cs` (the repo's existing data-only migration) —
same bin-lock reason as the migrations before it. `Down` is a no-op with a comment: the pre-fix
values were a default, not a decision, and restoring "everything is ClaudeCode" would be restoring
the bug.

### D6 — scope of `Kind`'s meaning, stated in code

`Agent.Kind`'s XML doc (`server/Domain/Entities/Agent.cs:88-100`) currently says only the dispatcher
writes it and a user agent keeps the default. After W1–W4 that is no longer true, and a stale doc on
this exact field is how the next reader re-derives the card's wrong premise. The doc gets rewritten
to state D1 as the contract, and `AgentTaskDispatcher.cs:1941`'s comment ("a user's Grok agent still
reads ClaudeCode") gets corrected in the same slice — it will be false, and it is load-bearing
reasoning for why that method reads the session instead of the row. The method's behaviour does not
change: reading the live session's kind stays strictly better evidence than the row.

---

## Slices

Three, each independently landable and independently revertible.

### S1 — the invariant at the API write path (W1) + the doc (D6)

`AgentProfileKind.Sync`, called from `ApplyTuiSelectionAsync`. Tests T1, T2, T3.
Behaviour change: an agent created or edited with a non-Claude profile now stores the right `Kind`.
Nothing reads it, so the observable change is the column and the tests.

### S2 — the three other writers (W2, W3, W4)

The importer's pool filter + sync, the profile-kind-edit re-sync, and the dispatcher restamp guard.
Tests T4, T5, T6. **W2 must land with T5 in the same commit** — it is the one edit that can break
the warm pool.

### S3 — the backfill migration (D5)

Migration + T7. Ships last so the invariant is already holding when the historical rows are
corrected; ordering it earlier would let the next `AgentService.UpdateAsync` on those rows silently
re-break them.

---

## Test coverage

The card asks for two tests. It needs seven; T1 and T7 are the two it names.

| # | Test | Where | Pins |
|---|------|-------|------|
| T1 | Creating an agent with a Codex profile stores `Kind = Codex`; with a Grok profile, `Kind = Grok` | `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs` | W1, create |
| T2 | `UpdateAsync` moving an agent from the `claude` profile to the `codex` profile moves `Kind` 1 → 2, and moving it back moves it 2 → 1 | same | W1, edit — the "later profile change" half the card asks about |
| T3 | **An agent with no `TuiProfileId` is completely unaffected**: create with `tuiProfileId: null` in a harness with no installation-default profile (so `ApplyTuiSelectionAsync:958` takes the early return), assert `Kind` is untouched and the legacy `AgentRegistry` launch path composes byte-identical arguments | same + a launch assertion modelled on `NamedCodexAgentLaunchTests` | the card's explicit regression requirement, and D1's second half |
| T4 | Editing a profile's `Kind` (Codex → OpenCode) re-syncs every attached agent; agents on *other* profiles are untouched | `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs` | W3 |
| T5 | **The importer backfill skips pool delegates**: a `Kind = Grok`, `TuiProfileId = null`, `IsPoolDelegate = true` row survives an import with its `Kind` **and** its null profile intact, while a standing null-profile agent alongside it is assigned the default profile *and* its kind | `tests/Antiphon.Tests/AgentTui/` (new `AgentTuiProfileImporterBackfillTests.cs`) | W2 / R1 — the edit that could break the warm pool |
| T6 | A task pinned to a **standing** agent that reaches the spawn path leaves `Agent.Kind` alone; the same path on a **pool delegate** still restamps it | `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs` or a sibling | W4 / R2 |
| T7 | Migration test: two rows seeded with `Kind` disagreeing with their profile are corrected; a mismatched pool row and a null-profile row are both left alone | `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs` (it already asserts over real migrations and the `Agents` schema) | D5 |

Note on T3's phrasing: "unaffected" is verifiable in two independent ways and both are worth
asserting, because the column being untouched is weaker than the launch being unchanged, and it is
the *launch* the card cares about.

Run: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0138/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"`
then the `AgentTui` namespace, then delete the ~12 `bin-card0138/` directories.

---

## Risks, and what is deliberately not done

- **The one dangerous edit is W2's pool filter.** Getting it backwards (syncing a pool delegate's
  `Kind` from a backfilled default profile) hands Grok/Codex tasks a warm Claude process — a
  successful-looking dispatch whose report never comes. T5 exists solely for this.
- **W4 changes dispatcher behaviour for standing agents.** After it, a standing agent's row keeps
  its own kind through a pinned dispatch. Nothing reads a standing agent's `Kind` (§Verdict 3), and
  `PlaceOnStandingAgentAsync` deliberately reads (`:1944-1948`) the *session*, so this is a write removal with
  no reader behind it. Called out because a write removal in the dispatcher deserves the scrutiny.
- **Not done: the dispatcher's spawn path ignoring a standing agent's TUI profile** (R2, second
  half). A task pinned to a stopped standing agent launches through
  `_agentRegistry.Resolve(DefinitionNameForKind(task.AgentKind))` — appsettings, not the agent's
  profile — so a Codex-profile agent can be cold-launched as `claude`, and its managed environment,
  model catalogue and profile arguments are all dropped. That is a genuine wrong-program launch,
  it is bigger than this card, and it wants its own card rather than being smuggled in under a
  column-consistency fix. **This is the defect the card thought it had found**; it is worth raising
  as CARD-0138's sibling with §R2 as its evidence.
- **Not done: the composite `(TuiProfileId, Kind)` foreign key** (D4). Written up above so it can be
  picked up as a hardening slice once the application invariant has held for a while.
- **Not done: any change to `CreateAgentRequest`/`UpdateAgentRequest`, the client, or any DTO.**
  Adding a `Kind` to the wire would create the drift this card is trying to remove.
- **Not done: restarting or re-launching the two mismatched agents.** Their launches are already
  composed from the profile (§Verdict 2); there is nothing in flight to repair.
