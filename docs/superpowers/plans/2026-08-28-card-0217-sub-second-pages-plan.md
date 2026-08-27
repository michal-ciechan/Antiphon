# CARD-0217 — Every page under a second: fix the shared floor once, then bound each page's own data

**Date:** 2026-08-28
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0217 (`599b702d-07ce-43cb-b9ff-ea604e671f1e`), board Antiphon
**Scope:** every route in `client/src/App.tsx` except the home page's phone band and workspace
fan-out, which CARD-0216 owns. The target is the user's: content in under one second. This plan
takes that as **loopback desktop, cold cache, to the DOM commit that holds the page's content**
(the investigation's marker), with the phone case reported alongside — see §5 D1 for the reading
this plan assumes.
**Evidence base:** `docs/investigations/2026-08-28-card-0217-page-load-sweep.md` (this card's
sweep — per-page table, A/B attribution, bundle attribution) and
`docs/investigations/2026-08-27-card-0216-post-load-spinner.md` (the home page). **This plan does
not re-derive any of it**; every number below is cited from those two docs.
**Builds on:** CARD-0216 S1 (built bundle on 17203 — the serving path this plan measures through),
CARD-0216 §2.3 (git process gate + `IsWorkingBatchAsync`, which §3.3 here extends to the detail
endpoint and readiness), CARD-0216 §4 (its explicit hand-offs to this card: `/api/agents` 5 s poll,
`/api/agent-tasks` payload, the unsplit entry chunk, code-splitting, the desktop render gate, the
`/api/attention` per-task probe).
**Model followed:** `docs/superpowers/plans/2026-08-27-card-0216-remote-home-load-and-post-load-spinner-plan.md`.

## Verdict, in one screen

| Finding (investigation §) | Consequence for the design |
|---|---|
| **No page meets 1 s cold; the light pages miss by 0.3–1.2 s and the heavy ones by 1.5–9 s.** Warm ≈ cold on every heavy page (§2). | Two tiers of work: a *floor* tier that every route pays (S1–S3, S7), and a *per-page* tier that bounds each page's own data (S4–S6, S8). The floor alone brings the light pages under 1 s; the heavy pages need both. |
| **The served bundle is a development React build** — 3,683 `jsxDEV` refs, dev warning strings, 470 KB larger than a plain `vite build`; cause with high confidence Aspire's `NODE_ENV=development` reaching `vite build` through `serve.mjs` (§4.1). | S1: pin `NODE_ENV=production` in the shim's build/watch/preview steps and assert the artefact. Cheapest slice, affects every page's render cost. |
| **Orchestrator is 8.7–10.8 s because `DelegationsBoard` renders all 540 tasks (14.8k nodes, 189 MB heap) and `DecisionsPanel` fetches 176 board details — both mounted behind every tab.** A/B: without the task list 0.6–1.3 s / 33 MB (§3.5). | S3 (`keepMounted={false}`) removes the hidden-tab cost in one line per page; S4 bounds what the delegations tab renders when it *is* the tab. |
| **Settings is 3.7–4.4 s for the same reason**: all six panels mount; `ProjectConfig` fires 84 readiness calls (a git spawn each) and renders 84 rows (§3.4). Agents fires 32 of the same calls (§3.3). | S3 for the tabs; S6 for readiness — one batch endpoint with a TTL cache, fetched lazily per visible row. |
| **`/api/boards/{id}` is 1.41 MB, 84 % of it descriptions and terminal reasons the list views never show**; `/boards`, the phone home and DecisionsPanel fetch it ×176 (§3.2). Nothing on the API path compresses (§4.2). | S2: response compression (phone-only win, but large). S5: a summary representation for every list surface + one "cards changed / cards in status" endpoint replacing the 176-way fan-out. |
| **`/api/attention` runs on every page from the nav badge** at 430–820 ms, 1.2–1.5 s under fan-out contention (§4.7). | S8: a counts-only endpoint for the badge, and CARD-0216 §4's "skip the probe below StallMinutes". |
| **The plan reader's `?ref=` costs 60–75 ms** (§3.6). | Nothing to do for `?ref=`. The page's dead weight is `useAgentTasks()` (662 KB every 15 s) for one label — S4 removes it. |
| **48 of 84 projects and ~160 of 176 boards are test residue** (§4.8). | Every fan-out is multiplied by it. §5 D2 is the operator's call; S9 gives them the tool. |

## 1. What exists today (only what the investigation did not already record; verified 2026-08-28)

### 1.1 Where the pages get their data (`client/src`)

- **Route table:** `App.tsx` — `/`, `/workflows`, `/workflow/:id`, `/boards`, `/boards/:id`,
  `/agents`, `/agents/:id/files` (outside `Layout`), `/channels`, `/plans`, `/thread/:cardId`,
  `/orchestrator`, `/settings`. All twelve page components are static imports; `lazy()` is used
  only for `MermaidDiagram` and `MonacoEditor`.
- **Global fetches:** `shared/Layout.tsx:173` → `DecisionsBadge` → `useAttention()` (15 s).
  `App.tsx` → `useSignalR` (one hub connection; `SessionTerminal`, `SessionTranscriptPanel`,
  `SessionMessageQueue` open their own — the extra `negotiate` calls on home and agent-files).
- **The three fan-out hooks:** `api/boards.ts:422` `useAllBoardDetails(ids)` — one query,
  `Promise.all` over `GET /boards/{id}`; `api/projectSetup.ts:127` `useProjectReadinessList(ids)`
  — `useQueries`, one per project; `api/agents.ts:442` `useAgent(id)` — 5 s interval on the
  selected agent.
- **Polls:** `useAgentList` 5 s (`agents.ts:438`), `useAgent` 5 s, `useAttention` 15 s,
  `useAgentTasks` 15 s with `staleTime: 5_000` (`agentTasks.ts:230`), `useGitHubStatus` 30 s.
  `hooks/useSignalRInvalidation.ts` already invalidates `agents`, `boards`, `agentTasks` and
  `attention` keys on hub events — the timers are a belt over those braces.
- **Tabs:** `features/settings/SettingsPage.tsx` and `features/orchestrator/OrchestratorPage.tsx`
  use `<Tabs>` with no `keepMounted`; Mantine 8's default is `true`
  (`node_modules/@mantine/core/lib/components/Tabs/Tabs.d.ts:38`). `CardModal.tsx:203` and
  `AgentCliModal.tsx:68` already pass `keepMounted={false}`.
- **DelegationsBoard:** `features/delegations/DelegationsBoard.tsx:36-70` — `all = tasks.data`,
  `visible = all` unless a run is selected, `forest` built from all, `defaultExpanded` = every root.
  No windowing, no date bound, no status default.
- **Plan reader:** `features/plans/PlanReaderPage.tsx:206` `useAgentTasks()` to find `taskId`;
  `api/agentTasks.ts:235` `useAgentTask(id)` → `GET /agent-tasks/{id}` already exists.
- **Card thread:** `features/thread/CardThreadPage.tsx:19` `useBoard(thread.data?.card.boardId)`
  — the full board detail to give one card its board context.

### 1.2 Server shapes the slices touch

- `BoardService.GetByIdAsync(id, includeArchived)` (`server/Application/Services/BoardService.cs:~60-110`)
  → `BoardDetailDto` → `CardDto` (`server/Application/Dtos/BoardDtos.cs:40-72`: `Description`,
  `TerminalReason`, `Sessions`, `ExternalIssue`, …). `GET /boards/{id}/columns` already exists as
  "the shape without contents" (`BoardEndpoints.cs:31-39`). `GET /cards/{id}` exists
  (`CardEndpoints.cs:35`).
- `ProjectSetupService.GetReadinessAsync` (`ProjectSetupService.cs:~50-130`): 6 DB queries +
  `GitRepositoryCheckAsync` → `_resolver.GetRepoToplevelAsync` + `TryGetOriginUrlAsync`
  (`:304-331`, a bare `Process.Start("git remote get-url origin")` with its own 10 s timeout, not
  through `GitWorkspaceService`, so CARD-0216 S3's process gate does not cover it).
- `AgentService.GetByIdAsync` (`AgentService.cs:~125-140`): `LoadLiveSessionsAsync` (runner HTTP,
  100 s client timeout) + `LoadSupervisionAsync` + `IsSessionWorkingAsync` — the single-agent shape
  of the `GetAllAsync` N+1 CARD-0216 S4 batches.
- `AttentionService.GetAsync` (`AttentionService.cs`): ~20 queries + `TryListRunnerSessionsAsync`
  (`:936-950`, runner HTTP) + `_files.ProbeProgressAsync` per open task (`:585`).
- `server/Program.cs`: no `AddResponseCompression` / `UseResponseCompression`. Kestrel serves JSON
  raw; the Vite proxy and Caddy pass it through unchanged.
- `client/scripts/serve.mjs:54-66,168,245`: build/preview/watch steps spawn `node vite.js …` with
  `{ ...this.env, ...step.env }` — the shim's own environment, which under Aspire carries
  `NODE_ENV=development` (`Antiphon.AppHost/Program.cs:87` `AddNpmApp("client", "../client",
  "serve")`; Aspire sets it for npm apps that are not being published).

### 1.3 What does not exist (the build list)

- A board/card **summary** representation; a **cards-across-boards** query (`updatedSince`,
  `status`); a **readiness batch** endpoint; an **attention counts** endpoint.
- Any client-side windowing/virtualisation (`grep` for react-window/react-virtual/Pagination:
  nothing).
- Route-level code splitting; `build.rollupOptions.output.manualChunks`.
- A checked-in page-load measurement (this card adds `scripts/perf/page-load-probe.py`).
- A test that the served bundle is a production build.

## 2. Design

### 2.1 S1 — Make the served bundle a production build (floor)

`client/scripts/serve.mjs`: every step in `planForMode('built')` (build, preview, watch, and the
`-Rebuild` path at `:245`) gets `env: { NODE_ENV: 'production' }` merged **after** the inherited
env, so nothing upstream can demote it; the dev-mode step keeps its inherited env (Vite's dev
server wants `development`). The shim logs `NODE_ENV` it observed and the one it set, once, at
the swap, so the cause is confirmed in the log the first time it runs (the investigation's evidence
is the artefact, §4.1 — the log line turns "high confidence" into "measured"). `vite.config.ts`
gains nothing. **Verification is the artefact:** `dist/assets/index-*.js` contains zero `jsxDEV`
and none of React's dev warning strings; size drops from 3.0 MB to ~2.5 MB minified. Re-run the
probe on `/agents` and `/orchestrator?tab=delegations`; expect the `jsxDEV` self-time gone from
the profile and a measurable (not dramatic) drop in render time — this is a floor fix, the page
fixes are S3–S6.

### 2.2 S2 — Compress API responses (floor, phone-only payoff)

`server/Program.cs`: `AddResponseCompression` with Brotli + Gzip providers, `EnableForHttps =
true` (Caddy terminates TLS, but keep it correct behind any front door), MIME types `application/json`
and `application/problem+json` only — **never** `text/event-stream` or the SignalR hub path
(`/hubs`), which must stay uncompressed for streaming. `UseResponseCompression()` before
`MapEndpoints`. Nothing changes on loopback timings (§2b: the bytes are the same, the RTT is ~0);
on the phone the board goes from 1.41 MB to ~150 KB and the task list from 662 KB to ~70 KB. The
Vite proxy and Caddy both pass `Content-Encoding` through (measured: the bundle's gzip survives
both). Caddy-side `encode` in the links repo is an alternative the operator may prefer (§5 D5) —
recommend Kestrel, because it also covers `localhost:17202` direct and the E2E fixture.

### 2.3 S3 — Stop mounting hidden tabs (per-page, one line each)

`SettingsPage.tsx` and `OrchestratorPage.tsx`: `<Tabs … keepMounted={false}>`. That alone takes
`/settings` from 100 API calls to ~10 (templates + providers + template-groups + model-routing +
attention + negotiate) and `/orchestrator` from 182 to ~5, because `ProjectConfig`,
`DelegationsBoard` and `DecisionsPanel` no longer mount until their tab is chosen. Tab switches
then cost their panel's own load, which is what S4–S6 bound. Tests that query a hidden panel's
content (`SettingsPage`/`OrchestratorPage` tests, if any assert on non-active panels) move to
clicking the tab first.

### 2.4 S4 — Bound the delegations board and stop shipping the whole task history

Two halves, both needed (§3.5's A/B: the render is ~7–8 s of the 9.5 s).

**Server:** `GET /api/agent-tasks` grows `since` (ISO instant) and `status` (comma list) filters,
default **unchanged** so scripts and `delegate.ps1` keep working, plus `GET /api/agent-tasks/summary`
→ `{ active, blocked, runs, byStatus }` for the counters that today are computed client-side from
the whole list. The dispatcher, orchestration tick and check-interpreter do not read this endpoint
(they read the DB) — confirm with a grep during the slice, nothing else changes.

**Client:** `DelegationsBoard` fetches `?since=<now − Delegations:DefaultWindowDays (7)>` plus every
non-terminal task regardless of age (a Blocked task from three weeks ago must still show), renders
the counters from `/summary`, and adds a "Show all" toggle that fetches the unbounded list on
demand. Lanes are windowed with `@tanstack/react-virtual` (the one new dependency in this plan —
already the ecosystem's default alongside `@tanstack/react-query`), so even "Show all" over 540
tasks paints in one frame. `forest`/`defaultExpanded` are computed over the fetched set only.
`PlanReaderPage` swaps `useAgentTasks()` for `useAgentTask(taskId)` (exists); `HomePage`'s two
`useAgentTasks()` subscribers and `ProjectTasksPanel` are CARD-0216 S5's `staleTime` territory and
stay as they are here, but they also pass `since=` once the parameter exists (home shows recent
work, not history). Target: `/orchestrator?tab=delegations` under 1 s cold with today's 540 tasks;
`/plans?file=…` loses 662 KB per open.

### 2.5 S5 — A summary shape for every card list, and one query for "cards across boards"

**Server:** `GET /boards/{id}?view=summary` returns the same `BoardDetailDto` shape with each
`CardDto.Description` replaced by its first `Cards:SummaryPreviewChars` (200) characters (on a
word boundary, `…` appended when cut), `TerminalReason` likewise, `Sessions` empty, and a new
`HasMore: bool` on the card so a consumer can tell a preview from a short description. The default
(`view=full`) is **unchanged** — `card.ps1`, tests, and the modal's optimistic updates keep the
contract. A new `GET /cards?updatedSince=&status=&boardId=` (any combination; at least one
required; capped at `Cards:MaxListResults` 500 with a `Truncated` flag) answers the two
"all cards" consumers in one round trip, in the summary shape.

**Client:** `BoardPage` (shape view — it renders 2 rows from 1.41 MB today), `AllCardsBoard`,
`MobileHomePage` and `DecisionsPanel` take the summary view; `CardModal` fetches `GET /cards/{id}`
when it opens (`useCard(id)`, new hook; the modal's edit/move mutations already invalidate the
board keys and gain the card key). `MobileHomePage` replaces `useAllBoardDetails` with
`GET /cards?updatedSince=<lastSeen>`; `DecisionsPanel` with `GET /cards?status=NeedsDecision`;
`AllCardsBoard` keeps the fan-out shape but over the summary view (176 × ~1 KB instead of one
1.4 MB board plus 175 small ones) until §5 D2 prunes the board count. `CardThreadPage` uses
`GET /boards/{id}/columns` (exists) for the column names it needs. Target: `/boards/{Antiphon}`
≤ 150 KB and under 1 s; phone home from 183 calls / 2.58 MB to ~8 calls / ~250 KB.

### 2.6 S6 — Readiness once, cached, lazily

**Server:** `GET /projects/readiness?ids=a,b,c` (≤ 100 ids) → `ProjectReadinessDto[]`, computed
through a per-project `IMemoryCache` entry with `Projects:ReadinessCacheSeconds` (60) TTL and
invalidated by the project-setup/update/delete paths, run with bounded parallelism (4) so 84 cold
projects are ~84 × 70 ms / 4 ≈ 1.5 s once a minute at worst, not 84 spawns per page view.
`TryGetOriginUrlAsync` moves onto `GitWorkspaceService.RunAsync` so CARD-0216 S3's process gate and
kill-on-timeout cover it.

**Client:** `useProjectReadinessList` becomes one query over the batch endpoint with `retry: 1`
(§3.3: 32 failing queries × default retry 3 wedged the page). `ProjectConfig` requests readiness
only for the rows currently rendered (the table is paginated at 25 with a search box — 84 rows of
which 48 are test residue is not a table anyone scrolls) and `AgentsPage` requests it only for the
**selected** agent's project, showing a neutral badge on the others. `AgentsPage`'s row component
is memoised so a readiness or `/agents` poll response re-renders one row, not 49 × 1,550 SVGs.
Target: `/settings?tab=projects` and `/agents` under 1 s cold.

### 2.7 S7 — Split the entry chunk by route and by heavy widget (floor)

`App.tsx`: `React.lazy` for each of the twelve pages behind the existing `SuspenseBoundary`
(`variant="page"` already renders a page-shaped fallback). `SessionTerminal` (xterm, 333 KB),
`CardEditModal` (tiptap/prosemirror, ~290 KB) and the markdown renderer (`highlight.js` +
`hast-util-raw` + markdown-it, ~350 KB) become `lazy()` at their use sites — none of the three is
on any route's first paint. `vite.config.ts` `build.rollupOptions.output.manualChunks` pins
`react`/`react-dom`/`react-router`, `@mantine/*`, and `@tanstack/*` into named vendor chunks so a
feature change does not invalidate the vendor cache. Target: entry chunk ≤ 900 KB minified
(≤ 300 KB gzip) — measured after S1, since S1 changes the baseline. Expected effect on loopback:
150–300 ms per cold load; on the phone, proportionally more (parse is CPU-bound).

### 2.8 S8 — The nav badge stops paying for the whole attention sweep

`GET /attention/summary` → `{ open, decisions, generatedAt }` computed from the same service but
with `includeProgressProbe: false`, cached 10 s. `DecisionsBadge` uses it (15 s interval as
today); `useAttention()` proper stays for the panels that render items. `AttentionService` gains
CARD-0216 §4's rule — skip `ProbeProgressAsync` when `now − DispatchedAt < TaskProgressPolicy.StallMinutes`
— which today removes the probe entirely (the one open task is minutes old). Target: the badge's
call under 100 ms; the home page's first wave no longer waits on a 430–820 ms call it does not
render from.

### 2.9 S9 — Test-residue tooling (operator decision D2 gates the run, not the build)

`scripts/prune-test-data.ps1`: lists (default) or archives (`-Execute`) boards and projects whose
names match the test shapes (`card-task-*`, `card\d+-*`, `Catalog Test*`, `PwshCreateTest`,
`TUI Probe`, `CARD-\d+ Repro *`) **and** have no agent, no session and no non-terminal task, with a
`-Match` override. Archive, never delete: the E2E and integration suites create their own rows in
their own database (`TestDbFixture`), so nothing live depends on these, but reversibility costs
nothing. Prints what it would touch and why each row qualified.

### 2.10 S0 — The measurement is part of the deliverable

`scripts/perf/page-load-probe.py` (checked in with this plan) is the acceptance test for every
slice: `AP_ONLY=<page> AP_MODES=cold browser-harness < scripts/perf/page-load-probe.py` prints
content-at, request count, bytes, heap per page; `AP_BLOCK` isolates an endpoint family;
`AP_PROFILE=1` attaches a CPU profile; `AP_VIEWPORT=390x844` is the phone. Each slice's PR records
before/after rows from it in the card, and close-out re-runs the full table into a new section of
the investigation doc.

## 3. What this costs its neighbours

- **`card.ps1`, `delegate.ps1`, the tracker sync, tests:** unaffected — every new server parameter
  defaults to today's behaviour (`view=full`, unbounded `/agent-tasks`, single-id readiness kept).
- **CARD-0216 S3/S4/S5** (git gate, working batch, home `staleTime`): S6 moves one more git spawn
  onto the gate; S4's `since=` on home composes with S5's `staleTime`. Serialise S6 after
  CARD-0216 S3 lands.
- **SignalR-driven invalidation:** S5's new `cards` query keys need the same hub-event
  invalidation `boards` keys get in `useSignalRInvalidation.ts`, or a moved card lags the phone
  home by one `updatedSince` poll.
- **Optimistic card moves** (`useMoveCard`) mutate the board detail cache in place; under
  `view=summary` the cached card carries a preview, and the modal's full text comes from
  `useCard(id)` — the two caches must not be written to each other.
- **Storybook / vitest:** `lazy()` routes need `await screen.findBy…` where tests currently
  `getBy…` synchronously after render; `keepMounted={false}` needs a tab click before asserting a
  hidden panel.
- **Response compression** adds CPU per response on the server (tens of µs per KB) — nil against
  the 300–900 ms these endpoints already take, but keep SignalR and SSE excluded.

## 4. Non-goals (deferred)

- **Server-side rendering / streaming HTML**: the floor after S1+S7 is ~0.5 s of bundle + mount on
  loopback, inside the target; SSR is a different architecture for a gain this plan does not need.
- **Replacing polling with SignalR entirely**: the timers stay as the fallback the codebase already
  relies on; only their periods and payloads change.
- **`/workflow/:id`** (not measurable today — no live workflow) and the Storybook app.
- **Virtualising the agents list**: after S6's memoisation and readiness laziness the 49-row list
  should be well under budget; revisit only if the probe says otherwise.
- **The 5 s `/api/agents` poll** — CARD-0216 §4 named it; this plan leaves the period alone (D4 is
  the operator's) because after S8 and S4 it is the only remaining periodic call on most pages and
  it is 74 KB / 40–110 ms.
- **Caddy `encode`** — D5; not from this repo.
- **Moving the AppHost off `NODE_ENV=development`** for the client resource: S1 pins production in
  the shim regardless, which is more robust than a `WithEnvironment` that a future Aspire default
  could re-flip.

## 5. Decisions that are the operator's — each with a recommendation

- **D1 — What does "under a second" mean?** Recommend: loopback desktop, cold, to content (the
  probe's marker), as the hard target every slice reports against; phone-over-5G reported as
  request count + bytes with the RTT × count projection, target "no fan-out, ≤ 300 KB, content
  before 2 s". Rejected alternative: measuring `load` — it fires before the frozen-tab phase on the
  orchestrator and after the content on light pages, so it is wrong in both directions.
- **D2 — Prune the test residue (48 projects, ~160 boards)?** Recommend **yes, archive via S9**,
  because it is a 5–10× multiplier on three fan-outs and on the `/boards` page, and it costs
  nothing to reverse. The plan does not *depend* on it (S5/S6 bound the fan-outs regardless), so a
  "no" changes nothing in the build list.
- **D3 — Delegations default window of 7 days + all non-terminal, with "Show all"?** Recommend
  yes. Alternative: a status-only default (active + blocked + last 24 h). Either reaches the
  target; the question is which history the operator reaches for daily.
- **D4 — Keep the 5 s `/api/agents` poll?** Recommend lengthen to 15 s with SignalR invalidation
  as the fast path (it already exists). If the operator relies on the 5 s cadence for the Working
  badge, leave it — it is not on the critical path after S8.
- **D5 — Kestrel compression (S2) vs Caddy `encode` in the links repo?** Recommend Kestrel (covers
  every path, lives in this repo, testable). Both is fine; Caddy alone leaves loopback and the
  Vite proxy uncompressed.
- **D6 — Card description preview length (200 chars) and whether `AllCardsBoard` should show
  descriptions at all.** Recommend 200 and keep the two-line clamp — the shape board already
  decided descriptions do not belong on tiles (`CardRow.tsx:20`); the operator may want the
  all-boards view to follow it, which makes the summary even smaller.
- **D7 — Order.** Recommend S0+S1+S3 first (an afternoon; S3 alone takes the orchestrator and
  settings pages from 9 s / 4 s to their panels' own cost), then S2, S4, S5, S6, S8, S7, S9. S7
  last because S1 changes its baseline and it is the only slice that touches every test file.

## 6. Slices, tiers, tests

Dispatch routing per this session's rule (plan → Fable/Opus; simple builds → Codex terra; verify →
Codex luna; else Grok). Every slice re-runs the S0 probe on the pages it names and records
before/after in the card.

| Slice | What | Tier / workspace | Tests (all red-before-green) |
|---|---|---|---|
| **S0** | `scripts/perf/page-load-probe.py` is already in the tree with this plan; add a `docs/perf.md` stub that names the budget and the command. | — (landed with the plan) | Manual: the command prints a row per page. |
| **S1** | `serve.mjs` built-mode steps pin `NODE_ENV=production`; one log line with observed vs set; a vitest on `planForMode('built')` step envs. | Codex terra, worktree | `serve.test.mjs` (exists for the shim's pure parts): every built-mode step's env has `NODE_ENV: 'production'`; dev-mode step does not override. Artefact check in the PR: `Select-String jsxDEV dist/assets/index-*.js` count = 0; size recorded. |
| **S2** | `Program.cs` response compression (Brotli/Gzip, JSON only, hubs excluded). | Codex terra, shared (server-only, one file) | `Antiphon.Tests` integration: `GET /api/boards` with `Accept-Encoding: br` → `Content-Encoding: br`; `GET /hubs/antiphon/negotiate` → no `Content-Encoding`; a JSON body round-trips identical. |
| **S3** | `keepMounted={false}` on `SettingsPage` and `OrchestratorPage`; test adjustments. | Codex terra, worktree | `OrchestratorPage.test.tsx` / `SettingsPage.test.tsx`: on mount, msw sees **no** `/api/agent-tasks` (orchestrator) / **no** `/readiness` (settings); after clicking the tab, it does. Probe: `/orchestrator` 182 → ≤ 6 calls, `/settings` 100 → ≤ 12. |
| **S4** | `since`/`status` on `/agent-tasks`, `/agent-tasks/summary`; DelegationsBoard window + virtualiser + "Show all"; PlanReader → `useAgentTask`. | Grok, worktree | TUnit: `since` excludes older *terminal* rows and keeps older non-terminal ones; `summary` counts match a full list. vitest: `DelegationsBoard` requests `since=` by default and the unbounded list after "Show all"; with 600 fixture tasks the DOM holds ≤ 80 lane cards; `PlanReaderPage` with `?task=` requests `/agent-tasks/{id}` and never `/agent-tasks`. Probe: `/orchestrator?tab=delegations` < 1 s cold. |
| **S5** | `view=summary` on `/boards/{id}`; `GET /cards?updatedSince&status&boardId`; `useCard(id)` in `CardModal`; list surfaces switched; `CardThreadPage` → `/columns`; SignalR invalidation for the new keys. | Grok, worktree | TUnit: summary preview cuts on a word boundary at 200 chars with `HasMore`; `view=full` byte-identical to today; `/cards?status=NeedsDecision` returns only those; `updatedSince` honours the revision timestamp, not `CreatedAt`. vitest: `BoardPage` requests `view=summary`; opening the modal requests `/cards/{id}`; `MobileHomePage` requests `/cards?updatedSince=` and never `/boards/{id}`; `DecisionsPanel` likewise. Probe: `/boards/{Antiphon}` < 1 s and ≤ 150 KB; phone home ≤ 10 calls. |
| **S6** | `GET /projects/readiness?ids=`, TTL cache, bounded parallelism, git spawn via `GitWorkspaceService`; batch hook with `retry: 1`; `ProjectConfig` pagination + lazy readiness; `AgentsPage` selected-only readiness + memoised rows. | Grok, worktree; **after CARD-0216 S3 merges** | TUnit: 84 ids → one response; second call inside TTL spawns no git (`gate.Started` unchanged); a failing project yields a per-row error, not a failed batch. vitest: `AgentsPage` with 49 agents requests readiness for exactly one project; a readiness response re-renders one row (React Profiler `onRender` count). Probe: `/settings?tab=projects`, `/agents` < 1 s. |
| **S7** | `lazy()` routes + the three heavy widgets; `manualChunks`. | Codex terra, worktree; **after S1** | `App.test.tsx`: every route resolves its page through the Suspense fallback; `SessionTerminal` chunk absent from the entry's `modulepreload`s. Build check in PR: entry ≤ 900 KB minified. Probe: `/workflows`, `/channels` < 1 s cold. |
| **S8** | `GET /attention/summary` + `DecisionsBadge` on it; `ProbeProgressAsync` skipped below `StallMinutes`. | Grok, shared (server + one client file) | TUnit: summary counts equal the full sweep's; with one open task dispatched 2 min ago the probe is not called (fake `IAgentFilesService`); with one at StallMinutes + 1 it is. vitest: `DecisionsBadge` requests `/attention/summary`, never `/attention`. Probe: every page's first wave loses the 430–820 ms call. |
| **S9** | `scripts/prune-test-data.ps1` (dry-run default). | Codex luna, shared | Pester-free smoke: dry run lists ≥ 40 projects and ≥ 150 boards on this DB and touches nothing (`git status` of the DB — row counts — unchanged); `-Execute` on a scratch DB archives only rows with no agent/session/open task. Run against live only on D2 = yes. |

Order: S0 (landed), S1 and S3 in parallel (disjoint files), then S2; S4 and S5 in parallel (server
files disjoint: agent-tasks vs boards/cards); S6 after CARD-0216 S3; S8 any time after S3; S7 after
S1; S9 whenever D2 is answered. Close-out re-runs the full probe table into
`docs/investigations/2026-08-28-card-0217-page-load-sweep.md` §6 with the after numbers.
