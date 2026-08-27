# CARD-0216 round 2 - the post-load spinner (opus, 2026-08-27)


Brief: reproduce the user's "spinner for 30-60 seconds AFTER the page has loaded, seems to be a
slow API call". Method: browser-harness against the CDP Edge on :9222, with a
`Page.addScriptToEvaluateOnNewDocument` probe installed BEFORE app boot that wraps `fetch`/`XHR`,
polls for `.mantine-Loader-root`/`.mantine-Skeleton-root` every 250 ms, and dumps
`PerformanceResourceTiming` split into stall / server / download. Plus `curl`, plus concurrency
load generators, plus a `Win32_Process` census of `git.exe`.

**Verdict up front: it is NOT a slow API call, and there is no single 30-60 s endpoint. On
localhost every home-page API call resolves in under 2 s, even under 12 concurrent git-heavy
requests. What costs tens of seconds is the CLIENT BOOTSTRAP, and the variable that turns round
1's "single-digit seconds" into the user's 30-60 s is ROUND TRIPS, not bytes and not server time:
the Vite dev server needs 235 requests to become interactive and revalidates 184 of them on EVERY
load, warm cache included. That is free on loopback and ruinous on any real link.** Served through
this machine's own Caddy HTTPS front door the same cold load already goes from 6.5 s to 20.6 s.

### 1. What actually spins, and the false positive that looks exactly like the complaint

A 90-second instrumented observation of a warm home load found exactly one spinner that never
resolves - still spinning at 85,704 ms, with **zero pending network requests**. It is not a loader.
It is `ActivityBadge` in the agent rail (`client/src/features/home/AgentRail.tsx:126-138`), which
renders `<Loader size={8} type="dots"/>` inside a yellow "Working" badge whenever
`agent.working` is true. It is a live-status indicator; it spins for as long as the agent is
mid-turn, which is routinely minutes.

**This is worth taking seriously as a candidate for what the user is describing.** It is an
animated spinner, on the home page, that persists long after the page has loaded, and nothing about
it distinguishes it from a stuck loading state. If the reported symptom is "the little spinner in
the agent list keeps going", the answer is that it is behaving correctly and the AFFORDANCE is
wrong, not the performance. A Plan pass should ask the user which spinner they mean before
optimising anything.

Everything else on the page resolves: the two genuine load spinners in the same run cleared at
~5 s and ~10 s.

### 2. The measured waterfall, and the reverse-proxy comparison that reproduces the band

Same browser, same API server, same minute. "dock" = `[data-testid="home-dock"]` present, i.e. the
page has real content.

| | localhost:17203 | https://antiphon.desktop.codeperf.net (Caddy -> same Vite, h2) |
|---|---|---|
| protocol | http/1.1 | h2 |
| **cold (cache disabled), dock at** | **6,451 ms** | **20,557 ms** |
| `loadEventEnd` cold | 3,453 ms | 20,104 ms |
| last module `responseEnd` | 2,850 ms | 10,731 ms |
| worst single-module stall | 885 ms | 4,874 ms |
| transferred bytes, cold | 16,083,667 | 16,051,385 |
| `#root` still EMPTY until | ~3.4 s | **~16.4 s** |
| **warm cache, dock at** | 5,371 / 5,512 ms | 4,787 / 4,567 ms |
| resources, warm | 221-229 | 232-235 |
| **of which revalidated (304)** | **184** | **184** |

Two things fall out of this table.

**(a) The same page, same machine, same second, is 3.2x slower through the HTTPS front door cold -
20.6 s versus 6.5 s - with identical bytes.** The delta is entirely stall: worst per-module stall
goes 885 ms -> 4,874 ms. That is queueing on round trips, not bandwidth and not the API.
`https://antiphon.desktop.codeperf.net/` is live and serves the DEV server (the response contains
`/@vite/client` and `/@react-refresh`), and `client/vite.config.ts:31` explicitly allowlists
`.desktop.codeperf.net`, `.laptop.codeperf.net`, `.localhost.codeperf.net` - so remote access is a
supported, configured path, not a hypothetical.

**(b) 184 of ~235 resources are revalidated on EVERY load, warm cache included.** Vite serves source
modules `Cache-Control: no-cache`, so a warm revisit is still 184 conditional round trips before
the app can mount. On loopback a 304 costs ~1 ms and this is invisible. Off-box it is 184 x RTT
before first paint, and under HTTP/1.1's 6-connections-per-origin limit those serialise ~31 deep:
at 50 ms RTT that alone is ~9 s, at 150 ms RTT ~28 s, before a single API call is made. **That is
the 30-60 s band, and it is a round-trip-count problem, which is exactly why round 1's loopback
measurements - correct as they were - could not see it.**

I could NOT reproduce 30-60 s on localhost. Ten instrumented loads, warm and cold, with and without
concurrent load, all landed between 3.0 s and 20.6 s to content. State this plainly to the user:
**if they are loading `http://localhost:17203`, the mechanism below is a different (smaller)
problem, and the first question is what URL and what device they see the 30-60 s on.**

