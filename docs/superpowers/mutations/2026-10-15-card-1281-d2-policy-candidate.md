# CARD-1281 S2-policy unbound Mutation candidate

Carry e7cfb812 renumbers the F-1 repair's six-guard row to **CP-24**; its closed ordinary selection is **CP-13/CP-24**. Shared-binding CP-14..CP-23 are unchanged. Historical acceptance evidence below remains bound to its original SHA; current carry evidence is `.antiphon/task-e7cfb812.md`. This renumbering discharges no PC or variant.

Code owner: `1904d993-dfb4-4f0d-96f5-31f1c192fa27`. Implementation tested at `c53d28c17a7b90944988ea6cf27a06db74e9df20`; CP-13 acceptance passed all ten methods. The caller's subsequent refinement limits this handoff to CP-13 and explicitly defers CP-12/R-1 whole Unit; that coverage remains incomplete, not passed. This candidate grants no approval or execution credit. Ordinary Review must complete before the caller lands the original Code task and separately commissions SourceLanding Mutation.

The authoritative 64-PC inventory remains in [the plan](../plans/2026-10-15-card-1281-pipeline-speed-plan.md). All PC-1..PC-64 and all variants are pending. Proposed method filters below are exactly `/*/*/AdvisoryChangePolicyTests/<method>`; no mutant was applied.

| PC | Policy location / required binding | Detecting method | Status |
|---|---|---|---|
| PC-1 | `AdvisoryChangePolicy.Decide`, advisory-only action; shipped Review/Code instruction independently | C1281_AdvisoryDoesNotRerun | Pending; bind compiling payload and instruction variant separately |
| PC-8 | `CanUseSeparateDocs`: inspected content kind, protected path and original path, regular-file modes, unsupported markup | C1281_NarrativeBoundary | Pending; split independently bypassable checks before cycles |
| PC-9 | Comment-kind refusal and shipped comment-proof policy. Positive equivalence is intentionally unsupported. | C1281_CommentEquivalence | Pending |
| PC-10 | `CanPrepareSeparateDocs` distinct helper SHA/identity; existing real land approval rejects borrowed Review | C1281_SeparateDocsIdentity | Pending; bind each independent identity check |
| PC-11 | Separate owner and helper latch checks; existing land consumer's helper latch | C1281_SeparateDocsIdentity | Pending for owner, helper and consumer variants |
| PC-12 | Historical data policy is pure and has no persistence writer. The method rereads seeded history and the composed instruction. | C1281_HistoryPreserved | Pending; Mutation must determine the meaningful history-write control; a phrase change alone cannot prove persistence |
| PC-52 | Frozen-tip and publication-first proposal checks plus private Git observation and shipped ordering policy | C1281_SeparateDocsIdentity | Pending; frozen-tip oracle is `reviewed-tip-must-stay-frozen`, publication oracle is `publication-first`, Git observation remains `reviewed-tip-unchanged` |

PC-2..PC-7 remain deferred to B-1 and its foundation/verification binding. PC-13..PC-51 and PC-53..PC-64 remain pending in other slices. Missing controls must be discovered and bound, not waived: original-path rename, before/after file modes, content syntax, original publication identity, Final/Full/source-clean shape and helper/source coordinates introduce distinct refusal branches. The caller policy consumes inspected content and confirmed publication coordinates; it neither authenticates those inputs nor grants backend landing authority. The existing landing boundary remains the approval consumer.

SourceLanding must rebind to the actual landed source and external evidence root, preserve per-PC red/restore/green receipts and use the exact method filter. This document is not a bound mutation pack and does not claim a coverage census or substitute for missing-control discovery.
