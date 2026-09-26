# CARD-0711: a land that races a push to master re-rebases itself; a refusal leaves nothing to undo

Plan date: 2026-09-26. Plan task: `fc89bde9` (Frontier, server2 Linux runner, worktree
`feat/card-task-fc89bde9`). Code inspected at `fa86dc64` (branch base `bafc3366` = `origin/master`);
the desktop serves `0e83d3f2` (`GET /api/version`, read 02:05Z), which contains the whole CARD-0688
schema-3 protocol (`7bb21046` … `8a79fbef`, landed 2026-09-25 02:07Z–14:22Z).
Card `c03b2cf8-dcd3-4bdb-aef5-98e5b91a59e6` (CARD-0711), board `8988ca03-7414-47ad-b0b6-51556c701703`.
Evidence: the source files named below; `GET /api/agent-tasks/68a64314` (the 2026-09-25 incident),
`GET /api/agent-tasks/22c54e3f` and `/4c670eaa` (the 2026-09-26 CARD-0735 refusals and the hand land);
and a Plan-stage probe run on this runner against the real-git land fixture
(`CHECKPOINT CP-P1 commit=f1a6361b … executed=3 passed=2 failed=1`, TRX under `.antiphon/c711-probe/`;
the probe source is [the diff beside this plan](2026-09-26-card-0711-land-refusal-race-probe.diff)).
No production code was changed. Next stage: TestDesign (this plan carries the verification design as
input for it, not in its place).

## Summary

