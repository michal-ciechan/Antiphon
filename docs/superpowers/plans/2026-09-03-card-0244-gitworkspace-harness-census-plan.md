# CARD-0244 — Census the GitWorkspaceService miss; do not rebuild the helper

**Date:** 2026-09-03 (Plan pass, task 0408b113 — design only; no code changed)
**Card:** CARD-0244 "Audit: test harnesses keep forgetting GitWorkspaceService (3rd occurrence tonight)"
**Diagnosis:** the class of miss is real; option 1 already shipped as CARD-0297; this card is the backstop CARD-0297 omitted, plus two leftover harnesses that prove it.

**Sources (verified this pass):** CARD-0244, CARD-0297 (`cee6c79c`, Review), CARD-0230 investigation §5.2–5.3, `DelegationTestServices.cs`, `DelegationTestServicesTests.cs`, `DelegationWorktreeService` ctor (`GitWorkspaceService` required), `AgentTaskReplyService.ResolveDeliverableAsync` (CARD-0230 never-abort try/catch at `:2024-2064`), `docs/testing-and-build.md` CARD-0297 paragraph, 32 `AddScoped<AgentTaskDispatcher>` test files, 1 remaining `AddScoped<DelegationWorktreeService>` in tests (`SessionMessageQueueBootWedgeTests.RegisterDispatcher`, added by CARD-0299 *after* the helper), `AgentTaskCatchUpSettlementTests.RuntimeFor` (settles via `AgentTaskReplyService` with no git registration).

---

## Decision

**Combine option 1 (already shipped) with option 2 as a source census. Do not spin up harnesses. Do not re-implement the helper. Do not unify dispatcher `ServiceCollection`s.**

The card offered three options. Current tree:

| Option | Status | This card |
|---|---|---|
| Shared `ServiceCollection` helper | Shipped CARD-0297 `cee6c79c`: `AddDelegationWorktreeGraph` / `AddGitWorkspaceService` in `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`. 31 of 32 dispatcher harnesses already call it. Docs already pin it. | Consume. Do not redo. |
| Startup self-check of every hand-rolled harness | Missing. `DelegationTestServicesTests` only constructs the helper itself. | **This card.** Source census, not a runtime registry. |
| Comment at `ResolveDeliverableAsync` | Shipped CARD-0230 (`:2024-2028`). Production swallows a missing `GitWorkspaceService` so settlement cannot strand a task. | Leave. |

The helper was the right root fix and **is feasible** — CARD-0297 already proved it. Harnesses vary on purpose (stopper, registry definitions, runner client, caps, `BridgeQueueHarness` vs a standalone `ServiceCollection`). `TryAdd*` is why a fake `IWorktreeManager` (`BridgeQueueHarness.NoWorktreeManager`) survives the helper. Pulling `AgentTaskDispatcher` / `AgentTaskReplyService` into one mega-builder would flatten those seams; CARD-0230 §5.3 and CARD-0297 already rejected that. Do not reopen it.

The helper **alone is not enough**. CARD-0299 (`78629298`, after `cee6c79c`) added `SessionMessageQueueBootWedgeTests.RegisterDispatcher` and hand-rolled `AddScoped<DelegationWorktreeService>()` again. That is the fourth occurrence the original card predicted. A comment at the production call site cannot catch a new test file.

Option 2 as written — "spin up every known hand-rolled `ServiceCollection` harness" — is **not** feasible:

- 32 private `CreateDispatchHarness` / `BuildHarness` / `RegisterDispatcher` methods, no common interface.
- They need different fakes, a `TestDbFixture`, temp dirs, or `BridgeQueueHarness.CreateAsync`.
- A registry of factory delegates would itself drift, which is the original bug.

A **source census** (the `AppHostBrokerSourceGuardTests` shape) names the file, needs no DI, and would have failed CARD-0299's `RegisterDispatcher` the night it landed.

---

## Ground truth (2026-09-03)

Two failure modes, same missing registration:

1. **Loud.** `DelegationWorktreeService` ctor requires `GitWorkspaceService`. `GetRequiredService<AgentTaskDispatcher>()` throws `No service for type 'GitWorkspaceService'`. This is CARD-0297 / `PinnedAgentKindTests.T3`.
2. **Silent.** `SettleAsync` → `ResolveDeliverableAsync` → `GetRequiredService<GitWorkspaceService>()`. CARD-0230 wrapped that in try/catch so production settlement cannot abort before `SaveChangesAsync`. A harness that registers `AgentTaskReplyService` without git then looks like it "just returns nothing" for the deliverable pointer — the original CARD-0244 symptom.

Helper coverage now:

- 31 dispatcher files call `AddDelegationWorktreeGraph`. The 32nd is `SessionMessageQueueBootWedgeTests`.
- Reply-only / card-service files call `AddGitWorkspaceService` (or inherit it from `BridgeQueueHarness`, which already does).
- Production `Program.cs` is out of scope.

Two leftovers, both still green today (so incidental discovery will miss them again):

| File | Why it is still a miss | Why it is green today |
|---|---|---|
| `SessionMessageQueueBootWedgeTests.RegisterDispatcher` (`:226-239`) | Hand-rolls `GitSettings` + `IGitService` + `AddScoped<DelegationWorktreeService>()` instead of the helper. Copied after CARD-0297 existed. | `BridgeQueueHarness` already called `AddGitWorkspaceService()`, so `DelegationWorktreeService` constructs. A clone of `RegisterDispatcher` as a standalone harness would throw. |
| `AgentTaskCatchUpSettlementTests.RuntimeFor` (`:224-254`) | `AddSingleton<AgentTaskReplyService>()` with no git helper and no `BridgeQueueHarness`. CARD-0288, same day as CARD-0297, never migrated. | `ResolveDeliverableAsync` swallows the miss. Tests assert `Succeeded` / `Marked`, not a deliverable path. |

`new AgentTaskReplyService(...)` call sites (`AgentTaskReplyIntegrationTests`, one watchdog helper) already pass through a collection that has the helper. Out of census scope.

---

## Slices

### S1 — Source census

New `tests/Antiphon.Tests/TestHelpers/DelegationHarnessCensusTests.cs`, `[Category("Unit")]`. Walk `tests/Antiphon.Tests/**/*.cs`. Fail with the relative path in the message. Repo-root walk is the `AppHostBrokerSourceGuardTests` `Antiphon.sln` parent loop.

Rules (string contains is enough; do not strip comments):

| Rule | Match | Passes when | Why |
|---|---|---|---|
| A | `AddScoped<DelegationWorktreeService>` | never, in this tree | Helper uses `TryAddScoped`. The one live hit is the boot-wedge leftover. |
| B | `AddScoped<AgentTaskDispatcher>` | file also contains `AddDelegationWorktreeGraph` | Every dispatcher harness. |
| C | `AddSingleton<AgentTaskReplyService>` | file contains `AddDelegationWorktreeGraph` **or** `AddGitWorkspaceService` **or** `BridgeQueueHarness` | Reply-only files inherit git from `BridgeQueueHarness`; catch-up does not. |
| D | `AddSingleton<GitWorkspaceService>` | only `DelegationTestServices.cs` and `DelegationTestServicesTests.cs` | Blocks the CARD-0230 one-liner from coming back. **Substring trap:** `TryAddSingleton<GitWorkspaceService>` contains `AddSingleton<GitWorkspaceService>`. Use a negative lookbehind `(?<!Try)AddSingleton<GitWorkspaceService>` or allowlist the helper file. The TryAdd-proof test in `DelegationTestServicesTests` is an intentional one-liner and stays allowlisted. |

Do not scan `server/`, docs, or other test projects. Do not build a list of factory methods.

S1 will be red on HEAD until S2. That is the point: land census and the two fixes in the same execute.

### S2 — The two leftovers

`SessionMessageQueueBootWedgeTests.RegisterDispatcher`: drop the `GitSettings` / `IGitService` / `AddScoped<DelegationWorktreeService>()` block. Call

```csharp
services.AddDelegationWorktreeGraph(new GitSettings
{
    WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-bootwedge-wt"),
});
```

