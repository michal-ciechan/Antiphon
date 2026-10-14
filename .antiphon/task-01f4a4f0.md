# CARD-1258 Code report

Implementation and scoped regressions pass; Final verification is incomplete because the whole Unit lane hit its unchanged 40-minute deadline without a final TRX. No Final-green claim is made. This handoff remains next: code under the Code-stage contract until R-3 is resolved.

## Ownership and publication

- Original Code task / landing owner: 01f4a4f0-6ba6-4753-afc4-4f21063e74ec.
- Branch: feat/card-task-01f4a4f0.
- Worktree: /work/worktrees/task-01f4a4f0.
- Task base / fetched master at start: 5665ddb1a3ba730f3b858acc109e65b5d7304d6c.
- Tests-first commit: 927fa3593b756768ee005e8fb8647073aaa1d70f.
- Discovery-safe filter correction / actual red source: e046c7473e25057de3be0e42aea13dac2a28dae7.
- Implementation / actual tested fixed source: 2ffcb09e05976a23397dfd3e245e20b69d988324.
- Supplemental base-reproduction plan: e6691ed53ad48162714f07d2994782d8163b514b.
- Each slice was committed and pushed to the assigned branch. The completion message records the final report commit and matching remote tip; later documentation commits do not relabel the tested source.
- Plan: docs/superpowers/plans/2026-10-14-card-1258-matrix-unknown-plan.md.
- Unbound Mutation candidate: docs/superpowers/mutations/2026-10-14-card-1258-candidate.md.

## Result and changed files

The model now treats a stage-pin read failure as unknown for every effective source, before agent/pin/origin labels. The component uses that same predicate to withhold effective candidates, stored candidates, and stored origin labels, while retaining the alert and enabled Configure action. Successful reads and the existing chain-read behavior retain their controls.

ComplexityEffective.Compose passes a null missing chain only for the default tier. Preferred pins on explicit non-default empty tiers therefore do not append RolePolicy or diagnose an unused invalid policy. Dispatch composition is unchanged.

Production files: client/src/features/settings/routingSettingsModel.ts; client/src/features/settings/RoutingSettingsTab.tsx; server/Application/Services/ComplexityEffective.cs.
Tests: the two corresponding client test files; tests/Antiphon.Tests/Application/ComplexityEffectiveComposeTests.cs; tests/Antiphon.Tests/Application/ComplexityEffectiveHttpTests.cs. Existing complexityChains client tests and ComplexityRoutingComposeTests are included as adjacent full-file/class coverage.

## Ordinary verification outcomes

| ID | Actual outcome |
|---|---|
| V-1 | PASS: 21 literal model cases, seven sources x three tiers. |
| V-2 | PASS: 21 component cases, exact alert and no candidate/origin/block promise, Configure enabled. |
| V-3 | PASS: three expanded compose results, exact ordered Hard/Medium/Easy candidates; only Medium includes RolePolicy. |
| V-4 | PASS: one compose result, unused invalid policy does not create a candidate or diagnostic. |
| V-5 | PASS: one persisted-pin HTTP method checks all three tiers, wire lists, source/default flags and pin ID. |
| R-1 | PASS: three complete client files, 127 passed, 0 failed, 0 skipped. |
| R-2 | PASS: three complete server classes, 33 passed, 0 failed, 0 skipped: ComplexityEffectiveComposeTests 13, ComplexityEffectiveHttpTests 4, ComplexityRoutingComposeTests 16. |
| R-3 | INCOMPLETE: whole Unit lane timed out at 40 minutes, no final TRX and no final counts. Ten observed failures reproduced at the unchanged base. Windows-only skip messages are not passes. |
| R-4 | PASS: client typecheck and production bundle, exit 0; routine bundle-size warning. |
| M-1 | PASS: final source/diff inspection confirmed both candidate paths and stored origin use the shared predicate, null-versus-empty composer semantics and literal HTTP expectations; diff whitespace check passed. No live/browser acceptance was required. |