The card describes two different things, and CARD-0688 already fixed one of them. The **residue** half
("the refused operation had already put its rebased candidate on the main checkout's local master, and
rebased the task's local branch") is the pre-CARD-0688 protocol: the 2026-09-25 incident's landing row
(`392e31f4`, task `68a64314`) is `schemaVersion: 2`, phase `LocalTargetAdvanced`, verified `482936bf`.
Schema 3, live since the 2026-09-25 evening restart, rebases in the detached land worktree, pushes
first, never moves the task branch (`ExpectedBranch(op) == SourceLocalSha`, `AgentTaskLandingProtocol.cs:516`)
and fast-forwards the canonical checkout only after `PublicationConfirmed` (`CleanupAsync`,
line 314). The probe confirms it on real git: after a refused push the task branch is at the reviewed SHA
and the canonical `master` is at the seed. So under today's protocol a refusal leaves **nothing to roll
back**, and a retry for the same reviewed SHA works: the probe's third request landed.

The **race** half is real and still costs two or three orchestrator round trips. The CARD-0735 plan
doc (task `22c54e3f`, 2026-09-26 00:39Z) ran on schema 3: request 1 was refused `push_rejected`
(the pre-push CAS matched, then another push won the race), request 2 was refused
`remote_changed_before_push` (it resumed the same `PushStarted` operation, re-observed, and found the
tip moved), and the orchestrator then landed by hand (task `4c670eaa` pushed `f2080559` straight to
`origin/master` from a worktree, the CARD-0358 anti-pattern) instead of running `-Land` a third time,
which would have succeeded (probe, attempt 3). Two defects make the chain long: (1) a rejected push
whose re-observation shows the remote moved is classified `push_rejected` and the operation is left
resumable at `PushStarted`, so the next request spends itself discovering the move; (2) nothing retries
a benign target race inside one request.

The fix is small and stays inside the service and the state policy: a schema-3 operation refused for
`remote_changed_before_push` (nothing published, source unchanged, only the target moved) may be replaced
by **its own request**, and `RunLeasedAsync` loops resolver → protocol up to
`Delegation:LandTargetRaceRetries` (default 2) times under the same lease and attempt, writing one
`Warning` event per retry and refusing with the same reason when the budget is spent. A rejected push
is reclassified as a race only when the re-observation proves the tip moved; `push_rejected` keeps
meaning "the remote did not move and the push still failed" and stays resumable. Every safety property
is kept because every retry is a **fresh operation** through the unchanged factory and protocol: the
source is re-read and must equal the request's `LocalBeforeSha` and the reviewed SHA, the approval
latch and request identity are rechecked at every boundary, the rebased candidate is verified again,
and a real conflict still refuses `rebase_conflict`. For the handful of legacy schema-2 rows the same
two refusals become terminal at `LocalTargetAdvanced`/`PushStarted`, so an explicit request can replace
them through the existing derivation rule; their local-`master` residue is the operator's to reset and
the `target_local_ahead` refusal now says so.

## Ground truth

| Card / brief assumption | What the code and evidence show | Consequence |
|---|---|---|
| "The refused operation had already put its rebased candidate on the main checkout's local master, and rebased the task's local branch." | True only for schema 2. `GET /api/agent-tasks/68a64314`: `landing.schemaVersion = 2`, `phase = LocalTargetAdvanced`, `verifiedSha = 482936bf`, `reason = source_changed`, requests 80b13d5a/524311c6/0a32b617/2105ea92 refused 13:54Z–13:57Z on 2026-09-25, before the CARD-0688 protocol landed (`8a79fbef`, 14:22Z). Schema 3: rebase and verify run in `op.LandWorktreePath` (`PrepareLandWorktreeAsync`, `AgentTaskLandingProtocol.cs:185–213`); the branch is never written (`RecheckSourceAsync` requires it to still equal `SourceLocalSha`, line 596); the canonical advance runs only from `PublicationConfirmed` (`CleanupAsync` → `AdvanceCanonicalAsync`, lines 312–318). Probe attempt 1: after `push_rejected` the source HEAD equals the reviewed SHA and `refs/heads/master` in the canonical checkout equals the seed. | No rollback machinery is needed. The plan proves the property with a test rather than adding code (V-1 asserts branch and canonical state after every refused attempt). |
| "Every retry then fails with `target_changed` or `source_changed`." | Schema 2 only. `target_changed` no longer exists in the protocol (only in `DelegationWorktreeService.cs:639/677` for the legacy merge path and in `GuardedWorktreeRemoval` for cleanup). The schema-3 chain, measured by the probe on real git: push-window race → `push_rejected` (op stays `PushStarted`, `PushExitCode = 1`) → same-SHA request 2 resumes, re-observes, refuses `remote_changed_before_push`, op `Refused` (terminal, catch block lines 289–297) → same-SHA request 3 creates a replacement (`AgentTaskLandSourceResolver.CreateOperationAsync` lines 186–191, `LandOperationFactory.CreateAsync`) with `TargetBeforeSha` = the new tip and lands. Race before the pre-push observation → `remote_changed_before_push` at request 1 (terminal) → request 2 lands. | Under schema 3 the retry already works; the plan makes it automatic and one request long, and removes the wasted `PushStarted` resume (D-3, D-6). |
| "Check whether CARD-0688 already fixes this; if it does, add a regression test and close." | Half. Fixed: residue and retry-ability. Not fixed: automatic re-rebase; the `push_rejected` misclassification; the docs still say "`LandRefused` can follow local target advancement or an unconfirmed push" ([orchestration-loop.md](../../orchestration-loop.md) §5) and "`LandRefused` does not imply the local target stayed unchanged" ([ops-http.md](../../ops-http.md) land row), which is why the orchestrator reached for a hand push on 2026-09-26. | Keep the card open with the narrowed scope: the acceptance test (V-1/V-2/V-3), the retry (S1/S2), the reclassification (S1), the docs (S5). |
| "A refusal caused by a push that raced with the land should retry automatically: rebase again onto the new tip, verify, and push." | There is no retry anywhere: `RunLeasedAsync` calls `_protocol.RunAsync` once (`AgentTaskLandService.cs:450`) and `RefuseAsync` on any unpublished result (line 487). The protocol refuses a same-request replacement of a `Refused` op (`explicitRequest` requires a different request id, lines 96–100; `AgentTaskLandingState.CanReplaceRefused`, lines 67–75), and the resolver's Resolved short-circuit returns the refused op as-is (`AgentTaskLandSourceResolver.cs:86–93`). | Three small gates open for exactly one case (schema 3, unpublished, `remote_changed_before_push`): D-4; the loop and its bound live in the service: D-2, D-8. |
| "Bounded number of times (2 or 3)." | Each retry is a rebase plus a verification build (Verify median 136 s on the desktop, CARD-0688 measurements; `base_unchanged` cannot apply after a rebase onto a moved tip). `LandMaxAttempts` (default 3) bounds crash re-runs of a request, not in-request retries. | `Delegation:LandTargetRaceRetries`, default 2 (three protocol runs per admission), 0 disables; clamp 0–5 (D-8). |
| "A retry after a refusal must work, with a fresh `ExpectedSourceSha` or from the recorded rebased tip." | Schema 3: the branch is never rewritten, so "the recorded rebased tip" does not arise; the same reviewed SHA retries (probe). Schema 2 residue: `LandOperationFactory.IsLegacyDerivation` (lines 98–100) accepts a branch left at the old rebase's tip and records `PreparationInputSha`/`PreviousPreparationOperationId` (CARD-0688 D-7), but only when the previous op is `Refused`; an unpublished schema-2 op at `LocalTargetAdvanced`/`PushStarted` is resumed instead (protocol lines 74–86), and when that resume refuses `remote_changed_before_push` (remote moved) or `source_changed` (branch reset by the operator) the catch block leaves it at its phase (line 292 requires `SchemaVersion == 3`), so every later request refuses the same way. That is the 68a64314 trap re-expressed in today's code. | D-7: those two refusals become terminal for schema ≠ 3 at those phases; the next explicit request replaces the op (derivation or reviewed SHA). Local `master` residue is not rolled back; `target_local_ahead` names the fix. |
| "When master moves between the land's rebase and its push (a benign race)" — one window. | Two windows and two reasons. (a) Between op creation (`ObserveAsync` at `LandOperationFactory.cs:45`, the rebase base) and the pre-push CAS read (`ObserveAsync` at protocol line 248): refused `remote_changed_before_push` at line 252, terminal. (b) Between the CAS read and the push: the plain `push <endpoint> <sha>:refs/heads/master` (`LandingGit.PushOwnedAsync`, line 666) is rejected non-fast-forward by the remote; the post-push re-observation (line 264) sees `!ContainsSource` and line 265 says `push_rejected` without looking at whether the tip moved. | D-6 turns (b) into `remote_changed_before_push` when the re-observed tip differs from `RemoteBeforeSha`; both windows then take the same retry path. |
| The fake-git harness can express the race. | `ControlledLandingGit` models `push` as unconditional success (`ExecuteAsync`, lines 664–673: it sets `_remoteTarget = sha`), so window (b) cannot be expressed there today; window (a) can (`C488_TargetCheckpointStillGuarded` uses `RewriteRemoteAwayFromSource` after `Verified`). The real-git fixture needs nothing: `LandingGitFixture.Observer` is an independent clone of the bare remote and can push to `master` (the probe's `PushIndependentCommitAsync`). | S4: the fake push rejects a non-fast-forward like git; V-8 pins it. The red tests live in the real-git `LandingSafetyHarness` (V-1..V-5) with fast fake-git companions (V-9). |
| Who pushes to `master` outside a land? | Lands are serialised per server by the repository lease, so the racing push is external: a delegate settling from server2 pushes only its task branch, but a hand push (task `4c670eaa`) or a land from another server or checkout goes straight to `origin/master`. AGENTS.md already forbids the hand push. | The race is rare but recurring (three refused lands in two days); a bounded retry covers it, and the doc slice repeats that the answer to a refusal is `-Land` again, never a hand push (S5). |
| Aging: a retry must not read as a stalled land. | `AgentTaskLandMonitorService` warns when `now - request.LastProgressAt >= LandWarningSeconds` (300 s); `TransitionAsync` raises `LastProgressAt` only when the new phase exceeds `request.HighestProgress` (protocol lines 676–683), so a replacement op that restarts at `Inspected` stamps no progress until it passes the previous high-water mark (`PushStarted`), i.e. after its whole rebuild. `AttachOperationAsync` stamps once at attachment. | D-5: the retry resets `HighestProgress = -1`, `LastProgressAt = now`, `WarningAt = ErrorAt = null` in the same transaction as its `Warning` event, exactly as admission does (`RunLeasedAsync` lines 379–384); V-11 pins it. |

### The two incidents, from the API

| When (UTC) | Task / request | Protocol | Refusal | Operation state left behind | What resolved it |
|---|---|---|---|---|---|
| 2026-09-25 13:54 | `68a64314` / `80b13d5a` (CARD-0706) | schema 2 (server at a pre-`7bb21046` build) | `remote_changed_before_push` | local `master` advanced to the candidate `482936bf`, task branch rebased to `482936bf`, op at `LocalTargetAdvanced` | — |
| 13:55, 13:56 | `524311c6`, `0a32b617` | schema 2 | `target_changed` (an operator `git pull --rebase` had carried the stray commit forward) | unchanged | — |
| 13:57 | `2105ea92` | schema 2 | `source_changed` (branch reset to the reviewed `34600eb8`; the op expected `482936bf`) | op still `LocalTargetAdvanced`, `mode=ResumePublication`, active today | hand land, task `4219092b` |
| 2026-09-26 00:40 | `22c54e3f` / `b20a866c` (CARD-0735 plan doc) | **schema 3** | `push_rejected` (`PushExitCode` per probe shape = 1) | op `PushStarted`, branch and canonical untouched | — |
| 00:46 | `9dbaa2d2` | schema 3 | `remote_changed_before_push` | op `Refused`, `publication=Refused`, `verifiedSha = 993a664c = sourceSha` (the first rebase was a no-op: the base was the source's parent) | hand land, task `4c670eaa` pushed `f2080559` to `origin/master` (`993a664c` is not in `origin/master`; its content is) |

### The probe (real git, this runner, commit `f1a6361b`)

`AgentTaskLandTargetRaceProbeTests` in `LandingSafetyHarness` (real canonical repo, bare remote, source
worktree, isolated Postgres schema, controlled verifier). `F.Git.BeforeCommand` lets the observer clone
commit and push to `origin/master` immediately before the land's first `push` (window b) or before the
second target `ls-remote` (window a). Output (`PROBE …` lines in the TRX):

| Probe | Request 1 | Request 2 | Request 3 | Result |
|---|---|---|---|---|
| `Probe_RaceAtPushWindow_TodayNeedsThreeRequests` | `LandRefused push_rejected`; op `c7f93667` `PushStarted`, `pushExit=1`, `verified=dd50f2d7` | `LandRefused remote_changed_before_push`; same op, now `Refused` | `Landed`; op `dfef1538`, `targetBefore` = the intruder's commit, `verified = rebased = 30fd7955`; remote `master` equals it | passed (today's chain) |
| `Probe_RaceBeforePrePushObservation_TodayNeedsTwoRequests` | `LandRefused remote_changed_before_push`; op `82a717c0` `Refused`, no push attempted | `Landed`; op `b12aeb98` | — | passed (today's chain) |
| `Probe_RaceAtPushWindow_OneRequestLands_RedToday` | `LandRefused push_rejected`; op `92ac4bd2` `PushStarted` | — | — | **failed** as expected: the red for S1/S2 |

Total 54 s for the three tests, 140 s slot time including the isolated build (`bin-c711p/`, since deleted).
