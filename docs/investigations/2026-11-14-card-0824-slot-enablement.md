# CARD-0824: slot enablement, measured state and confirmed gaps

2026-11-14 UTC; Investigate `9168c199-b6e4-45db-9eb7-20cb6e1193ba`.

**Recommendation: enable after the all-role and ref-only readiness gaps are resolved. There is no configuration value in the current implementation that enables the requested fourteen roles.** Runner slots are implemented but disabled on server2. `*` excludes Mutation, SourceLanding uses a separate verification workspace, and the approved ref-only desktop producer has not landed. A setting-only all-role rollout cannot deliver the operator's decision.

This is an evidence report, not an implementation plan. No live setting, task, database, runner, container or host was changed. No new approval is requested: the operator's enablement authority is already recorded; the caller applies operational changes after this report. The assigned branch was retained as instructed, rather than creating another branch.

## Evidence boundary and runtime observations

Source baseline and fetched `origin/master` were both **`4c5693a3f4dc935f78c074a18b670b0b5fce5b15`**. Unless explicitly qualified, file:line citations below refer to that source. The public API and standing server2 runner both reported **`ab7af12c699039270af7e22a5d7fd794035bc325`**, an ancestor of that master. Landed does not mean activated.

The inspected role grammar, RefOnly gate, RemoteWorkspaceService, DelegationWorktreeService, AgentTaskDispatcher and RunnerWorkspaceSlotPool are unchanged between that running SHA and the source baseline. The dispatcher file has later input/lock-timing changes; its slot feature and slot operation arms retain the same predicates. The CARD-1894 retention difference is identified separately below.

Read-only GETs at approximately **08:17–08:21Z** used the inherited API/task authentication without displaying credentials. Essential response facts are retained here; raw responses and local reconstruction scripts are gitignored under `.antiphon/evidence/card-0824/` in this worktree and are not publication dependencies.

| Observation | Stored response / measurement |
|---|---|
| `GET /api/version` | `version=ab7af12c699039270af7e22a5d7fd794035bc325`, capabilities include `land-v2` |
| `GET /api/orchestrator-instructions` | `[orchestrator-instructions v7f0b7ae8 rev 708]`; section 5 is **Role × complexity matrix**, not slot settings; section 9 target is **7** |
| `GET /api/session-runners` | server2 Linux, available/dispatchEligible, capacity **10**, occupied **8**, `workspaceRetentionV1` and `workspaceBranchRefsV1`, **no `workspaceSlotV1`**; server2-temp retired/unavailable; desktop Windows available, capacity **2**, occupied **0**, no slot feature |
| `GET /api/session-runners/server2/status` | buildVersion as above; runnerStoreId `f519bd08-e53a-47d1-adb1-2ab33475446f`, processBootId `152c4913-b52a-4d06-89ef-1363363f1df9`, runnerSessions **8** |
| `GET /api/session-runners/server2/slots` | declaredCapacity **10**, occupied **8**; eight Running sessions with owner tasks and four Exited rows. These are **session seats**, not reusable workspace slots |
| `GET /api/runner-defaults` | revision **2**, provenance Human, globalRunnerId **server2**, kindDefaults `[]` |
| `GET /api/agent-tasks/pipeline?unscoped=true` | fleet host facts: server2 inFlight **8**, effectiveLimit **10**, remaining **2**; local **0/2**. This unscoped task selection is not Antiphon project occupancy |
| `GET /api/projects` | Antiphon project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, localRepositoryPath `C:/src/Antiphon`. The observed control plane still has desktop coordinates; do not assume Linux-server cutover occurred |
| Runner process environment inherited by this child | exact non-secret key `PhoneHome__WorkspaceSlots__Enabled=false`; no other `PhoneHome__WorkspaceSlots__*` entries. `/app/appsettings.json` has no `PhoneHome.WorkspaceSlots` section |
| Filesystem | `/work/workspace-slots/ledger.json` absent; **0** direct-child `ws-*` directories; **120** worktree directories |
| `GET http://localhost:8080/sessions/workspace-slots` | **404**, consistent with absent S4b. Direct `/capabilities` identifies the same binary, but its runtime store ID differs from the phone-home store: do not conflate direct and phone-home identities |
| Shared build broker GET at `ANTIPHON_BUILD_SLOTS_URL` | enabled, budget **4**, maxCpuCount **6**, occupied **4**, all four holders alive; available memory **76882 MiB**, floor **16384 MiB** |

