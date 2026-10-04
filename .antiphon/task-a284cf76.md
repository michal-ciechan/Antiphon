# CARD-1029 S2/S3 Code report — a284cf76

S2/S3's seven methods are authored, but ordinary verification is incomplete. This branch is not ready for Review or landing. The two permitted runtime repair rounds are exhausted; the last round also needed a separately committed correction of a build error before its tests could run.

## Source and ownership

- Task base: `a50794508bb48683402b402b0468c9852af20a13`.
- Branch: `feat/card-task-a284cf76`; worktree: `/work/worktrees/task-a284cf76`.
- Original Code task / landing owner: `bed39f89-24ec-4ef4-a70e-f8af615ae5b2`.
- Plan: `docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md`.
- Qualification ledger: `docs/superpowers/plans/2026-10-04-card-1029-control-qualification.md`.
- Initial implementation: `a1e8f729d52eb44e07ec8c873f83baf7925cd63f`, committed and pushed before checkpoints.
- Repair round 1: `2d464ec51671801a309a473648b347b10371a617`, committed and pushed before checkpoints.
- Repair round 2: `c9430eb8512c490e0ec32d78fd4d774e0b9f7914`, committed and pushed; build rejected it with CS0103 before any test executed.
- Round 2 compile correction / last tested code: `bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8`, committed and pushed before the corrected checkpoint run. No production source changed in this task. The final report commit changes documentation only; the final published SHA is in the caller's progress line.

## Deliverables and limits

`tests/Antiphon.Tests/Application/CodexCliDeliveryWorld.cs` separates the isolated schema, seeded git origin, mirror, recipient terminals, transcript log and controlled clock from each disposable server graph. Graph recreation uses real adapters, attach, runtime ingestion, courier and phone-home frames. Test-local EF interceptors cover queue insertion and successful verdict saves. The working first-round arrangement passed the durable spill recreation witness in both eligible/busy variants.

`tests/Antiphon.Tests/Application/CodexCliObservationGapTests.cs` contains all seven named methods. They compare complete submitted W, independent producer E, recipient files, original attempt floors and real persisted state. Readiness/rules ACK latches release and join launch work in finally. No positive record is assembled from the expected W: delayed positives publish the captured actual terminal submission. Negative at-floor/timestamp adversaries are explicit synthetic negatives. The final method loops enumerate all eight kind vectors and all four timestamp inputs rather than stopping at the first failed vector.

`CodexCliRemoteDeliveryFixture.cs` retains its existing RunAsync entry point and adds kind-specific scripted surfaces, rules receipt/ACK handling and an optional clock. All eight original consumers passed twice. The only later fixture work is in the new world and gap-test files, so CP-4's latest source is honestly reported as 2d464ec, not as the last Code SHA.

The plan's CP-7/8 method alternation filters received the same suffix-wildcard discovery repair already used for S1 CP-15. Counts, ceilings and timeouts were not increased. RunnerCodexCliEvidenceTests.cs was not touched; no rebaseline or branch rebase was performed.

Current fixture defects requiring the next Code task:

1. DirectoryCounter returns an interface proxy where RemoteWorkspaceService requires a concrete phone-home client. All remote paths consequently fail before the recipient. Preserve the actual directory/client's workspace transport contract while placing typed-call counters at a compatible boundary. The selected capabilities fix must remain: using RoutingSessionRunnerClient for a non-session Grok capabilities lookup reaches the empty local client.
2. World.RowAsync must select the current session as well as the task. The added real prior-task Retry leaves both original and new same-task queue rows; its current SingleAsync query can see both. Keep the original row/history assertions separately; do not discard the prior record to make Single pass.
3. Verify the post-input save cut's actual fired flag and original durable attempt. The last round recognizes both Delivered and LateConfirmed successful save paths, but its full ordinary result must be read from the final TRX. Do not remove the one-submission, settlement, E-release, generation or original-floor assertions.
4. Establish a reachable inline Claude producer under the unchanged selected profile. The full ordinary remote Worktree brief includes branch/reporting contracts and can exceed the 900-byte conservative ceiling even with a short goal. Current remote setup fails before this explicit fit oracle, so this is an unresolved design/reachability issue, not a passed inline vector. Do not widen the ceiling or substitute a pointer for the required inline proof.
5. Grok handoff insertion/lost-return fault subcases are not completed. P-1 / CARD-1038 is excluded from ordinary Code and blocks PC-274 only in paused Mutation; this dispatch did not repair initialization cleanup, suppress kills, abandon a live launch or claim a surviving-recipient crash cut.

