# Terminal, Grok and receipt fixture repairs — 061d878e

Fixture repairs are pushed; portable verification passed **76 / 0 / 0** (passed / failed / skipped). No product code changed. Windows qualification is still owed; this report does not turn Linux evidence into native acceptance.

## Source and authority

Assigned branch: `feat/card-task-061d878e`, retained without rebase, merge, reset or force-push. Base: `9f00008facfc48e75555c08df59887019b3f8dfe` (`origin/rc/2026-10-27`, top-up 8). Acted under the caller's standing fix authority without a permission pause. No desktop/native run, real Grok login, deployment, tag, release script or tracker write was performed.

Code/test tip verified: `94dac4c827b81a55857da27a89b9c8a6d75b2d3c`. The subsequent report commit changes narrative/evidence only. Each slice was pushed immediately:

- `3b42d9339e7bf52f53a316a594bf01892bbefcc7`: native Windows receipt-string and unknown-custody witnesses.
- `7e3857e2901f0c87360c13d1786d0fa81c7d23f3`: terminal fixture generation, ingestion, input, path/filesystem and cleanup repairs.
- `25bca1380bc73ef358922f4996b3be2f3509662b`: scheduling, clocks, policy import and HTTP diagnostics.
- `94dac4c827b81a55857da27a89b9c8a6d75b2d3c`: correct an introduced nullable assertion compilation error and finish the controlled nudge schedule.

The rc settlement worker (`ac9bee88f5350f28abdfbe22eaf4dc1b14967f3f`), phone-home POSIX fixture (`7fc47ff727fe449c1a8f1fe5f0b66c5f2230d8b7`) and remote POSIX receipt repair (`72ed08d5b9aba1804bf728f53a93a707d12a6287`) remain in place. The receipt helper now additionally handles native Windows cwd strings while retaining the existing POSIX contract and witness.

## Repairs

All paths below are under `tests/`.

| Files | Result |
|---|---|
| `Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs` | Seed the actual `PtyWorld` session's StartedAt from the accepted launch generation. Assert the persisted value equals the runner's accepted value. Existing conditional forwarding and fault hooks remain. |
| `Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs` | Seed an observable idle TurnEnd and run/join the existing real-runner transcript pump in the short delivery case, supplying the ingestion lane just as its multiline sibling does. Keep the raw SUBMITTED assertion. This avoids relying on runtime ingestion without SessionStateStore; it does not disable transcript confirmation. |
| `Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs` | Expect each batch member's persisted channel identity marker in the whole recipient body. Let owned disposal capture, stop and join the child, instead of explicitly killing immediately before disposal recaptures it. Apply the same redundant-kill removal in the Grok queue siblings. |
| `Antiphon.Tests/Application/RemoteControlRecoveryPtyIntegrationTests.cs` | Clear the real blocked management dialog through explicit interactive input, matching the sibling operator-clearance case. Retain all three complete recipient prompt assertions. |
| `Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs` | Assert Claude/Herdr refusal and zero bytes/receipts before the Running wait. Keep Claude/PtyHost success, and add a Grok/Herdr variant retaining the full death, recovery and channel-reply assertions. Never manufacture Herdr input custody. |
| `Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.cs`, `TestHelpers/SiblingInspectionFixture.cs` | Hold the initial fairness wave so exactly 32 entries are admitted. Join it, replace the completed release barrier with a fresh one, admit unattempted #33 while consuming 32 completed holds, and assert Outstanding/InFlight = 1. The next tick fills 32 in-flight slots; release and join before the final census. |
| `Antiphon.Tests/Application/CodexCliRemoteDeliveryFixture.cs`, `TestHelpers/WindowsDeliveryFixtureWitnessTests.cs` | Choose receipt separators from the recipient cwd grammar, independent of the host OS. Force drive and UNC strings on Linux; reject mixed Windows/POSIX strings and retain the existing remote POSIX witness. Add the Clear-screen/unknown-custody refusal witness. |
| `Antiphon.SessionRunner.Tests/CompletedCardRunnerFixture.cs`, `CompletedCardWorkspaceInspectionRepairTests.cs`, `CompletedCardWorkspaceS5Tests.cs` | Open/truncate existing gitfiles rather than using FileMode.Create against Windows Hidden files. Preserve the Hidden attribute. The case-twin vector reports a missing case-sensitive scratch capability instead of claiming two names exist on ordinary NTFS; its two-file and inspection assertions remain on supported storage. |
| `Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` | The dedicated HTTP factory supplies default role-policy recommendation and MaxOpenTasks values before startup imports persisted concurrency policy. It no longer inherits these numeric values from local configuration. All endpoint assertions remain. |
| `Antiphon.Tests/Application/GrokSignInRuntimeTests.cs` | Register System time for launch/runtime services and supply the frozen clock only to the dispatcher grace calculation (and its existing delivery profile). The adapter factory already defaults ready-wait to System time; this change does not claim that the older frozen-ready-wait hypothesis explains the native lock cycle. Preserve empty GROK_HOME, disabled credential probe, zero typed brief, ProviderSignInRequired and AuthenticationRequired assertions. |
| `Antiphon.Tests/Application/GrokDelegateEndToEndTests.cs` | Force the reply-before-final-SentAt schedule: after the nudge Enter reaches the recipient and its real reply is persisted, cut the acknowledgement once, then flush the normal late-confirmation path. Assert the cut occurred and the nudge is LateConfirmed. Both timestamp assertions remain; the existing action worker still runs and joins. Join this method's pump on failure too. |
| `Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs` | Read and retain `launch-response.json` before asserting Created, include the body in the assertion, and retain the existing runner HTTP log. No product inference/edit was made from an unread or unexecuted native 500. |

