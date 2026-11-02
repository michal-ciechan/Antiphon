# CARD-1638 review b922ebf8 D-1 fix (task e7d4f999)

Branch feat/card-task-e7d4f999 from 4a0abd68507a5e3105bf7b258849b1abb033890a (feat/card-task-8faaee74).
Fix commit 1a819f55acdcbbaa65df76c4e680428660347a92 (test-only, 1 file, +5/-4).

## Change

tests/Antiphon.Tests/Scripts/RcFailedFirstExecutionTests.cs, JournalCustodySurvivesInterleaving:
- harness ExecutionBudget 326 s -> 481 s (ceil(2 x 240.257 s)); method `[Timeout]` 386_000 -> 541_000 (60 s cleanup margin kept).
- comment cites checkpoint run 20261102-133640-5f80 (server2, load 32-38 on 24 cores), notes the card's 163 s has no receipt, Provisional n=2 (163 s, 240.3 s). Bound stays finite. Nothing else changed.

## Checkpoints (run 20261102-140522-e2ce, ad hoc ignored plan .antiphon/card-1638-d1/plan.md; no plan table exists for this card part)

CHECKPOINT CP-1 commit=1a819f55acdcbbaa65df76c4e680428660347a92 build=ok filter=/*/*/TimeoutAuditTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-e7d4f999/.antiphon/checkpoints/20261102-140522-e2ce/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=1a819f55acdcbbaa65df76c4e680428660347a92 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=1a819f55acdcbbaa65df76c4e680428660347a92 build=ok filter=/*/*/RcFailedFirstExecutionTests/JournalCustodySurvivesInterleaving executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e7d4f999/.antiphon/checkpoints/20261102-140522-e2ce/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=1a819f55acdcbbaa65df76c4e680428660347a92 sourceState=clean buildSource=verified
verdict: GREEN exit=0; unlisted: none; validate: CHECKPOINT SOURCE VALID source=1a819f55acdcbbaa65df76c4e680428660347a92 rows=2.
CP-2 TRX duration 00:04:02.399 (242.4 s) at load average ~34-36 on 24 cores: third sample, 1.98x headroom under 481 s.
A budget change has no natural red witness; the measured samples are the basis.

## Gates

- Land graph -Check: digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69, approved, exit 0 (no -Update). Run twice: the first output was cut by tail before the digest line.
- check-evidence-diff e18a1177d..1a819f55: commits=3 entries=1 violations=0, exit 0.
- merge-tree whole branch: clean onto origin/master 43ae7371d and origin/rc/2026-11-01 10a826b9d. The fix commit alone (--merge-base HEAD~1) conflicts on both, as expected: it edits lines from 5ce77b7ae, which neither target has yet. It must travel with CARD-1638 part 1.

## Builds outside the closed list

- Checkpoint tool (tools/Antiphon.Checkpoints -> bin-c1638tool/) via build-slot (waited 0 s, held 48 s), needed to run the plan.
- LandGraphAudit evaluator Release -> bin-land-audit/ via build-slot (waited 0 s, held 3 s), required by -Check.
All four bin-* directories were removed with rm -r on literal paths.

## Pending

No PC: budget value only. SourceLanding Mutation has nothing method-scoped to mutate beyond the value.
