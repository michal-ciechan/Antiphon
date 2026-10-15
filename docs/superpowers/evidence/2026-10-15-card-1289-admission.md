# CARD-1289 admission Code evidence

Implemented and pushed; Final verification is incomplete. Latest tested code:
`3a2cb10fc293799a50e84876173aa99b9f0a9099`. Original Code/landing owner:
`5a9cd727-c32f-4f59-96ac-40b5a91c70b2`, branch `feat/card-task-5a9cd727`,
worktree `/work/worktrees/task-5a9cd727`.

The all-write card-token fence now compares admission coordinates. Ordinary Git
preparation precedes the card transaction; a read-only capacity preview retains
refusal before Git, and the locked capacity check still governs insertion.
Closed-card role scope, atomic handoff linkage and lock order remain unchanged.

Latest completed rows: CP-1 11/11 and CP-3 160/160. Before the fix, four unrelated
edits were refused and the ordinary Git path held the row lock. The final gated
sample performed seven Git calls without that lock; its measured lock span was
33.9 ms. Handoff retains the lock (619.0 ms measured) and linked-child retries
bypass Git. These are single instrumented samples, not throughput benchmarks.

CP-2 observed 35/35 and CP-4 observed 2938 passed/27 skipped on the earlier code
in a run stopped to repair concurrency refusal priority. They lack a terminal
run receipt and need Final qualification. CP-5/CP-6 hit the repair run's total
cap without final TRX; CP-7 did not run. Deferred: R-2/R-4, CP-2 and CP-4..CP-7.
No Final/Full or Review-ready claim is made. Next: Code to complete exact missing
qualification, then ordinary Review. Restart: server after landing.

The full report, unedited CHECKPOINT lines, source/provenance, historical costs,
class/method roster, all V/R outcomes and pending PC-1..PC-6 variants are in
[the task report](../../../.antiphon/task-5a9cd727.md).
The [plan](../plans/2026-10-15-card-1289-admission.md) holds the closed selection;
[Mutation candidate](../mutations/2026-10-15-card-1289-admission-candidate.md) remains
unbound. All controls and missing-control discovery belong to SourceLanding
Mutation after Review and landing of the original Code owner.
