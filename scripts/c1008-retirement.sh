# CARD-1609 receipt coordinator. Sourced by c590-remote.sh. ASCII only.
# A Ready receipt is review material plus the host record apply consumes.
# proof.md is rendered from the audit rows and is never parsed to authorize a delete.

c1609_retirement_root() {
    printf '%s/retirement/%s' "$SERVER2_ROOT" "$C1008_OPERATION"
}

c1609_note_census_attempt() {
    local attempt="$1" delay="$2" started="$3" finished="$4"
    mkdir -p -- "$CASE_DIR" || return 1
    (umask 077; jq -cn --argjson attempt "$attempt" --argjson delay "$delay" \
        --argjson started "$started" --argjson finished "$finished" \
        --arg gate "$gate_id" --arg cause "${C1008_TASK_ERROR:-}" \
        '{gateId:$gate,attempt:$attempt,backoffSeconds:$delay,cause:$cause,
          startedMonotonicMs:$started,finishedMonotonicMs:$finished,elapsedMs:($finished-$started)}' \
        >> "$CASE_DIR/c1609-census.jsonl") || return 1
    sync -f "$CASE_DIR/c1609-census.jsonl"
}

c1609_monotonic_ms() {
    local uptime ignored
    read -r uptime ignored < /proc/uptime || return 1
    [[ "$uptime" =~ ^[0-9]+\.[0-9]+$ ]] || return 1
    awk -v seconds="$uptime" 'BEGIN { printf "%.0f\n", seconds * 1000 }'
}

# Four attempts. Backoff after attempts 1, 2 and 3 is 2s, 5s, 10s.
# Only Drift and Unstable are retried. A bound land or a malformed census returns immediately.
# Containers-already-removed does not forgive Drift on this path.
c1609_census_v2_loop() {
    local attempt=1 code delay started finished now gate_id
    local C1609_CENSUS_DEADLINE_MS
    started="$(c1609_monotonic_ms)" || return 2
    C1609_CENSUS_DEADLINE_MS=$((started + 180000))
    gate_id="${BASHPID}-${started}-${RANDOM}"
    while [ "$attempt" -le 4 ]; do
        now="$(c1609_monotonic_ms)" || return 2
        if [ "$now" -ge "$C1609_CENSUS_DEADLINE_MS" ]; then
            C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=Deadline'
            return 2
        fi
        started="$now"
        code=0
        c1008_tasks_collect_attempt || code=$?
        finished="$(c1609_monotonic_ms)" || return 2
        delay=0
        if [ "$finished" -ge "$C1609_CENSUS_DEADLINE_MS" ]; then
            C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=Deadline'; code=2
        fi
        case "${C1008_TASK_ERROR:-}" in
            "RecycleTaskCensusUnknown cause=Drift "*|"RecycleTaskCensusUnknown cause=Unstable")
                if [ "$code" != 0 ] && [ "$attempt" -lt 4 ]; then
                    case "$attempt" in 1) delay=2 ;; 2) delay=5 ;; 3) delay=10 ;; esac
                fi
                ;;
        esac
        if [ "$delay" != 0 ] && [ "$((finished + delay * 1000))" -ge "$C1609_CENSUS_DEADLINE_MS" ]; then
            C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=Deadline'; code=2; delay=0
        fi
        c1609_note_census_attempt "$attempt" "$delay" "$started" "$finished" || {
            C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=ReceiptUnavailable'; return 2;
        }
        now="$(c1609_monotonic_ms)" || return 2
        if [ "$now" -ge "$C1609_CENSUS_DEADLINE_MS" ]; then
            C1008_TASK_ERROR='RecycleTaskCensusUnknown cause=Deadline'; return 2
        fi
        [ "$code" != 0 ] || return 0
        [ "$delay" != 0 ] || return "$code"
        sleep "$delay" || return 2
        attempt=$((attempt + 1))
    done
    return 2
}

