# Review: C1349 lost-wakeup same-adapter Register fix (CARD-1514, Debug 5e090a01)

Review task 3802ef67, round 1, Final/Full. Author Grok, reviewer Claude Opus.
Subject `origin/feat/card-task-5e090a01` tip `b103bdf8b97d7e4b7e85dbeecd51b8d09f016c0b`, base `300c8a99d0d267a39791410b7540189ab702f251`.

**Verdict: CLEAN.** No blockers. Three advisories follow. None of them holds the land.

## 1. Production safety

`AgentSessionRuntime.Register(Guid, IAgentProtocolAdapter)` has **no production caller**. I grepped every `Register(` call in `server/`, `src/`, `tools/` and `scripts/`. All of those calls go to other types: `RepositoryLeaseWaiters`, `PhoneHomeRunnerDirectory`, `RunnerProcessReaper`, cancellation tokens, `WindowsFileUseProvider` and `CheckpointTempRootSweep`. Every call to `AgentSessionRuntime.Register` is under `tests/Antiphon.Tests`: about 30 files, including the fake adapter's `RegisterOnStart` and `AttachAsync`, and `QueuedReceiptAssertions`. Production launches through `AgentSessionService.StartAsync`. That path runs `adapter.StartAsync` and then `_launchOwnership.TryRegister`, which is a different object. A real double launch is caught by the launch-queue claim: `AgentSessionLaunchQueue.LaunchAsync` records the session Failed and rethrows. That path never reaches `Register`, so making Register idempotent for the same adapter cannot mask a production double launch.

Lock risk: `_testAdapterRegistration` is held only across `TryAdd`, `_testBuffers.TryAdd` and `adapter.OnTextDelta += handler`. Each `OnTextDelta` add accessor is one of two kinds:

- A field-like event: `FakeAgentProtocolAdapter`, `ClaudeAdapter`, `CodexAdapter` or `RawPtyAdapter`.
- An empty `add { }`: all five `Runner*Adapter` types.

The only exception is a test wrapper in `Card1619ExitConsumerTests` that forwards to an inner adapter's event. None of these accessors invokes a delta or calls back into Register, so the lock cannot re-enter or deadlock. Hold times are three dictionary or delegate operations. The `continue` loop runs only when a concurrent `TryRemove` deletes the entry between the failed `TryAdd` and the `TryGetValue`. `TryRemove` does not take this lock, so the loop cannot spin forever. A different adapter still throws `ConflictException`. The witness asserts that, including the session id in the message.

## 2. RED witness and C1349 scenarios

I rebuilt `78b060e93` in a detached scratch worktree, with a leased build and run, `--timeout 570s` and `UseAppHost=false`. The result was total 1, failed 1, in 9.6 s wall. It threw `ConflictException: Agent session '<guid>' is already registered.` at `AgentSessionRuntime.cs:113`, called from `AgentSessionRuntimeTests.Register.cs:39`, the **second same-adapter call**. That is red for the stated reason. At the tip, CP-1 passes 1/1.

The witness is an acceptable deterministic substitute for the Windows one-off. It exercises exactly the operation that threw on Windows, a second `Register` of the same instance on the same runtime. It also checks two more things:

- The subscription stays single: one `Emit` yields buffer `"Z"` and sequence 1, not a doubled delta.
- A Barrier-raced pair of concurrent same-adapter registrations both return and still leave one subscription (`"ZZ"`, sequence 2).

The Linux stress could not reproduce the C1349 stack in 361 iterations, and the note says so plainly.

## 3. Reverse order

In the reverse order the helper's `TryAdd` wins and the launch's `RegisterOnStart.Register` throws. Both calls pass the same `harness.Adapter` to the same `harness.Runtime`. After the fix the launch's call returns idempotently, so `StartAsync` completes and the launch is not failed. **The fix closes this direction too.**

The stress `queue-inserted` empty-`SubmittedBodies` miss at `QueuedReceiptAssertions.cs:80` happened at the base. It is *consistent with* this reverse direction: the launch fault is swallowed by `WaitForIdleAsync`, so no `ConflictException` appears in the TRX. It is *not proven* to be that race, because another load-induced cause of an empty flush on the recovered runtime would look the same. Plainly: the `queue-inserted` miss is probably the same race, and the fix covers it. The `eligible` `TimeoutException` (`Delivery.cs:83/:85/:128`) is a **different** issue: 10-second git-entered and tick waits that expire under an artificial 2-CPU pin with burners. The fix does not touch it (advisory A-2).

