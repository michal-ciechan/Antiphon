# CARD-0817 fixture repair: authorized seams repaired; inherited guard red retained

Both authorized repairs are complete. Post-repair ordinary verification executed
53 tests: 52 passed, 1 confirmed inherited failure, 0 skipped. The full affected
RetiredTempContainerHostTests class passed 20/20. This is not a green Final Unit
qualification; the direct brief expressly prohibited whole-Unit/baseline sweeps.

## Identity and artifacts

- Repair Code task: f3fc782f-ffad-48c5-85b1-c50f89faadfa.
- Original Code task / landing owner: 2a876d8a-7681-47be-94bb-c8cba4abfade.
- Branch: feat/card-task-f3fc782f.
- Worktree: /work/worktrees/task-f3fc782f.
- Assigned repair base / red-first SHA: 73e469314ea9d13a65c5ad21375b924e7a21e925.
- Implementation and actual tested SHA: 274714f556b432452b2f303e37fa2f3c22f971d6.
- The later report commit contains only this report and a plan-link correction;
  its pushed SHA is in the caller-facing report. Receipts retain their actual SHA.
- Original plan: docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md.
- Repair plan: docs/superpowers/plans/2026-10-06-card-0817-fixture-repair.md.
- Stored report: /work/worktrees/task-f3fc782f/.antiphon/task-f3fc782f.md.
- Fresh raw evidence: /work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005041-b5d2/ and
  /work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005504-703a/.

## Changes and red-first proof

RetiredTempContainerHostTests.cs retains an exact 15/3 logical mount roster and
asserts the token bind's exact target, owned source, kind, directory form and
read-only mode. All five previous topology vectors remain; missing, foreign-source
and writable token mounts each additionally refuse before Docker mutation.

RemoteScriptContractTests.cs recognizes only ensure_runner_github_token_dir as
an additional host helper. Its first-command non-host refusal is asserted before
the sudo roster walk and in the independently green V-15 deploy method. No
production code, timeout, retry, or other helper exemption changed.

Pre-edit CP-18 and CP-19 each ran one test and failed on the intended current
fixture defect: expected [14,3] versus actual [15,3], and the token helper's sudo
install line respectively. These were ordinary reproductions, not deliberate
mutants. One implementation repair round was made and pushed before verification.
The exact red methods were rerun; the full class also exercised the repaired
mount method. No unchanged repetition followed the required scope.

## Checkpoint results

| CP | Pre-edit | Post-repair | Actual post-repair slot |
|---|---|---|---|
| CP-18 | 1 executed, 0 passed, 1 failed | 1 executed, 1 passed | slot=granted waited=0s |
| CP-19 | 1 executed, 0 passed, 1 failed (token helper) | 1 executed, 0 passed, 1 failed (inherited readlink) | slot=granted waited=0s |
| CP-20 | Not selected | 20 executed, 20 passed | slot=granted waited=0s |
| CP-4 | Not selected | 25 executed, 25 passed | slot=granted waited=0s |
| CP-5 | Not selected | 6 executed, 6 passed | slot=granted waited=0s |

Every intended class/method was checked against the fresh TRX TestDefinitions,
UnitTestResult outcomes and nonzero counters. There were no skips. The 53
post-repair executions represent 52 unique methods because CP-18 is also in
CP-20. The full class took 539 seconds including test-host overhead; the complete
post-repair checkpoint run took 13m26s. Both checkpoint invocations completed
with exit 1, for the failures explicitly recorded here, and were fully awaited.

## Invariant outcomes

| ID | Actual outcome |
|---|---|
| V-1 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-2 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-3 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-4 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-5 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-6 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-7 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-8 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-9 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-10 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-11 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-12 | PASS at repair SHA in CP-4. |
| V-13 | PASS at repair SHA in CP-4. |
| V-14 | PASS at repair SHA in CP-4. |
| V-15 | PASS at repair SHA in CP-5, including new first-command host refusal. |
| V-16 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-17 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-18 | Not rerun in this explicitly narrow repair; prior ordinary PASS retained in .antiphon/task-2a876d8a.md, not relabeled to this SHA. |
| V-19 | PASS in CP-18 and CP-20: exact token-bind receipt plus all eight topology vectors. |
| V-20 | New helper guard assertions pass; V-15 is green. CP-19 remains FAIL on the independent inherited readlink helper, so the entire nested-lane invariant is not green. |
| R-1 | Not rerun; prior six-method selection and 33/33 full workspace-class PASS remain historical evidence. |
| R-2 | PASS in CP-4, all 25 methods including the 22 original regressions. |
| R-3 | PASS in CP-5, all five original regressions plus V-15. |
| R-4 | PASS in CP-20, full 20-method retired-temp class including census, lease and persistence behavior. |

