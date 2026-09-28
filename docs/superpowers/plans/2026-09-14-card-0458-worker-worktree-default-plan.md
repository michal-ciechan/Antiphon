## Design (Plan task 5cfef7f8, 2026-09-14, against Antiphon 10390c81)

Stage: Plan. Verification design is a separate TestDesign stage. Written onto the card because the
Plan task could not write into C:\src\Antiphon; a Docs task should copy this section verbatim to
docs/superpowers/plans/2026-09-14-card-0458-worker-worktree-default-plan.md.

### Ground truth (card assumption vs code)

- Workers default to the shared checkout: TRUE. AgentTaskService.ResolveWorkspace returns Shared
  for every non-orchestrator request with no explicit workspace; delegate.ps1 sends `workspace`
  only when -Worktree / -Shared / -ReadOnly is passed.
- One Shared slot serialises across roles: TRUE. SharedWriterLeaseProjection.Holder.Evaluate
  returns Serialise for any Shared<->Shared pair in the same repo with no intersecting scope while
  Delegation:SerialiseSharedWriters (default true) is on; AgentTaskDispatcher skips the task and
  writes one Held event.
- The waiting message names a blocking task id: TRUE on all three surfaces. delegate.ps1 prints
  `will wait behind <id> (shared checkout, Shared)`; the Held event reads `Held: running task <id>
  "<title>" is already writing in this shared checkout (Shared <-> Shared; no intersecting scope -
  two shared writers share one working tree).`; the home rail reads `waiting: shared checkout held
  by task-<id> - <title> +N`.
- -IgnoreConcurrencyLimit is the wrong instrument: TRUE, and it fights a DIFFERENT mechanism. The
  create-time gate (DelegationOpenGate: Delegation:MaxOpenTasks 3 per project, and
  RolePolicy.RecommendedInFlight 1 for Plan/Code/Review) refuses a second open same-role task with
  409 regardless of workspace. This card removes the dispatch-time hold only; the create-time cap
  is untouched (see H-4).
- Nothing holds this per project today: TRUE. Project carries DefaultLaunchEnvJson and
  OrchestratorWorkspaceAcknowledgedAt as per-project delegation defaults and nothing about
  workspace. The task's project is a stored column (AgentTask.ProjectId; CARD-0115 S1: never
  derived from a path).
- Ordering trap: ResolveWorkspace runs at AgentTaskService.cs:425; projectId is derived at :833.
  The project default needs the project first (S3 hoists it; both inputs, caller and parent, are
  known at :358).
- Worktree-by-default would have broken slides: CONFIRMED. slides HEAD a58ef2c; deck/,
  deck-variants/, notes.md untracked; README/examples modified. CreateForTaskAsync runs
  `git worktree add` from MergeTargetRef ?? HEAD; nothing copies working-tree state.
- Cleanup is currently refused on Antiphon: GuardedWorktreeRemoval.HasProtectedIgnored refuses
  removal when any ignored path exists (CARD-0452 D2, open); the janitor PruneStaleAsync (7 days)
  routes to the same remover. Live: 291 registered worktrees, 313 card-task-* directories under
  C:\Antiphon\worktrees; an unbuilt one is ~58 MB / 4.6k files; C: has 684 GB free.
- Mix since 2026-08-14 (1,771 Worker rows): Shared 874, Worktree 862, ReadOnly 35. Code is
  already mostly Worktree (474 vs 191). Plan (237 Shared vs 144), Review (156 vs 104),
  Investigate (35 vs 32) and Docs (34 vs 0) are mostly Shared: those are the rows the flip moves.
- Merge-back at settle needs a clean target checkout (ReadCleanTargetAsync requires an empty
  status, untracked included); 216 of 246 merge-backs are LeftForHuman, so a Worktree task's
  output normally sits on a kept branch until -Land. Flipping Plan/Investigate/Docs adds a land
  step per stage. The pipeline already assumes it (CARD-0215, ready rows, `next: land`).
- The Antiphon main checkout is clean right now (0 status lines), so the dirty-source fallback in
  D-4 does not defeat the default there.

### Decisions

D-1 Setting home: a nullable column Projects.DefaultWorkerWorkspace (text; `Shared` | `Worktree`;
null = inherit). It sits on the row that already holds the other per-project delegation defaults
and is edited by PUT /api/projects/{id} and the Settings > Projects modal. Rejected: a repo-root
file like antiphon.areas.json (path-derived, which CARD-0115 forbids for scope; a -Dir into
another checkout would read the wrong file; committed and shared across machines while disk and
worktree limits are per machine). Rejected: config-only (the card asks for per-project; slides
needs Shared permanently while Antiphon wants Worktree).

D-2 Global tier: Delegation:DefaultWorkerWorkspace (`Shared` | `Worktree`), ships `Shared`. The
options validator refuses ReadOnly.

D-3 Resolution order, exact (ResolveWorkspace, now also given projectId, role, pin and the probe
result):
1. Explicit workspace on the request (-Worktree / -Shared / -ReadOnly): honoured verbatim, existing
   warnings kept. -Shared becomes the documented opt-out for workers as well as sub-orchestrators.
