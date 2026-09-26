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

## Decisions

- **D-1 — Keep the card, narrow it: CARD-0688 fixed the residue, not the race.** The card's first
  expectation ("a refused land rolls back its local side effects … or records them so the next attempt
  resumes") is satisfied by schema 3 by construction (nothing local is mutated before publication) and is
  pinned here by an assertion inside the acceptance test rather than by new code. The card's acceptance
  ("a push racing the land … then retries the same request or a new request for the same reviewed SHA;
  the retry lands, with no manual reset") becomes V-1/V-2 (same request, one land) and V-3 (new request
  after the budget is spent). Rejected: closing the card onto CARD-0688 with only a regression test
  (the race still costs two or three round trips and, as 2026-09-26 showed, ends in a hand push).
- **D-2 — The retry lives in `AgentTaskLandService.RunLeasedAsync`, under the same lease, request and
  attempt.** After `_protocol.RunAsync` returns unpublished with a race refusal (D-3) and the budget is
  not spent, the service writes the retry evidence (D-5) and loops back to the resolver, which now
  replaces the refused operation (D-4); the protocol then runs the replacement from `Inspected` exactly
  as a fresh request would. Rejected: (a) re-queueing the request as a new attempt — it releases the
  lease, re-enters the CARD-0672 yield to dispatch waiters, consumes `LandMaxAttempts`, adds a sweep
  latency of `LandSweepSeconds`, and still needs the same two gates; (b) rewinding the operation's phase
  inside the protocol — `AgentTaskLandingState.Transition` is deliberately monotonic (`Verified →
  RebaseStarted` would need new arms, the `target-before`/`prepared` pins would have to be re-pinned,
  and the verification receipts cleared), and the first attempt's evidence would be overwritten; (c) a
  `--force-with-lease` push — CARD-0688 D-4 already rejected it, and the failure here is not the CAS but
  what happens after the CAS fails.
- **D-3 — A benign target race is exactly one refusal: schema 3, unpublished, `Refused`,
  `LastReason == "remote_changed_before_push"`.** `AgentTaskLandingState.IsTargetRaceRefusal(op)` names
  it (`SchemaVersion == 3 && Phase == Refused && Publication == Refused && !HasPublication &&
  LastReason == "remote_changed_before_push"`). Three protocol sites raise that reason and all three are
  already terminal for schema 3 (catch block, `AgentTaskLandingProtocol.cs:289–297`): the pre-push CAS
  (line 252), the already-present path whose remote no longer contains the source (line 174, a rewound
  origin — the retry then does what a fresh `-Land` would: rebase onto the rewound tip and push), and,
  new under D-6, a rejected push whose re-observation proves the tip moved. Every other refusal
  (`rebase_conflict`, `verification_failed`, `source_changed`, `source_remote_changed`,
  `reviewed_source_mismatch`, `resume_approval_changed`, lock codes, I/O) is untouched and terminal or
  resumable exactly as today. Rejected: keying the retry on `push_rejected` as well (a push that fails
  while the remote did not move is auth, network or a hook; re-rebasing cannot help, and today's
  resumable `PushStarted` op is the right shape for it).
- **D-4 — A race-refused operation may be replaced by its own request; the bound is the service's.**
  Two gates open, both only for `IsTargetRaceRefusal`: `AgentTaskLandingState.CanReplaceRefused(previous,
  explicitRequest, leaseHeld)` treats a race refusal as replaceable without `explicitRequest` (still
  requires the lease, still never a publication, still the schema-2/3 approval rule); and
  `AgentTaskLandSourceResolver.ResolveAsync`'s Resolved short-circuit (lines 86–93) returns the existing
  operation only when it is not a race refusal, otherwise it falls through to `CreateOperationAsync`,
  which already replaces a `Refused` predecessor through `LandOperationFactory.CreateAsync` (source
  re-read and compared to `LocalBeforeSha` and the reviewed SHA; target re-observed; `ApprovalLandRequestId`,
  `ReviewEvidenceId` and `ApprovalKind` inherited because `OriginalSourceSha` is unchanged). The
  protocol's own `Phase == Refused` arm (lines 94–103) needs no change: after the resolver has attached
  the replacement, `task.ActiveLandingId` names an `Inspected` operation. The crash-resume path (sweep
  re-run of a still-pending request whose last operation is a race refusal) therefore also replaces,
  which is correct for this refusal and is bounded by `LandMaxAttempts`. Rejected: a protocol parameter
  ("this run may replace") — the state policy is where replacement rules already live and are unit-tested
  (`AgentTaskLandingStateTests`), and a flag would let a caller replace any refusal.
- **D-5 — One `Warning` event per retry, progress restarted, stage outcomes recorded per run; no new
  event type.** In one task-locked transaction the service adds
  `Warning` "Land raced with a push to `<remote>:<destinationRef>` (retry n of N): the target moved from
  `<old8>` to `<new8>` after the candidate `<verified8>` was verified; rebasing `<reviewed8>` onto the new
  tip." with `LandRequestId` and `LandingOperationId` set, and resets `request.HighestProgress = -1`,
  `LastProgressAt = now`, `WarningAt = ErrorAt = null` (as admission does, `RunLeasedAsync` lines
  379–384) so `AgentTaskLandMonitorService` does not age a retry that is rebuilding. The raced run's
  `Rebase`/`Verify` stage outcomes are recorded as today's refused path records them (lines 480–486): they
  were real work and real cost. On exhaustion the request is refused as today with
  `remote_changed_before_push; after N automatic rebases (Delegation:LandTargetRaceRetries=N); run -Land
  again`. Rejected: a new `AgentTaskEventType.LandRetried` (client union, DTO, docs and formatter churn for
  one informational line; `Warning` is the established type for "did not finish (server restarted);
  re-running" and "yield budget exhausted") and a `LandAged`-style caller note per retry (the caller hears
  the terminal outcome, which now names the retries).
- **D-6 — A rejected push is a race only when the re-observation proves the tip moved.** Line 265
  becomes a three-way: `push_unconfirmed` (push succeeded, remote does not contain the candidate),
  `remote_changed_before_push` (push failed **and** `beforePush.Sha != (RemoteBeforeSha ??
  TargetBeforeSha)`), `push_rejected` (push failed, tip unchanged). The first two are terminal for
  schema 3 (existing catch arm); `push_rejected` keeps today's shape — the operation stays `PushStarted`
  and a later request resumes it and pushes again — which is the right shape for a transient network or
  auth failure, and is pinned by V-4. `LandFailureDiagnostic` needs no new template: the reason strings
  already exist. Rejected: making `push_rejected` terminal too (a resumable op after a transient failure
  is the cheaper retry: no rebase, no rebuild).
- **D-7 — Legacy schema-2 rows: `remote_changed_before_push` and `source_changed` at
  `LocalTargetAdvanced`/`PushStarted` become terminal; nothing is rolled back; `target_local_ahead` names
  the fix.** The catch block gains `op.SchemaVersion != 3 && reason is "remote_changed_before_push" or
  "source_changed" && op.Phase is LocalTargetAdvanced or PushStarted`. Neither state can ever publish
  (the candidate is based on a stale tip; the branch is no longer at the rebased tip), so leaving the op
  resumable only guarantees the same refusal forever (the 68a64314 shape in today's code). Once terminal,
  an explicit request with the same reviewed SHA replaces it: `IsLegacyDerivation` accepts a branch still
  at the old rebase's tip (`PreparationInputSha` = that tip, "from the recorded rebased tip"), and a
  branch reset to the reviewed SHA lands from there. The schema-2 local-`master` residue is **not**
  undone by the land: `LandOperationFactory` refuses `target_local_ahead` while local `master` is not an
  ancestor of the observed remote, and that refusal now carries a detail: "local `refs/heads/master`
  `<L>` is ahead of `origin` `<R>`; in the main checkout run `git fetch origin && git reset --hard
  origin/master` (after committing or stashing any operator work), then run `-Land` again; do not `git
  pull --rebase`, which carries the stray commit forward (CARD-0711 request 524311c6)". Rejected:
  automatic reset of local `master` (CARD-0688 D-4: unpushed commits on local `master` are the operator's,
  and a land must never move the canonical checkout backwards), and an automatic race retry for schema-2
  rows (the residue makes `target_local_ahead` the likely next refusal; the operator step comes first).
- **D-8 — Budget `Delegation:LandTargetRaceRetries`, default 2, clamp 0–5, 0 disables.** Two retries
  are three protocol runs per admission; each retry is one rebase plus one verification build (94–215 s
  on the desktop, CARD-0688 measurements) because a rebase onto a moved tip never qualifies for
  `base_unchanged`. A `master` that moves faster than three verifications in a row is a queue problem,
  not a race, and the terminal refusal says how many rebases were spent. `LandMaxAttempts` (3) keeps
  bounding crash re-runs independently: worst case 3 × 3 protocol runs for one request, all evidenced.
  Setting added to `DelegationSettings` beside `LandMaxAttempts`, to `server/appsettings.json` (line
  288 block) and to the settings line in [session-runtime-invariants.md](../../session-runtime-invariants.md)
  (CARD-0331 bullet).
- **D-9 — Safety properties are kept by construction, and each is pinned by a test.** Every retry is a
  fresh operation through the unchanged factory and protocol: source changed → `source_changed`
  (`LocalBeforeSha` mismatch) or `source_remote_changed` (`RecheckRemoteSourceAsync` at protocol entry)
  (V-6); candidate not the reviewed one → `reviewed_source_mismatch` at the factory and
  `RecheckApprovalAsync` at every boundary (`request.ExpectedSourceSha == op.OriginalSourceSha`,
  `ReviewEvidenceId`, filter) (V-1 asserts the inherited identity, V-7 the policy table); real conflict
  on the re-rebase → `rebase_conflict`, `NeedsResolution`, merge task, no push (V-5); verification of the
  re-rebased candidate is never skipped (V-1 asserts `Verifier.Calls`). The retry never touches the task
  worktree, the task branch or the canonical checkout before publication (V-1 asserts them after every
  refused attempt), so a retry that is itself refused leaves the same nothing behind.
- **D-10 — Docs say what a refusal leaves and what to do.** [orchestration-loop.md](../../orchestration-loop.md)
  §5: replace "`LandRefused` can follow local target advancement or an unconfirmed push" with the schema-3
  statement (a refusal leaves the task branch, the task worktree and the canonical checkout as they were;
  the automatic re-rebase and its `Warning` line; after a terminal `remote_changed_before_push` the answer
  is `-Land` again with the same SHA, never a hand push of `master`; `push_rejected` means the remote did
  not move and `-Land` again resumes the push); the "explicit retry of an eligible terminal `Refused`
  operation" paragraph gains the race exception. [ops-http.md](../../ops-http.md) land row: replace
  "`LandRefused` does not imply the local target stayed unchanged" with "schema 3 never moves the local
  target before publication; a schema-2 row (pre-2026-09-25) may have". `AGENTS.md` unchanged (its land
  lines already say `-Land` again and forbid the hand push).
- **D-11 — No migration, no DTO change.** The retry count is not a column: the durable evidence is the
  chain of `AgentTaskLandings` rows for the request (each raced one `Refused` with
  `remote_changed_before_push`, `Active = false`, and the last one the landed or refused operation), the
  `Warning` events, and the terminal line. Rejected: a `TargetRaceAttempt` column (a migration and its
  Designer for a number the chain already encodes) and a `landRequest.raceRetries` DTO field (the events
  are already on the task).
- **D-12 — Fixtures: the fake push rejects a non-fast-forward; the real-git helper pushes as a
  stranger.** `ControlledLandingGit`'s `push` arm refuses with exit 1 and `! [rejected] … (fetch first)`
  when the current `_remoteTarget` is not an ancestor of the pushed SHA (its `merge-base --is-ancestor`
  model already exists, line 577), leaving `_remoteTarget` unchanged; a new `AdvanceRemoteTarget()`
  helper mirrors `AdvanceRemoteSource()`. `LandingGitFixture` gains `PushIndependentAsync(string name)`
  (the probe's helper: commit in `Observer`, push `HEAD:refs/heads/master` to the bare remote, return the
  SHA), and `LandingProtocolHarness` gains the `LandSettings` property `LandingSafetyHarness` already has
  so the budget is configurable in both harnesses. Rejected: a second remote or a second canonical clone
  (the observer clone already is the stranger).
- **D-13 — One Code round; TestDesign owns the final test shapes.** The verification design below is
  the plan's input to TestDesign: it names the fixture, the red, and the assertion for each V, and the
  probe diff is the working seed for V-1/V-2 (rename the class to `AgentTaskLandTargetRaceTests`, move
  the helpers into S4, keep the `PROBE` evidence lines out). Code runs the closed checkpoint table once;
  no second round is planned.

## Design

### One request, end to end, when `master` moves

1. Admission (`RunLeasedAsync` lines 355–398) is unchanged: lease held, `request.Attempt` and
   `task.LandAttempt` incremented once.
2. Resolution (lines 422–449) is unchanged for the first run: the resolver observes the remote source,
   creates operation A through `LandOperationFactory.CreateAsync` (`TargetBeforeSha = RemoteBeforeSha` =
   the observed `origin/master`).
3. Protocol run 1: A goes `Inspected → RecoveryPinned → RebaseStarted → Prepared → Verified`. Either the
   pre-push CAS (line 252) or, under D-6, the post-push re-observation (line 265) finds the tip moved:
   `remote_changed_before_push`, A becomes `Refused`/`Publication = Refused` (catch block). The task
   branch, the task worktree and the canonical checkout are as they were.
4. Service (new, after line 450): `result.Published == false`, `Conflicts.Count == 0`,
   `_state.IsTargetRaceRefusal(A)`, `raceRetries (0) < LandTargetRaceRetries (2)` → record A's `Rebase`/`Verify`
   stage outcomes as the refused path does today, write the `Warning` "(retry 1 of 2)" and restart the
   request's progress clocks in one task-locked transaction (D-5), reload `task`/`request`, and loop to
   step 2 with `resumeExisting == false` (A is `Refused`).
5. Resolution, second time: the request is already `Resolved` for the same `ExpectedSourceSha`; the
   short-circuit (lines 86–93) sees that the existing operation is a race refusal and calls
   `CreateOperationAsync`, which reads the branch (`show-ref`), compares it to `request.LocalBeforeSha`
   and the reviewed SHA, observes the **new** `origin/master`, requires local `master` to be its ancestor
   (`target_local_ahead` otherwise), and attaches operation B (`AttachOperationAsync`: A `Active = false`,
   `task.ActiveLandingId = B`, `request.LandingOperationId = B`, `LastProgressAt` stamped). B inherits
   `ApprovalLandRequestId`, `ReviewEvidenceId` and `ApprovalKind` from A because `OriginalSourceSha` is
   unchanged (factory lines 79–82).
6. Protocol run 2: B is `Inspected`; entry rechecks (`RecheckApprovalAsync`, `RecheckRemoteSourceAsync`,
   `RecheckSourceAsync`) run as for any fresh operation; rebase onto the new tip in the land worktree;
   verification build (never `base_unchanged`: `RebasedSourceSha != OriginalSourceSha`); CAS; push;
   `PublicationConfirmed`; canonical fast-forward; cleanup. The outcome line is the ordinary `landed
   operation=B …`; the task's events read `LandRequested`, `Warning (retry 1 of 2)`, `Landed`.
7. If run 2 races again: step 4 with `retry 2 of 2`; a third race refuses:
   `LandRefused: remote_changed_before_push; after 2 automatic rebases (Delegation:LandTargetRaceRetries=2);
   run -Land again`. Three `Refused` rows, two `Warning`s. A new `-Land` for the same SHA replaces the last
   row through the existing explicit-retry path (V-3).

### What stays exactly as it is

The lease and its yield; the resolver's authoritative source observation and its one-round-trip
recheck; every `RecheckApprovalAsync`/`RecheckRemoteSourceAsync`/`RecheckSourceAsync` point; the recovery
pins; the land worktree reset; the verification build and its build slot; the plain push as the CAS; the
canonical advance and its residue reasons; the cleanup journal; `push_unconfirmed`; the conflict path;
`LandMaxAttempts` and the sweep; the notification, receipt and stage-outcome machinery; every DTO; the
schema.

### Slices

- **S1 — Race classification and replacement (D-3, D-4, D-6).**
  `server/Application/Services/AgentTaskLandingState.cs`: `IsTargetRaceRefusal(AgentTaskLanding)`;
  `CanReplaceRefused(previous, explicitRequest, leaseHeld)` accepts `!explicitRequest` when
  `IsTargetRaceRefusal(previous)`.
  `server/Application/Services/AgentTaskLandingProtocol.cs`: line 265 three-way (D-6).
  `server/Application/Services/AgentTaskLandSourceResolver.cs`: lines 86–93 fall through to
  `CreateOperationAsync` for a race refusal.
- **S2 — The bounded loop, the setting, the evidence (D-2, D-5, D-8, D-11).**
  `server/Application/Services/AgentTaskLandService.cs`: `RunLeasedAsync` loop around lines 422–489,
  `WriteRaceRetryAsync(task, request, op, retry, budget, ct)` (Warning event + progress restart, task
  lock), exhaustion detail on `RefuseAsync`; `server/Application/Settings/DelegationSettings.cs`:
  `LandTargetRaceRetries` (default 2); `server/appsettings.json`: the key beside `LandMaxAttempts`.
- **S3 — Legacy rows (D-7).** `AgentTaskLandingProtocol.cs` catch block: the schema ≠ 3 arm for
  `remote_changed_before_push`/`source_changed` at `LocalTargetAdvanced`/`PushStarted`;
  `server/Application/Services/LandOperationFactory.cs`: `Outcome.Detail` for `target_local_ahead`;
  `AgentTaskLandSourceResolver.CreateOperationAsync` passes `created.Detail` to `RefuseAsync`.
- **S4 — Fixtures (D-12).** `tests/Antiphon.Tests/TestHelpers/ControlledLandingGit.cs`: non-fast-forward
  push rejection, `AdvanceRemoteTarget()`; `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs`:
  `PushIndependentAsync(string name)`; `tests/Antiphon.Tests/TestHelpers/LandingProtocolHarness.cs`:
  `LandSettings` property used by `CreateLand`.
- **S5 — Docs (D-10).** `docs/orchestration-loop.md` §5, `docs/ops-http.md` land row,
  `docs/session-runtime-invariants.md` CARD-0331 bullet (the setting), and the probe diff's role noted in
  this plan only.

Order for Code: S4 → tests (red, CP-0) → S1 → S2 → S3 → S5 → CP-1..CP-4.

## Verification design

Vocabulary: V-n is a new red-first test; R-n is an existing class kept green. "Red" states what fails at
`fa86dc64`. Real-git classes carry `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`
and use `LandingSafetyHarness` (real canonical repo, bare remote, observer clone, isolated schema,
`ControlledVerifier`); fake-git classes use `LandingProtocolHarness`/`ControlledLandingGit`. Paths through
`Path.Combine`; nothing Linux-only. The probe diff beside this plan is the working seed for V-1/V-2
(TestDesign renames the class to `AgentTaskLandTargetRaceTests`, moves `PushIndependentCommitAsync` into
S4 and drops the `PROBE` console lines).

- **V-1** `AgentTaskLandTargetRaceTests.C711_PushWindowRaceLandsInOneRequest` (real git). The observer
  pushes an unrelated commit to `master` from `F.Git.BeforeCommand` immediately before the land's first
  `push`. One `RequestAsync(expectedSourceSha: reviewed)` + `RunQueuedAsync()`. Assert events, in order:
  `LandRequested`; `Warning` matching `raced with a push to origin:refs/heads/master (retry 1 of 2)` with
  `LandRequestId` = the request and `LandingOperationId` = A; `Landed`; no `LandRefused`. Operations by
  `CreatedAt`: A `SchemaVersion 3`, `Refused`, `Publication Refused`, `LastReason remote_changed_before_push`,
  `PushExitCode 1`, `Active false`; B `Landed`, `Complete`, `TargetBeforeSha` = the intruder,
  `VerifiedSourceSha == RebasedSourceSha`, `VerificationPassed`, `VerificationSkipReason null`,
  `ApprovalLandRequestId == A.ApprovalLandRequestId == request.Id`, `ReviewEvidenceId == A.ReviewEvidenceId`,
  `OriginalSourceSha == ReviewedSourceSha == reviewed`. Inside the hook, when B's `rebase` starts: source
  HEAD == reviewed, canonical `refs/heads/master` == seed, no `rebase`/`merge`/`push` command ran in the
  source worktree or the canonical checkout (trace). After: remote `master` == B.VerifiedSourceSha and
  contains the intruder (observer fetch + `merge-base --is-ancestor`), canonical `master` ==
  B.VerifiedSourceSha, outcome line contains `canonical=advanced`, `Verifier.Invocations` last ==
  `(LandWorktreePath, null)`. Red: `LandRefused push_rejected`, one operation at `PushStarted` (probe).
- **V-2** `…C711_PreObservationRaceLandsInOneRequest` (real git). The intruder pushes before the second
  target `ls-remote` (the pre-push CAS read). A: `Refused remote_changed_before_push`, `PushStartedAt null`,
  `PushExitCode null`; B as in V-1; same event shape. Red: `LandRefused remote_changed_before_push`
  (probe).
- **V-3** `…C711_BudgetSpentRefusesAndNewRequestLands` (real git). `H.LandSettings.LandTargetRaceRetries
  = 2`; the intruder pushes before every `push`. Assert `LandRefused` whose detail contains
  `remote_changed_before_push; after 2 automatic rebases`; three `Refused` operations, two `Warning`s
  (`retry 1 of 2`, `retry 2 of 2`); `task.LandAttempt == 1` (one admission); branch and canonical as they
  were. Then stop the intruder, `RequestAsync(expectedSourceSha: reviewed)` again, `RunQueuedAsync()`:
  `Landed`, a fourth operation, `LandAttempt == 1` for the new request. `[Arguments(0)]` companion: with
  the budget 0 the first race refuses with the plain reason and no `Warning` (today's behaviour is the
  opt-out). Red: one operation, no `Warning`, no "after N".
- **V-4** `…C711_PushRejectedWithoutMovementStaysResumable` (real git). `BeforeCommand` returns
  `LandingGitResult(1, "", "! [remote rejected] master -> master (pre-receive hook declined)")` for the
  first `push` and moves nothing. Assert `LandRefused push_rejected`, no `Warning`, one operation at
  `PushStarted`, `Active`; then a new request resumes the **same** operation id, pushes, `Landed`;
  operations count 1. Green before and after; it pins D-6's non-race arm (positive control: force
  `moved = true` at line 265 → a spurious retry, two operations, the test fails).
