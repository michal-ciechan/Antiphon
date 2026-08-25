# CARD-0187 live herdr probes — launching non-Claude kinds

**Date:** 2026-08-25 · **herdr:** 0.8.2, protocol 20 · **machine:** this desktop (the operator's
live herdr, two `herdr` processes since 04:54) · **grok:** `C:\Users\lndco\.grok\bin\grok.exe`
(Grok 4.6) · **codex:** codex-cli 0.147.0 via `C:\Users\lndco\AppData\Roaming\npm\codex.cmd` ·
**claude:** 2.1.245.

All probes ran in a throwaway workspace `wD` (`card0187-probe`, cwd
`C:\src\Antiphon\.antiphon\herdr-probes-card0187`), one tab per probe, every pane closed and the
workspace closed afterwards (`workspace.close` → `ok`; the operator's `w1/w2/w9/wC` untouched).
Raw JSON is under `.antiphon/herdr-probes-card0187/` (gitignored). No prompt was ever sent to any
agent, so no model turn was spent. The grok session directories the probes created were deleted.

| # | Probe | Result | What it decides |
|---|---|---|---|
| **K1** | `agent.start --kind grok -- --no-alt-screen --always-approve --session-id <guid>` | **Works.** Returned `agent_started` in **4.4 s**, `agent_status: idle`, `interactive_ready: true`; envelope `argv = ["grok", <our args…>]`; `pane.process_info` foreground = one entry, `C:\Users\lndco\.grok\bin\grok.exe` with our args verbatim; detection rule `osc_title_idle` (bundled manifest 2026.07.16.2). `~/.grok/sessions/<url-enc-cwd>/<guid>/` existed within seconds, **before any prompt** — `--session-id` is honoured through herdr, so `GrokTranscriptTailer`'s deterministic path holds on this lane. | herdr's grok kind runs the canonical `grok` on PATH; nothing herdr-side blocks Grok |
| **K2** | `agent.start --kind codex -- --no-alt-screen --dangerously-bypass-approvals-and-sandbox` | **Fails on this machine.** herdr typed `$p=Start-Process -FilePath codex -ArgumentList '--no-alt-screen --dangerously-bypass-approvals-and-sandbox' -NoNewWindow -Wait -PassThru` into the pane's **Windows PowerShell 5.1** shell; `Start-Process` resolved `codex` to the extensionless npm shim and died with `%1 is not a valid Win32 application`; `agent.start` returned `{"code":"timeout"}` after the full **90 s** timeout with the pane back at its prompt and no agent. | `agent.start` cannot launch Codex here at all. Antiphon's own catalogue uses `codex.cmd` for exactly this reason (`docs/agent-kinds.md` §2) |
| **K3** | How `agent.start` launches anything | It is a **typed shell line**, not a `CreateProcess`: `Start-Process -FilePath <canonical exe> -ArgumentList '<args>' -NoNewWindow -Wait -PassThru` (K2's screen shows it verbatim). The schema has exactly `name`, `kind`, `pane_id`, `args[]`, `timeout_ms` — **no exe, no env, no cwd** (`herdr api schema --json`, `AgentStartParams`). | A wrapper exe (`gkp.ps1` / `cxp` / a pinned profile executable) cannot be expressed through `agent.start` |
| **K4** | `agent.start` argument encoding matrix (kind grok, one arg each): `two words` · `say "hi"` · `it's` · ``cost $5 and `tick` `` · a 9 689-char single argument · `line one\nline two` | Space, both quote kinds, `$`, backtick and the 9.7 KB argument all arrived **byte-identical** in `process_info.argv`. **A newline is refused**: `{"code":"invalid_agent_argument","message":"agent arguments cannot be encoded safely for the target shell"}`, no process started. | **Live defect today:** every standing-instruction bundle is multi-line and is passed as one argument (`--append-system-prompt` / `--rules` / `-c developer_instructions=` — `AgentControlService.cs:281`, `AgentTaskDispatcher.cs:2057`), so a Claude herdr launch that carries a composed bundle fails at `agent.start`. CARD-0186's AlwaysOn lift made that reachable |
| **K5** | Shell launch: `pane run <id> codex.cmd --no-alt-screen --dangerously-bypass-approvals-and-sandbox` | herdr detected **`codex`, `agent_status: idle`** within 10 s (rule `osc_title_idle`, evidence = the OSC title `Antiphon`, remote manifest 2026.08.09.1). `agent.get <paneId>`, `agent.list` and `agent.wait <paneId>` all work for a pane whose agent was never `agent.start`ed (no `name`). `process_info` foreground = **one** entry: `cmd.exe /c C:\…\npm\codex.cmd …` (the node leaf is not listed). No trust prompt and no MCP-boot stall appeared (cwd is inside the already-trusted Antiphon repo; codex 0.147.0 printed its normal banner + composer). An Enter sent to the idle empty composer submitted nothing. | Passive detection is enough to bind a shell-launched Codex; `ChildPid` for a `.cmd` launcher is the `cmd.exe` pid (kill-entire-tree already covers the node leaf) |
| **K6** | Wrapper launch: `pane run <id> pwsh -NoProfile -ExecutionPolicy Bypass -File fake-gkp.ps1 --session-id …` where the script execs `grok.exe --no-alt-screen --always-approve @args` | Detected **`grok`, idle** (`osc_title_idle`). `process_info` foreground = **one** entry, `grok.exe` — the intermediate `pwsh` is **not** listed; `shell_pid` is the pane shell. | A `gkp`-style wrapper is invisible to the foreground list, so `HerdrPaneChild.KillAsync`'s foreign-process guard (`HerdrPaneChild.cs:195-217`) does not trip on it and `ChildPid` is the real agent |
| **K7** | Launch-script shape: `pane.send_text` of `& '<dir>\launch-probe.ps1'` + `pane.send_keys ["enter"]`, where the script is `& $exe @('--no-alt-screen','--always-approve','--rules',"line one`nline two with 'sq' and `"dq`" and `$5",'--session-id',…)` | `process_info.argv` = exe + every argument **byte-identical, newline included**; herdr detected grok idle. `agent.wait <paneId>` issued 0.1 s after Enter returned **`agent_not_found`** — the wait surface only exists once detection has happened, so a launcher must poll `pane.get.agent` first. | One launch shape covers canonical exes, wrappers, `.cmd` launchers and multi-line arguments; readiness must be polled on `pane.get`, not `agent.wait` |
| **K8** | Pane shell | herdr's default pane shell on Windows is `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoExit -Command <prompt hook>` (`config.toml` `default_shell` empty → herdr's platform default). `shell_pid` names it in every `process_info`. | The typed launch line is a PowerShell expression; the launcher must check the shell before typing and refuse an unknown shell explicitly |

## What the matrix means for the design

1. `agent.start` is the wrong primitive for Antiphon's launches, not just for non-Claude kinds:
   it is a typed `Start-Process` line with three hard limits (canonical exe only, no newline in
   any argument, no env) and one platform break (Codex on Windows). K4 is a defect on the **Claude**
   lane today.
2. Shell-launching through a **launch script** (K7) has none of those limits and herdr's passive
   detection (K5/K6/K7) is what the operator already relies on for their own `pane run … gkp/cxp`
   panes. `pane.get.agent` is the readiness surface; `agent.wait` only after that.
3. The foreground list always named exactly our child (leaf `grok.exe` under a `pwsh` wrapper,
   `cmd.exe` for `codex.cmd`), so the CARD-0186 own-child-only kill discipline transposes unchanged.

Not probed here and left to the build slices: shell-launched **Claude** detection (Claude was only
launched through `agent.start`, K1's sibling from CARD-0160 P4); `pane.split --env` (P6 measured
`tab.create --env` only); bracketed-paste delivery through `pane.send_text` into **Grok/Codex**
composers (the 86 400 B herdr envelope was measured against Claude only, CARD-0161).