The brief's no-Unit sentence conflicts with the higher-priority Code-stage Final contract and its own Final profile. R-3 was therefore retained and attempted. This is a bounded Unit-category selection, not a full integration-assembly run. No unbounded integration classes were selected. The plan recorded the invariant and estimated cost before execution. No timeout was widened, assertion loosened, retry added, or passing proof repeated. The Final request remains Final; no incomplete ID is marked passed or silently deferred as an Interim result.

### Per-checkpoint counts

| CP | Source group | Actual result |
|---|---|---|
| CP-1 | tests-first | Client: 127 total, 87 passed, 40 failed, 0 skipped; expected assertion failures. |
| CP-2 | tests-first | Server: 33 executed, 29 passed, 4 failed, 0 skipped; expected assertion failures. |
| CP-3 | fixed | Client: 127 passed, 0 failed, 0 skipped. |
| CP-4 | fixed | Server: 33 executed, 33 passed, 0 failed, 0 skipped. |
| CP-5 | fixed | Unit: timeout=40m; executed/passed/failed/skipped unavailable, no final TRX. |
| CP-6 | fixed | Client build: exit 0; test counts not applicable. |
| CP-7 | base | 1 executed, 0 passed, 1 failed, 0 skipped. |
| CP-8 | base | 1 executed, 0 passed, 1 failed, 0 skipped. |
| CP-9 | base | 1 executed, 0 passed, 1 failed, 0 skipped. |
| CP-10 | base | 5 executed, 0 passed, 5 failed, 0 skipped. |
| CP-11 | base | 2 executed, 0 passed, 2 failed, 0 skipped. |

The red client evidence has 18 new model failures, the deliberately changed existing model expectation, and all 21 new component failures. The other source/tier rows are preservation controls. The four red server results are Hard/Easy extra RolePolicy candidates, the unused invalid-policy diagnostic, and the real HTTP projection's extra RolePolicy. All fail on the guarded assertions, not fixture/build errors. Fresh server TRX counters and intended class/method identities were inspected for both red and fixed runs, including all five expanded new server results. Baseline TRX identities/counts were also inspected individually.

## Findings versus disclosures

No task-specific introduced failure was observed. Implementation regressions requested by CARD-1258 are reproduced red and fixed green. The following are disclosed verification limits, not invented implementation findings:

1. R-3 did not complete. Its console's ten failures were reproduced with the same assertions at base 5665ddb1a3ba730f3b858acc109e65b5d7304d6c. This establishes inheritance only for those ten failures; it says nothing about unreported Unit results or the timeout's cause.
2. All three aggregate reports retain a cleanup_refused condition. Both receipt validators returned exit 2, reason cleanup_refused, for every report. Thus there is no accepted aggregate green certificate, even though source start/end are clean and buildSource is verified. Raw receipts and CHECKPOINT lines remain unedited.
3. The host is Linux. Windows-only Unit skip messages do not establish Windows coverage. No E2E, full client suite, full integration assembly or deliberate Mutation was required/run.

Baseline failures (all reproduced once, no flake claim):

- CP-7: Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata: lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests.
- CP-8: Antiphon.Tests.Checkpoints.CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns: expected two retry driver runs, actual one.
- CP-9: Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.confirmed_dead_local_identity_permits_cleanup: expected Removed, actual Refused.
- CP-10: Antiphon.Tests.Checkpoints.RerunPolicyTests.row_green_only_when_rerun_passed_and_nothing_else_failed: expected exit 1, actual 2.
- CP-10: Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_lines_and_reruns_suffix: expected reruns=1, actual empty line.
- CP-10: Antiphon.Tests.Checkpoints.RerunPolicyTests.unlisted_failure_never_reruns: expected exit 1, actual 2.
- CP-10: Antiphon.Tests.Checkpoints.RerunPolicyTests.reruns_only_known_flaky_names_once: expected exit 0, actual 2.
- CP-10: Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_invokes_once_per_class: expected exit 0, actual 2.
- CP-11: Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it: expected one build, actual zero.
- CP-11: Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_kills_running_and_skips_queued: expected skipped/timeout, actual slot-refused/slot-refused.