The task detail supplies a concrete database-backed dispatch reconstruction, rather than an inference from an empty ledger:

```text
GET /api/agent-tasks/9168c199-b6e4-45db-9eb7-20cb6e1193ba
summary: role=Investigate; workspace=Worktree; runnerId=server2
worktreePath=C:\Antiphon\worktrees\card-task-9168c199
worktreeBranch=feat/card-task-9168c199
2026-11-14T08:15:33.783955Z Dispatched:
  Worktree created at C:\Antiphon\worktrees\card-task-9168c199 on
  feat/card-task-9168c199 from 4c5693a3f4dc935f78c074a18b670b0b5fce5b15 (Explicit)
2026-11-14T08:15:49.895198Z Held:
  remote workspace preparation is in flight for runner 'server2'
2026-11-14T08:17:02.563456Z Refined:
  caller confirms A-D-6 was answered 2026-11-05: ref-only desktop halves,
  default off, server2 first; include S5b state/canary/rollback
Observed execution directory: /work/worktrees/task-9168c199
```

**Unverified live values:** the API has no generic configuration-read/write route for these options. Instructions, runner defaults, role policy and capacity do not expose `Delegation:*`, `Landing:*`, or the effective retention switches. The desktop configuration is unreachable from this mirror. Consequently the server slot flag, role string, ref-only switches, S2 deletion settings and desktop S4a overrides are **unknown**, not reported as their defaults or as the November 7 values. Runner support being off is independently sufficient to explain the observed legacy path. Absence of a WorkspaceSlotDecision event cannot establish the server flag: `RemoteWorkspaceService.cs:247–251` also suppresses it when capability is absent.

## S1–S5: what landed and what the switches mean

The November 3 amendment replaces the original slice breakdown. S3 was folded into desktop S5a/S5c, and S5-0 chose ref-only for runner halves. The November 5 ref-only plan supersedes conditional S5b desktop slots (`docs/superpowers/plans/2026-11-05-card-0824-s5b-ref-only-plan.md:5`). The operator's November 14 decision supersedes the old role waves; it does not make the missing implementation exist.

