# CARD-1011 faithful replay onto master

Replay task: `ffc7e9ad-a8a5-4467-907c-b66e5e76b65b`, branch
`feat/card-task-ffc7e9ad`, worktree `/work/worktrees/task-ffc7e9ad`.
Original Code/landing owner: `d422c5a9-e1b4-404d-9d28-438613671c15`.
Assigned master base: `7b8e687a73c17167a49b6ce0a3ace1d1ab1f796a`.
Reviewed source: `f2b9f918910c68125fbfaa6bf803657b87ac9a6b`.
Replayed source tip: `bcf77f88b7a1eb5182822f4e15356a3abf656fa2`.

All 24 non-master commits were cherry-picked oldest first, retaining authors and
messages and pushing each four-commit slice. Range-diff reports 23 `=` entries;
only `48b2face9 -> c442c72ee` is `!`, because the insertion context now follows
the four C1015 tests. The sole conflict was in `InstructionBundleTests.cs`:
both C1015 and C1011 groups remain, with the C1015 SourceLanding method's closing
brace retained before the C1011 `[Test]`. No documentation conflict occurred.

The 407 paths differing from the reviewed tip are exactly master's newer paths:
CARD-1014 spill incidents; CARD-1006 blocking-screen/fixture work; CARD-1005
census tooling; CARD-0901 plan status; CARD-1015 evidence policy, guards and
historical evidence deletions; CARD-1008 rollout/recycle work; and CARD-1018
host-less sign-in wording. All disjoint master files equal master byte-for-byte,
and all disjoint feature files equal the reviewed tip. The two overlaps contain
only master's added evidence-policy paragraph and four C1015 tests. No server
C# file differs from the assigned master base. The census literal remains 377.

The orchestrator bundle is byte-identical to the reviewed bundle: SHA256
`15f9660f2c09be8e32cddc98ab6d995f927d7a7b398da14e3b34256ef4d1d335`,
14,116 normalized characters against the unchanged 14,310 cap. Master had not
changed that file since the feature merge base.

Verification uses the [existing plan](../superpowers/plans/2026-10-03-card-1011-windows-grok-routing-plan.md)
CP-1/2/7, including the full InstructionBundleTests class, plus the compiled
census guard and the adjacent exact-bundle-hash guard. CP-1 should now expand
to 94 results (the reviewed 90 plus four preserved C1015 cases); CP-2/7 remain
44/8. The mandatory stage contract requires the checkpoint tool for the plan
rows, serially; the supplementary two-class check uses run-checkpoint.ps1.
The tool bootstrap and supplementary isolated build are the only additional
builds. No whole Unit rerun is commissioned: inherited evidence is 3,955 passed,
0 failed, 52 skipped at `9567afa32de09cb956ed478b612dcffbd1af8772`.
The brief supplies Windows CP-3/4/5/6 31/31 at that same SHA (Debug `8bd36a07`)
and clean Grok Review `d535eeec`. These are inherited, not fresh Linux results.

This note is frozen before qualification. Actual tested SHA, terminal outcomes,
fresh TRX rosters, unedited CHECKPOINT lines, validated source/build provenance,
full range-diff/path inventory, cleanup and final history-policy results belong
in the stored replay report `.antiphon/task-ffc7e9ad.md`; generated payloads stay
ignored. No new tests or production behavior are authored by this replay.

All PC-1 through PC-69 and every argument variant remain pending method-scoped
post-land SourceLanding Mutation. WQ-1 remains operator-excluded, WQ-2/3 retain
the accepted historical receipts, and WQ-4 remains post-activation. Restart now:
none. Server activation, live Debug pin/readback and bundle refresh belong to
the caller after delta-aware Review and landing of the original Code owner.
