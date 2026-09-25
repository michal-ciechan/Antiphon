# CARD-0723 — Where agent time and turns go

**Status:** Measured from the desktop API for tasks touched since 2026-09-25T00:00Z. This is not a defect with a single root cause. The time and the turn counts below are reconstructed from stored task rows and session transcripts.

**Date:** 2026-09-25. Snapshot taken from server2 against `https://antiphon.desktop.codeperf.net` between 18:22Z and 18:40Z. Investigate task `c94fb1eb`.

Not done, noted (no design in this stage): a checkpoint runner that overlaps independent rows would be the wall-clock lever; the measurements below only show that today's runs are sequential.

## Verdict

Settled work since midnight is **99.3 hours** of dispatch-to-complete wall and **$1,914** of recorded cost across **149 settled** tasks (158 rows including 8 still dispatched). Queue time is not the sink: median **0.6 min**, p90 **3.7 min**, **255 min** total.

A "turn" in the harness transcript is a `TurnEnd`. Those are almost always **1** (Claude and Grok do the whole task inside one or two model turns). The 40–200 turns named on the card are **tool calls**. Claude Code on Linux spends a median **79** tool calls and **28 min** on a Code task. Grok on Linux spends a median **184** tool calls and **47 min**. Codex records **no tool calls at all**.

The test run itself is the wall. A one-command checkpoint runner removes the ceremony around it (about **14 tool calls** on a typical Linux Claude Code task, about **20** on a typical Linux Grok Code task, and about **90** on the Windows poll-loop outlier). It does not remove the minutes the tests take unless it also overlaps independent rows. Windows Codex reviews are the largest wall bucket we cannot split into commands: **11 tasks, 17.6 hours, median 86 min**.

## Population

`GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&since=2026-09-25T00:00:00Z` returned **158** rows (Antiphon board, unscoped excluded). **8** were created on 2026-09-24T22–23Z and were still inside that `since` window. Status: 121 succeeded, 22 canceled, 7 failed, 8 dispatched.

| | Tasks | Transcripts read |
|---|---:|---:|
| Code | 65 | 64 |
| Review | 54 | 54 |
| Plan | 15 | 15 |
| Other (Investigate, Deploy, Debug, Merge, Custom) | 24 | 24 |
| **Total** | **158** | **157** (one Code Codex failure and one Code Claude failure had an empty transcript) |

Every session id on those rows was fetched (`GET /api/sessions/{id}/transcript?since=0`) and every task detail was fetched for the Created/Held events and the stored report. That is the sample: all of today's tasks, which covers Code, Review, and Plan, and both machines, for Claude and Codex. **Grok has no Review and no Plan task today.** Plan has **no Windows task** today. Grok Code on Windows is one still-open task (CARD-0721).

### Machine