## Verification and costs

Host: server2 mirror, Debian 12, .NET test runtime 9.0.22, SDK 10.0.403. Every build/test driver took a host build slot; none timed out or ran unleased. Source stayed committed and frozen for each run. No full suite or namespace sweep was run: the brief explicitly requested narrow selections and one shared graph build.

The requested graph build compiled both affected test projects. It failed at the introduced DateTime/DateTime? Shouldly setup assertion: **1 error, 1,239 warnings, 7m01.67s**, slot wait 0s / held 422s, source `25bca1380bc73ef358922f4996b3be2f3509662b`. This is an introduced compilation failure, not inherited red or a product witness. After correcting it and the nudge control, one necessary corrective Antiphon.Tests compile used `--no-restore --property:BuildProjectReferences=false`: **0 errors, 1,156 warnings, 4m31.08s**, slot wait 0s / held 272s, source `94dac4c827b81a55857da27a89b9c8a6d75b2d3c`. This additional compile is the explicit deviation from the one-build budget. Warnings include nullable/analyzer diagnostics and an ASP.NET analyzer exception; neither build is warning-free.

Antiphon.SessionRunner.Tests compiled in the first graph invocation. Its source, shared linked sources and production dependencies have an empty diff between those two SHAs. The corrected Antiphon.Tests assembly came from the second invocation. Retained SHA-256 values:

- Antiphon.Tests.dll: `fed0810a8e660dba4a79c172fd10283f02c8addf8c1a4a586f90cf13b36afb4a`.
- Antiphon.SessionRunner.Tests.dll: `3127fb8a4bba9e6226751b1b2714861d6c04c22ac3787cfbad74d5dc6e7b3d82`.

Each filter below is exactly `/*/*/<Class>/<Method>`. All rows have fresh nonzero TRX results, **zero failures and zero skips**. Wall time includes runner startup, test fixture setup/teardown and slot admission; it is distinct from per-test durations retained in TRX/`cases.json`.

| Project | Class / method | Passed | Wall seconds |
|---|---|---:|---:|
| Antiphon.Tests | WindowsDeliveryFixtureWitnessTests / `*` | 6 | 21.26 |
| Antiphon.Tests | TestOwnedPtyHostPolicyTests / Capture_retains_every_owned_generation | 3 | 4.00 |
| Antiphon.Tests | SessionQueueReceiptPlumbingTests / C475_PumpPersistsCompleteLinesOnce | 5 | 60.86 |
| Antiphon.Tests | DispatcherSiblingInspectionSchedulingTests / C1349_InspectionQueueRecovers | 10 | 44.46 |
| Antiphon.Tests | AgentTaskPipelineEndpointTests / `*` | 4 | 32.88 |
| Antiphon.Tests | RemoteControlRecoveryPtyIntegrationTests / C514_Working_modal_waits_for_operator_then_converts_without_replay | 1 | 34.26 |
| Antiphon.Tests | HerdrAlwaysOnChannelParityTests / Herdr_launch_definition_starts_adopts_and_exits | 3 | 46.37 |
| Antiphon.Tests | CodexCliObservationGapTests / C1029_Per_kind_receipts | 1 | 214.93 |
| Antiphon.SessionRunner.Tests | CompletedCardWorkspaceRemovalTests / C1032_InspectionHoldShapes | 42 | 10.87 |
| Antiphon.SessionRunner.Tests | CompletedCardWorkspaceRemovalTests / C1017_RunnerRepositoryIdentity | 1 | 5.04 |
| Total | | **76** | |

