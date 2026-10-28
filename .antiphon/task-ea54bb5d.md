# CARD-1594 Review: retire-temp audit allows antiphon-evidence (task ea54bb5d)

Subject: task 74294297-124d-4076-8dd8-ccd9e0c35bc4, branch feat/card-task-74294297.
Reviewed tip: ce1d73664edc819cebd371961b68099c5460c351. Red witness commit: 0b60c198b19303e91ad0f153ef25291061686a46. Base: rc/2026-10-27 68565db92359eadea14180c745c0a28711a36872.
Host: server2 runner mirror (Linux, uid 1654). Read-only. No retire-temp apply and no real Docker. Detached scratch worktrees only (tip, red, base). Nothing deleted.

## Verdict

The change is sound. It adds no regression and no fail-open. `antiphon-evidence/` is walked and refuses everything except plain single-link files and directories with non-Git names. Every refusal I tried refuses `RecycleGitAuditUnknown`. The diff to `c590-remote.sh` is three hunks, and none of them touches the cross-SHA image binding, the empty-image refusal, ignored receipts, the same-SHA lookup or the preview/apply docker arguments.

Two defects predate this change but sit in its scope:
- **D-1:** the census test is red at both base and tip.
- **D-2:** `review-evidence/` can hide a whole Git repository. This change's new documentation calls that directory safe.

Scope was Interim: only the narrow witnesses the brief named ran, not the whole Unit lane.

## Runs (build-slot gated; one UseAppHost=false build per SHA, then dotnet exec --no-build)

| Run | SHA | Result |
|---|---|---|
| RollingVolumeRecycleScriptTests/C1594_Antiphon_evidence_gitdir | ce1d7366 | 15/15 passed |
| DockerStackDocumentationTests | ce1d7366 | 15/15 passed |
| RcRosterRepairTests | ce1d7366 | 3/3 passed |
| WindowsUnitPlatformAdmissionTests (census) | ce1d7366 | 3 passed, 1 skipped (Windows-only), **1 failed**: C1210_Roster_closure_matches_source_gates |
| WindowsUnitPlatformAdmissionTests (census) | 68565db9 base | same: 3 passed, 1 skipped, **1 failed** (same assertion) |
| fixture variants via node (red) | 0b60c198 | 1 of 15 red: `antiphon-evidence` (the pass case). The 14 refusal or pass-through cases are green at base by construction, because base refused every antiphon-evidence entry |
| check-evidence-diff 68565db9..ce1d7366 | - | commits=2 entries=0 violations=0 |
| RemoteScriptContractTests, full RollingVolumeRecycleScriptTests, whole Unit | - | not run (brief: narrow witnesses only) |

## Witness adequacy (scratch copies of the tip script; the source tree was never edited)

Seven of the eight walk mutations turn a named witness red:
- Dropping the Git-name check: git, refs, objects, head, packed-refs and dot go red.
- Dropping the `-L` check: symlink goes red.
- Dropping the nlink check: hardlink goes red.
- Making the else-branch pass: odd (fifo) goes red.
- Dropping the file `-r` check: unreadable goes red.
- Dropping the common-level check: linked goes red.
- Dropping the root `-d`/`-L` check: symlink-root goes red.

The eighth mutation turns `find ... || refuse` into `|| :`. No witness catches it (A-1).

The hidden-repository witnesses really contain Git state:
- `-git` holds a nested `.git/HEAD`.
- `-refs` holds `refs/heads/master` with HEAD's oid.
- `-objects` holds `objects/pack/pack-x`.

My extra probes:
- **Refuse:** a real `git init --bare` inside the tree, a `.git` gitdir-pointer file, `logs/HEAD`, a newline in a name, a socket, a directory without the x bit, a regular file at the root, an internal hard link (both names inside), an unreadable subdirectory, and a mode-0 root.
- **Pass:** names with spaces or a leading dash (`-rf/--help`), `logs/build.log`, an empty tree, depth 150, `Objects`/`REFS` (Git names are case-sensitive on Linux) and loose `pack-*.pack` files.
- An unreadable directory anywhere also refuses earlier, at the whole-volume `find-links` walk (check=find-links). This held even with the walk's own find-failure handling mutated away.

## Priority answers

1. **Hidden unpublished work.**
   - A Git-openable repository cannot hide in `antiphon-evidence/`. Git needs HEAD plus objects/refs (or a `.git` or `commondir` pointer), and every one of those names refuses at any depth.
   - Opaque bytes do pass. My probe `x-bundle` put an unpublished commit in a `git bundle` file and the audit passed. A patch, notes or a tarball would pass the same way, and all of them are deleted with the volume. That is the same class as ignored files in a checkout, which the audit already deletes.
   - Compared with `review-evidence/` the new rule is strictly stronger: walked, and refusing names, symlinks, hard links and odd types. Compared with base it is weaker by design.
   - The docs say the tree is "agent output … not repository state". They do not say plainly that file contents are never inspected (A-4).
