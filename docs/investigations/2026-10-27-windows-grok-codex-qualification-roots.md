# Windows qualification roots: Grok, Codex receipts, phone-home

Static read of six Windows failures from qualification run `8f055f87` (desktop, Full) against `rc/2026-10-25` commit `105792f7d22f297746cdb338976f52e424b76fa7`. Error text and stacks are the final digest `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md` on `origin/feat/card-task-7e719c57`. No native test was run and the desktop was not touched. Current reading is `origin/rc/2026-10-27` (these files match this worktree). Line numbers below are that tip unless a candidate line is named.

## Login

None of these six should skip because the desktop has no `grok` login. They drive fakegrok, `cmd.exe`, an in-process phone-home socket, or a scripted remote recipient. A test that probes a real `GROK_HOME`/`auth.json` should skip, with a reason, when that probe is off or `auth.json` is absent. That is not these classes. `GrokSignInRuntimeTests` is the one that looks like a login test: it builds an empty `GROK_HOME` with the credential probe disabled and must fail the session as `ProviderSignInRequired`. Skipping it would hide that contract.

## GrokSignInRuntimeTests — fixture

`A_sign_in_mode_fakegrok_pool_task_never_types_the_brief` (F037, chunk 58, 31.59s). `TaskCanceledException` from `AgentSessionLaunchQueue.WaitForIdleAsync` (qualification stack line 216; current throw at `server/Application/Services/AgentSessionLaunchQueue.cs:220`). The class skips off Windows.

The harness registers a frozen `FakeTimeProvider` as the singleton `TimeProvider` (`tests/Antiphon.Tests/Application/GrokSignInRuntimeTests.cs:181`) and only advances it after idle wait returns (`:134`), for `DeadSessionFailGraceMinutes`. `RunnerGrokAdapter.WaitForReadyAsync` passes that clock into `GrokReadyWait` (`server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs:197`). Ready-wait poll and snapshot bounds delay on it (`src/Antiphon.Agents.Pty/GrokStartupReadiness.cs:270` and `:316`). The launch token is `CancellationToken.None`, so the idle wait's real 30s `CancelAfter` fires while the launch stays inside a delay that nothing advances. Fakegrok's sign-in paint matches `GrokSignInPromptDetector` and exits on a real stopwatch (`ANTIPHON_FAKE_SIGN_IN_EXIT_MS=8000`). A snapshot that classified sign-in would have returned before any delay. The 31.59s hang means the wait reached a frozen delay and never observed that exit.

An LF-only paint change was tried and dropped (`b3860c5f9de9b1b83a3c993f56128a1b63d82582` on `origin/rc/2026-10-27`). It is not this cause.

Minimal fix, test only: do not let the frozen clock be the `TimeProvider` ready-wait uses. Keep it for the dispatcher grace, and let ready-wait delays run on real time. Production uses `TimeProvider.System` and should keep doing so. Witness: Windows rerun of this method; the session must end `ProviderSignInRequired` rather than the idle wait cancelling.

## GrokDelegateEndToEndTests — fixture, already on rc

Three methods, chunk 49, all still `Dispatched` (F029, F031, F032). Screens show rules ACK and `SUBMITTED:` lines, and the interrupt case persisted turn ends (`ends=4:end_turn,6:cancelled`, `sawOpen=True`). ConPTY, fakegrok, and the rules file worked. The task row never left `Dispatched`.

At `105792f7`, `PumpTranscriptAsync` only called `AgentSessionRuntime.SyncTranscriptAsync`. That admits action rows and hints. `TryCatchUpSettlementAsync` (`AgentSessionRuntime.cs:1199`) has no callers. Settlement runs only inside `SessionRuntimeActionCoordinator`, which the candidate pump never started. `Working` is set from the reply path that the worker drains.

`ac9bee88f5350f28abdfbe22eaf4dc1b14967f3f` (`test: run and join settlement workers in Grok delegate harness`, on `origin/rc/2026-10-27`) starts `SessionRuntimeActionHostedService` around the pump (`GrokDelegateEndToEndTests.cs:1043-1055`). `AgentSessionRuntime.cs` did not change for this. The truncated `workspace=S` echo is inside one stored error line and is not the status bug: the brief `Status==Sent` assertion had already passed.

Witness: Windows rerun of the three methods at the rc tip. Do not treat this as a product settlement defect.

## GrokRulesHttpAcceptanceTests — unread HTTP 500

`Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled` (F126, session-runner group 4, 1.86s). `POST /sessions` returned `500` where the test expects `201` (`tests/Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs:63`). The assertion runs before the body is read. The stored error has no problem document. The class skips off Windows. The child is `cmd.exe`, not grok. `GET /capabilities` had already succeeded (the failure is the next assertion). The file is unchanged from the candidate through rc.

This is not the receipt-path grammar. `ValidateReceiptPath` accepts a drive path ending in `\instructions\grok\{id}\rules.md`. A grammar miss is `409` `grok_rules_receipt_invalid`. Argv-policy misses are `409`. A NUL body is the later request and is `422`. Host connect and launch timeouts are 15s and 30s (`SessionRunnerSettings`), longer than 1.86s, so this throw is before a hung ConPTY wait.

