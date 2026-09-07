# CARD-0419 implementation and verification evidence

Code stage, 2026-09-07. Implements the approved plan and TestDesign, including
review constraints D1/D2 and the D3 Shadow-cohort measurement. **Mandatory live
Apply acceptance is pending operator-authored approval.** No production mode
change, restart, deployment, live model dispatch, broker send or credential
change was performed by this Code stage.

## Result and implementation

- `IAgentReportStore` / `AgentReportStore` retain exact UTF-8 bytes under the
  persistent primary checkout's `.antiphon\reports\<full-guid>\<exact-sha256>.md`.
  Root verification, ignored/untracked custody, a two-second cooperative budget,
  sibling publication and exact readback precede publication of the path.
- `AgentReportPolicy` separates the default 4,000 UTF-16-character usefulness
  minimum from transport ceilings, mode/enablement and the inclusive 20,000
  model-input cap. Explicit 1,200-character overrides still work.
- **D1 precedence:** canonical storage wins when available. If it is unavailable,
  the existing transport backstop still runs above the selected reply ceiling.
  Existing delegate-authored legacy files remain untouched. Apply verifies that
  the selected file contains the authoritative raw result; a different, missing
  or unreadable file produces API recovery, never a false full-report label.
- Apply verifies files and enumerates generated attachments before taking the
  delivery semaphore or database row locks, under the original request deadline.
  It rechecks source/path identity under the locks, preserves the original header
  and deterministic deliverable block, and changes only the existing note body.
  Source raw text, claim/poll ordering, hold cleanup and final byte spill remain
  guarded. File reads do not increment the API-only `FullReadAt` metric.
- The exact 20,000-character integration boundary exposed a pre-existing storage
  mismatch: the specialist prompt wrapper exceeded `AgentTasks.Goal`'s
  `varchar(20000)`. Migration `20260907154144_AllowWrappedSpecialistGoal` widens
  internal goal storage to PostgreSQL `text`; public task creation retains its
  20,000-character validation. The migration was generated with EF's CLI using
  isolated outputs. A downgrade must inspect retained wrapped goals; it must not
  truncate them to satisfy the old column.
- The explicit E2E canary uses an owned database, random loopback runner, private
  manifest root, refusing messaging adapters and child-only modern ConPTY.
  It exercises normal source dispatch, the real cheap specialist, parent
  `UserPrompt` confirmation, a correlated filesystem tool call/hash result,
  worktree removal, host-only restart and owned-runner census. It exports failure
  evidence before cleanup and publishes sanitized acceptance only after all
  assertions pass. The canary has been compiled, not executed live.

The production changes are in `server/Application/{Interfaces,Services,Settings}`,
`server/Infrastructure/Files`, `server/Infrastructure/Data/AppDbContext.cs`,
`server/Program.cs`, domain pointer/metric documentation and the generated
migration. Tests are the four scoped policy/store/delivery/race classes, the
report workspace helper, retained producer/DI support, and the E2E canary and
guards. The living owner is [orchestration-loop.md](../orchestration-loop.md),
section 10; the [plan and verification design](../superpowers/plans/2026-09-07-card-0419-distillation-delivery-plan.md)
remain the acceptance contract.

## Evidence accounting

All paths below are relative to `C:\src\Antiphon`. Test data, raw reports,
transcripts, TRX and mutation logs remain gitignored. Deterministic model answers
are substituted only at the specialist result boundary; settlement, store,
request admission, hosted worker, database replacement and parent queue delivery
are real application code. Complete simulated `UserPrompt` evidence is asserted.
This is deliberately separate from the required real-model canary.

Each mutation runs in the disposable linked worktree
`.antiphon\verification\c419`, with fresh compilation on both the defective and
restored arms. `.antiphon\c419-control-evidence` contains exact diffs, full
assertions, native exit codes and distinct red/restored TRX. An empty filter,
compile/setup failure, watchdog exception or failed restoration is not a
qualified control. The committed control specification records the exact edits
and method filters for reproduction. Table counts overlap; only the separate
distinct-test totals should be added.

PC-9a removes both redundant linked-worktree refusals as one persistence-boundary
control. PC-21 changes semaphore acquisition and its matching release; PC-22
moves the validation await across that scope. PC-23b changes all duplicated
application-side equality comparisons together. PC-24b expires only the database
clock. PC-28a is split into database and runner identity subcontrols. These exact
equivalent edits are explicit in the committed specification; none is applied
to shared `master`.

The completed main/E2E TRX files are also copied byte-for-byte, with hash checks,
to `.antiphon\c419-deterministic-evidence\{main,e2e}`. The committed per-test index
points to that archive so cleanup of isolated build outputs does not erase its
evidence.

Automatic approval review refused the scoped cleanup of the main checkout's
`bin-c419` and `bin-c419-live` directories with `blocked by policy`; these
gitignored build outputs remain. No production output directories were targeted.
The clean, owned mutation worktree was removed with `git worktree remove` after
all 48 controls completed; its copied evidence remains in the paths above.

Implementation commits are `1ba060b6` (production changes and initial tests),
`4f53ff85` (expanded delivery/canary evidence), `31a2c9e4` (canonical canary
identity and real SendNow submission during file validation), and `a691ac62`
(independent missing-next and missing-handoff gate rows). Initial controls
used the first candidate; later controls used the corresponding committed test
expansions. The production implementation is unchanged across these checkpoints.

EF's generated upgrade script was inspected separately: it contains only
`ALTER TABLE "AgentTasks" ALTER COLUMN "Goal" TYPE text` and its migration-history
insert, within a transaction. Evidence: `.antiphon\c419-migration.sql` and
`.antiphon\c419-migration-script-fixed.log`. This was script generation, not a
production database update. The no-build CLI invocation needed both process-local
`OutputPath=bin-c419/` and `AppendTargetFrameworkToOutputPath=false` to select the
existing isolated binary. Its initial missing-deps-path failure is not a test pass.

<!-- GENERATED_VERIFICATION -->

Latest distinct deterministic results: **470 executed, 470 passed, 0 failed, 0 skipped**. Mutation controls: **48/48 executed, 48 qualified**. Across all latest control selections: 306 red-arm test rows (83 failures), 306 restored-arm rows (0 failures). **Live canary: 0 executed; pending.**

