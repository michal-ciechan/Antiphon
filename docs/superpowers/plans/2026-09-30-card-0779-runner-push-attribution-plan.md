# CARD-0779: runner push attribution — publish the mirror before a no-progress verdict, and stop asking runner tasks to rebase

Date: 2026-09-30. Stage: Plan, with the verification design folded in (the brief asked for the
closed `### Checkpoints` table). Next: Code. Baseline: `a8b4e9e5` on `feat/card-task-6f9ca8b4`
(clean tree). Card `bdf88d5e-af66-4689-8c51-0fa68c60e84e`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`, read with `scripts/card.ps1 get CARD-0779`. This task ran
on the `server2-temp` runner; every live probe below was made from that container against the
production API (`GET /api/agent-tasks/{id}`, `GET /api/sessions/{id}/transcript`) and against
GitHub (`git ls-remote`, `git fetch <sha>`), on 2026-09-30 between 00:30 and 01:10 UTC.

## Outcome and scope

The card asks for an investigation first: is there a common trigger, and which of the 2026-09-27
failures were genuine lost work versus false-positive blocks. That investigation is done here and
its answer changes the fix. There is one root cause behind both families, and it is not push
transport, GitHub replication, worktree contention, the rolling upgrade, or dispatcher polling:

**Code briefs on 2026-09-27 asked runner tasks to rebase their task branch onto master.** A runner
task branch is fast-forward-only from its dispatch baseline B (CARD-0657 D-3, kept deliberately by
CARD-0753 V-1). A rebased tip either (a) cannot be published by a plain `git push` (non-fast-forward
rejection), so it stays only in the runner mirror, settlement sees origin still at B and fails the
task `runner_no_pushed_progress` ("genuine loss": the commit never reached origin), or (b) is
force-pushed, so origin's tip S no longer descends from B and settlement refuses
`runner_sync_diverged` ("false-positive block": the work is on origin but the guard is doing its
job). The follow-up "land-owner recovery" tasks that only re-verified an existing tip then failed
`runner_no_pushed_progress` by policy (no new commit), which the card also counts.

Three things turn (a) from a recoverable state into destroyed work today: settlement never looks at
the runner mirror, so a committed-but-unpushed tip is reported as "no progress" and the task is
Failed rather than Blocked; the Codex brief never states the branch contract, the mirror path or the
claim line; and the temp runner's `work` volume is deleted at `retire-temp` (`c590-remote.sh`
`compose_temp down -v`), which is where two of the three lost tips lived.

This plan therefore does four things on the existing desktop-canonical model (CARD-0604 D-15,
CARD-0672 I-A), without blessing rebases or rewriting history:

1. Settlement inspects the task's runner mirror before any no-progress verdict and, when the mirror
   tip is a fast-forward of what origin holds, publishes it on the task's behalf (plain push of the
   task's own branch, never forced), then continues the ordinary sync at the published tip (D-2).
2. A rebased or reset mirror tip is a Blocked, retained, named state, never a Failed no-progress
   verdict; origin-side divergence keeps CARD-0753's explicit reviewed-source recovery and the note
   now prints that command (D-3).
3. Runner Worktree briefs carry a branch contract (mirror path, own branch, fast-forward-only,
   push after every slice, `ls-remote` confirmation, claim line), and the orchestrator rules say
   never to ask a runner task to rebase (D-4).
4. Mirror removal refuses to destroy unpublished commits unless forced (D-5).

Out of scope, stated so Code does not drift into them: the temp-runner retirement guard in the
deploy scripts (follow-up card F-A); Codex tool-call transcript ingestion on runners (F-B);
relaxing the "confirmation-only Code task has no progress" policy (kept, D-7); automatic adoption
of a force-pushed tip (rejected, D-3); any change to landing, Review, cards, or the Mutation lane.

## Ground truth

What the card asserts against what the code and the live records show. Code line numbers are at
`a8b4e9e5`.

| Card assumption | What the code / records show | Verdict |
|---|---|---|
| Multiple Code tasks reported a genuine pushed commit but settlement "could not attribute the push" | Settlement observes the exact ref `refs/heads/feat/card-task-<id>` on origin with `ls-remote --refs --exit-code` and pins the fetched object (`TaskProgressGit.ObserveExactRefAsync`, lines 59-146), then requires B to be an ancestor of S (`RemoteWorkspaceService.SyncOwnedCheckoutAsync`, lines 326-341). In all nine `runner_no_pushed_progress` rows since 2026-09-27 the stored evidence has `observedSha == baselineSha` (see the incident table). The three named "lost" SHAs `c71a1ef3…`, `904bd755…`, `a411d914…` answer `upload-pack: not our ref` from GitHub today: they never reached origin. | Attribution was correct; the pushes did not happen |
| CARD-0727 task `0627264d` hit `runner_sync_diverged` "on land" although the commit was pushed | It was the runner sync at settlement (14:23:23), not a land: `remoteSync.state=Refused reason=runner_sync_diverged observedSha=40feb0389…`. That commit is on origin (fetchable from this clone) and dated 13:57:51, a rebased tip force-pushed by the "retry, verify push" attempt after `9e6c8c3a` ("PC-62 fix + rebase") had left its rebased tip `c71a1ef3…` unpublished. The B-versus-S ancestry refusal is the deliberate guard CARD-0753 V-1 protects. | Confirmed as a guard refusal; its recovery lane already exists (below) |
| The same batch on CARD-0452 and CARD-0558 | `e7c20aaa` ("rebase + C448_C24 fix"): S == B == `60cb80df…`, report says "Rebased … onto origin/master … commit 904bd755…", SHA absent from GitHub; retry `47f087f7` force-pushed `3598291b…` and was refused `runner_sync_diverged`; `7c33fc2f` "land-owner recovery (real commit)" landed. `d92a0f97`: S == B == `eb3e4515…`, `a411d914…` absent; retry `f4ef4af4` Synchronized at `5600dcae…`; "Group A fixes + rebase" `058eda4e` ended `runner_sync_lease_busy`; recovery `02983cde` refused `runner_sync_diverged`; `da9b64d7` landed. CARD-0566: `71cc7048` "rebase onto master" refused `runner_sync_diverged`; `d6a88a73` (confirmation only, "confirmed the commit and build") failed `runner_no_pushed_progress` with S == B; `56217783` "(real commit)" landed. | Every incident chain begins with a rebase instruction or a no-commit confirmation task |
| Common trigger: server2 redeploy mid-session (CARD-0727 rolling upgrade) | Six of the nine no-progress rows ran on `server2`, three on `server2-temp`; settlements span 12:59-20:56 on 09-27 and 10:18-10:32 on 09-28; every session ended with a normal report (`reportEvidence=Marked`), none died. The upgrade matters afterwards: `retire-temp-runner` runs `compose_temp down -v` (`scripts/c590-remote.sh` line 1572), deleting the temp `work` volume that held the unpushed mirrors of `9e6c8c3a`, `d92a0f97` and `d6a88a73`. | Not the trigger; it is why the loss became permanent (D-5, F-A) |
| Common trigger: worktree/git contention from 5-7 concurrent Worker tasks | Only `058eda4e` carries a lease reason (`runner_sync_lease_busy`, Canceled at card close); lease waits are bounded, carried across sweeps and never a no-progress verdict (CARD-0657 R4, `RemoteWorkspaceService.WhileLeaseBusyAsync`). | Not the trigger |
| Common trigger: the dispatcher's own push-observation polling | There is no polling. One observation per settlement: `ls-remote` plus one pinned fetch under the repository lease, with at most three attempts only when the advertised tip changes during confirmation (`TaskProgressGit.cs` lines 85-146). | Premise wrong |
| Genuine loss needs "faster/more reliable push confirmation before a task can report done" | The server cannot stop a delegate from reporting; it can look at the mirror the task was launched in. The runner already runs git in that mirror and holds the push credential (`docker/session-runner-grok/gitconfig`, `ssh_config`); no phone-home operation exists to read or publish a mirror's branch (`PhoneHomeOperation` ends at `SetCapacity = 31`). `RemoteWorkspaceService.SyncOwnedCheckoutAsync` returns `NoPushedProgress` at line 271 (`Missing`) and line 380 (`S == B`) without consulting the runner. | Confirmed gap; closed by D-2 |
| False-positive blocks need the land-owner substitution "made official/automatic" | CARD-0753 shipped the explicit lane on 2026-09-26 (active since the restart at `1b5b282a`): `delegate.ps1 -Land <owner> -ExpectedSourceSha <S> -ReviewEvidenceId <id> -RecoverReviewedSource` (`docs/ops-http.md` line 122). It was live during all four `runner_sync_diverged` refusals on 09-27 and unused: the settlement warning (`AgentTaskReplyService.cs` lines 810-812) says only "repair it, then reply to this task", and `docs/orchestration-loop.md` has no runner-sync outcome guide. | Official lane exists; make it discoverable (D-3, D-6), prevent the cause (D-4) |
| CARD-0558's loss cost ~90 minutes of real work | `a411d914…` is absent from GitHub; its mirror `/work/worktrees/task-d92a0f97` was on the previous `server2-temp` container, whose volume was removed at retirement. The current temp container (this one) has only five mirrors, none from 09-27. | Unrecoverable now; D-5 and F-A prevent the next one |
| Not stated: every affected task is Codex | All nine no-progress rows and all four diverged rows are `agentKind=Codex` (gpt-6-sol) under the human pin that routed Code to Codex that day. A Codex session's ingested transcript on a runner holds only `UserPrompt`, `AssistantText` and `TurnEnd` rows (10-40 entries per 15-90 minute session, zero tool calls), so what `git push` printed cannot be recovered from Antiphon. | Forensic blind spot; follow-up F-B |
| Not stated: no report carried the claim line | `progressEvidence.claim` is null in every row although `ProgressClaimContract` is in every Code brief (`DelegationReportFormatter.cs` line 455). Delegates put the SHA in prose (`handoff:`), which the parser ignores by design (CARD-0499 V-27/R-9), so the reason reads `runner_no_pushed_progress` instead of `runner_reported_commit_not_pushed`. | D-2 does not depend on the claim; D-4 asks for it explicitly |
| Not stated: a failed task's mirror stays and a later task can push the old branch | `feat/card-task-c03af147` exists on origin today at `b7bd0b74…` with commits dated 10:23:47-10:32:15 on 09-28, after `c03af147` had already failed `runner_no_pushed_progress` at 10:18:04 and inside the session window of its redo `be4f9e5f` (10:19:17-10:32:50), which also failed with its own branch at B. The redo worked in the earlier mirror; its brief names only the desktop path `C:\Antiphon\worktrees\card-task-…` ("Assigned checkout", line 200), which does not exist on the runner. | Brief must name the mirror (D-4) |
| Not stated: mirror removal protects unpushed work | `RunnerWorkspaceService.RemoveAsync` (lines 227-258) refuses only a dirty tree; committed-but-unpushed commits are removed with `worktree remove --force`. The branch ref survives in the runner repository, the worktree does not. | Latent gap already noted by CARD-0812; closed by D-5 |
| Not stated: the wire is compatible either way | `PhoneHomeFraming.Json` ignores unknown properties; the server sends only operations the runner's `Capabilities()` advertises (`RunnerCapabilityFeatures`), as CARD-0812 D-6 did for `workspaceRepositoryV1`. | Same pattern here (D-2, D-5) |

### Incident table (Antiphon board, Code tasks with a runner reason, 2026-09-27 onward)

| Task | Card | Runner | Title | Settled | Reason | B | S observed | Report names | Now on GitHub |
|---|---|---|---|---|---|---|---|---|---|
| 9e6c8c3a | CARD-0727 | server2-temp | PC-62 fix + rebase | 09-27 13:48:59 Failed | runner_no_pushed_progress | cdf994be | cdf994be | c71a1ef3 (rebased tip) | not our ref |
| 0627264d | CARD-0727 | server2-temp | … (retry, verify push) | 09-27 14:23:23 Blocked, Canceled 15:12 | runner_sync_diverged | — | 40feb038 | 40feb038 | present (force-pushed) |
| e7c20aaa | CARD-0452 | server2 | rebase + C448_C24 fix | 09-27 15:42:09 Failed | runner_no_pushed_progress | 60cb80df | 60cb80df | 904bd755 (rebased tip) | not our ref |
| 47f087f7 | CARD-0452 | server2 | … (retry, verify push) | 09-27 16:14:27 Blocked, Canceled | runner_sync_diverged | — | 3598291b | 3598291b | force-pushed |
| d92a0f97 | CARD-0558 | server2-temp | shareable stage workflows | 09-27 15:54:51 Failed | runner_no_pushed_progress | eb3e4515 | eb3e4515 | a411d914 | not our ref |
| 058eda4e | CARD-0558 | server2 | Group A fixes + rebase | 09-27 19:20:50 Blocked, Canceled | runner_sync_lease_busy | — | — | — | — |
| 02983cde | CARD-0558 | server2 | land-owner recovery (rebase conflict) | 09-27 21:21:45 Blocked, Canceled | runner_sync_diverged | — | f529bc7d | — | — |
| 71cc7048 | CARD-0566 | server2-temp | rebase onto master | 09-27 13:24:43 Blocked, Canceled | runner_sync_diverged | — | e7f789b2 | — | — |
| d6a88a73 | CARD-0566 | server2-temp | land-owner recovery (confirm only) | 09-27 13:27:54 Failed | runner_no_pushed_progress | e7f789b2 | e7f789b2 | "confirmed the commit", no new commit | — |
| f15a5cfa / 7268d5be / 793dfb87 | CARD-0676 / 0643 / 0629 | server2 | verification-only rounds | 09-27 20:52-20:56 Failed | runner_no_pushed_progress | 39623574 | 39623574 | "No commit or push was made" | — |
| c03af147 | CARD-0649 | server2 | fix D1-D3,D5 | 09-28 10:18:04 Failed | runner_no_pushed_progress | 7fb7bc5c | 7fb7bc5c | — | branch at b7bd0b74, commits 10:23-10:32 |
| be4f9e5f | CARD-0649 | server2 | … (redo, verify push) | 09-28 10:32:50 Failed | runner_no_pushed_progress | 7fb7bc5c | 7fb7bc5c | "ls-remote matches local HEAD" | own branch absent |

Platform facts read on 2026-09-30: `GET /api/runner-defaults` has `globalRunnerId: server2`
(revision 2). `GET /api/session-runners` lists `desktop` (windows, 2 delegated-task slots),
`server2` (linux, capacity 10, `draining: true`, `acceptingNewWork: false`, no
`workspaceRepositoryV1`) and `server2-temp` (linux, capacity 10, accepting, advertises
`workspaceRepositoryV1`). A Code stage for this plan runs on the Linux lane (`server2-temp` today);
nothing here needs Windows. Checkpoints name the lane, not a host: omit `-Runner` and `-Platform`.

## Decisions

D-1 through D-7 are this plan's defaults. Each names what it rejects. None needs an operator
answer before Code starts; the caller may override any of them by editing this section.

### D-1: Classification is by the three commit identities, never by the report

A settlement outcome is classified from B (dispatch baseline), S (origin's exact tip of the
task's own branch) and, new in this plan, T (the runner mirror's branch tip):

- `T == S == B`: nothing was committed or the task was confirmation-only. Today's
  `runner_no_pushed_progress` verdict stands (D-7), now with the mirror's dirty flag in the note.
- `T` descends from `S` (`S == B` or `S` ahead of `B`): committed but unpublished work. Settlement
  publishes T (D-2) and continues at `S' = T`.