| Slice | Master status and identifying commit(s) | Exact control and default | Current API server / server2 / desktop |
|---|---|---|---|
| **S1 runner pool** | Landed runner `8395c4a431db27680e306aacb0ef46f1dbafbaa8`; server half `e06ef30735e57772e5d20255ddf91cebeaf76267`; custody repairs `20fac33a1dc1d16982091d29ab5a7c8ccdc40ebf`, `7ab9aa36cf88870410aada059071670c87528213` | Server `Delegation:WorkspaceSlotsEnabled=false`; runner `PhoneHome:WorkspaceSlots:Enabled=false` | Server **unknown**; server2 **false**, capability absent, no ledger; desktop runner slot implementation not delivered |
| **S1c role gate / safe rollback** | Landed `31569a95c408c118e51eca25257c9f4e84974fbd` | `Delegation:WorkspaceSlotRoles="*"`; one string; `*` means all **except Mutation**; `none` closes new decisions | Server effective string **unknown**; server2 has no separate role list; desktop uses the same server gate only for remote preparation |
| **S2 origin deletion** | Landed `f95e7e03d7325d45bd905b663b403f474cd257d3`; review repair `99747e3fd8f453f804f7e656690512082f1c0815` | `Landing:DeleteSourceBranchAfterPublication=false`; delay `SourceBranchDeleteDelayMinutes=1440`; cutoff `SourceBranchDeletionAfterUtc=null`; runner list `SourceBranchDeletionRunnerIds=[]`; `MaxSourceBranchDeletionsPerRun=25`; `SourceBranchDeletionDryRun=false` | Effective server values **unknown**; runner advertises branch-ref support, which is not evidence the deletion sweep is enabled; desktop values likewise unknown |
| **S3 Windows/local slots** | **Not landed** as a desktop pool/consumer; amendment folded it into S5a/S5c | Proposed `Git:WorkspaceSlots:*` is **not an implemented switch** in this source | No Windows slot enablement available; not a runner-half prerequisite under ref-only |
| **S4 runner retention/disk** | Delivered by CARD-1677: `1b6aaf36bc054d5a9c6b4246dae6a4a3459257c4`, `2921957643833588c201a9fcc0c84bd98676242e`, `bb0947982a3c170c14ee93d72b4a4f635436bb40` | Server `RunnerWorktreeRetention:Enabled=true`, `Execute=true`, shipped Runners `["server2"]`, `MinTerminalHours=12`, cap `25` | Capability active on server2; **effective server age/switches and executing run rows not verified**. CARD-1750 records a historical age override of 24; do not mistake it for a current read |
| **S4a desktop legacy release** | Landed `3eb9d002738ae707b2247678400fdaba9cabbb52`, later safety repairs and salvage | `WorktreeLegacyRelease:Enabled=true`, `Execute=true`, `MinTerminalHours=48`, `MaxReleasesPerTick=25` | Effective desktop/server values **unknown**; server2 does not run this desktop policy. The ref-only rollout plan explicitly requires effective S4a Enabled/Execute **false** before its producer activation |
| **S4b workspace-slot inventory/attention** | **Not landed**: no workspace-slot inventory operation/route or its server attention consumer; ledger attention records exist | No implemented enable switch | Absent on master and observed route 404; ledger inspection remains the existing fallback |
| **S5-0 / S5b ref-only runner halves** | S5-0 landed `dc0b75e771f97efdc8820842c39dc81726e93a3e`. S5b-1 schema/gate `7bf9f88df07678c9eb096a0ac679791c1ca78936`, cleanup `ea89b8436312369ec9d1affbfa0b443c3a0f572f`; substantial S5b-2 readers/sync/continuation code and migrations landed, e.g. `4bbb90de948200cf13bd70407e392d98cab76cc5`, `e0d68bd69a73ef767e7ec276eb4a8b5d2151c211`. **S5b-3 production admission is absent** | `Delegation:RefOnlyDesktop:Enabled=false`, `RunnerIds=[]`, `Roles="none"`; monitor-driven gate, same Mutation exclusion | Effective server values **unknown**. No ordinary producer calls the gate or RecordPlannedAsync, so setting them cannot stop new desktop checkout creation on this master. Current task proves a desktop checkout is still created |
| **S5a/S5c desktop pool/local consumer** | **Not landed**; deferred by ref-only choice | No implemented `Git:WorkspaceSlots` settings/pool | Local desktop execution cannot be converted to slots by configuration |

Sources: `server/Application/Settings/DelegationSettings.cs:736`, `WorkspaceSlotRoles.cs:6`, `RefOnlyDesktopSettings.cs:7`, `LandingSettings.cs:43`, `WorktreeLegacyReleaseSettings.cs:13`, `RunnerWorktreeRetentionSettings.cs:14`; runner defaults `src/Antiphon.SessionRunner/PhoneHomeWorkspaceSlotSettings.cs:7`. Branch history was checked on fetched master, not inferred from card column labels.

The S5b-3 absence is reconstructed from the call graph: `RefOnlyDesktopGate` is registered in `server/Program.cs:356`, but has **no production caller**; `TaskWorkspaceContext.RecordPlannedAsync` is declared at `server/Application/Services/TaskWorkspaceContext.cs:128` with no production caller. `AgentTaskDispatcher.cs:6083–6125` still unconditionally invokes ordinary `CreateForTaskAsync`; that service creates a checkout for ordinary tasks. Recorded RefOnly readers therefore do not imply an enabled producer. Stored S5b-2 reports also retain incomplete acceptance obligations; this investigation did not certify them green.

## Roles, Mutation, Merge and Deploy

