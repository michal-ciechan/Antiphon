# CARD-1653 S2 owned socket directory and kernel peer proof: test design

TestDesign task 5b84b043, 2026-11-03, server2 (Linux, uid 1654), on the Plan branch tip
`d456b2d9146527a813ce721b52fc10cae53d7127` (base master `907edaebb` plus the S2 plan commit).
Scope: **S2 only** of the [S2 plan](2026-11-03-card-1653-S2-plan.md) (§3, §4, §5, D-S2-1..D-S2-10). This
companion adds verification detail and does not change the fix design. Where a seam the plan assumes is
missing, the smallest seam to add is named (§Seams). No production or test source was changed. Every
expected result below is a design, not executed evidence, except the measurements under "Measured facts".

## Measured facts used by this design (server2, tip `d456b2d91`, native output, build-slot leases)

Load average 29-31 on 24 cores throughout.

- `tests/Antiphon.SessionRunner.Tests` build (`-p:UseAppHost=false`): **36 s held** inside the lease
  (slot wait 31 s, not counted). It was warm (S1 TestDesign measured a cold 82 s; the overall plan's
  p95 is 135 s), so rows keep the 135 s p95.
- `Card1619RunnerExitTests/(Relisten_hello_eof*)|(Eof_every_hello*)|(Corrupt_hello_frame*)|(Non_ack_hello*)`
  (the CP-20 filter): **4/4 green, total 4.8 s**, lease held 11 s (slot wait 60 s). Per-test TRX, two runs:
  `Relisten_hello_eof_is_retried_until_ack` 0.88 / 1.27 s, `Eof_every_hello_times_out_inside_the_connect_budget`
  1.99 / 1.99 s, `Corrupt_hello_frame_fails_fast` 0.04 / 0.01 s, `Non_ack_hello_reply_fails_without_retry`
  0.01 / 0.01 s. The filter matches exactly these four methods (the second run had all 10 class methods in
  its OR, and the four names are the only ones with those prefixes).
- `/*/*/(Card1636AdoptionIdentity*)|(Card1653RefusalRetainsHost*)|(Card1653FreshLaunchIdentity*)|(NativeLinuxPolicyWitness*)|(Card1619RunnerExit*)/*`:
  **30/30 green, 33.5 s**, lease held 39 s (slot wait 90 s). Counts: Card1636 6, Card1653RefusalRetainsHost 8,
  Card1653FreshLaunchIdentity 3 (so CP-6 = **17**), NativeLinuxPolicyWitness 3 (CP-17 = **3**), Card1619 10.
  Per-test p95: Card1653RefusalRetainsHost 3.28 s (`Released_identity_row_relaunch_*`), the rest ≤ 2.55 s.
  `NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` is **green** at this tip (the memory's
  legacy-digest red is not present).
- CP-19's five operands match exactly 7 executions in `Card1653RefusalRetainsHostTests`
  (`Refused_identity_*` ×3, `Stop_of_a_pending_*`, `Pending_identity_row_*`, `Vanish_sweep_*`,
  `Non_identity_*`), and none of them matches `Released_identity_row_relaunch_*` (TRX names above).
- `CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders`: not re-run; S1 TestDesign measured
  1/1 in 1.7 s.
- Windows: **not measured** (operator's live rc qualification on the desktop; no Windows build or dispatch).
- `import --plan` on this document's table: see §Checkpoints, "Import verdict".

## Verification design

### Inspection

Bodies read at `d456b2d91` before naming the cases:

- `PtyHostClient.ConnectAsync` (`PtyHostClient.cs:40-93`): one `new NamedPipeClientStream(".", name, InOut,
  PipeOptions.Asynchronous)` per attempt; hello write then read inside the `try`; the retry filter is
  `IOException or TimeoutException or HelloEofException or (OCE && !ct)`; every other exception disposes and
  rethrows. A `PtyHostPeerRefusedException : UnauthorizedAccessException` thrown inside the `try` takes the
  rethrow arm (D-S2-9 holds). → V-9, V-11, G-51, G-52.
- `AdoptOrphanedHostsAsync` (`SessionRunnerRuntime.cs:1969-2185`): with no `WorkspaceUseGate`, no custody
  ledger and no Herdr client (the `PtyHostStrangerWorld` runtime), the **only** `probe.IsAlive` call before
  `session.AdoptAsync` is the pre-check at `:2105`. An id already in `_sessions` is skipped at `:2072`
  (so a second sweep in one runtime never re-adopts). The S1 identity arm is at `:2120`; the generic arm at
  `:2133` kills `manifest.HostPid` for non-custody manifests and then falls through to `CreateAdoptedExited`
  plus `TryDeleteFile`. → V-11, V-11e, V-15, G-56..G-58, G-61 reachability.
- `RunnerSession.StartAsync` (`:3433-3535`) and `TearDownFailedLaunch` (`:3543`): `_hostPid` and
  `_hostStartTimeUtc` are set before `ConnectAsync(PipeNameFor(_sessionId), …)`; the catch tears down; the
  runtime's `StartAsync` catch (`:746-762`) tears down **again** (memory: a session-level teardown mutant alone
  stays green). The pre-launch Grok manifest (`:736`) writes `PipeNameFor`. → V-6b, V-13, V-13b, G-71.
- `RunnerSession.AdoptAsync`/`AdoptCoreAsync` (`:3564-3600`): signature `(manifest, ct)`, no probe; the
  only caller is the sweep (`:2111`). `_hostStartTimeUtc` is reset to null and re-read **after** the identity
  check. → seam S-1.
- `ReleaseUnprovenHostIfDead` (`:4553-4575`): keys on `_pendingReason == UnprovenPendingReason` only. → V-11f.
- `HerdrPeerIdentity` (`HerdrPeerIdentity.cs:10-40`): the AsyncLocal override pattern
  (`NativePidOverride`, `ForceUnavailable`) and the `GetNamedPipeServerProcessId`/`SO_PEERCRED` readers the
  plan copies. → seams S-2, S-3.
- `PtyHostProtocol.PipeNameFor` (`PtyHostMessages.cs:156`): `antiphon-pty-<N>` on Windows, `/tmp/` + that on
  Linux. `SessionRunnerSettings` (`:48-86`): `ResolvedPtyHostDir = PtyHostDir ?? <SessionLogPath>/pty-hosts`,
  `PtyHostConnectTimeoutSec = 15`.
- Tests/fixtures: `PtyHostStrangerWorld` (stand-in, manifest, in-process fake server at `PipeNameFor`, frames
  **after** hello only in `Requests`, `AdoptAsync()` hard-codes `new SystemProcessLivenessProbe()`,
  `RecordingLogger`, `PeerClosedAsync`, `ExitedEvents`, `RestartAsync`); `Card1653FreshLaunchIdentityTests`
  (`WriteFakeHostSource` and `FakePeer` are **private** to that class, `FakePeer` binds `PipeNameFor` and
  records frames after hello only); `Card1636AdoptionIdentityTests.Real_host_records_its_hello_instance_and_is_adopted`
  (real host, runtimeA launch, runtimeB adopt); `PtyHostLeakSweep.TestSessionLogRoot.Create` (root
  `Path.GetTempPath()/antiphon-<prefix>-<N>`, prefix allow-list); `tests/test-execution-policy.json` (the
  `Card1653FreshLaunchIdentityTests` `platform: windows` row at `:877`); `Card1619RunnerExitTests`
  (`[NativeLinuxOnly]`, four connect-retry methods). → fixture changes F-1..F-5.

