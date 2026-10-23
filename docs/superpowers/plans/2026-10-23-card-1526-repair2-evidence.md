# CARD-1526 scanner repair 2: frozen verification handoff

Code task/source: 663fcd39-a18d-4831-a116-dc324df29b15.
Assigned branch: feat/card-task-663fcd39.
Worktree: /work/worktrees/task-663fcd39.
Original Code owner: f93ec86b-7d26-4d05-b16e-e40c85e42609.
Whole-delta landing owner: 15cca245-0f8c-4e01-bf7a-2450326b6113.
Task base: 2a22d9b9855424f5d2758e329f3fced39f096df1.
Plan: [OS witnesses](2026-10-23-card-1526-os-gated-code-real-type-witness-plan.md).
Controls: [Mutation candidate](../mutations/2026-10-23-card-1526-os-witnesses.md).
Restart: none. No desktop, Windows qualification, release directory or host setting changed.

The original nine commits from 247e615da through 78d584ecc were cherry-picked
in order. The first plan commit was already present in master and was retained
as an empty commit. Only docs/apphost-runbook.md conflicted: master's CARD-1527
section precedes the chain's Failed startup-path pick section, both retained.
The re-cut ends at b429d027d48d980be3311b2b1b05b0980109ec5f (see Git for the
exact full identity if this abbreviated re-cut reference is used by tooling).

The literal requested global diff against 78d584ecc is not empty: 67 unrelated
files already differ on the newer master base. Removing those changes would
revert unrelated landed work. The task-footprint comparison is empty:
`git diff 78d584ecc b429d027d -- $(git diff --name-only 247e615da^ 78d584ecc -- . ':!docs/apphost-runbook.md')`.
Thus all original chain files except the deliberately combined runbook are
byte-equivalent before this repair; newer master files are retained unchanged.

Regression commits:
- 86647a9ca9955fb081a21be87bcf2672ceefbfa5: 13 new named cases.
- 0f4006c695aded436b495991b5ebd3d56a06a203: correct strict wrapper argument binding.
Repair: e5538730c92586eefce8c1e29273f06707278649.
The 38 old cases and 102 assertions are preserved. New total: 51 checker cases,
102 exit/diagnostic assertions plus 26 policy assertions = 128 assertions.
The test diff only adds fixtures/cases and changes the strict child launch helper;
no old condition, timeout or expected outcome is weakened.

Pre-fix evidence: five comment bypass cases failed with checker actual=0
(unterminated C#, unterminated PowerShell, nested PowerShell, inner-close-only,
and unterminated comment after otherwise valid code). Unterminated-string failed
with actual=1 versus expected=2/reason. Six valid controls passed. Initial strict
case failed in argument setup, not defect proof; the corrected committed wrapper
then failed with actual=2 and the missing `value` property exception under Latest.
The first thirteen-case diagnostic took a slot immediately, held 36 seconds;
the corrected strict case held 4 seconds, waited 0. These are expressly bounded
unlisted pre-fix diagnostics, not ordinary green receipts.

BUILD SLOT granted lease=1bfff847-f1f7-4a7d-9d23-dec1bd23b4c1 waited=0s maxcpucount=6
BUILD SLOT released lease=1bfff847-f1f7-4a7d-9d23-dec1bd23b4c1 held=36s
BUILD SLOT granted lease=cef58789-fa3b-4050-8cd4-f198114bf526 waited=0s maxcpucount=6
BUILD SLOT released lease=cef58789-fa3b-4050-8cd4-f198114bf526 held=4s

Scanner policy: retain closed literal arguments while masking them in the token
view; ignore comment openers inside strings. C# slash blocks do not nest,
PowerShell blocks do. Unterminated blocks and strings refuse with an explicit
kind/reason. Unsupported ambiguous raw/interpolated/here-string/subexpression
forms refuse instead of guessing; details are in the testing owner. This is
lexical declaration consistency, not a compiler or runtime execution witness.

