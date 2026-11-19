# CARD-0824: hot worktree slots on the desktop

Date: 2026-11-19. Plan task `b68892ff`. Branch `feat/card-task-b68892ff`.
Base: master `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8`. Read-only on the
live hosts. No deploy, no restart, no production Postgres connection.
This document is the plan. It does not enable a flag.

**Outcome:** tasks whose session runs on the Windows machine lease a warm
slot, the same re-home contract server2 already runs, instead of a fresh
`card-task-*` checkout. Ref-only stays the control-plane answer for a task
whose session runs on another host. It is not built out here, and it is not
turned on.

The 2026-11-19 operator sentence is the reason this plan exists: stop
isolated cold worktrees and use hot ones, on the desktop as well. That
sentence is recorded below as D-1. It does not by itself edit the card.
`next: decide` is the acceptance of D-1 through D-12.

Label **UNOBSERVED** on every duration, byte size and latency below unless
the row names a source. Nothing in this session measured a desktop build or
a desktop `git worktree add`.

## What was read

- CARD-0824 (`card.ps1 get` and `history`, 105 revisions). Board search
  `worktree slot` returned 0 cards. Search `ref-only` returned CARD-0824 and
  CARD-0975 (Done; reviewed adoption after a corrupted worktree backlink).
  CARD-0975 is not this design.
- Plans and amendments: `2026-11-02-card-0824-per-slot-worktrees-plan.md`,
  `2026-11-03-card-0824-s4-s5-amendment.md` (A-D-6..A-D-12),
  `2026-11-05-card-0824-s5b-ref-only-plan.md`,
  `2026-11-14-card-0824-all-roles-enablement-plan.md`.
- Owners: `docs/session-runtime-invariants.md` (Runner workspace slots),
  `docs/docker-stack.md` (runner workspace slots, desktop-win),
  `docs/testing-and-build.md` (Build slots, native output, checkpoints),
  `docs/bootstrap.md` (desktop ports and `C:\src\Antiphon`).
  `docs/orchestration-loop.md` has no workspace-slot section and does not
  mention `-ColdWorkspace`. Dispatch build-slots and seat slots live there.
  Worktree health is `WorktreeHealthService` plus S4a, not that file.
- `scripts/delegate.ps1` header: `-Worktree`, `-Shared`, `-ReadOnly`,
  `-Runner`, `-ColdWorkspace`, `-FreshWorktree`.
- Slot code: `RunnerWorkspaceSlotPool`, `RunnerWorkspaceService.Slots.cs`,
  `WorkspaceSlotResetPolicy`, `RemoteWorkspaceService.DecideSlot` /
  `ResolveSlotDecisionAsync`, `WorktreeManager.CreateAsync`,
  `RefOnlyDesktopGate`, `WorktreeLegacyReleaseSettings`.
- Live reads at 2026-11-19 about 11:14 UTC, not compiled into placement:
  `GET /api/runner-defaults` revision 2, `globalRunnerId` server2, no kind
  overrides (operator reason 2026-09-26: desktop only for Windows work).
  `GET /api/session-runners`: `desktop` Windows, eligible, capacity 2,
  occupied 0, `capacityKind=delegatedTasks`, no `workspaceSlotV1` and no
  `workspaceRepositoryV1`; `server2` Linux, eligible, capacity 10, occupied
  7, `workspaceSlotV1` and `workspaceRetentionV1` present; `server2-temp`
  retired. `desktop-win` is not in the catalogue. `GET /api/version` is
  `997f9b8d9ffc4ba870d2fd56caf7c03781591624` with `land-v2`, not this base.
- `docs/investigations/2026-11-19-worktree-guard-exit-128.md`: rc run
  `a6278259` failed `git worktree add` exit 128 because the release checkout
  had no `core.longpaths`. One tracked path is 163 characters. A 105-character
  temp root makes 269, over MAX_PATH 260. `C:\src\Antiphon` has no local
  `core.longpaths`. System git config does not set it. `git worktree add`
  uses the source repository's config.

This plan adds no session that waits for input. The parking checklist does
not apply. With parking disabled, a Blocked task still has no automatic
release deadline (CARD-1083). A Blocked slot lease stays, on both hosts.

## Current state

A Worktree task has up to three directories. They are not the same object.

