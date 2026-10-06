# CARD-0795: isolated builds compile in-process instead of through the shared VBCSCompiler

Plan for CARD-0795 (High/Soon, platform Any), written 2026-10-06 on the server2-class runner
mirror at `ed0f0d920` (origin/master). Platform read at plan time: `GET /api/runner-defaults`
global runner `server2` (revision 2, Human); `GET /api/session-runners` shows `server2` draining,
`server2-temp` accepting (capacity 10, occupied 6) and `desktop` (Windows, capacity 2). No fleet
location is embedded here: every checkpoint names the **Linux lane** and nothing pins a host.

## Outcome and boundaries

An isolated build (any `dotnet build|run|test` whose `OutputPath` is `bin-<name>/`, the shape every
delegate, `scripts/run-checkpoint.ps1`, the checkpoint tool and `scripts/build-slot.ps1` callers
use) compiles each project in its own short-lived `csc` child of its MSBuild node instead of
routing through the one per-user shared `VBCSCompiler`. Builds into `bin/` (the daemons'
rebuilds, `restart-apphost.ps1`, `run-daemon.ps1`), `--artifacts-path` builds (the desktop land
verifier) and operator shells are untouched. An explicit `UseSharedCompilation` value, from the
command line or the environment, still wins. The isolated-build recipe in
`docs/testing-and-build.md` records the rule, the measured cost and the rollback lever.

Out of scope: proving that the shared compiler caused the timing-test reds listed on the card
(see Ground truth row 4), the CARD-0589 Round 2 build watchdog, and any change to node reuse,
the build-slot broker, the checkpoint tool's recipe or the SourceLanding brief text.

## Ground truth

