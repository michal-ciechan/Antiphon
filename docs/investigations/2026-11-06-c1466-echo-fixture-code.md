# CARD-1466 C1382 fixture Code evidence

The fixture repair works. Final verification remains incomplete: the mechanical
contract guard is red and the whole Unit row hit its unchanged 15-minute bound.
Do not land or claim Full/Green from this report. Next work is Code/Debug, followed
by ordinary Review only when the remaining ordinary gate is complete.

## Identity and deliverables

- Original Code / landing owner: `69ade330-9ef8-47da-8414-72d5790a61ea`.
- Branch: `feat/card-task-69ade330`; worktree: `/work/worktrees/task-69ade330`.
- Task base and base-confirmation SHA: `1527c29fef2737bf3eaa49f2293d82d85402265a`.
- Compiling red witness SHA: `649d74b7a89921a78cf10277b4cca2e1b803b0c3`.
- Fix and actual tested candidate SHA: `d53b06a0a629c6ae7b7f849e0b0cba8025b190d5`.
- Plan: `docs/superpowers/plans/2026-11-05-c1466-echo-fixture-repair.md`.
- Pending unbound candidates: `docs/superpowers/mutations/2026-11-05-c1466-echo-output-candidates.md`.
- Read Debug first: `origin/feat/card-task-1f10d17d:docs/investigations/2026-11-05-debug-card-1466-inherited-reds.md` (report commit `2ce364964`).
- Runner defaults/session-runners were read: global default server2, available Linux
  runner; all commands ran locally on Linux. No desktop, landing or restart.
- Restart: **none**. No changed OS-selected production behavior or rc startup path.

Only the shared fake and cancellation fixture change behavior. The fake adds a
default-off synchronous mode; EchoWorld's shared factory is its sole opt-in. The
mode performs the existing Emit then first-output signal before SendPrompt returns.
The asynchronous default delay, empty-output behavior, callbacks and prompt failure
recording remain. The incomplete-task gate deterministically withholds asynchronous
work without consuming a worker thread. The witness drives EchoWorld's actual
factory, and fails on S1's old scheduling shape before passing on S2.

Manual diff inspection confirms the 1,000 ms setting and all existing state/status/
completion assertions remain. No product, policy, digest/pin or d69fe301 repair file
changed. New methods stay in an existing classified Slow/Integration class, so no
policy entry or policyHash/live-lane pin refresh is needed for this change. Consumer
search found 138 files; the opt-in closure is the one EchoWorld fixture. Its full
class and the default-path behavioral control are executed. Unit does not establish
delivery, landing, leases or general persistence acceptance. No full assembly ran.

## Ordinary invariant outcomes

| ID | Actual outcome |
|---|---|
| V-1 | Intended red: 1 failed at first-output assertion on S1; green: 1 passed on S2 (0.0144479 s body). Full fixture also passes witness. |
| V-2 | Passed: default async path emits/signals only after gate release. |
| V-3 | Passed: empty output plus emitted noise leaves first-output false. |
| V-4 | Passed: callback order, exact injected exception identity and both prompt-history entries. |
| R-1 | Passed: full AgentSessionCancelledTurnTests, 20/20, including all existing C1382 state assertions and four new controls. |
| R-2 | Incomplete/red: classification policy 39/39, metadata guard 1/1, registry merge 5/5, tripwire 2/2; mechanical contract 10 passed / 1 failed. |
| R-3 | Incomplete: whole Unit selection timed out at 15m; no TRX, no qualified execution count. Eight console failures observed before timeout. |
| R-4 | Passed within stage repeat cap: 13 C1382 methods x 3 = 39/39, one host/one test lease, native repeat ordinals 3/3. |

The brief requested 20 repeats. The higher-priority stage contract caps unchanged
proof at 3 normal plus 2 loaded, and permits excess only for a Review-demonstrated
flake. No such Review was supplied. Exactly three normal repetitions were run;
no additional deliberate loaded repetitions, no 20-repeat claim. Natural host load
was substantial (24 cores; load 49.30 earlier, 33.57 near proof admission).

CP-4 strict receipt validation passed with expected SHA d53b06a0a629c6ae7b7f849e0b0cba8025b190d5,
expected repeat 3 and rows CP-4: CHECKPOINT SOURCE VALID. All actual checkpoint
rows retain dirty=0, sourceState=clean, buildSource=verified. Red rows remain red.
The Unit timeout has executed=n/a; it is never converted into a zero-failure pass.

