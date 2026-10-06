# CARD-0817 fixture repair (f3fc782f)

Original Code / landing owner: `2a876d8a-7681-47be-94bb-c8cba4abfade`.
Repair branch: `feat/card-task-f3fc782f`; base: `73e469314ea9d13a65c5ad21375b924e7a21e925`.
Implementation plan: [HTTPS token plan](2026-10-05-card-0817-https-token-push-credential-plan.md).
Prior evidence: [.antiphon/task-2a876d8a.md](../../..//.antiphon/task-2a876d8a.md).

The direct repair brief commissions one repair round for F-1/F-2, exact narrow
filters only, trailing method wildcards, no whole-Unit or baseline sweep.
This specific scope controls over its appended generic Final profile. Whole-Unit
qualification remains caller-owned and incomplete; this repair claims no full
Final profile. There are no production edits or live acceptance actions.

## Repair

- F-1: retain an exact topology roster, updated to 15 runner mounts and 3
  state-init mounts for the new Compose token-directory bind. Assert its exact
  fixture source, target, bind kind, directory form and read-only mode. Preserve
  existing negative vectors and add missing, foreign-source and writable token
  mount refusals, each before any Docker mutation.
- F-2: recognize only `ensure_runner_github_token_dir` in the host-helper roster.
  Assert its first commands refuse a non-host lane before sudo, both in the
  nested-lane guard and the original V-15 method. Retain all other prohibitions.

Pre-edit CP-18/CP-19 reproduced both defects at the exact published base using
`.antiphon/c0817-fixture-red.md` and one isolated build. CP-18 expected `[14,3]`
but received `[15,3]`; CP-19 rejected the token helper's `sudo install -d`.
These are ordinary defect reproductions, not deliberate positive controls.

The original CP-13 at `71685b84772b82517c2db5dd5d18e085ca8f360a` already
reproduced `Nested_lane_never_uses_sudo_or_python` failing on
`c1008_owned_mounts`'s `sudo readlink`. Its retained fresh TRX, failure record,
and clean/verified receipt were inspected in the original task's
`.antiphon/c0817-baseline-checkpoints/20261006-003610-f93f/`. That unrelated
failure remains a separately reported finding; this repair does not exempt an
unguarded helper or hide the red test.

## Verification design

| ID | Required witness | Scope |
|---|---|---|
| V-19 | Exact 15/3 logical roster, token bind identity/mode and all eight positive/negative input vectors | `C994_Production_mount_topology_is_proven` |
| V-20 | Token-directory helper refuses non-host execution first; only its body is admitted | `Nested_lane_never_uses_sudo_or_python`, `Deploy_parent_creates_the_github_token_directory_without_reading_it` |
| R-4 | Complete retired-temp host fixture, including census, leases, persistence and no-mutation checks | all 20 `RetiredTempContainerHostTests` methods |
| V-12,V-13,V-14,R-2 | Original compose/image custody scope | original CP-4 |
| V-15,R-3 | Original deploy/restart/recycle scope plus token host guard | original CP-5 |

V-1..V-11, V-16..V-18 and R-1 retain the earlier report's results, not new proof
at this repair SHA. Original operator acceptance steps 1-5 remain post-land work.
CP-18 reruns the red method; CP-20 additionally meets the explicitly required
full affected class. No further unchanged repetitions follow green.

### Positive controls pending Mutation

PC-1..PC-20 from the original plan remain pending, including both PC-14 variants
(username output and whitespace handling). Add PC-21: remove the first non-host
refusal in `ensure_runner_github_token_dir`; detect with the exact V-15 method,
whose first-command assertions must fail. Add PC-22 (missing/source/writable
variants): admit the respective invalid token mount in production topology
validation; detect with the exact V-19 method's refusal/no-mutation assertions.
Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
The already-red nested-lane method cannot serve as a positive-control witness
until its independent inherited failure is resolved.

### Cost

One isolated test-project build, 53 TUnit executions (52 unique methods),
estimated 26 minutes including build. The tool bootstrap is separately gated
and uses `bin-c0817-fixture-tool/`. No whole assembly, whole Unit, Pty or live
Docker run. Fixture Docker calls use an offline fake and `docker compose config`.
Rows are serial to respect the assembly-local process-spawn limiter.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-18 | repair | `tests/Antiphon.Tests -> bin-c0817-fixture/` | mount-repair | `/*/*/RetiredTempContainerHostTests/C994_Production_mount_topology_is_proven*` | V-19 | one method, 0 failed/skipped | 1 | 8 | true |
| CP-19 | repair | CP-18 | host-helper-repair | `/*/*/RemoteScriptContractTests/Nested_lane_never_uses_sudo_or_python*` | V-20 | one method; report inherited red separately | 1 | 1 | true |
| CP-20 | repair | CP-18 | retired-temp-full | `/*/*/RetiredTempContainerHostTests/C994_*` | V-19, R-4 | all 20 methods, 0 failed/skipped | 20 | 13 | true |
| CP-4 | repair | CP-18 | custody-linux | `/*/*/DindRunnerContractTests/*` | V-12, V-13, V-14, R-2 | all 25 methods, 0 failed/skipped | 25 | 1 | true |
| CP-5 | repair | CP-18 | deploy-script-linux | `/*/*/RemoteScriptContractTests/(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)\|(C1008_Recycle_exact_default_volumes*)\|(Scrub_covers_github_token_prefixes*)` | V-15, V-20, R-3 | all 6 methods, 0 failed/skipped | 6 | 3 | true |

Run through `scripts/build-slot.ps1` with the checkpoint tool's `run --plan`
and `--expected-source-sha <committed HEAD>`; await completion, inspect every
fresh TRX and validate passing rows. Preserve original CHECKPOINT lines and
source/build provenance in the task report. Remove owned alternate outputs.
