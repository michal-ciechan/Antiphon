# Who creates the six-character empty directories in server2 /tmp (CARD-1981, 2026-11-18)

Task 19e310c5, Investigate, READ-ONLY. Master read: `56e1db3052d19266c6a24f8d228f58612a72f421`. Follows
[the /tmp census](2026-11-17-server2-tmp-census.md) (producer **UNOBSERVED**) and the CARD-1981 round 1 review
(`docs/reviews/2026-11-18-card-1981-owned-temp-producers-review-r1.md` at `473b4efa4b577b7eae223dca8032117a1437030c`), which
counted 62 new ones in an hour. Observed 20:02Z-20:25Z UTC from inside the runner container (hostname `7c9c24dac62d`, uid 1654
`app`). Nothing in `/tmp` was deleted, moved or changed; no container was stopped; nothing was installed.

## Outcome

**Confirmed and reproduced: the .NET 10 SDK CLI (`/usr/share/dotnet/sdk/10.0.404/dotnet.dll`) creates one empty `$TMPDIR/XXXXXX`
directory (mode 0700) on every `dotnet build`, `dotnet publish`, `dotnet test`, `dotnet pack` and `dotnet run` that builds, and never
removes it.** The path is the background workload-update advertising check:

```
RestoringCommand..ctor                       (every build-type command; Task.Run with no condition in front of it)
  -> Task.Run(<>c__DisplayClass6_0.<.ctor>b__0)
    -> WorkloadManifestUpdater.BackgroundUpdateAdvertisingManifestsAsync(userProfileDir)
      -> WorkloadManifestUpdater.GetInstance(...)
           tempPackagesDir = new DirectoryPath(TemporaryDirectory.CreateSubdirectory())    // InternalAbstractions
             -> System.IO.Directory.CreateTempSubdirectory()   // mkdtemp("$TMPDIR/XXXXXX"), 0700
      -> BackgroundUpdateAdvertisingManifestsWhenRequiredAsync()   // the 24 h / opt-out gate is HERE, after the directory exists
```

The directory is the scratch download folder of a `NuGetPackageDownloader` that only downloads when an advertising-manifest update is due.
When nothing is downloaded, which is every observed case, the folder stays empty and the CLI exits without deleting it.

- **Which /tmp:** the runner container's `/tmp`, which is the named volume `antiphon-runner_runner-tmp`
  (`/proc/self/mountinfo`: `/var/lib/docker/volumes/antiphon-runner_runner-tmp/_data /tmp`). The producer is any process in that
  container that runs a building `dotnet` command with `TMPDIR=/tmp` (the container default). The nested dind daemon runs nothing
  (`docker ps -a`: two containers, exited 11 days and 2 weeks ago). The server2 **host** `/tmp` is **UNOBSERVED**: the host is not
  reachable from the container (the census got `Permission denied (publickey)`).
- **Cadence:** there is none. The directories follow build activity, not a clock (no poller, cron, healthcheck or Windmill job).
  Each one appears 0.9-1.0 s after a `dotnet build` process starts, before MSBuild spawns its worker nodes. The hourly counts for the last
  24 h follow the agents' working hours: 1-3 an hour at 23Z-04Z, 27-40 an hour at 06Z-10Z, 52 at 19Z.
- **Not the producer:** `dotnet restore`, `dotnet clean`, `dotnet msbuild` (with and without `-restore`), `dotnet run --no-build`,
  `dotnet exec`, pwsh, the test hosts and the checkpoint tool's own `hold`/`execute` processes. **The CARD-1981 Code change cannot move
  this rate**, because no repository file creates these directories.
