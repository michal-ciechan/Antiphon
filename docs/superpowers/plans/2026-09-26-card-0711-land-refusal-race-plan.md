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

## Verification design — TestDesign (task `6195a018`, 2026-09-26)

Appended by TestDesign; the fix design above is unchanged. This section supersedes the plan's
`### Verification design` / `### Checkpoints` / `### Cost` above as the Code stage's closed list where the two
differ (differences are named in "Plan corrections"). Every row names the test file and method, the decisive
assertion, the production line whose change turns it red (line numbers at `989abc9f`), and its checkpoint.

### Inspection

- Read: `AgentTaskLandingState.cs` (all), `AgentTaskLandingProtocol.cs` 60–110, 160–300, 619–624, 668–685,
  `AgentTaskLandSourceResolver.cs` 75–200, `LandOperationFactory.cs` (all), `AgentTaskLandService.cs` 340–500,
  `DelegationSettings.cs` 582–642, 1160–1196, `AgentTaskLandMonitorService.cs` 35–45; the probe diff (all).
- Fixtures read: `LandingGitFixture` (paths, `Observer` clone, `FixtureGit.BeforeCommand/AfterCommand/Trace/Commands`),
  `LandingSafetyHarness` (`LandSettings`, `Verifier.Calls/Invocations/Barrier`, `Fault.AfterAcknowledged/Phase/AfterCommit`,
  `RequestAsync`, `RunQueuedAsync`, `RestartServicesAsync`), `LandingProtocolHarness` (no `LandSettings`: `CreateLand`
  line 138 passes `new DelegationSettings()` — **missing setup, S4**; `Fault.TerminalCut/EventKind`, `Clock`),
  `ControlledLandingGit` (push arm 664–673 unconditional; `IsAncestor` 884; `RewriteRemoteAwayFromSource` 395;
  `AdvanceRemoteSource` 340 is the model for `AdvanceRemoteTarget`), `C475LegacyLandTuples` (a tuple roster, **not** a
  schema-2 row builder — the plan's V-10 fixture reference is wrong; the real builder is
  `AgentTaskLandRecoveryTests.SeedSchemaTwoAsync` 474–530).
- Test bodies read: `AgentTaskLandingStateTests` (`C448_V17…` 259–280, `RR_V7…` 282–362, `ValidV2` 194),
  `ControlledLandingGitTests` (all), `AgentTaskLandSourceFreshnessTests.C488_TargetCheckpointStillGuarded` 631–652,
  `AgentTaskLandPublicationTests.C448_V09_PushAndConfirmationPreserveCompetingRemoteState` 16–74,
  `…C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent` 113–133, `…C448_V09_PushExitCannotReplaceRemoteConfirmation`
  430–455, `…C448_V06…` 227–260 (`SourceRefusalReason == "target_local_ahead"`), `…C688_LocalMasterAheadOfOriginRefuses` 721–735,
  `AgentTaskLandPreparationIdentityTests.C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation` 74–150,
  `AgentTaskLandRecoveryTests.C688_SchemaTwoOperationsOnResume` 370–415, `LandingProtocolGuardTests.C475_PushExitDoesNotConfirmPublication`
  127–145, `AgentTaskLandApprovalRecoveryTests.C488_PublicationNeedsTargetContainment` 751–766 and the schema-2 derivation
  helpers 462–475, `AgentTaskLandRefusedRetryTests.RR_V2/RR_V3` 42–82, `AgentTaskLandMonitoringTests.C467_V15_ThresholdsUseMeaningfulProgress`
  152–193, `AgentTaskLandFailureDiagnosticTests.C498_TerminalTransactionFaultMatrix` 198–232,
  `AgentTaskServiceIntegrationTests.land_sweep_settings_reject_out_of_range_values` 161–173.
- Boundaries → rows: race window (a) pre-CAS → V-2; window (b) push → V-1; both windows × budget {0, default, spent} →
  V-1/V-2/V-3/V-9/R-4; push failure × tip {moved, unchanged} × push exit {0, 1} → V-1 (moved,1), V-4 (unchanged,1),
  R-5 (moved,0 = `push_unconfirmed`), `C448_V08` (contained,1 = AlreadyPresent, unchanged); retry × {conflict, local source
  moved, remote source moved, clean} → V-5, V-6 local, V-6 remote, V-1; entry {fresh request, crash-resume sweep} → V-1,
  V-13; refusal kind × same-request re-entry {race, non-race} → V-1, V-14; schema {2, 3} × {race reason, `source_changed`,
  `push_rejected`} at `LocalTargetAdvanced`/`PushStarted` → V-10 ×3, V-7 schema row; settings {-1, 0, 2, 5, 6} → V-12,
  V-3. Excluded combinations are in Out of scope.

### Plan corrections found while reading (Code must apply them; they are test-side, not fix-design changes)

