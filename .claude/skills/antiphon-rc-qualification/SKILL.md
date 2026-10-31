---
name: antiphon-rc-qualification
description: Observe a Windows Full rc qualification, route failures to Linux fixers, integrate repairs, and prepare the next candidate without disturbing the live run. Use for the rc qualification operating loop, not publishing or promoting releases.
---

# Antiphon rc qualification

Run commands from the repository root. This is the rc-specific operating procedure;
[antiphon-orchestrator](../antiphon-orchestrator/SKILL.md) and
[antiphon-delegate](../antiphon-delegate/SKILL.md) own general dispatch mechanics.
Read [orchestration](../../../docs/orchestration-loop.md),
[release gates](../../../docs/release-gates.md), and
[testing and build](../../../docs/testing-and-build.md) before operating.
These instructions record the operator's 2026-10-31 rc workflow; read live operating
instructions and scoped effective settings before dispatch. Never change a budget to
make the target fit. Existing prerequisite, startup-probe and release gates still apply.

### OPERATOR DIRECTION overrides for rc qualification

These directions override the cited standing rules in
`docs/orchestration-loop.md` for this rc qualification workflow:

- **2026-10-23**: "rc fixes direct to rc, no review"; **2026-10-27**:
  "RC is the quick-merge line: keep testing minimal". Test-only and other Debug
  repairs follow this direction instead of standing rule 9's reviewed cherry-pick.
  Product/dispatcher/watchdog changes still require the Review in step 4.
- **2026-10-31**: the Debug cap of 4 is a hard create refusal. Custom is the
  sanctioned workaround for small fixes when Debug is full, instead of raising
  the cap under standing rules 1/2. Effective host/project limits still apply.
- **2026-10-31**: stopping the live qualification run is the operator's decision
  (human-only), overriding standing rule 10's abandoned-run stop autonomy here.

## Numbered operating procedure

1. **Record and observe the pinned Windows Full run.** Record the full candidate SHA,
   rc branch, run ID and exact paths supplied by its owner. The layout is
   `C:\Antiphon\releases\candidates\rc-<date>-<sha12>\logs\<run>`,
   `C:\Antiphon\releases\state-<id>\last-run.json`, and
   `C:\Antiphon\releases\task-<id>\supervisor-heartbeat.json`.
   Heartbeat fields include `utc`, `runId`, `supervisorPid`, `trx`, and
   `hangSuspected`. Windows chunks run strictly serially under one native lock.
   The run uses its own candidate directory; pushing to rc does not change its
   source. Logs and state are observations, never permission to stop the supervisor
   or clear the lock. Use `scripts/rc-qualification-status.ps1` for a coherent
   read-only status when its required identities are available.

2. **Arm three bounded watchers and re-arm about every 30 minutes.** Run each as a
   Monitor task with `timeout_ms: 1800000` in the orchestrator's
   Monitor facility (not a new native test task). Helpers finish before that timeout.
   Monitor is a watcher facility, not a `delegate.ps1 -Role` value. If the current
   client has no Monitor facility, report that limitation rather than starting an
   unowned background process. Keep watcher state in an orchestrator-owned scratch
   directory outside the candidate, qualification state, and supervisor directories.
   Reuse that state when re-arming; never write into the observed live run.
   The repository watchers below REPLACE any private watcher copies; re-arm
   these checked-in scripts instead of retaining a divergent private version.

   ```powershell
   pwsh -NoProfile -File scripts/orchestration/watch-tasks.ps1 -ProjectId '<project-guid>' -StateFile '<owned-scratch>\tasks.json'
   # Or use -BoardId <board-guid>; never enumerate the whole fleet implicitly.
   pwsh -NoProfile -File scripts/orchestration/watch-qual.ps1 -LogDir '<candidate>\logs\<run>' -StateFile '<owned-scratch>\qual.seen.txt' -Heartbeat '<task-dir>\supervisor-heartbeat.json'
   pwsh -NoProfile -File scripts/orchestration/watch-master.ps1 -From '<observed-master-full-sha>' -Repository '<repo-root>'
   ```

   `watch-tasks` baselines existing terminal/Blocked tasks on the first successful
   poll, then reports status changes across re-arms, including re-Blocked and
   re-Failed after Working. Legacy seen-pair state gets one explicit fresh baseline.
   `watch-qual` reports
   `FAILURES` per completed TRX, parse errors, suspected hangs, stale/missing heartbeats
   and run completion. Read each task's full report after an event; a watcher line
   is a prompt to investigate, not a verdict. Heartbeat stalls are a detection and
   decision state, never automatic stop authority. `watch-master` observes the
   remote master ref with `git ls-remote`, without fetching or changing a checkout.

