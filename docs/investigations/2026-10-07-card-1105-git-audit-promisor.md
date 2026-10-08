# CARD-1105 recycle git audit on a blobless promisor checkout

The audit protects content in the worktree/index and every locally retained commit before volume removal. Promisor configuration alone is permitted. Every Git invocation exports GIT_NO_LAZY_FETCH=1, GIT_CONFIG_SYSTEM=/dev/null and GIT_CONFIG_GLOBAL=/dev/null; replacement objects and the commit-graph are disabled. An explicit current-origin advertisement is still required; tips are compared with the advertised heads present in the clone (repair 4). No production host was inspected during this repair.

D1: enumerate all refs in every namespace, main and linked-worktree HEADs (including bare detached HEAD), private worktree refs, and both old/new object IDs in every reflog entry of the common Git directory and of every linked worktree's own Git directory (repair 3 below: before it, only the common logs were read). Reflog-only amended/rebased tips count as recoverable work: abandonment cannot be established from a log, so unpublished entries refuse RecycleUnpublishedWork. Missing/malformed logs, unsupported reflog file types or tips refuse RecycleGitAuditUnknown. The actual deploy seed command leaves clone reflogs pointing at published HEAD; a fetch-only clone also passes. The offline seed/fetch-only fixtures establish this without a live rollout. RunnerWorkspaceService uses the same blobless/no-checkout flags; the supplied 390-repository replay reports zero shallow/locks, but does not prove those hosts' current reflog contents. Old private rewrite history may therefore require operator recovery/publication before recycling.

D2: absence of an index is accepted only when porcelain contains no changes except HEAD deletions, the worktree contains literally nothing outside its top-level .git, and refs/stash is absent. This is exactly the deploy no-checkout filesystem shape. A previously used checkout emptied to this shape has no index or worktree bytes left to lose; remaining refs/reflogs must independently prove publication. We deliberately accept that equivalent empty shape without claiming seed provenance. Any file (including ignored content or tracked content), subdirectory, stash, modification or staged content refuses dirty. The empty-HEAD plus ignored-file case detects the previous early-return hole; an empty tree is not authority to discard ignored bytes.

D3: porcelain alone is insufficient. Reject non-H ls-files -v entries (assume-unchanged, skip-worktree, sparse/unmerged states), then hash every regular file/symlink against its index blob ID without filters or stat-cache shortcuts. Status also rejects staged and intent-to-add changes. Non-file index modes refuse unknown. Raw hashing deliberately refuses normalized checkout differences that cannot be proved byte-identical. The racy-stat fixture preserves the old blob ID while making cached stat data match a changed same-size file and asserts ordinary status is empty before auditing.

D4: traverse all local/recovery and advertised origin roots with rev-list --objects --filter=blob:none --missing=error --stdin, without publication exclusions. Missing ancestor commits/trees refuse unknown, while omitted blobs are allowed. Every tip must peel to a commit; noncommit recovery refs cannot establish publication and refuse unknown. Grafts refuse; replacement refs cannot replace the graph. The subsequent rev-list --count proof still checks each tip against current origin heads, validates numeric output, and never converts Git failure to zero.

D5: the existing C1008SeedPromisor fixture now enables uploadpack.allowFilter/allowAnySHA1InWant on its local origin and asserts that the HEAD blob appears as missing in rev-list --objects --missing=print, with no lazy fetch. It still executes the unique actual deploy clone line. New helper contracts use that same command and verify no fetch child and all three required environment values at every traced Git start (repair 4 adds the five commit-graph keys; an origin-side local-transport git-upload-pack, which Git starts with per-repository configuration variables cleared, is exempt from those five only).

D6: the journal audit field retains the successful publication proof used by resume. A refusal writes auditFailure; a first refusal with no proof also initializes audit for backward-compatible diagnostics. Both contain check/status/redacted-relative-repo details without Git stderr. The actual audit wrapper and resume comparison are exercised through outage/restoration of a local origin. GithubTokenAbsent remains a warning; the audit does not read the token (secondary receive-pack probes use it).

The whole Unit lane is outside this explicitly commissioned closed scope; it is not deferred or claimed passed. No full-assembly run is needed: the change is bounded to the recycle script, its local Git fixtures, documentation and named script classes. No delivery/landing/lease/persistence service implementation changed.

## Ordinary invariants

