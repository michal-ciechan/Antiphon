# CARD-0824 S5b: ref-only desktop halves for runner tasks

Date: 2026-11-05. Plan owner: task `47169b54`. Source read at
`d465bd4e363a3fd3ba334f174a5576852ee7e227`, branch `feat/card-task-47169b54`.
This document supersedes the conditional desktop-slot S5b in the November 3 amendment
§6.2. It does not supersede S1, S2, S4a, S4b, S5a or S5c.

**Outcome:** implement the operator's November 5 A-D-6 answer, “option a”: an admitted
ordinary runner task has a task branch and durable desktop identity, but no desktop
checkout, directory, worktree registration, index or desktop slot. The runner still
gets its existing mirror or S1 slot. Land still uses the dedicated land worktree.

Three independently landable slices put consumers before the producer. **S5b-3 is the
first slice that stops new desktop checkout growth**, initially for the configured
canary roles and runner. Widening it to all supported eligible roles removes almost
all of the measured growth; it does not remove existing directories. TestDesign is a
separate stage, beginning with S5b-1. The witnesses and checkpoints below are its
required contract, not a claim that those tests already exist or have run.

## Evidence and ground truth

Inputs read:

- [S5-0](../../investigations/2026-11-03-card-0824-s5-0.md), especially §§2, 3 and 5.
- `docs/investigations/2026-11-05-card-0824-state-and-plan.md` read with `git show`
  from `origin/feat/card-task-e438ae14`, tip
  `94b78fc7c35f2762112c829889eaa1072762e011`; its code baseline is
  `28cdefab326782f2c91ca361768a7252dc875ec7`, not this plan's baseline.
- CARD-0824 through `scripts/card.ps1 get CARD-0824 -Board Antiphon`: revision count
  52, InProgress, Critical/Now. The A-D-6 answer is supplied explicitly in this
  commission. The older description still describes slots and is not a contrary decision.
- Original plan at `origin/feat/card-task-cddf06e9`, tip
  `3c273ba05cf38311dae7d9cbb5cffd40c4ec1fb3`, and the later landed
  [S4/S5 amendment](2026-11-03-card-0824-s4-s5-amendment.md), §§6.0–6.2. The conditional
  §6.2 is in that amendment, rather than the original revision's section numbering.
- Owners: project-context, orchestration-loop, testing-and-build, test-performance-standards,
  ops-http, agent-card-lifecycle and the slot/parking portions of session-runtime-invariants.

All source citations below are repository-relative and were checked at the full source SHA
above. `Services/` abbreviates `server/Application/Services/`; it is not a new directory.

| Card/investigation assumption | Current code or evidence | Consequence |
|---|---|---|
| The desktop checkout is canonical for runner tasks. | `Services/RemoteWorkspaceService.cs:17–31` says so; `DelegationWorktreeService.cs:414–488` adopts/creates a checkout and reads its HEAD regardless of ordinary RunnerId. `AgentTaskDispatcher.cs:5999–6001` invokes it. | Replace the representation explicitly; changing just worktree creation is unsafe. |
| Nearly all desktop growth comes from runner halves. | November 5 investigation: 856/879 recorded desktop creations since October 29 were runner tasks, 97.38%. S5-0: about 150/day × 9,744 files, about 1.46M creates plus deletes/day and 31GB/day of writes. | Highest-value cut is no desktop checkout, independent of runner-slot enablement. These are historical measurements, not a fresh filesystem census. |
| Reusing desktop slots is cheap enough. | S5-0: 1,000 attached registrations cost 12.6–12.9s and 1.57GiB per listing; loaded checkout 40.4s, checkout child 30.8s. Defender real-time is the reported main cost; its share was not separately isolated. | Ref-only eliminates checkout work and new registrations; it does not make today's legacy registry free to inspect. |
| Push and baseline only need a ref. | `RemoteWorkspaceService.cs:110–132` runs HEAD/push in WorktreePath; `AgentTaskDispatcher.cs:7926–7940` reads HEAD there. `MintBranchAsync`, `DelegationWorktreeService.cs:301–332`, supports both branch-name schemes. | Use the recorded full task ref and SHA in RepoPath, never canonical HEAD or reconstructed legacy branch names. |
| Settlement can become a ref update. | `RemoteWorkspaceService.cs:538–589,637–871,943–1003` combines exact remote observation, mirror publication, ancestry, leases and pins with repeated registered-clean-checkout checks and `merge --ff-only`. | Retain publication/identity/ancestry/custody; replace only desktop-checkout validation and advancement with ref validation and compare-and-swap. |
| Missing desktop files are already tolerated by land. | `AgentTaskLandSourceResolver.cs:43–50` requires coordinates; `:109–160` reads branch refs. `LandOperationFactory.cs:70–84` invents a missing checkout's git-dir. `AgentTaskLandingProtocol.cs:610–679` binds registrations/source canonical path. `AgentTaskLandService.cs:904–945` probes source index locks. | Non-null identity remains useful, but absence is not a mode. Ref-only land must explicitly omit checkout identity/probes and never invent an admin path. |
| All land paths already use refs. | `AgentTaskLandSourceResolver.cs:349,490` still executes `reset --hard` for reviewed-source recovery. | Ref-only recovery refuses before mutation. Ordinary exact-SHA land remains supported; no hidden temporary desktop checkout. |
| Deliverables run only for channels. | **Not true at this baseline.** `AgentTaskReplyService.cs:1302–1303` calls bundles for Succeeded tasks with no outbound delivery id. `DeliverableBundleService.cs:48–57` has only enabled/status guards. `:197–224` diffs the working tree; `:264–304` prefers disk, then reads a moving branch. | Pin committed content before enabling even Plan tasks. Existing blob readers can be reused; disk-first and moving-ref fallback cannot. This changes sequencing, not the ref-only decision. |
| No directory means settlement debt is obsolete. | `SettlementSyncRecoveryService.cs:164–171,240–249,287–295` checks path identity and treats missing directory/retirement as supersession. | Mode-aware recovery must not discard legitimate ref-only debt. |
| Check shows current runner work. | `DelegateCheckProbe.cs:458–511` reads the desktop log and working-tree counts; that checkout stays at creation-time state until settlement. | Display observation SHA/time and unknown runner dirt; never report a clean runner from desktop absence or creation-time state. |
| S4a can drain the backlog while this ships. | Code still defaults `WorktreeLegacyRelease.Enabled/Execute=true` (`Settings/WorktreeLegacyReleaseSettings.cs:18,21`, `server/appsettings.json:230–241`). November 5 investigation records unresolved CARD-1717/1718/1708 safety gaps. | **S4a remains OFF operationally.** Deployment must verify explicit effective off overrides; shipped defaults are not an off guarantee. This plan performs no settings write. |

Placement was read at about 09:59 UTC via **GET /api/runner-defaults** and
**GET /api/session-runners**. Defaults revision 2 had global runner `server2` and no kind
overrides. The catalogue had a ready Linux runner (capacity 10, occupied 5), a ready Windows
desktop (capacity 2, occupied 0), and a retired/unavailable temporary runner. The ready remote
advertised repository identity, publication and parking capabilities, but no `workspaceSlotV1`.
These are observations, not placement constants. Refresh both routes before every dispatch.
Portable checkpoints omit `-Runner` and `-Platform`; native rows require `-Platform Windows`.
Use `-Runner` only to pin the explicitly approved canary host. No fleet filesystem location is
embedded in this design; paths derive from configured repository/worktree/evidence roots.

