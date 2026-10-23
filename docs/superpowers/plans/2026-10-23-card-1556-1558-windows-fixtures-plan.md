# CARD-1556 / CARD-1557 / CARD-1558 fixture repair

Original Code / landing owner: 86740437. Branch `feat/card-task-86740437`.
Base: `877b9ce108e6e2bb4ba81840a99c4dd788302ca4` (origin/master at start).
Read the three live cards and Debug 392d9d33's complete diagnosis from its branch.
Only tests and this plan/report change. Production budgets, validation and assertions
about unknown settings, recovery and delivered branches remain binding.

## Slices

S1 commits portable reproductions before repairs: normalize the real generated
matrix file to CRLF, give the existing notice witness a long real absolute path,
and force windows capabilities on the real Wire.RuntimeSurface backed by
PhoneHomeRuntimeAdapter. The unmodified registration still defaults to linux.
The null platform case retains the adapter's native platform (absence control).
CP-1/CP-2 must fail for the card reasons; controls must execute too.

S2 repairs MatrixLine to remove CR before both byte counting and body comparison;
checks bounded notice omission while requiring every failed/recovered tier in the
delta and complete file; and forwards one capabilities snapshot's platform in
Wire.ReconnectAsync. ParkWireDirectory must report that same platform. All branch
and recovery assertions remain. Commit source and report before final CP-3/CP-4.

## Verification design

The commission explicitly overrides the default Final sweep: NO Unit lane or whole
suite. Run the two affected instructions methods and the full delivery class only.
The unrelated runners-read fault in OrchestratorInstructionsRefreshTests belongs to
CARD-1555 and is outside this selection. No production shared impact is introduced.
One shared test build for the red source group, one shared test build for the final
group. Tool bootstrap is a gated, separately reported build; RC scratch compile is
an explicitly commissioned additional gated build, not an ordinary test selection.
No retries, widened deadlines, skips or deliberate Mutation cycles in Code.

| ID | Guard and ordinary result required |
|---|---|
| V-1 | Four file cases, including all-tier CRLF at the 160 UTF-8 byte boundary; no replacement rune; complete stored body and renderer truncation retain their assertions. |
| V-2 | Four notice cases with long paths: <=700 chars, unknown visible in delivered notice, all affected facts in delta and complete file, no false unset/policy, stable failure deduplicated and recovery revision/candidates complete. |
| V-3 | Real wire initial registration and reconnect for absent/native, explicit windows, explicit linux capabilities; descriptor agrees and List succeeds. |
| R-1 | Real HTTP directory still refuses conflicting linux/windows metadata with 409 and no live connection. |
| R-2 | All three C1213 methods: legacy/current recipient, queue gating, persisted mirror/park/settlement and recovery paths. |
| V-4 | Ordered source commit picks onto `5a126f6fc5ae4e16d803b628716b8f0485a37174` apply and the affected test project compiles in a detached scratch worktree. |
| R-3 | Final receipt validator, complete task-range evidence guard and conflict-free merge-tree against freshly fetched origin/master. |

Native Windows W-1 (four file cases), W-2 (four notice cases), W-3 (full delivery
class) are OWED to the desktop lane. Do not run desktop or claim these passed.
Portable witnesses do not discharge native qualification.

PC inventory for SourceLanding Mutation (all pending): PC-1 retain CR in MatrixLine
(all-tier file method); PC-2 lose an unknown/recovered tier in the complete file or
delta (notice method, failed/recovery variants); PC-3 omit registration platform
(explicit windows wire method); PC-4 accept inconsistent metadata (refusal method).
Mutation owns exact mutant sites, red/restore/green and missing-control discovery.
No PCs executed here. Restart: none.

Use `run --plan <this-path> --rows CP-1,CP-2` after S1 and `--rows CP-3,CP-4`
after S2, always `--expected-source-sha <committed-HEAD> --serial --row-timeout 8m
--total-timeout 16m --max-wait 50s`; continue wait until exit !=75. Estimated cost
is provisional (cold build ~3m, targeted fixture execution ~1-4m); record actuals.
Before each build inventory and remove only task-owned outputs from earlier runs.
Final `validate-checkpoint-receipt.ps1` must pass for both green rows at final tip.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1556-red/` | instructions-red | `/*/*/OrchestratorInstructionsRefreshTests/A_failed_chain_read*` | V-1,V-2 red witnesses | 8 executed; all-tier file fails at 161 bytes; notice fails at missing clipped transitions; 0 skipped | 8 | 4 |
| CP-2 | S1 | CP-1 | wire-red | `/*/*/DelegateBranchDeliveryTests/C1558_*` | V-3,R-1 red/control | 4 executed; windows wire fails HTTP 409; native/linux and mismatch control pass | 4 | 1 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1556-final/` | instructions-final | `/*/*/OrchestratorInstructionsRefreshTests/A_failed_chain_read*` | V-1,V-2 | all 8 passed, 0 failed/skipped | 8 | 4 |
| CP-4 | S2 | CP-3 | delivery-final | `/*/*/DelegateBranchDeliveryTests/*` | V-3,R-1,R-2 | all 7 passed, 0 failed/skipped | 7 | 4 |
