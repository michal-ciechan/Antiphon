# CARD-2058 Review round 1 (task 453f994d)

Verdict: **Clean**. No blockers, no regressions.

- Subject: `feat/card-task-9c157c8a` (Code task 9c157c8a, Codex High), range
  `2f4cfa8df9d5b51a1554e5adae5716b2e422c620..8007ac89a8de451ab9f2ebad21cb9169255a90c7` (2 commits, 2 files).
- Reviewer: Claude Opus 5.5 on server2 Linux, runner mirror `ws-08-d2`.

## What changed

`OrchestratorWorkspaceWarningService.ResolveProjectAsync` (boardless fallback only) now uses
`WorkingDirectoryIdentity.CandidateFilter` plus `WorkingDirectoryIdentity.Same`, the same two-step
lookup as `AgentService.FindProjectForWorkingDirectoryAsync` (CARD-0558) and project readiness. The
old predicate rewrote the stored side `/`→`\` while the probe used the host separator, so on Linux no
path containing a separator ever matched. The board-linked branch is unchanged.

## Behaviour checks

- Windows paths: case-insensitive, `/` equals `\`, trailing separators dropped above the root;
  `Z:\` = `z:/`; `Q:` and `Q:\` stay distinct. This matches the old Windows behaviour and also
  fixes stored paths with trailing separators, which the old SQL side never trimmed.
- POSIX paths: on Linux the old code never matched, so the new code only adds matches. On a Windows
  host the comparison becomes case-sensitive, and a backslash in a POSIX path no longer counts as a
  separator. The rule treats those spellings as different directories, so this is deliberate, not a
  regression. Under the CARD-0558 rule the only effect is that a warning is raised where it used to
  be suppressed (detection only).
- Fail-open: a match only suppresses the warning when the matched project is acknowledged. The
  catch-all that swallows exceptions (launch unaffected) already existed and was not changed.
- Collateral: none; only the service and its test class changed.

## Siblings left unfixed (advisory, not blocking)

`AgentService.PathsMatch` (:1331) still uses the host separator. It is used in
`AttentionService:3162` (filtering idle agents that own live cards), `AgentService:504` (log only)
and `ProjectSetupService:262/471`. The dispatcher, lease-projection and reply overlap checks the
author listed also still use it. They serve other features and do not share a code path with this
warning, so leaving them does not make this fix inconsistent. `AttentionService:3162` is the
nearest Linux misfire of the same kind and is worth its own card.

## Evidence (reviewer reruns, server2 Linux, build-slot gated, maxcpucount=6)

| Run | Source | Class `OrchestratorWorkspaceWarningTests` |
|---|---|---|
| Green | tip 8007ac89a | total 10, passed 10, failed 0, skipped 0 |
| Red | tip with service file restored from 5e0ee960d (pre-fix) | total 10, passed 6, failed 4 |

The red run failed the 4 match cases (`C:/repos/Antiphon`, `C:\repos\Orchestrator`, `C:/repos/Warnings`,
`Z:\`) on the Shouldly count assertion. The drive-relative control `(Q:, q:\, 1)` and the 5 earlier
tests passed. The reverted file was restored afterwards and the worktree was clean. The tests can
fail, and the `Q:` case is an in-test positive control that a warning is raised.

- `scripts/antiphon evidence-diff --base 2f4cfa8df`: commits=2, violations=0.
- `git merge-tree --write-tree origin/master(3189159aa) HEAD`: no conflicts.
- Not run: `WorkingDirectoryIdentityTests` (unchanged file), batched guards and the Unit lane.
  The caller limited this round to the named class. There is no plan checkpoint table for this card.
- Process note (advisory): the author's red run built outside the slot's `-maxcpucount`. The red
  evidence above replaces it.
