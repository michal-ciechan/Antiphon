# CARD-1024: cleanup blocked by the inherited deletion validator

Code task and original landing owner: `aae956f6-a895-4bee-aeac-01fd3e48abb3`.
Branch: `feat/card-task-aae956f6`.
Worktree: `/work/worktrees/task-aae956f6`.
Immutable admission base B: `70d7e26128af77d1440d873f297c66af5567fb08`;
the local `origin/master` also named B during admission.
Reference plan: [CARD-1015](../superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md).

No evidence was deleted. The required existing deletion validator fails on the
untouched base, before a CARD-1024 deletion can be staged. This task changes only
this note. Cleanup and ordinary verification remain incomplete.

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
