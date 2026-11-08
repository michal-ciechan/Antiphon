# Review CARD-1778 S1 (task fbed504a, Final/Full, round 1): CLEAN

Subject: origin/feat/card-task-c4eea940 tip 76a85e11aa6c6f5c7156073a3f7206aab255d283 (Grok Code).
Base c460b0d0a4de591b8e662f8dbc57eeac3f26d249. Master f54741ef3bc08baa0493f9e0bebd2425acab6fb5:
`git merge-tree --write-tree origin/master HEAD` clean (tree 9e67ee4dbbe941b6a3285e67e697d132fc91416d).
Diff: SessionRunnerSettings.cs +9, AgentControlService.cs +11/-2, docs/docker-stack.md +4,
new tests/Antiphon.Tests/Application/RemoteExecutableLaunchTests.cs (377), plan (229, byte-identical
to origin/feat/card-task-c748deca's copy; commit 7e71febc2d touches only that file).

## Brief CHECK items

1. False skips the server check for a non-runner-bound launch: `AgentControlService.cs:527-534`
   `else if (_verifyExecutablesOnServer) EnsureSpawnable(spec.Exe); else LogDebug(...)`. V-2 green:
   the fake runner's StartAsync received `/antiphon-no-such/claude` unchanged through the real
   AgentSessionLaunchQueue, no ConflictException.
2. Runner fails closed: no S1 code; pre-existing paths. `UnixPtyArgvGuard.VerifyOrThrow` leaves the
   absent executable to the launch runtime; a runner start that throws raises the
   "Interactive launch failed" alert (`AgentSessionLaunchQueue.cs:103-115`) and a standing agent's
   exit charges `RestartBackoffFailures` (`AgentSupervisorService.cs:285-326`). No automated witness
   of a Linux runner spawning a missing executable exists in the repo (Pty/SessionRunner tests
   searched); the plan's "Not done, unverified" records it. Advisory A-1.
3. Runner-bound and default paths unchanged: the `IsRunnerBound -> Project` line and the
   `EnsureSpawnable(spec.Exe)` call are the base bytes; the default (unset) path stays under
   `else if (true)`. V-1, V-3(true), V-3(false) green.
4. Flag is configuration only: the sole read is the ctor's `IOptions<SessionRunnerSettings>`
   (bound from section `SessionRunner`, `Program.cs:150`); no DTO, agent or task field; repo grep
   shows no other reference. `IOptions` is read once per scope.
5. Witnesses can go red: M1 (inverted read) fails V-1 (no throw, adapter started) and V-2
   (ConflictException). M2 (check or skip applied on the runner-bound branch) fails V-3 (refusal, or
   StartedExe != "claude"). M3 (property default false) fails R-1. Note: the harness registers no
   IOptions for V-1, so V-1 pins the ctor literal `?? true`, not the property default; R-1 pins the
   property default, which is what production DI uses. Advisory A-2.
6. Red commit: 43b71bf67a adds the property and the four witnesses with no `if`, so V-2 fails on
   `refused.ShouldBeNull()` (unconditional EnsureSpawnable on a rooted missing path throws);
   2f45c1f482 fixes the V-3 drain-probe stub; 76a85e11aa wires the skip. Red verified by
   construction and commit order, not re-executed this round (40-minute budget).
7. OS-branch witnesses: no OS-selected production behaviour changed (rooted-path File.Exists is
   OS-neutral; the resolver's Windows extension pass is untouched). No declaration needed.
8. docs/docker-stack.md: heading `### Standing agents and specialists on the sidecar (CARD-1778)`
   at the position the plan names for S2. `ServerImagePushCredentialTests` slices the CARD-1776
   section to the next `### `; every asserted string precedes the new heading (inspected, not run).
   Advisory A-3.
9. Input waits: none added; parking disabled means no automatic release deadline (CARD-1083), unchanged.

## Checkpoints (one tool run, 20261108-115602-e77b, plan extract .antiphon/c1778-s1-review-fbed504a.md)

| Row | Filter | executed | passed | failed | skipped | build | wall |
|---|---|---|---|---|---|---|---|
| CP-1 | /*/Antiphon.Tests.Application/RemoteExecutableLaunchTests/* | 5 | 5 | 0 | 0 | ok, verified | 55.7 s |
| CP-G | Final guard batch, 8 classes (brief's five + MechanicalPcContract, TaskPlatformGuidance, C1729LayeringGuard) | 75 | 75 | 0 | 0 | reused | 135.0 s |

CP-1 identities: Default_true_refuses_a_rooted_exe_missing_on_the_server, False_hands_the_launch_to_the_runner,
Runner_bound_agents_are_unaffected(True), Runner_bound_agents_are_unaffected(False), Setting_default_is_true.
The plan's "4" is the Min floor; V-3 is parameterised, so 5 is the real count.
source=76a85e11aa6c6f5c7156073a3f7206aab255d283 state=clean buildSource=verified slot=granted;
`validate --evidence report.json --expected-source-sha 76a85e11aa...`: CHECKPOINT SOURCE VALID rows=2.
Phases: build 521.9 s (host load ~26, two other Antiphon.Tests builds in flight), CP-1 host 54.6 s,
CP-G tests 104.6 s; SLOW CLASS TestClassificationPolicyTests 104 s (untouched class). Nothing near the
30-minute Debug obligation; no timeout widened. Unlisted runs: none (the tool-build lease c1778-review-tool-build only).
Evidence: /work/worktrees/task-fbed504a/.antiphon/checkpoints/20261108-115602-e77b/report.md
check-evidence-diff c460b0d0a4..76a85e11aa: commits=4 entries=0 violations=0.

## Pending

PC-2 (M1, M2, M3) stay pending for method-scoped SourceLanding Mutation against
`/*/*/RemoteExecutableLaunchTests/<method>`. Operator steps 0.12-4.x are manual and pending.

## Advisories (cards, never a hold)

- A-1: no witness that a Linux runner spawn of a missing executable ends as an incident plus backoff
  rather than a Running seat with no process. The plan scoped it out; a SessionRunner integration
  test with `Exe=/antiphon-no-such/claude` would close it (could ride S2 or a follow-up card).
- A-2: V-1's comment says it exercises the property default; it exercises the ctor fallback.
  Register `Options.Create(new SessionRunnerSettings())` in V-1 so both defaults are pinned by a launch.
- A-3: the `deploy-server2.ps1:1 says ...` paragraph (pre-existing tail of the CARD-1776 section)
  now sits under the CARD-1778 heading; S2's doc edit should move it above the heading.

--- review evidence ---
subjectTaskId: c4eea940 (owner; full GUID not in the brief)
reviewedSourceSha: 76a85e11aa6c6f5c7156073a3f7206aab255d283
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: c460b0d0a4de591b8e662f8dbc57eeac3f26d249..76a85e11aa6c6f5c7156073a3f7206aab255d283
