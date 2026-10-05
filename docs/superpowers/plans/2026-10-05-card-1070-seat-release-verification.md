# CARD-1070 seat release repair verification

Owner: Code task cec70c48-9462-4b67-b32f-3177069f595a. Base:
`d2fe587a55efcee7981aca2188d206d6d1eaadaa`.

The explicit brief limits this Final round to the role guard, unsupported transport
method, and the two full affected seat-release classes. No whole Unit run. Keep
AutomaticEnabled false, no migration, no timeout/assertion relaxation. Budget 40
minutes, at most two repair rounds. No full-assembly or unbounded shared impact.

S1: reproduce inherited failures, bisect S3 history with the exact unsupported
method; use AgentTaskRoles.IsSpecialist for custody. Correct the demonstrated
regression and preserve unsupported/no-force behavior. S2: ordinary verification.

## Verification design

V-1: SpecialistRoleContractTests.no_Check_comparison_survives_outside_the_allowlist
must find no direct Check comparison outside its existing allowlist.
V-2: TerminalRunnerSeatReleaseTests.Unsupported_server_transport_never_falls_back_to_force
must return Unsupported for HTTP and phone-home peers and make zero force calls.
R-1: full server TerminalRunnerSeatReleaseTests preserves durable reservation,
answer delivery, recovery, custody and conservative refusal.
R-2: full runner TerminalSeatReleaseTests preserves generation/evidence fences and
conditional release. Manual M-1: read runner defaults/catalogue. M-2: confirm the
two C1050 Windows audits assert Windows at entry despite inheriting Category Unit;
record platform selection guidance on the card without changing those tests.
M-3: AutomaticEnabled stays false and migration diff is empty.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1070-server/` | role-guard | `/*/*/SpecialistRoleContractTests*/no_Check_comparison_survives_outside_the_allowlist*` | V-1 | all listed, 0 failed | 1 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | unsupported-transport | `/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force*` | V-2 | all listed, 0 failed | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | server-seat-class | `/*/*/TerminalRunnerSeatReleaseTests*/*` | R-1 | all listed, 0 failed | 21 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1070-runner/` | runner-seat-class | `/*/*/TerminalSeatReleaseTests*/*` | R-2 | all listed, 0 failed | 38 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

Ordinary checkpoint estimate 12 minutes. Declared setup: build the checkpoint tool
through scripts/build-slot.ps1 into bin-c1070-tool/ (2 minutes). Before repair run
CP-1/CP-2 as inherited-red diagnostics at committed source. Historical bisect uses
the same CP-2 method in a detached local worktree and checkpoint tool row, one
isolated build per selected SHA (up to 15 minutes); compile failures are skipped,
never classified as test failures. This is the explicit reason for historical
runs outside the final table. Tool bootstrap is the only non-checkpoint build.

PC-1070-1 (specialist predicate bypass), PC-1070-2 (unsupported transport acceptance
or force fallback, HTTP 404/501 and phone-home unsupported variants) remain pending
for post-land SourceLanding Mutation; Mutation owns deliberate mutants and missing
control discovery. Existing CARD-0667 PC-1..PC-104 and all variants remain pending,
not discharged by this repair. Restart: none; caller owns any later activation.
