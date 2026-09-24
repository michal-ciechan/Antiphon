# CARD-0644: Worktree by default, SHA-admitted deployment

Date: 2026-09-23. Stage: Plan, with verification design folded in; next: Code.
Inspected base: `25564530` on `feat/card-task-ee1e8ee5`.
Card: `e4581f64-bdb4-457b-aa3a-4cb89782061b`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`. Also covers CARD-0636.

## Outcome and boundaries

An ordinary fresh delegation gets its own worktree and task branch when workspace is omitted.
Shared and ReadOnly remain explicit choices. Continuing an existing agent is an explicit choice
of that agent's checkout; a retired Worktree continuation must instead create a fresh branch at
the predecessor's verified tip, or refuse. Deployment from a linked worktree needs no override
when its HEAD matches the admitted SHA. Deployment must still prove which SHA actually started.

The live card includes a fourth requirement added to its description: housekeeping must not
hold every landing behind a Shared writer for the duration of the task. This plan includes it.
No worktree deletion, branch deletion, restart, remote deployment, provider launch, or live
schedule change is part of Plan or ordinary Code verification.

## Ground truth

Paths and symbols below were read in this checkout, and both cards were read using
`scripts/card.ps1 get ... -Json`. Line numbers are navigation hints at the inspected base.

| Card assumption / affected path | What the code does today | Planned disposition |
|---|---|---|
| CLI defaults to Shared | `scripts/delegate.ps1:910` omits `workspace` unless a switch is supplied. Its comments advertise Shared workers. `-Runner` explicitly sets Worktree. | Change effective default/help and validate exclusive switches; retain omission where the server must distinguish default from explicit mode. |
| POST create defaults to Shared | `AgentTaskDtos.cs:30` has nullable Workspace. `AgentTaskService.ResolveWorkspace:1656` chooses Shared for workers and for orchestrators in another directory; non-Git orchestrators fall back to Shared with warning. | Worktree for fresh omitted requests, independent of role or different `-Dir`; refuse non-Git rather than silently sharing. |
| Every default is the API default | `AgentTask.Workspace:139` and `AppDbContext:1726` default to Shared; the enum has stable values Shared=0, Worktree=1, ReadOnly=2. Production factories usually stamp a mode explicitly. | Preserve persistence compatibility; make dispatch decisions explicit in factories, not by changing enum values or backfilling stored tasks. |
| `-OnAgent` retirement only changes the agent | `AgentTaskService:365` adds inherited goal text but retains the new request's workspace/base. Live follow-up explicitly stamps Shared at line 438. | Retired Worktree gets frozen predecessor branch tip and fresh Worktree; retain live checkout continuity. |
| Default change only touches ResolveWorkspace | Interim shape at line 220 assumes omitted Worker=Shared; StartRef and SourceLanding insist on explicit Worktree at lines 246/261; workspace is resolved before routing pins are applied around line 675. | Use one effective fresh-mode decision across admission, then resolve existing-agent placement before final workspace admission. |
| Commit and Merge rely on omission | `CreateMergeTaskAsync:2671` and `CreateCommitTaskAsync:2765` already explicitly set Shared and Never. Merge works in the conflicted task's worktree; Commit works on the settled task's dirty checkout. | Preserve these intentional internal exceptions and test their cwd. Public fresh Commit/Merge have the ordinary default. |
| Settlement is mode-neutral | `CommitOnSettleEligibility` permits succeeded Shared only and excludes Commit/Merge/Mutation/specialists. Dispatcher warm reuse and reply-service pooling have Shared-only branches; Worktree settlement/landing is separate. | Keep policy and branches. Never auto-land or apply Shared commit-on-settle to the new default. |
| UI follows omission | `client/src/features/delegations/DelegateModal.tsx:67,124` initializes Shared and resets Worker to Shared; it posts an explicit selection. Help calls Shared the worker default and promises merge-back on finish. | Initialize Worktree, preserve explicit selection across kind changes, explain explicit landing accurately. |
| Scheduled/tracker work uses AgentTask create | `ScheduleService:418` calls `CardService.SpawnAsync`; release goes through the ordinary card queue. `ExternalTrackerSyncService` upserts cards. `AgentSessionService:160,2820` already creates/reuses a card worktree. | Preserve card-worktree ownership/reuse and test the real card launch boundary. Do not create a second task worktree or turn tracker sync into a spawn. |
| Herdr/channel spawns need Shared | `AgentChannelService.DelegateCardAsync:135` and `AgentControlService` call card spawn. Cardless standing sessions retain configured cwd. Herdr is a session backend, not a WorkspaceMode. | Card spawns stay card-worktree based; explicit standing/pinned continuations retain their configured checkout and backend. |
| Check interpreter is an ordinary omitted task | `SpecialistRequestService:232` and `SpecialistTaskRunner:341` stamp Shared. `LegacyCheckNotePublicationService:250` omits it on a captured interpretation row. Provisioner owns a standing seat/directory. | Keep specialists in their seat, add explicit Shared to the legacy row, preserve specialist lease/commit exemptions. |
| `docs/cards` follows delegate cwd | `CardTaskFileService` uses inspected/pinned board repository and directory, with privacy/publication gates; `CardTaskFileRenderer` says generated files must not be hand edited. | Keep canonical board publication. Delegates use card API for current data; a new branch need not contain current uncommitted exports. |
| Windmill nightly inherits delegation default | `scripts/lib/nightly-run-impl.ps1` rejects shared-tree and linked-worktree checkouts; the scheduled job uses its dedicated clone. It does not create an omitted AgentTask. | Keep isolated nightly clone, existing job, and its guards. No schedule mutation or extra checkout per nightly run. |
| Local deployment has one guard | `scripts/restart-apphost.ps1`, `scripts/deploy-local.ps1`, and `dev-aspire.ps1` each use `Get-AppHostWorktreeClassification` and blanket linked-root refusal. | One shared SHA admission helper, wired into all three before side effects. |
| Allowing those roots preserves locking automatically | Restart and launcher use `logs/apphost.*` under their own script root. Independent linked roots therefore have independent lock files for the same machine ports. | Keep source root separate from canonical shared-stack state root; all local callers/watchdog see the same locks and launcher state. |
| ExpectedSha already exists everywhere | Restart has `-ExpectedServerSha`, checked against source HEAD, then `/api/version`; local deploy has no expected SHA parameter. | Add consistent `-ExpectedSha`, preserve `-ExpectedServerSha` compatibility, and propagate the frozen SHA through children. |
| server2 has the same blanket refusal | `scripts/c590-real.ps1:Get-C590Sha` accepts manifest sourceSha or local HEAD; `Invoke-C590LiveCase` invokes token relay/SSH. There is no AllowWorktree guard in this bridge. `c590-remote.sh:case_deploy_parent` checks out the supplied SHA. | Add deploy-parent admission; do not remove an imaginary guard or apply local-port behavior to server2. |
| Worktree mode means no repository coordination | `AgentTaskLandService.FindWriterAsync:502` holds for another task on the source checkout or any Shared writer in the common repo. Worktree siblings are not repository-wide writer holds. `RepositoryMutationLease` separately owns common-dir `antiphon/landing.lock`. | Housekeeping uses its own Worktree, while actual Git mutations retain brief repository leases. Preserve exact source-owner holds. |
| Cleanup only needs a workspace default change | `TaskWorktreeRetirementService.TryRetireAsync:284` takes a lease per retirement, but holds it through remote-mirror removal. `GitService.DeleteBranchAsync:290` deletes local/remote branches without this lease; its production caller is `WorkflowEngine:851`. | Release retirement lease after durable local outcome and before remote mirror I/O; bound branch cleanup to an operation-sized lease. Do not loosen deletion authorization. |

## Decisions

These are implementation decisions within the operator's requirement, not unanswered requests.

**D-1 — Put the default at the creation boundary.** Omitted workspace on a fresh Worker or
Orchestrator means Worktree, including Deploy/Commit/Merge helpers and cross-directory requests.
CLI may omit the JSON property so configured pins remain distinguishable from an explicit
`-Worktree`; an explicit switch still posts the exact mode. Reject two workspace switches before
POST (current first-switch-wins behavior is ambiguous). Non-Git fresh default requests fail with
usable guidance to choose `-Shared` or `-ReadOnly`. Reject silent non-Git fallback and treating
an arbitrary `-Dir` as proof of isolation. An explicit Shared request remains supported.

**D-2 — Keep historical storage stable.** Keep enum numeric values, entity/DB legacy Shared
defaults, existing rows, retries, and their saved modes. All dispatch-capable creation factories
must assign their deliberate mode; explicitly mark the captured legacy specialist row Shared.
No database migration is required. Changing the EF default/entity initializer would affect old
fixtures, historical synthetic rows, and persistence semantics without fixing API admission.

**D-3 — Existing-agent selection is intentional checkout reuse.** Live `-OnAgent`, explicit
standing `-Agent`/agentId, and a configured standing-agent routing pin keep that agent's cwd,
kind, backend and queue semantics. Their effective writable mode is Shared, with a creation
message identifying reuse; explicit ReadOnly stays ReadOnly where supported. Do not provision
an unused worktree for a pinned process. Explicit Worktree plus an existing-process selection
refuses with a stable `workspace_existing_agent_conflict` diagnostic instead of being ignored.
Apply the decision after explicit/configured pins are known, before row/worktree creation;
revalidate at dispatch. Keep blocked-agent and kind mismatch refusals. OnAgent and Agent are
the explicit reuse opt-ins, so ordinary omission never silently takes the canonical checkout.

**D-4 — A retired Worktree follow-up continues committed work.** Resolve the prior task under
existing caller/project/repository authorization. Read its recorded local `WorktreeBranch` tip
as a commit through the existing Git seam; freeze the full SHA into the new task's existing
`WorktreeBaseRequestedRef` field before queueing. Provision a unique `feat/card-task-<new-id>`
there, never checkout/reset/rebase the old branch or use the old directory as the new writable
cwd. Record predecessor ID, frozen SHA and mode in the follow-up message/event. Public StartRef
plus follow-up remains a conflict; internally derived continuation is a distinct validated
path. Do not invent an operator-supplied selector or merge target.

If the predecessor branch/tip cannot be proven, refuse `follow_up_source_unavailable` with its
task/ref named; do not substitute master, the old base SHA, a remote ref guessed from the name,
or the current canonical HEAD. A caller may explicitly start an ordinary task with StartRef to
a chosen retained commit. A never-dispatched Worktree predecessor has no tip: refuse and give
that same recovery. A retired Shared predecessor uses the fresh Worktree default; explicit
Shared/ReadOnly still wins. For a retired Worktree predecessor, an explicitly requested different
mode is honored only as an explicit new-mode operation, with a warning that it does not continue
the predecessor's branch. This closes CARD-0636 without claiming uncommitted changes survive.
Keep card/report/context inheritance and commit-on-settle policy inheritance.

**D-5 — Normalize fresh mode consistently.** StartRef, RepairSource, SourceLanding, Runner and
Interim Code may use omitted workspace when it resolves to fresh Worktree. Explicit Shared and
ReadOnly keep their current conflicts; Interim Review still requires ReadOnly. Preserve all
role, pin, follow-up, authority, source-landing and structured-base exclusions. Do not relax
selector syntax. Adjust the CLI checks currently testing the raw `$Worktree` switch and API
checks currently testing only `request.Workspace`. This removes a contradictory requirement
to spell the new default while preserving the meaningful guard.

**D-6 — Admit deployment by exact commit, not directory name.** Add a shared, read-only helper
in `scripts/apphost-common.ps1` returning verified root, main root, source HEAD, expected ref/SHA,
admission result and diagnostic. Default expectation is locally resolved `refs/remotes/origin/master`
as a commit. `-ExpectedSha` must be a full 40/64-hex commit SHA equal to source HEAD; it explicitly
selects a release candidate and need not equal origin/master. Apply to both main and linked roots,
so a stale main checkout cannot bypass the comparison. Compare commit IDs, not branch names,
patch equivalence or ancestry. Fail closed on missing/unreadable Git identity or expected ref.

Do not fetch/pull/change branches inside restart admission. State that origin/master is the
local remote-tracking ref and print its resolved value; operators refresh it before an unpinned
deploy. An explicit SHA is reproducible without relying on freshness. `-AllowWorktree` remains
the deliberate non-master escape hatch: with no explicit SHA it admits a verified linked root
at its current HEAD and prints the override; it never overrides an explicit SHA mismatch,
unreadable Git, or restart/launch locks. Tracked-edit warning remains advisory as today: HEAD
equality does not claim clean file contents. No new secret/config-copy behavior.

**D-7 — Retain CARD-0358/CARD-0495 end-to-end proof and machine coordination.** Make
`ExpectedServerSha` an alias of ExpectedSha on restart (reject simultaneous conflicting forms
through normal parameter binding). Local deploy passes the admitted full SHA to restart;
restart passes it to dev-aspire. Each child rechecks source HEAD, and restart still requires
dashboard, `/health`, `/api/version` equal to frozen SHA, and source HEAD not moving during
observation. Preserve exit 3 before teardown, exit 5/lock retention on version uncertainty,
runner-port preservation, child build failures and timeouts.

Use classification.MainWorktreeRoot for local stack coordination files: launch/restart locks,
AppHost wrapper PID, dashboard URL and wrapper/watchdog logs. Build and execute code from the
admitted script source root; server/client own logs may remain there. Update every producer and
consumer of these particular files together. Do not redirect through a stale main checkout or
move a branch to make admission pass. Direct launcher must respect existing launch ownership;
restart-to-launch handoff must permit its own child without admitting a second restart. Test
the parent/child ownership distinction, not just equality of two path strings.

**D-8 — server2 deploy is remote but still has source admission.** Add ExpectedSha and
AllowWorktree parameters to `scripts/verify-docker-stack.ps1`, pass them to the live bridge only
for `deploy-parent`, and reuse D-6 before token relay, SSH, SCP or deployment state writes.
Manifest `sourceSha` is the requested payload, not implicit override authority: if supplied it
must equal the admitted SHA. Freeze that SHA into the C590_SHA payload; retain remote checkout
and image/runtime identity checks. Non-deploy verification cases keep their current SHA behavior.
Do not claim server2 touches local ports or copy local AppHost locks into the remote lane.

**D-9 — Keep exceptional callers explicit.** Internal Commit/Merge and standing specialists stay
Shared with their existing role safeguards. Card spawns (scheduled, tracker-triggered after
release, channel and assigned Herdr) retain their card worktree, including existing-card reuse.
Standing cardless sessions retain configured cwd. Nightly retains its dedicated clone. Card-file
publication retains its board repository/privacy boundary. Neither a Git worktree default nor
a changed DTO should rewrite these independent ownership contracts.

**D-10 — Housekeeping is an ordinary fresh Worktree task, not a Shared exemption.** Commission
cleanup from its own fresh Worktree (ReadOnly only for inventory); use existing preview/release/
retirement APIs for task worktree deletion. Do not infer maintenance from titles/goals, introduce
a Maintenance enum, bypass Shared writer protection, or drop the exact source-path hold. Document
that a manually forced Shared cleanup will still hold landings and is the wrong commission.

Keep `TryRetireAsync`'s repository lease around local revalidation, intent, removal and durable
component/outcome records only. Dispose before remote-mirror cleanup; preserve retry/residue
reporting after disposal. In `GitService.DeleteBranchAsync`, acquire the existing repository
mutation lease per branch operation, retain current caller authority and await all commands;
unavailable lease refuses/defer this deletion without running Git. No lease spans a sweep,
delegate lifetime, report/model wait, or idle gap between batches. The legacy WorkflowEngine
caller's best-effort handling must not report deletion success when refused. This card does not
create a new bulk remote deletion facility or authorize deleting any live branches.

## Caller inventory and ownership checks

Build must close this inventory, recording changed/retained/refused rather than blanket replacing
every occurrence of Shared. Re-run the two searches in S3 to catch additions since this plan.

| Caller / dependent code | Deliberate mode or behavior | Owning slice |
|---|---|---|
| `scripts/delegate.ps1`; `AgentTaskEndpoints`; `AgentTaskService.CreateAsync/ResolveWorkspace`; nullable DTO | Fresh default Worktree, explicit mode preserved | S1 |
| Same CLI's Runner, StartRef, RepairSource, SourceLanding, VerificationRound switches; service admission checks | Resolve effective Worktree consistently; retain incompatible shapes | S1 |
| `AgentTaskService` retired follow-up; `DelegationWorktreeService.ResolveKeptBranchTipAsync/CreateForTaskAsync`; `WorktreeBaseResolver` | Frozen prior tip, new branch; no fallback on unavailable source | S2 |
| live follow-up, AgentId/name, routing pins; dispatcher `TryReuseWarmAgentAsync/ResolveAgentAsync` | Explicit existing-agent cwd; conflict for explicit fresh Worktree | S2 |
| `CreateCommitTaskAsync/CreateMergeTaskAsync`; `CommitOnSettleEligibility`; `AgentTaskReplyService`; `DelegationReportFormatter` | Explicit Shared helpers; default Worktree settlement/landing contract unchanged | S3 |
| `SpecialistRequestService`, `SpecialistTaskRunner`, `LegacyCheckNotePublicationService`, `CheckInterpreterProvisioner` | Explicit Shared standing seat and existing specialist exemptions | S3 |
| `DelegateModal.tsx`; `client/src/api/agentTasks.ts` | Worktree UI default; keep payload type; remove inaccurate help/reset | S3 |
| `ScheduleService`, `CardService`, `OrchestratorService`, `ExternalTrackerSyncService`, `AgentSessionService` | Preserve existing card worktree launch/reuse; no background tracker spawn added | S3 |
| `AgentChannelService`, `AgentControlService`, `HerdrLaunchContextResolver` | Card worktree vs cardless/pinned cwd remains explicit | S3 |
| `CardTaskFileService`, `CardFileRepository`, `CardTaskFileSyncHostedService` | Canonical board export root; generated docs/cards untouched | S3 |
| `scripts/nightly-run.ps1`, `scripts/lib/nightly-run-impl.ps1`, `scripts/lib/nightly-common.ps1`; Windmill nightly | Dedicated clone; retain linked-worktree/shared-tree refusal | S3 |
| Local restart/deploy/dev launcher; watchdog/state observers and `set-apphost-maintenance.ps1` | Same canonical stack-state root, admitted source root and frozen SHA | S4 |
| `verify-docker-stack.ps1`, `c590-real.ps1`, remote deploy-parent | Remote deployment admission before effects; frozen payload SHA | S5 |
| `AgentTaskLandService`, `SharedWriterLeaseProjection`, `TaskWorktreeRetirementService`, `GitService`, `WorkflowEngine` | Worktree housekeeping does not hold repo-wide writer gate; operation-sized mutation lease remains | S6 |

## Code slices and round bounds

Each round is one foreground Code dispatch, at most about 90 minutes including its checkpoint.
Stop at a committed, pushed boundary; an unfinished slice is reported honestly, not hidden by
relaxing tests. Commit the red test slice before running it, then commit the implementation before
green verification. Do not edit source under a running test. No whole-suite run is commissioned.

| Slice / round | Authoring bound | Files and deliverable | Tests / checkpoint |
|---|---:|---|---|
| S1 — Fresh default and admission | 80 min | `server/Application/Services/AgentTaskService.cs`, `server/Application/Dtos/AgentTaskDtos.cs` comments, `server/Domain/Enums/AgentTaskEnums.cs` comments, `scripts/delegate.ps1`; update affected test request helpers to choose Shared explicitly when they intentionally use non-Git scratch directories. | New `tests/Antiphon.Tests/Application/WorktreeDefaultAdmissionTests.cs`, `DelegateScriptWorkspaceDefaultTests.cs`; update old Shared-default assertions and StartRef/Interim/SourceLanding cases. CP-1R/G. |
| S2 — Continuation and pins | 80 min | `AgentTaskService.cs`, `AgentTaskDispatcher.cs`, `DelegationWorktreeService.cs` only if a new source lookup seam is required. Reuse existing full-SHA/base fields. Preserve authorization and predecessor identity. | New `WorktreeDefaultContinuationTests.cs`; update affected `AgentTaskServiceIntegrationTests.cs`, `WorktreeStartRefDispatchTests.cs`, pin tests. CP-2R/G. |
| S3 — Callers, UI and settlement | 65 min | Explicit legacy specialist mode, internal factory comments/choices, `DelegateModal.tsx`, `DelegationReportFormatter.cs` if needed. Run the inventory searches below and examine target-typed request factories at actual create call sites. Preserve independent card/nightly/export implementations. | New `WorktreeDefaultCallerTests.cs` and `client/src/features/delegations/DelegateModal.test.tsx` (or extend existing same component tests). CP-3R/G/UIR/UIG. |
| S4 — Local SHA guard and shared state | 85 min | `scripts/apphost-common.ps1`, `scripts/restart-apphost.ps1`, `scripts/deploy-local.ps1`, `dev-aspire.ps1`; adjust state-path consumers only where they do not already use canonical root. Upgrade old refusal harness before guard activation. Preserve ASCII in changed daemon/auto-start scripts. | Rework `scripts/test-apphost-main-worktree-guard.ps1`; extend `scripts/test-apphost-server-version.ps1`; new aggregate `scripts/test-card0644-local-deploy.ps1` invokes their isolated cases. CP-4R/G. |
| S5 — server2 admission | 55 min | `scripts/verify-docker-stack.ps1`, `scripts/c590-real.ps1`; remote file only if admitted SHA propagation requires a change. Keep token handling opaque. | New `scripts/test-card0644-server2-admission.ps1` driving real deploy-parent admission with inert transport callbacks; existing remote payload checks retained. CP-5R/G. |
| S6 — Housekeeping and completed docs | 70 min | `TaskWorktreeRetirementService.cs`, `server/Infrastructure/Git/GitService.cs`, its DI registration if needed, `WorkflowEngine.cs` only for truthful refusal handling. Update docs named below, with focused updates alongside earlier slices where helpful. | New `WorktreeDefaultMaintenanceTests.cs`, using existing controlled landing/retirement fixtures and real disposable repo lease where necessary. CP-6R/G. |

All unqualified service/test filenames above live under `server/Application/Services` and
`tests/Antiphon.Tests/Application` respectively. New filenames are proposed deliverables, not
claims that they exist at this base. Extract small shared fixture helpers if needed; do not put
new tests behind source-text assertions for runtime behavior.

Inventory searches (read-only, not extra test runs):

```powershell
rg -n 'new AgentTask\b|CreateAgentTaskRequest|\.CreateAsync\(' server --glob '*.cs' --glob '!**/Migrations/**'
rg -n 'Workspace\s*[:=]|workspace' server scripts client/src --glob '!**/Migrations/**' --glob '!**/package-lock.json'
```

Required documentation updates: `AGENTS.md` local-stack/default/cleanup rules;
`docs/orchestration-loop.md` workspace defaults, pins/follow-ups, Commit/Merge, settlement,
housekeeping and deploy recipes; `docs/bootstrap.md` guards, canonical state paths and CARD-0358;
`docs/ops-http.md` omitted workspace, continuation messages, StartRef and SourceLanding admission.
Also correct `docs/apphost-runbook.md`, `docs/docker-stack.md`, relevant
`docs/antiphon-api.md` request descriptions, and CLI/UI help. Preserve the nightly clone recipe,
generated-card privacy instructions, card/session distinction and all literal land-v2 checks.
Do not edit generated `docs/cards/` or historical plans to rewrite their old behavior.

## Verification design

The brief commissions about three minutes of verification per round. This is a bounded ordinary
profile in place of the broad Unit/full-suite recipe, not permission to omit a checkpoint.
Times below are estimates, not measurements. A cold SDK/npm restore or shared-Postgres startup
can exceed them: record setup separately, then actual elapsed time; never widen timeouts or
report an unexecuted case as green to meet the budget. Review may commission broader regression
work separately. No production runner, local 172xx ports, live server2, live provider, live vault,
Windmill mutation, or deployment is required by these checks.

Use existing shared-Postgres fixture schema isolation for persistence tests. Child-process test
classes carry `[ParallelLimiter<ProcessSpawnLimit>]`; use the existing queue isolation where
appropriate. Every new test asserts an observable response, persisted mode/base, launch cwd,
Git identity, refusal side-effect count, or lease availability. Static string checks are only
appropriate for documentation, never the runtime verdict.

### Behavior roster and red witnesses

All proposed TUnit methods below are single executions (no parameterized expansion); a method
may assert several named cases internally. Min counts executions, not matrix combinations.

| ID | Test class / exact method(s) to implement | Observable proof; required red witness |
|---|---|---|
| V-1 | `WorktreeDefaultAdmissionTests.FreshWorkerUsesWorktree`, `.FreshOrchestratorInOtherRepoUsesWorktree`, `.NonGitDefaultRefusesBeforeInsert`, `.ExplicitModesRemainExplicit`, `.DefaultWorktreeAdmitsStructuredBases`, `.ExplicitIncompatibleModesStillRefuse` | Persisted mode and unchanged caller branch, no task on non-Git refusal. FreshWorker also dispatches through real scratch-worktree provisioning and a recording runtime, asserting unique path/branch and launch cwd. Exercise StartRef/Repair/SourceLanding/Runner/Interim at production CreateAsync boundary. Old Shared/default-mode guards make at least first three and applicable structured-base cases red. Explicit negatives assert no inserted task. |
| V-2 | `DelegateScriptWorkspaceDefaultTests.DefaultUsesServerDefault`, `.ExplicitModesRoundTrip`, `.ConflictingSwitchesNeverPost`, `.StartRefAcceptsOmittedWorktree`, `.InterimCodeAcceptsOmittedWorktree`, `.PinsRemainDistinguishable` | Real pwsh CLI against `DelegateCreateStubApi`; exact captured body/request count and exit. Default omission is intentional, not proof alone: V-1 proves its API meaning. Old StartRef/Interim/switch behavior supplies red. |
| V-3 | `WorktreeDefaultContinuationTests.RetiredWorktreeCutsAtPriorTip`, `.FrozenTipSurvivesPriorRefMovement`, `.UnavailablePriorTipRefuses`, `.RetiredSharedGetsFreshWorktree`, `.LiveFollowUpKeepsCwd`, `.StandingAndRoutingPinsKeepCwd`, `.ExplicitWorktreePinRefuses`, `.ContinuationRetainsCardContextAndPolicy` | Real scratch Git with predecessor-only commit; after create move predecessor ref and dispatch through recording runtime: new HEAD remains frozen, branch is unique, main HEAD untouched. Missing/deleted branch refuses with no task/session/worktree. Pin behavior is observed at final launch/reuse boundary. Old retired fallback/pin ordering goes red. |
| V-4 | `WorktreeDefaultCallerTests.CommitAndMergeStayOnOwnedCheckout`, `.SpecialistFactoriesStayOnSeat`, `.DefaultTaskUsesWorktreeSettlement`, `.SharedStillCommitsOnlyItsFootprint`, `.ScheduledAndChannelCardsLaunchInCardWorktree`, `.TrackerImportDoesNotLaunch`, `.CardExportsKeepBoardRoot`, `.NightlyKeepsDedicatedCloneGuard` | Call production factories, eligibility/settlement path, card session launch with recording runtime, sync with recording repository, and nightly guard with scratch fixtures. These are retained-behavior regressions and should pass at the S3 baseline after S1/S2. Their assertions must fail if the pertinent factory mode, launch cwd, footprint restriction, or guard is removed; that is the later method-scoped Mutation design, not an extra Code mutation run. S3's natural red witness is V-5. Do not run real nightly. |
| V-5 | `DelegateModal.test.tsx`: `defaults to Worktree`, `keeps explicit Shared across kind changes`, `submits explicit ReadOnly` | Render and submit the actual form with mocked mutation; assert posted payload, not source text. First two fail with old initializer/reset. |
| V-6 | `test-card0644-local-deploy.ps1` named scenarios: `LinkedMatchesOrigin`, `MainMatchesOrigin`, `MismatchNoEffects`, `MissingOriginNoEffects`, `BadExpectedNoEffects`, `ExplicitExpectedMatches`, `AllowWorktreeOverride`, `OverrideCannotBypassExpected`, `ParentPassesFrozenSha`, `CrossRootRestartLock`, `CrossRootLaunchLock`, `ParentChildHandoff`, `VersionMismatchKeepsLock`, `HeadMovementKeepsLock`, `HealthyWrongShaIsNotSuccess`, `TrackedEditsWarn` | Scratch main+linked+bare origin repositories, copied scripts and inert process/Docker/HTTP/time seams. Matching linked entry passes admission; mismatches name expected/actual/root and cause zero kill/start/remote calls. All three real entry points covered. Locks contend across fixture roots. Retain original CARD-0495 failure evidence. Old blanket guard/global-state paths make matching/cross-root scenarios red. |
| V-7 | `test-card0644-server2-admission.ps1`: `MatchingLinked`, `MatchingExplicit`, `MismatchNoTransport`, `MissingOriginNoTransport`, `ManifestDisagrees`, `OverridePinsHead`, `ExplicitMismatchNotOverridden`, `PayloadFrozenAfterRefMoves`, `OtherCasesUnchanged` | Call production bridge/admission path with recording relay/SSH/SCP seams. Assert refused calls occur before even credential relay; inspect exact admitted C590_SHA, never secret material. Old bridge permits mismatch, so negative scenarios go red. No disconnected mirror-only model of the policy. |
| V-8 | `WorktreeDefaultMaintenanceTests.WorktreeCleanupDoesNotHoldSiblingLand`, `.SharedAndSameSourceStillHold`, `.RetirementReleasesLeaseBeforeRemoteWait`, `.LeaseAvailableBetweenRetirements`, `.BranchDeletionRequiresLease`, `.BranchDeletionReleasesLeaseOnFailure` | Production landing admission with active housekeeping sibling and same-source controls; barrier-controlled remote mirror lets a second lease be acquired while remote work waits. Inspect real common-dir lease between actions and after exception; held lease causes zero deletion calls. Old retirement/branch cleanup fail these lease assertions. Preserve dirty/live/unknown-source retention. |

### Execution and red-first protocol

For each round first commit/push tests against production before that round's implementation.
Run its R row. It must execute the named roster, with the designated behavioral failures; zero
tests, compile errors, fixture errors or transport setup failures are not red evidence. Existing
regression cases may pass; CP-3R is intentionally a passing compatibility baseline, paired with
CP-3UIR's natural red witness. Future Mutation controls are method-scoped and separately
commissioned. They are not additional builds/runs hidden inside this Code manifest.

Where a new public parameter or inert test seam is necessary to reach a behavioral assertion,
the red-preparation commit may add that signature/seam without changing the admission policy.
For example, the old server2 policy must reach the recording transport and fail its zero-calls
assertion; an unknown ExpectedSha parameter or a missing helper is not the required red. Tests
invoke real entry points, not a replacement admission implementation supplied by the fixture.

Implement the round, commit/push, run its G row, and record final SHA, expanded counts, failures,
skips and TRX path. If failing unexpectedly, reproduce the same failing method at the preceding
implementation commit with its fixture prerequisites before calling it pre-existing. Fix within
the round and rerun that row only. No discovery-only evidence and no ad hoc namespace reruns.

TUnit rows use `scripts/run-checkpoint.ps1`, `-Project tests/Antiphon.Tests`, the row's OutputPath,
`-Filter`, `-MinExecuted`, and comma-separated `-Expect` class names, with a fresh
`.antiphon/card0644/<CP>/<attempt>` ResultsRoot. Red rows have a deliberately nonzero test exit;
their captured failures must match the roster. Each green row rebuilds because production changed;
no `--no-build` reuse across that change. This is the explicit reason for the two small builds per
backend round. Record an exact inventory of producer-owned `bin-c644-sN/` directories and delete
only those, after all runs have finished and absolute paths are verified under this worktree.

The two PowerShell harnesses must implement `-Phase RedWitness|Green`. The phase changes expected
verdict accounting only, never the production input or production control flow. Both execute the
same fixture scenarios; red phase reports the named pre-fix behavioral failures and exits nonzero.
Green phase requires every named scenario and zero failures. Aggregate local tests must preserve
individual scenario output/counts from the underlying guard/version harnesses.

**Test-harness safety dependency:** the current `test-apphost-main-worktree-guard.ps1` creates a
linked worktree off the real repository and executes production entry points assuming blanket
refusal. Once admission changes, that assumption can start/stop the live stack. Replace its
fixture with a standalone temporary Git repository and inert allowed-path seams in the red test
commit before running any modified guard. Do not simply update its expected exit code.

### Cost

Ordinary verification floor: **18 minutes** (sum of EstimatedMinutes below), approximately
3 minutes per S1-S6 round. Authoring bounds total 435 minutes; dispatch one round at a time with
the relevant artifact/checkpoints and expect about 83, 83, 68, 88, 58 and 73 minutes respectively.
These are scheduling bounds, not already measured costs. Cold dependency setup is recorded
separately; if measured execution exceeds the estimate, finish the named checks and report it.

### Checkpoints

`Sx-red` means that slice's test/preparation commit; `Sx` means the final production/test/doc commit for
that slice. Every row is commissioned; R and G are distinct invocations, not hidden reruns.

| CP | After | Build | Group | Filter / exact command | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1R | S1-red | `tests/Antiphon.Tests -> bin-c644-s1/` | default-red | `/*/Antiphon.Tests.Application/(WorktreeDefaultAdmissionTests*)\|(DelegateScriptWorkspaceDefaultTests*)/*` | V-1,V-2 | All 12 methods executed; designated default/admission failures, no build/fixture error | 12 | 1.5 |
| CP-1G | S1 | `tests/Antiphon.Tests -> bin-c644-s1/` | default-green | `/*/Antiphon.Tests.Application/(WorktreeDefaultAdmissionTests*)\|(DelegateScriptWorkspaceDefaultTests*)/*` | V-1,V-2 | All 12, 0 failed/skipped | 12 | 1.5 |
| CP-2R | S2-red | `tests/Antiphon.Tests -> bin-c644-s2/` | continuation-red | `/*/*/WorktreeDefaultContinuationTests/*` | V-3 | All 8; designated retired/pin failures | 8 | 1.5 |
| CP-2G | S2 | `tests/Antiphon.Tests -> bin-c644-s2/` | continuation-green | `/*/*/WorktreeDefaultContinuationTests/*` | V-3 | All 8, 0 failed/skipped | 8 | 1.5 |
| CP-3R | S3-red | `tests/Antiphon.Tests -> bin-c644-s3/` | callers-baseline | `/*/*/WorktreeDefaultCallerTests/*` | V-4 | All 8, 0 failed/skipped; retained behavior baseline | 8 | 1.25 |
| CP-3UIR | S3-red | n/a | modal-red | `pwsh -NoProfile -File scripts/test-client.ps1 DelegateModal.test.tsx` | V-5 | 3 named tests executed; old default/reset assertions fail | n/a | 0.25 |
| CP-3G | S3 | `tests/Antiphon.Tests -> bin-c644-s3/` | callers-green | `/*/*/WorktreeDefaultCallerTests/*` | V-4 | All 8, 0 failed/skipped | 8 | 1.25 |
| CP-3UIG | S3 | n/a | modal-green | `pwsh -NoProfile -File scripts/test-client.ps1 DelegateModal.test.tsx` | V-5 | 3 passed, 0 failed/skipped; exit 0 | n/a | 0.25 |
| CP-4R | S4-red | n/a | local-red | `pwsh -NoProfile -File scripts/test-card0644-local-deploy.ps1 -Phase RedWitness` | V-6 | All 16 named scenarios executed; expected old-guard/shared-state failures | n/a | 1.5 |
| CP-4G | S4 | n/a | local-green | `pwsh -NoProfile -File scripts/test-card0644-local-deploy.ps1 -Phase Green` | V-6 | All 16 passed, 0 failures; exit 0 | n/a | 1.5 |
| CP-5R | S5-red | n/a | remote-red | `pwsh -NoProfile -File scripts/test-card0644-server2-admission.ps1 -Phase RedWitness` | V-7 | All 9 named scenarios executed; expected missing-admission failures | n/a | 1.5 |
| CP-5G | S5 | n/a | remote-green | `pwsh -NoProfile -File scripts/test-card0644-server2-admission.ps1 -Phase Green` | V-7 | All 9 passed, 0 failures; exit 0 | n/a | 1.5 |
| CP-6R | S6-red | `tests/Antiphon.Tests -> bin-c644-s6/` | maintenance-red | `/*/*/WorktreeDefaultMaintenanceTests/*` | V-8 | All 6; old remote-wait/branch-lease assertions fail | 6 | 1.5 |
| CP-6G | S6 | `tests/Antiphon.Tests -> bin-c644-s6/` | maintenance-green | `/*/*/WorktreeDefaultMaintenanceTests/*` | V-8 | All 6, 0 failed/skipped | 6 | 1.5 |

## Completion and rollout

Code report names each CP and actual commit/counts, exceptions to the manifest, caller-inventory
dispositions, and remaining risks. Push each slice. Review checks the inventory and runtime
evidence before landing; no migration or historical task rewrite is expected. Existing servers
keep their old default until activated, so API omission is not proof of an upgraded deployment.

After normal Review/Land, an authorized activation should use an explicit full ExpectedSha and
verify `/api/version` SHA/capabilities. Then observe one ordinary new task's persisted Worktree,
fresh path and task branch; exercise a retired continuation in an authorized disposable task;
confirm a separate Worktree cleanup does not appear as the repository-wide writer hold on a
sibling land. Those are post-land operator acceptance, not ordinary Code's permission to deploy
or spend on agents. Record CARD-0636 closure only after continuation evidence passes.

Rollback is a code revert plus normal activation, not conversion of existing Worktree tasks to
Shared. Retain their branches and existing landing ownership. The new default increases fresh
checkouts/cold launches; pool warm reuse remains an explicit Shared/existing-agent optimization.

## Plan evidence

Read-only repository and live-card inspection completed. No application build, test suite,
deployment, cleanup, or runtime mutation was run in Plan. The verification cases above are a
commissioned design, not a claim that tests already exist or pass.
