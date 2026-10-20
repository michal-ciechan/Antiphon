# CARD-1367: keep helper apphosts out of the test output root

Plan and verification design, 2026-10-20. Source inspected:
`6ec71d427574f3a1b74bfbe2042e0ea9a819d191`. Code budget: **45 minutes (30–60 minutes)**.

## Root cause and ground truth

`tests/Antiphon.Tests/Antiphon.Tests.csproj:80–83` references the two executable
projects with `ReferenceOutputAssembly="false"`, but leaves `Private` unset.
That suppresses assembly referencing, not the SDK's transitive content copy.
`src/Antiphon.FakeClaude/Antiphon.FakeClaude.csproj:4–11` defines an executable
whose assembly/apphost name is `fakeclaude`; FakeGrok has the same shape at
`src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj:4–11`.

The installed SDK 10.0.402 makes this path explicit:

- `Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets:1031–1034`, target
  `_ComputeNETCoreBuildOutputFiles`, adds the apphost as `None`, with
  `Link=$(AssemblyName)$(_NativeExecutableExtension)` and
  `CopyToOutputDirectory=PreserveNewest`.
- `Microsoft.Common.CurrentVersion.targets:5255–5271`, target
  `_GetCopyToOutputDirectoryItemsFromTransitiveProjectReferences`, imports those
  items unless the project reference has `Private=false`. The normal output
  copy therefore places the extensionless apphost at the test output's
  `fakeclaude` path.
- `tests/Antiphon.Tests/Antiphon.Tests.csproj:96–97` also declares helper folders
  `fakeclaude` and `fakegrok`. `CopyStagedHelpers` computes those destinations at
  lines 115–127 and copies the producer's files beneath them. A file already
  occupying `fakeclaude` prevents these copies, causing MSB3021/retry MSB3027.
- `Microsoft.NET.Sdk.Shared.targets:33` supplies `.exe` for Windows. Its root
  `fakeclaude.exe` can coexist with directory `fakeclaude/`; Linux's names are
  identical. The same conflict would then affect FakeGrok.

| Card assumption | Code-grounded finding |
|---|---|
| An alternate output causes a file/directory collision. | Correct: with a relative `OutputPath`, producers have separate output directories, but SDK content propagation copies their apphosts into the consumer root before custom staging. |
| `ReferenceOutputAssembly=false` makes this a build-only reference. | Incomplete: it does not disable transitive content copying; `Private=false` does. |
| Checkpoint builds differ, presumably in layout. | Both layout and properties differ. `tools/Antiphon.Checkpoints/BuildConfinement.cs:473–477` and `scripts/run-checkpoint.ps1:563–572` explicitly add `UseAppHost=false` off Windows. The tool also confines outputs into a shared absolute root. |
| Only FakeClaude needs attention. | FakeGrok has the identical reference/folder pattern and must receive the same fix. |
| Native helper execution must survive. | Existing staging copies full helper outputs; Linux `CopyOwnedHerdrFakeAppHosts` at csproj lines 158–170 also builds native apphosts and puts them in `fakeclaude/fakeclaude` and `fakegrok/fakegrok`. Consumers use those nested paths (`tests/Shared/TestAppHostPath.cs:7–9`). |

No build was run during Plan. The card's reported ten MSB3021/MSB3027 errors are
historical evidence, not a fresh measurement. CP-1 establishes the fresh witness.

## Decisions

- **D-1 — fix the two consumer references.** Add `Private="false"` alongside
  `ReferenceOutputAssembly="false"` on FakeClaude and FakeGrok in
  `tests/Antiphon.Tests/Antiphon.Tests.csproj`, with a brief comment explaining
  that `CopyStagedHelpers` owns their output. Keep the producers and staged paths.
  This is two metadata changes using the SDK's existing content-copy gate.
- **D-2 — retain checkpoint confinement and its apphost override.** An absolute
  shared output can collide even without transitive copying because a producer
  writes there directly. This card repairs the documented relative isolated
  build; it does not claim to enable arbitrary shared `OutputPath` builds with
  `UseAppHost=true`. Reject a global `UseAppHost=false` fix because it changes
  executable generation throughout the graph and hides the defect in the witness.
- **D-3 — preserve the helper layout.** Reject renaming the folders or assemblies
  because consumers already depend on them. Reject deleting a colliding apphost
  before staging because it conceals duplicate ownership and adds ordering risk.
