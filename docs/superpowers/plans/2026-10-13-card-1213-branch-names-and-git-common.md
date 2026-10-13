# CARD-1213 plan: delegate branch names carry the card; `{{GIT-COMMON}}` fragment

Date 2026-10-13. Role Plan (task 6327ca04). Operator request, verbatim intent: "Why don't branch
names have the card as part of it? Update instructions ... or some common git instructions which is
then injected via system prompt markup such as {{GIT-COMMON}}." Deliverable: this plan only.
Paths are relative to the checkout at `0b6ce91d96f45a08eb7f78270105fdb5172c6c5d`.

## 1. Current state (verified)

### 1.1 Where the branch name is minted

| Step | Code |
|---|---|
| Identifier for every Worktree task | `DelegationWorktreeService.cs:305` `identifier = $"task-{Short(task.Id)}"` (8 lowercase hex, `DelegationReportFormatter.cs:20`). The same identifier is the worktree metadata `CardId` used for adoption on retry (`:346`, `:377`). |
| Branch and directory | `WorktreeManager.cs:18` `BranchPrefix = "feat/card-"`, `:19` `DirectoryPrefix = "card-"`, `:624` `BuildBranchName`, `:626` `BuildDirectoryName`. `CreateAsync` (`:63-75`) derives both from the identifier, so branch `feat/card-task-<8hex>` and directory `card-task-<8hex>` are one coupled pair. `:22` `CardIdPattern ^[A-Za-z0-9._-]+$` forbids `/`. |
| Stored on the task | `DelegationWorktreeService.cs:390` `task.WorktreeBranch = info.Branch`; brief prints it at `DelegationReportFormatter.cs:203`, `:215-225`. |
| Verification snapshot (SourceLanding Mutation) | `WorktreeManager.cs:140-156` compares `saved.Branch != BuildBranchName(identifier)`; `DelegationWorktreeService.cs:443` and `WorktreeRemovalEvidence.cs:57` pin `feat/card-task-<8hex>`; runner `RunnerVerificationWorkspace.cs:24,42,244` pins `^feat/card-task-[0-9a-f]{8}$`. |
| Runner mirror | Server sends `task.WorktreeBranch` in the mirror request (`RemoteWorkspaceService.cs:182,203`); mirror directory is `task-<8hex>` (`:95`; runner `RunnerWorkspaceService.cs:21` `NamePattern`). Runner admits any branch matching `^[A-Za-z0-9._/-]{1,200}$` at create (`:23`, `:128`). |

### 1.2 Every consumer that parses or pattern-matches the name

| Consumer | Site | What it does with the literal |
|---|---|---|
| Owned-branch derivation (server) | `RemoteWorkspaceService.cs:334` `OwnedBranch(taskId)`; callers `:399-402` (settlement sync refuses `BranchMismatch` unless `task.WorktreeBranch` equals it), `TaskCompletionProgressService.cs:122`, `PhoneHomeRunnerMirrorPublisher.cs:21`, `SettlementSyncDebtPolicy.cs:107`, `LocalTaskParkPublisher.cs:45` | Recomputes the branch from the task id instead of reading the stored branch. **Hard break for any new format.** |
| Runner publish | `RunnerWorkspaceService.cs:589` `request.Branch == "feat/card-task-" + name[5..]` | 409 unless branch is legacy. **Hard break; separately deployed binary.** |
| Runner park | `RunnerWorkspaceParkService.cs:230-232` `FullRef == "refs/heads/feat/card-" + name` | `park_invalid_target` unless legacy. **Hard break.** |
| Done-cleanup ownership | `CardWorktreeCleanupService.cs:151-153` `ClassifyOwnership` requires leaf `card-task-<id>` and branch `refs/heads/feat/card-task-<id>` | `ordinary_owner_required` otherwise. |
| Retirement | `TaskWorktreeRetirementService.cs:465-470` requires the same leaf and branch | `outside_scope` otherwise. |
| Health/residue scan | `WorktreeManager.cs:232-283` `ScanDelegateWorktreesAsync` filters by `TryParseDelegateTaskBranch` (`:498-519`, prefix + exactly 8 hex) and lists `git branch --list feat/card-task-*` (`:259`); residue expects directory from the parsed id (`:356-358`); `WorktreeHealthService.cs:114` joins scan rows to tasks by `ShortId`, `:265-272` `TryShortFromBranch` | New-format branches would be invisible to health and residue sweeps. |
| Dispatch adoption | `AgentTaskDispatcher.cs:5727-5730` `ownBranch = WorktreeBranch ?? "feat/card-task-<id>"` then `KeptBranchExistsAsync` | Only when `WorktreeBranch` is null (first dispatch or crash before persist). |
| Directory-only matchers (unaffected if directory stays) | `AgentTaskDispatcher.cs:8975-8990` `IsWorktreeWorkingDirectory`; `ZombieCensusClassifier.cs:42-43,380-390`; `scripts/reap-zombie-agents.ps1:593-596`; `RunnerWorkspaceInspection.cs:22`; `scripts/prune-test-data.ps1:56`; `GrokDetectors.cs:7` | Match `card-task-<8hex>` as a path leaf. |
| Cosmetic | `DelegationOpenGate.cs:185-198` `CompactStuck` strips the prefix from stuck labels. | |
| Doc/comment mentions | `WorktreeHealthShape.cs:4,15,18`, `WorktreeHealthFinding.cs:7`, `DelegateWorktreeScanEntry.cs:4`, `WorktreeResidueDtos.cs:17-18`, `WorktreeHealthHostedService.cs:11,57`, `scripts/delegate.ps1:85,367,644-647`, `docs/orchestration-loop.md:225,989,1184`, `.claude/skills/antiphon-orchestrator/SKILL.md:88` | Text only. |

