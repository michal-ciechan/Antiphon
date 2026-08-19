# CARD-0085 — False-negative delivery-failed: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0085 (`a48017bc-3b77-4728-abd4-d37bddc4a0a8`) — a delivery-failed task can be a
false negative: the work completed and pushed, only the transcript-bind verdict was wrong
**Incident:** task `9a5b93a3` (2026-08-18, CARD-0083 Plan, fable/ClaudeCode, worktree-isolated),
session `396f32aa`. Commit `b9ecf40` was already on origin; the orchestrator heard
`[task 9a5b93a3 failed] Boot prompt was never delivered: 10 minutes after dispatch the session
has zero transcript entries`.
**Precedent:** CARD-0055 `GraceConfirmAsync` / `LateConfirmAttemptedMessagesAsync` and CARD-0056
`TryLateConfirmBootPromptAsync` — pull more evidence before acting destructively. CARD-0046's
"Succeeded but loud" (Warning event + incident + caller-facing caveat) is how a recovered
verdict stays visible. Do not invent a new status. Do not loosen CARD-0006's C1–C4.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The Failed report is a watchdog decision on an empty `TranscriptEntries` table, not a C4
"first prompt" bug.** C4 already scans up to 32 recent user prompts
(`TranscriptCandidateProbe._recentPrompts`). The 9a5b93a3 file was refused because **no
recorded user-prompt head appeared in `SessionInputLog`** — the first user record was `"green"`
(5 chars, below `MinMatchChars = 12`, so it can never identify), and the goal text that later
proved the file was ours is not C4 evidence. C4 asks "is a user prompt text we typed?", not
"does later content mention the task?"

C1–C4 stay exactly as they are. The fix is the same shape as CARD-0055/0056, one layer up: when
`FailNeverStartedAsync` (and the dead-session twin) is **about to write Failed** because the
session ingested nothing, look at the working directory for positive evidence the work
happened — new commits on the task's branch, or a cwd-matching transcript whose *later* content
carries a distinctive task needle — and if found, settle **Succeeded with a Warning**, never
silent, never Failed. Failed is what makes a less-careful caller redispatch on top of
already-pushed work.

One Code slice.

## 1. Current shape (verified against the files, 2026-08-19)

### 1.1 What 9a5b93a3 actually hit

`AgentTaskDispatcher.FailNeverStartedAsync` (`server/Application/Services/AgentTaskDispatcher.cs:343`):

```
Dispatched + DispatchedAt older than DeliveryFailTimeoutMinutes (10)
  + AgentSessionId set
  + ZERO rows in TranscriptEntries for that session
    → FailAsync + KillAsync + RemoveEphemeralAgent + parent note
```

The reason string at `:382-387` is the card's report verbatim, including the already-written
hint to check the session if a `TranscriptBindFailed` is on the timeline. The hint is prose. The
method never queries incidents, never looks at the worktree, never opens a JSONL file, and then
kills the session.

The runner side, independently, did the safe thing (`TranscriptTailer.MaybeReportRefusal` `:631`,
then `ReportMissingAfterChildExit` `:661`):

```
[22:08:53] refusing every candidate … 69d97268-….jsonl: no prompt in it matches input
           delivered to this session. Running WITHOUT a transcript.
[22:15:55] liveness sweep marked Exited
[22:15:58] child exited without ever producing a transcript we could identify
```

`TranscriptBindingIncidentService.OnTranscriptFaultAsync` records
`AgentIncidentKind.TranscriptBindFailed = 15`. Nothing consumes that incident on the way to
Failed.

A second sweep can write the same false Failed with a different sentence:
`FailDeadSessionTasksAsync` (`:491`) — open task, session dead, runner not listing it Running,
grace `DeadSessionFailGraceMinutes = 3`. Tick order is watchdog first (`:114`), then
dead-session (`:120`). 9a5b93a3's text is the watchdog's; a session that dies at T+8 with the
same empty table is the other sweep's. Both sites need the same gate.

### 1.2 What C4 actually does (the card's "first prompt" reading is wrong)