## Decisions

- **D-1 — Ref-only is decided, not awaiting approval.** Selection requires
  `RemoteWorkspaceService.IsEligible` (`:444–449`): Worktree, a nonempty RunnerId,
  non-Mutation, no SourceLanding operation. Keep all four clauses. Feature gates further
  narrow new admissions. SourceLanding keeps its existing separate snapshot path.
  Reject desktop slots, sparse checkouts and disposable empty checkouts: each retains
  filesystem/registration custody that the operator asked to remove.
- **D-2 — Persist the mode; preserve a unique identity string.** Add
  `DesktopWorkspaceKind` (`Checkout=0`, `RefOnly=1`) to AgentTask, plus a nullable binding id.
  Existing rows remain Checkout, including already-removed checkouts. For RefOnly,
  WorktreePath is an absolute **identity-only** value computed once as
  `<configured WorktreeBasePath>/.ref-only/<full task GUID in N format>` using host-native
  separators. Neither this leaf nor its parent is created for the task. Persist it and never
  recycle it or recompute it after configuration changes. RepoPath is the existing canonical
  object repository; RemoteWorktreePath is the runner execution cwd. Reject null WorktreePath
  (widespread identity contracts), the common/bare repo path (cross-task collisions and deletion
  hazard), URI syntax (existing Path operations), and legacy short-id leaves (ambiguous custody).
- **D-3 — A durable binding owns refs, not files.** Add `TaskRefWorkspaceBinding`, keyed uniquely
  by full TaskId, containing binding id, canonical repository/common-directory identity,
  identity path, minted branch/full ref, immutable selected base SHA and base provenance,
  preparation state (`Planned`, `RefCreated`, `Published`) and revision. Uniqueness also
  covers canonical repository plus full ref; check collisions with legacy task owners.
  The task mode and binding are committed before any ref
  mutation. Attempts refer to this binding; they do not select a new mode. A restart may
  complete a recorded preparation, never adopt an unexplained same-named branch. Task id
  uniqueness does not make the eight-character branch suffix collision-free: preserve the
  branch-name policy and refuse a conflicting owner instead of overwriting it.
- **D-4 — Ref mutations use owned repository leases and expected-old SHA.** Add a concrete
  ref-workspace service over existing Git I/O interfaces, not a parallel Git executor.
  Creation is `update-ref <owned-full-ref> <base> <zero-old>` (object-format-correct zero),
  settlement is `update-ref <owned-full-ref> <S> <L>`. Require exact task/binding/repository,
  endpoint, no checkout attached to the owned source ref, ancestry and verification-window
  admission. Same desired tip is idempotent only with the same durable binding. Keep recovery
  pins and never use `reset`, `checkout`, `worktree add`, `clean`, wildcard fetch or force push
  in the RefOnly desktop arm. A failed CAS leaves the branch untouched and produces debt/hold.
- **D-5 — Separate identity, Git cwd and execution cwd.** Centralize their selection in a
  typed workspace context used by the consumers below. Do not introduce a helper that merely
  substitutes RepoPath everywhere: that would authorize unrelated file reads/writes in the
  main checkout. Missing RepoPath/common identity for RefOnly is a named refusal, never a
  fallback to WorktreePath or WorkingDirectory. Never launch a desktop session in the identity
  path. Existing remote mirror binding, runner store, slot generation and conditional seat
  release remain authoritative for runner ownership.
- **D-6 — Feature OFF, explicit runner and role allowlists.** Add typed
  `Delegation:RefOnlyDesktop:{Enabled=false,RunnerIds=[],Roles="none"}`. Runner ids are data,
  never a compiled fleet name; empty/unrecognized lists admit nothing. Roles follow the
  existing validated scalar-list convention, with `none` and exact names; `*` requires an
  explicit operator widening. Reject malformed startup configuration; invalid reload closes
  new admission with a diagnostic. Gates are evaluated before the first binding is committed.
  Thereafter mode handling is unconditional, including when Enabled is switched off. A role
  or runner excluded before selection takes the legacy path. A selected RefOnly task with a
  failed preparation holds; it never falls back to a desktop checkout. Soft rollback closes
  new admission and completes recorded tasks in their original mode. Binary downgrade to an
  unaware server requires draining all ref-only continuations/lands/debts first.
- **D-7 — Ordinary land is supported; legacy recovery is refused.** Persist source mode and
  binding id on land operations/tickets and compare them after every lease gap. RefOnly source
  has no GitDirectory; make that representation explicit/nullable, preserving legacy defaults.
  Refuse both recovery modes, including a resumed `source-adopt-reset`, with
  `ref_only_land_recovery_unsupported` before ref changes, child launches or remote writes.
  The safe operator remedy is a separately reviewed repair task and ordinary land of its
  exact SHA, preserving the old owner/history. A ref-only owner is never silently converted
  to Checkout for `-FromTask`. Designing a new ref-only adoption protocol is outside S5b.
- **D-8 — All artifact reads bind one verified commit.** For RefOnly, use the confirmed
  settlement source SHA (or an already admitted pinned pending SHA under the existing
  read-only pending contract) for diff and content. Never read canonical disk as a fallback.
  Keep report text in the task row/canonical report store even when source content is unavailable.
  This addresses non-channel as well as channel callers before producer activation.
- **D-9 — Cleanup cannot gain authority from an identity path.** RefOnly has no desktop
  cleanup work, registration or desktop-retirement claim. Mark the desktop component
  NotRequired with explicit mode provenance; do not pretend a remover succeeded. Remote
  mirror cleanup, evidence retention and slot release still run through their independent
  existing ownership proofs. Do not delete the task ref at settlement. S2's delayed,
  publication-bound branch deletion remains separately gated.
- **D-10 — Keep history; do not convert legacy tasks in place.** No mass DB relabeling,
  no directory migration in Code, no change to S4a activation. The approximately 700 desktop
  directories are a separate backlog with per-path custody, bytes and reflog obligations.
- **D-11 — Safety-supported rollout first.** The first activation wave is ordinary Worker
  Plan/Investigate/TestDesign/Docs tasks on the approved remote runner, configured explicitly.
  A selected task needing `ShouldArmDenyHook` must have equivalent runner-side hook delivery
  or hold `ref_only_remote_hook_unsupported`; do not write into the identity path or quietly
  bypass the deny hook. Non-Worker expansion is withheld until that contract has evidence.
  S5b does not design new remote orchestrator tooling merely to widen the first wave.

These are implementable defaults under the supplied ref-only decision. The outstanding operator
questions below concern live widening, recovery ergonomics and legacy cleanup, not permission to
produce this plan or begin S5b-1 TestDesign.

## Consumer and identity disposition

TestDesign must carry every row into a production-caller witness or an explicitly identified
unchanged regression. A wrapper-only test does not prove callers stopped using the old path.

