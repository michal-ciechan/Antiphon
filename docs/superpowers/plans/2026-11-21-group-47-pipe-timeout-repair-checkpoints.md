# Group-47 pipe-timeout repair

Review round 1 blockers on the post-exit pipe bound. A mutating command that
already exited keeps that exit when the pipe bound fires, so a successful push
still reaches remote confirmation. The drain verdict is one snapshot of the
drain task. `HungPipeHolderFailsWithGitPipeTimeout` and
`PushExitZeroWithHungPipeConfirmsPublication` are Linux-only rc exclusions.

Positive controls: this plan has no PC-n row. None are discharged.

The inherited `C1714_Status_that_cannot_open_a_directory_is_untrusted` failure
is inside R-1. It is not this repair.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | repair | `tests/Antiphon.Tests -> bin-c4660b-pipe/` | pipe-repair | `/*/*/LandingGitTests/*` | post-exit exit preservation and the drain snapshot | 0 failed except the inherited C1714 status case | 84 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | repair | CP-1 | pipe-policy | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(ReleaseGatePlatformTests*)/*` | Linux-only exclusion roster | 0 failed, 0 skipped | 8 | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | repair | CP-1 | pipe-resettlement | `/*/*/ReviewEvidenceResettlementTests/*` | the class that was in the hung chunk | 0 failed, 0 skipped, 10 executed | 10 | 5 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | repair | CP-1 | pipe-publication | `/*/*/AgentTaskLandPublicationTests/*` | push confirmation through the landing protocol | 0 failed, 0 skipped | 60 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
