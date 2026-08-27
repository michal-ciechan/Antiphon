# CARD-0216 — Serve a built bundle for normal access, and stop the phone home from blanking on one spinner

**Date:** 2026-08-27
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0216 (`050282f8-9117-41d4-98d1-6481a33bbd1f`), board Antiphon
**Scope:** (1) the primary fix — the URL the user actually loads (`https://antiphon.desktop.codeperf.net`,
and its laptop/localhost siblings) serves a PRE-BUILT bundle by default, with a one-command switch
back to the live Vite dev server for frontend work; (2) the `MobileHomePage.tsx:111` full-body render
gate; (3) the cheap-and-contained server-side fan-out fixes round 2 confirmed. Everything else the
two investigations surfaced is named in §4 and left to CARD-0217.
**Evidence base:** the card's description (round 1) and
`docs/investigations/2026-08-27-card-0216-post-load-spinner.md` (round 2 + the orchestrator's
confirmation section). **This plan does not re-derive any of it**; §1 lists only the facts about the
*serving path* that this design needed and that neither round recorded.
**Builds on:** CARD-0185 (AppHost per-machine opt-in shape), CARD-0043 (daemon desired-state files
under `logs/`), CARD-0091 (this session's plan format), CARD-0204/0221 (orphaned child processes —
the class of bug §2.3's kill-on-timeout closes for git).
**Model followed:** `docs/superpowers/plans/2026-08-27-card-0091-stale-parked-message-sweep-plan.md`.

## Verdict, in one screen

| Finding | Evidence | Consequence |
|---|---|---|
| **The remote URL is Caddy → `host.docker.internal:17203`, and 17203 is `npm run dev`** — the Vite dev server, full stop. The vhost is generated from the links machine file (`"proxy": 17203`), so the port is the contract; what listens on it is Antiphon's to decide. | §1.1 | The switch lives INSIDE Antiphon, on port 17203, and Caddy/links/DNS are untouched. No dual URL: the same address serves whichever mode is selected. |
| **Vite 8.0.0's `preview` server already has everything the dev server has that we rely on** — `preview.allowedHosts`, `preview.proxy` (same middleware, `ws: true` included), `preview.headers`, ETag revalidation. Round 1 measured this exact server at 19–21 requests / 1.25 s warm. | §1.2 | The built mode is `vite preview` with the existing proxy config hoisted so both servers share it. No new static-server dependency, no server-side `wwwroot` copy step. |
| **Aspire 9.3.0 cannot restart one npm resource programmatically** (`ResourceCommandService` is not public until later Aspire; only the dashboard button can). | §1.3 | A mode that is read once at resource start would need an AppHost restart (~2 min, API down) or a dashboard click to switch. The design instead makes the 17203 process a tiny self-supervising shim that swaps its child when the mode file changes — switching takes seconds and restarts nothing else. |
| **The bundle has 93 assets including lazily-loaded mermaid chunks**, and Vite's `emptyOutDir` wipes `dist/` at the start of each build. | §1.2 | A rebuild while a phone is mid-load, or while a tab later lazy-loads a diagram, would 404. Built mode builds once cleanly at start and then rebuilds **in place** (`emptyOutDir: false` for the watcher). |
| **The "8 dead gym-stat directories" cost zero git processes today** — `WorkspaceInfoService.GetWorkspaceAsync` (`:49`) answers a non-existent directory without spawning — and the rows are NOT dead: they are the Gym Stat project's 19 sub-repo agents, created 2026-08-26/27 by the AlwaysOn `Gym Stat Orchestrator` (CARD-0032's setup flow) whose repos have not been created yet. | §1.5 | **Nothing to prune.** Deleting them would break a live project setup for no measurable gain. Struck from scope with the evidence (§4). |
| The remaining server issues are each one method wide: unbounded `Task.WhenAll` with no in-flight dedupe (`WorkspaceInfoService.cs:39,49-54`), a git child never killed on its 15 s timeout (`GitWorkspaceService.cs:616-626`), `IsWorkingAsync` awaited per agent (`AgentService.cs:82-91`), a 100 s runner call on the agent list (`AgentService.cs:251`, `Program.cs:214-218`). | §1.4 | All four are contained and ship here (§2.3); the `/api/attention` git N+1 shares nothing with them and stays in CARD-0217 (§4). |
| **Two client waste fetches per load have one-line causes**: `useAgentTasks` has no `staleTime`, so a later-mounting subscriber refetches the 651 KB on mount; `gitDirs` changes identity when `tasks` lands after `agents`, so `/api/filesystem/workspaces` fires twice. | §1.6 | Both fixed in §2.4 as part of this card — they are on the home page's own path and cost nothing to fix. |

