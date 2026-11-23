# CARD-1514 readiness verification supplement

**Return to Code.** On the frozen repair SHA, the additional selection exposed 21 failing session-runner cases, a failing volume-retirement journal case and four unlisted Windows-only skips. Matching master replays established that all three problems are inherited.

| Where | Failure | Why it blocks qualification | Fix |
|---|---|---|---|
| CP-26, `Card1653SocketDirTests.cs:576` and `Card1653PeerCheckTests.cs:143` | Twenty cases assert that `Path.GetTempPath()` is `/tmp/`. One real host launch throws `pty_host_socket_path_too_long`. | The checkpoint driver gives the test host a private `/tmp/aslot-…/` directory. Master replay `20261123-082651-eb77` reproduced the same 21 case identities and error signatures at `d413d4058e9944dff14747bd69cc93d0c7bf2beb`. | Use a short, fixture-owned socket path that works under the checkpoint lease. Preserve the production length bound and the native launch, adoption and boundary assertions. Rerun the 21 cases under the gate. |
| CP-22, CP-23 and the completed filesystem portion of CP-24 (CP-30) | The Linux TRXs contain four Windows-only methods absent from `WindowsUnitSkipRoster.WindowsOnly`. | The placement check rejects each affected TRX with `skip_identity_unlisted`. Master replay `20261123-081710-d7c6` reproduced the identical four skipped identities with three passing controls, and its exact-SHA validator returned the same reason. | Bind the four exact method identities below, count one each, to their Windows placement reasons and validate the repaired selection. |
| CP-33, `VolumeRetirementReceiptTests.Journal_intents_and_partial_resume_are_exact` | The isolated shell extracts `c1008_audit_checked` alone. On the temp-resume path it calls the unavailable `c1609_audit_failure_detail` and exits 127. The test expects exit 2. | The fixture fails before the journal cut assertions. Master replay `CP-5-20261123-084320-960d` returned the identical assertion message using the verified, retained build. | Load the helper dependency from `scripts/c1008-retirement.sh` in the fixture, retain the refusal and journal assertions, then run the exact method. |

The missing methods are `CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset`, `LandingRemovalPolicyControlTests.C665_LockedFileMidDeleteResumesOnLaterPass`, `LandingRemovalPolicyControlTests.C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded` and `WorktreeNoFollowDeleteTests.C1717_B3_WindowsOpenWriterRefusesTheDelete`.

## Execution

The five committed repair rows matched Code’s filters. CP-10 passed 3 and skipped both named Grok cases. CP-12 passed all 17, CP-15 passed 6 and skipped one, CP-16 passed 1 and CP-17 passed 27. All 57 method and parameter identities matched the prior exact-SHA receipt. CP-18 produced 102 passes and seven roster-qualified skips, including all 25 passes from the changed-roster fixture and three Windows junction skips. The main, Pty and SessionRunner limiter classes passed 57, 2 and 2 cases respectively. The selector emits the timing probe under the main assembly. CP-20 exercised its actual fixture assembly through `C1514T_NativeLifecycle`, which checks seven child cases with timing disabled and enabled and asserts the intentional failure and timeout.

Candidate run `20261123-073007-7d0a` took 54m53s. Its three builds and 14 rows held slots and recorded clean source and verified build provenance. It returned exit 5, and the full receipt validator returned `report_failed`. CP-24 timed out at 15 minutes with no TRX. Its diagnostic journal showed 10 of 27 volume-retirement cases complete, including a 324-second method. The remaining verification split just CP-24 across smaller filters at the existing 15-minute row deadline. The caller owes a performance Debug task for this 54m53s run under the 30-minute rule. Its report records the exact selection, host load, build, admission and row timings.

Recovery run `20261123-082223-82ca` took 26m28s. Its exact-SHA validator returned `report_failed`. Across both candidate runs, 21 fresh TRXs accounted for **696 passes, 22 failures and 14 skips across 732 cases**. The source census and retained TRXs were checked by method, parameter display, run ID and execution ID. Every one of the 732 expected top-level cases was present. Ten of the 14 skips qualified against the frozen roster.