| Consumer and verified location | RefOnly disposition |
|---|---|
| `WorkspaceUseAdmission.cs:60–104`; reservation calls in `RemoteWorkspaceService.cs:610–618` and `AgentTaskReplyService.cs:5525–5534` | Keep task-unique identity plus full ref/repository for reservations. Branch overlap still fences other writers. Do not use common RepoPath as every task's workspace key. |
| `TerminalRunnerSeatReleaseService.cs:1222–1243` | Unique desktop identity cannot make two runner tasks ambiguous; remote slot identity remains runner/store/slot/generation. No session-release authority follows desktop absence. |
| `AgentTaskWorktreeBaseResolver.cs:187,211,255,319–323`; `CardSiblingInspectionService.cs:84,214,275` | Resolve refs through validated RepoPath. No fallback Git cwd at identity path. Keep live-sibling and same-card source holds, exact selected SHA and base provenance. |
| `AgentTaskDispatcher.cs:7828–7916` repair-base probe | The old probe demands exactly one owner checkout. For a RefOnly owner, validate binding/ref/pinned source instead; preserve remote currency checks and isolate the repair on its own branch. No symbolic-HEAD/status read of the identity. This does not authorize the separate legacy recovery/reset protocol. |
| `AdvisoryChangePolicy.cs:61` | Different task identities remain different workspaces even when sharing RepoPath. Preserve helper/source ownership checks. |
| `AgentSessionService.cs:3774–3782`; `AgentControlService.cs:1280–1290` | These path-to-owner reconstructions must not turn an identity into a launch cwd. Bind remote execution coordinates from the authenticated task/runner binding; refuse absent binding. |
| `AgentTaskService.cs:212–213,1889,2201–2203,2429,3497–3502,4286–4310,4678` | Authentication/default child context, inherited report, reroute warning and merge-helper creation need explicit repository/source-ref/runner context. Do not authorize Shared edits in RepoPath by default. Child work defaults to its own Worktree from the frozen parent SHA; a requested shared/local continuation without usable execution custody holds. Display identity as “ref-only”, not “missing worktree”. |
| `AgentTaskDecisionQuestionService.cs:113–114` | File-scoped decisions use the bound runner cwd and runner path semantics; missing runner coordinates refuse the grant. An identity path or broad RepoPath must not widen file authority. |
| `HomeTaskService.cs:179,315`; `Infrastructure/Agents/ZombieCensusService.cs:138–148` | Add mode to projections where needed; identity is history/display, not a filesystem location or absence proof. Actual remote owner remains the execution coordinate. |
| `TaskParkPublicationService.cs:129,289–303,412–417`; `RemoteWorkspaceService.cs:1028–1087` | Keep tuple/receipt/attempt checks; ref-only resume validates objects/ref/endpoint and the runner binding, never recreates a desktop worktree. Park/debt capture binds mode/binding through the immutable baseline. |
| `SettlementSyncRecoveryService.cs:164–171,240–249,287–295`; `BlockedTaskSyncRecoveryService.cs:20–44,65–70,114–120` | RefOnly absence is expected. Recover only the debt's exact SHA; binding/attempt/mode drift holds or supersedes according to existing debt rules, not filesystem absence. |
| `DelegateCheckProbe.cs:207–214,458–511,629–630`; `AgentTaskReplyService.cs:5173–5196,5493–5511` | Read bounded committed ranges by SHA in RepoPath. Check records observation time/source; optional exact read-only remote-tip observation can report a newer SHA, but unavailable objects mean unknown log/counts. Do not fetch/move refs from Check or describe creation-time state as current runner state. Working-tree counts are explicitly unavailable, never synthetic zero. |
| `DelegateBindRefusalRecovery.cs:64–75,112`; `AgentTaskReplyService.cs:3702–3750,3790–3865`; `Infrastructure/Files/AgentReportStore.cs:263` | Preserve held-report evidence and canonical report storage; no scans/spill writes under the identity or the unrelated main checkout. Source artifact references resolve to admitted SHA blobs. Missing source cannot invent completion or drop stored report text. |
| `DeliverableBundleService.cs:179–304`; `Infrastructure/Git/GitService.cs` | Use commit-to-commit NUL-delimited diff and bounded blob length/content reads. Preserve normalized path restrictions, exclusions, rename/delete semantics, exact bytes, size caps and omission manifest. One SHA is frozen for all files even if origin advances during bundling. |
| `TaskWorktreeRetirementService.cs:278–300,355–380,510–522,597–648`; `DesktopLegacyReleasePolicyService.cs:145–190,393–494` | Exclude RefOnly before filesystem admission/claims; recheck mode at action time. Preserve independently owned runner cleanup; do not short-circuit it merely because the desktop component is NotRequired. |
| `CardWorktreeCleanupService.cs:89–141`; `CardWorktreeCleanupExecutor.cs:47–149,197–237`; `WorktreeResidueSweepService.cs:157,247,611–642`; `WorktreeHealthService.cs:52,234` | No desktop cleanup target or “missing checkout” finding for RefOnly. Existing remote-binding targets remain. Unexpected files at an identity are foreign/unowned, never deletion candidates. Keep actual branch/ref residue visible. |
| `AgentTaskLandSourceResolver.cs:43–50,109–160,349,490`; `LandOperationFactory.cs:70–96`; `AgentTaskLandingProtocol.cs:610–679`; `AgentTaskLandService.cs:918–945`; `Infrastructure/Git/WorktreeGuardedCleanup.cs:58–63,136–137` | Bind mode/ref identity; source canonical path/admin/lock/residue probes are absent only for explicit RefOnly. Land/target checkout guards remain. Ref-only cleanup performs no disk operation; a late land cannot touch a later task's directory. |

`WorkingDirectory` must remain a legitimate configured caller/repository coordinate, never the
identity placeholder. Its existence alone is not permission to read/write files for a RefOnly task.
The distinct `AgentSession.Cwd` and pooled `Agent.WorkingDirectory` fields currently retain desktop
identity (`AgentTaskDispatcher.cs:6193–6210,6702–6722,7652–7670`). For a bound RefOnly remote session
they may retain the immutable identity for correlation/pool matching, while `RunnerCwd` alone is
the execution path. All local launch-spec construction (`:7175,7264`) must require the remote
binding and use its execution path before any path validation or child launch; missing binding
holds, including boot-wedge relaunch. Brief spill (`:6950–7012`) stays remote and cannot fall back
to writing desktop WorkingDirectory. These are distinct stored roles, not permission to execute
or inspect files at a session's identity string.
The exact DTO/client display edits follow the typed-mode projection in TestDesign; no generic UI
redesign is authorized. No task-owned files, report spill or deny hook may materialize `.ref-only`.

## Slices

### S5b-1 — Durable coordinates and safe land/cleanup consumers, no producer

**Files:** `server/Domain/Entities/AgentTask.cs:215–253`, `AgentTaskLanding.cs:22–23`;
new `Domain/Enums/DesktopWorkspaceKind.cs`, `Domain/Entities/TaskRefWorkspaceBinding.cs`;
`Infrastructure/Data/AppDbContext.cs:2178,2320–2334` and a CLI-generated migration/snapshot;
new `Application/Services/TaskWorkspaceContext.cs` and typed ref-only settings/validation;
`Application/Dtos/LandingDtos.cs:28–33`; the land, cleanup, reservation and identity consumers
in the preceding table. Register through `server/Program.cs`; extend
`tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` rather than copying DI graphs.

**Behavior:** add an additive representation and a durable admission/binding state; no ordinary
dispatch produces it yet. Model mode/binding in source coordinates, land operation and verification
ticket. RefOnly source has null checkout admin identity and explicit absent source-registration
expectation; an actual registration attached to its full ref is a custody refusal. Keep main/land
checkout guards and verified-source/endpoint checks. Reject legacy recovery early. Desktop removal,
health and residue callers distinguish NotRequired from Removed, leaving remote components intact.
Build the typed selectors needed by all identity rows before they can receive a RefOnly task.