### Fresh TRX class census

| Class (CP-2) | Results | Outcome |
|---|---:|---|
| AgentSessionCancelledTurnTests | 20 | all passed |
| TestClassificationPolicyTests | 39 | all passed |
| TestClassificationGuardTests | 1 | passed |
| SlowTestRegistryMergeTests | 5 | all passed |
| SlowTestTripwireTests | 2 | all passed |
| MechanicalPcContractTests | 11 | 10 passed, 1 failed |

Every intended class and new method was checked in fresh TestDefinitions/TestMethod
identities and native counters. Full TRX identities, per-test durations and SHA-256
are retained in the evidence archive's c1466-trx-inventory.json.

### Repeat durations (native TRX seconds)

| Method | Passed | Min seconds | Max seconds |
|---|---:|---:|---:|
| C1382_Echo_before_wrapper_does_not_stick_streaming_turn | 3 | 34.9782079 | 36.5703767 |
| C1382_Echo_text_does_not_postpone_stall_after_real_prompt | 3 | 34.9669413 | 35.6840778 |
| C1382_Order_real_prompt_only_and_wrapper_only | 3 | 34.3263197 | 34.4414707 |
| C1382_Rearm_completed_follow_up_survives_idle | 3 | 34.4113856 | 35.4465068 |
| C1382_Rearm_echo_after_second_interrupt_restores_input_wait | 3 | 34.9498472 | 35.4067338 |
| C1382_Rearm_follow_up_saved_before_turn_end_still_arms | 3 | 35.0164115 | 35.4348455 |
| C1382_Rearm_launch_prompt_saved_before_cancel_stays_waiting | 3 | 33.1511782 | 33.1570802 |
| C1382_Rearm_non_end_turn_stop_returns_to_awaiting_input | 3 | 37.0670684 | 37.1050740 |
| C1382_Rearm_pre_deploy_slash_prompt_is_not_killed | 3 | 33.1571189 | 34.3022592 |
| C1382_Rearm_same_burst_follow_up_after_provisional_slash_arm_still_rearms | 3 | 34.4407436 | 35.4367843 |
| C1382_Rearm_saved_interrupt_before_processing_returns_to_input | 3 | 34.4121342 | 34.4271779 |
| C1382_Rearm_saved_turn_end_before_processing_survives_idle | 3 | 33.1248897 | 34.7753808 |
| C1382_Rearm_second_interrupt_returns_to_awaiting_input | 3 | 35.6973685 | 35.7468597 |

These are native method envelopes, including overlapping setup/queue costs, not
first-output latency. CP-4 native host wall is 69.1905 s; test wall envelope 37.1058 s.
The tool's summed SLOW CLASS 1361 s is overlapping per-case time, not 22 minutes of
wall cost. No output deadline was inferred from those envelopes.

## Remaining failures and base confirmation

The exact failed method pack was rebuilt and run at clean, unchanged base HEAD
1527c29fef2737bf3eaa49f2293d82d85402265a in a temporary detached checkout,
/work/worktrees/task-69ade330-base-witness. Pre/post Git status was empty, HEAD
unchanged, and tested DLL SHA-256 remained
`c7f4624a976c53805ac3409d160cbff342a64d8fa44b86a50b4e04b8a150c956`.
Fresh base TRX: **45 executed, 42 passed, 3 failed, 0 skipped**, native duration
14.970 s; native TUnit driver exit 2. The four intended classes have 1 effort-dialog,
1 mechanical, 2 native-policy and 41 secret-floor vector results.

Confirmed inherited at the base, with matching failures:

- MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow: TestDesign
  bundle lacks expected phrase "binding required after Code".
- NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute and
  Markers_do_not_expand_policy_exclusions: reverse-coverage diagnostics for
  C1677RetentionServerTests.Ignored_nested_repository_secret_survives_real_transport
  and Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay.

Not reproduced by the base rerun: RunnerClaudeAdapterEffortPromptTests.
A_dialog_appearing_during_the_minimum_floor_is_resolved (early expected true),
and four WorktreeIgnoredContentClassifierTests.
Both_classifiers_protect_the_secret_floor_under_a_disposable_override vectors
(bin/id_ed25519.pub, bin/store.p12, bin/cert.pfx/token, bin/id_ed25519/x), which
reported RegexMatchTimeoutException in Unit. All 41 base vectors and the dialog
case passed in the focused pack. Their load-sensitive attribution remains unresolved;
this is not a claim that an exact same-tip isolation rerun passed.

