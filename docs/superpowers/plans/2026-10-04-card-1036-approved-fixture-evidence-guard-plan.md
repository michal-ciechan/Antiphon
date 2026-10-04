# CARD-1036: approved snapshot fixtures in the evidence guard

Date: 2026-10-04. Complexity: **easy**; TestDesign is folded into this plan.
Inspected source: `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`.
Code budget: **40 minutes estimated**, within the operator's 30-60 minute window.

## Outcome and scope

Permit explicitly named approved JSON snapshots beneath root `.antiphon/fixtures/`
without admitting ordinary run output. The exception changes the shared path
classifier, so history admission and deletion inventories agree. Preserve CARD-1015's
historical anchor, committed-object reads, complete-history range, and fail-closed
error handling. Update the Evidence Git policy in `docs/testing-and-build.md`.

This card does not clean up evidence, change CARD-1022's base or release scope,
alter landing, add a configuration service, or move existing artifacts. Ordinary
fixtures beneath `tests/` are already outside this guard's scope.

## Ground truth

| Card assumption | Inspected code / observed behavior | Design consequence |
|---|---|---|
| A snapshot beneath `.antiphon/fixtures/` is rejected. | `Get-EvidenceViolation` in `scripts/lib/evidence-policy.ps1` accepts only `.md` after root/directory/mode checks. A direct probe of `.antiphon/fixtures/c1022-guard-probe.approved.json`, regular mode and 48 bytes, returns `non_markdown`. | Add one narrowly named fixture exception at the format check. |
| The exception must not admit run output. | `source.json`, `run.trx`, and `compiler.log` under that same fixture directory currently return `non_markdown`; checkpoint Markdown returns `checkpoint_directory`. | Fixture directory membership alone is insufficient; retain explicit filename/type admission and all other checks. |
| The fix affects only the history CLI. | `Invoke-EvidenceHistory`, `Get-EvidencePathHash -Classification`, and `Invoke-EvidenceDeletion` share the classifier. Deletion inventories split into Delete/Keep and the final-tree check also calls it. | Verify both deletion modes preserve approved fixtures, as well as history acceptance. |
| CARD-1015's anchor must stay fixed. | Anchor `bb5fa774cd56f85ee6f0b1122c198192427e5ddf` contains 108 scoped entries and **zero** entries under `.antiphon/fixtures/`. Direct read reproduced the pinned hashes below. | The new namespace can be admitted without changing an anchor constant or historical classification. |
| Existing fixtures can prove the behavior cheaply. | `EvidenceGitFixture` creates real local Git objects, executes the actual PowerShell CLI, bounds and joins child processes, and owns its temporary root. `Reject` is an independent C# inventory oracle with the current Markdown-only rule. | Reuse the harness; update its independent oracle and assert literal expected fixture paths so a shared mistaken oracle cannot make tests green. |
| Generated fixtures become automatically tracked. | `.gitignore` ignores `.antiphon/` in full. The guard checks committed Git objects, not ignore rules or working files. | Keep ignore defaults; add only individual reviewed fixture files deliberately. No wildcard force-add or directory unignore. |
| This fixes the historical 290-record CARD-1022 failure. | Investigation at commit `353e374f`, `docs/investigations/2026-10-04-card-1022-evidence-guard.md`, identifies imported run output and a synthetic snapshot probe, not 290 approved fixtures. | No retroactive exemption for those output roots and no history-range shortcut. |

Pinned anchor path hash:
`356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c`.
Pinned classification hash:
`f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b`.
Existing anchor test additionally fixes 88 Delete / 20 Keep entries and their bytes.

## Decisions

- **D-1 — Use one named, anchored path-pattern allowlist entry.** Name it
  `approved-json-fixture`; match case-sensitively against the complete Git path:
  `\A\.antiphon/fixtures/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.approved\.json\z`.
  This accepts the card's probe and nested named fixture groups. The root,
  `fixtures`, and `.approved.json` spelling is exact; name/group characters are
  ASCII letters, digits, underscore, and hyphen. Full-string anchors reject
  trailing newline/suffix tricks. This is the chosen initial contract, not an
  unresolved operator default. Reject a blanket fixtures-directory exemption,
  unanchored substring matching, and a repository-wide JSON exemption because
  they admit receipts. Reject a separately configurable manifest for this small
  fix because it adds parsing/versioning and a second policy input without a
  demonstrated consumer. Other snapshot formats need a subsequent explicit rule.