1. **Three existing tests assert today's race refusal with the default budget and go red after S2.** Each is amended to
   pin the opt-out explicitly (`LandTargetRaceRetries = 0`), and the default-budget behaviour moves to a new V row;
   no assertion is loosened:
   `AgentTaskLandPublicationTests.C448_V09_PushAndConfirmationPreserveCompetingRemoteState` (row `non-ff-before-push`:
   with retries the land re-rebases onto `rival`, publishes, and cleanup removes the source — every assertion after
   `fired` flips) → R-2; `AgentTaskLandPreparationIdentityTests.C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation`
   (row `target`: `refused.Id.ShouldBe(previous.Id, "automatic recovery cannot replace changed preparation")` contradicts
   D-4's crash-resume sentence) → R-3, with V-13 as the default-budget companion;
   `AgentTaskLandSourceFreshnessTests.C488_TargetCheckpointStillGuarded` → R-4 (it is the plan's V-9 `[Arguments(0)]` row;
   V-9 keeps only the default-budget row so the two do not duplicate). The plan's table did not run
   `AgentTaskLandPreparationIdentityTests` at all, so R-3 would have gone red unseen.
2. **`RR_V2` does not pin "a non-race refusal is not replaced by a re-run"** (it is an explicit-request test:
   `land_worktree_foreign` refuses B again). The resolver's same-request fall-through guard gets its own row, V-14.
3. **V-10's fixture is `AgentTaskLandRecoveryTests.SeedSchemaTwoAsync`**, so V-10 lives in `AgentTaskLandRecoveryTests`,
   not `LandingProtocolGuardTests`.
4. **D-8 "clamp 0–5"** is implemented as a `DelegationSettingsValidator` failure, like `LandMaxAttempts` (lines 1192–1195),
   not a silent clamp; V-12 pins it.
5. **D-6 leaves `push_unconfirmed` exactly as today** (not terminal; "What stays exactly as it is" wins over D-6's "the
   first two are terminal"); R-5 pins it.
6. **`target_local_ahead` stays the bare `SourceRefusalReason` code**; S3's detail goes only into the terminal event
   detail (via `AppendDetail`). R-6 pins it.
7. **`LandOperationFactory.Outcome` has `Diagnostic`, not `Detail`**; S3 adds a string `Detail` (or reuses the
   diagnostic) — naming only, the assertion is on the `LandRefused` event detail.
8. **Min counts** in the plan's table were method counts; the table below uses TUnit executions (argument rows
   expanded), counted from source at `989abc9f`.

### Delivery inventory

No new or changed async delivery path. The only asynchronous paths the change touches are existing and unchanged:

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt | Durable identity |
|---|---|---|---|---|---|---|
| Land request queue | `AgentTaskLandService.RequestAsync` | `RunLeasedAsync` under the repository lease | `AgentTaskLandRequests` row (`IsPending`) | sweep re-run (`LandMaxAttempts`), V-13 | terminal `LandRequested → … → Landed/LandRefused` events | `LandRequestId` |
| Land outcome to caller | `RefuseAsync` / publication path `AddNotification(…Outcome)` | caller session via the land notification dispatcher | `AgentTaskLandNotifications` row | existing dispatcher retry | caller receipt (`ConfirmedAt`), transcript `UserPrompt` | notification id / `LandRequestId` |

The retry adds a `Warning` **event row only** (D-5: no notification, no DTO). The safety property is that a retry
never produces a caller message: V-1 asserts exactly one `LandNotificationKind.Outcome` row for the request and zero
notifications of any kind created before the terminal event (G-17 / PC-17). The outcome message's text changes
only on exhaustion (V-3). Recipient evidence (a caller transcript `UserPrompt`) is therefore not required by this
card; the existing path's recipient proof is `LandOutcomeDeliveryHarness` / `PostLandMutationDelivery*` (out of
scope, unchanged). Substitute declared: the harness `AgentTaskLandNotifications` row count proves "no extra message
was enqueued", and cannot prove how an enqueued message renders in a caller session.

### Proves it works now

Shared shapes. **Real git** = new file `tests/Antiphon.Tests/Application/AgentTaskLandTargetRaceTests.cs`,
`[Category("Integration")] [ParallelLimiter<ProcessSpawnLimit>] public sealed class AgentTaskLandTargetRaceTests`,
each test `await using var h = new LandingSafetyHarness(); await h.InitializeAsync(); var f = h.Fixture;
var reviewed = await h.AddSourceAsync();`. The intruder is `LandingGitFixture.PushIndependentAsync(string name)` (S4:
the probe's `PushIndependentCommitAsync` moved onto the fixture, returning the pushed SHA; no `PROBE` lines). Helpers in
the class: `Read(f, path, args)` (probe), `EventsAsync(h)` (task events by `At`), `OperationsAsync(h)` (task's
`AgentTaskLandings` by `CreatedAt`). A **race hook** is
`f.Git.BeforeCommand = async (dir, a) => { if (a[0] == "push" && pushes++ < N) intruder = await f.PushIndependentAsync($"intruder-{pushes}"); return null; }`.
"Red at `989abc9f`" is what CP-0 must show. The CP column names the row that runs each test after the fix.

- **V-1** Push-window race lands in one request | real git, service+protocol+resolver | `AgentTaskLandTargetRaceTests.C711_PushWindowRaceLandsInOneRequest`, CP-0 (red), CP-1 |
  Race hook with N = 1. `h.Verifier.Barrier` on call 2 records, **while B is verifying** (i.e. after B's rebase, before
  its push): `Read(f, f.Source, "rev-parse", "HEAD") == reviewed`, `Read(f, f.Repository, "rev-parse", f.TargetRef) == f.SeedSha`,
  and `f.Git.Commands` has no entry whose `Directory` path-equals `f.Source` or `f.Repository` with `Arguments[0]` in
  {`rebase`, `merge`, `push`, `reset`} and no `update-ref … refs/heads/master`; sets `barrierHit = true` (asserted after the run). One `RequestAsync(expectedSourceSha: reviewed)`
  + `RunQueuedAsync()`. Decisive assertions: events filtered to the request are exactly `[LandRequested, Warning, Landed]`
  in order; the `Warning` detail contains `raced with a push to origin:refs/heads/master (retry 1 of 2)`, its
  `LandRequestId == request.Id`, `LandingOperationId == A.Id`; no `LandRefused`. Operations `[A, B]`: A
  `SchemaVersion 3, Phase Refused, Publication Refused, LastReason "remote_changed_before_push", PushExitCode 1, Active false`;
  B `Publication Landed, Phase Complete, Active true, TargetBeforeSha == intruder, VerifiedSourceSha == RebasedSourceSha,
  VerificationPassed, VerificationSkipReason null, OriginalSourceSha == ReviewedSourceSha == reviewed,
  ApprovalLandRequestId == A.ApprovalLandRequestId == request.Id, ReviewEvidenceId == A.ReviewEvidenceId`.
  `h.Verifier.Calls == 2`, `Invocations[^1] == (B.LandWorktreePath, null)`. After: remote `master == B.VerifiedSourceSha`,
  `merge-base --is-ancestor intruder B.VerifiedSourceSha` exit 0 (in `f.Remote`), canonical `master == B.VerifiedSourceSha`,
  terminal detail contains `canonical=advanced`; `AgentTaskLandNotifications` for the request: exactly one, `Kind Outcome`.
  Red at `989abc9f`: one operation, `LastReason push_rejected`, `Phase PushStarted`, `LandRefused` (probe).
  Turned red by: `AgentTaskLandingProtocol.cs:265` (D-6) and the S2 loop after `AgentTaskLandService.cs:450`.
- **V-2** Pre-CAS race lands in one request | real git | `…C711_PreObservationRaceLandsInOneRequest`, CP-0, CP-1 |
  Hook: on the second target `ls-remote` (`a[0] == "ls-remote" && a[^1] == f.TargetRef && ++reads == 2`) push the
  intruder (probe shape). Assert A `Refused remote_changed_before_push, PushStartedAt null, PushExitCode null`; no `push`
  in `f.Git.Trace` before A's refusal (index of first `push` > index of A's refusal is not observable, so assert
  `f.Git.Trace.Count(a => a[0] == "push") == 1`); B as in V-1 (Landed, `TargetBeforeSha == intruder`, verified,
  inherited identity); events `[LandRequested, Warning(retry 1 of 2), Landed]`. Red: `LandRefused remote_changed_before_push`,
  one operation. Turned red by: the S2 loop (`AgentTaskLandService.cs:450–489`) + the resolver fall-through
  (`AgentTaskLandSourceResolver.cs:86–93`).
