# CARD-1529 unbound SourceLanding Mutation candidate

Landing owner: `684b15a2-e609-4a55-ba8d-5735a44fc15b`. This candidate is unbound;
commission only after ordinary Review and confirmed publication. All PCs are
pending. No mutant was applied during Code. Bind to the landing SHA and keep
execution evidence outside the SourceLanding snapshot.

Plan: `docs/superpowers/plans/2026-10-23-card-1529-refocus-brief.md`.
Each cycle uses only the exact named method under
`/*/*/RefocusBriefIdentityTests/<method>`; arguments are variants within that method.
The Mutation owner must establish assertion-red, restore, then same-selection
green, and discover any missing controls independently.

| PC | Defect | Exact method / variants |
|---|---|---|
| PC-1 | Producer omits durable purpose | C1529_Producer_keeps_compact_and_admits_exactly_one_brief / pending, completed, post-persist-fault |
| PC-2 | Admission retains identified refocus as uncertain brief evidence | C1529_Producer_keeps_compact_and_admits_exactly_one_brief / all |
| PC-3 | Starting recovery treats compact as existing brief | C1529_Recovery_backfills_after_a_persisted_refocus / starting |
| PC-4 | Running recovery treats compact as brief | C1529_Recovery_backfills_after_a_persisted_refocus / pending, sent |
| PC-5 | Exclusion accepts foreign ownership/task purpose | C1529_Only_positive_current_refocus_identity_excludes_evidence / other-task, foreign-owner |
| PC-6 | Exclusion accepts another attempt/generation | C1529_Only_positive_current_refocus_identity_excludes_evidence / other-attempt, other-generation |
| PC-7 | Exclusion accepts ambiguous/untyped purpose | C1529_Only_positive_current_refocus_identity_excludes_evidence / missing-key, empty-key, unknown-key, wrong-provider, wrong-body, marker-in-goal, retained-payload |
| PC-8 | Existing brief permits second insertion | C1529_Real_brief_blocks_a_second_brief_without_a_receipt |
| PC-9 | Compact receipt substitutes for complete brief receipt | C1529_Real_brief_blocks_a_second_brief_without_a_receipt / compactReceived=true |

Do not widen timeouts, loosen assertions, replay legacy blocked tasks, or claim
that Sent/CompactBoundary/QueuedUserPrompt proves delivery. Restart: server only;
runner none. Code has no Windows qualification authority.
