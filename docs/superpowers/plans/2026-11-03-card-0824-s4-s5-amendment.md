# CARD-0824 plan amendment: the remaining slices (S4, S5 with S3, S2) and the S1 canary

Plan task d94b0c85, 2026-11-03, server2 runner mirror (Linux), base master
`e56e4d1b6a727b3127b473307b860b7117980f09`. Docs only. No code, build or test ran.

This amends the [per-slot worktree plan](2026-11-02-card-0824-per-slot-worktrees-plan.md) (revision 2,
"the plan" below). It replaces the plan's §6 S3, S4 and S5 and its rows CP-11, CP-13 and CP-14. It adds a
canary slice S1c and a §8 rollout. S1 (landed) and the S2 test design
(`docs/superpowers/plans/2026-11-02-card-0824-S2-test-design.md` at `e59ca744c` on
`origin/feat/card-task-6a12a89d`) are unchanged except where §5 says so.

Inputs read: CARD-0824 with the operator decisions of 2026-11-02 (H-1..H-8); the plan; the status
report `docs/investigations/2026-11-03-card-0824-status.md` (origin `feat/card-task-d67c4ca2`); the
CARD-1677 plan `docs/superpowers/plans/2026-11-03-card-1677-runner-worktree-retention-plan.md`; the S2
test design; and the code named in the tables below.

## Summary

1. **S4 shrinks to two small slices.**
   - CARD-1677 already delivers S4's runner half: retiring legacy `task-*` mirrors on server2, the
     census, the batch bound and the disk alarm.
   - What remains:
     - **S4a**: the *desktop* automatic legacy release policy (H-6). Nothing retires desktop
       `card-task-*` worktrees today, and the desktop gains about 85 a day.
     - **S4b**: a read-only slot inventory, plus server attention for the slot events that the runner
       already records but nobody can see.
   - The per-volume legacy drain that gates S2 needs no code. It is a documented read of CARD-1677's
     run pages (§3.3).
   - The seven 2026-09 server2 worktrees are **closed now** by this amendment (§3.4). The receipts
     were found, and all seven have their committed work on origin.
2. **S3 and S5 merge into one track, S5, cut by layer.**
   - **S5a**: the Windows desktop pool core, flag off, with no consumer.
   - **S5b**: the desktop halves of runner tasks lease from it. This is the value: about 100 to 175
     checkouts a day become a bounded pool.
   - **S5c**: desktop-run tasks lease from it (the old S3 consumer, 1 to 8 tasks a day). S5c is also
     where native `bin/obj` locks, the cold reset and the rc fence matter.
   - Before S5b, an entry read (S5-0) decides between a desktop slot and a ref-only desktop half
     (D-6).
3. **The S1 canary needs a small slice first: S1c.**
   - The plan's canary ("docs-only roles first") cannot be expressed today. One server flag puts
     every runner task with a committed preparation on a slot (`RemoteWorkspaceService.cs:218-221`).
   - That flag is also the only thing that runs the release reconciler (`WorkspaceSlotReleaseService.cs:266-275`).
     So "flag off" as a rollback strands every leased slot.
   - S1c adds a hot-reloaded role allow-list. With it, the canary is one setting, and a soft rollback
     (`[]`) stops new leases while releases continue.
4. **S2 Code can start now.** It ships off. It touches landing plus one runner op, and it does not
   depend on S4 or S5 types. Enabling it needs four conditions (§5.2).
5. **Order:**
   - **Today:** S2 Code, the S1c TestDesign, the S4a TestDesign and the S5-0 read can start in
     parallel.
   - **Next:** S1c Code, then the canary after rc/2026-11-04 activation and the Mutation e87df8c7
     verdict; S4b; S5a then S5b; S5c last.

## 1. Ground truth: what the card, plan and brief assume vs what the code does

| Assumption | Code or state at `e56e4d1b6` (2026-11-03) | Consequence |
|---|---|---|
| CARD-1677 covers S4's legacy per-task release on server2 | Yes, for **server2 runner mirrors**. `RunnerWorktreeRetentionService` (server) plus `RunnerWorkspaceRetention.cs` (runner): terminal ≥ 12 h, clean, nothing unpushed, branch on live origin, no holder, never `--force`, 25 per 30-min tick, `server/appsettings.json:230-250` on with `Execute=true`, `Runners=["server2"]`. It is the plan's §5 step 3 with stricter checks (CARD-1677 D-10). The plan's H-6 cutoff was 48 h; 1677 ships 12 h by operator decision (1677 D-15) | S4's V-24/V-25 and its runner bulk removal are **dropped** (D-1) |
| The same, for the desktop | **No.** 1677 is Linux phone-home only (1677 D-11, I-12). Desktop `card-task-*` worktrees are removed only through a caller-filed `TaskWorktreeRetirements` release (`TaskWorktreeRetirementService.EvaluateEligibilityAsync` returns `release_required`, `:259`), and `WorktreeResidue:Execute=false` (`server/appsettings.json:223`) | S4a: an automatic desktop release policy (H-6) |
| CARD-1677 covers S4's disk alarm (S3) | Yes, as `AttentionKind.WorkspaceDiskLow` at 85 percent used on `/work` (`bb0947982`). The plan's `max(20 GiB, 10%)` floor is not built | Adopt 85 percent. Drop the floor (D-2) |
| CARD-1677 covers the temp sweep (S2) | **Not landed.** Only `RunnerWorktreeRetentionSettings.TempSweep` exists, defaulting to off, with no consumer (`grep TempSweep` finds only the settings). It was never CARD-0824 scope either: checkpoint temp roots belong to CARD-0804/0805 (plan §3.4) | Not a CARD-0824 remainder. No dependency |
| CARD-1677 is active on server2 | **No.** The running server is `919d8d42f`, which does not contain `1b6aaf36b`, `292195764` or `bb0947982`. server2's live features lack `workspaceRetentionV1` (`GET /api/session-runners`, 2026-11-03 ~21:00Z). All three commits are on `origin/rc/2026-11-04` | Activating rc/2026-11-04 is a precondition of the S1 canary (§4.2 P-1) |
| Retention never touches slots | True, with three guards and witnesses: the server leaf grammar plus `RemoteWorkspaceSlotId == null`; the runner `IsSlotLeaf`; the runner ledger check, which fails closed (status report §3) | No new guard. The canary watches for a breach (§4.4 S-6) |
| The S4 watchdog route `GET /sessions/workspace-slots` and the attention keys exist | **Partly.** The runner records `Overflow`/`Quarantined`/`Rehomed`/`RehomeFailed`/`Retired` entries in `ledger.json` (`RunnerWorkspaceSlotPool.cs:1328-1329`, `WorkspaceSlotContracts.cs:22-26`). Nothing reads them: there is no route and no phone-home op. The server writes only a task event on a quarantining release (`WorkspaceSlotReleaseService.cs:93`) | S4b: one read-only op plus one attention builder. Not needed for the canary, because a server2 delegate can read `/work/workspace-slots/ledger.json` directly (§4.3) |
| The canary can start with docs-only roles (plan §8) | **Cannot be expressed.** The lease is requested when `_workspaceSlots && binding is not null && runner advertises workspaceSlotV1` (`RemoteWorkspaceService.cs:218-221`). There is no role term. `_workspaceSlots` comes from `IOptions<DelegationSettings>` at construction (`:67`), so a change needs a server restart | S1c (D-3) |
| Rollback is "flag off" (plan §5) | **Strands leased slots.** The reconciler runs only while `Delegation:WorkspaceSlotsEnabled` is on (`WorkspaceSlotReleaseService.cs:266-275`). With the runner flag off, the pool object is never built (`PhoneHomeCommandDispatcher.cs:165-167`), and a release is refused as `workspace_slots_disabled` (409, `:176`). Free base slots are never retired (only slots above the target are, plan §3.2a) | Soft rollback through S1c. Hard rollback is documented with its cost (§4.5) |
| S1 is in the running server | Yes, through `7ab9aa36c` (the round-2 fixes, so the D-3 drain rule is in), plus CARD-1649 `bc9ec48fb` and CARD-1670 `2151b008a`. The live server2 *runner* build SHA was not read; its feature list predates CARD-1677 | P-1 covers both through the rc/2026-11-04 activation |
| The S1 Mutation is running | Task `e87df8c7` "Mutation sweep (waived): CARD-0824 S1", Dispatched 2026-11-03T20:36Z on server2 | P-2 waits for its verdict |
| The seven kept server2 worktrees need a fate | Absent from `/work/worktrees` and unregistered (re-checked 2026-11-03: 273 `task-*` directories, 0 `ws-*`). Receipts: CARD-1105's old-volume census (`docs/investigations/2026-10-09-card-1105-old-volume-cleanup.md`) listed all seven local and origin branches as KEEP. Origin today holds all seven (§3.4) | Closed here (D-4) |
| Plan §5: 28fc3bab has no origin branch | Wrong name. `feat/card-task-28fc3bab` is gone, but `review/card-0665-final-28fc3bab` (`9c93ad137`) holds its Review work | Recorded in §3.4 |
| S5: the desktop half needs a checkout | Partly. Settlement sync runs `merge --ff-only` **in** `task.WorktreePath` and validates "the same registered checkout, clean, at exactly S" (`RemoteWorkspaceService.cs:756-770`). Baseline capture rev-parses `HEAD` in the worktree (`AgentTaskDispatcher.cs:7934`). About 40 server files read `WorktreePath` (grep count). The desktop half runs no build or session: the session runs on server2 | The S5-0 read decides slot vs ref-only (D-6). Native `bin/obj` locks do not arise for desktop halves (D-7) |
| The desktop creates a fresh worktree per task | Yes: `_worktrees.CreateForTaskAsync` at `AgentTaskDispatcher.cs:6000`, then `DelegationWorktreeService.CreateForTaskAsync:364-470` (adopt, or `CreateAsync` with a minted branch). There is no `core.longpaths` anywhere in `server/` or `src/` (grep) | S5a passes `-c core.longpaths=true` on every slot git call |
| A Restart Manager probe exists | `RmGetList` is wrapped only in `src/Antiphon.HostCleanup/Diagnostics/NativeWindowsFileUseSource.cs` (internal, `[SupportedOSPlatform("windows")]`) and in test helpers (`tests/Antiphon.Tests/TestHelpers/WindowsFileLockOwners.cs`). `server/Antiphon.Server.csproj` does not reference HostCleanup | S5a ports the query-only declarations into `server/Infrastructure/Git/WindowsFileLockProbe.cs`. It adds no new project reference (D-8) |
| The rc Windows run is visible to the server | The native lock is `C:\Antiphon\verification\native-run.lock` (`scripts/lib/release-gate.ps1:19,313`, `Enter-ReleaseGateNativeLock`). The run uses `C:\Antiphon\releases\candidates\rc-<date>-<sha12>`, never `C:\Antiphon\worktrees` (rc skill step 1) | The S5 fence reads lock presence, failing closed (D-9) |

