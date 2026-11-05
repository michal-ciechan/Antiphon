# CARD-0824: state, runtime evidence, and prioritised handoff

Investigation `e438ae14-24e8-4a38-9764-58c4fafcd450`, 2026-11-05 UTC. Source baseline and freshly fetched `origin/master`: **`28cdefab326782f2c91ca361768a7252dc875ec7`**. All unqualified source line numbers below refer to that commit. This is an evidence report and sequencing recommendation for the next Plan task, not a new implementation design.

## Outcome

**We still create a worktree per task on both hosts.** Ordinary server2 Worktree tasks get a desktop checkout **and** a server2 mirror. S1's reusable runner pool has landed and is present in the running binary, but is not enabled; S1c and the survivor witnesses are not in that binary. The Windows desktop pool has not been implemented. Remote branch deletion (S2/H-1) has landed, ships off, and is absent from the running binary. S4a desktop automatic release has landed **on by default**, but is absent from the running binary and has unresolved data-loss defects. Do not activate a newer rc with automatic destruction on before the repair and acceptance gates below are satisfied.

Server2 measured **503 directories, 324.913 GiB allocated**, no `ws-*` directories and no `/work/workspace-slots` directory. `/work` has **136G available, 81% used**. The **708 directories** in the brief are an operator/board observation of the **desktop**, not server2. A separate desktop cleanup task is already running; this investigator made no live changes.

**Root cause confirmed:** creation still follows the per-task paths; runner reuse is gated off; desktop reuse is unimplemented; the newer automatic legacy-retention code is not loaded. This mechanism is reconstructed from code, a newly dispatched task's persisted events, current directory measurements, capability registration and build ancestry. It explains continued growth; it does not prove every existing directory is disposable.

## Evidence and observation boundary

Read the full card and enumerated its 49 revisions using:

```powershell
pwsh -NoProfile -File scripts/card.ps1 get CARD-0824 -Board Antiphon
pwsh -NoProfile -File scripts/card.ps1 history CARD-0824 -Board Antiphon -Json
```

Board `8988ca03-7414-47ad-b0b6-51556c701703`; card `4cda8fe1-f2b7-4df6-a8c5-b5f272d011be`. At the initial read the card was Review, Critical/Now, human-rated. Revision 49, `2026-11-05T04:46:07.774855Z`, records the operator's priority instruction and the desktop 708-directory observation. A column or Succeeded task is not publication or activation evidence.

At approximately **04:47–04:50 UTC**:

| Read | Stored observation |
|---|---|
| `GET /api/version` | `version=919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`, capability `land-v2` |
| `GET /api/session-runners/server2/status` | same `buildVersion`; store `f519bd08-e53a-47d1-adb1-2ab33475446f`; boot `052b11d6-7b20-436f-baad-3dd18955c169`; available/dispatchEligible/acceptingNewWork true; 9 runner sessions |
| `GET /api/session-runners` server2 row | Linux; capacity 10, occupied 9; has `workspaceRepositoryV1`, `workspacePublishV1`, `workspaceCardBranchV1`, `workspaceRepositoryIdentityV1`; **lacks `workspaceSlotV1` and `workspaceRetentionV1`** |
| same catalogue, desktop | Windows; available/dispatchEligible true; delegated-task capacity 2, occupied 1; no workspace-slot feature |
| same catalogue, server2-temp | retired, unavailable, draining; not a ready canary host |
| `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&unscoped=include` | complete list envelope: 7,265 returned rows; 51 explicitly bound to CARD-0824; appendix below |

A 04:59 repeat with `includeChecks=true` returned 23,660 rows and 52 CARD-0824-bound tasks: the only added bound row was newly commissioned server2 cleanup `374d7fa0`, created 04:52:37Z. No Check/Distill/Diagnose row was bound to this card. The initial list's `excluded.total=1232` describes rows outside the requested scope, not lost pages of CARD-0824. No `since` or status cap was used. The 17 non-task directory names below are not thereby established absent from the fleet. The phone-home status route's desktop placeholder returns `disconnectReason=desktop`; it is **not** a desktop outage observation. Desktop liveness here comes from the catalogue, not that placeholder.

Raw local reads and summaries are in `/tmp/card-0824-e438ae14/`: `card-history.json`, `tasks.json`, selected `task-<id>.json`, `status-<id>.txt`, `ancestry.txt`, and `directory-sizes.tsv`. These are uncommitted generated evidence. Essential identities, counts and observations are retained in this document so its conclusion does not depend on that temporary directory surviving.

No build, test, mutation, cleanup, settings write, restart or deployment ran in this investigation. All test/probe outcomes below are attributed to their original stored reports, not claimed as fresh verification.

## Operator decisions and changes to the slice map

The original plan is [revision 2](../superpowers/plans/2026-11-02-card-0824-per-slot-worktrees-plan.md), originally `b6bb58e763c9a8cdc5c8ff86f31e344b92e36a93` on `feat/card-task-a6025c48`; master carries its plan/decision commits `3b3501f44cc0833e2264cd6cd375c586ede69eb2` and `8bdad1cf42cd2c7f43e83d7d4273215170a768d6`.

Card revision 6 records H-1..H-8 on November 2: H-1 yes to remote deletion after confirmed land, default off pending per-volume drain, 24h delay and leased source SHA; H-2 hold at bounds, reusable overflow and re-home an unusable directory; H-3 native stable `bin/obj`, no custom output; H-4 warm by default, cold only explicit/scheduled/pre-rc; H-5 retain Markdown/report.json for seven days, 256MiB/task; H-6 automatic legacy release, originally terminal >=48h; H-7 check the seven old mirrors once then close; H-8 S1, S4, S5, S2, S3. The verbatim answer and label mapping are in the plan's §7 and the current card description.

The landed [November 3 amendment](../superpowers/plans/2026-11-03-card-0824-s4-s5-amendment.md), commit `d52c56fa950e773ce8d28073a057c0b828089539`, supersedes original S3/S4/S5 and original CP-11/13/14:

- S4 runner cleanup/disk alarm is supplied by CARD-1677. Its approved terminal cutoff is **12h**, distinct from S4a's **48h** desktop cutoff.
- S4a is automatic desktop legacy release; S4b is read-only slot inventory/attention.
- S3 becomes S5a Windows core plus S5c desktop-run consumer; S5b handles desktop halves of runner tasks.
- S5-0 investigates whether those desktop halves should instead be ref-only.
- S1c adds the role gate and soft rollback needed for a small canary.
- The seven September worktrees are closed in amendment §3.4: absent; committed work retained on origin, including the differently named `28fc3bab` review branch. Do not reopen that migration prerequisite based on the obsolete original table.

