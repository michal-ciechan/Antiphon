# CARD-0959 PATH isolation and dispatch repair

Task ef329fb0 is the new landing owner, based on
`d45a7915d2c9fa718363db0b0faeb522b08b95e3` (prior Code task bd02f8d9).
The original plan remains
`docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md`.

The caller refined the test-only repair to also remove the accidental Codex
authentication refusal before dispatch claim. Create/retry admission and the
runner's cold-launch backstop remain required. The new regression observes a
real framed signed-out response and requires a persisted claim, matching warm
session, reuse event, queued brief, and zero cold launches.

### Checkpoints

This additive manifest covers the refinement. CP-9 first runs red against the
unchanged dispatcher, then CP-10/11/5/8 run after the repair. CP-5 and CP-8
retain the original filters/counts and reuse CP-10's same-source isolated build.
Original-plan CP-4 passed at the PATH repair SHA; a post-auth-repair rerun is
deferred if it cannot fit the caller's 45-minute timebox.
All rows run serially with committed expected source SHA. No full assembly run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-9 | auth-test | `tests/Antiphon.Tests -> bin-c959-auth-red/` | signed-out-warm-red | `/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session` | V-27 | 1 result; expected assertion failure before repair | 1 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | auth-fix | `tests/Antiphon.Tests -> bin-c959-auth-green/` | full-phone-home-projection | `/*/*/PhoneHomeTaskDispatchProjectionTests/*` | V-27,R-6 | 8 results, 0 failed/skipped | 8 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | auth-fix | `tests/Antiphon.SessionRunner.Tests -> bin-c959-auth-backstop/` | runner-auth-backstop | `/*/*/CodexProviderAuthRoutingTests/*` | R-7 | full class, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | auth-fix | CP-10 | portable-existing-refusals | `/*/*/(CodexPhoneHomeCreateTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)/*` | R-1 | 22 expanded executions, full classes, 0 failed/skipped | 22 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | auth-fix | CP-10 | portable-final-unit | `/*/*/*/*[Category=Unit]` | R-5 | >=3961 executed, 0 failed, only the 33 recorded Windows exclusions; jq cases execute | 3961 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

V-27 guards warm claim/reuse despite definite signed-out Codex evidence.
R-6 covers every existing phone-home dispatch projection case. R-7 preserves
the real composed runner authentication routing and cold-launch refusal.
The original 192 PCs and all variants remain pending SourceLanding Mutation;
PC-256 is also pending: restore the removed pre-claim invocation and its helper's
internal visibility together, then run only
`PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`.
The claim-status assertion must fail with the signed-out refusal; restore and
run the same method green. No deliberate mutant was run in Code.

## Outcome and remaining work

Final verification is incomplete at the caller's 45-minute timebox. Both requested
repairs are committed. The latest fixture correction is unverified. Next is Code:
finish the Linux refinement checks, native Windows CP-2 (29/29 required) at the
final handoff SHA, then short delta Review. No approval or restart was requested.

The PATH-only repair is `0035492ba4335d995a4fe9ed3429a42674e6d8de`.
The dispatcher repair is `c57e1980fe7388f122cf69a73f20908ca14293de`.
The last code/test SHA is `a65bb44ac94ea8681bf5d8b9854c982ce82f7b5d`:
it adds the missing Codex definition to the integration fixture after CP-10
reported that omission. No subsequent test has verified that SHA.

The dispatcher pre-claim refusal is removed and the service helper is private.
The service's create/retry calls and the runner's `RejectSignedOutCodexAsync`
backstop remain unchanged. Launcher production code, assertion thresholds,
timeouts, the Windows assertions and all four V-8 case tuples are unchanged.

### PATH audit

- Both explicit empty PATH arguments in `CodexCliVersionWindowsTests` now use
  `kit.EmptyPath`, an existing empty directory under the fixture's owned root.
  V-8 can no longer resolve a machine-installed node through the empty-string
  fallback. The same isolation is applied to the V-7 direct-node helper call.
- V-6 PATH and sibling node candidates are created/copied into owned layouts;
  PATHEXT is explicit. V-7 native and direct-node/javascript selectors are
  absolute owned paths. Missing payload/package/wrapper cases remain unchanged.