Unit CP-3 ended at its original 15-minute bound, reason row-deadline-exceeded,
with no TRX. Eight console red results are diagnostic observations only. A caller
Debug/Code follow-up must investigate its noncompletion and these exact red methods,
repair confirmed guard/policy defects under their owners, and close R-2/R-3 with
required fresh execution. Do not widen a timeout, weaken an assertion, rerun green
rows, or infer a complete Unit census from partial phase/console data. This delegate
cannot sub-delegate; it has not commissioned an orphaned Debug task.

## Checkpoint receipts (unedited)

```text
CHECKPOINT CP-1 commit=649d74b7a89921a78cf10277b4cca2e1b803b0c3 build=ok filter=/*/*/AgentSessionCancelledTurnTests*/C1466_EchoWorld_output_precedes_withheld_async_work* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-69ade330/.antiphon/checkpoints/20261106-000159-4494/rows/CP-1/run.trx slot=granted waited=195s dirty=0 source=649d74b7a89921a78cf10277b4cca2e1b803b0c3 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=195s executed=9.021s wall=204.170s
PHASES CP-1 slotWait=225s build=435.0184864s startup=6.820023s testsWall=0.1096948s teardown=0.8507723s hostWall=7.7808077s
BUDGET wall=720.473s execution=495.412s admissionOnly=225.060s
CHECKPOINT CP-1 commit=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 build=ok filter=/*/*/AgentSessionCancelledTurnTests*/C1466_EchoWorld_output_precedes_withheld_async_work* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-69ade330/.antiphon/checkpoints/20261106-001554-5a46/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=30s executed=8.716s wall=38.785s
PHASES CP-1 slotWait=105s build=397.80871s startup=7.24809s testsWall=0.0144479s teardown=0.5403661s hostWall=7.8030804s
CHECKPOINT CP-2 commit=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 build=reused filter=/*/*/(AgentSessionCancelledTurnTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(MechanicalPcContractTests*)/* executed=78 passed=77 failed=1 skipped=0 trx=/work/worktrees/task-69ade330/.antiphon/checkpoints/20261106-001554-5a46/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=30s executed=113.312s wall=143.358s
PHASES CP-2 slotWait=30s build=0s startup=12.7206002s testsWall=96.3145042s teardown=3.4568605s hostWall=112.4919645s
CHECKPOINT CP-3 commit=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=60s dirty=0 source=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=60s executed=901.078s wall=961.145s reason=row-deadline-exceeded
BUDGET wall=1667.732s execution=1472.706s admissionOnly=195.027s
CHECKPOINT CP-4 commit=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 build=ok filter=/*/*/AgentSessionCancelledTurnTests*/C1382_* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-69ade330-repeat-proof/.antiphon/checkpoints/20261106-003151-9430/rows/CP-4/run.trx slot=granted waited=150s repeat=3 repetitions=3/3 hostInvocations=1 dirty=0 source=d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=150s executed=70.101s wall=220.209s
REPETITION 1/3 executed=13 passed=13 failed=0 skipped=0 (CP-4)
REPETITION 2/3 executed=13 passed=13 failed=0 skipped=0 (CP-4)
REPETITION 3/3 executed=13 passed=13 failed=0 skipped=0 (CP-4)
PHASES CP-4 slotWait=285s build=333.759689s startup=16.6989639s testsWall=37.1057547s teardown=15.3856937s hostWall=69.1905241s
BUDGET wall=706.518s execution=421.463s admissionOnly=285.055s
```

## Cost, extra drivers and evidence boundaries

The closed table ran in three committed groups: S1 CP-1; S2 CP-1/CP-2/CP-3;
S2 CP-4. The proof used /work/worktrees/task-69ade330-repeat-proof detached at
exact d53b06a0a629c6ae7b7f849e0b0cba8025b190d5 while Unit continued, with separate
outputs and leases. It drives fake adapters and isolated DB schemas, no native PTY
assembly; every owned command/executor was awaited. One build per worktree.

Additional drivers, explicitly accounted:

1. Brief-required first native incremental build: tests/Antiphon.Tests, UseAppHost=false,
   0 errors / 1728 warnings, MSBuild 314.52 s, slot=granted waited=0s held=315s.
   It also built the checkpoint tool; no separate bootstrap build ran.
