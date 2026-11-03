# CARD-1653 S2 Review round 2 (diff-only), task 2c8d00db

**Outcome: CLEAN at a5c9fdbb73433ef579cc8e144b137e99eca945ce** (code tip 6b2a631bd). Range reviewed: `93f10fbb7934bf0652d612e142143e74ab8a6bc3..a5c9fdbb73433ef579cc8e144b137e99eca945ce`. Round-1 D-1 is closed. No path writes the hello to an unproven peer under Enforce. No proof-step exception reaches the kill arm. No numbered defects.

## Verification items

**(a) D-1 closed.**
- `TryResolvePtyHostSocketDir` treats null, empty and whitespace as unset.
- An embedded NUL makes it return false.
- On Linux, `.`, `..`, relative paths, long paths and non-ASCII values all resolve without throwing. A long or non-ASCII path is bounded later by `EnsureSafe`, which counts UTF-8 bytes (dir + 1 + 32 <= 107).
- A trailing separator is trimmed. `/` stays `/`.
- Adoption compares through the Try form only and short-circuits when it returns false.
- The sweep-level `EnsureSafe` already catches all exceptions.
- At launch, `PipeNameFor` is inside the `PtyHostSocketDirUnsafeException` catch, so the launch fails with `pty_host_socket_dir_unsafe` before spawn.
- Probed with pwsh: `GetFullPath("")` and `GetFullPath("a\0b")` give `ArgumentException`. `/abs/sock/` gives `/abs/sock`.

**(b) Proof-step catch.**
- Under Enforce, both helpers map any exception to Refused or not-accepted, which leads to `Unproven`. `Unproven` throws `PtyHostPeerRefusedException`.
- That exception is an `UnauthorizedAccessException`. The retry filter (IO/Timeout/HelloEof/OCE-not-ct) does not match it, so it reaches `catch { dispose; throw; }`. The sweep arm (`when VerificationBinding is null`) then holds `pty_host_peer_unproven` with no kill.
- The hello is written only after `AcceptsPeer` returns true. When the first check was Absent, it also requires the re-check to return `Ok`.
- A pre-connect Refused throws before `Connect`.
- No other proof call is unwrapped:
  - `PeerPolicy` and `Create` only build lambdas.
  - The `pipe.SafePipeHandle` getter is evaluated outside the helper's try, but it cannot throw after a successful `Connect`.
  - `OnObserved` is a logger call.
- Cancellation: the proof steps take no token. An OCE raised inside one becomes a held refusal, not a kill.
- The sweep's own cancellation during `Connect` still reaches the generic arm. That is unchanged behaviour, it is not a proof step, and the Code report lists it as deliberately left.
- Observe with a proof fault logs and connects. Observe already writes the hello to a proven-mismatched peer by design, and a fault is weaker evidence than a mismatch. This is consistent with Observe being a rollout mode. It is not a fail-open that matters, provided Enforce is the security posture.

**(c) Legacy and Off unchanged.**
- Legacy `PipeNameFor` never reads the setting, and launch skips `EnsureSafe` for the legacy name. Adoption and the sweep checks are Owned-only.
- Under Off the policy is null. Both new helpers sit behind `policy?.CheckSocketFile is {}` and `policy is not null`, so the Off connect is byte-identical.
- Windows returns the bare name.
- The trailing-separator change affects Owned names only. G-63 (V-17 x6) is green in CP-5.

## Runs (server2, all build-slot leased, native output, UseAppHost=false)

**Run L via the checkpoint tool** (run `20261103-041824-f272`): GREEN. `validate --expected-source-sha a5c9fdbb7...` printed `CHECKPOINT SOURCE VALID source=a5c9fdbb73433ef579cc8e144b137e99eca945ce rows=6`.

| Row | Result | Notes |
|---|---|---|
| CP-4 | 28/28 | |
| CP-5 | 29/29 | |
| CP-6 | 17/17 | |
| CP-17 | 3/3 | |
| CP-18 | 1/1 | |
| CP-20 | 4/4 | |
| Totals | 2 builds, wall 1m44s | sourceState=clean, buildSource=verified |

