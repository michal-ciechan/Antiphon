# CARD-1287 Q2 Windows custody repair and RC comparisons

The refinement arrived and accepted the custody fix unchanged; it replaced further
repair work with four isolated comparisons. Those comparisons are complete: both
separate RC defects fail at current master and at the morning baseline, so both
are pre-existing, not regressions introduced today. Neither defect was repaired here.

Original Code task and landing owner: `7a7b92ba-6819-4460-9776-a07787de775d`.
Branch: `feat/card-task-7a7b92ba`.
Worktree: `C:\Antiphon\worktrees\card-task-7a7b92ba`.
Task base: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
Repair commit: `c2174f1f6e2f7ce6d831aa83982dc14d08169297`.
Comparison-plan commit: `6f6a91284cebb0a024779c7373bd8508b4cd6a42`.

## Custody repair

This was a Windows fixture defect. Virtual filesystem dictionary keys used Unix
path strings while the production path operations used host-native spelling.
The shared fixture and custody/receipt callers now construct paths consistently
with `Path.GetFullPath` and `Path.Combine`. Only these test files changed:

- `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointFixture.cs`
- `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointCustodyTests.cs`
- `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointReceiptTests.cs`

No product code, assertion expectation, timeout, test roster or safety safeguard
changed. Sibling tools and unowned runs remain protected; generic creation
custody remains separate from sealed-tool authority. Restart: none.

The original custody class ran 12 cases: 11 failed and 1 passed. The repaired
class ran 12 cases: all passed, no skips. The original binary was built at
`951cffebd3e8b394e8ee22818a74a37e29220246`, a documentation-only descendant of
the task base with identical tests/tools/src/server/scripts; this is not a claim
that the literal task-base SHA was tested. The green receipt is for the literal
repair SHA, validated by `scripts/validate-checkpoint-receipt.ps1 -Rows CP-2`.

## Two separate release-candidate defects

Current master was fetched and pinned to
`b7b2f112da759fe578b1a48ba16dfb747c55418c` for the comparison.
Morning baseline: `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.
Each method ran once, alone in its test host, at each exact committed source.
Each row executed 1, passed 0, failed 1, skipped 0. No red test was rerun after
its requested isolated execution, and no deliberate mutation ran.

| Separate defect | Current master | Morning baseline | Attribution |
|---|---|---|---|
| Migration fixture: `HostCleanupOrchestrationTests.AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | CP-5: FAIL | CP-7: FAIL | Pre-existing at morning baseline |
| Windows Git-object teardown: `OutputCleanupTests.nested_repository_tracked_output_is_owner_checked_and_kept` | CP-6: FAIL | CP-8: FAIL | Pre-existing at morning baseline |

Migration error at both sources: `DbUpdateException`, inner
`Npgsql.PostgresException: 42703: column "ReleasedSeatAnswer" of relation "AgentTasks" does not exist`.
Both stacks identify the test's `SaveChangesAsync`, line 61. The test attempts to
write the current entity against the historical migration state. This comparison
establishes the inherited test failure; it does not establish a deployed-database defect.

Cleanup error at current master:
`TestExecutionException: After(Test) hook failed: checkpoint fixture teardown failed: C:\Users\lndco\AppData\Local\Temp\c723-01a141de41b07fb6aeb3ac11d416a8fd: Access to the path '8593828617d313bd377d918dfead867ba6969d' is denied.`

Cleanup error at morning baseline:
`TestExecutionException: After(Test) hook failed: checkpoint fixture teardown failed: C:\Users\lndco\AppData\Local\Temp\c723-01a141ebf1fa706680a5da14b20079a6: Access to the path '2c41c2132f338e22d11fffa765a50a4bebe430' is denied.`

These are separate repair commissions for the caller. This task does not loosen
the teardown safeguard or change the migration test.

## Verification ledger and limits

- V-1: complete, original custody red reproduced, 12 executed / 11 failed / 1 passed.
- V-2: PASS, repaired custody 12/12; clean source and verified build receipt.
- V-3: PASS, manual diff confirms fixture-only repair and unchanged production safeguards.
- R-1: RED, full affected-class selection: 151 executed, 135 passed, 16 failed,
  12 platform skips (163 results, 157 source methods, all 12 intended classes).
  Exact failing-method replay on original source: 16/16 fail. Custody 12/12,
  receipt 8/8 and path 2/2 pass. The two requested defects above now also have
  literal current-master and morning-baseline evidence. Other inherited red
  results are preserved, not declared fixed or green.
