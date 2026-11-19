# desktop-win call-home gap (plan, 2026-11-19)

Investigate task `5d81b5f8`. Read-only except this plan. Base: master
`c84185e9a424a4cc74011c7d3884e382d3bf9992`. No test, no deploy, no restart, no
card move, no database connection, no secret read.

Live reads, 2026-11-19, against the production API (task token, no secret printed):

- `GET /api/version` = `997f9b8d9ffc4ba870d2fd56caf7c03781591624`. That commit is an
  ancestor of this master. This master is 142 commits ahead of it.
- `GET /api/session-runners`: `desktop` windows, available, eligible, capacity 2,
  occupied 0; `server2` linux, available, eligible, capacity 10, occupied 6;
  `server2-temp` not eligible, draining.
- `GET /api/session-runners/desktop-win/status` = **404**. The id is not in the
  running map.
- `GET /api/runner-defaults`: revision 2, `globalRunnerId=server2`, no kind defaults.

`997f9b8d9` has `scripts/start-windows-phone-home-runner.ps1` and does not have
`scripts/lib/windows-phone-home-env.ps1`. Its `docker-compose.server2-server.yml`
has no `desktop-win` lines (this master has 15). Its platform scan does not check
`AllowsDelegatedTasks` (`AgentTaskService.cs` at that commit, the `foreach` at
the same shape as `:2209` without the filter this master has at `:2213`). It has
no `HostLoadHistory:DirectRunnerKey`. Its runner Dockerfile pins Claude Code
**2.1.280**. This master's Dockerfile pins **2.1.327**. Its runner compose sets
`SessionRunner__HostStats__HistoryEnabled: "false"` and has no `HistoryPath` or
`SourceEpoch`. Pin commit `1285ab3ee` is on this master and is not an ancestor of
`997f9b8d9`.

Board, same read: CARD-1744 InProgress, open blockers CARD-1406 (Review) and
CARD-1778 (Review). CARD-1751 InProgress, platform Linux, its blocker CARD-1742
is Done. CARD-1893 Review. CARD-0490, CARD-0604, CARD-0679 and CARD-0729 are Done.
`card.ps1 search 'phone-home' -Board Antiphon -All` returned 87 cards; the open
ones that change this plan are named below. The two investigation files named in
the brief are not on this master (`docs/investigations/2026-11-18-server-to-server2-docker-plan.md`,
`docs/investigations/2026-11-18-card-1744-blocker-card-state.md`). UNOBSERVED here.
The S4/S5 plan cites them on other branches.

## Verdict

Phase 1 can be started against the **current** AppHost before the server moves.
The running server already accepts a second phone-home id (server2 is connected)
and already has the Windows start script. What it does not have is a `desktop-win`
map entry: status is 404. S4 supervision is on this master and is **not** on the
running checkout, so phase 1 starts from a separate clone of a tip that contains
`windows-phone-home-env.ps1`, on port 17214. Unpinned Windows work stays on the
direct desktop runner until cutover. Real isolation (AppHost stopped, `desktop-win`
on 17204, server in Docker on server2) is phase 2 and waits for the window.

Two server2 slices are ready to dispatch now. Neither touches the desktop.

- **DW-1.** The Windows phone-home env must set host-load history off. The runner
  default is on, and a null path spools under the user profile.
- **DW-2.** `docs/docker-stack.md` still says this master does not implement the
  S4 guard. This master does. An operator reading it will start the wrong script.

## 1. What a phone-home runner does today

server2 is the live instance. The Windows helper
(`scripts/lib/windows-phone-home-env.ps1`) builds a subset of the same env.
"Holds" means the same code path accepts a Windows runner. "Unproven" means no
live Windows phone-home session has been read in this task.

