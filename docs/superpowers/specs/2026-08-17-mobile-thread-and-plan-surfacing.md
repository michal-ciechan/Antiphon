# The work thread on a phone — surfacing plans, and one IA for the seven UX cards

- **Status**: Proposed (planning only — task `fef0136f`; nothing here is implemented)
- **Date**: 2026-08-17
- **Cards reconciled** (no new card filed, per the brief): CARD-0002, CARD-0031, CARD-0032,
  CARD-0033, CARD-0034, CARD-0035, CARD-0036. Prior art: CARD-0042 (shipped mobile StatePager).
- **Concurrent work respected** (§8): CARD-0035 slices 4–6 and CARD-0058 slice 6 are being
  implemented right now; no slice here touches their files, and slice M5 is explicitly sequenced
  behind CARD-0035 slice 6.
- **Visual proposals**: Storybook stories under `client/src/stories/mobile-proposal/` and
  screenshots under `docs/ui-screenshots/proposals-*` accompany this spec (§7).

## 0. What exists today (verified against the code, 2026-08-17)

### The four scattered records of one piece of work

| Piece | Where it lives | Reachable from a phone? |
|---|---|---|
| **Plan** | Markdown in `docs/superpowers/specs/` (23 files today), in git | **No.** Nothing in the client reads them — grep confirms no component, endpoint or route touches that path. `useAgentFileContent` (`client/src/api/review.ts:160`) reads files **per agent**, so a plan is visible only while its author agent exists and only by walking that agent's file tree on desktop. |
| **Card** | `Card` row — description (≤20 000), `TerminalReason`, revisions | Yes — CARD-0042's `StatePager` + `CardModal` (fullscreen, 900px collapse) work on a phone. |
| **Task** | `AgentTask` — goal, `Result` (final report), `ResultFilePath`, events incl. `Check`, `SubtreeCostUsd` | Partially — `TaskDrawer` via `/orchestrator?tab=delegations&task=…`, a desktop page with no mobile layout. |
| **Commits** | Git, `CARD-nnnn` in the message by convention | No surface at all (only agent-scoped `GET /api/agents/{id}/files/commits`). |

**There is no FK between `Card` and `AgentTask`** (`AgentTask.cs:6` says so deliberately). The
correlation that actually exists, everywhere, is the **card identifier as citation**: task titles
("CARD-0067 - the reply route…"), commit messages (`fix(channels): CARD-0067 - …`), spec filenames
(`2026-08-16-card-0035-stuck-work-view.md`), card terminal reasons, CLAUDE.md. CARD-0042 §4 made
`#67` a stable citation (allocator fixed, archive-not-delete). **The identifier is the thread key
this spec builds on.**

### Already shipped, to consume rather than redesign

- **`GET /api/attention`** (CARD-0035 slices 1–3): nine named stuck conditions, severity, evidence,
  per-row `actions[]`, `runnerConsulted` honesty flag (`client/src/api/attention.ts`,
  `server/Application/Services/AttentionService.cs`). The Orchestrator `?tab=attention` panel
  exists. Slices 4–6 (action wiring incl. `BlockedReplyRow`, interpretation into `Check` events,
  home badge) are **in flight now**.
- **Check-ins** (CARD-0047): `[check <id> #n]` notes with a 3–5-line interpreted reading, delivered
  to the *caller session's queue*; the deterministic digest is on the `Check` event, and CARD-0035
  slice 5 (in flight) puts the interpretation text into the event detail — after which the reading
  becomes queryable per task, which slice T2 here depends on.
- **Board on mobile** (CARD-0042): `StatePager.tsx`, `?state=` URLs, `#41` display form
  (`client/src/shared/cardIdentifier.ts`). The record surface is done; this spec does not touch it.
- **Rendering machinery**: `RenderedMarkdownReview.tsx` + `markdownSections.ts` (agent files),
  `MarkdownSectionTree.tsx` / `ArtifactViewer.tsx` (workflow artifacts) — markdown-with-sections
  rendering exists twice already; the plan reader reuses, not triplicates (§4, slice M3).
- **Action verbs**: reply/retry/cancel/escalate (`AgentTaskEndpoints.cs:50-75`), send-now/cancel
  message, kill session, and `PATCH /api/cards/{id}` move-with-reason — every one-tap verb this
  spec needs already has a server endpoint. **Zero new write endpoints in this entire plan.**
- **The orchestrator loop's own status format** (the thing being made portable):
  `#56 launch leak - slices 3+4 - opus, check 13:02` — card, short title, slice, tier, next check.
  This line is the unit of the mobile "In motion" band (§3).

