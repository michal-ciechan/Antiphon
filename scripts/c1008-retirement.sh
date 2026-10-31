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
    sync -- "$CASE_DIR/c1609-census.jsonl"
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

# Current runtime is protected host configuration, never a creating SHA or a
# cleanup receipt. Resolve a mutable reference once per phase; helpers receive
# only the resulting installed digest. The explicit Docker --user remains 1654.
c1609_resolve_current_audit() {
    local file="$SERVER2_ROOT/audit-current.json" canonical ref image
    c1609_custody "$file" file || return 1
    [ "$(stat -c %s -- "$file")" -le 8192 ] || return 1
    canonical="$(jq -cS -s 'if length==1 then .[0] else error("runtime") end' "$file" 2>/dev/null)" || return 1
    [ "$(cat -- "$file")" = "$canonical" ] || return 1
    jq -e 'type=="object" and keys==["image","policyVersion","protocol","schema"] and
        .schema==1 and .protocol==2 and .policyVersion==1 and (.image|type)=="string" and
        (.image|length)<=256 and (.image|test("^[A-Za-z0-9][A-Za-z0-9._/:@-]*$"))' "$file" >/dev/null 2>&1 || return 1
    ref="$(jq -r .image "$file")" || return 1
    image="$(docker image inspect -f '{{.Id}}' "$ref" 2>/dev/null)" || return 1
    [[ "$image" =~ ^sha256:[0-9a-f]{64}$ ]] || return 1
    if [[ "$ref" == sha256:* ]]; then [ "$ref" = "$image" ] || return 1; fi
    printf '%s\n' "$image"
}

# Deployment installs current tooling after the image build. This is installation
# provenance, not a requirement to recover that build when retiring old content.
c1609_publish_current_audit() {
    local ref="$1" image
    c1609_custody "$SERVER2_ROOT" dir || return 1
    image="$(docker image inspect -f '{{.Id}}' "$ref" 2>/dev/null)" || return 1
    [[ "$image" =~ ^sha256:[0-9a-f]{64}$ ]] || return 1
    jq -ncS --arg image "$image" '{schema:1,protocol:2,policyVersion:1,image:$image}' |
        c1609_persist "$SERVER2_ROOT/audit-current.json"
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
    # GNU sync with file operands fsyncs those objects. -f would syncfs the
    # containing filesystem, unnecessarily coupling each intent to unrelated IO.
    if ! jq -cS . > "$tmp" || ! sync -- "$tmp" || ! mv -T -- "$tmp" "$file" || ! sync -- "$dir"; then
        [ ! -e "$tmp" ] || rm -f -- "$tmp"
        return 1
    fi
}

c1609_target_snapshot() {
    local name facts mount identity raw labels_digest result='{}'
    local -a names=("${C1008_TARGETS[@]}")
    if [ "$#" = 1 ]; then names=("$1"); fi
    for name in "${names[@]}"; do
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

# Trusted control-host configuration, never an audited volume's origin list.
c1609_export_trusted_origins() {
    local requests="$1" destination="$2" registry="$SERVER2_ROOT/retirement-origins.json"
    [ -s "$requests" ] || return 0
    c1609_read_canonical "$registry" >/dev/null || return 1
    jq -e --arg project "$C1008_PROJECT_ID" '
      keys==["projects","schema"] and .schema==1 and (.projects|type)=="object" and
      all(.projects|to_entries[]; (.key|test("^[0-9a-f-]{36}$")) and
        (.value|keys)==["origins"] and (.value.origins|type)=="array" and
        (.value.origins|length)==(.value.origins|unique|length) and
        all(.value.origins[];type=="string" and length>0)) and .projects[$project]!=null
      ' "$registry" >/dev/null || return 1
    jq -Rse --arg project "$C1008_PROJECT_ID" --slurpfile registry "$registry" '
      split("\n")|map(select(length>0))|all(.[];
        capture("^C1008_PRIVATE_ORIGIN repo=[A-Za-z0-9._/-]+ origin=(?<origin>[^ ]+) originHash=[0-9a-f]{64} proofName=[0-9a-f]{64}$").origin as $origin |
        $registry[0].projects[$project].origins|index($origin)!=null)
      ' "$requests" >/dev/null || return 1
    jq -cS --arg project "$C1008_PROJECT_ID" '{schema:1,projectId:$project,origins:.projects[$project].origins}' "$registry" |
        c1609_persist "$destination"
}

# A long audit or census wait may age the host observation. Until a renewed
# exchange is available, stop before Ready/intent rather than reuse old proof.
c1609_publication_fresh() {
    local proof="${C1008_HOST_PROOF:-}" file name completed now count=0
    [ -n "$proof" ] || return 0
    c1609_custody "$proof" dir || return 1
    now="$(date -u +%s)" || return 1
    for file in "$proof"/*; do
        [ -e "$file" ] && [ ! -L "$file" ] || return 1
        name="${file##*/}"
        if [[ "$name" =~ ^[0-9a-f]{64}\.bundle$ ]]; then
            c1609_custody "$file" file || return 1
            continue
        fi
        [[ "$name" =~ ^[0-9a-f]{64}$ ]] || return 1
        c1609_custody "$file" file || return 1
        [ "$(sed -n '1p' "$file")" = 'c1008-host-proof 2' ] &&
          [ "$(sed -n '2p' "$file")" = "operation $C1008_OPERATION" ] &&
          [ "$(sed -n '6p' "$file")" = "nonce ${C1609_PROOF_NONCE:-}" ] &&
          [ "$(sed -n '7p' "$file")" = "inventory ${C1609_PROOF_INVENTORY:-}" ] || return 1
        completed="$(sed -n '11s/^completed //p' "$file")" || return 1
        [[ "$completed" =~ ^(0|[1-9][0-9]{0,11})$ ]] &&
          [ "$((now-completed))" -ge -60 ] && [ "$((now-completed))" -le 900 ] || return 1
        count=$((count+1))
    done
    [ "$count" -gt 0 ]
}

