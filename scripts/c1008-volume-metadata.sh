# CARD-1609. Host-side role scan for a temp retirement preview. State and
# dind are not mounted into the git helper. Credential-shaped names are not
# copied into the finding. A lone sentinel file is the fixture marker, not content.

c1609_volume_mount() {
    local name="$1" facts
    c1609_mount_path=''
    facts="$(c1008_volume "$name")" || return 2
    [ "$facts" != null ] || return 0
    c1609_mount_path="$(printf '%s' "$facts" | jq -r '.Mountpoint // empty')"
    [ -n "$c1609_mount_path" ] && [ -d "$c1609_mount_path" ] && [ ! -L "$c1609_mount_path" ] && [ -r "$c1609_mount_path" ] && [ -x "$c1609_mount_path" ]
}

c1609_scan_state() {
    local hit
    c1609_volume_mount "${TEMP_PROJECT}_runner-state" || return 2
    [ -n "$c1609_mount_path" ] || return 0
    hit="$(find -P "$c1609_mount_path" -xdev \( \
        -name auth.json -o -name .credentials.json -o -name credentials.json \
        -o -name id_rsa -o -name id_ed25519 -o -name id_ecdsa \
        -o -name '*.pem' -o -name '*.key' -o -name '*.p12' -o -name '*.pfx' \
        \) -print -quit 2>/dev/null)" || return 2
    [ -n "$hit" ] || return 0
    printf 'C1609_FINDING_JSON={"schema":2,"volume":"%s","role":"state","repo":null,"kind":"state","objectId":null,"commitId":null,"reason":"state-credential"}\n' \
        "${TEMP_PROJECT}_runner-state"
    c1609_meta_reason=state-credential
    return 1
}

c1609_scan_dind() {
    local entry base
    c1609_volume_mount "${TEMP_PROJECT}_dind-data" || return 2
    [ -n "$c1609_mount_path" ] || return 0
    while IFS= read -r -d '' entry; do
        base="${entry##*/}"
        if [ -f "$entry" ] && [ ! -L "$entry" ] && [ "$base" = sentinel ]; then
            continue
        fi
        if [ -d "$entry" ] && [ ! -L "$entry" ]; then
            case "$base" in
                overlay2|image|buildkit|tmp|builder|network|swarm|plugins|runtimes|containerd) continue ;;
            esac
        fi
        printf 'audit check=dind status=0 repo=. reason=dind-unknown\n'
        printf 'RecycleGitAuditUnknown\n'
        c1609_meta_reason=dind-unknown
        return 1
    done < <(find -P "$c1609_mount_path" -mindepth 1 -maxdepth 1 -print0 2>/dev/null)
    return 0
}

c1609_role_metadata() {
    local state_status=0 dind_status=0
    c1609_meta_reason=''
    c1609_scan_state || state_status=$?
    c1609_scan_dind || dind_status=$?
    if [ "$state_status" != 0 ]; then
        [ -n "$c1609_meta_reason" ] || c1609_meta_reason=state-credential
        return "$state_status"
    fi
    if [ "$dind_status" != 0 ]; then
        [ -n "$c1609_meta_reason" ] || c1609_meta_reason=dind-unknown
        return "$dind_status"
    fi
    return 0
}
