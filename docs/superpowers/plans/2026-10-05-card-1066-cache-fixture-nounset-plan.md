# CARD-1066 cache fixture nounset repair

Code task / landing owner: d73d91aa. Base: `0de12dac930ad52a235604c566ee643e71b21daa`.

## Implementation

S1 adds direct regression cases to `RemoteScriptContractTests`. Run CP-1 against
the unchanged production script first: its 11 new argument-expanded cases must
fail while the existing donor-validation guard passes. This is baseline defect
reproduction, not a deliberate Mutation cycle.

S2 splits each variable into its own `local` declaration in
`c849_fixture_tree_fault`, `c849_fixture_npm_install`, and
`c849_fixture_warm_probe`. Audit every `local` declaration in the entire script
for later initializers reading earlier names on the same statement.

## Verification design

- V-1: `C1066_Fixture_tree_fault_reaches_validator_under_nounset` calls the real
  function and real validator with host, metadata, reference, symlink, hardlink,
  and special. Assert exact exit 2 and existing diagnosis, and retain both
  original donor metadata files. A valid donor is checked first.
- V-2: `C1066_Fixture_npm_install_uses_argument_log_under_nounset` calls the real
  function for success and offline miss, substitutes only Docker, and asserts
  exact exit/diagnosis and the argument-derived log's contents.
- V-3: `C1066_Fixture_warm_probe_uses_argument_log_under_nounset` calls the real
  function for packages, scratch, and home; substitutes only Docker and asserts
  exact exit/diagnosis and the argument-derived log's contents.
- R-1: existing `C849_Seed_refuses_invalid_donors_and_partial_payloads` plus the
  three exact C1066 methods above preserve the affected script contracts.
  Continuation task 7cc255f0 explicitly replaces the full class with this bounded
  selection. These are the only tests directly calling the three changed helpers;
  the existing donor case exercises their real tree validator's rejection and
  donor-preservation boundaries. CP-2 names all four methods explicitly (12
  argument-expanded results); CP-1 retains the original regression selection.
  The overlap is intentional to execute both requested checkpoint rows, not an
  additional repeat-proof loop. Unrelated deployment, archive, recycle, and cold
  cache cases are outside these declaration-only changes.
  `Nested_lane_never_uses_sudo_or_python` is excluded as inherited: one exact
  method failed at base `0de12dac930ad52a235604c566ee643e71b21daa`, documented in
  `.antiphon/task-d73d91aa.md`. Backlog CARD-1067 owns its c1008 sudo allowlist
  failure; it is not repaired here.
- R-2: whole Unit lane remains deferred, not passed. The continuation brief
  explicitly prohibits a whole-Unit run and permits CP-3 deferral if unbounded.
  Its original ten-minute estimate is not a demonstrated bound: it includes the
  full script-contract class, whose predecessor run exceeded 13 minutes before
  the shared 15-minute deadline with no final TRX. Budget at least that observed
  lower bound plus all remaining Unit classes and qualification overhead; the
  actual whole-Unit cost is unknown and cannot fit a justified ten-minute claim.
  This continuation does not claim Final/Full profile completion. No production
  delivery, landing, lease or persistence code changes; no affected integration
  class outside the named script class, and no full assembly run.
- R-3: `bash -n scripts/c590-remote.sh` and full-file declaration audit.
- V-4 (post-land operational acceptance): trusted desktop owner runs
  `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <reviewed-landed-sha>`.
  Expect groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47,
  inventories=2 failures=0 productionMutations=0. The assigned runner mirror
  cannot perform the documented trusted desktop gate before Review/landing.
  Never run deployment, rollout phases, Reset, Prune, or Seed in this task.

### Mutation handoff

All controls remain pending SourceLanding Mutation. PC-1 restores the combined
tree declaration (six V-1 variants); PC-2 restores the npm declaration (two V-2
variants); PC-3 restores the warm declaration (three V-3 variants). Each cycle
must select the exact corresponding method. Existing operational Fixture
PC-01..PC-32 / 47 variants also remain pending live acceptance; local ordinary
green does not discharge them. Mutation owns deliberate mutants and missing
control discovery.

### Cost

Continuation ordinary floor: 7 minutes for CP-1, CP-2, CP-4, plus authoring within
the brief's 30-minute budget. CP-3's retained ten-minute historical estimate is
superseded by the unbounded-cost assessment in R-2 and is not in this run.
The predecessor already completed baseline-red proof; do not repeat it.
At most two repair rounds; no assertions or timeouts may be loosened.
Checkpoint tool bootstrap is an explained unlisted build under the slot gate.
No repeated proof is needed after green. All rows use committed expected SHA.

### Continuation receipt production

Commit this selection before running; keep tracked source frozen through receipt
validation. Run the checkpoint tool once with `--rows CP-1,CP-2,CP-4`,
`--expected-source-sha <full HEAD>`, and the existing 15-minute total deadline.
Do not include deferred CP-3 in the report's selected rows. This prevents the
previous `row_heading_disagreement`: a timed-out CP-2 and unrun CP-3 made the
aggregate build binding unknown while successful CP-1 remained verified.
Generate the report normally and validate its complete selected scope with both
the tool validator and `scripts/validate-checkpoint-receipt.ps1`; never edit a
receipt or relabel an earlier SHA. Store later facts and unedited CHECKPOINT
lines in ignored `.antiphon/task-7cc255f0.md` to keep the final tested HEAD stable.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1066/` | nounset-regressions | `/*/*/RemoteScriptContractTests/(C1066*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads)` | V-1, V-2, V-3, R-1 | 12 executed, 0 failed/skipped after S2 | 12 | 3 | true |
| CP-2 | all | CP-1 | remote-contracts | `/*/*/RemoteScriptContractTests/(C1066_Fixture_tree_fault_reaches_validator_under_nounset)\|(C1066_Fixture_npm_install_uses_argument_log_under_nounset)\|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads)` | R-1 | all four named methods, 12 executed, 0 failed/skipped | 12 | 3 | true |
| CP-3 | all | CP-1 | unit-final | `/*/*/*/*[Category=Unit]` | R-2 | all Unit cases, 0 failed/skipped | 1 | 10 | true |
| CP-4 | all | n/a | bash-syntax | `bash -n scripts/c590-remote.sh` | R-3 | exit 0 | n/a | 1 | true |
