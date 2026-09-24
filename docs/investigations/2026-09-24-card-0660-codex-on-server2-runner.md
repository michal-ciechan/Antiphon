# CARD-0660 — Codex on the server2 phone-home runner (investigation)

Date: 2026-09-24. Stage: Investigate (retry; first attempt stalled and wrote nothing).
Investigator ran inside the server2 runner container (`PhoneHome__RunnerId=server2`, uid `app`), read-only.
Card: "Codex on the server2 phone-home runner (follow-up to default-runner)"; parent CARD-0659 keeps Codex local.

Status: IN PROGRESS — sections are appended as findings land.

## 1. How a Codex session launches and is supervised today (desktop)

Codex is split across three processes exactly like Grok and Claude; the parts that matter for server2:

| Layer | Codex behaviour | Windows/desktop assumption? |
|---|---|---|
| Agent definition | `server/appsettings.json:82-86` — `Exe: codex.cmd`, `ArgsTemplate: ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"]` | **Yes** — `codex.cmd` is the npm Windows shim. |
| Launch args | `AgentTaskDispatcher.ComposeDelegateArgs` (`server/Application/Services/AgentTaskDispatcher.cs:5066-5150`): no `--name`; `-c model_reasoning_effort=<tier>`, `-c disable_paste_burst=true`; rules as ONE argv element `-c developer_instructions=<bundle>` (`CodexLaunchArgs.cs:40`), bounded by `InstructionBundleComposer.EnsureWithinCommandLineBudget` (30,000-unit budget) | No — argv only. Linux limit is `MAX_ARG_STRLEN` = 131,072 bytes per argv element; a ≤30,000-UTF-16-unit bundle is ≤90 KB UTF-8 worst case, so it fits (Grok needed a rules *file* on Linux, Codex does not). |
| Runner shim rewrite | `CodexWindowsLaunchPolicy.Apply` (`src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:39-41`) rewrites `codex.cmd` → `node.exe codex.js` and enforces the CRT command-line ceiling | Windows-only by construction: `if (!OperatingSystem.IsWindows() ...) return request;` — a no-op on Linux, nothing to port. |
| Server adapter | `RunnerCodexAdapter` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs`) — talks only to `ISessionRunnerClient`: snapshot/screen for readiness, `GetTranscriptAsync` for submit confirmation and turn end | **None.** It is transport-agnostic; the phone-home client implements the same interface, which is how `RunnerGrokAdapter`/Claude already work against server2. |
| Readiness | `CodexReadyWait.WaitAsync` over `CodexStartupReadiness` (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`) — positive gate on the rendered composer, MCP-boot status threshold, trust/update prompt handling, all from screen snapshots | Screen-text only; no path or OS logic. Needs one Linux capture to confirm the first-run screens (trust dir prompt, update nag, login screen) render the same through the Linux pty host. |
| Submit + done | `CodexSubmitConfirmation` requires a `UserPrompt` transcript row past the baseline; done = `TurnEnd` (`event_msg/task_complete`) row, screen tracker fallback | None beyond needing the transcript. |
| Rollout discovery | Runs **inside the session runner** (`SessionRunnerRuntime.cs:2093-2115`), `CodexTranscriptTailer` over `ResolveSessionsRoot(request.Env)` = `$CODEX_HOME/sessions` from the *launch* env, else the runner's own env, else `~/.codex/sessions` (`CodexTranscriptTailer.cs:170-181`). Binds only with C1 claim + C2 exact `session_meta.cwd` + C3 start time + C4 delivered-input match. | C2 compares case-sensitively on Linux (`CodexTranscriptTailer.cs:629`), which is correct. Nothing Windows-specific. |

Conclusion for (1): the runner binary that server2 already runs contains the whole Codex runtime (tailer, normalizer, rollout probe, sidecar re-adopt, `SupportedTranscriptFormats` already lists `codex` — `SessionRunnerRuntime.cs:25-26`, `:726`). The Windows-only pieces are the `codex.cmd` definition and the `CodexWindowsLaunchPolicy` rewrite, which already self-disables on Linux. What blocks Codex on server2 is admission, the image and auth — not the supervision code.

## 5a. Where Codex is refused today (dispatch path)

Three explicit gates, all intentional (CARD-0628 D-5, CARD-0659):

1. Server: `PhoneHomeLaunchPolicy.IsAdmittedKind` = `Grok or ClaudeCode` (`server/Application/Services/PhoneHomeLaunchPolicy.cs:47`); `RefuseUnsupportedStart` throws `phone_home_kind_refused` for a runner-bound task of any other kind (`:100-101`).
2. Server projection: `ProjectExe` maps only `grok(.exe)` / `claude(.exe)` or an exact `RawExeAllowList` entry (`:185-205`); `codex.cmd` falls through to `phone_home_wrapper_refused`. The projected env sets `GROK_HOME` and `CLAUDE_CONFIG_DIR` only (`:162-167`) — no `CODEX_HOME`.
3. Runner: `PhoneHomeCommandDispatcher.RejectUnsupportedLaunch` admits exe `grok`/`/usr/local/bin/grok`, `claude`/`/usr/local/bin/claude`, or the raw allow list (`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:321-331`) and refuses any `TranscriptFormat` other than Grok/Claude (`:365-370`, comment: "Anything else (codex) has no tailer here" — stale: the tailer is present, the image has no codex).
4. Auth probes: `ProviderAuthAsync` knows only `claude` and `grok` (`:233-256`); `RejectSignedOut{Claude,Grok}Async` are the per-launch backstops (`:264-296`).

