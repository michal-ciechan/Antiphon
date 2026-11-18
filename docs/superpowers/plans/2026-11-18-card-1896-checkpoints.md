# CARD-1896 temp sweep: implementation and checkpoint record

Code task / landing owner: `79e80be8-3ede-42c1-b546-91a34575bd2a`.
Branch: `feat/card-task-79e80be8`; worktree: `/work/worktrees/ws-07`.
Task base and fetched `origin/master` at start:
`e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`.

Status: **WIP, implementation and verification incomplete**. Only the first
red-first witnesses have been authored. No test result is claimed by this file.

## Scope and invariants

The active CARD-1896 description requires one unregistered row per prefix,
fair family turns with large entries first under the entry/byte limits,
registered or relocated review/master scratch clones with an age policy, and
a registered policy for session ANSI logs older than seven days. All ownership,
age, holder, no-follow, inventory and live-task protections remain mandatory.
Preview remains the default and execute stays off. CARD-1981 cleanup, live
configuration, host restart and existing temporary-directory removal are excluded.

The retained [census](../../investigations/2026-11-17-server2-tmp-census.md)
identifies bare six-character scratch names and short decimal IDs in
`clr-debug-pipe-*` as row amplification sources. It also identifies dirty and
unpublished scratch checkouts. Registering those names for deletion based only
on age/uid/absence of a process holder would not protect their work. Scratch
registration must distinguish active ownership and preserve unpublished work;
do not turn on a broad review/master regex as a shortcut.

Proposed selection order: one candidate per eligible family in each round,
largest candidate first within each family and across each round, then oldest
write and ordinal name for ties. A candidate above the remaining byte budget
is kept without preventing smaller candidates from other families proceeding.
No change to the entry/byte/time/inventory ceilings is authorized.

## Verification design and current outcomes

| ID | Invariant / witness | Actual outcome |
|---|---|---|
| V-1 | `C1677TempSweepTests.Unregistered_random_and_process_names_aggregate_by_prefix`: real filesystem entries, two aggregate rows, exact count/bytes, no holder scans or deletion | Authored; NOT RUN; red unconfirmed |
| V-2 | `C1677TempSweepTests.Entry_limit_admits_one_largest_entry_per_family_before_second_turn`: three families, two tiny oldest entries, two large entries in one family; exact chosen order/count/bytes and kept entries in Execute and DryRun | Authored; both variants NOT RUN; red unconfirmed |
| V-3 | `C1677TempSweepTests.Entry_over_remaining_byte_budget_does_not_starve_a_fitting_family`: oversized entry stays and smaller family proceeds in Execute and DryRun | Authored; both variants NOT RUN; red unconfirmed |
| V-4 | Scratch clone registration/relocation, age policy, active-owner exclusion and unpublished-work preservation | NOT IMPLEMENTED; witnesses and exact checkpoint must be bound before implementation |
| V-5 | Session ANSI-log family: seven-day minimum, active session exclusion, default-off execution and preview | NOT IMPLEMENTED; witnesses and exact checkpoint must be bound before implementation |
| R-1 | Existing `C1677TempSweepTests`: age/owner/holder/denied/default/bounds behavior | NOT RUN |
| R-2 | `C1677TempFamilyRegistryTests`: registered exact patterns and producer pins; required by matching `temp-family-registry` impacted-classes row when RunnerTempSweep.cs changes | NOT RUN |
| R-3 | `C1677TempSweepNativeTests`: real production owner seam, holders, symlinks and changed-inventory refusal | NOT RUN |
| R-4 | `C1677TempSweepDispatchTests` and `C1677TempSweepServerTests`: admission/transport and off/preview/execute settings | NOT RUN; do not edit dispatch tests owned by CARD-1942 |
| R-5 | Recompute policy/legacy/input pins, land-graph Check, full task evidence diff, merge-tree | Pending except separately recorded read-only results |

The existing equal-size oldest-first bound witness remains valid under the
proposed tie-break. No assertions or timeouts have been relaxed. New witnesses
must be committed and fail for their named reason on unchanged production
source before any fix. The whole Unit lane remains nightly/rc-owned.

## Execution authorization

The caller reopened the task and explicitly authorized checkpoint-owned
`bin-<name>/` outputs. Direct builds remain native. Each created isolated output
must be removed by its exact literal path before the final report. The earlier
blocker below is retained as history and is resolved.

