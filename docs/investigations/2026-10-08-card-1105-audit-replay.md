# CARD-1105 recycle git audit: read-only replay on server2 (2026-10-08)

Debug task 962f430d. Candidate audit: `c1008_git_program` at `feat/card-task-1a2ce245`
`c3b0381f87c96ce5641c28f89cfb443fea5dde86` (no later tip existed on that branch at 2026-10-08
02:30Z). Program extracted verbatim from that commit's `scripts/c590-remote.sh` heredoc
(399 lines, sha256 prefix `40072cfed49c95aa`).

## What was measured, and what was not

**Not measured: `antiphon-runner_work`.** This task ran in the server2 *temp* runner
(`antiphon-runner-temp`). `ssh mc@server2` from the runner is refused (`Permission denied
(publickey)`): the documented SSH path is only available from the desktop. The runner's
own Docker daemon is nested and cannot reach the host volume. I did not try to reach the host
disk any other way (for example a privileged nested mount), because that would be neither the
documented path nor certain to be read-only. The old volume's numbers below are therefore
**not** measured. See "Rerun on the old volume" for the command an operator can run from the
desktop.

**Measured: `antiphon-runner-temp_work`**, the same layout: a deploy-seeded blobless mirror
`repos/antiphon` and runner-created linked worktrees under `worktrees/`. It is mounted at `/work`
in this container. Read-only guarantee: a nested throwaway container
(`docker run --rm --network host -v /:/host:ro -v /work:/host/work:ro --tmpfs /host/tmp`) chroots
into the runner's own root filesystem. That is the same session-testing image family with the
same pinned `/usr/local/bin/git` 2.47.3. It runs as `--userspec=1654:1654 --groups=1654` with
`env -i`, and the program sets `GIT_OPTIONAL_LOCKS=0 GIT_NO_LAZY_FETCH=1
GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null` itself. `/work` is a separate top
`ro` mount. Positive control: `touch`/`mkdir` under `/work/repos` and `/work/worktrees` failed
with `Read-only file system`. The only writes were the audit's scratch files in the tmpfs `/tmp`.
No git command ran against the volume outside that container, except reads of file and
directory names.

The volume is live: other sessions push, fetch and create worktrees during the replay.
Counts are as of 02:30–03:30Z.

## Verbatim run (exactly the real audit's first-refusal semantics)

One unmodified run of the program over the whole volume took 26 s and returned exit 2:

```
audit check=cat-file status=128 repo=repos/antiphon
RecycleGitAuditUnknown
```

`repos/antiphon` is the first entry in `find` order. At that moment, 1 of 1426 advertised origin
heads was not in the mirror, so `cat-file -e <origin head>^{commit}` failed.

## Per-repository aggregate

The real program exits at the first refusal. To get one verdict per repository, the replay
driver runs the **verbatim function block (program lines 1–283, byte-identical)** and each
repository's **verbatim loop body** in a subshell, so a refusal ends that repository's subshell
only. Diagnostics print names, never contents. The driver diff against the verbatim program is
in the appendix.

Entries found: **396** (find pattern of the audit). Symlinks: 245, all confined (no
`link-confine`).