The current effective predicate is: ordinary eligible remote Worktree preparation, server flag on, committed preparation, runner advertises `workspaceSlotV1`, role gate admits. The choice is persisted **before send**, keyed to preparation; retries reuse it (`RemoteWorkspaceService.cs:224–280`). There are no per-role boolean switches or persisted wave rows. Runner defaults choose placement, not workspace mode.

| Requested role | Current `WorkspaceSlotRoles="*"` semantics, assuming the other predicates hold |
|---|---|
| Investigate, Plan, TestDesign, Code, Review, Debug, Custom, Docs, Commit, Coverage, Test, Merge, Deploy | Admitted for ordinary remote Worktree tasks |
| Mutation | **Denied**, and literal `Mutation` in a list fails configuration validation |
| Any task with SourceLandingOperationId, including normal post-land Mutation | **Denied independently**, regardless of role |

Mechanism: `WorkspaceSlotRoles.cs:18–20,40–48,99–108`; `RemoteWorkspaceService.IsEligible` also excludes Mutation at `RemoteWorkspaceService.cs:491–495`. Stored assertions explicitly require exclusion (`tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleSettingsTests.cs:207`, `C0824RefOnlyGateTests.cs:111`). Thus replacing `*` with all fourteen names fails startup; using `*` silently omits the requested Mutation role. The RefOnly gate uses the same grammar and IsEligible predicate (`RefOnlyDesktopGate.cs:25–35`), so it has the same limitation.

**Mutation differs structurally.** The current commissioned flow requires a source-bound verification snapshot at the exact confirmed landing SHA, not an ordinary mirror. `DelegationWorktreeService.cs:381–403` creates it directly on the runner through verification custody. `server/Bundles/stage-mutation.md:3–11` requires that identity, restoration and external evidence and forbids snapshot publication. Slot flag changes do not intercept this path. A mutant loop may leave tracked dirt, outputs or children on interruption; ordinary slot settlement preserves unsafe content by custody quarantine rather than assuming successful restoration. Build-slot leases remain per individual build, while a workspace lease lasts for the whole task. This report does not propose weakening either contract.

**Merge is admitted but has a reproduced reuse hazard.** A normal slot lease resets its own task branch to the server-resolved SHA (`RunnerWorkspaceSlotPool.cs:253–267`); this already gives an exact initial base. H-4 means **keep native build output by default**, not preserve the previous lessee's source/branch. It does not reset a Merge's branch between cuts inside one task: that remains task-owned behavior. Publication to origin/master can satisfy reachability for a simple Merge even if its task branch still points to the base. However:

- Release checks HEAD, the **entire current HEAD reflog**, and slot.Branch with `rev-list ... --not --remotes=origin`; replaced commits that only have equivalent published content still cause custody quarantine (`RunnerWorkspaceSlotPool.cs:411–420,692–720`). This agrees with the real CARD-1901 Merge incident.
- Successful release deletes only slot.Branch and its origin tracking ref, then HEAD reflog (`RunnerWorkspaceSlotPool.cs:495–517`). An auxiliary local merge branch is not deleted. Shared repository refs outlive a directory lease and can collide with a later task. If the Merge uses only slot.Branch, this auxiliary-ref concern does not arise.
- Leftover uncommitted source, sequencer state or live children do not become the next lessee's source: dirt/holders cause defer/quarantine; a successful reset aborts sequencers, detaches at the published SHA and verifies cleanliness. Pending/Blocked tasks keep their slot; there is no automatic task timeout that safely frees it.

**Deploy is admitted as an ordinary task, but a slot is still a linked worktree.** Host-changing scripts retain their own canonical-checkout guards. `scripts/restart-apphost.ps1:94–100` refuses a linked checkout without explicit override; `scripts/deploy-server2.ps1:76` documents no worktree override. Slot selection supplies no host authority, Docker volume ownership or canonical checkout. A deployment that leaves helpers running in the slot defers release or quarantines it through holder detection. Neither script execution nor a real Deploy canary was performed here.

**Concurrency:** the runner wraps slot mirror/release/cold operations in its mutation lock (`PhoneHomeCommandDispatcher.cs:465–492`), while the pool records Leasing and increments generation under its semaphore (`RunnerWorkspaceSlotPool.cs:158–205`); release checks slot/generation/task (`:328–363`). This is the implemented same-slot protection, not a live stress-test result. Capacity + retained/Blocked leases can exhaust the pool despite free session seats.

