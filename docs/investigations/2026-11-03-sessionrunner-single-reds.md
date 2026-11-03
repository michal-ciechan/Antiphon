# SessionRunner.Tests: the seven single Linux reds (CARD-1669 group 3)

Debug task 8a3a40d6, 2026-11-03. Read-only diagnosis; no code changed.

## Re-derivation

The whole `tests/Antiphon.SessionRunner.Tests` project ran at master `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c`
on server2 (native default output, `-p:UseAppHost=false`, one build-slot lease for the build and one for the run).
Result: 1917 total, 123 failed, 1710 passed, 84 skipped (12 min 08 s). By class: RunnerRestartHealth 55,
RunnerRestartPreflightSafety 26, RunnerRestartScriptCompatibility 8, DaemonLogRotation 4 (pwsh.exe/powershell.exe,
another group), CodexWindowsLaunchPolicy 19, CodexCliVersionWindows 4 (Windows policy, another group). The other 7
are the same seven listed in the CARD-1652 Code report (task 39e2eabd), and the assertions match that report's TRX:

| # | Class.Method | Assertion | Kind | Fix size |
|---|---|---|---|---|
| 1 | HerdrGkpLaunchGuardTests.Is_gkp_launch_keys_on_the_wrapper_file_name_only | `IsGkpLaunch(GkpArgs)` should be True but was False | product (separator) | 1 line |
| 2 | HerdrGkpLaunchGuardTests.Require_throws_the_gkp_code_naming_the_session_and_the_missing_facts | should throw HerdrLaunchException but did not | same root as 1 | (same) |
| 3 | HerdrGkpLaunchGuardTests.Missing_chat_proxy_url_warns_but_does_not_refuse | logs should contain the `[Warning]` GROK_CLI_CHAT_PROXY_BASE_URL line | same root as 1 | (same) |
| 4 | HerdrGrokResumeGuardTests.Unavailable_store_is_an_IO_failure_and_does_not_claim_missing_history | should throw IOException but threw HerdrLaunchException | product (Linux errno mapping) | ~3 lines |
| 5 | PtyHostLeakSweepTests.A_host_under_a_registered_root_is_identified_and_a_production_path_is_not | `IsOwnedHost(path, ...)` should be True but was False | test helper (backslash regex) | 1 line |
| 6 | PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout | should throw OperationCanceledException but threw WebSocketException | test (accepted socket not rooted; GC reset) | 3 lines |
| 7 | LinuxRunnerCustodyLifecycleTests.C1614_Installed_helpers_advertise_custody | exit code should be 0 but was 1: `/usr/local/bin/antiphon-custody-kill: line 91: id: unbound variable` | environment (stale server2 image) | image rebuild |

## 1-3 HerdrGkpLaunchGuard: `Path.GetFileName` does not split on `\` off Windows

`src/Antiphon.SessionRunner/HerdrGkpLaunchGuard.cs` `IsGkpLaunch` compares `Path.GetFileName(arg)` with `gkp.ps1`.
On Linux the only separator is `/`, so `Path.GetFileName(@"C:\Users\x\.local\bin\gkp.ps1")` returns the whole string.
This was verified in pwsh on .NET 9.0.10, which printed `C:\Users\x\.local\bin\gkp.ps1`. `IsGkpLaunch` is therefore
false. Then:
- 1 fails directly.
- In 2, `Require` returns early ("not a gkp launch"), so nothing throws.
- In 3, `Require` returns before the chat-proxy warning.

`Fully_routed_env...` and the `MissingRequirements` tests stay green because they never call `IsGkpLaunch` on a path
that would refuse.

Verdict: a PRODUCT defect, exposed by Windows-shaped test input. The guard is a fail-safe keyed on a file name, and
its result must not depend on which OS the runner happens to run. `HerdrPaneChild.cs:306` calls it with the
server-authored argv. A Windows-style wrapper path that reaches a Linux runner silently skips the refusal.

