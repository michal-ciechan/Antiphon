# CARD-0959 PATH isolation and dispatch repair

Task ef329fb0 is the new landing owner, based on
`d45a7915d2c9fa718363db0b0faeb522b08b95e3` (prior Code task bd02f8d9).
The original plan remains
`docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md`.

The caller refined the test-only repair to also remove the accidental Codex
authentication refusal before dispatch claim. Create/retry admission and the
runner's cold-launch backstop remain required. The new regression observes a
real framed signed-out response and requires a persisted claim, matching warm
session, reuse event, queued brief, and zero cold launches.

### Checkpoints

This additive manifest covers the refinement. CP-9 first runs red against the
unchanged dispatcher, then CP-10/11/5/8 run after the repair. CP-5 and CP-8
retain the original filters/counts and reuse CP-10's same-source isolated build.
Original-plan CP-4 passed at the PATH repair SHA; a post-auth-repair rerun is
deferred if it cannot fit the caller's 45-minute timebox.
All rows run serially with committed expected source SHA. No full assembly run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-9 | auth-test | `tests/Antiphon.Tests -> bin-c959-auth-red/` | signed-out-warm-red | `/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session` | V-27 | 1 result; expected assertion failure before repair | 1 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | auth-fix | `tests/Antiphon.Tests -> bin-c959-auth-green/` | full-phone-home-projection | `/*/*/PhoneHomeTaskDispatchProjectionTests/*` | V-27,R-6 | 8 results, 0 failed/skipped | 8 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | auth-fix | `tests/Antiphon.SessionRunner.Tests -> bin-c959-auth-backstop/` | runner-auth-backstop | `/*/*/CodexProviderAuthRoutingTests/*` | R-7 | full class, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | auth-fix | CP-10 | portable-existing-refusals | `/*/*/(CodexPhoneHomeCreateTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)/*` | R-1 | 22 expanded executions, full classes, 0 failed/skipped | 22 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | auth-fix | CP-10 | portable-final-unit | `/*/*/*/*[Category=Unit]` | R-5 | >=3961 executed, 0 failed, only the 33 recorded Windows exclusions; jq cases execute | 3961 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

V-27 guards warm claim/reuse despite definite signed-out Codex evidence.
R-6 covers every existing phone-home dispatch projection case. R-7 preserves
the real composed runner authentication routing and cold-launch refusal.
The original 192 PCs and all variants remain pending SourceLanding Mutation;
PC-256 is also pending: restore the removed pre-claim invocation and its helper's
internal visibility together, then run only
`PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`.
The claim-status assertion must fail with the signed-out refusal; restore and
run the same method green. No deliberate mutant was run in Code.

Verification results and the PATH audit will be recorded after execution.