3. **Dispatch immediately on every `FAILURES` line.** First extract the real failure:

   ```powershell
   pwsh -NoProfile -File scripts/orchestration/trx-failures.ps1 -Path '<completed-chunk.trx>'
   ```

   Capture the test name, exact message, top stack frame, duration, and relevant
   stdout. The helper displays at most 1500 message characters, three stack frames,
   and 2500 stdout characters; consult the exact TRX read-only when truncated detail
   is needed. Never truncate a collapsed string using the original string's length:
   use `$s.Substring(0, [Math]::Min($n, $s.Length))` on the **same** string.
   A previous extraction bug produced a fake `Substring` MethodInvocationException
   and cost 90 minutes; an extraction exception is not a test failure.

   Brief a Linux fixer with the Windows run identity, exact failure evidence,
   Windows hypotheses, sibling exclusions and the following rules. Use Codex High
   (`-Kind Codex -Level High -Runner server2 -Worktree`). Debug cap is 4; use Custom
   for this repair when Debug is full, subject to effective host/project limits.
   A failure in the same family as an in-flight Debug is a `-Refine` of that task,
   not a duplicate dispatch. Resolve the **current remote rc tip** and pass it as
   `-StartRef <full-sha>`; a default master-based worktree can block an rc fixer.

   ```powershell
   $goal = Get-Content -LiteralPath '<fix-brief.md>' -Raw
   pwsh -NoProfile -File scripts/delegate.ps1 -Role Debug -Kind Codex -Level High -Runner server2 -Worktree -StartRef '<current-rc-full-sha>' -Title 'rc failure: <family>' -Goal $goal
   ```

   Require RED FIRST through a deterministic Linux witness feeding Windows-shaped
   input: CRLF, backslashes/drive letters, read-only files, running-executable
   deletion, code-page decoding, or missing POSIX tools as relevant. Identify which
   side is wrong (production or fixture) and fix that side. Never weaken a test or
   skip Windows to hide a failure. Keep one red witness plus a narrow green; no
   whole-lane reruns. Record every owed native rerun by test class and exact command,
   to execute only after the live run releases its native lock. A Linux green does
   not discharge Windows evidence. Fixers never run native tests, tags, or land
   graph `-Update`. Commit and push each repair slice; report full SHAs and counts.

4. **Integrate repairs through a serial fix pipeline.** Debug fix -> Opus Review
   (`-Kind ClaudeCode -Level High`) for product/dispatcher/watchdog changes -> Merge.
   Test-only and other Debug repairs go directly to rc under this operator workflow;
   a Debug label never exempts a product-code change from Review. An Interim Review
   cannot approve a land; Final/Full needs the whole affected sweep, with missing
   required native execution kept explicitly incomplete.

   Commission one Merge at a time from the current **target rc tip**. Batch the
   series on its assigned branch, preserving fast-forward-only target publication.
   Use `git cherry <target> <source>` to identify `+` commits and `cherry-pick -x`
   them in dependency order. Skip report-only commits changing only
   `.antiphon/task-*.md` or review-report files; retain `docs/investigations` records.
   State every conflict and resolution. Never restore `_PtyHostNativeProps`.
   Never force-push or widen an allow-list to make a batch land.

   Run the **real** graph evaluator under the build-slot gate on the final tree:

   ```powershell
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-merge-graph -- scripts/audit-land-verification-graph.ps1 -Check
   ```

   Supply the documented built audit tool when needed; its build also takes a slot
   and uses an isolated OutputPath. Only a Merge whose `-Check` fails **solely** on
   the recorded digest may use the documented `-Update` once on the final tree.
   Commit regenerated records separately, then rerun `-Check`. Other failures are
   blockers, never authority to regenerate or widen inputs. Historical context:
   68 definitions, digest `7BDABBA0...`, became 69, `E7C6EDAF...`, when the test-only
   GitFixture project was added; these prefixes are history, not acceptance values.

   After dependent rc landings, commission a **master-port Merge** from the current
   master tip. For the 2026-10-31 chain, CARD-1610 lands first: CARD-1619's migration
   must be re-created after CARD-1610's. The rc-to-master merge-commit exception and
   target publication mechanics belong to `docs/release-gates.md`; do not substitute
   an ordinary rebase land for that merge. For a task using managed landing, the
   orchestrator orders `delegate.ps1 -Land <task> -ExpectedSourceSha <full-sha>`.
   Confirm publication only with `git ls-remote origin refs/heads/<target>`;
   `watch-master.ps1 -From <sha>` signals master movement, which must be matched to
   the intended landed SHA (an unrelated advance alone proves nothing).
   Master publication is **not activation**: AppHost and server2 track rc, never
   master. Follow activation gates only when the rc tip changes.

