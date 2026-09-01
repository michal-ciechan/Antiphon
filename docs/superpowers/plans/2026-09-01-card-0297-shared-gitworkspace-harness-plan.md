# CARD-0297 — Register `GitWorkspaceService` once, in a shared test helper, not per-harness

**Date:** 2026-09-01 (Plan pass, task fde7efb2 — design only; no code changed)
**Card:** CARD-0297 "`PinnedAgentKindTests.T3` red — `CreateDispatchHarness` missing `GitWorkspaceService`"
**Diagnosis:** done on the card (pre-existing at `d4d904b0`, introduced when `DelegationWorktreeService` grew the ctor dep). This pass census'd every copy of the same miss.

**Sources (verified this pass):** CARD-0297, `PinnedAgentKindTests.CreateDispatchHarness` (`:240-282`, no `GitWorkspaceService`), `DelegationWorktreeService` ctor (`GitWorkspaceService` required since the `-Land` / deliverable work), `Program.cs:445-447` (production registers `GitProcessGate` then `GitWorkspaceService` singleton), CARD-0230 investigation (`docs/superpowers/plans/2026-08-30-card-0230-grok-delegate-e2e-settlement-investigation.md` §5.3 already named this class of miss), one-liners in `DelegationScopeHoldTests:325`, `DelegateLaunchArgvIntegrityTests:519`, and ~25 other hand-built `ServiceCollection`s.

---

## Decision

Do **not** add another one-liner to `PinnedAgentKindTests`. That is the workaround three other executes already shipped today (`DelegationScopeHoldTests`, `DelegateLaunchArgvIntegrityTests`, plus the CARD-0230-era copies). The next `CreateDispatchHarness` clone will miss it again.

There is **no** shared dispatch-harness builder today. `BridgeQueueHarness` is one consumer, not the funnel. Every dispatcher suite copies a ~40-line `ServiceCollection`. The real fix is a **TestHelpers extension that is the only legal way to register the worktree graph**, using `TryAdd*` so existing one-liners are not duplicates.

Use the **real** `GitWorkspaceService`, not a fake. Constructor is `(ILogger, GitProcessGate? = null, IOptions<GitSettings>? = null)` — logging is already on every harness, the rest is optional. Production is the same singleton. These tests do not hit a git subprocess unless they take a Worktree/deliverable arm.

Do **not** change `SettleAsync` to `GetService` / never-throw (CARD-0230 §5.2 option A). Harness drift stays the defect; production stays honest about the dependency.

---

## Ground truth

### Why T3 dies

`CreateDispatchHarness` registers `DelegationWorktreeService` as scoped (`PinnedAgentKindTests.cs:277`). That type's constructor requires `GitWorkspaceService` (`DelegationWorktreeService.cs:25-29`). `GetRequiredService<AgentTaskDispatcher>()` builds the dispatcher, which builds the worktree service, which throws:

`InvalidOperationException: No service for type 'GitWorkspaceService' has been registered.`

T1/T2/T4 never construct a dispatcher. T3 does (`:104`). Same gap at base `d4d904b0`; not CARD-0291.

### The copy-paste is the product

`AddScoped<DelegationWorktreeService>()` appears in ~25 test files. About a third already have `services.AddSingleton<GitWorkspaceService>();` with a CARD-0230 comment. The rest do not. Confirmed **still missing** (will throw at dispatcher resolve):

| File | Harness |
|---|---|
| `PinnedAgentKindTests.cs` | `CreateDispatchHarness` — the card |
| `GrokDelegateDispatchTests.cs` | `BuildHarness` |
| `SubscriptionQuotaGateTests.cs` | `CreateDispatchHarness` |
| `PinnedCodexProfileDispatchLaunchTests.cs` | `ConfigureServices` (also has no `IWorktreeManager`) |
| `AgentTaskReuseEnqueueTests.cs` | `CreateHarness` |
| `AgentTaskStallEscalationTests.cs` | dispatcher builder |
| `AgentTaskCheckScheduleTests.cs` | dispatcher builder |
| `TaskProgressStallSweepTests.cs` | dispatcher builder |