| Step | server2, with file:line | Windows |
|---|---|---|
| Register | Runner `POST /api/session-runners/register` with the secret header and a body that includes platform `linux` or `windows`, capacity, store id, boot id and capabilities (`PhoneHomeConnectionService.cs:115-137`, `PhoneHomeContracts.cs:10`). Server rejects a disabled flag, a bad secret, an unknown id, a bad version, a retired id, capacity outside 1..`MaxCapacity`, a store or boot conflict (`SessionRunnerEndpoints.cs:50-61`, `PhoneHomeRunnerDirectory.cs:267-324`). | Holds in code: platform string is `windows` when the process is not Linux (`:132`). Drive-rooted paths validate (`PhoneHomeRunnerSettings.cs:390-407`, `PhoneHomeSettings.cs:152-165`). **Unproven live.** `desktop-win` is 404 today, so nothing has registered. |
| Epoch and lease | The server mints the epoch in `Register` and returns it on the ticket (`PhoneHomeRunnerDirectory.cs:327-352`). The runner uses that epoch, not its own counter (`PhoneHomeConnectionService.cs:160-166`). Lease default 90s (`PhoneHomeContracts.cs:21`). A competing boot cannot replace an unexpired owner (`:312-323`). Connect presents the ticket (`:143-153`). | Holds. Same socket. **Unproven** across a desktop sleep or a hairpin drop. |
| Capacity | Runner sends `PhoneHome__Capacity`. Server caps it at the entry `MaxCapacity` (`:279`). Occupancy is live or occupied sessions only. `PUT /api/session-runners/{id}/capacity` pushes a new value (`SessionRunnerEndpoints.cs:80-115`). server2 compose is 10 (`docker-compose.server2-runner.yml:64`). | Holds. Phase 1 capacity is 1, so `MaxCapacity` on the desktop entry must be at least 1. The production compose ceiling is 2 (`docker-compose.server2-server.yml:79`). |
| Dispatch | Create-time scan: desktop first, then phone-home ids in ordinal order (`AgentTaskService.cs:2195-2232`). This master skips an id unless it is runner-bound and `AllowsDelegatedTasks` (`:2213`). The running rc does not have that line. A pin is `delegate.ps1 -Runner <id>` (`scripts/delegate.ps1:168`, `:1283`) plus `-Platform` (`:177`, `:1289`). Global default is server2. Excluded shapes (ReadOnly, Shared, existing process, specialists, non-Claude orchestrators, Codex SourceLanding) are offered the desktop only (`:2145-2158`). | Holds for a **pinned** `-Runner desktop-win -Platform Windows` Worktree Grok or Claude task, once the id is eligible. An unpinned Windows task selects `desktop` while that row is windows and eligible, which it is today. After cutover, `local` is the Linux sidecar and the scan selects `desktop-win` (`docs/superpowers/plans/2026-11-18-card-1744-blockers-g1-g3-plan.md` D-1). The running server lacks the S2 filter; a pinned task does not use the scan. |
| Session start | `PhoneHomeOperation.Launch` (5) and `LaunchPlatformConstrained` (26) (`PhoneHomeContracts.cs:50`, `:99`). The server projects `GROK_HOME`, `CLAUDE_CONFIG_DIR`, and for Codex `CODEX_HOME`, from the runner entry, forces PtyHost, and sets Herdr null (`PhoneHomeLaunchPolicy.cs:247-271`). Bare `grok`/`claude`/`codex` are rewritten to `PhoneHome:Executables` on Windows (`PhoneHomeCommandDispatcher.cs:725-731`, `:932-944`). Codex `.cmd` is then rewritten by `CodexWindowsLaunchPolicy.Apply` (`SessionRunnerRuntime.cs:676`). | Holds in code if the helper's executable map and the server's child homes name the desktop installs. The helper fills the map from `Get-Command` (`autostart-session-runner.ps1:84-88`). **Unproven** that those paths are the installs the direct runner uses today. |
| Transcript, ledger, delivery | Events ride the socket (`PhoneHomeConnectionService.cs:187-202`). Transcript pull is operation 8 (`PhoneHomeContracts.cs:53`). The tailer prefers the launch env `CLAUDE_CONFIG_DIR`, then the process env (`TranscriptTailer.cs:1518-1526`). The launch env is set from `ChildClaudeHome`. The server ledger and delivery stay on the server. `GET /api/sessions/{id}/transcript` is unchanged (`docs/ops-http.md`). | Holds for a projected launch. **Unproven** end to end on Windows. |
| Mirror, push, land | The server pushes the branch to origin, then `WorkspaceMirror` (14) clones it on the runner (`PhoneHomeContracts.cs:60-65`, `docs/orchestration-loop.md` remote-prep push). Fetches are anonymous HTTPS (`PhoneHomeSettings.cs:36`). Settlement `WorkspacePublish` (32) asks the runner to fast-forward the mirror, then the server re-reads origin (`docs/ops-http.md` runner workspace paragraph). Git children set `GIT_TERMINAL_PROMPT=0` (`RunnerWorkspaceService.cs:1072-1074`). server2's push credential is the deploy key, git identity file and GitHub token directory (`docker-compose.server2-runner.yml:102-150`). The Windows helper sets none of those. Non-inspection git does not clear `credential.helper`. | Clone holds (anonymous HTTPS). **Push is unproven.** There is no Windows deploy key. A push uses whatever helper the interactive user already has, and it cannot prompt. The phase 1 land is the witness. Do not dispatch a credential slice before that witness fails. |
| Build slots | The runner serves `/build-slots` on its own port (`Program.cs` maps it in every mode; G1-G3 T-8). Windows `build-slot.ps1` defaults to `http://localhost:17204/build-slots`. Nothing injects `ANTIPHON_BUILD_SLOTS_URL` into children. | Holds by that default. Phase 1 children share the direct runner's broker. Phase 2, `desktop-win` is that broker. server2's separate `build-slots` service is not copied. |
| Host load | Sampler default on. History default **on** (`HostStatsSettings.cs:11`, `:29`). A null `HistoryPath` spools under LocalApplicationData or the user profile (`HostLoadCaptureService.cs:99-107`). server2 sets an explicit path, physical host, revision, epoch and authority, and advertises history (`docker-compose.server2-runner.yml:79-87`). The production server compose sets `HostLoadHistory__DirectRunnerKey: server2-direct` (`docker-compose.server2-server.yml:85`) so the sidecar does not reuse the desktop cursor. The running server has neither the key nor the runner binding. | **Gap.** The Windows helper does not set `HistoryEnabled` or `HistoryPath`, so a phase 1 process would spool into the user profile and advertise `HostLoadHistoryV1`. DW-1 turns history off until CARD-1893 is the live server. |
| Workspace slots | Flag default false. server2 compose defaults false (`docker-compose.server2-runner.yml:65`). The runner advertises `workspaceSlotV1` only when the flag is on (`PhoneHomeCommandDispatcher.cs:167-172`). | Holds as off. Not required for the first real task. |
| Retention | Constructed only when phone-home is on and the process is Linux (`Program.cs:82-99`). The server job's allow-list is `server2`. | Proven absent. `C:\Antiphon\runner\work\worktrees` grows until someone clears it. Does not block phase 1. |
| ConPTY | The modern backend ships and defaults off (`docs/adr/0002-modern-conpty-backend.md`). server2 sets `inbox` (`docker-compose.server2-runner.yml:105-106`). The Windows helper does not set `SessionRunner__PtyBackend` or `ANTIPHON_PTY_BACKEND`. | Agent sessions use the inbox default unless the process already has the flag. **Unproven** which flag the live direct runner exports. Windows Full tests spawn ConPTY themselves inside the qualification checkout. They do not use the runner's pty backend. |
| CLI auth | server2 probes Claude and Codex against `/state/claude` and `/state/codex`. Grok probe is presence of `PhoneHome:GrokHome/auth.json`, never the file contents (`GrokAuthProbe.cs:35-59`). Launch homes come from the server entry, not from the runner process env. | Holds if `Child*Home` equals the helper's `$HOME\.claude`, `.codex`, `.grok` (`windows-phone-home-env.ps1:273-275`). **Unproven** that those directories contain a sign-in. The probe returns logged-out when the file is absent; it does not print the file. |
| Git credentials | server2: deploy key, `GIT_CONFIG_GLOBAL`, token dir. Windows helper: none. | **Unproven.** See the push row. |
| Long paths | The rc checkout sets `core.longpaths=true` in its own clone (`docs/release-gates.md` Test). The runner's lazy clone command does not (`RunnerWorkspaceService.cs:538`). | Qualification long paths hold on the nightly checkout. **Unproven** for a `desktop-win` task worktree. |
| Antivirus | No exclusion in this repo. | **Unproven.** A phase 1 build stall is an operator exclusion, not a code slice, until a witness names a path. |