Earlier attempts:
- **Run `20261103-041341-2313`:** load average about 19.7 and build-slot queue full. CP-4 was 26/28. `Peer_uid_must_match_the_runner` hit its 10 s per-test timeout, and `Socket_appearing_during_the_connect_wait_is_checked_before_hello` timed out in `PeerClosedAsync(5 s)`. Then the executor died before phase=done, with a receipt-read refusal and no report.json.
- **Run `20261103-041757-fa6a`:** refused `pre-existing-build-source` because the dead run left its `bin-c1653-s2l` output behind. I removed that output with a literal-path `rm -r`.
- Both tests are unchanged in this diff, and so is their code path (the plain `AcceptsPeer` false / `Refused` path). Both were green in the rerun. I count them as load flakes (A-2).

**Mutants**, one build lease and one run lease each, over the 14 witness executions. Each was restored with `git checkout -- <path>`.

| Mutant | Result | Red tests |
|---|---|---|
| M1 (empty-as-unset removed) | killed, 7/14 red | (i) x3, (ii) empty and whitespace, (iii) empty and whitespace |
| M2 (throwing legacy compare) | killed, 1/14 red | (ii) nul |
| M3 (proof catch rethrows) | killed, 3/14 red | peer-reader, liveness, file-probe |

**SessionRunner.Tests Unit lane** (`[Category=Unit]`): 409 executed, 389 passed, 20 failed. The 20 are the inherited set from round 1, measured at base 7dc9e1168: `CodexWindowsLaunchPolicyTests` x19 and `PhoneHomeConnectionServiceTests/Websocket_connect_that_hangs...` x1.

**Not run:**
- PtyHost.Tests Unit lane: the build hit ENOSPC because the server2 volume is 100% full (44 MB free of 705 GB, host-wide, not caused by this task). Code measured it 37/37 at 6b2a631bd.
- Antiphon.Tests Unit lane: about 3 h, over budget, and no server code changed.
- Windows Run W (CP-7, CP-8, CP-19): owed.

## Gates

- `check-evidence-diff -BaseRef 7dc9e1168 -HeadRef HEAD`: commits=12, entries=2, violations=0. Over the round-2 range: commits=4, violations=0.
- `git merge-tree --write-tree origin/master HEAD` (master `c4e7aa06e`): clean, exit 0.
- `rc/2026-11-01` (`10a826b9d`):
  - A whole-branch merge-tree conflicts on `client/src/api/agentTasks.ts`, which is master drift.
  - A chained per-commit pick of the non-plan commits also conflicts, at the first S2 code commit a4d3e1be5. The conflicts are modify/delete on `Card1653FreshLaunchIdentityTests.cs` and `PtyHostStrangerWorld.cs`, plus `PtyHostLeakSweep.cs` and `test-execution-policy.json`.
  - Cause: S1 (c5556c544 and the rest) is on master but not on rc.
  - So an rc port needs S1 first. This is not a defect of this subject.

## Advisories

- **A-1.** `TryResolvePtyHostSocketDir` is documented "never throws", but its filter does not catch `IOException`.
  - Verified with pwsh: a relative `PtyHostSocketDir`, or a relative default via `SessionLogPath`, with the process cwd deleted, gives `GetFullPath` -> `FileNotFoundException`.
  - That exception escapes adoption into the kill arm, and escapes launch without the documented code.
  - It cannot be reached from the shipped Linux runner (Docker `WORKDIR /app`, which is never deleted). Windows does not run this path.
  - Cheap fix: add `IOException` to the filter.
- **A-2.** `PeerClosedAsync(5 s)` and the 10 s per-test timeout in `Card1653SocketDirTests` went red under server2 load. They were green alone. Budget them toward 2x the loaded p95.
- **A-3.** A proof-step fault under Enforce logs only the fixed code, not the exception type. A persistent probe fault would hold every adopted row with no cause in the log. Consider logging the type.

PCs: all still pending for SourceLanding Mutation (PC-33 to PC-81, the re-check candidate, M1-M3, and the unverified launch-catch-narrowing mutant).
