# CARD-0115 — Explicit project scope for pool delegates' API-key resolution

**Date:** 2026-08-21 · **Card:** CARD-0115 · **Status:** plan (no implementation in this pass)

**Verdict up top:** give the identity to the **task**, not the agent and not the path — a nullable
`AgentTask.ProjectId` FK, filled ONCE at creation from the caller's provenance (parent task's
`ProjectId`, or the calling session's card/board), inherited unchanged down the delegation tree, and
consumed at dispatch as `claimed.ProjectId ?? (pinned agent's board → project)`. No trustworthy
identity ⇒ the column is null ⇒ global-only resolution, byte-for-byte today's behavior — every
pre-existing row is null and needs no backfill. Filesystem-path derivation stays **rejected
permanently**, not deferred: worktrees are sibling directories, so a prefix match against
`Project.LocalRepositoryPath` can hand a project credential to the wrong task, and CARD-0106 already
refused it for that reason (`ApiKeyEnvResolver` class doc, `server/Application/Services/ApiKeyEnvResolver.cs:23-25`).
One extra fence the card didn't name but the code demands: the **warm pool** must not reuse a live
delegate across scopes, because a reused session's environment was resolved at its *first* launch and
env can't change on a live process — that reuse arm is a second cross-project leak vector and gets its
own slice (S3). Three slices; S1+S2 are the feature, S3 closes the reuse hole.

All file:line references verified by reading the files on 2026-08-21 against master `1ea8404`.

---

## 1. The ground truth this plan stands on

**How resolution works today (CARD-0106, shipped).** `ApiKeyEnvResolver.ResolveAsync`
(`server/Application/Services/ApiKeyEnvResolver.cs:100`) substitutes `{{key:NAME}}` over the fully-merged
env against an explicit `Guid? projectId` scope — project row first, then global (`:148-153`), with
`ApiKey.ProjectId == null` meaning global (`server/Domain/Entities/ApiKey.cs:27`, filtered unique
indexes `IX_ApiKeys_Name_Global` / `IX_ApiKeys_ProjectId_Name`, `AppDbContext.cs:682-689`). Two
`ResolveSpecAsync` overloads exist: **agent-derived** (`:69`, walks `Agent.BoardId → Board.ProjectId`
via `ResolveProjectIdAsync` `:51`) and **explicit-scope** (`:81`). The explicit-scope overload is
already used by the card-spawn path — `CardService.cs:589-593` passes `card.Board.ProjectId`, and
`AgentLaunchOptions.ApiKeyProjectId` (`CardService.cs:610`) carries it through the managed resolver
with the recorded rule *"the CARD's board names the project for a card spawn, whether or not the agent
that runs it happens to sit on the same board."* So the plumbing for "an identity that is not the
agent's board" exists; what's missing is any identity on the pool-delegate path.

**The pool-delegate path.** `AgentTaskDispatcher` dispatch (`AgentTaskDispatcher.cs:1281-1349`):
`ResolveAgentAsync` (`:1621-1664`) either takes the pinned row or creates a fresh ephemeral
`IsPoolDelegate` agent with **no `BoardId`**, then the spec is finalized and resolved via the
**agent-derived** overload (`:1341-1348`) — which is why a pool delegate is global-only today. The
comment at `:1337-1340` records that as deliberate CARD-0106 scoping.

**What identifies a task's provenance at creation.** `AgentTaskService.CreateAsync`
(`AgentTaskService.cs:90-279`) builds the row from a `Caller` (`:52-56`) resolved by
`AuthenticateAsync` (`:62-84`) in exactly three shapes:

1. **Task token** — the caller IS a task (an orchestrator delegating downward). The parent's own
   identity is the child's provenance.
2. **Session token** — a standing agent session delegating on its own behalf
   (`AgentSession.DelegationTokenHash`, `:80-83`). That session reaches a project two operator-set
   ways: `AgentSession.CardId → Card.BoardId → Board.ProjectId` (card sessions), or the owning agent
   by `Agent.PersistentSessionId == sessionId.ToString("D")` (an established lookup — ~10 call sites,
   e.g. `AgentSessionService.cs:647`) `→ Agent.BoardId → Board.ProjectId`.
3. **No token** — UI/curl: `Caller(null, null, "")` (`AgentTaskEndpoints.cs:112-120`). No project
   evidence exists. (The card's framing "a pool delegate is always spawned FROM a card/board" is
   **not** what the code says — tasks are spawned by orchestrator sessions or the UI, never directly
   by a card. The chain still bottoms out at an operator-set board binding when one exists.)

