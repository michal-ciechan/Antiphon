# CARD-0395 verification and live acceptance

Status: **final deterministic checks in progress**. Mapped server live compliance, Herdr wire
compliance and Herdr live compliance pass with native full-read/ack/marked-settlement evidence.
The corrected deterministic sweep covers 570 passing cases across 34 classes; later focused
checks and initial failures are recorded below. PC-1 through PC-31 now have recorded assertion
failures, source restoration and green reruns for their listed mutations.

**Operator scope decision, refinement 20260906194334:** two-auto-compaction endurance is
deliberately removed from CARD-0395 acceptance and moved to a follow-up card. No follow-up
card identifier was supplied. No further live compaction attempts will run for this task.
The revised acceptance is deterministic V/R/PC verification plus the passing single live
real-model compliance gate. V-9 and its changed-revision live-resume experiment are historical
observations for the follow-up, not blockers for this card and not falsely marked passed.
The earlier authentication refinement was honored using `GROK_AUTH_PATH` to reference the
existing authenticated store from isolated homes; no auth file or token was printed or copied.

Compaction **did trigger**: inline revision 2 passed two genuine compactions and all five
checkpoints. File attempt 3 also passed all five initial/two-compaction checkpoints, with one
additional compact during refresh. Its final revised-resume oracle failed on a test-only SQL
translation (`jsonb ~~ jsonb`), after native resume/read/ack, before the final challenge. That
query now selects the exact launch refresh key. It has not been rerun live, per the scope decision.
The explicit endurance tests now require the separate `ANTIPHON_GROK_RULES_ENDURANCE_TESTS=1`
opt-in in addition to their live/headed opt-ins, so compliance opt-in alone cannot launch them.

Latest continuation checkpoint (supersedes pending statements in the chronological notes):

- Mapped live attempt 4 produced the correct report but failed the real Code worktree-progress
  guard because its task prohibited changes. The corrected task writes only `acceptance-result.txt`
  inside its disposable worktree; the guard remains enabled. Attempt 5 passed in 75.7455169 s
  body / 95.391 s TUnit, UTC 18:36:12.955–18:37:28.756, task
  `af1f407e-ba62-4e68-91b3-5fb65c2c3439`, native/session `4d3fc3c9-ae43-488b-86cb-78b8ba20cd9d`.
- Herdr wire attempts exposed fixture kind persistence, `-ReadOnly` overriding `-Shared`, and
  the native 1,000-line read cap. Corrected wire uses actual `-Shared` standing reuse and a second
  offset-1001 read. Wire passed UTC 18:54:27.957–18:54:42.376, session
  `c647f63e-ac51-45fd-a194-b2711313872f`, task `f63cbeba-521d-4aa2-bde5-4f7339e6539b`.
  Live passed UTC 18:55:07.908–18:56:32.917, session `24509666-15f3-41de-85ad-5c18e798b5d7`,
  task `b49e462b-f00b-4259-8215-73169ed25b1a`, pane `w6:p1` on the isolated
  `card0395-c7ff2da0` server. Logs retain all failed attempts as well as the green runs.
- Calibration attempt 4 observed native auto-compaction start but failed because the stub
  misclassified the summary request. Corrected matching uses the actual last user message's
  summary instruction. Attempt 5 passed: five real file reads, unchanged fixture usage
  (10 input / 5 output), supported native threshold 1%, observed native window 500,000,
  body 4.1919343 s / TUnit 19.840 s, UTC 18:30:35.922–18:30:40.124. Native session
  `b4988433-4595-40c9-84ad-8c6cb63fe1e2` emitted five distinct completed IDs ending
  `-9`, `-16`, `-23`, `-30`, `-37`. Tokens-before 13,343 and tokens-after 15 are native
  observations from the stub calibration, not real-model retention results. The selected
  unmodified native fixture and provenance are under `tests/Antiphon.SessionRunner.Tests/Fixtures/`.
- Regression sweep: server 395/399 initially passed, runner 166/166, Pty 5/5; zero skips.
  Two obsolete argv assertions were corrected (6/6 green, 18.012 s), card unsafe-profile
  preflight was fixed before card/session claim (14/14 green, 38.437 s), and a watchdog
  delay hook was scoped to its own session (73/73 green, 38.112 s). These corrected runs
  give passing coverage of the original 570 cases, without counting reruns twice.
  `AgentControlService` now also uses injected rules settings for its early resume preflight;
  its six-case focused resume rerun and the 32-case native fixture tailer rerun passed.
- Both endurance harnesses use separate native-auth homes, native automatic compaction only,
  the same 20% supported threshold, disjoint challenge keys, actual answer parsing, and a
  predeclared 120-minute ceiling per arm. A zero/multiple-boundary workload is inconclusive;
  file-arm pre-idle canary failure returns acceptance to Plan. No endurance pass is claimed here.

Durable continuation evidence is the adjacent `2026-09-06-card-0395-continuation-evidence.zip`;
the latest archive checkpoint/hash below supersedes earlier hashes of that evolving archive.
Checkpoint SHA-256: `8c3fb93abff8ec45b484d1529c4c43daec90d798a6d88f5bd329d438c6ba4fc3`
(Historical checkpoint; final archive hash below supersedes this value.)

### Endurance observations recorded before the final file rerun

The first inline experiment read four 1,800-line files and produced three native compactions
within one turn, so it could not measure a separate canary interval after each. It also exposed
a harness nonce matcher that did not recognize transport-spilled prompts. Native session
`d7a4697b-ccb0-4f96-9e5b-ae07bb727e91` completed IDs `-660`, `-1104`, `-1601`
(101,877→8,234; 102,383→8,530; 102,372→8,731 tokens). Its native observation span was
244.184 s. After the final native turn ended, only its verified test/native/host PIDs were
explicitly stopped; the run is aborted/inconclusive, not a TUnit pass or a positive control.

Revision 2 uses two files then one, with short relative paths so the real native UserPrompt
contains the challenge nonce. Both arms retain threshold 20% and 120-minute ceilings.
The inline run passed UTC 19:10:36.479–19:13:22.354, body 165.877719 s, TUnit 179.463 s,
native session `44e3e91b-febf-4f56-857d-3f5b8c42ef16`, IDs `-559` and `-851`
(103,435→8,137 and 118,341→9,363 tokens). All five canary checkpoints survived.
Baseline checkpoint labels `after-refresh` mean equivalent next-idle challenges: no file
receipt or refresh existed, which the harness asserts. System-bootstrap context is unobservable.

File attempt 2, UTC 19:13:46.007–19:18:56.134, body 310.1325324 s / TUnit 325.619 s,
passed initial and compact-1 pre-idle canaries. The model autonomously reread standing rules
inside its work turn after native boundary `e9cbe3ca-7913-4a40-b43b-0d166732eb12-596`
(109,306→7,488 tokens). The subsequent queued idle refresh itself compacted at `-1195`
(109,706→8,061). Actual failure: `session.GrokRulesState should be GrokRulesState.Ready
but was GrokRulesState.Pending`. The harness checked an acknowledged prior row before the
bounded follow-on completed. The correction waits for the barrier to be Ready and fails promptly
if it becomes Failed; it changes no production recovery behavior. Attempt 3 is separately logged.