Card revision **27**, `2026-11-03T21:06:08.32369Z`, records later operator answers: Q-1 permits warm leases during Windows qualification but fences cold resets; Q-2 accepts an S1 canary with no surviving custody-critical mutants, other survivors becoming repair cards; **Q-3 explicitly overrides the amendment's off default: S4a Enabled=true and Execute=true**. Q-4 role-wave clarification and A-D-6 slot-versus-ref-only remain open. No later revision answers them. These decisions are not a request for this investigator to activate anything.

## Slice status at master

Every SHA shown as landed was checked with `git merge-base --is-ancestor <sha> 28cdefab326782f2c91ca361768a7252dc875ec7` or located directly in that master's history. Historical task-branch SHAs may differ after landing; use these master SHAs for ancestry.

| Slice | Master evidence | Runtime / remaining status |
|---|---|---|
| **S1 runner pool** | runner `8395c4a431db27680e306aacb0ef46f1dbafbaa8`; server `e06ef30735e57772e5d20255ddf91cebeaf76267`; ownership/cold/drain repair `20fac33a1dc1d16982091d29ab5a7c8ccdc40ebf`; bounded unknown release repair `7ab9aa36cf88870410aada059071670c87528213` | **LANDED BUT OFF.** Both initial feature commits and release repair are ancestors of live `919d8d42…`; no slot capability, paths or ledger. Both server and runner flags default false. |
| **S1 mutation-survivor witnesses** | `929bad42f51562c8fcf39835afb1f46a74f2b242` | Landed, **not active**. Mutation `e87df8c7` found 13 survivors of 103; Code `a9f45276` and Review `a753db19` added/rechecked seven safety guards (details below). This is not a completed live canary or new SourceLanding certificate. |
| **S1c role gate** | `31569a95c408c118e51eca25257c9f4e84974fbd`; follow-up `81e732dd42252ae1314840a87c98603faeeca25f` | **LANDED BUT NOT LOADED.** `WorkspaceSlotRoles` defaults to the single string `"*"`; it has no effect while the outer flag is off. `"none"` is the documented soft rollback; arrays are invalid. |
| **S2 / H-1 remote branch deletion** | stub/schema `9dbb8caac840826018e8daf038b3a0c0267fd51a`; implementation `f95e7e03d7325d45bd905b663b403f474cd257d3`; review repairs `99747e3fd8f453f804f7e656690512082f1c0815`; enum-collision red witness `0eaa01e0e31384d7b58bbc8c9f82361336a94bde`; survivor tests `e4c9fdfe926a8bd453a7e87d9d6bd8fa33ff6d15` | **LANDED BUT OFF AND NOT LOADED.** Default flag false, cutoff null, runner allow-list empty, grace 1440 min. Not a mechanism reclaiming today's worktrees. |
| **S4 runner legacy retention / disk alarm** | CARD-1677 engine `1b6aaf36bc054d5a9c6b4246dae6a4a3459257c4`; guard/on-default change `2921957643833588c201a9fcc0c84bd98676242e`; alarm `bb0947982a3c170c14ee93d72b4a4f635436bb40`; later name guards `97e77f9f1fd6f71e2719bc661b48f8f89f129029` | **LANDED ON BY DEFAULT, NOT LOADED.** Enabled=true/Execute=true/server2 only, 12h, 25/tick. Live binary lacks the commits and `workspaceRetentionV1`. No automatic runner retention activation can be inferred. |
| **S4a automatic desktop release** | `3eb9d002738ae707b2247678400fdaba9cabbb52`; queued-dependent/bounded-cursor repair `b3610c57b842badeb864c643193e61afcb1c2f01`; survivor witnesses `14c6811182fa71e2c0fdae9ed445dca7a40e2b6a`; warning-aware precheck `e9d7837695dc5d39070de59a91f9940fadbc17e3`; shared CARD-1714 recheck/path repair `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c` | **LANDED ON BY DEFAULT, NOT LOADED; UNSAFE TO ACTIVATE.** Subsequent audits/reviews retain blockers. Current live SHA is not a descendant of any listed S4a commit. |
| **S4b slot inventory/attention** | Amendment §3.5 only; no `WorkspaceSlotInventory` op, `BuildWorkspaceSlotItems`, inventory test class or list command in baseline | **NOT STARTED implementation.** Ledger attention entries exist, but do not establish a working read/poll/attention path. Required before broad S1 widening; direct ledger reads suffice for the initial staffed canary under the existing amendment. |
| **S5-0 entry read** | `dc0b75e771f97efdc8820842c39dc81726e93a3e`, [report](2026-11-03-card-0824-s5-0.md), task `8c49a43b` | **LANDED investigation.** Recommends ref-only desktop halves; decision A-D-6 remains open. Prior measurement: 1,070/1,132 desktop checkouts were runner halves (94.5%). |
| **S5a desktop pool core** | Amendment §6.1 only; no `DesktopWorkspaceSlotPool` implementation | **NOT STARTED.** S1's runner pool is not a Windows desktop pool. |
| **S5b desktop halves** | Amendment §6.2 plus S5-0 consumer table; creation still calls per-task `CreateAsync` | **NOT STARTED.** Existing design choice is desktop slots versus ref-only; do not start two implementations. |
| **S5c / original S3 desktop-run slots** | Amendment §6.3 only; no desktop slot consumer | **NOT STARTED.** Lower-volume consumer, including native lock/long-path and qualification-fence acceptance. |

CARD-1677's temp sweep also landed (`c09d83312a50442624f94e98c7ddd58b72682e51`) but ships `TempSweep.Enabled=false/Execute=false`; it is not a missing CARD-0824 slice. Retiring branches and worktree cleanup are separate actions.

For S1 enablement evidence, Mutation `e87df8c7` was explicitly a waived ordinary-worktree sweep, not CARD-0478 SourceLanding evidence. It reported 90/103 kills, including seven safety-related gaps. Review `a753db19` of witness SHA `ae5f2550ff596ac744646567cb60e0826947e202` independently reapplied X37, X11, X31, X06, X17, PC-74 and X47: each went red at the named assertion, then restored green; checkpoint 128/128 across nine rows. Its ordinary scope remained Interim. This closes the earlier missing-witness question to that extent; it does not supply a loaded release/canary result. S1c Review `7c36e01b` had green role tests but inherited-load timeout evidence on CP-22 and `reviewedSourceClean=false`; preserve that limitation rather than calling all activation checks green.

