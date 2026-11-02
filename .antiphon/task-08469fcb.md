# CARD-1636: non-custody adoption verifies the hello's session and host instance

Branch `feat/card-task-08469fcb`. It is stacked on `2b1e7494a9b100982e4e1f95a1be622fb8e918f9`, the tip of `feat/card-task-7d7761ce` (CARD-1621, not merged yet). Master only. Nothing was landed.

## Commits

| SHA | What |
|---|---|
| `08e2172eb99663050685d00eeb8fe4114fb33b65` | feat: `PtyHostManifest.HostInstanceId` (nullable). `HostSession` writes `_hostInstanceId` into both manifests, the launch-pending one and the running one. This is the same value its helloAck carries. |
| `c1c9187a806aab4b4f1865785cdbd5f9fbd08eb0` | test (red): `tests/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests.cs`, plus the `c1636-adopt` leak-sweep prefix. |
| `540bc589f69be329de97fd13e55699ce1c3b6be1` | fix: `SessionRunnerRuntime.RunnerSession.AdoptCoreAsync` / `HelloNamesManifestHost`. |

## The fix

After `PtyHostClient.ConnectAsync`, the non-custody path (`VerificationBinding is null`) now requires all of the following before it sends anything past hello:

- `Hello.SessionId == _sessionId == manifest.SessionId`.
- If the manifest recorded a `HostInstanceId`, `Hello.HostInstanceId` must equal it. A null instance id in the hello is refused.

On a mismatch the runner:

1. Logs `pty_host_identity_mismatch: adoption refused for session {SessionId}`.
2. Throws `InvalidOperationException("pty_host_identity_mismatch")`.

The message is fixed and never contains the ids the peer claimed. The sweep's existing adoption-failure path then runs unchanged. It disposes the client without sending Status or Attach, registers the session as Exited, deletes the manifest, and best-effort kills `manifest.HostPid`. The custody branch is untouched.

Decision taken under stated defaults: a legacy manifest has no `HostInstanceId` because the host was launched before this change. It is matched on the session id alone. Refusing it would kill every non-custody host that is still live across the upgrade, because adoption failure kills `HostPid`. This legacy window closes once those hosts have drained.

## Witnesses (TRX, server2 Linux, UseAppHost=false, TEMP=/tmp, one build-slot lease per build and per run)

| Run | Result |
|---|---|
| Red at `c1c9187a8` (unfixed) | total 6, failed 4, passed 2. The 4 refusal cases, `Hello_naming_another_session_is_not_adopted`, `Hello_from_another_host_instance_is_not_adopted(False/True)` and `Manifest_without_a_host_instance_adopts_only_its_own_session`, each had `AdoptAsync` returning 1 where 0 was expected. The matching case and the real-host case passed. |
| Green at `540bc589f` | total 6, passed 6, failed 0, skipped 0. |
| M1: the new comparison removed (`&& false`) | 4 failed: the same 4 refusal cases. 2 passed. |
| M2 + M3, batched because they touch different files and methods. M2 compares the session id only. M3 has the host stop writing `HostInstanceId` to the manifest. | 3 failed: `Hello_from_another_host_instance(False)` and `(True)` from M2, and `Real_host_records_its_hello_instance_and_is_adopted` from M3 (`manifest.HostInstanceId should not be null`). The session case, the matching case and the legacy case passed. |
| Restored, rebuilt at HEAD | Mutant source was never committed. `git status` is clean. |

The witness cases, by the brief's lettering:

- **(a)** A fake host answers helloAck with a different session. Not adopted. Nothing is sent past hello. The session is Exited. The log names the session and contains neither the stranger's id in D format nor in N format.
- **(b)** A different host instance, or a missing one. Refused.
- **(c)** A matching session and host are adopted (Running, Status and Attach sent), against the fake host and against a real pty-host after a runner restart.
- **(d)** Custody path. See the neighbour table below.

## Neighbour classes at HEAD `540bc589f` (one lease each)

| Selection | Result |
|---|---|
| `PtyHostAdoption*`, `GrokRulesAdoption*`, `TranscriptAdoptionSafety*`, `Card1636*` | total 60, passed 52, failed 6, skipped 2. All 6 failures are cmd.exe Windows fixtures that are red on Linux, and all 6 are inherited. The same 6 methods ran at base `2b1e7494a` (detached scratch worktree, own build): 6/6 failed. They are `Dead_host_is_registered_as_exited_ProcessVanished`, `Running_session_survives_runner_restart_with_buffer_and_input_intact`, `KillAll_kills_every_live_session_and_their_hosts`, `Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code`, `ToDto_reports_unbound_while_locating_exact_after_bind_and_sidecar_after_readopt` and `Post_start_stranger_transcript_reports_AdoptionRefused_before_stale_C3_candidates`. |
| `Card1619LinuxStopIntentTests` alone | 18/18 passed |
| `HerdrAdoptionSweepTests` alone | 22/22 passed |
| `RunnerCustody*`, `Card1619RunnerExit*` | total 47, passed 22, failed 0, skipped 25. The skipped rows are Windows-only custody rows. `Exit_while_detached_survives_adoption`, `Old_host_adoption_keeps_signal_unknown`, `Stale_generation_does_not_replace_exit_evidence` and the C1614 custody rows passed. |
| PtyHost.Tests `HostSessionPipe*`, `Card1619LinuxHostExit*`, `HostCustody*`, `Framing*` | total 37, passed 15, failed 0, skipped 22 (Windows ConPTY / job). |

Pending and not run here:

- The Windows lane for `Card1636AdoptionIdentityTests`, which is portable: a ping.exe stand-in and cmd.exe for the real host.
- The Windows-only custody adoption rows, `Live_host_adoption_keeps_original_binding_and_rejects_changed_replay` and `Actual_runner_crash_adopts_same_host_and_http_read_does_not_seal`.
- The whole Unit lane. The brief asked for narrow adoption classes inside a 90-minute budget, so it was not run.

## Other checks

| Check | Result |
|---|---|
| `scripts/audit-land-verification-graph.ps1 -Check` | exit 0, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69 phase=pass2 approved`. No `-Update`. The `bin-land-audit` directories were removed. |
| `scripts/check-evidence-diff.ps1` from `2b1e7494a` to HEAD | 3 commits, 0 entries, 0 violations. This was before the report commit. The caller summary carries the re-run. |
| `git merge-tree --write-tree origin/master HEAD` (master `43ae7371d`) | Clean, tree `dcf1df064`. |
| `git merge-tree --write-tree origin/rc/2026-11-01 HEAD` (rc `10a826b9d`) | Clean, tree `d7759bef8`. |

## Permissions today (read-only)

### Linux

- **Socket path.** `PtyHostProtocol.PipeNameFor` returns `/tmp/antiphon-pty-{id:N}`. The host binds it with `new NamedPipeServerStream(path, InOut, 1, Byte, Asynchronous)`. `PtyHostServer.cs` adds `CurrentUserOnly` on Windows only; it was dropped on Linux because of the CARD-0490 V-7 Docker SO_PEERCRED hang. Nothing in the code sets a mode or umask, or creates a directory for the socket.
- **Directory.** The socket's directory is `/tmp` itself. In this task container it is `drwxrwxrwt root root` (1777, sticky). It is not 0700 and not owned by the runner uid.
- **Socket file.** Probed in my container with pwsh on .NET 9.0.10, umask `0022`, uid 1654: the socket file is created `srwxr-xr-x` (0755), owned by the creator. The mode is the same with or without `CurrentUserOnly`. connect(2) needs write permission on the socket, so another uid cannot connect to the real host's socket.
- **What another uid can still do.** It can create `/tmp/antiphon-pty-<id>` while the path is absent: before the host binds, or in the relisten gap after the host unlinks its socket. The runner would then connect to that socket. The sticky bit stops another uid from unlinking the runner's own socket. Any same-uid process can do anything.
- **Adoption checks.** None on ownership. `PtyHostClient.ConnectAsync` uses `NamedPipeClientStream(".", name, InOut, Asynchronous)` with no `CurrentUserOnly`, no lstat or owner check, and no SO_PEERCRED check of the peer pid against `manifest.HostPid`.
- **Manifest directory.** `logs/pty-hosts/manifests` is created with default `Directory.CreateDirectory` (0755 under umask 022). It is readable by other uids, so the session ids and pipe names in it are readable too.
- I did not inspect the live server2 runner.

### Windows (code reading only, not verified on Windows)

- The host's server uses `PipeOptions.CurrentUserOnly`. In .NET this gives the pipe a DACL for the creating user only, so other users cannot open it.
- The runner's client does not pass `CurrentUserOnly`, so it never checks that the server's owner is the current user.
- The host uses `maxNumberOfServerInstances: 1`. A squatter who creates `\\.\pipe\antiphon-pty-<id>` first makes the host's create fail, and the runner would connect to the squatter.

### Gaps outside this card

- An identity refusal still kills the live `manifest.HostPid`, through the existing adoption-failure path. A stranger who hijacks the pipe can therefore get the real host killed (denial of service).
- The fresh-launch path (`StartAsync`, line ~3444) checks `Hello.SessionId` only under custody. A non-custody launch that connects to a pre-bound stranger is not checked.

## Proposed follow-up card (not implemented): pty-host peer ownership and hello proof

1. **Linux socket directory.** Move sockets into a per-runner directory, for example `<state>/pty-sock`, created 0700 with `UnixCreateMode`. Before use, lstat it and require: owner == euid, `mode & 077 == 0`, and not a symlink. Mind the 108-byte `sun_path` limit.
2. **Linux socket check.** Before connecting, lstat the socket: it must be a socket, owned by euid, and not a symlink. After connecting, compare `getsockopt(SO_PEERCRED)` on the client side against euid and `manifest.HostPid`. Do this on the client, not through .NET's server-side `CurrentUserOnly` accept, to avoid the CARD-0490 hang.
3. **Windows.** Use `CurrentUserOnly` on the runner's client so it checks the server owner.
4. **Per-session secret.** The runner generates 32 random bytes per launch and sends them only inside `LaunchMessage` over the pipe. The runner persists them in a 0600 file in its 0700 state directory, never in the world-readable manifest. Hello carries a nonce. The new helloAck feature `helloProofV1` returns `HMAC(secret, nonce‖sessionId‖hostInstanceId)`. A host without the feature is adopted only under the CARD-1636 identity rule and is logged once.
5. **Invariants.**
   - **I-1.** Nothing is adopted or launched unless the hello names the session and host instance. This card covers adoption. Extend it to the non-custody launch path.
   - **I-2.** The runner never connects to a socket or pipe that its own identity does not own, inside a directory only that identity can write.
   - **I-3.** The secret never appears in argv, env, logs, the manifest, or any file readable by other users.
   - **I-4.** A refusal names only the session and a fixed token.
   - **I-5.** An identity refusal never kills a pid whose identity was not proven by the peer. Decide whether a hijacked pipe should retain the manifest instead of killing the host.