## 2. Decisions (stated defaults; the plan proceeds under them)

Continuing the plan's numbering would collide with the S2 test design, so these are **A-D-n**
("amendment decision").

- **A-D-1 S4 drops its runner legacy removal and adopts CARD-1677.** CARD-1677 is the runner half
  (1677 D-10). Rejected: building the plan's batch-of-25 bulk mode in `RunnerWorkspaceService`. It
  would be a second deleter on one volume, and it would be less strict than 1677 (no live-origin
  check, and `RemoveAsync` runs `worktree remove --force` plus a repository-wide `prune`,
  `RunnerWorkspaceService.cs:584`).
- **A-D-2 The disk alarm is 1677's 85 percent `WorkspaceDiskLow`.** The plan's `max(20 GiB, 10%)`
  floor is dropped. On the 705G volume, 85 percent used leaves 106G free, which is already stricter.
  Rejected: a second kind or condition, which would be two alarms for one fact.
- **A-D-3 S1c: canary and soft rollback through a hot-reloaded role allow-list.** The setting is
  `Delegation:WorkspaceSlotRoles`, one string read through `IOptionsMonitor` at lease time (corrected by
  the S1c test design, 2026-11-03, T-D-1..T-D-6).
  - `"*"` (the default) means every role except Mutation, which keeps S1's semantics.
  - `"none"` means no new leases (the soft rollback). The reconciler is unaffected and keeps releasing.
  - Otherwise a comma-separated list of exact-case role names (`"Plan,Docs"`).
  - An array value is refused: `[]` binds to `null`, which would widen the canary to every role.
  - Mutation and SourceLanding never lease, whatever the list says (plan D-8).

  Rejected alternatives:
  - **(a) A per-task opt-in flag** (`delegate.ps1 -WorkspaceSlot`). It needs every dispatcher to
    change, and it gives no soft rollback.
  - **(b) Canary by runner.** server2-temp is retired. A second runner would need the staged rollout.
  - **(c) Enable for all roles at once.** That is 100 to 175 tasks a day on unexercised code.
  - **(d) Decoupling the reconciler from the flag instead.** It solves rollback but not canary scope.
- **A-D-4 The seven 2026-09 worktrees are closed.** "Absent on 2026-11-02 and 2026-11-03; committed
  work on origin for all seven (six task branches, the 28fc3bab review branch, plus the c03af147
  rescue branch); uncommitted bytes unrecoverable." No archive is kept on their behalf: no archive or
  donor volume is named in any receipt, and the CARD-1105 old volume was later reused as the current
  `/work` (`docs/investigations/2026-10-17-server2-recycle-recovery.md:60,218`).
  - The branches are never deleted by S2: S2 deletes only after a confirmed land, and none of the
    seven landed.
  - Rejected: a host ssh census for a donor volume. No receipt names one, and the operator's rule was
    "check once, then close".
- **A-D-5 S3 merges into S5, cut by layer** (S5a core, S5b runner halves, S5c desktop-run tasks).
  - S3's mechanics (the Windows pool, longpaths, the lock probe, re-homing, desktop legacy-path
    refusals) are exactly what S5 needs.
  - S3's own consumer (desktop-run tasks, 1 to 8 a day) is the low-value half.
  - Rejected: the plan's order (S5 after S3 as separate slices, with S3 last per H-8). S5 cannot ship
    without S3's mechanics, so the H-8 order "S5 before S3" was not executable as written.
  - This keeps H-8's intent (desktop halves before desktop-run tasks).