## Local reconstructions, without product changes

Three bounded local Git checks ran in `.antiphon/evidence/card-0824/scratch/` with a **local bare repository** as origin. No GitHub refs or live workspaces were mutated. These reconstruct the exact Git portions of the implementation; they are not full pool integration tests.

```text
re-cut Merge:
  base      5ba440103e9f5573ef2f96c741397fdfaf45db8f
  first cut 19e4d72fabeac3da7df7b7b0e4031c451db23521
  published 3a71625f93515c9233f2a6916e007432e2afcd80
  first-cut tree == published tree: true
  rev-list --max-count=1 <HEAD-reflog+HEAD+task-tip> --not --remotes=origin
    => 19e4d72fabeac3da7df7b7b0e4031c451db23521
  current UnpushedAsync therefore reports an unpublished commit

release cleanup namespace, after deleting only the two task refs:
  remaining refs/heads/master
            refs/heads/merge-scratch
            refs/heads/published-merge

warm clean -ffdx with the exact four keep exclusions:
  server/bin, server/obj, client/node_modules, client/dist files kept: true
  .antiphon/old-task.md and scratch.tmp removed: true
```

The keep exclusions are source-defined, not invented here: `src/Antiphon.SessionRunner.Contracts/WorkspaceSlotContracts.cs:97–122`. There were **3/3 successful local checks**, no builds, no TUnit runs and no live canary. Their success does not certify all roles or the release service.

## Existing operational controls and the limit of an enablement procedure

**There is no complete safe all-role enablement command for this binary. Do not apply the following partial controls as if they completed CARD-0824.** They document the existing mechanism for the caller's eventual operational step. The all-role/ref-only blockers above need the next planning stage first. No fix design is supplied here.

1. **Read the actual owning host's effective values first.** On the observed desktop control plane, inspect only the named non-secret keys and their provider precedence in canonical `C:\src\Antiphon\server\appsettings.Development.json`, server user secrets **`antiphon-server`**, and server launch environment. Do not list all secrets. The AppHost secret store is a different store and does not forward arbitrary options. There is **no PUT body or general operator settings API** for these flags (`server/Api/Endpoints/SettingsEndpoints.cs:15`; `docs/antiphon-api.md:1223`). Record values or absence individually. If cutover has since occurred, use the Linux server's actual Compose configuration and restart owner, not the desktop path.

2. **Runner support is independently restart-bound.** Existing deployment transport accepts the exact LF assignment `PhoneHome__WorkspaceSlots__Enabled=true` once in `/home/mc/antiphon-server2/secrets/stack.temp.env` for temp, or `.../secrets/stack.env` for standing. Missing means false; malformed, duplicate or CRLF assignments refuse. `docker-compose.server2-runner.yml:65`, `scripts/c590-remote.sh:950–989,1095` and `docs/docker-stack.md:770–795` document preservation and container recreation through the named `deploy-server2.ps1` phases. A saved-file edit alone does nothing. A rollout must preserve live sessions using the normal temp/standing drain sequence; do not restart a busy standing runner merely to flip this key. CARD-1750 is the existing activation card. Read its whole report before dispatching a retired temp runner.

3. **Server support requires an owning-server restart.** The supported desktop control is `dotnet user-secrets set 'Delegation:WorkspaceSlotsEnabled' 'true' --id antiphon-server`, or merging the equivalent boolean into the ignored server Development JSON. Keep `WorkspaceSlotRoles` in that reloadable JSON with no higher-priority same-key secret/environment override. Example closed-gate fragment, merged without replacing other settings:

   ```json
   { "Delegation": { "WorkspaceSlotsEnabled": true, "WorkspaceSlotRoles": "none" } }
   ```

   From the independent canonical desktop checkout, existing restart command is `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-intended-active-rc-sha>`, after checking restart/launch locks and checkout HEAD. No `-AllowWorktree`. Confirm `/api/version` matches that intended rc SHA. This is a server/AppHost restart, not the runner switch. For a Docker API server, use the owner `scripts/restart-linux-server.ps1` and actual Compose environment; environment configuration is not hot-reloaded (`docs/docker-stack.md:810–816`). Which restart is appropriate must follow the then-current host state.

