# CARD-0527 Commit on task settle: where the dirty tree is detected, and where a chained commit could hang

Investigate, 2026-09-15, task `62040dbd`, worktree `card-task-62040dbd` at Antiphon `9b914298`.
Read-only: code, the delegation database (`antiphon-postgres`), the live `slides` checkout
(`C:\src\slides` at `9e6fb64`), and a scratch git repository for the gitignore experiment. No fix
was designed or built; one fix idea is confined to the last section.

## Verdict

**Confirmed, from code and stored rows.** The condition the card describes is produced by one
site, `AgentTaskReplyService.TryDescribeGitAsync`, whose Shared-workspace branch is
warning-only by design (CARD-0261 S1). The server already commits a *Worktree* task's dirty
tree in-process at settle, with no agent and no ignore gate; a *Shared* task gets only the
warning, because CARD-0227 made shared git state unattributable. There is no structured
record of a "do not commit" instruction anywhere (it lives only in free goal text, and a text
match false-positives on this very task), and no per-project delegation policy row to hang an
opt-out on. The closest existing "chain a task at settle" mechanism is the Merge-role child that
`CreateMergeTaskAsync` spawns after a rebase conflict; it does not consult routing pins, so a
copy of it would land Commit work on `RolePolicy["Commit"]` (ClaudeCode/Medium) rather than the
live stage-wide pin (Codex/Low first).

## 1. Where settlement detects and reports the dirty tree (question 1)

Site: `server/Application/Services/AgentTaskReplyService.cs:2898-2931`
(`TryDescribeGitAsync`, Shared/ReadOnly arm at `:2905-2931`).

Callers:

- `SettleAsync` (`:611`) calls it at `:745`, after `MergeBackAsync` (`:727-743`) and before the
  note is delivered by `PersistDeliverThenReleaseAsync` (`:794-801`). The returned warning is
  appended to `callerWarning` (`:746-747`) and the header bit is passed as `git:` (`:801`).
- The UnmarkedWaiting block path calls it at `:2855` (header only, warning discarded).

What it measures:

1. `ExtractReportedRepositoryPaths` (`:2988`) regex-extracts paths **the report itself named**
   (backtick, Windows-absolute, and relative shapes), normalises to repo-relative, and stops at
   **20** paths.
2. `GitWorkspaceService.GetDirtyPathsAsync`
   (`server/Application/Services/GitWorkspaceService.cs:446-483`) runs
   `git --no-optional-locks status --porcelain -z --untracked-files=all -- <paths>` scoped to
   those names and returns the dirty/untracked subset.
3. Any dirty path: a `Warning` event
   `Report names N file(s) still uncommitted in the shared checkout: ...` (`:2924-2925`), the
   completion header bit `git=uncommitted:N` (`:2930`), and the caller line *"The report names N
   file(s) that are still uncommitted in the shared checkout — the work has not landed. Commit
   before building on it."* (`:2926-2928`). Clean: `git=landed`. No named paths: `unattributable`.

The header bit and warning line are rendered by `DelegationReportFormatter`
(`server/Application/Services/DelegationReportFormatter.cs:592`, `:605-606`). Tests pinning the
current behaviour: `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:3095-3146`
(`a_shared_report_naming_an_uncommitted_path_warns_the_caller`,
`a_shared_report_whose_claimed_paths_are_clean_reports_git_landed`).

Properties that matter for anything that would act on this signal:

- **It is report-derived, not a tree status.** The count is the dirty subset of the paths the
  delegate wrote in prose, capped at 20. On the `slides` board task `c90dbfa7` reported exactly
  20 while the true dirty set at the catch-up was "roughly 61 paths" (task `634e1345`'s goal).
  A whole-tree view exists in the same service (`GetChangesAsync` `:62`,
  `GetWorkingTreeCountsAsync` `:411`) but settlement does not use it.
- **It also runs for ReadOnly tasks** (the arm is `Shared or ReadOnly`); one of the 73 live
  warnings below came from a ReadOnly Grok task.
- **Warning only, never blocks or mutates** (CARD-0261 S1 contract, restated in the plan doc
  `docs/superpowers/plans/2026-08-31-card-0261-shared-plan-commit-skip-investigation.md:107-118`).
- **It runs before delivery.** `SettleAsync` still holds the settlement open when the git facts
  are known, and `PersistDeliverThenReleaseAsync` takes both a `workspaceNote` and a `warning`,
  so a settlement already has a slot for a line such as
  `merge conflict → task X is resolving it` (`:1439-1443`).

### Live evidence (delegation database, 2026-09-15)

Warning events whose detail starts `Report names % still uncommitted`:

| Project | Events | First | Last |
|---|---|---|---|
| markdown-package | 27 | 2026-09-06 | 2026-09-12 |
| Antiphon | 26 | 2026-09-01 | 2026-09-09 |
| slides | 13 | 2026-09-12 | 2026-09-14 |
| gym-stat | 3 | 2026-09-01 | 2026-09-02 |
| school-revision | 3 | 2026-09-02 | 2026-09-03 |
| (no project) | 1 | 2026-09-04 | 2026-09-04 |

Per day since 2026-09-01: 4, 9, 1, 1, 2, 3, 14, 4, 12, 0, 8, 3, 9, 3 (73 in 14 days). By
workspace and kind: Shared/ClaudeCode 40, Shared/Codex 27, Shared/Grok 5, ReadOnly/Grok 1.
Detection therefore fires on every kind, not only Codex (CARD-0261's finding was about who
*skips* the commit; this is about who gets *told*).

The thirteen `slides` rows (Role 0 = Custom, 6 = Docs; Kind 1 = ClaudeCode, 2 = Codex; Level
0 = Frontier, 1 = High, 2 = Medium; all Workspace 0 = Shared):

| Task | Settled (UTC) | Role | Kind/Level | Title | N | Goal says do-not-commit |
|---|---|---|---|---|---|---|
| af1ac427 | 09-12 23:21 | Docs | Claude/Medium | Finish AI Vibe Engineer folder | 2 | yes |
| 06f8a0b6 | 09-13 16:42 | Custom | Claude/High | reveal.js deck scaffold | 10 | no |
| 18104541 | 09-13 18:23 | Custom | Claude/Frontier | Maven theme variant: fable | 19 | yes |
| c90dbfa7 | 09-13 18:41 | Custom | Codex/Frontier | Maven theme variant: astra | 20 | yes |
| f85bdff8 | 09-13 18:46 | Custom | Claude/High | Capture Mike's talk notes verbatim | 1 | no |
| 7b6711a5 | 09-13 20:54 | Custom | Claude/High | Links entries for both deck variants | 19 | no |
| 0d7016c4 | 09-13 21:09 | Custom | Claude/Frontier | Promote fable to live deck with astra matrix | 2 | no |
| 4c80fa25 | 09-13 21:12 | Custom | Claude/Medium | Resolve three notes.md ambiguities | 1 | no |
| ceeeea3c | 09-13 21:25 | Custom | Claude/Medium | Resolve external API doc question | 1 | no |
| 4aa3b3a3 | 09-13 21:53 | Custom | Claude/Frontier | Stages diagram with fragment build | 19 | no |
| e8740cd1 | 09-14 07:06 | Custom | Claude/Medium | Settled copy: information to and from | 2 | no |
| 74345d9b | 09-14 07:19 | Custom | Claude/Frontier | Switch workflow diagram to two lanes | 2 | no |
| 1391ecf1 | 09-14 07:21 | Custom | Claude/Medium | Capture sub-stages entry in notes | 1 | no |

Only three of the thirteen carried a do-not-commit instruction. The other ten simply did not
commit, which matches CARD-0261's mechanism (no commit instruction in the brief, and see section
5 for why Custom-role tasks get no commit line at all).

