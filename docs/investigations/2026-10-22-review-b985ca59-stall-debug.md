Review b985ca59 stalled at an interactive Claude permission dialog **before its Bash command started**. It did not hang inside the census/apply harness, a build-slot wait, or a checkpoint. Debug task 43ac5232 inspected the session read-only on 2026-10-22, approximately 19:55–20:03 UTC; no input, approval, interruption, restart, kill, source repair, or subject-worktree cleanup was performed.

Subject: `b985ca59-f4e5-47e5-aca5-e374f2f100ad`, CARD-1493 S1 repair Review, ClaudeCode/High on server2. Session: `4915f8bb-6d79-4742-88fd-994b4397fb04`; mirror: `/work/worktrees/task-b985ca59`; source remains clean at `749a26c470705ab2a8042e3945eb3bc7d4f3e56f`.

| UTC | Evidence |
| --- | --- |
| 17:52:14 | Pty-host launched Claude PID 12286, parent 12271, UnixPty. Native transcript identifies Claude Code 2.1.280. |
| 17:58:17 | Checkpoint run `20261022-175314-423e` completed, exit 0. |
| 18:08:43 | Previous apply run returned: 354 numeric and 2 nonnumeric cases checked, 355 failures caused by reuse of an existing output manifest. The reviewer proposed fixing that harness. |
| 18:08:47.570 | Normalized transcript sequence 51: Bash `toolu_01HErjZiaozvAqGVotStUFGv`; no corresponding result in either normalized or native transcript. |
| 18:08:48.070 | Last write to this session's ANSI capture. Current rendered snapshot, sequence 9068, shows the permission prompt below. |
| 18:40:44.396 | `TaskProgressStalled`: “Working with no novel progress for 31m.” |
| 19:02:31.781 | Caller refinement: `interruptWritten=refused:kind-not-grok refinementDelivered=pending`. |
| 19:39:43.471 | Task Failed on the 90-minute LocalExecution deadline; failure explicitly says the session was not killed. |

The exact final command, reconstructed from native `tool_use.input.command`, was:

```sh
S=/tmp/claude-1654/-work-worktrees-task-b985ca59/4915f8bb-6d79-4742-88fd-994b4397fb04/scratchpad && cd $S && sed -i "s#const r=cp.spawnSync#try{fs.unlinkSync(out+'/x.yaml')}catch{}; const r=cp.spawnSync#" apply.js && rm -f tip/.antiphon/apply/* && timeout 590 node apply.js $S /work/worktrees/task-b985ca59/tools/Antiphon.Checkpoints/bin-rb985tool/Antiphon.Checkpoints.dll
```

Its Bash tool input explicitly sets `timeout: 600000`; the existing script also sets `spawnSync(... timeout:60000)`. These execution timeouts cannot expire a permission wait before execution. The snapshot reads:

> Compound command contains cd with write operation - manual approval required to prevent path resolution bypass
>
> Do you want to proceed? 1. Yes / 2. No
>
> Esc to cancel · Tab to amend

The preceding display mentions `running PreToolUse hook`; the final state is an approval dialog, not a live hook child. The repository hook is configured with a five-second timeout, is disarmed for worker tasks, and does not contain this denial text. Attribution to a particular provider/global hook is unproven and unnecessary to the observed cause. Despite bypass-permissions mode, this particular command requires manual approval.

Corroboration: `apply.js` still has its original 18:03:10 modification time and lacks the proposed `unlinkSync` edit. Its old scratch outputs remain. An ancestry-based `/proc` census found only pty-host 12271 and Claude 12286, no shell, Node harness, dotnet, timeout, or hook descendant; no other process had the subject/scratch cwd. Both processes were alive at 20:00:26Z. This excludes a currently deadlocked tool child. Matching native and normalized tails exclude a missing Antiphon ingestion/delivery of an already-produced result; the result does not yet exist. The visible approval gate is affirmative evidence, not an inference from silence.

Resource observations:

