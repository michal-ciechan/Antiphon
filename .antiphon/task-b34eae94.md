# CARD-1615 plan design review (task b34eae94)

Subject: `docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md` at
`afc0a3703b0e62e2853da2987fc07e4bf1ab5a2a` (branch `feat/card-task-7734ac39`). Read-only plan review
on server2/Linux. No product tests are in scope for a plan; Final ordinary scope is therefore the
plan's document checks, which were re-run.

## Verdict

The root cause is correct. The selected option, (d) a Worktree rollout controller plus a short Shared
canonical activation and an early wrapper refusal, is the smallest sound repair. It changes no
landing code, no census and no schema; the fleet-critical land path stays untouched. Options (a),
(b) and (c) all weaken land admission or the census, and the plan's reasons for rejecting them hold.
Three plan defects remain before TestDesign. All three are in admission or contract text, not in the
choice of design.

## Verified ground truth

- `AgentTaskLandService.cs:1015-1045` `FindWriterAsync`: any Dispatched/Working/Blocked nonspecialist
  **Shared** task whose Git common dir matches holds the land. Role and target ref are ignored.
  A Worktree candidate `continue`s unless its own path or canonical dir equals the land's source.
  So a Worktree controller never holds an unrelated land, as the plan claims. The hold is applied at
  `:593` and again at `:742`.
- `AgentTaskService.cs:2510-2514`: an omitted workspace means Worktree. Shared comes only from an
  explicit choice or `-Agent`/agentId.
- `deploy-server2.ps1` has **no** linked-worktree guard. It requires only canonical
  `provision-host-jq` (`:52-67`). The operator token is read from `%LOCALAPPDATA%` (`lib/runner-operator-token.ps1:5`),
  and the resume journal is remote (`/home/mc/antiphon-server2/recycle`). Running the rolling phases
  from a worktree is mechanically supported. Project resolution falls back to URL match (`:531-545`),
  or `-ProjectId` as the plan says.
- Census `:795-825`: bound pending lands refuse; there is no holder exception. Confirmed.
- The dispatcher takes the repository lease at dispatch (`AgentTaskDispatcher.cs:5508-5520`). A running
  land therefore blocks dispatch of the Shared activation. A land in verification is re-held after
  verification (`:742`). D-8's "a land already executing is protected" holds.
- Evidence written to the worktree's `.antiphon/rolling-server2/<run>` is retained on cleanup by
  `FileSystemEvidenceRetention` (`**/.antiphon/**`, orchestration-loop.md:166-171). This answers the
  "Retain the rollout receipts" rule (docker-stack.md:130), though the plan does not cite it.

## Defects

