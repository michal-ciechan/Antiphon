# CARD-0594 — Linux pty-host launch deadlock (not a mount/visibility problem)

**Status:** Root cause **confirmed** and reproduced live, from first principles, in a clean container. Investigate task `a69b4410`. Card CARD-0594. Related: CARD-0490 (V-7 phone-home live turn), CARD-0038 (Linux/PTY portability).

**Date:** 2026-09-22. Image under test `antiphon-session-runner-grok:1.0.40`, built 2026-09-21T18:39:22Z — i.e. **after** every CARD-0490 pipe fix (`79c37f9be` absolute `/tmp` pipe path, `68f9032d4` no 2s connect slicing, `dfa0ecfa1` no `CurrentUserOnly` on Linux). Runtime inside the image: `Microsoft.NETCore.App 9.0.19`.

Not done, noted (fix ideas only — no Code in this stage): close inherited descriptors above 2 in `PosixProcessSpawner.BecomeSessionLeaderOrExit` (iterate `/proc/self/fd`, or `closefrom(3)`) so the launcher's redirect pipes reach EOF; and dispose the `NamedPipeServerStream` on the cancelled-`WaitForConnection` path in `PtyHostServer.RunAsync` so the socket file is unlinked.

## Verdict

**The container can see the socket perfectly well. There is no mount, namespace, bind-mount or Docker-Desktop-path-translation problem, and `/tmp` is deliberately not bind-mounted at all.** The runner and the pty-host are the *same container*, the *same mount namespace* and the *same uid*; `connect(2)` to the socket succeeds when it is attempted while the host is alive (measured below).

The real defect is a **strict serialization deadlock in the Linux launch path**:

`PtyHostLauncher.LaunchDetachedAsync` cannot return until the *detached pty-host* process dies, because that host still holds duplicate write ends of the runner's redirected stdout/stderr pipes. So the runner only *starts* `NamedPipeClientStream.Connect` after the host has already hit its 30 s launch timeout and exited. It then spends its 15 s connect budget retrying `ECONNREFUSED` against the orphaned socket file of a dead host, and fails.

The two numbers in the card's title — "host waits 30 s with no Launch" and "client times out at 15 s" — are **not concurrent**. They are sequential, and 30 + 15 = the 45 s the launch actually takes.

This is **not** an isolated-container or phone-home problem. It is in platform-independent code (`src/Antiphon.PtyHost/PosixProcessSpawner.cs`, `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs`) and breaks **every** Linux/macOS pty-host launch.

## 1. The evidence the card points at does not exist

`.antiphon/card0490-live-evidence/container-logs.txt` is not on `feat/card-task-c9b6e142`, not on `feat/card-task-1a5f192c`, not on any ref (`git log --all -- '*container-logs*'` is empty), and not anywhere on disk under the Antiphon root (exhaustive `find`, exit 0, no matches). `.antiphon/` is untracked working-tree evidence; those roots survive only as scattered directories. What does survive:

- `worktrees/card-task-c9b6e142/.antiphon/card0490-live-100bc7d69fba/state/session-runner/pty-hosts/logs/460142fba870413d961e4e0677b87a02.log`:
  ```
  2026-09-20T22:53:34.3875035Z [INF] pty-host starting: session 460142fb-…, pipe antiphon-pty-460142fba870413d961e4e0677b87a02, pid 36
  2026-09-20T22:54:04.4355687Z [INF] Host exiting: launch timeout - no Launch received
  ```
  Exactly 30.048 s apart. Note the pipe name is **unrooted** — this run predates `79c37f9be`, so it is not evidence about the current tree.
- `worktrees/card-task-1a5f192c/.antiphon/card0490-live-evidence-v7{,b}/` hold only `blockers.txt` (a QEMU asset-pin blocker) and one `runner-status.json`. No container logs.

So the prior attempt's container-side evidence is gone. Everything below is freshly measured on the current image.

## 2. The mount/visibility question, answered

`docker-compose.runner-grok.yml` bind-mounts exactly four paths: `/work` (ro), `/state`, `/state/grok` (ro), `/state/grok/sessions`, plus the secret file. **`/tmp` is not bind-mounted.** It is the container's own writable layer, and `TMPDIR: /tmp` is set explicitly. `SessionRunner__PtyHostDir: /tmp/antiphon-pty-hosts` is there deliberately (the compose comment says NTFS/9p under `/state` drops Unix execute bits).

The pty-host is a grandchild `Process.Start` of the runner (`PtyHostLauncher.LaunchDetachedAsync` → `Antiphon.PtyHost --spawn` → `PosixProcessSpawner.StartDetached` → `Antiphon.PtyHost --detach`). Every hop is an ordinary `fork`/`exec` in the same container: **same mount namespace, same `/tmp`, same uid.** No namespace is crossed anywhere in the chain. `USER 1654:1654` in `docker/session-runner-grok/Dockerfile` is the aspnet image's `app` user, which is why the card saw "uid app".

