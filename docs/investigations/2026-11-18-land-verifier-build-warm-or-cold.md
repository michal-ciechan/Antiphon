# Land verifier build: warm or cold? (lands 3c4632e1 and aeda6794, 2026-11-18)

Read-only investigation, task 81ae1833. Master read: `3c388417552651de6f2800e7b09ecea361a00f94`
(origin/master at investigation time, equal to the land worktree HEAD). Nothing was built, changed or restarted.

## Answer in one paragraph

Land aeda6794 ran a **fully cold** build. That was not because the verifier lacks a warm cache: it has one.
It discarded the cache because the previous land had failed. The verifier keeps persistent build outputs in
`C:\src\Antiphon\.git\antiphon\land-verify\v1\<toolchain-hash>\<generation>` and reuses the current generation
only when its `state` file says `ready`. `ready` is written only when verification succeeds
(`server/Application/Services/LandingVerifier.cs:36`, `if (result.Ok) generation?.Complete();`). Land 3c4632e1
failed to compile (Antiphon.Resilience.Tests, CS0103), so its generation `426c6205…` stayed `building`.
85 minutes later, land aeda6794 found `building`, logged `land_artifacts quarantined`, created the empty
generation `2967c738…` and rebuilt all 62 projects from scratch
(`server/Infrastructure/Git/LandVerificationArtifacts.cs:52-66`). The discarded generation already held
up-to-date outputs for every project except Resilience.Tests. The two candidate trees differ by one test .cs
file in Antiphon.Tests, one Resilience.Tests .csproj line, one JSON file and two docs, so a warm reuse would
have recompiled roughly Antiphon.Tests only. The first land's 436 s build was labelled `warm` but was also
effectively cold: all 61 DLLs that built were rewritten, because that generation was last made `ready` on
2026-10-26, three weeks of master earlier.

## Q1. Land aeda6794 phase table

Sources: `AgentTaskLandRequests` and `AgentTaskLandings` rows (operation `d3d74170-7e01-4571-bd5a-fc1502790889`)
and `C:\src\Antiphon\server\logs\antiphon-20261118.log`. Per-project times come from output-file timestamps in
generation `2967c73855ec41648d7d62f82481e984`, because no binlog is recorded.

| Phase | Start (UTC) | End (UTC) | Duration | Evidence |
|---|---|---|---|---|
| Queue (request to start) | 14:11:33.475 | 14:11:33.721 | 0.25 s | RequestedAt/StartedAt |
| Admission (landing op created) | 14:11:33.721 | 14:11:36.265 | 2.5 s | AgentTaskLandings.CreatedAt |
| Git: persistent land worktree reset --hard + clean -fdx | 14:11:36.3 | 14:11:44.738 | 8.5 s (reset 3.54 s) | LandWorkspaceReadyAt; profile `reset=3.54` |
| Git: rebase | 14:11:44.769 | 14:11:45.914 | 1.15 s | RebaseStartedAt/PreparedAt; `rebase=1.15` |
| Verification decision | 14:11:45.981 | 14:11:46.733 | 0.75 s | `reason diff-requires-build target e276b4b4 candidate 3c388417` |
| Window R release + artifacts prepare (`dotnet --version`, tree scan, quarantine, new gen) | 14:11:47.994 | 14:11:51.476 | 3.5 s | `land_artifacts quarantined …426c…` then `land_artifacts new …2967c…` |
| Build slot | 14:11:51.479 | 14:11:51.479 | 0 s wait | `BUILD SLOT granted … waited=0s maxcpucount=4` |
| **dotnet build** | 14:11:51.5 | 14:19:24.6 | **453 s** | `BUILD SLOT released … held=453s` |
| Tests | – | – | none | No `results-*` directory in the generation; the land had no filter, so "build OK" (AgentTaskLandService.cs:1988) |
| Verified (window reacquired, bookkeeping) | 14:19:24.7 | 14:19:28.866 | 4 s | VerifiedAt; profile `verify=462.88` |
| Push | 14:19:30.379 | 14:19:49.809 | 19.4 s | PushStartedAt/RemoteConfirmedAt; `push=19.43` |
| Canonical advance | 14:19:49.840 | 14:19:50.688 | 0.85 s | CanonicalAdvance*; `canonical=0.85` |
| (Operator-visible end) | | 14:19:49.8 to 14:19:50.7 | 8 min 16-17 s total | LastProgressAt 14:19:50.70 |
| Cleanup (after the outcome) | 14:19:50.694 | 14:21:06.451 | 75.8 s | CleanupStarted/Completed; `cleanup=75.76`; profile `wallSeconds=584.62` |

