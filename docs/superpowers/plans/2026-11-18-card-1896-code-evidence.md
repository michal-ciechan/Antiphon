# CARD-1896 Code evidence

Task / original landing owner: `79e80be8-3ede-42c1-b546-91a34575bd2a`.
Branch `feat/card-task-79e80be8`, worktree `/work/worktrees/ws-07`.
Base `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`.
Plan: [Final checkpoints](2026-11-18-card-1896-checkpoints.md).
The final tested SHA, full receipts, class counts, manual checks and output-cleanup
ledger are in the final report and ignored `.antiphon/task-79e80be8.md`.
This committed record preserves the red-first history before Final qualification.

## Implemented behavior

Stable aggregate prefix rows; fair largest-first family rounds; independent byte
fit; disabled seven-day scratch/log families; explicitly owned, clean and published
scratch clones; configured ANSI root with runtime census and launch-gate exclusion;
additive candidate root preserved in server residue rows. The scratch producer and
operator policy are documented in `docs/docker-stack.md`. No live sweep was enabled.
Existing safety assertions, timeouts and hard ceilings were not loosened.

Initial WIP stopped on conflicting output instructions. The caller then explicitly
authorized checkpoint-owned isolated outputs and required their literal-path cleanup.
The source is now implemented; Final execution is tracked separately by actual receipts.
No permission remains outstanding and none was inferred for deployment or live cleanup.

## Historical red-first results

All five runs below were committed, clean, expected-SHA-pinned and pushed before
running. Fresh TRX method/argument identities and nonzero counts were inspected.
The corresponding fixes followed these observed failures; none was a build/fixture
failure or a self-compare. Checkpoint start/end source SHA, dirty count and fingerprint
agree and each build has `sourceState=clean buildSource=verified`.

| Row | Actual tested SHA | Run | Executed / passed / failed / skipped | Failure | Build wait / row wait | Build phase / test body / total wall seconds |
|---|---|---|---|---|---|---|
| CP-1 | `22de4263fb3706b4e9f9a02666dc0c4542945689` | `20261118-075357-876e` | 5 / 0 / 5 / 0 | per-name rows, oldest-first starvation, byte overflow starvation | 120s / 15s | 98.68 / see TRX / 312.535 |
| CP-4 | `d97ffbba65ce5bd3f99343a25d0a4d15eb668fdb` | `20261118-080020-5522` | 8 / 0 / 8 / 0 | families absent, zero candidate at named assertions | 15s / 45s | 72.05 / 0.269 / 189.718 |
| CP-5 | `991d167e24df2a555a20af02b5fbab38cca810dc` | `20261118-080617-c97f` | 1 / 0 / 1 / 0 | configured log absent from real production factory response | 0s / 0s | 99.68 / 0.376 / 171.525 |
| CP-6 | `05b2fefe31f5ee6eac6dc986765a5d5b66ee47d3` | `20261118-081201-f317` | 1 / 0 / 1 / 0 | stored /tmp instead of /fixture/session-logs | 0s / 210s | 300.387 / 9.637 / 618.107 |
| CP-7 | `e32177b503433b2d6d478616bd6f76dafbe82f99` | `20261118-082603-b0df` | 1 / 0 / 1 / 0 | actual [null,null], required [live_process,scratch_index_hidden] | 30s / 75s | 101.244 / 1.922 / 246.833 |

Historical total: 16 executed, 0 passed, 16 expected assertion failures, 0 skipped.
CP-7 reached the dirty/unpublished/preview/published-removal assertions before its
final two-refusal assertion. A red row is still red, not a qualified green selection.
S1 CP-2/3/8/12 rerun all red methods and variants; CP-9/10/11 supply required guards
and adjacent classes. Their authoritative outcomes belong to the Final receipt.

The native checkpoint tool was initially built through `build-slot.ps1` (135s wait,
8s held), with native output. Import/preflight corrections involved an ID header,
OR operand syntax, a wrong net10 DLL path and two mistyped expected SHA values;
these launched no tests and confer no credit. Historical CP-1/4 build-duplication
warning came from the same After label on different committed red slices; Final
labels distinguish them and Final rows reuse one build per project. Direct builds
for derived-index regeneration and land-graph evaluation are required manual tools,
not unlisted test selections. The compiled checkpoint DLL leases each driver itself.

All builds/runs set `ANTIPHON_LINUX_COMPILER_POLICY=0`; output is checked for absence
of `LINUX COMPILER`. Load is sampled before each command, with 30s polling above16;
CP-7 admitted at 15.61 after readings as high as34.00. Internal checkpoint drivers
are slot-gated; their test deadline is9m. Slot waits are retained independently,
not hidden as test time. No unchanged proof selection exceeded the 3 normal+2 loaded
repeat cap; no repeat is owed after green.

