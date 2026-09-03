# CARD-0347 — closing a GitHub-linked card pushes the GitHub close at close time (comment, state, status label); reopen is symmetric; archive is deferred — plan

**Date:** 2026-09-03

**Plan task:** c5959c78 (Frontier, plan only; no production code changed)

**Card:** CARD-0347 (`633f3231-51ce-46de-a569-b71d1a59c26d`) — "Closing a GitHub-linked card should close the GitHub issue too, with a comment"

**Verified against:** `feat/card-task-c5959c78` @ `dcefb1b8`. Every file:line below was re-read out of the code on that commit; the live evidence was read off the board and off GitHub today.

**Evidence base:** `TrackerBidirectionalSyncService` (`SyncBoardAsync`, `PushOutboundAsync`, `SyncStateAsync`, `SyncLabelsAsync`, `RunningBoards`), `ExternalTrackerSyncService` (cursor rule, `MarkInactive`), `GitHubIssuesTracker.SetStateAsync` / `PostCommentAsync` / `SendAsync`, `IBidirectionalIssueTracker`, `TrackerSyncMarkers`, `TrackerSyncDtos`, `TrackerSyncSummaryFormatter`, `TrackerSyncNotifier`, `TrackerSyncEndpoints`, `CardService` (`MoveAsync`, `ReopenAsync`, `ArchiveAsync`, `ApplyAutomatedMoveAsync`, `ApplyColumnMove`, `LoadCardForUpdateAsync`), `CardEndpoints`, `ScheduleService` target validation, `CardWorkTransitionService` targets, `scripts/card.ps1` (`close`, `move`, `reopen`), `client/src/api/boards.ts`, `tests/Antiphon.Tests/Application/TrackerBidirectionalSyncTests.cs` (+ its `FakeBidirectionalTracker`), `TrackerSyncEndpointTests.cs` (factory that swaps `IIssueTracker`), `TestHelpers/BridgeQueueHarness.cs`, `docs/workflow-tracker-block.md`, `docs/antiphon-api.md`; the live board (CARD-0344) and GitHub issue `michal-ciechan/Antiphon#34` via `gh`.

---

## Decision

**The card-close → GitHub-close write already exists and already fired for CARD-0344.** `TrackerBidirectionalSyncService.SyncStateAsync` (`TrackerBidirectionalSyncService.cs:528-603`) posts a system comment ("Card CARD-0344 reached terminal status **Done**: <closing reason>", hidden marker appended) and then `PATCH state=closed, state_reason=completed|not_planned` for every linked card that is terminal while the ref's cursor still says `open`; the reopen arm (a `Reopen` revision newer than `LastOutboundSyncedAt`) is already symmetric. Issue #34 is **CLOSED, `COMPLETED`, `closedAt 2026-09-03T14:00:09Z`**, with exactly that comment at `14:00:08Z` — 15:00 Europe/London, the Windmill `0 0 */3 * * *` slot. The doc's "archive→GH-close is out of scope" (`docs/workflow-tracker-block.md:93,102`) is about **archive**, not close, and CARD-0344's closing note ("#34 is NOT auto-closed by this") was wrong by up to three hours.

**What is actually missing is immediacy and feedback.** The write waits for the next explicit bidirectional trigger (Windmill every 3 h, `github-sync.ps1`, the board button, the two sync endpoints), and the person closing the card is told nothing — which is exactly how the wrong closing note got written. The "writes never run from the tick" rule (`workflow-tracker-block.md:102`) constrains the orchestrator's read tick; it says nothing against a write fired by the operator's own card write, which is the CARD-0166 "write-triggered" shape the card itself points at.

**So this card is: push that same write synchronously when the card crosses the terminal boundary, report the outcome on the response, and keep the scheduled run as the retry.** Four slices:

| Slice | What | Why here |
|---|---|---|
| S1 | Extract the per-card outbound state write (comment → state → `status:*` label) out of the per-board sync loop into a `TrackerCardStatePushService` with a per-card entry point, a bounded timeout and a kill switch. The sync loop calls the same code, so both paths produce byte-identical GitHub comments. | Same class of write, one implementation. The card asked for this explicitly ("not new plumbing"). |
| S2 | Call it from `CardService.MoveAsync` (terminal crossing) and `ReopenAsync`, after the local commit, never throwing; carry the outcome on `MoveCardResult` / a new `ReopenCardResult`; `card.ps1` prints it. | These two verbs are the only card-side terminal transitions (proved below). The response is the fix for the CARD-0344 confusion. |
| S3 | Digest continuity: a push made at close time is remembered on the ref so the next bidirectional run still itemises it as `ClosedOnGitHub` / `ReopenedOnGitHub` for the CARD-0171 chat digest. | Without this the family channel silently loses its "1 issue closed on GitHub: CARD-0344 (#34)" line — the scheduled run would find the cursor already `closed` and report nothing. Separable; ship S1+S2 first if needed. |
| S4 | Docs, the archive follow-up card, the CARD-0344 record correction. | The doc currently reads as if close never pushes; it must say when it does and where the retry lives. |

Archive stays out of scope (decision 3 below). Labels other than the managed `status:*` label, comments, content pushes and creates stay on the scheduled run unchanged.

## Ground truth, as verified

**The existing write, line by line.** `PushOutboundAsync` (`:317-361`) iterates every non-archived ref and calls, in order, `PushDiscussionCommentsAsync`, `SyncStateAsync`, `PushContentEditCommentsAsync`, `SyncLabelsAsync` (only when the issue was in the fresh `FetchCandidatesAsync` list) and the export title/body push. `SyncStateAsync`:

- close arm (`:546-568`): `terminal && cursor != "closed"` → `state_reason = Canceled ? "not_planned" : "completed"`; body = `AppendSystemCommentMarker($"Card {Identifier} reached terminal status **{Status}**" + (": " + TerminalReason)?, card.Id)`; `PostCommentAsync` then `SetStateAsync(closed)`; on success `LastKnownExternalState = "closed"`, `LastOutboundSyncedAt = utcNow`, `changes += ClosedOnGitHub`; any exception → Warning log, cursor untouched (so the next run retries).
- reopen arm (`:570-600`): `!terminal && cursor == "closed"` and the newest non-tracker `Reopen` revision has `CreatedAt > LastOutboundSyncedAt` → comment "Card X was reopened on Antiphon: reason" + `SetStateAsync(open, "reopened")`, cursor `open`, `ReopenedOnGitHub`.
- `SyncLabelsAsync` (`:460-526`) needs the issue's *current* labels (`TrackedIssue current`): export-origin does a full `ReplaceLabelsAsync`; import-origin removes stale managed `status:*`/`priority:*` and adds the desired `status:<kebab>` via the sub-resource.

**Terminal transitions on the card side come from exactly two verbs.** `CardService.MoveAsync` (`CardService.cs:387-446`, `PATCH /api/cards/{id}`; `card.ps1 close` is this — it PATCHes to the board's first terminal column, `card.ps1:446-453`; the UI drag is this; `wasTerminal` is already captured at `:409` for the review checkpoint) and `CardService.ReopenAsync` (`:591-628`, `POST /reopen`). `ApplyAutomatedMoveAsync` (`:646-692`) cannot reach a terminal column: `ScheduleService` refuses `Done`/`Canceled` targets outright (`ScheduleService.cs:827-835`, "A card action cannot close a card") and `CardWorkTransitionService` only ever decides `InProgress` or `Review` (`CardWorkTransitionService.cs:188,202`). `ArchiveAsync` (`:517-561`) changes neither column nor status, and the sync skips archived cards on both the refs dictionary (`:137`) and the push loop (`:335`). The tracker's own closes (`ExternalTrackerSyncService.MarkInactive`, `:623-652`) set the cursor to `closed` themselves, so they never look like something to push.

**The GitHub client.** `SetStateAsync` PATCHes `repos/{repo}/issues/{n}` with `{state, state_reason}`; `PostCommentAsync` POSTs `/comments`; both go through `SendAsync` which `EnsureSuccessStatusCode()`s (4xx/5xx → `HttpRequestException`). The typed client is registered with no timeout configuration (`Program.cs:535`) → `HttpClient` default 100 s. A GitHub outage would therefore hold a card close for up to 100 s per call without a bound of our own — hence the linked-CTS timeout in S1.

**Comment sizing.** `CardService.MaxReasonLength = 4_000` (`CardService.cs:45`); GitHub's comment body limit is 65,536 characters; markdown in the reason passes straight through (the #34 comment already renders the reason's quotes and paragraphs). The trailing `<!-- antiphon:system-comment=<cardId> -->` marker is what `PullCommentsAsync` (`:286-290`) uses to drop the echo without creating a `CardComment` row — every comment this plan posts keeps it.

