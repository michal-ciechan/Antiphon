# CARD-1906 compiler-policy stack environment plumbing

Original Code task and landing owner: `28eecdf5-2364-4811-934c-f9fbdc10ef55`.
Branch: `feat/card-task-28eecdf5`.
Worktree: `/work/worktrees/task-28eecdf5`.
Task base/current master at start: `d100f8fa2c679a5c74b6b235087308776a8f5d50`.
Plan/checkpoint manifest: `docs/superpowers/plans/2026-11-15-card-1906-compiler-policy-plumbing.md`.

Implementation preserves **default OFF**. The deployment reader is actually in
`scripts/c590-remote.sh`, invoked by `scripts/deploy-server2.ps1`; the coordinator
needs no change. Both Compose wrappers read their own project's saved file;
both deployment phase functions validate before effects and preserve the value
on rewrite. The temp overlay inherits the base service environment mapping.
Saved grammar is exact 0/1, LF, at most once, with a distinct sanitized refusal.
The stack file is never sourced. No BuildDriverEnvironment behavior is changed.

The two named defaults are Compose anchor `RunnerLinuxCompilerPolicyDefault` and
shell local readonly `RUNNER_LINUX_COMPILER_POLICY_DEFAULT`, both 0. A future
operator-approved default-on change edits those two nodes and their pinned absent
input expectations. The example's explicit 0 remains an opt-out after such a flip.

## Activation and rollback

Activation was explicitly authorized by the operator on 2026-11-15; this Code task
only makes the setting deliverable. No additional approval was requested.
Place the exact line:

```text
ANTIPHON_LINUX_COMPILER_POLICY=1
```

in `/home/mc/antiphon-server2/secrets/stack.env` before the next `redeploy-old`.
The caller's named rollout recreates the runner. Verify the non-secret setting:

```sh
docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' <runner-container-id> | grep '^ANTIPHON_LINUX_COMPILER_POLICY='
```

Expected output is exactly `ANTIPHON_LINUX_COMPILER_POLICY=1`. Then check new
build-driver diagnostics for `LINUX COMPILER policy=c1885-v1`, and record source/
runtime SHAs. Saved temp intent is independently in `secrets/stack.temp.env` and
applies through `deploy-temp`. Existing task processes and compilers are unchanged;
compilers drain naturally and must never be shut down. Rollback is exact saved 0
and recreation through the same named phase, affecting future builds.

The operator's 24-hour trial measures (1) host CPU/run queue/compiler minute-cores,
(2) matched completed build+test rows per hour and p50/p95 wall with sample counts
and wait/execution separated, (3) timeout and rerun counts. Preserve workload and
concurrency context plus original failures. This task performs no rollout, host
secret edit, compiler shutdown, Docker lifecycle operation or service restart.
Restart target for eventual activation: runner recreation by the named phase;
current Code restart: none.

## Environment custody

`docker/session-runner-grok/dind-entrypoint.sh` launches through `setpriv` without
clearing this variable. `PtyHostLauncher.SpawnAsync` uses an inherited
`ProcessStartInfo` environment; `RunnerSession.StartAsync` passes `request.Env` as
launch overrides. `HostSession` constructs the real `PtyAgentRunner`, which passes
these overrides to Porta.Pty on Linux. There is no runner launch environment
allowlist filtering this key. The Windows modern backend explicitly merges the
parent environment and overrides; this task changes no Windows policy or backend.
The narrow environment clearing in Codex version/auth probes is not session launch.
`scripts/build-slot.ps1` creates an inherited ProcessStartInfo, applies the shared
build policy and launches the requested command; its only module-path removal is
for direct Windows PowerShell 5.1 children. No allowlist addition is needed.

`RunnerLinuxCompilerPolicyTests.Rendered_policy_inherits_through_session_and_build_wrapper`
uses a real rendered Compose value, the production PtyAgentRunner, an override map
that explicitly lacks the policy key, and the real build wrapper with its existing
fixture slot broker. The observed final child value must equal the saved input for
both 0 and 1. It launches no build or live runner and restores parent test environment.