# The credential-capable manual entry commissions a short-lived, owner-protected
# host grant after operator authentication. Neither a task token, an actor string
# nor an inventory-authored document is a grant. The operator credential itself
# never crosses to this host record or to an audit helper. Same-account host
# custody is the authority boundary, as it is for the protected Ready record.
c1609_manual_attestation() {
    local phase="$1" fingerprints="$2" digest="$3" mode="${C1008_MODE:-scripted}"
    local file="${C1008_ATTESTATION_FILE:-}" grant canonical now issued expires stamp mount
    case "$mode" in scripted) printf '{}'; return 0 ;; manual) ;; *) return 1 ;; esac
    grant="${C1008_MANUAL_AUTHORITY_FILE:-}"
    [ "$grant" = "$SERVER2_ROOT/manual-authority/$C1008_OPERATION.json" ] || return 1
    for canonical in "$grant" "$file"; do
        c1609_custody "$canonical" file || return 1
        [ "$(stat -c %s -- "$canonical")" -le 1048576 ] || return 1
        # Canonical JSON also rejects duplicate keys, including nested ones.
        [ "$(cat -- "$canonical")" = "$(jq -cS -s 'if length==1 then .[0] else error("attestation") end' "$canonical")" ] || return 1
        [ "$(readlink -e -- "$canonical")" = "$canonical" ] || return 1
        while IFS= read -r mount; do
            [ -n "$mount" ] || return 1
            [[ "$canonical/" != "$mount/"* ]] || return 1
        done < <(printf '%s' "$fingerprints" | jq -r '.[]|select(.!=null)|.Mountpoint')
    done
    jq -e --arg op "$C1008_OPERATION" --arg phase "$phase" --arg digest "$digest" \
      --argjson fingerprints "$fingerprints" --slurpfile grant "$grant" '
      $grant[0] as $g |
      ($g|keys)==["actor","authorityRef","expiresAt","issuedAt","operationId","schema","targets"] and
      $g.schema==1 and $g.operationId==$op and
      ($g.actor|type)=="string" and ($g.actor|test("^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$")) and
      ($g.authorityRef|type)=="string" and ($g.authorityRef|test("^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$")) and
      all([$g.issuedAt,$g.expiresAt][]; type=="string" and test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")) and
      keys==["actor","authorityRef","fingerprints","mode","operationId","phase","previewDigest","reason","schema","targets","utc"] and
      .schema==1 and .mode=="manual" and .phase==$phase and .operationId==$op and
      .actor==$g.actor and .authorityRef==$g.authorityRef and
      (.reason|type)=="string" and (.reason|length)<=4096 and (.reason|test("\\S")) and
      (.utc|type)=="string" and (.utc|test("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")) and
      (.targets|type)=="array" and (.targets|length)==4 and (.targets|unique|length)==4 and
      (.targets|sort)==($fingerprints|keys) and (.targets|sort)==($g.targets|sort) and
      .fingerprints==$fingerprints and
      (if $phase=="Preview" then .previewDigest==null
       else .previewDigest==$digest and ($digest|test("^[0-9a-f]{64}$")) end)
      ' "$file" >/dev/null || return 1
    now="$(date -u +%s)" || return 1
    issued="$(date -u -d "$(jq -r .issuedAt "$grant")" +%s)" || return 1
    expires="$(date -u -d "$(jq -r .expiresAt "$grant")" +%s)" || return 1
    stamp="$(date -u -d "$(jq -r .utc "$file")" +%s)" || return 1
    [ "$issued" -le "$((now + 60))" ] && [ "$expires" -ge "$now" ] &&
      [ "$expires" -gt "$issued" ] && [ "$((expires - issued))" -le 900 ] &&
      [ "$((now - stamp))" -ge -60 ] && [ "$((now - stamp))" -le 900 ] || return 1
    jq -cnS --slurpfile attestation "$file" --slurpfile grant "$grant" \
      '{attestation:$attestation[0],authority:$grant[0]}'
}

c1609_recheck_target() {
    local name="$1" file current expected daemon
    file="$(c1609_retirement_root)/receipt.json"
    c1609_custody "$file" file || return 1
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ "$daemon" = "$(jq -r .daemon "$file")" ] || return 1
    current="$(c1609_target_snapshot "$name")" || return 1
    expected="$(jq -cS --arg name "$name" '{($name):.fingerprints[$name]}' "$file")" || return 1
    [ "$current" = "$expected" ]
}

