# CARD-1750: Turn on per-slot worktrees with the next rollout

Task: Plan `bc1e5202`. Card: CARD-1750 (`fbd0ad8f-5a9b-4338-8171-d06fdd643259`), High/Soon.
Base: `188ac942166884f060fdcc7cbf3b15f670a27b43`. Branch: `feat/card-task-bc1e5202`.
Lane: Unit. Platform: Any. This plan changes no product code. It does not name a fleet host
beyond the runner ids the existing rollout already uses (`server2`, `server2-temp`).

The slot model already shipped (CARD-0824). This card turns it on, temp canary first, during
the next server2 rollout of the rc tip and the AppHost restart that matches that tip.
CARD-1744 has not cut over: topology A is still production, so the server restart is
`scripts/restart-apphost.ps1`. `scripts/restart-linux-server.ps1` is not this activation.

Operator decisions this plan applies, and does not reopen:

- CARD-0824 H-2 / D-6 / D-13 / D-15: when no free slot remains, grow a reusable overflow slot
  (cap and disk budget); at the bound, hold. A directory that cannot be reset is re-homed.
  There is no fallback to a per-task mirror.
- CARD-0824 H-3 / D-14: warm reset keeps native build output (`**/bin/`, `**/obj/`,
  `**/node_modules/`, `client/dist/`). Direct builds in a slot pass no `OutputPath`.
- CARD-0824 H-4 / D-16: no cold reset by default. Cold reset is explicit, weekly (Sunday 02:00 UTC),
  or a pre-rc request. Enabling the flags does not cold-reset the pool.
- Operator 2026-11-07: file the turn-on into the next restart. Temp is the canary. Standing stays
  legacy until that canary passes.

Investigation `docs/investigations/2026-11-06-investigate-slot-and-retention-settings.md` on
`feat/card-task-374f6129` (`4b9e3a58dcb9c307933a9870daeedc5cff7175ba`) described a deployment
transport gap at `d164e93ae`. That gap is closed on this base: `57d0e3cb9c8f8021e1aad583a0599100fa3eda20`
is an ancestor, and the Compose passthrough plus the strict saved-assignment reader are in the
tree cited below. Do not commission another transport change.

## Intended behaviour

### Before (both flags off)

A Worktree task on server2 gets a per-task mirror `task-<8hex>` under `/work/worktrees`.
`RemoteWorkspaceService.DecideSlot` returns `flag_off` (`server/Application/Services/RemoteWorkspaceService.cs:227-232`).
`ResolveSlotDecisionAsync` writes no `WorkspaceSlotDecision` event when the server flag is off,
the preparation is missing, or the runner does not advertise `workspaceSlotV1` (`:247-251`).
The runner advertises that feature only while its own flag is on
(`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:169-173`).
Retention of finished `task-<8hex>` mirrors is already on for `server2`. Slots are not its
candidates. Nothing in this card changes that job's tracked settings.

### After (canary, then standing)

1. The temp runner container is recreated with `PhoneHome__WorkspaceSlots__Enabled=true`.
   Standing stays false, so it does not advertise `workspaceSlotV1`.
2. The desktop server restarts once with `Delegation:WorkspaceSlotsEnabled=true` and
   `Delegation:WorkspaceSlotRoles=none`. The flag is captured from `IOptions` at construction
   (`RemoteWorkspaceService.cs:69`) and is not reloaded. The role string is read from
   `IOptionsMonitor` on every decision (`WorkspaceSlotRoleGate.cs:20-38`).
3. With the server flag on and the temp runner advertising the feature, a preparation is
   recorded. `none` records `workspace_slot_decision: legacy ... reason=roles_none` and still
   sends a `task-<8hex>` mirror. Releases continue (`WorkspaceSlotReleaseHostedService`,
   `server/Application/Services/WorkspaceSlotReleaseService.cs:266-276`).