---

## 1. What exists today (only what the investigations did not already record; verified 2026-08-27)

### 1.1 The remote serving path, end to end

- **Caddy vhost** (`C:\src\ClaudeBot\desktop\caddy\generated\antiphon.caddy`, generated — do not hand-edit):
  `antiphon.desktop.codeperf.net { reverse_proxy host.docker.internal:17203 { header_up Host {host} } tls {…dns cloudflare…} }`,
  and the identical block for `antiphon.localhost.codeperf.net`. Generated by `gen-caddy.mjs` from the
  links machine file `C:\src\links\src\data\machines\desktop-ktlkpif.json:16`:
  `{ "title": "Antiphon", "host": "antiphon", "proxy": 17203, … }`. The laptop file
  (`mc-dell-xps2023.json:27`) points `antiphon.laptop.codeperf.net` at the same port. (Aside, not
  in scope: the desktop links row for Storybook still says `"proxy": 17283`; the AppHost has run it on
  17209 since `db7696d`.)
- **What listens on 17203**: `Antiphon.AppHost/Program.cs:97-101` —
  `builder.AddNpmApp("client", "../client", "dev").WithReference(server).WaitFor(server).WithEnvironment("BROWSER","none").WithHttpEndpoint(port: 17203, env: "VITE_PORT")`.
  `client/package.json` `"dev": "vite"`. `client/vite.config.ts` configures **only** `server`
  (`port` from `VITE_PORT`, `allowedHosts: ['.laptop.codeperf.net','.desktop.codeperf.net','.localhost.codeperf.net']`,
  `proxy` for `/api` and `/hubs` (`ws: true`) to `services__server__http__0` ?? `http://localhost:17202`).
  No `preview` section exists, and neither server sets `strictPort` — a busy 17203 would make Vite
  silently pick the next free port, on a machine where 17204–17209 all mean something.
- **Simple mode** (`dev-start.ps1:64-67`, `restart.ps1`) runs `npm run dev` on **17282** in its own
  tab. It never touches 17203 and is not in this design's path.
- **The .NET server already has SPA hosting** (`server/Program.cs:636-637`, `UseStaticFiles` +
  `MapFallbackToFile("index.html")`) — that is how the E2E fixture serves `client/dist`
  (`AntiphonAppFixture.UsePrebuiltFrontend`, `KestrelWebApplicationFactory(webRoot: client/dist)`).
  It is not used here (§2.1 explains why the Vite process stays), but it is the fallback if
  `vite preview` ever proves unsuitable.

### 1.2 Vite 8.0.0 `preview`, checked in the installed package

`client/node_modules/vite/dist/node/chunks/node.js` (`async function preview(`, line ~33395):
`hostValidationMiddleware(config.preview.allowedHosts)`, `proxyMiddleware(httpServer, config.preview.proxy, config)`
(the same middleware the dev server uses, so `ws: true` works), `sirv(distDir, { etag: true, … setHeaders: config.preview.headers })`.
So: hashed assets are served with an ETag and **no `Cache-Control`** — every warm load revalidates
each of the ~19 requests once (1 RTT each, multiplexed under h2 through Caddy). `preview.headers`
applies to **every** response including `index.html`, which must stay revalidatable, so the
immutable header for `/assets/` goes in a `configurePreviewServer` middleware, not `preview.headers`.
`build.emptyOutDir` (`index.d.ts:2095`) is the flag that wipes `dist/` per build.
`C:\src\Antiphon\client\dist` (built 12:13 today by round 1) holds **93** assets: the 2.5 MB entry
chunk plus `MermaidDiagram-*.js` and ~40 lazily-imported mermaid diagram chunks.

### 1.3 Aspire 9.3.0 and restarting one resource

