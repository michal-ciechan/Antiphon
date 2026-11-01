# CARD-1609. Host-side role scan for a temp retirement preview. State and
# dind are not mounted into the git helper. Credential-shaped names are not
# copied into the finding. A lone sentinel file is the fixture marker, not content.
# Mount reads use sudo -n, the same elevation CARD-849 uses for Docker volume
# paths. Production apply is not root, and the data root is mode 0710.

c1609_volume_mount() {
    local name="$1" facts mountpoint resolved symlink_status=0
    c1609_mount_path=''
    c1609_mount_fault=''
    facts="$(c1008_volume "$name")" || return 2
    [ "$facts" != null ] || return 0
    c1609_mount_path="$(printf '%s' "$facts" | jq -r '.Mountpoint // empty')" || return 2
    mountpoint="$c1609_mount_path"
    [ -n "$mountpoint" ] || return 2
    if ! command -v sudo >/dev/null 2>&1 || ! sudo -n true; then
        c1609_mount_fault=elevation
        return 2
    fi
    sudo -n test -d "$mountpoint" || { c1609_mount_fault=unreadable; return 2; }
    sudo -n test -r "$mountpoint" || { c1609_mount_fault=unreadable; return 2; }
    sudo -n test -x "$mountpoint" || { c1609_mount_fault=unreadable; return 2; }
    sudo -n test -L "$mountpoint" || symlink_status=$?
    [ "$symlink_status" = 1 ] || { c1609_mount_fault=unreadable; return 2; }
    resolved="$(sudo -n readlink -e -- "$mountpoint" 2>/dev/null)" || { c1609_mount_fault=unreadable; return 2; }
    [ "$resolved" = "$mountpoint" ] || { c1609_mount_fault=unreadable; return 2; }
    return 0
}

c1609_scan_state() {
    local hit mountpoint status=0
    c1609_volume_mount "${TEMP_PROJECT}_runner-state" || status=$?
    if [ "$status" != 0 ]; then
        case "${c1609_mount_fault:-}" in
            unreadable) c1609_meta_reason=state-unreadable ;;
            elevation) c1609_meta_reason=state-elevation-unavailable ;;
        esac
        return 2
    fi
    [ -n "$c1609_mount_path" ] || return 0
    mountpoint="$c1609_mount_path"
    hit="$(sudo -n find -P "$mountpoint" -xdev \( -name auth.json -o -name .credentials.json -o -name credentials.json -o -name id_rsa -o -name id_ed25519 -o -name id_ecdsa -o -name '*.pem' -o -name '*.key' -o -name '*.p12' -o -name '*.pfx' \) -print -quit 2>/dev/null)" || status=$?
    if [ "$status" != 0 ]; then
        c1609_meta_reason=state-unreadable
        return 2
    fi
    [ -n "$hit" ] || return 0
    printf 'C1609_FINDING_JSON={"schema":2,"volume":"%s","role":"state","repo":null,"kind":"state","objectId":null,"commitId":null,"reason":"state-credential"}\n' \
        "${TEMP_PROJECT}_runner-state"
    c1609_meta_reason=state-credential
    return 1
}

c1609_scan_dind() {
    local path base mountpoint list find_status=0 status=0 file_status link_status dir_status
    c1609_volume_mount "${TEMP_PROJECT}_dind-data" || status=$?
    if [ "$status" != 0 ]; then
        case "${c1609_mount_fault:-}" in
            unreadable) c1609_meta_reason=dind-unreadable ;;
            elevation) c1609_meta_reason=dind-elevation-unavailable ;;
        esac
        return 2
    fi
    [ -n "$c1609_mount_path" ] || return 0
    mountpoint="$c1609_mount_path"
    list="$(mktemp)" || { c1609_meta_reason=dind-unreadable; return 2; }
    sudo -n find -P "$mountpoint" -mindepth 1 -maxdepth 1 -print0 >"$list" 2>/dev/null || find_status=$?
    if [ "$find_status" != 0 ]; then
        rm -f -- "$list"
        c1609_meta_reason=dind-unreadable
        return 2
    fi
    while IFS= read -r -d '' path; do
        base="${path##*/}"
        file_status=0
        sudo -n test -f "$path" || file_status=$?
        link_status=0
        sudo -n test -L "$path" || link_status=$?
        dir_status=0
        sudo -n test -d "$path" || dir_status=$?
        if [ "$file_status" = 0 ] && [ "$link_status" = 1 ] && [ "$base" = sentinel ]; then
            continue
        fi
        if [ "$dir_status" = 0 ] && [ "$link_status" = 1 ]; then
            case "$base" in
                overlay2|image|buildkit|tmp|builder|network|swarm|plugins|runtimes|containerd) continue ;;
            esac
        fi
        rm -f -- "$list"
        printf 'audit check=dind status=0 repo=. reason=dind-unknown\n'
        printf 'RecycleGitAuditUnknown\n'
        c1609_meta_reason=dind-unknown
        return 1
    done <"$list"
    rm -f -- "$list"
    return 0
}

c1609_role_metadata() {
    local state_status=0 dind_status=0 state_reason=''
    c1609_meta_reason=''
    c1609_scan_state || state_status=$?
    # State is reported first. Keep its diagnosis when dind also fails.
    state_reason="${c1609_meta_reason:-}"
    c1609_scan_dind || dind_status=$?
    if [ "$state_status" != 0 ]; then
        c1609_meta_reason="$state_reason"
        [ -n "$c1609_meta_reason" ] || c1609_meta_reason=state-credential
        return "$state_status"
    fi
    if [ "$dind_status" != 0 ]; then
        [ -n "$c1609_meta_reason" ] || c1609_meta_reason=dind-unknown
        return "$dind_status"
    fi
    return 0
}