Two mapped producers return HTTP 500 as `GrokRulesTransportException` `grok_rules_file_write_failed`:

- `GrokRulesFileStore.WriteAsync` (`src/Antiphon.SessionRunner/GrokRulesFileStore.cs:85`), called from `SessionRunnerRuntime.StartCoreAsync` at `:583` (reasons `canonical_path`, `temp_write`, `replace`).
- `PtyHostManifest.SaveAtomic` when a receipt is present (`src/Antiphon.PtyHost.Protocol/PtyHostManifest.cs:67-68`, reason `metadata`), called from `SessionRunnerRuntime.cs:662` before the host starts, and again from `HostSession.LaunchCoreAsync` (`src/Antiphon.PtyHost/HostSession.cs:150` and `:215`).

Anything else `StartAsync` throws is unhandled. `PtyHostClient` wraps a host `launchFailed` reply as `InvalidOperationException` (`src/Antiphon.PtyHost.Client/PtyHostClient.cs:94`). `MapSessionLaunchRoute` does not catch that type (`src/Antiphon.SessionRunner/SessionReadLaunchRoutes.cs:67-95`), so ASP.NET returns 500 with no rules problem title.

The stored failure does not say which of those fired. Do not pick a write-flag or command-line change from this evidence.

Minimal next change: in the test, read the response body before asserting `Created`, and keep `runner-http.log` (the `finally` already writes it at `:113`). Witness: one Windows rerun. If the body is `grok_rules_file_write_failed`, the reason (`temp_write`, `replace`, or `metadata`) names the file operation. If it is `Host launch failed (launchFailed): ...`, the host exception text is the product defect. Until that body exists, this class stays open.

## PhoneHomeCompletedCardTransportTests — fixture, already on rc

`C1032_TransportClientUnsupported` (F030, chunk 49, 31.087s). `TimeoutException`: "Phone-home live connection was not accepted." Stack is `PhoneHomeTestHost.WaitLiveAsync`, then `World.Connect`. The sibling real-join method already skips off Linux.