### Seams (the plan's "TestDesign confirms" items)

| # | Plan assumption | Code at `d456b2d91` | Verdict and smallest seam |
|---|---|---|---|
| S-1 | V-11e probe flip: "the sweep passes its probe to `AdoptAsync`" (R-6) | **Missing.** `AdoptAsync(manifest, ct)`; the world's `AdoptAsync()` takes no probe | Add `IProcessLivenessProbe probe` to `RunnerSession.AdoptAsync`/`AdoptCoreAsync` (the sweep passes its own `probe`), and `PtyHostStrangerWorld.AdoptAsync(IProcessLivenessProbe? probe = null)`. With these, G-49 is witnessed (not a review item). The flip is **order-proof**: the test probe answers alive until the `PeerReaderOverride` has been called, then dead (`FlipProbe`, F-1). |
| S-2 | `PeerReaderOverride` (`internal static AsyncLocal<…>`) | **Missing** (new). Pattern exists: `HerdrPeerIdentity.NativePidOverride` | Add `internal static AsyncLocal<Func<SafePipeHandle, PtyHostPeerCredentials?>?> PeerReaderOverride` in `Antiphon.SessionRunner`. AsyncLocal set by the test before awaiting `AdoptOrphanedHostsAsync`/`StartAsync` flows into the policy factory. |
| S-3 | `FileProbeOverride` | **Missing** (new) | Add `internal static AsyncLocal<IUnixFileProbe?> FileProbeOverride`; `EnsureSafe` and `CheckSocketFile` callers use `FileProbeOverride.Value ?? PtyHostSocketDirectory.DefaultProbe`. Tests wrap the real probe (`OverridingProbe`, F-1) and override only named paths, so every other path is real. |
| S-4 | V-17 "a pure check over (mode × isWindows)" via `RunnerSession.PeerPolicy(expectedPid, expectedStart, probe)` | **Insufficient:** that signature reads the platform from `OperatingSystem`, so Linux cannot exercise the Windows half | Add an internal static pure factory `PtyHostPeerPolicy.Create(PtyHostPeerCheckMode mode, bool isWindows, bool isLinux, int expectedPid, DateTime? expectedStart, IProcessLivenessProbe probe, IUnixFileProbe fileProbe, Func<SafePipeHandle, PtyHostPeerCredentials?> reader, Action<string> onObserved)`; `PeerPolicy` delegates with `OperatingSystem.IsWindows()`/`IsLinux()`. |
| S-5 | (not in the plan) the policy each connect really used is observable | **Missing** | Add `internal static AsyncLocal<Action<PtyHostConnectPolicy?>?> ConnectPolicyObserver`, invoked by `RunnerSession` with the exact policy passed to `ConnectAsync` (null under Off). This is the call-site witness for G-76/G-77 on both lanes. |
| S-6 | (not in the plan) Off keeps `PipeOptions.Asynchronous` byte-identical, pinnable on Linux | **Missing** | Add `public static PipeOptions PtyHostConnectPolicy.PipeOptionsFor(PtyHostConnectPolicy? policy)` (null or `CurrentUserOnly=false` → `Asynchronous`; true → `Asynchronous | CurrentUserOnly`) and construct the client stream with it. Public because client internals are visible only to PtyHost.Tests (GT-S2-8, no csproj change). |
| S-7 | `PtyHostRendezvous.PipeNameFor(s, id, custody, isWindows)` | Pure function, new | Take the request instead of a `bool custody`: `PipeNameFor(SessionRunnerSettings s, RunnerLaunchRequest request, bool isWindows)`, custody = `request.VerificationBinding is not null`. The custody clause is then inside the pure function, so V-18a's custody cases witness it with no separately bypassable call-site flag. |

### Fixtures and binding conventions

- **F-1 `PtyHostStrangerWorld`** (changed, no `[Test]`): adds
  - `StartAsync(..., PtyHostPeerCheckMode peerCheck = Off, PtyHostSocketDirMode socketDirMode = Legacy,
    string? manifestPipeName = null, string? serverPipeName = null, TimeSpan? listenDelay = null,
    bool listen = true)`: the manifest records `manifestPipeName ?? PipeNameFor(id)`; the fake server binds
    `serverPipeName ?? manifestPipeName ?? PipeNameFor(id)` after `listenDelay`; `listen: false` binds
    nothing;
  - `Frames`: every frame the fake server read **including hello** (`HelloMessage` counts). The existing
    `Requests` (after hello) is unchanged for Card1636/S1 callers;
  - `Connections`: the number of accepted connections (0 means the client never connected);
  - `AdoptAsync(IProcessLivenessProbe? probe = null)` (S-1);
  - helpers `ProvenPeer()` = `_ => new PtyHostPeerCredentials(StandIn.Id, Euid())` (on Windows `Uid` null),
    `FlipProbe` (alive until a flag set by a wrapped reader, then dead), `OverridingProbe(real, map)`;
  - `LogLines(token)`: the log lines containing a token, for exact-line assertions.
  Defaults keep every Card1636/S1 call byte-identical (CP-6).
- **F-2 `PtyHostFakeHost`** (new `tests/Antiphon.SessionRunner.Tests/PtyHostFakeHost.cs`, no `[Test]`):
  `WriteFakeHostSource(root)` and `FakePeer` moved out of `Card1653FreshLaunchIdentityTests` unchanged in
  behaviour, plus `FakePeer.StartAsync(string pipeName, HelloAckMessage ack)` and a `HelloSeen` flag (frames
  including hello). `Card1653FreshLaunchIdentityTests` calls them unchanged (CP-6 is its regression).
- **F-3 prefixes** `c1653-sock` and `c1653-peer` in `TestSessionLogRoot.KnownPrefixes`. The default owned
  socket path is `/tmp/antiphon-c1653-sock-<32>/pty-hosts/sock/<32>` = 105 bytes. Tests that use the default
  directory assert `Path.GetTempPath() == "/tmp/"` first, so a long `TMPDIR` fails with a named precondition
  and not as a disguised `pty_host_socket_path_too_long`.
