# Backlog card census (2026-11-10)

Read-only. No card was moved, closed, or edited. Private notes were not read.

Scope: every card with status Backlog on the Antiphon board. `card.ps1 list -Board Antiphon -Status Backlog -Json` returned 267 cards, `truncated=false`. All 267 are in the Backlog column (`22a04b2d-609c-45bb-821d-280b948792fa`). Enumerated this session against the live API. `origin/master` is `ef7afd44927206f6c03547670a5add84130bdce9`.

Last revision date is the card `updatedAt`. Card writes bump it. `revisionCount` 0 means `updatedAt` equals `createdAt`.

## Result

249 cards have no task, no own plan or investigation, no unmerged branch, and no master commit that does their work. 8 are duplicate or obsolete filings with a named survivor. 10 are UNSURE. None are in flight, none have unlanded code of their own, and none look complete enough to close as landed.

## Counts by work state

| work state | cards |
|---|---:|
| NO-WORK | 249 |
| PLANNED-ONLY | 0 |
| IN-FLIGHT | 0 |
| CODE-UNLANDED | 0 |
| LANDED-OPEN-ITEMS | 0 |
| LANDED-REOPEN-QUESTION | 0 |
| DUPLICATE/OBSOLETE | 8 |
| UNSURE | 10 |
| **total** | **267** |

## Counts by importance/urgency

| importance/urgency | cards |
|---|---:|
| Normal/Normal | 231 |
| Low/Normal | 27 |
| High/Soon | 4 |
| Normal/Soon | 4 |
| High/Normal | 1 |

## Counts by platform

| requiredPlatform | cards |
|---|---:|
| Any | 265 |
| Linux | 1 |
| Windows | 1 |

The only non-Any cards are CARD-1752 (Linux, High/Normal, NO-WORK) and CARD-0990 (Windows, Normal/Normal, UNSURE).

## Work state by importance/urgency

| band | NO-WORK | DUPLICATE/OBSOLETE | UNSURE |
|---|---:|---:|---:|
| High/Normal | 1 | 0 | 0 |
| High/Soon | 4 | 0 | 0 |
| Low/Normal | 25 | 1 | 1 |
| Normal/Normal | 215 | 7 | 9 |
| Normal/Soon | 4 | 0 | 0 |

## How a card got its state

Bound tasks: `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703` returned 8011 rows (specialist Check/Distill/Diagnose rows excluded by that route). 5 rows are bound to Backlog cards, on 2 cards: CARD-0114 Plan Succeeded `5f7ba14d` and Code Succeeded `425791a4`; CARD-0623 three Investigate Canceled `09adcb72`, `571692b3`, `93dcaf67`. The board summary has 6 Dispatched tasks; none of them are bound to a Backlog card. Non-terminal means Queued, Dispatched, Working, or Blocked. In-flight count is 0.

Review and mutation: `GET /api/stage-outcomes?latestOnly=true` returned 7609 rows. None have a `cardId` in this Backlog set, so there is no Clean or Found verdict to cite. No Mutation task is bound to a Backlog card.

Master commits: `git log origin/master --grep=CARD- -i` produced 8587 messages. Five subjects name a current Backlog card. Four of those are another card's commit mentioning this one (CARD-0455 in CARD-0501, CARD-0576 in CARD-0508, CARD-0596 in CARD-1007, CARD-1646 in CARD-1610). The fifth is CARD-0114's plan, `f11d93648973`. Body mentions (20 cards) are citations such as "tracked by" or "filed as", not a landing of that card. A citation is recorded below and is not counted as work.

Unmerged branches: `git branch -r --no-merged origin/master` returned 5448 names. Fifteen names contain `card-<digits>`. None of those numbers are current Backlog cards. `git log --remotes --not origin/master --grep=CARD-` was searched. Hits on Backlog cards are range audits (`CARD-0770 through CARD-1832`, `CARD-0871 through CARD-1824`) or the other-card doc commits above. CARD-0114's `feat/card-task-425791a4` is not on the remote.

Docs: filenames under `docs/superpowers/plans` and `docs/investigations` matching `card-<digits>-` name only CARD-0114's plan for this set. Being mentioned inside another card's doc, including `docs/investigations/2026-10-08-backlog-triage.md` (230 of these 267 cards), is a citation. The October triage's probably-done and stale-obsolete rows were not copied into a close state unless this pass re-checked the claim.

