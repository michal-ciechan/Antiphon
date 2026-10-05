# CARD-1054 Code evidence

Implemented actual-child jq PATH admission; all eighteen required ordinary
results are green across the recorded source-qualified rows. Ready for separate
Review. All thirteen positive controls remain pending post-land Mutation.

Original Code task / landing owner: `dfea030e-aa75-457a-a38a-9ad246e19cf3`.
Branch: `feat/card-task-dfea030e`.
Worktree: `/work/worktrees/task-dfea030e`.
Task base: `ef017c651193784c90c48639e761cb9aab9e16bb`.
Plan: [frozen verification](../docs/superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md).
Final publication SHA is in the task Result/progress marker; the later S2 commit
contains documentation/evidence only and is not relabelled as a tested SHA.

## Implementation and source slices

- `8a9a785260e04ec11530f22299867489920dd755`: probe and fixture implementation,
  committed/pushed before verification. Initial checkpoint build failed at two
  new Shouldly calls; zero tests executed.
- `f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9`: repair 1 supplies explicit
  `Case.Sensitive` arguments. CP-1: 4 executed, 3 passed, 1 failed, 0 skipped.
  CP-2: 14 executed, 14 passed, 0 failed/skipped.
- `3422aad09bb78783353ddaaa153a48125a9f6a46`: repair 2 corrects the exact
  nonexecutable-leaf fixture expectation and documents the measured Bash
  behavior in the plan. CP-1: 4 executed, 4 passed, 0 failed/skipped.
  The production probe, shared fixture, version method and source contracts
  are byte-unchanged from CP-2's tested source. CP-2 was not repeated after green.

The probe clears command hashing, observes real `command -v jq` and `readlink -f`,
requires absolute executable-file lookup and the regular executable non-symlink
canonical destination, then executes the resolved target. Found/resolved
observations use Bash `%q` with `LC_ALL=C`; path refusals never invoke jq.
The uid, version, exit/stderr and shared result functions remain unchanged.

The four new native Linux tests copy the whole probe and count exactly three
substitutions: fixed jq destination, fixed probe home, and the jq arm's test uid.
Real private files, native tools, symlinks, same-shell command cache and an
independent NUL-delimited invocation log establish actual behavior. Identical
home/canonical bytes and direct healthy execution precede the shadow refusal.
The twelve version vectors each require actual canonical invocation, so path
refusals cannot mask their version/exit/stderr assertions. No test skip, timeout
increase, retry, admission weakening or assertion removal was introduced.

### Measured correction to frozen fixture design

The initial plan said a sole nonexecutable canonical leaf was invisible to Bash
and required `JqNotFound`/unavailable lookup. CP-1's real Bash child at `f25bea3c`
returned its actual canonical lookup and the probe correctly refused with
`JqLookupInvalid`. The fresh TRX locates this at the final nonexecutable vector,
after the dangling, self-loop and directory cases had passed. This is NEW test
setup/design red; the method does not exist at the task base, and no inherited
failure is claimed.

The correction follows the already frozen first-failed-boundary grammar:
nonexecutable found text must be retained and rejected before resolution.
The test now asserts exactly `JqLookupInvalid`, actual canonical lookup,
unavailable resolution, exit 1, one row and zero invocations. It accepts no
alternative receipt. The production predicate, grammar, CP roster and all
thirteen mutation seams are unchanged. Review should explicitly assess this
documented correction. Two repair rounds used; no deliberate mutant executed.

## Ordinary results and provenance

The brief's explicit bounded Final profile overrides its generic whole-Unit
boilerplate: seven unique methods, eighteen TUnit results, no additional affected
integration class. No whole Unit, namespace, assembly, image build, provider
qualification, host installer or CARD-1040 consumer battery was run.

| ID | Outcome and exact ordinary evidence |
|---|---|
| V-1 | PASS, CP-1 `C1054_Jq_row_rejects_home_shadow`: present/absent home shadows, missing discovery, relative lookup, missing readlink and stale hash refuse without invocation. |
| R-1 | PASS, same method; exact exit/reason/paths and zero trace guard canonical-present shadowing and each refusal boundary. |
| V-2 | PASS, CP-1 `C1054_Jq_row_accepts_canonical_file_and_alias`: direct, alias and nonexecutable-earlier fallback succeed. |
| R-2 | PASS, same method; traced executable is resolved canonical, argc/argv/HOME are exact. |
| V-3 | PASS, CP-1 `C1054_Jq_row_rejects_canonical_leaf_symlink`: live home target behind canonical or alias refuses; dangling, loop, directory and nonexecutable leaves refuse. |
| R-3 | PASS, same method; exact refusal fields and no invocation, with the measured nonexecutable expectation correction above. |
| V-4 | PASS, CP-1 `C1054_Jq_row_records_found_and_resolved_paths`: independent literal expected encodings for newline, space, tab, CR, quote and backslash in both fields. |
| R-4 | PASS, same method; one physical receipt line and exact found/resolved/sentinel evidence. |
| V-5 | PASS, CP-2 twelve `Version_row_accepts_only_exact_successful_pin_without_stderr` results plus `Pinned_download_is_verified_before_root_owned_install_in_every_runner_target` and `Qualification_grades_the_jq_row_for_both_targets`. |
| R-5 | PASS, same fourteen results; every version case invokes canonical, and original pin/install/wrapper assertions remain unchanged. |

