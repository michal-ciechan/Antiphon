# Review column triage (53 cards)

- Date: 2026-10-08. Stage: Investigate (task eff1e7f7). Read-only: no card, column, label, task or code was changed. The private-notes route was not read.
- Data, read 2026-10-08 09:46-09:58Z:
  - `GET /api/boards/8988ca03-7414-47ad-b0b6-51556c701703`: the Review column has **53** cards. The brief said 52; CARD-1143 arrived at 09:46Z.
  - `GET /api/cards/{id}/revisions` for all 53 cards.
  - `GET /api/agent-tasks?boardId=...` plus `GET /api/agent-tasks/{id}` for all 402 tasks bound to those cards (landing record, result, next stage).
  - `GET /api/session-runners`, `GET /api/hosts`, `GET /api/version`, and `GET /api/projects/{antiphon}`.
- Master: `origin/master` at `05252daf5`. Whether a task's work landed was decided by the landing record (`publication=Landed`, `remoteSha`) and by `git merge-base --is-ancestor`. Lands rebase, so where a task has no landing record, its reported progress commit was matched to master by author time and subject (patch identity). File:line citations are against `origin/master`.
- **The live server is `b5e78700a`, 77 commits behind master** (`GET /api/version`, 09:56Z). Anything landed after about 2026-10-07 15:15Z is not active yet.
- Machine-readable copy: [`2026-10-08-review-column-triage.json`](2026-10-08-review-column-triage.json), one object per card with these fields: `identifier`, `title`, `asked`, `landed`, `open`, `disposition`, `confidence`, `reason`, `closingVerdict`, `moveWhen`, `decisionNeeded`, `relatedCards`, `onlyOpen`, `boardStateIssue`.

## Why so many cards sit in Review

The mechanism is confirmed from source and from every card's revision history.

- `CardWorkTransitionService.Decide` (`server/Application/Services/CardWorkTransitionService.cs:176-206`) moves a card to **Review** whenever its newest settled task is `Succeeded` and no task is open. It does this for **any role**.
- An "open" task means Dispatched, Working or Blocked. Queued is not open (lines 34-37).
- Nothing moves a card out of Review except a new dispatch (to In Progress) or a manual move or close.

So in practice Review means "the last task finished cleanly and the orchestrator has not decided yet". It does not mean "awaiting code review".

These are the roles of the last Succeeded task on the 53 cards:

| Role | Cards |
|---|---:|
| Review | 23 |
| Plan | 7 |
| Code | 5 |
| Debug | 5 |
| TestDesign | 5 |
| Investigate | 4 |
| Deploy | 2 |
| Test | 1 |
| none (CARD-0512, moved by an older run-attempt rule) | 1 |

The `next=` values of those last tasks:

| `next=` | Cards |
|---|---:|
| `land` | 21 |
| `code` | 7 |
| `decide` | 5 |
| `review` | 4 |
| `test-design` | 4 |
| `none` | 3 |
| `plan` | 1 |
| `mutation` | 1 |
| (no next-stage block) | 6 |

Most `land` handoffs were landed, but no close followed. Closing is a manual step that nobody took for the older cards.

## Summary

| Recommended disposition | Cards |
|---|---:|
| close-done | 24 |
| needs-operator-decision | 12 |
| move-to-backlog | 11 |
| keep-in-review | 4 |
| move-to-in-progress | 2 |
| duplicate / merge-into | 0 (but see CARD-0598 below) |

**Closeable now with high confidence (17).** The verdict text for each is in the per-card section and the JSON.

- CARD-0604, CARD-0657, CARD-0675, CARD-0718, CARD-0826, CARD-0905, CARD-1031, CARD-1066, CARD-1074
- CARD-1076, CARD-1079, CARD-1103, CARD-1113, CARD-1129, CARD-1133, CARD-1135, CARD-1145

**Closeable with medium confidence (7).** Each has a small stated gap:

- **CARD-0512:** an investigation record; its follow-ups were never filed.
- **CARD-0655:** part 2, deploy evidence, is not done (`docs/logs.md:90`). Split it to a new card.
- **CARD-0698** and **CARD-0712:** the live measurement was never recorded.
- **CARD-0889:** fixed under CARD-0742 `c345371e2`.
- **CARD-1097:** items 2-5 are unaddressed, and item 1 is not live until the restart.
- **CARD-1104:** the remote-parent receipt is a documented gap.

**Waiting on an operator decision (12):**

| Card | Decision needed |
|---|---|
| CARD-0511 | Reviewed clean at `63cab35e7` on 2026-09-18 but **never landed**; the branch is 3,605 commits behind. Rebase and re-review, re-plan, or close. |
| CARD-0519 | Commission the final one-SHA requalification (S12e/f/g) and the Unit run, and choose when to enable `ChannelOutbound:UnifiedRecoveryEnabled`. Or close as "landed dormant" and track activation on a new card. |
| CARD-0552 | Keep (re-plan) or shelve the tracked-Mutation stage while Mutation is paused. Also decide what to do with the orphan test-only branch `feat/card-task-e67555ad`. |
| CARD-0599 | Authorise the S5/S6 live activation round (Hangfire RC cut and publication, release cards), or close as mechanism-landed and file S5/S6 separately. |
| CARD-0817 | A human mounts the PAT file on server2 and server2-temp (`docs/agent-credentials.md`), then one markdown-package task runs on server2 to prove the push. No markdown-package task has run on server2 since 2026-10-06. |
| CARD-0907 | The deploy gate lapsed (Codex 0.160.0 is live on desktop and server2-temp). Re-target it as a parser-qualification Backlog card, or close as superseded. |
| CARD-1023 | Accept or revise plan D-1 (observation first, admission later) and D-3 (the Sol 0.159.1 seed row). The plan exists only on `feat/card-task-63057c79`. |
| CARD-1039 | Continue (fix the shared-process timeouts found by Debug 7951fe34, then register the Windmill job), switch to Hangfire alongside CARD-0599 S6, or park. Two Code tasks failed. |
| CARD-1065 | When to set `BlockedTaskParking:Enabled=true`; follow-ups CARD-1136/1137/1138/1149/1150/1151 are still In Progress. Or close as landed-dormant. |
| CARD-1068 | Settle plan D-1 (explicit image build and import before Fixture) and D-5 (cleanup acceptance semantics). Also approve the two `sudo rm` residue cleanups named in the card. |
| CARD-1109 | Confirm D-1 (runner-attested ancestry) and D-9 (wait one week after CARD-1082 activation and measure, so about 2026-10-14). |
| CARD-1148 | Authorise an additive migration for a worktree registration id, or keep the F3d no-supersession behaviour and park. |

**Only open item is post-land Mutation (12).** Mutation is paused, so these can close now with "PCs pending Mutation" in the verdict:

- CARD-0604 (also has residual CARD-0634), CARD-0657, CARD-0675, CARD-0718 (also has an unrecorded CP-11 live receipt), CARD-1076
- CARD-1079, CARD-1103, CARD-1113, CARD-1129, CARD-1133, CARD-1135, CARD-1145

**Only open item is a restart or activation.**

- **CARD-1153:** landed `394229df9` at 07:49Z, after the served build. Activate in order: runner first, then AppHost.
- **CARD-1097:** item 1 (`48e9e7495`, `db4350ff3`) is not in `b5e78700a`; an AppHost restart activates it.
- **CARD-1087:** the activation is a real rollout run, not a restart. The code is live, but redeploy-old has not been rerun: server2 main is `draining`, server2-temp holds 10/10 seats, and the Antiphon project's `gitRepositoryUrl` is still `""`, so pass `-ProjectId` (`docs/docker-stack.md:31-34`).
- **CARD-0817 and CARD-1065** are activation-only too, but the activation is a human or operator decision; they are listed above.

**Only open item is a measurement:** CARD-0698 (pg_stat delta), CARD-0712 (DISCARD ALL rate), CARD-1040 (the S2 jq run; jq is now present at `/usr/local/bin/jq` on server2-temp).

**Board state that looks wrong:**

1. **CARD-0511:** a clean Final Review said land on 2026-09-18 and the land never happened. Nothing on the board or in attention tracks it.
2. **Succeeded Code with `next=review`, never reviewed for 4 days:**
   - CARD-0262: `f9183542` at `f6c13e173`.
   - CARD-0701: `4a97e419` at `20ba8a291`.
   - CARD-0552: `e67555ad`, test-only and never reviewed, since 2026-09-17.
3. **Owner tasks that are Failed or Canceled but carry a Landed publication:**
   - CARD-0519: `5724b53e`, `6ff18828`, `e739d9aa`, `170e6f0d`, `23e91309`.
   - CARD-1066: `d73d91aa`.
   - CARD-1065: `bc986d96`, `f0816532`, `24c08059`.

   Each is a recovery or adoption land onto an owner that had already settled. The card rule ignores them (CardWorkTransitionService.cs:194-199), so they are harmless to the column, but the task list reads as failed work.
4. **CARD-1143** shows Review while its Final Review `b44ef513` is Queued. Queued is not open by design (CardWorkTransitionService.cs:34-37), so the card will flip to In Progress only when that Review dispatches.
5. **Plan or TestDesign artifacts that exist only on unlanded branches**, so master has no copy:
   - CARD-0416 (`feat/card-task-31ea7c27`)
   - CARD-0491 (`feat/card-task-298f4c9f`)
   - CARD-0701 (the 2026-10-04 re-plan)
   - CARD-0889 (four 2026-10-04 docs)
   - CARD-0997 (`feat/card-task-cdd8d229`)
   - CARD-1010 (`feat/card-task-fd423b29`)
   - CARD-1023 (`feat/card-task-63057c79`)
6. **12 landings stuck in `cleanup=CleanupStarted`.** The reasons are `ignored_content_preserved`, `evidence_retention_unavailable` and `cleanup_additional_budget_expired`. The affected landings belong to CARD-0262, 0407, 0519 (×2), 0599, 0604 (×5), 0655 and 1039.
7. **CARD-0598 (Backlog).** CARD-0604 operator decision 4 says close CARD-0598 as a duplicate once Cut B lands. Cut B landed at `741c8c0eb` on 2026-09-23, and Linux custody code is on master (`src/Antiphon.SessionRunner/RunnerCustodyLedger.cs`, `docker/session-runner-grok/antiphon-custody-enter.sh`). The 2026-10-08 backlog triage row "Linux SourceLanding custody backend still absent" contradicts this. Confidence medium: CARD-0634's DinD residuals are still open.
8. **CARD-0907**, a pre-deploy gate, is still open after the deploy it gated (CARD-0904, Done).