Keep `DelegationWorkspaceResolver`, `AgentTaskService`, the stopper, `BootWedgeRelaunchState`, `AgentTaskDispatcher`. `BridgeQueueHarness` already registered `NoWorktreeManager` and `GitWorkspaceService`; `TryAdd` keeps both.

`AgentTaskCatchUpSettlementTests.RuntimeFor`: `services.AddGitWorkspaceService();` next to the existing `AddLogging()`. Reply-only; no worktree graph. Constructor of `GitWorkspaceService` is `(ILogger, GitProcessGate? = null, IOptions<GitSettings>? = null)` — logging is already there.

No assertion changes. Catch-up still does not care about a deliverable path.

### S3 — One sentence of docs

Append to the existing CARD-0297 paragraph in `docs/testing-and-build.md` (the "Hand-built ServiceCollections and the delegation worktree graph" block), not a new gotcha: `DelegationHarnessCensusTests` fails a new dispatcher or reply harness that skips the helper, and names the file (CARD-0244).

No production comment. `ResolveDeliverableAsync` already points at CARD-0230.

---

## What this card does not do

- Re-implement or rename `AddDelegationWorktreeGraph` / `AddGitWorkspaceService`.
- Unify the 32 dispatcher `ServiceCollection`s into one builder.
- Change `SettleAsync` / `ResolveDeliverableAsync` (never-abort is CARD-0230; harness drift stays the defect).
- A fake `GitWorkspaceService`.
- Close or re-execute CARD-0297 (Review, its own card).
- Fix other swallowed DI misses (CARD-0230 §5.1 `AgentSupervisorService` for `DeliveryUnverified`).
- Touch `new DelegationWorktreeService(...)` call sites.

---

## Test matrix

| Layer | Test |
|---|---|
| Unit | New `DelegationHarnessCensusTests` green after S2 (red on HEAD before S2; that is the pin) |
| Unit | Existing `DelegationTestServicesTests` still 4/4 |
| Integration | `SessionMessageQueueBootWedgeTests` after the helper swap |
| Integration | `AgentTaskCatchUpSettlementTests` after `AddGitWorkspaceService` |

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0244/ -- --treenode-filter "/*/*/DelegationHarnessCensusTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0244/ -- --treenode-filter "/*/*/DelegationTestServicesTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0244/ -- --treenode-filter "/*/*/SessionMessageQueueBootWedgeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0244/ -- --treenode-filter "/*/*/AgentTaskCatchUpSettlementTests/*"
```

Forward slash on OutputPath. Delete `bin-card0244*` after. Do not co-schedule Pty tests. Do not run the full `Antiphon.Tests` assembly for this card.

---

## Sequencing and risks

**Order: S1 census (red), S2 two leftovers (census green), S3 docs.** One execute.

| Risk | Disposition |
|---|---|
| Census false-positive on `TryAddSingleton<GitWorkspaceService>` | Negative lookbehind or allowlist the helper file (S1). |
| Census false-positive on a comment that names `AddScoped<DelegationWorktreeService>` | Accept; nobody comments the illegal call. Do not strip comments. |
| `BridgeQueueHarness` files that add `AgentTaskReplyService` in `ConfigureServices` | Rule C allows `BridgeQueueHarness` in the same file (`AgentTaskAnswerTests`, `AgentTaskReplyOverlayTests`). |
| Helper `TryAdd` after boot-wedge's fake `IWorktreeManager` | Already the CARD-0297 contract; `DelegationTestServicesTests` pins it. |
| Catch-up harness starts talking to git | `AddGitWorkspaceService` only. `GitWorkspaceService` is consulted on a `docs/**/*.md` path then a branch lookup for Worktree tasks. Catch-up seeds marked reports without that, so no subprocess. |

---

## Execution notes

- Re-run the three greps at execute time (`AddScoped<DelegationWorktreeService>`, `AddScoped<AgentTaskDispatcher>` without `AddDelegationWorktreeGraph`, `AddSingleton<AgentTaskReplyService>` without helper/`BridgeQueueHarness`). The two-file leftover list is 2026-09-03.
- If a third violator appears, migrate it the same way; do not expand the census rules.
- Do not add `AddSingleton<GitWorkspaceService>()` next to either leftover.
