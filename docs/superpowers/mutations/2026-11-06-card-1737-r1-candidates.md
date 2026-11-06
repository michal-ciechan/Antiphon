# CARD-1737 R1 unbound mutation candidates

Original Code/landing owner: `0cc54c85` on `feat/card-task-0cc54c85`.
Test implementation: `94ad9650a60922a652f7054c0bd5de660b7c95b3`.
The [frozen R1 TestDesign](../testdesign/2026-11-06-card-1737-r1-testdesign.md)
owns exact selector snippets, hashes, method filters and boundary wiring.
These candidates are unbound: no author pack or historical waiver binds a landed
source. Mutation owns landed-source binding, deliberate mutants, red/restore/green
and missing-control discovery after ordinary Review and caller publication.

| PC / variant | Production guard and sole defect | Exact method under `/*/*/<class>/<method>` | Decisive preservation assertion | Formal status |
|---|---|---|---|---|
| PC-1 / matching-content | `GuardedWorktreeRemoval.CustodyOf`: omit only directory-identity equality rejection; keep RestoreCustody and all manifest/hash guards | `LandingRemovalPolicyControlTests.C1737_MatchingContentReplacementIsNotDeleted` | `preserved:W-U9:replacement-directory` | pending SourceLanding Mutation |
| PC-2 / late-child | `GuardedWorktreeRemoval.CompleteBranchAsync`: omit only expected-old SHA from delete; retain --no-deref and live/current checks | `WorktreeGuardedCleanupTests.C1737_AdvancedBranchSurvivesDeleteCas` | `preserved:W-M7:advanced-ref-present` | pending SourceLanding Mutation |
| PC-3 / changed-length | `WorktreeNoFollowDelete.UnlinkSameNode`: omit only hash-ending-vs-fresh SameContent refusal; retain SameNode | `WorktreeNoFollowDeleteTests.C1737_PostHashWriteSurvivesFinalStat` | `preserved:W-A23:post-hash-file` | pending SourceLanding Mutation |

Every variant has one TUnit result. Use the precise method filter, never a class
or ordinary OR group. A fixture/build/timeout/reason-only failure is not a kill.
The commissioned author pack demonstrated baseline green and intended preservation
red for all three; it is additional author evidence only. Both temporary production
files were restored by original SHA-256 before ordinary qualification. The ignored
report `.antiphon/task-0cc54c85.md` retains exact author source, binary and TRX bindings.

Windows N-ID/N-RESTORE/N-ENTRY/N-LIST, real alias and long-path obligations remain
separately commissioned. U8/O-1 semantics and U10/O-2 remain the Plan's operator
choices. No S4a enablement, global safety or atomic stat-to-unlink claim is made.
Restart: none (tests and classification only).
