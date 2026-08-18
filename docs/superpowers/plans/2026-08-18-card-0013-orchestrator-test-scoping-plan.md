# CARD-0013 — OrchestratorServiceIntegrationTests global-count assertions

**Status:** planned, not yet implemented (verified 2026-08-18 — the class still carries
`[NotInParallel("Orchestrator")]` and the counter assertions are unchanged). The card has not
flaked, and the analysis below shows exactly why it hasn't — and why it still deserves the fix.

## 1. Finding: the card's prescribed remedy is already done; the real gap is different

The card says "scope the assertions to rows the test created". An audit of all 18 tests in
`tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs` shows **every DB-row
assertion is already scoped** — `SingleAsync(c => c.Id == graph.Card.Id)`,
`CountAsync(s => s.CardId == graph.Card.Id)`, `SingleAsync(c => c.BoardId == graph.Board.Id)`,
`Single(r => r.CardId == graph.Card.Id)` throughout. Nothing row-level needs changing.

What remains are the **counter assertions**, which cannot be scoped because they are plain ints
aggregated inside the service:

- `result.Dispatched.ShouldBe(0|1)`, `result.SkippedGlobalConcurrency.ShouldBe(1)`,
  `result.SkippedColumnConcurrency.ShouldBe(1)`, `result.Reconciled.ShouldBe(0|1)`,
  `result.EligibleCards.ShouldBe(0)`, `result.Failures.ShouldBe(0)` — all from
  `OrchestratorTickResult` (`server/Application/Dtos/OrchestratorDtos.cs`).
- `state.RetryQueueLength.ShouldBe(0)` from `GetStateAsync`.
- `synced.ShouldBe(1)` from `ExternalTrackerSyncService.SyncAsync` (a total across ALL
  non-Internal boards in the DB).
- `tracker.FetchCandidatesCalls.ShouldBe(1)` — incremented once per non-Internal board of the
  matching kind that the sync sweeps, so it is a board-count in disguise, same class of hazard.

## 2. Why it has never flaked — the sweep is already half-scoped

The harness sets `OrchestratorSettings.InternalTrackerRepositoryPathPrefix = tempRoot` (a
per-test GUID temp dir), and every orchestrator query — candidates, reconcile, retry queue,
state — filters through `ApplyScope`
(`server/Application/Services/OrchestratorService.cs:290-315`):

```
TrackerKind != Internal || LocalRepositoryPath.StartsWith(prefix)
```

