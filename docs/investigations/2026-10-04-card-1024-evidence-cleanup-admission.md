# CARD-1024: evidence cleanup admission and supplemental validator

Code task and original landing owner: `aae956f6-a895-4bee-aeac-01fd3e48abb3`.
Branch: `feat/card-task-aae956f6`.
Worktree: `/work/worktrees/task-aae956f6`.
Immutable admission base B: `70d7e26128af77d1440d873f297c66af5567fb08`;
the local `origin/master` also named B during admission.
Reference plan: [CARD-1015](../superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md).

Initial admission record (before the caller's repair authorization): no evidence
was deleted. The required existing deletion validator fails on the
untouched base, before a CARD-1024 deletion can be staged. This task changes only
this note. The continuation below supersedes that initial blocked handoff.

## Admission inventory

The complete committed tree was classified with `Read-EvidenceTree` and
`Get-EvidenceViolation` from `scripts/lib/evidence-policy.ps1`. An independently
expressed predicate over the same Git metadata agreed on the ordered rejected
set, count, bytes and NUL-delimited path SHA-256. No payloads, credentials or
private notes were read to select files.

S contains **238 paths / 19,693,603 bytes**, path SHA-256
`e4dd19d446ddd7b54de8512ada829a0da4e0b30f518ba67c1d313dad5be6d2ff`.
Hash input is each original UTF-8 path in Git tree order followed by NUL.

| Rejected family | Paths | Bytes |
|---|---:|---:|
| `.antiphon/c1008-evidence/` | 25 | 249,711 |
| `.antiphon/c1008-evidence-round2/` | 28 | 1,770,723 |
| `.antiphon/c1008-evidence-round3/` | 55 | 4,199,030 |
| `.antiphon/c1008-evidence-round4/` | 74 | 4,170,034 |
| `.antiphon/c1008-evidence-round5/` | 48 | 8,946,761 |
| `.antiphon/checkpoints/` | 8 | 357,344 |
| **Total** | **238** | **19,693,603** |

Rule reasons: `non_markdown` = 230 paths / 19,336,259 bytes;
`checkpoint_directory` = 8 paths / 357,344 bytes. The latter includes three
Markdown files, rejected because their directories contain `checkpoints`.
The two selected checkpoint runs are `20261003-211642-32ac` and
`20261003-224121-100a`. No ambiguous modes/families or rejected Markdown under
`docs/superpowers/plans/` or `docs/investigations/` were found.

Keep set: **46 permitted root-evidence Markdown paths / 797,865 bytes**, path
SHA-256 `9ed1e1578594bac0bf2548145113d390f5fef07371e3dd70087bf352ca4e1399`.
It contains 25 task reports, six c998 summaries and 15 c1008 summaries. All remain
unchanged; there is no removal or edit of any existing tracked path.

`git log --diff-filter=A B -- .antiphon` was intersected with all 238 selected
paths; every path had an introduction record. **Non-CARD-1008 rejected paths:
zero.** This is a complete policy census, not a card-name selection.

| Introducing commit | Selected paths |
|---|---:|
| `0049877688f895bc4fb75e49cfe8911489b2a937` | 4 |
| `18d64696354feeb10dcb1d7e90acab7c49762cc7` | 28 |
| `31e92d8e810ce6572300ed4331684caa59349551` | 21 |
| `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | 26 |
| `447f1d0f81cde0b10ca6d519f728bd3e967e4aae` | 2 |
| `5609cb93ba5109d19044ad7da5bf2c96363c5132` | 33 |
| `6eb387c1527fed85535125b3199bc3c5d79fd68f` | 26 |
| `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | 12 |
| `94c1f5eca2addfade3518ee605c4013f7276b393` | 2 |
| `97445c89878addf92b2ec457fbf369db495a53f1` | 13 |
| `c43bf296113e715c1087c7eae63782b7e229b73a` | 2 |
| `c696fc9fab53b1cce5205b12e5c718b908477809` | 1 |
| `cf9881dd143804e114d7d68c54af10bd156ab704` | 23 |
| `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | 26 |
| `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | 14 |
| `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | 5 |

## Reference checks

Read-only `rg -n -F` searches covered `src/`, `tests/`, `tools/`, `scripts/`,
`server/`, `client/`, `docs/` and `.github/`. All six family prefixes were searched;
the general checkpoint prefix has ordinary output-directory and historical
documentation references. Refining to the two selected run prefixes found zero
matches. Searching every exact rejected path, with both slash conventions, also
found zero matches. The five c1008 family prefixes have zero matches throughout
these roots. Thus no selected artifact dependency was identified.

The unchanged census literal is `selected = 377` in
`scripts/lib/checkpoint-usage.ps1:114`. The unchanged
`CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`
reads the usage library and compiled roster, not these evidence files.
`EvidenceDeletionGuardTests` borrows the original anchor's Git objects, which
ordinary deletion does not remove. `GrokLinuxBlockingPromptTests` reads its
checked-in fixture copies; its investigation path is provenance. No cheap
runtime check loading a selected artifact was identified. These are source and
reference observations, not executed TUnit results.

## Reproduced blocker

This read-only admission command ran once at clean B and exited **1**:

```sh
pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -InventoryRef 70d7e26128af77d1440d873f297c66af5567fb08 -InventoryOnly
```

It emitted 88 copies of this unedited diagnostic:

```text
EVIDENCE deletion violation reason=anchor_present
```

The inventory JSON reports `AnchorValid: false`; its original-anchor signature
is the expected `f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b`.
The missing entries are exactly the 88 rejected original-anchor paths, already
removed by ancestor commit `37ddd0501605d7117736436fa6cd794f572ce02a`
(`chore(CARD-1015): delete exactly 289 rule-rejected evidence paths; history retained`).
This is inherited behavior, reproduced without any source or index changes.

`scripts/lib/evidence-policy.ps1:274` unconditionally requires every original
anchor entry at InventoryRef in both modes. A later CARD-1024 deletion cannot
repair that pre-existing failure. Line 297 additionally recognizes only the exact
trailer `Antiphon-Evidence-Deletion: CARD-1015`. CP-7 is therefore specialized to
the first cleanup; it cannot give the requested green proof for this later base.
Changing B to the older CARD-1015 base would select the wrong inventory, and
restoring the 88 generated artifacts would violate the cleanup scope. Neither
workaround was attempted. No assertion, timeout or guard was loosened.

## Remaining verification and handoff

No builds, TUnit tests or deliberate mutants ran. `slot=notRequested`,
`waited=0s`; no CHECKPOINT lines, TRX or build-provenance receipts exist for this
task, and no alternate output directories were created.

- CP-2 (18 history methods; V-1 through V-18, R-2/R-3/R-4) and CP-3 (six deletion
  methods; V-21/R-5): not run; deletion admission is blocked.
- CP-4/CP-5 (V-22/R-2 adjacent smokes): not run.
- CP-6 (V-23): the full B..pushed-HEAD history guard must be recorded in the stored
  final report after this note is committed. A clean note-only range does not
  establish cleanup.
- CP-7 (V-24/R-5): not run against a deletion; the required InventoryOnly
  prerequisite failed as reproduced above. Final-tree cleanup and post-deletion
  recoverability are not claimed.
- V-19, V-20, V-25, R-1 and R-7: the old plan's bundle/workflow/whole-Unit work is
  outside this mechanical cleanup brief; not executed or claimed passed.
- R-6: census source unchanged at 377; compiled census execution not run.
- No ordinary IDs are claimed deferred-to-final passes: this is an incomplete
  Final round. No new PC is designed by this cleanup; inherited PC-1 through
  PC-101 and every variant remain pending their owning SourceLanding Mutation.

Caller action: commission a scoped Code repair/design for a repeat-cleanup
validation entry or mode with exact immutable inventory and deletion identity,
preserving CARD-1015's original anchored contract and tests. Then resume the
single exact-S deletion and its ordinary proof; review follows completion.
Do not reinterpret the original 88-file anchor failure as successful admission.

Runner defaults and session-runner catalogue were read through the required GETs.
No host pin, restart or activation was requested. **Restart: none; owner: caller**
(no operational action needed for this documentation/evidence-only task).

Generated admission metadata remains outside tracked source at
`/tmp/card1024-aae956f6-inventory.txt` and
`/tmp/card1024-aae956f6-admission.txt`; the essential findings are retained above.

## Authorized continuation

Caller input `371f8c2c-3f80-4acb-9e3f-557725d7f00e` authorized a minimal opt-in
supplemental validator, its own repair commit before the exact deletion commit,
and focused new success/refusal tests. The existing six deletion and 18 history
tests are unchanged. No test is added to `Antiphon.Tests.Checkpoints`; the
independent census stays 377.

`-SupplementalCleanup CARD-1024` selects a later inventory and the exact trailer
`Antiphon-Evidence-Deletion: CARD-1024`. Omission retains the original anchor,
messages, exit codes and CARD-1015 trailer. Empty/malformed labels and CARD-1015
as a supplemental label fail with exit 2. Supplemental inventory JSON identifies
its card explicitly and does not assert an original-anchor verdict.

The supplemental branch skips only original-anchor loading/validation. Both
modes share the existing immutable inventory, ancestry, unique single-parent
deletion, complete diff, old identity, kept Markdown, final-tree and recoverable
blob checks. Neither a hand-picked path list nor a candidate-derived expected set
is accepted. S remains exactly the recorded 238 paths at B above.

S1 is the validator, a new `EvidenceSupplementalDeletionGuardTests` integration
class and documentation, committed with verification pending. S2 is exactly S,
deleted in a separate ordinary commit bearing the CARD-1024 trailer, count and
digest. Freeze source after S2, then run this closed checkpoint list. Final
receipt facts belong in the ignored stored report to avoid changing tested HEAD.

### Verification design

The new class uses `EvidenceGitFixture` unchanged, real owned Git/PowerShell
children, the independent C# classification oracle and the process-spawn limiter.
Its positive test runs both after a real anchored first cleanup and in an owned
repository without any original anchor objects. The default call must still
reject the later anchored base. Exact expected paths, inventory aggregates,
complete final tree, read-only snapshots and recoverable objects are asserted.
Negative exact-set fixtures restore an extra deletion or finish a missing
deletion in an unmarked later commit, isolating the marked-commit guard from
final-tree/preservation failures. The recovery fixture deletes only its uniquely
owned loose blob after inventory/final-tree enumeration; a hit assertion and
native `cat-file` failure distinguish the intended refusal from fixture failure.

All new methods below are single results, with internal variants as specified.
Every old V/R covered by CP-2 through CP-7 retains its original meaning. V-19,
V-20, V-25 and whole-Unit R-7 are excluded by the mechanical-cleanup brief; no
bundle/workflow source or general runtime behavior changes. R-6 gets a separate
compiled census smoke. There is no asynchronous delivery or activation change.

| IDs | New exact method in EvidenceSupplementalDeletionGuardTests | Decisive observation |
|---|---|---|
| V-26 / R-8 | Verifies_later_cleanup_without_original_anchor_entries | Default anchored admission still fails; explicit later cleanup succeeds with independent inventory, unchanged keeps, recoverable blobs and read-only snapshots. |
| V-27 / R-9 | Rejects_extra_deletion | Extra kept Markdown or outside source, restored later: exit 1 with only deletion_extra. |
| V-28 / R-10 | Rejects_missing_deletion | Omitted selected path removed later: exit 1 with only deletion_missing. |
| V-29 / R-11 | Rejects_changed_kept_markdown | Content, missing path and mode variants each fail their sole kept-path reason. |
| V-30 / R-12 | Refuses_nonrecoverable_deleted_blob | One post-enumeration fault hit; missing owned blob produces exit 2 / git_result_cat-file. |
| V-31 / R-13 | Rejects_wrong_inventory_or_cleanup_label | Wrong count/digest/bytes fail independently; empty, whitespace, lowercase, original-card and injected labels fail both parameter sets with exit 2. |
| V-32 / R-14 | Rejects_wrong_ambiguous_or_merge_marker | Missing, other-card, original-card, body-only, duplicate and merge markers fail exact marker/parent checks. |
| V-33 / R-15 | Rejects_non_deletion_changes | Modified or added source in the marked commit fails deletion_only. |
| V-34 / R-16 | Rejects_changed_inventory_entries | Same-size changed blob or mode between B and deletion fails pinned old identity. |
| V-35 / R-17 | Rejects_resurrection_and_new_final_artifacts | Restored small Markdown isolates resurrection; a new forbidden final path isolates final-tree policy. |

### Checkpoints

Use the checkpoint tool with this note as the plan, after S1 and S2 are committed,
and the full committed HEAD as `--expected-source-sha`. One isolated build,
serial rows, no repeats. The three complete guard classes are the affected
integration scope. CP-4/CP-5 are the inherited adjacent smokes; CP-9 is the cheap
compiled census check. Total: **37 results**, estimated **14 minutes** including
the read-only commands, plus a separately disclosed slot-gated tool bootstrap.
No whole assembly or whole Unit run is required by this brief.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1024/` | history | `/*/*/EvidenceDiffGuardTests/*` | V-1..V-18, R-2, R-3, R-4 | all 18 original methods; zero failed/skipped | 18 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c1024-disabled;TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-3 | S1-S2 | CP-2 | original-deletion | `/*/*/EvidenceDeletionGuardTests/*` | V-21, R-5 | all six original unchanged methods; zero failed/skipped | 6 | 3 | true | `C804_ORPHAN_SWEEP_ROOT=c1024-disabled;TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-8 | S1-S2 | CP-2 | supplemental-deletion | `/*/*/EvidenceSupplementalDeletionGuardTests/*` | V-26..V-35, R-8..R-17 | all ten new methods; zero failed/skipped | 10 | 2 | true | `C804_ORPHAN_SWEEP_ROOT=c1024-disabled;TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-4 | S1-S2 | CP-2 | land-ignored-smoke | `/*/*/LandingGitTests/C642_IdentityAndStatusScopeSkipsIgnoredListing` | V-22, R-2 | exact method; zero failed/skipped | 1 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c1024-disabled` |
| CP-5 | S1-S2 | CP-2 | source-ignored-smoke | `/*/*/CheckpointSourceStateTests/clean_and_ignored_outputs_match_head` | V-22, R-2 | exact method; zero failed/skipped | 1 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c1024-disabled` |
| CP-9 | S1-S2 | CP-2 | census-smoke | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | R-6 | exact method; census 377; zero failed/skipped | 1 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c1024-disabled` |
| CP-6 | S1-S2 | n/a | task-history | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 70d7e26128af77d1440d873f297c66af5567fb08 -HeadRef HEAD` | V-23 | exit 0; full task range; zero violations | n/a | 1 | true | n/a |
| CP-7 | S1-S2 | n/a | actual-deletion | `pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -SupplementalCleanup CARD-1024 -InventoryRef 70d7e26128af77d1440d873f297c66af5567fb08 -InventoryPathSha256 e4dd19d446ddd7b54de8512ada829a0da4e0b30f518ba67c1d313dad5be6d2ff -InventoryCount 238 -InventoryBytes 19693603 -HeadRef HEAD` | V-24, R-5 | exit 0; exact 238 deleted; 46 kept; 238 recoverable; zero final-tree violations | n/a | 1 | true | n/a |

### Pending Mutation controls

No deliberate source mutant is run in Code. Inherited PC-1 through PC-101 remain
owned by their CARD-1015 Mutation plan. Additional method-scoped obligations are
listed below; every ID and variant is pending post-land SourceLanding Mutation.
The ordinary negative fixtures above are executable assertions, not completed
source-mutation cycles. Mutation owns missing-control discovery as well.

| PC | Targeted defect / variants | Exact method above |
|---|---|---|
| PC-102 | Default call bypasses original anchor presence | Verifies_later_cleanup_without_original_anchor_entries |
| PC-103 | Supplemental call still loads/requires original anchor | Verifies_later_cleanup_without_original_anchor_entries |
| PC-104 | Supplemental marker remains hard-coded CARD-1015 | Verifies_later_cleanup_without_original_anchor_entries |
| PC-105 | Supplemental inventory falsely emits AnchorValid | Verifies_later_cleanup_without_original_anchor_entries |
| PC-106 | Accept invalid label (empty/whitespace/lowercase/original-card/injected) in inventory or validation | Rejects_wrong_inventory_or_cleanup_label |
| PC-107 | Bypass inventory count/digest/bytes check separately | Rejects_wrong_inventory_or_cleanup_label |
| PC-108 | Bypass extra-set check (kept Markdown/outside source) | Rejects_extra_deletion |
| PC-109 | Bypass missing-set check | Rejects_missing_deletion |
| PC-110 | Bypass kept OID/presence/mode separately | Rejects_changed_kept_markdown |
| PC-111 | Skip recoverability loop or ignore cat-file failure | Refuses_nonrecoverable_deleted_blob |
| PC-112 | Accept missing/other-card/original-card/body-only/duplicate/merge marker separately | Rejects_wrong_ambiguous_or_merge_marker |
| PC-113 | Admit added/modified source in deletion commit | Rejects_non_deletion_changes |
| PC-114 | Bypass old OID/mode comparison separately | Rejects_changed_inventory_entries |
| PC-115 | Bypass resurrection/final-tree scan separately | Rejects_resurrection_and_new_final_artifacts |

After ordinary verification: next Review of original Code owner aae956f6, then
caller landing and SourceLanding Mutation. Restart remains none; owner caller.