These fixtures prove isolated Postgres / production queue / framed transport and retained scripted-recipient integration. They do not prove vendor CLI, native PTY, WAN or abrupt process/power-loss behavior. DI disposal is not an abrupt crash. The final prior-generation method attempts real Retry lineage plus a separate synthetic at-floor adversary; its red result does not qualify either control.

## Verification scope and outcomes

The explicit continuation brief and active plan slice override the generic Final Unit profile: no whole Unit lane, whole assembly, namespace sweep or Windows run was authorized. CP-4 covers the complete affected existing integration class; CP-5..8 select every method of the new class. No unbounded assembly classes were run. There are no green claims for deferred checks.

Initial checkpoint run `20261004-220933-969c`: CP-4 8/8; CP-5 0/1; CP-6 0/1; CP-7 0/3; CP-8 1/2. Total 9 passed, 6 failed, 0 skipped. The kind failure was remote Grok launch setup; Retry used inconsistent source timestamps; the old-floor adversary was installed too late; screen/timestamp release stayed unconfirmed; the post-input fault did not fire. One wait raced executor cleanup and returned 6; a fresh wait read phase=done / exit=1 and the complete report. No result was treated green on the race response.

Repair round 1 run `20261004-223335-4214`: CP-4 8/8; CP-5 0/1; CP-6 0/1; CP-7 1/3; CP-8 1/2. Total 10 passed, 5 failed, 0 skipped. Old-floor and durable spill witnesses passed. Remaining ordinary failures were remote Grok capabilities, remote Retry without a newly claimed session, unchanged screen/timestamp E release, and the post-input fault expectation. Fresh TRX rosters were checked.

Round 2 first attempt `20261004-230032-f00c`: build failed (services registration in a settings callback). No test failure from that run qualifies a regression or PC. Executor was stopped through the checkpoint tool and awaited before editing. The compile correction is a separate pushed commit; no runtime third repair round was performed.

Corrected round 2 run `20261004-230504-a4a0`: final details, fresh method roster and unchanged CHECKPOINT lines are appended below. This run passes bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 as expected source SHA. Ordinary acceptance remains incomplete regardless of clean source/build provenance.

V-13 and R-2: carried S1 exact transport receipt at 491c2ef, not rerun here. V-14, V-19 and R-3: CP-4 passed all eight methods at 2d464ec. V-21 and V-22: incomplete/red in this continuation. V-23, V-24, V-25 and V-26: existing CP-4 cases passed; the deferred remote matrix is not discharged. V-27 and R-6: carried S1 CP-16 receipt at 491c2ef. R-1: carried S1 CP-15 at 241adc8. R-7: carried S1 CP-17 at 491c2ef. S1's 14 passes are not receipts at the last Code SHA.

Deferred-to-follow-up, never marked passed: V-1..12 and R-4 through F-Observation/F-Native; F-Faults CP-9/10's 44 vectors; F-Matrix CP-11..14's 38 Retry + 38 seeded-warm + 38 recreated-warm vectors; F-Regression's remaining full classes; F-Native CP-2's exact L0959 29 Windows results and CP-18's current 30 plus additions. Preserve all these named obligations. P-1/CARD-1038 remains external to ordinary Code.

## Additional drivers and provenance

The checkpoint-tool bootstrap was an unlisted build required to run the checkpoint tool: gated dotnet build tools/Antiphon.Checkpoints with OutputPath=bin-c1029-tool/ and UseAppHost=false. It passed (0 errors; pre-existing CS8602 warning). BUILD SLOT granted, waited=0s; held=11s. Two incorrectly copied expected-SHA arguments were rejected before a checkpoint driver launched; the successful requests use the exact committed SHAs shown above.

Base checks are explicitly additional named-method runs to distinguish authored fixture failures from a production defect. A detached, test-owned worktree at a50794508bb48683402b402b0468c9852af20a13 received only the three test/fixture files. No production file changed, and no base-worktree commit/push was made. The copied harness makes these dirty-source diagnostic receipts, not clean-SHA acceptance receipts.

