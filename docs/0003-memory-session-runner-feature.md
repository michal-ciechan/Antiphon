# SessionRunner memory growth: unbounded transcript retention + Server GC

**Status:** investigation complete — proposed fixes below, not yet implemented
**Date:** 2026-08-01
**Trigger:** "Can we profile the .NET processes to see why they are using so much CPU" — profiling
found CPU was a non-issue (.NET totalled 2.8% of the machine) but surfaced a real memory problem.
**Related:** none yet — first doc on this subsystem's memory behaviour.

---

## TL;DR

`Antiphon.SessionRunner` is one shared, always-on daemon (port 17204) that tracks every Claude
Code session started on the machine, not just Antiphon's own. Each session gets a
`TranscriptTailer` that appends every parsed transcript line — including the full `Text` and
`ToolInput` strings — to an in-memory `List<RunnerTranscriptEvent>` that **is never trimmed**.

Worse: when a session's process exits, it is **not removed from the runtime's session
dictionary** — only its status flips to `"Exited"` in place. The session (and its full transcript
history) stays resident for the entire remaining uptime of the daemon. Since this daemon is
designed to be always-on and rarely restarted, memory grows monotonically for as long as it's up,
across every session it has ever hosted.

Measured 2026-08-01: 7 sessions tracked, one running continuously for 4 days with **355,465**
transcript-line sequence numbers, LOH at 1.26 GB, working set 666 MB → 944 MB over the course of
a single conversation. `System.GC.Server` is also `true` in the built `runtimeconfig.json`, which
is a poor fit for a lightweight background daemon and makes .NET slower to reclaim what it could.

Two changes proposed:
1. **Bound the in-memory transcript history** (§3).
2. **Switch from Server GC to Workstation ("Desktop") GC** (§4).

---

## 1. Background — how this was found

Profiling was prompted by a CPU complaint. Sampling live CPU deltas (not `Get-Process().CPU`,
which is cumulative since start and misleading) showed .NET was never the problem — Defender
scanning an unexcluded `C:\src`, the WSL/Docker VM, and the Claude sessions themselves accounted
for the load. But `dotnet-counters` on `Antiphon.SessionRunner` showed:

- LOH (large object heap) **1.26 GB**, POH 259 MB, committed 1.62 GB.
- **Zero gen0/gen1/gen2 collections in a 15s sample** while allocating only ~409 KB/5s — the heap
  is being *held*, not churned.
- Working set climbed **666 MB → 944 MB** during one conversation.

## 2. Root cause

### 2.1 Per-session transcript history has no cap

`TranscriptTailer` (`src/Antiphon.SessionRunner/TranscriptTailer.cs`) tails each session's Claude
Code JSONL transcript and, for every parsed line, appends a `RunnerTranscriptEvent` to:

```csharp
private readonly List<RunnerTranscriptEvent> _entries = new();   // TranscriptTailer.cs:44
```

`RunnerTranscriptEvent` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:77-98`)
carries the **full** `Text` and `ToolInput` strings for that line — file contents, command output,
web-fetch results, whatever the tool call produced. Nothing ever removes an entry: no cap on
count, no cap on total bytes, no age-based eviction, no "keep metadata, drop the body after N
minutes" compromise. `Snapshot()` (used by the `/sessions/{id}/snapshot` catch-up endpoint) just
returns `_entries.ToArray()` — a full copy of everything, every time it's called.

### 2.2 Exited sessions are never released — only the whole daemon shutting down clears them

`SessionRunnerRuntime` keeps sessions in `ConcurrentDictionary<Guid, RunnerSession> _sessions`
(`SessionRunnerRuntime.cs:20`). Searching every `TryRemove` call against it:

| Line | When it fires |
|---|---|
| `SessionRunnerRuntime.cs:73` | Only on a **start failure** (rollback), not on normal exit |
| `SessionRunnerRuntime.cs:315` | Only inside `DisposeAsync()` — i.e. **the whole runtime shutting down** |

`SessionLivenessSweepService` (`SessionLivenessSweepService.cs`) runs periodically but only calls
`SweepVanishedSessions`, which **marks** a vanished process `Exited` in place
(`SessionRunnerRuntime.cs:521,651,707-710`) — it never removes the entry. So a session that ran
for five minutes and exited hours ago is still sitting in `_sessions` with its `TranscriptTailer`
and full `_entries` list alive, held by the dictionary, for as long as the daemon keeps running.
Per `CLAUDE.md`, this daemon is explicitly designed to be **always-on** (Scheduled Task
"Antiphon Session Runner", survives reboot, adopted rather than restarted by the AppHost) — so in
practice it can run for weeks, accumulating every session ever hosted in that window.

### 2.3 Measured blast radius (2026-08-01, `GET http://localhost:17204/sessions`)

| pid | started | transcript lines (`lastSequence`) |
|---|---|---|
| 179240 | 2026-07-28 18:41 (4 days) | **355,465** |
| 18416  | 2026-07-28 21:57 | 91,045 |
| 191264 | 2026-07-28 23:14 | 42,979 |
| 203928 | 2026-07-28 18:41 | 36,903 |
| 35624  | 2026-08-01 15:56 | 9,611 |
| 199832 | 2026-07-28 18:41 | 8,270 |
| 117312 | 2026-07-31 21:03 | 55 |

