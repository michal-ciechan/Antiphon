# CARD-1537 committed graph fixture repair

Code/landing owner: `d3114f25`, branch `feat/card-task-d3114f25`, worktree
`/work/worktrees/task-d3114f25`. Base: `cbe0eda6a750dd934c1f3ed0bac4d1caee1f28bc`
(current origin/master when work began). The focused brief skips separate Plan/TestDesign
and overrides the generic Final profile: no Unit lane, whole class or whole suite.

## Implementation and observed red

Only fixture/test code changes. `SeedGraphAsync` now delegates to `CopyGraphAsync`,
which selects the real build graph from committed `ls-tree` rows, reads exact binary
Git objects, stages without autocrlf conversion, restores committed index modes and
asserts the seeded graph's complete rows match before any landing skip assertion.
Production fingerprinting and fail-closed verification remain unchanged. No timeout,
retry, skip or existing assertion changes. The Slow category and adjacent registry
reason are added together for the known 35-83 second Windows landing cases.

The new two-case real `LandingGitFixture` witness forces a genuine CRLF checkout,
includes binary bytes and an executable index mode, and repeats with the committed
props file absent from the working tree. The destination has isolated configuration
and `core.filemode=false`. It compares independently committed source/destination
tree rows, rather than computing the expected result with the copy helper.

Red source: `e3bdf41ee8760f2c8ec0fb087150da745248e1d6` (committed/pushed before execution).
Exact selection: `/*/*/LandNonCodeSkipTests/C1537_*`. Executed 2, failed 2, passed 0,
skipped 0. The false case fails its tree identity assertion: props blob
`8c119d5413b585461eb318f359a04eca07a09665` became
`a5d93e39436a395fb4fc793c538a36f12f9790f5`, and mode 100755 became 100644.
The true case throws FileNotFoundException at the inherited File.Copy; this is
absent-input evidence, not an additional assertion-based positive control.
The original seeding implementation is identical on the master base and RC base.

The red run is the required extra pre-fix driver, gated by build-slot with a 12m hard
cap: `slot=granted waited=0s`, held 317s. Its fresh TRX is under
`.antiphon/c1537-red/`. The red build also compiled the checkpoint tool; a task-owned
copy under `.antiphon/c1537-tool/` permits invoking that tool without another bootstrap
build. All `bin-c1537-red/` directories were removed before the final checkpoint.

## Ordinary verification and native boundary

| ID | Required outcome | Selection/evidence |
|---|---|---|
| V-1 | Both real-type CRLF/absent-input cases preserve committed binary bytes and modes | CP-1 C1537 cases |
| V-2 | Existing C1380/C1385 behavior, including all six reported Windows-red case identities, retains its original assertions | CP-1 complete C1380/C1385 families |
| R-1 | Slow marking and registry stay consistent in both directions | CP-2 G059/G060 |
| R-2 | RC picks apply; touched test project compiles there | Scratch RC application/build below |
| W-1 | Native Windows red/green, then affected existing cases | OWED to separately commissioned desktop lane |

The closed final checkpoint list below uses one shared isolated build. Its invariant
is exact seeded graph identity plus unchanged landing/build/skip behavior. It does
not claim assembly-wide coverage; no unbounded classes are selected. Estimated cost:
about 8 minutes build plus scoped tests and one minute for registry guards; hard
total 20m and row/build 15m, with units. Each method-OR operand ends in a wildcard.
No repetitions after green. The final tip, including this report, must be committed
and pushed before running; pass that exact HEAD as `--expected-source-sha`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1537-final/` | committed-graph-and-landing | `/*/*/LandNonCodeSkipTests/(C1380_*)\|(C1385_*)\|(C1537_*)` | V-1 V-2 | 46 passed, zero failed/skipped | 46 | 8 |
| CP-2 | S2 | CP-1 | slow-registry | `/*/*/TestClassificationPolicyTests/(C487_G059*)\|(C487_G060*)` | R-1 | 2 passed, zero failed/skipped | 2 | 1 |

Invoke the previously built checkpoint tool with `run --plan` this document,
`--expected-source-sha` the committed HEAD, `--total-timeout 20m --row-timeout 15m`.
Wait until its exit is not 75, inspect fresh TRX membership/counts for each row, and
run `scripts/validate-checkpoint-receipt.ps1` against its report and the same SHA.
Generated report/receipts and unedited CHECKPOINT lines remain ignored under
`.antiphon/checkpoints/`; settlement details go in `.antiphon/task-d3114f25.md`.

## RC application

Exact ordered source pick list:

1. `e3bdf41ee8760f2c8ec0fb087150da745248e1d6` (red witness and copy-helper extraction).
2. `9de2dc4f9ff8313a3912fb02e61fce4426b99ee6` (committed bytes/modes and consistent slow marking).

These applied without a Git conflict in detached scratch worktree
`/tmp/c1537-rc-apply` at origin/rc/2026-10-20
`5a126f6fc5ae4e16d803b628716b8f0485a37174`. The registry has `merge=union`:
the pick also imported the adjacent master-only CARD-1308 reason/class pair for
`Antiphon.Tests.Application.SessionRuntimeActionTemporaryBlockTests`, which does not
exist on this RC. Remove precisely that unrelated pair during the backport; keep
all original RC entries and add only the CARD-1537 pair. The preserved resulting
patch `.antiphon/c1537-rc.patch` changes just the intended two files, with exactly
three added registry lines. This adjustment must accompany the pick list.

The real touched Antiphon.Tests project compiled on the scratch RC: 0 errors,
1140 warnings, 2m37s, `slot=granted waited=0s`, held 159s. An incremental gated
compile refreshes the copied registry after that correction; no RC tests or native
Windows tests are claimed. Logs: `.antiphon/c1537-rc-compile.log` and
`.antiphon/c1537-rc-registry-compile.log`. These extra builds are the brief's explicitly
requested cheap RC compile check, not additional ordinary test selections.

W-1 remains OWED: on Windows run the new exact C1537 method at the red commit and
fixed source to prove native Git byte/mode preservation; run
`C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` to prove Verifier.Calls stays 0 with
a CRLF qualification checkout, then all six named original failing cells (including
empty/rename/README arguments). Use separately owned checkpoint/build-slot evidence.
The protected live qualification, PID 209072 and C:\Antiphon\releases were untouched.
Its historical incomplete groups and reds remain incomplete/red, not superseded here.

## Mutation handoff

All controls remain PENDING for method-scoped SourceLanding Mutation after ordinary
Review and caller landing of original Code task d3114f25. Candidate plan location:
`docs/superpowers/mutations/` (unbound). No deliberate mutant was run by Code.

| PC | Variants | Guarded defect / precise method |
|---|---|---|
| PC-1 | Working file present; working file absent | Restore working-tree copying instead of Git blob reading; C1537_SeedPreservesCommittedGraphWithCrLfCheckout |
| PC-2 | Both argument variants | Omit executable index-mode restoration with core.filemode=false; same C1537 method |

Mutation owns deliberate red/restore/green cycles and missing-control discovery.
No server or runner restart is required: restart=none.
