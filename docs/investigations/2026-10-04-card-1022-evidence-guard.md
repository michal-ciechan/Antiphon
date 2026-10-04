# CARD-1022 evidence guard investigation

Confirmed on 2026-10-04: the mandatory recorded-base history check includes already-landed prerequisite commits and counts their files again at the first-parent merge diff. It reproduces **290 violations over 264 distinct paths**. A nonempty throwaway re-cut on fetched master passes with **0 violations**. The classifier also demonstrably rejects synthetic snapshot JSON inside root `.antiphon/`, contrary to the operator's broader snapshot allowance.

Task: `8bc1672b`; investigated Code owner: `ffc43849-8d59-468d-8b6a-2a00033acbda`. Investigation branch: `feat/card-task-8bc1672b`; immutable investigation start: `65a78f3b1f88c213bbb4ac595681212476447130`. No production/test/guard source changed; no existing Code/master branch was rewritten or pushed. Only this Markdown deliverable is committed on the assigned investigation branch. All generated logs, indexes, probe data and scripts remain ignored.

## Pinned evidence and reproduction

`git fetch origin` succeeded. Fetched `origin/master` was `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`; original Code owner tip was `34410810c12c4bb5f01d6dd792ba0bd769b83abb`. The local guard and policy are byte-identical to that master: Git blob IDs `53ab35e2e446b9ab8923b9cc4386daca79bd85c0` and `f5542349d73ac5013676d41a70f1f6d60a6f3b70`, respectively. Guard calls use real local Git objects and no mocked I/O. No build or TUnit driver was needed or run.

The original [admission report](2026-10-04-card-1022-code-admission-ffc43849.md), stored at `34410810c12c4bb5f01d6dd792ba0bd769b83abb:docs/investigations/2026-10-04-card-1022-code-admission-ffc43849.md`, supplies the recorded base and original symptom. This checkout does not contain that file; the cited immutable Git object does.

| Check | Base | Head | Exit | Commits / entries / violations |
|---|---|---|---:|---|
| Original prerequisite-merge range | `3e3436b329da772134c24c8ce4925063bf7d898a` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | 1 | 191 / 594 / 290 |
| Exact pushed Code tip | same recorded base | `34410810c12c4bb5f01d6dd792ba0bd769b83abb` | 1 | 192 / 594 / 290 |
| Nonempty re-cut, carrying the owner's two documentation files | `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` | `e2bcef6cafca115d6ef496ae18f83f8fedd73d2c` | 0 | 1 / 0 / 0 |
| Synthetic snapshot JSON outside runtime root | `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` | `63b2fcbd91104d8082fe679dcdb1da0a58a628c8` | 0 | 1 / 0 / 0 |
| Identical snapshot JSON inside runtime root | `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` | `474ce941466de30aba89ad8670d433ff84e07713` | 1 | 1 / 1 / 1 |

Unedited guard result lines:

```text
EVIDENCE result commits=191 entries=594 violations=290 base=3e3436b329da772134c24c8ce4925063bf7d898a head=0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f
EVIDENCE result commits=192 entries=594 violations=290 base=3e3436b329da772134c24c8ce4925063bf7d898a head=34410810c12c4bb5f01d6dd792ba0bd769b83abb
EVIDENCE range base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd head=e2bcef6cafca115d6ef496ae18f83f8fedd73d2c
EVIDENCE result commits=1 entries=0 violations=0 base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd head=e2bcef6cafca115d6ef496ae18f83f8fedd73d2c
EVIDENCE range base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd head=63b2fcbd91104d8082fe679dcdb1da0a58a628c8
EVIDENCE result commits=1 entries=0 violations=0 base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd head=63b2fcbd91104d8082fe679dcdb1da0a58a628c8
EVIDENCE range base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd head=474ce941466de30aba89ad8670d433ff84e07713
EVIDENCE violation commit=474ce941466de30aba89ad8670d433ff84e07713 path=".antiphon/fixtures/c1022-guard-probe.approved.json" bytes=48 reason=non_markdown
EVIDENCE result commits=1 entries=1 violations=1 base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd head=474ce941466de30aba89ad8670d433ff84e07713
```

Raw local reproduction files: `/work/worktrees/task-8bc1672b/.antiphon/c1022-evidence-guard/` (ignored, not promised as durable after mirror retirement). Essential results, object identities and the full path inventory are retained in this report.

## Confirmed mechanism

