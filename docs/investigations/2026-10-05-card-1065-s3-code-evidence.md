# CARD-1065 S3 Code evidence — f69e8b6e

S3 is implemented, pushed and ready for ordinary Review. The caller explicitly authorized a third bounded repair round after inspection found an exited/absent source-verification bypass. New ordinary scenarios first failed against the unchanged production code; the repair then passed CP-3 with all three methods. PCs remain pending post-land SourceLanding Mutation.

## Ownership and source

- Original Code task / landing owner: `f69e8b6e-9daf-4228-931e-37bc1ba5220c`.
- Branch: `feat/card-task-f69e8b6e`.
- Worktree: `/work/worktrees/task-f69e8b6e`.
- Desktop companion (not accessible here): `C:\Antiphon\worktrees\card-task-f69e8b6e`.
- Full task base: `c1adb878a42f4c69258483e33dab3cd2a9797351` (S2, owner `bc986d96-6c94-49bb-a8dd-66e79f10da6c`).
- Final tested implementation: `c6f8caef1dcb920f162240623fdd6e05ddc6122e`. The subsequent commit containing this report changes documentation only.
- Plan: [2026-10-05-card-1065-blocked-task-parking-plan.md](../superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md).
- Intended eventual landing remains adoption of this original Code task after ordinary Review and S2 landing, followed by S4 source-policy.
- Task token was present. Read CARD-1065 and authenticated GET /api/runner-defaults and /api/session-runners. No fleet location was embedded.

## Implemented footprint

Only S3's named production files and its new BlockedParkWireTests were changed:
contracts PhoneHomeContracts, SessionRunnerContracts and TerminalSeatRelease;
runner PhoneHomeCommandDispatcher, PhoneHomeRuntimeAdapter, Program and SessionRunnerRuntime;
server ISessionRunnerClient and PhoneHomeRunnerClient, SessionRunnerHttpClient,
RoutingSessionRunnerClient and RunnerScopedSessionRunnerClient.

Adds workspaceParkV1 and append-only operation 36; existing wire numbers remain unchanged.
A versioned prepare/verify command and the publication receipt cross both transports.
Missing either parking or conditional-release capability, unknown versions and unsupported
local runtimes refuse without force-release/generation-kill fallback. The configured
runner performs publication under the session input/generation gate; a live conditional
release revalidates source and then fresh native transcript evidence before its final
synchronous fences. Fresh exited/absent confirmations also verify publication and
recheck session identity, generation and durable custody after the source read.
The server routing/scoped clients preserve session selection.
Parking remains default-off. No migrations, automatic callers, deployment, restarts,
live session stop, workspace deletion or provider launches were added/performed.

## Round 3 — exited/absent source confirmation repaired

Inspection at the previous green SHA `9652e7b90bc6e8c8447e10ab99af07a973e8f295` found:

- The source VerifyAsync call is inside `if (!wasExited)` (around lines 1134–1154).
- An already-exited child proceeds to ReleaseSlotUnderGateAsync without that verification.
  A receipt whose checkout changed after publication can therefore return AlreadyExited
  and remove custody metadata, despite D-2 requiring a fresh matching HEAD/status at release.
- The session-absent branch also returns authoritative absence without a source read;
  the repair must reconcile that branch with the same publication contract while retaining
  truthful physical occupancy and generation-watermark semantics.

The caller then authorized: “one further bounded repair round (round 3). Add a red test
first, then fix final source verification for already-exited/absent paths and matching
ordinary scenarios, rerun CP-3, push, report.”

Test-only commit `07e8632201d5fd57dc452d8cb93489f42152a91c` added 17 ordinary
scenarios inside V-8: exited and fresh absent states crossed with matching source,
advanced HEAD, dirty checkout, changed ref, changed endpoint, unavailable source read,
changed generation and replacement object, plus an absent durable-manifest race.
Receipts come from real scratch Git publication. The absence cases restart the runtime
so historical cached success cannot mask a fresh decision. Both valid controls pass.
The source-read failure is a real failing Git child; race callbacks change concrete
runtime/manifest state during the real Git read.

CP-3 then failed at the intended V-8 assertion with 15 unsafe confirmations:
exited returned AlreadyExited for seven negative scenarios; absent returned AlreadyAbsent
for eight. V-6 and V-7 passed. This is executed defect evidence against unchanged
production code, not a deliberate Mutation cycle or an inherited-red claim.