## What a new Worktree dispatch does today

The persisted record for **this task** supplies a fresh example:

```text
GET /api/agent-tasks/e438ae14
summary.id=e438ae14-24e8-4a38-9764-58c4fafcd450
runnerId=server2
worktreePath=C:\Antiphon\worktrees\card-task-e438ae14
worktreeBranch=feat/card-task-e438ae14
worktreeBaseSha=28cdefab326782f2c91ca361768a7252dc875ec7
2026-11-05T04:45:39.360206Z Dispatched:
  Worktree created at C:\Antiphon\worktrees\card-task-e438ae14 on feat/card-task-e438ae14
  from 28cdefab326782f2c91ca361768a7252dc875ec7 (Explicit)
2026-11-05T04:46:06.892096Z Held:
  remote workspace preparation is in flight for runner 'server2'
2026-11-05T04:46:17.569624Z Held:
  prepared workspace is ready; awaiting dispatch admission
2026-11-05T04:46:22.208593Z Dispatched to task-e438ae14
```

The task is actually executing in `/work/worktrees/task-e438ae14`, as the assigned cwd and local directory demonstrate. These are DB-backed dispatch events, not screen-redraw or delivery assumptions.

Creation mechanism:

1. `server/Application/Services/AgentTaskDispatcher.cs:6000` calls `CreateForTaskAsync` under the repository lease. `DelegationWorktreeService.cs:379` forms `task-<id8>`; `:419` adopts only the same task's prior creation, and `:444` mints the task branch before `:445` calls the worktree manager. **RunnerId does not bypass ordinary desktop creation.** SourceLanding has a separate remote-snapshot exception at `:381`–`:406`.
2. `server/Infrastructure/Git/WorktreeManager.cs:90` constructs the desktop path using its `card-` directory prefix (`:20`); `:127`/`:131` issue `git worktree add`, reattaching an existing task branch or creating it. Thus `task-e438ae14` becomes `card-task-e438ae14` under the desktop root. Same-task recovery is not cross-task slot reuse.
3. `RemoteWorkspaceService.cs:213` requires the server flag, committed preparation and advertised `workspaceSlotV1`; `:267` reads that capability. `:308` constructs the legacy mirror request and only `:312` adds `Slot=true`. `MirrorName` remains task-specific.
4. `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:106` chooses the slot arm only when `request.Slot==true`; the bound legacy arm validates `task-<own id8>` at `:235`, forms `/work/worktrees/<name>` at `:241`, and creates it with `git worktree add -B <branch> <path> <sha>` at `:311`. The unbound arm does the corresponding add at `:179`. The `workspaceRepositoryV1` feature means the repository/mirror protocol exists; it does **not** mean reusable slots are selected.
5. The real but unused pool is `RunnerWorkspaceSlotPool.cs:24`; target capacity+headroom is `:60`; it opens the ledger at `:55`. `PhoneHomeCommandDispatcher.cs:145` adds `workspaceSlotV1` only when the runner flag is enabled; `:169` constructs the pool only then. `RunnerWorkspaceService.Slots.cs:39` invokes `LeaseAsync` and returns slot id/generation. Default false is `PhoneHomeWorkspaceSlotSettings.cs:9` and `DelegationSettings.cs:703`. The existing capacity 10 plus headroom 4 gives target 14, with bounded overflow 4; it does not bound directories while the legacy arm is used.
6. The server release reconciler is implemented (`WorkspaceSlotReleaseService.cs:47`, path clear `:85`) but its hosted loop is itself conditional on the outer server flag (`:275`). Soft rollback is `WorkspaceSlotRoles="none"`; switching the outer flags off strands existing leases until re-enabled/reconciled, per amendment §4.5.

S2 is likewise real but gated: `LandingSettings.cs:48` is false, cutoff `:57` null, runner ids `:66` empty. `PublishedBranchDeletionService.cs:55` selects confirmed-publication rows only after the cutoff, `:152` rechecks dependents, `:159` calls the leased delete; `LandingGit.cs:1644` issues the endpoint push with `--force-with-lease=<ref>:<expected SHA>`. This is neither an unconditional branch deletion at settlement nor a worktree retention policy.

## Disk and desktop creation measurements

Local shell reads, ending at `2026-11-05T04:49:44Z`:

```text
$ ls /work/worktrees | wc -l
503
$ find /work/worktrees -mindepth 1 -maxdepth 1 -type d -name 'ws-*' -print
[no output]
$ df -h /work/worktrees
Filesystem                         Size  Used Avail Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv  705G  541G  136G  81% /work
```

`du -sk /work/worktrees/*`, sorted numerically, returned 503 rows totalling **340,695,544 KiB = 324.913 GiB**. This is an allocated-byte walk during live activity, not a filesystem snapshot. Largest entries:

| Directory under `/work/worktrees` | KiB | GiB, approx. |
|---|---:|---:|
| task-fcf7ce80 | 3,154,496 | 3.008 |
| task-8b317bb8 | 3,153,244 | 3.007 |
| task-516b3a0a | 3,152,520 | 3.007 |
| task-2e6da28d | 3,151,708 | 3.006 |
| task-ac3dd93d | 3,151,704 | 3.006 |
| task-072cf7b1 | 3,151,484 | 3.006 |
| task-e8784f40 | 3,150,252 | 3.004 |
| task-1b618bf5 | 3,150,172 | 3.004 |

Joining exact `task-<id8>` leaves to the scoped task rows: 458 Succeeded, 10 Canceled, 9 Failed, 8 Dispatched, 1 Blocked; 17 other directories have suffixes (`task-cffa0c4b-{uA,uB,uC,wD,wE,wF,wG}` and `task-e38d6427-{combined,laneA,laneB,laneC,wE,wF,wG,wI,wM,wX}`). Those 17 do not exactly name a returned task directory; no deletion eligibility is asserted. Terminal status alone establishes neither pushed content nor absence of an owner.

Fresh API-only desktop-creation cross-check: among returned Worktree rows created from **2026-10-29 00:00Z through the 04:47 read**, 879 have a `C:\Antiphon\worktrees\card-task-*` path; 856 (**97.38%**) have a non-desktop runner, 23 are desktop-run. From November 4 00:00Z: 194 paths, 190 runner halves, 4 desktop-run. These are recorded creations, not a count of currently existing desktop directories. They independently reinforce S5-0's conclusion that **S1 alone cannot stop desktop checkout growth**.

