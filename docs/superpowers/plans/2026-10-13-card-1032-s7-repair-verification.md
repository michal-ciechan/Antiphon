# CARD-1032 S7 repair F1-F3 verification

Repair Code / landing owner: 60025819-39c5-49e6-8951-817cdeeff5fc.
Repair base: cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c; full task base:
63b2e70479c846cacd01af6b20190e845f8258e4. Add commits without rebasing.
Review: /tmp/antiphon-review-e0b648d4/review.md, findings F1-F3.

## Verification design

Tests-first group selects CP-29,CP-30,CP-31 over unchanged production.
Final repaired group selects every row below once. CP-13/14 retain their frozen
seven-method filters. New methods deliberately use Report rather than Admission
prefixes so those counts remain seven. The full mirror class now has 17 methods;
the server three-class selection has 59 results, runner four-class selection 144.
No Unit lane: the repair brief explicitly excludes it. No full assembly or
namespace run is needed. Two isolated builds are necessary because the named
server and runner test classes live in separate test projects; server rows reuse
one build, runner rows use one build. The checkpoint bootstrap is the sole
unlisted build, necessary to run the tool, through the host slot gate.

F1 pins a hand-written shape/case table and real unique-byte preservation for
lowercase, uppercase, mixed-case, nonhex and Unicode identifier characters,
and the previous regex's single terminal-LF match.
The existing runner wildcard accepts eight non-separator characters, not only
hex; preserve that conservative evidence policy. Unicode lookalikes in the
directory, prefix or extension, ADS suffixes, trailing dot/space, other
extensions and a directory-shaped path do not match. Windows normalized filename
behavior still requires native qualification; literal strings are portable policy
evidence. F2 gates after committed producer intent and adds the report before
the real recipient request, with no sleep. Assertions cover exact bytes, actual
registration survival and no imported Complete receipt. An absent-report positive
still removes the fixture tree and imports the real durable receipt.

Implementation uses one contract predicate at producer and runner, preserves
the existing runner report shape, and holds task reports in fresh no-follow
recipient inventories before destructive work. The command has no per-report
byte manifest, so generic artifact pointers cannot certify remote task reports.
F3 distinguishes direct H/P ancestry from signed H-to-P mapping plus P ancestry.
Production composition remains inert, with no timeout/assertion relaxation.

Coverage: V-1 endpoint/generation/binding (CP-13,F2); V-2 command/receipt/unsupported
(CP-14,F3); V-3 source and report preservation (CP-13,29,30,31,F3); V-4 ownership
and V-5 component recovery (adjacent CP-F3, without new native qualification);
V-6 committed intent before real socket send and fresh DB receipt reads
(CP-13,14,30,F2). R-1/R-2 adjacent local/legacy regressions (CP-F2,F3);
R-3 mirror/recipient compatibility (CP-13,14,29..31,F2,F3).
Manual acceptance: inspect composition and the complete S7 safety-language diff.
No session input/notification flow changed; no new UserPrompt acceptance.
S8 CP-15/16, native Windows share/handle/reparse cases and activation remain
deferred to their existing owners, never reported passed by this Linux repair.
Restart: none. All existing PCs/variants and PC-591..593 below remain pending
for method-scoped SourceLanding Mutation; ordinary defect red is not Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-29 | S7-repair | `tests/Antiphon.Tests -> bin-c60025819-reports/` | report-case-shape | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable*` | V-3, R-3, F1 | exact method; 0 failed/skipped after repair | 1 | 5 | true |
| CP-30 | S7-repair | CP-29 | late-report | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection*` | V-3, V-6, R-3, F2 | exact method; 0 failed/skipped after repair | 1 | 2 | true |
| CP-31 | S7-repair | CP-29 | absent-report | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup*` | V-3, R-3 | exact positive; 0 failed/skipped | 1 | 1 | true |
| CP-13 | S7-repair | CP-29 | mirror-admission | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1032_Admission*)` | V-1, V-3, V-6, R-3 | all 7 listed; 0 failed/skipped | 7 | 2 | true |
| CP-14 | S7-repair | CP-29 | mirror-receipt | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1032_Receipt*)` | V-2, V-6, R-3 | all 7 listed; 0 failed/skipped | 7 | 3 | true |
| CP-F2 | S7-repair | CP-29 | local-adjacent | `/*/*/(CardWorktreeCleanupRecoveryTests*)\|(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-1..6, R-1..3 | all three complete classes, 59 results; 0 failed/skipped | 59 | 4 | true |
| CP-F3 | S7-repair | `tests/Antiphon.SessionRunner.Tests -> bin-c60025819-runner/` | runner-adjacent | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(CompletedCardWorkspaceContractTests*)\|(RunnerWorkspaceServiceTests*)\|(LegacyWorkspaceRemovalGateTests*)/*` | V-2..5, R-1, R-2 | all four complete classes, 144 results; 0 failed/skipped | 144 | 5 | true |
| CP-RG1 | S7-repair | CP-29 | classification-guard | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | metadata | exact guard; 0 failed/skipped | 1 | 1 | true |

### Pending Mutation additions

| PC | Deliberate defect | Detecting method filter | Variants / expected red |
|---|---|---|---|
| PC-591 | Producer uses lowercase-only report recognition or mismatched shape | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable` | uppercase, mixed-case, nonhex, Unicode identifier, terminal LF; report bytes absent or no-intent refusal missing; literal 24-row classifier table variants |
| PC-592 | Recipient omits fresh report-preservation hold | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection` | lowercase, uppercase, mixed-case late report; unique bytes/registration absent or imported Complete receipt present |
| PC-593 | Refuse generic admitted evidence when no remote task report exists | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup` | real tree/registration removal and imported durable receipt missing |