`Antiphon.AppHost.csproj` pins `Aspire.Hosting.AppHost` / `.NodeJs` **9.3.0**. Its `Aspire.Hosting.dll`
carries `ResourceCommandAnnotation`, `KnownResourceCommands` and `ExecuteCommandAsync`, but **no public
`ResourceCommandService`** — the type that lets AppHost code invoke a resource's `resource-restart`
arrived in a later Aspire. The existing control API (`Supervisor/ControlApiService.cs`, port 17207)
only drives `DaemonProcessService` entries (session-runner, fake-gateway); `dev-aspire.ps1:106`'s
banner `control/{server|client}/restart` predates the move to `AddNpmApp` and does nothing for the
client. `restart-apphost.ps1` restarts the whole tree (~2 min).

### 1.4 The four server sites, as they stand

- `WorkspaceInfoService` (singleton, `Program.cs:416`): `GetWorkspacesAsync` → `Task.WhenAll` over ≤64
  paths (`:39`); `GetWorkspaceAsync` (`:44-54`) checks the 20 s TTL cache, then **`Directory.Exists(path) ? await _git.GetWorkspaceInfoAsync : not-a-repo`**, and writes the cache only after git returns. No in-flight map.
- `GitWorkspaceService` (singleton, `Program.cs:410`): `RunAsync` (`:592-632`) is a bare
  `Process.Start` under `using var process`; a linked CTS with `GitTimeout = 15 s`; the
  `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` at `:622` logs and returns
  `(-1, "", "timeout")` **without killing the child**; a caller-side cancellation propagates without
  killing it either. Three test sites construct it directly (`new GitWorkspaceService(NullLogger…)`
  in `AgentTaskCheckSweepTests.cs:746,895,1007`), so the constructor must stay compatible.
  `WorkspaceInfoTests.cs` runs the resolution logic against **real** scratch git repos
  (`ScratchGitRepo`) — the pattern §6's tests reuse.
- `AgentService.GetAllAsync` (`:63-92`): batched everywhere except the loop's
  `await IsSessionWorkingAsync(live?.Dto, ct)` → `SessionMessageQueueService.IsWorkingAsync(db, sessionId)`
  (`:2596-2672`, **internal static**, two grouped queries filtered on one `AgentSessionId`, then two
  rules: `activity.Seq > end.Seq`, overridden to idle when timestamps prove activity predates the end).
  `AttachRunnerLiveStateAsync` (`:251`) calls `_runnerClient.ListAsync(ct)` on the typed
  `HttpClient` whose `Timeout` is `SessionRunnerSettings.RequestTimeoutSeconds` = 100
  (`server/appsettings.json:49`, `Program.cs:214-218`); its catch already leaves runner state
  unknown on any non-cancellation failure.
- `/api/agents` is polled every 5 s per tab (`agents.ts:433-438`); **not** changed here (§4).

### 1.5 The "dead" gym-stat rows

Live DB, 2026-08-27: 50 agents; 20 have `WorkingDirectory` under `C:\src\gym-stat*`, all
`AlwaysOn = false` except `Gym Stat Orchestrator` (`C:/src/gym-stat`, AlwaysOn), all with a
`PersistentSessionId`, `CreatedAt` 2026-08-26/27, paired `*-plan` / `*-code` per sub-project
(`auth`, `datamodel`, `deploy`, `fieldeditor`, `floorplan`, `install`, `logging`, `offline`,
`floorspace`→`planspace`, `mock`). The eight directories round 2 found missing are the eight
sub-repos not yet created. The cost of each on `/api/filesystem/workspaces` is one
`Directory.Exists` and one cache entry.

### 1.6 The two client waste fetches

- `useAgentTasks` (`client/src/api/agentTasks.ts:222-232`): `refetchInterval: 15_000`, **no
  `staleTime`** — React Query's default 0, so any subscriber that mounts after the first fetch
  resolves (the desktop `ProjectTasksPanel` after wave 2; a mobile band) refetches on mount. That is
  round 1's "fetched twice per load".
- `HomePage.tsx:82-93` builds `gitDirs` from `agents.data` **and** `tasks.data` in one memo and
  feeds it to `useWorkspaceGitInfos(gitDirs)` (`filesystem.ts:67-78`, the array IS the query key,
  `enabled: paths.length > 0`). `agents` resolves first → one call with agent dirs; `tasks` resolves →
  a new key with the union → a second call. Round 2's "`/api/filesystem/workspaces` (1 path)" then
  "(32 paths)".
