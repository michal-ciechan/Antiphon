# CARD-0667 S2b Code evidence

Implemented input ordering; CP-10 passes all four intended results. Both public input routes share the launch gate, preserve input bytes, advance the existing completed-input revision and refuse writes once conditional release owns the generation. Automatic release remains dormant, with no wire caller.

Original Code task / landing owner: `fa3ed513-4a19-4911-b896-2888553dc49c`.
Branch: `feat/card-task-fa3ed513`.
Worktree: `/work/worktrees/task-fa3ed513`; assigned desktop checkout `C:\Antiphon\worktrees\card-task-fa3ed513` was not accessed.
Task base: `691606689a7587db0b06c891471c5371e0440025`.
Test-first commit: `dc0b2bbc393a7354a50f6d57902a630797fd0c57`.
Tested implementation commit: `b7b13c5f1a68530a352063ddf34fbb1079334133`.
This evidence-only follow-up changes no implementation, tests or manifest; the final progress marker identifies its pushed tip separately from the actual tested SHA.
Plan: `/work/worktrees/task-fa3ed513/docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`, S2b / CP-10.

## Changes

- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: normal input acquires the existing per-session launch semaphore. Both routes use an already-locked writer and the production `InputAuthorization` decision. Normal input throws a typed `InputRefusedException`; conditional input returns `release-in-progress`. Existing generation/output fences, successful-write revision increments and uncertain/composer custody remain active. Empty input retains its no-op behavior. No recursive lock-taking call was introduced.
- `tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs`: four planned methods, real native transcript/tailer setup, fake child writes/signals and fake time. Tests pin launch-gate exclusion without scheduling sleeps, stale proof after either input route, exact LF/bracketed-paste/separate-Enter bytes, release waiting for an active writer, retained composer custody and both routes refusing later input after an unresolved release of a still-live generation. The pre-existing S2a pending-input scenario was adjusted to finish its writer barrier before awaiting release; its PendingDelivery/retained-custody assertions were preserved. That S2a method was not selected by CP-10; the same changed ordering is exercised inside both new input methods.
- Plan CP-10: added the four trailing method wildcards required by the pinned TUnit discovery filter. Exactly four results were selected on both runs.

## Verification scope and outcomes

The explicit brief requires S2b only, CP-10 only, and no whole-Unit run. Execution follows that bounded instruction and the amended plan. The embedded generic Final profile is broader: this is completed S2b ordinary verification, not a claim of whole-Unit/full-class or full-card Final qualification.

| ID | Actual outcome |
|---|---|
| CP-10 / V-1 S2b | PASS: Linux 4 executed, 4 passed, 0 failed, 0 skipped. One test-first run and one implementation run; no further repeat or repair round. |
| V-1 remainder | Not rerun: earlier S1a/S1b/S2a evidence remains separate. S2c/CP-11 transport and full Linux/Windows CP-1/CP-5 remain deferred. |
| V-2 | Not run; server lifecycle/persistence/delivery/attention remains with S3/S4 and final CP-2/CP-6. |
| V-3 | Not run; discovery/recovery remains with S3/S4 and final CP-3/CP-6. |
| R-1 | Deferred to S4c CP-1/CP-5 classifier/capacity qualification. |
| R-2 | Deferred to S4c CP-2 operator-route and legacy-intent qualification. |
| R-3 | Deferred to S4c CP-4 Shared/standing compatibility qualification. |
| R-4 | Deferred to Windows CP-5/CP-6 at S4c. |
| Manual S2b | PASS: static call-site inspection finds only the internal release definition and test invocation; no Program, phone-home adapter or dispatcher caller. Diff whitespace check passes. No provider/native process launch, live release or deployment acceptance was required or attempted. |

Whole Unit and full affected integration classes were not run under the explicit closed-list instruction. Their broader shared-input impact is not certified by this slice. Final qualification IDs CP-1 through CP-6 and the full-card manual acceptance remain pending; none is marked passed here. No full-assembly run occurred.

Both fresh TRX files were inspected for `TestDefinitions/UnitTest/TestMethod` class/method identities and nonzero result counts:

| Method (all in Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests) | Test-first | Implementation |
|---|---|---|
| Activity_resets_the_qualification_window | Passed; existing qualification already resets revisions | Passed |
| Input_winning_the_gate_invalidates_release | Failed: child writes 3 instead of 2 while launch gate held | Passed |
| Conditional_input_invalidates_release | Passed; existing conditional gate/revision behavior | Passed |
| Release_winning_the_gate_refuses_later_input | Failed: production decision Allowed instead of ReleaseInProgress before exit | Passed |

The two red cases are named behavioral assertions, not build/fixture failures. Already-green cases are reported honestly; no intentional mutation was run to manufacture ordinary red. PC proof remains separate.

## Unedited checkpoint evidence

