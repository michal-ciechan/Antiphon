# CARD-1370 unbound Mutation candidate

Code/landing owner after Review c3204e73 repair: 520be308-50bf-4397-816e-d4912fe112f0.
Earlier implementation owner: b5da8f79-a38e-4e68-b68d-e55084728d70.
Plan: ../plans/2026-10-20-card-1370-state-json-write-access-denied-plan.md.

No PC IDs were supplied by Plan. All deliberate mutants, missing-control discovery,
and red/restore/green cycles remain pending for caller-commissioned SourceLanding
Mutation after ordinary Review and publication of the original Code task.
This file is not bound to a landing SHA and records no passed positive controls.

The ordinary witness is method-scoped:
`/*/*/CheckpointStatePublicationTests/state_publication_survives_contention_and_preserves_completed_rows*`.
Candidate boundaries for discovery are retry removal/budget/classification,
history duplication, post-move stamp failure, scheduler result loss during draining,
final publication failure, premature cleanup, failed-report validation and wait
output, and failed corrective publication retaining a stale green terminal receipt.
Native Windows handle contention remains a separate ordinary qualification
obligation; Linux injected I/O does not discharge it. Mutation must name its exact
method-scoped filters and variants when commissioning controls.
