# Why missed: compiled probe model omitted review-engine collection metadata

Windows Full run `acca11ba`, group `antiphon-all-group-102`, on candidate
`6ed4cb58cd75fe386e94965165d96752452431a8` (`rc/2026-12-04`).

## Introducing commit/card

`94f10cd67f8e3887145c4c843d139749af92547f` introduced `ReviewFinding.Flags` and the other review-engine
string collections. `7cd7b750f2c5bbe0102f8b1f06903d2251f39b2b` consolidated the migrations into
`AddReviewEngine`. The failing group belongs to the CARD-1514 qualification repair loop.

## Approving reviews

The introducing change was in the operator's 2026-11-22 direct-push series. No approving Full or Interim
Review or Merge checkpoint for it was identified. The consolidated migration commit records a
`has-pending-model-changes` check. That check compares the design-time model against the migration snapshot.

## Outside the selection

`ChannelOutboundProbeModelTests` is `Integration` and `Slow`. Its publish test runs the generator in a
child process with no database connection. A normal Merge runs a build and narrow witnesses, including
the impacted-class selection for matching paths. The `migrations` kind already selects
`AppDbContextModelTests` for model and migration changes, but lacks the compiled-probe publish test.
Building the probe stages its executable. The publish test invokes it and checks the result.

The Linux Unit filter excludes this Integration test. The old Docker Linux roster cannot account for
recent execution coverage because CARD-1856 removed the unread `tests/linux-test-roster.json` in
`9d98ed09237fe10b80a145dc8d5ef3d3a4676675`. The review-engine roster commit
`7980047e7259f7e32ee45a8645c8c3f7b2f0bada` added a Windows E2E identity and duration row.
Neither roster supplied a pre-publication probe parity witness. See [Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18),
[impacted classes](../testing-and-build.md#impacted-classes-card-1782),
[the retired roster](../docker-stack.md), and [the current test categories](../../tests/Antiphon.Tests/Application/ChannelOutboundProbeModelTests.cs).

## Reproduction and impact

On Linux at `3189159aa0b2b06b97c083ee89b185576edd76ce`, the class ran two tests and failed only
`Probe_publishes_a_compiled_model_matching_its_runtime_model`. The generator took 12.9 s and exited 3.
`ReviewFinding.Flags`, `ReviewGate.Reasons`, `ReviewRun.Categories`, `ReviewSubject.OwnerTeams`, and
`ReviewSubject.OwnerUsers` all compared as `element-nullable=False` at runtime and `unknown` after
scaffolding. Their `text[]` store types, Npgsql array mappings, converters, and comparers matched.

`ProbeGeneratedModel` invokes `dotnet <staged Antiphon.ChannelOutbound.Probe.dll> --generate-model <dir>`
and keys its cache by build assembly identities. The [generator](../../tests/Antiphon.ChannelOutbound.Probe/ProbeModelGenerator.cs)
uses the EF scaffolder behind `dotnet ef dbcontext optimize`, compiles with Roslyn, and publishes the DLL
only after `ProbeModelComparison` accepts it. The failing build generated fresh source. EF 9's scaffolder
omitted `RuntimeProperty.SetElementType` metadata. The probe now emits it during scaffolding for
conventional arrays and compares the resulting element metadata directly. EF 10's scaffolder emits
element metadata itself. See the [pinned EF 9 property generator](https://github.com/dotnet/efcore/blob/v9.0.20/src/EFCore.Design/Scaffolding/Internal/CSharpRuntimeModelCodeGenerator.cs#L959)
and the [EF 10 element generator](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Design/Scaffolding/Internal/CSharpRuntimeModelCodeGenerator.cs#L1173).

Production `AppDbContext` uses EF's runtime model. Its `AddDbContext` options register interceptors and
Npgsql with the server migrations assembly and PostgreSQL 16. They supply no `CoreOptionsExtension.Model`,
and the context assembly has no `DbContextModelAttribute`. EF therefore calls `ModelSource.GetModel`,
which runs `OnModelCreating` and its `ConfigureReviewEngineModel`. `AddReviewEngine` defines `Flags` as
`text[]`. The defect affects probe publication and forces affected probes onto their slower runtime-model
fallback. Production uses the runtime mapping for `Flags` reads and writes. See
[server registration](../../server/Program.cs#L138),
[model configuration](../../server/Infrastructure/Data/AppDbContext.cs#L236),
[migration column](../../server/Migrations/20261122173647_AddReviewEngine.cs#L137),
[EF 9 model selection](https://github.com/dotnet/efcore/blob/v9.0.20/src/EFCore/Internal/DbContextServices.cs#L65),
and [probe model loading](../../tests/Antiphon.ChannelOutbound.Probe/ProbeModel.cs).

## Cheapest pre-land check

Add a method row to the CARD-1782 impacted-classes table for these path patterns:

| Path | Required Linux checkpoint filter |
|---|---|
| `server/Domain/Entities/**`, `server/Infrastructure/Data/AppDbContext*.cs`, `server/Infrastructure/Data/**/*Model.cs`, `server/Migrations/**`, `tests/Antiphon.ChannelOutbound.Probe/**` | `/*/Antiphon.Tests.Application/ChannelOutboundProbeModelTests/Probe_publishes_a_compiled_model_matching_its_runtime_model` |

Run it from the candidate's clean staged test build. Require one executed case and a clean receipt before
publishing a Merge or a direct master update for those paths. The case generates and checks the model in
one process. Keep the separate migration-snapshot witness for schema parity.

## Exemption

None.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-g102/` | model-parity | `/*/*/(ChannelOutboundProbeModelTests*)\|(ProbeModelComparisonTests*)\|(AppDbContextModelTests*)\|(ReviewEngineMigrationTests*)/*` | generated model parity, comparison witnesses and migration snapshot | 24 passed, 0 failed/skipped | 24 | 4 | true |