Consumers that do **not** parse the name and need no change: landing (`AgentTaskLandService.cs` has 9
`WorktreeBranch` reads and zero `card-task-` literals; sibling kept-branch warnings at `:1005-1045`
take `branch` from the sibling row), dispatch-base sibling warnings (`AgentTaskDispatcher.cs:5209-5216`
use `sibling.WorktreeBranch`; `KeptBranchExistsAsync` is `git show-ref --verify`,
`DelegationWorktreeService.cs:121-127`), `IsAntiphonBranch` (`WorktreeManager.cs:906`, prefix
`feat/card-` only). The client has no non-test reference.

### 1.3 How instructions are assembled today

- Bundles are markdown files under `server/Bundles/` embedded by `Antiphon.Server.csproj:26`
  (`Presets\*.md` at `:29` are embedded but skipped by the catalog, `InstructionBundles.cs:252-254`).
  Version is the content hash (`InstructionBundles.cs:14-18`), header `[bundle:<key> v<hash8>]` (`:28`).
- Role map `InstructionBundles.ForDelegate` (`:190-219`): stage role gets `stage-<role>` then
  `delegate-basics`; Orchestrator kind gets `orchestrator` then `delegate-basics`; specialists get none.
- `InstructionBundleComposer.Compose` (`InstructionBundleComposer.cs:78-119`) dedups by key, renders
  header+text, appends style then `SystemPromptAppend` verbatim. **It performs no placeholder
  expansion.** The only substitution in the system is `ChannelPreamble.Render` (`ChannelPreamble.cs:27-38`,
  `{agentName}`/`{channels}`) applied over the whole composed text for standing agents
  (`AgentSessionLaunchComposer.cs:109-137`); delegates skip it (`AgentTaskDispatcher.cs:7043-7069`).
  `InstructionBundleTests.cs:365` pins that no bundle contains those two tokens.
- Compose callers (closed): `AgentSessionLaunchComposer.cs:109`, `AgentService.cs:147`,
  `AgentTaskDispatcher.cs:7043` and `:8054` (Grok warm reuse), `PolicyRefreshService.cs:197` (drift).
  `ComposedInstructions.StampLine` (`InstructionBundleComposer.cs:36`) drives the drift badge.
