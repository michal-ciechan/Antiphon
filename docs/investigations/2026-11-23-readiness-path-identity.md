# Project readiness path identity on Linux

This note records why a Linux-only readiness red went unnoticed. It began as the readiness section of the shared [run `79d9e6f9` note](2026-11-22-rc-run-79d9e6f9-failures.md). It has its own file so that this chain no longer edits the shared note.

## Inherited master red · Project readiness path-matched agent

`ProjectReadinessTests.agent_counts_a_path_matched_agent_on_another_board` returned `Missing` on Linux at master `ecc9bfaa5`. No recent land broke it. CARD-0032 S1 (`a6ae44088`, 2026-08-26) wrote `ComputeReadinessAsync` this way. It normalized the project path with the host separator (`DelegationWorkspaceResolver.NormalizeSeparators`) but rewrote the stored agent path to backslashes in SQL. On Windows both sides end up with `\`. On Linux the project side keeps `/` and the agent side becomes `\`, so a path-matched agent never matched. The test was red on every Linux run from the start.

The first fix (`d94a55eef`, later re-cut as `2d2daae6b` and then `2bd457e91` with the same patch) uses the CARD-0558 rule that `AgentService.FindProjectForWorkingDirectoryAsync` already follows: forward slashes on both sides. The product code was wrong, not the test. A Linux-hosted server (CARD-1744) would report no linked agent for a project whose only agent sits on another board.

The chain that followed moved both queries onto one shared, host-independent identity (`server/Application/Services/WorkingDirectoryIdentity.cs`), used by `ProjectSetupService.ComputeReadinessAsync` and `AgentService.FindProjectForWorkingDirectoryAsync`. It also replaced the database case fold with a collation-independent prefilter and bounded the number of candidates the prefilter admits. The plan [2026-11-22-project-readiness-path-prefilter-r3-repair.md](../superpowers/plans/2026-11-22-project-readiness-path-prefilter-r3-repair.md) records those decisions and their measurements.

## Why missed

The readiness slice and its tests were verified on the Windows desktop. CARD-0558 fixed the same separator pattern in `AgentService` without checking the sibling queries for it. Linux runs showed the red from 2026-11-21, but nobody routed it because it looked like platform noise.

- When a Linux run shows a red in a test that has no platform gate, treat it as a product portability defect until it is proven otherwise.
- When you fix a separator comparison, grep for `Replace("/", "\\")` applied to a value that was normalized with the host separator. `OrchestratorWorkspaceWarningService.cs:140` still has the same pattern and needs its own fix.
