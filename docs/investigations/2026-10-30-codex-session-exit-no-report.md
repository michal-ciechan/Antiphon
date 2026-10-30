# Why Codex task e1bfe7eb died with no report (2026-10-30)

Investigate task 80d88957. This ran read-only on server2 (runner container `4551c5a7852f`) at rc
`e4c9547e10e3da59fd20684b49c029cdc6063fa8`. Evidence came from the server API (`GET /api/agent-tasks/...`,
`GET /api/sessions/{id}/transcript`, `GET /api/session-runners/server2/slots`), the runner and
pty-host logs, this session's Codex rollout and the Codex log DB (both opened read-only), and a
read-only SQL transaction against the desktop Postgres.

## Verdict

**e1bfe7eb is not the false-Failed settlement class.** Its Codex process died abruptly 48 s after
launch, in the middle of a turn. No final message existed, so no report was lost. The dead-session
sweep settled the task correctly. Two things in the evidence are wrong:

1. **The failure text overstates what is known.** It says "the agent process exited on its own
   (ProcessExit, exit code 0)", which reads as a clean exit. On Linux, Antiphon records exit code 0
   for a child killed by any signal (proof in section 3). The process left no clean-exit output and
   did not restore the terminal, which fits a signal death or an abort.
2. **The killer is not identified.** The runner, the peer agents, the OOM killer, quota, context
   size and auth are all ruled out (section 4). The signal number is thrown away at
   `src/Antiphon.Agents.Pty/PtySession.cs:41`, so the stored evidence cannot say which signal it was.

**Frequency: this has happened once in 7 days.** It is the only Linux session with
`TerminationSource=ProcessExit` among 653 Linux sessions that ended in that window, and the only
task of 877 whose reason is "Session died … ProcessExit, exit code 0".

## 1. Timeline (session d2e50140-0248-43f6-bfce-0aa03b7fcc47)

| UTC 2026-10-30 | Event | Source |
|---|---|---|
| 10:19:15.95 | Task created (Custom, Codex High, runner server2, CARD-1616) | task events |
| 10:20:55–10:21:02 | Remote prep held, then ready | task events |
| 10:21:05.389 | Dispatched to `task-e1bfe7eb` (gpt-6.1-sol) | task events |
| 10:21:06.21 | pty-host launched `/usr/local/bin/codex`, child pid 15367, UnixPty (Porta) | `/tmp/antiphon-pty-hosts/logs/d2e50140….log` |
| 10:21:07.4 | Codex session `01a18c53-d7c5-76b3-a5cc-44d3d8e35cb3`, cli_version 0.160.0, originator codex-tui | rollout line 0 |
| 10:21:10.58 | UserPrompt (pointer brief) recorded | transcript seq 1 |
| 10:21:13.38 | First tool call (`cat .antiphon/inbox/5dccb2c2….md`) | rollout line 12 |
| 10:21:13–10:21:50 | 13 shell commands, all read-only (cat/rg/sed/git show/card.ps1 get); 2 exit 1 from a missing glob | rollout CommandExecution items |
| 10:21:43.541 | Last AssistantText (commentary phase) | transcript seq 3 |
| 10:21:43.733 | Last row Codex flushed to `logs_2.sqlite` for `pid:15367` | Codex log DB |
| 10:21:50.324 | Last rollout line: `token_count` after a tool output. `needs_follow_up=true`; the next model request is in flight | rollout line 67 |
| ~10:21:53.44 | Last PTY bytes: the spinner and title (`⠋ Fix CARD-1616 …`) still redrawing | `.ansi.log` mtime / tail |
| 10:21:53.515 | `Child exited (code 0, reason ProcessExited)` | pty-host log |
| 10:21:53.758 | Session row `Stopped`, `TerminationSource=3` (ProcessExit), `ExitCode=0` | Postgres `AgentSessions` |
| 10:25:35.228 | Task `Failed` by the dead-session sweep, about 3.7 min later | task events |

The branch `feat/card-task-e1bfe7eb` is still at `e4c9547e1…`: nothing was pushed then or since.

### Transcript: all entries (there are only 3; Codex tool calls are not normalized)

1. `10:21:10.581Z UserPrompt`: "[antiphon-task:e1bfe7eb] role=Custom tier=High … Read it in full before you do anything else: '.antiphon/inbox/5dccb2c2-….md' …"
2. `10:21:13.366Z AssistantText`: "I'll read the full brief first, then follow its scope and reporting requirements."
3. `10:21:43.541Z AssistantText`: "The card is still accurate at this tip: the receipt validator rejects configured output names when admission times out before any output path exists. The Windows report places the 814 seconds in cleanup; I'm tracing that path, though its Windows cost will need native measurement."

Final assistant message: **none**. Entry 3 is a `phase: "commentary"` progress line (rollout line 53),
and tool calls followed it. There is no TurnEnd, `task_complete` or `turn_aborted`. The last 15
rollout items (lines 53–67) are: the commentary message, then three rounds of exec tool call → 2
CommandExecution completions → tool output → token_count, with reasoning items between them. The
stream simply stops.