[docs/testing-and-build.md:197](../testing-and-build.md#checkpoint-manifest-card-0585) requires the recorded task base through the exact pushed tip, including every introduced side-branch commit; unchanged legacy entries are grandfathered at lines 202–203. The old base predates the prerequisite history. Merge `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` has parents `3e3436b329da772134c24c8ce4925063bf7d898a` and `97e697017ede03643576a5dba199b13c55457e5a`.

[scripts/lib/evidence-policy.ps1:77](../../scripts/lib/evidence-policy.ps1) enumerates `rev-list --reverse --topo-order Base..Head`, without subtracting current-master history. At [lines 198–207](../../scripts/lib/evidence-policy.ps1#L198), every commit is diffed against its first parent, with only deletions skipped. Thus 238 CARD-1008 records plus 26 CARD-0959 records are inspected in their original commits; the same 26 CARD-0959 files are inspected again when introduced against the merge's first parent. All **26/26 merge blobs exactly equal the second-parent blobs**. The additional admission-report commit adds zero evidence entries.

CARD-1008's 238 rejected files were deleted upstream by `f7132d32976ae1d7d8bca5df9442c7c004f2667b` (`Antiphon-Evidence-Deletion: CARD-1024`); deletion does not erase earlier historical violations. Current master still has 26 non-Markdown root-evidence paths from CARD-0959. A re-cut grandfathering those unchanged paths is a clean **history range**, not a claim that master has no tracked run output.

There is no evidence that CARD-1022 authored these payloads. The failure is reconstructed entirely from immutable commits and the real guard's transcript. Calling only the tip diff, changing BaseRef on the existing task, or deleting files now would not prove compliance with the recorded-base contract.

## Classification: original 290 records

Classification uses blob contents and archive member inventories, not the word “snapshot” in a filename. A runtime `source.json` captures the run's SHA/timestamps/counts and is **run-output**, even when a report calls it a source snapshot. An approved/golden fixture is an expected test input/result retained for regression consumption; none was demonstrated in this original set.

The requested two categories do not exhaust the evidence: **287 records are run-output**, and **3 records (2 distinct paths) are authored helper source**, neither output nor demonstrated approved snapshots. Tables label the latter **snapshot-style/source helper†** to distinguish retained input/source from run output, with this qualification explicit. It would be inaccurate to claim three golden snapshots or to call executable helper source generated run output.

| Root path pattern | Records | Distinct paths | Snapshot-style/source helper† | Run-output |
|---|---:|---:|---:|---:|
| `.antiphon/c1008-evidence/**` | 25 | 25 | 0 | 25 |
| `.antiphon/c1008-evidence-round2/**` | 28 | 28 | 0 | 28 |
| `.antiphon/c1008-evidence-round3/**` | 55 | 55 | 0 | 55 |
| `.antiphon/c1008-evidence-round4/**` | 74 | 74 | 0 | 74 |
| `.antiphon/c1008-evidence-round5/**` | 48 | 48 | 1 | 47 |
| `.antiphon/checkpoints/**` | 8 | 8 | 0 | 8 |
| `.antiphon/c959-code-evidence/**` | 4 | 2 | 0 | 4 |
| `.antiphon/c959-cont-evidence/**` | 20 | 10 | 0 | 20 |
| `.antiphon/c959-second-evidence/**` | 28 | 14 | 2 | 26 |
| **Total** | **290** | **264** | **3** | **287** |

| File type / pattern | Records | Classification | Content evidence |
|---|---:|---|---|
| `.log` | 81 | Run-output | Build/compiler, executor, console, validation and driver output. |
| `.trx` | 28 | Run-output | XML TestRun definitions/results from named executions. |
| `.txt` | 55 | Run-output | CHECKPOINT copies, Git/host/output inventories, coverage, roster, validation, rerun and cleanup records. |
| `.json` | 67 | Run-output | Run reports/state/request/ownership, source/build receipts, inventories, census, Docker results and measured timings. |
| `.yaml` | 10 | Run-output | Imported/resolved checkpoint manifests saved for a specific execution; not golden regression fixtures. |
| `.lock` | 2 | Run-output | Checkpoint cleanup lock state. |
| `.html` | 24 | Run-output | Generated TUnit reports, including copied attachment reports. |
| `.jsonl` | 2 | Run-output | Checkpoint execution state history. |
| `.ps1` | 3 | Snapshot-style/source helper† | Authored benchmark and TRX inspection helpers; see qualified helper finding below. |
| `.md` | 3 | Run-output | Generated checkpoint report.md and failures.md, inside forbidden checkpoint directories. |
| `.yml` | 3 | Run-output | Imported checkpoint manifests saved during these tasks. |
| `.tar.gz` | 6 | Run-output | Archives of actual checkpoint runs, receipts, logs and state. |
| `.sha256` | 2 | Run-output | Digest of retained run archive. |
| `.tsv` | 4 | Run-output | Executed TRX roster and platform-exclusion summaries. |

Guard reasons: **282 non_markdown**, **8 checkpoint_directory**; no `oversize_blob` or `non_regular_mode` diagnostics (earlier checks take precedence). “Record” means one commit/path diagnostic, not one unique file.

## Introducing commits

All records are accounted for below. The final merge is an arrival record, not original authorship. Type totals include the duplicate merge observations.

| Commit | Subject | Records | Snapshot-style/source helper† | Run-output | File types |
|---|---|---:|---:|---:|---|
| `cf9881dd143804e114d7d68c54af10bd156ab704` | docs(card-1008): record 19 green enhanced methods and real manual subsets; Final incomplete at box | 23 | 0 | 23 | `.log` 9, `.trx` 4, `.txt` 3, `.json` 7 |
| `447f1d0f81cde0b10ca6d519f728bd3e967e4aae` | docs(card-1008): confirm completed R-1 54/54 at stop boundary; R-2 and Final remain pending | 2 | 0 | 2 | `.log` 1, `.trx` 1 |
| `18d64696354feeb10dcb1d7e90acab7c49762cc7` | docs(CARD-1008): report incomplete Final; CP4 207 green, latest CP3 53/54, Docker 25/32 red, Unit interrupted | 28 | 0 | 28 | `.log` 12, `.txt` 1, `.trx` 6, `.yaml` 4, `.json` 5 |
| `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | test(CARD-1008): CP3 donor repair qualified 54/54; archive missing-jq attempt | 26 | 0 | 26 | `.lock` 1, `.log` 6, `.json` 6, `.txt` 5, `.yaml` 1, `.html` 4, `.trx` 2, `.jsonl` 1 |
| `97445c89878addf92b2ec457fbf369db495a53f1` | fix(CARD-1008): retain retirement and seed call-chain contracts; base verifies retire red and seed green | 13 | 0 | 13 | `.log` 5, `.html` 2, `.txt` 2, `.trx` 2, `.json` 2 |
| `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | test(CARD-1008): CP5 green 32/32 outcomes in 531s with exact cleanup; Final Unit and CP2 remain pending | 14 | 0 | 14 | `.json` 6, `.txt` 3, `.trx` 1, `.log` 3, `.yaml` 1 |
| `94c1f5eca2addfade3518ee605c4013f7276b393` | docs(CARD-1008): report CP3 and Docker32 green; stop per brief with 85-130 minutes Final work pending | 2 | 0 | 2 | `.yaml` 1, `.json` 1 |
| `31e92d8e810ce6572300ed4331684caa59349551` | CARD-1008: repair origin and task-detail fixtures; CP2 timed out, old contracts 2/2 | 21 | 0 | 21 | `.txt` 2, `.log` 7, `.json` 8, `.yaml` 1, `.html` 2, `.trx` 1 |
| `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | CARD-1008: qualify repaired guards and old contracts 2/2 each; CP2 total-timeout, Unit pending | 26 | 0 | 26 | `.txt` 6, `.log` 9, `.yaml` 1, `.json` 4, `.html` 4, `.trx` 2 |
| `6eb387c1527fed85535125b3199bc3c5d79fd68f` | CARD-1008: qualify R1 54/54 and R2 full classes 207/207; CP2 and Unit remain pending | 26 | 0 | 26 | `.json` 8, `.log` 6, `.txt` 3, `.lock` 1, `.yaml` 1, `.html` 4, `.trx` 2, `.jsonl` 1 |
| `c696fc9fab53b1cce5205b12e5c718b908477809` | CARD-1008: report incomplete Final; R1/R2 and repairs green, CP2 total-timeout and Unit pending | 1 | 0 | 1 | `.log` 1 |
| `c43bf296113e715c1087c7eae63782b7e229b73a` | CARD-1008 invoke offline HTTP fixture in script scope; benchmark faster, CP-2 pending | 2 | 1 | 1 | `.json` 1, `.ps1` 1 |
| `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | CARD-1008 suppress fixture Console stderr; CP-2 completed 18/19, custody repair pending | 5 | 0 | 5 | `.json` 2, `.md` 2, `.trx` 1 |
| `5609cb93ba5109d19044ad7da5bf2c96363c5132` | CARD-1008 Unit affected rosters green, custody repaired; whole lane has isolated-green teardown failure and 33 skips | 33 | 0 | 33 | `.html` 8, `.log` 9, `.txt` 4, `.trx` 5, `.json` 7 |
| `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | CARD-1008 R2 full 207/207 and affected V/R rosters verified; retain unresolved Final receipt caveats | 12 | 0 | 12 | `.json` 5, `.log` 3, `.yml` 1, `.txt` 1, `.md` 1, `.trx` 1 |
| `0049877688f895bc4fb75e49cfe8911489b2a937` | CARD-1008 Final incomplete: speedup/custody and V/R green, Unit teardown and exact CP2 closure outstanding; outputs cleaned | 4 | 0 | 4 | `.json` 3, `.txt` 1 |
| `156b8a9150e9125f871c81a7b2548b2f6f624bb5` | docs(card-0959): preserve partial Code and 129 passing CP receipts; admission and Final remain incomplete | 2 | 0 | 2 | `.txt` 1, `.tar.gz` 1 |
| `41a28eeedc2c538fa8e2521e73325631ae97441f` | CARD-0959 partial Code handoff: Unit 3942 passed, portable 136 passed/1 LF delivery red; guards and Windows pending | 10 | 0 | 10 | `.log` 5, `.txt` 3, `.yml` 1, `.tar.gz` 1 |
| `79e55c095ed9aa36e42864f2483dfb360a9379c2` | CARD-0959 partial continuation: 137 portable and 3961 Unit pass; remote recovery and guard qualification remain | 14 | 1 | 13 | `.sha256` 1, `.txt` 8, `.ps1` 1, `.tar.gz` 1, `.json` 1, `.tsv` 2 |
| `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | merge(card-1022): integrate landed runtime and fixture prerequisites; verification pending | 26 | 1 | 25 | `.txt` 12, `.tar.gz` 3, `.log` 5, `.yml` 1, `.sha256` 1, `.ps1` 1, `.json` 1, `.tsv` 2 |

## Content witnesses and limits of the snapshot finding

All 264 distinct blobs were read from their diagnostic commit; all three distinct archives were listed without running or extracting their contents. The archives contain 52, 246 and 367 members respectively, including actual TRX/report/source/state/log/manifest files. These are archived run output, not approved snapshots. The [CARD-0959 README at line 5](../../.antiphon/c959-second-evidence/README.md) explicitly describes the two task-owned run roots; its lines 7–9 describe roster/validation results and incomplete qualification.

The checkpoint producer confirms these identities: [CheckpointApp.cs:369](../../tools/Antiphon.Checkpoints/CheckpointApp.cs#L369) writes `manifest.resolved.yaml`; line 386 writes `request.json`; [ReportWriter.cs:70](../../tools/Antiphon.Checkpoints/Report/ReportWriter.cs#L70) writes `report.md` and `report.json`. Saved/imported manifests include a task-specific absolute plan path, selected builds/checkpoints and run settings. Their existence as generated configuration does not establish a golden-snapshot contract.

Representative immutable content citations:

- `18d64696354feeb10dcb1d7e90acab7c49762cc7:.antiphon/c1008-evidence-round2/docker-partial-evidence.json:2`: source/base SHA, operation IDs, observed Docker outcomes and cleanup.
- `3940bf7c2b9f407afaa49962a98c7175a0a97ce8:.antiphon/c1008-evidence-round3/cp3-missing-jq/manifest.resolved.yaml:2`: absolute task plan and execution configuration; not an approved fixture.
- `79e55c095ed9aa36e42864f2483dfb360a9379c2:.antiphon/c959-second-evidence/run-history.json:3`: actual run ID, exit and SHA; lines 21–24 contain dirty-count/fingerprint/time observations.
- `5609cb93ba5109d19044ad7da5bf2c96363c5132:.antiphon/c1008-evidence-round5/bench-http.ps1:2`: authored benchmark invokes the existing `scripts/fixtures/c727-fake-http.ps1`; subsequent loops measure elapsed time and write `bench-http.json`. The helper is source; its resulting JSON is run-output.
- `79e55c095ed9aa36e42864f2483dfb360a9379c2:.antiphon/c959-second-evidence/inspect-receipts.ps1:2`: authored helper targets a concrete task worktree, parses real TRX (lines 7–13), and writes roster/count evidence (lines 15–16). The original and merge diagnostics observe the identical helper blob.

## Does the classifier wrongly reject snapshots?

Under the original documented rule, its behavior is intentional: [docs/testing-and-build.md:191](../testing-and-build.md#checkpoint-manifest-card-0585) permits only regular Markdown up to 1 MiB outside checkpoint-named directories under root `.antiphon/`. The implementation has **no snapshot/fixture allowlist**: [evidence-policy.ps1:135](../../scripts/lib/evidence-policy.ps1#L135) immediately accepts every path outside that root; line 138 rejects checkpoint directories; line 141 rejects every extension other than `.md`. The bytes' semantic role is never inspected.

Under the operator direction in this brief, the blanket rule cannot distinguish permitted snapshots from run output inside that root. It also rejects the two authored helper sources above, whose intended long-term custody remains unresolved. This is a **confirmed path/extension mechanism inconsistent with the broad snapshot allowance**, not evidence that all 290 records deserve exemption. The JSON probe is constructed evidence of the mechanism, not a claim that a production test currently consumes it: identical regular 48-byte blob `e29677a0b1c0378e9f1c12f01a5633b4471b9d7d`, contents `{"approved":true,"expected":{"status":"ready"}}`, accepted as `tests/Antiphon.Tests/Fixtures/c1022-guard-probe.approved.json` and rejected as `.antiphon/fixtures/c1022-guard-probe.approved.json`. Golden fixtures in ordinary test paths are already outside this guard's scope. No actual approved/golden fixture false positive was found among the original 290.

## Re-cut result and scope

The throwaway object `e2bcef6cafca115d6ef496ae18f83f8fedd73d2c` has fetched master `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` as its sole parent and tree `0abb100cd5b36196321c60cf0f463df446df47b1`. It copies exactly the original owner's admission report and plan/freeze file through an **isolated temporary Git index** and `commit-tree`, leaving all existing branch refs and the task working index untouched. It has a nonempty two-file delta: admission report added; plan modified. The real CLI returns exit 0. No throwaway objects/refs were pushed.

Reproduction of the failing range:

```sh
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 \
  -BaseRef 3e3436b329da772134c24c8ce4925063bf7d898a \
  -HeadRef 34410810c12c4bb5f01d6dd792ba0bd769b83abb
```

Reproduce a nonempty re-cut without checking out or changing any branch (run from the repository; keep the generated index ignored):

```sh
mkdir -p .antiphon/c1022-evidence-guard
GIT_INDEX_FILE="$PWD/.antiphon/c1022-evidence-guard/replay.index"
export GIT_INDEX_FILE
recut_base=9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd
recut_owner=34410810c12c4bb5f01d6dd792ba0bd769b83abb
git read-tree "$recut_base"
for recut_path in \
  docs/superpowers/plans/2026-10-03-card-1022-modern-conpty-only-plan.md \
  docs/investigations/2026-10-04-card-1022-code-admission-ffc43849.md
do
  recut_blob=$(git rev-parse "$recut_owner:$recut_path")
  git update-index --add --cacheinfo 100644 "$recut_blob" "$recut_path"
done
recut_tree=$(git write-tree)
recut_head=$(git -c commit.gpgsign=false commit-tree "$recut_tree" \
  -p "$recut_base" -m 'throwaway CARD-1022 evidence re-cut')
unset GIT_INDEX_FILE
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 \
  -BaseRef "$recut_base" -HeadRef "$recut_head"
```

This proves the baseline remedy for the **blocked owner's existing documentation delta**, not a fresh Code implementation or Windows/Linux qualification. Current master already contains later CARD-1022 work and a newer plan checkpoint roster (plan commit `dc129d2bd1e08edc8c37104abc09c22dc08a0122`). Copying the old plan verbatim would overwrite that newer plan: the synthetic experiment is not a ready-to-land successor. Also, `git merge-base --is-ancestor` returns 1 for the old owner tip against this re-cut. It does not establish eligibility for reviewed adoption, whose lineage requirement is documented in [orchestration-loop.md](../orchestration-loop.md#landing-a--startref-repair-card-0675). Caller must choose a valid commissioning/landing route; this investigation performs no adoption or land.

## Remaining uncertainties

- Whether the operator wants snapshot/fixture storage under runtime root `.antiphon/`, or only established test fixture directories. A named permitted path contract would resolve the scope of a guard amendment; the observed mechanism is already confirmed.
- Whether the two task-specific authored helper scripts should become maintained source. Their source nature is proven, but no durable regression consumer/approval was demonstrated; they must not be silently labeled golden snapshots.
- A future successor's exact implementation delta and landing eligibility are not investigated. Its recorded base, exact pushed candidate SHA and applicable owner lineage checks must be assessed when that task exists. The fetched-master result is pinned, not a promise about future master.

## Not done, noted

Recommendation: **both**—commission an appropriate fresh baseline for the old CARD-1022 history gate, and separately amend `Get-EvidenceViolation`'s blanket `non_markdown` rule (line 141) with an explicit approved fixture/snapshot path exception before that rejection, preserving checkpoint/run-output rejection and documenting the exception in the owner policy; no fix was designed or implemented here.

## Complete original path inventory

One row per distinct path; the first introducing commit is the diagnostic commit where it first appears in this range. A second merge record means the same blob is observed at `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f`. Together the 264 rows and 26 merge repeats reconstruct all 290 diagnostics. † marks authored helper source, not a demonstrated golden snapshot. Blob IDs, bytes and reasons are read from immutable Git objects.

| Path | First introducing commit | Blob | Bytes | Reason | Records | Classification |
|---|---|---|---:|---|---:|---|
| `.antiphon/c1008-evidence-round2/c1008-guard-red.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `6dad928f274092945feddd741230995c7f687b51` | 8459 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-repair-guards.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `33b6c8f1f506867e5ff6a2bd187e82c37cca35fa` | 202 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-cp2-final.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `6ccbe93503952efcaa1bf5590a8e712e7370dc0b` | 175 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-cp5-batched.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `383edc44a87eb7c95372a2c2970af729dd225f54` | 171 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-cp5-cp3-final.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `1d00e2da33e361c9c5744689e1d3487fb3ee6685` | 211 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-cp5-fixed.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `c3a0cca67275726a807894f7725fd27b1ca7b417` | 171 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-cp5-trace.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `29bfad0ed224e339f4839b46d10f2aa70f4ff8ce` | 172 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-final-checkpoints.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `d3b4f08e49382ef7ffd98cfda2cd48da380f9dea` | 252 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-run.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `399f09c8a593e374b256a569f5323d8225db19f4` | 210 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-unit-fixed.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `c3f78a1b199279d3a846e394053c53b8a7e88191` | 465118 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/c1008-round2-unit.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `fe051d82f608a04db46c7a869fa008a3b2bac6a0` | 22507 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/checkpoint-lines.txt` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `56845b37c4ad86638d9a80184fe9d94bc23c5440` | 2126 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-cp5/CP-3.trx` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `a9b863553c71f800d3146dda8e513bd3aec9771a` | 110584 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-cp5/CP-5.trx` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `69daa49bce6b6d7d29eb4a35fbe0de210657ec04` | 27287 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-cp5/manifest.resolved.yaml` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `9f81fe7e0fa3cf1afc55423ad32e42a9bd7d3bdf` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-cp5/report.json` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `eb0012a6f8418caecd4a36546dd9196fb03ef0ce` | 43823 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-repaired/CP-3.trx` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `b9c54969a1aacdc8945a9db3355edf095bfba6d4` | 107463 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-repaired/console.log` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `e512a4fc0562a54121d79400e40d7db2106d4187` | 3497 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-repaired/manifest.resolved.yaml` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `9f81fe7e0fa3cf1afc55423ad32e42a9bd7d3bdf` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp3-repaired/report.json` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `bb0455d324b1bbc89f20706a356043dcc8550051` | 10042 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp4/CP-4.trx` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `f7fd5d4a9be0c141cd567b7c32d9ab1e56504b57` | 300519 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp4/CP-5.trx` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `68e5839afedddd945fc7803526230f0ecb070d86` | 7918 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp4/manifest.resolved.yaml` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `9f81fe7e0fa3cf1afc55423ad32e42a9bd7d3bdf` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/cp4/report.json` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `537fe6ed7e58ac343d967a56cffc4d91261f0f31` | 16478 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/docker-partial-evidence.json` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `d73086318b5773a72a19f6a65b511bdc9ecc6ee9` | 608532 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/final-import.yaml` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `9f81fe7e0fa3cf1afc55423ad32e42a9bd7d3bdf` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/guard-red.trx` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `891a10886139314378a17c8a0f4954f12732bb0f` | 11870 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round2/outputs-removed.json` | `18d64696354feeb10dcb1d7e90acab7c49762cc7` | `8c8e3760561449cde6fb4a1605be46945440f796` | 7808 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-outputs-removed.json` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `f05d4a01faaff85560e862975fc00e7c84e15a0b` | 2018 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified.log` | `97445c89878addf92b2ec457fbf369db495a53f1` | `605cd8e5a8e8c89381ccc2510a82c4dd7b59b903` | 4780 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified/BASE-old-contracts-20261003-180009-5c8d/Antiphon.Tests-linux-net9.0-report.html` | `97445c89878addf92b2ec457fbf369db495a53f1` | `b889217fa97fe120933e55569ed4d84c3afff186` | 101860 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified/BASE-old-contracts-20261003-180009-5c8d/_5d9e09695ab3_2026-10-03_18_00_19.2404419/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `97445c89878addf92b2ec457fbf369db495a53f1` | `b889217fa97fe120933e55569ed4d84c3afff186` | 101860 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified/BASE-old-contracts-20261003-180009-5c8d/git.txt` | `97445c89878addf92b2ec457fbf369db495a53f1` | `9698259d7d0006b44d03a993474fcf097f8a4497` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified/BASE-old-contracts-20261003-180009-5c8d/run.log` | `97445c89878addf92b2ec457fbf369db495a53f1` | `99390e5f51683c8e27fbc5c6ca5bcb4192d04852` | 3643 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified/BASE-old-contracts-20261003-180009-5c8d/run.trx` | `97445c89878addf92b2ec457fbf369db495a53f1` | `2e1319434235100e6c8ae35ee1b937851745eab8` | 7449 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-qualified/BASE-old-contracts-20261003-180009-5c8d/source.json` | `97445c89878addf92b2ec457fbf369db495a53f1` | `f11f1be5dbc8bd5177550d2a66e1993dc653ddc7` | 1632 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-zero-filter.log` | `97445c89878addf92b2ec457fbf369db495a53f1` | `37a33980b8391335e89efde6b24df20efdcbaec4` | 432515 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-zero-filter/BASE-old-contracts-20261003-175728-204a/build.log` | `97445c89878addf92b2ec457fbf369db495a53f1` | `a602050f3292948131b5680bd37a3bbb07f3a89e` | 430479 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-zero-filter/BASE-old-contracts-20261003-175728-204a/git.txt` | `97445c89878addf92b2ec457fbf369db495a53f1` | `9698259d7d0006b44d03a993474fcf097f8a4497` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-zero-filter/BASE-old-contracts-20261003-175728-204a/run.log` | `97445c89878addf92b2ec457fbf369db495a53f1` | `24e8c5cf883eecad4ddd1119c3b57850fd18864d` | 1108 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-zero-filter/BASE-old-contracts-20261003-175728-204a/run.trx` | `97445c89878addf92b2ec457fbf369db495a53f1` | `e07786dd4e092ae80bb0e01e502d28d48e823ead` | 1387 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/base-zero-filter/BASE-old-contracts-20261003-175728-204a/source.json` | `97445c89878addf92b2ec457fbf369db495a53f1` | `570696d2088331d81fd006d6e30fa3a93dd53ef7` | 1631 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/checkpoint-lines.txt` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `933c1a3ed8814d6948885b1e254e6f748e0faaa3` | 2379 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/.cleanup.lock` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `e69de29bb2d1d6434b8b29ae775ad8c2e48c5391` | 0 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/builds/bin-c1008-cache/build.log` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `641ec34d864662855f5799c96e2b929f7be07248` | 432358 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/executor-ownership.json` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `78fb36ccc4be929ded4778c4b2faa7603b2fa722` | 288 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/executor.log` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `6b5379c760611cdac2e3bc545dadaa9c84d947c4` | 777 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/git.txt` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `873f6e9bfa272cfaced2472548155e5068b78fcd` | 5422 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/host.txt` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `44a10735e40d39e2183affa6e570080829f42c57` | 166 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/manifest.resolved.yaml` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `a917fa205259d1c5bbd2f7cb0d7548f07a327919` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/ownership.json` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `89d407c04a62cc993d27322bdbf1b35f417ccf1b` | 460 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/report.json` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `ac349c6d275fbe00c7dcb1208cafd2fa40ff219a` | 9404 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/request.json` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `90b0f43cb4af01748115ff03b43930603cf79efa` | 996 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/rows/CP-3/Antiphon.Tests-linux-net9.0-report.html` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `f1e3f71eec27573cb75e8af58949f8aaf0835eef` | 127931 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/rows/CP-3/_5d9e09695ab3_2026-10-03_17_53_38.4907733/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `f1e3f71eec27573cb75e8af58949f8aaf0835eef` | 127931 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/rows/CP-3/console.log` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `3cb6be1dba4524a1f964e1b877af17d7193f2d4a` | 6722 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/rows/CP-3/rerun.txt` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `b6d0a62509594396aa5caaac9a8adcb3782033bf` | 432 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/rows/CP-3/run.trx` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `cd5f3e8a597c04b3c96b0e36f5e36f0fcead93ad` | 99679 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/state.history.jsonl` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `9ce3718f67e8c1f0b1cd006a0ab82459e1b1b18f` | 29135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-missing-jq/state.json` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `9ced332ab987b87a460cf18ae8b7bf126107d45f` | 5768 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified.log` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `d2539edd4e9c410b785bfe707bbb57e99eb2646a` | 8573 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified/CP-3-20261003-175412-2e97/Antiphon.Tests-linux-net9.0-report.html` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `9df89a5d4cc36f38952e0d02bc18a168e502c617` | 125964 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified/CP-3-20261003-175412-2e97/_5d9e09695ab3_2026-10-03_17_56_53.3944189/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `9df89a5d4cc36f38952e0d02bc18a168e502c617` | 125964 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified/CP-3-20261003-175412-2e97/git.txt` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `137fe8729097548b5c1f8473ef4f8c1f6d82cdde` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified/CP-3-20261003-175412-2e97/run.log` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `a8f72b395697af5dd72c9eebeb03279844edb7d1` | 1435 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified/CP-3-20261003-175412-2e97/run.trx` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `600df847ea90a94703b98897832487363bc78f31` | 105671 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-qualified/CP-3-20261003-175412-2e97/source.json` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `5caa9d22e91c6fa1f5cd006887298f51d873b09d` | 1539 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp3-trx-inspection.txt` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `875995cd10406b66cf48a4e8182541bba109421d` | 58 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/CP-5.trx` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `8acc4569c38296c90fc8e10e246828907a932bb6` | 31660 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/build.log` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `eaa9baeec457fe61d86d9183d58202abd19e9c0f` | 429940 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/checkpoint-build-source.json` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `bffa66e30e3b7f3a51da5e7d920dedcdf6329796` | 549 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/console.log` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `c12ebd31816f05fc300cb9cb40e763cd2ac922af` | 1351 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/executor.log` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `bb3e7fe488cee1a1b06c2695657a714beb69435b` | 2908 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/git.txt` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `6f5e56a90b8676ca95628cc61764bc403c49e613` | 8033 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/host.txt` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `665ee5f0bcaa4efefea30adeaabebc9068c9cba8` | 168 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/manifest.resolved.yaml` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `a917fa205259d1c5bbd2f7cb0d7548f07a327919` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/cp5/report.json` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `a44165cd90684ec2178b1219845034edafb844fe` | 7724 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/default-cold-seed-inspection.json` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `ee5984987d1180b61c720720381a36f17f6e600b` | 338 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/docker-evidence.json` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `47fd57c34dc2d8a502e5fa09fec9615986dde3dd` | 1368990 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/docker-timing-summary.json` | `f0d69b033b5e9b48712600aa40a8fb0ce088acf6` | `93bbdce35e65b243bccd849a838bbef4cd969dce` | 19988 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/final-import.yaml` | `94c1f5eca2addfade3518ee605c4013f7276b393` | `a917fa205259d1c5bbd2f7cb0d7548f07a327919` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/outputs-removed.json` | `94c1f5eca2addfade3518ee605c4013f7276b393` | `f64f9121eb30147bf76e45609aa2cb250fa39234` | 5234 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round3/tool-build.log` | `3940bf7c2b9f407afaa49962a98c7175a0a97ce8` | `f875078b7c43c0638bbfdf77eb986c719112f5ba` | 982 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/bin-c1008-cache-build-source.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `cd34f93e766b0db97c86d16c8029e1623cb3276b` | 551 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/bin-c1008-docs-build-source.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `88353a14d961bcc2041748237f9be48a760a5cd1` | 549 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/bin-c1008-scripts-build-source.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `2dd26bab5172356f043e572e964d1a0bf55ebcf0` | 555 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/coverage-current.txt` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `7e68816044dc64c11be6a9f334bde0810422efe2` | 3249 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/coverage.txt` | `31e92d8e810ce6572300ed4331684caa59349551` | `d80479fffa174e241b28cacf31869f06f42d4008` | 3249 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-tool.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `8544aa66745d4f84c7291c296f6c07f871a8df29` | 169 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-total-timeout/builds/bin-c1008-scripts/build.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `e6b9c8a4267fab5331f0c7db84961d2051c8ed8f` | 12818 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-total-timeout/executor.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `90c7289a1a50a9745783b7df49acfc96b536dbc5` | 998 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-total-timeout/manifest.resolved.yaml` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `c478a966ebab920a212246a51106619f685417e9` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-total-timeout/report.json` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `57bb503d2619176d190a03e3ba72d8b59195da2f` | 6562 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-total-timeout/rows/CP-2/console.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `eb998fabd5dae9a9f22ce1c170ef56d8b1c51640` | 731 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-repaired-total-timeout/state.json` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `f231ca590a40f9b2da67ed0cbbd09375e0cea3bb` | 4903 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-tool.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `898299462bc4ff5bb0202baa88df70416f77a397` | 273 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/build.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `5bcef014a4efedf361a5f443642166576a551f0a` | 432912 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/checkpoint-build-source.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `e65abad756231331091d607be1b492a023e6ee29` | 555 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/console.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `0ca1e883f9b2a28e4236fa373122d685fe7e73f3` | 3718 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/executor.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `871ca67e20b0762947be3665a6f523109ded9700` | 909 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/manifest.resolved.yaml` | `31e92d8e810ce6572300ed4331684caa59349551` | `c478a966ebab920a212246a51106619f685417e9` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/report.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `45603b93389870d5f3cb319e88b4b7b95614eaad` | 6567 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp2-total-timeout/state.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `60e8948543d8a842e24653974c4afd2de97f9856` | 4905 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4-tool.log` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `b93259e805c54f7414510bba9d0807b3098b8bc9` | 211 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4-trx-inspection.txt` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `77236a9aaca5587d5066904e2e811fec5b0cae29` | 21553 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/.cleanup.lock` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `e69de29bb2d1d6434b8b29ae775ad8c2e48c5391` | 0 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/builds/bin-c1008-cache/build.log` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `06f60c09b93fff3bc702d78d73d45570f36d3701` | 429978 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/builds/bin-c1008-docs/build.log` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `0349e7a03acd72a502a0e083dda0c70f04a92938` | 428162 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/executor-ownership.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `6269fc5f1bc1799e33524dad8eb790e90a83b69b` | 288 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/executor.log` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `499c5316adebb76467a9d2be2dfaefd6506f91ac` | 1325 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/git.txt` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `1cec1667db95c8ffffb0e6400b56da4d0ccdc045` | 12440 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/host.txt` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `e7de2de9d5a3a45bf851dbbdfcd7d3356af7884e` | 164 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/manifest.resolved.yaml` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `c478a966ebab920a212246a51106619f685417e9` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/ownership.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `ae5b9d1949b85229a4ec12b575b5c2477834e3b6` | 460 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/report.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `60b02216dbc07bac3c488a19737051a6cf506030` | 11753 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/request.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `df47c3b0fd4bc9e17d73edb02fc08ac6b979d599` | 1002 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-3/Antiphon.Tests-linux-net9.0-report.html` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `5428ef3efbc1b9553dd7da35c142de6c2c09b3ce` | 126360 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-3/_5d9e09695ab3_2026-10-03_20_08_46.1110971/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `5428ef3efbc1b9553dd7da35c142de6c2c09b3ce` | 126360 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-3/console.log` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `c934710ea98ba2638cbfbe2143fd8667732166b0` | 1354 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-3/run.trx` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `d9dcc9f2d5cd04caabea29dd84c9e9448dfafc39` | 105571 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-4/Antiphon.Tests-linux-net9.0-report.html` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `3f931e4d25a59d2a607befbf2396dc0ec5c69136` | 182499 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-4/_5d9e09695ab3_2026-10-03_20_11_52.9808448/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `3f931e4d25a59d2a607befbf2396dc0ec5c69136` | 182499 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-4/console.log` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `198defd5f58dd7a9b81cc13a3c3ad3b1002c3b4b` | 1352 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/rows/CP-4/run.trx` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `c8423b95f7da83311a0e894335aac8a18867225c` | 300522 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/state.history.jsonl` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `780edf02de8622329cbe32d756b62f90a5f755e5` | 56674 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/cp3-cp4/state.json` | `6eb387c1527fed85535125b3199bc3c5d79fd68f` | `5113e3d74792d9633d14be6d46949ec01b94ee06` | 7510 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts-inspection.txt` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `0f4fa747fefab8bd186ede703b8193eae471b7b7` | 828 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `d9345f6151021f9472eb8f635871f06790890786` | 2604 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts/CURRENT-old-contracts-repaired-20261003-200216-1a21/Antiphon.Tests-linux-net9.0-report.html` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `89baff632bb392f4b0e09d67f9e1faa9092b3bfe` | 100560 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts/CURRENT-old-contracts-repaired-20261003-200216-1a21/_5d9e09695ab3_2026-10-03_20_02_21.6515810/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `89baff632bb392f4b0e09d67f9e1faa9092b3bfe` | 100560 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts/CURRENT-old-contracts-repaired-20261003-200216-1a21/git.txt` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `dd0daa78a41e41dad6e61ad72f726993b5771503` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts/CURRENT-old-contracts-repaired-20261003-200216-1a21/run.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `5bf21f73af8055471af8a6ef4dff223aae4a8668` | 1533 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts/CURRENT-old-contracts-repaired-20261003-200216-1a21/run.trx` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `db1b9cce930847fd09b421bfa2968b78160d7cf3` | 5700 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/current-old-contracts/CURRENT-old-contracts-repaired-20261003-200216-1a21/source.json` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `21136c928ba45588ef66284e11c9feb82ad676ac` | 1692 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/inherited-real-inspection.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `be8bd5fdc890bb63dbbe07beb7badc2ba36fb3f9` | 16650 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/master-census.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `17d4ff0ce344c433dc6fb1d39f125e6b38cf7bd5` | 2313 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contract-trx-inspection.txt` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `fbfbf5a246cfbb71f17fbda85049e9aba94d9e5d` | 537 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `67329520c32055604352833b127a7d54fa0aae31` | 2518 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts/CURRENT-old-contracts-20261003-191006-e376/Antiphon.Tests-linux-net9.0-report.html` | `31e92d8e810ce6572300ed4331684caa59349551` | `0eaca0f6c5a52ecb2b4c3e5e27c9998761ffa83d` | 100568 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts/CURRENT-old-contracts-20261003-191006-e376/_5d9e09695ab3_2026-10-03_19_10_13.3501390/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `31e92d8e810ce6572300ed4331684caa59349551` | `0eaca0f6c5a52ecb2b4c3e5e27c9998761ffa83d` | 100568 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts/CURRENT-old-contracts-20261003-191006-e376/git.txt` | `31e92d8e810ce6572300ed4331684caa59349551` | `35fd123b1b9b74e109c7a7fb3eecf3be39650f09` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts/CURRENT-old-contracts-20261003-191006-e376/run.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `979b911f769c36abd15d86c44177f522210647a5` | 1482 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts/CURRENT-old-contracts-20261003-191006-e376/run.trx` | `31e92d8e810ce6572300ed4331684caa59349551` | `b5bf4220a79b6d175de3a5caa557a36e42838877` | 5701 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/old-contracts/CURRENT-old-contracts-20261003-191006-e376/source.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `6aaed6959e0494b778fa746c027fdd47358072ba` | 1657 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/output-cleanup.log` | `c696fc9fab53b1cce5205b12e5c718b908477809` | `fd4c4326492aa4b015da5685a82916b353f7fc8a` | 6601 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/pc-inspection-coordinates.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `9c20c5261e08403779b200c149244eb801f9285f` | 110260 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification-inspection.txt` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `bbeb3471bbe1794c77e85e6c05c2ad0710dbb3d9` | 722 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `906e86442bbfc9b1773047480a072a47a7c709dd` | 432563 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/Antiphon.Tests-linux-net9.0-report.html` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `150e87d76cf67e3051185dd7bbcf66b338c688fb` | 100555 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/_5d9e09695ab3_2026-10-03_19_20_34.3290739/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `150e87d76cf67e3051185dd7bbcf66b338c688fb` | 100555 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/build.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `e4afafe75e04f02a66802c78fe32002b039df56f` | 430080 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/git.txt` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `dd0daa78a41e41dad6e61ad72f726993b5771503` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/run.log` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `8ad1e99f1260556d5a2e152fc7a56c3a23cf5431` | 1522 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/run.trx` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `f2db8cb29bdf974e21761ee488d0715341b4bddf` | 5167 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/repair-qualification/C1008-repair-qualification-20261003-191315-18e4/source.json` | `d45f5c76f5d8c596710b58e3d9bbed850bedf24b` | `47b41c5ed3c6416ef0ad49949bbbc8723ecdd567` | 1667 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/source-census.json` | `31e92d8e810ce6572300ed4331684caa59349551` | `c89c38e79d28b87beb7a1a063ad8dd2a8fac55f3` | 39924 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round4/tool-bootstrap.log` | `31e92d8e810ce6572300ed4331684caa59349551` | `d8b182d1e1cb00a97cb95b60e18ec32b7f2a11e5` | 986 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-output-inventory.json` | `0049877688f895bc4fb75e49cfe8911489b2a937` | `a55fb9a771ac3ce21ae56c9f434d237a70db656f` | 2538 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `bcedd844311b4ea22b228db7b882e22a440e56a6` | 100025 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/_5d9e09695ab3_2026-10-03_21_56_34.8913662/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `bcedd844311b4ea22b228db7b882e22a440e56a6` | 100025 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/build.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `0251dcff71c3a47c97b5497b2361cc6e02a03033` | 459655 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/git.txt` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `5f952532c318c43c28fff8796e38b6c7d7dbd25b` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/run.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `3c5a20cfab6d3fd67caa8f11a50e4cd0e1818005` | 1481 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/run.trx` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `7f8c5699e6e5675e9e2f3744e40791ae7962ecc7` | 3752 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-custody-base-20261003-215416-1909/source.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `1f5e197031ed382d8fa11acc392471d58261bf04` | 1553 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-ownership-base-20261003-223807-c764/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `dfa4a9cdecdd99accafdb708c33362fcfed3796a` | 100232 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-ownership-base-20261003-223807-c764/_5d9e09695ab3_2026-10-03_22_38_12.6290377/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `dfa4a9cdecdd99accafdb708c33362fcfed3796a` | 100232 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-ownership-base-20261003-223807-c764/git.txt` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `5f952532c318c43c28fff8796e38b6c7d7dbd25b` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-ownership-base-20261003-223807-c764/run.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `6808f9d5e1f4a7f891098b8ea7839fe195f692f6` | 1486 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-ownership-base-20261003-223807-c764/run.trx` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `30f5393263a1932ea2ff7415e647792da07e23d4` | 3851 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/base-proof/C1008-ownership-base-20261003-223807-c764/source.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `66ca6a1e6009ba0de32772d49268c6fcc0f29b83` | 1547 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/bench-http.json` | `c43bf296113e715c1087c7eae63782b7e229b73a` | `9a92cf7f6212330eedbc9474f0888b8de40683cd` | 1285 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/bench-http.ps1` | `c43bf296113e715c1087c7eae63782b7e229b73a` | `631f1c2a6683a69b48000345bee04754577b968f` | 2060 | `non_markdown` | 1 | Snapshot-style/source helper† |
| `.antiphon/c1008-evidence-round5/bin-c1008-final-unit-build-source.json` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `5304eafc986380051d2cf8de2cafeb61e74003b3` | 444 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/cache-inspection.json` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `3ff1bea0fea417a6090d45ae668ae82bd0f82a22` | 6728 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/census.json` | `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | `a5beeb36156039142262bae0a684801aee15a848` | 31530 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/checkpoint-lines.txt` | `0049877688f895bc4fb75e49cfe8911489b2a937` | `e7244404d56a5ddfdc04d543a5c68b9a914b2f12` | 4408 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/cp2-build.log` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `f615b54a500673ab6b10882d9064213e4191d81e` | 432456 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/cp2-method-timings.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `4dfb4aa9ef756b90a5200a66f2c5a403cee66c67` | 10874 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/cp4-build.log` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `831f241719af67f7383a63ac18da47fb0b3c53f0` | 429940 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/cp4-inspection.json` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `b296b3461fd56516053d5ba388910c9bd2c38866` | 672 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/custody-rerun-driver.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `67574d36ebbbf01db3b930d679278c00e7a7bf4a` | 2238 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/imported.yml` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `eb6f5ddf47178d14c932f20cc53c38cd039c86b7` | 3782 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/output-cleanup.json` | `0049877688f895bc4fb75e49cfe8911489b2a937` | `5014b00319ca8d0f98afd2c58b23c0df02834f0e` | 4979 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/output-inventory.json` | `0049877688f895bc4fb75e49cfe8911489b2a937` | `f632d13f41450d27e8b821240411c287cbce8bd7` | 2277 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/ownership-base-driver.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `24b234a18ff71507f3eb8be7c4eb23fd225dbab3` | 2263 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/ownership-current-driver.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `bf894e96e803e410a5dd20b32a8e2cfb44ace093` | 2257 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/pc-inspection-coordinates.json` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `38cde3dea5c08ac8a8af93e82307e31f3bfaa5c7` | 116967 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/C1008-ownership-current-20261003-223900-715b/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `79cdc2cf1b448b23c058d78ba21e7fa6a662dbe6` | 100120 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/C1008-ownership-current-20261003-223900-715b/_5d9e09695ab3_2026-10-03_22_39_06.9939074/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `79cdc2cf1b448b23c058d78ba21e7fa6a662dbe6` | 100120 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/C1008-ownership-current-20261003-223900-715b/git.txt` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `11aaee3ca9bad922223ae7632fba191549e7a4c6` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/C1008-ownership-current-20261003-223900-715b/run.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `17013951785d0e63c872666ee662fd29e1727d49` | 1473 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/C1008-ownership-current-20261003-223900-715b/run.trx` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `45fb20693fc95e0f44f48e80ef07c26023935a85` | 3722 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/C1008-ownership-current-20261003-223900-715b/source.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `3654050ac54978a6a780f62e7a5abd190cab624f` | 1554 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/CP-2-custody-rerun-20261003-223932-9aba/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `7bb00f5911d34159489b82cd1512793ac96bfe30` | 100013 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/CP-2-custody-rerun-20261003-223932-9aba/_5d9e09695ab3_2026-10-03_22_40_02.4710147/In/5d9e09695ab3/Antiphon.Tests-linux-net9.0-report.html` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `7bb00f5911d34159489b82cd1512793ac96bfe30` | 100013 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/CP-2-custody-rerun-20261003-223932-9aba/git.txt` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `11aaee3ca9bad922223ae7632fba191549e7a4c6` | 135 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/CP-2-custody-rerun-20261003-223932-9aba/run.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `d9cf44e561826d1c463263d4be72006cdb1a8838` | 1462 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/CP-2-custody-rerun-20261003-223932-9aba/run.trx` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `4a823d0afc992b90bf7e704e5c66e25d26831ff3` | 3726 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/targeted/CP-2-custody-rerun-20261003-223932-9aba/source.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `635bdcbf7f9ad2647e07bcf3009c4b2353c4a3c9` | 1547 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/unit-driver.log` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `48af8bbe715539a3976b335336efeb6be14d14b0` | 474885 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/unit-inspection.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `44e64a804bbe167c79b5f28b5d173d1c265e55ea` | 112439 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/unit/C1008-Final-Unit-20261003-215657-3d2c/run.trx` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `f43e21f9378c910d90fcb05cc88e6ddbe2c06f55` | 6012029 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/unit/C1008-Final-Unit-20261003-215657-3d2c/source.json` | `5609cb93ba5109d19044ad7da5bf2c96363c5132` | `560eab839eb6291d07f11ef78c8d8972afdba084` | 1502 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence-round5/validate-cp3.txt` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `bb3a75bc802bfa8986bce4f2ba72fc4657602a74` | 79 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/Options-null-red.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `de9df34cd7120482b07ef1795784066adb6b9345` | 679 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/Options-null-red.trx` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `38454a2e44af551de6a366765118181dabd8a557` | 5438 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/admission-CP-2.trx` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `1651c879ba598374b66dec6fccd357a620b0fde6` | 29931 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-final-wait.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `e402019e0b259f707faac480418e3b1c97b42cff` | 1603 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-driver.txt` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `0dfb900657d565e548f197403ea76eb6207ace6a` | 6060 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-evidence.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `f993cb4015e251c950be3edfaf739b173d0e33c2` | 14283 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-git-driver.txt` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `81f391189fccff12ecbfb022382a7d8c99e69315` | 4932 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-git-evidence.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `0e4c6eb54fc7f5f6b0fa0f8850e839ad4511e0fa` | 676 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-git.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `e53397967f1f301118c7be958ebcff8da4022e5f` | 307 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-host-driver.txt` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `6c065db274a26fd865c397bf8046520e5ecae409` | 8657 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-host-evidence.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `4617276117bbc66718b1f27ec5aa2ee4ceb7a60f` | 1450 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-host-final.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `ead84ddeae4831e0057b70cdb64b443d6e17dbfa` | 984 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-host-receipts.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `fb06cda15c6b08927b9a10ce5a504bf57d4a6eb0` | 14486 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-host-repair.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `2adb7d05c1c88fb75d62068a47031e5749a2209c` | 544 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual-host.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `11218791d8701bb1cbf0189358bafdf069f3ec2b` | 277 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/c1008-manual.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `a2160c4bf20c890e3f96445bdd8643eef269fca2` | 511 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/inherited-CP-1.trx` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `5cd399ea8907f627692de544bbe2e0d778f44eff` | 5612 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-CP-2-console.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `7fabe5ab85a243ae769936d57a5e07c7783e2307` | 1375 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-CP-2.trx` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `77e212961a10b6488771cc8a185a8d00c31ab9af` | 29931 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-CP-3-console.log` | `447f1d0f81cde0b10ca6d519f728bd3e967e4aae` | `7a5da3e78756aa9be55c46f94fe8a69c87b03971` | 1372 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-CP-3.trx` | `447f1d0f81cde0b10ca6d519f728bd3e967e4aae` | `30bcc7bb0389c71fec13dd14ad15760025864d87` | 105508 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-executor.log` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `2c65ab396d6432c9d3cec3df794839facbf590e2` | 1440 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-request.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `686203de3778a1fe1a9a4cb51d4b7076dd4ef7b3` | 1020 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/latest-state.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `3a9892b670c71b7aec26371aae8988552e04b977` | 7702 | `non_markdown` | 1 | Run-output |
| `.antiphon/c1008-evidence/output-cleanup.json` | `cf9881dd143804e114d7d68c54af10bd156ab704` | `efd832dbc349bbaaea66640d2a9d771c336cd882` | 4933 | `non_markdown` | 1 | Run-output |
| `.antiphon/c959-code-evidence/archive-files.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `120f1db4e7dcf59be4b5394f45e27d8c758fbd6f` | 3477 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-code-evidence/receipts.tar.gz` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `384a00309e789feedc93514cbde0e8c5fd292764` | 965338 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/bootstrap.log` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `f9d8db0e6bbaad81ade1e94b642c862511fec125` | 981 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/checksums.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `16fb03f2f8f67f948403957521479a65673f0cef` | 111 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/commits.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `a328029a601284ca29cc28f9ac9bf976eb23c19c` | 1666 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/coverage-explicit.log` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `1eec58d9bdf712a5c4fac45bf67555c2dd49adf2` | 546 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/coverage.log` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `1eec58d9bdf712a5c4fac45bf67555c2dd49adf2` | 546 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/import.yml` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `275ad796e014d3e1fcb7e0248530c1774bcebc4c` | 4306 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/output-cleanup.log` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `4e20cf5fd31ed6d3c9bf5517ff6b2e6ab1026ab9` | 11723 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/receipts.tar.gz` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `dbcc08690f2e22ee0c47d835a059b2203dcaa589` | 1521121 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/roster.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `b7ea3cf9db21befed0d04722cb4acc5f0e493da1` | 509081 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-cont-evidence/source-validate.log` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `67206b8c4614e850fec686f46ef6c8742c2b5747` | 79 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/archive.sha256` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `ebdaf08f47b210cd39c7bd497d192c4fcaa03067` | 89 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/checkpoint-lines.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `a184e1a000b5b99e79532babd4819b1d3a8dd314` | 7193 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/final-output-inventory.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `72db4bd1ba5d59db01b09eb3ee87cefd8942d30e` | 193 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/inspect-receipts.ps1` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `b9e011d600d7a79b09bf65bc58f4ae6dc7cf8386` | 1477 | `non_markdown` | 2 (includes merge) | Snapshot-style/source helper† |
| `.antiphon/c959-second-evidence/jq-prerequisite.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `248d18f6ff4a14a97c378e33f8e7283153e806a3` | 594 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/output-cleanup.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `5f3d3dc9a53c1842f2f452dab4a22707c40e7d4b` | 13959 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/raw-checkpoints.tar.gz` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `e7dc057a56023dc42b33d9406f6968097c335492` | 6934955 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/remaining-output-inventory.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `00ee0b6a5b0d7c49c4e15d2d079a4eb60cb4b0da` | 71 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/roster-inspection.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `b97d71f2c04f31e4f9d8db2af08a3318864bd08a` | 822 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/run-history.json` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `5b67fb50be5f11f3697989e7c295a7bac9aafeff` | 21505 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/source-validation.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `8e815bd6ce8bef2790a888e5cacc5368666fcec4` | 614 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/trx-counts.txt` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `c609b0e2af012991a74c3509cbf479c65b1f2cbb` | 717 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/trx-roster.tsv` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `ff30e695a84b721c9d977c608ad5e4492ec10d48` | 735802 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/c959-second-evidence/unit-windows-exclusions.tsv` | `0dff90f3ff3b3776f3e2794b3ac6d3a28bcd067f` | `bdf5bf42639a0222f2703c93180170145b328689` | 8449 | `non_markdown` | 2 (includes merge) | Run-output |
| `.antiphon/checkpoints/20261003-211642-32ac/report.json` | `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | `ce714925df90eddbf9d3fcb12e730fc68f22dc46` | 9599 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-211642-32ac/report.md` | `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | `ed6bb5ef9e4e4f3d725d2713d940b5fe8a5c9354` | 1955 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-211642-32ac/rows/CP-2/failures.md` | `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | `180a07fea7cc64521bcc6dc552bce0305ca94c05` | 1984 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-211642-32ac/rows/CP-2/run.trx` | `f53cd7ebb3db5bb64e30f3ba49be2cab950d954c` | `cca71bcaa69d53764e16b3cde67fbc1ed77fd74a` | 31728 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-224121-100a/report.json` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `bc37bf0368f9c9004ca60874462cbc5fdf96c67e` | 8763 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-224121-100a/report.md` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `486e37baa4035c2616c99096aa68071ef06c8332` | 1441 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-224121-100a/rows/CP-4/console.log` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `901cacebf64a384d806cbf59a10cd62be5a101b2` | 1352 | `checkpoint_directory` | 1 | Run-output |
| `.antiphon/checkpoints/20261003-224121-100a/rows/CP-4/run.trx` | `7baf2b7cd20094b0b85a302a196a5fc3a7d16fb9` | `3f2daa68c30ebe6acaba7dc8ad3e8b36eeb5956e` | 300522 | `checkpoint_directory` | 1 | Run-output |

--- next stage ---
next: plan
handoff: The 290-record failure is confirmed as imported prerequisite history plus first-parent merge repetition; a nonempty current-master re-cut is clean. Plan a valid successor baseline/landing route and an explicit snapshot exception consistent with operator policy, without exempting actual run output.
artifact: docs/investigations/2026-10-04-card-1022-evidence-guard.md