4. `WorkspaceSlotRoles=Plan` is a file edit, not a restart. The orchestrator then creates one
   ordinary Worktree Plan task on `server2-temp`. The decision is
   `workspace_slot_decision: slot ... reason=listed` before the RPC
   (`RemoteWorkspaceService.cs:271-278`). The runner leases `ws-NN` (or a re-homed `ws-NN-dK`)
   from the pool, warm-resets it to the SHA the server already resolved, and returns that path.
5. Release runs only when the task is terminal, its session holds no seat, no sync debt is
   Pending or Held, and no live task follows up (`WorkspaceSlotReleaseService.EligibleAsync`,
   `:251-262`). The server clears `RemoteWorktreePath` only after the runner replies. The slot
   id and generation stay on the task (`RemoteWorktreePath` at `server/Domain/Entities/AgentTask.cs:256`;
   slot id, generation and `RemoteWorkspaceSlotReleasedAt` at `:281-286`).
6. While temp is primary, roles widen to `Investigate,TestDesign,Docs`. Mutation is never a
   list member. SourceLanding never leases (`WorkspaceSlotRoles.Admits`,
   `server/Application/Settings/WorkspaceSlotRoles.cs:99-106`), including under `*`.
7. After `drain-old` reaches zero counters, standing gets the same runner flag and is recreated.
   One ordinary Worktree Plan on `server2` must lease and release before roles become `*`.
8. `drain-temp` and `retire-temp` then follow the existing CARD-0934 gates. A free slot sits
   detached at a published SHA. A custody quarantine that still holds unpublished work refuses
   the recycle. That refusal is a stop, not a prompt to delete the directory.

What stays unchanged, file and line, is the whole product surface. Code does not edit these.
The operational writes are untracked secrets and the ignored desktop overlay:

| Site | Lines | Stays |
|---|---|---|
| `docker-compose.server2-runner.yml` | 64-72 | `PhoneHome__Capacity` 10. Slot flag interpolates `${PhoneHome__WorkspaceSlots__Enabled:-false}` at line 65. Build slots stay `MaxConcurrent` 4, `MaxCpuCount` 6, `MinAvailableMemoryMb` 16384 (lines 70-72). |
| `docker-compose.server2-runner.temp.yml` | service `session-runner` environment | No slot key. Temp inherits the base mapping and the wrapper passes temp's own file. |
| `scripts/c590-remote.sh` | reader `runner_workspace_slots_enabled` 1095-1117; `compose_host` 949-969; `compose_temp` 977-988; `case_deploy_parent` reads at 9574 and writes the standing file at 9588-9593; `case_deploy_temp_runner` reads at 9829 and writes the temp file at 9859-9864 | Exact `=true` or `=false`, once, LF. Anything else is `RunnerWorkspaceSlotsSettingInvalid`, exit 2, before a side effect. Missing file or key is false. |
| `docker/stack.env.example` | 12-14 | Documents the key, default false. |
| `server/Application/Settings/DelegationSettings.cs` | 711, 719 | `WorkspaceSlotsEnabled` default false. `WorkspaceSlotRoles` default `*`. |
| `server/Application/Settings/WorkspaceSlotRoles.cs` | 12-60, 95-110 | Grammar. Mutation cannot be listed. SourceLanding never leases. |
| `server/Application/Services/WorkspaceSlotRoleGate.cs` | 16-47 | Invalid reload leases nothing, one warning, legacy mirror, no hold. |
| `server/appsettings.json` | 245-258 | Retention stays Enabled/Execute, `Runners` `server2` only (not `server2-temp`), `MinTerminalHours` 12. |
| `src/Antiphon.SessionRunner/PhoneHomeWorkspaceSlotSettings.cs` | 9-34 | Headroom 4, overflow wait 10 s, max overflow 4, 3 GiB estimate, 64 GiB pool budget, idle retire 72 h, max 4 open quarantines, max 3 re-homes per slot per day, weekly cold reset, 256 MiB / 7 day evidence. |
| `src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs` | 63, 153-214, 242-251 | `Target = Capacity + Headroom` (10+4 = 14 on this compose). Overflow only past that target. Disk floor is free space below `max(20 GiB, total/10) + 2 × estimate`, plus the 64 GiB pool sum. |
| `server/Application/Dtos/SessionRunnerCatalogueDtos.cs` | 17 | `Features` on `GET /api/session-runners`. |
| `server/Application/Services/SessionRunnerCatalogue.cs` | 155 | Copies the runner's feature list into that row. |
| RefOnly desktop, land worktree, parking, build-slot broker | — | Off or unchanged. Slots do not remove desktop checkouts. Enabling `RefOnlyDesktop` is out of scope. |

