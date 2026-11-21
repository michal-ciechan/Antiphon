# Codex 0.170.0 never becomes ready on server2

**Date:** 2026-11-21. **Pin:** `493bee0ee3af2052575bc7a26e9a313669b2fc2d` (`GET /api/version` `informationalVersion` `1.0.0+493bee0ee3af2052575bc7a26e9a313669b2fc2d`). Read-only except this note. No setting change, restart, deploy, or production Postgres connection. The shared `/state/codex` auth store was not opened.

**Outcome.** Both Codex canaries started `codex-cli 0.170.0`, logged in, and painted an idle composer. The ready gate still killed them. Codex 0.170.0 draws a new last row, `← for agents · ? for shortcuts`, under the model/effort/cwd footer and moves the warning chip onto that row. `TryReadBottomComposer` treats the last non-empty line as the footer and the line above it as the composer, so it tries to parse `GPT-6.2-Sol high · /work/...` as the composer, fails, and returns `Unknown` until the kill. The composer text the gate wants, `› Ask Codex to do anything`, is on the screen the whole time. This is not a sign-in, trust, update, sandbox, or model-slug failure. A 0.160.0 session on this runner became ready because that chip still sat on the model line, which stayed the last line.

## What the two canaries showed

| Task | Session | Model on the final grid | Child start (UTC) | SIGKILL request (UTC) |
|---|---|---|---|---|
| `f2975b42-04a3-42dd-bb99-d7251d80b2c9` High, no `-Model`, dispatched as `gpt-6.2-sol` | `149a0bb6-26c1-4013-ae4f-291021c60b2e` | `GPT-6.2-Sol high` | `03:13:04.012` | `03:15:04.123` (120.1 s) |
| `185080ff-8ca9-48ba-b45f-6af35b177fc4` High, `-Model gpt-6.2-astra` | `4e26ae02-4cd6-40f4-a777-90bb4d29f755` | `GPT-6.2-Astra high` | `03:13:52.861` | `03:15:53.056` (120.2 s) |

Runner log (`/state/runner-logs/session-runner-20261121.log`): `Codex auth probe: loggedIn=true error=null`, then `Phone-home Launch ... exe=codex ... outcome=started`, then `Phone-home KillGeneration ... outcome=killed`. Pty host: `Launched /usr/local/bin/codex`, backend `UnixPty`, exit `signal 9, reason KilledByRequest`. Task failure text: `Agent process did not become ready.; stopped by Antiphon (SIGKILL)`. `GET /api/sessions/<sid>/transcript?since=0` is `entries: []`, `lastSequence: 0`. Runner transcripts have `firstInputAtUtc: null`. The Grok canary `b7a8c1c8` on the same runner succeeded (session `056303ad`).

`GET /api/sessions/<sid>/buffer` is HTTP 200 and is the raw ANSI stream (same bytes as `/state/session-runner/<sid-n>.ansi.log`). Replayed at the launch size, 120 columns by 30 rows (`AgentLaunchOptions` defaults), the grid at kill for `149a0bb6` is:

```
  >_ OpenAI Codex (v0.170.0)
     /work/worktrees/ws-02-d3
  permissions: YOLO mode

  Tip: When goals are enabled, use /goal to set or inspect the objective for a long-running task.


› Ask Codex to do anything

  GPT-6.2-Sol high · /work/worktrees/ws-02-d3
  ← for agents · ? for shortcuts                                                              ⚠ 2 warnings · f2 to view
```

`4e26ae02` is the same shape with `GPT-6.2-Astra high · /work/worktrees/ws-03-d3` and a different tip line (`You can resume a previous conversation by running codex resume`). The raw stream shows the footer effort as `default` for one paint, then `high`, and the chip going from `1 warning` to `2 warnings`. The final grid is `high`. `--model` and `-c model_reasoning_effort=high` were accepted. The warning body was never drawn; `f2` was not sent. The chip is not a modal: there is no `Press enter to continue`, no `Skip until next version`, no trust prompt, and no sign-in text.

