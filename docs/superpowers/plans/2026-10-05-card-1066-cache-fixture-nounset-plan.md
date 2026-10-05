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
- R-1: existing `C849_Seed_refuses_invalid_donors_and_partial_payloads`, then the
  full `RemoteScriptContractTests` class, preserve adjacent script contracts.
- R-2: whole Unit lane, required by the stage Final profile. No production
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

Ordinary floor: 17 minutes, plus authoring within the brief's 30-minute budget.
One initial baseline red run and one fixed run; at most two repair rounds.
Checkpoint tool bootstrap is an explained unlisted build under the slot gate.
No repeated proof is needed after green. All rows use committed expected SHA.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1066/` | nounset-regressions | `/*/*/RemoteScriptContractTests/(C1066*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads)` | V-1, V-2, V-3, R-1 | 12 executed, 0 failed/skipped after S2 | 12 | 3 | true |
| CP-2 | all | CP-1 | remote-contracts | `/*/*/RemoteScriptContractTests/*` | R-1 | all methods, 0 failed/skipped | 12 | 3 | true |
| CP-3 | all | CP-1 | unit-final | `/*/*/*/*[Category=Unit]` | R-2 | all Unit cases, 0 failed/skipped | 1 | 10 | true |
| CP-4 | all | n/a | bash-syntax | `bash -n scripts/c590-remote.sh` | R-3 | exit 0 | n/a | 1 | true |