## 1. Decisions

### D1. A plan is a git file; the server grows a read-only projection over it. No DB copy.

Rendered-markdown-from-the-repo wins over an artifact record in the DB, for the reason this repo
has already paid for twice (CARD-0067, CLAUDE.md "two-stores rule"): **one durable store per
fact.** Git already holds the plans, versions them, diffs them, and survives restarts; a DB copy
would be a second store that drifts the moment an agent edits the file without re-POSTing. It
would also orphan the 23 existing specs, and require every current and future agent to adopt a new
write path — whereas the projection is retroactive over everything ever written by construction.

**Who writes plans: unchanged.** Agents keep writing markdown to `docs/superpowers/specs/` (and
`docs/features/*/proposal.md`). The projection asks nothing new of authors. One *convention*
hardening (not a requirement — the parser tolerates its absence): keep the existing header block
(`- **Status**:`, `- **Card**:`, `- **Date**:`) that most specs already carry; the parser reads
status/cards/date from it and falls back to filename parsing (`YYYY-MM-DD-card-NNNN-*.md`).

The known cost, stated: a plan written in an **unmerged worktree** is invisible to a projection
scanning the shared checkout until it merges. Accepted — the standing feedback rule is "plans land
on master fast", and the check-in loop already polices delegate branches. The projection also
scans the repo's *worktrees* list (already served by `GET /api/filesystem/worktrees`) as a v2
option, deliberately deferred (§9.3).

### D2. The thread: one projection per card, correlated by identifier

`GET /api/cards/{id}/thread` assembles the four pieces into one response:

- **card** — the existing `CardDto` (description, terminal reason, revisions count);
- **plans** — spec files whose filename or header cites this card's identifier (from D1's catalog);
- **tasks** — `AgentTask` rows whose `Title` or `Goal` contains the canonical identifier
  (`CARD-0067`), each with status, tier, `SubtreeCostUsd`, and its **latest check reading** (the
  `Check` event detail — interpretation first when CARD-0035 slice 5 has stamped it, digest tail
  otherwise) and final `Result` when settled;