- **V-3** Budget spent refuses; a new request lands; budget 0 is the opt-out | real git | `…C711_BudgetSpentRefusesAndNewRequestLands(int budget)`
  `[Arguments(2)] [Arguments(0)]`, CP-0, CP-1 | `h.LandSettings.LandTargetRaceRetries = budget`; race hook with N = budget + 1.
  Assert terminal `LandRefused` detail: budget 2 → contains `remote_changed_before_push; after 2 automatic rebases
  (Delegation:LandTargetRaceRetries=2)`; budget 0 → contains `remote_changed_before_push` and **not** `automatic rebases`.
  Operations: `budget + 1` rows, all `Refused remote_changed_before_push`, exactly one `Active` (the last). `Warning`
  events: `budget` of them, details `retry 1 of 2`, `retry 2 of 2` in order (none for 0). `task.LandAttempt == 1`,
  `request.Attempt == 1`. Source HEAD `== reviewed`, canonical `master == f.SeedSha`. Then `f.Git.BeforeCommand = null`,
  second `RequestAsync(expectedSourceSha: reviewed)` + `RunQueuedAsync()`: `Landed`, operations `budget + 2`, the new one's
  `ApprovalLandRequestId == first request id` (inherited, factory line 79–80), `task.LandAttempt == 2`, second request
  `Attempt == 1`. Red (budget 2): one operation, `push_rejected`, no `Warning`, no "after 2". Budget 0 is red only on the
  reason (`push_rejected` today) — D-6. Turned red by: S2's budget comparison and exhaustion detail.
- **V-4** A rejected push with an unchanged tip stays resumable | real git | `…C711_PushRejectedWithoutMovementStaysResumable`,
  CP-0 (passes; not a red row), CP-1 | Hook returns `new LandingGitResult(1, "", "! [remote rejected] master -> master (pre-receive hook declined)")`
  for the **first** `push` only and moves nothing. Assert `LandRefused` detail contains `push_rejected`; no `Warning`;
  operations: one, `Phase PushStarted, Active true, PushExitCode 1, Publication Unconfirmed`; `h.Verifier.Calls == 1`.
  Second request (`expectedSourceSha: reviewed`): `Landed`, the **same** operation id, operations count 1,
  `h.Verifier.Calls == 1` (no re-verification), remote `master == op.VerifiedSourceSha`. Green before and after — a
  guard, flagged: its value is PC-7. Guards: D-6's unchanged-tip arm.