**How tests build `CardService`.** Nowhere directly: zero `new CardService(` in `tests/`; `BridgeQueueHarness` registers it via DI (`BridgeQueueHarness.cs:148`) alongside `ExternalTrackerSyncService` but registers **no** `IIssueTracker`, so `IEnumerable<IIssueTracker>` resolves empty unless a test adds a fake. `TrackerSyncEndpointTests`' factory shows the swap pattern (`TrackerSyncEndpointTests.cs:378-383`: remove every `IIssueTracker` descriptor, add the fake). `FakeBidirectionalTracker` is private to `TrackerBidirectionalSyncTests` (`:935`) and records `PostCommentCalls`, `SetStateCalls`, label calls and has `ThrowOnPostComment`.

**The CARD-0171 digest interaction.** `TrackerSyncNotifier` announces `TrackerSyncBoardResult.Changes` of the run that made the writes; the gate is `Changes.Count > 0`. If the close-time push has already set the cursor to `closed`, the next Windmill run's `SyncStateAsync` is a no-op and the run reports no change — the channel never hears about the close. Today it does (the 14:00Z run would have produced "1 issue closed on GitHub: CARD-0344 (#34)" if `notify_channel` is set). S3 exists because of this.

**Concurrency.** `TrackerBidirectionalSyncService.RunningBoards` (`:18`) is a static per-board guard for bidirectional runs. A per-card push racing a run in flight could double-post the closing comment (the state PATCH itself is idempotent). The window is the few seconds a 3-hourly run spends on one board; S1 checks the guard and yields.

**Wire contracts.** `MoveCardResult(Card, SpawnedSessionId, SpawnSuppressed)` (`BoardDtos.cs:194`) is read by `client/src/api/boards.ts:262-269`; adding an optional field is additive. `ReopenAsync` returns a bare `CardDto`; the React caller ignores the body (`boards.ts:663-664`, `onSuccess: (_card, …) => invalidate…`) and `card.ps1 reopen` prints it via `Write-CardLine $updated` (`card.ps1:524`). `CardDto.HasMore` (`BoardDtos.cs:90`) is the precedent for omitting a default so an existing route's bytes do not change.

## The five questions, answered

