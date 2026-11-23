# CARD-1514 readiness re-cut review

**Return to Code for the missing impact selection.** All nine chain blobs match the previous reviewed tip. The only production delta from the candidate base is the reviewed change to `WorkingDirectoryIdentity`, `ProjectSetupService` and `AgentService`. The re-cut fixes the fable-hold leak.

The reviewed Code owner is `84561585-b264-4731-afe4-2d7dfdb1fadd` at `eefad7d1802defd9043980ca9e985438f50c3291`. The previous reviewed SHA is `0ad676b98811edc463ee251fe6a1f4fae595ce3a`. The candidate base is `e298e6fbb5c9125e3144e02b6f9a8432a229a3bc`.

## Verification findings

| Where | Failure | Why it matters | Fix |
|---|---|---|---|
| Re-cut checkpoint table, CP-4 and CP-7 | The impact selector returns 19 Linux classes for the edits after the byte-identical picks. The table covers eight. Across the candidate-base diff it omits 17 distinct Linux class names. | The writer fixture has additional callers, and the generated index has additional consumers. Seven green rows leave these unchecked. | Run the missing project-specific selections once at the frozen SHA. Keep the validated rows. |
| Code checkpoint-tool bootstrap | Code transcript tool calls 87, 89, 95, 98 and 100 use build-capable `dotnet run --project tools/Antiphon.Checkpoints` without a host build lease or `--no-build`. | The scheduler grants leases after the launcher has already invoked MSBuild. Its “unlisted” count only covers the scheduler's own commands. | Use a leased tool build followed by the built DLL. Review did this for its independent run. |

Eleven omissions come from the re-cut itself.

- The writer fixture selects `CardSpawnModelArgumentTests`, `GrokRulesLaunchRefusalTests`, `HerdrPlacementPreflightTests`, `StandingSessionOwnershipTests`, `StandingSessionSelectionTests` and `WorktreeDefaultCallerTests`.
- The index selects `IncrementalSelectionSoundnessTests`, `LandGeneratedArtifactTests`, `RcInputManifestTests`, `RcInputOwnersMapTests` and `ReleaseImpactEnvelopeTests`.

The candidate-base selector adds six more missing class names, `CardFilePrivacyGitTests`, `CardFilePrivacyPathTests`, `ProcessSpawnLimitTests`, `RcBridgeProbePlatformTests`, `RunCheckpointSourceScriptTests` and `TestTimingOperationLabelTests`. It selects `ProcessSpawnLimitTests` in Antiphon.Tests, Antiphon.Agents.Pty.Tests and Antiphon.SessionRunner.Tests. The migration/downgrade selector matched zero rows. The selector also reports a native Windows filter for `AgentSessionServiceIntegrationTests` and 14 capped dependents of `AgentService.cs`.

## Results at the frozen SHA

| Checkpoint | Passed | Failed | Skipped |
|---|---:|---:|---:|
| CP-1 original readiness witness | 1 | 0 | 0 |
| CP-2 readiness and identity | 199 | 0 | 0 |
| CP-3 Final guards | 85 | 0 | 0 |
| CP-4 all 24 named classes | 514 | 0 | 10 |
| CP-5 roster consumer | 6 | 0 | 1 |
| CP-6 two writers and the launch reader | 3 | 0 | 0 |
| CP-7 generated index | 27 | 0 | 0 |
| Two more executions of the CP-6 filter | 6 | 0 | 0 |

The pre-fix TRX at `79904873bf749f6945592aeb37c08b3a7489ca2a` records both writers failing on the saved hold's visibility from the shared fixture database. Each now uses the same private clone for seeding and service resolution. The writers share the AgentControl serialization key, so at most one of their clones is live per test host. Async disposal clears the clone's connection pool and force-drops its database, including after an assertion throws. Review's three trio TRXs each show the reader overlapping both writers. The index test rebuilt the expected bytes from the final tree and compared them with the entire committed file.

Code's run `20261123-050340-420c` recorded seven rows. Transcript tool result 112 confirms the exact-SHA validator accepted all seven at the time. After workspace retirement the retained copy validates five rows. CP-4 and CP-5 need their original TRXs to authenticate the skips, and the retention rule kept only the two reports. Review preserved its own original TRXs with its receipt. All positive controls remain pending for SourceLanding Mutation.

Review run `20261123-053028-07ea` validated all nine rows at the frozen SHA with verified build provenance, 841 passes and 11 roster-qualified skips. It took 23m52s including 6m03s of finalization. The two additional rows repeated only CP-6. The leased tool bootstrap and evidence-diff launcher were the two preparatory builds. The unedited checkpoint lines, report and nine original TRXs are under `/work/review-evidence/23b2fe07/20261123-053028-07ea/`.

The fresh merge-tree against master `9eaff7acbf7bb111aefa636830aadb0e04aa994a` was clean and produced tree `638d0e3cd06eeb693c476e7aa5945bbb0e96a8d1`. Evidence-diff found 0 entries and 0 violations in 19 candidate commits.