- **No opt-out:** `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true` or `=1` still creates the directory (the variable is read after
  `GetInstance`). `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `-tl:off` make no difference. **Only `TMPDIR` moves it**: the SDK honours `TMPDIR`
  (each of the 9 creating runs below put it in its own TMPDIR, none in `/tmp`).

## Evidence

### 1. Shape of the debris (read-only listing)

`find /tmp -mindepth 1 -maxdepth 1 -type d -regextype posix-extended -regex '/tmp/[A-Za-z0-9]{6}'` at 20:02Z found **2,465** entries, every
one uid/gid 1654 (`app`) and mode `drwx------`, with birth time equal to mtime (`stat %w`). 2,455 are empty. The ten that are not empty:

| Name(s) | Content | Explanation |
|---|---|---|
| `census`, `triage`, `ildump`, `c1514d/e/f` | agent work folders (`board.json`, `Program.cs`, `c1192.sh`, ...) | named by agents, not random |
| `dox631` (10-30), `ov6xeb` (11-03), `vfB3uM` (11-07), `QuAH8D` (11-14) | one 1,061-byte `project.assets.json` for the `dotnet-ef/9.0.20` **tool** package, `net10.0/linux-x64` | a second, rare SDK producer through the same helper: `ProjectToolsCommandResolver.GenerateDepsJsonFile` (local `dotnet ef` tool resolution) also calls `TemporaryDirectory.CreateSubdirectory`. 4 in 20 days; not observed live |

Count by day of creation: 10-30 1, 11-03 1, 11-07 1, 11-10 2, 11-14 85, 11-15 425, 11-16 461, 11-17 1,146, 11-18 343 (up to 20:02Z).
The census counted 21,579 such directories on 2026-11-17 (15,683 older than 7 days). Today almost none older than 11-14 remain, so
something removed about 19,000 of them between 11-17 20:30Z and 11-18 20:02Z. **This note did not investigate that removal.** The
container was recreated in that window (`~/.dotnet/10.0.404.dotnetFirstUseSentinel` is from 2026-11-18 04:54Z; census hostname
`d0d4c9a390d9`, today `7c9c24dac62d`), but the volume is the same.

### 2. Live correlation (inotify, 9.5 minutes)

`inotifywait`, `strace` and `auditctl` are not installed. Node 22's `fs.watch('/tmp')` uses inotify, so a watcher
(`scratchpad/watch.js`) recorded `IN_CREATE`/`IN_MOVED_TO` of every `[A-Za-z0-9]{6}` name. On each event it read `/proc/*/stat`,
`cmdline` and `cwd` and listed processes younger than 30 s. Every 1 s it also logged every new process. It ran from
**20:03:36.143Z to 20:13:06.146Z** and then exited on its own timer (sampling stopped; `pgrep` afterwards found no watcher). The log
was written only under the task scratchpad (`/tmp/claude-1654/-work-worktrees-ws-10-d2/<session>/scratchpad/watch1.log`), not to the
top level of `/tmp`.

| Event (UTC) | Name | Youngest process (age at event) |
|---|---|---|
| 20:04:26.285 | `bD2i99` | `dotnet build tests/Antiphon.Tests --property:OutputPath=.../bin-c1893s1/ ...` (ws-05, checkpoint row build), 0.9 s |
| 20:08:49.555 | `e7oEVd` | `/usr/bin/dotnet build tools/Antiphon.LandGraphAudit/...csproj -c Release ...` (ws-01-d4, `audit-land-verification-graph.ps1 -Check` under `build-slot.ps1`), 1.0 s |
| 20:11:20.004 | `B3vAV7` | `/usr/bin/dotnet build scripts/regenerate-derived-impact-index/...csproj -p:UseAppHost=false` (ws-01-d4, under `build-slot.ps1`), 0.9 s |

3 events, 3 `dotnet build` starts, matched one to one. In the same window, these produced **no** event: 36 `dotnet msbuild <proj>
-getItem:ProjectReference` (the checkpoint tool's reference walk, 20:03:41-20:04:21), 9 `dotnet run --project tests/Antiphon.Tests
--no-build`, 9 `dotnet exec .../Antiphon.Tests.dll`, 3 `dotnet run --project .../Antiphon.DerivedImpactIndex.csproj --no-build`
(20:12:07-20:12:15) and the checkpoint `hold` processes.
Outside the window: `3EoEOU` at 20:03:04 (before the start), and `oB5iCf` at 20:13:06 and `0rOorQ` at 20:13:21 (other agents, after the stop).

### 3. Reproduction with an owned TMPDIR (nothing written to /tmp's top level)

In the scratchpad there were two throwaway projects (`Probe.csproj`, a net10.0 library, and `ProbeX.csproj`, a net10.0 exe). Each command
ran with `TMPDIR=<scratchpad>/t<X>` (a fresh empty directory) under `pwsh -NoProfile -File scripts/build-slot.ps1 -Label tmp-probe-<X> -- ...`.
All 14 runs exited 0.

| Run | Command | New entries in its TMPDIR |
|---|---|---|
| A | `dotnet build Probe.csproj --nologo` | `Sw0uxK` (d, 700, empty) |
| B | `dotnet build Probe.csproj --no-restore --nologo` | `Xwnezj` |
| C | `dotnet restore Probe.csproj` | none |
| D | `dotnet msbuild Probe.csproj -t:Build -nologo` | none |
| E | `dotnet msbuild Probe.csproj -restore -t:Build -nologo` | none |
| F | A with `DOTNET_CLI_TELEMETRY_OPTOUT=1` | `cig1M6` |
| G | A with `-tl:off` | `xTdxLG` |
| H | `dotnet publish Probe.csproj --nologo` | `5YntfO` |
| I | `dotnet clean Probe.csproj --nologo` | none |
| J | `dotnet run --project ProbeX.csproj` | `dleRZt` |
| K | `dotnet run --project ProbeX.csproj --no-build` | none |
| L | `dotnet test ProbeX.csproj --nologo` | `SVWQPo` |
| M | A with `env DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true` | `KH5g9i` |
| N | A with `env DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1` | `EkrVKU` |

The split matches the SDK's `RestoringCommand` users exactly. The IL scan below lists `RestoringCommand..ctor` callers:
`BuildCommand.FromParseResult`, `PublishCommand..ctor`, `PackCommand..ctor`, `TestCommand..ctor`,
`Test.MSBuildUtility.BuildOrRestoreProjectOrSolution` and `RunCommand.EnsureProjectIsBuilt`. `restore`, `clean` and `msbuild` are plain
MSBuild forwarders, and `run --no-build` skips `EnsureProjectIsBuilt`.

### 4. Code path (IL scan of the shipped SDK, no decompiler)

A pwsh script (`System.Reflection.Metadata`; `scratchpad/callers.ps1`, `callers2.ps1`, `calls.ps1`, `params.ps1`) scanned method
bodies for `call`/`callvirt`/`newobj` tokens in `/usr/share/dotnet/sdk/10.0.404` (`dotnet --version` 10.0.404, the only SDK):

- `Directory.CreateTempSubdirectory` is referenced by `Microsoft.DotNet.InternalAbstractions.TemporaryDirectory::CreateSubdirectory`,
  by `Microsoft.Build.Framework.FileUtilities::CreateFolderUnderTemp` (the separate `MSBuildTemp<rnd6>` family) and by
  `System.CodeDom...TempFileCollection::GetTempDirectory`.
- In `dotnet.dll`, `TemporaryDirectory::CreateSubdirectory` callers: `WorkloadManifestUpdater::GetInstance`,
  `ProjectToolsCommandResolver::GenerateDepsJsonFile`, `ToolInstallGlobalOrToolPathCommand..ctor`, `WorkloadCommandBase..ctor`,
  `WorkloadIntegrityChecker::RunFirstUseCheck` (first use only), `FileBasedInstaller..ctor`, `NetSdkMsiInstallerClient::Create`,
  `SudoEnvironmentDirectoryOverride` and `ExtractManifestAsync`.
- `GetInstance` call sequence: `SdkDirectoryWorkloadManifestProvider..ctor`, `WorkloadResolver.Create`,
  **`TemporaryDirectory.CreateSubdirectory`**, `DirectoryPath..ctor`, `NuGetPackageDownloader..ctor`, ..., `WorkloadManifestUpdater..ctor`.
  It has no `Delete`.
- `GetInstance` has one caller, `<BackgroundUpdateAdvertisingManifestsAsync>d__14::MoveNext`, which calls `GetInstance` and then
  `BackgroundUpdateAdvertisingManifestsWhenRequiredAsync`. That method's only caller is `RestoringCommand/<>c__DisplayClass6_0::<.ctor>b__0`.
- `RestoringCommand..ctor(msbuildArgs, noRestore, msbuildPath, userProfileDir, advertiseWorkloadUpdates)` IL begins:
  `ldloc.0; ldftn b__0; newobj Func<Task>; call Task.Run; pop`, with **no branch before it**. `advertiseWorkloadUpdates` is read only
  afterwards, for the foreground `AdvertiseWorkloadUpdates` in `Execute`. So no argument or environment variable can stop the directory
  for a build-type command (runs M and N confirm this).
- `~/.dotnet` holds no workload-advertising sentinel, so the "when required" check probably finds an update due on every build and
  then finds nothing to download. The directory is created either way, so this does not change the conclusion.

## Producers inside Antiphon (who runs `dotnet build` with TMPDIR=/tmp)

Any agent shell, script or test in the runner container that runs a building `dotnet` command. Seen in this window: checkpoint row builds
(`tools/Antiphon.Checkpoints` building `tests/Antiphon.Tests`), `scripts/audit-land-verification-graph.ps1 -Check` (builds
`tools/Antiphon.LandGraphAudit`) and `scripts/regenerate-derived-impact-index` under `scripts/build-slot.ps1`. Not seen live but the same
mechanism: agent `dotnet build`/`dotnet run` without `--no-build`, land verification builds, and tests that shell out to `dotnet build`.
Neither `scripts/build-slot.ps1` nor `src/Antiphon.SessionRunner` sets `TMPDIR` anywhere (`grep TMPDIR` finds nothing), so every
session inherits the container's `TMPDIR=/tmp`.

## Recommended fix (card text; not filed)

> **CARD: give building dotnet commands an owned TMPDIR, and sweep the SDK's empty six-character directories (CARD-1981 slice 1).**
> Root cause (docs/investigations/2026-11-18-tmp-six-char-producer.md): the .NET 10 SDK's `RestoringCommand` constructor always runs
> `WorkloadManifestUpdater.GetInstance`, which calls `Directory.CreateTempSubdirectory()` (an empty `$TMPDIR/XXXXXX`, 0700) and never
> deletes it. Every `dotnet build|publish|pack|test|run` (without `--no-build`) leaves one. That is 1-52 an hour on server2, and no SDK
> switch turns it off; only `TMPDIR` moves it.
> 1. **Stopgap (runner sweep, small):** register an SDK family in `src/Antiphon.SessionRunner/RunnerTempSweep.cs`
>    `RegisteredFamilies`, id `dotnet-sdk-empty`, regex `^[A-Za-z0-9]{6}\z`. Like `claude-scratch`, it has no repository producer pin
>    (producer = SDK 10.0.404 `WorkloadManifestUpdater.GetInstance`). Eligibility must be stricter than the other families: a directory,
>    mode exactly 0700, owner 1654, **zero entries** and older than 1 h. Any content keeps it, which also protects the agent-named
>    `census`/`triage`/`ildump` folders. This reclaims the 2,455 existing empty ones and the steady flow when the sweep is enabled.
> 2. **Structural (owned scratch root):** set `TMPDIR` for every delegate session the runner launches to a short owned per-session root,
>    e.g. `/tmp/s/<8hex>`. Keep it under about 60 characters, because .NET puts `dotnet-diagnostic-<pid>-<n>-socket` Unix sockets in
>    `TMPDIR` and `sun_path` is 108 bytes. The runner removes that root when it releases the session. The same move also captures the
>    `clr-debug-pipe-*`/`dotnet-diagnostic-*` triples and `MSBuildTemp*`, which come from the same process tree. Check first: Claude Code's
>    scratchpad (`/tmp/claude-1654/...`, the `claude-scratch` family) and the tests that read `Path.GetTempPath()` would move with it.
>    Decide whether that is wanted before choosing per-session over a narrower `scripts/build-slot.ps1` per-lease `TMPDIR`. The per-lease
>    option covers leased builds and test runs only, not agent shells.
> 3. Optional upstream: report to dotnet/sdk that `WorkloadManifestUpdater.GetInstance` leaks its `tempPackagesDir` on every
>    build-type command.
> Witness: the hourly rate `find /tmp -maxdepth 1 -type d -regextype posix-extended -regex '/tmp/[A-Za-z0-9]{6}' -mmin -60 | wc -l` goes to
> zero after (2), or the empty ones older than 1 h go to zero after (1) with the sweep enabled. A unit witness for (1) is a family-table
> row test: an empty 0700 directory qualifies; one with a file, or with mode 0755, is kept.

## Remaining uncertainties

- The server2 host `/tmp` was not observed (no host access). This finding covers the runner volume, which is the `/tmp` the census and the
  review measured.
- The rare `project.assets.json` variant (`dotnet-ef` tool resolution) is attributed by code path and content, not seen live.
- Who removed about 19,000 six-character directories between the census and today is not known; this note did not investigate it.
- The run F environment prefix was applied through a bash function. Runs M and N used `env` explicitly. Telemetry is irrelevant to the
  code path either way.

## Side effects of this investigation

No change to `/tmp`'s top level from this task. Scratch files (watcher log, the probe projects and their `bin/obj`, 14 per-run TMPDIRs
holding the 9 reproduced directories) are all inside this session's Claude scratchpad
`/tmp/claude-1654/-work-worktrees-ws-10-d2/bcfd50a5-.../scratchpad`. There were 14 build-slot leases, each a few seconds long. The pwsh
processes that ran the IL scans used the default `TMPDIR=/tmp` and exited normally, so their runtime IPC files were removed.

## Not done, noted

- Fix idea (one line, per Investigate rules): the stopgap sweep family plus a per-session owned `TMPDIR` set by the runner, as in the card text above.