- Every `CodexWindowsLaunchPolicyTests` method was audited. Missing-node cases
  already use existing empty directories; positive PATH/sibling cases create
  their own node files. Rooted shim/node/native and missing payload checks use
  owned layouts. No tested resolution depends on USERPROFILE, APPDATA,
  ProgramFiles, nvm, global npm, or host codex installs.
- Fixture defaults can capture inherited PATH/PATHEXT, but the other absolute
  owned selectors make host node/codex installs irrelevant. The fixture's pwsh
  child needs installed PowerShell as a declared test-tool prerequisite; it does
  not launch a host node/codex child. The notepad path is only compared.
- Existing residual precondition: the relative-node policy case expects no
  matching `node_modules/@openai/codex/bin/codex.js` under the process CWD. A
  repository-local npm installation could violate it; this is not global PATH
  resolution and was not changed in this scoped fix.
- No checkpoint tests/census source changed. The brief says 377, but the actual
  base and current `scripts/lib/checkpoint-usage.ps1` census is 365.

### Executed verification

All launched builds/tests were serial and slot-gated: `slot=granted waited=0s`.
No full assembly run, loaded repetitions, deliberate mutants, timeout widening,
assertion relaxation, or concurrent source edits occurred. GET runner-defaults
and session-runners succeeded after an initial 502; no runner/platform pin was
used. Native Windows is not available in this checkout.

| Run | Tested SHA | Actual outcome |
|---|---|---|
| 20261004-013727-a2d0 | 0035492ba4335d995a4fe9ed3429a42674e6d8de | Original CP-1 5/5, CP-3 5/5, CP-4 8/8; zero failures/skips; terminal 0. Each fresh TRX method inspected. Exact-SHA validator accepted all three clean receipts with verified build provenance. |
| 20261004-015500-8be2 | d6664056f5c6a32211761b086a8b92cee6f9d1f4 | CP-9 1 executed/1 failed: missing RunnerStoreId fixture setup. Not guarded-defect evidence. |
| 20261004-015843-993c | a100fc29b8d4ebf1eeac3b2b4a6d8a8b6e7c1ca0 | CP-9 1 executed/1 failed: capacity held task Queued before auth. Not guarded-defect evidence. |
| 20261004-020428-8191 | a8ec92f92673baef965afe0ae35ad5f869eec381 | CP-9 1 executed/1 failed at claimed.Status: expected Dispatched, actual Failed, specifically Codex signed-out pre-session refusal. This is the guarded defect on the unchanged dispatcher. |
| 20261004-020947-537c | c57e1980fe7388f122cf69a73f20908ca14293de | Isolated Antiphon.Tests build succeeded. CP-10 8 executed, 7 passed, 1 failed: new method passed the removed auth gate but lacked a configured Codex definition. All seven pre-existing methods passed. CP-5 interrupted without TRX; CP-11 and CP-8 never started. Run explicitly stopped; final wait exit 6 confirms stopped executor, not green. |

The CP-10 fixture failure is ours, not an inherited failure. The correction is
committed at a65bb44a; its rerun is pending. The three original red-baseline runs
used changed fixture source, not repeated unchanged proof. CP-10's stopped run
has clean same-SHA build provenance in state.json but no finalized report;
no whole-run clean/green claim is made.

The only unlisted build was the slot-gated checkpoint tool bootstrap to
`tools/Antiphon.Checkpoints/bin-c959-ef329fb0-tool/`, required to execute the
manifest; it succeeded with one existing CS8602 warning. Checkpoint cleanup
removed the task's alternate outputs after all execution stopped.

### V/R status and deferred Final work