Custody: a Windows phone-home runner advertises `windows-job-v1` (`VerificationCustody.cs:26-28`, `docs/docker-stack.md` native section). A remote SourceLanding Mutation on that runner has not been run. Keep Mutation off `desktop-win`.

## 2. Gap list

Sizes are delegate-days of code, not the operator window. "server2" means a
delegate on this runner can land it. "desktop" means a person at the machine.
"operator" means a secret, a host file, or a go/no-go.

| Gap | Where | Size | Risk if skipped | Who |
|---|---|---|---|---|
| No `desktop-win` map on the live server | User-secrets `antiphon-server` (`server/Antiphon.Server.csproj:8`). Status 404. | none (config) | Runner cannot register. | operator |
| Shared secret file | `C:\Antiphon\runner\secrets\phone-home`. Same bytes as `PhoneHomeRunner:Runners:desktop-win:SharedSecret`. Never printed. | none | Register 403. | operator |
| Hairpin | Runner origin must be `https` with no path (`windows-phone-home-env.ps1:177-191`). Phase 1 dials the public origin back to this desktop. | none | Register never connects. `disconnectReason` on status. | operator witness. No code change in DW-1. |
| S4 not on the running checkout | `997f9b8d9` has no helper and no `-PhoneHomeConfigPath`. | none | Starting phase 1 from `C:\src\Antiphon` cannot use the S4 path. | operator clones `C:\Antiphon\runner\app` from this master or later |
| History spool | `HostStatsSettings.cs:29`, `HostLoadCaptureService.cs:99-107`, helper omits the key | small | A profile spool and a history capability the live server does not bind. | server2, DW-1 |
| Stale runbook sentences | `docs/docker-stack.md` native section says master `a789c877` has no guard and no parameter. This master has both (`restart-apphost.ps1:103-110`, `dev-aspire.ps1` sets the same path, `autostart-session-runner.ps1:17-23`). | small | Operator uses the unsupervised fallback, or deletes the wrong file. | server2, DW-2 |
| S2 scan not on the live server | rc `SelectPlatformCandidateAsync` has no `AllowsDelegatedTasks` check. This master has it at `AgentTaskService.cs:2213`. | already on master | Unpinned routing after cutover is wrong until the next rc is activated. Pinned phase 1 tasks do not need it. | rides the next rc. No new slice. |
| S3 `DirectRunnerKey` not on the live server | Absent at `997f9b8d9`. Present on this master (`docker-compose.server2-server.yml:85`). | already on master | Needed when the sidecar replaces `local`, not for phase 1. | rides the next rc |
| Production compose `desktop-win` not activated | On this master (`docker-compose.server2-server.yml:70-84`). Not on `997f9b8d9`. | already on master | The server2 server, once started, has no Windows pool until this compose is what it runs. | rides the next rc, then the window |
| Push from the mirror | No Windows deploy key. `RunnerWorkspaceService.cs:1072-1074`. | unknown until the land | A phase 1 land fails closed with no prompt. | operator witness first. Delegate only if it fails. |
| CLI sign-in in the phone-home homes | `Child*Home` must match `$HOME` | none if the operator pairs them | Probe says logged out and the launch is refused. | operator |
| ConPTY flag | Helper does not set it | none for tests | Agent sessions stay on inbox unless the process inherited `modern`. | unproven. Not a slice. |
| Long paths on the runner clone | `RunnerWorkspaceService.cs:538` | small if a witness fails | A task worktree, not the rc checkout. | not a slice until a witness |
| Antivirus | none in repo | none | Build stalls. | operator, after a witness |
| No Windows retention | `Program.cs:89` | medium | Disk growth. | file a card after phase 1 shows the rate. Not now. |
| CARD-1751 metadata probe | Staging container reads the existing broker. No produce, no join. Card InProgress. Blocker CARD-1742 is Done. | operator on the host | Blocks the channel-bridge cutover (CARD-1752), not `desktop-win` registration. | operator. Not a desktop-win slice. |
| CARD-1778 agent re-home | Standing agents whose seat is the desktop. Plan: `docs/superpowers/plans/2026-11-08-card-1778-rehome-agents-to-sidecar.md`. Card Review, and it is an open blocker of CARD-1744. | operator at the window | Herdr sessions cannot move onto `desktop-win`: projection sets Herdr null (`PhoneHomeLaunchPolicy.cs:271`) and the helper sets Herdr false (`windows-phone-home-env.ps1:79`). They stay on the direct runner through phase 1. At the window they are re-homed to the sidecar as PtyHost, or they stop. | operator, inside the window. Not phase 1. |
| CARD-1406 | Still an unresolved blocker, status Review. | UNOBSERVED | This session did not open Postgres. The brief calls the move done. The board does not. | operator confirms before the window. Not before phase 1. |
| How the orchestrator targets it | `delegate.ps1 -Runner desktop-win -Platform Windows`, Worktree, Grok or Claude. | none | An unpinned `-Platform Windows` task hits `desktop` during phase 1. | orchestrator, pinned tasks only until cutover |
| G-2 after cutover | Scan selects the first eligible Windows phone-home id. With `AllowDelegatedTasks` true and no standing agent, that id is `desktop-win`. The live server lacks the filter; this master has it. | rides the rc | A runner that does not admit delegates could be selected by the old scan. Phase 1 avoids that by pinning. | next rc activation |
| Windows-only tests and the Windows Full run | `scripts/qualification/` has no `/api`, no `17202`, no `ANTIPHON_API`. `launch-rc.ps1` reads a local JSON record, checks out a local HEAD, and wraps `nightly-run.ps1` in one local `build-slot.ps1` lease, label `rc-qualification` unless the record overrides it (`launch-rc.ps1:23-65`). State root default `C:\Antiphon\nightly`. | none | The supervisor keeps reading local files when the server moves. It does not follow the server. It stays on the desktop. The launch stays local. | orchestrator: do not put the Full run on `desktop-win`. Switch 17204 only between Full runs, after `GET http://localhost:17204/build-slots` shows no `rc-qualification` lease. |
| AppHost on the desktop | Phase 1: it stays. It is the server being dialed. Phase 2 and the window: it stops. End state is the maintenance marker `logs/apphost.down-on-purpose`, logon task `Antiphon AppHost` disabled, watchdog disabled (`docs/docker-stack.md` rollback). `restart-apphost.ps1` refuses with exit 3 `REFUSED: phone-home-config-present` while `C:\Antiphon\runner\phone-home.json` exists (`:103-110`). The running checkout does not have that guard. Do not create that file during phase 1. | none | Creating the phase 2 file on a pre-S4 checkout does nothing to AppHost and makes the next S4 checkout refuse to start. | operator |
| Credentials | Names only. Desktop user-secrets `PhoneHomeRunner:Runners:desktop-win:SharedSecret`. File `C:\Antiphon\runner\secrets\phone-home`. Later, host env `PHONE_HOME_DESKTOP_WIN_SHARED_SECRET` and `PHONE_HOME_DESKTOP_WIN_HOME` for the server2 server. Claude and Codex homes are directories, not tokens. Grok is `auth.json` presence. | operator | A printed secret is a rotation. | operator. Delegates never read the files. |