Pool behaviour the canary relies on, already implemented, not retuned:

- Warm reset keeps native build output. An untracked file planted inside a kept `bin/` survives (accepted Q-3). Custom `bin-<id>/` is not native output and is wiped.
- Reset, re-home and overflow creation take no build slot (`docs/session-runtime-invariants.md:380`).
- At `overflow_cap` or `disk_budget` the lease refuses `workspace_slot_unavailable:<bound>`. The task stays Queued with a Held event and never receives a per-task mirror.
- A failed reset quarantines that directory in place (`git worktree lock`) and re-homes the slot. Two failed replacements mark only that slot Failed. The pool continues.

### Ground truth

| The card or the 2026-11-06 investigation assumes | What this base does |
|---|---|
| Two flags, both off, both required | True. Server `Delegation:WorkspaceSlotsEnabled` (restart-bound, default false) and runner `PhoneHome:WorkspaceSlots:Enabled` (default false). Either one off keeps the per-task mirror. |
| Named phases cannot carry the runner flag | False now. The phases preserve each stack file's own assignment and the wrappers forward it. A missing key stays false. |
| Set temp first, standing only at redeploy-old | The mechanism allows it. Temp and standing read different files. |
| Desktop overlay or user secrets id `antiphon-server`; AppHost secrets do not forward these keys | True. `server/Antiphon.Server.csproj` secrets id is `antiphon-server`. Ignored `server/appsettings.Development.json` is the reloadable file. `Delegation__WorkspaceSlotRoles` is not reloaded and would shadow the file. |
| Production restart is `restart-apphost.ps1` | True until the CARD-1744 window. `docs/docker-stack.md:1732-1736` says that window has not been recorded. |
| Retention `MinTerminalHours=24` is a desktop user secret | Not in git. Tracked JSON is 12 (`server/appsettings.json:250`). The tick log records the effective `minTerminalHours` (`RunnerWorktreeRetentionService.cs:604-608`). This plan does not change either value. |
| `GET /sessions/workspace-slots` lists the ledger | Not a server route. Seat route `GET /api/session-runners/{id}/slots` is session seats. The ledger is the file `/work/workspace-slots/ledger.json` on the runner. Record-release is `POST /api/session-runners/{runnerId}/workspace-slots/{slotId}/record-release` (operator token). |
| A decision event proves a lease | The event is written before the RPC. The lease proof is the task row plus the ledger row plus a later release. |
| Workers can create the canary | No. A worker token gets 403. The orchestrator creates every canary. |

## Decisions

D-1. No product change. Rejected: flipping the Compose interpolation default to true (every undeclared deploy would enable slots), and setting `WorkspaceSlotsEnabled` in tracked `appsettings.json` (every server that loads that file would lease before a temp canary exists).

D-2. Temp file first. `secrets/stack.temp.env` becomes `true` before `deploy-temp`. `secrets/stack.env` stays false until the temp slot proof has passed and `drain-old` has reached zero counters, and it is set immediately before `redeploy-old`. Rejected: writing both files first (a bad temp canary would have no legacy standing runner), and enabling standing only (skips the canary host).

D-3. Server gate opens at `WorkspaceSlotsEnabled=true` with `WorkspaceSlotRoles=none`, one AppHost restart, then the role string widens without a restart: `Plan`, then `Investigate,TestDesign,Docs`, then `*`. Rejected: starting at `*` (every eligible role leases before one Plan proof), putting the role string in `Delegation__WorkspaceSlotRoles` (not reloaded; shadows the file), and enabling RefOnlyDesktop in the same restart.