**Fail-safe:** absence of marker/binding is Checkout; a contradictory or unknown enum/binding
refuses. Feature OFF remains the shipping default. An existing task cannot become RefOnly merely
because its old directory was cleaned. No filesystem mutation in any RefOnly cleanup arm.

**Witnesses FIRST:** add the following classes under `tests/Antiphon.Tests/Application/`.
Names identify individual `[Test]` methods; counts below do not count assertion loops. New API
scaffolding may declare types first, but first behavioral tests must execute and fail at an
assertion against the unchanged production behavior, not at compile/discovery/setup time.

| Class / method | Red-at-base observation (or retained regression) | Required mutant |
|---|---|---|
| `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | EF model has no persisted mode/binding. Assert model/schema/round trip, not a constant. | Drop mapping/default. |
| `...Identity_is_unique_stable_and_never_created` | Existing creation returns a real short-id checkout. Distinct full GUIDs, including equal short prefixes, cannot alias. | Use RepoPath or short id as identity. |
| `...Missing_repository_never_uses_identity_as_git_cwd` | Existing readers fall back to WorktreePath. | Restore fallback. |
| `...Mode_does_not_follow_directory_presence` | Existing admission/factory use directory existence. Exercise legacy-absent and ref-only-with-decoy. | Infer mode from existence. |
| `...Binding_survives_attempt_and_settings_changes` | No persisted ref-only binding to preserve. | Re-evaluate mode on retry. |
| `...Foreign_binding_refuses_before_git_or_file_io` | No binding ownership guard. | Skip task/common-directory match. |
| `C0824RefOnlyGateTests.Default_off_and_empty_lists_admit_nothing` | Retained negative contract; base already creates legacy trees. | Default true or empty means all. |
| `...Role_and_runner_both_must_match` | On-config cannot select no-checkout behavior at base. | OR the gates. |
| `...Malformed_reload_closes_only_new_admission` | No validated ref-only hot gate. | Invalid means wildcard; stop old-mode handling. |
| `...Mutation_source_landing_and_desktop_are_excluded` | Retained IsEligible regression with explicit feature config. | Drop each eligibility clause separately. |
| `C0824RefOnlyLandingTests.Late_land_uses_pinned_source_without_touching_decoy` | Factory/probes bind an existing decoy at WorktreePath; desired behavior lands from refs and preserves it. | Restore source canonical/index-lock lookup. |
| `...Verification_resume_binds_mode_and_binding` | Ticket has no mode/binding comparison. | Omit either comparison. |
| `...Foreign_source_registration_holds_without_mutation` | No typed ref-only registration expectation. | Ignore attached source branch. |
| `...Recovery_and_interrupted_reset_refuse_before_any_write` | Recovery reaches checkout/reset paths. Exercise both recovery modes plus persisted reset intent. | Permit ref-only recovery/reset. |
| `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | Existing callers select task path and create cleanup work. Exercise each named entry point; preserve foreign decoy and pins. | Remove one consumer's mode guard per variant. |
| `...Remote_cleanup_still_uses_its_binding_and_generation` | Desired separated desktop NotRequired flow does not exist. | Return before remote component. |
| `...Legacy_missing_and_dirty_checkouts_keep_their_guards` | Retained regression; legacy missing is not RefOnly, dirt remains guarded. | Apply ref-only shortcut to Checkout. |

This slice closes CP-1..CP-3 in the single importable **Checkpoints** table below.
**Risks:** mode omitted from a copied DTO, old binary interpreting identity as cwd, no-op cleanup
accidentally suppressing runner cleanup, swallowed invalid binding. **Not done:** ref creation,
runner launch, setting activation, new deletion/recovery policy or legacy conversion.

### S5b-2 — Ref settlement, reports and continuations, still no producer

**Files:** `RemoteWorkspaceService.cs:538–871,943–1003,1028–1087` (extract common publication/
ancestry logic, add explicit ref advancement); new concrete `RefOnlyDesktopWorkspaceService.cs`
over `ILandingGit`/`ITaskProgressGit`; `Application/Dtos/TaskProgressDtos.cs` and
`RemoteSettlementSyncDtos.cs` (compatible mode/binding provenance); `SettlementSyncRecoveryService.cs`
and `BlockedTaskSyncRecoveryService.cs`; `TaskParkPublicationService.cs:289–303,412–417`;
`AgentTaskReplyService.cs:1287–1303,3702–3865,5173–5196,5493–5511`;
`DeliverableBundleService.cs:179–304`, `GitWorkspaceService.cs`/`Infrastructure/Git/GitService.cs`
commit-diff/blob seams; Check, authentication/child/default-cwd, control/session and decision
consumers from the table. Preserve `TaskCompletionProgressService`'s remote branch and existing
outcome classifications. Update the RemoteWorkspaceService historical comment to describe both modes.

**Behavior:** keep one sync engine for eligibility, exact endpoint observation, runner publish,
reported-tip validation, baseline/observed pins, lease fairness/cancellation and debt outcomes.
Its desktop strategy is either existing checkout validation/FF or binding validation/ref CAS.
For RefOnly, observe L and remote S; recheck binding, full ref, endpoint, no attached source
checkout and L under the lease. Require B ancestor of S and L ancestor of S, and exact pin S.
Pin S, CAS L→S, then re-read the exact ref. Confirm only S, never an ahead/diverged branch.
Equal S is idempotent. A race or inspection uncertainty is not success. Continue populating
legacy `DesktopBeforeSha`/`DesktopAfterSha` fields with the local **ref** tips for compatible
progress consumers, with explicit source-mode provenance; do not call them checkout proof.

Debt recovery follows the recorded S, never the latest origin, and may not classify a missing
identity directory as retired. Reader paths use B..S and S:path, with a bounded read-only
observation description for Check. Freeze S once for the whole bundle; keep deleted paths
absent, follow renamed destinations, refuse path traversal/excluded docs and symlink entries,
enforce existing byte/total limits before allocation and retain exact-byte hashes. Unavailable
blob content is an explicit omitted/unavailable source, not a successful empty attachment.
Retain task report text when canonical report storage is unavailable; never spill into the
identity path or unrelated main checkout. Existing legacy artifact behavior stays as-is.

Blocked reply/resume keeps the mode/binding. With parking enabled separately, validate its
receipt and exact ref, and restore/validate only the **runner** execution workspace through
existing mirror/park contracts. No desktop `worktree add`/prune arm runs. A rebind to another
remote must retain the existing release and store/generation fences. Redirect to desktop for an
existing RefOnly task holds `ref_only_requires_remote_runner`; it cannot materialize a checkout.

**Fail-safe:** malformed baseline version/binding, unknown observed SHA, endpoint drift, failed
CAS or stale debt stays unconfirmed. Existing v1 Checkout baselines retain behavior; a RefOnly
baseline missing its required discriminator is invalid. Ref-only handlers run even after the gate
is disabled. No new parked-session release authority or timer is introduced.

