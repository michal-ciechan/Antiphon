# Windows checkpoint debugging — b20bf5ac

Base: f7c77683ffb940291f26eb6dee63f92efed25bff. Native Windows repair of the persistent CheckpointSlotExecutorTests.executor_reachable_broker_grants_build_and_rows state publication refusal and investigation of the two batch-only checkpoint failures. Historical red evidence is retained read-only at C:\Antiphon\releases\task-7783f346 and in origin/feat/card-task-7783f346:.antiphon/task-7783f346.md.

Assigned verification: native isolated reproduction up to ten attempts; the two historical batch-only methods individually and the original 95-case checkpoint selection together; targeted state publication regressions for any fix. No full Unit run: this Custom Debug brief specifies narrow native verification. Every driver uses scripts/build-slot.ps1. Evidence: C:\Antiphon\releases\task-b20bf5ac. Alternate output: bin-b20bf5ac/.

Historical results: slot method 0/1 in both fixed-checkpoints and focused-native-checks. Batch-only failures: CheckpointExecutorLogTests.note_returns_when_the_sink_resumes_on_the_caller_context and CheckpointAdmissionContractTests.C1531_inputs_resolve_without_disabling_bounds; both passed in the focused run. These are unresolved observations, not attributed regressions.
