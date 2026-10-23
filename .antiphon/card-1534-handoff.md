# CARD-1534 frozen Code handoff

Code task and landing owner: 5696ae92-4d1b-4660-abe7-8eb896ffca87.
Branch: feat/card-task-5696ae92.
Worktree: /work/worktrees/task-5696ae92.
Task base: 3d234eda960feb72b74db4c6b4fc7b1ccfa9cc97.
Final round, brief-scoped: this script only; no Unit, integration assemblies,
documentation-contract CP-2, desktop, Windows qualification or restart.
Restart: none. The brief explicitly skips Plan and TestDesign.

The checker now resolves candidate witnessId only after ContainsKey, like the
existing optional case value. Both candidate dispositions use the resolved value.
The new named non-behavior-without-witness-id fixture asserts physical absence
before invoking the real checker. strict-whole-suite runs the entire case table
and policy assertions under Set-StrictMode Latest, propagates strict mode to
every checker child, and checks the same ordered names, actual exit codes and
assertion count against the default run. Each child independently checks the
original expected exit and diagnostic; comparison alone is not the oracle.
Existing 51 cases and 128 assertions are preserved; the new omission case adds
three assertions. Each mode runs 52 checker cases and 131 assertions. Three
outer strict-suite assertions make the parent's count 134 (265 total across
both processes). Existing checker children retain the 15-second deadline;
the new whole-suite child has a 120-second deadline. No timeout was widened.

Red-first commit: be0b67cc6a8f882c64a54f3d03b68734265eb5cc, pushed before proof.
Both requested pre-fix diagnostics failed: strict-whole-suite's child reports
candidate-reason expected=0 actual=2, and the focused omission case reports
expected=0 actual=2. Both say the witnessId property cannot be found. Checker
source at that commit is unchanged from the task base/current master.
Commands were -Case strict-whole-suite and -Case non-behavior-without-witness-id
-StrictSuite. The bounded unlisted red diagnostics are required by the brief,
not checkpoint passes. They shared one lease, held 121 seconds, waited 0:

BUILD SLOT granted lease=1714acc0-1087-472a-a705-6ea84e062892 waited=0s maxcpucount=6
BUILD SLOT released lease=1714acc0-1087-472a-a705-6ea84e062892 held=121s

Before final verification the new comparison saves the pre-comparison assertion
count; comparing after adding the parent's own assertions would be incorrect.
This test-helper correction does not change the observed red exit oracle.

The sole unlisted build bootstrapped the checkpoint tool at the same red-test
commit: dotnet build tools/Antiphon.Checkpoints
--property:OutputPath=bin-c1534-tool/ --nologo, through build-slot.ps1.
It succeeded in 6.43 seconds, 0 errors, 4 existing nullable warnings:

BUILD SLOT granted lease=fedc1736-715f-4b01-becb-523ec7dbcbe5 waited=0s maxcpucount=6
BUILD SLOT released lease=fedc1736-715f-4b01-becb-523ec7dbcbe5 held=7s

## Optional-member audit

The requested grep of both scripts for $case., $candidate., $decl., $witness.
and $input. found only $case and $candidate reads; no declarations/reads of the
other three variable names. Reviewed the actual witness alias $w as well.

- $case.value is optional and already guarded by ContainsKey plus index access.
- $case.state and $case.setupAssertion are required (Need); keyPresent is a
  required Boolean, validated before the state-specific conditions. These are
  not optional. The state-specific conditions use the guarded caseValue.
- $candidate.witnessId was optional for non-behavior and is now guarded once
  before either disposition reads it. Missing witness IDs still refuse witness
  dispositions; populated IDs still undergo dictionary/path validation.
- $candidate.revision, line and disposition are required candidate identity and
  classification members. reason is read via Need's indexed access.
- $w.nativeObligation and input are required witness records; kind, cases and key
  are required for optional-config, description/setupAssertion for other input.
  Other $w identity/type/path/setup/execution members are required by Need.
- restart.nativeObligation is conditionally required only when restart.required;
  the false branch uses ContainsKey to reject a contradictory obligation.
- Test fixture nested reads are initialized in the valid/witness records or
  assigned before use. The new omitted key is checked with ContainsKey, never
  read directly. There are no other optional reads in these requested groups.

Manual inspection compared the entire old case-table body against origin/master
410738a522585e07943cabf23c1243790773d153: removing only the added omission case
makes it byte-identical. Both scripts are ASCII and parse with no errors; AST/
token checks reject post-5.1 conditional/null/pipeline operators. The changed
syntax is Windows PowerShell 5.1-compatible. Native Windows 5.1 execution was
not performed (existing runtime APIs require pwsh); the brief forbids touching
the desktop qualification. No owner docs or policy selections were changed.

Both required placement GETs succeeded; defaults revision 2, default server2,
eligible Linux runner. No Runner/Platform argument or fleet URL is embedded.

## Verification design

V-1: strict-whole-suite matches every default case's expected result and actual
exit code under strict mode. V-2: non-behavior-without-witness-id accepts a
physically absent member in both modes. R-1: all original 51 cases and 128
assertions retained. R-2: optional-read, ASCII, 5.1 syntax and table-diff manual
inspection. No integration class or unbounded shared behavior is affected.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | witness-strict-mode | `pwsh -NoProfile -File scripts/test-os-witnesses.ps1` | V-1, V-2, R-1 | 52 checker cases in each mode; strict-whole-suite passes; 134 parent assertions and 131 strict child assertions; exit 0 | n/a | 3 |

Run ONCE through the bootstrapped checkpoint tool using this report as --plan,
--expected-source-sha <committed HEAD> --row-timeout 4m --total-timeout 6m
--max-wait 50s. Wait to terminal exit (never settle on 75). This is a build-free
command row, so counts come from fresh console.log, not nonexistent TRX; source
buildSource is legitimately notApplicable. Validate report.json with
scripts/validate-checkpoint-receipt.ps1 and inspect both named regressions and
the entire result roster. Delete both owned bin-c1534-tool output directories.
Run scripts/check-evidence-diff.ps1 over the entire task base..HEAD.

This frozen report is committed with the fix BEFORE final qualification; it
claims no outcome for that future run. Exact final SHA, unedited CHECKPOINT
lines, receipt validation, manual results, merge-tree and RC picks are recorded
in the ignored .antiphon/task-5696ae92.md. Raw evidence: .antiphon/c1534/ and the
checkpoint run directory. No generated logs, JSON, receipts or TRX are committed.

Mutation remains pending. New candidate PC-11: remove the optional witnessId
guard, exercise non-behavior-without-witness-id under StrictSuite, restore and
verify; Mutation owns the deliberate mutant and missing-control discovery.
Inherited controls in docs/superpowers/mutations/2026-10-23-card-1526-os-witnesses.md
remain pending and are not discharged here: PC-1, PC-2, PC-2b, PC-3, PC-4, PC-5,
PC-6 (five variants), PC-6b (two), PC-6c (three), PC-6d (two), PC-7, PC-8 (three),
PC-9 (two), PC-10. No ordinary obligation is deferred to a later Final round.

RC picks follow the original CARD-1526 chain and its repair commits from the
repair2 evidence file, including b11e200a12976c52a5d8cf3bca888deed7b1c763, on
5a126f6fc5ae4e16d803b628716b8f0485a37174, then the two commits of this task in
chronological order. Full exact list is in the final ignored report. No RC ref,
master ref, deployed service or live qualification is changed by Code.

Next: light independent Review of the exact final tested/pushed source. Caller
lands through this original Code owner, then commissions SourceLanding Mutation.