| Class / method (new Application test files) | Red-at-base observation | Required mutant |
|---|---|---|
| `C0824RefOnlySyncTests.Pushed_tip_advances_only_owned_ref` | Base requires a registered checkout; ref-only cannot Confirm. Verify canonical HEAD/index/files unchanged. | Use HEAD, unchecked update-ref or merge in RepoPath. |
| `...Equal_tip_is_idempotent_and_ancestry_failures_preserve_refs` | Missing checkout prevents required equal case; existing ancestry cases are controls. | Accept ahead/diverged/rewound tip. |
| `...Concurrent_ref_advance_fails_compare_and_swap` | No ref-only CAS path. Barrier moves L immediately before update. | Omit expected-old L. |
| `...Endpoint_pin_and_binding_changes_refuse` | No ref-only validation tuple. Fault one dimension at a time. | Drop endpoint, observation pin or binding recheck. |
| `...Reported_tip_and_mirror_publication_keep_existing_contract` | Ref-only cannot reach publication/confirmation. Dirty/unpushed/unknown mirror controls remain. | Ignore report SHA; bypass publisher proof. |
| `...Lease_window_and_timeout_leave_no_running_child` | Base refuses before ref work; desired lease/child outcome unimplemented. | Run unleased or abandon child at cancellation. |
| `...Source_checkout_attachment_is_not_fast_forwarded` | Ref-only source guard absent. Assert no update and no index/tree change in attached tree. | Drop under-lease registration check. |
| `C0824RefOnlyDebtTests.Absent_identity_directory_does_not_supersede_debt` | `RegistrationGoneAsync` returns true. | Keep Directory.Exists supersession. |
| `...Recovery_confirms_only_recorded_sha_and_attempt` | No ref-only recovery can confirm. Change source/attempt independently as controls. | Follow latest origin or omit attempt check. |
| `...Disabling_gate_keeps_existing_recovery_alive` | No ref-only mode handling. | Put sync/debt behind Enabled. |
| `C0824RefOnlyReadTests.Check_distinguishes_creation_baseline_from_remote_observation` | Base sees creation-time desktop state or null. Assert stale/unknown labels and observed SHA/time. | Label baseline current; unknown dirt becomes zero. |
| `...Reply_progress_counts_confirmed_range_never_main_head` | Ref-only confirmation unavailable at base; seed conflicting main HEAD for an outcome oracle. | Count HEAD. |
| `...Long_report_retains_text_without_materializing_identity` | Spill fallback writes WorkingDirectory. | Restore workspace spill fallback. |
| `C0824RefOnlyDeliverableTests.Bundle_reads_exact_settled_bytes_despite_stale_disk` | Disk wins over correct branch bytes. | Restore disk-first read. |
| `...Bundle_pins_one_sha_while_branch_moves` | Base reads moving branch separately. | Resolve branch per file. |
| `...Commit_diff_handles_docs_only_rename_delete_and_named_paths` | Base diffs missing working tree. | Use working diff or omit rename/delete handling. |
| `...Missing_oversize_excluded_and_link_blobs_are_not_attached` | Base can fall back to unrelated disk. | Fall back, drop budget/path/type check (separate variants). |
| `C0824RefOnlyContinuationTests.Park_resume_never_adds_a_desktop_worktree` | Base `RefuseParkResumeAsync` recreates it. | Restore add/prune path. |
| `...Pending_park_debt_keeps_exact_publication_and_mode` | No ref-only debt mode; absence supersedes. | Follow newer tip or drop receipt/binding. |
| `...Blocked_reply_preserves_runner_and_binding_when_gate_closes` | No recorded ref-only continuation. | Re-evaluate flag or substitute desktop. |
| `...Runner_redirect_requires_old_custody_and_rejects_desktop` | Identity path can be treated as normal local checkout. | Retarget without release or launch locally. |
| `C0824RefOnlyConsumerTests.Child_auth_uses_explicit_repo_ref_and_remote_execution` | AuthenticateAsync returns WorktreePath as caller cwd. | Restore identity cwd or default Shared main checkout. |
| `...Decision_and_control_paths_do_not_grant_identity_file_access` | Old path reconstruction grants against WorktreePath. | Use RepoPath/identity as grant root. |
| `...Same_repo_tasks_keep_distinct_ownership_and_display_mode` | No mode-aware projections. Verify reservation/advisory/seat/home/zombie entry points. | Collapse keys to repo; infer missing/clean from absence. |

This slice closes CP-4..CP-7. **Risks:** broad extraction regresses Checkout sync; old debt
schemas misread; blob readers leak canonical files; shared-repo child authorization broadens;
Check reports misleading zero work. **Not done:** creating RefOnly production tasks, changing
parking enablement, remoting local-only hooks, auto-release deadlines or ref garbage collection.

### S5b-3 — Checkout-free dispatch and bounded native acceptance

**Files:** `DelegationWorktreeService.cs:301–345,370–499`; `AgentTaskDispatcher.cs:5980–6030,
6193–6210,6702–6722,6950–7012,7175,7264,7652–7670,7828–7968,9891–9894`;
`RemoteWorkspaceService.cs:110–132`; `RemoteWorkspacePreparer.cs:242–281`;
binding/service from S5b-1/2; `DelegationReportFormatter.cs`, task DTO projections as needed;
`server/appsettings.json` explicit off settings; `docs/testing-and-build.md` remote-worktree
description and `docs/session-runtime-invariants.md` mode/continuation documentation.

**Behavior:** decide mode and save a binding intent before Git. If the current dispatch claim
cannot commit that intent before its Git crossing, split preparation into an owned queued phase
and a later claim; do not hold a DB transaction across remote push, or infer crash recovery from
a branch alone. Reuse current base precedence, sibling inspection and branch minting; resolve
the chosen ref to one full SHA and freeze it. Under the repository lease, create the branch
with expected absence, record completion and retry idempotently. No worktree-list adoption or
CreateAsync call in the new arm; collision recovery is binding-specific, never arbitrary adoption.

Push from RepoPath with the recorded full ref and full SHA, not HEAD. First publication requires
the remote ref absent or already exactly the intended SHA, using an expected-absence lease for
creation; never overwrite a different pre-existing remote ref. A resumed attempt may reuse only
its freshly verified owned descendant and existing publication contract, never repush the old
creation SHA over runner progress. Record the normalized endpoint and observed pushed SHA in
the existing preparation/binding protocol. Baseline capture reads that full ref and records mode/
binding/identity, not HEAD. Preserve the owned background preparer, pushed-source/attempt/store
binding and existing mirror-or-slot decision; failure stays Queued with its existing bounded
backoff. The session's actual cwd remains RemoteWorktreePath. Creation messages say ref-only
branch prepared, never “worktree created” for an absent directory.

**Fail-safe:** no usable binding/lease/endpoint/hook support means a named hold with no checkout
fallback. A previously persisted Checkout task retries as Checkout even if now allowlisted. A
persisted RefOnly task remains RefOnly on flag-off. Unknown crash residue is retained for
inspection. No pruning, branch deletion or sweeping is added to recover a failed preparation.

