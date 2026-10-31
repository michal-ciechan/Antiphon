# Land verification slowdown: desktop log check (2026-10-31)

Investigate task cb60ed48 (desktop, read-only). It follows
[the server2 investigation b368eff2](2026-10-31-land-verification-on-server2-investigation.md) (branch
`feat/card-task-b368eff2`), which left the cause of the 2026-10-23 01:38Z step open (its section 3.3,
H1-H4). Sources: desktop server Serilog (`C:\src\Antiphon\server\logs\antiphon-*.log`), the session
runner's own Serilog with the build-slot broker lines (`%TEMP%\antiphon-logs\session-runner-*.log`,
kept from 2026-10-18), `logs\watchdog-apphost.log`, read-only `AgentTaskLandings` queries
(`BEGIN READ ONLY ... ROLLBACK`), directory listings, git history, and the CARD-1525 card text.
Nothing was built, tested, restarted, changed or deleted. Under `C:\Antiphon\releases` only directory
names and timestamps were listed.

## Verdict

**H1 is confirmed: the CARD-1506 Windows build-driver policy (`UseSharedCompilation=false`,
`DOTNET_gcServer=0`, `DOTNET_gcConcurrent=0`, MSBuild server off, `MSBUILDDISABLENODEREUSE=1`) caused
the step.** It is applied to the land's `dotnet build` (`AgentTaskLandService.cs:1976`). It first took
effect for lands when the server was restarted at 2026-10-23 ~01:51-01:53Z onto rc `5a126f6fc`
(CARD-1525 guard), after the temporary user-level opt-out was removed. H4 (cold artifacts) is
**refuted**: every land in the retained window reused the same warm generation, and the generation in
use at the step spanned it. Build-slot wait is **refuted** as the 3x: waits are 0-62 s. Host load (H3)
explains the **later drift** from ~420-550 s to ~750-1,570 s (Windows rc qualifications, memory-floor
pressure), but not the step.

**Smallest fix (not designed here):** stop applying the CARD-1506 policy to the land verifier's child,
or at least let it keep the shared compiler. It is one call site, and an operator decision because
CARD-1506 exists for memory.

### The natural experiment

The same server binary family, toolchain hash, warm generation (`25df0afb...`) and land worktree
(`C:\Antiphon\worktrees\land\0f9784a67773`) were in use before and after. Only the policy changed:

| Server PID (from broker lines) | Policy | Build-slot holds of land-verify (s) |
|---|---|---|
| 308056 (10-22 18:32Z .. 10-23 00:28Z) | none (pre-CARD-1506 code) | 149-296, typically 155-230 |
| 141036 (10-23 ~01:00Z .. 01:51Z) | CARD-1506 code, **opt-out=1 set** (see below) | 227 (01:05Z), 189 (01:14Z) |
| 89580 (from 10-23 ~01:53Z) | CARD-1506 + CARD-1525, opt-out removed | 766, 636, 496, 488, 500, 528 (01:58-02:57Z) |
| 44004 (10-27), alone on the broker (`1/2 occupied`), no qualification dir active | on | 413 (19:49Z), 438 (21:32Z), 424 (22:03Z) |

Why PID 141036 ran with the policy off: CARD-1525's card text (`card.ps1 get CARD-1525`). The
00:41Z restart onto rc with CARD-1506 failed: `build-driver-environment.ps1` threw
`KeyNotFound 'ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'` in `dev-aspire.ps1`'s `apphost-restore`. The
AppHost was "down from about 01:41 to 02:15 BST" (00:41-01:15Z). It was "Recovered by setting the
documented opt-out ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT=1 in the process and USER environment
(TEMPORARY; the policy is thereby DISABLED on this desktop until this fix is live ...)". The broker
shows that recovery: `apphost-restore` 00:41:23Z, 00:54:55Z, 00:57:44Z (1-2 s holds: the failed
attempts), then `apphost-restore` 00:58:38Z, `apphost-build` 00:58:48Z (88 s) and `daemon-build`
01:00:24Z, after which the land leases carry pid 141036. Then `rc-c1525-probe` ran 01:39:43-01:50:24Z,
followed by `apphost-restore` 01:51:18Z, `apphost-build` 01:51:28Z (125 s) and `daemon-build`
01:53:41Z, after which land leases carry pid 89580. Today the user-level and machine-level
`ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT` are empty (read with `[Environment]::GetEnvironmentVariable`), so
the opt-out was removed as the card instructed.