## Red-first evidence

Committed test source: `9f0b62cea551501728c6b2df365e0151e6a73a15`.
Checkpoint run: `.antiphon/c1906/red/20261115-114322-32ff/`.
Fresh TRX: 33 executed, 0 passed, 33 failed, 0 skipped; all 32 refusal shapes
failed their four status assertions (91 instead of 2), and the Compose matrix
failed on missing key (`undefined` instead of `0`). No build/fixture error is
credited as an intended red. Build was clean and verified, 0 compiler errors.
Build execution 423.978s, row execution 18.877s, full run wall 468.361s.
Admission waits were 0s. Loadavg observations during that build ranged from
`5.39 8.66 8.00` to `27.81 13.65 9.76`; these are contextual samples, not a p95.
An initial expected-SHA typo refused before building. Earlier observer calls used
a relative --run path and returned 75 on a nonexistent default-root path; the
absolute-path wait returned the real terminal exit 1. None launched another build.

Final qualification and source-bound checkpoint lines will be appended after
execution. Generated TRX, JSON receipts and logs remain ignored.

## Introduced admission defect and repair

The first full core run at `63f94c280026ecfa2e0358f2083f50a9c2fc69c7` is RED, not green: CP-3 failed `Markers_do_not_expand_policy_exclusions` and `Policy_rows_require_pinned_attribute`; CP-7 failed `LinuxOnlyWindowsExclusionPolicy`. All diagnose the new native test class having no Windows exclusion row. Other core rows passed. This is introduced by this task, not an inherited failure. The guard protected its invariant and was not weakened.

Repair commit `d60e81d0c7d902bfa7c6db2f8654ba7797db424e` adds only that required exclusion row and repins policyHash plus the one protected-input digest line. This necessary minimal hunk touches two files the brief identified as shared with Merge 241; no other part of either file changed.

Old master pins were independently verified before the change. New policyHash: `16917233483bf9f13534d8d5f86a1bdbdfabe4a4981153127287537c25bb5179`. New PinnedInputDigest: `0a6eaab72768e0e3f65c1a0cf15ef47c4471f913397389ca6900b9538b44fe00`. LegacyExclusionDigest remains `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`; the new prefixed Linux-only row is outside its frozen legacy payload. The already-built native index driver was used again after the policy change; both regenerations printed unchanged.

The stopped preliminary core run imported only five rows because of a blank table line; its slot fixture passed but no test build launched. It was stopped through the supported tool, joined, and preserved with terminal exit 130. The nine-row table and compact MODEL JSON transport were repaired before the actual full core run. No assertion, production timeout or retry rule was loosened.

### Unedited historical receipt: red/20261115-114322-32ff

Report SHA-256: `ce53c1ced41e80efb65187356b657a32dbaef6352b8d6d56804af44b69d616a8`, verified against its schema-2 JSON `markdownSha256` (hex case normalized only). Source/build qualification is retained exactly as printed, including unknown/canceled rows.

```text
commit: 9f0b62cea551501728c6b2df365e0151e6a73a15  branch: feat/card-task-28eecdf5  worktree: /work/worktrees/task-28eecdf5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 9f0b62cea551501728c6b2df365e0151e6a73a15 state=clean buildSource=verified
CHECKPOINT CP-1 commit=9f0b62cea551501728c6b2df365e0151e6a73a15 build=ok filter=/*/*/RunnerLinuxCompilerPolicyTests/* executed=33 passed=0 failed=33 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/red/20261115-114322-32ff/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9f0b62cea551501728c6b2df365e0151e6a73a15 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=18.877s wall=18.916s
BUDGET wall=468.361s execution=468.360s admissionOnly=0.001s
verdict: RED exit=1
```

### Unedited historical receipt: core-final/20261115-120013-478e

