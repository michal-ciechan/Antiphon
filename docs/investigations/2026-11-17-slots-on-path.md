# 2026-11-17 Workspace slots back on for every role: state, payoff, what still blocks

Investigate task be9d04c4 (read-only). Times are UTC on 2026-11-17. No setting was changed, no quarantine
was released, nothing was restarted, and no token was read or printed. "UNOBSERVED" marks a fact this task
could not read.

**Sources, with exact SHAs**

- master `38c64361b903fadf9d75d92add00b44e36a51958` (= `origin/master` = `origin/rc/2026-11-25` = this
  branch's base). Every `file:line` below is at this SHA.
- Live server `GET /api/version` at 17:40:50Z: `38c64361b903fadf9d75d92add00b44e36a51958` (the AppHost had
  just restarted; the public origin returned 502 from 17:36 to 17:40).
- server2 runner container (`PhoneHome__RunnerId=server2`, the container this task runs in): ledger
  `/work/workspace-slots/ledger.json` read with `jq` at 17:36Z and 17:43Z; runner dll
  `InformationalVersion 1.0.0+9b98828eb34eb552ce62bcde5a64dd045a872a88`, which is the `rc/2026-11-23` tip and
  is also what `GET /api/session-runners/server2/status` reports as `buildVersion`.
- `GET /api/session-runners`, `.../status`, `GET /api/agent-tasks?projectId=<Antiphon>&since=2026-11-17T00:00:00Z`
  (109 tasks) and `GET /api/agent-tasks/{id}` for the 103 tasks dispatched today plus ab41aa31.
- The caller's refinement at 17:42:06Z, which changed the scope (see Outcome).

## Outcome

- **Slots are on and leasing.** The orchestrator released all six quarantines as `Pushed` from 17:41:23Z to
  17:41:38Z. The ledger now has `open 0`, and all six rows have `disposition=Pushed` and `outcome=removed: ...`.
  Roles are `*`. The first lease after that came at 17:45:14Z: Code task `ab41aa31` recorded
  `workspace_slot_decision: slot ... reason=all`, and the ledger shows `ws-01` Leased (`/work/worktrees/ws-01-d4`,
  generation 17, `feat/card-task-ab41aa31`).
- **The pool will degrade again unless someone releases quarantines.** The server2 runner is `9b98828eb`, which
  does not include CARD-1961 (`72d98d936d` is in `rc/2026-11-25` but not in `rc/2026-11-23` or `rc/2026-11-24`).
  Any slot task that runs a checkpoint build will be quarantined `filesystem/reset` when it releases. The pool
  refuses every new lease at the fifth open quarantine. Each quarantine now takes one release command (§3, §5).
- **1961 can reach server2 only through the held rollout.** There is no runner-only recreate (§2). The
  CARD-1893 §7.0 check prints `HOLD` for `rc/2026-11-25`.
- **The desktop cannot use slots.** Slots exist only on phone-home runners (§4).
- **What slots save is disk and Git registrations, not build or start time** (§6). On server2, preparing a
  workspace takes about 1.1 s with a slot and about 2.8 s without (p50).

## 1. State per runner

| | desktop | server2 | server2-temp |
|---|---|---|---|
| Runner slot pool | **None.** It is not a phone-home runner, and `workspaceSlotV1` is absent from its 19 features. Only `PhoneHomeCommandDispatcher` adds that feature (`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:168-172`). The release route answers `409 workspace_slot_runner_not_phone_home` (docs/ops-http.md:435-436). | **On.** The container env has `PhoneHome__WorkspaceSlots__Enabled=true`, and `workspaceSlotV1` is advertised. The compose file passes only that key (`docker-compose.server2-runner.yml:65`). | **Retired** (`retiredAt 2026-11-16T15:10:01Z`, `available=false`, no features, no container). Its `secrets/stack.temp.env` flag is UNOBSERVED from here. docs/docker-stack.md:1091-1093 says it is `true` and takes effect at the next `deploy-temp`. |
| Server role setting | Not consulted. `ResolveSlotDecisionAsync` returns `ineligible` unless the runner advertises `workspaceSlotV1` (`server/Application/Services/RemoteWorkspaceService.cs:247-251`). | `Delegation:WorkspaceSlotsEnabled` is restart-only, and `Delegation:WorkspaceSlotRoles` hot-reloads on every decision (`server/Application/Services/WorkspaceSlotRoleGate.cs:8-14`). `*` means every role except Mutation (`server/Application/Settings/WorkspaceSlotRoles.cs:5-7`); SourceLanding never leases (docs/docker-stack.md:1089-1090). | Same as server2 once it is deployed. |
| Ledger at 17:43Z | n/a | 7 slots: ws-01-d4 (leased by ab41aa31 at 17:45Z), ws-02-d2, ws-03-d2, ws-04-d2, ws-05, ws-06, ws-07. **0 open quarantines** of 6 recorded. | n/a |
| Tasks dispatched today (00:00-17:45Z) | 5, all local `C:\Antiphon\worktrees\card-task-*` | 98: **10 in slots** (00:00-01:05Z, `reason=all`), **88 legacy mirrors** (`reason=roles_none`), then ab41aa31 in a slot at 17:45Z | 0 |

Slot tasks today: 9b9b24c8 Merge, 24cbd9e1 Plan, 82a03c61 Review, 31f0501d Plan, a1d414b6 Plan, 5f0633e8 Review,
c913e8aa Review, c3619b22 Review, c1c28227 Investigate, d0ae093a Merge. Four tasks were held on
`workspace_slot_pool_degraded` 22 times in total from 01:06 to 01:24Z: e96c2ae3 (6), 2e74cf4e (6), 83d30eec (5)
and e7394f29 (5). They started on legacy mirrors between 01:27 and 01:39Z, 17 to 33 minutes late.

## 2. Sequence: what is done, what is next, and who can do it

| # | Step | Status / command | Who |
|---|---|---|---|
| S-1 | AppHost on an rc that has CARD-1960 | **Done.** `/api/version` = `38c64361b` at 17:40:50Z | orchestrator (autonomy) |
| S-2 | Release the open quarantines | **Done**, 17:41:23-17:41:38Z, all six `Pushed` | orchestrator |
| S-3 | `Delegation:WorkspaceSlotRoles` = `*` | **Done** (caller's report). Confirmed by ab41aa31 `reason=all` at 17:45:14Z | orchestrator |
| S-4 | Keep the open count at or below 4 until 1961 is live | Per quarantine: §3 holder check, then `pwsh -NoProfile -File scripts/workspace-slots.ps1 release -Runner server2 -DirId <12hex> -Disposition Pushed -Reason "<why>"`. Exit 0 means `removed`; exit 1 means `refused` or `removal_failed`, so stop and leave that entry. | orchestrator; operator token via the script (S-2 shows the orchestrator can do it) |
| S-5 | 1961 on server2 | Needs a recreate of the server2 runner from an rc that contains `72d98d936d`. **Held** (below). | orchestrator runs the phases once the hold is released |

**Is there a runner-only recreate outside the rolling rollout? No.** The runner is recreated in phase
`redeploy-old` (gate 6, docs/docker-stack.md:127). That phase refuses unless server2 is drained with
`redirectTo=server2-temp`, has zero counters and is not accepting, and unless server2-temp is accepting
(`scripts/deploy-server2.ps1:1408-1428`). So `deploy-temp` (gate 1) and `drain-old` (gate 4) must come first.
The doc forbids any other path: "do not force a phase or improvise a Docker replacement" (docs/docker-stack.md:9-10).
A stack-file edit alone does nothing until a named phase recreates the container (docs/docker-stack.md:1054-1055).
The CARD-1893 §7.0 hold covers both `deploy-temp` and `redeploy-old` for any SHA that contains `9cf3e4917`
unless the compose files carry D-4/D-5 (`docs/superpowers/plans/2026-11-16-card-1893-activation-plan.md:176`).
The mechanical check on `rc/2026-11-25` printed **`HOLD`**: master has no `HistoryPath` line in
`docker-compose.server2-runner.yml`. On master, CARD-1893 S1/S2 have reached only test design (b919f55077,
round 2 clean). No S1 code is on master.

**What the held rollout needs from the operator (CARD-1893 §8).** Whether the operator has already answered
these is UNOBSERVED; neither the plan nor the card text records an answer.

| Q | Blocks the 1961 runner rollout? | Recommendation (the plan's) |
|---|---|---|
| Q-8 Ship the S0 stop-gap (`SessionRunner__HostStats__HistoryEnabled: "false"` on the base runner) to release the hold before S1 | **Yes. This is the fastest way to release the hold.** The plan says no, *"unless an unrelated runner fix must reach server2 first"*, and 1961 is exactly that fix. | **Operator decision.** Yes if the slots are wanted before the S1 Code, Review and port cycle. S0 still needs its own small Code, Review and rc port. |
| Q-9 Release the hold by a reviewed rc port of S1 rather than waiting for the next cut | Yes, on the S1 path | Yes, once S1 is Review clean |
| Q-1 Store the server2 binding in desktop user-secrets | Only for the S1 activation (step 5), not for the recreate itself | Yes |
| Q-2 Values `server2` / `server2-build-slots` / revision 1 / new epoch GUID | S1 content | Yes |
| Q-3 Turn off durable history on temp | S1 content (D-4); the hold check needs it in the temp compose | Yes |
| Q-4 Commission S2 (guarded `upgrade-broker`) | No (broker only) | Yes |
| Q-5 Swap waits up to 30 min for idle | No (broker only) | Yes |
| Q-6a/Q-6b Cursor-row delete in the pump-paused window | Only on branch P (E-4 shows a `server2` row) | Yes |
| Q-7 Pin the broker to the runner's rc SHA12 | No (broker only) | Yes |

## 3. The quarantines

All six were open at 17:36Z and released at 17:41Z. The checks this task ran before the release, all read-only:

- Unpushed: no fetch, `--no-optional-locks`, and the reflog, HEAD and branch tip compared against `--remotes=origin`.
- Dirt: `status --porcelain --untracked-files=all`.
- Holders: every `/proc/*/cwd` in the runner container.

All of them came back clean.

| dirId | slot | class/reason | branch | bytes | age at 17:36Z | unpushed | dirt | holder | ignored-only residue |
|---|---|---|---|---|---|---|---|---|---|
| 66ece2d86b23 | ws-01 | filesystem/reset | feat/card-task-2b159393 | 2,637,725,197 | 25.6 h | none | none | none | `.antiphon/control-window-2b159393/` |
| c139d63622e5 | ws-04 | custody/lingering (pid 2840 gone) | feat/card-task-cd9b15c9 | 2,443,602,459 | 21.7 h | none | none | none | `.antiphon/inbox/`, `.antiphon/rolling-server2/` (10 files) |
| 7ef5203b564f | ws-03 | filesystem/reset | feat/card-task-92c62298 | 456,123,772 | 19.8 h | none | none | none | `bin-c1893-r92c-*` |
| 2d0ef6e62dae | ws-01 | custody/lingering (pid 5898 gone) | feat/card-task-a0639c99 | 2,447,835,068 | 19.6 h | none | none | none | `.antiphon/c1956-review/` (51 files, 4.2 MiB), `rolling-server2/` |
| 7b3f3b265073 | ws-01 | filesystem/reset | feat/card-task-e2208352 | 493,259,913 | 16.5 h | none | none | none | `bin-c1958-*` |
| c3db3bb02698 | ws-02 | custody/lingering (pid 27648 gone) | feat/card-task-c6c85b5b | 2,446,433,632 | 15.1 h | none | none | none | `.antiphon/inbox/`, `rolling-server2/` |

The sixth entry, c3db3bb02698, was new since the incident note. It was quarantined at 02:28:23Z, after the
roles rollback, when ws-02's existing lease released with a lingering `sleep`.

**Dispositions** (`src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs:971-1013`):

- `Pushed` fetches the branch, refuses `commit ... is not on origin` and refuses `dirty:`, then removes the
  directory. It does **not** recheck process holders.
- `AcceptLoss` writes the diff, the untracked list and the unpushed commits to
  `/work/retained/quarantine/<dirId>/`, then removes the directory. Use it only when the loss is accepted.
- `Removed` removes the directory with **no check at all**.

Only `Pushed` is safe without further proof. Ignored residue is deleted by any of the three, so copy ignored
evidence first if it is wanted. The `.antiphon/c1956-review/` and `rolling-server2/` evidence went with the
17:41Z release.

**What the next residue quarantine will look like (runner without 1961).** A slot task that ran
`tools/Antiphon.Checkpoints` or a `-OutputPath bin-x/` build leaves the following at release:

- the ledger row `class: filesystem`, `reason: reset`, `error: "residue: paths outside the keep-list: !! tests/Antiphon.Tests/bin-<row>/"`;
- attention `workspace_slot_rehomed`;
- the slot moved to `ws-NN-dK`.

The task itself has already settled, so it is not affected. Release it the same way as before:

1. Read the count: `docker exec antiphon-runner-session-runner-1 jq -r '[.quarantinedDirectories[]|select(.isOpen)] | "open \(length)", (.[] | "\(.dirId) \(.slotId) \(.class) \(.reason) \(.branch) \(.bytes)")' /work/workspace-slots/ledger.json`
   (docs/incident-runbook-server2-dispatch.md:129).
2. For a `custody/lingering` row, confirm that no process has its cwd under the path, because `Pushed` does not check.
3. Run `scripts/workspace-slots.ps1 release ... -Disposition Pushed`.

**Re-home cap.** `ws-01` has rehomes at 2026-11-16T16:00Z, 22:02Z and 2026-11-17T01:05Z. The 16:00Z entry is
pruned at the next rehome (`:850-855`). Two more ws-01 quarantines before 22:02Z today would make 4 in 24 h,
which is more than the limit of 3, and ws-01 would be retired. The pool would then create a new base slot,
because Target is 14.

## 4. Desktop runner

- **Slots cannot be turned on for the desktop today.** The pool is built only by the phone-home dispatcher
  (`PhoneHomeCommandDispatcher.cs:194-196`), and the desktop runner is the local HTTP runner, not a phone-home
  runner.
- Windows slots (S3) and desktop slots (S5a/S5c) were never built. The all-roles plan rejects desktop slots
  (`docs/superpowers/plans/2026-11-14-card-0824-all-roles-enablement-plan.md:24,77,93`).
- The five desktop-local tasks today used `C:\Antiphon\worktrees\card-task-*`.
- **No risk to the Windows live-run or qualification lane.** Neither `*` nor a release touches the desktop runner.
- Every server2 task still lists a desktop `worktreePath` (`C:\Antiphon\worktrees\card-task-<id>`, for example
  `9b9b24c8`). Whether the ref-only gate (`Delegation:RefOnlyDesktop:*`) creates that checkout is UNOBSERVED,
  because the setting lives in the gitignored desktop config. That is where the desktop worktree growth comes
  from, and server2 slots do not change it.

## 5. Fastest safe order from now

| Step | When | Time | Advance when | Roll back |
|---|---|---|---|---|
| A. Watch and release | Now, and continuously until 1961 is live | 1-2 min per entry | Open count kept at 2 or fewer by releasing each `Pushed`-clean entry when it appears | `WorkspaceSlotRoles=none` (hot reload; confirm `reason=roles_none` on the next decision). Outstanding leases still release normally. |
| B. Decide Q-8 or Q-9 (§2) | Now (operator) | minutes | The decision is recorded | n/a |
| C. S0 or S1: Code, Review, land, rc port; the §7.0 check prints `RELEASED` | After B | One Code, Review and port cycle (the plan's estimate). The real duration is UNOBSERVED. | Review clean, rc qualification has no new red | Keep the hold |
| D. server2 rollout `deploy-temp` → `drain-old` (up to 240 min) → `redeploy-old` → smoke → `drain-temp` → `retire-temp` | After C | Up to about 4 h for the drain | `buildVersion` = rc SHA; `filesystem/reset` quarantines stop (R-2 of the 1960/1961 plan) | The standard phases. Temp also has slots enabled, so it will quarantine on a pre-1961 image only. |

Answers to the scope questions:

- **Before or after the rc cut.** Every server-side step is already done. Only D needs new runner code.
- **A subset of roles with more than 4 open quarantines.** No subset helps.
  `LeaseAsync` refuses every lease once the open count is more than `MaxQuarantinedDirectories`, before it
  looks at the role (`RunnerWorkspaceSlotPool.cs:95-96`). This was moot after 17:41Z.
- **Minimum number of releases.** With 6 open, releasing 2 would have brought the count to 4, which is not more
  than 4. That leaves no margin: the next quarantine refuses again. Releasing all of them was right.
- **Expected rate without 1961.** The first window under `*` (2026-11-16 16:00Z to 2026-11-17 02:28Z) produced
  6 quarantines. Across the life of the pool, the ledger lease generations add up to 53 lease attempts, so
  quarantines ran at roughly 1 in 9. The start date of that count is UNOBSERVED. Today's 98 server2 tasks at
  that rate would give about 10 quarantines a day. The pool would degrade within about 5 quarantines unless
  someone releases them.

**MaxQuarantinedDirectories = 50 (operator question).**

- **It is not env-only from the stack file.** The runner would bind
  `PhoneHome__WorkspaceSlots__MaxQuarantinedDirectories` (`src/Antiphon.SessionRunner/Program.cs:61`), but the
  compose file passes only `...__Enabled` (`docker-compose.server2-runner.yml:65`). `c590-remote.sh` reads only
  that key and the compiler-policy key from `stack.env`, and never sources the file (`scripts/c590-remote.sh:1105-1132`).
- So it needs a compose edit, land, an rc and a runner recreate, which is the same held rollout as 1961. It
  cannot arrive sooner than 1961.
- **Measured size:**
  - Quarantined directories were 0.46-2.64 GB (the ledger `bytes` above).
  - Live slots now: 0.25 GiB without a build, 2.3-2.4 GiB warm, and 4.0 GiB for ws-04-d2 (`du -s`).
  - `/work` has 82 GiB free of 705 GiB (89% used).

| N | at 2.4 GiB each | at 4.0 GiB each | Against 82 GiB free |
|---|---|---|---|
| 12 | 29 GiB | 48 GiB | fits |
| 25 | 60 GiB | 100 GiB | leaves 22 GiB (typical) or exceeds free space (worst case) |
| 50 | 120 GiB | 200 GiB | **exceeds free space** |

- Open quarantines also count against `PoolDiskBudgetGiB` 64 for overflow slots. Overflow creation also
  refuses below max(20 GiB, 10% of the volume) + 2 x 3 GiB, which is 76.5 GiB free (`RunnerWorkspaceSlotPool.cs:242-251`).
  At 82 GiB free, one or two more quarantines already stop overflow slots. Base slots up to Target 14 are not
  disk-gated (`:172-173`).
- **Recommendation: no to 50.** If the limit is raised at all, raise it to about 12, together with 1961 in the
  same compose change.

## 6. What slots save, and what they do not

| | Legacy mirror | Slot | Source |
|---|---|---|---|
| Workspace prep (decision → "prepared workspace is ready") | p50 2.8 s, p90 4.1 s, max 5.1 s (n=88) | p50 1.1 s, p90 2.4 s, max 3.2 s (n=11) | task events today |
| Checkout of the tree on server2 | 1-4 s (p99 file mtime minus gitdir creation, 69 mirrors still present from today) | reset only | this task |
| Cold `tests/Antiphon.Tests` build in native output | 212 s (Merge 259, `held=212s`) | Warm native `obj/` is kept, so the build is incremental. **Its time is UNOBSERVED; no warm-slot build was measured.** | `docs/investigations/2026-11-17-merge-259-timing.md` (origin `feat/card-task-ba15e8f5`) |
| Checkpoint-tool builds | 214-448 s each, cold by design (`bin-<row>/artifacts`, `tools/Antiphon.Checkpoints/BuildConfinement.cs:508-540`) | **No saving.** These outputs are not on the keep-list, and 1961 deletes them. | same |
| Disk on `/work` | 109 `task-*` mirror directories = 157 GiB now; 110 `task-*` registrations in `/work/repos/antiphon` | Up to 14 base + 4 overflow, about 2.4 GiB each (34-43 GiB) | `du`, `ls` |
| Load waiting | 85 of 159 min of Merge 259 was waiting for load to drop below 16 | **Unchanged.** Slots do not reduce concurrent cold builds. | Merge 259 note |

**Payoff:**

- Slots save about 1.7 s of preparation per task, which is negligible.
- A warm incremental build replaces a cold one for tasks that build in native output (CARD-1629). The size of
  that saving is unmeasured.
- They bound server2 disk at roughly 40 GiB instead of 157 GiB of mirrors, and they reduce Git registrations.
- They do not help checkpoint-heavy Code, Review or Debug time, load waits, or desktop worktree growth.

## 7. Operator decisions (yes/no)

1. **Keep `WorkspaceSlotRoles=*` on the pre-1961 runner, with the orchestrator releasing `Pushed`-clean
   quarantines as they appear?** **Yes.** The only cost is one command per quarantine, and the rollback is a
   hot reload.
2. **Authorize the orchestrator to release `Pushed` without asking each time, after a holder check for
   `custody` rows?** **Yes.** It already did this at 17:41Z. `AcceptLoss` and `Removed` still need the operator.
3. **Ship CARD-1893 S0 (Q-8) to release the hold for 1961 sooner?** **Operator choice.** Choose yes if waiting
   for S1's Code, Review and port cycle is longer than you will accept for manual releases.
4. **Raise `MaxQuarantinedDirectories` to 50?** **No.** It would exceed the 82 GiB free on `/work`, and it needs
   the same held rollout. At most about 12, and only bundled with 1961.
5. **Desktop slots?** **No.** They do not exist for the desktop runner, and no card builds them today.

## Not done, noted

- Fix idea: re-check open quarantines automatically (incident option C, plan Q-3). Separate card; not designed here.
- Fix idea: measure a warm-slot incremental `tests/Antiphon.Tests` build on server2 so §6 has a number.
