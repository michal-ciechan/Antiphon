# Why missed: harness hangs in antiphon g118 and session-runner g4 (run ae84478d)

Debug task d268c50c, branch `feat/card-task-d268c50c` from `56e96eabf` (rc/2026-11-18).
The evidence is the desktop read on `feat/card-task-ed1f9715`
(`docs/investigations/2026-11-12-harness-hangs-g118-srg4-windows.md`), plus the final-result
note on `feat/card-task-25e35cdf`. This note covers why the defects reached a Full Windows
run, and what this branch changes.

## What hung

Every test in both chunks passed. In each, one descendant of the test host was still alive
60 s after the run finished. The harness (`scripts/lib/nightly-owned-process.cs`) gates
completion on the job's active-process count, so it marked the chunk `completed-but-hung`.

| Chunk | Holder | Test that left it | Mechanism |
|---|---|---|---|
| antiphon `all-group-118` | msys `sleep` (pid 82328) | `RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced` | The pre-commit hook `sleep 90` runs under Git for Windows bash, by fork plus exec. The fork stub exits about 20 ms later, so the sleeper's Windows parent pid names a dead process. The test kills its pwsh worker with `entireProcessTree: false`, which closes the worker's `OwnedChildMarker` job (no KILL_ON_JOB_CLOSE). It then cleans up with `child.Kill(entireProcessTree: true)` on git. That parent-pid walk stops at the hook's `sh`, and the sleep lives on for 90 s. |
| session-runner `all-group-4` | `Antiphon.PtyHost` (pid 118772) | `RunnerSessionGenerationTests.C502_V26_kill_and_relaunch_publish_their_own_generations` | The `StubProbe(false)` sweep sends host B a shutdown ack while its child is still alive. The test then kills only the child pid. On Windows the host's modern-ConPTY dispose drops the exit, so `Completion` never fires and the host waits forever. The `[After(Assembly)]` CARD-0206 sweep should have killed it, but its root pattern did not include `c502-v26`. |

## Why it was missed

1. **Both leaks happen only on Windows, and the loop that repairs failures is on Linux.**
   Linux bash forks and execs in the same pid, so `Kill(true)` reaches `sleep`. The Linux PTY
   dispose path completes the runner. The Linux harness has no job gate, and
   `PtyHostLeakSweep` returns at once off Windows. Every server2 run of these classes was
   clean, and only a native Full run could show either hang.
2. **No test asserted on descendants.** C24 checks the recorded git child and C502 checks
   generations. Neither checks what the test leaves running. The test code assumed
   `Kill(entireProcessTree: true)` reaches everything. The product already knew it does not:
   `LandingGit.ReleaseChildAsync` names "an msys sleep whose parent was the exited fork stub",
   and `OwnedChildMarker` exists for that case (CARD-1076). The tests did not use that
   knowledge, because the worker's job dies with the worker.
3. **The hang depends on position.** C24 hangs g118 only when it runs in the last 30 s of the
   chunk (90 s sleep minus 60 s grace). Chunk membership comes from the cost plan, and both
   recorded hangs (d88de419 and ae84478d) had that position. Elsewhere the sleep exits unseen.
   C502's host wedges only when the shutdown ack beats the child exit, and that race was clean
   in 3 of 7 g4 runs.
4. **The sweep had two sources of truth.** `TestSessionLogRoot.Create` validates against
   `KnownPrefixes`, but `RootFromHostPath` kept a hand-written regex of the first seven
   prefixes. CARD-0502 (`32bb2a52f`) and later cards added 23 prefixes to the list only. The
   one sweep test used `cpu-watchdog-tests`, which the regex still matched.
5. **Earlier evidence could not name the holder.** Before the hang capture existed, sr g4
   showed up as an 1801 s launch-watchdog timeout (8e938b6d, 231f573c, dce78ee0). The
   2026-11-10 note (`2026-11-10-why-missed-session-runner-group4-post-completion-hang.md`)
   could not name the survivor, and the d88de419 g118 capture came during a run the reboot
   killed.

