# Review efd4665b — task 4840174e (runner unhandled-exception problem details)

Reviewed: feat/card-task-4840174e tip a8505f83946f7d04ec2627dfb3b7900c5fc9b98f, base rc/2026-10-27 8004259c70a068507decec1b1b9896d58c5b4047.
Round: Final review with Interim scope, as the brief commissioned. Outcome: **3 defects, changes required.**

## Defects

### D-1 Regression: the server cannot save the new 500 detail, so the failed launch is never recorded
- **Where:** `src/Antiphon.SessionRunner/SessionReadLaunchRoutes.cs:61-65` puts `ex.ToString()` in the 500 detail. `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:1224` maps any 500 to `HttpRequestException($"Session runner returned 500: {detail}")`. `server/Application/Services/AgentSessionService.cs:443` (interactive launch) and `:335/:343` (card launch) then assign `ex.Message` unclipped to `AgentSessions.FailureReason` (varchar(2000)) and `RunAttempts.ErrorDetails` (varchar(4000)).
- **Failure:** I measured the commit's own test, unchanged apart from writing the body to a file. The body is 2,825 bytes, the detail is 2,708 chars, and the resulting message is 2,737 chars. That is more than 2000, so `SaveChangesAsync` in the launch catch throws Npgsql 22001. The evidence transaction rolls back, the session stays Starting, and the agent stays Running with no live session (the phantom-Running case the catch exists to prevent). `AgentChanged` is not published, and a DbUpdateException replaces the launch error. A real native failure has a deeper stack plus inner exceptions, so it will be longer still. Before this change the body was empty, the detail became "Internal Server Error", and it fit.
- **Why:** `ColumnText` (CARD-0195/0205) records the rule that a failure report must not fail because of its own size. Here the diagnosis this change exists for is lost on the server.
- **Fix:** Send a bounded detail from the runner: type + message, clipped (for example to 1000 chars). The stack is already in the runner log through `LogError`. On the server, `ColumnText.Clip` the `FailureReason` (2000) and `ErrorDetails` (4000) assignments, or clip in `ThrowForRunnerProblemAsync`. Add a server test in which a runner 500 with a detail over 2000 chars still leaves a persisted Failed session row.

### D-2 Disclosure: the detail is unbounded and unredacted, including the full launch command line
- **Where:** same `SessionReadLaunchRoutes.cs:63` (`detail: ex.ToString()`). Source of the text: `src/Antiphon.Agents.Pty/ModernConPtyConnection.cs:245` `throw LastError($"Could not start terminal process {commandLine}")`. This is the native Windows modern-ConPTY path under investigation. The text travels through `HostSession.cs` `launchFailed` (ex.Message), `PtyHostClient.cs:94` and the new catch-all, then into the HTTP body and from there to server `FailureReason` (UI, delegate status).
- **Failure:** A Claude launch command line carries the whole composed instruction bundle through `--append-system-prompt` (`InstructionBundleComposer.cs:222`). One failed CreateProcessW therefore ships the instructions body, plus a full stack with build paths, over HTTP to the server's stored failure text. API keys are env-only by policy (`ApiKeyPlaceholder`), so I found no key in argv. The exposure is the instructions body and the stack/paths. The repo already avoids this elsewhere: `LaunchArgvGuard.Describe` deliberately excerpts argv, and `DiagnosticsRedactor`/`HerdrDisposalRedactor` exist. None of them is applied here.
- **Why:** The new test's `ShouldNotContain(sentinel)` cannot go red for this leak, because the rules payload never enters the exception message in that test.
- **Fix:** Use the bounded type + message detail from D-1. Do not send the stack on the wire. Redact or excerpt argv in the problem detail. Add a test that throws an exception whose message contains a long argv/instruction sentinel, and assert the body omits or clips it.

