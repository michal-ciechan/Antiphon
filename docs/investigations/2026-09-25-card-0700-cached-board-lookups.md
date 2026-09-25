# CARD-0700 cached board lookups

## Design

`CardFileBoardLookup` is a DI singleton containing an immutable projection of board
and project ownership metadata. One query fills it; sibling slug owners, including
archived and opted-out boards, are indexed in memory using the original creation
clock/ID ordering and case-insensitive comparisons. Prospective board validation
and ignore-pins validation retain the previous slug rules. Callers receive fresh
mutable copies where they need entities; cached EF entities are never shared.

The existing `BoardChanged` event bus invalidates before SignalR sends. Project
create/delete now publish that event. Board/project mutations also invalidate the
local policy service when an event bus is absent or substituted. Opt-in, card
visibility, ownership pin writes and the outer setup transaction's commit clear
the cache. A generation check rejects a fill raced by invalidation. Transactional
reads bypass shared data so uncommitted owners cannot leak into other scopes.

The sweep starts with all owners to discover legacy residue. An opted-out board
leaves later sweeps only after both working-tree and Git cleanup are confirmed
clear. Unknown or pending cleanup retries. Explicit status/sync still read current
policy under existing leases and inspect the filesystem. The cache has no TTL;
external SQL maintenance must publish an event or restart the process.

## Verification design

- V-1: counted SQL on cold/warm inspection and across service scopes; counted zero
  database reads on a subsequent sweep containing only clean opted-out boards.
- V-2: board/project create, rename, delete, archive and unarchive; repository and
  card visibility; opt-in/out; renamed sibling collisions and retained pins;
  invalidation during a fill; shared waiters; uncommitted transaction isolation;
  setup commit invalidation. There is no board-rename API, so that case commits a
  rename and publishes the existing `BoardChanged` contract.
- V-3: legacy opt-out cleanup before suppression and retry of pending Git residue.
- R-1: full card-file classes, Unit lane, and complete affected mutation-producer
  classes. All PCs remain pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | red tests | tests/Antiphon.Tests -> bin-c700/ | red-first | `/*/*/CardFileBoardLookupTests/*` | V-1,V-3 | 3 expected baseline failures | 3 | 4 |
| CP-2 | implementation | tests/Antiphon.Tests -> bin-c700/ | card-files | `/*/*/(CardFile*)\|(CardTaskFile*)/*` | V-1,V-2,V-3 | full roster, 0 failures | 100 | 5 |
| CP-3 | implementation | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-1 | full lane, 0 failures | 1992 | 5 |
| CP-4 | implementation | CP-2 | event-producers | `/*/*/(ProjectServiceTests*)\|(ProjectSetupServiceTests*)\|(ProjectDeletionTests*)\|(BoardProjectArchiveTests*)\|(BoardServiceIntegrationTests*)/*` | V-2 | all five classes, 0 failures | 30 | 3 |

CP-4 was added before execution because the implementation touched those mutation
producers. CP-B is an additional baseline probe of the complete five classes
containing the initial card-file failures, justified by their Windows path,
junction and Git-hook assumptions. Its isolated checkout is at the red-test
commit, with unchanged production code.

## Results

The red-first commit is `bdaf40da8aefdce57f7f889c7818715b1e9ea4ee`:
3 executed, 0 passed, 3 failed as expected (three repeated lookup queries, and
opted-out boards still swept).

Final card-file execution at `d0eb4ae3b4fb37465c7ca391992119041ff1e30e`:
263 executed, 243 passed, 20 failed, 0 skipped. All 21 new cache cases passed;
none took five seconds. The initial project-warning regression is fixed.

The baseline probe executed 57 cases: 37 passed and the exact same 20 failed.
CARD-0713 tracks these existing integration portability failures:

| Class | Failed cases | Evidence |
|---|---:|---|
| CardFileGitFailureAcceptanceTests | 4 | Hooks are written without executable permission; Unix Git does not run them. |
| CardFilePrivacyGitTests | 3 | Windows Junction setup/cleanup assumptions. |
| CardFilePrivacyPathTests | 3 | Windows Junction setup/cleanup assumptions. |
| CardFilePrivacyScriptTests | 10 | Remote Windows target display uses Join-Path with a nonexistent local C drive. |

The whole Unit lane passed: 3,120 executed, 3,120 passed, zero failed, 29 skipped.
The five producer classes executed 44 tests: 43 passed, one failed, zero skipped.
The new setup-commit case passed, so all 22 new tests pass. The existing
`ProjectSetupServiceTests.setup_rejects_a_directory_already_owned_by_a_project`
failure also reproduces on baseline (43 executed, 42 passed, one failed), tracked
as CARD-0715. No baseline Unit run was necessary or executed.

This is not an all-green Final verification claim: the 20 card-file and one
setup failures remain open. There are no new failures relative to baseline in
the executed selections. Platform fixes/Windows evidence remain separate from
CARD-0700, and all PCs remain pending SourceLanding Mutation.

