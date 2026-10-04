# CARD-0889 verification refresh: worker migration and C578 handshake

Date: 2026-10-04. TestDesign task: `c916f049-bd8a-4927-a1c4-ba9704b9b68d`.
Inspected source: `cd07828f63726c62532a597fbe3ff7650eeb0323`.

Continuation: the [N1-N4 Plan amendment](2026-10-04-card-0889-verification-seams-plan.md)
answers this document's Plan handoff with test-only implementation decisions and
an explicit shared-harness roster delta. Return to **TestDesign** to confirm it;
the historical refusal and 74-control inventory below remain the record at the
inspected source, not an executable certificate for the proposed changes.

This companion appends verification to the unchanged [fix plan](2026-10-04-card-0889-grouped-flaky-fixes-plan.md)
(`a06b4b242f6959b9f381fc66c100e8f85a02eeab`). It preserves S1–S7 and all
54 outstanding controls from the [original TestDesign](2026-10-02-flaky-test-fixes-plan.md).
The [reconciliation](../../investigations/2026-10-04-card-0889-reconcile.md)
is the source of historical publication/platform facts. No production code, test,
build, mutation, deployment or card write was performed here.

**Next: Plan, for four verification seams N1–N4 below.** The current implementation
scope does not supply the finite driver witness, submit-driver failure observation,
or cancellation propagation or readiness-barrier placement that the promised controls require. Do not commission
Code from the old proposed table or treat this document's complete inventory as
an executable-source certificate. The designs below specify concrete outcomes and
mutations, but their missing setup must be incorporated into the implementation
plan before a Code handoff. This is an engineering scope correction, not a request
for a human preference or permission to change production behavior.

## Verification design

### Inspection

Bodies read, not just names or attributes:

| Bodies read | Boundaries mapped |
|---|---|
| `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries`, all seven argument branches; `LandingSafetyHarness.RunCrashWorkerAsync`, service creation/restart and fixture initialization; `LandingGitFixture` ownership/initialization | Worker launch, durable cut, root-only death, typed resume, parent lifetime -> V-1, R-1, R-2 |
| `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix`, `LandCrashAsync`, `PublicationCommitCrashAsync`, `ConfirmLandReceiptAsync`, `DeliveryCut`; adjacent producer/queue receipt methods | Publication, enqueue failure/ack loss, wakeup, busy/eligible recipient, receipt persistence and replay -> V-2, R-3 |
| `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff`, `StartRetirementWorker`, `RestartRetirement`; `LandingSafetyHarness.RunRetirementCrashWorkerAsync` | Thirteen retirement cuts and parent-owned restart -> V-3, R-2 |
| `TestWorkerModes` full body; `TestDbFixture.InitializeAsync`, explicit options and disposal; `SharedStoreWarmup` selection entry points; lifecycle registry census and lazy initialization worker method/launcher | Dispatch before store warmup, marker census, process exits and no child tests -> V-4, R-2 |
| Nearest fixtures for the three new helper files: `LandingRemovalCrashWorker` full body and `CheckCompactionFixture.Start`/`DrainAsync` | Existing typed request, owned-root validation and dotnet binding precedent -> V-1, V-3. Their polling implementation is not copied. |
| C578 shim, `New-C578Case`, `Start-C578Runner`, `Get-C578Child`, ready/log/exit waits, owned cleanup, evidence writer, all three C578 cases; complete `RunCheckpointScriptTests` and `Scripts/ScriptHarness` | Stream flush is earlier than wrapper-log observation; PID/start, event order, failed exit and cleanup -> V-5, R-4, N3 |
| `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision`, `ScriptedCodexRunnerClient` snapshot/Enter state; `ControlledTimeProvider` full body | Timer create/change, attempted vs completed reads, System defaults -> V-6, R-5 |
| `ResilienceBudgetTests.Slow_first_attempt_consumes_the_same_budget`, short-owner method in `HttpResilienceRegistrationTests`, `ResilienceTestHost.AdvanceAfterAsync`, retained cancellation registration | Before/at 10/30/3/10 seconds and held completion -> V-7, R-6, N1 |
| All eight `RunnerCodexAdapterSubmitConfirmTests` bodies and `DriveSendAsync`; `CodexSubmitConfirmation` full body | Enter/body/default/receipt/blind boundaries and driver preemption -> V-8, R-7, N2 |
| Complete `GrokRulesLaunchRefusalTests`, `GrokRulesArgvPolicyTests`, `ScaledTimeProvider` and its six tests | Windows-only refusal, portable policy, shared source UTC/timestamp/timer -> V-9, V-10, R-8, R-9 |
| `BridgeQueueHarness` registration, both OnSubmitted callbacks, transcript insertion and runner fake; notifier enqueue/recovery/receipt body and `LandNoteReceipt` full body | Actual queue vs fake provider, auto-generated receipt masking, session/body/floor -> V-2, R-3 |

