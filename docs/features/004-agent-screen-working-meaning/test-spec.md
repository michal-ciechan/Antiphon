# Feature 004 — Test spec: agents-screen "Working" means mid-turn

**Status:** implemented and re-verified — S1–S6 landed in `e1a46d1`
(`AgentServiceIntegrationTests.cs:517-666`), C1–C6 in `e72d6c8` (`AgentsPage.test.tsx:192-320`);
S7 n/a (R3 skipped). Re-run green on 2026-08-09: server `AgentServiceIntegrationTests` 25/25
passed, client `AgentsPage` 20/20 passed. §4 live smoke still outstanding — see below.
**Date:** 2026-08-06 (status updated 2026-08-09)
**Card:** CARD-0001
**Companion:** [implementation-spec.md](implementation-spec.md)

---

## What is already pinned (do NOT duplicate)

| Behaviour | Pinned by |
|---|---|
| The working/idle *rule* itself: activity vs turn-end, interrupt marker = turn end, local slash-commands excluded, compact boundary excluded, WhenIdle hold/flush | `tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs` (via `GetQueueAsync().Working`) |
| Real-Claude transcript shapes behind those exclusions | `ClaudeInterruptCanaryTests`, `ClaudeLocalCommandCanaryTests` (headed), `FakeClaudeContractTests` |
| Client: spinner badge only for the transcript-working agent, not the merely-started one | `client/src/features/agents/AgentsPage.test.tsx:165-191` |
| The badge's own branch matrix (Working / Review / Failed / Disconnected / `showIdle` fallback), since it was extracted to a shared component in `7fe8f5b` | `client/src/features/agents/AgentActivityBadge.test.tsx` — assert *page wiring* in `AgentsPage.test.tsx`, *branch logic* here |

The rule's semantics live at the queue tier. **Agent-tier tests must test the projection, not
re-litigate the rule** — one shared-rule canary (S5) is the only overlap allowed.

---

## 1. Server — agent projection tests (the gap; must implement)

**Where:** `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs` (extend — reuse its
`CreateContext()` / `CreateService(db, eventBus)` helpers and `[NotInParallel("AgentQueue")]`), or a
sibling `AgentWorkingProjectionTests.cs` in the same group if the file gets crowded.

**Arrange pattern** (mirror `SessionMessageQueueServiceTests.CreateHarnessAsync`,
`SessionMessageQueueServiceTests.cs:358-427`, and its `MarkWorkingAsync` seeding, `:343`):

1. Create an agent via `service.CreateAsync` (unique name — see `UniqueAgentName`).
2. Seed an `AgentSession` row (`Status = SessionStatus.Running`, `DefinitionName = "fake"`,
   `AgentKind = ClaudeCode`) and set `agent.PersistentSessionId = sessionId.ToString("D")` —
   the same DB-tier wiring `ContractSnapshotTests.cs:70-75` uses.
3. Seed `TranscriptEntries` to shape working/idle:
   - *mid-turn*: one `TranscriptKinds.AssistantText`-style activity entry, **no** `TurnEnd` after it.
   - *idle*: activity entry followed by a `TurnEnd` with a higher `Sequence`.
4. Act: `service.GetAllAsync(ct)` / `service.GetByIdAsync(agentId, ct)`.
5. Clean up seeded sessions/agents (`ExecuteDeleteAsync`) in `finally` — shared Postgres fixture.

### Required tests

