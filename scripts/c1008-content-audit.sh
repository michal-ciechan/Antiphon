# CARD-1609. Spliced into the temp audit body. v1 stays fail-fast when
# C1609_CONTENT_V2 is unset. This file must not contain the audit delimiters.
# A repo is disposable only when its raw commit bytes match the card-1605 seed.

# V2 binds the complete repository/origin request roster and fetched graph.
c1609_apply_host_proof() {
    local rel url name file bundle line actual wanted created completed started now n=0 ref
    local ref_re=$'^[0-9a-f]{40}\trefs/(heads|tags)/[^[:space:]]+$'
    local -a headers=()
    local -A seen=()
    rel="$(rel_repo "$audit_repo")"; c1008_rel_safe "$rel" || fail 128 ' reason=proof-repo'
    path_of git --git-dir="$top" remote get-url origin 2>/dev/null || fail 128
    url="$path_out"; c1008_scrub_origin "$url" || fail 128 ' reason=proof-origin'
    [ "$url" = "$c1008_scrubbed" ] || fail 128 ' reason=proof-origin'
    name="$(c1008_proof_name "$rel" "$url")"
    [[ "$name" =~ ^[0-9a-f]{64}$ ]] || fail 128 ' reason=proof-parse'
    file="${C1008_HOST_PROOF:-}/$name"; bundle="$file.bundle"
    [ -f "$file" ] && [ ! -L "$file" ] && [ -r "$file" ] &&
      [ -f "$bundle" ] && [ ! -L "$bundle" ] && [ -r "$bundle" ] || fail 128 ' reason=proof-missing'
    [ "$(stat -c %s -- "$file")" -le 16777216 ] || fail 128 ' reason=proof-parse'
    grep -q $'\r' "$file" && fail 128 ' reason=proof-parse'
    {
        for ((n=0;n<11;n++)); do IFS= read -r line || fail 128 ' reason=proof-parse'; headers+=("$line"); done
    } < "$file"
    [ "${headers[0]}" = 'c1008-host-proof 2' ] &&
      [ "${headers[1]}" = "operation ${C1008_OPERATION:-}" ] &&
      [ "${headers[2]}" = "origin $(c1008_sha256 "$url")" ] &&
      [ "${headers[3]}" = "repo $rel" ] || fail 128 ' reason=proof-binding'
    [[ "${C1609_PROOF_NONCE:-}" =~ ^[0-9a-f]{32}$ ]] &&
      [[ "${C1609_PROOF_INVENTORY:-}" =~ ^[0-9a-f]{64}$ ]] &&
      [ "${headers[5]}" = "nonce $C1609_PROOF_NONCE" ] &&
      [ "${headers[6]}" = "inventory $C1609_PROOF_INVENTORY" ] || fail 128 ' reason=proof-binding'
    created="${headers[4]#created }"; completed="${headers[10]#completed }"; started="${C1008_OPERATION_STARTED:-}"
    [[ "$created" =~ ^(0|[1-9][0-9]{0,11})$ && "$completed" =~ ^(0|[1-9][0-9]{0,11})$ && "$started" =~ ^(0|[1-9][0-9]{0,11})$ ]] || fail 128 ' reason=proof-stale'
    [ "${headers[4]}" = "created $created" ] && [ "${headers[10]}" = "completed $completed" ] || fail 128 ' reason=proof-parse'
    now="$(date -u +%s)" || fail 128 ' reason=proof-stale'
    [ "$created" -ge "$started" ] && [ "$completed" -ge "$created" ] &&
      [ "$((completed-created))" -le 600 ] && [ "$((now-completed))" -ge -60 ] &&
      [ "$((now-completed))" -le 900 ] || fail 128 ' reason=proof-stale'
    wanted="${headers[7]#bundle }"; [[ "$wanted" =~ ^[0-9a-f]{64}$ ]] &&
      [ "${headers[7]}" = "bundle $wanted" ] && [ "$(sha256sum "$bundle" | cut -d' ' -f1)" = "$wanted" ] || fail 128 ' reason=proof-bundle'
    actual="${headers[9]#count }"; [[ "$actual" =~ ^[1-9][0-9]{0,7}$ ]] &&
      [ "${headers[9]}" = "count $actual" ] || fail 128 ' reason=proof-parse'
    : > "$scratch/origin"; n=0
    while IFS= read -r line || [ -n "$line" ]; do
        n=$((n+1)); [ "$n" -gt 11 ] || continue
        [[ "$line" =~ $ref_re ]] || fail 128 ' reason=proof-parse'
        ref="${line#*$'\t'}"; [ -z "${seen[$ref]+x}" ] || fail 128 ' reason=proof-parse'; seen[$ref]=1
        case "${line,,}" in *ghp_*|*gho_*|*ghu_*|*ghs_*|*ghr_*|*github_pat_*|*token*|*password*|*secret*) fail 128 ' reason=proof-parse' ;; esac
        printf '%s\n' "$line" >> "$scratch/origin" || fail 128 ' reason=proof-parse'
    done < "$file"
    [ "$((n-11))" = "$actual" ] || fail 128 ' reason=proof-coverage'
    wanted="${headers[8]#advertisement }"; [[ "$wanted" =~ ^[0-9a-f]{64}$ ]] &&
      [ "${headers[8]}" = "advertisement $wanted" ] && [ "$(sha256sum "$scratch/origin" | cut -d' ' -f1)" = "$wanted" ] || fail 128 ' reason=proof-coverage'
    printf 'C1008_PRIVATE_ORIGIN repo=%s origin=%s originHash=%s proofName=%s\n' "$rel" "$url" "$(c1008_sha256 "$url")" "$name" >> "$scratch/c1609-proof-requests"
}

