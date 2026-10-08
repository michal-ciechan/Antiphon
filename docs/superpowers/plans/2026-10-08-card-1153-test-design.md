# CARD-1153 test design

TestDesign for `docs/superpowers/plans/2026-10-08-card-1153-runner-absence-evidence-plan.md`
(plan commit `999de4d8b0018b726cd60aea0328b204e0fa3b60`, landed on master as `3c6fd43fe`).
The fix design (D-1..D-6) is unchanged. This file is the checkpoint `--plan` input: the
tool reads the first `### Checkpoints` table below. Production code was not edited.
Baseline for every citation: master `e987af29fe242fe6c8d18bd86991a3f4c71a4ac1`.

Committed beside this note are compiling skeletons that fix the class, method and
argument rosters named below. Every skeleton body throws TUnit's `SkipTestException`
with its slice tag, so it is discovered, counted as skipped, never green, and cannot be
mistaken for proof. Code replaces each body in the slice that lands the behaviour.

| Skeleton | Project | Slice |
|---|---|---|
| `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceTests.cs` | runner | S1 |
| `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceRuntimeTests.cs` | runner | S2 |
| `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceContractTests.cs` | runner | S3 |
| `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidencePhoneHomeTests.cs` | runner | S3 |
| `tests/Antiphon.Tests/Agents/SessionRunnerAbsenceEvidenceClientTests.cs` | server | S3 |
| `tests/Antiphon.Tests/Agents/AbsenceCertificateShape.cs` (fixture) | server | S3 |
| `tests/Antiphon.Tests/Application/RunnerAbsenceEvidenceValidatorTests.cs` | server | S3 |
| `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentEvidence.cs` | server | S4 |
| `tests/Antiphon.Tests/Application/SessionRunnerAbsenceEvidenceDocumentationTests.cs` | server | S5 |

## Protocol audit

The certificate is safety-critical: a wrong one lets the dispatcher declare a session
never attempted while the runner creates it, and CARD-0079 is the only automatic stop
of a Working session. The rule under audit is a whitelist: today's Failed outcome is kept
unless a complete, authenticated, fresh certificate names the same session id, the
`SessionGeneration.Equal` generation, the owning store and the serving epoch, with every
presence field explicitly false. Anything unknown keeps today's behaviour.

### Failure table

One row per hazard. "Required outcome" is what production must do; "Witness" is the
V/R/PC that proves it. Rows marked A-n add something the plan left implicit.

