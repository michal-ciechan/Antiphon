# Codex on server2 never became ready (session e41748c6)

Date: 2026-10-18. Stage: Investigate (read-only). Bound card: CARD-0907. Failed work: Test task `405d59ce-d622-4ae9-9fa9-1a3a4157b505` (CARD-1311, Codex / Frontier / `gpt-6-astra`, runner `server2`).

## Verdict

Confirmed for the failure sentence and the screen it was waiting on. Not confirmed for the single subsystem that kept the footer from painting.

`Agent process did not become ready.` is the generic sentence `AgentSessionService` throws when `WaitForReadyAsync` returns false and the adapter's `LaunchBlock` is null (`server/Application/Services/AgentSessionService.cs:2652-2673`, `NotReadyBase` at `:2668`). `RunnerCodexAdapter` never sets `LaunchBlock` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:144-188`). The wait is `CodexReadyWait` with `CodexReadyMaxWaitMs` 60000 and `CodexReadyQuietPeriodMs` 1000 (`server/Application/Settings/AgentRegistrySettings.cs:62-63`). Ready requires a Codex banner, a selected model (a `model:` row, or a footer-only `GPT-… <effort>`), an idle composer, and a `model effort · cwd` footer (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:53-109`, footer-only fallback `:175-198`). Trust, sign-in, sandbox, and the update picker are classified earlier (`:68-75`, sign-in `:294-299`, update `:312-318`). Trust gets one Enter; a measured update picker gets one Escape (`CodexReadyWait` `:496-564`). A splash that is none of those ends as `reason=Unknown` (`LogFinal` `:640`).

That is what this launch did. The child lived 60.0s and was then killed. The last screen is the 0.160.0 splash with no model row and no footer.

## Timeline

| Instant (UTC) | Evidence |
|---|---|
| 2026-10-18T16:55:07.480Z | Task created. Event: Codex, Frontier, candidate `gpt-6-astra`. |
| 16:55:07 and 17:03:03.955 | Runner log: `Codex auth probe: loggedIn=true error=null` (`/state/runner-logs/session-runner-20261018.log:314` and `:321`). |
| 17:02:12 | Worktree cut at `C:\Antiphon\worktrees\card-task-405d59ce` from `a1d24b6f`. |
| 17:02:29 | Held for the server2 mirror. |
| 17:03:04.051Z | Dispatched to `task-405d59ce` (`gpt-6-astra`). |
| 17:03:04.585Z | pty-host launched `/usr/local/bin/codex`, child pid 31943, UnixPty (`/tmp/antiphon-pty-hosts/logs/e41748c62e9c4a539c4886210b4b98f9.log`). |
| 17:03:04.929Z | ANSI log stops growing (`/state/session-runner/e41748c62e9c4a539c4886210b4b98f9.ansi.log`, 1563 bytes). |
| 17:04:04.782Z | `Child exited (code 0, reason KilledByRequest)`. Runner log `:323` `KillGeneration … outcome=killed`. |
| 17:04:05Z | Session `Failed`, `endedAt` 17:04:05. |
| 17:07:11.383Z | Task `Failed`. The four minutes in the brief are dispatch-to-settlement, not the ready budget. |

`GET /api/sessions/e41748c6-2e9c-4a53-9c48-86210b4b98f9` is 404. `SessionEndpoints` maps `/buffer` and `/transcript` (`server/Api/Endpoints/SessionEndpoints.cs:52-67`) and does not map a session GET. Transcript `lastSequence=0`, `entries=[]`. Buffer `lastSequence=12` is the same ANSI as the runner file. Pool agent `3063d022-cc69-4b61-98ad-b2fd259ad380` is already gone (404).

## Last screen

Reconstructed from the ANSI cursor positions. The second paint, which is also the last paint:

```
>_ OpenAI Codex (v0.160.0)
     /work/worktrees/task-405d59ce
  permissions: YOLO mode

  Forty-two is an answer. What’s the question?

› Ask Codex to do anything
```