D-4. Rollback keeps both flags on and sets `WorkspaceSlotRoles=none` until accepted leases release, then sets the runner assignment false and recreates that runner, then restarts the server with the server flag false. Rejected: turning the server flag off first (`WorkspaceSlotReleaseHostedService` stops, so leased slots are not released by the reconciler).

D-5. Retention stays an untracked desktop overlay. Witness the effective `minTerminalHours` from the tick log. Do not commit 24 into `server/appsettings.json`. Rejected: a tracked edit, which would change every host that has no overlay and is not required to turn slots on.

D-6. CARD-1750 is the operator authorization for exactly four writes: the temp assignment, the standing assignment, `Delegation:WorkspaceSlotsEnabled`, and `Delegation:WorkspaceSlotRoles` along the D-3 sequence, plus the D-4 rollback of those same keys. Named rolling phases, the one AppHost restart after those server keys are set, smoke, cache checks, and `retire-temp` preview-then-apply stay inside the existing autonomy grant (`docs/orchestration-loop.md:19-37`). Every other setting, budget, route pin, Reset, prune, and main state/cache recycle stays operator-only (`:80-89`).

D-7. The slot proof is one ordinary Worktree Plan the orchestrator creates, kind Grok (operator 2026-11-09), pinned to `server2-temp` before `drain-old`, and a second one pinned to `server2` after standing smoke and before `WorkspaceSlotRoles=*`. The existing gate-2 creator-made Custom canary stays the rollout's runner smoke and is expected to be a legacy mirror while roles are `none`. Rejected: treating that Custom canary as the slot proof, and pinning any new work to `server2-temp` after it retires.

D-8. Do not force a failing-slot recycle. A clean canary has an empty `quarantinedDirectories` list. If a reset fails on its own, the pass condition is a re-home of that slot, a published or retained quarantine, and the task still finishing on the new directory without a `task-<8hex>` mirror. A deliberate break of a slot directory is operator-only. Rejected: injecting a fault on the production volume to exercise re-home.

D-9. Build-slot settings stay at compose lines 70-72. Overflow adds disk, not build concurrency. The canary does not have to compile. If it does, it uses `scripts/build-slot.ps1` and the native `bin/` of the slot. Rejected: raising `MaxConcurrent` because the workspace pool grew.

## Operational checklist

Run this only after S2 has landed and the deployed SHA is the rc tip being rolled out. One phase per call. Never `-Phase all`. Never `-AllowWorktree`. Do not `cat` a stack env file or a user-secrets store: those files can hold other secrets. Change only the named key. A malformed assignment fails the phase with `RunnerWorkspaceSlotsSettingInvalid` before a side effect; stop there.

`<sha>` is the full rc tip. Record `GET /api/version` and `GET /api/runner-defaults` before gate 1. Do not PUT runner defaults. Keep dispatching under `docs/docker-stack.md` Continuous dispatch. The in-flight target stays 6. New unpinned work goes to `server2-temp` until `redeploy-old` exits 0 and standing smoke plus Both have passed, then to `server2`.

