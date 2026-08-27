# CARD-0217 — every page measured against the sub-1s target (fable, 2026-08-28)

Brief: sweep every route for the same class of problem CARD-0216 found on the home page, with real
browser traces (not curl alone), diagnose each page on its own evidence, and call out anything that
is one cause across many pages. This doc is the evidence; the plan is
`docs/superpowers/plans/2026-08-28-card-0217-sub-second-pages-plan.md`.

**Verdict up front: no page meets the target cold on loopback, and the misses fall into two very
different bands.** The light pages (workflows, channels, plan reader, agent files, the shape board)
land at 1.2–2.3 s and are bounded by things every page pays — a 3.0 MB entry chunk that turns out to
be a *development* React build, plus one slow `/api/attention` call fired from the nav bar. The
heavy pages (Orchestrator 8.7–10.8 s, Settings 3.7–4.4 s, all-boards 3.3–3.7 s, Agents 2.3–2.7 s)
each have their own unbounded fan-out or unbounded render, and the phone home page — the exact
device CARD-0216 was about — silently fetches all 176 board details (183 API calls, 2.6 MB) on
every load. None of it is server CPU: the slowest single API call on any page is 0.9 s.

## 1. Method, and what the numbers mean

- `client/scripts/serve.mjs` in **built** mode on `localhost:17203` (CARD-0216 S1 shipped;
  `client-mode.ps1 -Status` = built, `lastBuildAt` 21:57:15, watcher on). Bundle served gzipped
  (839 KB on the wire for the 3.0 MB entry chunk), `/assets/*` immutable.
- Browser: the CDP Edge on :9222 driven by `browser-harness`. A probe installed with
  `Page.addScriptToEvaluateOnNewDocument` before app boot records `PerformanceResourceTiming` for
  every request, and a **MutationObserver on `#root`** stamps the moment a page-specific content
  marker enters the DOM (a card row test-id, an agent name, a plan heading — listed per row below).
  "Content" therefore means *the DOM commit that contains the thing the user came for*, not
  `load`. Cold = `Network.setCacheDisabled(true)`; warm = normal cache (assets from cache, index.html
  revalidated). Three to four cold runs and three warm runs per page; the table gives the range.
- Two caveats that matter. (1) The automation Edge window reports `visibilityState: hidden`
  (occluded), so its timers run at 1 Hz; runs 1–2 used a timer-polled marker and are ±1 s, run 3
  used the MutationObserver and is exact to the commit — they agree to within that resolution
  everywhere. (2) That profile has React DevTools installed; its `measureHostInstance` shows up at
  100–330 ms in CPU profiles, so profile totals are slightly pessimistic. Nothing in the
  conclusions rests on sub-300 ms differences.
- CPU attribution: `Profiler.start/stop` over the cold load (500 µs sampling), and **A/B runs with
  `Network.setBlockedURLs`** on one endpoint family at a time — the honest way to say "this page
  is slow *because of* X" rather than "X is also slow".
- Mobile: `Emulation.setDeviceMetricsOverride` 390×844, mobile=true, so `MobileHomePage` renders.
  Remote: `https://antiphon.desktop.codeperf.net` (Caddy → 17203, h2). On this machine that
  hostname resolves to the box's own Tailscale IP, so remote RTT here is ~0 — those rows show the
  *request-count* and *byte* cost of the remote path, not the user's 5G RTT.
- Live data during the sweep: 176 boards (81 of them under the "Antiphon (2)" project), 84 projects
  (48 test-shaped: `card-task-*`, `Catalog Test`, `CARD-0142 Repro …`), 49 agents, 540 agent tasks
  (486 Succeeded / 38 Failed / 15 Canceled / 1 Dispatched), the Antiphon board at 222 cards.

The probe is checked in at `scripts/perf/page-load-probe.py` so every slice in the plan can re-run
the exact measurement (`AP_ONLY=board AP_MODES=cold browser-harness < scripts/perf/page-load-probe.py`).

## 2. The per-page table (loopback, desktop 1600×1000, built bundle)

