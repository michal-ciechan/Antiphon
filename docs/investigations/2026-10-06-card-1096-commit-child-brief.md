# CARD-1096: commit-child brief assertion on Linux

Date: 2026-10-06. Task `bbd4f9d6`. Investigation only. No production or test code was changed.

## Verdict

**Confirmed.** Both parameterizations of `AgentTaskCommitEndpointTests.Spawned_child_complete_brief_gated_operation_and_parent_receipt` fail at `tests/Antiphon.Tests/Application/AgentTaskCommitChildChainTests.cs:158` because the typed child prompt is a spill pointer, and the pointer does not contain `child.Goal`.

This is a stale assertion against the standing spill gate, not a lost commit brief. On this Linux host the factory's `"modern"` request resolves to Unix PTY, `CeilingsFor` maps that to the 900-byte inbox ceiling, and the commit-child brief is 2,169 UTF-8 bytes. The full brief, including the goal and the commit authorization, is written to the spill file. The typed body is the pointer, whose first lines are the task marker and the title `Commit: C527 produced worker`.

Real delegate runs that commit through this path are consistent with that design: the delegate is told to read the spill file before acting. The red does not show the gated commit endpoint refusing work.

## Reproduction

Source: `5b713f6855ee417737ff7d7e47cb340d66e3d9b8` on `feat/card-task-bbd4f9d6`. `b17aa00c71baf78abb9e08f2be74b70598a9029d` is an ancestor of that commit. The CARD-1065 S7 diff is not what this assertion checks.

`dotnet build tests/Antiphon.Tests` compiled `Antiphon.Tests.dll` (2026-10-06 15:58:14Z) and then failed the post-build copy because `tests/Antiphon.Tests/bin/Debug/net9.0/fakeclaude` is a file where that target wants a directory. The two methods were run with `--no-build` against that DLL, through `scripts/build-slot.ps1`, filter `/*/*/AgentTaskCommitEndpointTests/Spawned_child_complete_brief_gated_operation_and_parent_receipt*`. No `OutputPath` property. No Unit lane.

Result: total 2, failed 2, succeeded 0, skipped 0, duration 1m 00s, exit 2.

| Argument | Failure | Dispatcher log |
|---|---|---|
| `recover=false` | `AgentTaskCommitChildChainTests.cs:158`, 10s 317ms | Task `d712eb62`: brief is 2,169 UTF-8 bytes (> 900 — InboxConhost: brief 900B, reply 3,000 chars, single write 1,024B (Unix PTY via Porta; Windows backend selectors do not apply)); pointer `/tmp/c527-ep-repoeSUWh4/.antiphon/task-d712eb62-brief.md` |
| `recover=true` | same line, 4s 537ms | Task `2bfaed01`: the same 2,169-byte / 900-byte Unix PTY line; pointer `/tmp/c527-ep-repoVkYJCj/.antiphon/task-2bfaed01-brief.md` |

Shouldly at line 158, both arms: `prompt.Text` should contain the multi-line goal beginning `Task <short> (C527 produced worker) left dirty paths.` but was actually:

```text
[antiphon-task:<child>] role=Commit tier=Medium workspace=Shared

Commit: C527 produced worker

[an...
```

The `recover=true` arm also logs `C527 producer interrupted after durable settlement` from `CommitChainBoundary.ReachedAsync` (`AgentTaskCommitEndpointTests.cs:352`). That throw is the test's own settlement cut. The assertion that fails is still line 158.

The suite log says it cleared inherited `ANTIPHON_PTY_BACKEND='inbox'`. The factory still constructs `PtyDeliveryProfile` with `"modern"` (`AgentTaskCommitEndpointTests.cs:332-336`). The ceiling reason in the failure log is the Unix PTY sentence, so the red is not the cleared environment variable.

## Mechanism

1. The parent worker settles with dirty `x.md`. `CreateCommitTaskAsync` stores the multi-line goal on the child (`AgentTaskService.cs:3792-3803`) and sets `CommitOnSettle = Never`, `Role = Commit`, `Workspace = Shared`. The test's routing pin resolves ClaudeCode / Medium. Line 109 (`child.AgentKind.ShouldBe(ClaudeCode)`) is before the failure, so this is not the non-Claude zero inline ceiling.

2. Dispatch reuses the warm child session and queues `FitBriefForSession` (`AgentTaskDispatcher.cs:5226-5228`). The test session has no `RunnerCwd`, so `CeilingsForBrief` keeps the process ceilings (`AgentTaskDispatcher.cs:5849-5855`).

3. `CommitEndpointWebAppFactory` asks for `"modern"`. On non-Windows, `PtyBackendPolicy.Resolve` returns `UnixPty` before any Windows selector is honored (`src/Antiphon.Agents.Pty/PtyBackend.cs:90-92`). `DelegationSettings.CeilingsFor` gives `ModernConPty` the 43,200-byte ceiling and every other backend, including `UnixPty`, the inbox record: 900-byte brief, 3,000-char reply, 1,024-byte single write (`DelegationSettings.cs:280-287`).