c1609_write_ready_receipt() {
    local root targets digest now daemon nonce snapshot manual
    root="$(c1609_retirement_root)"
    [ ! -e "$root/closure.json" ] && [ ! -L "$root/closure.json" ] &&
      [ ! -e "$root/close-pending.json" ] && [ ! -L "$root/close-pending.json" ] || return 1
    c1609_custody "$SERVER2_ROOT" dir || return 1
    [ ! -L "$SERVER2_ROOT/retirement" ] && [ ! -L "$root" ] || return 1
    (umask 077; mkdir -p -- "$root") || return 1
    c1609_custody "$root" dir || return 1
    c1609_custody "$CASE_DIR/preview-audit.out" file || return 1
    targets="$(printf '%s\n' "${C1008_TARGETS[@]}" | jq -R . | jq -sc .)" || return 1
    snapshot="$(c1609_target_snapshot)" || return 1
    manual="$(c1609_manual_attestation Preview "$snapshot" '')" || return 1
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ -n "$daemon" ] && [[ "$daemon" != *[$'\n\r\t']* ]] || return 1
    digest="$(c1609_inventory_digest "$CASE_DIR/preview-audit.out")" || return 1
    nonce="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')" || return 1
    [[ "$nonce" =~ ^[0-9a-f]{32}$ ]] || return 1
    [ ! -e "$root/preview-$nonce.json" ] && [ ! -L "$root/preview-$nonce.json" ] || return 1
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)" || return 1
    c1609_phase_preview_issued || return 1
    jq -cn --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" --arg now "$now" \
        --arg digest "$digest" --arg image "${C994_IMAGE:-}" --arg daemon "$daemon" \
        --arg retiredAt "$C1008_RETIRED_AT" --arg nonce "$nonce" --argjson targets "$targets" --argjson fingerprints "$snapshot" \
        --arg mode "${C1008_MODE:-scripted}" --argjson manual "$manual" \
        '({schema:2,policyVersion:1,operationId:$op,project:$project,mode:$mode,state:"Ready",consumed:false,
          targets:$targets,image:$image,inventoryDigest:$digest,readyAt:$now,daemon:$daemon,
          retiredAt:$retiredAt,phaseNonce:$nonce,fingerprints:$fingerprints} + $manual)' | c1609_persist "$root/preview-$nonce.json" || return 1
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
    local root file tmp canonical now ready age daemon current digest nonce manual
    root="$(c1609_retirement_root)"
    [ ! -e "$root/closure.json" ] && [ ! -L "$root/closure.json" ] &&
      [ ! -e "$root/close-pending.json" ] && [ ! -L "$root/close-pending.json" ] || return 1
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
    jq -e --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" --arg mode "${C1008_MODE:-scripted}" '
      type=="object" and
      (keys==(["schema","policyVersion","operationId","project","mode","state","consumed","targets","image",
                "inventoryDigest","readyAt","daemon","retiredAt","phaseNonce","fingerprints"] +
                (if $mode=="manual" then ["attestation","authority"] else [] end)|sort)) and
      .schema==2 and .policyVersion==1 and .state=="Ready" and .consumed==false and .mode==$mode and
      ($mode=="scripted" or $mode=="manual") and
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
    digest="$(sha256sum "$file" | cut -d' ' -f1)" || return 1
    manual="$(c1609_manual_attestation Apply "$current" "$digest")" || return 1
    if [ -n "${C1609_APPLY_INVENTORY_FILE:-}" ]; then
        digest="$(c1609_inventory_digest "$C1609_APPLY_INVENTORY_FILE")" || return 1
        [ "$digest" = "$(jq -r .inventoryDigest "$file")" ] || return 1
    fi
    jq --argjson manual "$manual" '.consumed=true | .state="Applying" |
      if .mode=="manual" then .applyAttestation=$manual.attestation | .applyAuthority=$manual.authority else . end' \
      "$file" | c1609_persist "$file" || return 1
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

