# CARD-1272: filter and repeat host reports belong to the test sandbox

Code and landing owner: `c660169d-4e9a-45a8-b9d2-ce90aa2b58cb`.
Task base: `e7b6b80378dffe7b2326a8bc46ee5a91d30e9e69` (`git merge-base origin/master HEAD`).

Observed in run 20261014-224348-1b4b (CARD-1267 Code evidence, Runs): a green
`/*/*/RerunFilterHostTests/*` row left `checkpoint-filter-host/TestResults` in the sealed
build output, and cleanup refused `output-ledger-mismatch`. The test launches the staged
filter host with `--treenode-filter` and no `--results-directory`, so the test platform
writes its report beside the DLL. CARD-1264 (`c551d7c6d`) fixed the same defect for the
lifecycle host by passing `--results-directory <owned sandbox>/TestResults`.

## Implementation

S1 commits the failing report-location assertions with the unchanged filter-host launch.
`RerunFilterHostTests` takes a `CheckpointTestScope` sandbox per child run, sets
TMPDIR/TEMP/TMP to it, registers the child, requires each sandbox's `TestResults`
(one HTML report for the matching run, none for the zero-match run, as a probe
disclosed in the evidence observed), requires `checkpoint-filter-host/TestResults` to be
absent, and requires the sandboxes to be reclaimed on disposal. `CheckpointRepeatHostTests` already
passes `--results-directory <sandbox>`; S1 adds the same temp environment, child
registration and beside-the-DLL absence assertion.
S2 adds `--results-directory <sandbox>/TestResults` to the filter-host launch.
Cleanup refusal, the output ledger and CARD-1242 seal-time adoption stay unchanged.

Scope: `tests/Antiphon.Tests/Checkpoints/RerunFilterHostTests.cs`,
`tests/Antiphon.Tests/Checkpoints/CheckpointRepeatHostTests.cs`, this plan and the
CARD-1264 paragraph of `docs/testing-and-build.md`. No production code, census, service,
database, delivery, landing or lease change. Restart: none.

## Verification design

- V-1: `RerunFilterHostTests.parenthesized_class_filter_runs_both_methods_on_a_real_host`
  keeps its filter and count assertions, and adds the sandbox report, beside-the-DLL
  absence and reclaim assertions above. Red at S1 (CP-1): the report is beside the DLL.
- V-2: `CheckpointRepeatHostTests` (3 methods) asserts after each host run that
  `checkpoint-repeat-host/TestResults` is absent. Its results-directory argument predates
  this task, so S1 cannot turn it red; that is a disclosure, and the omission variant is
  a pending PC.
- V-3: manual acceptance of the S2 checkpoint run without `--keep-outputs`: exit 0 GREEN,
  `outputs: deleted`, empty cleanup refusals, and both receipt validators accept the
  exact tested SHA (`scripts/validate-checkpoint-receipt.ps1` and the tool's `validate`).
- R-1: `CheckpointNamespaceCensusUsageTests` (CP-3) keeps the compiled census aligned;
  this task adds no test method.

### Positive controls (pending SourceLanding Mutation)

| PC | Variant | Method-scoped detecting filter |
|---|---|---|
| PC-1 | Omit the filter-host `--results-directory` argument | `/*/*/RerunFilterHostTests/parenthesized_class_filter_runs_both_methods_on_a_real_host` |
| PC-2 | Point the filter-host results directory beside the staged DLL | `/*/*/RerunFilterHostTests/parenthesized_class_filter_runs_both_methods_on_a_real_host` |
| PC-3 | Omit the repeat-host `--results-directory` argument | `/*/*/CheckpointRepeatHostTests/native_repeat_reports_five_sets_in_one_process_and_lifecycle` |

Code performs no deliberate mutants.

### Cost

One isolated test build plus three short rows per run; a red S1 run and a green S2 run,
within the 90-minute task budget. No whole-Unit lane: the brief excludes it; the change
is confined to two test fixtures' child launches. Bootstrap the checkpoint tool once
through `scripts/build-slot.ps1` into `bin-c1272-tool/` (the planned unlisted build).
Import this table to an ignored manifest with `timeouts.rowMinutes: 40`; run with
`--total-timeout 150m`, the exact committed `--expected-source-sha`, no `--keep-outputs`,
and wait until the exit is not 75. The S1 red run keeps its outputs; remove only that
run's literal output root after inspection. Each proof selection runs at most three
normal times; none after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1272-proof/` | rerun-host | `/*/*/RerunFilterHostTests/*` | V-1, V-3 | 1 test; S1 1 failed; S2 1 passed | 1 | 10 | false | n/a |
| CP-2 | S1 | CP-1 | repeat-host | `/*/*/CheckpointRepeatHostTests/*` | V-2, V-3 | 3 passed | 3 | 3 | false | n/a |
| CP-3 | S1 | CP-1 | census | `/*/*/CheckpointNamespaceCensusUsageTests/*` | R-1 | 1 passed | 1 | 2 | false | n/a |
