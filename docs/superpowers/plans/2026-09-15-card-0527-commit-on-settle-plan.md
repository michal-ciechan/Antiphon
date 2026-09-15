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

Next: Code, against the `## Verification design` below (S1 and S2 first, then S3, S4, S5, S6); ordinary Review before land; Mutation executes the PC rows after land.

## Verification design

Stage: TestDesign, task `2139f2f3`, against plan commit `7e464050` (identical to `e4bc7ae5` on this
branch). Read-only against code; no fix was built. Every test below names the class and method Code
writes; the `C527_` prefix on new `AgentTaskReplyIntegrationTests` methods is what the verify filter
keys on. Statuses, codes and header strings are the plan's (D-12) unless the Inspection row says
the codebase forces a change.

### Inspection

- `tests/Antiphon.Tests/TestHelpers/ScratchGitRepo.cs` (real git, `GitInAsync(dir, args)`, `CommitFileAsync`, `GitReadAsync`; `Dispose` strips read-only attributes) | every gate case runs on it; nested/deleted/untracked `.gitignore` boundaries -> V-4, V-5; force-add -> V-6; tracked-then-ignored -> V-3.
- `tests/Antiphon.Tests/TestHelpers/ControlledGitWorkspaceService.cs` (subclass seam: `TryGetChangesAsync`/`TryGetRecentCommitsAsync` are `virtual`; `RunAsync` at `GitWorkspaceService.cs:711` is **private**) | the no-push spy needs a seam: Code makes `RunAsync` `protected internal virtual` and adds `tests/Antiphon.Tests/TestHelpers/RecordingGitWorkspaceService.cs` (records every `args[0]` verb in `Verbs`, exposes `Func<string[], Task>? BeforeRun`, delegates to base). Missing setup, recorded here -> V-10, V-11, PC-6.
- `server/Application/Services/DelegationWorktreeService.cs:445-476` (`TryMergeBackAsync` already holds `IRepositoryMutationLease` when it reaches the sweep) | a `GatedCommitService.CommitAsync` that re-acquires the lease returns `repository_busy` on every Worktree merge. Code adds a held-lease overload `CommitAsync(repo, pathspec, message, trailers, RepositoryLease held, ct)`; the existing `DelegationWorktreeTests.a_clean_change_lands_on_the_target_and_the_worktree_is_removed` is the guard -> R-14. Not a PC (liveness, not safety).
- `server/Infrastructure/Git/RepositoryMutationLease.cs` (a `FileShare.None` stream on `<common>/antiphon/landing.lock`; `new RepositoryMutationLease(new LandingGit())` is parameterless, `DelegationTestServices.CreateGitGraph:103-104`) | "lease busy" is simulated by holding a lease from the same provider across the call -> V-9, V-22, V-31. `GatedCommitServiceTests` constructs the service directly: `new GatedCommitService(spy, new RepositoryMutationLease(new LandingGit()), NullLogger<GatedCommitService>.Instance)`; no `ServiceCollection`, so `DelegationHarnessCensusTests` does not apply.
- `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs` (`[Category("Integration")] [NotInParallel]`, shared `TestDbFixture` schema; `TestScopeFactory(worktreeRoot, supervision, delegation)` wires `AddDelegationWorktreeGraph`, `AgentTaskService`, `AgentFilesService`, no `RoutingPinService`, no `SaveChangesInterceptor`; `SeedDispatchedTaskAsync(dir, parentSessionId, configure)` seeds `Workspace = Shared`, `DispatchedAt = UtcNow`; `SeedEntryAsync` sets `ToolName`/`ToolUseId` but never `ToolInput`; `NewDeliveryFactory` + `AttachTerminal` + `Queue(factory).FlushSessionAsync` + `AssertParentReceivedNoteAsync` are the CARD-0417 V-5 receipt machinery; `Phone_note_waits_for_a_busy_parent_and_lands_on_its_turn_end` is the busy-recipient shape; `a_specialist_role_failed_token_still_fails_the_task` shows `ReportToken(id, "failed")` with `closingVerdict: false`) | Missing setup: (a) `SeedFileEditAsync(sessionId, toolName, absolutePath, at)` writing `ToolInput = {"file_path": "<abs>"}` and `CreatedAt = at`; (b) `TestScopeFactory` gains `routingPins: bool`, `gitSpy: RecordingGitWorkspaceService?` (passed as `AddDelegationWorktreeGraph(..., workspaceGit: spy)`) and `saveInterceptor: SaveChangesInterceptor?` (`o.UseNpgsql(...).AddInterceptors(...)`); (c) `t.DispatchedAt = DateTime.UtcNow.AddMinutes(-1)` in `configure` so edit rows seeded at `UtcNow` are strictly after dispatch (equal timestamps are the CARD-0046 trap at `:1039`). Boundaries: edit at `DispatchedAt - 1s` -> R-6; edit at `DispatchedAt + 1s` -> V-12.
- `tests/Antiphon.Tests/Application/RoutingPinServiceTests.cs:15-22` (stage-wide pin index is unique on ROLE alone; `SeedCardAsync(db, identifier)` is `internal static`) and `RoutingPinCandidateCreateTests.cs:140-160` (multi-candidate `PutRoutingPinRequest(... Candidates: [...])`), `RoutingPinCandidateDispatchTests.cs:24-50` (`SeedQueuedPinTaskAsync` + `CreateDispatcher(schema)` re-walk shape) | the reply class shares the assembly schema, so its Commit pin is **card-scoped** (`Card: "CARD-0527T"` on a `SeedCardAsync` card bound as `t.CardId`), cleared in `finally` -> V-13; the dispatch re-walk for a Commit child uses the isolated-schema dispatch harness -> V-15.
- `server/Application/Services/AgentTaskService.cs:522-585` (`ResolveAsync` + `RoutingCandidates.Compose` + `Walked` -> `RoutingPinId`; `_routingPins` optional ctor arg; `CreateMergeTaskAsync:2311-2403` row shape, `RawTokens[id] = token`) | V-13/V-14 assert the Commit child against this shape; role-policy fallback when `_routingPins` is null -> V-14.
- `server/Api/Endpoints/AgentTaskEndpoints.cs:247-256` (`ResolveCallerAsync` returns an EMPTY `Caller` when the header is absent; `AuthenticateAsync` throws `ForbiddenException`; `ForbiddenException` is 403 at `server/Application/Exceptions/ForbiddenException.cs:9`; **no 401 exists anywhere under `server/Api` or `server/Application/Exceptions`**) | Plan case 19's "401 without" is corrected to **403 `delegation_token_required`** (the endpoint throws `ForbiddenException` when `caller.Task is null`). The Never-non-child refusal (S4) is also `ForbiddenException` 403, code `commit_on_settle_never`. -> V-38, V-39, PC-24, PC-25.
- `tests/Antiphon.Tests/Application/AgentPinnedInstructionEndpointTests.cs:18-21, 306-330` (`[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerClass)]`, token minted as `AgentTaskService.HashToken(token)` into the row) and `AgentTaskLandContractEndpointTests.cs` (`LandContractWebAppFactory.ApplyTestOverrides` removes hosted services) | `AgentTaskCommitEndpointTests` uses a new `CommitEndpointWebAppFactory : AntiphonWebAppFactory` whose `ApplyTestOverrides` replaces the `GitWorkspaceService` singleton with `RecordingGitWorkspaceService` -> V-33..V-40.
- `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs` + `DelegateScriptKindTests.cs` + `TestHelpers/DelegateCreateStubApi.cs` (real pwsh, `LastBody`, `RequestCount`) and `tests/Antiphon.Tests/Scripts/RoutingPinScriptTests.cs:285-330` (per-path stub, `LastMethod`, ASCII-only test at `:230`) | `-NoCommit` -> V-41/R-10; `project.ps1 set` needs a two-request stub `ProjectApiStub` (answers any GET under `/api/projects` with one canned project, records the PUT body as `LastPutBody`) -> V-42, V-43; ASCII -> V-46. No test references `scripts/project.ps1` today (grep), so the `set` verb tests are new.
- `tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs:586-607, 752-800, 995-1047` (`NewTask(repoPath, mergeTarget)`, `CreateService(repo)` via `CreateGitGraph`, `index.lock` failure control at `:773`) | S5 -> V-47, R-14; `MockGitService.CommitAllChangesAsync` (`:75-79`) is used only by `CardReviewServiceIntegrationTests:180` (card review PR path), which S5 does not touch: no adjustment needed there.
- `tests/Antiphon.Tests/Application/DelegationUnitTests.cs:509-535` (`BuildBrief(task, Settings)` contains/does-not-contain `SharedWriteCommitLine`; `[Category("Unit")]`) | D-4 brief line -> V-44, R-11, PC-29.
- `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs:635-662, 1186-1216, 1737-1757` (422 via `Should.ThrowAsync<ValidationException>` + `ex.Errors.ShouldContainKey`; follow-up via `FollowUpOnTask = Short(prior.Id)`; `CreateService(db)`) | -> V-32, V-48..V-52.
- `tests/Antiphon.Tests/AgentTui/AgentTuiModelArgumentMigrationTests.cs:16-23` (references `Antiphon.Server.Migrations`; isolated schema applies every migration) | additive-migration check reads `new AddCommitOnSettlePolicy().UpOperations` -> V-53.
- `client/src/features/settings/ProjectConfig.test.tsx:47-100` (msw `http.put` captures the body; "submits configured visibility only when the operator changes the select" is the exact shape) | -> V-54, R-12.
- `server/Application/Services/AgentFilesService.cs:566-600` (`Write`/`Edit`/`NotebookEdit` ToolCall rows, `file_path` or `notebook_path`, `CreatedAt > since`) | notebook_path boundary -> V-12 arm 2.
- `docs/testing-and-build.md:150-200` (method-scoped PC cycles, batch only across different files/methods, refresh timestamps after restore) and `tests/Antiphon.Tests/slow-tests-allowlist.txt` (5 s tripwire; `DelegationWorktreeTests` listed, `AgentTaskReplyIntegrationTests` not) | Cost section; new process-spawning classes carry `[ParallelLimiter<ProcessSpawnLimit>]` (`GatedCommitServiceTests`, `AgentTaskCommitEndpointTests`, `Scripts/CommitOnSettleScriptTests`); none is expected past 5 s per test, so no allowlist entry.
- Excluded boundaries: `.git/info/exclude` and the global excludes file (plan D-5 residual, trusted as found): a test would have to mutate the developer's global git config; excluded. Renames (`R` status records with `OldPath`) inside a footprint: `git add -A -- <candidates>` with both paths in the pathspec handles them; covered incidentally by V-2 (an `Edit` on a moved file is two dirty records); no dedicated case.