Other creation sites: the merge-conflict child (`AgentTaskService.cs:651-702`) copies its parent's
coordinates; the check-interpreter task (`AgentTaskCheckService.cs:521-563`) is pinned to a standing
specialist whose own `BoardId` already drives resolution. Retry/escalation re-queue the **same row**
(`AgentTaskService.cs:529-533`), so a creation-time identity survives every re-dispatch.

**The reuse paths never re-resolve keys.** `TryReuseWarmAgentAsync` (`AgentTaskDispatcher.cs:1756-1871`)
delivers a brief into an already-running session — the env was fixed at that session's launch. The
unpinned pool query matches kind + tier + `SameDirectory` (`:1795-1814`); the pinned arm takes over a
warm pool delegate outright, with one existing precedent for "wrong furniture ⇒ relaunch the same
row": the kind-mismatch arm (`:1779-1785`, `SpawnFresh` restamps `Agent.Kind` in `ResolveAgentAsync`).
`SameDirectory` does NOT imply same project — cross-repo orchestration (`AgentTask.WorkingDirectory`
is a property of the task, `AgentTask.cs:71-75`) lets a project-A run and a project-B run both
delegate into the same directory, and two boards on different projects can target one repo.

---

## 2. Recommended design — identity on the task, set at creation, inherited by provenance

### 2.1 Schema

```csharp
// AgentTask.cs — new column
/// <summary>
/// The project on whose behalf this task runs — the scope its {{key:NAME}} placeholders resolve
/// against (project key wins over global; CARD-0115). Set ONCE at creation from the caller's
/// provenance (parent task, or the calling session's card/board binding), NEVER derived from a
/// filesystem path (worktrees are sibling directories; a prefix match can hand a project's
/// credential to the wrong task). Null = no trustworthy identity = global keys only.
/// </summary>
public Guid? ProjectId { get; set; }
```

EF config in the `AgentTask` block (`AppDbContext.cs:1314`): `HasOne<Project>().WithMany()
.HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.SetNull)` + `IsRequired(false)`.
**SetNull, not Cascade** — deleting a project must degrade its tasks to global-only, not destroy
delegation history (contrast `ApiKey.ProjectId`, which cascades on purpose, `AppDbContext.cs:694`).
Migration via `dotnet ef migrations add` into `server/Migrations/` (latest: `20260820175717_AddApiKeys`).
No index needed — the column is only ever read off an already-loaded row. No backfill: null on every
existing row IS the correct value (they were all created with no identity).

### 2.2 Filling it at creation

In `CreateAsync`, before the `new AgentTask` block:

```
ProjectId = parent?.ProjectId ?? await DeriveCallerProjectAsync(caller, ct)
```

`DeriveCallerProjectAsync` (new private helper, ~15 lines): null unless `caller.SessionId` is set and
the caller has no task; then load the session row, and take **card first, owning agent second** —
`session.CardId → card.BoardId → Board.ProjectId`, else agent by `PersistentSessionId` →
`ResolveProjectIdAsync(agent.BoardId)` shape. Card-before-agent mirrors the card-spawn rule already
recorded at `CardService.cs:608-610`. No-token caller ⇒ null ⇒ global-only.

Inheritance down the tree is **unconditional**: a child's `ProjectId` is its parent's, even when the
child works in a different repo. The identity means *"on whose behalf"* (who commissioned the run),
not *"where the bytes live"* — a project-A orchestrator sending a worker into another checkout is
project A's work, spending project A's credentials, which is exactly what a project-scoped key is
for. Gating inheritance on "same repo" was considered and dropped: the only way to test it is
comparing paths (`RepoPath` of a worktree child is the *worktree's* toplevel, not the main repo's, so
even exact toplevel equality breaks on the most common nested shape — an orchestrator in its own
worktree delegating shared-workspace children), and path comparison is the exact mechanism this card
exists to ban.

Other creation sites: merge-conflict child copies `conflicted.ProjectId` (one line at
`AgentTaskService.cs:651-688`); check-interpreter task stays null (its pinned specialist's `BoardId`
already names the right scope via the fallback below). The `Created` event detail gains a
` — project scope: {id}` suffix **only when set**, same only-when-interesting convention as the
AgentKind suffix at `:256-260`.