- .antiphon/base-proof: implicit-build dotnet run through build-slot; compile error, zero tests. The gate reported that implicit run builds do not receive its maxcpucount, so subsequent diagnostics use an explicit gated build plus --no-build run.
- .antiphon/base-proof-corrected: explicit build passed; two methods executed and failed in workspace preparation because of DirectoryCounter. Neither qualifies the suspected release defect. Build slot granted waited=0s held=177s; test slot granted waited=0s held=55s.
- .antiphon/base-proof-working: restores the previously working 2d464ec test/world bodies against base production; preserves the same shared fixture bytes. Build/test results and any defect-card reference are appended below. The exact selected methods are C1029_Unobservable_screen_retains_spill and C1029_Unobservable_timestamp_floor_is_original; nothing else is selected.

Every build/test driver used the host gate. No fleet runner or platform was pinned. GET /api/runner-defaults and /api/session-runners were read before work (defaults revision 2, Linux global default, no kind overrides; both Linux/Windows catalogue entries available, temporary entry unavailable). All intended checkpoint methods require nonzero executed counts; skipped/zero/build failures are not passes. No deliberate production mutant, PC red/restore/green, loaded repetition or assertion/timeout relaxation was performed.

Restart: none. Activation/landing owner remains the caller and original Code owner after ordinary Code and Review succeed. Next: Code, not land/deploy. After a completed ordinary Code pass, commission Review; only after Review and original-owner landing commission SourceLanding Mutation.

The confirmed production gap is now CARD-1056 (bc5c7335-6925-4023-b304-c30fa9e2230c), Backlog on board Antiphon. The create request succeeded; its subsequent optional card-file publication failed because the board is not opted in and its configured Windows repository drive is unavailable in this Linux mirror. It was not retried or duplicated. A fresh get confirms the server card. No generated docs/cards file was edited.

At the last tested source the post-input crash method still fails because the expected InjectedFault is not thrown. It is not a successful save-cut witness and PC-220 remains pending. The final old-generation method fails with more than one queue row; the earlier at-floor-only version's green is not substituted for its added real prior-generation setup.

## Stored receipts and pending controls

The appendices retain unedited CHECKPOINT lines and identify every pending PC/variant. Generated TRX/JSON/logs stay gitignored in their original evidence directories; they are not force-added. Only this individual Markdown report and deliberate documentation changes may be committed.

## Final fresh TRX census and source receipts

Run 20261004-220933-969c phase=done exit=1 source=a1e8f729d52eb44e07ec8c873f83baf7925cd63f sourceState=clean buildSource=verified.
```text
CHECKPOINT CP-4 commit=a1e8f729d52eb44e07ec8c873f83baf7925cd63f build=ok filter=/*/*/CodexCliObservationTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-220933-969c/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a1e8f729d52eb44e07ec8c873f83baf7925cd63f sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=a1e8f729d52eb44e07ec8c873f83baf7925cd63f build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-220933-969c/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=a1e8f729d52eb44e07ec8c873f83baf7925cd63f sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=a1e8f729d52eb44e07ec8c873f83baf7925cd63f build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-220933-969c/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a1e8f729d52eb44e07ec8c873f83baf7925cd63f sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=a1e8f729d52eb44e07ec8c873f83baf7925cd63f build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)|(C1029_Unobservable_screen_retains_spill*)|(C1029_Unobservable_timestamp_floor_is_original*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-220933-969c/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=a1e8f729d52eb44e07ec8c873f83baf7925cd63f sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=a1e8f729d52eb44e07ec8c873f83baf7925cd63f build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)|(C1029_Post_input_crash_late_confirms_once*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-220933-969c/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=a1e8f729d52eb44e07ec8c873f83baf7925cd63f sourceState=clean buildSource=verified
```

Run 20261004-223335-4214 phase=done exit=1 source=2d464ec51671801a309a473648b347b10371a617 sourceState=clean buildSource=verified.
```text
CHECKPOINT CP-4 commit=2d464ec51671801a309a473648b347b10371a617 build=ok filter=/*/*/CodexCliObservationTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-223335-4214/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=2d464ec51671801a309a473648b347b10371a617 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=2d464ec51671801a309a473648b347b10371a617 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-223335-4214/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=2d464ec51671801a309a473648b347b10371a617 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=2d464ec51671801a309a473648b347b10371a617 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-223335-4214/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=2d464ec51671801a309a473648b347b10371a617 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=2d464ec51671801a309a473648b347b10371a617 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)|(C1029_Unobservable_screen_retains_spill*)|(C1029_Unobservable_timestamp_floor_is_original*) executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-223335-4214/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=2d464ec51671801a309a473648b347b10371a617 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=2d464ec51671801a309a473648b347b10371a617 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)|(C1029_Post_input_crash_late_confirms_once*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-223335-4214/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=2d464ec51671801a309a473648b347b10371a617 sourceState=clean buildSource=verified
```