| Piece | Who creates it | Path today | Hot? |
|---|---|---|---|
| Desktop session cwd, `RunnerId` null | Server `DelegationWorktreeService.CreateForTaskAsync` → `WorktreeManager.CreateAsync` | `Git:WorktreeBasePath` + `card-task-<8hex>`. Committed default `C:\Antiphon\worktrees` (`server/appsettings.json`) | No. New directory, empty `bin/` and `obj/` |
| Control-plane checkout for a phone-home task | Same `CreateForTaskAsync`, unless `RefOnlyDesktopGate.Admits` | Same `card-task-*` leaf, on whichever host the API's `Git:WorktreeBasePath` names. Linux compose sets `/work/worktrees` | No, while the gate is off |
| Runner mirror | `RemoteWorkspaceService.MirrorAsync` → runner | Slots off: `<AllowedCwd>/worktrees/task-<8hex>`. Slots on: `<AllowedCwd>/worktrees/ws-NN` or `ws-NN-dK` | Only when both flags and the role gate say so |

The Windows catalogue row is the direct runner on port 17204. Its feature
list is session and Herdr features. It does not create worktrees. The server
creates `card-task-*` and the runner starts the agent with that cwd.
`PhoneHome:Enabled` is false in `src/Antiphon.SessionRunner/appsettings.json`.
`PhoneHome:AllowedCwd` defaults to `/work`, which is the Linux runner value.
`desktop-win` (CARD-1744 S4, `C:\Antiphon\runner\phone-home.json`) is the
future Windows phone-home id. The catalogue does not have it. The phase-2
env helper sets `PhoneHome__AllowedCwd` to `<root>/work`
(`scripts/lib/windows-phone-home-env.ps1`), so a mirror would land at
`C:/Antiphon/runner/work/worktrees/task-<8hex>` until slots are on.

server2 is the hot path. Both halves are on in the live process: the runner
advertises `workspaceSlotV1`, and the 2026-11-16 operational note records
`Delegation:WorkspaceSlotRoles=*`. Repository defaults stay off
(`Delegation:WorkspaceSlotsEnabled` false, runner flag false). `*` is every
role except Mutation. SourceLanding never leases
(`RemoteWorkspaceService.IsEligible`). The role string is one server value
for every runner. An invalid value leases nothing new and falls back to
`task-<id>`. `none` is the soft rollback and is reloadable only where the
server actually reloads JSON. The Docker API's
`Delegation__WorkspaceSlotRoles` is restart-bound.

Shared code, not Linux-only: `RunnerWorkspaceSlotPool`, the ledger, warm
reset, quarantine, re-home, and `WorkspaceSlotResetPolicy`. The pool is
constructed only when `PhoneHome:WorkspaceSlots:Enabled` is true
(`PhoneHomeCommandDispatcher.Workspace`). Flag off keeps the `task-<id>`
mirror byte for byte.

Linux-only around that pool:

- `ProcWorkspaceSlotProcessProbe` reads `/proc`. On any other host `Holders`
  returns no holders and `Scan` is incomplete. The pool calls `Holders` only,
  so a Windows release looks holder-free. The class comment says S3 owns
  Windows Restart Manager attribution. That S3 was folded into S5a and not
  built.
- `RunnerWorkspaceRetention` and the temp sweep are constructed only when
  `PhoneHome.Enabled && OperatingSystem.IsLinux()` (`Program.cs`). CARD-1677
  does not retire desktop `card-task-*`.
- Slot git does not pass `-c core.longpaths=true`. `LandingGit` status does.
  The runner's `GitAsync` does not.

Ref-only is on master and shipping off. `RefOnlyDesktopSettings` defaults
`Enabled=false`, `RunnerIds=[]`, `Roles=none`. `AgentTaskDispatcher` calls
`RefOnlyDesktopProvisioner.PlanAsync` only when the gate admits; otherwise
it calls `CreateForTaskAsync`. Consumers (sync, cleanup, health, landing)
already distinguish `DesktopWorkspaceKind.RefOnly`. There is no desktop slot
pool in the server process. A-D-10's `DesktopWorkspaceSlotPool` was never
added.

S4a is on. `WorktreeLegacyRelease` in `server/appsettings.json` has
`Enabled=true` and `Execute=true`, terminal age 48 hours, 25 releases a
tick. It files a system release for `card-task-*`. `WorktreeResidue:Execute`
stays false. Slot leaves `ws-*` are not its grammar.

`-ColdWorkspace` sets `workspaceColdReset` on the create body. On a slot
lease that becomes `Cold=true` and the warm keep-list is not applied. The
default lease keeps native output. The build-slot broker does not delete
`bin/` or `obj/`. It caps how many drivers run.

