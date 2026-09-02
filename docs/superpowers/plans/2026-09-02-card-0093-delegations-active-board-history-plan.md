# CARD-0093 — Delegations board: active work on the board, settled work in a History tab

**Date:** 2026-09-02 (Plan pass, task 9007a2d8 — design only; no production code changed)
**Card:** CARD-0093 "The delegations fan-out board shows every task ever created - it needs to
default to active work, with settled tasks moved to a separate history view"
**Builds on:** [`2026-09-02-card-0092-running-sessions-visibility-plan.md`](2026-09-02-card-0092-running-sessions-visibility-plan.md)
(same method: check whether the Home rail already answers the complaint before building),
[`2026-09-02-card-0002-home-tasks-section-plan.md`](2026-09-02-card-0002-home-tasks-section-plan.md)
(Home Tasks rail, Done today), [`2026-09-02-card-0031-project-status-view-plan.md`](2026-09-02-card-0031-project-status-view-plan.md)
(liveness / elapsed / queue reason, Done today) and
[`2026-08-28-card-0217-sub-second-pages-plan.md`](2026-08-28-card-0217-sub-second-pages-plan.md) §2.4
(the 7-day window, `Show all`, `/summary`, lane virtualisation — shipped `856372b2`, 2026-08-28).

**Sources (verified this pass, head `d8477506`, live 17202 on 2026-09-02):** CARD-0093, CARD-0002
(Done), CARD-0031 (Done), CARD-0092 (Backlog → plan landed today), CARD-0217 (`856372b2`),
`client/src/features/delegations/{DelegationsBoard,TaskTree,TaskChip,TaskDrawer}.tsx`,
`taskVisuals.ts`, `DelegationsBoard.test.tsx`, `DelegationsBoard.stories.tsx`,
`client/src/api/agentTasks.ts`, `client/src/features/orchestrator/OrchestratorPage.tsx`,
`client/src/features/home/{HomePage,MobileHomePage,awayDelta,workLineFormat,taskReview}.ts*`,
`client/src/features/home/tasks/{TasksSection,TaskCard}.tsx`, `client/src/hooks/useSignalRInvalidation.ts`,
`server/Api/Endpoints/AgentTaskEndpoints.cs`, `server/Application/Services/AgentTaskService.cs`
(`ListAsync` `:1037-1067`, `GetListSummaryAsync` `:1069`), `server/Application/Services/HomeTaskService.cs`,
`server/Application/Settings/DelegationSettings.cs`, `tests/Antiphon.Tests/Application/AgentTaskCardBindingTests.cs:294`,
`tests/Antiphon.E2E/ContractSnapshotTests.cs:316`, `docs/features/007-multi-agent-orchestration/proposal.md` §2.9,
`docs/antiphon-api.md:205-216`, and the live endpoints `/api/agent-tasks`, `/api/agent-tasks?since=`,
`/api/agent-tasks/summary`, `/api/home/tasks`.

---

## Verdict up front

**Not satisfied by the Home rail. Do not close as superseded. The ask stands, on exactly the
component the card names, and it is a client-only change of about 1.25 days.** The card's stated
root cause is half stale (CARD-0217 already added a 7-day window and `Show all`), but the thing
the user complained about is unchanged: the board's main screen is still almost entirely history.

Measured at the same instant on 2026-09-02:

| Surface | Showed |
|---|---|
| `GET /api/agent-tasks` (no filter — what `MobileHomePage` still fetches) | 815 tasks: Succeeded 724 · Failed 55 · Canceled 30 · Dispatched 3 · Blocked 3 |
| `GET /api/agent-tasks?since=<now − 7 d>` (what `DelegationsBoard` fetches by default) | **320 tasks: 314 settled, 6 open** (3 Dispatched, 3 Blocked, 0 Queued, 0 Working) |
| Settled inside the window, by recency | last 1 h: 1 · 3 h: 7 · 6 h: 15 · 24 h: 113 · 48 h: 180 · 72 h: 219 |
| Shape of the window | 320 tasks under **319 roots**, all `Worker`, one task at depth 1; 308 bound to a card, 12 unbound |
| `GET /api/agent-tasks/summary` | active 3 · blocked 3 · runs 812 · spend $9,053 |
| `GET /api/home/tasks` (Home rail) | 169 items: Needs you 2 · Running 3 · To review 34 · Up next 70 · Done 60 (cap); sources Card 157 · **Delegation 12** |

