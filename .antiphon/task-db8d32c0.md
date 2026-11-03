# CARD-1653 S2 Review round 1 (task db8d32c0)

**Outcome: one defect (D-1). The security invariant holds.** Under Enforce, the hello is never written to an unproven peer. Run L is green at `93f10fbb7934bf0652d612e142143e74ab8a6bc3`, and all 20 of my security mutants were killed. Flags-off behaviour is byte-identical.

D-1 is separate from that invariant. With `Owned` and an **empty** `PtyHostSocketDir`, the adoption sweep kills honest live hosts (proven by probe). It also refuses honest launches with an unexplained `ArgumentException`.

- **Subject:** `feat/card-task-7b966444` at `93f10fbb7934bf0652d612e142143e74ab8a6bc3` (code tip `b4c6a631c`, base master `7dc9e1168`).
- **Code owner:** `7b966444-e3a1-49d4-b5db-efd81a3ab74b`.
- **Server2 only.** Nothing ran on the desktop.

## Defects

### D-1: Owned plus an empty `PtyHostSocketDir` kills honest live hosts at adoption

- **Where.**
  - `src/Antiphon.SessionRunner/SessionRunnerSettings.cs`: `ResolvedPtyHostSocketDir => Path.GetFullPath(PtyHostSocketDir ?? …)`. Only `null` falls back to the default; `""` is passed to `GetFullPath`.
  - It is read in three places:
    - `AdoptCoreAsync`, in the `pty_host_legacy_socket_path` comparison, before `ConnectAsync`.
    - `PtyHostRendezvous.PipeNameFor` (Owned).
    - The sweep's `EnsureSafe` diagnostic.
- **Failure (probe, scratch merge of origin/master + S2, never committed).**
  - Configuration: `SessionRunner:PtyHostSocketDir = ""` (in-memory configuration; any empty source such as an empty env var or compose `${X:-}` binds the same way). The probe confirmed the bound value is `""`, not null.
  - Then `ResolvedPtyHostSocketDir` throws `ArgumentException: The value cannot be an empty string`.
  - Probe sequence:
    1. Runtime A (Legacy) launches a real `/bin/sh` host.
    2. Runtime B (`Owned`, `PeerCheck=Off`, `PtyHostSocketDir=""`) runs `AdoptOrphanedHostsAsync`.
    3. Result: `adopted=0 hostAlive=False status=Exited`. The log reads `Live pty-host … could not be adopted; treating as dead | System.ArgumentException: The value cannot be an empty string. (Parameter 'path')`.
  - The exception is thrown inside `AdoptCoreAsync` before any connect. It reaches the sweep's generic `catch (Exception)`, which calls `KillPidBestEffort(manifest.HostPid)` and deletes the manifest. That is the G1 kill S1 removed.
  - A fresh Owned launch fails with the same `ArgumentException`, not `pty_host_socket_dir_unsafe`.
- **Why it matters.**
  - The rollout doc tells operators to turn on `SessionRunner__PtyHostSocketDirMode=Owned`. One empty override in an env file or compose template then kills every live session on the next runner restart.
  - `docs/testing-and-build.md#os-branch-witnesses` requires distinct witnesses for the absent, empty and value states of optional configuration (the CARD-1525 lesson). V-18b covers only the absent state.
  - Off and Legacy are unaffected: every read is behind an `Owned` guard.
- **Fix (one Code slice).**
  1. Treat a null, empty or whitespace `PtyHostSocketDir` as unset. Normalise once, including the trailing separator (see A-3).
  2. Make the legacy-path comparison in `AdoptCoreAsync` unable to throw.
  3. Add witnesses:
     - V-18b empty and whitespace cases.
     - An adoption witness that a live host survives Owned with an empty `PtyHostSocketDir`.
  4. Optionally, map any exception from the S2 proof step (file probe, peer reader, liveness) to `PtyHostPeerRefusedException`, so that an unknown result is held, never killed (A-4).

## Verification (all on server2, Linux; counts are executions)