### Delivery inventory

**Path A (changed): the settled task's note carries `git=` and reaches the parent session.**
Producer: `AgentTaskReplyService.SettleAsync` -> new `TryCommitOnSettleAsync` (runs git BEFORE any DB write) -> `PersistDeliverThenReleaseAsync` (one `SaveChanges`: task row, `Committed`/`Warning` events, the `SessionQueuedMessages` row, `Origin = Delegation`). Destination: `ParentSessionId`'s terminal via `SessionMessageQueueService`; receipt is a complete `UserPrompt` transcript entry whose body is the note. Persistence boundary: the git commit is outside the DB transaction; it exists the moment `git commit` exits 0, before the save. Durable identity: the commit trailer `antiphon-task=<task id>` (D-5 step 7) joins the git side to the row; the queued note's task correlation (`AssertParentReceivedNoteAsync`) joins the row to the transcript. Recovery: when the save fails after the commit, `OnTurnEndAsync` re-enters on the next sweep with a clean footprint; `TryCommitOnSettleAsync` MUST first look for a commit carrying `antiphon-task=<id>` (`GitWorkspaceService.ListCommitsByGrepAsync`, existing) and, when found, render `committed:<sha7>` and record the `Committed` event instead of `landed`. This is the only addition to the plan the design needs, and it is small (V-30, PC-30). Observable receipt: V-1 (eligible parent, real queue flush, UserPrompt contains `git=committed:`), V-29 (busy parent: note Pending until the parent's TurnEnd, then delivered once), V-30 (save failure after the commit: no second commit, header still `committed:`).

**Path B (new): the Commit child.** Producer: `AgentTaskService.CreateCommitTaskAsync` called inside the parent's settle, child row + `Created` event added to the same scoped `AppDbContext` and saved by the parent's `PersistDeliverThenReleaseAsync` (atomic with the parent's settlement, `:1439-1443` precedent). Destination: `AgentTaskDispatcher` (Queued row, D3 hold), then the child's own settlement note to the same `ParentSessionId`. Durable identity: `child.ParentTaskId == settled.Id` and `child.CommitBaselineSha`. Recovery: a failed parent save persists neither the parent settlement nor the child; the re-settle recreates both (V-30 asserts exactly one child after recovery). Enqueue failure at the child's own note is the existing child path (`a_finished_merge_delegate_unblocks_its_conflicted_parent` shape); V-24 asserts the child's audit lines reach the parent transcript through the real queue. Busy recipient for the child note: covered by V-29's mechanism (same queue, same WhenIdle rule); not duplicated.

**Path C (new, synchronous): `POST /api/agent-tasks/{id}/commit`.** Request/response over HTTP; there is no queue. Substitute: the 200 body's `sha` plus `git rev-parse HEAD` equality (V-33). What it cannot prove: that the Commit child actually calls it. That is V-45 (`task-commit.ps1` posts to it) plus V-24 (the audit flags a commit made around it).

**Declared substitutes.** `SessionQueuedMessages` rows and `Committed` events are never accepted as delivery; every V that claims "the caller reads" ends in a UserPrompt assertion or is marked header-only and paired with a receipt test on the same header family (V-1 covers `committed:`, V-13 covers `-> commit task`, V-5 header-only cases share V-1's transport).

### Proves it works now

`GatedCommitServiceTests` = `tests/Antiphon.Tests/Application/GatedCommitServiceTests.cs`, `[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>]`, one `ScratchGitRepo("c527-gate")` per test with `.gitignore` = `_private/` + `*.secret`, committed `.gitignore` and `tracked.txt`, untracked `_private/verbatim.txt`; `Gate()` builds the service on a `RecordingGitWorkspaceService` spy. `Reply` = `AgentTaskReplyIntegrationTests`. `Endpoint` = `tests/Antiphon.Tests/Application/AgentTaskCommitEndpointTests.cs` (`[NotInParallel] [ClassDataSource<CommitEndpointWebAppFactory>(Shared = SharedType.PerClass)] [ParallelLimiter<ProcessSpawnLimit>]`). `Scripts` = `tests/Antiphon.Tests/Scripts/CommitOnSettleScriptTests.cs` (`[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>]`). `Policy` = `tests/Antiphon.Tests/Application/CommitOnSettlePolicyTests.cs` (`[Category("Integration")]`, `AgentTaskServiceIntegrationTests` helpers copied: `CreateService(db)`, `NewRequest`, `ManualCaller`). `Resolver` = `tests/Antiphon.Tests/Application/CommitOnSettlePolicyResolverTests.cs` (`[Category("Unit")]`).

- V-1: acceptance 1, tier 1 commits the transcript footprint and the parent RECEIVES `git=committed:` | integration (real git + real queue) | `Reply.C527_tier1_commits_the_transcript_footprint_and_delivers_committed_to_the_parent`: Shared/ClaudeCode/`Role = Custom` task, `NewDeliveryFactory()` + `AttachTerminal`, two files written on disk + `SeedFileEditAsync(sessionId, "Write", <abs>)` each, report "Wrote a.md and b.md.", settle, `Queue(factory).FlushSessionAsync(parent)` | `git log -1 --format=%B` contains `antiphon-task: <id>` and `antiphon-commit: gated`; `git diff-tree --no-commit-id --name-only -r HEAD` == {`a.md`,`b.md`}; `git status --porcelain` empty; `Committed` event detail contains the sha7 and both paths; `AssertParentReceivedNoteAsync` prompt text contains `git=committed:<sha7> (2 files)`; spy `Verbs` contains `commit` and not `push`.
- V-2: acceptance 2, residual dirty path left and named | integration | `Reply.C527_residual_dirty_path_outside_the_footprint_is_left_and_named`: as V-1 plus untracked `other.md` never touched | commit lists exactly two paths; `git status --porcelain` == `?? other.md`; `note.NoteHeader` contains `committed:<sha7> (2 files); 1 other dirty path(s) left as found`; no `Warning` event.
- V-3: investigation case 2 at the primitive | integration | `GatedCommitServiceTests.Case2_tracked_then_ignored_file_is_refused_IgnoredPathStaged`: commit `tracked.txt`, then commit a `.gitignore` line `tracked.txt`, then modify `tracked.txt` and add `new.txt`; `CommitAsync(repo, ["tracked.txt","new.txt"], ...)` | `Outcome == IgnoredPathStaged`; `Refusals` single entry path `tracked.txt` with rule `.gitignore:3:tracked.txt`; `git diff --cached --name-only` empty; HEAD unchanged.
- V-4: investigation case 3 | integration | `GatedCommitServiceTests.Case3_deleted_gitignore_is_refused_IgnoreRulesChanged_and_nothing_is_staged`: `File.Delete(.gitignore)`, add `new.txt`; pathspec null (whole) and again pathspec [`new.txt`] | both `Outcome == IgnoreRulesChanged`, `Refusals` names `.gitignore`; spy `Verbs` contains no `add`; index empty; HEAD unchanged.
- V-5: nested and untracked ignore files are G1 too | integration | `GatedCommitServiceTests.Case3b_modified_nested_or_new_gitignore_is_refused` with `[Arguments("docs/.gitignore", modified)]`, `[Arguments("sub/deep/.gitignore", untracked)]`, `[Arguments(".gitignore", modified)]` | `Outcome == IgnoreRulesChanged` naming the file; no `add` verb.
- V-6: investigation case 4 | integration | `GatedCommitServiceTests.Case4_force_added_ignored_file_is_refused_naming_path_and_rule`: `git add -f a.secret`, commit it, modify it, add `new.txt`; pathspec [`a.secret`,`new.txt`] | `Outcome == IgnoredPathStaged`; refusal path `a.secret`, rule `.gitignore:2:*.secret`; HEAD unchanged; `new.txt` still untracked (all-or-nothing).
- V-7: investigation case 1 | integration | `GatedCommitServiceTests.Case1_new_file_beside_ignored_untracked_commits_only_the_new_file`: untracked `_private/more.txt` + `new.txt`; pathspec null | `Outcome == Committed`; `Files == ["new.txt"]`; `_private/more.txt` still untracked and absent from `git ls-files`.
- V-8: acceptance 13 at the primitive | integration | `GatedCommitServiceTests.Scoped_commit_leaves_a_foreign_staged_path_staged_and_out_of_the_commit`: `git add foo.txt` first, pathspec [`bar.txt`] | `git diff --cached --name-only` == `foo.txt` after; `diff-tree HEAD` == `bar.txt`.
- V-9: lease busy | integration | `GatedCommitServiceTests.Held_lease_is_RepositoryBusy_and_nothing_is_staged`: `await using var held = await lease.TryAcquireAsync(repo.Path)` then `CommitAsync` | `Outcome == RepositoryBusy`; spy saw only `rev-parse`/`status` verbs, no `add`.
- V-10: trailers and message | integration | `GatedCommitServiceTests.Trailers_name_the_task_and_the_gate` | `git log -1 --format=%B` has the subject line first, a blank line, the body, then `antiphon: true`, `antiphon-task: <id>`, `antiphon-commit: gated` (git's `--trailer` renders `key: value`).
- V-11: D-8 at the primitive | integration | `GatedCommitServiceTests.Nothing_is_ever_pushed`: repo with a bare origin (`ScratchGitRepo.AddBareOriginAsync()` helper: `git init --bare`, `remote add origin`, `push -u origin master`), commit through the gate | spy `Verbs` contains `commit` and not `push`; `git rev-parse origin/master` unchanged.
- V-12: footprint sources and the since-bound | integration | `Reply.C527_report_named_path_is_footprint_when_the_transcript_is_empty` (acceptance 4: no ToolCall rows, report names `docs/x.md`, dirty `docs/x.md`) and arm 2 `Reply.C527_notebook_path_edits_count_as_footprint` (`NotebookEdit` with `notebook_path`) | committed exactly the named path; `committed:` header.
- V-13: acceptance 3, Codex task, no attribution, child routed by the pin | integration | `Reply.C527_unattributable_dirty_tree_spawns_a_commit_child_routed_by_the_card_pin`: `TestScopeFactory(routingPins: true)`, card via `RoutingPinServiceTests.SeedCardAsync(db, "CARD-0527T")`, `t.CardId`, `t.AgentKind = Codex`, card pin `Role Commit, Required, Human, Candidates [Codex/Low, ClaudeCode/Medium]`; dirty `x.md`; report names nothing; `NewDeliveryFactory` + flush | HEAD unchanged; child row: `ParentTaskId == task.Id`, `Role == Commit`, `Kind == Worker`, `AgentKind == Codex`, `ModelLevel == Low`, `RoutingPinId == pin.Id`, `Workspace == Shared`, `WorkingDirectory == repo.Path`, `Ephemeral`, `MaxAttempts == 2`, `CommitOnSettle == Never`, `CommitBaselineSha == HEAD`, `Title == "Commit: Seeded delegate"`, `Goal` contains `x.md` and `unattributable`; child `Created` event detail `Spawned by the server to commit 1 dirty path(s) left by task <short> (unattributable).`; parent's UserPrompt contains `git=uncommitted:1 -> commit task <childShort>`.
- V-14: role policy when no pin service | integration | `Reply.C527_commit_child_falls_back_to_role_policy_with_a_warning_when_no_pin_service`: default factory (no `RoutingPinService`) | child `AgentKind == ClaudeCode`, `ModelLevel == Medium`, `RoutingPinId == null`; a `Warning` event on the child naming the pin service.
- V-15: D-7b re-walk on a model hold | integration | `RoutingPinCandidateDispatchTests.C527_queued_commit_child_rewalks_to_the_next_pin_candidate_at_dispatch`: `SeedStageListAsync` variant for `Role Commit` (Codex/Low then ClaudeCode/Medium), hold on the Codex alias, `SeedQueuedPinTaskAsync` with `Role = Commit`, `RoutingPinId = pin.Id`, `TickAsync` | `Status == Dispatched`, `AgentKind == ClaudeCode`, `ModelLevel == Medium`, `Rerouted` event contains `Commit stage pin`.
- V-16: acceptance 5 | integration | `Reply.C527_NoCommit_task_leaves_the_tree_with_the_no_commit_header`: `t.CommitOnSettle = Never`, dirty tree, report names the path | HEAD unchanged; no child; `note.NoteHeader` contains `git=uncommitted:1 (no-commit)`; body warning `1 file(s) left uncommitted as requested (-NoCommit).`; body does not contain `Commit before building on it`.
- V-17: acceptance 6 | integration | `Reply.C527_project_off_inherited_skips_with_todays_warning`: seeded `Project { CommitOnSettle = false }`, `t.ProjectId`, `t.CommitOnSettle = null` | HEAD unchanged; no child; header `uncommitted:1 (commit-on-settle off)`; body contains `Commit before building on it.`
- V-18: acceptance 7 | integration | `Reply.C527_task_Always_beats_project_off` | `committed:` header; `Committed` event.
- V-19: acceptance 8 | integration | `Reply.C527_Agent_policy_skips_tier1_and_spawns_the_child`: attributable footprint (transcript `Write`) but `t.CommitOnSettle = Agent` | HEAD unchanged; no `Committed` event; child exists with reason `agent-policy`; header `-> commit task`.
- V-20: acceptance 9 | integration | `Reply.C527_global_off_with_project_null_skips`: factory `DelegationSettings { CommitOnSettle = false }` | HEAD unchanged; header `(commit-on-settle off)`.
- V-21: acceptance 10 | integration | `Reply.C527_deleted_gitignore_refuses_spawns_the_child_and_warns` | HEAD unchanged; index empty; child reason `ignore-rules-changed`; `Warning` event detail names `.gitignore`; child `Goal` contains `.gitignore`.
- V-22: acceptance 15 | integration | `Reply.C527_held_lease_is_RepositoryBusy_and_spawns_the_child`: hold via `factory.ServiceProvider.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(repo.Path)` | child reason `repository-busy`; HEAD unchanged.
- V-23: acceptance 14 | integration | `Reply.C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr`: `ScratchGitRepo.InstallFailingPreCommitHookAsync("gate says no")` (writes `.git/hooks/pre-commit` = `#!/bin/sh`, `echo "gate says no" >&2`, `exit 1`) | HEAD unchanged; `git diff --cached --name-only` == footprint (index left staged); child `Goal` contains `gate says no`; `note.NoteHeader` does not contain `committed:`; `Warning` event contains `CommitFailed`.
- V-24: acceptance 17 + 18 + D-8 audit, parent receives the lines | integration | `Reply.C527_commit_child_audit_flags_an_ignored_path_committed_outside_the_gate` (child seeded `Role = Commit`, `ParentTaskId`, `CommitBaselineSha = HEAD`; then `git add -f a.secret` and `git commit -m "oops"` without trailers; settle the child with `NewDeliveryFactory`) | `Warning` event contains `REVERT commit <sha7>` and `a.secret`; `AgentIncidents` row `Kind == DelegateCommitAudit` for the child's session; parent UserPrompt contains `REVERT commit <sha7>: it contains ignored path(s) a.secret`. Sibling `Reply.C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` (clean file, no trailer) | `Warning` contains `commit <sha7> was made outside the gate`; no incident; no `REVERT`. Sibling `Reply.C527_commit_child_audit_flags_upstream_movement` (bare origin, child pushes) | `Warning` contains `pushed` and the new `origin/master` sha7.
- V-25: acceptance 23 | integration | `Reply.C527_task_cap_leaves_the_header_and_warning`: factory `DelegationSettings { MaxTasksPerRoot = 1 }`, unattributable dirty tree | no child; header `uncommitted:1 (commit task not spawned: run at task cap)`; body contains `Commit before building on it.`
- V-26: D-12 row 6 | integration | `Reply.C527_gate_refusal_with_no_child_possible_renders_commit_refused`: cap 1 + deleted `.gitignore` | header `commit refused: ignore rules changed (.gitignore)`; body warning names `.gitignore`.
- V-27: acceptance 12 | integration | `Reply.C527_tracked_then_ignored_path_refuses_the_whole_footprint`: footprint {`tracked.txt`,`new.txt`} | HEAD unchanged; `git status --porcelain` lists both; child spawned with reason `ignored-path-staged`.
- V-28: acceptance 13 end to end | integration | `Reply.C527_foreign_staged_path_survives_tier1_untouched` | after settle `git diff --cached --name-only` == `foo.txt`; commit lists only the footprint.
- V-29: delivery, busy recipient | integration (real queue) | `Reply.C527_busy_parent_receives_the_committed_note_on_its_turn_end`: parent mid-turn (`AssistantText` after its last `UserPrompt`, no `TurnEnd`), settle, `FlushSessionAsync` | `terminal.Inputs` empty; queued row `Pending`; then seed parent `TurnEnd` + `Queue.OnTurnEndAsync(parent)`; `AssertParentReceivedNoteAsync` prompt contains `git=committed:`; `SubmittedBodies` has exactly one item.
- V-30: delivery, save failure after the commit | integration | `Reply.C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit`: `TestScopeFactory(saveInterceptor: new ThrowOnceSaveInterceptor(onSaving: true))`; first `OnTurnEndAsync` throws after the git commit; second `OnTurnEndAsync` on the same session | `git rev-list --count HEAD` grew by exactly 1 across both calls; `git log --grep antiphon-task=<id>` has one hit; `Committed` event exactly one; note header `committed:<sha7>`; task `Succeeded`.
- V-31: endpoint lease busy | HTTP | `Endpoint.Commit_under_a_held_lease_is_409_repository_busy` | 409, code `repository_busy`.
- V-32: D-3 request validation | integration | `Policy.Create_with_an_unknown_commitOnSettle_is_422` (`"Sometimes"`) | `ValidationException`, `Errors` key `CommitOnSettle`; and `Policy.Create_persists_each_commitOnSettle_value` with `[Arguments("Never")] [Arguments("Always")] [Arguments("Agent")]` | stored enum equals.
- V-33: acceptance 19 happy path | HTTP | `Endpoint.Commit_with_the_task_token_returns_200_and_the_sha`: Commit-child row (`Role Commit`, `CommitOnSettle Never`, `TokenHash = HashToken(token)`, `RepoPath = repo.Path`, `Workspace Shared`), header `X-Antiphon-Task-Token`, body `{ paths: ["x.md"], message: "task 1234abcd: x" }` | 200; `sha` == `git rev-parse HEAD`; `files == ["x.md"]`; commit body has the three trailers; spy `Verbs` no `push`.
- V-34: 409 `ignored_path_staged` | HTTP | `Endpoint.Commit_of_a_force_added_ignored_path_is_409_ignored_path_staged` | 409 + code; body `refusals[0].path == "a.secret"`, `rule` ends with `*.secret`.
- V-35: 409 `ignore_rules_changed` | HTTP | `Endpoint.Commit_with_a_dirty_gitignore_is_409_ignore_rules_changed` | 409 + code; HEAD unchanged.
- V-36: 409 `nothing_to_commit` | HTTP | `Endpoint.Commit_of_a_clean_tree_is_409_nothing_to_commit` | 409 + code.
- V-37: 409 `commit_failed` | HTTP | `Endpoint.Commit_with_a_failing_hook_is_409_commit_failed` | 409 + code; body `stderr` contains `gate says no`.
- V-38: no token | HTTP | `Endpoint.Commit_without_a_token_is_403` | 403, code `delegation_token_required`; HEAD unchanged.
- V-39: Never non-child | HTTP | `Endpoint.Commit_by_a_Never_task_that_is_not_a_commit_child_is_403` (`Role Docs`, `CommitOnSettle Never`) | 403, code `commit_on_settle_never`; sibling `Endpoint.Commit_by_a_commit_child_with_Never_is_allowed` | 200.
- V-40: endpoint never pushes | HTTP | `Endpoint.Commit_never_pushes` (bare origin) | spy `Verbs` no `push`; `origin/master` unchanged.
- V-41: `-NoCommit` | script | `Scripts.NoCommit_posts_commitOnSettle_Never` via `DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role","Docs","-Goal","x","-NoCommit")` on `DelegateCreateStubApi` | exit 0; `LastBody.commitOnSettle == "Never"`.
- V-42: acceptance 22, script round-trip | script | `Scripts.Project_set_CommitOnSettle_puts_the_value` with `[Arguments("On","On")] [Arguments("Off","Off")] [Arguments("Inherit","Inherit")]`: `pwsh -File scripts/project.ps1 set antiphon -CommitOnSettle <v>` against `ProjectApiStub` | exit 0; `LastPutBody.commitOnSettle == expected`; `LastMethod == "PUT"`.
- V-43: PUT carries the GET fields | script | `Scripts.Project_set_PUT_carries_the_GET_fields_unchanged` | `LastPutBody.name`, `gitRepositoryUrl`, `baseBranch` equal the canned GET; `defaultLaunchEnv` absent from the PUT (null leaves it unchanged).
- V-44: D-4 brief | unit | `DelegationUnitTests.a_never_commit_brief_carries_the_do_not_commit_line_and_not_the_commit_line` with `[Arguments(Plan)] [Arguments(Docs)] [Arguments(Code)] [Arguments(Custom)]`, `Workspace Shared`, `CommitOnSettle Never` | contains `Do NOT commit or push. Leave your changes in the working tree for the caller to review, and name every file you changed in your report.`; does not contain `SharedWriteCommitLine`.
- V-45: `task-commit.ps1` | script | `Scripts.Task_commit_script_posts_paths_and_message_to_the_commit_endpoint` (stub answers 200 `{sha, files}`) | exit 0; `LastPath` ends `/api/agent-tasks/<id>/commit`; body `paths` array and `message` from `-MessageFile`; header `X-Antiphon-Task-Token` present. Sibling `Scripts.Task_commit_script_reports_a_409_refusal_verbatim_and_exits_nonzero` (stub 409 `ignored_path_staged`) | exit != 0; output contains `ignored_path_staged` and the path.
- V-46: ASCII | unit-shaped script test | `Scripts.Commit_on_settle_scripts_are_ascii_only` over `scripts/task-commit.ps1`, `scripts/project.ps1`, `scripts/delegate.ps1` | no byte > 127.
- V-47: acceptance 20 | integration | `DelegationWorktreeTests.a_worktree_with_a_force_added_ignored_file_is_left_for_review_naming_it`: in the worktree `git add -f a.secret` then dirty it | `Outcome.Result == LeftForHuman`; `Detail` contains `a.secret` and `*.secret`; `Directory.Exists(task.WorktreePath)`; `feat/parent` unmoved.
- V-48: acceptance 24 | integration | `Policy.Follow_up_inherits_the_prior_tasks_Never_unless_overridden` (prior `CommitOnSettle = Never`; request without the field -> `Never`; request `"Always"` -> `Always`).
- V-49: acceptance 21 | integration | `Policy.Do_not_commit_prose_only_warns_when_the_field_is_absent` with `[Arguments("DO NOT commit anything")] [Arguments("please don't commit")] [Arguments("run it without committing")]` | `created.Warning` contains `pass -NoCommit`; stored `CommitOnSettle == null`; sibling `Policy.Do_not_commit_prose_with_NoCommit_does_not_warn` | no such warning; sibling `Policy.A_goal_that_merely_says_commit_does_not_warn`.
- V-50: D-3 resolution matrix | unit | `Resolver.Resolve_task_then_project_then_global` with `[Arguments]` over task {null, Never, Always, Agent} x project {null, true, false} x global {true, false} | expected `Effective` per D-3 (Never -> `Off`; Always -> `Tier1`; Agent -> `AgentOnly`; null -> project ?? global mapped to `Tier1`/`Off`).
- V-51: acceptance 22, PUT semantics | integration | `Policy.Project_PUT_null_leaves_On_Inherit_clears_Off_sets`: `ProjectService.UpdateAsync` with `CommitOnSettle = "On"`, then `null`, then `"Inherit"`, then `"Off"`, then `"Sideways"` | true, true, null, false, then `ValidationException` key `CommitOnSettle`; `ProjectDto.CommitOnSettle` and `EffectiveCommitOnSettle` reflect each step with the global default true.
- V-52: children are created with Never | integration | `Policy.Merge_and_commit_children_are_created_with_Never` (`CreateMergeTaskAsync` via a seeded conflicted task; `CreateCommitTaskAsync` via a seeded settled task) | both `CommitOnSettle == Never`; `CommitBaselineSha` set on the Commit child only.
- V-53: migration additive | unit | `tests/Antiphon.Tests/Migrations/CommitOnSettleMigrationTests.AddCommitOnSettlePolicy_is_additive` | `new AddCommitOnSettlePolicy().UpOperations` are all `AddColumnOperation`, `IsNullable`, on `Projects.CommitOnSettle`, `AgentTasks.CommitOnSettle`, `AgentTasks.CommitBaselineSha`; sibling `Existing_rows_resolve_to_inherit` (isolated schema, raw `INSERT` of a task without the column, read back `CommitOnSettle == null`, resolver -> global).
- V-54: settings modal | client (vitest) | `client/src/features/settings/ProjectConfig.test.tsx` `it('submits commitOnSettle only when the operator changes the select')` | PUT body `commitOnSettle: 'Off'` after choosing Off; the untouched save's PUT has no `commitOnSettle` key; the select shows `Inherit (on)` from `effectiveCommitOnSettle`.
- V-55: S6 docs | unit | `tests/Antiphon.Tests/Application/CommitOnSettleDocumentationTests.Docs_name_the_flag_the_endpoint_and_the_policy` | `AGENTS.md` contains `-NoCommit`; `.claude/skills/antiphon-delegate/SKILL.md` contains `-NoCommit` and `Delegation:CommitOnSettle`; `docs/antiphon-api.md` contains `POST   /api/agent-tasks/{id}/commit` and `commitOnSettle`; `docs/orchestration-loop.md` contains `Commit on settle`.

### Guards the regression

- R-1: the existing Shared warning is now the OFF arm | `Reply.a_shared_report_naming_an_uncommitted_path_warns_the_caller` rewritten with `TestScopeFactory(delegation: new DelegationSettings { CommitOnSettle = false })`; decisive: `note.NoteHeader.ShouldContain("git=uncommitted:1 (commit-on-settle off)")` and the `still uncommitted` Warning event still present. Sibling `Reply.a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on` (same seed, default settings): `NoteHeader.ShouldContain("git=committed:")`, no `still uncommitted` event.
- R-2: clean tree unchanged | `Reply.a_shared_report_whose_claimed_paths_are_clean_reports_git_landed` (existing, unchanged) stays green with the policy on; decisive: `git=landed`, no `Committed` event, no child.
- R-3: the merge conflict path is untouched | `Reply.a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate` (existing): the Merge child is created with `ModelLevel High` and `CommitOnSettle Never`, no Commit child.
- R-4: acceptance 16, ineligible tasks never run the hook | `Reply.C527_ineligible_tasks_never_run_the_hook(Ineligible c)` with `enum Ineligible { Blocked, Failed, ReadOnly, CommitRole, MutationRole, SourceLanding }`: Blocked via `ReportToken(id, "blocked")`, Failed via `ReportToken(id, "failed")` (`closingVerdict: false`), ReadOnly via `t.Workspace`, roles via `t.Role`, SourceLanding via `t.SourceLandingOperationId = Guid.NewGuid()`; all with a dirty attributable tree; decisive: `git rev-parse HEAD` unchanged, spy `Verbs` contains no `add`/`commit`, no child row, no `Committed` event; Blocked/Failed keep today's `still uncommitted` warning when the report names the path.
- R-5: `MergeBackAsync` + Worktree tasks still go through the sweep, not tier 1 | `DelegationWorktreeTests.a_still_registered_dirty_worktree_is_still_swept_and_merged` (existing) green; decisive: `Merged` and the swept file on `feat/parent`.
- R-6: warm-pool predecessor edits are invisible | `Reply.C527_edits_before_dispatch_are_not_in_the_footprint`: a `Write` ToolCall with `CreatedAt = DispatchedAt - 1s` on the same session for the only dirty path; decisive: HEAD unchanged, child spawned with reason `unattributable`.
- R-7: the dirty-count trigger is whole-tree, not the report's 20-cap | `Reply.C527_more_than_twenty_dirty_paths_are_all_attributed_from_the_transcript`: 25 files written via transcript, report names none; decisive: commit lists 25 paths; header `(25 files)`.
- R-8: index reset after a late refusal | `GatedCommitServiceTests.Whole_tree_recheck_catches_a_file_force_added_mid_run_and_resets_the_index`: spy `BeforeRun = args => args[0]=="add" ? GitInAsync(repo, "add","-f","late.secret") : done`; decisive: `Outcome == IgnoredPathStaged` naming `late.secret`; `git diff --cached --name-only` empty afterwards; HEAD unchanged.
- R-9: scoped commit with a deletion | `GatedCommitServiceTests.Scoped_commit_with_a_deletion_records_the_deletion`: delete `tracked.txt`, pathspec [`tracked.txt`]; decisive: `diff-tree HEAD` shows `tracked.txt` as `D`.
- R-10: omitted `-NoCommit` sends no field | `Scripts.Omitted_NoCommit_sends_no_commitOnSettle`; decisive: `LastBody.TryGetProperty("commitOnSettle", out _) == false`.
- R-11: ReadOnly brief keeps its own line | `DelegationUnitTests.a_never_commit_readonly_brief_keeps_the_read_only_line`; decisive: contains `Do NOT modify any files`, does not contain the D-4 line twice.
- R-12: untouched save never sends `commitOnSettle` | second half of V-54; decisive: `expect(put).toHaveBeenCalledWith(expect.not.objectContaining({ commitOnSettle: expect.anything() }))`.
- R-13: `Always`/`Agent` are API-only (H-2) | `Scripts.Delegate_has_no_Always_or_Agent_switch`; decisive: `delegate.ps1 -CommitAlways` and `-CommitAgent` exit non-zero at the parameter binder with `RequestCount == 0`.
- R-14: the gated Worktree sweep still merges a clean change under the held lease | `DelegationWorktreeTests.a_clean_change_lands_on_the_target_and_the_worktree_is_removed` (existing); decisive: `Merged`, `feat/parent:feature.md == "the work"`.
- R-15: the Worktree sweep's commit-all failure control still fails | `DelegationWorktreeTests.a_commit_all_failure_on_a_live_worktree_still_fails` (existing, `index.lock`); decisive: `Failed`, `Detail` contains `Committing the delegate's work failed`.
- R-16: `CardReviewServiceIntegrationTests` keeps `IGitService.CommitAllChangesAsync` (plan S5: "stays for other callers"); decisive: `git.Operations` contains `CommitAllChanges` at `:180`.
- R-17: contract snapshot | `tests/Antiphon.E2E/ContractSnapshotTests` regenerates `client/src/test/fixtures/contract/agent-task-detail.json`; decisive: the fixture carries `commitOnSettle: null` and `commitBaselineSha: null` on the legacy row.

### Guard inventory

- G-1: D-5 step 2, G1: any `.gitignore` (any depth; modified, deleted or untracked) refuses before staging | PC-1
- G-2: D-5 step 4, G2: `check-ignore` exit 0 over the candidates refuses (force-added, case 4) | PC-2
- G-3: D-5 step 4, `--no-index`: a tracked-then-ignored path (case 2) is still a hit | PC-3
- G-4: D-5 step 6, whole-mode re-check of the staged list catches a file staged between status and add | PC-4
- G-5: D-5 step 7, `commit --only -- <candidates>` leaves foreign staged paths out and staged | PC-5
- G-6: D-8, no push call in `GatedCommitService` | PC-6
- G-7: D-2, `Status == Succeeded` only (Blocked/Failed never committed, H-4) | PC-7
- G-8: D-2, `Workspace == Shared` only (ReadOnly never) | PC-8
- G-9: D-2, role `Commit` excluded (no recursion) | PC-9
- G-10: D-2, role `Mutation` excluded (snapshot rule) | PC-10
- G-11: D-2, `SourceLandingOperationId` null | PC-11
- G-12: D-3, task `Never` skips both tiers | PC-12
- G-13: D-3, project off is inherited | PC-13
- G-14: D-3, global off | PC-14
- G-15: D-3, `Agent` skips tier 1 | PC-15
- G-16: D-6, transcript edits bounded by `CreatedAt > DispatchedAt` | PC-16
- G-17: D-6, tier 1 commits footprint intersected with dirty only; residual left | PC-17
- G-18: D-6, empty footprint goes to tier 2, never a whole-tree sweep in Shared | PC-18
- G-19: acceptance 12, one refusal refuses the whole footprint | PC-19
- G-20: D-7a, Commit child created with `CommitOnSettle = Never` | PC-20
- G-21: D-7c, child audit runs G2 over every commit in `CommitBaselineSha..HEAD` | PC-21
- G-22: D-7c, child audit flags a commit without `antiphon-commit=gated` | PC-22
- G-23: D-8, child audit flags upstream movement | PC-23
- G-24: S4, `POST /{id}/commit` requires a task-token caller | PC-24
- G-25: S4, endpoint refuses a `Never` caller that is not a Commit child | PC-25
- G-26: S5, the Worktree sweep goes through the gate | PC-26
- G-27: D-5 step 6, index reset (`git reset -q -- <staged>`) after a post-stage refusal | PC-27
- G-28: D-12, `committed:` header only from a real sha (CommitFailed renders no `committed:`) | PC-28
- G-29: D-4, `Never` brief carries the do-not-commit line and not `SharedWriteCommitLine` | PC-29
- G-30: Delivery inventory Path A recovery: re-settle after a failed save finds the trailer and does not commit twice | PC-30
- G-31: D-3 request validation, unknown `commitOnSettle` is 422 before any row exists | PC-31

Guards = 31, mapped = 31, missing = 0, duplicate PC mappings = 0. `Always` beats project off (V-18) and the create-time advisory (V-49) are behaviour, not safety: their failure is a missing commit or a missing warning, both loud in the header; they carry V/R only.

### Positive controls

Each PC: apply the compiling defect, build once into `bin-pc/`, run ONLY the named method with `--treenode-filter "/*/*/<Class>/<Method>"`, observe the named assertion red, restore the exact source (refresh the timestamp), rebuild, run the same filter green. Code implements the tests and runs V/R; ordinary Review judges them before land; Mutation executes the PCs after land from the SourceLanding snapshot (docs/testing-and-build.md, CARD-0451).

- PC-1: break G-1 by narrowing the G1 predicate in `GatedCommitService.CommitAsync` to `c.Path == ".gitignore" && c.Status == Modified`; expect `GatedCommitServiceTests.Case3_deleted_gitignore_is_refused_IgnoreRulesChanged_and_nothing_is_staged` red at `result.Outcome.ShouldBe(GatedCommitOutcome.IgnoreRulesChanged)` (and `Case3b_...` red on its nested/untracked arms).
- PC-2: break G-2 by inverting the exit-code test (`ignored = exitCode != 0`) in the G2 call; expect `Case4_force_added_ignored_file_is_refused_naming_path_and_rule` red at `Outcome.ShouldBe(IgnoredPathStaged)`.
- PC-3: break G-3 by removing `"--no-index"` from the `check-ignore` argument list; expect `Case2_tracked_then_ignored_file_is_refused_IgnoredPathStaged` red at `Outcome.ShouldBe(IgnoredPathStaged)`.
- PC-4: break G-4 by skipping the step-6 re-check in whole mode (`if (pathspec is null) goto commit`); expect `Whole_tree_recheck_catches_a_file_force_added_mid_run_and_resets_the_index` red at `Outcome.ShouldBe(IgnoredPathStaged)`.
- PC-5: break G-5 by replacing `commit --only -- <candidates>` with plain `commit` in scoped mode; expect `Scoped_commit_leaves_a_foreign_staged_path_staged_and_out_of_the_commit` red at `committedPaths.ShouldNotContain("foo.txt")`.
- PC-6: break G-6 by appending `await _git.RunAsync(repo, ct, "push")` after a successful commit in `GatedCommitService`; expect `Nothing_is_ever_pushed` red at `spy.Verbs.ShouldNotContain("push")`.
- PC-7: break G-7 in `CommitOnSettleEligibility.IsEligible` by changing `Status == Succeeded` to `Status != Canceled`; expect `Reply.C527_ineligible_tasks_never_run_the_hook(Ineligible.Blocked)` red at `headAfter.ShouldBe(headBefore)`.
- PC-8: break G-8 by changing `Workspace == Shared` to `Workspace != Worktree`; expect `...(Ineligible.ReadOnly)` red at `headAfter.ShouldBe(headBefore)`.
- PC-9: break G-9 by removing `AgentTaskRole.Commit` from the excluded set; expect `...(Ineligible.CommitRole)` red at `headAfter.ShouldBe(headBefore)`.
- PC-10: break G-10 by removing `AgentTaskRole.Mutation` from the excluded set; expect `...(Ineligible.MutationRole)` red at `headAfter.ShouldBe(headBefore)`.
- PC-11: break G-11 by deleting the `SourceLandingOperationId is null` clause; expect `...(Ineligible.SourceLanding)` red at `headAfter.ShouldBe(headBefore)`.
- PC-12: break G-12 in `CommitOnSettlePolicyResolver.Resolve` by mapping `Never` to `Tier1`; expect `Reply.C527_NoCommit_task_leaves_the_tree_with_the_no_commit_header` red at `headAfter.ShouldBe(headBefore)`.
- PC-13: break G-13 by returning the global value whenever the task value is null (skip the project); expect `Reply.C527_project_off_inherited_skips_with_todays_warning` red at `headAfter.ShouldBe(headBefore)`.
- PC-14: break G-14 by replacing the settings read with `true`; expect `Reply.C527_global_off_with_project_null_skips` red at `headAfter.ShouldBe(headBefore)`.
- PC-15: break G-15 by mapping `Agent` to `Tier1`; expect `Reply.C527_Agent_policy_skips_tier1_and_spawns_the_child` red at `committedEvent.ShouldBeNull()`.
- PC-16: break G-16 in `AgentFilesService.GetEditedPathsAsync` by dropping the `CreatedAt > since` filter; expect `Reply.C527_edits_before_dispatch_are_not_in_the_footprint` red at `headAfter.ShouldBe(headBefore)`.
- PC-17: break G-17 in `AgentTaskReplyService.TryCommitOnSettleAsync` by passing the whole dirty set as the pathspec; expect `Reply.C527_residual_dirty_path_outside_the_footprint_is_left_and_named` red at `status.ShouldContain("?? other.md")`.
- PC-18: break G-18 by passing `pathspec: null` when the footprint is empty; expect `Reply.C527_unattributable_dirty_tree_spawns_a_commit_child_routed_by_the_card_pin` red at `headAfter.ShouldBe(headBefore)`.
- PC-19: break G-19 in `GatedCommitService` by removing the G2 hits from the candidates and continuing; expect `Reply.C527_tracked_then_ignored_path_refuses_the_whole_footprint` red at `status.ShouldContain("new.txt")`.
- PC-20: break G-20 in `AgentTaskService.CreateCommitTaskAsync` by leaving `CommitOnSettle` null; expect `Policy.Merge_and_commit_children_are_created_with_Never` red at `commitChild.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never)`.
- PC-21: break G-21 in `AgentTaskReplyService.AuditCommitChildAsync` by skipping the `check-ignore` pass; expect `Reply.C527_commit_child_audit_flags_an_ignored_path_committed_outside_the_gate` red at `warning.Detail.ShouldContain("REVERT commit")`.
- PC-22: break G-22 by skipping the trailer check; expect `Reply.C527_commit_child_audit_flags_a_commit_without_the_gate_trailer` red at `warning.Detail.ShouldContain("was made outside the gate")`.
- PC-23: break G-23 by skipping the `@{u}` comparison; expect `Reply.C527_commit_child_audit_flags_upstream_movement` red at `warning.Detail.ShouldContain("pushed")`.
- PC-24: break G-24 in `AgentTaskEndpoints` by guarding the refusal with `if (false && caller.Task is null)`; expect `Endpoint.Commit_without_a_token_is_403` red at `response.StatusCode.ShouldBe(HttpStatusCode.Forbidden)` (a 500 or 200 arrives).
- PC-25: break G-25 by deleting the `CommitOnSettle == Never && Role != Commit` refusal; expect `Endpoint.Commit_by_a_Never_task_that_is_not_a_commit_child_is_403` red at `StatusCode.ShouldBe(Forbidden)`.
- PC-26: break G-26 in `DelegationWorktreeService.TryMergeBackAsync` by calling `_git.CommitAllChangesAsync` instead of the gate; expect `DelegationWorktreeTests.a_worktree_with_a_force_added_ignored_file_is_left_for_review_naming_it` red at `outcome.Result.ShouldBe(MergeResult.LeftForHuman)`.
- PC-27: break G-27 by removing the `git reset -q -- <staged>` after a post-stage refusal; expect `GatedCommitServiceTests.Whole_tree_recheck_catches_a_file_force_added_mid_run_and_resets_the_index` red at `stagedAfter.ShouldBeEmpty()`.
- PC-28: break G-28 in `TryCommitOnSettleAsync` by rendering `committed:{sha7}` from `result.Sha ?? ""` regardless of `Outcome`; expect `Reply.C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr` red at `note.NoteHeader.ShouldNotContain("committed:")`.
- PC-29: break G-29 in `DelegationReportFormatter.BuildBrief` by emitting `SharedWriteCommitLine` for Shared write roles regardless of `CommitOnSettle`; expect `DelegationUnitTests.a_never_commit_brief_carries_the_do_not_commit_line_and_not_the_commit_line(AgentTaskRole.Code)` red at `ShouldNotContain(SharedWriteCommitLine)`.
- PC-30: break G-30 by skipping the `antiphon-task=<id>` lookup at the top of `TryCommitOnSettleAsync`; expect `Reply.C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit` red at `note.NoteHeader.ShouldContain("committed:")` (renders `landed`).
- PC-31: break G-31 in `AgentTaskService.CreateAsync` by parsing an unknown value as `null` instead of throwing; expect `Policy.Create_with_an_unknown_commitOnSettle_is_422` red at `Should.ThrowAsync<ValidationException>`.

Batching for Mutation (different files/methods only): serial depth is set by `GatedCommitService.CommitAsync` (PC-1..6, 19, 27 = 8 cycles). Each cycle may also carry one of `CommitOnSettleEligibility` (PC-7..11), one of `CommitOnSettlePolicyResolver` (PC-12..15), one of `TryCommitOnSettleAsync` (PC-17, 18, 28, 30), one of `AuditCommitChildAsync` (PC-21..23), one of `AgentTaskEndpoints` (PC-24, 25), and the singletons PC-16, PC-20, PC-26, PC-29, PC-31; eight cycles cover all 31.

### Out of scope

- Real Codex/Grok delegates running `task-commit.ps1` against a live server (needs a provider session; the endpoint, the script's request shape and the child audit are each proven separately: V-33..V-40, V-45, V-24).
- `.git/info/exclude` and the global excludes file (D-5 residual; a test would edit the developer's global git config).
- D3 hold ordering of the Commit child (D-10: unchanged; `SharedWriterLeaseProjection` has its own tests).
- Dispatcher quota/availability 409 at child create (D-7b: none at create by design; the model-hold re-walk is V-15).
- Backfilling historical rows, automatic reverts, push automation (plan "Out of scope").
- Settings > Projects modal visual layout beyond the select's PUT semantics (V-54, R-12).
- CARD-0458's `DefaultWorkerWorkspace` field (D-9: same shape, other card).

### Cost

Ordinary V/R floor (Code), estimated:

| Step | Command | Minutes |
|---|---|---|
| Build once | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c527/ --nologo` | 3.0 |
| Unit lane (resolver, migration, docs, brief) | `--treenode-filter "/*/*/(CommitOnSettlePolicyResolverTests*)|(CommitOnSettleMigrationTests*)|(CommitOnSettleDocumentationTests*)|(DelegationUnitTests*)/*"` | 0.3 |
| Gate primitive | `--treenode-filter "/*/*/GatedCommitServiceTests/*"` (16 tests, real git) | 0.7 |
| Reply hook | `--treenode-filter "/*/*/AgentTaskReplyIntegrationTests/C527_*"` plus the two rewritten `a_shared_report_*` methods (30 tests, 3 with a real queue flush) | 3.5 |
| Policy + routing | `--treenode-filter "/*/*/(CommitOnSettlePolicyTests*)|(RoutingPinCandidateDispatchTests*)/*"` | 1.0 |
| Endpoint | `--treenode-filter "/*/*/AgentTaskCommitEndpointTests/*"` (host boot + 10) | 1.0 |
| Worktree | `--treenode-filter "/*/*/DelegationWorktreeTests/(a_worktree_with_a_force_added*)|(a_clean_change_lands*)|(a_still_registered*)|(a_commit_all_failure*)"` | 0.5 |
| Scripts | `--treenode-filter "/*/*/(CommitOnSettleScriptTests*)|(DelegateScriptKindTests*)/*"` (pwsh spawns) | 0.7 |
| Client | `pwsh -File scripts/test-client.ps1 ProjectConfig.test` | 0.6 |
| Contract fixture | E2E `ContractSnapshotTests` regeneration (client/dist rebuild excluded; run once) | 2.0 |
| **V/R floor** | | **13.3 (estimated)** |

PC floor (Mutation), estimated: 8 cycles x (mutate 1.0 + build 3.0 + red runs 1.5 + restore/refresh 0.5 + build 3.0 + green runs 1.5) = 8 x 10.5 = **84 minutes (estimated)**; each red run is the method-scoped filter for that cycle's PCs (up to 7 invocations against `--no-build` output).

Total verification floor = 3.0 (setup/build) + 10.3 (V/R after build) + 84 (PC) = **97 minutes, estimated**. Savings: the `C527_` prefix filter runs 30 methods instead of the whole `AgentTaskReplyIntegrationTests` class (about 12 minutes saved per pass); batching 31 PCs into 8 builds instead of 31 saves 23 x 6 = 138 minutes; method-scoped PC runs against `--no-build` output avoid 31 rebuilds of the test host. Nothing is zero-cost; the E2E fixture regeneration is the one step that cannot be filtered below 2 minutes.

Before handoff: fixture and test bodies read (listed under Inspection); guards = 31, mapped = 31, missing = 0, duplicate PC mappings = 0; every PC is a compiling defect against a named method with a named red assertion; Cost is numeric and labelled estimated.

Corrections to the plan recorded by this design (Code follows these): acceptance 19's "401" is 403 (`ForbiddenException`); `GatedCommitService.CommitAsync` needs a held-lease overload for the Worktree sweep; `TryCommitOnSettleAsync` starts with the `antiphon-task=<id>` trailer lookup so a re-settle after a failed save is idempotent; `GitWorkspaceService.RunAsync` becomes `protected internal virtual` as the spy seam.
