# Windows rc group 3: disposal and local effort readiness

Debug `c1f2ee9f`, 2026-10-22. Both reds are in unchanged fixture paths: disposal fails during filesystem cleanup after its idempotence and exit assertions pass; the effort case selects `xhigh` successfully and then fails readiness with probe text still in the composer. No evidence attributes either to CARD-1481 or establishes a new product regression. Transient Windows file retention and delayed fixture input are the respective leading mechanisms; the original lock holder and memory contribution are not proven. Backlog defects filed: **CARD-1515** and **CARD-1516**.

This was a read-only investigation of source and qualification evidence. Only this report and ignored card-description drafts were written. No code, test, timeout, running qualification, service, release checkout, or evidence file was changed. No build/test was launched. The only stopped process was this task's own overly broad `rg --files` inventory, identified by exact command, parent and start time; no qualification child was stopped.

## Evidence and historical outcomes

Current evidence root (R):
`C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b\logs\2026-10-22-1606-d2777e21`

Earlier evidence root (B):
`C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784\logs\2026-10-22-0157-27a1b370`

Each root contains `antiphon-all-group-3-tests.log` and `antiphon-all-group-3.trx`. R's invocation is `d2777e21ef4c4a3e9b1fb315ff8f5602`; B's is `27a1b370ab984049a08da85a184939e5`. Candidate journals bind these directories to the full source SHAs below. Both logs report TUnit 1.44.0.0, Microsoft Testing Platform 2.2.2, Windows 10.0.19045, win-x64, .NET 9.0.21.

| Source/run | Dispose_is_idempotent | Local effort (xhigh) | Local effort (high) | Qualification meaning |
|---|---|---|---|---|
| rc `0714a9a89e4bff65aae7e0b4b94098694aeb931c`, R | Failed, 8.2028115 s, 16:13:32.5440584–16:13:40.7468699Z | Failed, 8.1980456 s, 16:14:49.3417052–16:14:57.5397508Z | Passed, 4.5298202 s | Group 3: 76 total, 74 passed, 2 failed, 0 skipped |
| rc `ceb6e3d6b78471db6831258699c493b8eb2d619b`, B | Passed, 3.0367813 s, 02:20:48.8339903–02:20:51.8707716Z | Passed, 4.0120791 s, 02:21:35.7149204–02:21:39.7269995Z | Passed, 4.1125123 s | Group 3: 76/76 passed; the whole qualification was later stopped, not complete-green |
| rc `6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45` | No execution evidence found | No execution evidence found | No execution evidence found | Identical relevant source, **not a verified pass** |
| master `00651b1df65d6ecbbd56f2edf64df954d218da22` (fetched during this investigation) | Current-tip execution unknown | Current-tip execution unknown | Current-tip execution unknown | Identical relevant source, **not a verified pass** |

There is no `6e79f3e13` candidate directory in the inspected release-candidate inventory. The [qualification start record](2026-10-22-windows-qualification-0714a9a89-start.md) establishes that the old qualification remained pinned to **ceb6e3d6b**, was stopped at 16:04Z, and was replaced by R. The [6e79f3e13 health report](2026-10-22-post-restart-rc-6e79f3e13-health.md) concerns the running AppHost for about 21 minutes; it is not a test-gate receipt. No `complete-green.json` was found under the candidate root, and `C:\Antiphon\nightly\last-complete-green.json` is absent. Therefore “since the last green rc gate” is supportable here only as a comparison with the earlier **green group**, not a full green rc qualification.

Additional history: `rc-2026-10-20-66e9cfa9fc6b`, run directory `2026-10-21-1550-ebd2ff29`, group 3 recorded `Dispose_is_idempotent` Passed in 2.9151625 s (120/120 group results); the local effort method was not in that group's roster. The [CARD-0449 verification report](../superpowers/plans/2026-09-09-card-0449-effort-dialog-code-verification.md) records both local effort rows passing at `15652113179c4fa84e9668d9cc7ba5a008ee6e66`, an ancestor of inspected master. CARD-1020's retained board outcome records native Windows CP-3 at `937246ed` as 39 passed/1 named symlink-privilege skip; its plan includes the disposal class. The cleanup land `c9e9c4b505dcf2d9a8ec6c9aa00107b0a6eeb307` is also a master ancestor. Those historical reports do not establish a current-master pass; the old CARD-0449 TRX path is no longer present.