CP-2 had three retries: child-node build exits; an interrupted build retry; then
one introduced warning regression plus the 20 baseline failures, followed by the
corrected run above. CP-B's first filter selected zero tests; the required trailing
wildcards were restored and its successful build reused. No zero-test run counts
as evidence. The final build uses one MSBuild worker, BuildInParallel=false,
UseSharedCompilation=false, and UseAppHost=false on this runner. The duration
tripwire found no new slow tests; three existing API/hosted-service/migration cases
exceeded five seconds (6.17/5.49/5.29 seconds).

### Recorded checkpoint lines

```text
CHECKPOINT CP-1 commit=bdaf40da8aefdce57f7f889c7818715b1e9ea4ee build=ok filter=/*/*/CardFileBoardLookupTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-957902d6/.antiphon/c700-red/CP-1-20260925-145315-5fae/run.trx
CHECKPOINT CP-2 commit=d0eb4ae3b4fb37465c7ca391992119041ff1e30e build=ok filter=/*/*/(CardFile*)|(CardTaskFile*)/* executed=263 passed=243 failed=20 skipped=0 trx=/work/worktrees/task-957902d6/.antiphon/c700-files/CP-2-20260925-153000-e581/run.trx reruns=3
CHECKPOINT CP-3 commit=d0eb4ae3b4fb37465c7ca391992119041ff1e30e build=reused filter=/*/*/*/*[Category=Unit] executed=3120 passed=3120 failed=0 skipped=29 trx=/work/worktrees/task-957902d6/.antiphon/c700-unit/CP-3-20260925-153849-dfec/run.trx
CHECKPOINT CP-4 commit=d0eb4ae3b4fb37465c7ca391992119041ff1e30e build=reused filter=/*/*/(ProjectServiceTests*)|(ProjectSetupServiceTests*)|(ProjectDeletionTests*)|(BoardProjectArchiveTests*)|(BoardServiceIntegrationTests*)/* executed=44 passed=43 failed=1 skipped=0 trx=/work/worktrees/task-957902d6/.antiphon/c700-producers/CP-4-20260925-154254-0d26/run.trx
CHECKPOINT CP-B commit=bdaf40da8aefdce57f7f889c7818715b1e9ea4ee build=reused filter=/*/*/(CardFileGitFailureAcceptanceTests*)|(CardFilePrivacyGitTests*)|(CardFilePrivacyPathTests*)|(CardFilePrivacyScriptTests*)|(CardFileIgnoreAcceptanceTests*)/* executed=57 passed=37 failed=20 skipped=0 trx=/work/worktrees/task-957902d6/.antiphon/c700-baseline/CP-B-20260925-152721-bcd6/run.trx reruns=1
CHECKPOINT CP-BP commit=bdaf40da8aefdce57f7f889c7818715b1e9ea4ee build=reused filter=/*/*/(ProjectServiceTests*)|(ProjectSetupServiceTests*)|(ProjectDeletionTests*)|(BoardProjectArchiveTests*)|(BoardServiceIntegrationTests*)/* executed=43 passed=42 failed=1 skipped=0 trx=/work/worktrees/task-957902d6/.antiphon/c700-baseline-producers/CP-BP-20260925-154435-f87d/run.trx
```

CP-BP is the additional no-build baseline producer comparison justified by the
sole CP-4 failure. Both baseline probes use an independently built, detached
checkout of the unchanged production source. Expected failures and invalid
zero-test/build attempts are not counted as green evidence.

### Rerun

On this Linux runner, build once with:

```sh
dotnet build tests/Antiphon.Tests --maxcpucount:1 --property:OutputPath=bin-c700/ --property:UseAppHost=false --property:BuildInParallel=false --property:UseSharedCompilation=false
```

Then run `scripts/run-checkpoint.ps1` for CP-2, CP-3 and CP-4 with `-NoBuild`,
`-Project tests/Antiphon.Tests -OutputPath bin-c700/`, the exact table filters,
`-MsBuildProperty BuildInParallel=false,UseSharedCompilation=false` and a fresh
results root. The script supplies UseAppHost=false off Windows. This run used
DOTNET_PROCESSOR_COUNT=4, TUNIT_MAX_PARALLEL_TESTS=1 for integration selections and
2 for Unit; task-local Git invocations had 45-second deadlines.

### Files and cleanup

The production change is in CardFileBoardLookup, CardTaskFileService/Policy,
EventBus, Program DI, and board/project/card mutation hooks. Regression coverage
is in CardFileBoardLookupTests (two partial-class files) and ProjectSetupServiceTests.
The privacy and testing guides record the cache and reproduced platform caveats.

All 34 producer-owned bin-c700/bin-c700base directories were removed after the
test processes completed. The detached baseline checkout and task-local diagnostic
tool were also removed. The output inventory and full raw logs remain under
`.antiphon/` in the task worktree.

## Handoff

The verified code/test commit is `d0eb4ae3b4fb37465c7ca391992119041ff1e30e`.
The final follow-up commit adds only this report and the testing-guide note.

Review the implementation and test evidence. The caller owns landing, restart,
`/api/version` verification and desktop `pg_stat_statements` measurement. Ordinary
runs do not discharge any SourceLanding Mutation obligation. Work proceeded under
the standing operator authority; no additional approval was requested.
