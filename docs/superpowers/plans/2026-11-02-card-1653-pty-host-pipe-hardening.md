# CARD-1653 plan: pty-host pipe hardening (refusal without kill, owned socket, peer check, hello proof)

Plan task 5a6278c8, 2026-11-02, server2 (Linux), base master `a00962fd6a055c513fa567c3a930fb875660dbef`.
This was a read-only investigation. Nothing was built, and no repository test was run. One
scratch pwsh probe (.NET 9.0.10, uid `app`, umask 022) checked socket modes, unlink-before-bind and
the Linux `CurrentUserOnly` client; its results are cited where they are used.
CARD-1636 (Code 08469fcb, Review 4521968e) is on master as `775d8ddfa` / `4c1a3049e`. It is **not**
on `rc/2026-11-01`. Every slice here sits on CARD-1636 and is master-first. An rc port needs CARD-1636
ported first.

## Summary

CARD-1636 made non-custody adoption check that the hello names the manifest's session and host
instance. That leaves three classes of local (same-host) exposure.

1. **Destructive refusal (G1).** An identity refusal still runs the generic adoption-failure path. That
   path best-effort kills `manifest.HostPid`, registers the session Exited and deletes the manifest. A
   process that only squats the pipe name can therefore get the real host killed.
2. **Unchecked fresh launch (G2).** The non-custody `StartAsync` sends `LaunchMessage` without looking
   at the hello. `LaunchMessage.Env` carries the session's secrets, so a squatter that answers first
   receives them.
3. **No peer proof.** The Linux socket lives directly in `/tmp`. Nothing checks the socket's owner or the
   peer's credentials, and on Windows the client does not use `CurrentUserOnly`. The CARD-1636
   identity is also weak: `HostInstanceId` is stored in a manifest that other uids can read (§2, GT-6),
   so a stranger can replay it.

Three slices, in ship order:

| Slice | What | Protocol change | Flag | Effort |
|---|---|---|---|---|
| **S1** | An identity refusal never kills or forgets. It retains the manifest and registers a pending row, which surfaces as an incident. The fresh launch checks the hello before sending Launch (G2). The vanish sweep releases the pending row once its host pid is dead. | none | none (D-1) | S: about half a day of Code; runner only |
| **S2** | A runner-owned 0700 socket directory, checked before use. The socket file's owner is checked before connecting. After connecting, the kernel-reported peer uid/pid is compared with the launched or manifest host pid: `SO_PEERCRED` on Linux, `CurrentUserOnly` plus `GetNamedPipeServerProcessId` on Windows. Every post-connect adoption failure with an unproven peer becomes a no-kill pending row. Existing `/tmp` sockets are migrated. | none (host binds `--pipe` as given) | `PtyHostSocketDir` Legacy/Owned, `PtyHostPeerCheck` Off/Observe/Enforce, both default off | M: about 1.5 to 2 days, plus a Windows lane |
| **S3** | A per-session key, sent only inside `LaunchMessage` and kept out of the manifest. The host proves it in the helloAck with an HMAC over a runner nonce. Protocol v2 stays compatible with v1 hosts. | yes (append-only) | `PtyHostHelloProof` Off/Issue/Require, default Off; Require needs `PtyHostPeerCheck=Enforce` | L: about 2 to 3 days |

**Recommendation (H-1): ship S1 now and S2 next. Hold S3 for the operator.** Against other uids, S2
already gives kernel-attested proof on both platforms. S3 adds defence in depth: it binds launch to
adoption across pid reuse and covers platforms or namespaces where peer credentials are unavailable.
It does **not** stop a same-uid process, and the agent child runs as the runner's uid. The key has to
persist on disk for adoption after a runner restart, and a same-uid process can read it (§3.4). S1 has
no dependency on the S2/S3 answer.

## 1. Ground truth: what the card assumes vs what the code does