# Resume observes the complete set before changing the ledger. A missing name
# is attributable only to a durable intent, never to a Docker read failure.
# The immutable preview binds every non-ledger field of the mutable receipt.
c1609_prepare_resume() {
    local root file preview nonce canonical expected current name original intent
    local observed=() targets daemon stamp manual manual_digest
    root="$(c1609_retirement_root)"; file="$root/receipt.json"
    [ ! -e "$root/closure.json" ] && [ ! -L "$root/closure.json" ] &&
      [ ! -e "$root/close-pending.json" ] && [ ! -L "$root/close-pending.json" ] || return 1
    c1609_custody "$file" file || return 1
    [ "$(stat -c %s -- "$file")" -le 1048576 ] || return 1
    canonical="$(jq -cS -s 'if length==1 then .[0] else error("envelope") end' "$file")" || return 1
    [ "$(cat -- "$file")" = "$canonical" ] || return 1
    nonce="$(jq -er '.phaseNonce|select(type=="string" and test("^[0-9a-f]{32}$"))' "$file")" || return 1
    preview="$root/preview-$nonce.json"
    c1609_custody "$preview" file || return 1
    targets="$(printf '%s\n' "${C1008_TARGETS[@]}" | jq -R . | jq -sc 'sort')" || return 1
    jq -e --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" --arg mode "${C1008_MODE:-scripted}" --argjson targets "$targets" \
      --slurpfile preview "$preview" '
      . as $r | $preview[0] as $p |
      .schema==2 and .policyVersion==1 and .operationId==$op and .project==$project and .mode==$mode and
      .consumed==true and .owner==null and
      (.state=="Applying" or .state=="PartialRefused" or .state=="Refused" or .state=="Completed") and
      $p.state=="Ready" and $p.consumed==false and
      (del(.state,.consumed,.owner,.intents,.effects,.applyAttestation,.applyAuthority)==($p|del(.state,.consumed))) and
      (if $mode=="manual" then
        (.applyAttestation|keys)==["actor","authorityRef","fingerprints","mode","operationId","phase","previewDigest","reason","schema","targets","utc"] and
        .applyAttestation.schema==1 and .applyAttestation.mode=="manual" and .applyAttestation.phase=="Apply" and
        .applyAttestation.operationId==$op and .applyAttestation.fingerprints==$p.fingerprints and
        .applyAttestation.targets==$p.targets and .applyAttestation.actor==$p.attestation.actor and
        .applyAttestation.authorityRef==$p.attestation.authorityRef and
        (.applyAuthority|keys)==["actor","authorityRef","expiresAt","issuedAt","operationId","schema","targets"] and
        .applyAuthority.schema==1 and .applyAuthority.operationId==$op and
        .applyAuthority.actor==$p.attestation.actor and .applyAuthority.authorityRef==$p.attestation.authorityRef
       else (has("applyAttestation")|not) and (has("applyAuthority")|not) end) and
      (.targets|sort)==$targets and (.fingerprints|keys)==$targets and
      ((.intents // [])|type)=="array" and ((.effects // [])|type)=="array" and
      ((.intents // [])|map(.name)|length)==((.intents // [])|map(.name)|unique|length) and
      ((.effects // [])|map(.name)|length)==((.effects // [])|map(.name)|unique|length) and
      all(.intents[]?; . as $i | keys==["intent","name","result"] and
        ($targets|index($i.name)!=null) and $r.fingerprints[$i.name]!=null and .intent=="remove" and
        (.result=="pending" or .result=="removed" or .result=="removed-observed")) and
      all(.effects[]?; . as $e | keys==["name","result"] and ($targets|index($e.name)!=null) and
        (if .result=="alreadyAbsent" then $r.fingerprints[$e.name]==null
         else (.result=="removed" or .result=="removed-observed") and
           any($r.intents[]?; .name==$e.name and .result==$e.result) end)) and
      all(.intents[]?; . as $i | if .result=="pending" then all($r.effects[]?; .name!=$i.name)
        else any($r.effects[]?; .name==$i.name and .result==$i.result) end)
      ' "$file" >/dev/null || return 1
    if [ -n "${C1008_EXPECTED_PROOF_DIGEST:-}" ]; then
        [ "$(sha256sum "$file" | cut -d' ' -f1)" = "$C1008_EXPECTED_PROOF_DIGEST" ] || return 1
    fi
    manual="$(c1609_manual_attestation Apply "$(jq -cS .fingerprints "$preview")" "$(sha256sum "$preview" | cut -d' ' -f1)")" || return 1
    if [ "${C1008_MODE:-scripted}" = manual ]; then
        manual_digest="$(printf '%s\n' "$manual" | sha256sum | cut -d' ' -f1)" || return 1
        if [ -e "$root/resume-authority-$manual_digest.json" ] || [ -L "$root/resume-authority-$manual_digest.json" ]; then
            [ "$(c1609_read_canonical "$root/resume-authority-$manual_digest.json")" = "$manual" ] || return 1
        else
            printf '%s\n' "$manual" | c1609_persist "$root/resume-authority-$manual_digest.json" || return 1
        fi
    fi
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ "$daemon" = "$(jq -r .daemon "$file")" ] || return 1
    stamp="$(date -u -d "$(jq -r .retiredAt "$file")" +%s)" || return 1
    [ "$stamp" = "$(date -u -d "$C1008_RETIRED_AT" +%s)" ] || return 1
    for name in "${C1008_TARGETS[@]}"; do
        current="$(c1609_target_snapshot "$name")" || return 1
        current="$(printf '%s' "$current" | jq -cS --arg name "$name" '.[$name]')" || return 1
        original="$(jq -cS --arg name "$name" '.fingerprints[$name]' "$file")" || return 1
        intent="$(jq -r --arg name "$name" '[.intents[]?|select(.name==$name)][0].result // "none"' "$file")" || return 1
        if [ "$current" = null ]; then
            if [ "$original" != null ]; then
                case "$intent" in
                    pending) observed+=("$name") ;;
                    removed|removed-observed) ;;
                    *) return 1 ;;
                esac
            fi
        else
            [ "$current" = "$original" ] && { [ "$intent" = none ] || [ "$intent" = pending ]; } || return 1
        fi
    done
    jq '.state="Applying"' "$file" | c1609_persist "$file" || return 1
    for name in "${observed[@]}"; do
        c1609_target_result "$name" removed-observed || return 1
    done
}

c1609_target_intent() {
    local name="$1" file
    file="$(c1609_retirement_root)/receipt.json"
    c1609_custody "$file" file || return 1
    jq -e --arg name "$name" '.state=="Applying" and .consumed==true and
        (.targets|index($name)!=null) and (.fingerprints[$name]!=null)' "$file" >/dev/null || return 1
    if jq -e --arg name "$name" 'any(.intents[]?; .name==$name)' "$file" >/dev/null; then
        [ "${C1609_RESUME_VALIDATED:-0}" = 1 ] &&
            jq -e --arg name "$name" 'any(.intents[]?; .name==$name and .result=="pending")' "$file" >/dev/null
        return $?
    fi
    jq --arg name "$name" '.intents=((.intents // []) + [{name:$name,intent:"remove",result:"pending"}])' "$file" |
        c1609_persist "$file"
}

c1609_target_result() {
    local name="$1" result="$2" file
    file="$(c1609_retirement_root)/receipt.json"
    c1609_custody "$file" file || return 1
    case "$result" in removed|removed-observed|alreadyAbsent) ;; *) return 1 ;; esac
    if jq -e --arg name "$name" 'any(.effects[]?; .name==$name)' "$file" >/dev/null; then
        jq -e --arg name "$name" --arg result "$result" 'any(.effects[]?; .name==$name and .result==$result)' "$file" >/dev/null
        return $?
    fi
    if [ "$result" != alreadyAbsent ]; then
        jq -e --arg name "$name" 'any(.intents[]?; .name==$name and .result=="pending")' "$file" >/dev/null || return 1
    else
        jq -e --arg name "$name" '.fingerprints[$name]==null' "$file" >/dev/null || return 1
    fi
    jq --arg name "$name" --arg result "$result" '
        .intents=((.intents // [])|map(if .name==$name then .result=$result else . end)) |
        .effects=((.effects // []) + [{name:$name,result:$result}])' "$file" | c1609_persist "$file"
}

c1609_target_absent() {
    local name="$1" file
    file="$(c1609_retirement_root)/receipt.json"
    c1609_custody "$file" file || return 1
    # An earlier result is retained verbatim; absence does not invent a second effect.
    if jq -e --arg name "$name" 'any(.effects[]?; .name==$name)' "$file" >/dev/null; then return 0; fi
    c1609_target_result "$name" alreadyAbsent
}

# Successful temp deletion is Completed. Applying with no closure still blocks deploy.
c1609_mark_completed() {
    local root file tmp
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    if [ ! -f "$file" ] || [ -L "$file" ] || [ -L "$root" ]; then
        return 0
    fi
    jq -e '. as $record | .state=="Applying" and .consumed==true and
        all(.intents[]?; .result!="pending") and all(.targets[]; . as $name |
        any($record.effects[]?; .name==$name and (.result=="removed" or .result=="removed-observed" or .result=="alreadyAbsent")))' "$file" >/dev/null || return 1
    jq '.state="Completed" | .consumed=true | .owner=null' "$file" | c1609_persist "$file"
}

# Canonical protected records are the only inputs to Close. A duplicate key,
# linked file/ancestor, writable custody or incomplete document is unknown.
c1609_read_canonical() {
    local file="$1" body
    c1609_custody "$file" file || return 1
    [ "$(stat -c %s -- "$file")" -le 1048576 ] || return 1
    [ "$(readlink -e -- "$file")" = "$file" ] || return 1
    body="$(jq -cS -s 'if length==1 then .[0] else error("record") end' "$file")" || return 1
    [ "$(cat -- "$file")" = "$body" ] || return 1
    printf '%s\n' "$body"
}

c1609_lifecycle_valid() {
    local file="$1"
    c1609_read_canonical "$file" >/dev/null || return 1
    jq -e --arg op "$C1008_OPERATION" '
      keys==["active","children","complete","daemon","nonce","operationId","owner","readyPublished","schema"] and
      .schema==1 and .operationId==$op and (.active|type)=="boolean" and (.complete|type)=="boolean" and (.readyPublished|type)=="boolean" and
      (.daemon|type)=="string" and .daemon!="" and (.nonce|test("^[0-9a-f]{32}$")) and
      (.owner|keys)==["pid","startTicks"] and (.owner.pid|type)=="number" and
      .owner.pid>0 and .owner.pid==(.owner.pid|floor) and (.owner.startTicks|type)=="string" and
      (.owner.startTicks|test("^[0-9]+$")) and (.children|type)=="array" and
      ([.children[].name]|length)==([.children[].name]|unique|length) and
      all(.children[]; keys==["joined","label","name"] and (.joined|type)=="boolean" and
        (.label|test("^"+$op+":[0-9a-f]{16}$")) and .name==("c1008-preview-"+(.label|sub(":";"-"))))
      ' "$file" >/dev/null
}

c1609_phase_begin() {
    local root daemon nonce pid ticks previous_pid previous_ticks prior_nonce issued=false
    root="$(c1609_retirement_root)"
    c1609_custody "$SERVER2_ROOT" dir || return 1
    [ ! -L "$SERVER2_ROOT/retirement" ] && [ ! -L "$root" ] || return 1
    (umask 077; mkdir -p -- "$root") || return 1
    c1609_custody "$root" dir || return 1
    [ ! -e "$root/closure.json" ] && [ ! -L "$root/closure.json" ] &&
        [ ! -e "$root/close-pending.json" ] && [ ! -L "$root/close-pending.json" ] || return 1
    if [ -e "$root/lifecycle.json" ] || [ -L "$root/lifecycle.json" ]; then
        c1609_lifecycle_valid "$root/lifecycle.json" || return 1
        jq -e '.complete==true and all(.children[];.joined==true)' "$root/lifecycle.json" >/dev/null || return 1
        previous_pid="$(jq -r .owner.pid "$root/lifecycle.json")"
        previous_ticks="$(jq -r .owner.startTicks "$root/lifecycle.json")"
        if [ -e "/proc/$previous_pid" ]; then
            [ "$(awk '{sub(/^.*\) /, ""); print $20}' "/proc/$previous_pid/stat")" != "$previous_ticks" ] || return 1
        fi
        issued="$(jq -r .readyPublished "$root/lifecycle.json")"
        prior_nonce="$(jq -r .nonce "$root/lifecycle.json")"
        if [ ! -e "$root/lifecycle-$prior_nonce.json" ]; then
            cat -- "$root/lifecycle.json" | c1609_persist "$root/lifecycle-$prior_nonce.json" || return 1
        fi
    fi
    [ ! -e "$root/receipt.json" ] && [ ! -L "$root/receipt.json" ] || issued=true
    daemon="$(docker info --format '{{.ID}}')" || return 1
    [ -n "$daemon" ] && [[ "$daemon" != *[$'\n\r\t']* ]] || return 1
    pid="$BASHPID"
    ticks="$(awk '{ sub(/^.*\) /, ""); print $20 }' "/proc/$pid/stat")" || return 1
    [[ "$ticks" =~ ^[0-9]+$ ]] || return 1
    nonce="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')" || return 1
    [[ "$nonce" =~ ^[0-9a-f]{32}$ ]] || return 1
    jq -cnS --arg op "$C1008_OPERATION" --arg daemon "$daemon" --arg ticks "$ticks" --arg nonce "$nonce" --argjson pid "$pid" --argjson issued "$issued" \
      '{schema:1,operationId:$op,daemon:$daemon,nonce:$nonce,owner:{pid:$pid,startTicks:$ticks},active:true,complete:true,readyPublished:$issued,children:[]}' |
      c1609_persist "$root/lifecycle.json" || return 1
    C1609_PHASE_ROOT="$root"
    C1609_PHASE_PID="$pid"
}

c1609_phase_helper() {
    [ -n "${C1609_PHASE_ROOT:-}" ] || return 0
    local name="$1" label="$2" file="$C1609_PHASE_ROOT/lifecycle.json"
    c1609_lifecycle_valid "$file" || return 1
    jq --arg name "$name" --arg label "$label" '.children += [{name:$name,label:$label,joined:false}]' "$file" |
      c1609_persist "$file"
}

c1609_phase_helpers_joined() {
    [ -n "${C1609_PHASE_ROOT:-}" ] || return 0
    local file="$C1609_PHASE_ROOT/lifecycle.json" name raw code
    c1609_lifecycle_valid "$file" || return 1
    while IFS= read -r name; do
        code=0; raw="$(docker inspect "$name" 2>&1)" || code=$?
        [ "$code" = 1 ] && { [[ "${raw,,}" == *'no such object:'* ]] || [[ "${raw,,}" == *'no such container:'* ]]; } || return 1
    done < <(jq -r '.children[]|select(.joined==false)|.name' "$file")
    jq '.children|=map(.joined=true)' "$file" | c1609_persist "$file"
}

c1609_phase_end() {
    [ -n "${C1609_PHASE_ROOT:-}" ] || return 0
    # A command-substitution child cannot publish its still-running parent's join.
    [ "$BASHPID" = "${C1609_PHASE_PID:-}" ] || return 1
    local file="$C1609_PHASE_ROOT/lifecycle.json" nonce
    c1609_lifecycle_valid "$file" || return 1
    jq -e 'all(.children[];.joined==true)' "$file" >/dev/null || return 1
    nonce="$(jq -r .nonce "$file")" || return 1
    [ ! -e "$C1609_PHASE_ROOT/lifecycle-$nonce.json" ] && [ ! -L "$C1609_PHASE_ROOT/lifecycle-$nonce.json" ] || return 1
    jq '.active=false|.complete=true' "$file" | c1609_persist "$C1609_PHASE_ROOT/lifecycle-$nonce.json" || return 1
    cat -- "$C1609_PHASE_ROOT/lifecycle-$nonce.json" | c1609_persist "$file"
}

c1609_close_fail() {
    printf 'TempRetirementCloseRefused: %s\n' "$1" >&2
    return 2
}

# All containers, including exited containers, foreign projects, bind aliases
# and same-operation helpers. Export only a census digest, never raw labels.
c1609_close_census() {
    local receipt="$1" ids id raw snapshot='[]' source mount resolved expected
    ids="$(docker ps -aq --no-trunc)" || return 1
    [ "$(printf '%s\n' "$ids" | sed '/^$/d' | LC_ALL=C sort | uniq -d)" = '' ] || return 1
    while IFS= read -r id; do
        [ -n "$id" ] || continue
        [[ "$id" =~ ^[0-9a-f]{64}$ ]] || return 1
        raw="$(docker inspect "$id")" || return 1
        raw="$(printf '%s' "$raw" | jq -ceS --arg id "$id" '
          if type=="array" and length==1 and .[0].Id==$id and
             (.[0].Config.Labels|type)=="object" and all(.[0].Config.Labels[];type=="string") and
             (.[0].Mounts|type)=="array" and
             (.[0].State.Running|type)=="boolean" and (.[0].State.Status|type)=="string"
          then .[0]|{Id,Config:{Labels:.Config.Labels},State:{Running:.State.Running,Status:.State.Status},
            Mounts:(.Mounts|sort_by(.Destination,.Source,.Type))} else error("census") end')" || return 1
        printf '%s' "$raw" | jq -e --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" '
          (.Config.Labels["com.docker.compose.project"]!=$project) and
          ((.Config.Labels["io.antiphon.preview-audit"] // "")|startswith($op+":")|not)' >/dev/null || return 1
        printf '%s' "$raw" | jq -e --slurpfile record "$receipt" '
          all(.Mounts[]; (.Type=="bind" or .Type=="volume" or .Type=="tmpfs") and
            (.Destination|type)=="string" and (.Destination|startswith("/")) and (.RW|type)=="boolean" and
            (if .Type=="volume" then (.Name|type)=="string" and .Name!="" and
               (.Source|type)=="string" and (.Source|startswith("/")) and
               (.Name as $n | ($record[0].targets|index($n))==null)
             elif .Type=="bind" then (.Source|type)=="string" and (.Source|startswith("/")) else true end))' >/dev/null || return 1
        while IFS= read -r source; do
            resolved="$(sudo -n readlink -e -- "$source")" || return 1
            [ -n "$resolved" ] || return 1
            while IFS= read -r mount; do
                [ -n "$mount" ] || return 1
                # The original mount may now be absent. Its protected canonical
                # absolute name still bounds bind aliases into that former tree.
                expected="$(sudo -n readlink -m -- "$mount")" || return 1
                [ "$expected" = "$mount" ] || return 1
                [[ "$resolved/" != "$mount/"* && "$mount/" != "$resolved/"* ]] || return 1
            done < <(jq -r '.fingerprints[]|select(.!=null)|.Mountpoint' "$receipt")
        done < <(printf '%s' "$raw" | jq -r '.Mounts[]|select(.Type=="bind")|.Source')
        snapshot="$(printf '%s' "$snapshot" | jq -c --argjson item "$raw" '.+[$item]')" || return 1
    done <<< "$ids"
    printf '%s' "$snapshot" | jq -cS 'sort_by(.Id)' | sha256sum | cut -d' ' -f1
}

c1609_closure_valid() {
    local dir="$1" prior digest daemon
    local file="$dir/closure.json" C1008_OPERATION="${dir##*/}"
    [ ! -e "$dir/close-pending.json" ] && [ ! -L "$dir/close-pending.json" ] || return 1
    c1609_read_canonical "$file" >/dev/null || return 1
    c1609_read_canonical "$dir/receipt.json" >/dev/null || return 1
    c1609_lifecycle_valid "$dir/lifecycle.json" || return 1
    jq -e '.complete==true and all(.children[];.joined==true)' "$dir/lifecycle.json" >/dev/null || return 1
    prior="$(sha256sum "$dir/receipt.json" | cut -d' ' -f1)" || return 1
    digest="$(jq -cS 'del(.digest)' "$file" | sha256sum | cut -d' ' -f1)" || return 1
    daemon="$(docker info --format '{{.ID}}')" || return 1
    jq -e --arg prior "$prior" --arg digest "$digest" --arg daemon "$daemon" --slurpfile receipt "$dir/receipt.json" '
      keys==["actor","authority","censusDigest","daemon","digest","lifecycleDigest","observedAt","operationId","priorDigest","reason","reconciliations","schema","state","targets","toolingDigest","volumesAbsent"] and
      .schema==2 and .state=="Closed" and .priorDigest==$prior and .digest==$digest and
      .daemon==$daemon and .daemon==$receipt[0].daemon and .operationId==$receipt[0].operationId and
      .targets==$receipt[0].targets and (.volumesAbsent|sort)==(.targets|sort) and
      (.actor|test("^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$")) and
      (.authority|test("^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$")) and
      (.reason|type)=="string" and (.reason|test("\\S")) and
      (.toolingDigest|test("^[0-9a-f]{64}$")) and (.censusDigest|test("^[0-9a-f]{64}$")) and (.lifecycleDigest|test("^[0-9a-f]{64}$")) and
      (.reconciliations|type)=="array" and (.reconciliations|map(.name)|sort)==(.targets|sort) and
      all(.reconciliations[]; . as $o | keys==["name","result"] and
        .result==(if any($receipt[0].intents[]?;.name==$o.name) then "removed-observed" else "unattributed-absence" end))' "$file" >/dev/null || return 1
    [ "$(sha256sum "$dir/lifecycle.json" | cut -d' ' -f1)" = "$(jq -r .lifecycleDigest "$file")" ]
}

# Caller holds the same FD-8/FD-9 locks as supported writers through every read,
# fsync and the final reply. This transition has no delete or drain primitive.
c1609_close_host() {
    local expected="$1" actor="$2" authority="$3" reason64="$4"
    local root file phase original preview nonce targets daemon pid ticks observed census again name facts reason body digest entry idempotent=false
    [[ "$C1008_OPERATION" =~ ^c1008[0-9a-f]{32}$ && "$expected" =~ ^[0-9a-f]{64}$ ]] || { c1609_close_fail Context; return 2; }
    [ "$C1008_PROJECT" = antiphon-runner-temp ] || { c1609_close_fail Targets; return 2; }
    [[ "$actor" =~ ^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$ && "$authority" =~ ^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$ ]] || { c1609_close_fail Authority; return 2; }
    reason="$(printf '%s' "$reason64" | base64 -d)" || return 2
    [ "${#reason}" -le 4096 ] && [[ "$reason" =~ [^[:space:]] ]] || { c1609_close_fail ReasonFile; return 2; }
    root="$(c1609_retirement_root)"; file="$root/receipt.json"; phase="$root/lifecycle.json"
    c1609_read_canonical "$file" >/dev/null || { c1609_close_fail JournalUnknown; return 2; }
    [ "$(sha256sum "$file" | cut -d' ' -f1)" = "$expected" ] || { c1609_close_fail DigestMismatch; return 2; }
    c1609_close_receipt_valid "$file" || { c1609_close_fail JournalUnknown; return 2; }
    targets='["antiphon-runner-temp_dind-data","antiphon-runner-temp_runner-state","antiphon-runner-temp_runner-tmp","antiphon-runner-temp_work"]'
    jq -e --arg op "$C1008_OPERATION" --argjson targets "$targets" '
      .schema==2 and .policyVersion==1 and .operationId==$op and .project=="antiphon-runner-temp" and
      (.state|IN("Pending","Ready","Applying","Refused","Partial","PartialRefused","Completed")) and
      (.mode=="scripted" or .mode=="manual") and .owner==null and
      (.targets|sort)==$targets and (.fingerprints|keys)==$targets and
      ((.intents // [])|type)=="array" and ((.effects // [])|type)=="array" and
      ((.intents // [])|map(.name)|unique|length)==((.intents // [])|length) and
      all(.intents[]?; . as $i | keys==["intent","name","result"] and ($targets|index($i.name))!=null and
        .intent=="remove" and (.result|IN("pending","removed","removed-observed"))) and
      all(.effects[]?; . as $e | keys==["name","result"] and ($targets|index($e.name))!=null and
        (.result|IN("removed","removed-observed","alreadyAbsent")))' "$file" >/dev/null || { c1609_close_fail JournalUnknown; return 2; }
    nonce="$(jq -er '.phaseNonce|select(test("^[0-9a-f]{32}$"))' "$file")" || return 2
    preview="$root/preview-$nonce.json"
    c1609_read_canonical "$preview" >/dev/null || return 2
    c1609_close_receipt_valid "$preview" || return 2
    jq -e '.state=="Ready" and .consumed==false' "$preview" >/dev/null || return 2
    jq -e --slurpfile preview "$preview" '
      del(.state,.consumed,.owner,.intents,.effects,.applyAttestation,.applyAuthority)==($preview[0]|del(.state,.consumed))' "$file" >/dev/null || return 2
    daemon="$(docker info --format '{{.ID}}')" || return 2
    [ "$daemon" = "$(jq -r .daemon "$file")" ] || { c1609_close_fail Daemon; return 2; }
    c1609_lifecycle_valid "$phase" || { c1609_close_fail ChildrenUnknown; return 2; }
    jq -e --arg daemon "$daemon" '.daemon==$daemon and .complete==true and all(.children[];.joined==true)' "$phase" >/dev/null || { c1609_close_fail ChildrenUnknown; return 2; }
    pid="$(jq -r .owner.pid "$phase")"; ticks="$(jq -r .owner.startTicks "$phase")"
    if [ -e "/proc/$pid" ]; then
        observed="$(awk '{ sub(/^.*\) /, ""); print $20 }' "/proc/$pid/stat")" || return 2
        [ "$observed" != "$ticks" ] || { c1609_close_fail OwnerActive; return 2; }
    fi
    # Unknown sibling records cannot be hidden by closing a newer operation.
    for entry in "$SERVER2_ROOT/retirement"/*; do
        [ -e "$entry" ] || { [ ! -L "$entry" ] || return 2; continue; }
        [[ "${entry##*/}" =~ ^c1008[0-9a-f]{32}$ ]] && c1609_custody "$entry" dir || return 2
        c1609_read_canonical "$entry/receipt.json" >/dev/null || return 2
        (local C1008_OPERATION="${entry##*/}"; c1609_close_receipt_valid "$entry/receipt.json") || return 2
    done
    for name in $(printf '%s' "$targets" | jq -r '.[]'); do
        facts="$(c1008_volume "$name")" || { c1609_close_fail CensusUnknown; return 2; }
        [ "$facts" = null ] || { c1609_close_fail "TempVolumesRetained $name"; return 2; }
    done
    census="$(c1609_close_census "$file")" || { c1609_close_fail CensusUnknown; return 2; }
    again="$(c1609_close_census "$file")" || return 2
    [ "$again" = "$census" ] || { c1609_close_fail CensusDrift; return 2; }
    for name in $(printf '%s' "$targets" | jq -r '.[]'); do
        [ "$(c1008_volume "$name")" = null ] || { c1609_close_fail CensusUnknown; return 2; }
    done
    [ "$(sha256sum "$file" | cut -d' ' -f1)" = "$expected" ] || return 2
    if [ -e "$root/closure.json" ] || [ -L "$root/closure.json" ]; then
        # A prior failed fsync keeps close-pending. Re-observe above, validate the
        # exact pending appendix, flush it again, and finish without overwriting it.
        [ -e "$root/close-pending.json" ] || idempotent=true
        c1609_read_canonical "$root/closure.json" >/dev/null || return 2
        jq -e --arg digest "$expected" --arg actor "$actor" --arg authority "$authority" --arg reason "$reason" \
          '.priorDigest==$digest and .actor==$actor and .authority==$authority and .reason==$reason' "$root/closure.json" >/dev/null || return 2
        sync -- "$root/closure.json" && sync -- "$root" || return 2
    else
        observed="$(date -u +%Y-%m-%dT%H:%M:%SZ)" || return 2
        body="$(jq -cnS --arg op "$C1008_OPERATION" --arg prior "$expected" --arg actor "$actor" --arg authority "$authority" \
          --arg reason "$reason" --arg observed "$observed" --arg daemon "$daemon" --arg census "$census" \
          --arg tooling "${C1609_CLOSE_TOOLING_DIGEST:?}" --arg lifecycle "$(sha256sum "$phase" | cut -d' ' -f1)" --slurpfile receipt "$file" \
          '{schema:2,state:"Closed",operationId:$op,priorDigest:$prior,actor:$actor,authority:$authority,reason:$reason,
            observedAt:$observed,daemon:$daemon,censusDigest:$census,lifecycleDigest:$lifecycle,toolingDigest:$tooling,
            targets:$receipt[0].targets,volumesAbsent:$receipt[0].targets,
            reconciliations:[$receipt[0].targets[] as $n|{name:$n,result:(if any($receipt[0].intents[]?;.name==$n) then "removed-observed" else "unattributed-absence" end)}]}')" || return 2
        digest="$(printf '%s\n' "$body" | sha256sum | cut -d' ' -f1)" || return 2
        printf '%s\n' "$body" | c1609_persist "$root/close-pending.json" || return 2
        printf '%s\n' "$body" | jq --arg digest "$digest" '.+{digest:$digest}' | c1609_persist "$root/closure.json" || return 2
    fi
    if [ -e "$root/close-pending.json" ]; then
        c1609_read_canonical "$root/close-pending.json" >/dev/null || return 2
        [ "$(jq -cS 'del(.digest)' "$root/closure.json")" = "$(cat -- "$root/close-pending.json")" ] || return 2
        rm -- "$root/close-pending.json" || return 2
        if ! sync -- "$root"; then
            jq 'del(.digest)' "$root/closure.json" | c1609_persist "$root/close-pending.json" || true
            return 2
        fi
    fi
    c1609_closure_valid "$root" || return 2
    printf 'TempRetirementClosed operation=%s idempotent=%s\n' "$C1008_OPERATION" "$idempotent"
}

c1609_close_receipt_valid() {
    local file="$1"
    jq -e --arg op "$C1008_OPERATION" '
      ["consumed","daemon","fingerprints","image","inventoryDigest","mode","operationId","phaseNonce",
       "policyVersion","project","readyAt","retiredAt","schema","state","targets"] as $base |
      ["owner","intents","effects","attestation","authority","applyAttestation","applyAuthority"] as $ledger |
      type=="object" and (($base-keys)|length)==0 and ((keys-($base+$ledger))|length)==0 and
      .schema==2 and .policyVersion==1 and .operationId==$op and .project=="antiphon-runner-temp" and
      (.state|IN("Pending","Ready","Applying","Refused","Partial","PartialRefused","Completed")) and
      (.consumed|type)=="boolean" and (.mode=="scripted" or .mode=="manual") and
      (.image|test("^sha256:[0-9a-f]{64}$")) and (.inventoryDigest|test("^[0-9a-f]{64}$")) and
      (.phaseNonce|test("^[0-9a-f]{32}$")) and (.daemon|type)=="string" and .daemon!="" and
      (.readyAt|type)=="string" and (.retiredAt|type)=="string" and
      (.targets|sort)==["antiphon-runner-temp_dind-data","antiphon-runner-temp_runner-state","antiphon-runner-temp_runner-tmp","antiphon-runner-temp_work"] and
      (.fingerprints|keys)==(.targets|sort) and
      all(.fingerprints|to_entries[]; .key as $name | .value==null or
        (.value|keys)==["CreatedAt","Driver","Labels","Mountpoint","Name","Options","Scope","allLabelsDigest","hostStat","role"] and
        .value.Name==$name and .value.Driver=="local" and .value.Scope=="local" and
        (.value.CreatedAt|type)=="string" and .value.CreatedAt!="" and
        (.value.Mountpoint|type)=="string" and (.value.Mountpoint|startswith("/")) and
        (.value.allLabelsDigest|test("^[0-9a-f]{64}$")) and (.value.hostStat|test("^[0-9]+:[0-9]+:[0-9]{4}-")) and
        .value.role==($name|ltrimstr("antiphon-runner-temp_")) and
        .value.Labels["com.docker.compose.project"]=="antiphon-runner-temp" and
        .value.Labels["com.docker.compose.volume"]==.value.role and
        (.value.Options==null or .value.Options=={})) and
      ((.intents // [])|type)=="array" and ((.effects // [])|type)=="array" and
      (. as $r | all(.effects[]?; . as $e |
        if .result=="alreadyAbsent" then $r.fingerprints[$e.name]==null else
        any($r.intents[]?;.name==$e.name and .result==$e.result) end)) and
      (. as $r | all(.intents[]?; . as $i |
        if .result=="pending" then all($r.effects[]?;.name!=$i.name) else
        any($r.effects[]?;.name==$i.name and .result==$i.result) end)) and
      (if .state=="Completed" then . as $r | .consumed==true and
        all(.targets[];. as $n|any($r.effects[]?;.name==$n)) else true end)
      ' "$file" >/dev/null
}

# Once an operation may have published Ready, a missing journal is unknown.
c1609_phase_preview_issued() {
    [ -n "${C1609_PHASE_ROOT:-}" ] || return 0
    local file="$C1609_PHASE_ROOT/lifecycle.json"
    c1609_lifecycle_valid "$file" || return 1
    jq '.readyPublished=true' "$file" | c1609_persist "$file"
}