- **D-2 — Broaden only the permitted format predicate.** Keep the order root,
  checkpoint directories, regular mode, permitted format, byte limit. At the
  format check accept Markdown OR the approved fixture pattern. Do not return
  success early for a fixture. Preserve regular modes `100644`/`100755`, the
  1,048,576-byte inclusive cap, and case-insensitive `checkpoints` containment in
  every directory component. Existing diagnostic reasons and exit codes remain.
- **D-3 — Approval is a source-review convention.** A matching path identifies an
  intentionally retained expected input/result, consumed by a named regression
  test; the Code/Review description for a real fixture identifies its consumer.
  A producer's `source.json`, receipt, TRX, log, compiler log, archive, or actual
  result file does not become a fixture by being moved or renamed. Do not parse
  JSON content to guess provenance: this guard cannot distinguish a deliberately
  disguised receipt from a golden JSON value. Document this precise limit instead
  of claiming semantic approval enforcement. No new human approval flow is added.
- **D-4 — Share policy, preserve history.** Implement the exception in
  `Get-EvidenceViolation` via a small named path predicate. Do not fork history
  and deletion rules or change the anchor/signature constants, cleanup markers,
  workflows, range selection, OID checks, or runtime-owned reports policy.
  Keep `.gitignore` intact; if adding a real approved fixture later, stage that
  exact reviewed file individually, never an evidence directory. This card's
  regression inputs are synthesized in temporary Git repositories.
- **D-5 — Use the portable Git/PowerShell integration lane.** Live
  `GET /api/runner-defaults` and `GET /api/session-runners` were read on 2026-10-04
  (defaults revision 2; an eligible Linux default and an eligible Windows runner
  were available). Placement remains runtime-owned: omit `-Runner` and
  `-Platform`; no OS-specific behavior is needed. Use `-Platform Any` only to
  clear an inherited pin. Refresh those routes at dispatch, not a stored host
  address or fleet location. Every checkpoint below names its lane in Group.
- **D-6 — One coherent Code slice; separate Review and Mutation.** Commit/push
  S1 before its checkpoint run. Code executes ordinary V/R only; the seven
  method-scoped PCs run after land in the normal SourceLanding Mutation stage.
  No whole-Unit, whole-assembly, namespace, browser, or live-runner test run.

## Implementation and slices

### S1 — classifier, independent fixtures, and policy documentation

Files:

- `scripts/lib/evidence-policy.ps1`: introduce
  `Test-EvidenceApprovedFixturePath` for D-1; call it only in the existing
  non-Markdown rejection condition. Keep all other production branches intact.
- `tests/Antiphon.Tests/Scripts/EvidenceApprovedFixtureGuardTests.cs` (new):
  the seven methods below, `[Category("Integration")]` and
  `[ParallelLimiter<ProcessSpawnLimit>]`, reusing `EvidenceGitFixture`.
- `tests/Antiphon.Tests/Scripts/EvidenceGitFixture.cs`: extend independent
  `Reject` classification with an ordinal component/suffix check for the selected
  convention. Do not call PowerShell or read its regex to derive expected values.
  Keep process custody and object-only setup unchanged.
- `docs/testing-and-build.md`, Evidence Git policy: describe the exact exception,
  the retained restrictions, fixture consumer/review convention, individual-file
  staging, and continued exclusion of run output. No blanket wording that
  generated files are always prohibited when they are approved fixtures.

Existing regression files selected for execution but not requiring edits:
`EvidenceDiffGuardTests.cs`, `EvidenceDeletionGuardTests.cs`, and
`EvidenceSupplementalDeletionGuardTests.cs` in `tests/Antiphon.Tests/Scripts/`.
No fixture payload, receipt, log, TRX, or temporary repository is committed.

Commit S1 with verification pending, push, then run CP-1 through CP-4 against
that exact SHA. If a failure requires a fix, commit/push the fix before rerunning
the affected row; do not edit source during a run. Final evidence admission covers
the Code task's original recorded base through its last pushed commit.

## Verification design

### Inspection

Read production `Get-EvidenceViolation`, `Test-EvidenceRoot`, history iteration,
tree/hash readers, and complete deletion validation; both CLI wrappers and the
Actions workflow; `EvidenceGitFixture` setup, Git plumbing, process runner,
inventory oracle, and disposal; the selected history and deletion test bodies.
The existing workflow calls the real guard and propagates its exit code; no
workflow change is needed. The inspection and six direct classifier probes
confirm the premise but are not executed Code checkpoints.

Boundaries: path/type admission -> V-1..V-3; checkpoint/mode/size precedence ->
V-4..V-6; shared deletion classification -> V-7; historical traversal and pinned
objects -> R-1; original anchor and later cleanup -> R-2/R-3.

### Delivery inventory

