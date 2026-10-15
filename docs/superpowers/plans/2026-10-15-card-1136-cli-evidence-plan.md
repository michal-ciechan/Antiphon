# CARD-1136 F5: CLI status and build-slot documentation pins

Code owner: `039bb1d0-287d-4398-83ce-57540438dbb1`.
Task base: `8ffe3633357e6383b7e408bd078465acf9f5fd79`.
Parent: [CARD-1082 follow-ups](2026-10-07-card-1082-followups-plan.md), F5 / D-7.

This is the next unfinished CARD-1136 slice: item 4 and item 1b. The F3 fixture
repairs from Review 260e7059 are already landed as ca1108d6576c0451f68b9be431a401cf59580ae4.
Held debt clearance (items 2-3) still requires registration identity under CARD-1148;
this slice does not claim to clear it. CARD-1125's mirror-removal part of the
multi-card F5 slice is separate. No server or runner restart is needed.

The explicit task brief limits ordinary verification to closed checkpoint rows
and the existing classes found by the affected-file search, with no whole Unit
lane (CARD-1287). This table specializes parent CP-9/CP-10 for CARD-1136, replacing
their dependency on the unrelated CARD-1125 CP-8 build. It does not claim the
parent's multi-card final CP-11..CP-15 certificate or discharge other slices.

## Verification design

V-15: Run the real `delegate.ps1 -Status` with a loopback `LandApiStub`, for
pending-with-debt, pending-without-debt, and synchronized. Compare complete output
lines, distinguish observed from mirror/confirmed SHA, require the correct
absence of the debt/reply lines, require facts before the report, and verify
that only the task-detail GET was issued. The existing CLI already implements
this behavior; its regression test is a coverage addition, not a production fix.
PC-11 remains the method-scoped Mutation red/restore/green obligation.

V-16: Add the parent plan's exact chained-operator pitfall sentence under Build
slots and pin it with a test. Commit tests first, run CP-9/CP-10 against the
missing sentence, then add the sentence and rerun those rows. The doc assertion
must fail for the missing contract, not a build or fixture problem.

R-10 (CARD-1136 subset): Run the complete CLI and documentation classes.
R-12: Run all existing direct consumers identified by the affected-file search.
Search tests for the edited filenames, test identifiers, and the strings
`Runner sync: Pending`, `Desktop sync debt:`, `Pending evidence without a debt row`,
and `Never chain a second driver`. Direct consumers of `testing-and-build.md`
are included even when they pin other sections. `AgentTaskReplyIntegrationTests`
only embeds that filename in synthetic report text; it does not read the file.
Plan-coverage frozen `.md.txt` fixtures are historical input, not live consumers.
No production path or shared helper changes, so no unbounded integration impact
or full-assembly run is required. The whole parent R-10 includes unrelated policy
and progress classes and is not claimed by this scoped slice.

### Checkpoints

One committed test slice runs CP-9 and CP-10 red-first; the committed documentation
fix runs all three rows. Keep every receipt and inspect the fresh TRX roster.
CP-EXTRA repeats the new methods only as part of the required full affected
classes; no unchanged selection is repeated after green otherwise.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-9 | F5 | `tests/Antiphon.Tests -> bin-c1136-f5/` | cli-status | `/*/*/DelegateScriptLandStatusTests/C1136_StatusPrintsSyncDebtAndPendingLines` | V-15 | exact 3 results, 0 failed/skipped | 3 | 8 | true |
| CP-10 | F5 | CP-9 | docs-pitfall | `/*/*/(CheckpointManifestDocumentationTests)\|(RunnerBranchContractDocumentationTests)/*` | V-16, V-7 | exact 16 results, 0 failed/skipped | 16 | 2 | true |
| CP-EXTRA | F5 | CP-9 | affected-classes | `/*/*/(DelegateScriptLandStatusTests)\|(CheckpointManifestDocumentationTests)\|(RunnerBranchContractDocumentationTests)\|(ScopedVerificationInstructionTests)\|(AdvisoryChangePolicyTests)\|(MechanicalPcContractTests)\|(CheckpointRepeatDocumentationTests)\|(TaskPlatformGuidanceTests)\|(RunnerDefaultGuidanceTests)\|(DockerStackDocumentationTests)\|(GrokRunnerImageContractTests)\|(RunnerPushCredentialDocsTests)\|(PackageVersionPinContractTests)/*` | R-10 subset, R-12 | all listed classes, 0 failed/skipped | 124 | 5 | true |

Expected cost: 15 minutes per full group including one isolated build; the first
red-first group omits CP-EXTRA. No timeout increase or assertion relaxation.
The checkpoint executable needs one explicitly declared bootstrap build through
`scripts/build-slot.ps1` into `bin-c1136-tool/`; this is not acceptance evidence.

### Mutation handoff

Parent PC-1 through PC-11 and all variants remain pending; no deliberate mutant
is part of this Code slice. PC-11 now has its intended real-command detector:
`/*/*/DelegateScriptLandStatusTests/C1136_StatusPrintsSyncDebtAndPendingLines`.
Changing Pending's origin to confirmedSha must fail the two Pending cases; the
synchronized case is the negative control. Mutation owns any missing controls.

### Ordinary outcomes

The committed evidence report is `.antiphon/task-039bb1d0.md`. Later source
qualification facts belong in the final stored task report; receipts always
retain their actual tested SHA. No parent V/R outside the scoped rows is passed
by implication.