## Ground truth

| Assumption | What the code and the live reads show | Consequence |
|---|---|---|
| The desktop runner already leases slots | `desktop` has no `workspaceSlotV1`. Null `RunnerId` never calls `MirrorAsync` | Enabling the runner flag on 17204 does nothing until that process is phone-home and the task has a runner id |
| Ref-only removed desktop checkouts | Gate defaults off. Dispatcher still calls `CreateForTaskAsync` when the gate does not admit | Fresh `card-task-*` still grow. S4a is the only reaper, and only after 48 terminal hours |
| "Stop worktrees" means no desktop checkout | 2026-11-05 option a said that for a task that runs elsewhere. 2026-11-19 says the desktop execution workspace should be hot | D-1. Decide confirms it |
| One role list can canary the desktop alone | `Delegation:WorkspaceSlotRoles` is global. Live server2 already leases widely | D-8. A desktop canary must not write `Investigate` into that string |
| The slot pool is safe to turn on for Windows as-is | Holder probe is fail-open off Linux. Slot git has no long-path override. No rc fence in the pool | S3 before any flag |
| Warm output is wiped on every lease | `WorkspaceSlotResetPolicy.KeepList` keeps `**/bin/`, `**/obj/`, `**/node_modules/`, `client/dist/` unless `Cold` | D-6 keeps that rule |
| `C:\Antiphon\slots\ws-NN` falls out of the current pool | The runner sets worktree root to `AllowedCwd + "/worktrees"` | D-9. Do not fork the leaf layout |
| S4a will delete slots | Leaf grammar is `card-task-*` | Slots and legacy trees stay apart |
| This plan should finish ref-only | Producer admission is a separate gated feature with its own acceptance | Out of scope. Leave the gate off |

## Decisions

Stated defaults. `decide` accepts them or replaces a row. Rejected
alternatives are in the same row.

- **D-1 — Hot means the Windows session cwd is a reusable slot.** A task
  that runs on Windows leases `ws-NN`, checks out detached at the requested
  SHA on the task branch, and keeps native build output. Fresh
  `card-task-*` stops being the cwd for those tasks. Reason: that is the
  2026-11-19 sentence, and it is the same H-3 contract server2 already
  runs. Rejected: treating the sentence as "also delete the desktop
  checkout of a server2 task." The 2026-11-05 sentence still covers that
  checkout: if the session runs somewhere else, the desktop does not need a
  second tree. Ref-only remains that mechanism and stays off.
- **D-2 — The pool stays in the Windows phone-home runner.** Reuse
  `RunnerWorkspaceSlotPool`. Do not add `DesktopWorkspaceSlotPool` beside
  `DelegationWorktreeService`. Reason: the lease, ledger, quarantine and
  re-home already exist, and CARD-1744's `desktop-win` is the Windows
  runner that can host them. Rejected: A-D-10's server-process pool. It
  would be a second implementation of the same contract, and the live
  Windows runner does not create the directory today.
- **D-3 — `desktop-win` is the runner id that may lease.** The catalogue id
  `desktop` is the direct 17204 process (`capacityKind=delegatedTasks`).
  It is not given slots. Placement of a Windows Worktree task onto
  `desktop-win` is the switch that stops `CreateForTaskAsync` from being
  the session cwd. Until that id is in the catalogue, local Windows tasks
  keep today's `card-task-*` path. S4 does not invent a runner.
- **D-4 — No second checkout on the same machine.** When the session cwd is
  the Windows slot, settlement uses that slot as the registered checkout.
  The dispatcher must not also `CreateForTaskAsync` a `card-task-*` for
  that task. Reason: two Windows trees double the disk and only one is
  compiled. A task whose runner is server2 is unchanged: Linux slot plus
  the existing control-plane checkout (or ref-only, still off).
- **D-5 — Pool size is the Windows runner's own capacity.** Observed
  desktop delegated capacity is 2. `desktop-win` is documented at
  `MaxCapacity` 2. Base slots = capacity + `Headroom` 2 = 4. `MaxOverflowSlots`
  2. `SlotBytesEstimateGiB` stays 3 until S1 measures one real tree; the
  desktop `PoolDiskBudgetGiB` is set to 32 in the runner env and not in the
  repository default (the code default 64 would also be safe, and 32 is the
  tighter canary). Rejected: A-D-11's 16, which sized a desktop pool for
  every server2 half. Those halves are not this pool.
