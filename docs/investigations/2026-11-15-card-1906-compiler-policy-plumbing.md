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