| Rule | Where | What it asks |
|---|---|---|
| C1 | `TranscriptClaimRegistry` | unclaimed |
| C2 | `TranscriptCandidateProbe.Cwd` + `CwdMatches` | recorded cwd is this session's |
| C2b | `probe.AgentName` vs launch `--name` | no conflicting `agentName` (absence neutral; `custom-title` is **not** read — CARD-0006 decision) |
| C3 | `EpochOk` / `FirstTimestamp` | first *timestamped* record not older than the child (`--resume` waives) |
| C4 | `probe.ContentMatched` ← `SessionInputLog.MatchesRecordedInput` | **some** retained user prompt's head window (`PromptSubmissionMatch`, 12 / 200 chars) is a substring of what this session was typed |

C4 is re-tested on every probe refresh (`TranscriptCandidateProbe.Refresh` `:108-117`) against
up to `MaxRetainedPrompts = 32` user prompts, newest kept. Local-command and interrupt records
are excluded. Recency is a tiebreak only after a file qualifies (`EvaluateCandidates` `:461`).

So a stray leading `"green"` cannot, by itself, poison C4 for a later matching user prompt. The
9a5b93a3 refusal means: after ~7 minutes and 208 records, **no user-prompt head in that file
was text the runner had typed**. Plausible mechanisms (not this slice): the typed brief never
became a `UserPrompt` (`"green"` submitted instead — CARD-0055's stale-composer shape); the
brief lived on disk and the agent discovered it, so the goal text is in assistant records; the
brief aged out of the 32-prompt window before the input log could match it. The goal text
appearing four times is exactly the evidence C4 is forbidden to use, and that is the point of
CARD-0006.

Do not change C4 to search assistant text or the task Goal. That is how you re-bind the
operator's conversation in `C:\src\Antiphon`.

### 1.3 Distinct from CARD-0064

CARD-0064 is a *persistent, channel-bound* session losing its bind across a **runner restart**
(empty `SessionInputLog`, stale sidecar). 9a5b93a3 is a **first launch** into a fresh worktree.
Same incident kind, different cause, different fix. Do not reopen CARD-0064.

## 2. The fix — pull evidence, then decide

Same posture as `GraceConfirmAsync` (`SessionMessageQueueService.cs:1026`) and
`TryLateConfirmBootPromptAsync` (`AgentSessionService.cs:556`): a destructive Failed is not
allowed on "the transcript table is empty" without asking the working directory. Absence of
ingested rows is not evidence the work did not happen.

### 2.1 When the gate runs

Only on the path that is about to claim **"this task never started / produced nothing
ingestible"**:

- `FailNeverStartedAsync`'s zero-`TranscriptEntries` arm (`:366-387`). The
  `DelegateReportUncorrelated` arm (`:389`) stays Failed — that task *did* start; the report
  could not be attributed.
- `FailDeadSessionTasksAsync`, **and only when that session also has zero `TranscriptEntries`**.
  A dead session that ingested turns is CARD-0021's "no report is coming"; do not widen.

A `TranscriptBindFailed` incident is context for the Warning text, not a gate. Requiring it
would miss "child died before any JSONL, but the worktree has the commit". Recovery still
requires **positive** evidence below; no incident + no commit + no needle is still Failed.

### 2.2 Evidence — two arms, either is enough, both are positive-only

New small type `DelegateBindRefusalRecovery` under `server/Application/Services/`. The
dispatcher does not grow a third git implementation. Inject it optionally (same constructor
pattern as `_replies` / `_runnerClient`) so predating harnesses keep today's Failed.

**Arm A — git, the card's ask, and what would have recovered 9a5b93a3 alone.** Reuse
`GitWorkspaceService.LogOnelineAsync` exactly as `DelegateCheckProbe.GatherGitAsync` already
does (`DelegateCheckProbe.cs:267-280`):

| Workspace | Read | What counts |
|---|---|---|
| Worktree (`WorktreeBranch` + `MergeTargetRef` set) | `MergeTargetRef..WorktreeBranch` | **any** commit — the branch is the task's |
| Shared | `HEAD` `--since=DispatchedAt` | a commit whose subject/body contains a **distinctive needle** (below) |

`null` from git means "could not ask", not "no commits". That is not evidence.

Shared must filter. `C:\src\Antiphon` is the CARD-0006 collision cwd; anyone's commit in the
dispatch window is not this task's. Needles, first match wins:

1. `DelegationReportFormatter.Short(task.Id)` (8 hex) or the `[antiphon-task:…]` marker.
2. A `CARD-NNNN` extracted from `Title` / `Goal`, matched with the same anchored regex
   `GitWorkspaceService.ListCommitsByGrepAsync` already uses (`:256`) so `CARD-0083` does not
   hit `CARD-00830`.

9a5b93a3's `b9ecf40` subject is `docs(providers): CARD-0083 plan - …` on a unique worktree
branch — Arm A Worktree, no needle required.

**Arm B — later transcript content, the brief's ask. Does not bind. Does not ingest.** Scan
Claude JSONL under the session's cwd (`AgentSessions.Cwd`). Encoded project dir is Claude's
rule (non-alphanumeric → `-`; live example
`~\.claude\projects\C--src-ClaudeBot-agents-az-care\`). Inject the projects root so tests do
not mutate `CLAUDE_CONFIG_DIR`; default is the same `CLAUDE_CONFIG_DIR` / `~\.claude\projects`
the tailer uses.

For each `*.jsonl`:

1. C2: recorded `cwd` equals the session cwd. No match → skip (never read past the lead).
2. C3: if the first timestamped record predates the session's `StartedAt` (2 s slack, same as
   `TranscriptTailer.EpochSkewSlack`), **skip**. Recovering from a C3-refused file *is* the
   2026-08-09 operator-collision. The brief's "C3/C4 refusing" is too wide; C3-refused files
   are not this session.
3. Search **every** record's text (assistant included, not just the first user prompt) for a
   distinctive needle from the list above. Generic Goal prose ("plan the provider contract")
   is not a needle.

A hit is evidence the refused file was on-topic for *this* task. It is not a bind. Do not
start a tailer. Do not write `TranscriptEntries`. Do not claim the file.

Grok is out of Arm B. Its path is deterministic (`GROK_HOME/sessions/<url-enc-cwd>/…`); this
incident is Claude. Arm A is kind-agnostic and covers a Grok worktree the same way.

### 2.3 Recovery — Succeeded, loud, no kill

CARD-0046 already settled this question (`AgentTaskReplyService.SettleAsync` `:430-453`):
Succeeded is the right status when the work happened; "Succeeded" alone would lie about
verification, so three surfaces carry the caveat.

On positive evidence, **do not call `FailAsync`**. Call a new
`AgentTaskReplyService.RecoverFromBindRefusalAsync` (or the recovery type, with the reply
service doing the settlement it already owns) that:

1. Sets `Status = Succeeded`, `CompletedAt = now`, `Result` = a short recovered note naming
   the commit(s) and/or the JSONL path. This is not a fake delegate report.
2. Writes `AgentTaskEventType.Warning` (existing = 12) with the same text.
3. Records a **new** `AgentIncidentKind.DelegateBindRefusalRecovered = 24`, Warning: "task
   {short} recovered from an unbound session; work is at {commit / file}. C1–C4 were not
   changed." Existing `TranscriptBindFailed` stays on the timeline — that is the refusal;
   this is the recovery. Do not add a client enum map; unknown kinds already render by name.
4. If `Workspace == Worktree`, runs the existing `MergeBackAsync` path so the branch actually
   lands. 9a5b93a3's commit was already pushed; merge-back is how the next occurrence does
   not depend on the orchestrator ff-merging by hand.
5. Releases the delegate the way ordinary success does (`ReleaseDelegateAsync`).
6. Parent note via `BuildCompletionNote` with `warning:` set — the caller reads the caveat
   **above** the result (`DelegationReportFormatter.cs:188-190`). Header will say `succeeded`,
   not `failed`. That is the whole point.
7. **Does not `KillAsync`.** CARD-0056: a kill on a false Failed is how you kill a live
   worker. `FailDeadSessionTasksAsync` already must not kill; `FailNeverStartedAsync`'s kill
   is skipped on this arm.

If `_replies` is null (old harness), skip recovery and Fail as today.

Log at Warning. A silent Succeeded is how the next occurrence disappears.

### 2.4 What Failed still means

Zero evidence after both arms: Failed, current reason, current kill (watchdog only). The
reason already tells the caller to look at a `TranscriptBindFailed` if one exists. Leave that
sentence.

## 3. Tests — pin the verdict, not a log line

Extend `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs` (already
`[NotInParallel]`, no group key, assertions scoped to rows the test created). Add the matching
dead-session case to `AgentTaskDeadSessionReconciliationTests.cs`. Drive a real temp git repo
the way `DelegationWorktreeTests` already does; drive Arm B with a temp projects root injected
into the helper.

| Test | What it pins | Red today? |
|---|---|---|
| Zero transcript + worktree branch has a post-base commit → Succeeded, Warning event, kind 24, session **not** killed, Result names the sha | 9a5b93a3 / Arm A Worktree | **Yes** — FailAsync + Kill |
| Zero transcript + Shared HEAD has a since-dispatch commit that does **not** cite the task or a CARD-NNNN from the title → still Failed + killed | CARD-0006: do not steal a neighbour's commit on `C:\src\Antiphon` | No (already Failed) — the pin |
| Zero transcript + Shared HEAD commit subject contains `CARD-0083` and the task title does too → Succeeded + Warning | Arm A Shared needle | **Yes** |
| Zero transcript + cwd-matching JSONL whose **later assistant** record contains `[antiphon-task:{short}]`, first user record is `"green"` → Succeeded, incident names the file, **no** `TranscriptEntries` written | Arm B; C4 stays refused | **Yes** |
| Zero transcript + JSONL that mentions the card but whose first timestamp predates `StartedAt` → still Failed | C3-refused file is not evidence | No (already Failed) — the pin |
| `DelegateReportUncorrelated` arm still Fails | do not widen the other watchdog branch | No |
| Dead session, zero transcript, same worktree commit evidence → Succeeded, still no kill | second site | **Yes** (`FailDeadSessionTasksAsync` Fails today) |

Cleanup in `finally` must not leave the temp repo / JSONL tree.

Do not add a tailer test that binds on goal text. That would be loosening C1–C4.

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0085/ -- --treenode-filter "/*/*/AgentTaskDeliveryWatchdogTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0085/ -- --treenode-filter "/*/*/AgentTaskDeadSessionReconciliationTests/*"
```

Same project, **one after the other** (both `[NotInParallel]` against the shared test Postgres).
Delete `bin-card0085/` afterwards.

## 4. Out of scope

- Changing C1–C4, `MaxRetainedPrompts`, `MinMatchChars`, or searching assistant text inside
  the tailer.
- Binding or ingesting the refused file. Recovery is a task-verdict only.
- The stray `"green"` first user record (PTY cross-talk between `407afaf8` / `dd6b7866` /
  `9a5b93a3` is a hypothesis, not confirmed). Separate investigation if it recurs.
- CARD-0064 (restart / sidecar / empty input log on a standing channel-bound session).
- Grok Arm B. Arm A covers Grok worktrees.
- A new `AgentTaskStatus`. Failed-with-a-nicer-reason still redispatches.
- UI for kind 24. Timeline already shows the enum name.
- Closing or moving CARD-0085. This plan lands; a Code slice implements.

## 5. Slice

One Code slice, in this order: the two red tests that pin 9a5b93a3 (watchdog worktree +
dead-session worktree), then `DelegateBindRefusalRecovery` (Arm A, then Arm B), then
`RecoverFromBindRefusalAsync` on the reply service (Succeeded + Warning + kind 24 + merge-back
+ no kill), then the remaining pins. Verify with the two `dotnet run` lines in §3.
