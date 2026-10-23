# CARD-1526 Code evidence handoff

Original Code / landing owner: f93ec86b-7d26-4d05-b16e-e40c85e42609.
Branch: feat/card-task-f93ec86b.
Worktree: /work/worktrees/task-f93ec86b.
Task base: 247e615da70ec1f75a05d5aa56a9c7941ae94911 (clean origin/master at start).
Plan: docs/superpowers/plans/2026-10-23-card-1526-os-gated-code-real-type-witness-plan.md.
Mutation: docs/superpowers/mutations/2026-10-23-card-1526-os-witnesses.md.
Restart: none. No desktop, live Windows qualification or releases checkout touched.

Implementation: pinned-Git OS witness declaration checker and isolated fixture
harness; Code/Review mandatory owner pointers; changed behavior, real factory/type,
absent/empty/value setup, native pending/receipt and startup rename/delete rules;
exact-candidate native probe, post-push restore rehearsal, forward rc compensation.
The checker proves syntax/inventory only. Runtime identity and actual execution
remain Review responsibilities; native operations described here were not run.
CARD-1525 repair remains separately owned; its final reviewed witness is unbound.

Scope override: the explicit brief and plan require the two portable rows, not the
boilerplate Unit lane. CP-1: 25 checker cases / 50 assertions plus 26 policy
assertions. CP-2: all 14 methods in TaskPlatformGuidanceTests,
CheckpointManifestDocumentationTests, CheckpointRepeatDocumentationTests.
No sibling RunnerDefaultGuidanceTests, namespace, assembly or Unit-lane execution.
V-1 through V-10 map to CP-1; R-1 to CP-1, R-2 to CP-2; R-3 is the manual diff,
merge-tree and sequential cherry-pick simulation. No ordinary IDs deferred.
PC-1..PC-5 and every named variant remain pending for SourceLanding Mutation.

Historical pre-fix red at 83fd5dd6a (full SHA in git history):
`timeout 120s pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1526-policy-red -- pwsh -NoProfile -File scripts/test-os-witnesses.ps1 -PolicyOnly`
failed on `ASSERT policy: missing OS-witness requirement` (exit 1), not setup/tooling.
BUILD SLOT granted lease=05a54404-d094-40ac-93fe-80151c8a661c waited=0s maxcpucount=6
BUILD SLOT released lease=05a54404-d094-40ac-93fe-80151c8a661c held=1s

Unlisted bootstrap build was necessary to run the prescribed checkpoint tool:
`timeout 180s pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1526-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1526-tool/ --nologo`
Result: 0 errors, 4 warnings, 17.68 seconds build elapsed, at S3 commit f71a3113b.
BUILD SLOT granted lease=4051fba3-23bf-4cc0-8f08-dcd76089d618 waited=0s maxcpucount=6
BUILD SLOT released lease=4051fba3-23bf-4cc0-8f08-dcd76089d618 held=19s

This tracked report is frozen before final qualification so its commit is tested.
Fresh outcomes, actual tested full SHA, unedited CHECKPOINT lines, receipt
validation and TRX census are recorded in ignored `.antiphon/card1526/results.log`
and the checkpoint run's report.json/TRX; final caller report names that run.
This pre-run record makes no claim that those not-yet-run checks have passed.
Only failed rows will be rerun; any repair adds its own truthful history.

Reproduce: bootstrap the tool through the slot gate above, import the plan with
`dotnet tools/Antiphon.Checkpoints/bin-c1526-tool/Antiphon.Checkpoints.dll import --plan <plan> --out .antiphon/card1526/manifest.yaml`, pin CP-1/CP-2 timeoutMinutes
at 2/8 (no expansion), then use `run <manifest> --expected-source-sha <HEAD>
--total-timeout 12m --max-wait 50s` and `wait --run <printed-run> --max-wait 50s`
until terminal. Validate report.json with scripts/validate-checkpoint-receipt.ps1,
inspect all fresh class/method identities/counts, and run check-evidence-diff.ps1
over the full task base..HEAD. Remove owned bin-c1526*/ outputs when finished.