`RunnerId` is not on the task-list DTO. The Created event is. **126** tasks include `[runner source=explicit requested=server2 default=unset selected=server2 ...]`. **32** have no runner tag. Shell commands on those 32, where the transcript has any, use `C:\` or `/c/Antiphon` (Git Bash on the desktop), including the Grok rows. Example, task `12cd8c8b` (CARD-0660, Review, Claude, Linux despite a `C:\Antiphon\worktrees\...` worktree path): Held detail `remote workspace preparation is in flight for runner 'server2'`. The desktop worktree path stays Windows even for a server2 mirror (`AgentTask.RunnerId`, `server/Domain/Entities/AgentTask.cs:198`).

## What a transcript can see

Two normalizers drop the rows that would have made this a command log.

- Codex command, file, and MCP activity is intentionally not stored. `CodexTranscriptNormalizer.FromThreadItem` maps user text, agent text, and reasoning, then returns nothing for `CommandExecution` / `FileChange` / `McpToolCall` (`src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs:253`). All **92** Codex transcripts are narration-only (one empty). Tool-call counts for Codex are **unknown**, not zero work. Assistant-message counts and dispatch wall are known. Narration gaps on the long Windows reviews are 1–6 minutes, so those hours are continuous work, not an idle socket.
- Grok stores the tool call and drops the tool result. Completed updates of non-question tools are skipped "so command output does not flood the transcript" (`src/Antiphon.SessionRunner/GrokTranscriptNormalizer.cs:31`). Build logs and TRX text are not in the Grok transcript. The wait is the timestamp gap after the call.

Claude stores `ToolCall` and `ToolResult`, including `Time Elapsed` lines and `MSB3027`.

**Definitions used below**

- **Wall:** `completedAt - dispatchedAt` from the task row. Open tasks are excluded from medians.
- **Tool calls / polls / launches:** counted only from `ToolCall` rows after the task prompt. A launch is `dotnet build`, `dotnet run`/`dotnet test` with a tree filter, or `scripts/run-checkpoint.ps1`. A poll is `get_command_or_subagent_output`, or a shell `until … EXIT CODE` loop that does not itself contain `dotnet`/`run-checkpoint`. A TRX read is a later command that greps `CHECKPOINT`, `run.trx`, or `Time Elapsed`.
- **Command wall:** timestamp of the next transcript entry minus the tool call. It includes a few seconds of model time before the next call. Foreground `timeout 590 pwsh … run-checkpoint.ps1` therefore puts the whole checkpoint in one gap. Grok backgrounds the build, so the launch gap is ~20s and the following poll gaps are the run.
- **Unit / CP / land class:** the command text contains `Category=Unit` (unit lane), or `run-checkpoint` / `treenode-filter` (CP), or a land/retirement class name (`Land`, `LandingGit`, `GuardedWorktree`, `WorktreeRetirement`). A compound shell line is classified by the strongest of those.

## By stage, kind, and machine

Medians are over settled tasks. Tool medians need a transcript with tool rows, so Codex tool cells are "n/a". Hours are the sum of dispatch wall.

| Stage | Kind | Machine | Settled | Median wall | Sum | Median tool calls | Median launches | Median polls |
|---|---|---|---:|---:|---:|---:|---:|---:|
| Code | Claude | Linux | 21 | 28 min | 14.6 h | 79 | 10 | 0 |
| Code | Claude | Windows | 2 | 134 min | 4.5 h | 143 | 11 | 56 |
| Code | Grok | Linux | 6 | 47 min | 4.5 h | 184 | 8 | 14 |
| Code | Codex | Linux | 27 | 48 min | 21.6 h | n/a | n/a | n/a |
| Code | Codex | Windows | 1 | 6 min | 0.1 h | n/a (empty transcript) | n/a | n/a |
| Review | Claude | Linux | 7 | 20 min | 3.7 h | 47 | 6 | 0 |
| Review | Claude | Windows | 3 | 73 min | 2.9 h | 72 | 9 | 0 |
| Review | Codex | Linux | 33 | 21 min | 17.1 h | n/a | n/a | n/a |
| Review | Codex | Windows | 11 | 86 min | 17.6 h | n/a | n/a | n/a |
| Plan | Claude | Linux | 6 | 24 min | 2.8 h | 82 | 1 | 0 |
| Plan | Codex | Linux | 9 | 13 min | 2.1 h | n/a | n/a | n/a |

Code Claude on Linux also has median **6** TRX-parse commands and median **2** `TurnEnd`s. Grok Code on Linux has median **1** `TurnEnd` and median **0.5** TRX-parse commands (the result text was not stored, so the agent rarely re-reads it through a tool). Plan barely runs checkpoints (median 1 launch) and does not poll.

Recorded cost on the 158 rows is **$1,913.98**. Grok's 6 settled Linux Code tasks are **$361** (median **$43**). Claude's 21 settled Linux Code tasks are **$344** (median **$8**). Codex Linux Code is **$475** across 27 tasks (median **$18**).

The card's picture ("every Code, Review, and land task polls in 2-minute waits") matches **Grok**, because long shell commands are backgrounded and then polled, and it matches the **Windows Claude poll-loop outlier**. It does not match the median Linux Claude task: median **0** polls, checkpoint invoked in the foreground.

## Top 10 wall sinks

Open Grok Code tasks are a snapshot (CARD-0717 already **109 min** and 486 tool calls, CARD-0714 **104 min** / 355, CARD-0710 **55 min** / 527). They are still running, so the settled list is the one to rank.

| Wall | Task | Card | What the transcript supports |
|---:|---|---|---|
| 240 min | `8747995c` Review Codex Windows **Failed** | CARD-0688 | Hit the 240-minute Review ceiling. Failure text: "Ran 4h00m against the 240-minute ceiling… Last transcript entry: AssistantText." 72 assistant messages from 04:46Z to 08:46Z. Narration: "CP-4 has crossed an hour" and "much slower on this Windows host than the plan's 14-minute estimate." Largest gap between messages is 6 min. |
| 221 min | `7ebf8aa2` Review Codex Linux **Canceled** | CARD-0589 | 190 assistant messages over 13:04Z–16:31Z. Narration stays on checkpoint retries and a quoted-filter timeout. 9 `TurnEnd`s, no command rows. |
| 218 min | `e0e5e480` Review Codex Windows Succeeded | CARD-0665 | 75 assistant messages, 03:34Z–07:11Z. Narration: matrix "91 executed, 80 passed, 11 failed"; CP-5a still running while the matrix TRX was already done. |
| 190 min | `d9b1f124` Code Claude Linux Succeeded | CARD-0688 | **391 tool calls**, 1 `TurnEnd`, **$120**. Foreground `dotnet build tests/Antiphon.Tests` gap **113s** with `Time Elapsed 00:01:52.12`, then the same tree again at **45s** and **39s**. Land-class checkpoint `RED` gap **210s**. This is real test and migration work, not a poll loop. |
| 185 min | `3bf6c1f4` Code Claude Windows Succeeded | CARD-0665 | **191 tool calls**. The checkpoint is started once, then re-issued as `timeout 110 bash -c 'until grep -q "EXIT CODE" …; do sleep 10; done'`. Five successive polls of `CP-R3r2.log` alone are **122, 117, 117, 120, 125s**. **94** such polls. |
| 124 min | `d2a0d121` Code Codex Linux Succeeded | CARD-0589 | 57 assistant messages, 1 `TurnEnd`, **$18**. Commands not stored. |
| 121 min | `3488192e` Review Codex Windows Succeeded | CARD-0672 | 37 assistant messages. Commands not stored. |
| 121 min | `06805739` Code Claude Linux Succeeded | CARD-0691 | **233 tool calls**, 2 `TurnEnd`s, **$39**. Includes a **218s** `dotnet build` of a base worktree. |
| 116 min | `ccb964a1` Review Codex Windows Succeeded | CARD-0688 | 54 assistant messages. Commands not stored. |
| 93 min | `0c0b9a4e` Review Codex Windows Succeeded | CARD-0665 | 37 assistant messages. Commands not stored. |

Seven of the ten longest settled tasks are Codex, so their command mix is not in the transcript. Of the three that are fully instrumented, one is a Windows `until grep EXIT CODE` loop and two are Linux Claude tasks actually building and running land-class tests.

## Top 10 turn sinks

Ranked by tool calls on **settled** tasks. Codex cannot appear here.

| Tool calls | Wall | Task | Card | Polls on that task |
|---:|---:|---|---|---:|
| 424 | 48 min | `e5d0294c` Code Grok Linux **Failed** | CARD-0710 | 16 |
| 391 | 190 min | `d9b1f124` Code Claude Linux | CARD-0688 | 45 |
| 258 | 47 min | `be035022` Code Grok Linux | CARD-0710 | 11 |
| 233 | 121 min | `06805739` Code Claude Linux | CARD-0691 | 29 |
| 203 | 47 min | `b1ffd29e` Code Grok Linux | CARD-0700 | 15 |
| 191 | 185 min | `3bf6c1f4` Code Claude Windows | CARD-0665 | 94 |
| 183 | 69 min | `75089dd7` Code Claude Linux | CARD-0589 | 0 polls; 5 builds and 41 class-filter launches |
| 164 | 54 min | `4aa8cc21` Code Claude Linux Canceled | CARD-0664 | 0 polls; 5 land-class runs, CP-7 at 602s |
| 162 | 49 min | `3678a831` Code Grok Linux | CARD-0701 | Grok poll pattern |
| 161 | 41 min | `e9516943` Code Grok Linux **Failed** | CARD-0697 | Grok poll pattern |

Grok's median settled Code task is already **184** tool calls in **47 min**: many short reads and greps, plus a poll per backgrounded build. Claude's median is **79** calls in **28 min**. The 40–200 band on the card is this tool-call count, and Grok sits at the top of it even when the wall is ordinary.

Report writing is not a measured multi-minute phase. On Grok Code `b5e6521b` (CARD-0708, 38 min, 104 tool calls) the last test command is 15:35:41Z and `TurnEnd` is 15:36:24Z. The long Codex reviews are still talking about checkpoints in their slowest 4–6 minute narration gaps, not composing the report.

## Build and test cost per machine

### Full `tests/Antiphon.Tests` build

Compiler `Time Elapsed` lines in Claude tool results (22 lines across the day). The short ones (2s, 12s, 18s) are not a test-project build. The ones next to `dotnet build tests/Antiphon.Tests`:

| Machine | Time Elapsed observed on test-project builds | Command-gap median for any `dotnet build` |
|---|---|---|
| Linux | 1:01, 1:04, 1:10, 1:12, 1:14, 1:24, 1:27, 1:47, 1:52, 2:01, 2:13, and one review build at 3:47. Same task `d9b1f124` then rebuilt in **0:45** and **0:39**. | **63s** (32 gaps ≥20s, p90 113s, max 218s) |
| Windows | 1:48, 2:17, 2:29, 3:03, 3:15 | **156s** (5 gaps, p90 190s, max 221s) |

Linux test-project builds cluster around **1–2 min** once the tree is warm, and a repeat on the same output drops under a minute (`d9b1f124`: 1:52 then 0:45 then 0:39). Windows test-project builds cluster around **2–3 min**. The gap median agrees with the compiler timer.

### Unit lane

Foreground commands whose text contains `Category=Unit` (Claude; Grok's unit runs are inside poll gaps, so they are not in this table).

| Machine | Runs | Min | Median | Max |
|---|---:|---:|---:|---:|
| Linux | 25 | 110s | **173s** | 299s |
| Windows | 2 | 224s | 224s and 340s | 340s |

Examples: Linux `1d94da9e` CARD-0650, `run-checkpoint.ps1 -Name UNIT -NoBuild … [Category=Unit]`, gap **129s**. Linux `70302dc6` CARD-0660, gap **143s**. Windows `88485f49` CARD-0672, `UNIT-lane`, gap **224s**. Windows `3e24ff4a` CARD-0664, `CP-9-land` with the Unit filter, gap **340s**.

### Ordinary checkpoint (class filter, not the unit lane, not a land-class name)

| Machine | Runs | Min | Median | p90 | Max |
|---|---:|---:|---:|---:|---:|
| Linux | 141 | 20s | **98s** | 215s | 601s |
| Windows | 12 | 27s | **80s** | 275s | 390s |

The Windows median is **12 Claude commands**, not the Windows Codex reviews. Those reviews have no command rows, and `8747995c` narrates a checkpoint still running after an hour. Do not read 80s as "Windows checkpoints are faster."

### Git-heavy land classes

Commands naming `Land*` / `GuardedWorktree` / `WorktreeRetirement`.

| Machine | Runs | Median | The long ones |
|---|---:|---:|---|
| Linux | 9 | **161s** | `4aa8cc21` CARD-0664 CP-7 Worktree filter **602s**; `f94c8fa4` CARD-0672 **208s** and **161s** |
| Windows | 5 | **219s** | `3e24ff4a` CARD-0664 `TaskWorktreeRetirement` **603s**; `88485f49` CARD-0672 `AgentTaskLandDispatch` **600s** |

Those three ~600s runs were wrapped in `timeout 590` / `timeout 598`. The gap equals the wrapper plus a few seconds, so the run **did not finish inside the agent's timeout**. Ten minutes is a lower bound for those land-class invocations, on both machines. `d9b1f124`'s land-class `RED` checkpoint returned in **210s** and did finish inside its wrapper.

### Stalls actually in tool results

- **One** `MSB3027` copy failure: Claude Code `f7833ef2`, CARD-0660, Linux, `Could not copy "…/Antiphon.FakeClaude/bin-c660r/…"`. That is the FakeClaude apphost collision `UseAppHost=false` is meant to avoid.
- Windows deploy tasks on CARD-0692 (`b75a3769`, `64dc1fff`, `ab435579`, `7984e409`) hit "being used by another process" while **deleting worktrees**, not while building tests.
- "node reuse" appears in narration (agents saying they disabled it, and a Plan task reading the docs). No tool result today is a node-reuse hang.

Linux Claude Code passed `UseAppHost=false` on **45** commands and omitted any `UseAppHost` flag on **9** builds. `run-checkpoint.ps1` already adds `UseAppHost=false` off Windows; the misses are raw `dotnet build`.

## What one manifest command would save

The runner still has to execute the tests. Savings below subtract only the ceremony the transcript shows, using settled medians.

| Typical task | Tool calls now | Ceremony (launches + polls + TRX reads) | Calls left if that ceremony becomes 1 run + 1 report read | Wall that stays |
|---|---:|---:|---:|---|
| Code, Claude, Linux (n=21) | 79 | 10 + 0 + 6 = **16** | **~65** (about 14 calls, 18%) | Median 28 min. ~10 sequential runs at the 98s CP median is ~16 min of that. |
| Code, Grok, Linux (n=6) | 184 | 8 + 14 + 1 = **23** | **~163** (about 20 calls, 11%) | Median 47 min. The 14 poll gaps (median poll **112s**, p90 **123s**) **are** the backgrounded build and test. |
| Review, Claude, Linux (n=7) | 47 | 6 + 0 + 3 = **9** | **~40** (about 7 calls) | Median 20 min. |
| Review, Claude, Windows (n=3) | 72 | 9 + 0 median (21 polls summed across the three) | about 10 calls on the median; more where a poll loop ran | Median 73 min. |
| Code, Claude, Windows outlier `3bf6c1f4` | 191 | 94 polls plus the launches | **~90 calls**, almost all of them the `until grep EXIT CODE` loop | 185 min, because each poll waited ~2 min of real test time. |

Grok example, settled, task `b5e6521b` CARD-0708 (38 min, not a median-buster): `dotnet build tests/Antiphon.Tests … UseAppHost=false` returned in **24s** (backgrounded), then `get_command_or_subagent_output` gaps **92s** and **112s**. The same shape repeats for the green build. One foreground wait would have been two tool calls instead of six for those two builds, and the same minutes.

Codex wall does not move in this table because the calls were never stored. The Windows Codex review bucket (17.6 h, median 86 min, one 240 min ceiling failure) is where a single manifest report would have the most operator-visible effect, and this investigation cannot say how many calls it would delete there.

### Ranked by measured effect

1. **One command, one wait, one report.** Cuts about 14 tool calls on a typical Linux Claude Code task and about 20 on a typical Linux Grok Code task. Cuts about 90 tool calls on a Windows `until` loop like `3bf6c1f4`. Does not cut the median Code wall in half. The wall moves only if independent rows overlap; today they are sequential (Linux CP median 98s, and the long tasks run those one after another).
2. **Keep the test time visible.** Unit lane is **~3 min** Linux (25 runs, median 173s) and **4–6 min** on the two Windows runs. Ordinary class checkpoints are **~1.5 min** Linux (141 runs). Git-heavy land classes are **~3 min** median and **≥10 min** when the agent wrapped them in `timeout 590` (both machines, three runs at 600–603s). A Windows Codex review narrated a checkpoint past **one hour** and then hit the 4-hour role ceiling (`8747995c`).
3. **`UseAppHost=false` as the default on raw Linux `dotnet build`.** One real `MSB3027` today (CARD-0660, `f7833ef2`). Agents already pass the flag by hand most of the time (45 vs 9). `run-checkpoint.ps1` is already covered off Windows.
4. **Incremental rebuilds, not a second cold build.** The only paired compiler timings are `d9b1f124`: **1:52**, then **0:45**, then **0:39** for `tests/Antiphon.Tests`. That is about **one minute** saved on a Linux repeat. Red-then-green still has to compile the edit; the win is the warm `obj`/NuGet cache, which those numbers say is already happening inside one task. No tool result today shows a node-reuse hang to price separately.

## Uncertainties

- Codex command counts and per-checkpoint durations are not in the transcript, by the normalizer cited above. The hour-long Windows CP is the agent's narration, not a TRX duration.
- Grok command output is not in the transcript. Poll gaps include the build. They are not extra idle time on top of a finished build, except for the few seconds between a poll returning and the next poll being issued (`b5e6521b`: a 2s gap between two polls, then the next real wait).
- A shell line that both adds a worktree and starts a unit run is counted once, under the test class. A handful of the 25 Linux unit gaps may include that prefix. The minimum of those gaps is still 110s.
- Eight tasks are still dispatched, six of them Grok Code on Linux, and their tool counts are still climbing.
- No Grok Review, no Grok Plan, and no Windows Plan task exists in this window, so those cells are empty rather than sampled.
