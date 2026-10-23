# RC fixture repair mutation candidates

Original Code / landing owner: a5f9c44c. Unbound candidate only. Every control and variant below is **PENDING** for method-scoped SourceLanding Mutation after ordinary Review and landing. Code ran no deliberate mutant. Mutation owns binding, missing-control discovery and red/restore/green evidence.

| PC | Guarded defect / variants | Exact method(s) in Antiphon.Tests.Application |
|---|---|---|
| PC-1 | Hold dispatch repository lease through baseline capture; primary / repair-source uncached objects | RepairSourceDispatchTests.C1553_ReleasedDispatchLeaseFetchesUncachedBaseline |
| PC-2 | Treat a lease-busy observation as Present; primary / repair-source absent-object cases | RepairSourceDispatchTests.C1115_PrimaryBaselinePinsOnlyPresentObservations; RepairSourceDispatchTests.C1115_RepairBaselinePinsOnlyPresentObservations |
| PC-3 | Credit remote movement without a Present baseline; primary / repair-source false variants | RepairSourceDispatchTests.C1115_MovedPrimaryRemoteRequiresPresentBaseline; RepairSourceDispatchTests.C1115_MovedRepairRemoteRequiresPresentBaseline |
| PC-4 | Broaden runners fault to unrelated AgentTasks counts; runners false / true | OrchestratorInstructionsRefreshTests.Failed_reads_are_unknown_in_file_and_notice_then_recover |
| PC-5 | Restore optional index writes in the production probe (account for both CLI and environment guards); inherited optional-locks=0 control / probe index timestamp | DelegateCheckProbeTests.a_status_probe_leaves_the_git_index_untouched |
| PC-6 | Omit supported idle Claude dialog observation; unknown/deduplication/recovery variants | OrchestratorInstructionsRefreshTests.Failed_reads_are_unknown_in_file_and_notice_then_recover |

Each cycle must select the exact class and method, e.g. `/*/*/RepairSourceDispatchTests/C1553_ReleasedDispatchLeaseFetchesUncachedBaseline`, with a bound pack. Do not substitute build/fixture errors or zero executions for an expected assertion failure. These candidates do not authorize changing production timeouts or assertions.
