# Review engine UI: test backlog

Status: backlog for the client slice (2026-11-22). The slice shipped with one Vitest file,
`client/src/features/review/fake/fakeReviewApi.test.ts`, which covers the fake's state transitions
(batch response with Pending → Synced and recomputed counts; AnotherRound moving the run to
round + 1 with the steer stored and the author refused; assign random excluding the author and
passers; "not me" returning the item). Everything below is still to write. Owner of this file:
the review UI track.

## API seam (`client/src/api/reviewEngine.ts`)

- `reviewApiMode`: the localStorage override `antiphon.reviewApi` wins over `VITE_REVIEW_API`;
  `http` and `fake` are the only accepted values; a throwing `localStorage` falls back to the env
  value; any env value other than `http` selects the fake.
- `setReviewApiOverride` returns false when storage throws, and clears on `null`.
- `httpReviewApi` builds each contract route and query string exactly (MSW): empty filter values
  are omitted, ids are URL-encoded, `assignGate` without a login sends `{}`.
- Problem Details from the HTTP API surface through `getApiErrorMessage` on every page (404 run,
  409 decided gate, 422 dismissal without a scope).
- `reviewRunChangedKeys` equals the contract's SignalR list, with and without a `runId`.
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
  `warn.` code and keeps `valid: true`.

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
- Header: blast mini chart is "pending" when `blastRadius` is null; cost tooltip splits actual
  (Http) from estimate (Seat).

### Findings tab

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

- Disabled with the right reason for: the author; a pre-assigned gate on a run not at the gate;
  a gate assigned to someone else. Enabled for the assignee and for an unassigned gate.
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
- Default collapse: a section with no executions starts collapsed; the viewer's toggles win and
  persist per workflow name; Expand all / Collapse all.
- Overlay: `rollupStatus` precedence (Running > Failed > Waiting > Pending > Succeeded > Skipped);
  fan-out shows "×n · k/n done"; the iteration selector changes statuses, not the layout;
  `backEdgeTaken` counts on payments-api#618 (await-fixes 1, human-approve 1).
- Clicking a step opens the side panel with parameters and every execution; pane click clears.
- Validate posts the graph and shows diagnostics.
- React Flow is rendered only while the tab is visible (hidden containers measure 0).

### Timeline tab

- Steps that start after a park began are listed under the park, not as bars (record-drift).
- One minute scale across iterations; tick spacing ≥ 44 px; running bars extend to now.
- Gate decisions land under the park of the iteration they ended; the steer is quoted.

## Queue page

- Tabs Mine / My teams / All with counts from three queries; kind chips filter client-side.
- Mine = assigned to me + unassigned in my teams that I have not passed (fake semantics; assert the
  HTTP API agrees once it exists).
- Take disabled for the author; "Not me" enabled only when assigned to me or unassigned in my
  team; the reason (plus optional note) is sent; success notification names the team.
- Assign random notification names the pick and the exclusions; 409 "nobody eligible" shows.
- SLA cell colour and "near / breached" text per `slaState`.

## Stories and wiring

- Every `Review/*` story renders without console errors (Storybook smoke via
  `client/scripts/storybook-screenshots.mjs review-`).
- `App.test.tsx`: add `/reviews`, `/reviews/:runId` and `/review-queue` to the routes table.
- Layout nav shows "Reviews" and "Review queue"; `/reviews` stays active on a run page.
- Nothing outside `src/stories/` imports the proposal mocks (a lint-style guard test).

## HTTP switch (when the server lands)

- Contract fixtures captured from the server (as `src/test/fixtures/contract/` does for other
  features) replace the fake in page tests; the fake keeps the same shapes (a fixture-vs-fake shape
  test).
- E2E: respond to a finding, see Pending → Synced via SignalR; decide a gate; pass and assign.
