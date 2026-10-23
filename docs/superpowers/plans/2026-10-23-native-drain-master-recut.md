# Native custody/drain master re-cut (Code dc2b4959)

Base: `51fe4f6446997e9d92a92db7fa2a7dabb668f58b` (origin/master).
Landing owner: original Code task `dc2b4959`.

## Implementation

S1 cherry-picks `2c4160acf77e9d455330555b657d19c4866899a7` (witnesses).
S2 cherry-picks `a121becb6a0240e2f1743ee9948131c54d34b7c8` (repair).
Keep master's newer child environment setup. Resolve WindowsApps PowerShell to
unpackaged Program Files PowerShell; refuse packaged roots before resume; bound
output drain independently at 15 seconds and report incomplete cleanup on expiry.
Do not change execution deadlines, assertions, shared services or release refs.

## Performance budget

Windows-only native process integration; shared native lock per foreground driver.
One isolated Antiphon.Tests build and one full NightlyNativeOwnershipTests class.
No Unit lane or assembly sweep: explicit operator brief limits this re-cut to the
nine native regressions. No delivery, landing, lease or persistence code changes.
Prior task 089ef857 measured master build 799.510 seconds and fixed class 78.841
seconds; these are single samples, not p95. Allow 25 minutes for the isolated
build/row, 35 minutes execution total, 50 minutes hard wall, and 45 minutes slot
admission per acquisition. Bounds are provisional for this re-cut, with no product
timeout changes. Harness retains its 120-second per-case watchdog and fixture
barriers/cleanup limits. Record build, slot, per-case and aggregate times in the
external task evidence root. Setup is built once and staged beside test output;
each method owns its independent temporary fixture roots and joins its processes.
No selection approaches 30 minutes based on the prior observation.

The checkpoint-tool bootstrap is an additional infrastructure build under the
build-slot gate, necessary because this fresh worktree has no tool binary. It
does not add an application verification selection. Cite original assertion-red
witnesses from `C:\Antiphon\verification\task-089ef857\report.md` rather than pay
for another test-only application build. Original report records both new methods
failing intended assertions before the production repair.

## Verification design

V-1 PowerShell descendant belongs to the private job, exact generation is retained,
and raw packaged roots are refused before resume (PowerShellDescendantCustody).
V-2 retained external writer expires the independent drain budget, returns failed
output/cleanup facts and retains partial evidence (RetainedWriterDrainDeadline).
R-1 assignment-before-resume and assignment refusal (AssignBeforeResume).
R-2 descendant/root accounting and breakaway refusal (DescendantExit).
R-3 stdout/stderr/log ordering and production cleanup admission (DrainBeforeReturn).
R-4 null/supplied/empty/cleared environment semantics (four C1045 methods).
V-3 manual ASCII-only changed scripts, full-range evidence Git audit, clean
merge-tree against fresh origin/master, and unchanged master-only environment hunk.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-dc2b-native/` | native-ownership | `/*/*/NightlyNativeOwnershipTests/*` | V-1, V-2, R-1, R-2, R-3, R-4 | all 9 methods, 0 failed/skipped | 9 | 16 |

## Pending SourceLanding Mutation

Every control and variant stays pending; ordinary green does not discharge them.
PC-1 resolver bypass (V-1); PC-2 raw packaged-root admission bypass (V-1);
PC-3 removed drain deadline (V-2); PC-4 incorrect successful cleanup on drain
expiry (V-2). Mutation owns deliberate mutants, method-scoped red/restore/green,
and missing-control discovery. These controls have not been executed in this task.

Restart target: none. No activation, release or qualification credit is claimed.