Smallest fix (one line in `IsGkpLaunch`). Take the file name after the last `/` or `\`:
`fileName = arg[(arg.LastIndexOfAny(['/', '\\']) + 1)..];`. This drops the `Path.GetFileName` try/catch, which existed
only for the ArgumentException on invalid characters. The test needs no change. It already covers the Windows path,
the bare `GKP.PS1` and the `.bak` negative. Optionally add a `/home/x/.local/bin/gkp.ps1` case.

## 4 HerdrGrokResumeGuard: ENOTDIR maps to DirectoryNotFoundException on Unix

The test writes a regular file at `<GROK_HOME>/sessions` and expects the guard to report storage Unavailable
(IOException), not missing history. `GrokNativeSessionStore.Probe` (`src/Antiphon.Agents.Pty/GrokNativeSessionStore.cs:10`)
calls `Directory.GetDirectories(root)`. The two platforms throw different exceptions:
- Windows: ERROR_DIRECTORY gives IOException, so the result is `Unavailable`.
- Linux: ENOTDIR gives DirectoryNotFoundException. This was verified in pwsh: `GetDirectories(<file>)` throws
  `System.IO.DirectoryNotFoundException`.

The `catch (DirectoryNotFoundException) { return Missing; }` therefore returns Missing. The guard then throws
HerdrLaunchException `GrokNativeSessionMissing` ("native history is missing").

Verdict: a PRODUCT defect on Linux. `SessionRunnerRuntime.cs:617` runs this guard for every non-herdr Grok launch,
which is the server2 grok-linux path. A store that cannot be read is reported as missing history. That is exactly the
false claim the CARD-0383 test forbids, because callers treat Missing as "only a fresh start helps".

Smallest fix in `Probe`: hoist `root` above the `try`, and make the outer catch
`catch (DirectoryNotFoundException) when (!File.Exists(root)) { return Missing; }`. Alternatively, keep the catch and
return `Unavailable` when `File.Exists(root)`. Any non-directory `sessions` entry then falls through to the existing
IOException arm and gives `Unavailable` on both OSes. A file as GROK_HOME itself also gives ENOTDIR on Linux; the
same guard on `root` covers it. The test needs no change.

## 5 PtyHostLeakSweep.RootFromHostPath: Windows-only regex

`tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs` `RootFromHostPath` matches
`^(.*\\antiphon-(...)-[0-9a-f]{32})\\pty-hosts\\bin\\`. On Linux the path from `Path.Combine` uses `/`, so the
regex never matches, `root` is null and `IsOwnedHost` is false.

Verdict: a TEST-helper defect, a Windows assumption. The `[After(Assembly)]` sweep itself returns early off Windows,
so only the unit test exercises the helper on Linux.

Smallest fix: make the three separators `[\\/]` in the regex:
`^(.*[\\/]antiphon-(?:...)-[0-9a-f]{32})[\\/]pty-hosts[\\/]bin[\\/]`. The production-path negative,
`C:\logs\...`, still fails the temp-prefix check. The alternative, `Skip.Test` off Windows, is weaker because it
hides a portable helper.

Separate observation, not a fix: the sweep does nothing on Linux, so leaked Linux pty-hosts are not swept. Its regex
prefix list (7 names) also trails `KnownPrefixes` (27 names). Both belong to a separate card if wanted.

## 6 PhoneHome connect timeout: the accepted socket is garbage-collected and resets the connection

The test discards the accept task (`_ = listener.AcceptTcpClientAsync();`). Once the WebSocket client connects,
nothing roots the accepted `TcpClient`/`Socket`. A GC during the 1 s window finalises the socket's SafeHandle and
closes the fd while the HTTP upgrade request is still unread. The kernel sends RST. The client then sees
`HttpRequestException` / `IOException: Connection reset by peer`. `ClientWebSocket.ConnectAsync` wraps any
non-cancellation fault as `WebSocketException("Unable to connect to the remote server")`.

Evidence, from pwsh on .NET 9.0.10 with the same shape: a 1 s CancelAfter and a hanging loopback listener.
- Without GC: `TaskCanceledException` after 1005-1048 ms. That is the expected OCE, so the product is correct.
- With `GC.Collect(); WaitForPendingFinalizers()` 300 ms in: `WebSocketException: Unable to connect to the remote
  server`, then `HttpRequestException`, then `IOException: Connection reset by peer`. That is exactly the red.

The test is red in whole-project runs, where GC pressure from the other tests is constant. It is not specific to
Linux; it depends on timing.

Verdict: a TEST defect (the fixture lifetime). The product's connect-timeout path is correct.

Smallest fix: keep and dispose the accepted client.
`var accepted = listener.AcceptTcpClientAsync();` ...
`finally { listener.Stop(); if (accepted.IsCompletedSuccessfully) accepted.Result.Dispose(); }`.
The local is used after the await, so the async state machine holds it until the assertion has run.

## 7 C1614_Installed_helpers_advertise_custody: the server2 runner image is stale

The installed `/usr/local/bin/antiphon-custody-kill` (root, 2026-09-23) is the CARD-0604 version. Line 91 is
`local id="$1" pids_dir="$V1_PIDS_ROOT/$id" ...`. Bash expands every word of a `local` declaration before it assigns
any of them, so under `set -u` `$id` is unbound (reproduced: `set -u; f(){ local id="$1" d="x/$id"; }` gives
`id: unbound variable`). The host is cgroup v1 (`/proc/1/cgroup` lists `pids:` and `devices:`), so `--probe` takes
the `kill_v1` path.

The repository fixed this in `846ab147b` ("split helper locals", CARD-1614, 2026-10-29). Current
`docker/session-runner-grok/antiphon-custody-kill.sh` has `local id="$1"` on its own line. The installed copy differs
from the repository copy throughout: it has no common library, no namespace and no freezer refusal.
`/usr/local/lib/antiphon-custody-common.sh` does not exist on this host either; the current Dockerfile line 248
installs it. `sudo -n antiphon-custody-enter --probe` exits 0 because the old enter has no such line.

Verdict: neither a test nor a product defect. The server2 session-runner image predates the CARD-1614 helpers. The
test correctly reports that the installed custody helpers do not work.

Fix:
- An operator rebuilds and redeploys the server2 session-runner-grok image from a tip containing `846ab147b`
  (and `2ca4d8162`), through the staged rollout.
- Optional test hardening (a Code task, small): extend `RequireHelpers` to require
  `/usr/local/lib/antiphon-custody-common.sh`. When the binaries exist without it, fail with a message such as
  "installed custody helpers predate CARD-1614 (no common library); rebuild the runner image" instead of the bash
  error. This should stay a failure, not a skip: a stale image is a real defect, and a skip would hide it.

## Proposed Code tasks

1. HerdrGkpLaunchGuard separator fix (product). Rows 1-3 turn green. Red witness: the existing three tests on Linux.
2. GrokNativeSessionStore.Probe ENOTDIR as Unavailable (product). Row 4 turns green. Red witness: the existing test on
   Linux.
3. Test-only portability: the PtyHostLeakSweep regex `[\\/]` (row 5) and the PhoneHome accepted-client lifetime
   (row 6). Row 6 can go red only through GC timing; the pwsh probe above is the deterministic red.
4. Operations: rebuild the server2 runner image (row 7); optionally the `RequireHelpers` staleness message.

Tasks 1-3 touch disjoint files and can be one Code task (about 10 lines total) or three.