- `T` diverged from `S` or `B` (rebased/reset, never force-pushed): retained work on the runner.
  Blocked with `runner_mirror_diverged` (D-3), never Failed.
- `S` diverged from `B` (force-pushed): unchanged `runner_sync_diverged` refusal, note prints the
  CARD-0753 recovery command (D-3, D-6).

Rejected: parsing prose SHAs from the report (CARD-0499 V-27/R-9 forbid it, and a SHA in prose
proved nothing here); trusting the runner's answer without re-observing origin (the desktop
observation stays the only source of S).

### D-2: Settlement publishes the mirror's fast-forward tip before a no-progress verdict

**Wire.** `PhoneHomeOperation.WorkspacePublish = 32` in
`src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`, with:

```
PhoneHomeWorkspacePublishRequest(string Path, string Branch, string BaselineSha, string? RemoteSha, bool Publish)
PhoneHomeWorkspacePublishResponse(string? Tip, string Relation, bool? DescendsFromBaseline, bool Dirty, bool Pushed, string? Refusal)
```

`Relation` is the mirror tip against `RemoteSha ?? BaselineSha`: `equal`, `descends` (a plain push
is a fast-forward), `diverged`, `behind`, `unknown`. Feature flag
`RunnerCapabilityFeatures.WorkspacePublishV1 = "workspacePublishV1"`, added to
`PhoneHomeRuntimeAdapter.StaticFeatures` and pinned by `RunnerCapabilitiesTests`.