SHA-256 of inspected immutable files:

| Evidence | SHA-256 |
|---|---|
| R TRX | `E08161007B23D9E353909237FF296923A0D17F15F1589B2D34D8AACAADFB4302` |
| R tests log | `B72FCE4D7E3811EC865360C3C9B9564713D78FF2DBB50C3AF0F75DBFED935183` |
| B TRX | `8571E82147F51517090015A7A19E673F72584F4DCCBD5DF54F66287644D29697` |
| B tests log | `C13D7933531AA5A1231B5B3FDA198E6810A4A051C73EC8D99938B3BD97644446` |

R's disposal output records the tested `Antiphon.Tests.dll` SHA-256 as `90292fe488f82c3ca38275659b73b79ae6319c31cf01dd3dd547d20f67f03da5`. These are historical run observations, not a newly certified build or gate.

## Dispose_is_idempotent

The failing operation is `fixture.DeleteRoot()` at test line 141, which calls `Directory.Delete(Root, true)` at line 366. **No idempotence assertion failed.** The preceding assertions are `calls.ShouldBe(1, "dispose-core-once")`, shared completion-task reference equality, null disposal exception, and independently observed exit for every witness. The later `private-tree-deletable` assertion is never reached because deletion throws.

Exact recorded exception/stack (the runner omits TUnit internals):

```text
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] UnauthorizedAccessException: Access to the path 'OpenConsole.exe' is denied.
  at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)
  at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)
  at Antiphon.Tests.Agents.OwnedPtyFixture.DeleteRoot() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\DirectSessionRunnerClientDisposalTests.cs:366
  at Antiphon.Tests.Agents.DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\DirectSessionRunnerClientDisposalTests.cs:141
  at Antiphon.Tests.Agents.DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent() in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\DirectSessionRunnerClientDisposalTests.cs:141
```

Session `06e43038-76fa-4f60-a178-dda2ebdb7e7e` owns host PID 418832, fakegrok child 189360 and console 326452, each captured with start/generation and executable path. Host log: child exited at 16:13:40.5001995Z, runner disconnected at .5469463Z, host exiting on shutdown acknowledgement at .5762452Z. Cleanup observed all three exits at .62853Z. Independent witnesses reported host/child/console `exited=True` at .71521–.71524Z, about 32 ms before the failed test ended. This excludes an observed surviving captured process and the old “wait 72 seconds for linger” explanation. The other five tests in the disposal class passed in R, including explicit host/descendant exit and body-failure cleanup.

The refused file is in the private shadow copy:
`C:\Users\lndco\AppData\Local\Temp\c1020-pty-8c1ab3a699024f4581af598358704274\logs\pty-hosts\bin\20261022-161333-07aa7c79\conpty\win-x64\OpenConsole.exe`.
Its hash was the expected shipped ConPTY 1.24.260710001 hash, `b7fd936c2668b87b9ecf7b3366dc6568afc1c6f981874cba3e955a1c35cf8160`. Later read-only metadata inspection found the file still present with `Archive`, not `ReadOnly`, attributes. This is not the known read-only Git-object fixture failure. `ShadowCopyStore` uses ordinary `File.Copy` into this private root, not shared hard links.

**Classification:** fixture/environment filesystem-cleanup failure in an inherited path. Transient image/file-handle retention after process exit, possibly involving an external scanner, is the leading explanation. The exception alone does not identify the lock holder, and no contemporaneous native-handle/ACL capture exists. A load contribution is plausible but cannot be proved from the 8.2 s total or later memory state. There is no evidence of a new disposal product regression since B.

