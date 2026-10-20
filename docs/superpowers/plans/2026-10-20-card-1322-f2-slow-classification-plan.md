# CARD-1322 F2: slow-test classification

Code task `08e19b90-928c-406a-9e84-c8e8ca8887c1`; original landing owner
`7f45965a-8226-4e8a-bec7-e70ba7c2364d`. Base:
`0c5c3ec3d03c5d10ba927f856733c5c79b1775c3`.

Apply only the Slow category and the CARD-1322 allowlist entry from
`81b6e30068789a46af0a7a6faf7bafd122f063b3`. Keep Integration. This is a test-only,
master-only change. The explicit F2 brief excludes the whole Unit lane and all
F1 re-arm changes, tests and plan amendments.

## Verification

Run both rows once against committed HEAD through the checkpoint tool, with
`--expected-source-sha <HEAD> --row-timeout 40m`. Bootstrap the tool through
`scripts/build-slot.ps1`; this required tool build is the only additional build.
Await terminal completion, inspect fresh TRX identities and counts, and validate
the source/build receipt. On receipt refusal remove only this run's owned leftover
output and retry once, as authorized by the brief.

R-6 is the existing compiled classification registry guard. V-3 and R-4 are the
existing caller regression methods, unchanged from master. The full affected
class has two methods. Run `scripts/test-duration-tripwire.ps1 -Trx <CP-14 TRX>`
once as manual acceptance: require both class identities to resolve and zero
unlisted slow tests. No new test or assertion is introduced. No other ordinary
V/R selection is assigned to F2.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-12 | F2 | `tests/Antiphon.Tests -> bin-c1322-f2-08e19b90/` | unit-registry | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | R-6 | all listed, 0 failed/skipped | 1 | 6 |
| CP-14 | F2 | CP-12 | integration-caller | `/*/*/AgentSessionCancelledTurnTests/*` | V-3, R-4 | both existing methods, 0 failed/skipped | 2 | 2 |

## Cost and handoff

Estimated ordinary execution is eight minutes plus the tool bootstrap and host
slot wait, within the 30-minute task budget. No full assembly run is authorized.
There are zero F2 PC rows; SourceLanding Mutation still owns missing-control
discovery. Initial ordinary Review is next. Restart: none.