So the default board today renders a fan-out tree of 319 root rows and a *Done* lane of 314 chips
beside three *Working* and three *Blocked* chips. Ninety-eight per cent of the main screen is the
record, not the work. That is the user's complaint, verbatim, two weeks on.

### The three questions the brief asked

**1. Is CARD-0093 now satisfied by the Home rail's Running/Done grouping?** No, for a structural
reason, not a cosmetic one. The rail is *per-project* and *item-major*: a bound delegation is
never its own item — it folds into its card's single worker line (`HomeTaskService.RankCard`,
`openBound ?? newest`; CARD-0002 decision 2). Live, **308 of the 320 tasks in the window are
bound**, so the rail shows twelve of them as items and the rest only as "the newest bound task of
this card". A settled bound task older than its card's newest one is on no Home surface at all.
The rail's *Done* group is also capped at 60 mixed Cards-and-tasks over 7 days, per project. It
is the right "what finished for this project" glance and the wrong "every delegation, as a
record" view — which is why the rail itself links out with "Open delegations →", "+N more → open
delegations" (`TasksSection.tsx:160,183`), the kebab's "Open delegations" (`TaskCard.tsx:320`),
and the mobile away band's "all settled work · open the delegations board" (`MobileHomePage.tsx:268`).
**Every surface built today treats the Delegations board as where the history lives.** The board
is the history view; it just has no active view in front of it.

**2. Does `DelegationsBoard` serve a genuinely different purpose?** Yes, four of them, none
replaced by the rail: the fan-out tree ("who asked for what", `buildTaskForest` + `TaskTree`,
the only tree over `parentTaskId` in the client), the per-run filter (*Only this run* over
`subtreeIds`), one chip per task with tier / role / workspace / escalation / elapsed / cost, and
the drawer with brief, untouched report, event timeline and Retry / Escalate / Cancel / Reroute
(CARD-0090). Feature 007 §2.9 designed it as the second tab of `/orchestrator` for exactly this.
Both `CARD-0092`'s plan (decision 3: "the Delegations tab stays the *task* tree") and `CARD-0031`'s
plan ("What this card does not do: … the delegations board") explicitly leave it alone.

**3. What is actually stale in the card?** The claim that the client "never passes" a filter.
CARD-0217 S4 (`856372b2`) added `since` and `status` to `GET /api/agent-tasks` (server default
unchanged for scripts), `GET /api/agent-tasks/summary` for the header counters, a rolling 7-day
`since` on the board with a *Show all* toggle, and `@tanstack/react-virtual` lanes. That was a
**performance** fix (9.5 s → sub-second) and it worked; it was never the UX fix, and its own
words say so: "home shows recent work, not history". `DelegationSettings.DefaultWindowDays` was
added alongside and is read by nothing on the server — the client constant
`DELEGATIONS_DEFAULT_WINDOW_DAYS = 7` is the truth (left open below).

### Root cause, restated against today's code

`LANES` (`taskVisuals.ts:71-76`) still files every `Succeeded | Failed | Canceled` row into
*Done*, and the board's fetch (`DelegationsBoard.tsx:41`) asks for seven days of them. The tree
is built over the same set. There is no notion of "active" anywhere in the component; the
server already has one — `ListAsync`'s `since` clause keeps *every* non-settled row regardless
of age and trims *only* settled rows by `CompletedAt` (`AgentTaskService.cs:1055-1061`, pinned
by `AgentTaskCardBindingTests.cs:294`). That clause is the whole fix: the board needs a
**short** window, not a seven-day one, and the seven-day one needs its own tab.