Not applicable: no async delivery path, queue, session, or runtime notification is
added or changed. The observable result is the real synchronous CLI exit plus its
structured diagnostics/inventory, tied to explicit Git base/head OIDs.

### Proves it works now

All seven methods are on `EvidenceApprovedFixtureGuardTests`. Each is one TUnit
execution; internal data loops do not increase checkpoint Min. Fixture setup
creates real commits with `AddCommitAsync`/`SetAsync`, then invokes the actual CLI.
Do not replace outcome assertions with regex/source-text assertions.

| ID | Exact method | Inputs and decisive assertions |
|---|---|---|
| V-1 | `Allows_approved_json_fixtures` | `.antiphon/fixtures/c1022-guard-probe.approved.json` and `.antiphon/fixtures/protocol_v1/Ready-2.approved.json`, both regular modes. Each history CLI returns 0 with one inspected entry and zero violations. Include modifying an existing approved fixture, proving the new blob is allowed. Label `c1036-approved-accepted`. |
| V-2 | `Rejects_fixture_path_near_misses` | Under the scoped root: `fixtures-other/x.approved.json`, `x/fixtures/x.approved.json`, `reports/x.approved.json`, `x.approved.json`, `Fixtures/x.approved.json`; also `.ANTIPHON/fixtures/x.approved.json`, a newline after the suffix, a space/dot in a group, and an empty fixture basename. Each returns 1 with the rejected path and `reason=non_markdown`. Label `c1036-path-boundary`. Do not expect rejection outside root `.antiphon`; R-1 covers that intentional scope. |
| V-3 | `Rejects_run_output_in_fixture_directory` | Beneath `.antiphon/fixtures/` and one valid subgroup: `source.json`, `receipt.json`, `request.json`, `report.json`, `run.trx`, `compiler.log`, `build.binlog`, `console.txt`, `receipt.jsonl`, `receipts.tar.gz`, `x.received.json`, `x.approved.json.log`, and `x.APPROVED.JSON`. Each returns 1 with `reason=non_markdown`, not an error. Label `c1036-run-output-rejected`. These are representative actual producer formats, not disguised approved JSON. |
| V-4 | `Approved_fixtures_obey_checkpoint_exclusion` | Otherwise valid `x.approved.json` beneath `.antiphon/fixtures/checkpoints/`, nested `a/Mixed-CheckPoints/`, and `c1036-checkpoints/`; each returns 1 with `reason=checkpoint_directory`. Label `c1036-checkpoint-precedence`. |
| V-5 | `Approved_fixtures_obey_regular_mode_constraint` | An allowed path stored as symlink mode `120000` and gitlink `160000` through Git plumbing; each returns 1 with `reason=non_regular_mode`. Label `c1036-mode`. No OS symlink permission dependency. |
| V-6 | `Approved_fixtures_obey_one_mib_limit` | The same allowed path with committed blobs of 1,048,575 / 1,048,576 bytes returns 0; 1,048,577 bytes returns 1 with `reason=oversize_blob`. Include multibyte UTF-8 crossing the byte cap and a small index/worktree replacement of the oversized committed blob: still rejected. Label `c1036-size`. |
| V-7 | `Deletion_inventory_preserves_approved_fixtures` | Run both an anchored default cleanup and an unanchored supplemental cleanup labelled CARD-1036 in owned fixture repos. Add an approved JSON plus ordinary `receipt.json` and `compiler.log` in the fixture namespace. Assert the literal approved path is in Keep, absent from Delete (`c1036-fixture-kept`), and both output paths are in Delete. Execute the exact synthetic deletion and validation: exit 0, fixture OID/mode unchanged, no rejected final artifacts, deleted blobs still recoverable. Add a new approved fixture after deletion and validate again, covering the final-tree classifier. No real repository cleanup occurs. |

For V-7, compute expected inventory using the independent C# fixture oracle,
then assert the literal fixture/run-output membership before comparing the CLI
result. This prevents production and oracle changing together from hiding an
incorrect classification. Keep a before/after repository snapshot around the
read-only guard calls. Do not snapshot around intentional fixture Git mutations.

### Guards the regression