The catch-up: task `634e1345` "Commit deck, variants and notes", Role 7 = Commit, Codex/Low,
Succeeded, created 2026-09-14 07:19:23Z. Its result: three commits on `slides` (`6ecafac`,
`37a5884`, `9aee2d2`) plus one on `links`, "nothing was pushed", audits for `_private` and
`node_modules` passed. Two tasks were still running when it was created and settled *after* it
(`74345d9b` at 07:19:40Z, `1391ecf1` at 07:21:53Z); the goal explicitly accepted "you may
capture a mid-edit state". Today `C:\src\slides` is clean at `9e6fb64`.

## 2. Worktree tasks already get an in-process commit at settle; Shared tasks get the warning

`SettleAsync:727-743` calls `MergeBackAsync` (`:1391`) for every Succeeded Worktree task, and
`DelegationWorktreeService.TryMergeBackAsync`
(`server/Application/Services/DelegationWorktreeService.cs:433-476`) runs
`_git.CommitAllChangesAsync(worktree, "task <short>: <title>")` at `:469-470` **before** it
checks whether a merge target exists (`:477`). So the server already commits a delegate's
leftover work, with a synthetic message and no agent, whenever the task ran in a worktree. The
comment there says it plainly: "a report that says 'done' with a dirty tree is normal, not an
error. Sweep it into the task branch first."

`GitService.CommitAllChangesAsync` (`server/Infrastructure/Git/GitService.cs:221-243`):
`status --porcelain` → `add -A` → `diff --cached --name-only` → `commit -m <msg> --trailer
antiphon=true`. No ignore gate. It runs under a repository mutation lease
(`IRepositoryMutationLease.TryAcquireAsync`, `server/Application/Interfaces/IRepositoryMutationLease.cs:5`,
acquired at `DelegationWorktreeService.cs:445`). The only test reference to it is
`tests/Antiphon.Tests/TestHelpers/MockGitService.cs`.

Exclusions already encoded there: Mutation role and SourceLanding snapshots return
`LeftForHuman` before any commit (`:435-436`); the CARD-0499 progress-evidence gate
(`SettleAsync:731-739`) withholds merge-back when progress is "alternate or unavailable".

The asymmetry is deliberate: CARD-0227 made a Shared task's git state unattributable, and the
Shared arm of `TryDescribeGitAsync` was added later (CARD-0261 S1) as a read-only check.

## 3. Where a chained commit would be triggered from (question 2)

Two "run another task after this one" idioms exist.

### 3a. Server-spawned child at settle: the Merge precedent

`AgentTaskService.CreateMergeTaskAsync`
(`server/Application/Services/AgentTaskService.cs:2311-2403`) is called from
`MergeBackAsync` at `AgentTaskReplyService.cs:1439-1440` when a rebase conflicts. Shape:

- A new `Queued` `AgentTask` with `ParentTaskId` = the settled task, same `RootTaskId`,
  `ParentSessionId`, `ReplyTo`, `CardId`, `ProjectId`, `RepoPath`; `Depth + 1`; `Ephemeral`;
  `Workspace = Shared` with `WorkingDirectory` = the tree to operate in; `MaxAttempts = 2`;
  `StandingAuthority` copied; `AutoContinueOnWait = false`; a `Created` event "Spawned by the
  server ...".
- Caps honoured: `MaxTasksPerRoot` and `MaxDepth` only. The doc comment says it "bypasses the
  caller checks" (open-task gate, quota gate, model hold).
- Tier: `ResolveLevel(Worker, Merge, null)` (`:2425-2435`) = `RolePolicy["Merge"]`; kind is the
  entity default `ClaudeCode`. **It does not call `RoutingPinService`.** Pins are resolved only
  in the create path (`:522-530` `ResolveAsync`, composed into kind/level at `:640-700`,
  `RoutingPinId` stored at `:923`). The dispatcher honours only a pin's `NotBefore` for a row
  without `RoutingPinId` (`AgentTaskDispatcher.cs:476-500`) and list-walks only when
  `Complexity != null || RoutingPinId != null` (`:520-526`, `:844`).