Run 20261004-230032-f00c phase=stopped exit= source=c9430eb8512c490e0ec32d78fd4d774e0b9f7914 sourceState=clean buildSource=unknown.
```text
```

Run 20261004-230504-a4a0 phase=done exit=1 source=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 sourceState=clean buildSource=verified.
```text
CHECKPOINT CP-5 commit=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-230504-a4a0/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-230504-a4a0/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)|(C1029_Unobservable_screen_retains_spill*)|(C1029_Unobservable_timestamp_floor_is_original*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-230504-a4a0/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)|(C1029_Post_input_crash_late_confirms_once*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-a284cf76/.antiphon/checkpoints/20261004-230504-a4a0/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8 sourceState=clean buildSource=verified
```
- CP-5: C1029_Per_kind_receipts: Failed.
  First failure: AggregateException: C1029 kind vector failures (C1029 Grok remote=True busy=False) (C1029 Grok remote=True busy=True) (C1029 Claude spill=False busy=False) (C1029 Claude spill=True busy=False) (C1029 Claude spill=False busy=True) (C1029 Claude spill=True busy=True)
- CP-6: C1029_Enqueue_fault_and_retry_keep_identity: Failed.
  First failure: ShouldAssertException: task.AgentSessionId
- CP-7: C1029_Old_generation_does_not_confirm: Failed.
  First failure: InvalidOperationException: Sequence contains more than one element.
- CP-7: C1029_Unobservable_screen_retains_spill: Failed.
  First failure: ShouldAssertException: task.AgentSessionId
- CP-7: C1029_Unobservable_timestamp_floor_is_original: Failed.
  First failure: AggregateException: C1029 timestamp vector failures (C1029 timestamp offset=-1) (C1029 timestamp offset=null) (C1029 timestamp offset=0) (C1029 timestamp offset=1)
- CP-8: C1029_Durable_spill_survives_recreated_graph: Failed.
  First failure: ShouldAssertException: task.AgentSessionId
- CP-8: C1029_Post_input_crash_late_confirms_once: Failed.
  First failure: ShouldAssertException: Task `w.FlushAsync()`

Final Code census: executed=7 passed=0 failed=7 skipped=0. All four selected rows and all four isolated builds have slot=granted waited=0s. Source/build receipts are clean/verified at bfd30a706a6dcf324bd6d4bd26a2d4aa4a50fac8. This is a red ordinary result, not acceptance.

Working-fixture base reproduction: executed=2 passed=0 failed=2 skipped=0, test host exit=2. The screen method fails at LateConfirmed versus Delivered after actual current W ingestion; the timestamp method fails because durable E remains after captured current W ingestion. It reaches the minus-one-tick case only in the earlier working method. Equality/plus-one/null boundary acceptance is not claimed. Test-owned base production is unchanged; three dirty transplanted test files are diagnostic source, not clean acceptance. BUILD SLOT build granted waited=0s held=73s. Test slot grant/release lines follow.
```text
BUILD SLOT granted lease=cd17266a-6f2d-42b9-a8f2-475973bd2238 waited=0s maxcpucount=6
BUILD SLOT released lease=cd17266a-6f2d-42b9-a8f2-475973bd2238 held=66s
```

