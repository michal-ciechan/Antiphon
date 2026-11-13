# CARD-1844 Hangfire PostgreSQL storage verification

Status: execution blocked by the supplied container restrictions; build pending.

## Subject and scope

- Task: dd593fff; Review: 85da972b, round 2, Interim.
- Tested source requested: `b82f4eccdd6b7e2a497bf614eef3910990122c97`.
- Assigned branch: `feat/card-task-dd593fff`, initially clean at that exact SHA;
  `origin/feat/card-task-0303dd11` also resolves to that SHA. No branch switch was needed.
- Exact requested selection: `/*/*/HangfirePostgreSqlStorageTests/*` in
  `tests/Antiphon.Tests/Infrastructure/HangfireDurableStorageTests.cs`.
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

Build pending. Test selection not started: executed 0, passed 0, failed 0,
skipped 0; these are not a green result. No test failure text exists. A base
comparison is not applicable because no candidate test failure was observed.

No container was created, started, stopped or removed by this task. In
particular, `c-hangfire-test-85da972b` was never created; there is nothing owned
by this task to remove. Existing containers were only enumerated.

## Required continuation

Review 85da972b remains Interim; this pass supplies no land approval. The caller
must revise the execution brief to permit the actual fixture's lifecycle, or
commission a separate Code task for an external-store fixture mode that satisfies
the stated isolation and cleanup contract. Preserve the reviewed subject SHA and
run the exact class after that incompatibility is resolved.
