# CARD-0415 continuation evidence (in progress)

This is the incremental evidence ledger for task `fad77641`, continuing the S1
handoff in `2026-09-09-card-0415-follow-up.md`. S2–S6 are **not yet accepted**.
No deployment or live-provider qualification has been performed. Codex remains
PendingDependency CARD-0167; its deferred agent-path acceptance gate is unchanged.

Worktree: `C:\Antiphon\worktrees\card-task-2ddbfaf6`, branch
`feat/card-task-2ddbfaf6`. The shared main checkout and production runner were not
used for test launches. CARD-0412 was integrated from master (`e138c41b` is its
implementation reference); Attention retained CapacityRecoveryExhausted=32 and
appended StandingSpecialistHealth=33.

## Recorded passing observations

All paths below are absolute after expanding these evidence roots:

- L = `C:\Antiphon\worktrees\card-task-2ddbfaf6\.antiphon\acceptance\fad77641`
- T = `C:\Antiphon\worktrees\card-task-2ddbfaf6\tests\Antiphon.Tests\bin-c415-followup\TestResults`
- U = `C:\Antiphon\worktrees\card415-s3-fad\tests\Antiphon.Tests\bin-c415-seats\TestResults`

| Area / exact class or method | Executed / passed / failed | Artifact | Limits |
|---|---|---|---|
| V4, `SpecialistInputTransportTests` | 11 / 11 / 0 | T/c415-fad-transport-cases.trx | Synthetic native receipts; complete envelope, UTF-8, reload, no batching, damaged records, generation drift. |
| V2/V3, `CheckSpecialistLaunchPolicyTests` | 18 / 18 / 0 | T/c415-fad-launch-policy-green.trx | Before the final isolated settings/MCP flags; final launch unit regression still due. |
| V5, native CLI envelope method | 2 / 2 / 0 | T/c415-fad-cli-envelope-ready.trx | Actual owned Modern ConPTY Claude launch, 8191/8192-byte native full-prompt equality. Before final MCP isolation flags. |
| V5, native CLI tool methods in `Agents.SpecialistToolPolicyTests` | 8 / 8 / 0 | T/c415-fad-cli-tools-isolated.trx | Read/Write/Bash/MCP, protected plus unprotected controls. Final isolated flags, 8192-byte input each. Network/search, complete finite grid and warm/resume still due. |
| V13/V25, `StandingSpecialistSeatTests` | 7 / 6 / 1 initially; corrected exact method 1 / 1 / 0 | U/c415-fad-seat-lifecycle.trx; U/c415-fad-seat-shape-green.trx | Initial failure was null versus empty-object environment assertion. Six other cases passed. Not the entire lifecycle acceptance matrix. |
| V16/V17, `SpecialistHealthAttentionTests` | 4 / 4 / 0 | U/c415-fad-health.trx | Pure clock/reset rules plus durable Attention fixture; full request-driven matrix still due. |
| V7, `SpecialistAttemptEvidenceTests` | 12 / 12 / 0 | T/c415-fad-attempt-evidence.trx | Native-turn validator units; these do not claim actual model qualification. |
| V7, `SpecialistQualificationTests.Card0415_V07_authorized_batch_uses_real_dispatch_queue_and_settlement_once` | 2 / 2 / 0 | T/c415-fad-qualification-native-clock.trx | Injected external evidence/model I/O only; actual request producer, dispatcher, full-inline queue, timestamped native records and reply settlement. Successful batch once; wrong semantic answer quarantines, stops batch and cannot reset logical health. |
| V7, `SpecialistQualificationTests.Card0415_V07_production_verifier_refuses_synthetic_qualification_provenance` | 1 / 1 / 0 | T/c415-fad-qualification.trx | The other two cases in that initial run failed harness setup. Subsequent harness attempts exposed omitted native prompt timestamps; no setup failure is counted as a positive control. |
| V14 publication boundary, `SpecialistPublicationTests.Card0415_V14_publication_rolls_back_and_two_queue_instances_reuse_one_durable_identity` | 1 / 1 / 0 | T/c415-fad-publication.trx | Injected write failure rolls back note/timeline/publication bit; two real queue instances retry the same logical publication once. This is a publication-boundary integration test, not full end-to-end Check acceptance. |
| CARD-0412 regression, `CapacityRecoverySupervisionTests.Card0412_D8_stalled_standing_start_rearms_and_actually_starts_again` | 1 / 1 / 0 | T/c415-fad-capacity-handoff.trx | Matching rearmed grant actually starts; full S5b lock/concurrency acceptance still due. |
| `SpecialistRoutingPanel.test.tsx`, `attentionVisuals.test.ts` | 15 / 14 / 1 initially | L/client-routing.log | Stale-edit test had wrong fireEvent import; dependency UI and all 13 visual cases passed. |
| Exact client case `retains the edited revision` | 1 / 1 / 0 (one other case filtered out) | L/client-stale-green.log | Corrected import; actual requests preserve stale revision during query refresh and require explicit reload. |
| Client TypeScript + Vite build | exit 0 | L/client-build.log | Built in the extra isolated checkout; ordinary chunk-size warning only. |

