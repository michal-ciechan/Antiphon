# CARD-0889 test design: finite seams, C578 cancellation custody and the frozen roster

Date: 2026-10-09. TestDesign task: `384a715e`. Branch `feat/card-task-384a715e`,
fast-forward-only from `89e4f769c0a15bb417d015dc0efc843e1c1eee34` (inspected source).
Plan under design: `docs/superpowers/plans/2026-10-04-card-0889-verification-seams-plan.md`
at `926896173fbecb06446ea10d8f1969ed684b6a3c` (branch `feat/card-task-6c3e5034`; that
commit, its refresh `2026-10-04-card-0889-grouped-flaky-fixes-test-design.md` and the
grouped fix plan are not ancestors of this branch, so this document carries the
inherited tables verbatim and cites them by that SHA).

Design document only: no production or test code, no build, no test run and no
mutation was performed. `ANTIPHON_TASK_TOKEN` was present in this session; it was not
used because no checkpoint run was executed. A future Code run binds with it.

## Outcome and authority

**Next: code.** The four seams N1-N4 have finite, named assertion paths at the
inspected source once the plan's test-only changes land (reachability per control
below). C578 cancellation custody is reconciled against the ScriptHarness owner that
CARD-0806/CARD-1047 landed after the plan's inspected source: the C# side of the
plan's N3 (caller token, kill/join/drain, fresh cleanup budget, pre-cancel refusal,
retained diagnostics) exists and is tested; this design keeps that code untouched and
scopes N3 to the offline C578 script, its shim, a shared observation fixture and one
new TUnit class of three methods / five results. PC-1..74 are preserved with their IDs,
detecting methods and labels; PC-75..83 are added. The roster is frozen in
`### Checkpoints` as three independently commissionable groups.

Decisions here are engineering choices within the requested test-only scope (D-9..D-13).
No production default, delivery policy or product source changes. Post-land Mutation
is paused: PC-1..83 are specified and pending; none ran.

## Verification design

### Inspection

Bodies read at `89e4f769c` (not names or attributes), with the boundary each maps to:

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `RunnerCodexAdapterSubmitConfirmTests` (all 8 methods, `DriveSendAsync`, `NewAdapter`); `CodexSubmitConfirmation` (full); `ScriptedCodexRunnerClient` (snapshot/Enter/transcript state); `ControlledTimeProvider` (full) | Enter 1 at t=0; polls at 250 ms steps to t=2 s; Enter 2/3/4 at 250/500/750 ms; early completion on old receipt; direct synchronous timer inventory; `AbsentSettle` -> V-8, R-7, N2 |
| `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` (full) and class | Registered 50 ms poll, attempts vs completions, hook placement, System defaults -> V-6, R-5, N4 |
| `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` (full); `ResilienceTestHost.AdvanceAfterAsync`, `CollectingLoggerProvider`, `RetainedCancellationRegistration`, `ScriptHandler`; `ResiliencePipelineCache` OnRetry log sites (`src/Antiphon.Resilience/ResiliencePipelineCache.cs:214,302`) | t=10 attempt cancel, held completion, zero-advance loop, retry observation before delay, absolute t=30 -> V-7, R-6, N1 |
| `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` labels | 3 s / 10 s owner caps -> V-7 (unchanged) |
| `scripts/test-run-checkpoint.ps1` (param block, C487 entry, full C578 section: shim body, `New-C578Case`, `Start-C578Runner`, `Get-C578Child`, `Read-C578Log`, `Wait-C578RunOutput`, `Wait-C578Ready`, `Wait-C578Exit`, `Stop-C578Owned`, `Save-C578Evidence`, three `Test-C578_*`, `C585ExpectedRows`, no-Case loop) | Wall-clock 10 s/5 s waits, polling holds, PID+start identity, wrapper-first interruption, 109 rows -> V-5, R-4, N3 |
| `Scripts/ScriptHarness.cs`, `ScriptHarnessProcess.cs` (full), `LinuxScriptHarnessProcess.cs` (full), `ScriptHarnessProcessFixture.cs` (full), `ScriptHarnessProcessContractTests.cs` (full, 26 results), `ScriptHarnessProcessTests.cs` (full, 11), `RunCheckpointScriptTests.cs` (full, 24); `tests/Antiphon.ScriptHarnessHost/Program.cs` launch argument shape | Caller token -> execution token -> STOP -> private group/job kill -> `ConfirmDeadAsync` -> pipe drain -> dispose under a fresh cleanup budget; `AdditionalArguments` appended after `-ResultsDirectory`; results/control deletion only after clean cleanup -> V-11, R-10, N3 reconciliation |
| Compatibility callers named by the plan (`BuildSlotScriptTests.C589_WrapperRunsUnderLease`, `NightlyVerificationContractTests.C544_DailyValidity`, `ReleaseGateStatusTests.C599_StatusIdentity`) | Unchanged callers of an unchanged helper -> excluded from the roster (D-12) |
| `TestWorkerModes.All` (6 modes), `TestDbFixtureLazyInitializationTests.WorkerMarkers`, `[Arguments]` census of C448 (7), C478 (12), C459 (13); `ScaledTimeProviderTests` (6), `HttpResilienceRegistrationTests` (7), `GrokRulesLaunchRefusalTests` method names | Inherited S4/S3/S7 counts re-verified -> CP-4..12 unchanged |
| `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs` header/optional columns, `n/a` rules, `CP-n` reuse rule | Table schema below is importable (nine required columns + `Serial`, `Environment`) |
| Owners: `docs/testing-and-build.md` (manifest, tool, build slots, Mutation PC execution, ScriptHarness paragraph), `AGENTS.md` | Checkpoint/evidence rules applied below |

Ground truth that changes the plan's N3 wording:

| Plan (at `bfeb335c5`) says | Code at `89e4f769c` does | Design consequence |
|---|---|---|
| `ScriptHarness` creates a 300 s token in C#, passes no signal, disposes `Process` without kill/join. | `ScriptHarnessProcess.RunAsync` links caller token and 300 s budget, owns a private Linux session/group or Windows job, sends STOP, confirms death by accounting, drains both pipes and disposes under a separate 10 s cleanup budget, keeps diagnostics on failure, refuses pre-canceled input before any owner exists. Tested by 26 contract results, 11 process results, 21 Linux and 10 Windows ownership results. | Do not change `ScriptHarness.cs`/`ScriptHarnessProcess.cs`. Reuse the existing overload (`options`, `cancellationToken`, `validate`). The plan's generic child fixture, pre-cancel test, noncooperative generic test and validator test are duplicates of landed tests and are not designed in (D-12). |
| Supply the descriptor through the child's `ProcessStartInfo.Environment`. | The owner helper launches `pwsh -NoProfile -NonInteractive -File <script> -Case <case> -ResultsDirectory <dir> <AdditionalArguments...>`; it exposes no environment seam. | Supply the descriptor as named script parameters through `ScriptHarnessOptions.AdditionalArguments` (`-C889Descriptor <dir>`, `-C889HoldPhase <phase>`, `-C889IgnoreCancel`), bound by `ArgumentList`, never shell text. Paths and a random nonce are not secrets. |
| Ack/journal live under the owned results root. | The harness results directory is deleted by the owner after a clean cleanup, before the test can read it. | The descriptor directory is test-owned (`Path.GetTempPath()/c889-cancel-<nonce>`), passed by argument, read and deleted by the test's own finally. |
| Two-second cooperative grace before kill. | Cleanup starts immediately in the shared `finally`; adding an opt-in grace would alter landed shared code and its contract tests. | Cooperative cancellation is proved with the harness run completing normally (script observes the cancel file, cleans up, exits nonzero with a FAIL row); fallback is proved with the caller token against a deliberately noncooperative tree (D-11). |

### Delivery inventory

