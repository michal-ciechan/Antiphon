# Codex Debug 0751ffb8: fatal account-bootstrap timeout

Codex 0.160.0 exited during account/workspace routing discovery, before receiving the Debug brief. The process ran for about 44 seconds; the reported 7m34s was the task dispatch-to-settlement interval. Recommend rerunning the group-10 Debug on **desktop, ClaudeCode/High (opus)**. The investigation changed no code or runtime configuration, ran no builds/tests, and did not restart or touch the live Windows rc qualification.

Investigated by task `6af8d750` on 2026-10-22. Subject task: `0751ffb8-e732-420c-9d5c-98433d45f3dc`; session: `700478bc-798b-47f1-8f94-0bcaac056f42`; requested model: `gpt-6-astra`. Inspected source HEAD: `3f54023d6b715fe2653014dc82187d358c0193b6`.

The exact terminal error was:

```text
Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603)
```

The raw terminal error has no individual timestamp. It is the final output before the pty-host recorded exit at **2026-10-22 18:39:51.9460110 UTC / 19:39:51.946 BST**, and it appears in the startup capture taken at `18:39:52.0807857Z`.

| Event | UTC on 2026-10-22 | Evidence |
| --- | --- | --- |
| Task created | 18:36:53.966279 | Task detail; initially held behind a repository mutation lease |
| Stored task dispatchedAt | 18:38:35.197818 | Task summary |
| Actual child launch | 18:39:08.0167638 | pty-host launched `node.exe`, PID 285476, ModernConPty |
| Child exited | 18:39:51.9460110 | `Child exited (code 1, reason ProcessExited); lingering for runner ack` |
| Readiness failure recorded | 18:39:52.079 | `codex-startup not-ready reason=Unknown elapsedMs=43954 mcpSeen=False` |
| Session ended | 18:39:52.04891 | Task detail's session record |
| Task failed by reconciler | 18:46:09.782984 | Failed event; server log confirms dead-session reconciliation |

The actual child lifetime was 43.9 seconds. Settlement followed process exit by about 6m18s. Antiphon's readiness wait ended on the exited process; this was not evidence of a 60-second ready-gate kill. The host logged runner acknowledgement and normal host shutdown after the child exit.

At death the TUI was bootstrapping, displaying its welcome screen and `permissions: YOLO mode`. The authoritative server transcript GET returned **zero entries, lastSequence=0**. Its runner sidecar records `firstInputAtUtc=null`, `transcriptPath=null` and `format=codex`. No prompt or tool execution occurred, so the agent had not read `C:\Antiphon\releases\...` or any large qualification log. No native rollout path was bound; provider credential files were not inspected.

This establishes an **account/bootstrap routing timeout**, not a model usage failure. There is no quota/429 message, sign-in/401 rejection, sandbox refusal, command-line overflow, or allocation/panic message in the captured output. The launcher reached the Codex TUI successfully. A bounded Windows System/Application event check for 19:37-19:41 BST found no matching resource-exhaustion or Codex/node crash event. The reported 2.4 GB free memory therefore does not establish an OOM cause. A lower-level network, service, or host-scheduling reason for discovery timing out cannot be distinguished from the retained evidence; do not call this proven invalid authentication or a demonstrated Antiphon launch defect.

The requested project-scoped query was repeated at **19:58:33 BST**:

```text
GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&status=Failed
```

It returned 463 Failed tasks overall, nine completed since local midnight (`2026-10-21T23:00:00Z`), and **two Codex tasks today**. Only `0751ffb8` had `ProcessExited, code 1`. The other, `5fdc4967-304e-4555-9613-6fd8b8471030`, failed at `10:03:40.452894Z` before a session existed: `Dispatch failed before a session existed: An error occurred while saving the entity changes. See the inner exception for details.` Thus this is a singleton in today's current Failed-task snapshot, not an established recurring same-day Codex exit class. This census does not cover other projects or failures hidden by later task retries/status changes.

A separate, demonstrated diagnostics defect is now **CARD-1508**, Backlog, card `4f7787f0-621b-4bf6-8b82-7b6694d7d03e`, Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`: *Preserve Codex fatal account-bootstrap errors in failed task outcomes*. The actual fatal text survives in local captures, but the caller sees only `Process exited (ProcessExited, code 1)`, and the startup classifier reports `Unknown`. At the inspected source, `RunnerCodexAdapter.cs` stores the not-ready frame; `CodexStartupReadiness.cs` has no fatal-bootstrap classification; `AgentSessionRuntime.cs` assigns the generic exit failure reason. This diagnostics gap is separate from the timeout's upstream cause.

Before filing, complete `card.ps1 -All` searches covered `workspace routing`, `account/read`, `startup failure`, `ProcessExited`, and `Codex` (284 results, `truncated=false`, no continuation). Full candidate reads covered CARD-0497 (fixed command-line overflow), CARD-0777 (local startup capture/update handling), and CARD-0907 (0.160.0 screen qualification). None covers this failure-reason propagation. CARD-1508 asks for bounded, sanitized classification and outcome propagation with the captured frame as a regression fixture; it does not authorize automatic retries, provider switching, auth changes, quota holds, or larger readiness timeouts.

For immediate recovery, commission the original read-only group-10 Debug with explicit `-Kind ClaudeCode -Level High -Runner desktop`, retaining its original goal and Windows evidence paths. This avoids another attempt through the failing Codex bootstrap path for the urgent investigation. The evidence does not justify disabling Codex globally or changing its credentials. No replacement task was dispatched by this investigation.

Evidence is retained outside the worktree at **`C:\logs\antiphon\session-runner\investigations\2026-10-22-6af8d750`**:

- `target.ansi.log`, `target-pty-host.log`, `target-startup-capture.txt`: exact error, process exit and timestamped captured screen.
- `target-task.json`, `target-transcript.json`, `target-transcript-sidecar.json`: authoritative task/session facts and absent first input/rollout binding.
- `target-server-excerpt.txt`: relevant lines from `C:\src\Antiphon\server\logs\antiphon-20261022_002.log`, including launch, readiness and reconciliation.
- `failed-tasks-final.json`, `other-codex-task.json`: scoped census and contrasting dispatch failure.
- `codex-cards.json`, `card-description.md`, `created-card.json`: duplicate-search inventory and new Backlog receipt.

The shared runner supervisor/Serilog logs were checked but contained no target-session match; the per-session pty-host log and server startup capture supplied the direct evidence. Official OpenAI documentation search did not establish the cause of this exact timeout; the [configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference) was consulted only for context, and the diagnosis above rests on local incident evidence.

--- next stage ---
next: none
handoff: Investigation complete. Caller should rerun the original group-10 Debug on desktop with explicit ClaudeCode/High (opus); schedule CARD-1508 separately through its ordinary pipeline.
artifact: C:\Antiphon\worktrees\card-task-6af8d750\docs\investigations\2026-10-22-card-0751ffb8-codex-exit-debug.md
