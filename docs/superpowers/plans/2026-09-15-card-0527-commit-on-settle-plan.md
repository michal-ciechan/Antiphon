# CARD-0527: Commit on task settle — gated in-process commit, Commit delegate for the rest

Date: 2026-09-15. Stage: Plan; verification design is a separate TestDesign stage.
Based on Investigate task `62040dbd`
([docs/investigations/2026-09-15-card-0527-commit-on-task-settle.md](../../investigations/2026-09-15-card-0527-commit-on-task-settle.md))
and checkout `e7868a49` (the investigation commit; master `0ad20a35` carries the same doc).
Task `35921275`, worktree `card-task-35921275`. Read-only against code and the live board;
no fix was built.

## Outcome and scope

A Shared task that settles with its own work uncommitted gets that work committed **before its
completion note is delivered**, by the server, through a commit primitive whose ignore gate is
code, not brief text. The Commit-role delegate the card asks for is kept, and reached through the
live routing pin (Codex/Low first), but it is the second tier: it runs only when the server
cannot attribute the dirty paths to the task or the gate refuses, and when it does run it commits
through the same gated primitive. Nothing in either tier pushes.

Two facts from the investigation shape this:

1. The server already commits a **Worktree** task's leftovers in-process at settle
   (`TryMergeBackAsync` → `CommitAllChangesAsync`, `add -A`, no gate). The precedent for an
   in-process commit exists; what is missing is the gate, the Shared arm, and a policy.