Desktop filesystem/free-space measurement is delegated already to **`e601cbb4-1a39-4633-aa6a-25c9a9a4c639`**, `feat/card-task-e601cbb4`, Custom/Codex High, Windows desktop. `delegate.ps1 -Status e601cbb4` reported Dispatched, running/working at 04:48, publication unconfirmed. Its brief requires a proof-first inventory and limits removals; it explicitly forbids using/enabling S4a. Its result is not yet available here. Do not duplicate its deletion work or report its intended cleanup as completed.

A later read at **04:59 UTC** also found newly dispatched server2 cleanup **`374d7fa0-2978-4b9a-89c1-176f0dd0b977`**, `feat/card-task-374d7fa0`, Custom/Codex High, created `2026-11-05T04:52:37.832285Z`. It has no result yet. Its separately commissioned operator cleanup is distinct from S4a activation; this investigation neither executes it nor treats its intended reclamation as observed. Subsequent counts/free space can change while these two cleanup owners work.

## S4a defects and exact activation blockers

Shipping configuration is explicit: `server/appsettings.json:230`–`:241` and `WorktreeLegacyReleaseSettings.cs:18`/`:21` set Enabled/Execute **true**, cutoff 48h, cap 25, hourly tick. Current non-exposure comes from the **old binary**, not from proof that a live override has been applied. Master and remote rc/2026-11-06 `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` both contain S4a (`merge-base --is-ancestor 3eb9d002… <target>` exit 0); live `919d8d42…` does not (exit 1). Activation must account for that default.

### CARD-1717: audit B and rejected first repairs

Card `9a96da0c-13a8-482b-9ecc-77429b5f9928`, InProgress, Critical/Now. Original audit: task **`adbd81fe`**, subject `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c`. Full report and real-code reproducers remain at `/tmp/adbd81fe-evidence/docs/investigations/2026-11-04-s4a-independent-audit-b.md` and `probe-run.log`. No claim that its Linux reproductions were Windows executions.

| Defect | Confirmed mechanism / stored result | Present disposition |
|---|---|---|
| **D-1 switch bypass** | Policy samples the switch before awaited work; common retirement/sweep continuation does not universally reauthorize. Audit disabled both legacy flags, then residue sweep completed deletion. Master locations: `DesktopLegacyReleasePolicyService.cs:220`, `TaskWorktreeRetirementService.cs:317`, `WorktreeResidueSweepService.cs:201`. | Owner `362f7434`; first repair `96d7cde36af3868502900d77d5f8ab8fd7504941` rejected by Review `5242d604`. Follow-up `d5e8910e` at `35bb34ad7fe28541d8194f18e2bde10d211db6a4` **in Review `11a6d8bb`**, not landed. |
| **D-2 post-status writes** | After fourth real clean status, a new non-ignored only-copy file was written and deleted; status/lease is not OS writer exclusion. `GuardedWorktreeRemoval.cs:134` then delete `:193`. | Owner `73c94e0b`; initial repair rejected; further Code running. |
| **D-3 changed retained evidence** | Retention copied old bytes; same filename changed; only filename arrays were compared (`GuardedWorktreeRemoval.cs:128`, `:143`), then newer bytes were deleted. | Same owner; initial repair's missing-copy and crash/retry evidence authority still failed. |
| **D-4 set-aside replacement/new bytes** | Pending+unregistered directly deletes the recorded pathname (`GuardedWorktreeRemoval.cs:70`–`:104`) without re-establishing current identity/content. Real retry deleted a newly inserted file and a replacement directory. | Same owner; initial custody seal still missed equal-stat content changes, replacement restore and legitimate partial-progress recovery. |
| **D-5 ancestor symlink race** | Path-based Unix walk checks and later resolves separately (`WorktreeNoFollowDelete.cs:132`–`:145`). Native joined writer exchanged a child and an outside symlink: **85/200 outside files deleted**, then refusal. | Initial descriptor-relative repair preserved outside files in 200 review cases, but its other B-1..B-5 defects prevent landing/activation of that repair chain. |

Stored unedited audit observation:

```text
D2_LATE_UNTRACKED {"statuses":4,"Outcome":"Removed","ReasonCode":"removed","exists":false,"tree":false}
D3_EVIDENCE_BYTES_CHANGED {"Outcome":"Removed","ReasonCode":"removed","source":false,"retained":"retained-old"}
D4_RETRY_NEW_BYTES {"IsClean":true,"Residue":null,"retryStatuses":0,"asideExists":false,"notesExist":false}
```

Review **`3f52b55a-fcaf-4efd-baea-6a7f2f245362`** of `467b00c25ba04fb2a6cbe583079e06211c93c8a7` retains **B-1 missing retained-copy/crash-retry authority; B-2 equal-size/mtime byte changes; B-3 late writes and late repository markers; B-4 restore of a replacement without custody; B-5 partial-unlink recovery refusal; B-6 incomplete ordinary/native verification**. Report: `/tmp/3f52b55a-evidence/docs/investigations/2026-11-05-review-card-1717-a.md`. Its raw `evidence-retry-run.log` records:

```text
D3_CRASH_RETRY {"first":"worktree_removal_incomplete","IsClean":true,"Residue":null,"sameSeal":true,"sourceExists":false,"retained":"retained-old","mvid":"529a7493-30a1-47ab-9d44-8d61988393be"}
```

Its native-marker log reports 19,968 files still present after a new `.git` marker appeared, then all 19,968 deleted before a final directory-not-empty failure. These are results on the **repair candidate**, not a fresh run at this task's master.

Review **`5242d604`** rejected the first switch repair: four actual deletions after disable and one release inserted after disable. Source proof precedes awaited destructive continuations. Full report `/tmp/5242d604-evidence/docs/investigations/2026-11-05-review-card-1717-b.md`; exact task identity remains available through `GET /api/agent-tasks/5242d604`.

Live repair tasks, independently read with `pwsh -NoProfile -File scripts/delegate.ps1 -Status <id>`:

