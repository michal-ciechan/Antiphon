# CARD-1105 recycle git audit on a blobless promisor checkout

The audit protects content in the worktree/index and every locally retained commit before volume removal. Promisor configuration alone is permitted. Every Git invocation exports GIT_NO_LAZY_FETCH=1, GIT_CONFIG_SYSTEM=/dev/null and GIT_CONFIG_GLOBAL=/dev/null; replacement objects and the commit-graph are disabled. An explicit current-origin advertisement is still required. Repair 6 compares tips with origin's real heads, fetched without blobs into a scratch proof repository, plus the advertised heads present in the clone; if that fetch fails, only the present heads count (the repair 4 rule). No production host was inspected during this repair.

D1: enumerate all refs in every namespace, main and linked-worktree HEADs (including bare detached HEAD), private worktree refs, and both old/new object IDs in every reflog entry of the common Git directory and of every linked worktree's own Git directory (repair 3 below: before it, only the common logs were read). Reflog-only amended/rebased tips count as recoverable work: abandonment cannot be established from a log, so unpublished entries refuse RecycleUnpublishedWork. Missing/malformed logs, unsupported reflog file types or tips refuse RecycleGitAuditUnknown. The actual deploy seed command leaves clone reflogs pointing at published HEAD; a fetch-only clone also passes. The offline seed/fetch-only fixtures establish this without a live rollout. RunnerWorkspaceService uses the same blobless/no-checkout flags; the supplied 390-repository replay reports zero shallow/locks, but does not prove those hosts' current reflog contents. Old private rewrite history may therefore require operator recovery/publication before recycling.

D2: absence of an index is accepted only when porcelain contains no changes except HEAD deletions, the worktree contains literally nothing outside its top-level .git, and refs/stash is absent. This is exactly the deploy no-checkout filesystem shape. A previously used checkout emptied to this shape has no index or worktree bytes left to lose; remaining refs/reflogs must independently prove publication. We deliberately accept that equivalent empty shape without claiming seed provenance. Any file (including ignored content or tracked content), subdirectory, stash, modification or staged content refuses dirty. The empty-HEAD plus ignored-file case detects the previous early-return hole; an empty tree is not authority to discard ignored bytes.

D3: porcelain alone is insufficient. Reject non-H ls-files -v entries (assume-unchanged, skip-worktree, sparse/unmerged states), then hash every regular file/symlink against its index blob ID without filters or stat-cache shortcuts. Status also rejects staged and intent-to-add changes. Non-file index modes refuse unknown. Raw hashing deliberately refuses normalized checkout differences that cannot be proved byte-identical. The racy-stat fixture preserves the old blob ID while making cached stat data match a changed same-size file and asserts ordinary status is empty before auditing.