# Only local safety facts enter the preview/apply inventory comparison. A new
# advertised remote tip can prove the same retained objects without changing it.
c1609_inventory_digest() {
    local file="$1"
    [ -f "$file" ] && [ ! -L "$file" ] || return 1
    LC_ALL=C awk '/^(entry repo=|tip=|inventory-local-|repositories=|disposable-fixture|published-fetch-head)/' "$file" |
        LC_ALL=C sort | sha256sum | cut -d' ' -f1
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
    local name facts mount identity raw labels_digest result='{}'
    for name in "${C1008_TARGETS[@]}"; do
        case "$name" in
            "${C1008_PROJECT}_work"|"${C1008_PROJECT}_runner-tmp"|"${C1008_PROJECT}_runner-state"|"${C1008_PROJECT}_dind-data") ;;
            *) return 1 ;;
        esac
        facts="$(c1008_volume "$name")" || return 1
        if [ "$facts" != null ]; then
            raw="$(docker volume inspect "$name")" || return 1
            labels_digest="$(printf '%s' "$raw" | jq -ceS --arg name "$name" '
                if length!=1 or .[0].Name!=$name or (.[0].Labels|type)!="object" then error("identity")
                else .[0].Labels end' | sha256sum | cut -d' ' -f1)" || return 1
            facts="$(printf '%s' "$facts" | jq -ce --arg name "$name" '
                if .Name!=$name or .Driver!="local" or (.CreatedAt|type)!="string" or .CreatedAt=="" or
                   (.Mountpoint|type)!="string" or (.Labels|type)!="object" then error("identity")
                else {Name,Driver,Options,CreatedAt,Mountpoint,Scope:(.Scope // "local"),
                    Labels:(.Labels|with_entries(select(.key=="com.docker.compose.project" or
                      .key=="com.docker.compose.volume")))} end')" || return 1
            mount="$(printf '%s' "$facts" | jq -r .Mountpoint)"
            [ "$(sudo -n readlink -e -- "$mount")" = "$mount" ] && [ ! -L "$mount" ] || return 1
            identity="$(sudo -n stat -c '%d:%i:%z' -- "$mount")" || return 1
            [[ "$identity" =~ ^[0-9]+:[0-9]+:[0-9]{4}- ]] && [[ "$identity" != *[$'\n\r\t']* ]] || return 1
            facts="$(printf '%s' "$facts" | jq -c --arg stat "$identity" --arg role "${name#"${C1008_PROJECT}_"}" \
                --arg labels "$labels_digest" '. + {hostStat:$stat,role:$role,allLabelsDigest:$labels}')" || return 1
        fi
        result="$(printf '%s' "$result" | jq -c --arg name "$name" --argjson facts "$facts" '.[$name]=$facts')" || return 1
    done
    printf '%s' "$result" | jq -cS .
}

