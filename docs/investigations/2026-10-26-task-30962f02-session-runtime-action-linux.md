# SessionRuntimeActionTests Linux investigation (task 30962f02)

The Windows group-29 failure did not reproduce on Linux: 28 executed, 28 passed,
0 failed, 0 skipped. No fixture or product change is justified by this run.
The native Windows failure remains unresolved and needs the exact class rerun below.

## Source and scope

- Assigned branch: `feat/card-task-30962f02`.
- Tested source: `105792f7d22f297746cdb338976f52e424b76fa7`, the fetched
  `origin/rc/2026-10-25` candidate; tracked source/index were clean throughout execution.
- Host: server2/Linux, Debian 12, .NET 9.0.21, SDK 10.0.402, TUnit 1.44.0.
- Only `tests/Antiphon.Tests/Application/SessionRuntimeActionTests.cs` was selected.
  No desktop commands, native locks, release scripts or sibling tests were run.
- This report is the only tracked change. All assertions, fixtures and production
  behavior remain unchanged. The caller's existing debug/fix authority was sufficient.

## Execution and retained evidence

One isolated build and one exact class run, both under the host build-slot gate.
The explicit brief permits direct `--no-build` execution because the checkpoint
assembly reader cannot handle the test DLL's size; no checkpoint validation is claimed.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-30962f02-base-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-debug30962/ --property:UseAppHost=false --verbosity:quiet
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-30962f02-base-class -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug30962/ --property:UseAppHost=false -- --treenode-filter '/*/*/SessionRuntimeActionTests/*' --report-trx --report-trx-filename base.trx --results-directory /work/worktrees/task-30962f02/.antiphon/debug-30962f02/base-results
```

The run additionally set the existing `ANTIPHON_TEST_PHASE_*` variables: root
`/work/worktrees/task-30962f02/.antiphon/debug-30962f02/base-phases`, run ID
`debug30962-base`, group `SessionRuntimeActionTests`, attempt `1`, suite `Antiphon.Tests`.
No timeout, retry or parallelism override was introduced.

Build: exit 0, 0 errors, 1,196 warnings; 225.36 s MSBuild elapsed.
Slot `9c127af1-8810-418c-be3c-d50f6f156d3f`: waited 0 s, held 226 s,
maxcpucount 6. Test slot `fc24fed9-db9a-45a8-8757-0e16f8b56908`: waited 0 s,
held 58 s; test runner reported 55.149 s. Both leases released normally.

| Class/method | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| SessionRuntimeActionTests (total) | 28 | 28 | 0 | 0 |
| C1259_commit_and_effect_admission_are_atomic | 7 | 7 | 0 | 0 |
| C1259_stream_pull_and_replay_preserve_order | 5 | 5 | 0 | 0 |
| C1259_saturation_keeps_ingestion_live_and_recovers_overflow | 5 | 5 | 0 | 0 |
| C1259_shutdown_joins_and_restart_reconciles_submission | 6 | 6 | 0 | 0 |
| C1259_retry_and_retention_preserve_pending_turn | 5 | 5 | 0 | 0 |

Ignored evidence under `/work/worktrees/task-30962f02/.antiphon/debug-30962f02/`:
`base-build.log`, `base-class.log`, `base-results/base.trx` and `base-phases/*.jsonl`.
The TRX preserves every variant's duration and identity. The phase recorder emitted
59 records, with assembly MVID `6a242d32-d17d-438d-8dfe-51632a03a206`.
Slowest case: `poison-page`, 17.277 s. The guarded `after-input` case passed in
15.278 s. Detailed external-call/fixture phase attribution is unavailable in this
recording and remains unknown; the pre-test startup/remainder is not attributed.
This single Linux sample is not a p95 or native qualification.

## Classification and native handoff

No reproduced cluster. CARD-0680 observation omission is not established: the
`after-input` branch already uses `ClaudeDialogFixture.CreateBridgeAsync`, which
supplies the persisted session generation and guarded composer observation.
The native failure's method, assertion and stack were not in the brief; desktop TRX
was inaccessible here. Windows semantics, shared-state interaction and timing under
qualification load remain unresolved possibilities, not diagnosed root causes.
No Windows string witness was added because no Windows-semantics cluster was identified.

After the qualification owner releases its native lane, run one fresh Windows row
against a producer-owned native build of the candidate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-30962f02-native -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-native30962/ -- --treenode-filter '/*/*/SessionRuntimeActionTests/*' --report-trx --report-trx-filename native.trx --results-directory .antiphon/debug-30962f02-native
```

Expected count: 28 executed, with no skips. `bin-native30962/` must first be built
on Windows under a slot. Use a fresh results directory. If red, retain the exact
variant, assertion, stack and timings; those identify the next repair scope. No
native run was performed here and the original Windows failure is not cleared.

## Publication and merge checks

This is a report-only handoff; there is no fix to cherry-pick. At inspection,
`origin/rc/2026-10-25` remained `105792f7d22f297746cdb338976f52e424b76fa7`.
`git merge-tree --write-tree origin/rc/2026-10-25 HEAD` was conflict-free at the
tested source. The equivalent check against master
`7b7aea79b26716654923d79600f0e05cc6c60c29` found inherited add/add conflicts in
`.antiphon/task-af2b78c8.md` and
`docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`.
These exist at the unchanged rc base; no merge, rebase or conflict repair was attempted.
The report commit does not establish master landability or resolve native qualification.
