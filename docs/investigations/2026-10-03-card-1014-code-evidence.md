# CARD-1014 Code evidence

Original Code/landing owner: a8a78ab3-b4a9-4bbb-8629-f897bb7418d4.
Branch feat/card-task-a8a78ab3, worktree /work/worktrees/task-a8a78ab3.
Plan: ../superpowers/plans/2026-10-03-card-1014-spill-incident.md.

S1 c23090dc8f93a6f2624259a56f30f616f75cbc08: unchanged production, three fresh
TRX results fail exactly ordinary-spill-incident-count=1:{send-now,flush,enqueue-now}.
Each failure reports actual 0, expected 1. Earlier queue-state assertions pass.

CHECKPOINT CP-1 commit=c23090dc8f93a6f2624259a56f30f616f75cbc08 build=ok filter=/*/*/AgentTaskInputFallbackTests/Ordinary_spill_errors_keep_their_existing_delivery_behavior executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-a8a78ab3/.antiphon/checkpoints/20261003-134906-4dc3/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=c23090dc8f93a6f2624259a56f30f616f75cbc08 sourceState=clean buildSource=verified

CP-1 machine evidence: .antiphon/checkpoints/20261003-134906-4dc3/report.json.
Only this note and the task report are tracked; TRX, checkpoint folders and logs
remain ignored. Setup exception: checkpoint tool bootstrap build via build-slot,
OutputPath=bin-c1014-tool/, UseAppHost=false, slot=granted waited=0s, 0 errors,
one inherited CS8602 warning. No other unlisted build/test drivers.

S2 records the legacy fixed RunnerSpillWriteException message through the same
RecordTransportFailureAsync recorder, only after ordinary fallback refusal and
queue reversion in all three handlers. Successful task-input fallback stays separate.
Final S2 qualification runs CP-2 through CP-6 once at committed HEAD, with exact
source SHA and build reuse. Its authoritative generated report/receipts are under
.antiphon/checkpoints (latest matching S2 commit); final caller report supplies the
run ID and counts. No source edits are permitted while that run is in flight.

All PC-1 path variants and PC-2/kind remain pending for SourceLanding Mutation;
CARD-0888/0965 parent PC-1..PC-36 remain pending in their existing ownership.
Activation target: server (canonical AppHost restart after Review and landing).
No restart, landing or deployment occurs in this Code task.
