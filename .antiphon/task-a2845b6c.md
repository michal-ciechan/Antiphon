# Review a2845b6c: CARD-1778 S2 (runner trust seed, sidecar compose rows)

Verdict: CLEAN. Ordinary round 1, Final. Subject `origin/feat/card-task-d68d1868` tip
`df1516b315860aec4cad872114ec4b6d4a0ef272` (Code task `d68d1868-c5bd-47d0-8917-cbc62f624144`, Grok Code).
Reviewed range `76a85e11aa6c6f5c7156073a3f7206aab255d283..df1516b315860aec4cad872114ec4b6d4a0ef272` (S1 at
`76a85e11a` was reviewed clean by `fbed504a` and was not re-reviewed). Master at review time
`b35fb137acde6e0c52e296dd6d7b4e345fe3d238`. No product file was changed by this review; the only
commit on `feat/card-task-a2845b6c` is this document.

## Brief checks

| Check | Result | Where |
|---|---|---|
| Seed written by the runner, in the config dir it owns, just before spawn, on both lanes | Yes. One call at the top of the `try` in `StartCoreAsync`, before the `useHerdr` split, so the Herdr lane and the pty-host lane both pass it. | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:711-715` |
| Scoped to the launch cwd only | Yes. `ClaudeProjectTrust.Seed(request.Cwd, path)` writes exactly `projects[ProjectKey(cwd)].hasTrustDialogAccepted`; no ancestor, no other key. | `ClaudeLaunchTrustSeed.cs:49`, `ClaudeProjectTrust.cs:155-205` |
| No write outside `CLAUDE_CONFIG_DIR`, no traversal/symlink write | Path is `Path.Combine(<request env CLAUDE_CONFIG_DIR> ?? <runner process CLAUDE_CONFIG_DIR>, ".claude.json")`, else `~/.claude.json`. The file is never created (`NoConfigFile`), so a dir without Claude's own config gets nothing. Replacement is `File.Move(tmp, path)`: a symlinked `.claude.json` is replaced, never written through. The request env is server-supplied and is the same env the spawned Claude reads, so seed and reader agree by construction. | `ClaudeLaunchTrustSeed.cs:13-33,77-91`; `ClaudeProjectTrust.cs:212-233` |
| Mode/owner sane | Runner uid 1654 writes `/state/claude/.claude.json` on the `runner-state` volume (chowned 1654 by init); the server mounts that volume `ro`. The rewritten file takes the umask default mode (pre-existing `WriteAtomic` behaviour, advisory A-2). | compose `:53`, `:148-150` |
| Idempotent | `Seed` memoises Seeded/AlreadyTrusted per (path,key); a trusted key is not rewritten. | `ClaudeProjectTrust.cs:96-125` |
| No secret read or printed | Reads only `.claude.json` (no `.credentials.json`); the log line is `Claude trust seed <Outcome> session <id>`, no path, no cwd, no env value. | `ClaudeLaunchTrustSeed.cs:50,55` |
| Never throws | `Apply` and `ConfigPathFor` catch all; `Seed` never throws by contract; the logger call is wrapped. `ReadProcessEnvironment()` runs inside the launch `try`, so even its failure would fail that launch cleanly. | `ClaudeLaunchTrustSeed.cs` |
| Sidecar `CLAUDE_CONFIG_DIR=/state/claude` on runner-state; server state not mounted rw | Yes. Sidecar mounts `runner-state:/state` and `work:/work` only; `server-state` is not mounted on the sidecar at all; server keeps `runner-state:/runner-state:ro`. The child inherits the runner's env with the launch env layered on top (`Pty_child_inherits_the_runner_containers_claude_oauth_token`, rerun green in CP-2b). | compose `:144-150`, `:50-53` |
| Instructions path on `/work/.antiphon`, readable by the sidecar | `Delegation__OrchestratorInstructions__Path` binds to `DelegationSettings.OrchestratorInstructions.Path` (`DelegationSettings.cs:346`), absolute-path checked by `AntiphonDataPaths.ResolveOrchestratorInstructionsPath`; `/work` is `work` rw on both services; init creates and chmods `/work/.antiphon` and chowns 1654. | `server/Application/Core/AntiphonDataPaths.cs:13-28` |
| `/work/agents` uid 1654 mode 0755 | In the init `mkdir -p` list and the `chmod 0755` list; `chown -R 1654:1654 /work` follows. | compose `:24` |
| `SessionRunner__VerifyExecutablesOnServer` binds | `Configure<SessionRunnerSettings>(GetSection("SessionRunner"))`, property default true, read at `AgentControlService.cs:113`. S1 scope, confirmed only as the key S2 pins. | `server/Program.cs:150` |
| CARD-1775 contract tests still pass | `Server2ProductionServerContractTests` 12/12 inside CP-3 (17 = 12 + 5). | CP-3 below |
| Policy digests | No policy, registry, slow-test or classification file in the diff; guard batch green (CP-G1 50/50, CP-G2 4/4). | `git diff --name-only` |
| Witnesses can go red | V-4..V-8 assert file state and outcome enums on temp config files (request vs process file both checked, so M4/M5/M6 each flip an assertion); V-9..V-13 are text pins with exact values. Probe below: M7+M8 in a scratch tree made V-9 and V-11 red, 3 others green. | probe |
| CARD-1775 A-9 resolved | Yes. Specialist launches now reach the runner with `Exe=/usr/local/bin/claude` and cwd `/work/specialists/<x>`; the runner seeds `/state/claude/.claude.json`, the file Claude in the sidecar reads. The server-side seed stays and, with no server `CLAUDE_CONFIG_DIR`, returns `NoConfigFile` at Debug level. Requires the operator login (plan step 0.13) so the file exists; otherwise `ClaudeStartupReadiness` answers the dialog as before. | `StandingSpecialistProvisioner.cs:206-230` |
| OS branch witnesses | No OS-selected production branch changed (`Replace('\\','/')` + `Path.GetFileName` is portable); the Linux-only skip is in the Integration test, so `check-os-witnesses` is not triggered. | `docs/testing-and-build.md#os-branch-witnesses` |
| Waits for input | None added. | plan "Sessions that wait for input" |