2. Modes the server already pins: a live follow-up (-OnAgent) forces Shared; SourceLanding requires
   explicit Worktree (422 otherwise); auto-created Merge tasks set Shared. Unchanged.
3. Orchestrator kind: existing rule (own -Dir is isolation; else Worktree when a repo; else Shared
   plus warning). Unchanged.
4. Worker kind with role in {Deploy, Commit, Merge}, or pinned to a standing agent (-Agent /
   agentId): Shared. Deploy restarts the stack from the main checkout (AGENTS.md refuses
   worktrees), Commit commits the shared tree by definition, Merge integrates in place, and a
   standing agent's value is its own cwd.
5. Otherwise the project default; null falls to the global default.
6. A resolved Worktree with no repo (RepoPath null) becomes Shared with the existing "is not a git
   repository" warning.
7. A Worktree resolved BY DEFAULT whose source checkout is dirty becomes Shared with a warning
   (D-4). An explicit -Worktree is never downgraded.
The outcome is recorded as AgentTask.WorkspaceSource (nullable text: Explicit, Project, Global,
DirtySource, NoRepository, Pinned, Orchestrator; null on historical rows), echoed in
AgentTaskCreatedDto.workspace / workspaceSource, and written into the Created event detail
("Worktree (project default)"). delegate.ps1 prints `workspace: worktree (project default)`.

D-4 Dirty source tree: fall back to Shared, say why, name the flag. Probe: `git status --porcelain
--untracked-files=normal` in resolved.RepoPath (ignored files excluded, so bin/obj/node_modules
never count), 10 s budget, run only when step 5 resolved Worktree by default. Dirty = any line.
Warning: `Project default is Worktree, but <repo> has <n> modified and <m> untracked file(s) that
a worktree branched from HEAD would not contain (<first three paths>). Running Shared instead.
Pass -Worktree to isolate anyway (it will not see those files), or -Shared to silence this.`
Probe failure or timeout: honour the default and warn `could not verify that <repo> is clean`
(a probe fault must not serialise the fleet).
Rejected: carry uncommitted changes into the worktree. It is a snapshot that diverges from the
orchestrator's live tree the moment it is taken; the carried diff is then committed on the task
branch while the same diff still sits uncommitted in the shared checkout, so merge-back fails
target_dirty_or_unknown and the land conflicts; for an untracked-only repo like slides it turns
"empty worktree" into "work stranded on a branch that cannot fast-forward a dirty checkout"; and
ignored-vs-untracked copy rules are a second policy surface. Could be a later opt-in flag; not
now. Rejected: require a commit first (the server would commit half-done orchestrator work under
its own name, and several boards forbid delegate commits). Rejected: a per-project dirty-policy
knob (a two-value setting plus one fixed rule is enough; a project whose tree is dirty by design
sets Shared).

D-5 The waiting message, verbatim.
delegate.ps1 create echo, no intersecting areas:
`  will wait: nothing asked to isolate this task, so it shares the one shared-checkout slot for
<repo> with <N> running task(s): <id> "<title>". This is not a dependency. Pass -Worktree to run
it now in its own worktree, or set the project default: project.ps1 set <project>
-DefaultWorkerWorkspace Worktree.`
When WorkspaceSource is Explicit the clause "nothing asked to isolate this task" becomes "you
passed -Shared"; when DirtySource it becomes "it fell back to Shared because <repo> had
uncommitted changes".
Held event (Overlap.Describe, Areas null):
`Held: sharing the one shared-checkout slot with running task <id> "<title>" (Shared <-> Shared,
no intersecting scope). Not a dependency: nothing asked to isolate this task. Re-dispatch with
-Worktree, or set the project's default worker workspace to Worktree.` Same two variants.
Home rail and pipeline label:
`waiting: sharing the shared-checkout slot with task-<id> - <title> (+N); -Worktree would run it
now`.
The intersecting-areas sentences stay as they are: that is real contention.

D-6 Migration: one additive migration (two nullable columns, no backfill). Config default Shared.
Deploying changes nothing for anyone. Flipping a project is one explicit write:
`project.ps1 set Antiphon -DefaultWorkerWorkspace Worktree` (new verb: GET then PUT with the same
fields; `Inherit` clears to null; a null field on PUT leaves the setting unchanged, the
defaultLaunchEnv rule). ProjectDto also carries effectiveWorkerWorkspace so the UI and scripts
show what applies.

D-7 Cleanup and disk: this card adds no cleanup mechanism; CARD-0452 D2 owns the automatic story
and is the gate for H-1. Today: Merged or NothingToMerge at settle call RemoveLocalAsync, which the
guard refuses whenever ignored content exists (every built Antiphon worktree). LeftForHuman keeps
branch and worktree until -Land, whose cleanup is refused the same way. Failed and Canceled tasks
never remove their worktree (kept as evidence; -WorktreeHealth detects, never prunes). The janitor
sweeps after 7 days through the same refusing remover. So on Antiphon the flip multiplies retained
worktrees by the Shared share of Plan/Review/Investigate/Docs rows (about 20 a day at the recent
rate) until D2 lands; unbuilt ones are ~58 MB, built ones add bin/obj/node_modules. On a repo with
no ignored content (slides-like) removal works as designed. Optional S6: warn at create when the
repo has more than Delegation:WorktreeCountWarnAt (50) registered card-task-* worktrees, reusing
the -WorktreeHealth enumeration.