- The dispatcher picks the row up from the ordinary Queued scan (`AgentTaskDispatcher.cs:327`).
- The return string becomes the settled task's note line
  (`merge conflict → task <short> is resolving it`, `AgentTaskReplyService.cs:1441-1443`), so the
  caller learns about the child in the same note.

Consequence for a Commit chain built the same way: `RolePolicy["Commit"]` is
`AgentModelLevel.Medium` (`server/Application/Settings/DelegationSettings.cs:325`), kind
ClaudeCode, i.e. Sonnet. The live pin says otherwise:

```
routing-pin.ps1 get -Role Commit
stage-wide  Commit  Human Required  Codex/Low (gpt-5.6-luna) +2: ClaudeCode/Medium (sonnet), Grok
Operator 2026-09-11: lowest-tier work stays on Luna/Sonnet first, Grok as fallback
```

Commit-role tasks in the last 60 days (Role = 7): Codex/Low 6 tasks, mean $0.046 and 1.6 min;
ClaudeCode/Medium 1 task, $1.647 and 6.5 min. The card's "cheap" is already policy, but only
for tasks created through `CreateAsync`.

### 3b. Caller follow-up on the same agent (`-OnAgent` / `FollowUpOnTask`)

`scripts/delegate.ps1:81-84` and `:759` send `followUpOnTask`; the server resolves it at
`AgentTaskService.cs:298-330`. A *live* follow-up pins to the prior task's agent and "inherits
that agent's directory and its TIER — the model is already running; a role policy cannot change
it mid-session"; it is forced Shared. After a Shared task settles, its pool delegate is kept
warm rather than killed (`SettleAsync:757-761` comment; `ReleaseDelegateAsync:1590`). So the
follow-up idiom would run the commit on the same Frontier/High agent that just finished, in the
same context that was (in three of the slides cases) told not to commit. It is a "same delegate
again" mechanism, not a "cheap specialist" one. If the agent has retired, create degrades to a
fresh delegate with the prior task's context prefixed (`:309-323`).

### 3c. Concurrency and ordering facts that constrain either idiom

- A Queued Shared task in a repo with a running Shared writer is **Held** regardless of scope
  when `Delegation:SerialiseSharedWriters` is on (default true, `DelegationSettings.cs:834`;
  rule D3 at `server/Application/Services/SharedWriterLeaseProjection.cs:51-70`). Worktree
  writers do not hold it. The settled task no longer holds the slot once its status leaves
  Dispatched/Working.
- The completion note is built once, at `PersistDeliverThenReleaseAsync:794`; anything the
  caller should read about a chained child has to exist before that call, as the Merge spawn
  does.
- `.antiphon/` spill files are written into the task's working directory by settlement
  (`ResolveReportFileAsync:2248`, refinement spill `:560-570`). `git check-ignore` confirms
  `.antiphon/` is ignored in all five active repos checked (Antiphon, slides,
  markdown-package, gym-stat, school-revision); slides holds 13 such files, markdown-package
  534. A project without that rule would have them swept by `add -A`.

## 4. Gitignore safety: what is reusable, and what `git add -A` actually does (question 3)

Existing primitives:

| Primitive | Where | What it does |
|---|---|---|
| `CommitAllChangesAsync` | `GitService.cs:221-243` | `add -A` then commit; no gate |
| `IsIgnoredAsync` | `server/Infrastructure/Git/CardFileRepository.cs:149-155` | `check-ignore --no-index -z --stdin` over a path list |
| `EnsureIgnoredAsync` | `server/Infrastructure/Files/AgentReportStore.cs:142-176` | `ls-files` (must be untracked) + `check-ignore` per path + append to `info/exclude` + re-check |
| `PreviewIgnoreAsync` | `GitWorkspaceService.cs:516-560` | evaluates a pattern via a temp `-c core.excludesFile=`; `ls-files --cached --ignored --exclude-standard` for tracked-but-ignored |
| `ListFilesAsync` | `GitWorkspaceService.cs:493-502` | `ls-files --cached --others --exclude-standard` |
| `GuardedWorktreeRemoval.HasProtectedIgnored` | CARD-0452 | refuses worktree removal while ignored paths exist |