| ID | Existing exact methods | Required result |
|---|---|---|
| R-1 | `EvidenceDiffGuardTests.Rejects_non_markdown_outputs`, `EvidenceDiffGuardTests.Allows_small_markdown_and_other_source`, `EvidenceDiffGuardTests.Checks_intermediate_commits_even_when_tip_is_clean`, `EvidenceDiffGuardTests.Checks_merge_side_history`, `EvidenceDiffGuardTests.Reads_pinned_git_objects_not_index_or_worktree` | All five pass: existing output refusals, Markdown/outside-root allowance, full intermediate/side history, and committed-object reads remain effective. |
| R-2 | `EvidenceDeletionGuardTests.Verifies_exact_legacy_deletion` | Unchanged fixed anchor: 108 entries, 88 Delete / 20 Keep, existing hashes/bytes, exact deletion and retained blob readability; default cleanup still requires the anchor. Do not update expected constants. |
| R-3 | `EvidenceSupplementalDeletionGuardTests.Verifies_later_cleanup_without_original_anchor_entries` | Both existing fixture branches pass; later cleanup works without anchor entries while default mode still refuses their absence. |

### Guard inventory

This inventory covers the allowance and independently bypassable restrictions on
that allowance. Existing history/parser/cleanup authorization guards are unchanged;
their existing CARD-1015/CARD-1024 PCs are not duplicated in this bounded card.

| Guard | Invariant | Control |
|---|---|---|
| G-1 | D-1/D-2: eligible fixture reaches the shared accepted format branch | PC-1 |
| G-2 | D-1: full anchored, case-sensitive fixture path boundary | PC-2 |
| G-3 | D-1/D-3: directory membership does not waive approved JSON naming | PC-3 |
| G-4 | D-2: checkpoint directory exclusion precedes fixture allowance | PC-4 |
| G-5 | D-2: fixture allowance cannot waive regular mode | PC-5 |
| G-6 | D-2: fixture allowance cannot waive committed byte cap | PC-6 |
| G-7 | D-4: cleanup partitions approved fixtures into Keep | PC-7 |

Guards = 7; mapped = 7; missing = 0; duplicate PC maps = 0.

### Positive controls

Mutation only, after ordinary Review and land. Each cycle uses exactly
`/*/*/EvidenceApprovedFixtureGuardTests/<method>` with the method named below;
never use the whole class for a PC. All cuts are temporary PowerShell source
changes in `scripts/lib/evidence-policy.ps1`, one at a time. Assert the named
failure in a fresh TRX, restore exact source, and rerun that method green.
Build/fixture errors, zero tests, and diagnostic-only exits are not PC red.

| PC | Production defect to introduce | Exact method and expected assertion failure |
|---|---|---|
| PC-1 | Make `Test-EvidenceApprovedFixturePath` return false. | `Allows_approved_json_fixtures`: `c1036-approved-accepted`, expected exit 0 becomes 1. |
| PC-2 | Broaden only the fixture-directory prefix to any directory below `.antiphon/`, retaining approved suffix matching. | `Rejects_fixture_path_near_misses`: `c1036-path-boundary`, `reports/x.approved.json` unexpectedly exits 0. |
| PC-3 | Accept every regular filename under the correct fixture prefix, removing the approved JSON suffix restriction. | `Rejects_run_output_in_fixture_directory`: `c1036-run-output-rejected`, `source.json` unexpectedly exits 0. |
| PC-4 | Bypass the checkpoint-directory rejection only when the approved path predicate matches. | `Approved_fixtures_obey_checkpoint_exclusion`: `c1036-checkpoint-precedence`, approved JSON in `checkpoints/` unexpectedly exits 0. |
| PC-5 | Bypass the non-regular mode rejection only for an approved path. | `Approved_fixtures_obey_regular_mode_constraint`: `c1036-mode`, symlink unexpectedly exits 0. |
| PC-6 | Bypass the oversize rejection only for an approved path. | `Approved_fixtures_obey_one_mib_limit`: `c1036-size`, the 1,048,577-byte fixture unexpectedly exits 0. |
| PC-7 | In `Invoke-EvidenceDeletion`'s Delete/Keep projections, deliberately classify approved paths as Delete, leaving history and anchor hashing unchanged. | `Deletion_inventory_preserves_approved_fixtures`: `c1036-fixture-kept`, approved path wrongly appears in Delete/is absent from Keep. |

PCs share a production file and therefore run sequentially, without batching.
Sourced Mutation keeps its report, receipts, and restoration record under its
assigned external evidence root; it neither commits nor pushes snapshot changes.

### Out of scope

No receipt-content detector or cryptographic fixture approval, no XML/TRX/log
snapshot exception, no broad fixture relocation, no `.gitignore` expansion, no
historical evidence deletion, no CI or build infrastructure edits, and no
additional OS qualification run. None is needed to prove this path-policy change.

### Checkpoints