D-8 Not built here: carrying changes; commit-first; a per-role matrix per project; any change to
the create-time cap (MaxOpenTasks / RecommendedInFlight), although it is the other half of the
-IgnoreConcurrencyLimit habit; new cleanup; -BaseOn (CARD-0215); flipping the global default.

### Slices (files, in order)

S1 storage: server/Domain/Entities/Project.cs; server/Domain/Entities/AgentTask.cs;
server/Domain/Enums/AgentTaskEnums.cs (WorkspaceSource enum; WorkspaceMode doc no longer says
"Shared is the default"); server/Application/Settings/DelegationSettings.cs (+ validator);
server/Migrations/2026091xxxxxxx_Card0458WorkerWorkspaceDefault.cs + AppDbContextModelSnapshot.cs.
S2 project surface: CreateProjectRequest.cs, UpdateProjectRequest.cs, ProjectDto.cs,
ProjectService.cs; scripts/project.ps1 (`set`); client/src/api/projects.ts;
client/src/features/settings/ProjectConfig.tsx (+ Select) and ProjectConfig.test.tsx; new
tests/Antiphon.Tests/Application/ProjectWorkerWorkspaceDefaultTests.cs.
S3 resolution: server/Application/Services/AgentTaskService.cs (hoist projectId above :425;
ResolveWorkspace takes project default, global default, role, pin and probe; sets WorkspaceSource;
Created event); DelegationWorkspaceResolver.cs (+ ProbeCleanlinessAsync);
server/Application/Dtos/AgentTaskDtos.cs (+ workspace, workspaceSource on the created DTO and
workspaceSource on detail); client/src/test/fixtures/contract/agent-task-detail.json; new
tests/Antiphon.Tests/Application/AgentTaskWorkspaceDefaultTests.cs.
S4 messages: SharedWriterLeaseProjection.cs; scripts/delegate.ps1 (echo, header, -Worktree and
-Shared comments); client/src/features/home/tasks/homeTasksModel.ts,
client/src/features/orchestrator/pipelineStageModel.ts and their tests;
SharedWriterLeaseProjectionTests.cs; new DelegateScriptWorkspaceTests.cs on DelegateScriptRunner
(no `workspace` in the body by default, `Shared` with -Shared, the echo line).
S5 docs: delegate.ps1 header; .claude/skills/antiphon-delegate/SKILL.md ("Workers default to
shared" section); docs/orchestration-loop.md; docs/antiphon-api.md (projects, create request,
pipeline reasons); AGENTS.md one line.
S6 optional: worktree-count warning.
S1 and S2 ship alone with zero behaviour change; S3 depends on S1; S4 on S3; S5 last.

### Acceptance cases for TestDesign

1 explicit -Worktree / -Shared / -ReadOnly unchanged under every project setting. 2 project
Worktree, clean repo, Worker/Plan, no flag: Worktree, source Project. 3 project null, global
Shared: Shared/Global. 4 project null, global Worktree: Worktree/Global. 5 project Worktree, dirty
repo: Shared/DirtySource with the warning naming counts, paths and -Worktree. 6 explicit -Worktree
on a dirty repo: Worktree, no downgrade. 7 Deploy, Commit, Merge and -Agent pinned stay
Shared/Pinned under project Worktree. 8 -OnAgent live follow-up stays Shared. 9 non-repo -Dir
under project Worktree: Shared/NoRepository. 10 probe timeout: Worktree with the "could not
verify" warning. 11 orchestrator rules unchanged. 12 Created event and created DTO carry the
source. 13 Held text for Explicit vs defaulted vs DirtySource. 14 delegate.ps1 echo variants. 15
PUT with null leaves the setting, `Inherit` clears it, ReadOnly is 422. 16 migration is additive
and existing rows resolve as before. 17 home-rail and pipeline labels. 18 project.ps1 set
round-trips.

### Human decisions

H-1 Flip Antiphon to Worktree now (retained worktrees accumulate until CARD-0452 D2, ~58 MB+
each) or after D2 lands. Recommended: after D2, or now with S6 and a weekly manual sweep.
H-2 Whether the global default ever moves to Worktree. Recommended: not until D2, and only by an
operator, because every null-setting project changes silently when it does.
H-3 Set slides to Shared explicitly now (one write; the effect is what it has today).
H-4 Whether the create-time cap (1 per role per project) becomes the next card; it is the other
reason -IgnoreConcurrencyLimit was needed on slides.
Cross-project note: the setting is an API write, not a checkout write, so the allowed-roots
restriction does not stop an orchestrator on one project from flipping another project's
default, and there is no authentication. The write bumps UpdatedAt and logs the actor; project.ps1
set and the Settings modal are the documented paths.