# CARD-1896 temp sweep: Final plan and verification design

Task / original landing owner: `79e80be8-3ede-42c1-b546-91a34575bd2a`.
Branch: `feat/card-task-79e80be8`; worktree: `/work/worktrees/ws-07`.
Task base: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (fetched master at start).
Implementation and red-first history are in [the evidence record](2026-11-18-card-1896-code-evidence.md).
Final receipts and actual tested SHA are retained in `.antiphon/checkpoints/` and
`.antiphon/task-79e80be8.md`; a planned row is never itself a pass.

## Scope and implementation

Unregistered six-character random names and decimal process IDs aggregate by
prefix, with exact counts and bounded byte totals; unregistered content is never
removed. Each eligible family gets one turn per round, largest entries first
within a family and round, then oldest write and ordinal name for ties. An entry
above the remaining byte budget does not block a fitting entry. Existing entry,
byte, inventory and elapsed-time ceilings are unchanged.

The disabled `review-clone` and `session-ansi` families have a seven-day floor.
Scratch producers use `scripts/new-review-scratch.ps1`; the marker records an
existing owner worktree and cannot retrofit an existing checkout. The production
policy requires the owner and retiring copies absent, standalone clean clones,
no hidden index flags, the runner's exact live origin, and all local/ref-log
commits published. Linked worktrees, unknown content and failed audits are kept.
A workspace-slot owner is conservatively held while the slot exists, even after
reuse. Existing dirty/unpublished scratch checkout cleanup is CARD-1981, excluded.

ANSI logs use configured `SessionLogPath`; tracked sessions, in-progress launches
and process holders keep their logs. Final validation/unlink takes the same
identity gate as session creation. The added partial runtime method has one
production caller (this sweep); existing creation paths are unchanged. Candidate
Root is additive, with response Root as the old-peer fallback on the server.
Default preview, execute-off, age/uid/holder/no-follow/inventory protections stay.
No migration, live setting change, host restart or existing scratch removal occurs.

## Verification design

| ID | Witness / invariant | Bound selection |
|---|---|---|
| V-1 | `C1677TempSweepTests.Unregistered_random_and_process_names_aggregate_by_prefix`: two exact aggregate rows/counts/bytes, no holder scan or deletion | CP-1 red; CP-2 green |
| V-2 | `C1677TempSweepTests.Entry_limit_admits_one_largest_entry_per_family_before_second_turn`: three families, exact order/count/bytes, kept remainder, Execute and DryRun | CP-1 red; CP-2 green |
| V-3 | `C1677TempSweepTests.Entry_over_remaining_byte_budget_does_not_starve_a_fitting_family`: oversize stays, smaller family proceeds, Execute and DryRun | CP-1 red; CP-2 green |
| V-4 | `C1896TempSweepPolicyTests`: absent opt-in/registration and age floor; `C1677TempSweepNativeTests.Production_owner_seam_reads_each_entrys_own_uid`: real clones, active owner, dirty/unpublished work, hidden index flag, published preview and removal | CP-4/5/7 red; CP-3/12 green; M-1 producer |
| V-5 | `C1896TempSweepPolicyTests`: 167/169 hours, preview, holder and default-off; same native factory method: configured root, tracked session, launch gate, removal; `C1677TempSweepServerTests.Configured_ansi_root_is_preserved_in_the_residue_row`: wire/storage root | CP-4/5/6/7 red; CP-3/8/12 green |
| R-1 | Existing `C1677TempSweepTests`: owner, age, denied, bounds, holder and unknown scan behavior | CP-2 |
| R-2 | `C1677TempFamilyRegistryTests`: exact producer pins, regexes and count; matching temp-family-registry impact row | CP-3 |
| R-3 | `C1677TempSweepNativeTests`: real uid, symlink, cwd/fd and changed-inventory refusal; unchanged 10s test timeout | CP-3 |
| R-4 | `C1677TempSweepDispatchTests`, `C1677TempSweepServerTests`: admission/transport, off/preview/execute, backward-compatible root | CP-3/8 |
| R-5 | Three pin recomputations; native derived-index driver build plus two runs, second unchanged; land-graph Check; full base..HEAD evidence-diff, OS-witness lint and merge-tree | Manual repository checks |
| R-6 | Classification/policy/mechanical/platform/layering guards and impacted process-spawn rosters; C1729 VIOLATIONS=0 LISTED=162 | CP-3/8/9/11 |
| R-7 | Bounded adjacent retention integration classes selected by the current index for changed retention contracts/Program/service | CP-8/10 |