| Task / branch | Role and current evidence |
|---|---|
| `11a6d8bb` / `feat/card-task-11a6d8bb` | Review/Frontier on server2; Dispatched; running/working; reviewing switch follow-up `35bb34ad…`; no land requested/publication unconfirmed |
| `23889c9b` / `feat/card-task-23889c9b` | Code/High on server2; Dispatched; continues `73c94e0b` B-1..B-5 after failed predecessor `b5b7324a`; remote branch observed `3ec62c653a80c819fff5fecfd7b2ff8ea1bf6874`; no completed report or publication |
| `27e2f0b5` / `feat/card-task-27e2f0b5` | Code/High on server2; Dispatched; CARD-1718 A-D3 follow-up, below; remote `64e9c7cf9918848d0bc899292d7da67c6ef25ea3`; no completed report/publication |

`git merge-base --is-ancestor` for both `467b00c25…` and `35bb34ad…` against master returned **1**: repairs are not landed. Do not count original author “fixed” results as closure after a Found Review.

### CARD-1718: audit A's clean-proof holes

Card `fa097258-0d8f-49b8-992a-02f283fb3368`, InProgress, Critical/Now. Audit **`03e7cd6d`**, subject `37d92a8a0e86ed7200b3015df0d21dc6571a6672`; committed report `7190d151cba68c2a46d99b1d60d7baaf4e3388c9` on `feat/card-task-03e7cd6d`, path `docs/investigations/2026-11-04-s4a-independent-audit-a-grok.md` (not in baseline master).

| Item | Evidence and scope | Status |
|---|---|---|
| **A-D1 index flags** | `git status` exits 0 with zero stdout/stderr while skip-worktree `S` or assume-unchanged `h` hides changed tracked bytes. Both precheck `DesktopLegacyReleasePolicyService.cs:416` and `LandingGit.InspectAsync:841` lack the index-tag check; recovery inspection has one at `LandingGit.cs:1010`. Linux Git scratch reproduction; deletion consequence reconstructed from remover. | No separate bound implementation task found in complete card task list. Open. |
| **A-D2 case twins** | With `core.ignorecase=true`, `Readme.txt` and untracked `readme.txt` yield empty status on a case-sensitive directory; false reveals `?? readme.txt`. Same proof commands. Linux reproduction; Windows case-sensitive-directory execution pending. | No bound repair task found. Open. |
| **A-D3 ignored secrets** | Server protected patterns (`WorktreeCleanupSettings.cs:34`) omit `.env*`; broad disposable globs win for `node_modules/pkg/.env`, `bin/.env`, etc. Root `.env` remains default-denied. Classification initially reconstructed; follow-up Review actually exercises retention/removal. | Code `5e05fc80`, tip `249d4654d2410c4a3959a492ee0165e8a75b75f9`, **rejected** by Review `47d31366`: runner-retention bypass, server/runner minimum mismatch and secret-directory descendants remain. Canceled `1edbc845` replaced by active Code `27e2f0b5`; not landed. |
| **A-D4 ignored census warning** | `ls-files --others --ignored --exclude-standard -z` returns exit 0, empty listing and stderr warning for inaccessible ignored directory. `LandingGit.cs:849`–`:852` ignores this stderr. Audit explicitly observed later cleanup refusal preserving bytes; this is an incomplete proof, **not a demonstrated deletion in that scenario**. | No bound repair task found. Open. |
| **A-D5 alternate streams** | No Windows ADS enumeration before deleting a file and its streams. Source gap, Windows-only; **not reproduced** on Linux. | Native evidence/disposition required; no bound repair task found. |

Review `47d31366`'s external report `/work/evidence/review-47d31366/2026-11-05-review-card-1718-d3.md` reports 271 named passes, 8 native skips, and a 75-minute Unit timeout without a terminal TRX. B-1 points at candidate `RunnerWorkspaceRetention.cs:297,151` bypassing ignored classification, B-2 at runner protection floors `RunnerWorkspaceIgnoredContent.cs:19`, B-3 at secret directory descendants. Thus **runner legacy retention activation also needs this safety disposition**, not just desktop S4a. Do not automatically activate default-on retention as an isolated “safe cleanup” workaround.

### CARD-1708: reflog-only commits

Card `1d7e9b7c-adbb-4032-a04c-5137584749d9`, Backlog, no implementation task returned. Mutation `e5258e8c`, Code `e23c0f83` and audit A agree: clean branch-tip/origin containment does not cover earlier HEAD/branch reflog commits. Audit A reconstructed amend, reset and detached-commit/return-to-branch cases; the last has `HEAD==branch==pushed tip` while the only reference to `c716780e5b4708799e20492425fc0254169c826e` is worktree `logs/HEAD`. Unregister deletes that administrative log (`LandingGit.cs:774`–`:794`) and branch completion removes the source ref (`GuardedWorktreeRemoval.cs:310`). This loses the durable recovery name; objects may persist until GC, so immediate physical object disappearance is not established.

The card explicitly asks for an operator decision between holding such trees and retaining their recovery history/content. **No new choice is made here.** Stashes and unrelated refs are shared-repository state and are not this demonstrated loss mechanism. CARD-1708 is a blocker to automatic release activation under the caller's instruction.

### CARD-1719 and CARD-1721: separate seat-release dependency

CARD-1719 (`1a7f78d6-e12a-4b23-bb35-6d158259830d`, Review) is **ghost runner seats after Failed/CompletedWithoutProgress**, not another S4a filesystem defect. The card records sessions `303df159` and `c1a9c8f9` retaining seats for 26.7h/15.6h, manually released at 01:08Z. Its mechanism: `killSession=false`, automatic terminal release off, janitor marks PoolIdleSince/keeps live Failed sessions, and orphan discovery does not select them. Relevant recorded locations: `AgentTaskReplyService.cs:1562,2908`, `AgentTaskDispatcher.cs:9226,9469`, `RunnerSlotService.cs:31` at that investigation's source.

Code **`93a16649-de05-4087-a93c-9e3161d536f2`**, tested `e76378e45adf6d8f8dcb92af1adf17b345ad4f84`, has Review **`bbd446f6-31b8-466d-9866-1d7822a2e8dd`**: 139/139, no product blocker, **Interim ordinary scope, next Review; land not authorized**. Not in master's history. Do not convert the card's “Review” or the reviewer's “no blockers” into a land approval.

CARD-1721 (`a44944da-d90a-4ff9-b5e1-2240b1629db0`, Backlog, no bound task) records idle measured from completion rather than last activity, stale mirror evidence, missing release cap, attention wording and switch-on witness. The card and prior Review classify them as advisories and say they do not independently block land. They are not a code dependency of S4a. **The current brief nevertheless explicitly includes 1719/1721 in the no-activation gate; retain that caller gate until they land or the caller explicitly narrows it.** Their connection to S1 is direct: slot release requires a non-occupying session; otherwise a settled slot can remain leased. Their connection to S4a is its live-session hold, not a demonstrated S4a deletion path.

