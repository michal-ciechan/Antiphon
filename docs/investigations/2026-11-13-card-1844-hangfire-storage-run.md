# CARD-1844 Hangfire PostgreSQL storage verification

Status: build passed; test execution blocked by the supplied container restrictions.

## Subject and scope

- Task: dd593fff; Review: 85da972b, round 2, Interim.
- Tested source requested: `b82f4eccdd6b7e2a497bf614eef3910990122c97`.
- Assigned branch: `feat/card-task-dd593fff`, initially clean at that exact SHA;
  `origin/feat/card-task-0303dd11` also resolves to that SHA. No branch switch was needed.
- Exact requested selection: `/*/*/HangfirePostgreSqlStorageTests/*` in
  `tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs`.
- The class declares 22 `[Test]` methods (lines 314-871); two later methods
  belong to the separate `HangfireLeaseDocumentationTests` class.
- Evidence only: no production code, test code, fixture or configuration changes.

## Blocking source facts

The premise that this selection uses an externally supplied test PostgreSQL at
`127.0.0.1:54329` is incorrect at the requested SHA. That port occurs only in
`tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycleTests.cs`, in fake lifecycle
operations used by another class. It is not the live fixture connection.

`HangfirePostgreSqlStorageTests` calls `TestDbFixture.CreateIsolatedSchemaAsync()`.
`TestDbFixtureLifecycle.BootstrapAsync()` creates a Testcontainers container through
`TestDbOperations`, starts it and uses `Real.GetConnectionString()`. There is no
external connection-string override. The real builder uses `postgres:16-alpine`,
database `antiphon_test`, username `test` and the repository's disposable test
password. Individual tests use cloned databases from the assembly template.

`docker-compose.dev.yml` contains no `test-postgres` service. Its development
PostgreSQL service instead uses `postgres:16`, database/user `antiphon`, port
17280, a persistent volume and a different development credential. It cannot
be mirrored as the brief describes.

The real fixture starts an orphan sweep in `BootstrapAsync()` before container
creation. `TestDbContainerOwnership.SweepAsync()` may remove existing labelled
containers whose owner has exited, and its removal includes anonymous volumes.
The normal fixture creates a generated container name; baked-image lookup/baking
is enabled by default and may create another container and prune old images.
Disabling baking does not provide an external connection override, explicit
container name, tmpfs data directory or a way to disable the orphan sweep.

Consequently, starting the requested manual container would not supply this
selection's database. Running the unmodified selection would violate the brief's
one-container, explicit-name, tmpfs and no-existing-container/no-volume limits.
No unsupported fixture substitution or source patch was attempted.

## Read-only preflight commands

All commands ran in `/work/worktrees/task-dd593fff` on 2026-11-13 UTC.

```sh
git status --short --branch
git rev-parse HEAD
git rev-parse origin/feat/card-task-0303dd11
docker version
docker info --format '{{.Name}}'
hostname
docker ps -a --format '{{.Names}} {{.Image}} {{.Ports}}'
rg -n 'HangfirePostgreSqlStorageTests|54329|test-postgres' tests/Antiphon.Tests docker-compose.dev.yml
cat docker-compose.dev.yml
cat tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs
sed -n '1,170p' tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs
sed -n '465,585p' tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs
cat tests/Antiphon.Tests/TestHelpers/TestDbContainerOwnership.cs
cat tests/Antiphon.Tests/TestHelpers/TestDbBakedImages.cs
```

Docker client and server both answered, version 27.5.1. Docker daemon name and
process hostname both equalled `3ace63c74cb4`, consistent with the runner's own
nested daemon required by `docs/docker-stack.md`. No alternative Docker route,
host access or stack operation was used.

## Execution and container lifecycle

The following command completed in the foreground, with exit 0:

```sh
timeout --foreground --signal=INT --kill-after=30s 10m pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1844-hangfire-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo > .antiphon/hangfire-dd593fff/build.log 2>&1
```

The 10-minute outer build limit was a provisional bound for this one build,
not a change to any test timeout. Native default outputs were used; no output
override was passed. Build HEAD was
`f972b5da5dbd3010e4c26f6d91cebb2860246bc8`, whose only delta from the requested
subject was the initial version of this report. Production and test source
therefore matched the requested subject exactly.

Build result: **0 errors, 2,051 warnings**, elapsed **00:05:38.93**.
Warnings included nullable diagnostics, TUnit missing cancellation-token
diagnostics and ASP.NET route-analyzer `IndexOutOfRangeException` diagnostics
reported as AD0001 warnings. These did not fail the build; no warning repair
was attempted in this evidence-only pass.

Essential unedited slot provenance:

```text
BUILD SLOT granted lease=2acbf6e9-ba6d-4dab-a4f9-56ed6647cc67 waited=0s maxcpucount=6
BUILD SLOT released lease=2acbf6e9-ba6d-4dab-a4f9-56ed6647cc67 held=340s
```

Full ignored build log:
`/work/worktrees/task-dd593fff/.antiphon/hangfire-dd593fff/build.log`.
SHA-256: `298caf7332ea4bc0fc9172776fd10d26b38b76378ac0efbd94d4bd60c0d24050`.
The log is local generated evidence, not a pushed deliverable; the durable
build outcome and provenance are retained here.

Test selection not started: executed 0, passed 0, failed 0,
skipped 0; these are not a green result. No test failure text exists. A base
comparison is not applicable because no candidate test failure was observed.
No test runtime, per-test durations or database phase timings are available.

The intended class command below was **not executed** and remains conditional
on resolving the fixture/lifecycle restrictions; its 10-minute whole-class
limit is provisional, not a measured p95 or a completed test receipt:

```sh
timeout --foreground --signal=INT --kill-after=30s 10m pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1844-hangfire-class -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/HangfirePostgreSqlStorageTests/*'
```

No container was created, started, stopped or removed by this task. In
particular, `c-hangfire-test-85da972b` was never created; there is nothing owned
by this task to remove. Existing containers were only enumerated.

Final exact-name check returned exit 0 with no rows:

```sh
docker ps -a --filter name=^/c-hangfire-test-85da972b$ --format '{{.Names}} {{.Status}}'
```

Evidence-diff check over the subject-to-report range passed with no violations:

```sh
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef b82f4eccdd6b7e2a497bf614eef3910990122c97 -HeadRef HEAD
```

## Required continuation

Review 85da972b remains Interim; this pass supplies no land approval. The caller
must revise the execution brief to permit the actual fixture's lifecycle, or
commission a separate Code task for an external-store fixture mode that satisfies
the stated isolation and cleanup contract. Preserve the reviewed subject SHA and
run the exact class after that incompatibility is resolved.
