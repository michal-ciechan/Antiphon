# Review CARD-1315 fixture repair (Final) — task ad35fed2

Subject: repair task d83a15d5-c19c-47ad-99bb-92f71d5706d2, branch feat/card-task-d83a15d5,
tip a2eb78220f572db658c89e6adaf8f7fbfd8bb614, stacked on 40bb77c9ca355e2039b02d6864cc18c92e1ec9a4
(production change), rc base 5e5d5cc728949420647d15a932766e70755a6f01.
Reviewed read-only in the runner mirror /work/worktrees/task-ad35fed2 at the same tip.

## Verdict

Clean. No regression of existing behaviour and no new reachable fail-open. The only red test in
the required selection is the inherited `C1192_Npm_seed_install_and_refill_use_work_volume`
(`refill-volume-source=0`, CARD-1328), which fails with identical fixture facts at the rc base.
Next: land d83a15d5 at a2eb7822 on rc/2026-10-15, then the rc merge into master.

## Change reviewed (40bb77c9..a2eb7822)

- `scripts/fixtures/c913-seed-contract.sh`: absent `volume:inspect` prints `[]` on stdout and
  `Error response from daemon: get <name>: no such volume` on stderr, exit 1 (D1).
- `scripts/fixtures/c973-marker-reader.sh`: extracts `c849_fixture_resource_state` (D2).
- `scripts/fixtures/c1315-fail-closed.sh`: new mode `modern-volume-absent` (volume state 1; the
  same text on a network inspect stays state 2).
- `tests/Antiphon.Tests/Scripts/CacheFixtureCompletionTests.cs`: new witness
  `Modern_daemon_missing_volume_text_is_positive_absence`.
- `docs/superpowers/plans/2026-10-17-card-1315-fixture-repair.md`: plan with CP-1/CP-2.
- `scripts/c590-remote.sh`: unchanged. Blob `8803cb5c0ee8b721fb7284e62da63026dc9eb273` at both
  40bb77c9 and a2eb7822 (`git diff` empty). The reviewed production semantics are intact: the six
  fixture namespace sites classify through `c849_fixture_resource_state` with
  `FixtureVolumeInspectFailed` on unknown; `case_verify_runner_caches_retired` separates
  `TempPrivateVolumeRemains`/`MainTmpMissing` from `CacheVolumeInspectFailed` and names a failed
  `docker ps` as `CacheDonorLookupFailed`; seed hash failures are `FixtureSeedPayloadChanged` and
  the prune manifest hash is `CacheSeedMarkerInvalid`, both requiring a 64-hex digest.

## Brief items

1. CP-1 and CP-2 re-run through the checkpoint tool (runs below). CP-1 19/19. CP-2 14 executed,
   13 passed, 1 failed, and the failure is only the inherited npm test (serial runs 2 and 3).
   This matches Code's CP-1 and CP-2 lines.
2. Inherited at base: the npm fixture mode run directly against a `git archive` of 5e5d5cc7 prints
   `npm-work-volume=1 refill-volume-source=0 offline-network-none=0`, byte-identical to HEAD; the
   test asserts `refill-volume-source` is 1 and fails the same way at both. The tool's `--baseline`
   classification did not help (see advisory).
3. `scripts/c590-remote.sh` is byte-identical to 40bb77c9 (above).
4. Witness can go red: with the modern arm deleted from a scratch copy of the helper, the volume
   call returns 2 and the fixture prints `FAIL modern-volume-absent state=2`, exit 1; the intact
   worktree prints `PASS modern-volume-absent`, exit 0. Scratch copies only; no worktree edit.
5. Other consumers of the repaired fixtures: `RemoteScriptContractTests/C913_*` (six methods: the
   remaining c913 modes tree/seed/ready/prune/inventory plus receipts) 6/6 green serially;
   `RemoteScriptContractTests/C973_*` (twelve rows, every reader-harness user) 12/12 green.
   `CacheFixtureCompletionTests` (CP-1) covers the other c913 consumers. `c727-fake-verify.ps1`
   only passes the reader path through to bash (same reader, covered by C973).
6. Real Docker 27.5.1 on this host: `docker volume inspect <missing>` prints `[]` on stdout and
   `Error response from daemon: get <name>: no such volume` on stderr, exit 1, matching both
   fixtures byte for byte; network prints `Error response from daemon: network <name> not found`
   and container `Error response from daemon: No such container: <name>`, both matched by the
   helper's arms. Docker 24.0.2 is not available here; the previous Review checked it.