## What this branch changes

All changes are test-side. No production code changed.

- `tests/Antiphon.Tests/TestHelpers/DescendantLeakScope.cs` is the leak check for a
  test-started root. It marks every descendant with an environment token, which Linux reads
  from environ. On Windows it adds a KILL_ON_JOB_CLOSE job that the root is assigned to right
  after it starts. The job reaches a member whose parent has exited.
  `AssertNoneAliveAsync(bound)` fails the test when a member survives its cleanup, after
  killing that member. `DescendantLeakScopeTests` builds the orphan shape on purpose (sh
  backgrounds `sleep`, or `cmd start /b ping`, and the root exits first). It shows that the
  check fails and kills the orphan, that `KillAll` and `Dispose` reach it, and that an
  unrelated process is never touched.
- C24: the worker runs in a scope. The `finally` calls `leaks.KillAll()` after the existing
  tree kills, and the test ends with the leak check. V28 runs the production `LandingGit`
  through a `ScopedLandingGit` (the `ConfigureProcess` and `StartProcess` seams only) and
  checks that cancellation leaves nothing running. Production's job terminate is what makes
  this pass, so it is a guard.
- `PtyHostLeakSweep.RootFromHostPath` builds its alternation from `KnownPrefixes`.
- `TestSessionTeardown` gains `CaptureHost` (pid and start, read while the host runs),
  `KillHost` (a start-checked tree kill) and `AssertHostGoneAsync` (a bounded leak check that
  kills a survivor and then fails).
- `RunnerSessionGenerationTests`: C502_V26 relaunch kills host B directly in a `finally`. The
  manifest and herdr cases, which killed only the child pid, tear down through
  `KillAndAwaitHostExitAsync`. All three end with the host leak check.
  `RunnerMultilinePromptDeliveryTests` (also in g4, with a 72 s linger and an unswept root)
  does the same.

## Evidence on Linux, and what native Windows must prove

On Linux, these went red then green: the sweep witness (`f703711c1`, red: `card0395-adoption`
unmatched); the scope witness; the host leak-check witness; and mutants M1 to M3 and M6 to M9
(see the task report). C24's wiring was proved with an uncommitted hook that also
double-forks `( sleep 90 & )`. At `47da99716` it fails with `pid=<n>/image=sleep`. At
`58e046d90` it passes, and no `sleep` is left.

Native Windows proof owed by the orchestrator:

- At `47da99716` (C24 check, no kill): C24 should be red, with the `sleep` named.
- At `13188302` (C502 check, child-only kill): C502_V26 relaunch should be red ("outlived the
  test's teardown"). The race can go either way, but the 72 s linger fails the 5 s bound too.
- At the branch tip, both classes should be green, with no `completed-but-hung` in a g118 or
  sr g4 rerun.

## Not fixed here (follow-up cards)

- **Product, sr g4 root cause:** on Windows, `PtyAgentRunner.DisposeAsync` with
  `ModernConPtyConnection` never completes `Completion`. A host given Shutdown while its child
  lives waits forever, which leaks hosts on the desktop as well. A Windows-only red test is
  needed: a host given Shutdown with a live modern-ConPTY child exits within a bound. This
  needs a production Code task, and this Linux runner cannot verify it.
- **Product:** the `OwnedProcessTree` parent-pid walk cannot see an msys exec grandchild. The
  `OwnedChildMarker` job covers it only while the owning process lives and assigned the job
  before the child spawned.
- **Latent:** `PtyHostLingerHours = 0.02` (72 s) is longer than the 60 s completion grace. Any
  test that leaves a host to linger in the last 12 s of a chunk hangs it.
- **Audit:** the same worker-death shape (`Kill(entireProcessTree: false)` while a descendant
  runs) also appears in `AgentTaskLandRecoveryTests`, `PostLandMutationDeliveryTests`,
  `WorktreeResidueRecoveryTests`, `RepositoryChildRecoveryTests` and the `*Worker` helpers.
  Each is a candidate for `DescendantLeakScope`.