### D-3 The widened PtyHost catch changes behaviour with no test
- **Where:** `src/Antiphon.PtyHost/PtyHostServer.cs:130-139`.
- **Failure:** Every fault other than OperationCanceledException now replies `commandFailed`. Before, an IOException dropped the connection, and other faults stopped the host, whose dispose kills the child.
  - **Custody:** an IOException during custody observation used to surface to the runner as a dropped pipe. `SessionRunnerRuntime.cs:4486` mapped that to Unknown/`custody_observer_unavailable`. It now becomes `VerificationCustodyException("commandFailed")` (`PtyHostClient.cs:110`), which the runner route answers as a custody 409 of type `commandFailed`.
  - **Faults that used to stop the host:** a Win32Exception from Kill, or an UnauthorizedAccessException from the post-launch manifest save, used to end the host. Now the host and child stay alive.
  - **Misrouted errors:** an unsolicited `commandFailed` from fire-and-forget Input/Kill completes whichever request is pending at that moment (`PtyHostClient.cs:220-226`).
  - **Existing test:** the only test is the Windows-only `HostSessionPipeTests.Command_before_launch_returns_error_frame_and_connection_survives`. It covers the InvalidOperationException path, which did not change.
- **Fix:** Either narrow the catch (IOException keeps dropping the connection; only non-I/O faults are named) or add Linux-runnable tests. Those tests need a non-IOE/ArgEx dispatch fault that gets `commandFailed` while the connection survives, and the custody IOException outcome.

## Checks that passed (no defect)
- **Middleware order:** `UseRunnerExceptionMapping` is the first `app.Use` (`Program.cs:170`). Route-level catches and endpoint filters (HerdrUnreachableFilter) map before it, and the specific catches sit above the new one, so it hides no 404/409/422/503.
- **Cancellation and started responses:** OperationCanceledException still propagates, and a response that has already started is rethrown.
- **Existing clients:** the server's `ThrowForRunnerProblemAsync` reads type/detail and maps 500 to HttpRequestException whatever the type. An unknown type does not break parsing. `RestartFailurePolicy` already classifies HttpRequestException, so retry behaviour is unchanged.
- **Phone-home launches** (`PhoneHomeCommandDispatcher`) do not pass through this middleware. Server2 phone-home sessions are not affected by either the fix or D-1/D-2.

## Advisories
- A-1: The comment at `SessionReadLaunchRoutes.cs:21-25` ("Narrow on purpose … mapping anything wider would hide a real bug") now sits above a catch-all. Update it.
- A-2: Make `runner_unhandled_exception` a named constant like the other problem types.

## Test evidence (server2, Linux, build-slot gated, UseAppHost=false, `dotnet exec --no-build`)
| Row | Commit | Result |
|---|---|---|
| SessionRunner.Tests `(GrokRules*)\|(HerdrPlacementCheckRoute*)\|(RunnerAbsenceEvidenceContract*)` | a8505f839 | total 96, passed 94, skipped 2 (Windows-only `Real_http_capability…herdr_disabled`, `Restart_recovers…(False, True)`), failed 0 |
| Red witness `Unmapped_launch_failure_names_the_cause_in_the_response_body` | scratch `git archive` of HEAD with SessionReadLaunchRoutes.cs + PtyHostServer.cs restored to 8004259c7 | 1/1 **failed** as intended: empty body, `ShouldContain(cause)` fails |
| Same test, green, body length measured | scratch copy of HEAD | 1/1 passed; body 2,825 bytes |
| HostSessionPipeTests, RcRosterRepairTests (3), WindowsUnitPlatformAdmissionTests (C1210), C1514LandingSplitBudgetTests (C1404/C1577/C1584/C1410/G2865), DelegationDispatchRecoveryBoundaryTests C1153_* | — | **Not run.** The build-slot queue was full (position 2, 4/4 occupied) for over 9 minutes and the 45-minute budget ran out. |
| (discarded) whole-project PtyHost.Tests run | — | My mistake in scoping. With UseAppHost=false the launcher tests cannot find the native `Antiphon.PtyHost` host. This is not evidence about the change. No processes were orphaned. |

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 8004259c7 -HeadRef HEAD`: commits=2 violations=0.
- `git merge-tree --write-tree origin/rc/2026-10-27 (91b45006b) HEAD`: clean.
- No checkpoint-tool receipt was produced, so reviewedSourceClean is false.
- Cleanup: every `bin-rvefd` directory was removed by literal path. The scratch copy is under the session scratchpad only.
