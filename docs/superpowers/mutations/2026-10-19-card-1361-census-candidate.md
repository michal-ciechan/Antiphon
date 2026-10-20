# CARD-1361 / CARD-1371 unbound Mutation candidate

Code owner: `38f082ad-2a5a-4370-972e-9773ecdc6262`.
Plan: [census status and count drift](../plans/2026-10-19-card-1361-census-status-flip-tolerance-plan.md).

Status: unbound, higher-tier-required. Ordinary Review and confirmed source landing
must precede SourceLanding Mutation. No deliberate mutation was executed by Code.

Every plan control **PC-1/a through PC-81/a is pending**, with no waived variants.
The plan's exact method filters and guard inventory remain the candidate roster.
Bind payload/preimage/mutant hashes, unique source context and exact assertion
sites against the confirmed landed source; do not treat this document as a bound pack.

The caller's CARD-1371 override changes the old one-read/no-retry expectation:
the complete census now retries cross-read count Drift up to three attempts,
sleeping 30 seconds between attempts. Malformed responses refuse immediately.
PC-77/a and PC-78/a still guard the separate bound-land wait: exhausted Drift
must receive no additional retry there. The C1371 witness calls both real wait
wrappers and checks three root reads and two sleeps on persistent drift.

Mutation owns missing-control discovery for the new retry policy as well,
including disabling recovery, exceeding the attempt bound, and removing the
delay. These additions are unbound discovery obligations, not executed controls.

Real-boundary seams now include PowerShell `Test-RecycleOpenStatusMove` (both
call sites), `Sync-RecycleOpenStatuses`, the ordinary pending-detail validator,
the total-count validator and bound-only land projection. Host probes extract
both literal `open_status_move` definitions, the actual reconciliation program,
ordinary pending-detail guard, count predicate, land projection and appearance
program. Public census paths remain alongside these injected-boundary probes.

All ordinary and mutation evidence is offline. Restart: none. Master publication
does not activate this change in the running rollout; RC adoption is separate.