Report SHA-256: `c378e3eeae86df9e8872dfe22a3605f488d3432c716383ece8b8791a57d7b457`, verified against its schema-2 JSON `markdownSha256` (hex case normalized only). Source/build qualification is retained exactly as printed, including unknown/canceled rows.

```text
commit: 7669a964d90e3964875043fa4034f2429e8b983f  branch: feat/card-task-28eecdf5  worktree: /work/worktrees/task-28eecdf5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 7669a964d90e3964875043fa4034f2429e8b983f state=clean buildSource=unknown
CHECKPOINT CP-1 commit=7669a964d90e3964875043fa4034f2429e8b983f build=n/a filter=/*/*/RunnerLinuxCompilerPolicyTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=7669a964d90e3964875043fa4034f2429e8b983f sourceState=clean buildSource=unknown
CHECKPOINT CP-2 commit=7669a964d90e3964875043fa4034f2429e8b983f build=n/a filter=/*/*/(DockerStackContractTests*)|(DeployServer2ApiDefaultTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=7669a964d90e3964875043fa4034f2429e8b983f sourceState=clean buildSource=unknown
CHECKPOINT CP-3 commit=7669a964d90e3964875043fa4034f2429e8b983f build=n/a filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ImpactedClassesSelectorTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=7669a964d90e3964875043fa4034f2429e8b983f sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=7669a964d90e3964875043fa4034f2429e8b983f build=n/a filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_ordering_contract_is_pinned*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Deploy_prepares_and_verifies_before_acceptance*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=7669a964d90e3964875043fa4034f2429e8b983f sourceState=clean buildSource=unknown
CHECKPOINT CP-5 commit=7669a964d90e3964875043fa4034f2429e8b983f build=n/a filter=node scripts/fixtures/c346-workspace-slot-compose.mjs executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=7669a964d90e3964875043fa4034f2429e8b983f sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-5 waited=0s executed=10.716s wall=10.864s
BUDGET wall=11.434s execution=11.430s admissionOnly=0.004s
verdict: RED exit=130 reason=supported-stop
```

### Unedited historical receipt: core-final/20261115-120128-55e7

Report SHA-256: `f7d25a3e791d3e6444cf102fd42cb8be9df8d58b5f9d624e916a95db361581eb`, verified against its schema-2 JSON `markdownSha256` (hex case normalized only). Source/build qualification is retained exactly as printed, including unknown/canceled rows.

```text
commit: 63f94c280026ecfa2e0358f2083f50a9c2fc69c7  branch: feat/card-task-28eecdf5  worktree: /work/worktrees/task-28eecdf5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 63f94c280026ecfa2e0358f2083f50a9c2fc69c7 state=clean buildSource=verified
CHECKPOINT CP-5 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=n/a filter=node scripts/fixtures/c346-workspace-slot-compose.mjs executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-5 waited=0s executed=8.719s wall=8.813s
CHECKPOINT CP-1 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=ok filter=/*/*/RunnerLinuxCompilerPolicyTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=28.921s wall=28.965s
CHECKPOINT CP-2 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=reused filter=/*/*/(DockerStackContractTests*)|(DeployServer2ApiDefaultTests*)/* executed=120 passed=120 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=17.471s wall=17.522s
CHECKPOINT CP-3 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ImpactedClassesSelectorTests*)/* executed=164 passed=162 failed=2 skipped=1 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=446.191s wall=446.241s
CHECKPOINT CP-4 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_ordering_contract_is_pinned*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Deploy_prepares_and_verifies_before_acceptance*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=70.670s wall=70.719s
CHECKPOINT CP-6 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=14.942s wall=14.983s
CHECKPOINT CP-7 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=reused filter=/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)|(LinuxOnlyWindowsExclusionPolicy*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=56.099s wall=56.152s
CHECKPOINT CP-8 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=3.991s wall=4.044s
CHECKPOINT CP-9 commit=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-final/20261115-120128-55e7/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=63f94c280026ecfa2e0358f2083f50a9c2fc69c7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=3.924s wall=3.983s
BUDGET wall=1159.792s execution=1159.777s admissionOnly=0.014s
verdict: RED exit=1
```