**Direct proof that the socket is reachable.** Spawn a host in the image and `connect(2)` from a second process in the same container (`curl --unix-socket` is a real UDS connect):

```
srwxr-xr-x 1 app app 0 Sep 22 13:10 /tmp/antiphon-pty-e78d14b6cd5b45b0be35442bede00cd8
curl: (56) Recv failure: Connection reset by peer        # connect() SUCCEEDED; HTTP bytes then rejected
host log: 13:10:17.7276074 [INF] Runner connected
host log: 13:10:17.7919975 [ERR] Connection dropped: InvalidDataException: Invalid pty-host frame length 542393671.
```

The host accepted the connection and parsed `GET / HTTP/1.1` as a frame length. **Visibility and permissions are fine.** Hypothesis closed.

## 3. The actual mechanism, reproduced end to end

Minimal repro — no Grok, no phone-home, no isolated server, no Postgres, no Windows bind mounts, no `/state`:

```
docker run -d --name c594probe --user 1654:1654 -p 18299:8080 \
  -e TMPDIR=/tmp -e SessionRunner__SessionLogPath=/tmp/state/session-runner \
  -e SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts -e Serilog__LogPath=/tmp/state/runner-logs \
  -e PhoneHome__Enabled=false antiphon-session-runner-grok:1.0.40
curl -X POST :18299/sessions -d '{"sessionId":"…","exe":"/bin/bash","args":[],"env":{},"cwd":"/tmp","cols":120,"rows":30}'
```

Real timestamps, both sides:

| UTC | Side | Event |
|---|---|---|
| 13:10:58.204 | runner | `POST /sessions` |
| 13:10:58.547 | host | `pty-host starting … pipe /tmp/antiphon-pty-33e6e3eb3cb547ce8acdc1baca85c1cd, pid 37` — bind + listen done |
| 13:10:58.5 → 13:11:28.5 | runner | **blocked inside `LaunchDetachedAsync`. No connect is attempted at all.** |
| 13:11:28.571 | host | `Host exiting: launch timeout - no Launch received` (30.024 s) |
| ~13:11:28.8 | runner | `LaunchDetachedAsync` finally returns; `NamedPipeClientStream.Connect` begins — against a dead host |
| 13:11:43.803 | runner | `TimeoutException: Could not connect to pty-host pipe '/tmp/antiphon-pty-33e6…' within 00:00:15` → HTTP 500, `total=45.453s` |

`45.453 − 15.000 = 30.45` — the connect began *after* the host exited, not before. The host bound its socket 0.34 s after the POST and sat unconnected for its whole 30 s window.

### 3.1 Why the launcher blocks: leaked pipe write ends in the detached host

`PtyHostLauncher.LaunchDetachedAsync` starts the intermediary with `RedirectStandardOutput = true` / `RedirectStandardError = true` and then awaits `Task.WhenAll(stdoutTask, stderrTask, exitTask)` where both tasks are `ReadToEndAsync`. A pipe reaches EOF only when **every** writer closes it.

`PosixProcessSpawner.BecomeSessionLeaderOrExit` calls `setsid()` then `dup2(/dev/null, 0|1|2)` — it only fixes descriptors **0, 1 and 2**. The `/proc` fd table of a live detached host, sampled 4 s into its wait, shows the leak (probe run 13:13:28, host pid 21, reader `cat` pid 12):

```
-- 12 : cat
     lr-x------ 0 -> pipe:[18882905]          # the reader
-- 21 : /app/Antiphon.PtyHost --detach --session b68cb15d-… --launch-timeout-sec 25
     lrwx------ 0 -> /dev/null                # dup2 worked
     lrwx------ 1 -> /dev/null                # dup2 worked
     lrwx------ 2 -> /dev/null                # dup2 worked
     l-wx------ 6 -> pipe:[18882905]          # LEAKED write end
     l-wx------ 7 -> pipe:[18882905]          # LEAKED write end
```

Two surviving write ends of the very pipe the launcher is reading, at fds 6 and 7 — one per inherited standard stream, inherited past `dup2` because they are duplicates, not fds 1 and 2. Direct timing of the same shape, with a pipe (not a file) as the redirect target:

```
T0=13:12:27.984                       # intermediary started
19                                    # host pid printed immediately
13:12:28.1299421 [INF] pty-host starting …
T1=13:12:48.167  (pipe reached EOF)   # 20.18 s
13:12:48.1605142 [INF] Host exiting: launch timeout - no Launch received
```