| # | Card / brief assumes | Code at `a00962fd6` (and probe) | Consequence for the plan |
|---|---|---|---|
| GT-1 | The Linux socket is `/tmp/antiphon-pty-<id>`, created 0755 | `PtyHostProtocol.PipeNameFor` (`PtyHostMessages.cs:152`) returns `/tmp/antiphon-pty-{id:N}` on non-Windows. Probe: the socket is `srwxr-xr-x`, owned by the creator. On server2 the `/tmp` is the container's `runner-tmp` volume (`docker-compose.server2-runner.yml:110`). | Confirmed. connect(2) needs write permission, so another uid cannot connect to the real socket. |
| GT-2 | Another uid can bind the path while it is absent | .NET's server **unlinks an existing path before bind**. Probe: a regular file at the path was replaced by the socket (same uid). In sticky `/tmp`, the host cannot unlink another uid's file. `new NamedPipeServerStream` sits **outside** the try in `PtyHostServer.RunAsync` (`:31-37`), so a squat makes the real host's bind throw and the host exits. This is code reading. A cross-uid run was not possible here (one uid). | The squat is worse than the card says: it can also end the real host's listening, either before launch or in the relisten gap. S2's owned directory removes the precondition. S1/S2 do not change the host. |
| GT-3 | Adoption does no owner or peer check | `PtyHostClient.ConnectAsync` (`PtyHostClient.cs:53`) builds `NamedPipeClientStream(".", name, InOut, Asynchronous)` with no `CurrentUserOnly`, no lstat and no peer credentials. | Confirmed. S2 owns this. |
| GT-4 | CurrentUserOnly was dropped on Linux for the CARD-0490 V-7 SO_PEERCRED hang | The comment at `PtyHostServer.cs:25-29` says so. However, `docs/testing-and-build.md:951` attributes the V-7 block to CARD-0594's launch deadlock. Probe: a **client-side** `CurrentUserOnly` connect works on Linux in 12 ms (not Docker). | S2 does not re-enable server-side `CurrentUserOnly`, which authenticates the client and is not what this card needs. It checks the peer on the client with an explicit `getsockopt`, so the uncertain history does not matter. |
| GT-5 | Windows: the host pipe is CurrentUserOnly, the client is not | `PtyHostServer.cs:31-32` adds `CurrentUserOnly` on Windows only. The client has none. The host uses `maxNumberOfServerInstances: 1`. | Confirmed by code reading only. S2 adds `CurrentUserOnly` to the client and a server-pid check. |
| GT-6 | (not in card) The CARD-1636 host-instance identity is a proof | `HostInstanceId` is written to the manifest (`HostSession.cs:161,220`). The manifest directory and files use default creation (`PtyHostManifest.SaveAtomic`): 0755 directory, 0644 file under umask 022. Any uid can read the instance id and replay it in a forged helloAck. | CARD-1636 stops **accidental** confusion, not a deliberate stranger. S1 treats the refusal as unproven. S2's kernel peer check is the real proof. |
| GT-7 | G1: a refusal kills the real host's pid | `AdoptOrphanedHostsAsync` catch (`SessionRunnerRuntime.cs:2120-2133`): when `WorkspaceUseGate` is null, `KillPidBestEffort(manifest.HostPid, …)`, then `CreateAdoptedExited` and `TryDeleteFile(file)`. `WorkspaceUseGate` is set only through `CompletedCardWorkspace`, which production never sets (grep: no assignment outside tests). | G1 is live in production. The `WorkspaceUseGate` branch (`CreateWorkspaceConflict`, Pending `workspace_ownership_conflict`) is the pattern S1 reuses. |
| GT-8 | G2: the fresh launch checks the session id only under custody | `RunnerSession.StartAsync` (`:3442-3471`) checks `Hello.SessionId` only inside `if (VerificationBinding is { } binding)`. Non-custody goes straight to `LaunchAsync` with `request.Env`. | Confirmed. A session-id check is cheap but weak, because the id is in the pipe name and in the host's argv. S1 adds it before Launch. S2 adds the proof (peer pid == the pid our intermediary reported). |
| GT-9 | "register a pending conflict with an attention item, as the WorkspaceUseGate branch already does" | `CreateWorkspaceConflict` gives a row with Status `Starting`, `Pending=workspace_ownership_conflict`, no client and the manifest retained. The server's `SessionReconciliationService.RaiseHerdrPendingIncidentsAsync` (pass 1b) raises an `AgentIncident` plus an alert for **any** non-empty `Pending` after `HerdrPendingAlertMinutes` (default 5). Its wording is Herdr-specific. | S1 needs no server change. The incident carries `Pending=pty_host_identity_unproven`. The Herdr wording is an existing defect shared with the workspace conflict (follow-up F-2). |
| GT-10 | (not in card) A pending pty row resolves itself | `MarkVanishedIfDead` returns early unless the status is `Running` (`:4463`). `KillAsync` on a row with no client returns at once (`:4391`). A pending pty row therefore stays until a runner restart. | S1 adds a vanish-sweep release for its own pending rows when the manifest host is dead (D-5). |
| GT-11 | A peer-credential primitive would be new | `HerdrPeerIdentity` (`src/Antiphon.SessionRunner/HerdrPeerIdentity.cs`) already reads `SO_PEERCRED` (`getsockopt(fd, 1, 17)`, `ucred{pid,uid,gid}`) and Windows `GetNamedPipeServerProcessId`. It is internal to SessionRunner and takes a `Socket`. `CheckpointNativeIo` already uses `statx` and `geteuid`. | S2 copies the small native readers into `Antiphon.PtyHost.Client` (D-7) instead of refactoring the Herdr code. |
| GT-12 | (not in card) The host could take a secret in its environment | `LaunchMessage.Env` already travels over the pipe because it holds secrets (`PtyHostMessages.cs:42-46`). The manifest deliberately never stores env (`PtyHostManifest.cs:6-8`). | S3 reuses that channel (D-12). |
| GT-13 | Collision with in-flight work on `SessionRunnerRuntime.cs` / `PtyHostClient.cs` | Census of 658 origin branches active since 2026-10-29 with a merge-base on or after 2026-10-29 (rc-based branches excluded): every branch with commits touching `SessionRunnerRuntime.cs`, `PtyHostClient.cs`, `PtyHostServer.cs`, `PtyHostMessages.cs`, `PtyHostLauncher.cs`, `HostSession.cs` or `PtyHostManifest.cs` is already on master by subject (CARD-1636 `4c1a3049e`, CARD-1621 `3a8d5426d`/`1fbeb7e2e`, CARD-1624 `f5283ad08`, CARD-1545's `PtyHostClient` hunks `b136a6fbc`/`1186580e2`). The board search for `pty-host` shows no InProgress card on these files except this one. | There is no live collision at this base. Code re-runs the census at dispatch (§5). |

## 2. Invariants

- **I-1 (identity before effect).** The runner sends nothing past hello (no Launch, Status or Attach) to
  a peer that has not named this session. Once S2 is enabled, it also sends nothing to a peer whose
  kernel-reported pid is not the expected host. On a fresh launch, `LaunchMessage.Env` reaches only a
  peer that passed I-1.
- **I-2 (refusal is not a kill).** An identity or peer refusal never stops, signals or deletes the
  manifest of a process whose identity was not proven by the peer itself. The runner may stop the
  manifest host only when the peer is proven to **be** that host: S2's peer pid equals
  `manifest.HostPid` with a matching start time, and the hello or proof then fails. A host the runner
  spawned itself in this `StartAsync` is proven by spawn (its pid came from our own intermediary and
  its start time was captured).
- **I-3 (refusal stays visible).** A refused adoption keeps the manifest and registers a pending row,
  which the server turns into an incident. It is released only by evidence: the host pid is dead
  (vanish sweep), a later sweep proves the host, or an operator stops the host after verifying it.
  There is no timer release (§9).
- **I-4 (owned rendezvous, S2).** New sockets are created only inside a directory that the runner's euid
  owns, with mode exactly 0700, that is not a symlink, and whose path fits `sun_path`. If the
  directory is unsafe, the runner fails closed with `pty_host_socket_dir_unsafe` and never falls back
  to `/tmp` (D-6).
- **I-5 (secret custody, S3).** The session key never appears in argv, env, logs, the manifest, an HTTP
  DTO, or any file readable by another uid. It crosses the pipe only inside `LaunchMessage`, and only
  after I-1 holds with S2 enforced.
- **I-6 (no downgrade, S3).** The runner decides whether proof is required from **its own** key store,
  never from the peer's advertised version or features.
- **I-7 (fixed refusal text).** A refusal logs and surfaces only the session id and a fixed token
  (`pty_host_identity_mismatch`, `pty_host_peer_mismatch`, `pty_host_proof_mismatch`). It never logs
  ids, pids or proofs that the peer claimed. This extends CARD-1636's rule.
- **I-8 (legacy hosts keep working).** A host launched before an upgrade keeps being adopted, under the
  strongest rule its manifest supports: the CARD-1636 identity, plus the S2 peer check on its legacy
  path. It is never refused merely for being old, until it drains.

## 3. Design

### 3.1 S1: refusal without kill, fresh-launch identity, release

**Typed refusal.** `AdoptCoreAsync` throws a new `PtyHostIdentityRefusedException(string code)` (code
`pty_host_identity_mismatch`) instead of `InvalidOperationException("pty_host_identity_mismatch")`.
The log line is unchanged, so CARD-1636's text assertion still holds.

**Adoption sweep** (`AdoptOrphanedHostsAsync`, catch at `:2120`). A new branch comes before the
existing `WorkspaceUseGate` and kill branches:

```text
catch (PtyHostIdentityRefusedException refusal):
    dispose the session (detach only, no Shutdown)
    register RunnerSession.CreatePendingConflict(manifest, settings, events, logger, refusal.Code)
    keep the manifest file; do not call KillPidBestEffort; do not CreateAdoptedExited
    hostAttempt.Complete(0, "refused"); continue
```

`CreateWorkspaceConflict` becomes `CreatePendingConflict(…, string pendingReason)`. The two existing
callers pass `workspace_ownership_conflict` unchanged. The new reason is `pty_host_identity_unproven`.
That is a reason token, distinct from the log token. Every **other** adoption exception keeps today's
path (kill, Exited, delete) in S1 (D-3). Without S2 the runner cannot tell a squatter's late failure
from a hung real host, and CARD-1632/1634 guard that path.

**Fresh launch (G2)** (`RunnerSession.StartAsync`, after `ConnectAsync`, before the custody block). For
`VerificationBinding is null`, require all of:

- `Hello.SessionId == _sessionId`;
- `Hello.HostInstanceId is not null` (every host since CARD-0604 sends it: `HostSession.GetHelloAck`);
- `Hello.Status == WaitingForLaunch`.

On a mismatch, log `pty_host_identity_mismatch: launch refused for session {SessionId}` and throw the
typed exception. The existing catch runs `TearDownFailedLaunch`, which kills `_hostPid`. That pid is
the host **we** spawned, so I-2 allows it. `LaunchAsync` is never reached and the env is never sent.

**Release (D-5).** `MarkVanishedIfDead` gains one branch. If `_pendingReason` is
`pty_host_identity_unproven` (later also `pty_host_peer_unproven`) and `_hostPid` is not alive at the
recorded `HostStartTimeUtc`, then: set the status to Exited with `ProcessVanished`, publish
`SessionExited`, and delete the retained manifest. The manifest path and start time are kept on the
pending row for this. Herdr pending rows are untouched (`IsPendingHerdr` stays as it is). A live real
host behind a squatted path stays pending until a runner restart re-runs adoption.

**Failure modes, S1.**

- F-S1-1: a real host answers with a mismatched identity. No code path produces this: the
  `Real_host_records_its_hello_instance_and_is_adopted` witness covers the normal path. If it ever
  happened, the host is left running and visible instead of being killed. This is the intended trade.
- F-S1-2: the pending row hides a host that the operator wants gone. The incident names it. The
  operator stops the pid after verifying it (the same procedure as a retained custody host), and the
  vanish sweep then releases the row.
- F-S1-3: G2 refuses a real host on a fresh launch. That would mean `HostSession` stopped sending its
  instance or status, and the existing launch tests go red. The launch fails, and the dispatcher's
  existing launch-failure handling applies.

### 3.2 S2: owned socket directory and peer proof

**Socket directory** (Linux; new `src/Antiphon.PtyHost.Client/PtyHostSocketDirectory.cs`).

- Path: `SessionRunner:PtyHostSocketDir` if set, else `Path.GetFullPath(Path.Combine(ResolvedPtyHostDir, "sock"))`.
  On server2 that is `/tmp/antiphon-pty-hosts/sock`, so a socket path is 24+5+1+32 = 62 bytes.
- `EnsureSafe()`, before every launch and once per adoption sweep:
  - create the directory with `Directory.CreateDirectory(path, UnixFileMode.UserRead|UserWrite|UserExecute)`;
  - then `statx(AT_SYMLINK_NOFOLLOW)` it (the pattern of `CheckpointNativeIo`, arch-independent layout).
    Require: a directory, not a symlink, `uid == geteuid()`, `mode & 0777 == 0700`;
  - an owned directory with a wider mode is tightened with `chmod 0700` and re-checked. Any other
    failure is `pty_host_socket_dir_unsafe`;
  - require `UTF8(path) + 1 + 32 <= 107` bytes, else `pty_host_socket_path_too_long`.
- New launches pass `pipeName: <dir>/<id:N>` to `LaunchDetachedAsync`, which already supports it
  (`BuildHostArgs` emits `--pipe`). The connect uses that same stored `_pipeName`, never `PipeNameFor`.
  The host writes `PipeName` into its manifest as today. **No host change.**
- Windows: no directory. The pipe name stays `antiphon-pty-<id:N>`.

**Socket-file check before connect** (Linux). `statx(AT_SYMLINK_NOFOLLOW)` on the socket path. Require
`S_IFSOCK`, `uid == geteuid()`, not a symlink. If the path is absent, keep retrying inside the existing
connect budget: that is today's "server not ready". If the path exists with the wrong type or owner,
refuse with `pty_host_peer_mismatch` and do not retry (an `UnauthorizedAccessException`-class failure
that bypasses the retry catch). This check alone is a TOCTOU heuristic. The proof is the next step.

**Peer check after connect.**

- Linux: `getsockopt(SOL_SOCKET=1, SO_PEERCRED=17)` on the client fd
  (`pipe.SafePipeHandle.DangerousGetHandle()` under `DangerousAddRef`; on Unix this handle is the
  socket fd). For a connected client this returns the credentials the **listener** had at `listen(2)`,
  i.e. the host.
- Windows: `NamedPipeClientStream(..., PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)`, so .NET
  verifies the server's owner SID is the current user, then `GetNamedPipeServerProcessId`.
- Expected pid:
  - **fresh launch:** `_hostPid`, from our intermediary;
  - **adoption:** `manifest.HostPid`, plus `Process.GetProcessById(pid).StartTime == manifest.HostStartTimeUtc`
    (`SessionGeneration.Equal`);
  - Linux also requires `uid == geteuid()`.
- `PtyHostClient.ConnectAsync` gains an optional `Func<PeerCredentials, bool>? acceptPeer`. It runs
  after connect and **before** the hello write. A refused peer throws `PtyHostPeerRefusedException`,
  which is not retried. A peer whose credentials cannot be read (`getsockopt` fails, pid 0 from another
  pid namespace) is unproven, not accepted.
- Modes:
  - `PtyHostPeerCheck=Off`: today's behaviour.
  - `Observe`: run every check, log `pty_host_peer_mismatch (observe)` with the session only, then
    continue. The canary uses this.
  - `Enforce`: refuse.

**Kill rule widened (I-2).** With `PtyHostPeerCheck=Enforce`, `AdoptCoreAsync` records
`_peerProven = true` only after the peer check passes. In the sweep catch:

- a peer refusal becomes a pending row (`pty_host_peer_unproven`);
- an identity refusal after a **proven** peer keeps S1's pending row. The real host named the wrong
  session, so leave it for the operator (D-3);
- any other exception **after** connect with `_peerProven == false` becomes a pending row
  (`pty_host_peer_unproven`), not a kill;
- exceptions before any peer answered (connect timeout) and exceptions with a proven peer keep today's
  kill path. That kill is lawful under I-2.

**Migration of existing `/tmp` sockets (I-8).** A host launched before the upgrade keeps its
`/tmp/antiphon-pty-<id>` path in its manifest, and adoption already connects to `manifest.PipeName`.

- With S2 on, a manifest path outside the owned directory is adopted through the same socket-file and
  peer checks. A socket owned by the runner's uid, with a peer pid equal to `manifest.HostPid`, is
  proof even in sticky `/tmp`.
- It is logged once per session (`pty_host_legacy_socket_path`).
- Such hosts keep working until their child exits and the runner acks Shutdown, or until the 24 h
  linger TTL (`PtyHostLingerHours`). There is no forced restart.
- A Docker container replacement drops `/tmp` anyway (`docs/logs.md:84`).
- New launches go to the owned directory as soon as `PtyHostSocketDir=Owned`. Turning it back to
  Legacy affects only new launches.
- `AuditCleanupService` leaves `sock/` alone. Sockets are unlinked by their host
  (`PtyHostServer` CARD-0594 dispose). A stale socket file in `sock/` is harmless: the host's bind
  unlinks it (same uid, GT-2), and the client's peer check refuses a dead listener.

**Failure modes, S2.**

- F-S2-1: `SO_PEERCRED` pid differs from `manifest.HostPid` for a real host. This is impossible within
  one pid namespace: the host itself calls `listen`, and the intermediary has already exited. Observe
  mode measures it before Enforce.
- F-S2-2: the owned directory is missing after `/tmp` is recreated. `EnsureSafe` recreates it. Legacy
  manifests in a wiped `/tmp` were already dead.
- F-S2-3: a long `PtyHostDir` on a native Linux dev box. Fail closed with `pty_host_socket_path_too_long`
  and set `PtyHostSocketDir`.
- F-S2-4: Windows `CurrentUserOnly` client against a host running elevated or as another user. That is
  a refusal, which is the point. The desktop host and runner run as one user.

### 3.3 S3: per-session key and hello proof

- **Key.** 32 bytes from `RandomNumberGenerator`, generated per `StartAsync`.
- **Persist before send.** The runner writes the key to `<ResolvedPtyHostDir>/keys/<id:N>.key`:
  - Linux: the directory is 0700 and checked like §3.2; the file is created with `FileMode.CreateNew` and
    `UnixCreateMode = UserRead|UserWrite`.
  - Windows: a file DACL with a single ACE for the current user SID, inheritance disabled.

  It is written **before** `LaunchAsync`, so a runner crash between the two leaves a key for a host that
  times out at `PtyHostLaunchTimeoutSec`. The key is deleted wherever the runner deletes the manifest
  (terminal ack, Exited registration, sweep release).
- **Send.** `LaunchMessage` gains `byte[]? HelloKey` (append-only). It is sent only when
  `PtyHostHelloProof` is Issue or Require **and** the S2 peer check passed in Enforce (I-5). The host
  keeps it in memory. It is never written to the manifest, the custody journal or the log, and it is
  never echoed in a reply.
- **Proof.**
  - `HelloMessage` gains `byte[]? Nonce`, 32 random bytes per connect.
  - `HelloAckMessage` gains `byte[]? HelloProof = HMAC-SHA256(key, "antiphon-pty-hello-v1" ‖ nonce ‖ sessionId(16 bytes) ‖ hostInstanceId(16 bytes))`.
    It is present only when the host holds a key and the client sent a nonce.
  - The feature is `helloProofV1`. `PtyHostProtocol.Version` goes to 2. The host already continues for
    any client version (`PtyHostServer.cs:120-121`), and the version is informational: the runner
    decides on the feature and its own key, never on the version.
- **Verify.**
  - When the runner's key store holds a key for the session, adoption requires a valid proof:
    `CryptographicOperations.FixedTimeEquals`.
  - `Require` refuses with `pty_host_proof_mismatch`, which becomes a pending row with no kill unless S2
    proved the peer. `Issue` logs and continues.
  - No key on file (a legacy host, or a key lost with runner state) falls back to the S2 rule (I-8) and
    is logged once.
- **Compatibility.**
  - System.Text.Json ignores unknown properties. A v1 host ignores `Nonce`/`HelloKey` and answers
    without `HelloProof`. That is a legacy host, accepted only where the runner has no key.
  - A v1 runner sends no nonce and gets no proof.
  - The `tests/Antiphon.PtyHost.Tests/FrozenV1` reader must parse a v2 helloAck and launch (witness V-24).
- **Channel decision (D-12).**
  - **Chosen:** inside `LaunchMessage` over the pipe, after peer proof.
  - **Rejected:**
    - environment: it is inherited by the intermediary and the host. The host builds the agent child's
      env from `launch.Env` but other children may inherit. `/proc/<pid>/environ` keeps the exec-time
      block for the process lifetime even after `unsetenv`, readable by the same uid. It also appears in
      crash dumps;
    - argv: `/proc/<pid>/cmdline` is world-readable;
    - stdin through the intermediary: needs new plumbing in both `PosixProcessSpawner` and the Win32
      detached spawner, and the detached Win32 path has no stdin;
    - runner memory only: lost on a runner restart, which is exactly when adoption needs it.

**Failure modes, S3.**

- F-S3-1: the key file is unreadable after a restore. The runner falls back to the S2 rule and logs it.
- F-S3-2: a host crash loses its in-memory key. Nothing to adopt anyway.
- F-S3-3: replay. The proof binds the per-connect nonce, so a captured helloAck fails a new nonce
  (witness V-21).

### 3.4 What S3 does not buy (input to H-1)

- The agent child, and every tool it runs, executes as the runner's uid. Any same-uid process can:
  connect to any host socket while the runner is disconnected; read the 0600 key file; and, with
  `ptrace_scope` 0, read host memory.
- The host does not authenticate its client at all today. Mutual authentication (the host requiring a
  runner proof over a host nonce before accepting commands) is the only S3 extension that changes what a
  same-uid process can do. Even that does not hold against a process that can read the key file. It is
  question H-2, not in S3 by default.
- Isolating agents from the runner's uid is the real control. That is out of this card's scope.

## 4. Slices

Code runs each slice's `### Checkpoints` rows as a closed list. TestDesign refines method names,
counts and timeouts from measurements. Every slice: red witness committed first, then the fix, then
green. Linux-only classes carry `[NativeLinuxOnly("CARD-1653 …")]` and a `test-execution-policy.json`
`platform: windows` exclusion row with a resealed digest (repository recipe, CARD-1613). Windows-only
classes are skipped on Linux by their own platform guard and run on the windows-native lane.

### S1: refusal without kill, fresh-launch identity, release

**Files.**

- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: the sweep catch (`:2120`), `AdoptCoreAsync` throw
  (`:3565-3569`), `StartAsync` hello check (`:3442`), `CreateWorkspaceConflict` → `CreatePendingConflict`
  (`:3758`), `MarkVanishedIfDead` (`:4456`).
- New `src/Antiphon.SessionRunner/PtyHostIdentityRefusedException.cs`.
- `tests/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests.cs`: **changed assertions.**
  `AssertRefused` now expects Status `Starting`, Pending `pty_host_identity_unproven`, the manifest
  retained and the stand-in alive, instead of `Exited`. This is a deliberate change of the CARD-1636
  contract, recorded here.
- New `tests/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests.cs` (portable) and
  `Card1653FreshLaunchIdentityTests.cs` (linux-native).

**Witnesses (red first).**

| V | Class | Lane | What | Red on base because |
|---|---|---|---|---|
| V-1 | Card1653RefusalRetainsHostTests | portable | A fake pipe server answers with another session for a manifest whose `HostPid` is a stand-in (`sleep`/`ping.exe`, the CARD-1636 `StrangerWorld` shape). After the sweep: stand-in alive, manifest file present, `Get(id).Pending == "pty_host_identity_unproven"`, Status `Starting`, nothing past hello sent. | the stand-in is killed and the manifest deleted |
| V-2 | same | portable | The same with a wrong host instance (the replayed-instance case uses the right instance plus another session). The log contains the fixed token and session id, and neither the stranger's session nor its instance in D/N format. | as V-1 |
| V-3 | same | portable | After V-1, kill the stand-in and run `SweepVanishedSessions`. The row becomes Exited `ProcessVanished`, `SessionExited` is published once, and the manifest is deleted. | no row exists to release (the base registered Exited immediately), so this asserts the S1 release itself; red on S1-without-D-5 (mutant M-5) |
| V-4 | same | portable | Control: a matching fake hello is adopted Running (no regression to the CARD-1636 happy path). | green on base (control) |
| V-5 | Card1636AdoptionIdentityTests | portable | The existing 6 cases with the changed refusal assertions. | the refusal cases expect `Starting`, base gives `Exited` |
| V-6 | Card1653FreshLaunchIdentityTests | linux-native | `SessionRunnerSettings.PtyHostSourceDir` points to a fake host directory whose `Antiphon.PtyHost` is a `/bin/sh` script: it starts `sleep 60`, prints that pid and exits 0, which satisfies the intermediary contract. A test pipe server pre-bound at `PipeNameFor(id)` answers hello with another session. `StartAsync` fails with the fixed token, the server's frame log has **no** `LaunchMessage`, and the spawned `sleep` is torn down. | the base sends Launch with env to the stranger |
| V-7 | same | linux-native | The stranger answers with the right session but a null `HostInstanceId` or `Status=Running`. Refused, no Launch. | as V-6 |

TestDesign confirms V-6's fake host source satisfies `ShadowCopyStore.HasRuntimePayload` (host
executable plus deps.json). If it does not, it uses a runtime-internal launcher seam instead.

