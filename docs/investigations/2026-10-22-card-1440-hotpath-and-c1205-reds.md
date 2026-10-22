# CARD-1440: hot-path and C1205 base reds

Date: 2026-10-22. Investigated on server2 (Linux) at `121ba48139963e85b420b7c04eee8e24ef160e27`
(this includes CARD-1403 `a550bcbd2`; base 99eb4a62 is an ancestor).

**Verdict: both reds are confirmed, and both are stale test fixtures.** Both come from the same
commit, `dd039a405c9425d7cffc83b64d60a1032d940636` (`WIP CARD-1259 S2: persist deferred transcript
actions and own bounded workers`, 2026-10-15). That commit added two product invariants that
these older fixtures (CARD-0698 2026-09-25, CARD-1205 2026-10-13) were never updated for. The
product code is behaving as designed. CARD-1403 does not change either failure: both reproduce
with it applied.

## 1. TranscriptHotPathQueryTests: InvalidOperationException during ingest

### Mechanism

- `tests/Antiphon.Tests/TestHelpers/TranscriptHotPathFixture.cs:37-43` builds its own
  `ServiceCollection` that registers only `AppDbContext`. It then constructs
  `AgentSessionRuntime` through the short overload with no `states:` argument and with
  `NullLogger`. `SessionStateStore` and `SessionRuntimeActionCoordinator` are never registered.
- `server/Application/Services/AgentSessionRuntime.cs:1234`, in `PersistTranscriptAsync`:
  `await (_states ?? throw new InvalidOperationException("Transcript ingestion requires SessionStateStore."))`.
  Before dd039a405, this line was `_states is null ? null : await _states.BeginWriteAsync(...)`, so a
  null store was allowed (seen in `git show dd039a405 -- server/Application/Services/AgentSessionRuntime.cs`).
  `git log -S'Transcript ingestion requires SessionStateStore'` names only dd039a405.
- The `catch` at `AgentSessionRuntime.cs:1249-1251` records the failure, and
  `PersistFailureDetail` (`:1758`) keeps only the SQL state or the innermost exception type. That
  is why the failure record showed a bare `InvalidOperationException`. The `NullLogger` dropped
  the `LogWarning` that carried the message.
- `TranscriptHotPathFixture.MembershipAsync` (`:128-129`) then asserts
  `TryGetTranscriptPersistFailure(...).ShouldBeFalse()`. Three tests call `MembershipAsync`, and all
  three fail. `Working_batch_uses_one_statement_and_indexed_boundaries` calls only `WorkingAsync`
  (no ingest), so it passes. This matches the observed 3-of-4 split.
- There is a second latent fault behind the first. `PersistTranscriptAsync` also calls
  `Actions().Hint(sessionId)` (`:1246`, also from dd039a405), and that resolves
  `SessionRuntimeActionCoordinator` from the scope (`:1146-1150`). In this fixture, that would
  throw a second IOE (`No service for type`) after a successful commit.

### Evidence (narrow runs, alternate OutputPath, build slot)

| Run | Filter | Change in the worktree (uncommitted, since reverted) | Result |
|---|---|---|---|
| H1 | `/*/*/TranscriptHotPathQueryTests/Uuid_membership_seeks_by_session_and_uuid` | `RecordingLogger` in place of `NullLogger`, printing entries after persist | 1/1 failed; logged: `Transcript state unavailable for session … :: System.InvalidOperationException: Transcript ingestion requires SessionStateStore.` |
| H2 | `/*/*/TranscriptHotPathQueryTests/*` | H1, plus the fixture registers `SessionStateSettings`, `SessionStateCommandMetrics`, `ISessionStateLoader→SessionStateLoader`, `SessionStateStore`, `SessionRuntimeActionCoordinator` and `AddLogging`, and passes `states:` | 4/4 passed |

H2 shows that the missing wiring is the whole cause. No plan or index assertion regressed.

## 2. C1205_Concurrent_commit_invalidates_snapshot(replace-frontier, *): 23503

### Mechanism

- `Harness.CreateAsync` (`tests/Antiphon.Tests/Application/WorkingTranscriptRecoveryTests.cs:478`)
  ingests the `UserPrompt` row through `Runtime.PersistTranscriptAsync`.