Already patched with the one-liner (keep green; switch to the helper): `DelegationScopeHoldTests`, `DelegateLaunchArgvIntegrityTests`, `CodexDelegateDispatchTests`, `DelegateBundleLaunchTests`, `LaunchEnvLayersIntegrationTests`, `AgentTaskStandingAgentDispatchTests`, `AgentTaskDispatchFailureTests`, `AgentTaskPoolTests`, `AgentTaskOverdueDeadlineTests`, `AgentTaskDeliveryWatchdogTests`, `AgentTaskDeadSessionReconciliationTests`, `GrokDelegateEndToEndTests`, `AgentTaskCheckSweepTests`, `AgentTaskCheckInterpreterTests`, `PinnedProfileLaunchSpecTests`, `AgentTaskReplyIntegrationTests`, `BridgeQueueHarness`, and the rest of the first `AddSingleton<GitWorkspaceService>` grep.

The brief named `AgentServiceIntegrationTests` (CARD-0255). That file does **not** register `DelegationWorktreeService` or `GitWorkspaceService`. Leave it unless execute-time census shows a different DI miss.

`new DelegationWorktreeService(...)` call sites already pass the dep in the ctor — out of scope.

---

## Slices

### S1 — Shared helper

**New** `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`:

```csharp
public static class DelegationTestServices
{
    /// <summary>
    /// Production worktree graph for hand-built test ServiceCollections.
    /// GitWorkspaceService is required since DelegationWorktreeService gained it (ae596005 / c4d7e0d).
    /// TryAdd so a harness that already one-lined the registration is not a duplicate.
    /// </summary>
    public static IServiceCollection AddDelegationWorktreeGraph(
        this IServiceCollection services,
        GitSettings? gitSettings = null)
    {
        if (gitSettings is not null)
            services.TryAddSingleton(Options.Create(gitSettings));
        else
            services.TryAddSingleton(Options.Create(new GitSettings()));

        services.TryAddSingleton<IWorktreeManager, WorktreeManager>();
        services.TryAddSingleton<IGitService, GitService>();
        services.TryAddSingleton<GitWorkspaceService>();
        services.TryAddScoped<DelegationWorktreeService>();
        return services;
    }
}
```

- `TryAddSingleton<GitWorkspaceService>()` is the load-bearing line. The rest is the trio every dispatcher harness already meant to register together, so a future ctor dep on `IGitService` does not spawn a CARD-0298.
- Do **not** register `GitProcessGate` unless a test needs the concurrency cap; the service's ctor already falls back to `SharedGate`.
- Do not pull `AgentTaskDispatcher` / `AgentTaskService` into this helper. Those graphs differ on purpose (stopper, definitions, caps) — CARD-0230 §5.3.

### S2 — Point every DI worktree registration at the helper

Replace this block wherever it appears (with or without the GitWorkspace one-liner):

```
services.AddSingleton(Options.Create(new GitSettings { WorktreeBasePath = ... }));
services.AddSingleton<IWorktreeManager, WorktreeManager>();
services.AddSingleton<IGitService, GitService>();
// optional: services.AddSingleton<GitWorkspaceService>();
services.AddScoped<DelegationWorktreeService>();
```

with:

```
services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = ... });
```

That includes `PinnedAgentKindTests` (the card), every **missing** file in the table above, and every **already-patched** file so the one-liner does not remain a second source of truth.

Harnesses that register `GitWorkspaceService` for `AgentTaskReplyService` / `DelegateBindRefusalRecovery` **without** `DelegationWorktreeService` still call `AddDelegationWorktreeGraph` or, if they truly have no worktree graph, `TryAddSingleton<GitWorkspaceService>()` via a one-line `AddGitWorkspaceService()` on the same class. Prefer one method: `AddDelegationWorktreeGraph` is enough if they already have worktree types; for reply-only, add:

```csharp
public static IServiceCollection AddGitWorkspaceService(this IServiceCollection services)
{
    services.TryAddSingleton<GitWorkspaceService>();
    return services;
}
```

and have `AddDelegationWorktreeGraph` call it.

`BridgeQueueHarness` switches to the helper too (it is the most-copied "shared" harness and still one-lines today).