Fresh TRX inspection confirmed the exact four literal CP-1 methods, each once,
and CP-2's two source methods plus all twelve original version arguments. All
eighteen latest required outcomes are Passed, zero Failed/Skipped. CP-1 and CP-2
do not share a final tested SHA: the exact tested SHAs above are intentional.

Receipt validators both exited 0:

```text
CHECKPOINT SOURCE VALID source=3422aad09bb78783353ddaaa153a48125a9f6a46 rows=1
CHECKPOINT SOURCE VALID source=f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9 rows=1
```

The passing rows have `dirty=0`, `sourceState=clean`, `buildSource=verified`,
`slot=granted`, `waited=0s`. Their structured source snapshots bind the actual
build and tests to those committed SHAs; source stayed frozen for every run.
CP-1 passing build: 109.5343891 seconds, host wall 4.3416302 seconds, run wall
115 seconds rounded. CP-2's producing build: 151.0691755 seconds; CP-2 host wall
4.4763626 seconds. No parallel method-duration sum is presented as wall time.

Runs: `20261005-094339-02c8` (compile red), `20261005-094550-a02f` (CP-1 red,
CP-2 green), `20261005-095012-b753` (CP-1 green). CP-1 had two repair reruns,
CP-2 one build-failure repair rerun; no automatic retries or post-green repeat.
The tool's unedited lines below omit aggregate rerun counts, so those counts
are stated here without rewriting emitted evidence.

The only build outside the table was its expressly authorized checkpoint-tool
bootstrap, through `scripts/build-slot.ps1 -Label c1054-tool`, output
`bin-c1054-tool/`, `UseAppHost=false`: exit 0, slot granted, waited 0 seconds,
held 5 seconds, 0 errors and 1 existing nullable warning. Each table build also
had a granted slot and zero wait. The first build failure had 2 new CS1503 errors;
its test rows never acquired a slot or ran. No unlisted test was run.
`bash -n` and `git diff --check` are static validation, not extra TUnit groups.

## S2 and remaining ownership

The base already contained CARD-1025's helper/wrapper/canonical-leaf repair and
the updated CARD-1040 row grammar. Both landed host commits (`dc7d1794b`,
`fc3b3e1e8`) are ancestors. Host implementation, wrapper, Dockerfile and host
tests remain untouched. S2 adds this implementation/receipt handoff to the two
existing qualification owners. The CARD-1040 Checkpoints-and-Cost section,
fifteen names, three rows, 18-minute floor and image/activation/uid/digest/owner/
mode requirements are preserved. Historical S1 evidence remains untouched.

The platform front doors were read at 09:38 UTC on 2026-10-05 through configured
`ANTIPHON_API`: defaults revision 2, eligible Linux and Windows descriptors,
one unavailable draining descriptor. No fleet location was embedded and no
runner setting changed. Fixtures qualify native Bash behavior only; they do
not qualify real image ownership, uid 1654, an active container or delivery.
No new asynchronous producer/destination/persistence/lease path exists.

Required manual/static work: syntax, edited-link resolution, preservation of
the CARD-1040 checkpoint/cost section and `git diff --check`; the final task
Result reports the full-base-to-final-HEAD evidence history guard. All owned
checkpoint children were awaited to terminal. Failed-run alternate outputs were
cleaned through the tool and the green run removed its outputs; bootstrap
outputs are removed before settlement. Generated TRX/JSON/logs remain ignored.

Restart: **none**. Activation/restart ownership remains the caller's CARD-1040
S2 / canonical rollout owner. No deployment or installation occurred.
CARD-1058 still owns canonical-directory hardlinks and writer check-to-use swaps.

Pending Mutation controls (none discharged by ordinary green):