---

## Decisions

Numbered to match the card's "What a Plan pass will need to decide".

1. **History is its own tab on `/orchestrator`: `?tab=history`.** Not a toggle inside the
   Delegations tab (the card itself flags that as probably not "somewhere else"), not a new
   route (a new nav entry and Layout change for a view that shares every component with the
   board). The page's contract is "tabs answer one question each and live in the URL"
   (`OrchestratorPage.tsx`, pinned by "puts the tab in the URL"). *Delegations* becomes "what is
   in flight"; *History* is the record. Every existing deep link `?tab=delegations&task=<id>`
   keeps working for a settled task because the drawer fetches by id (`useAgentTask`) and never
   depended on the list — S1 pins that with a test.

2. **"Recently done enough to still show" = settled within the last 60 minutes, as a *Just
   settled* lane.** The card's own counter-example (a task that settled thirty seconds ago,
   mid-handoff) is real: an orchestrator watching its delegate wants to see the chip go green or
   red without changing tab. Sixty minutes bounds that lane at a handful of chips even on the
   busiest measured day (113 settled / 24 h ⇒ ~5 per hour), and it is one constant
   (`DELEGATIONS_ACTIVE_GRACE_MINUTES = 60`). The lane keeps its `done` key and `lane-done`
   test id; its label becomes *Just settled* with the hint "settled in the last hour · older in
   History →". Older settled work is not on the board at all. Blocked, Queued, Dispatched and
   Working rows stay regardless of age — the server clause already guarantees it, and a
   three-week-old Blocked question is precisely what must not vanish.

3. **Reuse `since`; no server change; no new server default.** The active board fetches
   `?since=<now − 60 min>` (new `since: 'active'` resolution beside `'default'`); History fetches
   `?since=<now − 7 d>&status=Succeeded,Failed,Canceled` with *Show all* dropping `since` and
   keeping `status` — the first real use of the `status` option the client has carried since
   CARD-0217. The endpoint's unfiltered default stays as it is: `delegate.ps1`, the contract
   snapshot (`ContractSnapshotTests.cs:316`, `?rootId=`) and every script keep their contract.
   The two views have distinct query keys (`since` and `status` are in `agentTaskKeys.list`), so
   SignalR's `['agentTasks','list']` prefix invalidation (`useSignalRInvalidation.ts:142`) and the
   mutation prefix invalidation in `agentTasks.ts` refresh both.