| Route (marker) | Cold → content | Warm → content | API calls | API bytes | Heap | Bottleneck (own evidence, §3) |
|---|---|---|---|---|---|---|
| `/` home (`home-dock`) | **2.1–3.2 s** | 1.4–1.8 s | 15 | 775 KB | 28–38 MB | bundle floor + attention/agents/agent-tasks wave then two dependent waves; CARD-0216 S2–S4 territory |
| `/` home, **mobile 390×844** (`NEEDS YOU`) | **1.5 s** (remote 2.5 s) | — | **183** | **2.58 MB** | 21 MB | `useAllBoardDetails` over 176 boards (1.8 MB) for the away-delta; not the gate, but it doubles `/api/attention`'s latency (1.2–1.4 s vs 0.45–0.6 s) by contention |
| `/boards/{Antiphon}` shape board (`card-row-CARD-0217`) | **1.5–2.3 s** | 1.6–2.3 s | 6 | **1.52 MB** | 34 MB | one 1.41 MB `/api/boards/{id}` (994 KB card descriptions + 184 KB terminalReason) parsed to render 2 rows / 990 nodes; uncompressed on the wire |
| `/boards` all boards (`CARD-0217` text) | **3.3–3.7 s** | 3.4–3.6 s | **181** | 1.81 MB | 36 MB | 176× `/api/boards/{id}` in one `Promise.all` gate; then all 224 cards rendered with descriptions (1.06 MB of text in the DOM) |
| `/agents` (`Antiphon-Orchestrator` in list) | **2.3–2.7 s** | 2.4–2.7 s | 40–41 | 249–572 KB | 74–79 MB | 32× `/projects/{id}/readiness` (git spawn each, 200–280 ms under fan-out), `/agents/{id}` 861 ms, 12.4k-node list re-rendered per response |
| `/settings` (templates tab) | 0.9–4.1 s ¹ | 1.3–4.4 s | **100** | 397 KB | 68–73 MB | every tab panel mounts (Mantine `keepMounted` default) → ProjectConfig's 84× readiness + 84-row table paid on every visit |
| `/settings?tab=projects` (`az-care` row) | **3.9–4.3 s** | 4.0–4.4 s | 100 | 397 KB | 70–109 MB | as above, and the marker is behind it |
| `/orchestrator` Cards tab (`Running Sessions`) | 0.8–1.3 s text, **then frozen to 8.7–9.6 s** | same | **182** | **2.44 MB** | **189 MB** | DelegationsBoard renders 540 tasks (14.8k nodes, 562 Papers) + DecisionsPanel's 176 board details — both mounted behind the visible tab |
| `/orchestrator?tab=delegations` (`CARD-0217`) | **9.1–10.8 s** | 9.3–10.4 s | 182 | 2.44 MB | 189 MB | A/B: block `/api/agent-tasks` → **0.6–1.3 s, 33 MB**; block board details → 8.2 s; block both → 0.9 s |
| `/orchestrator?tab=attention` | **8.7–9.4 s** | 0.9–1.0 s ² | 182 | 2.44 MB | 189 MB | same two loads; the panel itself is cheap |
| `/plans` catalog (`CARD-0216` in list) | **2.1–2.6 s** (one 7.0 s outlier: `/api/plans` 4.8 s) | 1.7–2.9 s | 4 | 74 KB | 28 MB | bundle floor + `/api/plans` 160–680 ms (repo scan; 30 s server cache) |
| `/plans?file=…` HEAD (`Verdict, in one screen`) | **0.9–1.6 s** | 1.3–1.8 s | 6 | **772 KB** | 21 MB | content 35 KB in 10–40 ms; the other 662 KB is `useAgentTasks()` for the `?task=` label |
| `/plans?file=…&ref=master` | **1.6–1.9 s** (one 4.4 s outlier: slow asset fetch) | 1.2–1.5 s | 6 | 771 KB | 21 MB | `git show` path costs 60–75 ms — **`?ref=` reading is not a latency problem** |
| `/channels` (`AZ Care`) | 1.4–2.2 s | 1.4–2.2 s | 5 | 84 KB | 37 MB | bundle floor; `/api/agents` fetched serially after `/api/channels` |
| `/workflows` | 1.25–1.7 s | 0.7–1.2 s | 7 | 47 KB | 20 MB | bundle floor + attention; nothing page-specific |
| `/agents/{id}/files` (`conversation-dock`) | 0.6–1.5 s | 0.5–1.7 s | 9–10 | 329–352 KB | 49–63 MB | two identical `/sessions/{id}/transcript?since=0` at 1.3 s each (duplicate fetch); full-screen route, no Layout |
| `/thread/:cardId`, `/workflow/:id` | not measured ³ | | | | | |

