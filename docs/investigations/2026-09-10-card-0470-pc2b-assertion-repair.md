# CARD-0470 PC-2b assertion repair

Task `14c89e5f` repaired the P2 assertion gap on `feat/card-task-ce744e22`.
Tested implementation: `080a4204f184e8ae3b10e4a43ca8c2811352c6f9`, based on `64a1311d`.
Worktree: `C:\Antiphon\worktrees\card-task-ce744e22`.
Original landing owner remains `ce744e22`.

## Repair

`tests/Antiphon.Tests/Application/MutationPipelineTests.cs` now checks Queued/Working
readiness consumption in the separate `C470_open_mutation_consumes_ready` test.
The four `C470_mutation_completion_consumes_ready` cases retain real reply settlement,
persisted outcome/handoff/artifact checks, and caller queue assertions. They read the
settled task by ID, independently of the predicate under mutation. Their final
`AgentTaskPipelineStatusService.GetAsync` projection still executes the production
SQL query with `.Where(AgentTaskRoles.Stage)`.

This supersedes the earlier PC-2b red evidence, which stopped at the open-state
assertion, and the follow-up record's description of a stage-filtered settlement
lookup. No production change remains.

## Fresh evidence

Evidence root:
`C:\Antiphon\verification\card0470\080a4204f184e8ae3b10e4a43ca8c2811352c6f9`.
Each arm contains `results.trx`, `run.log`, and `exit.txt`.

| Arm | Executed | Passed | Failed | Verdict |
|---|---:|---:|---:|---|
| `V-6` | 8 | 8 | 0 | Entire MutationPipelineTests class, including both open-state cases, Code settlement, enum storage/HTTP contract, and four completion cases. Exit 0. |
| `pc\PC-2b-red` | 4 | 0 | 4 | Exact completion method only. Both Review cases fail at line 182 because Review readiness has zero rows; both Land cases fail at line 185 because stale Mutation readiness has one row. Exit 2. |
| `pc\PC-2b-green` | 4 | 4 | 0 | Same four completion cases after restoration and rebuild. Exit 0. |

All arms have zero errors, timeouts, aborted or unexecuted cases. Every red failure
is a Shouldly assertion on the final pipeline projection, after successful settlement
and persisted outcome/handoff/artifact/caller-queue checks; none is a fixture or
stage-filtered `SingleAsync` exception.

The only deliberate defect removed `|| t.Role == AgentTaskRole.Mutation` from
`AgentTaskRoles.Stage` in `server/Domain/Enums/AgentTaskEnums.cs`.
`PC-2b-mutation.diff` records it. `AgentTaskEnums.cs.fixed`, `fixed-hash.txt`,
`mutated-hash.txt`, and `restored-hash.txt` retain the source evidence. Restored bytes
match the fixed backup, and the source timestamp was refreshed before rebuilding.
The restored server DLL in the test output matches the server build output and has
SHA256 `0CDCC387C9ED4558C7209A1D310F620C456F6B82381BE84D1390DAE6C7300402`, different
from mutant `B21F923CB5EBE02BFC41F5D2970950554C8D80087B8CA2AA6F3034ECAFDAEF6B`.
DLL hashes and restored build timestamps are retained alongside the TRX files.
Tracked source/index were clean after restoration; this evidence document is the
only subsequent change to the tested implementation. Build outputs are retained
under `bin-c470-pc2b/`, with paths in `output-inventory.txt`.

## Rerun and handoff

From the retained worktree, choose a fresh absolute result directory:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c470-pc2b/ -- --treenode-filter '/*/*/MutationPipelineTests/*' --results-directory <fresh-directory> --report-trx --report-trx-filename results.trx
```

For each PC arm use the exact filter
`/*/*/MutationPipelineTests/C470_mutation_completion_consumes_ready`, a separate
fresh result directory, and rebuild after mutation/restoration.

Next: Review of this repair and fresh evidence. PC-8 remains accepted. Preserve
landing owner `ce744e22`; server/client rollout and B-1 remain caller-owned after land.