The first paint had `loading` where the directory path later appeared. Nothing after that second paint was written for the remaining 60s. The text does not contain `Trust this folder`, `Sign in with ChatGPT`, `Sign in with Device Code`, `Press enter to continue`, `Update available`, `Skip until next version`, `sandbox setup`, `input disabled`, `MCP startup`, or a rate-limit line. There is no `model:` row and no `GPT-6-Astra xhigh · …` footer. `Classify` returns `Unknown` on that frame (`CodexStartupReadiness.cs:87-98`): the idle hint is the last content line, so it is not a composer sitting above a model footer.

No Codex rollout was written. `/state/codex/sessions/2026/10/18` does not exist. The newest `session_index.jsonl` row is `2026-10-16T13:24:34Z`. The sqlite log WAL has no `task-405d59ce` and no `e41748c6`. This runner's 2026-10-18 log has exactly one `exe=codex` launch, and it is this one.

## What the launch was

Delegate Codex argv is the profile model plus three `-c` overrides (`server/Application/Services/AgentTaskDispatcher.cs:7019-7030`): `model_reasoning_effort=xhigh` for Frontier (`CodexLaunchArgs.cs:59-70`), `disable_paste_burst=true`, and `check_for_update_on_startup=false`. The tier slug is `gpt-6-astra` (`docs/agent-kinds.md:214`, launch shape `:488-516`). The runner only records `exe=codex`, not the argv. The banner the child painted is `OpenAI Codex (v0.160.0)`, which matches the catalogue.

## Auth, home, and the roll