- **F-4 classes.** `Card1653SocketDirTests`: `[NativeLinuxOnly("CARD-1653 S2 statx, SO_PEERCRED and the
  /bin/sh fake host require Linux")]`, plus a `tests/test-execution-policy.json` row (`suite: session-runner`,
  `class: Card1653SocketDirTests`, `platform: windows`, reason `Linux-only by construction: statx, SO_PEERCRED,
  Unix socket files and the /bin/sh fake pty-host. The case remains required on Linux.`, owner `CARD-1653`)
  with `policyHash` resealed from the witness output, in the red commit. `Card1653PeerCheckTests`: portable,
  no Linux-only method (Linux-only assertions are guarded by `OperatingSystem.IsLinux()` inside portable
  methods). Both: `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, `[Timeout(10_000)]`.
- **F-5 exact log lines.** The fixed lines are pinned **exactly** (whole line equality via `LogLines`), never
  by "does not contain the pid": a pid is decimal and can occur inside a GUID's hex text, which would make a
  negative substring assertion flaky. Lines (Code may choose other fixed wording only if every assertion
  changes with it):
  - adoption refusal: `pty_host_peer_mismatch: adoption refused for session {id}`;
  - launch refusal: `pty_host_peer_mismatch: launch refused for session {id}`;
  - observe: `pty_host_peer_mismatch (observe): session {id}`;
  - legacy: `pty_host_legacy_socket_path: session {id}`;
  - directory: `pty_host_socket_dir_unsafe` (the launch exception message is exactly the code).

### Delivery inventory

S2 adds no message and no transport, and changes no protocol frame. It adds one reason string,
`pty_host_peer_unproven`, to S1's held pending row, and it moves where new Linux sockets live.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (recipient) | Durable identity |
|---|---|---|---|---|---|---|
| D-A' held peer refusal | sweep peer arm | server pass 1 (hold) and pass 1b (incident) via the runner session list | the retained manifest file | runner restart re-runs adoption and re-registers the row; server restart rebuilds `_pendingAlerts` (existing, S1) | runner DTO `Starting`/`Pending == "pty_host_peer_unproven"` (V-11); the server side is S1's reason-agnostic `Pending` handling | `SessionId` + manifest `HostPid`/`HostStartTimeUtc` |
| D-B' release | `ReleaseUnprovenHostIfDead` | server via `SessionExited` and the list | none new; manifest deleted after the transition | a lost event is recovered by the list (existing) | `SessionExited` exactly once + DTO `Exited/ProcessVanished` (V-11f) | `SessionId` |

Recipient evidence and declared substitutes:

- **Substitute 1:** no server row. S1's V-8 (`SessionReconciliationServiceTests.Pty_identity_unproven_*`)
  proves the server holds and alerts on a pending reason; the server code compares
  `!string.IsNullOrEmpty(Pending)` and never the value. It cannot prove that the transport carries the new
  string; that is the same `string? Pending` field S1 already carries. The S1 mutation sweep found S1's PC-26
  equivalent (the hold sits behind `Status == "Running"`); that is not reopened here.
- **Substitute 2:** D-B' recipient evidence is the runner hub publish (V-11f), as in S1.
- Session input is not touched: a pending row refuses input (S1), so no UserPrompt transcript applies.

### Proves it works now

**Class `Card1653SocketDirTests`** (linux-native, root prefix `c1653-sock`). Every adoption case uses
F-1 with `peerCheck: Enforce` and a 5 s adoption budget (the existing `AdoptCoreAsync` constant).

- **V-8a** `Ensure_safe_creates_an_absent_directory_owner_only`: `EnsureSafe(<root>/s/sock, realProbe)` on an
  absent path → returns; `File.GetUnixFileMode == 0700` (`UserRead|UserWrite|UserExecute` only). Red at
  `d456b2d91`: does not compile (type new; red-commit shim throws `NotImplementedException`).
- **V-8** `Ensure_safe_tightens_an_owned_wider_directory`: pre-create `<root>/s/sock` with 0755 → returns;
  mode 0700. Separate from V-8a so PC-33 and PC-34 each have their own method.
- **V-8b** `Unsafe_socket_directory_is_refused(string shape)`, 5 cases; each throws
  `PtyHostSocketDirUnsafeException` whose `Message == "pty_host_socket_dir_unsafe"`:
  - `"symlink"`: `<root>/s/sock` is a real symlink to `<root>/target` (created 0700, owned): real probe;
  - `"foreign-owner"`: `OverridingProbe` reports the directory `Uid = euid + 1` (mode 0700);
  - `"unsafe-ancestor"`: `OverridingProbe` reports `<root>/s` as `Directory, Uid = euid + 1, Mode = 0777`
    (no sticky bit);
  - `"symlinked-unsafe-ancestor"`: `<root>/s` is a real symlink to `<root>/t`, and `OverridingProbe` reports
    `<root>/t` as foreign 0777 non-sticky (D-S2-5: the resolved target is checked);
  - `"tighten-ineffective"`: `OverridingProbe` reports the directory as owned 0755 on every Lstat, so the
    chmod cannot satisfy the re-check.
- **V-8c** `Socket_path_length_is_bounded(int length, bool refused)`, cases `(107, false)` and `(108, true)`:
  `PtyHostSocketDir` is `<root>/` padded with `a` to `length - 33` bytes, so `dir + "/" + 32` is exactly
  `length` UTF-8 bytes. 107 returns (and the directory is 0700); 108 throws with
  `Message == "pty_host_socket_path_too_long"`.
- **V-9** `Unsafe_socket_file_is_refused_before_connect(string shape)`, 3 cases. Setup: Enforce,
  `PeerReaderOverride = world.ProvenPeer()` (so only the file check can refuse), manifest `PipeName =
  <root>/p/<N>`:
  - `"regular-file"`: a regular file at the manifest path, `listen: false`;
  - `"symlink"`: the manifest path is a symlink to the live fake server's socket at `PipeNameFor(id)`;
  - `"foreign-owner"`: the fake server listens at the manifest path, and `OverridingProbe` reports the socket
    file `Uid = euid + 1`.

  Assertions, in order:
  1. `AdoptAsync()` returns 0 within **< 1 s** (a stopwatch around the sweep; budget 5 s, so a retry would
     take ≥ 5 s);
  2. `Connections == 0` and `Frames` empty (refused before connect);
  3. `StandIn.WaitForExit(500)` is false;
  4. the manifest exists, its hash unchanged;
  5. `Status == "Starting"`, `Pending == "pty_host_peer_unproven"`;
  6. exactly one line equal to the adoption refusal line (F-5).

  Red at `d456b2d91` (shim ignores the policy): `"regular-file"` at 1 (retried until the 5 s budget, then the
  generic arm kills the stand-in); `"symlink"` and `"foreign-owner"` at 2 (connected and adopted).
- **V-9b** `Absent_socket_retries_inside_the_budget`: Enforce, `ProvenPeer()`, the fake server binds the
  manifest path after `listenDelay: 1 s` → `AdoptAsync()` returns 1, `Status == "Running"`, the sweep took
  ≥ 1 s and < 5 s. Green at base (control, PC-45 shows it can go red).
- **V-11d** `Peer_uid_must_match_the_runner`: Enforce, `PeerReaderOverride` returns `(StandIn.Id, euid + 1)`;
  the socket file is real (owned) → refused exactly as V-9 assertions 3-6, with `Connections == 1` and
  `Frames` empty.
- **V-13b** `Unsafe_directory_fails_the_launch_before_spawn`: F-2 fake host source, Owned, Enforce,
  `PtyHostConnectTimeoutSec = 5`, `PtyHostSocketDir` = a symlink to an owned 0700 directory. `StartAsync`
  throws; the innermost `PtyHostSocketDirUnsafeException.Message == "pty_host_socket_dir_unsafe"`;
  `<root>/fake-host.pid` does not exist (no spawn); no file in `PtyHostLogDir`; no manifest; no
  `/tmp/antiphon-pty-<N>`; `Get(id)` is null (the failed launch is removed).
- **V-14** `Legacy_real_host_is_adopted_after_owned_enforce`: a real host launched by runtime A under
  Legacy/Off (`manifest.PipeName == PipeNameFor(id)`); runtime B under Owned/Enforce **with
  `PtyHostSocketDir` pointing at a symlink** (an unsafe directory) → `AdoptOrphanedHostsAsync` returns 1,
  `Running`; exactly one line equal to the legacy line (F-5); a second sweep on B returns 0 and the legacy
  line count is still 1; the log contains `pty_host_socket_dir_unsafe` (the sweep diagnostic) and the host is
  still adopted (D-S2-4). `finally`: stop the session and kill the host pid best effort.
- **V-14b** `Forged_legacy_peer_is_refused`: F-1 at the legacy path (`PipeNameFor`), Owned/Enforce, real
  reader (the peer is the test process) → refused as V-9 assertions 3-6, `Frames` empty.
- **V-6b** `Fresh_launch_refuses_a_foreign_peer_before_hello`: F-2 fake host, Owned, Enforce,
  `PtyHostConnectTimeoutSec = 5`; the test pre-creates the owned directory and binds `FakePeer` at
  `<sock>/<N>` answering a **valid** fresh ack (own session, a fresh instance, `WaitingForLaunch`, so S1's G2
  check would pass). `StartAsync` throws; the innermost `PtyHostPeerRefusedException.Message ==
  "pty_host_peer_mismatch"`; `PeerClosedAsync(5 s)`, then `HelloSeen` false and `Frames` empty (no hello, no
  Launch, no canary); exactly one launch refusal line; the pid in `<root>/fake-host.pid` is dead within 5 s.
  Red at base: hello and `LaunchMessage` (with the canary) recorded.

Count: 1 + 1 + 5 + 2 + 3 + 1 + 1 + 1 + 1 + 1 + 1 = **18 executions**.

**Class `Card1653PeerCheckTests`** (portable, root prefix `c1653-peer`; on Windows the stand-in is
`ping.exe` and every `Uid` is null).

- **V-10** `Peer_reader_returns_the_listener_process`: an in-process `NamedPipeServerStream` and a connected
  client; `PtyHostPeerCredentials.Read(client.SafePipeHandle)` → `Pid == Environment.ProcessId`; Linux:
  `Uid == geteuid()`; Windows: `Uid` null. Red: type new (shim returns null).
- **V-11** `Enforce_refuses_a_replayed_identity_from_a_foreign_peer`: F-1 with the **correct** session and
  instance (`Answer(SessionId, HostInstance)`), Enforce, real reader. Assertions:
  1. `AdoptAsync()` returns 0;
  2. `PeerClosedAsync(5 s)`, then `Frames` empty (no hello written);
  3. `StandIn.WaitForExit(500)` false;
  4. manifest present, hash unchanged;
  5. `Status == "Starting"`, `Pending == "pty_host_peer_unproven"`, `HostPid == StandIn.Id`;
  6. `ExitedEvents(id) == 0`;
  7. exactly one adoption refusal line (F-5) and no line containing `pty_host_identity_mismatch`;
  8. `ConnectPolicyObserver` saw one non-null policy, `CurrentUserOnly == OperatingSystem.IsWindows()`.

  Red at base: 1 (adopted; the CARD-1636 gap). On Windows this is the overall plan's V-16 (M-13).
- **V-11b** `Off_adopts_the_replayed_identity`: the same world under Off → returns 1, `Running`, `Frames`
  starts with `HelloMessage`; `ConnectPolicyObserver` saw `null`. Control: proves V-11's fixture can adopt.
- **V-11c** `Unreadable_peer_credentials_are_unproven`: `PeerReaderOverride` returns null → V-11
  assertions 1-7.
- **V-11e** `Peer_start_must_match_the_manifest_host`: `PeerReaderOverride` = `ProvenPeer()` wrapped to set
  the flip flag; `AdoptAsync(FlipProbe)` (alive for the sweep pre-check, dead once the peer was read) → V-11
  assertions 1-7, and `FlipProbe.Calls >= 2` (the re-check ran). Seam S-1.
- **V-11f** `Peer_refusal_row_is_released_once_its_host_is_dead`: after V-11's refusal, a sweep with the
  stand-in alive returns `[]`; kill and reap it; `SweepVanishedSessions` returns `[id]`; `Exited`,
  `ProcessVanished`, `Pending` null, `ExitedEvents == 1`, the manifest absent; a second sweep `[]`.
- **V-12** `Observe_adopts_and_logs_the_mismatch`: V-11's world under Observe → returns 1, `Running`;
  exactly one line equal to the observe line (F-5); no adoption refusal line; the observer saw
  `CurrentUserOnly == false` on both platforms.
- **V-13** `Owned_enforce_real_host_is_launched_and_adopted`: a real host (the Card1636 real-host shape),
  runtime A under Owned/Enforce, `PtyHostConnectTimeoutSec = 5`: launch `Running`; Linux:
  `manifest.PipeName == <ResolvedPtyHostSocketDir>/<N>`, the directory mode 0700, the socket file owned by
  euid, no `/tmp/antiphon-pty-<N>`; Windows: `manifest.PipeName == PipeNameFor(id)` (I-S2-b). Runtime B
  (Owned/Enforce, real reader) adopts: returns 1, `Running`, no legacy line, no refusal line. This is the
  cross-process `SO_PEERCRED` and `GetNamedPipeServerProcessId` positive witness (no false refusal).
- **V-15** `Proven_peer_failure_keeps_the_lawful_exit_path`: Enforce, `ProvenPeer()`, `StatusReply=Error`
  → returns 0, `Exited`, `Pending` null, manifest absent, `StandIn.WaitForExit(5000)` **true** (killed),
  no refusal line. Green at base (control).
- **V-17** `Connect_policy_by_mode_and_platform(PtyHostPeerCheckMode mode, bool isWindows)`, 6 cases (Off,
  Observe, Enforce × Windows, Linux), through `PtyHostPeerPolicy.Create` (S-4):
  - Off → null, and `PipeOptionsFor(null) == PipeOptions.Asynchronous` (S-6, the Windows byte-identity pin);
  - Observe → `CurrentUserOnly == false`, `Observe == true`, `PipeOptionsFor == Asynchronous`;
  - Enforce → `CurrentUserOnly == isWindows`, `Observe == false`, `PipeOptionsFor == Asynchronous |
    (isWindows ? CurrentUserOnly : 0)`;
  - `CheckSocketFile` non-null only for `isLinux` cases.
- **V-18a** `Rendezvous_name_by_mode_platform_and_custody(PtyHostSocketDirMode mode, bool isWindows, bool custody)`,
  8 cases through `PtyHostRendezvous.PipeNameFor(settings, request, isWindows)` (S-7): only (Owned, Linux,
  non-custody) gives `<ResolvedPtyHostSocketDir>/<N>`; every other case equals `PipeNameFor(id)` computed for
  that platform (`antiphon-pty-<N>` for Windows cases, `/tmp/antiphon-pty-<N>` for Linux cases; the expected
  value is built from the case's platform, not the host's). Custody cases carry a `VerificationExecutionBinding`
  (the `UnixPtyArgvAdmissionTests.Binding` shape).
- **V-18b** `Settings_default_to_legacy_and_off`: `new SessionRunnerSettings()` and a bind from an empty
  `ConfigurationBuilder` → `Legacy`, `Off`, `PtyHostSocketDir` null, `ResolvedPtyHostSocketDir ==
  Path.GetFullPath(<ResolvedPtyHostDir>/sock)`.

Count: 1 + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 6 + 8 + 1 = **24 executions**.

### Guards the regression

- **R-1** I-S2-a on S1 and CARD-1636 with the defaults: `Card1636AdoptionIdentityTests`,
  `Card1653RefusalRetainsHostTests`, `Card1653FreshLaunchIdentityTests` unchanged in assertions after F-1/F-2
  (CP-6; measured 17/17 at the tip).
- **R-2** connect retry semantics with a null policy (CARD-1632/1634): the four `Card1619RunnerExitTests`
  methods (CP-20; measured 4/4).
- **R-3** the Windows suites stay sealed with the new Linux-only class: `NativeLinuxPolicyWitnessTests/*`
  (CP-17; measured 3/3).
- **R-4** the sweep's catch reordering keeps `workspace_ownership_conflict`:
  `CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` (CP-18).
- **R-5** S1 no-kill and CARD-1636 on Windows (the S1 TestDesign gap): CP-8, CP-19 (Windows lane).

### Guard inventory

Continues S1's G-1..G-32. G-33..G-67 keep the plan's numbers. G-68..G-81 are splits and guards this design
adds (each independently bypassable).

| G | Plan ref + guard | PC |
|---|---|---|
| G-33 | I-4 an absent directory is created 0700 | PC-33 |
| G-34 | I-4 an owned wider directory is tightened | PC-34 |
| G-35 | I-4 a symlinked socket directory is refused (no-follow Lstat) | PC-35 |
| G-36 | I-4 a foreign-owned socket directory is refused | PC-36 |
| G-37 | I-4/D-S2-5 an ancestor must be owned by euid/root and not writable without sticky | PC-37 |
| G-38 | I-4 a path over 107 bytes is refused | PC-38 |
| G-39 | I-4/D-6 an unsafe directory fails the launch before spawn (no `/tmp` fallback) | PC-39 |
| G-40 | §3.2 an Owned launch connects to the same owned path it gave the host | PC-40 |
| G-41 | I-S2-a Legacy keeps `PipeNameFor` | PC-41 |
| G-42 | I-S2-c custody keeps `PipeNameFor` | PC-42 |
| G-43 | §3.4 a non-socket or symlink at the socket path is refused before connect | PC-43 |
| G-44 | §3.4 a foreign-owned socket file is refused | PC-44 |
| G-45 | GT-S2-5 an absent socket keeps retrying | PC-45 |
| G-46 | §3.4 the Linux reader returns the listener (`SO_PEERCRED`) | PC-46 |
| G-47 | I-1 the peer pid must equal the expected host pid | PC-47 |
| G-48 | I-1 unreadable credentials are unproven | PC-48 |
| G-49 | I-1 the adoption peer must be alive at the manifest start | PC-49 |
| G-50 | I-1 Linux peer uid == euid | PC-50 |
| G-51 | I-1/D-S2-1 the peer check runs before the hello write | PC-51 |
| G-52 | GT-S2-5/D-S2-9 a peer refusal is not retried | PC-52 |
| G-53 | I-1 a fresh launch connects with a policy (Launch never reaches an unproven peer) | PC-53 |
| G-54 | §3.5 Observe never refuses | PC-54 |
| G-55 | I-7 the observe line is the fixed token and session only | PC-55 |
| G-56 | I-2 a peer refusal never kills `manifest.HostPid` | PC-56 |
| G-57 | I-3 a peer refusal keeps the manifest | PC-57 |
| G-58 | I-2 a proven peer's failure keeps the lawful exit path | PC-58 |
| G-59 | I-3 the vanish sweep releases `pty_host_peer_unproven` | PC-59 |
| G-60 | I-8 legacy paths get the same checks under Owned/Enforce | PC-60 |
| G-61 | I-8/§6 an Owned adoption of a legacy path emits the legacy line once | PC-61 |
| G-62 | §3.4 the policy factory sets `CurrentUserOnly` for Windows Enforce | PC-62 |
| G-63 | I-S2-a the factory: Off is null, Observe never sets `CurrentUserOnly` | PC-63 |
| G-64 | I-1 Windows: the server-pid compare applies when `Uid` is null | PC-64 |
| G-65 | I-S2-b the pure name choice keeps the Windows name under Owned | PC-65 |
| G-66 | §4 the Linux-only class is pinned and excluded on Windows (policy seal) | PC-66 |
| G-67 | D-10 the flags default to Legacy/Off | PC-67 |
| G-68 | I-4 the mode is re-checked after the chmod | PC-68 |
| G-69 | D-S2-5 a symlinked ancestor is resolved and its target checked | PC-69 |
| G-70 | I-4 a path of exactly 107 bytes is accepted (no false refusal) | PC-70 |
| G-71 | I-2 a fresh-launch peer refusal tears down the host this launch spawned | PC-71 |
| G-72 | I-7 the adoption refusal line is the fixed token and session only | PC-72 |
| G-73 | I-7 the refusal exception message is the fixed code only (it surfaces as the launch failure) | PC-73 |
| G-74 | I-3 the refusal row is `Starting` with exactly `pty_host_peer_unproven` | PC-74 |
| G-75 | I-3 a peer refusal publishes no `SessionExited` | PC-75 |
| G-76 | §3.4 the runtime passes the real platform to the policy factory (Windows Enforce gets `CurrentUserOnly`) | PC-76 |
| G-77 | I-S2-a the runtime passes no policy under Off | PC-77 |
| G-78 | I-S2-a a null policy keeps `PipeOptions.Asynchronous` exactly (Windows byte-identity, pinned on Linux) | PC-78 |
| G-79 | I-S2-b the launch passes the real platform to the name choice (Windows keeps `antiphon-pty-<N>`) | PC-79 |
| G-80 | D-S2-4 the sweep's `EnsureSafe` diagnostic never gates adoption | PC-80 |
| G-81 | §3.4 the Windows reader returns the server pid (`GetNamedPipeServerProcessId`) | PC-81 |

guards=49, mapped=49, missing=0, duplicate PC maps=0.

### Positive controls

Each PC breaks its guard with a compiling defect and expects the named method red at the named assertion.
Mutation runs break/red/restore/green after the land. Code runs the V/R rows. Review judges before the land.
Filters are method-scoped (`--treenode-filter "/*/Antiphon.SessionRunner.Tests/<Class>/<Method>"`); a method
with `[Arguments]` runs all its cases, and the named case must go red. Zero tests, a build error or a fixture
error is not red. Abbreviations: SD = `Card1653SocketDirTests`, PK = `Card1653PeerCheckTests`; files:
`Dir` = `PtyHostSocketDirectory.cs`, `Cli` = `PtyHostClient.cs`, `Cred` = `PtyHostPeerCredentials.cs`,
`Rdv` = `PtyHostRendezvous.cs`, `Rt` = `SessionRunnerRuntime.cs`.