- **A-D-6 S5-0 decides slot vs ref-only for desktop halves before S5b's TestDesign.**
  - A ref-only half (no desktop checkout for runner tasks; settlement sync as a compare-and-swap
    `update-ref` and no `merge --ff-only`) would remove the desktop registry growth outright and needs
    no pool.
  - It is not chosen now. About 40 consumers read `WorktreePath`, and settlement sync's postconditions
    assume a registered checkout.
  - Default if S5-0 is inconclusive: **slots** (the plan's design).
  - Choose ref-only only if S5-0 shows that every runner-task consumer needs refs alone.
- **A-D-7 Desktop halves have no native build output.** A desktop half runs no build or session, so
  its warm and cold resets are equivalent. Native lock handling (testhost, node, PtyHost, MCP) matters
  for S5c, not for S5b. S5b still runs the lock probe as part of the free invariant, because Defender,
  the indexer or an operator's editor can hold a file.
- **A-D-8 The lock probe has two tiers and is query-only.**
  1. Before a reset: a process census for any process whose main module path is under the slot
     (testhost, dotnet, node, PtyHost apphost, MCP servers launched from `node_modules`).
  2. After a sharing or access failure: `RmGetList` on the **exact failing paths** git reports, with
     at most 64 files.

  It never registers a whole tree with Restart Manager: RM is per file, and a 9,290-file tree would
  blow the bound. The probe never kills anything. A persistent holder quarantines the directory and
  re-homes the slot (plan §3.5a).
- **A-D-9 rc fence: no cold or scheduled reset of a desktop slot while `native-run.lock` exists.**
  - Lock presence is read with an existence and readable check. Unreadable counts as held (fail
    closed).
  - A cold-reset request, the weekly schedule and idle retirement record `Pending(rc_fence)` and run
    after the lock is gone.
  - **Warm per-task lease and release continue during a run.** They replace today's `worktree add`,
    which already runs during rc runs. Blocking them would hold every runner task's desktop half for
    the many hours of a Full run.
  - The pre-rc cold reset (plan D-17) runs **before** the lock is taken. The orchestrator waits for
    the Free slots' outcomes. Leased slots stay pending until the run ends, never mid-run.
  - Slot code refuses any path outside `C:\Antiphon\worktrees\ws-*`, so the candidate directory can
    never be a slot.
  - Rejected: treating every warm reset as a "reset" under the brief's "never reset during an rc run".
    That would make the pool unusable for the run's duration. Say so if this reading is wrong (Q-1).
- **A-D-10 The desktop pool lives in the server process**, beside `DelegationWorktreeService`.
  - That is where desktop worktrees are created today, under the repository mutation lease. Resets
    are repository mutations of the canonical repo: they move a branch ref and delete refs (plan §3.7).
  - The desktop session runner (17204) is not involved.
  - The operator cold-reset route for the desktop becomes a local call, not the plan's
    `POST /sessions/workspace-slots/cold-reset` on the desktop runner.
- **A-D-11 The desktop pool is sized from runner capacity.** The target is (server2 capacity + desktop
  delegated capacity) + Headroom 4: 10 + 2 + 4 = 16 today. MaxOverflowSlots is 4.
  - The plan's desktop Headroom of 2 assumed desktop-run tasks only.
  - Desktop halves are held until settlement sync is confirmed, so they need the server2-sized
    headroom.
- **A-D-12 S4a's policy files a system release; it does not flip `WorktreeResidue:Execute`** (H-6).
  - It has its own `Enabled`/`Execute` switches and runs on a 48 h terminal cutoff (H-6).
  - For a task whose runner is listed in `RunnerWorktreeRetention:Runners`, the release **skips the
    best-effort runner mirror removal** (`TaskWorktreeRetirementService.cs:364`). CARD-1677 owns that
    mirror; the skip avoids `RemoveAsync --force` racing retention.
  - Default `Enabled=false`. Whether to ship it on, as 1677 did, is Q-3.

### Open questions (each has the default above)

- **Q-1 The rc fence reading (A-D-9).** Warm per-task lease and release continue during a Windows
  Full run; only cold and scheduled resets wait. Confirm, or say that the desktop pool must hold all
  resets, which holds desktop halves for the whole run.
- **Q-2 The S1 canary without a Mutation gate.** P-2 accepts survivors outside the custody-critical PC
  set as advisory. Confirm, or require a clean sweep.
- **Q-3 Ship S4a on by default** (`Execute=true` for terminal ≥ 48 h), as CARD-1677 did?
  Default: off, with the preview route first.
- **Q-4 Canary roles.** Plan, Investigate, TestDesign and Docs first; Review and Code second; Debug and
  Custom last. Confirm the waves.

## 3. S4 amended: only the remainder

### 3.1 Covered by CARD-1677 (no CARD-0824 work)

| Plan S4 item | Covered by | Note |
|---|---|---|
| §5 step 2, census of legacy `task-*` | 1677 `WorkspaceRetentionInventory` plus the run pages `GET /api/agent-tasks/worktree-residue/runs/{runId}` and the always-DryRun preview `POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview` | Unmatched names are reported as `identity_unknown` |
| §5 step 3, retire legacy mirrors in batches | 1677 S1 (`MaxRemovalsPerTick=25`, `PauseBetweenSeconds=5`) | Cutoff 12 h, not 48 h (operator, 1677 D-15) |
| V-24 dirty, unpushed or unknown kept | 1677 V-2..V-8 | |
| V-25 batch bound | 1677 V-32 | |
| V-26 `workspace_disk_low` | 1677 S3 `WorkspaceDiskLow` (V-60..V-65) | 85 percent, A-D-2 |
| Temp roots | Not S4 (CARD-0804/0805); 1677 S2 is unlanded anyway | |

### 3.2 Remaining: S4a, the desktop automatic legacy release policy (H-6)

**Why now.** This is the only thing that bounds the desktop before S5 ships. The desktop had 237
registrations, cleaned down to 99 on 2026-11-01, and creates about 85 a day. CARD-1677 never reaches
the desktop.

**Design.**
- A new recurring job, `DesktopLegacyReleasePolicyService` (`antiphon:desktop-legacy-release`, cron
  `15 * * * *`, `DisableConcurrentExecution`). Settings `WorktreeLegacyRelease:{Enabled=false,
  Execute=false, MinTerminalHours=48, MaxReleasesPerTick=25, PauseBetweenSeconds=5}`.
- **Candidates.** Read from the task table, oldest `CompletedAt` first:
  - `Workspace=Worktree`, terminal for at least `MinTerminalHours`, `WorktreePath` set and under the
    desktop worktree root with leaf `card-task-<8hex>` matching the task;
  - no active `TaskWorktreeRetirements` row;
  - the same server predicates as CARD-1677 §3.2: no pending land, no sync debt in
    Pending/Ready/Held, no open task sharing the path, no live session, `Result` present.
- **Release and retire.** For each candidate, file a system `TaskWorktreeRetirement` release through
  `TaskWorktreeRetirementService.ReleaseAsync`. Caller = `system:desktop-legacy-policy`,
  `NoFurtherWorkspaceUse=true`, reason `CARD-0824 S4a terminal>=48h`. Then call
  `TryRetireAsync(release, runId)`.
- **Eligibility stays the service's.** `EvaluateEligibilityAsync`, which covers handoff, artifacts,
  recovery debt, live owner and unique owner, still decides. A refusal records the reason and leaves
  the release active, so a later tick retries it. One `WorktreeResidueRun` per tick records the
  candidates, with Lane `DesktopLegacyPolicy`. There is no migration: the CARD-1677 D-8 pattern.
- **Runner mirrors.** With A-D-12, `TryRetireCoreAsync` skips `RemoveRemoteMirrorAsync` when the
  task's `RunnerId` is in `RunnerWorktreeRetention:Runners`.
- **Execute=false** writes the run rows with `would_release` and files nothing.

**Files.**
- New: `server/Application/Services/DesktopLegacyReleasePolicyService.cs` and
  `server/Application/Settings/WorktreeLegacyReleaseSettings.cs`.
- `server/Application/Services/TaskWorktreeRetirementService.cs` (the system caller; the A-D-12 skip).
- `server/Program.cs` (registration, recurring job) and `server/appsettings.json`.
- Docs: `docs/orchestration-loop.md` (legacy cleanup paragraph) and `docs/ops-http.md`.

**Tests**: `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs`. Each test names
the mutant it kills [in brackets].
- L-1 terminal 47 h is not a candidate and 48 h is [`<=` vs `<`; drop the age term].
- L-2 each server predicate keeps the tree: land pending, sync debt Pending/Ready/Held, path shared,
  live session, `Result` null. These are `[Arguments]` rows [drop each predicate].
- L-3 `Execute=false` files no release and runs no git [file then skip].
- L-4 `Enabled=false` sends nothing and writes no run row [ignore the switch].
- L-5 an eligible tree is retired through `TryRetireAsync`, and the run row carries
  `Lane=DesktopLegacyPolicy` [bypass the service and call `IWorktreeManager.RemoveAsync`].
- L-6 a `TryRetireAsync` refusal (`handoff_pending`) leaves the release active and the tree present;
  the next tick retries [revoke on refusal].
- L-7 at most 25 per tick, oldest first [unbounded; newest first].
- L-8 a task on server2 does not call `RemoveMirrorAsync`, while a task on an unlisted runner does
  [drop the A-D-12 skip].
- L-9 a `ws-*` desktop path (S5) is never a candidate (`slot_managed`) [leaf grammar only].
- L-10 a crash after the release and before the retirement is retried once, with no duplicate release
  row [file a release per tick].

Regression: `TaskWorktreeRetirementTests` and `WorktreeResidueSweepTests`.

**Effort.** TestDesign 1.5 h, Code 3 h, Review 1 to 2 rounds: about 6 to 8 h of pipeline time.

### 3.3 Remaining: the legacy drain that gates S2 (docs only, in S2's docs step)

S2 enables per runner volume through `SourceBranchDeletionRunnerIds` (S2 TD B-5, Q-5). Its runner
step refuses while a legacy mirror still has the branch checked out (S2 TD G-25), so per-branch
safety is already in S2. The per-volume switch is the operator's confirmation that retention has
drained the backlog. **A runner volume is drained when:**
1. CARD-1677 is active on that runner (`workspaceRetentionV1` advertised), and
2. one retention preview run, read across every page, shows **no** `task-*` row with terminal ≥ 24 h
   (S2's delay) in `would_remove`, and
3. every kept row has a reason the operator accepts (dirty, unpushed and similar), and
4. no S1 slot is in `Releasing` or quarantined with custody class (from `ledger.json`, §4.3).

S2's docs step writes this as a checklist in `docs/orchestration-loop.md`. No code.

**Steady state.** Retention retires a mirror 12 h after the task becomes terminal. S2 acts 24 h after a
confirmed land. So the mirror is normally gone before S2 reaches its branch. A mirror that retention
keeps (dirty, unpushed) keeps its branch on origin through G-25. That is the safe direction: CARD-1677
D-14 keeps any mirror whose branch is absent on origin (`branch_absent_on_origin`). S2 must therefore
never delete a branch while a legacy mirror still has it checked out, and G-25 already says so.

### 3.4 Closed: the seven 2026-09 server2 worktrees (H-7, A-D-4)

Checked 2026-11-03 (`git ls-remote origin`, the CARD-1105 census tables):

| Task | Origin today | CARD-1105 old-volume census (2026-10-09) | Verdict |
|---|---|---|---|
| 19e7f181 | `feat/card-task-19e7f181` `75235a25d679` | KEEP, advertised, 1 patch not on master | closed, branch kept |
| 28fc3bab | `review/card-0665-final-28fc3bab` `9c93ad137b31` (the `feat/` branch is gone) | KEEP: `feat` 7 patches, tip in another advertised head; review branch 2 patches | closed, review branch kept |
| 7647cb6a | `feat/card-task-7647cb6a` `04de04356741` | KEEP, 2 patches | closed, branch kept |
| 7ebf8aa2 | `feat/card-task-7ebf8aa2` `4ba1d1110470` | KEEP, 1 patch | closed, branch kept |
| 89719e41 | `feat/card-task-89719e41` `248c2775cc21` | KEEP, 1 patch | closed, branch kept |
| c03af147 | `feat/card-task-c03af147` `b7bd0b748351` **and** `rescue/server2-wipe-c03af147` `4cb5eccb5da1` (diverged: 4 rescue-only and 3 feat-only commits, merge-base `7fb7bc5cb`) | KEEP, both | closed, both branches kept |
| e7c20aaa | `feat/card-task-e7c20aaa` `904bd75525c8` | KEEP, 3 patches | closed, branch kept |

Directories: absent from `/work/worktrees` and unregistered in `/work/repos/antiphon` (2026-11-02 and
2026-11-03). Uncommitted bytes, if any, cannot be recovered. The orchestrator records this closure on
CARD-0824 (a card note, not a column). Nothing in any slice depends on these seven.

### 3.5 Remaining: S4b, slot inventory and slot attention (read only)

**Why.** The runner ledger already records overflow, quarantine, re-home, re-home failure and
retirement, but nothing exposes them. A widened rollout (§4.6 step 3) needs them on the attention
feed. The canary does not: a server2 delegate reads the ledger file directly (§4.3).

**Design.**
- **Inventory op.** A read-only phone-home op `WorkspaceSlotInventory`, run outside `MutationLock`
  like 1677's inventory. It returns the plan §3.11 fields: per slot state, task, lease and directory
  generation, path, `createdAs`, idle time, last reset kind and duration, and pending cold reset; the
  quarantined directories with their exact paths; the ledger `Attention[]` entries newer than a
  cursor. There is no legacy count: that is retention's.
- **Server poll and attention.** The server polls it per runner in the existing host-stats poll
  cadence. `AttentionService.Alarms.cs` gains `BuildWorkspaceSlotItems`. It raises one Warning row per
  open quarantined directory (`workspace-slot-quarantined:<runner>:<path>`), clearing when the
  directory leaves quarantine. It also raises `workspace_slot_rehome_failed` and
  `workspace_slot_pool_degraded` while true. `workspace_slot_rehomed` and `workspace_slot_overflow` are
  Info rows that expire after 24 h.
- One attention kind with a key prefix, the CARD-1677 S3 pattern, not five kinds.
- `scripts/workspace-slots.ps1 list -Runner <name>` (ASCII-only).

**Files.**
- Runner side: `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (the op and DTO),
  `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`,
  `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs` (read-only snapshot).
- Server side: `server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs` (or a sibling poller),
  `server/Application/Services/AttentionService.Alarms.cs`, `server/Application/Dtos/AttentionDtos.cs`
  (one new kind, the next free wire value).
- Client: `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`.
- `scripts/workspace-slots.ps1`.

**Tests.** Runner: `tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotInventoryTests.cs`.
- I-1 the inventory lists every ledger slot and quarantined directory with exact paths [drop
  quarantined].
- I-2 it takes no `MutationLock`: a held lock does not delay it [take the lock].
- I-3 it never mutates: the ledger bytes are equal before and after [touch the ledger].
- I-4 the cursor returns only newer attention entries [ignore the cursor].
- I-5 an unreadable ledger returns `ledger_unreadable`, not an empty list [fail open].

Server: `tests/Antiphon.Tests/Application/C0824SlotAttentionTests.cs`.
- A-1 one row per open quarantined directory, which clears on release [no clear].
- A-2 re-home failure and pool degraded are Warning while true [inverted].
- A-3 rehomed and overflow rows expire after 24 h [never expire].
- A-4 a runner without `workspaceSlotV1` is never polled [poll all].
- A-5 a stale or failed poll raises nothing and marks the evidence `stale` [raise on failure].
- A-6 the wire value is stable (the `AttentionKindWireTests` pattern).

**Effort.** About 6 to 8 h of pipeline time (one runner op, one builder, the client mapping).

## 4. S1 enablement: S1c and the canary

### 4.1 S1c: role allow-list (server only, before the canary)

**Files.**
- `server/Application/Settings/DelegationSettings.cs` (`WorkspaceSlotRoles`, `string`, default
  `"*"`; the S1c test design T-D-1 grammar).
- `server/Application/Services/RemoteWorkspaceService.cs`: read `IOptionsMonitor<DelegationSettings>`
  at `MirrorAsync`, and add the role term to `:218`.
- `server/Program.cs` (the monitor is already registered with options).
- `docs/session-runtime-invariants.md` (CARD-0824 paragraph, `:327-344`) and `docs/docker-stack.md`
  (`:688-705`): the canary, soft rollback and hard rollback text from §4.4 and §4.5. The docs state the
  role setting as the single string `Delegation:WorkspaceSlotRoles` (default `"*"`, every role except
  Mutation; `"none"` for no new leases; or a comma-separated list of exact role names), and that an
  array value is refused: `[]` binds to `null`, which would widen the canary to every role. See the S1c
  test design, 2026-11-03 (T-D-1..T-D-6).

**Tests**: `tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleGateTests.cs`, using the existing
`C0824SlotTransportWorld`.
- R-1 with `"*"`, every role except Mutation on a slot-capable runner sends `Slot=true` (S1
  unchanged) [treat `*` as none].
- R-2 `"Plan"`: a Plan task sends `Slot=true`, while a Code task sends the legacy request shape,
  byte-equal to the flag-off request (CP-5's comparer) [ignore the list].
- R-3 `"none"`: no lease is requested, and the reconciler still releases an already-leased slot [gate the
  reconciler on the list].
- R-4 a changed monitor value applies to the next `MirrorAsync` with no restart [read once in the
  constructor].
- R-5 Mutation with SourceLanding never leases, even when listed [list overrides D-8].
- R-6 role names are matched by enum name, ordinally in canonical case. Other case, an unknown name,
  `Mutation`, an empty entry or an array value is refused by validation, and an invalid reload leases
  nothing [case-insensitive, or an unknown name read as all].

Regression: `C0824WorkspaceSlotServerTests` (14 methods).

**Effort.** TestDesign 1 h, Code 2 h, Review 1 round: about 4 h.

### 4.2 Canary preconditions (all must hold; the orchestrator checks each)

- **P-1 rc/2026-11-04 is active on the dispatching server and on the server2 runner.**
  - `GET /api/version` names a SHA that contains `f32aed42e`, `1b6aaf36b` and `292195764`.
  - server2 advertises `workspaceRetentionV1`.
  - This puts CARD-1677 retention live **before** slots, so the canary runs beside it from day one.
- **P-2 Mutation e87df8c7 has settled.** No surviving mutant in the custody-critical PCs:
  - the ownership proof before destructive git;
  - the reflog unpushed check;
  - quarantine instead of wipe;
  - never cold while leased;
  - `slot_managed` refusal;
  - the generation-checked release.

  Survivors elsewhere become fix cards, not a block (Q-2).
- **P-3 S1c is landed and active** (on master, ported to the active rc line, running).
- **P-4 Disk.** `/work` free is at least 64 GiB (pool budget) plus 20 GiB. It was 236G free on
  2026-11-03.
- **P-5 No server2 rolling phase is in progress.** The D-3 drain rule is present, but a canary that
  spans a drain doubles the variables.
- **P-6 Observation is staffed.** A server2 Investigate delegate is dispatched every 12 h during the
  canary (§4.3).

### 4.3 Canary configuration and observation

1. **Roles.** Set `Delegation:WorkspaceSlotRoles="Plan,Investigate,TestDesign,Docs"` (a single string;
   the default is `"*"`; see the S1c test design, 2026-11-03) and
   `Delegation:WorkspaceSlotsEnabled=true` on the dispatching server (restart, P-1 build). Set
   `PhoneHome__WorkspaceSlots__Enabled: "true"` on the server2 runner only (runner restart through
   `scripts/restart-session-runner.ps1`, or the rollout's runner phase). These are operator settings:
   this amendment does not change them. The desktop runner stays off: the S1 runner pool is Linux-only
   (`f32aed42e` gating).
2. **Duration.** At least 48 h **and** at least 40 released leases, so the plan's 20 warm-reset
   samples are reached. At 30 to 60 docs-role tasks a day on server2, that is 48 to 72 h.
3. **Each 12 h observation** is a server2 delegate, read-only, reporting:
   - `/work/workspace-slots/ledger.json`: states, quarantined directories, `Attention[]`, pending cold
     resets;
   - the reset span phases from the runner log (`docs/logs.md`): warm-reset p50 and p95;
   - every canary task's `AgentTaskEvents` for `workspace_slot_*`;
   - the 1677 retention run pages: no candidate row names a `ws-*` path;
   - settlement sync debt for slot-bound tasks;
   - `df /work`.
4. **One explicit cold reset** of one Free slot through `scripts/workspace-slots.ps1 cold-reset
   -Runner server2 -Slot <id>` in the first 24 h, to sample its p95.

### 4.4 Stop signals (any one: soft rollback, then a Debug task)

- **S-1 Leakage.** Any lessee sees another task's files: a `.antiphon/` inbox, report or checkpoint;
  `.claude/settings.local.json`; or a dirty tree at lease. Evidence is the task report or the lease
  verify refusal.
- **S-2 Lost work.** A slot-bound task's origin branch lacks a commit its report or transcript claims.
  Also any release that ran its reset after an unpushed-check error.
- **S-3** `workspace_slot_release_stuck`, `workspace_slot_rehome_failed`, or
  `workspace_slot_pool_degraded`.
- **S-4** More than 2 custody-class quarantines in 24 h. They are not a stop by themselves, but more
  than 2 a day means the release trigger is wrong.
- **S-5 Latency.** Warm-reset p95 over 30 s (2x the 15 s target), any lease RPC timeout, or a
  `workspace_slot_unavailable` hold older than 10 min.
- **S-6** A 1677 retention row or a legacy removal path acting on a `ws-*` path.
- **S-7** Slot-bound sync debt Held, or a Pending debt older than 1 h.

### 4.5 Rollback, and what happens to leased slots

- **Soft (default): `WorkspaceSlotRoles="none"`.** It is hot-reloaded, with no restart. The value is
  the string `"none"`, never an array: `[]` binds to `null`, which would widen the canary to every role
  (see the S1c test design, 2026-11-03).
  - New tasks get `task-<id>` mirrors at once.
  - Leased slots finish their tasks, and the reconciler releases them normally: the flag is still on.
  - When the ledger shows no slot in Leased or Releasing, turn both flags off. A runner restart with
    its flag off then ignores the ledger.
  - The Free `ws-*` directories stay on disk, up to about 18 × 2.5 GiB. CARD-1677 never touches them.
    Removing them is a separate, explicitly approved step: a guarded `git worktree remove` of exact
    `ws-NN` paths by a server2 delegate. No tool exists for this, and none is built unless the
    rollback becomes permanent.
- **Hard: the server flag off** (restart). New leases stop, **and the reconciler stops**.
  - Leased slots stay Leased in the ledger. Their tasks keep running and pushing normally.
  - Turning the flag back on releases them (`RecoverAsync` keeps Leased slots for the server's
    release). So does `record-release` per slot, for a stuck one.
- **Hard: the runner flag off** (runner restart). The pool is not built, and releases are refused as
  `workspace_slots_disabled`. Running sessions in `ws-*` cwds are unaffected beyond the restart
  itself.
- Use hard rollback only for S-1 or S-2. Nothing is lost either way: a stranded slot holds disk, not
  work.

### 4.6 Widening

1. After a clean canary: add Review and Code (Q-4 wave 2) for 48 h.
2. Then set `WorkspaceSlotRoles="*"` (the default: every role except Mutation).
3. Before step 2, land S4b, so that quarantines reach the attention feed rather than a 12-hourly read.
4. **CARD-1677 interaction while widening.**
   - The legacy population stops growing on server2 once all roles lease. Retention then drains the
     remainder in about 12 h plus the backlog at 25 per 30 min.
   - Retention phase A and the slot lease both run under the runner's `MutationLock`. The canary
     measures the lease's lock wait as its own phase. If S-5 trips on lock wait alone, lower
     `MaxRemovalsPerTick` first.

## 5. S2: can Code start now, and what gates enabling

### 5.1 Code can start now (ships off)

- It does not depend on S4 or S5 types (S2 TD intro).
- It touches `AgentTaskLandingProtocol`/`LandingGit`, a new `PublishedBranchDeletionService`, one
  runner op (`WorkspaceBranchRefsDelete`) and one migration.
- **Collision.**
  - S2 adds a phone-home op in `PhoneHomeContracts.cs` and `PhoneHomeCommandDispatcher.cs`, which
    S4b also edits. Run S2 Code **before** S4b Code, or give them disjoint op numbers (Code picks the
    next free value at its base).
  - S1c edits `RemoteWorkspaceService.cs` and S2 does not. S1c and S2 Code can run together.
- First land the S2 test design doc (`e59ca744c`) or carry it, and use its CP-12 and CP-15..CP-20
  unchanged.
- The S2 TD cost (Code about 52 min of machine time; PC floor about 860 min) needs its Debug timing
  task before the Mutation battery, as the TD says.

### 5.2 Before `DeleteSourceBranchAfterPublication=true`

- **E-1** CARD-1677 is active on each runner listed in `SourceBranchDeletionRunnerIds`, and §3.3's
  drain checklist is green for that volume.
- **E-2** A windows-native run of CP-12 and CP-15 is green (S2 TD "Native proof"). The sweep runs in
  the desktop server.
- **E-3** `SourceBranchDeletionAfterUtc` is set to the enable time, so no historical backlog is
  eligible (S2 TD Q-3), with `MaxSourceBranchDeletionsPerRun=25`.
- **E-4** A one-land canary on a disposable branch with delay 0 proves the real GitHub endpoint:
  protection, credentials, latency. Then delay 1440.
- S2's Mutation result is the same gate shape as P-2.

## 6. S5 amended: desktop slots (absorbs S3)

### 6.0 S5-0 entry read and measurement (Investigate, server2, read-only; about 2 h)

1. **Measure.** Desktop registrations joined to `AgentTasks.RunnerId`, from the API: `WorktreePath`
   per task for tasks created in the last 7 days, grouped by `RunnerId`. This proves or disproves plan
   §1's inference that most desktop checkouts are runner-task halves. No desktop shell is needed: the
   rows carry the path.
2. **Consumer table.** For each reader of `WorktreePath` on a runner task, does it need **files** (a
   checkout) or only **refs**? The readers: `CaptureProgressBaselineAsync`, `RemoteWorkspaceService`
   settlement sync (`:690-790`), `SettlementSyncRecoveryService`, `BlockedTaskSyncRecoveryService`,
   `TaskParkPublicationService` resume, `AgentTaskReplyService` sync, `ArmDenyHookAsync` (for a runner
   task the hook must reach the mirror), the land residue probe, `WorktreeHealthService`,
   `DeliverableBundleService`, `WorkspaceUseAdmission`, `CardWorktree*Cleanup*`, `WorktreeResidue*`,
   and `TaskWorktreeRetirementService`.
3. **Verdict:**
   - all refs-only: ref-only desktop half (A-D-6), and S5b becomes "no desktop checkout for runner
     tasks";
   - otherwise: slots.
   - Either way, S5a is still needed for S5c.

### 6.1 S5a: desktop pool core (Windows mechanics, flag off, no consumer)

**Design.** The plan's S1 contract, ported to the server process (A-D-10), for the canonical repo.
- **Layout.** Slot leaf `ws-NN` under `C:\Antiphon\worktrees`. Re-homes go to `ws-NN-dK`. The ledger
  is `C:\Antiphon\workspace-slots\ledger.json`, with the same schema as the runner ledger.
- **Reset, under the repository mutation lease.** It honours the CARD-1379 verification-window fence,
  as `WorktreeProvision` does today. Steps:
  1. take the lease;
  2. prove ownership (S1 review D-1, with Windows canonicalization: no reparse point on the directory,
     its `.git` file or the registration);
  3. run the free-invariant probe (A-D-8 tier 1);
  4. `git -c core.longpaths=true checkout --force -B <branch> <sha>`;
  5. `git -c core.longpaths=true clean -ffdx` with the keep-list `-e`;
  6. verify;
  7. on a sharing or access failure, run the tier-2 RM probe on the failing paths, then defer and
     retry (1, 2 and 4 min, 3 attempts), then quarantine and re-home.
- **Filesystem calls.** All .NET filesystem calls on slot paths use the `\\?\` prefix through one
  helper. No `cmd /c rd`, `Remove-Item -Recurse` or `Directory.Delete(recursive)` on a slot root:
  removal is `git worktree remove` (guarded) only.
- **Evidence retention.** `FileSystemEvidenceRetention` (CARD-1446) runs before the wipe.
- **Free slots** are detached at the fetched `origin/master`. Their local task branch is deleted after
  the unpushed check, which also keeps the desktop's attached-HEAD registry cost down (plan §1:
  attached 12.7 s versus detached 2.75 s at 1,000).
- **Desktop legacy paths.** They must refuse `ws-*` as `slot_managed`. This is the desktop analogue of
  plan §3.6:
  - `TaskWorktreeRetirementService`;
  - `WorktreeResidueSweepService`;
  - `CardWorktreeCleanupService` and `CardWorktreeCleanupExecutor`;
  - `WorktreeHealthService`;
  - `VerificationCleanupService`;
  - `WorkspaceUseAdmission` (compares slot id plus generation, not the path);
  - S4a's policy (L-9);
  - `WorktreeManager.PruneStaleAsync` (never prunes a locked quarantine).
- **Settings.** `Git:WorkspaceSlots:{Enabled=false, Headroom=4, MaxOverflowSlots=4,
  IdleRetireHours=72, ColdResetSchedule=Weekly, QualificationLockPaths=["C:\\Antiphon\\verification\\native-run.lock"],
  PoolDiskBudgetGiB=48}`. The paths are in backslash form (AGENTS.md).

**Files.**
- New `server/Infrastructure/Git/DesktopWorkspaceSlotPool.cs`, `DesktopWorkspaceSlotLedger.cs`,
  `WindowsFileLockProbe.cs` (the query-only RM declarations, `[SupportedOSPlatform("windows")]`, plus a
  portable `IFileLockProbe` seam), and `DesktopSlotPaths.cs` (the `\\?\` helper).
- `server/Infrastructure/Git/WorktreeManager.cs` (slot create, reset and remove with
  `core.longpaths`).
- `server/Application/Settings/GitSettings.cs` and the legacy-path
  consumers listed above.
- Pure shared policy in `src/Antiphon.SessionRunner.Contracts/WorkspaceSlotResetPolicy.cs`, reused
  unchanged.

**Tests.**

`tests/Antiphon.Tests/Infrastructure/C0824DesktopSlotPoolTests.cs` (portable, scratch repos, injected
`IFileLockProbe` and filesystem faults):
- D-1 the lease resets to the exact SHA and branch, keeps the keep-list and wipes the rest [keep
  everything].
- D-2 the reset refuses without the repository mutation lease [skip `Owns(lease)`].
- D-3 the reset waits for the verification-window fence [ignore the fence].
- D-4 an injected holder defers, then quarantines the directory and re-homes to `ws-NN-d2`; the old
  directory is `worktree lock`ed and intact [delete the old directory].
- D-5 an injected `Filename too long` re-homes [treat it as success].
- D-6 the ownership proof refuses a reparse-point or sibling registration with nothing run [skip the
  proof].
- D-7 evidence is retained before the wipe [wipe first].
- D-8 the unpushed check quarantines a commit that is not on origin [skip it].
- D-9 overflow growth and the disk-budget bound [unbounded].
- D-10 every git call carries `core.longpaths=true` (a command recorder) [drop it].
- D-11 the RM probe is called only with the failing paths, never the tree, at most 64 [register the
  tree].
- D-12 flag off: no pool, no ledger, and `CreateForTaskAsync` is byte-identical [build the pool
  anyway].

`tests/Antiphon.Tests/Application/C0824DesktopSlotLegacyPathTests.cs` (portable): each desktop legacy
removal path in the list above returns `slot_managed` for `ws-NN` and `ws-NN-dK`, and acts normally
on `card-task-<8hex>` [path-only check]. These are `[Arguments]` rows, one per consumer.

`tests/Antiphon.Tests/Infrastructure/C0824DesktopSlotNativeTests.cs` (windows-native, `[WindowsOnly]`
plus the NativeWindows policy row):
- N-1 a real child process (a copied `dotnet` exec of a tiny console app run from `bin/`) holding a
  DLL under the slot: the reset defers, RM names its pid, and after 3 attempts the directory is
  quarantined and the slot re-homed. The test kills its own child in `finally` [skip RM].
- N-2 a path of more than 260 characters under the slot resets and removes with `core.longpaths`,
  and fails without it (paired control) [drop `core.longpaths`].
- N-3 a read-only file plus a reparse point inside the tree: clean, then verify, then quarantine with
  the target intact [follow the link].
- N-4 the `\\?\` helper deletes a long path that `Directory.Delete` without the prefix cannot [no
  prefix].

Regression: `LandWorkspaceTests` (CARD-0688 land worktree unchanged, windows-native).

**Effort.** TestDesign 3 h, Code 6 to 8 h, Review 2 rounds, plus desktop-lane runs: **about 1.5
pipeline days**.

### 6.2 S5b: desktop halves of runner tasks lease from the pool (if S5-0 picks slots)

**Design.**
- At `AgentTaskDispatcher.cs:5999-6001`, a Worktree task with `RunnerId` set (a phone-home runner)
  and `Git:WorkspaceSlots:Enabled=true` calls `DesktopWorkspaceSlotPool.LeaseAsync(task, baseSha,
  branch, lease)` instead of `CreateForTaskAsync`'s `CreateAsync`. The base resolution, minted
  branch, `WorktreeBase*` recording and adoption logic stay where they are (`:364-470`). Only the
  checkout-creation call changes.
- `task.WorktreePath = <slot path>`. New additive columns `DesktopWorkspaceSlotId` and
  `DesktopWorkspaceSlotGeneration` (EF migration).
- **Release trigger** (a server reconciler; `WorkspaceSlotReleaseService` gains a desktop pass) when
  all of these hold:
  - the task is terminal;
  - its runner-side slot or mirror has released, or the task never had one;
  - settlement sync debt is Confirmed or None;
  - no live follow-up;
  - no pending land;
  - a Blocked task keeps its desktop slot (§8).
- **Reopen or reply after release** re-leases a slot at the branch tip. The branch is on origin,
  because the unpushed check passed at release.
- **The deny hook** is written into the slot and wiped at release (it is in the plan §3.4 wipe list).
- **Pool exhaustion** grows overflow, then holds with `workspace_slot_unavailable` (desktop), as on
  the runner.
- **Flag off** is byte-identical (`CreateForTaskAsync` unchanged).

**Files.** `server/Application/Services/AgentTaskDispatcher.cs` (`:5999-6001`),
`DelegationWorktreeService.cs`, `WorkspaceSlotReleaseService.cs`, `Domain/Entities/AgentTask.cs` plus
the migration, and every consumer S5-0 marks as needing a slot-aware identity (at least
`WorkspaceUseAdmission`, `TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync` and
`TaskWorktreeRetirementService`).

**Tests.**

`tests/Antiphon.Tests/Application/C0824DesktopHalfLeaseTests.cs` (portable, the `LandingSafetyHarness`
canonical repo):
- H-1 a runner task gets a `ws-*` desktop path, and a desktop-run task still gets `card-task-*` until
  S5c [lease everything].
- H-2 settlement sync fast-forwards inside the slot, with the postconditions met [validate the
  `card-task` grammar].
- H-3 release waits for Confirmed sync debt [release on terminal].
- H-4 release waits for the runner slot's release [ignore the runner].
- H-5 reopen after release re-leases at the origin tip with the task's own commits [lease at the
  base].
- H-6 a Blocked task keeps the slot with no deadline [release Blocked].
- H-7 two tasks never share a slot, and a stale generation cannot release [path compare].
- H-8 the baseline capture reads the slot HEAD [capture before the lease].
- H-9 flag off is byte-identical.
- H-10 a quarantined desktop directory raises the S4b attention when S4b is present, or a task event
  otherwise.

Regression: `SettlementSyncRecoveryTests`, `RemoteWorkspacePreparerTests`.

`tests/Antiphon.Tests/Application/C0824DesktopHalfNativeTests.cs` (windows-native):
- W-1 one real dispatch and settle through a phone-home fake runner on Windows: lease, sync,
  release, re-lease of the same `ws-NN`, with the registry count unchanged across 5 tasks [create per
  task].

**Effort.** S5-0 2 h; TestDesign 3 h; Code 8 h; Review 2 rounds; plus desktop-lane runs: **about 1.5
to 2 pipeline days**. Under ref-only (A-D-6), estimate again after S5-0. Expect a similar size: fewer
pool calls, more consumer edits.

### 6.3 S5c: desktop-run tasks lease from the pool (the old S3 consumer)

**Design.**
- Desktop-run Worktree tasks lease as in S5b. They are the only lessees with native `bin/obj`, so the
  warm keep-list, the cold-reset policy (plan §3.12) and the rc fence (A-D-9) matter here.
- **Pre-rc cold reset on the desktop.**
  - `workspace-slots.ps1 cold-reset -AllRunners` now also covers the desktop pool (local call).
  - The rc skill's pre-run step waits for the desktop Free slots' outcomes **before** taking the
    native lock, and records the request id.
  - Leased desktop slots stay `Pending(rc_fence)` until the lock is gone.

**Files.** `DesktopWorkspaceSlotPool.cs` (the fence, scheduled cold reset), `scripts/workspace-slots.ps1`,
`.claude/skills/antiphon-rc-qualification/SKILL.md` (pre-run step), `docs/release-gates.md`.

**Tests.**

`tests/Antiphon.Tests/Application/C0824DesktopRunSlotTests.cs` (portable):
- C-1 a desktop-run task leases, warm-keeps `bin/obj` and wipes the rest [wipe `bin`].
- C-2 a cold lease (`-ColdWorkspace`) wipes `bin/obj` [ignore Cold].
- C-3 a weekly schedule marks the Free slots, and a leased slot stays pending [cold while leased].
- C-4 S4a and the residue sweep refuse `ws-*` [path grammar only].

`tests/Antiphon.Tests/Application/C0824DesktopSlotQualificationFenceTests.cs` (portable, lock-file
seam):
- F-1 with the lock present, a cold request, a schedule boundary or idle retirement records
  `Pending(rc_fence)` and runs nothing [ignore the fence].
- F-2 an unreadable lock counts as held [fail open].
- F-3 warm lease and release proceed with the lock present [fence everything].
- F-4 lock gone: the pending items run once [run twice; never].
- F-5 a candidate-directory path is refused as `outside_scope` [no root check].

`tests/Antiphon.Tests/Infrastructure/C0824DesktopRunSlotNativeTests.cs` (windows-native):
- X-1 a real testhost-like child holding `bin\Debug\...\x.dll` during a cold reset: deferred, then
  quarantined and re-homed, with the old directory intact [skip the probe].
- X-2 a warm reset with an MCP-like `node` child whose cwd is under `node_modules`: deferred, then
  released after the child exits [kill the child; ignore it].

**Effort.** TestDesign 2 h, Code 4 to 6 h, Review 1 to 2 rounds, plus desktop-lane runs: **about 1
pipeline day**.

## 7. Recommended order, and what can start in parallel today

**Today, in parallel (server2; each in its own `-Worktree`; different source areas):**
1. **S2 Code** from the S2 test design (`e59ca744c`). It ships off. Area: landing plus one runner op.
2. **S1c TestDesign**, then Code. Area: `RemoteWorkspaceService.cs`, settings. Small.
3. **S4a TestDesign**. Area: `TaskWorktreeRetirementService`, a new policy service.
4. **S5-0 Investigate** (read-only: the measurement and the consumer table).
5. **Record the §3.4 closure** of the seven on CARD-0824 (an orchestrator card note).

**Gated:**

| Step | Gate |
|---|---|
| S1 canary (§4) | P-1 rc/2026-11-04 activation (operator-owned), P-2 Mutation e87df8c7 verdict, P-3 S1c landed and active |
| S4a Code | Its TestDesign. It can run beside S2 Code: no shared files |
| S4b Code | After S2 Code has landed (shared phone-home files, §5.1). Needed before canary widening step 2 |
| S5a TestDesign and Code | S5-0 verdict (S5a is needed either way, so its TestDesign may start once S5-0 has named the consumer list) |
| S5b | S5a landed, plus S5-0 = slots |
| S5c | S5b landed. Lowest value |
| S2 enable | §5.2 E-1..E-4 |

## 8. Waiting-input checklist

- **S1c** adds no waiting session.
- **S5b and S5c change what a Blocked task holds.** It now also holds a desktop slot, in addition to
  its seat and its runner slot.
  - Only the task reaching a terminal state (through the reconciler) or an operator cancel releases
    it.
  - With `BlockedTaskParking:Enabled=false`, **no automatic release deadline exists** (CARD-1083).
  - Headroom 4 plus 4 overflow absorb Blocked holders. Past that bound, `workspace_slot_unavailable`
    holds dispatch visibly.
- A cold reset pending on a Blocked task's desktop slot has no deadline either.
- **S4a** never selects a Blocked task: it is not terminal.

## Performance budget

This applies `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`.
No test ran in this task.

**Measured inputs.**
- Antiphon.Tests build p50 326 s and p95 579 s on loaded server2 (n=117), and SessionRunner.Tests build
  p95 135 s (n=11) (the plan's cf8b6982 receipts).
- The C0824 server class per-test p95 is 17.2 s loaded, which set the CARD-1660 35 s timeout
  (`0e88f5adb`).
- `git worktree add` takes 2.8 s on server2, and 40.4 s or 10.2 s on the desktop (loaded or after
  cleanup).
- The Windows build is **unmeasured**. The 15 min Windows row estimates are provisional, and the first
  S5a Code run measures them.

**Shared setup.**
- Server rows use the existing `TestDbFixture` (once per assembly), plus `C0824SlotTransportWorld`
  (S1c) or the `LandingSafetyHarness` (S4a, S5b), built once per class.
- Pool rows share one template scratch repo per class, cloned per test with `--shared` (≤ 2 s per
  class).
- No AppHost, container, migration run or browser is involved.

**Per-test bodies and timeouts (provisional; TestDesign replaces each with about 2x measured p95).**

| Class | Body | Timeout | Basis |
|---|---|---|---|
| Server policy classes (S1c, S4a, S4b) | ≤ 5 s | 35 s | CARD-1660 basis |
| Portable pool classes | ≤ 3 s | 30 s | |
| Windows-native classes | ≤ 20 s | 60 s | One child process per test under `ParallelLimiter<ProcessSpawnLimit>`, killed in `finally`; RM calls bounded at 10 s |

**Phases recorded per row:** build, fixture setup, bodies, teardown. Slot wait is reported separately
and is not counted in the estimates. TRX per row has per-test durations.

**Runs.** Every After group is one checkpoint run under 30 min:

| Group | Run time | Note |
|---|---|---|
| S1c | about 16 min | |
| S4a | about 20 min | |
| S4b | about 16 min | Two builds |
| S5a | portable about 16 min; windows-native about 19 min | The windows-native rows are a separate run in the desktop lane |
| S5b | portable about 20 min; windows-native about 15 min | |
| S5c | portable about 16 min; windows-native about 15 min | |

**Production budgets.**
- Desktop warm reset p95 ≤ 20 s (plan), with a 60 s lease budget inside the dispatch claim.
  - The claim already waits up to 180 s for `worktree add` today (the 7b8e4061 incident). S5b must
    measure the reset at lease time.
  - If S5b holds the repository mutation lease longer than today's `worktree add`, it reports that
    number.
- Canary server2 warm reset p95 ≤ 15 s (the S-5 stop is at 30 s), and cold reset p95 ≤ 60 s.

### Checkpoints

**Lanes.**
- `portable`: default placement. `GET /api/runner-defaults` gives `globalRunnerId=server2`, so omit
  `-Runner` and `-Platform`.
- `windows-native`: needs Windows. Today that is only the desktop runner (`GET /api/session-runners`:
  desktop windows, capacity 2). Pass `-Platform Windows`, and do not pin a runner.

**Builds.** Each build is the checkpoint tool's fresh `bin-` custody output with a forward slash,
through the build-slot gate. Each After group has its own builds: the import refuses reuse across
groups.

**Numbering.** CP-21 and up, so nothing collides with the plan's CP-1..CP-14 or the S2 test design's
CP-12 and CP-15..CP-20. The plan's CP-11 (S4), CP-13 and CP-14 (S3) are **superseded** by CP-23..CP-36.

**OR operands** carry parentheses and a trailing `*`, with the pipe escaped.

**Not validated.** `import --plan` was **not run** (this task ran no build). Each TestDesign runs
`dotnet run --project tools/Antiphon.Checkpoints -- import --plan
docs/superpowers/plans/2026-11-03-card-0824-s4-s5-amendment.md` and sets `Min` from the counted
`[Arguments]` cases.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-21 | S1c | tests/Antiphon.Tests -> bin-c0824-s1c/ | portable-slot-role-gate | /*/Antiphon.Tests.Application/C0824WorkspaceSlotRoleGateTests/* | R-1..R-6 | class, 0 failed/skipped | 6 | 12 | true |
| CP-22 | S1c | CP-21 | portable-slot-server-regression | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | S1 server half unchanged | existing class, 0 failed/skipped | 14 | 4 | true |
| CP-23 | S4a | tests/Antiphon.Tests -> bin-c0824-s4a/ | portable-desktop-legacy-policy | /*/Antiphon.Tests.Application/C0824DesktopLegacyReleasePolicyTests/* | L-1..L-10 | class, 0 failed/skipped | 10 | 12 | true |
| CP-24 | S4a | CP-23 | portable-retirement-regression | /*/Antiphon.Tests.Application/(TaskWorktreeRetirementTests*)\|(WorktreeResidueSweepTests*)/* | retirement and residue sweep unchanged | 2 existing classes, 0 failed/skipped | 34 | 8 | true |
| CP-25 | S4b | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s4br/ | portable-slot-inventory | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotInventoryTests/* | I-1..I-5 | class, 0 failed/skipped | 5 | 4 | true |
| CP-26 | S4b | tests/Antiphon.Tests -> bin-c0824-s4bs/ | portable-slot-attention | /*/Antiphon.Tests.Application/C0824SlotAttentionTests/* | A-1..A-6 | class, 0 failed/skipped | 6 | 12 | true |
| CP-27 | S5a | tests/Antiphon.Tests -> bin-c0824-s5a/ | portable-desktop-slot-pool | /*/Antiphon.Tests.Infrastructure/C0824DesktopSlotPoolTests/* | D-1..D-12 | class, 0 failed/skipped | 12 | 12 | true |
| CP-28 | S5a | CP-27 | portable-desktop-slot-legacy-paths | /*/Antiphon.Tests.Application/C0824DesktopSlotLegacyPathTests/* | desktop legacy paths refuse ws-* | class, 0 failed/skipped | 1 | 4 | true |
| CP-29 | S5a | tests/Antiphon.Tests -> bin-c0824-s5aw/ | windows-native-desktop-slot | /*/Antiphon.Tests.Infrastructure/C0824DesktopSlotNativeTests/* | N-1..N-4 | class, 0 failed/skipped | 4 | 15 | true |
| CP-30 | S5a | CP-29 | windows-native-land-regression | /*/Antiphon.Tests.Infrastructure/LandWorkspaceTests/* | CARD-0688 land worktree unchanged | existing class, 0 failed/skipped | 7 | 4 | true |
| CP-31 | S5b | tests/Antiphon.Tests -> bin-c0824-s5b/ | portable-desktop-half-lease | /*/Antiphon.Tests.Application/C0824DesktopHalfLeaseTests/* | H-1..H-10 | class, 0 failed/skipped | 10 | 12 | true |
| CP-32 | S5b | CP-31 | portable-sync-prep-regression | /*/Antiphon.Tests.Application/(SettlementSyncRecoveryTests*)\|(RemoteWorkspacePreparerTests*)/* | settlement sync and remote prep unchanged | 2 existing classes, 0 failed/skipped | 37 | 8 | true |
| CP-33 | S5b | tests/Antiphon.Tests -> bin-c0824-s5bw/ | windows-native-desktop-half | /*/Antiphon.Tests.Application/C0824DesktopHalfNativeTests/* | W-1 | class, 0 failed/skipped | 1 | 15 | true |
| CP-34 | S5c | tests/Antiphon.Tests -> bin-c0824-s5c/ | portable-desktop-run-slot | /*/Antiphon.Tests.Application/C0824DesktopRunSlotTests/* | C-1..C-4 | class, 0 failed/skipped | 4 | 12 | true |
| CP-35 | S5c | CP-34 | portable-desktop-rc-fence | /*/Antiphon.Tests.Application/C0824DesktopSlotQualificationFenceTests/* | F-1..F-5 | class, 0 failed/skipped | 5 | 4 | true |
| CP-36 | S5c | tests/Antiphon.Tests -> bin-c0824-s5cw/ | windows-native-desktop-run-slot | /*/Antiphon.Tests.Infrastructure/C0824DesktopRunSlotNativeTests/* | X-1, X-2 | class, 0 failed/skipped | 2 | 15 | true |

**Roster check** (grep of `class C0824*` in `tests/` at `e56e4d1b6`).

Existing C0824 classes:
- `C0824WorkspaceSlot{ColdReset,Custody,NativeRehome,Overflow,ProcessScan,Rehome,Reset,Server}Tests`;
- the helpers `C0824SlotScratch`, `C0824SlotTransportWorld` and `C0824StoreRuntime`.

None of the new names exists yet. Every new row uses an exact class name.

Prefix overlaps, harmless here but checked for any future OR:
- `C0824WorkspaceSlotRoleGateTests` and `C0824WorkspaceSlotRehomeTests` share `C0824WorkspaceSlotR`,
  but neither name is a prefix of the other.
- `C0824DesktopSlot{Pool,LegacyPath,Native,QualificationFence}Tests` share `C0824DesktopSlot`, but no
  full name is a prefix of another.
- `C0824DesktopHalfLeaseTests` is not a prefix of `C0824DesktopHalfNativeTests`.
- `C0824DesktopRunSlotTests` is not a prefix of `C0824DesktopRunSlotNativeTests`: that name
  continues `...RunSlotN...`, not `...RunSlotTests...`.

OR operands in CP-24 and CP-32: each existing operand matches exactly one class.
- `TaskWorktreeRetirementTests` (19 methods), `WorktreeResidueSweepTests` (15),
  `SettlementSyncRecoveryTests` (19) and `RemoteWorkspacePreparerTests` (18) are all in
  `Antiphon.Tests.Application`, and no other class starts with those names.
- `LandWorkspaceTests` (7) is in `Antiphon.Tests.Infrastructure`.
- `C0824WorkspaceSlotServerTests` (14) is in `Antiphon.Tests.Application`.
- Each `Min` is the `[Test]` method count; `[Arguments]` cases raise the real count.
