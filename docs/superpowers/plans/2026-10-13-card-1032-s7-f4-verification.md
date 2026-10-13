# CARD-1032 S7 F4 negative-witness verification

Code task: 55328335-fca8-474e-a307-7a5fc84b37ff, continuing landing owner
60025819-39c5-49e6-8951-817cdeeff5fc. Repair base:
1383709fc74516e4b79570d3bd880ed9bea23aaf; full task base:
63b2e70479c846cacd01af6b20190e845f8258e4. Commits added without rebase.
Finding: Final Review 75a50129 F4, from scratch probe 045e9719 survivors
VP-08, VP-09, VP-10, VP-13 and VP-15.

## Verification design

Five negative witnesses, one per surviving family. Each pairs its refusal
vectors with a real positive in the same method so a broken fixture cannot
pass as a guard:

- CP-33 `C1032_LandingSignatureCoversTaskId` (VP-08): sign a rebased command,
  verify true; change only `Binding.TaskId` (other GUID, `Guid.Empty`) and
  `Verify` must be false; wrong key false.
- CP-34 `C1032_WitnessLandingOwnership` (VP-09): an actual server landing maps
  mirror H onto rebased P. The same row, made internally consistent for a
  foreign task (TaskId plus recovery prefix), a foreign store path, a foreign
  repository or an earlier generation, must refuse `unlanded_work` with no
  intent, no recipient request, journal empty and evidence intact. The
  restored row then completes real rebased removal.
- CP-35 `C1032_WitnessLandingCompleteness` (VP-10): 13 missing/empty
  landing-evidence fields outside the narrower eligibility checks
  (observed remote target null/empty, verifiedAt, confirmation method
  null/empty, recovery prefix, common/git directory, target-before SHA,
  destination ref, source/target/prepared pins) each refuse as above; the
  restored row completes.
- CP-36 `C1032_WitnessPersistedReplayTamper` (VP-13): a real durable intent
  (stop after commit), then four valid-shaped persisted-request edits
  (report digest, policy digest, binding worktree path, expected HEAD) without
  updating RequestDigest. Re-entry holds `immutable_request_unreadable`
  before any recipient request; bytes, digest, operation and IntentRecorded
  remain. Restoring the original bytes replays to real removal.
- CP-37 `C1032_WitnessUnsupportedRemovalAfterInspection` (VP-15): real
  inspection and intent, then the runner's removal service is withdrawn so
  the real recipient answers unsupported; then intercepted unsupported,
  unknown and absent refusals. Each keeps mirror, registration, sentinel and
  journal, refuses with the reply reason (absent: `receipt_mismatch`) and
  imports no receipt. The restored runtime replays the same operation to
  real removal.

The fixture gains only a removal-request counter and a refusal-only removal
interceptor (it asserts the intercepted reply is not Complete). Production is
unchanged: every witness was green on unchanged production, so no hole was
found. Production composition remains inert.

New method names avoid the Admission/Receipt/Report prefixes, so CP-13/14
stay seven and CP-29..31 stay one. CP-F2 grows by four (63); CP-F3 by one
(145). Two isolated builds are needed because the server and runner classes
live in separate test projects; each is reused by its rows. The checkpoint
tool bootstrap is the only setup build outside the table. No Unit lane: the
brief excludes it.

