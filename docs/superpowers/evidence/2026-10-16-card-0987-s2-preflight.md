# CARD-0987 S2 Code preflight: blocked on extraction closure

S2 implementation did not start. Two production dependencies outside its frozen
file list require a scoped Plan/TestDesign refresh before the move. These are
inherited planning gaps, not observed failures of the unchanged landed source.
No build, test, deliberate mutant, cleanup, deployment or restart ran.

Original Code task / landing owner: `288c1f6e-5bd3-4949-9022-b85d29ee1609`.
Branch: `feat/card-task-288c1f6e`.
Worktree: `/work/worktrees/task-288c1f6e`.
Assigned base and fetched `origin/master`: `69031cfa11244dc988c8d79d3f516bc627dd3824`.
This task changes only this preflight evidence; it does not qualify an S2 landing.

Authority:

- [Custody refresh plan](../plans/2026-10-15-card-0987-custody-refresh-plan.md),
  D-3 and S2 scope: move the enumerated shared closure, preserve single public type
  identity and keep the library independent of the checkpoint executable.
- [Frozen TestDesign](../plans/2026-10-15-card-0987-test-design.md), lines 47-52:
  "any additional production dependency returns to Plan."
- The task brief permits only the next slice and requires stopping rather than
  guessing when a refresh is needed. The next slice is S2, after the landed
  S1a0/S1a1/S1a/S1b and S1b D-1 repair. S3 and later work remain untouched.

## Blocking dependencies

### B-1: the moved delete policy still calls the executable

`tools/Antiphon.Checkpoints/Cleanup/SafeDelete.cs:157,160` calls
`OutputCleanup.IsIsolatedOutputId`. That public facade and its actual naming
policy are in `tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs:21-35,246-251`.
OutputCleanup.cs is in S1a's scope, but absent from S2's enumerated move/split scope.
Its `RecordCreatedOutputs` method at line 143 also depends on tool-owned RunState;
moving the entire file would expand the closure again. A library reference to the
executable would violate D-3 and create the reverse of the existing project edge.

Required refresh: name a shared owner for the isolated-output naming policy and
authorize the OutputCleanup forwarding change while keeping RunState integration
in the tool. Preserve the existing predicate exactly. Copying a second policy
implementation or weakening the delete predicate is not the proposed repair.
This is source dependency evidence; no intentionally broken build was performed.

### B-2: the PowerShell driver loads a source file that S2 moves

`scripts/lib/checkpoint-owned-output.ps1:9-13` reads and Add-Type compiles
`tools/Antiphon.Checkpoints/Cleanup/ExclusiveDirectory.cs`. The script is absent
from the frozen S2 source scope. `scripts/run-checkpoint.ps1` dot-sources it at
line 64 and calls New-CheckpointFreshOutput at lines 559 and 991 for fresh result
and build output directories. A physical move without preserving this consumer
would break the checkpoint driver used to verify that very move.

The [S1a evidence](2026-10-15-card-0987-s1a-code.md) explicitly retains this source
binding until a separately scoped consumer/extraction change. CARD-1302 item 4,
read live, records the separate native mkdir implementation and drift concern.
The [project-reference repair](../plans/2026-10-15-card-0987-project-reference-contract-repair.md)
also records the standalone Add-Type/create/collision contract.

Required refresh: explicitly preserve the standalone source-loading contract,
preferably by authorizing the helper's source-path rebind alongside the move.
Keep the independently compilable native source separate from context-dependent
overloads. If a retained source/link facade is preferred, specify that exception
to the physical move and its single-type compilation contract. Do not introduce
a new assembly-loading mechanism or duplicate custody type implicitly.

TestDesign must bind the offline native create/collision smoke and the affected
script classes. `RunCheckpointScriptTests` has 24 current test methods, including
`C585_Green`, `C585_FreshResultsDir`, `C585_NoBuild` and `C589_SlotGranted`.
This census is not a passing run or an amended executable checkpoint selection.
Keep the landed four-variant `HostCleanupCheckpointSweepRaceTests` regression
(CP-30 in the S1b D-1 repair plan) when the sweep moves. Review the remaining
script consumers before freezing the additional row(s).