**Runner.** `RunnerWorkspaceService.PublishAsync` (new, `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`),
routed by `PhoneHomeCommandDispatcher` under `MutateAsync` like `WorkspaceMirror`: refuse a path
outside the worktree root or a `Branch` that is not `feat/card-task-<the mirror's 8 hex>`
(`UnsupportedTarget`, before any git process); refuse when `symbolic-ref HEAD` is not that branch
or a sequencer is active (`Refusal = "not_on_branch"` / `"sequencer_active"`); read `Tip`,
`Dirty` (`status --porcelain`), the ancestry answers with `merge-base --is-ancestor`; when
`Publish` and `Relation == descends`, run `git push origin refs/heads/<Branch>:refs/heads/<Branch>`
from the mirror (no `--force`, no `-u`, 60 s bounded like every runner git call). A rejected push
returns `Pushed=false, Refusal="push_rejected"` with the stderr tail passed through the CARD-0812
redaction helper (no URL, no credential). Nothing else is ever pushed: not master, not another
task's branch, not a tag.

**Server.** `PhoneHomeRunnerClient.PublishWorkspaceAsync`. `RemoteWorkspaceService` gets an
optional `IRunnerMirrorPublisher` seam (default implementation calls the client after
`ISessionRunnerDirectory.DescribeAsync` shows the feature; a runner without it returns null so
today's path runs unchanged). In `SyncOwnedCheckoutAsync`, after the exact-ref observation and
only when `reportedTips is null` (a correlated `done` or the unmarked-success fallback, never the
bind-refusal recovery) and the observation is `Missing` or `S == B`:

1. Ask the runner with `Publish = true`, `BaselineSha = B`, `RemoteSha = S` (null when missing).
2. `Pushed == true`: re-observe the exact ref; require the new S' to equal `Tip` (else
   `runner_sync_changed_during_validation`), then continue the existing path (lease, identity
   revalidation, pin, ancestry, fast-forward) with `s = S'`. The result carries
   `MirrorSha = T`, `MirrorPushed = true`.