| Step | Action | Who |
|---|---|---|
| 0 | `check-census`, then `check-host-jq`. `provision-host-jq` only on `HostJqMissing`. Fixture and Inventory cache receipts. Seed only when the documented empty-volume path says so. | Autonomy |
| 1 | Set `PhoneHome__WorkspaceSlots__Enabled=true` in `secrets/stack.temp.env` only. Standing file unchanged (missing or `false`). | CARD-1750 write |
| 2 | `deploy-temp`. Before and after, the standing container id and start time are identical. | Autonomy |
| 3 | Smoke `server2-temp` (status block in the CARD-0934 table). Gate-2 Custom canary. It must not be required to lease a slot. | Autonomy |
| 4 | `GET /api/session-runners`: `server2-temp` `features` contains `workspaceSlotV1`; `server2` does not. | Autonomy, read |
| 5 | Merge server overlay `WorkspaceSlotsEnabled=true` and `WorkspaceSlotRoles=none` without dropping other keys. Restart from the main checkout at `<sha>`: `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <sha>`. Confirm `GET /api/version` SHA equals `<sha>`. | CARD-1750 write, then autonomy restart. Desktop control plane |
| 6 | Set `WorkspaceSlotRoles=Plan` in that same reloadable JSON. No restart. No `Delegation__WorkspaceSlotRoles` variable. | CARD-1750 write |
| 7 | Orchestrator creates one ordinary Worktree Plan, kind Grok, `-Runner server2-temp`, on an existing card, with an existing sanctioned brief. Pass the checks in the next section. Wait until it has released. | Autonomy, one sanctioned canary |
| 8 | Set `WorkspaceSlotRoles=Investigate,TestDesign,Docs`. | CARD-1750 write |
| 9 | `drain-old -WaitIdleMinutes 240`. Proceed only when `sessions`, `runnerSessions` and `queuedTasks` are zero. The four-hour seat stop is the existing CARD-0934 exception, server2 only. | Autonomy |
| 10 | Set `PhoneHome__WorkspaceSlots__Enabled=true` in `secrets/stack.env`. | CARD-1750 write |
| 11 | `redeploy-old`. Then standing smoke and `verify-card0849-caches.ps1 -Case Both -Sha <sha>`. `server2` `features` contains `workspaceSlotV1`. | Autonomy |
| 12 | One ordinary Worktree Plan, kind Grok, `-Runner server2`, same proof as step 7. | Autonomy, one sanctioned canary |
| 13 | Set `WorkspaceSlotRoles=*`. | CARD-1750 write |
| 14 | `drain-temp -WaitIdleMinutes 240`. | Autonomy |
| 15 | `retire-temp -DryRun`, then the enabled apply with that preview's token inside 900 s. | Autonomy |

Retention, beside the rollout, not a substitute for a slot proof: on the desktop, read the tick log line `minTerminalHours` for `antiphon:runner-worktree-retention`. Record one executing run id and follow every page of `GET /api/agent-tasks/worktree-residue/runs/{runId}?page=`. The preview route is DryRun and is not proof the job ran. If the logged floor is not 24, stop and report; do not edit tracked JSON. Slot directories are not retention candidates. `server2-temp` is not in `Runners`.

### Canary checks (steps 7 and 12)

Pass all of these. A miss stops the rollout before the next phase. Leave the accepting runner accepting.

- Task event detail `workspace_slot_decision: slot preparation=<guid> reason=listed`.
- `AgentTasks` row: `RunnerId` is the pinned runner, `RemoteWorkspaceSlotId` is `ws-NN`, `RemoteWorkspaceSlotGeneration` matches the ledger, `RemoteWorktreePath` is `/work/worktrees/ws-NN` or a re-homed `/work/worktrees/ws-NN-dK`. Read with the operator's existing SQL path. Do not print other columns.
- On that runner, `/work/workspace-slots/ledger.json` shows the same task, preparation, slot id, `leaseGeneration`, path and `Leased`, then `Free` after release. `RemoteWorktreePath` is null only after the release reply. Slot id and generation remain.
- The path is not `task-<8hex>`.
- `df` of `/work` before the lease and after release. Stop the widen if free space is below the pool floor in `RunnerWorkspaceSlotPool.DiskAllowsOverflow` (20 GiB or a tenth of the volume, plus 6 GiB for two 3 GiB estimates). Do not pre-create overflow slots.
- Build-slot env on the recreated container is still `MaxConcurrent` 4, `MaxCpuCount` 6, `MinAvailableMemoryMb` 16384. A build inside the canary, if any, is a normal `scripts/build-slot.ps1` grant. The lease itself does not take a build slot.
- `quarantinedDirectories` is empty. Do not force a failing-slot recycle. A natural failure passes only under D-8.
- After release, no live seat, no Pending or Held sync debt, no live follow-up. A Blocked canary keeps the slot: parking is off, so there is no automatic release deadline (CARD-1083). Do not use a Blocked task as the proof.

