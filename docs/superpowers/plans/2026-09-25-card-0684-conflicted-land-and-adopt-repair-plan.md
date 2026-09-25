# CARD-0684 (+ CARD-0675): a conflicted land must not block its own resolution — hold on request state, a StartRef exemption, a helper that pushes its branch, and an adopt/supersede path for a repair or rebase task's branch

Plan date: 2026-09-25. Plan task: `9b83888e` (Frontier, server2 Linux runner, worktree).
Code inspected at `e6c94f89464af8ec0cb5efa15d67665e74400042` (`origin/master` fetched 2026-09-25 02:15Z; it carries
CARD-0657 R1–R4, CARD-0693 and the CARD-0672/CARD-0688 plans; this task branch is at the same commit).
Cards: CARD-0684 `01e1e3dc-eb2f-4961-a97e-e9f99c57e760` (InProgress) and CARD-0675
`f980c996-8892-4045-9594-979eb74de795` (Backlog, folded in here as the brief asks), board
`8988ca03-7414-47ad-b0b6-51556c701703`.
Evidence: the source files named below; the desktop API read 2026-09-25 02:20Z–02:50Z — `GET /api/agent-tasks/{id}`
(events, `landRequest`, `landing`) for `27dd8efb`, `303a8c1f`, `5fc68446`, `6eed34e6`, `5dc73c10`, and
`GET /api/sessions/bb3c3443-f9a9-40c1-a2e7-f7912db2d057/transcript` (the auto-Merge helper's session); `git ls-remote
origin`, `git merge-base --is-ancestor` and `git cherry` in this worktree against the SHAs the cards name. No code was
changed, no build or test was run, no database was written.

The deliverable is three bounded, sequential Code rounds. The verification design is folded into this plan (the
dispatch says `Next stage: Code`). R3 is optional if CARD-0688 R1 lands first (see D-9).

## Summary

The card describes one night correctly in its effects and wrongly in two mechanisms, and the transcript of the
auto-Merge helper shows a fourth defect the card could not see.

- **The hold is real and keyed on the wrong thing.** `EvaluateCardSiblingBaseAsync` holds a same-card Worktree task
  while a kept sibling has `LandRequestedAt != null` (AgentTaskDispatcher.cs:3894). A conflicted land leaves that
  column set, the request `NeedsResolution` and the owner `Blocked` (AgentTaskLandService.cs:428–438), and the
  sibling query includes Blocked owners (3853). A NeedsResolution land makes no progress on its own, so the hold
  CARD-0215 designed as "the land finishes, master advances, the next tick passes" becomes a hold only the held task
  could lift. **D-1** keys the hold on the sibling's request *state* (Queued/Held/Running) and sends NeedsResolution
  to the existing warn arm.
- **The rebase task's StartRef did equal the conflicted source, and the hold still fired for a reason.** At 19:47:04
  the helper had finished `git rebase master` in the owner's worktree ("Successfully rebased and updated
  refs/heads/feat/card-task-27dd8efb"), so the kept branch's local tip was a rewritten stack, and `git cherry
  64b350d1 <rewritten>` carries one `+` — the conflict-resolved commit `15e0b903` has a new patch-id. The dispatcher's
  containment witness is the local tip; it is not stable. **D-2** adds a second witness: the sibling's
  request-recorded `ExpectedSourceSha`. A task whose start ref equals or descends from it contains the reviewed source
  by construction and is exempt from that sibling's hold and warning.
- **The auto-Merge helper started 16 seconds after dispatch, resolved the conflict, and then stopped because its brief
  contradicted itself.** Its Goal says `git fetch . {branch}:{target}` and `git push origin {target}`
  (AgentTaskService.cs:2946–2953, text from the pre-land-v2 merge-back era); the Shared/`CommitOnSettle=Never`
  arm appends "Do NOT commit or push" (DelegationReportFormatter.cs:141, 225). The delegate did the rebase, refused the
  push, ended its turn with no report marker at 19:47:53, and sat `Dispatched` — a Shared writer in the common
  directory — until the orchestrator canceled it at 19:53:38. Every land in the repository waited on it for 9 min 38 s.
  **D-3** rewrites the land-origin helper brief (resolve, push *its branch* with `--force-with-lease`, report the tip,
  never touch the target). **D-4** makes a helper a writer only while it has a live, transcript-confirmed session, which
  is what the card asks and closes the Queued/never-booted cases. **D-5** tells the owner and the orchestrator when a
  helper ends without resolving.
- **There is no supersede path, and the same-SHA requeue is unreachable for a conflicted owner.** A different
  `expectedSourceSha` on a pending request is 409 `land_request_identity_conflict` (AgentTaskLandService.cs:121–129);
  the NeedsResolution→Queued requeue at line 130 sits behind the `Succeeded` check at 87–88, and the conflict made the
  owner Blocked. `LandRequestState.Superseded` is declared and never assigned. **D-6** makes a NeedsResolution request
  supersedable by a new request. **D-7/D-8** add the adoption the two cards ask for: `-Land <owner> -ExpectedSourceSha
  <sha> -FromTask <task>` rewrites the owner's source (local checkout and remote branch, `--force-with-lease`, journaled,
  pinned, evidenced) to the from-task's pushed tip and then runs the ordinary land. Lineage is provenance (the from-task
  was dispatched from the owner's reviewed tip), because both real cases fail strict git ancestry and `git cherry`
  fails on precisely the conflict-resolved commit.
- **CARD-0675's "stale local owner branch" is the desktop placeholder.** A runner-bound task's desktop branch never
  moves from its dispatch base; a runner that rewrote its branch is `Diverged` from it and refused, which is why
  `303a8c1f`'s own first land was refused at 20:29:39. **D-9** (R3) lets the resolver reset a placeholder that provably
  holds no work. CARD-0688 D-2 (source as a ref; the task worktree never read before cleanup) removes the problem
  entirely, so R3 is skipped if 0688 R1 lands first.

## Ground truth

| The card assumes | What the code and the evidence say | Consequence |
|---|---|---|
| A NeedsResolution request "counts as landing" for the same-card hold. | True. Hold predicate is `sibling.LandRequestedAt is not null` (AgentTaskDispatcher.cs:3894) over Succeeded **and** Blocked siblings (3853). The conflict arm sets `task.Status = Blocked`, `request.State = NeedsResolution`, keeps `IsPending` and `LandRequestedAt` (AgentTaskLandService.cs:428–438). `303a8c1f` Held at 20:16:33 with exactly that text. | D-1: hold on request state, not on the column. |
| The rebase task branched at the conflicted source, so nothing could be "not yet in 64b350d1". | The request's source was 64b350d1 (`landRequest.expectedSourceSha`, `resolvedSourceSha`; local 11ea499a → `Behind` → fast-forwarded at admission), and the protocol aborts a conflicted rebase (AgentTaskLandingProtocol.cs:216–221) — so after 19:33 the local tip was 64b350d1. But the helper then rewrote it: transcript seq 14 at 19:47:04 "Successfully rebased and updated refs/heads/feat/card-task-27dd8efb", top commit `ef5f14f4`. `ContainsPatchesAsync` is `git cherry <base> <tip>` (DelegationWorktreeService.cs:176–189); `git cherry 87f261ac 64b350d1` (the same rewrite, as later landed) shows one `+` (`15e0b903`, the resolved commit). So the hold fired by its own rule. | D-2: the exemption keys on the sibling request's `ExpectedSourceSha`, which the helper cannot rewrite. |
| The auto-spawned Merge task "never started (the desktop was at 100% CPU)". | Wrong. `5fc68446`: Queued 19:33:16, held on the land lease until 19:44:00 (Dispatched), prompt delivered 19:44:28 (transcript seq 1), `git rebase master` 19:45:25, conflict resolved 19:46:22, rebase finished 19:47:04, turn ended 19:47:53 with "The brief says both 'push origin master' and 'Do NOT commit or push', so I stopped before step 3", no marker, Canceled 19:53:38. Its session `bb3c3443` has 19 transcript rows. | D-3 (brief), D-5 (surfacing). The card's remedy (2) still applies to the Queued/never-booted shape. |
| While unstarted it "counted as a repository/source writer and blocked EVERY other land". | Right about the blocking, wrong about when. `CreateMergeTaskAsync` builds it `Workspace = Shared`, `WorkingDirectory = <owner worktree>`, `RepoPath = owner.RepoPath` (AgentTaskService.cs:2960–2965). `FindWriterAsync` takes Dispatched/Working/Blocked non-specialists (AgentTaskLandService.cs:546–547) and a Shared candidate in the same common directory holds every land (561–566). Queued is not a candidate, so 19:33–19:44 blocked nothing; 19:44:00–19:53:38 blocked everything, 3 min 25 s of it while it really was rewriting the branch. | D-4: helper is a writer only with a live transcript-confirmed session; the idle-after-turn-end minutes are the report machinery's (`UnmarkedWaitingMinutes` 5) and are noted, not redesigned. |
| A helper whose launch fails should "time out back to NeedsResolution". | Partly exists. `BootReplyWatch` (`BootModelWaitDeadlineMinutes` 8) fails a session with no boot reply and the helper's `MaxAttempts = 2` retries once; the owner stays Blocked/NeedsResolution throughout. Nothing tells the owner's caller, and the attention headline "task X is resolving it" picks the newest Merge child with **no status filter** (AttentionService.cs:288–295; BlockedContextBuilder.cs:33–38), so a Canceled helper is still "resolving". | D-5: owner Warning + re-issued Conflict notification on helper Failed/Canceled; headline names only an open helper. |
| One could "supersede a NeedsResolution request with a new source instead of cancelling the task". | No path. Different SHA on a pending request → 409 `land_request_identity_conflict` (AgentTaskLandService.cs:121–129). Same-SHA requeue (130) needs `task.Status == Succeeded` (87–88), false after a conflict. `LandRequestState.Superseded` (LandingEnums.cs:17) is never assigned. Exits today: helper success → `ResolveConflictedParentAsync` sets the owner Succeeded and clears `LandRequestedAt` but leaves the request row `IsPending=true`/`NeedsResolution` (orphaned); or task cancel → `RunRequestAsync` marks the request Canceled `task_no_longer_eligible` (what `27dd8efb` shows). | D-6: supersede = a new request; the old one becomes `Superseded` with a terminal event; the owner flips to Succeeded as the helper path already does. |
| CARD-0675: `-Land 5dc73c10 -ExpectedSourceSha <repair sha>` refused `source_remote_diverged` (local a0a5b7b6, remote 8035c0ea). | True, two causes stacked. `ClassifyAsync` (AgentTaskLandSourceResolver.cs:~380) found local (the desktop placeholder = `WorktreeBaseSha` a0a5b7b6) and remote 8035c0ea diverged (`a0a5b7b6` is not an ancestor of `8035c0ea`: the runner rewrote its stack); expected fcd657c2 ≠ remote would then be `reviewed_source_mismatch`. The repair's branch (`feat/card-task-6eed34e6` = fcd657c2, still on origin) is not the owner's source ref, so no `expectedSourceSha` can select it. | D-7: adoption rewrites the owner's source to the from-task's tip, then the ordinary land runs. |
| CARD-0675: `-Land 6eed34e6` refused "must have succeeded". | True (AgentTaskLandService.cs:87–88); `6eed34e6` settled Failed `unclaimed_or_unmatched_commit`, the CARD-0657 defect, whose R1–R4 are on master since `87f261ac` (2026-09-24 20:40Z). A repair remains the wrong landing identity regardless: card binding, review evidence, `RequiresFinalVerificationReview`, cleanup of the owner's worktree all belong to the owner. | D-7 lands through the owner; the from-task must be Succeeded (CARD-0657 now makes that true for runner repairs). |
| The adopted SHA "descends from the owner's reviewed base plus master". | Not as git ancestry: `8035c0ea` ⊀ `fcd657c2` (the repair rebased the R3 stack: 563e6f17/748f5fda are rewrites of 8035c0ea/697adaee) and `64b350d1` ⊀ `87f261ac` (the rebase task). `git cherry fcd657c2 8035c0ea` is all `-` (contained); `git cherry 87f261ac 64b350d1` has one `+`. What both share: the from-task's recorded dispatch base (`worktreeBaseRequestedRef` 8035c0ea / 64b350d1) **is** the owner's remote tip at that time. | D-8: lineage is provenance (dispatch base ∈ owner's reviewed tips); patch containment is recorded evidence, never a gate. |
| Alternative: "let -RepairSource work with runner tasks and -StartRef". | Refused by design: `worktree_start_ref_mode` (AgentTaskService.cs:268–285, CARD-0613; delegate.ps1:958–961). `-RepairSource` attributes a claimed commit on the **owner's** ref (`repair_source_*` validation, 1698–1735); a runner task pushes its own branch and never writes the owner's ref. | Rejected in D-7. |
| After the cancel, `303a8c1f` could land itself normally. | Its first land at 20:29:39 was refused `source_remote_diverged local=64b350d1 remote=87f261ac`: local was its own `WorktreeBaseSha`, remote was the reviewed tip. The orchestrator reset the desktop branch by hand and re-POSTed at 20:30:27 (Landed 20:40:29 with eight unlanded-sibling warnings for the other CARD-0657 kept branches). | D-9 (R3): reset a placeholder that holds no work; bridge until CARD-0688 D-2. |
| The helper's brief is fixable without touching the protocol. | Yes. CARD-0688 keeps "the merge helper task is created by the service exactly as today and works in the task's own worktree, whose branch is untouched" (its plan, line 374). The land-origin helper is distinguishable at the call site (`CreateMergeTaskAsync(..., landingTarget)` from AgentTaskLandService.cs:430 vs the merge-back call at AgentTaskReplyService.cs:1789). | D-3 takes an explicit origin argument. |

### The night of 2026-09-24, from the events and the helper's transcript (UTC)

- **18:40:35** `27dd8efb` created (Worker/Code, CARD-0657, runner `server2`); held on land leases until **19:06:45**
  (worktree cut from `11ea499a`); **19:09:05** dispatched; **19:22:58** completed with "produced no commits on
  feat/card-task-27dd8efb" (the runner pushed `64b350d1` to origin; the desktop branch stayed at `11ea499a`).
- **19:23:54** `LandRequested expected=64b350d1`; **19:30:03** admitted; resolver: `localBeforeSha 11ea499a`,
  `remoteSourceSha 64b350d1`, `Behind`, fast-forwarded, `Resolved`; **19:33:16** `Conflicted
  tests/Antiphon.Tests/slow-tests-allowlist.txt`; the protocol aborted its rebase; owner Blocked, request
  NeedsResolution, helper `5fc68446` created (Shared, in the owner's worktree, `MaxAttempts 2`).
- **19:33:35–19:44:00** helper Queued, held on the lease behind land `492a3ddb`; **19:44:00** Dispatched;
  **19:44:28** prompt; **19:45:25** `git rebase master` → conflict at `15e0b903`; **19:46:22** resolved;
  **19:47:04** rebase finished, `feat/card-task-27dd8efb` rewritten (top `ef5f14f4`); **19:47:53** turn ended
  without a push or a marker. From 19:44:00 every land in `C:\src\Antiphon` was held `repository/source writer
  5fc68446 (Dispatched)`.
- **19:53:38** helper canceled by the orchestrator.
- **20:16:17** `303a8c1f` created with `-StartRef 64b350d1`; **20:16:33** Held "CARD-0657's kept branch
  feat/card-task-27dd8efb (task 27dd8efb) is landing and is not yet in 64b350d1…"; **20:20:32** `27dd8efb` canceled
  (request Canceled, `task_no_longer_eligible`); **20:21:57** worktree cut; **20:23:09** dispatched; **20:27:50**
  completed (the runner pushed `87f261ac`).
- **20:29:18** `LandRequested expected=87f261ac`; **20:29:39** `LandRefused source_remote_diverged local=64b350d1
  remote=87f261ac`; desktop branch reset by hand; **20:30:27** re-POST; **20:40:29** `Landed` `87f261ac` → master.
- **CARD-0675, earlier the same day.** `5dc73c10` (owner, CARD-0650, base `a0a5b7b6`, runner pushed `8035c0ea`).
  `6eed34e6` (`-StartRef 8035c0ea`) **16:50–17:08** settled Failed `unclaimed_or_unmatched_commit`; its branch is
  `fcd657c2`. **17:24:53** `-Land 5dc73c10 expected=fcd657c2`; **17:31:43** refused `source_remote_diverged
  local=a0a5b7b6 remote=8035c0ea`; by hand: desktop reset and force-push of `feat/card-task-5dc73c10` to `fcd657c2`
  (both branches sit at `fcd657c2` on origin now); **17:33:04** re-POST; **17:41:58** `Landed` (verified `822a0d16`).

## Decisions

- **D-1 — The same-card hold keys on the sibling's current land request state, not on `LandRequestedAt`.**
  `EvaluateCardSiblingBaseAsync` joins the sibling's `CurrentLandRequestId` row and holds only while that request
  `IsPending` and its `State` is `Queued`, `Held` or `Running`. A sibling whose request is `NeedsResolution` (or whose
  column is set with no pending row) falls into the existing warn arm: the task dispatches with the DispatchBase
  warning "branched from … without CARD-nnnn's kept branch …". *Reason:* the hold's contract (CARD-0215 plan, "a hold
  can never be permanent") assumed a land that finishes by itself; NeedsResolution finishes only through a new request
  (D-6) or a cancel. *Rejected:* keeping the hold and documenting the cancel — that is the deadlock the card reports.
  *Rejected:* excluding Blocked owners from the sibling query — a Blocked owner with a Queued/Running request is
  impossible today, but the query's Blocked arm is what makes a conflicted branch visible as a kept sibling, which
  CARD-0215 wanted.
- **D-2 — A task whose explicit start ref equals or descends from the sibling's request-recorded source is exempt
  from that sibling's hold and warning.** The dispatcher resolves `task.WorktreeBaseRequestedRef` once to a full SHA
  (`rev-parse --verify <ref>^{commit}` in the repo; unresolvable → no exemption, current behaviour). Per sibling, with
  `source = request.ExpectedSourceSha ?? landing.OriginalSourceSha`, `requestedSha == source ||
  IsCommitAncestorAsync(repo, source, requestedSha)` → `continue` before the tip/containment probe. *Reason:* the
  reviewed source is the fact the land was approved on; the kept branch's local tip is whatever the last process left
  there (the helper rewrote it at 19:47:04). *Rejected:* comparing the start ref with the kept tip only — that is the
  `ContainsPatchesAsync` probe that failed, and by patch-id it must fail after any conflict resolution.
- **D-3 — The land-origin Merge helper resolves on the branch, pushes the branch, reports the tip, and never touches
  the target.** `CreateMergeTaskAsync` gains an explicit `MergeHelperOrigin { MergeBack, Land }` argument (the two call
  sites already differ by `landingTarget`). For `Land` the Goal reads: `git rebase <target>`; resolve as the task
  intended; `git rebase --continue` until clean; `git push --force-with-lease=<branch>:<remoteBefore> origin <branch>`
  (the remote tip observed at conflict time is written into the Goal); report the resulting `git rev-parse HEAD` and
  each resolution; do **not** fast-forward or push `<target>` — the server lands it. The formatter's Shared arm
  (DelegationReportFormatter.cs:223–225) gets a Merge-role line instead of `DoNotCommitLine`: "This is a
  conflict-resolution seat: the rebase rewrites the task branch named in the goal; push ONLY that branch, with
  --force-with-lease, and never the target." The `MergeBack` origin keeps today's text. *Reason:* the transcript shows
  the contradiction stopped the helper; the fast-forward/push-master steps predate land-v2 and would bypass
  verification and evidence; the delegate rules forbid pushing master by hand. *Rejected:* auto re-landing on helper
  success — the resolved SHA is not the caller's approval SHA under CARD-0488; the owner's completion note carries the
  tip and the orchestrator lands it (a plain fresh request: local == remote == the tip, `Equal`).
- **D-4 — A Merge-role Shared task is a repository/source writer only while it has a live, transcript-confirmed
  session.** In `FindWriterAsync` and the pure `IsHeldBehindSharedWriter` (which takes a `Func<AgentTask,bool>
  helperIsLive`), a candidate with `Role == Merge` counts only when its `AgentSessionId` names a session with
  `EndedAt == null` and `Status` Starting/Running **and** at least one `TranscriptEntries` row of kind
  `TranscriptKinds.UserPrompt`. Queued (already excluded), Dispatched-without-prompt and ended-session helpers hold
  nothing. Every other candidate keeps the C467 matrix as is. *Reason:* the card's ask, verbatim; on 2026-09-24 it
  saves 16 s, but a helper held on the lease for 11 minutes or one that never boots must never hold lands. *Rejected:*
  dropping the helper from the common-directory arm entirely — while it works it reads local master, fetches and pushes
  shared refs; keeping it a writer while live is the conservative reading of "Landing waits for repository/source
  writer". *Rejected:* generalising to every Shared Dispatched task — a Shared Docs/Code task's first write is not
  gated by its transcript.
- **D-5 — When a land-origin helper ends without resolving, the owner says so.** On the helper's terminal Failed or
  Canceled (settlement and the cancel endpoint): a `Warning` on the owner "merge helper <id> ended <status>; land
  request <id> stays NeedsResolution; resolve with `-Land <owner> -ExpectedSourceSha <sha> -FromTask <task>`, a new
  helper (`-Retry <helper>`), or `-Cancel <owner>`", a re-issued `Conflict` land notification (same body, new row), and
  the attention headline / blocked context name a helper only when it is Queued/Dispatched/Working/Blocked. The boot
  deadline and the helper's one retry are unchanged. *Reason:* "times out back to NeedsResolution" in the card is
  visibility: the owner already is NeedsResolution; nobody was told the helper was gone.
- **D-6 — A NeedsResolution request is superseded by a new request, never edited.** `RequestAsync` admits an owner
  that is `Blocked` with a pending `NeedsResolution` request (today 409 "must have succeeded"): with the same
  `expectedSourceSha` it is the existing requeue (line 130) plus the Blocked→Succeeded flip; with a different SHA or an
  `adoptFromTaskId` it supersedes — old request `State = Superseded`, `IsPending = false`, `TerminalEventId` = a new
  `LandSuperseded` event ("superseded by request <id>: expected <sha>[, adopting <from>]"), owner `Succeeded`,
  `FailureReason = null` (as `ResolveConflictedParentAsync` does), new request with `SupersedesRequestId`. An open
  land-origin helper that is Queued, Dispatched-without-prompt, or whose session has ended is Canceled with that
  detail; a live working helper is 409 `merge_helper_active` (cancel it first). A pending Queued/Held/Running request
  keeps 409 `land_request_identity_conflict`. `ResolveConflictedParentAsync` also marks the request `Superseded` with
  the `Merged` event as terminal instead of leaving it pending-orphaned. *Reason:* request identity is what the
  checkpoint baseline, the C467 tests and the notifications key on; a new row keeps every existing invariant.
  *Rejected:* mutating `ExpectedSourceSha` in place.
- **D-7 — Adoption rewrites the owner's source to the adopted SHA under the land lease, then the ordinary land
  runs.** `POST /land/v2 { expectedSourceSha: S, adoptFromTaskId: F, reviewEvidenceId?, verify? }`. Request-time (DB
  only): `F != owner`; `F` is a Worktree task with `WorktreeBranch`, same `CardId`, same git common directory (the
  `SameCommonDirectoryAsync` check RepairSource uses), `Status == Succeeded`, no pending land request of its own, not a
  Mutation/SourceLanding row — codes `adopt_source_self`, `adopt_source_not_found`, `adopt_source_invalid`,
  `adopt_source_not_succeeded`, `adopt_source_landing`. `reviewEvidenceId`, when given, may name a clean Review whose
  subject is **either** the owner or `F` at `S` (`LandApproval.LoadUsableEvidenceAsync` takes the allowed subjects);
  the owner's `RequiresFinalVerificationReview` gate is unchanged. Resolver (under the lease, before the ordinary
  observation, re-entrant on retry):
  1. observe `refs/heads/<F.branch>` at the push endpoint (`ObserveSourceAsync`, prefix
     `…/adopt-observed`; the pin makes `S` local) — tip must equal `S`, else `adopt_source_not_remote_tip`;
  2. observe the owner's `refs/heads/<owner.branch>` → `B_remote` (or `source_remote_missing` → the owner was never
     pushed and `B = local tip`); lineage per D-8, else `adopt_source_lineage`;
  3. the owner checkout must pass `InspectAsync(IdentityAndStatus)` (clean, on its branch, no active sequencer) —
     existing reasons (`source_dirty`, `active_sequencer`, …) refuse before any mutation;
  4. pins `adopt-local-before`, `adopt-remote-before` (when present), `adopt-source = S` under the request's
     recovery prefix;
  5. journaled child `source-adopt-reset` (`RunOwnedAsync`, same columns as `source-ff`): `reset --hard S` in the owner
     checkout, skipped when the local tip is already `S`; inspect → `HeadSha == S` else `source_changed`;
  6. journaled child `source-adopt-push` through a new `ILandingGit.PushSourceOwnedAsync(repository, sourceFullRef, sha,
     expectedRemoteSha?, started, ct)`: `push <endpoint> --force-with-lease=<sourceFullRef>:<B_remote or empty>
     <S>:<sourceFullRef>`; a rejected lease is `adopt_source_push_rejected` (the remote moved; a re-POST re-observes);
     re-observe → `S`, else `adopt_source_push_unconfirmed`;
  7. record `AdoptedAt`, `AdoptionBaseSha = B`, `AdoptionRelationship`, `AdoptSourceRemoteSha = S`,
     `AdoptionPatchesContained` + `AdoptionUncontainedPatches` (D-8 evidence), then fall through to the ordinary path,
     which now sees local == remote == `S` → `Equal` → `Resolved` → the operation with `OriginalSourceSha =
     ReviewedSourceSha = S`, `AdoptedFromTaskId`, `AdoptionBaseSha`; a `SourceAdopted` event on the owner ("adopted
     <S> from task <F> (<branch>) over <B>; relationship=…; uncontained=…") and on `F` ("adopted into the land of
     <owner>, request <id>"); the `Landed` line gains `adopted-from=<F short> base=<B>`.
  With no `adoptFromTaskId`, a supersede with a different SHA is a **local adoption**: `S` must equal the owner
  checkout's tip (`adopt_local_tip_mismatch`), provenance is a Merge child of the owner created after the superseded
  request's `Conflicted` event (`adopt_local_provenance` otherwise), steps 5–7 run with the reset skipped. This is the
  recovery for exactly what `5fc68446` left behind: a finished rebase, nothing pushed, helper gone.
  *Reason for rewriting the owner's source rather than landing from `F`'s ref:* the protocol, its cleanup, the request
  coordinate snapshots (`request_coordinates_changed`) and `CollectUnlandedSiblingsAsync` all key on the owner's
  branch and worktree; landing from another ref would need a two-worktree cleanup and a second coordinate set. The
  rewrite is what the orchestrator did by hand twice on 2026-09-24, now leased, journaled, pinned and evidenced.
  *Rejected:* `-RepairSource` on runner tasks with `-StartRef` (ground truth row 9). *Rejected:* `-Land <F>` (row 7).
  *Rejected:* a server-side auto-adopt of a Succeeded `-StartRef` child — no caller approval SHA.
  *Under CARD-0688* (source as a ref, the task worktree never touched before cleanup), step 5 becomes a guarded
  `update-ref refs/heads/<owner> S <localBefore>` and the task worktree is left desynced for 0688's cleanup, which
  deletes the branch at the SHA it has (0688 D-6); step 3's clean-checkout requirement then drops. This is the one
  coordination point between the two plans; whichever lands second adapts S8.
- **D-8 — Lineage is provenance; patch containment is evidence.** Adoption requires `F.WorktreeBaseSha ∈ {B_remote,
  owner local tip, supersededRequest.ExpectedSourceSha}` or `IsAncestor(one of those, F.WorktreeBaseSha)` (a chain of
  repairs). `AdoptionRelationship` is `Descendant` when `IsAncestor(B, S)`, else `Rewritten`. `git cherry S B` is
  recorded (`AdoptionPatchesContained`, and the `+` SHAs in `AdoptionUncontainedPatches` and the `SourceAdopted`
  event) so a reviewer sees which reviewed patches changed under the resolution. *Reason:* both real cases fail
  ancestry; containment fails on the resolved commit by construction; the dispatch base is the one fact the server
  recorded when it cut the from-task's worktree (`WorktreeBaseSha`, immutable). *Rejected:* ancestry or containment as
  a gate (refuse the very cases the cards are about). *Rejected:* no lineage check (any same-card branch could replace
  a reviewed source).
- **D-9 — (R3, optional) A runner-bound task's desktop placeholder is reset to the reviewed remote tip when it
  provably holds no work.** In the resolver's `Diverged` arm: if `task.RunnerId != null`, `local.HeadSha ==
  task.WorktreeBaseSha`, `expected == observed remote`, and the checkout is clean, advance with a journaled
  `source-reset` (`reset --hard <expected>`) instead of refusing; `SourceRelationship` stays `Diverged` and
  `SourceAdvanceChildOperation = "source-reset"` records the path. Anything else keeps `source_remote_diverged`.
  *Reason:* `303a8c1f` at 20:29:39, and any runner delegate that rebases its own branch (which the briefs sometimes
  tell it to do). *Rejected:* "remote wins for runner tasks" — a desktop-side commit on the placeholder would be lost.
  *Skip rule:* if CARD-0688 R1 (source as a ref; no local placeholder read) has landed when R3 is dispatched, S11 is
  dropped and this decision is closed as superseded.
- **D-10 — Enum additions are appended, never renumbered.** `AgentTaskEventType.LandSuperseded = 38`,
  `SourceAdopted = 39` (after `HeldAged = 37`); `LandRequestState.Superseded` (exists) becomes live; new
  `LandSourceAdoptionRelationship { None = 0, Descendant = 1, Rewritten = 2 }`; new `MergeHelperOrigin { MergeBack,
  Land }` (in-process only).
- **D-11 — CLI.** `delegate.ps1 -Land <owner> -ExpectedSourceSha <sha> -FromTask <id>`: `-FromTask` accepts a GUID or a
  short id; a short id is resolved through `GET /api/agent-tasks/{short}` before the POST so the body carries a GUID
  (`adoptFromTaskId`); `-FromTask` without `-ExpectedSourceSha` is exit 1 with zero POSTs; `-Status` prints
  `adopted from <short> over <base> (<relationship>)` when the request or landing carries it.
- **D-12 — Three sequential Code rounds.** R1 = D-1…D-6 without adoption (S1–S5; small; ends the deadlock and the
  helper's dead end). R2 = D-7, D-8, D-10, D-11 (S6–S10; entities, migration, resolver, CLI, docs). R3 = D-9 (S11),
  skipped under the D-9 rule. R1 and R2 both edit `AgentTaskLandService.cs` and the resolver, which CARD-0688 also
  edits; the rounds are small enough to rebase over either order.

## Design

### R1 — the hold, the helper, the writer, the surfacing

**Dispatcher (S1).** `EvaluateCardSiblingBaseAsync` (AgentTaskDispatcher.cs:3838) selects, per sibling,
`CurrentLandRequestId` and reads that request's `IsPending`/`State`/`ExpectedSourceSha` (one query over
`AgentTaskLandRequests` for the sibling set) and, when the request has an operation, the landing's
`OriginalSourceSha`. Before the tip probe: the D-2 exemption. After the probe: `hold ??= unlanded` only when
`request is { IsPending: true, State: Queued or Held or Running }`; otherwise `warnings.Add(unlanded)`. `UnlandedSibling`
gains `RequestState` so the Held/Warning text can say `(request <short> NeedsResolution)`. `HoldKind.SiblingLanding`
is unchanged. `DelegationWorktreeService` gains `ResolveCommitAsync(repo, revision)` (rev-parse, full SHA or null) for
the start-ref resolution; `IsCommitAncestorAsync` already exists.

**Helper brief (S2).** `CreateMergeTaskAsync(conflicted, conflictFiles, ct, MergeHelperOrigin origin, string?
landingTarget = null, string? remoteBeforeSha = null)`; the land call site passes `Land` and
`request.RemoteSourceSha`; the merge-back call site passes `MergeBack`. The Goal template for `Land` is in D-3. The
formatter arm at DelegationReportFormatter.cs:223 tests `task.Role == AgentTaskRole.Merge` first.

**Writer rule (S3).** `FindWriterAsync` computes, for Merge-role candidates only, a live set with one query:
sessions `EndedAt == null && Status in (Starting, Running)` joined to `TranscriptEntries.Any(Kind ==
TranscriptKinds.UserPrompt)`. `IsHeldBehindSharedWriter(landing, candidates, helperIsLive)` keeps its pure shape.

**Surfacing and hygiene (S4).** `ResolveConflictedParentAsync` (AgentTaskReplyService) marks the request
`Superseded`, `IsPending=false`, `TerminalEventId` = the `Merged` event, and writes the owner checkout tip into the
`Merged` detail ("Conflict resolved by merge task <id>; branch tip <sha>; land with -ExpectedSourceSha <sha>").
Helper terminal Failed/Canceled: a hook where a task settles Failed (settlement) and where `Cancel` writes `Canceled`
calls `AgentTaskLandService.NotifyHelperEndedAsync(helper, ct)` which writes the owner Warning and re-issues the
`Conflict` notification for the owner's current NeedsResolution request. AttentionService and BlockedContextBuilder
filter the Merge children to open statuses.

### R2 — supersede and adopt

**Entities and DTOs (S6).** `AgentTaskLandRequest`: `AdoptFromTaskId Guid?`, `AdoptSourceFullRef string?`,
`SupersedesRequestId Guid?`, `AdoptionBaseSha string?`, `AdoptionRelationship LandSourceAdoptionRelationship`,
`AdoptedAt DateTime?`, `AdoptSourceRemoteSha string?`, `AdoptionPatchesContained bool?`, `AdoptionUncontainedPatches
string?`. `AgentTaskLanding`: `AdoptedFromTaskId Guid?`, `AdoptionBaseSha string?`, `AdoptionRelationship`.
`LandSourceCheckpointBaseline`/`LandSourceCheckpointPatch` carry the adoption columns the resolver writes (the writer
applies only patch columns; anything else is lost on the reload at AgentTaskLandRequestWriter.cs:131). DTOs:
`LandAgentTaskRequest.AdoptFromTaskId`, `LandRequestStatusDto` and the landing DTO expose the new fields.
Migration `AddLandSourceAdoption` (`dotnet tool restore; dotnet ef migrations add AddLandSourceAdoption --project
server`, per docs/testing-and-build.md:267).

**Request (S7).** The `RequestAsync` flow in D-6/D-7. Order: repair/mutation/workspace refusals as today → status
admission (Succeeded, or Blocked with a pending NeedsResolution request and a Conflicted latest event) → `land_running`
→ pending-request arm (same SHA and no adopt: requeue; different SHA or adopt: supersede) or fresh arm → adopt
validation → evidence → new row (`SupersedesRequestId`, `AdoptFromTaskId`, `AdoptSourceFullRef`) → helper handling →
events → commit → enqueue.

**Resolver (S8).** A new private `AdoptAsync` runs after the coordinate/lease checks and before the `SourceAdvance`
journal handling, when `request.AdoptFromTaskId != null || request.SupersedesRequestId != null &&
request.AdoptSourceFullRef == null` and `AdoptedAt == null`; it is re-entrant by observing local/remote first (local
already `S` → skip the reset; remote already `S` → skip the push). Its children use the same journal columns as
`source-ff` with operation names `source-adopt-reset` / `source-adopt-push`, so the existing
`interrupted_process_requires_inspection` arm covers an interrupted adoption. `LandingGit.PushSourceOwnedAsync` is the
only method that ever pushes a source ref and the only one that ever emits `--force-with-lease`;
`C488_PublicationNeverMutatesRemoteSource` keeps asserting the target push never carries `--force` or `+`.
`ControlledLandingGit` and `LandingGitFixture` gain the method (trace + real push) and a `RemoteSourceSha` reader for
assertions.

**CLI and docs (S9, S10).** D-11; docs/ops-http.md:103 land row (body, codes, `Superseded`), docs/orchestration-loop.md
("Also delegated: the landing mechanics" paragraph gains the NeedsResolution exits; the Merge row at line 334; the
"Repair source" paragraph points at adoption; the "Continuing a sibling's work" paragraph says a `-StartRef` task's
branch is landed through the owner with `-FromTask`), docs/session-runtime-invariants.md Gotcha #81 ("the only
source-branch mutation a land performs is an adoption push with `--force-with-lease`, leased, journaled, pinned,
evidenced"); CARD-0675 is closed by this card's land (its asks are D-7/D-8 and V-14/V-18).

### R3 — the placeholder

**Resolver (S11).** D-9 in the `Diverged` arm of `ResolveAsync` (AgentTaskLandSourceResolver.cs:~190): the
`source-reset` child reuses the `needFf` block with `reset --hard <expected>` in place of `merge --ff-only`; the
before/after inspections and `RecheckRemoteAsync` calls are the same.

### Slices

- **S1 — Dispatcher hold and exemption** (R1): `AgentTaskDispatcher.cs` (`EvaluateCardSiblingBaseAsync`,
  `UnlandedSibling`), `DelegationWorktreeService.cs` (`ResolveCommitAsync`). Tests: `AgentTaskDispatchBaseGuardTests`
  V-1…V-4.
- **S2 — Helper brief** (R1): `AgentTaskService.cs` (`CreateMergeTaskAsync`, `MergeHelperOrigin`),
  `AgentTaskLandService.cs:430`, `AgentTaskReplyService.cs:1789`, `DelegationReportFormatter.cs:223`. Tests: new
  `MergeHelperBriefTests` V-7; `AgentTaskLandStageOutcomeTests` (regression, it calls the factory).
- **S3 — Writer rule** (R1): `AgentTaskLandService.cs` (`FindWriterAsync`, `IsHeldBehindSharedWriter`). Tests:
  `DelegationWorktreeTests` V-5, `AgentTaskLandHoldVisibilityTests` V-6.
- **S4 — Surfacing and request hygiene** (R1): `AgentTaskReplyService.cs` (`ResolveConflictedParentAsync`, Failed
  settlement hook), the cancel path in `AgentTaskService.cs`, `AgentTaskLandService.cs` (`NotifyHelperEndedAsync`),
  `AttentionService.cs:288`, `BlockedContextBuilder.cs:33`. Tests: new `MergeHelperResolutionTests` V-8, V-9;
  `AgentTaskDetailBlockedContextTests` V-9b.
- **S5 — Docs R1**: docs/orchestration-loop.md (hold rule, helper contract, Merge row), docs/ops-http.md (writer note).
- **S6 — Entities, migration, DTOs, checkpoint columns** (R2): `AgentTaskLandRequest.cs`, `AgentTaskLanding.cs`,
  `LandingEnums.cs`, `AgentTaskEnums.cs`, `AgentTaskDtos.cs:594`, `LandRequestStatusDto.cs`, the landing DTO,
  `AgentTaskLandRequestWriter.cs`, migration `AddLandSourceAdoption`.
- **S7 — Request supersede and adopt validation** (R2): `AgentTaskLandService.cs` (`RequestAsync`),
  `LandApproval.LoadUsableEvidenceAsync`. Tests: `AgentTaskLandRequestTests` V-10…V-13.
- **S8 — Resolver adoption** (R2): `AgentTaskLandSourceResolver.cs` (`AdoptAsync`), `ILandingGit.cs` +
  `LandingGit.cs` (`PushSourceOwnedAsync`), `ControlledLandingGit.cs`, `LandingGitFixture.cs`, `AgentTaskLandService.cs`
  (`SourceAdopted` events, outcome line). Tests: new `AgentTaskLandAdoptionTests` V-14…V-21;
  `LandingSourceFreshnessTests` V-21b.
- **S9 — CLI** (R2): `scripts/delegate.ps1` (Land parameter set 274–284, Land block 720–770, Status output). Tests: new
  `DelegateScriptAdoptTests` V-23 (through `DelegateScriptRunner`).
- **S10 — Docs R2**: docs/ops-http.md, docs/orchestration-loop.md, docs/session-runtime-invariants.md.
- **S11 — Placeholder reset** (R3): `AgentTaskLandSourceResolver.cs`, `ControlledLandingGit.cs`. Tests:
  `LandingSourceFreshnessTests` V-22; docs line in ops-http.

### Code rounds

- **R1** (S1–S5): one Code dispatch, `-ExpectAbout` per the Cost block; Review ordinary; land.
- **R2** (S6–S10): one Code dispatch after R1 has landed (S7 builds on S4's `Superseded` handling); Review checks the
  push trace assertions (V-21, V-21b) personally; land.
- **R3** (S11): dispatched only if CARD-0688 R1 has not landed; otherwise the orchestrator records D-9 as superseded on
  the card.

## Migration and rollback

`AddLandSourceAdoption` adds nullable columns and an int column with default 0 to `AgentTaskLandRequests` and
`AgentTaskLandings`; no data migration; no index. Rolling back R2 code without the migration is safe (unused columns).
The enum values are appended (D-10). R1 has no migration. The helper Goal change affects only helpers created after
the deploy. A NeedsResolution request created before R1 is handled by R1's rules on the next tick (the dispatcher
reads state, not history).

## Verification design

Test project `tests/Antiphon.Tests`; TUnit via `dotnet run --project`; real-git classes carry
`[ParallelLimiter<ProcessSpawnLimit>]` and `Slow` where the file already does. Every new test is written red first
against the production line it guards; the red evidence is the first commit of each slice.

### Round 1

- **V-1** `AgentTaskDispatchBaseGuardTests.a_needs_resolution_sibling_warns_and_dispatches`: kept sibling with
  `LandRequestedAt` set and a current request `NeedsResolution`; tick → task Dispatched, one DispatchBase Warning
  naming the sibling and `NeedsResolution`, zero `Held`. Red today: Held.
- **V-2** `…a_start_ref_equal_to_the_sibling_request_source_is_exempt`: sibling request `Running` with
  `ExpectedSourceSha = tip`; task `WorktreeBaseRequestedRef = tip`; tick → Dispatched from `tip`, no Held, no Warning.
  Not red today (an equal tip is patch-contained); kept as the guard that the exemption does not change the
  contained case. V-4 is the red one.
- **V-3** `…a_start_ref_descending_from_the_sibling_request_source_is_exempt`: task base = a commit on top of the
  sibling source; same expectations.
- **V-4** `…a_rewritten_kept_tip_does_not_hold_a_start_ref_at_the_request_source` (the 303a8c1f replay): sibling
  request `Running`/`NeedsResolution` with `ExpectedSourceSha = tip`; then rewrite the kept branch (`git rebase` onto a
  new master commit with a resolved change so the patch-id differs); task base = `tip`; tick → Dispatched, no Held.
  Red today: Held "is landing and is not yet in <tip>".
- **V-5** `DelegationWorktreeTests.land_shared_writer_hold_ignores_a_merge_helper_without_a_live_prompt`: pure
  `IsHeldBehindSharedWriter` with a Merge/Shared/Dispatched candidate and `helperIsLive: _ => false` → false;
  `_ => true` → true; a Code/Shared/Dispatched candidate → true regardless. Red today: no predicate overload.
- **V-6** `AgentTaskLandHoldVisibilityTests.C684_MergeHelperWriterMatrix(state)` `[Arguments]`:
  `queued`, `dispatched-no-prompt`, `dispatched-prompt`, `working-prompt`, `ended-session` → hold only for
  `dispatched-prompt` and `working-prompt` with `repository_or_source_writer`; others admit. Red today: every
  Dispatched/Working helper holds.
- **V-7** `MergeHelperBriefTests.a_land_origin_helper_pushes_its_branch_and_never_the_target` and
  `…a_merge_back_helper_keeps_the_legacy_goal`: the Goal contains `push --force-with-lease=<branch>:<remoteBefore>
  origin <branch>` and `rev-parse HEAD`, not `fetch .` nor `push origin master`; the rendered brief contains the
  Merge-role line and not `DoNotCommitLine`. Red today: both.
- **V-8** `MergeHelperResolutionTests.a_finished_helper_supersedes_the_request_and_names_the_tip`: the request is
  `Superseded`/not pending with `TerminalEventId` = the `Merged` event, whose detail carries the branch tip. Red today:
  request stays pending NeedsResolution.
- **V-9** `…a_failed_or_canceled_helper_warns_the_owner_and_reissues_the_conflict_note` `[Arguments(Failed)]`,
  `[Arguments(Canceled)]`: owner gets one Warning naming the helper and the `-FromTask` recovery; a second `Conflict`
  notification row exists for the same request. **V-9b** `AgentTaskDetailBlockedContextTests` /
  `AttentionServiceTests`: a Canceled helper is not "resolving it"; an open one is.
- **R-1** existing, kept green: `AgentTaskDispatchBaseGuardTests` (11 + 13 in the SiblingWarnings partial),
  `DispatchBaseNotificationTests`, `ExpectationPipelineTests`/`ExpectationSnapshotTests` (they quote the Held text),
  `AgentTaskLandHoldVisibilityTests` C467_V03 matrix, `AgentTaskLandRequestTests`, `AgentTaskLandMonitoringTests`,
  `AgentTaskLandStageOutcomeTests`, `DelegationWorktreeTests`.

### Round 2

- **V-10** `AgentTaskLandRequestTests.C684_AdoptValidationCodes` `[Arguments]`: self, missing, other card, other repo,
  not Worktree, Failed, from-task with pending land → the D-7 codes; no row created. Red today: `adoptFromTaskId` is
  not a property (compile red first), then 409 codes.
- **V-11** `…C684_SupersedeNeedsResolution`: owner Blocked with a NeedsResolution request and a Queued helper; POST
  with a new SHA + `adoptFromTaskId` → 202 `queued`; old request `Superseded`, `IsPending=false`, terminal
  `LandSuperseded` event; new request has `SupersedesRequestId`; owner Succeeded, `FailureReason` null; helper
  Canceled with the supersede detail. Red today: 409 "must have succeeded".
- **V-12** `…C684_LiveHelperRefusesSupersede`: helper Dispatched with a UserPrompt transcript row and a live session →
  409 `merge_helper_active`; nothing changes.
- **V-13** `…C684_QueuedRequestKeepsIdentityConflict` and `…C684_SameShaOnConflictedOwnerRequeues`: a pending
  Queued request with a different SHA → 409 `land_request_identity_conflict`; same SHA on a Blocked-conflicted owner →
  `requeued`, owner Succeeded.
- **V-14** `AgentTaskLandAdoptionTests.C684_RepairAdoptionDescendant` (`LandingSafetyHarness`, real git): owner
  branch pushed at `B`, desktop checkout left at the placeholder base `P ≠ B` (stale local, CARD-0675); from-task cut
  at `B`, two commits, pushed `S`; POST adopt → resolver: checkout tip `S`, remote source `S`, `adopt-*` pins present,
  request `AdoptionRelationship = Descendant`, `AdoptionPatchesContained = true`, `SourceAdopted` events on both rows,
  operation `OriginalSourceSha == S` with `AdoptedFromTaskId`; the trace shows exactly one push of the source ref with
  `--force-with-lease=refs/heads/<owner>:<B>`.
- **V-15** `…C684_RebaseAdoptionRewritten`: from-task cut at `B`, rebased onto a newer target with one resolved
  conflict, pushed `S` (`B` not an ancestor of `S`); adopt → `Rewritten`, `AdoptionPatchesContained = false`,
  `AdoptionUncontainedPatches` names the resolved commit; the land then proceeds to `Landed`.
- **V-16** `…C684_LineageRefused`: from-task cut at a commit that is neither `B` nor a descendant of it →
  `adopt_source_lineage`; no reset, no push, remote source unchanged.
- **V-17** `…C684_NotRemoteTipRefused`: `expectedSourceSha` = a commit behind the from-branch tip →
  `adopt_source_not_remote_tip`.
- **V-18** `…C684_DirtyOwnerCheckoutRefusesBeforeMutation`: dirty owner checkout → `source_dirty`; no pins, no reset,
  remote unchanged (the CARD-0675 "stale local" variant that must not lose work).
- **V-19** `…C684_PushLeaseRejectedThenRetried`: move the owner's remote between observation and push (fixture hook) →
  `adopt_source_push_rejected`, local left at `S` and pinned; re-POST → adoption completes with the new lease.
- **V-20** `…C684_LocalAdoptionAfterHelperRewrite`: owner checkout rewritten by a (seeded) Merge child after the
  Conflicted event, nothing pushed; supersede with `expectedSourceSha` = the local tip and no `adoptFromTaskId` →
  remote pushed with lease, land proceeds; without a Merge child → `adopt_local_provenance`; with a different tip →
  `adopt_local_tip_mismatch`.
- **V-21** `…C684_OnlyTheSourcePushForces`: after a full adopted land, the trace contains exactly one
  `--force-with-lease` (the source push) and the target push carries neither `--force` nor `+`. **V-21b**
  `LandingSourceFreshnessTests.C488_PublicationNeverMutatesRemoteSource` unchanged and green.
- **V-23** `DelegateScriptAdoptTests`: `-Land <owner> -ExpectedSourceSha <sha> -FromTask <short>` → one GET
  resolving the short id, one POST whose body has `adoptFromTaskId` as a GUID; `-FromTask` without
  `-ExpectedSourceSha` → exit 1, zero POSTs; `-Status` prints the adoption line.
- **R-2** existing, kept green: `LandingProtocolHarnessTests`, `AgentTaskLandSourcePersistenceTests`,
  `ControlledLandingGitTests`, `LandingSourceFreshnessTests`, `AgentTaskLandHoldVisibilityTests`,
  `DelegateScriptLandStatusTests`, `DelegationWorktreeTests`.

### Round 3

- **V-22** `LandingSourceFreshnessTests.C684_RunnerPlaceholderResetAdvance` `[Arguments]`: runner task, local ==
  `WorktreeBaseSha`, remote rewritten and == expected → `Resolved`, `SourceAdvanceChildOperation` was `source-reset`,
  checkout at expected; non-runner → `source_remote_diverged`; local ≠ base → `source_remote_diverged`; dirty →
  `source_dirty`.

### Checkpoints

Isolated outputs `bin-c684a/` (R1), `bin-c684b/` (R2), `bin-c684c/` (R3), forward slash; one build per round, every
other row `--no-build`; on server2 `run-checkpoint.ps1` adds `UseAppHost=false` itself. `Min` is the count of `[Test]`
methods in the named classes at `e6c94f89` (argument-expanded rows counted per argument) plus the new methods, minus
a 10 % margin; a floor, not a census.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c684a/` | dispatch-base-guard | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(DispatchBaseNotificationTests*)/*` | V-1, V-2, V-3, V-4, R-1 | all listed, 0 failed | 45 | 10 |
| CP-2 | S2-S4 | CP-1 | helper-and-writer | `/*/*/(MergeHelperBriefTests*)\|(MergeHelperResolutionTests*)\|(DelegationWorktreeTests*)\|(AgentTaskLandHoldVisibilityTests*)\|(AgentTaskDetailBlockedContextTests*)/*` | V-5, V-6, V-7, V-8, V-9, V-9b, R-1 | all listed, 0 failed | 60 | 10 |
| CP-3 | S1-S4 | CP-1 | land-regression-r1 | `/*/*/(AgentTaskLandRequestTests*)\|(AgentTaskLandMonitoringTests*)\|(AgentTaskLandStageOutcomeTests*)\|(ExpectationPipelineTests*)\|(ExpectationSnapshotTests*)/*` | R-1 | all listed, 0 failed | 60 | 8 |
| CP-4 | S5 | n/a | docs-r1 | `git grep -n -e "NeedsResolution" -e "merge_helper_active" -e "force-with-lease" -- docs/orchestration-loop.md docs/ops-http.md` | S5 | ≥ 3 matching lines, exit 0 | n/a | 1 |
| CP-5 | S6-S8 | `tests/Antiphon.Tests -> bin-c684b/` | adoption | `/*/*/(AgentTaskLandAdoptionTests*)\|(AgentTaskLandRequestTests*)\|(LandingSourceFreshnessTests*)/*` | V-10 … V-21, V-21b | all listed, 0 failed | 50 | 12 |
| CP-6 | S6-S8 | CP-5 | land-regression-r2 | `/*/*/(LandingProtocolHarnessTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(ControlledLandingGitTests*)\|(AgentTaskLandHoldVisibilityTests*)\|(DelegationWorktreeTests*)/*` | R-2 | all listed, 0 failed | 130 | 12 |
| CP-7 | S9 | CP-5 | delegate-script | `/*/*/(DelegateScriptLandStatusTests*)\|(DelegateScriptAdoptTests*)/*` | V-23, R-2 | all listed, 0 failed | 22 | 5 |
| CP-8 | S10 | n/a | docs-r2 | `git grep -n -e "adoptFromTaskId" -e "adopt_source_lineage" -e "Gotcha #81" -- docs/ops-http.md docs/orchestration-loop.md docs/session-runtime-invariants.md` | S10 | ≥ 3 matching lines across the three files, exit 0 | n/a | 1 |
| CP-9 | S11 | `tests/Antiphon.Tests -> bin-c684c/` | placeholder | `/*/*/(LandingSourceFreshnessTests*)\|(AgentTaskLandSourcePersistenceTests*)/*` | V-22, R-2 | all listed, 0 failed | 55 | 8 |

The pipe characters inside the `Filter` cells are escaped for the table; the command line uses a plain `|`, quoted as
[docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403) shows. Run each row with
`scripts/run-checkpoint.ps1 -Name CP-n -Project tests/Antiphon.Tests -OutputPath bin-c684x/ -Filter '<filter>'
-MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c684-checkpoints` (`-NoBuild` for reuse rows). R1 runs
CP-1…CP-4; R2 runs CP-5…CP-8; R3 runs CP-9. Unlisted runs need a stated reason; a compile error found by a row's own
build is fixed and the same `CP-n` rerun. A new test that cannot go red against the production line it guards is a
stub (rule 4). Delete every `bin-c684*` directory before finishing.

### Cost

Ordinary Code floor: R1 = 10 + 10 + 8 + 1 = 29 minutes of checkpoints plus authoring (S1–S5 are ~250 lines across
six files; `-ExpectAbout 120`). R2 = 12 + 12 + 5 + 1 = 30 minutes of checkpoints plus the migration and the
real-git adoption tests (S6–S10 are ~600 lines; `-ExpectAbout 180`). R3 = 8 minutes plus ~80 lines (`-ExpectAbout 45`),
or zero under the D-9 skip rule.

## Follow-ups (not in this card)

- **Retire the from-task's worktree and branch at adoption cleanup.** After V-15 the from-task's branch is
  patch-contained in the landed SHA (no unlanded-sibling warning), but its worktree stays for the residue sweep
  (CARD-0459). A cleanup extension that removes both at the from-task's recorded tip belongs with CARD-0688's cleanup
  contract, not here.
- **A `-StartRef` task's completion note should print the `-FromTask` land line** when its card's landing owner is
  NeedsResolution, so the orchestrator never has to compose it.
- **The eight "not an ancestor of the rebased HEAD" warnings on `303a8c1f`'s Landed event** are the other CARD-0657
  kept branches (R1–R4 repairs and verification records) that the collapse rule of CARD-0215 does not reduce at land
  time; a card for reducing landed-event sibling noise the same way the dispatch warning is reduced.