The 01:38:45Z verification (473 s, pid 141036, policy still off) is a load outlier, not the step.
Broker at 01:41:59Z: `waiter land-verify 0f9784a67773 (pid 141036) dropped: silent for 65s`.
`rc-c1525-probe` then waited 639 s with no other holder, which is the memory-floor block. That
matches the server's `memory_floor_fallback` pattern (120 s wait, then unleased at `maxcpucount=2`,
seen live on 10-28 03:55Z and 10-30 17:01Z). The server's own lines for 10-23 have rotated away.

## 1. Per-land artifacts, build time and slot

The server log is kept only from 2026-10-27 (5-day retention, `docs/logs.md:11`). For 10-22/10-23 the
evidence is the broker log plus DB rows. Every land-verify line found 10-27..10-31 (35 lands) says
`land_artifacts warm ...\dcaeb6b9...\426c6205...`. None says `new`, `cold` or `quarantined`.

Today's four (server log `antiphon-20261031.log`, UTC):

| Request | Artifacts | Slot wait | Lease | cpu | Build (held) |
|---|---|---|---|---|---|
| 4e7f3385 | `05:17:58.175 land_artifacts warm ...\426c6205...` | `05:17:58.193 BUILD SLOT granted lease=d86efbd1-... waited=0s maxcpucount=4` | d86efbd1 | 4 | `05:33:39.066 released ... held=940s` |
| 944218ee | `07:13:01.097 land_artifacts warm` | `07:13:01.110 BUILD SLOT waiting ... reason=memory_floor available=5760MB floor=6144MB`; `07:13:16.126 granted lease=7a24af92-... waited=15s maxcpucount=4` | 7a24af92 | 4 | `07:25:46.258 ... held=750s` |
| 102e8d54 | `09:25:28.677 land_artifacts warm` | `09:25:28.720 granted lease=97033d0c-... waited=0s maxcpucount=4` | 97033d0c | 4 | `09:51:35.274 ... held=1566s` |
| 7fb2810b | `10:00:03.088 land_artifacts warm` | `10:00:03.672 waiting ... reason=memory_floor available=4183MB floor=6144MB`; `10:01:05.339 granted lease=83801821-... waited=62s maxcpucount=4` | 83801821 | 4 | `10:17:01.268 ... held=955s` |

All four were granted with the desktop broker at `1/2 occupied`, so no other build-slot holder was
present. 7fb2810b's 09:53Z request queued in the land lane behind 102e8d54 (the DB figure in b368eff2),
not in the build-slot broker.

10-22/10-23 (DB `AgentTaskLandings`, `VerifiedAt - VerificationStartedAt`, with the broker hold): all
40 rows from 10-22 20:00Z to 10-23 04:00Z used `LandWorktreePath = C:\Antiphon\worktrees\land\0f9784a67773`,
and every one passed. Fast through `01:14:06` (208 s), then the docs skips at 01:19 and 01:28, then
`01:38:45` 473 s, `01:58:29` 816 s, `02:14:37` 663 s, `02:27:18` 506 s, `02:37:09` 499 s, `02:46:56` 510 s
and `02:57:23` 541 s.

## 2. The land-verify directory

`C:\src\Antiphon\.git\antiphon\land-verify\v1`, a single toolchain directory
`dcaeb6b99f12...627fee` (created 10-20 19:42:52Z). No listing was truncated; each took 5-69 s.

| Generation | Created (UTC) | Last write (UTC) | Files | Size |
|---|---|---|---|---|
| 25df0afb30ff410a895cb21db5e63758 | 10-20 19:42:52 | 10-25 23:22:49 | 7,671 | 2.59 GB |
| cfd9988b8dc548ecbc96fefe089cf1b7 | 10-25 23:29:46 | 10-25 23:29:57 | 5,305 | 1.63 GB |
| e1b09535545844d488f41adcf05e83b7 | 10-25 23:36:55 | 10-26 01:57:26 | 7,671 | 2.59 GB |
| 426c6205b8e44b3c8bc397967a671e41 (`current`, written 10-26 02:16:36) | 10-26 02:16:36 | 10-31 10:17:01 | 8,006 | 2.70 GB |

**No new directory is created per land.** One generation is reused for days. The generation in use
at the 10-23 step (25df0afb) lived from 10-20 to 10-25. Three generations were started on 10-25/26
(an 11-second abandoned one, then two). That is where cold builds happened, not at the step. The land
worktree itself is persistent (created 2026-09-25, reset per land: `LandWorkspace.cs:50,92`), so the
builds are truly incremental. The three superseded generations (about 6.8 GB) are not cleaned up; this is
noted, not investigated.

## 3. The resident-compiler policy for the land build

- Code: `scripts/lib/BuildDriverEnvironment.cs:24-37` (six overrides, skipped only when
  `ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT == "1"`), applied to the land child at
  `server/Application/Services/AgentTaskLandService.cs:1976`. Node reuse is also off from
  `Directory.Build.rsp:4` (`-nodeReuse:false`); that predates the step and is not a cause.