Continuation worktree: `C:\Antiphon\worktrees\card-task-c7ff2da0`.
Continuation branch: `feat/card-task-c7ff2da0-verification`, starting at the complete
previous implementation tip `79edc300` without rewriting its commits.

Previous worktree: `C:\Antiphon\worktrees\card-task-dfc6f878`.
Branch: `feat/card-task-dfc6f878-implementation`, based on fetched `57db87f555173a13c9d1623bd28836479573e5a6`.
Initial slices: `a06242c0` typed payload/store, `402d1481` settings/effective budget,
`d6044934` durable barriers/refresh/FakeGrok E2Es, `8c0ec49b` dispatch fixtures/documentation.
No deployment, shared-stack restart, production runner calls, or shared database migration.
The migration was generated with EF CLI; isolated tests applied their normal test migrations.

## Implemented scope

- Full composed Grok text travels in optional typed launch contracts, with strict UTF-8 body
  validation, source-conflict checks, runner capability gating and a stable atomic file store.
  Shared CARD-0382 argv policy is unchanged. Actual argv budgeting includes the generated bootstrap.
- Receipt metadata flows through runner responses, PtyHost manifests and Herdr sidecars. Adoption
  verifies disk hash/count. Remote receipt validation does not stat the server filesystem.
- Session generation/hash/count/state and refresh queue identity/deadline/coverage/ack evidence
  are persisted. Ordinary flush and Now/send-now hold behind the barrier. Initial delegate briefs
  are constructed after acknowledgement; an advisory lock serializes recovery and initial enqueue.
- Matching owning UserPrompt, assistant acknowledgement and successful TurnEnd are required.
  Rules turns have explicit task settlement, deferred settlement, channel and boot-reply exclusions.
- Transactional compact trigger rows, replay deduplication, conservative one-follow-on loop limit,
  error incidents and periodic recovery are present. These tests seed normalized boundaries;
  they are not native compaction acceptance.
- Legacy resume/removal preflights retain history and require the explicit existing fresh action.
  Warm Grok pool reuse checks receipt revision and Ready state. No live migration was performed.

## Historical initial-slice gaps (superseded by resumed implementation evidence)

1. T-10 metadata ordering is incomplete: the file exists before launch, but normal PtyHost manifest
   and Herdr sidecar receipt persistence still occurs after native child/pane launch. Implement and
   fault-test the required durable pre-spawn receipt ordering and torn-metadata recovery.
2. Recovery needs the complete owned-startup failure matrix. A server restart after status became
   Running but before initialization ended uses the periodic recovery path; that path does not yet
   terminalize/reap a failed owned initialization through the failed-launch owner. A bad receipt
   can abort a recovery pass and delay other sessions. Deadline start currently follows first eligible
   queue attempt, and must be pinned against the required first-ready startup eligibility.
3. Complete the actual Herdr adoption/live-target checks, v1 native resume and policy-refresh tests,
   explicit artifact expiry, complete remote-path/malformed receipt matrix, and all direct-call
   body-validation/configuration boundaries. Card resume/control preflights still use default rules
   settings at some early checks; the adapter/client/runner use injected settings.
4. Complete the channel/late-chunk and queue bypass interleavings, delivery-attempt exhaustion reason,
   interrupted ordinary delivery ordering, all crash points, startup recovery isolation, and initial
   task/claim/card ownership assertions. A full subsequent tool read is not normalized as an evidence
   object; loop handling conservatively requests one follow-on instead of claiming read coverage.
5. Implement `GrokRulesDispatchAcceptanceTests`, `GrokRulesCompactionAcceptanceTests`, genuine 1.0.13
   auto-compaction fixture/provenance and the full V/PC matrices. No plan-specified live harness was
   authored or executed in this slice. Missing coverage must not be replaced with seeded success.

## Executed test suites

Final successful class-scoped runs below have zero failures and zero skips. Counts are tests,
not a claim that the broader V/R/PC contracts are all covered. TUnit duration excludes compilation.

| Project / filter class | Tests | Duration | Local log |
|---|---:|---|---|
| SessionRunner.Tests / `GrokRules*Tests` | 54 | 1.586 s | `.antiphon/card0395-runner-rules.log` |
| Antiphon.Tests / `GrokRules*Tests` | 19 | 26.183 s | `.antiphon/card0395-server-rules-final.log` |
| Antiphon.Tests / `GrokDelegateEndToEndTests` | 4 | 117.644 s | `.antiphon/card0395-e2e-rerun.log` |
| Antiphon.Tests / `GrokDelegateDispatchTests` | 28 | 20.213 s | `.antiphon/card0395-dispatch-rerun.log` |
| Antiphon.Tests / `DelegateBundleLaunchTests` | 16 | 22.379 s | `.antiphon/card0395-bundles.log` |
| Agents.Pty.Tests / `FakeGrokContractTests` | 17 | 10.059 s | `.antiphon/card0395-fake-contract.log` |
| Antiphon.Tests / `SessionMessageQueueGrokPtyIntegrationTests` | 4 | 44.748 s | `.antiphon/card0395-queue-pty.log` |

Selected suites total: 142 passed, zero failures, zero skips. Mutation runs are reported separately.

The server built successfully (zero errors; existing AgentService nullable warning). Test builds
also emit pre-existing nullable/TUnit warnings. Earlier corrections: E2E workspace validation failed
2 restored fixtures until explicit disposable `-Dir` was supplied; six dispatch tests retained old
refusal/legacy-pool expectations and were updated to the new contracts. A compaction test initially
missed its contracts import (build failure, not a positive control). One no-build invocation ran stale
tests (1 failure, 2 skips); one malformed OR filter ran zero tests. Neither is acceptance evidence.

