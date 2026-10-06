# CARD-1076 S3 Final Review report (task dbabe3aa)

Verdict: clean. No regression of existing behaviour and no reachable fail-open in S3 (D-5 lease-free re-arm). Reviewed `feat/card-task-08871966` at tip `85c55ef95903a814154f067ef847b0bda4321a98` (tested source `b3cbe3b5c8de7122a73b27057074c673a20a0c0a` plus the Code report commit; the two differ only by `.antiphon/task-08871966.md`). Code owner / landing owner `08871966-1134-4b28-8d6f-92f06d240424`. Base `678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4`. `origin/master` has since moved to `391302f125b1b2d2579d7033759f8add57cb6e3a` (CARD-1079 S2); none of those commits touch the three S3 files. Plan `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md`.

## Rerun (one checkpoint-tool run, 20261006-090232-df3a, Debian 12 runner mirror)

Manifest: `import --plan` of the plan table into the session scratchpad plus one review row `CP-R3P` (`/*/*/AgentTaskDispatcherPredicateTests/*`, reuses CP-3's build, min 17) because CP-4 cannot go green before S4/S5. Tool bootstrap `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-rev-dbabe3aa-tool/` under slot `b8d1ba8b` (held 3 s) is the one explained unlisted build; the run was `dotnet exec` of that DLL. One test build (`bin-c1076-b`, slot `0b99561a`), rows under slots `a68efc77` and `701c3abd`, all `waited=0s`. No other build or test command ran. `bin-c1076-b/` was deleted by the green run; `bin-rev-dbabe3aa-tool/` deleted by hand after confining the resolved path; no `bin-*` directory remains.

```
CHECKPOINT CP-3 commit=85c55ef95903a814154f067ef847b0bda4321a98 build=ok filter=/*/*/(RemoteWorkspacePreparerTests*)|(DispatcherRemotePrepStarvationTests*)|(PhoneHomeTaskDispatchProjectionTests*)/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-dbabe3aa/.antiphon/checkpoints/20261006-090232-df3a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=85c55ef95903a814154f067ef847b0bda4321a98 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=165.8228944s startup=70.3190124s testsWall=16.5475326s teardown=2.4986987s hostWall=89.3652503s
CHECKPOINT CP-R3P commit=85c55ef95903a814154f067ef847b0bda4321a98 build=reused filter=/*/*/AgentTaskDispatcherPredicateTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-dbabe3aa/.antiphon/checkpoints/20261006-090232-df3a/rows/CP-R3P/run.trx slot=granted waited=0s dirty=0 source=85c55ef95903a814154f067ef847b0bda4321a98 sourceState=clean buildSource=verified
PHASES CP-R3P slotWait=0s build=0s startup=3.9477086s testsWall=0.0245348s teardown=0.8127724s hostWall=4.7850156s
verdict: GREEN exit=0   wall 4m21s
CHECKPOINT SOURCE VALID source=85c55ef95903a814154f067ef847b0bda4321a98 rows=2   (validate --evidence report.json, exit 0)
```

TRX rosters: CP-3 = `RemoteWorkspacePreparerTests` 23, `DispatcherRemotePrepStarvationTests` 4, `PhoneHomeTaskDispatchProjectionTests` 8, all `Passed`; the S3 arms `C672_prepared_task_launches_while_a_land_holds_the_lease(rearm)` and `(rearm-no-baseline)` and the S2 tests `C1076_prep_push_carries_the_task_tag_and_the_configured_budget`, `C1076_pushes_on_one_repository_run_one_at_a_time_and_the_waiter_names_the_pusher` executed. CP-R3P = `C1076_RearmsPreparedWorktree` 9 + `C672_LaunchesPreparedMirror` 8, all `Passed`.

Evidence guard: `scripts/check-evidence-diff.ps1 -BaseRef 678f246fa… -HeadRef 85c55ef95…` → `commits=2 entries=1 violations=0`, exit 0 (the whole task range, report commit included).

| ID | This round |
|---|---|
| V-7 (rearm, rearm-no-baseline), V-5, V-6, R-1, R-7, R-10 | Passed in CP-3 |
| V-8 (9), R-5 (8) | Passed in CP-R3P |
| V-9..V-15, R-4, R-6, R-9, R-11, R-12; CP-4..CP-7; whole Unit lane | Not run: S4/S5 not written, rows pinned to the final SHA, Unit lane caller-owned |
| R-2, R-3, R-8 (S1 classes) | Not rerun here; S3 does not touch them; S2 Review ran them at the base |
| PC-6, PC-7 (and PC-1..PC-5, PC-11) | Pending for SourceLanding Mutation; untouched |

## Code report versus the `### Checkpoints` table

CP-3 reported once with the exact filter, 35/35, clean certificate at `b3cbe3b5c`. CP-4..CP-7 are `After: all` and correctly not run, with the reason stated. Unlisted runs each carry a reason and a slot id: the tool bootstrap build, the red-first V-7 run (7 executed, 6 passed, 1 failed; the `rearm` arm failed on `HeldOnLease` expected 0 actual 1 before the `needsLease` wire, a genuine red for PC-6), and `CP-S3-PRED` (predicate class plus the registry guards, named invariants V-8/R-5, 221 s). No broad run, no zero count, no stub test. Report commit is Markdown, 64 lines, outside checkpoint directories.

## Code review of `b3cbe3b5c`

(a) Fail-closed. `RearmsPreparedWorktree` is a conjunction of eight guards; every null, empty or default value makes it false except two that are intended: `VerificationRound` default is not `Interim` (identical to `LaunchesPreparedMirror`), and a whitespace-only `ProgressBaselineJson` counts as captured. The latter is harmless because the capture guard at `AgentTaskDispatcher.cs:4870` uses the same `string.IsNullOrEmpty`, so a row the predicate exempts never reaches `CaptureProgressBaselineAsync`; `ProgressBaselineJson` has exactly one writer (`:4905`) and nothing nulls it, so the pre-claim `task` and the reloaded `claimed` cannot disagree on it. An empty-string `RemoteWorktreePath` reads as "not recorded" in the predicate, in `LaunchesPreparedMirror` and in `PrepareRemoteWorkspaceAsync` (`is { Length: > 0 }`, `:6231`); the `is null` test at `:4975` would make such a row spin `NotClaimed`, but no writer produces an empty string and that shape predates S3 (CARD-0633). First launch (`WorktreePath` null), repair, snapshot, Interim, recorded mirror, missing baseline, local, Shared and ReadOnly rows all keep today's lease behaviour.

(b) Real risk. Traced `DispatchOneAsync` for an exempted row from the lease decision to `HeldForRemotePrep`: the worktree cut (`:4798`), repair prep, `ValidateVerificationAsync` and the baseline capture are all skipped; `PrepareRemoteWorkspaceAsync` consults the runner registry and the row only; `TryBegin` hands the push and mirror to the preparer off the tick. The tick runs no git for this row. The preparer's push was never under the lease, before or after S3. What S3 changes is ordering: a re-arm's push may now start while a land holds the lease instead of after it. Together with S1 (D-3, a live tagged push no longer fences the lease) the push and a land can overlap in either order. The push writes `refs/remotes/origin/<task-branch>` and the branch's upstream config under git's own ref and config locks; a lock collision fails the push, which the preparer backs off and retries from the row. That is the model CARD-0672 D-1 and D-3 already accepted; nothing a land reads-then-writes depends on those two writes. Disclosure, not a defect.

(c) `LaunchesPreparedMirror` is byte-for-byte unchanged (the diff adds only the new method and the `&& !RearmsPreparedWorktree(task)` conjunct). It has one caller, `needsLease`. The two predicates are mutually exclusive on `RemoteWorktreePath`, so no row for which `LaunchesPreparedMirror` was true or false changes its lease outcome unless `RearmsPreparedWorktree` is true. R-5 8/8 green.

(d) The matrix arms each assert the production predicate against the argument's expected bool; dropping any guard reds exactly its arm (PC-7 reds `no-baseline`). V-7 `rearm` asserts `HeldOnLease == 0`, Queued, `RemoteMirrorRequested` last, no lease sentence, empty waiters and exactly one `WorkspaceMirror` request; `rearm-no-baseline` asserts `HeldOnLease == 1`, the `LeaseHeldByOwner` sentence and zero mirror requests. The Code report's unwired run shows the `rearm` arm actually red. No self-compare or constant assertion.

Not applicable: producer/recipient, queue and UserPrompt transcript audit; S3 changes a dispatcher lease predicate and no delivery path.

## Disclosures (Backlog, not defects)

- `rearm-no-baseline` is configuration-identical to the pre-existing `unprepared` arm (the rig seeds `WorktreePath` and leaves the baseline null); redundant but it is the named PC-7 witness.
- No matrix arm for an empty-string `ProgressBaselineJson` or `RemoteWorktreePath`; a mutation of the baseline guard from `IsNullOrEmpty` to `is not null` would survive the matrix. Not a fail-open (the capture guard would still run the capture, whose pins take their own non-reentrant lease), so a one-line `empty-baseline` arm is a Backlog nicety.
- D-5's text says `RemoteWorktreePath` **null**; the code uses `IsNullOrEmpty`, consistent with `LaunchesPreparedMirror` and the claim path.

--- review evidence ---
subjectTaskId: 08871966-1134-4b28-8d6f-92f06d240424
reviewedSourceSha: 85c55ef95903a814154f067ef847b0bda4321a98
reviewedSourceClean: true
ordinaryScopeCompleted: Interim