UNSURE is used where those sources do not support one of the other states.

## Close proofs

These are the only cards in `docs/investigations/2026-11-10-card-census-backlog-close.txt`. No card is LANDED-REOPEN-QUESTION.

- **CARD-0275** — Superseded by Done CARD-0723: scripts/run-checkpoint.ps1 and tools/Antiphon.Checkpoints are on origin/master ef7afd44927206f6c03547670a5add84130bdce9. October triage confidence was medium; this pass did not re-test a residual need.
- **CARD-0393** — Duplicate of Backlog CARD-0756. This title is "ContractSnapshotTests: 2/5 cases red at HEAD (fixture drift)"; CARD-0756 is "ContractSnapshotTests: 4 of 5 fail at master (stale fixtures), pipeline case reads desktop user-secrets".
- **CARD-0702** — Duplicate of Backlog CARD-0673 (Codex reviewers cannot delete bin-* output). CARD-0702 is the Windows wording of that approval-policy defect. Confirm CARD-0673 covers Windows before closing.
- **CARD-0722** — Same defect as Backlog CARD-0689. CARD-0722: check interpreter [antiphon-report] text leaked into a caller check digest. CARD-0689: check notes carry stray interpreter report tokens and end-of-session narration.
- **CARD-0776** — Duplicate of Backlog CARD-0828. CARD-0776 is two checkpoint ownership tests waiting 5s of wall clock and timing out under load. CARD-0828 is those CheckpointTaskOwnershipTests timing out under load and leaking a CPU-spinning owner watch loop.
- **CARD-0819** — Duplicate of Backlog CARD-0773. Both titles are the checkpoint tool building tests/Antiphon.Tests in parallel against one shared obj/ directory.
- **CARD-0843** — Same CARD-0772 fallout as Backlog CARD-0783. CARD-0843: PinnedAgentKindTests T1/T2 red because runner-less Codex is refused. CARD-0783: four Codex delegate/pinned-profile tests stale for that refusal.
- **CARD-0943** — Same scratch self-check as Backlog CARD-0949. tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs:165-167 still builds a scratch copy and asserts the removed sentence is absent. Closing CARD-0943 does not remove that pin; CARD-0949 still tracks it.

## UNSURE

- **CARD-0114** (Normal/Normal, updated 2026-09-07) Converge AgentTuiSecret into API keys — Plan is on master (f11d93648973). S0/S1 code is present (AgentTuiProfileService.cs:1156, AgentTuiSecretMigrator.cs). AgentTuiSecret.cs remains, so S3 is not done. No review row. Code branch feat/card-task-425791a4 is gone. Code task 425791a4 Succeeded 2026-08-21. Next: Do not close. Finish or re-scope S2/S3.
- **CARD-0278** (Normal/Normal, updated 2026-08-31) Guard Codex tests with isolated CODEX_HOME — 2026-10-08 triage probably-done, low confidence (CODEX_HOME gate). Not re-checked on ef7afd449272. No bound task or review. Next: Re-read the Codex test gate before closing.
- **CARD-0380** (Normal/Normal, updated 2026-09-07) Release-notes discipline on land + periodic official release via nightly build — 2026-10-08 triage probably-done, medium, pointing at CARD-0599 release notes. Not re-checked on ef7afd449272. No bound task or review. Next: Re-read docs/release-gates.md against the card before closing.
- **CARD-0455** (Low/Normal, updated 2026-09-09) Delegate bin-* cleanup silently 'blocked by policy' with no reason surfaced — No bound task. CARD-0501 doc commit c5820cecf2b5 records an output-retention waiver. 2026-10-08 triage: human decision, low confidence (CLI approval policy, not Antiphon). Next: Operator decides whether this stays a card.
- **CARD-0525** (Normal/Normal, updated 2026-09-14) Investigate high CPU reading: trace shows threads parked in native waits, not managed hot code — 2026-10-08 triage called it stale-obsolete (medium): one-off 2026-09-14 CPU reading, no recurrence named. This pass did not remeasure. No task or commit. Next: Close only after someone confirms there is no recurrence.
- **CARD-0596** (Normal/Normal, updated 2026-09-21) Grok_inspect_is_bounded_at_the_nested_checkout_git_root fails at master tip (pre-existing) — Not fixed here. CARD-1007 commit d1d046267400 says the oracle was baseline-confirmed red. Same subject is also on unmerged origin/feat/card-task-e1b6cb4a. No bound task and no review. Next: Treat as an open red test. Do not close from the CARD-1007 note.
- **CARD-0605** (Normal/Normal, updated 2026-09-22) RequireLinux()-gated test classes (e.g. LinuxPtyHostLauncherTests) have no execution lane, have never run — 2026-10-08 triage probably-done, low: Linux runner might give RequireLinux tests a lane. Not verified on a checkpoint row. No bound task or review. Next: Do not close until a Linux checkpoint row actually runs the class.
- **CARD-0623** (Normal/Normal, updated 2026-09-23) TaskDetailBody test fails reproducibly (pre-existing, not flaky) — Three Investigate tasks canceled 2026-09-23 (09adcb72, 571692b3, 93dcaf67). No investigation doc, branch, or master commit names the card. Next: Re-investigate, or confirm the TaskDetailBody failure is gone before closing.
- **CARD-0636** (Normal/Normal, updated 2026-09-23) delegate -OnAgent on a retired Worktree task silently falls back to Shared in the canonical checkout — 2026-10-08 triage said merge into CARD-0644 after it lands. CARD-0644 is still InProgress. No task, commit, or branch on CARD-0636. Next: Wait until CARD-0644 lands, then decide the merge.
- **CARD-0990** (Normal/Normal, updated 2026-10-02) CARD-0970 Windows case-alias obstruction rows (GEN/Gen) have never executed: qualify on Windows — 2026-10-08 triage probably-done, medium, from the card's own Windows note. requiredPlatform=Windows. No bound task, commit, or review on this card. Next: Re-check the Windows GEN/Gen result before closing.