- **commits** — `git log --grep=<identifier> --fixed-strings` over the card's project repo
  (resolved worktree-first via `Card.CurrentWorktree`, else the board project's directory), via
  the existing `GitWorkspaceService`;
- **attention** — any `/api/attention` items whose `taskId` belongs to this thread (client-side
  join; the server does not re-derive stuckness — the CARD-0035 non-widening rule).

Correlation by identifier text is honest about what it is: the convention *is* the record today,
and the projection makes the convention pay rent. A stored `CardId` FK on `AgentTask` is priced
in §9.1 — it is **not** required for v1 and adding it does not change the response shape.

### D3. The phone home: three bands, and calm is a designed state

Below `48em`, the home surface becomes three bands in fixed order (the CARD-0031 urgency order,
collapsed to a phone's attention budget):

1. **Needs you** — `/api/attention` items at `Critical`/`Error`, rendered as one-line rows with
   the answer affordance inline (a blocked question expands to the reply box in place — the
   CARD-0033 ask, reusing CARD-0035 slice 4's `BlockedReplyRow`, not a second implementation).
   Absent entirely when empty — no empty-state scaffolding for the band that should usually not
   exist.
2. **In motion** — one compact status line per live task/card, in exactly the orchestrator's
   format: `#56 launch leak · slices 3+4 · opus · check 13:02`. Tap → thread. The check time is
   `NextCheckAt`; a task whose checks are spent shows `checks spent` (which is also an attention
   row, so it appears above too — deliberately: that duplication is the signal).
3. **While you were away** — a client-computed delta since the last visit (`localStorage`
   timestamp): settled tasks (with one line of their report's first sentence), cards that changed
   state, plans that appeared, total spend in the window. This is CARD-0036's *pull* half; the
   *push* half (Telegram digest) is explicitly not this spec (§9.2).

**When nothing needs you** — the common case, and it must read as calm, not empty: band 1 is
absent, band 2 shows the live lines (or "nothing running"), band 3 leads with what finished. The
screen's first words on a healthy day are the three in-motion lines and *"Nothing needs you —
next check 13:02"*: the system says when it will next have something to say, which is what makes
quiet feel supervised rather than dead. (Storybook story: `MobileHome/Calm`, §7.)

### D4. Check-ins and attention without a firehose

Everything is **pull**. Check readings appear in exactly two places — the thread (latest per
task, history in a collapsible list) and the away-delta — never as toasts, never as a feed.
Attention items appear in band 1 and on the thread they belong to. **No push notifications in
v1**: the one candidate worth waking a phone for (Critical attention: a parked channel reply, a
blocked question) is deferred with CARD-0036's push half, because the delivery channel decision
(web push vs Telegram) belongs to that design, and a wrong default here is how notification
fatigue kills trust in band 1.

### D5. One-tap actions (all existing endpoints)

| Action | From | Endpoint |
|---|---|---|
| **Answer** a blocked question | band 1 row, thread | `POST /api/agent-tasks/{id}/reply` |
| **Approve a plan** | thread plan header | `PATCH /api/cards/{id}` — a move with `reason: "plan approved: <file>"`, spawn confirm shown when the target column `IsActive` (the CARD-0042 MoveMenu contract, reused) |
| **Retry / Cancel / Escalate** | thread task row, band 1 | existing task verbs |
| **Send now / Cancel message** | band 1 parked rows | existing queue verbs |
| **Hand back** ("change this") | thread plan/report | opens `DelegateModal` pre-filled with the card identifier + plan path as context — the CARD-0034 react gesture at phone altitude (passage-level selection stays desktop, §D6) |

Approve deliberately produces **a card move with a reason**, not a new "approval" entity: the
board column is where work-state lives, the reason is durable on the move (terminal) or in the
revision trail (CARD-0019), and the existing spawn-confirm already guards the side effect.

### D6. Deliberately desktop-only

Terminal interaction (keypad exists but is an escape hatch, not a designed loop), diff review and
passage-selection delegation (CARD-0034's precision half), the workflow editor, project creation
and settings (CARD-0032 — its safety-boundary explanation needs room), the shape strip (mobile
has the pager), Storybook/dev surfaces. Stated cost: a phone user who needs a diff opens the
laptop; the thread shows *that* commits exist and their messages, which is the decision-grade
fact (traceability), not the review-grade one.

## 2. The seven cards, reconciled

| Card | Disposition under this plan |
|---|---|
| **CARD-0002** (home-rail tasks section) | **Depends on / desktop remains.** Mobile expression is subsumed by bands 1–2 (which consume `/api/attention` exactly as 0035 D2 prescribed for 010). The desktop rail redesign stays 0002's own scope, unchanged. |
| **CARD-0031** (project status view) | **Mobile expression subsumed.** The three bands *are* its five questions at phone altitude (needs-me / working / waiting-review→thread plans / queued→in-motion chips / finished→away-delta). Desktop cockpit remains open, but must consume the same two projections (`/api/attention`, `/api/cards/{id}/thread`) rather than a third derivation — that is this spec's IA claim. |
| **CARD-0032** (new project → first task) | **Untouched, deliberately desktop-only** (D6). Nothing here blocks or is blocked by it. |
| **CARD-0033** (answer blocked question in place) | **Closed by CARD-0035 slice 4 on desktop; slice M4 here carries it to the phone** by reusing `BlockedReplyRow` in band 1. Hard dependency on 0035 slice 4 landing first. |
| **CARD-0034** (review produced work, react in place) | **Partially subsumed**: the thread + plan reader deliver the discovery and reading halves (its own note says the gap is "knowing something is waiting… finding it"); the hand-back verb (D5) delivers a coarse react. Passage-level selection-to-task stays open on 0034, desktop. |
| **CARD-0035** (stuck-work view) | **Dependency, consumed.** Slices 1–3 shipped; 4–6 in flight. This plan adds no stuckness derivation and never touches its files (§8). |
| **CARD-0036** (catch up from phone) | **Pull half subsumed** by band 3 (M6). Push half (Telegram digest, triggers, size ceilings) remains open on 0036, explicitly narrowed to "push + digest-as-prompt" by this spec. |

**Net**: 0033 closes (after 0035-s4 + M4); 0036 narrows; 0031/0034 narrow to desktop scope;
0002/0032 untouched; 0035/0042 are the foundations. Seven cards become: two foundations (shipped),
two closures, two narrowings, two untouched — one information architecture, no eighth card.

## 3. Server design

New files only; nothing concurrent-owned is touched.

- **`server/Application/Services/PlanCatalogService.cs`** — scans `docs/superpowers/specs/` and
  `docs/features/*/proposal.md` under a given repo root (path-validated exactly like
  `FilesystemEndpoints` browse; no traversal outside the root). Returns per file: relative path,
  filename-parsed date, cited card identifiers (filename + header + first-200-lines scan for
  `CARD-\d{4}`), header `Status` line when present, title (first `# ` heading), size. Content
  fetch returns raw markdown. Cached per root for 30 s (`IMemoryCache`) — a phone poll must not
  stat 25 files per tap.
- **`server/Application/Services/CardThreadService.cs`** — D2's assembly. Inputs: `AppDbContext`
  (`AsNoTracking`), `PlanCatalogService`, `GitWorkspaceService` (new method
  `ListCommitsByGrepAsync(root, needle, take)` — additive), `TimeProvider`. Task correlation:
  `EF.Functions.ILike` on `Title`/`Goal` against the canonical identifier. Latest check reading:
  most recent `AgentTaskEvent` of type `Check` per correlated task, detail passed through
  verbatim. Repo-root resolution order: card's `CurrentWorktree.Path` → owning board's project
  directory → omit git/plan sections with `reposConsulted: false` (the `runnerConsulted` honesty
  pattern).
- **`server/Application/Dtos/CardThreadDtos.cs`**, **`server/Api/Endpoints/PlanEndpoints.cs`**
  (`GET /api/plans?path=`, `GET /api/plans/content?path=&file=`),
  `GET /api/cards/{id}/thread` added in **`server/Api/Endpoints/CardEndpoints.cs`** (additive
  route registration only — re-check `git log` on that file first; CARD-0051 recently landed
  there).

## 4. Client design

New feature folder `client/src/features/thread/` + one plans folder:

- `client/src/api/plans.ts`, `client/src/api/cardThread.ts` — hooks (`useQuery`, 15 s
  `refetchInterval` on thread; SignalR invalidation on `CardChanged`/`AgentTaskChanged`).
- `features/plans/PlanReaderPage.tsx` — route `/plans` (`?path=&file=`): renders via the
  extracted markdown component (see below), **ToC-first on mobile** (`MarkdownSectionTree`
  pattern: section list, tap to expand one section — a 20 000-char plan is read section-at-a-time
  on a phone, never as one scroll), sticky header carrying `#67 · <plan title> · Status`.
- **Extraction**: `RenderedMarkdownReview.tsx`'s renderer moves to
  `client/src/shared/RenderedMarkdown.tsx` (marks stay in the agent-files wrapper). This is the
  one refactor of existing code in the plan; it is mechanical and its tests move with it.
- `features/thread/CardThreadPanel.tsx` — one scroll: card header (`#67`, title, state chip,
  priority) → plan rows (title, status, date; tap → reader; **Approve** per D5) → task rows
  (status line format; latest check reading in a quote block; Answer/Retry/Cancel per status;
  final report first-paragraph with "read all" expansion) → commit list (hash, message, age) →
  terminal reason when closed. Renders inside `CardModal` as a new **Thread** tab (default tab on
  mobile) and full-screen at `/thread/:cardId` for links from bands.
- `features/home/MobileHomePage.tsx` — the three bands (D3); `WorkLine.tsx` (the compact status
  line, pure, testable); `awayDelta.ts` (pure delta computation over tasks/cards given a
  timestamp).
- `HomePage.tsx` gains only `isMobile ? <MobileHomePage/> : <existing/>` — **after** CARD-0035
  slice 6 lands its badge there (§8).

## 5. Slices (each independently landable; every one leaves the app shippable)

| # | Slice | Files | Tests | Closes / advances |
|---|---|---|---|---|
| **T1** | Plan catalog service + endpoints | `PlanCatalogService.cs`, `PlanDtos.cs`, `PlanEndpoints.cs`, `Program.cs` reg | `tests/Antiphon.Tests/Application/PlanCatalogServiceTests.cs` (temp-dir fixtures: filename/header parse, card citation extraction, traversal refusal, cache), run via `--property:OutputPath=bin-plans/` | Unblocks everything; plans become *reachable* |
| **T2** | Thread projection | `CardThreadService.cs`, `CardThreadDtos.cs`, `CardEndpoints.cs` (+`GitWorkspaceService.ListCommitsByGrepAsync`) | `CardThreadServiceTests.cs` — correlation by identifier (task title AND goal), no-FK false-positive guard (`CARD-0067` must not match `CARD-00670`: match on word boundary), check-reading pass-through, missing-repo degrade; **assertions scoped to created rows** (shared-Postgres rule) | The four-places problem, server half |
| **M3** | Plan reader page + `RenderedMarkdown` extraction | `plans.ts`, `PlanReaderPage.tsx`, `shared/RenderedMarkdown.tsx`, `App.tsx` route; `RenderedMarkdownReview.tsx` re-imports | `PlanReaderPage.test.tsx` (renderWithProviders + MSW: ToC-first render, section expand, header chips); moved renderer tests stay green | **The central gap: a plan is readable on a phone** |
| **M4** | Thread tab + mobile thread page | `cardThread.ts`, `CardThreadPanel.tsx`, `CardModal.tsx` (tab add), `App.tsx` route | `CardThreadPanel.test.tsx` — ordering, Answer row posts reply (MSW spy), Approve opens move confirm naming spawn when target active, hand-back opens DelegateModal prefilled | CARD-0034 (reading+coarse react); CARD-0033 on phone (with 0035-s4) |
| **M5** | Mobile home bands | `MobileHomePage.tsx`, `WorkLine.tsx`, `awayDelta.ts`, `HomePage.tsx` (branch only) | `WorkLine.test.tsx` (format incl. checks-spent), `awayDelta.test.ts` (pure), `MobileHomePage.test.tsx` (band order, calm state wording, band-1 absent when empty) | CARD-0031 mobile, CARD-0002 mobile |
| **M6** | Away delta band | `awayDelta.ts` wiring, localStorage last-seen | in M5's files | CARD-0036 pull half |

Order: T1→T2 then M3–M6 in any order (M4 wants T2; M5 waits for 0035-s6). Suggested tiers:
T1/T2/M3 sonnet-with-review, M4/M5 opus (interaction density).

## 6. What this costs the surfaces it shares screen with

Desktop: nothing moves — the thread is a new `CardModal` tab (its `?card=` contract untouched, so
its tests hold), plans a new route, home untouched above `48em`. Mobile: the phone home stops
being the desktop rail squeezed narrow and becomes the bands; the board pager and card modal are
unchanged. The one shared-code risk is the `RenderedMarkdown` extraction (M3), contained by its
moved tests.

## 7. Storybook proposals (delivered with this spec)

`client/src/stories/mobile-proposal/` — self-contained presentational mocks (no production
imports beyond Mantine + theme; they are *drawings*, and deleting the folder deletes the proposal
cleanly): `MobileHome.stories.tsx` (NeedsYou / Calm), `CardThread.stories.tsx` (open thread with
plan+check+commits; settled thread), `PlanReader.stories.tsx` (ToC-first; section open; approve
bar). Real data shapes from this repo (#67, #56, #35). Screenshots:
`node scripts/storybook-screenshots.mjs proposals` → `docs/ui-screenshots/proposals-*.png`,
iPhone-12 viewport via story `globals` (the `DashboardPage.stories.tsx` convention).

## 8. Collision map (files this plan must NOT touch until the concurrent work lands)

- CARD-0035 s4–6 own: `AttentionPanel.tsx`, `BlockedReplyRow.tsx`, `SessionQueueDtos.cs`,
  `SessionMessageQueueService.cs` (DTO mapping), `AgentTaskCheckService.cs`, `HomePage.tsx`.
  M4/M5 *consume* `BlockedReplyRow` and the badge afterwards; M5's `HomePage.tsx` branch is the
  single deliberate overlap and is sequenced behind their landing.
- CARD-0058 s6 owns: agent settings modal, `AgentDetailDto`. No overlap.
- `CardEndpoints.cs` (CARD-0051 just landed): T2 adds one route; rebase-check before starting.

## 9. What I could not determine

1. **Whether a `Card.Id` FK on `AgentTask` is wanted** once the thread proves the correlation.
   Priced: one nullable column + backfill by the same identifier match; the DTO shape here does
   not change. Decide after the thread is lived-with — the text match may be enough forever.
2. **The push channel for Critical attention** (web push vs Telegram vs both) — CARD-0036's
   remaining half; needs the operator's preference, not code archaeology.
3. **Worktree-resident plans**: whether the catalog should scan sibling worktrees before merge.
   Deferred; "plans land on master fast" is the standing rule, and the projection degrades
   honestly (a plan not yet on master simply is not listed).
4. **Whether `docs/features/*/proposal.md` should be first-class plans or a separate "designs"
   group** in the catalog. v1 lists both under one list with a `kind` field; the reader is
   identical either way.
5. **Live check-reading latency**: T2 reads the reading from the `Check` event detail, which
   exists only after CARD-0035 slice 5 lands. Until then threads show the digest tail — verified
   acceptable as the degrade mode, but the timestamp of that landing is theirs, not mine.