## Final Code settlement: implementation and closed core complete; full impact gate incomplete

Actual tested source for the complete passing core run is `e2d3d6643c52d9a1873b462fd4a058434656c945`. Every checkpoint passed that committed HEAD as expected SHA. A subsequent report publication changes only this Markdown report; no receipt is relabeled to that publication. Ten rows are GREEN, exit 0; 394 executed/394 passed/0 failed, plus one existing native-Windows skip. No remaining introduced product or core-test failure is claimed. **R-2 remains incomplete for the 38 expanded derived-impact classes listed below; Final Review is not yet ready.**

| Row | Passed / failed / skipped | Execution seconds | Scope |
|---|---:|---:|---|
| CP-1 | 35 / 0 / 0 | 30.974 | new policy (32 refusals, model/persistence matrix, two real native inheritance cases) |
| CP-2 | 120 / 0 / 0 | 16.722 | DockerStackContractTests 117; DeployServer2ApiDefaultTests 3 |
| CP-3 | 164 / 0 / 1 | 461.915 | every named guard class plus ImpactedClassesSelectorTests |
| CP-4 | 7 / 0 / 0 | 64.867 | all five explicit impacted remote methods plus both adjacent harness callers |
| CP-5 | command exit 0 | 9.427 | 120 refusal boundaries, 16 workspace-slot matrices, missing file and baseline equivalence; command exit 0 |
| CP-6 | 57 / 0 / 0 | 15.877 | main ProcessSpawnLimitTests |
| CP-7 | 2 / 0 / 0 | 61.390 | C1686_LinuxOnlyCensus and LinuxOnlyWindowsExclusionPolicy |
| CP-8 | 2 / 0 / 0 | 6.872 | session-runner ProcessSpawnLimitTests |
| CP-9 | 2 / 0 / 0 | 3.417 | agents-PTY ProcessSpawnLimitTests |
| CP-10 | 5 / 0 / 0 | 62.766 | LiveLaneRunnerTests 4; NightlyCoreTests 1 |

