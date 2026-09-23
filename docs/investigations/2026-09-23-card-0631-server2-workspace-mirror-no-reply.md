# CARD-0631 — server2 runner never replies to WorkspaceMirror

Date: 2026-09-23. Stage: Investigate. Status: **root cause confirmed.** It was reproduced four
times in the live runner's own logs and once more by hand inside the container.

## Cause, in one paragraph

The runner **does** receive every WorkspaceMirror request, and every one of them **fails at once**.
The failures are never silent hangs. Two defects combine:

1. **Provisioning (the trigger).** The runner's configured repository,
   `PhoneHome:RunnerRepository = /work/repos/antiphon`, does not exist inside the container. `/work`
   is the named volume `antiphon-runner_work`. `state-init` creates `/work/repos` empty, and nothing
   ever clones the repo into it. `RunnerWorkspaceService.GitAsync` then fails in `Process.Start`
   with `Win32Exception (2)`, which is ENOENT from the chdir into the missing working directory.
2. **Reply contract (the silence).** `Win32Exception` is not in `PhoneHomeCommandDispatcher.DispatchAsync`'s
   catch list, so it escapes the dispatcher. The receive loop's fire-and-forget `Task.Run` catches it,
   logs `WRN Phone-home command WorkspaceMirror failed`, and **writes no frame**. The server's
   `RequestAsync` waited on `Timeout.Infinite` until ea6b603f, and now waits 5 min, for a reply
   that the runner has already decided never to send.

Defect 1 is the reason no server2 Worktree task has ever mirrored. Defect 2 is the reason the
failure showed up as a fleet-wide dispatcher freeze (CARD-0629) and not as an immediate, legible
409.

## Evidence

### Runner side (server2, `antiphon-runner-session-runner-1`)

Image `antiphon-server2/session-testing:6d90c6fcf46e` (label `org.opencontainers.image.revision=6d90c6fc…`).
Created and started 2026-09-23 13:04:21Z. It already contains the CP-6a epoch fix 9acac1c4, so the
epoch-desync frame drop that fix describes is ruled out. It registered successfully at 13:04:35Z.

`docker logs` holds exactly four WorkspaceMirror records, all identical apart from the timestamp (UTC):

```
[14:26:20 WRN] Phone-home command WorkspaceMirror failed
System.ComponentModel.Win32Exception (2): An error occurred trying to start process '/usr/bin/git'
  with working directory '/work/repos/antiphon'. No such file or directory
   at RunnerWorkspaceService.GitAsync(...)            RunnerWorkspaceService.cs:line 185
   at RunnerWorkspaceService.MirrorAsync(...)         RunnerWorkspaceService.cs:line 64
   at PhoneHomeCommandDispatcher...<DispatchAsync>b__1 PhoneHomeCommandDispatcher.cs:line 94
   at PhoneHomeCommandDispatcher.MutateAsync(...)     PhoneHomeCommandDispatcher.cs:line 240
   at PhoneHomeCommandDispatcher.DispatchAsync(...)   PhoneHomeCommandDispatcher.cs:line 90
   at PhoneHomeConnectionService...ReceiveLoopAsync   PhoneHomeConnectionService.cs:line 185
[15:09:15 WRN] Phone-home command WorkspaceMirror failed      (same stack)
[15:12:23 WRN] Phone-home command WorkspaceMirror failed      (same stack)
[15:40:39 WRN] Phone-home command WorkspaceMirror failed      (same stack)
```

The log has no other WorkspaceMirror line: no second attempt, no timeout, and no error-frame send.

Container state, measured live:

| Probe | Result |
|---|---|
| `ls -la /usr/bin/git`; `git --version` | present, 2.39.5 |
| `ls /work`, `/work/repos`, `/work/worktrees` | exist, owned `app` (1654), **both empty** |
| `ls /work/repos/antiphon` | `No such file or directory` |
| `cd /work/repos/antiphon` as 1654 | `can't cd to /work/repos/antiphon`, exit 2 (manual repro of the ENOENT) |
| Mounts | `/work` = volume `antiphon-runner_work`; the host's own `/work/repos/antiphon` (owned `mc`, at 6d90c6fc) is **not** mounted. The matching path names are a coincidence. |
| Env | `PhoneHome__AllowedCwd=/work`; `RunnerRepository` not set, so the default `/work/repos/antiphon` (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:18`) applies |

### Server side (desktop, `C:\src\Antiphon\server\logs\antiphon-20260923.log`, +01:00)

Task 09adcb72 (Grok, Worktree, `-Runner server2`) was created 15:25:16 (line 43329). Each server
start dispatches it again: the worktree is logged, and about 7 s later (push, then mirror) the
runner logs its failure.

| Server log (+01:00) | Event | Runner log (UTC) |
|---|---|---|
| 15:26:13 (l. 43385) | worktree `feat/card-task-09adcb72` @ e992897c | 14:26:20 WorkspaceMirror failed |
| 16:08:45 restart → 16:09:12 (l. 46870) | same | 15:09:15 failed |
| 16:12:10 restart → 16:12:20 (l. 47131) | same | 15:12:23 failed |
| 16:40:22 restart → 16:40:37 (l. 47690) | same | 15:40:39 failed |
| 20:13:15 restart (ea6b603f deployed) | — | — |

After the fourth request the server logged nothing more for this task until the 20:13 restart. That
is the 3.5 h freeze CARD-0629 captured in a dump. The runner's 502 reconnect bursts (15:07, 15:11,
15:39, 19:12) are the desktop restarts, and the runner re-registered after each one. Task 09adcb72
is now `Canceled` (completedAt 19:13:26Z).

### Code path (file:line at 587fd147)

- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:64`: first git call,
  `GitAsync(_repository, ct, "fetch", "origin", branch)`. `WorkingDirectory = _repository`
  (`:171-177`). `Process.Start` (`:185`) throws `Win32Exception` when the directory is missing.
  None of `GitAsync`'s own handling applies, because it only converts the *timeout*
  `OperationCanceledException` (`:195-200`).
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:128-148`: the catch list is
  `PhoneHomeAdmissionException`, `KeyNotFoundException`, `VerificationCustodyException`,
  `GrokRulesLaunchException`, `HerdrLaunchException`, `ArgumentException or
  InvalidOperationException`. `Win32Exception`, `IOException`, `UnauthorizedAccessException`,
  `JsonException` (payload deserialise) and every other type escape it.
- `src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:181-198`: the `Task.Run` catch
  `when (ex is not OperationCanceledException)` logs and **never writes an Error frame**. An OCE
  gets neither a log line nor a frame.
- `server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs:84-126`: since ea6b603f
  the server waits `RequestTimeoutFor(WorkspaceMirror)` = 5 min and then throws
  `phone_home_request_timeout`. `AgentTaskDispatcher.PrepareRemoteWorkspaceAsync` (`:4611`, mirror
  at `:4642`) turns that into a requeue. So today every `-Runner server2` Worktree task burns 5 min
  per dispatch attempt and never launches.

### Nothing in the product provisions the checkout

- `docker/stack/init-state.sh` creates `/work /work/repos /work/worktrees` and chowns them. It
  clones nothing.
- `docker/session-runner-grok/dind-entrypoint.sh` stages secrets, starts dockerd and starts the
  runner. It clones nothing.
- `scripts/c590-remote.sh` `case_deploy_parent` (`:972`) is the production deploy. It builds and
  `compose up`s the runner, then probes secret readability and registration. It never seeds
  `antiphon-runner_work`. The only seeder, `copy_checkout_into_volume` (`:270-291`), writes
  `${CHILD_PROJECT}_work` (the throwaway `c604<run>` child stack, `:24`), **not** `antiphon-runner_work`.
- Plan `docs/superpowers/plans/2026-09-22-card-0604-persistent-runner-dind-plan.md` D-13/D-15
  (`:346`, `:396`) *assumes* `/work/repos/antiphon` exists, and no step ever creates it. CP-6a
  tested registration and epoch agreement. It never tested a real mirror, so the gap went unnoticed.

## Further blockers behind the mirror (verified; not the cause of the silence)

These surface as soon as the mirror succeeds. The card's "prove a real `-Runner server2` Worktree
task gets through end to end" needs all three addressed.

1. **Deploy key not registered on GitHub.** Run as 1654 inside the container:
   `ssh -F /etc/antiphon/ssh_config -T git@github.com` gives `git@ssh.github.com: Permission denied (publickey).`,
   and `git push --dry-run` to `https://github.com/michal-ciechan/Antiphon.git` (rewritten to SSH by
   `pushInsteadOf`) gives `Please make sure you have the correct access rights`. Fetch is anonymous
   HTTPS and works (`ls-remote` returned master 587fd147). A mirrored session can therefore run, but
   its commits cannot reach origin, so settlement sync has nothing to fetch. This is operator
   action: add `deploy_key.pub` (`antiphon-server2-runner`, write) to the repo's deploy keys.
2. **No git identity for uid 1654.** `/home/app/.gitconfig` is absent and `/etc/gitconfig` sets
   none. `git commit` fails with `Author identity unknown … unable to auto-detect email address
   (got 'app@<container>.(none)')`. A remote session's first commit would fail unless the agent
   sets identity itself.
3. **Proven workable once seeded.** As 1654 in the live container:
   `git clone --filter=blob:none --no-checkout https://github.com/michal-ciechan/Antiphon.git`, then
   `fetch origin master`, then `worktree add -B <branch> <path> <sha>`, all succeeded, with HEAD = sha.
   No `safe.directory` complaint appeared, because the uid owns the repo. The scratch copy under
   `/tmp/c631` was deleted afterwards.

## Latent risks noted while reading (unobserved, recorded for the plan)