## Execution, provenance and evidence

Read GET /api/runner-defaults and GET /api/session-runners. Default placement was server2; desktop and server2 were eligible, temporary runner unavailable/draining. No host or platform was pinned. No production settings, routing, restart or deployment were performed.

Every test/build driver held a build slot. The only out-of-table drivers were the necessary checkpoint-tool bootstrap and client dependency installation:

- pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1258-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1258-tool/ --nologo: exit 0, slot=granted waited=0s, held about 28s, two existing nullable warnings.
- pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1258-client-dependencies -- npm --prefix client ci: exit 0, slot=granted waited=0s, held about 42s. Separated the two esbuild hard links before qualification.

Imported the closed Checkpoints table, used rowMinutes 40, serial execution, --row-timeout 40m --total-timeout 150m, no --keep-outputs, and passed each committed source SHA as --expected-source-sha. Waited every run to terminal exit; no test/build remained running at settlement. Linux checkpoint builds used UseAppHost=false. An obsolete initial manifest import was replaced by .antiphon/c1258-v2.yaml after the importer safely refused overwrite; it did not run extra tests.

Run roots under /work/worktrees/task-01f4a4f0/.antiphon/checkpoints/:

- 20261014-194303-3758: red e046c7473e25057de3be0e42aea13dac2a28dae7, exit 1, wall 10m30s. Build slot waited 60s; CP-2 test slot waited 30s; CP-1 waited 0s. All slots granted.
- 20261014-195457-e773: fixed 2ffcb09e05976a23397dfd3e245e20b69d988324, exit 5, wall 49m55s. Build waited 0s; CP-4 waited 30s; CP-3/5/6 waited 0s. All slots granted.
- 20261014-204536-2629: baseline 5665ddb1a3ba730f3b858acc109e65b5d7304d6c, exit 1, wall 5m36s. Every slot granted, waited 0s. Full raw evidence copied byte-for-byte from the temporary detached worktree before removal. Embedded original absolute paths still name /tmp/antiphon-c1258-base-01f4a4f0; find the same relative payloads in this assigned-worktree copy.

For each run, both validators were run against its report.json and actual expected source before cleanup:

- pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha <actual-source-sha>
- dotnet tools/Antiphon.Checkpoints/bin-c1258-tool/Antiphon.Checkpoints.dll validate --evidence <report.json> --expected-source-sha <actual-source-sha>

Every validator returned exit 2: CHECKPOINT SOURCE INVALID reason=cleanup_refused. This is recorded without rewriting receipts. Output cleanup subsequently removed only the literal bin-c1258-red, bin-c1258-green, bin-c1258-base and bin-c1258-tool directories created by this task. The detached base worktree was removed. No bin-c1258-* directory remains under tests/tools/src/server. Generated evidence remains gitignored; this individual Markdown report is the only committed evidence artifact.

To continue R-3, use the plan's CP-5 selection through the checkpoint tool at the then-committed HEAD with fresh source qualification, preserving the 40-minute bound. Diagnose the recorded inherited checkpoint failures and missing final TRX rather than widening a deadline or relaxing assertions. Do not rerun the already-green scoped proof without a new change or unresolved concern. Fresh evidence must retain the actual source and be validated; the current receipts cannot be called aggregate-green.

## Pending Mutation and operational handoff

All PCs remain pending for SourceLanding Mutation after ordinary Review and landing:

- PC-1: pin, pin+chain, role, any, config, rolePolicy, agent x Hard, Medium, Easy; exact model/component witnesses in the candidate.
- PC-2: independently remove effective-candidate, stored-candidate, or stored-origin gate.
- PC-3: Hard and Easy default-only fallback defect; retain Medium preservation control and HTTP witness.
- PC-4: unused invalid RolePolicy variant.

Mutation owns method-scoped binding, red/restore/green and missing-control discovery. No deliberate mutant was run here.

Restart required after landing/activation: server, including the updated client bundle. Runner: none. This task did not restart or deploy.