Rerun each filter separately, sequentially between assemblies:

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokRules*Tests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokRules*Tests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokDelegateEndToEndTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokDelegateDispatchTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/DelegateBundleLaunchTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/FakeGrokContractTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/SessionMessageQueueGrokPtyIntegrationTests/*"
```

## Every V gate

“Partial” means related executable checks passed, while the complete named contract is unrun.
V-5 tests require a captured native ACP fixture; the current normalized-row unit/integration checks
cannot satisfy those gates. Native live compliance now passed; endurance remains separately reported below.

| Gate | Status / available evidence |
|---|---|
| V-1a | Passed listed composition matrix: `GrokRulesCompositionTests.Worker_and_stage_bundles_reach_typed_rules_payload_without_argv_text` (five roles), plus `DelegateLaunchArgvIntegrityTests` and dispatch/bundle suites. Original exact source-to-byte tests and PC-2/3/4 retained. |
| V-1b | Passed both-backend `GrokRulesCompositionTests.Standing_channel_composition_preserves_attachment_style_append_and_preamble_bytes`; actual Herdr standing/channel composition additionally passed V-8b/d. |
| V-1c | Passed `CardSpawnModelArgumentTests` (14 cases): file composition/barrier plus existing model/profile precedence and corrected unsafe-profile no-session refusal. |
| V-1d | Passed `GrokRulesFileStoreTests.Rules_content_preserves_control_newlines_unicode_and_tail_beyond_1000_lines`; native >1,000-line continuation and Herdr full-read gates also passed. Exact disk bytes remain the losslessness oracle. |
| V-1e | Passed `GrokRulesFileStoreTests` exact-byte/Unicode validation, `GrokRulesTransportCompatibilityTests.Invalid_body_is_refused_server_side_before_any_runner_request`, and `GrokRulesFileLaunchTests.Invalid_payload_refuses_before_session_registration_or_disk_effects`; PC-7 independently broke both boundaries. |
| V-1f | Passed `GrokRulesFileLaunchTests.Actual_argv_budget_includes_generated_bootstrap_before_materialization` and launch/composition budget checks; PC-8 red/green. Claude/Codex argv regression sweep passed. |
| V-1g | Passed store/launch bootstrap checks and real native V-8 reads. No literal-path expansion claim. `GrokRulesFileLaunchTests` owns the final effective-argv assertion. |
| V-1h | Passed `GrokRulesFileLaunchTests.Explicit_rules_conflict_and_unsafe_source_precedence_have_no_effects`, compatibility raw-argv matrix and dispatch bare/specialist controls; PC-9 red/green. |
| V-2a | Passed `GrokRulesFileLaunchTests.Herdr_receipt_is_durable_before_first_request_and_before_typing` and `Launch_failure_retains_pre_spawn_receipt_in_existing_manifest`; PC-13 broke both metadata paths. Actual Herdr launch/read passed V-8b/d. |
| V-2b | Passed atomic R1/R2 and replacement fault tests in `GrokRulesFileStoreTests`, corruption adoption cases in `GrokRulesAdoptionTests`, PC-10. The revised live-resume experiment is descoped V-9b. |
| V-2c | Passed six `GrokRulesStoreFailureTests.Storage_failure_refuses_before_any_child_or_pane_effect` variants; final payload/argv refusal zero-effects tests also pass. |
| V-2d | Passed `GrokRulesFileStoreTests.Shared_cwd_sessions_have_isolated_files_and_generations`; PC-14 cwd mutation red/green. |
| V-2e | Passed all four `GrokRulesAdoptionTests` variants on real runtime recreation, worktree removal, live-expiry refusal, explicit stopped-session artifact expiry and sibling retention; Pty/Herdr adoption regressions also pass. |
| V-2f | Passed `GrokRulesTransportCompatibilityTests.Missing_capability_refuses_before_sending_a_payload_to_an_old_runner`; PC-11 red/green. |
| V-2g | Passed `GrokRulesReceiptTests.Runner_receipt_is_validated_before_commit_or_refresh_input` and store remote-path grammar; PC-12 missing/version/generation/hash/count/path red/green. |
| V-2h | Passed both interrupted-launch receipt cases (8/8 class): persisted Starting state models lost Start response after runner commit; recovery obtains the committed runner DTO, preserves exact file/generation, sends one refresh before work and never starts a second process. Missing receipt fails with owned cleanup and no input. Actual HTTP receipt serialization is independently covered by V-2i; this is a commit-state fault test, not a TCP connection-drop test. |
| V-2i | Passed real isolated HTTP runner test (1/0/0, 5.960 s): capabilities with Herdr disabled, POST/GET/list receipt equality, exact Unicode bytes, invalid-body ProblemDetails, and response/log sentinel exclusion. Runner and inert child owned by the test; production runner never used. |
| V-3a | Passed `GrokRulesTransportCompatibilityTests.Unsafe_raw_rules_are_refused_server_side_before_runner_calls`, named-agent refusals, and card final-profile refusal; PC-3 independently disables the server raw guard. |
| V-3b | Passed `GrokRulesFileLaunchTests.Unsafe_final_runner_boundary_has_zero_effects_even_without_server_validation` and `GrokRulesRunnerRefusalTests`; PC-4 independently disables the runner raw guard. |
| V-3c | Passed Herdr effective-environment cases in runner refusal/file-launch and `DollarEnvArgTests` (11) / `AgentTuiLaunchResolverTests` (14); PC-5 server and runner red/green. |
| V-3d | Passed: all 26 unchanged GrokRulesArgvPolicyTests. |
| V-3e | Passed policy/store checks: literal values remain unchanged; no native expansion claim. |
| V-4a | Passed ready/receipt ordering (2), queue-entry barrier matrix, card boot, restored fake E2Es, and native V-8 ack-before-brief. Final queue-origin/hold-expiry expansion is recorded in the focused table. |
| V-4b | Passed `RunnerGrokAdapterSignInPromptTests` (3), `RunnerGrokAdapterTrustPromptTests` (4) and `GrokRulesReadyOrderingTests` (2); no authentication bypass used in live gates. |
| V-4c | Passed all 19 initialization cases, including quoted/fenced/tool/user/stale/premature/provider-error negatives and native split acknowledgment with late owning prompt. Correct prompt/ack/successful-end releases once; PC-16 independently mutates each prerequisite. |
| V-4d | Passed `GrokRulesFailureTests`, initialization delivery-exhaustion/deadline tests, `AgentSessionLaunchFailureTests` (11) and restored boot retry E2E. PC-24 deadline and PC-25 ownership controls retained. |
| V-4e | Passed 13 `GrokRulesReplayMatrixTests` cases, interrupted launch (6), interrupted queued attempts (11), delivery verification (104). PC-15/16/19/20 retained; no fabricated live acceptance inferred. |
| V-4f | Passed `GrokRulesChannelTests`, settlement/deferred race cases, `ChannelMachineTurnTextTests` (19), `ChannelFollowUpAttachmentTests` (22), and native Herdr bound fake channel sink; PC-17 independent exclusion mutations red/green. |
| V-4g | Passed deterministic `GrokRulesResumeMigrationTests` revision case and `GrokNativeSessionResumeTests` (6). The genuine changed-revision resumed-model answer belongs to descoped V-9b. |
| V-4h | Passed legacy refusal variants of `GrokRulesResumeMigrationTests` and `PolicyRefreshServiceTests` legacy pre-kill checks; PC-26 legacy-policy/silent-fresh red/green. |
| V-4i | Passed explicit fresh variants of `GrokRulesResumeMigrationTests`, with old native-history bytes retained; no retained-context claim for the fresh conversation. |
| V-4j | Passed removal and bare variants of `GrokRulesResumeMigrationTests`; PC-26 removal red/green. |
| V-4k | Passed exact restored no-task-answer boot E2E and all 73 watchdog cases after scoping the test hook; PC-18 boot-reply red/green. |
| V-5a | Passed genuine captured Grok 1.0.13 tool/boundary/end rows through live, sync and startup lanes across standing/channel/retired-pool populations. Final blocked/in-flight extensions are recorded in the final focused table. |
| V-5b | Partial: native tailer/runtime mid-tool-to-idle ordering in `GrokRulesCompactionRecoveryTests`, queue barrier controls and actual V-9 observations. Blocked Herdr UI plus concurrent already-started ordinary delivery matrix remains unverified. |
| V-5c | Passed native 1.0.13 sync/rebased/replay recovery and 13 persisted replay cases. PC-20 sync red/green retained. |
| V-5d | Passed native 1.0.13 startup recovery lane; PC-20 startup red/green retained. |
| V-5e | Passed all three transaction tests against native 1.0.13 boundary evidence: queue-write rollback/recovery and DB duplicate-key refusal. PC-19/22 red/green retained. |
| V-5f | Passed compact variants of `GrokRulesReplayMatrixTests.Persisted_refresh_evidence_reconciles_after_each_commit_without_new_logical_delivery`; PC-16/22 retained. |
| V-5g | Passed `GrokRulesReplayMatrixTests.Concurrent_recovery_coalesces_busy_boundaries_and_new_boundary_after_ack_creates_new_read` and `GrokRulesCompactionTests.Concurrent_replay_keeps_one_trigger_per_boundary_and_coalesces_untyped_reads`; PC-22 coverage red/green. |
| V-5h | Passed persisted conservative one-follow-on branch and restart-safe loop failure with retained ownership. The implementation always requests one follow-on for a compact during refresh; it does not optimize away that read from tool-position evidence. This may reject an otherwise recoverable repeated-compaction chain; it never silently releases work without the later acknowledged read. |
| V-5i | Passed established-session ownership assertions in failure/compaction tests and PC-25; no automatic kill on failed established refresh. |
| V-5j | Passed all 32 Grok tailer cases including all five distinct boundaries from the unchanged 33-row native 1.0.13 fixture, no early TurnEnd, checkpoint/tokenless controls; Codex normalizer/tailer/working-state regressions also pass. |
| V-6a | Passed Grok dispatch pool eligibility/revision/ready cases and PC-27 unacknowledged/invalid-receipt controls. No stand-alone `GrokRulesPoolReuseTests` class was added. |
| V-6b | Passed all 20 `PolicyRefreshServiceTests` and deterministic revision/removal resume matrix. Genuine revised-model behavior belongs to descoped V-9b. |
| V-6c | Passed all named provider regressions in the continuation sweep: Claude/Codex argv, Codex dispatch/tailers, compaction recovery, normalizers and working state; zero skips. |
| V-6d | Passed launch failure/ownership, interrupted launch, termination-source, restored E2Es and five `PtyKillProcessTreeTests`; PC-25 retained. |
| V-6e | Passed wrong-kind payload validation in server/runner boundary matrices and bare/specialist dispatch controls. Baseline V-9 additionally asserts no file receipt or refresh row. |
| V-7a | Passed all five Grok delegate E2Es (2m 07.859 s): rules read/ack before brief and marked settlement/pricing, complete head/middle/tail brief spill plus native pointer, unsafe registry refusal before process/brief, unmarked nudge, task-only boot retry and Claude control. Actual managed-profile refusal is independently covered by CardSpawnModelArgumentTests. Native full brief tool-output coverage is V-8a. |
| V-7b | Passed: exact named unmarked-after-nudge E2E; zero skips. |
| V-7c | Passed: exact named task-only boot-stall/retry E2E; zero skips. |
| V-8a | Passed real CLI/PtyHost/script/HTTP-relay/service-graph wire acceptance with full rules and head/middle/tail brief tool outputs, ack-before-brief and marked settlement. PC-28 missing-file, PC-29 truncated read/lying ack, and PC-30 helper-only capture each produce assertion failures and restored greens; see mutation ledger for exact seams. |
| V-8b | Passed 1/0/0: `GrokRulesHerdrAcceptanceTests.Real_cli_herdr_standing_attachment_reads_rules_before_work`; actual named Herdr server, standing reuse, full >1,000-line read, bound fake channel sink, marked settlement. 14.420 s body / 32.590 s TUnit. |
| V-8c | Passed 1/0/0: `GrokRulesLiveMappedDispatchTests` attempt 5; actual mapped POST, native authenticated model, rules-only canaries and restart requirement, full brief and marked settlement. 75.746 s body / 95.391 s TUnit. Earlier four failures retained below. |
| V-8d | Passed 1/0/0: `GrokRulesHerdrAcceptanceTests.Live_model_herdr_standing_attachment_obeys_rules_after_startup_ack`; native authenticated model, full rules read, same standing Herdr pane/session, rules-only canaries, marked settlement, no ack leak to bound fake channel. 85.011 s body / 103.947 s TUnit. |
| V-9a | **Descoped by operator; follow-up evidence retained.** Revised authenticated inline arm passed 1/0/0, 165.877719 s body / 179.463 s TUnit, two genuine completed IDs and five surviving checkpoints. |
| V-9b | **Descoped by operator; not an acceptance blocker.** Attempt 3 passed all five initial/two-compaction checkpoints, then failed its test-only JSONB query during revised-resume verification: 582.266 s body / 596.421 s TUnit, 1 failed/0 skips. Exact query corrected, no further live run authorized for this task. |

## Regression control ledger

The current R mapping below uses the final V evidence and retained assertion-red/restored-green
controls. V-9 endurance is explicitly excluded by the operator scope decision. Test seams and
the conservative V-5h follow-on behavior remain disclosed; no full live-resume claim is inferred.

| Control | Status |
|---|---|
| R-1 | Composition/store/argv matrices and PC-1/2/3/4 pass; final V-7 E2E includes raw-registry refusal, with actual managed-profile refusal covered independently by card preflight. |
| R-2 | Independent server/direct-runner raw and effective-env guards plus unchanged shared policy pass; PC-3/4/5/6 red/green. |
| R-3 | Native default-agent full reads and authenticated compliance pass. Revised native-resume behavioral proof belongs to descoped V-9b. |
| R-4 | Pre-spawn store/metadata and capability refusal pass; interrupted-launch committed-receipt recovery/no-second-start and real HTTP receipt tests cover V-2h/i. |
| R-5 | Distinct paths, real runtime adoption, worktree removal, live-expiry refusal, stopped-session expiry and sibling retention pass; V-2h commit-state recovery also passes. |
| R-6 | All 19 owning prompt/ack/end cases pass, including native split/late capture and quoted/tool negatives; persisted replay and native V-8 ordering pass. |
| R-7 | Native 1.0.13 live/sync/startup/transaction recovery passes; independent PC-19/20/22 assertion red/green retained. |
| R-8 | Native working-state/parser and queue ordering pass; blocked Herdr UI/already-started-delivery interleaving remains V-5b. |
| R-9 | Attachment/channel/retired-agent recovery and dispatch pool eligibility pass, including PC-23/27. |
| R-10 | Settlement/deferred/channel/boot exclusions and all restored task-only E2Es pass; PC-17/18 red/green. |
| R-11 | Persisted deadlines, replay, conservative one-follow-on cap and ownership pass. V-5h deliberately performs a later read rather than claiming tool-position-based coverage. |
| R-12 | Legacy/removal refusal and explicit-fresh history preservation pass; PC-26 red/green. |
| R-13 | Actual V-8c/d live compliance passed; PC-31 expected-row/duplicate-ID mutations each failed then passed after restoration. V-9 endurance deliberately moved to the operator's follow-up card. |
| R-14 | Exact Unicode bytes, limits, real HTTP capabilities/receipts/problem/log no-leak checks, no env transport and PC-1/7 pass. |

## Positive controls

Full assertion output (including the large exact expected/actual byte arrays), green output and
machine-readable per-mutation elapsed measurements are retained in the adjacent evidence ZIP.
No intentionally broken source was committed. Each accepted mutation below ran one test red,
restored original source bytes, then ran the same test green (one pass, zero skips).

Archive SHA-256: `9d2c984aba687a5806da037f5db3ad8b470c480e33a72234b0806c0112ba9410`.

| Mutation | Red / revert / green | Assertion excerpt | Build+test elapsed red / green |
|---|---|---|---|
| PC-1-truncation | 1 failed / source restored / 1 passed | `ShouldAssertException: await File.ReadAllBytesAsync(receipt.Path)`; `should be` [exact expected bytes], `but was` [corrupted bytes]. Full original arrays in archive. | 19.922 s / 14.984 s |
| PC-1-normalization | 1 failed / source restored / 1 passed | `ShouldAssertException: await File.ReadAllBytesAsync(receipt.Path)`; `should be` [exact expected bytes], `but was` [corrupted bytes]. Full original arrays in archive. | 15.859 s / 14.328 s |
| PC-8-bootstrap-budget | 1 failed / source restored / 1 passed | `ShouldAssertException: Directory.Exists(root)` / `should be False` / `but was True`. | 17.750 s / 14.969 s |
| PC-9-source-conflict | 1 failed / source restored / 1 passed | `ShouldAssertException: Directory.Exists(root)` / `should be False` / `but was True`. | 14.844 s / 15.281 s |

PC-8 and PC-9 initially failed on a downstream exception-type assertion. Their tests were strengthened
to assert zero disk effects first and rerun; the archived final evidence is the direct disk-effects
assertion, with a nonexistent disposable host source preventing actual child creation.

| Group | Status |
|---|---|
| PC-1 | Executed: separate truncation and newline-normalization mutations. |
| PC-2 | Recorded red/restore/green: `PC-2-raw-body`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-3 | Recorded red/restore/green: `PC-3-server-raw`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-4 | Recorded red/restore/green: `PC-4-runner-raw`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-5 | Recorded red/restore/green: `PC-5-server-env`, `PC-5-runner-env`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-6 | Recorded red/restore/green: `PC-6-CR`, `PC-6-NUL`, `PC-6-4097-accepted`, `PC-6-4096-rejected`, `PC-6-alias`, `PC-6-equals`, `PC-6-later-occurrence`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-7 | Recorded red/restore/green: `PC-7-server-NUL`, `PC-7-server-key`, `PC-7-server-bytes`, `PC-7-server-kind`, `PC-7-runner-NUL`, `PC-7-runner-key`, `PC-7-runner-bytes`, `PC-7-runner-kind`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-8 | Executed runner-boundary mutation; see evidence above. |
| PC-9 | Executed runner-boundary mutation; see evidence above. |
| PC-10 | Recorded red/restore/green: `PC-10-before-replace`, `PC-10-swallow-replace`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-11 | Recorded red/restore/green: `PC-11-capability`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-12 | Recorded red/restore/green: `PC-12-missing`, `PC-12-version`, `PC-12-generation`, `PC-12-hash`, `PC-12-count`, `PC-12-path`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-13 | Recorded red/restore/green: `PC-13-pty-manifest`, `PC-13-herdr-sidecar`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-14 | Recorded red/restore/green: `PC-14-cwd`, `PC-14-premature-delete`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-15 | Recorded red/restore/green: `PC-15-flush`, `PC-15-now-sendnow`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-16 | Recorded red/restore/green: `PC-16-hash`, `PC-16-id`, `PC-16-generation`, `PC-16-prompt`, `PC-16-end`, `PC-16-provider`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-17 | Recorded red/restore/green: `PC-17-settlement`, `PC-17-channel-main`, `PC-17-channel-machine`, `PC-17-deferred`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-18 | Recorded red/restore/green: `PC-18-boot-reply`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-19 | Recorded red/restore/green: `PC-19-watermark-before-queue`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-20 | Recorded red/restore/green: `PC-20-sync`, `PC-20-startup`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-21 | Recorded red/restore/green: `PC-21-turn-end`, `PC-21-mid-tool`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-22 | Recorded red/restore/green: `PC-22-database-index`, `PC-22-coverage`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-23 | Recorded red/restore/green: `PC-23-append-gate`, `PC-23-named-agent`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-24 | Recorded red/restore/green: `PC-24-deadline`, `PC-24-followon`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-25 | Recorded red/restore/green: `PC-25-established-kill`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-26 | Recorded red/restore/green: `PC-26-legacy-policy`, `PC-26-silent-fresh`, `PC-26-removal`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-27 | Recorded red/restore/green: `PC-27-unacknowledged`, `PC-27-invalid-receipt`. See resumed evidence and ZIP for exact assertions; this certifies only the listed mutations. |
| PC-28 | Queued refresh pointer mutated to a missing file: 1 assertion failure / bytes restored / 1 pass. Actual `state.GrokRulesState should be GrokRulesState.Ready but was GrokRulesState.Failed`; reason `grok_rules_initialization_failed: unreadable`. Red 134.469 s / green 90.859 s including build. Native argv bootstrap itself was not changed. |
| PC-29 | Two controls executed: native read stops at line 1,000; real dispatch reads only 10 of its 58 rule lines but sends a success ack. Both named assertions fail, both source restorations pass. See continuation controls. |
| PC-30 | Helper-only captured evidence substituted at the acceptance oracle: task-tail/tool-bearing assertion fails, restore passes. No provider route changed. See continuation controls. |
| PC-31 | Executed against captured authenticated baseline evidence: expected-row mutation failed independent-oracle comparison (106.000 s red / 35.765 s green); duplicate-ID mutation failed `ids.Distinct(StringComparer.Ordinal).Count() should be 2 but was 1` (35.453 s red / 35.766 s green). Each 1 failed/0 skips, bytes restored, 1 passed/0 skips. No native/model data was fabricated. |

## Handoff

Continue Code on the continuation branch. The historical T-6/T-10 implementation gaps above
were addressed by the resumed implementation; do not rebuild them. Finish the missing acceptance harnesses and remaining named tests. Complete every unrun V/R/PC case, then the mandatory isolated real-wire and real-model gates.
Keep the card open and do not hand this implementation to Land as a completed fix.

## Resumed implementation evidence

This section supersedes the initial-slice metadata/startup gap descriptions above; the full acceptance contract remains open. Commit `318b0966` records pre-spawn receipt persistence and failed-startup recovery. Subsequent verified changes persist the server receipt/refresh row before provider readiness, prevent refresh typing while Starting, record termination intent before owned startup cleanup, and refuse legacy Grok policy drift before notification/kill. A policy fixture initially retained a Claude launch definition; corrected Grok-definition rerun passed.

Current selected verification is enumerated per log in `card0395-current-verify/ledger.json` within the archive. Counts overlap earlier suites and are not additive. The corrected card-spawn case passed (1 test, 25.757 s); deferred settlement passed both normal and late-text cases. Native ACP parser/live/sync/startup tests use the attributed Grok 1.0.5 fixture and do not establish live 1.0.13 endurance.

Additional mutation evidence follows. Full actual assertion text and exact elapsed measurements are in the archive. Every accepted row restores original source bytes and reruns the same filter green. Rejected build-failure attempts retained in the archive are not positive controls.

| Mutation | Red exit / green exit | Actual assertion excerpt | Elapsed red / green |
|---|---|---|---|
| PC-6-CR | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: v should not be null but was at Antiphon.SessionRunner.Tests.GrokRulesArgvPolicyTests.AssertRefused(GrokRulesArgvViolation v, String reason, String flag) in C:\Antiphon\worktree` | 19.672 / 14.110 s |
| PC-6-NUL | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: v!.Reason should be "nul" but was` | 14.156 / 13.765 s |
| PC-6-4097-accepted | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: v should not be null but was at Antiphon.SessionRunner.Tests.GrokRulesArgvPolicyTests.AssertRefused(GrokRulesArgvViolation v, String reason, String flag) in C:\Antiphon\worktree` | 13.641 / 14.234 s |
| PC-6-4096-rejected | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: GrokRulesArgvPolicy.ValidatePayload(payload, isWindows: true, isGrok: true) should be null but was GrokRulesArgvViolation { Reason = token_too_long, Flag = --rules, OccurrenceIn` | 13.765 / 13.907 s |
| PC-6-alias | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: v should not be null but was at Antiphon.SessionRunner.Tests.GrokRulesArgvPolicyTests.AssertRefused(GrokRulesArgvViolation v, String reason, String flag) in C:\Antiphon\worktree` | 14.000 / 13.234 s |
| PC-6-equals | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: v should not be null but was at Antiphon.SessionRunner.Tests.GrokRulesArgvPolicyTests.AssertRefused(GrokRulesArgvViolation v, String reason, String flag) in C:\Antiphon\worktree` | 13.828 / 13.594 s |
| PC-6-later-occurrence | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: v should not be null but was at Antiphon.SessionRunner.Tests.GrokRulesArgvPolicyTests.AssertRefused(GrokRulesArgvViolation v, String reason, String flag) in C:\Antiphon\worktree` | 13.984 / 14.406 s |
| PC-3-server-raw | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Starts should be 0 but was` | 43.328 / 41.140 s |
| PC-4-runner-raw | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Directory.Exists(root) should be False but was` | 16.188 / 15.281 s |
| PC-5-server-env | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Starts should be 0 but was` | 58.343 / 42.016 s |
| PC-5-runner-env | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: fake.Requests should be empty but had 12 items and was` | 15.781 / 15.016 s |
| PC-7-server-NUL | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Requests should be 0 but was` | 27.484 / 29.875 s |
| PC-7-server-key | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Requests should be 0 but was` | 59.672 / 26.407 s |
| PC-7-server-bytes | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Requests should be 0 but was` | 26.328 / 25.078 s |
| PC-7-server-kind | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Requests should be 0 but was` | 25.297 / 25.516 s |
| PC-7-runner-NUL | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Directory.Exists(root) should be False but was` | 15.484 / 12.985 s |
| PC-7-runner-key | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Directory.Exists(root) should be False but was` | 12.687 / 13.453 s |
| PC-7-runner-bytes | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Directory.Exists(root) should be False but was` | 13.141 / 12.484 s |
| PC-7-runner-kind | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Directory.Exists(root) should be False but was` | 13.000 / 12.297 s |
| PC-11-capability | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: handler.Starts should be 0 but was` | 34.657 / 48.891 s |
| PC-15-flush | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Adapter.SubmittedBodies should be empty but had 1 item and was` | 69.547 / 40.046 s |
| PC-15-now-sendnow | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Adapter.SubmittedBodies should be empty but had 1 item and was` | 39.188 / 54.594 s |
| PC-16-hash | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesState == GrokRulesState.Ready should be False but was` | 59.718 / 40.829 s |
| PC-16-id | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesState == GrokRulesState.Ready should be False but was` | 56.890 / 60.610 s |
| PC-16-generation | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesState == GrokRulesState.Ready should be False but was` | 79.531 / 63.734 s |
| PC-16-prompt | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesState == GrokRulesState.Ready should be False but was` | 47.063 / 41.593 s |
| PC-16-end | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesState == GrokRulesState.Ready should be False but was` | 41.329 / 42.218 s |
| PC-16-provider | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesState == GrokRulesState.Ready should be False but was` | 57.703 / 58.625 s |
| PC-24-deadline | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: state.GrokRulesState should be GrokRulesState.Failed but was` | 35.656 / 35.313 s |
| PC-24-followon | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesFailure should be "grok_rules_refresh_failed: refresh_loop" but was` | 35.422 / 35.390 s |
| PC-25-established-kill | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: adapter.Killed should be False but was` | 51.797 / 54.235 s |
| PC-26-legacy-policy | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Adapter.SubmittedBodies should be empty but had 1 item and was` | 41.188 / 38.328 s |
| PC-10-before-replace | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: snapshot.Bytes should be [102, 117, 108, 108, 13, 10, 114, 117, 108, 101, 115] but was` | 18.453 / 14.016 s |
| PC-10-swallow-replace | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: File.Exists(PtyHostManifest.PathFor(settings.PtyHostManifestDir, id)) should be False but was` | 13.625 / 13.922 s |
| PC-12-missing | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesReceiptJson should be null but was "null" ` | 55.297 / 40.625 s |
| PC-12-version | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesReceiptJson should be null but was "{"Path":"C:\\remote runner\\instructions\\grok\\1c955c3e821c40c5a3a68bb707bb40ea\\rules.md","Sha256":"fc624225997a27f837ec2b` | 40.125 / 41.828 s |
| PC-12-generation | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesReceiptJson should be null but was "{"Path":"C:\\remote runner\\instructions\\grok\\2c9714aeb01443dd8e122510f6e59909\\rules.md","Sha256":"fc624225997a27f837ec2b` | 53.375 / 53.984 s |
| PC-12-hash | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesReceiptJson should be null but was "{"Path":"C:\\remote runner\\instructions\\grok\\b194430189034222bef5c935db30fe24\\rules.md","Sha256":"0000000000000000000000` | 38.843 / 39.563 s |
| PC-12-count | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesReceiptJson should be null but was "{"Path":"C:\\remote runner\\instructions\\grok\\ff24173f47cd4914b1f47414fc5ddc68\\rules.md","Sha256":"fc624225997a27f837ec2b` | 39.297 / 38.984 s |
| PC-12-path | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: session.GrokRulesReceiptJson should be null but was "{"Path":"remote runner\\instructions\\grok\\de1eab15da604af18074475055c29096\\rules.md","Sha256":"fc624225997a27f837ec2b91c5` | 56.156 / 56.500 s |
| PC-13-pty-manifest | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: else recovered.GrokRulesReceipt should be GrokRulesReceipt { Path = C:\Users\lndco\AppData\Local\Temp\antiphon-card0395-adoption-d5c2ba8039194cdfa6b2ebc0d43fd2be\instructions\gr` | 21.563 / 19.546 s |
| PC-13-herdr-sidecar | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: else recovered.GrokRulesReceipt should be GrokRulesReceipt { Path = C:\Users\lndco\AppData\Local\Temp\antiphon-card0395-adoption-0b0fec412935442db39c3a46af3fa6d6\instructions\gr` | 19.547 / 19.188 s |
| PC-14-cwd | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: else recovered.GrokRulesReceipt should be GrokRulesReceipt { Path = C:\Users\lndco\AppData\Local\Temp\antiphon-card0395-adoption-58935d4e1a9b4381b8efbb71fba458ce\disposable work` | 16.875 / 16.281 s |
| PC-14-premature-delete | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: File.Exists(receipt.Path) should be True but was` | 16.641 / 16.688 s |
| PC-17-settlement | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: unchanged.Status should be AgentTaskStatus.Dispatched but was` | 50.922 / 46.250 s |
| PC-17-channel-main | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Messaging.SentReplies.Count should be 0 but was` | 51.484 / 38.562 s |
| PC-17-channel-machine | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Messaging.SentReplies.Count should be 0 but was` | 37.563 / 53.703 s |
| PC-19-watermark-before-queue | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: rows.Count should be 1 but was` | 75.719 / 53.109 s |
| PC-20-sync | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was` | 53.625 / 39.719 s |
| PC-20-startup | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was` | 39.312 / 38.485 s |
| PC-21-turn-end | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: captured.Kind should be "CompactBoundary" but was` | 28.640 / 30.079 s |
| PC-22-database-index | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: Task `db.SaveChangesAsync()` should throw Microsoft.EntityFrameworkCore.DbUpdateException but did not` | 36.156 / 34.594 s |
| PC-23-append-gate REJECTED (not assertion evidence) | 2 / 0 | `NO ASSERTION FAILURE` | 51.812 / 51.422 s |
| PC-23-append-gate | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was` | 61.500 / 57.062 s |
| PC-27-unacknowledged | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: actual.AgentSessionId should not be 7ca2f9d0-de73-4fa2-969e-19c894493083 but was` | 56.109 / 64.094 s |
| PC-27-invalid-receipt | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: actual.AgentSessionId should not be acc64376-90fa-4b22-931f-fb7a937cbcc3 but was` | 39.484 / 39.172 s |
| PC-21-mid-tool | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: captured.Kind should be "CompactBoundary" but was` | 63.375 / 37.047 s |
| PC-23-named-agent | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was` | 59.500 / 61.484 s |
| PC-26-silent-fresh | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: child.StartedArgs should contain "--resume" but was actually` | 58.422 / 56.531 s |
| PC-2-raw-body | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: spec.GrokRulesPayload should not be null but was at Antiphon.Tests.Application.GrokRulesCompositionTests.Worker_and_stage_bundles_reach_typed_rules_payload_without_argv_text(Age` | 34.719 / 34.890 s |
| PC-22-coverage | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: rows.Count(m => m.RulesCoveredByMessageId == null) should be 1 but was` | 34.984 / 36.469 s |
| PC-26-removal | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Adapter.Killed should be False but was` | 37.766 / 37.703 s |
| PC-18-boot-reply | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: digest should contain (case insensitive comparison) "BOOT TURN" but was actually` | 47.859 / 53.172 s |
| PC-17-deferred | 2 / 0 | `TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: provider.GetRequiredService<DeferredReportSweepMarks>().ShouldHandOff(sessionId, end.Sequence, null, DateTime.UtcNow, 3600) should be True but was` | 50.343 / 48.407 s |

Current archive SHA-256: `0b56117e0c5a9bc5f1172a1e8760a3f4e6d53173661dd6125fc9d30f5de71e05`. Earlier archive hash above identifies the prior four-mutation artifact. All still-unrun V/R/PC rows remain open; related newer deterministic checks do not certify the broader gate. No live acceptance call or real auto-compaction has yet been executed.

## Continuation c7ff2da0 — active verification

2026-09-06: reconciled the stale PC summary table against the already-committed mutation
evidence. This is a documentation correction, not a new execution or broader V/R certification.
PC-28..31 remain unrun. The V/R tables remain conservative pending case-level verification.

Prerequisite checks: Docker server 29.5.3 and grok/dotnet/python/pwsh are available.
The conventional `%LOCALAPPDATA%\Antiphon\grok-test-home\auth.json` is absent.
Requested an authenticated isolated test-home path or approved credential profile; no credential
store was read or copied, and no live provider refusal is claimed from that file check.
The full dispatch/endurance acceptance classes are absent at the starting tip;
`GrokRulesNativeReadWireTests` is calibration only. Live gates remain unrun.

### Live prerequisite probe (not V-8c/d or V-9 acceptance)

The existing native OAuth login is present. The repository-supported `GROK_AUTH_PATH`
override allowed a disposable `GROK_HOME` to use it without reading/copying credentials or
adding an API-key fallback. A real grok-4.6 model call completed on 2026-09-06 in **19.844 s**,
exit 0, native session `6bb4afde-33c2-4daa-a0e3-33916a1d192b`.
The model voluntarily called `read_file` then a continuation with `offset=1001, limit=1000`.
All three random early/middle/tail file-only answers matched. CLI-reported usage: input 23,992,
cache-read 39,808, output 478, three calls, displayed cost USD 0.01202852.
The probe is print mode with a standalone rules file; it has no server queue, mapped dispatch,
startup ack barrier or compactions. It establishes available authenticated access and preliminary
model-mediated reading only. **There is no auth blocker and the mandatory live gates remain open.**
Evidence is `.antiphon/card0395-continuation/live-prerequisite/manifest.json` plus native output.

The first V-8a wire implementation passed one test (30.131 s inside the acceptance run).
A tightened request classifier then failed one test with `state.GrokRulesState should be
GrokRulesState.Ready but was GrokRulesState.Failed`, reason `missing_ack`: the classifier
incorrectly included JSON role/type labels ahead of the content. This is a test-harness defect,
not a runtime regression or a positive control. Correction and rerun are pending.
The separate native continuation-read test passed 1 test / 0 failures / 0 skips (31.129 s TUnit).

### Corrected wire dispatch checkpoint

`Real_cli_delegate_wire_reads_full_rules_before_brief_and_settles` passed **1/1**, zero skips,
76.159 s TUnit, **44.548 s** measured acceptance body. Task `87c11016-4947-415e-af9c-ab7fa743b6ae`,
Antiphon/native session `a275b1d0-f640-46aa-8f1b-7a54fe7d200a`. Four actual user requests:
rules read, returned native rules content plus ack, spilled-brief read, returned native brief content
plus marked report. Dashboard helper traffic is excluded by the current user-query envelope.
Actual stage-code and delegate-basics composition has 58 lines; every nonblank line was present
in native function-call output. The separate 1,105-line canary verifies continuation reads.
The in-process PowerShell invocation supplied an 8,722-byte multiline goal with independent markers.
This passes the implemented V-8a PtyHost/HTTP-relay path; the full V-8a evidence schema and PC controls
are still being completed. It does not pass Herdr, mapped real-Program live dispatch, or endurance.

Safe durable evidence: `2026-09-06-card-0395-continuation-evidence.zip` (adjacent), SHA-256
`bcaea283dfdfc4ad1a05a210604ded87b3bb57c893162adf5f920c613b038dfe`. It includes rules/brief, receipt/queue/settlement timeline, normalized transcript,
actual assertions, and the live prerequisite's native tool/text rows with thought rows omitted.
Credential files, headers, homes, plugin caches and full raw request bodies are not archived.

PC-28 continuation checkpoint: the mutation changed only the initialization message file pointer.
The source was restored before its green rerun; this is an explicit read/barrier control, not
a claim that a missing argv-only pointer was exercised. Full logs and elapsed live in
`.antiphon/card0395-continuation/mutations.json` and the named red/green logs (archive update pending).

### Continuation wire positive controls

All four cases below ran exactly one test red and the same test green after restoring source bytes.
No skips; no build failure counted as a control. PC-29 separates the >1,000-line native oracle
from the 58-line actual Worker dispatch; it does not claim a >1,000-line Worker bundle.
PC-30 changes the evidence presented to the checker only; the real provider redirect stays intact.

| Mutation | Actual assertion | Red / green build+test seconds |
|---|---|---|
| PC-28-refresh-missing | `ShouldAssertException: state.GrokRulesState should be GrokRulesState.Ready but was GrokRulesState.Failed Additional Info: grok_rules_initialization_failed: unreadable` | 134.469 / 90.859 |
| PC-29-native-first1000 | `ShouldAssertException: toolOutputs should contain (case insensitive comparison) "RULE-LINE-1001 neutral calibration material." but was actually "1→RULE-LINE-0001 neutral calibration material. RULE-LINE-0002 neutral calibration material. RULE-LIN..." Additional Info: Every line including the tail beyond the native default must be read` | 72.781 / 71.984 |
| PC-29-dispatch-first10 | `ShouldAssertException: toolText should contain (case insensitive comparison) "[bundle:delegate-basics v36f4d127]" but was actually "function_call_output card0395-rules-read 1→[bundle:stage-code v2503714b] You are executing the lande..." Additional Info: Native rules output must cover every composed line` | 97.719 / 95.203 |
| PC-30-helper-only | `ShouldAssertException: all.Any(r => r.Contains(nonce + "-TAIL") && r.Contains("\"tools\"")) should be True but was False Additional Info: Full task tail must reach a tool-bearing user request` | 92.141 / 94.766 |

Updated continuation archive SHA-256: `712f2cd899273192676bf2aafa62eea513c0d0005a0dbbab3c13bd6d9d48ff7b`.
PC-31 and both live endurance arms remain open; zero genuine auto-compactions have been observed so far.

### New harness attempts in progress

`GrokRulesAutoCompactionCalibrationTests.Native_auto_compaction_emits_its_own_ACP_boundary`:
first run **1 failed**, zero skips, 26.565 s TUnit / 4.210 s native process. It observed seven
user requests but zero automatic boundaries. The native read tool refused the requested 2,000-line
range: **26,648 tokens exceeds maximum 25,000 tokens**. No usage counters were inflated; this
failed workload is not compaction evidence. The next attempt uses native 1,000-line ranges.
The installed 1.0.13's extracted `docs/user-guide/26-config-reference.md` confirms
`session.auto_compact_threshold_percent`; the disposable config sets it to 5. Upstream resolution
was cross-checked at `72a61251fcffb464bcc687aeb5a998e5a98ec0c9`,
`crates/codegen/xai-grok-shell/src/util/config/resolve/compaction.rs`.

`GrokRulesLiveMappedDispatchTests.Live_model_delegate_obeys_file_only_rules_and_consumes_full_brief`:
first run **1 failed**, zero skips, 27.394 s TUnit. Actual failure is
`OptionsValidationException: Agents:Definitions:grok:Env entry 'GROK_HOME' is not explicitly
classified and is not recognizably secret` (also auth-path, updater and telemetry names).
This occurred before any model launch; the test definition now classifies those nonsecret names.
No production registry/profile configuration was changed. The corrected run is pending.

### Failed mapped-live and calibration attempts, continuation checkpoint

Mapped attempt 2 failed before dispatch: **1 failed / 0 skips**, 32.443 s TUnit,
`ObjectDisposedException: IServiceProvider`; its test database context outlived the options
scope. The factory now builds each context directly from its isolated connection string.
Attempt 3 reached the actual mapped route and native authenticated model: **1 failed / 0 skips**,
243.357 s TUnit, **217.584 s** measured body, task `ebd9ca55-397a-4c9c-9a15-f1c59087ac54`,
native/Antiphon session `f1351c77-92e6-4293-b979-11b15e11cbdf`.
Actual assertion: `ShouldAssertException: ...a003fb209b3b4550a2780d50d70d8995: Dispatched`.
The native transcript shows full 58-line rules read, matching standalone acknowledgement,
then full 192-line spilled brief read. It ends at that completed tool result; no final marked
report arrived in the predeclared three-minute work wait. This is a failed V-8c attempt,
not evidence of model compliance or an authentication refusal. Failure diagnostics now also
save the runner screen and queue state for the next same-window rerun.

Calibration attempt 2: **1 failed / 0 skips**, 27.242 s TUnit, 4.810 s native; asking for
1,000-line ranges still hit the file's 26,648-token native limit. Attempt 3 uses five separate
500-line files (53,498-byte body each), and the native requests grow to 317,411 bytes through
real tool output. **1 failed / 0 skips**, 25.021 s TUnit, **4.108 s** native, six user requests,
zero automatic boundaries; native session `ab471d74-9cf3-467a-813e-e24a0b4bddd2`.
The fixture model advertises a 256,000-token context and its standard usage stays at 10 input /
5 output. The CLI estimates new tool output after that count; five percent is too high for each
individual output estimate. Attempt 4 is separately declared at the supported **1%** setting,
with the same real tools and unmodified fixture usage. It is pending, not a success claim.

Regression sweep is running sequentially. Initial failures: `DelegateLaunchArgvIntegrityTests`
**4 passed / 2 failed**, and `CardSpawnModelArgumentTests` **13 passed / 1 failed**. Both assert
the superseded blanket CARD-0382 multiline refusal. The launch matrix now checks typed content
and absence from argv; the card negative case targets unsafe inline profile rules, while the
existing multiline file/barrier case remains. Corrected reruns are pending. Shared argv policy
has not changed.

Dedicated Herdr server `card0395-c7ff2da0`, version 0.8.2/protocol 20, was started and checked;
no pane has been launched yet. Availability alone does not satisfy V-8b/d.
Continuation evidence ZIP checkpoint SHA-256:
`2aa7cf3e79a55edefdf95f8e0a3bb0ec51ec00150e2b367dfb001797b8ad9257`.


## Final deterministic continuation checkpoint

The refinement is incorporated above. Real live dispatch settlement is passing; endurance is
operator-descoped. Newly completed checks: initialization 19/19 (31.807 s), interrupted launch
8/8 (32.647 s), native Grok tailer 32/32 (5.041 s), actual HTTP runner 1/1 (5.960 s), resume
migration 6/6 (20.238 s), transaction recovery 3/3 (15.870 s), Grok delegate E2E 5/5
(127.859 s), argv integrity 6/6 (16.613 s), and Grok delegate dispatch 38/38 (22.526 s).
Zero skips in these runs. They overlap the previous 570-case regression coverage; do not add
these totals and call the sum unique tests.

The unsafe registry E2E exposed a real preflight gap: the asynchronous launch refused the raw
argv but left its task Dispatched. Dispatcher now checks final resolved raw argv before queueing
through the existing conflict handler, which fails both claimed task/session rows and queues no
brief. The earlier actual managed-profile card fix remains independently tested. Shared
CARD-0382 policy is unchanged.

Initial final-check failures are retained: blocked-Herdr fixture omitted SessionDeliveryProfile;
adding the actual profile/capabilities fixed 10/10 recovery cases. FakeGrok records the spilled
brief pointer, so its complete brief assertion now checks disk bytes plus native pointer; actual
native tool-output completeness remains the separate real-CLI V-8 oracle. Build-only failures
(missing namespace and earlier DTO/property/list-type edits) are not positive controls, and no
stale no-build execution is used to certify newly added cases. One misspelled class filter
GrokRulesDispatchTests selected zero tests (exit 8); the correct 38-case class passed above.

Further expansion currently pending: in-flight ordinary delivery confirmation under the closed
rules barrier (one failure), and 28 queue-origin/entry-point cases (26 passed; the two supervision
cases need the established cancel-on-ineligible expectation). A stricter PC-28 run moves the
owned disposable file named by both native bootstrap and refresh after runner commit, before
native read; its assertion-red/restored-green evidence will be recorded below.