3. `Relation == diverged` or `behind`: return `Refused` with the new reason
   `runner_mirror_diverged`, `MirrorSha = T`, desktop untouched.
4. `Relation == descends` but not pushed (rejected, timeout, transport error): return
   `Unavailable` with the new reason `runner_mirror_publish_failed`, `MirrorSha = T`.
5. `equal`, `unknown`, no mirror path, no feature, or a phone-home error: fall through to today's
   `NoPushedProgress`, with `MirrorSha`, `MirrorDirty` and the reason the runner could not be
   consulted (`runner_mirror_unavailable`) recorded in the evidence, never in the status.

`RemoteSettlementSyncResult` and `RemoteSyncEvidence` (`TaskProgressDtos.cs`) gain
`MirrorSha`, `MirrorRelation`, `MirrorDirty`, `MirrorPushed`. `Confirmed` semantics are unchanged.
The whole salvage runs inside the existing `SyncBudget` (120 s); the runner's push is bounded at
60 s, so the budget still holds one observation, one publish and one re-observation.

Why this shape: the branch is task-owned and its name is derived from the task id, the runner
already holds the credential and already runs git in that mirror on the task's behalf, and the
desktop still moves only through the leased fast-forward sync (CARD-0672 I-A is untouched). It
converts every "committed but not pushed" case, whatever its cause (a rejected push, a delegate
that forgot, a session killed between commit and push), into the ordinary Synchronized path.

