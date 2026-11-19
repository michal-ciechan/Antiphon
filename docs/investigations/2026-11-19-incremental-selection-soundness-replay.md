# Incremental selection soundness replay (2026-11-19)

Twelve historical rc failures, one per failure family, from qualification runs
`6591e378`, `d88de419`, `0b292fa4`, `97150a0b`, `34d41b7e`, `f2de5b30`, and
`a6278259`. Each introducing commit is the one named in the why-missed note.
The path list is that commit's `git diff-tree --name-only`. Nothing here checks
out or builds those commits. The witness is
`tests/Antiphon.Tests/Scripts/IncrementalSelectionSoundnessTests.cs` over
`tests/fixtures/incremental-selection-soundness.json`.

Release selection still forces Full and does not call the selector. The two
measurements are pure functions over the stored paths.

## Current owner map

`scripts/lib/rc-input-owners.json` has one root, `**`, consumed by `*`. Every
changed path selects every suite. All twelve cases are selected. The test reads
that file, so a later map that drops a case fails until the fixture is updated.
Known-unsound for this map is empty.

## Proposed project closure

Not the S3 map, and not built into the owner file. A suite is selected when any
changed path is under the suite csproj or a project reached from it by
`ProjectReference` (the suite itself included, transitive, every `Include`,
conditions kept, no `Directory.Build` import walk), or the changed file name is
a build-graph root: `Directory.Build.*`, `Directory.Packages.props`,
`global.json`, `NuGet.config`, `packages.lock.json`, `*.sln`, `*.slnx`.
Linked `Compile` items are not edges. A missing reference target fails the test.

Eleven cases stay selected. One is missed.

| Case | Run | Suite | Closure |
|---|---|---|---|
| `d88de419-platform-taxonomy` | d88de419 | Antiphon.Tests | selected, own tree |
| `6591e378-composer-probe` | 6591e378 | Agents.Pty.Tests | selected, Pty sources; server paths in the same commit are outside this closure |
| `6591e378-e2e-land` | 6591e378 | E2E | selected, server reference |
| `97150a0b-checkpoint-source` | 97150a0b | Antiphon.Tests | selected, own tree; the plan markdown is not a build-graph root |
| `34d41b7e-step-deadline` | 34d41b7e | Antiphon.Tests | selected, sibling checkpoint files; the failing class file is not in the commit |
| `34d41b7e-linux-compiler` | 34d41b7e | Antiphon.Tests | selected by the test file. `scripts/lib/BuildDriverEnvironment.cs` is outside every referenced directory and is not a build-graph root |
| `0b292fa4-cardfile` | 0b292fa4 | Antiphon.Tests | selected, server and sibling tests; the failing class file is not in the commit |
| `d88de419-turn-end` | d88de419 | Antiphon.Tests | selected, server reference. The note names `7c6af459fd31e20ffd2529394b9c112f4caebc41`, which is not an ancestor of this master tip |
| `f2de5b30-runner-budget` | f2de5b30 | SessionRunner.Tests | selected, sibling test file |
| `97150a0b-c1514-sampler` | 97150a0b | SessionRunner.Tests | selected, `HostStatsSamplerService.cs` only. The note names `1380bb4ada387a22e11361de13b57ecd06be3aae`, replayed on master as `4e2b755d6264384cce91452d8f204f75975ef196`; the introducing object is not an ancestor of this tip |
| `a6278259-spawn-lane` | a6278259 | Antiphon.Tests | selected, own tree. The Windows miss was a platform skip |
| `f2de5b30-resilience-mirror` | f2de5b30 | Resilience.Tests | **missed** |

## Known-unsound

`knownUnsound` is exactly `f2de5b30-resilience-mirror`.

Commit `2c1b05349067731304b6a237e5f06511615675e5` changes only
`tests/Antiphon.Tests/TestHelpers/TestDbFailureEvidence.cs` and
`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs`.
`tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj` compiles
those files with `Compile Include` and does not `ProjectReference`
`tests/Antiphon.Tests/Antiphon.Tests.csproj`. The same path list selects
Antiphon.Tests, which owns the files. The rc failure was the Resilience.Tests
compile, so the recorded suite is the mirror project. The current owner map
still selects it.

That is the soundness risk of using this project closure as an execution map:
a linked helper outside the referenced projects is invisible. The other eleven
introducing commits also touched the suite tree or a referenced project, so
they do not show a second miss. A commit that changed only
`scripts/lib/BuildDriverEnvironment.cs` would miss `LinuxSharedCompilerPolicyTests`
under this same rule; the named introducing commit also changed the test file,
so the recorded case stays selected.

The test fails when a computed status moves, whether the miss disappears or a
new one appears, until `knownUnsound` is edited to match.