4. **History lists tasks individually, newest settled first, in a dense virtualised table — no
   roll-up by root.** Live: 319 roots for 320 tasks; a per-root roll-up would be the same list
   with a chevron on one row. The table keeps the parent relationship visible where it exists
   (a `↳` marker and the root's title, dimmed, on a row with `parentTaskId`), which is the
   honest amount of tree for a record. Columns: settled-at, outcome, title (+ `CARD-nnnn` chip
   when bound — 96 % are), role, tier, agent, duration, cost, unread dot. Row click opens the
   same `TaskDrawer`. Chips and lanes are the wrong shape for a record: the record is read by
   time, scanned for red, and filtered by outcome.

5. **One definition of "active", shared with CARD-0092, not a third one.** Active =
   `status ∉ {Succeeded, Failed, Canceled}`, which is what the server's `since` clause, the Home
   projection's `IsOpenBound`, and `awayDelta.ts`'s `SETTLED` all already encode. CARD-0092's
   Running Sessions table shows the *session-level* subset of that (Dispatched/Working with a live
   pty; Blocked and Queued excluded on purpose because they are waiting, not running). This board
   shows the *task-level* whole of it — Queued, Working, Blocked lanes are exactly "waiting for a
   slot / on it / needs me". The two agree on what is *not* active, which is the only thing the
   user's complaint needed them to agree on.

6. **Header counters stay where they are.** The board's `runs · working · blocked · spend`
   badges come from `/summary` (fleet-wide, deliberately independent of the window — commit
   `2d57e53e` restored the spend badge after S4 dropped it). They render on both tabs unchanged.

7. **Links that mean "the record" point at History; links that mean "this task" keep the
   drawer.** Retarget the four call sites whose text says history (S3). Attention rows and any
   link carrying `&task=` keep `?tab=delegations` — the drawer opens on arrival either way.

---

## Ground truth (checked, not guessed)

Line numbers as of `d8477506`.

| Claim in the card (2026-08-19) | Today | Consequence |
|---|---|---|
| Client calls `GET /agent-tasks` with no filter | Board passes `since=<now − 7 d>` (`DelegationsBoard.tsx:41`, `agentTasks.ts:345-366`); `HomePage` ×2 pass it too; **`MobileHomePage.tsx:55` still fetches unfiltered** (815 rows) | Window exists; it is the wrong length for the board. Mobile is left open |
| Server endpoint supports one `status` | Supports `rootId`, `status` (comma list), `includeChecks`, `since` (`AgentTaskEndpoints.cs:32-40`); **none documented** in `docs/antiphon-api.md:210` | S3 documents them |
| Every terminal task sits in *Done* forever | Every terminal task from the last 7 days: 314 live | Decision 2 |
| "50+ tasks a day" | ~40 / day over the last week (320 / 7); 113 in the last 24 h | Sixty-minute grace ⇒ ≤ ~5 chips typical |
| Check-role tasks are hidden by default | Still true (`ListAsync` `Role != Check` unless `includeChecks`) | Untouched |
| CARD-0044 retention is the wrong tool | Still true: `TaskRetentionDays` 180 is disk cleanup | Untouched |
| CARD-0092 should share the "running" signal | CARD-0092's plan landed today; joins through `AgentTask.Status` | Decision 5 |
| Board story seeds the board's query | `DelegationsBoard.stories.tsx:39` seeds `agentTaskKeys.list()` (key `…,null,null`); the board reads `agentTaskKeys.list(false,{since:'default'})` (key `…,'default',null`) and `agentTaskKeys.summary()` is unseeded | **Pre-existing since `856372b2`, by key inspection: the `Board` story fetches live and shows a loader.** S2 fixes the seed; `docs/ui-screenshots/delegations-board--board.png` dates from 2026-08-07 |
| `TaskTree` empty text | "No delegated tasks yet. Start one with New task…" (`TaskTree.tsx:29-32`) | Wrong once the board is active-only and 812 runs exist; S1 splits it |
| Drawer depends on the list | No — `TaskDrawer` → `useAgentTask(id)`; `drawerId` seeds from `?task=` (`DelegationsBoard.tsx:45-46`) | Deep links to settled tasks survive; S1 pins it |
| Home rail shows bound delegations as items | Never (`HomeTaskService.LoadUnboundTasksAsync` `:148-150`; CARD-0002 decision 2) | Question 1 above |

---

## Slices

### S1 — Active board: sixty-minute window, *Just settled* lane, honest empty state

**Files:** `client/src/api/agentTasks.ts` (`AgentTaskListOptions.since` gains `'active'`;
`DELEGATIONS_ACTIVE_GRACE_MINUTES = 60` beside `DELEGATIONS_DEFAULT_WINDOW_DAYS`;
`queryForAgentTasks` resolves it), `client/src/features/delegations/taskVisuals.ts`
(`LANES[done]` label *Just settled*, hint; export `SETTLED_STATUSES` and `isSettled(status)`
— `laneOf` unchanged), `DelegationsBoard.tsx` (fetch `{ since: 'active' }`; remove `showAll`
and the *Show all* button — it moves to History; lane header for `done` gets a dimmed
`Anchor` "older in History →" to `/orchestrator?tab=history`), `TaskTree.tsx` (empty state
takes `runs` from the summary: `0` → today's text; `> 0` → "Nothing in flight. N runs settled —
open History →" with the anchor).

**Tests** (`DelegationsBoard.test.tsx`):

1. "requests the active window by default" — the single `since` parameter parses to within
   `60 min ± 5 s` of `Date.now()` and no `status` parameter is sent. Replaces "requests the
   configured recent window by default, then the complete history on Show all".
2. "lands each task in the lane that says what it needs" — unchanged assertions, plus the lane
   header reads *Just settled* and carries the History anchor with `href="/orchestrator?tab=history"`.
3. "says what an empty board is for" — two cases: summary `runs: 0` keeps the existing sentence;
   `runs: 812` renders the "Nothing in flight" sentence with the History anchor.
4. "opens the drawer for a settled task named in the URL even when it is not on the board" —
   render at `/orchestrator?tab=delegations&task=<id>` (`window.history.pushState` before
   `renderWithProviders`, as the page test does) with the list mock omitting the id and
   `GET /api/agent-tasks/<id>` returning a detail whose summary is `Succeeded`; the drawer title
   appears.
5. Virtualisation, tier, Grok alias, nesting, collapsed-subtree, drawer-on-chip and *Only this
   run* tests are unchanged.

**Verify:** `pwsh -File scripts/test-client.ps1 DelegationsBoard` and `taskVisuals`.

### S2 — History tab

**Files:** new `client/src/features/delegations/DelegationsHistory.tsx` (+ `.test.tsx`),
`client/src/features/orchestrator/OrchestratorPage.tsx` (`TABS` gains `'history'`; tab after
*Delegations*, icon `TbHistory`, label *History*; panel renders `DelegationsHistory`),
`OrchestratorPage.test.tsx`, `DelegationsBoard.stories.tsx` (fix the seed keys; add a `History`
story seeded with the fixture's settled rows under the History key).

`DelegationsHistory`:

- Fetch: `useAgentTasks(false, { since: showAll ? undefined : 'default', status: SETTLED_STATUSES })`
  plus `useAgentTaskListSummary()`. Header: *History* title, the same four summary badges, a
  `SegmentedControl` *All / Succeeded / Failed / Canceled* (client-side filter over the fetched
  set — it is already in memory), the *Show all* / *Last 7 days* toggle moved here from the
  board, a "N settled" count, the refresh icon.
- Rows: sorted by `completedAt` descending (server returns `CreatedAt` ascending); fixed-height
  rows in a scroll box, windowed with `useVirtualizer` the way `VirtualTaskLane` does
  (`estimateSize` ≈ 36, `initialRect` for the first paint, the same "first viewport when virtual
  items are empty" guard for DOM tests). Column grid: settled-at (`formatClockTime` today,
  date otherwise; full instant in a tooltip) · outcome `Badge` (`STATUS_COLOR`) · title with
  `cardIdentifier` chip when bound and a dimmed `↳ <root title>` when `parentTaskId` is set
  (root looked up in the fetched set; absent root ⇒ marker only) · role · `TierBadge` · agent
  name or short id · `formatDuration(elapsedSeconds)` with the `~` recovered marker
  (`completionObserved`) · `formatCost` with the CARD-0023 legacy marker (`isLegacyCostEstimate`)
  · unread dot via `isUnreadDeliverable` from `features/home/taskReview.ts`.
- Row click → `TaskDrawer` (shared); `?task=` on arrival opens it exactly as the board does.
- Empty states: window empty and `runs > 0` → "Nothing settled in the last 7 days — Show all";
  `runs === 0` → the board's "no delegated tasks yet" sentence.

**Tests** (`DelegationsHistory.test.tsx`):

1. Default request carries `since` (7 d) **and** `status=Succeeded,Failed,Canceled`; *Show all*
   drops `since` and keeps `status`.
2. Rows are newest-settled first; a `Working` row in the mock (defensive — the server should
   not send one) is not rendered.
3. Outcome filter *Failed* leaves only the Failed row; *All* restores.
4. A bound row shows its `CARD-nnnn` chip; a depth-1 row shows the `↳` marker with its root's title.
5. An unread Succeeded deliverable shows the dot; a read one does not.
6. Row click opens the drawer with the delegate's words (mirror of the board test).
7. 600 settled rows render fewer than 80 row elements.

`OrchestratorPage.test.tsx`: "`?tab=history` renders the History panel, and the Delegations
panel is not mounted" (mirrors "defers the delegations request until its tab is opened").

**Verify:** `pwsh -File scripts/test-client.ps1 DelegationsHistory`, `OrchestratorPage`,
`DelegationsBoard`.

### S3 — Links, docs, screenshots, browser check

**Files:** `client/src/features/home/tasks/TasksSection.tsx:183` (`MoreLink` for the *Done*
group → `?tab=history`; other groups unchanged), `client/src/features/home/tasks/TaskCard.tsx:320`
(kebab *Open delegations* → `?tab=history&task=<id>` when `item.group === 'Done'`),
`client/src/features/home/workLineFormat.ts:71` (`workLineTarget`: settled task → history),
`client/src/features/home/MobileHomePage.tsx:268` ("all settled work" → `?tab=history`); their
tests pin hrefs — update each pinned string, add none. `docs/antiphon-api.md:210`: document
`rootId`, `status` (comma list), `includeChecks`, `since` and `GET /api/agent-tasks/summary`.
`docs/features/007-multi-agent-orchestration/proposal.md` §2.9 "As built": one sentence —
lanes are active-only with a sixty-minute *Just settled* grace; settled work is the *History*
tab (CARD-0093). Regenerate `docs/ui-screenshots/delegations-board--*.png` and add
`delegations-history--history.png` (`npm run screenshots -- delegations`, from `client/`).

**Browser (user rule: UI work is not done on tests alone; `client-mode.ps1 -Status` before
trusting 17203):** open `/orchestrator?tab=delegations` — the tree is a handful of rows, the
*Just settled* lane holds only the last hour, the header still says `812 runs`; open
`?tab=history` — hundreds of rows scroll in one frame, *Failed* filter works, a row opens the
drawer; open `/orchestrator?tab=delegations&task=<a settled id from History>` — the drawer opens
on a board that does not list it; Home rail "+N more → open history" and mobile "all settled
work" land on the History tab.

**Verify:** `pwsh -File scripts/test-client.ps1 features/home` (TasksSection, TaskCard,
workLineFormat, MobileHomePage pinned hrefs).

---

## What this card does not do

- **Any server change.** No new endpoint, no default change on `GET /api/agent-tasks`, no
  pagination, no storage. The `since` clause and the `status` filter already exist and are tested.
- **Roll-up by root, or a tree in History.** Decision 4; 319 roots for 320 tasks.
- **Changing Home's groups, caps, or the rail's Done semantics** (CARD-0002/0031 own them), the
  Running Sessions table (CARD-0092), the mobile bands beyond one href, or CARD-0044 retention.
- **A badge on the Delegations tab**, making it the default tab, or renaming it.
- **Deleting or archiving settled tasks.** Hiding is a view; the record stays queryable.
- **Wiring `DelegationSettings.DefaultWindowDays`.** Left open — it is dead configuration today
  and this card must not silently make it live.

## Left open, deliberately

1. **`Delegation:DefaultWindowDays` is read by nothing.** Either expose both windows through
   `/summary` and drop the client constants, or delete the setting. One-line card.
2. **`MobileHomePage.tsx:55` fetches the unfiltered list (815 rows).** CARD-0217 §2.4 said the
   Home subscribers "pass `since=` once the parameter exists"; the desktop ones do, mobile does
   not. `awayDelta` needs only tasks settled since `lastSeen` (fallback 12 h), so `since:
   'default'` is safe there. One-line card, not this one's.
3. **Server-side paging for History** once *Show all* passes ~5 k rows (a year at today's rate);
   CARD-0217 measured 540 rows painting in one frame under virtualisation, so not yet.
4. **The grace window as a per-viewer preference** (`localStorage`) if sixty minutes turns out
   to be the wrong constant for how the user actually watches a run.
5. **A `Failed` quick filter on the board itself** — a failed task older than an hour is in
   History; if operators want "recent failures" on the main screen, the attention feed's
   `RecentFailure` rows are the existing answer.

## Test matrix

| Layer | Test | Command |
|---|---|---|
| Client S1 | `DelegationsBoard.test.tsx` (4 changed/added), `taskVisuals.test.ts` (`isSettled`) | `pwsh -File scripts/test-client.ps1 DelegationsBoard` / `taskVisuals` |
| Client S2 | `DelegationsHistory.test.tsx` (7), `OrchestratorPage.test.tsx` (+1) | `pwsh -File scripts/test-client.ps1 DelegationsHistory` / `OrchestratorPage` |
| Client S3 | `TasksSection`, `TaskCard`, `workLineFormat`, `MobileHomePage` pinned hrefs | `pwsh -File scripts/test-client.ps1 features/home` |
| Server | none changed; `AgentTaskCardBindingTests` (`since` semantics) stays green untouched | not run unless the executor touches `AgentTaskService` |
| Screenshots | `delegations-board--*.png`, `delegations-history--history.png` | `npm run screenshots -- delegations` (from `client/`) |

No `dotnet` build is needed for S1–S3. If a slice does touch the server, build to
`--property:OutputPath=bin-card0093/` (forward slash) and delete every `bin-card0093` directory
before finishing.

## Sequencing, estimate, risks

**Order:** S1 → S2 → S3, one worker, one landing; Shared workspace is fine (nothing here touches
the contract fixture directory). **Estimate:** S1 0.25 d, S2 0.75 d, S3 0.25 d — about 1.25 d.

| Risk | Disposition |
|---|---|
| A child still active whose root settled over an hour ago | `buildTaskForest` already treats a parent-less row as a root ("a filtered view can never silently drop work"); *Only this run* works on that root. Pinned by the existing nesting tests; no change |
| An operator loses a just-failed task from the board after an hour and does not know where it went | The lane header's "older in History →" anchor and the empty-state sentence both name the tab |
| Summary `active` disagrees with the lane count | Both are fleet-wide over the same statuses; the window only trims settled rows |
| History *Show all* over the full table (815 today) | Same virtualiser and the same 540-row measurement as CARD-0217; rows are lighter than chips |
| Story seed keys drift again when options change | S2 seeds via `agentTaskKeys.list(false, {...})` with the literal options each view uses, and the `History` story asserts nothing loads over the network by the repo's no-MSW convention |
| Pinned href tests in `features/home` | Four string edits, each named in S3; no new assertions |

## Execution notes

- Keep lane keys and `data-testid="lane-<key>"` byte-identical; only the `done` lane's label and
  hint change.
- Resolve `since: 'active'` at request time like `'default'` — never bake a timestamp into the
  query key, or the window stops rolling.
- History's client-side outcome filter is over the fetched set on purpose; do not add a second
  `status` round-trip per segment.
- `TaskDrawer` is shared, not duplicated; `?task=` handling in History is the same three lines
  as the board's.
- When closing the card, record the verdict on the move revision: "narrowed to `DelegationsBoard`
  — active-only board with a sixty-minute *Just settled* lane, settled work on a new History tab
  of `/orchestrator`; the Home rail (CARD-0002/0031) does not replace the board because bound
  delegations (96 % live) are never rail items and every other surface links here for the record."