## Checkpoint runs

All rows: source a2eb78220f572db658c89e6adaf8f7fbfd8bb614, sourceState=clean, dirty=0,
buildSource=verified, slot=granted. Evidence under `.antiphon/checkpoints/<run>/`.

| Run | Mode | CP-1 | CP-2 | CP-3 `C913_*` | CP-4 `C973_*` | Outputs |
|---|---|---|---|---|---|---|
| 20261017-211138-8e27 | rows overlapped, `--baseline 5e5d5cc7` | 19/19 | 14 exec, 12 pass, 2 fail | 6 exec, 5 pass, 1 timeout | 12/12 | kept, removed by hand |
| 20261017-213256-0e0c | `--rows CP-2,CP-3 --serial` | not selected | 14 exec, 13 pass, 1 fail (npm) | 6/6 | not selected | kept, removed by hand |
| 20261017-214304-ae17 | all rows, `--serial --clean-on-red` | 19/19 | 14 exec, 13 pass, 1 fail (npm) | 6/6 | 12/12 | deleted by the tool |

Run 1's two extra reds were contention: it overlapped CP-2 and CP-3 while the host 15-minute load
average was above 20 on 24 cores. `C1192_Race_programs_and_feed_share_the_work_volume` reported
`race-diagnosis=FixtureRestoreBarrierFailed` (30-second barrier bound) and
`C913_Full_marker_binds_verified_recovery_before_publication` hit the harness's 60-second limit.
Both pass standalone (seed mode 26 s; race mode prints `race-boundary=1` at HEAD and at the
archived base) and in both serial runs.

Plan used: a scratch copy of the repo plan with CP-1 and CP-2 verbatim plus two extra rows
(CP-3 `/*/*/RemoteScriptContractTests/C913_*`, CP-4 `/*/*/RemoteScriptContractTests/C973_*`,
Build = CP-2). The filter strings for CP-1 and CP-2 are identical to the repo table.

Receipt validation for run 20261017-214304-ae17 (`validate-checkpoint-receipt.ps1`,
`-ExpectedSourceSha a2eb7822…`): rows CP-1, CP-3, CP-4 `CHECKPOINT SOURCE VALID source=a2eb7822… rows=3`;
the whole run and the CP-2 row refuse `row_failed`, because the plan's CP-2 row carries the
inherited red by design. The tool's own `wait --expected-source-sha a2eb7822…` validated every
run's receipts (recorded exit returned; no `receipt-source-SHA-mismatching`). Runs 1 and 2 refuse
`cleanup_refused` because red runs retain outputs. No receipt was edited.

## Verification completeness

Rows match the plan's `### Checkpoints` and the Code claims (CP-1 19/19; CP-2 14/13/1 inherited).
No build or test driver ran outside the tool. Unlisted executions, with reasons: run 1's tool
baseline commands (`git fetch/rev-parse/worktree add`, a base build, a base test run) came from
`--baseline`; direct bash fixture invocations (c1315 `modern-volume-absent` against a scratch copy
and the worktree, c913 `seed`, c1192 `npm` and `race` at HEAD and at an archived 5e5d5cc7) were
read-only red-proof and base-comparison probes, the kind the plan's cost section names.
`check-evidence-diff.ps1` 5e5d5cc7..a2eb7822: 3 commits, 0 entries, 0 violations, exit 0.
`git merge-tree --write-tree` against origin/rc/2026-10-15 (5e5d5cc7) and origin/master
(f01104c8): both conflict-free.
PCs: none in this plan; the parent production controls stay pending for SourceLanding Mutation.
The stage contract's session, queue and transcript audit items do not apply to this fixture-only
change. All `bin-c1315-*` output directories are gone and the tree is clean.

## Advisory (D-2, separate from the verdict)

- Checkpoint tool `--baseline`: the base worktree built (4m38s) but the base test run exited after
  3 s with no TRX and no captured log, so every red was labelled NEW. Base facts were established
  by running the fixtures directly instead. Worth a tool card: capture the baseline run output.
- The c913 stub prints `[]` even for `docker volume inspect -f ...`; the real CLI prints an empty
  line there. Every consumer of the formatted form uses the exit status, so there is no test
  effect.
- `validate-checkpoint-receipt.ps1` cannot qualify a row whose plan expects an inherited red
  (`row_failed`); while CARD-1328 is open the CP-2 row can only be validated through the tool's
  `wait --expected-source-sha` binding.
