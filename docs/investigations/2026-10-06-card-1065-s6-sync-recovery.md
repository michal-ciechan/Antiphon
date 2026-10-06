# CARD-1065 S6: durable desktop sync recovery

S6 is ready for Review. Remote publication now persists exact-source desktop sync
debt; a bounded dispatcher sweep recovers it independently of seat release with
persisted 1/2/4/5-minute backoff. Recovery uses the captured desktop endpoint and
immutable parked SHA, refuses unsafe source, and never promotes the blocked task,
creates approval, replays settlement or launches a provider. Parking remains off by
default, and D-3 publication and Working-session protections remain.

Original Code task and landing owner: `24c08059-dd45-46fa-a854-69b9e6c254f6`.
Branch: `feat/card-task-24c08059`.
Worktree: `/work/worktrees/task-24c08059`.
Base: `fb37c36c10f38da4b04de72496ce9c080e993c85`.
Actual final tested source: `59ce5532dba07b023850d46c77f916ffa872dd29`.
The report-only closing commit is separate from that tested source.

The [landed plan](../superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md)
assigns S6 CP-6 / V-15 / V-16. Final S6 verification passed **308/308**, zero failed or
skipped: CP-6 2; full release classes 67; full sync/publication classes 55; full task
detail/attention/dispatcher lifetime classes 181; registry guards 3. V-9-V-16,
V-30/V-31 and all three R-1 methods actually ran and passed. Every final receipt
reports clean source, verified build provenance, slot=granted and waited=0s.

The initial tests failed on missing durable debt and exact source. Two repair rounds
fixed fixture dependencies and an inherited default-off local ownership refusal.
The inherited orphan test failed unchanged at the exact task base before its safe
Owned veto was restored. No assertion, timeout or retry was weakened. The explicit
CP-6-only/no-whole-Unit brief governs this slice; other CARD V/R rows, S7/S8
continuation and S11 integrated/manual acceptance are not claimed passed.

[Full evidence and unedited receipts](../../.antiphon/task-24c08059.md) contain all
commits, test counts, red/base results, provenance, exact rerun commands, scope and
cleanup. Generated TRX/JSON/logs remain ignored; isolated outputs and the temporary
base worktree were removed. Evidence guard over base..tested-source passed with zero
violations; the final message reports the complete range through the closing commit.

PC-118 through PC-128 and all variants remain pending method-scoped SourceLanding
Mutation. Added PC-S6R-1 removes the default-off known-local-session Owned veto and
runs `/*/*/RunnerSeatOrphanSweepTests/Unknown_server_session_with_idle_runner_is_released`.
All other plan controls and PC-S5R-1 through PC-S5R-7 remain pending. Mutation owns
deliberate mutants, red/restore/green and missing-control discovery.

Next is Review; caller then lands this original Code task and commissions Mutation.
Restart: **server**, owned by caller/orchestrator after Review/landing and activation
checks. No restart or deployment was performed, and no runner restart is required.