## 4. Checkpoint rerun

There was one checkpoint-tool run, `20261117-085400-278d`, with `--serial` and `--expected-source-sha b103bdf8b…`. The 1-minute load was 10.42 at start. Every row reported slot=granted, dirty=0, sourceState=clean and buildSource=verified. One cold build took 295 s. The tool reported `unlisted: none`.

| CP | Result | Wall |
|---|---|---:|
| CP-1 witness | 1/1 passed | 12 s |
| CP-2 `DispatcherSiblingInspectionSchedulingTests/*` | 49/49 passed, including all four `C1349_LaterTaskReceivesBriefBeforeInspectionCompletes` cases (eligible, busy, queue-inserted, lost-wakeup) | 95 s |
| CP-3 `TestTimingOperationLabelTests` | 2/2 | 10 s |
| CP-4 `ImpactedClassesSelectorTests` | 50/50 (floor 31) | 287 s |
| CP-5 eleven-class guard union | 114 passed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact`, the plan-declared Linux skip), 0 failed | 282 s |

`validate --rows CP-1,CP-2,CP-3,CP-4` gave `CHECKPOINT SOURCE VALID rows=4`. CP-5 is left out of the receipt only because the validator treats any skip as row_failed. The skip is the declared C1210 Linux skip.

Other checks:

- `check-evidence-diff.ps1` over base..tip: commits=4, entries=0, violations=0.
- `git merge-tree --write-tree origin/master` with master now at `7901e9ba537b757740c2913aa2211d513b0b31f2`: clean, exit 0.
- `regenerate-derived-impact-index.ps1 -NoBuild` at the tip printed `unchanged`, and the tree stayed clean.

## 5. Scope

The diff touches exactly five files: `AgentSessionRuntime.cs`, the comment in `FakeAgentProtocolAdapter.cs` (comment only), the new `AgentSessionRuntimeTests.Register.cs`, the investigation note and `tests/derived-impact-index.json`. Nightly pins, the selector chain and the policy JSON are untouched. The index changes are the expected `ConflictException.cs` class count (182 to 183), the `AgentSessionRuntimeTests` entry and the new file's row.

## 6. Why-missed and pre-land row

These are correct. The introducing commits were `dee14d2c4` (helper check-then-Register) and `999a4c6db` (`RegisterOnStart = harness.Runtime` on C1349). The class is Integration/Slow and outside the Code/Review guard union and the Merge high-radius subset. The proposed CARD-1782 row `server/Application/Services/AgentSessionRuntime.cs -> /*/*/AgentSessionRuntimeTests/Same_adapter_register_is_idempotent_and_a_different_adapter_conflicts` is the cheapest deterministic catch, at about 12 s on a warm build. Native Windows proof for group 36 is **owed to the next rc qualification run**. This Linux review does not replace it.

## Advisories (to cards, not blocking)

- **A-1:** `Register` is a public method on a production service, but it is a test-only seam. It could become `internal`, since `Antiphon.Tests` already has `InternalsVisibleTo`, or carry a `// test seam` marker, so that a future production caller cannot inherit same-adapter idempotency by accident. This predates the subject. The change adds no production risk today.
- **A-2:** C1349 `eligible` uses 10 s git-entered and tick waits, and those timed out once under a 2-CPU pin with burners. It is not seen under normal load. Re-check against a loaded p95 only if it shows up in an rc run.
- **A-3:** The reverse direction is covered by reasoning plus the order-agnostic witness. No test forces helper-first-then-launch through the real `AgentSessionLaunchQueue`. A deterministic C1349 variant that registers before releasing the launch would prove it end to end. This is optional.

## Process note

The scratch red run at `78b060e93` (17 s lease) started with the 1-minute load at 18.34, above the brief's 16 threshold. I read the load but did not wait. The build before it started at 15.95. The red result is unaffected because it is deterministic, but that start broke the brief's load rule.