Coverage: V-1 (CP-13, F2, 34); V-2 (CP-14, F3, 33, 36, 37); V-3 (CP-13,
29..32, 34, 35, F3); V-4/V-5 adjacent (CP-F3, 32); V-6 (CP-13, 14, 30, 36,
F2). R-1/R-2 adjacent (CP-F2, F3); R-3 (CP-13, 14, 29..37, F2, F3).
Restart: none. Every PC stays pending for method-scoped SourceLanding
Mutation; scratch red here is Code evidence, not Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-29 | S7-F4 | `tests/Antiphon.Tests -> bin-c55328335-server/` | report-case-shape | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable*` | V-3, R-3, F1 | exact method; 0 failed/skipped | 1 | 5 | true |
| CP-30 | S7-F4 | CP-29 | late-report | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection*` | V-3, V-6, R-3, F2 | exact method; 0 failed/skipped | 1 | 2 | true |
| CP-31 | S7-F4 | CP-29 | absent-report | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup*` | V-3, R-3 | exact positive; 0 failed/skipped | 1 | 1 | true |
| CP-13 | S7-F4 | CP-29 | mirror-admission | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1032_Admission*)` | V-1, V-3, V-6, R-3 | all 7 listed; 0 failed/skipped | 7 | 2 | true |
| CP-14 | S7-F4 | CP-29 | mirror-receipt | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1032_Receipt*)` | V-2, V-6, R-3 | all 7 listed; 0 failed/skipped | 7 | 3 | true |
| CP-34 | S7-F4 | CP-29 | landing-ownership | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingOwnership*` | V-1, V-3, R-3, F4 | exact method; 0 failed/skipped | 1 | 2 | true |
| CP-35 | S7-F4 | CP-29 | landing-completeness | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingCompleteness*` | V-3, R-3, F4 | exact method; 0 failed/skipped | 1 | 3 | true |
| CP-36 | S7-F4 | CP-29 | replay-tamper | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessPersistedReplayTamper*` | V-2, V-6, R-3, F4 | exact method; 0 failed/skipped | 1 | 2 | true |
| CP-37 | S7-F4 | CP-29 | unsupported-removal | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessUnsupportedRemovalAfterInspection*` | V-2, R-3, F4 | exact method; 0 failed/skipped | 1 | 2 | true |
| CP-F2 | S7-F4 | CP-29 | local-adjacent | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-1..6, R-1..3 | all three complete classes, 63 results; 0 failed/skipped | 63 | 5 | true |
| CP-RG1 | S7-F4 | CP-29 | classification-guard | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | metadata | exact guard; 0 failed/skipped | 1 | 1 | true |
| CP-32 | S7-F4 | `tests/Antiphon.SessionRunner.Tests -> bin-c55328335-runner/` | report-and-evidence | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical*` | V-3, V-5, R-3 | exact method; 0 failed/skipped | 1 | 3 | true |
| CP-33 | S7-F4 | CP-32 | signature-task-id | `/*/*/CompletedCardWorkspaceContractTests/C1032_LandingSignatureCoversTaskId*` | V-2, R-3, F4 | exact method; 0 failed/skipped | 1 | 1 | true |
| CP-F3 | S7-F4 | CP-32 | runner-adjacent | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(CompletedCardWorkspaceContractTests*)\|(RunnerWorkspaceServiceTests*)\|(LegacyWorkspaceRemovalGateTests*)/*` | V-2..5, R-1, R-2 | all four complete classes, 145 results; 0 failed/skipped | 145 | 3 | true |

### Pending Mutation additions

| PC | Deliberate defect | Detecting method filter | Variants / expected red |
|---|---|---|---|
| PC-595 | Landing MAC omits/normalizes Binding.TaskId | `/*/*/CompletedCardWorkspaceContractTests/C1032_LandingSignatureCoversTaskId` | other TaskId; empty TaskId; Verify returns true |
| PC-596 | Publication lookup ignores landing task ownership | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingOwnership` | foreign task; also discover controls for foreign store path, foreign repository, earlier generation; rebased removal completes |
| PC-597 | Publication omits complete landing-evidence predicate | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessLandingCompleteness` | each of the 13 field vectors; rebased removal completes |
| PC-598 | Replay trusts schema-valid persisted bytes without RequestDigest | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessPersistedReplayTamper` | report digest, policy digest, binding worktree path, expected HEAD; tampered request reaches recipient |
| PC-599 | Unsupported removal reply counted as success | `/*/*/CardWorktreeMirrorCleanupTests/C1032_WitnessUnsupportedRemovalAfterInspection` | real runtime unsupported, intercepted unsupported; discover controls for unknown and absent replies; Complete=true or receipt imported |
