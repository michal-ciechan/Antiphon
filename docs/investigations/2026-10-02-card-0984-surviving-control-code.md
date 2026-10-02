# CARD-0984 surviving-control repair

Code task and new landing owner: 12869ac9. Branch: feat/card-task-12869ac9.
Runner worktree: /work/worktrees/task-12869ac9.
Desktop counterpart: C:\Antiphon\worktrees\card-task-12869ac9 (not accessed).
Base: 6e62ca291ce050ebd4f6a7d1734a4b0e2c135e54; retains all four earlier Code commits.
Plan: docs/superpowers/plans/2026-10-02-card-0984-fakegrok-lf-oracle-plan.md, S4 / CP-5.
Final actual receipt/report: .antiphon/task-12869ac9.md, populated after the committed-head run.

Only the pure test fixture and expected string change in source: both gain explicit U+0009 immediately before the final CRLF/FAKE response. Exact equality now requires that control to survive. The U+0008 position remains before the expected space after TAIL last, retaining the earlier guard and its label. The helper, Windows Unbracketed test, native-log assertions and UnixPtyArgvTests are unchanged. No timeout or assertion is loosened.

V-1, V-2, V-3, V-4 and R-1 are inherited S0-S3 verification; prior details are in docs/investigations/2026-10-02-card-0984-fakegrok-lf-oracle-code.md and the original task report. V-5 Windows inbox-host confirmation passed 19/19 at 6e62ca29 according to the repair brief; no final-SHA Windows result is claimed. CP-5 will qualify V-6 and ordinary R-2 at this repair's committed SHA; results remain pending until its fresh TRX is inspected.

The explicit narrow Final brief requires the full FakeGrokContractTests class only. No whole Unit lane or whole-project run: no shared helper changed, no unbounded class impact. Expected Linux roster is exactly 19 total, 1 passed, 18 named Windows-only skips, 0 failed. Estimated ordinary cost: two minutes. All builds/tests take the host build-slot gate. The checkpoint-tool bootstrap is an unlisted gated build necessary to obey the standing Code-stage checkpoint-tool requirement; the environment has a task token, so owner verification is available despite the brief's concern.

PC-1 and variants MUT-A/MUT-B remain pending for post-land SourceLanding Mutation, which is paused. The Code-stage instruction assigns all deliberate mutants, red/restore/green and missing-control discovery to Mutation; no scratch mutant runs are claimed. Review 10955a8f showed the old fixture failed to distinguish removal of every control character. The updated expected literal distinguishes MUT-B, while MUT-A still differs first at U+0008 versus space. Actual mutant assertion output must be collected by Mutation; these observations are source reasoning only.

Read GET /api/runner-defaults and GET /api/session-runners; no host/platform pin or fleet address was embedded. Restart: none. No land or deploy. Next: ordinary Review of new landing owner 12869ac9, then caller-owned landing and paused Mutation follow-up. Optional final-SHA Windows inbox-host reconfirmation is a separate Debug task.