| PC | Break | Expect red | Reachability (checked against the code and the design) |
|---|---|---|---|
| PC-33 | `Dir`: `Directory.CreateDirectory(path)` with no mode, and skip the tighten | SD/`Ensure_safe_creates_an_absent_directory_owner_only` at mode 0700 | umask 022 gives 0755 |
| PC-34 | `Dir`: skip the chmod branch (accept owned 0755 as is) | SD/`Ensure_safe_tightens_an_owned_wider_directory` at mode 0700 | pre-created 0755 |
| PC-35 | `Dir`: statx flag `AT_SYMLINK_NOFOLLOW` → 0 | SD/`Unsafe_socket_directory_is_refused("symlink")` at the throw | target is owned 0700, so a following stat accepts it |
| PC-36 | `Dir`: drop `Uid == Euid` for the directory | same `("foreign-owner")` | probe-only foreign uid |
| PC-37 | `Dir`: drop the ancestor walk | same `("unsafe-ancestor")` | the only unsafe fact is on the parent |
| PC-38 | `Dir`: bound 107 → 200 | SD/`Socket_path_length_is_bounded(108, true)` | directory is otherwise safe |
| PC-39 | `Rt`: on `PtyHostSocketDirUnsafeException`, fall back to `PtyHostProtocol.PipeNameFor` | SD/`Unsafe_directory_fails_the_launch_before_spawn` at the code (and `fake-host.pid` absent) | the fallback spawns the fake host and times out at 5 s, a different exception |
| PC-40 | `Rt`: `RunnerSession.StartAsync` connects to `PipeNameFor(_sessionId)` instead of `pipeName` | PK/`Owned_enforce_real_host_is_launched_and_adopted` at launch `Running` (Linux) | host listens only at the owned path; connect times out |
| PC-41 | `Rdv`: Legacy returns the owned path on Linux | PK/`Rendezvous_name_by_mode_platform_and_custody` (Legacy, Linux) | pure |
| PC-42 | `Rdv`: drop the custody clause | same (Owned, Linux, custody) | pure, custody derived from the request (S-7) |
| PC-43 | `Dir`: `CheckSocketFile` returns `Ok` for any existing path | SD/`Unsafe_socket_file_is_refused_before_connect("regular-file")` at < 1 s and `("symlink")` at `Connections == 0` | `ProvenPeer()` stops the peer check masking the symlink case |
| PC-44 | `Dir`: `CheckSocketFile` ignores the uid | same `("foreign-owner")` at `Connections == 0` | `ProvenPeer()` again |
| PC-45 | `Cli`: `Absent` treated as `Refused` | SD/`Absent_socket_retries_inside_the_budget` at returns 1 | |
| PC-46 | `Cred`: getsockopt option 17 → 18 (`SO_RCVLOWAT`, 4 bytes) | PK/`Peer_reader_returns_the_listener_process` at `Pid` (Linux) | a 4-byte answer is not the pid; a length check returns null, also red |
| PC-47 | `Rt`: `AcceptPeer` compares the uid only (`!IsLinux || c.Uid == euid`) | PK/`Enforce_refuses_a_replayed_identity_from_a_foreign_peer` at 1 | test process has the runner's uid |
| PC-48 | `Rt`: `AcceptPeer` returns true for null credentials | PK/`Unreadable_peer_credentials_are_unproven` at 1 | |
| PC-49 | `Rt`: drop the `probe.IsAlive(c.Pid, expectedStart)` clause | PK/`Peer_start_must_match_the_manifest_host` at 1 | needs S-1; FlipProbe is order-proof |
| PC-50 | `Rt`: drop the Linux uid clause | SD/`Peer_uid_must_match_the_runner` at returns 0 | pid and start match; only uid differs |
| PC-51 | `Cli`: run the peer check after the hello write/read | PK/`Enforce_refuses_a_replayed_identity_from_a_foreign_peer` at 2 (`Frames` empty) | F-1 `Frames` counts hello |
| PC-52 | `Cli`: `PtyHostPeerRefusedException : IOException` | SD/`Unsafe_socket_file_is_refused_before_connect("regular-file")` at < 1 s | retried for 5 s, then the generic arm |
| PC-53 | `Rt`: the fresh launch passes `policy: null` | SD/`Fresh_launch_refuses_a_foreign_peer_before_hello` at the throw / `HelloSeen` | the valid fresh ack passes S1's G2 |
| PC-54 | `Cli`: Observe throws like Enforce | PK/`Observe_adopts_and_logs_the_mismatch` at returns 1 | |
| PC-55 | `Rt`: the observe line adds `" pid {Pid}"` | same, at the exact observe line | F-5 exact equality |
| PC-56 | `Rt`: delete the peer catch arm (the refusal reaches the generic arm) | PK/`Enforce_refuses_…` at 3 (stand-in alive) | M-12 |
| PC-57 | `Rt`: the peer arm calls `TryDeleteFile(file)` | same, at 4 | |
| PC-58 | `Rt`: the generic arm registers the peer pending row whenever the mode is Enforce | PK/`Proven_peer_failure_keeps_the_lawful_exit_path` at `Exited` | `ProvenPeer()` passes the check |
| PC-59 | `Rt`: the release keeps only `pty_host_identity_unproven` | PK/`Peer_refusal_row_is_released_once_its_host_is_dead` at `[id]` | |
| PC-60 | `Rt`: skip the policy when the path is not under `ResolvedPtyHostSocketDir` | SD/`Forged_legacy_peer_is_refused` at returns 0 | M-11 |
| PC-61 | `Rt`: delete the legacy log line | SD/`Legacy_real_host_is_adopted_after_owned_enforce` at count 1 | the plan's PC-61 ("log on every sweep") is **equivalent**: a second sweep skips a registered id (`:2072`), so it never reaches the log; replaced |
| PC-62 | `Rt`: the factory sets `CurrentUserOnly = false` always | PK/`Connect_policy_by_mode_and_platform(Enforce, true)` | pure (S-4) |
| PC-63 | `Rt`: the factory sets `CurrentUserOnly = isWindows` for Observe too | same `(Observe, true)` | pure |
| PC-64 | `Rt`: `AcceptPeer` returns true when `Uid` is null | PK/`Enforce_refuses_…` at 1 on the **Windows lane** | Linux reader always has a uid, so Linux stays green; Windows lane, not measured |
| PC-65 | `Rdv`: Owned applies on Windows | PK/`Rendezvous_name_by_mode_platform_and_custody` (Owned, Windows, non-custody) | pure |
| PC-66 | remove `[NativeLinuxOnly]` from `Card1653SocketDirTests` (test file) | `Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` | green at the tip (measured); Mutation records the exact red text |
| PC-67 | default `PtyHostPeerCheck = Enforce` | PK/`Settings_default_to_legacy_and_off` | |
| PC-68 | `Dir`: skip the Lstat re-check after the chmod | SD/`Unsafe_socket_directory_is_refused("tighten-ineffective")` | probe reports 0755 after the chmod |
| PC-69 | `Dir`: accept a symlinked ancestor without resolving it | same `("symlinked-unsafe-ancestor")` | only the resolved target is unsafe |
| PC-70 | `Dir`: bound `>` → `>=` | SD/`Socket_path_length_is_bounded(107, false)` | |
| PC-71 | `Rt`: in **both** `RunnerSession.StartAsync`'s catch and the runtime `StartAsync` catch, skip `TearDownFailedLaunch` when the exception is `PtyHostPeerRefusedException` | SD/`Fresh_launch_refuses_a_foreign_peer_before_hello` at the fake pid dead | one catch alone is equivalent (double teardown, memory; S1 PC-22 stayed green this way), so the mutant edits both |
| PC-72 | `Rt`: the adoption refusal line adds the peer pid | PK/`Enforce_refuses_…` at 7 | F-5 exact equality |
| PC-73 | `Cli`/`Rt`: the refusal message is `$"{code}: {pid}"` | SD/`Fresh_launch_refuses_a_foreign_peer_before_hello` at `Message ==` | |
| PC-74 | `Rt`: the peer arm passes `UnprovenPendingReason` (the identity reason) | PK/`Enforce_refuses_…` at 5 | the release would still work, so only the exact assertion catches it |
| PC-75 | `Rt`: the peer arm publishes `SessionExited` after registering the row | same, at 6 | |
| PC-76 | `Rt`: `PeerPolicy` passes `isWindows: false` | PK/`Enforce_refuses_…` at 8 on the **Windows lane** | Linux stays green (expects false); Windows lane, not measured |
| PC-77 | `Rt`: under Off the call site passes `new PtyHostConnectPolicy()` instead of the factory's null | PK/`Off_adopts_the_replayed_identity` at the observer null | S-5 |
| PC-78 | `Cli`: `PipeOptionsFor(null)` returns `Asynchronous | CurrentUserOnly` | PK/`Connect_policy_by_mode_and_platform(Off, true)` and `(Off, false)` | S-6; runs on Linux |
| PC-79 | `Rt`: `StartAsync` passes `isWindows: false` to `PtyHostRendezvous.PipeNameFor` | PK/`Owned_enforce_real_host_is_launched_and_adopted` at `PipeName == PipeNameFor(id)` on the **Windows lane** | Linux stays green; Windows lane, not measured |
| PC-80 | `Rt`: the sweep returns (or skips pty manifests) when its `EnsureSafe` diagnostic fails | SD/`Legacy_real_host_is_adopted_after_owned_enforce` at returns 1 | V-14 runs B with an unsafe (symlink) directory |
| PC-81 | `Cred`: the Windows branch returns null | PK/`Peer_reader_returns_the_listener_process` on the **Windows lane** | Windows lane, not measured |