The command (AgentTaskLandService.cs:1981-1986) ran in cwd `C:\Antiphon\worktrees\land\0f9784a67773`, the
persistent land worktree, which picks up `Antiphon.sln` (the only solution in the root):

`dotnet build --artifacts-path C:\src\antiphon\.git\antiphon\land-verify\v1\dcaeb6b9…\2967c73855ec41648d7d62f82481e984 --configuration Debug -maxcpucount:4`

The run had implicit restore, no `--no-incremental`, and default MSBuild node reuse. `UseSharedCompilation` is
not overridden in Directory.Build.props, so the compiler server is on by default.

### Build breakdown (critical path, from obj/ timestamps)

| Segment | Window | Duration | Host CPU avg (8 logical cores) |
|---|---|---|---|
| Restore (all `project.assets.json`) | 14:11:51.5 to 14:11:56 | ~4.5 s | n/a |
| Leaf and library projects up to Antiphon.Server's compile start | 14:11:56 to 14:12:57 | ~61 s | 93.9 % |
| **Antiphon.Server CoreCompile** (34 MB dll) | 14:12:57 to 14:14:55.7 | **~119 s** | 72.6 % |
| Server dependents (SessionRunner, Checkpoints, TimeoutAudit, DockerStack.Fixture, staged helpers…) up to Antiphon.Tests' RAR end | 14:14:56 to 14:16:22 | ~86 s | 88.7 % |
| **Antiphon.Tests CoreCompile** (68.9 MB dll, 12.6 MB pdb) | 14:16:22.1 to 14:19:14.9 | **~173 s** | 67.4 % |
| Copy/apphost/exit | 14:19:15 to 14:19:24.6 | ~10 s | 60.5 % |

Generation 2967c holds 9,862 files and 3.7 GB.

## Q2. Warm or cold, and why

- **Where it builds.** The source is one persistent land worktree per repository (`LandWorkspace.PathFor`,
  `C:\Antiphon\worktrees\land\0f9784a67773`). It is reset with `reset --hard <sha>` and `clean -fdx`
  (`server/Infrastructure/Git/LandWorkspace.cs:92-95`). `reset --hard` rewrites only changed files, so
  unchanged sources keep their timestamps. `clean -fdx` touches no outputs, because outputs go to
  `--artifacts-path` outside the worktree. Same path, no fresh checkout: the source side is incremental-friendly.
- **The outputs.** They are persistent generations keyed by `sha256(sdk|OS|arch|Debug)`. The key does not
  include the target branch.
- **Why the cache was not reused.** `Prepare` reuses `current` only when `state == "ready"`. `building` causes
  quarantine and a new empty generation (LandVerificationArtifacts.cs:52-66). `Complete()` (writes `ready`) runs
  only when `result.Ok` (LandingVerifier.cs:36). So **any failed verification (build or tests) permanently
  discards the warm cache for the next land**, even though MSBuild leaves every successfully built project
  incrementally valid. The 3c4632e1 build failed in Antiphon.Resilience.Tests. Its generation `426c…` holds
  11-18 12:45-12:52 outputs for all 61 other projects, including Antiphon.Tests.dll at 12:52:11. Only
  Resilience.Tests has no dll. The `state` files now read: `426c…` building, `2967c…` ready, current = `2967c…`.
- **What else was reused.** The NuGet package cache was reused: restore took ~4.5 s, so it was not network
  bound. Node reuse and the compiler server are at their defaults, with nothing disabled. They do not matter
  when every project's obj/ is empty.
