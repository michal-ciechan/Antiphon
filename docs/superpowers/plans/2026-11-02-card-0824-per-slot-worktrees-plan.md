# CARD-0824 plan: per-slot reusable worktrees

Plan task cddf06e9, 2026-11-02, server2 (Linux), base master `ead93feb00fcaf8bf31018599437f17bee8e934c`.
Read-only planning: no code change, build or test run. The operator authorised this planning stage
("Yes go for this", 2026-11-02).

**Revision 2 (plan task a6025c48, 2026-11-02, server2/Linux, base `3c273ba05`).** Records the operator's
answers to H-1..H-8 (§7 "Decisions recorded") and propagates them: overflow slots (§3.2a), slot
re-homing to a new directory (§3.5a), native build output kept by the reset (§3.4, §3.8), and the reset
policy with explicit, scheduled and pre-rc cold resets (§3.12). Docs only: no code, build or test run.

## Summary

**Problem.** Every Worktree task gets a fresh checkout, and settled checkouts are almost never removed
automatically. A runner task gets **two** checkouts: a desktop worktree (`C:\Antiphon\worktrees\card-task-<id>`)
and a runner mirror (`/work/worktrees/task-<id>`). So moving work to server2 did not stop the desktop
from accumulating checkouts.

- server2 held 502 mirrors (327G) on 2026-09-29, and **358 mirrors (133.3 GiB) on 2026-11-02 05:20Z**.
- The desktop held 1,025 registrations on 2026-10-31. Branch-attached registrations cost Git about 13 s
  and 1.6 GiB per `worktree list`/`add` at 1,000 entries. One dispatch failed on a 180 s add timeout.
- Retirement exists but needs explicit per-task release (zero `TaskWorktreeRetirements` rows) or
  `WorktreeResidue:Execute=true` (shipped false). Operators clean up by hand.

**Design.** Each runner (and, in S3/S5, the desktop) owns a bounded pool of **workspace slots**: one
reusable checkout per concurrent task, capped at runner capacity plus a small headroom. Settled tasks
release their slot after a guarded check.

- A task leases a free slot. The runner resets it to the exact SHA and branch the server resolved,
  verifies the tree, and binds it to the task (slot id plus generation).
- Release checks that nothing is unpushed (HEAD reflog since the lease, branch tip contained in
  origin). It retains evidence, wipes everything except the slot's **native build output** (the
  worktree's default `bin/`, `obj/`, `node_modules/`, `client/dist/`; no custom OutputPath), deletes
  the task's local refs and parks the slot detached at a published SHA.
- The default (warm) reset **never wipes native build output**. A **cold reset** (wipe it too) runs only
  on an explicit dispatch flag or operator action, on a schedule (default weekly, a setting), and before
  the start of an rc qualification run. It never runs while a slot is leased (§3.12).
- A directory that fails any check is **quarantined in place, never wiped**. The slot itself survives:
  it is **re-homed** to a new directory (`ws-NN-dK`) under the same slot id, so a catastrophic,
  unrecoverable directory (an undeletable file, a lock, a too-long path) never kills the slot (§3.5a).
- When every slot is leased, the runner grows a **reusable overflow slot** (bounded count and disk
  budget) instead of leaving work stuck. Only at that bound does dispatch hold (§3.2a).
- After a confirmed land, the landed source branch is deleted on origin with a lease on the landed
  SHA, after a 24 h delay. A refused or unconfirmed land keeps it.
- Disk is bounded by construction: about 14 base slots plus at most 4 overflow slots × ≤2.5 GiB on
  server2, inside a 64 GiB pool budget, against 133 GiB today.