## 3. Phase 1, against the current AppHost

Yes, with what is on this master, plus one operator secret entry the running
server does not have. S4 does not need to be the running AppHost. It needs to
be the clone that executes the runner. DW-1 should land before that clone is
cut, so the first process does not open a history spool. If the operator starts
before DW-1 lands, use the fallback start script and accept the spool until the
process is restarted from a DW-1 tip.

### Operator, secrets first

1. Write the shared secret once, without display, to
   `C:\Antiphon\runner\secrets\phone-home`. Owner-only file. Do not commit it.
2. In the desktop user-secrets store `antiphon-server`
   (`server/Antiphon.Server.csproj:8`), set these keys. The value of
   `SharedSecret` is the file from step 1. Do not echo it. Do not put a
   `secret` key in any json file (the helper refuses unknown keys,
   `windows-phone-home-env.ps1:143-152`).

| Key | Value |
|---|---|
| `PhoneHomeRunner:Runners:desktop-win:Enabled` | `true` |
| `PhoneHomeRunner:Runners:desktop-win:AllowDelegatedTasks` | `true` |
| `PhoneHomeRunner:Runners:desktop-win:DisplayName` | `desktop-win` |
| `PhoneHomeRunner:Runners:desktop-win:HostWorkspaceRoot` | `C:\src\Antiphon` |
| `PhoneHomeRunner:Runners:desktop-win:RunnerWorkspace` | `C:/Antiphon/runner/work` |
| `PhoneHomeRunner:Runners:desktop-win:RunnerRepository` | `C:/Antiphon/runner/work/repos/antiphon` |
| `PhoneHomeRunner:Runners:desktop-win:MaxCapacity` | `1` |
| `PhoneHomeRunner:Runners:desktop-win:RawExeAllowList:0` | `C:/Program Files/PowerShell/7/pwsh.exe` |
| `PhoneHomeRunner:Runners:desktop-win:CallbackOrigin` | `https://antiphon.desktop.codeperf.net` |
| `PhoneHomeRunner:Runners:desktop-win:SharedSecret` | the file from step 1, not printed |
| `PhoneHomeRunner:Runners:desktop-win:ChildClaudeHome` | the interactive profile plus `/.claude` |
| `PhoneHomeRunner:Runners:desktop-win:ChildCodexHome` | the same profile plus `/.codex` |
| `PhoneHomeRunner:Runners:desktop-win:ChildGrokHome` | the same profile plus `/.grok` |
| `PhoneHomeRunner:Runners:desktop-win:ClaudeAuthProbeEnabled` | `true` |
| `PhoneHomeRunner:Runners:desktop-win:CodexAuthProbeEnabled` | `true` |