Read owners: `docs/testing-and-build.md` (manifest, slots, provenance, Mutation,
delivery), `docs/project-context.md`, `docs/resilience.md`,
`docs/session-runtime-invariants.md` (receipt/lifecycle invariants),
`docs/orchestration-loop.md` (delegate, stage and landing contracts), and
`docs/ops-http.md` (supported scoped read routes).

#### Preserved qualification ledger

All five groups are published and report-backed ordinary-qualified on both OSes.
These are historical reports, not raw receipts independently revalidated here.
None is a certificate at today's HEAD or at an unrelated publication SHA.

| Groups | Tested source and reports | Publication | Limits still carried |
|---|---|---|---|
| S1/S2/S5 | Linux `a8a8bec33741a11c625b41b5db4c233cbd802ab1`, Code `f0251152`, Review `ed5dcb00`: readiness 12, resilience 14, submit 8, zero failed/skipped. Windows `f20e44f267c6cbcdc73db2dea5f70b9d24028577`, `a85cc266`: 12/14/8 plus resilience 14 x3 and submit 8. Repaired loaded S2 `24a51e38`: branch and base 14/14 x3. | `c345371e2aa0ed5c127b27f71d3199a29421122f`, operation `6b1670e6-6acf-43b8-ad69-201c67586148` | f20e44f2 and a8a8bec3 whole trees equal; landing whole tree differs. Earlier failing 42b75f7a/7f51f2e5 evidence is not repaired-source qualification. PC-1..10 and PC-33..40 pending. |
| S3 | Linux Code `cab6a109` / Review `e21bd32f` at `dabf9f20da15e14d2d61a14a6311a12cd5f4769a`: launch 3 passed/2 policy skips, portable policy 26. Windows `4646e932` at that same SHA: launch 5, policy 26; both named refusal methods 1 each. | `6cff76eae6326654794ed68d6da8564adabcd9f2`, operation `bfb76cbf-0cf9-416d-b4ea-a47921b57896` | Old Linux CP-3/15 zero selections remain missing certificates. Windows PC-labelled runs were unmutated green only. PC-11..14 pending. |
| S7 | Same dabf9f20 source/reports: six helper results on each OS, Linux quiet 180 over 30 ordinals; Review consumers 164. | Same `6cff76eae6326654794ed68d6da8564adabcd9f2` | Loaded CP-19 has no receipt, CARD-0932 owns controller failure. PC-48..54 pending. |

No prior control is accepted or obsolete: **PC-1..54 all pending**. S4 PC-15..32
and S6 PC-41..47 still require the unimplemented workers/handshake. Scratch reds,
Done card status, and a clean ordinary Review never discharge SourceLanding Mutation.
The reconciliation preserves the unedited historical CHECKPOINT lines and task IDs.

#### Exact-path occupancy recheck

Read the whole board-scoped listing (3,159 rows), `/api/agent-tasks/pipeline`,
`/api/session-runners`, `/api/runner-defaults`, and `/api/hosts` at
**2026-10-04 12:33:56 UTC**, then full goals, fetched branch tips and local dirty
path lists for the three in-flight Code tasks. Generated captures are ignored
under `.antiphon/c889-testdesign/`; essential facts are retained here.