### S3 — Pins

- `PinnedAgentKindTests.T3` green (the card).
- New `DelegationTestServicesTests`: a `ServiceCollection` with `AddLogging()` + `AddDelegationWorktreeGraph()` can `GetRequiredService<DelegationWorktreeService>()` and `GetRequiredService<GitWorkspaceService>()`. That is the contract future harnesses lean on.
- No headed tests. No production code.

### S4 — Docs

One gotcha in `docs/testing-and-build.md` (CARD-0254 preserved section or a short new one): a hand-built `ServiceCollection` that resolves `AgentTaskDispatcher` / `DelegationWorktreeService` / `AgentTaskReplyService` must call `AddDelegationWorktreeGraph` (or `AddGitWorkspaceService`). Do not add `AddSingleton<GitWorkspaceService>()` next to a local `CreateDispatchHarness`. CARD-0230/0297 are the evidence.

---

## What this card does not do

- Unifying all dispatcher `ServiceCollection`s into one mega-harness.
- Making `SettleAsync` swallow a missing `GitWorkspaceService`.
- A fake `GitWorkspaceService`.
- Fixing other swallowed DI misses (CARD-0230 §5.1 `AgentSupervisorService` for `DeliveryUnverified`).
- Touching `AgentServiceIntegrationTests` unless execute-time proves it is in this graph.

---

## Test matrix

| Layer | Test |
|---|---|
| Application | `PinnedAgentKindTests` 4/4, especially T3 |
| Application | Each previously-missing file's existing dispatcher tests (Grok dispatch, quota gate, reuse enqueue, stall, check schedule, progress stall, pinned Codex launch) |
| Unit | `DelegationTestServicesTests` can resolve the worktree graph from logging + helper only |
| Regression | One already-patched file (`DelegationScopeHoldTests` or `DelegateLaunchArgvIntegrityTests`) still green after switching to the helper |

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0297/ -- --treenode-filter "/*/*/PinnedAgentKindTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0297/ -- --treenode-filter "/*/*/DelegationTestServicesTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0297/ -- --treenode-filter "/*/*/DelegationScopeHoldTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0297/ -- --treenode-filter "/*/*/GrokDelegateDispatchTests/*"
```

Forward slash on OutputPath. Delete `bin-card0297*` after. Do not co-schedule Pty tests.

---

## Sequencing and risks

**Order: S1 helper + S3 pin test, then S2 migrate `PinnedAgentKindTests` and the missing table, then the already-patched copies, then S4.** Each migrated file should still construct `AgentTaskDispatcher`.

| Risk | Disposition |
|---|---|
| `TryAddSingleton` after a harness already `AddSingleton<IWorktreeManager, Fake>` | TryAdd no-ops; fake wins. Good. |
| Harness wants a different `GitSettings.WorktreeBasePath` | Pass it into the helper; if they `AddSingleton(Options.Create(git))` first, TryAdd on Options might still add a second `IOptions<GitSettings>`. **Rule:** the helper is the only `GitSettings` registration when used; delete the prior `AddSingleton(Options.Create(new GitSettings…))`. If a test must override later, `AddSingleton` before the helper, and the helper should `TryAddSingleton<IOptions<GitSettings>>` only when `gitSettings is not null` OR when none is registered — implement with `services.Any(d => d.ServiceType == typeof(IOptions<GitSettings>))` or just document "call helper instead of AddSingleton GitSettings". Simplest: helper always `TryAddSingleton(Options.Create(gitSettings ?? new GitSettings()))`. Callers drop their own Options line. |
| Double `AddScoped<DelegationWorktreeService>` | TryAddScoped skips the second. Callers drop their AddScoped line. |
| PinnedCodex missing `IWorktreeManager` | Helper supplies it. |
| Helper pulls git into a test that never wanted it | Only suites that already registered `DelegationWorktreeService`. |

---

## Execution notes

- Re-run the missing-file census (`AddScoped<DelegationWorktreeService>` vs `GitWorkspaceService`) at execute time; the list above is 2026-09-01.
- Do not leave any `AddSingleton<GitWorkspaceService>()` next to a dispatcher builder once the helper exists.
