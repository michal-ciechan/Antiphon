# CARD-0567 — Four groups of pre-existing land-test failures: root causes and fixes

Plan stage, task `c7b07802`, written against `1044eef7` (master as of 2026-09-19).
Verification design is inline (the brief folds test-design into this dispatch); Build can
execute it directly. Defaults are enumerated as D-n. Every slice below was prototyped in this
worktree and run green against `1044eef7`; the exact patch is
`2026-09-19-card-0567-land-test-baseline-failures-prototype.diff` next to this file.

## Outcome and scope

Twelve tests in three land test classes fail identically at every base SHA the nightly and
review passes have tried since 2026-09-15. All four groups are **stale tests**, not
production regressions: each test still asserts the shape of a production path that a later,
deliberate land change replaced. Two production commits explain all twelve failures. No
production behaviour is wrong; one behaviour-neutral production edit is made anyway (a
private method becomes internal and its marker formatter is extracted, so the seam that
failed silently by reflection fails at compile time instead).

In scope: make the twelve tests green against current production semantics while keeping
what each test was written to guard, and remove a second latent defect found on the way (the
C508 matrix cannot finish inside its single 180 s budget on a loaded host). Out of scope: the
dead protocol branch and the vestigial timestamp variant noted at the end (surfaced for a
follow-up card, not changed here).

## Ground truth

| Card assumes | Code actually does | Consequence |
|---|---|---|
| Group 1 fails "before an operation is even created", suggesting a fixture race or a missing precondition. | Since `ba2dd86d` (CARD-0488, 2026-09-11) `AgentTaskLandSourceResolver.ResolveAsync` observes the **remote source branch** (`ls-remote`, `fetch`, `merge-base`) before `CreateOperationAsync`. The test faults *every* `ls-remote`/`fetch`/`merge-base`, so the fault is consumed by the source observation and the resolver refuses (`source_remote_unreadable` etc.) with no `AgentTaskLanding` row. | Not a race. The fault must be scoped to the target-branch observation, exactly as `RR_V4_RemoteReadFailureRetriesUnchangedSource` (same era, still green) already scopes its `ls-remote` injection to `TargetRef`. |
| Group 2 is "a stale assertion against a trace shape that changed". | The trace shape did not change. The fabricated request row is schema 1 with no `ExpectedSourceSha`; since `ba2dd86d` `AgentTaskLandService.RunRequestAsync` refuses such a row at admission (`legacy_review_binding_required`, line ~299) before the resolver or protocol ever inspects the source. No `status` on the source is run at all. | The assertion "source status ran" was the test's proof that execution reached the protocol. That proof is now impossible for a legacy row and must be replaced by proof that the recovery settled the legacy request. |
| Group 3 is "a genuine type-safety bug worth finding precisely". | `CollectUnlandedSiblingsAsync` changed its tuple from `(string? Marker, …)` to `(IReadOnlyList<string> Siblings, …)` in `0fdc1392` (CARD-0443, 2026-09-15). The test (`70118af9`, 2026-09-14) reaches the private method by reflection and casts `Item1` to `string`. The production tuple is used correctly at its one call site. | Test-side staleness hidden by reflection. The commit that changed it says "14 baseline failures confirmed, verification pending" in its own message. On a loaded host the test never even reaches the cast: its single 180 s `[Timeout]` expires during the second real land (see evidence). |
| Group 4 is "a reason code renamed without updating the test". | Neither code was renamed. `remote_read_failed` is still returned by `LandingGit.ObserveAsync` (target) and still asserted by `LandingGitTests` and `RR_V4`. Since `ba2dd86d` the remote **source** is observed on origin's **push** endpoint (CARD-0488 plan D-3 / G-33: never the fetch URL), so an unusable push URL now refuses in source resolution with `source_remote_unreadable`, before the target read that produced `remote_read_failed`. | The test's premise comment ("fetch URL stays the bare remote so prepare succeeds; push URL is unusable so finalize fails") is exactly what CARD-0488 removed. The same commit touched this test (`RequestHeadAsync`) without updating the oracle. |
| The four groups are likely independent. | Groups 1, 2 and 4 share one root cause (`ba2dd86d`: source resolution before operation creation). Group 3 is independent (`0fdc1392`). | Two production commits explain all twelve failures. |

