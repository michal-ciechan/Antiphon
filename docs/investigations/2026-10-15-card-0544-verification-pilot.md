# CARD-0544 portable Interim preflight

Status: **PILOT NOT RUN; live qualification pending.** Original Code/landing owner:
`18880da1-ceca-4132-b085-75d512bed1b8` (CARD-1281 D-5). Source coordinates and run
provenance are in the [nightly record](2026-10-15-card-0487-nightly-qualification.md).

CP-9 and CP-10 cover V-21/R-4 with
`InterimVerificationPolicyTests.C544_BaselineCompletion` and
`InterimVerificationLandGuardTests.C544_FinalFullRequired`. Completed Found
Final/Full can provide baseline coverage; it never grants landing approval.
Incomplete/failed/canceled/queued/working baselines refuse. A latched owner needs
usable exact-source Final/Full Review; Unknown/None/legacy scope cannot land.

CARD-1281 Q-3 remains pending after Q-1/Q-2: a real qualified Interim repair
admission, lost-readiness refusal, retained owner latch, restored Final/Full at
exact source covering every deferred case, confirmed land, measured costs and
rollback to FullOnly/disabled. Live owner/source/selection identities must first be
bound; budget 90 minutes plus that source's ordinary scope. No policy or settings
are changed. No deferred check is reported as passed. Measured savings: zero.

PC-46 (baseline completion) and PC-47 (latched Final/Full land), including every
scope variant, remain **pending** for method-scoped SourceLanding Mutation.
All inherited CARD-0544 controls remain pending under their original IDs.
The unbound candidate location is `docs/superpowers/mutations/`; no pack or
SourceLanding operation has been commissioned here. Restart: none.

## Final verification supplement

Reason for these unlisted D runs: the standing Code-stage Final contract requires
whole Unit and full named integration classes. The portable preflight brief asks
for only CP-7..CP-11; this supplement records the additional higher-priority
requirement transparently. It does not alter that closed manifest or the live gates.
Estimated additional cost: 16 minutes, including one isolated build. No assembly
sweep is needed: all impact is bounded to documentation; the five reused classes
exercise the existing guards whose results this record describes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-14 | S5-final | `tests/Antiphon.Tests -> bin-c1281-d-final/` | qualification-final-unit | `/*/*/*/*[Category=Unit]` | Final Unit | Full Unit roster, zero failed/skipped | 1 | 7 |
| CP-15 | S5-final | CP-14 | qualification-final-classes | `/*/*/(ReleaseGateActivationTests*)\|(ReleaseGateRunTests*)\|(InterimVerificationPolicyTests*)\|(InterimVerificationLandGuardTests*)/*` | V-19,V-20,V-21,R-4 | Four complete classes, all variants, zero failed/skipped | 47 | 7 |
| CP-16 | S5-final | CP-14 | qualification-final-watchdog | `/*/*/NightlyWatchdogCoreTests/*` | R-4 | Complete watchdog core class, all variants, zero failed/skipped | 3 | 2 |

Run this supplement separately with the checkpoint tool, `--after S5-final`,
`--serial` and the exact committed `--expected-source-sha`. Await terminal exit,
inspect fresh TRX identities/counts, qualify receipts, and retain actual SHA facts.