- **Why the first land was slow too.** Generation 426c was `ready` from 2026-10-26 (owner dir created
  2026-10-26 02:16Z). Land 3c4632e1 reused it (`land_artifacts warm`), but 3+ weeks of master drift rewrote every
  DLL (61 of 61 dated 11-18 12:45-12:52). Warm by label, cold in effect: 436 s against 453 s for the truly cold run.
- **A comparable same-tree build.** None was run. The authority forbids a solution build unless it is the only
  way, and it is not: the generation timestamps and the candidate diff give the counterfactual directly.
  `git diff --stat 18628faa 3c388417` lists 5 files: two docs, `tests/derived-impact-index.json` (no project
  includes it), one line of `tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj`, and
  `tests/Antiphon.Tests/TestHelpers/LinkedTestHelperClosureTests.cs` (+104 lines). A warm reuse of 426c would
  have compiled Antiphon.Resilience.Tests (small, and never built in 426c) plus Antiphon.Tests (~173 s CoreCompile
  plus its RAR), with up-to-date checks elsewhere. **Estimate: ~3.5-4.5 min instead of 7.5 min (saving ~3-4 min).**
  This is an estimate, not measured: the no-op up-to-date pass over 62 projects at -m:4 is unmeasured
  (assumed 30-60 s).

## Q3. What dominates (top 3, with evidence)

