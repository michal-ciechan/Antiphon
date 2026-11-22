# Review engine UI: test backlog

Status: backlog for the client slice (2026-11-22). The slice shipped with one Vitest file,
`client/src/features/review/fake/fakeReviewApi.test.ts`, which covers the fake's state transitions
(batch response with Pending → Synced and recomputed counts; AnotherRound moving the run to
round + 1 with the steer stored and the author refused; assign random excluding the author and
passers; "not me" returning the item with its `passes` entry). The pages moved to contract v0.2
on 2026-11-22 (viewer from `GET /api/review-me`, gate viewer fields, iterations, outcomes, owners,
hunks, the cost split, SLA minutes, diagnostic severity) without new tests. The HTTP API became
the default the same day; `client/src/features/review/reviewApiMode.test.ts` covers the mode
selection (HTTP by default, fake only from `VITE_REVIEW_API=fake` or localStorage, a stored `http`
beating a fake build, throwing storage, and `forceReviewApiMode`). A browser E2E against the real
server landed the same day (see [Browser E2E against the real server](#browser-e2e-against-the-real-server)).
Everything else below is still to write. Owner of this file: the review UI track.

## API seam (`client/src/api/reviewEngine.ts`)

- `reviewApiMode` is covered (see the status note). Still open: `currentReviewApiMode()` reads
  storage once and caches it, so a later storage change does not switch a live page.
- `setReviewApiOverride` returns false when storage throws, and clears on `null`; the banner's
  "Use HTTP API" and the dev-only "Fake data" switch set the override and reload.
- `FakeDataSwitch` renders only when `import.meta.env.DEV` and the mode is HTTP; `FakeApiBanner`
  renders only in fake mode.
- `seedDemo` posts `/review-runs/demo` with `{}`.
- `httpReviewApi` builds each contract route and query string exactly (MSW): empty filter values
  are omitted, ids are URL-encoded, `assignGate` without a login sends `{}`.
- Problem Details from the HTTP API surface through `getApiErrorMessage` on every page (404 run,
  409 decided gate, 422 dismissal without a scope).
- `reviewRunChangedKeys` equals the contract's SignalR list, with and without a `runId`; `me` is
  not in it.
- `getWorkflowRevision` and `getMe` hit `/review-workflows/revisions/{id}` and `/review-me`.
- `useReviewWorkflow(null)` uses the default-workflow key; with a revision id it uses the revision
  key and never refetches (staleTime Infinity).
- `useSignalRInvalidation`: `ReviewRunChanged { runId }` invalidates exactly those keys; a payload
  without `runId` still refreshes the lists, queue and KPIs.

## Fake API (`features/review/fake`)

- Validation: Dismiss without reason or scope → 422 with the finding key in `errors`;
  NotABlocker on a non-blocker → 422; text required for Dispute / Question / NotABlocker / Reply;
  an Outdated finding → 409; a batch with one bad response applies none.
- A Question gets a bot Reply after the sync delay; unposted findings stay `NotApplicable`.
- Refresh retries Failed syncs (F4 on payments-api#612) → Pending → Synced.
- Approve → AwaitingMerge with base-moved and a waiting await-merge; Close → Closed / Done;
  a DisputedFindings decision changes only the disputed findings, not the run.
- A pre-assigned gate on a run that has not reached it → 409.
- `createRun` → Queued run that walks intake → packs → reviewer fan-out on the progress timer.
- KPIs: `inReview`, `waitingOnHumans`, `averageRounds`, `costPerAcceptedFindingUsd`, model usage
  all recompute after a response, a decision and a reset.
- `reset` cancels timers (no late emits after a story switch).
- `validateWorkflowGraph`: the default graph is valid; removing `maxIterations` from
  `await-fixes → intake` does not invalidate it (await-fixes is a Wait); an unbounded cycle of code
  nodes is `cycle-unbounded`; unknown edge ends; a node in two sections; missing description is a
  `Warning` (`severity`) and keeps `valid: true`; an empty section and an unlabelled edge out of a
  branching node are warnings; every other code is an `Error`.
- Viewer fields: `getMe` answers the actor; each gate's `viewerCanDecide` and
  `viewerCannotDecideReason` follow the actor (author, pre-assigned before the gate, assigned to
  someone else, already decided) and a decision is refused with the same reason.
- `decideGate` with a decision outside `allowedDecisions` → 422 (the #617 hotfix gate refuses
  AnotherRound); `decidedBy` and `assigneeDisplayName` are set; the gate node gets an `outcome`.
- `passGate` appends `{ login, reason, at }`; a second pass by the same person adds a second entry;
  passers are excluded from Mine and from the random pick.
- Cost split: `costActualUsd` sums Http executions, `costEstimatedUsd` the rest, and they add up to
  `costUsd` after a new reviewer finishes.
- Iterations: `createRun` records a `manual` iteration with base and head; another round records
  `another-round` at the new round; Refresh of a Failed run (#1851) records a `recheck` iteration
  in the same round on the same head; the seeded #618 iterations are opened / push /
  another-round with heads 8d3e012, 4a77b10, e41b07c; a Queued run has no iteration.
- Seeded node `outcome`s match an outgoing edge label of their node in the default workflow (a
  guard that the overlay can highlight every seeded path).

## Runs page

- KPI cards toggle their filters (In review → Queued + Running; Waiting on humans; Avg rounds →
  round ≥ 3; model rows) and the "Showing N of M · filtered by" chips remove them one by one;
  Clear all keeps search and repo.
- Search is debounced and sent as `q`; the repo select is sent as `repo`.
- Status chip counts follow the searched set.
- Table row and title link both open `/reviews/:id`; the board groups statuses into its six columns
  with count and Σ cost.
- Filters and view survive navigating into a run and back (Zustand UI store).
- Cost formatting: `fmtCost` (0¢, <1¢, 53¢, $1, $16) and the ≈ marker when a split has an estimate.

## Run page

- `?tab=` deep links each tab; an unknown tab falls back to Findings; the gate strip appears on the
  other tabs only when a gate is open.
- 404 run id shows "No such review run" with the back link.
- Header: blast mini chart is "pending" when `blastRadius` is null; cost tooltip splits
  `costActualUsd` from `costEstimatedUsd`.
- Header owners: one chip per owner, team icon for `Team`, user icon and "@" for `User`; "none
  resolved" for an empty list.
- Header author: when `openedByLogin` differs from `authorLogin` the line reads "opened by
  @release-train, author resolved via …" (#617); otherwise only the "resolved author" badge when
  `authorResolvedBy` is set.
- A Failed run shows the latest Failed execution's node, iteration and `summary` (#1851); a run
  with no Failed execution shows the fallback text.
- The viewer: the page waits for `review-me`; when it fails the Findings tab shows the error and
  the other tabs still render.
- Fake banner: "Viewing as" sets the fake viewer and refetches every `review*` query (the gate panel
  flips from the author's reason to enabled when switching Alice → Hugo on #612).

### Findings tab

- Finding detail: `hunk` renders numbered lines from the `@@` header (new-file numbers; removed
  lines use old-file numbers), `+` green and `-` red, the anchor range marked; a null hunk falls
  back to the anchor block (#612 F9); CRLF hunks and a trailing newline parse the same.
- The rule preview's team layer is the first `Team` owner, never a `User` owner.

- Grouping: Open, Tracking (WillFix, Deferred, or any posted finding whose sync is Pending or
  Failed), Not posted inline, Closed; worst severity first.
- Composer: a blocker offers "Not a blocker" and not "Dismiss"; a non-blocker the reverse;
  "Not a blocker" needs a reason (false positive / by design / accepted risk) and text.
- Dismiss at path / repo / team opens the wide-scope modal when a reason is set; the draft cannot
  post until confirmed; changing reason or scope un-confirms it; the rule preview shows the path
  glob only for path scope and the two-repo note only for team scope; the steer is appended.
- Composer is locked while the finding's sync is Pending and for Outdated findings.
- Submit bar: badge per draft, "incomplete" list, button disabled until every draft is valid, label
  "Post N replies to GitHub as <name> via Antiphon"; success clears drafts and notifies; failure
  keeps drafts and notifies with the server message.
- Reply preview text for every action (signature line included).
- Drafts survive switching to Workflow and back (Findings panel stays mounted).

### Gate panel

- Disabled with the server's `viewerCannotDecideReason` whenever `viewerCanDecide` is false (and a
  fallback text when the reason is null); enabled otherwise, with no client-side rule.
- Only `allowedDecisions` are offered, in Approve / Another round / Close order, with a "not offered"
  note (#617: approve or close).
- Assignee chip uses `assigneeDisplayName`; passes list login, reason and relative time (absolute
  in the tooltip).
- With no open gate the rail shows the last decided gate: decision, `decidedBy`, time and steer.
- Another round shows the steer box; the button reads "Start round N+1 with steer" only when a
  steer is typed; the request omits an empty steer.
- Approve copy mentions open blockers recorded as accepted risk; DisputedFindings uses its own
  labels.
- After a decision the panel resets (keyed by gate id) and the run header reflects the new round.

### Workflow tab

- `layoutWorkflow`: every node is in exactly one band; bands alternate direction; a collapsed
  section produces one box and no member nodes; edges into a collapsed section merge (labels
  joined, back flag kept); back edges into an earlier band get distinct lanes; a cycle without
  `isBackEdge` flags still lays out (DFS fallback).
- Default collapse: each section starts at the workflow's `collapsed` (Branch maintenance folded);
  the viewer's toggles win and persist per workflow name; Expand all / Collapse all.
- The graph is the run's `workflowRevisionId` revision; a 404 revision shows the error naming it.
- Overlay: `rollupStatus` precedence (Running > Failed > Waiting > Pending > Succeeded > Skipped);
  fan-out items are grouped by `itemKey` (one pill per reviewer group, "×n · k/n done" counts
  items); the iteration selector changes statuses and the R/trigger/base..head line, not the
  layout; `backEdgeTaken` counts on payments-api#618 (await-fixes 1, human-approve 1).
- `edgeTakenIn`: a labelled edge is taken only when a source execution's `outcome` is one of its
  labels ("push | recheck" splits); an execution with no outcome falls back to timing; on #618 it1
  both `await-fixes → record-drift` and `await-fixes → intake` are taken, `route → human-review`
  is not.
- Side panel: Item and Took columns appear only when an execution has `itemKey` / `outcome`.
- Validation panel groups diagnostics by `severity` (errors first) with counts, and lists the
  affected nodes and edge.
- Clicking a step opens the side panel with parameters and every execution; pane click clears.
- Validate posts the graph and shows diagnostics.
- React Flow is rendered only while the tab is visible (hidden containers measure 0).

### Timeline tab

- Tracks come from `iterations[]`: round, trigger badge, start time and `base..head` short SHAs
  ("—" before intake pins them); an iteration with executions but no row still renders.
- Bars never start before zero when `startedAt` is later than the first execution.
- Steps that start after a park began are listed under the park, not as bars (record-drift).
- One minute scale across iterations; tick spacing ≥ 44 px; running bars extend to now.
- Gate decisions land under the park of the iteration they ended, named by `decidedBy`; the steer
  is quoted.

## Real-data states (HTTP mode, MSW)

- Empty list: "No reviews yet" with New review and Load demo data; the KPI strip, chips and table
  are not shown. Load demo data → success notification with the count, the list refetches.
- `POST /api/review-runs/demo` → 404: the button hides for the session (`demoSeedUnavailable`),
  an info notification carries the server's detail, and no error notification is shown.
- Loading: skeletons on the runs page (header line, KPI block, rows), the run page, the findings
  tab, the workflow tab and the queue; no spinner-only states remain.
- Query errors show the Problem Details `detail`, the `code` and the status, with Retry refetching
  the failed query: runs list (refetches list, search and KPIs), KPIs, run (not for a 404),
  findings, `review-me`, queue, workflow. A failed background refetch of a loaded run keeps the
  last copy under an alert.
- Mutation errors are notifications with `detail`, `code` and status from the hook's `onError`,
  once per failure (callers no longer add their own): 403 `review_gate_not_decider`, 409
  `review_blocker_open` / `review_gate_decided` / `review_no_eligible_reviewer` /
  `review_run_finished`, 422 field errors, and each 503 `review_code_host_*` with its hint and a
  10s auto-close.
- Intake modal: GitHub needs repo + positive integer PR number; Local needs repo + base ref, head
  defaults to `HEAD` and is omitted when cleared; an empty title is omitted; the request body is
  exactly `{ host, repo, prNumber }` or `{ host, repo, baseSha, headSha? }` (+ `title`). Success
  seeds the run cache and navigates to `/reviews/{id}`; 422 `errors.repo` / `prNumber` /
  `baseSha` land on their inputs; the form resets on reopen. Both entry points (header and empty
  state) open it.
- Refresh on the run page: the button shows a pending state, a 202 with an empty body succeeds
  with a "Refresh requested" notification, a 503 notifies with the code-host hint.
- Polling: the run detail refetches every 10s while `Queued` or `Running` and every 30s otherwise
  in HTTP mode, never in fake mode; review queries refetch on window focus.
- Unknown enum values from a newer server (run status, stage, waitingOn, severity, finding status,
  action, node status and kind, gate kind, queue status, SLA, sync state, diagnostic severity,
  decision) render as a grey label with the raw value; the board adds an "Other" column for a
  status no column lists; `Object.keys` over a `tolerant` record still lists only the known keys.
- Every contract status renders on the table, board and run header: `AwaitingFixes`, `AtGate`,
  `AwaitingMerge`, `Merged`, and `Failed` with the failed node's `summary` as the reason.

## Queue page

- Tabs Mine / My teams / All with counts from three queries; kind chips filter client-side.
- Mine = assigned to me + unassigned in my teams that I have not passed (fake semantics; assert the
  HTTP API agrees once it exists).
- Take disabled for the author; "Not me" enabled only when assigned to me or unassigned in my
  team; the reason (plus optional note) is sent; success notification names the team.
- Assign random notification names the pick and the exclusions; 409 "nobody eligible" shows.
- SLA cell colour, bar and "near / breached" text from `ageWorkingMinutes / slaWorkingMinutes`
  (warn at 75%, breached at 100%); falls back to `slaState` when `slaWorkingMinutes` is 0.
- Passes show under the item (login, reason, time); owners as chips (max 3, "+n" tooltip); the
  author cell shows "opened by @login" when the PR was opened by another account.
- The page waits for `review-me`; an error shows instead of an unscoped queue.
- Cost formatting: `fmtCostTotal` adds "≈" only when the estimate is non-zero; the board column and
  the runs total carry the summed split.

## Stories and wiring

- Every `Review/*` story renders without console errors (Storybook smoke via
  `client/scripts/storybook-screenshots.mjs review-`).
- `App.test.tsx`: add `/reviews`, `/reviews/:runId` and `/review-queue` to the routes table.
- Layout nav shows "Reviews" and "Review queue"; `/reviews` stays active on a run page.
- Nothing outside `src/stories/` imports the proposal mocks (a lint-style guard test).

## HTTP switch (when the server lands)

- `ReviewStoryShell` forces the fake; a story rendered without it would hit `/api` (assert the
  shell is the only way review stories render pages).
- The `Review/Review runs/Empty` story: Load demo data repopulates the fake.

- Contract fixtures captured from the server (as `src/test/fixtures/contract/` does for other
  features) replace the fake in page tests; the fake keeps the same shapes (a fixture-vs-fake shape
  test).
- E2E: respond to a finding, see Pending → Synced via SignalR; decide a gate; pass and assign.
  Responding and deciding are covered on a Local host (next section); Pending → Synced needs a
  GitHub-shaped host with threads, and pass and assign are still open.

## Browser E2E against the real server

`tests/Antiphon.E2E/ReviewEngineE2ETests.cs` boots the real server (Kestrel, Postgres testcontainer,
isolated runner, built `client/dist`) with the real workflow executor and the real
`LocalGitCodeHost` over a throwaway git repo (`main` base, `feature` head). Only the model seams
are scripted (`tests/Antiphon.E2E/Fixtures/ReviewEngineScenario.cs`: decisions, reviewer agents,
verifier). The viewer is `Review:DefaultViewerLogin` (`dana`, the author of a Local run); a second
browser context sends `X-Review-Viewer: lee` (`Review:AllowViewerHeader`). Run it after
`npm run build` in `client/`, under a build slot:
`dotnet run --project tests/Antiphon.E2E --no-build -- --treenode-filter "/*/*/ReviewEngineE2ETests/*"`.

Covered, in one test (`A_local_change_is_reviewed_fixed_and_approved_by_a_non_author`):

- Empty runs page → New review → Local git with repo, base and head refs; the title defaults to the
  head commit's subject; the page navigates to `/reviews/{id}`.
- Round 1 reaches Awaiting fixes (waiting on author, round 1, `feature → main @ head`) with the
  scripted major F1 at `src/Lease.cs:12`, its hunk, and "not posted inline" (a local host takes no
  reviews).
- The composer: Will fix with text, the submit bar's label, "Posted 1 reply", the thread shows the
  reply, the row reads "will fix"; the API shows the event settle `Pending` → `NotApplicable`.
- A pushed fix commit and Refresh: round 2 is clean, F1 becomes fixed (`still_present`), the run is
  At gate (waiting on humans) on an Approval gate with the blast-radius reason; the author's gate
  panel is disabled with "You are the author", and the API refuses the author with 403.
- Timeline: it1 · R1 manual intake (`base..head`) and it2 · R2 push (`..fixed head`).
- The other viewer: the All queue lists the gate, the row opens the run, Approve `<sha>` records the
  decision, the run walks to Awaiting merge (base unchanged, await-merge waiting) and the decided
  gate names `lee`; the author's open page follows to Awaiting merge.

Not covered yet: GitHub-shaped hosts (posted findings, thread sync, statuses), Another round with a
steer, Close, dismiss and dispute flows, pass and assign from the queue, demo data, and failure
states (a failed run and Refresh's recheck).

| UI-FS-1 | Workflow fullscreen: `?graph=fullscreen` opens it and Back/Exit leaves it; a step click opens the floating window and a drag moves it; the validation window; browser fullscreen is refused quietly in an iframe. | Checked once by hand with a headless Edge script against Storybook (open, click collate, drag -560/+208, exit); no automated test. |
