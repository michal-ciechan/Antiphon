# CARD-1718 A-D3 round 2 repair verification

Original Code landing owner: `5e05fc80-930f-4f03-8c71-6c323d552d63`.
Review diff base: `249d4654d2410c4a3959a492ee0165e8a75b75f9`.
Resume base: `f47be567b5545c0014775ff18be9f423714ee14a`.

Invariant: a secret-shaped ignored path, including a descendant of a secret-named directory,
keeps its bytes and Git registration through admission, locked recheck, removal and recovery.
The server and runner consume one mandatory floor; disposable overrides cannot weaken it.
Each new physical witness plants one secret independently so another match cannot mask a defect.

Scope follows the resume brief: full named affected classes below, ordinary B-1..B-3 witnesses,
and guards. The whole Unit lane belongs to CARD-1720. No full assembly run is selected.
No changed OS branch/factory or startup composition: the real Linux retention transport uses
`RunnerWorkspaceRetention` with `ProcWorkspaceSlotProcessProbe` and real scratch Git repositories.
Windows alias/open-handle/long-path native acceptance remains pending while desktop qualification
is live. No server/runner restart is required.

## Verification design

- V-B1: initial admission, locked recheck, phase B and DryRun/Execute residue retention;
  failed ignored enumeration refuses; real server-to-runner transport and lost reply replay.
- V-B2: shared floor, runner default and catch-all disposable policy, initial and final
  completed-card transport inspection, byte/registration preservation and replay.
- V-B3: independent credentials/tls.pem descendants, server physical cleanup on each reading,
  real retention transport, and all conventional names in classifier arguments.
- R-1: full classifier/settings, legacy desktop release portable policy, retention and removal classes.
- R-2: classification and slow-test guards for both changed test assemblies.
- V-Native: C0824 native cases are selected for visibility but their Linux skips remain pending.
  Whole Unit and the prior Review performance finding V-2 are owned by CARD-1720, not passed here.

Every PC remains pending for method-scoped SourceLanding Mutation. Existing controls M1/M2,
PEM-floor removal and protected-after-disposable remain pending. Additional candidate controls:
remove admission, locked recheck, phase-B or residue floor guard; remove shared runner floor;
remove credentials/tls.pem descendant companions; move protected classification after disposal.
Code uses the real historical pre-fix source as a negative witness, not deliberate production mutants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.SessionRunner.Tests -> bin-c1718-r2-runner/ | runner-floor-and-retention | /*/*/(RunnerWorkspaceIgnoredContentFloorTests*)\|(C1677RetentionEngineTests*)\|(C1677RetentionDispatchTests*)\|(C1677RetentionInventoryTests*)\|(C1677RetentionNativeProcessTests*)\|(CompletedCardWorkspaceRemovalTests*)\|(TestClassificationGuardTests*)/* | V-B1, V-B2, V-B3, R-1, R-2 | all listed, 0 failed | 100 | 10 | true | TEMP=/tmp |
| CP-2 | all | tests/Antiphon.Tests -> bin-c1718-r2-server/ | server-floor-transport-and-regressions | /*/*/(WorktreeIgnoredContentClassifierTests*)\|(WorktreeCleanupSettingsValidatorTests*)\|(C0824DesktopLegacyRelease*)\|(C1677RetentionServerTests*)\|(WorktreeGuardedCleanupTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | V-B1, V-B2, V-B3, R-1, R-2, V-Native | all listed, 0 failed; native skips pending | 300 | 12 | true | TEMP=/tmp |
| CP-3 | all | CP-1 | appsettings-negative-witness | /*/*/C1677RetentionEngineTests/Appsettings_secret_survives_every_retention_boundary | V-B1-appsettings | 5 assertion failures before fix, green after fix | 5 | 1 | true | TEMP=/tmp |

## Performance budget

Two isolated builds, sequential classes with their existing assembly process limiters. Provisional
22-minute execution estimate; 30-minute total allowance and 50-minute hard wall cap. Each build
and each row acquires its own host slot (20-minute admission bound in generated manifest if needed;
the tool default is 45 minutes). No test timeout is widened. The prior server subset measured
233 seconds build plus 151 seconds tests at 249d4654d; added transport/removal coverage is provisional,
one collection run under CARD-1718. Existing per-test deadlines remain unchanged. Tool bootstrap is
an explicitly unlisted necessary build, native outputs, `UseAppHost=false`, separately leased.
Fresh TRX and clean source/build receipts remain under ignored `.antiphon/checkpoints/`.
If a row is red, diagnose and rerun that row after a committed repair; no green repetition.
