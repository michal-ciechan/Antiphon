# CARD-1718 A-D3 round 2 — unbound Mutation candidate

Status: every row/variant PENDING. This document binds no pack or landed snapshot. Ordinary
Code runs and historical pre-fix assertion failures do not discharge Mutation. Review the final
source first; after the caller lands original Code owner `5e05fc80-930f-4f03-8c71-6c323d552d63`,
commission method-scoped SourceLanding Mutation with the actual landed snapshot and external
evidence root. Mutation owns deliberate changes, red/restore/green, and missing-control discovery.
No mutation was performed in this Code resume.

For every cycle, use `--treenode-filter "/*/*/ClassName/ExactTestMethod"`, include every
parameter case of that method, prove the expected assertion is red, restore production bytes,
then run the same selection green. Fixture/build failures and zero tests are not red. Never
widen a timeout or weaken an assertion. This candidate does not change inherited PC obligations.

| ID / variants | Deliberate defect candidate | Exact method witness | State |
|---|---|---|---|
| Historical M1 / M2 | Reintroduce the original secret-name/default-disposal gaps described by Review 47d31366; bind their precise edits from the original D3 plan before execution | WorktreeIgnoredContentClassifierTests.Secret_shaped_name_inside_disposable_directory_is_protected; WorktreeIgnoredContentClassifierTests.Operator_disposable_override_cannot_release_a_secret_shaped_name | PENDING; inherited IDs retained |
| Historical PEM-removal | Remove PEM protection from the floor | WorktreeIgnoredContentClassifierTests.Secret_shaped_name_inside_disposable_directory_is_protected | PENDING |
| Historical protected-after-disposable | Classify disposable before protected on the server | WorktreeIgnoredContentClassifierTests.Operator_disposable_override_cannot_release_a_secret_shaped_name | PENDING |
| PC-D3-1 / admission | Omit the ignored-secret admission check | C1677RetentionEngineTests.Ignored_secret_files_survive_admission_on_dry_run_and_execute | PENDING |
| PC-D3-2 / locked | Omit the floor on the locked second CheckAsync reading | C1677RetentionEngineTests.Appsettings_secret_survives_every_retention_boundary | PENDING |
| PC-D3-3 / phase-B | Omit the CompleteAsync ignored-secret check | C1677RetentionEngineTests.Phase_b_keeps_an_ignored_secret_planted_after_the_move | PENDING |
| PC-D3-4 / recovery-DryRun, recovery-Execute | Omit residue inspection before a DryRun answer or removal handoff; bind a separate edit for each reachable guard | C1677RetentionEngineTests.Residue_retry_keeps_an_ignored_secret | PENDING (both variants) |
| PC-D3-5 / unreadable-list | Treat a failed ignored enumeration as empty | C1677RetentionEngineTests.Unavailable_ignored_list_keeps_the_tree | PENDING |
| PC-D3-6 / runner-floor | Remove shared minima from RunnerWorkspaceIgnoredContent | RunnerWorkspaceIgnoredContentFloorTests.Disposable_override_cannot_release_a_secret_shaped_name | PENDING |
| PC-D3-7 / credentials-descendants | Remove `**/credentials*/**` from the shared floor | CompletedCardWorkspaceRemovalTests.Secret_floor_survives_transport_inspection_and_replay | PENDING |
| PC-D3-8 / PEM-descendants | Remove `**/*.pem/**` from the shared floor | WorktreeGuardedCleanupTests.Secret_directory_descendants_survive_each_cleanup_reading | PENDING |
| PC-D3-9 / appsettings | Remove the appsettings rules from the shared floor (keep other secret patterns intact) | C1677RetentionEngineTests.Appsettings_secret_survives_every_retention_boundary | PENDING |
| PC-D3-10 / initial-transport, final-transport | Remove the appropriate real completed-card inspection or floor check; confirm each variant has a discriminating oracle before binding | CompletedCardWorkspaceRemovalTests.Secret_floor_survives_transport_inspection_and_replay | PENDING (both variants) |
| PC-D3-11 / active-recipient-replay | Bypass active runner floor checks without bypassing framing; one independent secret must be physically removed or incorrectly cleared by replay | C1677RetentionServerTests.Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay | PENDING |

The 68 S4a controls and extra mutants in its separate plan are not discharged by running
C0824 ordinary classes here. Preserve their own ownership and existing evidence; this task
makes no new passed claim for them. Native Windows cases stay pending during live qualification.

Known interpretation limit: removing only an initial Execute guard may be masked by a later
guard. DryRun and deterministic late writes are intentional discriminators. Mutation must
record missing controls rather than declaring a non-red edit covered by green ordinary tests.