M-1: run the scratch producer only in a private fixture and check exact path,
exact marker, existing-container refusal and mismatched-owner refusal. Four
checks passed in `.antiphon/c1896-manual.ps1`. No production scratch was registered.

### Checkpoints

Historical red groups run at their recorded committed sources; S1 is Final.
CP-2/3/10/12 share one isolated runner build. CP-8/9 share one server build.
No full Unit lane or full assembly is selected. CP-9 is required because the
regenerated derived-index selector artifact changes; it is the brief's exact
named guard union, not a whole policy lane. Every row runs serially.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | tests/Antiphon.SessionRunner.Tests -> bin-c1896-red/ | c1896-red | `/*/*/C1677TempSweepTests/(Unregistered_random_and_process_names_aggregate_by_prefix*)\|(Entry_limit_admits_one_largest_entry_per_family_before_second_turn*)\|(Entry_over_remaining_byte_budget_does_not_starve_a_fitting_family*)` | V-1,V-2,V-3 | historical five assertion failures; green rerun covered by CP-2 | 5 | 15 |
| CP-2 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1896-green/ | c1896-green | `/*/*/C1677TempSweepTests/*` | V-1,V-2,V-3,R-1 | all listed, zero failed | 5 | 3 |
| CP-3 | S1 | CP-2 | c1896-adjacent | `/*/*/(C1677TempFamilyRegistryTests*)\|(C1677TempSweepNativeTests*)\|(C1677TempSweepDispatchTests*)\|(ProcessSpawnLimitTests*)/*` | V-4,V-5,R-2,R-3,R-4,R-6 | every named class nonzero, zero failed | 4 | 3 |
| CP-4 | S0-policy | tests/Antiphon.SessionRunner.Tests -> bin-c1896-policy-red/ | c1896-policy-red | `/*/*/C1896TempSweepPolicyTests/*` | V-4,V-5 | historical eight missing-family assertion failures; green rerun CP-12 | 8 | 3 |
| CP-5 | S0-native | tests/Antiphon.SessionRunner.Tests -> bin-c1896-native-red/ | c1896-native-red | `/*/*/C1677TempSweepNativeTests/Production_owner_seam_reads_each_entrys_own_uid` | V-4,V-5,R-3 | historical missing configured-log assertion; green rerun CP-3 | 1 | 3 |
| CP-6 | S0-server | tests/Antiphon.Tests -> bin-c1896-server-red/ | c1896-server-red | `/*/*/C1677TempSweepServerTests/Configured_ansi_root_is_preserved_in_the_residue_row` | V-5,R-4 | historical incorrect /tmp-root assertion; green rerun CP-8 | 1 | 3 |
| CP-7 | S0-race | tests/Antiphon.SessionRunner.Tests -> bin-c1896-race-red/ | c1896-race-red | `/*/*/C1677TempSweepNativeTests/Production_owner_seam_reads_each_entrys_own_uid` | V-4,V-5 | historical null launch/hidden-index refusals; green rerun CP-3 | 1 | 3 |
| CP-8 | S1 | tests/Antiphon.Tests -> bin-c1896-server-green/ | c1896-server-green | `/*/*/(C1677TempSweepServerTests*)\|(C1677RetentionServerTests*)\|(ProcessSpawnLimitTests*)/*` | V-5,R-4,R-6,R-7 | every named class nonzero, zero failed | 3 | 6 |
| CP-9 | S1 | CP-8 | c1896-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-5,R-6 | every named class nonzero, zero failed | 11 | 4 |
| CP-10 | S1 | CP-2 | c1896-retention | `/*/*/(C1677RetentionDispatchTests*)\|(C1677RetentionEngineTests*)\|(C1677RetentionInventoryTests*)\|(C1677RetentionNativeProcessTests*)\|(C1818GcOwnerRobustnessTests*)/*` | R-7 | every named class nonzero, zero failed | 5 | 4 |
| CP-11 | S1 | tests/Antiphon.Agents.Pty.Tests -> bin-c1896-pty-green/ | c1896-pty-roster | `/*/*/ProcessSpawnLimitTests/*` | R-6 | all listed, zero failed | 2 | 2 |
| CP-12 | S1 | CP-2 | c1896-policy-green | `/*/*/C1896TempSweepPolicyTests/*` | V-4,V-5 | all eight cases, zero failed | 8 | 2 |

