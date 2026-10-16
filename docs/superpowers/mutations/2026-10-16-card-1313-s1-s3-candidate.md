# CARD-1313 S1-S3 Mutation candidate

Original Code/landing owner: `209c77e6-2f0d-4da5-910a-c931d34aa655`.
Branch: `feat/card-task-209c77e6`. Target: master. Restart: none.

This is an **unbound candidate**, not an executable SourceLanding pack or a qualification receipt.
Mutation is paused by the Code brief. Every control and variant below is **pending**, with zero
mutants executed. The exact semantic defects and detecting filters remain owned by the
[landed verification design](../plans/2026-10-16-card-1313-change-aware-selection-plan.md#positive-controls).
Mutation must bind minimal compiling patches, restored fixture digests, expanded counts and
current landed source before execution; it owns missing-control discovery. Do not infer control
sensitivity from ordinary green or from the three legacy characterization reds.

S1-S3 implementation sites available for binding:

| Controls | Production/data site |
|---|---|
| PC-1, PC-4, PC-5 | `scripts/lib/nightly-policy.ps1`: project census/roles, supported versions and policy hash |
| PC-2, PC-70 | `tests/test-execution-policy.json` exclusions and `tests/test-selection-policy.json` resource/platform entries |
| PC-3 | `ClaudeVerifiedDeliveryTests.Real_evidence_appears_then_submit_completes_a_turn`: batch argument metadata |
| PC-6..PC-22, PC-71..PC-73, PC-79 | `scripts/lib/test-selection.ps1`: `Get-TestSelection`, `Invoke-TestSelectionGit`, `Assert-TestSelectionCatalog`; catalog rules/edges |
| PC-23 | `scripts/lib/nightly-coverage.ps1`: `New-NightlyExactRoster`; `scripts/lib/nightly-tests-impl.ps1`: exact argument compiler/owned execution. Bind around the independent filter validation; an early unrelated refusal is not the forbidden-sibling witness. |
| PC-24..PC-28 | `scripts/lib/nightly-coverage.ps1`: `Test-NightlyExactCoverage` |
| PC-29 | `New-NightlyExactRoster` unknown UID broadening and `Invoke-NightlyExactRoster` prelaunch hold |
| Remaining controls | S4-S7 are outside this increment and remain unimplemented/binding required |

Ordinary test targets use the plan's exact method names. `C1313_PathIdentity` has three
expanded rows (delete, case-rename, nul-paths); other targets have one outer execution.
Windows fake delivery/discovery and cross-platform rows are not discharged by Linux metadata.
SourceLanding must use its external evidence root and restore without committing its snapshot.

| PC | Variant | Detecting method | Assertion | Status |
|---|---|---|---|---|
| PC-1 | G-1: ignore an unknown fixture-looking project in the project-role validator | `TestSelectionCatalogTests.C1313_ProjectAndCatalogUniverse` | `unclassified-project` | Pending |
| PC-2 | G-2: admit the exact ClaudeHeadedLongTests row as unattended without its gate | `TestSelectionCatalogTests.C1313_LiveEligibility` | `live-obligations-visible` | Pending |
| PC-3 | G-3: drop only the real batch Arguments attribute (compiles) | `ClaudeVerifiedDeliveryTests.C1313_DeliveryDiscovery` | `five-delivery-uids` | Pending |
| PC-4 | G-4: add fixture schema 7 to the supported versions | `ReleaseGatePolicyTests.C599_ProfileSchema` | `C599 ProfileSchema unknown version refuses` | Pending |
| PC-5 | G-5: bypass the canonical hash inequality check | `ReleaseGatePolicyTests.C599_ProfileSchema` | `C599 ProfileSchema stale hash refuses` | Pending |
| PC-6 | G-6: resolve the valid fixture's 12-hex candidate abbreviation instead of refusing it, then continue with that commit | `TestSelectionTests.C1313_InputRefusal` | `invalid-intent-refused` | Pending |
| PC-7 | G-7: convert a nonzero diff exit into an empty path list | `TestSelectionTests.C1313_InputRefusal` | `git-read-refused` | Pending |
| PC-8 | G-8: read the edited working-tree catalog instead of the candidate blob | `TestSelectionTests.C1313_PinnedPolicy` | `candidate-blob` | Pending |
| PC-9 | G-9: return an empty selected set for B=A | `TestSelectionTests.C1313_CoreAndUnknownFallback` | `core-seven` | Pending |
| PC-10 | G-10: return core-only for the unknown fixture path | `TestSelectionTests.C1313_CoreAndUnknownFallback` | `unknown-full` | Pending |
| PC-11 | G-11: omit the selection-authority-changed full fallback for a changed catalog | `TestSelectionTests.C1313_AuthorityChanges` | `authority-full` | Pending |
| PC-12 | G-12: remove the ancestry-to-Full branch | `TestSelectionTests.C1313_AuthorityChanges` | `nonancestor-full` | Pending |
| PC-13 | G-13: look up deletion ownership only in A | `TestSelectionTests.C1313_PathIdentity` | `old-new-union` | Pending |
| PC-14 | G-14: lowercase parsed Git paths before ownership lookup | `TestSelectionTests.C1313_PathIdentity` | `ordinal-paths` | Pending |
| PC-15 | G-15: classify the unlisted ClaudeNewShared fixture by its prefix | `TestSelectionDependencyTests.C1313_ProviderAndShared` | `shared-all-providers` | Pending |
| PC-16 | G-16: drop the one fixture linked-Compile edge | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `linked-consumer` | Pending |
| PC-17 | G-17: drop the staged-child edge while leaving normal references intact | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `staged-consumer` | Pending |
| PC-18 | G-18: drop the helper import edge | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `helper-consumer` | Pending |
| PC-19 | G-19: drop the client/dist runtime edge | `TestSelectionDependencyTests.C1313_RuntimeEdges` | `bundle-consumer` | Pending |
| PC-20 | G-20: stop matching after the first provider rule | `TestSelectionDependencyTests.C1313_ProviderAndShared` | `full-dominates` | Pending |
| PC-21 | G-21: ignore a missing prerequisite target | `TestSelectionDependencyTests.C1313_InvalidGraph` | `invalid-graph` | Pending |
| PC-22 | G-22: catch regex validation/timeout and treat it as no match | `TestSelectionDependencyTests.C1313_InvalidGraph` | `invalid-rule` | Pending |
| PC-23 | G-23: restore the legacy class-prefix star for the colliding class | `TestSelectionExecutionTests.C1313_ExactExpandedExecution` | `forbidden-not-launched` | Pending |
| PC-24 | G-24: ignore the missing UID set | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `missing-uid` | Pending |
| PC-25 | G-25: ignore the extra executed UID set | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `extra-uid` | Pending |
| PC-26 | G-26: deduplicate duplicate terminal UIDs silently | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `duplicate-uid` | Pending |
| PC-27 | G-27: normalize required Skipped to Passed | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `required-skip` | Pending |
| PC-28 | G-28: accept a zero-required/zero-executed ledger | `TestSelectionExecutionTests.C1313_CoverageRefusal` | `zero-required` | Pending |
| PC-29 | G-29: assign the new fixture UID not-impacted instead of suite remainder | `TestSelectionExecutionTests.C1313_NewUidRemainder` | `new-uid-required` | Pending |
| PC-30 | G-30: assign caseDurationSumSeconds to caseWindowSeconds | `TestSetTimingTests.C1313_WallEnvelopes` | `wall-not-sum` | Pending |
| PC-31 | G-31: coalesce an unavailable wall measurement to zero | `TestSetTimingTests.C1313_UnavailableMeasurements` | `unknown-not-zero` | Pending |
| PC-32 | G-32: accept the fixture foreign assembly digest in the metadata join | `TestSetTimingTests.C1313_DiscoveryJoin` | `category-build-bound` | Pending |
| PC-33 | G-33: enable both tool and wrapper row completion writers | `TestSetTimingTests.C1313_ProducerReceipts` | `one-row-producer` | Pending |
| PC-34 | G-34: append terminal completion before releasing/joining the held child | `TestSetTimingTests.C1313_ProducerReceipts` | `completion-after-join` | Pending |
| PC-35 | G-35: allow ../escape as the run component | `TimingJournalTests.C1313_RootCustody` | `path-component-refused` | Pending |
| PC-36 | G-36: accept the output-contained hosted root | `TimingJournalTests.C1313_RootCustody` | `root-outside-custody` | Pending |
| PC-37 | G-37: include the external timing shard in the producer cleanup list | `TimingJournalTests.C1313_RootCustody` | `history-survives-output` | Pending |
| PC-38 | G-38: return append success before the injected flush barrier completes | `TimingJournalTests.C1313_RecoveryHandoffs` | `flush-before-ack` | Pending |
| PC-39 | G-39: append an exact duplicate event instead of recognizing it | `TimingJournalTests.C1313_ReplayIdentity` | `replay-once` | Pending |
| PC-40 | G-40: accept the changed payload with the same event id | `TimingJournalTests.C1313_ReplayIdentity` | `event-conflict` | Pending |
| PC-41 | G-41: restart sequence at zero for an existing shard | `TimingJournalTests.C1313_ReplayIdentity` | `sequence-resumed` | Pending |
| PC-42 | G-42: remove spool after a failed append | `TimingJournalTests.C1313_RecoveryHandoffs` | `recoverable-handoff` | Pending |
| PC-43 | G-43: synthesize success for a start-only attempt | `TimingJournalTests.C1313_RecoveryHandoffs` | `unfinished-not-success` | Pending |
| PC-44 | G-44: overwrite failed test outcome with pass when the timing writer fails | `TimingJournalTests.C1313_RecoveryHandoffs` | `persistence-not-test-outcome` | Pending |
| PC-45 | G-45: ignore a malformed complete middle record | `TimingJournalTests.C1313_TornTail` | `corrupt-not-truncated` | Pending |
| PC-46 | G-46: omit membership digest from the comparability key | `TestTimingReportTests.C1313_ComparableHistory` | `comparable-series` | Pending |
| PC-47 | G-47: filter failed attempts out of total incurred cost | `TestTimingReportTests.C1313_AttemptCost` | `attempt-cost` | Pending |
| PC-48 | G-48: drop an expensive set when its p90 exceeds the budget | `TestTimingReportTests.C1313_EstimatesNeverSelect` | `budget-never-omits` | Pending |
| PC-49 | G-49: execute the proposed narrow union in Shadow | `ChangeAwareReleaseTests.C1313_ModeCredit` | `shadow-full` | Pending |
| PC-50 | G-50: treat absence/revocation of activation as qualified | `ChangeAwareReleaseTests.C1313_ModeCredit` | `unqualified-no-credit` | Pending |
| PC-51 | G-51: accept a ChangeAware receipt as the Full baseline | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `baseline-full-only` | Pending |
| PC-52 | G-52: keep narrow mode when baseline receipt is absent | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `baseline-absent-full` | Pending |
| PC-53 | G-53: treat a foreign baseline digest as missing and fall back silently | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `baseline-corrupt-refused` | Pending |
| PC-54 | G-54: accept a baseline with a changed selector contract hash | `ChangeAwareReleaseTests.C1313_BaselineAuthority` | `contract-hash-bound` | Pending |
| PC-55 | G-55: accept discovery built at B for candidate A | `ChangeAwareReleaseTests.C1313_CandidateBinding` | `candidate-build-bound` | Pending |
| PC-56 | G-56: trust the edited not-impacted list after its local hashes are repaired | `ChangeAwareReleaseTests.C1313_ScopeAuthority` | `forged-omission-refused` | Pending |
| PC-57 | G-57: emit coverageScope=Full for a result with omissions | `ChangeAwareReleaseTests.C1313_ScopeAuthority` | `selected-not-full` | Pending |
| PC-58 | G-58: admit new scoped authority through the legacy Full reader | `ChangeAwareReleaseTests.C1313_ScopeAuthority` | `legacy-rejects-scoped` | Pending |
| PC-59 | G-59: omit BaselineSha only from the self-reexec argument list | `ChangeAwareReleaseTests.C1313_ParameterHops` | `hop-baseline-bound` | Pending |
| PC-60 | G-60: reuse the selection after the aggregate membership digest changes | `ChangeAwareReleaseTests.C1313_TrainAggregate` | `aggregate-bound` | Pending |
| PC-61 | G-61: reuse narrow selection after recomposition failure | `ChangeAwareReleaseTests.C1313_TrainAggregate` | `recomposition-full` | Pending |
| PC-62 | G-62: skip production authority/plan creation while retaining exit-zero child results | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | `producer-ledger-present` | Pending |
| PC-63 | G-63: mark report complete when the actual queue/write fails | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | `report-pending-until-receipt` | Pending |
| PC-64 | G-64: allocate a new report identity after recipient commit before receipt persistence | `ReleaseGateReportDeliveryTests.C1313_ProductionReceipt` | `one-logical-report` | Pending |
| PC-65 | G-65: accept a final/foreign receipt kind as prepublication | `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedgerReceipt` | `C599 G-306: a final or foreign receipt kind where the prepublication receipt is required refuses` | Pending |
| PC-66 | G-66: use the local ledger copy when recipient GET fails | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: an unavailable, failed, unknown or non-object release card readback refuses (fail closed)` | Pending |
| PC-67 | G-67: ignore the current-card revision inequality | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: a receipt revision other than the release card current revision refuses` | Pending |
| PC-68 | G-68: ignore a foreign runId in the card body | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: a release card body without exactly this run correlation refuses` | Pending |
| PC-69 | G-69: copy host identity from a timing row into the manifest | `ChangeAwareReleaseTests.C1313_PublicProjection` | `public-allowlist` | Pending |
| PC-70 | G-70: mark the Linux-only PDF method runnable on the Windows profile | `TestSelectionCatalogTests.C1313_LiveEligibility` | `other-os-obligation` | Pending |
| PC-71 | G-71: default the unknown resource fixture to no prerequisites | `TestSelectionCatalogTests.C1313_LiveEligibility` | `unknown-resource-ineligible` | Pending |
| PC-72 | G-72: treat the back edge of the two-node prerequisite cycle as already satisfied | `TestSelectionDependencyTests.C1313_InvalidGraph` | `cycle-refused` | Pending |
| PC-73 | G-73: catch the bounded matcher timeout as a successful non-match | `TestSelectionDependencyTests.C1313_InvalidGraph` | `regex-timeout-refused` | Pending |
| PC-74 | G-74: join the two colliding display names instead of expanded UID | `TestSetTimingTests.C1313_DiscoveryJoin` | `category-uid-bound` | Pending |
| PC-75 | G-75: ignore a dirty candidate observation while preserving its SHA | `ChangeAwareReleaseTests.C1313_CandidateBinding` | `candidate-clean-required` | Pending |
| PC-76 | G-76: skip the returned-card id comparison | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | `C599 V-12: a readback of any card but the bound release card refuses` | Pending |
| PC-77 | G-77: remove the shard writer lock while holding two append calls at the sequence barrier | `TimingJournalTests.C1313_ReplayIdentity` | `serialized-shard` | Pending |
| PC-78 | G-78: skip completion emission for the build-failed path | `TestSetTimingTests.C1313_ProducerReceipts` | `failed-attempt-recorded` | Pending |
| PC-79 | G-79: accept a script-block rule and evaluate it in the controlled fixture | `TestSelectionDependencyTests.C1313_InvalidGraph` | `data-not-code` | Pending |
| PC-80 | G-80: write an active qualification record as a side effect of selection | `ChangeAwareReleaseTests.C1313_ModeCredit` | `qualification-root-unchanged` | Pending |
| PC-81 | G-81: launch the child before releasing the start-event flush barrier | `TestSetTimingTests.C1313_ProducerReceipts` | `start-before-launch` | Pending |
