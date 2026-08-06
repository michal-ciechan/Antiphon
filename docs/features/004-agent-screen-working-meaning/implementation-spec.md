# Feature 004 — Implementation spec: agents-screen "Working" means mid-turn

**Status:** ready for implementation
**Date:** 2026-08-06
**Card:** CARD-0001
**Based on:** [initial-investigation.md](initial-investigation.md)
**Companion:** [test-spec.md](test-spec.md)

---

## Where we actually are

The investigation's recommended **Option A landed in commit `1ce1084`**
(`feat(agents): overhaul agent cards — Working means mid-turn, kebab menu, terminal-colour
liveness`, 2026-08-03), which is this branch's HEAD. Any implementing agent MUST read that commit
first (`git show 1ce1084`) — the core feature is **done**, and this spec covers (a) the as-built
design you must not regress, and (b) the three remaining work items.

### As-built summary (do not re-implement)

| Piece | Where |
|---|---|
| `Working` bool on both agent DTOs, doc'd as transcript-derived mid-turn | `server/Application/Dtos/AgentDtos.cs:41-43,69-70` |
| Computed per agent in list + detail, gated on a **Running** live session | `AgentService.cs:46-76` (`IsSessionWorkingAsync`, `AgentService.cs:63`) |
| The one shared rule — `SessionMessageQueueService.IsWorkingAsync`, made `internal static` so AgentService reuses it (no second implementation) | `SessionMessageQueueService.cs:547-579` |
| Client `working` field + `AgentStatus` union | `client/src/api/agents.ts:53,89-92` |
| `AgentActivityBadge`: Working spinner (transcript-working) / Review / Failed / Disconnected; **quiet states render nothing** — liveness is the terminal icon's colour (green/yellow/gray) | `AgentsPage.tsx:391-419,139-165` |
| Detail header uses the same badge; Stop shows when `liveSession \|\| status === 'Working'` | `AgentsPage.tsx:225,242` |
| List + detail poll at 5s so the spinner tracks turn *starts* (SignalR only covers turn end via `SessionFinished`) | `agents.ts:241-242,256-257` |
| Client test: spinner only for the transcript-working agent | `AgentsPage.test.tsx:150-174` |

Design decisions already made (deliberate, keep them):

- `Agent.Status` stays a lifecycle latch. `SessionReconciliationService.cs:179` depends on
  `Status == Working` meaning "should have a live session" — Option C remains rejected.
- Quiet lifecycle states (`Idle`/`Ready`/`Stopped`) show **no badge**, a departure from the
  investigation's "second badge" sketch. The terminal icon's colour carries liveness.
- The working check reuses the hardened queue-tier rule verbatim (interrupt markers, local
  slash-commands, compact boundary — each exclusion pins a real stranding incident; see CLAUDE.md).

---

## Remaining work

### R1 — Rename `AgentStatus.Working` → `AgentStatus.Running` (resolve the open question: **yes, do it**)

The investigation left this open. Do it: with the activity badge now labelled "Working", a
lifecycle enum member also called `Working` is a standing trap — `AgentsPage.tsx:242` already reads
as "is busy" when it means "was started". Rename makes both self-explanatory. Do it as **its own
commit** (mechanical, wide diff) after R2's tests are green, so the rename diff contains zero
behaviour change.

**Persistence — no migration needed.** `Agent.Status` is int-backed
(`AppDbContext.cs:562` is just `IsRequired()`, no string conversion). Keep the numeric value:

```csharp
public enum AgentStatus { Idle = 0, Ready = 1, Running = 2, WaitingForHumanReview = 3, Stopped = 4, Disconnected = 5, Failed = 6 }
```

Verify `dotnet ef migrations has-pending-model-changes` style check / build produces **no** new
migration (enum member names are not part of the relational model).

**Wire value changes.** `Program.cs:116` registers `JsonStringEnumConverter`, so the API string
flips `"Working"` → `"Running"`. Every string comparison must move in the same commit:

Server (rename symbol; compiler finds these):
- `AgentControlService.cs:97` (set on Start)
- `AgentSessionService.cs:294`, `AgentSessionRuntime.cs:168`, `SessionReconciliationService.cs:179`
- Tests: `SessionReconciliationServiceTests.cs:115,131,139,166,233`, `ReviewLoopTests.cs:508`,
  `ChannelBridgeTests.cs:600`, `AgentSupervisionTests.cs:57`, `AgentStartRecoveryTests.cs:55`,
  `AgentControlServiceIntegrationTests.cs:56`

Client (string literals; the compiler does NOT find these — grep `'Working'`):
- `client/src/api/agents.ts:53` — union member `'Working'` → `'Running'`
- `AgentsPage.tsx:242` — `status === 'Working'` → `'Running'` (Stop-button gating)
- `AgentsPage.test.tsx:157,165,493,536,556` — fixture `status: 'Working'`

Also update stale comments referring to a "phantom Working agent"
(`AgentSessionService.cs:290`, `AgentControlService.cs:176`) and the DTO doc comments that contrast
`Status=Working` (`AgentDtos.cs:41-43`, `agents.ts:89-91`, `AgentService.cs:61`,
`SessionMessageQueueService.cs:546`) so the docs name `Running`.

**Acceptance:** repo-wide grep for `AgentStatus.Working` and `status === 'Working'` returns zero
hits outside `docs/` history; all suites green; agents page still gates Start/Stop correctly
against a live dev server.

### R2 — Server-side projection tests (the real gap)

Commit `1ce1084` shipped **zero server tests**. The queue-tier rule is well pinned
(`SessionMessageQueueServiceTests`), but the *agent projection* — the Running-status gate in
`IsSessionWorkingAsync`, the threading into both DTOs — is untested; a regression would silently
return the page to always-Working (or always-idle). Full test list and harness guidance in
[test-spec.md](test-spec.md) — implement exactly that.

### R3 (optional) — Batch the per-agent working queries

`GetAllAsync` awaits `IsWorkingAsync` sequentially per agent (`AgentService.cs:50-56`), and each
call issues two `MaxAsync` queries — so a list render costs `2N` round-trips, polled at 5s per open
viewer. At today's ~6 agents this is fine; the investigation asked for batching, so if you touch it,
do it properly:

- Add `internal static Task<IReadOnlySet<Guid>> GetWorkingSessionIdsAsync(AppDbContext db, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)`
  next to `IsWorkingAsync` in `SessionMessageQueueService` — one grouped query:
  `TranscriptEntries.Where(t => sessionIds.Contains(t.AgentSessionId)).GroupBy(t => t.AgentSessionId)`
  projecting per-group `Max(lastEnd)` / `Max(lastActivity)` with the **same predicates** as
  `IsWorkingAsync` (extract the two predicate expressions into shared
  `Expression<Func<TranscriptEntry,bool>>` constants so the single and batched paths cannot drift —
  drift here recreates exactly the class of stranding bug the exclusions fixed).
- `GetAllAsync` calls it once with the Running live-session ids; `GetByIdAsync` keeps the single
  `IsWorkingAsync`.
- Only worthwhile with the shared-predicate refactor; a copy-pasted second predicate is worse than
  the `2N` queries. Skip R3 entirely if that feels heavy — it is a perf nit, not a correctness gap.

---

## Non-goals

- No SignalR turn-*start* push (polling at 5s is the accepted mechanism; a `SessionTurnStarted`
  event is future work).
- No writes to `Agent.Status` on turn boundaries (Option C — rejected, breaks reconciliation).
- No visual redesign of the cards; badge semantics are settled.

## Suggested sequencing

1. R2 tests (red where they expose gaps → green) — commit.
2. R1 rename — separate mechanical commit, all suites green.
3. R3 only if doing the shared-predicate refactor — separate commit.