- **D-6 — Warm keep, cold wipe, same as server2.** A normal lease keeps
  `bin/`, `obj/`, `node_modules/` and `client/dist/`. Custom `bin-*` is not
  native output and is cleaned every reset. A cold wipe happens only for
  `-ColdWorkspace`, an operator or pre-rc cold reset, or the weekly schedule
  (Sunday 02:00 UTC) when that slot is free. Poison across tasks is bounded
  by the checkout, not by deleting `obj/` every time:
  - the lease checks out the requested SHA with `--force --detach` before
    the session; the previous task's source is not the base
  - one lease is one session, so one build runs in that tree
  - MSBuild rebuilds inputs whose timestamps change; a restored file that
    keeps an old write time still needs the CARD-0412 touch, or
    `--no-incremental`
  - a different `-p:` set shares `obj/` and rebuilds what it invalidates;
    the slot does not promise two property sets at once
  - an untracked file planted inside a kept `bin/` still survives (existing
    Q-3). That is accepted, not a Windows exception
  Rejected: wiping native output on every Windows lease. That makes every
  task cold, which is the cost this card exists to remove.
- **D-7 — A Windows holder scan is fail-closed.** `Holders()` returning an
  empty list is not proof on Windows. The pool treats an incomplete scan as
  lingering: defer, then quarantine and re-home. The Windows probe is
  query-only (process cwd census, then `RmGetList` on the paths a failed
  clean names, at most 64). It never kills. Linux keeps `/proc`. Rejected:
  turning the flag on with the current fail-open probe.
- **D-8 — Desktop admission is a separate allow-list.** New settings,
  default off: `Delegation:WindowsSlotRunnerIds` empty, and
  `Delegation:WindowsSlotRoles` `none`, same grammar as
  `WorkspaceSlotRoles` (`*`, `none`, or comma-separated canonical names, no
  spaces, Mutation forbidden). A Windows runner leases only when its id is
  listed and the role is admitted. server2 ignores these keys and keeps
  `Delegation:WorkspaceSlotRoles`. Rejected: writing `Investigate` into the
  global role string. That would also narrow server2.
- **D-9 — Short root, and long paths on every slot git call.** Slot
  `AllowedCwd` is `C:/Antiphon/slots`. The pool then uses
  `C:/Antiphon/slots/worktrees/ws-NN`. Ledger
  `C:/Antiphon/slots/workspace-slots/ledger.json`. Retained evidence
  `C:/Antiphon/slots/retained/`. Every slot `worktree add`, checkout,
  clean, status and reset passes `-c core.longpaths=true`, and the runner
  clone gets local `core.longpaths=true` once at provision. Flag-off legacy
  mirrors do not gain the argument (byte-identical path). Reason: run
  `a6278259` showed `git worktree add` exit 128 when the source config lacks
  the key; one tracked file is already past MAX_PATH under a long root, and
  `bin/` / `obj/` paths are longer than tracked files. A short root alone
  does not cover a later longer path. Rejected: `C:\Antiphon\worktrees`
  (legacy `card-task-*` and the S4a root), `C:\src\Antiphon` (AppHost
  checkout; no local longpaths today; a slot must not attach there),
  `C:\Antiphon\releases\checkout-*` and
  `C:\Antiphon\releases\candidates\*` (qualification).
- **D-10 — Qualification fence.** While
  `C:\Antiphon\verification\native-run.lock` exists or cannot be read, cold
  and scheduled resets record pending and wait. Warm lease and release
  continue. Slot git does not take a build slot. A compile inside a leased
  slot takes one desktop build slot (budget 2, max CPU 4, 6144 MiB floor)
  and queues behind the qualification holder. Do not raise that budget.
  The pre-rc cold reset runs before the lock is taken. Rejected: blocking
  warm lease for the whole Full run. That holds every Windows task for the
  run. Q-1 already accepted the warm reading.
- **D-11 — Legacy `card-task-*` stay on S4a.** No hand delete, no new
  reaper. S1 counts them. S4a already removes a terminal tree at least 48
  hours old when its release checks pass, and it skips runner-mirror
  removal for runners in `RunnerWorktreeRetention:Runners`. Dirty,
  unpushed, or live trees stay. Slot directories are not in that sweep.
- **D-12 — Rollback is the Windows allow-list, not the global role
  string.** Clear `WindowsSlotRunnerIds` (soft: no new Windows leases,
  releases continue). Keep the runner flag on until the ledger shows no
  Leased row for those tasks, then set
  `PhoneHome__WorkspaceSlots__Enabled=false` and recreate that runner.
  server2's flag and role string are not the rollback handle.