Rejected: a pre-report hook that pushes on the delegate's behalf (nothing observes the delegate's
final message before it ends its turn); polling origin after the report (there is nothing to wait
for; the push never happened); force-publishing a diverged tip (D-3); a push from the desktop (the
desktop has no copy of the commits); making the runner fetch the desktop's baseline (B is already
in the mirror's history).

### D-3: A rebased tip is a retained Blocked state; origin-side divergence keeps the explicit recovery lane

`runner_mirror_diverged` is a `Refused` sync state, so `RemoteSyncBlockReason` blocks the task
(CARD-0657 D-5) and the session, worktree and mirror are kept. The Blocked note names the runner,
mirror path, T, B and the fast-forward-only rule, and gives the recovery: dispatch a fresh Code
task pinned to the same runner (`-Runner <id>`) from the wanted base (`-StartRef`), tell it to
cherry-pick `git log <merge-base>..<T>` from local branch `feat/card-task-<old>` in the shared
runner repository (`/work/repos/antiphon`, where the branch ref survives the worktree), and push.

`runner_sync_diverged` (S force-pushed) stays a refusal exactly as CARD-0753 V-1 pins it. Its note
and the `Warning` event now print the reviewed-source lane verbatim: `Review the pushed tip S
(Worktree Review with -StartRef <S>), then delegate.ps1 -Land <owner> -ExpectedSourceSha <S>
-ReviewEvidenceId <guid> -RecoverReviewedSource` (`docs/ops-http.md` line 122). That is the
"official" substitution the card asks for; it is explicit by CARD-0753's decision and stays so.

Rejected: automatic adoption of a force-pushed S when it "looks like a rebase of B" (silently
blesses history rewrites, the exact thing CARD-0753 refused, and the guard is what caught every
force-push here); flipping a diverged owner to Succeeded (status rewrite); deleting the mirror to
"clean up" a diverged tip.

### D-4: The runner brief states the branch contract; orchestrators never ask a runner task to rebase

`DelegationReportFormatter` adds, for every Worktree task with a `RunnerId` (the same
`RemoteWorkspaceService.IsEligible` shape), an ASCII block after the assignment lines:

```
--- runner branch contract ---
You work in the runner mirror <RemoteWorktreePath> on branch <WorktreeBranch>; the desktop
checkout <WorktreePath> is not reachable from here. Commit on this branch only and push after
every slice with `git push origin HEAD:refs/heads/<WorktreeBranch>`. The branch is fast-forward-only
from <WorktreeBaseSha>: never rebase, amend or reset a pushed commit, and never force-push; landing
rebases onto the target itself. If your brief asks you to rebase this branch, report blocked and
say so. Before your final message run `git ls-remote origin refs/heads/<WorktreeBranch>` and
include the line `[antiphon-progress:<task guid> commit=<that sha>]`. Work left unpushed is
invisible to the caller and is destroyed when the mirror is retired.
```

The "Assigned checkout" line for a runner task names the mirror path first and the desktop
worktree second. `docs/orchestration-loop.md` gets a "Runner sync outcomes" subsection under
section 5 (reason codes, what each means in B/S/T terms, the recovery for each) and one rule under
section 3: to bring a runner task's branch current, land it (the land rebases) or dispatch a fresh
task with `-StartRef`; a confirmation-only step is a Review, not a Code task.
`.claude/skills/antiphon-orchestrator/SKILL.md` carries the same rule in one paragraph.
`docs/session-runtime-invariants.md` records the invariant: a runner task branch is published only
as a fast-forward of its dispatch baseline; settlement may publish the mirror's fast-forward tip on
the task's behalf; a mirror holding unpublished commits is never removed without `Force`.