## Remaining inherited finding and caller-owned work

CP-19 now fails on exactly the original CP-13 failure:

    sudo outside a declared host-lane case or helper: canonical="$(sudo -n readlink -e -- "$source" 2>/dev/null)" || c1008_refuse RecycleContainerStateUnknown

The original pre-CARD-0817 base is 71685b84772b82517c2db5dd5d18e085ca8f360a.
I inspected its retained CP-13 TRX, failure record and source/build receipt under
/work/worktrees/task-2a876d8a/.antiphon/c0817-baseline-checkpoints/20261006-003610-f93f/.
They prove one executed failure with clean source and verified producing build
at that exact SHA. No new baseline sweep was run. c1008_owned_mounts has no local
host guard; exempting it merely to turn the test green would weaken the check.
Its disposition remains with caller/Review under this two-seam repair brief.

Deferred to caller-owned Final qualification, not passed here: original CP-9
whole Unit coverage, CP-7's recorded inherited failures, CP-19's inherited guard
finding (original CP-13), and original F-3 websocket outcome disposition. Original
CP-8 and CP-17 passes are historical, not fresh checks in this repair. No full
RemoteScriptContractTests, assembly, whole Unit, E2E or Pty run was made.
Original operator acceptance steps 1-5, token provisioning, live credential
probes, canary and staged runner rollout remain post-land caller/operator work.

## Pending SourceLanding Mutation

- PC-1: PENDING.
- PC-2: PENDING.
- PC-3: PENDING.
- PC-4: PENDING.
- PC-5: PENDING.
- PC-6: PENDING.
- PC-7: PENDING.
- PC-8: PENDING.
- PC-9: PENDING.
- PC-10: PENDING.
- PC-11: PENDING.
- PC-12: PENDING.
- PC-13: PENDING.
- PC-14: PENDING — both username-output and whitespace-handling variants.
- PC-15: PENDING.
- PC-16: PENDING.
- PC-17: PENDING.
- PC-18: PENDING.
- PC-19: PENDING.
- PC-20: PENDING.
- PC-21: PENDING — remove the first non-host refusal; use the green V-15 method.
- PC-22: PENDING — missing, foreign-source and writable token-mount admission variants.

Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
The red CP-19 method cannot be a valid positive-control witness until its unrelated
failure is resolved. Ordinary green does not discharge any PC.

## Source qualification, gates and invocations

All test-project builds and test rows used granted host slots. Red-first producing
build: slot=granted waited=15s; repair producing build: slot=granted waited=0s.
Every test row: slot=granted waited=0s. Both real checkpoint launchers had
slot=granted waited=0s. Source remained committed, clean and unchanged through
each run; every test row has dirty=0, sourceState=clean and buildSource=verified.

The receipt validator returned exit 0 with:

    CHECKPOINT SOURCE VALID source=274714f556b432452b2f303e37fa2f3c22f971d6 rows=4

It selected CP-18, CP-20, CP-4 and CP-5. CP-19 is retained as red diagnostic
proof, not a green certificate. The original inherited receipt was inspected
at its own base SHA, never relabeled as this repair's run.

The red-first two-row manifest was declared before execution. The repair plan's
closed five-row list was committed with the implementation. No unlisted
application build/test was run. The isolated checkpoint-tool bootstrap was
needed to execute those manifests and ran through build-slot.ps1 with
OutputPath=bin-c0817-fixture-tool/. Subsequent tool launch reused that output.
One initial repair launcher mistakenly supplied an abbreviated expected SHA;
validation refused it (exit 2) before any build/test, slot=granted waited=0s.
The corrected invocation supplied the full committed SHA. This was an invocation
correction, not an additional repair or proof repetition.

Read-only TRX/receipt/history checks and output cleanup were not test drivers.
One cleanup call initially used a nonexistent net9.0 subdirectory for the tool
DLL and launched nothing; using its actual flat output location completed cleanup.