### Acceptance still missing

The exact activation stop list is therefore:

1. CARD-1717 A's B-1..B-5 and switch D-1 follow-up must receive successful ordinary Review and publication; currently repair/review work is active, not landed.
2. CARD-1718 A-D1/A-D2/A-D4 require assigned repairs and evidence; A-D3's three follow-up gaps are still in Code; A-D5 requires Windows evidence and an explicit disposition. A Linux source read is not native qualification.
3. CARD-1708 has neither a recorded operator choice nor an implemented/reviewed preservation contract.
4. Preserve the brief's CARD-1719/1721 gate; 1719 has only Interim Review and 1721 is unstarted. Their existing advisory classification is a caller reconciliation point, not permission to silently drop them.
5. Complete the required ordinary/native selections at the combined repaired source with trustworthy receipts. Reviews `3f52b55a`, `5242d604` and `47d31366` retain incomplete Unit/native evidence; original portable green rows do not discharge the native CP-25 cases or changed Windows helpers. No activation can be called verified from these reports.
6. Only then assess the exact rc and loaded server/runner SHA, configuration, durable existing releases/retries and publication state. Default-on S4a is part of that deployment assessment. No activation was attempted here.

Debug `86a941a3` subsequently attributes the `Narrower_parent_deadlines_win` console failure to fake-clock continuation scheduling and explains CP-29's missing TRX by the total execution budget: six isolated 7-case class runs passed across master and candidate; identical source was observed. Report commit `8f86f2c7a49149e0f60d5a1167563495465ee534`. This diagnoses that console/budget issue; **it does not supply the missing whole-Unit or native result**, nor an independently reproduced base failure under original load. Do not repeat an unbounded whole lane merely to relabel the Review.

## Prioritised continuation for Plan

Recommendation: **finish safety work before activating default-on deletion, then run the already designed S1 canary; prioritise S5b's desktop-growth decision over inventing another S1 implementation.** With 136G free, the existing >=84GiB canary disk prerequisite currently passes, but activation/readiness and safety gates do not. S1 reduces future server2 growth; guarded legacy cleanup reclaims backlog; S5b removes/bounds the much larger desktop creation stream. S2 remote-branch hygiene goes last because it neither reclaims build output nor safely substitutes for mirror drain.

The table commissions no product changes. Scopes/files below are existing card/plan work, with prioritisation and dependencies for a new Plan task. Estimates are provisional scheduling envelopes, not test timeout budgets or fresh measured p95. Prefer Codex for Code/Plan and mix independent Claude/Codex Review where available, per operator preference; availability must be checked at dispatch.

| Priority | Existing scope / files | Dependencies and role | Time and operator question |
|---|---|---|---|
| **0, already running** | Existing desktop cleanup `e601cbb4` and server2 cleanup `374d7fa0`; desktop inventory report `docs/investigations/2026-11-05-desktop-worktree-cleanup.md`. No S4a call. | Let both existing Custom/Codex owners finish; read their outcomes before duplicating work. Current investigator remains read-only. | Desktop brief budget 90min; server2 owner reports its own limit. **Decision:** only the held items it actually reports; no new activation approval requested. |
| **1** | Complete CARD-1717 A/B and CARD-1718 A-D3 work already active: `GuardedWorktreeRemoval.cs`, `WorktreeNoFollowDelete.cs`, `WorktreeSetAside.cs`, retirement/policy/sweep services, server/runner ignored-content/retention policies and their existing preservation witnesses. | Existing Code `23889c9b`, Review `11a6d8bb`, Code `27e2f0b5`; retain original landing owners `73c94e0b`, `362f7434`, `5e05fc80`. Serialize overlapping removal-service work. Fresh ordinary Review of actual combined repair; then land through owner. | Allow **1–2 pipeline days** including repair/review/native windows; low confidence while writers/custody defects remain open. **Decision:** none to continue already commissioned repairs; do not pre-authorize activation. |
| **2, planning alongside 1** | Commission the remaining CARD-1718 A-D1/A-D2/A-D4/A-D5 and CARD-1708 scope using their stored evidence; proof files `DesktopLegacyReleasePolicyService.cs`, `LandingGit.cs`, cleanup settings/classifier, reflog/admin removal; Windows evidence owns ADS disposition. | **Plan**, then TestDesign/Code/independent Review; avoid the shared files until priority 1 is published. This report intentionally selects no new preservation design. | **Plan 2–4h**, implementation estimate belongs to that plan (reserve another pipeline day provisionally). **Decisions:** CARD-1708 hold-versus-retention choice; explicit disposition/native proof for A-D5. |
| **3, parallel where disjoint** | Finish CARD-1719 ordinary review, reconcile CARD-1721: pool supervision, `RunnerSlotService`, held-seat attention and release witnesses. | **Review** for `93a16649` at its exact source; Code only for separately commissioned substantive work. Honour existing advisory classification and caller activation gate explicitly. | Review **2–4h** provisional; 1721 scope/estimate to be decided. **Decision:** caller either retains “1721 must land” or explicitly narrows that gate; do not infer waiver. |
| **4** | Prepare a safe loaded rc containing approved safety/retention/S1c source; then execute existing amendment §4's **S1 canary**, not new S1 Code. Relevant flags: `Delegation:WorkspaceSlotsEnabled`, `Delegation:WorkspaceSlotRoles`, `PhoneHome:WorkspaceSlots:Enabled`; slot release, ledger and retention services. | Priorities 1–3 plus remaining safety disposition; verify exact loaded SHAs and retention capability, custody-critical mutation disposition, no rolling phase, staffed observations. Operator deployment owner handles activation; Plan records readiness, Review judges evidence. | Existing canary **>=48h and >=40 releases**, likely 48–72h; observation every 12h. **Flags:** first-wave string `"Plan,Investigate,TestDesign,Docs"`; Q-4 later role-wave question remains open. No S1 enablement performed. |
| **5, can be prepared before 4 completes** | Existing **S4b** inventory/attention slice: `PhoneHomeContracts.cs`, `PhoneHomeCommandDispatcher.cs`, pool snapshot, `HostStatsPollService.cs`, `AttentionService.Alarms.cs`, attention DTO/client mapping, `workspace-slots.ps1`. | **Plan/TestDesign → Code → Review**; S2 already landed resolves the old shared-op sequencing issue. Needed before broad all-role widening, not before initial manually observed canary. | Existing amendment estimate **6–8h pipeline time**, plus actual queue/Review. **Decision:** no new product choice; preserve read-only inventory and fail-closed evidence contract. |
| **6** | Existing **S5b desktop halves** continuation from S5-0. Consumer scope: creation, push/prep, baseline, settlement/recovery sync, land identity/residue, reply/check/deliverables and cleanup. | **Plan now**, using S5-0 consumer table and late-land/path-reuse hazard; then TestDesign/Code/Review after shared-file collisions clear. If slots chosen, S5a core is prerequisite. | Existing S5-0 estimate **1.5–2 pipeline days** for ref-only S5b; slots additionally need S5a. **Decision A-D-6:** ref-only versus desktop slots; prior investigation recommends ref-only. This is the largest desktop growth lever (fresh 97.38% runner halves). |
| **7** | **S5a/S5c, original S3** Windows desktop core/consumer, native output/reset/locks and rc fence: worktree manager, planned `DesktopWorkspaceSlotPool`/Windows lock probe, creation/release consumers and qualification lock. | **Plan/TestDesign/Code/Review** conditional on A-D-6. Ref-only leaves this for the low-volume desktop-run population; slot S5b needs S5a first. | Re-estimate in Plan; amendment's Windows/core track is substantial, approximately **another 1–2 pipeline days**, no fresh measurements here. **Flags:** keep desktop pool off until native acceptance; warm leases allowed during rc per answered Q-1, cold resets fenced. |
| **8** | Finish legacy per-volume drain and **S2/H-1 enablement**; `PublishedBranchDeletionService`, `LandingSettings`, runner branch-ref deletion and checklist in orchestration owner. | Existing amendment §3.3 and §5.2: retention active, fresh drain pages, native CP-12/15, mutation disposition, exact new-publication cutoff and one disposable real-endpoint canary. **Plan/Review** readiness; operator applies settings. | **2–4h readiness/native window** provisional, then normal **24h grace**. **Flags:** per-volume runner allow-list, cutoff set to enable time, max 25, flag true only after gates. No historical backlog by default. |