Leave `StandingAgentId` unset. The validator rejects an enabled entry that has
neither a standing agent nor `AllowDelegatedTasks` (`PhoneHomeRunnerSettings.cs:386-387`).
The three child homes must be the same directories `autostart` will derive from
`$HOME` (`windows-phone-home-env.ps1:273-275`).

### Orchestrator, after the operator says the entry is saved

3. On the desktop, `GET http://localhost:17204/build-slots`. If a lease is
   labelled `rc-qualification`, wait. AppHost restart does not own port 17204
   (`restart-apphost.ps1:122-125`), but do not overlap a Full run.
4. From `C:\src\Antiphon`, `scripts/restart-apphost.ps1 -ExpectedServerSha 997f9b8d9ffc4ba870d2fd56caf7c03781591624`.
   That is the running tip. User-secrets load at process start. Check the
   restart and launch locks first.
5. `GET /api/session-runners` must list `desktop-win`. Status 200 with
   `available: false` is the configured-but-not-connected row (CARD-0729).
   Status 404 means the entry did not bind. Do not start the runner on a 404.

### Operator, the process

6. Do not create `C:\Antiphon\runner\phone-home.json`. That file is phase 2.
7. Clone `C:\Antiphon\runner\app` at a tip that contains
   `scripts/lib/windows-phone-home-env.ps1`. This master qualifies. The running
   rc does not.