**Smallest repair:** keep the production disposal contract and every idempotence/exit assertion. Repair `OwnedPtyFixture`'s owned-root teardown: establish filesystem-release completion, retain native error/HResult and exact image/ownership diagnostics on denial, and preserve final cleanup when the body fails. A bounded transient-lock cleanup mechanism, if needed after diagnosis, must have explicit transient-success and permanent-failure controls; a blind sleep, swallowed exception, or removed deletion assertion is not a repair. Persistent denial must remain red. No implementation was made here.

Tracked as **CARD-1515**, ID `c4764c43-3dff-422f-adac-98cdff12a6df`, Backlog, Normal/Soon.

## Local_adapter_keeps_requested_effort_and_probes_after_clearance(xhigh)

The actual assertion at `ClaudeAdapterEffortPromptTests.cs:86` is:

```csharp
(await task).ShouldBe(!stuck, adapter.SnapshotRenderedScreen());
```

Here `stuck=false`, so the expected readiness result is `True`; the actual result is `False`. The JSON assertion `applied.ShouldBe(requested)` at line 95 has not executed. Significant error text and the complete recorded source/async stack are:

```text
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await task
    should be
True
    but was
False
Additional Info:
    Fable 5.1 with xhigh effort
> zzb10d73aezzb10d
? for shortcuts
  at Antiphon.Tests.Agents.ClaudeAdapterEffortPromptTests.RunAsync(Boolean stuck, String requested) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\ClaudeAdapterEffortPromptTests.cs:86
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at Antiphon.Tests.Agents.ClaudeAdapterEffortPromptTests.RunAsync(Boolean stuck, String requested) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\ClaudeAdapterEffortPromptTests.cs:102
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at Antiphon.Tests.Agents.ClaudeAdapterEffortPromptTests.RunAsync(Boolean stuck, String requested) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\ClaudeAdapterEffortPromptTests.cs:114
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at Antiphon.Tests.Agents.ClaudeAdapterEffortPromptTests.RunAsync(Boolean stuck, String requested) in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\Agents\ClaudeAdapterEffortPromptTests.cs:114
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
  at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
  at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
  at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
  at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

Blank screen rows are omitted above; TUnit itself omits its internal frames. `Fable 5.1 with xhigh effort` is painted by the fixture only after its dialog is accepted. The token is ten characters (`zz` plus eight session hex digits); the composer contains one full token and a prefix of another. That proves retyping while fixture input/rendering was still catching up. The effort selection was already correct.

The fixture `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1:33–35` serializes its entire growing key list and performs `Set-Content` **on every key**, before repainting. The settings supplied by `RunnerClaudeAdapterEffortPromptTests.Settings()` are a 12,000 ms total probe/startup budget, 25 ms polling, 1,000 ms retype interval and only 500 ms clearance. The probe may enqueue another token before the PowerShell input loop finishes consuming the first; Ctrl+U then waits behind pending keys and their synchronous JSON/file writes.

**Classification:** inherited fixture/readiness timing susceptibility, plausibly exposed by host load. A post-token `NeverCleared` is the strongest inference: the whole case took 8.198 s, less than the 12 s startup budget, and a full token is still visible. The exact coordinator outcome was not logged: `RunAsync` constructs the adapter without the existing `StartupLogger`, then kills the child and deletes `trace.json` in `finally`, including on assertion failure. Therefore the original key ordering, clear timestamps and possible process-exit alternative cannot be conclusively recovered. No logged OOM establishes memory as the cause. The supervisor precheck recorded 8,119 MB free at 16:06:40Z, several minutes before these failures; it is not an incident-time sample.

**CARD-1481 is not the failing path.** Its rc implementation `a7e1ee4dd8d192ec7d4d267922941a255f1e57c3` modifies dispatcher/service role-to-argv policy, `ClaudeLaunchArgs` and `DelegationSettings`. This test directly constructs an `AgentLaunchSpec` for a local PowerShell fixture with literal `--effort requested` and `C449_REQUESTED`; it never invokes the dispatcher or reads the per-role override map. Its high row passed in the same run. Rolling back CARD-1481 would not repair the observed fixture path.

**Smallest repair:** remove per-character disk serialization from the fixture's input hot path while retaining the in-memory key oracle and publishing trace at semantic checkpoints such as Enter and completed clear. Attach `StartupLogger` and retain trace/raw/rendered output in test results before scratch deletion on failure. Keep both requested-effort rows, wrong-highlight navigation, final Ctrl+U, empty-composer and null-block assertions. A deterministic delayed-input witness should distinguish a clearance failure from wrong effort. Do not widen production timeouts or rerun until green. No implementation was made here.

Tracked as **CARD-1516**, ID `1c756bee-1c04-475d-9667-d9c7c47fd8c1`, Backlog, Normal/Soon.

## Source attribution and scope limits

The following Git objects are identical at **all four** SHAs in the outcome table. Directory objects establish equality of the whole named subtree, not only one method:

| Path | Blob/tree object |
|---|---|
| `tests/Antiphon.Tests/Agents/DirectSessionRunnerClientDisposalTests.cs` | `58cbdc568f82357648afff7e2d8a91217d4b3705` |
| `tests/Antiphon.Tests/TestHelpers/TestOwnedPtyHost.cs` | `9acfc5b957c336f02143491aa6baaf34da48dd69` |
| `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` | `580b4ff8407cab2d42cb15d930a40de7b0b0761c` |
| `tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs` | `2f906557bce4a5c9baf1627ccdfe01fb7f59a09f` |
| `tests/Antiphon.Tests/Agents/RunnerClaudeAdapterEffortPromptTests.cs` | `40107baa7f2046a13f33421acc5c56861d9636db` |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1` | `81c1ff829c52dc9739bb66dd6e8aace4534cd3a8` |
| `server/Infrastructure/Agents/Pty` | `d1505468abb88a9ab58293caf3df732e18183308` |
| `src/Antiphon.Agents.Pty` | `0744348abda2935fab6d801090e0a49dd2d55467` |
| `src/Antiphon.PtyHost` | `c1408637518245acc45a1fc2bd1e22b654b07531` |
| `src/Antiphon.PtyHost.Client` | `64adb268e03ea719c13622c472a37b908518ccff` |