## What hot is on Windows

A pool of 4 base directories plus at most 2 overflow, created on first
lease, not pre-filled with `bin/`. Each lease:

1. Proves the directory is that slot's own worktree (existing D-1 proof).
2. Refuses a link, a foreign `.git`, or an owner stamp for another
   generation.
3. Requires a complete holder scan. A lock or an incomplete scan defers
   (1, 2, 4 minutes) and then quarantines in place (`git worktree lock`)
   and re-homes to `ws-NN-dK`. Nothing is killed to free the slot.
4. `git checkout --force --detach <sha>` and `git clean` with the warm
   keep-list, every command under `-c core.longpaths=true`.
5. Checks out the task branch at that SHA. The previous lessee's branch
   ref is deleted only after the unpushed check, as on server2.
6. Returns the same path for the session cwd and for settlement.

Warmth is whatever the last lease compiled. The first lease after a cold
reset or a new slot is a cold build. That cost is UNOBSERVED. S1 measures
it on one existing `card-task-*` that already has `bin/` and `obj/`, and
on one fresh add, and only when `native-run.lock` is absent.

NuGet and npm caches are not inside the slot. They stay the user/runner
caches. The slot does not delete them.

Restart: a runner restart keeps Leased rows. The server still owes the
release. The ledger is a file under the slot root and is not wiped by
process start. A store-id change keeps the existing
`workspace_slot_release_store_changed` refusal.

## Desktop hazards

- **Qualification.** The Windows Full run owns the desktop build-slot
  broker for the run (pid liveness, 48 hour absolute cap). Slot resets do
  not join that queue. Slot compiles do. S2 and S4 do not run `dotnet
  build` while `native-run.lock` exists.
- **AppHost checkout.** `C:\src\Antiphon` is never a slot, never the runner
  repository, and never receives `core.longpaths` as a side effect of
  provision. The runner clone is `C:/Antiphon/slots/repos/antiphon`.
- **Release checkouts.** `C:\Antiphon\releases\checkout-*` and the
  candidate directory used by run `a6278259` stay outside the pool. The
  provision script refuses a path under `C:\Antiphon\releases`,
  `C:\src\Antiphon`, or `C:\Antiphon\worktrees`.
- **Defender and locks.** A scanner or a leftover `testhost` / `dotnet` /
  `VBCSCompiler` holding a file makes `git clean` fail. The slot
  quarantines and re-homes. It does not retry the clean in a loop and it
  does not stop the process. `Directory.Build.rsp` already sets
  `-nodeReuse:false`; the idle compiler can still hold files for minutes.
- **Path length.** See D-9 and the `a6278259` investigation. The long file
  is 163 characters. `C:\Antiphon\slots\worktrees\ws-01` plus that file is
  under 260. `obj/` paths are not. The `-c` override is the gate, the
  short root is the margin.
- **PowerShell 5.1.** `scripts/desktop-slot-provision.ps1` is ASCII, no
  smart quotes, and parses under `powershell.exe` 5.1 as well as `pwsh`.
  It is not a logon task. The logon runner scripts are untouched.
- **Disk.** Four warm trees with `bin/`, `obj/` and `node_modules` are
  multi-gigabyte. The 32 GiB budget guards overflow creation the same way
  the server2 64 GiB budget does: it is not a hard cap on later `bin/`
  growth. S1 records free bytes on the volume and the size of one real
  `card-task-*`. Quarantined `ws-NN-dK` directories stay until
  `scripts/workspace-slots.ps1 release`. They are not S4a input.
- **Build slot versus workspace slot.** Different leases. Resetting a
  workspace slot does not hold a build slot. Building in it does. Two
  Windows sessions can compile at once only up to the desktop build budget
  of 2, and they must be different slot directories.

## Slices

Each slice lands on its own. The flag stays off through S3. S4 and S5 are
operator steps with exact commands, after `desktop-win` exists.

### S1 — Read-only census on the desktop

No code. An orchestrator or the operator runs this from the desktop, in
`pwsh`, and pastes the counts into the card. Do not delete, reset, build,
or restart. Skip the build lines when `native-run.lock` exists.