8. Write `C:\Antiphon\runner\phone-home.phase1.json`, ASCII, no secret:

```json
{"runnerId":"desktop-win","serverOrigin":"https://antiphon.desktop.codeperf.net","capacity":1,"port":17214,"root":"C:\\Antiphon\\runner"}
```

9. From a normal terminal, not an agent Job Object:

```text
pwsh -NoProfile -File C:\Antiphon\runner\app\scripts\autostart-session-runner.ps1 -PhoneHomeConfigPath C:\Antiphon\runner\phone-home.phase1.json
```

Fallback, only if that clone has no `-PhoneHomeConfigPath`: the start script
defaults capacity to 2 (`start-windows-phone-home-runner.ps1:34`), which a
`MaxCapacity` of 1 would refuse, so pass `-Capacity 1`. It has no crash
relaunch. It refuses to run off Windows (`:40`).

```text
pwsh -NoProfile -File C:\Antiphon\runner\app\scripts\start-windows-phone-home-runner.ps1 -RunnerId desktop-win -Port 17214 -Capacity 1
```

### Witness

Read-only. A server2 delegate can run it. Pass: HTTP 200,
`dispatchEligible: true`, `platform: windows`. Also confirm `desktop` is still
windows, eligible, capacity 2.

```text
GET /api/session-runners/desktop-win/status
GET /api/session-runners
```

