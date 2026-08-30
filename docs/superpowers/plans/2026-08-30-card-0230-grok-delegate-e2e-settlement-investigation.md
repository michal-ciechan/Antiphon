# CARD-0230 — `GrokDelegateEndToEndTests` stuck at `Dispatched`: a deterministic harness miss, not a flake

**Date:** 2026-08-30 · **Task:** c662b5df (Plan / Frontier) · **Verdict:** real, deterministic
regression in the TEST HARNESS, introduced by `c4d7e0d` (2026-08-26). Not load-sensitive, not
CARD-0238's Postgres exhaustion. Production is unaffected. Fixed in the same commit as this doc
(one DI registration in the harness); verified 5/5 green in isolation, 0/1 green before the fix.

## 1. Outcome in one paragraph

`AgentTaskReplyService.SettleAsync` has called
`services.GetRequiredService<GitWorkspaceService>()` (via `ResolveDeliverableAsync`,
`server/Application/Services/AgentTaskReplyService.cs:1663`) since commit `c4d7e0d`
"feat(review): surface unread delegated deliverables". That commit registered
`GitWorkspaceService` in the three harnesses it touched (`AgentTaskReplyIntegrationTests`,
`CardThreadServiceTests`, `PlanCatalogServiceTests`) and in `Program.cs:417`, but
`GrokDelegateEndToEndTests.BuildHarness` builds its own `ServiceCollection` by hand and was not
updated. The throw happens at `SettleAsync` line 419, *before* `db.SaveChangesAsync`, so the task
row is never touched; `OnTurnEndAsync`'s catch-all (`:149-153`) logs it as a Warning and returns;
the harness registered `AddLogging()` with **no sink**, so the Warning went nowhere. Both tests
then read the row and found `Dispatched`. The card's "concurrent load" theory was a reasonable
guess from the symptom, but the failure reproduces identically on a quiet machine.

## 2. Evidence

### 2.1 Isolation runs (quiet machine, `bin-c0230/` build of master `16f849b`)

| Run | Harness | Grok capstone | Claude control | Notes |
|---|---|---|---|---|
| 1 | as on master | **FAIL** 1m15s | **FAIL** 42s | identical shape to the card: `settled.Status … Succeeded but was Dispatched` at `:403`; Grok side times out in `WaitUntilAsync` (`:575`, called from `:253`) |
| 2 | + `AddSingleton<GitWorkspaceService>()` | pass 12s | pass 42s | |
| 3 | same | pass 21s | pass 44s | |
| 4 | same | pass | pass | |
| 5 | same | pass | pass | |
| 6 | same | pass | pass | |

Nothing else was running on the machine during these runs (no other delegates, no full-suite
run, no AppHost restart). Logs: `.antiphon/c0230-run{1..6}.log`,
`.antiphon/c0230-claude-logged.log` (run 1 of the Claude test with a Debug console sink).

### 2.2 The swallowed exception, verbatim (Claude control test, Debug sink)

```
warn: Antiphon.Server.Application.Services.AgentTaskReplyService[0]
      Failed to settle a delegated task for session d79271dd-7d1b-42c5-9356-cc7749cb0297
      System.InvalidOperationException: No service for type 'Antiphon.Server.Application.Services.GitWorkspaceService' has been registered.
         at Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(IServiceProvider provider, Type serviceType)
         at Antiphon.Server.Application.Services.AgentTaskReplyService.ResolveDeliverableAsync(...) in AgentTaskReplyService.cs:line 1663
         at Antiphon.Server.Application.Services.AgentTaskReplyService.SettleAsync(...) in AgentTaskReplyService.cs:line 419
         at Antiphon.Server.Application.Services.AgentTaskReplyService.OnTurnEndAsync(Guid sessionId, CancellationToken ct) in AgentTaskReplyService.cs:line 149
```