- **V-5** `…C711_ConflictOnRetryRefusesAsConflict` (real git). The intruder's commit rewrites
  `feature.txt` before the first `push`. Assert A race-refused; B `Refused rebase_conflict`; request
  `NeedsResolution`; a `Conflicted` event naming `feature.txt`; no second `push` in the trace; remote
  `master` == the intruder; source branch untouched. Red: `LandRefused push_rejected`, no conflict path.
- **V-6** `…C711_SourceMovedBeforeRetryRefuses` (real git, `[Arguments("remote")]`/`("local")`). In the
  same hook as the intruder's push, (remote) the observer pushes one more commit to the task branch's
  remote ref; (local) a commit is added in the source worktree without a push. Assert exactly two
  operations: A race-refused, B `Refused` with `source_remote_changed` (remote row, protocol entry) or
  `source_changed` (local row, factory `LocalBeforeSha` mismatch, reported through the resolver's refusal);
  one `Warning`; `LandRefused <reason>`; no push of B; the remote `master` still equals the intruder. Red:
  one operation, `push_rejected`.
- **V-7** `AgentTaskLandingStateTests.C711_TargetRaceRefusalPolicy` (unit, table). `IsTargetRaceRefusal`:
  the race row → true; schema 2 → false; `Publication Landed` (published shape) → false;
  `verification_failed` → false; `push_rejected` → false; `Phase PushStarted` → false.
  `CanReplaceRefused(previous, explicitRequest: false, leaseHeld: true)` → true only for the race row;
  `leaseHeld: false` → false for every row; `explicitRequest: true` rows unchanged from today's table
  (lines 276, 358). Red: member absent.
- **V-8** `ControlledLandingGitTests.C711_PushRejectsNonFastForward` (unit). Seed a descendant D of the
  seed; `AdvanceRemoteTarget()`; `PushOwnedAsync(D)` → exit 1, diagnostic contains `rejected`,
  `RemoteTarget` unchanged; a descendant of the new remote tip pushes with exit 0 and moves it. Red: exit 0
  and the tip moves.
- **V-9** `AgentTaskLandSourceFreshnessTests.C711_RaceRetriesInTheFakeHarness` (fake).
  `Fault.AfterAcknowledged(Verified)` → `RewriteRemoteAwayFromSource()` once (the
  `C488_TargetCheckpointStillGuarded` shape); default budget → `OwnedTrace` has two `rebase` entries and
  one `push`, the active operation is `Landed`, a `Warning` with `retry 1 of 2`; `[Arguments(0)]` →
  exactly today's `C488_TargetCheckpointStillGuarded` assertions. Red: one `rebase`, `Refused`.
- **V-10** `LandingProtocolGuardTests.C711_LegacyAdvancedRowsBecomeTerminal` (fake, schema-2 tuple from
  `C475LegacyLandTuples`, `[Arguments("remote")]`/`("branch")`). An unpublished schema-2 operation at
  `LocalTargetAdvanced` (`VerifiedSourceSha` = the old rebase's tip, local target at that tip): (remote)
  the remote tip moves → the resume refuses `remote_changed_before_push` and the operation is `Refused`;
  (branch) the branch is rewound to the reviewed SHA → `source_changed`, `Refused`. A new explicit
  request then refuses `target_local_ahead` with a detail containing `git reset --hard origin/master` and
  not `pull --rebase`; after the fixture's local target is reset to the observed remote, the same request
  re-run creates a schema-3 operation with `PreparationInputSha` = the old tip and
  `PreviousPreparationOperationId` = the legacy row (remote row) or `PreparationInputSha` = the reviewed
  SHA (branch row), and lands. Red: the operation stays `LocalTargetAdvanced` and the second request
  refuses the same reason.
- **V-11** `AgentTaskLandMonitoringTests.C711_RetryRestartsTheProgressClock` (fake, the class's offset
  clock). After the retry's `Warning`: `request.HighestProgress == -1`, `LastProgressAt` == the retry
  instant, `WarningAt`/`ErrorAt` null; a monitor tick at `LandWarningSeconds − 1` seconds after the retry
  writes no `LandAged`; a tick at `LandWarningSeconds + 1` with no further progress does. Red:
  `HighestProgress` stays at `PushStarted` and `LastProgressAt` is the first attempt's, so the first tick
  ages the request.
- **R-1** Existing classes in the checkpoint table stay green: `AgentTaskLandRefusedRetryTests` (the
  explicit-retry contract, including `RR_V2`'s "a non-race refusal is not replaced by a re-run"),
  `AgentTaskLandPublicationTests`, `AgentTaskLandRecoveryTests` (crash-resume), the fake-protocol classes,
  the monitoring/aging classes, `DelegateScriptLandStatusTests`.

Positive controls for Mutation (each named test must go red): line 265 `moved` forced true → V-4;
`IsTargetRaceRefusal` returning true for every `Refused` row → V-7 and `RR_V2`; budget comparison off by
one → V-3; resolver fall-through for every `Refused` row → V-7 (`CanReplaceRefused`) and
`AgentTaskLandRefusedRetryTests.RR_V2`; progress restart omitted → V-11; legacy arm omitted → V-10;
fake push rejection removed → V-8.

### Checkpoints

Test project `tests/Antiphon.Tests`; forward-slash isolated outputs; on server2 `run-checkpoint.ps1` adds
`UseAppHost=false` itself. `Min` is the `[Test]` count of the named classes at `fa86dc64` plus the new
methods (argument rows counted per argument): `AgentTaskLandRefusedRetryTests` 12,
`AgentTaskLandPublicationTests` 22, `AgentTaskLandRecoveryTests` 8, `AgentTaskLandSourceFreshnessTests` 46,
`LandingProtocolGuardTests` 17, `LandingProtocolHarnessTests` 9, `AgentTaskLandingStateTests` 13,
`ControlledLandingGitTests` 9, `AgentTaskLandMonitoringTests` 8, `AgentTaskLandQueueAgingTests` 7,
`AgentTaskLandStageOutcomeTests` 13, `DelegateScriptLandStatusTests` 3; new: V-1..V-6 = 8 rows, V-7 1,
V-8 1, V-9 2, V-10 2, V-11 1.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-0 | S4 + tests | `tests/Antiphon.Tests -> bin-c711r/` | race-red | `/*/*/(AgentTaskLandTargetRaceTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)/*` | V-1..V-8 red gate | all listed executed; V-1, V-2, V-3, V-5, V-6, V-7, V-8 **fail**; V-4 passes (exit 1 is the expected result of this row) | 30 | 8 |
| CP-1 | all | `tests/Antiphon.Tests -> bin-c711a/` | race-real-git | `/*/*/(AgentTaskLandTargetRaceTests*)\|(AgentTaskLandRefusedRetryTests*)\|(AgentTaskLandPublicationTests*)\|(AgentTaskLandRecoveryTests*)/*` | V-1..V-6, R-1 | all listed, 0 failed | 48 | 14 |
| CP-2 | all | CP-1 | race-fake-protocol | `/*/*/(AgentTaskLandSourceFreshnessTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)/*` | V-7, V-8, V-9, V-10, R-1 | all listed, 0 failed | 98 | 8 |
| CP-3 | all | CP-1 | land-service-aging | `/*/*/(AgentTaskLandMonitoringTests*)\|(AgentTaskLandQueueAgingTests*)\|(AgentTaskLandStageOutcomeTests*)\|(DelegateScriptLandStatusTests*)/*` | V-11, R-1 | all listed, 0 failed | 31 | 6 |
| CP-4 | S5 | n/a | docs-named | `git grep -n -e "LandTargetRaceRetries" -e "raced with a push" -e "never moves the local target" -- docs/orchestration-loop.md docs/ops-http.md docs/session-runtime-invariants.md server/appsettings.json` | S5 | ≥ 4 matching lines across the four files, exit 0 | n/a | 1 |

The pipe characters inside `Filter` are escaped for the table; the command line uses a plain `|`, quoted
as [docs/testing-and-build.md](../../testing-and-build.md#combined-class-filters-card-0403) shows. Run each
row with `scripts/run-checkpoint.ps1 -Name CP-n -Project tests/Antiphon.Tests -OutputPath bin-c711x/
-Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot .antiphon/c711-checkpoints`
(`-NoBuild` for CP-2/CP-3). CP-0 is the red-first gate and is run once, before S1–S3 exist; its
expected failures are listed in the report by name. The lane is Linux (server2) or Windows; nothing here
needs `-Platform Windows`. Delete `bin-c711r/`/`bin-c711a/` before finishing.

### Cost

Ordinary floor = 8 + 14 + 8 + 6 + 1 = **37 minutes** of checkpoint time; the isolated builds of
`tests/Antiphon.Tests` took ~90 s each on this runner during the probe. Per-retry production cost: one
rebase plus one verification build (94–215 s on the desktop) per race, at most two per request.

### Reproducing the probe

`git apply docs/superpowers/plans/2026-09-26-card-0711-land-refusal-race-probe.diff`, then
`pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-P1 -Project tests/Antiphon.Tests -OutputPath
bin-c711p/ -Filter '/*/*/AgentTaskLandTargetRaceProbeTests/*' -MinExecuted 3 -Expect
AgentTaskLandTargetRaceProbeTests -ResultsRoot .antiphon/c711-probe`. Expected at `fa86dc64`: 2 passed
(the two "today needs N requests" characterisations), 1 failed (`…OneRequestLands_RedToday`). After
S1/S2 the characterisations must fail (they assert today's chain) and the red one must pass; the diff is
not production coverage and is not to be committed to `tests/`.

## Follow-ups (not in this card)

- **Unpublished schema-2 rows still active.** Task `68a64314`'s operation `392e31f4` is `Active` at
  `LocalTargetAdvanced` although its card was landed by hand. S3 makes such rows replaceable, but nothing
  lists them. A one-off query (`AgentTaskLandings` where `SchemaVersion < 3`, `Active`, unpublished) and a
  decision per row (supersede or leave) belongs to an ops card.
- **Recovery refs of refused operations.** Each operation pins `refs/antiphon/land/<task>/<op>/{source,
  target-before, prepared, remote-observed/*}`; a race retry adds one more set per attempt and refused
  sets are not deleted anywhere this plan touched. Small, but worth a card once CARD-0692 settles cleanup.
- **CARD-0688 R2 (`LandNonBuildableGlobs`) is not landed** (`git grep` finds no reference at `fa86dc64`).
  Doc-only lands, which are the ones that raced this week, would retry at rebase cost only once it is.
- **Hand pushes to `master`.** Task `4c670eaa` is the third documented hand push after a refusal. The doc
  slice repeats the rule; if it recurs after this card, a server-side guard (refuse a task whose goal
  contains `push … origin/master`) is a separate decision.