## Every positive control remains pending
- PC-1: pending post-land SourceLanding Mutation.
- PC-2: pending post-land SourceLanding Mutation.
- PC-3: pending post-land SourceLanding Mutation.
- PC-4: pending post-land SourceLanding Mutation.
- PC-5: pending post-land SourceLanding Mutation.
- PC-6: pending post-land SourceLanding Mutation.
- PC-7: pending post-land SourceLanding Mutation.
- PC-8: pending post-land SourceLanding Mutation.
- PC-9: pending post-land SourceLanding Mutation.
- PC-10: pending post-land SourceLanding Mutation.
- PC-11: pending post-land SourceLanding Mutation.
- PC-12: pending post-land SourceLanding Mutation.
- PC-13: pending post-land SourceLanding Mutation.
- PC-14: pending post-land SourceLanding Mutation.
- PC-16: pending post-land SourceLanding Mutation.
- PC-17: pending post-land SourceLanding Mutation.
- PC-18: pending post-land SourceLanding Mutation.
- PC-19: pending post-land SourceLanding Mutation.
- PC-20: pending post-land SourceLanding Mutation.
- PC-21: pending post-land SourceLanding Mutation.
- PC-22: pending post-land SourceLanding Mutation.
- PC-23: pending post-land SourceLanding Mutation.
- PC-24: pending post-land SourceLanding Mutation.
- PC-25: pending post-land SourceLanding Mutation.
- PC-26: pending post-land SourceLanding Mutation.
- PC-27: pending post-land SourceLanding Mutation.
- PC-28: pending post-land SourceLanding Mutation.
- PC-29: pending post-land SourceLanding Mutation.
- PC-30: pending post-land SourceLanding Mutation.
- PC-31: pending post-land SourceLanding Mutation.
- PC-32: pending post-land SourceLanding Mutation.
- PC-33: pending post-land SourceLanding Mutation.
- PC-34: pending post-land SourceLanding Mutation.
- PC-35: pending post-land SourceLanding Mutation.
- PC-36: pending post-land SourceLanding Mutation.
- PC-37: pending post-land SourceLanding Mutation.
- PC-38: pending post-land SourceLanding Mutation.
- PC-39: pending post-land SourceLanding Mutation.
- PC-40: pending post-land SourceLanding Mutation.
- PC-41: pending post-land SourceLanding Mutation.
- PC-42: pending post-land SourceLanding Mutation.
- PC-43: pending post-land SourceLanding Mutation.
- PC-44: pending post-land SourceLanding Mutation.
- PC-45: pending post-land SourceLanding Mutation.
- PC-46: pending post-land SourceLanding Mutation.
- PC-47: pending post-land SourceLanding Mutation.
- PC-48: pending post-land SourceLanding Mutation.
- PC-49: pending post-land SourceLanding Mutation.
- PC-50: pending post-land SourceLanding Mutation.
- PC-51: pending post-land SourceLanding Mutation.
- PC-52: pending post-land SourceLanding Mutation.
- PC-53: pending post-land SourceLanding Mutation.
- PC-54: pending post-land SourceLanding Mutation.
- PC-55: pending post-land SourceLanding Mutation.
- PC-56: pending post-land SourceLanding Mutation.
- PC-57: pending post-land SourceLanding Mutation.
- PC-58: pending post-land SourceLanding Mutation.
- PC-59: pending post-land SourceLanding Mutation.
- PC-60: pending post-land SourceLanding Mutation.
- PC-61: pending post-land SourceLanding Mutation.
- PC-62: pending post-land SourceLanding Mutation.
- PC-63: pending post-land SourceLanding Mutation.
- PC-64: pending post-land SourceLanding Mutation.
- PC-65: pending post-land SourceLanding Mutation.
- PC-66: pending post-land SourceLanding Mutation.
- PC-67: pending post-land SourceLanding Mutation.
- PC-68: pending post-land SourceLanding Mutation.
- PC-69: pending post-land SourceLanding Mutation.
- PC-70: pending post-land SourceLanding Mutation.
- PC-71: pending post-land SourceLanding Mutation.
- PC-72: pending post-land SourceLanding Mutation.
- PC-73: pending post-land SourceLanding Mutation.
- PC-74: pending post-land SourceLanding Mutation.
- PC-75: pending post-land SourceLanding Mutation.
- PC-76: pending post-land SourceLanding Mutation.
- PC-77: pending post-land SourceLanding Mutation.
- PC-78: pending post-land SourceLanding Mutation.
- PC-79: pending post-land SourceLanding Mutation.
- PC-80: pending post-land SourceLanding Mutation.
- PC-81: pending post-land SourceLanding Mutation.
- PC-82: pending post-land SourceLanding Mutation.
- PC-83: pending post-land SourceLanding Mutation.
- PC-84: pending post-land SourceLanding Mutation.
- PC-89: pending post-land SourceLanding Mutation.
- PC-90: pending post-land SourceLanding Mutation.
- PC-91: pending post-land SourceLanding Mutation.
- PC-92: pending post-land SourceLanding Mutation.
- PC-93: pending post-land SourceLanding Mutation.
- PC-94: pending post-land SourceLanding Mutation.
- PC-95: pending post-land SourceLanding Mutation.
- PC-96: pending post-land SourceLanding Mutation.
- PC-97: pending post-land SourceLanding Mutation.
- PC-98: pending post-land SourceLanding Mutation.
- PC-99: pending post-land SourceLanding Mutation.
- PC-100: pending post-land SourceLanding Mutation.
- PC-101: pending post-land SourceLanding Mutation.
- PC-102: pending post-land SourceLanding Mutation.
- PC-103: pending post-land SourceLanding Mutation.
- PC-104: pending post-land SourceLanding Mutation.
- PC-105: pending post-land SourceLanding Mutation.
- PC-106: pending post-land SourceLanding Mutation.
- PC-107: pending post-land SourceLanding Mutation.
- PC-108: pending post-land SourceLanding Mutation.
- PC-109: pending post-land SourceLanding Mutation.
- PC-111: pending post-land SourceLanding Mutation.
- PC-112: pending post-land SourceLanding Mutation.
- PC-113: pending post-land SourceLanding Mutation.
- PC-114: pending post-land SourceLanding Mutation.
- PC-115: pending post-land SourceLanding Mutation.
- PC-116: pending post-land SourceLanding Mutation.
- PC-117: pending post-land SourceLanding Mutation.
- PC-118: pending post-land SourceLanding Mutation.
- PC-119: pending post-land SourceLanding Mutation.
- PC-120: pending post-land SourceLanding Mutation.
- PC-122: pending post-land SourceLanding Mutation.
- PC-123: pending post-land SourceLanding Mutation.
- PC-124: pending post-land SourceLanding Mutation.
- PC-125: pending post-land SourceLanding Mutation.
- PC-126: pending post-land SourceLanding Mutation.
- PC-127: pending post-land SourceLanding Mutation.
- PC-153: pending post-land SourceLanding Mutation.
- PC-154: pending post-land SourceLanding Mutation.
- PC-155: pending post-land SourceLanding Mutation.
- PC-156: pending post-land SourceLanding Mutation.
- PC-157: pending post-land SourceLanding Mutation.
- PC-158: pending post-land SourceLanding Mutation.
- PC-159: pending post-land SourceLanding Mutation.
- PC-184: pending post-land SourceLanding Mutation.
- PC-185: pending post-land SourceLanding Mutation.
- PC-186: pending post-land SourceLanding Mutation.
- PC-187: pending post-land SourceLanding Mutation.
- PC-188: pending post-land SourceLanding Mutation.
- PC-190: pending post-land SourceLanding Mutation.
- PC-191: pending post-land SourceLanding Mutation.
- PC-192: pending post-land SourceLanding Mutation.
- PC-194: pending post-land SourceLanding Mutation.
- PC-199: pending post-land SourceLanding Mutation.
- PC-200: pending post-land SourceLanding Mutation.
- PC-201: pending post-land SourceLanding Mutation.
- PC-202: pending post-land SourceLanding Mutation.
- PC-203: pending post-land SourceLanding Mutation.
- PC-205: pending post-land SourceLanding Mutation.
- PC-206: pending post-land SourceLanding Mutation.
- PC-207: pending post-land SourceLanding Mutation.
- PC-208: pending post-land SourceLanding Mutation.
- PC-209: pending post-land SourceLanding Mutation.
- PC-210: pending post-land SourceLanding Mutation.
- PC-211: pending post-land SourceLanding Mutation.
- PC-212: pending post-land SourceLanding Mutation.
- PC-213: pending post-land SourceLanding Mutation.
- PC-214: pending post-land SourceLanding Mutation.
- PC-215: pending post-land SourceLanding Mutation.
- PC-216: pending post-land SourceLanding Mutation.
- PC-217: pending post-land SourceLanding Mutation.
- PC-218: pending post-land SourceLanding Mutation.
- PC-219: pending post-land SourceLanding Mutation.
- PC-220: pending post-land SourceLanding Mutation.
- PC-221: pending post-land SourceLanding Mutation.
- PC-222: pending post-land SourceLanding Mutation.
- PC-223: pending post-land SourceLanding Mutation.
- PC-224: pending post-land SourceLanding Mutation.
- PC-225: pending post-land SourceLanding Mutation.
- PC-226: pending post-land SourceLanding Mutation.
- PC-227: pending post-land SourceLanding Mutation.
- PC-228: pending post-land SourceLanding Mutation.
- PC-229: pending post-land SourceLanding Mutation.
- PC-230: pending post-land SourceLanding Mutation.
- PC-231: pending post-land SourceLanding Mutation.
- PC-232: pending post-land SourceLanding Mutation.
- PC-233: pending post-land SourceLanding Mutation.
- PC-234: pending post-land SourceLanding Mutation.
- PC-235: pending post-land SourceLanding Mutation.
- PC-236: pending post-land SourceLanding Mutation.
- PC-237: pending post-land SourceLanding Mutation.
- PC-238: pending post-land SourceLanding Mutation.
- PC-239: pending post-land SourceLanding Mutation.
- PC-240: pending post-land SourceLanding Mutation.
- PC-241: pending post-land SourceLanding Mutation.
- PC-242: pending post-land SourceLanding Mutation.
- PC-243: pending post-land SourceLanding Mutation.
- PC-244: pending post-land SourceLanding Mutation.
- PC-245: pending post-land SourceLanding Mutation.
- PC-246: pending post-land SourceLanding Mutation.
- PC-247: pending post-land SourceLanding Mutation.
- PC-248: pending post-land SourceLanding Mutation.
- PC-249: pending post-land SourceLanding Mutation.
- PC-250: pending post-land SourceLanding Mutation.
- PC-251: pending post-land SourceLanding Mutation.
- PC-252: pending post-land SourceLanding Mutation.
- PC-253: pending post-land SourceLanding Mutation.
- PC-254: pending post-land SourceLanding Mutation.
- PC-255: pending post-land SourceLanding Mutation.
- PC-256: pending post-land SourceLanding Mutation.
- PC-257: pending post-land SourceLanding Mutation.
- PC-258: pending post-land SourceLanding Mutation.
- PC-259: pending post-land SourceLanding Mutation.
- PC-260: pending post-land SourceLanding Mutation.
- PC-261: pending post-land SourceLanding Mutation.
- PC-262: pending post-land SourceLanding Mutation.
- PC-263: pending post-land SourceLanding Mutation.
- PC-264: pending post-land SourceLanding Mutation.
- PC-265: pending post-land SourceLanding Mutation.
- PC-266: pending post-land SourceLanding Mutation.
- PC-267: pending post-land SourceLanding Mutation.
- PC-268: pending post-land SourceLanding Mutation.
- PC-269: pending post-land SourceLanding Mutation.
- PC-270: pending post-land SourceLanding Mutation.
- PC-271: pending post-land SourceLanding Mutation.
- PC-272: pending post-land SourceLanding Mutation.
- PC-273: pending post-land SourceLanding Mutation.
- PC-274: pending post-land SourceLanding Mutation; P-1/CARD-1038 blocks its interrupted-init setup only, outside ordinary Code.

