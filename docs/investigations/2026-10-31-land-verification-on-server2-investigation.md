# Why lands verify on the desktop, not server2 (2026-10-31)

Investigate task b368eff2 (runner mirror on server2, branch `feat/card-task-b368eff2`, base `2b471b21c`).
Read-only. Code references are to this branch (master lineage). Database figures come from read-only
queries (`BEGIN READ ONLY ... ROLLBACK`) against the desktop dev Postgres, made 2026-10-31 around
11:00-11:30Z. The live server answered `GET /api/version` = `8edda9996...` (on `origin/rc/2026-10-27`,
not on master) with `land-v2`. Nothing was restarted, built, written or reconfigured. The qualification
run d47848cc and `C:\Antiphon` were not touched.

This extends [why lands run on the desktop](2026-10-22-why-lands-run-on-the-desktop.md) (CARD-1462,
2026-10-22). That report has the step-by-step land trace with file:line. It is still accurate, and it
is summarised rather than repeated here. What is new: since 2026-10-23 the verification build costs
about 3x what it did, while the lane is no longer congested.

## Answer

**Why lands do not run on server2.** A land is a method of the one Antiphon server process
(`AgentTaskLandService`). That process runs only on the desktop: server2 runs a session runner, a
build-slot broker and a seed container (`docker-compose.server2-runner.yml`). The verifier's build slot
is hard-wired to the server's local runner:
`server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs:38-40` ("the build budget
belongs to the host this server builds on, i.e. the local runner"). The build itself is a direct child
of the server: `Process.Start` in `AgentTaskLandService.RunProcessAsync`
(`server/Application/Services/AgentTaskLandService.cs:1971-1984`). **No mechanism exists to run land
verification on a runner.** No setting, capability or code path does it. The 2026-10-22 option B
("verify on server2") was recommended as slice S4 and never built: CARD-1462's option-A plan carried
S1-S10 only, and grep finds no `VerificationRunner`/`verifiedOn` anywhere.

**Why it hurts now.** In 2026-10-20..22 the verification step had a median of 142-170 s. From
2026-10-23 01:38Z it is about 550 s, and today the median is 1,031 s (max 1,603 s). It is 85-90% of
every land. The lane itself is idle: 4-11 lands a day, and the queue wait is usually 1-4 s. So the cost
is wall time and desktop CPU per land, not queueing. The cause of the 3x step is **not confirmed**
(section 3.3).

## 1. Where and why land v2 executes (summary; full trace in the 2026-10-22 report section 1)

| Step | Code | Needs the desktop because |
|---|---|---|
| `delegate.ps1 -Land` -> `POST /api/agent-tasks/{id}/land/v2` | `scripts/delegate.ps1` (2026-10-22 report: `:379,845`); `docs/ops-http.md:194` | `ANTIPHON_API` is the desktop server |
| In-memory land queue, one global reader | `docs/orchestration-loop.md` land queue (CARD-0331: not restart-safe; the sweep re-reads pending requests from the DB) | in-process state of the desktop server |
| Yield to dispatch, then L (`land-execution.lock`) and R (`landing.lock`) | `LandExecutionLease.cs`, `RepositoryMutationLease.cs` (`FileShare.None`) | OS file locks under `C:\src\Antiphon\.git\antiphon\`; they fence only same-host writers |
| Child-journal admission fence | `RepositoryChildJournal` (`<common>/antiphon/children/*.json`, pid plus start ticks) | liveness is checked against the desktop process table |
| Source resolution (fetch branch into `refs/antiphon/land/...`; lands from origin's SHA if the desktop branch is Behind) | `AgentTaskLandSourceResolver.cs` | the desktop repository's objects and refs |
| `target_local_ahead`, rebase in `C:\Antiphon\worktrees\land\<sha256>` | `LandOperationFactory.cs`, `LandWorkspace.cs`, `AgentTaskLandingProtocol.cs` | the desktop filesystem |
| Verification decision | `LandVerificationPolicy.cs:14-16`: forced, filter or non-master target means build; a master diff that changes no file in the audited build-graph inventory may skip (CARD-1385, `LandVerificationBuildGraph.cs`, `policy=unrecognized-build-graph` when the manifest digest does not match) | none (pure) |
| Verification build | R released, L and the verification-window marker kept; `LandingVerifier.VerifyAsync` -> `LandVerificationArtifacts.PrepareAsync` (persistent generation under `<common>\antiphon\land-verify\v1\<sha256(toolchain)>\<generation>`, `LandVerificationArtifacts.cs:22,27-28`) -> `VerifyWithObserverAsync` (slot from the **local** runner) -> `dotnet build --artifacts-path <gen> --configuration Debug -maxcpucount:<grant>` (`AgentTaskLandService.cs:1938-1944`); tests only when a `-Verify` filter is given (`:1945`) | local runner broker; desktop CPU and RAM; the `.git` common dir |
| Publication | CAS push to origin with an observation before and after (`AgentTaskLandingProtocol.cs`, `LandingGit.cs`) | the desktop repository |
| Canonical advance | `update-ref refs/heads/master` on the desktop (the main checkout is on rc, so ref-only: 171 of 171 since 2026-10-16) | local `master` is what dispatch cuts from without fetching (`WorktreeManager.cs`, `WorktreeBaseResolver.cs`) |
| Cleanup | `GuardedWorktreeRemoval` removes `C:\Antiphon\worktrees\card-task-<id>` | the desktop worktree is canonical for every Worktree task, server2 included (CARD-0604 D-15) |

Rules that tie a land to the desktop. "Land never advances the main checkout" (CARD-0358, `AGENTS.md`)
and "hosts run the rc tip, never master" do not require the land to run there. A master land is not an
activation. What does require the desktop is the host-local leases and journal, local-`master` custody
for dispatch cuts, and cleanup of the canonical desktop worktree. The verification build has none of
those needs. It runs with R already released, in a detached land worktree, and nothing in it is
Windows-specific: no csproj is Windows-only, and no land in the window carried a filter.

## 2. Is there an existing mechanism to verify on a runner?

- **Land verification: no.** `IBuildSlotGate` is runner-agnostic, but `RoutingSessionRunnerClient.cs:38-43`
  routes `AcquireBuildSlotAsync`/`ReleaseBuildSlotAsync` to `_directory.Local`. The build is a local
  `Process.Start`.
- **Reusable precedent: the post-land Mutation snapshot.** `RemoteVerificationWorkspace`
  (`server/Infrastructure/Agents/SessionRunner/RemoteVerificationWorkspace.cs:18-57`, CARD-0604 D-19)
  and `VerificationWorkspaceDirectory.Resolve(runnerId)` already ask a runner, over typed phone-home
  operations, to create a worktree at an exact SHA. They re-validate the returned coordinates (POSIX,
  rooted, `InitialSha == landedSha`, creation id). Option (a) would need that SHA-bound create/validate
  pattern. That workspace serves an agent session, though, not a server-driven build.
- **What server2 already proves before a land.** A Code task runs its plan's checkpoint rows on server2:
  isolated builds plus exact test filters, receipts bound to the branch tip SHA. Review re-validates
  those receipts at the reviewed SHA. The desktop land build is a **second build** and proves three
  things the first does not:
  1. **The rebased candidate compiles.** It builds `RebasedSourceSha` = target tip + branch, which is a
     different SHA from the reviewed one whenever master moved. This catches semantic merge breaks
     between independently green branches. Only this property is unique to the land.
  2. **A Windows, Debug, whole-solution `Antiphon.sln` build.** Checkpoint rows build only the projects
     their rows need, with `UseAppHost=false` off Windows. Windows correctness as a whole belongs to the
     rc qualification (`docs/release-gates.md`), not the land.
  3. **The graph digest/audit (CARD-1385)** is not a build. It is a manifest comparison that decides
     whether a docs-only diff may *skip* the build (`LandVerificationPolicy.cs`). It runs before
     verification and costs nothing measurable.

  No tests run in a land: 0 of the landings since 2026-10-20 have a `VerificationFilter`.
  `VerificationCommand` always reads "dotnet build; dotnet run --project tests/Antiphon.Tests", but that
  label is a constant (`AgentTaskLandingProtocol.cs:313`). Tests run only with a filter
  (`AgentTaskLandService.cs:1945`).

## 3. Cost and load

### 3.1 What runs and how

| Item | Value | Evidence |
|---|---|---|
| Command | `dotnet build --artifacts-path <gen> --configuration Debug -maxcpucount:<grant>` in the land worktree (whole `Antiphon.sln`, 42 projects at master `cbe3e6b42`) | `AgentTaskLandService.cs:1940-1942` |
| Tests | none unless `-Verify` | `:1945` |
| Build slot | taken (CARD-0589 S4) from the desktop broker, label `land-verify <worktree>`, wait up to `Landing:BuildSlotWaitMinutes` 30 | `AgentTaskLandService.cs:1915-1920`, `LandingSettings.cs` |
| CPU count | desktop grant `MaxCpuCount` 4 (code default); unreachable runner -> unleased at 4; memory-floor fallback (CARD-1461, default **off**, `BuildSlotMemoryFloorFallbackMinutes` 0) -> 2 | `docs/testing-and-build.md:1086-1089`, `LandingSettings.cs` |
| Desktop broker | `MaxConcurrent` 2, floor 6,144 MB. The land competes for those 2 slots with every desktop build-slot user | same table |
| Process priority | **normal**. No priority class is set anywhere in `server/` (grep `PriorityClass|BelowNormal` finds only pty Job Object structs) | `AgentTaskLandService.cs:1971-1984` |
| Driver environment (Windows) | CARD-1506 `BuildDriverEnvironment`: `UseSharedCompilation=false`, `DOTNET_gcServer=0`, `DOTNET_gcConcurrent=0`, MSBuild server off, `MSBUILDDISABLENODEREUSE=1`, unless `ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT=1` is set in the server's environment | `scripts/lib/BuildDriverEnvironment.cs:24-37`, applied at `AgentTaskLandService.cs:1976`; on rc since `c15961b94`, absent-key guard `462dfdd8e` (CARD-1525) |
| Node reuse | `-nodeReuse:false` from the root `Directory.Build.rsp` | `docs/testing-and-build.md:1080` (its sentence "`UseSharedCompilation` stays on" is stale since CARD-1506) |
| Incrementality | warm generation reuse (`Landing:ReuseVerificationArtifacts` default true). A generation left in `building` is quarantined and a new empty one starts (a full build) | `LandVerificationArtifacts.cs:30-75`, `LandingVerifier.cs:28-33` |
| Qualification awareness | none. The land verifier and the runner's broker do not consult the qualification's `native-run.lock` (grep finds it only in `scripts/lib/nightly-run-impl.ps1:324`, `rc-qualification-status.ps1:126`) | |

The operator saw VBCSCompiler next to the land build. With CARD-1506 active, the land's own build should
not start one (`UseSharedCompilation=false`). Either that VBCSCompiler belonged to another build on the
host (the qualification, or a `bin-*` build outside the wrapper), or the server runs with the opt-out
set. Not verifiable from here (uncertainty 2).

### 3.2 Measured durations (DB, `AgentTaskLandings` schema 3, verification = `VerifiedAt - VerificationStartedAt`, slot wait included)

| Day | Lands | Docs skips | Verify median s | Verify max s | Land op median s |
|---|---|---|---|---|---|
| 10-20 | 71 | 0 | 150 | 304 | 302 |
| 10-21 | 54 | 14 | 142 | 1,225 | 332 |
| 10-22 | 122 | 37 | 170 | 1,539 | 252 |
| 10-23 | 72 | 2 | 548 | 11,523 | 630 |
| 10-24 | 43 | 0 | 555 | 1,391 | 640 |
| 10-25 | 27 | 1 | 539 | 1,650 | 603 |
| 10-26 | 8 | 0 | 554 | 682 | 487 |
| 10-27 | 6 | 0 | 509 | 672 | 582 |
| 10-28 | 11 | 0 | 705 | 1,315 | 866 |
| 10-29 | 7 | 0 | 650 | 845 | 745 |
| 10-30 | 7 | 0 | 871 | 1,220 | 1,023 |
| 10-31 (to ~11Z) | 4 | 0 | 1,031 | 1,603 | 1,149 |

Today's land requests, requested -> terminal: 05:16Z 1,182 s, 07:12Z 934 s, 09:24Z 1,850 s, 09:53Z
1,570 s (306 s queued behind 09:24). For each, preparation took 24-42 s, verification 790-1,603 s and
push/canonical/cleanup 105-211 s. Since 10-26, queue wait has been 1-38 s except for three held
requests (10,543-10,549 s and 3,456 s).

### 3.3 The 2026-10-23 01:38Z step (cause not confirmed)

Verifications through 10-23 01:14Z ran 150-270 s (`01:14:06` -> 208 s). The next built land started at
`01:38:45` and took 473 s. Every one after it took 430-816 s, and the shortest since then is 366 s
(10-26 07:55Z). Lands at 01:19Z and 01:28Z were docs-only skips.

| Hypothesis | For | Against |
|---|---|---|
| H1: the server began applying CARD-1506 (no shared compiler, workstation non-concurrent GC) | The docs predict it ("Incremental Windows builds may be slower", `docs/testing-and-build.md:1074`). A uniform ~3x floor fits a configuration change better than load. | **No server restart at the step.** `TranscriptEntries` rows were written every minute 01:30-01:39Z and task events continue. Before CARD-1525 (`462dfdd8e`, picked 01:38:40Z) the policy read threw `KeyNotFound` on a real `ProcessStartInfo` (CARD-1525 plan), so a server running CARD-1506 without CARD-1525 would have failed every verification, and none failed. The running server's activation history was not visible. |
| H2: the candidate got heavier | master grew 39 -> 42 projects and 4,147 -> 4,413 `.cs` files between 10-22 and 10-31 (+6%) | too small for a 3x step at one instant; could explain part of the later drift |
| H3: desktop load started at 01:38 and has mostly continued (Windows rc qualification, a second build-slot holder, CARD-1623's contention) | The CARD-1525 plan (10-23) names a live qualification (supervisor PID 133964). The drift from ~550 s to ~1,000 s over 10-28..31 matches growing load. Normal priority and `-maxcpucount:4` give the land no protection. | Qualification start/stop times were not visible from here. A pure-load cause would show more spread down toward 150 s on quiet hours, and the minimum is 366 s. |
| H4: warm-artifact reuse stopped working, so every land builds cold | Would give a constant ~3x. `Prepare` quarantines a `building` generation and starts an empty one. | No evidence either way; the deciding `land_artifacts warm/new/cold/quarantined` lines are only in the desktop server log |

**What would resolve it.** Do these on the desktop, read-only:
1. Grep the server log since 2026-10-22 for `land_artifacts ` (warm, new, cold or quarantined) and
   `BUILD SLOT` for each land (`LandingVerifier.cs` Observer.Line). This separates slot wait from build
   time and confirms or refutes H4.
2. Read the server process start times (AppHost restart log, `logs/apphost.*`). Read whether
   `ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT` is set in the AppHost/server environment (H1).
3. Read `GET :17204/build-slots/metrics` and the qualification `run.lock`/`native-run.lock` history
   against the land times (H3).

## 4. Options

| Option | Effort | Risk | Safety-story change |
|---|---|---|---|
| **(a) Verify on server2, publish on the desktop** | Medium-large: 3-4 Code slices | Cross-host wait. The server2 broker (4 slots, `-maxcpucount` 6) is shared with checkpoints. A Linux build replaces a Windows one. | The server pushes the prepared candidate to an owned origin ref (`refs/antiphon/land/<task>/<op>/prepared`, already a pin prefix). A runner operation, modelled on `RemoteVerificationWorkspace`, fetches exactly that SHA, builds it through the runner broker and returns `{candidateSha, treeSha, buildGraphDigest, sdk/toolchain, exit, duration, runnerId}`. Before the push the server checks `returned.candidateSha == op.RebasedSourceSha` and that the local `prepared` pin is unchanged. The existing post-verify rechecks under R then run unchanged, and publication pushes that SHA, so verified source == pushed source by SHA equality. The receipt records `verifiedOn`. The CARD-1379 verification window and L stay as today. |
| **(b) Keep the desktop build, lighten it** | Small: 1 slice plus settings | Low. Longer lands while a qualification runs. | None to custody. Start the child below normal priority. Use a land-specific `-maxcpucount` (e.g. 2). Optionally defer the start of verification while `native-run.lock` is held, under L and with R released, bounded by the existing 30-minute slot wait. Does not cut the cost; only moves it off the qualification's critical path. |
| **(c) Reuse a server2 green receipt for the exact SHA** | Small-medium | **High.** A Code or Review receipt is for the branch tip, not the rebased candidate. It matches only when the branch already sits on the target tip, or (fast-forward) the rebase was a no-op, and then only for the rows the plan selected, never the whole solution. | Weakens "the land verified what it pushed". Defensible only as "skip when `RebasedSourceSha == ReviewedSourceSha` and a whole-solution clean receipt exists for that SHA". That is rare: most lands rebase over newer master. |
| **(d) Move the whole land service to server2** | Large: a second server or a runner land capability, plus a protocol split | High: host-local leases cannot span hosts; local `master` custody for dispatch; desktop worktree cleanup | Needs the operator decisions in the 2026-10-22 report section 6 (local master custody, CARD-0604 D-15). The desktop still holds R twice per land. Breaks `docs/orchestration-loop.md` "lands are desktop work", the CARD-0358 pull rule text and the residue wording. |

**Recommendation: (a), preceded by the cheap half of (b).** Resolve 3.3 first, because H1 or H4 may be
a smaller fix than any option.

- **S0 (Investigate, desktop lane, 15 min):** the three reads in 3.3. If H4, the fix is in
  `LandVerificationArtifacts`. If H1, the decision is whether the land verifier keeps the CARD-1506
  policy.
- **S1 (b, Code):** land-specific priority and `-maxcpucount` settings (`Landing:VerificationPriority`,
  `Landing:VerificationMaxCpuCount`). Checkpoint: a fake `LandProcessRunner` sees the capped argument; a
  native Windows row confirms BelowNormal on the child.
- **S2 (a, Plan):** design the runner verify operation, the receipt fields, the SHA-equality guard,
  fallback to the desktop when the runner is ineligible, and the setting `Landing:VerificationRunner`
  (default local).
- **S3-S5 (a, Code):** runner contract and handler (fetch prepared SHA, isolated build through the
  runner broker, receipt); server client and guard in `LandingVerifier`/`PreparedLandingVerification`;
  landing receipt `verifiedOn` plus attention on fallback. Checkpoints: (i) a mismatched returned SHA
  refuses before push; (ii) the prepared pin moving during remote verification refuses; (iii) an
  ineligible runner falls back to local with a recorded reason; (iv) an end-to-end `LandVerificationWindowHarness`
  run with a fake runner shows R released and L held throughout.
- **S6 (measure):** a week of `VerifiedAt - VerificationStartedAt` with `verifiedOn`, compared with
  3.2.

## 5. Operator decisions

1. **Verification platform.** Is a Linux whole-solution Debug build on server2 an acceptable land
   verification, with Windows coverage left to the rc qualification? It is today's de facto split:
   lands run no tests.
2. **Is a land allowed to wait for the qualification?** That is, defer while the qualification holds
   `native-run.lock` under (b), or take server2 capacity under (a). Today it does neither.
3. **Keep the CARD-1506 driver policy for the land verifier**, if S0 shows it is the 3x (H1).
4. **Who may push the `prepared` candidate to an origin ref under (a)?** The desktop pushes it, so no
   new credential is needed. GitHub ref protections on `refs/antiphon/*` were not checked.
5. **Server2 capacity:** whether a land-verify may take one of the four server2 slots ahead of
   checkpoints, or gets a reserved slot.

## Uncertainties

1. The cause of the 10-23 step (section 3.3) is unconfirmed. The deciding log lines are in the desktop
   server log, which is not reachable from server2.
2. Whether the running server applies CARD-1506 to the land build (an opt-out could be set in its
   environment). This leaves the operator's VBCSCompiler observation unexplained.
3. Verification time includes the build-slot wait. The split between waiting and building is unknown
   without the `BUILD SLOT` lines.
4. Qualification run start/stop times and desktop CPU load per land were not available.
5. The live desktop `Landing:*` and `SessionRunner:BuildSlots:*` overrides were not read; the values
   above are code defaults.
6. Line references in section 1 that come from the 2026-10-22 report were checked at its base
   `3fa8d7bd4`; the ones re-read here (`AgentTaskLandService.cs:1897-1984`, `LandingVerifier.cs`,
   `LandVerificationArtifacts.cs`, `RoutingSessionRunnerClient.cs:38-43`, `BuildDriverEnvironment.cs`,
   `LandVerificationPolicy.cs:12-16`) are current.

## Not done, noted

- No fix designed beyond the option table; no process, setting, repository or database changed.
- One-line fix idea: run the land build on the server2 runner against a pushed `prepared` SHA and
  publish only on SHA equality (option a), after S0 rules out a cold-artifact or driver-policy regression.
- `docs/testing-and-build.md:1080` still says "`UseSharedCompilation` stays on", which CARD-1506
  contradicts on Windows. This is a docs fix.