Total ≈ 544,000 retained transcript events across 7 sessions in a single daemon process, none of
them evictable short of restarting it.

### 2.4 Compounding factor: Server GC

The built binary's `runtimeconfig.json` has `"System.GC.Server": true` (default for
`Microsoft.NET.Sdk.Web` unless overridden). Server GC allocates one heap **per logical core**
(8 on this desktop) with large per-heap collection thresholds — tuned for high-throughput request
servers, not a low-traffic background daemon. It doesn't cause the retention above, but it means
.NET commits more memory up front and is slower to return it, amplifying the effect of §2.1/§2.2.

### 2.5 Related, unconfirmed: unbounded per-subscriber SSE channels

`SessionRunnerEventHub` (`SessionRunnerRuntime.cs:793-829`) hands each subscriber an
`Channel.CreateUnbounded<RunnerServerSentEvent>`. If a consumer's cancellation token doesn't fire
cleanly on disconnect (a dropped connection that doesn't trigger `ct.Register`'s callback), events
would queue on that channel forever, referenced by `_subscribers`, invisible to any cap on
`_entries`. Not verified as happening — flagged here so it's checked if the primary fix doesn't
fully explain observed growth.

## 3. Proposed fix — bound the in-memory transcript history

Needs a decision on shape before implementing; options, roughly cheapest-to-most-work:

- **A — hard cap by count**: cap `_entries` at N most-recent lines (ring buffer / `Deque`), drop
  the oldest. Simplest; loses `/snapshot` fidelity for long sessions past the cap.
- **B — cap by content size, keep structure**: once a session exceeds an age/count threshold,
  null out `Text`/`ToolInput` on older entries (keep `Sequence`, `Kind`, `Uuid`, `Role`,
  `Timestamp`, token counts) so ordering/dedup/token-accounting still work but the bulk of the
  memory (the big strings) is released. Better fidelity than A, more surface area to get right.
  Consumers reading through `/transcript`/SSE for the live tail are unaffected either way, since
  they only care about recent lines.
  - **B is probably the right shape**: the large payloads (file reads, tool outputs) are the ones
    least likely to be needed again once several hundred lines have passed, but the structural
    metadata is cheap and still useful for the full-session record.
- **Eviction trigger**: on session exit — actually remove the `RunnerSession` (and its tailer)
  from `_sessions` after some grace period (not immediately, in case of the resync/re-adoption
  path in §2.2 — but *eventually*, not "only when the daemon restarts"). This closes §2.2
  independent of whichever of A/B is chosen for live sessions.
- Precedent in the same process: `AuditCleanupService` already does periodic best-effort pruning
  for the on-disk PTY-audit dumps (age + count caps, every 30 min). The same pattern — a
  `BackgroundService` on a `PeriodicTimer` — fits an in-memory trim/evict pass too, and could
  plausibly live right next to it or be folded into the existing service.

**Open questions before implementing:**
- What's an acceptable cap for `/snapshot`'s "catch-up" use case — is there a real consumer that
  needs the *entire* multi-day history, or just enough to resync a reconnecting UI?
- Grace period for removing exited sessions from `_sessions` — long enough that the re-adoption
  path (a runner restart re-attaching to a still-live pty-host) never gets confused, short enough
  to actually bound memory.
- Byte cap vs count cap vs age cap for option B — probably whichever is cheapest to reason about
  first, revisit if it doesn't hold.

## 4. Proposed fix — switch to Workstation ("Desktop") GC

Set in `Antiphon.SessionRunner.csproj` (or the shared `Directory.Build.props`, if it should apply
repo-wide to other long-running-but-low-throughput daemons like `Antiphon.PtyHost`):

```xml
<PropertyGroup>
  <ServerGarbageCollection>false</ServerGarbageCollection>
</PropertyGroup>
```

Rationale: this is a background daemon serving a handful of local clients, not a high-throughput
web server — Server GC's one-heap-per-core design and large collection thresholds are the wrong
trade-off here and directly explain why 944 MB resident produced zero collections in a 15s
sample. Workstation GC collects more eagerly and returns memory to the OS more readily, which
should reduce steady-state footprint independent of the §3 fix, and reduces the amount of memory
any single unbounded-retention bug (like §2.1/§2.2) can hide behind before it's visible.

**Open question:** confirm this doesn't measurably hurt request latency/throughput for the
`Antiphon.Server` API process too, if the same property change is applied there — SessionRunner
is the one with clear evidence for it; Server's case is weaker (its own CPU/GC profile was
healthy) and probably shouldn't be changed on the strength of this doc alone.

## 5. Non-goals / what this doc does not cover

- No code changes have been made — this is a spec only, written for review before implementation
  (the codebase was being actively edited elsewhere while this was investigated; changes here
  should be coordinated rather than dropped in blind).
- Does not address `Antiphon.PtyHost`'s own memory behaviour — out of scope, not profiled here.
- Does not resolve the unrelated finding from the same profiling session: `Antiphon.Server`
  throwing an `IOException` every 5 seconds. Separate issue, separate doc if pursued.