4. **The role control is one string.** The existing documented canary sets `WorkspaceSlotRoles=Plan` or `Plan,Investigate,TestDesign,Docs`, then widens to `*`. That is the historical procedure in CARD-1750 and amendment §4; the November 14 decision replaces its role waves with all roles in one step. **No current replacement value satisfies that decision.** Do not call `*` all fourteen. After the implementation supports that decision, the first observed real task can be an already-authorized ordinary Plan task; the next stage must retain the role decision rather than silently reverting to subset rollout.

5. **Ref-only is a separate server gate, not a runner requirement.** The defined configuration shape is:

   ```json
   { "Delegation": { "RefOnlyDesktop": {
     "Enabled": true, "RunnerIds": ["server2"], "Roles": "*"
   } } }
   ```

   Today this is **not an executable producer-enablement procedure**: S5b-3 is absent and `*` still excludes Mutation. The approved recommendation is ref-only, not desktop slots, and is no longer an open decision. Ref-only can work with either ordinary runner mirrors or runner slots; enabling S1 alone leaves a real desktop checkout per ordinary runner task. The existing S5b rollout requires S5b-1/-2/-3 ordinary Review, native acceptance, migrations, effective S4a Enabled/Execute false, then a server2 acceptance sample (`...s5b-ref-only-plan.md:517–547`). Existing recorded mode must govern settlement, artifacts, continuations, land and retirement, even when the admission gate closes. Do not convert existing Checkout tasks by switching a flag.

6. **S2 remains a separate operation.** It is not necessary for slot reuse. The landed enablement gates are in `docs/orchestration-loop.md:2766–2830`: all listed volumes drained with complete preview pages and accepted kept reasons, no custody quarantine/Releasing slot, native test evidence, cutoff set to enable time, 24h delay restored after any expressly commissioned disposable-branch canary. No drain proof or effective live S2 values were obtained here. Do not include automatic remote-branch deletion in a slot flag flip.

## Canary evidence and rollback using existing contracts

After the missing implementation/acceptance is resolved, the evidence to demand is already defined by the plans. This investigation executed none of it:

- Catalogue advertises `workspaceSlotV1` on the intended runner. The task's `WorkspaceSlotDecision` event names the preparation and admitted role. Its runner cwd is `/work/worktrees/ws-NN` or `ws-NN-dK`, and the ledger binds the same task/preparation/generation. Exact resolved base SHA and branch agree. A session seat response alone is insufficient.
- For the first released task, wait for its session seat to stop, settlement sync to confirm, and no live follow-up; the release service polls at one minute. Task slot release timestamp is recorded, RemoteWorktreePath cleared, ledger Free/detached/published, task-local refs removed. The next ordinary task increments generation and can reuse the same directory. Native `bin/obj` files survive a warm reset; prior `.antiphon`, deny hooks and source dirt do not. Check any auxiliary Merge refs independently.
- For ref-only, the essential signal is **zero new desktop directories and registrations**, an owned recorded binding/ref rather than a directory, correct source/creation SHA and exact artifact bytes. A task that merely says it used a slot has not proved S5b. The approved S5b acceptance sample is at least **20 tasks over 24h**, with restart/retry, blocked reply, artifact bundle, concurrency and two lands. S1's existing acceptance is at least **48h and 40 released leases**, warm-reset samples, an explicit cold reset only for a Free slot, and 12-hour ledger/retention observations (`...s4-s5-amendment.md:378–437`). These are retained acceptance requirements, not evidence collected here.
- Existing stops include leakage, missing pushed work, release-stuck/rehome-failed/pool-degraded, repeated custody quarantine, warm-reset p95 >30s, lease RPC timeout, unavailable >10min, retention touching slots, and Held or >1h Pending sync debt. S4b remains missing, so no automatic slot attention coverage can be assumed.