- Mobile: `useAttention` (`attention.ts:118-125`) and `useAgentTasks` both poll at 15 s; the gate at
  `MobileHomePage.tsx:111` is `if (attention.isLoading || tasks.isLoading) return <Loader/>`. The
  header ("Antiphon", "Report bug", avatar) is `client/src/shared/Layout.tsx`, outside the page — which
  is why the screenshot shows it above the spinner. On an **error** in either query `isLoading` is
  false, `needsYou` is `[]`, and the page renders `CalmCard` ("Nothing needs you.") — a wrong answer
  that today's gate does not prevent. `client/src/shared/SkeletonLayouts.tsx` already exports
  `PanelSkeleton` / `InlineSkeleton` / `CardSkeleton` (used by `SuspenseBoundary`).

### 1.7 What does not exist (the build list)

- No `preview` config, no `strictPort`, no asset cache headers, no `serve` npm script, no mode file,
  no `scripts/client-mode.ps1`, no shim (`client/scripts/serve.mjs`), no doc line saying what 17203
  serves.
- No per-band loading state on the phone home; no error state for band 1.
- No process gate, no in-flight map, no kill-on-timeout, no batch `IsWorkingAsync`, no runner list
  deadline.
- No `staleTime` on the tasks query; no "both loaded" guard on `gitDirs`.

---

## 2. Design

### 2.1 Client serving mode — one port, one process, two modes, a file to flip them

**Shape.** Port 17203 stays the client's address for Caddy, the dashboard, the docs and every
delegate's browser check. What Aspire starts on it becomes `npm run serve` → `node scripts/serve.mjs`,
a dependency-free ESM shim (~80 lines) whose only job is to run the right Vite child and swap it
when asked:

```
logs/client.mode            "built" (default when missing) | "dev"      ← the switch
client/scripts/serve.mjs    reads the mode, spawns the child, polls the file every 1 s, swaps
logs/client.state.json      { mode, pid, since, lastBuildAt, status: starting|building|serving|switching }
scripts/client-mode.ps1     -Mode built|dev | -Status | -Rebuild   (writes the file, waits, reports)
```

- **`built` mode** (the default, and what the remote domain gets): the shim (1) runs one clean
  `vite build` (no `tsc -b` — type checking stays with `npm run build`, lint and vitest; a type error
  must not take the phone down), (2) starts `vite preview` on `VITE_PORT`, and (3) starts
  `vite build --watch` with `emptyOutDir` **off** so later rebuilds land in place and a mid-load phone
  or a later mermaid lazy-load never 404s. The watcher is the answer to "does this need a
  rebuild-on-change" (§5 D2): with it, a merge to master shows up on the phone within one build
  (~20–40 s, to be measured in S1) and a delegate verifying a client change at `localhost:17203`
  sees its code without switching modes; without it, the bundle is only as fresh as the last AppHost
  restart. Stale hashed chunks accumulate in `dist/` between restarts (harmless; the clean build at
  start prunes them). `ANTIPHON_CLIENT_WATCH=0` turns the watcher off for a machine that cannot spare
  the memory.
- **`dev` mode**: the shim runs `vite` exactly as `npm run dev` does today — HMR, `/@vite/client`,
  source maps — on the same port. Nothing else in the stack knows the mode changed.
- **Switching**: `pwsh -File scripts/client-mode.ps1 -Mode dev` writes the file; within ~1 s the shim
  kills its current children (`child.kill()` on the directly-spawned `node …/vite.js` processes — the
  shim never goes through `npm`/`cmd`, so there is no wrapper to orphan a grandchild behind), waits
  for 17203 to close, spawns the other mode, and updates the state file. The script then polls
  `http://localhost:17203/` until the body matches the requested mode (`/@vite/client` present ⇔ dev)
  and prints the result — under 10 s to dev, under one build to built. `-Status` prints the file, the
  probe verdict and `lastBuildAt`; `-Rebuild` (built mode only) touches a sentinel the shim treats as
  "swap to built again", i.e. a clean rebuild without a restart. The choice **persists** across
  AppHost restarts (§5 D3): a machine mid-frontend-work stays in dev until told otherwise, and
  `-Status` says so.
