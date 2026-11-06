# CARD-0824: worktree status, counts, and activation evidence

Investigation `39da4445-ccb1-4124-ac57-41a2c9d621ff`, 2026-11-06 UTC. Source baseline and fetched `origin/master`: **`df1fc145181a3bedf444f9113f822f30d3b136f6`**. Source line citations refer to this SHA unless explicitly qualified. No product code, settings, cleanup, build, test, restart, deployment or desktop command was run.

## Operator answer

- **It is not completely done. New ordinary runner tasks still get a desktop worktree and a runner worktree.** This investigation's own persisted dispatch event records the desktop checkout; the investigator is running in its separate runner checkout.
- **Runner slots are implemented but inactive.** Both production server and server2 report the November 3 build `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`. The runner lacks `workspaceSlotV1`; the observed worktree root has no `ws-*` directory or slot ledger.
- **At 13:35:05Z there were 320 task worktrees:** 197 for terminal tasks completed more than 24 hours earlier, 114 for newer terminal tasks, and 9 for open tasks. One additional hidden directory is evidence, not a task worktree. `/work` was 54% used, with about 317 GiB available.
- **Automatic runner retention already exists and ships on for server2, but it is absent from the running build.** The accumulation is an activation gap, not evidence that master ships retention disabled. Its checked-in age is 12 hours; the operator's manual-cleanup rule is more than 24 hours from task completion.
- **A small reusable runner pool means 14 target slots and up to 4 overflow slots at the current capacity of 10.** Legacy trees, excluded task kinds/repositories and quarantined old directories can remain alongside them. A 24-hour sweep alone cannot give a handful: even removing all 197 aged trees would leave 123 task trees in this snapshot.
- **The desktop growth cut is S5b-3, which has no producer on master yet.** S5b-1 has landed; S5b-2 is in Review and still has recorded verification obligations. The decided desktop half is ref-only, with no desktop checkout or desktop slot. Desktop-run task slots, S5a/S5c, remain unimplemented.
- **No live desktop census is claimed.** The last readable desktop cleanup report ended November 5 with 249 Git registrations and 275 held/deferred observed paths. Those are different populations and are not today's count. Cleanup and rollout decisions remain with the caller; the existing server2 cleanup task was already Queued.

## Confirmed mechanism and observation boundary

**Confirmed:** fresh ordinary Worktree dispatch still creates per-task coordinates; runner slot admission is inactive; newer runner retention is not loaded; ref-only desktop production is unfinished. This is reconstructed from source, recorded task events, live build/capability reads, commit ancestry and a filesystem/task-row join. It explains continuing growth. It does not establish that any particular aged tree is safe to remove.