| Class / method (new Application test files) | Red-at-base observation | Required mutant |
|---|---|---|
| `C0824RefOnlyProvisionTests.Eligible_dispatch_creates_ref_without_checkout_or_registration` | Base creates a full worktree. Assert registry delta zero, no identity directory, ref at frozen base. | Call CreateAsync anyway. |
| `...Explicit_repair_and_card_bases_keep_exact_sha_and_provenance` | Ref-only path absent; old repair probe requires an owner registration. Cover ref-only/Checkout owners and move source after selection. | Read main HEAD, require owner checkout or re-resolve moved ref. |
| `...Crash_after_ref_creation_recovers_only_matching_intent` | Base adopts checkout; no durable ref intent. Crash before/after each persistence boundary. | Adopt unexplained branch; rewrite advanced ref. |
| `...Flag_off_unlisted_and_existing_checkout_paths_remain_legacy` | Retained negative cases paired with on positive. | Apply gate to old tasks or bypass role/runner. |
| `...Selected_failure_never_falls_back_to_checkout` | No ref-only failure path. Inject lease/ref/publication failures and verify no local launch. | Catch-and-CreateAsync. |
| `...Short_branch_collision_and_remote_hook_requirement_hold` | Existing checkout branch scheme does not prove ref binding; deny hook writes locally. | Ignore owner collision or hook requirement. |
| `C0824RefOnlyPreparationTests.Push_and_baseline_use_owned_ref_not_main_head` | Both base operations use WorktreePath HEAD. | Use RepoPath HEAD. |
| `...Remote_collision_is_not_overwritten_and_resume_never_rewinds` | New ref publication admission missing. | Push without absence/expected-tip checks; reuse old base on retry. |
| `...Restart_and_lost_reply_preserve_attempt_store_and_source` | No ref-only binding; expected end-to-end path unavailable. Cover boot-wedge relaunch and remote brief spill; identity is never a child cwd or spill destination. | Re-decide binding/mode on resend; drop RunnerCwd or use local spill fallback. |
| `...Dispatch_settle_land_keeps_main_checkout_and_other_task_intact` | Base creates desktop checkout. Exercise real local Git plus existing loopback peer, stored report and ordinary land. | Skip any source binding; route session locally. |
| `C0824RefOnlyNativeTests.Windows_dispatch_settle_late_land_has_zero_desktop_checkout` | Base creates a Windows checkout. Actual Windows paths/registry, second task and delayed land; preserve a decoy directory. | Restore path-based land/cleanup. |
| `...Windows_restart_and_gate_rollback_keep_ref_only_custody` | Base lacks durable mode. Resume from committed DB/ref state with gate off. | Recreate desktop checkout after restart. |
| `...Windows_two_tasks_and_case_normalization_never_alias` | No typed ref identity. Use real Windows path normalization and runner ownership tuple. | Use shared common path or normalize remote POSIX paths as local paths. |

This slice closes CP-8..CP-10. **Risks:** DB/Git crash gap, old-preparation resurrection, remote
branch collision, identity accidentally appearing in a prompt/cwd, Windows-only equality behavior.
**Not done:** a desktop pool, S1/S2 activation, production canary dispatch during Code, bulk cleanup,
new terminal/parking timers, or support for resetting a ref-only owner during legacy land recovery.

## Performance budget