| Candidate group | Passed | Failed | Skipped |
|---|---:|---:|---:|
| CP-10 grok | 3 | 0 | 2 |
| CP-12 timing | 17 | 0 | 0 |
| CP-15 roster | 6 | 0 | 1 |
| CP-16 exclusion | 1 | 0 | 0 |
| CP-17 derived-index | 27 | 0 | 0 |
| CP-18 roster-fixtures | 102 | 0 | 7 |
| CP-19 timing-consumers | 24 | 0 | 0 |
| CP-20 timing-probe | 1 | 0 | 0 |
| CP-21 persistence | 40 | 0 | 0 |
| CP-22 checkpoint-files | 37 | 0 | 1 |
| CP-23 release-policy | 298 | 0 | 2 |
| CP-24 file-ownership | — | — | — |
| CP-25 pty-limiter | 2 | 0 | 0 |
| CP-26 runner-lifecycle | 66 | 21 | 0 |
| CP-30 filesystem | 46 | 0 | 1 |
| CP-31 volume-1 | 3 | 0 | 0 |
| CP-32 volume-2 | 3 | 0 | 0 |
| CP-33 volume-3 | 3 | 1 | 0 |
| CP-34 volume-4 | 3 | 0 | 0 |
| CP-35 volume-5 | 4 | 0 | 0 |
| CP-36 volume-6 | 4 | 0 | 0 |
| CP-37 volume-7 | 6 | 0 | 0 |

The brief permits reuse from round 2. The retained TRX hashes, unchanged sources and prior identity census account for 132 passes across `CardSpawnModelArgumentTests` (15), `HerdrPlacementPreflightTests` (7), `StandingSessionOwnershipTests` (5), `StandingSessionSelectionTests` (39), `WorktreeDefaultCallerTests` (8), `IncrementalSelectionSoundnessTests` (2), `LandGeneratedArtifactTests` (5), `RcInputManifestTests` (6), `RcInputOwnersMapTests` (34) and `ReleaseImpactEnvelopeTests` (11).

## Provenance and follow-up

The prior failing C1620 TRX names both dropped C-locale labels. The repaired guard passed. The archived pre-repair Grok skips failed placement and the repaired skips qualified. The two shared-database writer assertion failures and three subsequent green writer/reader runs matched their archived hashes. All positive controls stay pending for SourceLanding Mutation.

The commissioned r4 pass supplies execution evidence for the unchanged round-3 source. The substantive reviewed range is empty. The impact comparison from `eefad7d1802defd9043980ca9e985438f50c3291` changes two timing labels, two placement entries and the repair plan. The selector emitted 32 class names across three primary projects and selected zero migration/downgrade rows. Evidence-diff against `e298e6fbb5c9125e3144e02b6f9a8432a229a3bc` returned zero entries and zero violations across 21 commits. Merge-tree against fetched master `d413d4058e9944dff14747bd69cc93d0c7bf2beb` returned `cf12d87daacb73ffbfcc7caae5504881e3f51fa3` without conflicts. The checkpoint bootstrap and evidence-diff launcher had separate leases. Round 3 recorded five ungated Code bootstrap invocations and one ungated Code evidence-diff launcher.

The plan follow-up requested in the brief is to bind all five Grok methods to CP-10 and set execution floors of three on Linux and five on Windows. Require all 17 CP-12 cases, including the timing-label guard. Neither item affected the observed repair identities.

The original Code landing owner is `84561585-b264-4731-afe4-2d7dfdb1fadd`. The reviewed repair source is `74b3af9d-e34b-49bd-82d3-35ee62bbb66b`, `a8a6c6f35c7ca1175465d89d93ea42eed390f57e`. The evidence root is `/work/review-evidence/a2668d12/`. It contains unedited checkpoint reports, available TRXs and hashes, both master replays, the case audits, selector results and validator outputs.