Scratch experiment (fresh repo; `.gitignore` = `_private/` and `*.secret`; committed
`.gitignore` and `tracked.txt`; untracked `_private/verbatim.txt`, `a.secret`):

| Case | Setup | `git add -A` stages | Detected by |
|---|---|---|---|
| 1 | new ignored file `_private/more.txt` + new `new.txt` | `new.txt` only | n/a, safe |
| 2 | `tracked.txt` added to `.gitignore` after being tracked, then modified | `.gitignore`, `new.txt`, `tracked.txt` | `ls-files --cached --ignored --exclude-standard` lists `tracked.txt` |
| 3 | delegate deleted `.gitignore` | `.gitignore` (deleted), `_private/more.txt`, `_private/verbatim.txt`, `a.secret`, `new.txt` | **not** by `check-ignore` against the working tree (the rule is gone) |
| 4 | delegate ran `git add -f a.secret` earlier | `a.secret`, `new.txt` | `git diff --cached --name-only -z \| git check-ignore --no-index -z --stdin` exits 0 and lists `a.secret` |

So `add -A` is safe in the steady state (case 1: git never adds ignored untracked files), and
the card's "irreversible" scenario needs one of: the ignore file itself edited or removed
(case 3), a prior force-add (case 4), or ignore rules added after tracking (case 2, harmless to
history but noisy). A gate that evaluates against the **working tree's** ignore files is blind
to case 3; evaluating against HEAD's rules (the `PreviewIgnoreAsync` temp-excludes technique can
take `git show HEAD:.gitignore`), or refusing when `.gitignore` / `.git/info/exclude` is in the
diff, would see it. Case 4 is caught by piping the staged list through `check-ignore`.

The `slides` rule is `.gitignore:151-152`: `/2026-09-ai-vibe-engineer/reference/_private/`,
commented "Local-only reference copies - personal reading, never committed". The manual
catch-up task's goal put the whole check on the agent ("verify it yourself before you commit,
and again after ... if anything from that directory would be staged, stop and report") and the
agent's result reports the audit; n = 1, Codex/Low.

## 5. Where "DO NOT commit" lives today (question 4)

Nowhere structured.

- `AgentTask` (`server/Domain/Entities/AgentTask.cs`) has no commit-policy column. The nearest
  fields: `Workspace` (`:139`), `DenyDirectEdits` (`:145`, orchestrator PreToolUse hook only),
  `StandingAuthority` (`:518`, free text up to 2 000 chars, CARD-0294), `AutoContinueOnWait`
  (`:524`), and the InternalDecisionPolicy grants JSON (`:17`), which is a per-path grant
  vocabulary whose eligible roles include Commit
  (`server/Application/Services/InternalDecisionPolicy.cs:41-52`).
- `CreateAgentTaskRequest` (`server/Application/Dtos/AgentTaskDtos.cs:20-70`) has no such field;
  `scripts/delegate.ps1` has no switch.
- The instruction exists only in `AgentTask.Goal`. Tasks created in the last 30 days whose goal
  matches do-not-commit wording: 59 (37 with no project, Antiphon 9, slides 5, gym-stat 4,
  school-revision 3, markdown-package 1). Wording varies: `DO NOT commit`, `Do not commit`,
  `No commit`, `no commit`, `without committing`. The match includes **this task
  (`62040dbd`)**, whose brief merely *talks about* the instruction, so a text heuristic
  false-positives on any task that discusses the rule.
- Countervailing standing instructions: every delegate's bundle says COMMIT AND PUSH EACH
  MEANINGFUL SLICE (`server/Bundles/delegate-basics.md`), and Shared **Plan/Docs/Code** briefs
  get `SharedWriteCommitLine` ("git add ... commit ... and push, before your final report",
  `DelegationReportFormatter.cs:137-138`, applied at `:212-215`). Both say *push*; the card's
  auto path must not. Custom-role tasks (ten of the thirteen slides rows) get neither a stage
  bundle nor that line.