The task brief forbids custom build output directories, while requiring the
checkpoint tool. Its TUnit importer accepts only `<project> -> bin-<name>/`
(`tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`, build-cell parser),
and `ManifestValidator.OutputPathPattern` rejects native `bin/` output.
The strict build stamp also requires a fresh isolated output directory.

Clarification requested: permit checkpoint-owned isolated outputs while keeping
direct builds native, or retain the prohibition and stop at committed witnesses.
No output directory was created to bypass that decision. Command rows wrapping
ad hoc TUnit runs would not supply the mandated strict TUnit build provenance.
The table below records the concrete pending selection if the exception is
authorized; it is not a claim that execution was authorized or performed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | tests/Antiphon.SessionRunner.Tests -> bin-c1896-red/ | c1896-red | `/*/*/C1677TempSweepTests/(Unregistered_random_and_process_names_aggregate_by_prefix*)\|(Entry_limit_admits_one_largest_entry_per_family_before_second_turn*)\|(Entry_over_remaining_byte_budget_does_not_starve_a_fitting_family*)` | V-1,V-2,V-3 | all listed, 0 failed after fix; before fix expect five assertion failures | 5 | 15 |
| CP-2 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1896-green/ | c1896-green | `/*/*/C1677TempSweepTests/*` | V-1,V-2,V-3,R-1 | all listed, 0 failed | 5 | 3 |
| CP-3 | S1 | CP-2 | c1896-adjacent | `/*/*/(C1677TempFamilyRegistryTests*)\|(C1677TempSweepNativeTests*)\|(C1677TempSweepDispatchTests*)/*` | R-2,R-3,R-4 runner | all listed, 0 failed | 3 | 2 |
| CP-4 | S0 | tests/Antiphon.SessionRunner.Tests -> bin-c1896-policy-red/ | c1896-policy-red | `/*/*/C1896TempSweepPolicyTests/*` | V-4,V-5 | all listed, 0 failed after fix; eight missing-family assertion failures before fix | 8 | 3 |

This is the closed list for the authored witness slice, not a complete Final
manifest. V-4/V-5 and the server part of R-4 require additional short rows once
their implementation design is bound; they may not be marked passed or omitted
from the eventual Final. CP-1 intentionally records red before the fix and must
be run again green after the fix (or its methods qualified by CP-2 with fresh
TRX identities). No whole slow class or guard union is authorized unless the
classification/selector/pin paths are touched. Re-evaluate impacted-class globs
against the full task base..HEAD before Final.

## Performance budget

The first row has the required 15-minute cold-build estimate; test execution
is bounded separately to 570 seconds and each row runs serially. Existing
per-test limits remain 10 seconds. CP-2/3 estimates are provisional with no
measurement sample yet; CARD-1896 will collect the first red/green durations.
Do not raise a timeout to make a failure pass. No repetitions are required after
green; the normal/loaded proof cap remains three plus two.

All fixtures use private scratch roots; no database, container or web host is
needed for CP-1/2/3. The native witnesses own and join their own child processes.
Per-test filesystem/body/teardown timing not emitted by the existing fixture
is unknown, not zero. Retain per-test TRX durations and build/slot timing in
ignored `.antiphon/checkpoints/<run>/` evidence. Record `/proc/loadavg` before
every build/test and poll at 30 seconds until load1 is below 16. Export
`ANTIPHON_LINUX_COMPILER_POLICY=0` for every build/test/tool launch; no
`LINUX COMPILER` line is acceptable. All drivers take the host slot gate.
Pass the committed HEAD as expected source SHA and validate each fresh receipt.

No whole-assembly run is budgeted: the named invariants have bounded classes.
The first observed sample will establish actual cost before widening selection.

## Mutation handoff

No deliberate mutation is executed by Code. The operator waiver does not turn
an unexecuted positive control into a pass. Pending SourceLanding controls:

| ID | Proposed mutant | Exact witness / variants | State |
|---|---|---|---|
| PC-1 | Restore old hex/digit-only normalization | V-1 | PENDING Mutation |
| PC-2 | Restore global oldest-first order | V-2 Execute/DryRun | PENDING Mutation |
| PC-3 | Sort globally by size without family rounds | V-2 Execute/DryRun | PENDING Mutation |
| PC-4 | Stop all selection after an entry exceeds remaining bytes | V-3 Execute/DryRun | PENDING Mutation |

Controls for V-4/V-5 remain unbound pending their witnesses. Mutation owns
missing-control discovery. Restart: none for this WIP; final runner activation
requirements must be reported with the implemented production changes.