**Safe soft rollback:** set the **server** `Delegation:WorkspaceSlotRoles` to the string **`none`**, while keeping `Delegation:WorkspaceSlotsEnabled=true` and runner `PhoneHome:WorkspaceSlots:Enabled=true`. JSON monitor reload applies to new preparation decisions; already-persisted decisions may still finish as slots. Verify a new eligible preparation records `reason=roles_none`. Existing running tasks keep their directories and the release loop continues. Do not delete directories or clear leases. Only after no leased/releasing/in-flight preparation remains can flags be disabled through their owning restart/recreation. Server flag off stops the release loop; runner flag off removes its pool and release RPC support (`WorkspaceSlotReleaseService.cs:270–288`, `PhoneHomeCommandDispatcher.cs:188–208`).

**Ref-only rollback:** close `Delegation:RefOnlyDesktop:Enabled=false` or `Roles="none"` for new admissions, leaving schema/readers and existing mode-bound refs in place. That gate is independent of the S1 role setting. Recorded RefOnly handling does not become a checkout because admission closes (`RefOnlyDesktopGate.cs:9`, `RemoteWorkspaceService.cs:118,610,1134`). In the present source there is no producer to roll back, and no live RefOnly canary was proved. For a Docker server these environment flips require the graceful server restart owner; they are not instant JSON reloads. A restart is not permission to kill running runner sessions.

## Disk, retention and retirement risks

Measured foreground command output, approximately 08:19Z:

```text
df -h /work: 705G total, 529G used, 148G available, 79% used
du -sh /work/worktrees:                         176G
du -sh /work/worktrees-retiring:               4.0K
du -sh /work/worktrees/task-9168c199:            247M
du -sh /work/worktrees/task-4cadee8f:            2.3G
  server/bin 173M; server/obj 53M
  tests/Antiphon.Tests/bin 918M; obj 106M
direct worktree directories 120; ws-* directories 0
```

`du -h` values are binary units rounded for display. This is one built sample, not a measured slot p95 or a promise about large mutant/checkpoint tasks.

Runner default pool settings are `Headroom=4`, `MaxOverflowSlots=4`, `OverflowWaitSeconds=10`, `SlotBytesEstimateGiB=3`, `PoolDiskBudgetGiB=64`, `IdleRetireHours=72`, `LingerRetries=3`, `MaxQuarantinedDirectories=4`, `MaxRehomesPerSlotPerDay=3`, `ColdResetSchedule=Weekly`, Sunday 02:00 UTC, evidence cap **256 MiB/task**, retention **7 days** (`PhoneHomeWorkspaceSlotSettings.cs:7–38`). These are source defaults, not a read of an active pool: there is no pool today, and effective non-flag overrides were not independently queried from the owning runner process.

At capacity 10 the target is **14 base slots**, grown lazily, plus **4 overflow**, not seven slots for seven tasks (`RunnerWorkspaceSlotPool.cs:63,153–214`). At the observed 2.3GiB sample, 18 slots are approximately 41GiB before quarantine/evidence; at the 3GiB estimate they are 54GiB. This overlaps the existing 176GiB population during migration. The plan's entry floor is 84GiB free, so the measured 148GiB clears that one condition only. Base slot growth is **not governed by the 64GiB overflow guard**, and that setting does not bound later output growth. Overflow requires free space at least `max(20GiB, volume/10)+2*estimate`, and measured active/quarantine bytes plus estimate <=64GiB (`:242–250`). Fourteen long-held leases can therefore consume the base target despite only seven/eight live tasks. Free overflow retires after 72h only while active count exceeds target (`:1156–1171`). Quarantine persists in place and can consume more disk; more than four open quarantines degrades the pool (`:95`).

The shared **build** broker and **workspace** pool are different resources. Eight tasks can have workspace leases while only four build/test drivers hold build leases. Mutant loops re-lease the broker per build, not per entire loop (CARD-1627). Checkpoint outputs remain fresh by their custody contract; preserved native output does not make checkpoint builds incrementally reusable. Do not replace checkpoint output policy to claim a slot performance win.