¹ The templates panel's own marker lands at 0.9–1.3 s in some runs and 3.7–4.1 s in others: the
page's data waves are the same every time (100 calls, `load` 3.8–4.5 s), but whether the first
commit with template names is scheduled before or after the readiness responses start landing is a
race. Either way the tab is not interactive until ~4 s.
² The "Needs attention" label is in the tab strip and appears at first paint; the *page* is still
frozen to ~9 s (`load` 8.9–16.4 s). The cold row uses the same marker and reads 8.7–9.4 s because
the first commit itself is blocked — see §3.5.
³ `/workflow/:id` needs a live workflow (`/api/workflows` is `[]`). `/thread/:cardId` was read, not
run: `CardThreadPage.tsx:19` calls `useBoard(thread.data?.card.boardId)` — the full 1.41 MB board
detail for one card's thread. It inherits the §3.2 fix.

**Repeatability:** the four cold runs per page span ≤ 1 s except the two named outliers (both
external: a 4.8 s `/api/plans` catalog scan, and one slow bundle fetch), so the ranking is stable.
Warm ≈ cold on every heavy page — **the bundle is not what makes them slow; their own data is.**

### 2b. Mobile and remote

| Route | Mobile 390×844, loopback | Mobile, remote domain | Desktop, remote domain |
|---|---|---|---|
| `/` | 1.5 s (183 calls, 2.58 MB) | 2.5 s (attention 1.17–1.44 s) | 4.0 s |
| `/boards/{Antiphon}` | 1.4 s | 1.9 s | 1.8 s |
| `/agents` | 2.5 s | 2.5 s | 2.6 s |
| `/orchestrator` (+ delegations) | 9.8 s / 8.9 s | 2.3 s text, `load` 9.5 s / 9.4 s | 2.1 s text, `load` 9.5 s |
| `/settings` | 4.7 s | 1.2 s (templates marker), `load` 4.2 s | 1.2 s, `load` 4.2 s |
| `/plans` | 1.5 s | 1.5 s | — |
| `/boards` | 3.4 s | — | — |

The remote path costs the same request counts and bytes as loopback (it is the same bundle and the
same uncompressed API); what it adds on the user's phone is RTT per request and phone CPU per byte
parsed — which is exactly why 183 requests / 2.6 MB on the mobile home and 1.41 MB raw JSON on the
board matter more than the loopback timings suggest.

## 3. Diagnoses, page by page

### 3.1 Home — desktop (CARD-0216 covers the phone; the desktop gate is still one call)

Waterfall (cold, ms from navigation): bundle done ~200–1,000; React mounts and fires
`/api/attention` + `/api/agents` + `/api/agent-tasks` + SignalR negotiate at ~600–1,700; second
wave (`files?since=checkpoint`, `review/threads`, `worktrees`, `workspaces`) ~400 ms later; third
wave (`files/commits`) ~1.1 s after that. `/api/attention` is the slowest first-wave call every
time (430–820 ms). Content at 2.1–3.2 s. The dependent waves and the `agents.isLoading` page gate
are CARD-0216 §4's explicit hand-off to this card. Nothing new here; the fix list stands.

### 3.2 Board pages — the payload is 30× what the shape view renders

`/api/boards/{Antiphon}` is 1,408,405 bytes for 222 cards. By field, summed across the cards:
`description` 993,795 B, `terminalReason` 184,365 B, `sessions` 36,433 B, `title` 20,101 B —
**84 % of the board is text the shape view never shows** (`CardRow.tsx:20` dropped the description
from the tile on purpose; the page renders 2 card rows and 990 DOM nodes). The server builds it in
365–460 ms. It is served **uncompressed** — `Content-Encoding` is absent from 17202, from the Vite
proxy on 17203, and from the Caddy front door (1,421,693 B on the wire with `Accept-Encoding:
gzip, br`). `server/Program.cs` has no `AddResponseCompression`.

`/boards` (all boards) is the same payload ×176 — `useAllBoardDetails` (`api/boards.ts:422`) is one
`useQuery` whose `queryFn` is `Promise.all(boardIds.map(...))`, so the page shows a single spinner
until the slowest of 176 requests returns (3.3–3.7 s; under HTTP/1.1's six connections that is
~30 requests deep), then `AllCardsBoard` renders all 224 cards *with* descriptions
(`BoardCard.tsx:45`, `lineClamp={2}`) — 1.06 MB of text in the DOM. `MobileHomePage.tsx:91` and
`DecisionsPanel.tsx:19` call the same hook over the same 176 boards, for a card list neither needs
whole (the away-delta wants "cards that changed since I last looked"; decisions wants the cards in
one status).

Corrective shape, not built here: a board *summary* representation (description preview ≤ ~200
chars, no `terminalReason`, no `sessions`) for every list surface, with `CardModal` fetching the
existing `GET /api/cards/{id}` for the full text on open; and one `GET /api/cards?updatedSince=` /
`?status=NeedsDecision` for the two "all cards" consumers instead of 176 round trips.