| # | Name (intent) | Arrange | Assert |
|---|---|---|---|
| S1 | **Idle live session reports Working=false while lifecycle status stays started** — the headline regression from the investigation | Started agent (`Status = AgentStatus.Working`¹), Running session, transcript idle | Summary `Working == false` **and** `Status` still the started lifecycle value. This pair is the whole point: the two fields answer different questions. |
| S2 | Mid-turn live session reports Working=true | Same, transcript mid-turn | Summary `Working == true` |
| S3 | Non-Running live session never reports working | Session `Status = SessionStatus.Starting` (and/or `Stopped`), transcript *mid-turn* | `Working == false` — pins the gate in `AgentService.IsSessionWorkingAsync` (`AgentService.cs:63-65`), which suppresses stale transcripts of dead sessions |
| S4 | No live session / no `PersistentSessionId` → Working=false | Fresh agent, no session row | `Working == false`, no throw |
| S5 | Detail parity + shared-rule canary | Same seeding as S2, plus a variant whose last entry is an interrupt marker (`TranscriptKinds.InterruptedPromptPrefix` user prompt) | `GetByIdAsync(...).Working` matches the summary for the same state; the interrupt variant reads `false` — proves agent tier rides the *same* rule (one case only; the full exclusion matrix stays at the queue tier) |
| S6 | List projection isolates agents | Two agents: one mid-turn, one idle, both Running | `GetAllAsync` returns `Working == true` for exactly the mid-turn one — catches a batched-query grouping bug if R3 lands |

¹ After R1 (rename), `AgentStatus.Running` — S1's assert is then
`Status == AgentStatus.Running && Working == false`, which reads exactly right.

**If R3 (batched query) is implemented:** S1–S6 must pass unchanged against the batched path (they
run through `GetAllAsync`, so they cover it automatically), and add:

| # | | |
|---|---|---|
| S7 | Batched and single-session paths agree | Seed the S5 exclusion variants (interrupt marker, local-command record, compact boundary — reuse the seeding shapes from `SessionMessageQueueServiceTests`) | For each: `GetWorkingSessionIdsAsync(db, [id])` membership `==` `IsWorkingAsync(db, id)`. This is the anti-drift lock for the shared predicates. |

### How to run

The always-on session-runner/dev server lock `bin/` — build to an alternate output:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0001\ `
  -- --treenode-filter "/*/*/AgentServiceIntegrationTests/*"
```

The `--` is required: without it `dotnet run` eats `--treenode-filter` as its own argument.

Needs the always-on Postgres (`antiphon-postgres`, port 17280) up. Some Antiphon.Tests are
PTY-timing flaky under full parallel load — rerun failures in isolation before blaming a change.

**Clean up after the run.** `OutputPath` applies to every project in the graph, so it drops an
untracked `bin-<name>/` in ~12 directories, and those names are *not* gitignored — left behind they
show up as untracked noise in a repo where other fleet agents are committing:

```powershell
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0001 | Remove-Item -Recurse -Force
```

---

## 2. Client — Vitest gaps (`AgentsPage.test.tsx`)

Existing coverage: card spinner only for `working: true` (`:150-174`). Add:

| # | Test | Assert |
|---|---|---|
| C1 | Detail header shows the Working spinner | Selected agent detail with `working: true` → badge with spinner in the header (`AgentsPage.tsx:225`); `working: false` → no "Working" text anywhere in the header |
| C2 | Quiet states show no badge | Agents with `status` `Idle` / `Ready` / `Stopped`, `working: false` → `AgentActivityBadge` renders nothing (no `Working`/`Review`/status text on the card) |
| C3 | Attention states | `status: 'WaitingForHumanReview'` → `Review` badge; `status: 'Failed'` and `'Disconnected'` → red badge with the status text — even with `working: false` |
| C4 | Working wins over attention states | `working: true` **and** `status: 'Failed'` → spinner badge (first branch, `AgentsPage.tsx:392`) — pins the precedence order |
| C5 | Stop/Start gating | Detail with `liveSession` present but `status: 'Stopped'` → **Stop** shown (gating is `liveSession \|\| status === 'Working'`, `AgentsPage.tsx:242`); no `liveSession` and idle status → **Start**. Check `:493-556` first — partial coverage may exist; extend, don't duplicate. |
| C6 | Terminal icon liveness colour | `liveSession.status: 'Running'` → green icon; `'Starting'` → yellow; none → gray (tooltip text as proxy if colour is awkward to assert) |

Follow the file's existing MSW `agentHandlers` fixture pattern (`:152-168`). Run:
`npm test -- AgentsPage` from `client/` (`npm install` first if `node_modules` missing).