[Exact deterministic rows and TRX paths](2026-09-07-card-0419-test-results.json), [exact mutation edits and filters](2026-09-07-card-0419-mutation-controls.json), [full mutation assertions and native exits](2026-09-07-card-0419-mutation-results.json).

| ID | Implemented check | Executed rows / result | Scope, failure or limitation | Evidence |
|---|---|---|---|---|
| V-1 | `OutputDistillationPolicyTests.Defaults_separate_usefulness_from_transport` | 1 / 1 pass, 0 fail | Defaults and unchanged byte/character envelopes. | test-results.json; per-row TRX |
| V-2 | `OutputDistillationPolicyTests.Report_boundaries`<br>`OutputDistillationDeliveryTests.Report_boundaries` | 19 / 19 pass, 0 fail | 14 policy invocations exercise both profiles (28 cells); five real settlement/model boundaries. | test-results.json; per-row TRX |
| V-3 | `OutputDistillationDeliveryTests.Mode_and_terminal_status_preserve_report` | 8 / 8 pass, 0 fail | All enabled/mode/status combinations. | test-results.json; per-row TRX |
| V-4 | `OutputDistillationDeliveryTests.Legacy_minimum_and_unicode_boundaries`<br>`OutputDistillationPolicyTests.Unicode_length_is_not_utf8_bytes_or_tokens` | 7 / 7 pass, 0 fail | Explicit 1200 override and UTF-16 units. | test-results.json; per-row TRX |
| V-5 | `OutputDistillationDeliveryTests.Ineligible_reports_do_not_enter_canonical_or_model_work`<br>`OutputDistillationPolicyTests.Nonterminal_tasks_are_ineligible`<br>`OutputDistillationPolicyTests.Specialists_are_never_targets`<br>`AgentTaskReplyIntegrationTests.a_spill_file_the_delegate_wrote_itself_is_used_as_is`<br>`AgentTaskReplyIntegrationTests.an_oversized_report_is_backstopped_to_a_file_by_the_server` | 14 / 14 pass, 0 fail | Specialists/nonterminal/None plus retained legacy backstop. | test-results.json; per-row TRX |
| V-6 | `AgentReportStoreTests.Exact_content_and_full_ids_own_distinct_files` | 1 / 1 pass, 0 fail | Independent exact bytes, full GUID collisions and normalized-digest variants. | test-results.json; per-row TRX |
| V-7 | `AgentReportStoreTests.Root_selection_requires_a_durable_owned_location` | 11 / 11 pass, 0 fail | Eleven owned-root cases; relative root resolves to a valid fixture only on the defective arm. | test-results.json; per-row TRX |
| V-8 | `AgentReportStoreTests.Ignore_and_tracking_are_checked_before_writing` | 5 / 5 pass, 0 fail | Ignored/untracked before first report byte; tracked ignore unchanged. | test-results.json; per-row TRX |
| V-9 | `AgentReportStoreTests.Publication_exposes_only_a_complete_verified_report` | 1 / 1 pass, 0 fail | Barrier, retry, corruption, simultaneous publication and legacy preservation. | test-results.json; per-row TRX |
| V-10 | `OutputDistillationDeliveryTests.Canonical_file_survives_worktree_removal_and_provider_recreation` | 1 / 1 pass, 0 fail | Real Git worktree removal and provider recreation; no report rewrite. | test-results.json; per-row TRX |
| V-11 | `OutputDistillationDeliveryTests.Storage_failures_leave_a_recoverable_settled_report`<br>`OutputDistillationDeliveryTests.Host_cancellation_during_storage_can_retry_settlement_once`<br>`AgentReportStoreTests.Failures_and_cancellation_never_publish_a_bad_pointer`<br>`AgentReportStoreTests.Paths_over_the_persisted_limit_are_unavailable` | 16 / 16 pass, 0 fail | Bounded faults, path limit, cancellation and fresh-scope retry. | test-results.json; per-row TRX |
| V-12 | `OutputDistillationDeliveryTests.Modern_report_below_transport_limit_applies_with_canonical_file` | 1 / 1 pass, 0 fail | Actual simulated complete UserPrompt; authoritative raw unchanged. | test-results.json; per-row TRX |
| V-13 | `OutputDistillationDeliveryTests.Unusable_file_uses_api_recovery`<br>`OutputDistillationApplyRaceTests.Path_changed_after_validation_cannot_publish_stale_pointer`<br>`OutputDistillationDeliveryTests.Target_canonical_unavailable_retains_legacy_backstop_without_relabeling_author_file`<br>`OutputDistillationDeliveryTests.Canonical_selection_preserves_but_never_advertises_different_legacy_author_file` | 8 / 8 pass, 0 fail | Includes both required D1 precedence cases. | test-results.json; per-row TRX |
| V-14 | `OutputDistillationDeliveryTests.Apply_preserves_handoff_warnings_and_generated_deliverables`<br>`OutputDistillationDeliveryTests.Modern_report_below_transport_limit_applies_with_canonical_file` | 3 / 3 pass, 0 fail | Original warning/git/scope header; next/handoff; generated files once; empty-deliverable primary case. | test-results.json; per-row TRX |
| V-15 | `OutputDistillationDeliveryTests.Fallback_delivers_original_raw_or_marked_excerpt`<br>`OutputDistillationDeliveryTests.Admission_and_deadline_fallbacks_reach_parent` | 28 / 28 pass, 0 fail | Both profiles: six gate/result refusals and eight admission/deadline cases. Anchor is server/Program.cs. | test-results.json; per-row TRX |
| V-16 | `OutputDistillationApplyRaceTests.Apply_eligibility_matrix` | 14 / 14 pass, 0 fail | File-bearing matrix includes missing source/note; unrelated note explicitly unchanged. | test-results.json; per-row TRX |
| V-17 | `OutputDistillationApplyRaceTests.Apply_and_SendNow_preserve_one_complete_body`<br>`OutputDistillationApplyRaceTests.Apply_owns_the_delivery_session_lock_until_commit`<br>`OutputDistillationApplyRaceTests.File_validation_does_not_own_delivery_lock`<br>`OutputDistillationApplyRaceTests.Parent_poll_and_apply_have_one_ordered_winner`<br>`OutputDistillationApplyRaceTests.Source_row_lock_prevents_a_poll_commit_between_read_and_apply` | 7 / 7 pass, 0 fail | Both legal orders plus forced stale-poll control at the real row boundary. | test-results.json; per-row TRX |
| V-18 | `OutputDistillationApplyRaceTests.File_validation_consumes_original_deadline`<br>`OutputDistillationApplyRaceTests.Equality_and_postlock_expiry_never_apply`<br>`OutputDistillationApplyRaceTests.Database_clock_rejects_an_expired_body_update`<br>`OutputDistillationApplyRaceTests.Expired_lock_wait_never_applies`<br>`OutputDistillationApplyRaceTests.Expired_database_write_never_applies` | 6 / 6 pass, 0 fail | Original deadline/equality, post-lock no-UPDATE assertion and independently expired DB clock. | test-results.json; per-row TRX |
| V-19 | `OutputDistillationDeliveryTests.Provider_recreation_releases_expired_raw_note_once`<br>`OutputDistillationDeliveryTests.Expired_hold_is_deliverable_at_two_normal_flush_opportunities` | 2 / 2 pass, 0 fail | Hosted recovery without new TurnEnd/SendNow plus two completed normal flush opportunities; not CARD-0392 recovery. | test-results.json; per-row TRX |
| V-20 | `OutputDistillationDeliveryTests.Composed_summary_and_metadata_obey_utf8_spill_guard`<br>`OutputDistillationDeliveryTests.Same_root_applied_notes_batch_into_one_intact_spill_file` | 4 / 4 pass, 0 fail | Actual C-1/C/C+1 with Unicode/generated metadata; two same-root notes cross conservative C together. | test-results.json; per-row TRX |
| V-21 | `OutputDistillationDeliveryTests.File_read_does_not_claim_a_parent_api_poll`<br>`DistillationEndpointTests` | 6 / 6 pass, 0 fail | Raw/path API compatibility; parent-only sent-note metric. | test-results.json; per-row TRX |
| V-22 | `OutputDistillationCanaryGuardTests` | 33 / 33 pass, 0 fail | No-launch resource/approval/evidence guard cases. | test-results.json; per-row TRX |
| V-23 | `OutputDistillationApplyCanaryTests.Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup` | 0 / pending | PENDING MANDATORY LIVE ACCEPTANCE: approval file absent; no live execution or provider-availability claim. | test-results.json; per-row TRX |
| R-1 | `OutputDistillationPolicyTests`<br>`OutputDistillationDeliveryTests.Mode_and_terminal_status_preserve_report`<br>`OutputDistillationDeliveryTests.Modern_report_below_transport_limit_applies_with_canonical_file` | 33 / 33 pass, 0 fail | Usefulness independent of transport/mode. | test-results.json; per-row TRX |
| R-2 | `AgentReportStoreTests`<br>`OutputDistillationDeliveryTests.Canonical_file_survives_worktree_removal_and_provider_recreation` | 27 / 27 pass, 0 fail | Custody, exact identity and lifetime. | test-results.json; per-row TRX |
| R-3 | `OutputDistillationDeliveryTests.Modern_report_below_transport_limit_applies_with_canonical_file`<br>`OutputDistillationDeliveryTests.Apply_preserves_handoff_warnings_and_generated_deliverables` | 3 / 3 pass, 0 fail | Deterministic parent delivery proved; live arm V-23 remains pending. | test-results.json; per-row TRX |
| R-4 | `OutputDistillationAdmissionTests`<br>`OutputDistillationDeadlineTests`<br>`OutputDistillationCleanupTests`<br>`OutputDistillationDispatchTests`<br>`OutputDistillationTests`<br>`OutputDistillationDeliveryTests.Fallback_delivers_original_raw_or_marked_excerpt`<br>`OutputDistillationDeliveryTests.Admission_and_deadline_fallbacks_reach_parent`<br>`OutputDistillationDeliveryTests.Provider_recreation_releases_expired_raw_note_once` | 67 / 67 pass, 0 fail | Raw fallback, claims, bounded cleanup and standing ownership. | test-results.json; per-row TRX |
| R-5 | `PolledCompletionNoteShrinkTests`<br>`DistillationEndpointTests`<br>`OutputDistillationDeliveryTests.File_read_does_not_claim_a_parent_api_poll`<br>`OutputDistillationApplyRaceTests.Parent_poll_and_apply_have_one_ordered_winner`<br>`OutputDistillationApplyRaceTests.Source_row_lock_prevents_a_poll_commit_between_read_and_apply` | 17 / 17 pass, 0 fail | Parent poll serialization/suppression and API-only metrics. | test-results.json; per-row TRX |
| R-6 | `SessionMessageQueueSpillTests`<br>`PtyDeliveryCeilingsTests`<br>`SessionMessageQueueDeliveryVerificationTests`<br>`DelegationReportFormatterTests`<br>`OutputDistillationDeliveryTests.Composed_summary_and_metadata_obey_utf8_spill_guard`<br>`OutputDistillationDeliveryTests.Same_root_applied_notes_batch_into_one_intact_spill_file` | 102 / 102 pass, 0 fail | All six named transcript regressions; whole message versus canonical/inbox files. | test-results.json; per-row TRX |
| R-7 | `OutputDistillationGateTests`<br>`InstructionBundleTests`<br>`OutputDistillationDeliveryTests.Fallback_delivers_original_raw_or_marked_excerpt` | 117 / 117 pass, 0 fail | Pinned bundles and unchanged quality gates. | test-results.json; per-row TRX |
| R-8 | `OutputDistillationCanaryGuardTests` | 33 / 33 pass, 0 fail | Isolation guard verified; real parent/read/census acceptance remains pending. | test-results.json; per-row TRX |
| PC-1 | `/*/*/OutputDistillationDeliveryTests/Modern_report_below_transport_limit_applies_with_canonical_file` | 1/1 red; 1/1 restored; qualified | Modern_report_below_transport_limit_applies_with_canonical_file: ShouldAssertException: note.Body     should not contain (case insensitive comparison) "GET /api/agent-tasks/"     but was actually "[task 44d1aaef done] CARD-0320 concurrent settle · sonnet · 1s · report=marked · git=unattributable ..." | `.antiphon/c419-control-evidence/PC-1*`; mutation-results.json |
| PC-2 | `/*/*/OutputDistillationDeliveryTests/Mode_and_terminal_status_preserve_report` | 6/8 red; 8/8 restored; qualified | Mode_and_terminal_status_preserve_report(Shadow, True, False): ShouldAssertException: seed.Task.ResultFilePath     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\273cefa11adc48c8a31ba9e209f53ebe\main space é\.antiphon\reports\06759898-ca26-4bab-9b36-b4c44dc67603\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but was null; Mode_and_terminal_status_preserve_report(Shadow, False, False): ShouldAssertException: seed.Task.ResultFilePath     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\8e3d4b8dcf3d425e987b7377fe17ee46\main space é\.antiphon\reports\bead5c2c-9cd4-4158-9643-5f9d8f0cce5b\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but was null; Mode_and_terminal_status_preserve_report(Apply, False, False): ShouldAssertException: seed.Task.ResultFilePath     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\e383752920794c939480307648a098e2\main space é\.antiphon\reports\a0b05ad6-b6c3-4592-937d-e3dc4dae20de\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but was null; Mode_and_terminal_status_preserve_report(Shadow, True, True): ShouldAssertException: seed.Task.ResultFilePath     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\6079bfbc5ef941c99a522c2958629779\main space é\.antiphon\reports\95745c0d-becf-46c8-9f97-534317b9b851\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but was null; Mode_and_terminal_status_preserve_report(Shadow, False, True): ShouldAssertException: seed.Task.ResultFilePath     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\9523f7c9fb5d4bbba3054e63b304f6fe\main space é\.antiphon\reports\ed178cba-81a5-477e-a7f7-f048d83beaa2\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but was null; Mode_and_terminal_status_preserve_report(Apply, False, True): ShouldAssertException: seed.Task.ResultFilePath     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\e19e919039e94260a68d5b28da550c73\main space é\.antiphon\reports\b619a9b1-b1f0-40f2-8ec1-036d2c5e3b56\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but was null | `.antiphon/c419-control-evidence/PC-2*`; mutation-results.json |
| PC-3a | `/*/*/OutputDistillationDeliveryTests/Report_boundaries` | 1/5 red; 5/5 restored; qualified | Report_boundaries(4000, True): ShouldAssertException: seed.Task.ResultFilePath     should not be null but was | `.antiphon/c419-control-evidence/PC-3a*`; mutation-results.json |
| PC-3b | `/*/*/OutputDistillationDeliveryTests/Report_boundaries` | 1/5 red; 5/5 restored; qualified | Report_boundaries(20000, True): ShouldAssertException: early     should be null but was Antiphon.Server.Domain.Entities.OutputDistillationRecord (54262172)  Additional Info:     an eligible source must create a specialist task; an early skip/refusal is not an attempt | `.antiphon/c419-control-evidence/PC-3b*`; mutation-results.json |
| PC-4 | `/*/*/OutputDistillationPolicyTests/Unicode_length_is_not_utf8_bytes_or_tokens` | 1/2 red; 2/2 restored; qualified | Unicode_length_is_not_utf8_bytes_or_tokens(3999): ShouldAssertException: AgentReportPolicy.ShouldStore(Target(text), new())     should be False     but was True | `.antiphon/c419-control-evidence/PC-4*`; mutation-results.json |
| PC-5 | `/*/*/OutputDistillationDeliveryTests/Canonical_file_survives_worktree_removal_and_provider_recreation` | 1/1 red; 1/1 restored; qualified | Canonical_file_survives_worktree_removal_and_provider_recreation: ShouldAssertException: seed.Task.ResultFilePath     should not be null but was  Additional Info:     settlement must retain a canonical file before worktree removal | `.antiphon/c419-control-evidence/PC-5*`; mutation-results.json |
| PC-6 | `/*/*/AgentReportStoreTests/Exact_content_and_full_ids_own_distinct_files` | 1/1 red; 1/1 restored; qualified | Exact_content_and_full_ids_own_distinct_files: ShouldAssertException: result.Path     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\70c323589de049b5a71d46937a469d37\main space é\.antiphon\reports\12345678-1111-1111-1111-111111111111\9e4758eff9812d592614daf6351993b177d2194199fa6732aa05b440031ba8db.md"     but was "C:\src\Antiphon\.antiphon\verification\c4 | `.antiphon/c419-control-evidence/PC-6*`; mutation-results.json |
| PC-7 | `/*/*/AgentReportStoreTests/Exact_content_and_full_ids_own_distinct_files` | 1/1 red; 1/1 restored; qualified | Exact_content_and_full_ids_own_distinct_files: ShouldAssertException: result.Path     should be "C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\55268e86f02a498abe1a4efa1fb4c141\main space é\.antiphon\reports\12345678-1111-1111-1111-111111111111\9e4758eff9812d592614daf6351993b177d2194199fa6732aa05b440031ba8db.md"     but was "C:\src\Antiphon\.antiphon\verification\c4 | `.antiphon/c419-control-evidence/PC-7*`; mutation-results.json |
| PC-8a | `/*/*/AgentReportStoreTests/Ignore_and_tracking_are_checked_before_writing` | 1/5 red; 5/5 restored; qualified | Ignore_and_tracking_are_checked_before_writing(tracked): ShouldAssertException: await w.GitAsync(w.Main, "ls-files", "--", path)     should be empty but was ".antiphon/reports/a0dbf8b9-d0bc-4a58-8307-e9fb8992fb5f/da78da457a1cb0b8d4978029840c2f41e9793ad3b93a45032aba1ec2fc35c210.md " | `.antiphon/c419-control-evidence/PC-8a*`; mutation-results.json |
| PC-8b | `/*/*/AgentReportStoreTests/Ignore_and_tracking_are_checked_before_writing` | 1/5 red; 5/5 restored; qualified | Ignore_and_tracking_are_checked_before_writing(negation): ShouldAssertException: process.ExitCode     should be 0     but was 1  Additional Info:      | `.antiphon/c419-control-evidence/PC-8b*`; mutation-results.json |
| PC-9a | `/*/*/AgentReportStoreTests/Root_selection_requires_a_durable_owned_location` | 1/11 red; 11/11 restored; qualified | Root_selection_requires_a_durable_owned_location(worktree-override): ShouldAssertException: result.Succeeded     should be False     but was True  Additional Info:     worktree-override | `.antiphon/c419-control-evidence/PC-9a*`; mutation-results.json |
| PC-9b | `/*/*/AgentReportStoreTests/Root_selection_requires_a_durable_owned_location` | 1/11 red; 11/11 restored; qualified | Root_selection_requires_a_durable_owned_location(temp): ShouldAssertException: result.Succeeded     should be False     but was True  Additional Info:     temp | `.antiphon/c419-control-evidence/PC-9b*`; mutation-results.json |
| PC-9c | `/*/*/AgentReportStoreTests/Root_selection_requires_a_durable_owned_location` | 1/11 red; 11/11 restored; qualified | Root_selection_requires_a_durable_owned_location(relative): ShouldAssertException: result.Succeeded     should be False     but was True  Additional Info:     relative | `.antiphon/c419-control-evidence/PC-9c*`; mutation-results.json |
| PC-10 | `/*/*/AgentReportStoreTests/Publication_exposes_only_a_complete_verified_report` | 1/1 red; 1/1 restored; qualified | Publication_exposes_only_a_complete_verified_report: ShouldAssertException: await File.ReadAllTextAsync(path)     should be "exact full report é\ud83d\ude42"     but was "partial"     difference Difference     /  /    /    /    /    /    /    /    /    /    /    /    /    /    /    /    /    /    /    /    /    /                   / \//  \//  \//  \//  \//  \//  \//  \//  \//  \//  \//  \//  \//  \// | `.antiphon/c419-control-evidence/PC-10*`; mutation-results.json |
| PC-11 | `/*/*/AgentReportStoreTests/Publication_exposes_only_a_complete_verified_report` | 1/1 red; 1/1 restored; qualified | Publication_exposes_only_a_complete_verified_report: ShouldAssertException: try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); File.Exists(path)     should be False     but was True | `.antiphon/c419-control-evidence/PC-11*`; mutation-results.json |
| PC-12a | `/*/*/AgentReportStoreTests/Failures_and_cancellation_never_publish_a_bad_pointer` | 7/7 red; 7/7 restored; qualified | Failures_and_cancellation_never_publish_a_bad_pointer(write): ShouldAssertException: ct.CanBeCanceled     should be True     but was False  Additional Info:     storage phases share the bounded cooperative token; Failures_and_cancellation_never_publish_a_bad_pointer(denied): ShouldAssertException: ct.CanBeCanceled     should be True     but was False  Additional Info:     storage phases share the bounded cooperative token; Failures_and_cancellation_never_publish_a_bad_pointer(publish): ShouldAssertException: ct.CanBeCanceled     should be True     but was False  Additional Info:     storage phases share the bounded cooperative token; Failures_and_cancellation_never_publish_a_bad_pointer(root): ShouldAssertException: ct.CanBeCanceled     should be True     but was False  Additional Info:     storage phases share the bounded cooperative token; Failures_and_cancellation_never_publish_a_bad_pointer(readback): ShouldAssertException: ct.CanBeCanceled     should be True     but was False  Additional Info:     storage phases share the bounded cooperative token; Failures_and_cancellation_never_publish_a_bad_pointer(budget): ShouldAssertException: ct.CanBeCanceled     should be True     but was False  Additional Info:     storage phases share the bounded cooperative token; Failures_and_cancellation_never_publish_a_bad_pointer(host-cancel): ShouldAssertException: Task `store.StoreAsync(task, host.Token)`     should throw System.OperationCanceledException     but threw Shouldly.ShouldAssertException | `.antiphon/c419-control-evidence/PC-12a*`; mutation-results.json |
| PC-12b | `/*/*/AgentReportStoreTests/Paths_over_the_persisted_limit_are_unavailable` | 1/1 red; 1/1 restored; qualified | Paths_over_the_persisted_limit_are_unavailable: ShouldAssertException: result.Path     should be "path-too-long"     but was "verification-failed"     difference Difference     /  /    /    /    /    /    /    /    /    /    /              /    /    /    /    /    /    /                   / \//  \//  \//  \//  \//  \//  \//  \//  \//  \//            \//  \//  \//  \//  \//  \//  \//   Index      | `.antiphon/c419-control-evidence/PC-12b*`; mutation-results.json |
| PC-13 | `/*/*/OutputDistillationDeliveryTests/Modern_report_below_transport_limit_applies_with_canonical_file` | 1/1 red; 1/1 restored; qualified | Modern_report_below_transport_limit_applies_with_canonical_file: ShouldAssertException: note.Body     should contain (case insensitive comparison) "Full report: C:\src\Antiphon\.antiphon\verification\c419\.antiphon\test-output\card-0419\8bb2bf01c0844b11bc6143896cb86c35\main space é\.antiphon\reports\80bf53f0-a7ea-43fa-9c62-d042378d9495\c9c32e88a70f1ffe3d9e093c91697f2aa7d162c4c5bd7a0f92cdde58e197e340.md"     but  | `.antiphon/c419-control-evidence/PC-13*`; mutation-results.json |
| PC-14a | `/*/*/OutputDistillationDeliveryTests/Unusable_file_uses_api_recovery` | 3/4 red; 4/4 restored; qualified | Unusable_file_uses_api_recovery(deleted): ShouldAssertException: note.Body     should contain (case insensitive comparison) "GET /api/agent-tasks/"     but was actually "[task fe144190 done] CARD-0320 concurrent settle · sonnet · 0s · report=marked · git=unattributable ..."; Unusable_file_uses_api_recovery(corrupt): ShouldAssertException: note.Body     should contain (case insensitive comparison) "GET /api/agent-tasks/"     but was actually "[task 41a786b1 done] CARD-0320 concurrent settle · sonnet · 0s · report=marked · git=unattributable ..."; Unusable_file_uses_api_recovery(unreadable): ShouldAssertException: note.Body     should contain (case insensitive comparison) "GET /api/agent-tasks/"     but was actually "[task 45ff10cc done] CARD-0320 concurrent settle · sonnet · 0s · report=marked · git=unattributable ..." | `.antiphon/c419-control-evidence/PC-14a*`; mutation-results.json |
| PC-14b | `/*/*/OutputDistillationApplyRaceTests/Path_changed_after_validation_cannot_publish_stale_pointer` | 1/1 red; 1/1 restored; qualified | Path_changed_after_validation_cannot_publish_stale_pointer: ShouldAssertException: note.Body     should not contain (case insensitive comparison) "C:\Users\lndco\AppData\Local\Temp\antiphon-distiller-wire4pkqfo5x.vmw\report.md"     but was actually "[task ffedea85 done] seeded  - Landed CARD-0330 at a1b2c3d4e5f6789. See https://example.com/x. Cost ..." | `.antiphon/c419-control-evidence/PC-14b*`; mutation-results.json |
| PC-15 | `/*/*/OutputDistillationDeliveryTests/Apply_preserves_handoff_warnings_and_generated_deliverables` | 2/2 red; 2/2 restored; qualified | Apply_preserves_handoff_warnings_and_generated_deliverables(False): ShouldAssertException: note.Body.Split("[[attach: " + path + "]]", StringSplitOptions.None).Length     should be 2     but was 1; Apply_preserves_handoff_warnings_and_generated_deliverables(True): ShouldAssertException: note.Body.Split("[[attach: " + path + "]]", StringSplitOptions.None).Length     should be 2     but was 1 | `.antiphon/c419-control-evidence/PC-15*`; mutation-results.json |
| PC-16 | `/*/*/OutputDistillationDeliveryTests/Modern_report_below_transport_limit_applies_with_canonical_file` | 1/1 red; 1/1 restored; qualified | Modern_report_below_transport_limit_applies_with_canonical_file: ShouldAssertException: (await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id)).Result     should be "Completed the report. ordinary explanatory words ordinary explanatory words ordinary explanatory words ordinary explanatory words ordinary explanatory words ordinary explanatory words ordinary explanatory words ordinary explanatory words ordina | `.antiphon/c419-control-evidence/PC-16*`; mutation-results.json |
| PC-17 | `/*/*/OutputDistillationDeliveryTests/Fallback_delivers_original_raw_or_marked_excerpt` | 8/12 red; 12/12 restored; qualified | Fallback_delivers_original_raw_or_marked_excerpt(missing-path, True): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedOverCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(missing-next, True): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedOverCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(missing-handoff, True): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedOverCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(oversized, True): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedUnderCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(missing-path, False): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedOverCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(missing-next, False): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedOverCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(missing-handoff, False): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedOverCompressed     but was DistillationOutcome.Applied; Fallback_delivers_original_raw_or_marked_excerpt(oversized, False): ShouldAssertException: ledger.Outcome     should be DistillationOutcome.RejectedUnderCompressed     but was DistillationOutcome.Applied | `.antiphon/c419-control-evidence/PC-17*`; mutation-results.json |
| PC-18a | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(wrong-source): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-18a*`; mutation-results.json |
| PC-18b | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(wrong-digest): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-18b*`; mutation-results.json |
| PC-18c | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(wrong-report): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-18c*`; mutation-results.json |
| PC-18d | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(wrong-origin): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-18d*`; mutation-results.json |
| PC-19a | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 2/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(canceled): ShouldAssertException: else { result     should not be null but was; Apply_eligibility_matrix(sent): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-19a*`; mutation-results.json |
| PC-19b | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(attempted): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-19b*`; mutation-results.json |
| PC-19c | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(shadow): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-19c*`; mutation-results.json |
| PC-20a | `/*/*/OutputDistillationApplyRaceTests/Apply_eligibility_matrix` | 1/14 red; 14/14 restored; qualified | Apply_eligibility_matrix(polled): ShouldAssertException: else { result     should not be null but was | `.antiphon/c419-control-evidence/PC-20a*`; mutation-results.json |
| PC-20b | `/*/*/OutputDistillationApplyRaceTests/Source_row_lock_prevents_a_poll_commit_between_read_and_apply` | 1/1 red; 1/1 restored; qualified | Source_row_lock_prevents_a_poll_commit_between_read_and_apply: ShouldAssertException: outcome     should be "full-report-read"     but was null  Additional Info:     a committed full-report poll must never be followed by stale summary replacement | `.antiphon/c419-control-evidence/PC-20b*`; mutation-results.json |
| PC-21 | `/*/*/OutputDistillationApplyRaceTests/Apply_owns_the_delivery_session_lock_until_commit` | 1/1 red; 1/1 restored; qualified | Apply_owns_the_delivery_session_lock_until_commit: ShouldAssertException: bypassed     should be False     but was True  Additional Info:     delivery must not enter the queue critical section while application owns its read/write decision | `.antiphon/c419-control-evidence/PC-21*`; mutation-results.json |
| PC-22 | `/*/*/OutputDistillationApplyRaceTests/File_validation_does_not_own_delivery_lock` | 1/1 red; 1/1 restored; qualified | File_validation_does_not_own_delivery_lock: ShouldAssertException: submitted.Task.IsCompleted     should be True     but was False  Additional Info:     SendNow must reach real protocol submission while only file validation is held | `.antiphon/c419-control-evidence/PC-22*`; mutation-results.json |
| PC-23a | `/*/*/OutputDistillationApplyRaceTests/File_validation_consumes_original_deadline` | 2/2 red; 2/2 restored; qualified | File_validation_consumes_original_deadline(False): ShouldAssertException: (await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),             seed.Digest, h.PassingDistillation(), CancellationToken.None))     should be "deadline"     but was null; File_validation_consumes_original_deadline(True): ShouldAssertException: (await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),             seed.Digest, h.PassingDistillation(), CancellationToken.None))     should be "deadline"     but was null | `.antiphon/c419-control-evidence/PC-23a*`; mutation-results.json |
| PC-23b | `/*/*/OutputDistillationApplyRaceTests/File_validation_consumes_original_deadline` | 1/2 red; 2/2 restored; qualified | File_validation_consumes_original_deadline(True): ShouldAssertException: (await queue.TryApplyDistillationAsync(new(seed.Task.Id, seed.QueuedMessageId, now, now.AddSeconds(45), OutputDistillerMode.Apply),             seed.Digest, h.PassingDistillation(), CancellationToken.None))     should be "deadline"     but was null | `.antiphon/c419-control-evidence/PC-23b*`; mutation-results.json |
| PC-24a | `/*/*/OutputDistillationApplyRaceTests/Equality_and_postlock_expiry_never_apply` | 1/1 red; 1/1 restored; qualified | Equality_and_postlock_expiry_never_apply: ShouldAssertException: probe.Count     should be 0     but was 1 | `.antiphon/c419-control-evidence/PC-24a*`; mutation-results.json |
| PC-24b | `/*/*/OutputDistillationApplyRaceTests/Database_clock_rejects_an_expired_body_update` | 1/1 red; 1/1 restored; qualified | Database_clock_rejects_an_expired_body_update: ShouldAssertException: await apply     should be "deadline"     but was null | `.antiphon/c419-control-evidence/PC-24b*`; mutation-results.json |
| PC-25a | `/*/*/OutputDistillationDeliveryTests/Fallback_delivers_original_raw_or_marked_excerpt` | 12/12 red; 12/12 restored; qualified | Fallback_delivers_original_raw_or_marked_excerpt(missing-path, True): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:16.1354460Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(missing-next, True): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:21.2418150Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(missing-handoff, True): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:25.4468550Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(oversized, True): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:28.9711300Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(empty, True): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:32.1815030Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(failed, True): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:35.2764090Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(missing-path, False): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:38.2947200Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(missing-next, False): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:41.6271900Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(missing-handoff, False): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:44.8807800Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(oversized, False): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:48.4316310Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(empty, False): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:51.5622320Z  Additional Info:     rejection cleanup must release the original hold before delivery; Fallback_delivers_original_raw_or_marked_excerpt(failed, False): ShouldAssertException: (await f.ReadNoteAsync(seed.NoteId)).HoldUntil     should be null but was 2026-09-07T17:44:55.3020110Z  Additional Info:     rejection cleanup must release the original hold before delivery | `.antiphon/c419-control-evidence/PC-25a*`; mutation-results.json |
| PC-25b | `/*/*/OutputDistillationDeliveryTests/Expired_hold_is_deliverable_at_two_normal_flush_opportunities` | 1/1 red; 1/1 restored; qualified | Expired_hold_is_deliverable_at_two_normal_flush_opportunities: ShouldAssertException: note.DeliveryVerdict     should be DeliveryVerdict.Delivered     but was null  Additional Info:     two completed normal flush opportunities must not strand an expired hold | `.antiphon/c419-control-evidence/PC-25b*`; mutation-results.json |
| PC-26 | `/*/*/OutputDistillationDeliveryTests/Composed_summary_and_metadata_obey_utf8_spill_guard` | 1/3 red; 3/3 restored; qualified | Composed_summary_and_metadata_obey_utf8_spill_guard(1): ShouldAssertException: wire.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal)     should be True     but was False | `.antiphon/c419-control-evidence/PC-26*`; mutation-results.json |
| PC-27 | `/*/*/SessionMessageQueueDeliveryVerificationTests/A_clipped_prefix_parks_as_truncated_not_sent` | 1/1 red; 1/1 restored; qualified | A_clipped_prefix_parks_as_truncated_not_sent: ShouldAssertException: dto.Messages.Count     should be 1     but was 0 | `.antiphon/c419-control-evidence/PC-27*`; mutation-results.json |
| PC-28a | `/*/*/OutputDistillationCanaryGuardTests/Configuration_refuses_unowned_resources_before_start` | 1/12 red; 12/12 restored; qualified | Configuration_refuses_unowned_resources_before_start(db): ShouldAssertException: `DistillerCanaryGuard.ValidateOptIns("1", scenario == "opt-in" ? null : "1"); approval = scenario switch { "expired" => approval with { ExpiresUtc = now }, "future" => approval with { ApprovedUtc = now.AddSeconds(1) }, "stale" => approval with { AvailabilityCheckedUtc = now.AddMinutes(-6) }, "refused" => approval with { Avail | `.antiphon/c419-control-evidence/PC-28a*`; mutation-results.json |
| PC-28a-runner | `/*/*/OutputDistillationCanaryGuardTests/Configuration_refuses_unowned_resources_before_start` | 1/12 red; 12/12 restored; qualified | Configuration_refuses_unowned_resources_before_start(runner): ShouldAssertException: `DistillerCanaryGuard.ValidateOptIns("1", scenario == "opt-in" ? null : "1"); approval = scenario switch { "expired" => approval with { ExpiresUtc = now }, "future" => approval with { ApprovedUtc = now.AddSeconds(1) }, "stale" => approval with { AvailabilityCheckedUtc = now.AddMinutes(-6) }, "refused" => approval with { Avail | `.antiphon/c419-control-evidence/PC-28a-runner*`; mutation-results.json |
| PC-28b | `/*/*/OutputDistillationCanaryGuardTests/Evidence_rejects_missing_or_wrong_delivery_proof` | 2/21 red; 21/21 restored; qualified | Evidence_rejects_missing_or_wrong_delivery_proof(assistant): ShouldAssertException: `evidence.Validate("distinctive middle", ["marker"])`     should throw System.InvalidOperationException     but did not; Evidence_rejects_missing_or_wrong_delivery_proof(screen): ShouldAssertException: `evidence.Validate("distinctive middle", ["marker"])`     should throw System.InvalidOperationException     but did not | `.antiphon/c419-control-evidence/PC-28b*`; mutation-results.json |
| PC-28c | `/*/*/OutputDistillationCanaryGuardTests/Evidence_rejects_missing_or_wrong_delivery_proof` | 1/21 red; 21/21 restored; qualified | Evidence_rejects_missing_or_wrong_delivery_proof(api): ShouldAssertException: `evidence.Validate("distinctive middle", ["marker"])`     should throw System.InvalidOperationException     but did not | `.antiphon/c419-control-evidence/PC-28c*`; mutation-results.json |
| PC-29a | `/*/*/OutputDistillationGateTests/dropping_next_or_handoff_from_a_present_block_is_over_compressed` | 1/2 red; 2/2 restored; qualified | dropping_next_or_handoff_from_a_present_block_is_over_compressed(next): ShouldAssertException: result.Verdict     should be DistillationGateVerdict.RejectedOverCompressed     but was DistillationGateVerdict.Pass | `.antiphon/c419-control-evidence/PC-29a*`; mutation-results.json |
| PC-29b | `/*/*/OutputDistillationGateTests/dropping_next_or_handoff_from_a_present_block_is_over_compressed` | 1/2 red; 2/2 restored; qualified | dropping_next_or_handoff_from_a_present_block_is_over_compressed(handoff): ShouldAssertException: result.Verdict     should be DistillationGateVerdict.RejectedOverCompressed     but was DistillationGateVerdict.Pass | `.antiphon/c419-control-evidence/PC-29b*`; mutation-results.json |

## Corrections and limits

Initial fixture failures included an ignore-negation setup without parent
unignores, a missing normal distiller cleanup in the poll-first fixture, and a
restart fixture that had no pre-existing idle-parent baseline. They were fixed
without changing production delivery semantics. The 20,000-character failure
required the migration above. An attempted combined class filter selected zero
tests; it is excluded from all verification totals and was replaced with exact
class filters.

The new same-root batch fixture initially stopped the hosted worker after its
first source, which closes the in-memory request channel. It now keeps that
same worker alive through both sources; the corrected case passes. The first
PC-15 runner restoration removed a source file's UTF-8 BOM and was refused by
the exact-diff check, before a restored test run. That interrupted attempt is
retained under `.antiphon\c419-control-evidence\first-PC-15`. The runner now
preserves encoding on mutation and restores the original bytes exactly.

The first PC-29a survived because the existing unit method named
`dropping_next_or_handoff_from_a_present_block_is_over_compressed` only omitted
handoff. The new V-15 delivery fixtures already covered missing next. The unit
method now has separate next/handoff argument rows and starts from a proven
passing candidate; this closes the selection gap without changing production
gates. The first unqualified pair is retained under
`.antiphon\c419-control-evidence\first-PC-29a`.

The first PC-14a restoration failed on two null fixture paths before its intended
assertions. Its evidence is preserved under
`.antiphon\c419-control-evidence\first-PC-14a`; that pair is not qualified. The
disposable repository now starts with the real checkout's `.antiphon/` ignore,
avoiding unrelated exclude creation in delivery tests. The explicit exclude,
negation and tracked-file tests still replace that setup and exercise custody.
No production I/O deadline was increased.

The final 103-row gate run had a long quiet test-host phase, then completed with
103 passes in 7m42s. An owned-process diagnostic dump was collected while waiting;
`dumpasync` showed the TUnit scheduling phase, without establishing the cause.
The test completed without terminating it or restarting Docker or the local stack.

These tests do not prove recovery of interrupted distillation requests or a
lost in-memory request/ledger. V-19 demonstrates finite raw fallback after the
original hold; it does not close CARD-0392. Nor do they guarantee retention after
the canonical root is deleted, prevent an external actor changing files after
validation, count exact model tokens or observe every filesystem read.

## D3: effect on the Shadow cohort

Measured at `2026-09-07T16:00:53.8969266Z` through the documented
`GET /api/distillations?since=2026-08-31T16:00:53.5415786Z&limit=200` route.
The response contained 162 rows, all Shadow, below the requested 200-row cap.
Aggregate-only evidence is `.antiphon\c419-shadow-cohort.json`.

| Same pre-deployment cohort | Minimum 1,200 | Minimum 4,000 |
|---|---:|---:|
| Attempted reports within the 20,000-character cap that qualify | 125 | 55 |
| Share of all 162 Shadow rows | 77.16% | 33.95% |

This is **56% fewer eligible attempts** (70 of 125) under the proposed minimum.
It is a counterfactual filter on the same existing cohort, not an observed
post-deployment rate. CARD-0330's week-of-ledger assessment should segment at the
default change and allow for fewer samples. Existing explicit minimum overrides
are not rewritten.

## D2: the remaining live gate and authoring path

The **operator** authors
`C:\src\Antiphon\.antiphon\acceptance\card-0419\approval.json` after a scoped
human decision. The Code delegate does not manufacture that decision or file.
Use the committed [approval example](../examples/card-0419-canary-approval.example.json):
replace its deliberately invalid `EXAMPLE` reference and expired timestamps;
record `approvalReference`, `approvedUtc`, `expiresUtc`, `expectedSourceRole`,
`expectedDistillerKind`, `expectedDistillerModelAlias`, `availabilityCheckedUtc`
and `availabilityVerdict`. The record must name Review, ClaudeCode and the
approved Low model alias, and contain an allowed availability check no older
than five minutes at validation. A differing resolved model refuses the run.

After the approved record exists, run from the main checkout, preserving and
restoring any prior values of these three test-only environment variables:

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
$env:ANTIPHON_DISTILLER_APPLY_CANARY = '1'
$env:ANTIPHON_DISTILLER_CANARY_APPROVAL_FILE = 'C:\src\Antiphon\.antiphon\acceptance\card-0419\approval.json'
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c419-live/ -- --treenode-filter '/*/*/OutputDistillationApplyCanaryTests/Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup' --report-trx --report-trx-filename card-0419-live.trx
```

The approval file was absent during this Code stage. **V-23 is pending mandatory
live acceptance: zero live runs and zero model spend by this stage.** The
implemented canary does not establish real provider availability or runtime
success until executed. Its intended spend is two source tasks, one ordinary
cheap distillation and real parent boot/ack/read turns, without automatic retries.
The generated sanitized acceptance document is intentionally absent until success.

Broad Apply rollout and closure also need reviewed CARD-0392 recovery evidence
and the human's acceptance of honest raw/excerpt fallback when a summary cannot
pass the existing gate. Both release conditions remain pending; no production
Apply claim is made here.

## Reproduction

Run TUnit through `dotnet run`, with fixed isolated output folders, exact class
filters and unique TRX filenames. Execute the main and E2E test assemblies
sequentially. The main new classes are `OutputDistillationPolicyTests`,
`AgentReportStoreTests`, `OutputDistillationDeliveryTests` and
`OutputDistillationApplyRaceTests`; use the same form for each retained class
listed in the evidence table:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c419/ -- --treenode-filter '/*/*/OutputDistillationDeliveryTests/*' --report-trx --report-trx-filename card-0419-delivery-rerun.trx
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-c419-live/ -- --treenode-filter '/*/*/OutputDistillationCanaryGuardTests/*' --report-trx --report-trx-filename card-0419-guards-rerun.trx
```

The plan calls the formatting regression file `DelegationUnitTests.cs`; its
actual class is `DelegationReportFormatterTests`. Use that class in filters.
No client, namespace-wide, all-provider or full-suite claim is made.