2. A Shared checkout is not the task's alone. The orchestrator is a live session in the same
   directory, and the `slides` residue was left by earlier tasks that were *told* not to commit.
   Sweeping the whole tree — by server or by agent — is what CARD-0458 D-4 rejected ("the server
   would commit half-done orchestrator work under its own name"). So the automatic path commits
   the **task's footprint**, never the tree.

Out of scope, deliberately: changing what delegates are told about pushing (the
`SharedWriteCommitLine` and the delegate-basics bundle stay); parsing "DO NOT commit" out of goal
prose (a create-time *advisory* only, D-4); reverting commits automatically; any change to the D3
shared-writer hold; CARD-0458's workspace default itself (only its storage shape is shared, D-9);
backfilling historical rows.

## Ground truth

Verified against `e7868a49` on 2026-09-15 unless the row cites the investigation.

| Card assumption or brief shortcut | What the code does | Consequence for the design |
|---|---|---|
| "The harness already detects the condition precisely, counts the files" | `TryDescribeGitAsync` Shared arm ([AgentTaskReplyService.cs:2905-2931](../../../server/Application/Services/AgentTaskReplyService.cs)) counts only the dirty subset of paths **the report named**, capped at 20 (`ExtractReportedRepositoryPaths`). `c90dbfa7` reported 20 of ~61. | The trigger must be a whole-tree status (`GitWorkspaceService.TryGetChangesAsync`, `status --porcelain -z --untracked-files=all`), not the report-derived count. The report-derived set is still useful as one attribution source (D-6). |
| "The routing pin already makes Commit cheap" | True for `CreateAsync` ([AgentTaskService.cs:522-530](../../../server/Application/Services/AgentTaskService.cs)); `CreateMergeTaskAsync` ([:2311-2403](../../../server/Application/Services/AgentTaskService.cs)) never calls `RoutingPinService` and lands on `RolePolicy["Merge"]`. `RolePolicy["Commit"]` is ClaudeCode/Medium ([DelegationSettings.cs:325](../../../server/Application/Settings/DelegationSettings.cs)); the live stage pin is `Required Codex/Low → ClaudeCode/Medium → Grok`. | A Merge-shaped spawn must call `ResolveAsync` + `RoutingCandidates.Compose` itself and store `RoutingPinId` when the list is walked, or the child costs 35x (D-7). |
| "An automatic committer touching `_private/` would be irreversible" | `git add -A` never stages ignored untracked files (investigation §4 case 1). The exposure is: ignore file edited/deleted by the delegate (case 3), a prior `add -f` (case 4), rules added after tracking (case 2, noisy only). Nothing pushes in either tier, so "irreversible" is only ever a push away, and pushes are not automated. | The gate has two checks: refuse when any `.gitignore` is dirty (covers case 3 and every rewrite); refuse when `check-ignore --no-index` matches a candidate path (covers case 4 and case 2). With no dirty `.gitignore`, working-tree rules equal HEAD rules for every versioned ignore file, which is what "HEAD rules" buys (D-5). |
| "Most delegates carried an explicit DO NOT commit instruction" | Prose only: no column on `AgentTask`, no request field, no `delegate.ps1` switch. 59 goals in 30 days match the wording; the match includes the investigation task itself, which merely discusses the rule (§5). | A structured `commitOnSettle` field is the only thing settlement honours (D-3). Prose earns a create-time advisory warning, never a behaviour change (D-4). |
| "Per-project opt-out" | `Project` has `DefaultLaunchEnvJson` and `OrchestratorWorkspaceAcknowledgedAt`; nothing else per-project for delegation. CARD-0458 (Review, design on the card, **no code on any ref** — `git branch -a`/`git log --all --grep 0458` empty) proposes `Projects.DefaultWorkerWorkspace` + `Delegation:DefaultWorkerWorkspace` + `project.ps1 set` + Settings > Projects modal. | Same shape, second field: `Projects.CommitOnSettle` (nullable bool) + `Delegation:CommitOnSettle`; this card builds the `project.ps1 set` verb and the modal row that 0458 will extend (D-9). |
| "Commit, do not push" | `CommitAllChangesAsync` ([GitService.cs:221-243](../../../server/Infrastructure/Git/GitService.cs)) commits only; `LandingGit` is the only push path and belongs to `-Land`. Delegates *are* told to push by their brief (`SharedWriteCommitLine`, delegate-basics). | The auto path has no push call at all (D-8, asserted by test). The delegate's own commit-and-push instruction is untouched: a delegate that commits itself leaves nothing for settlement to do. |
| "Chain a commit task at settle" has a precedent | Yes: `MergeBackAsync` → `CreateMergeTaskAsync` on a rebase conflict, child added to the **same** scoped `AppDbContext` and saved by `PersistDeliverThenReleaseAsync`; the child's short id rides in the note via the `workspaceNote` slot ([:1439-1443](../../../server/Application/Services/AgentTaskReplyService.cs)). | The Commit child follows exactly that shape and slot (D-7). |
| Who touched what is unknowable in a Shared checkout (CARD-0227) | Partly false now: `AgentFilesService.GetAgentActivityAsync` ([AgentFilesService.cs](../../../server/Application/Services/AgentFilesService.cs)) reads `Write`/`Edit`/`NotebookEdit` tool calls from the session transcript, keyed by `file_path`. `RecordScopeDriftAsync` already uses it at settle ([:847-880](../../../server/Application/Services/AgentTaskReplyService.cs)) with `since: null` (whole session). | A per-task footprint exists for ClaudeCode delegates: edits since `DispatchedAt` on `task.AgentSessionId`. Codex/Grok transcripts carry no such tool names; Bash-created files are invisible to it. Those cases go to tier 2 (D-6). |
| The Worktree sweep is already safe | It is not gated, and unlike a Shared commit its branch **is** pushed later by `-Land`. | The same gate wraps the Worktree sweep (S5). Refusal there is `LeftForHuman` naming the paths. |
| A chained Shared child runs promptly | D3 ([SharedWriterLeaseProjection.cs:51-70](../../../server/Application/Services/SharedWriterLeaseProjection.cs), `Delegation:SerialiseSharedWriters` default true) holds it behind any running Shared writer, and holds other Shared writers behind it while it runs. | Tier 1 is why this is acceptable: the common case never queues. Tier 2 is rare and its `Held` event is visible. The hold is not changed (D-10). |
| The settled task still holds the Shared slot when the child is created | `task.Status` is assigned at the top of `SettleAsync` ([:632](../../../server/Application/Services/AgentTaskReplyService.cs)) and persisted with the child in one `SaveChanges`. | The child is never held by its own parent. |
| The report is a good commit-message source | The reporting contract's first line is "the outcome in one line"; delegate-basics repeats it. | Tier 1 subject is `task <short>: <title>`, body is the report's first non-empty line (D-11). |
| `ProgressBaselineJson` could record the child's start SHA | Captured only for `Workspace == Worktree && Role == Code` ([AgentTaskDispatcher.cs:3134-3150](../../../server/Application/Services/AgentTaskDispatcher.cs)). | The Commit child records repo `HEAD` at spawn in a new nullable column (D-7) so its settlement can audit what it committed. |
| The settlement has a slot for what happened to git | `PersistDeliverThenReleaseAsync(... workspaceNote, warning, drift, git ...)`; `DelegationReportFormatter` renders `git=` and the warning ([:592-606](../../../server/Application/Services/DelegationReportFormatter.cs)). | New header values and warning lines only; no new note surface (D-12). |

## Decisions

### D-1. Two tiers, one primitive

**Tier 1 (in-process, at settle).** For an eligible Shared task (D-2) whose whole-tree status is
dirty, the server commits the task's **footprint** (D-6) through `GatedCommitService` (D-5) under
the repository mutation lease, before the note is built. Result in the note header
(`git=committed:<sha7> (N files)`), a `Committed` event on the task, nothing pushed.

**Tier 2 (Commit delegate).** When tier 1 cannot act — footprint empty while the tree is dirty,
gate refusal, lease busy, commit failure (hook), or policy `Agent` (D-3) — a Commit-role child is
spawned in the Merge-task shape, routed through the routing pin (D-7), and told to commit **only
this task's paths**, through `POST /api/agent-tasks/{id}/commit` (the same primitive) rather than
its own `git add`. Its settlement audits every commit it produced (D-7c).

Rejected: **agent-only** (the brief's literal shape). Costs a queued task, the D3 hold and ~2 min
per settle for work the server can do in one lease; loses the per-task commit boundary whenever
the child waits behind another writer; and the ignore gate would be brief text unless the agent is
made to go through an endpoint anyway — at which point the server is doing the commit, and the
agent is only supplying judgement. Keep the agent for exactly the judgement cases.

Rejected: **in-process only.** Codex/Grok and Bash-writing delegates leave no transcript
attribution; a whole-tree sweep in a Shared checkout is CARD-0458 D-4's rejected case; and the
card's author asked for the cheap agent. The agent is the right tool when the answer is "which of
these 61 paths are yours".

### D-2. Eligibility (structural, before policy)

Tier 1 and tier 2 consider a task only when all hold:

- `Status == Succeeded`. Blocked and Failed keep today's warning: a Blocked task's session is still
  live and continuing; a Failed task's partial edits are evidence the caller may want to discard.
- `Workspace == Shared`. ReadOnly never commits (it must not have written). Worktree already
  commits in `TryMergeBackAsync`; S5 adds the gate there.
- `RepoPath` is a directory and `IsRepositoryAsync` is true.
- Role is not `Commit` (no recursion), `Merge` (worktree integration), `Mutation`, `Check`,
  `Distill`, `Diagnose`; `SourceLandingOperationId` is null. Every other role, **including
   `Custom`** (ten of the thirteen `slides` rows) and both kinds, is eligible.

### D-3. Policy: task field > project column > global setting

| Level | Storage | Values | Default |
|---|---|---|---|
| Task | `AgentTask.CommitOnSettle` (`CommitOnSettlePolicy?`, stored as text) | `Never`, `Always`, `Agent` | null = inherit |
| Project | `Projects.CommitOnSettle` (`bool?`) | on / off | null = inherit |
| Global | `Delegation:CommitOnSettle` (`bool`) | on / off | **true** |

Resolution at settle: task value if set; else project (`task.ProjectId`); else global.
`Always` overrides a project/global off for one task. `Agent` skips tier 1 and goes straight to
tier 2 (for a caller who wants a judged message). `Never` skips both tiers.

Request surface: `CreateAgentTaskRequest.CommitOnSettle` (`string?`, same three values, 422 on
anything else); `delegate.ps1 -NoCommit` sends `Never`. `Always`/`Agent` are API-only in this
card (no second switch; a caller who wants them passes the field). A `FollowUpOnTask` child
inherits the prior task's value unless the request sets one. `CreateMergeTaskAsync` and
`CreateCommitTaskAsync` children are created with `Never`.

Global **on** by default, rejected alternative **off** (the `CardFileSync:AutoCommit` precedent):
that switch guards a commit onto master of ~315 unreviewed files; this one guards a local,
unpushed, footprint-scoped commit that `git reset --soft HEAD~1` undoes, on trees that are
already dirty at every one of the 73 warnings a fortnight. The investigation found no
per-board "never commit" policy, only per-task review-first instructions (59 goals across every
project), which `-NoCommit` now carries. H-1 records the operator's right to flip it.

### D-4. `-NoCommit` is the instruction; prose is an advisory

When `CommitOnSettle == Never`, `BuildBrief` replaces `SharedWriteCommitLine` (and adds, for
roles that had no line) with:
`Do NOT commit or push. Leave your changes in the working tree for the caller to review, and name every file you changed in your report.`
Settlement then skips both tiers and renders `git=uncommitted:N (no-commit)` with the warning
`N file(s) left uncommitted as requested (-NoCommit).` — the existing "commit before building on
it" line is not shown, because the caller asked for this.

Prose is not parsed for behaviour. At **create**, when the goal matches the investigation's
wording list (`do not commit`, `don't commit`, `no commit`, `without committing`, case-insensitive)
and the request did not set `commitOnSettle`, the create warning adds:
`Goal text mentions not committing, but commit-on-settle is on for this project; pass -NoCommit if the tree must stay uncommitted.`
This is the defined precedence the card asks for: the flag decides, the prose gets a nudge. The
false-positive the investigation demonstrated (a task that *discusses* the rule) costs one
warning line, never a wrong commit.

### D-5. The gate, and the primitive around it

`GatedCommitService.CommitAsync(repo, pathspec?, message, trailers, ct)` →
`GatedCommitResult { Outcome, Sha?, Files, Refusals }`. Runs under `IRepositoryMutationLease`
(`repository_busy` when it cannot be taken). Git calls go through new `GitWorkspaceService`
methods so the runner stays in one place. `pathspec == null` (whole tree) is permitted only from
the Worktree sweep; the Shared path always passes a pathspec.

1. **Status.** `TryGetChangesAsync` (untracked included, ignored excluded). Empty →
   `NothingToCommit`.
2. **G1 — ignore rules unchanged.** Any status record whose basename is `.gitignore`, at any
   depth, modified, deleted or untracked → `IgnoreRulesChanged` naming them. No staging happens.
   With G1 clean, every versioned ignore file equals its HEAD content, so working-tree rules are
   HEAD rules; this is the "HEAD-rules" property without rewriting patterns (the reason
   `PreviewIgnoreAsync` refuses to reimplement gitignore semantics applies here too). Residual,
   documented: `.git/info/exclude` and the global excludes file are local-only and trusted as
   found.
3. **Candidates.** Status paths ∩ pathspec (or all).
4. **G2 — no ignored path staged.** `git check-ignore --no-index -v -z --stdin` over the
   candidates (the `CardFileRepository.IsIgnoredAsync` shape, extended to return *which* paths and
   the matching `source:line pattern`). Exit 0 → `IgnoredPathStaged` naming path and rule. This
   is what catches a previously force-added ignored file (case 4) and a tracked-then-ignored file
   (case 2); both are refused rather than committed.
5. **Stage.** Scoped: `git add -A -- <candidates>`. Whole: `git add -A`.
6. **Re-check.** `git diff --cached --name-only -z`; whole mode re-runs G2 over the staged list
   (a file created between steps 1 and 5); scoped mode requires staged ⊆ candidates. Any
   refusal here → `git reset -q -- <staged>` (index only, those paths only) and refuse.
7. **Commit.** Scoped: `git commit --only -- <candidates> -m <message> --trailer antiphon=true --trailer antiphon-task=<id> --trailer antiphon-commit=gated`; `--only` leaves anything the orchestrator had staged for *other* paths exactly as it was. Whole: plain `git commit` with the same trailers. Hooks run; a non-zero exit is `CommitFailed` with stderr, index left as staged (that is what a human would see next).
8. Return sha, file list.

There is no push call in the type; a test asserts the runner spy never sees `push`.

Rejected: building the commit in a temporary index (`GIT_INDEX_FILE` + `commit-tree`). Exact
isolation from the shared index, but bypasses hooks and signing config, and leaves the real
index disagreeing with the new HEAD until a pathspec reset — more plumbing for the same
guarantee `--only` gives. Rejected: evaluating HEAD's root `.gitignore` through
`-c core.excludesFile=` (the brief's `PreviewIgnoreAsync` idea): lowest-precedence layer, so a
working-tree negation still wins, and nested ignore files are not covered; G1 makes it redundant.

### D-6. Footprint = what this task can be shown to have touched

Union, then ∩ the whole-tree dirty set, all repo-relative forward-slash:

- transcript edits: `Write`/`Edit`/`NotebookEdit` `file_path`s on `task.AgentSessionId` with
  `CreatedAt > task.DispatchedAt` (new public `AgentFilesService.GetEditedPathsAsync(sessionId, root, since)` wrapping the existing private query; the `since` bound is what makes a warm pool delegate's earlier tasks invisible);
- report-named paths: `ExtractReportedRepositoryPaths(report, repo)` (existing, cap 20).

Footprint empty and dirty set non-empty → tier 2 with reason `unattributable`. Footprint
non-empty → tier 1 commits it; dirty paths **outside** it are reported (`; R other dirty path(s) left as found`) and neither committed nor chained — they are someone else's work or a `-NoCommit` predecessor's, and the header makes them visible every settle until someone acts.

### D-7. The Commit child

`AgentTaskService.CreateCommitTaskAsync(settled, reason, dirtyPaths, ct)`, the Merge shape:

a. **Row.** `ParentTaskId` = settled, same `RootTaskId`, `ParentSessionId`, `ReplyTo`, `CardId`,
   `ProjectId`, `RepoPath`, `StandingAuthority`; `Depth + 1`; `Kind Worker`, `Role Commit`,
   `Stage null`; `Workspace Shared`, `WorkingDirectory` = settled's; `Ephemeral`; `MaxAttempts 2`;
   `AutoContinueOnWait false`; `CommitOnSettle = Never`; new `CommitBaselineSha` = repo `HEAD` at
   spawn; `Title = "Commit: " + Clamp(settled.Title, 240)`. Caps: `MaxTasksPerRoot`, `MaxDepth`
   only (system spawn). A `Created` event: `Spawned by the server to commit N dirty path(s) left by task <short> (<reason>).`
b. **Routing.** `_routingPins.ResolveAsync(settled.CardId, Commit, new Ask(null, null, null, false))`
   → `RoutingCandidates.Compose(decision, chain: null, chainLabel: null, null, null, ResolveRoutingPair(Worker, Commit, ·, ·))`.
   Head candidate → `AgentKind`/`ModelLevel`; `Walked` → `RoutingPinId = decision.Pin.Id` so the
   dispatcher's `IsListGoverned` re-walk applies on a model hold; `EnforceForbiddenAliases` is
   honoured. Pin service absent or throwing → role policy plus a `Warning` event naming it. No
   quota gate, no availability 409 at create (the dispatcher holds, as for every queued row).
   With today's pin this is Codex/Low (gpt-5.6-luna), the six-task mean $0.046.
c. **Brief (Goal).** Settled task id and title; the report's first line; the dirty paths (≤ 60,
   then `+N more`); the reason; the rules — commit only paths that are task X's work, judged from
   the report and `git diff`; use `scripts/task-commit.ps1 -Paths ... -MessageFile ...` (S4),
   which refuses ignored paths and ignore-rule edits; never `git add -f`, never edit `.gitignore`
   or `info/exclude`, never push, never touch other dirty paths; a refusal is reported, not worked
   around. Report: sha and paths, or the refusal verbatim.
   **Audit at the child's settle:** for each commit in `CommitBaselineSha..HEAD`, `diff-tree --no-commit-id --name-only -r -z` piped through G2; any hit → `Warning` event, `DelegateCommitAudit` incident, and the parent-facing line `REVERT commit <sha7>: it contains ignored path(s) <list>`; a commit without the `antiphon-commit=gated` trailer → `Warning` `commit <sha7> was made outside the gate`. No automatic revert: unpushed local history is a human's to rewrite.
d. **Note.** The settled task's header carries `git=uncommitted:N → commit task <short>` in the
   `git` bit; the child's own settlement note reaches the same parent session as any child's.

Rejected: `FollowUpOnTask` on the settled delegate. It inherits the Frontier/High tier the pin
exists to avoid, and the context that was in three cases told not to commit.

### D-8. Never push

Neither tier has a push path. `GatedCommitService` exposes no push; the Commit brief forbids it;
the child's audit warns on any remote-tracking movement (`rev-parse @{u}` compared before/after
when an upstream exists). Publication stays `-Land` or a human.

### D-9. Storage shared with CARD-0458

Same row, same shape, second field. This card lands: migration `AddCommitOnSettlePolicy`
(`Projects.CommitOnSettle bool?`, `AgentTasks.CommitOnSettle text?`, `AgentTasks.CommitBaselineSha text?`),
`UpdateProjectRequest.CommitOnSettle` (`string?`: `On`/`Off`/`Inherit`; null leaves it unchanged,
the `DefaultLaunchEnv` rule), `ProjectDto.CommitOnSettle` + `EffectiveCommitOnSettle`,
`scripts/project.ps1 set <project> -CommitOnSettle On|Off|Inherit` (GET then PUT; the verb
CARD-0458 D-6 also needs), and one row in the Settings > Projects modal. CARD-0458 adds
`-DefaultWorkerWorkspace` to the same verb, modal and DTOs when it lands; if it lands first, this
card's S1 rebases onto its verb. Neither card blocks the other.

### D-10. D3 hold is unchanged

The Commit child is a Shared writer and queues like one. Tier 1 keeps the per-task commit
boundary in the common case, so a held tier-2 child is the exception, visible in its `Held`
event. Not built: queue priority for Commit children.

### D-11. Message

Tier 1: subject `task <short>: <title>` (clamped to 72), blank line, the report's first non-empty
line (clamped to 500), then `Card: CARD-nnnn` when bound. Trailers as in D-5. Tier 2: the agent
writes it, from the report; the trailers are added by the endpoint.

### D-12. What the caller reads

`git=` header values, all rendered by the existing formatter bit:

| Situation | Header | Warning line |
|---|---|---|
| tier 1 committed | `committed:<sha7> (N files)` [`; R left as found`] | none |
| `-NoCommit` | `uncommitted:N (no-commit)` | `N file(s) left uncommitted as requested (-NoCommit).` |
| policy off (project/global) | `uncommitted:N (commit-on-settle off)` | today's "Commit before building on it." |
| tier 2 spawned | `uncommitted:N → commit task <short>` | none (the child reports) |
| tier 2 unavailable (caps) | `uncommitted:N (commit task not spawned: run at task cap)` | today's line |
| gate refused, no child possible | `commit refused: <reason>` | the refusal, paths named |
| clean tree | today's `landed` / `unattributable` | none |

Events: `Committed` (new `AgentTaskEventType`, detail = sha + paths) on tier 1; `Warning` for
every refusal; the child's `Created`.

## Slices

Each slice builds, its tests are green, and it is committed before the next starts.

**S1 — policy storage and surfaces (no behaviour change).**
`server/Domain/Enums/AgentTaskEnums.cs` (`CommitOnSettlePolicy`, `AgentTaskEventType.Committed`);
`server/Domain/Entities/AgentTask.cs` (`CommitOnSettle`, `CommitBaselineSha`);
`server/Domain/Entities/Project.cs` (`CommitOnSettle`);
`server/Application/Settings/DelegationSettings.cs` (`CommitOnSettle = true`);
`server/Migrations/` via `dotnet ef migrations add AddCommitOnSettlePolicy --project server`;
`server/Application/Dtos/AgentTaskDtos.cs` (request field, 422 validation, detail DTO);
`server/Application/Dtos/UpdateProjectRequest.cs`, `ProjectDto.cs`, `server/Application/Services/ProjectService.cs`;
`server/Application/Services/AgentTaskService.cs` (persist the field; follow-up inheritance; D-4 advisory);
`scripts/delegate.ps1` (`-NoCommit`, header comment); `scripts/project.ps1` (`set` verb);
`client/src/api/projects.ts`, `client/src/features/settings/ProjectConfig.tsx` (+ test);
`client/src/test/fixtures/contract/agent-task-detail.json`.
Tests: new `tests/Antiphon.Tests/Application/CommitOnSettlePolicyTests.cs` (resolution order,
422 on bad value, follow-up inheritance, PUT null leaves / `Inherit` clears, advisory warning
fires only without the field); `DelegateScriptRunner`-based test for `-NoCommit` in the body.

**S2 — `GatedCommitService` and the git methods.**
New `server/Application/Services/GatedCommitService.cs`;
`server/Application/Services/GitWorkspaceService.cs` (`CheckIgnoredAsync` returning matches with
rules, `StageAsync`, `CommitOnlyAsync`, `UnstageAsync`, `HeadShaAsync`, `DiffTreePathsAsync`);
DI in `server/Program.cs`.
Tests: new `tests/Antiphon.Tests/Application/GatedCommitServiceTests.cs` on `ScratchGitRepo`,
one test per investigation case (1 commits `new.txt` only; 2 refuses the tracked-then-ignored
file; 3 refuses on deleted `.gitignore`; 4 refuses the force-added file naming the rule), plus
scoped commit leaves foreign staged paths staged, `--only` with a deletion, hook failure →
`CommitFailed`, lease busy → `RepositoryBusy`, trailers present, and the no-push assertion.

**S3 — settle hook (tier 1) and the note.**
`server/Application/Services/AgentTaskReplyService.cs` (new `TryCommitOnSettleAsync` between
the merge-back block and `TryDescribeGitAsync`; the Shared arm of `TryDescribeGitAsync` is
skipped when the hook produced a header; `Committed`/`Warning` events);
`server/Application/Services/AgentFilesService.cs` (`GetEditedPathsAsync`);
`server/Application/Services/DelegationReportFormatter.cs` (D-4 brief line; no header change
needed beyond the values);
`server/Bundles/` untouched.
Tests: in `AgentTaskReplyIntegrationTests.cs` — a Shared ClaudeCode task with seeded `Write`
transcript rows commits exactly its paths and the note reads `git=committed:`; a residual path
is reported and left; `-NoCommit` skips with the new header; project off skips; global off skips;
task `Always` beats project off; ReadOnly/Blocked/Failed/Commit-role never commit; a dirty
`.gitignore` yields `commit refused` when no child can be spawned; the existing
`a_shared_report_naming_an_uncommitted_path_warns_the_caller` is rewritten to run with the policy
off (its behaviour is now the off arm) and a sibling asserts the on arm.

**S4 — tier 2: the Commit child, the endpoint, the script, the audit.**
`server/Application/Services/AgentTaskService.cs` (`CreateCommitTaskAsync`, pin routing);
`server/Application/Services/AgentTaskReplyService.cs` (spawn from the hook; child-settle audit;
`CommitBaselineSha`);
`server/Api/Endpoints/AgentTaskEndpoints.cs` (`POST /{id}/commit`, task-token caller via
`ResolveCallerAsync`, 409 codes `ignored_path_staged`, `ignore_rules_changed`,
`nothing_to_commit`, `repository_busy`, `commit_failed`; refused for a task whose
`CommitOnSettle == Never` only when it is not itself a Commit child); DTOs in `AgentTaskDtos.cs`;
new `scripts/task-commit.ps1` (`-Paths`, `-MessageFile`, ASCII-only);
`server/Application/Services/DelegationReportFormatter.cs` (Commit-child goal builder is in
`AgentTaskService`, formatter unchanged);
`server/Domain/Enums/AgentIncidentKind.cs` (`DelegateCommitAudit`).
Tests: `AgentTaskReplyIntegrationTests.cs` — a Shared Codex task (no transcript edits) with a
dirty tree spawns a Commit child whose kind/level follow a seeded `Required` Commit pin and whose
`RoutingPinId` is set when the pin lists 2+; role policy with a `Warning` when no pin service;
caps → header text; the child's settle audit warns on an ignored path in its commits and on a
commit without the trailer. New `AgentTaskCommitEndpointTests.cs` (WebApplicationFactory or the
existing endpoint harness): 200 with sha; each 409; 401 without the token.

**S5 — gate the Worktree sweep.**
`server/Application/Services/DelegationWorktreeService.cs` (`TryMergeBackAsync` calls
`GatedCommitService.CommitAsync(worktree, pathspec: null, ...)`; refusal → `LeftForHuman` with
the paths); `tests/Antiphon.Tests/TestHelpers/MockGitService.cs` and the worktree tests that
relied on `CommitAllChangesAsync` being called (`DelegationWorktreeTests.cs`) adjust to the
service; `IGitService.CommitAllChangesAsync` stays for other callers.
Tests: a worktree with a force-added ignored file is left for review naming it; the clean case
still merges.

**S6 — docs.**
`docs/orchestration-loop.md` (helpers table: what `Commit` now is; a "Commit on settle" paragraph
under the delegate section); `.claude/skills/antiphon-delegate/SKILL.md` (`-NoCommit`, the
policy, "Workers default to shared" paragraph); `docs/antiphon-api.md` (create request field,
`POST /{id}/commit`, projects PUT field); `docs/session-runtime-invariants.md` only if the
settle ordering paragraph exists there (Code checks); `AGENTS.md` one line under Cards and
tracker: "settlement commits a Shared task's own footprint through a gated primitive; `-NoCommit`
is the only opt-out an instruction can carry".

S1 and S2 are independent; S3 needs both; S4 needs S3; S5 needs S2; S6 last.

## Acceptance cases for TestDesign

1. Shared/ClaudeCode/Custom task, policy on, writes two files via `Write`, reports: both
   committed, sha in header, tree clean for those paths, nothing pushed.
2. Same, with a third dirty file the task never touched: committed two, header names one left as
   found, that file still dirty.
3. Shared/Codex task, dirty tree, no transcript edits, report names no path: Commit child spawned,
   Codex/Low under the seeded pin, `RoutingPinId` set, parent header names the child.
4. Report names a path and transcript is empty: footprint from the report, tier 1.
5. `-NoCommit` task with a dirty tree: no commit, no child, header `(no-commit)`, brief carried
   the do-not-commit line and not `SharedWriteCommitLine`.
6. Project `CommitOnSettle=false`, task inherits: no commit, today's warning text.
7. Project off, task `Always`: committed.
8. Task `Agent`: no tier 1; child spawned even though the footprint was attributable.
9. Global off, project null: no commit.
10. Delegate deleted `.gitignore`: `IgnoreRulesChanged`, nothing staged, no commit; child spawned
    with the reason; a `Warning` names the file.
11. Force-added ignored file modified: `IgnoredPathStaged` naming path and `source:line`.
12. Tracked-then-ignored file modified: refused (case 2), other paths in the footprint still
    committed? — No: one refusal refuses the whole commit (all-or-nothing keeps the boundary
    honest); assert the whole footprint stays uncommitted and the child is spawned.
13. Orchestrator had `foo.txt` staged before the task ran: after tier 1, `foo.txt` is still staged
    and not in the commit.
14. Pre-commit hook fails: `CommitFailed`, index staged, child spawned with stderr head in the
    brief.
15. Lease busy: `RepositoryBusy`, child spawned.
16. Blocked, Failed, ReadOnly, Commit-role, Mutation, SourceLanding tasks: hook never runs.
17. Commit child settles having committed an ignored path outside the gate: `Warning`, incident,
    parent line `REVERT commit`.
18. Commit child settles with a non-gated commit: `Warning` naming it.
19. `POST /{id}/commit` with the task token: 200 + sha; each 409 code; 401 without.
20. Worktree sweep with a force-added ignored file: `LeftForHuman` naming it; clean worktree
    still `Merged`.
21. Create with goal "DO NOT commit" and no field: warning line; with `-NoCommit`: no warning.
22. Migration is additive; existing rows resolve to inherit; `project.ps1 set` round-trips
    `On`/`Off`/`Inherit`; PUT with null leaves the value.
23. Task/Commit caps reached: header `(commit task not spawned: run at task cap)`, warning kept.
24. Follow-up (`-OnAgent`) inherits the prior task's `Never` unless overridden.

## Human decisions

H-1 Global default **on** (D-3). Flip to off before landing if any project must never see a
local server-made commit; then `project.ps1 set slides -CommitOnSettle On` and the same for
Antiphon are the two writes that keep the card's acceptance true.

H-2 Whether `Always`/`Agent` deserve `delegate.ps1` switches now. Default: API-only.

H-3 Order against CARD-0458: this card's S1 ships the `project.ps1 set` verb and modal row;
0458 extends them. If 0458's Code is dispatched first, reverse the ownership in its brief.

H-4 Blocked/Failed tasks stay uncommitted (D-2). Say so if partial work from a Failed task
should be swept instead.

Next: TestDesign (separate stage) writes the `## Verification design` section against the 24 cases above; Code gate stays closed until then.