Production-only repair `c6f8caef1dcb920f162240623fdd6e05ddc6122e` extracts the shared
publication verifier and uses it before fresh exited/absent confirmation. After the
await, exited rechecks object identity, accepted generation, physical exit and verification
ownership; absent rechecks tracked-session absence and both durable seat manifests.
Negative source results refuse without removing custody metadata or signaling a child.
Live release retains its fresh native transcript read and synchronous final fences.
Historical cached success is still a replay, not a new release/absence confirmation.

The unchanged tests pass after the repair, including no signal, retained neighboring
session, unchanged generation watermark/transcript sidecar, retained refused custody
and truthful physical occupancy. No timeout, retry or assertion was loosened.
Mutation owns deliberate mutants and the final missing-control/variant inventory.

## Ordinary verification and repairs

The explicit S3 brief and plan restrict this task to CP-3 and prohibit a whole-Unit run;
that specific instruction was followed over the appended generic Final-profile boilerplate.
No assembly/namespace run was needed.

| ID | Exact method | Actual outcome |
|---|---|---|
| V-6 | BlockedParkWireTests.C1065_OldRunnerNeverReceivesFallbackKill | Passed, 1 |
| V-7 | BlockedParkWireTests.C1065_ActivityOrReplacementInvalidatesParkRelease | Passed, 1; earlier fixture repairs recorded below |
| V-8 | BlockedParkWireTests.C1065_ExitUnconfirmedRetainsSeatAndCustody | Passed, 1; includes all 17 new exited/absent scenarios after the observed defect red |

Fresh TRX was inspected for all three exact class/method identities: total/executed/passed=3,
failed/skipped=0. Receipt validation returned:

```text
CHECKPOINT SOURCE VALID source=c6f8caef1dcb920f162240623fdd6e05ddc6122e rows=1
```

The first two reds were new fixture failures, not claimed inherited failures:
1. `847c7a5768b38b155dfa923799c1ada8e7ec949e`: 2 pass / 1 fail. G-47 mistakenly inspected the completed-write counter during an attempted write. Repair checks the actual production pre-write proof invalidation and still asserts no signal/retained custody.
2. `54f659e0816f4cdea2d287d4cdc4eb8044ad91bb`: 2 pass / 1 fail. The final object-replacement scenario used Track, which correctly refuses duplicate registration. Repair supplies concrete replacement state at the final I/O barrier, and separately exposes the real replay decision so the independent release-in-progress guard cannot mask it.

The second repair also moves G-51's native activity to the publication boundary so only the final fresh read detects it. No deliberate mutants were executed. No timeout, retry policy or safety assertion was loosened.

Required checkpoints were run by the checkpoint tool, using committed HEAD as expected-source-sha and waiting through exit 75 to completion. Source was frozen during every build/run.
Unedited receipts:

```text
CHECKPOINT CP-3 commit=847c7a5768b38b155dfa923799c1ada8e7ec949e build=ok filter=/*/*/BlockedParkWireTests/C1065_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-f69e8b6e/.antiphon/checkpoints/20261005-231422-6b95/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=847c7a5768b38b155dfa923799c1ada8e7ec949e sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=54f659e0816f4cdea2d287d4cdc4eb8044ad91bb build=ok filter=/*/*/BlockedParkWireTests/C1065_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-f69e8b6e/.antiphon/checkpoints/20261005-231535-2511/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=54f659e0816f4cdea2d287d4cdc4eb8044ad91bb sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=9652e7b90bc6e8c8447e10ab99af07a973e8f295 build=ok filter=/*/*/BlockedParkWireTests/C1065_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f69e8b6e/.antiphon/checkpoints/20261005-231749-b049/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9652e7b90bc6e8c8447e10ab99af07a973e8f295 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=07e8632201d5fd57dc452d8cb93489f42152a91c build=ok filter=/*/*/BlockedParkWireTests/C1065_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-f69e8b6e/.antiphon/checkpoints/20261005-232649-9b2e/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=07e8632201d5fd57dc452d8cb93489f42152a91c sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=c6f8caef1dcb920f162240623fdd6e05ddc6122e build=ok filter=/*/*/BlockedParkWireTests/C1065_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f69e8b6e/.antiphon/checkpoints/20261005-232819-103d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=c6f8caef1dcb920f162240623fdd6e05ddc6122e sourceState=clean buildSource=verified
```

All five runs: slot=granted, waited=0s, sourceState=clean, buildSource=verified.
Final CP-3 build: 19.5787537s; test host: 31.4055785s; overall: 52s.
These were one run per changed source selection, not unchanged-proof repetitions.

