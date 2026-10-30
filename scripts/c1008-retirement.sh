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

c1609_write_ready_receipt() {
    local root tmp targets digest now
    root="$(c1609_retirement_root)"
    if [ -L "$SERVER2_ROOT/retirement" ] || [ -L "$root" ]; then
        return 1
    fi
    mkdir -p -- "$root" || return 1
    targets="$(printf '%s\n' "${C1008_TARGETS[@]}" | jq -R . | jq -sc .)" || return 1
    if [ -f "$CASE_DIR/preview-audit.out" ] && [ ! -L "$CASE_DIR/preview-audit.out" ]; then
        digest="$(sha256sum "$CASE_DIR/preview-audit.out" | awk '{print $1}')"
    else
        digest=none
    fi
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    tmp="$(mktemp "$root/receipt.XXXXXX")" || return 1
    jq -cn \
        --arg op "$C1008_OPERATION" \
        --arg project "$C1008_PROJECT" \
        --arg now "$now" \
        --arg digest "$digest" \
        --arg image "${C994_IMAGE:-}" \
        --argjson targets "$targets" \
        '{schema:2,policyVersion:1,operationId:$op,project:$project,mode:"scripted",state:"Ready",consumed:false,targets:$targets,image:$image,inventoryDigest:$digest,readyAt:$now}' \
        > "$tmp" || { rm -f -- "$tmp"; return 1; }
    mv -f -- "$tmp" "$root/receipt.json" || return 1
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
    local root file tmp
    root="$(c1609_retirement_root)"
    file="$root/receipt.json"
    if [ ! -f "$file" ] || [ -L "$file" ] || [ -L "$root" ]; then
        printf 'reason=preview-required\n'
        return 1
    fi
    if ! jq -e --arg op "$C1008_OPERATION" --arg project "$C1008_PROJECT" \
        '.schema==2 and .policyVersion==1 and .state=="Ready" and .consumed==false and .operationId==$op and .project==$project and (.targets|type)=="array"' \
        "$file" >/dev/null; then
        printf 'reason=preview-required\n'
        return 1
    fi
    tmp="$(mktemp "$root/receipt.XXXXXX")" || return 1
    jq -c '.consumed=true | .state="Applying"' "$file" > "$tmp" || { rm -f -- "$tmp"; return 1; }
    mv -f -- "$tmp" "$file" || return 1
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