### 2.3 Consuming it at dispatch — the precedence rule

Replace the agent-derived call at `AgentTaskDispatcher.cs:1341-1348` with the explicit-scope overload:

```
var scope = claimed.ProjectId
    ?? await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, ct);
spec = await _apiKeyEnvResolver.ResolveSpecAsync(spec, scope, subject, ct);
```

- **Task's project wins over the pinned agent's board** when both exist — the same rule the card
  spawn already established (the work's home outranks whoever runs it).
- **Task null + standing agent with a board ⇒ the agent's project**, which is today's behavior for
  "task pinned to a standing agent" and keeps `ApiKeyLaunchPathTests.a_pinned_agents_board_selects_its_projects_key_over_the_global_one`
  green untouched.
- **Both null ⇒ global-only** — the safe default the card demands, and the value every fresh pool
  delegate had before this card.

Within `ResolveAsync` nothing changes: project-key-beats-global for the same NAME is already built
and tested (`ApiKeyEnvResolver.cs:148-153`); the candidates query (`:136-141`) already restricts to
`ProjectId == null || ProjectId == scope`, so a *different* project's key is structurally unable to
resolve — the leak-proofing burden of this card is entirely about which `scope` value arrives there.

### 2.4 Fencing the warm pool (the vector the card didn't name)

A reused warm delegate's env holds the keys of the scope it was **launched** under. Fence, mirroring
the existing kind-mismatch precedent:

- New nullable column `Agent.PoolProjectId` next to the other pool fields (`Agent.cs:108-118`),
  documented as *"the project scope this pool delegate's live environment was resolved under; null =
  global; meaningless off the pool."* Stamped in the dispatch path right where `scope` is computed
  (fresh spawns and relaunches both pass through it).
- **Unpinned pool shopping** (`:1795-1814`): add `&& a.PoolProjectId == claimed.ProjectId` to the
  query (EF's null-semantics compensation makes null-equals-null work as intended for a nullable
  parameter). Strict equality — we deliberately do NOT try to track "did the env actually consume a
  project key" to allow more reuse; erring toward a cold start is cheap, erring toward a stale
  credential is the bug this card exists to prevent.
- **Pinned pool-delegate takeover** (`:1760-1791`): scope mismatch ⇒ `SpawnFresh`, exactly like the
  kind mismatch at `:1779-1785` — the same agent row relaunches through the spawn path, which
  re-resolves the env under the new task's scope and restamps `PoolProjectId`.
- **`PlaceOnStandingAgentAsync` (`:1894`) is deliberately untouched**: placing a task on a standing
  agent's live session runs it in that agent's own env by the operator's explicit pin choice; no key
  resolution happens there and none should.

---

## 3. Rejected alternatives

**(b) Derive at dispatch time by walking the parent chain** (no schema change: at dispatch, follow
`ParentSessionId`/`ParentTaskId` up to a session, then card/board). Rejected: `ParentSessionId` is
null for UI-created roots and `ReplyTo = None` shapes; ephemeral agents and their sessions are
*deleted* when tasks settle (`AgentTask.cs:109-110`), so the chain rots between creation and a
retried/escalated re-dispatch hours later; and it answers the same question on every dispatch instead
of once, un-auditably — the creation-time column is written where the evidence is freshest and shows
up in the row and the Created event for an operator to inspect. The warm-pool fence would still need
a stamped column anyway, so this saves nothing.

**(c) Stamp `Agent.BoardId` on the ephemeral pool row at spawn.** Rejected: `BoardId` means board
*membership* — board session caps (`Board.MaxConcurrentSessions`), board UI grouping, always-on
supervision semantics — and pool furniture must not acquire those side meanings to smuggle a
credential scope. It also puts the identity on the reusable object instead of the work: one agent row
serves many tasks across roots, so the row would need restamping on every takeover *and* the reuse
fence anyway, at which point it is design (a) with extra aliasing. The narrow `Agent.PoolProjectId`
in §2.4 records precisely one fact (what the live env was resolved under) without board semantics.

**(d) Any path-derived fallback** (prefix-match `WorkingDirectory`/`RepoPath` against
`Project.LocalRepositoryPath` when no explicit identity exists). Rejected permanently per the card
and CARD-0106: worktrees are sibling directories, two projects can share a repo, and a silent wrong
match is a cross-project secret grant. "No trustworthy identity ⇒ global-only" is the floor, not a
gap to fill later.

---

## 4. Slices

**S1 — the column and its provenance (schema + creation).**
`AgentTask.ProjectId` + EF config (SetNull) + migration; `DeriveCallerProjectAsync` in
`AgentTaskService`; fill at `CreateAsync` (parent-first), merge-conflict child copies parent's;
Created-event suffix. Tests (in `tests/Antiphon.Tests`, TUnit, run via
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0115/`):
- root created via session token on a **card session** carries the card's project;
- root created via session token on a **board-bound agent's session** carries the board's project;
- root with **no token** (UI shape) carries null;
- child inherits parent's `ProjectId`, including across a differing `WorkingDirectory`;
- merge-conflict child inherits;
- retry keeps the value (same-row requeue).
Scope every assertion to rows the test created (shared-Postgres rule).

**S2 — dispatch consumes it (precedence + leakage).**
Swap the dispatcher call site to the explicit-scope overload with the `claimed.ProjectId ?? agent-board`
fallback. Tests extend `tests/Antiphon.Tests/ApiKeys/ApiKeyLaunchPathTests.cs` style:
- task with `ProjectId = A`, key NAME exists in A **and** globally ⇒ A's value wins;
- **the leak test the card demands**: projects A and B both hold a key of the same NAME; a pool
  delegate for a task scoped to A must resolve A's (never B's), and a task scoped to **null** must
  resolve the **global** value even though A's and B's rows exist — i.e. a project key never
  resolves for anyone but its own project's tasks;
- task `ProjectId` **overrides** a pinned standing agent's differing board project;
- task null + pinned agent's board ⇒ agent's project (existing behavior pinned; the three existing
  launch-path tests stay green untouched);
- unknown NAME under scope A still fails naming A-then-global as searched scopes.

**S3 — warm-pool scope fence.**
`Agent.PoolProjectId` + migration; stamp at dispatch; pool-query predicate; pinned-takeover mismatch
⇒ `SpawnFresh`. Tests at the dispatcher level (existing dispatcher test fixtures):
- warm delegate launched under A is **not** offered to a same-directory task scoped B, nor to a
  null-scoped task; equal scopes (including null==null) still reuse;
- pinned warm pool delegate with mismatched scope relaunches (fresh session, env re-resolved, row
  restamped) instead of receiving the brief;
- standing-agent placement unchanged.

Slices land independently; S1 alone changes no observable behavior (nothing reads the column yet),
S2 is the feature, S3 closes reuse. Build with `--property:OutputPath=bin-c0115/` (forward slash)
while the daemons hold `bin/`, and delete the `bin-c0115` directories afterwards.

## 5. Deliberately not in scope

- **An explicit `ProjectId` field on `CreateAgentTaskRequest`.** A task-token caller naming an
  arbitrary project would be a privilege escalation (any orchestrator could claim any project's
  keys); accepting it only from the unauthenticated UI shape needs an authorization story this
  single-operator tool doesn't have (CARD-0106 plan §1 settled that). Server-side derivation only;
  file a follow-up card if the UI ever needs a project picker on manual task creation.
- **Standing-agent placement env** (§2.4 last bullet) — operator's pin, no resolution occurs.
- **Path-derived fallback** — rejected permanently, not deferred (§3d).
- **Tracking whether a reused env actually consumed a project key** to loosen the S3 fence — strict
  scope equality is the safe simple rule; revisit only if pool hit rates measurably suffer.
- **Backfill of existing task rows** — null is correct for all of them.
- **AgentTuiSecret convergence** — CARD-0106 §S4's own follow-up, unrelated.

## 6. Card housekeeping

- CARD-0115 stays where it is until implementation; this plan is the design decision the card asked
  for ("choose an explicit project identity") — identity = `AgentTask.ProjectId` by provenance.
- When implementing, revise the card description to name the three slices and the warm-pool vector
  (S3), which the card text does not currently mention.
- The dispatcher comment at `AgentTaskDispatcher.cs:1337-1340` and the `ApiKeyEnvResolver` class doc
  (`:23-25`) both state "a pool delegate has no board, so global only" — S2 must rewrite both, or
  they become the wrong-thing-naming furniture this repo keeps having to exorcise.
- CARD-0106's plan §4 rule ("Agent.BoardId → Board.ProjectId is the only mapping used") is
  superseded for tasks by this design; note that in the CARD-0106 plan doc when S2 lands.