- Git rules today: `delegate-basics.md:19-24` (commit and push each slice), `:35-39` (commit before a
  long run), `:31` (evidence-diff); `stage-code.md` SOURCE line (full SHA, evidence-diff);
  `stage-review.md` "INVARIANTS: Read-only"; `stage-mutation.md` "Never commit or push from the
  snapshot"; per-task runner contract `DelegationReportFormatter.cs:216-225` (never rebase/amend/
  reset/force-push); `docs/orchestration-loop.md:1000-1004` (CARD-0779) and `:1006-1012` ("do not
  retype"). **`merge-tree` and `commit_refs` appear in no owner doc, bundle, script or skill**; they
  exist only in per-task plan and evidence files (for example
  `docs/superpowers/plans/2026-10-13-card-1209-stale-unit-assertions.md:23`), that is, brief folklore.

### 1.4 Ground truth

| Card assumes | Code does | Consequence |
|---|---|---|
| Branch is minted in "WorktreeManager / dispatch". | Identifier minted in `DelegationWorktreeService.cs:305`; `WorktreeManager.CreateAsync` derives branch and directory from it. | Change the mint in DelegationWorktreeService; give WorktreeManager an explicit branch parameter. |
| Consumers that "parse it" are landing, cleanup, reconciliation, runner sync, scripts. | Landing parses nothing. The breaking parsers are `OwnedBranch` (6 sites), runner publish/park, cleanup/retirement ownership, health/residue scan. | Closed list in §1.2; landing untouched. |
| `feat/card-1208/code-8c713d0c` is a candidate. | `CardIdPattern` forbids `/`; directory is derived from the same string; runner park pins `refs/heads/feat/card-<name>`; git refuses `feat/card-1208` beside `feat/card-1208/x`. | Hyphen-only form (D-1). |
| Git conventions are restated in each stage's role instructions. | Stage bundles carry per-stage overrides only; the generic rules live in `delegate-basics.md` and in briefs; merge-tree and commit_refs rules have no owner at all. | Fragment becomes the single owner (D-5). |
| The runner is in the same deployable. | `Antiphon.SessionRunner` is a separate container with a capability list (`PhoneHomeRuntimeAdapter.cs:18`, `SessionRunnerContracts.cs:141-153`). | Gate the new name on a runner feature (D-4). |
| A task can have several cards. | `AgentTask.CardId` is a single `Guid?` (`AgentTask.cs:176`). | No multi-card name; carrier/repair tasks bind to one card. |
| Placement is known. | `GET /api/runner-defaults` revision 2: global default `server2`; `GET /api/session-runners`: `desktop` (windows), `server2` (linux), `server2-temp`. | Checkpoints name the lane, not a host. |

## 2. Decisions

- **D-1 Name format.** `feat/card-<n>-<stage>-<8hex>`, for example `feat/card-1213-plan-6327ca04`.
  `<n>` is the digits of `Card.Identifier` (`CARD-1213` → `1213`; allocator `CardIdentifierAllocator.cs:66`),
  `<stage>` is the role name lower-cased (`investigate|plan|testdesign|code|mutation|review|docs|debug|…`),
  Orchestrator kind uses `orch`; `<8hex>` is the existing short task id, which alone keeps the name unique.
  Rejected: `feat/card-1213/plan-…` (ref-prefix collision with any future `feat/card-1213`, `CardIdPattern`,
  runner park coupling); `feat/card-1213-…` without stage (operator asked for stage/round); a round counter
  (no stable source; the task id already distinguishes rounds).
- **D-2 Unbound and special tasks.** `CardId == null`, a card whose identifier is not `CARD-\d+`, and every
  SourceLanding Mutation snapshot keep `feat/card-task-<8hex>`. Snapshots are never pushed and their
  identity checks (`WorktreeManager.cs:152`, `DelegationWorktreeService.cs:443`, runner verification) stay
  untouched. Rejected: a third form such as `feat/task-<stage>-<8hex>`.
- **D-3 Directory and mirror names do not change.** Directory stays `card-task-<8hex>`, mirror
  `task-<8hex>`, metadata `CardId` stays the identifier. Only the branch carries the card. This leaves
  every directory-only matcher in §1.2 and the runner `NamePattern` alone. `WorktreeManager.CreateAsync`
  gains an optional `branch` argument; `InferAntiphonBranchFromDirectory` (`:923-933`) stays as the
  metadata-less fallback.
- **D-4 Compatibility rule.** No branch is renamed, ever, including on retry: adoption by identifier
  (`DelegationWorktreeService.cs:346,377`) returns the existing worktree and its existing branch. Every
  consumer accepts both forms through one parser, `DelegateBranchName.TryParse`, in
  `src/Antiphon.SessionRunner.Contracts` (referenced by server `Antiphon.Server.csproj:71` and runner
  `Antiphon.SessionRunner.csproj:34`). Exact regex:
  `^(?:refs/heads/)?feat/card-(?:task|(?<card>[0-9]{1,8})-(?<stage>[a-z][a-z0-9]{0,15}))-(?<id>[0-9a-fA-F]{8})$`
  (`id` normalised to lower case). Ownership checks become "parses, and `id` equals the task's short id"
  instead of string equality. A new-format name is minted only when the task has no runner or its runner
  advertises `RunnerCapabilityFeatures.WorkspaceCardBranchV1` (new constant); otherwise legacy. Rejected:
  a data migration of `WorktreeBranch`; regex per consumer.
- **D-5 `{{GIT-COMMON}}` owner and grammar.** One file `server/Bundles/Fragments/git-common.md`, embedded
  via a new `Bundles\Fragments\*.md` item and skipped by the bundle catalog like `Presets.`. A fragment is
  `InstructionFragment(Key, Version, Text)` with content-hash version and header
  `[fragment:git-common v<hash8>]`. Placeholder grammar: `{{NAME}}`, `NAME` matching `^[A-Z][A-Z0-9-]{1,31}$`,
  recognised only inside catalog bundle text. Expansion point: `InstructionBundleComposer.Compose`, after
  `Get(key)` and before `Render()`; first occurrence in a composition expands to header plus text, later
  occurrences to `[fragment:git-common v<hash8>: see above]`. Fragment versions join `Stamps`/`StampLine`,
  so drift badge, `PolicyRefreshService` and Grok warm-reuse recompose see fragment edits for free.
  `SystemPromptAppend`, styles' semantics, briefs and `ChannelPreamble` are not touched. Rejected: a
  `git-common` bundle in `ForDelegate` (cannot be positioned before per-stage overrides, and the operator
  asked for markup); expanding briefs (pty budget, false positives on quoted `{{…}}`; see OD-4).
- **D-6 Unknown placeholder fails loudly.** `Compose` throws `InvalidOperationException` naming the bundle,
  the token and the known names; the launch fails the same way an over-budget composition does
  (`AgentTaskDispatcher.cs:7046-7050`). A fragment may not contain `{{`, `{agentName}` or `{channels}`.
  `InstructionBundleTests` pins that every shipped bundle's placeholders resolve, so the throw is a
  backstop and never reaches a live dispatch from committed files.
- **D-7 Versioning.** Content hash, exactly like bundles (`server/Bundles/README.md` "nothing to bump by
  hand"), plus a first line `GIT-COMMON contract v1.` and a pinned INVARIANTS block, mirroring the
  output-distiller precedent (README §output-distiller). Semantic changes bump the human `vN`; any edit
  changes the hash.
- **D-8 What stays per stage.** `stage-review.md` keeps "Read-only. Do not fix anything" and gains "never
  check out, rebase, commit to or push the subject branch"; `stage-mutation.md` keeps the SourceLanding
  no-commit exception; `stage-code.md` keeps checkpoint SOURCE/evidence rules; the per-task runner contract
  (`DelegationReportFormatter.cs:216-225`) keeps the concrete branch, base SHA and push command and cites
  git-common instead of restating the never-rebase sentence. Overrides may narrow git-common, never widen it.

### 2.1 The fragment (target ≤ 1,500 characters; exact prose is Code's, these rules are binding)

1. Branch: work only on the assigned branch (`feat/card-<n>-<stage>-<8hex>`, legacy `feat/card-task-<8hex>`); never create, rename or switch branches.
2. Commit each slice with its real outcome; push after every commit with `git push origin HEAD:refs/heads/<branch>`; report full 40-hex SHAs.
3. Rebase, never merge: never merge the target into the task branch; never rebase, amend, reset or force-push a pushed commit (task branches are fast-forward-only; landing rebases).
4. Tests first, then fix: commit the failing test before the commit that makes it pass.
5. A branch under Review is read-only to everyone, including its author, until Review settles.
6. Before claiming landable: `git fetch origin master` and `git merge-tree --write-tree origin/master HEAD` must report no conflicts; report the result; do not resolve by merging.
7. On `fatal: ... error in commit_refs` from a push: wait, `git fetch`, confirm the remote tip with `git ls-remote`, retry up to three times; never force.
8. No secrets in commits, logs or reports; generated evidence stays ignored; Code and Review run `scripts/check-evidence-diff.ps1` over base..HEAD.

## 3. Files to change (closed list)

**S1 name format + consumers**
- New `src/Antiphon.SessionRunner.Contracts/DelegateBranchName.cs` (Build, Legacy, TryParse, Regex);
  `SessionRunnerContracts.cs:141-153` add `WorkspaceCardBranchV1`.
- `server/Infrastructure/Git/WorktreeManager.cs` (`:63-75` branch argument; `:259` glob `feat/card-*`
  filtered by TryParse; `:498-519` delegate to TryParse); `server/Application/Interfaces/IWorktreeManager.cs:35-65`.
- `server/Application/Services/DelegationWorktreeService.cs:295-390` (mint; card identifier lookup via
  existing optional `_db`, `:35,46,59`; runner feature check via the runner registry already used at `:311-321`).
- `server/Application/Services/RemoteWorkspaceService.cs:334,399-402` (`OwnedBranch(AgentTask)` returns the
  stored branch after TryParse + tail check); callers `TaskCompletionProgressService.cs:122`,
  `PhoneHomeRunnerMirrorPublisher.cs:21`, `SettlementSyncDebtPolicy.cs:107`, `server/Infrastructure/Git/LocalTaskParkPublisher.cs:45`.
- `CardWorktreeCleanupService.cs:151-153`, `TaskWorktreeRetirementService.cs:465-470`,
  `WorktreeHealthService.cs:265-272`, `DelegationOpenGate.cs:188`, `AgentTaskDispatcher.cs:5727-5730`
  (check both candidate names when `WorktreeBranch` is null).
- Runner: `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:589`, `RunnerWorkspaceParkService.cs:230-232`,
  `PhoneHomeRuntimeAdapter.cs:18` (advertise).
- XML comments listed in §1.2; `scripts/delegate.ps1:85,367,644-647` wording.
- Tests: new `tests/Antiphon.Tests/Application/DelegateBranchNameTests.cs`; edits in
  `WorktreeManagerTests.cs:65-76`, `WorktreeHealthServiceTests.cs`, `WorktreeResidueSweepTests.cs`,
  `CardDoneWorktreeCleanupTests.cs`, `TaskWorktreeRetirementTests.cs`, `RemoteWorktreeMirrorTests.cs`,
  `RunnerSettlementSyncTests.cs`, `SettlementSyncDebtPolicyTests.cs`, `TaskCompletionProgressPolicyTests.cs`,
  `AgentTaskDispatchBaseGuardTests.cs`; runner `RunnerWorkspaceServiceTests.cs`, `WorkspaceParkPublicationTests.cs`.

**S2 placeholder mechanism + fragment**
- New `server/Bundles/Fragments/git-common.md`; `server/Antiphon.Server.csproj:26-29` embed item.
- New `server/Application/Services/InstructionFragments.cs` (catalog, `Expand`, `PlaceholderPattern`);
  `InstructionBundles.cs:238-260` skip `Fragments.`; `InstructionBundleComposer.cs:13-37,78-119`
  (fragments in `ComposedInstructions`, expansion, throw).
- `server/Bundles/delegate-basics.md:19-39` (git sentences replaced by `{{GIT-COMMON}}`; harness rules stay),
  `stage-review.md` (D-8 line), `orchestrator.md` (one sentence: delegates carry git-common; do not restate
  git rules in briefs; size pin `InstructionBundleTests.cs:631` must still pass), `server/Bundles/README.md`.
- Tests: new `tests/Antiphon.Tests/Application/InstructionFragmentTests.cs`; `InstructionBundleTests.cs:320,365,645,666`.

**S3 migrate repeated boilerplate**
- `docs/orchestration-loop.md:225,989,1000-1012,1184`; `.claude/skills/antiphon-orchestrator/SKILL.md:88`;
  `docs/session-runtime-invariants.md` (one line naming both branch forms next to the worktree invariants);
  `server/Application/Services/DelegationReportFormatter.cs:216-225` (cite git-common, keep per-task values);
  `tests/Antiphon.Tests/Application/DelegationUnitTests.cs` (runner contract assertion).

## 4. Slices

| Slice | Minutes | Content | Lands independently |
|---|---:|---|---|
| S1 | 60 | `DelegateBranchName`, server mint and consumers, runner publish/park, capability gate, tests. Commit group 1: contracts + server; group 2: runner + gate. | Yes; until the runner image is rolled out, only desktop-local tasks get new names. |
| S2 | 45 | Fragments catalog, expansion in Compose, `git-common.md`, bundle edits, tests. | Yes; independent of S1. |
| S3 | 30 | Docs, skill, formatter runner-contract sentence, README cross-links. | Yes; after S2. |

## 5. Test list (case → mutation → expected red)

| ID | Case | Mutation | Red |
|---|---|---|---|
| T1 | `Build` yields `feat/card-1213-plan-6327ca04` for role Plan, card `CARD-1213` | drop the stage segment | name assertion |
| T2 | `TryParse` accepts legacy, new, `refs/heads/` form, upper-case hex; rejects `feat/card-E03-003`, `feat/card-1213`, `feat/card-1213/plan-6327ca04`, 7-hex tail, `feat/card-1213-Plan-…` | tail `{8}` → `{7,8}`; allow `/` | reject assertions |
| T3 | `CardId == null` or non-`CARD-\d+` identifier mints legacy | always mint new with `0` | legacy assertion |
| T4 | SourceLanding snapshot mints legacy and passes `ValidateVerificationAsync` | mint new for snapshots | `ConflictException verification_creation_identity_mismatch` |
| T5 | runner without `WorkspaceCardBranchV1` mints legacy; with it, new; no runner, new | ignore the feature | legacy assertion |
| T6 | `CreateAsync(repo, "task-…", base, lease, ct, branch:)` checks out the requested branch into `card-task-<id>` | derive branch from directory | branch assertion |
| T7 | `ScanDelegateWorktreesAsync` lists a dangling new-format branch with its `ShortId` | keep `feat/card-task-*` glob | missing row |
| T8 | health `Classify` joins a new-format registration to its task | `TryShortFromBranch` legacy-only | `RegisteredNoTask` instead of healthy |
| T9 | `ClassifyOwnership` accepts new branch with matching tail, rejects foreign tail | prefix-only compare | ownership string |
| T10 | `RequireUniqueOrdinaryOwner` accepts new branch | literal compare | `outside_scope` |
| T11 | settlement sync accepts stored branch in either form, refuses a foreign tail | `OwnedBranch` legacy-only | `BranchMismatch` |
| T12 | progress `fullRef`, debt policy and park publisher follow the stored branch | derive from id | ref assertion |
| T13 | runner `PublishAsync` accepts new branch whose tail equals `name[5..]`, 409 on foreign tail | legacy compare | 409 |
| T14 | runner `ValidateLexicalTarget` accepts both forms | literal compare | `park_invalid_target` |
| T15 | runner advertises `WorkspaceCardBranchV1` | remove from list | feature assertion |
| T16 | dispatcher adopts a kept legacy branch when `WorktreeBranch` is null and the task would now mint new | check new name only | adoption assertion |
| T17 | fragments catalog holds exactly `git-common`; version is 8 hex of content | embed a stray file | key-set assertion |
| T18 | compose `[stage-code, delegate-basics]` expands `{{GIT-COMMON}}` once with header; second reference renders "see above" | expand every occurrence | count assertion |
| T19 | unknown `{{NOPE}}` throws naming bundle, token, known names (internal test seam with a synthetic bundle) | swallow and emit verbatim | no throw |
| T20 | `StampLine` contains `git-common v<hash>`; editing text changes it | omit fragments from stamps | stamp assertion |
| T21 | fragment contains no `{{`, `{agentName}`, `{channels}`; ≤ 1,500 chars | add token | pin |
| T22 | every composed delegate role text contains no `{{` | leave a token unexpanded | pin |
| T23 | composed delegate text keeps pinned phrases: "never merge", "merge-tree", "commit_refs", "push after every commit", "40-hex", "check-evidence-diff" | delete a sentence | phrase pin |
| T24 | Review composition has git-common once and the read-only override after it | reorder | index assertion |
| T25 | worst-case composition (`InstructionBundleTests.cs:666`) and TestDesign ceiling (`:713`) re-measured under budget | oversize fragment | budget throw |
| T26 | runner branch contract cites `git-common` and still prints branch, base SHA and push command | drop push command | brief assertion |

## 6. Checkpoints

Lane: Linux (`server2`) for both assemblies; no Windows-only witness. Build to isolated OutputPath with a forward slash through the slot gate.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1213-s1/` | name | `/*/*/DelegateBranchNameTests/*` | T1-T3 | all green; ≥ 8 cases | 8 | 3 | true |
| CP-2 | S1 | CP-1 | manager | `/*/*/(WorktreeManagerTests*)|(WorktreeManagerStartRefLocalOnlyTests*)/*` | T6, T7 | all green | 20 | 4 | true |
| CP-3 | S1 | CP-1 | health | `/*/*/(WorktreeHealthServiceTests*)|(WorktreeResidueSweepTests*)|(WorktreeResidueRegistrationTests*)/*` | T8 | all green | 15 | 4 | true |
| CP-4 | S1 | CP-1 | ownership | `/*/*/(CardDoneWorktreeCleanupTests*)|(TaskWorktreeRetirementTests*)|(WorktreeRetirementRaceTests*)/*` | T9, T10 | all green | 10 | 5 | true |
| CP-5 | S1 | CP-1 | sync | `/*/*/(RemoteWorktreeMirrorTests*)|(RunnerSettlementSyncTests*)|(SettlementSyncDebtPolicyTests*)|(TaskCompletionProgressPolicyTests*)/*` | T11, T12 | all green | 20 | 5 | true |
| CP-6 | S1 | CP-1 | dispatch | `/*/*/(AgentTaskDispatchBaseGuardTests*)|(WorktreeStartRefDispatchTests*)|(RemoteVerificationWorkspaceTests*)/*` | T4, T5, T16 | all green | 10 | 5 | true |
| CP-7 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1213-s1r/` | runner | `/*/*/(RunnerWorkspaceServiceTests*)|(WorkspaceParkPublicationTests*)|(RunnerWorkspaceBindingTests*)/*` | T13-T15 | all green | 15 | 5 | true |
| CP-8 | S2 | `tests/Antiphon.Tests -> bin-c1213-s2/` | fragments | `/*/*/(InstructionFragmentTests*)|(InstructionBundleTests*)/*` | T17-T25 | all green | 45 | 4 | true |
| CP-9 | S2 | CP-8 | launch | `/*/*/(DelegateBundleLaunchTests*)|(DelegateScriptKindTests*)/*` | T22 | all green | 6 | 3 | true |
| CP-10 | S3 | `tests/Antiphon.Tests -> bin-c1213-s3/` | brief | `/*/*/(DelegationUnitTests*)|(InstructionBundleTests*)/*` | T26, T23 | all green | 40 | 5 | true |

PC list: pending (Mutation owns deliberate mutants; the mutations in §5 are the candidates).

## 7. Risks

- **Runner skew.** An old runner 409s new-format publishes (`RunnerWorkspaceService.cs:589`) and holds parks. Mitigated by the feature gate (D-4); until the staged server2 rollout, server2 tasks keep legacy names. Verify after rollout with one dispatched task's `Assigned branch:` line.
- **Card numbers are board-scoped** (`CardIdentifierAllocator.cs:66`). Two boards can both have 1213; the 8-hex tail keeps refs unique, the number is a human hint only.
- **Card-spawn branches** `feat/card-<identifier>` (`WorktreeManagerTests.cs:323`) share the prefix. The regex requires digits, a stage token and an 8-hex tail, so they never parse as delegate branches; T2 pins it.
- **Wider glob** `feat/card-*` in the scan returns more rows; TryParse filters them. Measure on the desktop repo during Review.
- **Command-line budget.** The fragment adds ≈1.5k characters to every delegate composition; T25 re-measures (`DelegationSettings.CommandLineBudgetChars`).
- **Warm pool delegates** keep the composition they launched with until retirement (README); the fragment reaches them at next launch, as with any bundle edit.
- **Null `WorktreeBranch` at dispatch** with a kept legacy branch: T16 covers both-candidate adoption.

## 8. Operator decisions (recommendation first)

- **OD-1** Enable new names by default once S1 lands? Recommend yes; the runner gate makes it safe. Alternative: setting `Delegation:CardBranchNames=false` until the rollout is confirmed.
- **OD-2** Stage token for TestDesign: `testdesign` (recommended; hyphen stays the separator) or `tests`.
- **OD-3** Unbound tasks keep `feat/card-task-<8hex>` (recommended) or move to `feat/task-<stage>-<8hex>`.
- **OD-4** No placeholder expansion in briefs in this card (recommended); orchestrators stop restating because every delegate carries git-common. Revisit if brief templates are wanted.
- **OD-5** Human contract line `GIT-COMMON contract v1.` plus hash (recommended) or hash only.
- **OD-6** Directory stays `card-task-<8hex>` (recommended; D-3) or also carries the card (nine more consumers plus runner `NamePattern`).

## 9. TestDesign handoff

TestDesign is a separate stage (not folded here). It turns §5 into method-scoped PC rows and confirms the §6 filters against a fresh census (`docs/testing-and-build.md:503-508`, `:543-547`, `:817-821`).