Retention safety is present in source: server candidates reject slot IDs (`RunnerWorktreeRetentionService.cs:750–764`), runner confines leaf names and refuses `ws-*` (`RunnerWorkspaceRetention.cs:227–243`), and ledger ownership is protected with fail-closed unreadable ledger (`:487–498`). Runner operations serialize with slot preparation. These are source mechanisms, not proof that an executing retention run never touched a lease. Read all existing run pages; a retention preview is a **POST that writes run/cursor evidence**, so none was invoked under this task's GET-only live restriction.

Related card reads and current limits:

- **CARD-1750, Review:** existing slot activation card; November 7 state and old procedure, not proof activation happened. Its old Mutation exclusion and role waves conflict with the new requested outcome.
- **CARD-1677, Done:** runner retention capability is now active; execution rows and effective age remain unverified here.
- **CARD-1894, Review:** paid Ready debt held legacy trees. Repair **`0d6e311e49ace3b489430f0baac516994f679a2a`** is on master; `SyncDebtRetentionPolicy.Unpaid` is consumed by both policies (`RunnerWorktreeRetentionService.cs:768`, `DesktopLegacyReleasePolicyService.cs:782`). It is **not an ancestor of the running `ab7af12...`**. The running source still includes Ready in OpenDebt at lines 48–49/770. No repaired tick is claimed. Slot release itself checks Pending/Held, a different predicate (`WorkspaceSlotReleaseService.cs:259`).
- **CARD-1901, InProgress:** accepted November 14 retirement-audit plan; implementation task was in flight in the observed broker, not landed/activated evidence. Its real incident is 16 reflog-only commits from a re-cut Merge blocking retirement after salvage/patch-equivalent publication. The decision explicitly rejects patch-id authority and reflog expiry, and requires fresh exact origin salvage proof. Do not assume the changing audit accepts a slot's local salvage/quarantine. The slot UnpushedAsync currently considers origin remote heads, not a salvage namespace; changing the retirement auditor alone would not change slot release.
- **CARD-0831, Backlog:** historic unpublished-temp-work loss remains the reason to preserve the current recycle refusal gates. A custody quarantine must block volume retirement; it is not permission to remove a slot. S4b absence also means no successful slot inventory/attention acceptance has been observed.
- Board search `card.ps1 search slot -Board Antiphon -All` completed enumeration: **228** hits; a separate `ref-only` search found **2** hits. These are completed enumerations with previewed descriptions; relevant cards above were read in full. No absence claim is made from a capped preview.

## Verdict and remaining uncertainty

**Confirmed mechanism:** runner support is disabled on the observed server2 process; ordinary tasks therefore still receive per-task mirrors. Independently, the implementation excludes Mutation/SourceLanding and lacks the ref-only producer, so no current setting can fulfill the fourteen-role plus ref-only decision. Merge's re-cut publication and auxiliary-ref behavior was reconstructed with exact Git evidence. Existing slot flags and role reload explain both enablement and the safe rollback, but they cannot close those code gaps.

Remaining uncertainty: exact effective server/desktop config overrides; current ordinary Review/native/Mutation acceptance for the slices; full active rollout/drain state; actual slot release/reuse under sustained load; current retention run outcomes/age; a real Deploy task's host path; current S2 activation/drain proof; and caller activation of the master-only CARD-1894 repair. Resolve configuration uncertainty by a caller read of the owning desktop/server providers, and runtime uncertainty by the existing plan acceptance evidence. The 404 and call-graph checks establish the named missing pieces on this pinned master; they do not exclude changes on other unlanded branches.

**Not done, noted:** a separately commissioned Plan must reconcile the all-role decision with Mutation/SourceLanding custody, missing S5b-3 production admission, and Merge reuse evidence before activation; no implementation or fix design was performed.

--- next stage ---
next: plan
handoff: Plan the confirmed all-role blockers: Mutation/SourceLanding exclusion, absent S5b-3 ref-only producer, and Merge reflog/auxiliary-ref reuse; retain the operator's one-step all-role and ref-only decisions, existing custody/rollback contracts, and require owning-host config reads plus acceptance before activation.
artifact: docs/investigations/2026-11-14-card-0824-slot-enablement.md