Between B and R, `src/Antiphon.SessionRunner` changes only in build-slot broker metrics/routes, not the in-process runtime used by the disposal fixture. The test project and common build configuration inspected are unchanged over that rc range. Inspected master additionally stages the separate CARD-1483 `LandGraphAudit` helper via the test project; source equality is therefore intentionally **not** presented as equal build output or a passing current-master execution.

Duplicate searches used `scripts/card.ps1 search ... -Board Antiphon -All` with exact method names, `OwnedPtyFixture`, `effort prompt`, `NeverCleared`, `OpenConsole`, `file lock` and `probe`. Full relevant records read: CARD-1020, CARD-1481, CARD-0453 and CARD-1445. CARD-1020 is the completed process-ownership repair; CARD-1445 is checkpoint staticwebassets cleanup; CARD-0453 is parser coverage for another provider copy cohort. None covers these two observed defects. The new cards were confirmed Backlog by the create responses; board sync was off, so no generated card file was written.

## Handoff

Commission Plan for CARD-1515 and CARD-1516 with the existing Windows qualification protected. The first implementation verification should select only `/*/*/DirectSessionRunnerClientDisposalTests/Dispose_is_idempotent` (1 case) and `/*/*/ClaudeAdapterEffortPromptTests/Local_adapter_keeps_requested_effort_and_probes_after_clearance` (2 argument cases), respectively, through the required checkpoint/build-slot front door on separately owned output. A fresh master or 6e79f3e13 result requires an actual separately commissioned run; the missing execution evidence must not be relabeled green. Permanent-denial and delayed-input controls belong to the corresponding repair plan.

No new tests/builds were run in this investigation; the counts above are extracted historical results. This report does not authorize rc publication, claim an inherited-red rerun, or change the live qualification outcome.

--- next stage ---
next: plan
handoff: Plan the separate fixture repairs on CARD-1515 and CARD-1516; preserve exact assertions and diagnostics, and keep the running Windows rc qualification untouched.
artifact: docs/investigations/2026-10-22-rc-group-3-reds-debug.md