## Oldest 15 NO-WORK cards by creation date

| id | created | updated | rev | importance/urgency | title |
|---|---|---|---:|---|---|
| CARD-0081 | 2026-08-18 | 2026-08-18 | 1 | Normal/Normal | Idea: headless Grok ACP adapter for delegate sessions (parked, low prio) |
| CARD-0253 | 2026-08-30 | 2026-09-06 | 3 | Normal/Normal | The CARD-0247 nudge hook already runs inside Grok sessions via Claude compat, unintentionally |
| CARD-0269 | 2026-08-30 | 2026-09-06 | 2 | Normal/Normal | Check-delivered log is misleading on failure, and an unexplained NoComposerEvidence (CARD-0264 follow-up) |
| CARD-0271 | 2026-08-31 | 2026-09-06 | 2 | Normal/Normal | Attention sweep for red CI on master (CARD-0258 follow-up) |
| CARD-0274 | 2026-08-31 | 2026-08-31 | 1 | Normal/Normal | Validate Windows-only path settings |
| CARD-0277 | 2026-08-31 | 2026-08-31 | 1 | Normal/Normal | Guard hosted pumps against timeout OCE catches |
| CARD-0280 | 2026-08-31 | 2026-08-31 | 1 | Normal/Normal | Provide safe queue-test clock factory and guard |
| CARD-0290 | 2026-08-31 | 2026-09-07 | 3 | Normal/Normal | Provider usage tracking: hourly sampling + multi-window rollups (Codex ran out silently) |
| CARD-0311 | 2026-09-01 | 2026-09-07 | 2 | Normal/Normal | Use context fullness as a diagnostic signal when an agent is dead or seems dead |
| CARD-0314 | 2026-09-01 | 2026-09-06 | 2 | Normal/Normal | Tokenless orchestrator cannot schedule a safe Grok delegate; documented model-availability writes return 405 |
| CARD-0317 | 2026-09-02 | 2026-09-02 | 2 | Normal/Normal | Investigate per-kind prompt-cache TTL and give idle orchestrator/agent sessions a cost-aware keep-warm check-i |
| CARD-0318 | 2026-09-02 | 2026-09-02 | 2 | Normal/Normal | Distinguish cosmetic (spinner/timer) screen motion from real progress, and ask a suspected-stalled agent for s |
| CARD-0356 | 2026-09-04 | 2026-09-04 | 0 | Normal/Normal | Boot-turn TurnTitle blind spot + AgentTaskOverdueDeadlineTests order flake |
| CARD-0363 | 2026-09-04 | 2026-09-07 | 1 | Normal/Normal | Tracker sync read half runs unauthenticated, hits GitHub anonymous rate limit |
| CARD-0364 | 2026-09-04 | 2026-09-04 | 0 | Normal/Normal | CARD-0347 S3+S4: digest continuity, docs, archive follow-up |