- **Runner seat:** still occupied. `/api/session-runners/server2/slots` reports Running, `occupiesCapacity=true`, PID 12286, `openTaskId=null`, `orphan=false`. A terminal task is not an exited session; the orphan flag does not establish a release.
- **Build leases:** none. Runner `/build-slots` returned `occupied=0`, `leases=[]`, `waiters=[]`. Executor log records release of build lease `cb536d3d-e265-4674-bed8-6bb2f819120d` at 17:56:45.798Z and test lease `662a3e92-5778-43b5-995d-99196afee80d` at 17:57:10.489Z.
- **Checkpoint:** finished, not waiting. State is `done`, exit 0, executor PID 13985 absent. Offline validation of its existing `report.json` using `scripts/validate-checkpoint-receipt.ps1 -ExpectedSourceSha 749a26c470705ab2a8042e3945eb3bc7d4f3e56f -Rows CP-2` returned `CHECKPOINT SOURCE VALID ... rows=1`. No new build/test run was needed.
- **Repository:** this ordinary prepared runner mirror does not hold a desktop repository mutation lease during execution (`AgentTaskDispatcher.cs`, `LaunchesPreparedMirror` admission and early Worktree lease disposal). Its task has no land underway; no local worktree `index.lock` exists. The desktop's in-memory lease owner was not directly observable from this container, so this is execution-contract evidence, not a claim to have inspected every desktop lock. The original pre-dispatch lease wait ended before launch and is unrelated.

Preserved checkpoint line:

```text
CHECKPOINT CP-2 commit=749a26c470705ab2a8042e3945eb3bc7d4f3e56f build=ok filter=/*/*/CheckpointImportTests/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-b985ca59/.antiphon/checkpoints/20261022-175314-423e/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=749a26c470705ab2a8042e3945eb3bc7d4f3e56f sourceState=clean buildSource=verified
```

The supported `-Refine -Interrupt` path cannot interrupt Claude: `MidTurnInterruptPolicy.cs:81` rejects every non-Grok kind. `AgentTaskReplyService` first queues WhenIdle, then evaluates the interrupt. Row `3561c380-9598-4e69-af14-debc82f87b77` remains Pending with zero attempts and no confirming UserPrompt. Once Failed, new Refine calls also refuse on task status. This does **not** mean Claude itself cannot be interrupted: the visible dialog offers Esc, raw input exists, and CARD-0512 records a historical successful raw Ctrl+C test. None was attempted here; provider-specific conditional interruption still needs qualification.

The second gap is classification. The queue reports `modalBlocked=false` because its persisted modal projection covers Remote Control episodes (`SessionMessageQueueService.cs:5473`), while `TaskDeadlinePolicy.cs:264` maps ToolCall directly to LocalExecution. Thus an unstarted command awaiting approval looked like a running local tool for 90 minutes. Generic stall detection worked; it neither identified the approval decision nor provided a usable Claude refinement path. Automatic killing is not the proposed fix.

Complete `card.ps1 search ... -Board Antiphon -All` enumerations for interrupt, stall, permission, approval and deadline were checked; CARD-0491, CARD-0680, CARD-0512 and CARD-0294 were read in full. Filed **CARD-1517** (`10dc26c1-71c6-43b3-a006-15b16df4d4ee`, Backlog) for one opt-in conditional Claude Ctrl+C with safe refusal and transcript-confirmed steering, and **CARD-1518** (`123c4833-da91-41cb-b14a-5015bb064a53`, Backlog) for permission-aware stall/deadline reporting and explicit terminal-task/live-session recovery. CARD-0680 already owns dialog detection/direct-send protection; CARD-1518 reuses that work rather than duplicating it. CARD-0491 shipped Grok only; CARD-0294 addresses end-of-turn approval requests; CARD-0512's unexplained older hang is not this evidenced approval gate.

Caller decision: arrange explicit cancellation/recovery of the visible approval wait, or commission a fresh Review while separately resolving the retained seat. Do not send an unqualified Enter/Yes. Preserve the existing valid CP-2 receipt, scratch evidence and original Code owner `8461f217-2ce5-4f23-a05c-b2dc7cda1705`; remaining Review acceptance is incomplete, so this Debug report supplies no land approval.

Evidence sources: server task/transcript/messages/incidents/slots reads; runner `/sessions/{id}/snapshot` and `/build-slots`; `/state/runner-logs/session-runner-20261022.log`; `/tmp/antiphon-pty-hosts/logs/4915f8bb6d79474288fd994b4397fb04.log`; the exact native transcript `/state/claude/projects/-work-worktrees-task-b985ca59/4915f8bb-6d79-4742-88fd-994b4397fb04.jsonl`; and the checkpoint run directory above. Local diagnostic captures are gitignored under `.antiphon/debug-43ac5232/`; the decisive observations are preserved in this committed report.
