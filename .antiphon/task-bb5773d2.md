# CARD-1483 S5 Code report

S5 implementation and required red witnesses are complete. This report is committed
before the final CP-5; the final caller report and ignored checkpoint receipt carry
the actual final result and full tested SHA, rather than retroactively changing this
tracked source after qualification.

Original Code/landing owner: bb5773d2-1779-40a3-8afa-db6701554690.
Branch: feat/card-task-bb5773d2. Worktree: /work/worktrees/task-bb5773d2.
Task base: 4d4718a5bc3305565c26f8c42069e5513951753c, verified equal to clean
origin/master at start. No carried commits, rebase, merge, branch switch or force push.
Every commit is pushed to this assigned branch. No desktop, Windows qualification,
releases directory, server or runner was changed. Restart: none.

Plan: docs/superpowers/plans/2026-10-22-card-1483-real-msbuild-land-audit-plan.md,
the landed S2/S3/S4 amendments, and
docs/superpowers/plans/2026-10-23-card-1483-s5-amendment.md.
Final round, with the brief's explicit Unit/whole-suite deferral (CARD-1466).
No ordinary R-n rows exist. S6 wording is outside this task.

## Implementation

- scripts/audit-land-verification-graph.ps1 is the fail-closed production wrapper.
  Update runs the unchanged emulation rules, then the built real evaluator. It
  publishes digest/count and digest/SDK/pins records only after evaluation exit 0
  and a second disk/HEAD binding check. A missing tool, failed evaluation or dirty
  tracked source/pins refuses without overwriting either previous record.
- scripts/audit-land-verification-precheck.ps1 retains the former script's rules,
  with only a scratch-manifest destination parameter and explanatory header.
  Legacy fixtures exercise that pre-check directly with unchanged assertions;
  C1385_RegistryMatchesNarrowGraph still invokes the production wrapper.
- Check runs the pre-check and requires exactly three matching evaluation lines:
  graph digest, selected SDK and tracked pins blob SHA. It performs no evaluation.
- LandGraphEvaluationScriptTests.cs adds twelve integration executions, including
  successful replacement of existing records, missing/malformed/mismatched records,
  real evaluator exit 2, missing DLL and dirty project/pins. CARD-1498 also runs
  directly through real evaluation and asserts the resolved docs path.

No evaluator allowances, pin edits, assertion relaxation or timeout widening.
The two-file publication is not transactional: evaluation record first, manifest
last. An interrupted replacement may leave mismatched records; Check refuses them.

## Activation and cost

At land time the existing service still hashes the candidate's tracked definition
rows and compares the embedded two-line manifest. It runs no script or MSBuild
evaluation to decide the skip. Added per-land runtime: zero.
An author-time refusal prints `audit refused: ...` and exits 1. Evaluation nonzero
exits, including 2, do not publish either record. A stale graph digest takes the
existing full verification-build path; a failing build refuses publication through
the existing verifier. No new orchestrator HTTP refusal or runtime flag is added.

Reviewed defaults remain: retain emulation, no restore, Linux evaluation plus
reviewed OS/Target pins, master-only until the reviewed RC port. R-1 package props,
R-2 non-built projects and R-3 pinned task execution remain accepted residuals.
CARD-1528's static File.Exists and metadata-only reads are not assigned to S5 and
remain unresolved, not claimed repaired or passed.

Manual real-tree Update with the staged evaluator succeeded at implementation
bd770cb40fa88c280b2c3211e48b03e8d1c34290: slot=granted, waited=46s, held=37s.
This is the measured whole author-time invocation, including pre-check/evaluation;
it excludes compilation and slot queue time. No implicit build or restore occurs.
The first manual attempt at d31928248e12dc3e9eb7b80ec4550674e12078cc approved
evaluation but failed publication because PowerShell coerced a null File.Replace
backup path to an empty string (slot=granted, waited=0s, held=41s). This introduced
integration error was fixed with NullString.Value; replacement is now guarded by
the existing-record positive case. The failed attempt is retained, not called green.

## Generated records and inherited drift

