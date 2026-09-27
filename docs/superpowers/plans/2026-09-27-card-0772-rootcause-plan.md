# CARD-0772 root cause: desktop Codex dies at the 60s ready gate

Date: 2026-09-27. Investigate task: `d77c21f4`. Card: CARD-0772.

This is a root-cause record, not a pipeline plan and not a fix. Evidence is in [the investigation](../../investigations/2026-09-27-card-0772-codex-desktop-rootcause.md). No production code was changed. No Codex process was launched.

## What is confirmed

Desktop Codex sessions `5786d2d9`, `32f81ff0` and `4f7cf634` were killed by Antiphon at the CARD-0574 positive ready deadline. Each pty-host child was `C:\Program Files\nodejs\node.exe`, lived 60.1–61.3s, and exited `code 1, reason KilledByRequest`. The server logged `codex-startup not-ready reason=Unknown elapsedMs=60002..60015 mcpSeen=False` and then `Agent process did not become ready.` The session row's `FailureReason` is `Process exited (KilledByRequest, code 1).` because the exit observer writes that while the row is still Starting, and `KilledByRequest` maps to ProcessExit. The task is failed 181–195s later by the dead-session reconciler. That interval is `DeadSessionFailGraceMinutes` (3) from the first sweep that saw the dead row, not a second kill and not a two-minute mystery.

The gate is the current one. `RunnerCodexAdapter.WaitForReadyAsync` waits `CodexReadyMaxWaitMs` (60000) for a positive layout. Deadline returns false. There is no bypass that types anyway.

Codex's own log never reached `thread/start` or `session_loop` on these three. The native `codex.exe` behind `codex.js` spent the minute on startup HTTP. The first death completed `announcement_tip.toml`, GitHub `releases/latest` (last-modified 2026-09-26 01:04:09 GMT), and a 15.8 MB `GET` of `registry.npmjs.org/@openai/codex`, then wrote `~/.codex/version.json` with `latest_version` 0.157.1 against installed 0.156.1, and still had no thread when it was killed ~30s later. The other two completed only the announcement tip and then went silent until the kill.

## What also ran, so this is not "the desktop cannot host an agent"

- Claude review `09f3dc47` (CARD-0718) on the desktop overlapped the first two deaths and succeeded. Pty-host launched `claude.exe` on the same ConPTY bin (`20260926-140853-8f11f840`).
- Claude review `80103e29` (CARD-0726) on the desktop succeeded immediately before the cluster.
- Grok Code `d75b52ff` (CARD-0759), the checkpoint-tool task named in the brief, launched `grok.exe` 3.5 minutes after Codex `4f7cf634` was killed, on the same card and the same ConPTY bin, and succeeded through 23:01Z.
- CARD-0464's desktop review in this window was Grok `6cfbb6f5` and succeeded. CARD-0758's review `f22995f6` succeeded on server2. Those two were not desktop Claude reviews.
- Codex Code `ad64242f` (CARD-0726) on runner `server2` was dispatched 30s after the first desktop kill and succeeded after 1,754s. The same server-side gate accepted that screen.
- Desktop Codex session `061581e5` on 2026-09-25 launched the same `node.exe` rewrite, accepted a turn 13s later, and lived 77 minutes. The rewrite is exercised on both the success and the deaths.

## What is not the cause

The CARD-0497 shim rewrite did not fail closed and did not crash the child. A failed rewrite never creates a pty child. The child existed, and `codex.js` did spawn the native binary (different pid, startup HTTP in `logs_2.sqlite`). Linux not using the shim explains why server2 never hits this Windows startup path. It does not explain the kill: the Windows path has already succeeded on this machine.

## What remains unknown

The rendered frame is gone. `Unknown` with MCP never seen means the last sample was empty or unrecognized. It does not say which. `ContainsBlockingUpdate` matches only `Press enter to continue`, so a newer update/announcement frame would be scored `Unknown` and would not be auto-dismissed.

The ~30s after the first death's npm response, with no further Codex log, is not explained. Parsing the 15.8 MB body, a request that logs only when it completes, and a silent modal are all still possible.

Sessions two and three never logged a completed second request. That they were blocked on the same npm GET is an inference.

server2's Codex version and whether it does this fetch were not read on that host.

One earlier desktop death (`8abba29d`, 2026-09-25) reached `thread/start` and was still `Unknown` at 60s. Lengthening the budget is not, by itself, shown to be enough.

## What a fix has to do

The refuse-Codex-on-desktop mitigation stays valid until a desktop launch is shown reaching `thread/start` and a positive ready screen inside the budget. A fix that replaces that mitigation has to do all of the following, and this document does not implement any of it:

1. Not kill a process whose Codex log is still in pre-session startup HTTP, or stop that HTTP from consuming the whole ready budget before the first paintable frame. The 26 Sep deaths never got a thread.
2. On `not-ready`, keep the rendered screen and the last Codex startup target. A later incident cannot be diagnosed from `reason=Unknown` alone.
3. Classify an update or announcement frame if one is actually painted. Today's blocking-update match is a single phrase and did not fire here.
4. Leave `Agent process did not become ready.` on the session row. The exit observer's `KilledByRequest` text, and the `40P01` deadlock between that observer's `FOR UPDATE` and the launch catch's `FOR UPDATE`, hide the gate failure from the task reason.
5. Show the repair on a desktop launch that uses the installed 0.156.1 CLI (or whatever is current then), not only on server2 and not only against a scripted screen.

## Not done, noted

No code change, no timeout edit, and no live Codex launch.
