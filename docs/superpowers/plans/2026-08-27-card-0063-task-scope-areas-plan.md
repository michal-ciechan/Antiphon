# CARD-0063 — Task scopes as named areas: what `-Scope` actually does today, and the small mechanism that replaces it

**Date:** 2026-08-27 · **Card:** CARD-0063 (`59c837ca-eaec-4a74-872c-2de79864996b`) · **Status:** investigation
complete, design only — nothing implemented. **Verified against:** `master` @ `7860878` (this worktree). Every
count below was read out of the live dev database (`antiphon-postgres`, 623 `AgentTasks` rows from 2026-08-09 to
2026-08-27) on 2026-08-27; every code claim carries a `file:line`.

**Related, not blocking:** CARD-0062 (refine a running delegate) is Done and merged — cited by the card as the
sibling "caller keeps control of work in flight" concern, nothing here depends on it.

---

## Verdict up front

| Question the card/brief posed | Answer, measured |
|---|---|
| Does `-Scope` **block**, **warn**, or **queue**? | **It queues, silently.** The only consumer is the 5-second dispatcher tick (`AgentTaskDispatcher.cs:228-262`): a Queued task whose glob "intersects" any Dispatched/Working task's glob is skipped this tick with `continue` and re-evaluated next tick. No status change, no task event, no create-time error, and the hosted service's only mention is a **Debug** log that fires only on a tick that also dispatched or failed something (`AgentTaskDispatcherHostedService.cs:45-51`). A task held for ten minutes leaves no trace anywhere but its own `DispatchedAt − CreatedAt`. |
| Does it apply to Shared tasks only, as the comments say? | **No.** The comment at `:228` and the entity doc at `AgentTask.cs:111` both say "two Shared tasks"; the query at `:231-234` filters on status and `ScopeGlob != null` only. Worktree and **ReadOnly** tasks hold and are held alike (79 Worktree / 45 Shared / 4 ReadOnly scoped rows). |
| Is anything enforced about what a delegate actually **writes**? | **Nothing, anywhere.** The scope is echoed into the brief header as `scope=…` (`DelegationReportFormatter.cs:53-54`, `:317-318`) and that is its entire influence on the running delegate. The only PreToolUse hook in the system is the orchestrator's deny-**all**-edits hook (`DelegationWorktreeService.cs:112-128`), which is path-blind and can only ever be armed in a task's own worktree (`:132-136`). No diff is ever compared to a declared scope. |
| What does "intersects" mean? | Same `WorkingDirectory` string **and** one glob's *literal prefix* (text before the first `*?[`) is a string-prefix of the other's (`ScopesIntersect`, `:2695-2714`). Four unit tests, all single globs (`DelegationUnitTests.cs:653-687`). |
| Has it ever held anything? | **Once, and wrongly.** The only hold in 623 rows: CARD-0054 slice 3 (`card-reopen-cli`) waited **579 s** behind slice 2 (`card-reopen-client`) — because `"card-reopen-client".StartsWith("card-reopen-cli")`. Two different labels, serialised by string accident. |
| Has it ever *missed* a hold it should have made? | **Five times.** Callers have been writing comma-separated lists since 2026-08-17 (62 of the 128 scoped rows); `LiteralPrefix` treats the whole list as one string, so lists intersect only when one is a prefix of the other. Five pairs of concurrently running same-repo tasks shared an element outright — e.g. both naming `server/Application/Services/SessionMessageQueueService.cs` (CARD-0024 ∥ CARD-0070), both `server/Migrations/` (CARD-0082 S1 ∥ S2), both `AGENTS.md` (CARD-0185 ∥ CARD-0186 S1) — and none was held. |
| Is the card's "went unused for an evening" still true? | **No — the opposite problem now.** 128 of 623 tasks carry a scope; since 2026-08-25 nearly every dispatch does. But 44 of them are **bare labels** (`merge,deploy` ×7, `deploy-restart`, `client-cards,client-thread`, `planning`, `server-logging,server-program` …): callers already invented area names, and the server compares them as string prefixes. The taxonomy the card asks for is half-written in the `ScopeGlob` column already, with nothing behind it. |
| Is there any merge-order coordination? | **None in the server, and the auto-merge path has never seen a conflict.** `TryMergeBackAsync` (`DelegationWorktreeService.cs:200-263`) runs at settlement with no lock between concurrent settlements. Outcomes across all history: **216 `LeftForHuman`**, 8 `Merged`, 8 `AlreadyCleanedUp`, 14 `MergeBackFailed` (all the pre-fix "not a git repository" self-cleanup shape), **0 `Conflicted`**. `MergeTargetRef` is only defaulted when the task has a parent *task* (`AgentTaskService.cs:295-298`); the operator's own orchestrator session is token-less, so its worktree tasks land as `LeftForHuman` and the orchestrator merges by hand (`docs/orchestration-loop.md` §5). The 8 `Merge`-role tasks in the DB were all hand-dispatched (label `merge,deploy`). The three rebases and the one `&&`-swallowed merge in the card's evidence happened **inside delegate sessions**, invisible to the server. |
| So is dispatch-time serialisation solving the wrong end? | **For worktree tasks, yes; for shared tasks, no.** Two worktree tasks in one area cannot corrupt each other — they cost a rebase at merge. Two **Shared** tasks in one checkout collide immediately and area-independently (`git add -A` sweeps, shared `bin/`, shared `git status` — the skill's own 2026-08-18 live miss, `SKILL.md:83-98`). The right policy is therefore **per workspace pair**, not one rule for all areas (§2.3). |

**Net:** the mechanism is ~40 lines in one method and one static helper, with no schema beyond a `varchar(1000)`.
The fix is proportionate to that: parse the list, compare names exactly, resolve names through one per-repo map,
weight by workspace pair, make holds visible, record drift at settlement. No hook, no merge queue, no new service.

---

## 1. Facts, as verified

### 1.1 The path of a `-Scope`

- `scripts/delegate.ps1:64-66` — `[string]$Scope`, comment "Declare the files this task owns; intersecting scopes
  are serialised". Sent as `scopeGlob` (`:200`). No `-Areas`, no listing verb, no validation.
- `.claude/skills/antiphon-delegate/SKILL.md:80` — the one-line table row; nothing says what "serialised" means.
  The shared-vs-worktree checklist (`:83-98`) explicitly says disjoint scopes do **not** make two shared writers
  safe.
- `CreateAgentTaskRequest.ScopeGlob` (`server/Application/Dtos/AgentTaskDtos.cs:33`) → trimmed onto
  `AgentTask.ScopeGlob` (`AgentTaskService.cs:290`; entity `AgentTask.cs:111-112`, `HasMaxLength(1000)` at
  `AppDbContext.cs:1389`). Summary DTO echoes it (`AgentTaskDtos.cs:89`); the client shows `· scope X` in the
  drawer (`client/src/features/delegations/TaskDrawer.tsx:185`), offers a free-text field in `DelegateModal.tsx:69,89`,
  and pre-fills a single file path from the Files review panel (`FilesReviewPanel.tsx:836`,
  `SelectionDelegate.tsx:124,211`). E2E contract snapshot pins two values (`ContractSnapshotTests.cs:263,279`).
- Also stored in the DB, never read: nothing else consumes the column. `grep ScopeGlob` finds the dispatcher,
  the formatter, the DTOs, the entity, and migrations designer files.

### 1.2 What the dispatcher does with it (`AgentTaskDispatcher.TickAsync`)

```
:224-227   queued = Queued tasks, OrderBy CreatedAt
:231-237   heldScopes = (WorkingDirectory, ScopeGlob) of EVERY Dispatched/Working task with a glob
:258-262   foreach queued: if glob intersects any held → skippedScope++; continue   ← stays Queued, no event
:279-280   on successful dispatch, the task's own glob joins heldScopes for the rest of this tick
:2695-2714 ScopesIntersect: same dir (separator-normalised, case-insensitive) AND LiteralPrefix(a) ⊑ LiteralPrefix(b)
           or vice versa; LiteralPrefix = glob with '\'→'/', leading './' trimmed, cut at first '*', '?' or '['
```

Consequences that matter for the design:

1. **Held is invisible.** `TickResult.SkippedScope` reaches one Debug line, gated on `Dispatched > 0 ||
   Failures > 0`. No `AgentTaskEvent`, no `FailureReason`, no create-time hint. The 579-second hold above was
   indistinguishable, from the board, from a task the tick had not yet reached.
2. **Not head-of-line blocking.** A held task does not stop later queued tasks (it is `continue`, not `break`).
3. **Directory comparison is on the task's declared `WorkingDirectory`**, which for a Worktree task is still the
   repo root — `WorktreePath` is only used for the session (`:2130`). So worktree tasks *do* compare with shared
   tasks. But a task dispatched with `-Dir <repo>/client` never intersects a repo-root task at all. `RepoPath`
   is the right key (`AgentTaskService.cs:289`, set for every git-rooted task).
4. **The comparison is a string-prefix test over a single token.** It has no notion of a list, a name, or a
   glob beyond "where does the wildcard start". Every shape callers actually write (§1.3) is wrong for it.
5. **ReadOnly is not exempt** in either direction.

### 1.3 What callers actually write (live DB, 128 scoped tasks)

| Shape | Rows | First–last seen | What `LiteralPrefix` makes of it |
|---|---|---|---|
| comma-separated paths (`a.cs,b.cs,tests/**`) | 62 | 08-17 → 08-25 | one token: the whole string up to the first `*`; intersects only with a string-prefix of itself |
| bare labels (`merge,deploy`, `client-cards,client-thread`, `planning`) | 44 | 08-17 → 08-19 | compared as strings — `card-reopen-cli` ⊑ `card-reopen-client` |
| single glob (`src/Antiphon.SessionRunner/**`) | 12 | 08-18 → 08-19 | what the code was written for |
| single path (`scripts/bootstrap-check.ps1`) | 10 | 08-09 → 08-25 | fine |

- **Holds that happened:** 1 (the false one). **Concurrent same-repo pairs whose per-element scopes genuinely
  intersected and were NOT held:** 5. **Concurrent same-repo pairs of any kind:** 226 — the fleet runs
  concurrently in one repo as a matter of course, and worktrees are what has made that survivable.
- Queue wait, tasks with vs without a scope: p50 2.5 s vs 2.6 s, p90 4.4 s vs 4.7 s. Apart from the one false
  hold, the scope has cost and saved nothing.
- The labels callers chose are a taxonomy draft the card did not know existed: `deploy`/`deploy-restart`/`merge`
  (ops), `client-cards`/`client-thread`/`client-home`/`client-mobile`/`client-plans` (client, sub-areas),
  `server-logging`/`server-program`, `planning`, `cards`. They are finer than the card's table on the client
  side and coarser on the server side.

### 1.4 Enforcement of writes: none, and the deny hook cannot be the vehicle

- The orchestrator deny hook (`DelegationWorktreeService.cs:112-128`) matches `Edit|Write|MultiEdit|NotebookEdit`
  and exits 2 unconditionally — it is "you are an orchestrator, delegate this", not a path rule. It is written to
  `.claude/settings.local.json` **in the task's own worktree only**, because a settings file in a shared
  directory changes every session that runs there (`:132-136`; policy `AgentTaskDispatcher.cs:2682`;
  `DelegationDenyHookPolicyTests`). The check interpreter's hook is the same deny-all shape.
- Therefore a path-scoped blocking hook could only ever protect **worktree** tasks — the ones where an
  out-of-area write is already isolated and costs at most a rebase. It could never protect a shared checkout,
  which is where an out-of-area write actually hurts. Blocking is the wrong tool for the only case that matters.
- What already knows which files a delegate touched: `AgentFilesService` (`server/Application/Services/AgentFilesService.cs:9-16`)
  merges git working-tree changes vs a baseline with `Write`/`Edit`/`NotebookEdit` tool-call paths from the
  session transcript; `DelegateCheckProbe.CheckGitFacts` (`DelegateCheckProbe.cs:145-151`) already collects
  commits and `ChangedFiles` per check-in. Recording drift is a mapping over data that is already gathered.

### 1.5 Merge ordering today

- `AgentTaskReplyService.MergeBackAsync` (`:940-1000`) → `TryMergeBackAsync`: commit-all → `rebase <target>` →
  `fetch . branch:target` / `merge --ff-only` (`DelegationWorktreeService.cs:200-263`, `:270-289`). On conflict:
  Blocked + a child `Merge` task (`AgentTaskService.CreateMergeTaskAsync`, `:761-830`). No cross-task lock; two
  settlements racing on the same target lose the race at the fast-forward and report `Failed`, never corrupt.
- **The conflict path has never run in production** (0 `Conflicted` events). 216 of 246 merge-back outcomes are
  `LeftForHuman` because the token-less operator session cannot inherit a `MergeTargetRef`
  (`AgentTaskService.cs:295-298`), and the documented workflow is that the orchestrator merges `--ff-only`
  itself after verifying on master (`docs/orchestration-loop.md:25,205-210`).
- So "ordering merges" is not a thing the server does or a thing the card's evidence shows the server failing at:
  the rebases happened in delegate sessions that were told to self-merge. What the server *can* cheaply do is
  tell the caller, at completion, that a still-running task overlaps this one's areas — so the caller merges in
  an order that makes the second rebase trivial (§2.4). A merge queue is out of proportion (§4).

---

## 2. Design

### 2.1 One field, two token kinds, exact names

`ScopeGlob` becomes **`Scope`**: a comma-separated list where each token is either an **area name** or a **path
glob**. A token is a path if it contains `/`, `\`, `.` or a wildcard; otherwise it is a name. Both kinds may be
mixed (`delivery,tests/Antiphon.Tests/Application/SessionMessageQueue*`). Every existing row parses under this
rule with no migration of data; the 44 label rows become names, the 62 lists become lists.

Resolution (`ScopeResolver`, a new static helper beside `ScopesIntersect`):

- a **name** resolves through the repo's area map (§2.2) to that area's glob set; a name the map does not know
  resolves to itself as an opaque label (§5 D1);
- a **path** resolves to itself;
- two scopes intersect iff **any** resolved element of one intersects any of the other, where two globs
  intersect by the existing `LiteralPrefix` rule (kept — it is the right cheap approximation for dispatch
  gating; a real glob matcher is not needed to decide "might these touch the same tree") and two labels intersect
  by **exact, case-insensitive equality** — never prefix.
- keyed on **`RepoPath`**, falling back to `WorkingDirectory` when null (a non-git directory).

This alone fixes the one false hold and the five missed ones in §1.3, with today's policy unchanged.

### 2.2 Where the area→paths map lives: `antiphon.areas.json` at the repo root

**Recommendation:** one tracked JSON file at the root of each repository delegates work in, read by the server
from `task.RepoPath` at create and dispatch (cached by path + mtime; a parse failure logs a Warning and behaves as
"no map", never fails a dispatch). Reasons, against the alternatives:

- *Not* `appsettings.json` / `DelegationSettings`: areas are facts about a **repo's** layout, and tasks carry
  their own `WorkingDirectory`/`RepoPath` precisely so cross-repo orchestration works (`AgentTask.cs:86-91`).
  A server-global map would be wrong for ClaudeBot the day it is used there.
- *Not* `.antiphon/…`: that directory is gitignored (`.gitignore:48`); a map that must be reviewed cannot live in
  the throwaway-reports folder behind a `!` exception nobody remembers.
- *Not* the `Project` entity / a board YAML block: editable only through the UI, invisible in a diff, and the
  people who add an area are the people editing code in that area — the map should land in the same commit.
- Root-level, next to `AGENTS.md`, is where the `tracker:` doc, `global.json` and `Directory.Build.props`
  conventions already put repo-wide contracts. `.claude/skills/antiphon-delegate/SKILL.md` links to it.

Shape (v1; `serialise` is the default weight, see §2.3):

```jsonc
{
  "$schema": "docs/schemas/antiphon.areas.schema.json",
  "areas": {
    "session-launch":    { "paths": ["server/Application/Services/AgentSessionService*.cs",
                                     "server/Infrastructure/Agents/**",
                                     "tests/Antiphon.Tests/Application/AgentSessionLaunch*"] },
    "session-lifecycle": { "paths": ["server/Application/Services/AgentSupervisor*.cs",
                                     "server/Application/Services/SessionReconciliation*.cs",
                                     "server/Application/Services/AgentControlService*.cs"] },
    "delegation":        { "paths": ["server/Application/Services/AgentTask*.cs",
                                     "server/Application/Services/Delegation*.cs",
                                     "server/Application/Services/DelegateCheckProbe.cs",
                                     "server/Application/Settings/DelegationSettings.cs",
                                     "scripts/delegate.ps1", ".claude/skills/antiphon-delegate/**"] },
    "delivery":          { "paths": ["server/Application/Services/SessionMessageQueueService*.cs",
                                     "server/Application/Services/*DeliveryProfile.cs",
                                     "src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs",
                                     "src/Antiphon.Agents.Pty/ComposerDeliveryEvidence*.cs",
                                     "server/Application/Settings/SupervisionSettings.cs"] },
    "pty":               { "paths": ["src/Antiphon.Agents.Pty/**", "src/Antiphon.PtyHost*/**",
                                     "tests/Antiphon.Agents.Pty.Tests/**", "src/Antiphon.FakeClaude/**", "src/Antiphon.FakeGrok/**"] },
    "runner":            { "paths": ["src/Antiphon.SessionRunner/**", "src/Antiphon.SessionRunner.Contracts/**",
                                     "tests/Antiphon.SessionRunner.Tests/**"] },
    "herdr":             { "paths": ["src/Antiphon.SessionRunner/Herdr*", "server/Application/Services/Herdr*",
                                     "docs/herdr-sessions.md"] },
    "schema":            { "paths": ["server/Migrations/**", "server/Infrastructure/Data/AppDbContext.cs",
                                     "server/Domain/Entities/**"] },
    "board":             { "paths": ["server/Application/Services/Card*.cs", "server/Application/Services/Board*.cs",
                                     "server/Api/**/Card*", "scripts/card.ps1"] },
    "tracker":           { "paths": ["server/Application/Services/Tracker*.cs", "scripts/github-sync.ps1",
                                     "docs/workflow-tracker-block.md"] },
    "channels":          { "paths": ["server/Application/Services/Channel*.cs", "server/Application/Services/Chat*.cs",
                                     "server/Application/Services/Telegram*.cs", "server/Application/Services/Slack*.cs",
                                     "src/Antiphon.Messaging*/**", "docs/messaging/**", "docs/telegram*.md", "docs/slack*.md"] },
    "agents-admin":      { "paths": ["server/Application/Services/AgentService.cs",
                                     "server/Application/Services/AgentTuiProfile*.cs",
                                     "server/Application/Services/ApiKey*.cs"] },
    "client":            { "paths": ["client/**"] },
    "ops":               { "paths": ["scripts/**", "*.ps1", "Antiphon.AppHost/**", "docker-compose*.yml"] },
    "docs":              { "paths": ["docs/**", "AGENTS.md", "CLAUDE.md", "README.md", ".claude/skills/**"],
                           "weight": "allow" }
  }
}
```

Changes from the card's table, and why: **`schema`** is added because two migrations in flight conflict on
`AppDbContextModelSnapshot.cs` every single time (CARD-0082 S1 ∥ S2 ran concurrently, §1.3) — it is the most
reliable collision in the repo and no area in the card's list names it. **`tracker`**, **`herdr`**,
**`agents-admin`** and **`ops`** are added because they are the areas the last ten days' scopes actually named
(§1.3: four `TrackerBidirectionalSyncService` tasks, seven `merge,deploy`, the herdr S1–S4 cards). `docs` gets
`weight: allow` because two docs tasks colliding costs a bullet-order rebase, and because nearly every card
appends to `AGENTS.md` — serialising on it would serialise the fleet. Areas overlap on purpose (`delivery` and
`pty` both reach into `Antiphon.Agents.Pty`, as the card says); overlap is resolved by the path-set intersection,
not by making areas disjoint. A `settings` area was considered and rejected: a settings class belongs to the area
that owns it. **The exact globs are S2's first deliverable and the operator's to review** — the list above is
seeded from the file names in §1 and the collisions in §1.3, not from a tree walk.

Rule for extending it, written into the file's header and the skill doc: *an area is added when two tasks
collide in it, named for the work, not the folder.*

### 2.3 Weight by workspace pair, not one rule per area

The card asks whether overlap severity should be coarse-grained per area. The evidence says the severity is
mostly a property of **the two workspaces**, and only secondarily of the area:

| Queued task ↔ running task, same repo, scopes intersect | Policy | Why |
|---|---|---|
| **Shared ↔ Shared** | **serialise** (hold, as today, but visible) | the only pair that corrupts: one checkout, one `git status`, one `bin/` |
| **Shared ↔ Worktree** / **Worktree ↔ Shared** | **warn** — dispatch now, `Warning` event on the queued task naming the running one | the worktree task is isolated; the shared one is the parent's checkout the merge lands in — the caller should know a rebase is coming |
| **Worktree ↔ Worktree** | **warn** | collides at merge only; blocking dispatch throws away the parallelism worktrees exist to give |
| **ReadOnly ↔ anything**, either direction | **allow** — holds nothing, held by nothing, no event | it writes nothing; the four scoped ReadOnly rows in the DB could only ever have held a writer for no reason |
| any pair where **every** intersecting area is `weight: allow` | **allow** | `docs`; the per-area weight is a *downgrade* only — it can never raise `warn` to `serialise` |

Two consequences worth stating: (a) the intersect-and-serialise arm becomes **narrower** than today (worktree
tasks stop being held), which is correct because §1.3 shows the hold has never once protected anything;
(b) `Shared ↔ Shared` remains the one silent-until-now arm, and S1 makes it loud.

**Undeclared shared writers (operator decision D3):** the skill doc already states that a second write-capable
task in a shared checkout is a collision *regardless of scope* (`SKILL.md:83-98`), and its 2026-08-18 live miss is
exactly a caller not asking that question. The dispatcher can ask it: a Queued **Shared** task with a Dispatched/
Working **Shared** task in the same repo, **whether or not either declares a scope**, is held under
`Delegation:SerialiseSharedWriters` (default **true** recommended). Check-role and ReadOnly tasks are outside it.
With S1's Held event this is a visible wait, not a silent one.

### 2.4 Visibility: a hold is an event, a warning is an event, the caller hears it at dispatch

- **`Held` event** (new `AgentTaskEventType.Held`), written **once** when a task is first skipped for scope, text
  `Held: scope 'delivery' intersects running task <short-id> "<title>" (Shared ↔ Shared)`. Re-holds on later ticks
  do not re-write it; a hold that resolves is not an event (dispatch is). Surfaces in the drawer and the check
  digest automatically — events already do.
- **`Warning` event** (existing type) for the warn arm, same sentence with `— dispatching anyway; expect a rebase
  against <branch>`.
- **Create-time answer:** `POST /api/agent-tasks` already knows the running set; the response gains
  `scopeOverlaps: [{taskId, title, workspace, policy}]`, and `delegate.ps1` prints `will wait behind 3f2a1c…
  (delivery, Shared↔Shared)` or `overlaps 3f2a1c… (worktree) — merge order matters` under its existing routing
  line. **This is the ergonomic centre of the card**: the caller declares intent and is told, immediately, what
  that intent costs.
- **Tick log** at Information, once per hold *transition* (held→not, not→held), not per tick.
- **Completion note header** (`DelegationReportFormatter.BuildBrief`/completion path) gains `areas=…` in place
  of `scope=…`, plus `overlapping-running=<ids>` when non-empty — this is the whole of the "merge ordering"
  deliverable: the caller merging by hand (§1.5) is told which still-running task's areas this one touched, and
  merges this one first or expects the rebase. No queue, no lock.

### 2.5 Enforcement: record drift, do not block

**Recommendation: record.** At settlement (and, cheaply, at each check-in probe), take the task's touched paths —
`AgentFilesService`'s union of git changes vs the dispatch baseline and `Write`/`Edit` tool-call paths — map each
through the repo's area map, and:

- store the result on the task as **`ObservedScope`** (same `varchar(1000)` shape as `Scope`: area names, plus
  any path that matched no area);
- when `ObservedScope` contains an area (or an unmapped path) not covered by the declared `Scope`, write a
  **`ScopeDrift` Warning event**: `Touched 'schema' (server/Migrations/20260827_…cs) outside declared
  [delivery]`, and add `drift=schema` to the completion note header;
- never fail, hold, kill or re-type anything on drift.

Why not block, given §1.4: a path hook can only be armed in a worktree, where an out-of-area write is already
harmless; the card's own evidence is that predicted file lists are wrong (an enum, a DI registration, a settings
class), so a blocking rule converts every wrong prediction into a stuck delegate at exactly the moment it found
the file nobody predicted; and the deny hook's existing message contract ("delegate this instead") has no honest
equivalent for "you may not touch this file" when the file is the one the task needs. Recording, by contrast,
makes both the declaration and the map converge on the truth: a `ScopeDrift` that recurs is either a caller who
should declare `schema` too, or a map missing a path — both are one-line fixes visible in the event log. A later
card can turn a *specific* drift shape into a block for worktree tasks once the events show one that deserves it;
nothing in this design forecloses that.

### 2.6 Surfaces that change

| Surface | Change |
|---|---|
| `scripts/delegate.ps1` | `-Scope` keeps its name; comment rewritten ("area names from antiphon.areas.json and/or path globs; Shared↔Shared intersections wait, worktree intersections warn, ReadOnly never waits"). New `-ListAreas` (GET `/api/agent-tasks/areas?directory=`) prints the map with each area's paths and weight. Prints `scopeOverlaps` from the create response. |
| `.claude/skills/antiphon-delegate/SKILL.md:80` | row rewritten to say what happens; a short "declare areas" paragraph under the shared/worktree checklist pointing at `antiphon.areas.json`. |
| `AgentTask.ScopeGlob` → `Scope`; new `ObservedScope` | one `RenameColumn` + one `AddColumn` migration. API field `scopeGlob` → `scope` (+ `observedScope` on the summary). Client: `agentTasks.ts:86,158`, `TaskDrawer.tsx:185`, `DelegateModal.tsx:34,69,89`, `SelectionDelegate.tsx:104-124,211`, `FilesReviewPanel.tsx:836` (a file path is still a valid token). E2E `ContractSnapshotTests.cs:263,279` re-pinned. |
| `AgentTaskEventType` | `+ Held = 18`, `+ ScopeDrift = 19` (append; the enum is stored as int). |
| `DelegationSettings` | `+ SerialiseSharedWriters = true`, `+ AreasFileName = "antiphon.areas.json"`. |
| `AGENTS.md` | one Gotcha bullet: what `-Scope` does now, and that a hold is a `Held` event, never silent. |

---

## 3. Slices, tests, tiers

Each slice is independently mergeable and leaves the system strictly better than the one before it. S1 alone
fixes every measured defect in §1.3.

| # | Slice | Tests (all `Antiphon.Tests`, `[Category("Unit")]` unless noted) | Role / tier |
|---|---|---|---|
| **S1** | **Parse the list, compare names exactly, exempt ReadOnly, key on RepoPath, make holds visible.** `ScopeResolver.Parse`/`Intersects` replacing the single-token `ScopesIntersect`; ReadOnly excluded from both sides of the query at `:231-237` and `:258`; `Held` event once; Information log on transition. No schema change (the column is still `ScopeGlob`). | `DelegationScopeLeaseTests` extended: comma list ∩ comma list per element; `card-reopen-cli` ≠ `card-reopen-client`; `delivery` = `Delivery`; `a.cs,tests/**` ∩ `tests/Foo.cs`; ReadOnly holds nothing / is held by nothing; `-Dir <repo>/client` ∩ repo-root task (RepoPath key). Dispatcher tick test (pattern of `AgentTaskPoolTests`, `[NotInParallel]`, assertions scoped to the rows it made): a held task carries exactly one `Held` event across three ticks, and dispatches on the fourth when the holder settles. | Code · **Codex terra** is enough — ~80 lines, fully specified by the table above, no judgment calls |
| **S2** | **The area map.** `antiphon.areas.json` (seeded from §2.2, globs verified against the tree), its JSON schema under `docs/schemas/` (new directory), `AreaMapLoader` (per-RepoPath, mtime cache, Warning-and-no-map on parse failure), name→globs resolution inside `ScopeResolver`, unknown-name `Warning` event (D1), `GET /api/agent-tasks/areas`, `delegate.ps1 -ListAreas`, column/API rename `ScopeGlob`→`Scope`, contract snapshot re-pin, client field renames. | `AreaMapLoaderTests`: missing file ⇒ empty map; malformed ⇒ Warning + empty; mtime change ⇒ reload; `weight` parsed, default `serialise`. `ScopeResolver` with a map: `delivery` ∩ `pty` via the shared `Antiphon.Agents.Pty` glob; `docs` ∩ `docs` under weight allow ⇒ no intersection; unknown name ⇒ label, exact-match only. A test that loads the **real** `antiphon.areas.json` and asserts every glob's literal prefix exists in the tree (so a rename in the repo goes red here, not silently). E2E contract snapshot updated. | Code · **opus** (touches DTOs, a migration, the client and a contract snapshot — enough surface to want judgment) |
| **S3** | **Pair-weighted policy** (§2.3): the four arms, `Warning` event on the warn arm, `scopeOverlaps` in the create response + `delegate.ps1` print, `overlapping-running=` in the completion header, `SerialiseSharedWriters` (D3). | Tick tests per arm: Shared↔Shared held; Shared↔Worktree dispatched with one Warning naming the running id and branch; Worktree↔Worktree same; ReadOnly↔Shared nothing; undeclared Shared↔Shared held under the setting, dispatched with it off; docs↔docs allowed. Create-response test: `scopeOverlaps` lists the running task with `policy: "serialise"`. Completion-note test: header names the overlapping running id. | Code · **opus** |
| **S4** | **Record drift** (§2.5): `ObservedScope` column + migration, settlement-time mapping via `AgentFilesService`, `ScopeDrift` event, `drift=` in the completion header; optional per-check `observed=` line in `DelegateCheckProbe`'s git facts. Never blocks. | `ScopeDriftTests` with a fake `AgentFilesService`: paths inside declared areas ⇒ no event, `ObservedScope` = declared; a `server/Migrations/…` write under declared `delivery` ⇒ one `ScopeDrift` naming `schema` and the path; an unmapped path ⇒ named verbatim; a task with no declared scope ⇒ `ObservedScope` filled, no drift event (nothing to drift from); settlement never throws when the files service does (mirror of the "observability must never break settlement" catch at `AgentTaskReplyService.cs:927-934`). | Code · **opus** |
| **S5** | **Docs**: skill row + paragraph, `AGENTS.md` bullet, `docs/orchestration-loop.md` §5 gets two lines on `overlapping-running=`, header comment in `antiphon.areas.json` with the extension rule. | Read-through; the S2 real-map test is the only executable check. | Docs · **sonnet** |

Order: S1 → S2 → S3 → S4 → S5; S1 can ship today, alone, and S3/S4 are independent of each other after S2.
Verification for each slice is the slice's own test class by `--treenode-filter`, then the
`Antiphon.Tests.Application` chunk once before merge. Deploy: server restart only (`scripts/restart-apphost.ps1`);
S2 also needs `npm run build` in the main checkout for the client field rename.

Estimated: S1 ~1 h, S2 ~3 h (the map's globs are the slow part — verify each against the tree), S3 ~2 h, S4 ~2 h,
S5 ~30 min.

---

## 4. Non-goals

- **A PreToolUse path hook.** §1.4: it can only be armed where it does not matter, and it turns wrong predictions
  into stuck delegates. Drift is recorded; a later card may block a specific recurring shape.
- **A merge queue or automatic merge ordering.** §1.5: the server's auto-merge has never met a conflict; the
  operator merges by hand by design (`orchestration-loop.md` §5). The completion header's `overlapping-running=`
  is the entire merge-ordering deliverable.
- **Fixing `LeftForHuman` for token-less callers** (defaulting `MergeTargetRef` to the repo's base branch). Real,
  separate, and it changes who lands code on `master` — its own card.
- **A real glob matcher** for dispatch gating. `LiteralPrefix` per element is the right cost for "might these
  touch the same tree"; S4's drift mapping is the one place a real match is needed, and `AgentFilesService`
  already works on concrete paths, so it uses `Microsoft.Extensions.FileSystemGlobbing` (already a direct reference of
  `Antiphon.Server.csproj`) there only.
- **Migrating the 128 historical `ScopeGlob` strings.** They parse under §2.1 as-is; the column rename is a
  rename, not a rewrite.
- **Area-based routing** (picking an agent or a pool by area), area-based cost accounting, or per-area
  `MaxConcurrentTasks`. Nothing in the evidence asks for them.
- **Cross-repo areas.** The map is per repo; a task in ClaudeBot reads ClaudeBot's file or has no map.

---

## 5. Operator decisions (each with a recommendation)

| # | Decision | Recommendation |
|---|---|---|
| **D1** | An area name the repo's map does not know: **reject** the create (400 listing the known names) or **accept as a label + Warning event**? | **Accept + warn.** A bookkeeping field must never refuse a launch (the CARD-0136 lesson: a 409 on dispatch is a refusal, and this one would be for a typo). The Warning names the known list; `delegate.ps1` prints it; the label still exact-matches another task using the same unknown name, which is strictly better than today. Revisit to reject if the events show names drifting instead of the map being extended. |
| **D2** | Rename `ScopeGlob`/`scopeGlob` → `Scope`/`scope` in S2, or keep the name and only change the semantics? | **Rename.** A field called `scopeGlob` holding `delivery,schema` is a standing lie in every API response and brief header; the cost is one `RenameColumn`, five client lines and a contract-snapshot re-pin, all in the slice that already touches them. |
| **D3** | `SerialiseSharedWriters` — hold a Shared task behind any running Shared task in the same repo **even with no scope declared** (§2.3)? | **On by default.** The skill doc already says this pair is never safe and the one live miss was a caller forgetting to check. With S1's `Held` event the wait is visible and the caller can `-Worktree` and re-dispatch. The setting exists so an operator running deliberately sequential shared tasks in two checkouts of one repo can turn it off. |
| **D4** | Should `docs` be the only `weight: allow` area in v1? | **Yes.** It is the only area with evidence (AGENTS.md touched by nearly every card, trivially rebased). `client` was considered — its sub-area labels (`client-cards`, `client-thread`…) show callers already wanting finer grain there — but the right move is finer **areas** (`client-board`, `client-agents`, …) in the map once two client tasks actually collide, not a blanket allow. |
| **D5** | Should S4's drift mapping run at every check-in, or only at settlement? | **Settlement only in S4**, with the check-probe line as a follow-up if the operator finds drift worth seeing mid-task. Settlement is where `AgentFilesService`'s baseline is unambiguous; a per-check computation adds a git call to a probe that already runs `log`/`status` and is on a 5-minute cadence. |