So **Internal boards from every other suite are filtered out** (their repo paths can never start
with this test's GUID tempRoot), and 13 of the 18 tests use only Internal boards — their counters
genuinely count nothing but this test's rows. That is the deliberate isolation mechanism, and it
is why the exact counts have held.

The hole is the left side of that `||`: **every non-Internal board in the shared database passes
the scope filter**, for all three sweeps (candidates, reconcile, external-tracker sync — the sync
enumerates `Boards.Where(b => b.TrackerKind != TrackerKind.Internal)` with no prefix at all,
`ExternalTrackerSyncService.cs:38-43`). Five tests in this class create GitHubIssues/Linear
boards. Today this never fires because:

1. the class is `[NotInParallel("Orchestrator")]`, so its own five non-Internal boards are never
   live concurrently (and each test deletes its graph in `finally` via
   `CleanupProjectsByTempRootAsync`);
2. **no other suite in the assembly persists a non-Internal board** — verified:
   `IssueTrackerAdapterTests` never touches the DB (pure adapter tests over stubbed HttpClient),
   and `KanbanPersistenceTests`' only Linear reference is an `ExternalIssueRef` row on a card
   whose board stays Internal.

Point 2 is exactly the trap the card names: a hidden assembly-wide assertion — "no other test has
non-Internal board data right now" — that holds by luck of what other suites happen to create.
The first future suite that persists a GitHubIssues or Linear board breaks all 18 tests' ticks in
both directions:

- **inbound:** the foreign board's eligible cards inflate `EligibleCards`/`Dispatched`, and —
  worse than a count — a foreign dispatch **steals the single queued fake adapter**
  (`QueueAdapterFactory` throws "No fake adapter was queued" for the second claim), so
  `adapter.SentPrompt.ShouldContain(...)` and `Failures.ShouldBe(0)` fail in ways no assertion
  scoping can repair;
- **outbound:** this class's `PollTickAsync` would *mutate the other suite's rows* — reconcile
  clears foreign `OwnerSessionId` claims and fails their sessions
  (`Reconcile_handles_external_tracker_claims` shape), and the tracker sync can create cards on
  or terminal-move cards of any foreign board whose kind matches a registered fake tracker.

## 3. The fix — the AgentSupervisionTests treatment, one line plus a comment

This is precisely the shape CLAUDE.md's shared-Postgres rule ends on: *"a test that drives a
global sweep needs `[NotInParallel]` with NO group key — a key serialises it only against
itself"*. `AgentSupervisionTests` (`tests/Antiphon.Tests/Application/AgentSupervisionTests.cs:25-32`)
is the precedent, ungrouped with a comment explaining why. Mirror it:

1. **`OrchestratorServiceIntegrationTests.cs`** — change the class attribute:

   ```csharp
   [Category("Integration")]
   // Ungrouped on purpose: PollTickAsync sweeps the assembly-wide test database — the tempRoot
   // prefix scope (InternalTrackerRepositoryPathPrefix) pins Internal boards to this test, but
   // every NON-Internal board anywhere in the DB passes the scope filter, for the candidate,
   // reconcile AND external-tracker-sync sweeps. Five tests here create GitHubIssues/Linear
   // boards; a foreign one would inflate the tick counters, steal the single queued fake
   // adapter, and let this class's reconcile/sync mutate the other suite's rows. The DB-row
   // assertions are already scoped; the tick counters are unscopable ints, so exclusive
   // execution is what makes their exact values honest (same lesson as AgentSupervisionTests).
   [NotInParallel]
   public class OrchestratorServiceIntegrationTests
   ```

2. **Keep every counter assertion exact.** Do NOT weaken `Dispatched.ShouldBe(1)` etc. to
   `ShouldBeGreaterThanOrEqualTo` — exact dispatch/skip/reconcile counts are the point of these
   tests (a `>= 1` would let a double-dispatch bug pass), and under keyless `[NotInParallel]`
   plus the prefix scope they are deterministic. The `ShouldBeGreaterThanOrEqualTo` remedy is for
   sweep totals a test cannot make deterministic; these now can be.

3. **`CLAUDE.md`** — in the shared-Postgres gotcha, replace the sentence
   "Known remaining instances: `OrchestratorServiceIntegrationTests` asserts … same shape." with a
   note that it is fixed (ungrouped `[NotInParallel]`, counters deliberately exact) or simply
   delete it, so the gotcha stops advertising a stale known-instance.

### Rejected alternatives

- **Scoping the counters** — impossible; they are aggregate ints computed inside the service.
- **`ShouldBeGreaterThanOrEqualTo` on the counters** — doesn't survive the real failure mode (the
  adapter-steal makes the test fail elsewhere anyway) and weakens the dispatch-semantics signal.
- **Tightening `ApplyScope` so non-Internal boards must also match the prefix** — a production
  behavior change to fix a test: `InternalTrackerRepositoryPathPrefix` is documented and named as
  an *Internal*-tracker scope, and prod deployments that set it would silently stop orchestrating
  external-tracker boards. Out of scope for a test-hygiene card.

## 4. Verification

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0013/ --treenode-filter "/*/*/OrchestratorServiceIntegrationTests/*"
```

(then delete the `bin-card0013` dirs: `Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0013 | Remove-Item -Recurse -Force`).
All 18 tests should pass unchanged — the attribute affects scheduling, not behavior. A full-assembly
run afterward confirms nothing deadlocks on the new exclusive group (the assembly already runs
three keyless `[NotInParallel]` classes: `AgentSupervisionTests`, `AgentTuiApiTests`,
`AgentTuiDiscoveryTests`).

## 5. Slice

One commit: `test(orchestrator): CARD-0013 - ungrouped NotInParallel; tick counters are
assembly-global on non-Internal boards` (attribute + comment + CLAUDE.md line). No production
code changes, no migration.