| The card or brief assumes | What the code does | Where |
|---|---|---|
| "Every isolated-build command used by delegate tasks and the checkpoint tool sets `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`." | **Wrong.** No script, tool, runner launch path or settings file sets either variable (grep of `src`, `server`, `tools`, `scripts`, `tests`). Node reuse is off through the repo-root `Directory.Build.rsp` (`-nodeReuse:false`, CARD-0589 D-2) for every invoker, and the checkpoint tool repeats `-nodeReuse:false` on its own build line. The only occurrence of the env var name is the SourceLanding brief sentence. | `Directory.Build.rsp:5`; `tools/Antiphon.Checkpoints/Execution/BuildStep.cs:48`; `server/Application/Services/DelegationReportFormatter.cs:181` |
| "Roslyn's shared compilation server is a distinct mechanism these settings do not touch." | **Correct.** `-nodeReuse:false` governs MSBuild worker nodes only. `UseSharedCompilation` is unset in every repo recipe, so every build on a host shares the one `VBCSCompiler` keyed by user and SDK path. CARD-0589 D-2 left it on deliberately: one server shares metadata and peaks lower than the same number of in-process compilers, and it idles out after ten minutes. | `docs/testing-and-build.md:763`; `docs/superpowers/plans/2026-09-25-card-0589-build-fanout-cap-plan.md:119-123` |
| "So each isolated build gets its own compiler process." | **Not what the property does.** With `UseSharedCompilation=false` the `Csc` task starts `csc.dll` as a child of the MSBuild node for each project compile; it exits with that compile and belongs to the build's own process tree. There is no per-build server. Measured here (SDK 10.0.401, `-v:d`): the tool build with the property logs `CompilerServer: tool - using command line tool by design '<csc path>'` and no `server processed compilation` line; the default build logs `CompilerServer: server - server processed compilation - <project>`. | Roslyn `ManagedCompiler.ExecuteTool`; plan-stage builds of `tools/Antiphon.Checkpoints` into `bin-c795-plan/` |
| "A `VBCSCompiler` at 450-680 % CPU for hours is contention, and the cause of the ResilienceBudgetTests / ScaledTimeProviderTests / CheckpointTaskOwnershipTests reds." | **Partly.** The server compiles concurrent requests on its own threads, so its CPU is the sum of every concurrent build's compile work; nothing in the build-slot grant bounds it (`-maxcpucount:N` caps MSBuild nodes, not compiler threads). What is specific to the shared server: one long-lived process that outlives and is attributable to no session (at plan time a `VBCSCompiler` started by another task was visible from this task's container and gone a minute later), one GC heap for every build on the host, and no place in the CARD-0589 accounting. Turning it off bounds concurrent compiles to the node count per build (at most 4 builds x 6 nodes on server2, 2 x 4 on desktop) and makes each compile a child of its build. Whether the listed reds drop is a hypothesis this card cannot prove; D-4 says what is measured and what is left to observation. | `GET /build-slots` on this runner: `budget 4, maxCpuCount 6, floor 16384 MB, available 104804 MB`; `docs/testing-and-build.md:775-779` |
| "Add the property to the recipe used by delegate builds and the checkpoint tool." | Both recipes exist and share one marker. `scripts/run-checkpoint.ps1:370` and `BuildStep.BuildArguments` (`tools/Antiphon.Checkpoints/Execution/BuildStep.cs:46`) build with `--property:OutputPath=<bin-x/>`; `scripts/lib/build-slot.ps1` `Add-AntiphonMaxCpuCount` wraps raw drivers that carry the same switch; the manifest validator enforces `^bin-[A-Za-z0-9._-]+/$` (`tools/Antiphon.Checkpoints/Manifest/ManifestValidator.cs:8`) and `Directory.Build.props` already calls `bin-<name>/` "every delegate build". The land verifier builds with `--artifacts-path` (`server/Application/Services/AgentTaskLandService.cs:1756`) and the daemons into `bin/`; neither carries the marker. | as cited |
| CARD-0589 D-7 "the watchdog enumerates `VBCSCompiler` ... and attributes it to a session". | **Not landed.** No `BuildProcessWatchdog*` type exists under `src/Antiphon.SessionRunner` (only `SessionCpuWatchdogService`). Nothing today ties a shared compiler to a session. | `src/Antiphon.SessionRunner/` |
| Where `UseSharedCompilation=false` is already used. | SourceLanding briefs (custody), the CARD-0478 custody checkpoints and the CARD-0700 investigation passed it explicitly per command. None of those flow through the repo recipes, and an explicit value keeps winning after this card. | `DelegationReportFormatter.cs:181`; `docs/investigations/2026-09-25-card-0700-cached-board-lookups.md:124` |
| The property can be keyed on the isolated marker at evaluation time. | **Measured** in a scratch SDK project under the proposed `Directory.Build.props` (`dotnet msbuild -getProperty`, gated): no `OutputPath` -> `UseSharedCompilation` empty; `bin-c795x/` -> `false`; `bin-c795x\` -> `false`; `/abs/elsewhere/` -> empty; `bin-c795x/` plus `--property:UseSharedCompilation=true` -> `true`. | plan-stage evaluation, 2026-10-06 |
| Test registration. | `tests/linux-test-roster.json` is the frozen CARD-0590 Docker-stack roster; none of the six classes added since (for example `DirectoryBuildRspTests`, `CheckpointImportTests`) were registered, so this plan adds none. `tests/Antiphon.Tests/slow-tests-allowlist.txt` is unaffected: the new class is Integration, under ten seconds. | `scripts/test-docker-container.ps1:40`; `git log -1 -- tests/linux-test-roster.json` (CARD-0735) |

## Decisions

### D-1: the switch lives in `Directory.Build.props`, conditioned on the isolated-output marker

Add one property group to the repo-root `Directory.Build.props` (validated text; keep the file
ASCII and the comment in the file's existing style):

```xml
  <!--
    CARD-0795: an isolated build (OutputPath=bin-<name>/, the shape every delegate build,
    scripts/run-checkpoint.ps1, the checkpoint tool and build-slot.ps1 callers use) compiles each
    project in its own short-lived csc child of its MSBuild node instead of the one per-user shared
    VBCSCompiler, so concurrent builds stop sharing one unattributable compiler process and each
    compile dies with its build. bin/ builds (the daemons, restart-apphost.ps1), artifacts-path
    builds (the land verifier) and operator shells keep the SDK default. An explicit
    UseSharedCompilation (command line or environment) wins; deleting this group is the rollback.
    Owner: docs/testing-and-build.md "Build slots (CARD-0589)".
  -->
  <PropertyGroup>
    <AntiphonIsolatedOutput Condition="'$(AntiphonIsolatedOutput)' == '' and $([System.String]::Copy('$(OutputPath)').StartsWith('bin-'))">true</AntiphonIsolatedOutput>
    <UseSharedCompilation Condition="'$(UseSharedCompilation)' == '' and '$(AntiphonIsolatedOutput)' == 'true'">false</UseSharedCompilation>
  </PropertyGroup>