Apply [testing-and-build performance standards](../../testing-and-build.md#performance-standards)
and [test-performance-standards](../../test-performance-standards.md). This Plan runs no build,
test, live mutation or timing benchmark. Its new-test costs are **provisional**, tracked on
CARD-0824 and refined by TestDesign before Code; no invented measured p95 is asserted.

Measured comparators: S1c TestDesign §Performance budget cites `Antiphon.Tests` build 143s warm
(`b477d7ccf`, CARD-1677) and loaded p50 326s/p95 **579s**, n=117 (`cf8b6982` receipts).
`C0824WorkspaceSlotServerTests.cs:28–33` records per-test p95 **17.2s** at load 37 on the
November 2 `30cb5279b` source, versus 12.6–15.8s alone at load 17–28, with 35s timeout.
These short SHAs identify inherited measurement sources, not tests of this plan. TestDesign
must resolve and retain the original full source/host/sample receipts before treating them as
comparable measured budgets. The brief reports 24 CPUs saturated and 5–10 minute slot waits;
the November 5 CARD-1718 performance handoff records waits of 600s and 330s separately from
execution. Fresh native Windows p95 for this design is **unknown**.

Selection is server Git/delegation/landing and its messaging-artifact consumer, all in
`tests/Antiphon.Tests`. The direct graph is that project → `server/Antiphon.Server` → Pty,
Messaging, Messaging.Client, SessionRunner.Contracts, Resilience and Observability; the test
project also references SessionRunner and fixture/tool projects (`.csproj:73–106`). Ordinary
scope is the exact caller groups below, not all tests of every build dependency. No Pty/provider
runtime changes are planned, so no provider suite is added. If TestDesign adds a contracts or
runtime change, enumerate its transitive consumer batches before Code; unknown graph/ownership
means Full qualification separately, never claimed from these targeted rows. This is not rc gate
credit. Git extraction CARD-1546 is not a prerequisite or an excuse to reorganize projects here.

Test categories are explicit: Gate tests are Unit; Identity tests include persisted binding/model
round trips and are Integration, as are land/cleanup/sync/debt/read/artifact/continuation/consumer/
provision/preparation tests. Native tests use the repository's NativeWindows policy and require
actual Windows execution. Mixing Unit and Integration in CP-1 is intentional and recorded in
membership; only its pure gate methods use the 2s body budget. Setup is outside those body spans.

| Phase / selection | Provisional expected duration | Timeout/measurement obligation |
|---|---|---|
| Build slot admission | 5–10min under cited load | Separate finite 12min admission bound per acquisition; expiry is not a test failure/pass or permission to bypass slots. Record wait and configured gate limit. |
| One fresh Antiphon.Tests build | 6–10min loaded | Approximately 2 × 579s = 1,158s, round to **20min** build budget. Record actual SHA/platform/load; do not apply this as a test-body timeout. |
| Pure identity/settings method | <0.1s normally; class ≤10s | Initial **2s/test**, provisional CARD-0824; collect timing and replace with rounded ~2× observed p95. File reload cases, if introduced, need their own measured bound. |
| Git/DB lifecycle method | 5–20s, complex multi-step ≤30s | **40s/test** ordinary or **60s/test** multi-step provisional, motivated by 17.2s comparator. Native method initial **120s**, no Windows p95 claim; one measured baseline per native case before finalizing. |
| Shared container/readiness + migration/seed | ≤60s + ≤30s once per test host | Existing TestDbFixture lazy assembly/session lifetime; instrument start, readiness, migration and seed separately. No per-class container/web host. |
| Per-test DB clone/data + scratch Git | ≤5s + ≤5s | Template-cloned DB and independently owned scratch repos/refs. Shared immutable seed repositories; no mutable branch/index shared between tests. |
| Loopback web/phone-home host | ≤5s once per test host | Shared fixture; isolated task/attempt/store identifiers. A restart/crash witness alone creates a fresh host and labels/measures that exception. No AppHost, Docker stack or production runner. |
| Body + teardown per integration row | 1–4min + ≤15s | Budget **8min** row execution provisionally; pure row **1min**. Native row expected 3–6min, initial **12min** execution budget. Calibrate to about 2× actual row p95. |
| CLI/HTTP/DB external calls | Typical local Git <1s; loopback <1s | Record calls separately, use existing admitted/cancelable drivers with finite deadlines including stdout/stderr drains. Initially 10s local Git, 5s loopback/DB, 15s teardown; explicitly measured fetch/push/sync subject budgets stay existing production values. No timeout inflation for red. |

The outer row limit may include its isolated build: use **28min charged execution** for a fresh
portable build+row, **8min** for reused integration rows, **1min** for reused pure rows, and
**32min** for the native cold build+row only after the required performance Debug disposition.
These combined caps do not replace independent build/body deadlines. Hard wall is separately
finite (charged cap plus bounded admission allowance); if the available tool cannot express a
separate build/body cap, TestDesign must retain the explicit CARD-0824 provisional exception
and choose single-row runs, rather than silently accept importer's 15min floor/3× estimator.
R5 requires measurement-driven budgets, not treating these bootstrap ceilings as p95 values.

**R1 stop:** a whole slice/union run can exceed 30min once build-slot waits are included.
Before scheduling that batch, the caller must commission a bounded **Debug performance task
on CARD-0824**, using the cited CARD-1718 handoff and the first row's current timings, to settle
packing/admission versus execution costs. Split at the named row boundaries; do not repeat a
whole Unit/assembly run. Any observed >30min selection/run still requires its timing evidence
and Debug follow-up, even if the time was admission. The native fresh-build exception also
requires that disposition before scheduling. This is a future execution gate, not a live action
or an unreported completed Debug task.

Shared setup: one TestDbFixture/template and, when required, one loopback host per test host.
Use transactions or cloned databases for test isolation, `DelegationTestServices` for graphs,
and assembly-local `ParallelLimiter<ProcessSpawnLimit>` for spawned processes. Do not copy S1c's
historical per-test host setup. No parallel Antiphon.Tests processes without the established DB
and process isolation, and no co-scheduled Pty/FakeClaude assembly. One build at a time/worktree.

Record fresh TRX, executed Class.Method roster, source/build receipts and TestTiming phases under
the ignored `.antiphon/checkpoints/<run-id>/`. Include host/platform/category/app-group, source SHA,
sample size, p50/p95/max, admission, build, container/readiness, migrations/seed, clone/data, host,
body, teardown and CLI/HTTP/DB/filesystem spans. Unknown phases remain unknown and remainder is
explicit. TestDesign obtains actual per-method timings from the first required red/green runs;
no blanket repeated green battery. At most the existing normal/loaded repeat allowance when a
demonstrated measurement gap needs it. Review checks setup sharing, comparable p95 and ~2× limits.

### Checkpoints

One table covers **every slice** because `PlanTableImporter.cs:24–55` reads only the first
Checkpoints table. Each slice's rows above refer here. `portable` means default placement without
runner/platform pins; `windows-native` means Windows required, no host hard-coded. The checkpoint
tool owns isolated outputs and the build-slot gate. CP references reuse an exact clean build at
the same committed After slice; each row still runs its one exact filter. Native CP-10 is a
separate build on the actual Windows source SHA. Do not reuse a binary across hosts or commits.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S5b-1 | tests/Antiphon.Tests -> bin-c0824-ref-1/ | portable-ref-identity | /*/Antiphon.Tests.Application/(C0824RefOnlyIdentityTests*)\|(C0824RefOnlyGateTests*)/* | D-1..D-6; S5b-1 identity/gate | all 10 named methods, 0 failed/skipped | 10 | 13 | true |
| CP-2 | S5b-1 | CP-1 | portable-ref-land-cleanup | /*/Antiphon.Tests.Application/(C0824RefOnlyLandingTests*)\|(C0824RefOnlyCleanupTests*)/* | D-7,D-9; late land, mode ticket, cleanup | all 7 named methods, 0 failed/skipped | 7 | 4 | true |
| CP-3 | S5b-1 | CP-1 | portable-legacy-base | /*/Antiphon.Tests.Application/DelegationWorktreeTests/(C508_ReuseKeepsRecordedBase*)\|(C508_BaseResolutionRequiresOwnedLease*) | Checkout base/admission unchanged | both exact intended methods, 0 failed/skipped | 2 | 1 | true |
| CP-4 | S5b-2 | tests/Antiphon.Tests -> bin-c0824-ref-2/ | portable-ref-sync-debt | /*/Antiphon.Tests.Application/(C0824RefOnlySyncTests*)\|(C0824RefOnlyDebtTests*)/* | D-3..D-6; exact ref, debt and CAS | all 10 named methods, 0 failed/skipped | 10 | 14 | true |
| CP-5 | S5b-2 | CP-4 | portable-ref-read-artifact | /*/Antiphon.Tests.Application/(C0824RefOnlyReadTests*)\|(C0824RefOnlyDeliverableTests*)/* | D-8; committed evidence/content | all 7 named methods, 0 failed/skipped | 7 | 3 | true |
| CP-6 | S5b-2 | CP-4 | portable-ref-continuation | /*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)\|(C0824RefOnlyConsumerTests*)/* | identity table; reply/park/child/control | all 7 named methods, 0 failed/skipped | 7 | 4 | true |
| CP-7 | S5b-2 | CP-4 | portable-legacy-sync | /*/Antiphon.Tests.Application/RunnerSettlementSyncTests/(Pushed_tip_fast_forwards_exact_owned_checkout*)\|(Endpoint_change_is_not_followed*)\|(C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource*) | Checkout sync, endpoint and exact debt unchanged | all 3 intended methods, 0 failed/skipped | 3 | 2 | true |
| CP-8 | S5b-3 | tests/Antiphon.Tests -> bin-c0824-ref-3/ | portable-ref-dispatch | /*/Antiphon.Tests.Application/(C0824RefOnlyProvisionTests*)\|(C0824RefOnlyPreparationTests*)/* | new producer, retry/publication, lifecycle | all 10 named methods, 0 failed/skipped | 10 | 14 | true |
| CP-9 | S5b-3 | CP-8 | portable-legacy-preparer | /*/Antiphon.Tests.Application/RemoteWorkspacePreparerTests/(Mirror_runs_outside_the_claim_and_the_task_stays_queued_with_an_in_flight_hold*)\|(A_process_restart_re_arms_the_mirror_and_the_row_is_written_once*) | owned preparer and restart regression | both intended methods, 0 failed/skipped | 2 | 2 | true |
| CP-10 | S5b-3 | tests/Antiphon.Tests -> bin-c0824-ref-native/ | windows-native-ref-lifecycle | /*/Antiphon.Tests.Application/C0824RefOnlyNativeTests/* | Windows zero checkout, late land, restart, identity | all 3 native methods, 0 failed/skipped | 3 | 16 | true |

Roster audit at this plan's source: no existing class starts `C0824RefOnly`. Existing classes
and namespace are verified in their Application files. The seven selected existing method prefixes
(two base, three sync, two preparer) each match exactly one method at the plan baseline.
TestDesign repeats the suffix-collision scan after adding tests,
checks actual TRX membership, and splits to exact-method rows if any prefix widens. Every OR operand
has parentheses and its own trailing `*`; table pipes are escaped. Counts are future named method
floors, not claims of current executions. If parameterized rows replace methods, TestDesign updates
both roster and Min before Code. Do not run the planned filters while their tests are absent.

Tool execution: use `dotnet run --project tools/Antiphon.Checkpoints --no-build -- run --plan
<this-path> --after <slice> --expected-source-sha <committed-sha>` with the measured timeout
overrides/row packing finalized by TestDesign. Bootstrap, if needed, is separately recorded and
uses `scripts/build-slot.ps1`; all direct builds use native bin/obj without OutputPath overrides.
Checkpoint outputs are tool-owned cold exceptions. Wait until the checkpoint run is terminal;
exit 75 means continue waiting, never settle while a child runs. Slot timeout exit 4 is not-run,
never a license to run unleased. Preserve unedited CHECKPOINT lines and validate receipts against
the exact tested SHA. Commit assertion-red witnesses before fixes and commit the slice before its
long checkpoint run. Code/Review run `scripts/check-evidence-diff.ps1` over base..HEAD.

### Cost

Provisional ordinary execution floor from EstimatedMinutes: S5b-1 **18min**, S5b-2 **23min**,
S5b-3 **32min** (16 portable + 16 Windows), total **73min** per complete Code or ordinary Review
pass, before queue waits and red/fix cycles. R1 therefore applies to a combined run; do not
schedule all rows as a single unattended battery. Reuse setup/builds within each source group;
TestDesign/Debug may refine packing with evidence without broadening the closed roster.

| Work | TestDesign | Code authoring + ordinary verification | Independent Review |
|---|---:|---:|---:|
| S5b-1 | 2–3h | 5–7h | 2–3h |
| S5b-2 | 3–4h | 8–12h | 3–4h |
| S5b-3 | 2–3h | 5–8h plus native scheduling | 2–3h plus native scheduling |
| Total | **7–10h** | **18–27h**, plus admission/repair waits | **7–10h**, plus any diff-only repair round |

These are planning estimates, not paid/measured effort. They exceed S5-0's 8–10h Code estimate
because this plan includes durable crash-safe mode, full consumer migration, existing recovery
refusal, non-channel artifacts and native acceptance. Expect roughly 3–5 pipeline days before
canary evidence, depending on host/Review availability. No promise of a two-day complete rollout.
Review rounds follow the repository's three-round ceiling. Positive controls are separate
post-land SourceLanding Mutation work; TestDesign enumerates every listed guard variant, exact
method, red assertion and cost before commissioning it. PCs remain method-scoped, not whole
class runs. Every mutant must show the intended assertion red, restoration green and zero owned
children; a compile/fixture failure or zero tests is not a kill. No mutation is run by this Plan.

## Rollout, migration and dependencies

1. Land S5b-1, then S5b-2, then S5b-3 through normal Code/ordinary Review. Do not enable the
   producer on a mixed server binary or before the native row passes. Deploy only through the
   existing activation owner; confirm loaded source SHA, migrations, mode readers and effective
   S4a **Enabled=false/Execute=false**. Plan/Code landing alone does not activate anything.
2. Refresh runner-defaults/catalogue and choose the explicitly approved runner allowlist. Initial
   intended canary is **server2 only**, per this commission, with Worker roles
   `Plan,Investigate,TestDesign,Docs`. This is an operational value, not a hard-coded location or
   general dispatch pin. Keep S1 slot flags unchanged: ref-only works with ordinary mirrors.
3. Provisional staffed acceptance is **at least 20 new tasks over at least 24h**, including one
   restart/retry, one blocked reply, one artifact bundle, two concurrent tasks and two ordinary
   lands, one requested after another task has settled. Reuse actual authorized work where
   possible; any dedicated spend/setting change is commissioned separately. Record task ids,
   mode, binding/ref/base/source SHA, runner binding, dispatch/settlement/land events, desktop
   registration and directory deltas. The required result is **zero new desktop checkouts or
   registrations for admitted tasks**, not merely faster worktree add. A canary is future work,
   not acceptance claimed by this document.
4. Stop new admission for any wrong ref/endpoint, checkout creation, foreign-path access,
   false clean/progress, lost report, unsafe cleanup, unsupported continuation or unbounded
   child. Preserve evidence and bound refs; let correctly bound in-flight handlers finish.
   Hold uncertain tasks; use Debug for defects. Closing the gate does not change their mode.
5. After accepted evidence, add Code/Review/Debug/Merge/Custom/Test and remaining eligible
   Worker roles by operator choice. Mutation/SourceLanding stay excluded by IsEligible.
   Explain the deliberate legacy recovery refusal before adding Code/Merge; callers use a
   fresh repair/ordinary land instead of expecting `-FromTask` to materialize an owner tree.
   Non-Worker deny-hook capability remains its own admission hold. Only broad widening stops
   almost all of the 97.4% runner-half growth; the first four roles stop their own contribution.
6. Leave local task branches and required pins until existing publication/dependent-task rules
   allow retirement. S2 remote deletion stays separately gated; mode does not supply publication
   or drain proof. S5a/S5c and runner slot reuse are not prerequisites for this cut.

**Existing approximately 700 desktop directories:** the November 5 investigation's 708 is an
operator observation, not a current inventory. Let the separately commissioned manual cleanup
owners finish (`e601cbb4` desktop, `374d7fa0` runner); read their full reports before scheduling
another cleanup. They are the interim drain while S4a is OFF. A fresh exact-path inventory must
join task/attempt/branch to live sessions, reservations, pending land/sync/park debt and dependent
tasks; prove pushed commits plus retained report/ignored/protected content and reflog-only work.
Unknown owner, dirty/index-hidden/case-twin bytes, inaccessible census, alternate streams,
unproven holder, set-aside or incomplete receipt means keep/quarantine and report, not delete.
Use the authorized proof-first manual cleanup contract and exact paths; do not reuse this plan
as a bulk removal authorization. Preserve the local/remote branch and historical Checkout mode
for late lands. No branch deletion is bundled with deleting a directory.

**Safety dependency:** S5b implementation and no-checkout admission do not require CARD-1717,
CARD-1718 or CARD-1708 to be finished: new RefOnly tasks have no desktop tree/index/ignored files/
worktree HEAD reflog to release, so most S4a exposure disappears for them. S5b-1's explicit
no-desktop-cleanup guard must land before enabling. Existing runner-half desktop directories,
all future desktop-run tasks and any excluded legacy-mode runner tasks still have the full
S4a exposure: late writes/removal races (1717), index flags/case twins/ignored secrets/census
warnings/Windows ADS (1718), and reflog-only recovery history (1708). Those repairs and their
native/preservation acceptance remain prerequisites to **automatic backlog/desktop cleanup**.
The actual runner mirror still has files and its own retention risks, including CARD-1718;
ref-only desktop custody does not fix or enable that cleanup. Ref/pin retention also remains
a separate concern. Keep overlapping cleanup repairs serialized and refresh their landed status
before commissioning edits; no repair is claimed complete from the old investigation.

**Sessions waiting for input:** this plan changes their source representation, not release policy.
A Blocked/AwaitingInput session keeps its runner seat while parking is disabled. **No automatic
release deadline exists in that state (CARD-1083).** An answer resumes the existing permitted
continuation; explicit cancellation/release or separately enabled receipt-backed parking follows
existing custody rules. If parking is enabled, publication, exact owner/generation and conditional
release evidence control release, not an elapsed-time shortcut. No 48h cleanup cutoff, sync
timeout, 120s idle probe or 600s backoff is an input-wait release promise. Working sessions are
not parked. RefOnly does not create a desktop slot to release.

## Operator questions and next handoff

No question blocks S5b-1 TestDesign. The operator already chose no desktop checkout; no second
approval of that choice is requested.

- **O-1, before activation:** confirm the first runner/role allowlist and staffed canary owner;
  proposed values are the server2-only first wave and 20 tasks/24h above. Keep feature OFF until
  activation is commissioned. Widening roles/runners is a separate recorded decision.
- **O-2, before Code/Merge widening:** accept the named legacy recovery refusal with fresh repair
  plus ordinary land as the interim workflow, or commission a separate ref-only adoption design.
  Default here is refusal; no destructive reset is authorized by the ref-only decision.
- **O-3, separate cleanup track:** CARD-1708 still needs hold versus durable recovery-history
  retention disposition, and CARD-1718 ADS needs native evidence/disposition. Ref-only is not an
  answer to either. Until repaired/accepted, keep S4a OFF and continue only the authorized manual
  cleanup workflow.

Next: TestDesign **S5b-1** at this exact plan commit. Expand the caller witnesses and mutant
variants, resolve inherited measurement provenance, settle performance Debug/packing, validate
the importer/filter roster and freeze the first slice's verification artifact. Keep later slices
sequenced; do not enable the producer while its consumers are incomplete.
