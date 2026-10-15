# CARD-0987 S1a Mutation candidate (unbound)

Code/landing owner: `52bcdf87-94a4-45cf-a2de-4b51a0576b7f`.
Scope: S1a caller continuity, tool authority and CARD-1302 item 3.
Frozen authority: [test design](../plans/2026-10-15-card-0987-test-design.md).
Evidence: [Code evidence](../evidence/2026-10-15-card-0987-s1a-code.md).

All **66** controls below and every variant are **PENDING**. Every PC-1..413
in the full design remains pending; ordinary green does not discharge a PC.
SourceLanding Mutation owns deliberate mutants, exact-method red/restore/green,
restoration proof and missing-control discovery after Review and landing the
original Code task. This candidate is unbound and is not an executable pack.

Tests live in `tests/Antiphon.Tests/Infrastructure/` in the two named classes.
Each bound control must select exactly `/*/*/<ClassName>/<ExactMethod>`; no class
or suite selection. Keep all Arguments/internal cases of that method. Authority
assertion labels are `C987.<ExactMethod>.<variant>`; field guards recompute the seal
hash so a prior digest guard cannot mask their intended defect.

Bindings: PC-109/207 use EvidenceFolder/ToolCopyCleanup; PC-116/205 use Program.Stop;
PC-203/210 use WaitCommand; PC-204 uses CheckpointApp.CreateRun; PC-206 uses
CheckpointOutputCleanup.RemoveOlderRuns; PC-208/209/211 use ToolCopyCleanup.
PC-342..368 use ToolCopyAuthority, with PC-343 guarding creation-registry isolation.
PC-369..372 use WaitCommand; PC-373..386 use TestRootGuard.MarkDeleting and
CheckpointWrite.Text. PC-387..396 follow the named production caller in the table.

Binding clarifications for the unchanged frozen rows:
- PC-204 also has a current-context-owned dead sibling with a valid ledger; startup
  must retain it even though an explicitly requested cleanup could remove it.
- PC-208 has no production signaling seam in Sweep. Its live-owner primitive
  witness is paired with decoded IL call-reference rejection of process-control
  interfaces, implementations and Process.Kill. The bound mutant must actually
  introduce the forbidden signaling call; a disconnected constant is not a control.
- PC-353 includes empty and 10001-entry records. PC-351 includes escaping, rooted
  and backslash relative paths. PC-383 includes AliveSame, Unknown and ReusedPid.
- PC-393's landed clean front door intentionally retains outputs for manual
  removal; bind the dropped context at its real state/report reader before that
  refusal. Do not resurrect destructive clean behavior to match stale wording.
  The separate `output` case covers direct injectable CleanOwnedOutputs.
- PC-396 and PC-209 observe each timer registration before advancing fake time;
  a completed cleanup without a timer fails an assertion, without widening a timeout.

