# Review 6d8dd96a — round 1 of 3 (full): baked test Postgres runs from a volume (EINTR fix)

Subject: origin/feat/card-task-b1a8061c tip 06fe39d299cf6ae7dacffac10077bca68cf3310e
(base e7a18676c91899374bc3fc1361bee7bcf9ea4910; red witness aa8490dd6, fix 06fe39d29). Verdict: **Clean**, no D-n.

## Runs (all on server2 Linux, through build-slot, native output, UseAppHost=false, TEMP=/tmp, tested SHA 06fe39d29)

| Selection | Result |
|---|---|
| Build tests/Antiphon.Tests (Debug) | 0 errors |
| 7 TestDb* classes (DataDirectory, BakedImageLifecycle, ClonePool, FixtureIsolation, LazyInitialization, FixtureLifecycle, SchemaCacheLifecycle) | 78 total, 75 passed, 3 skipped (Child_* probe children), 0 failed, 2m16s |
| BootStallAttentionTests + AgentTaskCatchUpSettlementTests | 13/13 passed, 22 s; fixture took the image path: `C1420 schema-cache image key=a7027ac0… ms=4212` |
| WindowsUnitPlatformAdmissionTests C1210_* | 6 total, 5 passed, 1 skipped (C1210_Windows_linux_only_outcomes_are_exact, Windows-only), 0 failed |
| Mutant: `RunFromVolume` call disabled (throwaway, restored with git checkout, rebuilt) | witness RED: `data_directory /pgdata is on mount / (overlay)`, 1 failed |
| check-evidence-diff.ps1 -BaseRef e7a18676c -HeadRef HEAD | commits=2 violations=0 |
| merge-tree onto origin/master e7a18676c / origin/rc/2026-11-03 919d8d42f | clean / clean |

## Verification points

1. **Wrapper quoting (Windows).** The wrapper is not passed through the docker CLI: `WithEntrypoint("sh","-c",script,"sh")` goes through Testcontainers/Docker.DotNet as a JSON exec-form array over the engine API (npipe on Docker Desktop), so neither cmd.exe nor CreateProcess quoting touches it. `$PGDATA`/`$@` are expanded only by the container's `sh`. The literal is single-line with no CR. The Testcontainers Cmd (`-c fsync=off … -c max_connections=N`) becomes `$@`. docker-entrypoint.sh prepends `postgres` because the first argument starts with `-`.
2. **Copy-once / restart.** The condition is `[ -s "$PGDATA/PG_VERSION" ] || cp -a /pgdata/. "$PGDATA"/ || exit 1`. In a direct docker probe I started the cached real image with the identical script, wrote a row, ran CHECKPOINT and then `docker restart`. The row survived and nothing was re-copied. Data dir: mode 700, owner postgres, 82.4 M, ext4 volume (not overlay). The entrypoint also chmods/chowns $PGDATA as root.
3. **Loud failure.** With the source path broken (`cp` of a missing dir) the container exits 1 before Postgres starts. Testcontainers 4.16's default `WaitStrategyMode.Running` throws ContainerNotRunningException with the logs, so the fixture fails instead of hanging.
4. **Cost.** Ready-to-pg_isready measured ×3: wrapped 1229/1348/1424 ms against unwrapped 796/804/858 ms. That is about +0.5 s per started container. No timeout was widened.
5. **Volume lifecycle.** The base VOLUME is inherited by the commit (`Config.Volumes {/var/lib/postgresql/data}`) and is empty at create, so every container gets a fresh anonymous volume. Teardown (`DisposeOwnedAsync` → `RemoveAsync`) and the owner sweep both use `RemoveVolumes = true`, and `.Own()` overrides the image's `baked-image` owner label with the live owner, so the sweep catches orphans. Probe: `docker rm -f -v` removed the volume. After the runs there were no leftover labelled containers and no c1685 images.
6. **Stock path / off.** Unchanged. Only a non-`postgres:16-alpine` image is wrapped, and with `ANTIPHON_TEST_PG_IMAGE=off` no baked image is ever requested. Schema-key image hit verified live (above).

## Advisories (non-blocking)

- A-1: If `cp` fails part-way after PG_VERSION has been copied and the same container is later restarted, the restart skips the copy and starts on a partial dir. The fixture never restarts containers (no restart policy), so this is theoretical. A completion marker written after `cp` would close it.
- A-2: The witness's throwaway tag `antiphon-test-pg:c1685-witness-*` counts towards another process's concurrent `PruneAsync` keep-4 window. At worst one real tag is evicted early, which costs a re-bake.
- A-3: The docs could record the measured ~+0.5 s start cost (test-performance standards).

Not run: full Unit lane / other integration classes — the brief named this selection (Final profile scope as commissioned). PCs stay pending for SourceLanding Mutation.