Then the orchestrator dispatches one pinned Worktree task, then a second after
the first settles: `-Runner desktop-win -Platform Windows`, Grok or Claude,
capacity 1. One of them must land. A failed push is the credential witness.
Not a Full run. Not an unpinned Windows task. Not Mutation.

### Rollback

Stop the 17214 supervisor from the terminal that started it. Do not run
`restart-session-runner.ps1` from `C:\src\Antiphon` to kill it: that script
kills the main checkout's `Antiphon.SessionRunner.exe`, and phase 1 must not
be that executable (G1-G3 T-9). Remove the `desktop-win` user-secrets entry
and restart AppHost so the catalogue 404s again. Delete
`phone-home.phase1.json`. Leave `phone-home.json` absent. Leave the 17204
direct runner running. The secret file can stay for a later phase 2; deleting
it is an operator choice and is not required to roll back.

## 4. Slices to dispatch now

Short tests only. One build and one filter per row. Lane: server2, Linux.
No `-Runner`, no `-Platform`. `ANTIPHON_LINUX_COMPILER_POLICY=0` before every
dotnet command. The checkpoint tool takes its own build slot.

### DW-1 — history off in the Windows phone-home env

**Goal.** `New-WindowsPhoneHomeRunnerEnv` sets
`SessionRunner__HostStats__HistoryEnabled` to `false`. Phase 1 and phase 2
stay off until a later slice, after CARD-1893 is the live server, sets a
`HistoryPath` under `C:/Antiphon/runner/state/host-load` and the matching
binding keys. This slice does not add those keys.

**Files.** `scripts/lib/windows-phone-home-env.ps1`.
`scripts/test-windows-phone-home-runner.ps1` (`Invoke-EnvTable` expected keys
and values, around `:190-235`).

**Red witness.** Add the expected key and the value `false` to `Invoke-EnvTable`.
Do not change the helper in that commit. `env-table.keys` / `env-table.values`
fail. The class has 10 `[Test]` methods and no `[Arguments]` at this base, so
one method fails and the executed count stays 10.

**Desktop.** None. The phase 1 clone picks the commit up when the operator
cuts or updates it.

**Owner.** Code delegate, then Review.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | DW-1-red | tests/Antiphon.Tests -> bin-dw1-red/ | dw1-red | `/*/*/C1744G3PhoneHomeSupervisionTests/*` | env table requires HistoryEnabled false, helper does not set it | 1 failed, 9 passed | 10 | 15 |
| CP-2 | DW-1 | tests/Antiphon.Tests -> bin-dw1/ | dw1 | `/*/*/C1744G3PhoneHomeSupervisionTests/*` | helper sets HistoryEnabled false | 0 failed | 10 | 12 |

Measured basis: none on this task. The S4 plan's harness cases are a few
seconds plus process start; the row is dominated by the `Antiphon.Tests` build.
Code replaces these estimates with its own durations. A row past 2x the
estimate gets a Debug task, not a retry.

### DW-2 — docker-stack says what this master actually does

**Goal.** The native Windows section and the CARD-1744 rollback paragraph stop
saying that master `a789c877` lacks `-PhoneHomeConfigPath` and the AppHost
guard. State the split this plan measured: this master has both; the running
server `997f9b8d9` has neither; phase 1 uses the side clone; phase 2 still
waits for an activated tip. Do not change the compose, the scripts, or the
secret names.

**Files.** `docs/docker-stack.md` only.

**Red witness.** None. The documentation class does not pin those stale
sentences (it pins `/run/secrets/phone-home` at
`DockerStackDocumentationTests.cs:323`). The row is the guard that the edit
did not drop a pinned string. 20 `[Test]` methods, no `[Arguments]`, at this
base.

**Desktop.** None.

**Owner.** Docs delegate, then Review. Disjoint from DW-1. Dispatch together.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-3 | DW-2 | tests/Antiphon.Tests -> bin-dw2/ | dw2 | `/*/*/DockerStackDocumentationTests/*` | pinned docker-stack strings still hold | 0 failed | 20 | 12 |

### Not dispatched now

- A Windows deploy key, git identity, or push-allow-list. Wait for the phase 1
  land. If it fails `RepositoryPushUnauthorized` or a prompt-disabled auth
  error, that failure is the red witness for a new card.
- Host-load binding for `desktop-win`. After CARD-1893 is activated, not before.
  Turning history on against the current server writes a spool the server does
  not bind.