1. **The cache was discarded, so all 62 projects compiled.** Mechanism above. Accounts for roughly 4 of the
   7.5 minutes (everything except Antiphon.Tests' compile and RAR).
2. **The Antiphon.Tests CoreCompile alone, ~173 s single csc**, at 67 % host CPU, which is effectively a serial
   tail. Antiphon.Server CoreCompile (~119 s) is second on the critical path. Any change touching Antiphon.Tests
   pays ~3 min even warm. Any change touching server/ pays Server plus its dependents plus Tests, about 6 min even warm.
3. **The host was saturated, against a cap of 4 MSBuild nodes.** The desktop host-load samples
   (`HostLoadSamples`, RunnerId `desktop`, 8 logical cores) show the build window averaging 76.9 % CPU (p50 80.4 %,
   max 100 %) against a 29.2 % baseline in the 11 minutes before (AppHost, Postgres, live sessions). Available
   memory fell to 4.5 GB of 32 GB. The first land's window was the same (75.4 % avg). Phases 1 and 3, where many
   small projects run in parallel, ran at 89-94 %, so a higher `-maxcpucount` would gain little on this host.

UNOBSERVED:
- Windows Defender. Real-time protection is on, but `Get-MpPreference` exclusions are unreadable without admin,
  so whether `C:\src\Antiphon\.git\antiphon\land-verify` or `C:\Antiphon\worktrees\land` is excluded is unknown.
  No per-process scan cost was measured.
- Disk latency (`ioWaitPercent` is null on Windows samples).
- Analyzer and source-generator share of the CoreCompile time. No binlog and no `-p:ReportAnalyzer=true`.

Ruled out by evidence: NuGet restore (~4.5 s); build-slot wait (0 s); the land queue and git work (~13 s together).

## Side findings

- **Quarantined generations are never deleted** ("Nothing here deletes an old generation",
  LandVerificationArtifacts.cs:12). Four dead generations now hold 10.3 GB under `.git\antiphon\land-verify\v1\dcaeb6…`
  (`25df0afb…` 2.6 GB, `426c6205…` 3.5 GB, `cfd9988b…` 1.6 GB, `e1b09535…` 2.6 GB), plus the live `2967c…` at 3.7 GB.
- The generation key has no target branch. Lands alternating rc/master would thrash the one generation. That
  stays correct under MSBuild incrementality, but the speed benefit is lost.
- `Prepare` runs `RequireUnlinkedTree` over the whole current generation (~9.8k files) before reading `state`,
  about 3 s per land.
- Post-outcome cleanup takes 75.8 s. The operator does not see it, but it holds the land lock before the next land.
- Correction to `docs/investigations/2026-11-18-slow-direct-land-35-min.md` (ed55f4ad7): land 3c4632e1 was not
  merely near-cold. It was `warm`-labelled, and every DLL was recompiled.

## Q4. Recommendations (card text, NOT filed)

**(a) Keep the land-verify generation warm after a failed verification**
Title: Land verifier: a failed build/test must not quarantine the warm artifact generation
1. `LandingVerifier.cs:36` marks a generation `ready` only on success. Every failed land makes the next land fully cold (aeda6794: 453 s cold vs ~3.5-4.5 min estimated warm).
2. Distinguish "child exited (any code) with streams drained" (outputs incrementally valid, mark reusable) from "killed/crashed/cancelled mid-build" (quarantine).
3. Safety: callers already hold L then R. Keep the owner/link/hardlink checks. Add the target branch (or rc/master family) to the key, or keep one generation per target, so rc and master lands do not thrash.
4. Delete quarantined generations older than N days, or keep at most one (10.3 GB stranded today).
5. Expected: a one-test-file change goes from ~7.5 min to ~3.5-4.5 min, dominated by the ~173 s Antiphon.Tests CoreCompile. Add a log line with per-target time or a `-bl` binlog kept per generation to confirm.

**(b) Build only the projects affected by the diff**
Title: Land verifier: build the impacted-project closure instead of Antiphon.sln
1. Derive changed projects from the diff (tests/derived-impact-index.json or the project graph), then build those plus their dependents, not the whole solution.
2. Cold, the gain is small: Antiphon.Tests' reference closure is most of the solution (Server, SessionRunner, Checkpoints…). It skips about 10 leaf test/host projects (E2E, AppHost, PtyHost.Tests, Agents.Pty.Tests, Messaging.Tests, SessionRunner.Tests…), freeing nodes in the 86 s middle segment. Estimated saving 30-60 s, unmeasured.
3. Warm (with (a)), it saves the up-to-date pass over ~50 unaffected projects, an estimated 20-40 s.
4. Keep a periodic full solution build. The nightly already builds Antiphon.sln, and project-reference or solution-only breaks must still be caught somewhere.
5. Lower value than (a). Do it after (a) and measure.

**(c) Defender exclusion (only say; change nothing)**
Title: Measure Defender cost on the land-verify output and land worktree, then decide on an exclusion
1. Real-time protection is on. Exclusions cannot be read without admin, so the cost is UNOBSERVED.
2. An admin should read `Get-MpPreference` ExclusionPath/ExclusionProcess, and take one ProcMon or `MpPerformanceRecorder` capture during a land build.
3. Candidates: `C:\src\Antiphon\.git\antiphon\land-verify`, `C:\Antiphon\worktrees\land`, and the `dotnet.exe`/`VBCSCompiler` processes.
4. Only if the capture shows significant MsMpEng time. Typical .NET build overhead is 10-30 %, unverified here.
5. Operator-only security setting. No agent change.

**(d) Overlap the land build with Review**
Title: Pre-warm the land-verify generation for the candidate while Review runs
1. When Review starts on a candidate SHA, build it (rebased on the current target) into the warm generation under a build slot, outside the land lock, or into a per-candidate copy.
2. At land time, the verification build becomes an up-to-date pass plus the target delta: an estimated 30-90 s instead of 3.5-7.5 min.
3. Needs (a) first. Must respect the single land lock, slot budget and memory floor, and must not race a concurrent land on the same generation (per-candidate generation or lock).
4. No benefit for direct Debug lands like aeda6794, where the land was requested 60 s after settlement (14:10:33 to 14:11:33).
5. Estimated saving: ~3-6 min per reviewed land, at the cost of one extra build per reviewed candidate (some candidates never land).

## Remaining uncertainties

- The warm-reuse estimate (~3.5-4.5 min) is reconstructed from timestamps, not measured. The no-op pass cost and
  whether `derived-impact-index.json` or the csproj edit triggers wider rebuilds through some glob are unconfirmed.
  The grep found no project including the JSON.
- Per-target analyzer/generator time and Defender cost are UNOBSERVED (no binlog, no admin).

## Not done, noted

- Fix idea, one line: mark a generation reusable after a clean non-zero exit, and quarantine only after a kill
  or crash (card text (a)).