Rejected: a server-side hard refusal of rebase instructions in brief text (unenforceable and the
orchestrator's titles are not the brief); a global `push.default`/hook change on the runner image
(the failure is a rebase, not a push option).

### D-5: Mirror removal refuses unpublished commits unless forced

`PhoneHomeWorkspaceRemoveRequest(string Path, bool Force = false, string? PublishedSha = null)`.
When `PublishedSha` is given and `Force` is false, `RemoveAsync` reads the mirror branch tip T and
refuses with the new problem type `PhoneHomeProblemTypes.UnpublishedWork =
"phone_home_unpublished_work"` (409) unless `T == PublishedSha` or T is an ancestor of it. The
server records the refusal as residue exactly as it records any other removal failure
(`TaskWorktreeRetirementService.RemoveRemoteMirrorAsync`, `AgentTaskDispatcher.RebindDrainingAsync`),
naming T in the residue text. Callers pass the best published identity they hold: retirement passes
the evidence's `RemoteSync.ConfirmedSha` (else the task's `WorktreeBaseSha`); drain rebind passes
the dispatch SHA (the mirror was never used). A null `PublishedSha` keeps today's behaviour for
every existing test and any older server.

Rejected: a runner-side `ls-remote` at removal (network at retirement, and the server already knows
S); deleting the local branch at removal (it is the last copy).

### D-6: Reasons and notes name the mirror state and the exact recovery

New stable reasons in `RemoteSettlementSyncReasons`: `runner_mirror_diverged`,
`runner_mirror_publish_failed`, `runner_mirror_unavailable`. The no-progress failure sentence in
`AgentTaskReplyService.TryClassifyCompletedWithoutProgressAsync` replaces "(uncommitted or unpushed
runner work cannot be seen from here)" with the mirror facts when they exist ("the runner mirror
<path> on '<runner>' is at <T>, <relation>, dirty=<bool>"). `GET /api/agent-tasks/{id}` exposes the
new evidence fields through the existing `progressEvidence.remoteSync` object; `delegate.ps1` status
prints `mirror=<sha> <relation> pushed=<bool>` on the runner-sync line. No new settings.

### D-7: Kept as is

A Code task that only re-verifies its dispatch tip has no new attributed progress (CARD-0753
V-2). The three verification-only rounds and `d6a88a73` were correct verdicts; the orchestrator
guidance in D-4 sends that work to Review. The temp-runner retirement (`compose_temp down -v`) and
Codex tool-call ingestion are follow-up cards, not slices here.

## Slices

- **S1 — contract and runner.** `PhoneHomeContracts.cs` (operation 32, publish request/response,
  `PublishedSha` on remove, `UnpublishedWork` problem type); `SessionRunnerContracts.cs`
  (`WorkspacePublishV1`); `RunnerWorkspaceService.cs` (`PublishAsync`, remove guard, branch-name
  derivation from the mirror name, redacted push refusal); `PhoneHomeCommandDispatcher.cs`
  (route under `MutateAsync`, cwd admission before git); `PhoneHomeRuntimeAdapter.cs`
  (`StaticFeatures`). Tests in `tests/Antiphon.SessionRunner.Tests`:
  `RunnerWorkspaceServiceTests`, `PhoneHomeCommandDispatcherTests`, `RunnerCapabilitiesTests`.
- **S2 — settlement salvage and removal identity.** `PhoneHomeRunnerClient.cs`
  (`PublishWorkspaceAsync`); new `IRunnerMirrorPublisher` under `server/Application/Interfaces`
  with the phone-home implementation beside `RemoteWorkspaceService`; `RemoteWorkspaceService.cs`
  (salvage step, new reasons, `RemoveMirrorAsync(task, publishedSha)`); `RemoteSettlementSyncDtos.cs`
  and `TaskProgressDtos.cs` (mirror fields); `AgentTaskReplyService.cs` (notes, recovery text);
  `TaskWorktreeRetirementService.cs` and `AgentTaskDispatcher.RebindDrainingAsync` (pass
  `PublishedSha`). Tests: `RunnerSettlementSyncTests` (SyncWorld gains a `LocalMirrorPublisher`
  that runs the runner's exact git sequence in `SyncWorld.Runner` and a feature-aware directory
  double), `RunnerTaskSettlementTests`, `RemoteWorktreeMirrorTests`, `RemoteWorkspacePreparerTests`
  (unchanged behaviour), `PhoneHomeRollingRunnerTests`.
- **S3 — brief, script and docs.** `DelegationReportFormatter.cs` (branch contract block, mirror
  path in the assignment line); `scripts/delegate.ps1` (status line); `docs/orchestration-loop.md`,
  `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/agent-kinds.md` (Codex
  transcript note), `.claude/skills/antiphon-orchestrator/SKILL.md`. Tests:
  `DelegationReportFormatterTests` (in `DelegationUnitTests.cs`), new
  `RunnerBranchContractDocumentationTests` (pins the rule and the reason codes in the docs, the
  shape `RepairSourceDocumentationTests` uses).

Commit each slice with its tests; run the checkpoint tool once per committed slice group.

## Verification design

