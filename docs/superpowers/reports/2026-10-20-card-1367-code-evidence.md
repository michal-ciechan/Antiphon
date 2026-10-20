# CARD-1367 Code evidence

Implemented the relative-output helper apphost fix. The unfixed build reproduced
the collision; the fixed build passed all helper-preservation checks.

- Original Code task / landing owner: `cd152fbc-d069-4e58-982e-769fdf7dddee`.
- Branch: `feat/card-task-cd152fbc`; worktree: `/work/worktrees/task-cd152fbc`.
- Task base: `ace4ef10d1b58ff50fb2cdd47af361859cd5add3`.
- Plan: `docs/superpowers/plans/2026-10-20-card-1367-fakeclaude-apphost-collision-plan.md`
  at the task base above.
- S1, harness committed and pushed before red:
  `abb667ac2a64418cfe5843a959d41f857a7bbf0f`.
- S2, reference metadata and owner documentation committed and pushed before green:
  `18317667d8cdef3947639c9dcbf8b3f9d7721e1b`.
- This report is an evidence-only slice after S2; the tested SHAs below remain the
  actual tested commits. Restart: **none**. Target: **master only**.

`scripts/test-isolated-helper-build.ps1` runs one real Linux build through the host
slot wrapper. It records source snapshots, the native build exit, complete output,
individual checks and an output inventory before cleanup. It refuses dirty source,
unsupported OS, existing output roots, slot timeout and unexpected build outcomes.
Both fake-provider references now carry `Private="false"`; the established staged
paths and shared-output checkpoint workaround remain.

## Ordinary verification

Final round, with the brief's explicit build-only selection: one red build and one
narrow green build. No Unit, integration, assembly, Windows, helper-process or
positive-control executions were authorized or run. TUnit counts and TRX are
**not applicable**, not zero-test passes. No ordinary V/R IDs are deferred.

| ID | Checkpoint | Actual outcome |
|---|---|---|
| V-1 | CP-1 | Passed: one build exited **1**, with MSB3021/MSB3027 collision diagnostics (10 build errors); **3/3** witness checks passed, including the ordinary top-level `fakeclaude` file. Harness exit **0**. |
| V-2 | CP-2 | Passed: one build exited **0**; **12/12** checks passed. Harness exit **0**. |
| R-1 | CP-2 | Passed within those 12 checks: each helper directory contained its extensionless apphost, DLL, deps JSON and runtimeconfig JSON; `Antiphon.Tests.dll` existed at the consumer output root. |

The harness used no `UseAppHost`, `ArtifactsPath`, `OutDir` or `UseArtifactsOutput`
override. The red build used the unfixed references, so the defect was witnessed
before its repair without deliberate source mutation.

| Row | Harness seconds, including cleanup | Checkpoint wall | Inner build slot | Outer command slot |
|---|---:|---:|---|---|
| CP-1 | 279.945 | 4m51s | granted; waited=30s; held=244s; maxcpucount=6 | granted; waited=0s |
| CP-2 | 264.643 | 4m31s | granted; waited=0s; held=254s; maxcpucount=6 | granted; waited=0s |

Exactly **two build-driver invocations**, no unlisted builds or test suites, and no
repetitions after green. The command-row tool summary reports zero managed builds
because the row's harness owns its build; that does not replace the inner exits.

## Source and receipts

Both runs passed the committed HEAD as `--expected-source-sha`, with
`--row-timeout 40m`. Each `wait` was followed until a terminal exit, not exit 75.
Both checkpoint `report.json` files passed `scripts/validate-checkpoint-receipt.ps1`
for their exact row and SHA. Their command-row provenance is `notApplicable`;
each harness's own source snapshot records `dirty=0`, `state=clean`,
`buildSource=verified`, and matching before/after HEAD and fingerprint.

Unedited checkpoint lines:

```text
CHECKPOINT CP-1 commit=abb667ac2a64418cfe5843a959d41f857a7bbf0f build=n/a filter=pwsh -NoProfile -File scripts/test-isolated-helper-build.ps1 -Phase Red -OutputPath bin-c1367-red/ executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=abb667ac2a64418cfe5843a959d41f857a7bbf0f sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=18317667d8cdef3947639c9dcbf8b3f9d7721e1b build=n/a filter=pwsh -NoProfile -File scripts/test-isolated-helper-build.ps1 -Phase Green -OutputPath bin-c1367-green/ executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=18317667d8cdef3947639c9dcbf8b3f9d7721e1b sourceState=clean buildSource=notApplicable
```

Unedited inner build-slot lines:

```text
BUILD SLOT waiting label=card-1367-Red position=1 occupied=4/4 elapsed=0m
BUILD SLOT granted lease=a756a12c-d673-4595-9e7f-90b223e13cc3 waited=30s maxcpucount=6
BUILD SLOT released lease=a756a12c-d673-4595-9e7f-90b223e13cc3 held=244s
BUILD SLOT granted lease=6ace14e7-1692-490f-af21-92558a528a20 waited=0s maxcpucount=6
BUILD SLOT released lease=6ace14e7-1692-490f-af21-92558a528a20 held=254s
```

Evidence paths, relative to the worktree above:

- CP-1: `.antiphon/checkpoints/20261020-014124-335e/report.json`.
- CP-2: `.antiphon/checkpoints/20261020-014701-b7fa/report.json`.
- Red: `.antiphon/card-1367/red-20261020-014141-dffd696e/`.
- Green: `.antiphon/card-1367/green-20261020-014710-70b5efda/`.

Each harness directory retains `build.log`, `build-slot.log`, `source.json`,
`checks.json`, `summary.json` and `inventory.json`. Red inventoried 3,580 entries
and green 3,648; each invocation created and removed 28 alternate output roots.
No ordinary `bin/`, `obj/` or another run's output was deleted. Generated payloads
remain ignored.

## Tool startup and static checks

The first CP-1 startup (`20261020-014054-6816`) refused before any build:
`output-ledger-mismatch` on its shadow tool ownership file. The prebuilt tool had
been copied from another run including its ownership metadata. Removed those
copied metadata files and the failed shadow output, preserving the failed run's
state; the single allowed retry succeeded. No extra build was bootstrapped.
A shell `rm -f` cleanup attempt was rejected before execution; cleanup used
explicit file deletion and empty-directory removal after checking for links.

Prebuilt tool DLL SHA256:
`81c62ddedcdc44509da7507cbe6dff19076e581de48d125c0eebf14c00ae9899`.
Source copy: `/work/worktrees/task-2029a2bb/.antiphon/checkpoints/20261020-013634-1f2a/tool/`.
Local invocation: `dotnet .antiphon/card-1367-tool/Antiphon.Checkpoints.dll run --plan
docs/superpowers/plans/2026-10-20-card-1367-fakeclaude-apphost-collision-plan.md
--rows CP-n --expected-source-sha <committed-sha> --row-timeout 40m --max-wait 50s`.

Read `/api/runner-defaults` and `/api/session-runners` before execution: the
configured Linux default was unavailable/draining and another eligible Linux
runner was available. No fleet location was embedded in the harness.

PowerShell parser and `git diff --check` passed. The full task-base-to-S2 evidence
history check passed (2 commits, 0 violations). After fetching `origin/master`
(`6ec71d427574f3a1b74bfbe2042e0ea9a819d191`), `git merge-tree --write-tree
origin/master HEAD` passed at S2, tree `660632b845de24c5bdc09ecf0d3b528d09b1ff53`.
The final report includes the repeated history and merge checks at the published
evidence tip; these are static checks, not additional builds or suites.

## Handoff

Ordinary implementation and V-1/V-2/R-1 are complete. Initial ordinary Review is
required. The plan lists **zero PC-n rows/variants**; SourceLanding Mutation and
missing-control discovery remain pending after Review and the caller's landing of
the original Code task. No Mutation qualification is claimed. There is no runtime
activation requirement for this build/test-only change.
