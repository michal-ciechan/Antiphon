# CARD-1153 S4 (dispatcher side): Code evidence

Code task `c6c07d1e-d147-4977-87ae-a91f1136ef14` (its own landing owner), branch
`feat/card-task-c6c07d1e`, runner mirror worktree `/work/worktrees/task-c6c07d1e`, fast-forward
from `origin/master` `8e1fe916cc421a98dbef46d73c1ee9bf97ef6f3e`. Plan
`2026-10-08-card-1153-runner-absence-evidence-plan.md` (D-1, D-4, D-5, S4 row); test design
`2026-10-08-card-1153-test-design.md` (V-14..V-20, V-24, CP-18..CP-25). S5 is not in this slice.
The checkpoint receipts (unedited CHECKPOINT lines) are in the Code report; this note records the
design decisions, the red checks and the disclosures.

## Production change (`server/Application/Services/AgentTaskDispatcher.cs` only)

- Cold dispatch: after the claim commits (and after the launch spec is finished) and before either
  launch sink, `PrepareAbsenceEvidenceAsync` sends one prepare for the session id this invocation
  allocated. Skipped for a released-seat answer resume. Unsupported, refused, failed or slow
  preparation is logged at Debug and never gates the launch; caller cancellation propagates.
  Warm reuse, boot-wedge relaunch and interrupted-launch resume have no call site.
- Due absent-launch decision: `ReadAbsentLaunchEvidenceAsync` no longer reads the runner
  transcript; it returns database facts with the native-empty fact unknown. `DecideAbsentLaunchAsync`
  pre-screens those facts with a provisional native-empty fact (false when a native attempt was
  found), asks the owning runner for a certificate only if the pre-screen passes, then runs the
  unchanged `AbsentLaunchPolicy` again with the native-empty fact = "a validated certificate exists".
  `nativeAttempt` stays an unconditional exclusion.
