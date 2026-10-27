# Native Windows rerun: terminal, PTY and delivery

Task: `32365cdf`. Investigation in progress; several fixture mechanisms confirmed; no green claim.

## Provenance

- Requested rc: `origin/rc/2026-10-27`, fetched at `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- Assigned branch: `feat/card-task-32365cdf`. It was unpublished and clean, initially based at `718bd29f4a1504c64c491d607d31403fcb87dae2`. Repositioned the unpublished branch onto the requested rc with no task commits replayed or published history rewritten.
- Host: Windows 10 Pro `10.0.19045`, .NET SDK `10.0.300`; preflight available memory 9,424 MiB; no matching Antiphon test hosts, FakeClaude or FakeGrok processes; native qualification lock absent.
- Historical results (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Current generated evidence: `C:\Antiphon\worktrees\card-task-32365cdf\logs\native-32365cdf` (gitignored).
- One native build of the three required project graphs completed at report commit `2a13310b17aac1874eac018fbd07c04ccb5a0402` (same product/test source as rc): exit 0, 1,240 warnings, 0 errors, 9m04s compiler time; lease granted immediately and held 546s. Product and test source are unchanged. The warnings include nullable/analyzer diagnostics and an ASP.NET analyzer exception; a successful build is not a warning-free build.

## Results

Pending the native runs. Each class will retain its exact filter, pass/fail/skip counts, errors and stack in generated evidence. The historical digest is corroboration; raw TRX rows own the before counts.

First completed class: `SessionMessageQueuePtyIntegrationTests`, historical 3 passed / 5 failed; current 6 passed / 2 failed in 115.35s including gate/launch. Its fresh TRX is `logs/native-32365cdf/SessionMessageQueuePtyIntegrationTests-1/results.trx`.

## Evidence established while reruns continue

1. **Confirmed stale batch expectation (fixture).** The failed `Batched_multiline_body_passes_composer_delivery_verification_and_submits_once` reached the child transcript. Expected lines were `[T] batched alpha` and `[T] batched omega`; actual complete lines include `[antiphon-channel:308db271a0214ea2818c76e0223eb4e0]` and `[antiphon-channel:f13bab6df0e646d4ae948c4aaad578a6]`. The assertion constructs unmarked text at `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:393` and compares the recipient file at `:395`. Production marks each initial channel batch member at `server/Application/Services/SessionMessageQueueService.cs:2627`; `ChannelPromptCorrelation.cs:76` defines the first-attempt eligibility and `:15` the marker. This is not evidence of a lost or partial prompt.
2. **Confirmed Windows filesystem assumptions (fixture), independent witness.** `logs/native-32365cdf/file-witness.log` records a newly created Git worktree `.git` as `Hidden, Archive`. `File.WriteAllBytes` throws `UnauthorizedAccessException`; opening that same file with `FileMode.Open`, `FileAccess.Write` succeeds (`CanWrite=True`). Thus the failure does not establish absent write permission. `CompletedCardWorkspaceInspectionRepairTests.cs:139` overwrites that hidden file with `File.WriteAllText`; `CompletedCardWorkspaceS5Tests.cs:96` uses `WriteAllBytes`. The same witness creates `Twin` then `twin`, obtaining exactly one file with content `second`; `CompletedCardWorkspaceInspectionRepairTests.cs:144` expects two. Native class rerun is pending.
3. **Cleanup race under investigation.** In the first class's `Launch_args_reach_the_child_process(inbox)`, the C1020 log captures PID 469860 at `05:17:39.3141574Z`, begins disposal at `.5199320Z`, rejects the same host at `.5585811Z`, then records its exit at `.5614359Z`. The test kills before disposal (`SessionMessageQueuePtyIntegrationTests.cs:641`). Disposal recaptures session/request manifests (`DirectSessionRunnerClient.cs:417`), and `TestOwnedPtyHost.cs:62` requires Alive before checking for an already captured generation (`:76`). Rejection becomes an accumulated error (`:107`, `:116`, `:163`) even when later exit is observed. Further repetitions will distinguish frequency; the recorded error does not imply a survivor.

The cleanup mechanism is now **confirmed by a pure witness against the unchanged compiled helper**: `cleanup-witness-2.log` records `deadBeforeRecapture=False cleanup=success; events=captured,kill,observed-exit`, and `deadBeforeRecapture=True cleanup=cleanup-unresolved; events=captured,rejected,observed-exit; kills=`. No actual process is controlled by this witness. The first witness invocation had a PowerShell empty-collection enumeration error and is retained as `cleanup-witness.log`; it is not mechanism evidence.

4. **Confirmed modal fixture/API mismatch.** `RemoteControlRecoveryPtyIntegrationTests` now gives 7 passed / 1 failed. `C514_Idle_modal_clearance_delivers_three_complete_prompts` calls ordinary guarded `SendInputAsync` with Esc at `:94`. Its saved diagnostic for session `104ec42f-ac92-4806-983a-10899db0eb05` reports dialog State 2, Kind Choice, InputHistoryKnown true, accepted generation `2026-10-27T05:20:55.369253Z`, and the visible Remote Control menu. `AgentSessionRuntime.cs:2157` invokes `ProviderDialogObservationService.Request`; `ProviderDialogObservationService.cs:41` refuses the ordinary input while blocked. The failure is the fixture's attempt to clear the modal through this API, before its three body sends.
5. **Confirmed receipt fixture generation mismatch, live DB capture pending.** All 9 receipt-plumbing failures remain. `SessionQueueReceiptPlumbingTests.cs:640` creates the launch spec with `WithNativeClaudeDialog`, but `:647` seeds `StartedAt=now` after the awaited start. The guard compares that persisted generation (`AgentSessionRuntime.cs:2158`) against the launch observation (`ProviderDialogObservationService.cs:26`). Diagnostics for failed session `dbe70795-32f4-48ca-bf7d-d25770ff66e5` report accepted generation `05:21:29.071031Z`, Clear state, known history and InputRevision 0. The empty input is consistent with the deterministic mismatched-generation refusal, not missing composer chrome.
6. **Confirmed stale queue-capacity expectation.** The fairness case fails at `DispatcherSiblingInspectionSchedulingTests.cs:377`, expecting Outstanding 0 but observing 1. Its log first records `inFlight=32, active=2, gitCalls=8`, then `elapsed=11.041s, inFlight=0, active=0, gitCalls=224`. `CardSiblingInspectionQueue.cs:38` counts every retained entry in Outstanding; `SiblingInspectionResults.cs:66` admits based only on unfinished entries, defined at `:161`. The next tick can admit the previously unattempted 33rd task. The test's `:376` comment assumes completed results still consume admission capacity.
7. **Confirmed mixed-separator Grok receipt fixture.** `CodexCliRemoteDeliveryFixture.cs:256` appends `/instructions/grok/.../rules.md` to a native Windows cwd; its recipient publishes this path at `:319`. The current C1029 output records all four Grok startup arms Failed with `grok_rules_receipt_invalid: path`, followed by fault arms never reaching their expected queue-save cut. `GrokRulesTransport.cs:100` rejects forward slashes in a Windows path. `rules-path-witness-2.log` runs the compiled validator: mixed Windows/POSIX path rejected; consistent Windows and POSIX paths accepted. The first witness accidentally constructed one combined PowerShell string; its log is retained and excluded from evidence.

Additional first-pass outcomes so far: brief ceiling 3 passed / 0 failed (fixed by rc/2026-10-27); runtime actions 27/1 (`poison-page`, first-page wait at `SessionRuntimeActionTests.cs:580`); terminal seat release 38/1 (new timeout at `TerminalRunnerSeatReleaseTests.cs:628`, not the historical `landing.lock` error); sibling inspection 48/1. Timing/race cases remain open until repetition.

## Remaining uncertainties

The first pass is still running. Additional completed classes are phone-home transport (1 passed, 1 Linux-only skip), channel outbound recovery (18 passed), standing-agent dispatch (10 passed), card-file privacy (23 passed), and FakeClaude contracts (51 passed): fixed by rc/2026-10-27 in this native selection. This label records the current result, not a bisection establishing which commit changed it.

The current Grok end-to-end failure occurs after successful settlement: `GrokDelegateEndToEndTests.cs:327` requires `replyEnd.CreatedAt <= nudge.SentAt`, while the captured row values are `nudge.CreatedAt=2026-10-27T05:43:50.3045040Z`, `nudge.SentAt=05:43:50.3397560Z`, `boundary.CreatedAt=05:43:51.7031540Z`, `seq=10`, `nudgedSeq=7`, task prefix `2e0af3b9`. All preceding status/result assertions passed. The fixture does not force this particular ordering; isolated repetitions are pending. Its historical timeout is not the current failure signature.

The Grok sign-in test detects sign-in (`outcome=SignIn`, 24ms) before its launch-queue wait times out. A source-reconstructed product hypothesis is a self-blocking transaction: `AgentSessionService.cs:407` starts the launch-error transaction, `:416` locks the agent FOR UPDATE, `:458` awaits incident recording through a fresh scope (`:1577`), whose insert references the locked agent (`AppDbContext.cs:542`). Commit is later at `AgentSessionService.cs:480`. Live PostgreSQL blocking evidence is pending; the timeout alone does not confirm it.

The Grok short queue fixture records `SUBMITTED:queued hello` in retained raw output (`grok-queue-e407.ansi.log`) but Enqueue returns a no-output conflict. Unlike the multiline fixture, it starts no transcript pump and seeds no transcript entry (`SessionMessageQueueGrokPtyIntegrationTests.cs:139` versus `:331`). Native UserPrompt capture and a database snapshot are pending; raw terminal output alone is not the delivery verdict.

The remaining timing failures include `SessionRuntimeActionTests.cs:580` (first page not observed within 10s), `TerminalRunnerSeatReleaseTests.cs:628` (BeforeDispatch callback not entered within 10s), and Claude native standing-history recovery (`HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:67`, observation-unknown). These remain unclassified until isolated repeats. The separate Herdr always-on failure also records observation-unknown: the first test expects Running at `HerdrAlwaysOnChannelParityTests.cs:107`, whereas the newer standing-recovery fixture explicitly expects guarded refusal for Claude/Herdr at `StandingRecovery.cs:61`.

Pipeline HTTP still fails the Plan recommendation expectation at `AgentTaskPipelineStatusTests.cs:993`. The endpoint now uses the persisted policy when present (`AgentTaskPipelineStatusService.cs:358`), not the static recommendation fallback at `:355`. The stored policy value and its origin remain to be captured.

## Not done, noted

No fix designed or implemented; this is an evidence-only Investigate task.

--- next stage ---
next: investigate
handoff: Complete individual native class reruns and reconstruct any remaining failures from their exact error and source evidence.
artifact: docs/investigations/2026-10-27-native-rerun-terminal-pty-delivery.md