```powershell
$lock = 'C:\Antiphon\verification\native-run.lock'
'lock=' + (Test-Path -LiteralPath $lock)
'phone-home=' + (Test-Path -LiteralPath 'C:\Antiphon\runner\phone-home.json')
'longpaths-src=' + (git -C C:\src\Antiphon config --local --get core.longpaths)
Get-ChildItem -LiteralPath 'C:\Antiphon\worktrees' -Directory -ErrorAction SilentlyContinue |
  Group-Object { if ($_.Name -like 'card-task-*') {'card-task'} elseif ($_.Name -like 'ws-*') {'ws'} else {'other'} } |
  Select-Object Name, Count
Get-PSDrive -Name C | Select-Object Used, Free
```

If the lock is absent, time one existing `card-task-*` that already
contains `bin\` and one fresh `git worktree add` into a temp directory
that the command deletes afterwards. Record seconds. Those two numbers are
the cold-add and warm-build baselines. They are UNOBSERVED until this
runs. Do not point the add at `C:\src\Antiphon` or a release checkout.

Rollback: none. The commands do not write.

### S2 — Provision script

Goal: create the short root and prove `core.longpaths` without leasing.

Files: `scripts/desktop-slot-provision.ps1`,
`scripts/test-desktop-slot-provision.ps1`.

The script takes `-Root` (default `C:\Antiphon\slots`) and `-WhatIf`.
It refuses a root under `C:\src\Antiphon`, `C:\Antiphon\releases`, or
`C:\Antiphon\worktrees`, a root that is a filesystem root, and a root whose
free space is below 32 GiB. It creates `worktrees`, `workspace-slots`,
`retained`, and `repos`. It does not clone and it does not call `dotnet`.
On an existing runner clone it sets local `core.longpaths=true` and fails
if `git config --local --get core.longpaths` is not `true`. ASCII only.

Red witness: the script test points `-Root` at a fixture path whose parent
is named `releases` and expects exit 2 and no directory created. A second
case uses a short fixture root and expects `core.longpaths=true` on a
fixture git dir the test creates.

Rollback: `desktop-slot-provision.ps1 -Root C:\Antiphon\slots -RemoveEmpty`
removes the four directories only when the ledger is absent and
`worktrees` has no children. A ledger or a child refuses.

### S3 — Windows lease behind flags that default off

Goal: the shared pool is safe on Windows, and the server will not ask a
Windows runner for a slot until the new allow-list says so.

Files:

- `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs` — if the probe
  implements `IWorkspaceHolderScan` and `Complete` is false, that is
  lingering, same as a holder.
- `src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs` — new.
  Query-only cwd census plus `RmGetList` on at most 64 failed paths.
  Default pool probe stays `ProcWorkspaceSlotProcessProbe` on Linux and
  becomes this type on Windows. Tests that pass an explicit probe are
  unchanged.
- `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs` — slot git only
  (lease, reset, re-home, worktree add for a slot) passes
  `-c core.longpaths=true` before the subcommand. Legacy `task-<id>` git
  does not.
- `server/Application/Settings/DelegationSettings.cs` and
  `RemoteWorkspaceService.DecideSlot` — `WindowsSlotRunnerIds` and
  `WindowsSlotRoles`. A runner the catalogue reports as Windows leases
  only when both admit. Any other runner uses the existing role gate.
  Defaults: empty ids, roles `none`.
- `src/Antiphon.SessionRunner/PhoneHomeWorkspaceSlotSettings.cs` —
  `RcFencePath` default empty. When set, cold and scheduled reset check
  that path and treat a missing read as held. The desktop env sets it to
  `C:/Antiphon/verification/native-run.lock` in S4, not in the repository
  default.
- Tests named in the checkpoint table.
- Docs: one paragraph in `docs/session-runtime-invariants.md` and one in
  `docs/docker-stack.md` stating the Windows allow-list and the short root.
  No fleet host, no port pin beyond the existing 17204 build-slot sentence.

Red witnesses, committed before the fix:

- `C0824WindowsSlotHolderTests.Incomplete_scan_defers_release`. Pool built
  with `new ProcWorkspaceSlotProcessProbe("/proc", false)`. A clean slot
  release today returns free, because `Holders` is empty. The test expects
  deferred.
- `C0824WindowsSlotLongPathTests.Slot_checkout_passes_longpaths`. The git
  start seam records argv. Slot checkout today has no
  `core.longpaths=true`. The test expects that argument. A legacy mirror
  add in the same class must not gain it.
- `C0824WindowsSlotAdmissionTests.Windows_runner_stays_legacy_until_listed`.
  A Windows runner that advertises `workspaceSlotV1`, with global roles
  `*`, still gets the legacy mirror request. Today `DecideSlot` would mark
  the preparation slot. The test expects `reason` containing
  `windows_roles_none` (the new reason) and `Slot=false`.

No migration. No new public route. No operation-label change. No remote
script function change. No statement-budget change. The wire request stays
`Slot` / `Cold`. `scripts/impacted-classes.ps1` on the slice diff: the
`workspace-mutation-lock` row fires only if `PhoneHomeCommandDispatcher.cs`
changes. This slice does not need to change that file, because the pool
picks the probe. If a later edit touches it, Code adds
`C0824WorkspaceSlotCustodyTests` to the closed list before running.

Rollback of the running system: leave both new settings at their defaults.
The new code leases nobody. server2's existing decision path is the branch
that ignores the Windows keys. A defect in that branch is a Code fix, not
a flag.

### S4 — One role, after `desktop-win` is up

No product code. Operator steps, in order:

1. Confirm `GET /api/session-runners` contains `desktop-win`, platform
   windows, `dispatchEligible=true`. If it does not, stop. CARD-1744 S4 is
   the missing piece. Do not retarget the `desktop` row.
2. Run S2's script for real: `powershell.exe -NoProfile -File
   scripts\desktop-slot-provision.ps1 -Root C:\Antiphon\slots -Execute`.
3. Set the runner env `PhoneHome__AllowedCwd=C:/Antiphon/slots`,
   `PhoneHome__RunnerRepository=C:/Antiphon/slots/repos/antiphon`,
   `PhoneHome__WorkspaceSlots__Enabled=true`,
   `PhoneHome__WorkspaceSlots__Headroom=2`,
   `PhoneHome__WorkspaceSlots__MaxOverflowSlots=2`,
   `PhoneHome__WorkspaceSlots__PoolDiskBudgetGiB=32`,
   `PhoneHome__WorkspaceSlots__RcFencePath=C:/Antiphon/verification/native-run.lock`.
   Recreate only that runner, through its existing restart, when no
   qualification lock is held. Do not recreate server2.
4. Set `Delegation:WindowsSlotRunnerIds` to `desktop-win` and
   `Delegation:WindowsSlotRoles` to `Investigate`. Do not edit
   `Delegation:WorkspaceSlotRoles`.
5. Dispatch one Investigate Worktree task with `-Runner desktop-win
   -Platform Windows`. Require `WorkspaceSlotDecision` slot, and a ledger
   path under `C:/Antiphon/slots/worktrees/ws-`. Record the seconds from
   dispatch to the session cwd existing, and the first build's minutes.
   Those are the success numbers. UNOBSERVED until this step.

Rollback: D-12. Clear `WindowsSlotRunnerIds` first. Do not set the global
role string to `none`.

### S5 — Widen Windows roles

Still the Windows allow-list only. After S4's one task releases and a
second Investigate task reuses the same `ws-` id:

`Delegation:WindowsSlotRoles=Investigate,Plan,TestDesign,Docs,Debug`

Code, Review, Merge, Deploy, Mutation and SourceLanding stay off this list
until a later decide. Mutation and SourceLanding cannot lease anyway.
Code is the role that makes warmth pay; it waits until S4's measured warm
build is actually shorter than S1's cold build. If it is not, stop and
report the numbers. Do not add Code to hide a cold result.

Rollback: set `WindowsSlotRoles` back to `Investigate`, or clear the runner
ids (D-12).

### Legacy directories

S4a keeps owning `C:\Antiphon\worktrees\card-task-*`. This plan does not
change its cron, its 48 hour floor, or its execute switch. S1's count is
the inventory. A tree S4a refuses (dirty, unpushed, live session, under 48
hours) stays on disk and stays listed in the retirement refusal. Quarantined
slot directories under `C:\Antiphon\slots` are released only through
`scripts/workspace-slots.ps1 release -Runner desktop-win -DirId <id>`.
Never delete either tree by hand.

## Verification design

TestDesign is not folded into this dispatch. The rows below are the closed
list once `decide` accepts the defaults. A decide that changes D-1, D-2 or
D-8 sends the rows back to TestDesign. Code runs the table on the Linux
lane (omit `-Runner`, omit `-Platform`). The real `RmGetList` probe is not
in the table: it needs Windows, and a Linux run would skip it. The desktop
command after S3 is the witness, and it is listed here so it is not an
unlisted run:

```powershell
pwsh -NoProfile -File scripts\build-slot.ps1 -Label win-slot-probe -- dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build -- --treenode-filter "/*/*/C0824WindowsSlotHolderTests/Rm_lock_defers_release*"
```

Run that only on the desktop, only when `native-run.lock` is absent, after
the Linux rows are green. One method. It plants a process that holds a
file in a fixture slot and expects deferred. UNOBSERVED duration; stop it
if it exceeds 10 minutes and file a Debug task.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | n/a | provision-script | `pwsh -NoProfile -File scripts/test-desktop-slot-provision.ps1` | D-9, S2 | exit 0; the releases-parent case exits 2 and creates nothing | n/a | 15 |
| CP-2 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-win-slot/` | holder-scan | `/*/*/C0824WindowsSlotHolderTests/*` | D-7 | Linux: 1 executed, 1 skipped, 0 failed; Windows: 2 executed, 0 failed | 1 linux / 2 windows | 15 |
| CP-3 | S3 | CP-2 | longpaths | `/*/*/C0824WindowsSlotLongPathTests/*` | D-9 | all listed, 0 failed | 2 | 8 |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-win-admit/` | admission | `/*/*/C0824WindowsSlotAdmissionTests/*` | D-8 | all listed, 0 failed | 2 | 20 |

CP-2's two methods are `Incomplete_scan_defers_release` and
`Rm_lock_defers_release`. On Linux the second is skipped by its Windows
attribute, so Min is `1 linux / 2 windows`. That split is the closed floor.
Do not delete the Windows method, and do not edit Min after the first run.
CP-3's two methods are `Slot_checkout_passes_longpaths` and
`Legacy_mirror_omits_longpaths`. CP-4's two methods are
`Windows_runner_stays_legacy_until_listed` and
`Listed_investigate_runner_leases`.

No OR filter. Class names are new, so a trailing `*` cannot collide with
an existing `C0824` class. The first row's 15 minutes is the required
floor for an unmeasured script row, not a measured p95.

## Performance budget

No production timeout changes. Existing `C0824` slot classes keep the
CARD-1722 60 second ceiling. Windows collection for those classes is still
pending and this plan does not widen them.

Selection: the four rows above, nothing else in the closed list. The
desktop `Rm_lock` command is the one extra, with the reason in the
verification section. Project graph: CP-1 is a script and does not build
the solution. CP-2 builds `tests/Antiphon.SessionRunner.Tests` once.
CP-4 builds `tests/Antiphon.Tests` once. Downstream coverage is those
assemblies' own references. No AppHost, no container, no migration, no
shared Postgres.

Durations are **UNOBSERVED**. Provisional row budgets are the
`EstimatedMinutes` column (15, 15, 8, 20). They include the build. The
test bodies are expected to be seconds because they use the existing slot
scratch and a fake or captured git start, not a live runner. There is no
measured p95 in this session, so no timeout is set from them. The
checkpoint tool's own row deadline applies. A row whose wall time exceeds
30 minutes is stopped and a Debug task is filed with the timing. It is not
retried with a larger timeout. After the first green run, replace the
provisional minutes with the measured p95 and set any new test
`[Timeout]` to about twice that p95. Until then the new tests use the
assembly's existing default.

Phases: script parse and fixture git (CP-1); build (CP-2, CP-4, reported
separate from the test body); slot scratch create; the assertion body;
scratch delete. No host startup. Build-slot wait is reported on the
`CHECKPOINT` line and is not part of the product budget.

Setup: one scratch per test, the existing `C0824SlotScratch` pattern.
Nothing is shared across tests that would leak a slot directory. CP-3
reuses CP-2's build.

Recording: the checkpoint `report.json` under the tool's results root.
Code copies the `CHECKPOINT` lines into the task report. S1's desktop
counts and S4's dispatch-to-cwd seconds and first-build minutes are the
success numbers, stored on the card, not in git.

Success, once S4 has run: the second Investigate task's build minutes are
lower than S1's fresh-tree build minutes, and the dispatch-to-cwd time is
lower than S1's fresh `worktree add`. If either is not, S5 does not add
Code.

## Existing tests

No migration, public DTO, HTTP route, operation label, remote script
function, or statement budget. Impacted-classes: none of the current globs
match the files in S2 or S3 unless `PhoneHomeCommandDispatcher.cs` is
edited. The `workspace-mutation-lock` row
(`C0824WorkspaceSlotCustodyTests`) is the one to add if that file changes.
Do not run the Linux `/proc` classes
`C0824WorkspaceSlotProcessScanTests` as a substitute for the Windows probe.
They stay Linux-native and are not in this table.
