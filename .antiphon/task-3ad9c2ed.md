# CARD-1615 plan round 3 — Final Review (task 3ad9c2ed)

Subject: `feat/card-task-7dd90600` @ `0d442c194be86f9cc38e55c56319c78dfc36b11d`,
`docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md`.
Read-only plan review on server2/Linux. No product tests exist yet; none ran.

**Verdict: four defects (D-1..D-4). Round-2 D-1, D-3 and D-5 are closed. Round-2 D-2 (marker)
and D-4 (detached HEAD) are only partly closed.**

## Checks performed

- Importer (slot-gated isolated build, lease `e287b706-…` held 6 s, 5 warnings / 0 errors;
  import lease `840ff1e0-2386-4849-8983-df3d7374e34f` held 2 s): exit 0, **6 rows**, one
  build `bin-c1615`, floors **9/5/6/126/12/6**, every row `serial: true`, Linux estimates
  **6/4/2/1/3/3**, Windows **9/6/3/2/4/4**, all whole minutes. The YAML stays ignored at
  `.antiphon/rv3ad9/import.yaml`. Build outputs have been deleted.
- Independent roster count from source attributes: StandingPipelinePolicy 9/12,
  DockerStackDocumentation 16/16, InstructionBundle 58/78 (25 Arguments rows),
  RunnerBranchContract 8/8, OrchestratorInstructionsGuidance 5/6, ReleaseBranchModel 2/2,
  CommitOnSettle 2/2, AgentContextContract 2/2 = **126**. AgentTaskCallerResolutionTests has 7
  and C1364BoundLandWaitProofTests has 6. No class in the OR filters has a suffixed sibling.
  The four new class names are unused.
- `check-evidence-diff.ps1 -BaseRef 30b76fbf… -HeadRef 0d442c19…`: 10 commits, 0 entries,
  0 violations.

## Closed (with code citations)

- **Round-2 D-1 (task posing as a standing agent): closed** by plan §Caller admission
  (:92-129). `AgentTaskService.AuthenticateAsync` (AgentTaskService.cs:186-234) does resolve
  task, then session, then capability, and the capability arm writes `LastUsedAt`
  (:227-228), exactly as the plan states. The route reads only the header, has no
  caller-selected ID, returns a generic 403 with no principal facts, sets no-store and never
  echoes the token. A missing header yields only `callerClass=Interactive`, so no task IDs
  are disclosed. `Task_token_wins_over_session_and_missing_environment_ids` is both a CP-5
  method and an S1 red row. See advisory A-1 for the Interactive residual.
- **Round-2 D-3 (240-minute ceiling): closed** by §Phase tasks (:285-367). Drain re-entry is
  server-side idempotent. `deploy-server2.ps1:1238-1245` re-POSTs drain only when the runner
  is not already draining, so a slice ending does not abandon the drain. The handover witness
  and its negative vectors are specified.
- **Round-2 D-5 (doc pins): closed.** Pinned phrases are kept (`need no land freeze`,
  `never pull while a land runs`, the authority sentence). CP-4 covers the touched pin classes.
  The other readers of these docs (KeepSix, PublishedRouting, DockerStackContract and
  others) pin sections the plan does not edit. AGENTS.md is 17,803 of 24,576 bytes, so there
  is headroom.
- **Ordering:** CARD-1609 lands first (D-11). CARD-1610 is not a dependency (D-9). The plan
  is an rc fix with a separate master port (:907-913). No definition files are touched
  (:375-376). The merge-tree conflicts with master are inherited rc/master divergence.

## Defects

**D-1 — The AppHost watchdog cannot recover a crashed AppHost while a marker is live.**
- **Where:** plan :210-218 (decision table) and :231-233, which only exclude dev-aspire.
  The plan never mentions `scripts/watchdog-apphost.ps1`.
- **Failure:** the Scheduled Task runs every 2 minutes and calls `restart-apphost.ps1` with no
  `-ExpectedServerSha` (`apphost-common.ps1:611-612`, ArgumentList
  `-NoLogo -NonInteractive -File`). Under the plan's table that is "Live X + missing → exit 3".
  The watchdog treats exit 3 as refused-not-stamped (`watchdog-apphost.ps1:329-332`) and
  simply tries again. If the AppHost crashes mid drain-old slice, the controller keeps
  heartbeating, because renewal is a local file write that does not need the API. Automatic
  recovery is then refused for up to the 75-minute phase budget, and the controller's census
  and version reads fail against a dead server.
- **Why it matters:** the brief requires that the new rule must not make AppHost recovery
  impossible. Here it does, for a phase-long window, with nobody notified.
- **Fix:** specify watchdog behaviour. When the stack is down, the marker is live and canonical
  `HEAD == X`, the watchdog passes `-ExpectedServerSha X`: same-X recovery of a down stack.
  Otherwise it logs loudly and alerts. Qualify the bootstrap.md watchdog text. Add a CP-6
  vector that drives the real watchdog through its `-RestartScript` stub seam.