| PC (all pending) | Frozen compiling defect | Exact detecting method and variant | Decisive oracle |
|---|---|---|---|
| PC-109 | Delete report alongside tool in recovery. | `HostCleanupCheckpointCompatibilityTests.automatic_recovery_preserves_all_evidence` | all evidence hashes remain identical. |
| PC-116 | Signal before checking expected start token. | `HostCleanupCheckpointCompatibilityTests.stop_validates_generation_before_signaling` | CountingControl.Calls == 0; tool retained. |
| PC-203 | Trust done phase/dead progress PID over unknown launch custody. | `HostCleanupCheckpointCompatibilityTests.wait_uses_custody_not_progress_pid` | StillRunning and tool/report retained. |
| PC-204 | In CreateRun add a dead-sibling branch that unlinks its sentinel and removes its tool through the injected primitive I/O, without creation custody. | `HostCleanupCheckpointCompatibilityTests.next_start_preserves_all_pre_existing_sibling_tools` | Fresh run exists; dead, unknown and unrelated pre-existing tool bytes remain; sibling delete-entry attempts == 0. |
| PC-205 | Set phase done after stop request returns false. | `HostCleanupCheckpointCompatibilityTests.stop_waits_for_confirmed_exit` | StillRunning, phase running and tool present. |
| PC-206 | In OutputCleanup.RemoveOlderRuns bypass the missing-ledger return for the dead old run and perform primitive unlink/nonrecursive rmdir of that fixture run. | `HostCleanupCheckpointCompatibilityTests.age_clean_cannot_bypass_live_custody` | Deleted is empty; Refusals contains output-ledger-missing; dead/live/reused/foreign-identity directories and bytes remain. |
| PC-207 | Remove tool directly from EvidenceFolder.Write. | `HostCleanupCheckpointCompatibilityTests.evidence_write_has_no_raw_delete_bypass` | Tool directory still present without custody. |
| PC-208 | Call injected process control from sweep on live owner. | `HostCleanupCheckpointCompatibilityTests.sweep_never_signals_a_process` | Control calls == 0; live tool remains. |
| PC-209 | Return Removed after delete IOException. | `HostCleanupCheckpointCompatibilityTests.failed_delete_receipt_is_truthful_and_retryable` | failed-delete-truthful outcome == Failed then retry == Removed. |
| PC-210 | Remove run directory instead of tool on completed wait. | `HostCleanupCheckpointCompatibilityTests.completed_wait_removes_only_tool` | All four rows preserve report hash, return exact exitCode and remove only tool. |
| PC-211 | Return Removed for missing tool. | `HostCleanupCheckpointCompatibilityTests.already_absent_is_idempotent` | Second outcome == AlreadyAbsent. |
| PC-342 | Always require generic memory authority even for valid tool seal. | `HostCleanupCheckpointAuthorityTests.Fresh_context_removes_only_sealed_tool` (`cross-context`) | both Arguments exit 0/1 return expected exit and tool absent; evidence unchanged. |
| PC-343 | Import authority entries into fresh context registry. | `HostCleanupCheckpointAuthorityTests.Tool_authority_does_not_seed_generic_registry` (`authority-import`) | generic output remains memory-authority-missing after successful tool load. |
| PC-344 | Skip seal.Sha256 comparison only. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`digest`) | malformed/replaced authority with otherwise-valid record is Refused before delete. |
| PC-345 | Omit RequireUniqueProperties. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`unique-properties`) | correctly rehashed duplicate-key authority yields tool-authority-unreadable. |
| PC-346 | Skip record.Version comparison. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`version`) | correctly rehashed v2 gives tool-authority-run-mismatch. |
| PC-347 | Skip record.RunId comparison. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`run-id`) | correctly rehashed foreign RunId gives run-mismatch. |
| PC-348 | Skip record.RunDirectory comparison. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`run-path`) | correctly rehashed foreign run directory gives run-mismatch. |
| PC-349 | Skip record.ToolDirectory comparison. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`tool-path`) | foreign tool directory gives run-mismatch before any foreign observation. |
| PC-350 | Skip record.Creator comparison. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`creator`) | changed creator only gives run-mismatch. |
| PC-351 | Skip SafeRelative admission. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`relative-path`) | escape variant with recomputed digest gives tool-authority-path-invalid before entry traversal. |
| PC-352 | Remove Distinct-count check and use last-wins dictionary construction. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`duplicate-entry`) | duplicate-path authority returns path-invalid before Check. |
| PC-353 | Remove entry-count admission check. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`entry-count`) | empty and 10001-entry records give path-invalid before inventory. |
| PC-354 | Skip SameDirectory device equality. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`directory-device`) | replacement device with same inode yields root-mismatch. |
| PC-355 | Skip SameDirectory inode equality. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`directory-inode`) | replacement inode with same device yields root-mismatch. |
| PC-356 | Skip fallback creation-time comparison. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`directory-created`) | no-inode replaced directory yields root-mismatch. |
| PC-357 | Ignore actual.Count mismatch. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`roster-count`) | missing sealed file gives inventory mismatch with all remaining entries valid. |
| PC-358 | Skip unlisted-entry rejection. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`roster-membership`) | same-count inventory replacing a sealed file with a foreign file gives entry mismatch before mutation. |
| PC-359 | Compare length but omit hash in Matches. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`content`) | same-size changed bytes with frozen stat give entry-mismatch. |
| PC-360 | Omit entry.Stamp.Matches(before). | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`stat-before`) | wrong stamp is refused even when bytes match seal. |
| PC-361 | Omit before.Matches(after). | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`stat-after`) | post-read substitution gives entry-mismatch. |
| PC-362 | Omit Remove root equality check. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`remove-target`) | mismatched Remove target returns root-mismatch before target unlink. |
| PC-363 | Skip Matches inside Remove file loop. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`final-entry`) | entry swapped after whole-tree Check survives; unlink-entry zero. |
| PC-364 | Skip SameDirectory inside directory loop. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`final-subdirectory`) | replaced nested directory survives; directory-removal-entry zero. |
| PC-365 | Skip last SameDirectory before root rmdir. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`final-root`) | replacement root is retained even after sealed payloads removed. |
| PC-366 | Make final tool rmdir recursive. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`late-addition`) | late-between-check-and-rmdir bytes remain; recursive-intent zero. |
| PC-367 | Skip CreationRecord/MatchesCreation gate while sealing. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`seal-creation`) | unrecorded entry raises tool-authority-creation-mismatch; no authority file written. |
| PC-368 | Skip stamp equality after MatchesCreation at Seal. | `HostCleanupCheckpointAuthorityTests.Fresh_context_retains_unproven_tool` (`seal-stat`) | changed-during-seal raises creation-mismatch; no capability published. |
| PC-369 | Move cleanup.Remove ahead of RequiredReceiptRefusal. | `HostCleanupCheckpointAuthorityTests.Invalid_terminal_receipt_prevents_cleanup` (`required-before-cleanup`) | invalid-terminal tuple has cleanup-entry attempts zero and exit 2. |
| PC-370 | Omit expectedSourceSha comparison. | `HostCleanupCheckpointAuthorityTests.Invalid_terminal_receipt_prevents_cleanup` (`expected-sha`) | source-SHA-mismatching, exit 2, cleanup-entry attempts zero. |
| PC-371 | Return 0 on dead executor without done state. | `HostCleanupCheckpointAuthorityTests.Invalid_terminal_receipt_prevents_cleanup` (`dead-undone`) | exit 6 and executor died without phase=done text. |
| PC-372 | Coerce Unknown to Dead at wait decision. | `HostCleanupCheckpointAuthorityTests.Invalid_terminal_receipt_prevents_cleanup` (`unknown-wait`) | exit 75 on fake deadline and no cleanup-entry attempts. |
| PC-373 | Omit gate.CanWrite check. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`gate-writable`) | root-marker-transition-unproven; marker bytes unchanged. |
| PC-374 | Omit gate.Name path equality. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`gate-name`) | wrong-root held gate is refused and both markers unchanged. |
| PC-375 | Omit current.State allowed-values check. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`current-state`) | sealed marker rejected without replacement. |
| PC-376 | Omit expected.State check. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`expected-state`) | request to active/foreign state rejected without overwrite. |
| PC-377 | Omit current.RootId comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`root-id`) | mismatched expected RootId refused. |
| PC-378 | Omit current.AttemptId comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`attempt`) | mismatched expected attempt refused. |
| PC-379 | Omit current.AssemblyInvocationId comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`assembly`) | mismatched assembly invocation refused. |
| PC-380 | Omit current.CreatedAt comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`created`) | mismatched creation timestamp refused. |
| PC-381 | Omit current.Owner comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`owner`) | mismatched expected owner refused even when current owner is dead. |
| PC-382 | Omit current/expected RootPath comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`root-path`) | foreign expected path refused. |
| PC-383 | Omit probe.Observe dead comparison. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`fresh-death`) | owner changes to AliveSame/Unknown/Reused before transition; refused and unchanged. |
| PC-384 | Omit serialization equality against current. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`first-bytes`) | changed marker after Read yields root-marker-changed. |
| PC-385 | Omit final SequenceEqual(previous). | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`final-bytes`) | changed-before-move gives root-marker-changed; new owner bytes preserved. |
| PC-386 | Allow any existing control file when caller holds any gate. | `HostCleanupCheckpointAuthorityTests.Marker_transition_requires_gate_identity_and_dead_owner` (`foreign-file`) | ordinary foreign control Text still unrecorded-existing; bytes unchanged. |
| PC-387 | Drop supplied context at CreateRun. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`create`) | create caller witness present; native fallback count zero. |
| PC-388 | Drop context when constructing shadow-copy path. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`start`) | start witness covers copy and authority seal; native fallback count zero. |
| PC-389 | Resolve default context inside ExecutorOwnershipStore.Write. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`ack`) | ack writer witness present; native fallback count zero. |
| PC-390 | Drop context at WaitCommand cleanup construction. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`wait`) | wait reader/cleanup witnesses present; native fallback count zero. |
| PC-391 | Drop Runtime context at run dispatch. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`run`) | run front-door witness present with recorded launch only; native fallback count zero. |
| PC-392 | Use default state store only on stop. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`stop`) | stop state witness present; native fallback count zero. |
| PC-393 | Drop context in OutputCleanup invocation. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`clean`) | clean policy-entry witness present before expected refusal; native fallback count zero. |
| PC-394 | Resolve default cleanup from EvidenceFolder. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`evidence`) | evidence wrapper cleanup-entry witness present; native fallback count zero. |
| PC-395 | Use default creation registry in CleanOwnedOutputs. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`output`) | output deletion uses virtual registry and removes positive root; native fallback count zero. |
| PC-396 | Replace context delay with Thread.Sleep. | `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context` (`delays`) | retry records scheduled delay before controlled advance; completion and attempt order match virtual timer trace. |

Additional discovery, without inventing frozen PC IDs: caller-cancellation in
Invalid_terminal_receipt_prevents_cleanup checks the actual active read token
before joining completion; current-image retention checks zero unlink attempts.
Inspect status/report/validate, usage-event writes and source/branch observations
for missing caller controls. The `directory-created` variant includes a valid
portable fallback identity before the one-tick substitution. Missing/malformed,
forged-ledger, links/hardlinks and JSON variants remain pending alongside every
listed row. No deliberate mutant ran during Code.
