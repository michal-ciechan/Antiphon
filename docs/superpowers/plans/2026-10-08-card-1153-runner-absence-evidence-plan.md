# CARD-1153: authenticated evidence for a never-created runner session

Plan date: 2026-10-08. Next stage: **test-design** (separate audit before Code).
Planning baseline: fetched `origin/master` **e987af29fe242fe6c8d18bd86991a3f4c71a4ac1**.
The task branch was advanced to that commit by fast-forward, without rewriting history.
CARD-1153: `99911534-c54d-4181-8fbc-5b632d28e77c`, Antiphon board.
Read the live card in full with `scripts/card.ps1 get CARD-1153 -Board Antiphon`.

Provide an explicit certificate for the refused-launch case that CARD-1149 S1
currently cannot recognize through the real runner client. Keep the existing S1
Blocked transaction, original-input preservation and zero automatic relaunch bound.
An ordinary unknown ID, an empty transcript DTO, and an unavailable runner are not
certificates. This plan deliberately does not retroactively certify old unknown IDs.

The minimum honest implementation needs more than a new 404 response body: establish
coverage before the first launch, remember an attempted creation even if its runtime
entry disappears, and prevent a delayed launch from invalidating the proof. The two
small protocol operations below provide that coverage without changing the database.

## Ground truth

Paths and line numbers below were checked in the baseline above; they are baseline
citations, not promises that line numbers survive concurrent landings. `Dispatcher`
means `server/Application/Services/AgentTaskDispatcher.cs`; `Runtime` means
`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`.

| Card assumption / question | What master actually does | Consequence |
|---|---|---|
| S1 can hold a real unknown session. | `Dispatcher:2767` reads `GetTranscriptAsync`; `:2768` accepts complete empty entries only with matching ID/generation. `:2774` catches failed reads as unknown evidence. | Replace that evidence source, not the S1 whitelist. |
| The real transcript API supplies an empty result for absence. | `src/Antiphon.SessionRunner/Program.cs:312` calls `Runtime.GetTranscript`; `Runtime:880` calls `GetSession`; `:2130` throws `KeyNotFoundException`. `Program.cs:152` maps this to HTTP 404. | Preserve the existing route and its 404 behavior. |
| A client can distinguish that 404 from empty history today. | `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:595` calls `ReadRequiredJsonAsync`; `:140` calls `EnsureSuccessStatusCode`. `PhoneHomeRunnerClient.cs:207` uses the Transcript operation; `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:234` calls the same runtime lookup. | Cover both transports; do not catch 404 and manufacture an empty transcript. |
| Empty inventory alone authorizes a hold. | `Dispatcher:2716` first requires owning-runner absence, then `:2719` applies `AbsentLaunchPolicy`. `:2789` reads the owner binding; remote absence requires `RunnerInventory.Available`. `:2933` rechecks evidence under the task lock. | Preserve inventory and Working safety withholds before the ordinary failure path. |
| Empty native entries are the only whitelist condition. | `server/Application/Services/AbsentLaunchPolicy.cs:36` requires exactly one pristine original brief, known column census, no Working/history and the exact runner-unknown reason. `Dispatcher:2744` reads every related message identity, with a two-row refusal bound. | No field, correlation, history or reason guard is removed or weakened. |
| Generation can measure freshness or coverage since startup. | `src/Antiphon.SessionRunner.Contracts/SessionGeneration.cs:5` explicitly defines it as an opaque equality token at PostgreSQL microsecond precision, not a clock test. | Use `SessionGeneration.Equal` for identity; nonce plus local elapsed time for freshness. |
| A missing session record proves it was never created. | `Runtime:521` removes a failed launch; `:1519` removes a released runtime entry; `:1550` deletes durable session artifacts. `PhoneHomeLaunchGenerationStore.cs:22` retains generation fences only seven days and `:40` can return null. | Neither dictionary absence, missing sidecar nor missing watermark is complete history. |
| The runner already has coverage for every unknown ID. | `src/Antiphon.SessionRunner/TranscriptSidecar.cs:64` names the binding path; `:82` intentionally returns null on corrupt/unreadable input. `Runtime:1675` adopts survivors; `Program.cs:193` gates HTTP readiness on adoption. | A tolerant loader is not an absence probe. Require a prior fresh-ID record, strict reads and completed adoption. |
| Both runner transports already authenticate certificates. | `Program.cs:152` and the session route at `:312` have no caller-auth check. `server/Api/Endpoints/SessionRunnerEndpoints.cs:50` authenticates phone-home registration with the configured runner secret; `PhoneHomeConnectionService.cs:100` reads that secret from its file. | Phone-home can use its authenticated connection; direct HTTP needs explicit authentication for the new operations. Loopback is not authentication. |
| There is a narrow place to establish fresh-ID coverage. | `Dispatcher:5471` constructs a fresh session with `Guid.NewGuid`; `:5493` binds runner/store. `:5589` commits before `:5618` finishes the spec and `:5625` enqueues launch. | Prepare only that newly allocated cold-dispatch ID after commit and before the first enqueue. Never prepare from a sweep, retry/resume reader or GET. |
| S1 has no implementation evidence. | `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunch.cs:31` is the positive hold fixture. `.AbsentLaunchWhitelist.cs:24` pins six unknown-native shapes. `DelegationDispatchRecoveryBoundaryTests.cs:25` pins 18/18/4 statements. | Retain the assertions; replace explicit positive fake evidence with the new explicit certificate. Add a real-client witness. |
| CARD-0079 is the stated only automatic stop of a Working session. | Policy: `docs/session-runtime-invariants.md:770`. Master also has the separately tracked boot-stall call at `Dispatcher:3157`, implementation `:3211`, and `RetryAsync` at `:3291`; `BootStallWorkingTickCharacterizationTests.cs:29` pins that existing stop/requeue. | Preserve the CARD-0079 policy; do not claim CARD-1153 repairs the CARD-1151 policy/code discrepancy. Leave both boot-stall methods and their characterization unchanged. |
| Blocking releases a waiting seat after a timeout. | `docs/session-runtime-invariants.md:70-79`: parking is disabled by default and supplies no automatic release deadline. | A never-created session owns no process. Any unrelated session waiting for input still needs the existing explicit action; no new release timer. |