- **V-5** A conflicting intruder on the retry refuses as a conflict | real git | `…C711_ConflictOnRetryRefusesAsConflict`, CP-0, CP-1 |
  The intruder commit (written by S4's helper with a `(name, path, content)` overload) rewrites the file
  `AddSourceAsync` changed, with different content. Assert A race-refused; B `Refused, LastReason rebase_conflict`;
  request `State NeedsResolution`; a `Conflicted` event whose detail contains that file name; `f.Git.Trace.Count(push) == 1`
  (A's only); remote `master == intruder`; source HEAD `== reviewed`; one `Warning`. Red: `push_rejected`, no `Conflicted`.
  Turned red by: the S2 loop (the conflict path itself is unchanged).
- **V-6** Source moved before the retry refuses without a push | real git | `…C711_SourceMovedBeforeRetryRefuses(string where)`
  `[Arguments("remote")] [Arguments("local")]`, CP-0, CP-1 | In the same hook call as the intruder: `remote` — the
  observer pushes a new commit to `f.SourceRef` (`checkout -B` from the fetched source, commit, `push origin HEAD:<SourceRef>`);
  `local` — `commit --allow-empty` in `f.Source` (no push). `remote`: operations exactly two, B `Refused` with
  `LastReason source_remote_changed` (protocol entry recheck). `local`: operations exactly one — the factory refuses
  `source_changed` at `LandOperationFactory.cs:25` before creating B, so the refusal is the resolver's — and the
  request's `SourceRefusalReason == "source_changed"`, `LandRefused` detail contains `source_changed`. Both: one `Warning`, `f.Git.Trace.Count(push) == 1`, remote `master == intruder`,
  `h.Verifier.Calls == 1`. Red: `push_rejected`, no `Warning`. Turned red by: S2 loop; guarded by factory line 25
  (local) and `RecheckRemoteSourceAsync` (remote).
- **V-12** The budget is validated 0..5 | unit (in the race class, no harness) | `…C711_RaceRetryBudgetIsValidated`, CP-0, CP-1 |
  `new DelegationSettings().LandTargetRaceRetries == 2`; `DelegationSettingsValidator.Validate` fails with a message
  containing `LandTargetRaceRetries` for -1 and 6, and succeeds for 0 and 5. Red: compile error (member absent) — CP-0
  records it as the build failure of that row; see CP-0 note. Turned red by: S2 `DelegationSettings`.
- **V-13** A crash-resumed request whose target raced retries with fresh verification | real git |
  `…C711_CrashResumeAfterRaceRetries`, CP-0, CP-1 | `h.Fault.Phase = LandPhase.Verified; h.Fault.AfterCommit = true;`
  `await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync())` (the `C448_V15` shape); then
  `f.PushIndependentAsync("intruder-crash")`, `h.Fault.Phase = null`, `RestartServicesAsync()`, `RunAsync()`. Assert
  A `Refused remote_changed_before_push`, B `Landed`, `TargetBeforeSha == intruder`, `Verifier.Calls == 2`, one `Warning`
  (retry 1 of 2), `request.Attempt == 2` (crash re-run is a second admission), source HEAD `== reviewed`. Red: A
  `Refused`, no B. Turned red by: S1 resolver fall-through + S2 loop (D-4 crash-resume sentence).
- **V-7** Race-refusal policy table | unit, state policy | `AgentTaskLandingStateTests.C711_TargetRaceRefusalPolicy(string row, bool isRace, bool automatic, bool explicitReplace)`,
  CP-0, CP-3 | Base op: `SchemaVersion 3, Phase Refused, Publication Refused, LastReason "remote_changed_before_push",
  PushExitCode 1, ApprovalLandRequestId = Guid.NewGuid(), OriginalSourceSha = ReviewedSourceSha = 'a'×40`, identity fields
  as `RR_V7`'s base. Rows `[Arguments(row, isRace, automatic, explicitReplace)]`: `race` (T,T,T); `schema2` (F,F,T);
  `published` (Phase Refused + the `RR_V7` `receipt:Landed` publication fields, `HasPublication` asserted true first)
  (F,F,F); `reason:verification_failed` (F,F,T); `reason:push_rejected` (F,F,T); `reason:null` (F,F,T);
  `phase:PushStarted` with `Publication Refused` kept, so only the phase conjunct differs (F,F,F); `publication:Unconfirmed` (Phase Refused) (F,F,T);
  `no-lease` (T,F,F — both calls with `leaseHeld: false`); `no-approval` (`ApprovalLandRequestId = null`) (T,F,F).
  Asserts: `IsTargetRaceRefusal(op) == isRace`; `CanReplaceRefused(op, explicitRequest: false, leaseHeld) == automatic`;
  `CanReplaceRefused(op, explicitRequest: true, leaseHeld) == explicitReplace` (today's rule, unchanged); the op's JSON
  is byte-identical before and after (the `RR_V7` no-mutation check). 10 executions. Red at CP-0: `race`, `no-lease`,
  `no-approval` fail on `isRace` (S0 stub returns false) and `race` on `automatic`. Turned red by:
  `AgentTaskLandingState.cs:67–75` (S1).
- **V-8** Fake push rejects a non-fast-forward | unit, fixture | `ControlledLandingGitTests.C711_PushRejectsNonFastForward`, CP-0, CP-3 |
  `using var git = new ControlledLandingGit(); var dest = await git.DestinationAsync(git.Repository, git.TargetRef, ct);`
  `await git.RequiredAsync(git.Source, "commit", "--allow-empty", "-m", "d"); var d = git.SourceHead;` (1) `PushOwnedAsync(…, d, …)`
  → exit 0, `RemoteTarget == d`. (2) `var moved = git.AdvanceRemoteTarget();` (child of `d`). (3) push `d` again → exit 1,
  `Diagnostic` contains `rejected`, `RemoteTarget == moved`. (4) push `git.SeedSha` → exit 1, `RemoteTarget == moved`.
  (5) `PushAsync` of `git.SeedSha` to `SourceRef` destination → exit 0 (the source ref keeps today's unconditional model).
  Red at CP-0 (S4b not yet applied): step (3) exits 0 and moves the tip. Turned red by: `ControlledLandingGit.cs:664–673` (S4b).
- **V-9** The retry in the fake harness | fake protocol | `AgentTaskLandTargetRaceFakeTests.C711_RaceRetriesInTheFakeHarness`, CP-0, CP-3 | The `C488_TargetCheckpointStillGuarded` shape with a one-shot guard
  (`if (phase == LandPhase.Verified && !raced) { raced = true; h.Git.RewriteRemoteAwayFromSource(); }`), default budget.
  Assert `h.Git.OwnedTrace.Count(a => a.Contains("rebase") && !a.Contains("--abort")) == 2`,
  `h.Git.Trace.Count(a => a[0] == "push") == 1`, `h.Verifier.Calls == 2`, operations 2 (A `Refused remote_changed_before_push`,
  B `Landed`), `h.Git.RemoteTarget == B.VerifiedSourceSha`, one `Warning` containing `retry 1 of 2`. Red at CP-0: one
  rebase, A `Refused`, no B. Turned red by: S1 + S2 (and S4b makes the retry's push a checked fast-forward).
- **V-10** Legacy schema-2 rows become terminal and the operator step is named | fake protocol |
  `AgentTaskLandTargetRaceFakeTests.C711_LegacyAdvancedRowsBecomeTerminal(string change)` `[Arguments("remote")] [Arguments("branch")]
  [Arguments("push-rejected")]`, CP-0, CP-3 | The `C688_SchemaTwoOperationsOnResume` preamble (`original = AddSourceAsync()`,
  empty commit, `rebased = h.Git.SourceHead`, `AgentTaskLandRecoveryTests.SeedSchemaTwoAsync(h, "local-target-advanced", original, rebased)` (S4a: `private` → `internal`) — the
  seed advances local `master` to `rebased`: the residue). `remote`: `h.Git.RewriteRemoteAwayFromSource()`; `RunQueuedAsync()`
  → the seeded op `Phase Refused, Publication Refused, LastReason remote_changed_before_push`, no `push` in `h.Git.Commands`.
  `branch`: `h.Git.RewindSource(original)`; `RunQueuedAsync()` → `Refused`, `LastReason source_changed`, no `push`. Then (both):
  `RequestAsync(expectedSourceSha: original)` + `RunQueuedAsync()` → terminal `LandRefused` detail contains `target_local_ahead`
  and `git reset --hard origin/master` and does **not** contain `pull --rebase`; request `SourceRefusalReason == "target_local_ahead"`;
  operations count 1. Then `update-ref <TargetRef> <remote tip> <rebased>` in `h.Git.Repository` (the operator's reset),
  `RequestAsync` + `RunQueuedAsync()` → a second op, `SchemaVersion 3`, `Landed`; `remote`: `PreparationInputSha == rebased`,
  `PreviousPreparationOperationId == seeded.Id`; `branch`: `PreparationInputSha == original`, `PreviousPreparationOperationId null`.
  `push-rejected`: `BeforeCommand` returns `(1, "", "! [remote rejected] master -> master (pre-receive hook declined)")` for the
  first `push`; `RunQueuedAsync()` → the seeded op stays `Phase PushStarted`, `Active`, `LastReason push_rejected` (the legacy
  arm is limited to its two reasons) — a guard row, green before and after. Red at `989abc9f` (`remote`, `branch`): the op
  stays `LocalTargetAdvanced` (catch arm `AgentTaskLandingProtocol.cs:292` is schema-3 only), and the detail lacks the reset
  text. Turned red by: S3 (protocol catch arm, factory detail, resolver pass-through).
- **V-11** A retry restarts the progress clock | fake protocol + monitor | `AgentTaskLandTargetRaceFakeTests.C711_RetryRestartsTheProgressClock`, CP-0, CP-3 |
  `LandingProtocolHarness` with `var clock = new FakeTimeProvider(start); h.Clock = clock;`. One-shot at
  `Fault.AfterAcknowledged(Verified)`: `clock.Advance(LandWarningSeconds + 5 s)`; run one
  `AgentTaskLandMonitorService(h.CreateContext(), clock, Options.Create(h.LandSettings), new MockEventBus()).SweepAsync`
  and assert the request's `WarningAt != null` (precondition: the first run aged, so the reset is observable); then
  `RewriteRemoteAwayFromSource()`; `raceAt = clock.GetUtcNow()`. `h.Verifier.Barrier` on call 2 (B verifying): request
  `HighestProgress == (int)LandPhase.Prepared`, `LastProgressAt == raceAt`, `WarningAt == null`, `ErrorAt == null`; Aged
  notifications for the request == 1; `clock.Advance(LandWarningSeconds − 1 s)`, sweep → still 1; `clock.Advance(2 s)`,
  sweep → 2; `barrierHit = true`. After: `barrierHit.ShouldBeTrue()`, B `Landed`. Red at `989abc9f`: no retry, barrier call 2
  never happens (`barrierHit` false). Turned red by: S2 `WriteRaceRetryAsync` progress reset (D-5). Seam: if an in-flight
  sweep collides with the service's request token, Code moves the two sweeps after a second `Fault.AfterAcknowledged(Prepared)`
  hook for B; the decisive assertions do not change.
- **V-14** A non-race refusal is not replaced by the same request's re-entry | fake protocol |
  `AgentTaskLandTargetRaceFakeTests.C711_NonRaceRefusalIsNotReplacedBySameRequest`, CP-0 (passes), CP-3 | `h.Verifier.Passed = false`;
  `RequestAsync(expectedSourceSha: source)` + `RunQueuedAsync()` → A `Refused verification_failed`. Re-open the same request
  in the DB exactly as a crash before the terminal commit would leave it: `request.IsPending = true`, `State = Queued`,
  `TerminalEventId = null`, `task.CurrentLandRequestId = request.Id`, `task.LandRequestedAt = request.RequestedAt`,
  `task.LandAttempt = request.Attempt`, `task.Status = Succeeded` (the admission checks at `AgentTaskLandService.cs:359–366`);
  `request.SourceResolutionState` stays `Resolved`. `h.Verifier.Passed = true`; `h.RunAsync()`. Assert operations count 1,
  A unchanged (`Refused verification_failed`), `h.Verifier.Calls == 1`, no `push`, no `Warning` event, terminal
  `LandRefused` contains `verification_failed`. Green before and after — guard row, flagged; its value is PC-5.
- **V-15** The operator step on real git | real git | `AgentTaskLandTargetRaceTests.C711_LocalMasterAheadNamesTheOperatorReset`, CP-0, CP-1 |
  The `AgentTaskLandPublicationTests.C688_LocalMasterAheadOfOriginRefuses` setup (`commit --allow-empty` on canonical
  `master`, not pushed), then `RequestAsync(expectedSourceSha: reviewed)` + `RunQueuedAsync()`. Terminal `LandRefused`
  detail contains `target_local_ahead`, `git fetch origin && git reset --hard origin/master` and does not contain
  `pull --rebase`; the request's `SourceRefusalReason == "target_local_ahead"` exactly (R-6 on the same path); no operation.
  Red at CP-0: no detail. Turned red by: S3 factory detail. (`C688_LocalMasterAheadOfOriginRefuses` itself is unchanged.)

**Fake class.** New file `tests/Antiphon.Tests/Application/AgentTaskLandTargetRaceFakeTests.cs`,
`[Category("Integration")] public sealed class AgentTaskLandTargetRaceFakeTests` (`LandingProtocolHarness`; no process
spawns, so no limiter, as `LandingProtocolGuardTests`). It holds V-9, V-10 (3 rows), V-11 and V-14 so that CP-0 can prove
each red with class-level filters, instead of running three large existing classes in the red gate.

**Code order (replaces the plan's "S4 → tests → S1…").** S0 compile seam: `AgentTaskLandingState.IsTargetRaceRefusal(op)`
returning `false` and `DelegationSettings.LandTargetRaceRetries { get; set; } = 2` with no reader and no validation —
without them V-7/V-12/V-3 do not compile and CP-0 would be a build failure, not a red. S4a fixtures:
`LandingGitFixture.PushIndependentAsync(name)` and `(name, path, content)`, `ControlledLandingGit.AdvanceRemoteTarget()`,
`LandingProtocolHarness.LandSettings` (used by `CreateLand` line 138), `AgentTaskLandRecoveryTests.SeedSchemaTwoAsync`
`internal`. Then all tests and the R-2/R-3/R-4 amendments → **CP-0** (red gate, committed first). Then S4b (the fake
push rejection), S1, S2, S3, S5 → CP-1..CP-4. S0's two members become the real S1/S2 members; nothing is deleted.

### Guards the regression

- **R-1** Unchanged classes stay green: every class in CP-1..CP-3 not named below (the explicit-retry contract
  `AgentTaskLandRefusedRetryTests`, crash-resume `AgentTaskLandRecoveryTests`, publication, fake protocol, approval
  recovery incl. schema-2 derivation `C488_DerivationAlwaysReverifies`, diagnostics, monitoring/aging, stage outcomes,
  the script status reader). Decisive: 0 failed per CP row.
- **R-2** `AgentTaskLandPublicationTests.C448_V09_PushAndConfirmationPreserveCompetingRemoteState` — amended: add
  `h.LandSettings.LandTargetRaceRetries = 0;` after `InitializeAsync` (all four rows; only `non-ff-before-push` races).
  Decisive (unchanged lines): remote `master == rival`, `Directory.Exists(h.Fixture.Source)`, `RemoteConfirmedAt null`.
- **R-3** `AgentTaskLandPreparationIdentityTests.C448_V15_ChangedVerifiedPreparationCanOpenAFreshExplicitOperation` — amended:
  `h.LandSettings.LandTargetRaceRetries = 0;` after `InitializeAsync`. Decisive: `refused.Id.ShouldBe(previous.Id, …)`
  (automatic recovery with the retry disabled still never replaces). Default-budget counterpart: V-13.
- **R-4** `AgentTaskLandSourceFreshnessTests.C488_TargetCheckpointStillGuarded` — amended: `h.LandSettings.LandTargetRaceRetries = 0;`
  (S4a property). Decisive: `Phase Refused`, no `push`.
- **R-5** `LandingProtocolGuardTests.C475_PushExitDoesNotConfirmPublication` and
  `AgentTaskLandApprovalRecoveryTests.C488_PublicationNeedsTargetContainment` — unchanged, **default budget**: a successful
  push followed by a rewrite is `push_unconfirmed`, not a race. Decisive: `h.Git.Trace.Count(a => a[0] == "push") == 1`
  and `Publication != Landed`.
- **R-6** `AgentTaskLandPublicationTests.C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained` — unchanged:
  `SourceRefusalReason.ShouldBe("target_local_ahead")` exactly (the S3 detail must not leak into the reason code).
- **R-7** `AgentTaskLandPublicationTests.C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent` — unchanged:
  a rejected push whose re-observation contains the source is `AlreadyPresent` (D-6's three-way runs only when
  `!ContainsSource`).
- **R-8** `AgentTaskLandRecoveryTests.C688_SchemaTwoOperationsOnResume` rows `local-target-advanced`/`push-started` —
  unchanged: with the remote unmoved the legacy op still lands (the S3 arm fires only on its two reasons).
- **R-9** `AgentTaskLandingStateTests.RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission` rows `automatic`,
  `automatic-changed` — unchanged: an automatic request never replaces a non-race refusal (schema-1 base row).

### Guard inventory

Every safety-critical guard the fix adds or relies on, split where one conjunct can be bypassed alone. `!HasPublication`
inside `IsTargetRaceRefusal` is not listed apart from G-3: `Publication == Refused` implies it (`HasPublication` requires
`Landed`/`AlreadyPresent`), so no mutation of it alone is observable; G-3's PC removes the unpublished check as a whole.
Likewise the `WarningAt`/`LastProgressAt` resets in D-5 are masked by `TransitionAsync`'s own stamp once
`HighestProgress` is reset, so G-17 is the one reset guard.

- G-1: D-3 `IsTargetRaceRefusal` — schema 3 only (legacy rows never auto-retry) | PC-1
- G-2: D-3 — `LastReason == "remote_changed_before_push"` only (no other refusal is auto-replaced) | PC-2
- G-3: D-3 — unpublished, terminal publication outcome (`Publication == Refused`, with the implied `!HasPublication`) | PC-3
- G-4: D-3 — `Phase == Refused` only | PC-4
- G-5: D-4 `CanReplaceRefused` race arm still requires the lease | PC-5
- G-6: D-4 race arm still requires the schema-2/3 approval binding (`ApprovalLandRequestId`, reviewed == original) | PC-6
- G-7: D-4 resolver fall-through (`AgentTaskLandSourceResolver.cs:86–93`) only for a race refusal | PC-7
- G-8: D-2 service loop re-enters only when `IsTargetRaceRefusal(op)` (not on any unpublished result) | PC-8
- G-9: D-6 a failed push is a race only when the re-observed tip differs from `RemoteBeforeSha ?? TargetBeforeSha` | PC-9
- G-10: D-6 a **successful** push followed by a non-containing observation stays `push_unconfirmed` (never a race) | PC-10
- G-11: D-8 retry bound `retries < LandTargetRaceRetries` | PC-11
- G-12: D-8 the bound is read from settings (0 disables) | PC-12
- G-13: D-9 the retried candidate is verified again (`base_unchanged` cannot apply to a re-rebase) | PC-13
- G-14: D-9 the rebase runs only in the land worktree (task branch/worktree untouched by a raced attempt) | PC-14
- G-15: D-9 canonical `master` advances only from `PublicationConfirmed` (never from a refused attempt) | PC-15
- G-16: D-5 a retry enqueues no caller notification | PC-16
- G-17: D-5 the retry resets `HighestProgress` (the monitor does not age a rebuilding retry) | PC-17
- G-18: D-9 a retry re-reads the branch and refuses `source_changed` when it moved (factory line 25) | PC-18
- G-19: D-9 a retry rechecks the remote source (`RecheckRemoteSourceAsync`) | PC-19
- G-20: D-9 a conflicting re-rebase refuses as a conflict with its files (no push) | PC-20
- G-21: D-4/D-9 the replacement inherits the approval identity (`ApprovalLandRequestId`, factory lines 79–80) | PC-21
- G-22: D-7 legacy arm — `remote_changed_before_push` at `LocalTargetAdvanced`/`PushStarted` is terminal | PC-22
- G-23: D-7 legacy arm — `source_changed` at those phases is terminal (independently listed reason) | PC-23
- G-24: D-7 legacy arm limited to its two reasons (`push_rejected` stays resumable) | PC-24
- G-25: D-7 the `target_local_ahead` detail names `fetch` + `reset --hard origin/master`, never `pull --rebase` | PC-25
- G-26: D-7 `SourceRefusalReason` stays the bare code `target_local_ahead` | PC-26
- G-27: D-8 settings validation rejects < 0 and > 5 | PC-27
- G-28: D-12 fake push rejects a non-fast-forward of the target (the fake cannot hide an overwrite) | PC-28

guards = 28, mapped = 28, missing = 0, duplicate PC maps = 0.

### Positive controls

Each PC is one compiling edit, run method-scoped (`--treenode-filter "/*/*/<Class>/<Method>"`) red → restore → green.
Line numbers are the S1–S4 code's; the Mutation stage locates them by the named member.

- PC-1: `IsTargetRaceRefusal` drops `op.SchemaVersion == 3 &&` → `AgentTaskLandingStateTests/C711_TargetRaceRefusalPolicy` red on row `schema2` at `IsTargetRaceRefusal(op).ShouldBe(isRace)`.
- PC-2: drops the `LastReason == "remote_changed_before_push"` conjunct → same method red on rows `reason:verification_failed`, `reason:push_rejected`, `reason:null` at `IsTargetRaceRefusal(op).ShouldBe(isRace)`.
- PC-3: drops the unpublished conjuncts (`Publication == Refused` and `!HasPublication`) → same method red on rows `publication:Unconfirmed` and `published` at `IsTargetRaceRefusal(op).ShouldBe(isRace)`.
- PC-4: drops `Phase == Refused` → same method red on row `phase:PushStarted` at `IsTargetRaceRefusal(op).ShouldBe(isRace)`.
- PC-5: `CanReplaceRefused` returns `true` for `IsTargetRaceRefusal(previous)` before the `!leaseHeld` test → same method red on row `no-lease` at `CanReplaceRefused(op, false, leaseHeld).ShouldBe(automatic)`.
- PC-6: the race arm returns `true` before the schema-2/3 approval test → same method red on row `no-approval` at `CanReplaceRefused(op, false, leaseHeld).ShouldBe(automatic)`.
- PC-7: resolver short-circuit falls through for every `Refused` existing op → `AgentTaskLandTargetRaceFakeTests/C711_NonRaceRefusalIsNotReplacedBySameRequest` red at `operations.Count.ShouldBe(1)`.
- PC-8: service loop re-enters on any unpublished, conflict-free result → `AgentTaskLandTargetRaceTests/C711_PushRejectedWithoutMovementStaysResumable` red at the first `LandRefused` detail `ShouldContain("push_rejected")` (the re-entered resume pushes again and lands).
- PC-9: D-6 `moved` forced `true` → `AgentTaskLandTargetRaceTests/C711_PushRejectedWithoutMovementStaysResumable` red at `ShouldContain("push_rejected")` (reason becomes `remote_changed_before_push`, a retry lands).
- PC-10: D-6 evaluates `moved` before `pushed.Succeeded` → `LandingProtocolGuardTests/C475_PushExitDoesNotConfirmPublication` red at `h.Git.Trace.Count(a => a[0] == "push").ShouldBe(1)`.
- PC-11: bound `retries < budget` → `<=` → `AgentTaskLandTargetRaceTests/C711_BudgetSpentRefusesAndNewRequestLands` (row 2) red at the operations count `ShouldBe(budget + 1)`.
- PC-12: the loop uses a literal `2` instead of `LandTargetRaceRetries` → same method row 0 red at the `Warning` count `ShouldBe(budget)`.
- PC-13: the `base_unchanged` skip condition drops `op.RebasedSourceSha == op.OriginalSourceSha` → `AgentTaskLandTargetRaceTests/C711_PushWindowRaceLandsInOneRequest` red at `h.Verifier.Calls.ShouldBe(2)`.
- PC-14: the rebase `MutateAsync(op, land, [...rebase...])` runs in `op.WorktreePath` instead of `land` → `…/C711_PushWindowRaceLandsInOneRequest` red at the event sequence `[LandRequested, Warning, Landed]` (A refuses `source_changed`; no retry).
- PC-15: the schema-3 catch arm calls `await AdvanceCanonicalAsync(op, CancellationToken.None)` before `Transition(op, Refused)` → `…/C711_PushWindowRaceLandsInOneRequest` red at the in-barrier canonical `master == f.SeedSha` (or at `barrierHit.ShouldBeTrue()` if the premature advance throws and no retry runs).
- PC-16: `WriteRaceRetryAsync` also calls `AddNotification(task, request, warning, LandNotificationKind.Aged)` → `…/C711_PushWindowRaceLandsInOneRequest` red at notifications `ShouldHaveSingleItem()` / `Kind == Outcome`.
- PC-17: `WriteRaceRetryAsync` omits `request.HighestProgress = -1` → `AgentTaskLandTargetRaceFakeTests/C711_RetryRestartsTheProgressClock` red at `HighestProgress.ShouldBe((int)LandPhase.Prepared)`.
- PC-18: delete `if (local.Sha != request.LocalBeforeSha) return new(null, "source_changed");` → `AgentTaskLandTargetRaceTests/C711_SourceMovedBeforeRetryRefuses` row `local` red at `SourceRefusalReason.ShouldBe("source_changed")`.
- PC-19: `RecheckRemoteSourceAsync` returns immediately → same method row `remote` red at B `LastReason.ShouldBe("source_remote_changed")`.
- PC-20: the rebase-failure return passes `[]` instead of `files` → `AgentTaskLandTargetRaceTests/C711_ConflictOnRetryRefusesAsConflict` red at request `State.ShouldBe(NeedsResolution)`.
- PC-21: factory sets `ApprovalLandRequestId = request.Id` unconditionally → `…/C711_BudgetSpentRefusesAndNewRequestLands` (row 2) red at the new op's `ApprovalLandRequestId.ShouldBe(firstRequestId)`.
- PC-22: the legacy arm omits `"remote_changed_before_push"` → `AgentTaskLandTargetRaceFakeTests/C711_LegacyAdvancedRowsBecomeTerminal` row `remote` red at `Phase.ShouldBe(LandPhase.Refused)`.
- PC-23: the legacy arm omits `"source_changed"` → same method row `branch` red at `Phase.ShouldBe(LandPhase.Refused)`.
- PC-24: the legacy arm drops its reason filter → same method row `push-rejected` red at `Phase.ShouldBe(LandPhase.PushStarted)`.
- PC-25: the detail text says `git pull --rebase` instead of the fetch/reset pair → `AgentTaskLandTargetRaceTests/C711_LocalMasterAheadNamesTheOperatorReset` red at `ShouldNotContain("pull --rebase")`.
- PC-26: the resolver stores `AppendDetail(reason, detail)` as `SourceRefusalReason` → same method red at `SourceRefusalReason.ShouldBe("target_local_ahead")`.
- PC-27: remove the `LandTargetRaceRetries` validator clause → `AgentTaskLandTargetRaceTests/C711_RaceRetryBudgetIsValidated` red at `Validate(-1).Failed.ShouldBeTrue()`.
- PC-28: remove the non-fast-forward check from `ControlledLandingGit`'s `push` arm → `ControlledLandingGitTests/C711_PushRejectsNonFastForward` red at step (3) `ExitCode.ShouldBe(1)`.

All 28 are executable: each names a compiling edit, one method (with the row), and one assertion.

### Out of scope

- Caller-side rendering of the outcome message (`LandOutcomeDeliveryHarness`, `PostLandMutationDelivery*`): the path and
  payload shape are unchanged (D-11); only the exhaustion detail string differs, asserted at the event (V-3).
- Schema-3 `source_changed` at `PushStarted` becoming terminal if the legacy arm lost its schema filter: terminal vs
  resumable is not a safety property (either way nothing publishes; an explicit request replaces), so no guard/PC.
- Multi-server concurrent lands on one repository (the intruder here is an independent clone, which is the same remote
  state a second server produces; the lease is per server and unchanged).
- A remote that moves faster than three verifications (D-8 says it is a queue problem; V-3 pins the exhaustion refusal).
- `LandMaxAttempts` × race budget interaction beyond V-13 (one crash + one retry): the product bound 3 × 3 is arithmetic
  over two independently tested bounds.
- Recovery-ref accumulation per retry (plan Follow-ups) and listing active schema-2 rows (ops follow-up).
- The other fake-git consumers (`AgentTaskLandAdmissionControlledTests`, `…BoundaryControlledTests`,
  `…ConcurrencyControlledTests`, `…SourcePersistenceTests`, `LandingSourceFreshnessTests`, `InterimVerificationLandGuardTests`,
  `VerificationRoundDeliveryTests`): none moves the remote target mid-land, injects a push failure, or pushes a
  non-descendant (grep of `RewriteRemoteAwayFromSource`/`SetRemoteContainsSource`/`"push"` at `989abc9f`), so S4b and
  S1–S3 cannot change their outcome; the nightly full run covers them.

### Checkpoints

Test project `tests/Antiphon.Tests`; each row is `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-n -Project
tests/Antiphon.Tests -OutputPath bin-c711x/ -Filter '<filter>' -MinExecuted <Min> -Expect <classes> -ResultsRoot
.antiphon/c711-checkpoints` (`-NoBuild` on CP-2/CP-3, which reuse CP-1's `bin-c711a/`). Filters use a plain `|` on the
command line (escaped `\|` in the table). `Min` = TUnit executions at `989abc9f` with argument rows expanded, plus the new
rows: race 11 (V-1, V-2, V-3×2, V-4, V-5, V-6×2, V-12, V-13, V-15), race-fake 6 (V-9, V-10×3, V-11, V-14), state 66 + 10,
fake git 50 + 1. Run CP-0 once, before S4b/S1–S3, and commit before it; its expected failures are listed by name in the
Code report. Delete `bin-c711r/` and `bin-c711a/` before finishing.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-0 | S0 + S4a + all tests + R-2/R-3/R-4 amendments | `tests/Antiphon.Tests -> bin-c711r/` | race-red | `/*/*/(AgentTaskLandTargetRaceTests*)\|(AgentTaskLandTargetRaceFakeTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)/*` | red gate for V-1..V-15 | exit 1 with **exactly** these failing: V-1, V-2, V-3 (2), V-5, V-6 (2), V-9, V-10 `remote`/`branch`, V-11, V-12, V-13, V-15, V-7 rows `race`/`no-lease`/`no-approval`, V-8 = 19 executions; passing: V-4, V-10 `push-rejected`, V-14, the other 7 V-7 rows, the 116 existing rows | 144 | 7 |
| CP-1 | S5 (all slices) | `tests/Antiphon.Tests -> bin-c711a/` | race-real-git | `/*/*/(AgentTaskLandTargetRaceTests*)\|(AgentTaskLandRefusedRetryTests*)\|(AgentTaskLandRecoveryTests*)/*` | V-1..V-6, V-12, V-13, V-15, R-1, R-8 | exit 0, 0 failed | 48 | 9 |
| CP-2 | CP-1 | CP-1 (`-NoBuild`) | publication-real-git | `/*/*/(AgentTaskLandPublicationTests*)\|(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandStageOutcomeTests*)/*` | R-1, R-2, R-3, R-6, R-7 | exit 0, 0 failed | 91 | 9 |
| CP-3 | CP-1 | CP-1 (`-NoBuild`) | fake-protocol-aging | `/*/*/(AgentTaskLandTargetRaceFakeTests*)\|(AgentTaskLandSourceFreshnessTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(AgentTaskLandApprovalRecoveryTests*)\|(AgentTaskLandFailureDiagnosticTests*)\|(AgentTaskLandMonitoringTests*)\|(AgentTaskLandQueueAgingTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)\|(DelegateScriptLandStatusTests*)\|(ProcessSpawnLimitTests*)\|(TestLaneCategoryGuardTests*)\|(DelegationHarnessCensusTests*)/*` | V-7..V-11, V-14, R-1, R-4, R-5, R-9, census of the two new classes | exit 0, 0 failed | 377 | 9 |
| CP-4 | S5 | n/a | docs-named | `git grep -n -e "LandTargetRaceRetries" -e "raced with a push" -e "never moves the local target" -- docs/orchestration-loop.md docs/ops-http.md docs/session-runtime-invariants.md server/appsettings.json` | S5 | ≥ 4 matching lines across the four files, exit 0 | n/a | 1 |

CP-3 `Min` = 6 + 58 + 23 + 18 + 56 + 25 + 25 + 9 + 76 + 51 + 19 + 3 + 1 + 7 = 377. The union of CP-1..CP-4 is the whole
ordinary scope: every class that drives the changed service/protocol/resolver/factory/fake-git through a race, a push
failure, a refused replacement, a schema-2 row, or the aging monitor, plus the census guards for the new files.

### Cost

- **Ordinary V/R floor (Code)** = CP-0 7 + CP-1 9 + CP-2 9 + CP-3 9 + CP-4 1 = **35 minutes** (estimated). Basis: the
  plan-stage probe measured 3 real-git race tests in 54 s (≈ 18 s each) and a ~90 s isolated build on this runner;
  CARD-0498 measured ~11 min for six land classes serialised by `ProcessSpawnLimit` (≈ 4–5 s per real/fake harness row).
  CP-1: 11 × 18 s + 37 × 5 s + 1.5 min build ≈ 8.9 min; CP-2: 91 × 5 s ≈ 7.6 min (no build); CP-3: 131 unit rows < 0.5 min,
  ~227 harness rows (limiter-serial for two classes, parallel for the rest) + 19 pwsh rows ≈ 8 min; CP-0: 11 real-git red
  rows fail faster than they pass (≈ 3 min) + build + units ≈ 6 min. Every row is sized under one 10-minute foreground
  window; if a measured row exceeds it, Code reports the measurement rather than splitting silently.
- Savings vs the plan's table: the plan's CP-0 would not have compiled (V-7/V-12 reference absent members) and its CP-1
  (14 min estimated) exceeded a foreground window; this table adds `AgentTaskLandPreparationIdentityTests`,
  `AgentTaskLandApprovalRecoveryTests`, `AgentTaskLandFailureDiagnosticTests` and the census guards for +~2 min net, and
  catches R-3's red that the plan's scope missed.
- **PC floor (Mutation)** = 28 PCs, each red build (~1.5 min incremental) + method-scoped red run + restore build (~1.5 min)
  + green run. Real-git methods (PC-8, -9, -11..-16, -18..-21, -25..-27: 15 PCs) ≈ 4 min each = 60 min; fake/unit methods
  (PC-1..-7, -10, -17, -22..-24, -28: 13 PCs) ≈ 3.3 min each = 43 min. Unbatched **≈ 103 minutes** (estimated). PC-1..PC-6
  share one method and cannot batch; batching independent files (e.g. PC-28 with PC-27, PC-25/26 with PC-22..24, PC-17 with
  PC-10) saves ≈ 25 min → **≈ 78 minutes** batched (estimated).
- **Total** = setup/build 1.5 + V/R 35 + PC 78 ≈ **115 minutes** (estimated; no row measured in this stage).