### Rollback

| Where you are | Action |
|---|---|
| Temp failed before `drain-old` | Leave standing accepting. Set roles `none` if the server flag is already on. Do not drain standing. Temp stays for diagnosis. |
| After `drain-old`, standing still healthy | `runner-drain.ps1 clear -RunnerId server2` only after standing is verified healthy. |
| Standing smoke failed after `redeploy-old` | Drain standing to accepting temp: `runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp`. |
| Leases exist | `WorkspaceSlotRoles=none` until each accepted lease is `released`, `already_free` or `quarantined`. Then set that runner's saved flag to `false` and recreate it through the named phase. Then restart the server with `WorkspaceSlotsEnabled=false`. |
| `retire-temp` returns `RecycleUnpublishedWork` or `RecycleWorktreeDirty` | Stop and report. Do not delete the directory. |
| `RecycleGitAuditUnknown reason=registry-missing` | Stop and report. Only the operator writes `/home/mc/antiphon-server2/retirement-origins.json`. |
| Any other retire refusal | One fresh preview and apply, then a Debug task. |

`record-release` is the operator route when the runner answer is `slot_unknown` three times (`workspace_slot_release_stuck`). It is not a canary shortcut.

## Migrations

None. No schema, no DTO, no route, no operation label, no remote-script function, no statement budget. `tests/impacted-classes.json` has no row for `docs/docker-stack.md` or `DockerStackDocumentationTests`. Impacted-classes table: none.

## Slices

S1 (about 60 minutes). Add `Card_1750_slot_activation_order_is_documented` to `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`, `[Category("Unit")]` already on the class. The method reads `docs/docker-stack.md` through the class's existing `Read` helper. It fails on this tree because the heading is absent. Commit that failing test alone. Do not add the heading in S1.

S2 (about 90 minutes). Insert the section below into `docs/docker-stack.md` immediately after the paragraph that ends with `reason=roles_none` (current lines 802-808) and immediately before `### Runner worktree retention (CARD-1677)`. Change no other file. The S1 test goes green. CP-3 re-runs the two existing contract methods so the tracked default is still false.

### Doc contract text (paste in S2)

The first occurrence of each pinned phrase is in this order. Later repeats are allowed.
`secrets/stack.env` is not a substring of `secrets/stack.temp.env`. `slot proof on standing server2`
is not a substring of `slot proof on server2-temp`. The token `WorkspaceSlotRoles=*` appears only
in the last proof sentence, so a widen list must not spell that token earlier.

```markdown
### Per-slot worktree activation (CARD-1750)

Turn the shipped flags on during the next rc rollout. Tracked default stays false:
`docker-compose.server2-runner.yml` interpolates `${PhoneHome__WorkspaceSlots__Enabled:-false}`,
and `Delegation:WorkspaceSlotsEnabled` defaults to false.

Set `PhoneHome__WorkspaceSlots__Enabled=true` in `secrets/stack.temp.env` before
`-Phase deploy-temp`. Set the same assignment in `secrets/stack.env` only after the temp
slot proof and zero drain-old counters, immediately before `-Phase redeploy-old`.
Each saved assignment is exactly `true` or `false`, once, LF. A bad value refuses
`RunnerWorkspaceSlotsSettingInvalid` before a side effect.

Restart the desktop production server once with `scripts/restart-apphost.ps1
-ExpectedServerSha <sha>` after merging `WorkspaceSlotsEnabled=true` and
`WorkspaceSlotRoles=none`. This remains the production restart until the CARD-1744 window.
Do not use restart-linux-server.ps1 for this activation until that window is recorded.
Change roles only in the ignored server `appsettings.Development.json` (or user secrets id
`antiphon-server` for the restart-bound flag). Never set `Delegation__WorkspaceSlotRoles`.

Then set `WorkspaceSlotRoles=Plan`. Run the slot proof on server2-temp.
Then set `Investigate,TestDesign,Docs`. Mutation is never listed.
SourceLanding never leases. The gate-2 Custom canary is a legacy mirror while roles are none.

Run `-Phase drain-old` only after that temp proof. After standing is recreated, run the
slot proof on standing server2 before `WorkspaceSlotRoles=*`.

A warm reset keeps native build output. Enabling the flags does no default cold reset.
`SessionRunner__BuildSlots__MaxConcurrent` stays `4`. A slot lease, reset, re-home or
overflow does not take a build slot.

Rollback is roles none while leases drain, then both flags false.
`retire-temp` stops on `RecycleUnpublishedWork` and on `RecycleWorktreeDirty`.
`RecycleGitAuditUnknown reason=registry-missing` is operator-only.
Do not force a failing-slot recycle. A natural failure must re-home that slot.
```