Baseline reference: [CARD-1149/1150 plan, D-1](2026-10-07-card-1149-1150-dispatch-recovery-plan.md#d-1-card-1149-uses-an-explicit-hold-not-a-new-automatic-launch-state-machine)
explicitly acknowledges the 404 limitation and requires a separate absence protocol.

## Decisions

### D-1. A small preparation record establishes the evidence domain

Add authenticated `POST /sessions/{id}/absence-evidence/prepare`, with version 1,
the freshly allocated session ID, expected accepted generation, expected runner store,
and a request nonce. The only production caller is the cold-dispatch branch that
allocated this GUID in this invocation, after the claim commits and before either
launch sink is called. It asserts fresh allocation, not merely an empty DB transcript.
Do not call it for a reused pool session, recovery, resume, or an existing ID selected
from the DB. A cryptographically generated GUID's uniqueness is the same identity
assumption as today's session allocation; this operation is not a way to erase history.

The runtime keeps one small record per observed ID in
`<SessionLogPath>/absence-evidence/<id:N>.json`, plus an instance epoch. A record has
version, store, session ID, generation, preparing epoch and one of `Prepared`,
`Attempted`, `ClosedUnused`. Atomic temp/write/flush/rename uses the existing runner
state volume; no global provider-directory scan, database table or migration.
Retain these records; no seven-day pruning or AuditCleanup ownership. Unknown files,
schema versions, missing roots after initialization, IO errors and malformed records
are unknown evidence, never a blank store. Treat `File.Exists` false cautiously:
permission/IO errors must remain distinguishable through strict opens/enumeration.

Prepare holds the existing per-session launch gate. It refuses any existing runtime
entry of any status/generation, any prior attempt, any durable launch/custody/manifest,
Herdr or transcript-sidecar evidence, including corrupt or empty artifacts. It checks
the retained phone-home watermark as an exclusion, never as positive proof. A clean
first call durably writes `Prepared`; repeated calls return the original record only
for the identical identity in the same runtime epoch. Never overwrite Attempted,
ClosedUnused, foreign generation/store, or a record from an earlier epoch.

Every provider-creation/attach entry records `Attempted` under the same gate before
any process, pane attachment, native transcript discovery or sidecar write can occur.
Record attempts even when no prepare exists: reconciliation can beat the prepare call
after the DB commit. Preserve the marker on failed starts, releases and disposal.
Startup/adoption records discovered IDs as attempted and completes before evidence is
available. An attempted-write failure latches evidence unavailable for this runtime
before launch proceeds; it must not leave a valid Prepared record usable in memory.
Do not introduce a new launch refusal just because optional evidence storage failed.

Prepared/ClosedUnused proof is usable only in the epoch that prepared it. After a
runner restart all earlier records refuse certification; their presence also refuses
re-preparation. This handles incomplete adoption, old-build interludes and downgrade
without inventing historical completeness. Availability across runner restarts is
deliberately sacrificed; an absent task then keeps existing Failed behavior. New
post-restart IDs can qualify normally. Never use generation time as an epoch cutoff.

Preparation has one bounded attempt, at most five seconds. Unsupported, error or lost
acknowledgement does not prevent the existing dispatch/launch path. Caller cancellation
propagates. Do not retry preparation as a background job, nor add recurring SQL reads.
If the claim committed but execution died before prepare, there is no proof: preserve
the existing result. This makes the concrete refused-enqueue case work when prepare
succeeded, rather than promising retrospective repair of every historical absence.

Rejected: treating absence from the runtime dictionary or seven-day watermark as
never-created; comparing StartedAt to runner boot time; checking provider directories
from the server; changing ordinary transcript semantics; adding a general retry ledger.

### D-2. Certification closes only an uncreated ID and returns a typed proof

Add authenticated `POST /sessions/{id}/absence-evidence`, request version 1 with
expected generation/store and a fresh random nonce. This is deliberately POST: it
closes the unused identity. Under the launch gate, require the same-epoch Prepared
record, complete adoption, no latched storage fault, no attempt and no runtime or
retained artifact. Atomically change Prepared to ClosedUnused **before** replying.
Subsequent requests can return a fresh certificate from the same ClosedUnused record
after the same exclusions are checked. Do not cache response bodies/nonces.

ClosedUnused permanently refuses a later create or attach for that session ID,
including another generation. That prevents a delayed launch after the last inventory
read from invalidating the proof. This is an admission fence for an ID that has never
had a process, not a stop, kill, parking operation, capacity seat or timer. Explicit
Retry can allocate a new session through its existing path; this plan adds no retry.
Before legacy launch/attach entry points do anything, they must consult this fence.
Never silently turn a refused same-ID resume into a fresh session.

Wire success, HTTP 200 / phone-home Result:

```json
{
  "version": 1,
  "outcome": "never_created",
  "sessionId": "<exact requested id>",
  "acceptedStartedAt": "<exact normalized generation G>",
  "runnerStoreId": "<bound owning store>",
  "runtimeEpoch": "<preparing and currently serving epoch>",
  "requestNonce": "<this request's nonce>",
  "complete": true,
  "creationObserved": false,
  "nativeTranscriptPresent": false,
  "sidecarTranscriptPresent": false,
  "processPresent": false,
  "identityClosed": true,
  "observedAtUtc": "<runner audit timestamp>"
}
```

Use nullable evidence fields on deserialization, so omitted false/true values cannot
be mistaken for observations. The certificate contains metadata only, no paths, raw
transcript, environment, credentials or process command lines. The API is internal
and has no server public proxy. Return `Cache-Control: no-store` for HTTP results.

An arbitrary never-prepared ID remains 404, with no certificate. Known/attempted,
wrong-generation/store, prior-epoch or incomplete state returns a typed 409 refusal
without positive evidence. Malformed input is 400; authentication failure is 401/403;
unavailable inspection is 503. On phone-home use the corresponding typed error frame.
The existing GET `/sessions/{id}` and `/sessions/{id}/transcript` remain 404 for a
prepared-but-never-created ID. Test that fact, not a substituted empty transcript.

Rejected: a purely observational certificate that allows a delayed create immediately
after issuance; deleting runtime artifacts to manufacture proof; a release/kill call
as part of certification; indefinite leases that would require new parking semantics.

### D-3. Authenticate both protocol paths explicitly

Advertise `sessionAbsenceEvidenceV1` through both capability producers only when the
evidence service is ready. Append phone-home enum operations without changing existing
ordinals: PrepareAbsenceEvidence and CertifyAbsence. Dispatch only through the existing
authenticated, runner/store-bound phone-home connection. The response correlation ID,
connection ownership, expected store and nonce must all match; no fallback to desktop.
An enum/schema match on an unauthenticated JSON object is not authentication.

For direct HTTP add optional typed `SessionRunner:AbsenceEvidence:KeyPath` settings on
runner and server. Use a dedicated owner-readable random key file through the existing
configuration custody policy, never argv, source control, logs or a delegate's inherited
environment. Missing key means this transport cannot certify. No task/operator token
is repurposed. Provisioning a live secret remains a human/operator step under
`docs/orchestration-loop.md`; this Plan/Code task does not create or print a live key.

Use HMAC-SHA256 request and response authentication with a domain-separated, versioned,
length-delimited canonical encoding of operation, ID, normalized generation, store,
nonce and body digest. Response signature covers the entire returned evidence and
request nonce. Compare MACs in constant time. Require a cryptographic 32-byte nonce,
an explicit key ID and exact version; changing any evidence bit invalidates the MAC.
The test fixture uses an ephemeral synthetic key. Do not expose the key through a
capabilities DTO or copy it into launched provider processes. Response signing is
necessary: adding a request bearer header alone does not authenticate a certificate.

Use existing authenticated phone-home transport instead of adding another secret there.
Keep the direct HTTP routes and phone-home operations thin adapters over the same
production evidence service. Extract `AbsenceEvidenceRoutes.cs` so contract fixtures map
the real handlers in an isolated random-port host without booting real server Program.

### D-4. Only a validated certificate supplies S1's native-empty fact

Add default-unsupported methods to `ISessionRunnerClient`; implement HTTP, phone-home,
routing and runner-scoped wrappers. Keep raw wire parsing in Infrastructure. Expose a
validated application result, not a caller-set `authenticated=true` bit. Share structural
validation so transports cannot disagree. Use TimeProvider for request elapsed time.

Each read uses a new nonce, a five-second total deadline and no generic read retries.
Accept only a successful authenticated response for the owning store, exact session
and `SessionGeneration.Equal` generation, known nonempty runtime epoch, matching nonce,
version 1, outcome never_created, complete=true, identityClosed=true and every presence
field explicitly false. Reject missing/default fields and malformed JSON. Local elapsed
request time must be <= five seconds; the validated object expires five seconds after
receipt. `observedAtUtc` is audit metadata, never an equality/clock-skew authority.
Re-read under the queue gate/task lock and recheck age immediately before StageBlocked.
No certificate object survives a sweep, retry, store change or connection replacement.

Change `ReadAbsentLaunchEvidenceAsync` to request this result in place of accepting
an empty transcript. Pre-screen the already-read DB whitelist facts before requesting
the closing certificate, so an attempted/dirty brief cannot close an ID incidentally.
The pre-screen may call the unchanged pure policy with a provisional native-empty
fact, but that value must never escape into the final decision: replace it with the
validated certificate result and run the policy again. Test this boundary explicitly.
Retain nativeAttempt=true as an unconditional exclusion.

404 without certificate, unsupported route/operation, old capability set, unauthenticated
or tampered response, timeout, unreachable evidence endpoint, wrong ID/store/generation,
stale nonce/age, incomplete evidence and ordinary empty transcript all fail the proof.
For an otherwise eligible due failure with fresh positive inventory absence, they take
today's `FailAndNotifyAsync` outcome, not Blocked. **Do not change S1's earlier safety
withhold:** when the runner inventory itself is unreachable/unknown, or a Working/listed
session appears, S1 leaves the task untouched. The card's shorthand “every error means
Failed” must not override that existing safety gate. Distinguish these two test setups.
Caller-requested cancellation still cancels; it must not be converted into a task failure.

Preserve the original brief IDs, bodies and spill bytes, dispatch identity, worktree,
one Blocked event and parent-note transaction, CompletedAt=null and zero automatic
relaunches. Do not change `AbsentLaunchPolicy`'s column census or whitelist semantics.
Retain repeated-tick idempotence, save-failure rollback and current ownership rechecks.

### D-5. Compatibility, rollout and performance are conservative

| Server / runner | Behavior |
|---|---|
| New / old | Capability/route/operation unsupported: no preparation, no certificate; normal launch still works and the existing due failure remains Failed when inventory absence is known. |
| Old / new | Old launch, Get and Transcript behavior remains; no prepare caller means no positive certificates. Write-ahead attempted markers do not alter successful legacy launch behavior. |
| New / new, prepared unused ID | Certifies through authenticated transport; S1 may hold only after all other whitelist gates pass. |
| New / new, arbitrary old ID or lost preparation | 404/unknown, never retroactive empty history. |
| Restart / downgrade / changed store | Existing prepared IDs cannot certify across epochs; no re-preparation. New IDs qualify after healthy readiness. Store mismatch refuses. |
| New local pair without evidence key | Feature unavailable for HTTP; existing launch and failure semantics continue. Phone-home uses its existing authentication. |

No database migration. Do not overload specialist/capacity JSON columns to store this
evidence. Runner files are protocol records, not application schema. No new SQL in idle,
held, Working or inside-grace scans; preserve **18/18/4 exactly** in the existing fixture.
Add HTTP/certification only at first cold dispatch and the rare already-due S1 path.
Reusing already-loaded runner fields may enlarge a projection, not add a recurring query.
Measure due-failure statements separately; preserve S1's existing <=40/no-parent and
<=48/parent ceilings wherever their fixtures exist at the Code baseline.

### D-6. Safety and waiting-session boundaries

CARD-0079 remains the only policy-authorized automatic stop of a Working session.
This feature performs no stop. The existing CARD-1151 `TryFailBootStallAsync` +
`RetryAsync` tail is untouched, including its current characterization. No assertion
is weakened or deleted to hide that separately tracked discrepancy.

The new hold has no runner process to release. It does wait for an explicit caller
decision: inspect original input, then retry or cancel through existing actions.
Automatic relaunch bound is zero. For a real session waiting for input, today nothing
automatically releases it while parking is disabled; there is **no timeout/deadline**
from this card (CARD-1083). ClosedUnused is an identity fence, not a reserved capacity
seat. It neither consumes a process slot nor creates a lease-expiry cleanup job.

## Slices, collisions and activation

All slices are sequential. Commit/push each meaningful slice before its checkpoints.
Paths below are the planned edit surface; existing filenames are baseline-verified,
and new filenames are explicitly marked. Do not implement from stale line offsets.

| Slice | Authoring | Files and work | Tests / checkpoint group | Activation |
|---|---:|---|---|---|
| S1 | 60 min | New `src/Antiphon.SessionRunner.Contracts/RunnerAbsenceEvidence.cs`; new runner `RunnerAbsenceEvidenceStore.cs`, `RunnerAbsenceEvidenceService.cs`; record state machine and strict filesystem access. | New `tests/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceTests.cs`; V-1..V-4. | Runner restart after all slices land; none during authoring. |
| S2 | 60 min | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`: launch/attach/adoption gates and ClosedUnused refusal, attempt markers before provider effects. New service integration; startup ready/fault latch. | Same runner tests plus new `RunnerAbsenceEvidenceRuntimeTests.cs`; V-5..V-8. | Runner restart. |
| S3 | 60 min | New runner `AbsenceEvidenceRoutes.cs`; new contracts `AbsenceEvidenceAuthentication.cs`; `Program.cs`, runner `SessionRunnerSettings.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeRuntimeAdapter.cs`; contracts `PhoneHomeContracts.cs`, `SessionRunnerContracts.cs`. Server `Application/Interfaces/ISessionRunnerClient.cs`, `Application/Settings/SessionRunnerSettings.cs`, new `Application/Dtos/SessionRunnerAbsenceEvidenceDto.cs` and `Application/Services/RunnerAbsenceEvidenceValidator.cs`; all four `Infrastructure/Agents/SessionRunner/*Client.cs` wrappers. | New `RunnerAbsenceEvidenceContractTests.cs`, `RunnerAbsenceEvidencePhoneHomeTests.cs`, `tests/Antiphon.Tests/Agents/SessionRunnerAbsenceEvidenceClientTests.cs`; V-9..V-13. | Runner + AppHost restart; HTTP key provision is separate operator custody. |
| S4 | 60 min | `AgentTaskDispatcher.cs`: fresh-ID preparation after commit/before sink, evidence-reader substitution, final freshness guard. New `.AbsentEvidence.cs` partial for `DelegationDispatchRecoveryBoundaryTests`; adapt its existing explicit certificate fixtures without weakening assertions. | V-14..V-20, 18/18/4 witness; real HTTP and phone-home clients. | AppHost restart. |
| S5 | 45 min | `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/agent-credentials.md`: sentences below; new `tests/Antiphon.Tests/Application/SessionRunnerAbsenceEvidenceDocumentationTests.cs`; finish named regressions and evidence report. | R-1..R-9 and static doc pin V-21. | Docs alone none; activate executable slices together after Review/Land. |

TestDesign must split a slice further if actual authoring exceeds 60 minutes; it must
not fold away a named behavior or substitute a broad whole-Unit run. The capability
declaration is `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:141`
and its DTO is at `:972`; extend these rather than introducing a second catalogue.

| In-flight work named in the brief | Shared files / disposition |
|---|---|
| CARD-1149/1150 S2, Code `7c5280e8` | **AgentTaskDispatcher.cs** directly overlaps S4. It also owns `SessionMessageQueueService.DispatchBrief.cs` and `SessionReconciliationService.cs`; neither is changed here. Serialize S4 after its land, re-read the evidence/hold sites and rerun the common boundary class selections. |
| CARD-1115 S1, `c57f1d8a` | **AgentTaskDispatcher.cs** overlaps S4. Preserve baseline capture and warnings; serialize the shared-file Code work. |
| CARD-1144 S1, `8652f40b` | Park Reply files do not overlap planned executable edits. Do not change Reply/park/release semantics. Owner docs may overlap S5; integrate only the new absence paragraphs. |
| CARD-1105 audit repair | `scripts/c590-remote.sh` does not overlap. Do not edit it for checkpoint convenience. |

Observed live settings were read with GET `/api/runner-defaults` and GET
`/api/session-runners` at 2026-10-07 23:47 UTC. Defaults revision was 2, no per-kind
override; catalogue had one available Windows entry and two available Linux entries,
one Linux entry draining and the other accepting work with 7/10 occupied. These are
observations, not placement instructions. Resolve the live defaults again before
dispatch; omit `-Runner` and `-Platform` for portable work. Use `-Platform Windows`
only for the specifically named native generation regression row, with no host pin.
Never embed a fleet URL or location into the implementation or checkpoint command.

No restart is needed to write/review this plan. After ordinary Code/Review/Land,
activate updated runners first, then AppHost; this order preserves mixed-version
behavior. A new runner binary is necessary for certificates. The orchestrator uses
the canonical-checkout `scripts/restart-session-runner.ps1` for a desktop runner,
without `-KillSessions` or `-AllowWorktree`, and the documented rolling phases for
a container runner. Preserve and re-adopt detached Working sessions; never kill them
to deploy this API. Check restart/launch locks and ownership first. AppHost activation
uses `scripts/restart-apphost.ps1`, verifies `/api/version` against canonical HEAD,
and separately checks runner build/capability/authenticated probe. Health alone is
not feature activation. Do not rotate/provision live secrets as an incidental restart.

## Owner-document sentences and pins

S5 adds these sentences at the existing launch/recovery, runner route and credential
sections; update the full API map as well as the operational guide. Pin behavior in
the named tests and exact documentation text in V-21.

1. Runtime owner: “CARD-1153 accepts native-empty evidence only from a fresh,
   authenticated never-created certificate for the same session, generation and
   owning runner store; a 404 or an empty transcript is not that certificate.”
   Pins: V-9, V-12, V-14..V-16.
2. Runtime owner: “Certification closes an unused session identity against delayed
   launch and never stops a process; old IDs and preparation from an earlier runner
   epoch remain unknown.” Pins: V-3, V-5, V-7, V-8.
3. HTTP/API owners: “POST /sessions/{id}/absence-evidence/prepare and POST
   /sessions/{id}/absence-evidence are runner routes without /api; ordinary session
   and transcript GETs still return 404 for a never-created ID.” Pins: V-9, V-10.
4. Credential owner: “Direct HTTP absence evidence requires its dedicated key-file
   configuration and signed request/response; missing authentication disables only
   absence certification. Phone-home uses its authenticated owning connection.”
   Pins: V-10..V-13. Do not document a literal key or fleet path.
5. Runtime owner: “CARD-1153 adds no automatic relaunch or input-wait release deadline;
   parking remains disabled by default (CARD-1083), and CARD-1151 owns the existing
   boot-stall retry behavior.” Pins: V-14, V-17, R-6.

## Verification design

This section gives TestDesign a concrete behavior/PC inventory and runnable checkpoint
manifest to audit. Verification is a **separate next stage**, not execution claimed by
this Plan. No test, build, PC, deployment or live-secret operation ran in Plan.

### Fixtures and ordinary witnesses

Use random-port in-process Kestrel hosts mapping the **production** route extension,
real JSON contracts, synthetic signing keys, a fake provider-creation seam, temp state
roots and FakeTimeProvider. Do not use the Windows-only `LocalHttpRunner.exe` fixture
for portable contract tests. No fake provider is allowed to fabricate the certificate
instead of the production store/service in runner contract tests. Every lifetime is
awaited and disposed; a fixture that starts a child uses its assembly's
`ParallelLimiter<ProcessSpawnLimit>`. No production runner port or external broker.

Dispatcher tests use the existing isolated PostgreSQL fixture and production
SessionRunnerHttpClient over a fake runner HTTP host. That host must send the exact
signed wire shape produced by the contract fixture, including the matching nonce,
and answer GET transcript with real 404. Do not replace ISessionRunnerClient with a
fake empty transcript for the new positive witness. The phone-home case uses real
PhoneHomeRunnerClient and framed requests through the established test connection;
test a misleading local inventory plus the correctly routed remote owner.

In every positive dispatcher test assert final persisted task/event/note rows, byte
identity of original input and spill, one hold across repeated due ticks, CompletedAt
null, and zero Start/Kill/Release/Retry calls. Pull the queued caller note and assert
its stable reason and preserved identity; this card reuses the existing delivery
mechanism, so it does not claim a queued note is a received UserPrompt. R-1 preserves
the established parent-note and rollback assertions. Negative evidence tests assert
the unchanged Failed outcome only with independently available absent inventory.
Separate unknown-inventory tests assert Withheld and no state/process mutation.

Every V-n below names exactly one new test method, with `[Arguments]` expanded to the
stated count. No hidden loop inflates Min. Names are the implementation contract.

| V | Class.Method (method suffix below follows `C1153_`) | Cases / decisive assertions |
|---|---|---|
| V-1 | RunnerAbsenceEvidenceTests.Prepare_records_only_a_fresh_identity | 1: fresh ID is Prepared; no runtime session, provider call or transcript is created; identical prepare is idempotent. |
| V-2 | RunnerAbsenceEvidenceTests.Prepare_refuses_existing_evidence | 7: live, exited, attempted-only, transcript-sidecar, Herdr-sidecar, manifest, custody/watermark artifact. Every case refuses; malformed artifact still refuses. |
| V-3 | RunnerAbsenceEvidenceTests.Restart_or_store_change_never_renews_proof | 3: Prepared prior epoch, ClosedUnused prior epoch, changed store; no certificate and no overwrite on re-prepare. |
| V-4 | RunnerAbsenceEvidenceTests.Unknown_or_unreadable_state_is_not_absence | 5: unprepared ID, corrupt record, unknown schema, denied read, lost initialized root; never a positive certificate. |
| V-5 | RunnerAbsenceEvidenceRuntimeTests.Creation_consumes_proof_before_effects | 3: start, attach, adoption; provider seam sees Attempted before its first effect, including a throw then runtime removal/release. |
| V-6 | RunnerAbsenceEvidenceRuntimeTests.Store_failure_disables_proof_without_stopping_work | 1: failed Attempted write latches unknown before provider effects; provider seam throws before creating artifacts and runtime entry is removed, leaving the old Prepared record; no certificate afterward. Healthy legacy launch behavior remains. |
| V-7 | RunnerAbsenceEvidenceRuntimeTests.Certificate_and_launch_race_is_serialized | 2: barrier orders launch-first (no certificate) and certify-first (no launch); losing branch cannot return contradictory success. |
| V-8 | RunnerAbsenceEvidenceRuntimeTests.Closed_identity_refuses_delayed_creation | 3: same-generation Start, newer-generation Start, Attach; repeat certificate fresh nonce allowed in same epoch; no provider calls. |
| V-9 | RunnerAbsenceEvidenceContractTests.Real_unknown_transcript_has_a_separate_certificate | 1: production prepare + certify returns full shape; session/transcript GET stay 404; arbitrary ID stays uncertified 404. |
| V-10 | RunnerAbsenceEvidenceContractTests.Http_authentication_covers_request_and_response | 4: missing key, wrong request MAC, changed request field, altered response bit; no authenticated certificate or unauthorized state change. Verify no-store and no secret/path output in the positive control setup. |
| V-11 | RunnerAbsenceEvidencePhoneHomeTests.Authenticated_operation_preserves_binding | 2: prepare/certify framing via runtime adapter; unsupported operation errors; foreign store refuses; unauthenticated transport cannot invoke the handler. |
| V-12 | SessionRunnerAbsenceEvidenceClientTests.Rejects_noncertificate_wire_shapes | 14: 404, 501, empty-200, malformed, ordinary-transcript, missing-presence-field, incomplete, wrong-ID, wrong-generation, wrong-store, wrong-nonce, unknown-version, unsigned, bad-MAC. |
| V-13 | SessionRunnerAbsenceEvidenceClientTests.Freshness_and_cancellation_are_bounded | 4: <=5s valid, >5s deadline, expired validated result, caller cancellation; fake clock/barriers, no wall-clock sleep; one transport attempt. |
| V-14 | DelegationDispatchRecoveryBoundaryTests.Real_client_certificate_holds_original_input | 2: local HTTP, remote phone-home; refused enqueue after successful prepare, real transcript 404, due reconciliation/sweep, persisted hold, original bytes, parent note, zero process mutations. |
| V-15 | DelegationDispatchRecoveryBoundaryTests.Real_client_bad_evidence_keeps_failure | 8: plain404, old-runner, evidence-unreachable, timeout, stale-nonce, mismatched-generation, incomplete, empty-transcript; known available absent inventory, task Failed, no Blocked event. |
| V-16 | DelegationDispatchRecoveryBoundaryTests.Final_certificate_is_revalidated_under_lock | 3: changed generation, store/epoch change, expired proof between first read and final decision; no Blocked commit; preserve existing safety-withhold where applicable. |
| V-17 | DelegationDispatchRecoveryBoundaryTests.Working_or_unknown_inventory_withholds | 3: Working task, Working transcript, unavailable owning inventory with misleading empty local list; no certificate request, no fail/hold/kill/start. |
| V-18 | DelegationDispatchRecoveryBoundaryTests.Only_new_cold_dispatch_prepares_evidence | 4: fresh cold ID calls prepare before sink, warm reuse does not, recovery does not, resume does not; failed/unsupported prepare never becomes a launch gate. |
| V-19 | DelegationDispatchRecoveryBoundaryTests.Nonpristine_brief_never_closes_identity | 3: attempted delivery, extra related row, nativeAttempt; no certify call and original whitelist outcome. |
| V-20 | DelegationDispatchRecoveryBoundaryTests.Certificate_failure_does_not_leak_a_hold_on_later_save | 1: certificate passes, hold save fails, later unrelated save; no partial task/event/parent-note commit; retry tick remains recoverable. |
| V-21 | SessionRunnerAbsenceEvidenceDocumentationTests.Owner_sentences_match_the_protocol | 1: exact owner sentences/routes, no-api distinction, credential requirement and restart/legacy caveat. This is a doc pin, not behavioral proof. |

### Negative controls and restoration

Mutation runs after ordinary Code -> Review -> Land, on the exact SourceLanding SHA.
For each V-n use PC-n below and **only** `/*/*/<Class>/C1153_<ExactMethod>*` (the method
from the preceding table); keep each argument's receipt. Green, compile a quick
production-line mutation, intended assertion red, restore, rebuild and green. Do not
run a whole class for a PC. Zero tests, compile errors, fixture failures or a different
assertion do not count. Every argument must have an applicable targeted variant if
one mutation does not exercise it; TestDesign records the expanded variant roster.
No concurrent source edit under a run. Generated evidence stays ignored/external per
SourceLanding rules, and all mutations must be restored before settlement.

| PC / V | Production defect to inject | Required red |
|---|---|---|
| 1 | In new Store.Prepare, skip the Prepared write / return a new record on duplicate. | Missing durable record / identity or idempotence mismatch. |
| 2 | In Service.Prepare, bypass each named known-evidence exclusion individually. | That artifact case unexpectedly prepares. |
| 3 | Remove epoch or store equality in Store read/prepare (separate variants). | Prior epoch or changed-store certificate/re-prepare succeeds. |
| 4 | In strict store reader, convert each unknown/error branch to Prepared. | The corresponding unknown state certifies. |
| 5 | Move each runtime attempt marker after its first effect. | Provider seam observes no prior Attempted marker. |
| 6 | Remove the evidence-health latch on a failed attempt write. | A subsequent certify produces proof after attempted provider work. |
| 7 | Remove the certify/creation shared launch-gate acquisition. | Barrier-controlled simultaneous contradictory successes. |
| 8 | Bypass ClosedUnused refusal separately in start/attach entry points. | The delayed provider call occurs. |
| 9 | Restore GetSession lookup in the certify route. | Real prepared unknown ID returns 404 instead of a certificate. |
| 10 | Bypass request MAC check / response MAC coverage (separate variants). | Unauthorized state change or accepted tampered response. |
| 11 | Drop phone-home binding/operation forwarding or authenticated admission guard. | Wrong wire result or foreign/unauthenticated request enters runtime. |
| 12 | Bypass each transport/result validation guard separately. | Its malformed/mismatched wire shape is accepted as proof. |
| 13 | Remove deadline / age / cancellation guard individually. | Late certificate accepted or caller cancellation swallowed. |
| 14 | In dispatcher evidence reader return unknown instead of validated proof. | Persisted Failed rather than Blocked on the real-client positive path. |
| 15 | Treat each error/noncertificate response as native-empty. | Erroneous Blocked instead of existing Failed. |
| 16 | Reuse the first certificate rather than final read/age check. | Stale/rebound proof commits Blocked. |
| 17 | Remove each earlier Working/owning-inventory safety guard. | Evidence request or task/process mutation occurs. |
| 18 | Move prepare after enqueue / call it on warm, recovery or resume paths. | Call-order violation or forbidden prepare. |
| 19 | Skip the DB-only whitelist pre-screen and nativeAttempt exclusion. | Nonpristine candidate closes its ID. |
| 20 | Remove the existing DiscardUncommittedHoldAsync cleanup on rollback. | Later SaveChanges leaks task/event/note mutation. |
| 21 | Change one pinned sentence in its owning doc, not in the test. | Exact doc pin fails; record this as a documentation control. |

Do not weaken/delete the six existing unknown-native cases when adapting their fake
to the new certificate method. Keep every assertion and vary the corresponding
certificate/transport field. Add ordinary-transcript-only as a new negative witness.
The 76-argument pure whitelist and current Working/ownership tests remain unchanged.

### Regression scope, execution and cost

R-1: all existing `C1149_*` boundary methods (input/hold idempotence, whitelist, rollback,
race and real-failure protections). R-2: AbsentLaunchPolicyTests (77 executions at this
baseline). R-3: SessionRunnerGenerationWireTests (9). R-4: six exact phone-home launch
generation methods named by CP-25/26. R-5: CompactionContinuationStopTests (6).
R-6: BootStallWorkingTickCharacterizationTests (3, unchanged CARD-1151 boundary).
R-7: DelegationBriefRecoveryTests (3). R-8: existing 18/18/4 budget method (3 baseline
arguments; preserve additional S2 budget arguments if landed). R-9: Windows
RunnerSessionGenerationTests (5; real executable/generation/adoption regression).

No whole Unit category, whole Antiphon.Tests assembly or whole suite. PostgreSQL rows
run serial with `TUNIT_MAX_PARALLEL_TESTS=1`, and each new fixture gets its own schema.
Never co-schedule Antiphon.Tests with Antiphon.Agents.Pty.Tests/FakeClaude. Native
Windows generation work is a separate Windows lane; every other row is portable.

For each committed After group use the checkpoint tool through the build-slot wrapper:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1153-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-08-card-1153-runner-absence-evidence-plan.md --after S1 --serial --expected-source-sha <full-committed-sha>
```

Select S2/S3/S4/all at its commit, or exact `--rows` for the Windows-only row. Await
the foreground command; if it returns 75, continue the same run with tool `wait`
until terminal. Slot timeout is not permission for an unleased retry. The tool owns
per-row slots and isolated forward-slash `bin-.../` outputs. Preserve each unedited
CHECKPOINT line with SHA, roster/counts, dirty/source/buildSource provenance and reruns.
Do not edit source during a group. Validate receipts against their actual SHA;
remove only that run's owned alternate output directories through checkpoint cleanup.
No standalone exploratory build/test run without a stated reason.

### Cost

Authoring estimate: 285 minutes in five 45-60 minute slices. Ordinary checkpoint floor:
**68 minutes**, including the Windows lane. Code floor: **353 minutes** before failure
investigation and handoff. Post-land Mutation floor: **100 minutes** (21 PCs, with
per-argument validation variants expected to dominate); TestDesign must enumerate
variants and revise this numeric floor upward if needed. Combined floor: **453 minutes**.
No PC execution is claimed by Code or this Plan. Recheck these sums if rows change.

### Checkpoints

Group prefix names the lane: `portable-` or `windows-`. Build reuse stays within an
identical After token. Floors are executed TUnit results, not assertion counts.
For R-1 and R-4, execute all matching named methods, preserve the baseline roster, and
include any additional landed arguments; the stated minimum is a conservative floor.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-s1/` | portable-prepare | `/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity*` | V-1 | 1 executed, 0 failed/skipped | 1 | 5 | true | n/a |
| CP-2 | S1 | `CP-1` | portable-known-history | `/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence*` | V-2 | 7 executed, 0 failed/skipped | 7 | 1 | true | n/a |
| CP-3 | S1 | `CP-1` | portable-epoch | `/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof*` | V-3 | 3 executed, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-4 | S1 | `CP-1` | portable-store-unknown | `/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence*` | V-4 | 5 executed, 0 failed/skipped | 5 | 1 | true | n/a |
| CP-5 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-s2/` | portable-write-ahead | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Creation_consumes_proof_before_effects*` | V-5 | 3 executed, 0 failed/skipped | 3 | 5 | true | n/a |
| CP-6 | S2 | `CP-5` | portable-storage-fault | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Store_failure_disables_proof_without_stopping_work*` | V-6 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-7 | S2 | `CP-5` | portable-launch-race | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certificate_and_launch_race_is_serialized*` | V-7 | 2 executed, 0 failed/skipped | 2 | 1 | true | n/a |
| CP-8 | S2 | `CP-5` | portable-closed-id | `/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Closed_identity_refuses_delayed_creation*` | V-8 | 3 executed, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-9 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-s3-runner/` | portable-real-route | `/*/*/RunnerAbsenceEvidenceContractTests/C1153_Real_unknown_transcript_has_a_separate_certificate*` | V-9 | 1 executed, 0 failed/skipped | 1 | 5 | true | n/a |
| CP-10 | S3 | `CP-9` | portable-auth | `/*/*/RunnerAbsenceEvidenceContractTests/C1153_Http_authentication_covers_request_and_response*` | V-10 | 4 executed, 0 failed/skipped | 4 | 1 | true | n/a |
| CP-11 | S3 | `CP-9` | portable-phone-home | `/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding*` | V-11 | 2 executed, 0 failed/skipped | 2 | 1 | true | n/a |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1153-s3-client/` | portable-client-shapes | `/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Rejects_noncertificate_wire_shapes*` | V-12 | 14 executed, 0 failed/skipped | 14 | 5 | true | n/a |
| CP-13 | S3 | `CP-12` | portable-client-time | `/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Freshness_and_cancellation_are_bounded*` | V-13 | 4 executed, 0 failed/skipped | 4 | 1 | true | n/a |
| CP-14 | S4 | `tests/Antiphon.Tests -> bin-c1153-s4/` | portable-real-hold | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Real_client_certificate_holds_original_input*` | V-14 | 2 executed, 0 failed/skipped | 2 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S4 | `CP-14` | portable-failed | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Real_client_bad_evidence_keeps_failure*` | V-15 | 8 executed, 0 failed/skipped | 8 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S4 | `CP-14` | portable-final-read | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Final_certificate_is_revalidated_under_lock*` | V-16 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S4 | `CP-14` | portable-working | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Working_or_unknown_inventory_withholds*` | V-17 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | S4 | `CP-14` | portable-enrollment | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Only_new_cold_dispatch_prepares_evidence*` | V-18 | 4 executed, 0 failed/skipped | 4 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S4 | `CP-14` | portable-prescreen | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Nonpristine_brief_never_closes_identity*` | V-19 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S4 | `CP-14` | portable-rollback | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1153_Certificate_failure_does_not_leak_a_hold_on_later_save*` | V-20 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | all | `tests/Antiphon.Tests -> bin-c1153-final-server/` | portable-docs | `/*/*/SessionRunnerAbsenceEvidenceDocumentationTests/C1153_Owner_sentences_match_the_protocol*` | V-21 | 1 executed, 0 failed/skipped | 1 | 5 | true | n/a |
| CP-22 | all | `CP-21` | portable-s1-regression | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_*` | R-1 | all existing C1149 methods and argument cases, 0 failed/skipped | 40 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | all | `CP-21` | portable-whitelist | `/*/*/AbsentLaunchPolicyTests/*` | R-2 | 77 executed, 0 failed/skipped | 77 | 1 | true | n/a |
| CP-24 | all | `CP-21` | portable-generation-wire | `/*/*/SessionRunnerGenerationWireTests/*` | R-3 | 9 executed, 0 failed/skipped | 9 | 1 | true | n/a |
| CP-25 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-final-runner/` | portable-launch-generation | `/*/*/PhoneHomeCommandDispatcherTests/Duplicate_launch*` | R-4a | all 3 duplicate-launch methods, 0 failed/skipped | 3 | 5 | true | n/a |
| CP-26 | all | `CP-25` | portable-release-generation | `/*/*/PhoneHomeCommandDispatcherTests/(Pre_ack_resend*\|Launch_of_an_exited_session_under_a_new_generation_still_relaunches*)` | R-4b | all 3 methods, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-27 | all | `CP-25` | portable-compaction-stop | `/*/*/CompactionContinuationStopTests/*` | R-5 | 6 executed, 0 failed/skipped | 6 | 1 | true | n/a |
| CP-28 | all | `CP-21` | portable-boot-stall | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-6 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-29 | all | `CP-21` | portable-brief-recovery | `/*/*/DelegationBriefRecoveryTests/*` | R-7 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | all | `CP-21` | portable-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-8 | baseline 18/18/4 plus any landed S2 arguments unchanged, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-final-windows/` | windows-generation | `/*/*/RunnerSessionGenerationTests/*` | R-9 | all 5 methods, 0 failed/skipped | 5 | 6 | true | n/a |