Additional builds, explicitly explained before execution:
- Checkpoint-tool bootstrap, required by the plan: output bin-c1065-driver/, slot=granted waited=0s, 0 errors / 1 warning, 4.59s MSBuild.
- Round-3 checkpoint-tool bootstrap after prior cleanup: same isolated output, slot=granted waited=0s, 0 errors / 1 warning, 3.89s MSBuild.
- Server adapter compile, because CP-3's runner project does not compile the five changed server files: output bin-c1065-server/, tested source 9652e7b90bc6e8c8447e10ab99af07a973e8f295, slot=granted waited=0s, 0 errors / 15 warnings, 44.73s MSBuild.
  Log: `.antiphon/c1065-s3-server-build.log`.
- No additional test driver or unchanged repetition after green. Round 3 changed only runtime/test source, so no second server adapter build was needed.

## Coverage lint and deferred obligations

Full-plan coverage ran at the initial implementation, previous green implementation and
final repaired implementation commits. Each exited 2 because future selected classes
do not yet exist. The final complete text is:

```text
PLAN-COVERAGE schema=1 mode=static plan="docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md" planSha256=8d780a561c59779df30b528061caea4c6842e64dfeb8d89259b377b0530cb7f3 inputsSha256= files=0
COVERAGE code=INPUT_INVALID planLine=0 planColumn=1 id= test="" name="" testPath="tests/Shared/TestClassificationMetadata.cs" testLine=0 detail="unresolved selected class"
PLAN-COVERAGE-END obligations=0 matched=0 missing=0 unmapped=0 pcIssues=0 pcAdvisories=0 result=invalid reachability=unproven
```

This is not clean static coverage. Its reported path is the reader's last enumerated source,
not a finding in TestClassificationMetadata.cs. Rerun on the integrated implementation.

- V-1, V-2, V-3, V-4, V-5: predecessor slices, not rerun or claimed passed here.
- V-9, V-10, V-11: S4 pending.
- V-12, V-13, V-14: S5 pending.
- V-15, V-16: S6 pending.
- V-17, V-18, V-19: S7 pending.
- V-20, V-21, V-22, V-27: S8 pending.
- V-23, V-24: S9 pending.
- V-25, V-26: S10 pending.
- R-1, R-2, R-3, R-4, R-5, R-6: deferred to S11's CP-11/12/13, none passed by this task.
- CP-1/2 belong to predecessors; CP-4–13 are outside this slice and pending their owners.
- Whole Unit: not run, explicitly excluded by this brief.
- Linux/Windows integrated qualification, isolated provider/OS acceptance and activation remain pending S11/rollout; controlled child exit is not OS process-tree proof.
- Server client routing integration remains assigned to S5 by TestDesign; this task compiled those clients and exercised the actual runner HTTP mapper/phone-home adapter.

S3 controls all remain **pending SourceLanding Mutation**:
PC-29, PC-30, PC-31, PC-32, PC-33, PC-34, PC-35, PC-36, PC-37, PC-38, PC-39, PC-40, PC-41, PC-42, PC-43, PC-44, PC-45, PC-46, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58, PC-59, PC-60, PC-61, PC-62, PC-63, PC-64, PC-65, PC-66, PC-67.
No variant is discharged by CP-3. PC-47 must target the landed pre-write proof-discard
decision (the precursor uses delivery-proof invalidation, not an attempted-write counter).
PC-64's actual replay decision is PriorTerminalRelease; its assertion precedes the separate
release-in-progress protection. Other slices' PCs (PC-1–28 and PC-68–200) also remain pending
their commissioned SourceLanding owners.
Round 3 adds ordinary exited/absent variants for G-57–60 and final identity/custody guards;
these do not discharge PC-52/53/57/58/59/60/67 or their required variants. Mutation must
inspect both fresh confirmation paths, including post-source-read generation/object/
manifest races and same-SHA ref/endpoint changes.

## Custody, cleanup and next action

Generated TRX/JSON/logs remain ignored under .antiphon; this Markdown preserves essential
receipts without moving generated payloads into source. The green checkpoint removed its
producer-owned row outputs; red-run clean was awaited. All six server alternate output
directories and the checkpoint bootstrap directory were removed using their exact producer
inventory. No owned child/build/test run remains active.

Run the full task-range evidence guard after this documentation commit (final result
reported with the pushed SHA in the caller report):
`pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef c1adb878a42f4c69258483e33dab3cd2a9797351 -HeadRef HEAD`.

Restart: **none**. Future activation owner: caller/orchestrator after integrated qualification.
Next: ordinary Review of S3, then caller adoption of the original Code task after S2
lands, then S4 source-policy. The authorized third repair round is complete.
