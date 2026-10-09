# CARD-1153 S4 repair: monotonic certificate age (Code ed868d4e)

Repairs Final Review a9d35067 R1 on top of 8c7d59cb7f049d3969214595c7a49879ed9abcd6 (landing
owner c6c07d1e-d147-4977-87ae-a91f1136ef14). Production change: `server/Application/Services/AgentTaskDispatcher.cs` only.

## Defect

The hold re-check under the task-row lock compared only `UtcNow() <= LocalDeadline`, a wall
deadline taken before the certify request. A wall rollback extended the five-second life (Review
probe: six seconds elapsed, wall stepped back two seconds, Blocked was committed).

## Fix and rule

- `ReadAbsenceCertificateAsync` (AgentTaskDispatcher.cs:2786) captures `_timeProvider.GetTimestamp()`
  before the request, beside the existing wall deadline (:2787); the private `AbsenceCertificate`
  record carries both. It is constructed only there, in this dispatcher instance, and dropped after
  the decision, so a certificate from another process or lease, or one without a timestamp, cannot
  reach the predicate.
- `AbsenceCertificateHolds` (:2823-2825) requires `0 <= GetElapsedTime(RequestedTimestamp) <= 5 s`
  AND `UtcNow() <= LocalDeadline`. The monotonic age decides the life; a negative age is not an age.
- Pinned rule for jumps: a wall jump FORWARD past the deadline withholds even with a fresh monotonic
  age (either clock saying expired withholds; the next due pass asks the runner again). A wall
  ROLLBACK with a fresh monotonic age holds: the monotonic age is the truth and the wall bound can
  only shorten, never extend, the life.
- The age is measured from before the request, so it covers the exchange plus the life: the hold
  happens at most five monotonic seconds after the request was sent.

## Clock sweep: prepare / certify / hold

| Read | Where | Kind | Verdict |
|---|---|---|---|
| Certificate timestamp | AgentTaskDispatcher.cs:2786 | monotonic | new deciding bound |
| Certificate wall deadline | AgentTaskDispatcher.cs:2787, checked :2825 | wall | kept as fail-safe second bound: a rollback cannot extend past the monotonic bound; a forward jump only withholds |
| Monotonic age check | AgentTaskDispatcher.cs:2823-2824 | monotonic | deciding |
| Prepare call | AgentTaskDispatcher.cs:2757-2775 (call site :5535) | none in dispatcher | prepare is never evidence; the client bounds it with the timer below |
| Due-pass `now`, dead-session grace | AgentTaskDispatcher.cs:2554, :2573 (DeadSessionFirstSeenState.cs:28) | wall | pre-existing CARD-0085/1149 delay gate, unchanged by S4: a jump only moves when the fully gated decision runs; a hold still needs positive inventory absence, the DB facts and a fresh certificate. It also times the ordinary dead-session failure path, which is outside S4; recommended for a separate card in the CARD-1143 F1 class |
| Commit-recovery hold window | AgentTaskDispatcher.cs:2626 | wall | pre-existing CARD-0547 gate before the decision; non-deciding for the certificate |
| Blocked event `At` | AgentTaskDispatcher.cs:6849 (StageBlocked) | wall | record stamp only |
| Parent note `CreatedAt` | AgentTaskService.cs:4559 | wall | record stamp only |
| Exchange deadline | SessionRunnerHttpClient.cs:665 `CancellationTokenSource(AbsenceDeadline, _time)` | TimeProvider timer (monotonic on the system provider) | deciding, already monotonic |
| `sentAt` / `receivedAt` | SessionRunnerHttpClient.cs:667, :735 | wall | feed the validator; see next row |
| Validator elapsed and receipt life | RunnerAbsenceEvidenceValidator.cs:41, :43 (now from SessionRunnerHttpClient.cs:651) | wall | a jump forward or back past receipt refuses (fail-safe Unknown); a rollback that makes them pass is subsumed by the dispatcher's monotonic bound from before the request, which is stricter. Not edited (client area held for CARD-1121/1156) |
| Request stamp | SessionRunnerHttpClient.cs:695 | wall | runner-side replay freshness across hosts; a skew refuses (no certificate) or is bounded by the fresh MAC'd nonce; never an age for the hold |
| Capability cache TTL | SessionRunnerHttpClient.cs:318, :493 | wall | pre-existing feature cache; a capability is never evidence |
| `SessionRunnerAbsenceEvidence.ReceivedAt/ExpiresAt/IsFresh` | SessionRunnerAbsenceEvidenceDto.cs:23-27 | wall | audit fields; not consulted by the dispatcher |

## Regression (V-25): `C1153_Certificate_age_is_monotonic`

Dispatcher-level, real PostgreSQL schema, production `SessionRunnerHttpClient` against the real
evidence host. `SplitClock` (a `FakeTimeProvider` whose timestamp and wall move independently;
timers stay on fake time) is shared by the dispatcher, client and runtime. `LockedClockStep` steps
the clocks at the hold's task-row FOR UPDATE, after the certify exchange.

| Case | Elapsed | Wall delta | Expect | Red on 8c7d59cb | Red under mutation |
|---|---|---|---|---|---|
| review-rollback | +6 s | +4 s | Withheld | yes (Blocked) | drop the monotonic clause (= base) |
| rollback-before-request | +6 s | -1 s | Withheld | yes (Blocked) | drop the monotonic clause (= base) |
| negative-age | -1 s | 0 | Withheld | yes (Blocked) | drop `age >= TimeSpan.Zero` |
| forward-jump-fresh | +1 s | +6 s | Withheld | no | drop the wall clause |
| wall-rollback-fresh | +1 s | -3 s | Held | no | pinned rule; pending for Mutation |
| exact-five-seconds | +5 s | +5 s | Held | no | `age <= Lifetime` to `age < Lifetime` |
| fresh | +1 s | +1 s | Held | no | pending for Mutation (always-false predicate) |
| ordinary-expiry | +5 s + 1 tick | +5 s + 1 tick | Withheld | no | doubly guarded: red only with both clauses dropped; pending for Mutation |

Red checks (quick mutations, not PCs), probe build `bin-ed868d4e-mut/`, UseAppHost=false, method filter
`/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Certificate_age_is_monotonic*`:

- fixed source: 8 executed, 8 passed.
- 8c7d59cb dispatcher with the new test: 8 executed, 3 failed (review-rollback, rollback-before-request,
  negative-age: `task.Status should be Dispatched but was Blocked`), 5 passed.
- one build with three disjoint mutations of the changed predicate (`age < Lifetime`, no `age >= 0`,
  wall clause replaced by `true`): 8 executed, 3 failed (negative-age and forward-jump-fresh: Blocked
  for Dispatched; exact-five-seconds: Dispatched for Blocked), 5 passed. Each failing case depends on
  exactly one of the three mutations. Restored by `git checkout -- server`; tree verified clean.

## Checkpoint

CP-32 (S4): `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Certificate_age_is_monotonic*`, 8
executed, 0 failed/skipped, Serial, reusing the S4 build. Existing assertions, the 18/18/4 budgets
and the 19/21 due-hold pins are unchanged.

## Activation

AppHost restart after land (server code only). No runner restart, no migration.

## Pending

Every PC stays pending for method-scoped SourceLanding Mutation, including the V-25 cases listed
as pending above.