c1609_verify_host_coverage() {
    [ -n "${C1008_HOST_PROOF:-}" ] || return 0
    [ -f "$scratch/c1609-proof-requests" ] || : > "$scratch/c1609-proof-requests"
    LC_ALL=C sort "$scratch/c1609-proof-requests" > "$scratch/c1609-proof-sorted" || fail 128 ' reason=proof-coverage'
    [ "$(sha256sum "$scratch/c1609-proof-sorted" | cut -d' ' -f1)" = "${C1609_PROOF_INVENTORY:-}" ] || fail 128 ' reason=proof-coverage'
}

c1609_refuse_reason() {
    printf 'audit check=content status=0 repo=%s reason=%s\n' "$(rel_repo "$2")" "$1"
    printf 'RecycleGitAuditUnknown\n'
    exit 2
}

c1609_finding() {
    local role="$1" repo="$2" kind="$3" object_id="$4" commit_id="$5" reason="$6"
    local volume="antiphon-runner-temp_work"
    [ "$role" = tmp ] && volume="antiphon-runner-temp_runner-tmp"
    printf 'C1609_FINDING role=%s repo=%s kind=%s commit=%s reason=%s\n' "$role" "$repo" "$kind" "$commit_id" "$reason"
    printf 'C1609_FINDING_JSON={"schema":2,"volume":"%s","role":"%s","repo":"%s","kind":"%s","objectId":%s,"commitId":%s,"reason":"%s"}\n' \
        "$volume" "$role" "$repo" "$kind" "$object_id" "$commit_id" "$reason"
}

c1609_note_unvisited() {
    local rel="$1"
    c1609_findings_complete=false
    rel="${rel//\\/\\\\}"
    rel="${rel//\"/\\\"}"
    if [ -z "${c1609_unvisited_json:-}" ]; then
        c1609_unvisited_json="\"$rel\""
    else
        c1609_unvisited_json="${c1609_unvisited_json},\"$rel\""
    fi
}

c1609_origin_configured() {
    git --git-dir="$top" config --get remote.origin.url >/dev/null 2>&1
}

c1609_epoch_ok() {
    [[ "$1" =~ ^0$|^[1-9][0-9]{0,17}$ ]]
}