The Grok capstone fails by the same path: its transcript pump persists the real `TurnEnd`
(stage 5 of the test passes — the screen dump in the failure shows `FAKE response to: …` and
`Worked for 1.7s`), the runtime's turn-boundary flush calls `OnTurnEndAsync`, and the same throw
is swallowed; the test's 60 s `WaitUntilAsync` at `:253` then expires.

### 2.3 Why it is `c4d7e0d` and nothing later

- `git show c4d7e0d^:server/Application/Services/AgentTaskReplyService.cs | grep -c GitWorkspaceService` → `0`
- `git show c4d7e0d^:tests/…/GrokDelegateEndToEndTests.cs | grep -c GitWorkspaceService` → `0`
- `git log -S 'GetRequiredService<GitWorkspaceService>' -- server/Application/Services/AgentTaskReplyService.cs` → only `c4d7e0d`
- `c4d7e0d`'s diff to `AgentTaskReplyIntegrationTests.cs` line 76: `+ services.AddSingleton<GitWorkspaceService>();` — the author fixed the harness they were looking at.

So the two tests have been red on every run since 2026-08-26 17:35 +0100. The 2026-08-29 finding
(red on both the CARD-0220 branch and on master, under load) is consistent with that: both trees
contained `c4d7e0d`, and load was a coincidence.

### 2.4 Line numbers in the card

`403` is the one assertion (`settled.Status.ShouldBe(AgentTaskStatus.Succeeded)`); `416` and
`423` are the async state machine's re-throw frames of the same failure (the `finally` /
`CleanupAsync` boundaries), not three separate assertions. Unchanged on today's master.

## 3. Relationship to CARD-0238 (Postgres connection exhaustion)

**Different mechanism; do not close CARD-0230 as a duplicate.** CARD-0238's failures are fast
`53300 too many clients` / `53200 out of shared memory` errors thrown *into* the test; this
failure is a swallowed DI exception that leaves a row unchanged, with no database error anywhere.
Neither of these two tests calls `CreateIsolatedSchemaAsync`, and run 1 above reproduced the
failure with the whole container to itself. CARD-0238's own text ("likely also explains …
GrokDelegateEndToEndTests if those tests happen to also hit CreateIsolatedSchemaAsync under
load") can be answered: they do not, and it does not.

## 4. What was changed (test code only)

`tests/Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs`, `BuildHarness`:

1. `services.AddSingleton<GitWorkspaceService>();` — the registration `c4d7e0d` added to the other
   harnesses. Its constructor is `(ILogger<GitWorkspaceService>, GitProcessGate? = null,
   IOptions<GitSettings>? = null)`; the harness already registers logging and `GitSettings`, so
   nothing else is needed. Safe because it is exactly what production (`Program.cs:417`) and the
   sibling harnesses already do, and the service is only *consulted* on settlement (a
   `docs/**/*.md` path in the report, then a branch lookup only for Worktree tasks — neither test
   produces one, so the resolved deliverable is `(null, null)` and no git process is spawned).
2. `services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));` in place of the
   sink-less `AddLogging()`. TUnit captures the console per test, so the next swallowed settlement
   exception appears under the failing test's "Standard output" instead of in nothing. At Warning
   this is ~6 lines per test (three EF model-validation warnings that every harness emits, plus
   the delivery lines in §5.1); at Debug it was 19 000 lines, which is why Debug is not kept.

No production code was changed.

## 5. Secondary findings (not fixed here — the caller's call)

### 5.1 The same harness also cannot record `DeliveryUnverified`

With the sink on, every Claude-control run also prints:

```
fail: Antiphon.Server.Application.Services.SessionMessageQueueService[0]
      Failed to record DeliveryUnverified for session …
      System.InvalidOperationException: No service for type '…AgentSupervisorService' has been registered.
         at …SessionMessageQueueService.RecordDeliveryUnverifiedAsync(...) in SessionMessageQueueService.cs:line 1285
```