## CARD-1307 disposition

- A1: still pending. ToolCopyAuthority.Remove unlinks individual entries, while
  Matches reads using the supplied cancellation token. Cancellation between
  entries can leave a partial tool. The requested test and clear partial-removal
  marker/all-or-nothing decision were not implemented. Bind this to refreshed S2
  scope; do not infer transactional deletion from ordinary filesystem calls.
- A2: still pending. Named cancellation exit mapping belongs to Program.cs,
  which the frozen S2 scope does not include. Explicitly assign a later scope or
  authorize it in the refresh; no exit code was invented here.
- A3: the reported process-wide file-only lookup is not present at this base.
  `CheckpointDelete` has `private CheckpointWrite SafeWrite => new(context)` at
  SafeDelete.cs:59; its FileOnly branch at line 90 resolves that instance property.
  Git blame attributes the property to landed S1a0 commit 367c8ee4cd. This is a
  source finding, not a new runtime proof or card closure. Keep a positive
  context-isolation witness for the file-only path in the refreshed design.
- A4: use the full explicit base SHA for evidence-diff, as required by CARD-1290.

## Verification and pending work

The specific brief and frozen design exclude the whole Unit lane. No broad run
was attempted. No checkpoints were started because implementation stopped at the
required dependency gate. There are no fresh TRX, tested SHA, build provenance or
CHECKPOINT lines from this task. Builds=0; tests executed=0; passed=0; failed=0;
skipped=0; slot=not-requested; waited=0s. These zero counts do not mean green.

| Ordinary obligation | Actual outcome in this task |
|---|---|
| R-1a, R-1b / full R-1 | Not run: CP-29 pre-move 54 and CP-2 post-move 54 remain required. |
| R-6 | Not run: CP-25 (65) and CP-27 (32), plus the refreshed affected-reader selection, remain required. |
| V-14, V-15, V-16 | Not run: CP-28 (12 + 8 + 16 = 36) remains required. |
| V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, V-11, V-12, V-13 | Not run; later slices, no pass claimed. |
| R-2, R-3, R-4, R-5 | Not run; later slices, no pass claimed. |
| Manual acceptance | Dependency/source-scope audit complete; standalone smoke, linked-host build and implementation acceptance not run. |

All unrun S2 obligations above are deferred to the next commissioned Final;
this is not an Interim pass. Historical 14/16 floors retain their original
evidence. The 54/80 floors remain unclaimed. Frozen S2 ordinary estimate remains
57 minutes before authoring, red-first reruns and any newly required rows; no
runtime cost or saving was measured here. No output trees were created.

Every PC-1 through PC-413 and every variant in the frozen ledger remains pending
SourceLanding Mutation, along with proposed PC-414's four variants in
[the D-1 candidate](../mutations/2026-10-16-card-0987-s1b-d1-candidate.md).
Existing S1a0/S1a1/S1a/S1b candidates under `docs/superpowers/mutations/` retain
their original bindings. No S2 mutation binding or deliberate control cycle was
created or discharged. Mutation still owns missing-control discovery.

GET `/api/runner-defaults` and GET `/api/session-runners` both succeeded:
defaults revision 2, eligible Linux and Windows lanes. No host or platform pin
was supplied. Git fetch confirmed all prerequisites at the assigned base; no
merge, rebase or branch switch occurred. Final full-history evidence-diff,
merge-tree and exact remote-ref results are recorded in
`/work/worktrees/task-288c1f6e/.antiphon/task-288c1f6e.md` after this report commit.

Caller action: authorize the narrow S2 closure refresh above, commission Plan
then TestDesign, and recommission Code from their landed artifacts. No later
slice should start to bypass the dependency gate. Required restart: **none**.