| H | Hazard | Plan coverage | Required outcome | Witness |
|---|---|---|---|---|
| H-1 | Key-less or unauthenticated direct HTTP prepare/certify | D-3 | 401/403 typed; no record written, no transition; HTTP certification unavailable; launch unaffected; S1 due failure stays Failed | V-10 missing-key, wrong-request-MAC; PC-14 |
| H-2 | Forged/replayed request: wrong MAC, reused nonce, route id, body id and signed id disagree | D-3 nonce and MAC; id agreement is A-1 | 400/401; no state change. Revised by Review 1a174347 F2: a replayed request (a nonce already presented to this runner epoch, any operation or outcome; issued outside the 30 s freshness window; issued before the epoch start plus that window) is refused 409 `absence_evidence_replayed` or 401 `absence_evidence_stale_request` with no state change. A fresh-nonce prepare of the identical identity in the same epoch returns the original record; a fresh-nonce certify on ClosedUnused re-runs every exclusion before a fresh certificate | V-10 changed-request-field; V-12 wrong-ID, wrong-nonce; V-23 nonce and replay rows; V-26; PC-14 |
| H-3 | Replayed, substituted or altered response (old certificate, another session's, one flipped bit) | D-3 response MAC over all evidence plus request nonce; D-4 fresh nonce per read | Not a proof; Failed | V-10 altered-response-bit; V-12 unsigned, bad-MAC, wrong-nonce; PC-15, PC-17 |
| H-4 | Creation holds the launch gate when certify arrives | D-1 Attempted under the gate before the first effect; D-2 same gate | Certify waits, observes Attempted, answers 409; dispatcher keeps Failed | V-7 launch-first; V-5; PC-9, PC-11 |
| H-5 | Certify holds the gate; a delayed create arrives after the reply | D-2 ClosedUnused before reply; fence at every entry | Create refused with a typed error; no process, no artifact; closed for every generation | V-7 certify-first; V-8 (4); PC-11, PC-12 |
| H-6 | Launch reached the runner, process failed to start, runtime entry removed | D-1 preserve marker on failed start | Attempted persists; certify 409; today's Failed classification; no hold | V-5 throw arm; V-6; PC-9, PC-10 |
| H-7 | Phone-home admission refused before `StartAsync` (capacity, signed-out, platform) | A-2 | No Attempted and no watermark exist (capacity throws before `Record`); the id was never created, so a certificate is correct. A watermark written just before `StartAsync` is an exclusion at prepare and at certify | V-23 watermark-present; V-2 watermark; PC-5, PC-27 |
| H-8 | Pre-ack Launch re-send after certification | CARD-0679 D-9 re-send; fence | Re-send refused with the closed-identity problem type (A-3); the desktop launch path treats it as a terminal refusal, never a retry loop | V-8 same-generation; R-4 (CP-32, CP-33); PC-12 |
| H-9 | Runner restart between prepare and certify; old-build interlude; downgrade | D-1 epoch | Prior-epoch Prepared/ClosedUnused refuse certify and re-prepare; no retroactive proof; Failed | V-3 (3); V-23 prior-epoch rows; PC-6 |
| H-10 | Epoch derived from a clock, or reused by a quick restart | A-4 | Epoch is a random value per evidence-service instance, compared by equality only, nonempty in every certificate | V-3; V-22 epoch rows; PC-6, PC-18 |
| H-11 | Certify before adoption completes (phone-home connects before HTTP readiness) | D-2 complete adoption | 503 typed; no certificate; a later request after readiness may succeed | V-23 adoption-incomplete; PC-27 |
| H-12 | Recycled volume, missing root after init, IO error, corrupt record, unknown schema | D-1 strict reads | Unknown evidence, never a blank store or a Prepared record; prepare and certify refuse; Failed. F1: creation and attach also refuse on unknown evidence, so a certified closure survives later storage failure | V-4 (5); V-23 corrupt-record, store-header-changed; V-25; PC-8 |
| H-13 | Generation: resume under a newer generation; wire precision; DB versus wire normalization | D-4 `SessionGeneration.Equal` | Only normalized-equal accepted (sub-microsecond tolerance, one microsecond refuses); a resume of a closed id is refused, never turned into a fresh session | V-22 generation rows; V-8 newer-generation; V-16 changed-generation; PC-18, PC-12, PC-22 |
| H-14 | Clock skew between server and runner; the runner's wall clock steps backwards or forwards (revised by Review a086fe80 F2) | D-4 nonce plus local elapsed; `observedAtUtc` audit only; F2 signed `issuedAtUtc` with a per-epoch high-water mark and a monotonic step check | The server's own read uses only local elapsed time (5 s local elapsed, 5 s validated age via TimeProvider). The runner does compare across machines: it admits a request only if the server-signed `issuedAtUtc` is within 30 s of the runner's wall clock, so server and runner clocks must agree within 30 s or every evidence request refuses (401 stale; the dispatcher keeps Failed). Within an epoch admission is monotone: a request issued more than 30 s before the highest `issuedAtUtc` already seen is refused for good, a nonce is retired only below that mark, and a backward wall-clock step beyond 500 ms (+0.1% of the monotonic gap) refuses every evidence request for 30 s of monotonic time; a forward jump makes requests stale until it is undone. The first 30 s after a runner start refuse evidence requests (launches unaffected). Residual: the mark is memory only, so a pre-restart request replayed to a runner whose wall clock was behind at its start can pass the clock checks; the epoch rule (prior-epoch records refuse certify and re-prepare) bounds its effect to what the server could ask for anyway | V-13; V-23 freshness rows; V-26; V-29 (5); PC-19, PC-31 |
| H-15 | Crash after claim commit, before prepare | D-1 | No proof; Failed (today) | V-18 prepare-faulted; V-15 plain404 |
| H-16 | Crash after prepare before enqueue, or launch lost in transport | D-1, D-2 | Runner never created it; certify succeeds; Blocked hold with original input; zero relaunch | V-14 (2); PC-20 |
| H-17 | Old runner, new server | D-5 | No capability: no prepare POST/frame; launch unchanged; due failure stays Failed | V-18 remote-old-runner; V-15 old-runner; PC-24, PC-21 |
| H-18 | Old server, new runner | D-5 | Attempted markers on every launch; launch success unchanged; a marker write failure latches only evidence; an unreadable or damaged evidence store refuses creation (F1) | V-6; R-4; R-9; R-11; PC-10 |
| H-19 | Mixed fleet: the owning runner's capability decides, not the local one | D-3/D-4 routing | Capability read on the routed owner; a remote owner is never judged from the local list | V-14 phone-home; V-17 unavailable-owning-inventory; V-18 remote-old-runner; PC-23 |
| H-20 | Prepare on a reused pool, recovery, resume or DB-selected id | D-1 | Never called there; an id with history refuses anyway | V-18 warm, recovery, resume; V-2; PC-24 |
| H-21 | Provisional native-empty fact escapes the pre-screen | D-4 | Final decision uses only the validated certificate; a nonpristine brief never calls certify; nativeAttempt=true excludes | V-19 (3); PC-25 |
| H-22 | Attempt rebind or status change between reads | existing S1 recheck | Withheld | R-1 `C1149_Changed_or_working_attempt_is_untouched`; V-16 |
| H-23 | Working task or transcript; owning inventory unavailable while the local list is empty | existing S1 withhold | No evidence request, no task or process mutation | V-17 (3); PC-23 |
| H-24 | Runner filesystem tampering (hand-written Prepared record) | out of scope | Runner's own authority, same trust as manifests and sidecars | documented only |
| H-25 | Key custody: key in argv, env, logs, capabilities DTO or a child's environment | D-3 | File path through configuration custody only; no environment name exists for the key; never logged; absent from capabilities; children receive `request.Env` only | V-10 canary (no key bytes or path in any response or captured log); Review checklist |
| H-26 | Evidence store grows without pruning | D-1 retain | One small file per cold dispatch; accepted; AuditCleanup must not own the directory | V-4 lost-root; documented |
| H-27 | Two sweeps or two dispatchers certify concurrently | D-2 | First transitions, second re-checks and re-issues; only one hold commits under `FOR UPDATE` | V-8 repeat certificate; R-1 hold idempotence |
| H-28 | Phone-home connection replaced mid-request (reconnect, new epoch) | CARD-0679 correlation; D-4 | Typed transport loss; no proof; Failed or Withheld per existing rules | V-11; V-16 store-or-epoch-change; PC-16, PC-22 |
| H-29 | Custody-bound (verification) launch for a closed id | A-5 | Refused before any custody record or `PrepareStart` | V-8 custody-bound-start; PC-12 |
| H-30 | Generic HTTP resilience retries or the 100 s request timeout on certify | D-4 one attempt, 5 s | Exactly one transport attempt per read, one nonce, 5 s total | V-13 one-transport-attempt, over-deadline; PC-19 |
| H-31 | Herdr attach or startup adoption registers a closed id | D-2 | Attach refused; adoption cannot find a manifest for a closed id because prepare refuses when one exists; a later manifest is H-24 | V-8 attach; V-5 adoption arm; PC-12 |
| H-32 | Certificate requested while the owning inventory lists the session | existing S1 inventory gate | Withheld before any certificate request | R-1 `C1149_Listed_or_unknown_runner_is_never_absence`; V-17 |
| H-33 | Unknown extra JSON members in a signed certificate | A-6 | Rejected under version 1 (strict member set) | V-22 unknown-extra-member; PC-18 |
| H-34 | A local HTTP session has `RunnerStoreId` null in the DB | A-7 | Remote session: certificate store equals `AgentSession.RunnerStoreId`. Local session: store nonempty; the per-runner key file plus epoch and record are the binding | V-22 store rows; V-16 store-or-epoch-change; PC-18, PC-22 |
| H-35 | A certificate was issued, then the record or the store is corrupted, truncated, emptied, deleted, replaced by a directory, made unreadable, wiped, replaced or lost (Review 1a174347 F1) | D-2 + F1 store identity and closure log | Delayed start or attach refused with the closed-identity type before any effect; the id never certifies again; residual: deleting both a closed record and its closure-log line, or the anchor with the whole root, is runner-store tampering (H-24) | V-25 (24) |
| H-37 | Power loss after a certificate drops directory entries that were created or renamed but not yet synced (Review a086fe80 F1) | D-2 + F1 round 2 directory sync | Every created directory, created file and rename is followed by a sync of its directory (Unix fsync of the directory; Windows write-through rename and FlushFileBuffers on the directory handle; any failure fails closed: no certificate, latched); the anchor is written last; a store this process did not initialize is synced once before its first write. A crash at any step leaves a never-initialized or healthy store; a returned certificate's record, closure-log line, root and anchor survive. Windows behaviour is implemented but executed only on Linux in this round | V-28 (11); PC-30 |
| H-38 | Closure record and closure log disagree: a ClosedUnused record without its log entry, one differing from its entry, a duplicated, reordered or foreign log line (Review a086fe80 F3) | D-2 + F3 round 2 hashed, chained closure log | Unknown evidence: the id (or, for a broken chain, the whole store) refuses creation with the closed-identity type and never certifies; certify refuses without a prior fence latch. A logged closure whose record is missing or not ClosedUnused stays V-25 | V-30 (5); V-31 (2); PC-32, PC-33 |
| H-36 | Capability discovery consumes the five-second budget (Review 1a174347 F3) | D-4 + F3 | One deadline from before discovery; a pending or slow discovery ends at the deadline with Unknown / not prepared, never proof | V-27 (4) |

### Additions the plan leaves implicit

- A-1: the route id, the body id and the MAC-covered id must be one value; a disagreement is 400 with no state change.
- A-2: phone-home admission refusals before `_runtime.StartAsync` (capacity, signed-out, platform) write no Attempted marker and, for capacity, no watermark. The watermark is an exclusion at certify as well as at prepare.
- A-3: a creation refused by ClosedUnused uses one problem type on both transports, `phone_home_session_identity_closed` on phone-home and HTTP 409 type `session_identity_closed`. The server-side launch sink maps it to the existing refused-launch path; it must not retry.
- A-4: the runtime epoch is a random `Guid` created with the evidence service, held in memory only, written into every record, compared by equality, never derived from time.
- A-5: the fence and the Attempted marker sit inside the launch gate before `_custody.PrepareStart`, before Grok rules file writes and before the manifest or sidecar `SaveAtomic`. The first provider effect is `launcher.LaunchDetachedAsync` on pty, `ConnectAndValidateAsync` on herdr attach and `_sessions.TryAdd` on adoption.
- A-6: version 1 certificates have a closed member set; an unknown member fails validation even when the MAC verifies.
- A-7: expected store is `AgentSession.RunnerStoreId` when non-null; for a local session the validator requires a nonempty store and relies on the key file, the epoch and the record identity.
- A-8: two one-condition-flip tables (V-22 validator, V-23 certify service) and a due-path statement ceiling (V-24) are added. Each table case first admits an independently seeded pristine positive, then flips exactly one condition and requires refusal.
- A-9: regression rows R-10 (authenticated transport) and R-11 (Linux launch admission through both launch routes) are added because S2 and S3 edit the launch path.
- A-10: the certify-side service takes an inspection seam (runtime entry lookup, artifact presence, adoption-complete flag, fault latch, watermark reader) so V-23 runs at S1 without a process. The runtime wires the real inspection in S2. This is a test seam, not a change to the decision.

No unfixable hole was found. Every hazard has a required outcome that the plan's design can meet; A-1..A-10 are clarifications that Code must implement and the named witnesses detect.

## Restart order and serialization

Activation order after Code, Review and Land: runners first, AppHost second. A new runner
binary is necessary for certificates; a new AppHost against an old runner simply finds no
capability and keeps today's behaviour (H-17), while a new runner under an old AppHost only
writes markers (H-18). Reversing the order is safe but activates nothing until the runner moves.

- Desktop runner: `pwsh -NoProfile -File scripts/restart-session-runner.ps1` from the canonical checkout, without `-KillSessions` (human-only) and without `-AllowWorktree`. The script performs an incremental build, launches and completes adoption before HTTP; detached sessions survive and are re-adopted. `-Hard` only for a planned supervisor refresh. A runner restart never kills a Working session; a stall is a decision, not a kill.
- server2 runner: the documented rolling phases (`deploy-temp`, `drain-old`, `redeploy-old`, `drain-temp`), never a direct container restart.
- AppHost: `git pull --rebase` in the main checkout, then `pwsh -NoProfile -File scripts/restart-apphost.ps1`; exit 0 requires `/api/version` SHA equal to source-root HEAD. Check `logs/apphost.restart.lock` and `logs/apphost.launch.lock` first; exit 3 is a refusal to inspect.
- Feature activation check, separate from health: `GET /capabilities` on the desktop runner and the registration capabilities on `GET /api/session-runners` must list `sessionAbsenceEvidenceV1`; one authenticated prepare/certify probe against a throwaway id. Health alone is not activation.
- Live key provisioning for direct HTTP is an operator step under `docs/agent-credentials.md`; no stage prints or rotates a key.

Serialization of the dispatcher edits (S4 edits `server/Application/Services/AgentTaskDispatcher.cs`):

| In-flight work | State observed 2026-10-08 (UTC) | Rule for CARD-1153 |
|---|---|---|
| CARD-1149/1150 S2 Code `7c5280e8` | Dispatched on server2-temp; branch `feat/card-task-7c5280e8` still at `e2c5150`, no commits pushed | S4 starts only after its land. Then re-read `DecideAbsentLaunchAsync` (`:2680`), `ReadAbsentLaunchEvidenceAsync` (`:2735`), `HoldUnderLockAsync` (`:2888`) and the cold-dispatch site (`:5471-5625`) at the new master, and rerun CP-26, CP-29 and CP-31 after the rebase. |
| CARD-1115 S1 Review `4a9cb90f` | Succeeded 00:06 UTC; branch `6a16838` edits `AgentTaskDispatcher.cs` near `:7147` and `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs`; not yet on master | S4 starts only after its land; preserve baseline capture and warnings. |
| CARD-1144 S1 `8652f40b` | Park Reply files | No executable overlap; S5 integrates only the new absence paragraphs into shared owner docs. |

S1, S2 and S3 touch only runner, contracts and infrastructure files and may proceed before
those lands. The orchestrator keeps S4 queued until both are on master.

## Statement budgets

`DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets` (`:25`) measures
every DbCommand through `FullCommandCounter` on a `BridgeQueueHarness` tick (held Dispatched
and young Working, 18 each) and on `SessionReconciliationServiceTests.BuildService` inside the
grace (4). The three arguments stay the decisive witness because:

- The prepare call is HTTP, not SQL, and runs only inside the cold-dispatch branch after the claim commit (`:5589`) and before the sink (`:5625`). Both measured ticks dispatch zero tasks (`tick.Dispatched.ShouldBe(0)`), so the branch is never entered.
- The certificate request runs only on the dead-session due path after the grace and the exact runner-unknown reason; neither measured tick nor the inside-grace scan reaches it.
- The expected-store read reuses the existing `AgentSessions` projection at `:2709` (`StartedAt`) and the `fresh` projection at `:2905` (`Status`, `FailureReason`, `StartedAt`) by adding `RunnerStoreId` to each select; the statement count is unchanged.
- No new scan, sweep, cache warm-up or background retry is added anywhere.

V-24 measures the due path itself (the only path that gains HTTP work) and pins its exact
statement count once Code measures it, with ceilings <=40 without a parent note and <=48
with one. PC-28 adds one SELECT on each of the three measured graphs; the exact pins go red.

## Verification design

### Inspection

Bodies read for this design:

- `AgentTaskDispatcher.FailDeadSessionTasksAsync` tail (`:2600-2650`), `DecideAbsentLaunchAsync` (`:2680-2732`), `ReadAbsentLaunchEvidenceAsync` (`:2735-2787`), `ReadAbsenceAsync` (`:2795-2840`), `TryHoldAbsentLaunchAsync` (`:2857-2886`), `HoldUnderLockAsync` (`:2888-2960`), cold dispatch from `new AgentSession` (`:5471`) through commit (`:5589`), `FinishDispatchedLaunchSpecAsync` and the sink enqueue (`:5618-5640`).
- `AbsentLaunchPolicy.IsNeverAttempted` and `MessageColumns` (`AbsentLaunchPolicy.cs:15-70`).
- `SessionRunnerRuntime.StartAsync` (`:309-336`), `StartCoreAsync` (`:360-540`, `_sessions.TryAdd` at `:450`, failed-launch removal at `:521`), `AttachHerdrAsync`/`AttachHerdrCoreAsync` (`:554-625`), `GetTranscript`/`GetSession` (`:880`, `:2130`), `ReleaseSlot` tail (`:1505-1530`), `ForgetDurableSession` (`:1550`), `AdoptOrphanedHostsAsync` (`:1675-1800`), `RunnerSession.StartAsync` first effects (`:2839-2905`).
- `Program.cs` 404 mapping (`:152`), readiness gating (`:193-210`), route order (`:221-232`), transcript route (`:312`), capabilities route (`:447-464`).
- `SessionRunnerHttpClient.ReadRequiredJsonAsync` (`:140-150`), `GetTranscriptAsync` (`:595`); `PhoneHomeRunnerClient.GetTranscriptAsync` (`:207`); `PhoneHomeCommandDispatcher` operation switch (`:218-250`), `LaunchAsync` admission order (`:453-520`), `MutateAsync` (`:741`); `PhoneHomeLiveConnection.RequestAsync` (`:323-385`), `RequestTimeoutFor` (`:310`); `RoutingSessionRunnerClient.Route` (`:95-105`).
- `SessionGeneration` (whole), `PhoneHomeLaunchGenerationStore` (`:1-80`), `TranscriptSidecar.TryLoad`/`LoadAll` (`:64-100`), `SessionRunnerEndpoints` registration auth (`:42-52`), `PhoneHomeConnectionService.RunConnectionAsync` secret read (`:97-100`), `RunnerCapabilityFeatures` (`SessionRunnerContracts.cs:141`) and `RunnerCapabilitiesDto` (`:972`), `PhoneHomeOperation` enum (`PhoneHomeContracts.cs:32-72`), `IPhoneHomeAdoptionGate`.
- Fixtures: `DelegationDispatchRecoveryBoundaryTests` partials (`.cs`, `.AbsentLaunch.cs` V-1 body, `OpenSweep`, `CountingRunner`, `SweepHost`, `Quiet`, `SeedAsync`; `.AbsentLaunchWhitelist.cs`; `.AbsentLaunchRepair.cs`), `AbsentLaunchPolicyTests` (76 flip arguments plus the column census), `TerminalSeatReleaseTests.SeatWire`/`SeatWorld` (random-port Kestrel over the production route extension and a real runtime without a process), `SessionRunnerGenerationWireTests` (`StubHandler`, `Client`, `Capabilities`), `PhoneHomeTestHost` and `CodexCliDeliveryWorld` (real `PhoneHomeCommandDispatcher` as the scripted peer), `PhoneHomeConnectionTests`, `PtyHostAdoptionTests`, `LinuxPhoneHomeRunnerTests` (SkipTestException precedent), `RepairSourceDocumentationTests.FindRepoRoot`, `TestClassificationMetadata` (only `Slow` classes need registry entries).

Boundaries and where they are proved:

| Boundary | Where |
|---|---|
| A fresh id gets one durable Prepared record and nothing else | V-1 |
| Any history, artifact or record other than a same-epoch Prepared refuses prepare | V-2 |
| No proof crosses a runner epoch or a store change | V-3 |
| Unknown, corrupt, denied or lost state is never absence | V-4 |
| Attempted is durable before the first provider effect on every creation path | V-5 |
| A marker write failure disables evidence, not launch | V-6 |
| Certify and creation are serialized on the launch gate | V-7 |
| ClosedUnused refuses every later creation, any generation, any entry point | V-8 |
| The certificate is a separate authenticated route; ordinary GETs stay 404 | V-9, V-10 |
| Phone-home binding, operation framing and authenticated admission | V-11, R-10 |
| Every non-certificate wire shape is refused by the shared validator | V-12, V-22 |
| Freshness, cancellation and single attempt | V-13 |
| Real-client certificate holds the original input, both transports | V-14 |
| Every bad-evidence shape keeps Failed with known absent inventory | V-15 |
| The final decision revalidates under the lock | V-16 |
| Working or unknown inventory withholds before any evidence request | V-17 |
| Only a fresh cold dispatch prepares, after commit, before sink, never a launch gate | V-18 |
| DB pre-screen, provisional-fact boundary and nativeAttempt exclusion | V-19 |
| Hold rollback leaks nothing to a later save | V-20 |
| Owner documents state the protocol | V-21 |
| Certify-side acceptance is a closed whitelist | V-23 |
| Statement budgets and the due-path ceiling | V-24, R-8 |
| Inherited S1 behaviour, boot-stall characterization, brief recovery, generation wire, launch generation, compaction stop, Windows generation, Linux launch admission | R-1..R-9, R-11 |

Missing setup recorded for Code:

- The runtime's launcher is a concrete `PtyHostLauncher` (`SessionRunnerRuntime.cs:47`). V-5/V-6/V-7/V-8 need an injectable creation seam that records the order of the Attempted write against the first effect without a process; `RunnerSession.BindChildForTest` (`:3475-3478`) and `SeatWorld` show the shape. The seam is test-only and must not bypass the gate.
- V-23 needs the inspection seam of A-10.
- V-14 hosts the production `AbsenceEvidenceRoutes`, `MapSessionGetRoute` and the transcript route on a random-port Kestrel inside `tests/Antiphon.Tests` (the project references `Antiphon.SessionRunner`), with the real evidence service over a temp root and a synthetic key. The phone-home case uses `PhoneHomeTestHost` with a real `PhoneHomeCommandDispatcher` over a `PhoneHomeRuntimeAdapter` as the scripted peer.
- `RunnerSessionGenerationTests` has no platform skip and uses `cmd.exe`; CP-35 and CP-36 are Windows-only rows selected with `--rows`.
- New classes are `Unit` or `Integration`; none is `Slow`, so `slow-tests-allowlist.txt` is unchanged.

Excluded: a database migration, a new per-tick query, parking enablement, any automatic
stop of a Working session, rewriting the boot-stall tail, and changing the 76-argument pure
whitelist or the six unknown-native cases.

### Delivery inventory

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (durable identity) |
|---|---|---|---|---|---|
| Prepare | Cold dispatch after the claim commit, before the sink | Runner evidence store `absence-evidence/<id:N>.json` | Claim and Starting session committed; record written atomically on the runner | None by design: a lost prepare leaves no proof and today's Failed outcome | Record state `Prepared` with session id, generation, store, epoch read back through certify (V-1, V-18); never a request count |
| Certify | Due-failure path after grace and exact reason | Runner evidence service, same record | `Prepared` to `ClosedUnused` written before the reply | A second request re-checks exclusions and re-issues (V-8) | Validated certificate object plus the record state on disk (V-9, V-14); a 200 or a frame alone proves nothing |
| Launch after prepare | `_taskLaunchSink`/`_launchQueue` enqueue | Runner `StartAsync` | Attempted written under the gate before the first effect | Existing interrupted-launch recovery; a closed id is refused, never re-minted | Attempted record plus provider seam order (V-5); refusal type on a closed id (V-8) |
| Hold publication | `HoldUnderLockAsync` | Task row, Blocked event, parent-note queue row | One transaction | `DiscardUncommittedHoldAsync` on failure (V-20) | Persisted rows after repeated due ticks (V-14); `C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero` |
| Caller note | `EnqueueBlockedParentNoteAsync` | Parent session via the real queue flush | Queue row committed with the hold | Existing queue delivery and verification | One complete UserPrompt in the parent transcript: busy, eligible and crash modes already proved by `C1149_Caller_note_has_one_complete_user_prompt` (R-1); V-14 asserts the queued note's identity and reason, and does not claim receipt |
| Change notice | `PublishToAllAsync("AgentTaskChanged")` | Event bus | After the commit | Logged only | Not delivery evidence; excluded |

Substitutes and what they cannot prove: a prepare acknowledgement proves a record was
written, not that the runner will refuse creation (V-8 proves that); a certify 200 proves
nothing until the validator accepts it (V-12); a queued note is not a received UserPrompt
(R-1 D3 proves receipt); a fake runner client is not a transport (V-14 uses the production
HTTP and phone-home clients). No design stops before recipient evidence: V-14 persists the
hold and R-1 D3 reads the parent transcript.

### Proves it works now

Nothing of CARD-1153 exists at the baseline; every V-n is a Code obligation and every
skeleton is skipped until its slice lands. Diagnostic rows run at `999de4d8b` plus the
skeleton commit are recorded under "Diagnostic runs" below; they are not checkpoint executions.

- V-1: a fresh id becomes `Prepared`, no runtime session, provider call or transcript exists, an identical prepare returns the same record | runner service over a temp root | `RunnerAbsenceEvidenceTests.C1153_Prepare_records_only_a_fresh_identity` | 1 executed; record state, identity and idempotence asserted; F1: the first write also creates the store identity (closure log, header, anchor), so the file set is the record plus those three and four writes
- V-2: every existing-evidence shape refuses prepare | runner service | `RunnerAbsenceEvidenceTests.C1153_Prepare_refuses_existing_evidence` | 9: live, exited, attempted-record, closed-unused-record, transcript-sidecar, herdr-sidecar, manifest, custody-or-watermark, malformed-sidecar; each refuses with no overwrite
- V-3: no proof across epochs or stores | runner service, two instances over one root | `RunnerAbsenceEvidenceTests.C1153_Restart_or_store_change_never_renews_proof` | 3: prepared-prior-epoch, closed-prior-epoch, changed-store; no certificate, no overwrite on re-prepare
- V-4: unknown state is not absence | runner store | `RunnerAbsenceEvidenceTests.C1153_Unknown_or_unreadable_state_is_not_absence` | 5: unprepared, corrupt-record, unknown-schema, denied-read, lost-root; never a certificate, errors distinguishable from absence
- V-23: certify-side whitelist, one flip per case | runner service with the A-10 seam | `RunnerAbsenceEvidenceTests.C1153_Certify_requires_every_fact` | 28: pristine admits, then no-record, prepared-prior-epoch, closed-prior-epoch, attempted, wrong-generation, wrong-store, adoption-incomplete, storage-fault-latched, runtime-entry-live, runtime-entry-exited, manifest-present, transcript-sidecar-present, herdr-sidecar-present, custody-reservation-present, watermark-present, nonce-missing, nonce-short, version-0, version-2, empty-session-id, corrupt-record, store-header-changed (F1), replayed-nonce, stale-issued-at, future-issued-at, issued-in-epoch-window, issued-at-missing (F2) refuse; closed-same-epoch-reissue admits a fresh certificate
- V-5: Attempted precedes the first effect | runtime with the creation seam | `RunnerAbsenceEvidenceRuntimeTests.C1153_Creation_consumes_proof_before_effects` | 3: start, attach, adoption; seam observes Attempted before its first call, including a throw followed by runtime removal
- V-6: storage fault latches evidence, not launch | runtime | `RunnerAbsenceEvidenceRuntimeTests.C1153_Store_failure_disables_proof_without_stopping_work` | 1: failed Attempted write latches unknown before the seam runs; a later certify refuses; a healthy legacy launch still completes
- V-7: certify and creation serialize on the gate | runtime, barriers | `RunnerAbsenceEvidenceRuntimeTests.C1153_Certificate_and_launch_race_is_serialized` | 2: launch-first yields no certificate; certify-first yields no launch; never both successes
- V-8: closed identity refuses delayed creation | runtime | `RunnerAbsenceEvidenceRuntimeTests.C1153_Closed_identity_refuses_delayed_creation` | 4: same-generation-start, newer-generation-start, attach, custody-bound-start; typed refusal, zero seam calls; a repeat certificate with a fresh nonce still succeeds
- V-9: the certificate is a separate route; GETs stay 404 | random-port Kestrel over production routes | `RunnerAbsenceEvidenceContractTests.C1153_Real_unknown_transcript_has_a_separate_certificate` | 1: prepare then certify returns the full shape with `Cache-Control: no-store`; `GET /sessions/{id}` and `/transcript` stay 404; an arbitrary id stays 404 without a certificate
- V-10: HTTP authentication covers both directions | same host, synthetic key | `RunnerAbsenceEvidenceContractTests.C1153_Http_authentication_covers_request_and_response` | 4: missing-key, wrong-request-MAC, changed-request-field, altered-response-bit; no state change, no accepted certificate; canary: no key bytes or path in responses or captured logs
- V-11: phone-home operations preserve binding | real `PhoneHomeCommandDispatcher` over a runtime adapter | `RunnerAbsenceEvidencePhoneHomeTests.C1153_Authenticated_operation_preserves_binding` | 3: prepare-and-certify framing round-trips both directions; unsupported-operation and foreign-store answer typed errors without entering the runtime; transcript-of-prepared-id stays a typed 404 frame
- V-12: non-certificate wire shapes are refused by the production HTTP client | `SessionRunnerHttpClient` over `StubHandler` | `SessionRunnerAbsenceEvidenceClientTests.C1153_Rejects_noncertificate_wire_shapes` | 14: 404, 501, empty-200, malformed, ordinary-transcript, missing-presence-field, incomplete, wrong-ID, wrong-generation, wrong-store, wrong-nonce, unknown-version, unsigned, bad-MAC; each yields the unsupported/unknown result, never proof
- V-13: freshness and cancellation are bounded | same client, FakeTimeProvider | `SessionRunnerAbsenceEvidenceClientTests.C1153_Freshness_and_cancellation_are_bounded` | 4: within-5s-valid, over-5s-deadline, expired-validated-result, caller-cancellation; exactly one transport attempt per read
- V-22: validator whitelist, one flip per case | pure `RunnerAbsenceEvidenceValidator` | `RunnerAbsenceEvidenceValidatorTests.C1153_Validator_requires_every_fact` | 35: pristine admits, then each of version-missing, version-0, version-2, outcome-missing, outcome-other, sessionId-missing, sessionId-mismatch, sessionId-empty, generation-missing, generation-one-microsecond, store-missing, store-mismatch, store-empty, epoch-missing, epoch-empty, nonce-missing, nonce-mismatch, complete-missing, complete-false, creationObserved-missing, creationObserved-true, native-missing, native-true, sidecar-missing, sidecar-true, process-missing, process-true, identityClosed-missing, identityClosed-false, unknown-extra-member, elapsed-over-5s, age-expired, unauthenticated, bad-MAC refuses; generation-sub-microsecond admits
- V-14: real-client certificate holds the original input | Postgres fixture, production clients, in-process production routes | `DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_certificate_holds_original_input` | 2: local-http, remote-phone-home; refused enqueue after a successful prepare, real transcript 404, due sweeps; Blocked, `CompletedAt` null, one Blocked event, byte-identical brief and spill, queued parent note with stable reason, zero Start/Kill/Release/Retry, one certify request observed
- V-15: bad evidence keeps Failed | same fixture, known absent inventory | `DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_bad_evidence_keeps_failure` | 8: plain404, old-runner, evidence-unreachable, timeout, stale-nonce, mismatched-generation, incomplete, empty-transcript; Failed, no Blocked event, bytes retained
- V-16: final certificate revalidated under the lock | same fixture | `DelegationDispatchRecoveryBoundaryTests.C1153_Final_certificate_is_revalidated_under_lock` | 3: changed-generation, store-or-epoch-change, expired-proof between first read and `StageBlocked`; no Blocked commit; existing withhold preserved
- V-17: Working or unknown inventory withholds | same fixture | `DelegationDispatchRecoveryBoundaryTests.C1153_Working_or_unknown_inventory_withholds` | 3: working-task, working-transcript, unavailable-owning-inventory with a misleading empty local list; zero certificate requests, no fail/hold/kill/start
- V-18: only a new cold dispatch prepares | same fixture, in-process routes | `DelegationDispatchRecoveryBoundaryTests.C1153_Only_new_cold_dispatch_prepares_evidence` | 6: cold-fresh prepares after commit and before the sink; warm-reuse, recovery, resume send nothing; prepare-faulted still launches; remote-old-runner sends no frame
- V-19: nonpristine brief never closes its identity | same fixture | `DelegationDispatchRecoveryBoundaryTests.C1153_Nonpristine_brief_never_closes_identity` | 3: attempted-delivery, extra-related-row, native-attempt; zero certify requests, previous whitelist outcome
- V-20: certificate pass then hold save failure leaks nothing | same fixture, `BlockedSaveFault` | `DelegationDispatchRecoveryBoundaryTests.C1153_Certificate_failure_does_not_leak_a_hold_on_later_save` | 1: later unrelated save persists no task/event/note mutation; the next due tick recovers
- V-24: due-path statement ceiling | same fixture, `FullCommandCounter` | `DelegationDispatchRecoveryBoundaryTests.C1153_Due_hold_statement_ceiling` | 2: no-parent exact pin <=40, parent exact pin <=48; roster printed
- V-25 (Review 1a174347 F1): a certified closure survives storage failure | runner service and runtime over a temp root | `RunnerAbsenceEvidenceTests.C1153_Closure_survives_storage_failure` | 12: corrupt-record, truncated-record, zero-length-record, deleted-record, directory-in-place, denied-read, read-error, store-wiped, store-replaced, lost-root, reverted-record, anchor-lost each refuse the fence and the attempt marker (no write) and never certify again; pristine control admits a marker. `RunnerAbsenceEvidenceRuntimeTests.C1153_Certified_identity_survives_store_damage` | 12: certificate issued, then corrupt/truncated/zero-length/deleted/directory/permission-denied/store-wiped/lost-root (start or attach, four across a restart) refuse with the closed-identity type before any provider effect, registration, manifest or sidecar, and never certify
- V-26 (Review 1a174347 F2): replayed requests are rejected | random-port Kestrel over production routes | `RunnerAbsenceEvidenceContractTests.C1153_Replayed_request_is_rejected` | 5: prepare-twice, certify-twice, cross-route (same nonce re-signed for certify) 409 replayed; after-skew-window, across-restart 401 stale; authenticated refusal, no state change, a fresh nonce still admits
- V-28 (Review a086fe80 F1): a certified closure survives power loss | runner service over the in-memory `PowerLossEvidenceFiles` volume (a name is durable only after its directory sync; a crash keeps durable names plus none, all or only the latest pending name) | `RunnerAbsenceEvidenceTests.C1153_Certified_closure_survives_power_loss` | 11: crash immediately after root-created, closure-log-created, header-written, anchor-written, prepared-record-written, closure-appended, closed-record-written, and after certificate-returned, session-log-path-created (nested missing ancestors), adopted-unsynced-store (names an earlier process never synced), each under the three writeback outcomes: the store stays usable, a returned certificate or logged closure refuses fence, marker and certify, an id with neither stays open, and the recovered store's next certificate survives a second power loss; sync-fails: a failing directory sync refuses the certificate and latches
- V-29 (Review a086fe80 F2): replay admission is monotone | runner service, FakeTimeProvider monotonic clock plus a stepping runner wall clock (`SteppingWallClock`) | `RunnerAbsenceEvidenceTests.C1153_Replay_admission_is_monotone` | 5: expiry-then-rollback (the Review's 30.5 s then -1 s: 503 clock step, admission resumes after one window), rollback-then-identical (70 s back, after the step window: 401 below the high-water mark), flood-then-replay-at-cap (4096 live nonces: replay 409, new 503, later request retires, replay after a rollback 401), forward-jump (+1 h stale, the jump back 503, resumes after one window, replay stays refused), small-step-under-skew (runner 20 s ahead, a 0.45 s step: the retained nonce answers 409)
- V-30 (Review a086fe80 F3): closure record and log must agree | runner service over a temp root | `RunnerAbsenceEvidenceTests.C1153_Closure_record_and_log_must_agree` | 5: record-without-log-entry (empty log), last-entry-truncated, record-differs-from-logged-closure, duplicate-entry, reordered-entries: the read is unknown, certify refuses first (no prior latch), then after a restart fence and marker refuse with no write
- V-31 (round 2 guard lines): `RunnerAbsenceEvidenceTests.C1153_Store_guard_lines_fail_closed` | 2: attempt-store-turns-unknown (store healthy at admission, unknown at the marker write: closed-identity refusal, latch, no marker), closure-log-length-malformed (partial line: store unknown, no certificate, creation refused)
- V-27 (Review 1a174347 F3): the five-second deadline covers capability discovery | `SessionRunnerHttpClient` over `StubHandler`, FakeTimeProvider | `SessionRunnerAbsenceEvidenceClientTests.C1153_Deadline_covers_capability_discovery` | 4: certify and prepare x pending-discovery (no POST) and discovery-plus-post (3 s + 2.1 s) end at the deadline as Unknown / not prepared; pristine 2 s + 2.9 s control per method admits
- V-21: owner sentences | file read | `SessionRunnerAbsenceEvidenceDocumentationTests.C1153_Owner_sentences_match_the_protocol` | 1: the five plan sentences verbatim in `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/agent-credentials.md`; the runner route map lists both POST routes without `/api`

### Guards the regression

- R-1: all `C1149_*` methods in `DelegationDispatchRecoveryBoundaryTests` (49 executions at this baseline: 1+1+5+3+3, 6+19, 4+1+3, 3). CP-26.
- R-2: `AbsentLaunchPolicyTests` (77). CP-27.
- R-3: `SessionRunnerGenerationWireTests` (9). CP-28.
- R-4: `PhoneHomeCommandDispatcherTests` `Duplicate_launch*` (3) and `Pre_ack_resend*` plus `Launch_of_an_exited_session_under_a_new_generation_still_relaunches` (3). CP-32, CP-33.
- R-5: `CompactionContinuationStopTests` (6, Windows lane: it launches `cmd.exe`). CP-36.
- R-6: `BootStallWorkingTickCharacterizationTests` (3, CARD-1151 boundary unchanged). CP-29.
- R-7: `DelegationBriefRecoveryTests` (3). CP-30.
- R-8: `C1149_C1150_Statement_budgets` (3 at this baseline; more if S2 lands arguments). CP-31.
- R-9: Windows `RunnerSessionGenerationTests` (5). CP-35.
- R-10: `PhoneHomeConnectionTests.Authentication_is_required_at_both_endpoints` (1): the unauthenticated transport cannot reach any handler. CP-16.
- R-11: `UnixPtyArgvAdmissionTests` (12): both launch routes and the phone-home launch after S2's `StartAsync` edits. CP-9.

### Guard inventory

- G-1: a fresh id writes exactly one durable Prepared record and creates nothing else | PC-1
- G-2: a repeated prepare returns the original record only for the identical identity in the same epoch | PC-2
- G-3: prepare refuses a live or exited runtime entry | PC-3
- G-4: prepare refuses an Attempted or ClosedUnused record | PC-4
- G-5: prepare refuses manifest, transcript sidecar, herdr sidecar, custody and watermark artifacts, including malformed ones | PC-5
- G-6: epoch equality for certify and for re-prepare refusal | PC-6
- G-7: store equality on the record | PC-7
- G-8: the strict reader classifies corrupt, unknown-schema, denied, lost-root and unprepared as unknown | PC-8
- G-9: Attempted is written under the gate before the first effect on start, attach and adoption | PC-9
- G-10: a failed Attempted write latches evidence unavailable without stopping the launch | PC-10
- G-11: certify and creation acquire the same per-session launch gate | PC-11
- G-12: ClosedUnused refuses start (same and newer generation), attach and custody-bound start | PC-12
- G-13: the certify route never resolves through `GetSession`; unprepared ids and ordinary GETs stay 404 | PC-13
- G-14: HTTP request MAC, key presence and route/body/signed id agreement | PC-14
- G-15: HTTP response MAC covers every evidence member and the request nonce; `no-store` | PC-15
- G-16: phone-home forwarding, store binding and typed refusal of unsupported operations | PC-16
- G-17: the HTTP client routes every wire shape through the shared validator | PC-17
- G-18: the validator admits only the complete positive member set | PC-18
- G-19: 5 s deadline, 5 s age, caller cancellation, single attempt | PC-19
- G-20: the dispatcher's native-empty fact comes only from the validated certificate | PC-20
- G-21: every non-certificate outcome keeps `FailAndNotifyAsync` | PC-21
- G-22: the final read under the lock revalidates generation, store/epoch and age | PC-22
- G-23: Working task, Working transcript and owning-inventory withholds precede any evidence request | PC-23
- G-24: prepare only on fresh cold dispatch, after commit, before the sink, never a launch gate, never to a runner without the capability | PC-24
- G-25: DB pre-screen before certify, provisional fact confined, nativeAttempt exclusion | PC-25
- G-26: `DiscardUncommittedHoldAsync` on rollback | PC-26
- G-27: certify-side whitelist including adoption-complete, fault latch, watermark and nonce/version rules | PC-27
- G-28: 18/18/4 and the due-path exact pins | PC-28
- G-29: owner documents carry the five sentences | PC-29
- G-30: the hold's caller note is one complete UserPrompt (inherited) | inherited CARD-1149 PC-D3; re-run as R-1, not re-mutated here

- G-31: names durable before a certificate (directory syncs, anchor last, adopted-store sync) | PC-30
- G-32: monotone replay admission (high-water mark, mark-based retirement, step check, cap) | PC-31
- G-33: closure record and log agree (unlogged record, hash, chain) | PC-32
- G-34: marker store-turns-unknown catch; closure-log length check | PC-33

Guards = 34, mapped = 33 to distinct PCs, inherited = 1 with its own prior PC, missing = 0 (round 2 added G-31..G-34),
duplicate PC maps = 0.

### Positive controls

Each control is a compiling production defect, run method-scoped after land on the
SourceLanding SHA with `/*/*/<Class>/C1153_<Method>*`, baseline green, red at the named
assertion, restore, green. Zero tests, build errors, fixture failures or a different
assertion are not red. Variants in one method or file run as separate cycles; only variants
in different files and methods may batch. The SourceLanding snapshot is single; no sharding.

- PC-1 (2): in `RunnerAbsenceEvidenceStore.Prepare` skip the durable write; separately let prepare register a runtime entry or call the creation seam. `C1153_Prepare_records_only_a_fresh_identity` red at the missing record or the nonzero seam count.
- PC-2 (1): return a new record on a duplicate prepare. Same method red at the identity/idempotence compare.
- PC-3 (2): bypass the live-entry exclusion; separately the exited-entry exclusion. `C1153_Prepare_refuses_existing_evidence` red on `live` or `exited`.
- PC-4 (2): overwrite an Attempted record; separately a ClosedUnused record. Same method red on `attempted-record` or `closed-unused-record`.
- PC-5 (6): bypass one artifact exclusion at a time: manifest, transcript sidecar, herdr sidecar, custody/watermark, and treat a malformed sidecar as absent. Same method red on that argument.
- PC-6 (2): drop epoch equality in certify; separately allow re-prepare over a prior-epoch record. `C1153_Restart_or_store_change_never_renews_proof` red on `prepared-prior-epoch`/`closed-prior-epoch`.
- PC-7 (1): drop store equality. Same method red on `changed-store`.
- PC-8 (5): in the strict reader convert each branch to Prepared: unprepared, corrupt, unknown schema, denied read, lost root. `C1153_Unknown_or_unreadable_state_is_not_absence` red on that argument.
- PC-9 (3): move the Attempted write after `LaunchDetachedAsync`; after `ConnectAndValidateAsync`; after the adoption `TryAdd`. `C1153_Creation_consumes_proof_before_effects` red at the seam order on `start`, `attach`, `adoption`.
- PC-10 (2): remove the fault latch; separately keep the in-memory Prepared record usable after a failed write. `C1153_Store_failure_disables_proof_without_stopping_work` red at the later certificate.
- PC-11 (2): certify outside the launch gate; separately write Attempted after `_sessions.TryAdd`. `C1153_Certificate_and_launch_race_is_serialized` red at two contradictory successes.
- PC-12 (4): bypass the fence in `StartCoreAsync` for the same generation; make it generation-specific so a newer generation passes; bypass it in `AttachHerdrCoreAsync`; place it after `_custody.PrepareStart`. `C1153_Closed_identity_refuses_delayed_creation` red at a seam call on that argument.
- PC-13 (3): resolve `GetSession` first in the certify route; return a certificate for an arbitrary id; return an empty 200 transcript for a prepared id. `C1153_Real_unknown_transcript_has_a_separate_certificate` red at 404-instead-of-certificate, the arbitrary-id assertion, or the transcript 404 assertion.
- PC-14 (3): skip request MAC verification; accept with no key configured; accept a body id that differs from the route id. `C1153_Http_authentication_covers_request_and_response` red on `wrong-request-MAC`, `missing-key`, `changed-request-field`.
- PC-15 (2): exclude one evidence member from the response MAC input; drop the `no-store` header. Same method red on `altered-response-bit` or the header assertion.
- PC-16 (3): forward `PrepareAbsenceEvidence` to the certify handler; drop the store binding check; let an unsupported operation reach the runtime. `C1153_Authenticated_operation_preserves_binding` red on its argument.
- PC-17 (14): in the HTTP transport accept each wire shape as proof, one per argument. `C1153_Rejects_noncertificate_wire_shapes` red on that argument.
- PC-18 (34): in `RunnerAbsenceEvidenceValidator` drop one predicate per refusal condition (for null cases explicitly admit null). `C1153_Validator_requires_every_fact` red on that argument.
- PC-19 (4): remove the 5 s deadline; the age expiry; swallow caller cancellation; add a second transport attempt. `C1153_Freshness_and_cancellation_are_bounded` red on `over-5s-deadline`, `expired-validated-result`, `caller-cancellation`, or the attempt count.
- PC-20 (2): return unknown from the evidence reader; fabricate proof without calling the client. `C1153_Real_client_certificate_holds_original_input` red at Blocked-versus-Failed or at the observed certify request count.
- PC-21 (8): treat one non-certificate outcome as native-empty, one per argument. `C1153_Real_client_bad_evidence_keeps_failure` red on that argument.
- PC-22 (3): reuse the first certificate; skip the age recheck; skip the store/epoch compare. `C1153_Final_certificate_is_revalidated_under_lock` red on its argument.
- PC-23 (3): remove the Working-task guard; the Working-transcript guard; judge a remote owner from the local list. `C1153_Working_or_unknown_inventory_withholds` red at a nonzero certificate request or mutation.
- PC-24 (6): prepare after the enqueue; on warm reuse; on recovery; on resume; make a failed prepare throw out of dispatch; send a prepare to a runner without the capability. `C1153_Only_new_cold_dispatch_prepares_evidence` red on its argument.
- PC-25 (3): skip the DB pre-screen; let the provisional native-empty fact reach the final decision; drop the nativeAttempt exclusion. `C1153_Nonpristine_brief_never_closes_identity` red on its argument.
- PC-26 (1): remove `DiscardUncommittedHoldAsync`. `C1153_Certificate_failure_does_not_leak_a_hold_on_later_save` red at the leaked row.
- PC-27 (21): in the certify service drop one exclusion per refusal condition. `C1153_Certify_requires_every_fact` red on that argument.
- PC-28 (3): add one SELECT on the held tick; on the inside-grace scan; on the due path. `C1149_C1150_Statement_budgets` red at 19 != 18 or 5 != 4; `C1153_Due_hold_statement_ceiling` red at the exact pin.
- PC-29 (5): change one pinned sentence in its owning document, one per sentence. `C1153_Owner_sentences_match_the_protocol` red; documentation control.
- PC-30 (6): in `RunnerAbsenceEvidenceStore` drop the adopted-store sync (`EnsureNamespaceDurable` body); the anchor-directory sync; the created-ancestor parent syncs; the root sync after the header; the root sync after a record write; write the anchor before the header. `C1153_Certified_closure_survives_power_loss` red on adopted-unsynced-store; 9 cases; session-log-path-created and anchor-written; anchor-written and prepared-record-written; all 11; anchor-written and header-written. Equivalent, excluded: the root sync after the closure log (the root sync after the header covers it before anything depends on it).
- PC-31 (4): in `AdmitOnceLocked` disable the backward-step check; drop the high-water refusal; retire nonces by wall clock (`issued + window < now`); drop the 4096 cap. `C1153_Replay_admission_is_monotone` red on expiry-then-rollback and forward-jump; rollback-then-identical and flood-then-replay-at-cap; small-step-under-skew; flood-then-replay-at-cap.
- PC-32 (3): in `RunnerAbsenceEvidenceStore.Read` drop the unlogged-ClosedUnused check; drop the record-hash compare; in `ParseClosures` drop the chain compare. `C1153_Closure_record_and_log_must_agree` red on record-without-log-entry and last-entry-truncated; record-differs-from-logged-closure; reordered-entries. Missing-control candidate: `closed.TryAdd` (a raw duplicate line already breaks the chain; only a correctly re-chained duplicate would reach it).
- PC-33 (2): remove the marker's `RunnerAbsenceStoreUnknownException` catch; remove the closure-log length check. `C1153_Store_guard_lines_fail_closed` red on attempt-store-turns-unknown; closure-log-length-malformed.

Variant total: 165 (150 plus round 2's PC-30..PC-33: 15). Excluded from mutation with reason: constant-time MAC comparison (no
assertion can observe timing; Review reads the compare call).

### Out of scope

- V-1..V-24 bodies. Code writes them; the committed skeletons are skipped, never green.
- Rewriting the boot-stall tail (CARD-1151), parking enablement, any release timer or Working stop.
- A database migration, overloading JSON columns, a general retry ledger, pruning of evidence records.
- Whole-assembly or whole-Unit runs. No production runner, provider or live broker. No live key.
- Runner filesystem tampering (H-24) and timing side channels.

### Checkpoints

Group prefix names the lane: `portable-` or `windows-`. Builds add `UseAppHost=false` off
Windows; Postgres classes run serial with `TUNIT_MAX_PARALLEL_TESTS=1`. One build per After
group per project; CP-35 and CP-36 are selected alone with `--rows` on a Windows host.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-s1/` | portable-prepare | `/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity*` | V-1 | 1 executed, 0 failed/skipped | 1 | 5 | true | n/a |
| CP-2 | S1 | `CP-1` | portable-known-history | `/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence*` | V-2 | 9 executed, 0 failed/skipped | 9 | 1 | true | n/a |
| CP-3 | S1 | `CP-1` | portable-epoch | `/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof*` | V-3 | 3 executed, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-4 | S1 | `CP-1` | portable-store-unknown | `/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence*` | V-4 | 5 executed, 0 failed/skipped | 5 | 1 | true | n/a |
| CP-5 | S1 | `CP-1` | portable-certify-table | `/*/*/RunnerAbsenceEvidenceTests/C1153_Certify_requires_every_fact*` | V-23 | 28 executed, 0 failed/skipped | 28 | 1 | true | n/a |
| CP-37 | S1 | `CP-1` | portable-closure-durable | `/*/*/RunnerAbsenceEvidenceTests/C1153_Closure_survives_storage_failure*` | V-25 | 12 executed, 0 failed/skipped | 12 | 1 | true | n/a |
| CP-6 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-s2/` | portable-write-ahead | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Creation_consumes_proof_before_effects*` | V-5 | 3 executed, 0 failed/skipped | 3 | 5 | true | n/a |
| CP-7 | S2 | `CP-6` | portable-storage-fault | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Store_failure_disables_proof_without_stopping_work*` | V-6 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-8 | S2 | `CP-6` | portable-launch-race | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certificate_and_launch_race_is_serialized*` | V-7 | 2 executed, 0 failed/skipped | 2 | 1 | true | n/a |
| CP-9 | S2 | `CP-6` | portable-closed-id | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Closed_identity_refuses_delayed_creation*` | V-8 | 4 executed, 0 failed/skipped | 4 | 1 | true | n/a |
| CP-10 | S2 | `CP-6` | portable-unix-admission | `/*/*/UnixPtyArgvAdmissionTests/*` | R-11 | 12 executed, 0 failed/skipped | 12 | 2 | true | n/a |
| CP-38 | S2 | `CP-6` | portable-closure-runtime | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certified_identity_survives_store_damage*` | V-25 | 12 executed, 0 failed/skipped | 12 | 1 | true | n/a |
| CP-11 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-s3-runner/` | portable-real-route | `/*/*/RunnerAbsenceEvidenceContractTests/C1153_Real_unknown_transcript_has_a_separate_certificate*` | V-9 | 1 executed, 0 failed/skipped | 1 | 5 | true | n/a |
| CP-12 | S3 | `CP-11` | portable-auth | `/*/*/RunnerAbsenceEvidenceContractTests/C1153_Http_authentication_covers_request_and_response*` | V-10 | 4 executed, 0 failed/skipped | 4 | 1 | true | n/a |
| CP-13 | S3 | `CP-11` | portable-phone-home | `/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding*` | V-11 | 3 executed, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-39 | S3 | `CP-11` | portable-replay | `/*/*/RunnerAbsenceEvidenceContractTests/C1153_Replayed_request_is_rejected*` | V-26 | 5 executed, 0 failed/skipped | 5 | 1 | true | n/a |
| CP-14 | S3 | `tests/Antiphon.Tests -> bin-c1153-s3-client/` | portable-client-shapes | `/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Rejects_noncertificate_wire_shapes*` | V-12 | 14 executed, 0 failed/skipped | 14 | 5 | true | n/a |
| CP-15 | S3 | `CP-14` | portable-client-time | `/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Freshness_and_cancellation_are_bounded*` | V-13 | 4 executed, 0 failed/skipped | 4 | 1 | true | n/a |
| CP-16 | S3 | `CP-14` | portable-validator-table | `/*/*/RunnerAbsenceEvidenceValidatorTests/C1153_Validator_requires_every_fact*` | V-22 | 35 executed, 0 failed/skipped | 35 | 1 | true | n/a |
| CP-17 | S3 | `CP-14` | portable-phone-home-auth | `/*/*/PhoneHomeConnectionTests/Authentication_is_required_at_both_endpoints*` | R-10 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-40 | S3 | `CP-14` | portable-client-deadline | `/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Deadline_covers_capability_discovery*` | V-27 | 4 executed, 0 failed/skipped | 4 | 1 | true | n/a |
| CP-41 | S1 | `CP-1` | portable-power-loss | `/*/*/RunnerAbsenceEvidenceTests/C1153_Certified_closure_survives_power_loss*` | V-28 | 11 executed, 0 failed/skipped | 11 | 1 | true | n/a |
| CP-42 | S1 | `CP-1` | portable-monotone-replay | `/*/*/RunnerAbsenceEvidenceTests/C1153_Replay_admission_is_monotone*` | V-29 | 5 executed, 0 failed/skipped | 5 | 1 | true | n/a |
| CP-43 | S1 | `CP-1` | portable-closure-agreement | `/*/*/RunnerAbsenceEvidenceTests/C1153_Closure_record_and_log_must_agree*` | V-30 | 5 executed, 0 failed/skipped | 5 | 1 | true | n/a |
| CP-44 | S1 | `CP-1` | portable-store-guards | `/*/*/RunnerAbsenceEvidenceTests/C1153_Store_guard_lines_fail_closed*` | V-31 | 2 executed, 0 failed/skipped | 2 | 1 | true | n/a |
| CP-18 | S4 | `tests/Antiphon.Tests -> bin-c1153-s4/` | portable-real-hold | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Real_client_certificate_holds_original_input*` | V-14 | 2 executed, 0 failed/skipped | 2 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S4 | `CP-18` | portable-failed | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Real_client_bad_evidence_keeps_failure*` | V-15 | 8 executed, 0 failed/skipped | 8 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S4 | `CP-18` | portable-final-read | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Final_certificate_is_revalidated_under_lock*` | V-16 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S4 | `CP-18` | portable-working | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Working_or_unknown_inventory_withholds*` | V-17 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S4 | `CP-18` | portable-enrollment | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Only_new_cold_dispatch_prepares_evidence*` | V-18 | 6 executed, 0 failed/skipped | 6 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S4 | `CP-18` | portable-prescreen | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Nonpristine_brief_never_closes_identity*` | V-19 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S4 | `CP-18` | portable-rollback | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Certificate_failure_does_not_leak_a_hold_on_later_save*` | V-20 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S4 | `CP-18` | portable-due-ceiling | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Due_hold_statement_ceiling*` | V-24 | 2 executed, 0 failed/skipped; exact pins reported | 2 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | all | `tests/Antiphon.Tests -> bin-c1153-final-server/` | portable-s1-regression | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_*` | R-1 | all C1149 methods and arguments, 0 failed/skipped | 49 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | all | `CP-26` | portable-whitelist | `/*/*/AbsentLaunchPolicyTests/*` | R-2 | 77 executed, 0 failed/skipped | 77 | 1 | true | n/a |
| CP-28 | all | `CP-26` | portable-generation-wire | `/*/*/SessionRunnerGenerationWireTests/*` | R-3 | 9 executed, 0 failed/skipped | 9 | 1 | true | n/a |
| CP-29 | all | `CP-26` | portable-boot-stall | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-6 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | all | `CP-26` | portable-brief-recovery | `/*/*/DelegationBriefRecoveryTests/*` | R-7 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | all | `CP-26` | portable-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-8 | baseline 18/18/4 plus any landed S2 arguments, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | all | `CP-26` | portable-docs | `/*/*/SessionRunnerAbsenceEvidenceDocumentationTests/C1153_Owner_sentences_match_the_protocol*` | V-21 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-33 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-final-runner/` | portable-launch-generation | `/*/*/PhoneHomeCommandDispatcherTests/Duplicate_launch*` | R-4 | all 3 duplicate-launch methods, 0 failed/skipped | 3 | 5 | true | n/a |
| CP-34 | all | `CP-33` | portable-release-generation | `/*/*/PhoneHomeCommandDispatcherTests/(Pre_ack_resend*)\|(Launch_of_an_exited_session_under_a_new_generation_still_relaunches*)` | R-4 | all 3 methods, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-35 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-final-windows/` | windows-generation | `/*/*/RunnerSessionGenerationTests/*` | R-9 | all 5 methods, 0 failed/skipped | 5 | 6 | true | n/a |
| CP-36 | all | `CP-35` | windows-compaction-stop | `/*/*/CompactionContinuationStopTests/*` | R-5 | 6 executed, 0 failed/skipped | 6 | 1 | true | n/a |

Roster notes. Counts are TUnit executions from `[Test]` plus `[Arguments]`; no Skip, MethodData
or Matrix in any selected class. CP-26's 49 is V-1 1, V-2 1, V-3 5, V-4 3, V-5 3, unknown-native
6, whitelist-gap 19, native-attempt 4, failed-hold 1, caller-note 3, budgets 3; include any
argument CARD-1149/1150 S2 lands. CP-10's 12 is the four parameterized methods of
`UnixPtyArgvAdmissionTests`. CP-35 and CP-36 run on a Windows host only: both classes launch
`cmd.exe` and carry no platform skip, and `CompactionContinuationStopTests` measured 2 passed,
4 failed on this Linux mirror at the baseline (`Status` already `Exited`), so the plan's
`portable-compaction-stop` label was wrong. On Linux pass `--rows CP-26,CP-27,CP-28,CP-29,
CP-30,CP-31,CP-32,CP-33,CP-34` for the final group and run CP-35 and CP-36 on Windows with
`--rows CP-35,CP-36`. CP-34 uses the CARD-0403 per-operand form `(A*)|(B*)`; the plan's
single-group `(A*|B*)` form is not the documented syntax. Both CP-33 and CP-34 were verified
on this mirror to select exactly 3 methods each (3 passed). Union of all rows = V-1..V-24
and R-1..R-11.

### Cost

- Ordinary V/R floor for Code (sum of EstimatedMinutes, CP-1..CP-36) = 75 minutes, estimated: S1 rows 9, S2 rows 10, S3 runner rows 7, S3 server rows 8, S4 rows 13, final server rows 15, final runner rows 6, Windows lane 7. Slot waits are outside the floor; today's diagnostic waits were 0 to 105 seconds per lease.
- Setup/build = 8 isolated builds (runner S1, S2, S3, final; server S3, S4, final; Windows runner final) at about 2 to 3 minutes each, 20 minutes estimated, already inside the row estimates that build.
- PC floor (Mutation) = 525 minutes, estimated: 150 method-scoped cycles. Runner-side cycles (PC-1..PC-16, PC-27: 64 variants) at 3 minutes = 192; server non-Postgres cycles (PC-17, PC-18, PC-19: 52 variants) at 3.5 minutes = 182; server Postgres cycles (PC-20..PC-26, PC-28: 29 variants) at 5 minutes = 145; documentation controls (PC-29: 5) at 1 minute = 5; rounded to 525. The plan's 100-minute floor is revised upward; the two one-flip tables (55 cycles, about 180 minutes) dominate and cannot be batched because they share one method each, and SourceLanding forbids shards.
- Round 2 (Review a086fe80): rows CP-41..CP-44 add 4 estimated ordinary minutes (they reuse the S1 build); PC-30..PC-33 add 15 runner-side method-scoped cycles at 3 minutes = 45 minutes, for a PC floor of 570 minutes.
- Total = 285 authoring (plan) + 75 ordinary + 525 PC = 885 minutes, estimated. Reuse avoids 28 extra builds (every `CP-n` build cell); at 2.5 minutes each that is 70 minutes not spent.

Passed the bundle check: bodies read; guards 30, mapped 29 plus 1 inherited with its prior PC, missing 0, duplicate PC maps 0; every PC names a compiling defect and an exact method; Cost is numeric.

## Diagnostic runs

Run on this Linux runner mirror (nested Docker, Testcontainers PostgreSQL) at the skeleton
commit `bb77118e90c0920c51fb6871310839f6c7ac6aae`, through the build-slot gate. They are
diagnostics, not checkpoint executions, and they prove three things: the skeletons compile
in both test projects, the baseline rows this design leans on are green here, and the
statement budgets are measured at 18/18/4 by the existing row.

- Checkpoint tool: `import --plan` on this file imported 36 rows with no warnings; `coverage --plan` reported `obligations=0 missing=0 unmapped=0 result=clean` (the lint binds the plan's V table; this note's bullet format yields no obligations).
- `CHECKPOINT TD-R1 commit=bb77118e9 build=ok filter=/*/*/CompactionContinuationStopTests/* executed=6 passed=2 failed=4 skipped=0 slot=granted waited=90s dirty=0 sourceState=clean buildSource=verified` (runner build `bin-c1153-td-runner/`, 46 s build, 21 s run). The four failures assert `Status` not `Exited` on a `cmd.exe` child; inherited Windows-only fixture, moved to CP-36.
- Runner skeletons on that build: `/*/*/RunnerAbsenceEvidence*/*` discovered all 58 skeleton cases as skipped with their `CARD-1153 S<n> pending` reason (22+9+5+3+1 for S1, 4+3+2+1 for S2, 4+3+1 for S3); 0 passed, 0 failed.
- `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1153-td-server/ --property:UseAppHost=false`: 0 errors, 142 s under lease.
- `CHECKPOINT TD-S1 commit=bb77118e9 build=reused filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets* executed=3 passed=3 failed=0 skipped=0 dirty=0 sourceState=clean` with `C1149-BUDGET held-dispatched-tick total=18`, `working-live-tick total=18`, `inside-grace-absent-scan total=4`; 47 s wall clock including the container.
- `CHECKPOINT TD-S2 commit=bb77118e9 build=reused filter=/*/*/AbsentLaunchPolicyTests/* executed=77 passed=77 failed=0 skipped=0`; 2 s.
- Server skeletons on that build: `/*/Antiphon.Tests.Application/(RunnerAbsenceEvidenceValidatorTests*)|(SessionRunnerAbsenceEvidenceDocumentationTests*)|(DelegationDispatchRecoveryBoundaryTests*)/C1153_*` discovered 64 skipped (35, 1, 28) and `/*/Antiphon.Tests.Agents/(SessionRunnerAbsenceEvidenceClientTests*)/C1153_*` discovered 18 skipped (14+4); 0 passed, 0 failed. A parenthesised class list without the per-operand form selected zero tests, which is the CARD-0403 behaviour.
- `/*/*/PhoneHomeCommandDispatcherTests/Duplicate_launch*` and `/*/*/PhoneHomeCommandDispatcherTests/(Pre_ack_resend*)|(Launch_of_an_exited_session_under_a_new_generation_still_relaunches*)` each executed 3, passed 3 on the runner build.

Alternate outputs `bin-c1153-td-tool/`, `bin-c1153-td-runner/` and `bin-c1153-td-server/` were deleted after these runs; results stay under the ignored `.antiphon/c1153-td/`.