This reorders work around new audit evidence without reversing H-1..H-8. Safety repairs are urgent prerequisites, not a reason to defer independent S4b or S5 decision preparation. Automatic legacy cleanup and new-slot reuse solve different populations. The obsolete September mirror closure and already landed S1/S2 implementations should not be commissioned again.

## Remaining uncertainties

- Exact server and runner slot flag overrides were not read from live configuration. Runtime non-use is established independently by capability absence, no pool directories/ledger and a fresh per-task creation. No claim is made that the configured server flag alone was measured false.
- Desktop count/allocated bytes/free space are not freshly measured here; 708 is revision-49/brief evidence. Await `e601cbb4` for its actual census and reclaimed/held result. API creation counts are a different measurement.
- Current directories are not a deletion candidate certificate: per-directory age, source/ignored/reflog proof and holders were not established. No expiry date for disk exhaustion is inferred from a single free-space sample.
- Initial audit A D-1/D-2 are reproduced Git proof gaps with deletion consequences reconstructed from source; D-3 initially used source classification; D-4's specific inaccessible-tree scenario preserved bytes; D-5 ADS is unexecuted Windows evidence. They are not five uniformly reproduced deletion runs.
- Repair candidates evolve. Status and remote tips are time-stamped observations; a 04:59 repeat still found all three active repair/review tasks Dispatched with no completion report. Refresh their statuses before dispatch or landing.
- Required native, ordinary and post-land Mutation obligations remain attached to their exact sources. Historic waived sweeps/Interim Reviews and newly landed witnesses are not substituted for missing receipts.
- A-D-6 and CARD-1708 require choices, but do not block producing this investigation or a Plan that separates independent work. CARD-1719/1721's caller gate versus advisory classification needs explicit reconciliation before activation.

## Not done, noted

Fix idea (not designed): carry the existing cards' source/content/recovery-custody requirements into one reviewed activation plan before permitting automatic destruction; choose the CARD-1708 and S5b contracts in Plan.

No product fix, activation or cleanup was performed. The existing operator authority was sufficient to complete this investigation and prioritisation without another go-ahead.

## Appendix: all CARD-0824-bound task rows

Snapshot source: the board-scoped GET above, initially 04:47 UTC, repeated with `includeChecks=true` at 04:59 UTC (52 bound rows). Branch shorthand is exact: every ID below uses **`feat/card-task-<id8>`**. Full GUIDs are recoverable via `GET /api/agent-tasks/<id8>`. “Succeeded” is task settlement only; publication/activation classifications are in the slice table. Related defect cards' active tasks are listed separately above.

