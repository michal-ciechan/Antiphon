# CARD-1724 unbound Mutation candidate

Original Code / landing owner: `d933f966-15f7-4f30-bac2-1b5c6b21eed9`.
Plan: [E2E artifact paths](../plans/2026-11-05-card-1724-e2e-artifact-paths.md).
Implementation commit: `d58669821823ce5772f6b6ce4562faf27f299adc`.
Restart: none. No publication operation or landed source is bound yet.

All PC-1 variants remain pending. Ordinary verification and Review precede caller
landing of the original Code task and a separately commissioned SourceLanding
Mutation. Code's S1 ordinary red witness does not discharge a deliberate PC cycle.

Project: `tests/Antiphon.Tests`. Exact detecting method for every variant:
`/*/*/E2EArtifactPathGuardTests/E2E_writers_do_not_target_repository_docs`.

| PC / variant | Compiling defect | Expected assertion red | Status |
|---|---|---|---|
| PC-1 / Agent | Replace PlaywrightFixture.ScreenshotRoot in AgentE2ETests with AntiphonAppFixture.FindRepositoryRoot(), "docs", "screenshots" | Guard names AgentE2ETests.cs | Pending |
| PC-1 / Board | Make that replacement for one BoardE2ETests screenshot | Guard names BoardE2ETests.cs | Pending |
| PC-1 / canary | Restore the original FindRepositoryRoot()/docs/investigations acceptance path in OutputDistillationApplyCanaryTests | Guard names OutputDistillationApplyCanaryTests.cs | Pending |

The screenshot replacement uses an existing root helper so it compiles; restoring
the base FindRepoRoot() spelling also requires its removed local helper. Each
variant is independent and restored before the next; these are one guard's three
source variants, not concurrent cycles. Require method-scoped green / compiling
defect / intended assertion red / restore / fresh build / green, nonzero counts,
and source/build/cleanup evidence for each variant. Build/fixture errors are not red.
Mutation owns missing-control discovery, including the bounded scanner's aliases,
root helpers, alternate path syntax and sink coverage. Never widen a timeout or
loosen an assertion. No browser or real canary execution is needed for these controls.

Cost: the ordinary cold Antiphon.Tests checkpoint build measured 8m31s on the
loaded Linux host; initial cold preparation should budget about 9 minutes plus
admission. Incremental mutant builds are unmeasured. Retain build/admission/body/
teardown separately and revise the cycle budget from actual measurements.