Only the first test invocation waited for a slot (15s); other test waits were 0s. Largest measured test-host duration: the receipt-routing aggregate, 3m25.958s. No selection approached 30 minutes. External-call/fixture phase breakdowns were not separately collected here and remain unknown, not zero; invocation wall times, per-test durations, exact membership and the existing emitted logs are retained. No required native row is credited by these portable rows. These direct Debug runs do not claim checkpoint receipts, reviewedSourceClean or release-gate qualification.

Reproduction (fresh owned output required after cleanup):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label fixture-build -- dotnet build <two-project.slnf> --property:OutputPath=bin-<fresh>/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label <selection> -- dotnet run --project tests/<Project> --no-build --property:OutputPath=bin-<fresh>/ -- --treenode-filter '/*/*/<Class>/<Method>' --report-trx --report-trx-filename results.trx --results-directory <fresh-evidence-directory> --timeout 10m
```

The solution filter includes only `tests/Antiphon.Tests/Antiphon.Tests.csproj` and `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj`, with their project-reference graphs. The local exact driver/filter manifest is `.antiphon/debug-061d878e/run-selected.ps1`; build logs, invocation JSON, TRX/HTML, `cases.json`, counts, assembly hashes and cleanup inventories remain under that gitignored evidence root. No generated payload is committed.

## Native reruns still owed

Use native Windows, freshly built from the final fixture source, with fake providers and isolated test infrastructure. Missing a real Grok login is not a skip condition for any of these fixtures. The caller owns these reruns; this task did not touch the desktop.

| Project / class | Exact method selection or bounded class | What remains to prove |
|---|---|---|
| Antiphon.Tests / SessionMessageQueuePtyIntegrationTests | `*` | Marked whole batch and owned cleanup ordering on both native argument variants. |
| Antiphon.Tests / SessionMessageQueueGrokPtyIntegrationTests | `*` | Short single-line receipt now ingests and confirms; multiline/pointer/Enter-only siblings still pass. |
| Antiphon.Tests / SessionQueueReceiptPlumbingTests | `*` | Matching accepted generation reaches all conditional fault/receipt boundaries, complete prompts and separate Enter. |
| Antiphon.Tests / RemoteControlRecoveryPtyIntegrationTests | C514_Idle_modal_clearance_delivers_three_complete_prompts | Interactive Esc clears the actual native modal and all three bodies arrive once. |
| Antiphon.Tests / HerdrAlwaysOnChannelParityTests | AlwaysOn_channel_bound_survives_child_death_and_replies | Three variants: Claude/Herdr refuses; Claude/PtyHost and Grok/Herdr retain successful full recovery/reply coverage. |
| Antiphon.Tests / DispatcherSiblingInspectionSchedulingTests | C1349_InspectionQueueRecovers | Native qualification of the same controlled fairness schedule already green on Linux. |
| Antiphon.Tests / CodexCliObservationGapTests | C1029_Per_kind_receipts | Native Windows cwd and remote POSIX receipts both flow through the recipient; Linux already passed the aggregate. |
| Antiphon.Tests / AgentTaskPipelineEndpointTests | pipeline_route_is_literal_and_returns_the_advisory_contract | Startup import remains isolated on the machine with the previously observed local 3/5/5 policy. |
| Antiphon.Tests / GrokSignInRuntimeTests | A_sign_in_mode_fakegrok_pool_task_never_types_the_brief | Empty GROK_HOME reaches ProviderSignInRequired, zero brief input and AuthenticationRequired; known independent transaction defect below remains a prerequisite. |
| Antiphon.Tests / GrokDelegateEndToEndTests | an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge | Real after-Enter cut, late confirmation and both retained ordering assertions. This new native schedule has compiled but was not executed here. |
| Antiphon.SessionRunner.Tests / CompletedCardWorkspaceRemovalTests | C1032_InspectionHoldShapes; C1017_RunnerRepositoryIdentity | Existing Hidden gitfile replacements succeed. Case-twin row executes only with case-sensitive scratch storage; otherwise its precise capability skip is expected. |
| Antiphon.SessionRunner.Tests / GrokRulesHttpAcceptanceTests | Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled | Preserve the response body and runner log before deleting native outputs; if red, classify the exact operation before any separately reviewed product repair. |

## Product finding and four unresolved mechanisms

The native task `32365cdf` already demonstrated the **Grok sign-in transaction self-block**: the launch-error transaction locks the agent, then awaits a fresh-scope AgentIncidents insert whose FK waits on that lock. Current source still follows that path. It is a confirmed pre-existing product finding, not cured by clock isolation; no new native reproduction or product repair was commissioned/performed here. Keep that independent repair separate and give it a red witness and Final Review. Do not skip or weaken the sign-in test to hide it.

The following remain open exactly as in the native report, with no timer widening, product patch or passing-repeat relabelling:

1. **Native Claude observation refusal** — `HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply` alternates failing Claude/PtyHost launch/resume variants. Capture the exact refused write's session/accepted generation, runtime epoch, dialog state/kind, input-history flag, output sequence/input revision and rendered snapshot. A later complete composer is not the guard's observation.
2. **Runtime-action timeout** — `SessionRuntimeActionTests.C1259_saturation_keeps_ingestion_live_and_recovers_overflow("poison-page")` failed before UI effect 128 in a class run; three isolated repeats passed. Capture last completed sequence, action phase/attempt/due time/failure reason, active worker owners, and DB/phase/scheduling latency under the failing class-load condition. Keep the original page/timeout oracle.
3. **Seat-release timeout** — `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner` timed out before BeforeDispatch in the class run; three isolated runs passed. Capture reservation and verification results plus elapsed phases before `TerminalRunnerSeatReleaseService` reaches its callback. Do not infer a cause from the older non-recurring landing.lock signature.
4. **Shadow-copy rename denial** — native Grok HTTP 500 originated at the `.copying` directory rename in `ShadowCopyStore`; three isolated repeats passed. Capture the failure response/log and a file/handle trace at the rename with owner identity and sharing mode. No ACL, antivirus or competing-holder attribution is established. No body naming `grok_rules_file_write_failed` or `Host launch failed` was obtained by this task, so no conditional product edit was justified.

Evidence owners: `origin/feat/card-task-32365cdf:docs/investigations/2026-10-27-native-rerun-terminal-pty-delivery.md`, `origin/feat/card-task-9d3dc152:docs/investigations/2026-10-27-windows-terminal-receipt-static-analysis.md`, and `origin/feat/card-task-49dc3edc:docs/investigations/2026-10-27-windows-grok-codex-qualification-roots.md` were fetched and read. Historical native 393/26/4 and isolated-repeat outcomes remain historical and are not overwritten by this portable 76/0/0.

## Cleanup and landing

Every foreground build/test invocation exited and was joined. No test host executable from this checkout remained in the final process census. All **34** producer-owned ordinary `bin-061d878e` directories were validated, inventoried and removed, including staged native helper subtrees. Fresh enumeration found none remaining. Raw evidence stays gitignored. `git diff --check` and the evidence-history guard passed (0 violations).

`git merge-tree --write-tree origin/rc/2026-10-27 HEAD` is clean against `9f00008facfc48e75555c08df59887019b3f8dfe`. The fetched master is `718bd29f4a1504c64c491d607d31403fcb87dae2`. Its merge-tree has the two pre-existing add/add conflicts in `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (also reproduced against the untouched rc base), plus task/master collisions in `AgentTaskPipelineStatusTests.cs`, `CodexCliRemoteDeliveryFixture.cs`, and `WindowsDeliveryFixtureWitnessTests.cs`. No merge or rebase was performed. The branch is ready for the rc landing handoff, not claimed conflict-free onto master; the required master propagation needs the landing task's reconciliation.

--- next stage ---
next: land
handoff: Land the fixture-only slices onto rc, reconcile their separate propagation to master without rewriting this branch, and commission the listed native reruns. Preserve the four open native mechanisms and the independent Grok sign-in product finding; no product code changed in this task.
artifact: docs/investigations/2026-10-27-terminal-grok-receipt-fixture-repairs.md