Equivalent or unreachable mutants considered and **not** listed as guards:

- The plan's PC-61 wording (log on every sweep): equivalent, replaced (row above).
- "Shutdown the refused session before disposing" (S1's G-8 analogue): equivalent in S2, because a peer
  refusal happens inside `ConnectAsync`, so there is no client to send `Shutdown` on.
- "`AcceptPeer` drops the `Pid > 0` clause": equivalent while `expectedPid > 0`; pid 0 already fails the
  equality (G-47). The sweep pre-check requires `manifest.HostPid > 0`, and a fresh launch's `_hostPid` is a
  live pid.
- "`ConnectAsync` constructs the stream with `CurrentUserOnly` without `PipeOptionsFor`": not observable on a
  single account on either platform (R-3), and the defect direction is stricter, not weaker. Review checks
  that the one construction uses `PipeOptionsFor`.

Windows-lane PCs: PC-64, PC-76, PC-79, PC-81 (four). Every other PC (45) runs on server2.

### Out of scope

- S3 (key, HMAC hello, protocol v2): plan header.
- Custody launches and adoptions under a peer refusal: custody keeps today's path (I-S2-c, which never kills
  a bound host; existing behaviour). `RunnerCustodyCrashTests`/`HostCustodyTests` pass no policy, so their
  path is the null-policy code that CP-20 and CP-6 cover; their Linux runs are unmeasured here.