### 3.3 Agents — a per-project git spawn per row, and a list that re-renders per response

`AgentsPage.tsx:72` → `useProjectReadinessList(projectIds)` fires `/projects/{id}/readiness` for
every distinct project any agent belongs to (32 today). Each readiness call runs
`ProjectSetupService.GetReadinessAsync`, which spawns `git remote get-url origin`
(`ProjectSetupService.cs:304-331`) — 70 ms alone, 200–280 ms each when 32 land together. The
selected agent's `GET /agents/{id}` takes 861 ms (`AgentService.GetByIdAsync` does the runner
round-trip plus `IsSessionWorkingAsync`, the per-agent shape of CARD-0216 §2.3's batch fix). The
list itself is 12,431 DOM nodes and 1,550 inline SVGs for 49 agents, and the agent name marker
lands only at 2.3–2.7 s although `/api/agents` returns at ~0.6 s: the CPU profile is React
render + GC (543 ms GC, `jsx`/`jsxDEV` on top), consistent with the unmemoised 49-row list
re-rendering as each of the 32 readiness responses resolves.

The A/B that blocks `/readiness` from the client **wedged the page** (the probe's
`Runtime.evaluate` timed out twice) — 32 failing queries × React Query's default `retry: 3` with
backoff is a retry storm. That is a finding of its own for the slice: measure with the endpoint
stubbed server-side, and give the readiness queries `retry: 1`.

### 3.4 Settings — six tabs, all mounted, one of them owns 84 projects

`SettingsPage.tsx` renders six `Tabs.Panel`s; Mantine's `keepMounted` defaults to **true**, so
`/settings` (any tab) mounts `ProjectConfig`, which fires `useProjectReadinessList` over all 84
projects (`ProjectConfig.tsx:84`) and renders an 84-row table with a `ProjectReadinessPanel` per
row, plus `AgentTuiConfig` (`/agent-tui/profiles`, `/agent-tui/runner-types`), `StatusTab`
(`/github/status`, `/github/repos`), `TemplateManager` (templates + per-template model-routing).
100 API calls, `load` 3.8–4.5 s, heap up to 109 MB — on a page whose default tab needs three
small calls. The readiness fan-out is the same git spawn as §3.3, ×84, and 48 of those 84 projects
are test residue.

### 3.5 Orchestrator — a 540-task board and 176 board details behind whichever tab you opened

Same `keepMounted` mechanism (`OrchestratorPage.tsx`), worse contents. `DelegationsBoard.tsx:36`
takes `useAgentTasks()` (662 KB, 540 tasks) and renders **all of them** as lane cards — 562
`Paper`s, 1,952 badges, 14,848 DOM nodes; `DecisionsPanel.tsx:19` adds the 176-board fan-out.
The result is a page whose visible Cards tab paints at 0.8–1.3 s and then **freezes for ~8 s**
(one uninterrupted main-thread block in every run; `load` 8.7–9.7 s; heap 189 MB; CPU profile
16 s busy of which 3.4 s is garbage collection). The A/B isolates it cleanly:

| Blocked | Delegations tab content | Attention tab | Heap |
|---|---|---|---|
| nothing (warm) | 9.5 s | — | 189 MB |
| `/api/boards/*` (176 details) | 8.2 s | — | 188 MB |
| `/api/agent-tasks` | — | 0.6 s (page usable 1.3 s) | **33 MB** |
| both | — | 0.9–1.6 s | 23 MB |

**The 540-task render is ~7–8 s of it; the board fan-out ~1 s.** Any slice that leaves the
DelegationsBoard rendering the whole task history cannot reach the target.

### 3.6 Plans — `?ref=` is fine; the task list is the dead weight

`/api/plans` (catalog, 68 KB): 160–680 ms typical, one 4.8 s cold-scan outlier; server-side 30 s
cache. `/api/plans/content?file=…&ref=master`: 70 ms (`GitWorkspaceService.GetContentAtAsync` →
`git show`); the same file at HEAD: 10–40 ms. The plan body (35 KB) is not the cost. What is:
`PlanReaderPage.tsx:206` calls `useAgentTasks()` — the full 662 KB list, polled every 15 s while
the plan is open — to look up one task by id for the `?task=` label. `GET /api/agent-tasks/{id}`
exists (`api/agentTasks.ts:235`).

### 3.7 Light pages — the floor every route pays

