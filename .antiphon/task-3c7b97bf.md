# CARD-1594 Final Review round 2 (Interim scope): cross-SHA retire-temp image

Subject task abf772f4-d09b-4b4b-8a31-5fd2271681b9 (continues 79ea4e60-f6b6-44ec-bd44-252a050386a0),
branch feat/card-task-abf772f4. Code tip 1238e6a815a40d1164527ddbb5e09cb143211d71. Branch tip
101d694b5148b415347024e18945c345373098f6 adds only `.antiphon/task-abf772f4.md`. Round-2 range is
b26804183..1238e6a81 (179dfa41 red, 6300c4f2, ca7921ca, 4a0656c1, 1238e6a8 docs). Base rc
4563e52529b23052856cb0f1e0d01b383cf7590f.

Verdict: CLEAN for round 2. I found no fail-open and no regression that is new in this range or
left open by round 1. Round 1's D-1, D-2 and D-3 are fixed. Scope is Interim, not Full (see Runs).

## Priority 1: ignored receipts and callers

There are three callers of `c994_lookup_image` (git grep; no other script calls it):
- `c1008_recycle` dry-run, which is the retire-temp preview: `c994_lookup_image "$op" allow-preview audit || c1008_refuse RecycleGitAuditUnknown`.
- `c1008_recycle` apply: `c994_lookup_image "$op" audit || c1008_refuse ...`. After either call, an
  empty `C994_IMAGE` with `C994_LOOKUP` set to anything other than `unpinned` (that is, `image-absent`)
  refuses `RecycleGitAuditUnknown` before `c1008_bind_generation`, the lock, or any helper.
- `case_retire_temp_containers`: `c994_lookup_image || c994_refuse TempContainerReceiptUnavailable`
  (non-audit). The positional `"${C1008_CLEANUP_OPERATION:-}"` is quoted, so an empty operation
  still leaves `audit` in `$2` or `$3`.

What an ignored receipt does:
- A receipt for a different project, project id or retirement only ever leads to `continue`. In
  the result, an ignored receipt is the same as having no receipt: `unpinned`.
- With no operation named, that is byte-for-byte base behaviour: base skipped non-matching records
  with `[ -z "$operation" ] && continue`.
- On apply, `unpinned` does not skip the audit. At l.8187 the image falls back to the session-runner
  container image or the current-SHA image. `c1008_audit_checked` and `c1008_audit` run as in base
  (identical bytes) and refuse when that image is absent.
- An ignored receipt cannot unset a binding, and it cannot cancel `saw_empty`, `cross_conflict` or
  `image-absent` from a matching receipt. So it cannot convert a refusal into a pass.

What "operation named" means:
- It is `C1008_CLEANUP_OPERATION`, from the context's `cleanupOperationId` (deploy-server2.ps1:1333,
  c590-real.ps1:366/527).
- deploy-server2 names only the operation it has just created with `Invoke-TempContainerCleanup`. That
  receipt is a fresh, same-SHA receipt whose identity and SHA were validated by the wrapper. Resume
  passes no cleanup operation and skips the lookup. So the deploy path cannot pick a stale named file.
- A c590-real manifest can name any operation. A named file whose own identity mismatches returns 2.
  The witness `C1594_Older_retirement_receipt_is_ignored` covers the ignored case. The named-mismatch
  rule is unchanged from b268.

## Priority 2: empty image

On the audit lookup, an empty, settled, eligible matching receipt refuses when nothing else binds.
I found no path where apply proceeds with an empty pinned image. The possible outcomes are:
- `bound`, with an image that is present by id;
- `image-absent`, which refuses;
- `unpinned`, which audits with the current or container image;
- return 2, which refuses.

In cleanup, an empty `C994_IMAGE` is only recorded in the receipt. That is the base behaviour the
round 1 fix asked to restore.

## Priority 3: two other-SHA images for the same retirement

Two such receipts can only differ if a later cleanup saw the temp's session-runner container with
a different image after the earlier receipt's image became image-absent. That requires the temp to
be re-created under the same retirement stamp, which is pathological. Otherwise the cleanup's own
check `[ "$C994_IMAGE" = "$image" ] || refuse` stops it.