- **Vite config**: hoist `port`, `strictPort: true`, `allowedHosts` and `proxy` into one const used
  by both `server` and `preview`; leave `preview.headers` unset (it would hit `index.html` too, §1.2)
  and add a `configurePreviewServer` plugin that sets `Cache-Control: public, max-age=31536000, immutable` on
  `/assets/*` only (content-hashed by construction). With it a warm remote load is `index.html` (one
  conditional GET) plus the API calls — the 184 revalidations round 2 measured become ~1.
  `build.emptyOutDir` reads `process.env.ANTIPHON_VITE_KEEP_OUTDIR` so the watcher can request in-place
  rebuilds without touching `npm run build`'s behaviour. `strictPort` is deliberate: a shim whose
  child cannot bind 17203 exits non-zero and Aspire shows it red, instead of Vite quietly serving on
  a port that belongs to the session-runner or the dashboard.
- **AppHost**: `AddNpmApp("client", "../client", "serve")` — the one-word change; env, endpoint and
  `WaitFor(server)` unchanged. The shim inherits `VITE_PORT` and `services__server__http__0` and passes
  them to its children. The AppHost logs the mode it found at start next to the broker line.
- **Not the .NET server's `wwwroot`**: it would work (§1.1) but it moves the client's origin to 17202,
  which changes the Caddy contract (a links-file edit + `apply.ps1` per machine), needs a copy step
  into the server's output on every build, and would make "switch to dev" a Caddy retarget on a
  different machine's repo. The Vite process on 17203 keeps the whole switch inside this repo.
- **Local dev**: `localhost:17203` follows the same mode — built by default, with the watcher keeping
  it fresh. Someone doing HMR-driven frontend work runs `client-mode.ps1 -Mode dev` once. Simple mode
  (`dev-start.ps1`, 17282) is untouched and is always the dev server, so it remains a pure-dev path
  with no mode to think about. `restart-apphost.ps1` needs no change: it kills the tree (the shim and
  its children with it) and the shim comes back in the persisted mode; its `-NoBuild` still only
  skips `npm install`.
- **What the phone sees after S1** (from round 1/2's own measurements, not new claims): ~20 requests
  instead of 235, ~1 revalidation instead of 184, warm content ~1.25 s on loopback; the remote gap
  was pure round-trip count, so the remote number should collapse toward the loopback one. S1's
  verification (§6) measures it through Caddy, mobile-emulated, before the slice closes.

### 2.2 The phone home: gate each band, never the page

`MobileHomePage.tsx:111-117` goes. `<Stack data-testid="mobile-home">` renders on first paint and
each band owns its own loading and error state:

| Band | Query | pending | error | loaded |
|---|---|---|---|---|
| 1 Needs you / Calm | `attention` | `CardSkeleton` in CalmCard's slot — **never** `CalmCard`: calm is a claim, and a claim needs the data (the file's own doc comment) | one dimmed line, `data-testid="needs-you-error"`: "Couldn't load what needs you — retrying." (React Query keeps retrying; the 15 s poll stays) | as today |
| 2 In motion | `tasks` | `BandTitle` + two `InlineSkeleton` rows | "Couldn't load running work." | as today |
| 3 Away | `tasks` (+ boards/plans, already non-blocking) | `BandTitle` + `InlineSkeleton` | as band 2 | as today |

Use `isPending` (no data yet), not `isLoading`, so a background refetch never flashes a skeleton over
real content. `byWatchOrder`, `computeAwayDelta` and every band component are untouched — this is
the top of the function only. The skeletons come from `shared/SkeletonLayouts.tsx`; if their sizes
do not fit a phone band, add `BandSkeleton` beside them rather than inline `Skeleton` markup.

**Desktop `HomePage.tsx:143-149` — not in this pass** (§5 D4). It was not the reported symptom; its
gate is structurally different (project/workspace/agent selection needs `agents`, and rendering the
switchers empty is not obviously better than a loader); every pane below it already has its own
loading state; and after S1 the whole desktop warm load is ~1.25 s of which this gate is a fraction.
CARD-0217 gets a note: "render the header row with disabled switchers while `agents.isPending`".

### 2.3 Server: bound the git fan-out, dedupe in-flight work, kill what times out, batch the working check

**`GitProcessGate`** (new, `server/Application/Services/GitProcessGate.cs`, singleton): a
`SemaphoreSlim(MaxConcurrent)` plus two counters, `InFlight` and `Started` (the test seams). Sized by
`Git:MaxConcurrentProcesses` (new `GitSettings`, default **8**, §5 D5). `GitWorkspaceService` takes it
as an **optional** constructor parameter defaulting to a process-wide shared instance, so the three
`new GitWorkspaceService(NullLogger…)` test sites keep compiling. `RunAsync` is a leaf (it never
awaits another `RunAsync` while holding the gate), so a semaphore cannot deadlock it.