The V5 stub endpoint is synthetic and isolated. Passing those cells does not
certify model behavior. The production capability catalog is intentionally empty
until the complete finite capability matrix is measured; there is no API switch
that installs synthetic behavioral qualification.

The expanded finite grid (`U/c415-fad-cli-grid.trx`, `L/cli-grid.log`) executed
12 cases: 2 passed (the M+1 refusal arms), 10 failed. Small first-turn receipts
were retained in `L/grid`, but warm repeats and larger complete envelopes did
not satisfy native full-prompt/turn-end confirmation. Proposed M=32768 is **not
certified**. A separate exact warm-small diagnostic is being investigated;
these failures are measured capability gaps, not positive controls or accepted
grid coverage.

## Positive controls completed in this pass

`L/run-s2-controls.ps1` runs exact methods, verifies expected assertion failures,
restores the original source bytes and rebuilds the same method green. Results
including source SHA and TRX filenames are in `L/s2-controls.jsonl`.

| Control | Mutation | Red failed / executed | Restored passed / executed |
|---|---|---|---|
| PC9 dispatch | Remove typed full-inline dispatcher branch | 2 / 2 | 2 / 2 |
| PC9 queue | Remove typed second-spill bypass | 2 / 2 | 2 / 2 |
| PC10 bytes | Count characters instead of UTF-8 bytes | 1 / 1 | 1 / 1 |
| PC10 boundary | Reject equality (`>= M`) | 1 / 2 | 2 / 2 |

These controls ran at `69d40389`. No source mutation remains active. Compiler
errors, fixture setup failures and filtered/skipped cases were not treated as
positive controls. The original handoff retains S1 control evidence; all other
PC rows remain pending unless a later entry below explicitly records their red
and restored-green artifacts.

## Implementation checkpoints

- `18a5ab59`–`531a704f`: full-inline transport, strict Check launch policy and
  owned real-CLI stub canaries; complete native input confirmation survives retries.
- `3093f0d2`, `68fcf5d1`: typed owner/alternate lifecycle, separate scratch seats,
  launch barriers, primary identity preservation and lifecycle fixtures.
- `a00f0441`, `21532a59`: durable requests/attempts/health schema, Attention
  projection and health clock/reset tests.
- `f20064c1`, `37fad6aa`: routing settings UI and stale-write/dependency tests.
- `fa8bc35e`: durable Check attempt producer and native winner evidence; expiry
  at dispatcher/queue boundaries; provider redemption and start moved outside the
  standing supervisor's consumer transaction. Generated launch-evidence migration.

The qualified execution producer still needs its complete two-instance,
publication, cancellation, calibration, current-generation and failure matrix.
The production capability catalog and authenticated acceptance remain unaccepted.
Further worker/qualification changes after these checkpoints must be validated
before they are claimed here.

## Remaining acceptance

The full V1–V26, R1–R15 and PC1–PC59 matrix remains authoritative in the plan and
the preceding handoff. This ledger adds evidence only for the cells above. In
particular, partial V5 transport/tool-family success is not V5 certification;
pure health and reading rules are not the full service-graph V6–V17 acceptance;
the CARD-0412 regression is not the complete specialist S5b integration matrix.

Still required: full Claude V5 finite grid/tool families/fresh and warm generations,
actual qualification and request service graph, atomic lifecycle edits/deletion,
durable exactly-once caller publication and late-result ownership, calibrated
deadline evidence, S5b mixed-consumer concurrency, remaining positive controls and
applicable authenticated Claude V26 acceptance. Keep the Codex cells pending
CARD-0167 instead of bypassing the prerequisite or silently substituting a provider.

## Warm confirmation, owned lifecycle and request order (fad77641)

- `c415-fad-cli-warm-small-fixed.trx`: 1/1 passed, native Claude fresh then warm
  synthetic prompts. The prior failed run contained the complete warm UserPrompt
  at sequence 4 despite NoTranscriptRecord. Full-inline confirmation now pulls
  the native transcript even when earlier session history is observable.
- `c415-fad-warm-pull.trx`: 1/1 passed with a missing live transcript stream.
- `c415-fad-qualified-check.trx`: 1/1 passed: two semantic qualification turns,
  real request/dispatcher/queue/native evidence/reply settlement, one winning
  real-purpose Check, and duplicate-request reuse without another task.
- `c415-fad-identity-edit.trx`: 3/3 passed (Dispatched/Working/Blocked).
- `c415-fad-delete-ownership.trx`: 2/2 passed (live session or queued owned task).
- `c415-fad-health-order.trx`: 2/2 passed. Persisted LastRequestStartedAt prevents
  an older completion from resolving a newer outage or reopening newer recovery;
  generated migration `20260909030639_OrderSpecialistHealthRequests`.