The standing container is still `a6457bc89508` on `antiphon-server2/session-testing:09e48c5d6f1d`. `GET /api/session-runners/server2/status` at investigation time: `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f` (same bytes as `/state/runner-store-id`), `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233`, `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, `acceptingNewWork=true`, `draining=false`. `codexCliVersion=0.160.0`, `codexCliVersionError=stderr_output`, `codexCliVersionStale=true` (checked `2026-10-18T14:38:02Z`; the display window is 15 minutes, `docs/ops-http.md:81-85`). Stale version display does not refuse launch.

`CODEX_HOME=/state/codex`. `auth.json` is a regular file, 3880 bytes, mtime `2026-10-15T06:54:18Z`. The probe that logged `loggedIn=true` only checks that the file is a regular non-link; it does not open it (`src/Antiphon.SessionRunner/CodexAuthProbe.cs:8-16` and `:66`). Opened here for timestamps only: `auth_mode=chatgpt`, access-token `exp=2026-10-25T06:54:18Z` (still valid at 17:03Z), id-token `exp=2026-10-15T07:54:18Z` (already expired). The file was not rewritten during the 60s launch. `config.toml` (372 bytes, mtime 2026-10-14) sets `check_for_update_on_startup=false`, trusts `[projects."/work/repos/antiphon"]`, and has no model override. The failed cwd is a linked worktree of that repo (`git-common-dir` `/work/repos/antiphon/.git`). No `version.json`.

`docs/investigations/2026-10-17-rc-rollout-finish.md:348-367` records gate 7 as `codex --version` → `codex-cli 0.160.0` (exit 0) plus the same `stderr_output` advisory. `redeploy-old` skipped `deploy-parent`, which is the phase that writes `codex-auth-present.txt` (`docs/docker-stack.md:831`). The roll record itself does not contain an auth-present line. Presence was measured on the live volume as above.

`models_cache.json` was fetched `2026-10-16T18:01:05Z` by client `0.160.0`. `gpt-6-astra` is `visibility=list`, `supported_in_api=true`, and its reasoning levels include `xhigh`. `upgrade` is null. A historical turn on this same home (thread `01a1176f-3814-7330-a66c-84bc06c2ab9b`, index `updated_at=2026-10-07T17:35:33Z`) logged `model=gpt-6-astra` and `codex.turn.reasoning_effort=xhigh` with `auth_mode=Chatgpt`. `api.openai.com/v1/models` answered 401 in 0.43s at investigation time, with no credential sent. That does not prove the path at 17:03Z.

## Desktop since 17:00Z

`GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&since=2026-10-18T00:00:00Z&unscoped=include` has two Codex rows and no others:

| id | runner | status | note |
|---|---|---|---|
| `405d59ce-d622-4ae9-9fa9-1a3a4157b505` | server2 | Failed | this session |
| `e340b1a2-2497-4d73-acd4-ce26da5070db` | desktop | Queued | `rc Windows npm ci failure diagnosis`, created 16:55:47Z, never dispatched. Held from 17:03:05Z: `concurrency cap reached (2 process-spawning tasks running)`. |

No desktop Codex task has succeeded or failed since 2026-10-18T17:00Z. The Linux-lane retry `dcba76bd-92aa-483e-a727-f91e72b7ed5d` is Grok, not Codex.

## Ruled out on this screen

- Trust dialog. Wording is absent, and the cwd's common dir is the trusted `/work/repos/antiphon`.
- Sign-in chooser. Wording is absent. Access token was unexpired. Probe file-presence was true.
- Update modal. Wording is absent, `version.json` is absent, and both the seeded config and the launch override disable the startup check.
- Usage-limit line. Not on the screen. The cached astra entry has no upgrade prompt.
- Unknown model slug. The cache lists `gpt-6-astra` with `xhigh`, and this home has completed an astra/xhigh turn.
- A dead CLI. The process painted `v0.160.0` and stayed up until the server killed it.
- `stderr_output` on the version probe. That advisory is explicitly non-blocking (`docs/agent-kinds.md:263-266`).

## Still open

Why the 0.160.0 TUI stopped after the splash. The desktop startup capture (default `%TEMP%\antiphon-codex-startup`, `AgentRegistrySettings.cs:73-78`) was not readable from this container; the server log line `codex-startup not-ready reason=…` was not retrieved. The runner ANSI is enough to classify the frame as `Unknown`, and it is not enough to name the blocked call behind the missing footer. The id token was already expired; the access token was not, and `auth.json` was not rewritten, so a refresh-that-never-finished is possible and unproven. A multi-GB sqlite open (`logs_2.sqlite` 1.5GB, `thread_history_1.sqlite` 1.8GB) is also possible; the same files already existed on 2026-10-16, when this home last wrote a session.

## Smallest safe remedy

An operator does this. This investigation did not launch Codex.

1. Do not `codex login` first. The ChatGPT access token is valid through 2026-10-25, and the file is a normal `auth.json`.
2. On the desktop, read the capture whose path is on the server log line `Session e41748c6-2e9c-4a53-9c48-86210b4b98f9 codex-startup capture stored at …`, and the matching `codex-startup not-ready reason=` line. Expect `reason=Unknown` and this splash.
3. In `antiphon-runner-session-runner-1` as uid 1654, one foreground `codex` with `CODEX_HOME=/state/codex`, cwd a linked worktree of `/work/repos/antiphon`, and argv `--no-alt-screen --dangerously-bypass-approvals-and-sandbox -m gpt-6-astra -c model_reasoning_effort=xhigh -c disable_paste_burst=true -c check_for_update_on_startup=false`. Watch for a `GPT-6-Astra xhigh · <cwd>` footer. Quit when it appears, or at 60s.
4. If the footer appears, re-dispatch the Linux lane as Codex/Frontier. If the same splash sits for 60s, stop retrying Codex on server2 and diagnose the post-chrome stall (catalog or account call, or the local sqlite). That is a Codex-startup problem, not a credential refresh and not a readiness-parser change.

## Not done, noted

Do not treat this splash as ready: it has no selected model. A later change could put `CodexStartupReason` into the failure sentence, because `RunnerCodexAdapter` leaves `LaunchBlock` null and every non-block timeout stays the generic sentence.
