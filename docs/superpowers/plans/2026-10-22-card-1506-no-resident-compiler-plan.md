# CARD-1506: Windows build driver memory policy

Owner: original Code task 84f61b86-39be-4636-8edf-537143fae95b.
Base: 3f54023d6b715fe2653014dc82187d358c0193b6. Round: Final.

## Design

Use one shared C# policy source, `scripts/lib/BuildDriverEnvironment.cs`, linked
into the checkpoint tool and server and loaded by a small PowerShell adapter.
Apply it to the child environment before process creation. On Windows set
`UseSharedCompilation=false`, `DOTNET_gcServer=0`, `DOTNET_gcConcurrent=0`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0`, `MSBUILDDISABLENODEREUSE=1`, and the SDK's
`DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. An exact
`ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT=1` preserves the inherited environment.
Linux is a no-op. Never set machine/user environment or shut down build servers.

Read inventory and integration boundaries:

| Entry | Actual driver launch | Integration |
|---|---|---|
| `scripts/build-slot.ps1` | native, PowerShell, command shim | scoped wrapper environment, inherited by children |
| checkpoint tool | `ProcessDriver` and `BuildConfinement` evaluation | child ProcessStartInfo |
| `scripts/run-checkpoint.ps1` | build/run and graph evaluation | child ProcessStartInfo |
| land verifier | `AgentTaskLandService.RunProcessAsync` | child ProcessStartInfo |
| `scripts/nightly-run.ps1` | bootstrap delegates to nightly tests | no build in bootstrap itself |
| `scripts/lib/nightly-tests-impl.ps1` | native owner and exact roster | child environment before native creation |
| `scripts/restart-apphost.ps1` | calls `dev-aspire.ps1` | route restore/build through build-slot; launch AppHost with no-build |
| `Directory.Build.props` | project evaluation, after driver startup | unchanged; cannot set startup GC or CLI environment |
| AppHost daemon supervisor | `scripts/run-daemon.ps1` builds before executable launch | build-slot child; daemon does not inherit policy |
| watched test executable | `scripts/run-tests-watched.ps1` | scoped environment around Start-Process |

The launch census also found operator-only publish/isolated-host scripts
`deploy-nightly-watchdog.ps1` and `verify-phone-home-grok.ps1`; invoke these through
`scripts/build-slot.ps1` as the existing build-slot contract requires. Their
children inherit the central policy. Linux-only nightly probe supervision,
Dockerfiles, shell deployment builds, fixture children and `dotnet --version`
identity reads are outside the Windows driver change.

## Slices

- S1: plan and narrow script witness. Commit/push, import the table, run CP-1 red
  against the existing Windows wrapper branch. Use a fixture-local platform
  override, fake slot broker and harmless environment-printing child.
- S2: shared policy and launch adapters, documentation, complete witness controls.
  Commit/push and rerun CP-1 green at final source; compile touched C# consumers.

## Verification design

The explicit brief and standing minimal-testing rule override the generic whole
Unit profile: one narrow red then green, no whole Unit/assembly/rc run. No live
Windows qualification, restart or shutdown is permitted from this task.

- V-1: Windows wrapper child receives all six settings, including when inherited
  values conflict. The red assertion names the missing setting.
- V-2: shared helper's child-environment output agrees across Windows default,
  exact opt-out and non-opt-out values; unrelated variables and parent survive.
  Four direct ProcessStartInfo cases additionally cover the C# platform/opt-out
  guards (24 setting assertions); the wrapper caller checks its own environment
  after the command returns.
- R-1: Linux wrapper and helper preserve absent and pre-existing settings.
- R-2: opt-out preserves conflicting caller values on Windows.
- V-3: compile both C# consumers and inspect every launch adapter above; parse
  changed PowerShell and confirm daemon scripts stay ASCII.
- V-4: full base..HEAD evidence guard, current-master merge-tree, and a three-way
  apply of the complete task delta onto current rc in a temporary index.

Native Windows memory/process-census acceptance belongs to the existing rc gate
after the reviewed pick; this Linux task proves launch environment, not a live
desktop memory measurement. Activation requires rc AppHost and desktop runner
restart, after qualification permits it. No restart performed here.

### Positive controls pending for SourceLanding Mutation

- PC-1: remove each of the six assignments, one variant per variable; V-1 must fail.
- PC-2: remove Windows guard; R-1 must fail.
- PC-3: ignore opt-out; R-2 must fail.
- PC-4: omit wrapper application; V-1 must fail.

Mutation owns deliberate mutants, red/restore/green, and missing-control discovery.

### Cost

45-minute task budget; ordinary checkpoint estimate 2 minutes plus two consumer
builds (estimated 4 minutes), tool bootstrap (1 minute), and authoring/inspection.
Unlisted builds are only checkpoint-tool bootstrap and final server compilation;
both take build slots and isolated output. No repetition after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | n/a | build-driver-environment | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1` | V-1, V-2, R-1, R-2 | 6 child scenarios and 4 direct cases, 0 failed (S1 intentionally red on Windows settings) | n/a | 2 |

The shared definition-file fingerprint is left at its base value to avoid
conflicting with concurrent target-wide fingerprint refreshes. A mismatching
fingerprint safely forces verification builds; regenerate it on the integrated
target with `scripts/audit-land-verification-graph.ps1 -Update`. The intermediate
fingerprint-only commit is canceled by a later ordinary commit, not rewritten.
Unbound controls: `docs/superpowers/mutations/2026-10-22-card-1506-no-resident-compiler-candidate.md`.