Lane for every row: **portable script integration (Git + PowerShell)**, with
placement selected from live runner defaults. The Group names encode that lane.
One isolated test-project build is reused across the same committed S1 group.
Rows run serially. These are the closed ordinary test scope: 14 TUnit executions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1036/` | portable-scripts-fixtures | `/*/*/EvidenceApprovedFixtureGuardTests/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7 | all 7 named methods, 0 failed/skipped | 7 | 8 | true |
| CP-2 | S1 | CP-1 | portable-scripts-history | `/*/*/EvidenceDiffGuardTests/(Rejects_non_markdown_outputs*)\|(Allows_small_markdown_and_other_source*)\|(Checks_intermediate_commits_even_when_tip_is_clean*)\|(Checks_merge_side_history*)\|(Reads_pinned_git_objects_not_index_or_worktree*)` | R-1 | exactly the 5 named methods, 0 failed/skipped | 5 | 2 | true |
| CP-3 | S1 | CP-1 | portable-scripts-anchor | `/*/*/EvidenceDeletionGuardTests/Verifies_exact_legacy_deletion` | R-2 | 1 named method, 0 failed/skipped | 1 | 2 | true |
| CP-4 | S1 | CP-1 | portable-scripts-supplemental | `/*/*/EvidenceSupplementalDeletionGuardTests/Verifies_later_cleanup_without_original_anchor_entries` | R-3 | 1 named method, 0 failed/skipped | 1 | 2 | true |

Use the checkpoint tool's `run --plan` with this file, `--after S1`, and
`--expected-source-sha <committed-S1-sha>`; continue `wait` until exit is not 75.
No source edits or settlement while the run is active. The tool owns the row
build-slot gate. If its executable must first be bootstrapped, build only
`tools/Antiphon.Checkpoints` through `scripts/build-slot.ps1`, using an isolated
`bin-c1036-tool/` OutputPath, then invoke that built DLL to run/wait. Report this
as a tool-bootstrap build, not an unreported test run; do not hold an outer slot
while the tool's rows acquire their own. A slot timeout is not permission to run
unleased. Preserve unedited CHECKPOINT lines, exact SHA, counts, and provenance.

Run the mandatory read-only history admission check over the complete recorded
Code range after the last report commit, including any follow-up evidence prose:

```powershell
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef <recorded-Code-base> -HeadRef <exact-pushed-Code-SHA>
```

This is final artifact admission, separate from the TUnit scope. Require exit 0
and zero violations; never substitute the last-commit diff. Validate clean
checkpoint receipts against the verified SHA. Clean up only owned `bin-c1036/`
and any `bin-c1036-tool/` outputs across projects after all runs complete.

### Cost

All timings below are estimates, not measurements:

- Ordinary V/R floor: CP-1 8 + CP-2 2 + CP-3 2 + CP-4 2 = **14 minutes**,
  including the isolated test build and all 14 results. No repeats after green.
- Code: author classifier/tests/docs 20 + ordinary 14 + bootstrap/report/admission/
  cleanup 6 = **40 minutes**; use `ExpectAbout 40`. A failure-driven rerun stays
  on its named row; report the extra cost and inherited-red reproduction if needed.
- Mutation: source preparation/baseline build 6 + seven exact-method baseline
  checks 3 + seven red/restore/green cycles at 3 minutes each 21 + restoration/
  report 3 = **33 minutes**, separately commissioned. Small incremental rebuilds
  and child Git/PowerShell calls are included; the two phases have one result each.
- Existing historical guard PCs are not re-executed. Ordinary scope is 14 results
  rather than a whole Unit/assembly pass; one shared build avoids three additional
  builds (roughly 9 minutes at an estimated 3 minutes each). No whole-Unit run is
  allowed by the operator brief.

## Plan validation and handoff

At the inspected base, six direct classifier probes produced the expected current
results: four `non_markdown` refusals (approved probe, source JSON, TRX, log), one
`checkpoint_directory`, and one allowed Markdown. The real anchor tree read
confirmed 108 entries, zero fixture-root entries, and both pinned hashes above.
No build, TUnit run, or PC was executed in Plan.

Publish this plan through the normal Plan-task land immediately after settlement,
then commission Code with this plan's checkpoint table. A running Plan task cannot
land itself: `AgentTaskLandService` requires Succeeded before ordinary landing.
The caller can run:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land e86672a3-37e1-4ff5-85a1-218cdf1aa1e6 -ExpectedSourceSha <pushed-plan-SHA>
```

After confirmed publication, dispatch Code without host/OS pins and bind
`checkpoints: docs/superpowers/plans/2026-10-04-card-1036-approved-fixture-evidence-guard-plan.md@<published-plan-SHA> section "### Checkpoints"`.
No decision is pending; the verification design is complete. Next stage: **code**.
