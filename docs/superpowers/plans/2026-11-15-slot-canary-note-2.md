# CARD-0824 slot canary 2: release and reuse
Lease: server and runner enablement plus the Plan role gate admit a reusable ws-NN slot; the ledger binds task, preparation, branch, exact base and generations.
Observed before writing: ws-01 is Leased, leaseGeneration=2, directoryGeneration=1, taskId=dc726c22-77ff-4bdb-af6e-bfb4cca12ced, idleSince=2026-11-15T21:27:19.7514532+00:00.
Reuse: lastTaskId=870a2044-4aa6-4611-9723-2bb745fd7755; retained directory name: 870a20444aa6461197232bb745fd7755; this proves directory reuse, not warm build-output reuse.
Release: wait for session stop, confirmed sync and no live follow-up; reconcile to Free/detached at the published SHA, removing local task refs and residue while preserving origin and retained evidence; the one-minute poll is no deadline.
Rollback: set server roles to scalar none, confirm the next WorkspaceSlotDecision reason=roles_none, keep runner support until outstanding leases release, then disable its saved flag and recreate through named phases; Docker server role changes require restart, eligible desktop JSON changes can reload.
## Performance budget
Documentation only: no builds/tests, setup, test phases, measured p95 or changed timeouts; no affected existing test classes (CARD-1782); no session-wait behavior changes.
