# Family channel input safety

This repair follows Review 9015c74b on source a668cfb00aaf3a0d34fe5c2ec7c34478c4a72265. The original Code and landing owner is de62cb52-7fda-4e3c-9fa6-bc1eb3f88a97. The [investigation](../../investigations/2026-11-27-family-input-withheld-fix.md) records the earlier behavior and receipts.

## Verification design

| ID | Acceptance |
| --- | --- |
| V-1 | The guarded Claude writer requires an empty composer for a new channel body. A separate Enter requires the expected draft. An operator edit and an unread redraw each produce zero unintended backend bytes. |
| V-2 | An interrupted channel attempt remains unresolved after the recipient accepted Enter. Native transcript catch-up leads to one complete owning UserPrompt and one correlated answer from one body write. Legacy Pending attempts and spilled batch pointers retain their original delivery floors. |
| V-3 | Withheld channel retries use the 10, 30 and 120 second intervals. A newer inbound respects the older row's deadline and order. Advance the fixture clock with the original stored times. |
| V-4 | An interrupted bound session persists the continuation decision before launch. Repeated recovery after an interruption between commit and launch preserves one keyed continuation and keeps startup output outside the previous reply window. |
| V-5 | Adopted history requires the current owned transcript to be fully read, idle and paired with a stable empty composer. |
| R-1 | Exercise adjacent channel ingestion, prompt ownership, resume, queue, and guard classes. |
| R-2 | Run the six policy classes, mechanical contract, platform, layering, teardown, plan selection, and live inventory guards. The three plan-selection operands already recorded by Review 9015c74b belong to CARD-2093. |
| R-3 | Resolve and record the impacted-classes selection for the full task range. Check evidence-diff and the merge tree against fetched origin/master. |

Run the exact witness methods on the committed pre-fix source and inspect their assertion failures. Use the table below for current-source verification. Runner tests exercise the production input gate with controlled backend bytes and the real native transcript reader. Server tests use synthetic messages and PostgreSQL fixtures. The shared guarded input path runs before either native PTY write. Windows-native acceptance needs its own receipt because the desktop build lane is reserved. [Mutation candidates](../mutations/2026-11-27-family-channel-input-safety-unbound.md).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | --- |
| CP-1 | all | tests/Antiphon.SessionRunner.Tests -> bin-b4d89988-final-runner/ | Input and adopted history | `/*/*/(ClaudeDialogInputGuardTests*)\|(ResumeReceiptReadinessTests*)/*` | V-1, V-5 | 0 failed | 14 | 3 | true |
| CP-2 | all | tests/Antiphon.Tests -> bin-b4d89988-final-server/ | Channel input and resume | `/*/*/(ChannelInputWithheldIncidentTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSessionInterruptedLaunchResumeTests*)\|(ClaudeDialogDeliveryTests*)/*` | V-2, V-3, V-4, R-1 | 0 failed | 100 | 12 | true |
| CP-3 | all | CP-2 | Producer and correlation | `/*/*/(ChannelBridgeTests*)\|(ChannelInboundRecoveryTests*)\|(ChannelPromptCorrelationTests*)/*` | R-1 | 0 failed | 10 | 4 | true |
| CP-4 | all | CP-2 | Policy and adjacent guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestFixtureCleanupGuardTests*)/*` | R-2 | 0 failed | 80 | 2 | true |
| CP-5 | all | CP-2 | Plan selection | `/*/*/PlanCheckpointClassSelectionGuardTests/*` | R-2 | Compare inherited unresolved operands | 2 | 1 | true |
| CP-6 | all | CP-2 | Live inventory | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-2 | 0 failed | 2 | 1 | true |