## Repository and manual checks

The private scratch-producer witness passed4 checks: exact path, exact marker,
existing-root refusal, mismatched task-owner refusal. It is retained as
`.antiphon/c1896-manual.ps1`. No existing checkout was registered or removed.
The required runner-default/session-runner GETs were read; nothing was pinned and
no desktop work occurred. No migration exists; migration red/green counts N/A.

Recomputed pins (no pin changed):

| Pin | SHA-256 |
|---|---|
| policyHash | `5c478a13259f9d4bc3e638ab28ecee3ef517dc292d04f075246a796da944e352` |
| LegacyExclusionDigest | `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` |
| PinnedInputDigest | `5e8c164421506fd1024e6da39351c097c4119f475957c41ee030c0c391488994` |

The policy calculation imports `nightly-common.ps1` plus `nightly-policy.ps1` with
`ANTIPHON_NIGHTLY_CORE=off`. Protected input uses `test-live-lane-run.ps1 -PrintInputDigest`.
Legacy uses the checked-in witnessed-row keys, exclusion rules and canonical JSON
hash helpers. Initial missing-helper and unavailable-python read-only attempts were
corrected in PowerShell; they were not test outcomes. Final requires derived driver
native build/two runs, index last commit if changed, C1729 0/162, land-graph Check,
OS-witness declaration lint, full-range evidence-diff and merge-tree checks.

PC-1..PC-12 and every variant in the plan remain PENDING SourceLanding Mutation.
No deliberate mutant was run; missing-control discovery belongs to Mutation.
The original operator waiver is respected. Ordinary Review follows completed V/R.
Server and runner activation will be needed after caller landing; neither was
restarted here. Production PostgreSQL, Docker volumes and Windows qualification
were untouched. Fixture-owned isolated test databases follow the test harness.

## Unedited checkpoint lines

The following lines are copied from each run's retained `report.md`. Their JSON
build source records were inspected for matching start/end SHA/fingerprint and
zero dirty files. TRX/JSON/log payloads remain in ignored checkpoint directories.

~~~text
CHECKPOINT CP-1 commit=22de4263fb3706b4e9f9a02666dc0c4542945689 build=ok filter=/*/*/C1677TempSweepTests/(Unregistered_random_and_process_names_aggregate_by_prefix*)|(Entry_limit_admits_one_largest_entry_per_family_before_second_turn*)|(Entry_over_remaining_byte_budget_does_not_starve_a_fitting_family*) executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/ws-07/.antiphon/checkpoints/20261118-075357-876e/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=22de4263fb3706b4e9f9a02666dc0c4542945689 sourceState=clean buildSource=verified
~~~

~~~text
CHECKPOINT CP-4 commit=d97ffbba65ce5bd3f99343a25d0a4d15eb668fdb build=ok filter=/*/*/C1896TempSweepPolicyTests/* executed=8 passed=0 failed=8 skipped=0 trx=/work/worktrees/ws-07/.antiphon/checkpoints/20261118-080020-5522/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=d97ffbba65ce5bd3f99343a25d0a4d15eb668fdb sourceState=clean buildSource=verified
~~~

~~~text
CHECKPOINT CP-5 commit=991d167e24df2a555a20af02b5fbab38cca810dc build=ok filter=/*/*/C1677TempSweepNativeTests/Production_owner_seam_reads_each_entrys_own_uid executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/ws-07/.antiphon/checkpoints/20261118-080617-c97f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=991d167e24df2a555a20af02b5fbab38cca810dc sourceState=clean buildSource=verified
~~~

~~~text
CHECKPOINT CP-6 commit=05b2fefe31f5ee6eac6dc986765a5d5b66ee47d3 build=ok filter=/*/*/C1677TempSweepServerTests/Configured_ansi_root_is_preserved_in_the_residue_row executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/ws-07/.antiphon/checkpoints/20261118-081201-f317/rows/CP-6/run.trx slot=granted waited=210s dirty=0 source=05b2fefe31f5ee6eac6dc986765a5d5b66ee47d3 sourceState=clean buildSource=verified
~~~

~~~text
CHECKPOINT CP-7 commit=e32177b503433b2d6d478616bd6f76dafbe82f99 build=ok filter=/*/*/C1677TempSweepNativeTests/Production_owner_seam_reads_each_entrys_own_uid executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/ws-07/.antiphon/checkpoints/20261118-082603-b0df/rows/CP-7/run.trx slot=granted waited=75s dirty=0 source=e32177b503433b2d6d478616bd6f76dafbe82f99 sourceState=clean buildSource=verified
~~~