Every intended class and method/parameterized case in these rows was inspected in its fresh TRX, with nonzero class counts; command assertions were inspected in fresh stdout. The only skip is `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`; it is not credited as a Windows execution. Layering stdout is exactly `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

The official validator returned `CHECKPOINT SOURCE VALID source=e2d3d6643c52d9a1873b462fd4a058434656c945 rows=9` for CP-1, CP-2 and CP-4 through CP-10. CP-3 is omitted from that strict zero-skip certificate because of the named native-Windows skip; its ordinary GREEN outcome, actual counts and verified clean build provenance remain recorded, never converted into a zero-skip certificate. The full Final scope remains incomplete independently.

### Every ordinary V/R

| ID | Actual outcome |
|---|---|
| V-1 | PASS: 32 invalid shapes x four production boundaries reject with exact sanitized reason/exit before effects; initial red retained. |
| V-2 | PASS: saved absence defaults 0; missing file and empty ambient Compose input distinguished; project intent remains independent. |
| V-3 | PASS: 16 real Compose/persistence matrices, direct absent/empty fallback assertions, baseline-equivalence assertion and temp inheritance. |
| V-4 | PASS: real rendered 0 and 1 reach the production native PtyAgentRunner and real build-wrapper child without the key in the session override map; no launch allowlist exists. |
| R-1 | PASS: existing slot assertions, deployment contracts and all seven adjacent remote methods. |
| R-2 | commissioned to Debug c2489e73, manifest pending. PARTIAL/INCOMPLETE: guard union, explicit impacted rows and new nightly-live-lane row all run; 38 derived whole-class selections NOT RUN by this Code task. Native Windows-only guard remains skipped, not passed. |
| R-3 | PASS for the tested source: full-base evidence history, OS inventory lint and conflict-free merge-tree; all three policy pins independently checked as described above. Final publication receives an additional full-range history/merge-tree check. |

Production activation/inspect and the measured 24-hour trial remain future rollout work under the already granted operator authority. No approval was requested for that authority, no live activation was performed, and no trial metrics are invented. Restart in this task: none. Future activation target: runner recreation through the named phase.

### Performance, source and execution costs

Passing run wall 1364.271 seconds (22m44s); charged execution 1364.135 seconds; admission-only 0.015 seconds. All rows/builds use one lease at a time, `slot=granted waited=0s`, six-node grants and unchanged nine-minute row/build limits. Three isolated builds were serial: bin-c1906-d 276.146s; bin-c1906-runner-b 112.960s; bin-c1906-pty-b 81.852s. Native direct bootstraps used native default outputs and UseAppHost=false. No row reached ten minutes and no individual completed checkpoint invocation exceeded thirty minutes. Main guard row measured 446.191s before repair and 461.915s after, N=2 under differing load, not a calibrated p95. Row limits were not widened.

Loadavg context samples across the repaired run included `2.12 3.73 5.50` at 12:26:05Z, `20.80 11.55 8.10` at 12:29:53Z, `5.25 5.92 6.55` at 12:38:01Z and `20.19 10.08 7.59` at 12:45:18Z. This is contextual sampling, not a causal or throughput comparison. TRX contains per-test durations; checkpoint PHASES contains startup/body/teardown spans. Inner fixture setup, individual uninstrumented CLI calls and remaining preflight/settlement components are unknown rather than zero; raw logs and phase files remain in the ignored run roots.

The red-first run, stopped setup run and first failed core receipt remain historical outcomes with their costs above. The complete core was repeated once after the admission repair because the prior parent receipt was RED. No green core row is owed another repetition. Unlisted driver reasons: checkpoint-tool bootstrap was required to run the checkpoint tool; the standalone derived-index driver bootstrap was explicitly required. Index generation ran separately leased with --no-build: changed/unchanged first, unchanged/unchanged after admission repair. All bootstrap/regeneration waits were 0s. Static hashing, Git inspection, receipt validation, selector enumeration and OS lint build nothing. No full Unit lane, assembly sweep, migration row, live host or deliberate mutant was run.

### Every tested class count

| Row | Class | Fresh TRX cases |
|---|---|---:|
| CP-1 | Antiphon.Tests.Scripts.RunnerLinuxCompilerPolicyTests | 35 |
| CP-2 | Antiphon.Tests.Infrastructure.DockerStackContractTests | 117 |
| CP-2 | Antiphon.Tests.Scripts.DeployServer2ApiDefaultTests | 3 |
| CP-3 | Antiphon.Tests.Application.MechanicalPcContractTests | 11 |
| CP-3 | Antiphon.Tests.Application.TaskPlatformGuidanceTests | 5 |
| CP-3 | Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | 11 |
| CP-3 | Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests | 7 |
| CP-3 | Antiphon.Tests.TestHelpers.SlowTestTripwireTests | 2 |
| CP-3 | Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | 3 |
| CP-3 | Antiphon.Tests.Scripts.DerivedImpactIndexTests | 27 |
| CP-3 | Antiphon.TestSupport.TestClassificationGuardTests | 1 |
| CP-3 | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | 5 |
| CP-3 | Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests | 1 |
| CP-3 | Antiphon.Tests.Scripts.ImpactedClassesSelectorTests | 50 |
| CP-3 | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | 42 |
| CP-4 | Antiphon.Tests.Scripts.RemoteScriptContractTests | 7 |
| CP-6 | Antiphon.Tests.ProcessSpawnLimitTests | 57 |
| CP-7 | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | 2 |
| CP-10 | Antiphon.Tests.Scripts.LiveLaneRunnerTests | 4 |
| CP-10 | Antiphon.Tests.Scripts.NightlyCoreTests | 1 |
| CP-8 | Antiphon.SessionRunner.Tests.ProcessSpawnLimitTests | 2 |
| CP-9 | Antiphon.Agents.Pty.Tests.ProcessSpawnLimitTests | 2 |

### Pending expanded Final selection and cost stop gate

The actual base..tested-source selector matched `linux-only-policy`, `nightly-live-lane`, `remote-scripts`, `spawn-limit-rosters` and `test-lane-category`. Its full file-read-derived filters also include the following 38 whole classes, all **NOT RUN by this Code task**, not passed or silently dropped. The two explicit method filters were completed. R-2 is **commissioned to Debug c2489e73, manifest pending** at `docs/investigations/2026-11-15-card-1906-r2-bounded-selection.md`.

The prior Debug report `docs/investigations/2026-11-13-selector-fixture-retirement-checkpoints.md` (task 49ae044d) records a stopped over-30-minute selection, 252 complete-roster child invocations in the aggregate rollout case, 765 seconds for 22 container-host cases, 224 seconds for 16 script cases, and a 248-second private-origin case. Its superseding 25/24/15-minute rows do not fit this brief's under-ten-minute rows. Launching that unchanged broad sweep would violate the mandatory performance gate. An asynchronous request for the bounded Debug inventory was made while independent core work continued; no inventory was supplied before the initial report. The caller subsequently commissioned Debug c2489e73, based on published tip `1ca3d5a961b78089fe24fdc09a3597c886d4f528`, to inventory and run these 38 classes in partitions each under ten minutes. Its manifest and clean results remain pending. This is missing required execution, not an advisory waiver or a product regression.

| Pending ID | Project | Class | Outcome |
|---|---|---|---|
| R-2/D01 | tests/Antiphon.SessionRunner.Tests | C1818GcOwnerRobustnessTests | NOT RUN |
| R-2/D02 | tests/Antiphon.SessionRunner.Tests | CustodyNamespaceBootstrapTests | NOT RUN |
| R-2/D03 | tests/Antiphon.SessionRunner.Tests | HostStatsProbeParseTests | NOT RUN |
| R-2/D04 | tests/Antiphon.Tests | C0824BranchDeletionRecycleAuditTests | NOT RUN |
| R-2/D05 | tests/Antiphon.Tests | C1008HostFixtureSetupInsertionTests | NOT RUN |
| R-2/D06 | tests/Antiphon.Tests | C1008PlatformContractTests | NOT RUN |
| R-2/D07 | tests/Antiphon.Tests | C1364BoundLandWaitProofTests | NOT RUN |
| R-2/D08 | tests/Antiphon.Tests | ClaudeTokenRefreshOptInTests | NOT RUN |
| R-2/D09 | tests/Antiphon.Tests | CodexRootExecContractTests | NOT RUN |
| R-2/D10 | tests/Antiphon.Tests | CodexRunnerImageContractTests | NOT RUN |
| R-2/D11 | tests/Antiphon.Tests | DindRunnerContractTests | NOT RUN |
| R-2/D12 | tests/Antiphon.Tests | DockerStackDocumentationTests | NOT RUN |
| R-2/D13 | tests/Antiphon.Tests | GrokRunnerImageContractTests | NOT RUN |
| R-2/D14 | tests/Antiphon.Tests | HangfireLeaseDocumentationTests | NOT RUN |
| R-2/D15 | tests/Antiphon.Tests | HangfirePostgreSqlStorageTests | NOT RUN |
| R-2/D16 | tests/Antiphon.Tests | HangfireStorageSelectionTests | NOT RUN |
| R-2/D17 | tests/Antiphon.Tests | HostJqPrerequisiteScriptTests | NOT RUN |
| R-2/D18 | tests/Antiphon.Tests | JqRunnerImageContractTests | NOT RUN |
| R-2/D19 | tests/Antiphon.Tests | RcBridgeProbePlatformTests | NOT RUN |
| R-2/D20 | tests/Antiphon.Tests | ReleaseImpactEnvelopeTests | NOT RUN |
| R-2/D21 | tests/Antiphon.Tests | RetiredTempContainerHostTests | NOT RUN |
| R-2/D22 | tests/Antiphon.Tests | RetiredTempContainerScriptTests | NOT RUN |
| R-2/D23 | tests/Antiphon.Tests | RollingProductionMountTests | NOT RUN |
| R-2/D24 | tests/Antiphon.Tests | RollingVolumeRecycleCensusChurnTests | NOT RUN |
| R-2/D25 | tests/Antiphon.Tests | RollingVolumeRecycleScriptTests | NOT RUN |
| R-2/D26 | tests/Antiphon.Tests | RunnerPushCredentialDocsTests | NOT RUN |
| R-2/D27 | tests/Antiphon.Tests | SeedSmokeTargetingPackContractTests | NOT RUN |
| R-2/D28 | tests/Antiphon.Tests | Server2ProductionServerContractTests | NOT RUN |
| R-2/D29 | tests/Antiphon.Tests | Server2SidecarStandingAgentsTests | NOT RUN |
| R-2/D30 | tests/Antiphon.Tests | Server2StagingServerContractTests | NOT RUN |
| R-2/D31 | tests/Antiphon.Tests | ServerImagePushCredentialTests | NOT RUN |
| R-2/D32 | tests/Antiphon.Tests | TimeoutAuditTests | NOT RUN |
| R-2/D33 | tests/Antiphon.Tests | VerifyDindRunnerScriptTests | NOT RUN |
| R-2/D34 | tests/Antiphon.Tests | VolumeRetirementApplyGateTests | NOT RUN |
| R-2/D35 | tests/Antiphon.Tests | VolumeRetirementContentTests | NOT RUN |
| R-2/D36 | tests/Antiphon.Tests | VolumeRetirementReceiptTests | NOT RUN |
| R-2/D37 | tests/Antiphon.Tests | VolumeRetirementWrapperTests | NOT RUN |
| R-2/D38 | tests/Antiphon.Tests | ZombieCensusPlatformTests | NOT RUN |

Regenerate the full selector with `pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range d100f8fa2c679a5c74b6b235087308776a8f5d50..HEAD -Json`; do not run its aggregate filter until Debug binds bounded rows/cases. No item above is marked passed. The caller instructed this Code task to finish with current results and `next: code`, naming Debug c2489e73. Final Review follows only after that Debug reports clean and publishes its manifest; never land from this report.

### OS inventory and controls

`.antiphon/c1906/os-witnesses.json` inventories ten changed non-document files and all six unchanged lexical OS candidates at base/head. `scripts/check-os-witnesses.ps1` exited 0 at the tested source: syntax/inventory complete, not execution certification. No production OS-selected branch changed. Existing platform guards in the remote test file were inspected; changed helpers and the actual launch path are traced above, with real Linux inheritance proof in CP-1. No Windows startup helper changed and no new Windows production witness is claimed. GET /api/runner-defaults and GET /api/session-runners were read; portable work was not repinned to a fleet location.

Every PC-1 refusal variant, PC-2/standing, PC-3/temp, both PC-4 phase variants, all three PC-5 default variants, all four PC-6 session/wrapper value variants and PC-7/native-admission remain pending in `docs/superpowers/mutations/2026-11-15-card-1906-compiler-policy-plumbing.md`. They are unbound candidates; Mutation owns real red/restore/green cycles, equivalent-mutant/missing-control disposition and zero-PC plans. Operator waiver did not fabricate any control result.

### Unedited passing receipt and verified provenance

Report: `.antiphon/c1906/core-repaired/20261115-122540-ea6a/report.md`; SHA-256 `eae7744bb8b70b3c9ae1dbda862e5426355c3d2f7d9dc89b2b1e5990114e0595` matches its schema-2 JSON binding (hex case normalized only). Report/source snapshots are clean at the actual tested SHA; every build source is verified. The official nine-row source validation is recorded above.

```text
commit: e2d3d6643c52d9a1873b462fd4a058434656c945  branch: feat/card-task-28eecdf5  worktree: /work/worktrees/task-28eecdf5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: e2d3d6643c52d9a1873b462fd4a058434656c945 state=clean buildSource=verified
CHECKPOINT CP-5 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=n/a filter=node scripts/fixtures/c346-workspace-slot-compose.mjs executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-5 waited=0s executed=9.427s wall=9.515s
CHECKPOINT CP-1 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=ok filter=/*/*/RunnerLinuxCompilerPolicyTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=30.974s wall=31.022s
CHECKPOINT CP-2 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=reused filter=/*/*/(DockerStackContractTests*)|(DeployServer2ApiDefaultTests*)/* executed=120 passed=120 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=16.722s wall=16.770s
CHECKPOINT CP-3 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ImpactedClassesSelectorTests*)/* executed=164 passed=164 failed=0 skipped=1 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=461.915s wall=461.964s
CHECKPOINT CP-4 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_ordering_contract_is_pinned*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Deploy_prepares_and_verifies_before_acceptance*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=64.867s wall=64.915s
CHECKPOINT CP-6 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=15.877s wall=15.912s
CHECKPOINT CP-7 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=reused filter=/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)|(LinuxOnlyWindowsExclusionPolicy*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=61.390s wall=61.447s
CHECKPOINT CP-10 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=reused filter=/*/*/(LiveLaneRunnerTests*)|(NightlyCoreTests*)/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=62.766s wall=62.818s
CHECKPOINT CP-8 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=6.872s wall=6.963s
CHECKPOINT CP-9 commit=e2d3d6643c52d9a1873b462fd4a058434656c945 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-28eecdf5/.antiphon/c1906/core-repaired/20261115-122540-ea6a/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=e2d3d6643c52d9a1873b462fd4a058434656c945 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=3.417s wall=3.477s
BUDGET wall=1364.151s execution=1364.135s admissionOnly=0.015s
verdict: GREEN exit=0
```

### Published source history through tested HEAD

```text
9f0b62cea551501728c6b2df365e0151e6a73a15 test(card-1906): pin compiler stack-env refusals and compose transport before implementation
f413c9ffe637e4ac8a74583df3476eb64af2b4de feat(card-1906): preserve opt-in compiler policy through runner stack environments
516fd2fa46329ad4dbb146357e236715bda534f6 test(card-1906): regenerate derived impact index after compiler-policy coverage
7669a964d90e3964875043fa4034f2429e8b983f docs(card-1906): preserve red output and bind bounded final core checkpoints
63f94c280026ecfa2e0358f2083f50a9c2fc69c7 test(card-1906): keep model transport and checkpoint inventory complete
d60e81d0c7d902bfa7c6db2f8654ba7797db424e test(card-1906): admit native compiler-policy witnesses and repin policy inputs
cf443528e142e89ae7c7b4b899949649410d28e2 docs(card-1906): retain guard reds and bind admission-repair checkpoints
e2d3d6643c52d9a1873b462fd4a058434656c945 docs(card-1906): preserve source-bound checkpoint history and policy repair
```

The settlement progress marker gives the later report-only publication SHA; actual test results remain bound to `e2d3d6643c52d9a1873b462fd4a058434656c945`.

### Reopened-task handoff audit

Caller steering: finish this Code task now; R-2 is **commissioned to Debug c2489e73, manifest pending** at `docs/investigations/2026-11-15-card-1906-r2-bounded-selection.md`. No new build or test was run for this report-only update. All historical outcomes, costs, source identities and unedited receipt lines remain retained. Every PC and variant above remains pending for Mutation. Both named defaults remain OFF: Compose `RunnerLinuxCompilerPolicyDefault` uses fallback `0`; shell `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0`. Restart: none. The next stage remains Code for the commissioned Debug's results, followed by Final Review only after clean completion.