Placement GETs succeeded: defaults revision 2, eligible Linux runner. No Runner
or Platform pin was passed. The brief/plan explicitly excludes Unit, full
assemblies and native operations. Final ordinary obligations: V-1 through V-14
and R-1 via CP-1; R-2 via the 14 methods in all three full named classes in CP-2;
R-3 via manual diff/integration proof. No ordinary obligation is deferred.

One unlisted bootstrap build is needed to run the checkpoint tool, through the
host slot: `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1526-r2-tool/ --nologo`.
At e5538730c92586eefce8c1e29273f06707278649 it took 4.79 seconds, 0 errors and
4 existing nullable warnings; slot waited 0, held 6 seconds.
BUILD SLOT granted lease=254cba1e-284e-4468-8957-e8ea09936bf7 waited=0s maxcpucount=6
BUILD SLOT released lease=254cba1e-284e-4468-8957-e8ea09936bf7 held=6s

This report is committed before final qualification. Import the plan through the
built checkpoint tool, set only the two existing hard row limits (2/8 minutes),
then run its manifest with `--expected-source-sha <HEAD> --total-timeout 12m
--max-wait 50s`. Wait until exit is not 75. One test-project build in that run.
Validate report.json with scripts/validate-checkpoint-receipt.ps1, inspect fresh
TRX for all 14 intended methods and CP-1 output for 51 cases plus policy/128.
Keep final receipts, exact actual SHA and unedited CHECKPOINT lines in the ignored
.antiphon/task-663fcd39.md and run directory. This frozen report claims no pending
run outcome. Run check-evidence-diff.ps1 over the entire task base..final HEAD.

Before this report, merge-tree against origin/master
2a22d9b9855424f5d2758e329f3fced39f096df1 was clean (tree
f768bd1e24c0c14382bfecc3a40a2d69aab36b90). The ordered RC simulation below
was clean at origin/rc/2026-10-20, 5a126f6fc5ae4e16d803b628716b8f0485a37174.
Simulation uses merge-tree with each picked commit's first parent as merge base,
then unreferenced commit-tree objects. It changes no ref or checkout. Recheck
including this report commit before final reporting. The RC takes the ORIGINAL
chain, not the re-cut commits, followed by these repair commits, in order:

1. 247e615da70ec1f75a05d5aa56a9c7941ae94911
2. 83fd5dd6ab93de2e455a27f583429bf39cbc4092
3. 81414fac1f39b9870e9e7f697d8e3dfc7dbd5407
4. f71a3113b4583dcff22fb6656ddc9eb57b483fb7
5. badbd75a202d6cc9368da238d9186c6ee7133982
6. 221224ae1754ed3235dc7d19a04dd921be91582a
7. 28163f29d7391e9731a489aa8de6d83bc15b3e3f
8. ec5f6b2745bb7475e96427c59d1d9122ba04e2e4
9. 78d584eccc37eedf7119b765454928f619d3b4fa
10. 86647a9ca9955fb081a21be87bcf2672ceefbfa5
11. 0f4006c695aded436b495991b5ebd3d56a06a203
12. e5538730c92586eefce8c1e29273f06707278649
13. The report commit containing this file (full SHA in final caller report).

Every PC/variant remains pending for SourceLanding Mutation: PC-1, PC-2,
PC-2b, PC-3, PC-4, PC-5, PC-6 (five), PC-6b (two), PC-6c (three), PC-6d (two),
PC-7, PC-8 (three), PC-9 (two), PC-10. The stage contract reserves deliberate
mutants for Mutation, so the brief's three requested mutants were added there;
none was executed or credited by Code. Review 6690773f's finding is historical.
Next is independent Final Review of the exact final tested/pushed source; the
caller lands through the original landing owner and commissions SourceLanding
Mutation afterward. Server bundle activation is a separate reviewed RC action.