No product delivery path changes. Rows marked internal are test IPC; the S4 rows are
carried unchanged from the inherited inventory (refresh `926896173`, "Delivery
inventory"), including the land-matrix recipient-receipt requirements and substitutes.

| Path: producer -> destination | Persistence boundary and durable identity | Recovery | Observable receipt (what counts) | Coverage |
|---|---|---|---|---|
| C# caller token registration -> cancel file -> C578 parent observation cycle and shim hold (internal) | Descriptor directory outside the harness root; `cancel` file carrying the nonce; `phase.ack` and `identity.journal` keyed by nonce+phase+PID+start ticks | Cancel present before subscription: immediate recheck after subscribing. Cancel while held: wakeup -> full reread. Script-side guard (standalone) publishes its own cancel on expiry. | Not the cancel file. Receipt = `phase.ack` written after cleanup facts hold (nonce, phase, `parentObserved=cancel`, `shimExit=130`, `wrapperFirst=true`, `subscriptions=0`) plus the FAIL row and `PASS C578 FailedBuild owned processes exited`, plus journal identities observed not executing. | V-11; PC-75..79, PC-82 |
| Caller token -> owner STOP -> private group/job kill -> accounting -> pipe EOF (fallback, landed) | Owner nonce handshake; journal identities from the script | Ignored cancel file: owner kill/join under the fresh cleanup budget | `OperationCanceledException` whose token is the caller's; both pre-cancel stream sentinels in `ScriptHarnessDiagnostics`; journal identities not executing; results/control directories gone | V-11; PC-80, PC-81; CARD-0806 PC-10..12 remain that card's obligation |
| C578 shim -> wrapper log -> parent observer (internal) | Case root + nonce + phase + PID/start ticks; entry/ready/release/log files survive observation order | Reread after each wakeup; files created before subscription | Ready admitted only with live identity, expected phase/nonce, ready record and both stream markers with nonce fences in the wrapper log; final log exit 37 once, wrapper exit 2, no run entry/log/TRX | V-5, R-4; PC-41..47, PC-69..72, PC-76, PC-83 |
| S4 rows (land worker, notification reconciler -> real queue -> recipient transcript -> receipt save, retirement child) | As inherited | As inherited | Complete UserPrompt at sequence 11 in the intended session read from persistence; never enqueue/Sent/event | V-1..4, R-1..3; PC-15..32, PC-55..68, PC-73, PC-74 |

Substitutes and what they cannot prove: real `pwsh` wrapper, shim and owner processes
prove process/stream/file custody on the host, not a real compiler or live slot broker.
Logical schedules prove the shared decision function; only the real held shim proves
OS event wiring. S1/S5 transcript fakes stay ordinary regression substitutes. No live
provider is called (the Codex hold is irrelevant to this design).

Evidence order for cancellation claims: (1) phase ack file complete with nonce ->
(2) cancel published through the token registration -> (3) harness run terminal
(root exit and both EOFs, or caller OCE) -> (4) assertions on ack/journal/diagnostics.
A step out of this order is not evidence.

### Proves it works now

Each V names the mutation that would make it red (case -> mutation -> red).

- V-1: C448 seven cuts recover with normal dotnet binding | integration, real child + DB/Git | CP-8 `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | 7 pass; PC-15..21, 24, 25, 55..58, 64..68.
- V-2: producer reaches caller after each land-delivery handoff | real queue integration | CP-9 `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | 12 pass with queue row and complete UserPrompt assertions; PC-27..32, 73, 74.
- V-3: thirteen retirement cuts survive child death | integration | CP-10 `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | 13 pass; PC-26, 59..62.
- V-4: eight modes registered and exit before store warmup | census + child lifecycle | CP-11/12 | 1 + 8; PC-22, 23, 63.
- V-5: C578 live hold, late observation, release and actual failure are deterministic | script bridge + full harness | CP-13 `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` (7 labels) and class (24), CP-14 (111 rows) | PC-41..47, 69..72, 76, 83. The logical schedule table below is the content of `C578 c578-child-held-until-observed` and `C578 c578-late-ready-event-accepted`.
- V-6: readiness is one snapshot per decision | adapter unit | CP-1 `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` | at t=0 exactly one completion before the first registered poll, with no hook installed; hook installed only after `snapshot-one-before-first-poll` passes; two attempts/one completion held; two completions after release | PC-1..4.
- V-7: original budget and owner deadlines are exact with a held first attempt | real pipeline, test handler/clock | CP-2 `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` and `HttpResilienceRegistrationTests` | step observed while phase pending with error null and now = started+10 s; terminal `TaskCanceledException`, Sends 1, no `Resilience retry` line for RunnerGet before request two; t=30 absolute | PC-5..10.
- V-8: submit types the body once, presses Enter at most four times and expires honestly | adapter/helper unit | CP-3 `RunnerCodexAdapterSubmitConfirmTests` (8) | direct 250 ms and `AbsentSettle` probes assert on return from `SubmitAsync` before any adapter schedule; finite driver returns outcome + Enter count + terminal virtual time; Enters 2/3/4 at 250/500/750 ms | PC-33..40.
- V-9: Grok composition and Windows refusal separate | CP-5 or CP-6, CP-7 | Linux 3/26, Windows 5/26 | PC-11..14.
- V-10: one source clock drives scaled time | CP-4 `ScaledTimeProviderTests` (6) | PC-48..54.
- V-11: cooperative C578 cancellation reaches each held phase and the owner rescues a noncooperative tree | integration, real pwsh tree | CP-15 `ScriptHarnessCancellationTests` (5 results), CP-16 two existing contract methods (2) | three phases ack cooperatively with no completion receipt; fallback returns the caller's token with both sentinels drained and journal identities dead; registration publishes the nonce once; pre-cancel never creates an owner; invalid inventories stay failures | PC-75..82; CARD-0806 PC-10 and inventory coverage cited, not re-mapped.

### Guards the regression

- R-1: no PowerShell assembly binding or positional resume | CP-8/9 | decisive: `worker-resume-exit`, `worker-typed-resume`.
- R-2: migration cannot transfer fixture custody or leave children | CP-8, 10, 11, 12 | `worker-owned-children-joined`, `retirement-*-owned`.
- R-3: enqueue or Sent is not delivery; recovery does not retype | CP-9 | `land-complete-userprompt-required`, `land-recovery-does-not-retype`.
- R-4: late readiness, early exit or swallowed events cannot produce false green | CP-13/14 | the seven failed-build labels; `C578 c578-child-held-until-observed` logical rows.
- R-5: readiness seam intact | CP-1 | `snapshot-one-before-first-poll` now asserted before the hold exists.
- R-6: no over-advance or fresh-budget substitution; a helper exception cannot preempt the named assertion | CP-2 | `held-completion-keeps-time-at-10` on the observed step record; `cancelled-attempt-is-terminal` on the inspected signal set.
- R-7: no retype, no press after expiry, no old receipt, no System clock | CP-3 | `submit-enter-4-before-deadline` on the captured count (not an awaited fifth signal); `submit-does-not-reuse-old-receipt` on the captured outcome; `submit-poll-uses-clock`/`blind-settle-uses-clock` on the direct inventory first.
- R-8: no generic Linux refusal counted as Windows protection | CP-5/6/7.
- R-9: no wall-time ratio | CP-4.
- R-10: a cancel file is not delivery and a kill is not cooperation; the standalone script cannot wait unbounded; validator and pre-cancel refusal unchanged | CP-15/16 | `harness-cancel-cooperative` (shim exit 130 and `cleanup=cooperative`), `harness-cancel-no-completion-receipt`, `harness-fallback-joins-owned-tree`, `harness-cancel-published`, standalone guard row in the schedule table.

### Reachability audit of N1-N4 (per control)

Read against the current bodies; "today" = `89e4f769c` without the plan's changes.

| Control | Today | After the plan's change | Finite red witness |
|---|---|---|---|
| PC-10 | `cancelled.Task` is completed inside `AdvanceTo` by the retained callback; `while (!phase.IsCompleted)` does not run; no witness. | Phase = `Task.WhenAll(cancelled.Task, releasePhase.Task)`; the loop runs; the step observer publishes (now, boundary, phasePending, error) in `finally` around the existing body and rethrows. | Test races first step vs captured driver completion, asserts step.Error null and step.Now == started+10 s at `held-completion-keeps-time-at-10` before releasing. Mutant +1 ms: error is the invariant exception, now = 10.001 -> red. Helper exception cannot escape first because the driver task is captured, not awaited. |
| PC-7 | Retry of a canceled attempt computes a delay on the held clock; `firstSend.WaitAsync(5 s)` times out (watchdog, not red). | `CollectingLoggerProvider` gains an optional line callback; test subscribes for `Resilience retry` + `Operation=RunnerGet` lines (OnRetry logs before the delay; `ResiliencePipelineCache.cs:214`) and handler entry 2; races with `firstSend`; inspects all completed signals. | `cancelled-attempt-is-terminal` requires terminal `TaskCanceledException`, Sends 1 and no retry line. Mutant: retry line present at virtual t=10 -> red without advancing time. The observer detaches before request two so its legitimate 503 retries cannot contaminate it. |
| PC-5/6/8/9 | Timer inventory assertions precede pending work. | Unchanged. | Existing finite labels. |
| PC-33 | `DriveSendAsync` awaits `enters[4]`; with two extra Enters it waits 5 s (watchdog). | Driver races captured send completion / next create-or-change timer / next Enter; advances only the next registered deadline <= 2 s; bound of eight polls and one settle; returns outcome and counts. | `client.Enters.ShouldBe(4, "submit-enter-4-before-deadline")` reads 3 -> red after the captured `PromptDeliveryException`. |
| PC-38 | Early success leaves the driver waiting for a 250 ms timer; red surfaces as `submit-poll-uses-clock` after 5 s (wrong label). | Driver returns the successful outcome immediately. | `submit-does-not-reuse-old-receipt` on `outcome is PromptDeliveryException` -> red. |
| PC-39/40 | Direct probes sit after the adapter schedule, which a System-clock mutant reaches first. | Direct probes move to the start of their methods; `SubmitAsync` registers the fake timer synchronously before returning its pending task. | `directClock.Events` lacks the 250 ms (PC-39) or `AbsentSettle` (PC-40) record -> red before any adapter schedule. Cancel/drain in finally. |
| PC-37, 34, 35, 36 | Finite today. | Retained verbatim (cutoff clock schedule, body-once, two looks, defaults). | Existing labels. |
| PC-1 | Hook installed at construction blocks attempt two; mutant blocks before the first poll; red arrives as a 5 s watchdog on the right label. | No hook at construction; observe the registered 50 ms poll without advancing; assert completions == 1; then install the attempt-two hook and advance. | `snapshot-one-before-first-poll` reads 2 -> red immediately. |
| PC-2/3/4 | Finite today. | Hook placement only; race and release unchanged. | Existing labels. |
| PC-41, 42, 69, 70, 71 | Shared decision function does not exist; waits are wall-clock polls. | One decision function and one observation cycle in `scripts/fixtures/c889-c578-observation.ps1`, dot-sourced by the harness and the shim; logical schedules run before the real shim. | Schedule table rows below; each row is one flipped input with a hand-written expected decision. |
| PC-43 | Missing stderr marker produces a 10 s ready timeout, then the label fails late. | Nonce fences after each marker; both fences observed -> MarkersMissing is terminal and finite. | `C578 FailedBuild retains stdout and stderr` red after a finite MarkersMissing diagnostic, child released and drained. |
| PC-47, 72 | Inner cleanup is followed by the same function's return. | Inner cleanup predicate inspected at the label before the C# owner's rescue; subscription set reported. | `C578 FailedBuild owned processes exited` red; owner rescue kills/joins/disposes afterwards. |

Logical schedule table (inputs to the shared decision function; one flip per row from
the accepted baseline; executed inside `Test-C578_FailedBuildKeepsLogAndExit` before the
real shim run; aggregated into the two new labels; cycle end is a test input, not a
fabricated filesystem event):

| Row | Baseline flipped | Expected decision | Mutation -> red |
|---|---|---|---|
| L-1 | none (live identity, expected phase/nonce, ready, both markers and fences) | ObservedReady | PC-42: elapsed >= 10 s rejection restored -> Pending at logical +11 s |
| L-2 | child exited before observation | PrematureExit (held label fails if treated as ready) | PC-41: hold disabled -> ObservedReady on an exited child |
| L-3 | entry PID/start differs from journal | IdentityMismatch | PC-69: identity check bypassed -> ObservedReady |
| L-4 | files complete before subscription, explicit cycle end, no later event | ObservedReady (`c578-late-ready-event-accepted`) | PC-70: immediate recheck omitted -> Pending |
| L-5 | release present before subscription | Released | PC-71: release recheck omitted -> Pending |
| L-6 | stderr marker present, stderr fence absent | Pending | PC-43 variant: fence ignored -> ObservedReady (covered by the real PC-43 red) |
| L-7 | both fences present, stderr marker absent | MarkersMissing | PC-43: missing text produces a finite terminal decision |
| L-8 | cancel file present together with ready and markers | Cancelled | PC-76: ready checked before cancel -> ObservedReady |
| L-9 | standalone descriptor, monotonic elapsed >= 300 s injected | Cancelled (reason guard) | PC-83: guard check removed -> Pending |
| L-10 | partial unreadable record, nothing terminal | Pending | fail-closed whitelist: any new positive condition must be listed; unknown -> Pending |

Decisions: Pending, ObservedReady, Released, Cancelled, PrematureExit, IdentityMismatch,
MarkersMissing. Logical elapsed time is diagnostic except for L-9's guard, which reads a
`Stopwatch`, not `Get-Date`. The fixture stays ASCII.

### Decisions added by TestDesign

- **D-9 Keep the landed owner untouched.** N3's C# scope is the new test class only.
  Rejected: an opt-in grace in `ScriptHarnessProcess.RunAsync` (changes 26 contract
  results' timing contract), environment seams in the owner helper, a second validator.
- **D-10 Descriptor by named arguments, test-owned directory.** `-C889Descriptor`,
  `-C889HoldPhase before-ready|partial-log|release-held`, `-C889IgnoreCancel` on
  `scripts/test-run-checkpoint.ps1`; absent descriptor -> `New-C889Descriptor` makes a
  standalone one under the case root with a monotonic 300 s guard (CP-14 path).
- **D-11 Two observations of cancellation, kept apart.** Cooperative: the test publishes
  the cancel file through the same `Register(token)` helper on a test-local token; the
  harness run ends normally and is judged by the test's own validator (nonzero exit and
  FAIL row expected). Fallback: the harness caller token is canceled against
  `-C889IgnoreCancel`; the owner rescues. A fallback cannot counterfeit cooperation
  because the ack carries `cleanup=cooperative` and `shimExit=130` only on the first path.
- **D-12 No duplicates of landed tests.** `Precancelled_harness_does_not_launch`,
  `Noncooperative_harness_cancellation_joins_process_and_drains_streams`,
  `Harness_result_validation_preserves_exit_and_inventory_failures` and
  `scripts/fixtures/c889-script-harness-child.ps1` are not designed in:
  `ScriptHarnessProcessContractTests.Caller_cancellation_preserves_token`,
  `ScriptHarnessProcessTests.Caller_cancellation_kills_tree_before_returning`,
  `ScriptHarnessProcessContractTests.Inventory_failures_remain_failures` and
  `ScriptHarnessProcessTests.Bad_pass_inventory_still_fails` already assert them with
  real or faked owners. The two Unit methods join the roster as CP-16. The plan's three
  compatibility rows (CP-16..18 there) are dropped: their callers and helper are unchanged.
- **D-13 Three After groups.** `A` = N1+N2+N4 (three test files); `C` = S4 worker
  migration; `B` = N3+S6 (script, fixture, bridge, new class). Each has its own build row
  so a slice can be commissioned alone; reuse is within a group only. Final landing
  qualification runs the rows at the landed source as Review decides.

### New test class

`tests/Antiphon.Tests/Scripts/ScriptHarnessCancellationTests.cs`, `[Category("Integration")]`,
`[ParallelLimiter<ProcessSpawnLimit>]`. Three methods, five results. Each runs the real
`test-run-checkpoint.ps1 -Case C578_FailedBuildKeepsLogAndExit` through
`ScriptHarnessProcess.RunAsync` with `ScriptHarnessOptions.Default` plus `AdditionalArguments`,
and its own `validate`. Finally: delete the descriptor directory; observed identities are
asserted dead before any emergency stop; a rescue that had to act fails the test.

| Method and literal arguments | Results | Evidence order and detecting labels |
|---|---:|---|
| `C578_cancellation_reaches_the_held_phase_and_joins_owned_processes("before-ready" \| "partial-log" \| "release-held")` | 3 | wait `phase.ack` with nonce+phase (`harness-cancel-reaches-phase`) -> `Register(testToken)`, cancel -> run completes normally -> `harness-cancel-cooperative`: ack `parentObserved=cancel`, `shimExit=130`, `cleanup=cooperative`, `descriptor=caller`; `harness-cancel-no-completion-receipt`: build.log has no `DOTNET build EXIT CODE:` line, stdout has no `CHECKPOINT CP-1 EXIT CODE:` line, no run entry/log/TRX, ack `wrapperFirst=true`; stdout has `FAIL C578 FailedBuild cancelled` and `PASS C578 FailedBuild owned processes exited`; `harness-both-pipes-drained`: both nonce sentinels in result stdout/stderr; `harness-subscriptions-disposed`: ack `subscriptions=0`; journal identities not executing. |
| `C578_cancellation_falls_back_to_outer_owned_tree_cleanup()` | 1 | `-C889HoldPhase before-ready -C889IgnoreCancel`; wait ack -> cancel the harness caller token -> `OperationCanceledException` with that token (`harness-fallback-joins-owned-tree`); journal names wrapper and shim with distinct PIDs; each identity not executing; no `cleanup=cooperative` in ack; `harness-both-pipes-drained`: both pre-cancel sentinels present in `Data["ScriptHarnessDiagnostics"]`; results/control directories absent. |
| `C578_token_registration_publishes_a_nonce_cancel_file_once()` | 1 | No process. `Register(token)`: no file before cancel; after cancel exactly one file whose content is the descriptor nonce; a second registration/cancel leaves content unchanged; disposing the registration before deleting the directory succeeds (`harness-cancel-published`). |

Script contract additions (ASCII): shim hold points for the three phases observe the
cancel file as well as the release file through the shared cycle and exit 130 on
cancellation; the parent writes `identity.journal` right after `Process.Start` and after
each entry record; on cancel it stops the wrapper first, then the shim, drains, disposes
subscriptions, writes `phase.ack` last, emits one FAIL row for the case and the retained
cleanup PASS row. Label contract: five failed-build labels verbatim plus exactly
`C578 c578-child-held-until-observed` and `C578 c578-late-ready-event-accepted`;
failed-build `expectedRows` 5 -> 7; `C585ExpectedRows` `62 + 28 + 19` -> `62 + 28 + 21`
(109 -> 111). Cancellation runs occur only with a caller descriptor, so the no-Case
inventory is unchanged at 111.

### Guard inventory

Inherited G-1..74 (verbatim from the refresh at `926896173`), then G-75..86.

| Guard | Plan reference and safety invariant | Positive control |
|---|---|---|
| G-1 | S1/D-2: exactly one completed snapshot before first poll | PC-1 |
| G-2 | S1/D-2: decision two attempts a fresh snapshot while completion remains held | PC-2 |
| G-3 | S1/D-2: exactly two completed decision reads after release | PC-3 |
| G-4 | S1/D-2: omitted/null readiness clock defaults to System | PC-4 |
| G-5 | S2/D-2: first attempt cancels at 10 seconds, not later | PC-5 |
| G-6 | S2/D-2: second request consumes original absolute 30-second budget | PC-6 |
| G-7 | S2/D-2: canceled first attempt does not retry | PC-7 |
| G-8 | S2/D-2: runner list cap is 3 seconds | PC-8 |
| G-9 | S2/D-2: git connectivity cap is 10 seconds | PC-9 |
| G-10 | S2/D-6: pending phase cannot advance the boundary driver beyond t=10 | PC-10 |
| G-11 | S3/D-1: unsafe Windows raw argv produces specific refusal | PC-11 |
| G-12 | S3/D-1: refusal creates no session | PC-12 |
| G-13 | S3/D-1: refusal omits prompt sentinel | PC-13 |
| G-14 | S3/D-1: explicit non-Windows payload is admitted | PC-14 |
| G-15 | S4/D-4: ready cut equals requested cut | PC-15 |
| G-16 | S4/D-4: ready cut agrees with independently read durable phase | PC-16 |
| G-17 | S4/D-4: successful resume has zero process exit | PC-17 |
| G-18 | S4/D-4: land request root is owned | PC-18 |
| G-19 | S4/D-4: land ready path is confined to its root | PC-19 |
| G-20 | S4/D-4: land task matches fixture owner | PC-20 |
| G-21 | S4/D-4: land cut belongs to land whitelist | PC-21 |
| G-22 | S4/D-3: land worker marker is registered | PC-22 |
| G-23 | S4/D-4: selected owned worker exits before store initialization | PC-23 |
| G-24 | S4/D-4: child inherits exactly one owned worker marker | PC-24 |
| G-25 | S4/D-4: final cleanup joins captured children | PC-25 |
| G-26 | S4/D-4: retirement component result survives the corresponding handoff | PC-26 |
| G-27 | S4/D-6: receipt-required success cannot bypass absent queue row | PC-27 |
| G-28 | S4/D-6: busy caller receives no new input | PC-28 |
| G-29 | S4/D-6: receipt belongs to intended session | PC-29 |
| G-30 | S4/D-6: head/tail-only receipt is insufficient | PC-30 |
| G-31 | S4/D-6: receipt is newer than committed attempt floor | PC-31 |
| G-32 | S4/D-6: repeated confirmed recovery sends no duplicate input | PC-32 |
| G-33 | S5/D-2: allowed unconfirmed schedule reaches four Enters | PC-33 |
| G-34 | S5/D-2: retry only presses Enter, no body retype | PC-34 |
| G-35 | S5/D-2: blind body decision uses distinct snapshots | PC-35 |
| G-36 | S5/D-2: options null/omitted clock defaults to System | PC-36 |
| G-37 | S5/D-2: global expiry can stop before Enter four | PC-37 |
| G-38 | S5/D-2: transient fetch miss retains previous baseline | PC-38 |
| G-39 | S5/D-2: poll delay uses effective clock | PC-39 |
| G-40 | S5/D-2: blind settle uses effective clock | PC-40 |
| G-41 | S6/D-5: child holds until parent acknowledges live readiness | PC-41 |
| G-42 | S6/D-5/D-6: local ten-second decision is absent | PC-42 |
| G-43 | S6/D-5: both flushed streams reach wrapper log | PC-43 |
| G-44 | S6/D-5: actual failed child exit is 37 exactly once | PC-44 |
| G-45 | S6/D-5: wrapper projects failed build as exit 2 | PC-45 |
| G-46 | S6/D-5: failed build produces no test invocation/results | PC-46 |
| G-47 | S6/D-5: final cleanup joins wrapper and shim | PC-47 |
| G-48 | S7/D-1: UTC applies speed multiplier | PC-48 |
| G-49 | S7/D-1: timestamp applies speed multiplier independently | PC-49 |
| G-50 | S7/D-1: due time is scaled through same source | PC-50 |
| G-51 | S7/D-1: offset Advance does not fire source timers | PC-51 |
| G-52 | S7/D-1: speed-one UTC preserves explicit offset | PC-52 |
| G-53 | S7/D-1: cancellation must not fire before scaled boundary | PC-53 |
| G-54 | S7/D-1: zero speed is refused | PC-54 |
| G-55 | S4/D-4: ready PID is captured child identity independently of cut | PC-55 |
| G-56 | S4/D-4: subscribed ready observation rechecks already-created complete file | PC-56 |
| G-57 | S4/D-4: premature exit becomes captured diagnostic outcome | PC-57 |
| G-58 | S4/D-4: private connection carrier stays child environment only | PC-58 |
| G-59 | S4/D-4: retirement root admission is independently enforced | PC-59 |
| G-60 | S4/D-4: retirement Ready path admission is independently enforced | PC-60 |
| G-61 | S4/D-4: retirement TaskId admission is independently enforced | PC-61 |
| G-62 | S4/D-4: retirement cut whitelist is independent of land whitelist | PC-62 |
| G-63 | S4/D-3: retirement marker is registered independently of land marker | PC-63 |
| G-64 | S4/D-4: resume request changes cut without positional argv mutation | PC-64 |
| G-65 | S4/D-4: stdout is consumed to completion | PC-65 |
| G-66 | S4/D-4: stderr is consumed independently | PC-66 |
| G-67 | S4/D-4: child never disposes parent store/repository | PC-67 |
| G-68 | S4/D-4: intentional crash kill differs from final owned-tree cleanup | PC-68 |
| G-69 | S6/D-5: child identity matches entry, not merely log text | PC-69 |
| G-70 | S6/D-5: ready/log subscription rechecks full current state | PC-70 |
| G-71 | S6/D-5: child release subscriber handles release already present | PC-71 |
| G-72 | S6/D-5: event/watcher registrations are disposed independently of process exit | PC-72 |
| G-73 | S4/D-6: final test verdict reads complete recipient UserPrompt | PC-73 |
| G-74 | S4/D-6: lost enqueue acknowledgment recovers one durable queue identity | PC-74 |
| G-75 | N3/D-4, D-11: token registration publishes the descriptor nonce exactly once | PC-75 |
| G-76 | N3 shared decision: a present cancel record wins over ready/markers | PC-76 |
| G-77 | N3 before-ready phase: held shim observes the cancel and exits cooperatively (130) | PC-77 |
| G-78 | N3 partial-log phase: parent does not admit ready from a partial log while canceled | PC-78 |
| G-79 | N3 release-held/interrupted cleanup: wrapper stops first; no completion receipt after cancel | PC-79 |
| G-80 | N3 fallback: journal names each owned process so the owner's rescue can be audited | PC-80 |
| G-81 | N3: both pipes carry pre-cancel sentinels through drain | PC-81 |
| G-82 | N3 cancel path: subscription set is empty after cleanup | PC-82 |
| G-83 | N3 standalone descriptor: monotonic 300 s guard publishes cancellation, no unbounded wait | PC-83 |
| G-84 | Owner: pre-canceled caller token creates no owner and no child | none: CARD-0806 PC-10, `ScriptHarnessProcessContractTests.Caller_cancellation_preserves_token`; unchanged code, run as CP-16 |
| G-85 | Owner: nonzero exit, missing/extra/wrong PASS label and wrong count stay failures | none: CARD-0806 inventory control, `ScriptHarnessProcessContractTests.Inventory_failures_remain_failures`; unchanged code, run as CP-16 |
| G-86 | Owner: caller cancel kills the private tree and returns the caller's token | none: CARD-0806 PC-10..12, `ScriptHarnessProcessTests.Caller_cancellation_kills_tree_before_returning`; this plan adds PC-80/81 on the C578 tree instead |

Guards 84-86 are landed shared-owner invariants that this plan does not modify; they are
listed so the N3 custody chain is complete, and their mutation remains CARD-0806's
pending SourceLanding obligation. New guards = 9, mapped = 9, missing = 0, duplicate
PC maps = 0 (two PCs may share a detecting method; no two share a PC ID).

### Positive controls

Mutation runs original-green / compiling-defect-red / exact-restore-green after land,
method-scoped, with external evidence. Code runs V/R; Review judges before land. Setup
errors, zero tests, watchdog expiry and unrelated early assertions are not reds. PC-1..74
rows are verbatim from the refresh; their reachability is now supplied by N1-N4 above.

| PC | Break guard by this compiling defect; exact red witness | Exact detecting method | Assertion |
|---|---|---|---|
| PC-1 | Break G-1: Add an awaited second snapshot fetch before first poll registration. N4 lets it complete; completed count 2 must fail expected 1. | `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` | `snapshot-one-before-first-poll` |
| PC-2 | Break G-2: Reuse previous frame on decision two. Existing next-timer/completion race reaches attempts/completions assertion (not absent entry). | `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` | `snapshot-two-held` |
| PC-3 | Break G-3: Add a fetch after read-two release; final completion count 3 fails expected 2. | `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` | `snapshot-one-per-decision` |
| PC-4 | Break G-4: Use a distinct test mutation TimeProvider subclass for constructor null fallback (declare subclass in the same production file; no test assembly reference); reference identity fails. | `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` | `ready-default-is-system` |
| PC-5 | Break G-5: Set fixture attempt timeout to 11; registered deadline fails expected 10 before completion is awaited. | `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` | `attempt-cancel-at-10` |
| PC-6 | Break G-6: Stamp second request with a fresh budget; observed total timer points to 40, not 30. | `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` | `second-request-original-total-deadline` |
| PC-7 | Break G-7: Enable retry of canceled attempt in scratch retry predicate. N1 must race terminal result against next-handler/retry-phase and assert one send/terminal cancellation before issuing request two; a watchdog is not a red. | `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` | `cancelled-attempt-is-terminal` |
| PC-8 | Break G-8: Widen fixture ListTimeoutSeconds to 4; timer inventory fails expected 3 before token completion. | `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` | `runner-owner-cancel-at-3` |
| PC-9 | Break G-9: Widen only the production connectivity owner cap to 11; registered deadline fails expected 10. | `HttpResilienceRegistrationTests.Runner_list_and_git_connectivity_keep_their_short_deadlines` | `git-owner-cancel-at-10` |
| PC-10 | Break G-10: Change actual AdvanceAfterAsync loop Advance(TimeSpan.Zero) to Advance(TimeSpan.FromMilliseconds(1)). N1 holds the passed phase, observes a loop step and captures invariant exception as data; assert no error and now=10 before release. Current source does not deterministically enter this loop. | `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget` | `held-completion-keeps-time-at-10` |
| PC-11 | Break G-11: Keep ConflictException but substitute conflict code; exact code assertion fails on Windows. | `GrokRulesLaunchRefusalTests.Named_grok_herdr_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists` | `windows-unsafe-code` |
| PC-12 | Break G-12: Insert one fixture session at the workspace after correct refusal; preserve code/privacy outcome; census 1 fails expected 0 on Windows. | `GrokRulesLaunchRefusalTests.Named_grok_pty_host_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists` | `windows-no-session` |
| PC-13 | Break G-13: Append only the synthetic sentinel to correct refusal text; required name/flag/reason still pass, exclusion fails on Windows. | `GrokRulesLaunchRefusalTests.Named_grok_herdr_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists` | `windows-private-diagnostic` |
| PC-14 | Break G-14: Change only first explicit isWindows:false argument to true; violation is non-null and existing ShouldBeNull fails. This is a policy-input control, not live Linux launch proof. | `GrokRulesArgvPolicyTests.Any_payload_is_allowed_when_not_windows` | `ValidatePayload(...).ShouldBeNull()` |
| PC-15 | Break G-15: Write another allowed cut into ready JSON while retaining correct PID; ready-cut comparison fails before DB phase check. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-cut-identity` |
| PC-16 | Break G-16: At C03 corrupt the persisted phase to Prepared after legitimate cut and before ready publication; independent observer expects Inspected and fails before resume. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-durable-phase` |
| PC-17 | Break G-17: Run resume normally then exit 7 in selected worker; captured exit comparison fails before subsequent durable assertions. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-resume-exit` |
| PC-18 | Break G-18: Bypass land root admission check; pure validator preflight with otherwise valid fields and wrong root must report rejection, not admitted; no DB/process is opened for invalid probes. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-root-owned` |
| PC-19 | Break G-19: Bypass land Ready-parent check; a one-field outside-root Ready probe returns admitted and fails its rejection assertion. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-ready-confined` |
| PC-20 | Break G-20: Bypass land fixture-owner comparison; only TaskId differs in otherwise owned request; admission must remain false. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-task-owned` |
| PC-21 | Break G-21: Permit unknown land cut in validator; finite admission result fails rejection. Retirement arm is independently PC-62, not hidden in this mapping. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-cut-allowed` |
| PC-22 | Break G-22: Omit LandProtocolCrashWorker from All while keeping literal Marker field; independent reflection census equality fails. | `TestDbFixtureLifecycleTests.Worker_mode_list_names_every_owned_child_worker` | `all-owned-worker-markers-registered` |
| PC-23 | Break G-23: Initialize TestDbFixture lifecycle immediately before RunAndExitAsync. Keep malformed selected payload so child exits finitely; marker-specific never-requested string fails, not database startup. Use controlled lifecycle operations for the probe if required to avoid infrastructure dependency. | `TestDbFixtureLazyInitializationTests.A_worker_child_exits_before_the_shared_store_warmup` | `marker + failed (dbLifecycle=never-requested)` |
| PC-24 | Break G-24: Stop clearing inherited marker keys in ProcessStartInfo; probe seeds all other markers in its copied environment only; pre-start dictionary assertion fails. Never mutate parent environment. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-one-selected-marker` |
| PC-25 | Break G-25: Return from inner cleanup with deliberately held owned child still alive; inspect captured state at assertion; independent outer finally actually terminates/joins it. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-owned-children-joined` |
| PC-26 | Break G-26: At directory-result suppress only committed DirectoryRemoved while preserving ready and eventual resume; label existing before-death component assertion; false fails expected true. Other twelve cut branches retain existing assertions. | `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | `retirement-durable-handoff` |
| PC-27 | Break G-27: Suppress enqueue in fixture, retain RetryPending, advance only explicit due fixture boundary; QueueMessageId required assertion fails; no success-path early return. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-queue-row-required` |
| PC-28 | Break G-28: Bypass working-state eligibility for lost-wakeup,true; preserve active-working fixture; captured Inputs must remain empty before setting idle. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-busy-does-not-submit` |
| PC-29 | Break G-29: Remove session predicate in LandNoteReceipt.Prompts; isolated wrong-session whole-body/fresh receipt is then accepted; assert intended note unconfirmed before valid receipt is introduced. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-receipt-session-identity` |
| PC-30 | Break G-30: Bypass IsCompleteIn in LandNoteReceipt.IsReceipt; seed same-session fresh head+tail with body middle missing; assert unconfirmed before valid whole-body receipt. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-receipt-complete-body` |
| PC-31 | Break G-31: Change sequence > floor to >= floor in matcher; same-session complete receipt at 10 must leave note unconfirmed, then valid 11 closes it. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-receipt-after-floor` |
| PC-32 | Break G-32: Inject second fixture queue submission after a valid receipt during second reconciliation; preserve receipt and compare final Inputs count to pre-reconcile count. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-recovery-does-not-retype` |
| PC-33 | Break G-33: Reduce ExtraEnterAttempts from 3 to 2. N2 drives registered timers/terminal result without awaiting nonexistent Enter four, captures PromptDeliveryException and then fails count 3 vs 4. | `RunnerCodexAdapterSubmitConfirmTests.An_unconfirmed_submit_over_a_live_transcript_throws_prompt_delivery` | `submit-enter-4-before-deadline` |
| PC-34 | Break G-34: Invoke sendLine instead of pressEnter on the retry; second Enter still confirms; BodyWrites=2 fails expected 1. | `RunnerCodexAdapterSubmitConfirmTests.A_folded_first_CR_is_recovered_by_pressing_Enter_again_and_never_by_re_typing` | `submit-body-once` |
| PC-35 | Break G-35: Reuse first screen for second look, leaving settle timer and expected exception; snapshot count 1 fails expected 2. | `RunnerCodexAdapterSubmitConfirmTests.A_blind_first_turn_with_the_body_still_standing_after_every_Enter_throws_composer_may_hold_body` | `blind-body-two-looks` |
| PC-36 | Break G-36: Replace only options fallback with distinct mutation TimeProvider subclass declared in production file; null/omitted reference checks fail; explicit System and immediate confirmed behavior remain controls. | `RunnerCodexAdapterSubmitConfirmTests.A_confirmed_first_CR_costs_no_extra_enters` | `submit-default-is-system` |
| PC-37 | Break G-37: Remove global-deadline term from loop break, retain retry count. Existing internal schedule releases third-read hold at t=2 seconds; fourthEnter completes and ShouldBeFalse fails finitely. | `RunnerCodexAdapterSubmitConfirmTests.An_unconfirmed_submit_over_a_live_transcript_throws_prompt_delivery` | `submit-expired-budget-stops-repress` |
| PC-38 | Break G-38: Reset adapter remembered baseline to zero on missed fetch. N2 observes early send completion; captured outcome is not PromptDeliveryException and fails label. | `RunnerCodexAdapterSubmitConfirmTests.A_transient_fetch_failure_on_a_later_turn_does_not_confirm_against_the_previous_UserPrompt` | `submit-does-not-reuse-old-receipt` |
| PC-39 | Break G-39: Replace only poll delay clock with System. After N2 moves synchronous direct subcase first, fake timer inventory lacks 250 ms timer; assert immediately then cancel/drain. | `RunnerCodexAdapterSubmitConfirmTests.An_unconfirmed_submit_over_a_live_transcript_throws_prompt_delivery` | `submit-poll-uses-clock` |
| PC-40 | Break G-40: Replace only blind settle clock with System. Direct zero-budget subcase first under N2 sees no AbsentSettle fake timer; immediate assertion fails before adapter scheduling. | `RunnerCodexAdapterSubmitConfirmTests.A_blind_first_turn_with_the_body_still_standing_after_every_Enter_throws_composer_may_hold_body` | `blind-settle-uses-clock` |
| PC-41 | Break G-41: Disable hold in actual shared decision used by logical and real wait. Feed child exit before deferred parent observation; finite live-held assertion fails before real-shim phase. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-child-held-until-observed` |
| PC-42 | Break G-42: Restore elapsed>=10 rejection in same decision function called by real wait. Feed Ready at logical +11 seconds with owned live identity; assert accepted, with no real eleven-second sleep. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-late-ready-event-accepted` |
| PC-43 | Break G-43: Omit stderr marker only in shim; controlled marker-phase-complete observation releases and drains child without waiting for missing text; exact final log assertion fails. Normal path still observes both streams while held. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 FailedBuild retains stdout and stderr` |
| PC-44 | Break G-44: Have same shim exit 38 after identical output/hold; wrapper still reports failure; exact 37 log marker count fails. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 FailedBuild records actual exit 37 once` |
| PC-45 | Break G-45: Project captured wrapper outcome as 0 in fixture after actual 37 log is recorded; wrapper-exit assertion fails with retained earlier log assertions. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 FailedBuild returns checkpoint exit 2` |
| PC-46 | Break G-46: Fixture writes run.entry while retaining build exit37/wrapper2; no-run assertion fails; do not force an earlier code failure. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 FailedBuild never invokes tests` |
| PC-47 | Break G-47: Inner cleanup leaves captured shim held; assert live state at cleanup label before unconditional outer rescue releases/joins under N3. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 FailedBuild owned processes exited` |
| PC-48 | Break G-48: Drop UTC multiplier only; +100 ms source gives 100 ms instead of 1 s. | `ScaledTimeProviderTests.Speed_10_advances_ten_times_real_time` | `scaled-utc-100ms-is-1s` |
| PC-49 | Break G-49: Drop timestamp multiplier only; UTC stays correct; timestamp elapsed is 100 ms, not 1 s. | `ScaledTimeProviderTests.Speed_10_advances_ten_times_real_time` | `scaled-timestamp-100ms-is-1s` |
| PC-50 | Break G-50: Forward unscaled dueTime; pending task remains incomplete at source100 ms; finite state assertion fails before await. | `ScaledTimeProviderTests.Delay_on_the_clock_completes_speed_times_sooner` | `scaled-timer-due-at-100ms` |
| PC-51 | Break G-51: In test-helper mutation make Advance also advance supplied FakeTimeProvider; offset checks pass but pending timer completed, failing expected false. | `ScaledTimeProviderTests.Advance_jumps_now_without_firing_a_pending_delay` | `offset-does-not-fire-timer` |
| PC-52 | Break G-52: Omit offset in UTC calculation; expected +31 seconds is absent at frozen source. | `ScaledTimeProviderTests.Speed_one_is_an_offset_clock` | `speed-one-offset-exact` |
| PC-53 | Break G-53: Scale due time twice; source99 ms already has canceled token; current exact before-label ShouldBeFalse fails. At-due and wait-observation labels remain separately inspected in ordinary V/R. | `ScaledTimeProviderTests.CancelAfter_on_the_clock_is_scaled` | `scaled-cancel-at-100ms: before` |
| PC-54 | Break G-54: Remove speed<=0 validation; zero constructor returns and explicit existing exception assertion fails. | `ScaledTimeProviderTests.Non_positive_speed_is_refused` | `Should.Throw<ArgumentOutOfRangeException>(() => new ScaledTimeProvider(0))` |
| PC-55 | Break G-55: Write another PID with correct requested cut; exact PID comparison fails before any kill; never kill that other PID. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-pid-identity` |
| PC-56 | Break G-56: Remove immediate read after subscribing; a controlled pre-subscription complete file plus finite observer-turn acknowledgment yields not-ready and fails expected accepted, without waiting for a missing event. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-ready-recheck` |
| PC-57 | Break G-57: Ignore child-exit observation; feed exited child plus absent ready through actual ready-decision path and explicit completion-of-observation signal; assert early-exit outcome with exit/error, not timeout. Outer cleanup remains independent. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-exit-before-ready` |
| PC-58 | Break G-58: Add synthetic connection sentinel to request/argv serialization; assert absence in argv/JSON/diagnostic captures and presence only at expected child environment key before launch. Never use real connection text as assertion detail. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-connection-env-only` |
| PC-59 | Break G-59: Bypass retirement root validator; one-field invalid-root preflight becomes admitted before connection access and fails rejection. | `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | `retirement-root-owned` |
| PC-60 | Break G-60: Bypass retirement Ready-parent validator; otherwise owned request with outside-root Ready fails specific rejection assertion. | `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | `retirement-ready-confined` |
| PC-61 | Break G-61: Bypass retirement fixture-owner comparison; different TaskId is admitted and fails rejection before connection access. | `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | `retirement-task-owned` |
| PC-62 | Break G-62: Permit unknown retirement cut; include a land-only cut as cross-mode invalid input and assert both are rejected. | `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | `retirement-cut-allowed` |
| PC-63 | Break G-63: Omit only WorktreeRetirementCrashWorker from All; independent literal Marker census still includes it and equality fails. | `TestDbFixtureLifecycleTests.Worker_mode_list_names_every_owned_child_worker` | `all-owned-worker-markers-registered` |
| PC-64 | Break G-64: Construct resume request with original cut rather than resume; inspect serialized typed request before launching, expected resume fails; no second crash worker is left hung. Exercise both modes in the preflight helper. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-typed-resume` |
| PC-65 | Break G-65: Replace captured stdout drain with completed empty-string task. Owned successful child writes a short nonsecret ready diagnostic; after exit assert sentinel survives in captured stdout; keep bounded output so mutant reaches assertion. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-stdout-drained` |
| PC-66 | Break G-66: Replace stderr drain with completed empty-string task; validation-error probe writes known nonsecret failure to stderr and exits; exact captured marker assertion fails, stdout path unchanged. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-stderr-drained` |
| PC-67 | Break G-67: Call child harness DisposeAsync on resume (compiling test-helper defect); fresh parent observer captures missing store/root as data and fails custody assertion before parent teardown. Mutation operates only on uniquely owned test fixture. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-parent-fixture-survives` |
| PC-68 | Break G-68: Change only crash-cut Kill(false) to Kill(true). Fixture owns a bounded held descendant witness; after root death it must still be alive until final cleanup. Separate outer cleanup always terminates/joins witness, including red. | `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | `worker-crash-kills-root-only` |
| PC-69 | Break G-69: Bypass PID/start identity check in shared ready decision; otherwise ready/full-marker record with wrong identity must be rejected in finite subcase, before real shim schedule. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-child-held-until-observed` |
| PC-70 | Break G-70: Omit ready/log immediate state recheck in shared observation path; deliver complete files before subscription and then an explicit observer-cycle-end record, with no subsequent file event; assert accepted snapshot fails finitely. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-late-ready-event-accepted` |
| PC-71 | Break G-71: Omit release immediate recheck; controlled release-before-subscription schedule feeds completion of observation, asserts child released before real shim run. Same release decision must run inside shim, not a parent-only lookalike. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-child-held-until-observed` |
| PC-72 | Break G-72: Suppress subscription disposal in actual owner cleanup; tracked subscription set stays nonempty after joined child, fail cleanup predicate; outer rescue disposes all handles. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 FailedBuild owned processes exited` |
| PC-73 | Break G-73: Suppress only fake recipient UserPrompt publication after actual submitted body (retain queue/event/Sent evidence); finite transcript-source completion acknowledgment leads to fresh DB assertion requiring matching whole-body UserPrompt at11, which fails. Do not wait for timeout or treat Confirmed alone as receipt. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-complete-userprompt-required` |
| PC-74 | Break G-74: During queue-inserted recovery insert a second fixture queue row with a distinct key but same logical source/task/body; fresh count and original QueueMessageId assertion fail before receipt, retaining actual enqueue recovery path. | `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | `land-keyed-recovery-single-row` |
| PC-75 | Break G-75: write `Guid.NewGuid()` instead of the descriptor nonce in `Register`'s callback; content comparison fails. | `ScriptHarnessCancellationTests.C578_token_registration_publishes_a_nonce_cancel_file_once` | `harness-cancel-published` |
| PC-76 | Break G-76: in the shared decision function test ready/markers before the cancel record; L-8 returns ObservedReady, finite logical row fails. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-child-held-until-observed` |
| PC-77 | Break G-77: remove the cancel-file check from the shim's before-ready hold (release-only loop); parent stops the held shim instead; ack `shimExit` is a kill code, not 130. | `ScriptHarnessCancellationTests.C578_cancellation_reaches_the_held_phase_and_joins_owned_processes("before-ready")` | `harness-cancel-cooperative` |
| PC-78 | Break G-78: admit ready on the stdout marker alone while canceled; ack `parentObserved=ready`. | `ScriptHarnessCancellationTests.C578_cancellation_reaches_the_held_phase_and_joins_owned_processes("partial-log")` | `harness-cancel-reaches-phase` |
| PC-79 | Break G-79: on cancel in release-held publish the normal release and join; build.log gains `DOTNET build EXIT CODE: 37` and stdout `CHECKPOINT CP-1 EXIT CODE: 2`. | `ScriptHarnessCancellationTests.C578_cancellation_reaches_the_held_phase_and_joins_owned_processes("release-held")` | `harness-cancel-no-completion-receipt` |
| PC-80 | Break G-80: journal records only the wrapper identity; "names wrapper and shim with distinct PIDs" fails before the death checks. | `ScriptHarnessCancellationTests.C578_cancellation_falls_back_to_outer_owned_tree_cleanup` | `harness-fallback-joins-owned-tree` |
| PC-81 | Break G-81: shim omits its stderr sentinel line (stdout retained); diagnostics lack the stderr sentinel. | `ScriptHarnessCancellationTests.C578_cancellation_falls_back_to_outer_owned_tree_cleanup` | `harness-both-pipes-drained` |
| PC-82 | Break G-82: skip subscription disposal on the cancel cleanup path only; ack `subscriptions` is nonzero. | `ScriptHarnessCancellationTests.C578_cancellation_reaches_the_held_phase_and_joins_owned_processes("release-held")` | `harness-subscriptions-disposed` |
| PC-83 | Break G-83: remove the standalone guard-expiry branch from the decision; L-9 returns Pending. | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` | `C578 c578-child-held-until-observed` |

#### Method-scoped Mutation selections and phase costs

Inherited rows verbatim (PC-11..13 on Windows; others once on Linux), then the new rows.
Exact filters via the slot-gated driver; `tests/Antiphon.Tests` except PC-14. Phase run
minutes exclude the three-minute isolated build budgeted per control per phase.

| PCs | Exact filter | Executions per phase per PC | Run minutes per phase per PC |
|---|---|---:|---:|
| PC-1, PC-2, PC-3, PC-4 | `/*/*/RunnerCodexAdapterReadyTests/One_snapshot_is_used_for_each_startup_decision` | 1 | 1 |
| PC-5, PC-6, PC-7, PC-10 | `/*/*/ResilienceBudgetTests/Slow_first_attempt_consumes_the_same_budget` | 1 | 1 |
| PC-8, PC-9 | `/*/*/HttpResilienceRegistrationTests/Runner_list_and_git_connectivity_keep_their_short_deadlines` | 1 | 1 |
| PC-11, PC-13 | `/*/*/GrokRulesLaunchRefusalTests/Named_grok_herdr_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists` | 1 | 1 |
| PC-12 | `/*/*/GrokRulesLaunchRefusalTests/Named_grok_pty_host_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists` | 1 | 1 |
| PC-14 | `/*/*/GrokRulesArgvPolicyTests/Any_payload_is_allowed_when_not_windows` | 1 | 1 |
| PC-15, PC-16, PC-17, PC-18, PC-19, PC-20, PC-21, PC-24, PC-25, PC-55, PC-56, PC-57, PC-58, PC-64, PC-65, PC-66, PC-67, PC-68 | `/*/*/AgentTaskLandRecoveryTests/C448_V15_RealWorkerDeathRecoversDurableBoundaries` | 7 | 8 |
| PC-22, PC-63 | `/*/*/TestDbFixtureLifecycleTests/Worker_mode_list_names_every_owned_child_worker` | 1 | 1 |
| PC-23 | `/*/*/TestDbFixtureLazyInitializationTests/A_worker_child_exits_before_the_shared_store_warmup` | 8 | 8 |
| PC-26, PC-59, PC-60, PC-61, PC-62 | `/*/*/WorktreeResidueRecoveryTests/C459_WorkerDeathAtEveryRetirementHandoff` | 13 | 12 |
| PC-27, PC-28, PC-29, PC-30, PC-31, PC-32, PC-73, PC-74 | `/*/*/PostLandMutationDeliveryTests/C478_V09a_LandCrashMatrix` | 12 | 10 |
| PC-33, PC-37, PC-39 | `/*/*/RunnerCodexAdapterSubmitConfirmTests/An_unconfirmed_submit_over_a_live_transcript_throws_prompt_delivery` | 1 | 1 |
| PC-34 | `/*/*/RunnerCodexAdapterSubmitConfirmTests/A_folded_first_CR_is_recovered_by_pressing_Enter_again_and_never_by_re_typing` | 1 | 1 |
| PC-35, PC-40 | `/*/*/RunnerCodexAdapterSubmitConfirmTests/A_blind_first_turn_with_the_body_still_standing_after_every_Enter_throws_composer_may_hold_body` | 1 | 1 |
| PC-36 | `/*/*/RunnerCodexAdapterSubmitConfirmTests/A_confirmed_first_CR_costs_no_extra_enters` | 1 | 1 |
| PC-38 | `/*/*/RunnerCodexAdapterSubmitConfirmTests/A_transient_fetch_failure_on_a_later_turn_does_not_confirm_against_the_previous_UserPrompt` | 1 | 1 |
| PC-41, PC-42, PC-43, PC-44, PC-45, PC-46, PC-47, PC-69, PC-70, PC-71, PC-72 | `/*/*/RunCheckpointScriptTests/C578_FailedBuildKeepsLogAndExit` | 1 | 2 |
| PC-48, PC-49 | `/*/*/ScaledTimeProviderTests/Speed_10_advances_ten_times_real_time` | 1 | 1 |
| PC-50 | `/*/*/ScaledTimeProviderTests/Delay_on_the_clock_completes_speed_times_sooner` | 1 | 1 |
| PC-51 | `/*/*/ScaledTimeProviderTests/Advance_jumps_now_without_firing_a_pending_delay` | 1 | 1 |
| PC-52 | `/*/*/ScaledTimeProviderTests/Speed_one_is_an_offset_clock` | 1 | 1 |
| PC-53 | `/*/*/ScaledTimeProviderTests/CancelAfter_on_the_clock_is_scaled` | 1 | 1 |
| PC-54 | `/*/*/ScaledTimeProviderTests/Non_positive_speed_is_refused` | 1 | 1 |
| PC-75 | `/*/*/ScriptHarnessCancellationTests/C578_token_registration_publishes_a_nonce_cancel_file_once` | 1 | 1 |
| PC-76, PC-83 | `/*/*/RunCheckpointScriptTests/C578_FailedBuildKeepsLogAndExit` | 1 | 2 |
| PC-77, PC-78, PC-79, PC-82 | `/*/*/ScriptHarnessCancellationTests/C578_cancellation_reaches_the_held_phase_and_joins_owned_processes*` | 3 | 2 |
| PC-80, PC-81 | `/*/*/ScriptHarnessCancellationTests/C578_cancellation_falls_back_to_outer_owned_tree_cleanup` | 1 | 1 |

### Out of scope

- No reimplementation of S1/S2/S3/S5/S7, production timing defaults, delivery policy,
  landing/retirement algorithms, DB model, checkpoint driver (`scripts/run-checkpoint.ps1`
  read-only) or provider transport. No change to `ScriptHarness.cs`, `ScriptHarnessProcess.cs`,
  `LinuxScriptHarnessProcess.cs`, `WindowsScriptHarnessProcess.cs` or the owner helper.
- The plan's `scripts/fixtures/c889-script-harness-child.ps1` and the three dropped
  methods (D-12). The plan's compatibility rows for build-slot/nightly/release callers.
- Mutation of CARD-0806/CARD-1047 owner guards (their own pending PCs).
- No live-provider, fleet or Windows-by-inference certificate; Windows rows run on Windows.
- No whole Unit/namespace lane; no 24-burner battery; repeat proof stays at the CARD-0885
  cap and is not required after green.
- Occupancy: the orchestrator re-reads `GET /api/agent-tasks/pipeline`, `/api/session-runners`,
  `/api/runner-defaults`, `/api/hosts` and in-flight Code footprints at dispatch (AGENTS.md
  section 1). Collision-sensitive paths for group B: `scripts/test-run-checkpoint.ps1`,
  `scripts/fixtures/c889-c578-observation.ps1`, `tests/Antiphon.Tests/Scripts/*`;
  group A: the three named test files; group C: the S4 files in the grouped plan.

### Checkpoints

Closed list. One isolated build per group; `CP-n` reuse only within the same `After`.
Groups: `A` = N1+N2+N4 committed; `C` = S4 committed; `B` = N3+S6 committed. Lane is in
Group/Expect; the importer does not filter by OS: Linux selects all rows except CP-6,
Windows all except CP-5. CP-5 and CP-16 method ORs keep a trailing wildcard on each
name; the bare names select zero tests. Counts are TUnit executed results; internal labels, rows and
schedules are stated in Expect only.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | A | `tests/Antiphon.Tests -> bin-c889-a/` | both-ready | `/*/*/RunnerCodexAdapterReadyTests*/*` | V-6,R-5 | Both OSes: 12, 0 failed/skipped | 12 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | A | CP-1 | both-resilience | `/*/*/(ResilienceBudgetTests*)\|(HttpResilienceRegistrationTests*)/*` | V-7,R-6 | Both OSes: 14, 0 failed/skipped | 14 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | A | CP-1 | both-submit | `/*/*/RunnerCodexAdapterSubmitConfirmTests*/*` | V-8,R-7 | Both OSes: 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | A | CP-1 | both-scaled | `/*/*/ScaledTimeProviderTests*/*` | V-10,R-9 | Both OSes: 6, 0 failed/skipped | 6 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | A | CP-1 | linux-grok-launch | `/*/*/GrokRulesLaunchRefusalTests/(Cold_grok_delegate_keeps_full_composed_bundles_in_typed_payload*)\|(Named_grok_agent_with_a_single_line_append_composes_the_rendered_line_byte_identical*)\|(Over_budget_single_line_composition_still_throws_invalid_operation*)` | V-9,R-8 | Linux: 3, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | A | CP-1 | windows-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/*` | V-9,R-8 | Windows: 5, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | A | `tests/Antiphon.SessionRunner.Tests -> bin-c889-policy/` | both-grok-policy | `/*/*/GrokRulesArgvPolicyTests*/*` | V-9,R-8 | Both OSes: 26, 0 failed/skipped | 26 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | C | `tests/Antiphon.Tests -> bin-c889-c/` | both-land-worker | `/*/*/AgentTaskLandRecoveryTests*/C448_V15_RealWorkerDeathRecoversDurableBoundaries` | V-1,R-1,R-2 | Both OSes: 7 cuts, 0 failed/skipped | 7 | 11 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | C | CP-8 | both-land-delivery | `/*/*/PostLandMutationDeliveryTests*/C478_V09a_LandCrashMatrix` | V-2,R-1,R-3 | Both OSes: 12 with queue-row and complete-UserPrompt assertions, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | C | CP-8 | both-retirement | `/*/*/WorktreeResidueRecoveryTests*/C459_WorkerDeathAtEveryRetirementHandoff` | V-3,R-2 | Both OSes: 13 cuts, 0 failed/skipped | 13 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | C | CP-8 | both-worker-registry | `/*/*/TestDbFixtureLifecycleTests*/Worker_mode_list_names_every_owned_child_worker` | V-4,R-2 | Both OSes: 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | C | CP-8 | both-worker-warmup | `/*/*/TestDbFixtureLazyInitializationTests*/A_worker_child_exits_before_the_shared_store_warmup` | V-4,R-2 | Both OSes: 8 after S4 (6 today), 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | B | `tests/Antiphon.Tests -> bin-c889-b/` | both-script-bridge | `/*/*/RunCheckpointScriptTests*/*` | V-5,R-4 | Both OSes: 24, 0 failed/skipped; internal C578 labels 8/7/6 | 24 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | B | n/a | both-script-full-inventory | `pwsh -NoProfile -File scripts/test-run-checkpoint.ps1` | V-5,R-4 | Both OSes: `C487: 111 passed, 0 failed, 111 rows` and `C487 HARNESS EXIT CODE: 0` | n/a | 5 | true | n/a |
| CP-15 | B | CP-13 | both-harness-cancellation | `/*/*/ScriptHarnessCancellationTests/*` | V-11,R-10 | Both OSes: 5 (3+1+1), 0 failed/skipped, no owned residue | 5 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | B | CP-13 | both-harness-owner-compat | `/*/*/ScriptHarnessProcessContractTests/(Caller_cancellation_preserves_token*)\|(Inventory_failures_remain_failures*)` | V-11,R-10 | Both OSes: 2, 0 failed/skipped | 2 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Census at `89e4f769c`: readiness 10 methods/12 results; resilience 7+7; submit 8; scaled 6;
Grok launch 3 Linux-applicable of 5 (two Windows-only), policy 26; C448 selects 7, delivery
12, retirement 13, registry 1, warmup 6 today -> 8 after S4; script bridge 24; new class 5;
contract selection 2. Roster: **Linux 141, Windows 143** TUnit results plus 111 CP-14 rows
per OS. Group sums: A 23, C 43, B 20 minutes; literal table 86.

Run each group once per OS with `dotnet run --project tools/Antiphon.Checkpoints -- run
--plan <this file> --after <A|B|C> --expected-source-sha <committed sha>`, `wait` through
exit 75, no source edits during a run, bootstrap builds through `scripts/build-slot.ps1`,
slot exit 4 reported not bypassed. Preserve unedited CHECKPOINT lines and exact OS/SHA
provenance; run `scripts/check-evidence-diff.ps1` over the full task range. A red row is
diagnosed at the same exact method on the recorded base before any attribution; no
timeout or assertion is widened.

### Cost

All values are planning estimates; this task ran zero builds, tests or mutations.

- Ordinary V/R floor (Code) = sum of `EstimatedMinutes` = **86 minutes literal**; one OS
  lane drops CP-5 or CP-6 (3) -> **83 minutes per OS, 166 minutes for both**. Builds per
  OS: 3 isolated `Antiphon.Tests` builds (groups A, C, B at 3 minutes each) and one
  SessionRunner.Tests build (2) = 11 minutes; execution 72. The plan's single-build table
  was 80 per OS; the three-group shape adds 6 build minutes per OS and buys independent
  commissioning, and drops 3 compatibility minutes, so net +3 per OS.
- PC floor (Mutation), inherited: **1,775 minutes** for PC-1..74 as computed in the refresh
  (337 executions / 345 run minutes per phase x 3 phases = 1,035; builds 74 x 3 x 3 = 666;
  apply/restore/report 74). New PC-75..83: per phase 17 executions / 15 run minutes
  (1 + 2 + 4 x 2 + 2 x 1 + 2) -> 51 executions / 45 run minutes over three phases; builds
  9 x 3 x 3 = 81; apply/restore/report 9 x 1 = 9. New PC floor = **135 minutes**.
  Total PC floor = **1,910 minutes**; PC-11..13 on Windows, the rest once on Linux.
- Setup allowance = 10 minutes (tool build plus environment check on each OS).
- Total = setup 10 + ordinary 166 + Mutation 1,910 = **2,086 minutes (34.8 hours)**,
  excluding authoring (plan estimate 4-8 engineer hours for N1-N4 and S6 test work, plus
  S4), retries and slot waits.
- Savings versus the plan's proposal: 6 process-spawning results per OS (eleven proposed
  -> five) and their would-be PC cycles, two new fixture files -> one, zero shared C#
  changes to the owner (no re-qualification of 68 landed owner results), and 3 ordinary
  minutes per OS from the dropped compatibility rows. Batching discount for Mutation is
  budgeted zero: SourceLanding custody keeps each control isolated.

#### Handoff audit

Bodies read: listed in Inspection. Guards = 86 listed (74 inherited + 9 new mapped + 3
landed-owner invariants justified to existing CARD-0806 controls); new mapped = 9,
missing = 0, duplicate PC maps = 0. All 83 PCs executable after the named slices land:
PC-1 after A (N4), PC-7/10 after A (N1), PC-33/38/39/40 after A (N2), PC-41..47/69..72 and
PC-76/83 after B, PC-75/77..82 after B, PC-15..32/55..68/73/74 after C. Cost is numeric.
No placeholders.

## Code handoff

First slice: group **A** (N2 in `RunnerCodexAdapterSubmitConfirmTests.cs`, N4 in
`RunnerCodexAdapterReadyTests.cs`, N1 in `ResilienceTestSupport.cs` and
`ResilienceBudgetTests.cs`), rows CP-1..7, `checkpoints:` pointing at this file's
`### Checkpoints` with `--after A`. Groups B and C may run in parallel in their own
worktrees if their paths do not collide with in-flight Code (re-read occupancy first).
Code brief line: `checkpoints: docs/superpowers/plans/2026-10-09-card-0889-test-design.md@<this commit> section "### Checkpoints"`.