c1609_zone_ok() {
    [[ "$1" =~ ^[+-](0[0-9]|1[0-9]|2[0-3])[0-5][0-9]$ ]]
}

c1609_seed_path() {
    local rep="$1" ancestor rel walk part
    local -a parts=()
    for ancestor in "${!work_trees[@]}"; do
        [ "$ancestor" != "$rep" ] || continue
        case "$rep" in
            "$ancestor"/*) ;;
            *) continue ;;
        esac
        rel="${rep#"$ancestor"/}"
        [ "$rel" = ".antiphon/test-output/card-1605/template" ] || continue
        walk="$ancestor"
        IFS=/ read -r -a parts <<< "$rel"
        for part in "${parts[@]}"; do
            walk="$walk/$part"
            [ ! -L "$walk" ] || return 1
        done
        return 0
    done
    return 1
}

c1609_blob_is() {
    local oid="$1" expected="$2" size got
    size="$(git --git-dir="$top" cat-file -s "$oid" 2>/dev/null)" || return 1
    got="$(git --git-dir="$top" cat-file blob "$oid" 2>/dev/null; printf x)" || return 1
    got="${got%x}"
    [ "${#got}" = "$size" ] || return 1
    [ "$got" = "$expected" ]
}

c1609_seed_bytes() {
    local rep="$1" fmt refs head_ref oid main_oid count remotes name raw body author_line committer_line
    local a_epoch a_zone c_epoch c_zone expected tree_b64 hook base old new rest log
    fmt="$(git --git-dir="$top" rev-parse --show-object-format 2>/dev/null || printf sha1)"
    [ "$fmt" = sha1 ] || return 1
    remotes="$(git --git-dir="$top" remote 2>/dev/null || true)"
    [ -z "$remotes" ] || return 1
    [ ! -e "$top/objects/info/alternates" ] && [ ! -L "$top/objects/info/alternates" ] || return 1
    [ ! -e "$top/worktrees" ] && [ ! -L "$top/worktrees" ] || return 1
    [ ! -e "$top/reftable" ] && [ ! -L "$top/reftable" ] || return 1
    if [ -d "$top/hooks" ]; then
        while IFS= read -r -d '' hook; do
            base="${hook##*/}"
            case "$base" in
                *.sample) ;;
                *) return 1 ;;
            esac
        done < <(find "$top/hooks" -mindepth 1 -maxdepth 1 -print0 2>/dev/null || true)
    fi
    while IFS= read -r name || [ -n "$name" ]; do
        [ -n "$name" ] || continue
        name="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"
        case "$name" in
            include.*|includeif.*|filter.*|fsmonitor.*|diff.external|core.hookspath|core.sshcommand|protocol.*|remote.*)
                return 1
                ;;
        esac
    done < <(git --git-dir="$top" config --local --name-only --list 2>/dev/null || true)
    refs="$(git --git-dir="$top" for-each-ref --format='%(refname)' 2>/dev/null || true)"
    [ "$refs" = refs/heads/main ] || return 1
    head_ref="$(git --git-dir="$top" symbolic-ref -q HEAD 2>/dev/null || true)"
    [ "$head_ref" = refs/heads/main ] || return 1
    oid="$(git --git-dir="$top" rev-parse HEAD 2>/dev/null || true)"
    main_oid="$(git --git-dir="$top" rev-parse refs/heads/main 2>/dev/null || true)"
    [[ "$oid" =~ ^[0-9a-f]{40}$ ]] || return 1
    [ "$oid" = "$main_oid" ] || return 1
    if git --git-dir="$top" rev-parse --verify --quiet "${oid}^" >/dev/null 2>&1; then
        return 1
    fi
    count="$(git --git-dir="$top" rev-list --count --all 2>/dev/null || true)"
    [ "$count" = 1 ] || return 1
    if [ -d "$top/logs" ]; then
        while IFS= read -r -d '' log; do
            [ -f "$log" ] && [ -r "$log" ] || return 1
            while IFS=' ' read -r old new rest || [ -n "${old:-}${new:-}" ]; do
                [ -n "${old:-}" ] || continue
                [[ "$old" =~ ^[0-9a-f]{40}$ && "$new" =~ ^[0-9a-f]{40}$ ]] || return 1
                [ "$old" = 0000000000000000000000000000000000000000 ] || [ "$old" = "$oid" ] || return 1
                [ "$new" = 0000000000000000000000000000000000000000 ] || [ "$new" = "$oid" ] || return 1
            done < "$log"
        done < <(find "$top/logs" -type f -print0 2>/dev/null || true)
    fi
    c1609_blob_is 94d1ec507d791eab738351179c145df29bd8fb9d $'.antiphon/\n' || return 1
    c1609_blob_is e50285176007e126bede4a894b09763c6c194f6d 'benign fixture' || return 1
    tree_b64="$(git --git-dir="$top" cat-file tree 831fc61e72b5c6b9570f91d6eabc272ad8f2899f 2>/dev/null | base64 -w0)" || return 1
    [ "$tree_b64" = 'MTAwNjQ0IC5naXRpZ25vcmUAlNHsUH15Hqtzg1EXnBRd8pvY+50xMDA2NDQgc2VlZC50eHQA5QKFF2AH4Sa+3kqJSwl2PGwZT20=' ] || return 1
    raw="$(git --git-dir="$top" cat-file commit "$oid" 2>/dev/null; printf x)" || return 1
    raw="${raw%x}"
    count="$(git --git-dir="$top" cat-file -s "$oid" 2>/dev/null || true)"
    [ "${#raw}" = "$count" ] || return 1
    body="$raw"
    local nl=$'\n'
    [ "${body#"tree 831fc61e72b5c6b9570f91d6eabc272ad8f2899f$nl"}" != "$body" ] || return 1
    body="${body#"tree 831fc61e72b5c6b9570f91d6eabc272ad8f2899f$nl"}"
    author_line="${body%%"$nl"*}"
    body="${body#*"$nl"}"
    committer_line="${body%%"$nl"*}"
    body="${body#*"$nl"}"
    [ "${body#"$nl"}" != "$body" ] || return 1
    body="${body#"$nl"}"
    [ "$body" = "seed$nl" ] || return 1
    [ "${author_line#"author Fixture <fixture@example.invalid> "}" != "$author_line" ] || return 1
    a_epoch="${author_line#"author Fixture <fixture@example.invalid> "}"
    a_zone="${a_epoch#* }"
    a_epoch="${a_epoch%% *}"
    [ "$author_line" = "author Fixture <fixture@example.invalid> $a_epoch $a_zone" ] || return 1
    c1609_epoch_ok "$a_epoch" || return 1
    c1609_zone_ok "$a_zone" || return 1
    [ "${committer_line#"committer Fixture <fixture@example.invalid> "}" != "$committer_line" ] || return 1
    c_epoch="${committer_line#"committer Fixture <fixture@example.invalid> "}"
    c_zone="${c_epoch#* }"
    c_epoch="${c_epoch%% *}"
    [ "$committer_line" = "committer Fixture <fixture@example.invalid> $c_epoch $c_zone" ] || return 1
    c1609_epoch_ok "$c_epoch" || return 1
    c1609_zone_ok "$c_zone" || return 1
    expected="tree 831fc61e72b5c6b9570f91d6eabc272ad8f2899f"$'\n'"author Fixture <fixture@example.invalid> $a_epoch $a_zone"$'\n'"committer Fixture <fixture@example.invalid> $c_epoch $c_zone"$'\n\n'"seed"$'\n'
    [ "$raw" = "$expected" ] || return 1
    printf 'disposable-fixture classifier=v1 path=%s commit=%s\n' "$(rel_repo "$rep")" "$oid"
}