**`RunAsync` kills its child.** Restructure so the `Process` is in scope of the catch:

```
using var process = Process.Start(psi) …
try { …read/wait under timeoutCts… }
catch (OperationCanceledException)
{
    TryKill(process);                       // Kill(entireProcessTree: true), exceptions swallowed
    if (ct.IsCancellationRequested) throw;  // caller gone: same contract as today, minus the leak
    _logger.LogWarning("git {Args} timed out after {Timeout} in {Dir}; child killed", …);
    return (-1, "", "timeout");
}
```

The timeout (`GitTimeout`) and the executable name (`"git"`) move to `GitSettings` so a test can point
the service at a script that sleeps and set a 1 s deadline. This is CARD-0221's bug class in
miniature (a child outliving the row that owns it) — cite it, do not wait for it.

**`WorkspaceInfoService` in-flight map.** `GetWorkspaceAsync` becomes: cache hit → return; else
`_inFlight.GetOrAdd(key, _ => ComputeAsync(path))` and `await` that task, removing the entry in a
`finally` once it completes. `ComputeAsync` runs the git call under **`CancellationToken.None`**: the
task is shared by every concurrent caller, so the first caller's aborted request must not cancel the
second caller's answer (git's own 15 s timeout bounds it). Same shape for `GetWorktreesAsync`. With
this, a cold home load's two `workspaces` calls, N open tabs, and the 30 s poll all collapse to one
spawn pair per path per 20 s. `Clear()` also clears the map.

**`IsWorkingAsync` becomes a batch with a single-id wrapper.** Add
`internal static Task<IReadOnlyDictionary<Guid,bool>> IsWorkingBatchAsync(AppDbContext db, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)`:
the same two queries with `sessionIds.Contains(t.AgentSessionId)` and the existing `GroupBy`, then the
same two rules per id. `IsWorkingAsync(db, id, ct)` is `(await IsWorkingBatchAsync(db, [id], ct))[id]`
— **one implementation**, so the three-way lockstep AGENTS.md pins (server / client / runner) still
has exactly one server copy. `AgentService.GetAllAsync` calls the batch once for every
`Status == Running` live session before the loop. `GetByIdAsync` is unchanged (it is the single-id
wrapper already).

**Runner list deadline.** `AttachRunnerLiveStateAsync` wraps `ListAsync` in a linked CTS with
`SessionRunnerSettings.ListTimeoutSeconds` (new, default **3**, §5 D6) and adds
`catch (OperationCanceledException) when (!ct.IsCancellationRequested)` → Warning
"runner did not list sessions within {n}s; live runner state left unknown" → return. The 100 s
`HttpClient.Timeout` stays for the calls that genuinely take time (launch, kill); this is one
read-only call on the hottest page. Per AGENTS.md's rule, the caller's own token is checked before
treating the OCE as a timeout.

### 2.4 Client: one `workspaces` call, no refetch-on-mount for the task list

- `HomePage.tsx:82-93`: `gitDirs` is `[]` until **both** `agents.data` and `tasks.data` exist
  (`useWorkspaceGitInfos` is already `enabled: paths.length > 0`). Wave 2 starts at
  max(agents, tasks) instead of agents — round 2 measured those at 529 ms and 441 ms, so the delay is
  nil and the second call is gone.
- `useAgentTasks`: `staleTime: 5_000` (well under the 15 s poll). SignalR's `invalidateQueries`
  marks the key stale explicitly, so the `AgentTaskChanged` refetch path is unchanged; only the
  mount-after-resolve duplicate disappears.

### 2.5 Docs (S1)

- `AGENTS.md`, "Running Locally" → one paragraph: 17203 serves the **built** bundle by default via
  `client/scripts/serve.mjs`; `pwsh -File scripts/client-mode.ps1 -Mode dev|built|-Status`; the
  watcher; where the mode file lives; simple mode is always dev. Update the `dev-aspire.ps1:106`
  banner (drop the stale `control/client/restart` claim). Fix the Dev Port Map's 17203 row to say
  "Vite client (built bundle by default; `client-mode.ps1 -Mode dev` for HMR)".
- `docs/bootstrap.md`: the first-start flow builds the bundle on the client resource's first start;
  nothing extra to run.