## 2. Settlement path and what it can tell apart

- The message comes from `server/Application/Services/AgentTaskLiveness.cs:136-143` and the
  `DescribeStop` clause at `:156-163`. The prefix is `:28`.
- The sweep that acts on it is `AgentTaskDispatcher.FailDeadSessionTasksAsync`
  (`server/Application/Services/AgentTaskDispatcher.cs:3044`). Before it fails a task that has
  transcript rows, it calls `_replies.OnTurnEndAsync(settleSession)`
  (`AgentTaskDispatcher.cs:3190-3204`, CARD-0288). A report sitting in the transcript therefore
  settles the task instead of failing it. That is the only "final message" handling on this path.
- It does **not** separate "exited 0 after a final unmarked message" from "exited 0 mid-work".
  `ClassifyFailure` reads only the session row's status, termination source, exit code and whether
  any transcript rows exist. For e1bfe7eb the difference does not matter: there was no final message
  to capture, so the classification and the settlement were correct.

## 3. On Linux, "exit code 0" does not mean a clean exit

- `src/Antiphon.Agents.Pty/PtySession.cs:41` forwards `PtyExitedEventArgs.ExitCode` and nothing else.
  The Porta.Pty 1.0.7 assembly also carries an `exitSignal` field, so the library keeps the signal
  separately and Antiphon never reads it.
- Antiphon's own kill is `Process.Kill(entireProcessTree: true)`, which is SIGKILL on Linux
  (`PtySession.cs:55-66`, reached from `PtyAgentRunner.cs:521-525`). Every one of those kills is
  stored as **exit code 0**:
  - In the pty-host logs retained since 2026-10-29T23:52Z there are 76 child exits: 75
    `code 0, reason KilledByRequest` (25 claude, 19 codex, 31 grok) and 1
    `code 0, reason ProcessExited` (d2e50140).
  - In Postgres, over the last 7 days, Linux sessions with `TerminationSource=2` (SystemRequest)
    have `ExitCode` 0 in 476 rows, -1 in 5 and null in 164. None has a non-zero code. (Desktop
    ConPTY rows do carry real codes, e.g. `1073807364`.)
- So "ProcessExit, exit code 0" on a Linux runner means the child ended, either cleanly or from
  any signal.

## 4. Cause of the death: what is ruled out and what is left

| Candidate | Evidence | Verdict |
|---|---|---|
| Clean TUI quit (`/quit`, Ctrl-D, Ctrl-C ×2) | The PTY stream ends mid-spinner. It has no `?1049l`/`?2004l` restore and no "codex resume" banner. The Codex log tail (10:21:43–10:21:53) was never flushed | ruled out |
| Turn ended and the CLI exited | This is the interactive TUI, not `codex exec`, and the rollout has no `task_complete` | ruled out |
| Quota or rate limit | Last `rate_limits`: primary 50% of a 10080-minute window, `rate_limit_reached_type=null`, credits present | ruled out |
| Context limit | Last request 69,149 of 475,000 tokens; `token_limit_reached=false` | ruled out |
| Auth | Runner Codex auth probe `loggedIn=true` at 10:21:05; the model was streaming normally | ruled out |
| Approval/sandbox prompt | No approval item. Every command completed in ≤1.9 s | ruled out |
| Long foreground command (build-slot wait) | The longest command took 1.9 s. Total life was 48 s | ruled out |
| Antiphon runner kill (slot release, orphan sweep, Herdr by-pid, custody) | The runner log has no Kill/ReleaseSlot for d2e50140 and is silent from 10:21:10.66 to 10:22:30. Every by-pid kill path logs a warning (`SessionRunnerRuntime.cs:2333/2923/4269`). The pty-host reason is `ProcessExited`, not `KilledByRequest` | ruled out |
| Peer agent `kill`/`pkill` | The 6 other live server2 sessions were checked. The 4 Grok transcripts show only read_file/grep in 10:20:30–10:22:10. The 2 Codex rollouts (acfe7ff6: 86 commands, 8e7e03c1: 13) have no kill/pkill/codex command before 10:22:30 | ruled out |
| Kernel OOM or segfault | Container cgroup `oom_kill 0`, `failcnt 0`. Host `dmesg` has no oom/segfault/killed-process line. MemAvailable 98 GB of 132 GB | ruled out |
| Host load | 7 sessions live on server2 (4 Grok, 3 Codex) | not a factor |
| **A signal from an unidentified source, or a Codex-internal abort with no visible output** | Consistent with every artifact. Not provable because the signal number is discarded | **open** |

Remaining uncertainties: the signal number; whether Codex 0.160.0 can abort without writing to the
terminal (for example `panic=abort` with the panic hook writing only to the unflushed log); and
pid reuse (`pid_max=32768`) by a process outside Antiphon. No retained record names pid 15367
anywhere except its own pty-host log.

## 5. Quantification (tasks created since 2026-10-23T00:00Z, Antiphon project, 877 tasks)

