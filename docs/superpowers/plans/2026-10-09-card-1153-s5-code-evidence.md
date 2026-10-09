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
   and is pinned by a named test that passed at S4 (the server C1153 rows) or S1-S3 (the runner rows).
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

No other false sentence was found.

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