- A cross-uid squatter and a cross-account Windows owner: one uid on server2 and one account on the desktop
  (R-3). The probe and reader seams stand in on Linux.
- `SO_PEERCRED` across pid namespaces (R-5): does not arise on server2; Observe measures it in rollout §6.
- Stale socket cleanup (F-4) and owned custody rendezvous (F-5).
- An unknown enum value failing options binding: .NET binder behaviour, not S2 code.

### Implementation steps (red first, then green)

1. Red commit: F-1..F-3, both classes, the policy row with the resealed hash, and a compile shim. The shim is
   the new public/internal types and parameters (S-1..S-7, settings, enums) with today's behaviour: the
   policy is ignored, `EnsureSafe`/`CheckSocketFile` throw `NotImplementedException`, the reader returns null,
   `PipeNameFor` returns `PipeNameFor(id)`, the policy factory returns null, `PipeOptionsFor` returns
   `Asynchronous`. Expected red: V-8a, V-8, V-8b (5), V-8c (2), V-9 (3), V-11d, V-13b, V-14b, V-6b; V-10,
   V-11, V-11c, V-11e, V-11f, V-12, V-13 (Linux), V-17 (Enforce/Observe cases), V-18a (Owned, Linux,
   non-custody). Expected green: V-9b, V-14 (adoption works at base; its legacy-line assertion is red),
   V-11b, V-15, V-18b and the other V-17/V-18a cases (controls whose ability to go red is shown by their PCs).
   Code records the exact red list.