The brief's requested master merge conflicts with GIT-COMMON's prohibition on merging the target into a task branch. Used fetch and merge-tree without changing branch ancestry. The fetched target checked before the report commit was 8c34bd258e40a963f0e53a22d277a608ce5a9998; merge-tree exited 0. Final merge-tree, full base..HEAD evidence-diff gate, clean status, final SHA and matching ls-remote are reported in the completion message. The report-only commit is not represented as a new tested source.

## Unedited checkpoint reports


### 20261014-194303-3758

```text
--- checkpoint report ---
run: 20261014-194303-3758   manifest: /work/worktrees/task-01f4a4f0/.antiphon/checkpoints/20261014-194303-3758/manifest.resolved.yaml
commit: e046c7473e25057de3be0e42aea13dac2a28dae7  branch: feat/card-task-01f4a4f0  worktree: /work/worktrees/task-01f4a4f0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: e046c7473e25057de3be0e42aea13dac2a28dae7 state=clean buildSource=verified
CHECKPOINT CP-1 commit=e046c7473e25057de3be0e42aea13dac2a28dae7 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=e046c7473e25057de3be0e42aea13dac2a28dae7 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=e046c7473e25057de3be0e42aea13dac2a28dae7 build=ok filter=/*/*/(ComplexityEffectiveComposeTests*)|(ComplexityEffectiveHttpTests*)|(ComplexityRoutingComposeTests*)/* executed=33 passed=29 failed=4 skipped=0 trx=/work/worktrees/task-01f4a4f0/.antiphon/checkpoints/20261014-194303-3758/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=e046c7473e25057de3be0e42aea13dac2a28dae7 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=90s build=299.7657854s startup=7.7900081s testsWall=51.7714529s teardown=3.017659s hostWall=62.5793476s
FAILED Antiphon.Tests.Application.ComplexityEffectiveComposeTests.A_preferred_pin_on_an_empty_cell_appends_policy_only_for_the_default_tier (CP-2) ShouldAssertException: result.Candidates.Select(c => (c.AgentKind, c.ModelLevel, c.Alias))     should be [(Codex, Frontier, gpt-6-astra)]     but was [(Codex, Frontier, gpt-6-astra), (ClaudeCode, Frontier, fable)]     difference [(Codex, Frontier, gpt-6-astra), *(ClaudeCode, Frontier, fable)*] -> rows/CP-2/failures.md
FAILED Antiphon.Tests.Application.ComplexityEffectiveComposeTests.A_non_default_preferred_pin_does_not_report_an_unused_policy_as_unlaunchable (CP-2) ShouldAssertException: result.WithheldReason     should be null but was "Plan policy names Raw/Frontier, which delegation cannot launch" -> rows/CP-2/failures.md
FAILED Antiphon.Tests.Application.ComplexityEffectiveComposeTests.A_preferred_pin_on_an_empty_cell_appends_policy_only_for_the_default_tier (CP-2) ShouldAssertException: result.Candidates.Select(c => (c.AgentKind, c.ModelLevel, c.Alias))     should be [(Codex, Frontier, gpt-6-astra)]     but was [(Codex, Frontier, gpt-6-astra), (ClaudeCode, Frontier, fable)]     difference [(Codex, Frontier, gpt-6-astra), *(ClaudeCode, Frontier, fable)*] -> rows/CP-2/failures.md
FAILED Antiphon.Tests.Application.ComplexityEffectiveHttpTests.A_preferred_pin_on_empty_tiers_exposes_policy_only_on_the_default_tier (CP-2) ShouldAssertException: candidates     should be [(Codex, Frontier, gpt-6-astra)]     but was [(Codex, Frontier, gpt-6-astra), (ClaudeCode, Frontier, fable)]     difference [(Codex, Frontier, gpt-6-astra), *(ClaudeCode, Frontier, fable)*] -> rows/CP-2/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 10m30s  sequential-equivalent: 3m58s  builds: 2  max-concurrent-builds: 1  rows: 0 green 2 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1258-red/
evidence: /work/worktrees/task-01f4a4f0/.antiphon/checkpoints/20261014-194303-3758/report.md
verdict: RED exit=1
```