### Blame table

| Group | Tests | Test last changed | Production change that invalidated it | Verdict |
|---|---|---|---|---|
| 1 | `AgentTaskLandPublicationTests.C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment` ×8 | `e8b6baa4` 2026-09-10 | `ba2dd86d` 2026-09-11 (resolver's `ObserveSourceAsync` + `ClassifyAsync` run before any operation) | stale test |
| 2 | `AgentTaskLandRefusedRetryTests.RR_V5_OriginalPendingRequestCannotReplaceRefusal`, `RR_V5_EqualPendingTimestampCannotReplaceRefusal` | `fc165e3d` 2026-09-10 | `ba2dd86d` 2026-09-11 (`canResolve` gate refuses schema-1 requests at admission) | stale test |
| 3 | `AgentTaskLandStageOutcomeTests.C508_RebasedSiblingMarkerMatrix` | `70118af9` 2026-09-14 | `0fdc1392` 2026-09-15 (tuple `Item1` became `IReadOnlyList<string>`) | stale test (reflection hid the break) + too-tight single timeout |
| 4 | `AgentTaskLandStageOutcomeTests.unreadable_push_endpoint_refuses_before_any_stage` | oracle `2f3b9dae` 2026-09-08; plumbing `ba2dd86d` 2026-09-11 | `ba2dd86d` 2026-09-11 (source observed on the push endpoint) | stale test |

## Decisions

### D-1. Group 1 keeps its purpose: scope the injected faults to the target-branch observation

The test guards one property: when the remote **target** cannot be read, landing must not
fall back to cached (`refs/remotes/origin/master`, which the test deliberately points at the
source) or local containment. That property lives in the protocol's `ObserveAsync` and is
unchanged. The fix is fixture-side: fault only the commands of the target observation.

Discriminator (the resolver and the protocol's source rechecks never issue these shapes):

- `ls-remote` whose arguments contain `h.Fixture.TargetRef` (`refs/heads/master`); source
  reads name `refs/heads/feat/card-task-…`.
- `fetch` whose refspec starts with `TargetRef + ":"` (`refs/heads/master:refs/antiphon/land/…/remote-observed/…`);
  source fetches start with the source ref.
- `merge-base` only once a target `ls-remote` is already in `h.Fixture.Git.Trace`. The
  resolver's `ClassifyAsync` ancestry checks use the same argument shape
  (`merge-base --is-ancestor <sha> <seed>`) but run before any target read; the trace is
  cleared right before `RunAsync`, and `FixtureGit.RunAsync` appends to the trace before
  invoking `BeforeCommand`, so the ordering is observable synchronously.

Two extra guards pin the discriminator's meaning: the trace must contain a source `ls-remote`
(the resolver ran and was not faulted) and `operation.SourceRemoteSha` must equal
`h.Fixture.SeedSha` (the operation was created from a real remote-source observation). The
six non-throwing rows additionally pin the exact refusal code the target observation yields
(`remote_read_failed` for `read-error`/`missing`, `remote_response_invalid` for
`empty`/`malformed`, `remote_fetch_failed`, `remote_ancestry_error`); the original test only
checked that no private marker leaked, which stays.

Rejected: (a) accept refusal-without-operation and assert on the request row — that would
retarget the test at the source observation and stop exercising the target-containment
property; (b) fault by call count — brittle against the recheck cadence; (c) a DB read of
`OperationAsync()` inside `BeforeCommand` to detect "operation exists" — works, but the
trace-order predicate is synchronous and already the established style in this file.

### D-2. Group 2 keeps the legacy row and replaces the "source status ran" proof with "the legacy request was settled by its own terminal event"

What the two tests can still meaningfully guard: after a restart, a pending request row that
carries no review binding must not create a second operation, must leave A active with its
refusal untouched, must not touch the target, and must be settled (not left pending forever).
All of that is exactly what the `legacy_review_binding_required` admission gate does, and
legacy rows are a real production state (requests persisted before 2026-09-11).

The replacement guard: the last `IsLandTerminal` event after recovery is a **new** event
(different id from the one that settled A), carries `LandRequestId == <fabricated request>`,
`LandingOperationId == A.Id`, and `Detail` containing `legacy_review_binding_required`; the
fabricated row reads back `IsPending == false`, `State == Completed`,
`TerminalEventId == <that event>`. The existing "no target status, no `update-ref`",
pins-unchanged and `AssertRefusalAsync(A)` checks stay.

Rejected alternatives, with the facts that kill them:

- Revive A's own approval request (`A.ApprovalLandRequestId`) as the pending row so the
  protocol's Refused branch (`explicitRequest == false`) is reached and the source-status
  assertion survives. `PersistRefusalAsync` returns early when `request.TerminalEventId` is
  set, and `AgentTaskEvents` has a unique filtered index on `LandRequestId WHERE IsLandTerminal`
  (`AppDbContext.cs:1638`), so the row cannot be revived without deleting A's terminal
  event — modelling a state that atomic settlement (CARD-0467) makes impossible. The test's
  own 2026-09-10 comment already refused this for the same reason.
- Fabricate a schema-2 row with `ExpectedSourceSha` so the protocol is reached. A distinct
  request row with a binding *is* an explicit retry by construction and creates B; that is
  `RR_V1`'s scenario, and it contradicts the test name.
- Inject a `TerminalCut` save fault to model "refused but not settled". Refusal phase and
  settlement commit in one transaction, so the cut leaves A at `RecoveryPinned`, and the
  recovery then *resumes* A and lands (the sentinel is deleted). Wrong scenario.
- Delete the two tests. `RR_V5c` covers "settled refusal is not re-queued" but nothing else
  covers "an unbindable pending row is refused and settled without touching A".

### D-3. Group 3: the seam becomes a typed internal call; the marker formatter is shared

`CollectUnlandedSiblingsAsync` becomes `internal` (the class already exposes `internal static`
seams for this test project: `FormatOutcome`, `AppendDetail`, `DurationSeconds`,
`VerifyAsync`; `InternalsVisibleTo("Antiphon.Tests")` exists). The marker composition at
`AgentTaskLandService.cs:374` moves into `internal static string? UnlandedMarker(IReadOnlyList<string> siblings)`
and the test's `CollectAsync` helper calls both directly. The next signature change is a
compile error in the test project, which is what the reflection helper hid.

Rejected: keep reflection and cast `Item1` to `IReadOnlyList<string>` — same failure class on
the next change, and the test would re-implement the marker format instead of asserting the
production one.

### D-4. Group 4: assert `source_remote_unreadable`, rewrite the premise comment, add "no operation row"

The refusal still happens before any stage (rows empty, worktree retained, pending cleared,
attempt 1). Only the reason and the comment change, plus one guard that no `AgentTaskLanding`
row exists — the stronger form of "before any stage". `remote_read_failed` remains covered
by `LandingGitTests` (unit, exit-code matrix) and `RR_V4_RemoteReadFailureRetriesUnchangedSource`
(target read faulted after a good source observation; green at `1044eef7`, see evidence).

Rejected: accept either reason — imprecise, and it would hide a future regression where the
source observation silently switches to the fetch URL (CARD-0488 G-33).

### D-5. Keep both group 2 test names and both timestamp rows

`equal`/`less` no longer take different production paths (no `RequestedAt`-vs-`UpdatedAt`
comparison exists on the refusal-replacement path; the only remaining request-timestamp
comparison is the bound-approval resume rule at `AgentTaskLandingProtocol.cs:458`). The two
rows cost one extra execution and keep the evidence trail in
`docs/investigations/2026-09-10-task-aa632ab3-*` and CARD-0567's own failure list readable.
The comment inside `RecoveryAsync` says so.

### D-6. No production behaviour change beyond D-3's visibility/formatter extraction

Every other edit is in the three test files. The Code stage must not "fix" production to
match the old oracles: the old oracles describe the pre-CARD-0488 ordering.

### D-7. Split `C508_RebasedSiblingMarkerMatrix` into three test methods, one per row

Measured on this host with two other worktrees' suites running: the two real-land rows take
1m49s and 1m11s and the component row 3s, so the original single method (two lands plus the
component, one `[Timeout(180_000)]`) expired at 3:00 in both the reproduction and the first
prototype run, before ever reaching the reflection cast. That makes the failure
host-dependent (the nightly reports the cast; a loaded review host reports a timeout) and
non-attributable. Three methods, `C508_RebasedSiblingMarkerMatrix_AllMinusSiblingIsSilent`,
`…_MixedSiblingKeepsMarker`, `…_PinnedVerifiedShaDecides`, each keep the 180 s budget and the
exact assertions; the `[Timeout]` cancellation-token parameter (TUnit0015) is deliberately
not threaded into the land calls, which take `CancellationToken.None` today.

Rejected: raise the single timeout to 10 minutes — keeps three scenarios behind one verdict
and one fixture lifetime; the prefix-named split is what the rest of this class already does
(`land_warns_…`, `land_is_silent_…`).

## Slices

### S1. Group 1 — `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs`

In `C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment`, replace the `BeforeCommand`
installation with a `TargetObservation(args)` predicate and a statement lambda that returns
`null` for anything else, keeping the eight fault arms verbatim:

```csharp
bool TargetObservation(IReadOnlyList<string> args) => args[0] switch
{
    "ls-remote" => args.Contains(h.Fixture.TargetRef),
    "fetch" => args.Any(a => a.StartsWith(h.Fixture.TargetRef + ":", StringComparison.Ordinal)),
    "merge-base" => h.Fixture.Git.Trace.Any(t => t[0] == "ls-remote" && t.Contains(h.Fixture.TargetRef)),
    _ => false,
};
h.Fixture.Git.BeforeCommand = (_, args) =>
{
    if (!TargetObservation(args)) return Task.FromResult<LandingGitResult?>(null);
    return Task.FromResult<LandingGitResult?>(fault switch { /* existing eight arms */ });
};
```

After the run, before the existing assertions:

```csharp
h.Fixture.Git.Trace.ShouldContain(a => a[0] == "ls-remote" && a.Contains(h.Fixture.SourceRef),
    "the remote source observation must run unfaulted before the target observation");
var operation = (await h.OperationAsync()).ShouldNotBeNull();
operation.SourceRemoteSha.ShouldBe(h.Fixture.SeedSha);
if (fault is not ("timeout" or "canceled"))
    operation.LastReason.ShouldBe(fault switch
    {
        "read-error" or "missing" => "remote_read_failed",
        "empty" or "malformed" => "remote_response_invalid",
        "fetch-error" => "remote_fetch_failed",
        _ => "remote_ancestry_error",
    });
```

Everything else in the method stays (RemoteConfirmedAt null, marker redaction on operation
and event, event bound to the operation, no push/rebase/remove, source directory and ref
intact, remote source intact). A statement lambda is used rather than a conditional
expression so the target-typed `new(...)` arms keep their type without casts.

### S2. Group 2 — `tests/Antiphon.Tests/Application/AgentTaskLandRefusedRetryTests.cs`

In `RecoveryAsync(bool equal)`:

- Capture `var settled = (await s.EventsAsync()).Last(e => e.IsLandTerminal);` after
  `RefuseAsync()`, and keep the fabricated request's `Id` in a local.
- Rewrite the comment on the fabricated row: it models a request persisted before
  CARD-0488 (schema 1, no `ExpectedSourceSha`) that a restart finds pending beside A's settled
  history; CARD-0488 refuses it at admission with `legacy_review_binding_required` before any
  source inspection, and CARD-0467's one-terminal-event-per-request rule is why A's own row is
  not revived. Note that `equal` no longer changes the path (D-5).
- Replace the `s.Trace.ShouldContain(t => s.IsSourceStatus(...) && … Output.Length == 0)` line with:

```csharp
var terminal = (await s.EventsAsync()).Last(e => e.IsLandTerminal);
terminal.Id.ShouldNotBe(settled.Id, "recovery must settle the legacy request with its own terminal event");
terminal.LandRequestId.ShouldBe(requestId);
terminal.LandingOperationId.ShouldBe(s.A.Id);
terminal.Detail.ShouldContain("legacy_review_binding_required");
await using (var db = s.H.CreateContext())
{
    var legacy = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
    legacy.IsPending.ShouldBeFalse();
    legacy.State.ShouldBe(LandRequestState.Completed);
    legacy.TerminalEventId.ShouldBe(terminal.Id);
}
```

The single-operation-A, active-pointer, `LastReason`-unchanged, no-target-status /
`update-ref`, pins and `AssertRefusalAsync(active)` checks stay.

### S3. Group 3 — `server/Application/Services/AgentTaskLandService.cs` and `tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs`

Production:

```csharp
/// <summary>The landed-event marker for the sibling tokens; null when nothing is unlanded.</summary>
internal static string? UnlandedMarker(IReadOnlyList<string> siblings) =>
    siblings.Count == 0 ? null : $"unlanded-sibling={string.Join(",", siblings)}";
```

- the call site: `var marker = UnlandedMarker(siblings);`
- `private async Task<(…)> CollectUnlandedSiblingsAsync(` → `internal async Task<(…)> CollectUnlandedSiblingsAsync(`
  (doc comment notes it is the typed seam for the stage-outcome component row).

Test: `CollectAsync` becomes

```csharp
var (siblings, warnings) = await land.CollectUnlandedSiblingsAsync(task, rebasedHeadRepo, CancellationToken.None, verifiedSha);
return (AgentTaskLandService.UnlandedMarker(siblings), warnings);
```

with no `System.Reflection` usage, and the matrix method is split per D-7 into
`C508_RebasedSiblingMarkerMatrix_AllMinusSiblingIsSilent`, `…_MixedSiblingKeepsMarker` and
`…_PinnedVerifiedShaDecides`, each `[Test] [Timeout(180_000)]`, bodies and assertions moved
verbatim (the three blocks were already self-contained: own repo, own schema, own `db`). The
class-level summary explains why.

### S4. Group 4 — `tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs`

In `unreadable_push_endpoint_refuses_before_any_stage`:

- Replace the premise comment: the push endpoint is the only endpoint landing reads
  (CARD-0488 D-3: the remote source is observed on origin's push URL, never the fetch URL), so
  an unusable push URL is refused by source resolution before any operation, stage row or Git
  mutation; the fetch URL stays the bare remote so `RequestHeadAsync` can still publish the
  source; a rival push before `RunAsync` would be an origin-ahead refusal instead.
- `refused.Detail.ShouldContain("remote_read_failed")` → `ShouldContain("source_remote_unreadable")`.
- Add `(await db.AgentTaskLandings.CountAsync(o => o.TaskId == task.Id)).ShouldBe(0, "refused before any operation exists");`.

### S5. Docs

Add two sentences to `docs/testing-and-build.md`, next to the combined-filter guidance
(CARD-0403 section): (1) when a land test injects a Git fault, scope it to the target-branch
observation, because CARD-0488's resolver observes the source branch first — the lesson of
groups 1 and 4; (2) the method-segment OR form `/*/*/Class/(MethodA*)|(MethodB*)` works on
the pinned TUnit 1.44, while a bare `|` between two full paths does **not**: it silently ran
the whole `AgentTaskLandRefusedRetryTests` class in one case and zero tests in another
(evidence below). Keep both to a sentence each.

## Verification design

### Simulating the conditions

All four groups are reproduced by the tests themselves against real git and an isolated
Postgres schema; no additional fixture is needed. The conditions the fixes must hold under:

- Group 1: every row of the fault matrix, including the two throwing rows (`timeout`,
  `canceled`) whose exception must still propagate out of `RunAsync` after the operation
  exists and be settled by `FailAsync` against that operation.
- Group 2: restart of the service graph, a sweep that re-enqueues the fabricated row, a
  queued execution under the fixture's queue claim.
- Group 3: the two land rows (all-minus, mixed) as their own methods, and the component row
  reproducing the frozen-SHA result (`pinned.Marker` set, `moving.Marker` null).
- Group 4: unusable push URL with a readable fetch URL, request pushed to the fetch URL.

### Guards

| Guard | Test | Passes when |
|---|---|---|
| G-1a target read fault cannot use cached/local containment | `C448_V05` all 8 rows | operation exists, `RemoteConfirmedAt` null, no push/rebase/remove, source dir and ref intact |
| G-1b the fault is consumed by the target observation, not the source one | `C448_V05` all 8 rows | trace has a source `ls-remote`; `operation.SourceRemoteSha == SeedSha` |
| G-1c exact target refusal codes | `C448_V05` six non-throwing rows | `LastReason` matches the D-1 table |
| G-1d thrown faults settle against the operation | `C448_V05` `timeout`, `canceled` | `Should.ThrowAsync` + `FailAsync` leave a `LandRefused` event with `LandingOperationId == operation.Id` and no private marker |
| G-2a legacy pending row cannot replace a refusal | `RR_V5_*` both | single operation A active, `LastReason` unchanged |
| G-2b recovery actually ran and settled the legacy row | `RR_V5_*` both | new terminal event bound to the fabricated request and to A, `legacy_review_binding_required`, row `Completed`/not pending |
| G-2c no mutation on recovery | `RR_V5_*` both | no target status, no `update-ref`, pins unchanged, `AssertRefusalAsync(A)` |
| G-3a marker/warnings from the typed seam | `C508_…_PinnedVerifiedShaDecides` | `pinned.Marker == unlanded-sibling=<short>:<branch>`, one warning; `moving` null/empty |
| G-3b production marker format is the one asserted | `C508_…_AllMinusSiblingIsSilent`, `…_MixedSiblingKeepsMarker` | landed event detail without/with the marker |
| G-3c each C508 row completes inside its own 180 s budget on a loaded host | the three C508 methods | measured 1m49s / 1m11s / 3s with two foreign suites running |
| G-4a unusable push endpoint refuses in source resolution | `unreadable_push_endpoint…` | no stage rows, no `AgentTaskLanding` row, `source_remote_unreadable`, worktree retained, pending cleared at attempt 1 |
| G-4b `remote_read_failed` still reachable | `LandingGitTests` (exit matrix), `RR_V4_RemoteReadFailureRetriesUnchangedSource` | unchanged, must stay green in the regression run |

### Execution profile (Code stage)

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c567/ --nologo
# the fourteen rows, method-scoped (three invocations; each fits a foreground window even on a loaded host)
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c567/ -- --treenode-filter "/*/*/AgentTaskLandPublicationTests/C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment*" --report-trx --report-trx-filename pub.trx --results-directory .antiphon/c567/pub
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c567/ -- --treenode-filter "/*/*/AgentTaskLandRefusedRetryTests/(RR_V5_OriginalPendingRequestCannotReplaceRefusal*)|(RR_V5_EqualPendingTimestampCannotReplaceRefusal*)" --report-trx --report-trx-filename rr.trx --results-directory .antiphon/c567/rr
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c567/ -- --treenode-filter "/*/*/AgentTaskLandStageOutcomeTests/(C508_RebasedSiblingMarkerMatrix*)|(unreadable_push_endpoint_refuses_before_any_stage*)" --report-trx --report-trx-filename so.trx --results-directory .antiphon/c567/so
# collateral for the production edit (S3) and the reason-code contract (G-4b); class-wide, separate windows
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c567/ -- --treenode-filter "/*/Antiphon.Tests.Application/(AgentTaskLandStageOutcomeTests*)|(AgentTaskWorktreeLockOutcomeTests*)|(WorktreeCleanupPresentationTests*)/*" --report-trx --report-trx-filename collateral.trx --results-directory .antiphon/c567/collateral
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c567/ -- --treenode-filter "/*/Antiphon.Tests.Application/(AgentTaskLandRefusedRetryTests*)/*" --report-trx --report-trx-filename rr-class.trx --results-directory .antiphon/c567/rr-class
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c567/ -- --treenode-filter "/*/Antiphon.Tests.Infrastructure/(LandingGitTests*)/*" --report-trx --report-trx-filename git.trx --results-directory .antiphon/c567/git
```

Read executed method names and nonzero counters from each fresh TRX; exit 0 with zero tests is
a failed filter (native exit 8). Use a distinct `--results-directory` per invocation (the HTML
report name is fixed). Do not co-schedule with `Antiphon.Agents.Pty.Tests`. Expect `C448_V05`
rows at 25–75 s each and the whole `AgentTaskLandRefusedRetryTests` class at ~17 min when
other worktrees' suites are running on the host (observed 2026-09-19: three concurrent
`Antiphon.Tests.exe`); `AgentTaskLandPublicationTests` class-wide is likewise not a
single-window run and is not required by this card.

### Positive controls (Mutation stage; method-scoped, red then green, restore after each)

| PC | Mutation (single site) | Method filter | Expected red |
|---|---|---|---|
| PC-1 (G-1a) | `LandingGit.ObserveAsync`: on `!read.Succeeded`, instead of `remote_read_failed` return containment computed from `refs/remotes/origin/<target>` (`CommitAsync` on that ref, then `merge-base --is-ancestor sourceSha <that sha>`, `new(sha, exit==0, null)`). | `/*/*/AgentTaskLandPublicationTests/C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(read-error)` and `(missing)` | `RemoteConfirmedAt.ShouldBeNull()` fails (AlreadyPresent via the poisoned tracking ref) and `Directory.Exists(Source)` is false |
| PC-2 (G-1b) | `AgentTaskLandSourceResolver.ResolveAsync`: skip `ObserveSourceAsync` and set `observed` to a fabricated accepted observation with `Sha = expected`. | same two rows | `Trace.ShouldContain(source ls-remote)` fails; `SourceRemoteSha` is not `SeedSha` |
| PC-3 (G-2b) | `AgentTaskLandService.RunRequestAsync`: change the gate's reason literal to `"legacy_binding"`. | `/*/*/AgentTaskLandRefusedRetryTests/RR_V5_OriginalPendingRequestCannotReplaceRefusal` | `Detail.ShouldContain("legacy_review_binding_required")` fails |
| PC-4 (G-2b) | `AgentTaskLandService.CompleteTerminalLockedAsync`: drop the `CompleteRequest(task, request, terminal)` call. | same method | `legacy.IsPending.ShouldBeFalse()` fails (`TerminalEventId` null) |
| PC-5 (G-3a/b) | `AgentTaskLandService.UnlandedMarker`: join with `";"`. | `/*/*/AgentTaskLandStageOutcomeTests/(C508_RebasedSiblingMarkerMatrix_PinnedVerifiedShaDecides*)|(C508_RebasedSiblingMarkerMatrix_MixedSiblingKeepsMarker*)` | component `pinned.Marker` fails; mixed row `landed.Detail.ShouldContain(...)` fails |
| PC-6 (G-4a, CARD-0488 G-33) | `LandingGit.EndpointAsync`: drop `--push` (read the fetch URL). | `/*/*/AgentTaskLandStageOutcomeTests/unreadable_push_endpoint_refuses_before_any_stage` | source observation succeeds on the fetch URL; stage rows exist (`rows.ShouldBeEmpty` fails) and the reason is not `source_remote_unreadable` |

Why no PC targets "the legacy row creates B": that requires defeating the service gate, the
resolver's schema gate and the null-`expected` mismatch together — three sites — so it is not
a valid single-site control; PC-3/PC-4 cover the gate's contract and settlement instead. The
2026-09-09 plan's PC-2 (remove `explicitRequest &&` in `CanReplaceRefused`) is void under
current production for the reason in "Not done, noted".

## Risks

- G-1's `merge-base` discriminator depends on the resolver never issuing a target `ls-remote`
  before its ancestry classification. It does not today (the target is read only by
  `LandingGit.ObserveAsync`, called only from the protocol). If a future change reads the
  target in the resolver, the `ancestry-error` row would fault the resolver's `merge-base` and
  fail at "operation should not be null" — a loud, correct signal that the discriminator needs
  updating, not a silent pass.
- S3 makes a private method internal. Nothing else in the server may start calling it;
  Review should check the diff is limited to the visibility keyword, the doc comment and the
  extracted formatter (the prototype diff is the reference).
- Group 2's guard reads the reason literal `legacy_review_binding_required`, which
  `delegate.ps1 -Status` and `docs/orchestration-loop.md` also surface; a rename there is a
  contract change and should fail this test.
- The C508 rows are land-heavy and host-sensitive (1m49s under load for the all-minus row).
  180 s per row leaves headroom on this host; if a future host is slower still, the split
  makes the slow row nameable instead of hiding it behind one timeout.

## Not done, noted

- The protocol's Refused branch (`AgentTaskLandingProtocol.RunAsync`, `explicitRequest` /
  `CanReplaceRefused`) is unreachable with `explicitRequest == false` through
  `AgentTaskLandService`: after CARD-0467 a refused A's request is completed atomically, and
  after CARD-0488 any request that reaches the protocol beside a refused A is a distinct row
  carrying a binding, which the resolver turns into B before the protocol runs. The branch is
  dead defensive code and PC-2 of the 2026-09-09 plan can no longer go red. Worth a small
  follow-up card (simplify, or add a direct protocol-level test); not changed here.
- `RR_V5_EqualPendingTimestampCannotReplaceRefusal` is now behaviourally identical to its
  sibling (D-5). Fold or delete it in the same follow-up.
- `0fdc1392` (CARD-0443) shipped the collector signature change while its own message
  recorded 14 known baseline failures; the reflection seam is the reason the break did not
  surface at build time. D-3 removes that class of seam for this method only; other
  reflection-based test seams were not audited.
- The bare-`|` treenode filter form silently widening to a whole class (or to nothing) is a
  runner pitfall worth its own line in the testing doc (S5); it cost one 17-minute run here.

## Evidence appendix

Host: this worktree, `1044eef7`, isolated build `bin-c7b07802/` (deleted after the runs),
Postgres testcontainer per invocation, two foreign `Antiphon.Tests.exe` suites running
concurrently throughout (`bin-r508` since 07:44, `bin-r534` since 11:43).

### Reproduction (unmodified source)

| Invocation | Result | Detail |
|---|---|---|
| `C448_V05_*` (8 rows) | 8 failed / 0 passed, 2m31s | every row: `await h.OperationAsync() should not be null but was` (line 169); rows 10–65 s |
| `AgentTaskLandRefusedRetryTests` **whole class** (the bare-`|` filter widened; 14 tests, ~17 min) | 2 failed / 12 passed | `RR_V5_OriginalPending…` 55 s and `RR_V5_EqualPending…` 56 s: `s.Trace should contain an element satisfying (IsSourceStatus && Succeeded && Output.Length == 0) but does not`; all other twelve green, incl. `RR_V4_RemoteReadFailureRetriesUnchangedSource` (2m16s) |
| `(C508…)|(unreadable…)` | 2 failed, 3m46s | `unreadable_push_endpoint…` 43 s: `refused.Detail should contain "remote_read_failed" but was actually "land refused: source_remote_unreadable expected=192a05… local=192a0…"`; `C508…` **timed out at 00:03:00** (never reached the cast on this host) |
| bare-`|` two-path filter for the stage-outcome pair | zero tests ran (exit 0, "Zero tests ran") | runner pitfall, see S5 |

### Prototype (the committed `.diff` applied to `1044eef7`)

Build: 0 errors. Runs:

| Invocation | Result | Per-test |
|---|---|---|
| `C448_V05_*` | 8/8 passed, 4m34s | fetch-error 26 s, timeout 27 s, canceled 27 s, ancestry-error 29 s, missing 29 s, read-error 30 s, empty 31 s, malformed 1m14s |
| `RR_V5_(Original…)|(Equal…)` | 2/2 passed, 1m54s | Original 37 s, Equal 1m16s |
| `(C508…)|(unreadable…)` before D-7 | 1/2: `unreadable…` passed (51 s); `C508…` timed out at 3:00 | motivated D-7 |
| `(C508_RebasedSiblingMarkerMatrix*)` after D-7 | 3/3 passed, 3m04s | AllMinus 1m48s, Mixed 1m11s, Pinned 3 s |

The prototype was reverted from the source tree before this plan was committed; only the
plan and the `.diff` are on the branch. `git apply` of the `.diff` on `1044eef7` reproduces
the exact tree that produced the table above.
