# CARD-0987 S1b: unbound SourceLanding Mutation candidate

Original Code/landing owner: 0f66f6e0-e581-4d18-998a-faee2104c9e7.
Plan and frozen ledger: docs/superpowers/plans/2026-10-15-card-0987-test-design.md.
Ordinary evidence: docs/superpowers/evidence/2026-10-16-card-0987-s1b-code.md.
This is an unbound candidate, not a PC execution report. Bind it to the actual
SourceLanding SHA after ordinary Review and publication. Restart: none.

Both controls and every variant remain pending. Mutation owns deliberate
compiling defects, exact-method red/restore/fresh-green cycles and missing-control
discovery. All PC-1..413 remain pending overall; the S1a candidate retains its
previous 66 bindings. No prior scope is discharged by this slice's ordinary green.

| ID / variant | Exact selection | Defect site | Required assertion |
|---|---|---|---|
| PC-201 / nested live executor | `/*/*/HostCleanupCheckpointCompatibilityTests/nested_live_executor_vetoes_whole_root` | `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs`, `Eligible`: bypass the nested non-Dead executor veto after the real journal/identity reads | Candidate examined once, `nested-identity-alive`, CompletedRoots 0 and unchanged root sentinel; root gate reached and no unlink/rmdir. |
| PC-202 / missing nested custody | `/*/*/HostCleanupCheckpointCompatibilityTests/unknown_nested_custody_vetoes_whole_root` | Same `Eligible`: accept a tool-bearing directory without ownership (continue past the missing-custody guard, rather than merely falling into the invalid-journal guard) | Candidate examined once, `nested-custody-missing`, CompletedRoots 0 and unchanged nested tool sentinel; root gate reached and no unlink/rmdir. |

Each method has a positive pair over the same candidate: real serialized dead
custody, re-registration, then CompletedRoots 1, absent root and no recursive
removal intent. Virtual facts drive real sweep/marker/run/executor policy.
The Code red-first run proves the missing instance-context path (Examined 0);
it does not substitute for either guard mutation above.

Discovery also examines constructor/default-context continuity, post-lock
eligibility and MarkDeleting context, injected monotonic deadline and UTC age,
index migration/state/temp operations, file truncation and handle disposal. The
remaining 27 sweep and 11 scope compatibility results are S2 obligations; they
were not implemented or claimed by S1b. The frozen 16/54/80 floors are unchanged.