The S1 test slices from `### Per-slot worktree activation (CARD-1750)` until the next line
that starts with `### `, then asserts this first-index order:

1. `secrets/stack.temp.env` before `secrets/stack.env`
2. `WorkspaceSlotRoles=none` before `WorkspaceSlotRoles=Plan`
3. `WorkspaceSlotRoles=Plan` before `slot proof on server2-temp`
4. `slot proof on server2-temp` before `Investigate,TestDesign,Docs`
5. `Investigate,TestDesign,Docs` before `-Phase drain-old`
6. `-Phase drain-old` before `slot proof on standing server2`
7. `slot proof on standing server2` before `WorkspaceSlotRoles=*`

The slice contains `Mutation is never listed`, `SourceLanding never leases`,
`RecycleUnpublishedWork`, `RecycleWorktreeDirty`, `RecycleGitAuditUnknown reason=registry-missing`,
`restart-apphost.ps1`, `until the CARD-1744 window`, `tracked default stays false`,
`${PhoneHome__WorkspaceSlots__Enabled:-false}`, `no default cold reset`, `native build output`,
`SessionRunner__BuildSlots__MaxConcurrent`, `does not take a build slot`, and
`Do not force a failing-slot recycle`.

The slice does not contain `tracked default becomes true`, `Mutation,`, `,Mutation`, or
`restart-linux-server.ps1 is the production restart for this activation`.
The rollback sentence says `roles none`, not `WorkspaceSlotRoles=none`, so the first `none`
token stays the restart gate.

## Verification design

No session that waits for input is added or changed. An existing Blocked task still keeps a leased slot with no automatic deadline while parking is off.

V-1. The new documentation method fails while the heading is absent (S1) and passes when the pasted section is the slice (S2). It is red-first: the assertion is `IndexOf` of a heading this tree does not have, so a test that only checks a constant cannot go green at S1.

R-1. `DockerStackContractTests.Server2_workspace_slots_are_opt_in_and_temp_inherits_the_environment` and `Server2_workspace_slot_setting_survives_named_deployment_phases` still pass at S2. They lock the false interpolation default and the saved-assignment reader. S2 must not touch those sources.

Mutants, method-scoped, pending SourceLanding Mutation. Each one is a one-line edit of the pasted section, then `/*/*/DockerStackDocumentationTests/Card_1750_slot_activation_order_is_documented*`, then restore. Zero tests or a build error is not red.

| Id | Defect | Witness |
|---|---|---|
| PC-1 | Put `secrets/stack.env` ahead of `secrets/stack.temp.env` | V-1 order |
| PC-2 | Delete `WorkspaceSlotRoles=none` or move `WorkspaceSlotRoles=*` ahead of `Plan` | V-1 order |
| PC-3 | Delete `Mutation is never listed`, or insert `Mutation,` into the widen list | V-1 contain / forbidden |
| PC-4 | Delete `RecycleUnpublishedWork` or `RecycleWorktreeDirty` | V-1 contain |
| PC-5 | Replace the CARD-1744 sentence with `restart-linux-server.ps1 is the production restart for this activation` | V-1 contain and forbidden |
| PC-6 | Delete `tracked default stays false` or the `${PhoneHome__WorkspaceSlots__Enabled:-false}` token | V-1 contain |
| PC-7 | Delete `no default cold reset` | V-1 contain |
| PC-8 | Delete `does not take a build slot` | V-1 contain |
| PC-9 | Move `slot proof on standing server2` ahead of `-Phase drain-old` | V-1 order |