Read-only API requests used the inherited `ANTIPHON_API` and task-token header, without printing credentials. The task list was explicitly scoped to project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` with `includeChecks=true`, no status or time filter. It returned **7,961 items**, an explicit scope envelope and excluded-population counts. Every one of the 320 task-directory prefixes matched exactly one returned full task UUID, all with `runnerId=server2`; no inference about excluded tasks was necessary. The list implementation has no page/Take cap (`server/Application/Services/AgentTaskService.cs:2675`, `:2760`, `:2787`).

| Observation | Retained result |
| --- | --- |
| `GET /api/version`, approximately 13:33:51Z | `version=919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`; `land-v2` present |
| `GET /api/session-runners/server2/status`, approximately 13:34:24Z | same `buildVersion`; `available=true`, `dispatchEligible=true`, `acceptingNewWork=true`; store `f519bd08-e53a-47d1-adb1-2ab33475446f`, boot `052b11d6-7b20-436f-baad-3dd18955c169`; 10 sessions, 3 queued tasks |
| `GET /api/session-runners`, 13:33:51Z | server2 capacity 10, occupied 10, **no `workspaceSlotV1` or `workspaceRetentionV1`**; desktop capacity 2, occupied 0, available/eligible; temporary runner retired/unavailable |
| `GET /api/runner-defaults` | revision 2; global runner `server2`; no kind overrides; operator default recorded September 26 |
| Direct runner filesystem, 13:35:05.149Z | 320 directories matching exactly `task-[0-9a-f]{8}`; no suffixed task leaf, no `ws-*`; `.antiphon-test-evidence` is the one other entry; `/work/workspace-slots` absent |
| `git -C /work/repos/antiphon worktree list --porcelain`, approximately 13:37Z | 326 registrations overall; exactly 320 under `/work/worktrees`, all task leaves; zero slot registrations. The six outside-root registrations were not acted on. |
| `df -h /work`, 13:33:38Z | volume 705G, used 360G, available 317G, use 54% |
| `GET /api/settings` | HTTP 404. The API route map documents provider/template settings, not a generic configuration dump (`docs/antiphon-api.md:1223`). No host-override value is invented from this response. |

The brief's earlier 319/53% observation and this 320/54% measurement are separate live samples. The brief's stated 13:35Z reference is not a clock anchor for this investigation. Directory arrivals and other host activity can occur between samples.

Trusted SSH from this container to `mc@server2` failed **host key verification**. Verification was not bypassed. The census instead used direct access to the assigned server2 runner mirror and its `/work` volume, as permitted by the brief. The container hostname was `dfcc116be984`. A local `127.0.0.1:8080/capabilities` read also reported build `919d8d42…`, but its store ID was `897f354b-0f0d-4032-93a3-d3addffccbe2`, different from the phone-home status store. Therefore the authoritative phone-home activation assertions above use the server's registered-runner routes, not an assumption that those two stores are identical.

## What was decided

The original plan and decisions landed as `3b3501f44cc0833e2264cd6cd375c586ede69eb2` and `8bdad1cf42cd2c7f43e83d7d4273215170a768d6`. The operator's H-1..H-8 mapping is in `docs/superpowers/plans/2026-11-02-card-0824-per-slot-worktrees-plan.md:846` and CARD-0824 revision 6, created `2026-11-02T06:11:49.403158Z` (revision reason records the append; the current card contains the mapping).

- Hold at exhaustion, but allow bounded reusable overflow; quarantine an unusable directory and re-home its slot to `ws-NN-dK`.
- Use native project `bin/obj`, without custom output paths, and retain native build output during the normal warm reset. The instruction to avoid a default reset concerns **wiping build output**; source is still reset to the new task's exact SHA. Explicit, scheduled and pre-rc cold resets wipe outputs. The runner's default schedule is weekly, Sunday 02:00 UTC.
- Keep evidence before reuse, with the agreed seven-day/256 MiB task cap; delete an origin source branch only after confirmed publication, with 24-hour grace and separate enablement.

The November 3 amendment landed at `d52c56fa950e773ce8d28073a057c0b828089539`: S4 runner cleanup/alarm is supplied by CARD-1677; S4a is desktop legacy release; S4b is slot inventory/attention; original S3 becomes S5a core plus S5c desktop-run consumption (`docs/superpowers/plans/2026-11-03-card-0824-s4-s5-amendment.md:17`). Card revision 27 (`2026-11-03T21:06:08.32369Z`, UUID `01a1a33b-cf43-7b4a-a1c6-6a0c515ab5cf`) explicitly chose **S4a on by default**, overriding the amendment's prior off default; it also accepted warm leases during rc while fencing cold resets.

The November 5 ref-only decision supersedes the amendment's desktop-slot version of S5b. Its landed plan is `c794f592986cedf6c1d4b84416d7b1f7cdf842b2`, `docs/superpowers/plans/2026-11-05-card-0824-s5b-ref-only-plan.md:1`, `:66`. It leaves S5a/S5c for desktop-run tasks. No competing CARD-0824/slot specification was found under `docs/superpowers/specs`; the cited plan and amendment own this slice map.

## Landed versus available in an rc versus active

Every listed master SHA passed an ancestry check against fetched `origin/master=df1fc145…`. Rc membership below means actual commit ancestry, not a task's Succeeded state or similar subject. At the fetch, the relevant rc tips were:

```text
rc/2026-11-03 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd (actually loaded)
rc/2026-11-04 f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0
rc/2026-11-05 1470340559889c2985ea722173181ea25c51b47c
rc/2026-11-06 5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a
rc/2026-11-07 1038d07e66a54b630a1bc1d394923bc7049b73dd
```

Task `aa3c54ff-965c-4ecc-997e-434578c8debd`, cutting rc/2026-11-08, was Dispatched in the snapshot. A task title does not establish that a new rc exists or has been activated.

| Slice | Landed master evidence | Rc membership and actual activation | Remaining |
| --- | --- | --- | --- |
| S1 runner slots | runner `8395c4a431db27680e306aacb0ef46f1dbafbaa8`; server `e06ef30735e57772e5d20255ddf91cebeaf76267`; release repair `7ab9aa36cf88870410aada059071670c87528213` | All are in rc/11-03 and later listed rcs, and in the loaded binary. **Inactive**: registered runner does not advertise slots; no pool on disk. | Enablement/canary and recorded acceptance; legacy drain remains separate. |
| S1c role gate | `31569a95c408c118e51eca25257c9f4e84974fbd`; follow-up `81e732dd42252ae1314840a87c98603faeeca25f` | Role implementation is in rc/11-05 through 11-07; absent from loaded 11-03. | Loaded role-gated canary and observed decision events. Historical investigation `e438ae14` retained receipt limitations; no new qualification is claimed here. |
| S2 source-branch deletion | implementation `f95e7e03d7325d45bd905b663b403f474cd257d3`; repair `99747e3fd8f453f804f7e656690512082f1c0815` | Implementation in rc/11-05 through 11-07; absent from loaded build; ships off. | Per-volume drain, explicit cutoff/runner allow-list and activation acceptance. This deletes refs; it is not the runner directory sweep. |
| S4 runner legacy cleanup, CARD-1677 | engine `1b6aaf36bc054d5a9c6b4246dae6a4a3459257c4`; on-default/guard change `2921957643833588c201a9fcc0c84bd98676242e`; alarm `bb0947982a3c170c14ee93d72b4a4f635436bb40` | On-default change in rc/11-04 through 11-07; absent from loaded build and runner capability list. | Activation and census of actual kept/removable reasons. Later name guard `97e77f9f1fd6f71e2719bc661b48f8f89f129029` starts in rc/11-05. |
| S4a automatic desktop legacy release | initial `3eb9d002738ae707b2247678400fdaba9cabbb52`; repaired integrated stack tip **`fed5899951daa3c66be3dee31355e6261286dc01`** | Initial implementation in rc/11-05 through 11-07; integrated November 6 repair stack is in **none** of those fetched rc tips. Neither is loaded. | Exact activation scope, remaining native/Unit/Mutation obligations and CARD-1737 witness repairs; checked-in on-default is not proof of operational readiness. |
| S4b slot inventory/attention | Amendment only; no `WorkspaceSlotInventory`, `BuildWorkspaceSlotItems`, or slot-list consumer implementation found on master | No implementation SHA/rc/active service. Existing cold-reset/record-release endpoints are not S4b inventory. | Entire slice from amendment `:293`, including exposure of existing ledger events. |
| S5a Windows desktop pool core | No `DesktopWorkspaceSlotPool` implementation on master | No implementation SHA or effective feature setting. | Desktop pool core/native acceptance; ref-only S5b does not require it. |
| S5b-1 ref-only identity/land/cleanup | coordinates `7bf9f88df07678c9eb096a0ac679791c1ca78936`; land `22a6472afb78c45a7104267ad7a9e18b4748505f`; cleanup `ea89b8436312369ec9d1affbfa0b443c3a0f572f`; completed slice `2fcbd3ab525f2133cb5840bb8f1291a8eb5f5ee3` | Completed slice in rc/11-07; absent from loaded build. **No producer**, even on master. | S5b-2/3 and outstanding verification; landing the consumer slice alone cannot stop desktop growth. |
| S5b-2 settlement/read/report/continuation consumers | Candidate **`de1963dd09e5c12951aa1a178cb833c85e69bda4`**, `feat/card-task-204a327a`, is **not** an ancestor of master or a listed rc | Review task `fd0ebdc3-4a81-48d8-8ca5-b3e174b89746` was Dispatched; no result at observation. No activation. | Review disposition and recorded incomplete ordinary/native verification; still no producer by design. |
| S5b-3 ref-only producer | Plan `:320` only; production dispatcher on master still cuts the desktop checkout | No implementation SHA/rc/activation. | The producer is the first slice that actually stops new desktop checkouts for admitted runner tasks. |
| S5c desktop-run slots (old S3 consumer) | Amendment only; no desktop slot consumer on master | No implementation SHA/rc/active flag. | Windows desktop-run consumer, locks/long paths, rc fence and native acceptance. |

The runner secret-floor repair `cfcfa33cd46037eeebbaf47885dcf9885a1aac5e` and nested-secret witness `9a6a92e39fde5c93457f46b992f0237994f3eb7a` are on master and rc/11-07, but not loaded. They must not be attributed to rc/11-04's original retention engine.

Stored outcome qualifications, not fresh tests:

- Merge task `fee547cb-9814-4102-a202-998c216a7bfc` records publication of the integrated S4a stack at `fed589…`, **no activation**, 1,128 passed / 1 failed / 15 skipped, with caller-accepted inherited-load attribution and native/Unit/Mutation obligations retained.
- S5b-2 Code task `204a327a-f5fa-4f58-90bd-88435d76e5c2` is Succeeded as a task but its report explicitly says **Final verification incomplete**: CP-1..6 106 passed; broad affected scope 1,384 passed / 1 failed / 1 native skip; unresolved cancellation attribution and Unit/native/manual obligations; 161 PCs pending. Its handoff was `next: code`, although the subsequent Review was already dispatched. This investigation does not upgrade that to a green gate.
- S4a witness investigation `f565efa1-4624-4818-83e2-c3cf2c6e7c0f` retains 15 waived kills and a masked X-26 control. CARD-1737 Plan `85a330bf-275f-4079-a042-ec54eeabf69d` records 13 repair rows, 27 closed preservation bindings, 3 ledger corrections, 3 Windows obligations and 2 decisions. Its R1 Code task was Dispatched. These newer records supersede an assumption that the original S4a landing completed all acceptance.

## Settings: defaults and what was actually observable

| Control | C# and checked-in host configuration | Live conclusion |
| --- | --- | --- |
| `Delegation:WorkspaceSlotsEnabled` | C# false (`server/Application/Settings/DelegationSettings.cs:703`); no override in tracked server appsettings or compose files | Exact server override unavailable without desktop config access. End-to-end reuse is inactive. |
| `PhoneHome:WorkspaceSlots:Enabled` | C# false (`src/Antiphon.SessionRunner/PhoneHomeWorkspaceSlotSettings.cs:9`); not set by tracked compose/appsettings | Phone-home `workspaceSlotV1` is added only when enabled (`PhoneHomeCommandDispatcher.cs:144`); live registered runner lacks it. Thus the runner slot gate is effectively off. The local process's selected slot env keys and `/app/appsettings.json` also supplied no slot override; they are supporting observations, not a substitute for registered-runner identity. |
| `Delegation:WorkspaceSlotRoles` | `"*"` in C# (`DelegationSettings.cs:711`); validated scalar, `none` closes new leases | Not loaded; no effective live role-list value is claimed. Existing releases require the outer server and runner flags to remain on (`WorkspaceSlotReleaseService.cs:266`). |
| Slot sizing/reset | runner settings: Headroom 4, MaxOverflowSlots 4, PoolDiskBudgetGiB 64, IdleRetireHours 72; Weekly cold reset Sunday 02:00, 256 MiB and 7-day evidence (`PhoneHomeWorkspaceSlotSettings.cs:12`) | Pool inactive; these are defaults, not a live slot inventory. |
| `Landing:DeleteSourceBranchAfterPublication` | false; delay 1440 min, cutoff null, runner list empty (`LandingSettings.cs:49`); no tracked activation override | Feature absent from loaded binary; no effective live override read. |
| `RunnerWorktreeRetention` | Enabled/Execute true in C# and `server/appsettings.json:243`; C# runner list empty, JSON list `["server2"]`; age 12h; cap 25/30-min tick; backoff 60min to 24h | Feature absent from both loaded server commit and registered runner capability list. **Not established as a live `Enabled=false` override.** |
| `WorktreeLegacyRelease` (S4a) | Enabled/Execute true, age 48h, cap 25 hourly at minute 15 (`server/appsettings.json:230`; `WorktreeLegacyReleaseSettings.cs:18`) | Absent from loaded binary. November 5 plan explicitly requires operational OFF pending safety acceptance (`2026-11-05-card-0824-s5b-ref-only-plan.md:54`, `:519`); actual desktop override was not inspected. Current validator rejects age below 48h (`WorktreeLegacyReleaseSettings.cs:66`), so the manual 24h instruction is not this default policy. |
| `Delegation:RefOnlyDesktop` | Enabled false, RunnerIds empty, Roles `none` (`RefOnlyDesktopSettings.cs:7`); no tracked override | Not loaded, and no producer on master. Turning on the gate in the current slice cannot create ref-only tasks. |
| `WorktreeResidue` | Enabled true, Execute false, daily 10:00 Europe/London, minimum settled 120min, cap 25 (`server/appsettings.json:218`) | This pre-existing preview/retirement machinery is separate from CARD-1677. A live Execute override was not read. |
| `WorktreeCleanup` | Classification/retention settings, **not a scheduler or pool enable flag**; disposable outputs, retained `.antiphon` evidence, protected secret floor (`server/appsettings.json:266`; `WorktreeCleanupSettings.cs:6`) | No live settings dump. These settings cannot change per-task allocation into slots. |
| `RunnerSlots` / seats | `RunnerSlotService.cs:34` lists runner sessions and capacity; not workspace directories. No `RunnerSlots` settings section was found. `TerminalRunnerSeatRelease:AutomaticEnabled` defaults false (`TerminalRunnerSeatReleaseService.cs:17`), with no tracked appsettings/compose override; it is another independent gate. | Catalogue capacity 10 and occupancy 10 measure session seats. The live automatic-seat override was not inspected. Neither setting enables workspace reuse. Build slots are a third, separate concurrency gate. |

Tracked `server/appsettings*.json` and compose searches found no activation override for slots, ref-only or source deletion. Untracked desktop JSON/environment settings remain unknown. There is no evidence supporting the stronger claim that all host configuration values were read.

## Today's dispatch versus the slot model

`scripts/delegate.ps1:1230` serializes `-Worktree` as workspace `Worktree`; its omission also defaults fresh ordinary workers to Worktree (`:1228`). It is not a request for a reusable workspace slot. `-ColdWorkspace` sets the separate cold-reset property (`:1289`).

On master, `server/Application/Services/AgentTaskDispatcher.cs:6000` calls `DelegationWorktreeService.CreateForTaskAsync`. The ordinary path adopts an existing checkout for the **same task** or mints a branch and calls checkout creation (`DelegationWorktreeService.cs:414`, `:445`). Thus retries may reuse their task's checkout; it is not one checkout per card and not cross-task reuse. Remote preparation then chooses a mirror name `task-<id8>` (`RemoteWorkspaceService.cs:107`). The runner's normal path constructs that directory and adds the worktree with `worktree add -B <branch> <path> <sha>` (`src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:106`, `:138`, `:309`).

The stored task evidence makes the current path concrete:

```text
GET /api/agent-tasks/39da4445-ccb1-4124-ac57-41a2c9d621ff
summary.workspace=Worktree
summary.runnerId=server2
summary.worktreePath=C:\Antiphon\worktrees\card-task-39da4445
summary.worktreeBranch=feat/card-task-39da4445
worktreeBaseSha=df1fc145181a3bedf444f9113f822f30d3b136f6
events[Dispatched] at=2026-11-06T13:31:57.823311Z:
Worktree created at C:\Antiphon\worktrees\card-task-39da4445 on feat/card-task-39da4445 from df1fc145181a3bedf444f9113f822f30d3b136f6 (Explicit) (no merge target — branch left for review)
events[Held] at=2026-11-06T13:32:28.388832Z:
Held: remote workspace preparation is in flight for runner 'server2' (branch push and mirror requested 2026-11-06T13:32:28.3850071Z); the task dispatches once the mirror is recorded.
events[Held] at=2026-11-06T13:32:40.280647Z:
Held: prepared workspace is ready; awaiting dispatch admission.
direct execution cwd=/work/worktrees/task-39da4445
```

With admitted S1 reuse, a request takes the slot arm instead (`RunnerWorkspaceService.cs:109`). Selection requires server enablement, a binding, the runner capability and the role decision (`RemoteWorkspaceService.cs:215`). Pool target is `Capacity + Headroom` (`RunnerWorkspaceSlotPool.cs:60`); the overflow count bound is enforced at `:171`. At capacity 10 and defaults, that is **14 base/target slots plus at most 4 reusable overflow slots**, not 18 new task directories per batch. A quarantined old directory remains while the slot is re-homed; legacy worktrees, role exclusions, Mutation/SourceLanding and secondary-repository paths are not automatically converted. Pool exhaustion holds the task rather than silently creating a legacy fallback for an admitted slot lease (original plan `:146`).

Warm cleanup keeps `**/obj/`, `**/bin/`, `**/node_modules/`, `/client/dist/`; cold cleanup omits those exclusions (`src/Antiphon.SessionRunner.Contracts/WorkspaceSlotContracts.cs:94`). This does not make checkpoint builds warm: the original plan distinguishes checkpoint-owned cold outputs from direct native incremental builds (`2026-11-02-card-0824-per-slot-worktrees-plan.md:48`).

S5b is orthogonal. Its admitted runner task keeps a branch and durable identity **without a desktop directory, index, registration or slot**, while using either a normal runner mirror or S1 runner slot. S5b-1's master gate is registered but has no production `Admits` caller; the dispatcher still follows checkout creation. The plan deliberately puts consumers before the S5b-3 producer (`2026-11-05-card-0824-s5b-ref-only-plan.md:193`, `:245`, `:320`).

## Counts and the 24-hour rule

Filesystem snapshot **2026-11-06T13:35:05.149Z**, joined to the approximately 13:34 API snapshot. Age is `(snapshot UTC - task.completedAt) / 3600000`, strictly greater than 24 for the aged bucket; neither directory mtime nor file age was used. All task leaves were directories and matched uniquely. Full frozen row values are in the appendix.

| Owning task state | >24h terminal | Terminal <=24h | Open |
| --- | ---: | ---: | ---: |
| Succeeded | 180 | 92 | 0 |
| Failed | 5 | 13 | 0 |
| Canceled | 12 | 9 | 0 |
| Dispatched | 0 | 0 | 5 |
| Working | 0 | 0 | 1 |
| Blocked | 0 | 0 | 3 |
| **Total** | **197** | **114** | **9** |

The 197 aged rows include **12 Mutation-role tasks**, which CARD-1677's current classifier excludes (`RunnerWorktreeRetentionService.cs:360`). Other age-qualified tasks may have dirty/unpushed work, holders, debt, missing reports or pending lands; those facts were not individually certified. “197 older than 24h” is not “197 approved deletions.” No source or ignored secret content was opened for this census.

The nine observed open task prefixes are `0cc54c85`, `23abede2`, `39da4445`, `6569dd36`, `84d42fbe`, `8a9f92ba`, `aa3c54ff`, `e0c73157`, `fd0ebdc3`. The last two sample rows establishing the age distinction are:

| Full task UUID | Status | completedAt | Directory |
| --- | --- | --- | --- |
| `00a0b2d1-ec0d-4c2e-9907-1fc371e0bb64` | Succeeded | `2026-11-04T12:44:05.920555Z` | `task-00a0b2d1` (>24h) |
| `013b9ff7-b813-4986-8c7f-2a5b0fbdef95` | Succeeded | `2026-11-05T20:25:00.520615Z` | `task-013b9ff7` (<=24h) |
| `6569dd36-5266-4f91-b797-60e984e4be6c` | Blocked | null | `task-6569dd36` (open, never aged by filesystem time) |

The prior server2 cleanup report at `origin/feat/card-task-9e4c7963:docs/investigations/2026-11-05-server2-worktree-cleanup-2.md:1`, commit `556398e429ab5f696f94e6779056d4469d734365`, records **344 removed from 505**, leaving **161** at `2026-11-05T05:16:01.018Z`. Disk fell from 82% to 44%. That proves a manual reduction occurred; today's larger population is compatible with continued per-task creation and an inactive automatic sweep, not evidence that this cleanup failed.

The readable desktop report at `origin/feat/card-task-4b63abcc:docs/investigations/2026-11-05-desktop-worktree-cleanup-2.md:1`, commit `79d7eff97c54de55b41a4762d5da0a26f12e3008`, ended `2026-11-05T14:52:47.838683+00:00`: 675 observed paths initially, 400 removed, **275 held/deferred**, canonical registrations **649 -> 249**. Held/deferred reasons were 144 recent, 83 capped, 9 owner, 22 protected/outside scope, 17 unregistered (`:46`). These are historical values with broader scope than just direct task directories. Today's desktop total and today's desktop >24h/open partition remain **unmeasured** under the prohibition on desktop commands. API creation rows alone cannot establish surviving desktop directories.

Task `4c98a708-8d94-4f35-8bfb-ad9c2a306eee` was already Queued at observation, titled “Clean up server2 worktrees: terminal tasks older than 24 hours,” with no worktree yet. This investigation did not compete with or perform that cleanup.

## Existing cleanup machinery and its limits

1. **Runner mirror retention:** the server's `RunnerWorktreeRetentionService` selects terminal task rows and sends requests only to a live phone-home client advertising `workspaceRetentionV1` (`:317`). It checks completion age, path/branch identity, no slot, no SourceLanding/repair-source/Mutation, no land/debt/shared-path/live-session exposure and report preservation (`:338`). The job already has a 30-minute schedule (`server/Infrastructure/Agents/HangfireConfiguration.cs:72`; `server/Program.cs:1174`). No new scheduler is needed to make that existing mechanism periodic.
2. **Runner filesystem action:** `RunnerWorkspaceRetention.cs:293` proves branch, clean tracked/untracked state and HEAD publication; `:324` verifies live origin, then checks process holders and ignored-secret names. It moves an approved tree to `/work/worktrees-retiring` and calls ordinary `git worktree remove` without force (`:162`). Uncertain and refused trees remain as evidence and use backoff. This runner service does **not** call the server's `GuardedWorktreeRemoval` or consume the whole desktop `WorktreeCleanup` disposal policy.
3. **Desktop retirement/guarded removal:** a terminal row needs a release (`TaskWorktreeRetirementService.cs:341`); retirement calls the guarded worktree manager (`:481`). S4a automatically files such a system release. `GuardedWorktreeRemoval.cs:136` classifies protected/evidence/disposable ignored content, retains evidence and rechecks before removal. `WorktreeCleanupSettings.cs:31` protects secret-shaped names even under build directories. These are existing guards, not evidence that desktop automatic cleanup is currently running.
4. **Salvage is a separate fact:** CARD-1708's `WorktreeReflogSalvage.cs:9` preserves reflog-only commits before S4a deletion and bounds pruning to completed, aged releases. Runner retention checks `HEAD --not --remotes=origin` (`RunnerWorkspaceRetention.cs:315`) and leaves branch refs; no reflog-salvage invocation appears in that runner path. Therefore it is not proved that enabling runner retention alone implements every interpretation of the operator's salvage requirement. Existing salvage refs and ignored secret files were left untouched by this investigation.

**Why hundreds accumulate:** the actual server/runner SHA precedes the CARD-1677 retention implementation, the runner lacks its required capability, and fresh tasks still allocate their own mirrors. Retention being on in master's JSON cannot execute code absent from the loaded binary. S4a and source-branch deletion do not substitute for that runner path. The population remaining after a manual sweep grows again under the observed allocation model.

**What existing controls can and cannot accomplish:** CARD-1677 already supplies scheduled age-based removal, with `Enabled`, `Execute`, `Runners` and `MinTerminalHours`; the runner age validator accepts 24h. Its default is 12h, not the operator's current 24h manual rule. S1 supplies bounded reuse through the two slot flags and role gate. Age-based retention bounds the historical tail subject to guards and arrival rate; workspace-slot admission bounds new admitted checkouts by concurrency. Even a perfect 24h deletion sweep would leave **114 recent terminal + 9 open = 123** task directories in this census. No existing single cleanup flag can honestly be described as “turn it on and only a handful remain.”

## Remaining uncertainties / caller decisions

- Exact untracked desktop server/runner configuration overrides were not read. Inactive behavior and loaded build identity are established independently. Resolving the configuration values requires a caller-owned, narrowly filtered host-settings read; it does not require exposing the whole config or secrets.
- No runner-retention run/candidate rows were read: the live feature is absent, and child-token access to residue-run detail is explicitly forbidden by `TaskWorktreeRetirementService.cs:294`. A future caller census needs all pages, kept reasons and loaded capabilities before claiming automatic removal works.
- Fresh per-tree ignored/reflog/publication/holder/debt proofs remain the cleanup task's responsibility. Age counts and role exclusions are not deletion receipts. The runner HEAD check versus reflog-only salvage distinction needs explicit disposition before claiming full salvage-policy equivalence.
- Live desktop counts remain unknown; the historical report and dispatch event demonstrate history/current allocation, not today's desktop filesystem total.
- S5b-2 is in Review with an incomplete-source report; S5b-3, S4b, S5a and S5c have no master implementation. Their absent SHAs are not assumed from title search alone: the dispatcher/gate references and named implementations/ops were searched in the source.
- New rc cuts, activation, canary roles and acceptance/safety debt are separate caller decisions. The integrated S4a stack is newer than every fetched rc. Old rc containment does not authorize enabling automatic desktop destruction; recorded native/Unit/Mutation/witness obligations remain.

## Not done, noted

Existing-control idea only, not designed or applied: activate a qualified rc containing runner retention with the intended 24h/salvage disposition, and separately qualify S1 slot admission to bound new mirrors; complete S5b-2/3 to stop desktop halves growing. No new scheduler or product repair was designed.

## Evidence custody and rerun

Raw generated reads stay gitignored under `/work/worktrees/task-39da4445/.antiphon/worktree-status-evidence/`; essential rows, events, identities and counts are retained in this report. API JSON contains no copied authentication headers. Hashes identify the frozen samples, not a claim that a shared host stayed unchanged:

```text
tasks.json          cde0fe831459d1cd879595d946bdf24fa844569cfb96dff33bb4e132f316ad24
census.json         1936c4faca79822ecc561f4376c24a23f8b90bbe7c5dd534aba03e009af8234e
version.json        bc638b21dd4bd964acc116f91ec5b8e49660e640a00dbc2a6a828c9dfc0187b7
runners.json        e334ce8250b1cde4ef2e041ba0ef06c5962c428da539ece865aef821f11152c2
runner-status.json  167b66e7f52f9237f8c15e7fd44b2d683aa0fe0246725820ae9eed43ef0d187a
history.json        f7bc656b2ab52bb1c65d3466d45d3d001a84a84bfe3f743ee0916401b0ac853b
registrations.txt   bc680a228aaed9d9cc8e8cd2de9085ca0822e0d392c69b25275fb184fbfc024f
```

To reproduce the population, read `GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&includeChecks=true`, inventory direct children of `/work/worktrees`, require exactly one full-ID match for each exact task leaf, classify terminal statuses Succeeded/Failed/Canceled using `completedAt` against one recorded UTC instant, and report unmatched/non-task entries separately. Cross-check Git registrations and `df`; refresh `/api/version` and `/api/session-runners/server2/status`. These are read-only measurements. For each SHA in the slice table use `git merge-base --is-ancestor <sha> origin/master` and the same command against the recorded rc tip/live SHA. Do not infer activation from landing or health.

Validation for this docs-only investigation: arithmetic/identity checks on the frozen census and source/ancestry inspection; no tests/builds. No task-owned bin/obj/test outputs were created, so none required removal. Only the two individually named Markdown report paths are intended for the single task-branch commit; generated evidence is excluded. Publication is authorized by the brief; no further go-ahead was required.

## Appendix: frozen filesystem/task rows

Every leaf below is `/work/worktrees/task-<first 8 UUID characters>`. This table retains the complete mapped population, including recent/open rows, so the snapshot counts can be audited without the ignored JSON. `old` = terminal strictly over 24h at 13:35:05.149Z; `recent` = terminal <=24h; `open` = nonterminal. All rows have runner `server2`.

| Full task UUID | State | completedAt UTC | Bucket |
| --- | --- | --- | --- |
| 00646fa6-4585-4681-9c9b-819ba1f0c3bb | Canceled | 2026-11-06T03:35:59.432423Z | recent |
| 00a0b2d1-ec0d-4c2e-9907-1fc371e0bb64 | Succeeded | 2026-11-04T12:44:05.920555Z | old |
| 013b9ff7-b813-4986-8c7f-2a5b0fbdef95 | Succeeded | 2026-11-05T20:25:00.520615Z | recent |
| 01d698da-f6be-498b-a3c0-f04eef33e1ec | Failed | 2026-11-06T05:14:26.357882Z | recent |
| 02710520-3f0b-4c81-bb1e-01255f03f46d | Succeeded | 2026-11-06T01:01:57.10921Z | recent |
| 03477baf-e082-4e35-994d-cfada8d7914c | Succeeded | 2026-11-06T07:14:00.00372Z | recent |
| 0387f626-287d-4d34-b052-ab030c2c44b2 | Succeeded | 2026-11-06T11:21:04.289468Z | recent |
| 03e7cd6d-813a-4737-b76c-cacc1c832e66 | Succeeded | 2026-11-05T00:24:12.140202Z | old |
| 044f7257-730c-4108-b231-2c8023cc90a2 | Succeeded | 2026-11-06T05:23:03.346321Z | recent |
| 05081946-6364-4d21-a420-3e5ba180ec57 | Succeeded | 2026-11-05T10:45:02.769447Z | old |
| 07ab1e49-119c-4b0b-aba6-3706085e27f8 | Succeeded | 2026-11-06T00:22:07.254565Z | recent |
| 08a2e7f3-03ed-4ae2-b4ad-46704b4829bf | Succeeded | 2026-11-04T20:21:22.005014Z | old |
| 08cca1b8-c685-424f-adf3-9ca19fdea77a | Succeeded | 2026-11-04T11:24:59.106092Z | old |
| 0915902c-8397-415c-a785-5441f82c0528 | Failed | 2026-11-06T02:28:29.611062Z | recent |
| 09d70215-013c-4689-b083-823bdcdbe2b8 | Succeeded | 2026-11-06T02:43:31.679124Z | recent |
| 0b3f9d62-ed60-4dc2-8ada-978e2f1d0e6b | Succeeded | 2026-11-06T10:00:24.524562Z | recent |
| 0b9155a9-cc75-4964-be68-f0472b482dbc | Succeeded | 2026-11-06T09:08:17.711529Z | recent |
| 0ba30c05-8071-4eee-b939-4ff649fcf666 | Succeeded | 2026-11-06T12:41:13.072144Z | recent |
| 0c0e9de1-aadc-4d41-8b7d-630795d383c0 | Succeeded | 2026-11-05T08:32:24.423574Z | old |
| 0c3b7de3-2cd1-45df-ba54-c4ccf8ef4b77 | Succeeded | 2026-11-04T11:58:28.531316Z | old |
| 0c839994-dbc8-4f03-8b6e-4385df959e17 | Succeeded | 2026-11-05T11:25:05.219557Z | old |
| 0cc54c85-4fbd-4501-a6ee-9f7b9cc1e1cd | Dispatched | null | open |
| 0cd23a4c-9851-4b35-a19b-ebaf22e0dcab | Canceled | 2026-11-05T08:18:16.60105Z | old |
| 0e8a1b50-9eb9-4149-a16b-441bad36a514 | Succeeded | 2026-11-04T18:00:46.125014Z | old |
| 0efa38ca-729e-40fe-8d30-c9fbe65bfc30 | Succeeded | 2026-11-04T16:18:54.692381Z | old |
| 11212cb7-cfa0-42f8-b746-034c8ff4b77e | Failed | 2026-11-06T02:14:11.751169Z | recent |
| 11a0b0b8-366f-4dfa-b46b-71b5550ae212 | Succeeded | 2026-11-05T02:17:58.269312Z | old |
| 11a6d8bb-e139-46c6-b024-7bfbe0afb2b9 | Succeeded | 2026-11-05T05:31:33.024917Z | old |
| 122f2dcf-d726-47c0-9638-954de585c8af | Succeeded | 2026-11-04T22:42:22.833083Z | old |
| 12c87cda-e40d-43dc-93e6-ae6aabaaf265 | Succeeded | 2026-11-04T07:35:19.951608Z | old |
| 133255e3-d409-49e8-a9ee-6d0709679e71 | Succeeded | 2026-11-05T07:58:37.022888Z | old |
| 144136d6-e932-4ffb-96af-f43436ec984e | Succeeded | 2026-11-06T07:40:15.735347Z | recent |
| 1630c3fe-454b-402b-a5fc-08f0c3662ea9 | Succeeded | 2026-11-06T05:15:36.731568Z | recent |
| 189595a5-2efa-417d-be76-bfb13cf090f8 | Succeeded | 2026-11-04T09:48:22.899113Z | old |
| 1999cb95-16e9-4a7a-a69f-e60f618d11ab | Succeeded | 2026-11-05T12:15:04.887258Z | old |
| 19a14614-dc03-4693-ba9b-0b0d18e6db3e | Succeeded | 2026-11-04T17:42:29.339474Z | old |
| 1c022492-542f-49a7-be21-9fd862217412 | Succeeded | 2026-11-04T14:03:20.561725Z | old |
| 1c0653a5-816b-4197-8882-ae4787e35d2d | Succeeded | 2026-11-04T19:15:53.107577Z | old |
| 1c2d93f1-0f86-442b-a4aa-7f217531da41 | Succeeded | 2026-11-04T16:10:54.056839Z | old |
| 1c44bc80-fa96-40eb-9a62-e42529340ee6 | Succeeded | 2026-11-04T16:33:38.874836Z | old |
| 1c4f8dd2-38f5-47da-9be2-ef8e435e5ac1 | Succeeded | 2026-11-04T23:16:26.593696Z | old |
| 1c7946d0-8231-429b-9c5b-c420711258ce | Succeeded | 2026-11-05T13:02:59.715746Z | old |
| 1c9ceb47-a8c8-4b13-84e2-362b1d706786 | Succeeded | 2026-11-05T22:34:11.853777Z | recent |
| 1cf13574-1b75-4828-b161-6ecc31a47a81 | Succeeded | 2026-11-04T09:00:18.709796Z | old |
| 1dc468a4-8a38-44fe-94eb-383e39717587 | Succeeded | 2026-11-04T07:49:53.597835Z | old |
| 1e279cab-ac20-404d-ac8a-5db94203bcc5 | Canceled | 2026-11-06T02:34:10.084527Z | recent |
| 1edbc845-5bd7-410a-a37a-3d205fe1aa3a | Canceled | 2026-11-05T04:39:10.024023Z | old |
| 1ee29680-8b77-4d27-a4aa-2bd4ef60014c | Succeeded | 2026-11-04T19:09:02.908765Z | old |
| 1f10d17d-b289-4e13-9945-6fb1cc279248 | Succeeded | 2026-11-05T23:45:53.924538Z | recent |
| 204a327a-f5fa-4f58-90bd-88435d76e5c2 | Succeeded | 2026-11-06T12:46:24.130145Z | recent |
| 212363b0-ffa2-424c-8bfa-aebc8efc024c | Succeeded | 2026-11-04T09:42:43.501167Z | old |
| 23889c9b-0eed-4812-9779-addac45144ab | Succeeded | 2026-11-05T08:20:55.862065Z | old |
| 23abede2-8099-4671-a46b-b0fcbd40dc27 | Dispatched | null | open |
| 23fce9a6-5bb2-494f-8426-866364b616c0 | Succeeded | 2026-11-04T16:53:11.347192Z | old |
| 27123eab-fefd-40b0-b509-d36e212d69b9 | Succeeded | 2026-11-05T17:38:16.126569Z | recent |
| 27ca134f-8c94-470c-a98c-a39b152009a5 | Succeeded | 2026-11-04T19:39:31.805587Z | old |
| 27e2f0b5-6507-4424-91aa-a4d7e378d33d | Succeeded | 2026-11-05T05:42:27.518826Z | old |
| 291753f8-3727-4066-87d8-926b6f994a6f | Succeeded | 2026-11-04T16:35:30.341944Z | old |
| 29664acc-814f-48a8-bfed-c8e1053db982 | Succeeded | 2026-11-04T09:57:37.902423Z | old |
| 29cd334a-672c-43bd-ba36-4d56be962306 | Succeeded | 2026-11-04T07:26:46.226883Z | old |
| 29e42131-45c0-4077-b82f-d5fe0ec96b84 | Succeeded | 2026-11-05T20:20:02.520544Z | recent |
| 2b5414eb-93dc-401e-9a00-b3dd3721799b | Succeeded | 2026-11-05T21:53:54.052116Z | recent |
| 2db0be9a-3b16-4664-a3f8-660816391ac6 | Succeeded | 2026-11-06T05:59:12.750673Z | recent |
| 2e0375f1-53dd-4758-9ed3-b37414892d0d | Succeeded | 2026-11-05T07:19:44.102816Z | old |
| 2ede815a-1ca4-4597-885d-b7d2509e5b0e | Succeeded | 2026-11-05T06:09:41.882261Z | old |
| 2f58c570-2b29-4a48-b102-295d87668be5 | Succeeded | 2026-11-04T14:23:30.409913Z | old |
| 306148cc-74f4-40e2-8948-46254b05b6fa | Succeeded | 2026-11-04T11:08:12.893501Z | old |
| 314297be-4869-46a5-bcf0-b6b9ec3d6894 | Canceled | 2026-11-04T12:46:08.93082Z | old |
| 31e20f5a-80df-4266-9800-03338264a4e1 | Succeeded | 2026-11-04T09:18:20.686805Z | old |
| 32c41b68-f21a-48ef-87b3-f4947c773b09 | Succeeded | 2026-11-06T10:22:15.66467Z | recent |
| 340f1d0a-ca9c-4a81-b2af-aaea56e9ade0 | Succeeded | 2026-11-05T21:33:45.129575Z | recent |
| 34125850-eccf-49d3-b77e-8eece792d992 | Canceled | 2026-11-05T19:19:46.663998Z | recent |
| 35623cb8-d7de-4215-b203-927268c02b4c | Succeeded | 2026-11-04T08:34:43.913042Z | old |
| 362f7434-af30-4737-a4d5-fd7a732c77ff | Succeeded | 2026-11-05T01:02:29.450966Z | old |
| 36b3dc05-5063-4647-aa1e-248dce8be32f | Succeeded | 2026-11-04T16:20:37.960985Z | old |
| 36e2134b-54e0-49dd-91e6-861f04f7d8d3 | Succeeded | 2026-11-05T17:00:52.360676Z | recent |
| 374d7fa0-2978-4b9a-89c1-176f0dd0b977 | Succeeded | 2026-11-05T05:01:34.941276Z | old |
| 37b01055-f351-4af4-ad78-bb4bab815bf6 | Succeeded | 2026-11-04T12:42:04.650806Z | old |
| 37f1e778-3a76-452b-b28c-3f373075de6d | Succeeded | 2026-11-05T13:48:26.412045Z | recent |
| 382b8506-c2a4-4c7f-9733-83e9431bf5eb | Succeeded | 2026-11-06T02:55:02.40046Z | recent |
| 38ec4219-4073-4bbb-ba0a-9a117c0f52e6 | Succeeded | 2026-11-05T22:40:03.25762Z | recent |
| 39da4445-ccb1-4124-ac57-41a2c9d621ff | Dispatched | null | open |
| 39e1bd2f-4f88-455f-84aa-5ee0f6a6a2a3 | Succeeded | 2026-11-06T10:14:57.925381Z | recent |
| 39f7bfe1-fa4a-4dfa-b16c-385f58d32303 | Succeeded | 2026-11-06T01:15:09.918773Z | recent |
| 3a179edb-d2a9-4f7e-a43b-b808abe932f9 | Succeeded | 2026-11-04T13:52:36.85552Z | old |
| 3b906877-feb6-4962-b100-84f851cc0ffc | Canceled | 2026-11-05T06:39:51.431152Z | old |
| 3c2aac7d-8e14-478f-9096-b4162ee9db09 | Succeeded | 2026-11-05T01:03:50.844508Z | old |
| 3e640ea9-c14c-421a-b692-ec67c0f484ad | Succeeded | 2026-11-05T06:30:09.618682Z | old |
| 3f52b55a-fcaf-4efd-baea-6a7f2f245362 | Succeeded | 2026-11-05T02:18:49.655448Z | old |
| 3fd6747e-1658-4fb4-9bb1-5d9486a38b03 | Failed | 2026-11-04T23:18:19.686023Z | old |
| 4047a80b-b0b7-43e4-8784-f194f4a04180 | Succeeded | 2026-11-04T15:44:01.235767Z | old |
| 406bca79-5305-4791-815c-b76395d549b7 | Succeeded | 2026-11-04T20:07:09.671625Z | old |
| 420b5c90-74c8-4582-82d5-d6d77237f18a | Succeeded | 2026-11-05T23:51:46.199895Z | recent |
| 449c541d-5587-4a5b-9a5f-8b0185269480 | Succeeded | 2026-11-04T14:23:51.188211Z | old |
| 459edfef-decf-4f4e-8c17-4b8e55a476e7 | Succeeded | 2026-11-04T20:09:03.685629Z | old |
| 47169b54-636d-4c9d-9c03-0f22ac1e3551 | Succeeded | 2026-11-05T10:21:52.773537Z | old |
| 47d31366-a695-444a-8498-120656596668 | Succeeded | 2026-11-05T02:54:21.566748Z | old |
| 47f9be84-ff93-452b-8fb2-4b45f24cf54d | Succeeded | 2026-11-04T05:08:52.128337Z | old |
| 481b379a-e724-4a92-bde0-31ea61edb156 | Succeeded | 2026-11-06T08:07:07.891206Z | recent |
| 4965fc83-f428-476e-a5ff-715c9cef1e2c | Succeeded | 2026-11-06T11:21:50.56469Z | recent |
| 4a75ce55-f6cf-4077-ab17-d04f8fa33e22 | Succeeded | 2026-11-04T09:03:06.654645Z | old |
| 4af5405a-3559-4fa0-8ed0-b0bbf5527a1e | Succeeded | 2026-11-04T11:50:02.980578Z | old |
| 4aff2d15-57e9-48c4-8aa8-aaffafc78169 | Succeeded | 2026-11-05T19:20:29.731346Z | recent |
| 4b9d4604-2817-4a7a-99d6-25aa2e09d30c | Succeeded | 2026-11-04T16:29:02.60242Z | old |
| 4c2d90db-5870-4622-91a4-fdb769b622d2 | Succeeded | 2026-11-04T11:57:28.267799Z | old |
| 5050b2de-d55f-4343-b83a-2e6bb15417a6 | Succeeded | 2026-11-06T10:01:43.650116Z | recent |
| 50e9a890-381f-4bfa-8d84-5b82cb7e4962 | Canceled | 2026-11-06T02:34:09.354679Z | recent |
| 50f25440-2f7d-49cf-85cd-a314095e99d1 | Succeeded | 2026-11-05T22:20:16.696778Z | recent |
| 51b0c438-dbd2-4840-9ad0-05e32af02333 | Failed | 2026-11-05T21:45:06.923656Z | recent |
| 52410842-b68f-4817-8c5b-ec887e532748 | Succeeded | 2026-11-04T20:06:15.194981Z | old |
| 5242d604-191b-4a0d-b5d0-afe9449e77f6 | Succeeded | 2026-11-05T02:23:25.95076Z | old |
| 525e0201-3ba6-4809-bcef-3dbb806d185f | Succeeded | 2026-11-06T11:57:25.716511Z | recent |
| 52890135-8e78-4289-a35c-e6ad5b440c5a | Canceled | 2026-11-05T18:46:57.572209Z | recent |
| 53da6f14-0fa0-4d10-959d-49d5721ea8f0 | Succeeded | 2026-11-06T00:41:15.168248Z | recent |
| 543d8c5b-8a8a-4796-bb6a-f9d28447cbb8 | Failed | 2026-11-06T00:23:50.242321Z | recent |
| 5528528e-83a2-4a03-9be8-d908b38ee7bc | Succeeded | 2026-11-06T09:45:48.663883Z | recent |
| 555da802-d558-4c26-9e38-49d072636603 | Failed | 2026-11-06T03:43:16.380319Z | recent |
| 55b96312-b6bb-47a3-967d-d23fc6f65969 | Succeeded | 2026-11-05T23:05:37.954261Z | recent |
| 56b0f9aa-f156-4d2c-b5da-fa326f65be08 | Succeeded | 2026-11-04T23:31:04.906508Z | old |
| 56bf971d-5f5c-473a-a3d5-043ff9e28ac6 | Succeeded | 2026-11-04T14:05:39.569597Z | old |
| 577b2ee0-e667-4a03-bc7c-7ed356c06cd1 | Succeeded | 2026-11-04T18:36:03.999082Z | old |
| 5960094e-6e30-42a7-b8c8-023ba53ada4c | Succeeded | 2026-11-06T02:32:36.193064Z | recent |
| 5ae98d31-ae33-4e5c-a466-4cfab7344616 | Succeeded | 2026-11-05T19:30:02.428618Z | recent |
| 5baa0ee3-5883-49a6-865b-fab19dc1a14f | Succeeded | 2026-11-05T10:28:45.215259Z | old |
| 5c10955b-0618-4b90-9c51-d5d9f42de4f7 | Succeeded | 2026-11-05T06:57:04.67344Z | old |
| 5d3d7fb0-ff8d-4261-a62a-91eae18177a9 | Succeeded | 2026-11-06T08:47:58.202175Z | recent |
| 5e05fc80-930f-4f03-8c71-6c323d552d63 | Succeeded | 2026-11-05T01:19:59.960059Z | old |
| 5e5930cc-d0fa-41a8-81cc-8aa702f8c51c | Succeeded | 2026-11-06T04:35:37.784702Z | recent |
| 5ec136af-0726-4c4f-8ec5-4ae4c4c0712e | Succeeded | 2026-11-06T04:27:14.180569Z | recent |
| 605c6f4e-293f-43b4-a397-1ea3ea97a4b3 | Succeeded | 2026-11-05T01:56:28.746413Z | old |
| 606b7d8d-4cc1-4115-a4dd-d7ea58c4e683 | Succeeded | 2026-11-05T00:28:57.932823Z | old |
| 60cb0a38-ee5e-462a-b3ed-6b182cc29872 | Succeeded | 2026-11-04T09:20:42.183661Z | old |
| 613f2230-8d49-4461-bf0d-d94216d0b121 | Succeeded | 2026-11-05T01:07:08.899512Z | old |
| 642b8665-4679-447f-a532-1ec2f0ac2f28 | Succeeded | 2026-11-06T02:29:15.022464Z | recent |
| 64fb3f5d-6ca7-46dd-ad20-251d5d984197 | Canceled | 2026-11-05T04:39:09.23004Z | old |
| 6532e78e-ab90-4742-993b-a7e2868d1c9f | Succeeded | 2026-11-05T19:24:57.473669Z | recent |
| 6569dd36-5266-4f91-b797-60e984e4be6c | Blocked | 2026-11-05T19:35:23.083284Z | open |
| 6578802d-12e7-4f58-874b-e9e6732de5b1 | Succeeded | 2026-11-04T13:35:42.212501Z | old |
| 65e6f8b0-9639-4276-b52e-06f7a99e6087 | Succeeded | 2026-11-06T00:32:16.772232Z | recent |
| 67077ff7-4001-40d2-a7c8-f64edd79b1a6 | Succeeded | 2026-11-05T13:41:23.239938Z | recent |
| 692b70e4-a874-4dbf-9ac3-7a96b7cde2e6 | Succeeded | 2026-11-04T10:51:34.823164Z | old |
| 6949514d-0b4a-4c58-badc-59cf6eeeee4d | Succeeded | 2026-11-04T16:15:50.0529Z | old |
| 69ade330-9ef8-47da-8414-72d5790a61ea | Failed | 2026-11-06T00:50:00.209829Z | recent |
| 6c1b0f87-1545-4d38-b113-8b2ccc14e3a8 | Succeeded | 2026-11-04T19:02:25.624711Z | old |
| 6dfad742-ef1b-45d5-9f80-b7a1a690eb14 | Succeeded | 2026-11-04T21:06:18.547228Z | old |
| 6f53cb5a-e96e-4fde-8f9a-8e335fc1d478 | Succeeded | 2026-11-06T11:24:58.958832Z | recent |
| 71dacd6c-a637-440c-aff3-f8b7e242e7c7 | Succeeded | 2026-11-05T07:20:49.460585Z | old |
| 727a57dd-0a24-4056-91be-7f9a85c05fe7 | Canceled | 2026-11-05T06:11:16.151927Z | old |
| 7289f7c7-2a58-4414-b3a3-df5380b9d855 | Succeeded | 2026-11-04T11:59:43.415145Z | old |
| 73837e64-b8d1-47de-b945-66602387f5d9 | Succeeded | 2026-11-04T11:16:18.767305Z | old |
| 73c027ae-8ab4-4e6d-a145-50d426f513ea | Succeeded | 2026-11-05T02:57:37.808916Z | old |
| 73c94e0b-ad0b-4917-b782-e989e262a1f9 | Succeeded | 2026-11-05T01:32:55.659035Z | old |
| 742b9c5c-9e25-4fac-910e-799803e461c9 | Succeeded | 2026-11-06T01:43:55.024217Z | recent |
| 75c9410c-601a-45a2-a67f-5fc87dc74d2e | Succeeded | 2026-11-04T12:26:56.578316Z | old |
| 75d1358a-8160-4fc0-ba8d-7463a3d2b434 | Succeeded | 2026-11-05T08:59:57.510128Z | old |
| 7626aee3-8d4f-4eba-a95d-702a23977392 | Succeeded | 2026-11-05T19:42:53.955058Z | recent |
| 7ab0d65a-9cfb-49d0-b8cd-69880049a783 | Succeeded | 2026-11-04T13:10:09.127658Z | old |
| 7c7132b3-f574-4cea-be77-9933de62b3f4 | Failed | 2026-11-04T23:34:09.674506Z | old |
| 7d6aabc8-2de8-4c44-b9d9-6e54edfd6a95 | Succeeded | 2026-11-06T07:49:05.32119Z | recent |
| 7e4799eb-145c-42e5-815a-265434ba5b7e | Failed | 2026-11-06T05:00:20.617097Z | recent |
| 7eb005f3-6d93-40de-b1c1-4a500f3a6107 | Succeeded | 2026-11-04T09:32:38.360261Z | old |
| 7ef9aa63-506a-487e-b5b0-8d79fa4dbae7 | Succeeded | 2026-11-05T10:26:30.34398Z | old |
| 7f675cf6-a38e-4843-bfe9-361a4948817f | Succeeded | 2026-11-04T15:52:26.993266Z | old |
| 801ad6c3-746b-47a9-80c0-3d3404c106fc | Succeeded | 2026-11-04T17:52:17.687551Z | old |
| 821a7c5b-d503-4da6-97d3-c9e92cb73488 | Succeeded | 2026-11-04T14:59:46.799557Z | old |
| 82cf72c1-959a-4efd-ba52-ac98f9558a42 | Failed | 2026-11-05T21:42:55.977828Z | recent |
| 8347d02a-8704-4645-a5e3-7bd5138bc85a | Succeeded | 2026-11-04T10:23:06.711342Z | old |
| 840f535c-ce81-4a9a-a894-a344a9d47e8a | Succeeded | 2026-11-06T05:22:12.854807Z | recent |
| 84b44899-e9b1-43dd-b39c-c67e68c95dca | Succeeded | 2026-11-04T10:04:44.991758Z | old |
| 84d42fbe-1822-478b-b80f-c181cb23f4bd | Blocked | 2026-11-06T06:56:57.641817Z | open |
| 8592d536-1c63-4b4d-8fdc-3fb92d817420 | Canceled | 2026-11-05T04:39:10.842255Z | old |
| 85a330bf-275f-4079-a042-ec54eeabf69d | Succeeded | 2026-11-06T12:05:59.656626Z | recent |
| 86a941a3-e3ef-414a-afb7-8bc4ce3d5e3a | Succeeded | 2026-11-05T02:47:29.598881Z | old |
| 8719af3e-0388-4f11-934f-916c115b838f | Succeeded | 2026-11-04T14:04:10.714218Z | old |
| 871c4033-05ab-41b6-8f2b-93023ec3aedc | Succeeded | 2026-11-05T13:43:15.638026Z | recent |
| 87232051-9b53-424b-9dba-20ee7ca01d49 | Succeeded | 2026-11-04T12:53:09.097888Z | old |
| 880edccc-6799-4004-9942-3cbb7f1ebe78 | Canceled | 2026-11-05T04:40:34.107694Z | old |
| 89ea871f-53aa-44f0-8e55-2e4f470eccd4 | Succeeded | 2026-11-06T01:28:57.546439Z | recent |
| 8a2a0e71-1378-4222-93ce-59c8f3a3aeeb | Succeeded | 2026-11-05T14:33:51.893182Z | recent |
| 8a9f92ba-1572-48a7-9f1d-6cac71c4aa50 | Blocked | 2026-11-06T06:08:46.784377Z | open |
| 8ac2299c-78cb-4826-bd72-e9a540dab3df | Succeeded | 2026-11-04T18:11:12.052107Z | old |
| 8acb8a4d-8362-4007-ac50-4a59273233cd | Succeeded | 2026-11-04T09:06:39.686569Z | old |
| 8b3bd292-7ada-4b34-a353-a50d98cd861d | Succeeded | 2026-11-06T10:18:38.076913Z | recent |
| 8b609b8a-ae27-41c2-911e-273d08fc73fa | Succeeded | 2026-11-04T18:50:07.272716Z | old |
| 8b9b7a9a-3c10-4686-ac94-9dce7e232471 | Succeeded | 2026-11-06T06:15:16.605166Z | recent |
| 8eb266f0-8ab4-433a-934c-ecdf98d2fe97 | Succeeded | 2026-11-06T01:05:28.7642Z | recent |
| 8f14e2c9-67a9-438f-8e29-7b5eb8f47bd9 | Succeeded | 2026-11-05T20:46:45.633694Z | recent |
| 8f43dbed-e7b8-4d44-b8e3-0252c374f651 | Succeeded | 2026-11-06T03:25:15.951049Z | recent |
| 9222cb00-312e-45af-96d0-e9d4c9d55b71 | Succeeded | 2026-11-05T22:57:32.944722Z | recent |
| 931a28cf-2dd8-415b-a9ad-c4ba3d53a553 | Succeeded | 2026-11-04T16:33:52.033829Z | old |
| 935ca9be-f898-4649-a739-342342771be4 | Succeeded | 2026-11-06T00:46:20.441423Z | recent |
| 93a16649-de05-4087-a93c-9e3161d536f2 | Succeeded | 2026-11-05T02:16:14.76859Z | old |
| 93aae0fa-b351-4933-888a-43fec30e133e | Succeeded | 2026-11-05T09:21:39.835297Z | old |
| 9602645b-8365-4afa-b474-b908fc682ebf | Succeeded | 2026-11-04T19:45:37.890766Z | old |
| 96f2fc86-8ac3-4280-aa24-d87f8ae0cecf | Succeeded | 2026-11-04T07:45:57.191355Z | old |
| 973b4d1b-2fcd-4be2-ad5e-ee9a67219783 | Succeeded | 2026-11-06T00:05:46.199674Z | recent |
| 977203e7-7375-45cf-82fe-47afcfd6ac81 | Succeeded | 2026-11-04T18:49:10.1203Z | old |
| 9843b768-bdf1-465b-ba6e-80863e57287c | Succeeded | 2026-11-04T06:09:39.750903Z | old |
| 99e23919-bec3-4777-8ef1-e387c5e44a64 | Succeeded | 2026-11-04T21:37:48.271442Z | old |
| 9c415aaf-7700-4ee5-b22d-0a1366c96649 | Succeeded | 2026-11-05T12:51:21.632867Z | old |
| 9e4c7963-2d06-468a-bf62-6e23e1c30e35 | Succeeded | 2026-11-05T05:17:37.501343Z | old |
| 9f03d97e-bb40-4f1b-99c8-5961aded0191 | Succeeded | 2026-11-05T14:06:33.47817Z | recent |
| 9f577891-4fed-4f7e-81b5-aeefb4269d19 | Succeeded | 2026-11-04T20:14:27.33127Z | old |
| a22bdfed-7ce2-48cb-a694-a72360f00dcd | Succeeded | 2026-11-04T14:47:13.885441Z | old |
| a2cfcdc1-7571-4f1f-af5a-146d9ef28c92 | Succeeded | 2026-11-05T08:13:34.81351Z | old |
| a342b58a-f264-4674-925e-d60245dc9409 | Failed | 2026-11-06T04:04:39.20661Z | recent |
| a3555767-d833-4bdd-a061-5f279d610ff2 | Succeeded | 2026-11-06T05:39:32.498609Z | recent |
| a3a1bbee-b8fe-480c-a5ef-f3e05e96da69 | Succeeded | 2026-11-05T19:16:37.579556Z | recent |
| a41890aa-9341-4b0a-ab12-756a36164796 | Succeeded | 2026-11-04T15:39:03.40971Z | old |
| a42d888f-ec0e-4c1c-90b9-34c2a2cab720 | Succeeded | 2026-11-04T13:19:18.360418Z | old |
| a43ac839-e8cc-4d7a-a3d4-bfedabe40c0d | Succeeded | 2026-11-06T12:22:22.403131Z | recent |
| a4933b4f-a694-48a8-a655-a1b82fb3b66f | Succeeded | 2026-11-05T08:13:28.307006Z | old |
| a695169d-37e1-41f7-bb35-2909791c5d35 | Succeeded | 2026-11-04T13:33:37.546911Z | old |
| a6c5c347-0602-430a-8213-96f3b4dcfccb | Succeeded | 2026-11-04T19:06:17.129553Z | old |
| a8945a17-c1da-451f-ae2c-1a42f9406656 | Succeeded | 2026-11-05T19:13:01.276646Z | recent |
| a8d643be-e182-4ec8-b67c-767be7e58e22 | Succeeded | 2026-11-05T08:37:17.158017Z | old |
| a98c2d1e-7763-48ab-a2a6-b1bc1f30e27e | Failed | 2026-11-06T03:43:41.133105Z | recent |
| a9b46304-690f-4845-985c-2d3d1d3213a6 | Canceled | 2026-11-06T02:34:08.68234Z | recent |
| aa3c54ff-965c-4ecc-997e-434578c8debd | Dispatched | null | open |
| aa58fb53-d9ec-439b-869d-46d88fa4812a | Failed | 2026-11-04T09:28:49.529577Z | old |
| aae1b064-a76a-47cb-beed-80a52db36da3 | Succeeded | 2026-11-06T06:09:56.957871Z | recent |
| ab37b2cf-3efc-4ca4-ad5c-11bfc713a850 | Succeeded | 2026-11-06T08:36:42.420305Z | recent |
| adbd81fe-2c25-4b72-94a7-2548976d0726 | Succeeded | 2026-11-04T23:50:30.579337Z | old |
| ae6457ac-0cce-4c0b-9e06-d6828793b5d2 | Succeeded | 2026-11-04T21:20:48.292317Z | old |
| aeb2deb6-f55c-4af6-b241-d8f81bb28119 | Succeeded | 2026-11-06T04:50:30.689878Z | recent |
| b02d7898-b0b8-4e92-86c0-86ba2c5e966b | Succeeded | 2026-11-04T18:07:43.604938Z | old |
| b054e10c-ae72-4f16-ab29-e516eb5d4f0a | Succeeded | 2026-11-04T07:38:56.619419Z | old |
| b055e88c-9e43-499a-a7af-59929b8a0c5b | Succeeded | 2026-11-05T08:56:29.420673Z | old |
| b1cdbed3-856c-43ba-a4a5-d40f333a41b5 | Succeeded | 2026-11-04T11:43:52.223767Z | old |
| b3bd8985-4ed7-4b98-a635-1f5912f5fb40 | Succeeded | 2026-11-04T13:17:34.670558Z | old |
| b3c01691-be50-4462-b1c7-c1a44c322c89 | Succeeded | 2026-11-04T17:05:12.560172Z | old |
| b578f67a-b19a-4574-bcc9-03520d58e867 | Succeeded | 2026-11-04T13:00:44.048546Z | old |
| b5b7324a-9f92-4843-9ee1-1be05812359b | Failed | 2026-11-05T03:59:42.878953Z | old |
| b7350767-a557-4b45-b076-07a2c1397cb7 | Canceled | 2026-11-05T12:53:40.70457Z | old |
| b7c4a9b7-c3d3-407b-a938-8bdb26e02837 | Canceled | 2026-11-06T02:34:07.792798Z | recent |
| b8770621-40ea-4393-a1ff-a51c57f98ac6 | Succeeded | 2026-11-06T00:39:26.141319Z | recent |
| b8eac553-cfd1-447f-a278-dc9e4510a4cb | Succeeded | 2026-11-05T23:59:38.943965Z | recent |
| ba8ec885-1df8-476b-9ca4-08653706938f | Succeeded | 2026-11-04T19:42:04.518606Z | old |
| bb0a6ce8-ae6c-4ef9-b642-e18e9575e94d | Succeeded | 2026-11-06T02:21:10.412377Z | recent |
| bbd446f6-31b8-466d-9866-1d7822a2e8dd | Succeeded | 2026-11-05T03:12:06.680218Z | old |
| bccb0c90-9d2e-409d-8812-3c28429ad0d2 | Succeeded | 2026-11-06T02:55:33.322178Z | recent |
| bdaa665c-5674-44ad-87d6-39b7d7f0638b | Succeeded | 2026-11-04T13:24:43.401896Z | old |
| c0cea8f5-0610-4c98-b4ba-718d12c965fb | Succeeded | 2026-11-04T09:20:15.262874Z | old |
| c1e2a660-f0a9-472a-b8c1-c96fba3dd2d8 | Succeeded | 2026-11-05T12:03:59.083601Z | old |
| c3ba1792-e318-4638-b9f2-b8222086960b | Succeeded | 2026-11-05T06:24:27.017213Z | old |
| c5d82758-7b4c-4c2c-9a8a-810fd166ee38 | Succeeded | 2026-11-05T12:40:04.101339Z | old |
| c781f7f8-e0d4-435a-8a37-cc71592aad17 | Canceled | 2026-11-05T11:35:05.422612Z | old |
| c7f02607-dfc4-4f86-8264-5aca38548217 | Succeeded | 2026-11-04T08:55:15.972104Z | old |
| c8274f67-76bc-4c48-8f4f-05d44a293b1c | Succeeded | 2026-11-04T15:00:55.74738Z | old |
| c993f042-927f-4d8b-b793-01aa016e69fe | Succeeded | 2026-11-05T19:20:35.886536Z | recent |
| c9ebcc1d-b220-4b15-98a5-28b875474f07 | Succeeded | 2026-11-05T06:33:09.981358Z | old |
| ca9712bd-209c-458f-927e-da383ecc26bf | Succeeded | 2026-11-06T10:15:48.044238Z | recent |
| cb6b4d80-d141-4c6e-be8e-ccc973836e9b | Succeeded | 2026-11-04T11:16:44.174212Z | old |
| ccfe679f-0450-4250-b5eb-fc0f71c731e8 | Succeeded | 2026-11-04T14:11:19.180888Z | old |
| cdf5415a-b79b-4018-bc12-cc19ada5ff4b | Succeeded | 2026-11-05T21:36:43.485819Z | recent |
| ce37c38f-c8c9-4c53-9122-35e9b97b8264 | Succeeded | 2026-11-06T09:30:01.595141Z | recent |
| d19080aa-d1a4-4705-873a-990763c26ccd | Succeeded | 2026-11-04T20:54:06.857026Z | old |
| d2564da4-a71a-40e6-8e9d-44999df97c34 | Succeeded | 2026-11-04T20:04:07.018092Z | old |
| d463ea3b-75bc-4f87-bd7c-08c0cae8325d | Succeeded | 2026-11-06T05:40:00.997649Z | recent |
| d5e8910e-0b67-497a-b0cc-0b31cfb98c2e | Succeeded | 2026-11-05T03:37:06.54814Z | old |
| d5fd03f4-b332-4b33-a1f4-83c9c63f0973 | Succeeded | 2026-11-06T04:48:31.283085Z | recent |
| d67a21b1-8aac-4b2d-a95e-c65aaefae6e0 | Succeeded | 2026-11-06T00:54:54.485913Z | recent |
| d69fe301-eb08-4a99-9063-c1bc22f8d21f | Failed | 2026-11-05T23:09:41.256436Z | recent |
| d6e48431-33ea-4247-89d1-aa1f582a6238 | Succeeded | 2026-11-05T06:34:19.980799Z | old |
| d707a486-2efb-4eef-9f9d-0d4b9b012160 | Succeeded | 2026-11-04T14:30:59.776625Z | old |
| d8af9f97-4e36-4217-9e5c-2762eafa7a48 | Succeeded | 2026-11-06T05:29:33.852065Z | recent |
| d8bdfd3a-e419-4c43-b770-0cad26b1e9e5 | Failed | 2026-11-06T00:07:52.342505Z | recent |
| d8f70e4c-bdef-466d-af72-5246aa55a878 | Succeeded | 2026-11-05T22:51:39.429129Z | recent |
| d933f966-15f7-4f30-bac2-1b5c6b21eed9 | Failed | 2026-11-05T11:15:23.065899Z | old |
| da743107-4ec9-4a98-9026-d18ba9c3d140 | Succeeded | 2026-11-04T08:23:36.307713Z | old |
| dc12c438-9622-47ba-a64d-c70580c40829 | Succeeded | 2026-11-04T18:54:45.449846Z | old |
| dd35b058-1f22-4442-b0e4-a2dfb52ac858 | Canceled | 2026-11-04T12:46:22.046901Z | old |
| e056f0ff-fbe7-498e-a3e7-df5483c5b9bc | Succeeded | 2026-11-04T18:53:12.577042Z | old |
| e0a09fa9-3b77-4937-a97a-ac13a6dd7e16 | Succeeded | 2026-11-06T12:11:36.934372Z | recent |
| e0c73157-ce0c-41ef-866d-7202dd5949f4 | Working | 2026-11-06T06:00:35.508559Z | open |
| e23c0f83-fed3-4aba-b669-1a01fae22ead | Succeeded | 2026-11-04T12:40:58.546641Z | old |
| e24018e3-553b-4f5d-a9ae-aa746c136267 | Succeeded | 2026-11-04T22:49:35.747679Z | old |
| e36a0edf-7441-4979-a2b7-6e8ce37151a2 | Succeeded | 2026-11-05T00:48:20.268318Z | old |
| e379bcd9-42b2-44bf-8a19-2786b3941f2b | Succeeded | 2026-11-04T12:36:22.31197Z | old |
| e38f32c6-8645-467d-9f53-1b4a261a584e | Succeeded | 2026-11-04T05:43:42.355412Z | old |
| e3c30656-689f-4662-a873-1f6278ade7b1 | Succeeded | 2026-11-05T09:33:06.183921Z | old |
| e438ae14-24e8-4a38-9764-58c4fafcd450 | Succeeded | 2026-11-05T05:00:42.9621Z | old |
| e45ab432-38a5-4425-8872-ed9ba98b5f6e | Succeeded | 2026-11-04T07:57:22.352127Z | old |
| e469bdcd-a91e-4ddd-bdab-62766ae7e6fd | Succeeded | 2026-11-06T11:20:41.791323Z | recent |
| e4cb0636-0b02-4058-9ed8-a49819755dd8 | Succeeded | 2026-11-05T22:01:52.527488Z | recent |
| e5258e8c-bdce-4b5e-9376-5eecd661b237 | Succeeded | 2026-11-04T11:38:08.732771Z | old |
| e778bd07-462a-4519-b7d1-03b2925d11ea | Succeeded | 2026-11-04T23:46:32.644013Z | old |
| e80ac465-8e65-463f-bf58-be05a040ad68 | Succeeded | 2026-11-05T03:37:39.788251Z | old |
| e8242d52-5c76-4ed2-a432-bb421cd72fbb | Succeeded | 2026-11-04T19:36:53.360203Z | old |
| e830e940-64a5-4e58-8327-cb62d55fc13f | Succeeded | 2026-11-06T07:13:21.513083Z | recent |
| e8915f2c-32e4-4c9d-a7d8-959d08345d1c | Canceled | 2026-11-06T04:40:05.637081Z | recent |
| ea048200-7666-4e46-bd8d-8b087c54d902 | Succeeded | 2026-11-05T10:22:54.772793Z | old |
| eab3a4fc-cfb8-4ea3-ab3d-97f6d31c8100 | Succeeded | 2026-11-04T21:14:21.210278Z | old |
| edbaee26-2ba5-4999-aa4d-17a5b60a7480 | Succeeded | 2026-11-05T13:04:54.219188Z | old |
| f059674d-a3ed-4582-b09f-6a2dacd70fb0 | Succeeded | 2026-11-05T05:29:02.971065Z | old |
| f2976027-cd0e-42ca-b267-49bfbb3ecc74 | Succeeded | 2026-11-04T19:16:13.93719Z | old |
| f2ed8c32-e319-49bd-97ca-3d8799628ce4 | Succeeded | 2026-11-04T12:22:09.683893Z | old |
| f2f59326-61b5-4a35-9f89-9d6c835c0f5f | Succeeded | 2026-11-04T11:41:02.805624Z | old |
| f303b3ec-53b7-4bbb-9af4-f5412a73c41d | Succeeded | 2026-11-04T12:50:30.545122Z | old |
| f35f1237-d903-4376-8dcc-a7500f01137f | Succeeded | 2026-11-04T22:02:04.604999Z | old |
| f54a5386-f0db-44fa-972c-b67445a168bf | Succeeded | 2026-11-05T11:33:10.79717Z | old |
| f565efa1-4624-4818-83e2-c3cf2c6e7c0f | Succeeded | 2026-11-06T11:37:05.012313Z | recent |
| f5e96e75-5601-4c0c-8359-db05a58c35b0 | Succeeded | 2026-11-04T17:45:02.382151Z | old |
| f5eb193f-75ca-4434-94c4-b3265d028521 | Succeeded | 2026-11-04T14:37:05.763846Z | old |
| f674c79a-9334-4960-ae05-8c5c08d724d2 | Canceled | 2026-11-05T04:39:11.920461Z | old |
| f67b2762-2988-457c-ae3b-9c833cda95f8 | Succeeded | 2026-11-04T06:09:25.428233Z | old |
| f6e3cc3c-119a-4e81-8195-66718ccb87d2 | Succeeded | 2026-11-04T13:49:16.686384Z | old |
| f7703a17-69d8-446d-bdff-ef79b2315e9a | Succeeded | 2026-11-04T10:10:21.408144Z | old |
| f822e84d-ef5c-44d0-ba24-05580ee5fadf | Succeeded | 2026-11-05T22:22:00.559677Z | recent |
| f85985fd-e83d-446d-a80f-dfbfbb6e45bc | Succeeded | 2026-11-05T11:57:45.424975Z | old |
| f893d1a3-8d3a-4f33-a885-d100aeeaa5ca | Succeeded | 2026-11-04T08:43:23.600051Z | old |
| f8ac5aa5-5b4f-4176-86ee-4460cf20280f | Succeeded | 2026-11-05T20:15:48.110306Z | recent |
| fada17c6-9246-4ab0-ab0b-9b83f81c0dce | Succeeded | 2026-11-05T12:22:57.426184Z | old |
| fc1834e8-cf67-4531-8c51-6b1d643b07e6 | Canceled | 2026-11-06T01:46:05.052818Z | recent |
| fc2bcb80-3957-4212-9ed7-7d3349761e30 | Succeeded | 2026-11-05T13:13:56.500118Z | old |
| fd0ebdc3-4a81-48d8-8ca5-b3e174b89746 | Dispatched | null | open |
| fee547cb-9814-4102-a202-998c216a7bfc | Succeeded | 2026-11-06T08:57:48.680933Z | recent |
| ff3ef62e-ca6c-4150-82ce-12966e3b0339 | Succeeded | 2026-11-05T06:21:13.030663Z | old |
| ff7ab869-00dd-40bb-a4a6-13f898f8ac19 | Succeeded | 2026-11-04T20:36:06.672096Z | old |
| ff8f8f3f-0643-4b2c-b6e6-c2ee63b6bd4a | Succeeded | 2026-11-04T11:38:29.695111Z | old |

--- next stage ---
next: decide
handoff: Decide qualified-rc activation and 24h/salvage policy separately from S1 slot canary and unfinished S5b-2/3, retaining the recorded safety, native, Unit and Mutation obligations. No product fix was designed; all slice, configuration and census evidence is above.
artifact: docs/investigations/2026-11-06-investigate-worktree-status.md