**Mutants (Code proves each red, then restores).**

- M-1: restore `KillPidBestEffort` on the identity refusal (V-1 red: the stand-in dies).
- M-2: delete the manifest on refusal (V-1 red).
- M-3: drop the G2 check (V-6 red).
- M-4: move the G2 check after `LaunchAsync` (V-6 red: Launch observed).
- M-5: drop the D-5 release branch (V-3 red).
- M-6: log the stranger's session id (V-2 red).

**Flag.** None (D-1). **Rollout.** An ordinary master land. Then an rc port once CARD-1636 is on rc.
**Effort.** S.

### S2: owned socket directory and peer proof

**Files.**

- New `src/Antiphon.PtyHost.Client/PtyHostSocketDirectory.cs`: `EnsureSafe`, `CheckSocketFile`, the
  `statx`/`geteuid`/`chmod` natives, and an injectable `IUnixFileProbe` so a test can present
  "owned by another uid" without root.
- New `src/Antiphon.PtyHost.Client/PtyHostPeerCredentials.cs`: Linux `SO_PEERCRED` on the pipe fd,
  Windows `GetNamedPipeServerProcessId`, and an AsyncLocal override seam on the `HerdrPeerIdentity`
  pattern for fake-host tests.
- `src/Antiphon.PtyHost.Client/PtyHostClient.cs`: the `acceptPeer` hook in `ConnectAsync`, a
  non-retried `PtyHostPeerRefusedException`, `CurrentUserOnly` on Windows.
- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: `_pipeName` chosen at `StartAsync`, the
  fresh/adoption expected pid, `_peerProven`, the widened catch, the legacy-path log.
- `src/Antiphon.SessionRunner/SessionRunnerSettings.cs`: `PtyHostSocketDir` (string?),
  `PtyHostSocketDirMode` (Legacy|Owned, default Legacy), `PtyHostPeerCheck` (Off|Observe|Enforce,
  default Off).
- `tests/test-execution-policy.json`: rows for the linux-native classes.
- New tests: `Card1653SocketDirTests` (linux-native), `Card1653PeerCredTests` (linux-native),
  `Card1653WindowsPeerTests` (windows-native).
- Docs: `docs/session-runtime-invariants.md` (pty-host rendezvous and peer proof) and
  `docs/docker-stack.md` (`sock/` under `/tmp/antiphon-pty-hosts` is runner state).

**Witnesses.**

| V | Class | Lane | What |
|---|---|---|---|
| V-8 | Card1653SocketDirTests | linux-native | `EnsureSafe` creates 0700. A pre-existing owned 0755 directory is tightened to 0700. A symlinked directory is refused with `pty_host_socket_dir_unsafe`. A probe-reported foreign uid is refused. A path over 107 bytes is refused with `pty_host_socket_path_too_long`. |
| V-9 | same | linux-native | `CheckSocketFile`: a regular file or a symlink at the socket path is refused before connect, with no retry and no hello written. |
| V-10 | Card1653PeerCredTests | linux-native | Spike: a real `NamedPipeServerStream` in the test process. After connect, the client's `SO_PEERCRED` gives `Environment.ProcessId` and the test uid. |
| V-11 | same | linux-native | Under Enforce, the CARD-1636 stranger shape, even with the **correct** session and instance replayed from the manifest, is refused: the peer pid (the test process) is not the stand-in. The stand-in survives, the row is pending `pty_host_peer_unproven`, and nothing past the client's first frame is sent. Under Off the same case is adopted, which is the CARD-1636 gap. |
| V-12 | same | linux-native | Under Observe, the same is adopted and the log contains `pty_host_peer_mismatch (observe)` with the session only. |
| V-13 | same | linux-native | A real host launched with `PtyHostSocketDirMode=Owned` and Enforce: the socket is in `sock/`, the launch succeeds, and after a runtime restart it is adopted (peer pid == `manifest.HostPid`). |
| V-14 | same | linux-native | A legacy `/tmp/antiphon-pty-<id>` real host, launched with Legacy, is adopted after a restart with Owned+Enforce through the peer check, and logs `pty_host_legacy_socket_path` once. |
| V-15 | same | linux-native | Under Enforce, a post-hello failure from an unproven peer (the fake answers the right identity then drops Status) gives a pending row and no kill. The same failure from the real proven host keeps today's path. |
| V-16 | Card1653WindowsPeerTests | windows-native | A fake pipe server in the test process gives a server pid that is not the manifest stand-in, so it is refused under Enforce and the stand-in survives. A real host is adopted. |
| V-17 | same | windows-native | The client stream is created with `CurrentUserOnly` (inspected through the factory seam). This is a weak witness: a foreign-owner pipe needs a second account, and stays a code-review item (R-3). |
| V-18 | Card1636AdoptionIdentityTests | linux-native and windows-native | Unchanged with the flags at their defaults. The Windows run also covers the still-pending CARD-1636 Windows lane. |