### 20261014-195457-e773

```text
--- checkpoint report ---
run: 20261014-195457-e773   manifest: /work/worktrees/task-01f4a4f0/.antiphon/checkpoints/20261014-195457-e773/manifest.resolved.yaml
commit: 2ffcb09e05976a23397dfd3e245e20b69d988324  branch: feat/card-task-01f4a4f0  worktree: /work/worktrees/task-01f4a4f0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 2ffcb09e05976a23397dfd3e245e20b69d988324 state=clean buildSource=verified
CHECKPOINT CP-3 commit=2ffcb09e05976a23397dfd3e245e20b69d988324 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=2ffcb09e05976a23397dfd3e245e20b69d988324 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-6 commit=2ffcb09e05976a23397dfd3e245e20b69d988324 build=n/a filter=npm --prefix client run build executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=2ffcb09e05976a23397dfd3e245e20b69d988324 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=2ffcb09e05976a23397dfd3e245e20b69d988324 build=ok filter=/*/*/(ComplexityEffectiveComposeTests*)|(ComplexityEffectiveHttpTests*)|(ComplexityRoutingComposeTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-01f4a4f0/.antiphon/checkpoints/20261014-195457-e773/rows/CP-4/run.trx slot=granted waited=30s dirty=0 source=2ffcb09e05976a23397dfd3e245e20b69d988324 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=30s build=360.3887587s startup=4.2040066s testsWall=67.4897073s teardown=3.1042138s hostWall=74.7983796s
SLOW CLASS Antiphon.Tests.Application.ComplexityEffectiveHttpTests 67s tests=4 (CP-4)
CHECKPOINT CP-5 commit=2ffcb09e05976a23397dfd3e245e20b69d988324 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=40m slot=granted waited=0s dirty=0 source=2ffcb09e05976a23397dfd3e245e20b69d988324 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 49m55s  sequential-equivalent: 43m30s  builds: 2  max-concurrent-builds: 1  rows: 3 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1258-green/
evidence: /work/worktrees/task-01f4a4f0/.antiphon/checkpoints/20261014-195457-e773/report.md
verdict: RED exit=5
```

### 20261014-204536-2629