- A Gotchas line: **"`localhost:17203` and the remote domain serve the built bundle; a client
  change appears after the watcher's rebuild (seconds), not instantly — `client-mode.ps1 -Status`
  shows `lastBuildAt`, and `-Mode dev` gives HMR."** This is the delegate footgun §3 names.

---

## 3. What this costs its neighbours

- **Delegates verifying UI at `localhost:17203`** see the built bundle. With the watcher on, their
  change is there after one rebuild; a check fired inside that window tests the old code. The
  Gotchas line and `-Status`'s `lastBuildAt` are the mitigation; `-Mode dev` is the escape hatch.
- **E2E** (`EnsureClientBundleIsCurrent`) is unaffected in a worktree (it looks at that checkout's
  `client/dist`) and is *helped* on the main checkout — the watcher keeps `C:\src\Antiphon\client\dist`
  current, so the staleness guard fails less often there.
- **Memory**: one extra Node process for the watcher (module graph resident; to be measured in S1
  and recorded in the doc line). `ANTIPHON_CLIENT_WATCH=0` is the off switch.
- **Orphan exposure**: unchanged in kind — `restart-apphost.ps1` already tree-kills the client;
  the shim adds one level (shim → vite) but removes the `npm`→`cmd` wrapper levels, so the tree is
  no deeper than today. A stale orphan on 17203 now fails **loudly** (`strictPort`) instead of
  drifting ports.
- **`IsWorkingAsync` callers** (`SessionMessageQueueService` ×5, `BuildQueueDtoAsync`) see no change:
  the single-id method keeps its signature and semantics.
- **`GitWorkspaceService` callers** (`AgentFilesService`, `AttentionService`'s probe, check sweeps)
  now queue behind the gate under load instead of all spawning at once — a latency trade at the tail
  in exchange for a bounded process count; git itself measured fine at 32-wide, so a bound of 8 is
  headroom, not a squeeze.

## 4. Non-goals (deferred to CARD-0217 or a future card)

- **`/api/attention`'s per-open-task git probe** (`AttentionService.cs:582-587`,
  `AgentFilesService.ProbeProgressAsync`): ~0.44 s at one open task today, O(open tasks). It does not
  share a cache with §2.3 — the probe wants `status`/`log` on a task's *own* directory since
  *dispatch time*, not repo identity — so bundling it would mean a second cache with different keys
  and TTL semantics. It **does** benefit from the process gate and the kill-on-timeout for free.
  CARD-0217 note: evaluate `TaskProgressPolicy` first and skip the probe when `elapsed < StallMinutes`
  (round 1's observation 1), then a per-directory TTL cache.
- **Pruning the gym-stat agents** — struck: not dead, and free (§1.5).
- **`/api/agents` 5 s polling, `/api/agent-tasks`' 651 KB payload and 15 s poll, the 2.5 MB unsplit
  entry chunk, code-splitting routes, the three-wave dependent chain on the desktop** — all fleet-wide
  or desktop-only; CARD-0217.
- **The desktop render gate** (§2.2 last paragraph).
- **Retry amplification** — tested and disproved in round 2; nothing here depends on it.
- **Moving the client behind the .NET server's `wwwroot`**, a Caddy/links-file change, an Aspire
  upgrade for `ResourceCommandService`, or making the client a `DaemonProcess` — none needed once
  the shim owns the swap (§1.3).
- **Storybook's links-file port drift** (§1.1 aside) — a one-line fix in another repo; mention to the
  operator, do not do it from this card.

## 5. Decisions that are the operator's — each with a recommendation

| # | Decision | Recommendation |
|---|---|---|
| D1 | Default mode when `logs/client.mode` is missing: `built` or `dev`? | **`built`.** It is what the user asked for, it is what the phone loads, and the watcher keeps it fresh enough for casual local use. |
| D2 | Watcher (`vite build --watch`) in built mode: on by default, off by default, or not built? | **On by default**, `ANTIPHON_CLIENT_WATCH=0` to disable. It is what makes "built" safe for delegates and for a merge landing while nobody restarts. Measure its RSS in S1; if it is over ~500 MB, revisit. |
| D3 | Does `-Mode dev` persist across AppHost restarts? | **Yes** — explicit state, shown by `-Status` and logged by the AppHost at start. An auto-revert timer would surprise whoever is mid-work. |
| D4 | Desktop gate in this pass? | **No** — CARD-0217, with the one-line note in §2.2. |
| D5 | `Git:MaxConcurrentProcesses` default | **8** (git measured fine at 32-wide; 8 keeps the machine responsive under the 12-loader shape round 2 used, and is trivially raised in config). |
| D6 | `SessionRunner:ListTimeoutSeconds` default | **3** (the runner answers in 5–33 ms today; 3 s is two orders of magnitude of headroom before the agent list degrades to "unknown"). |
| D7 | Tell the links repo owner about the Storybook `17283`→`17209` drift? | Yes, as a one-line note in the card's close-out; no work here. |

## 6. Slices, tiers, tests

Dispatch routing per this session's rule (plan → Fable/Opus; simple builds → Codex terra; verify →
Codex luna; else Grok). Both paid providers ran dry on the night of 2026-08-26 — confirm credits
before dispatching S1/S3.

