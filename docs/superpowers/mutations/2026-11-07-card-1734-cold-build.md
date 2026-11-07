# CARD-1734 unbound Mutation candidate

Original Code/landing owner: a38720f2. SourceLanding binding belongs to the caller.
Plan: ../plans/2026-11-07-card-1734-checkpoint-cold-build-plan.md.
Every control below is pending; ordinary red/green does not discharge Mutation.

| ID/variant | Guarded behavior | Deliberate mutant | Exact method filter |
|---|---|---|---|
| PC-1/tool | Native obj byte preservation, staged helper, valid terminal report | Remove `-p:UseArtifactsOutput=true` and `-p:ArtifactsPath=` from the `UseArtifactsOutput == true` Exec in the shipped target | `/*/*/CheckpointColdBuildTests/C1734_Cold_build_preserves_native_obj_and_terminal_receipt` |
| PC-1/script | Same argument forwarding through run-checkpoint.ps1 | Same mutant; the two argument cases may share one red/restore/green cycle | `/*/*/CheckpointColdBuildTests/C1734_Cold_build_preserves_native_obj_and_terminal_receipt` |
| PC-2 | Real tracked tampering remains refused with diagnostic and valid RED report | Suppress BuildConfinement's changed-path tampering classification; a source-changed exit alone must not satisfy the exact JSON refusal assertion | `/*/*/CheckpointColdBuildTests/C1734_Real_tampering_keeps_a_red_terminal_report` |
| PC-3/stop | Stop publishes a canceled terminal receipt | Omit the stop terminal write | `/*/*/CheckpointTerminalReceiptTests/C1734_Stop_publishes_before_the_executor_waiter_returns` |
| PC-3/cancel | Cancellation publishes its non-passing receipt | Omit cancellation fallback publication | `/*/*/CheckpointTerminalReceiptTests/C1734_Executor_terminal_receipt_reaches_a_real_waiter` |
| PC-4 | Cancellation cannot qualify as passed | Write GREEN/exit 0 on cancellation; separately retain canceled reason with forged green | `/*/*/CheckpointTerminalReceiptTests/C1734_Stop_replaces_only_its_bound_provisional_reports` |
| PC-5 | Waiter cannot outrun stop publication | Omit the waiter terminal gate | `/*/*/CheckpointTerminalReceiptTests/C1734_Stop_publishes_before_the_executor_waiter_returns` |
| PC-6 | Completed counts survive interrupted execution | Discard known TRX results in fallback | `/*/*/CheckpointTerminalReceiptTests/C1734_Partial_counts_survive_failure_and_cancel` |
| PC-7 | Provisional-report replacement retains custody | Permit replacement of foreign receipt bytes | `/*/*/CheckpointTerminalReceiptTests/C1734_Stop_replaces_only_its_bound_provisional_reports` |
| PC-8/changed, PC-8/unknown | Terminal recovery preserves adverse observed source history after restoration | Omit source-history preservation in fallback | `/*/*/CheckpointSourceExecutionTests/C1734_Failure_receipt_preserves_observed_source_history` |

Review-proposed publication/digest controls remain pending for Mutation's missing-
control discovery. Every row/variant above remains pending, including after Code
ordinary greens. SourceLanding Mutation owns deliberate mutants/red/restore/green.

The fixture executes a verbatim copy of the shipped MSBuild target. The first
ordinary run on S1 proved both PC-1 variants fail at the native obj snapshot.
Its third failure was a harness diagnostic-location error: when tracked source
changes, the Markdown row is replaced by source_changed. S2 checks the exact
JSON build SlotReason and the named TAMPERED build-log path instead. It still
requires exit 2, valid terminal report/state binding and retained changed bytes.
No timeout or custody assertion is relaxed. Mutation owns all deliberate source
mutants, missing-control discovery, restore records and final green receipts.