5. **Maintain routing and seats.** Authors, fixers and Merges use Codex High (Sol)
   about as often as Grok; keep Opus (ClaudeCode High) for Review so author and
   reviewer are different companies. Use Astra (`Codex`, `Frontier`) for Plan and
   Investigate. Model tiers are `-Level`, never `-Kind`. Prefer `-Runner server2`;
   reserve desktop work for Windows-native needs or scratchpad access. Aim for
   5 to 7 server2 tasks and refill after every completion, capped by live effective
   settings and seats. Snapshot role caps: Debug 4, Plan 3, Merge 1 (serial).
   Read `GET /api/agent-tasks/pipeline?projectId=<guid>` (or `?unscoped=true` for
   unscoped dispatch) and live orchestrator instructions; lower effective limits
   win. Do not silently reroute a pin, quota or sign-in refusal.

6. **Resolve Blocked and Failed tasks without losing work.** `-Reply` a genuinely
   Blocked task when standing authority answers its question. Exception: never
   Reply to a complete report blocked only by runner sync; use its pushed branch.
   If the report is missing, request the full report, including its closing token.
   To free a seat, cancel a superseded Blocked task whose work is already pushed:
   `POST /api/agent-tasks/<full-guid>/cancel`. Record its successor and retained SHA;
   cancellation is not permission to kill its session. Reply to Failed is rejected:
   dispatch a verification-only successor at the pushed tip. A task Failed at its
   ceiling can still have a live session; read its transcript before deciding.

7. **Protect human-only boundaries.** Tags, publish and promote stay human-only.
   Never kill sessions, touch the live qualification supervisor/lock/files, or
   modify anything under `C:\Antiphon` outside explicitly assigned, owned work.
   Do not use `-AllowWorktree`, `-KillSessions`, or `-Hard` in this workflow. Never
   delete `antiphon_pgdata` or any state volume; never read
   `GET /api/cards/{id}/private-notes`; never print secrets. No unscoped recursive
   or batch deletion: delegates delete only verified exact literal owned paths.

8. **Prepare a fresh Full run after integration.** Once fixes are on rc, record the
   new rc tip and create a fresh Full candidate under the release-gate procedure.
   The old candidate keeps its own source; stopping it is the operator's decision,
   not the orchestrator's. No second native owner: wait for release of the native
   lock before running the owed per-fix native command list or a new Full run.
   Retain test class, exact command, fix SHA and eventual native counts for each
   obligation. Use failed-first ordering and scoped lanes when implemented and
   authorized; do not treat in-progress plans as implemented release credit.

9. **Apply the ordering policy and leave open decisions open.** Priority is failures
   first; then the oldest things never run or unfinished (each lane resumes after
   its last passed chunk); then affected lanes only since the last rc run.
   A planner proposes the nodes; a validator disposes before execution. The graph
   plan `docs/superpowers/plans/2026-10-31-qualification-run-graph-plan.md` is **in
   progress on its owning Plan task branch** (resolve branch/SHA from the Plan
   report; it is absent from this skill's master base). The scoped-lane plan is
   `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`,
   in progress on `feat/card-task-5efd30dd` / `feat/card-task-da0479a5`; consult the
   latest Plan report for the authoritative amendment tip. Reference these plans
   in place, never copy them into this skill or assume they have landed.

   Still for the operator to decide, without implied answers:
   - `ANTIPHON_HEADED_TESTS` in Full versus a separate live lane.
   - R-2 junction and symlink rule for CARD-1618.
   - Whether the suite watchdog needs headroom.
   - Scoped live-lane decisions D-3/D-4/D-5/D-6/D-9/D-11: host/account, quota,
     enforcement, external freshness, and receipt signing, as defined by that plan.

## Handoff

Keep run identity, remote rc/master SHAs, active fix families, full pushed SHAs,
publication observations, owed native commands, and open decisions in the caller's
durable record. End stage reports with `--- next stage ---`, `next:`, `handoff:`,
and `artifact:`; never claim a fresh qualification for the old run's SHA.
