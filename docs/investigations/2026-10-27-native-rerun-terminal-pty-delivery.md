# Native Windows rerun: terminal, PTY and delivery

Task: `32365cdf`. All 22 requested classes were rerun individually on native Windows. The first pass remains red: 393 passed, 26 failed, 4 skipped. One product transaction cycle and multiple fixture mechanisms are confirmed. Four intermittent failure mechanisms remain unconfirmed; the next stage is Investigate for those, with the confirmed findings ready for a separate Plan. No product or test source changed. Investigation is complete within its bounded scope; required alternate-output cleanup remains blocked by automatic approval review, detailed below.

## Provenance

- Requested rc: `origin/rc/2026-10-27`, fetched at `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- Assigned branch: `feat/card-task-32365cdf`. It was unpublished and clean, initially based at `718bd29f4a1504c64c491d607d31403fcb87dae2`. Repositioned the unpublished branch onto the requested rc with no task commits replayed or published history rewritten.
- Host: Windows 10 Pro `10.0.19045`, .NET SDK `10.0.300`; preflight available memory 9,424 MiB; no matching Antiphon test hosts, FakeClaude or FakeGrok processes; native qualification lock absent.
- Historical results (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Current generated evidence: `C:\Antiphon\worktrees\card-task-32365cdf\logs\native-32365cdf` (gitignored).
- One native build of the three required project graphs completed at report commit `2a13310b17aac1874eac018fbd07c04ccb5a0402` (same product/test source as rc): exit 0, 1,240 warnings, 0 errors, 9m04s compiler time; lease granted immediately and held 546s. Product and test source are unchanged. The warnings include nullable/analyzer diagnostics and an ASP.NET analyzer exception; a successful build is not a warning-free build.

## Results

First-pass totals: historical **363 passed / 48 failed / 4 skipped** (415 tests); current **393 passed / 26 failed / 4 skipped** (423 tests). Four classes gained two tests each, so the totals are not identical selections. Eight classes have no current failures. Counts are P/F/S.

| Class | Historical | Native rc | Cause/classification |
|---|---:|---:|---|
| `SessionMessageQueuePtyIntegrationTests` | 3/5/0 | 6/2/0 | Fixture: marked batch expectation; confirmed cleanup race |
| `SessionMessageQueueGrokPtyIntegrationTests` | 4/1/0 | 4/1/0 | Fixture: missing SessionStateStore prevents transcript ingestion |
| `RemoteControlRecoveryPtyIntegrationTests` | 6/2/0 | 7/1/0 | Fixture: ordinary guarded Esc while dialog is Blocked |
| `SessionQueueReceiptPlumbingTests` | 13/9/0 | 13/9/0 | Fixture: persisted start generation differs from accepted launch generation |
| `HerdrAlwaysOnChannelParityTests` | 23/2/0 | 23/2/0 | Fixture: unsupported Herdr custody; native Claude refusal remains unresolved |
| `DelegationBriefCeilingPtyTests` | 1/2/0 | 3/0/0 | fixed by rc/2026-10-27 |
| `SessionRuntimeActionTests` | 27/1/0 | 27/1/0 | Flake: class-run page-128 timeout; three isolated repeats pass; delay source unconfirmed |
| `TerminalRunnerSeatReleaseTests` | 38/1/0 | 38/1/0 | Flake: class-run boundary timeout; three isolated repeats pass; delay source unconfirmed |
| `DispatcherSiblingInspectionSchedulingTests` | 48/1/0 | 48/1/0 | Fixture: completed results no longer consume unfinished queue capacity |
| `CodexCliObservationGapTests` | 6/1/0 | 6/1/0 | Fixture: mixed Windows/POSIX rules receipt path |
| `GrokSignInRuntimeTests` | 0/1/0 | 0/1/0 | Product: incident insert awaits agent lock held by its awaiting caller |
| `GrokDelegateEndToEndTests` | 3/3/0 | 5/1/0 | Fixture: unforced reply-before-SentAt timestamp assumption |
| `PhoneHomeCompletedCardTransportTests` | 0/1/1 | 1/0/1 | fixed by rc/2026-10-27; Linux-only case remains skipped |
| `ChannelOutboundRecoveryTests` | 16/2/0 | 18/0/0 | fixed by rc/2026-10-27 |
| `AgentTaskStandingAgentDispatchTests` | 9/1/0 | 10/0/0 | fixed by rc/2026-10-27 |
| `CardFilePrivacySyncTests` | 22/1/0 | 23/0/0 | fixed by rc/2026-10-27 |
| `AgentTaskPipelineEndpointTests` | 3/1/0 | 3/1/0 | Fixture/environment: imports local policy 3/5/5 but expects 1/2/2 |
| `FakeClaudeContractTests` | 48/1/0 | 51/0/0 | fixed by rc/2026-10-27 |
| `CompletedCardWorkspaceRemovalTests` | 90/4/3 | 93/3/3 | Fixture/Windows: hidden .git overwrite and case-insensitive scratch |
| `DaemonStartupDiagnosticsTests` | 2/6/0 | 10/0/0 | fixed by rc/2026-10-27 |
| `GrokRulesHttpAcceptanceTests` | 0/1/0 | 0/1/0 | Intermittent shadow-copy rename denial; handle owner unconfirmed |
| `WorkspaceSourceVerificationWireTests` | 1/1/0 | 4/0/0 | fixed by rc/2026-10-27 |

“Fixed by rc/2026-10-27” is the requested label for a passing class on this rc; it does not claim a causal bisection. The first-pass runner used one exact class filter per invocation. Every row has `console.log`, `results.trx`, and `invocation.json` beneath `logs/native-32365cdf/<Class>-1/`. `first-pass.errors.log` retains every exact failing message and stack; `before.errors.log` retains the historical counterparts. No failing row is relabelled green because a later repeat passed.

## Confirmed mechanisms and bounded findings

1. **Confirmed stale batch expectation (fixture).** The failed `Batched_multiline_body_passes_composer_delivery_verification_and_submits_once` reached the child transcript. Expected lines were `[T] batched alpha` and `[T] batched omega`; actual complete lines include `[antiphon-channel:308db271a0214ea2818c76e0223eb4e0]` and `[antiphon-channel:f13bab6df0e646d4ae948c4aaad578a6]`. The assertion constructs unmarked text at `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:393` and compares the recipient file at `:395`. Production marks each initial channel batch member at `server/Application/Services/SessionMessageQueueService.cs:2627`; `ChannelPromptCorrelation.cs:76` defines the first-attempt eligibility and `:15` the marker. This is not evidence of a lost or partial prompt.
2. **Confirmed Windows filesystem assumptions (fixture), independent witness.** `logs/native-32365cdf/file-witness.log` records a newly created Git worktree `.git` as `Hidden, Archive`. `File.WriteAllBytes` throws `UnauthorizedAccessException`; opening that same file with `FileMode.Open`, `FileAccess.Write` succeeds (`CanWrite=True`). Thus the failure does not establish absent write permission. `CompletedCardWorkspaceInspectionRepairTests.cs:139` overwrites that hidden file with `File.WriteAllText`; `CompletedCardWorkspaceS5Tests.cs:96` uses `WriteAllBytes`. The same witness creates `Twin` then `twin`, obtaining exactly one file with content `second`; `CompletedCardWorkspaceInspectionRepairTests.cs:144` expects two. The native class reproduced exactly these three failures; its historical crash-worker failure passed.
3. **Confirmed cleanup race (fixture).** In the first class's `Launch_args_reach_the_child_process(inbox)`, the C1020 log captures PID 469860 at `05:17:39.3141574Z`, begins disposal at `.5199320Z`, rejects the same host at `.5585811Z`, then records its exit at `.5614359Z`. The test kills before disposal (`SessionMessageQueuePtyIntegrationTests.cs:641`). Disposal recaptures session/request manifests (`DirectSessionRunnerClient.cs:417`), and `TestOwnedPtyHost.cs:62` requires Alive before checking for an already captured generation (`:76`). Rejection becomes an accumulated error (`:107`, `:116`, `:163`) even when later exit is observed. Both launch-argument variants passed on each of three isolated repeats; the recorded error does not imply a survivor.

The cleanup mechanism is now **confirmed by a pure witness against the unchanged compiled helper**: `cleanup-witness-2.log` records `deadBeforeRecapture=False cleanup=success; events=captured,kill,observed-exit`, and `deadBeforeRecapture=True cleanup=cleanup-unresolved; events=captured,rejected,observed-exit; kills=`. No actual process is controlled by this witness. The first witness invocation had a PowerShell empty-collection enumeration error and is retained as `cleanup-witness.log`; it is not mechanism evidence.

4. **Confirmed modal fixture/API mismatch.** `RemoteControlRecoveryPtyIntegrationTests` now gives 7 passed / 1 failed. `C514_Idle_modal_clearance_delivers_three_complete_prompts` calls ordinary guarded `SendInputAsync` with Esc at `:94`. Its saved diagnostic for session `104ec42f-ac92-4806-983a-10899db0eb05` reports dialog State 2, Kind Choice, InputHistoryKnown true, accepted generation `2026-10-27T05:20:55.369253Z`, and the visible Remote Control menu. `AgentSessionRuntime.cs:2157` invokes `ProviderDialogObservationService.Request`; `ProviderDialogObservationService.cs:41` refuses the ordinary input while blocked. The failure is the fixture's attempt to clear the modal through this API, before its three body sends.
5. **Confirmed receipt fixture generation mismatch, reproduced with stored DB and runner observations.** All 9 receipt-plumbing failures remain. `SessionQueueReceiptPlumbingTests.cs:644` creates the launch spec with `WithNativeClaudeDialog`, but `:651` seeds `StartedAt=now` after the awaited start. The guard compares that persisted generation (`AgentSessionRuntime.cs:2158`) against the launch observation (`ProviderDialogObservationService.cs:26`). The additional `C475_QueueCommitAndTransportRecovery` invocation reproduces all six failing variants. Its `console.log:72` records session `48281f22-6578-4b28-b022-1c9bad766eed` with accepted generation `2026-10-27T06:26:00.401204Z`, Clear state, known history, InputRevision 0, and no writes. `observe-SessionQueueReceiptPlumbingTests-5.log` captures that same session with DB `StartedAt=2026-10-27T06:26:02.020124Z` in three snapshots. The 1.618920s mismatch directly reconstructs the refused generation check. A second captured session, `7706e43e-b8e0-4e52-801e-75bba9a79be2`, likewise has accepted `06:26:21.085548Z` versus DB `06:26:22.888223Z`. Both retained composer snapshots are complete.
6. **Confirmed stale queue-capacity expectation.** The fairness case fails at `DispatcherSiblingInspectionSchedulingTests.cs:377`, expecting Outstanding 0 but observing 1. Its log first records `inFlight=32, active=2, gitCalls=8`, then `elapsed=11.041s, inFlight=0, active=0, gitCalls=224`. `CardSiblingInspectionQueue.cs:38` counts every retained entry in Outstanding; `SiblingInspectionResults.cs:66` admits based only on unfinished entries, defined at `:161`. The next tick can admit the previously unattempted 33rd task. The test's `:376` comment assumes completed results still consume admission capacity.
7. **Confirmed mixed-separator Grok receipt fixture.** `CodexCliRemoteDeliveryFixture.cs:256` appends `/instructions/grok/.../rules.md` to a native Windows cwd; its recipient publishes this path at `:319`. The current C1029 output records all four Grok startup arms Failed with `grok_rules_receipt_invalid: path`, followed by fault arms never reaching their expected queue-save cut. `GrokRulesTransport.cs:100` rejects forward slashes in a Windows path. `rules-path-witness-2.log` runs the compiled validator: mixed Windows/POSIX path rejected; consistent Windows and POSIX paths accepted. The first witness accidentally constructed one combined PowerShell string; its log is retained and excluded from evidence.

The current Grok end-to-end failure occurs after successful settlement: `GrokDelegateEndToEndTests.cs:327` requires `replyEnd.CreatedAt <= nudge.SentAt`, while the captured row values are `nudge.CreatedAt=2026-10-27T05:43:50.3045040Z`, `nudge.SentAt=05:43:50.3397560Z`, `boundary.CreatedAt=05:43:51.7031540Z`, `seq=10`, `nudgedSeq=7`, task prefix `2e0af3b9`. All preceding status/result assertions passed. All three isolated repeats fail the same timestamp assertion after successful settlement. The fixture does not force the ordering asserted; the captured rows reconstruct the mismatch. Its historical timeout is not the current failure signature.

**Confirmed product self-blocking transaction in Grok sign-in.** The test detects sign-in (`outcome=SignIn`, 24ms) before its launch-queue wait times out. `AgentSessionService.cs:407` starts the launch-error transaction, `:416` locks the agent FOR UPDATE, `:458` awaits incident recording through a fresh scope (`:1577`), whose insert references the locked agent (`AppDbContext.cs:542`). Commit is later at `AgentSessionService.cs:480`. During isolated repeat 2, `signin-repeat2-b.db.log` at `06:03:37Z` records backend 95 idle in transaction and backend 96 inserting AgentIncidents, waiting on `transactionid` with blocker `{95}`. `signin-repeat2-locks.log` at `06:03:52Z` records PID 95 holding transaction 749 ExclusiveLock, PID 96 waiting for ShareLock on 749, and the `FK_AgentIncidents_Agents_AgentId` foreign key. The agent row is `1625fb5a-1117-4f25-bda8-93f51c84b44e`, session `06de6488-a061-4304-95ed-f001c2e21819`; the session is still Starting while the incident writer is blocked. This reconstructs the application-level await cycle, not merely a slow screen. The same test failed again; production occurrence frequency is not measured.

**Confirmed missing ingestion dependency in the short Grok queue fixture.** Its service collection (`SessionMessageQueueGrokPtyIntegrationTests.cs:150-165`) registers AgentSessionRuntime without SessionStateStore. Nonempty transcript ingestion requires that store (`AgentSessionRuntime.cs:1262`); the exception is caught and recorded at `:1276-1280`, returning an empty persist result. Repeat 2's native JSONL records session `12e3f7b0-a491-4b1e-a090-67ad6699a770`, event `...-1`, `user_message_chunk`, text `queued hello`, `agentTimestampMs=1793081235211`, followed by a response and TurnEnd. `observe-SessionMessageQueueGrokPtyIntegrationTests-2.log` records the same Running session and zero TranscriptEntries in repeated snapshots through `06:07:34Z`. Thus the complete recipient receipt exists, but the harness cannot ingest it. The multiline fixture supplies a direct database pump at `:331`, which bypasses this missing runtime dependency. This is stronger evidence than the earlier raw `SUBMITTED` output alone.

**Confirmed unsupported Herdr custody expectation (fixture).** The old always-on test expects Running at `HerdrAlwaysOnChannelParityTests.cs:107`, but the real observation marks custody known only for non-adopted, certain-input PtyHost sessions (`SessionRunnerRuntime.cs:2521`). A Herdr session therefore cannot satisfy this guarded write. The newer fixture explicitly asserts the resulting Failed/observation-unknown state and no backend writes at `HerdrAlwaysOnChannelParityTests.cs:590-604` and `HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:61`. The Herdr arm fails in the class run and all three repeats; its PtyHost arm passes. The separate native history case below is not explained by this Herdr restriction.

**Confirmed pipeline fixture/configuration dependency.** The test expects Plan/Code/Review 1/2/2 at `AgentTaskPipelineStatusTests.cs:993-995`. The endpoint uses persisted policy (`AgentTaskPipelineStatusService.cs:358-367`). Repeat 3 captured row `8dde801a-cd8e-44cb-878d-588c44eeb6dc` in isolated database `test_8d0fec2210a64cb68f73e241d6ca0076`: scope global, revision 1, provenance Migration, import `06:11:58.2271537Z`, Plan=3, Code=5, Review=5, Investigate=3, absolute=6. These exactly match the five allowlisted numeric entries captured from local .NET user settings in `pipeline-safe-local-settings.json`; no credential values were logged. `server/Antiphon.Server.csproj:8` declares `antiphon-server` UserSecretsId; `AntiphonWebAppFactory.cs:93-130` overrides selected configuration but none of these policy keys. Startup imports the effective settings once (`DispatchConcurrencySettingsService.cs:70-82`). This is a fixture isolation issue, not evidence that the endpoint miscalculates its persisted policy.

**Grok HTTP launch failure, handle owner unresolved.** The preserved `grok-http-*.log` from first pass reports `IOException: Access to ... .copying is denied` at `ShadowCopyStore.cs:63`, the staging-directory rename, before a PTY child launches. The test sees HTTP 500 at `GrokRulesHttpAcceptanceTests.cs:63`. All three isolated repeats pass. The denial establishes the failed operation; it does not identify antivirus, an ACL, or a concurrent file holder. No such attribution is made.

## Three isolated repeats

Attempts 2, 3, and 4 use exact method filters and the original compiled output; argument variants remain together. Every suspect intermittent failure received three repeats. The deterministic path, modal, capacity, and filesystem mechanisms use their class result plus stored reconstruction/pure witnesses. P/F/S counts:

| Class.method | Repeat 1 | Repeat 2 | Repeat 3 |
|---|---:|---:|---:|
| `AgentTaskPipelineEndpointTests.pipeline_route_is_literal_and_returns_the_advisory_contract` | 0/1/0 | 0/1/0 | 0/1/0 |
| `GrokDelegateEndToEndTests.an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge` | 0/1/0 | 0/1/0 | 0/1/0 |
| `GrokRulesHttpAcceptanceTests.Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled` | 1/0/0 | 1/0/0 | 1/0/0 |
| `GrokSignInRuntimeTests.A_sign_in_mode_fakegrok_pool_task_never_types_the_brief` | 0/1/0 | 0/1/0 | 0/1/0 |
| `HerdrAlwaysOnChannelParityTests.AlwaysOn_channel_bound_survives_child_death_and_replies` | 1/1/0 | 1/1/0 | 1/1/0 |
| `HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply` | 3/1/0 | 3/1/0 | 3/1/0 |
| `SessionMessageQueueGrokPtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path` | 0/1/0 | 0/1/0 | 0/1/0 |
| `SessionMessageQueuePtyIntegrationTests.Launch_args_reach_the_child_process` | 2/0/0 | 2/0/0 | 2/0/0 |
| `SessionQueueReceiptPlumbingTests.C475_AlreadyIdleWhenIdleHasRecipientReceipt` | 0/1/0 | 0/1/0 | 0/1/0 |
| `SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow` | 5/0/0 | 5/0/0 | 5/0/0 |
| `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner` | 1/0/0 | 1/0/0 | 1/0/0 |

The 33 repeat invocations comprise 60 test cases: 39 passed and 21 failed. The additional receipt-generation capture comprises six failed cases, giving **432 passed / 53 failed / 4 skipped across 489 total executions** in 56 invocations. These do not replace the first-pass results. The native history test changes failing variant/step: first pass Claude/true at first launch (`StandingRecovery.cs:67`), repeat 1 Claude/true at resume (`:102`), repeat 2 Claude/false at the second fresh launch (`:82`), repeat 3 Claude/false at first launch (`:67`). Each fails with observation-unknown. Other variants in that method pass.

## Remaining uncertainties and evidence needed

- **Native Claude observation race:** `herdr-fast4-...-26db4f918b4f4641afd2bc8455cad29c.ansi.log` preserves the failed repeat-3 session's full composer but no probe token/input. The corresponding host log records launch at `06:21:31.3612902Z`; its test fails after 1.950s. This is a late screen, not the observation used by the guard. Need the exact generation, dialog state, input-history flag, output sequence, and rendered snapshot at the refused startup write. The existing evidence cannot choose between a transient frame and another observation-binding race. Neither a missing composer fixture nor an always-on-only mechanism explains the alternating failures.
- **Runtime action saturation:** the class run times out at `SessionRuntimeActionTests.cs:580` waiting 10s for the 128th UI effect; all three method repeats pass five variants. `SessionRuntimeActionCoordinator.cs:151` drains a page through sequential persisted phases. Need phase/DB latency or worker scheduling evidence from the failing class-load condition. The passing repeats demonstrate intermittency, not a measured delay cause. Read-only repeat-2 monitoring initially queried the shared database while these fixtures use isolated databases; later monitoring captured the isolated databases and is retained without being treated as a failure trace.
- **Terminal seat release:** the class run times out at `TerminalRunnerSeatReleaseTests.cs:628` before the BeforeDispatch callback. All three isolated runs pass, taking 63.06s, 64.56s, and 65.57s including admission/startup. Need the reservation/verification outcome and time spent before `TerminalRunnerSeatReleaseService.cs:639` in a failing run. The historical `landing.lock` exception did not recur; it is a different signature.
- **Grok HTTP shadow-copy rename:** the class run's HTTP 500 originates at `ShadowCopyStore.cs:63`; three isolated repeats pass. Need a file/handle trace at the denied rename to identify the holder and sharing mode. No ACL, antivirus, or cleanup-race attribution is established.

Production frequency and the specific rc commit responsible for each newly passing class were not measured. The native binaries and fake providers were used; no real provider credentials or live runner were exercised. The fixture guards log the dead runner address for Program boots, and HTTP runner tests bind their own random port.

## Reproduction and evidence provenance

The build graph is stored as `logs/native-32365cdf/lane.slnf` and includes only the three requested test-project dependency graphs. Exact commands:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-32365cdf-build -- dotnet build logs/native-32365cdf/lane.slnf --property:OutputPath=bin-native-32365cdf/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label <class> -SlotWaitMinutes 10 -- dotnet run --project tests/<project> --no-build --property:OutputPath=bin-native-32365cdf/ -- --treenode-filter "/*/*/<Class>/*" --report-trx --report-trx-filename results.trx --results-directory <unique-evidence-directory> --timeout 10m
```