Workflows, channels, agent files, and the HEAD plan reader are within 0.3–1.2 s of the target and
have no page-specific fan-out. Their cost is the shared floor: bundle download (200–650 ms cold on
loopback for 839 KB gz) → script evaluation and first mount (first API call fires 300–500 ms after
the script finishes) → the `/api/attention` call the nav bar makes on every page. Agent files
additionally fetches the same transcript twice (`/sessions/{id}/transcript?since=0` at 703 and
756 ms, 1.3 s each) — two subscribers, no shared query.

## 4. Shared causes — fix once, not per page

1. **The served bundle is a development React build.** `client/dist/assets/index-BUjwjInJ.js`
   (3,001,847 B) contains 3,683 `jsxDEV` references and React's dev-only warning strings (`should
   have a unique "key" prop`); a plain `vite build` of the same commit produces 2,530,590 B with 3.
   Cause, with high confidence: Aspire's `AddNpmApp` sets `NODE_ENV=development` on the process it
   starts (`Antiphon.AppHost/Program.cs:87`, `npm run serve`), `serve.mjs` passes its own env
   through to `vite build` / `vite build --watch` (`serve.mjs:57-66,168`), and Vite documents that
   `NODE_ENV=development vite build` builds in development mode. Every page pays it: `e.jsxDEV` is
   a top-five self-time entry on the orchestrator, settings and agents profiles (120–200 ms each),
   dev-mode reconciliation is slower throughout, and it is 470 KB more to download and parse.
   **The slice must verify the cause first** (log `process.env.NODE_ENV` from serve.mjs) — the
   evidence here is the artefact, not the env var read directly.
2. **No API response compression anywhere on the path** (Kestrel, Vite proxy, Caddy). Irrelevant on
   loopback; on the phone it is the difference between 1.41 MB and ~150 KB for a board and 662 KB
   vs ~70 KB for the task list, both of which several pages fetch and two of them poll.
3. **One 2.5–3.0 MB entry chunk, every route statically imported** (`App.tsx` imports all twelve
   pages; only `MermaidDiagram` and `MonacoEditor` are lazy). Minified-byte attribution via the
   sourcemap: app code 436 KB, `@xterm/xterm` 333 KB (SessionTerminal — a modal), `@mantine/core`
   241 KB, react-dom 175 KB, `highlight.js` 156 KB + `hast-util-raw` 128 KB + markdown-it 68 KB
   (markdown rendering), prosemirror + tiptap ~290 KB (the card editor), SignalR 54 KB. Routes and
   the three heavy widgets are the obvious split lines; react-icons is already tree-shaken (55 KB).
4. **Mantine `Tabs` mounts every panel** (`keepMounted` defaults true) — Settings and Orchestrator
   pay for all their tabs on every visit. `CardModal` already passes `keepMounted={false}` for
   exactly this reason; the two pages don't.
5. **Unbounded fan-outs from three hooks**: `useAllBoardDetails` (176 boards — mobile home,
   `/boards`, DecisionsPanel), `useProjectReadinessList` (84 / 32 projects — Settings, Agents),
   and the per-selected-agent detail. Each fans out client-side, one request per row, with a git
   spawn or a 1.4 MB payload behind the worst rows.
6. **Unbounded list payloads polled on a timer**: `/api/agent-tasks` (662 KB, 540 rows, every 15 s
   — home, plan reader, delegations); `/api/boards/{id}` (1.41 MB with full descriptions);
   `/api/agents` (74 KB every 5 s, on top of SignalR invalidation).
7. **`/api/attention` on every page** — `Layout.tsx:173` renders `DecisionsBadge`, which calls
   `useAttention()` (15 s interval). 430–820 ms alone, 1.2–1.5 s when a fan-out is competing for
   the same six connections. CARD-0216 §4 deferred its per-open-task git probe to this card.
8. **Test residue in live data**: 176 boards (81 under "Antiphon (2)"), 84 projects (48
   test-shaped). Every fan-out above is multiplied by it. Real usage is ~10 boards and ~10 projects.

## 5. What was NOT done / not claimed

No production code was touched; the only additions are this doc, the plan, and the probe script.
`/workflow/:id` was not measured (no live workflow); `/thread/:cardId` was read, not run. The
readiness-blocked A/B for Agents/Settings failed (page wedge, §3.3) and is recorded as such rather
than estimated. Remote-domain rows were taken from this machine (RTT ≈ 0) and show request counts
and bytes, not the phone's real latency — CARD-0216's arithmetic (RTT × request count) is the
right way to project them. Long-task timing came back empty from the `PerformanceObserver` in the
occluded tab, so main-thread blocking is evidenced by the CPU profiles and the DOM-commit timings,
not by long-task entries.
