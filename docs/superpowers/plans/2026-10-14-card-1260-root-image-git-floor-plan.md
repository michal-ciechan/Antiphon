# CARD-1260 root image Git floor

Code/landing owner: `3dac1d55-cd4f-4dd6-a78b-dd448014ef86`.
Task base and fetched master: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c`.

## Finding

`LandingGit`, `LandOperationFactory`, `GuardedWorktreeRemoval` and
`GuardedVerificationRemoval` run `git show-ref --exists` (Git 2.43+). They are registered
in `server/Program.cs`. The production server runs on the desktop under AppHost and uses
that host's Git. The root `Dockerfile` server image is used by the self-contained
`docker-compose.yml` stack, and by the nested child stack a server2 session starts from
that file. The server2 `state-init` service also uses it, but runs only `init-state.sh`.
Any land or guarded removal inside the self-contained or nested stack used the image's
apt Git 2.39.5 and failed with `unknown option 'exists'` (exit 129).

## Change

- `docker/git-minimum-version.sh` declares the floor (`GIT_MINIMUM_VERSION=2.43`) and
  checks a given version, or the installed `git --version`, using `sort -V`. Below the
  floor, or for an unparseable version, it exits 1.
- The root `Dockerfile` gains the runner's `git-build` stage, with the same pinned Git
  2.47.3 tarball and SHA-256. Its runtime installs no apt Git. It copies the pinned build
  and the guard, then runs the guard.
- `docker/session-runner-grok/Dockerfile` drops `ARG GIT_MINIMUM_VERSION` and runs the
  same guard. The floor is now declared only in the guard script.
- `docs/docker-stack.md` records the server image's Git and the measured version.

## Verification design

- V-1: the guard refuses 2.39.5, 2.42.9, 2.9.0 and 1.99.99. It admits 2.43, 2.43.0,
  2.47.3, 2.100.0 and 3.0.0. It refuses malformed versions. Without an argument, it reads
  an installed fake Git (2.39.5 refused, 2.47.3 admitted, unusable refused). Each case
  must produce the exact stdout and stderr. The cases 2.9.0 and 2.100.0 catch a string
  comparison that should be a version comparison.
  `GitMinimumVersionGuardTests.Guard_refuses_git_below_the_landing_floor`.
- V-2: the server image pins the runner's Git version and SHA-256. It verifies the
  tarball before unpacking it, with `sysconfdir=/etc`. It installs no apt Git, keeps
  libcurl-gnutls and runs the guard after both COPY steps.
  `DockerStackContractTests.Server_image_git_meets_the_declared_minimum`.
- V-3: the floor is declared once, in the guard. No Dockerfile restates
  `GIT_MINIMUM_VERSION`. The build context includes the guard and the docs state the
  floor. The runner contract reads the floor from the guard.
  `DockerStackContractTests.Git_floor_is_declared_once` and
  `DockerStackContractTests.Runner_git_meets_the_declared_minimum`.
- R-1: the full `DockerStackContractTests` and `GitMinimumVersionGuardTests` classes pass.
- R-2: the CARD-1210 native roster (58 identities, 69 results) and the
  ProcessSpawnLimit/timeout portfolio (12 classes) include the new native class.
  `WindowsUnitPlatformAdmissionTests`, `ProcessSpawnTimeoutPolicyTests` and the
  neighbouring `GrokSha256PinTests` pass.
- M-1: one fresh root `docker build` goes through `scripts/build-slot.ps1` and is queued
  once. Its build log must contain the guard's `meets minimum 2.43` line. In the image,
  `git --version` must report 2.47.3. In a fresh repository, `git show-ref --exists`
  must return 0 for a present ref and 2 for a missing one. The image revision label must
  equal the built SHA.

S1 commits the tests and this plan before the fix. CP-1 must be red on exactly the four
new or changed cases: V-1, V-2, V-3 and the runner contract. The brief excludes a
whole-Unit lane. That lane is deferred, not passed.

Positive controls for SourceLanding Mutation are all pending:

- PC-1: replace `sort -V -C` with `sort -C` in the guard. V-1 fails on 2.9.0 and 2.100.0.
- PC-2: set the guard floor to 2.39. V-1 admits 2.39.5 and `Runner_git...` fails.
- PC-3: remove the server runtime's `sh .../git-minimum-version.sh` step. V-2 fails.
- PC-4: restore `ARG GIT_MINIMUM_VERSION=2.43` in the runner Dockerfile. V-3 fails.
- PC-5: add `git` back to the server runtime's apt line. V-2 fails.
- PC-6: drop the `case` malformed-version check. The V-1 unparseable rows fail.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1260-red/` | floor-red | `/*/*/(GitMinimumVersionGuardTests*)\|(DockerStackContractTests*)/*` | V-1, V-2, V-3 red | 116 executed; exactly 4 red before fix (guard, server image, declared once, runner contract), 0 skipped | 116 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1260-green/` | floor-green | `/*/*/(GitMinimumVersionGuardTests*)\|(DockerStackContractTests*)/*` | V-1, V-2, V-3, R-1 | all 116 passed, 0 failed/skipped | 116 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | roster | `/*/*/(WindowsUnitPlatformAdmissionTests*)\|(ProcessSpawnTimeoutPolicyTests*)\|(GrokSha256PinTests*)/*` | R-2 | all 18 passed, 0 failed/skipped | 18 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Run CP-1 at
S1, then CP-2 and CP-3 at S2. Pass the committed HEAD as `--expected-source-sha`, use
`--total-timeout 150m` without `--keep-outputs`, and wait for completion. Linux builds use
`UseAppHost=false`. Build the checkpoint tool once through `scripts/build-slot.ps1` into
`bin-c1260-tool/`; that bootstrap is an unlisted, disclosed build. M-1 is the single
docker build that the brief allows.

### Cost

The checkpoint rows are estimated at 28 minutes. The docker build adds about
10–15 minutes cold, because it compiles Git. Authoring time and host slot waits come
on top. The total fits the 90-minute budget. No whole-assembly or whole-Unit run is
needed, because the change is confined to image build files, one shell guard and
contract tests.

## Results (Code 3dac1d55)

Checkpoint tool bootstrap (unlisted, disclosed): `build-slot.ps1 -Label c1260-checkpoint-tool-bootstrap`
built `tools/Antiphon.Checkpoints` into `bin-c1260-tool/` (waited 75 s, 0 errors).

S1 `37ec8031771007d3c0ed88d2719b67498590ed62`, run `20261014-194713-b173`:

```
CHECKPOINT CP-1 commit=37ec8031771007d3c0ed88d2719b67498590ed62 build=ok filter=/*/*/(GitMinimumVersionGuardTests*)|(DockerStackContractTests*)/* executed=116 passed=112 failed=4 skipped=0 trx=/work/worktrees/task-3dac1d55/.antiphon/checkpoints/20261014-194713-b173/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=37ec8031771007d3c0ed88d2719b67498590ed62 sourceState=clean buildSource=verified
```

The 4 red tests were exactly the planned ones. `Git_floor_is_declared_once` and
`Runner_git_meets_the_declared_minimum` failed because the guard was missing.
`Server_image_git_meets_the_declared_minimum` failed because the root Dockerfile had no
`ARG GIT_VERSION`. `Guard_refuses_git_below_the_landing_floor` failed because `sh` exited
2 on the missing script.

M-1 (`build-slot.ps1 -Label c1260-root-image`, `docker build` at S2 `797aac855be2`): the first
invocation was rejected while parsing arguments, because the legacy builder has no
`--progress`. No build step ran and the lease was held for 0 s. The second invocation built
image `143cdb711f61`; the lease was held for 523 s and the slot wait was 0 s. The tarball
check printed `git.tar.gz: OK` and the guard printed
`git-minimum-version: Git 2.47.3 meets minimum 2.43`. The revision label is
`797aac855be21183da6d9480150793b6dd5c5d20`. In the image, as uid 1654:

- `git` is `/usr/local/bin/git`, version 2.47.3, and no Debian git package is installed.
- In a fresh repository, `show-ref --exists` returns 0 for a present ref and 2 for a
  missing one.
- An HTTPS `ls-remote` returns 0.

S2 group at `52d53ed26fd7d976f0a5f44673554705d9ec7a7d` (S2 plus the measured-version docs),
run `20261014-200401-e038`:

```
CHECKPOINT CP-2 commit=52d53ed26fd7d976f0a5f44673554705d9ec7a7d build=ok filter=/*/*/(GitMinimumVersionGuardTests*)|(DockerStackContractTests*)/* executed=116 passed=116 failed=0 skipped=0 trx=/work/worktrees/task-3dac1d55/.antiphon/checkpoints/20261014-200401-e038/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=52d53ed26fd7d976f0a5f44673554705d9ec7a7d sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=52d53ed26fd7d976f0a5f44673554705d9ec7a7d build=reused filter=/*/*/(WindowsUnitPlatformAdmissionTests*)|(ProcessSpawnTimeoutPolicyTests*)|(GrokSha256PinTests*)/* executed=17 passed=16 failed=1 skipped=1 trx=/work/worktrees/task-3dac1d55/.antiphon/checkpoints/20261014-200401-e038/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=52d53ed26fd7d976f0a5f44673554705d9ec7a7d sourceState=clean buildSource=verified
```

CP-3 differs from the planned `Expect` in two ways:

- `C1210_Windows_linux_only_outcomes_are_exact` is Windows-only and skips on Linux. The
  `0 skipped` expectation was wrong, and this skip is expected.
- `C1210_Roster_closure_matches_source_gates` fails at
  `c1210-unrostered-gate: Antiphon.Tests.Scripts.RefreshGithubTokenRemoteCommandTests.Crlf_checkout_sends_carriage_return_free_remote_commands`.
  This failure is inherited. CARD-1245 commit `750254d9b`, already in the task base,
  added `NativeLinuxGate.Require(Reason)` to three `RefreshGithubTokenRemoteCommandTests`
  methods. Those methods appear in neither the base roster nor this one, and this task
  leaves the file unchanged.

Static closure check: across the test sources, the only gated methods missing from the
roster are those three CARD-1245 methods. `C1210_Native_linux_entries_guard_before_work`
(58/69) and `C1210_Guard_matches_native_platform` pass with the new entry. The S2 run was
not rerun because the red does not come from this change.

Both receipt validators (`Antiphon.Checkpoints validate` and
`validate-checkpoint-receipt.ps1`) exit 2 with `cleanup_refused` for both runs. The tool
keeps build outputs when any row is red: S1 was red by design, and S2 is red because of the
inherited CP-3 failure. The tested source rows themselves were clean.

The whole-Unit lane was not run because the brief excluded it. That lane is deferred, not
passed. PC-1 through PC-6 remain pending for SourceLanding Mutation.