For a repeat, replace the final `*` with the exact method in the repeat table. `run-classes.ps1`, `repeat-failures.ps1`, and every invocation receipt retain the expanded filters. Antiphon.Tests, FakeClaude/PTY, and SessionRunner invocations ran sequentially; every driver acquired and released a host build slot. There was one native build, no rebuild, no source mutation, and no namespace/full-suite run. The longest class invocation was Codex observation gaps, 468.86s; workspace removal took 396.09s. No selection exceeded 30 minutes and no slot timeout occurred.

`assembly-hashes.json` binds the three compiled test assemblies. `before.json`, `first-pass.json`, and `all-results.json` retain test IDs, file paths, messages, stacks and outcomes. The accompanying `*.errors.log` files retain exact unedited error/stack text. Individual console logs preserve BUILD SLOT provenance. This was a direct compiled-output investigation, so no CHECKPOINT receipt is claimed. `final-run-digest.md` is the fetched historical digest from task 7e719c57; the historical raw TRX remains authoritative.

Pure witnesses are `windows-file-witness.ps1` / `file-witness.log`, `cleanup-witness.ps1` / `cleanup-witness-2.log`, and `rules-path-witness.ps1` / `rules-path-witness-2.log`. Two initial witness-driver mistakes (PowerShell collection enumeration and string-array construction) are preserved and excluded from conclusions. Read-only observation scripts and captured DB/native transcript files stay in the same gitignored evidence root. The repeated short receipt test disposed its rows before the coarse observer caught them; the additional longer receipt method captured two stored generations. The observer's first attempt before schema initialization returned a missing-relation error; subsequent row captures above succeeded.

