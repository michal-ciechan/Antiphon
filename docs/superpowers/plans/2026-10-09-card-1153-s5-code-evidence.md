# CARD-1153 S5 (owner documents, V-21, final group): Code evidence

Code task `9114d176-15f6-450e-8a3e-4ca947fd4266` (its own landing owner), branch
`feat/card-task-9114d176`, runner mirror worktree `/work/worktrees/task-9114d176`, fast-forward from
`origin/master` `4007182f06011e80c1f661dd48194801a7463b16` (S4 with the monotonic-age repair).
Plan `2026-10-08-card-1153-runner-absence-evidence-plan.md` (S5 row, owner sentences 1-5); test
design `2026-10-08-card-1153-test-design.md` (V-21, CP-26..CP-36). Checkpoint receipts (unedited
CHECKPOINT lines) are in the Code report; this note records decisions, red checks and the handoff.

## Change

No production code changed. No migration. No restart is needed for S5 itself (documents and a
test); S4 still needs its AppHost restart after land, owned by the orchestrator.

- `docs/session-runtime-invariants.md`: new bullet "A never-created session is held only on the
  runner's certificate (CARD-1153)" after the CARD-0679 live/unknown/gone bullet. Carries owner
  sentences 1, 2 and 5 and three operator rules from S4: the cold-dispatch prepare, the hold's
  recheck (monotonic age 0..5 s AND the wall deadline), and activation (runners first; no
  `sessionAbsenceEvidenceV1` means no prepare and the existing failure; health is not activation).
  Each sentence is followed by the tests that pin it.