c1609_write_ready_receipt() {
    local root targets digest now daemon nonce snapshot
    root="$(c1609_retirement_root)"
    c1609_custody "$SERVER2_ROOT" dir || return 1
    [ ! -L "$SERVER2_ROOT/retirement" ] && [ ! -L "$root" ] || return 1
    (umask 077; mkdir -p -- "$root") || return 1
    c1609_custody "$root" dir || return 1
    c1609_custody "$CASE_DIR/preview-audit.out" file || return 1
    targets="$(printf '%s\n' "${C1008_TARGETS[@]}" | jq -R . | jq -sc .)" || return 1
    snapshot="$(c1609_target_snapshot)" || return 1
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ -n "$daemon" ] && [[ "$daemon" != *[$'\n\r\t']* ]] || return 1
    digest="$(c1609_inventory_digest "$CASE_DIR/preview-audit.out")" || return 1
    nonce="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')" || return 1
    [[ "$nonce" =~ ^[0-9a-f]{32}$ ]] || return 1
    [ ! -e "$root/preview-$nonce.json" ] && [ ! -L "$root/preview-$nonce.json" ] || return 1
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)" || return 1
    jq -cn --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" --arg now "$now" \
        --arg digest "$digest" --arg image "${C994_IMAGE:-}" --arg daemon "$daemon" \
        --arg retiredAt "$C1008_RETIRED_AT" --arg nonce "$nonce" --argjson targets "$targets" --argjson fingerprints "$snapshot" \
        '{schema:2,policyVersion:1,operationId:$op,project:$project,mode:"scripted",state:"Ready",consumed:false,
          targets:$targets,image:$image,inventoryDigest:$digest,readyAt:$now,daemon:$daemon,
          retiredAt:$retiredAt,phaseNonce:$nonce,fingerprints:$fingerprints}' | c1609_persist "$root/preview-$nonce.json" || return 1
    # Each nonce artifact is immutable. preview.json is only a current rendering.
    cat -- "$root/preview-$nonce.json" | c1609_persist "$root/preview.json" || return 1
    c1609_copy_preview_rows "$root" Ready || return 1
    cat -- "$root/preview-$nonce.json" | c1609_persist "$root/receipt.json" || return 1
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
    local root file tmp canonical now ready age daemon current digest nonce
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    c1609_custody "$file" file || { printf 'reason=preview-required\n'; return 1; }
    [ "$(stat -c %s -- "$file")" -le 1048576 ] || return 1
    nonce="$(jq -er '.phaseNonce | select(type=="string" and test("^[0-9a-f]{32}$"))' "$file")" || return 1
    for tmp in "$root/preview-$nonce.json"; do
        c1609_custody "$tmp" file || { printf 'reason=preview-required\n'; return 1; }
    done
    # Only the writer's canonical representation is accepted. Duplicate keys,
    # including nested duplicates, cannot equal their reserialized JSON object.
    canonical="$(jq -cS -s 'if length==1 then .[0] else error("envelope") end' "$file")" || return 1
    [ "$(cat -- "$file")" = "$canonical" ] && cmp -s -- "$file" "$root/preview-$nonce.json" || return 1
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
    if [ -n "${C1609_APPLY_INVENTORY_FILE:-}" ]; then
        digest="$(c1609_inventory_digest "$C1609_APPLY_INVENTORY_FILE")" || return 1
        [ "$digest" = "$(jq -r .inventoryDigest "$file")" ] || return 1
    fi
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
    jq '.state=(if ((.intents // [])|length)>0 then "PartialRefused" else "Refused" end)' "$file" |
        c1609_persist "$file"
}

c1609_target_intent() {
    local name="$1" file
    file="$(c1609_retirement_root)/receipt.json"
    c1609_custody "$file" file || return 1
    jq -e --arg name "$name" '.state=="Applying" and .consumed==true and
        (.targets|index($name)!=null) and (.fingerprints[$name]!=null) and
        all(.intents[]?; .name!=$name)' "$file" >/dev/null || return 1
    jq --arg name "$name" '.intents=((.intents // []) + [{name:$name,intent:"remove",result:"pending"}])' "$file" |
        c1609_persist "$file"
}

c1609_target_result() {
    local name="$1" result="$2" file
    file="$(c1609_retirement_root)/receipt.json"
    c1609_custody "$file" file || return 1
    case "$result" in removed|alreadyAbsent) ;; *) return 1 ;; esac
    if [ "$result" = removed ]; then
        jq -e --arg name "$name" 'any(.intents[]?; .name==$name and .result=="pending")' "$file" >/dev/null || return 1
    else
        jq -e --arg name "$name" '.fingerprints[$name]==null' "$file" >/dev/null || return 1
    fi
    jq --arg name "$name" --arg result "$result" '
        .intents=((.intents // [])|map(if .name==$name then .result=$result else . end)) |
        .effects=((.effects // []) + [{name:$name,result:$result}])' "$file" | c1609_persist "$file"
}

# Successful temp deletion is Completed. Applying with no closure still blocks deploy.
c1609_mark_completed() {
    local root file tmp
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    if [ ! -f "$file" ] || [ -L "$file" ] || [ -L "$root" ]; then
        return 0
    fi
    jq -e '. as $record | all(.targets[]; . as $name |
        any($record.effects[]?; .name==$name and (.result=="removed" or .result=="alreadyAbsent")))' "$file" >/dev/null || return 1
    jq '.state="Completed" | .consumed=true | .owner=null' "$file" | c1609_persist "$file"
}