| group | Succeeded | Canceled | Dispatched | SessionDied exit0 | SessionDied other | ModelWait 20m | RoleCeiling | Failed other | total |
|---|---|---|---|---|---|---|---|---|---|
| ClaudeCode | 150 | 14 | 2 | 0 | 1 | 0 | 2 | 5 | 174 |
| Codex Custom Frontier | 13 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 14 |
| Codex Custom High | 4 | 0 | 0 | **1 (e1bfe7eb)** | 0 | 0 | 0 | 0 | 5 |
| Codex Debug Frontier | 9 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 9 |
| Codex Debug High | 121 | 12 | 1 | 0 | 0 | 2 | 3 | 1 | 140 |
| Codex Investigate Frontier | 11 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 12 |
| Codex Investigate High | 18 | 3 | 0 | 0 | 0 | 0 | 0 | 0 | 21 |
| Codex Plan Frontier | 44 | 0 | 1 | 0 | 0 | 5 | 0 | 0 | 50 |
| Codex Plan Medium | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 |
| Codex other roles (Code/Merge/Review/…) | 98 | 34 | 0 | 0 | 0 | 4 | 1 | 20 | 157 |
| Grok | 278 | 7 | 3 | 0 | 0 | 0 | 0 | 0 | 288 |

- No task is Blocked. None of the 73 Canceled or re-armed tasks has a "Session died"/ProcessExit
  event either.
- The only other "Session died" task is ClaudeCode `5c904345` (Failed session, "Automated Claude
  input withheld: stale-observation"). It is a different shape.
- Grok has 0 and ClaudeCode has 0 with the exit-0 reason.
- **The false-Failed Codex plans (383955b2, 107cafd6, c90aa6c9, dcbf1d18, 30f56bd5) are all
  `Mid-turn and waiting on the model for 20m`.** That is a different class: the model-wait clock
  (CARD-1604, CARD-1610). The Codex normalized transcript has no ToolCall entries.
  `CodexTranscriptNormalizer.cs:276-279` deliberately maps no CommandExecution to a transcript
  row for it (d2e50140's transcript holds 3 entries against 13 commands). So "Last transcript entry:
  AssistantText, 20m ago" can be true while Codex is busy running tools. This is noted for
  CARD-1604 and was not investigated further here.
- Runner, version and time-of-day correlation is not meaningful with n=1: server2, codex-cli
  0.160.0, 10:21Z, task age 1.8 min at launch, brief 2,276 characters (pointer file 5,197).

## 6. Recommendation

- **This is not a settlement defect.** CARD-1610 S2/S3 (terminal-but-live sessions, ceiling
  awareness) does not cover it and does not need to.
- **It is a rare abrupt Codex process death.** No brief rule would have prevented it: the agent was
  48 s in and doing ordinary reads.
- **The defect worth filing is evidence quality.** The Linux exit record drops the signal, so every
  future occurrence will again read as "exited on its own (exit code 0)". Search done:
  `card.ps1 search 'Session died'`, `'exit code 0'`, `'exit signal'` and `'Codex process exited'`
  found no card for this. CARD-1190 is related (a ready-gate kill stored as ProcessExit) but is
  closed and Windows-shaped. Proposed card, **not filed**:
  - **Title:** Linux pty exit drops the signal: a signal-killed agent is recorded as "exited on its
    own (exit code 0)"
  - **Body:** `PortaPtySession` (`src/Antiphon.Agents.Pty/PtySession.cs:41`) forwards only
    `PtyExitedEventArgs.ExitCode`. On Linux, SIGKILL from our own `Process.Kill` is stored as
    `ExitCode=0` (all 476 non-null SystemRequest rows in 7 days; 75/75 pty-host KilledByRequest
    lines). Task e1bfe7eb (session d2e50140, Codex 0.160.0) died mid-turn with no terminal restore
    and was failed with "the agent process exited on its own (ProcessExit, exit code 0)". Carry the
    signal (or a raw wait status) through PtyHost `ExitedMessage` and the manifest to
    `AgentSessions`, and have `AgentTaskLiveness.DescribeStop` say "killed by signal N" or "exit
    code 0 (signal unknown)" instead of implying a clean exit. Investigation:
    docs/investigations/2026-10-30-codex-session-exit-no-report.md.
- **Retry 185d0094** (CARD-1616 Debug, Codex High, session ab099091): at 10:45:08Z it was
  `Running, working`, 15.5 min in, with its last transcript entry at 10:43:39. That is already 20×
  e1bfe7eb's lifetime. Base rate is 1 in about 400 Codex tasks per week, so the risk of a repeat is
  low. The orchestrator should watch `GET /api/agent-tasks/185d0094` `session.status`/`endedAt`
  and, if it dies, read `/tmp/antiphon-pty-hosts/logs/ab099091….log` and the rollout tail before
  re-running. Its larger exposure is the separate ModelWait-20m class, because the Codex transcript
  shows no tool activity during long build-slot waits.

## Not done, noted

- Fix idea (one line): plumb the Porta exit signal into `ExitedMessage`/`AgentSessions` and word
  `DescribeStop` honestly. This is design work for a Plan, not done here.
- Not done: a native reproduction of Porta's signal → `ExitCode=0` mapping in a toy program. The
  stored rows above already establish it.