Write V-1 through V-8 red against the production line each guards; R-1 and R-2 are
existing-behaviour controls. Real Git for every ancestry, push and removal case (`SyncWorld`'s bare
origin, desktop worktree and runner clone; the runner tests' `Scratch` repositories), because a
mocked push cannot prove the fast-forward-only invariant. Process-spawning tests keep their
assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Every build and test run goes through the
build-slot gate; the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints -- run --plan
<this file>`) takes the slot itself. Review checks the report's `CHECKPOINT` lines against the
table below.

- **V-1** runner publish (`RunnerWorkspaceServiceTests`): a mirror with two unpushed commits on
  its branch is pushed to a bare origin with a plain refspec and the response reports `Tip`,
  `descends`, `Pushed=true`; the command list contains no `--force`; a rebased mirror (tip does not
  contain `BaselineSha`) is `diverged`, not pushed, origin unchanged; `equal` and `behind` are not
  pushed; origin advanced past `RemoteSha` between the request and the push gives
  `Pushed=false, Refusal=push_rejected` with no URL in the text; a path outside the worktree root,
  a branch that is not the mirror's own name, a detached HEAD and an active rebase are refused
  before any push; `Dirty` reflects `status --porcelain`.
- **V-2** runner remove guard (`RunnerWorkspaceServiceTests`): `PublishedSha = B` with a mirror tip
  ahead of B refuses `phone_home_unpublished_work` and leaves the directory; `PublishedSha == tip`
  and `PublishedSha` descending from the tip remove; `Force` removes regardless; a null
  `PublishedSha` keeps today's dirty-only rule (existing `Remove_refuses_dirty_tree` unchanged).
- **V-3** dispatcher and capability (`PhoneHomeCommandDispatcherTests`, `RunnerCapabilitiesTests`):
  `WorkspacePublish` is admitted only under the allowed cwd and is serialized with the other
  workspace mutations; an unknown-repository or out-of-root path never starts git;
  `StaticFeatures` contains `workspacePublishV1`.
- **V-4** settlement sync salvage (`RunnerSettlementSyncTests`, `SyncWorld` +
  `LocalMirrorPublisher`): runner clone commits without pushing and `SyncAsync` returns
  `Synchronized` at T with `MirrorPushed=true`, the desktop fast-forwarded to T, the command trace
  showing one publish then one exact-ref observation (no second push, no sibling fetch, no
  FETCH_HEAD); an absent branch on origin is created the same way; a rebased runner tip returns
  `Refused runner_mirror_diverged` with `MirrorSha` and B/S/L unchanged; a publish that is
  rejected because origin moved returns `Unavailable runner_mirror_publish_failed`; a directory
  without `workspacePublishV1` returns today's `NoPushedProgress` with `runner_mirror_unavailable`
  in the evidence; `reportedTips` (bind-refusal recovery) never calls the publisher; `T == B` with
  a dirty mirror returns `NoPushedProgress` with `MirrorDirty=true`; the whole salvage respects
  `SyncBudget` (controlled clock, a publisher that blocks until cancelled ends in
  `runner_sync_timeout`).
- **V-5** settlement end to end (`RunnerTaskSettlementTests`, `RunnerSettlementWorld`): a `done`
  report with unpushed runner commits settles `Succeeded`, evidence and the git summary name T, and
  the completion note carries no warning; the same report from a rebased mirror settles `Blocked`
  with the note naming the mirror path, T, B and the fresh-task recovery, mirror and session kept;
  a force-pushed S still settles `Blocked runner_sync_diverged` and the note contains the
  `-RecoverReviewedSource` command with S; the unmarked-success fallback gets the same salvage;
  a runner without the feature settles today's `Failed` whose sentence names
  `runner_mirror_unavailable`; existing `No_push_settles_with_specific_reason` keeps its verdict
  for `T == B`.
- **V-6** brief contract (`DelegationReportFormatterTests`): the runner branch contract block
  appears for a Worktree task with `RunnerId` and `RemoteWorktreePath`, names the mirror path,
  branch, base SHA and the task's claim line, and is ASCII; it is absent for desktop Worktree,
  Shared and ReadOnly tasks; the assignment line names the mirror before the desktop path.
- **V-7** removal identity (`RemoteWorktreeMirrorTests`, `PhoneHomeRollingRunnerTests`):
  retirement sends the confirmed S as `PublishedSha` and records residue naming T when the runner
  refuses `phone_home_unpublished_work`; drain rebind sends the dispatch SHA and still moves the
  task when the old mirror is refused.
- **V-8** documentation (`RunnerBranchContractDocumentationTests`): `docs/orchestration-loop.md`
  names the never-rebase rule and every reason code in D-6 with its recovery;
  `docs/session-runtime-invariants.md` carries the D-4 invariant; `docs/ops-http.md` documents
  `WorkspacePublish` and `PublishedSha`.
- **R-1** CARD-0657/CARD-0753 controls stay green: `Diverged_rewound_and_local_ahead_tips_refuse`,
  `Unpushed_claim_cannot_borrow_older_push`, the two RunnerCompletionProgress claim rules, and
  every existing `RunnerSettlementSyncTests`/`RunnerTaskSettlementTests` method.
- **R-2** compatibility: a directory whose descriptor lacks the feature never sends operation 32;
  a remove request without `PublishedSha` round-trips through `PhoneHomeFraming.Json` unchanged;
  `RemoteWorkspacePreparerTests` unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c779r/` | runner-workspace | `/*/Antiphon.SessionRunner.Tests/(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)\|(RunnerCapabilitiesTests*)/*` | V-1, V-2, V-3 | all three classes, 0 failed | 70 | 14 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c779/` | settlement-sync | `/*/Antiphon.Tests.Application/(RunnerSettlementSyncTests*)\|(RunnerCompletionProgressTests*)/*` | V-4, R-1 | both classes, 0 failed | 28 | 20 |
| CP-3 | S1-S3 | CP-2 | settlement-e2e | `/*/Antiphon.Tests.Application/(RunnerTaskSettlementTests*)\|(RemoteWorktreeMirrorTests*)\|(RemoteWorkspacePreparerTests*)/*` | V-5, V-7, R-1, R-2 | all three classes, 0 failed | 36 | 15 |
| CP-4 | S1-S3 | CP-2 | brief-drain-docs | `/*/Antiphon.Tests.Application/(DelegationReportFormatterTests*)\|(PhoneHomeRollingRunnerTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-6, V-7, V-8 | all three classes, 0 failed; the drain class's 4 platform-explicit methods may skip | 80 | 12 |

`Min` values are floors on executed TUnit results: CP-1 has 64 methods today (22 + 42) plus the
capability pin and at least 8 new methods; CP-2 has 24 (16 + 8) plus at least 6 new; CP-3 has 35
(15 + 8 + 12) plus at least 4 new; CP-4 has 69 formatter methods, 15 executable drain methods and
at least 3 documentation methods. A TestDesign pass may raise floors and add methods, never widen
a filter or drop a row. Off Windows the checkpoint tool adds `UseAppHost=false` itself. Ordinary
checkpoint floor: **61 minutes**. CP-2 carries the Antiphon.Tests build, so the checkpoint tool warns that three times its estimate exceeds the 45-minute row ceiling; the row still runs inside that ceiling. Authoring and red-first tests: **6-9 hours** (the runner
operation, its client, the seam and the real-Git cases are the bulk). One Review round: **1-2
hours**. Cost excludes post-land Mutation.

## Risks and rollout

- **A plain push from settlement is still a push.** It is restricted to the task's own branch
  (name derived from the task id and checked against the mirror name on the runner), never forced,
  and re-observed from the desktop before anything moves. A rejected push leaves the task Blocked
  with the tip named; it never retries in a loop.
- **The salvage lengthens a settlement** by one runner round trip and one push (tens of seconds).
  It only runs in the branch that today ends Failed or Blocked, so a task that pushed itself pays
  nothing.
- **Kept mirrors accumulate.** D-5 refuses to delete unpublished work, so a Failed/Blocked runner
  task's mirror stays until an operator forces removal or recovers it. Residue is recorded on the
  task as it is today; the follow-up F-A adds the retirement-time inventory. Do not add a
  "clean up diverged mirrors" sweep here.
- **Codex still cannot be audited.** Until F-B lands, a runner Codex transcript shows no git
  commands; the mirror facts in the evidence are the only forensic record. That is why the
  evidence carries T, the relation and the dirty flag.
- **Wire skew during the next rolling upgrade.** The server sends operation 32 and `PublishedSha`
  only to a runner advertising `workspacePublishV1`; an older runner keeps today's exact behaviour
  and the note says so (`runner_mirror_unavailable`). Deploy the runner image (both `server2` and
  `server2-temp` compose projects) before relying on the salvage; confirm with
  `GET /api/session-runners` features.
- **Activation.** After landing and the server restart, confirm `GET /api/version` is the landed
  SHA and that a runner-bound Code task's evidence shows `mirrorRelation` before trusting the new
  verdicts. A healthy server is not evidence that the salvage is active.

## Follow-up cards for the caller

- **F-A** Temp-runner retirement destroys unpushed mirrors: `deploy-server2.ps1 'retire-temp'` /
  `c590-remote.sh retire-temp-runner` must refuse `compose_temp down -v` while any mirror under
  `/work/worktrees` holds commits not on origin (expose an `unpublishedMirrors` count on the runner
  status, wait for zero or copy the branches out first). This is what made `9e6c8c3a`, `d92a0f97`
  and `d6a88a73` unrecoverable.
- **F-B** Codex transcript ingestion on runners records only assistant text: ingest Codex
  `function_call` / `function_call_output` rollout records as `ToolCall` / `ToolResult` so a
  settlement dispute can show what git printed.
- **F-C** (operator, optional) `e7c20aaa`'s `904bd755…` may still exist on `server2` as local
  branch `feat/card-task-e7c20aaa` in `/work/repos/antiphon`; CARD-0452 already landed through a
  fresh recovery task, so this is a confirmation of the mechanism, not a recovery.