| What | Result |
|---|---|
| Run L via the checkpoint tool, run `20261103-031710-ed5f` at `93f10fbb7` | GREEN, 6/6 rows, validated `CHECKPOINT SOURCE VALID source=93f10fbb7934bf0652d612e142143e74ab8a6bc3 rows=6` |
| CP-4 `Card1653SocketDirTests` | 19/19 (design Min 18, plus the added re-check witness) |
| CP-5 `Card1653PeerCheckTests` | 24/24 |
| CP-6 (Card1636 + Card1653RefusalRetainsHost + Card1653FreshLaunchIdentity) | 17/17 |
| CP-17, CP-18, CP-20 | 3/3, 1/1, 4/4 |
| SessionRunner.Tests Unit lane at the tip | 409 executed, 389 passed, **20 failed**, 0 skipped |
| SessionRunner.Tests Unit lane at base `7dc9e1168` | 409 executed, 389 passed, **the same 20 failed**, so they are inherited |
| └ CodexWindowsLaunchPolicyTests alone at base | 19/19 red |
| └ `PhoneHomeConnectionServiceTests/Websocket_connect_that_hangs…` | Red in both lanes (`WebSocketException`, not OCE). The whole class is 17/17 green alone at the tip, so it is a co-run flake. |
| PtyHost.Tests Unit lane (`PtyHost.Client` changed) | 37/37 |
| Existing `ConnectAsync` callers, Off path: HostCustodyTests + Card1619LinuxHostExitTests | 8 passed, 15 skipped (platform-gated) |
| Existing `ConnectAsync` callers, Off path: RunnerCustodyCrashTests + Card1619RunnerExitTests | 10 passed, 2 skipped (platform-gated) |
| Scratch merge with origin/master `91aa16368` (scratch commit `c87ddbe4c`): S2 classes + CP-6 classes, including CARD-1672's new method | 61/61 |
| `check-evidence-diff` 7dc9e1168..93f10fbb7 | `commits=8 entries=1 violations=0` |
| `merge-tree` onto origin/master `91aa16368` | Clean (master moved: CARD-1665/1668/1670/1672; only CARD-1672 touches S2's classes, and the run above is green) |
| `merge-tree` onto rc/2026-11-01 `10a826b9d` | Conflicts in `client/src/api/agentTasks.ts` (master-only drift, not S2). An rc port needs CARD-1636 + S1 first, as the plan says. |

**Not run, so this round is Interim:**
- **Antiphon.Tests Unit lane** (and Observability/E2E). This is about 3 h on server2, over the 120-minute budget. S2 changes no server code; `DirectSessionRunnerClientDisposalTests` calls `ConnectAsync` with the old three arguments, and the new parameter is optional.
- **Windows Run W** (owed by the plan): CP-7 (Card1653PeerCheckTests, min 24), CP-8 (Card1636AdoptionIdentityTests, 6) and CP-19 (Card1653RefusalRetainsHostTests, five operands, 7) = R-5.

## Mutation (my 20 security mutants; one build-slot lease per build and per run; each restored with `git checkout -- <path>`)

Five builds. In each batch the mutants touch different files, and no batch-mate can make another mutant's witness red. All 20 were **killed** at the expected assertion.

| # | Mutant | Witness red |
|---|---|---|
| 1 | Post-connect re-check removed (`Cli`) | `Socket_appearing_during_the_connect_wait…` returned 1 |
| 2 | SO_PEERCRED uid clause dropped (PC-50) | `Peer_uid_must_match_the_runner` returned 1 |
| 3 | Directory ownership check dropped (PC-36) | `Unsafe_socket_directory_is_refused(foreign-owner)` did not throw |
| 4 | Fallback to `/tmp` on an unsafe directory (PC-39) | `Unsafe_directory_fails_the_launch_before_spawn`: TimeoutException, not the code |
| 5 | getsockopt 17 → 18 (PC-46) | `Peer_reader_returns_the_listener_process`: null |
| 6 | Peer check before hello skipped (`AcceptPeer` line deleted) | `Fresh_launch_refuses_a_foreign_peer_before_hello` (also every adoption refusal) |
| 7 | `AT_SYMLINK_NOFOLLOW` → 0 (PC-35) | `Unsafe_socket_directory_is_refused(symlink)` |
| 8 | Peer catch arm deleted, so kill-on-refusal is restored (PC-56) | `Unsafe_socket_file_is_refused_before_connect(regular-file)`: stand-in killed |
| 9 | pid compare dropped (start and uid kept) | `Enforce_refuses_a_replayed_identity…` returned 1 |
| 10 | Pre-connect file refusal removed | `Unsafe_socket_file_…` regular-file/symlink/foreign-owner |
| 11 | Ancestor walk dropped (PC-37) | `…(unsafe-ancestor)`, `…(symlinked-unsafe-ancestor)` |
| 12 | Sweep returns when its `EnsureSafe` diagnostic fails (PC-80) | `Legacy_real_host_is_adopted_after_owned_enforce` (red as a 10 s test timeout in teardown; see A-7) |
| 13 | Fresh launch passes a null policy (PC-53) | `Fresh_launch_refuses…` |
| 14 | `CheckSocketFile` ignores the uid (PC-44) | `…(foreign-owner)` returned 1 |
| 15 | Start-time clause dropped (PC-49) | `Peer_start_must_match_the_manifest_host` returned 1 |
| 16 | `PipeOptionsFor(null)` adds `CurrentUserOnly` (PC-78, G-63 pin) | `Connect_policy_by_mode_and_platform` ×6 |
| 17 | Legacy returns the owned path on Linux (PC-41) | `Rendezvous_name…(Legacy, False, False)` |
| 18 | Default `PtyHostPeerCheck = Enforce` (PC-67) | `Settings_default_to_legacy_and_off` |
| 19 | `PtyHostPeerRefusedException : IOException` (PC-52) | `…(regular-file)` took 5.13 s against the < 1 s bound |
| 20 | Length bound `>` → `>=` (PC-70) | `Socket_path_length_is_bounded(107, False)` |

No security mutant survived. All 49 design PCs stay **pending** for method-scoped SourceLanding Mutation; this sweep discharges none of them.

## Security reading (answers to the brief)

- **(a) Invariant.** There are only two connect sites, launch and adoption. Custody uses the same adoption site; Herdr is a different rendezvous. The order is: pre-connect file check, connect, post-connect re-check (only when the file was absent before connect), `SO_PEERCRED` / `GetNamedPipeServerProcessId` on the connected handle, then the hello.
  - Fail-closed paths: an Enforce refusal throws the never-retried `PtyHostPeerRefusedException`; null credentials are refused; a probe or reader exception is either retried as an `IOException` or rethrown. No path writes the hello.
  - TOCTOU: the peer check runs on the connected fd, so `SO_PEERCRED` is authoritative and the file checks are defence in depth.
  - Linux pid identity holds: the intermediary reports the child that calls `listen()` itself. On Windows, `dwProcessId` from `CreateProcess` is the pipe server.
  - Same-uid scope is stated honestly in the overall plan (§3.4) and the S2 plan (S3 does not defend against it either).
- **(b) Directory checks.**
  - The probe is correct: `statx` offsets (mask 0, uid 20, mode 28, size 256), mask 0x1|0x2|0x8, `AT_SYMLINK_NOFOLLOW` 0x100 and `geteuid` returning `uint`.
  - The mode check is exact on 0777 (setuid/setgid/sticky bits are not compared).
  - Creation uses `mkdir` with mode 0700. The umask can only narrow that, and a wider result is then tightened and re-read, so there is no window where the mode is wider.
  - A foreign owner is refused, not repaired. The length bound counts UTF-8 bytes plus the NUL.
  - S2 creates only the directory and deletes nothing.
- **(c) Held row.** It is never killed and its manifest is kept. The S1 vanish sweep releases both reasons (witnessed, and mutant 8). Log lines carry the fixed code and the session id only.
- **(d) Off/Legacy are byte-identical.**
  - Pipe name: `PtyHostRendezvous` Legacy equals `PtyHostProtocol.PipeNameFor` on both platforms, and the launcher's `pipeName ?? PipeNameFor` argv is unchanged.
  - Client: `PipeOptionsFor(null) == Asynchronous`, and a null policy skips every new branch.
  - Sweep: the new catch arm is unreachable under Off.
  - Pinned by V-17 and V-18a/b, and mutants 16-18 are killed.
- **(e) Windows (reading only).**
  - `CurrentUserOnly` raises `UnauthorizedAccessException` inside `Connect`, which is mapped to a refusal.
  - The P/Invoke signature `bool GetNamedPipeServerProcessId(SafePipeHandle, out uint)` is correct, and the handle is held under `DangerousAddRef`.
  - Every Linux API is OS-guarded. There is no conditional compilation, so the Linux build proves the Windows compile.
  - `[NativeLinuxOnly]` has its `platform: windows` row (CP-17 green).

## Advisories (no Review rerun needed)

- **A-1.** For a symlinked ancestor, only the resolved target directory is checked, not the target's own ancestors. This is defence in depth only.
- **A-2.** `EnsureSafe` runs the `chmod` (which follows symlinks) before the ancestor walk. Checking ancestors first would be cleaner.
- **A-3.** A configured `PtyHostSocketDir` with a trailing `/` yields `<dir>//<id>`. `EnsureSafe` counts the trimmed path, so the bound is one byte short at the boundary. Fold this into the D-1 normalisation.
- **A-4.** An exception, as opposed to a refusal, from the proof step reaches the sweep's generic kill arm. Examples: a missing `statx` entry point on an old libc, or a probe that throws. No hello is written, but the host is killed instead of held. This is the same class as D-1.
- **A-5.** The start clause uses `SystemProcessLivenessProbe` semantics ("started no later than start + 2 min"; `catch` → alive). It is a pid-reuse refinement, not an identity proof, and matters only for a same-uid attacker.
- **A-6.** `docs/session-runtime-invariants.md` S2 section does not say that a same-uid process is out of scope (the plans do).
- **A-7.** For Mutation: the PC-80 and Cred mutants make `Legacy_real_host…` red through the 10 s test timeout in teardown, not at the assertion. Record the red text.
- **A-8.** A custody peer refusal goes to the generic arm, which registers Exited and deletes the manifest without a kill. This is today's custody adoption-failure behaviour (I-S2-c).

## Cleanup

- Every native `bin/` and `obj/` under `src`, `tests` and `tools` was removed with `git clean -fdX -- src tests tools`. The preview listed only bin/obj.
- Scratch worktrees `base` and `merged` were removed with `git worktree remove --force` on literal paths. The scratch merge commit `c87ddbe4c` is unreferenced and was not pushed.
- Source is clean at `93f10fbb7`. Checkpoint evidence stays in the ignored `.antiphon/checkpoints/20261103-031710-ed5f/`.