- Since dd039a405, every persisted row is admitted as a durable action:
  `AgentSessionRuntime.cs:1396` (and the per-row fallback at `:1488`, `:1524`) calls `AdmitActionsAsync`, which calls
  `SessionRuntimeActionCoordinator.Admit` (`:1470`). That writes a `SessionRuntimeAction` with
  `TranscriptEntryId = row.Id` (`SessionRuntimeActionCoordinator.cs:46-47`).
- The FK is `OnDelete(DeleteBehavior.Restrict)` (`server/Infrastructure/Data/AppDbContext.cs:1637`;
  schema in `server/Migrations/20261015181237_SessionRuntimeActions.cs:44-46`, commit 4e58e05c6
  CARD-1259 S2).
- In the `replace-frontier` variant, the test deletes all of the session's `TranscriptEntries`
  directly (`WorkingTranscriptRecoveryTests.cs:138`) and leaves the action row. PostgreSQL then
  rejects the delete. The other variants (`new-prompt`, `queue-annotation`, `generation`,
  `remote-ownership`, `terminal-status`) delete no transcript rows, so they pass.
- Product code respects this invariant. The only product-side transcript delete,
  `DataRetentionService.cs:299`, is guarded by `SessionRuntimeActions.AnyAsync(...)` at `:290`
  (also `:191`, `:251`, `:357`; those guards were added in the same dd039a405). The test's
  direct delete models a state the product never produces while an action row exists.

### Evidence

| Run | Filter | Result |
|---|---|---|
| C1 | `/*/*/WorkingTranscriptRecoveryTests/C1205_Concurrent_commit_invalidates_snapshot` (same build as H2; that change does not touch this class) | 12 cases, 10 passed, 2 failed: `(replace-frontier, False)` and `(replace-frontier, True)`, both `PostgresException: 23503 … FK_SessionRuntimeActions_TranscriptEntries_TranscriptEntryId`, thrown at `WorkingTranscriptRecoveryTests.cs:138` (`ExecuteDeleteAsync`) |

## Bisect

No build bisect was needed, because the pickaxe and diff evidence is decisive:
- The IOE text and the unconditional `Actions().Hint` both first appear in dd039a405.
- The action admission per persisted row (`AdmitActionsAsync`) first appears in dd039a405, and
  the Restrict FK it fills was added in 4e58e05c6. Both are CARD-1259 S2, and both are ancestors
  of 99eb4a62.
- Both test files predate CARD-1259: the hot-path fixture is from e6bd9410c 2026-09-25, and the
  C1205 tests were last edited at 7f8448798 2026-10-13. Neither was touched by CARD-1259.

## Remaining uncertainties

- dd039a405 is a WIP commit. I did not run its parent to show the tests green there. The verdict
  rests on reproducing the failure at HEAD, on H2 turning the class green with only fixture
  wiring, and on the pickaxe and diff.
- H2 used `new SessionStateSettings()` (the default cache setting). I did not check whether the
  cache-enabled store adds captured commands that change the `Capture.Membership` counts under
  other settings. With the defaults, all count assertions held.
- I did not establish why CARD-1259's own Review missed these. The likely reason is that both
  classes are `[Category("Slow")]` and integration, and so were outside its checkpoint selection.

## Proposed fixes (test-only; for the Code stage)

1. `TranscriptHotPathFixture`: register `SessionStateStore` and its dependencies plus
   `SessionRuntimeActionCoordinator` (the same set as `BridgeQueueHarness.cs:173-177`), and pass
   `states:`. Replace `NullLogger` with a `RecordingLogger`, and make `MembershipAsync`'s failure
   assertion include the logged exception, so a future persist failure is not reduced to a type
   name.
2. `C1205_Concurrent_commit_invalidates_snapshot`, `replace-frontier` branch: delete the session's
   `SessionRuntimeActions` before its `TranscriptEntries` inside the same mutation
   (`WorkingTranscriptRecoveryTests.cs:136-142`), so the variant models a legal replacement.

## Not done, noted

- Possible product hardening: `PersistFailureDetail` could keep the exception message as well
  as the type. This was not assessed for secret or PII exposure, so it is left for a separate decision.