- **Unsynchronised WebSocket sends.** On the runner, the heartbeat loop (`:207`), the event loop
  (`:219`), the request-limit reply (`:172`) and each reply `Task.Run` (`:188`) all call
  `socket.SendAsync` (`PhoneHomeFraming.cs:63-70`) with no send lock. `ManagedWebSocket` allows
  one outstanding send and throws `InvalidOperationException` on a second. If a reply collides with
  a heartbeat, the throw lands in the `:190` catch, so the reply is dropped silently in exactly the
  same way. This was **not** the cause here, because every failure was the `Win32Exception`, but it
  is a second path to the same symptom.
- **Budget mismatch.** The runner's per-git timeout is 10 min (`RunnerWorkspaceService.cs:32`). The
  server's mirror budget is 5 min. A real git hang would give an error reply the server has already
  stopped waiting for.

## Fix options

| # | Option | Addresses | Notes |
|---|---|---|---|
| A | **Reply on every path.** `DispatchAsync` gets a final `catch (Exception ex) when (ex is not OperationCanceledException)`, and the receive-loop `Task.Run` writes an Error frame (`runner_internal_error`, 500, type plus message) whenever the dispatcher throws, including OCE caused by anything but connection shutdown. | Defect 2, the card's own lead | Small. Makes any future runner fault a fast, legible 5xx on the server rather than a 5-min timeout. |
| B | **Serialise sends.** A per-connection `SemaphoreSlim` around `WriteFrameAsync`. | Latent risk 1 | Small. Without it, A can still lose a reply. |
| C | **Provision the checkout.** The runner (or the entrypoint, as 1654) ensures `RunnerRepository` exists: clone anonymous HTTPS (`--filter=blob:none`) when `.git` is absent. The cheapest runtime form is inside `RunnerWorkspaceService.MirrorAsync`, before the fetch ("ensure repo, then fetch"), so it self-heals after a volume reset. | Defect 1 | Choose the entrypoint (boot cost, fail-fast refusal) or lazy-in-mirror (no boot cost, first mirror slower). Both need egress, which is already proven. |
| D | **Deploy preflight.** `case_deploy_parent` asserts `/work/repos/antiphon/.git` exists in `antiphon-runner_work` (or seeds it) and runs one `git -C … fetch` as 1654 before it reports success. | Defect 1, so this never ships unseen again | Belt and braces with C. |
| E | **Git identity.** Bake `user.name`/`user.email` for the runner into `/etc/gitconfig`, or set them per session in the mirror. | Blocker 2 | Tiny. |
| F | **Operator:** register `antiphon-server2-runner` as a write deploy key. | Blocker 1 | Not code. It needs the user's GitHub access. The plan should carry it as a precondition of the E2E proof. |
| G | Align budgets: runner git timeout below the server mirror budget (for example 4 min against 5). | Latent risk 2 | Optional. |

## Recommendation

Ship **A + B + C (lazy, in `MirrorAsync`) + E** as the code change, add **D** to the deploy
script, and list **F** as an operator precondition of the end-to-end proof. A+B honour the
invariant the card asks for: the runner always answers. C+E make the mirror, and the session's
first commit, actually work. F is the one step Claude cannot do.

### Tests that would go red today

| Test (new) | Project / file | Red today because |
|---|---|---|
| `Dispatch_replies_with_error_frame_when_handler_throws_unexpected_exception` | `Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` | A `RunnerRepository` pointing at a missing dir makes `DispatchAsync(WorkspaceMirror)` **throw** `Win32Exception` when it should return an `Error` frame |
| `Receive_loop_writes_error_frame_when_dispatch_throws` | `Antiphon.SessionRunner.Tests/PhoneHomeConnectionServiceTests.cs` | The `:190` catch logs and sends nothing, so the fake socket sees no frame for the request id |
| `Concurrent_reply_and_heartbeat_never_drop_a_reply` | same | With no send lock, a reply that races a heartbeat send can throw and be dropped (use a fake `WebSocket` that throws on overlapping `SendAsync`) |
| `Mirror_clones_repository_when_absent` | `Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs` | Against a local bare "origin" and a missing `_repository`, `MirrorAsync` throws `Win32Exception` when it should clone and then mirror |
| `Mirror_failure_is_admission_error_not_crash` | same | A git start failure escapes as `Win32Exception` rather than `PhoneHomeAdmissionException` |
| `Gitconfig_sets_runner_identity` | `Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs` | `docker/session-runner-grok/gitconfig` has no `[user]` section |
| `Deploy_parent_seeds_or_verifies_runner_checkout` | `Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | `case_deploy_parent` never references `antiphon-runner_work`/`/work/repos/antiphon` |

The E2E proof, after F and a redeploy, is a real `-Runner server2` Worktree task that reaches
`Working` and whose commit syncs back to the desktop worktree at settlement.

## Not done, noted

- Nothing was redeployed, and no production code was changed. Every in-container probe ran as
  1654 in `/tmp/c631` and was deleted afterwards. No secret was printed. The deploy key's public
  half was not read either.
- Fix idea (one line, per the invariant): add a generic reply-on-throw to the runner dispatch
  path, plus a lazy `git clone` of `RunnerRepository` in `MirrorAsync`.
