# C1375 fixture census repair

Original Code task / landing owner: `692422db-7fe6-4451-b7f9-0c98274d5500`.
Base: `27e9949274d46ea90db6e4d752687ac4fc4fc6fe`. Target: master only.

The RC preflight enumerates every `IsTestProject=true` project before launching
any workload. `scripts/fixtures/nightly/c1375-probe/Probe.csproj` is absent from
the policy. Its intentionally failing timeout/cancel producer supplies captured
inputs to `NightlyExecutionDiagnosticTests.D13_RealTimeoutCancelled` through
`scripts/test-nightly-tests.ps1`; it is a child fixture, like c487, not a primary
test lane. Register its Antiphon.Tests consumer and recompute the canonical hash.

S1 commits the two exact registry-count updates and a child/consumer assertion
before the policy repair. S2 adds the child disposition and policy hash. No
runtime, suite membership, exclusion, timeout or assertion relaxation is needed.

## Verification design

The explicit brief narrows this Final round to static census plus the exact
policy-registry methods below. No Unit lane, whole class, nightly lane, native
probe execution or RC qualification is authorized or claimed.

- V-1: `nightly-tests.ps1 -Profile rc -WhatIf` reports the unmatched c1375 project
  at the base; after S2 it exits zero before any workload. This validates the
  canonical policy hash, all project dispositions, profile exclusions and suite
  roster. The project census is shared by Windows and Linux and both profiles;
  this Linux preview does not execute or qualify either platform's test lanes.
- V-2: `C1313_ProjectAndCatalogUniverse` requires exactly 12 projects, seven
  primaries and five children, including the exact c1375 child and consumer. It
  also rejects an unclassified project and stale catalog hash. Record its S1 red
  directly through the gated script harness, then its final TUnit green once.
- R-1: `C1323_PlatformPolicy` preserves Windows/Linux exclusion and frozen
  authority contracts with the updated 12-project registry count. Record the
  S1 registry-count red through its gated script harness before the fix.

No other ordinary V/R IDs or deferred Final IDs. Raw evidence is ignored under
`.antiphon/c1375-census-evidence/` and `.antiphon/checkpoints/<run>/`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | n/a | rc-census-preview | `pwsh -NoProfile -File scripts/nightly-tests.ps1 -RepoRoot . -Profile rc -WhatIf` | V-1 | exit 0, nine suites previewed, no test lane launched | n/a | 1 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1375-census/` | project-registry | `/*/*/TestSelectionCatalogTests/C1313_ProjectAndCatalogUniverse*` | V-2 | exactly 1 executed, 0 failed/skipped; 5 harness assertions | 1 | 5 |
| CP-3 | S2 | CP-2 | platform-registry | `/*/*/ReleaseGatePlatformTests/C1323_PlatformPolicy*` | R-1 | exactly 1 executed, 0 failed/skipped; 19 harness assertions | 1 | 1 |

Run the closed list once with committed HEAD as `--expected-source-sha`,
`--row-timeout 40m`, and `--serial`; await completion and validate fresh TRX and
source receipts. The tool bootstrap is an explained setup build, under the host
slot gate into `bin-c1375-census-tool/`. Initial red static/script checks are
the brief's requested defect reproduction, also under the slot gate.

### Mutation handoff

Unbound candidate: `docs/superpowers/mutations/task-692422db-c1375-census.md`.
PC-1 (remove c1375 disposition and recompute hash) and PC-2 (replace its consumer
with another valid primary) remain pending for method-scoped SourceLanding
Mutation against `C1313_ProjectAndCatalogUniverse`. Code performs no deliberate
mutants. Review is next; caller lands the original Code task, then commissions
SourceLanding Mutation. Restart: none.

### Cost

Ordinary checkpoint estimate: seven minutes, one test-project build, two TUnit
methods and one static command. Setup and red reproduction are additional.
The brief's task budget is 30 minutes; the 40-minute row cap is supplied by it.