All 21 extra remote-only variants are also pending:
- PC-225 remote Create: pending.
- PC-228 remote Create: pending.
- PC-231 remote Create: pending.
- PC-234 remote Create: pending.
- PC-226 remote Retry: pending.
- PC-229 remote Retry: pending.
- PC-232 remote Retry: pending.
- PC-235 remote Retry: pending.
- PC-227 remote cold Worktree dispatch: pending.
- PC-227 remote seeded-warm handoff: pending.
- PC-230 remote cold Worktree dispatch: pending.
- PC-230 remote seeded-warm handoff: pending.
- PC-233 remote cold Worktree dispatch: pending.
- PC-233 remote seeded-warm handoff: pending.
- PC-236 remote cold Worktree dispatch: pending.
- PC-236 remote seeded-warm handoff: pending.
- PC-237 remote Create typed call: pending.
- PC-238 remote Retry typed call: pending.
- PC-239 remote cold claim typed call: pending.
- PC-240 remote seeded reuse typed call: pending.
- PC-253 remote cold final launch typed call: pending.

No PC is passed by this report. Mutation owns deliberate mutants, intended assertion red, exact restore and fresh green, plus missing-control discovery. Named deferred authoring and Windows obligations remain in the plan/ledger.

Cleanup completed: checkpoint-owned outputs and bootstrap/base alternate output directories removed; detached base worktree is removed after its drivers exited. Generated evidence remains at its original ignored paths. Full task base..HEAD evidence history check is required after the report commit.