4. `FitBriefForTyping` builds the full brief, counts UTF-8 bytes, and when the count exceeds the ceiling writes `.antiphon/task-<short>-brief.md` under the child working directory and returns `BuildBriefPointer` (`AgentTaskDispatcher.cs:5784-5832`). The measured count is 2,169. The log names a file path, which is set only after `File.WriteAllText` of that full brief (`AgentTaskDispatcher.cs:5808-5816`). A failed write would have logged "the API" instead.

5. `BuildBrief` appends `task.Goal` (`DelegationReportFormatter.cs:176`) and, for this child, the lines "explicitly authorized to commit", `scripts/task-commit.ps1`, and "Do NOT push" (`DelegationReportFormatter.cs:269-273`). `BuildBriefPointer` repeats the marker header and then `task.Title` (`DelegationReportFormatter.cs:856-878`). The title is `Commit: C527 produced worker` (`AgentTaskService.cs:3791`). The pointer does not contain the goal. The test requires the goal in the typed prompt:

```text
prompt.Text.ShouldBe(brief.Body);          // line 157, passes: both are the pointer
prompt.Text.ShouldContain(child.Goal);     // line 158, fails
```

Lines 159-161 (authorization, `scripts/task-commit.ps1`, "Do NOT push") are the same inline-body checks and are not reached. `PtyDeliveryCeilingsTests.The_same_brief_spills_on_the_inbox_backend_and_is_typed_inline_on_the_modern_one` already pins this split: inbox types the pointer, modern types the goal (`PtyDeliveryCeilingsTests.cs:111-126`). A 2,169-byte brief fits the modern 43,200-byte ceiling and does not fit 900.

## What the live path implies

The spawned commit child is a real delegate path. A live run that commits is reading the spill file (or the task goal through the API fallback). The authorization to use `scripts/task-commit.ps1` and not push lives in the full brief, which this run wrote to disk before typing the pointer. The typed prompt lacking the goal is the spill gate working. It is not evidence that Linux delegates are asked to commit without those instructions.

## When it started, and why that commit did not catch it

The assertion text is unchanged. `git blame -L 155,163` attributes lines 157-162 to `3f6403c4259a67fad8df1bad9568db79fc325b7b` (2026-09-15), which moved the block inside the verify scope. The same five assertions were introduced in `2f20436f0` (2026-09-17, "verification pending") at the old line 138. The goal sentences were introduced in `d5765e848` and are the same sentences at `2f20436f0`, `6511608fe`, `08a367e98`, and HEAD.

No later commit removed the goal from an inline brief. The mismatch is the spill gate, which has used the 900-byte inbox ceiling since CARD-0027 / CARD-0037, meeting an assertion written for the inline body.

- `6511608fe` (CARD-0527) is what pinned this factory to `"modern"`. Its message reports Unit 2479/2484 and four inherited reds. This method boots `CommitEndpointWebAppFactory` and is not in that Unit lane. On Windows, `"modern"` plus `conpty.dll` is `ModernConPty`, the ceiling is 43,200, and 2,169 bytes are typed inline, so line 158 passes. That commit never ran this method on Linux.
- `08a367e98` (CARD-1022, 2026-10-04) made non-Windows return `UnixPty` immediately. Before that, `"modern"` with no `conpty.dll` fell back to `InboxConhost`. Both values take `CeilingsFor`'s default 900-byte arm, so CARD-1022 did not change this spill. Its message says verification pending.
- The "inherited reds" in `6511608fe`, `02f04773a`, and `3f6403c42` are the CARD-0527 Unit lane (`docs/investigations/2026-09-15-card-0527-f10-f12-repair.md`: 2494 total, four inherited failures, one skip). They are not this method. CARD-1065 S7 is the Linux class rerun that recorded it. The same two failures are what this run reproduced on a descendant of `b17aa00c`.

## Not done, noted

Test-only: when the typed body is the pointer, assert the goal and the commit-authorization lines on the spill file the dispatcher just wrote, and keep those assertions on the typed body only when the brief is inline.

Do not raise the Unix ceiling to 43,200. That envelope was measured for modern ConPTY, not for this host. Do not paste the goal into the pointer. The pointer exists so a body over one 1,024-byte write is not typed, and this brief is 2,169 bytes.

## Risk if left

Every Linux run of these two methods stops at line 158. The commit POST, the `x.md` commit, the parent receipts, and the no-push check later in the same method do not run here. A later break in that gated path stays invisible on the runner that actually spills. Windows modern hosts can still pass the whole method, which is why the assertion survived review. Leaving the red does not by itself stop a live delegate that reads the spill file.

## Next stage

`next: code`. One test method. No production edit.