Generated solely by `audit-land-verification-graph.ps1 -Update`:

    digest=84E2796292E985EEECCB2381FFBE133A81376B81F7A042F4D7BAEEE9A2C517D3 count=58
    sdk=10.0.402
    pins=ed6f685830ef82613daf6a8503b08159c84ec5de

The base manifest stored 637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E.
The task's selected definition rows are byte-identical to the base, which already
hashes to 84E279. The original script's Check was run in a clean detached checkout
at the exact task base: exit 1, `manifest does not match the selected graph`,
digest 84E279/count 58. That temporary base checkout was removed. This is inherited
drift corrected by the generated manifest, not a new build-definition change.

## Red evidence and final selection

Unlisted, justified executions: slot-gated checkpoint-tool bootstrap (waited=46s,
held=7s, build exit 0, four existing warnings); CP-Red before implementation;
the two manual Update attempts above; read-only base Check to attribute the stale
manifest. There was one actual isolated build in CP-Red; the report's build list
also contains the unselected final build. No suite, Unit run or deliberate mutant.

CP-Red ran the base production script and tool with only the new tests committed.
Fresh TRX: twelve results, three passed, nine expected assertion failures, zero
skipped/build/fixture failures. Seven unsafe target/Condition fixtures returned 0
instead of 1; missing solution also returned 0; Update emitted no evaluation record.
The selected imported-directory fixture and both R2 spellings already refused.

```text
CHECKPOINT CP-Red commit=f2c7d9adf9d2d12e383e3b40e3b4e2565bff2514 build=ok filter=/*/*/LandGraphEvaluationTests/C1483_S5_* executed=12 passed=3 failed=9 skipped=0 trx=/work/worktrees/task-bb5773d2/.antiphon/checkpoints/20261023-024810-f770/rows/CP-Red/run.trx slot=granted waited=0s dirty=0 source=f2c7d9adf9d2d12e383e3b40e3b4e2565bff2514 sourceState=clean buildSource=verified
```

Red wall 8m15s; build 450s; test host 24.7s. Red is expected defect evidence and is
not a qualified green certificate. Source/build provenance is clean and verified.

Final closed CP-5: the full LandGraphEvaluationTests class (42 executions) and full
LandNonCodeSkipTests class (102), total 144, expected zero failed/skipped. This
covers V-1..V-16, V-1a/V-1b/V-3a, the original pre-check and landing regressions.
The final result must name every ID's actual outcome and inspect the fresh TRX
method identities/counts, then validate report.json against the exact final SHA.
There are no deferred S5 V/R IDs; Unit/whole-suite execution and S6 are outside
the brief's selection. No repeat is required after a qualified green.

Rerun with the built checkpoint tool:

    dotnet <Antiphon.Checkpoints.dll> run --plan docs/superpowers/plans/2026-10-23-card-1483-s5-amendment.md --rows CP-5 --expected-source-sha <committed-head>

Then wait until non-75, and run scripts/validate-checkpoint-receipt.ps1 on its
report.json with the same expected SHA. Full-range scripts/check-evidence-diff.ps1
and current-master merge-tree are required after the final report commit.

## RC and next owner

An isolated temporary Git index applied all ten S4 commits, then S5's source
commits through bd770cb40, to RC 5a126f6fc5ae4e16d803b628716b8f0485a37174 without
conflicts. No branch/worktree was switched or RC written. Repeat on the final tip.
RC still has digest 637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E,
count 58 and the same pins blob. After S4/S5, regenerate both records through
Update on the RC source; the RC manifest and evaluation digest must be 637CFA,
not master's 84E279. SDK line must be that environment's actual selected SDK.

Mutation candidate artifact: docs/superpowers/mutations/2026-10-23-card-1483-s5-candidates.md.
Every PC-S5-1..5 and variant is pending, along with original unnumbered controls,
S1 controls, PC-S2-1a/1b/1c/2a/2b/3a/3b/3c, PC-S3-1..7 and PC-S4-1..6/all variants.
Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
After ordinary verification, next is Review. Caller lands original Code task
bb5773d2 after Review and commissions SourceLanding Mutation. Restart: none.
