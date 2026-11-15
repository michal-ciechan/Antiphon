# CARD-1906 compiler-policy stack environment plumbing

Code owner/landing owner: `28eecdf5-2364-4811-934c-f9fbdc10ef55`.
Base: `d100f8fa2c679a5c74b6b235087308776a8f5d50` (current origin/master).
The dispatch brief is the implementation plan: default OFF; no live activation.
This manifest binds its verification design to the actual deploy reader in
`scripts/c590-remote.sh`, called by `deploy-server2.ps1`.

## Invariants and ordinary acceptance

V-1: exact saved `ANTIPHON_LINUX_COMPILER_POLICY=0` or `=1`, LF, at most once.
Malformed attempts refuse `RunnerLinuxCompilerPolicySettingInvalid`, exit 2,
before deployment effects in both full phase functions and Compose wrappers.
V-2: absent file/key means 0 independently per project; ambient values cannot
change saved intent. Whole-line comments and unrelated keys do not get sourced.
V-3: both real Compose models carry the saved value; both phase rewrites preserve
it and the other file. Default rendering changes only by the new OFF setting.
V-4: trace runner -> host -> CLI and build wrapper inheritance, and witness the
rendered key reaching a real session/build-driver child without launching builds.
R-1: existing workspace-slot assertions, deployment API and ordering smoke stay green.
R-2: guard union, impacted selector and all matching impacted-classes rows stay green;
regenerate derived index after building its driver, twice (second unchanged).
R-3: evidence history, OS inventory, merge-tree and nightly pins verified.
Manual production acceptance is deferred to activation: exact line
`ANTIPHON_LINUX_COMPILER_POLICY=1` in
`/home/mc/antiphon-server2/secrets/stack.env`, then the next `redeploy-old` and
container environment inspection. No host mutation/restart in Code.

## Performance budget

No database, real runner service or Docker daemon setup. New shell cases use
owned ignored fixture directories and real `docker compose config` only.
Refusal shapes are parameterized, each checks four entry points; bounded child
lifetimes and stdout/stderr retained in TRX. ProcessSpawnLimit serializes children.
One cold isolated Antiphon.Tests build per committed group, reused by its rows.
Estimates provisional (N=0 for the new cases); guard prior CARD-1906 investigation
was 334s, native test build 218s. This group should fit 27 minutes execution (including the selector-required runner/PTY guard builds);
serial rows/build deadline 9m, finite admission and 45m hard wall. No full lane.
No repeat after green. Red CP-1 precedes implementation; final closed list follows.
Checkpoint timings/logs and loadavg live under `.antiphon/c1906/`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1906-b/` | compiler-policy | `/*/*/RunnerLinuxCompilerPolicyTests/*` | V-1, V-2, V-3, V-4 | every new setting case, zero failures | 35 | 2 | true |
| CP-2 | all | CP-1 | deploy-contracts | `/*/*/(DockerStackContractTests*)\|(DeployServer2ApiDefaultTests*)/*` | R-1 | all named class cases, zero failures | 2 | 2 | true |
| CP-3 | all | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(ImpactedClassesSelectorTests*)/*` | R-2 | every named class, native Windows skip separately owed | 12 | 6 | true |
| CP-4 | all | CP-1 | remote-ordering | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C0995_Deploy_temp_checks_host_absence_before_checkout*)\|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)\|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)\|(C849_Deploy_prepares_and_verifies_before_acceptance*)` | R-1, R-2 | five impacted methods plus both other harness callers, zero failures | 7 | 2 | true |
| CP-5 | all | n/a | workspace-slots | `node scripts/fixtures/c346-workspace-slot-compose.mjs` | R-1 | unchanged slot assertions and real Compose models | n/a | 1 | true |

| CP-6 | all | CP-1 | limiter-main | `/*/*/ProcessSpawnLimitTests/*` | R-2 | main assembly spawn guard | 1 | 1 | true |
| CP-7 | all | CP-1 | platform-census | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | R-2 | impacted native annotation census | 2 | 1 | true |
| CP-8 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1906-runner/` | limiter-runner | `/*/*/ProcessSpawnLimitTests/*` | R-2 | runner assembly spawn guard | 2 | 3 | true |
| CP-9 | all | `tests/Antiphon.Agents.Pty.Tests -> bin-c1906-pty/` | limiter-pty | `/*/*/ProcessSpawnLimitTests/*` | R-2 | PTY assembly spawn guard | 2 | 2 | true |

## Scope and controls

Invariant name: saved compiler-policy intent is exact and independent per runner.
Shared impact is bounded to deployment environment construction; no compiler
implementation, session runtime, persistence, leases or migration changes.
`RollingServer2*` classes do not exist. Real volume recycle lifecycle tests are
outside this environment-only change; impacted remote ordering methods cover the
phase adjacency. No Unit lane or full assembly run.
Mutation is operator-waived for this dispatch (2026-11-12); no deliberate mutants
in Code. Candidate PC-1 reader accepts malformed values; PC-2 drop standing
passthrough; PC-3 drop temp passthrough; PC-4 drop one phase persistence; PC-5
change missing-key default. All remain pending for caller-commissioned Mutation,
never discharged by ordinary red-first evidence.

## Measured first red

At `9f0b62cea551501728c6b2df365e0151e6a73a15`, CP-1 executed 33 cases: 0 passed, 33 assertion failures, 0 skipped. All 128 refusal boundaries hit the side-effect marker (91 instead of 2); real Compose lacked the setting. Build execution 423.978s (MSBuild 368.92s), row execution 18.877s, run wall 468.361s; slot waits 0s. N=1 under variable load, not calibrated p95. Keep the nine-minute deadline unchanged.

## Full derived-impact expansion stop gate

The committed diff expands through derived file-read edges into whole rollout and
retirement classes, beyond the explicit four impacted-classes rows. Inventory is
retained in the report; this is not a green or narrowed-scope claim. Prior Debug
`docs/investigations/2026-11-13-selector-fixture-retirement-checkpoints.md` records
a stopped over-30-minute broad selection and a 252-child aggregate roster. Its
25/24/15-minute rows cannot satisfy this brief's under-ten-minute rows. Caller
Debug must supply a bounded expanded inventory before those classes run. Core
CP-1..9 remain independent and mandatory; no aggregate or full-assembly sweep is
authorized by this manifest. All expanded unexecuted classes remain R-2 pending.