| ID | Exact method in `JqRunnerImageContractTests` | Status |
|---|---|---|
| PC-1 | `C1054_Jq_row_rejects_home_shadow` | PENDING |
| PC-2 | `C1054_Jq_row_accepts_canonical_file_and_alias` | PENDING |
| PC-3 | `C1054_Jq_row_rejects_canonical_leaf_symlink` | PENDING |
| PC-4 | `C1054_Jq_row_records_found_and_resolved_paths` | PENDING |
| PC-5 | `C1054_Jq_row_records_found_and_resolved_paths` | PENDING |
| PC-6 | `C1054_Jq_row_records_found_and_resolved_paths` | PENDING |
| PC-7 | `C1054_Jq_row_rejects_home_shadow` | PENDING |
| PC-8 | `C1054_Jq_row_rejects_home_shadow` | PENDING |
| PC-9 | `C1054_Jq_row_rejects_home_shadow` | PENDING |
| PC-10 | `C1054_Jq_row_rejects_home_shadow` | PENDING |
| PC-11 | `C1054_Jq_row_accepts_canonical_file_and_alias` | PENDING |
| PC-12 | `C1054_Jq_row_rejects_home_shadow` | PENDING |
| PC-13 | `C1054_Jq_row_records_found_and_resolved_paths` | PENDING |

Every baseline/red/restored-green phase must use the corresponding exact
`/*/*/JqRunnerImageContractTests/<method>` filter after separate Review and the
caller landing the original Code task. No further ordinary V/R ID is deferred.

## Reproduction and unedited checkpoints

Bootstrap as in the plan, then use `run --plan
docs/superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md --after S1
--serial --expected-source-sha <committed-head> --max-wait 50s`; on exit 75,
`wait --run <id> --max-wait 50s` until terminal. The second repair used
`--rows CP-1` instead of `--after S1`. This is a rerun recipe, not a request to
repeat already green ordinary selections.

The following lines are copied unchanged from each run's structured report.

Run: 20261005-094339-02c8

```text
CHECKPOINT CP-1 commit=8a9a785260e04ec11530f22299867489920dd755 build=failed filter=/*/*/JqRunnerImageContractTests/(C1054_Jq_row_rejects_home_shadow*)|(C1054_Jq_row_accepts_canonical_file_and_alias*)|(C1054_Jq_row_rejects_canonical_leaf_symlink*)|(C1054_Jq_row_records_found_and_resolved_paths*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a9a785260e04ec11530f22299867489920dd755 sourceState=clean buildSource=unknown
CHECKPOINT CP-2 commit=8a9a785260e04ec11530f22299867489920dd755 build=failed filter=/*/*/JqRunnerImageContractTests/(Version_row_accepts_only_exact_successful_pin_without_stderr*)|(Pinned_download_is_verified_before_root_owned_install_in_every_runner_target*)|(Qualification_grades_the_jq_row_for_both_targets*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a9a785260e04ec11530f22299867489920dd755 sourceState=clean buildSource=unknown
```

Run: 20261005-094550-a02f

```text
CHECKPOINT CP-1 commit=f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9 build=ok filter=/*/*/JqRunnerImageContractTests/(C1054_Jq_row_rejects_home_shadow*)|(C1054_Jq_row_accepts_canonical_file_and_alias*)|(C1054_Jq_row_rejects_canonical_leaf_symlink*)|(C1054_Jq_row_records_found_and_resolved_paths*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-dfea030e/.antiphon/checkpoints/20261005-094550-a02f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9 build=reused filter=/*/*/JqRunnerImageContractTests/(Version_row_accepts_only_exact_successful_pin_without_stderr*)|(Pinned_download_is_verified_before_root_owned_install_in_every_runner_target*)|(Qualification_grades_the_jq_row_for_both_targets*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-dfea030e/.antiphon/checkpoints/20261005-094550-a02f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=f25bea3cdf7d1f50719a1cbb299cdffb570ee6a9 sourceState=clean buildSource=verified
```

Run: 20261005-095012-b753

```text
CHECKPOINT CP-1 commit=3422aad09bb78783353ddaaa153a48125a9f6a46 build=ok filter=/*/*/JqRunnerImageContractTests/(C1054_Jq_row_rejects_home_shadow*)|(C1054_Jq_row_accepts_canonical_file_and_alias*)|(C1054_Jq_row_rejects_canonical_leaf_symlink*)|(C1054_Jq_row_records_found_and_resolved_paths*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-dfea030e/.antiphon/checkpoints/20261005-095012-b753/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3422aad09bb78783353ddaaa153a48125a9f6a46 sourceState=clean buildSource=verified
```