2. Green: the settings, `PtyHostSocketDirectory`, `PtyHostPeerCredentials`, the client policy, the runtime
   wiring. Commit, then checkpoint Run L. Run W is a later Windows task.

## Performance budget

Sources: the measurements above (server2, loaded); S1 Code TRX (p95 2.78 s, plan); the overall plan's build
p95 135 s.

- **Per-test bodies (estimated).**
  - Fake-world adoptions (V-9, V-9b, V-11*, V-12, V-14b, V-15): 0.5-2 s (measured S1 analogues 0.15-2.07 s).
    V-9b ≥ 1 s by construction; V-11f about 2.5 s.
  - Pure (V-8*, V-10, V-17, V-18a, V-18b): < 0.2 s each.
  - Fake-host launches (V-6b, V-13b): about 1 s (measured S1 V-6 0.2-0.36 s plus the 5 s teardown poll's
    first hit).
  - Real-host (V-13, V-14): about 3 s (measured Card1636 real host 2.0 s, plus a second runtime and sweep).
- **`[Timeout(10_000)]`** on both new classes: max(10 s, 2x estimated p95 3 s = 6 s, the largest internal
  bound 5 s + 5 s = 10 s). The 5 s bounds are the adoption connect, `PtyHostConnectTimeoutSec = 5` and
  `PeerClosedAsync(5 s)`. Under PC-39/PC-52 a mutant waits the 5 s budget and still fails inside 10 s at the
  assertion, not at the timeout. Code resets each class from its first green TRX by the same formula.
- **Shared setup.** None across tests: one stand-in or real host and one root per test.
- **Teardown.** ≤ 2 s per test (`TestSessionTeardown`, stand-in disposal, best-effort host kill).
- **Phases.** `TestTiming.BeginPhase(ProcessWait, …)` around every sweep, launch and the V-9/V-9b connect.
  Slot wait is reported separately (31-90 s measured today).
- No row or run is near 30 min (R1).

### Checkpoints

Two checkpoint runs:

- **Run L (server2, now)** `--rows CP-4,CP-5,CP-6,CP-17,CP-18,CP-20`: one SessionRunner.Tests build.
- **Run W (Windows lane, not measured)** `--rows CP-7,CP-8,CP-19`: one SessionRunner.Tests build on the
  desktop. Command for the later Windows Code/Test task, from the desktop checkout, **only when no rc
  qualification is active on that host**:
  `dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-11-03-card-1653-S2-test-design.md --rows CP-7,CP-8,CP-19`,
  then `wait` until the exit is not 75.

