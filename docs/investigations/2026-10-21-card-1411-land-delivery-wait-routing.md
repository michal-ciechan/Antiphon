# CARD-1411: LandDeliveryWaitRoutingTests pin is stale after CARD-1359

Investigate task 659d1039, 2026-10-21. Checkout `7c585199d7f528ac41c08368d54f1b8faa1d5bbd` on `feat/card-task-659d1039`. The five files this test reads are the same blobs on that HEAD, on `origin/master` `5447617c55cef8a17ce6fc5d9d867e2f63707f3a`, and on rc/2026-10-20 tips `66e9cfa9fc6ba0525bd1bcb7b012b74c422767f3`, `0b455324d2cb5b811764c5ccd433cf136ac3f7e0`, and `cadf547f7d69b867937ece6e5cb11c5d4be9f504`.

## Outcome

Confirmed. The Windows RC red is a deterministic stale source pin, on Linux and on master as well. It is the same assertion at every tip above. It is a string search over committed sources, so path, timing, process, and file-semantics differences do not decide it.

`LandDeliveryWaitRoutingTests.Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not` expects the queue-cut evidence string to be preceded by `UntilProtocolAsync(`. CARD-1359 commit `ca856a8751bde71e986fcc910541687f50d5670d` replaced that call with `UntilDispatchAsync(`. `NearestWait` does not recognize `UntilDispatchAsync(`, so it reports the earlier `UntilAsync(` in the enqueue-failure test. The wait still renews: `UntilDispatchAsync` passes `ProtocolProgress() + dispatch` into `UntilAsync` (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs:182-187`).

## What the truncated assertion is

The poll text `expected UntilProtocolAsyn...` is Shouldly truncating the expected value at `LandDeliveryWaitRoutingTests.cs:41`:

```text
NearestWait(dispatchTests, "every dispatch warning must retain its notification key before the insert barrier")
    should be
"UntilProtocolAsync("
    but was
"UntilAsync("
```

`NearestWait` (`LandDeliveryWaitRoutingTests.cs:45-51`) finds the evidence string, then takes the later of `LastIndexOf("UntilAsync(")` and `LastIndexOf("UntilProtocolAsync(")` before that index. `UntilDispatchAsync(` contains neither token (`Until` is followed by `D`, and `UntilProtocolAsync(` is followed by `P`). With no `UntilProtocolAsync(` left in `DispatchBaseWarningDeliveryE2ETests.cs`, the search returns `UntilAsync(`.

That `UntilAsync(` is `DispatchBaseWarningDeliveryE2ETests.cs:97`, the plain enqueue-failure wait (`"two owned enqueue failures"`), which the test still requires to stay plain at line 42. Replaying `NearestWait` on the current dispatch-test blob puts both evidence strings at plain index 5006 and protocol index -1. The executed test stops at line 41, so line 42 did not run; the replay says it still matches `UntilAsync(`.

Every earlier pin in the method passed on the Linux run (the failure is the first throw, at line 41). A replay of the same method against the current land, fixture, and dispatch-fixture blobs matches the remaining `UntilAsync(` / `UntilProtocolAsync(` pins, the three `WaitForBoundaryAsync` contains, the three absent crash-cut strings, and `public Task WaitForBoundaryAsync(string boundary) => UntilProtocolAsync(`.

## Introducing commit

| Commit | Dispatch-test blob | Unit-test blob | `NearestWait` of the key-retention evidence |
|---|---|---|---|
| `208277fcea8e684f3e6faff465487a50e302c959` (parent) | `eaabcbf91129cb130d02458428e4f91a53cbee5c` | `5319cf66d6f05c71cc853ef96cebfc4520a7a8df` | `UntilProtocolAsync(` (protocol 6698, plain 5006). Replayed, not executed. |
| `ca856a8751bde71e986fcc910541687f50d5670d` | `b5d949948b3e15361b4b49eab57425e6d31a94f7` | same `5319cf66` | `UntilAsync(` (protocol -1, `UntilDispatchAsync(` at 6800). Replayed. |
| HEAD, `origin/master`, `66e9cfa9f`, `0b455324d`, `cadf547f7` | same `b5d949948` | same `5319cf66` | same failure. Executed on HEAD. |

`ca856a875` (`test(card-1359): S1 dispatch-aware quiet window for the C540 crash waits`, author 2026-10-19, committer 2026-10-20) is an ancestor of HEAD, `origin/master`, and all three RC tips. Its dispatch-test hunk replaces the queue-cut call and the other crash wait:

- `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs:130` `await f.UntilProtocolAsync(async () =>` became `await f.UntilDispatchAsync(async () =>`, with the evidence string still at `:135`.
- `:139` `else await f.WaitForBoundaryAsync(boundary);` became `else await f.WaitForDispatchBoundaryAsync(boundary);`.

The unit test still has a single history commit: `854becfd2460c7929d417095c1d71359258df44e` (CARD-0550, 2026-09-28), on both `origin/master` and `origin/rc/2026-10-20`. `docs/superpowers/plans/2026-10-19-card-1359-c540-wait-shape-plan.md` does not name `LandDeliveryWaitRoutingTests`. The S1 message says `WaitForBoundaryAsync`, `UntilProtocolAsync`, and `ReceiptAsync` are unchanged; those helpers are unchanged, and the call site the unit test pins is the one that moved.

The other four input blobs are also identical on HEAD, master, and the three RC tips:

- `tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs` `1f402377be5da5a1f9b8127b85f5d90d501ab257`
- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs` `f93f173c28cf72d99dafeeb9b3ad30d1c4aef5d0`
- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs` `6e4661eb569a0f19cb39b78a0cff5f096acfd663`

## Linux run

One run, build slot lease `7d4393e6-fffa-47ef-878a-985bee31da2f`, held 163s, `maxcpucount=6`:

`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c1411/ --property:UseAppHost=false -- --treenode-filter "/*/*/LandDeliveryWaitRoutingTests/Protocol_bound_waits_renew_on_git_progress_and_scan_waits_do_not"`

Exit 2. TUnit 1.44 on linux-x64, .NET 9.0.21. total 1, failed 1, succeeded 0, skipped 0, test duration 755ms. Stack: `LandDeliveryWaitRoutingTests.cs:41`. Isolated `bin-c1411/` directories were removed afterwards. The slot note that `dotnet run` does not apply `-maxcpucount` to its implicit build did not affect this result; the assertion is source text.

The class is `[Category("Unit")]` (`LandDeliveryWaitRoutingTests.cs:7`) and is on both CARD-1287 rosters (`docs/investigations/2026-10-15-card-1287-linux-unit-roster.txt`, `docs/investigations/2026-10-15-card-1287-windows-unit-roster.txt`). Which RC launch group lists it does not change the result.

## Classification

Real defect on both platforms: a unit source pin left behind by CARD-1359. The renewing queue-cut wait is `UntilDispatchAsync`, which still includes protocol-git progress. An RC-lane exclusion would leave the same red on master. CARD-1411 owns the pin update; CARD-1359 owns the introducing commit.

Line 139 (`WaitForDispatchBoundaryAsync`) has no evidence string this test searches, so this failure does not cover it. The fixture pin at `LandDeliveryWaitRoutingTests.cs:39` still matches `WaitForBoundaryAsync` itself.

## Remaining uncertainties

- The desktop log `C:\Antiphon\releases\candidates\rc-2026-10-20-66e9cfa9fc6b\logs\2026-10-21-1550-ebd2ff29` was not opened from this runner. The card's truncated expected text matches the Shouldly value reproduced here, and the five input blobs at the three RC tips are the blobs that were executed.
- Parent `208277fc` was classified by replaying `NearestWait` on its blob, not by a second test run.
- No separate Linux RC group log was retrieved. The Linux execution used the same five blobs as those tips and as `origin/master`.

## Not done, noted

Point `LandDeliveryWaitRoutingTests.cs:41` at `UntilDispatchAsync(` for the queue-cut evidence, and pin `DispatchBaseWarningDeliveryE2ETests.cs:139` to `WaitForDispatchBoundaryAsync`; an expected token of `UntilAsync(` matches the enqueue-failure wait at line 97 and leaves the queue-cut call unseen.
