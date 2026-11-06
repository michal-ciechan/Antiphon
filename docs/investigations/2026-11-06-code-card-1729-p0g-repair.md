# CARD-1729 P0-G resolver repair: Code evidence

Both resolver blockers are repaired and the targeted ordinary verification is green.
Final whole Unit verification is pending in this committed checkpoint report.

- Task: a342b58a-f264-4674-925e-d60245dc9409. Original Code task / landing owner:
  742b9c5c-9e25-4fac-910e-799803e461c9. Round: Final; next Review is Claude round 2,
  diff-only from 93efbc748def106ce2c2ca15d56589142c46986b.
- Worktree: `/work/worktrees/task-a342b58a`; branch `feat/card-task-a342b58a`.
- Base: 93efbc748def106ce2c2ca15d56589142c46986b.
- Red witness commit: 320050108dcf1454d76bb99b8e1b2301e1519aeb (engine identical to base).
- Fix / targeted tested commit: 8c8cd4c5a8e8e4a7d6b61f4475a57ce53bdbfce4.
- Plan: `docs/superpowers/testdesign/2026-11-06-card-1729-p0g-resolver-repair-testdesign.md`.
- Red evidence: `.antiphon/checkpoints/20261106-024412-181a/`.
- Green evidence: `.antiphon/checkpoints/20261106-025149-7d0f/`.
- Restart: none. No production OS-selected behavior or startup path changed.

## Invariants and changes

B-1: initializer/with members, named arguments, property patterns and named
attribute arguments cannot become type-reference rows. The L6 witness evaluates
all five variants and reports every false finding.

B-2: value names cannot hide references in type positions or qualified names.
The L5 witness evaluates all eight property, parameter, local, creation,
qualified/global-qualified/expression and own-nested-type variants. Each requires
the exact foreign row and strict failure. Unqualified own types prefer their own
declaration rather than creating an ambiguity with a foreign same-named type.

The census now uses bounded git ls-files rather than directory enumeration (A-2).
One class-shared repository fixture supplies the map, allowlist and analysis to
L1..L4 (A-4), and the test class has the assembly-local process limiter. A grouped
declaration lookup replaces the per-file full declaration scan. No test method
was added/removed and no category, Slow registry, policy/hash or census pin changed.
No timeout or assertion was weakened.

Real inventory: `VIOLATIONS 0 LISTED 223 STALE 0 AMBIGUOUS 3`, 1,349 tracked files.
Setup: census 0.011s, file reads 0.105s, analysis 8.852s, total 8.968s.
L4 assertion body 0.0007291s; shared setup remains visible in startup and its phase log.
This is a setup-sharing change, not a claim that parsing itself now takes milliseconds.

## Allowlist merge instructions

Exactly 67 false rows deleted: 57 Kind, six Active, two Generation, two Status.
59 deletions are P0-C5; eight are later-phase. One row added under P0-C5:
`server/Application/Services/AgentTaskLandingProtocol.cs -> LandWorkspace @ server/Infrastructure/Git/LandWorkspace.cs`.
Total 289 - 67 + 1 = 223; P0-C5 goes 68 -> 10, later-phase 104 -> 96.

Deleted baseline line ranges in `tests/Antiphon.Tests/Layering/allowlist.txt`:
106-110, 116, 118-131, 134-171, 173, 216, 226-227, 231, 246, 282, 299, 301.
The new LandWorkspace row is line 111 at the fix commit. Exact patch is retained
at `.antiphon/a342b58a-allowlist.patch`; do not replace parallel slices' groups.

## Verification outcomes

Red CP-1: nine executed, seven passed, two failed, zero skipped. L6 failed on
all five intended false-row labels. L5 failed on all eight intended missing-row
labels (each incorrectly passed strict at the base engine). These are assertion
failures, not build/fixture/discovery errors. Red L4 body: 15.956s at host load.

Green CP-1: nine executed/passed, zero failed/skipped. All frozen method names
were inspected in the fresh TRX, with the new variants inside L5/L6.
CP-91: 50 executed/passed, zero failed/skipped: TestClassificationPolicyTests 39,
TestClassificationGuardTests one, SlowTestRegistryMergeTests five,
SlowTestTripwireTests two, NativeLinuxPolicyWitnessTests three.
CP-92: MechanicalPcContractTests 11 executed/passed, zero failed/skipped.
Full row/method rosters are retained as ignored `.antiphon/a342b58a-CP-*-roster.json`.

V-1 and R-1 PASS (L1..L9, all new variants, exact real inventory).
V-2 synthetic strict behavior PASS; real-tree strict activation remains with
P0-G2 and is not claimed passed. V-3..V-12 / R-2..R-8 belong to other slices.
Repair-brief parity: added methods=0, removed=0, nine names retained; all variants
are in-body. No additional discovery run was required by that narrowed brief.

Every retained PC-1..PC-12 and every variant remains pending SourceLanding Mutation;
PC-11/12 still require P0-G2. New PC-36 (revert member-label filtering, five L6
variants) and PC-37 (restore unconditional name exclusion, eight L5 variants)
are pending there too. Mutation owns deliberate mutants, red/restore/green and
missing-control discovery. Phase-wide PC-13..PC-35 remain with their slice owners.

A-1 multi-glob dead-pattern checks, A-3 syntax-resolution limitations and A-5
warn-mode ratchet activation remain advisory/design notes, not repaired claims.

## Cost and provenance

Declared unlisted run: required native baseline incremental compile before edits,
UseAppHost=false, TEMP=/tmp, native output; zero errors, 1,728 existing warnings,
179.56s compiler time. Slot granted, waited=195s, held=181s. It also built the
checkpoint tool through the test project's existing graph; no separate tool build.

Checkpoint red group: 359.290s wall, build phase 289.440s, test driver 26.372s.
Targeted green group: 420.235s wall, shared build phase 259.565s, CP-1 16.791s,
CP-91 85.820s, CP-92 11.571s. Admissions reported waited=0s; original tool budget
records retain the small admission-only unions. Build and host/body time are
separate. External timing not recorded by existing tests is unknown, not zero.
The invalid initial manifest output/id mismatch launched no build/test; corrected
before starting the red group. There were no stability repetitions or deliberate mutants.

check-evidence-diff at fix: two commits, zero evidence entries/violations.
OS-witness lint: complete test-only inventory; no native execution obligation or
rc startup impact. Generated declaration: `.antiphon/a342b58a-os-witnesses.json`.
Fetched origin/master 1a22e0d2ca7b32e7f6ac346a658e1c7824252a46; merge-tree exited
zero, tree 404c6f10b83122b3cda2c1ed1f88392d831b0bc9. No merge/rebase/landing occurred.

## Unedited checkpoint receipts