- R-2 / CP-4: NOT RUN. Whole Unit remains an incomplete Final requirement. The
  original brief both excluded this lane and appended a Final profile requiring
  it; its measured 52m14s test cost could not fit the original 60-minute commission.
  The refinement explicitly accepts the custody fix and commissions only these
  four extra checks. No whole-Unit pass or complete Final gate is claimed.
- R-3: NOT RUN. Bound Linux/server2 execution was unavailable to this delegate.
  The same custody class still lacks this task's Linux receipt.
- V-4: complete diagnostic comparison, migration test FAIL at both exact sources.
- V-5: complete diagnostic comparison, cleanup test FAIL at both exact sources.
- R-4: complete, both requested defects classified as pre-existing at morning baseline.

Every inherited PC-1..413 and every variant remains pending method-scoped
SourceLanding Mutation, including missing-control discovery. Ordinary red/green
does not discharge PCs. The candidate is
`docs/superpowers/mutations/2026-10-15-card-1287-q2-candidate.md`.
Initial ordinary Review remains required. Caller owns landing the original Code
task and commissioning SourceLanding Mutation; no landing or deployment occurred here.

## Provenance, execution costs and retained evidence

Plans:
`docs/superpowers/plans/2026-10-15-card-1287-q2-windows-custody.md` and
`docs/superpowers/plans/2026-10-16-card-1287-q2-rc-comparison.md`.
All comparison starts were bound to this task/session. Windows placement was
necessary for the defect; GET runner-defaults and session-runners were read.
No missing-token fallback, external executor, subdelegate or full assembly ran.

The existing checkpoint CLI was built from `951cffebd3e8b394e8ee22818a74a37e29220246`.
Its `--repo-root` selected clean detached diagnostic checkouts; the tested source
SHA was each checkout's committed HEAD, not the repair branch or report commit.
The imported manifest explicitly set `UseAppHost: false` to align build/run launch
behavior. Each source had one successful isolated build reused by two method rows.
Every selected row has `slot=granted waited=0s`, clean start/end source fingerprints
and `buildSource=verified`. Red receipts are diagnostic, not green certificates.

Current run `20261016-001439-5888`: wall 5m59s, build 248.041s; test-host wall
53.823s and 8.573s. Launcher slot waited 0s; build and row slots waited 0s.
Morning successful run `20261016-002818-5645`: wall 7m07s, build 281.371s;
test-host wall 53.842s and 5.724s. Launcher waited 90s, build waited 15s,
row slots waited 0s. One ownership-status read timed out after 12s during the
morning build; the bound run completed with clean source/build provenance.
The report's `builds: 2` counts manifest build definitions; only one build was
used per run, the other was marked unused.

Morning admission attempt `20261016-002516-022f` stopped before compilation:
`refused: tracked-snapshot`, zero tests. Launcher waited 215s and held 28s;
run wall 27s. Its terminal wait also refused malformed receipt shape
`outputPaths required for outputNames`. The executor exited. This failed admission
was preserved and retried once with identical source, filters and deadlines.
It is neither a test failure nor a baseline pass. The subsequent four-case audit
checks exact method identities, nonzero counts, SHA/clean/build bindings, task
ownership, report Markdown digests and copied TRX hashes.

Earlier additional runs remain documented: slot-gated checkpoint CLI bootstrap;
two unsuccessful CP-1 admissions (Git timeout and missing apphost launch); exact
stamped-DLL custody red diagnostic; and the exact 16 inherited-failure replay.
These were explained diagnostic exceptions, not hidden test selections.

Full historical detail and all original errors:
`C:\Antiphon\worktrees\card-task-7a7b92ba\.antiphon\task-7a7b92ba.md`.
Persistent ignored evidence root:
`C:\src\Antiphon\.antiphon\investigations\card-1287-q2-7a7b92ba`.
The `refinement/current`, `refinement/morning`, and
`refinement/morning-admission` children retain exact reports, logs and receipts;
successful children also hold TRX, build-source stamps and test-binary hashes.
`refinement/four-case-audit.json` records the four exact outcomes and errors.
No generated evidence is committed.

Both newly created detached diagnostic worktrees and their alternate build
outputs were removed after confirming their exact paths, detached SHAs, clean
source and preserved evidence. All three comparison executors had exited.
The two successful run shadow copies were reclaimed by the checkpoint tool;
the failed-admission copy was removed with its diagnostic checkout.

