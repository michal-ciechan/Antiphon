# Release candidate 2026-10-15 defects and incomplete gates

Candidate source: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
No product/test fixes were made. This file is the caller's repair fan-out input.
Results below are observations, with causal source attribution distinguished from
historical execution. E2E results will be added after its separate attempt.

## D-1 — Checkpoint tool dependency contract assertion

- Step/class: Linux roster; `Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests`.
- Test: `csproj_has_no_project_references`, source line 22.
- Exact assertion message:

```text
ShouldAssertException: DockerStackDocuments.Read("tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj")
    should not contain (case insensitive comparison)
"<ProjectReference"
    but was actually
"<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFra..."
```

- Actual offending input: `<ProjectReference Include="../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj" />`.
- Classification: **regression in the retained contract test / architectural contract mismatch**, reproduced once in class isolation; not a demonstrated product runtime failure or a flake. Full roster: 4,515 pass / 1 fail / 0 skip. Isolated class: 3 pass / 1 fail / 0 skip, same assertion, exit 2, 24.027s outer wall including 15s slot wait.
- Introducing input commit: `367c8ee4cdcc877aa71f1a46d30c410d628edc3b`, CARD-0987 S1a0. Git blame and its patch identify the new reference; the morning baseline lacks it. The refresh plan explicitly calls for a one-way tool-to-HostCleanup reference. This is source attribution, not a historical checkout execution or bisection.
- Smallest proposed fix: reconcile the test with the approved one-way dependency, asserting the exact permitted HostCleanup reference and preventing unintended graph expansion. Preserve the required dependency; verify tool packaging/installability if the old no-reference contract was protecting standalone distribution. Do this in a separately commissioned Code task, then ordinary Review/land and a new candidate.
- Evidence: `/work/evidence/task-1c14bf28/raw/roster/run.trx`, `/work/evidence/task-1c14bf28/raw/rerun-project-contract/run.trx`, and `project-reference-introduction.patch` under the same raw root.

## D-2 — Stack smoke cannot enumerate Linux listeners

- Step: `pwsh -NoProfile -File verify-dev-stack.ps1 -SkipBrowser`, through a granted host slot.
- Class: script/platform environment; `verify-dev-stack.ps1:79`.
- Exact error: `The term 'Get-NetTCPConnection' is not recognized as a name of a cmdlet, function, script file, or executable program. Check the spelling of the name, or if a path was included, verify that the path is correct and try again.`
- Classification: **environment / Windows-only smoke driver on native Debian**, exit 1 after 4.120s outer wall. No product regression is established.
- Introducing commit/card: pending precise line attribution; this is an inherited script prerequisite, not an observed change introduced by this Deploy pass.
- Smallest proposed fix: a separately commissioned portable listener probe and explicit isolated endpoint parameters, retaining the Windows path. Without that work, run this smoke on its intended Windows stack and use isolated E2E for Linux application checks.
- Fixed production ports are hard-coded and there is no isolated-port option. No shared production stack was started, stopped or restarted. The HTTP-health portion was not reached and remains incomplete.
- Evidence: `/work/evidence/task-1c14bf28/raw/smoke/console.log` and `result.json`.

## D-3 — Stack smoke expects a dev Postgres container absent here

- Step/check: stack smoke, `Postgres container`.
- Exact result: `Postgres container           FAIL  not Up — check 'docker ps -a --filter name=antiphon-postgres'`.
- Classification: **environment**, not a demonstrated product regression. Docker daemon and owned disposable-network checks passed. The host's testcontainers do not supply the specifically named Aspire dev container.
- Introducing commit/card: no causal product commit established; this host does not run the documented Windows Aspire dev stack.
- Smallest proposed fix: make an isolated smoke accept explicit owned stack endpoints/resources, or collect the intended Windows-stack receipt. Do not create or recycle the shared production stack for this task.
- Evidence: the same smoke console. This failure was observed before D-2 terminated the script; neither is a passing stack-smoke receipt.
