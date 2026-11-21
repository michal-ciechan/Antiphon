# Group-47 pipe-timeout repair, review round 2

The git exit is captured before drain or dispose. A caller that passes
`GitCommandEffect` is authoritative; remaining three-argument calls classify by
the verb after global options. A push that exited 0 stays `PushStarted` and
pending when the confirming read misses, and the hung-pipe land requires one
complete UserPrompt.

Positive controls: this plan has no PC-n row. None are discharged.

`C1714_Status_that_cannot_open_a_directory_is_untrusted` is inside CP-1.
`C1199_WindowsRequiredRoster` is inside CP-2. Both are inherited from master
and are not this repair. CP-4's skip is the inherited Windows sharing denial
`C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | repair | `tests/Antiphon.Tests -> bin-c3add1e5-pipe/` | r2-git | `/*/*/LandingGitTests/*` | exit capture, verb effect, and the drain snapshot | 1 failed inherited C1714, 0 skipped | 84 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | repair | CP-1 | r2-policy | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(ReleaseGatePlatformTests*)/*` | Linux-only exclusion roster | 1 failed inherited C1199, 0 skipped | 8 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | repair | CP-1 | r2-resettle | `/*/*/ReviewEvidenceResettlementTests/*` | the class that was in the hung chunk | 0 failed, 0 skipped, 10 executed | 10 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | repair | CP-1 | r2-publish | `/*/*/AgentTaskLandPublicationTests/*` | push confirmation and the resumable observation | 0 failed, 1 skipped inherited C688 | 60 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | repair | CP-1 | r2-witness | `/*/*/(LandingGitTests*)\|(AgentTaskLandPublicationTests*)\|(LandFailureDiagnosticTests*)/(TimerWinThenEofBeforeTheSampleIsClosed*)\|(ExitIsCapturedBeforeTheProcessIsDisposed*)\|(CallSiteReadThrowsWhenTheOperandIsPush*)\|(CallSiteMutationKeepsAMissingVerbExit*)\|(EffectOfDoesNotTreatOperandsAsVerbs*)\|(HungPipeHolderFailsWithGitPipeTimeout*)\|(PushExitZeroWithFailedObservationStaysResumable*)\|(PushExitZeroWithHungPipeConfirmsPublication*)\|(C498_ClassifierMapsConcurrencyBeforePersistence*)` | the round-2 witnesses by name | 0 failed, 0 skipped, 9 executed | 9 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