| In-flight Code | Fetched tip | Exact observed changed paths relevant to collision decision |
|---|---|---|
| `6141b0b0` CARD-1030 | `9f1cd6d398f572f179c9983b78d52e987b9d6663` | From its continuation base 60ddacf8: only `docs/investigations/2026-10-04-card-1030-code-6141b0b0.md`; its goal is C1008 host fixtures/Unit qualification, not S4/S6. |
| `fdb14a5c` CARD-0892 | `d34058b3d4e4d7da4d44dc1853ccfcccc8763cee` | `tests/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests.cs`, `docs/superpowers/plans/2026-10-01-card-0866-disposal-preview-redaction-plan.md`. |
| `62fc2d31` CARD-0849 | `514878838d279f7b0cdc2830bc04f23cc8bc559b` | `scripts/fixtures/c913-image-contract.sh`, `c913-image-wrapper.ps1`, `c913-receipts.ps1`, `c913-seed-contract.sh`; `tests/Antiphon.Tests/Infrastructure/CodexRunnerImageContractTests.cs`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`. |

Each observed local Code tree was clean. Declared/observed scope fields were null,
so they were not used as proof of disjointness. Intersection of these edited paths
with every S4/S6 path in the fix plan, plus N1–N4's proposed helper/test paths,
was empty. This observation is not a reservation of future edits. CARD-1017,
1013 and 1022 Code were Blocked, not active writers; reactivation needs a new
comparison, especially CARD-1017's later `WorktreeResidueRecoveryTests` work.
CARD-0886 is ready for Code, has no active writer, and still has no implemented
harness reuse; serialize its future landing/script fixture edits with this work.

0788/0885/0888 publication prerequisites remain discharged at the SHAs recorded
by Investigate (4380891c / 80867765 / 829ed78b); 0886's plan publication does not
mean its optimization is implemented. Code stage observed 3 in flight, recommendation
5; no dispatch authority follows from this advisory number. Defaults revision 2
prefers Linux; Linux host 9/10, Windows 0/2, both eligible/accepting, temporary
runner unavailable/draining. Re-read all four fleet routes, board tasks, full goals
and exact pushed/dirty path footprints immediately before Code; use the lower
applicable enforced/policy limit. Do not pin a host to avoid a collision.

#### Missing setup and the required Plan continuation

**N1 — driver control (PC-10).** `AdvanceAfterAsync` is passed `cancelled.Task`,
whose callback can complete inside `AdvanceTo`; the later `releaseFirst` hold is
not that phase. The while-loop need not execute once. Replacing its zero advance
with a positive advance therefore has no deterministic witness. Plan a test-only
step acknowledgment on that actual loop, plus a held phase in the existing
slow-first method. One step must be observed while phase remains pending; capture
its outcome as data, assert no exception and exactly t=10 at
`held-completion-keeps-time-at-10`, then release/drain. Do not mutate the assertion,
a standalone clock, or production budgets. Proposed additional footprint:
`ResilienceTestSupport.cs` and the slow-first method in `ResilienceBudgetTests.cs`.
This completes an old control obligation; it does not reimplement S2.

**N2 — submit-driver reachability (PC-33/38/39/40).** `DriveSendAsync` assumes four
Enter signals and registered fake timers. Lowering retries or accepting the old
prompt can complete the send while the driver waits for an Enter that will never
happen. Ignoring the fake clock can also preempt the direct timer assertion later
in the method. Plan finite observation of send completion/next timer/next Enter,
return captured outcome to the detecting test, and put direct clock-inventory
subcases before the adapter schedule. Keep cancellation/draining in finally.
The existing four-Enter and old-receipt assertion must be the decisive red, never
the five-second watchdog. Proposed footprint: only
`RunnerCodexAdapterSubmitConfirmTests.cs`; no production seam change.

**N3 — cancellation and the actual C578 decision path.** `ScriptHarness` creates
its 300-second token in C#, but passes no cancellation signal to the PowerShell
script. It also disposes Process without kill/join on timeout. The plan's proposed
event wait therefore cannot currently consume the stated outer cancellation.
Plan an owned cancellation signal supplied by the existing outer token to the
C578 script, and outer finally kill/join/drain for the captured harness process
if cooperative cleanup fails. No larger timeout and no new local elapsed-time
readiness decision. Include this scope in `Scripts/ScriptHarness.cs` and the
C578 caller/helper; qualify cancellation before ready, during partial logs and
while release is held. Use one shared decision function from the real wait and
logical schedules; a separately tested reducer does not qualify the real path.
Plan owns the additional shared-helper regression scope before Code.

**N4 — readiness control (PC-1).** The current BeforeSnapshotAsync hook gates
attempt two from construction, whereas the old control requires unexpected reads
to finish before the first poll and installs the second-read hold only afterward.
Adding a second fetch before polling can therefore block behind that gate and
expire the registration watchdog instead of failing the completed-read count.
Plan moving activation of the existing hold until after first-poll observation;
keep attempts/completions distinct and permit unexpected reads to complete. The
only additional path is `RunnerCodexAdapterReadyTests.cs`; production readiness
and the five-group historical qualification remain unchanged.

S4 missing setup is already within D-3/D-4: three new helper files, two registered
modes, independent request validation probes, ready cut/PID checks, error/exit
races, both pipe drains, typed resume and unconditional owned cleanup. S6 must
also make labels consumable: ScriptHarness only counts `PASS C578 ` rows. Emit
exactly `C578 c578-child-held-until-observed` and
`C578 c578-late-ready-event-accepted`; use these full strings in the TUnit bridge.
Do not emit bare lowercase labels. Keep the five existing labels verbatim; update
failed-build expectedRows 5 -> 7 and script-wide `C585ExpectedRows` from
`62 + 28 + 19` to `62 + 28 + 21` (**109 -> 111**). Internal schedule assertions
are aggregated into those seven labels, each emitted once.

### Delivery inventory

No new product delivery path is introduced. S4 changes the process that produces
and recovers a real landing outcome. All its handoffs still require recipient
proof; worker-ready, event, Sent flag, enqueue acknowledgment and notification
Confirmed alone are insufficient.

| Path / producer -> destination | Persistence and durable join | Recovery and observable receipt | Ordinary/control coverage |
|---|---|---|---|
| Land worker -> restarted land service | Parent-owned repository/DB; TaskId + landing operation ID + source SHA + cut. C14 pauses after PublicationConfirmed transaction. | Kill only captured root; resume via normal dotnet host; same operation/publication and durable outcome notification survive. | V-1/V-2; PC-15..17, PC-27, PC-64 |
| Notification reconciler -> real SessionMessageQueueService | notification ID joins SourceLandNotificationId, QueueMessageId, TaskId, ParentSessionId, ContentDigest; one keyed row after insert acknowledgment loss | `before-enqueue` fails before insertion; `queue-inserted` loses acknowledgment after commit. Fresh context/reconciler retries due work and reuses the same keyed row. | V-2; PC-27, PC-74 |
| Durable queue -> busy/already eligible caller adapter | Actual queue checks committed working state, persists delivery attempt and sequence floor before input | `lost-wakeup` uses ordinary flush/turn-end; busy true remains untouched until idle; false is already eligible. Actual adapter submission leads to persisted UserPrompt. | V-2; PC-28, PC-73 |
| Recipient transcript -> notification receipt save | Recipient session, whole normalized delivered body/digest, UserPrompt kind, sequence 11 > attempt floor 10; join back to notification/operation/task | `receipt-before-save` and `post-submit-process` restart receipt reconciliation; repeated recovery must not type again. A fresh DB observer reads the complete matching UserPrompt and final ConfirmingPromptSequence. | V-2/R-3; PC-29..32, PC-73/74 |
| Retirement child -> restarted retirement service (internal worker IPC, not a user message) | TaskId + retirement/attempt IDs + requested cut; ready JSON is only readiness. DB rows remain authoritative. | Thirteen cuts; resume exit, active reservation/intents/component results and terminal state checked before parent teardown. | V-3/R-2; PC-26, PC-59..62, PC-67 |
| C578 shim -> wrapper log -> parent observer (internal test IPC) | Unique fixture root + nonce + phase + PID/start ticks; entry/ready/release/log files survive observation order | Re-read full files after subscription; verify live owned child and both markers before release; final actual exit 37, wrapper exit 2, no run receipt. | V-5/R-4; PC-41..47, PC-69..72 |

Land matrix stays **12 TUnit executions**: the five cuts
`before-enqueue,queue-inserted,lost-wakeup,receipt-before-save,after-receipt` crossed
with busy true/false, plus `publication-commit,false` and `post-submit-process,false`.
The last two omit busy=true because process death and caller eligibility are
independently exercised by the ten paired rows; do not claim a full seven-by-two
matrix. Within `lost-wakeup,false`, add restart substeps just before submission and
after actual adapter input before transcript ingestion. Unknown input stays
unconfirmed and is never retyped; a later complete receipt closes it. These are
internal schedules, not silently added TUnit rows.

**Recipient evidence setup:** disable the harness's default auto-receipt callback
for these selected cases. It otherwise inserts a UserPrompt before the negative
receipt probes. Seed baseline 10 before first input; capture the real queue's
submitted text, then publish that text through the runner fake as sequence 11 and
let runtime catch-up persist it. Re-read attempts/floor from a fresh context;
do not replace real attempt evidence with unconditional Sent assignments in the
successful producer-to-recipient case. For receipt-crash cuts, first exercise
actual queue submission and preserve its captured text before crashing receipt
save. For a deliberately parked uncertain-attempt subcase, seeded Sent is explicitly
a recovery substitute and earns no producer-to-recipient credit.

Replace the `QueueMessageId == null` success-path return with the named queue-row
assertion. Await the explicit retry boundary (or set only the fixture clock/due
instant), require one queue row, then proceed all the way to receipt. Keep temporary
negative entries isolated/removed between subcases: wrong session with fresh whole
body, right session head+tail body, whole body at floor 10, and absent UserPrompt
with only queue/event/Sent evidence. No genuine receipt may preexist a rejection
assertion. Supply valid receipt afterward, observe complete body and sequence 11
in the intended session, then reconcile again and assert no extra input.
`LandNoteReceipt` currently permits QueuedUserPrompt for non-legacy Outcome;
this design does not alter that policy or misclaim its rejection. The test's
stronger delivery verdict still requires the separate complete UserPrompt.

Substitutes: real test PostgreSQL and local bare Git remotes prove persistence and
recovery, not external Git service behavior. BridgeQueueHarness uses the real
queue/reconciler/runtime but fake provider I/O and transcript transport; it proves
the full application handoff to a simulated recipient, not a live provider's
acceptance. C578 uses the actual checkpoint wrapper with fake dotnet/TRX/slot HTTP;
it proves wrapper stream/exit behavior, not a real compiler run or live slot broker.
Logical schedules prove shared decision semantics; only the real held shim proves
OS event/process wiring. S1/S5 transcript fakes remain ordinary regression
substitutes, not new live-delivery evidence.

### Proves it works now

These are required future V/R outcomes, not results of this documentation task.
The proposed exact checkpoint selection follows below; it is gated on N1–N4 Plan
closure. Preserve the five published implementations and their attributed evidence.

- V-1: Normal dotnet binding and durable recovery for all seven C448 cuts | integration, real child + DB/Git | CP-8, `AgentTaskLandRecoveryTests.C448_V15_RealWorkerDeathRecoversDurableBoundaries` | 7 pass; ready cut/PID, expected phase, same operation, typed resume exit 0 and cleanup. C05 preserves operator evidence/refuses interrupted rebase until explicit new request; it is not forced to Complete like the other cuts.
- V-2: Producer reaches caller after every declared land-delivery handoff | real queue integration | CP-9, `PostLandMutationDeliveryTests.C478_V09a_LandCrashMatrix` | all 12 reach required queue and whole UserPrompt assertions, including publication crash. Before/after receipt recovery and busy/eligible schedules are explicit above.
- V-3: All retirement durable cuts survive child death | integration, real child | CP-10, `WorktreeResidueRecoveryTests.C459_WorkerDeathAtEveryRetirementHandoff` | 13 pass, component state appropriate to cut, same active retirement where one existed, captured children joined.
- V-4: Eight modes are registered and exit before shared store warmup | reflection census + child lifecycle | CP-11/12, `TestDbFixtureLifecycleTests.Worker_mode_list_names_every_owned_child_worker`, `TestDbFixtureLazyInitializationTests.A_worker_child_exits_before_the_shared_store_warmup` | 1 + 8 executions; independent literal-marker census equal, malformed child exits 1 with marker-specific `dbLifecycle=never-requested`, zero child tests, no lifecycle file.
- V-5: C578 live hold, delayed observation, release and actual failure are deterministic | script bridge and full offline harness | CP-13/14, `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` plus unchanged class | 24 TUnit results; failed-build seven labels, C578 8/7/6, whole harness 111 rows, zero failures. Logical +11-second record uses the same wait decision as the held real child without sleeping eleven seconds.
- V-6: Readiness remains one snapshot per decision | adapter unit | CP-1, `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision` and class | 12 results; t=0 one completion, t=50 ms two attempts/one completion held, finally exactly two completions and System defaults.
- V-7: Shared original budget and owner deadlines remain exact | real resilience pipeline with test handler/clock | CP-2, two selected classes | 14 results; t=10 cancellation and held outcome, original absolute t=30 total, runner 3/git 10, token false immediately before each boundary.
- V-8: Submit retains body once, limited Enters and honest expiry | adapter/helper unit | CP-3, `RunnerCodexAdapterSubmitConfirmTests` | 8 results; four Enters by 750 ms when allowed, fewer when t=2 s expires, no prior-turn receipt reuse, distinct blind looks and System defaults.
- V-9: Applicable Grok composition and Windows refusal remain separate | application fixture + pure policy | CP-5 or CP-6, plus CP-7 | Linux 3 launch/26 policy, Windows 5/26; refusal code, no session and private diagnostics remain required on Windows.
- V-10: One source clock drives scaled time and timers | helper unit | CP-4, `ScaledTimeProviderTests` | 6 results; exact UTC/timestamp, 99/100 ms timer/cancel boundaries, offset alone does not fire timer, speed one and invalid speed.

### Guards the regression

- R-1: No PowerShell assembly binding or positional resume dependency | C448 method and publication-commit branch of the land matrix | current Assembly.Location plus adjacent deps/runtimeconfig, correct selected marker/cut/PID, resumed exit 0 before durable assertions; 7/12 real cases on both OSes.
- R-2: Migration cannot transfer fixture custody or leave children behind | C448, retirement, marker census and warmup exact methods | invalid-root/task/ready/cut admission fails before connection access; parent data still readable after child exit; independent final cleanup observes owned processes exited and both streams drained.
- R-3: Enqueue or Sent is not delivery; recovery cannot duplicate input | land matrix | queue ID required, one keyed row; wrong-session/incomplete/stale receipt stays unconfirmed; matching complete UserPrompt read from persistence; repeated reconcile leaves input count unchanged.
- R-4: Late readiness, early child exit or swallowed events cannot produce false green | C578 failed-build, streaming and interruption methods and CP-14 | correct live identity and full logs before release, 37/2/no tests after failure; interrupted build has no completed/green receipt; owned processes and subscriptions released.
- R-5: Published readiness/default seam stays intact | CP-1 | existing named snapshot/default assertions, G-1..4.
- R-6: No over-advance/fresh-budget substitution | CP-2 | before/at owner boundaries, shared total deadline, one terminal first attempt and actual held-driver witness, G-5..10.
- R-7: No retype, extra press after expiry, blind single-look or old receipt | CP-3 | G-33..40; N2 ensures the mutant reaches its decisive assertion instead of a missing-event watchdog.
- R-8: No generic Linux refusal counted as Windows protection | CP-5/6/7 | Windows exact policy code/no-session/privacy and explicit non-Windows null policy, G-11..14.
- R-9: No wall-time ratio or mixed source clocks | CP-4 | exact source advances, G-48..54.

### Guard inventory

Scope is the migrated worker/handshake safety surface and the 54 inherited controls.
Unchanged product guards outside these slices remain outside new mutation qualification;
ordinary class coverage is not a claim to mutate every assertion in those classes.
All guards, including currently unexecutable controls, are listed. Independent
land/retirement admission, marker registration, stream drains, identity and event
checks are split; none is justified away. G-n maps only to PC-n.

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

### Positive controls

Mutation runs original-green / compiling-defect-red / exact-restore-green after
land, with source-bound external evidence. Code runs V/R; Review judges ordinary
coverage before land. All 54 old IDs remain pending; PC-55..74 add 20 separately
bypassable migration/handshake checks. No mutation ran in TestDesign.

Each row breaks only its G-n, keeps the detecting assertion unchanged, and must
reach that exact assertion in the named method. Setup/compile errors, zero tests,
early unrelated assertions and watchdog expiry are not reds. Current executable
readiness is explicitly withheld for N1–N4; their controls are specifications,
not claimed executable evidence. Controls sharing a file or method run separately.
An independent outer finally performs real cleanup even when inner cleanup is
the defect. Invalid request probes call the real validator as a pure preflight
before any connection/launch; normal valid requests call that same validator.

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

#### Method-scoped Mutation selections and phase costs

Use the exact class/method filter below (no class or suite PC runs), via the slot-gated
checkpoint script. PC-14 uses `tests/Antiphon.SessionRunner.Tests`; all others use
`tests/Antiphon.Tests`. PC-11..13 require Windows; the remaining 71 controls are
commissioned once on Linux. An optional second-OS battery is not in the floor.
Every phase uses the committed source identity plus recorded mutation, fresh TRX,
and an isolated forward-slash output. A clean restored build is required; stable
dirty red evidence is not a clean ordinary receipt. Phase run estimates below
exclude the separately budgeted three-minute build.

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

### Out of scope

- No reimplementation of S1/S2/S3/S5/S7, production timing defaults, delivery policy,
  landing/retirement algorithms, DB model/migration, checkpoint driver or provider
  transport. N1/N2/N4 concern finite test observations only; N3 concerns test process
  custody. Any product failure exposed by stronger receipt checks is diagnosed
  separately, with the same exact failing method at the recorded base.
- No live-provider or fleet-delivery certificate. The real queue integration uses
  explicitly described recipient substitutes. No production runner, credential
  home, stack restart, rollout, operator setting or messaging action is involved.
- No whole Unit/namespace or historical 30/10-round stress obligation. Extra repeats
  require a named unresolved failure and revised scope; the current 3 normal + 2
  loaded cap does not require any repetitions after green. CARD-0932 controller,
  CARD-0968 tooling follow-ups, CARD-0820 filesystem contention, CARD-0863 Unix NUL,
  broad legacy Pump migration and CARD-0886 optimization remain separate.
- S4 retention/recovery keeps existing cut-specific product assertions, including
  C05's refusal and retirement Partial outcomes. This is not a fresh mutation audit
  of every unchanged landing safety predicate. Readiness does not certify recovery;
  the listed durable-state assertions remain mandatory.

### Checkpoints

This is the concrete ordinary selection to carry into the Plan continuation.
It supersedes the **proposed** ordinary table in the parent only after N1–N4 are
resolved and any additional shared-ScriptHarness scope is enumerated. Do not run
it prematurely as a completed Code contract. No unknown test-count placeholder
is included: the table covers the existing method roster plus the specified
internal schedules and eight worker modes. If Plan needs new test methods for
N3, it must add their exact filters/counts/cost before returning to TestDesign.

`all` means the combined committed S4/S6 source and accepted verification-only
setup. Commit/push each meaningful slice, then freeze this combined source for
one resolved run on each OS. Linux selects CP-1,2,3,4,5,7,8,9,10,11,12,13,14;
Windows replaces CP-5 with CP-6. Group/Expect describe lane; the importer does not
automatically filter by platform. Build reuse is only in the same run, source,
OS and After group with verified provenance. A separately commissioned slice
requires its own explicit build row; no reuse of an output from another run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c889-verified/` | both-ready | `/*/*/RunnerCodexAdapterReadyTests*/*` | V-6,R-5 | Both OSes: 12, 0 failed/skipped | 12 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | all | CP-1 | both-resilience | `/*/*/(ResilienceBudgetTests*)\|(HttpResilienceRegistrationTests*)/*` | V-7,R-6 | Both OSes: 14, 0 failed/skipped | 14 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | all | CP-1 | both-submit | `/*/*/RunnerCodexAdapterSubmitConfirmTests*/*` | V-8,R-7 | Both OSes: 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | all | CP-1 | both-scaled | `/*/*/ScaledTimeProviderTests*/*` | V-10,R-9 | Both OSes: 6, 0 failed/skipped | 6 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | all | CP-1 | linux-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/(Cold_grok_delegate_keeps_full_composed_bundles_in_typed_payload)\|(Named_grok_agent_with_a_single_line_append_composes_the_rendered_line_byte_identical)\|(Over_budget_single_line_composition_still_throws_invalid_operation)` | V-9,R-8 | Linux: 3, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | all | CP-1 | windows-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/*` | V-9,R-8 | Windows: 5, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c889-policy/` | both-grok-policy | `/*/*/GrokRulesArgvPolicyTests*/*` | V-9,R-8 | Both OSes: 26, 0 failed/skipped | 26 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | CP-1 | both-land-worker | `/*/*/AgentTaskLandRecoveryTests*/C448_V15_RealWorkerDeathRecoversDurableBoundaries` | V-1,R-1,R-2 | Both OSes: all 7 cuts, 0 failed/skipped | 7 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | all | CP-1 | both-land-delivery | `/*/*/PostLandMutationDeliveryTests*/C478_V09a_LandCrashMatrix` | V-2,R-1,R-3 | Both OSes: 12, all receipt assertions, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | all | CP-1 | both-retirement | `/*/*/WorktreeResidueRecoveryTests*/C459_WorkerDeathAtEveryRetirementHandoff` | V-3,R-2 | Both OSes: all 13 cuts, 0 failed/skipped | 13 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | all | CP-1 | both-worker-registry | `/*/*/TestDbFixtureLifecycleTests*/Worker_mode_list_names_every_owned_child_worker` | V-4,R-2 | Both OSes: 1 census, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | all | CP-1 | both-worker-warmup | `/*/*/TestDbFixtureLazyInitializationTests*/A_worker_child_exits_before_the_shared_store_warmup` | V-4,R-2 | Both OSes: 8 markers after migration, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | all | CP-1 | both-script-bridge | `/*/*/RunCheckpointScriptTests*/*` | V-5,R-4 | Both OSes: 24, 0 failed/skipped; internal C578 8/7/6 labels | 24 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | all | n/a | both-script-full-inventory | `pwsh -NoProfile -File scripts/test-run-checkpoint.ps1` | V-5,R-4 | Both OSes: 111 passed, 0 failed, 111 rows and C487 HARNESS EXIT CODE: 0 | n/a | 5 | true | n/a |

Source attribute/body census: readiness 10 methods/12 results; resilience 7+7;
submit 8; scaled 6; Grok launch 5, portable policy 26. C448 class 8/22 selects
7; retirement 10/22 selects 13. Land matrix has twelve literal Arguments, not a
14-case Cartesian expansion. Registry selects one; the warmup method expands
`WorkerMarkers()` over six current modes and **eight** after the two additions.
Do not select child-only lazy-lifecycle methods. Script bridge is 24 singleton
methods, **109 current internal rows -> 111 projected**, not 111 TUnit results.
CP-14 additionally checks the script's no-Case expected-row contract, which
CP-13's individual `-Case` invocations do not check.

Final projected ordinary roster: **Linux 134, Windows 136** TUnit executions,
plus **111 internal rows in CP-14 per OS**. Neither those internal rows nor invalid
request/event schedules inflate Min. Linux's two deliberate Windows-policy skips
are excluded by CP-5; a whole-class diagnostic is not its zero-skip certificate.
The union covers V-1..10 and R-1..9 without an unlisted broad run.

After Plan closure: use `tools/Antiphon.Checkpoints` `run --plan` against this
artifact and exact resolved row selection, `--expected-source-sha` set to the
committed source, then `wait --max-wait 50s` through every exit 75. Do not end a
delegate turn with an active run. Tool bootstrap and any other build/test driver
take `scripts/build-slot.ps1`; checkpoint rows acquire their own slots. Slot
timeout 4 is not permission to run unleased. TUnit runs with `dotnet run`, never
`dotnet test`. Process limiter stays assembly-local; no co-scheduling with Pty
or FakeClaude tests. Remove only task-owned alternate outputs through checkpoint
cleanup. Preserve unedited CHECKPOINT lines, exact OS/SHA/build provenance,
roster and counts; validate strict receipts and run the evidence diff guard over
the full Code/Review task range. Windows qualification uses the same final source
and a separately commissioned Windows lane; this mirror cannot supply it.

### Cost

All values are **estimates**, not measured runtime; tests/builds here = 0.
Source authoring and slot waits are additional and not hidden in execution time.
The proposed floor must be revised if Plan adds N3 methods beyond this roster.

- Ordinary V/R floor (Code): literal table sum **75 minutes**; resolve one OS
  alternative to **72 minutes per OS**, **144 minutes total**. CP-1/7 include
  isolated builds (budgeted 3+2 minutes per OS); CP-2..6 and CP-8..13 reuse only
  that run's correct build. CP-14 is 5 minutes per OS for the no-Case contract.
  Execution portion = 134 minutes, ordinary builds = 10; their sum is 144.
- Mutation floor: **74 controls**, of which the original 54 are all carried.
  Exact filters and per-phase run minutes are in the method table. Original
  54 contribute **193 executions / 196 run minutes per phase**; additional
  20 contribute **144 executions / 149 run minutes per phase**. Total =
  **337 executions / 345 run minutes per phase**, **1,011 executions / 1,035
  run minutes** for original/red/restored phases. Budget one isolated three-minute
  build per control per phase: 74 x 3 x 3 = **666 build minutes**. Apply/restore,
  comparison and per-control reporting floor = 74 x 1 = **74 minutes**.
  Thus separate Mutation floor = **1,775 minutes**. PC-11..13 run on Windows;
  other exact methods run once on Linux. No optional second-OS Mutation hidden here.
- Shared setup/tool bootstrap allowance = **10 minutes** (three-minute gated
  tool build and two-minute environment check on each OS). Total proposed
  verification = setup 10 + ordinary 144 + PC edit/restoration 74 + PC builds
  666 + PC runs 1,035 = **1,929 minutes (32.15 hours)**, excluding source authoring,
  retries, slot waits and independent Plan/TestDesign work.
- The parent estimated 134 ordinary minutes; full-harness inventory adds 10.
  Reinstating the old 288-minute before/after stress prescription would cost
  422 minutes with that parent floor, versus 144 now: **278 minutes avoided**.
  Ordinary rows still run on both OSes at the final source. No speculative PC
  batching discount is claimed: shared files/methods and SourceLanding custody
  require isolated evidence, so mutation parallel/batch savings are budgeted zero.

#### Handoff audit

Bodies read: recorded above, including nearest new-file fixtures and real receipt
helpers. **Guards=74, mapped=74, missing=0, duplicate PC maps=0**; historical
PC-1..54 pending, additional PC-55..74 pending. Exact Mutation filters resolve to
existing method declarations; counts are projected for the explicitly described
internal setup and eight marker modes. Static declarations cannot prove reachability.

**All PCs executable: no.** PC-1 needs N4; PC-10 needs N1; PC-33/38/39/40 need
N2; the event/cleanup controls PC-41/42/47/69..72 need N3 plus S6 implementation.
S4's missing helper/receipt setup is already commissioned by its unchanged fix
plan. N1 also requires preserving a finite terminal/retry observation for PC-7.
These missing setups are explicit, rather than a zero-gap executable claim.
Return to Plan for the narrow fixture scope and cancellation design, then refresh
this manifest if its roster changes. No human choice is needed. **Code handoff
is refused until every named PC has a finite path to its specified assertion.**
