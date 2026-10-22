# CARD-1511 removal matrix fixture repair and evidence

Original Code/landing owner: `d59d844d-b6a0-4fbd-b94a-b067911fb12d`.
Branch: `feat/card-task-d59d844d`; worktree: `/work/worktrees/task-d59d844d`.
Task base: `c5da1d2387c08adf86e301a3874a5cbcfd4d6a98`.
Restart: none. Production changes: none.

The specific Code brief requires minimal Final verification: the affected method,
not the default Unit lane or full integration class. This document supplies its
single closed checkpoint row. No full-assembly run or unbounded shared impact is
needed for this local test fixture change.

## Evidence and repair

Read CARD-1511 in full and the Debug report
`docs/investigations/2026-10-22-rc-group-10-timeout-debug.md` on master.
The earlier `2026-10-22-rc-windows-qualification-interim-reds.md` is absent from
master; read the full report from its cited branch `feat/card-task-77c15d47`,
commit `7d47ae6ca0d1e84d13ac5fa604065895f2543420`. Its timing conjecture is
superseded by Debug's deterministic explanation and the Linux witness below.
CARD-0971/1211 deliberately preserve nested repositories. The first full
inspection classifies the existing `nested/.git` as protected, so inspection two
cannot be reached with that fixture. The product contract is correct.

### S1: unchanged-source red witness

The existing failing test is already committed at the task base. Before any
fixture edit, ran the real method through `scripts/run-checkpoint.ps1` with
`-Name RED-1511`, `-Project tests/Antiphon.Tests`,
`-OutputPath bin-card1511-red/`, the exact CP-1 filter below,
`-MinExecuted 24`, `-ExpectedSourceSha` equal to the task base and
`-ResultsRoot .antiphon/card1511-red`. This additional build/test is expressly
required by the brief to establish the red witness; it is not another ordinary
checkpoint row or a deliberate mutant.

Fresh TRX identities: all 24 results belong to
`Antiphon.Tests.Application.AgentTaskLandRemovalMatrixTests.C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries`.
23 passed, 1 failed, 0 skipped. Only `(2, submodule)` failed: `fired` expected
True, actual False, test line 101. Linux Debian 12, .NET 9.0.21. Build 295.72 s,
tests 112.810 s; slot held 480 s, wait 0 s. The receipt is clean and the build
source verified; this is an intentionally red diagnostic, not a green gate.

```text
CHECKPOINT RED-1511 commit=c5da1d2387c08adf86e301a3874a5cbcfd4d6a98 build=ok filter=/*/*/AgentTaskLandRemovalMatrixTests/C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries executed=24 passed=23 failed=1 skipped=0 trx=/work/worktrees/task-d59d844d/.antiphon/card1511-red/RED-1511-20261022-222759-f571/run.trx slot=granted waited=0s dirty=0 source=c5da1d2387c08adf86e301a3874a5cbcfd4d6a98 sourceState=clean buildSource=verified
```

### S2: fixture repair

Keep all 24 original argument combinations. The existing submodule with its
mutation armed for inspection two must instead prove first-inspection refusal:
one status read, no hook firing, `ignored_content_preserved`, unchanged bytes,
index, registration, branch, HEAD and remote source. Keep the first-inspection
dirty-submodule mutation and its preservation assertions.

Add `(1, nested-repository)` and `(2, nested-repository)` to the same method.
Create a real repository with a `.git` file under disposable ignored `obj/` only
when that status boundary is reached. Assert the hook fired, exact boundary
count, protected-path reason, unchanged private marker/content, retained actual
worktree registration, index/HEAD/remote and no removal commands. This reaches
inspection two without pre-seeding a repository that must refuse earlier.
No timeout changes, retries or product changes.

## Verification design

| ID | Ordinary proof | Required result |
|---|---|---|
| V-1 | Existing `(2, submodule)` | Early preservation at reading one with no second hook; bytes and ownership preserved |
| V-2 | New `(1, nested-repository)` and `(2, nested-repository)` | Late repository preserved at its exact injected boundary |
| R-1 | Other 23 original combinations | Existing dirty-submodule, content, identity, metadata and registration protections retained |

All are required in CP-1. No ordinary IDs deferred. Windows post-fix qualification
is not claimed by this Linux run; the original failure is reproduced portably.
Manual acceptance is fresh TRX identity/count inspection, receipt validation,
full task-range evidence diff, master merge-tree, RC patch applicability and
remote-tip confirmation. Final outcomes go into the ignored stored report
`.antiphon/task-d59d844d.md` after this document and the fixture are committed,
so no report commit changes the final tested SHA.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-card1511-final/` | removal-matrix | `/*/*/AgentTaskLandRemovalMatrixTests/C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries` | V-1, V-2, R-1 | all 26 variants, 0 failed/skipped | 26 | 8 |

Run via the checkpoint tool with `--plan` this file and
`--expected-source-sha` equal to the committed final HEAD. The unchanged tool
binary built by the red witness may launch the run, avoiding a separate tool
bootstrap build. Wait until exit is not 75; validate its `report.json` using
`scripts/validate-checkpoint-receipt.ps1 -ExpectedSourceSha <final HEAD> -Rows CP-1`.
Inspect fresh TRX for all 26 identities and nonzero counts. One ordinary run;
rerun only a red row after a genuine repair. No repeated green proof is needed.

### Pending SourceLanding Mutation

All controls remain pending for Mutation after ordinary Review and landing.
Unbound candidate inventory (bind to the actual landed source before execution):

| ID | Deliberate mutant, owned by Mutation | Expected non-stub failure |
|---|---|---|
| PC-1 | Bypass the first protected-content refusal in `GuardedWorktreeRemoval` | V-1 and V-2 reading-one exact count fails because a second inspection is reached |
| PC-2 | Bypass the second protected-content refusal | V-2 reading-two ownership/registration/no-removal assertions fail even if final no-follow deletion preserves private bytes |

Both use the exact CP-1 method filter; every variant's result must be retained.
Mutation owns red/restore/green and missing-control discovery. These controls
are not executed or discharged by Code. Ordinary floor: 8 minutes plus authoring;
Mutation estimate: 16 minutes plus inspection/reporting, with precise budgeting
by its commissioning caller. No production restart is required.