The server-side capture (`CodexStartupCaptureStore`, default `%TEMP%/antiphon-codex-startup` on the server host) is not on this runner. `/tmp/antiphon-codex-startup` here stops at 2026-11-14. The grid above is the frame that capture would have stored.

## Why the gate says not ready

`CodexStartupScreen.Classify` (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs:54`) requires the banner `OpenAI Codex`, an idle composer, and a model/effort/cwd footer. The idle composer is `›` or `>` plus empty or one of `Ask Codex to do anything`, `Improve documentation in @filename`, `Write tests for @filename` (`:43-51`, `:278-288`). There is no `model:` row on this screen, so the 0.158 fallback reads the footer (`TryReadFooterOnlyModel`, `:185-203`). That fallback would accept `GPT-6.2-Sol high · /work/worktrees/ws-02-d3` (`GPT-` plus `high` plus a `/` path).

It never sees that line as the footer. `TryReadBottomComposer` (`:231-255`) takes the last non-empty line as the footer and the line above it as the composer:

- last line = `← for agents · ? for shortcuts … ⚠ 2 warnings · f2 to view`
- line above = `GPT-6.2-Sol high · /work/worktrees/ws-02-d3`

`TryParseComposer` (`:258-276`) requires the composer line to start with `>` or `›`. The model line does not, so it returns false, and `Classify` returns `Unknown` at `:103-104`. `IsModelEffortCwdFooter` (`:291-310`) is not reached for the real footer. The shortcuts row itself would accidentally pass that loose check (it contains `·` and three tokens before it), which is why the fix has to step off that row rather than loosen the footer predicate.

`RunnerCodexAdapter.WaitForReadyAsync` (`server/Infrastructure/Agents/SessionRunner/RunnerCodexAdapter.cs:164`) uses `CodexReadyMaxWaitMs`. This SHA sets that to 60000 (`server/Application/Settings/AgentRegistrySettings.cs:63`, `server/appsettings.json:157`). Both children were SIGKILLed 120.1 s and 120.2 s after start, which is the same length as `PhoneHomeOperation.Launch`'s request timeout (`PhoneHomeLiveConnection.cs:361`, two minutes). The Launch RPC itself had already returned `outcome=started` at child start. The effective live budget was not re-read from the desktop process. It does not change the cause: the layout is `Unknown` from the first full paint, so a 60 s budget and a 120 s budget both expire. Do not widen the timeout.

## 0.160.0 was fine because the last line was still the model footer

Checked-in `tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/v0160-idle.txt:19` is one line:

```
  GPT-6.1-Sol low · /tmp/c907-e840b26d-9tcg88                                                  ⚠ 1 warning · f2 to view
```

The warning chip is on the model footer. There is no `for agents` / `for shortcuts` row. `CodexV0160StartupTests.C907_idle_frame_requires_full_settle_after_working` asserts `Classify` of that fixture is `Ready`.

On this runner, session `0820dcbb-393d-484a-b3e6-5372713a652a` (child start `2026-11-16T09:58:47Z`, first input `09:59:11Z`) painted `OpenAI Codex (v0.160.0)` and `GPT-6.1-Sol high · /work/worktrees/ws-02 ⚠ 1 warning · f2 to view`. Its ANSI log contains `for agents` 0 times and `for shortcuts` 0 times. It accepted a prompt. The canary log `149a0bb6` contains each phrase twice. The image pin's previous Dockerfile was `CODEX_VERSION=0.160.0` (`docs/investigations/2026-11-20-codex-0-170-everywhere.md`). The marker change is this new last row in 0.170.0, not the `gpt-6.2` slug.

## Unsigned probe, and the desktop

`codex --version` in this container, uid 1654, prints `codex-cli 0.170.0`. A 6-second PTY at 120x30 with `CODEX_HOME` under `/tmp/codex-probe-*` (not `/state/codex`), and the production flags `--no-alt-screen --dangerously-bypass-approvals-and-sandbox --model gpt-6.2-sol -c model_reasoning_effort=high -c disable_paste_burst=true -c check_for_update_on_startup=false`, painted:

```
  Welcome to Codex, OpenAI's command-line coding agent

  Choose how you want to use Codex.

> 1. Sign in with ChatGPT
     Usage included with Plus, Pro, Business, and Enterprise plans

  2. Sign in with Device Code
     Sign in from another device with a one-time code

  3. Sign in another way
     Use an API key or another provider

  Press enter to continue
```

`ContainsSignIn` (`CodexStartupReadiness.cs:317-322`) already matches `Sign in with ChatGPT` and `Sign in with Device Code`, so this screen is `SignIn`, not `Unknown`. The canaries were `loggedIn=true` and were past this screen. Option 3's wording changed from the 0.156 "Provide your own API key" note; that is not what killed the canaries.

`GET /api/agent-tasks?since=2026-11-20T00:00:00Z` with `Succeeded,Failed` and with the open statuses returned exactly two Codex tasks: these two Failed server2 canaries. No desktop Codex task became ready in that window. The classifier runs in the server for every runner. A desktop `codex-cli 0.170.0` that draws the same last row fails the same way. This container did not remeasure the desktop binary. The 2026-11-20 note left that measurement open; the canary brief says the desktop is already on 0.170.0.

Launch size is 120x30, and the model line is short, so the new row is not a wrap. uid 1654 is the normal runner user. The canaries used the logged-in `CODEX_HOME` (auth probe succeeded). None of those differ from a ready 0.160 launch in a way that explains `Unknown`.

## Smallest fix

Do not change the timeout, the launch argv, or `config.toml`. In `TryReadBottomComposer`, after the scan that sets `end` to the last non-empty line, if that line is the 0.170 shortcuts row, step `end` to the previous non-empty line and use that as the footer. Then the existing composer and `TryReadFooterOnlyModel` checks pass on the measured grid.

Add this helper next to `TryReadBottomComposer` in `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`:

```csharp
/// <summary>
/// Codex 0.170.0 draws this under the model/effort/cwd footer.
/// 0.160.0 kept the warning chip on the footer, which stayed the last line.
/// </summary>
internal static bool IsAgentShortcutsRow(string line)
{
    var t = line.Trim();
    return t.Contains("for agents", StringComparison.Ordinal)
        && t.Contains("for shortcuts", StringComparison.Ordinal);
}
```

Inside `TryReadBottomComposer`, immediately after the loop that walks `end` off trailing blank lines and before `footer = lines[end]`:

```csharp
if (IsAgentShortcutsRow(lines[end]))
{
    end--;
    while (end >= 0 && lines[end].Length == 0)
        end--;
    if (end < 0)
        return false;
}
```

Add one test, red before that edit, in `tests/Antiphon.Agents.Pty.Tests/CodexStartupReadinessTests.cs` (or `CodexV0160StartupTests`). The screen string is the measured `149a0bb6` grid, including the shortcuts row and `⚠ 2 warnings · f2 to view`. Assert `Classify` is `Ready`, `Reason` is `Ready`, and `NormalizedRegion` contains `GPT-6.2-Sol high`. Assert the same for the Astra grid (`GPT-6.2-Astra high · /work/worktrees/ws-03-d3`). Assert `CodexStartupFixtures.V0160Idle` is still `Ready`, so the 0.160 same-line warning chip is unchanged. Do not treat the shortcuts row as a ready footer by itself, and do not skip a line that is the model footer.

`IsLiveSession` (`:219-228`) uses the last line as a footer. The shortcuts row happens to pass `IsModelEffortCwdFooter`. That only suppresses the account-bootstrap fatal short-circuit; it does not grant Ready. Leave it unless a test says the pre-prompt frame is now misread.

Checkpoint, one isolated build slot and one exact filter: `/*/*/CodexStartupReadinessTests/*` plus `/*/*/CodexV0160StartupTests/*` if the new method lands in the second class. Not run here.