The refusal is fail-closed and symmetric with the same-SHA rule that base already had. It is
documented. It is not a liveness regression on the deploy path.

## Priority 4: round-1 clean checks re-held at the tip

- `c1008_audit`, `c1008_audit_readonly`, `c1008_audit_checked` and `c994_exact_absent` are byte-identical
  to base.
- No diff line touches `docker run`, `docker create`, `--user` or `--entrypoint`.
- The identity check is still exact `jq` string equality.
- The `sha256` regex is applied before any record is bound.
- `docker image inspect -f {{.Id}}` must equal the candidate.
- The same-SHA branch is unchanged apart from moving the `src` read earlier.

## Advisories (not blocking)

- A-1: A NAMED cross-SHA receipt that is partial, refused, pending or a dry-run now falls through
  to `unpinned`. For an empty image, b268 refused (`saw_empty`); base refused any named cross-SHA
  file. The deploy path never names a cross-SHA file, and `unpinned` still audits. Consider having a
  named ineligible cross-SHA file return 2.
- A-2: The docs say a cross-SHA image disagreement refuses `RecycleGitAuditUnknown`. On
  `retire-temp-containers` the same lookup refuses `TempContainerReceiptUnavailable`. The docs give no
  recovery for a permanent disagreement or for an empty-only receipt. In both cases the operator has
  to quarantine a receipt by hand.
- A-3: An absent temp with no image-bearing receipt still makes retire-temp-runner refuse
  (the audit `saw_empty`), whereas base audited it as unpinned. This is round-1 design, which round 1
  endorsed and the docs now state. In the deploy path the fresh cleanup receipt always matches, so
  `C994_IMAGE_UNPINNED` is reachable only from direct c590-real calls.
- A-4: The new C1594 witnesses exercise the preview only. Apply shares the lookup but has no
  apply-mode older-retirement witness.

## Runs

One build-slot build, `UseAppHost=false`, `OutputPath=bin-r3c7b/`, in a detached scratch worktree at
1238e6a81: succeeded in 3m32s. Then a build-slot `dotnet exec` of the built DLL for each run below.

| Run | Result |
|---|---|
| RollingVolumeRecycleScriptTests C1594* | 3/3 passed |
| RetiredTempContainerHostTests (C994_Older*)\|(C994_Absent*)\|(C994_Image*)\|(C994_Audit*) | 4/4 passed |
| Red check: scratch `scripts/c590-remote.sh` replaced by the 179dfa41 copy (= b268), then restored with `git checkout` (status clean) | C1594_Older, C1594_Disagreeing, C994_Older and C994_Absent: 4/4 FAILED, each on its exit-code assertion (expected 2 got 0; expected 0 got 2 x3). The witnesses are real. |
| DockerStackDocumentationTests | 15/15 passed |
| RemoteScriptContractTests (co-run with the docs class) | Did not complete. `C1008_Recycle_receipt_records_disk_and_partial_failure` failed at 2m21s (`df-argv` not found), with host load ~12 from other tenants. Fail-fast stopped the run, and it hit my 590 s timeout. Rerun alone: 1/1 passed. This was environmental. |
| RetiredTempContainerHostTests + RetiredTempContainerScriptTests, whole classes together | Hit the 590 s timeout with no verdicts (~40 s per test). |
| check-evidence-diff.ps1 4563e525..1238e6a8 | commits=7 entries=0 violations=0 |

Not run (Interim):
- the whole Unit lane;
- the whole RemoteScriptContractTests (499);
- the rest of RetiredTempContainerHostTests (15) and all of RetiredTempContainerScriptTests (16);
- the whole RollingVolumeRecycleScriptTests;
- CheckpointImportTests and RollingProductionMountTests;
- the real-Docker classes.

## Leftovers (the brief said delete nothing)

- Detached worktree `/tmp/claude-1654/-work-worktrees-task-3c7b97bf/64f08383-9ea8-4525-a7fd-e0a936e374f8/scratchpad/subject`,
  which contains 28 `bin-r3c7b` output directories.