## All 53 cards at a glance

| Card | Title | Updated | Disposition | Conf. | Reason |
|---|---|---|---|---|---|
| CARD-0262 | A user preference stored in KB does not reach the agent's own instructions | 2026-10-04 | move-to-in-progress | medium | Active multi-slice feature; the owed next step is the Review of f9183542 at f6c13e173, idle since 2026-10-04. |
| CARD-0407 | A blocked 'may I continue this internal fix' should be a Question that auto-continues when | 2026-09-28 | move-to-backlog | high | Half-built, inert feature with no activity since 2026-09-28; nothing user-visible ships until S3. |
| CARD-0416 | Prove the Grok rules refresh fires across two genuine auto-compactions (split from CARD-03 | 2026-09-27 | move-to-backlog | high | Operator explicitly deferred it on 2026-09-27; no defect is open. |
| CARD-0479 | Make Mutation execution cheap-model-capable via mechanically explicit PC specs | 2026-09-10 | move-to-backlog | medium | Untouched since 2026-09-10 and it optimises a stage the operator has paused. |
| CARD-0491 | Find a way to interrupt/nudge a Grok (or any) delegate mid-turn without killing it | 2026-09-27 | move-to-backlog | medium | Plan-only, no work since 2026-09-27 and no operator direction to continue. |
| CARD-0505 | Configurable dispatch concurrency: parallel + queued limits per stage/project | 2026-10-01 | move-to-backlog | high | Design is frozen and ready; no Code was ever dispatched. CARD-0881 and CARD-0822 are sequenced behind it. |
| CARD-0511 | Restart loop: SessionRunner/Server capability desync after sessionGenerationV1 (2026-09-13 | 2026-09-18 | needs-operator-decision | high | Reviewed-clean work was abandoned at the land step; it is too stale to land as-is. |
| CARD-0512 | Investigated: a Bash tool_use with no tool_result wedged a session; the 120s harness timeo | 2026-09-20 | close-done | medium | The investigation is complete and the card is an investigation record. The follow-ups need their own cards if still wanted. |
| CARD-0519 | Channel outbound: refused/crashed publish can silently strand a reply forever | 2026-10-06 | needs-operator-decision | medium | All code has landed and is dormant. What is left is final qualification and an activation choice. |
| CARD-0552 | Make Mutation a properly tracked pipeline stage, not an ad-hoc post-land dispatch | 2026-09-18 | needs-operator-decision | medium | It designs auto-dispatch for a stage the operator has paused; the only Code output is orphaned tests. |
| CARD-0599 | Release-gate model: lighter per-change tests + periodic full-test RC releases | 2026-09-24 | needs-operator-decision | high | The code half is done; the remaining half is explicitly operator-authorised activation. |
| CARD-0604 | Rework CARD-0590 server2 stack into persistent-runner DinD shape (not sibling compose) | 2026-09-23 | close-done | high | Both cuts landed and are live; the only residual has its own card. |
| CARD-0655 | server2: recurring phone-home HTTP 502 reconnects; deploy evidence not retained on server2 | 2026-09-24 | close-done | medium | The defect in the title is fixed and verified live; part 2 is a separate ops feature that should be its own card. |
| CARD-0657 | Runner-bound tasks marked Failed (unclaimed_or_unmatched_commit): progress check reads the | 2026-09-24 | close-done | high | Landed and active; superseded and extended by CARD-1082. |
| CARD-0675 | Landing a -StartRef repair needs a manual force-push of the owner branch (no adopt-repair  | 2026-09-30 | close-done | high | Landed and exercised in production repeatedly. |
| CARD-0692 | Post-land Cleanup stage: retire all task worktrees, branches, runner worktrees and session | 2026-09-29 | move-to-backlog | medium | The one-off reclaim is done; the requested stage was never designed. |
| CARD-0698 | antiphon-postgres burns ~5.6 cores: TranscriptEntries polled by full seq scan ~36x/s | 2026-09-25 | close-done | medium | Landed and active since 2026-09-25 with fixture proof; only the live measurement is missing. |
| CARD-0701 | Cache session working state in memory (ingest-updated, event-driven) instead of re-derivin | 2026-10-04 | move-to-in-progress | medium | The next owed step is the Review of S0 (Code 4a97e419 at 20ba8a2916f7c8ada05d31a97648e79051c89a07); idle since 2026-10-04. |
| CARD-0707 | A Blocked task from another project (worktree gone) holds every Antiphon land as a reposit | 2026-09-25 | move-to-backlog | high | A real, unfixed bug with a confirmed mechanism; no work since the Debug. |
| CARD-0712 | Stop Npgsql DISCARD ALL per pooled connection (No Reset On Close) after session-state safe | 2026-09-25 | close-done | medium | Landed and active; only the measurement is missing. |
| CARD-0718 | Host stats: 5s CPU/memory/load + parallel tasks per host, in-memory 1/5/15/30-min rollups, | 2026-09-26 | close-done | high | Landed and active since 2026-09-26; the open item is a recorded live receipt. |
| CARD-0817 | server2 runner: push credential for non-Antiphon repositories | 2026-10-06 | needs-operator-decision | high | Code is landed; the remaining step is a human credential action. |
| CARD-0822 | Live orchestrator instructions file: generated from settings, re-read on start/compact, pu | 2026-09-29 | move-to-backlog | high | Plan ready, nothing built, and blocked behind CARD-0505 and CARD-0881. |
| CARD-0826 | Daily per-host cleanup of worktrees, work volume and temp older than 24 hours | 2026-10-02 | close-done | high | Its remaining scope already lives in CARD-0987. |
| CARD-0881 | One effective-settings endpoint for orchestrators (concurrency limits, runner capacity, oc | 2026-10-01 | move-to-backlog | high | Ready for Code but explicitly ordered after CARD-0505, which has not started. |
| CARD-0889 | RunnerCodexAdapterSubmitConfirmTests fail under 24-burner CPU load (2 s confirmation budge | 2026-10-04 | close-done | medium | The card's own defect is fixed on master; the widened scope belongs to other cards. |
| CARD-0905 | WSL lacks pwsh so 5 RemoteScriptContractTests C849_* tests fail on the Windows desktop | 2026-10-02 | close-done | high | Landed; the follow-on defect is closed under CARD-0980. |
| CARD-0907 | Capture Codex 0.160.0 TUI screens and verify readiness/trust/done/update/usage-limit parse | 2026-10-02 | needs-operator-decision | medium | It was a pre-deploy gate and the deploy happened without it. The parser coverage risk remains, but the urgency is gone. |
| CARD-0997 | C849 RemoteScriptContractTests depend on real host free disk (>=20 GiB CacheDiskLow gate): | 2026-10-04 | move-to-backlog | high | Ready for Code, small, and still a live source of host-dependent reds. |
| CARD-1010 | Rolling recycle opt-ins -RecycleRunnerState / -RecycleCaches (D-6 maintenance proofs, leas | 2026-10-04 | move-to-backlog | high | Optional opt-ins; the default recycle is in CARD-1008. Blocked behind CARD-1030. |
| CARD-1023 | Compatibility matrix (CLI version x model x session-runner/pty-host/backend x host) replac | 2026-10-03 | needs-operator-decision | high | The plan explicitly stops for an operator decision. |
| CARD-1031 | Codex CLI version probe reports unknown (stderr_output) when codex --version writes to std | 2026-10-04 | close-done | high | Landed, active, and verified live. |
| CARD-1039 | Daily whole-Unit run: activate and verify the existing Windmill nightly | 2026-10-04 | needs-operator-decision | medium | Stalled after two failed Code attempts. Activation needs an operator action and a choice of mechanism (Windmill nightly here versus the CARD-0599 Hangfire direction). |
| CARD-1040 | Unit timeouts/inherited failures: resolve CARD-1021 jq preconditions and measure limits | 2026-10-05 | keep-in-review | medium | One concrete measurement run is left and its precondition now holds. |
| CARD-1065 | Blocked tasks hold runner seats indefinitely; park must push WIP and release the seat | 2026-10-06 | needs-operator-decision | medium | Left open on purpose tonight; everything has landed dormant and the remaining choice is when to enable parking. |
| CARD-1066 | Fixture PC-12 aborts on unset fault under set -u | 2026-10-05 | close-done | high | Its defect is fixed; the next blocker has its own card. |
| CARD-1068 | Fixture gate smokes the old 0744 runner image and leaves cleanup residue | 2026-10-05 | needs-operator-decision | high | The plan stops for D-1 and D-5. |
| CARD-1074 | AgentTaskDispatcher: interrupted dispatch loses its brief and a launch enqueue refusal aft | 2026-10-07 | close-done | high | Both named defects are fixed and the residuals are tracked on their own cards. |
| CARD-1076 | Remote-prep git push killed at 5 minutes serially fences lease-taking dispatch | 2026-10-06 | close-done | high | Landed and active. |
| CARD-1079 | Alert on idle runner seats, host occupancy above Working count, and orphan slots (detectio | 2026-10-06 | close-done | high | Landed and active. |
| CARD-1087 | Rolling redeploy-old stops with RecycleTaskCensusUnknown: empty project URL, 90 s census v | 2026-10-06 | keep-in-review | high | The code is landed; the proof is the next real rollout phase, which the orchestrator may run autonomously. |
| CARD-1097 | CARD-1065 S7 follow-ups: dedupe park_resume_refused warnings and read runner mirror identi | 2026-10-07 | close-done | medium | The card's priority item is landed. The rest are hardening disclosures that need their own Backlog card, not this one held open. |
| CARD-1103 | CARD-1065 parked follow-up guidance: scope HasConfirmedPublishedParkAsync to the attempt a | 2026-10-07 | close-done | high | Landed and active; the related disclosures are closed. |
| CARD-1104 | CARD-1065 V-27: prove the parked caller receipt on the inbox pointer path (default PtySing | 2026-10-07 | close-done | medium | The ask (prove the pointer path or document why not) is met for local parents, and the remote gap is documented as a known limit. |
| CARD-1109 | CARD-1082 residual: Code task whose commits were never fetched during the lease budget sti | 2026-10-06 | needs-operator-decision | high | The plan stops for D-1/D-9 and itself asks for a measurement first. |
| CARD-1113 | CARD-1082 S1 disclosure: pin SettlementSyncDebtPolicy inputs (FullRef, null evidence, Pend | 2026-10-07 | close-done | high | All three hardenings are in place and active. |
| CARD-1129 | CARD-1108 S2 disclosures: scheduled sweep recounts already-parked rows as released; once-p | 2026-10-07 | close-done | high | Landed (via the CARD-1145 land) and active. |
| CARD-1133 | CARD-1082 S5 disclosure: Blocked lease-busy settlements (unfetched Code, bind-refusal reco | 2026-10-07 | close-done | high | Landed and active. |
| CARD-1135 | CARD-1108 S3 disclosures: a Held park refused before PersistIntent is not re-stamped; the  | 2026-10-07 | close-done | high | Landed and active; the residual has its own card. |
| CARD-1143 | CARD-1135 residual: a Held park whose episode is no longer loadable is visited on every sw | 2026-10-08 | keep-in-review | high | Work is in flight; the Review is queued. |
| CARD-1145 | CARD-1129 reclaim counter drops new confirmations after a backward clock step | 2026-10-07 | close-done | high | Landed and active; the follow-up is closed. |
| CARD-1148 | Bind Held sync debt to a worktree registration id before superseding | 2026-10-07 | needs-operator-decision | high | The plan stops on a schema/migration authorisation. |
| CARD-1153 | Runner absence-evidence API so the CARD-1149 absent-launch hold works for a real unknown s | 2026-10-08 | keep-in-review | high | Landed; the only open step is the pending runner and AppHost restart. |

## Per-card detail

### CARD-0262: A user preference stored in KB does not reach the agent's own instructions

- **Asked:** Make a stored user preference reach the agent's own instructions; operator redesign to per-agent pinned instructions written to an Antiphon-owned antiphon.md (always created, per-agent-unique path) with re-read on change.
- **Landed on master:** S1 pin store/API b4ab99c2c + 8ccdb1c93 (2026-09-13); dormant composition core 6283122ed and S3a.1 native pin inspection (landing 9bce87ed9, 06483402f, 2026-10-04). Nothing is wired: AgentPinWorkspaceStore is unregistered in Program.cs (Review 18a3e6bc).
- **Still open:** S3a.2 (publication/custody/fencing) is pushed at f6c13e173 on feat/card-task-f9183542 with CP-29 11/11 but was never reviewed or landed (not on master). Then S3a reconciliation, transports, Windows rows CP-5/6/8/16/22-27, later slices, whole Unit, PC-1..207.
- **Recommendation:** `move-to-in-progress` (medium). Active multi-slice feature; the owed next step is the Review of f9183542 at f6c13e173, idle since 2026-10-04.
- **What moves it:** Dispatch Review of Code f9183542 at f6c13e1738d3a39abe15fc5c37087cd2d660817f, land it, then the next S3a Code slice. If the operator no longer wants this feature now, move to Backlog instead.
- **Board state:** Succeeded Code f9183542 (next=review) never reviewed; card sat in Review for 4 days with the Review not dispatched.

### CARD-0407: A blocked 'may I continue this internal fix' should be a Question that auto-continues when there is no end-user impact

- **Asked:** A 'may I continue this internal fix' go-ahead should be a Question that auto-continues when there is no end-user impact.
- **Landed on master:** S1 grant contract (landing 70b5a188f, 2026-09-13) and S2a structured decision check (landing a75ccdfd1, 2026-09-28). Both inert by design: no code writes AutoContinuedAt (Review 8a7132cb).
- **Still open:** Four planned Code slices S2b, S3, S4a, S4b (docs/superpowers/plans/2026-09-08-card-0407-internal-decision-authority-plan.md:407-450); a Review suggestion to split the retryable lock-contention 409 from decision_question_stale before S3; all PCs.
- **Recommendation:** `move-to-backlog` (high). Half-built, inert feature with no activity since 2026-09-28; nothing user-visible ships until S3.
- **What moves it:** Resume with Code S2b on landed master when prioritised.
- **Related:** CARD-0294

### CARD-0416: Prove the Grok rules refresh fires across two genuine auto-compactions (split from CARD-0395)

- **Asked:** Prove the Grok rules refresh fires across two genuine auto-compactions (endurance gate split from CARD-0395).
- **Landed on master:** Nothing. The Investigate plan (63e4f4ded, docs/superpowers/plans/2026-09-27-card-0416-plan.md) exists only on feat/card-task-31ea7c27; it is not on master.
- **Still open:** One authenticated Windows rerun of the existing two-arm acceptance class (about 25 min ordinary, 120 min ceiling per arm). Operator 2026-09-27: delay, lowest priority.
- **Recommendation:** `move-to-backlog` (high). Operator explicitly deferred it on 2026-09-27; no defect is open.
- **What moves it:** Land the plan from feat/card-task-31ea7c27 when picked up.
- **Board state:** Plan artifact only on an unlanded branch.
- **Related:** CARD-0395

### CARD-0479: Make Mutation execution cheap-model-capable via mechanically explicit PC specs

- **Asked:** Make Mutation execution cheap-model-capable via mechanically explicit PC specs (literal diff, expected failure, revert).
- **Landed on master:** Plan only: 717f5cfa4 docs/superpowers/plans/2026-09-10-card-0479-mechanical-pc-eligibility-plan.md.
- **Still open:** TestDesign, Code (bundle and routing changes), all of it; depends on CARD-0478 runtime. Mutation is currently paused by the operator.
- **Recommendation:** `move-to-backlog` (medium). Untouched since 2026-09-10 and it optimises a stage the operator has paused.
- **What moves it:** When Mutation is un-paused, run TestDesign on the landed plan.
- **Related:** CARD-0478, CARD-0552

### CARD-0491: Find a way to interrupt/nudge a Grok (or any) delegate mid-turn without killing it

- **Asked:** Find a way to interrupt or nudge a Grok (or any) delegate mid-turn without killing it.
- **Landed on master:** Nothing. The plan (a3c4d26b4, docs/superpowers/plans/2026-09-27-card-0491-plan.md, opt-in conditional Ctrl+C after a real Grok canary) exists only on feat/card-task-298f4c9f.
- **Still open:** Land the plan, then TestDesign, Code, and a real Grok canary.
- **Recommendation:** `move-to-backlog` (medium). Plan-only, no work since 2026-09-27 and no operator direction to continue.
- **What moves it:** Land the plan from feat/card-task-298f4c9f, then TestDesign.
- **Board state:** Plan artifact only on an unlanded branch.
- **Related:** CARD-0472

### CARD-0505: Configurable dispatch concurrency: parallel + queued limits per stage/project

- **Asked:** Configurable dispatch concurrency: settable, audited parallel and queued limits per stage and project (the enforcement half; CARD-0881 is the read half).
- **Landed on master:** Plan 08b7b100c and TestDesign b39832470 (54 tests, CP-1/2/3 = 38/85/77).
- **Still open:** All Code S1-S3. TestDesign's prerequisites (CARD-0778) are now Done.
- **Recommendation:** `move-to-backlog` (high). Design is frozen and ready; no Code was ever dispatched. CARD-0881 and CARD-0822 are sequenced behind it.
- **What moves it:** Dispatch Code S1 (recount the census first; master has moved since 2026-09-30).
- **Related:** CARD-0881, CARD-0506, CARD-0822

### CARD-0511: Restart loop: SessionRunner/Server capability desync after sessionGenerationV1 (2026-09-13, distinct from CARD-0509)

- **Asked:** Restart loop from SessionRunner/Server capability desync (sessionGenerationV1): stop caching a stale runner capability belief.
- **Landed on master:** Nothing. Code f1139c44 (S1-S4: fresh runner probe per launch, durable runner-build hold) was Final-Reviewed clean at 63cab35e7 on 2026-09-18 (Review 79253363, next=land, restart required) but never landed. git cherry shows all 6 commits absent from master; the branch is 3,605 commits behind origin/master.
- **Still open:** Land (now needs a rebase and fresh Review), restart AppHost (applies migration AddRunnerBuildHold), PC-511-1..24.
- **Recommendation:** `needs-operator-decision` (high). Reviewed-clean work was abandoned at the land step; it is too stale to land as-is.
- **Decision needed:** Rebase f1139c44 onto master and re-review, or re-plan from current master, or close as no longer reproducing. RunnerCapabilityMismatchException still exists on master (server/Application/Exceptions/RunnerCapabilityMismatchException.cs).
- **Board state:** Review said land at 63cab35e7 on 2026-09-18; the land never happened and nothing tracks it.
- **Related:** CARD-0509

### CARD-0512: Investigated: a Bash tool_use with no tool_result wedged a session; the 120s harness timeout failed to fire (2026-09-13)

- **Asked:** Investigate a session wedged on a Bash tool_use that never got a tool_result (2026-09-13).
- **Landed on master:** The investigation itself is recorded in the card body (2026-09-13): neither hypothesis reproduced on Claude Code 2.1.266; the real defect is the harness's 120 s backgrounding failing to fire once (1 of 1,625 tool_use entries). No code was expected.
- **Still open:** Its recommendations were never filed as cards: a supervisor watchdog for 'working with no transcript entry for N minutes' (restart, not kill), upstream reports (orphaned backgrounded child; missing tool_result), and a client timeout on POST /api/sessions/{id}/messages.
- **Recommendation:** `close-done` (medium). The investigation is complete and the card is an investigation record. The follow-ups need their own cards if still wanted.
- **Closing verdict:** Investigated 2026-09-13; no code change was in scope. Neither hypothesis reproduced on Claude Code 2.1.266. The 120 s auto-background fired in every controlled repro, and external kills and Ctrl+C closed the tool call within about 15 s. The incident was a single tool_use (toolu_019PEwuD4s5ax33gCZfJD57E) with no tool_result where the harness timeout itself never fired (1 of 1,625 tool calls). Open follow-ups, not done here: a silent-Working watchdog that restarts, upstream reports, and a client timeout on POST /api/sessions/{id}/messages.
- **Related:** CARD-0514

### CARD-0519: Channel outbound: refused/crashed publish can silently strand a reply forever

- **Asked:** A refused or crashed channel outbound publish can silently strand a reply forever; make every reply recoverable.
- **Landed on master:** Unified outbound recovery S1-S13 behind ChannelOutbound:UnifiedRecoveryEnabled=false (17 landings; last 28b66c20c S13, 2026-10-06). The generic dispatcher fixes it found (CARD-1074) landed in 6e04c87f3. docs/telegram.md:56 documents the switch as off.
- **Still open:** Final qualification at one frozen SHA: S12f failed CP-72 on a registry omission (ff6afff6), since repaired on master (tests/Antiphon.Tests/slow-tests-allowlist.txt:262,268), but S12e/f/g were never rerun together. Caller-owned whole-Unit qualification (D-S12-7). All PCs (PC-1..100 plus slice variants). The decision to turn UnifiedRecoveryEnabled on.
- **Recommendation:** `needs-operator-decision` (medium). All code has landed and is dormant. What is left is final qualification and an activation choice.
- **Decision needed:** Commission the final requalification (S12e/f/g at one SHA plus one Linux Unit run) and decide when to enable ChannelOutbound:UnifiedRecoveryEnabled; or close as 'landed dormant' and move activation to a new card.
- **Board state:** Owner tasks 5724b53e, 6ff18828, e739d9aa, 170e6f0d and 23e91309 are Failed but each carries a Landed publication; Plan 02c16198's land was Refused (rebase_conflict).
- **Related:** CARD-1074, CARD-0593

### CARD-0552: Make Mutation a properly tracked pipeline stage, not an ad-hoc post-land dispatch

- **Asked:** Make Mutation a tracked pipeline stage (durable Mutation debt, auto-dispatch policy, survivor handling, terminal marker).
- **Landed on master:** Investigation and plan docs only (6bda48d3a, 0d7765c1b, 7fc575c96). No production code: MutationAutoDispatchSweep and VerificationCardId do not exist under server/ on master.
- **Still open:** Code e67555ad 'recovered from an unbound session' left five test-only commits (f967cc84..2c483e0d) on feat/card-task-e67555ad. They were never reviewed or landed and are now very stale. All production work remains.
- **Recommendation:** `needs-operator-decision` (medium). It designs auto-dispatch for a stage the operator has paused; the only Code output is orphaned tests.
- **Decision needed:** Keep (re-plan against current master) or shelve while Mutation is paused; discard or keep the orphan test branch feat/card-task-e67555ad.
- **Board state:** Succeeded Code e67555ad has no review and no land; its commits are not on master.
- **Related:** CARD-0478, CARD-0479

### CARD-0599: Release-gate model: lighter per-change tests + periodic full-test RC releases

- **Asked:** Release-gate model: lighter per-change tests plus periodic full-test RC releases with tags, GitHub Releases and a release-card workflow.
- **Landed on master:** Mechanism S1-S4 plus B1 publisher authority, dormant (e.g. 294e2f6c9, 65cc0f628, f9092a219..ed326d92d). docs/release-gates.md:9-10 says no qualification receipt, published tag or Interim pilot exists, and CARD-0599's acceptance stays open until S5/S6.
- **Still open:** S5 (live qualification and pilot) and S6 (Hangfire-scheduled RC cut and publication, release cards on the shared board). The operator reserved both for a separately authorised round (2026-09-22). git tag lists nothing.
- **Recommendation:** `needs-operator-decision` (high). The code half is done; the remaining half is explicitly operator-authorised activation.
- **Decision needed:** Authorise the S5/S6 activation round now, or close CARD-0599 as mechanism-landed and file S5/S6 as a new card.
- **Related:** CARD-1039, CARD-0614

### CARD-0604: Rework CARD-0590 server2 stack into persistent-runner DinD shape (not sibling compose)

- **Asked:** Rework the CARD-0590 server2 stack into a persistent runner (DinD, phone-home) with a Linux custody backend (Cut B, folding CARD-0598).
- **Landed on master:** Cut A (landings ddb5d0f0d, d65047cd4, 6d90c6fcf, e992897cf) and Cut B custody (741c8c0eb, 2026-09-23). The persistent server2 runners are in production use: this task runs on server2-temp. Linux custody code is on master (src/Antiphon.SessionRunner/RunnerCustodyLedger.cs, docker/session-runner-grok/antiphon-custody-*.sh).
- **Still open:** Residual CP-14/15/17 DinD harness failures, already carded as CARD-0634 (Backlog). PCs pending Mutation.
- **Recommendation:** `close-done` (high). Both cuts landed and are live; the only residual has its own card.
- **Closing verdict:** Cut A (persistent runner, phone-home, remote tasks) and Cut B (Linux cgroup custody) landed by 741c8c0eb (2026-09-23); server2 and server2-temp have run delegated tasks since. Corrected along the way: D-2 moved to phone-home with no server or Postgres on server2. Still open: CP-14/15/17 DinD harness residuals (CARD-0634) and PCs pending Mutation. Per operator decision 4 (2026-09-22), CARD-0598 should now be closed as a duplicate of this card.
- **Board state:** CARD-0598 is still in Backlog although operator decision 4 says close it as duplicate when Cut B lands. The 2026-10-08 backlog triage calls it 'still absent', which contradicts master.
- **Related:** CARD-0634, CARD-0598

### CARD-0655: server2: recurring phone-home HTTP 502 reconnects; deploy evidence not retained on server2

- **Asked:** (1) Find and fix the recurring server2 phone-home HTTP 502 reconnects; (2) keep durable server2 deploy evidence.
- **Landed on master:** (1) Root cause: the runner event-hub overflow counter never decremented. Fixed in 00a86743c and f3b5a62fb (landing f3b5a62fb), deployed, and verified by Deploy d869b193 (2026-09-24 13:16-13:19Z: 0 overflow lines, epoch stable).
- **Still open:** (2) The durable deploy-evidence copy was never built. docs/logs.md:90 still records 'no retrievable durable server2 deployment-evidence source' and proposes the fix.
- **Recommendation:** `close-done` (medium). The defect in the title is fixed and verified live; part 2 is a separate ops feature that should be its own card.
- **Closing verdict:** Part 1 fixed: the runner event-hub subscriber counted every publish and never released on consume, so each connection died after about 1,024 events. Fixed in 00a86743c and f3b5a62fb, deployed 2026-09-24, and verified with 0 overflow lines and a stable epoch. Part 2, durable server2 deploy evidence under /home/mc/antiphon-server2/evidence/, is not done (docs/logs.md:90); split it to a new Backlog card.

### CARD-0657: Runner-bound tasks marked Failed (unclaimed_or_unmatched_commit): progress check reads the desktop worktree, but the commits are on the runner mirror and origin

- **Asked:** Runner-bound tasks were marked Failed because settlement read the desktop worktree instead of the runner's pushed branch.
- **Landed on master:** S1/S2 plus R1-R4 repairs, landing 87f261ac7 (2026-09-24). This was later extended by the CARD-1082 settlement sync-debt work (Done).
- **Still open:** PCs pending Mutation only. The 'Restart: server' activation has long since happened (live server b5e78700a contains it).
- **Recommendation:** `close-done` (high). Landed and active; superseded and extended by CARD-1082.
- **Closing verdict:** Fixed: runner task settlement syncs the task's own pushed commit into its owned checkout before attribution (S1/S2, R1-R4), landed 87f261ac7 on 2026-09-24 and active since the following restart. Later extended by CARD-1082 sync debt. Still open: PCs pending Mutation.
- **Board state:** Earlier owner 27dd8efb is Canceled with a Refused (rebase_conflict) land; harmless.
- **Related:** CARD-1082

### CARD-0675: Landing a -StartRef repair needs a manual force-push of the owner branch (no adopt-repair path; runner repairs cannot land themselves)

- **Asked:** A supported adopt-repair land path, so a -StartRef repair of a landing owner needs no manual force-push.
- **Landed on master:** Code 8a9a6d39 landed at 3a8d0f273 (2026-09-30): StartRef adoption on diverged owner mirrors, stale-mirror refusal, and status evidence. Documented at docs/ops-http.md:180.
- **Still open:** The 'real adoption drill' is satisfied in practice: at least 7 production lands used recoveryMode AdoptReviewedSource with a Descendant relationship, e.g. CARD-0519 7f356209 from 530ed557 (2026-10-05 03:42Z) and CARD-1153 4a4aacaa from 809c8b49 (2026-10-08 07:49Z). PC-1..6 are pending; an optional doc gap (detached_head is not in the refusal table).
- **Recommendation:** `close-done` (high). Landed and exercised in production repeatedly.
- **Closing verdict:** Shipped 3a8d0f273 (2026-09-30): -Land <owner> -FromTask <repair> adopts a reviewed descendant source, with recovery evidence on the landing. Proven in production by at least 7 AdoptReviewedSource lands (e.g. CARD-0519 7f356209, CARD-1066 d73d91aa, CARD-1097 c0caf152, CARD-1153 4a4aacaa). Still open: PC-1..PC-6 pending Mutation; optional docs fix adding detached_head to the orchestration-loop refusal table.
- **Related:** CARD-0684

### CARD-0692: Post-land Cleanup stage: retire all task worktrees, branches, runner worktrees and sessions for a landed card

- **Asked:** A post-land Cleanup stage that retires every task worktree, branch, runner worktree, session and temp state of a landed card.
- **Landed on master:** No stage. The card was used as an umbrella for manual cleanups: about 728 desktop worktrees removed (Deploy/Custom b75a3769, 64dc1fff, ab435579, 7984e409, b53af8bd, 36db5278), 607 origin branches deleted (b84e8f78, restore manifest 1d0461b2e), and a lost-work triage (c0e0f4165, 0 LOST).
- **Still open:** The whole feature: no Plan, TestDesign or Code. It overlaps CARD-0987 (CARD-0826 remainder), CARD-0459, CARD-0804/0805 and CARD-0824.
- **Recommendation:** `move-to-backlog` (medium). The one-off reclaim is done; the requested stage was never designed.
- **What moves it:** Plan, reconciling with CARD-0987/CARD-0824 so there are no duplicate deleters.
- **Related:** CARD-0987, CARD-0826, CARD-0459

### CARD-0698: antiphon-postgres burns ~5.6 cores: TranscriptEntries polled by full seq scan ~36x/s

- **Asked:** antiphon-postgres burned about 5.6 cores: TranscriptEntries polled by full seq scan about 36 times per second.
- **Landed on master:** Indexed transcript reads, bounded UUID replay, and online concurrent-index migrations (dbb22ef37, 50275689d; publication 008658d7f per Review 5294bdc8), the Windows startup hotfix 5f4e92e4e, and Retention 7 days (456011879). The Review reproduced seq_scan 0->0 and idx_scan 0->11,849 on a fixture.
- **Still open:** The card's live acceptance was never recorded: a desktop pg_stat_user_tables delta and Postgres CPU after activation. PCs pending Mutation.
- **Recommendation:** `close-done` (medium). Landed and active since 2026-09-25 with fixture proof; only the live measurement is missing.
- **Closing verdict:** Fixed: TranscriptEntries working-state and UUID reads are index-backed and bounded, with migrations recoverable after interruption. Landed 2026-09-25 (008658d7f, 5f4e92e4e) and active. Fixture proof: seq_scan 0->0, idx_scan 0->11,849. Retention cut to 7 days (456011879). Not recorded: the desktop live pg_stat delta and Postgres CPU. Follow-on caching is CARD-0701. PCs pending Mutation.
- **Related:** CARD-0701, CARD-0712

### CARD-0701: Cache session working state in memory (ingest-updated, event-driven) instead of re-deriving it from TranscriptEntries on every poll

- **Asked:** Cache session working state in memory (ingest-updated, event-driven) instead of re-deriving it from TranscriptEntries on every poll.
- **Landed on master:** R1 committed-state projection for queue and agents (ea65b6441, 1c3851de4) and the cascade-delete reseed (11df27153), 2026-09-25. Plan fbdc3c6e6.
- **Still open:** The 2026-10-04 re-plan of R2/R3 (Investigate cb945640a, Plan 94b2bbb07, TestDesign 870622488) and Code S0 (20ba8a291, CP-1 16/16) are on unlanded branches, and the S0 Review was never dispatched. Desktop AC-1 measurement before R2 activation; CP-2..14; PC-1..181.
- **Recommendation:** `move-to-in-progress` (medium). The next owed step is the Review of S0 (Code 4a97e419 at 20ba8a2916f7c8ada05d31a97648e79051c89a07); idle since 2026-10-04.
- **What moves it:** Dispatch Review of 4a97e419 at 20ba8a29, then land it (its branch carries the re-plan docs).
- **Board state:** Succeeded Code 4a97e419 (next=review) never reviewed; re-plan docs only on unlanded branches.
- **Related:** CARD-0698

### CARD-0707: A Blocked task from another project (worktree gone) holds every Antiphon land as a repository/source writer

- **Asked:** A Blocked task from another project whose worktree is gone holds every Antiphon land as a repository writer.
- **Landed on master:** Root cause only (Debug c9570fad, 2026-09-25); no fix. The defect is still on master: AgentTaskLandService.FindWriterAsync (server/Application/Services/AgentTaskLandService.cs:844-875) scans Dispatched/Working/Blocked tasks across all projects, and an IOException resolving a candidate returns that candidate as the writer (lines 864 and 872).
- **Still open:** Plan/TestDesign/Code: match writers by project/repository first, fail closed only for the task's own repository, surface unresolvable writers on attention, refresh the observation text.
- **Recommendation:** `move-to-backlog` (high). A real, unfixed bug with a confirmed mechanism; no work since the Debug.
- **What moves it:** Plan the fix (coordinate with CARD-0684).
- **Related:** CARD-0684

### CARD-0712: Stop Npgsql DISCARD ALL per pooled connection (No Reset On Close) after session-state safety check

- **Asked:** Stop Npgsql DISCARD ALL per pooled connection (No Reset On Close) after a session-state safety check.
- **Landed on master:** 69ffaffaf (2026-09-25): 'No Reset On Close=true' in server/appsettings.json:3, Antiphon.AppHost/appsettings.json:10 and docker-compose.yml:29, plus the audit docs/investigations/2026-09-25-card-0712-pool-reset-safety.md. Unit 3,121/0; 24 integration failures were inherited.
- **Still open:** The acceptance measurement (pg_stat_statements DISCARD ALL near 0 after activation) was never recorded. PCs pending.
- **Recommendation:** `close-done` (medium). Landed and active; only the measurement is missing.
- **Closing verdict:** Shipped 69ffaffaf (2026-09-25): pool reset disabled in the server, AppHost and compose connection strings after an audit found no unsafe pooled session state. Active since the next restart. Not recorded: a fresh DISCARD ALL rate on the desktop. Note: the CARD-0701 plan (docs/superpowers/plans/2026-09-25-card-0701-session-state-cache-plan.md:210) advised leaving it false, so reconcile that sentence.
- **Related:** CARD-0698, CARD-0701

### CARD-0718: Host stats: 5s CPU/memory/load + parallel tasks per host, in-memory 1/5/15/30-min rollups, Hosts page + API + graphs

- **Asked:** Host stats: 5 s CPU/memory/load and parallel tasks per host, in-memory rollups, Hosts page, API and graphs.
- **Landed on master:** Runner sampler, server polling/API, Hosts page and charts, docs (152b56b75..735ad68d7; landings 0f2f35507 and 735ad68d7, 2026-09-26).
- **Still open:** CP-11/V-15 live receipt after land and restart was never recorded. This task did not verify the stats route; GET /api/hosts answers with three hosts (local, server2, server2-temp), but that is the budget view. PCs pending.
- **Recommendation:** `close-done` (high). Landed and active since 2026-09-26; the open item is a recorded live receipt.
- **Closing verdict:** Shipped 2026-09-26 (landings 0f2f35507, 735ad68d7): runner host sampling every 5 s, server rollups, the /api/hosts series API, and the Hosts page with charts. Measured sampler overhead is recorded (10b00047b). Still open: the CP-11 live acceptance receipt and PCs pending Mutation.
- **Related:** CARD-0654

### CARD-0817: server2 runner: push credential for non-Antiphon repositories

- **Asked:** A push credential on server2 for non-Antiphon repositories (final decision: one classic PAT shared by server2 and server2-temp, HTTPS credential helper, allow-list stays the gate).
- **Landed on master:** Plans f205e8e98 and 84c374cb5; Code fead34620 (2026-10-06): mounted-token credential helper, HTTPS push probe, fixtures (CP-1..6 54/54).
- **Still open:** Operator-owned provisioning of the token file on server2 and server2-temp and a real acceptance push (no markdown-package task has run on any server2 runner since 2026-10-06: 1 task, desktop). CP-19 has an inherited red (c1008_owned_mounts sudo readlink). The whole Unit lane never finished. PC-1..22.
- **Recommendation:** `needs-operator-decision` (high). Code is landed; the remaining step is a human credential action.
- **Decision needed:** Operator (or ClaudeBot with vault access) mounts the PAT file on server2 and server2-temp per docs/agent-credentials.md, then one markdown-package task runs with -Runner server2 to prove the push.
- **Related:** CARD-0812

### CARD-0822: Live orchestrator instructions file: generated from settings, re-read on start/compact, pushed on change

- **Asked:** A live orchestrator instructions file generated from settings, re-read on start/compact and pushed on change.
- **Landed on master:** Plan only: 386c07b73 docs/superpowers/plans/2026-09-29-card-0822-orchestrator-instructions-file-plan.md.
- **Still open:** All Code S1-S4. It is sequenced last in the CARD-0881 order (0826->...->0505->0881->0822).
- **Recommendation:** `move-to-backlog` (high). Plan ready, nothing built, and blocked behind CARD-0505 and CARD-0881.
- **What moves it:** After CARD-0881 lands.
- **Related:** CARD-0881, CARD-0505

### CARD-0826: Daily per-host cleanup of worktrees, work volume and temp older than 24 hours

- **Asked:** A daily per-host cleanup of worktrees, the work volume and temp older than 24 hours, with allow/deny lists and dry run.
- **Landed on master:** S1 policy core, protected plans/holds, durable receipts and attention, and the AddHostCleanup migration (b26c97658 and earlier; landing b26c97658, 2026-10-02). It is inert: no hosted service (see CARD-0987).
- **Still open:** Everything else was moved by Review 87da3a46 to CARD-0987 (Backlog/High: S2/S3 integration, S4, S5, native, PCs) and CARD-0988 (flake).
- **Recommendation:** `close-done` (high). Its remaining scope already lives in CARD-0987.
- **Closing verdict:** S1 landed b26c97658 (2026-10-02): the host-cleanup policy core, plans/holds, idempotent receipts, board attention and the AddHostCleanup migration, inert. All remaining work (S2-S5, native, PCs) moved to CARD-0987; the Grok readiness flake to CARD-0988.
- **Related:** CARD-0987, CARD-0988, CARD-0692

### CARD-0881: One effective-settings endpoint for orchestrators (concurrency limits, runner capacity, occupancy)

- **Asked:** One effective-settings endpoint for orchestrators (limits, runner capacity, occupancy, sources), then rewrite the orchestrator policy text.
- **Landed on master:** Plan 0bae0856a and TestDesign 321c7826b (182 existing plus 40 new executions; 29 mutation variants).
- **Still open:** All Code, sequenced after CARD-0505. AGENTS.md still says 'CARD-0881 will replace' the three-route read.
- **Recommendation:** `move-to-backlog` (high). Ready for Code but explicitly ordered after CARD-0505, which has not started.
- **What moves it:** After CARD-0505 lands (or re-sequence if the operator wants the read half first).
- **Related:** CARD-0505, CARD-0822, CARD-0880

### CARD-0889: RunnerCodexAdapterSubmitConfirmTests fail under 24-burner CPU load (2 s confirmation budget)

- **Asked:** RunnerCodexAdapterSubmitConfirmTests failed under 24-burner CPU load (2 s real-time confirmation budget); add a fake-time seam.
- **Landed on master:** The named fix landed under CARD-0742: c345371e2 (2026-10-02) 'miss-safe time advance and TimeProvider seams' added TimeProvider to CodexSubmitOptions (server/Infrastructure/Agents/CodexSubmitConfirmation.cs:16-18). Root-cause doc 987d030b4.
- **Still open:** The 2026-10-04 re-plans widened the card to a grouped flaky-fix programme (C448 worker migration, C578 readiness). Those four docs (a06b4b242, cd07828f6, bfeb335c5, 926896173) are only on unlanded branches.
- **Recommendation:** `close-done` (medium). The card's own defect is fixed on master; the widened scope belongs to other cards.
- **Closing verdict:** Fixed by CARD-0742 c345371e2 (2026-10-02): CodexSubmitOptions takes a TimeProvider and the scripted no-confirmation tests run on fake time; production keeps the 20 s default. The 2026-10-04 grouped flaky-fix plans (C448 worker migration, C578 handshake) stayed on unlanded branches. File them as their own card if still wanted.
- **Board state:** Four Oct-4 Plan/Investigate/TestDesign artifacts only on unlanded branches.
- **Related:** CARD-0742, CARD-0778

### CARD-0905: WSL lacks pwsh so 5 RemoteScriptContractTests C849_* tests fail on the Windows desktop

- **Asked:** WSL lacked pwsh, so 5 C849 RemoteScriptContractTests failed on the Windows desktop; make the Windows lane deterministic with a loud skip.
- **Landed on master:** 59df65e15..2ec3410ae (landing 2ec3410ae, 2026-10-01): skip C849 shell tests with a named reason when Linux pwsh is absent, forced-probe isolation.
- **Still open:** None for this card. Debug 701643a0 then found a different defect once pwsh existed (Windows RepoRoot passed to WSL pwsh); that was filed and fixed as CARD-0980 (Done).
- **Recommendation:** `close-done` (high). Landed; the follow-on defect is closed under CARD-0980.
- **Closing verdict:** Shipped 2ec3410ae (2026-10-01): the five C849 WSL-pwsh tests skip with a named reason when pwsh is absent from WSL and still run on Linux. The next defect, Windows RepoRoot passed into WSL pwsh, was fixed under CARD-0980.
- **Related:** CARD-0980

### CARD-0907: Capture Codex 0.160.0 TUI screens and verify readiness/trust/done/update/usage-limit parsers before the server2 image deploy

- **Asked:** Capture Codex 0.160.0 TUI screens and verify the readiness/trust/done/update/usage-limit parsers before the server2 image deploy (deploy gate for CARD-0904).
- **Landed on master:** Plan 2373e9108 and TestDesign 1754c95a0 (29 tests, 12 controls, 252 per-OS results).
- **Still open:** All Code and captures. The gate has lapsed: CARD-0904 is Done and 0.160.0 is live (GET /api/session-runners reports codexCliVersion 0.160.0 on desktop and server2-temp, 2026-10-08).
- **Recommendation:** `needs-operator-decision` (medium). It was a pre-deploy gate and the deploy happened without it. The parser coverage risk remains, but the urgency is gone.
- **Decision needed:** Keep as a Backlog parser-qualification card (re-target to the current Codex version) or close as superseded by live operation since CARD-0904.
- **Board state:** A deploy gate that the deploy did not wait for.
- **Related:** CARD-0904, CARD-0903, CARD-1023

### CARD-0997: C849 RemoteScriptContractTests depend on real host free disk (>=20 GiB CacheDiskLow gate): Unit lane red on server2 when disk is low

- **Asked:** Make the C849 tests hermetic: they read the real host free disk (20 GiB CacheDiskLow gate) and go red on server2 when disk is low.
- **Landed on master:** Nothing on master. The plan and TestDesign (e84b80f10, 3c30cbc43; 20 results, 15 controls) are only on feat/card-task-cdd8d229. The defect is unchanged: scripts/c590-remote.sh:2432-2434 still runs df against the real Docker root, and scripts/c849-import-saved-donor.ps1:51 still uses the real drive.
- **Still open:** Land the plan, then Code S1/S2 (about 14 min ordinary V/R).
- **Recommendation:** `move-to-backlog` (high). Ready for Code, small, and still a live source of host-dependent reds.
- **What moves it:** Land the plan from feat/card-task-cdd8d229, then dispatch Code.
- **Board state:** Plan and TestDesign artifacts only on an unlanded branch.
- **Related:** CARD-0905, CARD-0980, CARD-0875

### CARD-1010: Rolling recycle opt-ins -RecycleRunnerState / -RecycleCaches (D-6 maintenance proofs, lease-expiry contract)

- **Asked:** Rolling recycle opt-ins -RecycleRunnerState and -RecycleCaches with a production-observable lease-expiry contract (D-6, split from CARD-1008).
- **Landed on master:** Only the split note 11675d5ef (CARD-1008 plan). The plan (2395858ec) and TestDesign (304d28722; 10 tests, 40 controls) are only on feat/card-task-fd423b29.
- **Still open:** Code S1-S3. The handoff orders it after CARD-1030 (In Progress) and CARD-0983 (Done).
- **Recommendation:** `move-to-backlog` (high). Optional opt-ins; the default recycle is in CARD-1008. Blocked behind CARD-1030.
- **What moves it:** Land the plan from feat/card-task-fd423b29; dispatch Code S1 once CARD-1030 lands.
- **Board state:** Plan and TestDesign artifacts only on an unlanded branch.
- **Related:** CARD-1008, CARD-1030

### CARD-1023: Compatibility matrix (CLI version x model x session-runner/pty-host/backend x host) replaces version-floor admission gating

- **Asked:** Compatibility matrix (CLI version x model x runner/pty-host/backend x host) that replaces version-floor admission gating: allow by default, refuse only on known-broken cells.
- **Landed on master:** Nothing on master. The plan 02d54f6ae is only on feat/card-task-63057c79 (next=decide).
- **Still open:** Decisions D-1 (observation-first scope, admission deferred) and D-3 (Sol 0.159.1 seed provenance), then TestDesign, Code S1-S3.
- **Recommendation:** `needs-operator-decision` (high). The plan explicitly stops for an operator decision.
- **Decision needed:** Accept or revise D-1 (catalogue, matcher and observation first; admission later) and D-3 (gpt-6.1-sol needs Codex 0.159.1 as the first known-broken row; Astra 0.153.4 stays guidance). Then land the plan from feat/card-task-63057c79.
- **Board state:** Plan artifact only on an unlanded branch.
- **Related:** CARD-0959, CARD-1031, CARD-1022

### CARD-1031: Codex CLI version probe reports unknown (stderr_output) when codex --version writes to stderr (npm update notice)

- **Asked:** The Codex CLI version probe reported unknown (stderr_output) when codex --version wrote an npm notice to stderr.
- **Landed on master:** TestDesign e0c74f38e and Code landing 1ac1b61db (2026-10-04); native Windows CP-4 12/12 (Test 66704481). stderr is now advisory (server/Application/Services/CodexCliObservation.cs:22,33).
- **Still open:** None. Live check 2026-10-08 09:55Z: desktop and server2-temp report codexCliVersion 0.160.0 with advisory codexCliVersionError stderr_output. The draining server2 main runner reports null.
- **Recommendation:** `close-done` (high). Landed, active, and verified live.
- **Closing verdict:** Fixed 1ac1b61db (2026-10-04): the probe parses the version from stdout and treats stderr as an advisory diagnostic (CodexCliObservation.cs:33). Native Windows CP-4 is 12/12. Live on 2026-10-08: desktop and server2-temp report Codex 0.160.0. PCs pending Mutation.
- **Related:** CARD-0959, CARD-1023

### CARD-1039: Daily whole-Unit run: activate and verify the existing Windmill nightly

- **Asked:** Activate and verify the existing Windmill nightly so a daily whole-Unit run exists with counts, SHA and retained receipts.
- **Landed on master:** Plans and TestDesigns (ff5712301..33b5ee959) and the S1n-a Windows native job containment (landing 56772d096).
- **Still open:** The nightly is still not activated. The next two Code tasks failed CompletedWithoutProgress (0fd2a915, 0025fbfd), and Debug 7951fe34 found load-sensitive timeouts and coverage-count failures in a shared-process run. The Windmill job registration is operator-run.
- **Recommendation:** `needs-operator-decision` (medium). Stalled after two failed Code attempts. Activation needs an operator action and a choice of mechanism (Windmill nightly here versus the CARD-0599 Hangfire direction).
- **Decision needed:** Continue (fix the shared-process timing failures from Debug 7951fe34, then register the Windmill job), switch scheduling to Hangfire alongside CARD-0599 S6, or park it.
- **Board state:** Two Failed CompletedWithoutProgress Code tasks; the last settle was a Debug, so the card shows as Review.
- **Related:** CARD-0599, CARD-0545, CARD-1114

### CARD-1040: Unit timeouts/inherited failures: resolve CARD-1021 jq preconditions and measure limits

- **Asked:** Unit timeouts and inherited failures: resolve the CARD-1021 jq preconditions and measure the limits.
- **Landed on master:** Plan 5de230cf7, TestDesign 5b46e3263, S1 jq admission docs (landing 90b786720).
- **Still open:** S2 V/R was waiting for jq in the runner image. jq is now present on this server2-temp mirror (/usr/local/bin/jq, observed 2026-10-08), but the 15-method S2 proof was never run. Code 6159b95b was Canceled. The C849 Fixture blocker found on the way moved to CARD-1066/CARD-1068.
- **Recommendation:** `keep-in-review` (medium). One concrete measurement run is left and its precondition now holds.
- **What moves it:** Run the S2 15-method jq verification on server2-temp, then close (or close now if the operator accepts CARD-0927/0983 evidence).
- **Related:** CARD-1021, CARD-0927, CARD-0983, CARD-1066

### CARD-1065: Blocked tasks hold runner seats indefinitely; park must push WIP and release the seat

- **Asked:** Blocked tasks hold runner seats indefinitely; park must push WIP and release the seat, with resume from the pushed branch.
- **Landed on master:** S1-S10 (15 landings, last 951e5bf43, 2026-10-06), including park publication, release, fresh-session resume, legacy reclaim and docs. Dormant: BlockedTaskParking:Enabled defaults to false (AGENTS.md; CARD-1083 Done). All are active in the live build b5e78700a.
- **Still open:** The activation decision, and the follow-up cluster still In Progress: CARD-1136, 1137, 1138, 1149, 1150, 1151 (1143 is in Review). PC-1..226 pending Mutation.
- **Recommendation:** `needs-operator-decision` (medium). Left open on purpose tonight; everything has landed dormant and the remaining choice is when to enable parking.
- **Decision needed:** When to set BlockedTaskParking:Enabled=true (after the In-Progress follow-ups land?), or close CARD-1065 as landed-dormant and track activation on its own card.
- **Board state:** Owner tasks bc986d96, f0816532 and 24c08059 are Canceled but each carries a Landed publication.
- **Related:** CARD-0667, CARD-1082, CARD-1083, CARD-1097, CARD-1103, CARD-1104, CARD-1129, CARD-1135, CARD-1143, CARD-1145

### CARD-1066: Fixture PC-12 aborts on unset fault under set -u

- **Asked:** Fixture PC-12 aborted on an unset $fault under set -u (combined local declaration) in scripts/c590-remote.sh.
- **Landed on master:** Nounset repair plus direct regressions (65439dfa1, 285141d61, 4f56bc272, 0e4c26f74; adopted from 7cc255f0 into owner d73d91aa, landing 0e4c26f74, 2026-10-05). Review deedf9dd was clean.
- **Still open:** The live Fixture rerun (CP-3/V-4) then failed for a different reason, which is CARD-1068. Residue on server2 needs two human sudo rm commands (Debug e2eb87b6). PCs.
- **Recommendation:** `close-done` (high). Its defect is fixed; the next blocker has its own card.
- **Closing verdict:** Fixed 0e4c26f74 (2026-10-05): each fixture variable gets its own local before use, and direct set -u regressions cover every c849_fixture_tree_fault kind. Review deedf9dd was clean. The live Fixture gate now stops on a different defect tracked as CARD-1068. Still open: the human cleanup of the server2 residue named by Debug e2eb87b6, and PCs pending Mutation.
- **Board state:** Owner d73d91aa is Failed while carrying the Landed publication (adopted from 7cc255f0).
- **Related:** CARD-1068, CARD-1067, CARD-0849

### CARD-1068: Fixture gate smokes the old 0744 runner image and leaves cleanup residue

- **Asked:** The Fixture gate smokes the old 0744 runner image (ImagePackMissing) and leaves root-owned and symlink cleanup residue.
- **Landed on master:** Plan only: 8ba516cd9 docs/superpowers/plans/2026-10-05-card-1068-cache-fixture-verifier-plan.md (8 slices, 22 controls).
- **Still open:** Decisions D-1 (explicit image build, qualification and import before Fixture) and D-5 (separate primary-result versus cleanup acceptance), then TestDesign and Code. Human cleanup: two sudo rm commands on server2.
- **Recommendation:** `needs-operator-decision` (high). The plan stops for D-1 and D-5.
- **Decision needed:** Settle D-1 and D-5; also approve or run the two sudo rm cleanups for run c84983933adda4d94c530 listed in the card.
- **Related:** CARD-1066, CARD-0934, CARD-0849

### CARD-1074: AgentTaskDispatcher: interrupted dispatch loses its brief and a launch enqueue refusal after the committed claim fails the task (generic delegation path; found by CARD-0519 S12) [bug,delegation,reliability]

- **Asked:** Generic dispatcher: an interrupted dispatch lost its brief, and a launch-enqueue refusal after the committed claim failed the task.
- **Landed on master:** Fix 6e04c87f3 (resume backfills a missing brief; an enqueue refusal keeps the task Dispatched) with witnesses fd5ca8f50 and b4be3d466, landed 2026-10-05 inside the CARD-0519 owner 5724b53e. Verified 3/3 by d7d5a060d.
- **Still open:** Two residual scenarios found by Investigate 4c391558 (docs/investigations/2026-10-07-card-1074-residual-scenarios.md), already filed and In Progress as CARD-1149 and CARD-1150.
- **Recommendation:** `close-done` (high). Both named defects are fixed and the residuals are tracked on their own cards.
- **Closing verdict:** Fixed 6e04c87f3 (2026-10-05; landed via the CARD-0519 owner 5724b53e): ResumeInterruptedLaunchAsync backfills a missing delegation brief, and a launch-enqueue refusal after the committed claim keeps the task Dispatched for recovery instead of Failing it. Verified 3/3 on 2026-10-07 (d7d5a060d). Residuals: absent runner (CARD-1149) and a crash after Running (CARD-1150), both In Progress.
- **Related:** CARD-1149, CARD-1150, CARD-0519

### CARD-1076: Remote-prep git push killed at 5 minutes serially fences lease-taking dispatch

- **Asked:** A remote-prep git push killed at 5 minutes serially fenced lease-taking dispatch; budget, non-fencing journal, lease-free re-arm, queue reasons.
- **Landed on master:** S1-S5 (landings 99ac3b318, 678f246fa, 23dc94ea5, 0ca45b6f4, 78894b45d, 2026-10-06), documented at docs/ops-http.md:135. Active in b5e78700a.
- **Still open:** PC-1..12 pending Mutation; a suggested Backlog card to bind the V-15 doc pins to code constants.
- **Recommendation:** `close-done` (high). Landed and active.
- **Closing verdict:** Shipped S1-S5 (2026-10-06, last 78894b45d): a per-call git budget (Delegation:RemotePrepPushBudgetMinutes, default 20), a non-fencing live remote-prep push, per-repository serialization, a lease-free re-arm of a cut worktree, and repositoryLease/remotePrep queue reasons. Still open: PC-1..12 pending Mutation; the optional V-15 constant-binding card.
- **Related:** CARD-0809, CARD-1093

### CARD-1079: Alert on idle runner seats, host occupancy above Working count, and orphan slots (detection only)

- **Asked:** Alert on idle runner seats, host occupancy above the Working count, and orphan slots (detection only), plus durable occupancy samples.
- **Landed on master:** S1-S4 (landings 760c3a9b8, 391302f12, ed41f67cd, 64f43a578, 2026-10-06): settings and sample rows, a timer sampler, SeatIdle/OccupancyDivergence/SlotOrphan attention, a read-only occupancy audit.
- **Still open:** PC-1..16 pending Mutation; route-hardening disclosures filed as CARD-1101.
- **Recommendation:** `close-done` (high). Landed and active.
- **Closing verdict:** Shipped S1-S4 (2026-10-06, last 64f43a578): durable host occupancy samples, SeatIdle, OccupancyDivergence and SlotOrphan attention rows, and a read-only occupancy audit route. Detection only. Still open: PC-1..16 pending Mutation; disclosures in CARD-1101.
- **Related:** CARD-1101, CARD-1124

### CARD-1087: Rolling redeploy-old stops with RecycleTaskCensusUnknown: empty project URL, 90 s census vs 15 s timeout, 141 Failed rows bound

- **Asked:** Rolling redeploy-old stopped with RecycleTaskCensusUnknown: empty project URL, a 90 s census against a 15 s timeout, 141 Failed rows bound.
- **Landed on master:** S1-S6 (landings 860cd6a8c, 0c79cce42, 2026-10-06): redacted census reads with typed causes, project resolution by repository identity or explicit -ProjectId, a pending-land filter, a read-only census preflight. docs/docker-stack.md:31-34.
- **Still open:** Never exercised by a real redeploy-old. Live 2026-10-08: the server2 runner is draining while server2-temp serves 10/10 seats, so the rollout is still mid-phase. The Antiphon project's gitRepositoryUrl is still empty (GET /api/projects/d4ea7ae9...), so the run needs -ProjectId or a full-row project update. Backlog suggestions: split CP-3, and harden the Docker census against foreign containers.
- **Recommendation:** `keep-in-review` (high). The code is landed; the proof is the next real rollout phase, which the orchestrator may run autonomously.
- **What moves it:** Run the census preflight and redeploy-old with -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f (or after a full-row project URL update); close when server2 completes redeploy-old.
- **Related:** CARD-0934, CARD-1008, CARD-1105

### CARD-1097: CARD-1065 S7 follow-ups: dedupe park_resume_refused warnings and read runner mirror identity at resume

- **Asked:** CARD-1065 S7 follow-ups: (1) dedupe park_resume_refused warnings; (2) read the runner mirror identity at resume; (3)-(5) lease window, confirm-path bookkeeping, prune scope.
- **Landed on master:** Item 1 only: 48e9e7495 (once per reason per park episode) and db4350ff3 (stay Queued when warning telemetry fails), adopted into owner c0caf152, landing f294c86ad (2026-10-07 17:05Z). Not live: neither commit is in the served build b5e78700a.
- **Still open:** An AppHost restart to activate item 1. Items 2-5 were not addressed; Review 24bc9efb says the disclosures 'need Backlog triage'. PCs.
- **Recommendation:** `close-done` (medium). The card's priority item is landed. The rest are hardening disclosures that need their own Backlog card, not this one held open.
- **Closing verdict:** Item 1 shipped f294c86ad (2026-10-07): park_resume_refused is written once per reason per park episode, and a refused resume stays Queued if the warning write fails. Active after the pending AppHost restart. Items 2-5 (resume-time runner identity read, inspection lease window, confirm-path bookkeeping order, repository-wide prune) were not done; move them to one Backlog card. PCs pending Mutation.
- **Related:** CARD-1065

### CARD-1103: CARD-1065 parked follow-up guidance: scope HasConfirmedPublishedParkAsync to the attempt and name Reply for remote confirmed parks

- **Asked:** Scope HasConfirmedPublishedParkAsync to the current attempt and name Reply for remote confirmed parks.
- **Landed on master:** 9f3f1c68c and efb6cb1e7 (landing efb6cb1e7, 2026-10-07). Active in b5e78700a.
- **Still open:** PC-7, PC-8 and the settlement-revision mutation pending Mutation. The related disclosures CARD-1144 and CARD-1146 are Done.
- **Recommendation:** `close-done` (high). Landed and active; the related disclosures are closed.
- **Closing verdict:** Shipped efb6cb1e7 (2026-10-07): confirmed-park guidance is scoped to the task's current attempt and recommends Reply only when seat admission matches. Related fixes CARD-1144/1146 are Done. Still open: PC-7, PC-8 and the settlement-revision control pending Mutation.
- **Related:** CARD-1065, CARD-1144, CARD-1146

### CARD-1104: CARD-1065 V-27: prove the parked caller receipt on the inbox pointer path (default PtySingleChunkBytes)

- **Asked:** Prove the CARD-1065 V-27 parked caller receipt on the inbox pointer path (default PtySingleChunkBytes).
- **Landed on master:** 5a402d6d9 (landing 5a402d6d9, 2026-10-07): proves the local-parent pointer receipt and rewrites the known limits.
- **Still open:** Remote-parent receipt coverage and a courier-throw negative test remain disclosed gaps (Review f10738e8). PC-12..14.
- **Recommendation:** `close-done` (medium). The ask (prove the pointer path or document why not) is met for local parents, and the remote gap is documented as a known limit.
- **Closing verdict:** Shipped 5a402d6d9 (2026-10-07): V-27 now proves the parked caller receipt on the pointer path for a local parent (pointer UserPrompt plus retained spill naming review evidence and SHA). The known-limits text was rewritten. Not covered: the remote-parent receipt and a courier-throw negative test (disclosed). PC-12..14 pending Mutation.
- **Related:** CARD-1065

### CARD-1109: CARD-1082 residual: Code task whose commits were never fetched during the lease budget still ends Blocked

- **Asked:** A Code task whose commits were never fetched during the lease budget still ends Blocked (CARD-1082 residual): measure, then close the gap.
- **Landed on master:** Plan 5032ea9e5 and eb1fe3407: runner-attested ancestry (DescendsFromBaseline).
- **Still open:** Decisions D-1 (admit the runner attestation when the mirror tip equals the server-observed tip) and D-9 (dispatch S1 only after CARD-1082 S5 activation plus a one-week measurement). CARD-1082 is Done (2026-10-07), so the measurement window is running.
- **Recommendation:** `needs-operator-decision` (high). The plan stops for D-1/D-9 and itself asks for a measurement first.
- **Decision needed:** Confirm D-1 and D-9. If D-9 stands, park until about 2026-10-14 and run the plan's SQL/API measurement before S1.
- **Related:** CARD-1082, CARD-1133, CARD-0743

### CARD-1113: CARD-1082 S1 disclosure: pin SettlementSyncDebtPolicy inputs (FullRef, null evidence, Pending guard) before S5 wires it

- **Asked:** Pin the SettlementSyncDebtPolicy inputs (FullRef provenance, null evidence, Pending guard) before S5 wires the policy.
- **Landed on master:** 319236fef (2026-10-07): sync-debt eligibility pinned to the owned ref, failing closed without progress evidence. Item 3 was satisfied by the S5 call-site guards; follow-up a77c7a3a5 (CARD-1133). Active in b5e78700a.
- **Still open:** PC-1..4 pending Mutation (PC-2's second detector arrived with F2).
- **Recommendation:** `close-done` (high). All three hardenings are in place and active.
- **Closing verdict:** Shipped 319236fef (2026-10-07): Eligible() requires the server-observed tip on the task's owned ref, and a Code settlement with no progress evidence fails closed. Pending-only helpers are guarded at the S5 call sites. Still open: PC-1..4 pending Mutation.
- **Related:** CARD-1082, CARD-1133

### CARD-1129: CARD-1108 S2 disclosures: scheduled sweep recounts already-parked rows as released; once-per-run bound assumes a stable Blocked set

- **Asked:** CARD-1108 S2 disclosures: the scheduled sweep recounted already-parked rows as released; the once-per-run bound assumed a stable Blocked set; the 119 s boundary was untested.
- **Landed on master:** 858a382d8 (count this run's releases; stop at a repeated row) from Code ec23e008. It reached master inside the CARD-1145 land 352489b66 (2026-10-07), which also fixed the rollback regression the CARD-1129 Review found.
- **Still open:** PCs pending Mutation.
- **Recommendation:** `close-done` (high). Landed (via the CARD-1145 land) and active.
- **Closing verdict:** Fixed 858a382d8, landed with CARD-1145 at 352489b66 (2026-10-07): a reclaim run counts only releases it confirmed and stops at the first repeated row. CARD-1145 corrected the clock-rollback accounting that Review e77ff54b found. Still open: PCs pending Mutation.
- **Board state:** Code ec23e008 (Succeeded) has no landing record of its own; its commit landed through 11d1db07.
- **Related:** CARD-1145, CARD-1147, CARD-1108

### CARD-1133: CARD-1082 S5 disclosure: Blocked lease-busy settlements (unfetched Code, bind-refusal recovery) record remoteSync Pending with no debt row and a 'Runner sync pending ... then reply' warning

- **Asked:** Blocked lease-busy settlements record remoteSync Pending with no debt row and a misleading 'Runner sync pending ... then reply' warning.
- **Landed on master:** a77c7a3a5 (2026-10-07): say 'unavailable' when a Pending sync result blocks. Active in b5e78700a.
- **Still open:** PC-2 and PC-5 pending Mutation.
- **Recommendation:** `close-done` (high). Landed and active.
- **Closing verdict:** Shipped a77c7a3a5 (2026-10-07): a Blocked lease-busy settlement now reports 'Runner sync unavailable', so Pending without a debt row is no longer described as self-healing. Still open: PC-2 and PC-5 pending Mutation.
- **Related:** CARD-1082, CARD-1113, CARD-1109

### CARD-1135: CARD-1108 S3 disclosures: a Held park refused before PersistIntent is not re-stamped; the Held restamp keeps the stale reason code

- **Asked:** CARD-1108 S3 disclosures: a Held park refused before PersistIntent was not re-stamped; the Held restamp kept a stale reason code.
- **Landed on master:** 2f751049b (2026-10-07): re-stamp a Held refusal (with its reason) and keep the loopback inventory budget. Active in b5e78700a.
- **Still open:** PC-1..3 pending Mutation. The residual (non-HoldAsync refusals) is CARD-1143, in Review with its Review queued.
- **Recommendation:** `close-done` (high). Landed and active; the residual has its own card.
- **Closing verdict:** Shipped 2f751049b (2026-10-07): every HoldAsync refusal on a Held park re-stamps NextAttemptAt and records the refusal reason. The residual (refusals that return Held without a write, e.g. an unloadable episode) is CARD-1143. PC-1..3 pending Mutation.
- **Related:** CARD-1143, CARD-1108

### CARD-1143: CARD-1135 residual: a Held park whose episode is no longer loadable is visited on every sweep without a re-stamp

- **Asked:** CARD-1135 residual: a Held park whose episode is no longer loadable is visited on every sweep without a re-stamp.
- **Landed on master:** Plan 06163abfa and 9f5f004f0 (reproduced residual, measured SQL budgets).
- **Still open:** Code cab80ac2 (S1-S3, ddeb495cb) is pushed but not landed; its Final Review b44ef513 was created 2026-10-08 09:46Z and is still Queued on server2-temp (10/10 seats occupied).
- **Recommendation:** `keep-in-review` (high). Work is in flight; the Review is queued.
- **What moves it:** Review b44ef513 dispatches (the card moves to In Progress automatically), then land cab80ac2.
- **Board state:** The card shows Review while its Review task is Queued: CardWorkTransitionService treats Queued as not open (CardWorkTransitionService.cs:34-37).
- **Related:** CARD-1135

### CARD-1145: CARD-1129 reclaim counter drops new confirmations after a backward clock step

- **Asked:** The CARD-1129 reclaim counter dropped new confirmations after a backward UTC clock step.
- **Landed on master:** 352489b66 (2026-10-07): count the confirmations this reclaim run produced (it also carries CARD-1129's 858a382d8). Active in b5e78700a.
- **Still open:** PCs pending Mutation; the rollback counter test follow-up became CARD-1147 (Done).
- **Recommendation:** `close-done` (high). Landed and active; the follow-up is closed.
- **Closing verdict:** Fixed 352489b66 (2026-10-07): the reclaim run counts the confirmation transitions it produced, not timestamps, so a backward clock step no longer drops a release. Follow-up pin CARD-1147 is Done. PCs pending Mutation.
- **Related:** CARD-1129, CARD-1147

### CARD-1148: Bind Held sync debt to a worktree registration id before superseding

- **Asked:** Bind a Held sync debt to a worktree registration id before superseding it.
- **Landed on master:** Plan 71838f79f (schema decision gate). CARD-1136 F3d (9aa57cfa8) dropped Held supersession as the interim safe behaviour.
- **Still open:** A decision on additive persistence (a migration adding registration identity to the debt and retirement records), then TestDesign and Code. CARD-1136 item 3 stays open.
- **Recommendation:** `needs-operator-decision` (high). The plan stops on a schema/migration authorisation.
- **Decision needed:** Authorise an additive migration for a worktree registration id (then TestDesign), or keep F3d's no-supersession behaviour and park the card.
- **Related:** CARD-1136, CARD-1082

### CARD-1153: Runner absence-evidence API so the CARD-1149 absent-launch hold works for a real unknown session (404 shape)

- **Asked:** A runner absence-evidence API so the CARD-1149 absent-launch hold works for a real unknown session (the 404 shape).
- **Landed on master:** S1-S3 plus repair rounds F1-F3 r2 (86cf635a5..394229df9), adopted from 809c8b49 into owner 4a4aacaa, landing 394229df9 (2026-10-08 07:49Z). Review e3d066f6 was clean (46 rows, 489 passed). Not live: none of the 20 commits is in the served build b5e78700a.
- **Still open:** Activation in order: runner first, then AppHost (Review handoff: 'an old closure log or clocks more than 30 s apart refuse new evidence'). Then CARD-1149's hold can use it. PCs.
- **Recommendation:** `keep-in-review` (high). Landed; the only open step is the pending runner and AppHost restart.
- **What moves it:** After restart-session-runner then restart-apphost, confirm /api/version equals master and the runners' build, then close with the verdict.
- **Related:** CARD-1149, CARD-1150, CARD-1151

## Method and remaining uncertainty

- **What every card read covered:** the full description from the board response; all revisions (move reasons and content-edit reasons); every bound task's role, status, landing and `nextStage`; and the result text of the last one or two tasks.
- **How "landed" was decided:** by the task landing record, or by patch identity (author time plus subject) of the task's last `[antiphon-progress ... commit=]` SHA against `origin/master`. A commit message that names no card (for example the CARD-0675 commits `d71be4788..3a8d0f273`) was attributed through its landing record only.
- **Defects called still present** were checked at the cited `origin/master` lines: CARD-0707 `AgentTaskLandService.cs:844-875`, and CARD-0997 `c590-remote.sh:2432-2434` and `c849-import-saved-donor.ps1:51`.
- **Not verified here:**
  - The CARD-0718 stats route (only the host budget view was read).
  - Whether the CARD-0817 token file is already mounted (inferred from the absence of server2 markdown-package tasks only).
  - Windmill job state for CARD-1039.
  - Live pg_stat numbers for CARD-0698 and CARD-0712.
- **Board drift:** the board kept changing during the read. CARD-1143 joined Review at 09:46Z, and its Review task was Queued at 09:58Z.
- **The 2026-10-08 backlog triage** (`docs/investigations/2026-10-08-backlog-triage.md`) is consistent with this one except for CARD-0598 (see "Board state that looks wrong", item 7).

## Not done, noted

- No card was closed, moved or edited. No follow-up cards were filed: the operator approves dispositions.
- Fix idea, one line: let `CardWorkTransitionService` route a settled Plan/TestDesign/Investigate/Debug with `next≠land` somewhere other than Review, or surface "Review, no task open for N days" on attention.