- Extra-checkout `c415-fad-cli-grid-fixed.trx`: 12 executed, 8 passed, 4 failed,
  no skips. Failing rows: small, ASCII M, ASCII M-1, multibyte M-1. Native
  transcripts in several failures order AssistantText/TurnEnd before UserPrompt.
  This is not a complete capability certificate. Provisional M=32768 remains
  uncertified. Full bounded facts and multibyte M passed fresh and warm.
  Raw owned synthetic evidence: `.antiphon/acceptance/fad77641/grid-fixed/`.

The native-order investigation and mutation controls for these new guards are
still ongoing. These results do not release V5/V26 or the Codex dependency.

## Native ordering gate and new mutation controls

Claude Code 2.1.263 executable SHA-256:
`0B35DF94C1307004F07B738390BFEF8DFCA5E9AF29AAF6517F305BF086B95B03`.
The repeated `c415-fad-cli-native-grid.trx` executed 12 cases: 7 passed, 5 failed,
zero skips. Failure rows vary between runs, so isolated passing repeats cannot
certify this combination. The native JSONL itself contains assistant/system before
user/attachments; the assistant parent chain eventually connects to that user.
The existing transcript consumers assign sequence in file order. Do not work
around this by treating screen evidence or task Result as delivery/qualification.
A sanitized structural excerpt is checked in at
`docs/investigations/fixtures/card-0415-claude-2.1.263-out-of-order.jsonl`.
Its user body is omitted; full-byte synthetic originals remain under
`.antiphon/acceptance/fad77641/native-grid/`. The 32768-byte proposal is uncertified.
V26 authenticated model spend remains gated off.

At `5ed99208`, exact-method mutation controls completed:

| Control | Mutation | Red | Restored green |
|---|---|---:|---:|
| PC12 warm receipt | full-inline warm confirmation stops pulling native transcript | 1 failed | 1 passed |
| PC24 active edit | active managed identity refusal disabled | 3 failed | 3 passed |
| PC36 older health | older request ordering guard disabled | 2 failed | 2 passed |

All sources were restored byte-for-byte with fresh timestamps. Red failures were
expected assertions, not build/fixture errors. Ledger and command script:
`.antiphon/acceptance/fad77641/continuation-controls.jsonl` and
`run-continuation-controls.ps1`.

## Current-result and declared-fallback graph

- `c415-fad-current-completion.trx`: 3/3 passed (newer Check number, replaced
  generation, live-caller evidence timeout). Stale native completion cannot win;
  an HTTP-style TaskCanceledException with a live caller is a transient outcome.
- `c415-fad-fallback-graph-fixed.trx`: 1/1 passed. Primary and declared alternate
  each earn two synthetic semantic qualification turns through the real graph;
  the primary's invalid real-purpose reading quarantines it; the alternate wins
  the same logical request/deadline and health projects UsingFallback. The earlier
  failed fixture used ordinary JSON serialization instead of RoutingCandidate.Serialize.
- `c415-fad-lost-capability.trx`: 1/1 passed. Loss of verifiable capability removes
  Qualified before more paid work, with no extra task or synthetic-certificate transfer.
- `c415-fad-cancel-outcomes.trx`: 2/2 passed. Caller cancellation and host shutdown
  durably close pending attempts with distinct neutral outcomes and cancel only
  unsubmitted owned tasks. These rows do not claim active-process cancellation coverage.
- `c415-fad-launch-final.trx`: named launch/provisioner regression found four stale
  fixtures whose AlwaysOn=false now correctly hits the managed-owner guard first.
  Corrected the fixture to an authorized standing Check. The exact affected class
  rerun `c415-fad-launch-final-fixed.trx` passed 4/4.

Request admission now enforces the configured logical Check backlog under the
counted-task/owner locks, includes compaction settings/watermark in its execution
fingerprint, and rechecks the database generation after external evidence reads.
The full backlog/compaction/concurrency mutation matrix is still outstanding;
these additions are not a completed V8/V13/V14/S5b acceptance claim.

## Stop intent and launch regressions

`SpecialistStartIntentTests.Card0415_V22_human_stop_wins_against_queued_and_inflight_Check_launch`
passed 3/3 in `c415-fad-stop-intent-fixed.trx`: Stop before launch creates no
adapter; Stop during readiness kills the started owned adapter and retains the
suspension/audit; the authorized control reaches Running without input.
The initial before-launch fixture lacked a runner Kill response; it was corrected
with the existing FakeSessionRunnerClient. No real process is spawned by this test.

`c415-fad-start-regression.trx` passed 103/103 across the four named classes
AgentControlServiceIntegrationTests, AgentSessionLaunchFailureTests,
SpecialistToolPolicyLaunchTests and CheckSpecialistLaunchPolicyTests. This includes
the two owned settings/MCP-file tamper checks. Current capability verification now
requires both owned policy files to remain armed. Check launch rechecks human intent
before starting and after readiness; automatic start cannot lift a Check suspension.
A missing native Check resume target no longer silently starts a fresh conversation.