| ID | Required proof | Selection |
|---|---|---|
| V-1 | Every retained recovery tip is published or refused | C1105_Git_audit_recovery_tips, all 10 variants |
| V-2 | Only empty no-index seed shape passes; content is retained; long-lived deploy clone passes | C1105_Git_audit_seed_content, all 11 variants |
| V-3 | Flags and stat shortcuts cannot conceal private content | C1105_Git_audit_index_content, all 5 variants |
| V-4 | Commit/tree completeness; blobs may be absent; a commit-graph cannot hide a missing commit | C1105_Git_audit_object_completeness, all 4 variants; seed positive |
| V-5 | Actual deploy fixture is truly blobless | C1008_Recycle_audits_promisor_checkout and each new local fixture |
| V-6 | Failure preserves saved proof; unchanged recovered origin resumes | C1105_Git_audit_resume_preserves_proof |
| V-7 | Every Git-directory location in the closed list: private commit hidden only there refuses with the stated class/check; published value passes; repair 4 shapes (present-origin comparison, admin-directory scan, AUTO_MERGE, antiphon/review-evidence, empty markers, editor files, ignored files) | C1105_Git_audit_hidden_locations, all 64 table rows |
| V-8 | A failure of only the per-tip `rev-list --count` refuses `check=rev-list status=128` | C1008_Recycle_audits_promisor_checkout (count-only block) |
| V-9 | Origin ahead of the clone or a deleted tracking ref is not a refusal; a tracking ref no origin head contains refuses unpublished | C1008_Recycle_refuses_uninspectable_git (tracking-deleted, origin-ahead, gone blocks) and the origin-* rows of V-7 |
| V-10 | Running out of the overall budget refuses `check=audit-timeout` | C1105_Git_audit_overall_timeout |
| V-11 | A replay-shaped volume (blobless mirror, 250 linked worktrees of 300 files, ignored build output) audits within 240 s; one pass per common directory, one content check per worktree | C1105_Git_audit_volume_scale |
| R-1 | Full commissioned adjacent regression | CP-1 through CP-12 |
| R-2 | No lazy fetch; required environment (including the commit-graph keys) at every Git start | all 97 C1105_Git_audit cases |
| R-3 | Dirty/unpublished/unknown classification, receipts, zero-count failures, UID 1654 | CP-11 existing 8 methods |

## Positive controls pending

All controls remain pending for method-scoped SourceLanding Mutation, including the earlier PC-1/2/3. The repair 3 and repair 4 red proofs below used mutated copies of the helper; repair 4 also ran three method-scoped TUnit mutants of the worktree script for the C1008 origin blocks, each restored with git checkout. None of them qualifies a PC. The method filter is /*/*/RemoteScriptContractTests/<exact method>; parameterized variants retain individual evidence.