c1609_inventory_git_file() {
    local file="$1" value
    [ -e "$file" ] || { [ ! -L "$file" ]; return $?; }
    [ -f "$file" ] && [ ! -L "$file" ] && [ -r "$file" ] || return 2
    [ "$(stat -c %h -- "$file")" = 1 ] || return 2
    value="$(sha256sum < "$file" | cut -d' ' -f1)" || return 2
    printf '%s\0%s\0' "${file#"$top/"}" "$value" >> "$c1609_metadata_roster"
}

c1609_inventory_git_admin() {
    local admin="$1" name directory file
    for name in HEAD ORIG_HEAD FETCH_HEAD REBASE_HEAD BISECT_HEAD MERGE_AUTOSTASH AUTO_MERGE \
        index config config.worktree packed-refs shallow commondir gitdir; do
        c1609_inventory_git_file "$admin/$name" || return 2
    done
    for directory in "$admin/refs" "$admin/logs"; do
        [ -e "$directory" ] || { [ ! -L "$directory" ] || return 2; continue; }
        [ -d "$directory" ] && [ ! -L "$directory" ] || return 2
        find -P "$directory" -mindepth 1 -print0 > "$scratch/c1609-meta-paths" 2>/dev/null || return 2
        sort -z "$scratch/c1609-meta-paths" > "$scratch/c1609-meta-sorted" || return 2
        while IFS= read -r -d '' file; do
            [ -d "$file" ] && [ ! -L "$file" ] && continue
            c1609_inventory_git_file "$file" || return 2
        done < "$scratch/c1609-meta-sorted"
    done
}