- `ReadAbsenceCertificateAsync`: only a `Proven` result for the same session,
  `SessionGeneration.Equal` generation and (when bound) store is kept, together with the store the
  request named and a deadline on the dispatcher's clock taken before the request (now + 5 s). Any
  other result, exception or unsupported transport is null (today's Failed path).
- `HoldUnderLockAsync`: after the existing status/attempt/session/generation/reason rechecks, the
  database re-read and the Working/inventory withholds, the DB facts must still pass (else
  NotThisShape, as before) and the first read's certificate must still hold for the locked row:
  same session, generation, requested store equal to the row's store, certificate store equal to a
  bound store, and unexpired on the dispatcher's clock. A certificate that no longer holds is a
  **withhold** (task untouched; the next due pass asks again; a closed record re-certifies with a
  fresh nonce), not a failure. One certify request per hold.
- Statement shape: `RunnerStoreId` added to the two existing `AgentSessions` projections; no new
  query. No migration. No change to `AbsentLaunchPolicy`, `SessionReconciliationService.cs`,
  `AttentionService.cs` or the client.

## Test changes

- `DelegationDispatchRecoveryBoundaryTests.AbsentEvidence.cs`: the eight skeletons replaced (28 cases).
  Fixtures: `EvidenceHost` (real `SessionRunnerRuntime` over a temp root, random-port Kestrel with the
  production capabilities, session GET, transcript and `AbsenceEvidenceRoutes`, synthetic key file,
  production `SessionRunnerHttpClient`); `RemoteEvidence` (real runtime behind
  `PhoneHomeRuntimeAdapter` + `PhoneHomeCommandDispatcher` as the scripted peer of
  `PhoneHomeTestHost`, routed through `RoutingSessionRunnerClient`/`PhoneHomeRunnerClient`);
  `SigningStub` (the production HTTP client over a handler that signs each V-15 shape with the
  client's key, so each is refused for the shape itself).
- `.AbsentLaunch.cs`: `CountingRunner` gains `Evidence` (forward prepare/certify/transcript to a
  production transport), `Certify`, `Prepares`, `Certifies`, `OnPrepare`; `OpenSweep` gains an
  optional `directory`. The positive fixture's explicit evidence is now an explicit certificate:
  `FixtureCertificate` runs a version 1 shape through the production validator as authenticated.
- `.AbsentLaunchWhitelist.cs`: the six unknown-native cases keep their names and every assertion;
  each condition now varies the certificate (unreadable throws, null result, `complete=false`,
  `sidecarTranscriptPresent=true`, another session id, generation + 1 h). Added
  `runner.Certifies > 0` (the certificate was asked for and refused). No assertion weakened or
  deleted anywhere.
- V-24 pins measured here: due hold **19** statements without a parent note, **21** with one
  (ceilings 40/48). 18/18/4 unchanged by construction (CP-31).

## Red checks (quick mutations, not PCs; each restored by `git checkout`, tree verified clean)

One isolated build per mutation set (`bin-c1153s4mut/`), filter
`/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_*` (28), at source `b15cfbfa` (A-C at `97ff5b36`;
the test-only V-16 fix between them changes no production line). Mutations in one set touch
disjoint arguments.

| Set | Mutation (production line) | Red (intended assertion) |
|---|---|---|
| A | `ReadAbsenceCertificateAsync` never yields proof | V-14 local-http, remote-phone-home (Blocked expected, Failed) |
| B | deadline dropped from `AbsenceCertificateHolds` | V-16 expired-proof |
| B | `DiscardUncommittedHoldAsync` call removed | V-20 |
| B | prepare moved after the enqueue | V-18 cold-fresh (order), prepare-faulted (order) |
| B | one extra SELECT in `ReadAbsenceCertificateAsync` | V-24 no-parent, parent (exact pins) |
| C | early Working guard removed in `DecideAbsentLaunchAsync` | V-17 working-task (certify asked) |
| C | prepare added to `RelaunchWedgedAsync` | V-18 recovery |
| C | capability gate removed in `PhoneHomeRunnerClient.ExchangeAbsenceAsync` | V-18 remote-old-runner |
| D | both store compares dropped from `AbsenceCertificateHolds` | V-16 store-or-epoch-change (Dispatched expected, Blocked) |
| D | generation recheck and certificate generation compare removed | V-16 changed-generation |
| D | DB pre-screen condition removed (certify always) | V-19 attempted-delivery, extra-related-row, native-attempt |
| D | owning-inventory guard before the certificate removed | V-17 unavailable-owning-inventory |
| D | prepare added to `TryReuseWarmAgentAsync` | V-18 warm-reuse |
| E | provisional native-empty fact used in the decision | V-15 all 8 shapes |
| E | failed prepare thrown out of dispatch (catch disabled) | V-18 prepare-faulted |
| E | prepare added to `ResumeInterruptedLaunchAsync` | V-18 resume |
| E | early Working guard and post-evidence Working recheck both removed | V-17 working-transcript |

Collateral reds in each set (V-24 pins when a statement disappears, V-16/V-20 under A, recovery
under E's prepare gate) are expected and not counted as the witness.

## Disclosures

1. V-14 remote-phone-home: the prepare is issued by the test through the production routed client
   (the same `ISessionRunnerClient.PrepareAbsenceEvidenceAsync` the dispatcher calls), not by a
   remote cold dispatch; a remote dispatch needs the remote-workspace preparation fixture. V-18
   cold-fresh proves the dispatcher's own call site and order. The misleading local inventory lists
   the id as `Exited`: a locally `Running` id is the pre-existing CARD-0056 gate, which leaves any
   task alone before the absent-launch decision.
2. V-18 remote-old-runner exercises the routed phone-home client's capability gate (no
   `PrepareAbsenceEvidence` frame), not a dispatch.
3. V-18 recovery = `RelaunchWedgedAsync` (fresh id allocated after the cold dispatch prepared; the
   prepare count stays 1); resume = `AgentSessionService.ResumeInterruptedLaunchAsync` (existing id).
   The released-seat-answer exclusion on the cold path has no V-18 argument: implemented, not
   witnessed (Review/Mutation gap candidate).
4. V-16 store-or-epoch-change changes the bound store of a runner-bound session (a local session
   has no store binding; the first fixture version changed one and the injected update itself
   failed, which the store red check exposed). An epoch change after the first read is not
   observable to the dispatcher by design: the certificate is bound by identity and a five-second
   life; a prior-epoch certify is refused at the runner (V-3, V-23).
5. V-17 working-transcript and V-16 changed-generation are each covered by two independent guards
   (the existing S1 guard plus the new one); each goes red only when both are removed.

## Activation

Server-only change: AppHost restart after land. The runner side (S1-S3) is already on master and
the desktop runner was restarted today at landed code; S4 needs no runner restart. Order unchanged:
runners first, then AppHost. Without a provisioned direct-HTTP key the local runner answers
unsupported and today's Failed outcome stays (D-5).

## Pending

Every PC (PC-20..PC-26 and PC-28 for this slice) stays pending for post-land SourceLanding Mutation.
Next slice: S5 (owner documents, V-21 doc pin, final regression group CP-26..CP-36 including the
Windows rows CP-35/CP-36).