At `105792f7` the unconfigured world set `AllowedCwd` and `RunnerRepository` from `Path.Combine` of a Windows temp root. `PhoneHomeSettings.Validate` (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:121-124`) requires both to start with `/` when phone-home is enabled. `PhoneHomeConnectionService.ExecuteAsync` calls `Validate()` before its first await (`:69`).

`7fc47ff727fe449c1a8f1fe5f0b66c5f2230d8b7` (`test: use POSIX protocol paths for unsupported completed-card transport`, on rc) points the unconfigured world at `AllowedCwd="/work"` and `RunnerRepository="/work/repos/antiphon"` (`PhoneHomeCompletedCardTransportTests.cs:161-162`). The configured world, which is the Linux-only test, still uses the temp paths.

Caveat, kept on purpose: a `Validate()` throw before the first await should surface from `StartAsync` as `InvalidOperationException` and never reach the 30s `WaitLive`. The stored failure is that timeout. So `7fc47ff72` is the required fixture correction, and it is not proven to be what produced this particular timeout. Registration and websocket failures inside the reconnect loop are swallowed and retried (`PhoneHomeConnectionService.cs:83` onward, backoff starting at 1000ms) until `WaitLive` gives up. Platform mismatch is not the cause: registration and `Capabilities().Platform` both come from the process OS. Witness: Windows rerun of `C1032_TransportClientUnsupported` at the rc tip. If it still times out, the next read is the reconnect loop's caught exception, not another path rewrite.

## CodexCliObservationGapTests — fixture, already on rc

`C1029_Per_kind_receipts` (F024, chunk 36, 3m37s). The aggregate lists only the six remote Grok vectors (`grok` and `grok-fault`, `remote=True`, both busy values). Local Grok and Claude vectors are absent, so they did not fail. Supplemental log: `grok_rules_receipt_invalid: path` with an empty rules field.

At `105792f7` the scripted recipient built the receipt with `Path.Combine(request.Cwd, "instructions", "grok", sessionId, "rules.md")` while the recipient platform is `linux` and `Cwd` is POSIX. On the Windows host `Path.Combine` inserts `\`, and `ValidateReceiptPath` rejects a `/`-rooted path that contains `\` (`GrokRulesTransport.cs:101`, reason `path`). That fails the launch, so the 25s snapshot wait never succeeds. This is the fixture's path join, not `GrokRulesFileStore` (that store is the local runner) and not a missing login.

`72ed08d5b9aba1804bf728f53a93a707d12a6287` (`test: compose remote Grok rules receipts using POSIX grammar`, on rc) replaces that join with `RulesPath` (`CodexCliRemoteDeliveryFixture.cs:256-257`, called at `:316`): `cwd.TrimEnd('/') + "/instructions/grok/" + sessionId:N + "/rules.md"`. `WindowsDeliveryFixtureWitnessTests.Posix_recipient_rules_receipt_uses_the_remote_grammar_on_a_Windows_host` (`:56-65`, added in `ea1ff25314680077c99f7e27409db0f8b2610f2b`) documents the mixed string and expects `RulesPath` to validate.

Residual for the native rerun, not a second required edit: `Directory.CreateDirectory(Path.GetDirectoryName(path))` on a POSIX path from a Windows process is not what the witness checks. The receipt string itself is not passed through `GetFullPath`. Witness: the witness test plus a Windows rerun of `C1029_Per_kind_receipts`.

## SessionMessageQueueGrokPtyIntegrationTests — fixture

`Queued_message_submits_through_the_real_runtime_runner_pty_path` (F027, chunk 38, 53.92s). `ConflictException` from `SessionMessageQueueService.EnqueueCoreAsync` (qualification stack line 661; current throw at `:661-663`): "the submitting Enter produced no output", which is `Describe(DeliveryVerdict.NoSubmitOutput)` (`:3752`). The file is unchanged candidate through rc. `6befcc97c22e12e58ac4ae538010dec8b517d597` only adds `NativePtyDiagnostics.CaptureAsync` on dispose and is not an ancestor of rc. It does not change the verdict.

The test (`SessionMessageQueueGrokPtyIntegrationTests.cs:140-227`) is Windows-only, pins the inbox backend, uses `TimeProvider.System`, waits until raw output contains `Fake Grok ready`, inserts a session row, and `EnqueueAsync(..., "queued hello", MessageSendMode.Now)`. It does not seed a transcript row and does not pump the runner transcript. The throw is inside `EnqueueAsync`, before the `SUBMITTED:queued hello` assertion, so the stored error has no screen.

Default `DeliveryVerificationSettings` has `TranscriptConfirmEnabled` (`SupervisionSettings.cs:352`) and the test does not replace `SupervisionSettings`, so the queue confirms. A new session has no transcript rows, so the baseline is unobservable. The confirm deadline is 30s and the Mode:Now grace is 20s (`:361`, `:404`). 53.92s is that pair plus a short evidence/settle overhead. Composer evidence was observed: a miss would be `NoComposerEvidence` ("Enter withheld") inside the 15s evidence window, and the message would say so.

For Grok, positive submit is only an emptied composer (`SubmitEvidence.cs:25-36`). `HeadFragmentIsVisible` is a substring of the normalized screen (`ComposerDeliveryEvidence.cs:159-172`). Fakegrok, after `\r`, writes `SUBMITTED:{text}` and `FAKE response to: {text}` (`src/Antiphon.FakeGrok/Program.cs:610-631` and `:854-887`) and does not clear that text. The head fragment `queued hello` therefore stays on screen after a successful submit, so the unobservable deadline takes `NoSubmitOutput` (`SessionMessageQueueService.cs:4328-4339`) even when Enter landed. Grace then looks for a `UserPrompt` row (`:4492-4516`). This test never mirrors the tailer, so no row appears. The exception text keeps the original `NoSubmitOutput` verdict (`:661`).

The sibling `Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer` (`:240-330`) seeds a `TurnEnd` and pumps runner transcript events into `TranscriptEntries` before `EnqueueAsync`. That is why it can confirm. This method cannot succeed on the current queue even if `\r` reaches fakegrok.

A swallowed Enter is not disproven: the screen was not captured. Both "Enter never submitted" and "Enter submitted, echo still contains the body, no transcript row" end at the same exception after ~54s.

Minimal fix, test only: give this method the sibling's observable baseline and transcript pump, then keep the raw `SUBMITTED:queued hello` assertion. Do not weaken the Grok emptied-composer rule; real grok clears the composer, and the echo false-positive is fakegrok. Witness: Windows rerun. If it still throws `NoSubmitOutput`, the deadline screen distinguishes a composer that still holds only the typed body from a screen that already contains `SUBMITTED:`.

## Already on origin/rc/2026-10-27

| Failure | Commit | What changed |
|---|---|---|
| F029 F031 F032 delegate stays Dispatched | `ac9bee88f5350f28abdfbe22eaf4dc1b14967f3f` | Pump starts the settlement worker |
| F030 phone-home live timeout | `7fc47ff727fe449c1a8f1fe5f0b66c5f2230d8b7` | Unconfigured world uses POSIX `/work` paths |
| F024 remote Grok receipt `reason=path` | `72ed08d5b9aba1804bf728f53a93a707d12a6287` | `RulesPath` POSIX concatenation; witness in `ea1ff25314680077c99f7e27409db0f8b2610f2b` |

## Still open as code

- F037: stop injecting the frozen `FakeTimeProvider` into Grok ready-wait (`GrokSignInRuntimeTests.cs:181`).
- F027: seed and pump the transcript the way the multiline sibling does.
- F126: retain the HTTP 500 body. No product edit until that body names `grok_rules_file_write_failed` or `launchFailed`.

Windows reruns of all six methods are the witness. Linux cannot execute these classes. `6befcc97c` is not the F027 fix and is not on rc.