- Live server: PID 288196, `C:\src\Antiphon\server\bin\Debug\net9.0\Antiphon.Server.exe`, started
  2026-10-29 17:51:28 (server time) by `dotnet run --no-build` (PID 369172). Its DLL (written
  10-29 17:51:10) contains the strings `ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT` and `UseSharedCompilation`.
  The user/machine opt-out is empty. **The process environment of 288196 was not read.** No repo
  diagnostic exists for it, and reading another process's PEB is not a cheap read, so it was skipped.
  The policy being in effect today is therefore inferred from the code, the binary and the empty
  user/machine variables. It is strongly consistent with the durations, but it is not observed.
- The log does not print the `dotnet build` command line, and the policy is environment, not
  arguments, so no `-p:UseSharedCompilation=false` appears anywhere.
- VBCSCompiler: none was running at 10:32Z (no build in progress). Per-land start times cannot be
  recovered after the fact. Under the policy the land build should not start one, so a VBCSCompiler
  seen beside a land belongs to some other build.

## 4. Host load

- Desktop broker capacity 2. The 10-27..10-31 broker grants checked were at `1/2 occupied` except
  10-27 16:46Z and 10-30 08:34Z (`2/2`); two lands ran unleased under the memory-floor fallback. Slot waits were 0 s apart from 10-28 03:20Z (30 s, memory floor),
  10-28 03:53Z (120 s, then `unleased reason=memory_floor_fallback available=4057MB ... maxcpucount=2`),
  10-29 12:19Z (135 s, `occupied=2/2`), 10-29 14:10Z (15 s), 10-30 16:59Z (120 s, fallback at
  cpu 2), and today's 15 s and 62 s. No land waited behind a checkpoint or task build for long.
- Qualification activity (directory timestamps under `C:\Antiphon\releases`, names only):
  task-a5303e1a 10-22 01:54-04:52Z; task-2a34ba8c 10-22 16:03Z-10-23 01:49Z; task-c11e7803 10-23
  01:58-02:20Z (broker: `rc-5a126f6f-windows-qualification` lease at 02:00:35Z); task-217e7700 10-24
  07:18Z-10-25 04:12Z; task-997186e6 10-26 09:44Z-10-27 04:45Z; task-1873094e 10-27 22:14-22:27Z;
  task-5906dbf2 10-28 10:08Z-10-29 09:14Z; task-3ec2f72f/checkout-3ec2f72f from 10-30 08:22Z (the
  live d47848cc run).
- Load against the step: 10-22 lands **during** task-2a34ba8c were still 147-296 s with the policy
  absent. 10-27 evening lands with **no** qualification directory active and alone on the broker were
  413-438 s with the policy on. So load alone does not give the 3x, and its absence does not remove it.
- Load against the drift: today's 750-1,566 s overlap the live qualification, with memory-floor
  readings of 4,183-5,760 MB against a 6,144 MB floor. That is about 2x the 10-27 alone baseline. The
  qualification's test phases hold no build slot, so `occupied` does not show them.
- Correction to b368eff2 section 3.1: memory-floor fallback is **active** on this desktop (live
  `unleased reason=memory_floor_fallback ... waited=120s maxcpucount=2`), not default-off as read from
  code defaults.

## Uncertainties

1. Which of the six overrides costs the most is not separated. `UseSharedCompilation=false` (a cold
   Roslyn process per project) is the likely main term, and workstation non-concurrent GC is a
   second. One A/B build on the desktop would resolve it (not run: read-only brief).
2. PID 89580's and 288196's process environments were not observed. That the opt-out was absent from
   ~01:53Z on 10-23 is inferred from the card's instructions, the empty user variable today and the
   duration change.
3. Server-side verifier lines for 10-22/10-23 have rotated away. The 01:38:45Z land's
   fallback-at-cpu-2 is inferred from the broker lines plus the live fallback pattern.
4. Load from desktop sessions that hold no build slot (agent sessions, the qualification's test
   phases) was not measured per land.

## Not done, noted

- Fix idea (one line): exempt the land verifier's `dotnet build` from `BuildDriverEnvironment` (or
  keep only the node-reuse/MSBuild-server parts and leave `UseSharedCompilation` on), behind a
  `Landing:*` setting, after one desktop A/B timing.
- `docs/testing-and-build.md:1080` still says "`UseSharedCompilation` stays on" (stale since CARD-1506).
- Three superseded land-verify generations (~6.8 GB) remain under `.git\antiphon\land-verify\v1\dcaeb6b9...`.
