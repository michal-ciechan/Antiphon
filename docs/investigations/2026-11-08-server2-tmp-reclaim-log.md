# server2 /tmp reclaim log (2026-11-08)

Task 90f054d8 (Deploy), runner container `6f03ea7dd3f9`, uid 1654, 15:29-15:45 UTC. Operator authority:
"Delete stuff" (2026-11-08). Basis: Investigate c04ce26d note
(`docs/investigations/2026-11-08-server2-work-volume-full.md` on `feat/card-task-c04ce26d`).
The runner TempSweep stayed OFF; nothing was changed in settings. Only `rm -rf` of the named /tmp entries below.

## Free space (one LV, so /tmp and /work report the same)

| Point | Used | Avail |
|---|---:|---:|
| Start (15:29) | 515G | 162G |
| After c487-probe | | 228G |
| After other families | | 234G (239.6 GB right after the delete, then normal build activity) |

/tmp total after: 17.6 GB (du), 77,681 top-level entries (was ~90,745 at the family census).

Note: free space was already 162G at start (the c04ce26d note saw 3.2G); the earlier mirror reclaim (ed0f7942)
had run. c487-probe was 1486 dirs / 68.5 GiB at start, not 2503.

## Removed

| Family | Rule | Removed | Size |
|---|---|---:|---:|
| `c487-probe-*` (dirs, `/tmp` top level) | dir mtime > 120 min, no file inside written in the last 120 min, no holder | 1414 | 65.2 GiB (68,350,728 KB) |
| `antiphon-bridge-queue-<32hex>` | newest write > 48 h, runner-owned throughout, no symlink root, no holder | 8594 | 74 MB |
| `antiphon-land-verify-<32hex>` | same | 340 | 566 MB |
| pty-test-root (`RunnerTempSweep` regex: c1653-sock/peer/adopt/launch, c1636-adopt, c79-stop, adoption-tests, card0395-*, c514-gen/seq/gate/dest, c502-v2x, c710-platform, ...) | same | 4159 | 8.1 GB |
| **Total** | | **18,507** | **~73.9 GiB** |

Family names matched exactly the `RunnerTempSweep.RegisteredFamilies` regexes (`src/Antiphon.SessionRunner/RunnerTempSweep.cs`).
Every listed path was verified gone afterwards (0 remaining).

Holder check: `/proc/*/cwd`, `/proc/*/fd/*`, `/proc/*/maps` and `/proc/net/unix` bound paths, before deletion.
Held /tmp entries were only `antiphon-pty-*` (live pty hosts), `antiphon-pty-hosts`, `claude-1654`, `cc-socks`,
`MSBuild257xx`, `antiphon-c1199-reader-*`, `c1780-watch-checkpoint.sh`, a CoreFxPipe and one random-named socket.
None was in a deleted family. Note: processes in other containers/host are not visible from here.

## Left in place

| What | n | Size | Why |
|---|---:|---:|---|
| `c487-probe-*` younger than 120 min | 72 | 3.4 GB | floor protects running tests; leak continues until Debug ee315192 lands |
| `antiphon-land-verify-*` < 48 h | 212 | 846 MB | age |
| `antiphon-bridge-queue-*` < 48 h | 1139 | 10 MB | age |
| pty-test-root < 48 h | 13 | 121 MB | age |
| `claude-1654` (CLI scratchpads) | 1 | 7.7 GB | live holder; not a named family |
| `antiphon-pty-*`, `antiphon-pty-hosts` | | small | live pty hosts, denied |

## Unexpected / unknown (listed, not deleted)

| Path | Size | mtime |
|---|---:|---|
| `/tmp/antiphon-master-8592d536` | 1.9 GB | 2026-11-05 |
| `/tmp/card0395` (416 subdirs) | 1.2 GB | 2026-11-06 |
| `/tmp/node-compile-cache` | 292 MB | (denied by sweep) |
| `/tmp/c1629-plain-one` | 220 MB | 2026-11-02 |
| `/tmp/dotnet-tools` | 127 MB | |
| `/tmp/mut-613f2230` | 86 MB | 2026-11-05 |
| `/tmp/antiphon-outbound-zip-budget-*` | 64 MB | |
| `/tmp/card-0824-e438ae14` | 50 MB | 2026-11-05 |
| `c487-tw*` (unregistered) | 2529 entries, 25 MB | |
| `rcspeed*` (registered, not in this brief) | 298, 31 MB | |
| `clr-debug-pipe-*` / `dotnet-diagnostic-*` | 32,849 / 16,413 | fifos/sockets, ~0 bytes, inode noise |