c1609_inventory_git_state() {
    local c1609_metadata_roster="$scratch/c1609-local-metadata" admin value
    : > "$c1609_metadata_roster" || return 2
    c1609_inventory_git_admin "$top" || return 2
    if [ -e "$top/worktrees" ] || [ -L "$top/worktrees" ]; then
        [ -d "$top/worktrees" ] && [ ! -L "$top/worktrees" ] || return 2
        find -P "$top/worktrees" -mindepth 1 -maxdepth 1 -print0 > "$scratch/c1609-meta-admins" 2>/dev/null || return 2
        sort -z "$scratch/c1609-meta-admins" > "$scratch/c1609-meta-admins-sorted" || return 2
        while IFS= read -r -d '' admin; do
            [ -d "$admin" ] && [ ! -L "$admin" ] || return 2
            c1609_inventory_git_admin "$admin" || return 2
        done < "$scratch/c1609-meta-admins-sorted"
    fi
    value="$(sha256sum "$c1609_metadata_roster" | cut -d' ' -f1)" || return 2
    printf 'inventory-local-state=%s common=%s\n' "$value" "$(digest "$top")"
}

c1609_prepare_common() {
    local rep="${common_repo[$top]}"
    c1609_skip_publication=0
    [ "${C1609_CONTENT_V2:-}" = 1 ] || return 0
    # Only Git metadata is hashed. Agent evidence and ignored payloads are never
    # opened by this inventory. Hashes bind reflog sides and linked-admin files
    # as well as the ref/object roster, without exporting their raw bytes.
    c1609_inventory_git_state || refuse_unknown local-inventory 2 "$rep"
    # Hash names as well as OIDs: adding a published ref still changes the local
    # inventory. Remote advertisements are deliberately outside this digest.
    local ref_digest
    git --git-dir="$top" for-each-ref --format='%(refname)%00%(objectname)%00%(objecttype)' \
        > "$scratch/c1609-local-refs" 2>/dev/null || refuse_unknown local-inventory 2 "$rep"
    ref_digest="$(sha256sum "$scratch/c1609-local-refs" | cut -d' ' -f1)" || refuse_unknown local-inventory 2 "$rep"
    printf 'inventory-local-refs=%s common=%s\n' "$ref_digest" "$(digest "$top")" || refuse_unknown local-inventory 2 "$rep"
    if c1609_origin_configured; then
        return 0
    fi
    if c1609_seed_path "$rep"; then
        if c1609_seed_bytes "$rep"; then
            c1609_skip_publication=1
            return 0
        fi
        c1609_refuse_reason seed-bytes "$rep"
    fi
    git --git-dir="$top" rev-list --all 2>/dev/null || true
    c1609_refuse_reason no-remote-unclassified "$rep"
}