**After R1 (rename):** update fixtures `status: 'Working'` → `'Running'`
(`:157,165,493,536,556`) and the union in `agents.ts:53` — TypeScript will *not* catch stale MSW
JSON strings, so grep the test file for `'Working'` and confirm only the badge-label assertions
(`toHaveTextContent('Working')`) remain.

---

## 3. Rename regression checks (with R1)

1. Repo-wide grep: `AgentStatus.Working` → 0 hits; `status === 'Working'` → 0 hits;
   `'Working'` in `client/src` only as the badge label / `working` field.
2. Full server suite green (reconciliation tests are the load-bearing ones —
   `SessionReconciliationServiceTests` asserts the lifecycle latch semantics survive the rename).
3. No new EF migration generated by the rename (int-backed enum).
4. `ContractSnapshotTests` (E2E) — rerun; if any snapshot embeds the agent status string, the
   snapshot update is the *only* acceptable diff.

---

## 4. Live smoke (manual, against the dev stack)

Run once after all code lands (`.\dev-aspire.ps1` stack up):

```powershell
# 1. Fleet at rest: idle always-on agents must NOT report working
#    (Format-Table renders these DTOs badly — build the line yourself)
$r = Invoke-RestMethod http://localhost:17202/api/agents
$r | Sort-Object name | ForEach-Object {
  '{0,-22} status={1,-10} working={2,-6} session={3}' -f `
    $_.name, $_.status, $_.working, $(if ($_.liveSession) { $_.liveSession.status } else { 'none' }) }

# 2. Cross-check EVERY live agent against the session-tier signal (must all agree)
$r | Where-Object { $_.liveSession } | ForEach-Object {
  $q = Invoke-RestMethod "http://localhost:17202/api/sessions/$($_.liveSession.id)/messages"
  '{0,-22} agent={1,-6} session={2}' -f $_.name, $_.working, $q.working }
```

### Recorded run — 2026-08-09 ✅ (headless half)

11 agents, 6 with a live Running session. **Exactly one** reported `working: true` — the
`Antiphon` fleet agent, which was genuinely mid-turn (it was the session running this check).
The other five read `status: Running` + `working: false`: the two fields disagreeing is the
whole point, and the original bug report's "every agent is Working" symptom is gone. The five
`Failed` agents had no live session and reported `working: false` (the S3/S4 gate, live).
Step 2 agreed on all six — no drift between the agent tier and the session tier.

Then in the browser (per `feedback_always_use_browser_harness` — CDP Edge on :9222, not the Chrome
extension): open `/agents`, confirm idle agents show **no** badge and green terminal icons; send a
message to one agent (its terminal modal or a bound channel); the spinner must appear on its card
within ~5s (poll interval) and disappear within ~5s of the turn ending. This is the check the
investigation's original bug report failed: **the page must not show every agent as Working**.

Expected pass state mirrors the investigation's table: started agents report `status` =
started-lifecycle value with `working: false` while idle, flipping `working: true` only mid-turn.

---

## Done means

- S1–S6 (S7 if R3) implemented and green; C1–C6 green. ✅ 2026-08-09 — 25/25 server, 20/20 client.
- Rename checks (§3) clean if R1 landed. ✅ 2026-08-09 — repo-wide grep for `AgentStatus.Working`
  and `status === 'Working'` returns hits only in `docs/` history and in the unrelated
  **delegation-task** status union (`'Dispatched' | 'Working' | …`, `TaskDrawer.tsx`,
  `DelegationsBoard.tsx`, the home-page fixtures) — a different enum, correctly left alone.
- Live smoke (§4) performed and its observations recorded in the card/PR description.
  🟡 **Half done** — the headless API half ran clean on 2026-08-09 (recorded in §4). The
  **browser half is the one outstanding item**: open `/agents`, confirm idle agents show no badge
  and green terminal icons, then watch a spinner appear/disappear within ~5s of a turn starting and
  ending. Do it before calling the card closed.