### 3. The API calls are NOT the bottleneck in the browser - measured, not assumed

Full fetch-level capture of a warm home load (ms from navigationStart, `+n` = duration), which also
settles round 1's three-wave question:

```
4104 +1980  /api/attention            5075 +1235  /api/agents/{id}/files?since=checkpoint
4107 + 529  /api/agents               5076 +1009  /api/agents/{id}/review/threads
4108 + 441  /api/agent-tasks          5081 +1006  /api/filesystem/workspaces  (32 paths)
4110 + 366  /hubs/antiphon/negotiate  5082 +1002  /api/filesystem/worktrees
4585 +1498  /api/filesystem/workspaces (1 path)   6101 + 209  /api/sessions/{id}/transcript
4588 +1495  /api/filesystem/worktrees             6343 + 124  /api/agents/{id}/files/commits
5071 +1016  /api/agent-tasks  (2nd)               6404 +  18  /api/sessions/{id}/transcript
```

Everything is done by 6.8 s and nothing exceeds 2 s. **The first API call does not fire until
t=4,104 ms** - the whole API story starts after four seconds of module loading. Re-running the same
load with 8 concurrent server-side git-heavy loaders running throughout: still **zero** API calls
over 2 s in the browser; content at 9.7 s, dock at 14.2 s.

Round 1's three dependent waves are confirmed and are real (they cost ~2.3 s of serialised round
trips here), but they are second-order next to the 4 s that precedes them.

I also swept every per-agent call the page can make, across all 48 agents and 12 live sessions, to
rule out "a different widget nobody has looked at": worst `files?since=checkpoint` is 7.97 s cold /
0.93 s warm (Torquay Leander) and 4.10 s cold / 1.07 s warm (Antiphon, 320,934 bytes); every
`/sessions/{id}/transcript` is 8-61 ms (largest 1,303,574 bytes, Family); every `review/threads` and
`files/commits` is under 220 ms. **Nothing is anywhere near 30-60 s.**

### 4. Server-side: real contention, well short of the reported band

Load-dependence was tested directly. Concurrency waves of the exact home-page call set (per-endpoint
worst of each wave):

| endpoint | 1x (7 reqs) | 3x (21) | 5x (35) |
|---|---|---|---|
| `files?since=checkpoint` (Antiphon) | 9.95 s | 16.34 s | 22.66 s |
| `/api/attention` | 1.74 s | 2.28 s | 14.07 s |
| `/api/agents` | 1.27 s | 1.44 s | 12.55 s |
| `/api/agent-tasks` (no git at all) | 0.47 s | 0.74 s | 13.98 s |

`/api/agent-tasks` touches no git, so its blow-up proves the contention is **server-wide, not
endpoint-local**. Re-run with proper keep-alive connections (the numbers above, and an earlier 56 s
outlier, were inflated by my own harness opening a fresh TCP connection per request - discounted,
do not quote them as server latency), the honest figure is: under 12 concurrent git-heavy requests
`/api/agent-tasks` goes from **0.088 s min / 0.091 s median quiet** to **0.174 s min / 0.501 s
median / 8.21 s max**, and `/health` stays under 0.14 s. Real, worth fixing, not 30-60 s.

During the heaviest run the server process sat at **~0-4% CPU with a flat 56-57 threads** while a
request took tens of seconds - so this is queueing on external processes, not CPU and not
thread-pool starvation.

Isolated control: 32 concurrent `git status --porcelain -z --untracked-files=all` on
`C:\src\Antiphon` complete in 1,485 ms wall, ~98 ms each. **Git itself scales fine on this machine**;
the cost is how the server orchestrates it.

### 5. Root causes in code, with the current live numbers

- **`WorkspaceInfoService.GetWorkspacesAsync` fans out unboundedly and has no in-flight dedupe.**
  `server/Application/Services/WorkspaceInfoService.cs:39` is
  `await Task.WhenAll(distinct.Select(p => GetWorkspaceAsync(p, ct)))` over up to
  `MaxPathsPerRequest = 64` paths (`:20`), each of which runs **2 sequential git spawns** -
  `rev-parse --path-format=absolute --show-toplevel --git-common-dir` then `branch --show-current`
  (`GitWorkspaceService.cs:497,516`). The 20 s TTL cache (`:16`) is written only AFTER the git call
  returns and there is no in-flight map, so N concurrent callers for the same cold path each spawn
  their own storm. **Today the home page sends 32 distinct paths** (built from 48 agents + active
  tasks at `HomePage.tsx:82-93`), so one cold call is ~48 git processes in two parallel bursts -
  and the page fires this endpoint **twice per load** with different path sets, plus once per
  additional open tab. Measured: 0.79-0.93 s cold for 32 paths, 0.002-0.024 s warm.
  **8 of those 32 directories do not exist on disk** (`C:\src\gym-stat-auth`, `-datamodel`,
  `-deploy`, `-fieldeditor`, `-floorplan`, `-install`, `-logging`, `-offline`) - dead agent rows
  widening the fan-out for nothing.