This is caught inside `RecordDeliveryUnverifiedAsync` (its own try/catch), so it costs the test
nothing but the incident row. It fires because the Claude control seeds its transcript *after*
delivery, so delivery is confirmed via the CARD-0164 degraded screen-only verdict and then tries
to record the incident. `AgentSupervisorService` takes `AppDbContext, AgentControlService,
ISessionRunnerClient, IEventBus, IAlertService, IOptions<SupervisionSettings>, TimeProvider,
ILogger` — registering it here would pull in `AgentControlService`'s graph, which is a bigger
harness change than the card warrants for a caught, harness-only loss. Left as-is, documented.

### 5.2 Design observation: one `GetRequiredService` in the settle path can strand a task silently

`SettleAsync` is explicit that observability must not be able to break settlement —
`RecordScopeDriftAsync` (`:531`) "never throws", and its doc comment says why. The deliverable
pointer extraction added by `c4d7e0d` sits in the same position (`:419`, before
`SaveChangesAsync`) but is written to throw: `GetRequiredService` on a service the method only
needs for the Worktree-branch arm, and no try/catch. In production `GitWorkspaceService` is
registered, so today this cannot fire there; the general shape — any exception between the top
of `SettleAsync` and `SaveChangesAsync` becomes "task stays Dispatched until the delivery
watchdog notices, with one Warning line as the only trace" — is what turned a one-line DI miss
into four days of red that read as a timing flake.

Options, with tradeoffs, in no presupposed order:

- **A. Make `ResolveDeliverableAsync` never-throw** (`GetService` + null-skip the branch arm, or
  a try/catch that returns `(null, null)` and logs). Cheapest; consistent with the scope-drift
  precedent; the cost is that a genuinely broken git lookup degrades to "no deliverable
  recorded" rather than being loud — which is the same trade the scope-drift code already made
  on purpose.
- **B. Leave the settle path as-is and treat harness drift as the defect** (see §5.3). Keeps the
  production code honest about its dependencies; leaves the silent-strand shape in place for the
  next hand-built harness.
- **C. Make `OnTurnEndAsync`'s catch-all louder** — e.g. raise an incident on the task/agent when
  a settle throws, the way `RecordUncorrelatedReportAsync` does for the uncorrelated case.
  Broader than this card; it changes production behaviour on an error path that has never been
  observed in production, and CARD-0003 already made the *uncorrelated* branch visible for
  exactly this reason, so there is precedent either way.

### 5.3 Nine hand-built harnesses register `AgentTaskReplyService`

`grep -l 'AddSingleton<AgentTaskReplyService>'` finds nine test files that assemble the settle
graph by hand (`AgentTaskCheckInterpreterTests`, `AgentTaskCheckSweepTests`,
`AgentTaskDeadSessionReconciliationTests`, `AgentTaskDeliveryWatchdogTests`,
`AgentTaskOverdueDeadlineTests`, `AgentTaskPoolTests`, `AgentTaskStandingAgentDispatchTests`,
`GrokDelegateEndToEndTests`, `TaskProgressStallSweepTests`). Seven of them now register
`GitWorkspaceService` (checked 2026-08-30). The two that do not — `AgentTaskPoolTests` and
`TaskProgressStallSweepTests` — never call `OnTurnEndAsync` (grep, 0 hits in each; the pool
tests' `Succeeded` rows are seeded, not settled), so they cannot reach the deliverable step and
are not at risk today. A shared "settle graph" registration helper would close the class
of miss; the tradeoff is that these harnesses differ on purpose (fakes vs real services per
seam), so a helper has to take the same seams as parameters or it flattens those choices.

## 6. Recommendation

- Close CARD-0230 as **fixed** by the harness registration in this commit; it is not a duplicate
  of CARD-0238 and needs no timeout or retry change.
- Decide §5.2 separately (a one-line card if option A is wanted; nothing if B). §5.1 and §5.3 are
  notes for whoever next touches these harnesses, not open work.
