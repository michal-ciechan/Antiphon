# CARD-0777: desktop Codex readiness timeouts were the update modal

Date: 2026-09-28. Debug task `77232fc3`. Base: `7ca550ce`.

## Verdict

Every desktop Codex launch that died at CARD-0574's 60-second gate (`reason=Unknown mcpSeen=False`) was sitting on Codex's **"Update available" modal**. The modal appears before the composer as soon as `CODEX_HOME/version.json` records a release newer than the installed CLI that has not been dismissed. It waits for a key. 0.156.1 and 0.158.0 draw it with the footer `enter continue · esc skip`. `CodexStartupScreen.ContainsBlockingUpdate` matched only `Press enter to continue` (the older headed-canary shape), so it classified the modal as `Unknown` instead of `BlockingUpdate`, and nothing ever answered it.

This is reproduced under ModernConPty, not inferred. Launching `node.exe codex.js --no-alt-screen --dangerously-bypass-approvals-and-sandbox -c disable_paste_burst=true` at 120x30 with a throwaway `CODEX_HOME` whose `version.json` recorded a newer release rendered this screen on both 0.156.1 (the incident version) and 0.158.0. `CodexStartupScreen.Classify` returned `Unknown` for it:

```
  Update available · 0.156.1 → 0.999.0
  Release notes: https://github.com/openai/codex/releases/latest

› 1. Update now (runs `npm install -g @openai/codex`)
  2. Skip
  3. Skip until next version

  enter continue · esc skip
```

With the same `version.json` plus `-c check_for_update_on_startup=false`, both versions skipped the modal and went straight to the next screen. In the throwaway home that screen was sign-in.

## Incident correlation

The sources are the pty-host logs (`C:\logs\antiphon\session-runner\pty-hosts\logs`), the server log `antiphon-2026092{6,7}.log`, and Codex's own `~\.codex\logs_2.sqlite`, joined by timestamp and process.

| Session | Launched (UTC) | Codex's own log | Server verdict |
|---|---|---|---|
| `5786d2d9` | 09-26 15:35:44.9 | 15:36:15–16: announcement tip, then GitHub releases and npm registry (15.8 MB). The 20 h refresh wrote `latest_version` 0.157.1. Nothing after. | `not-ready reason=Unknown elapsedMs=60007 mcpSeen=False` |
| `32f81ff0` | 09-26 15:40:28.1 | 15:40:29: announcement tip only, with no version refresh. Nothing after. | `reason=Unknown elapsedMs=60002` |
| `4f7cf634` | 09-26 21:52:24.5 | 21:52:25–26: announcement tip only. Nothing after. | `reason=Unknown elapsedMs=60015` |
| `4474040f` (4th, not in CARD-0772) | 09-27 12:51:53 | 12:51:56–57: refresh again, latest still 0.157.1. Nothing after. | `reason=Unknown elapsedMs=60001` |

- The installed CLI was 0.156.1. 0.157.0 shipped 09-25 02:35Z and 0.157.1 shipped 09-26 01:06Z (npm `time`). `version.json` still reads `latest_version 0.157.1`, `last_checked_at 2026-09-27T12:51:57Z`, `dismissed_version 0.154.0`, so the modal condition held for every launch after the 09-26 refresh.
- Codex writes no further log rows while it waits on the modal: the app-server, `thread/start` and MCP startup never run. That explains `mcpSeen=False` and the absence of any Codex log after startup.
- The successful 77-minute session `061581e5` (09-25 15:27Z) predates any recorded 0.157.x. The last run of successful desktop Codex launches (dozens on 09-24 and 09-25) all predate it too. The long-lived TUI process that ran 09-24 → 09-27 started before the release. There is no desktop Codex success after `version.json` recorded 0.157.x.
- Correction to CARD-0772/0777: only `5786d2d9` (and `4474040f`) performed the 15.8 MB npm check. It ran about 1–3 s into Codex's life and did not block. `32f81ff0` and `4f7cf634` never refreshed.
- `5786d2d9` is the weakest link. Its refresh ran in-process, and Codex decides the modal from the `version.json` it read *before* that refresh. Log pruning removed the rows that would show whether an earlier refresh (for example, from `061581e5`'s launch at 09-25 15:27, which was more than 20 h after any 09-24 check) had already recorded 0.157.0. That sequence is consistent with everything above but was not observed directly. Its codex.exe also logged its first row about 30 s after the pty launch, compared with about 1 s for the others; this was not explained.

## State at investigation time

At 2026-09-28 21:38Z, another process on this desktop ran `npm install --global @openai/codex@latest` from a directory with no project `.npmrc` (npm debug log `2026-09-28T21_38_01_084Z`). This task did not run it. Installed is now **0.158.0**, which equals npm `latest`. Because `version.json` still says 0.157.1, the modal would not show on a launch *today*. It would come back on the first launch after the next Codex release, unless the fix below is in place.

## Fix (commit on `feat/card-task-77232fc3`)

1. **Suppress the modal:** `CodexLaunchArgs.DisableUpdateCheck` (`check_for_update_on_startup=false`) on both Codex launch paths, `AgentTaskDispatcher` delegates and `AgentSessionLaunchComposer` named agents. server2's seeded `config.toml` already sets the same key (CARD-0660). This investigation is the first proof that the key suppresses the modal.
2. **Classify it:** `ContainsBlockingUpdate` also matches `Update available` + `Skip until next version`. That option exists only in the modal, never in the dismissed-version banner above a live composer. A future timeout on this screen then reports `reason=BlockingUpdate`.
3. **Keep the frame:** `CodexReadyWaitOptions.OnNotReadyFrame` hands the last snapshot over once, on a not-ready result. `RunnerCodexAdapter` stores the rendered screen and a control-escaped 16 KB raw tail with `CodexStartupCaptureStore`, in `AgentRegistry:CodexStartupCaptureDirectory` (default `%TEMP%\antiphon-codex-startup`, newest 50 kept). It logs only the path, so the CARD-0574 R-56 no-screen-in-logs rule holds.

The CARD-0772 desktop Codex refusal is **unchanged**. Lifting it still needs a desktop launch demonstrated reaching `thread/start` with a positive ready screen and a matching `UserPrompt` on a stack running this fix.

## Not done

- There was no live desktop Codex launch through Antiphon. The 409 refusal stands, and this task did not override it. The ConPTY probe used the same launch shape (node.exe, ModernConPty, 120x30, production flags) outside the runner.
- The modal is not dismissed automatically. Suppressing it is sufficient. Answering it would mean typing into a session before readiness is established.