D4: traverse all local/recovery and advertised origin roots with rev-list --objects --filter=blob:none --missing=error --stdin, without publication exclusions. Missing ancestor commits/trees refuse unknown, while omitted blobs are allowed. Repair 5 replaces the old "every tip must peel to a commit" rule: a non-commit tip must be the very object origin advertises (see the repair 5 section). Grafts refuse; replacement refs cannot replace the graph. The subsequent rev-list --count proof still checks each tip against current origin heads, validates numeric output, and never converts Git failure to zero.

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
| V-7 | Every Git-directory location in the closed list: private commit hidden only there refuses with the stated class/check; published value passes; repair 4 shapes (present-origin comparison, admin-directory scan, AUTO_MERGE, antiphon/review-evidence, empty markers, editor files, ignored files); repair 5 shapes (V-12 to V-15) | C1105_Git_audit_hidden_locations, every table row (132 at repair 8: 93 through repair 5, 16 added by repair 6, 13 by repair 7, 10 by repair 8) |
| V-8 | A failure of only the per-tip `rev-list --count` refuses `check=rev-list status=128` | C1008_Recycle_audits_promisor_checkout (count-only block) |
| V-9 | Origin ahead of the clone or a deleted tracking ref is not a refusal; a tracking ref no origin head contains refuses unpublished | C1008_Recycle_refuses_uninspectable_git (tracking-deleted, origin-ahead, gone blocks) and the origin-* rows of V-7 (origin-none-present now with the proof fetch failing; see V-17) |
| V-10 | Running out of the overall budget (3000 s since repair 7) refuses `check=audit-timeout` | C1105_Git_audit_overall_timeout (its anchor is the exact 3000 s budget line) |
| V-11 | A replay-shaped volume (blobless mirror, 250 linked worktrees of 300 files, ignored build output) audits within 240 s; one pass per common directory, one content check per worktree | C1105_Git_audit_volume_scale |
| V-12 | An existing recorded checkout is inspected against its admin directory when its `.git` pointer is missing or names another directory; clean or ignored-only content passes; an absent checkout keeps the index-equals-HEAD rule | hidden rows orphan-missing-modified, orphan-missing-untracked, orphan-elsewhere-modified, orphan-missing-clean, orphan-missing-ignored, orphan-symlink-ignored, plus stale-linked-published, pruned-path-staged, stale-pointer-staged |
| V-13 | A non-commit tip is published only as the very advertised object (a tree also when a present head reaches it); an advertised tag's commit is still proven against the heads | hidden rows tag-annotated, tag-annotated-packed, tag-annotated-published, tag-of-tag, tag-of-tag-published, tag-lightweight, tag-tree, tree-ref-published, tree-ref-unpublished, blob-ref, tag-published-off-branch |
| V-14 | Ignored output never refuses and never changes the receipt; symlinks in a checkout are its content and never followed; metadata and loose symlinks keep volume confinement | hidden rows ignored-head-equality, bare-reflog, damaged-git-dir-ignored, ignored-symlink-outside, ignored-symlink-dangling, untracked-symlink, tracked-symlink-outside, metadata-symlink-outside, loose-symlink-dangling; C1008_Recycle_refuses_uninspectable_git (escaping-link) |
| V-15 | Content Git never shows or a work tree moved by configuration refuses unknown | hidden rows dot-git-content, linked-core-worktree, bare-dot-git |
| V-16 | A checkout the runner registered under its /tmp is read in the read-only runner-tmp mount: clean passes, untracked/modified refuse dirty (named `/tmp/<id>`), an orphan is inspected against its admin directory, an absent one keeps index-equals-HEAD; with no tmp mount, outside every mounted volume, or resolving outside the mount through a symlink it refuses `worktree-confine` | hidden rows tmp-linked-clean, tmp-linked-untracked, tmp-linked-modified, tmp-linked-orphan-untracked, tmp-linked-absent-clean, tmp-linked-absent-staged, tmp-unmounted, tmp-mounted-outside, tmp-escape, linked-outside; C1008_Recycle_audits_work_as_1654 (create arguments) |
| V-17 | Publication is proven against origin's real heads fetched blobless into a scratch repository; a failed or timed-out fetch falls back to present heads (more refusals); no comparison head refuses unknown; the only fetch is that one, never lazy, never into the audited volume | hidden rows origin-none-present-fetched, origin-stale-master, origin-stale-master-fetch-failed, origin-stale-master-fetch-timeout, origin-none-present, origin-fetched-unpublished; the per-start fetch assertion in every C1105_Git_audit case; volume-scale (one fetch per common directory) |
| V-18 | The audited volume is never written: read-only bind mount and an unchanged metadata snapshot | hidden rows origin-stale-master-readonly, origin-stale-master |
| V-19 | Replay 3b (branches origin deleted) still refuses; the refusal receipt counts `gone-tracking` and `unadvertised-local` with `proof=` and `commits`/`tips` | hidden rows origin-gone-counts, origin-tracking-deleted, origin-fetched-unpublished |
| V-20 | Repair 7: a registered tmp checkout's path is translated to the mount before it is resolved: a `..` in the recorded path, a symlink target outside the runner's /tmp, or a target climbing above the mount refuses `worktree-confine`; a relative target, or an absolute one under the runner's /tmp, is followed in the mount; inside the checkout every `.git` entry and Git-shaped `HEAD` directory is classified as under /work (only another registered checkout's own pointer passes); ignored output and a registered checkout nested in it pass | hidden rows tmp-nested-dot-git, tmp-nested-dot-git-file, tmp-nested-pointer-unregistered, tmp-nested-git-shaped, tmp-dot-git-symlink, tmp-linked-ignored, tmp-nested-registered, tmp-symlink-dotdot, tmp-symlink-relative, tmp-symlink-absolute, tmp-symlink-climb |
| V-21 | Repair 7: all proof fetches share a 600 s allowance besides the 300 s cap each; once spent no fetch starts and the present heads decide (more refusals) | hidden rows origin-fetch-allowance-spent, origin-fetch-allowance-shared |
| V-22 | Repair 8: a symlink target or recorded worktree path is read byte for byte (a sentinel after the output, the command status kept, a NUL detected by length); one holding a newline, CR or other control character refuses unknown, never normalized; clean links and names with spaces, a leading dash, glob characters or a backslash resolve exactly | hidden rows tmp-link-target-newline, tmp-link-target-newline-last, tmp-link-target-cr, tmp-recorded-nul, tmp-recorded-cr, linked-recorded-nul-orphan, linked-link-target-newline-orphan, tmp-link-clean, tmp-space-path, tmp-space-path-untracked; existing tmp-symlink-relative and tmp-linked-ignored |
| R-1 | Full commissioned adjacent regression | CP-1 through CP-12 |
| R-2 | No lazy fetch; required environment (including the commit-graph keys) at every Git start | all 165 C1105_Git_audit cases |
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
| PC-R5 / one variant per row of the repair 5 mutation table | C1105_Git_audit_hidden_locations | the mutation named for that row | the named rows go red |
| PC-doc-r5 | DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only | remove a repair 5 sentence, or restore "Noncommit tips, unsupported index modes and grafts refuse unknown" | documentation pins fail |
| PC-R6 / one variant per row of the repair 6 mutation table (M1-M14) | C1105_Git_audit_hidden_locations | the mutation named for that row | the named rows go red |
| PC-R6-create | C1008_Recycle_audits_work_as_1654 | drop the runner-tmp mount, its C1008_TMP_MOUNT env or the scratch tmpfs from c1008_audit | create-argument assertion fails |
| PC-doc-r6 | DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only | remove a repair 6 sentence, or restore the present-only sentence | documentation pins fail |
| PC-R7 / one variant per row of the repair 7 mutation table (M1-M12) | C1105_Git_audit_hidden_locations (and tmp-linked-clean for M11) | the mutation named for that row | the named rows go red |
| PC-R7-budget | C1105_Git_audit_overall_timeout; C1105_Git_audit_hidden_locations origin-fetch-allowance-* | restore 1800 s, or drop/alter the 600 s allowance or the cap clamp | anchor assertion fails (text pin) |
| PC-doc-r7 | DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only | remove a repair 7 sentence, or restore the 30-minute budget or the resolve-through-symlink sentence | documentation pins fail |
| PC-R8 / one variant per row of the repair 8 mutation table (M1-M6) | C1105_Git_audit_hidden_locations | the mutation named for that row | the named rows go red |
| PC-R8-sweep / tmp-git,agreement,main,entry,link-scan,origin-url | C1105_Git_audit_hidden_locations (rows to be added by Mutation if missing: missing-control discovery) | revert that site to plain command substitution | a newline-/NUL-named path accepted (no row demonstrates one yet) |
| PC-doc-r8 | DockerStackDocumentationTests.Main_volume_recycling_is_scripted_only | remove the byte-for-byte sentence | documentation pin fails |

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
| index (+ sharedindex.*) | staged blobs | proven checkout: status plus raw hash (D2/D3); an existing checkout with a lost or foreign pointer: status plus raw hash against this admin directory (repair 5), after the index-equals-HEAD check; absent checkout: must equal its HEAD (`stale-index`, dirty); index in a bare directory: unknown | index, bare-index, stale-pointer-staged, pruned-path-staged, orphan-* |
| worktrees/<id> (repair 4, 5) | a whole linked Git directory and its recorded checkout | scanned directly, whether or not the checkout exists; common directory must match (`git-dir-layout`); the recorded checkout directory, when it exists, is inspected (through its own `.git` when both pointers agree, otherwise with `--git-dir=<id> --work-tree=<checkout>`: `orphan-status`); registration outside the volume: `worktree-confine`; a recorded path not ending in `.git`, a non-directory checkout or an unsearchable parent: `worktree-path`; a per-worktree `core.worktree`: `core-worktree` | stale-linked-published, linked-outside, linked-*, orphan-*, linked-core-worktree |
| antiphon/ (common only, repair 4) | product state, no object | ignored; non-empty `children/`: `antiphon-children`; in a linked directory: `gitdir-entry` | antiphon-state, antiphon-children, antiphon-linked |
| review-evidence/ (common only, repair 4) | agent evidence, no repository state | ignored, contents not audited; in a linked directory: `gitdir-entry` | review-evidence, review-evidence-linked |
| common/ and git-daemon-export-ok (common only, repair 4) | nothing when empty | empty: ignored; non-empty: `gitdir-common` / `gitdir-daemon-export` | common-empty, common-content, daemon-export |
| editor swap/backup (`.*.sw?`, `*~`, repair 4) | unrecovered text | `gitdir-editor`: recover or delete | editor-swap |
| info/grafts; refs/replace (loose or packed) | graph rewrite; replaced oid is only a refname | unknown | grafts, replace, replace-packed |
| refs/notes | notes commit naming objects by path | tip; never an origin head ancestor: unpublished | notes |
| any ref, packed-ref or pseudoref naming a non-commit (repair 5) | an annotated tag (its message), a tag of a tag, a tree, a blob | the very object must be advertised by origin (`ls-remote --heads --tags`, peeled lines included); a tree also when a present head reaches it; otherwise `RecycleUnpublishedWork` `check=tip-object`; an advertised tag's commit is counted against the heads | tag-*, tree-ref-*, blob-ref |
| reftable/ | refs and reflogs not in files | unknown (`ref-storage`) | reftable |
| modules/ | whole submodule repositories | unknown (`gitdir-modules`) | modules |
| shallow | boundary | unknown (existing `shallow`) | existing C1008 shallow case |
| info/ other than exclude, attributes, sparse-checkout, refs, empty grafts | unknown | unknown (`gitdir-info`) | info-entry |
| any other entry (for example lost-found) | unknown | unknown (`gitdir-entry`) | unknown-entry |
| objects/ | the store itself | an object no location above names is outside the audit, exactly as for `git gc`; it is never a tip | (none) |
| info/refs | repack's copy of refs | the refs it was copied from are read directly; a stale entry names an object no live location names (same as `objects/`) | published-gc, long-lived |
| outside Git directories (repair 5) | checkout content and stray files | a `.git` directory that is not the checkout's Git directory: `dot-git`; a HEAD file outside a Git-shaped directory (objects, refs, packed-refs or commondir) is not an entry; a Git-shaped directory Git does not open: `git-dir-shape`; a symlink in a checkout belongs to its content audit, any other must resolve inside the volume (`link-resolve`, `link-confine`); a bare common directory named `.git`: `core-bare` | dot-git-content, ignored-head-equality, bare-reflog, damaged-git-dir-ignored, *-symlink-*, bare-dot-git |
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