- **D-4 — use an actual build regression test.** Add one small PowerShell harness,
  with red/green modes, instead of an XML-string assertion. It must observe the
  real SDK build and the resulting helper files. This dispatch folds test design
  into Plan; no decision or separate TestDesign stage remains.

## Implementation slices

| Slice | Files and action | Verification |
|---|---|---|
| S1 | Add `scripts/test-isolated-helper-build.ps1`: Linux-only regression harness taking `-Phase Red/Green` and a validated relative `-OutputPath bin-.../`. It performs exactly one foreground `dotnet build tests/Antiphon.Tests --property:OutputPath=<value> --nologo` through `scripts/build-slot.ps1`, records the exit code and complete log, and asserts the outcomes below. Commit/push before running it. | CP-1 on unfixed references; preserve its failing build result before the fix. |
| S2 | Apply D-1 in `tests/Antiphon.Tests/Antiphon.Tests.csproj`. Add a short clarification to the MSBuild-properties paragraph of `docs/testing-and-build.md`: relative builds now isolate staged helpers via reference metadata; the shared-output checkpoint workaround remains. Commit/push before running. | CP-2 using the same harness and a fresh output name. |

The harness must fail on unsupported OS, dirty tracked source, pre-existing output
roots, build-slot timeout, missing expected diagnostics, or failed assertions. Record
HEAD before/after and the build-slot grant/release lines; propagate unexpected
failures. Keep logs and output inventories under ignored `.antiphon/`. Inventory
every project directory this invocation creates before cleanup; remove only its own
`bin-<name>` outputs after the build exits, and retain diagnostic evidence. Never
delete ordinary `bin/`, `obj/`, or another run's output. No automatic build retries.

## Verification design

Lane: **Linux, relative isolated output, build only**. Read
`GET /api/runner-defaults` and `GET /api/session-runners` before dispatch. Both were
read during Plan; an eligible Linux runner exists, while the configured default
is unavailable/draining. Placement is a live decision: specify `-Platform Linux`,
omit `-Runner`, and let current eligibility/default routing select the host.

**V-1 (red):** CP-1 runs one build without `UseAppHost`, `ArtifactsPath`, `OutDir`,
or `UseArtifactsOutput` overrides. Require a nonzero build exit, an MSB3021
diagnostic tied to the `fakeclaude` staging destination, and an ordinary file at
the consumer's top-level `fakeclaude` path. MSB3027 may accompany it; do not pin
the retry count or total error count. Other compiler/restore failures or a slot
timeout are not the witness. The harness reports three checks and exits zero only
when all expected-red checks pass; explicitly retain the inner build's failure.

**V-2/R-1 (green and helper preservation):** CP-2 repeats that build with a fresh
relative output after S2. Require exit zero (one check). For each helper require
its staged directory and four files: the extensionless apphost, `.dll`, `.deps.json`,
and `.runtimeconfig.json` (ten more checks). Require `Antiphon.Tests.dll` at the
consumer output root (one more check): **12 checks total**. Verify the files before
cleanup. No TUnit cases or helper processes are executed by this build test.

The table is the closed list: **one red build plus one narrow green build**, no
Unit/category/assembly runs, no Windows qualification, no extra positive-control
build. The real pre-fix collision is the red control for the build regression test.
Use command rows so checkpoint build confinement cannot rewrite the relative
output or inject `UseAppHost=false`. If driven by the checkpoint tool, select
`--rows CP-1` after S1 and `--rows CP-2` after S2, using an available prebuilt tool;
the commands below are also the explicit brief-authorized direct build-slot path.
Do not bootstrap an extra tool build just to run these two commands. Report actual
build counts, checks, SHA, native exit codes, slot outcome, duration and log paths;
do not invent TUnit counts or claim green if unrelated errors prevent the build.

### Cost

Ordinary execution floor: 10 + 10 = **20 minutes**. Harness/fix/documentation:
20 minutes; reporting/cleanup: 5 minutes. Total **45 minutes**, bounded by the
requested 30–60 minute Code budget. No optional broad verification is budgeted.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | n/a | linux-relative-red | `pwsh -NoProfile -File scripts/test-isolated-helper-build.ps1 -Phase Red -OutputPath bin-c1367-red/` | V-1 | Exactly 1 failed build with the expected collision; all 3 witness checks pass | n/a | 10 |
| CP-2 | S2 | n/a | linux-relative-green | `pwsh -NoProfile -File scripts/test-isolated-helper-build.ps1 -Phase Green -OutputPath bin-c1367-green/` | V-2, R-1 | Exactly 1 successful build; all 12 checks pass | n/a | 10 |