## Completion checks

All 56 test invocations and all read-only observers exited and were joined. `final-process-census.json` shows no surviving test hosts, fake providers, processes executable from this worktree, or test-owned shadow-copy PTY hosts. The 14 listed PTY hosts belong to the live runner under `C:\logs\antiphon\session-runner`; they were left alone. Four HTTP runner logs were copied from the alternate output into the evidence root before cleanup was attempted.

`evidence-sha256.json` inventories 421 evidence files (27,395,540 bytes, excluding the manifest itself). `git diff --check` passes; the sole tracked difference from the requested rc is this report. The 36 alternate output directories and raw evidence remain gitignored.

The fetched `origin/master` is `718bd29f4a1504c64c491d607d31403fcb87dae2`. `git merge-tree --write-tree origin/master HEAD` reports add/add conflicts in `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`. Repeating the merge-tree check against the untouched requested rc base produces the same conflicts. They predate this report; no merge or conflict resolution was performed, and this rc-based branch is not claimed landable onto master.

**Output cleanup blocked by automatic approval review:** the computed-path removal was rejected, then a narrower `Remove-Item -LiteralPath 'C:\Antiphon\worktrees\card-task-32365cdf\server\bin-native-32365cdf' -Recurse -Force` was also rejected with `blocked by policy`. Neither deletion ran. `output-cleanup-validation.json` validates all 36 targets as ordinary, non-linked `bin-native-32365cdf` directories beneath this assigned worktree. They remain listed in `owned-build-directories-all.txt`; caller cleanup through an allowed mechanism is still required. No alternative deletion mechanism was attempted after the explicit-target rejection.

## Not done, noted

No fix was designed or implemented; confirmed product and fixture mechanisms require a separate Plan, and the four unresolved mechanisms require the specific evidence listed above.

--- next stage ---
next: investigate
handoff: Capture the refused native Claude observation, failing runtime-action and seat-release phase timing, and the denied shadow-copy rename handle; confirmed sign-in transaction cycle and fixture findings are ready for separate Plan tasks.
artifact: docs/investigations/2026-10-27-native-rerun-terminal-pty-delivery.md