These rows replace the plan's rows with the same numbers. `Min` is the TUnit executed floor (`-MinExecuted`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-4 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s2l/ | linux-native-socket-dir | /*/Antiphon.SessionRunner.Tests/Card1653SocketDirTests/* | V-8a, V-8, V-8b, V-8c, V-9, V-9b, V-11d, V-13b, V-14, V-14b, V-6b, D-1 (ii)-(v) | class, 0 failed/skipped | 28 | 5 | true |
| CP-5 | S2 | CP-4 | portable-peer-check-linux | /*/Antiphon.SessionRunner.Tests/Card1653PeerCheckTests/* | V-10, V-11, V-11b, V-11c, V-11e, V-11f, V-12, V-13, V-15, V-17, V-18a, V-18b, D-1 (i), (v) | class, 0 failed/skipped | 29 | 2 | true |
| CP-6 | S2 | CP-4 | portable-identity-regression-linux | /*/Antiphon.SessionRunner.Tests/(Card1636AdoptionIdentity*)\|(Card1653RefusalRetainsHost*)\|(Card1653FreshLaunchIdentity*)/* | R-1 | 3 classes, 0 failed/skipped | 17 | 1 | true |
| CP-17 | S2 | CP-4 | portable-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-3 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-18 | S2 | CP-4 | portable-workspace-conflict-regression | /*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders | R-4 | method, 0 failed/skipped | 1 | 1 | true |
| CP-20 | S2 | CP-4 | linux-native-connect-retry-regression | /*/Antiphon.SessionRunner.Tests/Card1619RunnerExitTests/(Relisten_hello_eof*)\|(Eof_every_hello*)\|(Corrupt_hello_frame*)\|(Non_ack_hello*) | R-2 | 4 methods, 0 failed/skipped | 4 | 1 | true |
| CP-7 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s2w/ | windows-native-peer-check | /*/Antiphon.SessionRunner.Tests/Card1653PeerCheckTests/* | V-10, V-11 (overall V-16, M-13), V-13 Windows, V-17, V-18a, V-18b, D-1 (i), (v) on Windows | class, 0 failed/skipped | 29 | 8 | true |
| CP-8 | S2 | CP-7 | windows-native-c1636-regression | /*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* | R-5 (CARD-1636 Windows) | class, 0 failed/skipped | 6 | 1 | true |
| CP-19 | S2 | CP-7 | windows-native-s1-refusal-regression | /*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/(Refused_identity*)\|(Stop_of_a_pending*)\|(Pending_identity_row*)\|(Vanish_sweep*)\|(Non_identity*) | R-5 (S1 no-kill on Windows) | 5 methods, 0 failed/skipped | 7 | 1 | true |

How Min is counted (executions):

- CP-4: V-8a 1 + V-8 1 + V-8b 5 + V-8c 2 + V-9 3 + V-9b 1 + V-11d 1 + V-13b 1 + V-14 1 + V-14b 1 + V-6b 1 = 18.
- CP-5 and CP-7: 9 single methods + V-17 6 + V-18a 8 + V-18b 1 = 24 (the class has no Linux-only method, so
  the Windows count is the same).
- D-1 repair (Review db8d32c0, task 006e0b89) adds class-scoped witnesses, so the filters are unchanged:
  CP-4 gains the post-connect re-check witness 1 and D-1 (ii) 3, (iii) 2 + 1, (iv) 2, (v) file probe 1 = 28;
  CP-5 and CP-7 gain D-1 (i) 3 and (v) peer reader and liveness 2 = 29.
- CP-6: 6 + 8 + 3 = 17, CP-17: 3, CP-20: 4 (all measured at the tip). CP-18: 1 (S1 measured).
  CARD-1672 (not landed) adds one method to `Card1653RefusalRetainsHostTests`; `Min` is a floor, so 17 stays
  valid.
- CP-8: 6 (measured on Linux; the class is portable). CP-19: 7 (operands matched by TRX names at the tip).

Roster and suffix check (grep at `d456b2d91`): no class starts with `Card1653SocketDir`, `Card1653PeerCheck`
or `PtyHostFakeHost`; the new classes diverge after `Card1653` from each other and from the S1 classes.
`PtyHostFakeHost` and `PtyHostStrangerWorld` have no `[Test]`. CP-20 is linux-native because its class is
`[NativeLinuxOnly]`. CP-7's second build of the same project in the same After group is intended (a Windows
build cannot reuse a Linux one); `import` may print the CARD-1493 equivalent-build warning for it, which does
not change the exit code.

**Import verdict (measured, server2, 2026-11-03, one lease, held 39 s):** `dotnet run --project
tools/Antiphon.Checkpoints -- import --plan docs/superpowers/plans/2026-11-03-card-1653-S2-test-design.md
--out .antiphon/c1653-s2-manifest.yaml` → exit 0, `imported 9 rows`. One warning, as expected:
`WARNING CP-7: Build 'bin-c1653-s2w' duplicates 'bin-c1653-s2l' from CP-4 (same project …, After 'S2', Repeat 1)`.
It is kept on purpose: Run W builds on Windows and Run L on Linux, and the two runs never share a build.

### Cost

Labels: **measured** where stated, otherwise estimated. Slot wait (31-90 s per lease today) is reported
separately.

- **Ordinary V/R floor (Code).**
  - Run L: CP-4 5 (build p95 2.3 min, measured 36 s warm, plus about 1 min of class) + CP-5 2 + CP-6 1
    (measured 30 tests in 33 s with CP-17/CP-20 included) + CP-17 1 + CP-18 1 + CP-20 1 (measured 4.8 s)
    = **11 min**.
  - Run W: CP-7 8 + CP-8 1 + CP-19 1 = **10 min** (Windows lane, not measured).
  - Total **21 min** of row time. Savings against the plan: CP-20 measured at 1 min instead of 2 (-1 min);
    V-13 moved into the portable class adds the Windows real-host positive witness at no extra row.
- **Positive-control floor (Mutation)**, method-scoped, one incremental build per red and per restore:
  - 45 Linux PCs at about 2.3 min each (incremental SessionRunner.Tests build of about 60 s twice, plus two
    method runs of about 8 s; the S1 sweep measured 52 mutants in about 110 min with slot waits) = **about
    104 min serial**. Batching across files (`Dir`, `Cli`, `Cred`, `Rdv`, the settings file and the test file
    each pair with one `Rt` PC) saves about 12 cycles × 1 min ≈ 12 min → about 92 min. Three detached shards
    bring the wall time to **about 35 min**.
  - 4 Windows PCs: PC-64, PC-76 and PC-79 all edit `Rt`; PC-81 edits `Cred` and batches with PC-64. Three
    cycles at about 10 min (Windows build p95 unknown) = **about 30 min**, Windows lane, not measured.
  - Setup: one cold build per lane = 2.3 + 8 = **about 10 min**.
- **Total** = setup 10 + V/R 21 + PC 92 + 30 = **about 153 min estimated** (about 96 min wall with three
  Linux Mutation shards).

### Completion and handoff

- guards=49, mapped=49, missing=0, duplicate PC maps=0. All 49 PCs run (45 on server2, 4 on the Windows lane).
  Cost is numeric (estimated, with the measured inputs marked).
- Seams: S-1 (probe into `AdoptAsync`), S-4 (pure policy factory), S-5 (policy observer), S-6
  (`PipeOptionsFor`) and S-7 (request-taking name choice) are **missing** at the tip and are part of the Code
  slice; S-2/S-3 are the plan's own new overrides on an existing pattern. None is unverifiable.

## Open questions for Code (each with a default)

- **Q-1 Fake-host extraction.** Default: move `WriteFakeHostSource`/`FakePeer` to `PtyHostFakeHost.cs` in
  the red commit; `Card1653FreshLaunchIdentityTests` changes only its call sites (CP-6 is the regression).
- **Q-2 Where the refusal lines are logged.** Default: the runtime logs them (the client has no logger) from
  the peer arm and the launch catch; the observe line from `OnObserved`. Exactly one line per refusal.
- **Q-3 V-14 symlinked directory.** Default: runtime B's `PtyHostSocketDir` is a symlink to an owned 0700
  directory, so `EnsureSafe` fails only on the symlink rule.
- **Q-4 `[Timeout]` values.** 10 s until the first green TRX, then per the formula.