Re-run the repair selection from a committed checkout with its actual full HEAD:

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0817-fixture-repair -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c0817-fixture-tool/ -- run --plan docs/superpowers/plans/2026-10-06-card-0817-fixture-repair.md --rows CP-18,CP-19,CP-20,CP-4,CP-5 --serial --expected-source-sha <full-HEAD>

GET /api/runner-defaults (revision 2) and GET /api/session-runners were read.
No runner/platform pin or setting changed. No secrets or live credentials were
read. Restart: none for this fixture-only repair; original runner activation
remains owned by the caller/operator's staged rollout, with no server restart.

## Unedited CHECKPOINT lines

Red-first run 20261006-005041-b5d2:

```text
CHECKPOINT CP-18 commit=73e469314ea9d13a65c5ad21375b924e7a21e925 build=ok filter=/*/*/RetiredTempContainerHostTests/C994_Production_mount_topology_is_proven* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005041-b5d2/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=73e469314ea9d13a65c5ad21375b924e7a21e925 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=73e469314ea9d13a65c5ad21375b924e7a21e925 build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005041-b5d2/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=73e469314ea9d13a65c5ad21375b924e7a21e925 sourceState=clean buildSource=verified
```

Post-repair run 20261006-005504-703a:

```text
CHECKPOINT CP-18 commit=274714f556b432452b2f303e37fa2f3c22f971d6 build=ok filter=/*/*/RetiredTempContainerHostTests/C994_Production_mount_topology_is_proven* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005504-703a/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=274714f556b432452b2f303e37fa2f3c22f971d6 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=274714f556b432452b2f303e37fa2f3c22f971d6 build=reused filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005504-703a/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=274714f556b432452b2f303e37fa2f3c22f971d6 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=274714f556b432452b2f303e37fa2f3c22f971d6 build=reused filter=/*/*/RetiredTempContainerHostTests/C994_* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005504-703a/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=274714f556b432452b2f303e37fa2f3c22f971d6 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=274714f556b432452b2f303e37fa2f3c22f971d6 build=reused filter=/*/*/DindRunnerContractTests/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005504-703a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=274714f556b432452b2f303e37fa2f3c22f971d6 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=274714f556b432452b2f303e37fa2f3c22f971d6 build=reused filter=/*/*/RemoteScriptContractTests/(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)|(C1008_Recycle_exact_default_volumes*)|(Scrub_covers_github_token_prefixes*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f3fc782f/.antiphon/checkpoints/20261006-005504-703a/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=274714f556b432452b2f303e37fa2f3c22f971d6 sourceState=clean buildSource=verified
```

Previously executed baseline CP-13 (retained receipt inspected, not rerun here):

```text
CHECKPOINT CP-13 commit=71685b84772b82517c2db5dd5d18e085ca8f360a build=ok filter=/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2a876d8a-baseline/.antiphon/checkpoints/20261006-003610-f93f/rows/CP-13/run.trx slot=granted waited=75s dirty=0 source=71685b84772b82517c2db5dd5d18e085ca8f360a sourceState=clean buildSource=verified
```

## Cleanup and publication

Both checkpoint runs are terminal and both launchers released their slots. The
checkpoint tool removed the owned bin-c0817-fixture-red/ and bin-c0817-fixture/
outputs across all producing projects; the separately owned checkpoint bootstrap
output was removed after cleanup completed. Only ignored checkpoint evidence
(including build-log directories named after those outputs) remains. No owned
run is still active. Generated TRX, receipts, logs and checkpoint directories
remain ignored; only this individual Markdown report is committed.

The implementation slice was pushed fast-forward immediately. The final report
slice is also committed and pushed, without rebase, amend, reset or force push.
git diff --check and the evidence-history guard passed for the implementation;
the final caller-facing message records the guard result over the entire repair
range and original Code range through the report commit, and remote-confirmed SHA.

--- next stage ---
next: review
handoff: Review the two repaired CARD-0817 fixtures at the pushed repair branch; 52/53 checks pass, with the exact inherited c1008_owned_mounts guard failure retained. Whole-Unit qualification and live acceptance remain caller-owned. Landing owner is 2a876d8a; PCs remain pending post-land Mutation.
artifact: docs/superpowers/plans/2026-10-06-card-0817-fixture-repair.md