2. **The walk.**
   - `-print0` with `read -d ''` handles any name. Control characters (newline included) refuse, and names with spaces or a leading dash are safe.
   - Hard links: `stat %h` must equal 1 for regular files. Directories are not counted, which is correct because Linux cannot hard-link a directory.
   - The audit runs as `--user 1654 --cap-drop ALL`, so unreadable entries are a real case, and they refuse.
   - A fifo, a socket or any other type refuses.
   - Mounts: the walk has no `-xdev`, but the helper container sees no nested mounts inside the volume.
   - A linked admin's `antiphon-evidence` refuses (witnessed). Each repository's common directory gets its own walk.
   - Cost: about 2.5 ms per entry on loaded server2 (20,000 files took 50 s), because each file forks a `stat`. There is no cap. The overall audit cap of 10800 s ends in status 124/137, which is `RecycleGitAuditUnknown` (refuse, never pass). A full scratch tmpfs makes `find` fail and refuses.
3. **Fail-closed.**
   - Every branch of the walk refuses through `refuse_gitdir_entry`, which prints check=gitdir-entry and exit 2 with the diagnosis text unchanged. An unexpected error trips `set -e` or ERR and the audit ends non-zero.
   - The diff to `c590-remote.sh` has three hunks:
     - the closed-list comment;
     - the new `antiphon_evidence_git_name` and `consider_antiphon_evidence`;
     - a comment line on the `review-evidence` arm plus a new `antiphon-evidence)` arm in `consider_gitdir`.
   - No other code changed.
4. **Witnesses and roster.**
   - The witnesses are real (see above).
   - The new roster row `C1594_Antiphon_evidence_gitdir` is accepted by the closure. It sits in `Existing`, which is source-closure-only, so its Results value is not counted at runtime.
   - The census test is still red (D-1).

## Defects

**D-1 (inherited at base 68565db9; not introduced. Original landing owner: the earlier CARD-1594 Code slices 8cd467832/96a4c34dd)**
- Where: `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs`, Existing.
- Failure: `C1210_Roster_closure_matches_source_gates` fails with `c1210-unrostered-gate: …RollingVolumeRecycleScriptTests.C1594_Cross_sha_cleanup_image_binds_the_retired_temp`. A scan finds two more gated methods without rows: `C1594_Older_retirement_receipt_is_ignored` and `C1594_Disagreeing_cross_sha_images_refuse`.
- Why: rc stays red on the census. The subject reported "census 4: 0 failed", and I could not reproduce that.
- Fix: add three Existing rows (RollingVolumeRecycleScriptTests, Results 1, ExistingReason), then rerun the census class.

**D-2 (pre-existing fail-open; this change's documentation now calls it safe)**
- Where: the `scripts/c590-remote.sh` `consider_gitdir` `review-evidence)` arm, plus `docs/docker-stack.md` and its DockerStackDocumentationTests pin "`review-evidence/` is safe because it is agent evidence rather than repository state".
- Failure: my probe `x-review-repo` put a real non-bare repository with an unpublished commit at `<common>/review-evidence/x/repo/.git`. The audit exits 0 (`repositories=1 partial=1`) at both base and tip. The whole-volume repository `find` prunes at the outer `.git`, so it never sees the inner one.
- Why: retire-temp apply would delete that work.
- Fix: walk `review-evidence/` with the same rule. At minimum refuse a nested `.git`, `HEAD`, `objects` or `refs` there, and correct the documentation sentence.
- Operational guard before the real antiphon-runner-temp apply: run a read-only `find <common>/review-evidence \( -name .git -o -name HEAD -o -name objects -o -name refs \)` over the temp volume. This covers the audited repositories' common git directories under the work volume. Expect no hits.

## Advisory (no Review rerun needed)

- **A-1:** `find -P … || refuse_gitdir_entry` has no witness. Today it is backed by the `find-links` walk for permission failures, but not for a truncated scratch write.
- **A-2:** `stat` forks once per file. Using `find -printf '%y %n %p\0'` would remove the fork from the 2.5 ms per-entry cost.
- **A-3:** the roster row says Results 1 for a method with 15 `[Arguments]` cases. Sibling rows record case counts (C1105_Jq_argv_limit 3). This is harmless because Existing is source-closure-only.
- **A-4:** the docs could say that file contents under `antiphon-evidence/` are never read and that a patch or bundle there is deleted with the volume.