| ID | Role | State | Runner | Title |
|---|---|---|---|---|
| cddf06e9 | Plan | Succeeded | server2 | Plan CARD-0824: per-slot reusable worktrees |
| a6025c48 | Plan | Succeeded | server2 | Plan CARD-0824 revision: record operator decisions H-1 to H-8 |
| f189bd64 | TestDesign | Succeeded | server2 | TestDesign CARD-0824 S1: runner workspace slot pool |
| 6a12a89d | TestDesign | Succeeded | server2 | TestDesign CARD-0824 S2: delete origin branch after confirmed land |
| cf298805 | Code | Succeeded | server2 | CODE CARD-0824 S1: runner workspace slot pool behind a flag (default off) |
| 3a354688 | Review | Succeeded | server2 | Review round 1: CARD-0824 S1 runner workspace slot pool (flag off) |
| 00abf1bf | Code | Succeeded | server2 | Fix review D-1 D-2 D-3: slot pool ownership, cold reset, rebind |
| f4c293bc | Review | Succeeded | server2 | Review round 2 (diff-only): CARD-0824 S1 ownership proof, cold reset, rebind |
| aa89c309 | Code | Succeeded | server2 | Fix round 2: slot release slot_unknown bound and digest default |
| f742a321 | Review | Succeeded | server2 | Review round 3 (diff-only, blockers only): CARD-0824 S1 fix 2 |
| 08c004b5 | Code | Succeeded | server2 | C0824 slot server tests: 20 s budget to 2x measured p95 |
| d67c4ca2 | Investigate | Succeeded | server2 | Status of CARD-0824 per-slot worktrees: landed, enabled, remaining |
| e87df8c7 | Mutation | Succeeded | server2 | Mutation sweep (waived): CARD-0824 S1 per-slot worktree pool |
| d94b0c85 | Plan | Succeeded | server2 | Plan: CARD-0824 remaining slices S4 S5 S2 and the S1 canary |
| 9ee6f8ca | Code | Succeeded | server2 | CARD-0824 S2: delete the origin branch after a confirmed land (off) |
| 8c49a43b | Investigate | Succeeded | server2 | CARD-0824 S5-0: desktop halves, slot versus ref-only, Windows risks |
| 2bc285a9 | TestDesign | Succeeded | server2 | CARD-0824 S1c test design: role allow-list for the slot canary |
| 6db87da1 | Code | Succeeded | server2 | CARD-0824 S1c: role allow-list for the slot canary (off) |
| 1a3ccb7a | Merge | Succeeded | server2 | Merge batch 40: CARD-0824 docs (amendment, S5-0, S1c test design) |
| a9f45276 | Code | Succeeded | server2 | CARD-0824 S1: tests for the mutation survivors X37 X17 X11 X31 X06 |
| 370083f1 | Review | Succeeded | server2 | Review: CARD-0824 S2 origin branch deletion after a confirmed land |
| 7c36e01b | Review | Succeeded | server2 | Review: CARD-0824 S1c slot role allow-list and sticky decision |
| a753db19 | Review | Succeeded | server2 | Review: CARD-0824 S1 mutation-survivor tests X37 X17 X11 X31 X06 |
| 9ac68027 | Code | Succeeded | server2 | CARD-0824 S2: CP-17 CP-18, guard witnesses, review advisories |
| e814f8ee | TestDesign | Succeeded | server2 | CARD-0824 S4a test design: automatic desktop worktree release (on) |
| 250763e5 | Code | Succeeded | server2 | CARD-0824 S4a: automatic desktop worktree release (on by default) |
| 84692314 | Review | Succeeded | server2 | Review r2: CARD-0824 S2 follow-up CP-17 CP-18 guard witnesses A-1..A-4 |
| a1fb2dc7 | Merge | Succeeded | server2 | Merge batch 44: CARD-0824 S1c and survivor tests, CARD-1612, CARD-1677 witnesses |
| 1dcb8d90 | Review | Succeeded | server2 | Review: CARD-0824 S4a automatic desktop worktree release (on by default) |
| 56e7e9b7 | Code | Succeeded | server2 | CARD-0824 S4a D-1 D-2: queued dependents, bounded held pre-checks |
| 86a12341 | Code | Succeeded | server2 | CARD-0824 S2: renumber LandingBranchDeletion (41 collides) and rebase |
| 684dc939 | Code | Succeeded | server2 | CARD-0824 S2 re-cut on master: enum 42, rebase, re-record |
| 4ed13efe | Review | Succeeded | server2 | Review r2: CARD-0824 S4a D-1 D-2 fixes (on by default) |
| c0e71ca7 | Review | Succeeded | server2 | Review r3 (blockers only): CARD-0824 S2 re-cut on master |
| 29664acc | Mutation | Succeeded | server2 | Mutation sweep (waived): CARD-0824 S2 origin branch deletion |
| cb6b4d80 | Code | Succeeded | server2 | CARD-0824 S2 survivors: flag default, DryRun rows, containment, policy rows |
| e5258e8c | Mutation | Succeeded | server2 | Mutation sweep (waived): CARD-0824 S4a desktop worktree release |
| e23c0f83 | Code | Succeeded | server2 | CARD-0824 S4a: witnesses for the deletion-guard mutation survivors |
| 36b3dc05 | Investigate | Succeeded | server2 | Investigate: new run group-77 S4a policy tests time out on Windows |
| 6949514d | Investigate | Succeeded | server2 | Investigate: new run group-96 S4a native Windows tests fail |
| 4b9d4604 | Code | Succeeded | server2 | S4a native Windows tests: fix two expectations, add long-path untracked case |
| 291753f8 | Code | Succeeded | server2 | S4a policy tests: Windows-sized timeouts and bounded tick |
| 52410842 | Debug | Succeeded | server2 | Debug: S4a long-path untracked file not held, removal Partial (data loss hazard) |
| ff7ab869 | Review | Succeeded | server2 | Review: S4a status_untrusted hold and core.longpaths pre-check |
| 3fd6747e | Review | Failed | server2 | Astra audit: can S4a release or removal still destroy unsaved work |
| 56b0f9aa | Debug | Succeeded | server2 | Debug: Astra Review 3fd6747e agent process did not become ready |
| 7c7132b3 | Review | Failed | server2 | Astra audit A: can S4a release prove clean when it is not |
| adbd81fe | Review | Succeeded | server2 | Astra audit B: S4a race, delete and retry paths |
| 03e7cd6d | Review | Succeeded | server2 | Grok audit A: can S4a release prove clean when it is not |
| e438ae14 | Investigate | Dispatched | server2 | CARD-0824 state: which slices landed, what runs today, prioritised plan |
| e601cbb4 | Custom | Dispatched | desktop | Clean up old worktrees on the desktop (proof-first, manual) |
| 374d7fa0 | Custom | Dispatched | server2 | Clean up old worktrees on server2 (24 h+ terminal or orphan, 325 GB) |

## Stored checkpoint provenance (earlier Reviews; no new run)

These unedited lines retain the historical source binding and incomplete gate. Raw receipts and full reports remain at the named external evidence roots; these are not this investigator’s tests.

```text
CHECKPOINT CP-23 commit=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c build=ok filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=155 passed=155 failed=0 skipped=0 trx=/work/worktrees/task-adbd81fe/.antiphon/checkpoints/20261104-233219-7cee/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=96d7cde36af3868502900d77d5f8ab8fd7504941 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=60m slot=granted waited=0s dirty=0 source=96d7cde36af3868502900d77d5f8ab8fd7504941 sourceState=clean buildSource=verified
```

--- next stage ---
next: plan
handoff: Reconcile CARD-0824’s remaining safety/activation gates and existing slices from this evidence; retain the active CARD-1717/1718 repair owners, resolve CARD-1708 and S5b decisions, and commission no duplicate S1/S2 implementation. Keep automatic destruction unactivated until the documented repairs and verification gates close.
artifact: docs/investigations/2026-11-05-card-0824-state-and-plan.md