## Repair 5: orphaned checkout content, non-commit tips, symlinks and ignored HEAD files

Source: Final Review 4 (task de168b6d, three defects at 1ac296dd). No production host was contacted; every node fixture, mutation and TUnit driver ran through `scripts/build-slot.ps1`.

### What changed, per finding

| ID | Change |
|---|---|
| D1 (P1, introduced by repair 4) | `consider_linked` resolves the checkout an admin directory records (`<id>/gitdir` names `<checkout>/.git`; the directory part is resolved, the `.git` entry is not followed). When that directory exists it is always inspected: through its own `.git` when both pointers agree, otherwise by `consider_dirty` with `git -C <checkout> --git-dir=<id> --work-tree=<checkout>` (status, index flags, raw hashes; `check=orphan-status`), after the index-equals-HEAD check. Only an absent checkout falls back to the index-equals-HEAD check alone. A recorded path not ending in `.git`, a checkout that is not a directory, or an absent checkout below an unsearchable directory refuses `worktree-path`. The inspection is queued like every other worktree (same parallel queue, one status and one batched hash). |
| D2 (P1, inherited) | `ls-remote --heads --tags` (still one call). Each unique tip's own object type is read with one `cat-file --batch-check`. A commit is proven by the count, as before. A tag, tree or blob is published only when origin advertises that very object (heads, tags and peeled tag lines); a tree also when a present origin head reaches it (one extra `rev-list --objects --filter=blob:none` over the heads, only when such a tree exists); otherwise `RecycleUnpublishedWork` `check=tip-object`. An advertised tag's commit is still counted against the heads (consistent with the peeled line of a packed tag), so an advertised tag on an otherwise unpublished commit refuses `check=rev-list`. The old `tip-commit` unknown refusal is gone. |
| D3a (P2, inherited) | The volume symlink pass runs after every content audit. A dangling or out-of-volume symlink whose nearest owner is a checkout work tree (a queued worktree path, including orphan checkouts) is that checkout's content: ignored ones are ignored, tracked ones were compared by link text, unignored untracked ones already refused `status`. A symlink inside a Git directory (any `.git` component, a common or Git directory) or outside every checkout must still resolve inside the volume (`link-resolve`, `link-confine`). Symlinks are never followed or hashed as content. |
| D3b (P2, inherited) | A file named `HEAD` is a repository candidate only in a directory shaped like a Git directory (it also holds `objects`, `refs`, `packed-refs` or `commondir`); otherwise it adds no entry and runs no Git. A shaped directory must be its own Git directory; a subdirectory of a Git directory (a bare repository's `logs/`) is skipped; any other shaped directory Git does not open refuses `git-dir-shape`. The receipt lines (`entry`, `tip`) therefore depend only on repositories and retained tips; an ignored `obj/HEAD` leaves them equal. |

### Re-scan of the closed list (families: object IDs or content in a place the audit does not read)

| Location | Coverage now |
|---|---|
| Every Git-directory entry (common and each admin directory) | unchanged closed list (table above); unknown entries refuse |
| Refs, packed refs, pseudorefs, reflogs | raw parse plus Git's view; every named object is a tip; non-commit tips now proven as objects (D2) |
| Tag objects, tags of tags, trees, blobs | D2 |
| Main checkout | proven by `<checkout>/.git` = common directory; otherwise index equals HEAD; `core.worktree` in the common config refuses; NEW: `core.bare` on a directory named `.git` refuses `core-bare` (the files beside it were outside Git's view) |
| Linked checkouts reachable from admin directories | D1: inspected whenever the recorded directory exists; NEW: a per-worktree `core.worktree` (`config.worktree`, measured to redirect `status` to another directory while the recorded checkout's untracked files go unread) refuses `core-worktree` |
| Checkout content | status (untracked = all, ignored by the repository's own rules not protected), index flags, raw hashes; NEW: a `.git` directory inside a checkout that Git does not open as that checkout's Git directory refuses `dot-git` (measured: Git never lists `.git` entries, so `sub/.git/notes` was invisible and the audit passed) |
| Symlinks | D3a |
| Ignore-rule effects | ignored files never refuse (D2 of repair 4) and never change the receipt (D3b). Consequence of that decision, listed not changed: an untracked self-ignoring `.gitignore` (or `info/exclude`, `core.excludesFile`) hides untracked files by design |
| Objects no location names, unreferenced dangling commits, marker-free AUTO_MERGE trees, `rr-cache/`, stale `sharedindex.*`, message files, hooks | outside the audit by the documented closed-list decisions (repairs 3 and 4); unchanged |
| Files outside every repository (stray directories in the volume) | not Git content and not audited, as before; a stray `HEAD` file there no longer refuses (D3b), a Git-shaped directory there still must open |
| Separate-git-dir main checkout whose `.git` file was deleted | its files cannot be located from Git metadata (Git records no main work tree path); listed, not reachable by this audit |

### Baseline (1ac296dd helper; observing fixture copy without the pre-Git trace assertion, so baseline refusals that ran no Git still show their outcome)

| Case | Expect now | 1ac296dd outcome |
|---|---|---|
| orphan-missing-modified, orphan-missing-untracked | D `orphan-status` | pass (Review D1) |
| tag-annotated, tag-annotated-packed, tag-of-tag | U `tip-object` | pass (Review D2) |
| tag-tree, tree-ref-unpublished, blob-ref | U `tip-object` | K `tip-commit` |
| tree-ref-published | pass | K `tip-commit` |
| ignored-head-equality | pass, receipts equal | receipts differ (Review D3) |
| ignored-symlink-outside, tracked-symlink-outside | pass | K `link-confine` |
| ignored-symlink-dangling, orphan-symlink-ignored | pass | K `link-resolve` (Review D3) |
| untracked-symlink | D `status` | K `link-resolve` |
| damaged-git-dir-ignored | K `git-dir-shape` | pass |
| dot-git-content, linked-core-worktree, bare-dot-git | K `dot-git` / `core-worktree` / `core-bare` | pass |

Controls green at 1ac296dd by nature (not claimed as new detections): orphan-elsewhere-modified (D `status`, the entry scan already reads it), orphan-missing-clean, orphan-missing-ignored, tag-annotated-published, tag-of-tag-published, tag-lightweight, tag-published-off-branch (U `rev-list`), bare-reflog, metadata-symlink-outside (K `link-confine`), loose-symlink-dangling (K `link-resolve`). The prunable clean/staged cases are the existing rows stale-linked-published and pruned-path-staged.

### Mutation matrix (copies of the repaired helper, never the worktree; one gated driver, 18 mutants, 66 runs, 0 mismatches; each mutant also ran `seed` and `long-lived`, all green)

| Mutated production line | Case(s) red |
|---|---|
| orphan `queue_dirty ... orphan-status` removed | orphan-missing-modified, orphan-missing-untracked |
| `--git-dir=<admin> --work-tree` array not set | orphan-missing-modified, orphan-missing-clean, orphan-missing-ignored |
| index-equals-HEAD check before the orphan inspection removed | stale-pointer-staged (`orphan-status` instead of `stale-index`) |
| unadvertised tag/blob refusal no-op | tag-annotated, tag-annotated-packed, tag-of-tag, tag-tree, blob-ref |
| tag refs not added to the advertised set | tag-annotated-published, tag-of-tag-published |
| unreached-tree refusal no-op | tree-ref-unpublished |
| tree reachability skipped | tree-ref-published |
| advertised tag's commit not counted | tag-published-off-branch |
| every tip classified as a tag | tag-lightweight |
| HEAD shape skip removed | ignored-head-equality |
| Git-directory subdirectory refuses | bare-reflog |
| `git-dir-shape` refusal no-op | damaged-git-dir-ignored |
| `dot-git` refusal no-op | dot-git-content |
| checkout-owned symlink skip removed | ignored-symlink-outside, ignored-symlink-dangling, tracked-symlink-outside, orphan-symlink-ignored |
| symlink owner always the checkout | metadata-symlink-outside, loose-symlink-dangling |
| status-dirty refusal no-op | untracked-symlink |
| `core-worktree` refusal no-op | linked-core-worktree |
| `core-bare` refusal no-op | bare-dot-git |

### Performance

`volume-scale` (250 linked worktrees of 300 files, ignored build output): 47.2 s and 45.5 s for the candidate, 32.3 s for the 1ac296dd helper, on server2-temp at load average 30 to 43 (24 cores); assertion < 240 s, budget 1800 s. A trace profile of the same fixture at 60 worktrees, run back to back, counted identical Git starts per subcommand for both helpers except one more `cat-file` per common directory (the tip-type read), and took 4.1 s (candidate) and 7.6 s (baseline): the difference at 250 is host load, not added per-worktree processes. The orphan inspection uses the same queue and the same one-status, one-batched-hash cost per checkout.

### New and changed cases

29 new `C1105_Git_audit_hidden_locations` rows (93 total), each named in V-12 to V-15. No assertion was removed or weakened; `stale-pointer-staged` keeps `check=stale-index`. Documentation pins: five new sentences and two forbidden old sentences.

## Repair 6: runner-tmp checkouts and origin's real heads

Source: the read-only replay of the landed audit over the server2 OLD volume (task 96b558c0, `docs/investigations/2026-10-08-card-1105-old-volume-audit-replay.md` on `feat/card-task-96b558c0`): 390 entries, 389 content PASS; false refusals (A) `worktree-confine` for three CARD-1005/0983 checkouts under the runner's `/tmp` and (B) replay 3a, `RecycleUnpublishedWork` for commits that origin's current heads reach but the clone never fetched (293 of 1473 advertised heads absent). No production host was contacted; every fixture, baseline, mutant and TUnit driver ran through `scripts/build-slot.ps1`.

### What changed, per finding

| ID | Change |
|---|---|
| A (worktree-confine, `/tmp` checkouts) | `c1008_audit` mounts `${project}_runner-tmp` read-only at `/runner-tmp` with `C1008_TMP_MOUNT=/runner-tmp` when that volume exists, and the helper's own scratch (and the proof repository below) is a private tmpfs at `/tmp` (mode 1777, 2 GiB). The audited volume stays read-only. Mounting the tmp volume at a separate path rather than at `/tmp` keeps the helper's scratch, here-documents and `sort` spill files writable without moving them; the cost is one path translation, done once in `consider_linked`. |
| A, the rule | A linked admin directory's recorded checkout is inspected when it is under `/work`, or under the runner's `/tmp` with the tmp volume mounted: the path is translated to the mount, resolved there, and must stay inside it. Inspection is then exactly the existing one (`worktree-status` when both pointers agree, `orphan-status` against the admin directory otherwise, index-equals-HEAD when absent). Every other recorded checkout refuses `RecycleGitAuditUnknown check=worktree-confine`: outside every mounted volume, under `/tmp` when the tmp volume does not exist (the helper cannot see it), or resolving outside the tmp mount through a symlink. There is no exception for an outside checkout whose admin directory is classifiable: its files are invisible, so the audit cannot prove that it holds no unaudited work. Only registered checkouts are read in the tmp volume; the rest of `runner-tmp` is not enumerated (unchanged scope, listed). A refusal inside the tmp volume names `repo=/tmp/<path>`. |
| B (3a, stale origin heads) | `fetch_origin`, once per common directory: `git init --bare --template=` in the scratch tmpfs, `objects/info/alternates` naming the audited object store (read only), `remote.origin.url` from `git remote get-url origin`, promisor/partial-clone config, `core.alternateRefsCommand=true`, the present heads written as the proof repository's own refs (the negotiation haves), then `timeout --kill-after=5s 300s git fetch -q --filter=blob:none --no-tags --no-write-fetch-head --no-auto-gc --no-auto-maintenance --no-recurse-submodules origin '+refs/heads/*:refs/c1008/origin/*'`. Every fetched head must be a commit. Comparisons are the present heads plus the fetched heads; the tree-tip reachability and the per-tip `rev-list --count` run in the proof repository, which sees the audited objects through the alternate. Any failing step, a timeout, zero fetched heads or a non-commit fetched head falls back to the present heads alone (the repair 4 rule: more refusals, never fewer). No comparison head at all still refuses `origin-present`. The full commit/tree completeness traversal still runs in the audited repository over its tips and present heads. `GIT_NO_LAZY_FETCH=1`, system/global config `/dev/null` and the commit-graph keys apply to the fetch and its children too. Measured on git 2.47.3: without `core.alternateRefsCommand=true` Git lists the alternate's refs with a child `git --git-dir=<audited> for-each-ref` that drops `GIT_CONFIG_*` (the commit-graph keys); the fixture's per-start environment check caught it (mutant M13). |
| 3b (policy pending) | Unchanged rule: a tip no origin head reaches refuses, including tracking refs and local branches for branches origin deleted after a land. The `rev-list` refusal line now ends with `proof=fetched|present commits=<n> tips=<n> gone-tracking=<n> unadvertised-local=<n>`: unpublished commits, distinct unpublished tips, unpublished `refs/remotes/origin/*` tips whose branch origin no longer advertises, and unpublished local branches whose name origin does not advertise. A count that cannot be computed prints `?`; the verdict is already decided. The detail rides in `auditFailure` (the first `audit check=` line) and in the `RecycleUnpublishedWork` refusal text. Success receipts are unchanged: nothing origin-dependent was added, so the before/after and resume comparisons stay equal while origin moves. |
| Timeout | The overall budget stays the in-script constant `1800s` (no new knob: changing it is a reviewed source change, the existing mechanism). A timeout still refuses `check=audit-timeout`. |

### Baseline (302b870f helper; observing fixture copy without the two new anchor assertions)

| Case | Expect now | 302b870f outcome |
|---|---|---|
| tmp-linked-clean, tmp-linked-absent-clean | pass | K `worktree-confine` |
| tmp-linked-untracked, tmp-linked-modified | D `worktree-status repo=/tmp/c1005-master` | K `worktree-confine` |
| tmp-linked-orphan-untracked | D `orphan-status` | K `worktree-confine` |
| tmp-linked-absent-staged | D `stale-index` | K `worktree-confine` |
| origin-none-present-fetched | pass | K `origin-present` |
| origin-stale-master, origin-stale-master-readonly | pass | U `rev-list` (replay 3a) |
| origin-fetched-unpublished, origin-gone-counts | U `rev-list` with `proof=fetched` and counts | U `rev-list`, no counts |
| origin-stale-master-fetch-failed, origin-stale-master-fetch-timeout | U `rev-list proof=present ...` | U `rev-list`, no counts (same verdict: fallback controls) |

Green at 302b870f by nature (negative controls, not claimed as new detections): tmp-unmounted, tmp-mounted-outside, tmp-escape (all K `worktree-confine`), and the retained origin-none-present (K `origin-present`, now with the proof fetch failing). 13 of 17 red, 4 green controls.

### Mutation matrix (copies of the repaired helper, never the worktree; one gated driver, 14 mutants, 59 runs; each mutant also ran `seed` and `long-lived`)

| Mutated production line | Case(s) red |
|---|---|
| M1 `/tmp` checkout always refuses `worktree-confine` | tmp-linked-clean, tmp-linked-untracked, tmp-linked-modified, tmp-linked-orphan-untracked, tmp-linked-absent-clean, tmp-linked-absent-staged |
| M2 `/tmp` checkout not translated to the mount | tmp-linked-untracked, tmp-linked-modified, tmp-linked-orphan-untracked |
| M3 tmp-mount guard removed | tmp-unmounted |
| M4 resolved-inside-the-mount check removed | tmp-escape |
| M5 any path accepted when the tmp volume is mounted | tmp-mounted-outside |
| M6 `fetch_origin` always falls back | origin-none-present-fetched, origin-stale-master, origin-stale-master-readonly, origin-fetched-unpublished, origin-gone-counts |
| M7 fallback skips the count | origin-stale-master-fetch-failed, origin-stale-master-fetch-timeout |
| M8 proof repository is the audited repository | origin-stale-master, origin-stale-master-readonly; also `seed` and `long-lived` (the fetch-target assertion fails in every case: a stronger detection, the only 2 runs whose sanity rows went red) |
| M9 fetched heads not compared | origin-none-present-fetched, origin-stale-master |
| M10 `gone-tracking` not counted | origin-gone-counts |
| M11 `unadvertised-local` not counted | origin-gone-counts |
| M12 tmp receipt path not named | tmp-linked-untracked, tmp-linked-modified |
| M13 `core.alternateRefsCommand=true` removed | origin-stale-master (per-start environment check) |
| M14 unpublished detail dropped | origin-fetched-unpublished, origin-gone-counts, origin-stale-master-fetch-failed |

### Performance and headroom

`volume-scale` (250 linked worktrees of 300 files, one common directory, one proof fetch from a local origin): 17.2 s and 18.1 s for the candidate, 16.8 s for the 302b870f helper, back to back on server2-temp at load average 4 to 6 (assertion < 240 s, budget 1800 s). The fetch adds about one second locally plus whatever origin needs to send; it is capped at 300 s and then falls back. Replay headroom (corrected by repair 7; the 1 to 9 minutes first written here assumed one capped fetch and was wrong): the OLD-volume replay took 23.4 min (1404 s) end to end, about 20 min (about 1200 s) of it content checks, at load average 17. That volume has two common directories, so two proof fetches: one at its 300 s cap makes about 1704 s (28.4 min, 96 s left of 1800 s), two make about 2004 s (33.4 min), 204 s over the 1800 s budget. A timeout refuses (`audit-timeout`), never passes; repair 7 raised the budget (below).

### New and changed cases

16 new `C1105_Git_audit_hidden_locations` rows (109 total): origin-none-present-fetched, 9 `tmp-*` rows and 6 origin rows, each named in V-16 to V-19; one new create-argument assertion in `C1008_Recycle_audits_work_as_1654`; one fetch-count assertion in `volume-scale`. Changed because they encoded removed behaviour: (1) `origin-none-present` keeps K `origin-present` but now forces the proof fetch to fail (with the fetch, origin's master proves the commits, which is the 3a fix; the new origin-none-present-fetched row pins that); (2) the per-start "never fetch" assertion now allows exactly the proof fetch (whole-head refspec, `--filter=blob:none`, no `--stdin`, `--git-dir` outside the audited volume) and still rejects any lazy fetch; (3) the per-start environment exemption for the origin-side local `git-upload-pack` extends to its own children (`pack-objects`), which run in the origin repository, never the audited volume; (4) documentation: the pin "against the origin heads that `ls-remote` advertises and that are present in the clone" became a forbidden sentence, with five new pins. The fake Docker boundary accepts and checks the new mounts. No other assertion was removed or weakened.

Round 1 (run 20261008-134838-4b98 at b40da800, stopped after CP-11): CP-11 149/150, the one red was origin-stale-master-fetch-timeout. Its 0.01 s fetch timeout killed an origin-side `pack-objects` between its trace start event and its environment parameter events, so the per-start environment check saw a start without `GIT_NO_LAZY_FETCH`. The row now times out a fetch that waits on an origin that never answers (`--upload-pack='sleep 10; git-upload-pack'`, 2 s), so no Git process is killed mid-start; the expectation (fallback, `U rev-list proof=present`) and the check are unchanged; 5 repeated runs green and mutant M7 still red. No production line changed in round 2.

## Repair 7: tmp checkout fail-opens and the audit budget

Source: Final Review e079b0d3 of repair 6 (d6ba7439), which found two fail-opens in the runner-tmp checkout audit (both pass on d6ba7439 and refuse on 302b870f), the contradictory plan counts, and the headroom prose corrected above. No production host was contacted; every fixture, baseline, mutant and TUnit driver ran through `scripts/build-slot.ps1` or the checkpoint tool.

### What changed, per finding

| ID | Change |
|---|---|
| F1 (hidden Git content in a tmp checkout) | Under /work the repository scan makes every `.git` entry and Git-shaped `HEAD` directory an entry of its own; nothing scanned the tmp volume. `consider_tmp_git` runs the same `find` over each registered tmp checkout that exists. A `.git` file is accepted only when its `gitdir:` names an admin directory whose own recorded checkout is that very directory (a registered tmp checkout, inspected by its own admin; checked after every common directory through `tmp_nested`/`tmp_registered`). A `.git` directory, a symlinked `.git`, any other `.git` file and a Git-shaped `HEAD` directory refuse `RecycleGitAuditUnknown` (`dot-git`, `git-dir-shape`), ignored or not: their metadata is outside /work, the same reason /work refuses `common-dir-confine`. Ignored output itself still passes. |
| F2 (symlink plus `..`) | A recorded path under the runner's /tmp is translated to the mount before anything resolves it (`tmp_resolve`): a `..` component in the recorded path refuses; symlinks are followed one hop at a time in the mount, a relative target from its own directory, an absolute target only under the runner's /tmp (translated), a `..` in a target never above the mount and only from an existing directory, at most 40 hops. Anything it cannot prove refuses `worktree-confine`. A recorded path outside /tmp resolves in place and must lie under /work (it is no longer re-translated after resolving, which also read the helper's /tmp). |
| Budget | Overall audit budget 1800 s -> 3000 s. All proof fetches share a 600 s allowance (`fetch_left_ms`), charged by measured wall time; each fetch is capped at the smaller of 300 s and the remaining allowance; once spent, the remaining common directories compare only their present heads (more refusals, never fewer). A timeout still refuses `audit-timeout`. |
| F3 | V-7 now counts 122 rows; the checkpoint census says 163/299 once, history labelled. |
| F4 | Process: every run of this repair is gated (listed in the task report). |

### Deadline chain (measured, file:line at the repair 7 tip)

| Deadline | Where | Value | Encloses the audit? |
|---|---|---|---|
| Overall audit budget | scripts/c590-remote.sh:5385 | 3000 s, kill after 10 s more | is the audit |
| Proof fetch, each | scripts/c590-remote.sh:5053 (cap 5035, allowance 4439) | min(300 s, allowance left); allowance 600 s | inside |
| ls-remote / object traversal / tree reach / count | scripts/c590-remote.sh:5103, 5171, 5200, 5220 | 30 s each | inside |
| Unpublished detail | scripts/c590-remote.sh:5076 | 60 s, refusal path only | inside |
| Helper container | scripts/c590-remote.sh:5518 `docker start -a` | none | yes, unbounded |
| Pre-stop and post-removal audits | scripts/c590-remote.sh:5863, 5897 | one budget each | yes, sequential |
| Remote case under the rollout lock | scripts/c590-remote.sh:3779 `flock -w 60` | acquisition only | no wall limit after |
| ssh bridge | scripts/c590-real.ps1:112 | ServerAliveInterval 30 x CountMax 240: dead-link detection, not a wall deadline | yes, unbounded while the link answers |
| Host case | scripts/deploy-server2.ps1:892 (`& pwsh ... $verifier`), called at :1045 and :1102 | none | yes, unbounded |
| Rollout admission lock | scripts/deploy-server2.ps1:251 | 70 s to acquire | no |
| Host jq transport | scripts/deploy-server2.ps1:91 | 30 s / 180 s | no: check/provision-host-jq only |
| C1008 host fixture | tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:655, 768, 1023; RemoteScriptContractTests.cs:908 | 12 min, 30 s, 120 s, 12 min | test processes around fake Docker; not production |

No production deadline encloses the audit below 3000 s, so nothing else was raised. 3000 s is 2.14 x the replay's 1404 s and 996 s above the replay plus the whole 600 s fetch allowance (2004 s). Pins: the `audit-timeout` row anchors on the exact `timeout --kill-after=10s 3000s bash -c` line; the allowance rows anchor on `fetch_left_ms=600000` and the cap line; documentation pins the 3000 s budget and the 600 s allowance and forbids "30-minute budget".

### Baseline (d6ba7439 helper; same fixture)

| Case | Expect now | d6ba7439 outcome |
|---|---|---|
| tmp-nested-dot-git, tmp-nested-dot-git-file, tmp-nested-pointer-unregistered, tmp-nested-git-shaped, tmp-dot-git-symlink | K `dot-git` / `git-dir-shape` | pass (fail-open) |
| tmp-symlink-dotdot | K `worktree-confine` | pass (fail-open, F2) |
| tmp-symlink-climb | K `worktree-confine` | pass (fail-open: climbs out of the mount and back in) |
| tmp-symlink-absolute | D `worktree-status repo=/tmp/actual` | K `worktree-confine` (false refusal: resolved in the helper's /tmp) |
| origin-fetch-allowance-spent, origin-fetch-allowance-shared | U `proof=present`, 0 / 2 fetches | red on the anchor (no allowance exists) |

Green at d6ba7439 by nature (negative controls): tmp-linked-ignored, tmp-nested-registered, tmp-symlink-relative. 10 of 13 red, 3 controls.

### Mutation matrix (copies of the repaired helper, never the worktree; gated drivers, 12 mutants; each also ran `seed`, all green)

| Mutated production line | Case(s) red |
|---|---|
| M1 `consider_tmp_git` not called | tmp-nested-dot-git, tmp-nested-dot-git-file, tmp-nested-pointer-unregistered, tmp-nested-git-shaped |
| M2 deferred registered-pointer check removed | tmp-nested-pointer-unregistered |
| M3 regular-file check on a nested `.git` removed | tmp-dot-git-symlink (tmp-nested-dot-git still refuses under M3: a directory read as a pointer is empty) |
| M4 Git-shaped `HEAD` refusal removed | tmp-nested-git-shaped |
| M5 `..` in the recorded path accepted | tmp-symlink-dotdot |
| M6 repair 6 order restored (resolve, then translate) | tmp-symlink-dotdot, tmp-symlink-absolute, tmp-symlink-climb |
| M7 absolute target under the runner's /tmp not translated | tmp-symlink-absolute |
| M8 floor on `..` in a link target removed | tmp-symlink-climb |
| M9 spent-allowance guard removed | origin-fetch-allowance-spent (allowance-shared stays green: a negative cap makes `timeout` itself fail, which also falls back) |
| M10 fetch not charged | origin-fetch-allowance-shared |
| M11 tmp checkout not registered | tmp-linked-clean, tmp-nested-registered |
| M12 symlinks not followed in the mount | tmp-symlink-relative, tmp-symlink-absolute |

The cap clamp (`cap` below 300 s when the allowance is lower) and the 3000 s constant are pinned by text anchors only.

### New and changed cases

13 new `C1105_Git_audit_hidden_locations` rows (122 total), named in V-20 and V-21; hidden rows may now carry a fourth element run after the audit (the two allowance rows count fetch starts). Changed fixture anchors, not assertions: the proof-fetch anchor is now `timeout --kill-after=5s "${cap}s" git --git-dir="$proof" fetch` (the fetch-timeout row still shortens it to 2 s), and the overall-timeout anchor is the 3000 s line. Documentation pins: six new, one changed (the budget), two forbidden. No assertion was removed or weakened.

## Repair 8: link targets and recorded paths byte for byte

Source: Final Review 72434373 of repair 7 (a81c8bf0), F1 (P1): `tmp_resolve` read a symlink target through command substitution, which drops trailing newlines before the newline check; a link whose target is `actual\n` made the helper walk the absent `actual/checkout` and accept (exit 0) a registered `/tmp` checkout holding untracked content that d6ba7439 refuses. No production host was contacted; every fixture, baseline, mutant and TUnit driver ran through `scripts/build-slot.ps1` or the checkpoint tool.

### What changed

Two helpers in the audit body. `path_of <command>` captures a path a command prints with a sentinel byte after the output (`"$("$@" && printf x)"`), keeps the command's own status (a failed read never becomes an empty or different path), strips exactly the command's one trailing newline, and returns 3 for a path holding any control character (`[[:cntrl:]]` under `LC_ALL=C`: newline, CR, tab, ...). `read_record <file>` reads a recorded path (a worktree `gitdir`, a `.git` pointer) with the same sentinel, refuses a NUL (command substitution drops NUL bytes, so the text must be as long as `stat -c %s`), strips Git's one trailing newline and refuses any control character (1), or a failed read (2). Unsupported means unknown, never normalized.

### Sweep: every path-derived string through command substitution, read loops, xargs or word splitting in the audit body (scripts/c590-remote.sh at the repair 8 tip)

| Site | Before | Treatment |
|---|---|---|
| :4931 `tmp_resolve` link target | `$(readlink -- path)` dropped a trailing newline (F1) | `path_of readlink --`; control character refuses `worktree-confine` |
| :4915, :4938 `tmp_resolve` `IFS=/ read -a <<<` | a here-string read stops at a newline | inputs are control-free (`read_record`, `path_of`); `IFS=/` only, so spaces, a leading dash, globs and backslashes stay exact (`-r`) |
| :5005 `consider_linked` worktree `gitdir` | `$(< gitdir)` dropped trailing newlines and NUL bytes | `read_record`; refuses `worktree-path` (fail 2) |
| :5023 `consider_linked` checkout under /work | `$(realpath -m)` dropped a trailing newline of a resolved symlink target (orphan checkout skipped) | `path_of realpath -m`; refuses `worktree-path` status 3 |
| :5018 `$(tmp_resolve ...)` | output trailing newline | every component is control-free, so the one stripped newline is the printf's |
| :5031, :5038 `.git` pointer agreement | `$(< .git)` and `$(readlink -e)` comparison could agree on a normalized name | `read_record` (NUL/control: does not agree, the checkout is inspected against its admin directory; a failed read still fails) and `path_of readlink -e` compared exactly |
| :4960, :4964 `consider_tmp_git` nested `.git` | `$(< .git)`, `$(readlink -e)` | `read_record`, `path_of readlink -e`; refuses `dot-git` |
| :4987-4988, :5317-5323 common/Git directory | `$(git rev-parse)`, `$(readlink -e)` | `path_of` for both; a control character fails unknown (status 3) |
| :4895 main checkout, :5338 `.git` directory identity | `$(readlink -e)` compared to `top`/`gitdir` | `path_of readlink -e` compared exactly (mismatch keeps the stricter branch: stale index, `dot-git`) |
| :5412 symlink scan | `$(readlink -e)` | `path_of`; a control-character resolution outside a checkout fails unknown |
| :5281, :5285 `/work` and tmp mount roots | `$(readlink -e)` of fixed paths | `path_of` (fixed names; uniform) |
| :5077 origin URL for the proof fetch | `$(git remote get-url)` | `path_of`; a control character skips the fetch (present heads: more refusals) |
| :5080 `alternates` line | line-based file | `top` is control-free (`path_of`) |
| :4548 `rev-parse --git-path index` | trailing newline | the name ends in `index`; only decides seed mode |
| :4555, :4568, emptiness probes (`find -print -quit`, `status --porcelain`) | trailing newline | only emptiness is used; porcelain v1 C-quotes unusual names; a glob in `-path "$where/.git"` can prune only nested `.git` entries whose parent is printed first |
| :4609 `readlink -n` pipe, `ls-files -z`, `find -print0` loops | | exact (pipe, NUL-delimited) |
| :4473 `rel_repo` | here-string | receipt text only; newline/CR names print `?` |
| :4618-4642, :4721, :4742, ref/log/pseudoref reads | | object IDs and ref names, validated by anchored patterns; not paths |
| :5269 `kill $pids` | word splitting | job IDs only |
| xargs | | none in the audit |

### Baseline (a81c8bf0 helper; same fixture)

Seven of the ten new rows are red on a81c8bf0 (corrected by repair 9 from six; Final Review df55a140 F1 re-ran all ten against that base): five inputs the base accepts and two it refuses with a different class. The five accepted inputs are fail-opens that exit 0 (`repositories=1 partial=1`): tmp-link-target-newline (Review's case), tmp-link-target-newline-last, tmp-recorded-nul, linked-recorded-nul-orphan and linked-link-target-newline-orphan. The two differently classified refusals, tmp-link-target-cr and tmp-recorded-cr, are policy rows: the base followed the CR name exactly and refused `RecycleWorktreeDirty`; they now refuse unknown. The three controls (tmp-link-clean, tmp-space-path, tmp-space-path-untracked) match on both.

### Mutation matrix (copies of the repaired helper, never the worktree; one gated driver, 6 mutants x 7 refusal rows)

| Mutant | Red rows |
|---|---|
| M1 `tmp_resolve` target back to `$(readlink -- path)` | tmp-link-target-newline, tmp-link-target-newline-last, tmp-link-target-cr |
| M2 `path_of` without the sentinel | tmp-link-target-newline, tmp-link-target-newline-last, linked-link-target-newline-orphan |
| M3 `path_of` control-character refusal removed | tmp-link-target-newline, tmp-link-target-newline-last, tmp-link-target-cr, linked-link-target-newline-orphan |
| M4 `read_record` NUL length check removed | tmp-recorded-nul, linked-recorded-nul-orphan |
| M5 `read_record` refusing only a newline | tmp-recorded-cr |
| M6 checkout under /work back to `$(realpath -m)` | linked-link-target-newline-orphan |

Every other mutant/row pair stayed green. The remaining `path_of`/`read_record` call sites (`consider_tmp_git`, the `.git` agreement, `consider_main`, the entry loop, the symlink scan, the origin URL) are hardening with no demonstrated fail-open; their controls are pending for SourceLanding Mutation.

### New and changed cases

10 new `C1105_Git_audit_hidden_locations` rows (132 total), named in V-22; one helper (`linkTarget`) asserts the exact link bytes, the kernel-resolved checkout, the absent stripped path and Git's own status before the audit. One new documentation pin (the byte-for-byte sentence). No assertion was removed or weakened; no anchor changed.

## Checkpoints

Closed list: one isolated test build, UseAppHost=false, serial drivers. Current census (repair 8): CP-11 has 173 cases (8 existing methods + 165 C1105_Git_audit cases: 10 recovery, 11 seed, 5 index, 4 completeness, 1 resume, 132 hidden-location rows, 1 overall timeout, 1 volume scale), and the twelve rows' floors sum to 309 cases. History, for reading earlier reports only: repair 3 added 42 hidden-location rows and the long-lived seed variant; repair 4 added 22 rows, the missing-ancestor-graph variant and two methods; repair 5 added 29 rows (total then 270); repair 6 added 16 rows (286); repair 7 added 13 rows (299); repair 8 adds 10 rows (309). The slow CP-7/CP-9/CP-12 run last. Existing timeout estimates are unchanged. The Final Review e079b0d3 12-row wall was 83m27s; allow roughly 85 minutes plus implementation and baseline diagnostics.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | promisor | `tests/Antiphon.Tests -> bin-c1105-promisor/` | docs-pins | `/*/*/DockerStackDocumentationTests/*` | doc pins | 14 methods, 0 failed | 14 | 15 | true |
| CP-2 | promisor | CP-1 | registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | registry guard | 3 methods, 0 failed | 3 | 3 | true |
| CP-3 | promisor | CP-1 | compose-text-pins | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)\|(Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner*)` | compose text pins | 6 methods, 0 failed | 6 | 4 | true |
| CP-11 | promisor | CP-1 | remote-git-audit | `/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_audits_promisor_checkout*)\|(C1105_Git_audit_*)` | C1008/C1087 promisor audit | 16 methods / 173 cases, 0 failed | 173 | 15 | true |
| CP-4 | promisor | CP-1 | mount-class | `/*/*/RollingProductionMountTests/*` | class regression | 12 methods, 0 failed | 12 | 10 | true |
| CP-5 | promisor | CP-1 | retired-temp-script | `/*/*/RetiredTempContainerScriptTests/*` | class regression | 13 methods, 0 failed | 13 | 10 | true |
| CP-6 | promisor | CP-1 | host-jq | `/*/*/HostJqPrerequisiteScriptTests/*` | class regression | 39 methods, 0 failed | 39 | 15 | true |
| CP-8 | promisor | CP-1 | remote-generation | `/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Generation_identity_refuses_mismatch*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_dry_run_never_mutates*)` | C1008/C1105 | 5 methods, 0 failed | 5 | 15 | true |
| CP-10 | promisor | CP-1 | remote-resume | `/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)\|(C1105_Resume_identity_refusals*)\|(C1105_Resume_state_init_image_follows_the_journal*)\|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*)` | C1105 | 4 methods, 0 failed | 4 | 12 | true |
| CP-7 | promisor | CP-1 | retired-temp-host | `/*/*/RetiredTempContainerHostTests/*` | class regression | 21 methods, 0 failed | 21 | 15 | true |
| CP-9 | promisor | CP-1 | remote-census-long | `/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Recycle_refuses_references_and_unknown_census*)` | C1008 | 3 methods, 0 failed | 3 | 15 | true |
| CP-12 | promisor | CP-1 | recycle-wrapper | `/*/*/RollingVolumeRecycleScriptTests/*` | class regression | 16 methods, 0 failed | 16 | 15 | true |
