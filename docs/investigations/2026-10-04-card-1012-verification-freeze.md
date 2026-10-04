# CARD-1012 Code verification freeze

Code and original landing owner: `81922318-16b4-422f-905a-940ec6bbf1a4`.
Task base: `48663c16e9d9b7defc3fabd2388f5b05e414ef5f`.
Branch: `feat/card-task-81922318`; runner checkout:
`/work/worktrees/task-81922318`.

The Code brief explicitly folds TestDesign into this slice. The existing
[plan](../superpowers/plans/2026-10-04-card-1012-cp2-minimum-plan.md)'s V-1,
V-2, R-1 and two-row Checkpoints table are frozen unchanged. S1 changes only
CARD-0927 CP-2's Min cell from 100 to 163. This note records admission and
verification design before execution; it does not claim later tests passed.

## Admission (2026-10-04)

At both the task base and freshly fetched `origin/master`
`0049877688f895bc4fb75e49cfe8911489b2a937`, source attributes expand to
DockerStackContractTests 112, CodexRunnerImageContractTests 11,
GrokRunnerImageContractTests 26 and JqRunnerImageContractTests 14: total 163.
All four source blobs are identical across those commits. Count argument rows
as executions, replacing the corresponding single method result.

GET `/api/agent-tasks/pipeline` and each `delegate.ps1 -Status` showed active
Code owners bd02f8d9 (CARD-0959), d422c5a9 (CARD-1011) and 2c35a27d
(CARD-1013). Their briefs identify no overlap with either changed Markdown
path. CARD-0959 and CARD-1011 share provider documentation with image-contract
inputs; CARD-1013 owns `tools/Antiphon.Checkpoints/Coverage/PlanCoverageReader.cs`,
coverage test assertions and seven exact `.gitattributes` entries. Those are
verification dependencies, not edit overlaps. This slice changes none of them.
The literal checkpoint census remains 377. No concurrent branch is imported.

GET `/api/runner-defaults` returned revision 2; the selected Linux runner and
Windows alternative were available/eligible. The spare runner was unavailable
and draining. No runner or platform pin is added.

The real importer was built from the clean task base through
`scripts/build-slot.ps1 -Label c1012-importer -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1012-tool/ --property:UseAppHost=false`.
It succeeded (one existing CS8602 warning), `slot=granted waited=0s`.
It accepted an ignored candidate copy with the changed cell: four imported rows,
CP-2 `minExecuted: 163`, unchanged literal-pipe filter and all four roster tokens.
V-1 will repeat the import against the actual committed target file and compare
it with the base import, excluding only plan provenance and CP-2's minimum.

## Frozen execution

Run the checkpoint tool against the CARD-1012 plan with the full committed
`--expected-source-sha`, `--keep-outputs` and serial rows. CP-1 runs the unchanged
four-class filter, minimum 163. CP-2 reuses that build for the whole Unit category,
minimum 2000. Inspect fresh TRX, including every intended class/method and all
nonpassing results. Validate the source receipts against the actual tested SHA.
An inherited failure needs a method-scoped unchanged-base reproduction; skips
remain individually disclosed and cannot be called a clean receipt.

The brief also requests direct-script floor discrimination. After the ordinary
rows, reuse their verified build through `scripts/run-checkpoint.ps1 -NoBuild`
for two explicitly additional diagnostic invocations of one partial selection.
Keep the same four class operands and select all source test method prefixes
except jq's 12-case `Version_row_accepts_only_exact_successful_pin_without_stderr`.
Expect 151 cases across all four classes: Min 100 must accept; Min 163 must refuse
with exit 3. This compares input thresholds without editing test or driver source
or deliberately mutating production. It is not a replacement for full CP-1.
Run the low-floor case before the high-floor case. Both use all four `-Expect`
tokens and the same committed expected SHA. Preserve the exact realized filters,
counts and unedited CHECKPOINT lines in the stored report.

Ordinary estimated cost is 11 minutes plus bootstrap/import and the two bounded,
shared-build diagnostics (approximately one minute). No full-assembly run or
unbounded integration class is needed. No new test or guard is introduced.
There are zero new PC IDs/variants. Missing-control discovery remains the
post-land SourceLanding Mutation stage's responsibility; CARD-0927's existing
PC-1/PC-2 remain pending under their original owner and are not discharged here.

Do not widen timeouts, loosen assertions, update census or run image/provider
acceptance. Commit/push before ordinary execution, keep source frozen, run
`scripts/check-evidence-diff.ps1` over task base through final HEAD, and clean only
owned alternate outputs. Raw evidence stays ignored. Later outcomes belong in
the stored task report. Restart: none; no activation owner action is needed.