- Retention on Windows. A card after phase 1, with a measured growth rate.
- CARD-1751, CARD-1778, the image roll, and the window. Owners below.

## 5. Order against the next rc, the 2.1.327 roll, and CARD-1893

1. **Now.** Dispatch DW-1 and DW-2. Land them on master. They do not restart
   anything.
2. **Phase 1 can start as soon as DW-1 is on the clone tip** and the operator
   has saved the user-secrets entry. It does not wait for the next rc, for
   2.1.327, or for CARD-1893. It registers to the current AppHost. Work is
   pinned `-Runner desktop-win -Platform Windows`. The direct runner and the
   Full qualification stay as they are.
3. **CARD-1893** is Review. Its runner binding is on this master
   (`docker-compose.server2-runner.yml:79-87`: `HistoryPath=/state/host-load`,
   `PhysicalHostKey=server2`, `SourceEpoch` set) and is not on the running
   compose, which still forces history off. Leave `desktop-win` history off
   (DW-1) until that binding is the live server2 runner. Do not put a
   desktop-win history slice in front of it.
4. **2.1.327** is the server2 runner image (`docker/session-runner-grok/Dockerfile:87`,
   pin `1285ab3ee` on this master, not on `997f9b8d9`). The running Dockerfile
   is 2.1.280. The Windows runner uses the desktop's own `claude`, `codex` and
   `grok`. The image roll does not make `desktop-win` work, and `desktop-win`
   must not delay it. Roll it in the next server2 staged rollout, together
   with the CARD-1893 compose, so server2 sessions are interrupted once.
   A re-published 2.1.327 fails the image build at `sha256sum -c`. The remedy
   is a new pinned pair.
5. **Next rc cut** from master after DW-1 and DW-2 land. It should also contain
   the `desktop-win` compose entry, the S2 scan filter, `DirectRunnerKey`,
   S4 supervision, the 2.1.327 pin, and CARD-1893 once that card has landed.
   If CARD-1893 is still in Review, cut the rc without it only when the
   operator accepts a second runner rollout later. Do not hold phase 1 for
   that choice.
6. **Windows Full** of that rc stays on the desktop. `launch-rc.ps1` is local.
   The supervisor reads local files. It does not move with the server. Run it
   on the direct runner's broker. Do not point it at `desktop-win`.
7. **Server2 staged rollout** of that rc (temp, drain, upgrade, retire temp).
   One interruption. Keep dispatching. This is the 2.1.327 roll and, if the
   commit is in the rc, the CARD-1893 activation.
8. **Desktop activation of that same rc**, between Full runs: pull
   `C:\src\Antiphon`, restart AppHost at the rc tip, restart the direct
   runner. The S2 filter and the S4 guard become what the desktop checkout
   runs. Phase 1, if it is already up, keeps dialing the same origin.
9. **Phase 2 and the window** stay operator-run. Phase 2 writes
   `C:\Antiphon\runner\phone-home.json` and uses `restart-session-runner.ps1 -Hard`.
   AppHost then refuses to start, which is the point: the desktop server is
   stopped, the Linux server is the writer, and `desktop-win` is the Windows
   runner on 17204. CARD-1778 re-home happens in that window, not before.
   CARD-1751 is the broker probe for the channel bridge, also not phase 1.
   CARD-1406 is still Review on the board; the operator confirms the Postgres
   move before the window. This session did not read the database.

## Not done, noted

No Windows deploy-key path and no desktop-win history binding were added; DW-1
only turns history off, and push waits for the phase 1 land.

## Unobserved

- The desktop user-secrets file, the secret file, `$HOME`, and whether Claude,
  Codex and Grok are signed in there.
- Hairpin from the desktop to `https://antiphon.desktop.codeperf.net`.
- A connected `desktop-win` (status is 404).
- Which pty backend the live direct runner exported.
- A push from a Windows phone-home mirror.
- Antivirus and long-path behaviour on `C:\Antiphon\runner\work`.
- Whether CARD-1406's move has gone quiet. The card is Review.
- The two investigation files that are not on this master.
- `buildSlotLabel` overrides inside qualification records. The default in
  `launch-rc.ps1:60-61` is `rc-qualification`.