2. Required inherited-red confirmation build in detached base: native outputs,
   UseAppHost=false, 0 errors, MSBuild 227.58 s, slot=granted waited=315s held=228s.
3. Exact base failing-method pack: one --no-build TUnit invocation using matching
   UseAppHost=false; slot=granted waited=211s held=22s. No full base suite ran.

All build/test drivers took the host slot gate. No unleased or desktop run. Initial
CP-1 launcher budget 20m was refused before any build/test (derived minimum 22m);
it was corrected to that minimum. Row watchdog remained 15m throughout. Some early
wait polling used a relative path instead of a run ID; final absolute/ID waits
observed actual executor exit, cleanup and reports. No extra test executions resulted.

Checkpoint costs (admission-only versus execution and wall):

| Run | Admission-only seconds | Execution seconds | Wall seconds |
|---|---:|---:|---:|
| 20261106-000159-4494 | 225.0604969 | 495.412323 | 720.6081478 |
| 20261106-001554-5a46 | 195.0265091 | 1472.705619 | 1668.1204064 |
| 20261106-003151-9430 | 285.0550465 | 421.4626317 | 706.5929933 |

The unedited BUDGET/PHASES lines above are authoritative; parallel run wall times
must not be added as elapsed task wall. Build cost and admission are separate from
test time. Fine-grained external-call spans absent from instrumentation remain
unknown/remainder, not zero. Raw phase chunks are retained; the summary helper
returned 0 without a published summary, so no complete phase census is claimed.
Prior capacity Debug 5ae98d31 (cited by the read Debug report) measured 222-817 s
cold builds and up to 405 s admission; the present 315 s base admission and checkpoint
waits fit that capacity diagnosis. Unit noncompletion needs its own exact Debug
follow-up before another broad retry. No unchanged green selection was repeated
beyond the permitted proof budget.

Manual gates at tested source: full base..d53 evidence guard passed (2 commits,
0 entries, 0 violations); OS witness declaration lint passed with both changed C#
paths test-only, no witnesses/startup obligations; diff check passed. Fresh fetch
resolved master to 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a; merge-tree was
conflict-free, tree 20eb3501015646eeb2d1497adcc85321d5231848. No merge, rebase,
landing or activation occurred. The final narrative report commit is separately
published; actual test SHAs above remain unchanged.

## Mutation inventory

All pending for method-scoped SourceLanding Mutation: PC-1 option off in EchoWorld;
PC-2 omit Emit; PC-3 omit signal completion; PC-4 empty-output unconditional signal;
PC-5 enable shared default; PC-6 callback bypass and injected-failure bypass variants.
No candidate is bound or deliberately mutated by Code. The ordinary S1 regression
red and repeat green do not discharge any PC. Mutation owns deliberate
red/restore/fresh-green cycles and missing-control discovery.

## Handoff

Fixture implementation is complete and pushed. R-2 and R-3 remain open; no Review
or landing approval is claimed. Keep the repair, investigate/repair the reported
ordinary failures and Unit noncompletion, then ordinary Review. Original landing
owner remains Code 69ade330-9ef8-47da-8414-72d5790a61ea. Restart: none.

## Evidence retention and cleanup

All owned build/test drivers and checkpoint executors completed before cleanup.
Fresh TRX and native receipt validation were inspected before output deletion.
Evidence archive: `/tmp/c1466-evidence.tgz`, SHA-256
`81f96b236b1c970ede96c78e2bf2437185fd292d47bb97a42c02496299b255fb`.
It retains original TRX, raw checkpoint reports/receipts/manifest and build logs,
base results, per-test inventory and raw phase chunks. Generated evidence remains
uncommitted. The exact planned report paths and this individual Markdown are the
only narrative evidence committed; no raw payload directory was added.

Owned native bin/obj, isolated outputs and result directories were deleted using
individually typed absolute literal paths. PowerShell initially retained hidden
files; exact-path rm -r removed that residue, with no force/glob/variable deletion.
Both temporary detached worktrees were removed through ordinary git worktree remove.
The final primary output inventory is empty; tracked source remained clean.
The archive preserves receipts whose historical absolute TRX paths are now removed.
It can be extracted for read-only inspection; validation succeeded before cleanup.

The following report-only commit carries this narrative and preserves historical
outcomes; actual tests remain bound to the committed SHAs named above. Final
full-range evidence/OS/static merge checks and the remote tip are reported to caller.
