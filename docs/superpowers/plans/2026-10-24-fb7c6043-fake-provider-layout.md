# Linux fake-provider output layout repair

Original Code task/landing owner: fb7c6043-90ba-4c17-abe8-a77fec346f64.
Base: dfebf941d7d343009bb819cea4d65bb3dca8fbcf.

The two affected copy targets, fake-provider sources and shared build files are unchanged
between 12a0b107f and origin/master a97194ee7a4adf215454fd82c453ba46362596ee.
Clean default-apphost builds reproduce an existing executable/directory name collision.
This is not introduced by aad39f211's package pinning. Checkpoint builds suppress apphosts
on Linux, so the ordinary checkpoint smoke cannot alone qualify this repair.

S1 commits the actual-build red witness. S2 filters only redundant Unix fake-provider
apphosts from transitive content copying, retaining managed assembly references and full
nested staging. Windows retains its existing copy behavior. Existing regular apphost files
at the old root paths must be removed before staging, allowing recovery after a failed build.

## Performance budget

Provisional Linux measurements at the base: Pty default build 30 seconds including
10 one-second copy retries; E2E cold build approximately two minutes. Slot waits are separate
(observed zero). Budget ten minutes for the two witness groups and smoke builds, and ten
minutes provisionally for the required Unit lane including build. No timeout is changed.
No application/server/container fixture is needed by the two smoke classes or build witness.
Unit setup and external-call timing remain owned by existing fixtures; TRX records per-test
duration and class. Unit is the required category lane, not an unfiltered assembly run.
The brief's minimal integration scope is CxSessionTestHomeTests and
LandDeliveryFixtureProbeTests. StdinClipModelTests additionally verifies that filtering
apphosts preserves managed runtime references. Shared target imports affect only Pty/E2E;
the Antiphon.Tests Unit lane is a standing Final verification requirement.

## Verification design

V-1: both clean pre-fix builds fail specifically with MSB3021 into an apphost-occupied directory.
V-2: both clean post-fix default builds and a second build into the same output succeed,
with nested native apphost, DLL, deps and runtimeconfig files intact.
V-3: read-only Windows reasoning confirms the new target is disabled on Windows.
V-4: current rc has identical affected inputs and merge-tree application is conflict-free.
R-1: full CxSessionTestHomeTests and StdinClipModelTests execute.
R-2: full LandDeliveryFixtureProbeTests executes.
R-3: whole Antiphon.Tests Unit category executes as required by the Final stage contract.
Manual acceptance: recover the original failed output directories with the repaired targets.
No deliberate mutation is performed by Code. PC inventory: zero; missing-control discovery
and any resulting controls remain pending for SourceLanding Mutation.

CP-1/2/3/4 are command rows so checkpoint confinement cannot hide the defect by injecting
UseAppHost=false. Their inner builds inherit the row's host slot. Initial direct base builds
are diagnostic runs authorized by the brief; preserve their failures and cost separately.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | pty-layout-red | `pwsh -NoProfile -File scripts/test-fake-provider-layout.ps1 -Project Pty -Phase Red -OutputPath bin-fb7c6043-witness-red-pty/` | V-1 | one build fails with the expected copy collision | n/a | 1 | true |
| CP-2 | S1 | n/a | e2e-layout-red | `pwsh -NoProfile -File scripts/test-fake-provider-layout.ps1 -Project E2E -Phase Red -OutputPath bin-fb7c6043-witness-red-e2e/` | V-1 | one build fails with the expected copy collision | n/a | 3 | true |
| CP-3 | S2 | n/a | pty-layout-green | `pwsh -NoProfile -File scripts/test-fake-provider-layout.ps1 -Project Pty -Phase Green -OutputPath bin-fb7c6043-witness-green-pty/` | V-2 | both builds pass and all layout assertions pass | n/a | 2 | true |
| CP-4 | S2 | n/a | e2e-layout-green | `pwsh -NoProfile -File scripts/test-fake-provider-layout.ps1 -Project E2E -Phase Green -OutputPath bin-fb7c6043-witness-green-e2e/` | V-2 | both builds pass and all layout assertions pass | n/a | 3 | true |
| CP-5 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-fb7c6043-pty-smoke/` | pty-smoke | `/*/*/(CxSessionTestHomeTests*)\|(StdinClipModelTests*)/*` | R-1 | all listed, 0 failed/skipped | 12 | 2 | true |
| CP-6 | S2 | `tests/Antiphon.E2E -> bin-fb7c6043-e2e-smoke/` | e2e-smoke | `/*/*/LandDeliveryFixtureProbeTests/*` | R-2 | all listed, 0 failed/skipped | 1 | 3 | true |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-fb7c6043-unit/` | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed | 1 | 10 | true |