## Per-card table

Column is Backlog for every row. `updated` is the last revision date.

| id | state | importance/urgency | platform | updated | evidence | next action |
|---|---|---|---|---|---|---|
| CARD-0081 | NO-WORK | Normal/Normal | Any | 2026-08-18 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (high): Explicitly parked by a user decision ('do not plan or dispatch without asking'); decide un-park or archive. | Operator decision. No implementation has started. |
| CARD-0114 | UNSURE | Normal/Normal | Any | 2026-09-07 | Plan is on master (f11d93648973). S0/S1 code is present (AgentTuiProfileService.cs:1156, AgentTuiSecretMigrator.cs). AgentTuiSecret.cs remains, so S3 is not done. No review row. Code branch feat/card-task-425791a4 is gone. Code task 425791a4 Succeeded 2026-08-21. | Do not close. Finish or re-scope S2/S3. |
| CARD-0253 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0269 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0271 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0274 | NO-WORK | Normal/Normal | Any | 2026-08-31 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0275 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-08-31 | Superseded by Done CARD-0723. scripts/run-checkpoint.ps1 and tools/Antiphon.Checkpoints are on origin/master ef7afd449272. Residual need not re-tested. | Close if no separate front door is still required; keep CARD-0723. |
| CARD-0277 | NO-WORK | Normal/Normal | Any | 2026-08-31 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0278 | UNSURE | Normal/Normal | Any | 2026-08-31 | 2026-10-08 triage probably-done, low confidence (CODEX_HOME gate). Not re-checked on ef7afd449272. No bound task or review. | Re-read the Codex test gate before closing. |
| CARD-0280 | NO-WORK | Normal/Normal | Any | 2026-08-31 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0290 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0311 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0314 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (low): Tokenless-orchestrator path predates Delegation Capabilities (delegate.ps1 -Capability); decide whether capabilities close it and whether th | Operator decision. No implementation has started. |
| CARD-0317 | NO-WORK | Normal/Normal | Any | 2026-09-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0318 | NO-WORK | Normal/Normal | Any | 2026-09-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0356 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0363 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0364 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0365 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (medium): Operator offered two shapes (auto-close vs digest-only, plus hybrid) with a GitHub-close-cascade risk; pick the shape before Plan. | Operator decision. No implementation has started. |
| CARD-0368 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0369 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0370 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0371 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0373 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0374 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0376 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0379 | NO-WORK | Normal/Normal | Any | 2026-09-04 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0380 | UNSURE | Normal/Normal | Any | 2026-09-07 | 2026-10-08 triage probably-done, medium, pointing at CARD-0599 release notes. Not re-checked on ef7afd449272. No bound task or review. | Re-read docs/release-gates.md against the card before closing. |
| CARD-0392 | NO-WORK | Normal/Normal | Any | 2026-09-05 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0393 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-09-05 | Same ContractSnapshotTests fixture drift as Backlog CARD-0756 (2/5 here, 4/5 plus user-secrets there). | Close as duplicate; keep CARD-0756. |
| CARD-0404 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0405 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0414 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0421 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0422 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0423 | NO-WORK | Normal/Normal | Any | 2026-09-06 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (medium): Mockup-then-human-pick pipeline mechanism; operator must choose new role vs flag before Plan. | Operator decision. No implementation has started. |
| CARD-0427 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0428 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0433 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0434 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0435 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0438 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0439 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0440 | NO-WORK | Normal/Normal | Any | 2026-09-07 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0445 | NO-WORK | Normal/Normal | Any | 2026-09-08 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0447 | NO-WORK | Normal/Normal | Any | 2026-09-08 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0453 | NO-WORK | Normal/Normal | Any | 2026-09-09 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0455 | UNSURE | Low/Normal | Any | 2026-09-09 | No bound task. CARD-0501 doc commit c5820cecf2b5 records an output-retention waiver. 2026-10-08 triage: human decision, low confidence (CLI approval policy, not Antiphon). | Operator decides whether this stays a card. |
| CARD-0456 | NO-WORK | Normal/Normal | Any | 2026-09-09 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0463 | NO-WORK | Normal/Normal | Any | 2026-09-09 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0473 | NO-WORK | Low/Normal | Any | 2026-09-10 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0483 | NO-WORK | Normal/Normal | Any | 2026-09-11 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0513 | NO-WORK | Normal/Normal | Any | 2026-09-13 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0517 | NO-WORK | Normal/Normal | Any | 2026-09-13 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0522 | NO-WORK | Normal/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0525 | UNSURE | Normal/Normal | Any | 2026-09-14 | 2026-10-08 triage called it stale-obsolete (medium): one-off 2026-09-14 CPU reading, no recurrence named. This pass did not remeasure. No task or commit. | Close only after someone confirms there is no recurrence. |
| CARD-0526 | NO-WORK | Low/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0528 | NO-WORK | Normal/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0530 | NO-WORK | Normal/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0531 | NO-WORK | Normal/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0532 | NO-WORK | Normal/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0536 | NO-WORK | Normal/Normal | Any | 2026-09-14 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0539 | NO-WORK | Normal/Normal | Any | 2026-09-15 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0564 | NO-WORK | Normal/Normal | Any | 2026-09-18 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0565 | NO-WORK | Normal/Normal | Any | 2026-09-18 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0570 | NO-WORK | Normal/Normal | Any | 2026-09-19 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0572 | NO-WORK | Low/Normal | Any | 2026-09-19 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0576 | NO-WORK | Normal/Normal | Any | 2026-09-19 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0583 | NO-WORK | Normal/Normal | Any | 2026-09-19 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (low): Evaluate Hindsight skill (legitimacy check first); overlaps existing /reflect skills; operator interest call. | Operator decision. No implementation has started. |
| CARD-0591 | NO-WORK | Normal/Normal | Any | 2026-09-20 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0596 | UNSURE | Normal/Normal | Any | 2026-09-21 | Not fixed here. CARD-1007 commit d1d046267400 says the oracle was baseline-confirmed red. Same subject is also on unmerged origin/feat/card-task-e1b6cb4a. No bound task and no review. | Treat as an open red test. Do not close from the CARD-1007 note. |
| CARD-0601 | NO-WORK | Normal/Normal | Any | 2026-09-21 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0602 | NO-WORK | Normal/Normal | Any | 2026-09-22 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0605 | UNSURE | Normal/Normal | Any | 2026-09-22 | 2026-10-08 triage probably-done, low: Linux runner might give RequireLinux tests a lane. Not verified on a checkpoint row. No bound task or review. | Do not close until a Linux checkpoint row actually runs the class. |
| CARD-0609 | NO-WORK | Low/Normal | Any | 2026-09-22 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0612 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0614 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0618 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0619 | NO-WORK | Low/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0621 | NO-WORK | Low/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0622 | NO-WORK | Low/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0623 | UNSURE | Normal/Normal | Any | 2026-09-23 | Three Investigate tasks canceled 2026-09-23 (09adcb72, 571692b3, 93dcaf67). No investigation doc, branch, or master commit names the card. | Re-investigate, or confirm the TaskDetailBody failure is gone before closing. |
| CARD-0624 | NO-WORK | Low/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0625 | NO-WORK | Low/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0626 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0627 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0630 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0634 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0635 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0636 | UNSURE | Normal/Normal | Any | 2026-09-23 | 2026-10-08 triage said merge into CARD-0644 after it lands. CARD-0644 is still InProgress. No task, commit, or branch on CARD-0636. | Wait until CARD-0644 lands, then decide the merge. |
| CARD-0637 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0638 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0639 | NO-WORK | Normal/Normal | Any | 2026-09-23 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0648 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0652 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0663 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0668 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0670 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0673 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0674 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0682 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0683 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0686 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0689 | NO-WORK | Normal/Normal | Any | 2026-09-24 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0695 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0702 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-09-25 | Windows wording of Backlog CARD-0673: Codex approval policy blocks deleting bin-* build output. | Close only if CARD-0673 is understood to cover Windows. |
| CARD-0703 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (high): Unlanded CARD-0395 Herdr rules-ownership work preserved in a worktree; operator must decide port vs discard (possible safety gap). | Operator decision. No implementation has started. |
| CARD-0704 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (high): Unlanded CARD-0467 tests/drawer polling in two worktrees; decide port vs retire. | Operator decision. No implementation has started. |
| CARD-0713 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0715 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0720 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0722 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-09-25 | Same check-interpreter report-token leak as Backlog CARD-0689. | Close as duplicate; keep CARD-0689. |
| CARD-0724 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0725 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0731 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0732 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0733 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0734 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0736 | NO-WORK | Normal/Normal | Any | 2026-09-25 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0739 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0743 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0744 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0745 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0746 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0747 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0750 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0751 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0752 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0756 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0761 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0763 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0764 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0765 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0769 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0770 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0771 | NO-WORK | Normal/Normal | Any | 2026-09-26 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (high): Whether GET /api/agent-tasks/{id} should authenticate X-Antiphon-Task-Token or the tool/plan should stop claiming authenticated ownership; c | Operator decision. No implementation has started. |
| CARD-0773 | NO-WORK | Normal/Normal | Any | 2026-09-27 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0774 | NO-WORK | Normal/Normal | Any | 2026-09-27 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0775 | NO-WORK | Normal/Normal | Any | 2026-09-27 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0776 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-09-27 | Same CheckpointTaskOwnershipTests wall-clock waits as Backlog CARD-0828, which also names the leaked watch loop. | Close as duplicate; keep CARD-0828. |
| CARD-0782 | NO-WORK | Normal/Normal | Any | 2026-09-27 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0783 | NO-WORK | Normal/Normal | Any | 2026-09-27 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0787 | NO-WORK | Normal/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0789 | NO-WORK | Low/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0790 | NO-WORK | Normal/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0792 | NO-WORK | Normal/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0793 | NO-WORK | Normal/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0794 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0797 | NO-WORK | Low/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0798 | NO-WORK | Normal/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0799 | NO-WORK | Normal/Normal | Any | 2026-09-28 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0803 | NO-WORK | Low/Normal | Any | 2026-09-29 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0809 | NO-WORK | Normal/Normal | Any | 2026-09-29 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0814 | NO-WORK | Normal/Normal | Any | 2026-09-29 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (medium): Bump runner Claude CLI to 2.1.284 for Sonnet 5.5; may already have been superseded by a later CLI bump - operator to confirm current image v | Operator decision. No implementation has started. |
| CARD-0815 | NO-WORK | Normal/Normal | Any | 2026-09-29 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0818 | NO-WORK | Normal/Normal | Any | 2026-09-29 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0819 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-09-29 | Same checkpoint-tool shared obj/ collision as Backlog CARD-0773. | Close as duplicate; keep CARD-0773. |
| CARD-0825 | NO-WORK | Normal/Normal | Any | 2026-09-29 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0828 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0829 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0830 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0831 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0832 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0834 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0836 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0837 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0838 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0839 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0840 | NO-WORK | Low/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0841 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0843 | DUPLICATE/OBSOLETE | Normal/Normal | Any | 2026-09-30 | Same CARD-0772 runner-less Codex refusal as Backlog CARD-0783. | Close as duplicate; keep CARD-0783. |
| CARD-0844 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0848 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0850 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0853 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0854 | NO-WORK | Low/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0855 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0857 | NO-WORK | Normal/Normal | Any | 2026-09-30 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0862 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0864 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0865 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0867 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0868 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0869 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0871 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0872 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0873 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0874 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0875 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0877 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0878 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0879 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0887 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0893 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0894 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0895 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0896 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0897 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0898 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0899 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0906 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0910 | NO-WORK | Normal/Normal | Any | 2026-10-01 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0914 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (high): .NET 10 upgrade: all 45 csproj TargetFramework values are still net9.0 on master and net9 support ends 2026-11-10; large, cross-cutting - ne | Operator decision. No implementation has started. |
| CARD-0916 | NO-WORK | Low/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0917 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0920 | NO-WORK | Low/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0921 | NO-WORK | Low/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0922 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (high): Strict receipt validator rejects every honest Linux Unit-lane receipt (33 platform skips); choose (a) declared skip set or (b) Linux Unit fi | Operator decision. No implementation has started. |
| CARD-0923 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0924 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0928 | NO-WORK | Low/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0929 | NO-WORK | Low/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (low): Cache seed marker forgeable by host user mc; operator decides whether an mc-level writer is in the threat model. | Operator decision. No implementation has started. |
| CARD-0930 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0932 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0935 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0936 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (medium): N=30 loaded/quiet stress rows and Windows rows pending for CARD-0742; fund or waive. | Operator decision. No implementation has started. |
| CARD-0938 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0941 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0943 | DUPLICATE/OBSOLETE | Low/Normal | Any | 2026-10-02 | Same scratch self-check as Backlog CARD-0949. Pattern still at StandingPipelinePolicyDocumentationTests.cs:165. | Close as duplicate; keep CARD-0949. The pin is still in the test. |
| CARD-0949 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0962 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0968 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0979 | NO-WORK | Normal/Soon | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (medium): CARD-0883 residue: 9 seam-dependent results, Windows V-5, 81 PCs; fund or waive. | Operator decision. No implementation has started. |
| CARD-0982 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-0990 | UNSURE | Normal/Normal | Windows | 2026-10-02 | 2026-10-08 triage probably-done, medium, from the card's own Windows note. requiredPlatform=Windows. No bound task, commit, or review on this card. | Re-check the Windows GEN/Gen result before closing. |
| CARD-1000 | NO-WORK | Normal/Normal | Any | 2026-10-02 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-1078 | NO-WORK | Low/Normal | Any | 2026-10-05 | No task, commit, branch, or own plan. 2026-10-08 triage asks a decision (low): Deferred CARD-1029 inert-observation matrix (114 vectors, 211 PCs) explicitly operator-deprioritized; fund or close. | Operator decision. No implementation has started. |
| CARD-1084 | NO-WORK | Low/Normal | Any | 2026-10-05 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-1114 | NO-WORK | Normal/Normal | Any | 2026-10-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-1117 | NO-WORK | Normal/Normal | Any | 2026-10-06 | No task, commit, branch, or own plan. 2026-10-08 triage: keep. | No implementation has started. |
| CARD-1172 | NO-WORK | Normal/Normal | Any | 2026-10-09 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1212 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1214 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1215 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1216 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1218 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1220 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1224 | NO-WORK | Normal/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1227 | NO-WORK | Low/Normal | Any | 2026-10-13 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1304 | NO-WORK | Normal/Normal | Any | 2026-10-16 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1309 | NO-WORK | Normal/Normal | Any | 2026-10-16 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1429 | NO-WORK | Normal/Normal | Any | 2026-10-21 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1437 | NO-WORK | Normal/Normal | Any | 2026-10-21 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1443 | NO-WORK | Normal/Normal | Any | 2026-10-21 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1444 | NO-WORK | Normal/Normal | Any | 2026-10-21 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1445 | NO-WORK | Normal/Normal | Any | 2026-10-21 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1460 | NO-WORK | Low/Normal | Any | 2026-10-22 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1468 | NO-WORK | Normal/Normal | Any | 2026-10-22 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1487 | NO-WORK | Low/Normal | Any | 2026-10-22 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1588 | NO-WORK | Normal/Normal | Any | 2026-10-26 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1592 | NO-WORK | Normal/Normal | Any | 2026-10-27 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1611 | NO-WORK | Normal/Normal | Any | 2026-10-29 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1642 | NO-WORK | Normal/Soon | Any | 2026-11-02 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1643 | NO-WORK | Normal/Soon | Any | 2026-11-02 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1644 | NO-WORK | Normal/Soon | Any | 2026-11-02 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1646 | NO-WORK | Normal/Normal | Any | 2026-11-02 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1750 | NO-WORK | High/Soon | Any | 2026-11-07 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1752 | NO-WORK | High/Normal | Linux | 2026-11-07 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1786 | NO-WORK | Normal/Normal | Any | 2026-11-08 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1824 | NO-WORK | Low/Normal | Any | 2026-11-09 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1829 | NO-WORK | High/Soon | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1830 | NO-WORK | High/Soon | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1837 | NO-WORK | Normal/Normal | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1838 | NO-WORK | High/Soon | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1839 | NO-WORK | Normal/Normal | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1840 | NO-WORK | Normal/Normal | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |
| CARD-1841 | NO-WORK | Normal/Normal | Any | 2026-11-10 | No task, commit, branch, or own plan. Not in the 2026-10-08 triage. | No implementation has started. |

## Not done, noted

October probably-done rows still on Backlog (CARD-0278, CARD-0380, CARD-0605, CARD-0990) were left UNSURE instead of being re-proved against current source.
