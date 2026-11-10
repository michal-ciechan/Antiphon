# CARD-1225 Review, ordinary round 1 (Final), task 224d47be

Verdict: CLEAN. Linux placement arms the orchestrator deny hook with `pwsh -NoProfile -Command` and the invoked command exits 2. Windows placement keeps the Windows PowerShell 5.1 command text, byte-identical to the parent. No blockers.

## Subject

- Code owner: `4b46ef18-d0a6-4887-8cc1-5b42b7ab18f9` (branch `feat/card-task-4b46ef18`, frozen).
- Reviewed SHA: `bcbc01f23e299c6cb4ba84a36abb82a7264424be`.
- Reviewed range: `84bc17576287fb43cf2dc04395970a8bc52e2d76..bcbc01f23e299c6cb4ba84a36abb82a7264424be`.
- Review branch: `feat/card-task-224d47be`. This document is the only Review commit.
- Card has no product plan and no `### Checkpoints` table. The closed selection is the ignored extract `.antiphon/card-1225-review-plan.md`, which does not change the reviewed SHA.

## Behavior

`UsesLinuxDenyHook` reads `RunnerPlatformWire.Normalize(ObservedPlatform)` first. Observed `linux` selects the pwsh command. Observed `windows` selects the Windows command, including when `RequiredPlatform` is Linux. A blank or unknown observed value falls through to `RequiredPlatform.Linux` for pwsh, and every other requirement, including Any, keeps the Windows command.

`ArmDenyHookAsync` writes `DenyHookSettingsFor(task)` on a successful arm. The matcher stays `Edit|Write|MultiEdit|NotebookEdit`. The script writes the orchestrator deny line to stderr and exits 2. An existing settings file is left in place and the method returns false. A missing worktree returns false. Those not-armed paths are unchanged.

The Linux tests arm through `ArmDenyHookAsync`, then start the command with `/bin/sh -c` and require exit code 2 plus the deny line on stderr:

- `linux_placement_deny_hook_is_pwsh_and_exits_2`: (Any, linux), (Linux, null), (Linux, linux), (Windows, linux).
- `non_linux_placement_deny_hook_stays_windows_powershell`: (Any, null), (Windows, null), (Any, windows), (Windows, windows), (Linux, windows). The command equals the Windows constant and is not started.

Parent compare of `DenyHookSettingsJson` at `84bc17576287fb43cf2dc04395970a8bc52e2d76` and the reviewed SHA: both 639 characters, identical. The replace needle `"command": "powershell -NoProfile -Command ` occurs once. The Linux text contains `pwsh -NoProfile -Command ` and `exit 2`, and does not contain `powershell`.

The docs sentences in `docs/orchestration-loop.md` (Orchestrator deny hook on Linux), `docs/agent-kinds.md`, and the production-contract row in `docs/docker-stack.md` match that precedence and the pwsh command.

## Receipt

Tool run `20261110-212422-f102`, serial, `--expected-source-sha bcbc01f23e299c6cb4ba84a36abb82a7264424be`. `scripts/validate-checkpoint-receipt.ps1` exited 0:

```
CHECKPOINT SOURCE VALID source=bcbc01f23e299c6cb4ba84a36abb82a7264424be rows=4
```

