# CARD-0112 — A stale session-runner is invisible until it silently kills a working delegate: plan

**Date:** 2026-08-21
**Status:** planned (not implemented)
**Card:** CARD-0112 (`abe3eec0-fb12-49a0-8896-c6b110c1f0d4`) — stale session-runner daemon silently
serves old transcript-binding logic and gets a healthy Codex delegate killed.
**Precedent:** CARD-0056 (`GET /capabilities` + `PtyDeliveryProfile` — the one existing cross-process
capability check between server and runner, and the template here), CARD-0099 S1
(`CodexTranscriptTailer`, the code that was merged and stayed inert), CARD-0020 /
`FailNeverStartedAsync` (the watchdog that acted on the stale daemon's refusal), CARD-0055 (pull
before you judge; withhold the destructive half when the evidence is known-blind), CARD-0085
(`TryRecoverBindRefusalAsync`), CARD-0064 (the "the delegate may have been WORKING all along" warning
this reproduced), CARD-0006 (the C1–C4 binding rules the Claude tailer was correctly applying to a
Codex session), the pty-host-split spec (why a runner restart is safe, and what it still costs).
**Evidence:** the live stack and working tree, measured 2026-08-21. Running session-runner PID 41032;
`GET :17204/capabilities` and `:17204/health` answered; `git log` over the last 30 days; assembly
metadata read off the built DLLs on disk.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The fix is smaller than the card assumes, because the build identity it asks for is already baked
into every assembly by the SDK and nobody reads it. But a build-SHA comparison is the WRONG primary
signal — measured today it cries wolf on a perfectly correct daemon — and the mechanically exact
signal is one the runner can state directly: which transcript formats it can actually tail. Three
layers: the runner declares its formats and its build stamp, the server refuses to launch a kind the
runner cannot tail, and the watchdog stops killing sessions it has no way to see.**

| Question | Answer, on this evidence |
|---|---|
| Does a build-identity stamp already exist? | **Yes, on every assembly, free.** SDK 10.0.300's default SourceLink sets `SourceRevisionId`, so `AssemblyInformationalVersion` is already `1.0.0+<40-hex git sha>`. Nothing reads it except `PtyHostServer`, which reads it and never compares it. No new MSBuild machinery is needed. |
| Is the stack running stale code right now? | **The runner is 2 commits behind HEAD, and 0 of them are runner-side.** Built from `1a9dec7`, HEAD is `ca547da`. A naive "sha != HEAD" warning would fire today, on a daemon that is exactly right. |
| How often does this class of staleness arise? | **46 of 431 commits in 30 days (10.7%) touch the runner's project closure, spread over 19 of 30 days.** On roughly two days in three, the running daemon goes stale in substance at least once. |
| Was the live miss a one-in-a-thousand window? | No. `d3a674a` (17:08) was the **only** runner-side commit between the daemon's 13:44 start and the failing dispatch — the window is normal-sized, not freak. |
| Can the runner tell the server what it supports? | **Yes, and it already does for exactly one thing.** `GET /capabilities` (CARD-0056) reports the pty backend, and `PtyDeliveryProfile` already refuses to trust its own environment without the runner's corroboration. Same shape, one more field. |
| Is a git-based check possible server-side? | Possible but wrong. The server would have to shell out to `git` from a service; and the sha answers "which commit" while the question is "can you tail a Codex rollout". Git stays in PowerShell, where it belongs. |
| Should the AppHost auto-restart a stale runner? | **No.** A restart drops every pty-host pipe, re-runs adoption with `/sessions` down, re-binds transcripts under C1–C4, and rebuilds first — a build failure leaves the daemon in a 5-second retry loop. The signal is noisy (row 2). Warn loudly, name the one command. |
| Does the same class affect other adopted daemons? | **fake-gateway (17208): same adoption path, currently stale in date and fresh in substance** — built 2026-08-17 from `e0d3340`, **0** commits touching it since. **Adopted pty-hosts: yes, and their version is already on the wire, unread** (`HelloAckMessage.HostVersion`). Client/Storybook: npm `dev`, no compiled artifact — not in this class. |

