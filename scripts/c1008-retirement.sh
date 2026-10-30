# CARD-1609 receipt coordinator. Sourced by c590-remote.sh. ASCII only.
# A Ready receipt is review material plus the host record apply consumes.
# proof.md is rendered from the audit rows and is never parsed to authorize a delete.

c1609_retirement_root() {
    printf '%s/retirement/%s' "$SERVER2_ROOT" "$C1008_OPERATION"
}

c1609_note_census_attempt() {
    local attempt="$1" delay="$2"
    mkdir -p -- "$CASE_DIR" || return 1
    printf '{"attempt":%s,"backoffSeconds":%s,"cause":"%s"}\n' \
        "$attempt" "$delay" "${C1008_TASK_ERROR:-}" >> "$CASE_DIR/c1609-census.jsonl"
}

# Four attempts. Backoff after attempts 1, 2 and 3 is 2s, 5s, 10s.
# Only Drift and Unstable are retried. A bound land or a malformed census returns immediately.
# Containers-already-removed does not forgive Drift on this path.
c1609_census_v2_loop() {
    local attempt=1 code delay
    while [ "$attempt" -le 4 ]; do
        code=0
        c1008_tasks_collect_attempt || code=$?
        if [ "$code" = 0 ]; then
            return 0
        fi
        case "${C1008_TASK_ERROR:-}" in
            "RecycleTaskCensusUnknown cause=Drift "*|"RecycleTaskCensusUnknown cause=Unstable")
                if [ "$attempt" -eq 4 ]; then
                    return "$code"
                fi
                case "$attempt" in
                    1) delay=2 ;;
                    2) delay=5 ;;
                    3) delay=10 ;;
                    *) return 2 ;;
                esac
                c1609_note_census_attempt "$attempt" "$delay" || return 2
                sleep "$delay" || return 2
                attempt=$((attempt + 1))
                ;;
            *) return "$code" ;;
        esac
    done
    return 2
}

c1609_copy_preview_rows() {
    local root="$1" line
    : > "$root/findings.jsonl"
    if [ -f "$CASE_DIR/preview-audit.out" ] && [ ! -L "$CASE_DIR/preview-audit.out" ]; then
        cp -f -- "$CASE_DIR/preview-audit.out" "$root/audit.txt" || return 1
        while IFS= read -r line; do
            case "$line" in
                *'"kind":'*) printf '%s\n' "$line" >> "$root/findings.jsonl" ;;
            esac
        done < "$CASE_DIR/preview-audit.out"
    fi
    printf '%s\n' \
        "CARD-1609 volume retirement preview" \
        "operation: $C1008_OPERATION" \
        "project: $C1008_PROJECT" \
        "state: ${2:-unknown}" \
        "Machine rows: findings.jsonl and audit.txt (path, kind, commit)." \
        "This file is a rendering of those rows. It is not parsed to authorize deletion." \
        > "$root/proof.md"
}