c1609_commit_published() {
    local oid="$1" count
    [ "${#comparisons[@]}" -gt 0 ] || return 1
    count="$( { printf '%s\n' "$oid"; printf '^%s\n' "${comparisons[@]}"; } | timeout --kill-after=5s 30s "${cmp[@]}" rev-list --count --stdin 2>/dev/null )" || return 1
    [ "$count" = 0 ]
}

c1609_object_published() {
    local oid="$1"
    if [ -s "$scratch/advertised" ] && grep -Fxq -- "$oid" "$scratch/advertised"; then
        return 0
    fi
    [ "${#comparisons[@]}" -gt 0 ] || return 1
    printf '%s\n' "${comparisons[@]}" | timeout --kill-after=5s 30s "${cmp[@]}" rev-list --objects --no-object-names --stdin 2>/dev/null \
        | grep -Fxq -- "$oid"
}

c1609_consider_fetch_head() {
    local file="$1" line oid flag note type rest
    [ "${C1609_CONTENT_V2:-}" = 1 ] || { consider_pseudoref "$file" FETCH_HEAD; return 0; }
    audit_check=fetch-head
    [ -f "$file" ] && [ ! -L "$file" ] || fail 2
    while IFS= read -r line || [ -n "$line" ]; do
        [ -n "$line" ] || continue
        case "$line" in
            *$'\t'*) ;;
            *) c1609_refuse_reason fetch-head-malformed "$audit_repo" ;;
        esac
        oid="${line%%$'\t'*}"
        rest="${line#*$'\t'}"
        case "$rest" in
            *$'\t'*) ;;
            *) c1609_refuse_reason fetch-head-malformed "$audit_repo" ;;
        esac
        flag="${rest%%$'\t'*}"
        note="${rest#*$'\t'}"
        case "$flag" in
            ''|not-for-merge) ;;
            *) c1609_refuse_reason fetch-head-malformed "$audit_repo" ;;
        esac
        [[ "$oid" =~ ^[0-9a-f]{40}$ ]] || c1609_refuse_reason fetch-head-malformed "$audit_repo"
        printf '%s\n' "$oid"
        type="$(git --git-dir="$top" cat-file -t "$oid" 2>/dev/null || printf missing)"
        case "$type" in
            missing) c1609_refuse_reason fetch-head-missing "$audit_repo" ;;
            commit)
                if c1609_commit_published "$oid"; then
                    printf 'published-fetch-head %s\n' "$oid"
                fi
                printf '%s\n' "$oid" >> "$scratch/tips"
                ;;
            blob|tree|tag)
                if c1609_object_published "$oid"; then
                    printf 'published-fetch-head %s\n' "$oid"
                else
                    printf '%s\n' "$oid" >> "$scratch/tips"
                fi
                ;;
            *) c1609_refuse_reason fetch-head-malformed "$audit_repo" ;;
        esac
        : "$note"
    done < "$file"
}

c1609_emit_side() {
    local role="$1" rel="$2" side="$3"
    [[ "$side" =~ ^[0-9a-f]{40}$ ]] || return 0
    [ "$side" = 0000000000000000000000000000000000000000 ] && return 0
    c1609_finding "$role" "$rel" reflog "\"$side\"" "\"$side\"" reflog
}