- **`GitWorkspaceService` has no concurrency limit, no cache, and leaks a timed-out child.**
  `server/Application/Services/GitWorkspaceService.cs:592-632`: every call is a bare
  `Process.Start`, and on the 15 s timeout (`:16`, `:616`) the `OperationCanceledException` is
  caught at `:622` and the git process is **never killed** - `using var process` disposes the
  wrapper, not the child. Nothing throttles how many run at once. (Census taken during load: only
  one long-lived `git.exe` on the machine, 3.8 days old, a `-C \\?\C:\src\Antiphon rev-list
  --left-right --count` that GitWorkspaceService cannot have produced - it never passes `-C`. So
  the leak is latent, not currently biting. Fix it anyway; a 15 s timeout that abandons its child is
  a bug waiting for a slow repo.)

- **`AgentService.GetAllAsync` is an N+1 and calls the session runner on every request.**
  `server/Application/Services/AgentService.cs:82-91` awaits `IsSessionWorkingAsync` **inside** the
  per-agent loop (2 DB queries per running session; 12 live sessions today), and
  `AttachRunnerLiveStateAsync` (`:251`) makes an HTTP call to the session-runner on :17204 for
  every `/api/agents`. That client's timeout is **100 seconds**
  (`server/Program.cs:214-218` x `SessionRunnerSettings.RequestTimeoutSeconds = 100`,
  `server/appsettings.json:49`). The runner answers in 5-33 ms today, but there is no shorter
  deadline standing between a wedged runner and this endpoint.

- **`/api/agents` is the single render gate for the whole home page and is polled every 5 s.**
  `HomePage.tsx:143-149` returns a bare centred `<Loader/>` for the entire page body while
  `agents.isLoading`; `client/src/api/agents.ts:433-438` sets `refetchInterval: 5000`. So one slow
  `/api/agents` blanks the page to a spinner, and each open tab costs one `/api/agents` (and one
  runner HTTP call, and ~24 DB queries) every five seconds forever.
  Note for the Plan pass: I tried to demonstrate retry-amplification here (`new QueryClient()` at
  `App.tsx:28` takes React Query's defaults, i.e. `retry: 3` with 1/2/4 s backoff, which would keep
  `isLoading` true across 4 attempts) and **it did not reproduce** - failing `/api/agents` in-page
  produced one attempt and the page rendered anyway at 4.4 s. Do not build a fix on that theory.

- **Round 1's `/api/attention` git N+1 has not grown.** Current live counts: 534 tasks
  (482 Succeeded, 36 Failed, 15 Canceled, **1 Dispatched**), 6 attention items (5 `RecentFailure`,
  1 `RecentCriticalIncident`), 48 agents, 13 worktrees. Same single open task as round 1, so
  `AttentionService.cs:582-587`'s ~274 ms-per-open-task git probe is still ~0.44 s isolated. It is a
  scaling landmine, not today's problem.

### 6. Is it intermittent or load-dependent?

Load-dependent on the server side (section 4) but bounded: the worst honest server figure is 8.2 s
on a tail. Deterministic on the client side: **every** load pays 235 requests / 184 revalidations,
warm or cold, so a remote client pays that cost **every time**, and a localhost client never feels
it. That split is the single most useful diagnostic question to put to the user.

### 7. What a fix pass should target, ranked by measured payoff

1. **Serve the built bundle, not the dev server, for any access that is not active development** -
   round 1 measured 234 requests -> 19 and warm content 3.0-3.9 s -> 1.25 s. On the round-trip
   analysis above this is worth far more remotely than locally: 184 revalidations -> ~0.
2. **Confirm the URL/device the user actually sees this on** before anything else, and confirm which
   spinner (section 1's "Working" badge is a live plausible answer).
3. Bound the git fan-out: a `SemaphoreSlim` in `GitWorkspaceService.RunAsync`, an in-flight
   `Task` dedupe map in `WorkspaceInfoService`, and kill the child on timeout.
4. Prune the 8 dead agent directories; stop sending `/api/filesystem/workspaces` twice per load.
5. Give the home page a render gate that is not one unbounded network call, and give the runner
   call a deadline shorter than 100 s.
6. Hoist `IsSessionWorkingAsync` out of the `GetAllAsync` loop.

### Method notes / what was NOT done

No production code was touched; `git status` is clean. Server logs carry no request-timing lines
(`Microsoft.AspNetCore` is at Warning), so historical slow requests could not be mined - everything
above is a fresh measurement. The `Antiphon.Server` process was sampled with `Get-Process`, git with
`Get-CimInstance Win32_Process`. Two earlier load runs used a harness that opened a new TCP
connection per request and produced 46 s and 56 s figures; those were **client-side connection
churn**, were re-run with keep-alive, and are explicitly discounted above rather than quoted.
The claim that 184 revalidations x RTT reaches 30-60 s on a remote link is arithmetic from measured
request counts, not a measurement taken from the user's device - the proxy row (20.6 s cold on this
machine) is the measured half of it.
