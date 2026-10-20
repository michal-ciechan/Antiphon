# CARD-1380 S2 unbound mutation candidate

Original Code and landing owner: `efc5dea7-0623-43b6-b660-5851fe4cf6ef`.
Branch: `feat/card-task-efc5dea7`. Target: master. Restart: none.
Plan: [faster lands](../plans/2026-10-20-card-1380-faster-lands-plan.md).
No publication operation or landed SHA is bound yet. Ordinary Review precedes
the caller landing this Code task and commissioning SourceLanding Mutation.

Both plan controls remain pending; ordinary green and nightly cannot discharge
them. Mutation owns deliberate mutants, missing-control discovery and exact
red/restore/green execution, using only inherited children of the bound snapshot
and its assigned external evidence root. Never commit or push from that snapshot.

| ID | Deliberate defect | Exact method filter | Required failure |
|---|---|---|---|
| PC-1 | Make `LandVerificationPolicy` skip the `.cs` candidate in the production protocol | `/*/*/LandNonCodeSkipTests/C1380_CSharpChangeBuilds*` | Missing verifier invocation; expected one call, actual zero |
| PC-2 | Bypass `Require(verification.Passed, "verification_failed")` in `AgentTaskLandingProtocol` | `/*/*/LandNonCodeSkipTests/C1380_FailedBuildRefusesPublication*` | Publication/refusal or verification-passed assertion fails; target must have stayed unchanged in the restored run |

Run only the named method for each cycle through the host build-slot gate.
Preserve nonzero execution counts and assertion failures; fixture/build failure
or zero tests is not a red control. Restore and run the identical method green.
No timeout or assertion relaxation. Additional graph/diff/override and persisted
receipt controls may be discovered by Mutation; none are claimed executed here.

CP-3's pre-fix documentation witness is ordinary defect evidence, separate from
these two pending controls. Post-activation timing acceptance remains pending
the operator's later RC go and real workload observations.