c1609_scan_git() {
    local base="$1" role="$2" gitdir repo rel porcelain log old new rest line oid
    [ -d "$base" ] || return 0
    while IFS= read -r -d '' gitdir; do
        case "$gitdir" in
            */.git) repo="${gitdir%/.git}" ;;
            *) continue ;;
        esac
        rel="$(rel_repo "$repo")"
        if ! git -C "$repo" rev-parse --verify HEAD >/dev/null 2>&1; then
            c1609_note_unvisited "$rel"
            continue
        fi
        if ! porcelain="$(git -C "$repo" status --porcelain --untracked-files=all 2>/dev/null)"; then
            c1609_note_unvisited "$rel"
            continue
        fi
        if [ -n "$porcelain" ]; then
            c1609_finding "$role" "$rel" dirty null null dirty
        fi
        if [ -d "$gitdir/logs" ]; then
            while IFS= read -r -d '' log; do
                [ -f "$log" ] && [ -r "$log" ] || { c1609_note_unvisited "$rel"; continue; }
                while IFS=' ' read -r old new rest || [ -n "${old:-}${new:-}" ]; do
                    [ -n "${old:-}" ] || continue
                    c1609_emit_side "$role" "$rel" "${old:-}"
                    c1609_emit_side "$role" "$rel" "${new:-}"
                done < "$log"
            done < <(find "$gitdir/logs" -type f -print0 2>/dev/null || true)
        fi
        if [ -f "$gitdir/FETCH_HEAD" ] && [ ! -L "$gitdir/FETCH_HEAD" ] && [ -r "$gitdir/FETCH_HEAD" ]; then
            while IFS= read -r line || [ -n "$line" ]; do
                [ -n "$line" ] || continue
                oid="${line%%$'\t'*}"
                if [[ "$oid" =~ ^[0-9a-f]{40}$ ]]; then
                    c1609_finding "$role" "$rel" fetch-head "\"$oid\"" "\"$oid\"" fetch-head
                fi
            done < "$gitdir/FETCH_HEAD"
        fi
    done < <(find "$base" -xdev -name .git -print0 2>/dev/null || true)
}

c1609_scan_evidence() {
    local base="$1" role="$2" path rel
    [ -d "$base" ] || return 0
    while IFS= read -r -d '' path; do
        case "$path" in
            */.git|*/.git/*) continue ;;
        esac
        rel="$(rel_repo "$path")"
        c1609_finding "$role" "$rel" evidence null null evidence-name
        [ -n "${c1609_block_reason:-}" ] || c1609_block_reason=evidence-name
    done < <(find "$base" -xdev -type f \( -name HEAD -o -name ORIG_HEAD -o -name FETCH_HEAD -o -name REBASE_HEAD -o -name BISECT_HEAD -o -name MERGE_HEAD \) -print0 2>/dev/null || true)
}

c1609_scan_hooks() {
    local base="$1" role="$2" path base_name
    [ -d "$base" ] || return 0
    while IFS= read -r -d '' path; do
        base_name="${path##*/}"
        case "$base_name" in
            *.sample) continue ;;
        esac
        if [ -f "$path" ] && [ ! -L "$path" ] && [ -x "$path" ]; then
            [ -n "${c1609_block_reason:-}" ] || c1609_block_reason=hook
            c1609_finding "$role" "$(rel_repo "$path")" hook null null hook
        fi
    done < <(find "$base" -xdev -path '*/.git/hooks/*' -type f -print0 2>/dev/null || true)
}

c1609_list_offenders() {
    local flag
    [ "${C1609_CONTENT_V2:-}" = 1 ] || return 0
    c1609_findings_complete=true
    c1609_unvisited_json=''
    c1609_block_reason=''
    c1609_scan_git "$root" work
    c1609_scan_evidence "$root" work
    c1609_scan_hooks "$root" work
    if [ -n "${tmp_root:-}" ]; then
        c1609_scan_git "$tmp_root" tmp
        c1609_scan_evidence "$tmp_root" tmp
        c1609_scan_hooks "$tmp_root" tmp
    fi
    flag=true
    [ "$c1609_findings_complete" = true ] || flag=false
    printf 'C1609_FINDINGS_JSON={"schema":2,"complete":%s,"unvisited":[%s]}\n' "$flag" "$c1609_unvisited_json"
}

c1609_finish_blocks() {
    [ "${C1609_CONTENT_V2:-}" = 1 ] || return 0
    c1609_verify_host_coverage
    [ -n "${c1609_block_reason:-}" ] || return 0
    printf 'audit check=content status=0 repo=. reason=%s\n' "$c1609_block_reason"
    printf 'RecycleGitAuditUnknown\n'
    exit 2
}