| Group | Entries | PASS | REFUSE |
|---|---:|---:|---:|
| `repos/antiphon` (mirror) | 1 | 0 | 1 |
| `worktrees/*` (linked worktrees of the mirror) | 246 | 0 | 246 |
| `review-evidence/4a14585d/**` (Review delegate's deliberate audit fixtures) | 149 | 9 | 140 |
| **Total** | **396** | **9** | **387** |

Each mirror-family entry shares the mirror's common directory. So the common-directory checks
(origin proof, closed-list classification of the common directory and every `worktrees/<id>`,
and the tip proof) give the same answer for all 247 of them. They were measured once on
`repos/antiphon`. Each worktree's own pre-checks and index/content check were measured per
entry.

### Refusals, by `audit check=<name> status=<n>`

Mirror family (247 entries). Layers are in the order the audit reaches them. A lower layer was
measured by neutralising the layers above it in labelled hypothetical modes (appendix).

| # | Receipt | Entries | Offending name | Classification |
|---|---|---:|---|---|
| 1a | `check=status status=0` RecycleWorktreeDirty | 1 | `worktrees/task-70b27342`: untracked `docs/investigations/2026-10-06-card-1120-1122-inherited-delivery-reds.md` | Legitimate: real unpublished bytes |
| 1b | `check=cat-file status=128` RecycleGitAuditUnknown | 247 when origin is ahead of the mirror (seen in the verbatim run, then 6 consecutive entries over about 4 min) | origin head `31632adc…` not in the mirror | **FALSE REFUSAL** (see F-1) |
| 1c | `check=origin-advertisement status=2` RecycleGitAuditUnknown | 247 when the race is lost (6 of 8 concurrent runs) | `refs/heads/feat/card-task-ae10f79e`: oid differs between `ls-remote` and `refs/remotes/origin/*` | **FALSE REFUSAL** (see F-1) |
| 2 | `check=gitdir-entry status=0` RecycleGitAuditUnknown | 247: the mirror in every run that got past 1b/1c, and 8 of the 14 entries (mirror plus 13 worktrees) that the first unsharded mode-A sweep reached; the other 6, including the mirror, hit 1b | common-dir entries `antiphon/` (this volume: `antiphon/reviews/<id>/review.md`) and `review-evidence/` | **FALSE REFUSAL** for `antiphon/` (see F-2). `review-evidence/` is ad hoc (see F-2) |
| 3 | `check=worktree-path status=1` RecycleGitAuditUnknown | 247 | `worktrees/instr`: a live session's linked worktree registered at `/tmp/claude-1654/…/scratchpad/instr`, on the runner-tmp volume and outside `/work` | Legitimate fail-closed: its HEAD, index and reflog live in the work volume being removed, while its files are outside the audit. Operator or session must remove it. |
| 4 | `check=tip-commit status=1` RecycleGitAuditUnknown | 247 | `worktrees/task-55662594/AUTO_MERGE` names a tree (`d17863f4…`). No `MERGE_HEAD`, `rebase-*` or `sequencer` is present. | **FALSE REFUSAL** (see F-3) |
| 5 | `check=rev-list status=0` RecycleUnpublishedWork | 0 | none: all **1313** commit tips (all refs, both sides of every common and linked reflog, every linked HEAD, ORIG_HEAD and FETCH_HEAD) reach an advertised origin head | none |
| – | `rev-list-objects` (30 s, whole graph, blobs filtered) | 0 | completed within the 30 s bound on the mirror | none |

No mirror-family entry refused shallow, promisor/config, grafts, replace, reftable, modules,
lock markers, `index-flags`, `index-content` or `git-dir-layout`. Every linked Git directory
held only HEAD, ORIG_HEAD (247), FETCH_HEAD (146), COMMIT_EDITMSG (190), AUTO_MERGE (1–2),
commondir, gitdir, index, logs and refs. 245 of 246 worktrees pass their own index/content
check.

`review-evidence/4a14585d/**` holds deliberate fixtures a Review delegate left on the volume.
They refuse exactly as designed, so none is a false refusal of the closed list. They still strand
a recycle until an operator deletes them.

| Receipt | Count | Examples |
|---|---:|---|
| `ls-remote status=128` | 97 | `…/environment-check/origin.git` (fixture `origin.git` and `src` repos have no `origin` remote) |
| `rev-list status=0` RecycleUnpublishedWork | 18 | `…/audit-fixtures-XC0HLc/branch/work/repo`, `…/antiphon/work/repo` |
| `rev-list-objects status=128` | 8 | `…/full-missing-ancestor-commit/work/repo`, `…/full-missing-ancestor-tree/work/repo` |
| `status status=0` RecycleWorktreeDirty | 7 | `…/path-quotes-space-unicode/…`, newline-path fixture (`repo=?`) |
| `index-flags status=0` | 3 | `…/assume-unchanged-modified/work/repo`, `…/full-skip-worktree-modified/work/repo` |
| `status status=128` | 2 | `…/missing-head-commit/work/repo`, `…/missing-head-tree/work/repo` |
| `lock-index.lock`, `bare-index` | 1 each | `…/lock/work/repo`, `…/bare-detached-unpublished/work/repo` |
| PASS | 9 | `…/environment-check/work/repo`, `…/test-fixture-shape/unfiltered-seed`. Another 3 linked fixture worktrees pass their own checks but share a refusing main repository, so they count as REFUSE |

## False refusals and the precise fix handoff

**F-1. Origin ahead of the mirror (`cat-file` 128 and `origin-advertisement` 2).** For a
drained old runner this is deterministic, not a race. The old mirror stops fetching when it is
drained, while every delegate keeps pushing `feat/card-task-*` branches to origin (1428 heads,
changing minutes apart). So when `redeploy-old` reaches the audit, origin will advertise a head
the old mirror lacks (cat-file 128) or a tracking ref behind origin (origin-advertisement).
Both describe the mirror's normal state, not unpublished work.

The tip proof is still sound if it compares against **only those advertised heads that are
present locally**. Fewer comparisons can only produce more refusals, never a false pass. The
exact `refs/remotes/origin == ls-remote` equality adds no protection: tracking refs are
themselves tips, and each tip is proven against the live advertisement.

Fix:
1. In the `origin` parse loop, keep `ls-remote` as the authority and verify each line's
   format.
2. Drop advertised heads that `cat-file --batch-check` reports `missing`. Refuse any present
   object that is not a commit.
3. Require at least one comparison.
4. Remove the exact-equality `origin-advertisement` refusal, or reduce it to "every local
   tracking ref is a tip" (already true).

The replay's mode B did exactly this and every tip still proved published. Expect red:
`C1008_Recycle_refuses_uninspectable_git` and the CARD-1105 final-repair ordering assertions
expect `origin-advertisement` for stale or missing tracking. Those assertions need the plan's
decision on what stale tracking must still refuse. A deleted origin branch whose only copy is
a local tracking ref already refuses through the tip proof.

**F-2. `<git-common-dir>/antiphon/` refused as `gitdir-entry`.** This is a product-owned
location that names no Git object:
- `antiphon/landing.lock`: the RunnerWorkspaceParkService lease, `src/Antiphon.SessionRunner/RunnerWorkspaceParkService.cs:141`.
- `antiphon/children/`: mutation-child journals, `docs/orchestration-loop.md`.
- `antiphon/verification/<operation>/<task>/restoration.json`: the SourceLanding evidence root,
  `docs/testing-and-build.md`.
- `antiphon/reviews/` here: agent review notes.

Every long-lived runner mirror can have it. On the old volume it almost certainly exists,
because it landed and verified there. Fix: add an explicit `antiphon)` arm to
`consider_gitdir` (common level only). Decide whether a non-empty `antiphon/children/` (an
unacknowledged child journal: a mutation possibly in flight) should refuse
`RecycleGitAuditUnknown` (`check=gitdir-antiphon-children`); recommended. Everything else under
it is ignored. Add a hidden-location row for it, and a positive long-lived row with
`antiphon/landing.lock` and `antiphon/verification/...`.

`<common>/review-evidence/` is an ad hoc agent location, not a product one. Decision for the
caller: ignore it the same way, or keep refusing it and have the operator delete it before
recycling.

**F-3. Stale `AUTO_MERGE` refused as `tip-commit`.** Git leaves `AUTO_MERGE` (a **tree**: the
conflicted merge result) after a conflicted merge or rebase. Here it remains in
`worktrees/task-55662594` with no `MERGE_HEAD`, `rebase-merge`, `rebase-apply` or `sequencer`.
It is always a non-commit, so the current "tip" treatment refuses every such leftover. While an
operation is in progress, the interrupted-operation arms already refuse. Fix:
- Treat `AUTO_MERGE` as ignored when no interrupted-operation marker is in the same Git directory.
- Otherwise leave it to the existing `lock-*` refusal.
- Never put it in `tips`.

That reverses the `auto-merge` row of `C1105_Git_audit_hidden_locations` and the closed-list doc
row. Add an `auto-merge-in-progress` row that still refuses.

**F-4. The audit does not finish on a real mirror.** Not a refusal, but it strands the rollout
in the same way. Every worktree entry repeats the whole common-directory pass, including
`consider_dirty` on **every** listed worktree. `consider_dirty` spawns one `hash-object` per
index entry: 7178 files took 27 s for one isolated worktree, with a p50 of 76 s across 245
worktrees under 12–24 parallel shards. With 248 worktrees, the mirror entry alone takes about
248 × 27 s ≈ 1.9 h. The 247 worktree entries then repeat it: about 247 × 1.9 h, roughly 19 days
of serial work. `c1008_audit` has no overall timeout and runs twice per recycle. The roughly
390-entry old volume is worse. Fix, preserving every check:
1. Audit each common directory once. Key a processed set by `readlink -e` of
   `--git-common-dir`, and skip a later entry whose common directory is done.
2. Run `consider_dirty` exactly once per worktree path. A worktree entry is then covered by its
   mirror's worktree list.
3. Hash with one `git hash-object --no-filters --stdin-paths` per worktree instead of one
   process per file. That keeps the raw-bytes contract, so D3 and the racy-stat fixture stay
   valid.

Contract test: an inventory with N linked worktrees performs exactly N `consider_dirty` runs
(trace the fake-docker or per-git shim), plus one large-worktree timing bound.

Legitimate fail-closed refusals (no fix): the untracked file in `worktrees/task-70b27342`; the
`/tmp`-registered worktree `instr` (session or operator removes it); all `review-evidence`
fixtures (operator deletes the residue).

## Rerun on the old volume (operator, from the desktop)

The exact real audit, read-only and first-refusal:

```
git show c3b0381f87c96ce5641c28f89cfb443fea5dde86:scripts/c590-remote.sh |
  awk '/^c1008_git_program\(\) \{/{f=1;next} f&&/cat <<.C1008_GIT.$/{p=1;next} p&&/^C1008_GIT$/{exit} p{print}' > c1008-audit.sh
ssh mc@server2 'cat > /tmp/c1008-audit.sh' < c1008-audit.sh
ssh mc@server2 'img=$(docker inspect -f "{{.Image}}" antiphon-runner-session-runner-1) &&
  id=$(docker create --user 1654:1654 --entrypoint /bin/bash \
    --mount type=volume,source=antiphon-runner_work,target=/work,readonly "$img" -c "$(cat /tmp/c1008-audit.sh)") &&
  { docker start -a "$id" | grep -v "^tip="; docker rm "$id" >/dev/null; }'
```

This writes only `/tmp/c1008-audit.sh` on the host. The volume is mounted `readonly`, exactly
as `c1008_audit` mounts it. Because of F-4, a run that clears F-1 to F-3 may not finish. Bound it
with `timeout` and read the first refusal.

Before trusting the per-repository picture there, check the old volume for:
- `repos/antiphon/.git/antiphon` and `.git/review-evidence` (F-2)
- `worktrees/*/AUTO_MERGE` without operation markers (F-3)
- `.git/worktrees/*/gitdir` targets outside `/work` (layer 3)
- residue directories such as `review-evidence/`

Names only: `ls`, plus `cat` of the gitdir files, which contain paths, not secrets.

## Appendix: replay driver against the verbatim program

The driver changes only the loop scaffolding:
- per-entry subshells and path-hash sharding;
- diagnostics that print names only;
- labelled hypothetical switches, all off for the per-repository aggregate:
  - mode B: skip missing origin heads and the equality check;
  - `replayC`: ignore `antiphon` and `review-evidence`;
  - `SKIP_UNRESOLVED`;
  - `COLLECT`: report all tips rather than stopping at the first.

`SELF_ONLY` and `SKIP_LISTED` split one entry's work into its own-worktree part and the
common-directory part. The two parts sum to the audit's per-entry checks.

<details><summary>driver.diff (program.sh → replayC.sh)</summary>

```diff
--- program.sh	2026-10-08 02:22:20.471280468 +0000
+++ replayC.sh	2026-10-08 03:24:14.205952084 +0000
@@ -247,7 +247,7 @@
             MERGE_HEAD|CHERRY_PICK_HEAD|REVERT_HEAD|rebase-merge|rebase-apply|sequencer|BISECT_*|NOTES_MERGE_*|*.lock)
                 refuse_unknown "lock-$name" 0 "$audit_repo" ;;
             shallow|modules) refuse_unknown "gitdir-$name" 0 "$audit_repo" ;;
-            objects|hooks|branches|remotes|config|config.worktree|description|commondir|gitdir|locked|gc.log|gc.pid) ;;
+            objects|hooks|branches|remotes|config|config.worktree|description|commondir|gitdir|locked|gc.log|gc.pid|antiphon|review-evidence) ;;
             COMMIT_EDITMSG|MERGE_MSG|MERGE_MODE|MERGE_RR|SQUASH_MSG|TAG_EDITMSG|NOTES_EDITMSG|EDIT_DESCRIPTION|BRANCH_DESCRIPTION) ;;
             rr-cache|sharedindex.*|fsmonitor--daemon|fsmonitor--daemon.ipc) ;;
             *) refuse_unknown gitdir-entry 0 "$audit_repo" ;;
@@ -281,13 +281,78 @@
 find "$root" -xdev -name .git -print0 -prune -o -type f -name HEAD -print0 > "$scratch/repositories" 2>/dev/null || fail $?
 audit_check=find-links
 find "$root" -xdev -type l -print0 > "$scratch/links" 2>/dev/null || fail $?
+# ---- replay driver (CARD-1105 read-only replay; not part of the audit) ----
+eval "orig_$(declare -f refuse_unknown)"
+eval "orig_$(declare -f refuse_dirty)"
+eval "orig_$(declare -f refuse_unpublished)"
+eval "orig_$(declare -f fail)"
+diag_vars() {
+    local v val
+    for v in entry name file log linked link where tip ref work; do
+        val="${!v-}"
+        [ -n "$val" ] || continue
+        case "$v" in
+            name|tip) printf 'diag %s=%s\n' "$v" "$(printf '%s' "$val" | tr -c '[:alnum:]._/@:+=-' '?')" ;;
+            *) printf 'diag %s=%s\n' "$v" "$(rel_repo "$val")" ;;
+        esac
+    done
+}
+diag_origin() {
+    [ "${audit_check:-}" = origin-advertisement ] || [ "${1:-}" = origin-advertisement ] || return 0
+    printf '%s\n' "$origin" > "$scratch/d-origin"
+    printf '%s\n' "$local_refs" > "$scratch/d-local"
+    cut -f2 "$scratch/d-origin" | sort > "$scratch/d-on"
+    cut -f2 "$scratch/d-local" | sort > "$scratch/d-ln"
+    printf 'diag origin-heads=%s local-tracking=%s\n' "$(grep -c . "$scratch/d-on")" "$(grep -c . "$scratch/d-ln")"
+    printf 'diag only-origin=%s ex=%s\n' "$(comm -23 "$scratch/d-on" "$scratch/d-ln" | grep -c .)" "$(comm -23 "$scratch/d-on" "$scratch/d-ln" | head -3 | tr '\n' ',')"
+    printf 'diag only-local=%s ex=%s\n' "$(comm -13 "$scratch/d-on" "$scratch/d-ln" | grep -c .)" "$(comm -13 "$scratch/d-on" "$scratch/d-ln" | head -3 | tr '\n' ',')"
+    join -t "$(printf "\t")" -1 2 -2 2 <(sort -t "$(printf "\t")" -k2 "$scratch/d-origin") <(sort -t "$(printf "\t")" -k2 "$scratch/d-local") | awk -F"\t" '$2!=$3{print $1}' > "$scratch/d-diff"
+    printf 'diag oid-differs=%s ex=%s\n' "$(grep -c . "$scratch/d-diff")" "$(head -3 "$scratch/d-diff" | tr '\n' ,)"
+}
+diag_dirty() {
+    local w="$1"
+    [ -n "$w" ] || return 0
+    printf 'diag index-present=%s\n' "$([ -e "$(git -C "$w" rev-parse --path-format=absolute --git-path index 2>/dev/null)" ] && echo 1 || echo 0)"
+    git -C "$w" status --porcelain --untracked-files=all 2>/dev/null > "$scratch/d-status"
+    printf 'diag status-lines=%s non-D=%s ex=%s\n' "$(grep -c . "$scratch/d-status")" "$(grep -vc '^D ' "$scratch/d-status")" \
+        "$(grep -v '^D ' "$scratch/d-status" | head -3 | cut -c1-2,4- | tr '\n' ',' | tr -c '[:print:]' '?')"
+    printf 'diag top-level=%s\n' "$(find "$w" -mindepth 1 -maxdepth 1 ! -name .git -printf '%f,' 2>/dev/null | head -c 200 | tr -c '[:print:]' '?')"
+}
+diag_tip() {
+    printf 'diag tip=%s\n' "${tip:-}"
+    printf 'diag named-by=%s\n' "$(grep -rlF --exclude-dir=objects -- "${tip:-x}" "$top" 2>/dev/null | head -5 | while IFS= read -r f; do printf '%s,' "$(rel_repo "$f")"; done)"
+    printf 'diag count=%s\n' "${count:-}"
+}
+refuse_unknown() { trap - ERR; set +e; diag_vars; diag_origin "$1"; orig_refuse_unknown "$@"; }
+refuse_dirty() { trap - ERR; set +e; diag_vars; diag_dirty "$2"; orig_refuse_dirty "$@"; }
+refuse_unpublished() { trap - ERR; set +e; diag_tip; orig_refuse_unpublished "$@"; }
+fail() { local s="${1:-}"; trap - ERR; set +e; diag_vars; [ "${audit_check:-}" != cat-file ] || printf "diag origin-head-types=%s\n" "$(cut -f1 <<< "$origin" | git -C "$repo" cat-file --batch-check="%(objecttype)" 2>/dev/null | sort | uniq -c | tr -s " \n" " ")"; orig_fail "$s"; }
+trap - ERR
+set +e
+nlinks=0
 while IFS= read -r -d '' link; do
+    [ "${REPLAY_FROM:-0}" = 0 ] || continue
+    nlinks=$((nlinks + 1))
+    (
+    set -e
+    trap 'fail $?' ERR
     audit_check=link-resolve
     audit_repo="$link"
     resolved="$(readlink -e -- "$link")" || fail $?
     [[ "$resolved/" == "$root/"* ]] || refuse_unknown link-confine 0 "$link"
+    ) > "$scratch/one-link" 2>/dev/null
+    rc=$?
+    [ "$rc" = 0 ] || { printf 'LINK rc=%s link=%s\n' "$rc" "$(rel_repo "$link")"; sed 's/^/  /' "$scratch/one-link"; }
 done < "$scratch/links"
+printf 'links=%s\n' "$nlinks"
 while IFS= read -r -d '' entry; do
+    idx=$((${idx:--1} + 1))
+    [ -z "${REPLAY_ONLY:-}" ] || [ "$entry" = "$REPLAY_ONLY" ] || continue
+    shard_hash="$(printf '%s' "$entry" | cksum | cut -d' ' -f1)"; [ "$((shard_hash % ${REPLAY_TO:-1}))" = "${REPLAY_FROM:-0}" ] || continue
+    started=$SECONDS
+    (
+    set -e
+    trap 'fail $?' ERR
     case "$entry" in
         */.git) repo="${entry%/.git}" ;;
         */HEAD) repo="${entry%/HEAD}" ;;
@@ -329,10 +394,16 @@
         true) ;;
         *) refuse_unknown bare 2 "$repo" ;;
     esac
+    if [ "${REPLAY_SELF_ONLY:-0}" = 1 ] && [ "$gitdir" != "$top" ]; then printf 'SELFONLY\n'; exit 0; fi
     audit_check=ls-remote
     timeout --kill-after=5s 30s git -C "$repo" ls-remote --heads origin > "$scratch/origin" 2>/dev/null || fail $?
     audit_check=origin-parse
     origin="$(sort "$scratch/origin")" || fail $?
+    if [ "${REPLAY_MODE:-A}" = B ]; then
+        cut -f1 <<< "$origin" | git -C "$repo" cat-file --batch-check='%(objectname) %(objecttype)' > "$scratch/present" 2>/dev/null || fail $?
+        printf 'diag mode-b-missing-origin=%s\n' "$(grep -vc ' commit$' "$scratch/present")"
+        origin="$(join -t ' ' <(awk '$2=="commit"{print $1}' "$scratch/present" | sort -u) <(tr '\t' ' ' <<< "$origin" | sort) | tr ' ' '\t' | sort)"
+    fi
     [ -n "$origin" ] || refuse_unknown origin-empty 0 "$repo"
     comparisons=()
     while IFS=$'\t' read -r tip ref; do
@@ -347,7 +418,7 @@
         fi
     done <<< "$origin"
     [ "${#comparisons[@]}" -gt 0 ] || refuse_unknown origin-empty 0 "$repo"
-    if [ "$bare" = false ]; then
+    if [ "$bare" = false ] && [ "${REPLAY_MODE:-A}" != B ]; then
         audit_check=origin-advertisement
         git -C "$repo" for-each-ref --format='%(objectname)%09refs/heads/%(refname:strip=3)' refs/remotes/origin > "$scratch/local" 2>/dev/null || fail $?
         local_refs="$(awk '$2!="refs/heads/HEAD" {print}' "$scratch/local" | sort)" || fail $?
@@ -363,12 +434,12 @@
         [[ "$field" == worktree\ * ]] || continue
         audit_check=worktree-path
         work="${field#worktree }"
-        work="$(readlink -e "$work")" || fail $?
+        work="$(readlink -e "$work")" || { s=$?; if [ "${REPLAY_SKIP_UNRESOLVED:-0}" = 1 ]; then printf 'diag skipped-unresolved-worktree\n'; continue; fi; fail $s; }
         [[ "$work/" == "$root/"* ]] || refuse_unknown worktree-confine 0 "$repo"
         audit_check=worktree-bare
         work_bare="$(git -C "$work" rev-parse --is-bare-repository 2>/dev/null)" || fail $?
         if [ "$work_bare" = false ]; then
-            consider_dirty "$work" worktree-status
+            if [ "${REPLAY_SKIP_LISTED_DIRTY:-0}" != 1 ]; then consider_dirty "$work" worktree-status; fi
         elif [ "$work_bare" != true ]; then
             refuse_unknown worktree-bare 2 "$work"
         fi
@@ -385,15 +456,22 @@
     printf '%s\n' "${comparisons[@]}" >> "$scratch/roots"
     timeout --kill-after=5s 30s git -C "$repo" rev-list --objects --no-object-names --filter=blob:none --missing=error --stdin \
         < "$scratch/roots" > "$scratch/objects" 2>/dev/null || fail $?
+    printf 'STAGE unique-tips=%s secs=%s\n' "$(grep -c . "$scratch/unique")" "$((SECONDS - started))"
+    tipn=0
     while IFS= read -r tip; do
+        tipn=$((tipn + 1)); [ "$((tipn % ${REPLAY_TIP_SHARDS:-1}))" = "${REPLAY_TIP_SHARD:-0}" ] || continue
         [[ "$tip" =~ ^[0-9a-f]{40}$ ]] || refuse_unknown tips 2 "$repo"
         audit_check=tip-commit
-        git -C "$repo" cat-file -e "$tip^{commit}" 2>/dev/null || fail $?
+        git -C "$repo" cat-file -e "$tip^{commit}" 2>/dev/null || { s=$?; if [ "${REPLAY_COLLECT:-0}" = 1 ]; then printf 'diag tip=%s type=%s\nNONCOMMIT\n' "$tip" "$(git -C "$repo" cat-file -t "$tip" 2>/dev/null || echo missing)"; diag_tip; continue; fi; fail $s; }
         audit_check=rev-list
-        count="$(timeout --kill-after=5s 30s git -C "$repo" rev-list --count "$tip" --not "${comparisons[@]}" 2>/dev/null)" || fail $?
+        count="$(timeout --kill-after=5s 30s git -C "$repo" rev-list --count "$tip" --not "${comparisons[@]}" 2>/dev/null)" || { s=$?; if [ "${REPLAY_COLLECT:-0}" = 1 ]; then printf 'diag tip=%s status=%s\nCOUNTFAIL\n' "$tip" "$s"; continue; fi; fail $s; }
         [[ "$count" =~ ^[0-9]+$ ]] || refuse_unknown rev-list-count 2 "$repo"
-        [ "$count" = 0 ] || refuse_unpublished "$repo"
+        if [ "$count" != 0 ]; then if [ "${REPLAY_COLLECT:-0}" = 1 ]; then diag_tip; printf 'UNPUBLISHED\n'; else refuse_unpublished "$repo"; fi; fi
         printf 'tip=%s origin=%s repo=%s\n' "$tip" "$(printf '%s' "$origin" | sha256sum | cut -d' ' -f1)" "$(printf '%s' "$repo" | sha256sum | cut -d' ' -f1)"
     done < "$scratch/unique"
+    ) > "$scratch/one" 2>/dev/null
+    rc=$?
+    printf 'RESULT rc=%s secs=%s tips=%s entry=%s\n' "$rc" "$((SECONDS - started))" "$(grep -c '^tip=' "$scratch/one")" "$(rel_repo "$entry")"
+    grep -v '^tip=' "$scratch/one" | sed 's/^/  /'
 done < "$scratch/repositories"
-printf 'repositories=%s partial=%s\n' "$repos" "$partial_repos"
+printf 'DONE\n'
```

</details>