## 1. The mechanism, in the code

`SessionRunnerHttpClient.StartAsync` (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:40`)
computes `TranscriptFormat` from the agent kind and posts it. The runner's `SessionRunnerRuntime`
(`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:511-570`) dispatches on that string:

```
if  (TranscriptEnabled && format == "grok")   -> GrokTranscriptTailer      (deterministic path)
else if (TranscriptEnabled && format == "codex") -> CodexTranscriptTailer  (CARD-0099 S1)
else if (TranscriptEnabled)                   -> Claude discovery          <-- the default
```

The last branch has no guard. A runner built before `d3a674a` has no `codex` arm at all, so
`TranscriptFormat: "codex"` falls through to Claude's per-cwd discovery, which searches
`~/.claude/projects/<enc-cwd>/*.jsonl`, correctly refuses every candidate under C1–C4 (none of them
is a Codex conversation, and none carries a prompt this session sent), and logs the refusal. That
refusal is indistinguishable, from the server's side, from "this session never wrote anything" —
`TranscriptPromptSpan.HasTurnPromptSinceAsync` sees no `UserPrompt`, and
`AgentTaskDispatcher.FailNeverStartedAsync` (`server/Application/Services/AgentTaskDispatcher.cs:388`)
fails the task and kills the session.

Three correct components; one false kill. The unguarded `else` is the whole defect surface, and the
one line of it that matters is that **an unrecognised format is silently downgraded rather than
refused**.

## 2. What already exists (do not invent any of this)

### 2.1 The git SHA is already in every binary

Measured on disk, 2026-08-21:

| Assembly | `AssemblyInformationalVersion` | Built |
|---|---|---|
| `Antiphon.SessionRunner.dll` | `1.0.0+1a9dec7aad3b81330bb1463ee92cd5f757dd3fe1` | 2026-08-20 23:53:47 |
| `Antiphon.PtyHost.dll` | `1.0.0+1a9dec7aad3b81330bb1463ee92cd5f757dd3fe1` | 2026-08-20 23:53:37 |
| `Antiphon.Server.dll` | `1.0.0+1a9dec7aad3b81330bb1463ee92cd5f757dd3fe1` | 2026-08-20 23:33:12 |
| `Antiphon.Messaging.FakeGateway.dll` | `0.1.0+e0d33402cbf0066b73835705ad422cf3fff9aa71` | 2026-08-17 15:50:55 |

There is no `Version`/`SourceRevisionId` property anywhere in `Directory.Build.props` or any
`.csproj` — this is the .NET SDK's default SourceLink behaviour (`global.json` pins SDK `10.0.204`,
`rollForward: latestMinor`; 10.0.300 in use). `PtyHostServer.cs:16-18` already reads it into
`HostVersion` and puts it on the wire in every `HelloAckMessage`. **Nothing ever compares it.**

Two honest limits of that stamp, which is exactly why it is layer 2 of this design and not layer 1:

- **It does not mark a dirty tree.** A binary built from a working tree with uncommitted edits
  carries the last commit's SHA. `stamp == HEAD` does not prove the binary matches the source.
- **It cannot see a skipped rebuild of an unchanged commit.** It is evidence about *which commit the
  binary was built from*, and nothing more.

### 2.2 `/capabilities` is already the cross-process channel

`src/Antiphon.SessionRunner/Program.cs:117-122` serves `RunnerCapabilitiesDto`; the server consumes
it in `PtyDeliveryProfile.ProbeAsync` (`server/Application/Services/PtyDeliveryProfile.cs:122-186`)
with a 5-minute `ProbeTtl`, a 5-second `ProbeTimeout`, a cached snapshot, and one rule stated three
separate times in its own comments: **null is no evidence, never proof**. `ISessionRunnerClient`'s
default implementation returns null so every in-proc and fake client lands there safely
(`server/Application/Interfaces/ISessionRunnerClient.cs:16`).

That discipline is the template. This card adds fields to the same DTO and a second consumer with
the same null rule.

### 2.3 The server already knows what it wants

`SessionRunnerHttpClient.TranscriptEnabledFor(AgentKind)` and `TranscriptFormatFor(AgentKind)` are
public static and derive from `ProviderContractCatalog`. The gate in §3.2 needs no new mapping — it
compares the value these already produce against the list the runner declares.

## 3. Design

### 3.1 Layer 1 — the runner states what it can tail, and what it was built from

`RunnerCapabilitiesDto` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:499`) grows
two trailing optional members, so the record stays wire-compatible in both directions (a new server
reading an old runner's body gets nulls; an old server reading a new runner's body ignores the extra
fields):

```csharp
public sealed record RunnerCapabilitiesDto(
    string PtyBackend,
    string PtyBackendRequested,
    string PtyBackendReason,
    bool PtyBackendFellBack,
    IReadOnlyList<string>? TranscriptFormats = null,   // CARD-0112
    RunnerBuildDto? Build = null);

public sealed record RunnerBuildDto(
    string InformationalVersion,    // "1.0.0+<40-hex>", straight off the assembly attribute
    string? CommitSha,              // parsed out of it; null when the SDK stamped no revision
    DateTime AssemblyWriteTimeUtc,  // build time of the running assembly
    DateTime ProcessStartUtc);      // how long this binary has been serving
```

`TranscriptFormats` must be sourced from **the runner project, next to the switch that implements
it** — not from the shared `TranscriptFormats` constants class. The constants class is a list of
format names that exist in the contract; the capability list must be a list of format names *this
binary has a branch for*. A test asserts the two agree in the current tree, so adding a constant
without an arm fails the build rather than shipping a lie.

`Build` is resolved once at startup (unlike `PtyBackend`, which `Program.cs` deliberately resolves
per-request because the flag can change under a restart — the build identity cannot).

### 3.2 Layer 2 — the runner refuses an unknown format instead of downgrading it

In `SessionRunnerRuntime`, the final `else if (request.TranscriptEnabled)` becomes explicit:

- `null` → Claude. **This must not change**: null is the pre-Grok contract, and
  `TranscriptFormatFor` deliberately sends null for Claude so a new server never breaks an old
  runner (`SessionRunnerHttpClient.cs:70-79`).
- `"claude"` → Claude.
- `"grok"` / `"codex"` → their tailers, unchanged.
- anything else → **reject the launch with 400**, naming the format and the formats this binary
  supports.

This is the permanent fix for the direction "server learns a new format the runner does not have",
but be honest about what it cannot do: **it does not help against a runner that predates it.** The
2026-08-20 runner has no such rejection and never will. Old runners are covered only by §3.3.

### 3.3 Layer 3 — the server refuses to launch a kind the runner cannot tail

In `SessionRunnerHttpClient.StartAsync`, before the POST: if `TranscriptFormatFor(spec.Kind)` is
non-null, and the cached capabilities answer enumerates `TranscriptFormats`, and that list does not
contain it — throw `RunnerCapabilityMismatchException` carrying a message that is the whole
diagnosis and the one-line fix:

> The session runner at :17204 cannot tail a `codex` transcript — it reports support for
> `claude, grok` and was built from `1a9dec7` on 2026-08-20 23:53 (running since 23:55). Launching
> anyway would bind no transcript, and the delivery watchdog would read that as "never started" and
> kill a working session 10 minutes later (CARD-0112). Rebuild and restart it:
> `pwsh -File scripts/restart-session-runner.ps1`.

The evidence discipline is CARD-0056's, verbatim in spirit:

- runner unreachable, `GetCapabilitiesAsync` returns null, or `TranscriptFormats` is absent ⇒ **no
  evidence; launch exactly as today.** Absence of the field means "a runner too old to say", which is
  not the same as "a runner that cannot" — the Codex arm shipped before this card's field would.
- only a runner that answered, enumerated, and omitted the format is positive evidence.
- cache with the `PtyDeliveryProfile` shape (snapshot + TTL + background refresh) so the cost is one
  round trip per TTL, not per launch.

**Why refuse rather than launch-and-warn.** Every such launch ends the same way: ~10 minutes of a
real model doing real work, then a false `Failed` written against the delegate, a kill of a healthy
session, and a caller told the delegate never started. Refusing at t=0 costs one dispatch and prints
the fix. The refusal is only ever reached on positive evidence, so nothing that works today changes.

The refusal is recorded as `AgentIncidentKind.RunnerBuildStale = 29` (next free value; 28 is
`PtyHostCensusDiverged`) via `AgentSupervisorService.RecordIncidentAsync`, **Critical**: it disables
an entire `AgentKind` stack-wide until a human restarts a daemon.

*Implementation check for the build pass:* confirm the thrown reason reaches the task's
`FailureReason` through `AgentSessionService.StartAsync`'s catch and the dispatcher's launch path,
rather than being flattened into a generic launch error. If it is flattened, the incident is the
surface that matters and the exception message must still be logged verbatim.

### 3.4 Layer 4 — the watchdog stops killing what it cannot see

In `FailNeverStartedAsync`'s `!started` branch (`AgentTaskDispatcher.cs:421-450`), **after**
`TryRecoverBindRefusalAsync` gets its chance (it can settle the task *successfully* from git
evidence, which beats any failure text), ask the same capability question about this task's
`AgentKind`. On a positive mismatch:

- distinct reason text, naming the runner's build stamp, its declared formats, the kind that is
  missing, and `scripts/restart-session-runner.ps1` — not the generic "Boot prompt was never
  delivered".
- **do not kill the session.** This is the CARD-0055 working-kill guard applied to a new blindness:
  the transcript is not merely late, it was never going to exist, and the screen recording from
  2026-08-20 shows the session working the entire time. The kill is the part that destroyed real
  work; the fail is the part that stops the task stranding.
- raise the same `RunnerBuildStale` incident, deduped per session.

Still `FailAsync`, not `BlockAsync`: an unbound session's report can never be correlated, so the task
genuinely cannot settle and leaving it Dispatched strands it. The caller notification path below
`FailAsync` is unchanged and now carries an actionable reason.

### 3.5 Layer 5 — a loud warning wherever the stack adopts a daemon

New `scripts/check-daemon-build.ps1` (ASCII-only — it may run under Windows PowerShell 5.1), driven
by a table of `{name, port, project-closure paths}`:

1. `GET http://localhost:<port>/capabilities`, read `build.commitSha`. No answer, or no sha ⇒ print
   one dim line and exit 0. Never fail the stack on a missing probe.
2. `git merge-base --is-ancestor <sha> HEAD` — if the sha is not an ancestor (a rebase, a different
   branch, a rewritten history) say so and stop. Comparing across unrelated histories is noise.
3. `git log --oneline <sha>..HEAD -- <paths>` — the daemon's **project-reference closure**, not the
   whole repo. For the session-runner that is exactly six directories (verified from the csprojs):
   `src/Antiphon.SessionRunner`, `src/Antiphon.SessionRunner.Contracts`, `src/Antiphon.Agents.Pty`,
   `src/Antiphon.PtyHost`, `src/Antiphon.PtyHost.Client`, `src/Antiphon.PtyHost.Protocol`.
4. Zero commits ⇒ one dim confirmation line. One or more ⇒ a loud yellow block: the count, up to five
   commit subjects, the daemon's build time and uptime, and the exact fix command.
5. Exit 0 always (advisory). `-FailOnStale` for a caller that wants a gate.

The path-scoped step 3 is the whole point. **Measured today: sha-vs-HEAD says "2 commits behind" and
the scoped check says zero** — the naive version would have trained the operator to ignore it inside
a week.

Called from **both** `restart-apphost.ps1` (at the "preserving session-runner (PID …)" line,
`scripts/restart-apphost.ps1:103-105`) and `dev-aspire.ps1` (after the Postgres section), because the
logon autostart path is `autostart-apphost.ps1 -> dev-aspire.ps1` and never touches
`restart-apphost.ps1`. Both are advisory; neither restarts anything.

**Why warn and not auto-restart** (the card asks for reasoning). Sessions survive a runner restart —
that is what made the 2026-08-20 fix safe to apply live — but "survivable" is not "free". A restart
drops every pty-host pipe, re-runs `AdoptOrphanedHostsAsync` with the port deliberately down (the
reconciler treats that as skip-this-cycle, by design), re-binds every transcript through C1–C4, and
rebuilds first — and a build failure leaves the daemon down in `run-daemon.ps1`'s 5-second retry
loop. Wiring that into every AppHost start, including the one that fires a minute after logon and
every watchdog-driven restart, makes the most disruptive action in the stack the most frequent one,
on a signal that is noisy by construction (§3.5 step 3 exists precisely because of that noise).
Layer 3 already converts the *dangerous* case from silent to loud and blocks it at the door, so the
warning only has to carry "you probably want to restart", never "you must".

### 3.6 The other adopted daemons (card item 4)

- **fake-gateway, 17208** — same adoption branch (`DaemonProcessService.InitialiseAsync:33-41`), same
  class. Measured: built 2026-08-17 15:50 from `e0d3340`, **0** commits touching
  `src/Antiphon.Messaging.FakeGateway` since; and its failure mode is loud (a recorder that either
  records or visibly does not), with no cross-process contract that can silently degrade. It is also
  off the delivery path entirely right now — `Antiphon.AppHost/Program.cs:68` points the bridge at
  the live `server2:19092` broker. **Covered by the §3.5 script's table, nothing else.**
- **Adopted pty-hosts** — the third long-lived adopted thing and the only one whose version is
  already on the wire and unread. A restarted runner re-adopts hosts running the *previous* binary
  for as long as those sessions live. `HelloAckMessage.HostVersion` is SourceLink-stamped and never
  compared to the runner's own. One log line at adopt time when they differ (§4, S5b).
- **Vite client, Storybook** — AppHost-managed npm apps in `dev` mode, serving from source. No
  compiled artifact to go stale. Not in this class.
- **Server** — restarted by every AppHost restart, never adopted. Not in this class.

## 4. Slices

Each is independently mergeable and independently testable, in this order.

**S1 — build identity and declared formats on `/capabilities`.**
`RunnerCapabilitiesDto` + `RunnerBuildDto` (contracts), resolved in `Program.cs`; the declared list
sourced from the runner's own dispatch site. No consumer yet, so nothing can regress.
Tests (`tests/Antiphon.Tests` or a runner-side contract test): the declared list matches every arm of
the dispatch switch; the DTO round-trips through `System.Text.Json` with the new members absent (old
runner) and present; the sha parses out of the SDK's `1.0.0+<sha>` shape and is null when absent.

**S2 — the runner rejects an unknown non-null transcript format.**
400 naming the requested format and the supported set; `null` and `"claude"` unchanged.
Tests: null still tails Claude (the pre-Grok contract, pinned explicitly); `"codex"`/`"grok"` reach
their tailers; `"zzz"` is a 400 and starts no session.

**S3 — the server-side pre-launch gate.**
`RunnerCapabilityMismatchException`, the cached capability probe in `SessionRunnerHttpClient`
(`PtyDeliveryProfile` shape), `AgentIncidentKind.RunnerBuildStale = 29`.
Tests: positive mismatch refuses and raises the incident; absent `TranscriptFormats` launches exactly
as today; unreachable runner launches exactly as today; a matching list launches; the message names
the fix command.

**S4 — the watchdog's distinct verdict, without the kill.**
`FailNeverStartedAsync` after `TryRecoverBindRefusalAsync`.
Tests (extend `tests/Antiphon.Tests/Application/` alongside the existing `FailNeverStartedAsync`
coverage): a mismatch produces the distinct reason and **`KillAsync` is not called**; bind-refusal
recovery still wins when it can; no mismatch is byte-for-byte today's behaviour including the kill;
the caller still gets its completion note.

**S5 — the operator-facing warning.**
`scripts/check-daemon-build.ps1` + calls from `restart-apphost.ps1` and `dev-aspire.ps1`; table
covers session-runner and fake-gateway.
Guard test (cheap, and squarely in the spirit of this card): a unit test that walks
`Antiphon.SessionRunner.csproj`'s transitive `ProjectReference` closure and asserts the script's path
list covers it — so a new project in the runner's graph cannot silently fall out of the check.

**S5b — pty-host version drift.** When the runner adopts a host whose `HelloAckMessage.HostVersion`
differs from its own, log it (Information; it is expected and harmless after any restart, and only
interesting as context when a session misbehaves). Split out because it is unrelated to the launch
path and must not hold S5 up.

## 5. Deliberately not in scope

- **A general daemon health/version monitoring framework.** The card asks for a stamp and two places
  that read it. Dashboards, metrics, a `/version` on every service, a staleness projection in the UI
  — all out. The measured churn (§Verdict row 3) justifies the checks above and nothing more.
- **Auto-restarting anything on staleness.** §3.5 states the reasoning; a future card can revisit it
  with data on how often the warning is ignored.
- **Dirty-tree / uncommitted-edit detection.** SourceLink does not mark dirty, and building it would
  mean adding MSBuild machinery to a design whose whole appeal is that the stamp is already free. The
  semantic gate (§3.3) does not depend on the sha at all, so this limit costs nothing that matters.
- **Making the AppHost (or the server) shell out to `git`.** Git stays in PowerShell.
- **Negotiating anything other than transcript formats.** The pty backend already has its own check
  (CARD-0056). A general "runner feature set" handshake is the over-engineering this card explicitly
  warns against; add a second capability the day a second one is silently downgraded.
- **`AttentionService` / UI surfacing.** The incident row and the launch refusal are the surfaces.
- **Changing `restart-session-runner.ps1`.** It already does the right thing; this card makes it
  discoverable, not different.
- **Retro-settling task `2e152d49`.** The 2026-08-20 casualty stays as it is — the historical record
  of the miss, and CARD-0108's round trip was re-proved after the restart.

## 6. Risks and what to measure during the build

- **False refusal is the one dangerous outcome.** A bug in the gate blocks every launch of a kind.
  Mitigation is structural: positive evidence only, a null-safe default, and the S3 test list above
  spends four of its five cases on the negative arms.
- **A capability answer cached across a runner restart** could refuse launches against a runner that
  is now fine. The 5-minute `PtyDeliveryProfile` TTL bounds it; consider invalidating on a
  `SessionRunnerEventPump` reconnect, which is the cheapest existing "the runner just came back"
  signal. Decide at build time; do not add a new heartbeat for it.
- **The declared-formats list drifting from the implemented switch** is the same disease as this
  card. S1's test is the vaccine and must not be skipped.
- Worth capturing while implementing S5: how many days in a row the warning fires. If it fires
  constantly with an empty scoped list, step 3's path set is wrong.

## 7. Card housekeeping

- `pwsh -File scripts/card.ps1 move CARD-0112 -To <planning/doing column> -Reason "plan written: docs/superpowers/plans/2026-08-21-card-0112-stale-daemon-detection-plan.md"` when the build pass starts. A move into an ACTIVE column does not spawn an agent unless `-Spawn` is passed.
- Append to the card's description on `edit`: the plan path, the §Verdict finding that the SHA stamp
  already exists (so nobody re-litigates the MSBuild question), and the measured note that
  sha-vs-HEAD alone is a false-alarm generator.
- The card's item 4 is **answered here and needs no separate card**: fake-gateway shares the class
  and is covered by the S5 table; pty-host drift becomes S5b; nothing else in the stack is adopted.
- CLAUDE.md gets one bullet after S3/S4 land — the "Gotchas" list is where an agent will look, and
  the sentence it needs is: *a session-runner that predates a transcript-format change now refuses
  the launch instead of silently tailing the wrong file; the fix is
  `pwsh -File scripts/restart-session-runner.ps1`.*