| PC / variants | Detecting method | Deliberate defect for Mutation | Expected red |
|---|---|---|---|
| PC-1 | C1008_Recycle_audits_promisor_checkout | restore unconditional promisor refusal | published seed refused |
| PC-2 | C1008_Recycle_audits_promisor_checkout | turn failed count into zero (count line's `fail $?` fallback becomes `count=0`) | count-only block: `count-exit128` fault reaches the count after a successful traversal; removal happens |
| PC-3 | C1105_Git_audit_seed_content | drop GIT_NO_LAZY_FETCH export | per-Git environment/no-fetch proof fails |
| PC-D1 / stash,reflog,reflog-old,reflog-symlink,recovery,secondary,worktree-ref,linked-head,linked-private,bare-head | C1105_Git_audit_recovery_tips | omit the corresponding ref/reflog/HEAD source (isolate other retention roots) | private tip accepted |
| PC-D2 / seed-untracked,seed-tracked,seed-ignored,seed-empty-ignored,seed-stash,staged,modified | C1105_Git_audit_seed_content | bypass matching dirty/content/stash guard | content accepted |
| PC-D3 / assume,skip,intent,sparse,racy | C1105_Git_audit_index_content | bypass matching flag/content/status guard | hidden modification accepted |
| PC-D4 / missing-commit,missing-tree,missing-head-tree | C1105_Git_audit_object_completeness | omit complete graph validation (and HEAD status control where necessary) | absent required object accepted |
| PC-D5 / filter,clone-flags | C1008_Recycle_audits_promisor_checkout | disable origin filtering or change seed clone filter | real missing-blob/seed-command guard fails |
| PC-D6 | C1105_Git_audit_resume_preserves_proof | overwrite saved audit on refusal | proof differs and recovery cannot resume |
| PC-doc | DockerStackDocumentationTests (existing exact audit documentation method) | remove required audit contract sentence, or restore the false every-reflog sentence | documentation pins fail |
| PC-D7 / see the repair 3 and repair 4 mutation tables | C1105_Git_audit_hidden_locations | the mutation named for that row | private tip accepted, a false refusal, or a different refusal class/check |
| PC-R4-graph / missing-ancestor-graph | C1105_Git_audit_object_completeness | commit-graph left on (`GIT_CONFIG_VALUE_0=true`; Mutation must also relax the per-start environment proof, or remove only the export) | missing ancestor accepted |
| PC-R4-timeout | C1105_Git_audit_overall_timeout | drop the `124\|137` refusal | timed-out audit exits 124, not a refusal |
| PC-R4-scale / dedupe,common-once | C1105_Git_audit_volume_scale | remove `dirty_seen` / `common_seen` | status or ls-remote count, or budget, fails |
| PC-R4-origin / ahead,tracking,gone | C1008_Recycle_refuses_uninspectable_git | missing advertised head refuses / require a loose tracking ref / count check no-op | positive block refuses, or tracking-only commit removed |
| PC-doc-r4 | DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only | remove a repair 4 sentence or restore AUTO_MERGE to the pseudoref list | documentation pins fail |

Round 1 found two existing receipt-name assertions red because the completeness traversal named its logical purpose instead of its Git command. The corrected receipt names rev-list-objects; no assertion or timeout changed. The stopped row and rerun are retained in the task report.

Some variants are retained regressions already green at 4bfc7379; they are not claimed to detect a newly introduced defect. Baseline diagnostics record exact outcomes. D1/D2/D3/D4/D6 have independently reproduced baseline reds; D5's prior fixture shape is measured separately. Controls requiring more than one guard removal are explicitly variants for Mutation's missing-control discovery, not declared qualified by Code.

## Authorized final repair round

The caller authorized one additional round after the original two-round cap. The only production edit moves the existing current-origin advertisement proof before recovery-tip/worktree enumeration. Both checks remain fail-closed and byte-for-byte unchanged. The first failed check now determines the receipt: stale/deleted/missing origin tracking produces origin-advertisement before a broken-ref warning in recovery enumeration. The existing C1008_Recycle_refuses_uninspectable_git assertion is unchanged.

Run CP-2, CP-11, CP-7 and CP-12 (77 cases) on the new committed source, serially with one isolated build. CP-11 exercises the complete audit and receipt matrix; CP-7 includes real published/dirty/unpublished Git repositories through retirement; CP-12 is the expressly requested whole wrapper class; CP-2 is the required registry guard. CP-7/CP-12 remain last. No additional repair round is authorized if this run remains red.

The other eight rows retain their green evidence from run 20261007-193034-be09, source fff8dcd2eaece3ebb9c1dec8dcaf8093bd48f219 (96 cases). CP-1 pins unchanged documentation contract sentences; CP-3 pins seed/compose/deploy text outside the audit; CP-4 invokes compose/mount/generation helpers, not the Git audit; CP-5 uses wrapper/cleanup/bridge fixtures without a materialized Git repository; CP-6 exercises prerequisite admission and refuses before the Git audit; CP-8, CP-9 and CP-10 use empty-work Git inventories or refuse before the audit, so none enters the reordered per-repository checks. No assertion, timeout, filter, expected count, or fixture changed in this extra round. Whole Unit and deliberate PC mutants remain outside Code's commissioned scope.

## Repair 3: closed list of Git-directory locations

Final Review 2 found two more places an object ID could hide: a linked worktree's own reflog (the audit read only `--git-path logs`, which is the common directory) and the ORIG_HEAD/FETCH_HEAD pseudorefs. Rather than add those two, the helper now classifies every entry of the common Git directory and of every `worktrees/<id>` directory (`consider_gitdir`); an entry outside the list below refuses `RecycleGitAuditUnknown` (`check=gitdir-entry` or `gitdir-info`). Every worktree's own `rev-parse --absolute-git-dir` must be the common directory or a direct child of its `worktrees` directory (`check=git-dir-layout`), so no worktree Git directory escapes classification. The same list is in the helper comment.

| Location (main and every linked Git directory) | Can name | Audit | Table row(s) |
|---|---|---|---|
| HEAD | commit | raw file (oid or `ref: refs/...`) plus `rev-parse HEAD` per listed worktree: tip | head, linked-head |
| refs/** loose | any object | raw parse of every file plus `for-each-ref`: tip; non-file or unparseable: unknown | loose-ref, linked-ref |
| packed-refs | any object | raw parse (header, `oid name`, `^peeled`) plus `for-each-ref`: tip | packed-ref |
| logs/** | commits | both sides of every entry, common and each linked directory: tip; malformed or non-file: unknown | reflog, linked-reflog, linked-reset |
| ORIG_HEAD, FETCH_HEAD, REBASE_HEAD, BISECT_HEAD, MERGE_AUTOSTASH | commit | tip; missing object fails the complete-graph traversal (unknown) | orig-head, orig-head-missing, linked-orig-head, fetch-head, linked-fetch-head, rebase-head, bisect-head, merge-autostash |
| AUTO_MERGE (repair 4) | the conflicted result tree | not a tip. No operation marker in that Git directory: ignored when it names a tree or a missing object, a present non-tree refuses `auto-merge-type`; with MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, rebase-*, sequencer or BISECT_*: `lock-AUTO_MERGE` | auto-merge, auto-merge-commit, auto-merge-sequencer, auto-merge-in-progress |
| MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD | commits | interrupted operation: unknown (`lock-*`, existing early check plus every linked directory) | merge-head, linked-merge-head, cherry-pick-head, revert-head |
| rebase-merge/, rebase-apply/, sequencer/ | commits, autostash | interrupted operation: unknown | rebase-merge, rebase-apply, sequencer |
| BISECT_* other than BISECT_HEAD | commits | bisect in progress: unknown | bisect-state |
| NOTES_MERGE_* | commits | notes merge in progress: unknown | notes-merge |
| *.lock (top level and under refs) | new ref value | interrupted update: unknown | ref-lock |
| stash | refs/stash and logs/refs/stash | ref and reflog tips | stash |
| index (+ sharedindex.*) | staged blobs | proven checkout: status plus raw hash (D2/D3); no proven checkout: must equal its HEAD (`stale-index`, dirty); index in a bare directory: unknown | index, bare-index, stale-pointer-staged, pruned-path-staged |
| worktrees/<id> (repair 4) | a whole linked Git directory | scanned directly, whether or not the checkout exists; common directory must match (`git-dir-layout`); checkout inspected only when both pointers agree; registration outside the volume: `worktree-confine` | stale-linked-published, linked-outside, linked-* |
| antiphon/ (common only, repair 4) | product state, no object | ignored; non-empty `children/`: `antiphon-children`; in a linked directory: `gitdir-entry` | antiphon-state, antiphon-children, antiphon-linked |
| review-evidence/ (common only, repair 4) | agent evidence, no repository state | ignored, contents not audited; in a linked directory: `gitdir-entry` | review-evidence, review-evidence-linked |
| common/ and git-daemon-export-ok (common only, repair 4) | nothing when empty | empty: ignored; non-empty: `gitdir-common` / `gitdir-daemon-export` | common-empty, common-content, daemon-export |
| editor swap/backup (`.*.sw?`, `*~`, repair 4) | unrecovered text | `gitdir-editor`: recover or delete | editor-swap |
| info/grafts; refs/replace (loose or packed) | graph rewrite; replaced oid is only a refname | unknown | grafts, replace, replace-packed |
| refs/notes | notes commit naming objects by path | tip; never an origin head ancestor: unpublished | notes |
| reftable/ | refs and reflogs not in files | unknown (`ref-storage`) | reftable |
| modules/ | whole submodule repositories | unknown (`gitdir-modules`) | modules |
| shallow | boundary | unknown (existing `shallow`) | existing C1008 shallow case |
| info/ other than exclude, attributes, sparse-checkout, refs, empty grafts | unknown | unknown (`gitdir-info`) | info-entry |
| any other entry (for example lost-found) | unknown | unknown (`gitdir-entry`) | unknown-entry |
| objects/ | the store itself | an object no location above names is outside the audit, exactly as for `git gc`; it is never a tip | (none) |
| info/refs | repack's copy of refs | the refs it was copied from are read directly; a stale entry names an object no live location names (same as `objects/`) | published-gc, long-lived |
| config, config.worktree, description, hooks, branches, remotes, commondir, gitdir, locked, gc.log, gc.pid, COMMIT_EDITMSG, MERGE_MSG, MERGE_MODE, MERGE_RR, SQUASH_MSG, TAG_EDITMSG, NOTES_EDITMSG, EDIT_DESCRIPTION, BRANCH_DESCRIPTION, rr-cache, fsmonitor--daemon(.ipc) | no object ID | ignored | (none) |

The brief's "unless the object is a published commit" exception for a missing pseudoref object is vacuous: every commit reachable from an advertised origin head must already be present (cat-file and the complete traversal refuse otherwise), so a missing object cannot be shown to be published and always refuses unknown.

Positive rows: published-fetch-head (the deploy's `fetch --no-tags origin <branch>`), published-orig-head, published-linked (linked worktree reset between published commits), published-gc (gc writes info/refs and packed-refs), plus the seed_content `long-lived` variant: filtered fetches, a remote branch recorded by gc in info/refs and then pruned, the deploy fetch's FETCH_HEAD, reflogs with only clone/fetch entries, HEAD blob still absent.

PC-2 previously stayed green because the `exit128` fixture fault fails every `rev-list`, so the traversal refused first. The fake docker shim now has `count-exit128`, which fails only `rev-list --count`; the promisor test's count-only block requires `check=rev-list status=128` and no removal.

### Repair 3 baseline and mutation evidence

Measured with `node scripts/fixtures/c1105-git-audit.cjs <row> <script>` (the exact body of each `C1105_Git_audit_hidden_locations` case). At 039b26612437f035fd86310857e69185cb3c254e, 22 rows were accepted (audit exit 0): linked-reflog, linked-reset, linked-orig-head, linked-fetch-head, orig-head, orig-head-missing, fetch-head, rebase-head, bisect-head, auto-merge, merge-autostash, sequencer, bisect-state, notes-merge, ref-lock, replace, replace-packed, info-entry, unknown-entry, modules, reftable, bare-index. The other 15 refusal rows (head, loose-ref, packed-ref, reflog, stash, notes, index, linked-head, linked-ref, linked-merge-head, merge-head, cherry-pick-head, revert-head, rebase-merge, rebase-apply, grafts) and the 5 positive rows were already correct there; they are retained regressions, not claimed new detections.

| Mutation of the repaired helper (copy, not the worktree) | Rows red | Positive seed/long-lived |
|---|---|---|
| `logs)` skips linked directories | linked-reflog | green |
| `worktrees)` does not recurse | linked-reset, linked-reflog, linked-orig-head, linked-fetch-head | green |
| ORIG_HEAD moved from the tip arm to the ignored list | orig-head, linked-orig-head, orig-head-missing | green |
| FETCH_HEAD moved to ignored | fetch-head, linked-fetch-head | green |
| REBASE_HEAD / AUTO_MERGE / MERGE_AUTOSTASH moved to ignored | rebase-head / auto-merge / merge-autostash | green |
| BISECT_HEAD and BISECT_* moved to ignored | bisect-head, bisect-state | green |
| sequencer / BISECT_* / NOTES_MERGE_* removed from the interrupted-operation arm | sequencer / bisect-state / notes-merge | green |
| `*.lock` line in consider_refs removed | ref-lock (refuses unpublished instead of lock-ref) | green |
| loose / packed `refs/replace` refusal removed | replace / replace-packed | green |
| `gitdir-info` / `gitdir-entry` catch-all made a no-op | info-entry / unknown-entry | green |
| modules moved to ignored | modules | green |
| reftable check removed | reftable | green |
| bare-index check made a no-op | bare-index | green |

Method-scoped TUnit confirmation (isolated `bin-c1105r3mut/`, UseAppHost=false, every driver under `scripts/build-slot.ps1`, source restored with `git checkout` after each mutation): `C1105_Git_audit_hidden_locations*` with the 039b2661 `scripts/c590-remote.sh` checked out failed exactly those 22 rows (42 total, 20 passed). `C1008_Recycle_audits_promisor_checkout` with the count line's fallback mutated to `count=0` failed at the count-only block (`countOnly.Removed`, "a failed rev-list --count is not zero unpublished commits"); before this repair that mutant stayed green. `Main_volume_recycling_is_scripted_only` with the false every-reflog sentence restored failed on the new pins. Restored source: those methods passed 43/43 and 1/1.

The 15 retained refusal rows are each covered twice (raw parse and Git's own `for-each-ref`/`rev-parse`/early marker check); a Mutation variant must remove both to go red.

## Repair 4: replay false refusals and Final Review 3 defects

Sources: Final Review 3 (task c75cd4bc, five defects at c3b0381f) and the read-only replay of c3b0381f on the server2 temp runner volume (task 962f430d, `docs/investigations/2026-10-08-card-1105-audit-replay.md`: 396 entries, 9 pass, 387 refuse; all 1313 commit tips published; four false-refusal classes). No production host was contacted. Every node fixture and TUnit driver in this repair ran through `scripts/build-slot.ps1` (D5).

The helper keeps its contract: unpublished -> `RecycleUnpublishedWork`, dirty -> `RecycleWorktreeDirty`, unclassifiable -> `RecycleGitAuditUnknown`; a Git failure never becomes a zero count; promisor configuration alone is permitted; `GIT_NO_LAZY_FETCH=1` and `GIT_CONFIG_SYSTEM/GLOBAL=/dev/null` on every Git call; the receipt's `check/status/repo` with redacted relative paths and the D6 resume proof.

### What changed, per finding

| ID | Change |
|---|---|
| D1 | `<common>/worktrees/*` is scanned directly (`consider_linked`); `git worktree list` is no longer used. Each admin directory's own refs, HEAD, logs and pseudorefs are classified as before, its common directory must resolve to the audited one (`git-dir-layout`), and its HEAD/refs are read with `git --git-dir=<admin>`. Its checkout's files are inspected only when both pointers agree (`<id>/gitdir` names `<checkout>/.git` and that file's `gitdir:` resolves to `<id>`). Otherwise the index must equal that directory's HEAD (`git diff-index --cached --quiet HEAD`, `check=stale-index`, staged bytes refuse `RecycleWorktreeDirty`). A stale or prunable admin directory with a clean index passes. A registration outside the volume refuses `worktree-confine` (it may be live). The main checkout is inspected when `<checkout>/.git` is the common directory itself; a non-bare common directory with `core.worktree` set refuses `core-worktree`. |
| D2 | Decision: ignored files are not protected work. In a checkout with an index, `git status` omits files matched by the audited repository's own ignore rules (`.gitignore`, `info/exclude`, a `core.excludesFile` in its own config; system/global config are disabled), so build output passes. A file hidden by an ignore rule is accepted by design. Tracked files (raw bytes, even under an ignore pattern), staged entries and untracked non-ignored files stay protected; assume-unchanged/skip-worktree still refuse. The no-index seed shape keeps refusing any file, ignored or not. |
| D3 | Every Git call runs with `GIT_CONFIG_COUNT=2`, `core.commitGraph=false`, `gc.writeCommitGraph=false`, so the complete traversal opens every commit object. |
| D4 | Git-created metadata was inventoried against Git 2.47.3 (the pinned image Git) and the replay's real directories: the closed list already covered every name Git leaves in a long-lived clone or worktree except AUTO_MERGE (F-3); `addp-hunk-edit.diff` and `ADD_EDIT.patch` were measured not to persist. Review's three examples: an empty `common/` and an empty `git-daemon-export-ok` hold no bytes and pass (non-empty refuses `gitdir-common` / `gitdir-daemon-export`); an editor swap or backup (`.*.sw?`, `*~`) is not Git metadata and may hold unrecovered text, so it refuses `gitdir-editor` until recovered or deleted. |
| D5 | All drivers gated; the run list is in the task report. |
| F-1 | Each local tip is compared only with advertised origin heads present locally (`cat-file --batch-check`): a missing head is skipped, a present non-commit head refuses `origin-type`, no present head refuses `origin-present`. The exact `refs/remotes/origin` equality (`origin-advertisement`) and the bare `bare-ref` equality are removed: tracking refs and bare branches are tips, proven like every other tip. The per-tip `rev-list --count` is one `rev-list --count --stdin` over all tips with every present head excluded; any nonzero count refuses unpublished. |
| F-2 | `antiphon/` at the common level is product state and ignored, except a non-empty `antiphon/children/` refuses `antiphon-children`. Deviation from the brief: `landing.lock` existence does not refuse. RepositoryMutationLease and RunnerWorkspaceParkService open it with `FileMode.OpenOrCreate` and never unlink it (`scripts/recover-repository-children.ps1`: "its open handle, not its existence, owns exclusion"), so every mirror that ever landed or parked has it; refusing on existence would strand every long-lived mirror. The repeated audit after the runner stops (no container holds the volume) must equal the first. `review-evidence/` at the common level is agent evidence, not repository state, and is ignored. Both refuse `gitdir-entry` in a linked admin directory. |
| F-3 | AUTO_MERGE is not a tip. With no MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, rebase-merge, rebase-apply, sequencer or BISECT_* in the same Git directory it is ignored when it names a tree or a missing object; a present non-tree refuses `auto-merge-type`; with an operation marker it refuses `lock-AUTO_MERGE`. |
| F-4 | Entries run their own checks (common/git dir, layout, markers, shallow, promisor, bareness) and queue their checkout; each common directory is then audited once for all its entries. Each worktree path's content is checked once (`dirty_seen`), up to `min(nproc, 8)` in parallel, results read in queue order. Raw hashing is one `git hash-object --no-filters --stdin-paths` per worktree (names with a newline or CR are hashed alone). The whole program runs under `timeout --kill-after=10s 1800s`; 124/137 refuses `check=audit-timeout`. |
| F-5 (found here) | The tip lines no longer carry a hash of the live origin advertisement: the recycle compares the audit before and after stopping the runner, and the replay measured origin changing minutes apart, so that hash would refuse a normal rollout. Lines are now `entry repo=<sha256 of path> common=<sha256>` per entry and `tip=<oid> common=<sha256>` per common directory; each audit still proves every tip against the live advertisement. |

### Performance (F-4)

- Real-size worktree (this checkout, 7165 tracked files): `consider_dirty` took 2.23 s on server2-temp; the replay measured 27 s per such worktree with one `hash-object` per file.
- `C1105_Git_audit_volume_scale`: one blobless mirror plus 250 linked worktrees, 300 tracked files each, ignored `bin-*`/`obj` output in each; the whole-volume audit took 22.3 s (fixture run, server2-temp), assertion `< 240 s`. The same run asserts exactly one `ls-remote`, 251 `status` and 250 `--stdin-paths` hashes.
- Budget: 1800 s. Estimate for the old volume (~390 entries, ~400 worktrees at 2.2 s, 8 at a time): about 2 minutes plus the common pass (the replay's full traversal finished within its 30 s bound; measured here 2.5 s with the commit-graph off).

### New and changed cases

Changed (the only weakened or removed expectations, both encoding removed behaviour):
- `C1008_Recycle_refuses_uninspectable_git`: `deleted`/`missing` (a deleted `refs/remotes/origin/master`) expected `origin-advertisement`; they now pass by design and moved to a positive block (`tracking-deleted`, `origin-ahead`, both must reach removal). `stale` (a zero tracking ref) still refuses unknown, now at `check=for-each-ref`. Added a `gone` block: a tracking ref kept for a branch deleted on origin refuses `RecycleUnpublishedWork`.
- `auto-merge` hidden row: was a private commit in AUTO_MERGE expecting unpublished; now the real shape (a conflict tree) passing. `auto-merge-commit` keeps a commit there refusing (`auto-merge-type`).
- Documentation pin: the pseudoref list no longer names AUTO_MERGE; new pins below.

Baseline (c3b0381f helper, fixture with the new commit-graph env assertion relaxed so each row's own outcome shows; gated run):

| Case | Expect now | c3b0381f outcome |
|---|---|---|
| auto-merge | pass | refuse `tip-commit` |
| auto-merge-commit | K `auto-merge-type` | U |
| auto-merge-sequencer | K `lock-AUTO_MERGE` | K `lock-sequencer` |
| origin-ahead | pass | K `cat-file` |
| origin-tracking-deleted | U | K `origin-advertisement` |
| origin-none-present | K `origin-present` | K `cat-file` |
| origin-non-commit | K `origin-type` | K `cat-file` |
| stale-pointer-staged (Review D1 shape) | D `stale-index` | pass |
| pruned-path-staged | D `stale-index` | K `worktree-path` |
| stale-linked-published | pass | K `worktree-path` |
| antiphon-state | pass | K `gitdir-entry` |
| antiphon-children | K `antiphon-children` | K `gitdir-entry` |
| review-evidence | pass | K `gitdir-entry` |
| common-empty, daemon-export | pass | K `gitdir-entry` |
| common-content | K `gitdir-common` | K `gitdir-entry` |
| editor-swap | K `gitdir-editor` | K `gitdir-entry` |
| missing-ancestor-graph (Review D3 shape) | K `rev-list-objects` | pass |
| audit-timeout | K `audit-timeout` | no budget (fixture assertion) |
| volume-scale | pass, one pass per common dir | refuse (`origin-advertisement`) |
| C1008 tracking-deleted / origin-ahead / gone | pass / pass / U | see TUnit baseline in the task report |

Retained-behaviour controls, green at c3b0381f by nature (not claimed as new detections): auto-merge-in-progress (`lock-MERGE_HEAD`), linked-outside (`worktree-confine`), ignored-build-output and ignored-tracked-modified (the D2 decision pin and its tracked-content control), antiphon-linked and review-evidence-linked (`gitdir-entry`).

Mutation matrix (copies of the repaired helper, never the worktree; one gated driver run, lease b498fbeb, 628 s; each mutant also ran `seed` and `long-lived`, all green):

| Mutated production line | Case(s) red |
|---|---|
| `missing)` advertised head refuses instead of being skipped | origin-ahead |
| present non-commit head ignored (`origin-type` arm no-op) | origin-non-commit |
| zero-comparison guard removed | origin-none-present (refuses unpublished instead of `origin-present`) |
| count check no-op | origin-tracking-deleted |
| admin-directory loop no-op (`consider_linked`) | stale-pointer-staged, pruned-path-staged |
| `consider_stale_index` returns early | stale-pointer-staged, pruned-path-staged |
| one-direction pointer trust (recorded path taken as checkout) | stale-pointer-staged |
| `worktree-confine` line removed | linked-outside |
| `GIT_CONFIG_VALUE_0=true` (commit-graph on; env assertion relaxed copy) | missing-ancestor-graph |
| AUTO_MERGE back to a pseudoref tip | auto-merge |
| AUTO_MERGE operation-marker check removed | auto-merge-sequencer (`lock-sequencer` instead) |
| AUTO_MERGE type check removed | auto-merge-commit |
| `antiphon)` arm refuses | antiphon-state |
| `antiphon/children` pending check no-op | antiphon-children |
| `antiphon)` level check removed | antiphon-linked |
| `review-evidence)` arm refuses / level check removed | review-evidence / review-evidence-linked |
| `common)` arm removed / held check no-op | common-empty / common-content |
| `git-daemon-export-ok)` arm removed | daemon-export |
| editor arm removed | editor-swap (`gitdir-entry` instead) |
| `124\|137)` timeout refusal removed | audit-timeout |
| `dirty_seen` dedupe removed | volume-scale (status count) |
| `common_seen` check removed (common pass per entry) | volume-scale |
| `status --ignored` | ignored-build-output |

Method-scoped TUnit confirmation (`bin-c1105r4/`, every driver gated): with the c3b0381f `scripts/c590-remote.sh` and `docs/docker-stack.md` checked out, `C1008_Recycle_refuses_uninspectable_git`, the four `C1105_Git_audit_object_completeness` variants and `C1105_Git_audit_overall_timeout` failed 6/6 and `Main_volume_recycling_is_scripted_only` failed 1/1 (the old completeness rows fail there on the new commit-graph environment assertion, which is intended). Worktree-script mutants, restored with `git checkout` after each: `missing)` refusing failed `C1008_Recycle_refuses_uninspectable_git` at "published clone with origin-ahead"; requiring a loose `refs/remotes/origin/master` failed it at the `tracking-deleted` block (`Removed.Length` 3); the count check no-op failed it at "a tracking ref no origin head contains".

## Checkpoints

Closed list: one isolated test build, UseAppHost=false, serial drivers. CP-11 has 105 cases (8 existing methods + 97 C1105_Git_audit cases: 10 recovery, 11 seed, 5 index, 4 completeness, 1 resume, 64 hidden-location rows, 1 overall timeout, 1 volume scale). The slow CP-7/CP-9/CP-12 run last. Existing timeout estimates are unchanged. Expected total: 241 cases (repair 3 added the 42 hidden-location rows and the long-lived seed variant; repair 4 adds 22 hidden-location rows, the missing-ancestor-graph variant and two methods; the C1008 origin blocks are inside an existing method). Measured previous 12-row wall was 74m36s; allow roughly 80 minutes plus implementation and baseline diagnostics.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | promisor | `tests/Antiphon.Tests -> bin-c1105-promisor/` | docs-pins | `/*/*/DockerStackDocumentationTests/*` | doc pins | 14 methods, 0 failed | 14 | 15 | true |
| CP-2 | promisor | CP-1 | registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry guard | 3 methods, 0 failed | 3 | 3 | true |
| CP-3 | promisor | CP-1 | compose-text-pins | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)\|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*)` | compose text pins | 6 methods, 0 failed | 6 | 4 | true |
| CP-11 | promisor | CP-1 | remote-git-audit | `/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_audits_promisor_checkout*)\|(C1105_Git_audit_*)` | C1008/C1087 promisor audit | 16 methods / 105 cases, 0 failed | 105 | 15 | true |
| CP-4 | promisor | CP-1 | mount-class | `/*/*/RollingProductionMountTests/*` | class regression | 12 methods, 0 failed | 12 | 10 | true |
| CP-5 | promisor | CP-1 | retired-temp-script | `/*/*/RetiredTempContainerScriptTests/*` | class regression | 13 methods, 0 failed | 13 | 10 | true |
| CP-6 | promisor | CP-1 | host-jq | `/*/*/HostJqPrerequisiteScriptTests/*` | class regression | 39 methods, 0 failed | 39 | 15 | true |
| CP-8 | promisor | CP-1 | remote-generation | `/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Generation_identity_refuses_mismatch*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_dry_run_never_mutates*)` | C1008/C1105 | 5 methods, 0 failed | 5 | 15 | true |
| CP-10 | promisor | CP-1 | remote-resume | `/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)\|(C1105_Resume_identity_refusals*)\|(C1105_Resume_state_init_image_follows_the_journal*)\|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*)` | C1105 | 4 methods, 0 failed | 4 | 12 | true |
| CP-7 | promisor | CP-1 | retired-temp-host | `/*/*/RetiredTempContainerHostTests/*` | class regression | 21 methods, 0 failed | 21 | 15 | true |
| CP-9 | promisor | CP-1 | remote-census-long | `/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Recycle_refuses_references_and_unknown_census*)` | C1008 | 3 methods, 0 failed | 3 | 15 | true |
| CP-12 | promisor | CP-1 | recycle-wrapper | `/*/*/RollingVolumeRecycleScriptTests/*` | class regression | 16 methods, 0 failed | 16 | 15 | true |