EOF arrived 6 ms after the host died, with `--launch-timeout-sec 20`. The launcher is gated on the host's death, exactly.

### 3.2 Falsifiable prediction, confirmed

If the block really is the host's launch timeout, shortening it must shorten the whole failure by the same amount. Prediction before running: `SessionRunner__PtyHostLaunchTimeoutSec=5` gives ≈ 5 + 15 = 20 s instead of 45 s.

```
POST 13:15:11.432
13:15:11.9202491 [INF] pty-host starting … pid 36
13:15:16.9567172 [INF] Host exiting: launch timeout - no Launch received
HTTP=500 total=20.620407s
DONE 13:15:32.179
```

20.620 s. Confirmed. `PtyHostLaunchTimeoutSec` — a value that should be irrelevant to a healthy launch — linearly controls the failure latency.

### 3.3 Why the late connect then fails

The pty-host's socket file is **not unlinked** when the host exits, so the runner's connect finds a file with nothing listening:

```
--- socket after host exit ---
srwxr-xr-x 1 app app 0 Sep 22 13:15 /tmp/antiphon-pty-7ae4d8f88b1244e193935b85aef0d7f3
--- connect() to the dead socket ---
curl: (7) Failed to connect to localhost port 80 after 0 ms: Couldn't connect to server   # ECONNREFUSED
```

`NamedPipeClientStream.ConnectInternal` treats `ConnectionRefused`/`FileNotFound`/`AddressNotAvailable` as "server not up yet" and silently retries for the full 15 s, then throws `TimeoutException`. That is the exception the card quotes, and it is a *symptom two steps downstream* of the deadlock.

## 4. Why Windows never sees this, and why no test caught it

- `Win32ProcessSpawner.Start` calls `CreateProcessW` with **`bInheritHandles: false`**. The Windows detached host inherits no handles at all, so the runner's redirect pipes close the instant the intermediary exits. The asymmetry is entirely in `PosixProcessSpawner`.
- `tests/Antiphon.PtyHost.Tests/LinuxPtyHostLauncherTests.cs` is the only test that exercises this path, and every method opens with `RequireLinux()` → `SkipTestException` off Linux. The dev/CI machine is Windows, so **the class has never executed.** Its own `Shadow_host_exchanges_bytes_and_exits` passes `launchTimeout: TimeSpan.FromSeconds(60)`; run on Linux today it would block 60 s in `LaunchDetachedAsync` and then fail the same way.

## 5. Scope

Cheap-config fix: **no.** Nothing in `docker-compose.runner-grok.yml`, the Dockerfile, or any mount can change this. The bug is two files of platform-independent C#:

| File | Defect |
|---|---|
| `src/Antiphon.PtyHost/PosixProcessSpawner.cs:44` (`BecomeSessionLeaderOrExit`) | Releases only fds 0/1/2; duplicate write ends of the launcher's redirect pipes survive at higher fds. **Primary.** |
| `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs:86` (`await Task.WhenAll(stdoutTask, stderrTask, exitTask)`) | Waits for stdout/stderr EOF, which the detach contract cannot deliver on POSIX. The pid arrives on the first line; nothing needs full EOF. |
| `src/Antiphon.PtyHost/PtyHostServer.cs:45-49` | `catch (OperationCanceledException) { break; }` leaves the `NamedPipeServerStream` undisposed, so the Unix socket file is never unlinked. Secondary: orphan sockets accumulate in `/tmp`, and a retry of the same session id would fail `bind` with `AddressAlreadyInUse`. |

It is not ballooning: this is a small, well-localised fix with an obvious red test (`LaunchDetachedAsync` must return in well under the host's launch timeout on Linux). What *is* a separate, larger question — and should not be folded into CARD-0594 — is that `Antiphon.PtyHost.Tests`' entire Linux surface is dead on a Windows-only test host, so any fix here is unverifiable by the existing suite without a Linux execution lane (a container test, or the CARD-0490 QEMU lane).

## 6. Remaining uncertainties

- **Exact provenance of fds 6 and 7 is measured, not attributed.** They are unambiguously write ends of the launcher's pipe surviving in the detached host, but whether the duplicate is made by CoreCLR in the intermediary or by the grandchild's own runtime startup before `Main` was not isolated. It does not change the defect or the shape of the fix (release everything above fd 2), only the wording of a code comment.
- Measured on `antiphon-session-runner-grok:1.0.40` under Docker Desktop 29.5.3 / `overlay2` on this Windows desktop. The mechanism is kernel/CLR-level and not Docker-Desktop-specific, but it has not been re-measured on native Linux or on server2.
- The live V-7 turn was not re-run end to end. This investigation shows the launch path fails for *any* Linux session, so V-7 cannot pass until it is fixed; it does not prove V-7 has no *further* blocker behind it.
