# 2026-11-17 server2 workspace-slot pool degraded (`workspace_slot_pool_degraded`)

Debug task 23362b22. Times are UTC. No secrets or tokens were read or printed.

## Outcome

- **No quarantine was released.** No caller exists for the release operation.
  `WorkspaceSlotQuarantineRelease` (phone-home op 44) is handled on the runner
  (`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:488`,
  `RunnerWorkspaceSlotPool.cs:942`). Nothing on the server sends it: no route under
  `server/Api/Endpoints/SessionRunnerEndpoints.cs` (only `cold-reset` at :153 and
  `{slotId}/record-release` at :181), and no method on `PhoneHomeRunnerClient`. The runner
  has no HTTP route for it either; `docs/ops-http.md:428` says "no HTTP route on the runner yet". The same
  holds on `origin/master` 321f98ec9. `scripts/workspace-slots.ps1` has only `cold-reset`. The
  route named in the brief, `POST /sessions/workspace-slots/quarantine/{dirId}/release`, does
  not exist. Two other routes are forbidden: hand removal (docs/docker-stack.md:931, "Never
  remove one by hand") and ledger edits (brief).
- At 01:25Z all five entries were still open (count 5 > `MaxQuarantinedDirectories` 4), and all
  four queued tasks were still Queued. The server log showed `workspace_slot_pool_degraded` on
  every retry, 19 lines by 01:21Z, with backoff growing to 8 minutes.
- Every entry meets the runner's own `Pushed` checks (see below), so each would be released as
  `Pushed` once a caller exists.

## What was read

- Ledger `/work/workspace-slots/ledger.json`, read in the `antiphon-runner-session-runner-1`
  container at 01:13Z. No writes.
- `docs/docker-stack.md:925-940`, `docs/ops-http.md:420-431`.
- `PhoneHomeCommandDispatcher.cs:470-500`.
- `RunnerWorkspaceSlotPool.cs` lines 86-110 (the lease refusal), 370-460 (`SettleAsync`),
  492-515 (`ResetForFreeAsync`), 618-660 (`PreflightFreeAsync`, `CheckFreeAsync`), 692-730
  (`UnpushedAsync`, `DirtAsync`), 815-840 (`QuarantineAndRehomeAsync`) and 942-1010
  (`ReleaseQuarantineAsync`).
- `src/Antiphon.SessionRunner.Contracts/WorkspaceSlotContracts.cs:97-135`
  (`WorkspaceSlotResetPolicy`), `PhoneHomeWorkspaceSlotSettings.cs`, and
  `tools/Antiphon.Checkpoints/BuildConfinement.cs:508-540`.

### Open quarantines (01:13Z)

| dirId | path | slot | class/reason | branch | head | GiB |
|---|---|---|---|---|---|---|
| 66ece2d86b23 | /work/worktrees/ws-01 | ws-01 | filesystem/reset: `!! .antiphon/control-window-2b159393/` | feat/card-task-2b159393 | 202970c671c0 | 2.46 |
| c139d63622e5 | /work/worktrees/ws-04 | ws-04 | custody/lingering: pid=2840 sleep cwd | feat/card-task-cd9b15c9 | 84d7ccc9ece4 | 2.28 |
| 7ef5203b564f | /work/worktrees/ws-03 | ws-03 | filesystem/reset: `!! tests/Antiphon.SessionRunner.Tests/bin-c1893-r92c-cp2/`, `!! tests/Antiphon.Tests/bin-c1893-r92c-guards/` | feat/card-task-92c62298 | 87a37c7c314e | 0.42 |
| 2d0ef6e62dae | /work/worktrees/ws-01-d2 | ws-01 | custody/lingering: pid=5898 sleep cwd | feat/card-task-a0639c99 | f638432925f3 | 2.28 |
| 7b3f3b265073 | /work/worktrees/ws-01-d3 | ws-01 | filesystem/reset: `!! tests/Antiphon.Tests/bin-c1958-final-b/`, `bin-c1958-final/`, `bin-c1958-red/` | feat/card-task-e2208352 | 321f98ec990a | 0.46 |

All five are `locked: true` and `isOpen: true`. No quarantined path is a live slot path. The
live slots are ws-01-d4 Free, ws-02 Leased (c6c85b5b), ws-03-d2 Free, ws-04-d2 Leased
(d0ae093a), and ws-05, ws-06 and ws-07 Free. There is spare slot capacity; only the
open-quarantine count refuses (`RunnerWorkspaceSlotPool.cs:95`: `> Settings.MaxQuarantinedDirectories`).

### Pushed checks run without changing anything (01:15-01:18Z)

- **Origin.** Each head is on origin. cd9b15c9 and a0639c99 equal their branch tips.
  2b159393 is an ancestor of its branch tip. The heads of 92c62298 and e2208352 are not
  ancestors of their rebased branch tips, but both are on `origin/master`. The runner checks
  `--remotes=origin`, so the master copy counts.
- **Runner-equivalent `UnpushedAsync`.** In the container, `rev-list --max-count=1 <HEAD reflog + HEAD> --not --remotes=origin`
  printed nothing for all five. This was run without a fetch, which is the stricter form.
- **Runner-equivalent `DirtAsync`.** `status --porcelain --untracked-files=all` (ignored files
  excluded) printed nothing for all five. The only residue is ignored build or control output.
- **Custody holders.** pid 2840 and pid 5898 no longer exist in the container. No process has its
  cwd under any quarantined path. The owning tasks, cd9b15c9 (Review retire-temp fence) and
  a0639c99 (Review CARD-1956), are Succeeded. Nothing was killed because there was nothing to
  kill. The quarantines stay open because only `ReleaseQuarantineAsync` sets `ClosedAt`, and
  nothing re-checks an open entry.
- **Note.** The ws-01-d2 residue includes `.antiphon/c1956-review/`, which is ignored. A
  `Pushed` release removes it. If the Review's evidence is wanted, copy it first.

## Structural cause: why a custom `bin-<name>/` output quarantines a slot

1. **Release order.** Release (`SettleAsync`) runs `ResetForFreeAsync`. That runs
   `git clean -ffdx -e **/obj/ -e **/bin/ -e **/node_modules/ -e /client/dist/`
   (`WorkspaceSlotContracts.cs:100,107-120`; `RunnerWorkspaceSlotPool.cs:505`). Release then
   runs `CheckFreeAsync` (`RunnerWorkspaceSlotPool.cs:645-650`). That check lists
   `status --ignored=matching` and treats an `!!` entry as keep-list only when one of its path
   segments is literally `obj`, `bin` or `node_modules` (`IsKeepListPath`, `WorkspaceSlotContracts.cs:123-135`).
2. **Clean leaves the parent behind.** `git clean`'s `-e` exclusion protects a matching
   directory at any depth. So the clean deletes everything in `bin-c1958-final-b/` except the
   nested `artifacts/obj/` (seen in the container) and leaves the parent directory in place.
   Every residue in this incident has such a nested keep-list name:
   - `bin-c1958-*/artifacts/obj/`
   - `bin-c1893-r92c-*/artifacts/obj/`
   - `.antiphon/control-window-2b159393/coordinator-*/bin/ssh`, a fake-ssh fixture
3. **The check rejects the parent.** `git status --ignored=matching` reports the topmost
   ignored directory: `!! tests/Antiphon.Tests/bin-c1958-final-b/` (ignored by `.gitignore:48 bin-*/`).
   Its segments are `bin-c1958-final-b`, not `bin`, so `CheckFreeAsync` returns "paths outside
   the keep-list".
4. **The retry cannot help.** The one retry (`RunnerWorkspaceSlotPool.cs:438-443`) runs the
   same clean, which cannot remove the parent either. The second failure is quarantined as
   `filesystem/reset` with `residue: ...`, and the slot is re-homed.

The policy disagrees with itself. The warm keep-list is meant to keep only native `obj/` and
`bin/`, and `IsKeepListPath` agrees that `bin-x/` is not keep-list. But `git clean -e **/obj/`
also protects an `obj/` nested inside `bin-x/`, and its parent with it.

### Why it will recur now that `WorkspaceSlotRoles` is `*`

- **The checkpoint tool makes this layout on every run.** It is mandatory for Code checkpoint
  runs (CARD-0723). `tools/Antiphon.Checkpoints/BuildConfinement.cs:508-540` rewrites
  `--property:OutputPath=bin-<row>/` into an owned absolute output and adds `ArtifactsPath` and
  `UseArtifactsOutput=true`. The result is `bin-<row>/artifacts/obj/...`, which is exactly
  the residue in this incident.
- **Many tasks are affected.** Any Code, Debug, Mutation or Review task that runs a checkpoint
  build, or a `run-checkpoint.ps1 -OutputPath bin-x/` build, in a slot will quarantine that
  slot at release. Test fixtures that write a `bin/` under an ignored control directory do the
  same (`.antiphon/control-window-*`).
- **Observed rate.** Three filesystem quarantines in about 9 hours, at 16:00, 21:49 and 01:05.
  This was in the first day of `*`, with roughly 7 tasks in flight on server2. Expect several
  per day at that load.
- **Custody quarantines add to the count.** A lingering `sleep` left by a delegate's
  background command causes one. There were two in this incident, and they never close on
  their own.
- **How the pool fails.** With the limit at 4, the pool degrades for every role at the fifth
  open entry, and the only exit has no caller.
- **The slot-retirement cap compounds it.** ws-01 has re-homed 3 times in 24 hours (16:00,
  22:02, 01:05). One more quarantine on ws-01 before about 16:00Z retires the slot
  (`RunnerWorkspaceSlotPool.cs:826`, `MaxRehomesPerSlotPerDay` 3).

## Options

### Unblock now (decision for the caller or operator)

1. **Soft rollback of slot leasing.** Set `Delegation:WorkspaceSlotRoles` to `none` in
   `C:\src\Antiphon\server\appsettings.Development.json`. It is `"*"` at line 4, and AppHost
   hot-reloads it when no `Delegation__WorkspaceSlotRoles` environment variable shadows it
   (docs/docker-stack.md:942-947). Queued tasks then fall back to legacy `task-<id>` mirrors
   on their next retry, with no restart. Confirm with `WorkspaceSlotDecision reason=roles_none`.
   This reverses the operator's 2026-11-16 "use workslots for everything" until a release
   caller exists. It is fastest and reversible.
2. **Raise the runner limit.** Set `PhoneHome__WorkspaceSlots__MaxQuarantinedDirectories`, for
   example to 10, and recreate the runner through its named phase. This disrupts the leased
   sessions on ws-02 and ws-04 and only delays the next refusal.
3. **Ship a release caller and use it.** See durable option A below. This is the correct
   long-term path but needs Code, Review, rc and a restart before the pool opens.

### Durable fixes, ranked (not implemented)

**A. Add the missing operator release route. Required in any case.**

- Add `POST /api/session-runners/{runnerId}/workspace-slots/quarantine/{dirId}/release`.
  It takes body `{disposition, reason, requester}` and the operator token, and is modelled on
  the cold-reset route at `server/Api/Endpoints/SessionRunnerEndpoints.cs:153-175`.
- It sends `PhoneHomeOperation.WorkspaceSlotQuarantineRelease` through a new
  `PhoneHomeRunnerClient.ReleaseWorkspaceSlotQuarantineAsync`. The timeout is already mapped
  at `PhoneHomeLiveConnection.cs:357`.
- Add a `release` verb to `scripts/workspace-slots.ps1`, and correct `docs/ops-http.md:428`.
- Without this, no quarantine can ever close, so the pool degrades permanently after any 5
  quarantines.

**B. Make the warm reset remove non-keep-list ignored directories. Safest fix for the cause.**

- In `ResetForFreeAsync` (`RunnerWorkspaceSlotPool.cs:505-507`), after the warm clean, list
  `status --porcelain -z --ignored=matching`.
- For each `!!` entry that `IsKeepListPath` rejects, run a path-scoped cold clean
  `git clean -ffdx -- <entry>` with no `-e`. Add it as
  `WorkspaceSlotResetPolicy.ScopedCleanArguments(path)` in `WorkspaceSlotContracts.cs:107`.
- Why it is safe:
  - It only touches entries git already classes as ignored. `??` untracked files and tracked
    dirt are still refused earlier by `DirtAsync` (`RunnerWorkspaceSlotPool.cs:417`).
  - It runs after the ownership proof and the unpushed check.
  - It brings the reset into line with the decision `CheckFreeAsync` already makes.
- One test: a slot with `tests/X/bin-row/artifacts/obj/f` releases Free, and
  `tests/X/obj/f` survives.

**C. Re-check open quarantines automatically.** A maintenance sweep would close an open
entry, as `Removed` or `Pushed`, when it now passes the `Pushed` checks: no holder, no unpushed
commit, no dirt. This heals custody `lingering` entries whose `sleep` has exited, which B does
not cover. It is riskier than B because it deletes without an operator in the loop. Gate it to
holder-free, `Pushed`-clean entries only.

**D. Forbid custom outputs in slot briefs.** This does not work. The mandated checkpoint
tool and test fixtures create the nested layout themselves.

**E. Raise `MaxQuarantinedDirectories`** (`PhoneHomeWorkspaceSlotSettings.cs:24`). This is a
stopgap only. It costs disk (about 2.3-2.5 GiB per full slot, against a 64 GiB budget) and
postpones the same failure.

**Recommendation:** A first, because without it nothing can close. Then B, the smallest and
safest fix that removes the main recurrence. Add C for custody `lingering`.

## Timings

- 01:05:51 The fifth quarantine (7b3f3b265073) tips the pool into degraded.
- 01:13 Ledger read.
- 01:15-01:18 Origin and container checks.
- 01:20 Release-caller search finishes: there is no route.
- 01:21 The four tasks are confirmed Queued, with retries still refused.