**Mutants.**

- M-7: skip the mode check (V-8 red).
- M-8: compare the uid only (V-11 red: same uid, wrong pid).
- M-9: accept unreadable credentials (V-11 with the override returning null goes red).
- M-10: fall back to `/tmp` on an unsafe directory (V-8 red).
- M-11: skip the check for legacy paths (V-14's forged-legacy variant goes red).
- M-12: kill on a post-connect failure from an unproven peer (V-15 red).
- M-13: Windows, drop the pid compare (V-16 red).

**Flags and rollout.**

1. Land with both flags off.
2. server2 canary: `PtyHostSocketDirMode=Owned`, `PtyHostPeerCheck=Observe` for 48 h. Grep the runner log
   for `pty_host_peer_mismatch (observe)`. The expectation is zero.
3. Enforce on server2.
4. The desktop (Windows) follows after the windows-native rows pass. On Windows only `PtyHostPeerCheck`
   applies.

Turning a flag off is a runner restart. No host change is involved, so mixed host versions are a non-issue.

**Effort.** M.

### S3: per-session key and hello proof (held for H-1)

**Files.**

- `src/Antiphon.PtyHost.Protocol/PtyHostMessages.cs`: `HelloMessage.Nonce`, `HelloAckMessage.HelloProof`,
  `LaunchMessage.HelloKey`, `Version = 2`, a new `PtyHostHelloProof` helper (HMAC and constant-time
  verify) in the Protocol project.
- `src/Antiphon.PtyHost/HostSession.cs`: the in-memory key, the proof in `GetHelloAck(hostVersion, nonce)`.
- `src/Antiphon.PtyHost/PtyHostServer.cs`: pass the nonce.
- New `src/Antiphon.SessionRunner/PtyHostKeyStore.cs`: write, read and delete, with modes and DACL.
- `SessionRunnerRuntime.cs`: generate, persist, send and verify; key deletion beside every manifest delete.
- `PtyHostClient.cs`: the nonce in hello.
- `SessionRunnerSettings.cs`: `PtyHostHelloProof` (Off|Issue|Require, default Off), with a startup
  refusal of Require unless `PtyHostPeerCheck=Enforce`.
- New tests: `tests/Antiphon.PtyHost.Tests/Card1653ProofHostTests.cs` (portable),
  `tests/Antiphon.SessionRunner.Tests/Card1653ProofAdoptionTests.cs` (portable) and
  `Card1653SecretStoreTests.cs` (portable, with platform-specific assertions, run on both lanes).

**Witnesses.**

| V | Class | Lane | What |
|---|---|---|---|
| V-19 | Card1653ProofHostTests | portable | A host given a key answers hello+nonce with the expected HMAC, computed independently in the test, and advertises `helloProofV1`. |
| V-20 | same | portable | A host without a key, or a hello without a nonce, gives no proof and no feature. |
| V-21 | same | portable | Two connects with different nonces give different proofs, so a replayed ack fails verification. |
| V-22 | Card1653ProofAdoptionTests | portable | A real host launched with Issue and restarted under Require is adopted. |
| V-23 | same | portable | Downgrade: a fake host with the correct session and instance and no proof (it claims v1), for a session whose key is on file, is refused under Require with `pty_host_proof_mismatch`. The row goes pending with no kill. The log has no proof bytes. |
| V-24 | Card1653ProofHostTests | portable | The `FrozenV1` reader parses a v2 `helloAck` and `launch`. A v1 hello, with no nonce, is served. |
| V-25 | Card1653SecretStoreTests | portable and windows-native | The key file is 0600 in a 0700 directory (Linux) or has a single-SID DACL with inheritance off (Windows). The key bytes are absent from the manifest, the runner and host logs, the host's `/proc/<pid>/cmdline` and `/proc/<pid>/environ` (Linux only) and the session DTO. The key is deleted on terminal ack. |
| V-26 | Card1653ProofAdoptionTests | portable | A legacy session with no key on file is adopted under Require through the S2 rule, and logs once. |

**Mutants.**

- M-14: the proof omits the nonce (V-21 red).
- M-15: accept a missing proof when a key exists (V-23 red).
- M-16: save the key into the manifest (V-25 red).
- M-17: create the file 0644 (V-25 red on Linux).
- M-18: send the key under Issue without Enforce (startup-refusal witness, part of V-22's setup checks).

Non-constant-time compare is review-only.

**Flags and rollout.** Off, then Issue on the server2 canary (keys written, proofs verified and logged),
then Require. Hosts launched under Off have no key and keep the S2 rule until they drain (I-8). Turning
Require off is a runner restart. Keys left behind are deleted with their manifests.

**Effort.** L.

## 5. Collision map (in-flight work)

| File | S1 | S2 | S3 | In-flight at `a00962fd6` (census GT-13) |
|---|---|---|---|---|
| `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs` | yes | yes | yes | none unlanded. CARD-1636/1621/1624 are on master. |
| `src/Antiphon.PtyHost.Client/PtyHostClient.cs` | no | yes | yes (nonce) | none unlanded. CARD-1545's connect-retry hunks are on master. |
| `src/Antiphon.PtyHost.Protocol/PtyHostMessages.cs` | no | no | yes | none |
| `src/Antiphon.PtyHost/HostSession.cs`, `PtyHostServer.cs` | no | no | yes | none |
| `src/Antiphon.SessionRunner/SessionRunnerSettings.cs` | no | yes | yes | not censused; a re-census is required |
| `tests/test-execution-policy.json` | yes (V-6) | yes | no | shared hot file: CARD-0824 S1, CARD-1619 and others add rows. Expect an add/add hunk conflict at land. Resolve by adopting the peer's bytes and resealing (memory: parallel-slices add/add). |

S2 and S3 both edit `SessionRunnerRuntime.cs` and `PtyHostClient.cs`, so they must be serial: S3 after
S2 lands. S1 and S2 touch the same sweep catch, so S2 starts after S1 lands. Code re-runs the census at
dispatch:

```sh
git for-each-ref --sort=-committerdate refs/remotes/origin   # active branches; then:
git log $(git merge-base origin/master <b>)..<b> -- <files>
```

A non-empty result on an unlanded branch defers the slice.

## 6. Decisions (D-n, stated defaults this plan proceeds under)

- **D-1. S1 ships without a flag.**
  - Why: it removes an automatic kill and adds a refusal that no real host triggers (V-4/V-5 controls,
    `Real_host_…` witness).
  - Rejected: a default-off flag. It would leave the G1 denial of service in production and never be
    exercised. The operator may still ask for one.
- **D-2. The refusal reuses the workspace-conflict pending row,** generalized to a reason parameter
  (`pty_host_identity_unproven`), with no server change.
  - Rejected: a new runner Status. It would ripple through the DTO, reconciliation and client.
  - Rejected: a dedicated server incident kind. That is a server build and Antiphon.Tests rows for wording
    only (F-2).
- **D-3. S1 keeps today's kill for non-identity adoption failures.**
  - Why: without peer proof, a squatter's late failure is indistinguishable from a hung real host, and
    CARD-1632/1634 depend on that path.
  - S2 narrows it to proven peers (§3.2).
  - An identity refusal after a **proven** peer still stays pending: the real host named another
    session, which is an anomaly for the operator.
- **D-4. The G2 check runs before `LaunchAsync`** and checks the session, a non-null instance and
  `WaitingForLaunch`. The refusal tears down the host we spawned, which is proven by spawn.
  - Rejected: a check after Launch. The env would already be disclosed.
- **D-5. The vanish sweep releases a pty pending-identity row when its manifest host is dead,** and
  deletes the manifest.
  - Rejected: a periodic re-adoption retry. That is new machinery. A runner restart already retries.
- **D-6. The socket directory is `<PtyHostDir>/sock` (0700), overridable.** It fails closed when unsafe
  or too long.
  - Rejected: `$XDG_RUNTIME_DIR`. It is absent in the Docker runner and under service accounts.
  - Rejected: `mkdtemp` per runner start. Directories would accumulate, and an unstable path would
    complicate cleanup and diagnosis.
  - Rejected: a silent fallback to `/tmp`. It defeats I-4.
- **D-7. Copy the native readers into `Antiphon.PtyHost.Client`** (about 40 lines: `SO_PEERCRED`,
  `GetNamedPipeServerProcessId`, `statx`, `geteuid`).
  - Rejected: lifting `HerdrPeerIdentity` into a shared assembly. That touches Herdr adoption code and
    widens the collision surface for little gain.
- **D-8. Check the peer on the client** with explicit `getsockopt`, uid and pid.
  - Rejected: .NET client `CurrentUserOnly` on Linux. It checks the uid only, and it would add a second
    failure shape. It is used on Windows, where it is the owner-SID check.
  - Rejected: re-enabling server-side `CurrentUserOnly` on the host. It authenticates the client, not the
    host, and has the GT-4 history.
- **D-9. Legacy `/tmp` sockets are adopted through the peer check and drain naturally.** There is no host
  restart and no symlink shim.
- **D-10. S2 flags default off,** rolled out as Observe, then Enforce (§4 S2).
- **D-11. The S3 key channel is `LaunchMessage` after Enforce-mode peer proof.** The runner persists the
  key in a 0600/DACL file in a 0700 `keys/` directory. The host keeps it in memory only (§3.3).
- **D-12.** The proof is `HMAC-SHA256` over a domain tag, the runner nonce, the session id and the host
  instance id, plus the feature `helloProofV1`. `Version` is 2 but informational. The requirement is
  keyed on the runner's key store (I-6).
- **D-13.** Mutual authentication (the host verifying the runner) is **not** in S3 by default (H-2).

## 7. Open questions for the operator (none blocks S1 TestDesign)

- **H-1. S2/S3 scope.** Recommended: S1 now, then S2 (canary Observe, then Enforce). Hold S3. It does not
  defend against a same-uid agent child (§3.4), and S2 already gives kernel proof against other uids.
  Default if unanswered: S3 stays held after S2.
- **H-2. Mutual authentication in S3** (the host refuses commands until the runner proves the key over a
  host nonce). Default: no.
- **H-3. Follow-ups this plan found, proposed and not filed.** The orchestrator files them if wanted.
  - F-1: `PtyHostDir` (manifests, host logs) and the `SessionLogPath` ansi logs are created with the
    default mode. With umask 022 every uid can read session ids, cwd, exe and **terminal output**. Make
    the runner's state roots 0700, or 0750 for an operator group.
  - F-2: the pass-1b pending incident is worded for Herdr ("Herdr has been unreachable …") for every
    `Pending` reason, including `workspace_ownership_conflict` and S1's new reason.
  - F-3: `PtyHostServer.RunAsync` constructs `NamedPipeServerStream` outside its try. A failed bind (GT-2)
    ends the host instead of retrying or logging a typed refusal.

## 8. Risks

- R-1: S1 changes CARD-1636's asserted contract (refused rows become `Starting`/pending instead of
  `Exited`). Any consumer that waited for `SessionExited` after a refused adoption now waits for the
  vanish sweep or a restart. The server's pass-1 reconciliation leaves Pending rows alone (GT-9).
- R-2: getting `SO_PEERCRED` from `NamedPipeClientStream` depends on Unix `SafePipeHandle` exposing the
  socket fd. V-10 is the spike: if it fails, S2 switches to a raw `Socket` connect plus a
  `NamedPipeClientStream(PipeDirection, isAsync, isConnected, SafePipeHandle)` wrapper. Decide at S2
  TestDesign.
- R-3: the Windows `CurrentUserOnly` owner check has no foreign-account witness in this repository.
  `GetNamedPipeServerProcessId` (V-16) carries the proof on Windows.
- R-4: the GT-2 cross-uid behaviour (unlink refused, so the host's bind throws) comes from code reading
  plus a same-uid probe. No cross-uid run was possible here.

## 9. Waiting-input checklist

This plan adds no session that waits for input. S1 and S2 add a **held pending row**
(`pty_host_identity_unproven`, `pty_host_peer_unproven`). Input to it is refused, because the existing
`_pendingReason` guards apply. It is released by:

1. the vanish sweep once the manifest host pid is dead (D-5);
2. a runner restart's adoption sweep, which adopts a host that now proves itself, or registers Exited
   for a dead host;
3. an operator stopping the verified host pid, after which (1) applies.

There is no timer release. With `BlockedTaskParking:Enabled=false` there is **no automatic release
deadline** (CARD-1083), and this row adds none. The server raises its pending incident after
`HerdrPendingAlertMinutes` (default 5). The runner's `KillAsync` on such a row is a no-op (no client,
GT-10). That is unchanged and documented, not fixed here.

## Performance budget

No test was run by this Plan. Measurement sources:

- the cf8b6982 receipts (server2 Linux, loaded, 2026-10-30/31, cited by the CARD-0824 plan):
  `tests/Antiphon.SessionRunner.Tests` build p50 70 s, p95 135 s, n=11;
- the CARD-1636 Code run (08469fcb, server2 Linux): the `Card1636AdoptionIdentityTests` class, 6 cases,
  green inside one narrow lease. No per-test durations were reported, so per-test figures below are
  provisional.

Budget:

- **Selections.**
  - Every row is one exact class: the new `Card1653*` classes plus the CARD-1636 regression class.
  - No AppHost, Postgres, container, migration or browser. Antiphon.Tests is not built in any slice.
  - Downstream: the S3 protocol change is covered by the PtyHost.Tests `FrozenV1` compat witness (V-24)
    and the SessionRunner adoption classes.
- **Per-test bodies.**
  - Fake-host adoption tests: one `sleep`/`ping.exe` stand-in plus one in-process pipe server per test.
    Provisionally at most 3 s (the `StrangerWorld` 5 s listening wait is the bound).
  - Real-host tests (V-13, V-14, V-22, the CARD-1636 real-host case) spawn a detached pty-host and
    `/bin/sh`, restart the runtime and adopt. Provisionally at most 10 s, bounded by
    `PtyHostConnectTimeoutSec=15` and the 5 s adoption connect.
  - Per-test `[Timeout]` is provisionally 60 s, matching `StrangerWorld`'s 60 s CTS. TestDesign replaces
    it with about 2x measured p95 (expected about 20 s).
  - Every class takes `[ParallelLimiter<ProcessSpawnLimit>]`.
- **Phases.**
  - Build: SessionRunner.Tests at most 135 s p95. PtyHost.Tests is unmeasured, provisionally at most
    120 s.
  - Shared setup: none beyond per-test stand-ins. Each test owns its `TestSessionLogRoot` (`c1653-*`
    leak-sweep prefix, added to the sweep list like `c1636-adopt`).
  - Teardown: at most 2 s, killing stand-ins and hosts through `TestSessionTeardown`.
  - Slot wait is reported separately (server2 5 to 15 min, not included).
- **Rows.** A building row is about 5 min (p95 build 2.3 min plus a class of at most 2 min). A reuse
  row is 2 min. Windows rows are 8 min each (an unmeasured Windows build, provisional). No row is near
  30 min.
- **Runs** (all rows `serial: true`; sums exclude slot wait):
  - S1: one run (CP-1, CP-2, CP-3), about 12 min.
  - S2: a linux-native run (CP-4, CP-5, CP-6), about 9 min, and a windows-native run (CP-7, CP-8),
    about 11 min.
  - S3: a portable run (CP-9 to CP-12), about 14 min, and a windows-native run (CP-13), about 8 min.
- **Production budget.**
  - S2 adds one `statx` per launch and per adoption, plus one `getsockopt` per connect: microseconds,
    inside the existing 15 s / 5 s connect budgets.
  - S3 adds one HMAC-SHA256 per connect and one 32-byte file write per launch.
  - No new timeout is introduced in production.
- **Recording.** One TRX per row under the checkpoint run, with per-test durations. TestDesign records
  measured p95 for each class and re-derives `[Timeout]` and `EstimatedMinutes`.

### Checkpoints

This is the closed ordinary scope per committed slice, with one After group per slice.

- **Lanes:**
  - `portable`: default placement with no runner or platform pin. That is currently server2 Linux
    (`GET /api/runner-defaults`: `globalRunnerId=server2`).
  - `linux-native`: requires Linux (statx, `SO_PEERCRED`, `/bin/sh` fake host).
  - `windows-native`: requires Windows (`-Platform Windows`, the desktop runner).
- Each build is the checkpoint tool's fresh `bin-` output with a forward slash, through the build-slot
  gate, removed after the run.
- Every filter is a single exact class. There is no OR anywhere.
- Linux-native rows build separately from portable rows so that they can run on another Linux host. In
  one After group, CP-3 could reuse CP-1 when both lanes share server2.
- S3's rows are listed for completeness. They are not run unless H-1 releases S3.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s1/ | portable-identity-refusal | /*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/* | V-1..V-4 | class, 0 failed/skipped | 4 | 5 | true |
| CP-2 | S1 | CP-1 | portable-c1636-regression | /*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* | V-5 (changed refusal assertions) | class, 0 failed/skipped | 6 | 2 | true |
| CP-3 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s1l/ | linux-native-fresh-launch | /*/Antiphon.SessionRunner.Tests/Card1653FreshLaunchIdentityTests/* | V-6, V-7 | class, 0 failed/skipped | 2 | 5 | true |
| CP-4 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s2l/ | linux-native-socket-dir | /*/Antiphon.SessionRunner.Tests/Card1653SocketDirTests/* | V-8, V-9 | class, 0 failed/skipped | 6 | 5 | true |
| CP-5 | S2 | CP-4 | linux-native-peer-cred | /*/Antiphon.SessionRunner.Tests/Card1653PeerCredTests/* | V-10..V-15 | class, 0 failed/skipped | 6 | 3 | true |
| CP-6 | S2 | CP-4 | linux-native-c1636-regression | /*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* | V-18 (flags off) | class, 0 failed/skipped | 6 | 2 | true |
| CP-7 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s2w/ | windows-native-pipe-peer | /*/Antiphon.SessionRunner.Tests/Card1653WindowsPeerTests/* | V-16, V-17 | class, 0 failed/skipped | 2 | 8 | true |
| CP-8 | S2 | CP-7 | windows-native-c1636-regression | /*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* | V-18 (Windows; also the CARD-1636 pending Windows lane) | class, 0 failed/skipped | 6 | 3 | true |
| CP-9 | S3 | tests/Antiphon.PtyHost.Tests -> bin-c1653-s3h/ | portable-proof-host | /*/Antiphon.PtyHost.Tests/Card1653ProofHostTests/* | V-19..V-21, V-24 | class, 0 failed/skipped | 5 | 5 | true |
| CP-10 | S3 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s3r/ | portable-proof-adoption | /*/Antiphon.SessionRunner.Tests/Card1653ProofAdoptionTests/* | V-22, V-23, V-26 | class, 0 failed/skipped | 3 | 5 | true |
| CP-11 | S3 | CP-10 | portable-secret-store | /*/Antiphon.SessionRunner.Tests/Card1653SecretStoreTests/* | V-25 (Linux half) | class, 0 failed/skipped | 3 | 2 | true |
| CP-12 | S3 | CP-10 | portable-c1636-regression-s3 | /*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* | I-8 under protocol v2 | class, 0 failed/skipped | 6 | 2 | true |
| CP-13 | S3 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s3w/ | windows-native-secret-store | /*/Antiphon.SessionRunner.Tests/Card1653SecretStoreTests/* | V-25 (Windows DACL half) | class, 0 failed/skipped | 3 | 8 | true |

**Roster check (grep 2026-11-02).**

- No class in `tests/` starts with `Card1653` or `C1653`.
- The new names are `Card1653RefusalRetainsHostTests`, `Card1653FreshLaunchIdentityTests`,
  `Card1653SocketDirTests`, `Card1653PeerCredTests`, `Card1653WindowsPeerTests`,
  `Card1653ProofHostTests`, `Card1653ProofAdoptionTests` and `Card1653SecretStoreTests`. None is a prefix
  of another (`ProofHost`/`ProofAdoption` diverge after `Proof`), so a future `(Card1653ProofHost*)`
  operand cannot catch the adoption class.
- `Card1636AdoptionIdentityTests` is the verified existing class in namespace
  `Antiphon.SessionRunner.Tests`. It ran 6/6 green on server2 Linux at `540bc589f` (CARD-1636 report).
- The `PtyHostAdoption*` neighbours are deliberately not rows. Six of their methods are inherited
  cmd.exe reds on Linux (CARD-1636 report), and `RunnerCustody*` has 25 Windows skips, which would make
  `validate` refuse the row.
- Before a linux-native class's red commit, Code adds its `test-execution-policy.json` exclusion row so
  that the Windows suites stay sealed.

## 10. What was not read or run

- The live server2 runner's uid set, `/tmp` contents and umask were not inspected. Whether a second uid
  exists in the runner container is unknown. The exposure is certain on native Linux hosts with more
  than one user.
- Windows behaviour (GT-5, R-3) comes from code reading only.
- The cross-uid squat (GT-2) was not reproduced (one uid available).
- `SessionRunnerSettings.cs` and `test-execution-policy.json` were not censused for in-flight branches.
- `CompletedCardWorkspaceRemovalTests` (the other `CreateWorkspaceConflict` consumer) was not run. S1
  keeps its reason string byte-identical, and TestDesign may add it as a regression row if its Linux run
  has no skips.