```text
--- checkpoint report ---
run: 20261014-204536-2629   manifest: /tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/manifest.resolved.yaml
commit: 5665ddb1a3ba730f3b858acc109e65b5d7304d6c  branch: HEAD  worktree: /tmp/antiphon-c1258-base-01f4a4f0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 5665ddb1a3ba730f3b858acc109e65b5d7304d6c state=clean buildSource=verified
CHECKPOINT CP-7 commit=5665ddb1a3ba730f3b858acc109e65b5d7304d6c build=ok filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata executed=1 passed=0 failed=1 skipped=0 trx=/tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=5665ddb1a3ba730f3b858acc109e65b5d7304d6c sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=287.0872225s startup=3.235896s testsWall=4.0096447s teardown=0.4945217s hostWall=7.7404809s
FAILED Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata (CP-7) ShouldAssertException: lines     should be empty but had 1     item and was ["lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests"]  Additional Info:     lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests -> rows/CP-7/failures.md
CHECKPOINT CP-8 commit=5665ddb1a3ba730f3b858acc109e65b5d7304d6c build=reused filter=/*/*/CheckpointImportTests/environment_reaches_only_its_row_and_its_reruns executed=1 passed=0 failed=1 skipped=0 trx=/tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=5665ddb1a3ba730f3b858acc109e65b5d7304d6c sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=7.1109779s testsWall=0.3406933s teardown=0.8204376s hostWall=8.2721087s
FAILED Antiphon.Tests.Checkpoints.CheckpointImportTests.environment_reaches_only_its_row_and_its_reruns (CP-8) ShouldAssertException: retryDriver.Count(CheckpointFixtures.IsRun)     should be 2     but was 1 -> rows/CP-8/failures.md
CHECKPOINT CP-9 commit=5665ddb1a3ba730f3b858acc109e65b5d7304d6c build=reused filter=/*/*/CheckpointProcessIdentityTests/confirmed_dead_local_identity_permits_cleanup executed=1 passed=0 failed=1 skipped=0 trx=/tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=5665ddb1a3ba730f3b858acc109e65b5d7304d6c sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=7.1896302s testsWall=0.2810496s teardown=0.9049704s hostWall=8.3756506s
FAILED Antiphon.Tests.Checkpoints.CheckpointProcessIdentityTests.confirmed_dead_local_identity_permits_cleanup (CP-9) ShouldAssertException: receipt.Outcome     should be "Removed"     but was "Refused"     difference Difference     |            |    |    |                             |           \|/  \|/  \|/             Index          | 0    1    2    3    4    5    6     Expected Value | R    e    m    o    v    e    d     Actual Value   | R    e    f    u    s    e    d     Expected Code  | 82   101  109  111  118  101  100   Actual Code    | 82   101  102  117  115  101  100 -> rows/CP-9/failures.md
CHECKPOINT CP-10 commit=5665ddb1a3ba730f3b858acc109e65b5d7304d6c build=reused filter=/*/*/RerunPolicyTests/(rerun_lines_and_reruns_suffix*)|(row_green_only_when_rerun_passed_and_nothing_else_failed*)|(rerun_invokes_once_per_class*)|(unlisted_failure_never_reruns*)|(reruns_only_known_flaky_names_once*) executed=5 passed=0 failed=5 skipped=0 trx=/tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=5665ddb1a3ba730f3b858acc109e65b5d7304d6c sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=2.8565794s testsWall=0.3600634s teardown=0.5480571s hostWall=3.7647s
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.row_green_only_when_rerun_passed_and_nothing_else_failed (CP-10) ShouldAssertException: mixed.ExitCode     should be 1     but was 2 -> rows/CP-10/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_lines_and_reruns_suffix (CP-10) ShouldAssertException: result.Line     should contain (case insensitive comparison) "reruns=1"     but was actually "" -> rows/CP-10/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.unlisted_failure_never_reruns (CP-10) ShouldAssertException: result.ExitCode     should be 1     but was 2 -> rows/CP-10/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.reruns_only_known_flaky_names_once (CP-10) ShouldAssertException: result.ExitCode     should be 0     but was 2 -> rows/CP-10/failures.md
FAILED Antiphon.Tests.Checkpoints.RerunPolicyTests.rerun_invokes_once_per_class (CP-10) ShouldAssertException: result.ExitCode     should be 0     but was 2 -> rows/CP-10/failures.md
CHECKPOINT CP-11 commit=5665ddb1a3ba730f3b858acc109e65b5d7304d6c build=reused filter=/*/*/TimeoutTests/(total_deadline_skips_a_queued_build_without_starting_it*)|(total_deadline_kills_running_and_skips_queued*) executed=2 passed=0 failed=2 skipped=0 trx=/tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=5665ddb1a3ba730f3b858acc109e65b5d7304d6c sourceState=clean buildSource=verified
PHASES CP-11 slotWait=0s build=0s startup=2.9724592s testsWall=0.7178865s teardown=0.7918334s hostWall=4.4821791s
FAILED Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it (CP-11) ShouldAssertException: driver.Count(CheckpointFixtures.IsBuild)     should be 1     but was 0 -> rows/CP-11/failures.md
FAILED Antiphon.Tests.Checkpoints.TimeoutTests.total_deadline_kills_running_and_skips_queued (CP-11) ShouldAssertException: result.Rows.Select(row => row.State).OrderBy(state => state)     should be ["skipped", "timeout"]     but was (case sensitive comparison) ["slot-refused", "slot-refused"]     difference [*"slot-refused"*, *"slot-refused"*] -> rows/CP-11/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 5m36s  sequential-equivalent: 0m36s  builds: 3  max-concurrent-builds: 1  rows: 0 green 5 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1258-base/
evidence: /tmp/antiphon-c1258-base-01f4a4f0/.antiphon/checkpoints/20261014-204536-2629/report.md
verdict: RED exit=1
```