Final command: compiled checkpoint tool `run --plan <this-path> --after S1
--serial --expected-source-sha <committed-HEAD> --row-timeout 9m --max-wait 570s`;
use `wait --run <id>` until terminal if exit 75. Invoking the already-built tool
DLL avoids an outer build/second lease. Checkpoint owns each build/test slot.
Export `ANTIPHON_LINUX_COMPILER_POLICY=0` for every driver, read `/proc/loadavg`
and wait in 30-second intervals for load1 <16 before admitting a new command.
The tool's internal TUnit runs use its 9-minute deadline; its generated argv
has no TUnit `--timeout` option. Direct TUnit runs, if needed and explained, must
include `--timeout 570s`. No deadline or assertion may be relaxed to obtain green.

The first cold build estimate is 15 minutes; test rows are bounded below ten.
Measured red runner test bodies: 0.27-1.92s; server body 9.64s. Slot wait and
cold build/provenance cost are reported separately (server run 618s total,
including 210s slot wait; its test execution was 44.43s). Unknown fixture/phase
measurements remain unknown. Any selection >30 minutes needs diagnosis before
widening it. No normal/loaded repetition is required after green; cap 3+2 per
unchanged proof selection. No Review-authorized flake exception is used.

The caller explicitly authorized checkpoint-only `bin-<name>/` outputs after
the initial WIP was blocked by the brief's contrary output instruction. Direct
builds remain native. Remove each created output by its exact literal absolute
path, never a wildcard/variable recursive delete, then record ignored-output
inspection and git status. Generated evidence remains ignored.

## OS and impact declaration

Production factory: `RunnerTempSweep.CreateForRunner`, called only in Program's
existing Linux phone-home branch. CP-3 calls that same factory over private roots
with real statx/proc scans, a real SessionRunnerRuntime and real local Git clones.
There is no changed Windows behavior or native Windows obligation. The configured
log root is a required existing settings property; request family opt-in is
covered absent and false/true through defaults and explicit family dictionaries.
The source partial adds a sweep-only gate operation; existing runtime launch,
input, delivery and persistence paths are unchanged. Broad symbol edges from a
partial runtime type are not an unbounded behavior change. The ordinary scope is
these bounded invariants plus matching impacted-class rows, under this Code
brief's Final profile. R-7 adds the adjacent retention integrations; the whole
runtime/Unit lane stays nightly/rc-owned. No impacted-classes rules were edited.

## Mutation handoff

Every control below is PENDING method-scoped SourceLanding Mutation. No deliberate
mutant was executed by Code. The operator waiver grants no pass. Mutation owns
missing-control discovery and exact packs under `docs/superpowers/mutations/`.

| ID | Proposed mutant | Exact witness / variants | State |
|---|---|---|---|
| PC-1 | Restore old normalization | V-1 method | PENDING |
| PC-2 | Restore global oldest-first order | V-2 method Execute/DryRun | PENDING |
| PC-3 | Global size sorting without family rounds | V-2 method Execute/DryRun | PENDING |
| PC-4 | Stop selection at first byte overflow | V-3 method Execute/DryRun | PENDING |
| PC-5 | Default-enable either new family | `C1896TempSweepPolicyTests.New_families_are_disabled_without_explicit_opt_in` both families | PENDING |
| PC-6 | Remove seven-day floor | `Scratch_clones_have_a_seven_day_age_floor`; `Ansi_logs_obey_seven_days_and_preview` 167h/169h Execute/DryRun | PENDING |
| PC-7 | Accept missing registration or active owner | `Unregistered_scratch_ownership_never_authorizes_a_clone_removal`; native production-owner method | PENDING |
| PC-8 | Accept dirty or unpublished clone | native production-owner method, dirty/unpublished variants | PENDING |
| PC-9 | Ignore tracked session or holder | native production-owner method; `A_held_ansi_log_survives_even_after_seven_days` | PENDING |
| PC-10 | Use temp root for configured ANSI logs or stored residue | native production-owner method; server configured-root method | PENDING |
| PC-11 | Remove launch-gate exclusion | native production-owner method held gate with absent runtime session | PENDING |
| PC-12 | Accept hidden index flags | native production-owner method assume-unchanged variant | PENDING |

Initial ordinary Review follows completed implementation and ordinary V/R, even
with every PC pending. Restart after caller landing/activation: server and runner;
this Code task performs neither. No deferred-to-Final ID can be marked passed
without its fresh Final receipt.