| Slice | What | Tier / workspace | Tests (all red-before-green) |
|---|---|---|---|
| **S1** | `client/scripts/serve.mjs`, `"serve"` npm script, `vite.config.ts` (shared const, `strictPort`, `preview`, assets-cache plugin, `emptyOutDir` env), `AppHost/Program.cs` one-word change + mode log line, `scripts/client-mode.ps1`, docs (§2.5). **Verification is live, through Caddy**: merge → `restart-apphost.ps1` → browser-harness against `https://antiphon.desktop.codeperf.net`, 390×844 iPhone UA, cache disabled and warm: record request count, revalidation count, `#root` first-content time, dock time; then `-Mode dev` → `/@vite/client` present, HMR works; `-Mode built` → back. Record the watcher's rebuild time and RSS in the doc line. | Grok, worktree (nothing else touches these files) | vitest for the shim's pure parts (mode parsing, spawn-plan for each mode, swap decision when the file changes; spawn injected). A pwsh Pester-free smoke in `scripts/`: `client-mode.ps1 -Status` prints one of the known verdicts. |
| **S2** | `MobileHomePage.tsx` per-band states (§2.2) | Codex terra, worktree | `MobileHomePage.test.tsx`: (a) both pending → `mobile-home` present, band titles present, `calm-state` **absent**, skeletons present; (b) tasks resolved while attention pending → In-motion rows render, band 1 still skeleton; (c) attention error → `needs-you-error` present, `calm-state` absent; (d) existing tests unchanged. |
| **S3** | `GitProcessGate`, `GitSettings`, `RunAsync` kill-on-timeout, `WorkspaceInfoService` in-flight map (§2.3) | Grok, worktree | TUnit, `[Category("GitIntegration")]` with `ScratchGitRepo`: 16 concurrent `GetWorkspaceInfoAsync` → `gate.PeakInFlight <= 8` and all succeed; two concurrent `GetWorkspacesAsync` of one cold path → `gate.Started` delta == 2 (one `rev-parse`, one `branch`); a caller cancelling the first call does not fault the second. Kill test `[ParallelLimiter<ProcessSpawnLimit>]`: executable = a pwsh script that sleeps 60 s, timeout 1 s → returns `timeout` and the child pid is gone within 2 s. |
| **S4** | `IsWorkingBatchAsync` + single-id wrapper, `GetAllAsync` batch call, runner list deadline (§2.3) | Grok, shared or worktree (server-only; serialise after S3 — same files' neighbours) | `SessionMessageQueueServiceTests`: batch over {working, idle, no-transcript} sessions matches three single calls; existing working-rule tests untouched and green. `AgentServiceIntegrationTests`: a fake `ISessionRunnerClient` whose `ListAsync` never completes → `GetAllAsync` returns within the (test-shortened) deadline with `TranscriptBinding` null; DB statement count for `GetAllAsync` with 12 running sessions is the batched number, not 2×12 (Postgres statement log or EF interceptor, whichever the harness already has). |
| **S5** | `gitDirs` both-loaded guard, `useAgentTasks` `staleTime` (§2.4) | Codex terra, worktree | `HomePage.test.tsx`: msw counter — `/api/filesystem/workspaces` requested **once** with the union of agent + active-task dirs; `/api/agent-tasks` requested once when a second subscriber mounts within 5 s. |

Order: S1 first and alone (it is the fix, and its verification needs the live stack); S2 and S5 are
independent of everything and of each other; S3 then S4 on the server. Close-out re-runs S1's
remote measurement after everything is merged and records the before/after in the card.