```text
CHECKPOINT CP-10 commit=dc0b2bbc393a7354a50f6d57902a630797fd0c57 build=ok filter=/*/*/TerminalSeatReleaseTests*/(Activity_resets_the_qualification_window*)|(Input_winning_the_gate_invalidates_release*)|(Conditional_input_invalidates_release*)|(Release_winning_the_gate_refuses_later_input*) executed=4 passed=2 failed=2 skipped=0 trx=/work/worktrees/task-fa3ed513/.antiphon/checkpoints/20261005-111259-b1d0/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=dc0b2bbc393a7354a50f6d57902a630797fd0c57 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=b7b13c5f1a68530a352063ddf34fbb1079334133 build=ok filter=/*/*/TerminalSeatReleaseTests*/(Activity_resets_the_qualification_window*)|(Input_winning_the_gate_invalidates_release*)|(Conditional_input_invalidates_release*)|(Release_winning_the_gate_refuses_later_input*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-fa3ed513/.antiphon/checkpoints/20261005-111456-3d09/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=b7b13c5f1a68530a352063ddf34fbb1079334133 sourceState=clean buildSource=verified
```

Red run: `.antiphon/checkpoints/20261005-111259-b1d0/report.json`; wall 27 seconds, build 19.157 seconds, tests wall 2.932 seconds.
Green run: `.antiphon/checkpoints/20261005-111456-3d09/report.json`; wall 78 seconds, build 22.394 seconds, tests wall 2.637 seconds.
Both row slots were granted with waited=0s. The green **build** slot was separately granted after waited=45s; the phase receipt records this rather than the row's CHECKPOINT field. Red build wait was 0s. One isolated CP-10 build ran per invocation; the other 17 manifest build definitions have state `unused` (the tool's summary lists all definitions).

Green receipt validation returned exit 0:

```text
CHECKPOINT SOURCE VALID source=b7b13c5f1a68530a352063ddf34fbb1079334133 rows=1
```

Commands used (checkpoint tool invocations have no outer build slot because their drivers own their leases):

```powershell
# Declared setup: no prepared tool in this checkout. Only non-CP build.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c667-s2b-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false
# Executed at each of the two committed SHAs above.
dotnet run --no-build --no-restore --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md --rows CP-10 --expected-source-sha <committed-sha> --max-wait 50s
dotnet run --no-build --no-restore --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -- wait 20261005-111456-3d09 --max-wait 50s
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261005-111456-3d09/report.json -ExpectedSourceSha b7b13c5f1a68530a352063ddf34fbb1079334133 -Rows CP-10
```

Bootstrap passed (0 errors, one existing CS8602 warning in TaskOwnerGuard.cs), slot=granted waited=0s, held=5s. No unlisted test run. The green launcher returned 75 once; the subsequent wait completed at exit 0. No source edits occurred during either run. No timeout or assertion was relaxed, no inherited-red claim, no flake/repeat allowance used.

Checkpoint cleanup completed, including the red run's clean command; the tool bootstrap directory was separately removed after all tool commands finished. Verified zero `bin-c667-*` directories remain in this worktree. Generated logs/JSON/TRX stay ignored. This individual Markdown report is the only retained evidence file added to Git. Full base..HEAD evidence-policy validation and final remote SHA are reported at settlement.

## Pending Mutation controls

Every PC remains pending for caller-commissioned method-scoped SourceLanding Mutation after the plan's completed S4c candidate lands. This task discharges none of PC-1 through PC-90.

| S2b PC | Pending variants / detecting method |
|---|---|
| PC-31 | `Activity_resets_the_qualification_window`: independently change binding (with its valid expected identity), file digest, transcript revision, consumed byte count, last prompt, last end, input revision and output revision. Assert new first-observed time, zero elapsed and a full new 120-second interval; no downstream Unknown guard masks the reset. |
| PC-35 | `Input_winning_the_gate_invalidates_release`: remove only normal-input launch-gate acquisition while retaining completed-input revision. Writer count under the held real launch semaphore must fail. |
| PC-36 | `Conditional_input_invalidates_release`: omit the completed input revision increment on the fake-child/backend write path. With native transcript/output unchanged and composer clear, the old release token must no longer get StaleObservation. |
| PC-37 | `Release_winning_the_gate_refuses_later_input`: omit the release-in-progress branch from the actual already-locked input decision. Observe the decision before exit, then ordinary and conditional public routes; zero later child writes. |

Authenticated read-only runner defaults/catalogue calls both succeeded. Revision 2 resolved the current default to an available Linux runner; the Windows lane was available and the temporary runner unavailable/draining. No fleet location was embedded and no host/platform pin was added.

Restart: **none**. Owner: caller/orchestrator for any separately commissioned activation. Next: ordinary Review of this original Code task, retaining the bounded CP-10 verification scope and pending PCs. Caller lands the original Code owner after Review; S2c and subsequent slices were not started.