## Code report match

Code's CP-n lines came from `GET /api/agent-tasks/d68d1868-...` `result` (its `.antiphon/task-d68d1868.md` was
never committed; `.antiphon/` is ignored on runner mirrors). Code claimed run `20261108-121806-dff7`: CP-2 9/9,
CP-3 17/17, touched classes rerun, guard batch 75/75, V-4..V-13 red on restored mutants, evidence diff 0 violations,
merge-tree clean against master `f54741ef3`. Plan `### Checkpoints` rows after S2 are CP-2 and CP-3; both are
present with nonzero counts and match the plan filters and floors (plan CP-2 floor 5, 9 executed because V-8 has
five `[Arguments]` cases). No unlisted build or test run. All counts below are my own rerun.

## Rerun at the reviewed SHA (one checkpoint-tool run, review plan extract `.antiphon/c1778-review-plan.md`, not committed)

run: 20261108-131051-b12b  commit: df1516b315860aec4cad872114ec4b6d4a0ef272  branch: feat/card-task-a2845b6c  host: Debian 12 cores=24
source: df1516b315860aec4cad872114ec4b6d4a0ef272 state=clean buildSource=verified
CHECKPOINT CP-2 commit=df1516b315860aec4cad872114ec4b6d4a0ef272 build=ok filter=/*/Antiphon.SessionRunner.Tests/ClaudeLaunchTrustSeedTests/* executed=9 passed=9 failed=0 skipped=0 slot=granted waited=105s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-2b commit=df1516b315860aec4cad872114ec4b6d4a0ef272 build=reused filter=/*/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests/* executed=2 passed=2 failed=0 skipped=0 slot=granted waited=120s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-G2 commit=df1516b315860aec4cad872114ec4b6d4a0ef272 build=reused filter=/*/*/(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)/* executed=4 passed=4 failed=0 skipped=0 slot=granted waited=30s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=df1516b315860aec4cad872114ec4b6d4a0ef272 build=ok filter=/*/Antiphon.Tests.Infrastructure/(Server2SidecarStandingAgentsTests*)|(Server2ProductionServerContractTests*)/* executed=17 passed=17 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-G1 commit=df1516b315860aec4cad872114ec4b6d4a0ef272 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=50 passed=50 failed=0 skipped=0 slot=granted waited=30s dirty=0 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=165s build=105.6s testsWall=0.06s; CP-2b testsWall=6.1s; CP-3 slotWait=75s build=443.0s testsWall=15.1s; CP-G1 testsWall=134.7s (SLOW CLASS TestClassificationPolicyTests 134s tests=39); CP-G2 testsWall=1.4s
wall: 17m34s  sequential-equivalent: 8m11s  builds: 2  rows: 5 green 0 red 0 skipped  unlisted: none  verdict: GREEN exit=0
validate: CHECKPOINT SOURCE VALID source=df1516b315860aec4cad872114ec4b6d4a0ef272 rows=5 (exit 0)
evidence: /work/worktrees/task-a2845b6c/.antiphon/checkpoints/20261108-131051-b12b/report.md

Timing against the plan budget: CP-2 3.3 s and CP-3 28 s test bodies against 5 and 8 minute estimates; the
`Antiphon.Tests` cold build took 443 s under two concurrent runs on server2 (plan: 2-6 min). Nothing near 30 min.
The whole Unit lane was not run (about 3 h on server2; Code left it to nightly and rc, the brief's 40-minute
budget named the touched classes and the guard batch).

## Mutant probe (scratch copies only; the worktree was never modified)

The CP-3 build output was copied to the session scratchpad with `git archive HEAD` of `Antiphon.sln`, the two
composes, `docs/docker-stack.md` and `scripts/deploy-server2-server.ps1`, then
`Server2SidecarStandingAgentsTests` ran against each tree under its own build slot (`dotnet <dll>`):

- M7 + M8 (`CLAUDE_CONFIG_DIR` moved from the sidecar to `server`; `/work/.antiphon` dropped from both init
  lists): total 5, failed 2: `Sidecar_claude_config_dir_is_on_runner_state` (V-9) and
  `Orchestrator_instructions_file_is_on_the_shared_work_volume` (V-11). Expected red.
- M8 variant (`/work/.antiphon` dropped from the `mkdir -p` list only): total 5, failed 0. Advisory A-1.

C# mutants M4-M6 were judged by inspection (each flips a file-state or outcome assertion in V-4..V-6); their
red-then-green cycles stay with the SourceLanding Mutation (PC-3), as does PC-1 for the call site and S1's PC-2.

## Other checks

- `check-evidence-diff.ps1 -BaseRef 76a85e11a -HeadRef df1516b31`: commits=2 entries=0 violations=0.
- `git merge-tree --write-tree origin/master(b35fb137a) df1516b31`: clean, tree `53c772d80cc843044b8a0e8965e60ab9f20157ed`.
- Plan file in the subject tree equals `origin/feat/card-task-c748deca`'s copy (no diff).

## Advisories (CARD-1792 filed; none holds the land)

- A-1 V-11 checks `/work/.antiphon` by whole-block `ShouldContain` and the chmod list, not the mkdir list; add
  `InitMkdirTargets(init).ShouldContain("/work/.antiphon")`.
- A-2 Pre-existing `ClaudeProjectTrust.WriteAtomic`: fixed temp name and `File.Move` lose the original mode and
  can drop a concurrent writer's update; now exercised on a config file eleven sidecar Claude processes share.
  Mitigated by memoisation (one write per new cwd per process) and the launch-time dialog answerer.

--- review evidence ---
subjectTaskId: d68d1868-c5bd-47d0-8917-cbc62f624144
reviewedSourceSha: df1516b315860aec4cad872114ec4b6d4a0ef272
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 76a85e11aa6c6f5c7156073a3f7206aab255d283..df1516b315860aec4cad872114ec4b6d4a0ef272