1. **Trigger: synchronous, inside the close/reopen request, after the local commit, bounded, fail-soft.** Not the read tick (the rule stands: `OrchestratorService` still must not reference the bidirectional service — `OrchestratorTrackerCadenceTests.cs:123-127` keeps pinning it, and `CardService` depends on `OrchestratorService`, not the reverse, so the new dependency never appears on that constructor). Not fire-and-forget: the scoped `DbContext` dies with the request and the whole point is telling the closer what happened. Not "wait for the next tick": that is today's behaviour and the complaint. The scheduled run stays as it is and becomes the retry.
2. **Comment content: a one-line headline, then the closing reason verbatim as its own paragraph, then the hidden marker.** The reason on a terminal move is already "the verdict — what shipped, what was corrected, what is still open" (this repo's lifecycle convention), so it *is* the explanation; 4,000 chars vs GitHub's 65,536 always fits, and GitHub renders whatever markdown the reason carries. The current one-liner ("…reached terminal status **Done**: <multi-paragraph reason>") inlines the first paragraph after a colon; the new shape reads cleanly for multi-paragraph reasons:

   ```
   Card CARD-0344 closed as **Done** on Antiphon.

   <TerminalReason verbatim>

   <!-- antiphon:system-comment=<cardId> -->
   ```

   Reopen: `Card CARD-0344 reopened on Antiphon.` + the reopen reason. Canceled: `closed as **Canceled**` with `state_reason = not_planned` (unchanged). One builder, used by both the per-card push and the scheduled run, so the two paths are byte-identical. No card link (the board has no public URL; `AppendCardMarkerFooter` already passes `boardLink: null`). Voice check against CARD-0346: the chat digest names the same pair ("CARD-0344 (#34)"); nothing in CARD-0346 touches the GitHub-side comment, so no overlap.
3. **Scope: close and reopen in this card; archive is a follow-up.** Close = `MoveAsync` crossing into a terminal column (`targetColumn.IsTerminal && !wasTerminal`). Reopen = `ReopenAsync` (the sync already has the symmetric arm; wiring it costs one call and makes the symmetry immediate rather than three-hourly). Archive stays out because it is not "work finished" — it is "record taken off the board" (`ArchiveAsync` leaves column and status alone), the sync deliberately excludes archived cards, and the honest GitHub semantics (close as `not_planned`? comment and leave open? only when the card was still open?) is a product decision the card did not make. An archived card that had already been closed has already had its issue closed by this plan's close hook. S4 files the follow-up with that question spelled out. Also **in** scope, because it is the same per-card write and a closed issue wearing `status:in-progress` for three hours is the next card the user would file: after a successful state push, refresh the managed `status:*` label for that one issue (one `FetchByIdsAsync` + the existing `SyncLabelsAsync` logic), best-effort.
4. **Failure handling: never blocks, never fails the card write, always says why.** The push runs only after `SaveCardWriteAsync` has committed the move. Every exception is caught into the result (`failed` + reason) with a Warning log; the cursor is left untouched, so the next scheduled run's unchanged `SyncStateAsync` retries exactly as today. A linked `CancellationTokenSource` bounds the whole push (`Tracker:CardStatePushTimeoutSeconds`, default 15) so a GitHub outage delays a close by at most that; `Tracker:PushStateOnCardTransition=false` turns the hook off without a deploy. `card.ps1` prints the outcome, including "the next scheduled sync will retry".
5. **Only linked cards, and only bidirectional trackers.** `card.ExternalIssueRef is null` → no service call at all (no config parse, no token, no result on the wire). Linked to a board whose `TrackerKind` is back to `Internal` → `skipped: tracker_inactive`. Linked to Jira/Linear (read-only adapters) → `skipped: tracker_read_only`. `token_key` unresolved → `skipped: token_unresolved`. A run in flight for the board → `skipped: sync_running` (the run will do the close itself).

## Design

### S1 — `TrackerCardStatePushService` (the per-card write, shared with the run)

New `server/Application/Services/TrackerCardStatePushService.cs`, scoped. Constructor: `AppDbContext`, `TrackerTokenResolver`, `IEnumerable<IIssueTracker>`, `IOptions<TrackerSettings>?`, `ILogger<…>`, `TimeProvider`.

**The shared core (moved, not rewritten).** Move the bodies of `SyncStateAsync` and `SyncLabelsAsync` out of `TrackerBidirectionalSyncService` into the new class as:

```csharp
public Task<int> PushStateAsync(IBidirectionalIssueTracker tracker, IssueTrackerConfig config,
    ExternalIssueRef issueRef, DateTime utcNow, List<TrackerSyncChange> changes, CancellationToken ct);
public Task<int> SyncLabelsAsync(IBidirectionalIssueTracker tracker, IssueTrackerConfig config,
    ExternalIssueRef issueRef, TrackedIssue current, DateTime utcNow, List<TrackerSyncChange> changes, CancellationToken ct);
public static string BuildCloseComment(Card card);   // decision 2, marker appended
public static string BuildReopenComment(Card card, CardRevision reopen);
```

`TrackerBidirectionalSyncService` takes the new service in its constructor and its `PushOutboundAsync` calls these two exactly where it called the private methods (`:343`, `:348`). The `TrackerActor` constant and the reopen-revision rule (`EditedBy != "external-tracker"`, `CreatedAt > LastOutboundSyncedAt`) move with the code. Behaviour-preserving: every existing `TrackerBidirectionalSyncTests` case must stay green with only the comment wording changed (they assert the marker, not the prose — `:279-280`, `:306-307`).

**The per-card entry point.**

```csharp
public async Task<TrackerCardStatePushResult?> PushForCardAsync(Guid cardId, CancellationToken ct)
```

1. Load the card with `ExternalIssueRef`, `BoardColumn`, `Board.Columns`, `Board.WorkflowDefinitions`, `Revisions`. No ref → return `null` (the caller puts nothing on the wire).
2. Guards, each a `Skipped` result with the reason string from decision 5: settings switch off (`disabled`); `Board.TrackerKind == Internal` or `IssueTrackerConfigParser.TryParse` false (`tracker_inactive`); adapter not `IBidirectionalIssueTracker` (`tracker_read_only`); `TrackerTokenResolver.ResolveAsync` null (`token_unresolved`); `TrackerBidirectionalSyncService.IsRunning(board.Id)` (`sync_running` — expose the static guard as `internal static bool IsRunning(Guid)`).
3. `using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(settings.CardStatePushTimeoutSeconds)`.
4. `var changes = new List<TrackerSyncChange>(); var n = await PushStateAsync(tracker, config, issueRef, utcNow, changes, timeout.Token);` — but with one difference from the run: the per-card path must **surface** the failure, so `PushStateAsync` gets an `out`/return shape that says whether the arm threw (simplest: it returns a small record `StatePushOutcome { Changed, Failure }` and the run ignores `Failure` as it does today, keeping its Warning log).
5. If a state changed: best-effort `FetchByIdsAsync(config, [issueRef.ExternalId])` → `SyncLabelsAsync(…)`; a label failure is logged and does **not** change the outcome (the state is what the user asked for; the run repairs labels anyway).
6. S3 hook: on a successful close/reopen set `issueRef.UnannouncedStateChange = "closed" | "open"`.
7. `await _db.SaveChangesAsync(timeout.Token)`.
8. Map to the result: `Closed` / `Reopened` when a change was recorded; `InSync` when the arm found nothing to do (cursor already agrees); `Failed(reason)` for the caught exception (`OperationCanceledException` from the timeout → `timeout`; the caller's own `ct` cancelled → rethrow, nothing else does).

Everything in 1-8 sits inside one `try` whose `catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)` logs Warning and returns `Failed`. The service never throws to `CardService`.

**Settings.** `server/Application/Settings/TrackerSettings.cs`: `PushStateOnCardTransition = true`, `CardStatePushTimeoutSeconds = 15`; a validator rejecting `< 1`; bound in `Program.cs` next to the `OrchestratorSettings` block (`:130-131`) under section `Tracker` — the section name the tracker doc already reserved for `Tracker:NotifyOnLabelOnlyChanges`.

**DTO** (in `TrackerSyncDtos.cs`):

```csharp
public enum TrackerCardStatePushOutcome { Closed, Reopened, InSync, Skipped, Failed }
public sealed record TrackerCardStatePushResult(
    TrackerCardStatePushOutcome Outcome, TrackerKind TrackerKind, string ExternalKey, string Url, string? Reason);
```

**Registration.** `Program.cs` beside `TrackerBidirectionalSyncService` (`:522`); `BridgeQueueHarness` beside `ExternalTrackerSyncService` (`:141`) so every harness-built `CardService` can take it.

**Test fake.** Move `FakeBidirectionalTracker` from `TrackerBidirectionalSyncTests.cs:935` to `tests/Antiphon.Tests/TestHelpers/FakeBidirectionalTracker.cs` as `internal sealed`, unchanged, plus two knobs the new tests need: `HangOnSetState` (awaits a never-completing task, for the timeout test) and `FetchByIdsCalls`.

### S2 — the hook in `CardService`, the response, and `card.ps1`

- `CardService` gains an optional `TrackerCardStatePushService? trackerStatePush = null` constructor parameter (the `_launchResolver` precedent: production always registers it; a fixture that omits it gets no push).
- `MoveAsync`: after `SaveCardWriteAsync` and the review checkpoint (`:423-429`), `TrackerCardStatePushResult? push = null; if (targetColumn.IsTerminal && !wasTerminal && card.ExternalIssueRef is not null && _trackerStatePush is not null) push = await _trackerStatePush.PushForCardAsync(card.Id, ct);` — then the existing spawn/publish/`GetByIdAsync` tail; return `new MoveCardResult(dto, spawnedSessionId, spawnSuppressed, push)`.
- `ReopenAsync`: after `SaveCardWriteAsync` (`:625`), same call unconditionally on the ref; return `new ReopenCardResult(dto, push)`.
- `MoveCardResult` gains `TrackerCardStatePushResult? TrackerPush = null` with `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, so an unlinked card's PATCH response is byte-identical to today. New `ReopenCardResult(CardDto Card, TrackerCardStatePushResult? TrackerPush)` for `POST /reopen` (same ignore condition).
- `CardEndpoints` reopen route returns the new record; no other route changes.
- `card.ps1`: after `moved to <column>` (`:483`) and after the reopen `Write-CardLine` (`:524`, now `$updated.card`), print one line when `trackerPush` is present:
  `GitHub      closed #34 (https://github.com/…/issues/34)` / `GitHub      reopened #34 (…)` / `GitHub      already in sync (#34)` / `GitHub      skipped: <reason>` / `GitHub      push FAILED: <reason> - the next scheduled sync will retry`. Use `GitHub` for `GitHubIssues`, else the tracker kind name.
- `client/src/api/boards.ts`: `MoveCardResult` gets `trackerPush?: TrackerCardStatePush | null`; `reopenCard` becomes `apiPost<ReopenCardResult>` (the mutation's `onSuccess` ignores the body, so nothing else moves). No UI surface in this card — the board refresh already comes from `CardChanged`; a toast is a later nicety.

### S3 — digest continuity for the scheduled run

- `ExternalIssueRef.UnannouncedStateChange` (`string?`, `HasMaxLength(40)` like the cursor, `AppDbContext.cs:1319`); EF migration `AddExternalIssueRefUnannouncedStateChange` via `dotnet ef migrations add … --project server` (CLI only, `project-context.md:125`).
- `TrackerBidirectionalSyncService.SyncBoardAsync`, right after the refs dictionary is built (`:139`): for each ref with a non-null value add `Change(value == "closed" ? ClosedOnGitHub : ReopenedOnGitHub, ref)` to `changes` and clear the field. The run's counters are untouched (`StateChanges` keeps counting *this run's* writes); update the `TrackerSyncBoardResult.Changes` doc comment (`TrackerSyncDtos.cs:31-36`) to say `Changes` may also carry state pushes made at card-close time since the last run, so the "equivalent to the counter sum" sentence stops being asserted as an identity.
- Any run consumes it, whether or not it was asked to `notify` — the same rule as the run's own changes (a board-button run silently changes things today, and the doc says a click must not ping the chat).
- `TrackerSyncSummaryFormatter` is unchanged: the line already reads "1 issue closed on GitHub: CARD-0344 (#34)".

### S4 — docs and records

- `docs/workflow-tracker-block.md`: **Triggers** gets a fourth bullet — *Card close / reopen (CARD-0347):* `PATCH /api/cards/{id}` into a terminal column and `POST /api/cards/{id}/reopen` push that one card's state (closing comment, `state`/`state_reason`, managed `status:*` label) synchronously after the local write commits; bounded by `Tracker:CardStatePushTimeoutSeconds` (15), disabled by `Tracker:PushStateOnCardTransition=false`; never fails the card write; the outcome is on the response (`trackerPush`) and in `card.ps1`'s `GitHub` line; a failure leaves the cursor alone so the next scheduled run retries; a run already in flight for the board wins. **Ownership / out of scope**: keep "archive→GH-close" with the follow-up card number. The preserved gotcha at `:102` gets one appended sentence naming the per-card push as the other legitimate write trigger.
- `docs/antiphon-api.md`: the issue-tracker sync section (`:423-433`) gets the same trigger sentence; the card routes note `trackerPush` on `PATCH /cards/{id}` and the `ReopenCardResult` shape on `POST /reopen`.
- `AGENTS.md` "Cards and tracker": append to "Tracker writes are explicit, YAML-activated actions, not orchestration-tick side effects" — "; a card close or reopen pushes its own issue's state synchronously (CARD-0347)".
- File the follow-up card: *"Archiving a GitHub-linked card: decide and implement the GitHub side"* — options: comment + `not_planned` close when the card was still open; comment only; nothing (today); and whether `unarchive` reopens. Reference this plan's decision 3.
- Correct the record on CARD-0344 (`card.ps1 edit -ReasonFile …`, `-DescriptionFile` replaces the whole text so pull it from history first): #34 **was** closed by the 14:00Z scheduled sync with the closing reason as the comment. The orchestrator owns that edit.
- `docs/cards/` is generated; do not touch.

## Slices, order, verification

Order **S1 → S2 → S4**, then **S3** (separable; needs a migration, so it lands as its own commit). Each slice is one commit with the real outcome in the message.

| Slice | Server (TUnit, `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0347/ --treenode-filter …`) | Client (`pwsh -File scripts/test-client.ps1`) | Live |
|---|---|---|---|
| S1 | `"/*/*/TrackerBidirectionalSyncTests/*"` all green after the extraction (only comment wording differs; the marker assertions hold). New `"/*/*/TrackerCardStatePushServiceTests/*"` ([Category("Integration")], [NotInParallel], `TestDbFixture`, seed copied from `SeedLinkedBoardAsync`): (1) linked `Done`, cursor `open` → one `PostCommentCalls` whose body starts with `Card <id> closed as **Done** on Antiphon.` and ends with the system marker, one `SetStateCalls` (`closed`, `completed`), cursor `closed`, `LastOutboundSyncedAt` stamped, `FetchByIdsCalls == 1` and the `status:done` label written, outcome `Closed`, `ExternalKey == "#1"`; (2) `Canceled` → `not_planned`; (3) cursor already `closed` → `InSync`, `WriteCallCount == 0`; (4) non-terminal card with a fresh `Reopen` revision → comment + `SetState(open, reopened)` → `Reopened`; a second call → `InSync` (no double push, `LastOutboundSyncedAt` gate); (5) `ThrowOnPostComment` → `Failed`, no `SetStateCalls`, cursor still `open`; (6) `HangOnSetState` with `CardStatePushTimeoutSeconds = 1` → `Failed("timeout")` within ~2 s and the card row untouched; (7) unlinked → `null`; Jira-linked with a read-only fake → `Skipped(tracker_read_only)`; board `Internal` → `Skipped(tracker_inactive)`; unresolved `token_key` → `Skipped(token_unresolved)`; (8) switch off → `Skipped(disabled)`, zero writes; (9) `RunningBoards` held for the board → `Skipped(sync_running)`, zero writes. | — | — |
| S2 | New `"/*/*/CardServiceTrackerPushTests/*"` on `BridgeQueueHarness` + a registered `FakeBidirectionalTracker` as `IIssueTracker` + `TrackerCardStatePushService`: `MoveAsync` into `Done` on a linked card → `result.TrackerPush.Outcome == Closed`, one `SetStateCalls`, the card is `Done` first (assert the DB row even when the fake throws); unlinked card → `TrackerPush == null` and `WriteCallCount == 0`; a Backlog→InProgress move on a linked card → no push; `ReopenAsync` → `Reopened`; fake throwing → card still `Done`, `Failed` reason present. Endpoint pin in `CardCorrectionApiTests`' factory style: `PATCH /api/cards/{id}` on an unlinked card's JSON has **no** `trackerPush` property; on a linked card it has `"outcome":"closed"`. `"/*/*/OrchestratorTrackerCadenceTests/*"` still green (constructor pin). | `boards.test.tsx` green with the optional field; `reopenCard` typed to `ReopenCardResult`. | `card.ps1 close` on a throwaway issue linked to a scratch card (write-side live testing uses a throwaway issue — the CARD-0166 rule): the `GitHub closed #N` line prints, `gh issue view N --json state,stateReason,comments` shows `CLOSED`/`COMPLETED`, the headline comment and `status:done`; `card.ps1 reopen` reverses it. Then run `scripts/github-sync.ps1` and confirm zero new writes for that issue. |
| S3 | `TrackerBidirectionalSyncTests` new case: a ref with `UnannouncedStateChange = "closed"` and cursor `closed` → the run's `Changes` has one `ClosedOnGitHub` naming the card and `#1`, `StateChanges == 0`, `WriteCallCount == 0`, the field is null afterwards; `"/*/*/TrackerSyncSummaryFormatterTests/*"` unchanged. `TrackerSyncEndpointTests` notify pin still green. | — | After the S2 live close, run `github-sync.ps1 -Notify` and confirm the channel receives "1 issue closed on GitHub: CARD-xxxx (#N)" once, and a second run says nothing. |
| S4 | — | — | Docs re-read; follow-up card filed; CARD-0344 record corrected. |

Delete every `bin-c0347` directory after the last run (`Get-ChildItem . -Recurse -Depth 2 -Directory -Filter bin-c0347 | Remove-Item -Recurse -Force`).

## Non-goals and known edges

- **Archive** (decision 3) — follow-up card.
- **Free-form labels, `priority:*`, discussion comments, content-edit comments, creates** stay on the scheduled run. The close-time push touches only the state and the managed `status:*` label of the one issue.
- **No immediate chat ping.** A card drag to Done must not ping the family chat any more than the board's sync button does; the digest picks it up on the next `notify=true` run (S3).
- **Double comment in the race window** between a per-card push and a run already past the guard check is theoretically possible but bounded to the seconds a run spends on one board; the guard check in S1 covers the common case, and the state PATCH is idempotent either way.
- **Terminal → terminal moves** (`Done` → `Canceled`) are not reachable through `MoveAsync` today (`CardStateMachine`), so the hook's `!wasTerminal` condition is exact; if the state machine ever allows it, the `state_reason` change is a `SetState` on a closed issue and the hook condition should become `targetColumn.IsTerminal && (!wasTerminal || statusChanged)`.
- **Time cost per close**: two to four GitHub calls (comment, PATCH state, GET issue, label write) — roughly one second on a good day, at most the timeout on a bad one. Acceptable for an operator action; the setting exists for the bad week.

## Estimate

S1 is the bulk (a careful extraction, the service, ~10 tests): a Codex/Grok delegate on the Execute lane, one session. S2 is small once S1 exists (three call sites, two DTOs, `card.ps1`, one client type, ~6 tests). S3 is small plus a migration. S4 is documentation and two board writes. Sequential dispatch S1 → S2 (same delegate, same worktree) is the sensible shape; S3 and S4 can follow as one more.
