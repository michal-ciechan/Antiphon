# Review 8f467d8d CP-7 / CP-9 load-sensitive reds (Debug 79a8764d, 2026-11-23)

Subject: readiness Final review at bb5bdb87, report `/work/review-evidence/8f467d8d/20261123-171911-7679/`.
Both failures ran in the server2 dind container `bf7f80348d1b` (the TRX computerName).

## CP-7: `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(Grok, False)`

Cause (test fixture). `TestOwnedPtyHost.ObserveProcess` reads `/proc/<pid>/stat` with `File.ReadAllText`.
When the task is reaped between the open and the read, the kernel answers `ESRCH` on the read; the
runtime raises `IOException("No such process", HResult 3)`, not `FileNotFoundException`. `Observe`
mapped it to `Unknown: IOException`, and `WaitNativeAsync` throws on the first non-Alive observation,
so the wait phase failed at 47 ms for host pid 12256 even though the final probe recorded
`observed-exit` for all six captured processes.

Proof: `DirectSessionRunnerClientDisposalTests.Observation_of_a_process_reaped_between_stat_open_and_read_is_dead`
opens the real stat file, kills and reaps the child, then reads. Red at a3fd52cd5
(`IOException: No such process : '/proc/10341/stat'`), green after the fix. The `other-io-fault` case keeps an
unexplained read failure an unknown observation.

Fix: ESRCH on the stat read becomes the same fact as ENOENT at open (`FileNotFoundException`), so
`ObserveProcess`, `CaptureDescendants` and `CaptureProcess` reuse their existing vanished-process paths.
`OwnedPtyWitness.Exited` gets the same ESRCH classification. No timeout, retry or assertion changed.

## CP-9: `Termination_kills_reparented_member_before_rescue(False, True)`: not a test timing issue

Cause (production census policy, not a containment race). `LinuxOwnedGitMembers.ScanAsync` reads
`/proc/<pid>/environ` for every live process born after the root. A process of another uid answers
`EACCES`, which is an observation failure, so the whole scan is `Unknown` for as long as that process lives.
In the review window, Testcontainers Postgres containers (`antiphon-test-pg:7c6b9d35...`, started
17:43:08 and 17:43:38 in the same dind daemon) were forking uid-999 backends for new connections. The case
started at 17:43:51.97 and took 6.19 s: the full 6 s `TerminateAndJoinAsync` allowance.

Deterministic reproduction (scratch probe, not committed): the same topology with one
`docker run --rm --entrypoint /bin/sleep postgres:16 12` started after the root gave
`joined=False exited=True members=Unknown/0 reason=Deadline signals=1 dur=6001` with 560
`UnauthorizedAccessException` environ reads across 138 passes. The survivor was killed by the first
pass and every later pass found zero members. Without a foreign post-root process, 20/20 iterations
under 24 busy loops plus 4 `/bin/true` churners joined in 20-145 ms (one transient `Unknown` pass).

Verdict: the fail-closed rule is the documented census design ("unreadable ... scans return Unknown").
Making the test pass would mean hiding foreign processes from the census under test, which is the
paper-over the brief forbids. `LinuxOwnedGitProcess` is not yet selected by production callers, but
once it is, any unreadable process born during a git invocation in the same pid namespace (a Postgres
backend, a container process) makes `Joined` unattainable until it exits. That is a design decision
for the containment owner (for example, treat an unreadable process whose uid differs from ours and that
has no root chain as a non-member, or bound the claim to same-uid processes). The test now prints
the whole cleanup verdict when `Joined` is false.