## Performance budget

Applies `docs/test-performance-standards.md` and `docs/testing-and-build.md` performance standards.

- R1. No row is budgeted over 30 minutes. No Debug task.
- R2. The witness stays in `Antiphon.Tests` Unit beside the other doc pins. A new project would be larger than the pin.
- R3. No AppHost, database, container, or migration. Each test reads one file. Nothing to share.
- R4. The checkpoint tool records build, slot wait and execution separately (CARD-1531). These tests have no database, HTTP, or CLI phase. Record those phases unknown. Keep the first green receipt.
- R5. Estimates are provisional. There is no measured p95 for this new method. The collection sample is the first green CP-2 receipt (build seconds and test-body seconds, separate from slot wait). Do not widen a timeout from that single sample. The importer's 15-minute floor and 3× estimate are not this policy (CARD-1720). No `totalTimeoutOverrideReason`.
- R6. No external call.

`EstimatedMinutes` sum is 18. Derived execution allowance is `max(30, 2×18+10) = 46` minutes. The table sets `totalTimeoutMinutes: 50`. Repeat proof: none.

### Checkpoints

lane: Unit
totalTimeoutMinutes: 50

CP-1 runs at the S1 commit and is expected to fail one assertion. Keep that receipt failed.
CP-2 and CP-3 run at the S2 commit. CP-3 reuses CP-2's build. No other build or test is listed.
Bootstrap of `tools/Antiphon.Checkpoints` is the tool's own launch build, not a row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1750-s1/ | red-activation-pin | `/*/*/DockerStackDocumentationTests/Card_1750_slot_activation_order_is_documented*` | V-1 | 1 executed, 1 failed, heading absent | 1 | 8 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1750-s2/ | green-activation-pin | `/*/*/DockerStackDocumentationTests/Card_1750_slot_activation_order_is_documented*` | V-1 | all listed, 0 failed | 1 | 8 |
| CP-3 | S2 | CP-2 | slot-default-contracts | `/*/*/DockerStackContractTests/(Server2_workspace_slots_are_opt_in_and_temp_inherits_the_environment*)\|(Server2_workspace_slot_setting_survives_named_deployment_phases*)` | R-1 | all listed, 0 failed | 2 | 2 |

## Risks

| Risk | What to do |
|---|---|
| Server flag on before any runner advertises the feature | Decisions stay `ineligible` and no event is written. Step 4 must pass before step 6. |
| Role string set through the environment | The file edit does nothing. Step 6 forbids `Delegation__WorkspaceSlotRoles`. Confirm with the Plan decision's `reason=listed`, not with a healthy health check. |
| `*` before the standing proof | Step 13 is after step 12. PC-2 locks that order in the doc. |
| Quarantine holds unpublished work into `retire-temp` | Stop on `RecycleUnpublishedWork` / `RecycleWorktreeDirty`. Do not `git worktree remove` by hand. |
| Disk floor trips on the first overflow | Stop the widen. Do not raise `PoolDiskBudgetGiB` under this card. |
| Retention deletes a slot | It does not select slot-bound tasks or `ws-*` paths. If a run row names a `ws-*` path, stop. |
| Someone treats the 2026-11-06 transport gap as still open | It is closed. S2 does not edit `c590-remote.sh` or the Compose files. |
| Desktop restart from a worktree or a stale checkout | Exit 3 or exit 5. Run from the main checkout at `<sha>`. This plan's agent runs nothing on the desktop. |

Windows owed: the AppHost restart, the ignored overlay or `antiphon-server` user-secrets edit, and the retention log read. They are the desktop control plane. No checkpoint row runs there. Nothing in S1 or S2 is Windows-only.