```

Why here: one file covers every invoker on both hosts (a delegate's raw `dotnet build`, the
wrappers, the checkpoint tool's `dotnet build` and `dotnet run --no-build`, the nightly's
isolated rows) and exactly the builds that run concurrently, with no tool, script or runner
change and no rollout: it is live for any checkout at the commit. `OutputPath` is a global
property on every isolated command line, so it is visible at `Directory.Build.props` evaluation
and flows to every project in the graph through `ProjectReference`. `AntiphonIsolatedOutput` gives
the marker one definition (the test reads it; later rules can key on it) instead of repeating the
prefix check.

Rejected:

- `-p:UseSharedCompilation=false` in `Directory.Build.rsp`, or an unconditional property: reverses
  CARD-0589 D-2 for the daemons' rebuilds, `restart-apphost.ps1`, operator shells and the land
  verifier, which run alone and benefit from the server.
- Adding the switch in `Add-AntiphonMaxCpuCount`, `BuildStep.BuildArguments` and
  `run-checkpoint.ps1`: three places to keep aligned, raw commands typed outside the gate miss it,
  and `RowRunnerTests`, `BuildSlotScriptTests` and `RunCheckpointScriptTests` argv pins all move.
- `UseSharedCompilation=false` in the session launch environment (the CARD-0589 "launch env"
  shape): runner code on both hosts plus a server2 rollout before it takes effect, and it hits
  every build a session runs, including `bin/` ones.
- Checkpoint tool only: misses the delegate builds that produce the contention.
- Throttling compiler parallelism (`csc /parallel-`) or lowering the server's priority: trades
  build time for CPU and does nothing for attribution or the shared heap.

### D-2: the override and the rollback

An explicit `--property:UseSharedCompilation=true` on an isolated command line wins by MSBuild's
global-property precedence (measured); an environment variable `UseSharedCompilation=true` wins
through the `'$(UseSharedCompilation)' == ''` guard. That is the per-build lever for measuring or
for a session that needs the server. Deleting the property group is the repo-wide rollback, the
same shape as deleting `Directory.Build.rsp`. SourceLanding briefs keep saying
`UseSharedCompilation=false`; it is now redundant for `bin-*` builds and still correct.

### D-3: verification is an evaluation test, two measured builds and a documentation pin

- **Evaluation test** (`DirectoryBuildPropsSharedCompilationTests`, Integration,
  `ParallelLimiter<ProcessSpawnLimit>`): three methods run
  `dotnet msbuild src/Antiphon.SessionRunner.Contracts/Antiphon.SessionRunner.Contracts.csproj -getProperty:UseSharedCompilation -getProperty:AntiphonIsolatedOutput -nologo <properties>`
  from the repo root (`DockerStackDocuments.RepoRoot`) and parse the JSON `Properties` object.
  That project has no package references, so evaluation needs no restore, runs no target, starts
  no node and no compiler, and writes nothing; it is the real import chain, not a copy of the
  props. Methods: `An_isolated_output_turns_shared_compilation_off` (`--property:OutputPath=bin-c795-eval/`
  -> `false` and marker `true`); `A_daemon_output_keeps_the_sdk_default` (no property ->
  `UseSharedCompilation` is not `false` and the marker is empty: the negative control for the
  marker); `An_environment_value_wins_on_an_isolated_output` (child environment
  `UseSharedCompilation=true` plus the isolated `OutputPath` -> `true`: the negative control for
  the guard). A 120-second deadline; stderr goes into the failure message. Red first: the methods
  are written and run before the property group exists (CP-1 red on the first and third), then
  the group is added (CP-1 green).
- **Measured builds** M-1 and M-2 (CP-2, CP-3): the same `tests/Antiphon.Tests --no-incremental`
  graph built into `bin-c795-m1/` under the rule and into `bin-c795-m2/` with the explicit
  override, each at `-v:d` into `.antiphon/c795/`. The discriminator is the pinned log text:
  `using command line tool by design` counts the in-process compiles, `server processed compilation`
  counts the server ones. M-1 expects at least one of the first and zero of the second; M-2 (the
  control, proving the discriminator sees the server when it is used) the reverse. Each row prints
  one `C795_MEASURE` line with its wall seconds and the broker's occupancy at its start. Both rows
  are `Serial` and use the same explicit `-maxcpucount:4`, because the tool does not inject the
  grant's count into command rows; the row's own lease is the slot, so the command must not wrap
  `build-slot.ps1` (that would wait for a second lease under the first).
- **Documentation pin**: one new method in `CheckpointManifestDocumentationTests` pins the Build
  slots section on `UseSharedCompilation=false`, `bin-`, `CARD-0795` and `csc` (red before the
  docs edit).

### D-4: what "not unacceptably slower" means, and what stays unproven

The acceptance default: M-1 wall seconds within 25 % of M-2 on the same host and the same
`-maxcpucount:4`. Between 25 % and 50 %: land, record the number in the docs and in the report,
and file a card to narrow the rule (for example to test projects only). Above 50 %: do not land;
end the Code task `blocked` with both `C795_MEASURE` lines, because at that cost the card's owner
chooses between contention and build time. Occupancy on the broker at the time of each row is
part of the record; two rows taken while other tasks build are not comparable and are rerun.

The flake hypothesis is not provable in this card. After land the observable signal is whether
`ResilienceBudgetTests`, `ScaledTimeProviderTests`, `CheckpointTaskOwnershipTests`,
`RunnerCodexAdapterReadyTests` and `RunnerClaudeAdapterEffortPromptTests` stop appearing as
`INHERITED` or `NEW` reds in checkpoint reports under load; the Review report should say so rather
than claim the fix.

### D-5: the documentation change, exactly

Three edits in `docs/testing-and-build.md`, additive (no existing pinned phrase is removed; the
section pins in `CheckpointManifestDocumentationTests` and `CheckpointRepeatDocumentationTests`
are run in CP-4):

1. The "**Building while daemons run**" bullet (line 70): after "(gitignored by `bin-*/`)." add
   "Every `bin-<name>/` build also compiles in-process (`UseSharedCompilation=false` from
   `Directory.Build.props`, CARD-0795): each project's `csc` is a child of the build's MSBuild
   node and exits with it, so concurrent delegate builds do not share one `VBCSCompiler`. Pass
   `--property:UseSharedCompilation=true` to measure against the server."
2. The "Build slots (CARD-0589)" paragraph on `Directory.Build.rsp` (line 763): replace the last
   sentence "`UseSharedCompilation` stays on (the shared `VBCSCompiler` idles out after ten
   minutes)." with "`UseSharedCompilation` stays at the SDK default for `bin/`, `--artifacts-path`
   and operator builds (the shared `VBCSCompiler` idles out after ten minutes); every isolated
   `bin-<name>/` build sets `UseSharedCompilation=false` through `Directory.Build.props`
   (CARD-0795), so each compile is a `csc` child of its own build and no concurrent build shares
   one compiler process. Measured on <host> at `<sha>` with `-maxcpucount:4` and broker occupancy
   `<n>`: `tests/Antiphon.Tests --no-incremental` <M-1 s> in-process (`<k>` compiles by design,
   0 server) against <M-2 s> through the server (`<k>` server-processed); deleting the property
   group in `Directory.Build.props` is the rollback, and an explicit `UseSharedCompilation` on the
   command line or in the environment wins." The placeholders are the CP-2 and CP-3 numbers.
3. The "Checkpoint runner tool (CARD-0723)" paragraph (line 457): extend "Every build uses
   `-nodeReuse:false`, `--property:OutputPath=bin-<name>/`, and `UseAppHost=false` off Windows
   unless the manifest names `UseAppHost`." with ", and through `Directory.Build.props` every
   `bin-<name>/` output compiles with `UseSharedCompilation=false` (CARD-0795)".

Not changed: `AGENTS.md` (no safety trigger changes), the CARD-0589 plan (historical),
`docs/orchestration-loop.md`, the SourceLanding brief text, `scripts/lib/build-slot.ps1`,
`scripts/run-checkpoint.ps1`, the checkpoint tool.

### D-6: lanes and hosts

All checkpoint rows run on the Linux lane; nothing names a runner or platform. The property
group and the evaluation test are platform-neutral (`bin-x\` also matches), and the Windows
desktop exercises them through its own delegate builds and the nightly. The measured numbers are
Linux numbers; the docs sentence names the host it was measured on.

## Slices

Two slices touching disjoint files; S2 depends on S1's commit because CP-2 and CP-3 measure the
rule and feed the docs. One Code task can run both in order.

### S1: the property group and its evaluation test (45-60 minutes)

Files: `Directory.Build.props` (the D-1 group, placed after the existing `PropertyGroup`, before
the `SetSourceRevisionIdFromGit` target); new
`tests/Antiphon.Tests/Infrastructure/DirectoryBuildPropsSharedCompilationTests.cs` (namespace
`Antiphon.Tests.Infrastructure`, `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`,
the three D-3 methods, a private `EvaluateAsync(IReadOnlyDictionary<string,string>? environment,
params string[] properties)` helper using `ProcessStartInfo("dotnet")` with `ArgumentList`,
`UseShellExecute=false`, both streams redirected, `WorkingDirectory = DockerStackDocuments.RepoRoot`,
`System.Text.Json` to read `Properties.UseSharedCompilation` and `Properties.AntiphonIsolatedOutput`).

Order: write the test file; run CP-1 and confirm the first and third methods are red while
`A_daemon_output_keeps_the_sdk_default` and the two `DirectoryBuildRspTests` are green; add the
property group; run CP-1 green; commit (`feat(CARD-0795): isolated bin-* builds compile
in-process (UseSharedCompilation=false via Directory.Build.props)`), push. Then run CP-2 and CP-3
against that commit and keep both `C795_MEASURE` lines for S2.

Besides its own rows this slice must run: `DirectoryBuildRspTests` (in CP-1's filter: the sibling
pin on the rsp, which this card must not loosen). CP-1's build of `tests/Antiphon.Tests` into
`bin-c795-a/` is itself the first full isolated build under the new rule; a build failure there
is the signal that the group is malformed.

### S2: the measurement record and the documentation (30-45 minutes)

Files: `docs/testing-and-build.md` (the three D-5 edits with the CP-2/CP-3 numbers);
`tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs` (one new method
`the_build_slots_section_states_isolated_builds_compile_in_process`, same `Collapse`/section
helpers as `the_build_slots_section_is_documented`, phrases `UseSharedCompilation=false`,
`Directory.Build.props`, `CARD-0795`, `bin-<name>/`, `csc`).

Order: add the test method; run CP-4 red; make the docs edits; run CP-4 green; commit
(`docs(CARD-0795): record in-process isolated compiles and the measured cost`), push.

Besides its own rows this slice must run: the whole `CheckpointManifestDocumentationTests` class
(seven existing section pins on the same file) and `CheckpointRepeatDocumentationTests` (pins the
paragraph two sections above the one edited). Both are in CP-4's filter. If Review's diff of
`docs/testing-and-build.md` reaches outside the three D-5 locations, also run
`ScopedVerificationInstructionTests`, `TaskPlatformGuidanceTests`, `GrokRunnerImageContractTests`
and `RunnerPushCredentialDocsTests`, which pin other sections of the same document.

## Verification design

All rows run on the Linux lane. CP-2 and CP-3 need the runner's `/build-slots` broker reachable
(the `C795_MEASURE` line records its occupancy) and about 150 MB of free disk under `.antiphon/`
for two `-v:d` logs.

### Coverage

| ID | Class.Method | Behaviour proven |
|---|---|---|
| V-1 | `DirectoryBuildPropsSharedCompilationTests.An_isolated_output_turns_shared_compilation_off` | Evaluating a real repo project with `--property:OutputPath=bin-c795-eval/` yields `UseSharedCompilation=false` and `AntiphonIsolatedOutput=true` through the real `Directory.Build.props` import. |
| V-2 | `DirectoryBuildPropsSharedCompilationTests.A_daemon_output_keeps_the_sdk_default` | Negative control for the marker: with no `OutputPath` the marker is empty and `UseSharedCompilation` is not `false` (the daemons', land verifier's and operators' builds keep the server). |
| V-3 | `DirectoryBuildPropsSharedCompilationTests.An_environment_value_wins_on_an_isolated_output` | Negative control for the guard: a child environment `UseSharedCompilation=true` with the isolated `OutputPath` evaluates to `true`; the command-line form is MSBuild global-property precedence and was measured at plan time. |
| V-4 | CP-2 command row (M-1) | A real isolated `--no-incremental` build of `tests/Antiphon.Tests` at the committed SHA logs at least one `using command line tool by design` and zero `server processed compilation`; its wall seconds and broker occupancy are recorded. |
| R-1 | `DirectoryBuildRspTests.*` (2) | The rsp still carries `-nodeReuse:false`, stays ASCII and carries no `-maxcpucount`; this card changes nothing about node reuse. |
| R-2 | CP-3 command row (M-2) | Control: the same build with `--property:UseSharedCompilation=true` logs at least one `server processed compilation` and zero `by design`, so the discriminator detects the server when it is used and the override works on a real build. |
| R-3 | `CheckpointManifestDocumentationTests.*` (7 existing) and `CheckpointRepeatDocumentationTests.*` (1) | Every existing pin on `docs/testing-and-build.md` sections survives the three additive edits. |
| V-5 | `CheckpointManifestDocumentationTests.the_build_slots_section_states_isolated_builds_compile_in_process` | The Build slots section names `UseSharedCompilation=false`, `Directory.Build.props`, `CARD-0795`, `bin-<name>/` and `csc`. |

### Positive controls (SourceLanding Mutation, method-scoped, one per behaviour)

Run only the detecting filter with `--treenode-filter "/*/*/Class/Method"`; restore before the
next. Zero tests, a build error or a missing SDK is not red.

| PC | Change to production behaviour | Detecting filter | Expected red |
|---|---|---|---|
| PC-1 | Delete the `UseSharedCompilation` element from the CARD-0795 group in `Directory.Build.props` | `/*/*/DirectoryBuildPropsSharedCompilationTests/An_isolated_output_turns_shared_compilation_off` | `UseSharedCompilation` evaluates empty instead of `false`. |
| PC-2 | Remove `'$(UseSharedCompilation)' == '' and ` from the element's condition | `/*/*/DirectoryBuildPropsSharedCompilationTests/An_environment_value_wins_on_an_isolated_output` | The environment's `true` is overwritten with `false`. |
| PC-3 | Replace the `AntiphonIsolatedOutput` condition with `'$(AntiphonIsolatedOutput)' == ''` (marker always true) | `/*/*/DirectoryBuildPropsSharedCompilationTests/A_daemon_output_keeps_the_sdk_default` | A build without `OutputPath` evaluates `false`. |
| PC-4 | Remove the CARD-0795 sentence from the Build slots paragraph | `/*/*/CheckpointManifestDocumentationTests/the_build_slots_section_states_isolated_builds_compile_in_process` | Section pin fails on `CARD-0795`. |

### Cost

Checkpoint floor is the `EstimatedMinutes` sum, 34 minutes, plus authoring of roughly 75-105
minutes across S1-S2. One checkpoint run per committed slice, each through the build-slot gate
with the exact committed SHA (the tool bootstrap build is the one explained unlisted build):

```powershell
$planPath = 'docs/superpowers/plans/2026-10-06-card-0795-isolated-build-shared-compilation-plan.md'
$sha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c795-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c795-tool/ --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c795-tool/ -- run --plan $planPath --rows CP-1 --expected-source-sha $sha --row-timeout 15m --total-timeout 30m
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c795-tool/ -- run --plan $planPath --rows CP-2,CP-3 --expected-source-sha $sha --row-timeout 30m --total-timeout 70m
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c795-tool/ -- run --plan $planPath --rows CP-4 --expected-source-sha $sha --row-timeout 15m --total-timeout 30m
```

If `run` exits 75, call `wait` until it does not; never edit source while a run is active; exit 4
from the gate is a slot timeout to report. CP-1's red-first pass is the same row run before the
property group exists and is reported as such. Delete every `bin-c795-*` directory (about a dozen
per build, one per project) and `.antiphon/c795/` before finishing; a green run deletes its own
`bin-<name>/` outputs. No assertion, count or timeout is loosened to pass; a red row is fixed and
rerun as the same row. The Code brief points at this table as
`checkpoints: <this path>@<plan commit sha> section "### Checkpoints"`.

### Checkpoints

Closed Code list: one isolated build and one literal TUnit filter per TUnit row, one exact shell
command per measured row (`/bin/sh -lc`; the tool leases the row, so the command carries no
`build-slot.ps1` and an explicit `-maxcpucount:4`). Counts are from source inspection at
`ed0f0d920` (`DirectoryBuildRspTests` 2, `CheckpointManifestDocumentationTests` 7,
`CheckpointRepeatDocumentationTests` 1); recount at Code admission. The final-SHA row set is the
whole table: the last slice's Review runs CP-1 through CP-4 at the final commit.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c795-a/` | props-evaluation-linux | `/*/*/(DirectoryBuildPropsSharedCompilationTests*)\|(DirectoryBuildRspTests*)/*` | V-1, V-2, V-3, R-1 | exact 5 methods (3 new + 2 rsp pins), 0 failed/skipped | 5 | 8 | true |
| CP-2 | S1 | n/a | measure-isolated-linux | `mkdir -p .antiphon/c795 && o=$(curl -s -m 5 "$ANTIPHON_BUILD_SLOTS_URL" 2>/dev/null) && s=$(date +%s); dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c795-m1/ --no-incremental -maxcpucount:4 -nologo -v:d > .antiphon/c795/m1.log 2>&1; b=$?; e=$(date +%s); k=$(grep -c "using command line tool by design" .antiphon/c795/m1.log); p=$(grep -c "server processed compilation" .antiphon/c795/m1.log); echo "C795_MEASURE mode=isolated build=$b seconds=$((e-s)) byDesign=$k serverProcessed=$p slots=$o"; test "$b" -eq 0 -a "$k" -ge 1 -a "$p" -eq 0` | V-4 | exit 0; one `C795_MEASURE mode=isolated build=0 seconds=<n> byDesign=<k> serverProcessed=0` line with k >= 1 | n/a | 10 | true |
| CP-3 | S1 | n/a | measure-shared-control-linux | `mkdir -p .antiphon/c795 && o=$(curl -s -m 5 "$ANTIPHON_BUILD_SLOTS_URL" 2>/dev/null) && s=$(date +%s); dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c795-m2/ --property:UseSharedCompilation=true --no-incremental -maxcpucount:4 -nologo -v:d > .antiphon/c795/m2.log 2>&1; b=$?; e=$(date +%s); k=$(grep -c "using command line tool by design" .antiphon/c795/m2.log); p=$(grep -c "server processed compilation" .antiphon/c795/m2.log); echo "C795_MEASURE mode=shared build=$b seconds=$((e-s)) byDesign=$k serverProcessed=$p slots=$o"; test "$b" -eq 0 -a "$k" -eq 0 -a "$p" -ge 1` | R-2 | exit 0; one `C795_MEASURE mode=shared build=0 seconds=<n> byDesign=0 serverProcessed=<k>` line with k >= 1 | n/a | 10 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c795-b/` | docs-pins-linux | `/*/*/(CheckpointManifestDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)/*` | V-5, R-3 | exact 9 methods (7 existing + the_build_slots_section_states_isolated_builds_compile_in_process + 1 repeat pin), 0 failed/skipped | 9 | 6 | true |

## Risks and notes for Code and Review

- **Memory shape.** CARD-0589 kept the server because in-process compilers peak higher; the
  outage's "18 csc holding 11.5 GB" was uncapped fan-out. Under the broker the worst case is
  4 x 6 concurrent `csc` on server2 (about 7-14 GB transient against a 16 GB floor and 104 GB
  available at plan time) and 2 x 4 on desktop. If `build_slot_memory_floor` refusals rise after
  land, the per-host `MaxCpuCount` is the lever before the rollback.
- **Log strings are SDK-pinned.** `using command line tool by design` and
  `server processed compilation` were read from SDK 10.0.401 at `-v:d`. If an SDK upgrade changes
  them, CP-3 (the control) goes red first; update both rows together from the new log, never
  one.
- **Comparability.** CP-2 and CP-3 are `Serial` within the run but other tasks' builds share the
  host. Report the `slots=` occupancy; rerun both if it differs by more than one lease between
  them, and keep the first `VBCSCompiler` start out of the comparison by running CP-3 twice when
  the first CP-3 is the one that started the server (its log shows `server - server failed` or a
  startup line).
- **Evaluation needs the SDK.** The test spawns `dotnet msbuild`; `global.json` (10.0.204, roll
  forward latest minor) resolves to 10.0.401 on both hosts today. A host without the SDK fails the
  test loudly rather than skipping.
- **Nothing else changes.** `Add-AntiphonMaxCpuCount`, `BuildStep.BuildArguments`,
  `run-checkpoint.ps1`, the broker and the rsp are untouched; a Review that finds the property on
  a command line in those files is looking at a different card.
- **After land.** The orchestrator can watch whether the card's five timing-test classes keep
  appearing as reds in checkpoint reports under load; that observation, not this card, settles
  the card's causal claim.
