# CARD-0070 — Merge role tier: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0070 (`b490145e-4be2-4cfa-97d6-3ccc51851edd`) — Merge role runs at
High (opus), and a 2026-08-17 verify-merge-deploy was dispatched as `-Role Merge`
so a clean fast-forward paid opus prices.
**Precedent:** Feature 007 P2 (`docs/features/007-multi-agent-orchestration/proposal.md`)
put Merge at High and spawned a Merge-role delegate only *after* rebase conflicted.
`AgentTaskReplyIntegrationTests.a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate`
already pins `ModelLevel == High` with the message "conflict resolution is High-tier
work by policy". The client picker already says the same
(`client/src/api/agentTasks.ts` `AGENT_TASK_ROLES`: "resolve a conflict left by a
worktree task" / High).

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**Do not drop Merge to Low.** The brief's "tiny single-line Frontier → haiku"
mapping is the wrong fix for the evidence. Merge is **High (opus), not Frontier
(fable)** — the task title overstated the tier. The role's production job is
**conflict resolution**, not `git merge --ff-only`. Clean worktree landings never
go through this role at all: `DelegationWorktreeService.TryMergeBackAsync` rebases
and fast-forwards in-process, and `CreateMergeTaskAsync` fires only after that
already reported `Conflicted`.

The 2026-08-17 cost miss is a **dispatch mismatch**: an orchestrator used
`-Role Merge` for a routine slice-6 verify-merge-deploy. That work already has
cheap roles (`Test` / `Deploy` at Low, `Commit` at Medium). Putting the
conflict-resolver on haiku so those dispatches become cheap would make every
auto-spawned conflict a haiku decision, which the standing rule already forbids.

One small Code+Docs slice: comment the policy the way Test/Deploy already is,
pin Merge on the role→tier ladder test (it is missing today), and add Merge to
the two role tables that omit it. No schema. No event-driven `EscalateTo`. No
re-tier of Coverage or Commit.

## 1. Current shape (verified against the files, 2026-08-19)

### 1.1 What Merge actually resolves to

| Source | Merge's tier | Notes |
|---|---|---|
| `DelegationSettings.RolePolicy["Merge"]` (`DelegationSettings.cs:257`) | **High** | No comment. Test/Deploy on the next lines have the "RUN things / INTERPRETING is Debug" comment. |
| `appsettings.json` `Delegation` | not overridden | Only `MaxCostUsdPerRoot` and the check-interpreter cwd. Shipped defaults apply. |
| `DefaultLevel` | High | Deleting the Merge entry would still land High. |
| `AgentTaskService.ResolveLevel` (`:730-739`) | High | Role string → policy.Level; no Merge special case. |
| `CreateMergeTaskAsync` (`:675`) | `ResolveLevel(..., Merge, null)` | Inherits High. No explicit override. |
| `each_role_resolves_to_its_configured_tier` | **Merge not in the table** | Plan/Code/Review/Debug/Coverage/Docs/Commit/Test/Deploy only. |
| Conflict-spawn test (`AgentTaskReplyIntegrationTests.cs:1448`) | **High, pinned** | `"conflict resolution is High-tier work by policy"`. |
| Client picker (`agentTasks.ts:180`) | High | Copy already names conflict resolution. |
| Feature 007 proposal RolePolicy JSON | High | Same as shipped. Conflict spawn described as High-tier. |

Claude mapping (`AgentModelLevel` / `ModelLevelAliases`): Frontier = fable, High =
opus, Medium = sonnet, Low = haiku. A Merge task today is opus.

### 1.2 Two merge paths, only one is an agent

```
Worktree task settles Succeeded
  └─ AgentTaskReplyService.MergeBackAsync
       └─ DelegationWorktreeService.TryMergeBackAsync     // in-process git
            ├─ Merged / NothingToMerge / LeftForHuman     // NO agent
            └─ Conflicted
                 ├─ parent Status = Blocked
                 └─ AgentTaskService.CreateMergeTaskAsync // THE Merge role
                      Role = Merge, Level = High
                      Goal = resolve the listed files "the way the TASK intended"
```

`CreateMergeTaskAsync` (`AgentTaskService.cs:620-701`) is system-spawned, a child
of the conflicted task, working **in** the conflicted worktree, reporting to the
same parent session. Its goal is judgement: pick the task's version, continue the
rebase, fast-forward the target, report how each file was resolved. It is not
"run git and tell me the exit code."

`delegate.ps1` still accepts `-Role Merge` (`ValidateSet` includes it). That is
how a human or orchestrator can send **non-conflict** work through the same
expensive slot. The UI picker tries to stop that ("resolve a conflict left by a
worktree task"); the skill and `docs/orchestration-loop.md` §2 tables do not
list Merge at all, so an orchestrator reaching for "the merge step" has only
the ValidateSet name to go on.

### 1.3 Where "haiku merges" is actually written

It is **not** a RolePolicy rule, and it is **not** written against
`AgentTaskRole.Merge`.

| Document | What it actually says |
|---|---|
| `docs/orchestration-loop.md` §1 cycle | Orchestrator verifies on master, then `merge --ff-only` → deploy → close. Merge is a git verb, not a role. |
| Same file §2 role table | Plan/Code/Review/Debug/Docs/Commit/Test/Deploy. **Merge absent.** Test/Deploy = haiku. |
| Same file §5 | Verify yourself, then `git merge --ff-only`. No delegate. |
| Same file §7 | **Close** is "orchestrator writes the verdict, haiku executes it." Not merge. |
| `.claude/skills/antiphon-delegate/SKILL.md` role table | Same eight-plus-Coverage list. **Merge absent.** "Test and Deploy are cheap because they RUN things." |
| Feature 007 proposal §2.2 / §2.5 | Merge = High. Conflict → Blocked + Merge-role task at High. |
| Feature 007 test-spec C2 | No Merge row. S12 is the worktree conflict spawn. |
| `client/src/api/agentTasks.ts` | Merge = High, conflict-only. |

The card's "step 4 of the orchestration loop is haiku" is true of **Test and
Deploy**, and of the close/cleanup in §7. It is not true of the Merge *role*,
and the loop never assigned that role to the cheap step. The 2026-08-17 dispatch
filled a hole in the tables, not a hole in the policy.

### 1.4 Escalation as it exists today

`RolePolicyEntry` has `EscalateTo` + `EscalateAfterMinutes`. The only consumer
is `AgentTaskDispatcher.AutoEscalateStalledAsync`
(`AgentTaskDispatcher.cs:263-329`): a running task with **no transcript
progress** for that window is stopped and requeued one tier up. Progress
resets the clock. Debug is the only role that ships both knobs (High → Frontier
at 25 minutes). Test has `EscalateTo = Medium` and **no** `EscalateAfterMinutes`,
so the auto-sweep skips it.

A rebase conflict is not a stall. `CreateMergeTaskAsync` already ran because
`TryMergeBackAsync` exited non-zero. Putting Merge at Low with
`EscalateTo = Frontier` and "escalate when rebase conflicts" would fire on
**every** Merge task at creation: the conflict is the reason the row exists.
That is Frontier with a haiku round-trip in front, not a cheap default.

Event-driven escalation is also not a shape `RolePolicyEntry` has. Adding it
for this card is the schema the brief said not to force.

## 2. The card's three options, decided

| Option | Decision | Why |
|---|---|---|
| Merge becomes Low, `EscalateTo = Frontier`, escalate on conflict | **Reject** | Every production Merge task is already a conflict. Immediate escalate, or haiku resolving conflicts. Both contradict the pinned High policy and the "conflict is never a haiku agent's to resolve" rule the card itself restates. Time-based stall is the wrong proxy and the existing sweep already does only that. |
| Haiku Merge stops, reports the files, orchestrator dispatches Debug | **Reject as the default path** | The server already did the "stop and hand off" when it spawned the Merge task. A second hop through the orchestrator is the round-trip the card named, paid on every conflict, to re-do `CreateMergeTaskAsync`. A **manually** dispatched `-Role Merge` that hits a conflict it cannot resolve should Block and ask, same as any worker — that needs no new machinery. |
| Keep High; document; pin the ladder; tell dispatchers the cheap path is Test/Deploy/Commit | **Take this** | Matches spawn path, enum comment (`AgentTaskRole.Merge = 10`: "Resolve a merge conflict left behind by a Worktree task."), client picker, feature 007, and the existing conflict test. The cost bug is using the wrong role. |

Coverage stays High ("check what a change missed" is judgement; proposal put it
with first-pass debug). Commit stays Medium (git plumbing; skill and loop
already say sonnet). Neither is this card. Check is created with an explicit
`ModelLevel = Low` (`AgentTaskCheckService.cs:404`) and is absent from
RolePolicy on purpose — do not add it here.

## 3. The slice (one Code+Docs)

Smaller than a behaviour change. The behaviour is already correct; the ladder
test and the two caller-facing tables are what let the 2026-08-17 miss happen
again.

### 3.1 Policy comment — `DelegationSettings.cs`

Next to `["Merge"]`, same voice as the Test/Deploy comment immediately below:

```csharp
// High: this role is the conflict resolver CreateMergeTaskAsync spawns after
// TryMergeBackAsync already failed. Clean fast-forwards never reach it
// (in-process git). A verify-merge-deploy is Test/Deploy/Commit, not Merge.
["Merge"] = new() { Level = AgentModelLevel.High },
```

Do not add `EscalateTo`. If a later card wants stalled-Merge → Frontier the way
Debug already works, that is a copy of Debug's two knobs and a test in
`AgentTaskStallEscalationTests`. Not this slice.

### 3.2 Ladder test — `AgentTaskServiceIntegrationTests.cs`

Add one argument to `each_role_resolves_to_its_configured_tier`:

```csharp
[Arguments(AgentTaskRole.Merge, AgentModelLevel.High)]
```

That is the test the 2026-08-17 miss would have been invisible to even after a
mistaken Low edit, because Merge was not in the table. Keep the existing
conflict-spawn assertion at High (`AgentTaskReplyIntegrationTests.cs:1448`) —
it is the spawn-path pin; the ladder test is the policy pin.

Do not add Check or Custom here unless they get their own RolePolicy entries.
Custom is `DefaultLevel` (High) by design; Check is explicit Low at creation.

### 3.3 Caller-facing tables

Add one row, same words as the client picker, so the next orchestrator does
not invent a meaning from the ValidateSet name:

| Role | Use for | Tier |
|---|---|---|
| `Merge` | resolve a rebase conflict a worktree task left | opus |

Two files, keep them in lockstep with `AGENT_TASK_ROLES`:

- `.claude/skills/antiphon-delegate/SKILL.md` (the table under "Which role?")
- `docs/orchestration-loop.md` §2

One extra sentence under the Test/Deploy cheapness note in the skill, not a
new section:

> A clean fast-forward plus deploy is `Deploy` (and `Test` for the suites).
> `-Role Merge` is the conflict specialist the server already spawns; sending
> it a clean merge pays opus for haiku work.

`scripts/delegate.ps1` stays as-is (ValidateSet already lists Merge; ASCII-only;
no role-docs in that header). The client picker stays as-is.

### 3.4 Tests to run

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0070/ -- --treenode-filter "/*/*/AgentTaskServiceIntegrationTests/*"
```

That class owns the ladder. The conflict-spawn test is a regression lock, not
something this slice changes; re-run it only if the comment/policy edit is
suspected of drifting `CreateMergeTaskAsync`. Delete the `bin-card0070/` trees
after (`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0070`).

No client test: `AGENT_TASK_ROLES` is already the right copy and this slice
does not touch it.

## 4. Out of scope

- Dropping Merge to Low, or adding `EscalateTo` / `EscalateAfterMinutes` on it.
- A new event-driven escalation field on `RolePolicyEntry`.
- Re-tiering Coverage, Commit, Check, or Custom.
- Changing `CreateMergeTaskAsync` to pass an explicit level (it already inherits
  High from policy; an explicit High would only add an "override" event for a
  value that is not an override).
- Making `TryMergeBackAsync` call an agent on the happy path. Clean FFs are
  correctly in-process.
- Removing Merge from `delegate.ps1`'s ValidateSet. Manual dispatch of a
  conflict specialist is legitimate; the tables just have to say what it is.
- Teaching the dispatcher to reject Merge unless `CreateMergeTaskAsync` created
  the row. A human pointing a Merge worker at a real conflicted worktree is the
  intended escape hatch when the run is at task/depth cap (`CreateMergeTaskAsync`
  returns null and the completion note says "resolve by hand").

## 5. What this does not claim

An orchestrator can still type `-Role Merge` on a clean FF after this ships.
The slice makes that a documented mistake instead of an undocumented default.
If a later card wants a hard reject ("Merge without a conflicted parent is
refused"), that is a validation rule on `CreateAsync`, not a tier change, and
it would block the cap-escape hatch in §4. Do not sneak it in here.