**D-2 — The marker tracks controller-process liveness, not rollout state, so the guard lapses
at every phase handover.**
- **Where:** :193-195 (expiry is 5 minutes after the last heartbeat), :220-221 (the heartbeat
  belongs to the foreground task), :329-354 (one task per phase, successor dispatched by the
  orchestrator), :202 (admission refuses only a *live* foreign rollout).
- **Failure (a):** after each phase, and after each of up to 16 drain-old slices (480/30), the
  predecessor exits. Five minutes later the marker is valid-stale, and `restart-apphost`
  fails open with only a warning (:216). The gap between tasks routinely exceeds 5 minutes:
  the orchestrator reads the report and journal, dispatches, a Windows worktree is created and
  the session boots. For much of the rollout's wall time the mechanical guard is therefore
  back to docs-only, which is exactly round-2 D-2. The successor's API-X check only notices
  after Y has been activated.
- **Failure (b):** a new `deploy-temp`, with no `-RolloutId`, is admitted over a stale,
  non-Completed marker or journal belonging to another rollout. Two rollouts can then
  interleave the marker and the remote state.
- **Fix:** separate "rollout active" from "controller alive". Rollout-active is durable: the
  journal state is not Completed, and it ends only at Completed or an explicit recorded
  abandonment. While the journal is `SlicePending` or `ReadyForNext`, the restart table must
  stay restart-refusing until a bounded handover deadline (for example 30-60 minutes), and only
  then go stale. A new rollout refuses any non-Completed prior rollout. Add witnesses:
  predecessor exits, clock advances 6 minutes, restart without X is refused; new rollout over
  a stale incomplete rollout is refused. This must be combined with the D-1 and D-3 fixes so
  recovery stays possible.

**D-3 — `ActiveRolloutMarkerUnknown` has no recovery path and no operator override.**
- **Where:** :185-187, :218 and :231 ("no automatic overwrite or bypass", "no … general unlock
  flag").
- **Failure:** any of these blocks every restart, watchdog restarts included, indefinitely:
  - a truncated or corrupt file (power loss during replace, a disk-full partial write);
  - an unreadable file (Windows antivirus or indexer sharing violation, ACL drift);
  - a future-skew marker after a backward clock correction, which blocks for the size of the
    jump.
  No documented step clears it, so an orchestrator using its autonomy grant cannot bring
  AppHost back. The brief explicitly asks for an operator override with a loud explicit flag.
- **Fix:** define one loud, explicit recovery. For example, `restart-apphost.ps1
  -QuarantineActiveRolloutMarker <reason>` renames the file to
  `logs/active-rollout.quarantine-<utc>.json`, records the override, never applies to a valid
  live marker, and is documented in apphost-runbook.md. Or document a manual quarantine
  procedure. Add a bounded retry for transient sharing violations and a CP-6 vector.

**D-4 — The D-12 rationale and its witness misstate how the landing protocol behaves.**
- **Where:** :58, :87, :252-254 and :281-282 ("refuses", "holds every rc land at
  target_checked_out_elsewhere", "show the real landing protocol's refusal").
- **Failure:** `target_checked_out_elsewhere` (AgentTaskLandingProtocol.cs:557-564) is only
  produced inside `AdvanceCanonicalAsync`, which runs after publication. The code comment at
  :470-473 says "Every failure is a recorded residue reason, never a refusal: publication is
  already confirmed and final". A controller on rc therefore lets rc lands publish, as
  `LandedWithResidue`, and leaves the canonical checkout and local rc ref unadvanced. A Code
  author following the plan's witness text would assert a refusal that never happens, and
  would either get a wrong red or bend the assertion.
- **Fix:** restate the premise as canonical-advance residue. The adversarial fixture should
  assert publication plus `CanonicalAdvanceReason=target_checked_out_elsewhere`. The
  detached-HEAD rule itself is sound: detached main gives `(null, null)`, which leads to
  `update-ref`, at :563.

## Advisories

- **A-1:** Interactive admission requires the token, task ID and agent ID to be absent. Also
  require `ANTIPHON_SESSION_ID` (AgentTaskDispatcher.cs:7269) and `ANTIPHON_ORCHESTRATOR` to
  be absent. The non-main detached-source check still blocks the incident shape (a Shared
  task on the canonical checkout). The residual, a Shared task that clears its environment and
  runs from its own detached tree, is accepted at :127-129.
- **A-2:** Renewal refuses while a restart lock is active (:222-223), and `restart-apphost`
  keeps that lock for 15 minutes after exit 4/5. A failed same-X restart therefore ends the
  phase's renewals. State this as the intended fail-closed outcome and add a vector.
- **A-3:** Plan :469 still says the line numbers refer to the round-2 base. The cited
  passages do match the current base (orchestration-loop.md:15-37 and :622, orchestrator
  SKILL.md:174-179), so only the label needs correcting.