**What it does not buy.** It barely affects build speed. Checkpoint builds stay cold by design
(the tool's own fresh `bin-<id>/artifacts`; CARD-1629). Direct builds use the slot's native output and
become incremental across tasks in one slot, but the measured gain is 6–41% per build (a d1/d2-class
edit costs 59–94% of cold). The value lies elsewhere:
- disk and registration counts bounded by construction;
- no per-task retirement machinery for new work;
- on the desktop, a reset that rewrites only changed files instead of a 30–40 s full checkout.

**Slices**, in the decided order **S1, S4, S5, S2, S3** (H-8):
- S1: runner slot pool, lease and warm/cold reset contract, overflow slots and re-homing, behind a
  per-runner flag (default off), Linux/server2.
- S4: migration and cleanup of legacy per-task worktrees (terminal ≥ 48 h, automatic release policy),
  and the disk watchdog.
- S5: desktop halves of runner tasks become desktop slots. This is the change that bounds the desktop
  registry.
- S2: delete the origin branch after confirmed publication, plus local mirror refs (flag, default off;
  enabled per volume after S4).
- S3: Windows slot support for desktop-run tasks (long paths, file locks, re-homing, evidence retention).

**Top risks.**
1. Legacy removal paths act on a reused path: retirement, re-arm, completed-card cleanup and seat-release
   ambiguity all key on `RemoteWorktreePath`.
2. State leaks between tasks: the orchestrator deny hook, `.antiphon` evidence, nested repos, a
   lingering process writing into the next lessee's tree.
3. Unpushed work destroyed by a reset.
4. Deleting an origin branch whose rebased-away tips the CARD-1008 recycle audit then flags as
   unpublished.
5. Windows file locks and long paths making resets fail. Re-homing keeps the pool whole, but
   quarantined directories then consume disk until an operator clears them.
6. Stale outputs in a warm native `bin/` (a project the next SHA deleted, an old mtime restored by an
   agent) loading into a later task's test run. The cold reset is the remedy.

**Next.** H-1..H-8 are decided (§7). TestDesign for S1 now; S2's design is settled and can be
test-designed in parallel, but it ships after S4 and S5.

## 1. Problem, with the measured numbers

| Measure | Value | Source |
|---|---|---|
| server2 mirrors, 2026-09-29 | 502 task worktrees, 327G (largest 23G, 16G, 9.8G), root disk 100% with leaked checkpoint temp roots | CARD-0824 text |
| server2 mirrors, 2026-10-31 18:10Z | 250 dirs, 87.5 GiB; p50 0.22, p95 0.91, max 1.87 GiB; obj 15.7, bin/bin-* 14.9, `.antiphon` 9.9, node_modules 2.0 GiB; checkouts ~45 GiB | docs/investigations/2026-10-31-incremental-builds-and-worktree-reuse.md (origin `feat/card-task-cf8b6982`) |
| server2 mirrors, **2026-11-02 05:20Z** (this task, `du -s` per dir) | **358 dirs, 133.3 GiB; p50 360 MiB, p95 917 MiB, max 3.3 GiB**; age <1 d 71, 1–2 d 113, 2–7 d 174, ≥7 d 0; 363 registrations in `/work/repos/antiphon`; `/work` 397G of 705G | measured here |
| server2 2026-10-02/03 | 98% disk, 116 stale terminal worktrees = 100.7 GiB | docs/docker-stack.md "Manual main cleanup" |
| desktop registrations | 1,025 (2026-10-31), 912 screened safe (168 GiB logical); 237 → 99 after the 2026-11-01 accept-loss cleanup (225 → 87 under `C:\Antiphon\worktrees`); 85 created in the last 24 h | 2026-10-31-desktop-worktree-bloat-investigation.md (origin `feat/card-task-4dcb3ac6`), 2026-11-01-desktop-worktree-retirement.md (origin `feat/card-task-224cca6b`) |
| desktop Git cost | `worktree add` 40.4 s (30.0 s updating files); list 6–7.8 s at ~1,026 entries; attached-HEAD list 12.7 s and 1.57 GiB at 1,000 (detached 2.75 s, 18 MiB); after cleanup add 10.2 s, list 0.2 s | same |
| desktop incident | task 7b8e4061: `git worktree add ... timed out after 180s` | same |
| creation cost, server2 | `git worktree add --detach` 2.8 s with 260 registrations; 201 MB, 9,290 tracked files | cf8b6982 |
| builds | 173 checkpoint builds/24 h, all cold by construction, 11.98 build-h, ~56 CPU-h; warm reuse avoidable 9–17 CPU-h (16–30%) | cf8b6982 |
| incrementality | cold 1,579 CPU-s; no-op 4%; fresh output name 97%; test edit 59%; low-level API change 94%; copied obj at another path 99% | cf8b6982 |
| runner capacity, 2026-11-02 | server2 `capacity 10` (sessions), desktop `capacity 2` (delegatedTasks), server2-temp stale/draining; global default runner server2 | `GET /api/session-runners`, `GET /api/runner-defaults` |

**Why the desktop still grows (inference, high confidence, not counted).** Since 2026-09-26 the global
default runner has been server2, and the desktop runs at most 2 delegated tasks. Yet the desktop
created 85 checkouts in 24 h. Most of them must be the **desktop halves of server2 tasks**: the
dispatcher cuts a desktop worktree before every runner mirror (`AgentTaskDispatcher.cs:5852-5913`,
`DelegationWorktreeService.CreateForTaskAsync`). Measure this before S5 by joining desktop registrations
to `AgentTasks.RunnerId`.

## 2. Ground truth: what the card assumes vs what the code does

| Card assumption | Code today (base `ead93feb0`) | Consequence for the plan |
|---|---|---|
| "The runner keeps worktrees after the task settles" | True. The runner removes a mirror only on request (`RunnerWorkspaceService.RemoveAsync`, `:535`). The requests come from `TaskWorktreeRetirementService.RemoveRemoteMirrorAsync` (`:372`, after a released retirement), the dispatcher re-arm (`AgentTaskDispatcher.cs:1166`) and the inert CARD-1032 completed-card coordinator. No runner-side age sweep exists (grep of `src/Antiphon.SessionRunner`). | Release must become an automatic lifecycle step, not a retirement request |
| One worktree per task | Two per runner task: desktop `card-task-<id>` (canonical; baseline capture, settlement sync, resume) plus runner `task-<id>` mirror (`worktree add -B <branch> <path> <sha>`, `RunnerWorkspaceService.cs:308`) | S1 bounds server2 only; the desktop needs S5 |
| "Reset to the task's start ref" | The server resolves the base: repair > `-StartRef` > merge target > default, or the CardCurrent sibling via `AgentTaskWorktreeBaseResolver` (`WorktreeBaseResolver.Resolve`, `DelegationWorktreeService.cs:442`). The runner receives only an exact `Sha` plus `Branch` and verifies `FETCH_HEAD == Sha` (`:302-305`). | The slot's previous state is never an input to base selection. The reset target is always the exact SHA, so `-StartRef`, `-BaseTask`, Auto and CARD-1640 semantics are unchanged |
| "Keep ignored build output" | The checkpoint tool always builds into fresh `bin-<id>/artifacts/obj` and refuses existing output (`BuildStep.cs:42-50`, `SourceRunGuard.cs:113-118`) | Kept `obj/` helps only direct builds; checkpoint receipts stay cold (correct for custody) |
| Mirror identity can be reused | CARD-1032 bindings refuse "A pre-existing mirror cannot be enrolled" and `foreign_mirror_owner` (`RunnerWorkspaceService.cs:249-278`). Mirror names must match `task-<8hex>` of the task (`:21`, `:232-234`), as must `PublishAsync`'s leaf check (`:588-592`) | S1 needs a new slot binding kind (slot id plus generation), not a relaxed name check |
| Paths are per task | `TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync` (`:1216-1226`) treats two live tasks sharing a `RemoteWorktreePath` as ambiguous. The workspace reservation is keyed by path plus branch, and "completed retirement keeps a historical fence; new work gets new coordinates" (session-runtime-invariants, CARD-0459) | On release, move the slot path off the task (S1). Never let a stale task's coordinates address a slot |
| Land needs the task worktree | CARD-0688: the source is a ref; rebase, verify and push happen in one locked detached land worktree (`LandWorkspace.cs`, reset by `abort/--quit`, detach, `reset --hard`, `clean -fdx`, verify, `worktree lock`). The task worktree is only probed for cleanup residue (orchestration-loop.md CARD-1463) | Slots coexist with the land worktree; land cleanup becomes "slot-managed: nothing to remove" |
| Cleanup is blocked by ignored content | CARD-0665 shipped disposable classes (`WorktreeCleanup:DisposableIgnored`). CARD-1446 S4a `FileSystemEvidenceRetention` copies evidence-class ignored files to `.retained/<task>/<attempt>/` before removal | Slot reset reuses the evidence-retention idea before wiping `.antiphon/` |
| Delete the branch "once its commits are on master" | Lands **rebase**: the branch's original commits are never ancestors of master ("check landing by subject, not branch SHA"). Publication proof is the landing operation's confirmed publication plus fresh remote containment (`AgentTaskLandingProtocol.CleanupAsync` `:419-459`) | Deletion keys on the landing record and a lease on the landed source SHA, not on `merge-base --is-ancestor` |
| Recycle safety | The CARD-1008 recycle audit refuses any HEAD, reflog or ref tip not contained in an origin head (`RecycleUnpublishedWork`) and allows only `antiphon-mirror-owner.json` in a linked admin dir (docs/docker-stack.md) | A free slot must sit detached at a published SHA with its reflog expired and its task refs deleted. S2's deletions would otherwise turn every legacy mirror into a recycle refusal |
| Seven kept server2 worktrees | All seven are **absent** from `/work/worktrees` and unregistered in `/work/repos/antiphon` on 2026-11-02 (§5) | Their fate is a closure decision, not a migration step |
| Builds use the worktree's native output (operator, H-3) | Agents are told to build to `--property:OutputPath=bin-<name>/` "while the daemons hold their bin directories" (testing-and-build.md "Building while daemons run"; the git-common fragment). That reason exists only in the main checkout, where the runner and server run from `bin/`. No daemon runs from a task worktree | In a slot, direct builds use native `bin/`/`obj/` with no OutputPath. The alternate-output rule is scoped to the main checkout by the parallel CARD-1629 step 1 revision; the checkpoint tool's own fresh output is unchanged |
| A slot directory can always be reset or removed | 2026-11-01 desktop cleanup: `Filename too long` left `card-task-a7b2b64a-rc` unregistered but on disk. On Linux, files written by a container as another uid (or under a 0555 directory) cannot be unlinked by the runner's uid | Reset and removal failures are an expected class. The slot re-homes to a new directory; the old one is quarantined in place (§3.5a) |
| Slots exhaust only past capacity | Runner seats bound live sessions, but a slot stays leased after its session ends until release (sync debt, follow-up, warm pool, Blocked) | Exhaustion is possible below seat capacity. Overflow slots absorb it; seat and host admission still gate dispatch first (§3.2a) |

## 3. Design

### 3.1 Terms and names

The word "slot" already means two other things here: runner seats (`RunnerSlotService`,
`RunnerSlotEndpointTests`) and build slots (CARD-0589, `BuildSlot*`). This feature therefore uses
**workspace slot** in code and docs (`WorkspaceSlot*`; test classes `C0824WorkspaceSlot*`). No existing
class starts with `WorkspaceSlot` or `C0824` (grep, 2026-11-02).

### 3.2 Pool, sizing and placement

- A runner owns its pool per repository. S1 covers the primary repository only (`/work/repos/antiphon`).
  Secondary repositories keep per-task mirrors (D-3).
- Slot directories sit under the existing worktree root, so session cwd admission and
  `WriteSpillAsync` (`IsUnderRoot`) keep working: `<allowed>/worktrees/ws-NN`. `ws-` never matches
  `task-<8hex>`, so the name pattern alone separates slots from legacy mirrors (D-4).
- Target size = `capacity + Headroom`. Headroom defaults to 4 (D-5): settled tasks awaiting release.
  Slots are created lazily up to the target; past it only as overflow slots (§3.2a).
  - When capacity grows, the next lease that finds no free slot creates one.
  - When capacity shrinks, or a slot above target has been idle past `IdleRetireHours`, the runner
    retires that Free slot itself (§3.2a "Lifetime").
- Per-slot platform pinning is unnecessary: a slot belongs to one runner, which has one platform. A
  `-Platform` choice places the task on a runner, and the slot follows. The checkpoint lanes below
  name the platform per row.
- Pool exhaustion: the runner first grows an overflow slot (§3.2a). Only when the overflow bound is
  reached does dispatch **hold** the task Queued with hold reason `workspace_slot_unavailable` and an
  attention row. It never falls back to a per-task `task-<id>` mirror (D-6, H-2).

### 3.2a Overflow slots (H-2)

The operator's answer: hold is normal because the pool should never run dry, but if it does, create a
new reusable slot rather than leave work stuck, "even if it's temporary".

- **Trigger.** A lease finds no Free slot. If some slot is `Releasing`, the lease waits up to
  `OverflowWaitSeconds` (default 10) for it. Otherwise, or when that wait expires, the runner creates
  an overflow slot inside the same lease RPC. Nothing else creates one: no prediction and no pre-warm.
- **Cap.** Two bounds, both checked under the ledger lock before creation:
  - count: at most `MaxOverflowSlots` above target (default 4 on server2, 2 on the desktop), so the pool
    never exceeds `capacity + Headroom + MaxOverflowSlots`;
  - disk: the volume keeps at least `max(20 GiB, 10%)` free (the `workspace_disk_low` threshold) plus
    `2 × SlotBytesEstimateGiB` (default 3) after creation, and the measured pool total (slots plus
    quarantined directories) stays under `PoolDiskBudgetGiB` (default 64 on server2).
  Past either bound the lease returns the typed refusal `workspace_slot_unavailable` with the bound
  that failed (`overflow_cap` or `disk_budget`). The server holds the task and raises attention.
- **Lifetime.** An overflow slot is a normal reusable slot: same states, ledger, lease, reset, release,
  quarantine and re-homing. The ledger records `createdAs: overflow`, the creating task and the time,
  for reporting only. The runner's own pool maintenance (every 15 min and at start) retires a Free slot
  when the pool is above target and that slot has been idle for `IdleRetireHours` (default 72). It
  retires the highest-numbered idle slot first, re-checks the free invariant, and uses the guarded
  `git worktree remove`. Never by hand, never `rm -rf`. A removal that fails is a re-homing failure
  class: the directory is quarantined (§3.5a) and the slot id is retired.
- **Naming and location.** The next unused number under the same root: `<allowed>/worktrees/ws-NN`. A
  number is never reused while its directory, or a quarantined generation of it, still exists.
- **Capacity and build slots.** An overflow slot admits no task by itself: seat capacity, host limits
  (CARD-0533) and stage caps decide dispatch before any lease. Creating a slot is git and filesystem
  work only (`worktree add --detach` ≈ 2.8 s on server2), so it takes **no build slot**. Its first build
  is cold and takes a build slot like any other build (CARD-0589). The build-slot gate still bounds
  concurrent builds whatever the number of workspace slots, so overflow adds disk, not build
  concurrency.
- **Report.** `GET /sessions/workspace-slots` lists overflow slots and their idle time. Attention
  `workspace_slot_overflow` is raised once per creation (information, not an alert sink).

### 3.3 Lease (runner side, journaled)

Slot states: `Free → Leasing → Leased(task, generation) → Releasing → Free`, plus `ColdResetting`
(Free only, §3.12), `Rehoming` (from any state whose directory failed, §3.5a; it ends Free on the new
directory) and `Failed` (re-home impossible, slot retired). Quarantine applies to a **directory**, not
to a slot (§3.5). A ledger beside the binding store records the states
(`<allowed>/workspace-slots/`, the same custody style as CARD-1032 `workspace-bindings`). Each
transition writes an intent before acting, as `RunnerWorkspaceBindingStore` does today.

Lease (the extended mirror RPC; capability feature `WorkspaceSlotV1`; old servers keep `task-<id>`):

1. Admission is as today: binding, runner store id, origin identity (CARD-0812), and fetch of the branch
   with `FETCH_HEAD == Sha`.
2. Pick a Free slot (or grow an overflow slot, §3.2a) and journal
   `Leasing(task, preparationId, generation+1)`.
3. Pre-check: the slot must still verify as Free (§3.4 "free invariant"). Otherwise quarantine its
   directory, re-home the slot (§3.5a) and pick another Free slot.
4. Refuse if the branch is already checked out in any other registered worktree. This is a plain file
   read of `worktrees/*/HEAD`, as `LandWorkspace.Scan` does.
5. `git checkout --force -B <branch> <sha>`, then the clean in §3.4.
6. Verify: `rev-parse HEAD == sha`; `symbolic-ref HEAD == refs/heads/<branch>`;
   `status --porcelain=v1 -z --untracked-files=all` empty; no sequencer markers; no index lock. Any
   failure quarantines the directory and re-homes the slot; the lease retries once on another slot.
7. Stamp the owner: reuse `antiphon-mirror-owner.json` in the slot's admin dir, with slot id,
   generation and task (D-7: keeps the CARD-1008 audit allowlist unchanged). Journal `Leased`.
8. Respond with `RemoteWorktreePath = <slot path>`, `SlotId`, `Generation`. The server stores both on
   the task.

**Exact tree (Review, `-StartRef`, `-BaseTask`).** Every lease produces exactly the requested SHA on the
task's own branch with a clean tracked and untracked tree. Only the native build output survives
(§3.4). A dispatch can ask for a **cold lease** that also wipes it (`clean -ffdx` with no exclusions;
`delegate.ps1 -ColdWorkspace`, §3.12). No role is cold by default (H-4).
SourceLanding Mutation snapshots and land verification never use slots: they keep their own detached
schema-2 custody (`RunnerVerificationWorkspace`, `LocalVerificationWorkspace`, D-8).

### 3.4 Reset contract: what is wiped and kept

The warm (default) reset keeps the slot's **native build output**: the default `bin/` and `obj/` of
each project, `node_modules/` and `client/dist/`. There is no custom OutputPath and no `bin-ws/`: the
slot keeps the same native output between tasks (H-3). A cold reset (§3.12) wipes the keep-list too.

| Kept by a warm reset (exact keep-list; nothing else survives) | Wiped by every reset |
|---|---|
| `**/obj/**`, `**/bin/**` (native default outputs), `**/node_modules/**`, `client/dist/**` | Tracked modifications (`reset --hard` via `checkout --force`) |
| (Outside the tree, untouched: `NUGET_PACKAGES` host mount, npm cache, shared VBCSCompiler) | Untracked and ignored files outside the keep-list: `git clean -ffdx -e '**/obj/' -e '**/bin/' -e '**/node_modules/' -e '/client/dist/'` (`-x` still honours `-e`; `-ff` removes nested repos) |
| | `bin-*/` custom alternate outputs (not native; left by a legacy instruction or a checkpoint run's `cleanup_refused`), `.playwright/`, TestResults, `*.trx` (D-14) |
| | `.antiphon/**` (inbox, reports, checkpoints, deliverables), after evidence retention (below) |
| | `.claude/settings.local.json` (the orchestrator deny hook is ignored through `info/exclude`, `DelegationWorktreeService.cs:630`; leaking it would block the next task's edits) |
| | Sequencer state (`rebase/cherry-pick/revert/merge --abort`, then `--quit`, as in `LandWorkspace`), `config.worktree`, per-worktree refs (`refs/worktree/*`, `refs/bisect/*`), the slot's HEAD reflog (after the unpushed check) |
| | The task's runner-local branch `refs/heads/<branch>` and tracking ref `refs/remotes/origin/<branch>`, after the unpushed check |

The **free invariant**, checked at release and again at lease:
- HEAD is detached at a SHA contained in an origin head (D-9: the fetched `origin/master` tip at release);
- `status --porcelain=v1 -z --untracked-files=all --ignored=matching` lists only keep-list paths;
- no sequencer markers, no index lock, no `config.worktree`;
- no live process has its cwd or an open handle under the slot (Linux `/proc/*/cwd` and `/proc/*/fd`
  scan, scoped to the runner's uid; Windows Restart Manager, S3).

**Evidence retention before the wipe.** `.antiphon/**/*.md` and `.antiphon/checkpoints/*/report.json`
are copied, byte-verified, to `<allowed>/retained/<task>/<generation>/` with a 256 MiB cap per task
and a 7-day retention (H-5, decided as proposed). Everything else is gone. This mirrors CARD-1446 S4a on the desktop. Checkpoint temp
roots outside the tree (`/tmp/c723-*`) stay with their CARD-0804/0805 owners (no duplicate deleter).

### 3.5 Release, crash detection and quarantine

**Release trigger (server reconciler, D-10).** A leased slot is released when all of these hold:
1. the task is terminal;
2. its runner session no longer occupies capacity (Exited or Failed; a warm pooled session keeps
   its cwd, CARD-0221);
3. settlement sync debt is not Pending or Held (CARD-1082);
4. the task is not the target of a live follow-up.

A Blocked task keeps its seat and its slot. Parking is off, so **no automatic release deadline exists**
(CARD-1083). The release RPC carries `(slotId, generation, taskId)`. The runner refuses a stale
generation as `slot_generation_mismatch` and treats an already-Free slot as idempotent success. On
success, the server clears `RemoteWorktreePath` and keeps `RemoteWorkspaceSlotId`/`Generation` as
history.

**Unpushed check (release and crash recovery).**
- Fetch `refs/heads/<branch>` from origin.
- Every commit in the slot's HEAD reflog since the lease's generation start must be reachable from
  some origin head. Reflog entries are taken after the generation's stamp time, so a `checkout -b
  other` made by the agent is covered.
- Also: tracked or untracked dirt outside the keep-list = **quarantine** (preserve; attention
  `workspace_slot_quarantined` with path, task, reason, sizes).
- Only if the check passes: retain evidence, then reset (warm, or cold when one is pending, §3.12) and
  park the slot detached at the published SHA, expire the reflog, delete the task refs, journal Free.

**Quarantine is per directory, not per slot.** A quarantine preserves the directory in place and takes
it out of service. The slot is then re-homed to a fresh directory (§3.5a), so the pool's size never
shrinks because of a quarantine. Two reason classes share the mechanism:
- **custody** (unpushed commits, dirt, a lingering process, an unknown crash state): the directory holds
  work that may matter;
- **filesystem** (the reset or removal cannot complete: an undeletable file, a lock that outlives the
  retries, a path too long, a broken admin dir): the directory is broken.

**Crash recovery (runner start and reconnect).** Any slot not in Free state and without a live lease
confirmed by the server is evaluated by the same check: it is either reset to Free or its directory is
quarantined and the slot re-homed. A dirty directory is never handed over (step 3 of the lease
re-verifies).

**Lingering processes.** If a process still has its cwd or a handle in the slot, the release is
deferred (retry with backoff 1, 2, 4, then 5 min). After N = 3 attempts (D-11) the directory is
quarantined with the pid and argv[0], and the slot re-homes. On Linux a lingering process could
otherwise write into the next lessee's tree; after re-homing it can only write into the quarantined
directory.

**Quarantine exit.** A quarantined directory leaves quarantine only through an explicit operator action.
The new endpoint `POST /sessions/workspace-slots/quarantine/{dirId}/release` takes
`{ disposition: Pushed|AcceptLoss|Removed, reason }`.
- `Pushed`/`AcceptLoss` (custody class): the runner re-runs the check and, under AcceptLoss, retains the
  directory's diff (`git diff`, untracked file list and patch) to the retained root. It then removes the
  directory through the guarded removal.
- `Removed` (filesystem class): the operator has fixed or removed the blocker by hand (for example a
  root-owned file). The runner re-tries the guarded removal and records the outcome.
- **Bound.** Quarantined directories count toward `PoolDiskBudgetGiB`. When more than
  `MaxQuarantinedDirectories` (default 4 = Headroom) are open on a runner, dispatch to that runner holds
  (`workspace_slot_pool_degraded`) until the operator clears some. This is the only quarantine-driven
  hold, and it is loud.

### 3.5a Slot re-homing (H-2: a slot's directory can change)

The operator: existing worktrees might become unusable ("some weird file that can't be deleted"), so a
slot's directory must be changeable after a catastrophic, unrecoverable failure.

- **Detection.** Any of these during lease, release, reset, cold reset or retirement:
  - `git checkout`/`clean`/`worktree remove` exits non-zero with a filesystem error (`EACCES`, `EPERM`,
    `EBUSY`, `ENAMETOOLONG`, `ENOTEMPTY` after clean; Windows `Filename too long`,
    `ERROR_SHARING_VIOLATION`, `ERROR_ACCESS_DENIED`), after the linger retries where a process is
    attributable;
  - the post-reset verify still lists paths outside the keep-list after one retry (an undeletable
    residue);
  - the directory's identity is broken: `.git` file missing or pointing elsewhere, admin dir missing,
    registration absent (the CARD-1448 `identity_mismatch` shape);
  - any custody-class quarantine (§3.5).
- **Quarantine the old directory in place.** Never delete it and never move it (a move can fail the same
  way). Journal `Rehoming(slot, fromDir, fromGen)` first. Then `git worktree lock --reason "antiphon
  quarantine <slot> <class> <reason>"` so no `prune` drops its registration; if the lock itself fails,
  record that. Record in the ledger `quarantinedDirectories[]`: exact absolute path, slot id, directory
  generation, class, reason, the raw error text (bounded 4 KiB), branch, HEAD SHA, a best-effort size
  (bounded `du`, 60 s), time. Raise attention `workspace_slot_rehomed` carrying the exact path, so the
  operator can act on it.
- **Create the replacement.** `git worktree add --detach <root>/ws-NN-d<K> <published sha>` (K = the
  directory generation, the first generation is the plain `ws-NN`; `-c core.longpaths=true` on Windows),
  verify the free invariant, journal `Free(dirGen=K)`. If creation fails, try K+1 once. If that fails
  too, mark the slot `Failed`, raise `workspace_slot_rehome_failed`, and continue with the other slots
  and overflow. A failed re-home never blocks the pool.
- **Identity.** The slot id (`ws-NN`) stays. Its directory generation K increments; its lease generation
  continues monotonic. The server stores the slot path per task and compares `(slotId, lease
  generation)`, so no stale task's coordinates can address the new directory.
- **Loop guard.** A slot that re-homes more than `MaxRehomesPerSlotPerDay` (default 3) times in 24 h is
  retired (its id is no longer leased) with attention; the pool replaces it through normal growth.
- **Interplay.** `ws-*` (including `ws-NN-dK`) stays `slot_managed` for every legacy removal path
  (§3.6). A custody-quarantined directory with unpublished work still refuses a CARD-1008 volume recycle;
  a filesystem-quarantined directory whose content is published passes it, which makes a volume recycle
  the operator's last-resort recovery for files the runner's uid cannot delete.

### 3.6 Legacy removal paths against slot paths (the main custody hazard)

The following consumers of `RemoteWorktreePath` must not act on a slot they no longer lease:
- `TaskWorktreeRetirementService.RemoveRemoteMirrorAsync`;
- the dispatcher re-arm (`AgentTaskDispatcher.cs:1166`);
- the CARD-1032 completed-card coordinator (`CardWorktreeMirrorCleanupService`, `CompletedCardWorkspace*`);
- `RunnerWorkspaceService.RemoveAsync`;
- `TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync`;
- `PhoneHomeRunnerMirrorPublisher`;
- `TaskParkPublicationService`;
- `AgentTaskReplyService` sync messages.

Rules:
- The runner `RemoveAsync` refuses any `ws-*` path or ledger slot with typed `slot_managed`.
- The server treats `slot_managed` as "nothing to remove" and clears the task's path.
- `PublishAsync` resolves slots by ledger (task plus generation) instead of the leaf-name check.
- Ownership ambiguity compares `(slotId, generation)`, not the path.

Each rule gets a red-first witness (§6). The full consumer list (about 20 files) is in §6 S1.

### 3.7 Interaction with landing custody, the child journal and CARD-0688

- **Land worktree: coexist, do not merge (D-12).** The land worktree is held under `land-execution.lock`
  plus the repository mutation lease. It is wiped with `clean -fdx` every land and must never carry a
  task branch. A pool slot would couple land exclusivity to task leases. The reset logic shares a pure
  policy (sequencer markers, porcelain classification) placed in `Antiphon.SessionRunner.Contracts`,
  next to `DelegateBranchName`, so both hosts use one decision table.
- **Child journal.** Runner slots live in the runner repository (`/work/repos/antiphon`), which runs no
  land and has no `antiphon/children` journal. Desktop slots (S3/S5) are repository mutations of the
  canonical repo: a reset moves a branch ref and deletes refs. So a desktop reset takes the repository
  mutation lease, as `WorktreeProvision` does today, and honours the CARD-1379 verification-window
  fence.
- **Master only in the main checkout (CARD-0688 invariant).** Slots are never on `master`: they are on a
  task branch while leased and detached while free. Detached free slots also avoid the attached-HEAD
  registry cost measured on the desktop.
- **Land cleanup.** For a slot-bound task, `AgentTaskLandingProtocol.CleanupAsync` records `slot-managed`
  instead of guarded removal. The slot release (§3.5) is the only path that touches it.
- **Settlement sync and resume** read the desktop checkout, which stays per task until S5. S1 changes
  only the runner half.

### 3.8 Build slots (CARD-0589) and native build output (CARD-1629)

- A reset (warm or cold), an overflow creation and a re-home run only git and filesystem work, never a
  build, so they take **no build slot**. Build-slot leasing per build (CARD-1627) is unchanged.
- **One stable build output per slot = the native output (H-3).** Direct builds in a slot use each
  project's default `bin/` and `obj/`: no `--property:OutputPath`, no `-o`, no `bin-ws/`. The slot keeps
  them between tasks. This also avoids the CARD-0222 `FileListAbsolute.txt` growth, which comes from
  many OutputPath variants sharing one `obj/`.
- **CARD-1629 step 1** ("one stable `bin-<task>/` per worktree") is being revised in parallel to the same
  native-output rule. This plan depends on that revision for the brief, skill and testing-and-build.md
  wording (the "Building while daemons run" alternate output becomes main-checkout-only) and does not
  duplicate it. S1 docs only cross-reference it.
- The checkpoint tool keeps its own fresh `bin-<id>/artifacts` custody output (it refuses existing
  output, `SourceRunGuard.cs:113-118`); checkpoint receipts stay cold. Leftover `bin-*` from a refused
  cleanup is wiped by the next reset (§3.4).
- CARD-1629 step 2 (same-commit checkpoint build reuse) stays **within one task**: `.antiphon/checkpoints`
  is wiped at reset, so no receipt can be reused across tasks by construction.
- Known hazard (testing-and-build.md CARD-0412 D4/D6): restoring an old mtime lets an incremental build
  reuse a stale DLL. Git checkout writes changed files with the current mtime, so slot switches are
  safe. Agents restoring files with `cp -p`/`Copy-Item` remain the existing hazard.
- Stale outputs of a project that the next SHA deletes stay in `bin/`. A test that globs assemblies
  from `bin/` could load them. Witness this in TestDesign; the cold reset (§3.12) is the remedy, and the
  weekly schedule bounds how long such residue can live.

### 3.9 Windows desktop (S3)

- **Paths.** `C:\Antiphon\worktrees\ws-NN` is shorter than `card-task-xxxxxxxx`. Every slot git call
  passes `-c core.longpaths=true`: the 2026-11-01 `Filename too long` failure left `card-task-a7b2b64a-rc`
  unregistered but on disk. The cleanup uses the `\\?\` prefix and never a recursive shell delete. A
  path that still fails is the filesystem class: the directory is quarantined and the slot re-homes
  (§3.5a).
- **File locks.** A testhost, node or PtyHost child holding `bin/`/`obj/` makes `checkout`/`clean` fail
  part-way. The reset pre-checks handles under the slot with the Restart Manager (`RmGetList`). A lock
  defers the release (§3.5); if it persists, the directory is quarantined with pid and name and the slot
  re-homes. A half-reset tree is quarantined, never handed out. A cold reset is where native `bin/`
  locks bite hardest, which is one more reason it never runs while a slot is leased. MCP children holding the cwd were the CARD-0308 failure; `Kill()` now terminates
  the job, but linger exists.
- **Defender.** Real-time scanning inflates full checkouts (30 s of the 40 s add). A reset rewrites
  only changed files, which is the desktop's real speed win. Exclusions are human-only and not changed
  here.
- **Evidence.** Reuse `FileSystemEvidenceRetention` (CARD-1446) before the wipe.
- **Trust dialogs.** A stable slot path means each provider's trust prompt fires once per slot ever
  (CARD-0315 Grok, Claude), not once per task.

### 3.10 Remote branch deletion after confirmed publication (S2)

- **Trigger:** the landing operation reached confirmed publication. That is the same authority
  `CleanupAsync` uses: committed publication, fresh remote containment of the published SHA, current
  source identity.
- **Act:**
  `git push origin --force-with-lease=refs/heads/<b>:<landed source SHA> :refs/heads/<b>`.
  A branch that moved after the land (new commits) is refused and kept (`branch_moved`).
- **Never** for a refused, held, failed or unconfirmed land, a `LandedWithResidue` whose publication is
  unconfirmed, a repair-source owner whose recovery is open (CARD-0753), or a branch that another open
  task names as its `WorktreeBaseBranch`.
- **Reverted lands.** The landed content stays in master's history, and `refs/antiphon/land/**` pins
  (CARD-0669) keep the original source commits locally. A grace delay (default 24 h, H-1) leaves time
  for an operator revert before the name disappears.
- **Local refs.** The desktop deletes `refs/remotes/origin/<b>` with `update-ref -d` after a successful
  remote delete. It keeps `refs/heads/<b>` until its per-task worktree is retired (git refuses to delete
  a checked-out branch). Runner-local refs are deleted at slot release (§3.4).
- **Recording.** Record the deletion on the landing row (`BranchDeletionState/At/Detail`) and on the
  card's attention feed, never in a new column.
- **Recycle interplay.** After S2, a **legacy** mirror whose branch was deleted makes the CARD-1008
  recycle audit refuse `RecycleUnpublishedWork`. S4 retires legacy mirrors before S2 is enabled on a
  runner volume that will be recycled (rollout order).

### 3.11 Disk watchdog

The runner exposes `GET /sessions/workspace-slots` (no `/api`, runner convention). It returns per slot:
state, task, lease generation, directory generation and path, `createdAs` (base or overflow), idle
time, bytes measured at release (bounded `du`, 60 s budget), last reset ms and kind (warm or cold),
pending cold reset, and the last cold reset time. It lists quarantined directories (§3.5a) with their
exact paths. It also reports legacy `task-*` count and bytes and the volume's free bytes.

The server attention feed raises:
- `workspace_disk_low` when free space is below `max(20 GiB, 10%)`, matching the `CacheDiskLow` gate;
- `workspace_slot_quarantined` for each quarantined directory (custody class);
- `workspace_slot_rehomed` and `workspace_slot_rehome_failed` (§3.5a), with the exact path;
- `workspace_slot_overflow` for each overflow creation (§3.2a);
- `workspace_legacy_backlog` when legacy mirrors exceed 50.

It reports only; it never deletes.

### 3.12 Reset policy: warm by default, cold only explicitly, on a schedule, or before an rc run (H-4)

The operator: "don't reset, reset should only be explicit or maybe once a day/week and e.g. before
start of the whole rc run".

- **Warm reset (default, every lease and release).** §3.4: everything except native build output is
  wiped. No role, setting or schedule makes the default cold.
- **Cold reset** = the warm reset plus the keep-list (`git clean -ffdx` with no exclusions). It is
  requested in exactly three ways:
  1. **Per dispatch.** `delegate.ps1 -ColdWorkspace` sets `AgentTask.WorkspaceColdReset` (additive EF
     column); the lease request carries `Cold=true`; that lease does a cold reset before binding.
  2. **Operator action.** The server route `POST /api/session-runners/{runnerId}/workspace-slots/cold-reset`
     with `{ slots: ["ws-03", ...] | "all", reason }` (audited; delivered over the existing runner
     command channel: a phone-home command for server2, direct HTTP to
     `POST /sessions/workspace-slots/cold-reset` for the desktop runner). The script front door is
     `scripts/workspace-slots.ps1 cold-reset -Runner <name>|-AllRunners [-Slot ws-03] -Reason <r>`
     (ASCII-only), which prints the request id and per-slot outcome.
  3. **Schedule.** `PhoneHome:WorkspaceSlots:ColdResetSchedule` = `Off|Daily|Weekly`, default **Weekly**,
     with `ColdResetDayOfWeek=Sunday` and `ColdResetHourUtc=2`. The runner's pool maintenance marks every
     slot `ColdResetPending(schedule)` at the boundary; a runner that was down runs the missed one at
     its next start. Weekly is proposed because the measured warm gain is modest (6–41% per build) and a
     cold rebuild of ~14 slots costs roughly 6 CPU-h, so a daily cold reset would give back a large part
     of the 9–17 CPU-h/day saving, while weekly still bounds stale-output residue to seven days.
     `Daily` is one setting change.
- **Before an rc qualification run.** Before the orchestrator (or whoever starts the run) starts a
  whole rc qualification run, it calls `scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc
  qualification <rc branch> <sha12>"` and records the returned request id with the run identity. The
  Windows Full run itself uses its own candidate directory, not a slot; the cold reset covers the slots
  its Linux repair tasks will use. The start does not wait for leased slots: they cold-reset at their
  release. S1 adds this step to `.claude/skills/antiphon-rc-qualification/SKILL.md` step 1 and to
  docs/release-gates.md.
- **Never while leased.** A cold-reset request on a leased slot only records `ColdResetPending(requestId)`
  in the ledger; it runs inside that slot's release reset, before the slot becomes Free. A Free slot is
  cold-reset at once under the ledger lock (`Free → ColdResetting → Free`); a lease arriving meanwhile
  takes another Free slot or waits up to `OverflowWaitSeconds`. Requests are idempotent; the request id,
  requester and reason are journaled.
- **Safety unchanged.** A cold reset runs the same unpushed and free-invariant checks. A directory that
  fails any check is quarantined, never wiped blindly; a cold reset that cannot delete something is the
  filesystem class and re-homes the slot (§3.5a).

## 4. Overlap reconciliation

| Card | Status (2026-11-02) | Verdict | Reason and what remains |
|---|---|---|---|
| CARD-0692 post-land Cleanup stage | Review | **Narrow** | Its worktree-retirement part is unnecessary for slot tasks (release is automatic). Keep: (a) origin-branch deletion for the card's **non-owner** branches (repairs, reviews, rebases), implemented by calling the S2 primitive with the same lease and record rules; (b) legacy desktop and runner worktrees during migration; (c) stranded-session release (CARD-0691 part); (d) temp-root retirement, delegated to CARD-0804/0805 owners. If its Review is clean, land it as the legacy path, with no new worktree-retirement work |
| CARD-0459 land-time and scheduled sweep | Done | **Keep (no new work)** | Moot for slots. Its land-time cleanup and `antiphon:worktree-residue` sweep remain the legacy path during migration. S4 decides `WorktreeResidue:Execute` for legacy trees |
| CARD-0665 disposable ignored content | Done | **Keep** | Its classes and CARD-1446 evidence retention are reused by the slot reset (§3.4) and still govern legacy cleanup |
| CARD-0670 release policy for never-landed non-Code worktrees | Backlog | **Cancel after the slices ship** (H-6, decided; narrow to "legacy only" until then) | Slot release covers every role. S4's automatic legacy release policy replaces it for the remaining legacy population |
| CARD-0673 Codex cannot delete `bin-*` | Backlog | **Cancel after S1 (server2) and S3 (desktop)** | Slot builds use native output, so no `bin-*` is created; any leftover `bin-*` is wiped by the reset. Legacy residue is S4's |
| CARD-0669 land residue refs | Review | **Keep separate** | `source-recheck/*`, `refs/antiphon/land/**` pins and land-verify temp are land residue, not workspaces. S2 adds only branch and tracking-ref deletion |
| CARD-1017 completed-card worktree deletion (InProgress) and CARD-1032 runner execution (Done, inert) | — | **Narrow** | Restrict to legacy per-task coordinates. Its runner coordinator must treat `slot_managed` as nothing to remove (§3.6) |
| CARD-1629 incremental builds | InProgress | **Adjust step 1** (being revised in parallel) | Native `bin/`/`obj/` per slot, no custom OutputPath (H-3); the alternate-output rule becomes main-checkout-only. Step 2 stays within a task |
| CARD-1627 slot loops / CARD-1623 qualification contention | Backlog | Independent | Workspace slots change neither lease hold time nor land-verify placement |
| CARD-0508 / CARD-1640 Auto base | Done / Backlog | Independent | Base resolution is server-side on refs. The slot always resets to the resolved SHA (§2) |
| CARD-0831 temp-runner retirement vs unpublished mirrors | Backlog | **Coordinate** | Custody-quarantined directories are unpublished work. A temp-runner drain must read the slot ledger, and such a directory blocks `down -v` exactly as a dirty mirror does |

## 5. Migration and the seven kept server2 worktrees

**The seven (checked 2026-11-02 05:30Z).** None of the seven exists in `/work/worktrees` or is
registered in `/work/repos/antiphon`. The oldest surviving mirror dates from 2026-10-29. The task
records:

| Task | Status / role | Completed | Origin branch tip |
|---|---|---|---|
| 19e7f181 | Succeeded, Custom ("Raise server2 caps, desktop to 2") | 2026-09-26 | `75235a25d679` |
| 28fc3bab | Failed, Review (CARD-0665 rebase + Final verify) | 2026-09-25 | **none** |
| 7647cb6a | Succeeded, Plan (FakeGit plan) | 2026-09-28 | `04de04356738` |
| 7ebf8aa2 | Canceled, Review (CARD-0589 repair 2 Final) | 2026-09-25 | `4ba1d1110470` |
| 89719e41 | Failed, Code (CARD-0718 S2) | 2026-09-26 | `248c2775cc21` |
| c03af147 | Failed, Code (CARD-0649 D1-D3,D5) | 2026-09-28 | `b7bd0b748351` |
| e7c20aaa | Failed, Code (CARD-0452 rebase + C448_C24) | 2026-09-27 | `904bd75525c8` |

How they were removed was not traced. A volume recycle would have passed the CARD-1008 audit, which
refuses dirty or unpublished work. A manual cleanup would not. Any uncommitted bytes they held cannot be
recovered from here. **Decided (H-7, 2026-11-02):** read the rollout receipts for the recycle that
removed them once; if an archive or donor volume still holds them, keep it until the operator says
otherwise; otherwise close the item, recording "absent on 2026-11-02; 6/7 origin branches retained".
28fc3bab was a Review, which commits no source. No migration step depends on them. This check is a
one-off docs task inside S4, not code.

**Migration (S4), per runner:**
1. **Enable.** Turn slots on for new tasks on that runner with `PhoneHome:WorkspaceSlots:Enabled`
   (per-runner flag). Legacy mirrors are untouched.
2. **Census.** Run the S3.11 inventory: each legacy `task-*` with its task status, sync debt, live
   session and publication proof.
3. **Retire legacy mirrors.** Use the runner's own guarded `RemoveAsync` with `PublishedSha` proof. A
   bulk mode added in S4 takes no repository lock beyond each removal (operator housekeeping rule), works
   in batches of 25 with a pause, and is gated by the age cutoff (H-6, decided: terminal ≥ 48 h).
   - Dirty, unpushed or unknown mirrors are kept and reported, never deleted.
   - Never by hand, never `rm -rf`.
4. **Desktop.** The same for `card-task-*` through `TaskWorktreeRetirementService` with an automatic
   release policy for legacy terminal tasks (H-6, decided: this replaces CARD-0670;
   `WorktreeResidue:Execute` is not flipped).
5. **S2 last on a volume.** Enable branch deletion only after step 3 drains legacy mirrors whose
   branches it would delete (§3.10).

**Rollback.** Turn the flag off. New tasks get `task-<id>` mirrors again. Leased slots finish their
tasks and are released normally. Free slots stay idle; a later S4 command can remove them. No data
migration is needed, because the server's slot fields are additive history.

## 6. Slices

Each slice is independently shippable behind its own flag, default off. Code runs `### Checkpoints` as
a closed list. TestDesign refines the method names. The classes below are the intended rosters, and
their prefixes are unique (grep 2026-11-02).

**Order (H-8, decided): S1, S4, S5, S2, S3.** The sections below keep their slice numbers; the order is
the build and ship order. S2's TestDesign can run beside S1's because it touches a different area
(landing), but S2 is enabled on a volume only after S4 has drained its legacy mirrors.

### S1: runner workspace slot pool, lease and reset (Linux, server2)

**Files.**
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs` (lease path, `RemoveAsync` `slot_managed`,
  `PublishAsync` by ledger)
- new `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs` (lease, overflow growth, idle
  retirement, cold-reset scheduling), `RunnerWorkspaceSlotLedger.cs` (states, directory generations,
  quarantined directories, pending cold resets), `RunnerWorkspaceSlotRehomer.cs` (§3.5a), and a
  filesystem seam `IWorkspaceSlotFileSystem` so portable tests can inject `EACCES`/`ENAMETOOLONG`/sharing
  failures
- `src/Antiphon.SessionRunner/RunnerWorkspaceBindingStore.cs` (slot binding kind)
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (release, inventory, cold-reset request,
  quarantine release)
- `src/Antiphon.SessionRunner.Contracts/*` (mirror request `Cold`, mirror response `SlotId/Generation`,
  typed refusal `workspace_slot_unavailable{overflow_cap|disk_budget}`, release and cold-reset requests,
  feature `WorkspaceSlotV1`, pure `WorkspaceSlotResetPolicy` with warm/cold keep-lists)
- server: `RemoteWorkspaceService.cs`, `RemoteWorkspacePreparer.cs`, `AgentTaskDispatcher.cs` (re-arm
  `:1166`, hold reason), `TaskWorktreeRetirementService.cs`, `TerminalRunnerSeatReleaseService.cs`
  (`AmbiguousOwnershipAsync`), `PhoneHomeRunnerMirrorPublisher.cs`, `TaskParkPublicationService.cs`,
  `CardWorktreeMirrorCleanupService.cs`, `AgentTaskLandingProtocol.cs` (slot-managed cleanup)
- new `WorkspaceSlotReleaseService.cs` (reconciler) and the server route
  `POST /api/session-runners/{runnerId}/workspace-slots/cold-reset`
- `Domain/Entities/AgentTask.cs` plus an EF migration (`RemoteWorkspaceSlotId`,
  `RemoteWorkspaceSlotGeneration`, `WorkspaceColdReset`)
- `scripts/delegate.ps1` (`-ColdWorkspace`), new `scripts/workspace-slots.ps1` (list, cold-reset,
  quarantine release; ASCII-only)
- settings `PhoneHome:WorkspaceSlots:{Enabled=false, Headroom=4, LingerRetries=3,
  OverflowWaitSeconds=10, MaxOverflowSlots=4, SlotBytesEstimateGiB=3, PoolDiskBudgetGiB=64,
  IdleRetireHours=72, MaxQuarantinedDirectories=4, MaxRehomesPerSlotPerDay=3,
  ColdResetSchedule=Weekly, ColdResetDayOfWeek=Sunday, ColdResetHourUtc=2}`
- docs: `session-runtime-invariants.md`, `docker-stack.md` (slot layout, re-homing, quarantined
  directories), `ops-http.md` (runner and server routes),
  `.claude/skills/antiphon-rc-qualification/SKILL.md` and `release-gates.md` (pre-run cold reset);
  the native-output wording is CARD-1629's

**Acceptance.**
- With the flag on, ten sequential and four concurrent tasks use at most 14 directories.
- A free slot meets the free invariant.
- Every §3.4 wiped class planted by task A is absent for task B, and every kept native-output class is
  present (byte-identical) after a warm reset.
- An unpushed commit, dirty file or lingering process quarantines the directory and never wipes it; the
  slot re-homes and stays leasable.
- An unremovable directory re-homes the slot to `ws-NN-d2` under the same slot id, and the old path is
  kept, locked and reported.
- With all slots leased, a lease grows an overflow slot up to the cap and disk budget, then refuses
  `workspace_slot_unavailable`; an idle overflow slot is retired by the runner after `IdleRetireHours`.
- Native output is wiped only by a cold reset (dispatch flag, operator request, schedule); a cold reset
  requested for a leased slot runs at its release, never during the lease.
- A stale task's removal, publish or seat-release ambiguity cannot touch a slot leased to another task.
- Flag off is byte-for-byte today's mirror path.

**Witnesses** (red-first; the mutants that must turn them red are in brackets):
- V-1 lease resets to the exact SHA and branch from an unrelated previous state [skip the
  `checkout --force`; skip the clean]
- V-2 keep-list: native obj, bin, node_modules and client/dist survive; deny hook, `.antiphon`,
  `bin-*`, nested repo, `config.worktree`, sequencer, stash-free tree are wiped [drop `-ff`; widen the
  `-e` list to `**`; skip the sequencer abort]
- V-3 unpushed commit on the task branch, and on a side branch reached only through the reflog,
  quarantines the directory [check only the branch tip; skip the reflog walk]
- V-4 tracked or untracked dirt quarantines the directory and the bytes are preserved; the slot
  re-homes and the next lease gets a clean `ws-NN-d2` [treat dirt as resettable]
- V-5 lingering process (cwd or open fd under the slot) defers, then quarantines the directory; the
  next lessee never gets that directory [drop the process scan] (linux-native)
- V-6 crash at each journal phase (Leasing, Leased, Releasing) recovers to Free or Quarantined, never
  hands out a dirty tree [skip the lease-time re-verify]
- V-7 two concurrent leases never share a slot; the same branch is refused in a second slot [remove
  the ledger lock; skip the HEAD scan]
- V-8 `RemoveAsync` on a `ws-*` path refuses `slot_managed`; the lessee's tree is intact [delete the
  refusal]
- V-9 stale-generation release refuses `slot_generation_mismatch` [ignore the generation]
- V-10 server: a released task's coordinates do not make a live lessee "ambiguous", and retirement and
  re-arm treat `slot_managed` as removed [compare paths, not slot generation]
- V-11 the free slot sits detached at a published SHA with an empty reflog and the task's local and
  tracking refs gone [leave HEAD on the task branch]
- V-12 flag off: the mirror path and name are unchanged [default flag on]
- V-13 server: a runner refusal `workspace_slot_unavailable` (overflow cap or disk budget) holds the
  task Queued with that hold reason and attention, and no fallback mirror is created [fall back to
  `task-<id>`]
- V-27 native output, no reset by default: a warm reset leaves every native `bin/`/`obj/` file
  byte-identical with its mtime unchanged, and the lease passes no OutputPath [make the default reset
  cold; drop `bin`/`obj` from the keep-list]
- V-28 cold reset explicit: a lease with `Cold=true` and an operator cold-reset of a Free slot wipe
  native output; a lease without it does not [ignore the flag]
- V-29 cold reset never while leased: an operator or scheduled request on a leased slot records
  `ColdResetPending`, leaves the lessee's tree untouched, and runs at release before the slot is Free
  [run immediately on every slot]
- V-30 schedule: with a fake clock crossing the weekly boundary, every slot is marked pending once;
  `Off` marks none; a runner started after a missed boundary runs it once [run on every maintenance
  tick; skip the missed-run catch-up]
- V-31 overflow growth: with every base slot leased and none Releasing, a lease creates `ws-<next>`
  as a normal slot; with one Releasing, it waits up to `OverflowWaitSeconds` first [hold instead of
  growing; skip the wait]
- V-32 overflow bound: at `MaxOverflowSlots` or below the disk budget the lease refuses
  `workspace_slot_unavailable{overflow_cap|disk_budget}` and creates nothing [unbounded growth; skip
  the disk check]
- V-33 idle retirement: a Free slot above target idle past `IdleRetireHours` is removed through the
  guarded removal; a leased or recently used slot is never retired, and a quarantined directory is
  never removed by retirement [retire on count alone]
- V-34 re-homing (seam): an injected `EACCES`/`ENAMETOOLONG` during reset quarantines the directory in
  place (not deleted, `worktree lock`ed, ledger and attention carry the exact path), creates
  `ws-NN-d2` under the same slot id with the lease generation continuing, and the slot leases again
  [delete the old directory; mint a new slot id; reuse the old path]
- V-35 re-home failure never blocks the pool: a second injected failure on creation marks that slot
  `Failed` with `workspace_slot_rehome_failed`, and other slots and overflow keep leasing; more than
  `MaxRehomesPerSlotPerDay` re-homes retire the slot [block all leases; loop re-homing]
- V-36 real unremovable directory (linux-native): a file under a `0555` subdirectory that the runner
  uid cannot unlink makes the real `git clean` fail; the slot re-homes and the old directory survives
  with the file [swallow the clean error]. The test restores `0755` before its own cleanup and fails
  loudly (never skips) if it runs as uid 0, where `0555` does not stop an unlink.
- V-37 server: `-ColdWorkspace` persists `WorkspaceColdReset` and the lease request carries
  `Cold=true`; the operator cold-reset route records the requester and reason and forwards the slot
  list unchanged [drop the flag on the lease request]

### S2: origin branch deletion after confirmed publication

**Files.** `server/Application/Services/AgentTaskLandingProtocol.cs` (post-publication step), new
`PublishedBranchDeletionService.cs`, `server/Infrastructure/Git/LandingGit.cs` (delete push with
lease), `AgentTaskLanding` fields plus migration, settings
`Landing:DeleteSourceBranchAfterPublication=false`, `Landing:SourceBranchDeleteDelayMinutes=1440`,
`docs/orchestration-loop.md`.

**Decided (H-1, 2026-11-02): yes.** The flag ships default off and is turned on per volume after S4
has drained that volume's legacy mirrors (§3.10 "Recycle interplay"); the 24 h delay and the lease on
the landed SHA stand.

**Acceptance.** Only a confirmed-published land's source branch is deleted, after the delay, with a
lease on the landed SHA. Refused, failed and unconfirmed lands, moved branches and open repair or base
dependents keep the branch. Tracking refs are removed after the remote delete. The outcome is recorded.

**Witnesses:**
- V-14 confirmed publication deletes with the lease [delete without the lease]
- V-15 refused or unconfirmed land keeps the branch [trigger on any terminal land]
- V-16 branch moved after the land is refused `branch_moved` and kept [drop the expected SHA]
- V-17 a dependent task's `WorktreeBaseBranch` or an open CARD-0753 recovery keeps the branch [skip
  the dependency query]
- V-18 delay respected [zero delay]
- V-19 flag off is a no-op

### S3: Windows desktop slots for desktop-run tasks

**Files.**
- `server/Infrastructure/Git/WorktreeManager.cs` (slot create and reset with `core.longpaths`)
- new `server/Infrastructure/Git/DesktopWorkspaceSlotPool.cs`
- `server/Application/Services/DelegationWorktreeService.cs` (lease instead of `CreateAsync` for
  desktop-run tasks)
- Restart Manager seam `server/Infrastructure/Git/WindowsFileLockProbe.cs`
- `FileSystemEvidenceRetention` reuse
- settings `Git:WorkspaceSlots:{Enabled=false, Headroom=2, MaxOverflowSlots=2, IdleRetireHours=72,
  ColdResetSchedule=Weekly}` (the S1 keys, desktop values)

**Acceptance.** The S1 contract on Windows, including overflow, re-homing and the warm/cold reset
policy. A held file under native `bin/` defers, then quarantines the directory and re-homes the slot. A
path longer than 260 characters resets and removes; a forced `Filename too long` re-homes. Evidence is
retained before the wipe. The reset holds the repository mutation lease and respects the
verification-window fence.

**Witnesses:**
- V-20 locked file defers, then quarantines the directory and re-homes the slot [skip the RmGetList
  probe] (windows-native)
- V-21 long path reset and removal succeed; a path that still fails re-homes to `ws-NN-d2` and keeps
  the old directory [drop `core.longpaths`; delete the old directory] (windows-native)
- V-22 evidence retained before the wipe [wipe first]
- V-23 the reset refuses without the repository lease [skip `Owns(lease)`]

### S4: migration, legacy cleanup and disk watchdog

**Files.** `RunnerWorkspaceService.cs` (bulk guarded legacy removal, batches of 25),
`server/Application/Services/WorktreeResidueSweepService.cs` / `TaskWorktreeRetirementService.cs`
(legacy automatic release policy), runner inventory route, `AttentionService.cs` (three attention
keys), `docs/docker-stack.md` and `docs/apphost-runbook.md` runbook.

**Decided (H-6, H-7, 2026-11-02).** Cutoff terminal ≥ 48 h; an automatic legacy release policy (not
`WorktreeResidue:Execute`); CARD-0670 is cancelled after the slices ship. The seven 2026-09 worktrees get
one receipt/archive check, then the item is closed (§5).

**Acceptance.** The legacy census lists every `task-*` and `card-task-*` with a verdict. Only terminal
(≥ 48 h), clean, published, unowned trees are removed, in batches through the guarded path. The
watchdog raises its keys and never deletes.

**Witnesses:**
- V-24 dirty, unpushed or unknown legacy mirrors are kept [remove on age alone]
- V-25 batch bound and pause [unbounded]
- V-26 watchdog never deletes, and raises `workspace_disk_low` below the threshold [threshold
  inverted]

### S5: desktop halves of runner tasks become desktop slots

This slice is planned at design level only. It needs a read of the desktop-half consumers that this
plan did **not** read in full: `CaptureProgressBaselineAsync`, `SettlementSyncRecoveryService`,
`BlockedTaskSyncRecoveryService`, `TaskParkPublicationService` resume, `AgentTaskReplyService` sync
paths, and the land residue probe.

Same contract as S3. Pool size = sum of runner capacities + Headroom. Release follows the runner slot's
release plus sync debt Confirmed.

Before code, take one measurement: desktop registrations joined to `AgentTasks.RunnerId`, to prove the
§1 inference.

## 7. Decisions

### Stated defaults (D-n, this plan proceeds under them)

- D-1 Name "workspace slot"; tests are `C0824WorkspaceSlot*`.
- D-2 The runner owns the pool and ledger; the server owns the release decision.
- D-3 S1 covers the primary repository only; secondary repositories keep `task-<id>`.
- D-4 Slot leaf `ws-NN` under the existing worktree root.
- D-5 Headroom 4 (server2), 2 (desktop).
- D-6 Exhaustion grows an overflow slot (§3.2a); at the overflow bound it holds; it never falls back to
  a per-task mirror. (Revised by H-2.)
- D-7 Reuse the `antiphon-mirror-owner.json` stamp name.
- D-8 SourceLanding and land verification never use slots.
- D-9 Free slots are detached at the fetched `origin/master` tip.
- D-10 Release on terminal, plus seat free, plus sync debt not Pending or Held.
- D-11 Three lingering-process retries before quarantine.
- D-12 The CARD-0688 land worktree coexists with slots.

Added by revision 2 (defaults this plan chose while applying the operator's answers):
- D-13 Overflow: trigger = no Free slot, after waiting up to `OverflowWaitSeconds=10` when a release is
  in flight; cap `MaxOverflowSlots` 4 (server2) / 2 (desktop); disk budget `PoolDiskBudgetGiB=64` plus
  the `workspace_disk_low` floor plus `2 × SlotBytesEstimateGiB`; idle retirement after
  `IdleRetireHours=72`, highest-numbered first.
- D-14 "Native build output" kept by a warm reset = `**/bin/`, `**/obj/`, `**/node_modules/`,
  `client/dist/`. Custom `bin-*/` alternates are not native output and are wiped by every reset.
- D-15 Quarantine is per directory; every quarantine re-homes the slot. Re-homed directories are named
  `ws-NN-d<K>`; the old directory is `worktree lock`ed in place, never moved or deleted.
  `MaxQuarantinedDirectories=4` open per runner before dispatch holds; `MaxRehomesPerSlotPerDay=3`.
- D-16 Cold-reset schedule default **Weekly**, Sunday 02:00 UTC (`Off|Daily|Weekly`).
- D-17 The pre-rc cold reset is a step the orchestrator runs (`workspace-slots.ps1 cold-reset
  -AllRunners`) before starting the run; the start does not wait for leased slots.
- D-18 The checkpoint tool's own fresh `bin-<id>` custody output is unchanged; the native-output rule
  covers direct builds (CARD-1629 owns the wording).

### Decisions recorded (operator, 2026-11-02)

The operator's answer, verbatim (2026-11-02):

> "H1 yes, H2 hold as there should not be a slot available, if there is create a new reusable slot,
> even if its temporary, can be used for temporary future work which goes over the slots, also existing
> worktrees might become unusable (some weird file that cant be deleted) so need to be able to change
> the directory of a slot as well in case of a catastrophic failure that isnt recoverable. H4 yes one
> stable builds output per slot, just use the native build output, without specifying custom outputs.
> H4 no dont reset, reset should only be explicit or maybe once a day/week and eg before start of the
> whole rc run. H5 - sure that is fine. Rest are fine."

The operator's labels were slightly off (two "H4"s), so the answers are mapped by content (mapping given
by the dispatching orchestrator in the revision brief, 2026-11-02):

| # | Question | Decision (2026-11-02) | Where it is applied |
|---|---|---|---|
| H-1 | Delete origin branches after confirmed publication | **Yes** ("H1 yes"). Default off until S4 drains legacy mirrors per volume; 24 h delay; lease on the landed SHA | §3.10, S2 |
| H-2 | Pool exhausted: hold or fall back | **Hold** is the normal answer, but the pool may **create a new reusable overflow slot** (temporary by intent, a normal reusable slot afterwards) instead of leaving work stuck; and a slot's **directory must be changeable**: an unusable worktree is quarantined and the slot re-homed to a new directory, never blocking the pool | §3.2, §3.2a, §3.5, §3.5a, D-6, D-13, D-15, V-31..V-36 |
| H-3 | Stable build output per slot | **Yes, the native output**: each project's default `bin/`/`obj/`, no custom OutputPath, `-o` or `bin-ws/`; kept between tasks. CARD-1629 step 1 is revised in parallel to the same rule | §3.4, §3.8, D-14, D-18, V-27 |
| H-4 | Does a reset wipe build output? | **No by default.** A cold reset only explicitly (dispatch flag or operator action), on a schedule (daily or weekly), and before the start of a whole rc qualification run | §3.3, §3.12, D-16, D-17, V-28..V-30, V-37 |
| H-5 | Runner evidence before the wipe | **As proposed**: `.antiphon/**/*.md` + `checkpoints/*/report.json`, 7 days, 256 MiB cap per task | §3.4 |
| H-6 | Legacy cutoff and mechanism | **As recommended**: terminal ≥ 48 h; automatic legacy release policy; cancel CARD-0670 after the slices | §4, §5, S4 |
| H-7 | The seven absent 2026-09 worktrees | **As recommended**: check rollout receipts or archives once, then close | §5, S4 |
| H-8 | Slice order | **As recommended**: S1, S4, S5, S2, S3 | Summary, §6 |

## 8. Risks and safe rollout

| Risk | Mitigation |
|---|---|
| Legacy removal, publish or seat-release logic acting on a reused path (§3.6) | Typed `slot_managed` at the runner; generation-aware ownership; V-8/V-10 red-first |
| Cross-task leakage (deny hook, `.antiphon`, nested repo, process still writing) | Exact keep-list, free invariant re-checked at lease, process scan; V-2/V-5/V-6 |
| Destroying unpushed work | Reflog-based unpushed check, quarantine never wipes, AcceptLoss retains a patch; V-3/V-4 |
| Recycle audit refusals after branch deletion (CARD-1008) | Free slots detached at a published SHA with an expired reflog; S2 enabled per volume only after S4 |
| Windows locks and long paths make directories unusable | Deferred retries, Restart Manager attribution, `core.longpaths`; re-homing keeps the slot (V-34..V-36, V-20/V-21); more than `MaxQuarantinedDirectories` open holds dispatch loudly |
| Quarantined directories pile up on disk | Counted in `PoolDiskBudgetGiB`; attention with exact paths; `workspace_slot_pool_degraded` hold past the bound; operator release route |
| Overflow growth eats the disk | Count cap plus disk budget checked before creation (V-32); idle retirement (V-33) |
| Stale outputs from deleted projects in a warm native `bin/` | Cold reset per dispatch, by operator, weekly and before each rc run; TestDesign witness for assembly-globbing tests |
| A cold reset destroying a running task's build | Never while leased: pending until release (V-29) |
| Provider state keyed by cwd (Claude `~/.claude/projects/<enc-cwd>`; Codex `resume --last` unknown) | The tailer already requires C1–C4 positive identification (`TranscriptTailer.cs:13-33`), so a shared cwd cannot bind another task's transcript. Codex cwd-keyed resume is an **unknown**: TestDesign checks it before S1 code |
| Temp-runner drain (CARD-0831) | The drain reads the slot ledger; custody-quarantined directories block `down -v` |

**Rollout.**
1. Ship with all flags off, which is byte-identical behaviour (V-12/V-19).
2. Run a canary: S1 on **server2-temp** (or one runner), with one dispatch class first (Investigate/Plan
   docs-only tasks), for 48 h.
3. Watch the watchdog keys, quarantine, re-home and overflow counts. Measure warm and cold reset p95
   (budget below) and slot bytes.
4. Widen to all roles on that runner, then to server2 main.
5. Run S4 legacy cleanup, then S2 per volume.

Rollback at any step is flag off (§5).

## 9. Waiting-input checklist

This plan adds no new waiting session. It changes what a Blocked (waiting) task holds: its workspace
slot stays leased together with its seat. Only the task reaching a terminal state releases it
(through the §3.5 reconciler), or an operator cancel. With `BlockedTaskParking:Enabled=false` there is
**no automatic release deadline** (CARD-1083). A Blocked task can therefore hold a slot indefinitely.
Headroom, then overflow growth up to its bound, absorb that; past the bound the
`workspace_slot_unavailable` hold makes it visible instead of creating unbounded checkouts. A cold
reset requested for a Blocked task's slot stays pending for as long as the task holds the slot: it
has no deadline either, and the inventory shows it as pending.

## Performance budget

No test was run by this Plan. Measurement sources:
- the cf8b6982 receipts, server2 Linux, loaded, 2026-10-30/31: `tests/Antiphon.SessionRunner.Tests`
  build p50 70 s, p95 135 s, n=11; `tests/Antiphon.Tests` build p50 326 s, p95 579 s, n=117; slot wait
  p95 270–825 s, reported separately;
- worktree add 2.8 s (server2) and 40.4 s / 10.2 s (desktop, loaded / after cleanup).

- **Selections.** Each row is one class of fixture-repository tests. No AppHost, container, migration
  or browser is involved, except the S1 server rows, which use the existing shared Postgres test
  database fixture (`TestDbFixture`, once per assembly).
- **Per-test bodies.**
  - Scratch git repos of a few files: `git init`/commit/`worktree add` about 0.2–0.5 s on Linux. A
    single reset plus verify, with about 10 git spawns, is provisional at ≤ 3 s; TestDesign measures it.
  - Per-test timeout 30 s, provisional, to be replaced by about 2x measured p95.
  - Linux-native process-scan tests spawn one `sleep` child per test in its own process group
    (`ParallelLimiter<ProcessSpawnLimit>`) with a 10 s bound.
  - Overflow, re-home and cold-reset tests use the same scratch repos and a fake clock (no real waits:
    `OverflowWaitSeconds`, `IdleRetireHours` and the weekly boundary are driven by the test clock).
    Re-home seam tests inject failures without touching the filesystem's permissions; the one
    linux-native `0555` test restores permissions in a `finally` before cleanup. Provisional ≤ 3 s per
    body. Git-only classes take `ParallelLimiter<GitProcessSpawnLimit>`; any class that also spawns a
    non-git child (the `sleep` process-scan class) takes `ProcessSpawnLimit`.
- **Phase budget per row.**
  - Build: SessionRunner.Tests ≤ 135 s p95; Antiphon.Tests ≤ 579 s p95.
  - Fixture repo setup ≤ 2 s per class (shared template repo cloned per test with `--shared`).
  - Bodies ≤ 3 s each; teardown ≤ 2 s; slot wait is reported separately.
- **Row estimates.** SessionRunner rows that build 4 min (p95 build 2.3 min plus bodies), new
  SessionRunner classes reusing a build 2 min, existing regression classes 3 min, Antiphon.Tests rows
  12 min, Windows rows 15 min (unmeasured Windows build: provisional). No row is near 30 min.
- **Runs.** All rows are `serial: true`, so a run's wall time is the sum of its rows. The S1 group
  totals about 36 min, past the 30 min run standard, so S1 is executed as **two checkpoint runs**:
  `--rows CP-1,CP-2,CP-3,CP-6,CP-7,CP-8,CP-9,CP-10` (SessionRunner, two builds, ≈ 21 min) and
  `--rows CP-4,CP-5` (Antiphon.Tests, one build, ≈ 15 min). Every other After group is one run under
  30 min. Slot wait is reported separately and is not in these sums.
- **Production budget.** A warm slot reset RPC on the real 9,290-file tree is targeted at p95 ≤ 15 s on
  server2, with a 30 s RPC timeout. This is distinct from the existing 10 min per-git-op runner
  timeout. An overflow creation or re-home (`worktree add --detach` ≈ 2.8 s plus verify) fits inside
  the same 30 s lease RPC budget, including the ≤ 10 s overflow wait. A cold reset deletes about
  1–2.5 GiB of native output; it is targeted at p95 ≤ 60 s and runs outside any lease RPC (at release,
  or on a Free slot). S1 Code measures 20 warm resets, 5 cold resets and 3 overflow creations during the
  canary and records p50 and p95. Desktop warm reset target p95 ≤ 20 s.
- **Recording.** TRX per row under the checkpoint run, with per-test duration. Each reset span is logged
  by phase (fetch, checkout, clean, verify, process scan) in the runner log.

### Checkpoints

This is the closed ordinary scope per commissioned slice, with one After group per committed slice.
- Lanes: `portable` uses default placement with no runner or platform pin (currently server2);
  `linux-native` requires Linux (`/proc` scan); `windows-native` requires Windows (Restart Manager,
  long paths).
- Each build is the **checkpoint tool's** fresh `bin-` custody output with a forward slash, through the
  build-slot gate, removed after the run. This is the tool's evidence build (cold by design, D-18),
  not a slot's working build: the native-output rule (H-3) governs direct builds in a slot.
- Filters are class-scoped (`/*/<namespace>/<Class>/*`); TestDesign may narrow them to exact methods.
  Any OR must use `(NameA*)\|(NameB*)` with every operand parenthesized and starred.
- Rows are ordered by the decided ship order (S1, S4, S2, S3); S5 has no rows until its read and
  measurement are done. Revision 2 renumbered the rows (no run used the old numbers).
- CP-3 builds separately from CP-1 on purpose: it is a separate linux-native lane that may run on
  another host; CP-10 reuses the CP-3 build in that lane. `import --plan` (checkpoint tool whose
  `tools/Antiphon.Checkpoints` and `tools/Antiphon.HostCleanup` sources are identical to this base)
  accepted all 14 rows and only warned that CP-3 could reuse CP-1 when both lanes share one host.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s1r/ | portable-slot-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/* | V-1, V-2, V-11, V-27 (native output, no reset by default) | class, 0 failed/skipped | 7 | 4 | true |
| CP-2 | S1 | CP-1 | portable-slot-custody | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests/* | V-3, V-4, V-6, V-7, V-8, V-9 | class, 0 failed/skipped | 9 | 2 | true |
| CP-3 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c0824-s1l/ | linux-native-slot-process | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotProcessScanTests/* | V-5 | class, 0 failed/skipped | 2 | 4 | true |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c0824-s1s/ | portable-slot-server | /*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/* | V-10, V-12, V-13, V-37 | class, 0 failed/skipped | 6 | 12 | true |
| CP-5 | S1 | CP-4 | portable-slot-regression | /*/Antiphon.Tests.Application/RemoteWorkspacePreparerTests/* | flag-off mirror path unchanged | existing class, 0 failed/skipped | 1 | 3 | true |
| CP-6 | S1 | CP-1 | portable-binding-regression | /*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/* | CARD-1032 binding unchanged | existing class, 0 failed/skipped | 1 | 3 | true |
| CP-7 | S1 | CP-1 | portable-slot-overflow | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests/* | V-31, V-32, V-33 (overflow growth, bound, idle retirement) | class, 0 failed/skipped | 5 | 2 | true |
| CP-8 | S1 | CP-1 | portable-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/* | V-34, V-35 (re-homing via injected failures) | class, 0 failed/skipped | 4 | 2 | true |
| CP-9 | S1 | CP-1 | portable-slot-cold-reset | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotColdResetTests/* | V-28, V-29, V-30 (cold reset explicit, never while leased, scheduled) | class, 0 failed/skipped | 5 | 2 | true |
| CP-10 | S1 | CP-3 | linux-native-slot-rehome | /*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotNativeRehomeTests/* | V-36 (real unremovable directory) | class, 0 failed/skipped | 1 | 2 | true |
| CP-11 | S4 | tests/Antiphon.Tests -> bin-c0824-s4/ | portable-legacy-migration | /*/Antiphon.Tests.Application/C0824LegacyWorkspaceMigrationTests/* | V-24..V-26 | class, 0 failed/skipped | 3 | 12 | true |
| CP-12 | S2 | tests/Antiphon.Tests -> bin-c0824-s2/ | portable-branch-delete | /*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/* | V-14..V-19 | class, 0 failed/skipped | 6 | 12 | true |
| CP-13 | S3 | tests/Antiphon.Tests -> bin-c0824-s3w/ | windows-native-slot | /*/Antiphon.Tests.Infrastructure/C0824DesktopWorkspaceSlotTests/* | V-20..V-23 | class, 0 failed/skipped | 4 | 15 | true |
| CP-14 | S3 | CP-13 | windows-native-land-regression | /*/Antiphon.Tests.Infrastructure/LandWorkspaceTests/* | CARD-0688 land worktree unchanged | existing class, 0 failed/skipped | 1 | 4 | true |

Roster check: the new classes `C0824WorkspaceSlotResetTests`, `C0824WorkspaceSlotCustodyTests`,
`C0824WorkspaceSlotProcessScanTests`, `C0824WorkspaceSlotServerTests`,
`C0824WorkspaceSlotOverflowTests`, `C0824WorkspaceSlotRehomeTests`, `C0824WorkspaceSlotColdResetTests`,
`C0824WorkspaceSlotNativeRehomeTests`, `C0824PublishedBranchDeletionTests`,
`C0824DesktopWorkspaceSlotTests` and `C0824LegacyWorkspaceMigrationTests` are all exact class
filters. No name is a prefix of another (the native re-home class is deliberately
`...SlotNativeRehome...`, not `...SlotRehomeNative...`, so a future `(C0824WorkspaceSlotRehome*)` OR
operand cannot also match it; likewise `...SlotColdReset...` does not start with `...SlotReset...`).
No class in the repository starts with `C0824` (grep 2026-11-02). The existing classes are verified
exact names and namespaces from their files (2026-11-02): `RemoteWorkspacePreparerTests` in
`Antiphon.Tests.Application`, `RunnerWorkspaceBindingTests` in `Antiphon.SessionRunner.Tests`,
`LandWorkspaceTests` in `Antiphon.Tests.Infrastructure`.

## 10. Unknowns and what was not read

- Not read in full: the settlement sync and park and resume consumers of the desktop half (S5), the
  CARD-0692 branch's code (in Review), the inert CARD-1032 coordinator, and `RunnerWorkspaceCwd` cwd
  admission. Their behaviour is stated here only where quoted.
- How server2 mirrors older than about 7 days disappeared (no runner sweep exists) was not traced. It
  matches a volume recycle or a manual cleanup.
- Whether Codex `resume --last` (or any provider) keys state by cwd in a way a shared slot path could
  confuse was not checked.
- Reset p95 on the real tree, and the fraction of desktop registrations that are runner-task halves,
  are unmeasured. Both are listed as S1 and S5 entry measurements. Cold-reset p95 and overflow-creation
  time are unmeasured too (S1 canary).

### New open questions from revision 2 (none blocks TestDesign for S1; each has a stated default)

1. **Every quarantine re-homes (D-15).** The operator asked for re-homing on unrecoverable failures; this
   plan also re-homes on custody quarantines (unpushed work, dirt, lingering process), so the pool never
   shrinks. The cost is that quarantined directories, not slots, become the thing that accumulates,
   bounded by `MaxQuarantinedDirectories=4` and the disk budget. Confirm, or restrict re-homing to the
   filesystem class.
2. **Cold-reset schedule default (D-16).** Weekly, Sunday 02:00 UTC, is proposed; the operator said
   "once a day/week". One setting either way.
3. **`client/dist` counts as native build output (D-14).** Kept by a warm reset; E2E already rebuilds it.
   Say if it should be wiped like other generated content.