# Custody is checked on every open and on every ancestor. A sticky root-owned
# temporary directory is allowed only above the private operation boundary.
c1609_custody() {
    local path="$1" kind="$2" uid mode links type here
    [ ! -L "$path" ] || return 1
    if [ "$kind" = file ]; then
        [ -f "$path" ] || return 1
        read -r uid mode links < <(stat -c '%u %a %h' -- "$path") || return 1
        [ "$uid" = "$EUID" ] && [ "$links" = 1 ] && (( (8#$mode & 022) == 0 )) || return 1
        here="$(dirname -- "$path")"
    else
        here="$path"
    fi
    while :; do
        [ -d "$here" ] && [ ! -L "$here" ] || return 1
        read -r uid mode < <(stat -c '%u %a' -- "$here") || return 1
        [ "$uid" = "$EUID" ] || [ "$uid" = 0 ] || return 1
        if (( (8#$mode & 022) != 0 )); then
            # Sticky /tmp is outside the protected root; a writable operation or
            # retirement ancestor is never accepted on the strength of sticky.
            [ "$uid" = 0 ] && (( (8#$mode & 01000) != 0 )) &&
                [[ "$SERVER2_ROOT/" == "$here/"* ]] && [ "$here" != "$SERVER2_ROOT" ] || return 1
        fi
        [ "$here" != / ] || break
        here="$(dirname -- "$here")"
    done
}

# stdin -> one canonical private file. Both file and directory data must reach
# the filesystem before a Ready/intent acknowledgement. No partial file is read.
c1609_persist() {
    local file="$1" dir tmp
    dir="$(dirname -- "$file")"
    c1609_custody "$dir" dir || return 1
    if [ -e "$file" ] || [ -L "$file" ]; then c1609_custody "$file" file || return 1; fi
    tmp="$(umask 077; mktemp "$dir/record.XXXXXXXX")" || return 1
    if ! jq -cS . > "$tmp" || ! sync -f "$tmp" || ! mv -T -- "$tmp" "$file" || ! sync -f "$dir"; then
        [ ! -e "$tmp" ] || rm -f -- "$tmp"
        return 1
    fi
}

c1609_target_snapshot() {
    local name facts mount identity result='{}'
    for name in "${C1008_TARGETS[@]}"; do
        case "$name" in
            "${C1008_PROJECT}_work"|"${C1008_PROJECT}_runner-tmp"|"${C1008_PROJECT}_runner-state"|"${C1008_PROJECT}_dind-data") ;;
            *) return 1 ;;
        esac
        facts="$(c1008_volume "$name")" || return 1
        if [ "$facts" != null ]; then
            facts="$(printf '%s' "$facts" | jq -ce --arg name "$name" '
                if .Name!=$name or .Driver!="local" or (.CreatedAt|type)!="string" or .CreatedAt=="" or
                   (.Mountpoint|type)!="string" or (.Labels|type)!="object" then error("identity")
                else {Name,Driver,Options,CreatedAt,Mountpoint,Labels,Scope:(.Scope // "local")} end')" || return 1
            mount="$(printf '%s' "$facts" | jq -r .Mountpoint)"
            [ "$(sudo -n readlink -e -- "$mount")" = "$mount" ] && [ ! -L "$mount" ] || return 1
            identity="$(sudo -n stat -c '%d:%i:%Z' -- "$mount")" || return 1
            [[ "$identity" =~ ^[0-9]+:[0-9]+:[0-9]+$ ]] || return 1
            facts="$(printf '%s' "$facts" | jq -c --arg stat "$identity" --arg role "${name#"${C1008_PROJECT}_"}" '. + {hostStat:$stat,role:$role}')" || return 1
        fi
        result="$(printf '%s' "$result" | jq -c --arg name "$name" --argjson facts "$facts" '.[$name]=$facts')" || return 1
    done
    printf '%s' "$result" | jq -cS .
}

c1609_write_ready_receipt() {
    local root targets digest now daemon nonce snapshot
    root="$(c1609_retirement_root)"
    c1609_custody "$SERVER2_ROOT" dir || return 1
    [ ! -e "$root/preview.json" ] && [ ! -L "$root/preview.json" ] || return 1
    [ ! -L "$SERVER2_ROOT/retirement" ] && [ ! -L "$root" ] || return 1
    (umask 077; mkdir -p -- "$root") || return 1
    c1609_custody "$root" dir || return 1
    c1609_custody "$CASE_DIR/preview-audit.out" file || return 1
    targets="$(printf '%s\n' "${C1008_TARGETS[@]}" | jq -R . | jq -sc .)" || return 1
    snapshot="$(c1609_target_snapshot)" || return 1
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ -n "$daemon" ] && [[ "$daemon" != *[$'\n\r\t']* ]] || return 1
    digest="$(sha256sum "$CASE_DIR/preview-audit.out" | cut -d' ' -f1)" || return 1
    nonce="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')" || return 1
    [[ "$nonce" =~ ^[0-9a-f]{32}$ ]] || return 1
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)" || return 1
    jq -cn --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" --arg now "$now" \
        --arg digest "$digest" --arg image "${C994_IMAGE:-}" --arg daemon "$daemon" \
        --arg retiredAt "$C1008_RETIRED_AT" --arg nonce "$nonce" --argjson targets "$targets" --argjson fingerprints "$snapshot" \
        '{schema:2,policyVersion:1,operationId:$op,project:$project,mode:"scripted",state:"Ready",consumed:false,
          targets:$targets,image:$image,inventoryDigest:$digest,readyAt:$now,daemon:$daemon,
          retiredAt:$retiredAt,phaseNonce:$nonce,fingerprints:$fingerprints}' | c1609_persist "$root/preview.json" || return 1
    cat -- "$root/preview.json" | c1609_persist "$root/receipt.json" || return 1
    c1609_copy_preview_rows "$root" Ready || return 1
}

c1609_write_refused_preview() {
    local root reason="$1"
    root="$(c1609_retirement_root)"
    if [ -L "$SERVER2_ROOT/retirement" ] || [ -L "$root" ]; then
        return 0
    fi
    mkdir -p -- "$root" || return 0
    c1609_copy_preview_rows "$root" Refused || return 0
    printf '%s\n' "reason: $reason" >> "$root/proof.md" || return 0
}

c1609_require_ready_preview() {
    local root file tmp canonical now ready age daemon current digest
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    for tmp in "$file" "$root/preview.json"; do
        c1609_custody "$tmp" file || { printf 'reason=preview-required\n'; return 1; }
    done
    # Only the writer's canonical representation is accepted. Duplicate keys,
    # including nested duplicates, cannot equal their reserialized JSON object.
    canonical="$(jq -cS . "$file")" || return 1
    [ "$(cat -- "$file")" = "$canonical" ] && cmp -s -- "$file" "$root/preview.json" || return 1
    jq -e --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" '
      type=="object" and
      (keys==(["schema","policyVersion","operationId","project","mode","state","consumed","targets","image",
                "inventoryDigest","readyAt","daemon","retiredAt","phaseNonce","fingerprints"]|sort)) and
      .schema==2 and .policyVersion==1 and .state=="Ready" and .consumed==false and .mode=="scripted" and
      .operationId==$op and .project==$project and (.targets|type)=="array" and
      (.targets|length)==4 and (.targets|unique|length)==4 and
      (.fingerprints|type)=="object" and ((.fingerprints|keys)==(.targets|sort)) and
      (.phaseNonce|type)=="string" and (.phaseNonce|test("^[0-9a-f]{32}$")) and
      (.readyAt|type)=="string" and (.readyAt|test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")) and
      (.inventoryDigest|type)=="string" and (.inventoryDigest|test("^[0-9a-f]{64}$"))
    ' "$file" >/dev/null || return 1
    if [ -n "${C1008_EXPECTED_PROOF_DIGEST:-}" ]; then
        digest="$(sha256sum "$file" | cut -d' ' -f1)" || return 1
        [ "$digest" = "$C1008_EXPECTED_PROOF_DIGEST" ] || return 1
    fi
    now="$(date -u +%s)" || return 1
    ready="$(date -u -d "$(jq -r .readyAt "$file")" +%s)" || return 1
    [[ "$now" =~ ^[0-9]+$ && "$ready" =~ ^[0-9]+$ ]] || return 1
    age=$((now - ready))
    [ "$age" -ge -60 ] && [ "$age" -le 900 ] || return 1
    ready="$(date -u -d "$(jq -r .retiredAt "$file")" +%s)" || return 1
    [ "$ready" = "$(date -u -d "$C1008_RETIRED_AT" +%s)" ] || return 1
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ "$daemon" = "$(jq -r .daemon "$file")" ] || return 1
    current="$(c1609_target_snapshot)" || return 1
    [ "$current" = "$(jq -cS .fingerprints "$file")" ] || return 1
    jq '.consumed=true | .state="Applying"' "$file" | c1609_persist "$file" || return 1
    printf 'C1609_RETIREMENT state=Applying operation=%s\n' "$C1008_OPERATION"
}

c1609_mark_partial_refused() {
    local root file tmp
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    if [ ! -f "$file" ] || [ -L "$file" ]; then
        return 0
    fi
    tmp="$(mktemp "$root/receipt.XXXXXX")" || return 1
    jq -c '.state="PartialRefused"' "$file" > "$tmp" || { rm -f -- "$tmp"; return 1; }
    mv -f -- "$tmp" "$file" || return 1
}

# Successful temp deletion is Completed. Applying with no closure still blocks deploy.
c1609_mark_completed() {
    local root file tmp
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    if [ ! -f "$file" ] || [ -L "$file" ] || [ -L "$root" ]; then
        return 0
    fi
    tmp="$(mktemp "$root/receipt.XXXXXX")" || return 1
    jq -c '.state="Completed" | .consumed=true | .owner=null |
        .effects=((.targets // []) | map({name:.,result:"removed"})) |
        .intents=((.targets // []) | map({name:.,intent:"remove",result:"removed"}))' \
        "$file" > "$tmp" || { rm -f -- "$tmp"; return 1; }
    mv -f -- "$tmp" "$file" || return 1
}
