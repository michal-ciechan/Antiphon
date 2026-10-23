# CARD-1526 pinned-test repair: Code handoff

Repair and whole-delta landing owner: 15cca245-0f8c-4e01-bf7a-2450326b6113.
Original S1-S3 Code owner: f93ec86b-7d26-4d05-b16e-e40c85e42609.
Branch: feat/card-task-15cca245. Worktree: /work/worktrees/task-15cca245.
Repair base: 221224ae1754ed3235dc7d19a04dd921be91582a.
Full CARD-1526 code base: 247e615da70ec1f75a05d5aa56a9c7941ae94911.
Plan: docs/superpowers/plans/2026-10-23-card-1526-os-gated-code-real-type-witness-plan.md.
Mutation candidate: docs/superpowers/mutations/2026-10-23-card-1526-os-witnesses.md.
Restart: none. Desktop, live qualification, releases and CARD-1527 untouched.

The checker now consumes the pinned Git test blob, removes comments, requires
an exact test declaration and production type/factory code fragments, and checks
each optional-input setup assertion in code. Quoted examples do not supply
executable fragments. This remains bounded lexical consistency; runtime identity,
setup reaching production and actual execution are still Review obligations.
The owner documentation states the supported declaration shapes and boundary.

Regression slice: 28163f29d7391e9731a489aa8de6d83bc15b3e3f.
Repair slice: ec5f6b2745bb7475e96427c59d1d9122ba04e2e4.
The original 25 cases / 76 assertions remain, plus 13 named regression cases /
26 assertions. CP-1 requires 38 checker cases and policy-surface, 102 assertions.
CP-2 retains all 14 methods in the three exact named classes. The explicit brief
and plan override the boilerplate Unit lane: no Unit or broad assembly run.
V-1..V-10 and R-1 map to CP-1; V-11 is the new pinned-test contract. R-2 maps to
CP-2. R-3 is manual diff and integration compatibility. No ordinary IDs deferred.

Pre-fix proof at the regression slice used five precise cases:
test-seam-only, test-always-assigned, test-comment-only, test-case-missing,
test-comment-strings. Each failed on expected checker exit 1 versus actual 0,
not on setup or a build error. The driver observed all five expected failures.
BUILD SLOT granted lease=25c7c313-6712-4aa2-921b-37dba1557ea8 waited=195s maxcpucount=6
BUILD SLOT released lease=25c7c313-6712-4aa2-921b-37dba1557ea8 held=14s
An earlier outer 120-second deadline expired during slot wait, exit 124: no test
ran. Retry kept the 120-second execution bound and allowed five minutes for the
slot. Logs: .antiphon/card1526-repair/prefix-red.log and prefix-red-retry.log.
These narrow, unlisted runs prove the guarded defect before repair.

Review e9c9a232-744c-4c11-b9d5-d3da8fcee692 was retrieved from the task API;
its advertised Markdown report is absent from its remote branch. Its three
historical mutants guarded seam-only, absent-case-required and missing-path.
The stage contract reserves deliberate mutants for Mutation, so this Code task
runs ordinary pre-fix regression proof only. PC-1..PC-7, PC-2b and all PC-6
variants remain pending as enumerated in the unbound candidate, including the
new dropped-content/read control. No historical Review mutation is discharged.

Placement read from GET /api/runner-defaults and /api/session-runners: revision 2,
current Linux runner eligible; portable work stays unpinned. No host setting changed.

Manual integration at the repair slice: clean merge-tree against origin/master
cb87cfc42caf63502205787f51713f935721de56, tree
a641a85a97b1e4aee9d6ed43071755e808312c13. Ordered cherry-pick-equivalent
merge-tree simulation onto origin/rc/2026-10-20 at
5a126f6fc5ae4e16d803b628716b8f0485a37174 is clean with prerequisite plan
247e615da70ec1f75a05d5aa56a9c7941ae94911 first, then the full delta in order.
No shared ref or checkout is changed; the assigned branch stays fast-forward.
The final report-containing tip will be checked again. One initial simulation
helper invocation had an argument-splitting error; the corrected invocation
completed all eight picks. This is not test evidence.

This report is committed before final qualification so the final pushed tip is
the actual tested source. Later facts, unedited CHECKPOINT lines and fresh TRX
census are stored in ignored .antiphon/card1526-repair/results.log and the exact
checkpoint run's report.json/TRX, and in the final caller report. This pre-run
record does not claim checks that have not yet completed.

One additional leased bootstrap build is necessary to run the required checkpoint
tool: dotnet build tools/Antiphon.Checkpoints with OutputPath=bin-c1526-repair-tool/.
Import the plan into .antiphon/card1526-repair/manifest.yaml and retain the
original hard CP-1/CP-2 execution limits of 2/8 minutes. Run that manifest with
--expected-source-sha <HEAD> --total-timeout 12m --max-wait 50s, then wait with
the same expected SHA until exit is not 75. Validate report.json with
scripts/validate-checkpoint-receipt.ps1, inspect every intended method in fresh
TRX, and check evidence history over both repair base..HEAD and full code
base..HEAD. Remove only this task's owned build outputs after all runs complete.

Bootstrap completed at repair slice ec5f6b2745bb7475e96427c59d1d9122ba04e2e4:
6.40 seconds, 0 errors and 4 existing nullable warnings; held 7 seconds.
BUILD SLOT granted lease=633e546e-5ce2-4760-b41c-13d7e605f120 waited=0s maxcpucount=6
BUILD SLOT released lease=633e546e-5ce2-4760-b41c-13d7e605f120 held=7s
The initial report add encountered the repository ignore rule; this allowed
individual Markdown report is explicitly added before ordinary qualification.