CHECKPOINT CP-1 commit=bcbc01f23e299c6cb4ba84a36abb82a7264424be build=ok filter=/*/*/DelegationWorktreeTests/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/task-224d47be/.antiphon/checkpoints/20261110-212422-f102/rows/CP-1/run.trx slot=granted waited=90s dirty=0 source=bcbc01f23e299c6cb4ba84a36abb82a7264424be sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=bcbc01f23e299c6cb4ba84a36abb82a7264424be build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-224d47be/.antiphon/checkpoints/20261110-212422-f102/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=bcbc01f23e299c6cb4ba84a36abb82a7264424be sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=bcbc01f23e299c6cb4ba84a36abb82a7264424be build=reused filter=/*/*/(TestLaneCategoryGuardTests*)|(ServerGitProcessTests*)/* executed=24 passed=24 failed=0 skipped=0 trx=/work/worktrees/task-224d47be/.antiphon/checkpoints/20261110-212422-f102/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=bcbc01f23e299c6cb4ba84a36abb82a7264424be sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=bcbc01f23e299c6cb4ba84a36abb82a7264424be build=ok filter=/*/*/(GitIndexLockTests*)|(ServerGitAssemblyTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-224d47be/.antiphon/checkpoints/20261110-212422-f102/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=bcbc01f23e299c6cb4ba84a36abb82a7264424be sourceState=clean buildSource=verified

Tool verdict: GREEN exit=0. `unlisted: none` for that run. Wall 33m46s. Host Debian GNU/Linux 12, 24 cores.

## Other gates

- Evidence diff `84bc17576..bcbc01f`: `EVIDENCE result commits=2 entries=0 violations=0`.
- `git merge-tree --write-tree origin/master HEAD` exit 0, tree `745e312b8ca25fb2e3041b57dcf597a418380306`. Fetched `origin/master` is `ef7afd44927206f6c03547670a5add84130bdce9`. The brief pin `d27d19165db7a7834459645b389afb53bfce231a` is an ancestor of that tip; master advanced during this review.
- Impacted classes: `server-git-start-sites`, `test-lane-category`. Downgrade selector count: 0. The migrations kind did not match, and no migration file changed.
- OS witness: the production selector is `UsesLinuxDenyHook` / `DenyHookSettingsFor`. The Linux cases drive that selector and invoke the armed command. `scripts/check-os-witnesses.ps1` needs a product `### Checkpoints` table, which this card does not have, so that script was not run. Native Windows PowerShell 5.1 was not started on this Linux host. The Windows cases assert the command text.
- Positive controls stay pending. The committed red witness is `37f780b688aac5a819131050c05f5e25b840b0d2` (Code reported 4 Linux failures, 5 passes). This Review did not rebuild that SHA and did not re-apply Code's mutant. Code owed Mutation for the two new methods.
- No V-n or R-n rows and no manual acceptance.

## Disclosed runs outside the receipt

Before the ignored plan existed, one leased build and one leased test ran the CP-1, CP-2, and CP-3 classes together. Build lease `b4fa02b8-66b8-46f5-8ea4-e92599ca7f43` (waited 30s). Test lease `a572b22e-a4af-4ec9-ba7f-033f73cc5afc` (waited 0s): total 159, failed 0, skipped 0, duration 3m 46s. 58+77+24=159. Reason: that run preceded the receipt. The receipt above is the qualifying selection, and CP-4 ran only there. The checkpoint tool itself was built once through lease `9d835fd6-1cc0-4de0-bde5-e0ebb6bbc605` (`dotnet build tools/Antiphon.Checkpoints -p:UseAppHost=false`), then `run --no-build`. The tool took its own row slots.

## Performance (R1)

The receipt wall was 33m46s (`wall=2026.353s`, `execution=1786.333s`, `admissionOnly=240.021s`). CP-1's isolated graph build was 1279s and its tests were 37.5s. CP-2 tests were 82.7s; slow class `TestClassificationPolicyTests` 82s for 41 tests. CP-3 tests 3.8s. CP-4 build 91.6s, tests 2.2s. No test phase hung. Standing rule 10 asks the orchestrator to dispatch a Debug task with this run id, SHA, host, and timing. That cost does not hold this land.

## Advisories

These do not hold the land.

- `SpecialistSpec` deny-all hook command is still `powershell` (`server/Application/Services/SpecialistSpec.cs`). Code left it out of scope.
- `docker/session-runner-grok/Dockerfile` installs `pwsh` in the `session-testing` stage. The throwaway `runtime` target does not. Production server2 uses `session-testing`, which is what the docker-stack sentence describes.
- An unobserved `RequiredPlatform.Any` task keeps the Windows command. The new docs say so. A create that records no observed platform on a Linux runner would still write `powershell`.
- Point-in-time text still says the hook is unsupported: `docs/investigations/2026-11-07-decide-card-1221-linux-docker-production-contract.md` and `docs/superpowers/plans/2026-11-09-card-1695-standing-orchestrator-on-server2.md` line 191.

## Blockers

None.