- `docs/ops-http.md` (Two processes, two prefixes) and `docs/antiphon-api.md` (section 4, the
  runner's own API): sentence 3; the route map lists both POSTs; the API owner adds the wire
  summary (version 1 body, HMAC both directions, `no-store`, 200/404/409/400/401/503, closed-identity
  types, phone-home operations 38/39, when the feature is advertised).
- `docs/agent-credentials.md`: new `### Runner absence-evidence key (CARD-1153)` with sentence 4,
  the setting `SessionRunner:AbsenceEvidence:KeyPath` (server and each direct-HTTP runner, same
  file content: base64 of at least 32 random bytes), what is never done with the key, the
  missing-key behaviour and the operator-only provisioning/rotation order. No literal key or path.
- `tests/Antiphon.Tests/Application/SessionRunnerAbsenceEvidenceDocumentationTests.cs`: V-21
  replaces the skeleton. The two now-unused `Card1153Pending` helpers (server and runner test
  projects) are deleted; no other file used them.

## Decisions

1. Sentence 5 deviates from the plan. The plan's "CARD-1151 owns the existing boot-stall retry
   behavior" is false since CARD-1151 option B retired the automatic boot retry
   (`BootStallWorkingTickCharacterizationTests.Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing`).
   The owner says "CARD-1151 owns the boot-stall behavior, which neither stops nor retries a
   session"; V-21 asserts the plan's wording is absent.
2. Every stated rule was read in the landed code (AgentTaskDispatcher `DecideAbsentLaunchAsync`,
   `PrepareAbsenceEvidenceAsync` call site, `ReadAbsenceCertificateAsync`, `AbsenceCertificateHolds`,
   `HoldUnderLockAsync`; runner `AbsenceEvidenceRoutes`, `Program.cs` capability gate,
   `PhoneHomeRuntimeAdapter`; `AbsenceEvidenceKey.TryLoad`; `RunnerAbsenceRefusalCodes` statuses)
   and, except the released-seat exclusion (decision 3), is pinned by a named test that passed at S4
   (the server C1153 rows) or S1-S3 (the runner rows). This read missed the Attempted-marker write
   failure exception (Review b588c5c7 F1); see Repair F1 below.
   The certificate is requested once per decision; the hold re-checks that first certificate under
   the lock rather than asking again, so the owner says exactly that.
3. Released-seat answer resume: the cold path skips prepare when `ReleasedSeatAnswerId` is set, but
   no test pins it (S4 disclosure 3). The owner states the exclusion and says it is unpinned; it is
   not in a pinned sentence. Review/Mutation gap candidate.
4. V-21 compares the documents with production constants (route templates, feature token,
   closed-identity types, phone-home operation names and numbers, key setting names on both
   settings types, minimum key bytes, 30 s request freshness, 5 s lifetime). Server pins are
   `nameof`; runner pins are checked against the runner test source text, so a renamed method
   without its document fails.

## Sweep for sentences S1-S4 made false (docs/, server/, scripts/, tests/)

Searched for runner absence, unattempted/never-launched Dispatched tasks, runner-unknown,
empty-transcript evidence and the StartingGrace (90 s) plus dead-session grace (3 min) path.

| Site | Status |
|---|---|
| `docs/session-runtime-invariants.md` (CARD-1151 bullet: "S1's pristine absent-launch hold still takes precedence") | Still true; the hold now needs the certificate, stated in the new bullet. Unchanged. |
| `server/Application/Settings/DelegationSettings.cs:576-578` (`DeadSessionFailGraceMinutes`: "before the dispatcher fails it") | Incomplete since CARD-1149/1153: past the grace a certified never-attempted task is held Blocked, not failed. Listed, not edited: the same comment describes the in-memory wall-clock grace that CARD-1161 owns. |
| `server/Application/Services/SessionReconciliationService.cs:41-42` (`RunnerUnknownSessionReason`) | Still true. Out of bounds for this task. |
| `server/Application/Services/AgentTaskLiveness.cs:38` ("the reason written onto a task the dead-session sweep fails") | Still true for the failure path. Unchanged. |
| `AttentionService.cs:2311/3221`, `attentionVisuals.ts:294` | CARD-1160; not touched. |
| `AgentTaskDispatcher.cs` dead-session grace (`:2554/2573`) | CARD-1161; not touched. |
| `docs/superpowers/plans/*`, `docs/investigations/*` | Historical records; not owner documents. Unchanged. |
| Code comments in `AgentTaskDispatcher.cs`, `AbsentLaunchPolicy.cs`, the boundary test partials | Already updated by S4. |

No other false sentence was found by this sweep; Review b588c5c7 F1 later found the marker sentence false (Repair F1 below).

## Red checks for V-21 (quick mutations, not PCs)

Probe build `tests/Antiphon.Tests -> bin-c1153s5probe/` (UseAppHost=false, 110 s under lease,
deleted afterwards) at `1cd89f07`. Method filter
`/*/*/SessionRunnerAbsenceEvidenceDocumentationTests/C1153_Owner_sentences_match_the_protocol*`.
Green before: 1 executed, 1 passed. Each mutation was applied, run (1 executed, 1 failed) and
restored with `git checkout`; the tree was verified clean after the set. The first green attempt
was itself red: the agent-credentials edit had dropped the `### server2 runner credentials
(CARD-0604)` heading; the pin's section bound caught it and `1cd89f07` restored it.

| Case | Mutation | Red at |
|---|---|---|
| s1-certificate | runtime: "a 404 or an empty transcript" -> "a 404 or empty transcript" | owner sentence missing |
| s2-closure | runtime: "remain unknown" -> "stay unknown" | owner sentence missing |
| s3-ops | ops-http: drop "without /api" | ops-http: routes sentence |
| s3-api | antiphon-api: drop "without /api" | owner sentence missing |
| s4-credentials | agent-credentials: drop "only" | owner sentence missing |
| s5-no-deadline | runtime: "neither stops nor retries a session" -> "never stops a session" | owner sentence missing |
| prepare | runtime: drop "boot-wedge relaunch" | owner sentence missing |
| recheck-age | runtime: "0 to 5 s" -> "0 to 10 s" | owner sentence missing |
| activation | runtime: "health alone is not activation" -> "health is activation" | owner sentence missing |
| pin-dropped | runtime: remove the `C1153_Certificate_age_is_monotonic` pin | pin must follow |
| runner-pin-renamed | runner test source: rename `C1153_Closed_identity_refuses_delayed_creation` | runner pin names no test method |
| route-map-line | antiphon-api: delete the certify route-map line | route map: certify |
| api-prefix-in-ops | ops-http: write `/api/sessions/{id}/absence-evidence` | no /api absence route |
| plan-false-s5 | runtime: add the plan's boot-stall-retry sentence | CARD-1151 retired the boot retry |
| literal-key-path | agent-credentials: add a literal Windows key path | no literal key or fleet path |
| freshness-30s | runtime: "within 30 s" -> "within 60 s" | request freshness window |
| closed-type | antiphon-api: drop `phone_home_session_identity_closed` | route map closed type |
| op-number | antiphon-api: `CertifyAbsence` (39) -> (40) | phone-home certify operation |

## Final group

The run table below is this task's closed list: CP-26..CP-34 copied verbatim from the test design
(identical filters), plus CP-45..CP-50 on the same two builds: the registry guards
(`TestClassificationGuardTests` in both test projects, `SlowTestTripwireTests`) and the existing
doc-pin classes that read the four documents S5 edits. `StandingBootDocumentationTests` reads
`session-runtime-invariants.md` but is a CARD-1156 S6 skeleton that skips by design, so it is not a
row. The whole Unit lane was not run (AGENTS.md; brief).

Windows handoff (not run here, not passed): CP-35 `windows-generation`
`/*/*/RunnerSessionGenerationTests/*` (R-9, 5) and CP-36 `windows-compaction-stop`
`/*/*/CompactionContinuationStopTests/*` (R-5, 6) need a Windows runner (both launch `cmd.exe`, no
platform skip). Run them as a separate Windows-pinned task with
`--plan docs/superpowers/plans/2026-10-08-card-1153-test-design.md --rows CP-35,CP-36` (`-Platform Windows`, no host pin).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-26 | all | `tests/Antiphon.Tests -> bin-c1153-final-server/` | portable-s1-regression | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_*` | R-1 | all C1149 methods and arguments, 0 failed/skipped | 49 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | all | `CP-26` | portable-whitelist | `/*/*/AbsentLaunchPolicyTests/*` | R-2 | 77 executed, 0 failed/skipped | 77 | 1 | true | n/a |
| CP-28 | all | `CP-26` | portable-generation-wire | `/*/*/SessionRunnerGenerationWireTests/*` | R-3 | 9 executed, 0 failed/skipped | 9 | 1 | true | n/a |
| CP-29 | all | `CP-26` | portable-boot-stall | `/*/*/BootStallWorkingTickCharacterizationTests/*` | R-6 | 3 executed, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | all | `CP-26` | portable-brief-recovery | `/*/*/DelegationBriefRecoveryTests/*` | R-7 | 3 executed, 0 failed/skipped | 3 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | all | `CP-26` | portable-budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets*` | R-8 | baseline 18/18/4 plus any landed S2 arguments, 0 failed/skipped | 3 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | all | `CP-26` | portable-docs | `/*/*/SessionRunnerAbsenceEvidenceDocumentationTests/C1153_Owner_sentences_match_the_protocol*` | V-21 | 1 executed, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-33 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1153-final-runner/` | portable-launch-generation | `/*/*/PhoneHomeCommandDispatcherTests/Duplicate_launch*` | R-4 | all 3 duplicate-launch methods, 0 failed/skipped | 3 | 5 | true | n/a |
| CP-34 | all | `CP-33` | portable-release-generation | `/*/*/PhoneHomeCommandDispatcherTests/(Pre_ack_resend*)\|(Launch_of_an_exited_session_under_a_new_generation_still_relaunches*)` | R-4 | all 3 methods, 0 failed/skipped | 3 | 1 | true | n/a |
| CP-45 | all | `CP-26` | portable-guard-classification | `/*/Antiphon.TestSupport/TestClassificationGuardTests/*` | registry guard | all, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-46 | all | `CP-26` | portable-guard-slow | `/*/Antiphon.Tests.TestHelpers/SlowTestTripwireTests/*` | registry guard | all, 0 failed/skipped | 2 | 1 | true | n/a |
| CP-47 | all | `CP-26` | portable-doc-pins | `/*/Antiphon.Tests.Application/(BootStallDocumentationTests*)\|(RunnerBranchContractDocumentationTests*)\|(RepairSourceDocumentationTests*)\|(BlockedTaskParkProjectionTests*)\|(DelegationCapabilityContractTests*)\|(CommitOnSettleDocumentationTests*)\|(PipelineDefinitionDocumentationTests*)/*` | doc pins | 18 executed, 0 failed/skipped | 18 | 2 | true | n/a |
| CP-50 | all | `CP-26` | portable-doc-pins-infra | `/*/Antiphon.Tests.Infrastructure/(DockerStackDocumentationTests*)\|(RunnerPushCredentialDocsTests*)/*` | doc pins | 15 executed, 0 failed/skipped | 15 | 1 | true | n/a |
| CP-48 | all | `CP-33` | portable-guard-classification-runner | `/*/Antiphon.TestSupport/TestClassificationGuardTests/*` | registry guard | all, 0 failed/skipped | 1 | 1 | true | n/a |
| CP-49 | all | `CP-33` | portable-doc-pins-runner | `/*/*/PushProbeOutcomeTests/*` | doc pins | all, 0 failed/skipped | 5 | 1 | true | n/a |

## Pending

Every PC stays pending for post-land SourceLanding Mutation. For this slice: PC-29 (5 documentation
controls, one per owner sentence). PC-1..PC-28 and PC-30..PC-33 remain pending from S1-S4.

## Repair F1 (Code task e8c3f70b, after Review b588c5c7)

Review b588c5c7 F1: the runtime owner said the runner writes an Attempted marker before the first
provider effect, unconditionally. `RunnerAbsenceEvidenceService.RecordCreationAttempt` catches an
`IOException` or `UnauthorizedAccessException` from that write after a positive admission read,
latches evidence unavailable for the runner epoch and returns, so the launch proceeds; a store that
turns unknown at the write still refuses (`SessionIdentityClosedException`).
`RunnerAbsenceEvidenceRuntimeTests.C1153_Store_failure_disables_proof_without_stopping_work`
asserts the provider effect ran, the latch was set before it, the Prepared bytes are unchanged,
`AbsenceEvidenceReady` is false and a later certify answers `Unavailable` ("latched"). The owner
sentence now states that exception, is followed by that test and
`C1153_Creation_consumes_proof_before_effects`, and V-21 pins it (`MarkerSentence`) and rejects the
marker promise without ", except when" in any wrapping. No production change.

Absolute-word pass over every sentence S5 added or edited (file:line at the repair tip):

| Site | Absolute | Verdict |
|---|---|---|
| runtime:452 bullet title | only | True: the hold needs a validated certificate (`DecideAbsentLaunchAsync` -> `TryHoldAbsentLaunchAsync`). |
| runtime:455-458 | zero, only when | True: necessary conditions; the hold stages Blocked without a retry. |
| runtime:458-461 | before any, every other | Qualified: a session row gone when the decision reads it is also withheld (code-only, unpinned), and inventory/Working are re-checked when the pre-screen or certificate does not qualify. |
| runtime:462 | only | True: only `RunnerAbsenceEvidenceValidator`'s Proven result is a certificate. |
| runtime:468 | one, never, never | Qualified: "sends at most one prepare"; an old runner, a missing server key or an unknown/mismatched store sends none (`remote-old-runner` argument, client `Unsupported` arms). "Never gates" stays: only the dispatcher's own cancellation propagates. |
| runtime:470-473 | only after, once | True: one `ReadAbsenceCertificateAsync` per decision, after the inventory and pre-screen. |
| runtime:474, 477-478 | anything else | True (Review row; `C1153_Certificate_age_is_monotonic`). |
| runtime:479 | never stops | True: certify refuses a known identity; nothing in it stops a process. |
| runtime:484 | before the first provider effect | F1: qualified with the write-failure exception and pinned. |
| runtime:487-489 | every generation | True: `C1153_Closed_identity_refuses_delayed_creation` (newer-generation start, attach). Adoption is not a creation and is not claimed. |
| runtime:490-492 | once and only when | True: necessary conditions; other refusals (clock step-back, full cache) are 503. |
| runtime:493 | no, neither | True (Review row; CP-29). |
| runtime:497 | no prepare, alone | True: the client checks the feature before any POST/frame. |
| credentials:223 | only | True: prepare exists only for certification; launches are unchanged. |
| credentials:233-235 | never | True: `TryLoad` problems name only the failure type; the runner logs the derived key id. |
| credentials:236-238 | every | True: the response MAC covers the SHA-256 of the whole body, the nonce and the status. |
| credentials:239-242 | neither | Qualified: the runner-side and server-side missing key were conflated; a server without a key sends neither over HTTP even to an advertising runner (`absence_evidence_key_unconfigured`, code-only). |
| credentials:243-244 | no delegate | Policy statement, not a code claim. |
| api:917-918 | only | True: the one prepare call site and the one certify call site. |
| api:921-922 | every | True: the handler sets `no-store` before reading the body; a non-GUID id never reaches the route. |
| api:925-928 | 404/409/400/401/503 | True against `Certify`, `AdmitOnceLocked` and the route handler. |
| api:928-929 | only when | True: `Program.cs` needs `AbsenceEvidenceReady` and the key; phone-home needs a ready store. |
| ops-http:58-63 | only, no proxy | True: no server route maps absence evidence. |
| this note, decision 2 | every | Qualified: the released-seat exclusion is unpinned and the marker exception was missed. |
| this note, sweep | no other | Qualified: F1 was found later. |

Red checks for the repaired pin (not PCs; one probe build `tests/Antiphon.Tests -> bin-c1153f1mut/`,
UseAppHost=false, 115 s under lease, deleted afterwards; method filter
`/*/*/SessionRunnerAbsenceEvidenceDocumentationTests/C1153_Owner_sentences_match_the_protocol`;
each mutant its own results directory, restored with `git checkout -- docs`, tree clean after each):

| Case | Mutation | Result |
|---|---|---|
| green-before | none | 1/1 passed |
| restore-old-sentence | runtime: the S5 marker sentence and its pins put back verbatim | 1 failed: owner sentence missing (marker) |
| old-beside-new | runtime: the unconditional marker wording added beside the new sentence | 1 failed: the marker promise must carry its write-failure exception (F1) |
| drop-store-failure-pin | runtime: remove the `C1153_Store_failure_disables_proof_without_stopping_work` pin | 1 failed: pin must follow |
| prepare-unconditional | runtime: "sends at most one prepare" -> "sends one prepare" | 1 failed: owner sentence missing (prepare) |
| green-after | none | 1/1 passed |

## Repair 2 (Code task f494c857, after Review e4ac990f): shrink the claims

Third documentation round. Every S5 sentence in the four owner documents was re-read clause by
clause against the code and a named test; a clause with no test was removed, and detail was
replaced by a pointer to the code or test name. No production change, no migration; the 15-row
behavioural evidence (run 20261009-022103-a32e at `eec04695`) stands on a compile-identical
production tree (this repair edits only documents and the V-21 test).

**F1 (marker).** The marker sentence said a Prepared record stays on disk after a failed marker
write. False: the atomic replacement can succeed before the directory sync throws, so Attempted
is present then (`C1153_Store_failure_disables_proof_without_stopping_work` checks only the case
where the write itself fails). The sentence now says only: the marker is written before the first
provider effect; when that write fails the launch may still proceed and absence evidence is
unavailable for the rest of that runner epoch, so no certificate can be formed. It names no
record and no disk state. V-21 `MarkerSentence` requires exactly that text; `UnconditionalMarkerPattern`
rejects the marker promise without "; when that write fails,"; `DiskStatePattern` rejects
`Prepared ... stays|remains|is left|is kept` and `on disk` anywhere in the CARD-1153 bullet.

**F2 (withholding list).** "Every other shape keeps the existing failure" was not exhaustive: a
listed owning inventory withholds. The runtime owner now states a closed list (V-21
`WithholdSentence`; the old fallback is asserted absent), checked against
`AgentTaskDispatcher.DecideAbsentLaunchAsync` and `HoldUnderLockAsync`:

| Shape (leaves the task untouched) | Code (`AgentTaskDispatcher.cs`) | Test |
|---|---|---|
| owning runner lists the session (running, starting, exited, other generation) | `ReadAbsenceAsync != Positive` -> `Withheld` (decision and under lock) | `C1149_Listed_or_unknown_runner_is_never_absence` (listed-*, wrong-generation) |
| owning inventory unavailable | same | `C1149_Listed_or_unknown_runner_is_never_absence` (unavailable-inventory), `C1153_Working_or_unknown_inventory_withholds` |
| task Working or transcript Working | `expectedStatus == Working \|\| IsWorkingAsync` (decision, post-pre-screen, under lock) | `C1149_Changed_or_working_attempt_is_untouched`, `C1153_Working_or_unknown_inventory_withholds` |
| session row gone | `expected is null` -> `Withheld`; `fresh is null` under lock | unpinned |
| task or session row changed before the hold commits | task status/attempt/session/dispatchedAt or session status/reason/startedAt differ under lock | `C1149_Changed_or_working_attempt_is_untouched` (attempt-rebind), `C1149_Failed_hold_does_not_persist_on_a_later_save` (session reason changed) |
| certificate no longer holds under the lock | `AbsenceCertificateHolds` false -> `Withheld` | `C1153_Final_certificate_is_revalidated_under_lock`, `C1153_Certificate_age_is_monotonic` |
| hold does not commit | `catch` -> `DiscardUncommittedHoldAsync`, `Withheld` | unpinned |

Any other shape is "decided by the existing failure rules" (e.g. a missing or bad certificate,
which `C1153_Real_client_bad_evidence_keeps_failure` pins as Failed).

### Second pass: every S5 sentence (file:line at the repair-2 tip)

| Site | Kept | Removed |
|---|---|---|
| runtime:452-456 | CARD-1149 hold conditions (runner-unknown reason, positive absence, `AbsentLaunchPolicy`, native-empty); pointer to `DecideAbsentLaunchAsync`. Pinned by the CP-26 C1149 rows (`C1149_Absent_launch_is_blocked_with_original_input`, `C1149_Whitelist_gap_keeps_previous_failure`, `C1149_Unknown_native_evidence_is_not_unattempted`). | `StartingGraceMs` (90 s), `DeadSessionFailGraceMinutes` (3 min) and their timeline (CARD-1161 owns the grace); "original input kept, zero automatic relaunch" (moved to the no-relaunch sentence's pin). |
| runtime:457 | closed withholding list (F2), fallback "decided by the existing failure rules". Pins 458-463; two shapes named unpinned. | "checked before any certificate request, and inventory and Working again when ... does not qualify" (ordering detail; the code is the reference). |
| runtime:464 | certificate sentence unchanged (fresh: monotonic/age rows; authenticated: `C1153_Http_authentication_covers_request_and_response`; same session/generation/store: revalidation rows; 404 and empty transcript: `C1153_Real_client_bad_evidence_keeps_failure` plain404, empty-transcript). | none |
| runtime:470 | at most one prepare, after the claim commit, before the launch is enqueued; warm reuse, boot-wedge relaunch, interrupted-launch resume send none; a failed prepare does not stop the launch (`C1153_Only_new_cold_dispatch_prepares_evidence` cold-fresh, warm-reuse, recovery, resume, prepare-faulted). | "either launch sink" (only the local enqueue order is asserted), "unsupported or slow" (slow is unpinned; unsupported moved to the old-runner sentence), the released-seat exclusion sentence (code-only, unpinned). |
| runtime:472-473 | requested only after the database pre-screen, under one deadline covering capability discovery (`C1153_Nonpristine_brief_never_closes_identity`, `C1153_Deadline_covers_capability_discovery`). | "once per decision", "fresh nonce", "one transport attempt", "five-second" (no test names them on this path). |
| runtime:476 | recheck: locked row's session/generation/store, monotonic age 0..5 s, wall deadline; failure withholds (`C1153_Final_certificate_is_revalidated_under_lock` changed-generation, store-or-epoch-change, expired-proof; `C1153_Certificate_age_is_monotonic`). | "queue gate", "the next due pass asks the runner again", and the wall-jump/rollback sentence (restated the rule; the test is the reference). |
| runtime:479-481 | closes against a delayed start or attach; prior-epoch or other-store records never renewed (`C1153_Restart_or_store_change_never_renews_proof` prepared-prior-epoch, closed-prior-epoch, changed-store; `C1153_Closed_identity_refuses_delayed_creation`; race row); closed-id refusal types (`C1153_Closed_identity_refuses_delayed_creation`; phone-home type asserted in `C1153_Authenticated_operation_preserves_binding`). | "never stops a process", "old IDs ... remain unknown", "for every generation", "refuses creation when its evidence store is unreadable or damaged", "a task whose runner restarted ... keeps the existing failure" (none pinned by a named test here). |
| runtime:486 | marker sentence (F1). | "under the launch gate", "I/O or access error after the admission read", "any Prepared record stays on disk", "stops advertising" |
| runtime:489 | admitted once, signed issue time within 30 s (`C1153_Certify_requires_every_fact` stale-issued-at, `C1153_Replayed_request_is_rejected`). | "clocks must agree within 30 s" (derived), "a request issued within 30 s of a runner start is refused" (unpinned). |
| runtime:492 | no automatic relaunch; CARD-1151 boot-stall neither stops nor retries (`C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero`, `C1153_Real_client_certificate_holds_original_input`, the CP-29 boot-stall row). | "input-wait release deadline", "parking remains disabled by default (CARD-1083)" (owned by CARD-1083; not this card's test). |
| runtime:496 | a runner without `sessionAbsenceEvidenceV1` gets no prepare and the absent launch keeps the existing failure (`C1153_Only_new_cold_dispatch_prepares_evidence` remote-old-runner, `C1153_Real_client_bad_evidence_keeps_failure` old-runner). | "Activation is runners first, then AppHost", "health alone is not activation", the `GET /api/session-runners` check (not pinned). |
| ops-http:58-63 | routes sentence (pinned); signed with the key; offered via `sessionAbsenceEvidenceV1` on `GET /capabilities` (`Program.cs` capability gate; key-less runner asserted in `C1153_Http_authentication_covers_request_and_response`). | "server-internal", "not a curl probe", "no /api proxy", the phone-home `/api/session-runners` clause. |
| api:897-898 | route-map lines (V-21; "fresh, never-used": `C1153_Prepare_records_only_a_fresh_identity`). | none |
| api:913-922 | routes sentence and pins; HMAC-SHA256 both directions (`C1153_Http_authentication_covers_request_and_response` wrong-request-MAC, altered-response-bit); closed-id types; phone-home operations 38/39 (V-21 against the enum); pointer to `AbsenceEvidenceRoutes`/`RunnerAbsenceEvidence` and `RunnerAbsenceEvidenceContractTests`. | the wire summary: version 1 body fields, 32-byte nonce, `no-store`, the 200 field list, 404/409/400/401/503 mapping, the advertise rule, "prepares only the session id a cold dispatch just allocated" (runtime owns it). |
| credentials:221-236 | credential sentence and pins; setting name, same file content, base64 of at least 32 bytes (V-21 against `MinimumKeyBytes`; `AbsenceEvidenceAuthentication` decodes base64); custody statement; HMAC both directions and key-less runner does not advertise; no key bytes or path in a response or captured log (`C1153_Http_authentication_covers_request_and_response` canary); operator-only provisioning (policy). | "read the file but do not check its permissions", argv/environment/capabilities-DTO/provider-environment claims, "logs only a derived key id", "versioned, domain-separated encoding", "covers every evidence field and the request nonce", the server-side missing-key clause, "launches still proceed", the restart order (runner first, AppHost second). |
| this note, Change and Decisions 2-3, Repair F1 | historical record of what S5 and Repair F1 wrote. | Superseded by this section: decision 3's released-seat sentence, the wire summary, the operator restart order and Repair F1's "Prepared bytes are unchanged ... the owner sentence now states that exception" are no longer owner text. |

### Repair 2 verification

Checkpoint run `20261009-024609-3d19` at `04f5c299332e22caaff28aefa9f0fa0d959a9941`, bound (task
token present), `--serial`, scratch plan of the Review's six rows with identical filters (CP-32,
CP-45, CP-46, CP-47, CP-50 on one `tests/Antiphon.Tests` build; CP-901 on one
`tests/Antiphon.SessionRunner.Tests` build), UseAppHost=false (tool default off Windows): 6 green,
38 tests (1+1+2+18+15+1), 0 failed/skipped, `unlisted: none`, wall 2m28s, slot waits 0 s;
`validate`: CHECKPOINT SOURCE VALID rows=6. The whole Unit lane was not run (AGENTS.md; brief).
The 15-row behavioural run `20261009-022103-a32e` at `eec04695` stands: no production or
behavioural test file changed since.

Unlisted builds (both via `build-slot.ps1`, waited 0 s, deleted afterwards): `tools/Antiphon.Checkpoints
-> bin-c1153r2drv/` (6 s, tool bootstrap); `tests/Antiphon.Tests -> bin-c1153r2mut/` (110 s,
UseAppHost=false) for the V-21 red checks below (not PCs; method filter
`/*/*/SessionRunnerAbsenceEvidenceDocumentationTests/C1153_Owner_sentences_match_the_protocol`;
each mutant restored with `git checkout -- docs`, tree clean afterwards):

| Case | Mutation (runtime owner) | Result |
|---|---|---|
| green-before | none | 1/1 passed |
| old-disk-wording | marker sentence back to "proceeds, any Prepared record stays on disk" | 1 failed: owner sentence missing (marker) |
| disk-claim-beside | "The Prepared record stays on disk." added in the CARD-1153 bullet | 1 failed: no record or disk-state claim (e4ac990f F1) |
| old-fallback-beside | "Every other shape keeps the existing failure." added beside the new list | 1 failed: the fallback is not exhaustive (F2) |
| drop-listed-shape | listed/unavailable inventory removed from the closed list | 1 failed: owner sentence missing (withhold) |
| drop-listed-pin | `C1149_Listed_or_unknown_runner_is_never_absence` pin removed | 1 failed: pin must follow |
| unconditional-marker | marker sentence without its failure clause | 1 failed: owner sentence missing (marker) |
| fallback-reverted | "Any other shape is decided by the existing failure rules." -> the old fallback | 1 failed: owner sentence missing (withhold) |
| green-after | none | 1/1 passed |

PC-29 (documentation controls) and every other PC stay pending for post-land SourceLanding Mutation.