| IDs | Actual status |
|---|---|
| V-1, V-2, V-3, V-4, V-5 | CP-1 passed at PATH-only SHA. |
| V-6, V-7, V-8 | Native Windows CP-2 pending; audit alone is not a pass. |
| V-9, V-10, V-11, V-12 | CP-3 passed at PATH-only SHA. |
| V-13 | Current CP-3 body passed; inherited full descriptor/placeholder variants remain incomplete. |
| V-14, V-19 | CP-4 passed at PATH-only SHA; post-dispatch-repair CP-4 pending. |
| V-15, V-16, V-17, V-18, V-20 | Retired by the landed re-freeze, not claimed passed. |
| V-21, V-22, V-23, V-24, V-25, V-26 | Current CP-4 bodies passed at PATH-only SHA; inherited warm/recovery/delivery variants remain incomplete; post-repair rerun pending. |
| V-27 | Expected original-defect red confirmed; repaired green pending after fixture correction. |
| R-1 | CP-5 interrupted; complete auth/admission classes pending at final SHA. |
| R-2, R-3 | Original CP-6/7 not rerun here; earlier Code evidence is inherited, not current-SHA proof. |
| R-4 | Native Windows policy class pending (inside CP-2). |
| R-5 | Whole Unit CP-8 not run; pending. |
| R-6 | Full projection class 7/8 at c57e1980; pending rerun after fixture correction. |
| R-7 | Runner backstop CP-11 not run; pending. |

The prior Code report `.antiphon/task-bd02f8d9.md` already marked Final incomplete:
V-13 descriptor variants; V-21 local Grok/remote Claude/Grok eligible/busy and
enqueue/retry/old-generation/degraded-screen combinations; V-22 remote recovery
and fault seams; V-23..26 retry/warm recovery combinations; and manual guard
qualification. This repair does not close those gaps. SourceLanding Mutation
cannot replace missing ordinary qualification. The new test proves persisted
claim/reuse and queued delivery intent, not receipt of a complete UserPrompt.

All original 192 PC IDs and every variant remain pending SourceLanding Mutation:
PC-1..14,16..84,89..109,111..120,122..127,153..159,184..188,190..192,194,
199..203,205..255. Added PC-256 is also pending (193 total). No PC is discharged.

Original landing owner for this repair: ef329fb0-adcb-43eb-bad2-7c62bcb36728;
branch `feat/card-task-ef329fb0`; worktree `/work/worktrees/task-ef329fb0`.
The final documentation commit and origin verification are in the caller report.
Restart: server, caller owns activation after Review/land; none performed here.
The PATH-only part requires no restart; the refinement changes server code.

### Checkpoint receipts

The following lines are copied without editing from the completed run reports.
Raw TRX, logs, receipts and stopped-run state stay ignored under
`.antiphon/checkpoints/<run-id>/`. Full caller report:
`.antiphon/task-ef329fb0.md`.

```
CHECKPOINT CP-1 commit=0035492ba4335d995a4fe9ed3429a42674e6d8de build=ok filter=/*/*/CodexCliVersionProbeTests*/C959_* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-ef329fb0/.antiphon/checkpoints/20261004-013727-a2d0/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0035492ba4335d995a4fe9ed3429a42674e6d8de sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=0035492ba4335d995a4fe9ed3429a42674e6d8de build=ok filter=/*/*/RunnerCodexCliEvidenceTests*/C959_* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-ef329fb0/.antiphon/checkpoints/20261004-013727-a2d0/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0035492ba4335d995a4fe9ed3429a42674e6d8de sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=0035492ba4335d995a4fe9ed3429a42674e6d8de build=ok filter=/*/*/CodexCliObservationTests*/C959_* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-ef329fb0/.antiphon/checkpoints/20261004-013727-a2d0/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=0035492ba4335d995a4fe9ed3429a42674e6d8de sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=d6664056f5c6a32211761b086a8b92cee6f9d1f4 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ef329fb0/.antiphon/checkpoints/20261004-015500-8be2/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=d6664056f5c6a32211761b086a8b92cee6f9d1f4 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a100fc29b8d4ebf1eeac3b2b4a6d8a8b6e7c1ca0 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ef329fb0/.antiphon/checkpoints/20261004-015843-993c/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=a100fc29b8d4ebf1eeac3b2b4a6d8a8b6e7c1ca0 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a8ec92f92673baef965afe0ae35ad5f869eec381 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-ef329fb0/.antiphon/checkpoints/20261004-020428-8191/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=a8ec92f92673baef965afe0ae35ad5f869eec381 sourceState=clean buildSource=verified
```