**D-1 — D-7/V-3 refuses the standing orchestrator's own rollout path.**
- *Where:* plan D-7 ("If either exists [task ID or token], require a valid task ID ... Missing ...
  refuses"), V-3 ("token without ID ... refuse"), S2.
- *Failure:* a standing agent session launched through `AgentSessionLaunchComposer.ComposeForAgentAsync`
  (`:43-50`) gets `ANTIPHON_TASK_TOKEN` and `ANTIPHON_AGENT_ID` but no `ANTIPHON_TASK_ID`.
  `CardService.cs:1792-1799` and `OrchestratorService.cs:976-987` card-launched sessions are the same.
  Under the plan, an orchestrator that runs `deploy-server2.ps1 -Rolling -Phase deploy-temp` itself,
  as orchestration-loop.md:15-21 explicitly authorizes, is refused with `RolloutControllerIdentityUnknown`.
- *Why it is wrong:* that session is not an AgentTask, so `FindWriterAsync` never sees it. It cannot
  hold a land and cannot form this cycle. The plan treats a legitimate path that does not deadlock as
  tampering, and gives no migration for it.
- *Fix:* classify the identity explicitly as one of three cases: task (ID present), standing agent
  (token + `ANTIPHON_AGENT_ID`, no task ID), and interactive (neither). Decide whether a standing
  agent is admitted (it is not a land writer) or refused with a named reason and a documented
  migration. Add a V-3 vector per class, fed through the real env shape the launch composer produces.

**D-2 — the "later activation" step has no SHA binding and no rule for skew during the rollout.**
- *Where:* plan D-8 ("For later activation ... dispatch the short Shared activation") and the S3
  canonical-only list.
- *Failure:* today the Shared Deploy incidentally froze the canonical checkout by holding every land.
  Under (d), rc lands publish during the rollout, and `AgentTaskLandingProtocol` fast-forwards the
  main checkout because it holds rc. Concretely: the controller starts with AppHost at rc X. During
  `drain-old`'s 240-minute wait an rc land moves the canonical HEAD to Y. Any canonical restart
  before the controller finishes then builds Y while server2 runs X. That covers the D-8 "later
  activation", an unrelated orchestrator AppHost or desktop runner restart, and a recovery restart.
  `restart-apphost.ps1:177-208` binds the expected SHA to source HEAD, so it reports healthy at Y.
  The controller's later census and API reads then run against a server it was not qualified
  against (compare docker-stack.md:51-53, the `cause=Http status=400` skew case).
- *Fix:* state which SHA each canonical step targets: preparation activates X, and "later
  activation" targets a named SHA or is removed. State whether canonical AppHost and desktop runner
  restarts are forbidden while a controller is between phases, or are allowed only with
  `-ExpectedServerSha` equal to the rollout SHA. State what the controller does when `/api/version`
  differs from `-Sha` at a phase start (refuse with a named reason, or record it). Add the
  admission check if it is chosen.

**D-3 — S3 does not name the existing operating text that re-creates the cycle or contradicts the new split.**
- *Where:* plan S3 and D-8.
- *Failure:* orchestration-loop.md:29-31 says "Before and after an AppHost pull or restart, verify that
  queued lands have finished ... Wait for any land in progress before pulling". Inside the short
  Shared activation, every land requested after its dispatch is Held **by the activation itself**.
  An activation agent that follows this sentence waits on its own hold: the CARD-1615 cycle again,
  bounded only by the agent's judgement. Other text also contradicts the S3 phase split:
  - docker-stack.md:473 ("From the canonical desktop checkout" for manual main cleanup) and
    docker-stack.md:513 (`retire-temp` recovery "from the canonical desktop checkout") contradict
    S3, which moves `retire-temp` to the controller tree.
  - AGENTS.md:55 ("Restart and deploy scripts refuse ... when run from a linked Git worktree")
    leads a controller to believe `deploy-server2.ps1` cannot run from its worktree.
  - `.claude/skills/antiphon-delegate/SKILL.md:184` ("Workers default to shared") is stale against
    `EffectiveRequestedWorkspace` and steers dispatchers toward Shared.
- *Fix:* list these exact sentences in S3 with their replacements:
  - Inside a Shared activation, a Held land is not waited for. Only a Running land, which already
    blocks dispatch through the repository lease, matters.
  - `retire-temp` and manual cleanup run in the controller tree, or are explicitly kept canonical.
  - AGENTS.md scopes the worktree refusal to restart and `deploy-local`.
  - The skill's default matches the server.

## Advisories

- **A-1 (CARD-1609 ordering).** CARD-1609 is InProgress and edits volume retirement and `retire-temp`
  in the same `scripts/deploy-server2.ps1`. Its manual attested path writes
  `C:\src\Antiphon\.antiphon\rolling-server2\manual-retire-*`, in the canonical checkout. Declare
  `scripts/deploy-server2.ps1` in scope so the same-area Code deferral applies. Name which repair
  lands first. Say whether the manual retire path (not a `deploy-server2` phase) is also expected to
  run from the controller tree.
- **A-2 (CARD-1610).** D-9 is right. Note that admission is checked once per invocation: a controller
  failed by the role ceiling in the middle of a phase continues that phase, which is now harmless to
  lands because it is a Worktree. The next phase refuses (terminal owner). Say this explicitly so it
  is not "fixed" by adding re-checks inside a phase.
- **A-3.** Cite `FileSystemEvidenceRetention` as the retention mechanism for controller receipts. Make
  the controller's report carry the printed evidence paths, because the worktree copy is removed.
- **A-4 (placement).** Only one Windows runner exists today (`desktop`), so `-Platform Windows` without
  `-Runner` resolves correctly. The controller needs that host's `%LOCALAPPDATA%` operator token and
  its SSH identity. If a second Windows runner appears, an unpinned controller fails at
  `OperatorTokenMissing`. That fails closed but late, so the docs should name the requirement.
- **A-5.** Red evidence: V-1, V-2 and V-3 are the only red-at-base witnesses. That is consistent,
  because the remedy is admission and the land and census behaviour stays unchanged; V-4 to V-6
  characterize that behaviour. Keep V-6's "Shared cycle" leg as an executed characterization through
  the real `RunAsync` hold and the real census extraction, not a seeded `landRequest.state`.

## Checks re-run (document scope)

- Tool build through the gate: `build-slot.ps1 -Label b34eae94-import-build` (lease `3ef26b5d`, waited
  0 s, held 8 s, 5 warnings, 0 errors). Output `bin-b34rev/` was in two projects; both were deleted.
- `import --plan <plan>` through the gate (lease `2f1e8e64`): `imported 3 rows`, exit 0. Filters,
  `estimatedMinutes` 6/4/2, Windows 9/6/3 and `serial: true` were imported as written. Importer
  timeouts default to 18/15/15; the plan correctly says these are not its budget.
- CP-3 roster: `tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs`, namespace
  `Antiphon.Tests.Scripts`, exactly 6 `[Test]` methods, no `[Arguments]`. The minimum of 6 is correct.
  The CP-1 and CP-2 classes do not exist yet, as the plan states.
- No product build or test was run. The plan defines no Unit-lane or integration execution for a
  plan-design round. PCs: none defined; pending for TestDesign/Code.
- `check-evidence-diff`: not applicable. Review adds only this report.