- The only structured "never commit" today is the Mutation/SourceLanding snapshot rule
  (`DelegationReportFormatter.cs:176`, `server/Bundles/stage-mutation.md:11`), enforced in code
  by `TryMergeBackAsync:435-436`, not by reading the brief.

## 6. Per-project opt-out surfaces (question 5)

- `Project` (`server/Domain/Entities/Project.cs`) carries two per-project delegation defaults:
  `DefaultLaunchEnvJson` (`:39`) and `OrchestratorWorkspaceAcknowledgedAt` (`:47`). Nothing
  about workspace or commits. `PUT /api/projects/{id}` exists
  (`server/Api/Endpoints/ProjectEndpoints.cs:87`).
- `antiphon.areas.json` is the scope/collision map (header comment, lines 1-20): repo-committed,
  path-derived, consulted by the dispatcher for scope intersection. CARD-0458's design (D-1, on
  the card) rejected a repo-root file as the home for per-project defaults because it is
  path-derived (CARD-0115 forbids that for scope), reads the wrong file under `-Dir`, and is
  shared across machines while limits are per machine.
- CARD-0458 is in Review and **not on master**: no `DefaultWorkerWorkspace` symbol exists under
  `server/`. Its design is a nullable `Projects.DefaultWorkerWorkspace` (null = inherit) plus a
  global `Delegation:DefaultWorkerWorkspace`, edited via the project PUT and the Settings >
  Projects modal.
- Precedent for an auto-commit switch that ships OFF: `CardFileSync:AutoCommit`
  (`server/Application/Settings/CardFileSyncSettings.cs:17-25`), with `CommitSkipReason
  autocommit_disabled` when off. Global delegation booleans of the same shape:
  `SerialiseSharedWriters` (`DelegationSettings.cs:834`), `OrchestratorDenyHookEnabled`
  (`:811`), `PoolEnabled` (`:842`).

## 7. Relationship to CARD-0458 (noted, not implemented)

Uncommitted shared state blocks isolation from both ends:

- A worktree is cut from `MergeTargetRef ?? HEAD` and nothing copies working-tree state
  (CARD-0458 ground truth; slides at `a58ef2c` had `deck/`, `deck-variants/`, `notes.md`
  untracked).
- `ReadCleanTargetAsync` (`DelegationWorktreeService.cs:545-559`) requires an empty
  `status --porcelain -z --untracked-files=all` in the checkout that holds the target branch,
  so a dirty shared checkout also turns Worktree merge-backs into `LeftForHuman` (216 of 246 per
  CARD-0458's census).

## 8. Remaining uncertainties

- Whether the D3 hold (section 3c) would delay a chained commit behind other Shared writers
  long enough to lose the per-task commit boundary the card wants; it also prevents the
  mid-flight capture the manual run accepted. Design trade-off, not measurable here.
- Role coverage: the slides tasks were Custom, which today receives no commit instruction at
  all; whether the chain should key on role or on "any Shared writer" is a design choice.
- Whether an *agent* is needed at all for the commit versus the in-process sweep that Worktree
  settlement already does (section 2); the agent buys a report-derived message and judgement,
  the in-process path buys determinism and a code-enforced ignore gate.
- Cost band at the observed rate: 73 settles per fortnight at the Codex/Low mean is roughly
  $3.40 per fortnight; the single ClaudeCode/Medium sample was 35x that per task.
- The 73 warnings undercount the dirty-tree condition (report-derived, capped at 20, only
  paths the delegate named); the true rate of settles-with-dirty-tree is not in the database.

## Not done, noted

Fix idea, one line: a settle-time hook in `SettleAsync` between `MergeBackAsync` and
`PersistDeliverThenReleaseAsync` that, for Shared non-ReadOnly tasks with a whole-tree dirty
status, spawns a `CreateMergeTaskAsync`-shaped Commit child through the create path's pin
resolution, gated by a project/global switch and a structured no-commit flag on the request,
with an in-process HEAD-ignore-rules gate around `add -A`.