Automatic approval review had rejected removal of four earlier task-produced
output roots with the stated reason `blocked by policy`. They remain:
`tests/Antiphon.Tests/bin-q2-red`, `tests/Antiphon.Tests/bin-q2-green`,
`tools/Antiphon.Checkpoints/bin-q2-cli`, and `src/Antiphon.HostCleanup/bin-q2-cli`.
That rejection was not bypassed. Failed fixture teardown temp roots were also
retained. This cleanup hold does not change the recorded test outcomes.

## Unedited checkpoint lines

The historical green repair, affected-class red, failed baseline admission and
four literal-source comparison rows follow. Refer to persistent copies above
when an original diagnostic checkout path has been removed.

```text
CHECKPOINT CP-2 commit=c2174f1f6e2f7ce6d831aa83982dc14d08169297 build=ok filter=/*/*/HostCleanupCheckpointCustodyTests/* executed=12 passed=12 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-7a7b92ba\.antiphon\checkpoints\20261015-235231-5d91\rows\CP-2\run.trx slot=granted waited=0s dirty=0 source=c2174f1f6e2f7ce6d831aa83982dc14d08169297 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=c2174f1f6e2f7ce6d831aa83982dc14d08169297 build=reused filter=/*/*/(HostCleanup*)|(OutputCleanup*)|(CheckpointOwned*)/* executed=151 passed=135 failed=16 skipped=12 trx=C:\Antiphon\worktrees\card-task-7a7b92ba\.antiphon\checkpoints\20261015-235231-5d91\rows\CP-3\run.trx slot=granted waited=0s dirty=0 source=c2174f1f6e2f7ce6d831aa83982dc14d08169297 sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-5 commit=b7b2f112da759fe578b1a48ba16dfb747c55418c build=ok filter=/*/*/HostCleanupOrchestrationTests/AddHostCleanup_upgrades_current_master_without_changing_existing_rows executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\evidence\code-7a7b92ba\current\.antiphon\checkpoints\20261016-001439-5888\rows\CP-5\run.trx slot=granted waited=0s dirty=0 source=b7b2f112da759fe578b1a48ba16dfb747c55418c sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=b7b2f112da759fe578b1a48ba16dfb747c55418c build=reused filter=/*/*/OutputCleanupTests/nested_repository_tracked_output_is_owner_checked_and_kept executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\evidence\code-7a7b92ba\current\.antiphon\checkpoints\20261016-001439-5888\rows\CP-6\run.trx slot=granted waited=0s dirty=0 source=b7b2f112da759fe578b1a48ba16dfb747c55418c sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-7 commit=cc8798c1fa4f3c2faa834283aef080d8e5fd28f0 build=failed filter=/*/*/HostCleanupOrchestrationTests/AddHostCleanup_upgrades_current_master_without_changing_existing_rows executed=0 passed=0 failed=0 skipped=0 trx=n/a slot= waited=0s slot-reason=refused: tracked-snapshot dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-8 commit=cc8798c1fa4f3c2faa834283aef080d8e5fd28f0 build=failed filter=/*/*/OutputCleanupTests/nested_repository_tracked_output_is_owner_checked_and_kept executed=0 passed=0 failed=0 skipped=0 trx=n/a slot= waited=0s slot-reason=refused: tracked-snapshot dirty=unknown source=unknown sourceState=unknown buildSource=unknown
```

```text
CHECKPOINT CP-7 commit=cc8798c1fa4f3c2faa834283aef080d8e5fd28f0 build=ok filter=/*/*/HostCleanupOrchestrationTests/AddHostCleanup_upgrades_current_master_without_changing_existing_rows executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\evidence\code-7a7b92ba\morning\.antiphon\checkpoints\20261016-002818-5645\rows\CP-7\run.trx slot=granted waited=0s dirty=0 source=cc8798c1fa4f3c2faa834283aef080d8e5fd28f0 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=cc8798c1fa4f3c2faa834283aef080d8e5fd28f0 build=reused filter=/*/*/OutputCleanupTests/nested_repository_tracked_output_is_owner_checked_and_kept executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\evidence\code-7a7b92ba\morning\.antiphon\checkpoints\20261016-002818-5645\rows\CP-8\run.trx slot=granted waited=0s dirty=0 source=cc8798c1fa4f3c2faa834283aef080d8e5fd28f0 sourceState=clean buildSource=verified
```
