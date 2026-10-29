#!/usr/bin/env bash
# CARD-1192 S1 workspace model, S2 race barriers, S3 npm volume reads, and S4 payload boundary.
# Extracted c849_* functions against a filesystem Docker model.
# C1192_Foreign_volume_is_refused_after_create checks work/mask acquisition.
# C1208_Foreign_cache_is_dropped_and_untouched checks foreign cache refusal, ledger removal and untouched volume; C1208_Legacy_container_replacement_is_untouched and C1208_Legacy_network_replacement_is_untouched check legacy replacement retention/refusal; C1208_Unacquired_roster_is_untouched checks unacquired consumer/helper retention; C1208_Acquired_objects_are_removed and C1208_Recorded_container_id_does_not_delete_replacement check acquired disposal and ID-only removal; C1208_Legacy_owned_markers_are_migrated checks verified legacy migration, and C1208_Unknown_identity_retains_residue checks unknown refusal/residue. CP-45..53 retain positive-absence checks. CARD-1207 records Compose project resources before teardown, probes each recorded name afterward, and refuses success unless absence is confirmed; live Docker W-1 is pending.
# Never source dispatch. Never call the host docker daemon.
set -euo pipefail
shopt -s nullglob

c1192_refuse() {
    printf 'refuse %s\n' "$1"
}

# Absolute path, no symlink component, no '.' or '..' segment.
c1192_walk_unlinked() {
    local path="$1" rest walk part
    [ -n "$path" ] || return 1
    case "$path" in
        /*) ;;
        *) return 1 ;;
    esac
    rest="${path#/}"
    walk=""
    while [ -n "$rest" ]; do
        part="${rest%%/*}"
        if [ -z "$part" ] || [ "$part" = "." ] || [ "$part" = ".." ]; then
            return 1
        fi
        walk="${walk}/${part}"
        if [ -L "$walk" ]; then
            return 1
        fi
        if [ ! -e "$walk" ]; then
            return 1
        fi
        case "$rest" in
            */*) rest="${rest#*/}" ;;
            *) rest="" ;;
        esac
    done
    return 0
}

# C1192_Harness_cleanup_stays_inside_its_private_directory checks refusal of existing paths/markers.
# Ownership is written only after mkdir succeeds.
c1192_claim_fresh() {
    local candidate="${1-}" canon token
    C1192_CLAIM_CANON=
    C1192_CLAIM_TOKEN=
    if [ -z "${candidate}" ]; then c1192_refuse empty-path; return 1; fi
    if [ -L "$candidate" ]; then c1192_refuse symlink; return 1; fi
    if [ -e "$candidate" ]; then
        if [ -e "${candidate}/.c1192-owned" ] || [ -L "${candidate}/.c1192-owned" ]; then
            c1192_refuse pre-existing-marker
            return 1
        fi
        c1192_refuse pre-existing-directory
        return 1
    fi
    if ! mkdir -- "$candidate"; then
        c1192_refuse mkdir-failed
        return 1
    fi
    if [ -e "${candidate}/.c1192-owned" ] || [ -L "${candidate}/.c1192-owned" ]; then
        c1192_refuse pre-existing-marker
        return 1
    fi
    if ! canon=$(realpath -s -- "$candidate"); then
        c1192_refuse canonical
        return 1
    fi
    if ! c1192_walk_unlinked "$canon"; then
        c1192_refuse symlink
        return 1
    fi
    token=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
    if [ -z "$token" ]; then
        c1192_refuse empty-token
        return 1
    fi
    printf '%s\n' "$token" > "${candidate}/.c1192-owned"
    C1192_CLAIM_CANON=$canon
    C1192_CLAIM_TOKEN=$token
    return 0
}

# C1192_Harness_cleanup_stays_inside_its_private_directory checks contained removal
# and retention of symlink, marker-mismatch and replaced-ancestor canaries.
c1192_cleanup_contained() {
    local private_root="${1-}" target="${2-}" token="${3-}"
    local root_canon target_canon marker stamp
    if [ -z "${private_root}" ]; then c1192_refuse empty-private; return 1; fi
    if [ -z "${target}" ]; then c1192_refuse empty-target; return 1; fi
    if [ -L "$private_root" ] || [ -L "$target" ]; then c1192_refuse symlink; return 1; fi
    if [ -z "${token}" ]; then c1192_refuse ownership-unrecorded; return 1; fi
    if [ ! -d "$private_root" ] || [ ! -d "$target" ]; then c1192_refuse not-directory; return 1; fi
    if ! c1192_walk_unlinked "$private_root" || ! c1192_walk_unlinked "$target"; then
        c1192_refuse symlink
        return 1
    fi
    if ! root_canon=$(realpath -s -- "$private_root"); then c1192_refuse canonical; return 1; fi
    if ! target_canon=$(realpath -s -- "$target"); then c1192_refuse canonical; return 1; fi
    if [ "$target_canon" = "$root_canon" ]; then c1192_refuse not-strictly-under; return 1; fi
    case "$target_canon/" in
        "$root_canon/"*) ;;
        *) c1192_refuse outside-private-root; return 1 ;;
    esac
    if ! c1192_walk_unlinked "$root_canon" || ! c1192_walk_unlinked "$target_canon"; then
        c1192_refuse symlink
        return 1
    fi
    marker="${target_canon}/.c1192-owned"
    if [ -L "$marker" ] || [ ! -f "$marker" ]; then c1192_refuse marker-missing; return 1; fi
    stamp=$(cat -- "$marker") || { c1192_refuse marker-unreadable; return 1; }
    if [ -z "$stamp" ] || [ "$stamp" != "$token" ]; then
        c1192_refuse ownership-mismatch
        return 1
    fi
    # Runs in a subshell so the held cwd cannot leak, and so a path swap cannot
    # redirect the caller's later commands onto a replaced ancestor.
    c1192_delete_acquired "$root_canon" "$target_canon" "$token" || return 1
}

# Hold target, validate its parent, then use relative names for removal.
# C1192_CLEANUP_AFTER_MARKER, when set, runs
# after the held marker read and before any removal.
c1192_delete_acquired() {
    local root_canon="${1-}" target_canon="${2-}" token="${3-}"
    local held_pwd held_id root_id root_pwd now_pwd now_id path_id stamp
    local parent_pwd parent_id child child_id
    (
    cd -P -- "$target_canon" || { c1192_refuse identity-unreadable; exit 1; }
    held_pwd=$(pwd -P) || { c1192_refuse identity-unreadable; exit 1; }
    held_id=$(stat -c '%d:%i' -- .) || { c1192_refuse identity-unreadable; exit 1; }
    if [ "$held_pwd" != "$target_canon" ] || [ -z "$held_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    if [ -L "$root_canon" ] || [ -L "$target_canon" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    root_id=$(stat -c '%d:%i' -- "$root_canon") || { c1192_refuse identity-unreadable; exit 1; }
    root_pwd=$(cd -P -- "$root_canon" && pwd -P) || { c1192_refuse identity-unreadable; exit 1; }
    if [ "$root_pwd" != "$root_canon" ] || [ -z "$root_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    cd -P -- "$target_canon" || { c1192_refuse identity-unreadable; exit 1; }
    now_pwd=$(pwd -P) || { c1192_refuse identity-unreadable; exit 1; }
    now_id=$(stat -c '%d:%i' -- .) || { c1192_refuse identity-unreadable; exit 1; }
    if [ "$now_pwd" != "$held_pwd" ] || [ "$now_id" != "$held_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    if [ -L ./.c1192-owned ] || [ ! -f ./.c1192-owned ]; then
        c1192_refuse marker-missing
        exit 1
    fi
    stamp=$(cat -- ./.c1192-owned) || { c1192_refuse marker-unreadable; exit 1; }
    if [ -z "$stamp" ] || [ "$stamp" != "$token" ]; then
        c1192_refuse ownership-mismatch
        exit 1
    fi
    if [ -n "${C1192_CLEANUP_AFTER_MARKER:-}" ]; then
        bash -- "$C1192_CLEANUP_AFTER_MARKER" || { c1192_refuse hook-failed; exit 1; }
    fi
    now_pwd=$(pwd -P) || { c1192_refuse identity-changed; exit 1; }
    now_id=$(stat -c '%d:%i' -- .) || { c1192_refuse identity-changed; exit 1; }
    if [ "$now_pwd" != "$held_pwd" ] || [ "$now_id" != "$held_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    if [ -L "$root_canon" ] || [ -L "$target_canon" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    path_id=$(stat -c '%d:%i' -- "$target_canon") || { c1192_refuse identity-changed; exit 1; }
    if [ "$path_id" != "$held_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    find . -mindepth 1 -delete || { c1192_refuse delete-failed; exit 1; }
    now_pwd=$(pwd -P) || { c1192_refuse identity-changed; exit 1; }
    now_id=$(stat -c '%d:%i' -- .) || { c1192_refuse identity-changed; exit 1; }
    if [ "$now_pwd" != "$held_pwd" ] || [ "$now_id" != "$held_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    cd -P -- .. || { c1192_refuse identity-changed; exit 1; }
    parent_pwd=$(pwd -P) || { c1192_refuse identity-changed; exit 1; }
    parent_id=$(stat -c '%d:%i' -- .) || { c1192_refuse identity-changed; exit 1; }
    if [ "$parent_pwd" != "$root_canon" ] || [ "$parent_id" != "$root_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    child=${target_canon##*/}
    case "$child" in
        ''|.|..|*/*) c1192_refuse identity-changed; exit 1 ;;
    esac
    if [ -L "./$child" ] || [ ! -d "./$child" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    child_id=$(stat -c '%d:%i' -- "./$child") || { c1192_refuse identity-changed; exit 1; }
    if [ "$child_id" != "$held_id" ]; then
        c1192_refuse identity-changed
        exit 1
    fi
    rmdir -- "./$child" || { c1192_refuse delete-failed; exit 1; }
    )
}

if [ "${1:-}" = "claim" ]; then
    if c1192_claim_fresh "${3:?candidate}"; then
        printf '%s\n' "$C1192_CLAIM_CANON"
        exit 0
    fi
    exit 1
fi
if [ "${1:-}" = "cleanup-contained" ]; then
    if c1192_cleanup_contained "${3-}" "${4-}" "${5-}"; then
        exit 0
    fi
    exit 1
fi

mode="${1:?mode}"
repo="${2:?repo}"
private_parent=$(mktemp -d /tmp/c1192-parent.XXXXXX) || exit 1
private_parent_canon=$(realpath -s -- "$private_parent") || exit 1
if ! c1192_claim_fresh "${private_parent_canon}/scratch"; then
    exit 1
fi
private=$C1192_CLAIM_CANON
private_token=$C1192_CLAIM_TOKEN
cleanup_private() {
    local target="${private:?}" token="${private_token:?}" parent
    if [ -z "$target" ] || [ -z "$token" ]; then
        c1192_refuse empty-path
        return 0
    fi
    parent=$(dirname -- "$target")
    c1192_cleanup_contained "$parent" "$target" "$token" || true
    if [ -n "${private_parent_canon:-}" ] \
        && [ ! -L "$private_parent_canon" ] \
        && [ -d "$private_parent_canon" ]; then
        case "$private_parent_canon" in
            /tmp/c1192-parent.*) rmdir -- "$private_parent_canon" 2>/dev/null || true ;;
        esac
    fi
}
trap cleanup_private EXIT
root=$(mktemp -d "${private:?}/c1192.XXXXXX") || exit 1
RUN=c1192fixture0001
export C1192_ROOT="$root"
export C1192_FIXTURE_TREE="${root:?}/fixture-tree"
export C1192_FAULT=
export C1192_CUT=
mkdir -p "$root/bin" "$root/volumes" "$root/containers" "$root/case" "$root/server2/cache"
printf '#!/bin/sh\nprintf "REAL-DOCKER %%s\\n" "$*" >> "$C1192_ROOT/real-docker"\nexit 97\n' > "$root/bin/docker"
chmod 755 "$root/bin/docker"
export PATH="$root/bin:$PATH"
: > "$root/events"
: > "$root/order"
: > "$root/chown.log"
: > "$root/real-docker"
: > "$root/argv"
: > "$root/race-mounts"
: > "$root/run-log"
: > "$root/sleeps"
remote="$repo/scripts/c590-remote.sh"
awk '/^(c849_[a-z_]+|case_runner_cache_inventory)\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote" \
    | while IFS= read -r line || [ -n "$line" ]; do
        case "$line" in
            *'SERVER2_ROOT="/tmp/c849-fixture-$RUN"'*)
                printf '%s\n' '    SERVER2_ROOT="${C1192_FIXTURE_TREE:?}"'
                ;;
            *) printf '%s\n' "$line" ;;
        esac
    done > "$root/functions"
# shellcheck disable=SC1091
source "$root/functions"

SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
CASE=runner-cache-fixture
CASE_DIR="$root/case"
c849_fixture_case_bind
SERVER2_ROOT="$root/server2"
CHECKOUT="$repo"
ROOT="$repo"
HOST_PROJECT=antiphon-runner
TEMP_PROJECT=antiphon-runner-temp
C849_READY="$SERVER2_ROOT/cache/ready"
WROTE=0
LANE=host
ADMITTED="antiphon-server2/session-testing:${SHA:0:12}"
IMAGE_ID=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
WORK="c849${RUN}-work"
MASK="c849${RUN}-mask"
WRITER="c849-${RUN}-writer"
READER="c849-${RUN}-reader"

require_lane() { [ "$1" = host ]; }
write_result() {
    local accepted="$1" diagnosis="$2" code="$3"
    if [ "${WROTE:-0}" = 1 ]; then exit "$code"; fi
    WROTE=1
    mkdir -p "$CASE_DIR"
    printf 'DIAGNOSIS=%s\n' "$diagnosis"
    exit "$code"
}
c849_lock() { :; }
sudo() { printf 'SUDO %s\n' "$*" >> "$C1192_ROOT/order"; return 1; }
chown() { printf '%s\n' "$*" >> "$C1192_ROOT/chown.log"; }
export -f chown
# Docker's /bin/sh must see the same fake ownership operation as Bash. Exported
# Bash functions are not inherited by dash, so put the stub on the child PATH too.
printf '%s\n' '#!/bin/sh' 'printf "%s\n" "$*" >> "$C1192_ROOT/chown.log"' > "$C1192_ROOT/bin/chown"
chmod +x "$C1192_ROOT/bin/chown"

fail() {
    printf 'C1192_FAIL %s\n' "$*"
    exit 1
}

event() { printf '%s\n' "$*" >> "$C1192_ROOT/events"; }
order_log() { printf '%s\n' "$1" >> "$C1192_ROOT/order"; }

reset_runtime() {
    rm -rf -- "${C1192_ROOT:?}/volumes" "${C1192_ROOT:?}/containers"
    if [ -n "${C1192_FIXTURE_TREE:-}" ] && [ -d "$C1192_FIXTURE_TREE" ] && [ ! -L "$C1192_FIXTURE_TREE" ]; then
        case "$C1192_FIXTURE_TREE" in
            "${C1192_ROOT:?}/"*) rm -rf -- "$C1192_FIXTURE_TREE" ;;
        esac
    fi
    mkdir -p "$C1192_ROOT/volumes" "$C1192_ROOT/containers"
    : > "$C1192_ROOT/events"
    : > "$C1192_ROOT/order"
    : > "$C1192_ROOT/chown.log"
    : > "$C1192_ROOT/argv"
    : > "$C1192_ROOT/race-mounts"
    : > "$C1192_ROOT/run-log"
    : > "$C1192_ROOT/sleeps"
    rm -f "$C1192_ROOT/probe-at-handoff" "$C1192_ROOT/shared-before" "$C1192_ROOT/private-before-release"
    rm -rf "$CASE_DIR/fixture-controller"
    rm -f "$CASE_DIR/.fixture-escape" "$CASE_DIR/.fixture-escape-identity"
    rm -f "$CASE_DIR"/.fixture-compose-*
    C849_COMPOSE_EMPTY_PROJECT=''
    rm -f "$CASE_DIR"/.fixture-fingerprint-*
    rm -f "$CASE_DIR"/.fixture-id-* "$CASE_DIR/.fixture-network" "$CASE_DIR/.fixture-donor" "$CASE_DIR/.fixture-busy" "$CASE_DIR/.fixture-npm-server" "$CASE_DIR/.fixture-prune-smoke"
    rm -f "$CASE_DIR/.fixture-volumes" "$CASE_DIR/.fixture-helpers" \
        "$CASE_DIR/fixture-cleanup-residue.txt" "$CASE_DIR/fixture-summary.txt" "$CASE_DIR/.fixture-controller-identity" "$CASE_DIR/result" \
        "$CASE_DIR/.control-PC-26" "$CASE_DIR/.fixture-volumes.workspace" \
        "$CASE_DIR/.fixture-live-probes"
    local stale
    for stale in "$C1192_ROOT"/killed-*; do
        rm -f -- "$stale"
    done
    : > "$CASE_DIR/fixture-controls.txt"
    : > "$CASE_DIR/fixture-control-variants.txt"
    C849_FIXTURE_FINALIZED=0
    C1192_FAULT=
    C1192_CUT=
}

plant_volume() {
    local name="$1" driver="$2" options="$3" owner="$4" run_id="$5" role="$6"
    local dir="$C1192_ROOT/volumes/$name"
    mkdir -p "$dir/data"
    printf '%s\n' "$driver" > "$dir/driver"
    printf '%s\n' "$options" > "$dir/options"
    printf '%s\n' "$owner" > "$dir/label-owner"
    printf '%s\n' "$run_id" > "$dir/label-run"
    printf '%s\n' "$role" > "$dir/label-role"
}

plant_container() {
    local name="$1" owner="$2" run_id="$3" role="$4" image="$5" mount="$6"
    local dir="$C1192_ROOT/containers/$name"
    mkdir -p "$dir"
    printf '%s\n' "$(printf '%s:%s' "$name" "$(date +%s%N)" | sha256sum | cut -d' ' -f1)" > "$dir/id"
    printf '%s\n' "$owner" > "$dir/label-owner"
    printf '%s\n' "$run_id" > "$dir/label-run"
    printf '%s\n' "$role" > "$dir/label-role"
    printf '/%s\n' "$name" > "$dir/name"
    printf '2026-10-13T00:00:00Z\n' > "$dir/created"
    printf '%s\n' "$image" > "$dir/image"
    printf '%s\n' "$mount" > "$dir/mount"
}

volume_exists() { [ -d "$C1192_ROOT/volumes/$1" ]; }
container_exists() { [ -d "$C1192_ROOT/containers/$1" ]; }

# Line match without a pipe. grep -q under pipefail dies with SIGPIPE when
# the producer still has bytes to write.
c1192_has_exact_line() {
    local want="$1" text="$2" line
    while IFS= read -r line || [ -n "${line:-}" ]; do
        [ "$line" = "$want" ] && return 0
    done <<< "$text"
    return 1
}

c1192_has_substr() {
    local want="$1" text="$2" line
    while IFS= read -r line || [ -n "${line:-}" ]; do
        case "$line" in
            *"$want"*) return 0 ;;
        esac
    done <<< "$text"
    return 1
}

c1192_first_line_number() {
    local want="$1" text="$2" line n=0
    while IFS= read -r line || [ -n "${line:-}" ]; do
        n=$((n + 1))
        case "$line" in
            *"$want"*) printf '%s' "$n"; return 0 ;;
        esac
    done <<< "$text"
    return 1
}

c1192_file_has_substr() {
    local want="$1" file="$2" line
    [ -f "$file" ] && [ ! -L "$file" ] || return 1
    while IFS= read -r line || [ -n "${line:-}" ]; do
        case "$line" in
            *"$want"*) return 0 ;;
        esac
    done < "$file"
    return 1
}

# 0 when the snapshot has at least two restore probes and every one of them
# already carries a 64-hex id. A pending line is not yet safe to signal.
c1192_copied_live_cids() {
    local file="$1" pid cid name n=0
    [ -f "$file" ] && [ ! -L "$file" ] || return 1
    while read -r pid cid name || [ -n "${pid:-}" ]; do
        case "${name:-}" in
            *restore-p*) ;;
            *) continue ;;
        esac
        [[ "${cid:-}" =~ ^[0-9a-f]{64}$ ]] || return 1
        n=$((n + 1))
    done < "$file"
    [ "$n" -ge 2 ]
}

diagnosis_of() {
    local line last=""
    while IFS= read -r line || [ -n "${line:-}" ]; do
        case "$line" in
            DIAGNOSIS=*) last=$line ;;
        esac
    done <<< "$1"
    printf '%s' "${last#DIAGNOSIS=}"
}

create_count() {
    grep -c $'^volume-create\t' "$C1192_ROOT/events" 2>/dev/null || true
}

run_init() {
    local code=0
    C1192_OUT=$(c849_fixture_workspace_init 2>>"$C1192_ROOT/scenario.err") || code=$?
    C1192_CODE=$code
}

label_value() {
    local file="$1"
    if [ -f "$file" ]; then printf '%s\n' "$(< "$file")"; else printf '\n'; fi
}

rewrite_paths() {
    local data="$1"
    shift
    REWRITE=()
    local arg
    for arg in "$@"; do
        case "$arg" in
            /fixture) REWRITE+=("$data") ;;
            /fixture/*) REWRITE+=("$data/${arg#/fixture/}") ;;
            /cache) REWRITE+=("$data") ;;
            /cache/*) REWRITE+=("$data/${arg#/cache/}") ;;
            *) REWRITE+=("$arg") ;;
        esac
    done
}

mount_field() {
    local mount="$1" key="$2" bit
    IFS=',' read -r -a bits <<< "$mount"
    for bit in "${bits[@]}"; do
        if [ "${bit%%=*}" = "$key" ]; then printf '%s' "${bit#*=}"; return 0; fi
    done
}

run_script() {
    local mount="$1"
    shift
    local source target data script
    source=$(mount_field "$mount" source)
    target=$(mount_field "$mount" target)
    data="$C1192_ROOT/volumes/$source/data"
    mkdir -p "$data"
    if [ "${1:-}" != -c ]; then
        printf 'UNEXPECTED-COMMAND %s\n' "$*" >> "$C1192_ROOT/scenario.err"
        return 97
    fi
    script="${2//${target}/${data}}"
    # Cache admission reads a uid the model cannot enforce. The lie is only the stat line.
    if [ "$target" = /cache ]; then
        script="stat() { printf '1654:1654:700\n'; }; $script"
    fi
    shift 2
    bash -c "$script" "$@"
}

mount_source_for_target() {
    local want="$1" mount
    local IFS=';'
    local -a mounts=()
    read -r -a mounts <<< "$PARSED_MOUNTS"
    for mount in "${mounts[@]}"; do
        [ -n "$mount" ] || continue
        if [ "$(mount_field "$mount" target)" = "$want" ]; then
            mount_field "$mount" source
            return 0
        fi
    done
    return 1
}

volume_data_for_target() {
    local source
    source=$(mount_source_for_target "$1") || return 1
    printf '%s' "$C1192_ROOT/volumes/$source/data"
}

wait_for_volume_file() {
    local path="$1" i
    for i in $(seq 1 200); do
        [ -f "$path" ] && return 0
        command sleep 0.02
    done
    return 1
}

simulate_zip() {
    local data
    data=$(volume_data_for_target /fixture) || return 97
    mkdir -p "$data/race/feed"
    printf 'nupkg\n' > "$data/race/feed/C849.Probe.1.0.0.nupkg"
}

plant_restored_package() {
    local packages data
    packages=$(mount_source_for_target /home/app/.nuget/packages) || return 1
    data="$C1192_ROOT/volumes/$packages/data"
    mkdir -p "$data/c849.probe/1.0.0/content"
    printf 'meta\n' > "$data/c849.probe/1.0.0/.nupkg.metadata"
    printf 'C849_PACKAGE_SENTINEL\n' > "$data/c849.probe/1.0.0/content/probe.txt"
}

simulate_restore() {
    local n driver project data
    n=${#PARSED_CMD[@]}
    [ "$n" -ge 2 ] || return 97
    driver="${PARSED_CMD[$((n - 2))]}"
    project="${PARSED_CMD[$((n - 1))]}"
    # driver.sh is invoked as sh driver project deadline. The deadline is the
    # last token only when it is an epoch; the project stays the name before it.
    case "$project" in
        ''|*[!0-9]*) ;;
        *)
            [ "$n" -ge 3 ] || return 97
            driver="${PARSED_CMD[$((n - 3))]}"
            project="${PARSED_CMD[$((n - 2))]}"
            ;;
    esac
    data=$(volume_data_for_target /fixture) || return 97
    if [ "$driver" = offline.sh ]; then
        if [ "${C1192_FAULT:-}" = offline-slot-timeout ]; then
            return 4
        fi
        plant_restored_package || return 97
        return 0
    fi
    # Opt-in only. The default path still writes ready before go, which is what the
    # current barrier waits on. C849 slot tests set C1192_SLOT_GATE=1.
    if [ "${C1192_SLOT_GATE:-}" = 1 ]; then
        c1192_slot_gated_restore "$project" "$data"
        return $?
    fi
    mkdir -p "$data/race/$project"
    printf '1\n' > "$data/race/$project/slot-held"
    wait_for_volume_file "$data/race/go" || return 1
    printf '1\n' > "$data/race/$project/ready"
    if [ "${C1192_FAULT:-}" = "restore-$project" ]; then
        return 1
    fi
    plant_restored_package || return 97
    return 0
}

# Slot gate model. slot-held is the grant. ready is written only after go, immediately
# before the restore. A killed container or slot-stop ends the wait so a refusal cannot
# leave this function running after the scenario has recorded its facts.
c1192_slot_stop_requested() {
    [ -e "${C1192_ROOT:?}/slot-stop" ]
}

c1192_slot_killed() {
    [ -n "${PARSED_NAME:-}" ] && [ -e "${C1192_ROOT:?}/killed-$PARSED_NAME" ]
}

c1192_slot_gated_restore() {
    local project="$1" data="$2" spins=0 need n i
    if [ "${C1192_SLOT_EXIT4_FOR:-}" = "$project" ]; then
        return 4
    fi
    if [ "${C1192_SLOT_NEVER:-}" = 1 ]; then
        if [ -n "${C1192_SLOT_LEASE:-}" ] && [ "$project" = p1 ]; then
            printf 'BUILD SLOT granted lease=%s waited=0s maxcpucount=1\n' "$C1192_SLOT_LEASE"
        fi
        while [ ! -e "${C1192_ROOT:?}/killed-${PARSED_NAME:-}" ]; do
            c1192_slot_stop_requested && return 4
            spins=$((spins + 1))
            [ "$spins" -lt 20000 ] || return 4
            command sleep 0.05
        done
        return 4
    fi
    if [ "${C1192_SLOT_DELAY_FOR:-}" = "$project" ]; then
        need=${C1192_SLOT_DELAY_POLLS:-160}
        while true; do
            c1192_slot_killed && return 4
            c1192_slot_stop_requested && return 4
            n=$(grep -c '^0\.2$' "${C1192_ROOT:?}/sleeps" || true)
            [ "${n:-0}" -ge "$need" ] && break
            spins=$((spins + 1))
            [ "$spins" -lt 20000 ] || return 4
            command sleep 0.01
        done
    fi
    mkdir -p "$data/race/$project"
    printf '1\n' > "$data/race/$project/slot-held"
    for i in $(seq 1 1500); do
        [ -f "$data/race/go" ] && break
        c1192_slot_killed && return 4
        c1192_slot_stop_requested && return 1
        command sleep 0.02
    done
    [ -f "$data/race/go" ] || return 1
    printf '1\n' > "$data/race/$project/ready"
    if [ "${C1192_FAULT:-}" = "restore-$project" ]; then
        return 1
    fi
    plant_restored_package || return 97
    return 0
}

c1192_kill_jobs() {
    : > "${C1192_ROOT:?}/slot-stop"
    local pids
    pids=$(jobs -pr 2>/dev/null || true)
    [ -n "$pids" ] || return 0
    # shellcheck disable=SC2086
    kill $pids 2>/dev/null || true
    # shellcheck disable=SC2086
    kill -9 $pids 2>/dev/null || true
}

simulate_lock() {
    local mode="" generation="" i scratch data entered release
    for ((i = 0; i < ${#PARSED_CMD[@]}; i++)); do
        case "${PARSED_CMD[$i]}" in
            -Mode) mode="${PARSED_CMD[$((i + 1))]:-}" ;;
            -Generation) generation="${PARSED_CMD[$((i + 1))]:-}" ;;
        esac
    done
    [ -n "$mode" ] && [ -n "$generation" ] || return 97
    scratch=$(mount_source_for_target /var/cache/antiphon/nuget-scratch) || return 97
    data=$(volume_data_for_target /fixture) || return 97
    mkdir -p "$data/race"
    entered="$data/race/${generation}-${mode}-entered"
    release="$data/race/${generation}-release"
    if [ "$mode" = holder ]; then
        printf '1\n' > "$entered"
        wait_for_volume_file "$release"
        return
    fi
    case "$scratch" in
        *private*)
            printf '1\n' > "$entered"
            return 0
            ;;
    esac
    wait_for_volume_file "$release" || return 1
    if [ "${C1192_FAULT:-}" = waiter-no-entry ]; then
        return 0
    fi
    printf '1\n' > "$entered"
}

exec_in() {
    local name="$1"
    shift
    local mount source data arg script_text=""
    mount=$(cat "$C1192_ROOT/containers/$name/mount")
    source=$(mount_field "$mount" source)
    data="$C1192_ROOT/volumes/$source/data"
    mkdir -p "$data"
    if [ "${C1192_FAULT:-}" = touch-release ]; then
        for arg in "$@"; do
            if [ "$arg" = /fixture/race/shared-release ]; then
                return 1
            fi
        done
    fi
    for arg in "$@"; do
        script_text+="$arg"$'\n'
    done
    if c1192_has_substr 'chown -R 1654:1654' "$script_text" \
        && c1192_has_exact_line '/fixture/race' "$script_text"; then
        local probe="$data/race/lock-probe.ps1"
        if [ -f "$probe" ] && grep -q 'ExecuteWithFileLocked' "$probe" && grep -q '/fixture/race' "$probe"; then
            printf '1\n' > "$C1192_ROOT/probe-at-handoff"
        else
            printf '0\n' > "$C1192_ROOT/probe-at-handoff"
        fi
    fi
    rewrite_paths "$data" "$@"
    local interp="${REWRITE[0]:-}" bin
    case "$interp" in
        /bin/bash|bash) bin=bash ;;
        /bin/sh|sh) bin=sh ;;
        *)
            printf 'UNEXPECTED-EXEC %s\n' "$*" >> "$C1192_ROOT/scenario.err"
            return 97
            ;;
    esac
    if [ "${REWRITE[1]:-}" = -c ]; then
        model_mount_exec "$mount" "$data" "$bin" -c "${REWRITE[2]}" "${REWRITE[@]:3}"
        return
    fi
    if [ "${REWRITE[1]:-}" = -s ]; then
        local rest=("${REWRITE[@]:2}")
        [ "${rest[0]:-}" = -- ] && rest=("${rest[@]:1}")
        model_mount_exec "$mount" "$data" "$bin" -s -- "${rest[@]}"
        return
    fi
    printf 'UNEXPECTED-EXEC %s\n' "$*" >> "$C1192_ROOT/scenario.err"
    return 97
}

docker_volume() {
    local sub="$1"
    shift
    case "$sub" in
        ls)
            [ "$C1192_FAULT" = volume-ls-down ] && return 2
            local dir compose_project='' token
            for token in "$@"; do case "$token" in label=com.docker.compose.project=*) compose_project="${token#label=com.docker.compose.project=}" ;; esac; done
            for dir in "$C1192_ROOT/volumes"/*; do
                [ -z "$compose_project" ] || [[ "${dir##*/}" == "${compose_project}_"* ]] || continue
                [ -d "$dir" ] || continue
                printf '%s\n' "${dir##*/}"
            done
            ;;
        create)
            local driver=local name=""
            local -a labels=()
            while [ $# -gt 0 ]; do
                case "$1" in
                    --driver) driver="$2"; shift 2 ;;
                    --label) labels+=("$2"); shift 2 ;;
                    --opt|-o) shift 2 ;;
                    *) name="$1"; shift ;;
                esac
            done
            [ -n "$name" ] || return 97
            event "$(printf 'volume-create\t%s' "$name")"
            if [ -f "$CASE_DIR/.fixture-volumes" ] && grep -Fxq -- "$name" "$CASE_DIR/.fixture-volumes"; then
                printf '1\n' > "$C1192_ROOT/intent-$name"
            else
                printf '0\n' > "$C1192_ROOT/intent-$name"
            fi
            if [ "$C1192_CUT" = work-create ] && [ "$name" = "$WORK" ]; then return 1; fi
            if [ "$C1192_CUT" = mask-create ] && [ "$name" = "$MASK" ]; then return 1; fi
            # Idempotent create of a volume that appeared after the absence lookup.
            if [ "$C1192_FAULT" = foreign-create ] || [ "$C1192_FAULT" = foreign-missing ]; then
                if [ "$C1192_FAULT" = foreign-create ]; then
                    plant_volume "$name" local null foreign-owner other-run work
                else
                    local foreign_dir="$C1192_ROOT/volumes/$name"
                    mkdir -p "$foreign_dir/data"
                    printf '%s\n' local > "$foreign_dir/driver"
                    printf '%s\n' null > "$foreign_dir/options"
                fi
                return 0
            fi
            local owner=c849-fixture run_id="$RUN" role=work item key val
            for item in "${labels[@]}"; do
                key="${item%%=*}"
                val="${item#*=}"
                case "$key" in
                    io.antiphon.owner) owner="$val" ;;
                    io.antiphon.fixture-run) run_id="$val" ;;
                    io.antiphon.fixture-role) role="$val" ;;
                    io.antiphon.cache-role) role="$val" ;;
                esac
            done
            local options=null
            [ "$C1192_FAULT" = options-lie ] && options='{"device":"foreign"}'
            local stored=$driver
            [ "$C1192_FAULT" = driver-lie ] && stored=nfs
            plant_volume "$name" "$stored" "$options" "$owner" "$run_id" "$role"
            # Cache schema label is planted from the create labels when present.
            local dir="$C1192_ROOT/volumes/$name"
            for item in "${labels[@]}"; do
                key="${item%%=*}"
                val="${item#*=}"
                case "$key" in
                    io.antiphon.owner) printf '%s\n' "$val" > "$dir/label-owner" ;;
                    io.antiphon.cache-schema) printf '%s\n' "$val" > "$dir/label-schema" ;;
                    io.antiphon.cache-role) printf '%s\n' "$val" > "$dir/label-cache-role" ;;
                    io.antiphon.fixture-run) printf '%s\n' "$val" > "$dir/label-run" ;;
                    io.antiphon.fixture-role) printf '%s\n' "$val" > "$dir/label-role" ;;
                esac
            done
            ;;
        inspect)
            [ "$C1192_FAULT" = volume-inspect-down ] && return 2
            local fmt="" name=""
            while [ $# -gt 0 ]; do
                case "$1" in
                    -f|--format) fmt="$2"; shift 2 ;;
                    *) name="$1"; shift ;;
                esac
            done
            local dir="$C1192_ROOT/volumes/$name"
            # Daemon loss is not the missing-volume response. F-7 must not treat it as absence.
            if [ "${RETENTION_INSPECT_FAULT:-}" = temp-private ] && [[ "$name" == "c849${RUN}temp_"* ]]; then
                printf 'Cannot connect to the Docker daemon\n' >&2
                return 1
            fi
            [ -d "$dir" ] || { printf 'Error response from daemon: No such volume: %s\n' "$name" >&2; return 1; }
            if [ "$mode" = acquisition-probe ] && [ "$name" = "$ACQUIRE_NAME" ]; then
                if [ "$ACQUIRE_OUTCOME" = daemon ]; then
                    printf 'Cannot connect to the Docker daemon\n' >&2; return 1
                fi
                if [[ "$fmt" == *io.antiphon.owner* ]] && [ ! -f "$C1192_ROOT/acquire-probed" ]; then
                    : > "$C1192_ROOT/acquire-probed"
                    printf 'Cannot connect to the Docker daemon\n' >&2; return 1
                fi
            fi
            if [ -z "$fmt" ]; then return 0; fi
            case "$fmt" in
                *Driver*) label_value "$dir/driver" ;;
                *Options*) label_value "$dir/options" ;;
                *io.antiphon.owner*) label_value "$dir/label-owner" ;;
                *fixture-run*) label_value "$dir/label-run" ;;
                *fixture-role*) label_value "$dir/label-role" ;;
                *cache-schema*) label_value "$dir/label-schema" ;;
                *cache-role*) label_value "$dir/label-cache-role" ;;
                *) printf 'UNEXPECTED-FMT %s\n' "$fmt" >> "$C1192_ROOT/scenario.err"; return 97 ;;
            esac
            ;;
        rm)
            local name=""
            while [ $# -gt 0 ]; do
                case "$1" in
                    -f|--force) shift ;;
                    *) name="$1"; shift ;;
                esac
            done
            order_log "volume $name"
            if [ "$C1192_CUT" = rm-work ] && [ "$name" = "$WORK" ]; then return 1; fi
            rm -rf -- "$C1192_ROOT/volumes/$name"
            ;;
        *) printf 'UNEXPECTED-VOLUME %s\n' "$sub" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
}

parse_run_create() {
    PARSED_CIDFILE=""
    PARSED_NAME=""
    PARSED_NETWORK=""
    PARSED_USER=""
    PARSED_ENTRY=""
    PARSED_IMAGE=""
    PARSED_MOUNTS=""
    PARSED_CMD=()
    local labels=()
    while [ $# -gt 0 ]; do
        case "$1" in
            --rm|-d|-i|-t|-q) shift ;;
            --cidfile|--name|--network|--user|--entrypoint|-u|-w|--workdir|-e|--env|--env-file|--network-alias)
                local flag="$1" value="$2"
                shift 2
                case "$flag" in
                    --cidfile) PARSED_CIDFILE="$value" ;;
                    --name) PARSED_NAME="$value" ;;
                    --network) PARSED_NETWORK="$value" ;;
                    --user|-u) PARSED_USER="$value" ;;
                    --entrypoint) PARSED_ENTRY="$value" ;;
                esac
                ;;
            --label) labels+=("$2"); shift 2 ;;
            --mount) PARSED_MOUNTS+="$2;"; shift 2 ;;
            *) PARSED_IMAGE="$1"; shift; PARSED_CMD=("$@"); break ;;
        esac
    done
    PARSED_LABELS=$(IFS=';'; printf '%s' "${labels[*]:-}")
}

store_container() {
    local name="$1" image="$2" mounts="$3" labels="$4"
    local dir="$C1192_ROOT/containers/$name"
    mkdir -p "$dir"
    [ -f "$dir/id" ] || printf '%s\n' "$(printf '%s:%s' "$name" "$(date +%s%N)" | sha256sum | cut -d' ' -f1)" > "$dir/id"
    printf '/%s\n' "$name" > "$dir/name"
    printf '2026-10-13T00:00:00Z\n' > "$dir/created"
    printf '%s\n' "$image" > "$dir/image"
    printf '%s\n' "${mounts%%;}" > "$dir/mount"
    : > "$dir/label-owner"
    : > "$dir/label-run"
    : > "$dir/label-role"
    local item key val
    IFS=';' read -r -a items <<< "$labels"
    for item in "${items[@]}"; do
        [ -n "$item" ] || continue
        key="${item%%=*}"
        val="${item#*=}"
        case "$key" in
            io.antiphon.owner) printf '%s\n' "$val" > "$dir/label-owner" ;;
            io.antiphon.fixture-run) printf '%s\n' "$val" > "$dir/label-run" ;;
            io.antiphon.fixture-role) printf '%s\n' "$val" > "$dir/label-role" ;;
        esac
    done
}

docker_create() {
    parse_run_create "$@"
    local role=""
    case "$PARSED_NAME" in
        *-writer) role=writer ;;
        *-reader) role=reader ;;
    esac
    event "$(printf 'helper-create\t%s\t%s\t%s\t%s' "$role" "$PARSED_NETWORK" "$PARSED_IMAGE" "$PARSED_MOUNTS")"
    if [ "$C1192_CUT" = writer-create ] && [ "$role" = writer ]; then return 1; fi
    if [ "$C1192_CUT" = reader-create ] && [ "$role" = reader ]; then return 1; fi
    store_container "$PARSED_NAME" "$PARSED_IMAGE" "$PARSED_MOUNTS" "$PARSED_LABELS"
    [ -z "$PARSED_CIDFILE" ] || cat "$C1192_ROOT/containers/$PARSED_NAME/id" > "$PARSED_CIDFILE"
    if [ "$mode" = apphost ]; then
        printf '%s\t%s\t%s\t%s\t%s\n' "$PARSED_NAME" "$PARSED_NETWORK" "$PARSED_IMAGE" "$PARSED_MOUNTS" "${PARSED_CMD[*]:-}" >> "$C1192_ROOT/run-log"
    fi
    cat "$C1192_ROOT/containers/$PARSED_NAME/id"
}

simulate_npm_cache_add() {
    local joined="${PARSED_CMD[*]:-}" data cache_data
    if [[ "$joined" == *'/fixture/npm-work/package.tgz'* ]]; then
        data=$(volume_data_for_target /fixture) || return 97
        [ -f "$data/npm-work/package.tgz" ] || return 1
        mkdir -p "$data/npm-work/cache/_cacache"
        printf 'entry\n' > "$data/npm-work/cache/_cacache/entry"
        return 0
    fi
    if [[ "$joined" == *'/fixture/npm-offline/pkg.tgz'* ]]; then
        data=$(volume_data_for_target /fixture) || return 97
        [ -f "$data/npm-offline/pkg.tgz" ] || return 1
        cache_data=$(volume_data_for_target /home/app/.npm/_cacache) || return 97
        mkdir -p "$cache_data"
        printf 'entry\n' > "$cache_data/entry"
        return 0
    fi
    if [[ "$joined" == *'http://c849-npm-http:8080/pkg.tgz'* ]]; then
        cache_data=$(volume_data_for_target /home/app/.npm/_cacache) || return 97
        mkdir -p "$cache_data"
        printf 'entry\n' > "$cache_data/entry"
        return 0
    fi
    printf 'UNEXPECTED-NPM %s\n' "$joined" >> "$C1192_ROOT/scenario.err"
    return 97
}

simulate_npm_ci() {
    local project data cache_source
    project="${PARSED_CMD[-1]:-}"
    [ -n "$project" ] || return 97
    data=$(volume_data_for_target /fixture) || return 97
    cache_source=$(mount_source_for_target /home/app/.npm/_cacache) || return 97
    case "${C1192_FAULT:-}" in
        npm-enotcached)
            printf 'ENOTCACHED\n'
            return 1
            ;;
        npm-other)
            printf 'other-failure\n'
            return 1
            ;;
    esac
    case "$cache_source" in
        *npmempty*)
            printf 'ENOTCACHED\n'
            return 1
            ;;
    esac
    case "${C1192_FAULT:-}" in
        npm-missing) return 0 ;;
        npm-wrong)
            mkdir -p "$data/npm-offline/$project/node_modules/c849-cache-probe"
            printf 'WRONG\n' > "$data/npm-offline/$project/node_modules/c849-cache-probe/sentinel"
            return 0
            ;;
    esac
    mkdir -p "$data/npm-offline/$project/node_modules/c849-cache-probe"
    printf 'C849_NPM_OFFLINE\n' > "$data/npm-offline/$project/node_modules/c849-cache-probe/sentinel"
    return 0
}

docker_run() {
    parse_run_create "$@"
    local acquired_name="$PARSED_NAME" auto=0 arg code=0
    for arg in "$@"; do [ "$arg" != --rm ] || auto=1; done
    docker_run_payload "$@" || code=$?
    if [ "$auto" = 1 ] && [ -n "$acquired_name" ]; then
        rm -rf -- "$C1192_ROOT/containers/$acquired_name"
    fi
    return "$code"
}

docker_run_payload() {
    parse_run_create "$@"
    if [ -n "$PARSED_NAME" ]; then
        store_container "$PARSED_NAME" "$PARSED_IMAGE" "$PARSED_MOUNTS" "$PARSED_LABELS"
        if [ -n "$PARSED_CIDFILE" ]; then
            case "$PARSED_NAME" in
                *"-restore-${C1192_LATE_CID_FOR:-}")
                    : > "$PARSED_CIDFILE"
                    if [ -n "${C1192_SLOT_LEASE:-}" ]; then
                        printf 'BUILD SLOT granted lease=%s waited=0s maxcpucount=1\n' "$C1192_SLOT_LEASE"
                    fi
                    command sleep "${C1192_LATE_CID_SECONDS:-2}"
                    cat "$C1192_ROOT/containers/$PARSED_NAME/id" > "$PARSED_CIDFILE"
                    ;;
                *) cat "$C1192_ROOT/containers/$PARSED_NAME/id" > "$PARSED_CIDFILE" ;;
            esac
        fi
    fi
    if [ "$mode" = apphost ]; then apphost_run; return; fi
    local kind=other-run
    case "$PARSED_MOUNTS" in
        *target=/fixture*) kind=init-run ;;
        *target=/cache*) kind=cache-run ;;
    esac
    event "$(printf '%s\t-\t%s\t%s\t%s' "$kind" "$PARSED_NETWORK" "$PARSED_IMAGE" "$PARSED_MOUNTS")"
    if [ "$C1192_CUT" = root-init ] && [ "$kind" = init-run ]; then return 1; fi
    local joined="${PARSED_CMD[*]:-}"
    # Parallel restore/lock containers append one line each. flock keeps those lines intact.
    {
        flock -x 9
        printf '%s\t%s\t%s\n' "${PARSED_NAME:-noname}" "$PARSED_MOUNTS" "$joined" >> "$C1192_ROOT/race-mounts"
    } 9>>"$C1192_ROOT/race-mounts.lock"
    printf '%s\t%s\t%s\t%s\t%s\n' \
        "${PARSED_NAME:-noname}" "${PARSED_NETWORK:-}" "${PARSED_IMAGE:-}" \
        "${PARSED_MOUNTS:-}" "$joined" >> "$C1192_ROOT/run-log"
    case "$joined" in
        *make-zip.ps1*) simulate_zip; return ;;
        *lock-probe.ps1*) simulate_lock; return ;;
        *c849-fixture-restore*) simulate_restore; return ;;
        *hash-tarball.ps1*) printf 'FIXTUREHASH'; return 0 ;;
        *server.js*) return 0 ;;
        *'cache add'*)
            simulate_npm_cache_add || return $?
            return 0
            ;;
        *'npm ci --offline'*)
            simulate_npm_ci || return $?
            return 0
            ;;
        *c849-npm-http:8080*) return 0 ;;
    esac
    if [ "$PARSED_ENTRY" = sleep ]; then
        printf 'UNEXPECTED-SLEEP\n' >> "$C1192_ROOT/scenario.err"
        return 97
    fi
    local mount
    IFS=';' read -r -a mounts <<< "$PARSED_MOUNTS"
    mount="${mounts[0]:-}"
    # A curl wait has no mount. run_script would rewrite an empty target globally.
    if [ -z "$mount" ]; then
        printf 'UNEXPECTED-COMMAND %s\n' "$joined" >> "$C1192_ROOT/scenario.err"
        return 97
    fi
    run_script "$mount" "${PARSED_CMD[@]}"
}

# Single-line model fields use live Bash reads; do not fork cat for each ID
# candidate and field. No identity, fault scenario or observation is cached.
docker_inspect_container() {
    local fmt="" name=""
    while [ $# -gt 0 ]; do
        case "$1" in
            -f|--format) fmt="$2"; shift 2 ;;
            *) name="$1"; shift ;;
        esac
    done
    local requested="$name" candidate
    if [[ "$name" =~ ^[0-9a-f]{64}$ ]]; then
        for candidate in "$C1192_ROOT/containers"/*; do
            [ -f "$candidate/id" ] && [ "$(< "$candidate/id")" = "$name" ] && name="${candidate##*/}" && break
        done
    fi
    local dir="$C1192_ROOT/containers/$name"
    [ -d "$dir" ] || { printf 'Error: No such container: %s\n' "$name" >&2; return 1; }
    [ -n "$fmt" ] || return 0
    case "$fmt" in
        '{{.Id}}') printf '%s\n' "$(< "$dir/id")" ;;
        '{{.Name}}') printf '%s\n' "$(< "$dir/name")" ;;
        '{{.Image}}') printf '%s\n' "$(< "$dir/image")" ;;
        '{{.Created}}') printf '%s\n' "$(< "$dir/created")" ;;
        *compose.project*) label_value "$dir/label-project" ;;
        *io.antiphon.owner*) label_value "$dir/label-owner" ;;
        *fixture-run*) label_value "$dir/label-run" ;;
        *fixture-role*) label_value "$dir/label-role" ;;
        *) printf 'UNEXPECTED-INSPECT %s\n' "$fmt" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
}

# docker cp container:/fixture/dir/. - streams a tar of that directory.
# A trailing * is the glob mutation and omits dotfiles. source-copy fails before any bytes.
payload_cp_stream() {
    local spec="$1" name path mount source data rel archive
    name="${spec%%:*}"
    path="${spec#*:}"
    [ -d "$C1192_ROOT/containers/$name" ] || return 1
    if [ "${C1192_FAULT:-}" = source-copy ]; then
        return 1
    fi
    mount=$(cat "$C1192_ROOT/containers/$name/mount")
    source=$(mount_field "$mount" source)
    data="$C1192_ROOT/volumes/$source/data"
    case "$path" in
        /fixture/*) ;;
        *) printf 'UNEXPECTED-CP %s\n' "$path" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
    if [ "${path##*/}" = "." ]; then
        rel="${path#/fixture/}"
        rel="${rel%/.}"
        archive="$data/$rel"
        [ -d "$archive" ] || return 1
        tar -C "$archive" -cf - . || return $?
        return 0
    fi
    if [ "${path##*/}" = "*" ]; then
        rel="${path#/fixture/}"
        rel="${rel%/*}"
        archive="$data/$rel"
        [ -d "$archive" ] || return 1
        ( cd -- "$archive" && tar -cf - * ) || return $?
        return 0
    fi
    printf 'UNEXPECTED-CP %s\n' "$path" >> "$C1192_ROOT/scenario.err"
    return 97
}

# tar -x -p keeps modes. Without -p the model clears execute bits.
# An empty archive is success so a failed docker cp is visible only under pipefail.
# sink-copy consumes the archive and then returns 1.
payload_tar_exec() {
    local name="$1" preserve=0 dest="" tmp st=0 mount source data rel
    shift
    while [ $# -gt 0 ]; do
        case "$1" in
            -x|-f) shift ;;
            -p|--preserve-permissions) preserve=1; shift ;;
            -C) dest="$2"; shift 2 ;;
            *) shift ;;
        esac
    done
    tmp=$(mktemp "$C1192_ROOT/tar.XXXXXX") || return 1
    cat > "$tmp" || { rm -f -- "$tmp"; return 1; }
    if [ "${C1192_FAULT:-}" = sink-copy ]; then
        rm -f -- "$tmp"
        return 1
    fi
    if [ ! -s "$tmp" ]; then
        rm -f -- "$tmp"
        return 0
    fi
    mount=$(cat "$C1192_ROOT/containers/$name/mount")
    source=$(mount_field "$mount" source)
    data="$C1192_ROOT/volumes/$source/data"
    case "$dest" in
        /fixture) rel="" ;;
        /fixture/*) rel="${dest#/fixture/}" ;;
        *) rm -f -- "$tmp"; printf 'UNEXPECTED-TAR %s\n' "$dest" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
    mkdir -p -- "$data/$rel"
    if [ "$preserve" = 1 ]; then
        tar -x -p -C "$data/$rel" -f "$tmp" || st=$?
    else
        tar -x -C "$data/$rel" -f "$tmp" || st=$?
        find "$data/$rel" -type f -exec chmod a-x {} + || st=$?
    fi
    rm -f -- "$tmp"
    return "$st"
}

# One invocation: argument count, then each argument, NUL-terminated.
record_argv() {
    local arg
    {
        printf '%s\0' "$#"
        for arg in "$@"; do printf '%s\0' "$arg"; done
    } >> "${C1192_ROOT:?}/argv"
}

docker() {
    record_argv "$@"
    if [ "$mode" = cleanup-probe ] && [ -f "$C1192_ROOT/teardown-done" ]; then
        if [ "$PROBE_BOUNDARY" = all ]; then
            printf 'Cannot connect to the Docker daemon at unix:///missing.sock. Is the docker daemon running?\n' >&2
            return 1
        fi
        local probe_target="${*: -1}" candidate
        if [[ "$probe_target" =~ ^[0-9a-f]{64}$ ]]; then
            for candidate in "$C1192_ROOT/containers"/*; do
                [ -f "$candidate/id" ] && [ "$(< "$candidate/id")" = "$probe_target" ] && probe_target="${candidate##*/}" && break
            done
            [ "$probe_target" != "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" ] || probe_target="c849-${RUN}-npm"
        fi
        if [ "$probe_target" = "$PROBE_NAME" ]; then
            case "$1:$2" in
                volume:rm|network:rm|rm:-f)
                    order_log "probe-remove $PROBE_NAME"
                    [ "$PROBE_OUTCOME" = rm-lie ] && return 0
                    printf 'removal did not complete\n' >&2; return 1 ;;
                volume:inspect|container:inspect|network:inspect|inspect:-f)
                    order_log "probe-inspect $PROBE_NAME"
                    case "$PROBE_OUTCOME" in
                        absent) printf 'Error response from daemon: No such %s: %s\n' "$PROBE_KIND" "$PROBE_NAME" >&2; return 1 ;;
                        daemon) printf 'Cannot connect to the Docker daemon\n' >&2; return 1 ;;
                        exit2) printf 'probe error\n' >&2; return 2 ;;
                        exit2-absent) printf 'Error: No such %s: %s\n' "$PROBE_KIND" "$PROBE_NAME" >&2; return 2 ;;
                        other-name) printf 'Error: No such %s: foreign-resource\n' "$PROBE_KIND" >&2; return 1 ;;
                        other-kind) printf 'Error: No such image: %s\n' "$PROBE_NAME" >&2; return 1 ;;
                        mixed) printf 'Error: No such %s: %s\nCannot connect to the Docker daemon\n' "$PROBE_KIND" "$PROBE_NAME" >&2; return 1 ;;
                        timeout) printf 'probe timed out\n' >&2; return 124 ;;
                        empty) return 1 ;;
                        permission) printf 'permission denied\n' >&2; return 1 ;;
                        socket) printf 'dial unix /missing.sock: no such file or directory\n' >&2; return 1 ;;
                        unrecognized) printf 'unrecognized failure\n' >&2; return 1 ;;
                        present|rm-lie) ;;
                        *) fail "unknown probe outcome $PROBE_OUTCOME" ;;
                    esac
                    ;;
            esac
        fi
    fi
    if [ "$mode" = compose-absence ]; then
        if c1207_docker "$@"; then return 0; else local handled=$?; [ "$handled" = 98 ] || return "$handled"; fi
    fi
    local arg
    {
        printf 'DOCKER'
        for arg in "$@"; do printf ' %q' "$arg"; done
        printf '\n'
    } >> "$C1192_ROOT/trace"
    case "$1" in
        volume) shift; docker_volume "$@" ;;
        container)
            shift
            case "$1" in
                inspect) shift; docker_inspect_container "$@" ;;
                *) printf 'UNEXPECTED-CONTAINER %s\n' "$1" >> "$C1192_ROOT/scenario.err"; return 97 ;;
            esac
            ;;
        image)
            shift
            local fmt="" name=""
            while [ $# -gt 0 ]; do
                case "$1" in
                    -f|--format) fmt="$2"; shift 2 ;;
                    *) name="$1"; shift ;;
                esac
            done
            [ "$name" = "$ADMITTED" ] || [ "$name" = "$IMAGE_ID" ] || return 1
            if [ "$fmt" = '{{.Id}}' ]; then printf '%s\n' "$IMAGE_ID"; fi
            ;;
        create) shift; docker_create "$@" ;;
        start)
            shift
            local name="$1"
            order_log "start $name"
            if [ "$C1192_CUT" = writer-start ] && [ "$name" = "$WRITER" ]; then return 1; fi
            if [ "$C1192_CUT" = reader-start ] && [ "$name" = "$READER" ]; then return 1; fi
            if [ "$C1192_CUT" = masked-start ] && [ "$name" = "c849-${RUN}-masked" ]; then return 1; fi
            [ -d "$C1192_ROOT/containers/$name" ] || return 1
            ;;
        rm)
            shift
            local name=""
            while [ $# -gt 0 ]; do
                case "$1" in
                    -f|--force) shift ;;
                    *) name="$1"; shift ;;
                esac
            done
            order_log "container $name"
            local requested="$name" candidate
            if [[ "$name" =~ ^[0-9a-f]{64}$ ]]; then
                for candidate in "$C1192_ROOT/containers"/*; do
                    [ -f "$candidate/id" ] && [ "$(< "$candidate/id")" = "$name" ] && name="${candidate##*/}" && break
                done
            fi
            [ "$name" = "$requested" ] || order_log "container $name"
            rm -rf -- "$C1192_ROOT/containers/$name"
            ;;
        exec)
            shift
            local name=""
            while [ $# -gt 0 ]; do
                case "$1" in
                    -i|-t|-d) shift ;;
                    -u|--user) shift 2 ;;
                    *) name="$1"; shift; break ;;
                esac
            done
            local joined="" part
            for part in "$@"; do joined+="$part"$'\x1e'; done
            event "$(printf 'exec\t%s' "$joined")"
            [ -d "$C1192_ROOT/containers/$name" ] || return 1
            if [ "${1:-}" = tar ]; then
                payload_tar_exec "$name" "$@"
                return
            fi
            if [ "$name" = "c849-${RUN}-donor" ]; then
                case "$*" in
                    *tar*-xf*)
                        mkdir -p "$C1192_ROOT/donor-npm"
                        tar -C "$C1192_ROOT/donor-npm" -xf - || return 1
                        return 0
                        ;;
                esac
                printf 'UNEXPECTED-DONOR %s\n' "$*" >> "$C1192_ROOT/scenario.err"
                return 97
            fi
            exec_in "$name" "$@"
            ;;
        inspect) shift; docker_inspect_container "$@" ;;
        run) shift; docker_run "$@" ;;
        ps) return 0 ;;
        network)
            [ "${2:-}" != ls ] || return 0
            if [ "$mode" = cleanup-probe ] || [ "$mode" = deletion-audit ]; then
                case "$2" in
                    inspect)
                        [ -f "$C1192_ROOT/network" ] || { printf 'Error response from daemon: No such network: %s\n' "${*: -1}" >&2; return 1; }
                        if [ "${3:-}" = -f ]; then
                            case "$4" in
                                *io.antiphon.owner*) printf 'c849-fixture\n' ;;
                                *fixture-run*) printf '%s\n' "$RUN" ;;
                                *fixture-role*) printf 'npm-network\n' ;;
                                *Id*) printf 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n' ;;
                                *Name*) printf 'c849-%s-npm\n' "$RUN" ;;
                            esac
                        fi ;;
                    rm) order_log "network ${*: -1}"; rm -f "$C1192_ROOT/network" ;;
                    *) fail "unexpected network command $*" ;;
                esac
            fi
            # c849_fixture_npm keeps this id and refuses any other create stdout (CARD-1208).
            # CARD-1333: a non-zero create prints no id, so the production || reports FixtureNpmNetworkFailed.
            if [ "${2:-}" = create ]; then
                if [ "${C1192_FAULT:-}" = npm-network-create ]; then
                    return 1
                fi
                printf '%s\n' "$(printf '%s' "${*: -1}" | sha256sum | cut -d' ' -f1)"
            fi
            return 0 ;;
        compose)
            if [ "$mode" = retention ] || [ "$mode" = retention-inspect ] || [ "$mode" = compose-absence ]; then shift; retention_compose "$@";
            elif [ "$mode" = finalization ] || [ "$mode" = cleanup-probe ]; then
                local previous="" token
                for token in "$@"; do
                    if [ "$previous" = -f ]; then [ -f "$token" ] || return 1; order_log compose-input-present; fi
                    previous=$token
                done
                if [ "$mode" = cleanup-probe ]; then
                    order_log compose-down
                    if [[ "$*" == *"c849${RUN}negative"* ]]; then : > "$C1192_ROOT/teardown-done"; fi
                fi
            else return 1; fi
            ;;
        cp)
            shift
            local spec="$1" dash="${2:-}"
            [ "$dash" = - ] || { printf 'UNEXPECTED-CP %s\n' "$*" >> "$C1192_ROOT/scenario.err"; return 97; }
            payload_cp_stream "$spec"
            ;;
        # Quiet when the name is already gone. A missing container is not an unexpected verb.
        # "kill" is not a container-removal line, so deletion-count audits stay stable.
        kill)
            shift
            local name=""
            while [ $# -gt 0 ]; do
                case "$1" in
                    --) shift; name="${1:-}"; [ $# -gt 0 ] && shift; break ;;
                    -*) shift ;;
                    *) name="$1"; shift ;;
                esac
            done
            local requested="$name" candidate
            if [[ "$name" =~ ^[0-9a-f]{64}$ ]]; then
                for candidate in "$C1192_ROOT/containers"/*; do
                    [ -f "$candidate/id" ] && [ "$(< "$candidate/id")" = "$name" ] && name="${candidate##*/}" && break
                done
            fi
            if [ -z "$name" ] || [ ! -d "$C1192_ROOT/containers/$name" ]; then
                return 1
            fi
            : > "$C1192_ROOT/killed-$name"
            order_log "kill $requested"
            return 0
            ;;
        *) printf 'UNEXPECTED-DOCKER %s\n' "$1" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
}

field_has() {
    case "$1" in
        *"$2"*) return 0 ;;
        *) return 1 ;;
    esac
}

# 0 when a create/run argument is --env-file or contains a credential path.
argv_forbidden() {
    local count arg i
    [ -s "${C1192_ROOT:?}/argv" ] || return 1
    while IFS= read -r -d '' count; do
        local -a args=()
        for ((i=0; i<count; i++)); do
            IFS= read -r -d '' arg || return 0
            args+=("$arg")
        done
        [ "${args[0]:-}" = create ] || [ "${args[0]:-}" = run ] || continue
        for ((i=0; i<${#args[@]}; i++)); do
            case "${args[$i]}" in
                --env-file|*credential*) return 0 ;;
            esac
        done
    done < "${C1192_ROOT:?}/argv"
    return 1
}

scan_argv_credentials() {
    local count arg i name mount value
    local saw_writer=0 saw_reader=0 saw_init=0
    credential_ok=0
    [ -s "${C1192_ROOT:?}/argv" ] || return 0
    while IFS= read -r -d '' count; do
        local -a args=()
        for ((i=0; i<count; i++)); do
            IFS= read -r -d '' arg || return 0
            args+=("$arg")
        done
        [ "${args[0]:-}" = create ] || [ "${args[0]:-}" = run ] || continue
        name=""
        mount=""
        for ((i=0; i<${#args[@]}; i++)); do
            if [ "${args[$i]}" = --name ]; then name="${args[$((i+1))]:-}"; fi
            if [ "${args[$i]}" = --mount ]; then
                value="${args[$((i+1))]:-}"
                case "$value" in
                    *target=/fixture*) mount=$value ;;
                esac
            fi
        done
        case "$name" in
            *-writer) saw_writer=1 ;;
            *-reader) saw_reader=1 ;;
        esac
        if [ "${args[0]}" = run ] && [ -n "$mount" ]; then saw_init=1; fi
    done < "${C1192_ROOT:?}/argv"
    if [ "$saw_writer" = 1 ] && [ "$saw_reader" = 1 ] && [ "$saw_init" = 1 ]; then
        argv_forbidden || credential_ok=1
    fi
}

scan_happy() {
    network_ok=1
    socket_ok=1
    credential_ok=0
    bind_ok=1
    readonly_ok=0
    nocopy_ok=1
    image_ok=1
    positional_ok=0
    local saw_writer=0 saw_reader=0 saw_init=0
    local line kind role net image mounts args part
    while IFS= read -r line || [ -n "$line" ]; do
        kind="${line%%$'\t'*}"
        case "$kind" in
            helper-create|init-run)
                IFS=$'\t' read -r kind role net image mounts <<< "$line"
                if [ "$kind" = helper-create ]; then
                    [ "$role" = writer ] && saw_writer=1
                    [ "$role" = reader ] && saw_reader=1
                    [ "$net" = none ] || network_ok=0
                    field_has "$mounts" readonly && [ "$role" = reader ] && readonly_ok=1
                    field_has "$mounts" volume-nocopy || nocopy_ok=0
                else
                    saw_init=1
                    field_has "$mounts" volume-nocopy || nocopy_ok=0
                fi
                [ "$image" = "$ADMITTED" ] || image_ok=0
                if field_has "$line" docker.sock || field_has "$line" '/var/run/docker'; then socket_ok=0; fi
                if field_has "$mounts" type=bind; then bind_ok=0; fi
                ;;
            exec)
                args="${line#exec$'\t'}"
                local has_script=0 has_path=0 joined=0
                IFS=$'\x1e' read -r -a parts <<< "$args"
                for part in "${parts[@]}"; do
                    [ "$part" = 'cat > "$1"' ] && has_script=1
                    [ "$part" = /fixture/probes/toxic-input.txt ] && has_path=1
                    case "$part" in
                        *toxic-input.txt*'cat '*|*'cat '*toxic-input.txt*) joined=1 ;;
                    esac
                done
                if [ "$has_script" = 1 ] && [ "$has_path" = 1 ] && [ "$joined" = 0 ]; then positional_ok=1; fi
                ;;
        esac
    done < "$C1192_ROOT/events"
    [ "$saw_writer" = 1 ] && [ "$saw_reader" = 1 ] || network_ok=0
    [ "$saw_init" = 1 ] || nocopy_ok=0
    [ "$saw_writer" = 1 ] && [ "$saw_reader" = 1 ] && [ "$saw_init" = 1 ] || image_ok=0
    scan_argv_credentials
}

emit() { printf '%s=%s\n' "$1" "$2"; }

mode_register() {
    local intent=0 ledger=0 driver=0 options=0 occupied=0 unknown=0 fresh=0
    local controller_mode=0 controller_owner=0 workspace_mode=0 workspace_owner=0
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    run_init
    local happy_diag
    happy_diag=$(diagnosis_of "$C1192_OUT")
    if [ "$C1192_CODE" = 0 ]; then
        ( c849_fixture_evidence_payloads ) || true
    fi
    scan_happy
    if [ -f "$C1192_ROOT/intent-$WORK" ] && [ "$(cat "$C1192_ROOT/intent-$WORK")" = 1 ]; then intent=1; fi
    local data="$C1192_ROOT/volumes/$WORK/data"
    if [ -d "$data" ] && [ "$(stat -c %a "$data")" = 700 ] && [ -d "$data/probes" ] && [ "$(stat -c %a "$data/probes")" = 700 ]; then
        workspace_mode=1
    fi
    if [ -f "$C1192_ROOT/chown.log" ] && grep -q '1654:1654' "$C1192_ROOT/chown.log" && ! grep -q '0:0' "$C1192_ROOT/chown.log"; then
        workspace_owner=1
    fi
    if [ -d "$CASE_DIR/fixture-controller" ] && [ ! -L "$CASE_DIR/fixture-controller" ] && [ "$(stat -c %a "$CASE_DIR/fixture-controller")" = 700 ]; then
        controller_mode=1
    fi
    ( c849_fixture_prepare >/dev/null ) || true
    if [ -f "$CASE_DIR/.fixture-volumes" ] \
        && grep -Fxq -- "$WORK" "$CASE_DIR/.fixture-volumes" \
        && grep -Fxq -- "$MASK" "$CASE_DIR/.fixture-volumes"; then
        ledger=1
    fi

    reset_runtime
    plant_volume "$WORK" local null c849-fixture "$RUN" work
    run_init
    local occ_diag creates
    occ_diag=$(diagnosis_of "$C1192_OUT")
    creates=$(create_count)
    [ "$occ_diag" = FixtureNamespaceOccupied ] && [ "${creates:-0}" = 0 ] && occupied=1

    reset_runtime
    C1192_FAULT=volume-inspect-down
    run_init
    local unk_diag unk_creates
    unk_diag=$(diagnosis_of "$C1192_OUT")
    unk_creates=$(create_count)
    local unk_inspect=0 unk_ls=0
    [ "$unk_diag" = FixtureNamespaceUnknown ] && [ "${unk_creates:-0}" = 0 ] && unk_inspect=1
    reset_runtime
    C1192_FAULT=volume-ls-down
    run_init
    unk_diag=$(diagnosis_of "$C1192_OUT")
    unk_creates=$(create_count)
    [ "$unk_diag" = FixtureNamespaceUnknown ] && [ "${unk_creates:-0}" = 0 ] && unk_ls=1
    [ "$unk_inspect" = 1 ] && [ "$unk_ls" = 1 ] && unknown=1

    reset_runtime
    C1192_FAULT=driver-lie
    run_init
    local driver_diag driver_creates
    driver_diag=$(diagnosis_of "$C1192_OUT")
    driver_creates=$(create_count)
    [ "$driver_diag" = FixtureWorkspaceDriver ] && [ "${driver_creates:-0}" -ge 1 ] && ! volume_exists "$MASK" && driver=1

    reset_runtime
    C1192_FAULT=options-lie
    run_init
    local options_diag options_creates
    options_diag=$(diagnosis_of "$C1192_OUT")
    options_creates=$(create_count)
    [ "$options_diag" = FixtureWorkspaceOptions ] && [ "${options_creates:-0}" -ge 1 ] && ! volume_exists "$MASK" && options=1

    reset_runtime
    mkdir -m 0700 "$CASE_DIR/fixture-controller"
    run_init
    local fresh_diag fresh_creates fresh_dir=0 fresh_link=0
    fresh_diag=$(diagnosis_of "$C1192_OUT")
    fresh_creates=$(create_count)
    [ "$fresh_diag" = FixtureControllerOccupied ] && [ "${fresh_creates:-0}" = 0 ] && fresh_dir=1
    reset_runtime
    ln -s /no/such/c1192-controller "$CASE_DIR/fixture-controller"
    run_init
    fresh_diag=$(diagnosis_of "$C1192_OUT")
    fresh_creates=$(create_count)
    [ "$fresh_diag" = FixtureControllerOccupied ] && [ "${fresh_creates:-0}" = 0 ] && fresh_link=1
    [ "$fresh_dir" = 1 ] && [ "$fresh_link" = 1 ] && fresh=1

    reset_runtime
    C1192_OUT=$(
        stat() {
            if [ "$1" = -c ] && [ "$2" = %u ]; then printf '99999\n'; return 0; fi
            command stat "$@"
        }
        c849_fixture_workspace_init
    ) || true
    local owner_diag owner_creates
    owner_diag=$(diagnosis_of "$C1192_OUT")
    owner_creates=$(create_count)
    [ "$owner_diag" = FixtureControllerOwner ] && [ "${owner_creates:-0}" = 0 ] && controller_owner=1

    emit occupied-name-no-create "$occupied"
    emit unknown-lookup-no-create "$unknown"
    emit intent-before-create "$intent"
    emit workspace-ledger-retained "$ledger"
    emit workspace-local-driver "$driver"
    emit workspace-empty-options "$options"
    emit workspace-network-none "$network_ok"
    emit workspace-no-socket "$socket_ok"
    emit workspace-no-credentials "$credential_ok"
    emit workspace-no-host-work-bind "$bind_ok"
    emit workspace-reader-readonly "$readonly_ok"
    emit workspace-nocopy "$nocopy_ok"
    emit workspace-mode-0700 "$workspace_mode"
    emit workspace-owner-1654 "$workspace_owner"
    emit workspace-positional-arguments "$positional_ok"
    emit controller-fresh-nonlink "$fresh"
    emit controller-mode-0700 "$controller_mode"
    emit controller-host-owner "$controller_owner"
    emit workspace-admitted-image "$image_ok"
    emit happy-diagnosis "$happy_diag"
}

line_no() {
    grep -n -F -m 1 -- "$1" "$C1192_ROOT/order" | head -n 1 | cut -d: -f1 || true
}

mode_cleanup() {
    local exact_junk=0 exact_foreign=0 exact_production=0 consumers=0 helpers=0
    local owner_kept=0 run_kept=0 role_kept=0 unknown_kept=0 twice=0
    local junk="c849${RUN}junk" foreign=c849ffff-work production=antiphon-runner-cache-nuget-packages
    local packages="c849${RUN}-packages"
    reset_runtime
    plant_volume "$WORK" local null c849-fixture "$RUN" work
    plant_volume "$MASK" local null c849-fixture "$RUN" mask
    plant_volume "$packages" local null server2-runner "$RUN" nuget-packages
    printf '1\n' > "$C1192_ROOT/volumes/$packages/label-schema"
    printf 'nuget-packages\n' > "$C1192_ROOT/volumes/$packages/label-cache-role"
    plant_volume "$junk" local null c849-fixture "$RUN" work
    plant_volume "$foreign" local null c849-fixture "$RUN" work
    plant_volume "$production" local null server2-runner production nuget-packages
    plant_container "$WRITER" c849-fixture "$RUN" writer "$ADMITTED" "type=volume,source=$WORK,target=/fixture,volume-nocopy"
    plant_container "$READER" c849-fixture "$RUN" reader "$ADMITTED" "type=volume,source=$WORK,target=/fixture,volume-nocopy,readonly"
    plant_container "c849-${RUN}-restore-p1" c849-fixture "$RUN" consumer "$ADMITTED" ""
    printf '%s\n' "$WORK" "$MASK" "$packages" "$junk" "$foreign" "$production" > "$CASE_DIR/.fixture-volumes"
    printf '%s\n' "$(cat "$C1192_ROOT/containers/c849-${RUN}-restore-p1/id")" > "$CASE_DIR/.fixture-id-c849-${RUN}-restore-p1"
    printf '%s\n' "$WRITER" "$READER" > "$CASE_DIR/.fixture-helpers"
    local code=0
    ( c849_fixture_cleanup ) || code=$?
    volume_exists "$junk" && exact_junk=1
    volume_exists "$foreign" && exact_foreign=1
    volume_exists "$production" && exact_production=1
    ! volume_exists "$WORK" && ! volume_exists "$MASK" && ! volume_exists "$packages" && ! container_exists "$WRITER" && ! container_exists "$READER" && ! container_exists "c849-${RUN}-restore-p1" || exact_junk=0
    local consumer_line helper_line volume_line
    consumer_line=$(line_no "container c849-${RUN}-restore-p1")
    helper_line=$(line_no "container $WRITER")
    volume_line=$(grep -n '^volume ' "$C1192_ROOT/order" | head -n 1 | cut -d: -f1 || true)
    if [ -n "$consumer_line" ] && [ -n "$volume_line" ] && [ "$consumer_line" -lt "$volume_line" ]; then consumers=1; fi
    if [ -n "$helper_line" ] && [ -n "$volume_line" ] && [ "$helper_line" -lt "$volume_line" ] && ! container_exists "$WRITER"; then helpers=1; fi
    [ "$code" = 0 ] || consumers=0

    reset_runtime
    plant_volume "$WORK" local null foreign-owner "$RUN" work
    printf '%s\n' "$WORK" > "$CASE_DIR/.fixture-volumes"
    ( c849_fixture_cleanup ) || true
    volume_exists "$WORK" && owner_kept=1

    reset_runtime
    plant_volume "$WORK" local null c849-fixture someone-else work
    printf '%s\n' "$WORK" > "$CASE_DIR/.fixture-volumes"
    ( c849_fixture_cleanup ) || true
    volume_exists "$WORK" && run_kept=1

    reset_runtime
    plant_volume "$WORK" local null c849-fixture "$RUN" mask
    printf '%s\n' "$WORK" > "$CASE_DIR/.fixture-volumes"
    ( c849_fixture_cleanup ) || true
    volume_exists "$WORK" && role_kept=1

    reset_runtime
    plant_volume "$WORK" local null c849-fixture "$RUN" work
    printf '%s\n' "$WORK" > "$CASE_DIR/.fixture-volumes"
    C1192_FAULT=volume-inspect-down
    ( c849_fixture_cleanup ) || true
    if volume_exists "$WORK" && [ -f "$CASE_DIR/fixture-cleanup-residue.txt" ] && grep -Fxq -- "$WORK" "$CASE_DIR/fixture-cleanup-residue.txt"; then
        unknown_kept=1
    fi

    reset_runtime
    printf '%s\n' "$WORK" > "$CASE_DIR/.fixture-volumes"
    local first=0 second=0
    ( c849_fixture_cleanup ) || first=$?
    ( c849_fixture_cleanup ) || second=$?
    [ "$first" = 0 ] && [ "$second" = 0 ] && twice=1

    emit unregistered-name-preserved "$exact_junk"
    emit foreign-run-preserved "$exact_foreign"
    emit foreign-owner-preserved "$owner_kept"
    emit foreign-label-run-preserved "$run_kept"
    emit wrong-role-preserved "$role_kept"
    emit unknown-resource-preserved "$unknown_kept"
    emit production-name-preserved "$exact_production"
    emit consumers-before-volumes "$consumers"
    emit helpers-before-volumes "$helpers"
    emit finalize-twice-clean "$twice"
}

partial_cut() {
    local cut="$1"
    reset_runtime
    C1192_CUT=$cut
    local code=0 out diag cleanup=0 work=0 mask=0 writer=0 reader=0 residue=-
    out=$(c849_fixture 2>>"$C1192_ROOT/scenario.err") || code=$?
    diag=$(diagnosis_of "$out")
    if grep -F -x -q "release-attempt container c849-${RUN}-restore-p1" "$C1192_ROOT/order"; then cleanup=1; fi
    volume_exists "$WORK" && work=1
    volume_exists "$MASK" && mask=1
    container_exists "$WRITER" && writer=1
    container_exists "$READER" && reader=1
    if [ -s "$CASE_DIR/fixture-cleanup-residue.txt" ]; then
        residue=$(head -n 1 "$CASE_DIR/fixture-cleanup-residue.txt")
    fi
    emit "partial-${cut}-diagnosis" "$diag"
    emit "partial-${cut}-exit" "$code"
    emit "partial-${cut}-cleanup" "$cleanup"
    emit "partial-${cut}-work" "$work"
    emit "partial-${cut}-mask" "$mask"
    emit "partial-${cut}-writer" "$writer"
    emit "partial-${cut}-reader" "$reader"
    emit "partial-${cut}-residue" "$residue"
}

mode_partial() {
    # Observe the real release call. An unacquired consumer must not be removed,
    # so a Docker rm event cannot witness finalization after identity-safe cleanup.
    eval "$(declare -f c849_fixture_consumer_release | sed '1s/c849_fixture_consumer_release/c1192_partial_consumer_release/')"
    c849_fixture_consumer_release() {
        order_log "release-attempt container $1"
        c1192_partial_consumer_release "$@"
    }
    c849_fixture_compose() { :; }
    c849_fixture_prepare() { :; }
    c849_fixture_seed() { :; }
    c849_fixture_nuget_race() { :; }
    c849_fixture_apphost() { :; }
    c849_fixture_npm() { :; }
    c849_fixture_retention() { :; }
    c849_fixture_prune() { :; }
    c849_fixture_contract_controls() { :; }
    c849_fixture_evidence() { :; }
    local cut
    for cut in work-create mask-create root-init writer-create writer-start reader-create reader-start rm-work; do
        partial_cut "$cut"
    done
}

mode_toxic() {
    reset_runtime
    local code=0
    ( c849_fixture_workspace_init ) || code=$?
    if [ "$code" = 0 ]; then
        ( c849_fixture_evidence_payloads ) || code=$?
    fi
    local toxic="$C1192_ROOT/volumes/$WORK/data/probes/toxic-input.txt"
    local sha=missing hits=0 groups=missing inventories=missing control=missing
    if [ -f "$toxic" ]; then sha=$(sha256sum "$toxic" | awk 'NR==1 { print $1 }'); fi
    if grep -Rq -- 'TOKEN_SENTINEL_C849_CONTENT' "$CASE_DIR"; then
        hits=$(grep -R -l -- 'TOKEN_SENTINEL_C849_CONTENT' "$CASE_DIR" | wc -l | tr -d ' ')
    fi
    if [ -f "$CASE_DIR/fixture-summary.txt" ]; then
        groups=$(sed -n 's/^groups=//p' "$CASE_DIR/fixture-summary.txt" | head -n 1)
        inventories=$(sed -n 's/^inventories=//p' "$CASE_DIR/fixture-summary.txt" | head -n 1)
    fi
    if [ -f "$CASE_DIR/.control-PC-26" ] && grep -Fxq 'EvidenceNotAllowListed' "$CASE_DIR/.control-PC-26"; then
        control=EvidenceNotAllowListed
    fi
    emit toxic-volume-sha "$sha"
    emit toxic-case-hits "$hits"
    emit summary-groups "$groups"
    emit summary-inventories "$inventories"
    emit control-pc26 "$control"
    emit toxic-init-code "$code"
}

mode_foreign() {
    local kind fault diag registered mounted removed
    for kind in different missing; do
        case "$kind" in
            different) fault=foreign-create ;;
            missing) fault=foreign-missing ;;
        esac
        reset_runtime
        : > "$C1192_ROOT/scenario.err"
        C1192_FAULT=$fault
        run_init
        diag=$(diagnosis_of "$C1192_OUT")
        registered=0
        if [ -f "$CASE_DIR/.fixture-volumes" ] && grep -Fxq -- "$WORK" "$CASE_DIR/.fixture-volumes"; then
            registered=1
        fi
        mounted=0
        if grep -q '^init-run	' "$C1192_ROOT/events" || grep -q '^helper-create	' "$C1192_ROOT/events"; then
            mounted=1
        fi
        removed=1
        volume_exists "$WORK" && removed=0
        emit "${kind}-diagnosis" "$diag"
        emit "${kind}-registered" "$registered"
        emit "${kind}-mounted" "$mounted"
        emit "${kind}-removed" "$removed"
    done
}

mode_argv() {
    local refused
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    run_init
    scan_argv_credentials
    emit argv-clean "$credential_ok"
    : > "$C1192_ROOT/argv"
    docker create --name "${WRITER}-env" --network none --user 1654:1654 \
        --env-file "${C1192_ROOT:?}/credential" \
        --mount "type=volume,source=$WORK,target=/fixture,volume-nocopy" \
        --entrypoint sleep "$ADMITTED" infinity >/dev/null 2>>"$C1192_ROOT/scenario.err" || true
    if argv_forbidden; then refused=1; else refused=0; fi
    emit argv-env-file-refused "$refused"
    : > "$C1192_ROOT/argv"
    docker create --name "${READER}-env" --network none \
        --mount "type=volume,source=$WORK,target=/fixture,readonly,volume-nocopy" \
        "$ADMITTED" --env-file "${C1192_ROOT:?}/credential" >/dev/null 2>>"$C1192_ROOT/scenario.err" || true
    if argv_forbidden; then refused=1; else refused=0; fi
    emit argv-reordered-refused "$refused"
}

# 150 x 0.2s stays the production bound. The model yields instead of burning 30s.
enable_synthetic_clock() {
    sleep() {
        printf '%s\n' "$*" >> "$C1192_ROOT/sleeps"
        if [ -n "${C1192_DELAY_READY:-}" ]; then
            local n
            n=$(grep -c '^0\.2$' "$C1192_ROOT/sleeps" || true)
            if [ "$n" = "$C1192_DELAY_READY" ]; then
                mkdir -p "$C1192_ROOT/volumes/$WORK/data/race/p1"
                printf '1\n' > "$C1192_ROOT/volumes/$WORK/data/race/p1/ready"
            fi
        fi
        command sleep 0.01
    }
}

plant_cache() {
    local name="$1" role="$2" dir
    plant_volume "$name" local null server2-runner "$RUN" "$role"
    dir="$C1192_ROOT/volumes/$name"
    printf '1\n' > "$dir/label-schema"
    printf '%s\n' "$role" > "$dir/label-cache-role"
    # These scenarios own the planted cache. Valid labels alone do not grant
    # the production mount guard acquisition authority.
    printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
}

sleeps_named() {
    local want="$1"
    grep -c "^${want}$" "$C1192_ROOT/sleeps" || true
}

require_init() {
    run_init
    if [ "$C1192_CODE" != 0 ]; then
        fail "init ${C1192_CODE} $(diagnosis_of "$C1192_OUT")"
    fi
}

refuse_unexpected() {
    if [ -s "$C1192_ROOT/scenario.err" ] && grep -q 'UNEXPECTED' "$C1192_ROOT/scenario.err"; then
        fail "unexpected $(tail -n 8 "$C1192_ROOT/scenario.err")"
    fi
}

# 0 when the /fixture mount is this run's work volume and not a payload bind.
mount_targets_work() {
    local mounts="$1" mount target typ source saw=0
    local IFS=';'
    local -a parts=()
    read -r -a parts <<< "$mounts"
    for mount in "${parts[@]}"; do
        [ -n "$mount" ] || continue
        target=$(mount_field "$mount" target)
        [ "$target" = /fixture ] || continue
        saw=$((saw + 1))
        typ=$(mount_field "$mount" type)
        source=$(mount_field "$mount" source)
        [ "$typ" = volume ] || return 1
        [ "$source" = "$WORK" ] || return 1
        case "$mount" in
            *volume-nocopy*) ;;
            *) return 1 ;;
        esac
    done
    [ "$saw" = 1 ]
}

classify_race_mounts() {
    zip_seen=0; p1_seen=0; p2_seen=0; p3_seen=0
    lh_seen=0; lw_seen=0; ph_seen=0; pw_seen=0
    zip_ok=0; p1_ok=0; p2_ok=0; p3_ok=0
    lh_ok=0; lw_ok=0; ph_ok=0; pw_ok=0
    local name mounts cmd key good
    while IFS=$'\t' read -r name mounts cmd || [ -n "${name:-}" ]; do
        [ -n "${name:-}" ] || continue
        key=""
        case "$cmd" in
            *make-zip.ps1*) key=zip ;;
            *lock-probe.ps1*)
                case "$name" in
                    *-lock-holder-shared) key=lh ;;
                    *-lock-waiter-shared) key=lw ;;
                    *-lock-holder-private) key=ph ;;
                    *-lock-waiter-private) key=pw ;;
                esac
                ;;
            *c849-fixture-restore*)
                case "$name" in
                    *-restore-p1) key=p1 ;;
                    *-restore-p2) key=p2 ;;
                    *-restore-p3) key=p3 ;;
                esac
                ;;
        esac
        [ -n "$key" ] || continue
        good=0
        if mount_targets_work "$mounts"; then good=1; fi
        case "$key" in
            zip) zip_seen=1; zip_ok=$good ;;
            p1) p1_seen=1; p1_ok=$good ;;
            p2) p2_seen=1; p2_ok=$good ;;
            p3) p3_seen=1; p3_ok=$good ;;
            lh) lh_seen=1; lh_ok=$good ;;
            lw) lw_seen=1; lw_ok=$good ;;
            ph) ph_seen=1; ph_ok=$good ;;
            pw) pw_seen=1; pw_ok=$good ;;
        esac
    done < "$C1192_ROOT/race-mounts"
}

race_files_present() {
    local data="$C1192_ROOT/volumes/$WORK/data"
    [ -s "$data/race/feed/C849.Probe.1.0.0.nupkg" ] || return 1
    [ -s "$data/race/p1/p1.csproj" ] || return 1
    [ -s "$data/race/p2/p2.csproj" ] || return 1
    [ -s "$data/race/p3/p3.csproj" ] || return 1
    [ -s "$data/race/make-zip.ps1" ] || return 1
    [ -s "$data/race/package/content/probe.txt" ] || return 1
    [ ! -e "$SERVER2_ROOT/race" ] || return 1
    grep -q 'PASS F-4' "$CASE_DIR/fixture-groups.txt"
}

race_paths_match() {
    local data="$C1192_ROOT/volumes/$WORK/data"
    [ -f "$data/race/NuGet.Config" ] || return 1
    grep -q 'value="/fixture/race/feed"' "$data/race/NuGet.Config" || return 1
    if grep -q 'value="/fixture/feed"' "$data/race/NuGet.Config"; then return 1; fi
    grep -q '/fixture/race/NuGet.Config' "$data/race/driver.sh" || return 1
    grep -q '/fixture/race/go' "$data/race/driver.sh" || return 1
    grep -q '/fixture/race/Offline.Config' "$data/race/offline.sh" || return 1
    grep -q 'value="/fixture/race/empty"' "$data/race/Offline.Config" || return 1
    if grep -q 'value="/fixture/empty"' "$data/race/Offline.Config"; then return 1; fi
    grep -F -q "\$root = '/fixture/race'" "$data/race/lock-probe.ps1" || return 1
    grep -q '/fixture/race/feed/C849.Probe.1.0.0.nupkg' "$C1192_ROOT/race-mounts"
}

decoy_untouched() {
    [ -f "$1" ] && [ "$(cat -- "$1")" = host-decoy ]
}

plant_host_decoys() {
    mkdir -p "$SERVER2_ROOT/race/p1" "$CASE_DIR/fixture-controller"
    printf 'host-decoy\n' > "$SERVER2_ROOT/race/p1/ready"
    printf 'host-decoy\n' > "$CASE_DIR/fixture-controller/go"
    printf 'host-decoy\n' > "$CASE_DIR/fixture-controller/shared-release"
}

# A read-only mount restricts its reader, not a peer's writable mount of the same
# volume. Interleave a peer write at the exact reader command, without a scheduler
# race: chmod on the shared backing tree must not make that write fail.
race_reader_allows_peer_write() (
    local data="$C1192_ROOT/volumes/$WORK/data" predicate status=0
    mkdir -p "$data/race"
    printf 'ready\n' > "$data/race/reader-probe"
    sh() {
        ( printf 'peer-write\n' > "$C1192_PEER_WRITE" ) 2>/dev/null || true
        command sh "$@"
    }
    for predicate in c849_fixture_volume_test_file c849_fixture_volume_test_exists; do
        C1192_PEER_WRITE="$data/race/$predicate-peer"
        "$predicate" /fixture/race/reader-probe || return 1
        [ -s "$C1192_PEER_WRITE" ] || status=1
    done
    return "$status"
)

mode_race() {
    local code=0 out boundary=0 mounts_ok=0 feed_ok=0 all_seen=0 reader_peer=0
    enable_synthetic_clock
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    race_reader_allows_peer_write && reader_peer=1
    out=$(c849_fixture_nuget_race 2>>"$C1192_ROOT/scenario.err") || code=$?
    refuse_unexpected
    classify_race_mounts
    if [ "$zip_seen" = 1 ] && [ "$p1_seen" = 1 ] && [ "$p2_seen" = 1 ] && [ "$p3_seen" = 1 ] \
        && [ "$lh_seen" = 1 ] && [ "$lw_seen" = 1 ] && [ "$ph_seen" = 1 ] && [ "$pw_seen" = 1 ]; then
        all_seen=1
    fi
    if [ "$all_seen" = 1 ] && [ "$zip_ok" = 1 ] && [ "$p1_ok" = 1 ] && [ "$p2_ok" = 1 ] && [ "$p3_ok" = 1 ] \
        && [ "$lh_ok" = 1 ] && [ "$lw_ok" = 1 ] && [ "$ph_ok" = 1 ] && [ "$pw_ok" = 1 ]; then
        mounts_ok=1
    fi
    if race_paths_match; then feed_ok=1; fi
    if [ "$code" = 0 ] && [ "$all_seen" = 1 ] && [ "$reader_peer" = 1 ] && race_files_present; then boundary=1; fi
    emit race-code "$code"
    emit race-diagnosis "$(diagnosis_of "$out")"
    emit race-reader-permits-peer-write "$reader_peer"
    emit race-boundary "$boundary"
    if [ "$boundary" = 1 ] && [ "$mounts_ok" = 1 ]; then
        emit race-work-volume-mount 1
    else
        emit race-work-volume-mount 0
    fi
    if [ "$boundary" = 1 ] && [ "$feed_ok" = 1 ]; then
        emit race-feed-recipient 1
    else
        emit race-feed-recipient 0
    fi
}

mode_barriers() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    local private_scratch="c849${RUN}private-scratch"
    local vol="$C1192_ROOT/volumes/$WORK/data"
    local already=0 delayed=0 never=0 dcount=0 ncount=0
    local ready_decoy=0 ready_absent=0 go_ok=0 release_ok=0
    local go_code=0 go_out rel_happy=0 rel_happy_out
    local child_code=0 child_out child_diag rel_code=0 rel_out rel_diag
    enable_synthetic_clock
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    unset C1192_DELAY_READY
    require_init
    plant_cache "$packages" nuget-packages
    plant_cache "$scratch" nuget-scratch
    plant_cache "$private_scratch" nuget-scratch
    plant_host_decoys
    c849_fixture_volume_mkdir /fixture/race/p1
    c849_fixture_volume_touch /fixture/race/p1/ready
    : > "$C1192_ROOT/sleeps"
    c849_fixture_wait_file /fixture/race/p1/ready || already=$?
    rm -f -- "$vol/race/p1/ready"
    : > "$C1192_ROOT/sleeps"
    C1192_DELAY_READY=3
    c849_fixture_wait_file /fixture/race/p1/ready || delayed=$?
    dcount=$(sleeps_named '0.2')
    unset C1192_DELAY_READY
    rm -f -- "$vol/race/p1/ready"
    : > "$C1192_ROOT/sleeps"
    c849_fixture_wait_file /fixture/race/p1/ready || never=$?
    ncount=$(sleeps_named '0.2')
    decoy_untouched "$SERVER2_ROOT/race/p1/ready" && ready_decoy=1
    [ ! -e "$vol/race/p1/ready" ] && ready_absent=1
    # Go and release are the barrier's and lock probe's own writes, checked against host decoys.
    go_out=$(c849_fixture_race_restore_barrier "$ADMITTED" "$packages" "$scratch" 2>>"$C1192_ROOT/scenario.err") || go_code=$?
    if [ "$go_code" = 0 ] && [ -f "$vol/race/go" ] && decoy_untouched "$CASE_DIR/fixture-controller/go"; then
        go_ok=1
    fi
    rel_happy_out=$(c849_fixture_race_lock_probe "$ADMITTED" "$scratch" "$private_scratch" 2>>"$C1192_ROOT/scenario.err") || rel_happy=$?
    if [ "$rel_happy" = 0 ] && [ -f "$vol/race/shared-release" ] \
        && decoy_untouched "$CASE_DIR/fixture-controller/shared-release"; then
        release_ok=1
    fi
    C1192_FAULT=restore-p2
    child_out=$(c849_fixture_race_restore_barrier "$ADMITTED" "$packages" "$scratch" 2>>"$C1192_ROOT/scenario.err") || child_code=$?
    C1192_FAULT=
    child_diag=$(diagnosis_of "$child_out")
    rm -f -- "$vol/race/shared-release" "$vol/race/shared-holder-entered" "$vol/race/shared-waiter-entered" \
        "$vol/race/private-holder-entered" "$vol/race/private-waiter-entered" "$vol/race/private-release"
    C1192_FAULT=touch-release
    rel_out=$(c849_fixture_race_lock_probe "$ADMITTED" "$scratch" "$private_scratch" 2>>"$C1192_ROOT/scenario.err") || rel_code=$?
    C1192_FAULT=
    rel_diag=$(diagnosis_of "$rel_out")
    refuse_unexpected
    if [ "$already" = 0 ] && [ "$never" = 1 ] && [ "$ready_decoy" = 1 ] && [ "$ready_absent" = 1 ]; then
        emit volume-ready-not-host-decoy 1
    else
        emit volume-ready-not-host-decoy 0
    fi
    if [ "$delayed" = 0 ] && [ "$dcount" = 3 ]; then
        emit delayed-ready 1
    else
        emit delayed-ready 0
    fi
    if [ "$never" = 1 ] && [ "$ncount" = 150 ]; then
        emit missing-ready-refused 1
    else
        emit missing-ready-refused 0
    fi
    local poll=0 wait_body
    wait_body=$(declare -f c849_fixture_wait_file)
    if c1192_has_substr 'seq 1 150' "$wait_body" \
        && c1192_has_substr 'sleep 0.2' "$wait_body" \
        && [ "$ncount" = 150 ]; then
        poll=1
    fi
    emit ready-poll-contract "$poll"
    emit volume-go-observed "$go_ok"
    emit volume-release-observed "$release_ok"
    if [ "$child_code" != 0 ] && [ "$child_diag" = FixtureConcurrentRestoreFailed ]; then
        emit child-failure-refused 1
    else
        emit child-failure-refused 0
    fi
    if [ "$rel_code" != 0 ] && [ "$rel_diag" = FixtureNuGetLockProbeFailed ] \
        && [ ! -e "$vol/race/shared-release" ]; then
        emit release-failure-refused 1
    else
        emit release-failure-refused 0
    fi
    emit already-status "$already"
    emit never-status "$never"
    emit child-diagnosis "$child_diag"
    emit release-diagnosis "$rel_diag"
}

install_lock_wrappers() {
    eval "$(declare -f c849_fixture_lock_assert | sed '1s/c849_fixture_lock_assert/c849_fixture_lock_assert_impl/')"
    eval "$(declare -f c849_fixture_wait_file | sed '1s/c849_fixture_wait_file/c849_fixture_wait_file_impl/')"
    c849_fixture_lock_assert() {
        if [ "$1" = /fixture/race/shared-waiter-entered ]; then
            if c849_fixture_volume_test_exists "$1"; then
                printf 'present\n' > "$C1192_ROOT/shared-before"
            else
                printf 'absent\n' > "$C1192_ROOT/shared-before"
            fi
        fi
        c849_fixture_lock_assert_impl "$@"
    }
    c849_fixture_wait_file() {
        local status=0
        c849_fixture_wait_file_impl "$@" || status=$?
        if [ "$status" = 0 ] && [ "$1" = /fixture/race/private-waiter-entered ]; then
            if ! c849_fixture_volume_test_exists /fixture/race/private-release; then
                printf '1\n' > "$C1192_ROOT/private-before-release"
            fi
        fi
        return "$status"
    }
}

mode_locks() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    local private_scratch="c849${RUN}private-scratch"
    local vol code=0 out happy_entered=0 shared_state=missing private_early=0
    local control_line=missing fault_code=0 fault_out fault_diag
    enable_synthetic_clock
    install_lock_wrappers
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    plant_cache "$packages" nuget-packages
    plant_cache "$scratch" nuget-scratch
    plant_cache "$private_scratch" nuget-scratch
    vol="$C1192_ROOT/volumes/$WORK/data"
    out=$(c849_fixture_race_lock_probe "$ADMITTED" "$scratch" "$private_scratch" 2>>"$C1192_ROOT/scenario.err") || code=$?
    if [ -f "$vol/race/shared-waiter-entered" ]; then happy_entered=1; fi
    if [ -f "$C1192_ROOT/shared-before" ]; then shared_state=$(cat -- "$C1192_ROOT/shared-before"); fi
    if [ -f "$C1192_ROOT/private-before-release" ]; then private_early=1; fi
    if [ -f "$CASE_DIR/.control-PC-17" ] && grep -Fxq 'NuGetLockNotShared' "$CASE_DIR/.control-PC-17"; then
        control_line=NuGetLockNotShared
    fi
    reset_runtime
    require_init
    plant_cache "$packages" nuget-packages
    plant_cache "$scratch" nuget-scratch
    plant_cache "$private_scratch" nuget-scratch
    vol="$C1192_ROOT/volumes/$WORK/data"
    C1192_FAULT=waiter-no-entry
    fault_out=$(c849_fixture_race_lock_probe "$ADMITTED" "$scratch" "$private_scratch" 2>>"$C1192_ROOT/scenario.err") || fault_code=$?
    C1192_FAULT=
    fault_diag=$(diagnosis_of "$fault_out")
    refuse_unexpected
    if [ "$shared_state" = absent ]; then
        emit shared-waiter-blocked 1
    else
        emit shared-waiter-blocked 0
    fi
    emit private-waiter-already-entered "$private_early"
    if [ "$code" = 0 ] && [ "$happy_entered" = 1 ] && [ "$fault_code" != 0 ] \
        && [ "$fault_diag" = FixtureNuGetLockProbeFailed ]; then
        emit shared-waiter-after-release 1
    else
        emit shared-waiter-after-release 0
    fi
    emit private-lock-not-shared "$control_line"
    emit lock-code "$code"
    emit lock-diagnosis "$(diagnosis_of "$out")"
    emit fault-diagnosis "$fault_diag"
}

# Missing probe is not "already written". The same predicate must become true only after both markers exist.
handoff_countercase() {
    local probe="$C1192_ROOT/countercase/lock-probe.ps1"
    mkdir -p "$C1192_ROOT/countercase"
    rm -f -- "$probe"
    if [ -f "$probe" ] && grep -q 'ExecuteWithFileLocked' "$probe" && grep -q '/fixture/race' "$probe"; then
        return 1
    fi
    printf '%s\n' 'ExecuteWithFileLocked' '/fixture/race' > "$probe"
    grep -q 'ExecuteWithFileLocked' "$probe" && grep -q '/fixture/race' "$probe"
}

mode_handoff() {
    local code=0 out snap=0 fact=0
    enable_synthetic_clock
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    out=$(c849_fixture_race_materialize "$ADMITTED" 2>>"$C1192_ROOT/scenario.err") || code=$?
    refuse_unexpected
    if [ -f "$C1192_ROOT/probe-at-handoff" ]; then snap=$(cat -- "$C1192_ROOT/probe-at-handoff"); fi
    if [ "$code" = 0 ] && [ "$snap" = 1 ] && handoff_countercase; then fact=1; fi
    emit probe-before-handoff "$fact"
    emit handoff-code "$code"
    emit handoff-snapshot "$snap"
    emit handoff-diagnosis "$(diagnosis_of "$out")"
}

npm_run_bind() {
    case "$1" in
        *type=bind*) return 0 ;;
        *) return 1 ;;
    esac
}

classify_npm_runs() {
    npm_add_ok=0
    npm_add_bind=0
    refill_ok=0
    refill_bind=0
    offline_seen=0
    offline_ok=1
    offline_bind=0
    hash_ok=0
    payload_bind=0
    local name network image mounts cmd
    while IFS=$'\t' read -r name network image mounts cmd || [ -n "${name:-}" ]; do
        [ -n "${name:-}" ] || continue
        case "$cmd" in
            *'cache add /fixture/npm-work/package.tgz'*)
                if [ "$network" = none ] && mount_targets_work "$mounts"; then npm_add_ok=1; fi
                npm_run_bind "$mounts" && npm_add_bind=1
                ;;
            *'cache add /fixture/npm-offline/pkg.tgz'*)
                if [ "$network" = none ] && mount_targets_work "$mounts" && [[ "$mounts" == *readonly* ]] \
                    && [[ "$cmd" == *'/fixture/npm-offline/pkg.tgz'* ]]; then
                    refill_ok=1
                fi
                npm_run_bind "$mounts" && refill_bind=1
                ;;
            *'npm ci --offline'*)
                offline_seen=1
                if [ "$network" != none ] || ! mount_targets_work "$mounts"; then offline_ok=0; fi
                npm_run_bind "$mounts" && offline_bind=1
                ;;
            *hash-tarball.ps1*)
                if [ "$network" = none ] && [ "$image" = "$ADMITTED" ] && mount_targets_work "$mounts" \
                    && [[ "$mounts" == *readonly* ]] \
                    && [[ "$cmd" == *'-File /fixture/npm-offline/hash-tarball.ps1'* ]] \
                    && [[ "$cmd" == *'-Path /fixture/npm-offline/pkg.tgz'* ]]; then
                    hash_ok=1
                fi
                ;;
        esac
        case "$cmd" in
            # The refill tarball read is refill_bind. Counting it here would make a
            # refill bind fail npm-work-volume before refill-volume-source.
            *'cache add /fixture/npm-offline/pkg.tgz'*)
                ;;
            *hash-tarball.ps1*|*server.js*|*'cache add'*|*'npm ci --offline'*)
                npm_run_bind "$mounts" && payload_bind=1
                ;;
        esac
    done < "$C1192_ROOT/run-log"
    # The last body command is often "not a bind" (status 1). A bare caller
    # would treat that status as a harness failure under set -e.
    return 0
}

mode_npm() {
    local code=0 refill_status=0 out data files=0 decoys=0
    enable_synthetic_clock
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    printf 'host-decoy\n' > "$SERVER2_ROOT/npm-work"
    printf 'host-decoy\n' > "$SERVER2_ROOT/npm-offline"
    plant_container "c849-${RUN}-donor" c849-fixture "$RUN" donor "$ADMITTED" ""
    printf '%s\n' "c849${RUN}temp" > "$C1192_ROOT/containers/c849-${RUN}-donor/label-project"
    out=$(c849_fixture_npm_work "$ADMITTED" "c849-${RUN}-donor" 2>>"$C1192_ROOT/scenario.err") || code=$?
    if [ "$code" = 0 ]; then
        # c849_volume's admission case expands every cache name. Prepare would
        # have assigned the nuget names before npm runs.
        C849_PACKAGES="c849${RUN}unset-packages"
        C849_SCRATCH="c849${RUN}unset-scratch"
        if [ "$mode" = npm-network-create ]; then
            C1192_FAULT=npm-network-create
        fi
        out=$(c849_fixture_npm 2>>"$C1192_ROOT/scenario.err") || code=$?
        C1192_FAULT=
    fi
    if [ "$code" = 0 ]; then
        C849_NPM="c849${RUN}npmcache-npm"
        c849_fixture_refill_tarball "$ADMITTED" >/dev/null 2>>"$C1192_ROOT/scenario.err" || refill_status=$?
    fi
    classify_npm_runs
    data="$C1192_ROOT/volumes/$WORK/data"
    if [ -s "$data/npm-work/package.tgz" ] && [ -s "$data/npm-work/cache/_cacache/entry" ] \
        && [ -s "$C1192_ROOT/donor-npm/entry" ] \
        && [ -s "$data/npm-offline/pkg.tgz" ] \
        && grep -q 'sha512-FIXTUREHASH' "$data/npm-offline/b/package-lock.json" \
        && [ "$hash_ok" = 1 ]; then
        files=1
    fi
    if decoy_untouched "$SERVER2_ROOT/npm-work" && decoy_untouched "$SERVER2_ROOT/npm-offline"; then
        decoys=1
    fi
    if [ "$npm_add_ok" = 1 ] && [ "$npm_add_bind" = 0 ] && [ "$files" = 1 ] && [ "$decoys" = 1 ] \
        && [ "$payload_bind" = 0 ]; then
        emit npm-work-volume 1
    else
        emit npm-work-volume 0
    fi
    if [ "$refill_ok" = 1 ] && [ "$refill_bind" = 0 ] && [ "$refill_status" = 0 ] && [ "$decoys" = 1 ]; then
        emit refill-volume-source 1
    else
        emit refill-volume-source 0
    fi
    if [ "$offline_seen" = 1 ] && [ "$offline_ok" = 1 ] && [ "$offline_bind" = 0 ] \
        && [ -f "$data/npm-offline/b/node_modules/c849-cache-probe/sentinel" ] \
        && [ "$(cat -- "$data/npm-offline/b/node_modules/c849-cache-probe/sentinel")" = $'C849_NPM_OFFLINE' ]; then
        emit offline-network-none 1
    else
        emit offline-network-none 0
    fi
    emit npm-code "$code"
    emit npm-refill-code "$refill_status"
    emit npm-hash-ok "$hash_ok"
    if [ "$mode" = npm-network-create ]; then
        emit npm-diagnosis "$(diagnosis_of "$out")"
    fi
    refuse_unexpected
}

npm_status_of() { cat -- "$C1192_ROOT/status-$1"; }
npm_out_of() { cat -- "$C1192_ROOT/out-$1"; }
npm_bytes_of() { cat -- "$C1192_ROOT/bytes-$1"; }

mode_npm_sentinel() {
    local cache="c849${RUN}npmcache-npm"
    local data modules sentinel good=0 missing=0 wrong=0 miss=0
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    plant_cache "$cache" npm-content
    data="$C1192_ROOT/volumes/$WORK/data"
    modules="$data/npm-offline/b/node_modules"
    sentinel="$modules/c849-cache-probe/sentinel"
    case "$modules" in
        "$C1192_ROOT/volumes/$WORK/data/npm-offline/"*) ;;
        *) fail "sentinel path" ;;
    esac
    run_npm_install() {
        local fault="$1" tag="$2" status=0 actual
        rm -rf -- "$modules"
        : > "$C1192_ROOT/events"
        C1192_FAULT=$fault
        actual=$(c849_fixture_npm_install "$ADMITTED" "$cache" b 2>>"$C1192_ROOT/scenario.err") || status=$?
        C1192_FAULT=
        printf '%s\n' "$status" > "$C1192_ROOT/status-$tag"
        printf '%s\n' "$actual" > "$C1192_ROOT/out-$tag"
        cp -- "$C1192_ROOT/events" "$C1192_ROOT/events-$tag"
        if [ -f "$sentinel" ]; then
            printf '%s\n' "$(cat -- "$sentinel")" > "$C1192_ROOT/bytes-$tag"
        else
            printf 'ABSENT\n' > "$C1192_ROOT/bytes-$tag"
        fi
    }
    run_npm_install "" good
    run_npm_install npm-missing missing
    run_npm_install npm-wrong wrong
    run_npm_install npm-enotcached miss
    run_npm_install npm-other other
    if [ "$(npm_status_of good)" = 0 ] && [ "$(npm_out_of good)" = "" ] \
        && [ "$(npm_bytes_of good)" = C849_NPM_OFFLINE ] \
        && grep -q 'cat --' "$C1192_ROOT/events-good"; then
        good=1
    fi
    # test -f is the missing-file refusal. A later byte compare also rejects an
    # empty read, so status 1 alone does not show that existence was checked.
    if [ "$good" = 1 ] && [ "$(npm_status_of missing)" = 1 ] && [ "$(npm_bytes_of missing)" = ABSENT ] \
        && grep -q 'test -f' "$C1192_ROOT/events-missing" \
        && ! grep -q 'cat --' "$C1192_ROOT/events-missing"; then
        missing=1
    fi
    if [ "$good" = 1 ] && [ "$(npm_status_of wrong)" != 0 ] && [ "$(npm_bytes_of wrong)" = WRONG ] \
        && grep -q 'cat --' "$C1192_ROOT/events-wrong" \
        && [ "$(npm_out_of wrong)" != NpmOfflineCacheMiss ]; then
        wrong=1
    fi
    if [ "$good" = 1 ] && [ "$(npm_status_of miss)" = 2 ] && [ "$(npm_out_of miss)" = NpmOfflineCacheMiss ] \
        && [ "$(npm_status_of other)" = 1 ] && [ "$(npm_out_of other)" != NpmOfflineCacheMiss ]; then
        miss=1
    fi
    emit missing-sentinel-refused "$missing"
    emit wrong-sentinel-refused "$wrong"
    emit offline-miss-refused "$miss"
    emit exact-sentinel-accepted "$good"
    refuse_unexpected
}

# Stop Seed/Preview/Prune after the backend decision. Production cases do not create this file.
# Without it, exercising those entry points here would enter the donor/reconnect loop.
backend_stop() {
    : > "$CASE_DIR/.payload-note-only"
}

backend_entries() {
    local entry status
    : > "$CASE_DIR/payload-entries.txt"
    for entry in seed saved cold preview prune; do
        status=0
        case "$entry" in
            seed) ( c849_seed ) || status=$? ;;
            saved) ( c849_saved_copy /not-a-donor "$CASE_DIR/unused-stage" "$ADMITTED" ) || status=$? ;;
            cold) ( c849_cold_seed ) || status=$? ;;
            preview) ( c849_preview ) || status=$? ;;
            prune) ( C590_PREVIEW_RUN=not-valid c849_prune ) || status=$? ;;
        esac
        printf '%s\n' "$status" > "$CASE_DIR/status-$entry"
    done
}

backend_lines_are() {
    local expect="$1" entry line status
    for entry in seed saved cold preview prune; do
        line=$(grep "^${entry} " "$CASE_DIR/payload-entries.txt" | tail -n 1 || true)
        status=$(cat "$CASE_DIR/status-$entry")
        [ "$line" = "$entry $expect" ] || return 1
        if [ "$expect" = "host $SERVER2_ROOT/cache" ]; then
            [ "$status" = 85 ] || return 1
        else
            [ "$status" = 86 ] || return 1
        fi
    done
}

mode_backend() {
    local default_host=0 owned=0 own_status=0 line status entry
    if ! declare -F c849_payload_select >/dev/null 2>&1; then
        emit real-cases-host-backend 0
        emit fixture-owned-volume 0
        return 0
    fi
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    backend_stop
    C849_PAYLOAD_BACKEND=volume
    export C849_PAYLOAD_BACKEND
    c849_payload_own volume || own_status=$?
    backend_entries
    if [ "$own_status" = 2 ] && backend_lines_are "host $SERVER2_ROOT/cache"; then
        default_host=1
    fi
    unset C849_PAYLOAD_BACKEND
    backend_entries
    backend_lines_are "host $SERVER2_ROOT/cache" || default_host=0

    reset_runtime
    require_init || default_host=0
    backend_stop
    C849_PAYLOAD_BACKEND=volume
    export C849_PAYLOAD_BACKEND
    if [ -f "$CASE_DIR/fixture-controller/payload-backend" ] \
        && [ ! -L "$CASE_DIR/fixture-controller/payload-backend" ] \
        && [ "$(cat -- "$CASE_DIR/fixture-controller/payload-backend")" = host ]; then
        :
    else
        default_host=0
    fi
    backend_entries
    backend_lines_are "host $SERVER2_ROOT/cache" || default_host=0
    emit real-cases-host-backend "$default_host"

    unset C849_PAYLOAD_BACKEND
    own_status=0
    c849_payload_own volume || own_status=$?
    backend_entries
    owned=1
    [ "$own_status" = 0 ] || owned=0
    backend_lines_are "volume /fixture/cache" || owned=0
    if compgen -G "$SERVER2_ROOT/cache/stage-*" > /dev/null; then owned=0; fi
    emit fixture-owned-volume "$owned"
}

plant_donor_tree() {
    local donor="$1"
    mkdir -p "$donor/packages/c913.probe/1.0.0" "$donor/npm"
    printf '{}\n' > "$donor/packages/c913.probe/1.0.0/.nupkg.metadata"
    printf 'probe\n' > "$donor/packages/c913.probe/1.0.0/data"
    printf 'dot\n' > "$donor/packages/c913.probe/1.0.0/.hidden"
    printf 'spaced\n' > "$donor/packages/c913.probe/1.0.0/my file.txt"
    printf '#!/bin/sh\n' > "$donor/packages/c913.probe/1.0.0/tool"
    chmod 755 "$donor/packages/c913.probe/1.0.0/tool"
    printf 'npm\n' > "$donor/npm/content"
}

transfer_refused() {
    local status="$1" trace="$2"
    [ "$status" != 0 ] || return 1
    [ ! -e "$CASE_DIR/payload-accepted" ] || return 1
    [ ! -L "$CASE_DIR/payload-accepted" ] || return 1
    ! grep -q 'cp -a' "$trace"
}

transfer_once() {
    local fault="$1" status=0 data
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    data="$C1192_ROOT/volumes/$WORK/data"
    plant_donor_tree "$data/donor"
    printf 'host-decoy\n' > "$SERVER2_ROOT/donor-decoy"
    C1192_FAULT=$fault
    set +o pipefail
    c849_payload_accept_transfer "$WRITER" /fixture/donor "$WRITER" /fixture/cache/stage \
        "$CASE_DIR/payload-accepted" "$ADMITTED" || status=$?
    set -o pipefail
    C1192_FAULT=
    printf '%s\n' "$status" > "$C1192_ROOT/transfer-status"
    cp -- "$C1192_ROOT/trace" "$C1192_ROOT/transfer-trace"
}

mode_transfer() {
    local data stage imported source_ok=0 sink_ok=0
    local dot_ok=0 exec_ok=0 space_ok=0 tool_mode
    if ! declare -F c849_payload_accept_transfer >/dev/null 2>&1; then
        emit source-copy-failure-refused 0
        emit sink-copy-failure-refused 0
        emit copy-dotfile-retained 0
        emit copy-executable-retained 0
        emit copy-spaced-path-retained 0
        return 0
    fi
    transfer_once source-copy
    transfer_refused "$(cat "$C1192_ROOT/transfer-status")" "$C1192_ROOT/transfer-trace" && source_ok=1
    transfer_once sink-copy
    transfer_refused "$(cat "$C1192_ROOT/transfer-status")" "$C1192_ROOT/transfer-trace" && sink_ok=1
    transfer_once ""
    data="$C1192_ROOT/volumes/$WORK/data"
    stage="$data/cache/stage/packages/c913.probe/1.0.0"
    imported="$data/cache/imported/packages/c913.probe/1.0.0"
    if [ "$(cat "$C1192_ROOT/transfer-status")" = 0 ] \
        && [ -f "$CASE_DIR/payload-accepted" ] && [ ! -L "$CASE_DIR/payload-accepted" ] \
        && [ -f "$stage/.hidden" ] && [ "$(cat -- "$stage/.hidden")" = dot ] \
        && [ -f "$imported/.hidden" ] && [ "$(cat -- "$imported/.hidden")" = dot ] \
        && [ "$(cat -- "$SERVER2_ROOT/donor-decoy")" = host-decoy ]; then
        dot_ok=1
    fi
    if [ -f "$stage/tool" ] && [ -f "$imported/tool" ]; then
        tool_mode=$(stat -c %a -- "$stage/tool")
        [ "$tool_mode" = 755 ] && [ "$(stat -c %a -- "$imported/tool")" = 755 ] && exec_ok=1
    fi
    if [ -f "$stage/my file.txt" ] && [ -f "$imported/my file.txt" ] \
        && [ "$(cat -- "$stage/my file.txt")" = spaced ] \
        && [ "$(cat -- "$imported/my file.txt")" = spaced ] \
        && [ -f "$stage/data" ] && [ "$(cat -- "$stage/data")" = probe ]; then
        space_ok=1
    fi
    emit source-copy-failure-refused "$source_ok"
    emit sink-copy-failure-refused "$sink_ok"
    emit copy-dotfile-retained "$dot_ok"
    emit copy-executable-retained "$exec_ok"
    emit copy-spaced-path-retained "$space_ok"
    refuse_unexpected
}

# Model readonly mounts with filesystem permissions; the harness runs as non-root.
model_mount_exec() {
    local mount="$1" data="$2" status=0
    shift 2
    if field_has "$mount" readonly; then
        [ "$(id -u)" != 0 ] || { printf 'read-only model requires non-root\n' >&2; return 97; }
        # These exact production probes cannot write. Changing shared directory
        # modes for a reader would also deny a concurrent writable mount.
        if [ "$#" = 5 ] && [ "$1" = sh ] && [ "$2" = -c ] && [ "$4" = sh ]; then
            case "$3" in
                'test -f "$1"'|'test -e "$1"') "$@"; return ;;
            esac
        fi
        find "$data" -type d -exec chmod a-w {} +
        "$@" || status=$?
        find "$data" -type d -exec chmod u+w {} +
    else
        "$@" || status=$?
    fi
    return "$status"
}

mode_writable_pruning() {
    reset_runtime
    require_init
    local data="$C1192_ROOT/volumes/$WORK/data" status=0 reader_ok=0 writer_ok=0
    plant_donor_tree "$data/donor"
    mkdir -p "$data/donor/packages/incomplete/1.0"
    printf partial > "$data/donor/packages/incomplete/1.0/data"
    {
        declare -f c849_validate_seed_relative c849_validate_seed_tree
        printf '%s\n' 'c849_validate_seed_tree "$1"'
    } | c849_fixture_helper_exec reader /bin/bash -s -- /fixture/donor || status=$?
    [ "$status" != 0 ] && [ -f "$data/donor/packages/incomplete/1.0/data" ] && reader_ok=1
    status=0
    c849_payload_accept_transfer "$WRITER" /fixture/donor "$WRITER" /fixture/cache/stage "$CASE_DIR/payload-accepted" "$ADMITTED" || status=$?
    [ "$status" = 0 ] && [ -f "$CASE_DIR/payload-accepted" ] && [ ! -e "$data/cache/stage/packages/incomplete/1.0" ] && writer_ok=1
    emit reader-cannot-prune "$reader_ok"
    emit writer-prunes-incomplete-stage "$writer_ok"
    refuse_unexpected
}

# S6 executes the real import across independently observed mounted volumes.
apphost_run() {
    local joined="${PARSED_CMD[*]:-}" source data cache mount arg
    printf '%s\t%s\t%s\t%s\t%s\n' "${PARSED_NAME:-noname}" "$PARSED_NETWORK" "$PARSED_IMAGE" "$PARSED_MOUNTS" "$joined" >> "$C1192_ROOT/run-log"
    if [ "$PARSED_ENTRY" = sleep ]; then
        store_container "$PARSED_NAME" "$PARSED_IMAGE" "$PARSED_MOUNTS" "$PARSED_LABELS"
        return 0
    fi
    case "$joined" in
        *'cp -a '*)
            source=$(mount_source_for_target /fixture) || return 1
            cache=$(volume_data_for_target /cache) || return 1
            data="$C1192_ROOT/volumes/$source/data"
            local args=()
            for arg in "${PARSED_CMD[@]}"; do
                arg="${arg//\/fixture/$data}"
                args+=("${arg//\/cache/$cache}")
            done
            bash -c 'chown() { :; }; export -f chown; exec bash "$@"' sh "${args[@]}"
            ;;
        *)
            IFS=';' read -r -a mounts <<< "$PARSED_MOUNTS"
            mount="${mounts[0]:-}"
            run_script "$mount" "${PARSED_CMD[@]}"
            ;;
    esac
}

# Compiler/image tools have their own tests; this smoke seam observes the mask recipient.
apphost_smoke() {
    local helper="$1" mount source target pack
    [ "$helper" = "c849-${RUN}-masked" ] || return 0
    mount=$(cat "$C1192_ROOT/containers/$helper/mount")
    source=$(mount_field "$mount" source)
    target=$(mount_field "$mount" target)
    case "$target" in
        /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/9.0.20|/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.20|/usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/9.0.20) ;;
        *) return 1 ;;
    esac
    [ "$(mount_field "$mount" type)" = volume ] && [ "$source" = "$MASK" ] \
        && field_has "$mount" readonly && field_has "$mount" volume-nocopy || return 1
    [ -z "$(find "$C1192_ROOT/volumes/$MASK/data" -mindepth 1 -print -quit)" ] || return 1
    pack="${target#/usr/share/dotnet/packs/}"; pack="${pack%/9.0.20}"
    printf '%s\n' "$pack" >> "$C1192_ROOT/masked-packs"
    if [ "$C1192_FAULT" = masked-smoke ]; then return 9; fi
    printf 'ImagePackMissing:%s\n' "$pack" > "$CASE_DIR/smoke.txt"
    return 2
}

mode_apphost() {
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    : > "$C1192_ROOT/masked-packs"
    require_init
    C849_NPM="c849${RUN}npmcache-npm"
    printf '%s\n' "$C849_NPM" >> "$CASE_DIR/.fixture-volumes"
    plant_volume "$C849_NPM" local null server2-runner "$RUN" npm-content
    printf 1 > "$C1192_ROOT/volumes/$C849_NPM/label-schema"
    printf npm-content > "$C1192_ROOT/volumes/$C849_NPM/label-cache-role"
    mkdir -p "$SERVER2_ROOT/apphost"
    printf host-decoy > "$SERVER2_ROOT/apphost/decoy"
    c849_smoke() { apphost_smoke "$@"; }
    local code=0 out data cache payload=0 decoy=0 import=0 bytes=0 masks=0 empty=0 clean=0
    out=$(c849_fixture_apphost 2>&1) || code=$?
    data="$C1192_ROOT/volumes/$WORK/data/apphost/packages"
    cache="$C1192_ROOT/volumes/c849${RUN}smoke-packages/data"
    [ "$code" = 0 ] && [ -f "$data/c913.probe/1.0.0/data" ] && payload=1
    [ "$(cat "$SERVER2_ROOT/apphost/decoy")" = host-decoy ] \
        && [ "$(find "$SERVER2_ROOT/apphost" -mindepth 1 | wc -l)" = 1 ] && decoy=1
    local name network image mounts cmd mask_readonly=1 mask_nocopy=1 mask_exact=1 mask_seen=0
    while IFS=$'\t' read -r name network image mounts cmd; do
        case "$cmd" in
            *'cp -a '*)
                if mount_targets_work "$mounts" && [[ "$mounts" == *'target=/fixture,volume-nocopy,readonly'* ]] \
                    && [[ "$mounts" != *type=bind* ]] && [ "$network" = none ]; then import=1; fi
                ;;
        esac
        if [ "$name" = "c849-${RUN}-masked" ]; then
            mask_seen=$((mask_seen + 1))
            field_has "$mounts" readonly || mask_readonly=0
            field_has "$mounts" volume-nocopy || mask_nocopy=0
            case "$mounts" in
                "type=volume,source=$MASK,target=/usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/9.0.20,readonly,volume-nocopy;"|"type=volume,source=$MASK,target=/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.20,readonly,volume-nocopy;"|"type=volume,source=$MASK,target=/usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/9.0.20,readonly,volume-nocopy;") ;;
                *) mask_exact=0 ;;
            esac
        fi
    done < "$C1192_ROOT/run-log"
    [ "$mask_seen" = 3 ] || { mask_readonly=0; mask_nocopy=0; mask_exact=0; }
    emit mask-readonly "$mask_readonly"
    emit mask-nocopy "$mask_nocopy"
    emit mask-exact-pack "$mask_exact"
    if [ -f "$cache/c913.probe/1.0.0/data" ] && [ "$(cat "$cache/c913.probe/1.0.0/data")" = ordinary-payload ] \
        && [ "$(stat -c %a "$cache")" = 700 ] && [ "$(stat -c %a "$cache/c913.tools/2.0.0/tool")" = 751 ] \
        && [ -f "$cache/c913.probe/1.0.0/.nupkg.metadata" ]; then bytes=1; fi
    if [ "$(sort -u "$C1192_ROOT/masked-packs" | wc -l)" = 3 ] \
        && [ "$(wc -l < "$C1192_ROOT/masked-packs")" = 3 ] \
        && [ "$(grep -c '^PC-18\.' "$CASE_DIR/fixture-control-variants.txt")" = 3 ]; then masks=1; fi
    [ -z "$(find "$C1192_ROOT/volumes/$MASK/data" -mindepth 1 -print -quit)" ] && empty=1
    C1192_FAULT=masked-smoke
    code=0
    out=$(c849_fixture_masked_pack "$ADMITTED" Microsoft.NETCore.App.Ref "$SERVER2_ROOT/apphost" 2>&1) || code=$?
    [ "$code" = 2 ] && [[ "$out" == *ControlNotSensitive* ]] && ! container_exists "c849-${RUN}-masked" && clean=1
    emit apphost-volume-payload "$payload"
    emit apphost-host-decoy "$decoy"
    emit apphost-import-readonly "$import"
    emit apphost-import-bytes-mode "$bytes"
    emit mask-exact-three "$masks"
    emit mask-empty "$empty"
    emit mask-failure-cleaned "$clean"
    C1192_FAULT=
    local foreign=0 work_foreign=0 cache_foreign=0 before after
    before=$(wc -l < "$C1192_ROOT/run-log")
    printf foreign > "$C1192_ROOT/volumes/$MASK/label-owner"
    code=0
    out=$(c849_fixture_masked_pack "$ADMITTED" Microsoft.NETCore.App.Ref "$SERVER2_ROOT/apphost" 2>&1) || code=$?
    after=$(wc -l < "$C1192_ROOT/run-log")
    [ "$code" != 0 ] && [ "$before" = "$after" ] && foreign=1
    printf c849-fixture > "$C1192_ROOT/volumes/$MASK/label-owner"
    printf foreign > "$C1192_ROOT/volumes/$WORK/label-owner"
    code=0
    out=$(c849_fixture_apphost 2>&1) || code=$?
    [ "$code" != 0 ] && [ "$(diagnosis_of "$out")" = FixtureWorkspaceForeign ] && [ "$(wc -l < "$C1192_ROOT/run-log")" = "$after" ] && work_foreign=1
    printf c849-fixture > "$C1192_ROOT/volumes/$WORK/label-owner"
    printf foreign > "$C1192_ROOT/volumes/$C849_NPM/label-owner"
    code=0
    out=$(c849_fixture_apphost 2>&1) || code=$?
    [ "$code" != 0 ] && [ "$(diagnosis_of "$out")" = CacheVolumeForeign ] && [ "$(wc -l < "$C1192_ROOT/run-log")" = "$after" ] && cache_foreign=1
    emit mask-foreign-refused "$foreign"
    emit apphost-foreign-work-refused "$work_foreign"
    emit apphost-foreign-cache-refused "$cache_foreign"
    printf server2-runner > "$C1192_ROOT/volumes/$C849_NPM/label-owner"
    # Previous successful masks must not supply the failed-start registration witness.
    grep -Fxv "c849-${RUN}-masked" "$CASE_DIR/.fixture-helpers" > "$CASE_DIR/.fixture-helpers.fresh"
    mv "$CASE_DIR/.fixture-helpers.fresh" "$CASE_DIR/.fixture-helpers"
    C1192_CUT=masked-start
    : > "$C1192_ROOT/order"
    code=0
    out=$(c849_fixture_masked_pack "$ADMITTED" Microsoft.NETCore.App.Ref 2>&1) || code=$?
    local partial=0
    if [ "$code" != 0 ] && container_exists "c849-${RUN}-masked" \
        && grep -Fxq "c849-${RUN}-masked" "$CASE_DIR/.fixture-helpers"; then
        c849_fixture_cleanup
        if ! container_exists "c849-${RUN}-masked" && ! volume_exists "$MASK" && ! volume_exists "$WORK"; then
            partial=1
        fi
    fi
    emit mask-partial-start-cleaned "$partial"
    local consumer_first=0
    if awk -v consumer="container c849-${RUN}-masked" -v writer="container $WRITER" '
        $0==consumer { c=NR } $0==writer { w=NR }
        END { exit !(c>0 && w>c) }
    ' "$C1192_ROOT/order"; then consumer_first=1; fi
    emit mask-consumer-before-workspace "$consumer_first"
    refuse_unexpected
}

# Parse the real Compose control document, so its external/private identities drive deletion.
retention_cache_name() {
    local project="$1" file="$2" key="$3" line
    line=$(sed -n "/^  $key: /p" "$file")
    if [[ "$line" == *'external: true'* ]]; then
        printf '%s' "${line##*name: }" | tr -d ' }'
    else
        printf '%s_%s' "$project" "$key"
    fi
}

retention_compose() {
    local project="" file="" action="" volume key target arg data
    while [ $# -gt 0 ]; do
        case "$1" in
            -p) project="$2"; shift 2 ;;
            -f) file="$2"; shift 2 ;;
            *) action="$1"; shift; break ;;
        esac
    done
    [ -n "$project" ] && [ -f "$file" ] || return 97
    printf '%s %s %s\n' "$project" "$action" "$*" >> "$C1192_ROOT/compose-actions"
    case "$action" in
        up)
            if [ "$mode" = compose-absence ]; then
                mkdir -p "$C1192_ROOT/compose-networks/$project"
                plant_container "${project}-probe-1" test "$RUN" private "$ADMITTED" ""
                printf '%s\n' "$project" > "$C1192_ROOT/containers/${project}-probe-1/label-project"
            fi
            cp "$file" "$C1192_ROOT/$project.yml"
            if [[ "$file" == *negative.yml ]]; then
                plant_volume "${project}_cache" local null test "$RUN" private
                return 0
            fi
            for key in packages scratch npm; do
                volume=$(retention_cache_name "$project" "$file" "$key")
                volume_exists "$volume" || plant_volume "$volume" local null test "$RUN" private
                data="$C1192_ROOT/volumes/$volume/data"
                # Image content appears on a missing nocopy mount, as on Docker.
                if ! awk -v key="$key" '/source:/ { selected=($2==key) } selected && /nocopy: true/ {ok=1} END {exit !ok}' "$file"; then
                    printf image-content > "$data/image-content"
                    chmod 755 "$data"
                fi
            done
            for key in runner-tmp work runner-state dind-data; do
                volume="${project}_$key"
                volume_exists "$volume" || plant_volume "$volume" local null test "$RUN" private
                [ "$key" != runner-tmp ] || chmod 1777 "$C1192_ROOT/volumes/$volume/data"
            done
            if [[ "$project" == *temp ]] && [[ "$*" == *--force-recreate* ]]; then
                for key in packages scratch npm; do
                    volume=$(retention_cache_name "$project" "$file" "$key")
                    [ "$(cat "$C1192_ROOT/volumes/$volume/data/retention-sentinel")" = retained ] || return 1
                done
                printf 1 > "$C1192_ROOT/recreate-retained"
            fi
            ;;
        exec)
            [ "$1" = -T ] && shift
            [ "$1" = probe ] && shift
            local args=()
            for arg in "$@"; do
                for key in packages scratch npm; do
                    case "$key" in
                        packages) target=/home/app/.nuget/packages ;;
                        scratch) target=/var/cache/antiphon/nuget-scratch ;;
                        npm) target=/home/app/.npm/_cacache ;;
                    esac
                    volume=$(retention_cache_name "$project" "$file" "$key")
                    data="$C1192_ROOT/volumes/$volume/data"
                    arg="${arg//$target/$data}"
                done
                # Prefix-only argument mapping; generated private path also lives under /tmp.
                if [ "$arg" = /tmp ]; then arg="$C1192_ROOT/volumes/${project}_runner-tmp/data";
                else arg="${arg//%a \/tmp/%a $C1192_ROOT/volumes/${project}_runner-tmp/data}"; fi
                args+=("$arg")
            done
            "${args[@]}" || return $?
            if [[ "$project" == *main ]] && [[ "$*" == *retention-sentinel* ]]; then
                for key in runner-tmp work runner-state dind-data; do
                    volume_exists "c849${RUN}temp_$key" && return 1
                    volume_exists "c849${RUN}main_$key" || return 1
                done
                printf 1 > "$C1192_ROOT/retention-before-main-down"
            fi
            ;;
        down)
            [[ "$*" == *-v* ]] || return 0
            if [ "$mode" = compose-absence ]; then
                printf '%s\n' "$project" >> "$C1192_ROOT/teardowns"
                if [[ "$project" == *main ]] && [ "$COMPOSE_CASE" = residual ]; then return 0; fi
                rm -rf -- "$C1192_ROOT/compose-networks/$project" "$C1192_ROOT/containers/${project}-probe-1"
                if [[ "$project" == *main ]]; then : > "$C1192_ROOT/main-down"; fi
            fi
            if [[ "$file" == *negative.yml ]]; then rm -rf -- "$C1192_ROOT/volumes/${project}_cache"; return 0; fi
            for key in packages scratch npm; do
                volume=$(retention_cache_name "$project" "$file" "$key")
                [[ "$volume" != "${project}_"* ]] || rm -rf -- "$C1192_ROOT/volumes/$volume"
            done
            for key in runner-tmp work runner-state dind-data; do
                if [ "$mode" = compose-absence ] && [[ "$project" == *main ]] && [ "$COMPOSE_CASE" = mixed ] && [ "$key" = work ]; then continue; fi
                rm -rf -- "$C1192_ROOT/volumes/${project}_$key"
            done
            ;;
        *) printf 'UNEXPECTED-COMPOSE %s\n' "$action" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
}

# CARD-1207 modeled project lists and post-teardown observations.
c1207_docker() {
    local kind="$1" name="${*: -1}" scoped=0 project='' path item
    if [ "$COMPOSE_CASE" = donor-unknown ] && { [ "$kind" = inspect ] || [ "$kind:${2:-}" = container:inspect ]; } \
        && { [ "$name" = "c849-${RUN}-donor" ] || [ "$name" = "${COMPLETION_DONOR_ID:-}" ]; }; then
        printf 'Cannot connect to the Docker daemon\n' >&2; return 1
    fi
    local token
    for token in "$@"; do
        case "$token" in label=com.docker.compose.project=*) scoped=1; project="${token#label=com.docker.compose.project=}" ;; esac
    done
    case "$kind:${2:-}" in
        volume:ls|network:ls|ps:*)
            if [ "$COMPOSE_CASE" = recording ] && [ "$scoped" = 1 ] && [[ "$project" == *main ]]; then printf 'Cannot connect to the Docker daemon\n' >&2; return 1; fi
            if [ -f "$C1192_ROOT/main-down" ] && [ "$COMPOSE_CASE" = daemon ]; then printf 'Cannot connect to the Docker daemon\n' >&2; return 1; fi
            if [ -f "$C1192_ROOT/main-down" ] && [ "$scoped" = 0 ] && [ "$COMPOSE_CASE" = mixed ] && [ "$kind" = volume ]; then return 1; fi
            case "$kind" in
                volume) for path in "$C1192_ROOT/volumes"/*; do
                    item="${path##*/}"; [ "$scoped" = 0 ] || [[ "$item" == "${project}_"* ]] || continue
                    printf '%s\n' "$item"
                done ;;
                network) for path in "$C1192_ROOT/compose-networks"/*; do
                    item="${path##*/}"; [ "$scoped" = 0 ] || [ "$item" = "$project" ] || continue
                    printf '%s_default\n' "$item"
                done ;;
                ps) for path in "$C1192_ROOT/containers"/*; do
                    if [ "$scoped" = 1 ]; then [ -f "$path/label-project" ] && [ "$(< "$path/label-project")" = "$project" ] || continue; fi
                    printf '%s\n' "${path##*/}"
                done ;;
            esac; return 0 ;;
        volume:inspect|container:inspect|network:inspect)
            if [[ "$name" == "c849${RUN}main"* ]] && [ -f "$C1192_ROOT/main-down" ]; then
                if [ "$COMPOSE_CASE" = daemon ] || { [ "$COMPOSE_CASE" = mixed ] && [ "$name" = "c849${RUN}main_work" ]; }; then
                    printf 'Cannot connect to the Docker daemon\n' >&2; return 1
                fi
            fi
            if [ "$kind" = network ]; then
                [ -d "$C1192_ROOT/compose-networks/${name%_default}" ] && return 0
                printf 'Error response from daemon: No such network: %s\n' "$name" >&2; return 1
            fi ;;
    esac
    return 98
}

mode_compose_absence() {
    reset_runtime
    COMPOSE_CASE="${3:?case}"
    mkdir -p "$C1192_ROOT/compose-networks"
    : > "$C1192_ROOT/teardowns"
    : > "$C1192_ROOT/compose-actions"
    S7_SCENARIO=compose
    s7_groups
    source <(awk '/^(c849_fixture_retention|c849_fixture_evidence|write_result|json_escape|scrub_file)\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote")
    case_runner_cache_inventory() { printf '{}\n' > "$CASE_DIR/server2-status.json"; printf '{}\n' > "$CASE_DIR/server2-temp-status.json"; }
    c849_fixture_evidence_payloads() { :; }
    source <(declare -f c849_fixture_seed | sed '1s/c849_fixture_seed/c1207_seed/')
    c849_fixture_seed() {
        c1207_seed
        if [[ "$COMPOSE_CASE" == donor* ]]; then
            local donor="c849-${RUN}-donor"
            plant_container "$donor" c849-fixture "$RUN" donor "$ADMITTED" ""
            printf '%s\n' "c849${RUN}temp" > "$C1192_ROOT/containers/$donor/label-project"
            printf 'session-runner\n' > "$C1192_ROOT/containers/$donor/label-service"
            COMPLETION_DONOR_ID=$(cat "$C1192_ROOT/containers/$donor/id")
            printf '%s\n' "$COMPLETION_DONOR_ID" > "$CASE_DIR/.fixture-donor"
            case "$COMPOSE_CASE" in
                donor-foreign) printf foreign > "$C1192_ROOT/containers/$donor/label-owner" ;;
                donor-replaced) printf '%064d\n' 7 > "$C1192_ROOT/containers/$donor/id" ;;
            esac
        fi
        C849_PACKAGES="c849${RUN}smoke-packages"; C849_SCRATCH="c849${RUN}smoke-scratch"; C849_NPM="c849${RUN}npmcache-npm"
        local volume
        for volume in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
            plant_volume "$volume" local null server2-runner "$RUN" cache
            chmod 700 "$C1192_ROOT/volumes/$volume/data"
        done
        s7_roster
        sed -i '$d; /^PASS F-7$/d' "$CASE_DIR/fixture-groups.txt"
        sed -i '/^PASS PC-20$/d' "$CASE_DIR/fixture-controls.txt"
        sed -i '/^PC-20[.]01 /d' "$CASE_DIR/fixture-control-variants.txt"
    }
    local code=0 key name good expected
    set +e
    ( set -e; c849_fixture ) > "$C1192_ROOT/compose-output" 2>&1
    code=$?
    set -e
    cat "$C1192_ROOT/compose-output"
    cat "$CASE_DIR/c590-result.json"
    printf '\n'
    expected=false; { [ "$COMPOSE_CASE" != absent ] && [ "$COMPOSE_CASE" != donor ]; } || expected=true
    good=0; jq -e --argjson expected "$expected" '.accepted==$expected' "$CASE_DIR/c590-result.json" >/dev/null && good=1
    emit verdict "$good"
    good=0
    if [ "$expected" = true ]; then [ "$code" = 0 ] && grep -q '^C849_FIXTURE ' "$C1192_ROOT/compose-output" && good=1
    else [ "$code" != 0 ] && ! grep -q '^C849_FIXTURE ' "$C1192_ROOT/compose-output" && good=1; fi
    emit marker "$good"
    if [[ "$COMPOSE_CASE" == donor* ]]; then
        good=0; name="c849-${RUN}-donor"
        if [ "$COMPOSE_CASE" = donor ]; then
            if [ "$code" = 0 ] && ! container_exists "$name" && [ ! -s "$CASE_DIR/fixture-cleanup-residue.txt" ] \
                && grep -Fxq "confirmed-absent container $name" "$CASE_DIR/fixture-cleanup-ledger.txt"; then good=1; fi
        else
            if [ "$code" != 0 ] && container_exists "$name" && [ -s "$CASE_DIR/fixture-cleanup-residue.txt" ] \
                && ! grep -Fxq "container $name" "$C1192_ROOT/order"; then good=1; fi
        fi
        emit donor-checked "$good"
    fi
    if [ "$COMPOSE_CASE" = recording ]; then
        good=0; ! grep -Fxq "c849${RUN}main" "$C1192_ROOT/teardowns" && good=1; emit no-unrecorded-down "$good"
        good=0; grep -Fxq "unknown-cleanup-outcome compose c849${RUN}main" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1; emit recording-ledger "$good"
    else
        for key in runner-tmp work runner-state dind-data; do
            name="c849${RUN}main_$key"; expected=confirmed-absent
            case "$COMPOSE_CASE:$key" in residual:*) expected=unknown-cleanup-outcome ;; daemon:*|mixed:work) expected=unknown-cleanup-outcome ;; esac
            good=0; grep -Fxq "$expected volume $name" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1; emit "ledger-$key" "$good"
            good=0
            if [ "$expected" = confirmed-absent ]; then ! grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
            else grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1; fi
            emit "residue-$key" "$good"
        done
        if [ "$COMPOSE_CASE" = residual ]; then
            for key in runner-tmp work runner-state dind-data; do good=0; volume_exists "c849${RUN}main_$key" && good=1; emit "retained-$key" "$good"; done
            good=0; container_exists "c849${RUN}main-probe-1" && grep -Fxq "unknown-cleanup-outcome container c849${RUN}main-probe-1" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1; emit retained-container "$good"
            good=0; [ -d "$C1192_ROOT/compose-networks/c849${RUN}main" ] && grep -Fxq "unknown-cleanup-outcome network c849${RUN}main_default" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1; emit retained-network "$good"
        fi
    fi
    refuse_unexpected
}

mode_retention_inspect() {
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    : > "$C1192_ROOT/compose-actions"
    C849_PACKAGES="c849${RUN}smoke-packages"; C849_SCRATCH="c849${RUN}smoke-scratch"; C849_NPM="c849${RUN}npmcache-npm"
    local volume code=0 out
    for volume in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        plant_volume "$volume" local null server2-runner "$RUN" cache
        chmod 700 "$C1192_ROOT/volumes/$volume/data"
    done
    RETENTION_INSPECT_FAULT=temp-private
    out=$(c849_fixture_retention 2>&1) || code=$?
    if [ "$code" != 2 ] || ! c1192_has_exact_line 'DIAGNOSIS=FixtureVolumeInspectFailed' "$out"; then
        fail "retention-inspect code=$code $out"
    fi
    if [ -f "$CASE_DIR/fixture-groups.txt" ] && grep -Fxq 'PASS F-7' "$CASE_DIR/fixture-groups.txt"; then
        fail retention-inspect-published-success
    fi
    if c1192_has_exact_line 'DIAGNOSIS=FixtureTempPrivateVolumeRetained' "$out"; then
        fail retention-inspect-absence-token
    fi
    printf 'PASS retention-inspect-refused\n'
}

mode_retention() {
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    : > "$C1192_ROOT/compose-actions"
    C849_PACKAGES="c849${RUN}smoke-packages"; C849_SCRATCH="c849${RUN}smoke-scratch"; C849_NPM="c849${RUN}npmcache-npm"
    local volume key code=0 out uid=0 nocopy=1 external=1 private=0 recreate=0 negative=0
    for volume in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        plant_volume "$volume" local null server2-runner "$RUN" cache
        chmod 700 "$C1192_ROOT/volumes/$volume/data"
    done
    out=$(c849_fixture_retention 2>&1) || code=$?
    [ "$code" = 0 ] || fail "retention code=$code $out"
    grep -q 'user: "1654:1654"' "$C1192_ROOT/c849${RUN}main.yml" && uid=1
    for volume in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        [ ! -e "$C1192_ROOT/volumes/$volume/data/image-content" ] && [ "$(stat -c %a "$C1192_ROOT/volumes/$volume/data")" = 700 ] || nocopy=0
        [ -f "$C1192_ROOT/volumes/$volume/data/retention-sentinel" ] && [ "$(cat "$C1192_ROOT/volumes/$volume/data/retention-sentinel")" = retained ] || external=0
    done
    [ -f "$C1192_ROOT/retention-before-main-down" ] && private=1
    [ -f "$C1192_ROOT/recreate-retained" ] && recreate=1
    ! volume_exists "c849${RUN}negative_cache" && grep -q '^PC-20.01 ' "$CASE_DIR/fixture-control-variants.txt" && negative=1
    emit retention-app-uid "$uid"
    emit retention-nocopy-three "$nocopy"
    emit retention-external-survives "$external"
    emit retention-private-separation "$private"
    emit retention-recreate-bytes "$recreate"
    emit retention-negative-lost "$negative"
    refuse_unexpected
}

# S7 exercises the actual fixture wrapper/EXIT/finalizer; group substitutes only
# provide controller inputs. Payload/export and roster validators remain real.
s7_groups() {
    c849_fixture_compose() { :; }
    c849_fixture_prepare() { :; }
    c849_fixture_seed() {
        printf '%s\n' "$SERVER2_ROOT" > "$C1192_ROOT/controller-used"
        mkdir -p "$SERVER2_ROOT/cache" "$SERVER2_ROOT/compose-retention"
        printf 'services: {}\n' > "$SERVER2_ROOT/compose-retention/shared.yml"
        local name
        # Log bytes/order/export failure belong to these four S7 scenarios.
        # Cleanup outcome matrices do not assert diagnostics; avoid five extra
        # atomic publications per outcome while retaining every outcome/receipt.
        if [ "$mode" != cleanup-probe ]; then
            case "$S7_SCENARIO" in success|primary|export|foreign)
                for name in race-p1.log race-p2.log race-offline.log lock-holder.log lock-waiter.log; do
                    printf '%s-body\n' "$name" > "$SERVER2_ROOT/$name"
                done ;;
            esac
        fi
        c849_fixture_volume_write /fixture/probes/payload-canary <<'DATA'
PAYLOAD_CANARY_S7
DATA
        ln -s "$CASE_DIR" "$CASE_DIR/.fixture-escape"
        stat -c '%d:%i:%u' -- "$CASE_DIR/.fixture-escape" > "$CASE_DIR/.fixture-escape-identity"
        case "$S7_SCENARIO" in
            primary) write_result false OriginalFailure 9 ;;
            unhandled) exit 9 ;;
            early) WROTE=1 ;;
            early-exit) write_result true '' 0 ;;
            helper-foreign) printf foreign > "$C1192_ROOT/containers/$WRITER/label-owner" ;;
            primary-delete) C1192_CUT=rm-work; write_result false OriginalFailure 9 ;;
            delete) C1192_CUT=rm-work ;;
            down) C1192_FAULT=volume-inspect-down ;;
            export) mv() { case "${*: -1}" in race-p1.log|race-p2.log|race-offline.log|lock-holder.log|lock-waiter.log) return 1 ;; esac; command mv "$@"; } ;;
            foreign) mv "$SERVER2_ROOT" "$CASE_DIR/owned-away"; mkdir -m 700 "$SERVER2_ROOT"; printf 'FOREIGN\n' > "$SERVER2_ROOT/foreign"; printf 'FOREIGN_DIAGNOSTIC\n' > "$SERVER2_ROOT/race-p1.log" ;;
            foreign-consumer) plant_container "c849-${RUN}-donor" intruder "other-run" donor "$ADMITTED" "" ;;
        esac
    }
    c849_fixture_nuget_race() { :; }
    c849_fixture_apphost() { :; }
    c849_fixture_npm() { :; }
    c849_fixture_retention() { :; }
    c849_fixture_prune() { :; }
    c849_fixture_contract_controls() { :; }
    c849_fixture_evidence() { :; }
    write_result() {
        local accepted="$1" diagnosis="$2" code="$3"
        if [ "$WROTE" = 1 ]; then exit "$code"; fi
        WROTE=1
        printf '%s %s %s\n' "$accepted" "$diagnosis" "$code" > "$CASE_DIR/result"
        order_log "result $accepted $diagnosis $code"
        printf 'DIAGNOSIS=%s\n' "$diagnosis"
        exit "$code"
    }
}

s7_run() {
    reset_runtime
    S7_SCENARIO=$1
    printf 'HOST_DECOY\n' > "$C1192_ROOT/host-decoy"
    s7_groups
    mv() { case "${*: -1}" in race-p1.log|race-p2.log|race-offline.log|lock-holder.log|lock-waiter.log) order_log export-log ;; esac; command mv "$@"; }
    S7_CODE=0
    S7_OUT=$(c849_fixture 2>>"$C1192_ROOT/scenario.err") || S7_CODE=$?
    S7_CONTROL=$(cat "$C1192_ROOT/controller-used")
}

s7_log_bytes() {
    local name
    for name in race-p1.log race-p2.log race-offline.log lock-holder.log lock-waiter.log; do
        [ -f "$CASE_DIR/$name" ] && [ ! -L "$CASE_DIR/$name" ] && grep -Fxq "$name-body" "$CASE_DIR/$name" || return 1
    done
}

mode_finalization() {
    local good=0 key
    s7_run success
    [ ! -e "$S7_CONTROL" ] && good=1; emit success-control-removed "$good"
    good=0; [ "$S7_CONTROL" = "$CASE_DIR/fixture-controller" ] && good=1; emit success-control-root "$good"
    good=0; ! grep -q '^SUDO ' "$C1192_ROOT/order" && good=1; emit success-no-sudo "$good"
    good=0; [ ! -d "$C1192_ROOT/volumes/$WORK" ] && [ ! -d "$C1192_ROOT/volumes/$MASK" ] && [ ! -d "$C1192_ROOT/containers/$WRITER" ] && [ ! -d "$C1192_ROOT/containers/$READER" ] && good=1; emit success-resources-removed "$good"
    good=0; grep -Fxq HOST_DECOY "$C1192_ROOT/host-decoy" && good=1; emit success-host-decoy "$good"
    good=0; grep -q 'payload-canary' "$C1192_ROOT/trace" && good=1; emit success-volume-payload "$good"
    good=0; [ "$S7_CODE" = 0 ] && [ "$(tail -n1 "$C1192_ROOT/order")" = 'result true  0' ] && good=1; emit success-last "$good"
    good=0; if declare -F c849_fixture_finalize >/dev/null; then ( c849_fixture_finalize ) && good=1; fi; emit success-repeat "$good"
    good=0; grep -q '^compose-input-present$' "$C1192_ROOT/order" && good=1; emit compose-before-removal "$good"
    good=0; s7_log_bytes && good=1; emit success-five-log-bytes "$good"
    good=0; [ "$(line_no export-log)" -lt "$(line_no "container $WRITER")" ] && good=1; emit export-before-helper "$good"
    good=0; [ "$(line_no "container $READER")" -lt "$(line_no "volume $WORK")" ] && good=1; emit helper-before-volume "$good"
    for key in primary unhandled delete export early early-exit helper-foreign primary-delete down foreign foreign-consumer; do
        s7_run "$key"
        good=0
        case "$key" in
            primary) [ "$S7_CODE" = 9 ] && grep -Fxq 'false OriginalFailure 9' "$CASE_DIR/result" && good=1; emit primary-preserved "$good"
                good=0; [ ! -L "$CASE_DIR/.fixture-escape" ] && good=1; emit primary-link-removed "$good"
                good=0; s7_log_bytes && good=1; emit primary-five-log-bytes "$good" ;;
            unhandled) [ "$S7_CODE" = 9 ] && grep -Fxq 'false UnhandledExit 9 9' "$CASE_DIR/result" && good=1; emit unhandled-preserved "$good" ;;
            delete)
                [ "$S7_CODE" = 2 ] && grep -Fxq 'false FixtureFinalizationFailed 2' "$CASE_DIR/result" && good=1; emit delete-refuses-success "$good"
                good=0; grep -Fxq "$WORK" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1; emit delete-exact-residue "$good"
                good=0; ! volume_exists "$MASK" && good=1; emit delete-continues "$good" ;;
            export) [ "$S7_CODE" = 2 ] && good=1; emit export-refuses-success "$good" ;;
            early-exit) [ "$S7_CODE" = 2 ] && grep -Fxq 'false FixtureEarlyReceipt 2' "$CASE_DIR/result" && good=1; emit early-exit-refused "$good" ;;
            helper-foreign)
                [ "$S7_CODE" = 2 ] && container_exists "$WRITER" && grep -Fxq "$WRITER" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
                emit foreign-helper-residue "$good" ;;
            primary-delete)
                [ "$S7_CODE" = 9 ] && grep -Fxq 'false OriginalFailure 9' "$CASE_DIR/result" && grep -Fxq "$WORK" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
                emit primary-secondary-preserved "$good" ;;
            early) [ "$S7_CODE" = 2 ] && grep -q '^false ' "$CASE_DIR/result" && good=1; emit early-success-refused "$good" ;;
            down) [ "$S7_CODE" = 2 ] && [ -s "$CASE_DIR/fixture-cleanup-residue.txt" ] && good=1; emit docker-unavailable-refused "$good" ;;
            foreign) [ "$S7_CODE" = 2 ] && grep -Fxq FOREIGN "$S7_CONTROL/foreign" && ! grep -Fq FOREIGN_DIAGNOSTIC "$CASE_DIR/race-p1.log" && good=1; emit foreign-controller-preserved "$good" ;;
            foreign-consumer) container_exists "c849-${RUN}-donor" && good=1; emit unregistered-consumer-preserved "$good" ;;
        esac
    done
}

s7_roster() {
    local family count ordinal
    seq 1 9 | sed 's/^/PASS F-/' > "$CASE_DIR/fixture-groups.txt"
    : > "$CASE_DIR/fixture-controls.txt"; : > "$CASE_DIR/fixture-control-variants.txt"
    for family in $(seq -w 1 32); do
        printf 'PASS PC-%s\n' "$family" >> "$CASE_DIR/fixture-controls.txt"
        count=1; case "$family" in 13|14|29|30) count=2 ;; 15|18|27) count=3 ;; 28) count=4 ;; 31) count=16 ;; esac
        for ordinal in $(seq 1 "$count"); do printf 'PC-%s.%02d expected-red\n' "$family" "$ordinal" >> "$CASE_DIR/fixture-control-variants.txt"; done
    done
}

mode_evidence() {
    reset_runtime; require_init
    SERVER2_ROOT="$CASE_DIR/fixture-controller"
    c849_fixture_evidence_payloads >/dev/null
    s7_roster
    local good=0 fault code=0 out
    [ "$(wc -l < "$CASE_DIR/fixture-summary.txt")" = 9 ] && good=1; emit summary-nine-lines "$good"
    printf 'source-sha=%s\nimage-id=%s\ninventories=2\ngroups=9\ncontrols=32\nexpected-red=32\nvariants=60\nexpected-red-variants=60\nproduction-mutations=0\n' "$SHA" "$IMAGE_ID" > "$C1192_ROOT/expected-summary"
    good=0; cmp -s "$C1192_ROOT/expected-summary" "$CASE_DIR/fixture-summary.txt" && good=1; emit summary-exact-bytes "$good"
    good=0; ( c849_fixture_roster ) && good=1; emit roster-complete "$good"
    for fault in missing duplicate family variant family-duplicate variant-duplicate; do
        s7_roster
        case "$fault" in
            missing) sed -i '$d' "$CASE_DIR/fixture-groups.txt" ;;
            duplicate) printf 'PASS F-9\n' >> "$CASE_DIR/fixture-groups.txt" ;;
            family) sed -i '$d' "$CASE_DIR/fixture-controls.txt" ;;
            variant) sed -i '$d' "$CASE_DIR/fixture-control-variants.txt" ;;
            family-duplicate) printf 'PASS PC-32\n' >> "$CASE_DIR/fixture-controls.txt" ;;
            variant-duplicate) printf 'PC-32.01 expected-red\n' >> "$CASE_DIR/fixture-control-variants.txt" ;;
        esac
        code=0; out=$(c849_fixture_roster) || code=$?
        good=0; [ "$code" = 2 ] && [ "$(diagnosis_of "$out")" = FixtureRosterIncomplete ] && good=1
        emit "roster-rejects-$fault" "$good"
    done
    good=0; ! grep -RqE 'TOKEN_SENTINEL_C849_CONTENT|PAYLOAD_CANARY_S7' "$CASE_DIR" && good=1; emit evidence-no-payload "$good"
    good=0; if declare -F c849_fixture_finalize >/dev/null; then ( c849_fixture_finalize ) && [ ! -d "$SERVER2_ROOT" ] && good=1; fi; emit evidence-control-removed "$good"
}

# Only work/mask Docker inspect is faulted; acquisition, EXIT, finalization and the JSON
# recipient are production functions. A one-probe outage tests that later
# successful cleanup does not erase the acquisition's unknown record.
mode_acquisition_probe() {
    local role outcome good key code
    for role in work mask; do
        ACQUIRE_NAME="c849${RUN}-$role"
        for outcome in daemon label-once; do
            reset_runtime
            rm -f "$C1192_ROOT/acquire-probed" "$CASE_DIR/c590-result.json" "$CASE_DIR/fixture-cleanup-ledger.txt"
            ACQUIRE_OUTCOME=$outcome
            S7_SCENARIO=success
            s7_groups
            source <(awk '/^(write_result|json_escape|scrub_file)\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote")
            set +e
            ( set -e; c849_fixture ) > "$C1192_ROOT/acquire-output" 2>&1
            code=$?
            set -e
            key="$role-$outcome"
            printf '%s exit=%s receipt=' "$key" "$code"; cat "$CASE_DIR/c590-result.json"; printf '\n'
            good=0; [ "$code" != 0 ] && jq -e '.accepted==false and .exit!=0' "$CASE_DIR/c590-result.json" >/dev/null \
                && ! grep -qE 'accepted[" ]*:true|result true|C849_FIXTURE ' "$C1192_ROOT/acquire-output" && good=1
            emit "$key-verdict" "$good"
            good=0; grep -Fxq "$ACQUIRE_NAME" "$CASE_DIR/.fixture-volumes" && good=1
            emit "$key-registered" "$good"
            good=0; grep -Fxq "$ACQUIRE_NAME" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
            emit "$key-residue" "$good"
            good=0; grep -Fxq "unknown-cleanup-outcome volume $ACQUIRE_NAME" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1
            emit "$key-ledger" "$good"
            good=0; grep -Fq volume-create "$C1192_ROOT/events" && [ "$(cat "$C1192_ROOT/intent-$ACQUIRE_NAME")" = 1 ] && good=1
            emit "$key-create-registered" "$good"
            if [ "$outcome" = daemon ]; then
                good=0; volume_exists "$ACQUIRE_NAME" && good=1
                emit "$key-retained" "$good"
            fi
        done
    done
}

mode_cleanup_probe() {
    PROBE_BOUNDARY="${3:?boundary}"
    local outcome good expected unknown retained name
    case "$PROBE_BOUNDARY" in
        all) PROBE_KIND=container; PROBE_NAME="$WRITER" ;;
        consumer) PROBE_KIND=container; PROBE_NAME="c849-${RUN}-restore-p1" ;;
        helper) PROBE_KIND=container; PROBE_NAME="$WRITER" ;;
        masked) PROBE_KIND=container; PROBE_NAME="c849-${RUN}-masked" ;;
        work) PROBE_KIND=volume; PROBE_NAME="$WORK" ;;
        mask) PROBE_KIND=volume; PROBE_NAME="$MASK" ;;
        cache) PROBE_KIND=volume; PROBE_NAME="c849${RUN}-packages" ;;
        network) PROBE_KIND=network; PROBE_NAME="c849-${RUN}-npm" ;;
        *) fail "unknown boundary $PROBE_BOUNDARY" ;;
    esac
    local outcomes='absent daemon exit2 present timeout empty permission socket unrecognized rm-lie exit2-absent other-name other-kind mixed'
    [ "$PROBE_BOUNDARY" != all ] || outcomes=daemon
    # The remote function text does not change between outcomes. Reading it once
    # keeps the fourteen fixture runs from scanning c590-remote.sh on every pass
    # (that scan is a /mnt read when Windows drives this bash through WSL).
    S7_SCENARIO=success
    s7_groups
    # Exercise the real evidence marker/census path. Only its external
    # inventory and payload producers are substituted for this cleanup proof.
    source <(awk '/^c849_fixture_evidence\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote")
    case_runner_cache_inventory() {
        printf '{}\n' > "$CASE_DIR/server2-status.json"
        printf '{}\n' > "$CASE_DIR/server2-temp-status.json"
    }
    c849_fixture_evidence_payloads() { :; }
    # Execute the real receipt writer, not the older S7 text substitute.
    source <(awk '/^(write_result|json_escape|scrub_file)\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote")
    source <(declare -f c849_fixture_seed | sed '1s/c849_fixture_seed/c1192_probe_seed/')
    c849_fixture_seed() {
        c1192_probe_seed
        printf 'services: {}\n' > "$SERVER2_ROOT/compose-retention/negative.yml"
        plant_container "c849-${RUN}-restore-p1" c849-fixture "$RUN" consumer "$ADMITTED" ""
        printf '%s\n' "$(cat "$C1192_ROOT/containers/c849-${RUN}-restore-p1/id")" > "$CASE_DIR/.fixture-id-c849-${RUN}-restore-p1"
        plant_container "c849-${RUN}-masked" c849-fixture "$RUN" masked "$ADMITTED" ""
        printf '%s\n' "c849-${RUN}-masked" >> "$CASE_DIR/.fixture-helpers"
        plant_volume "c849${RUN}-packages" local null server2-runner "$RUN" nuget-packages
        printf '1\n' > "$C1192_ROOT/volumes/c849${RUN}-packages/label-schema"
        printf 'nuget-packages\n' > "$C1192_ROOT/volumes/c849${RUN}-packages/label-cache-role"
        printf '%s\n' "c849${RUN}-packages" >> "$CASE_DIR/.fixture-volumes"
        printf '%s\n' "c849-${RUN}-npm" > "$CASE_DIR/.fixture-network"
        : > "$C1192_ROOT/network"
        s7_roster
        sed -i '$d' "$CASE_DIR/fixture-groups.txt"
        if [ "$PROBE_OUTCOME" = absent ]; then
            case "$PROBE_KIND" in
                volume) rm -rf "$C1192_ROOT/volumes/$PROBE_NAME" ;;
                container) rm -rf "$C1192_ROOT/containers/$PROBE_NAME" ;;
                network) rm -f "$C1192_ROOT/network" ;;
            esac
        fi
    }
    for outcome in $outcomes; do
        reset_runtime
        rm -f "$C1192_ROOT/teardown-done" "$CASE_DIR/.fixture-network" "$CASE_DIR/c590-result.json" "$CASE_DIR/fixture-cleanup-ledger.txt"
        PROBE_OUTCOME=$outcome
        # No conditional call: retain real set -e behavior in the wrapper/EXIT.
        set +e
        ( set -e; c849_fixture ) > "$C1192_ROOT/probe-output" 2>&1
        S7_CODE=$?
        set -e
        emit "$outcome-exit" "$S7_CODE"
        printf '%s receipt=' "$outcome"; cat "$CASE_DIR/c590-result.json"; printf '\n'
        good=0; [ "$(grep -c '^compose-down$' "$C1192_ROOT/order")" = 3 ] && good=1
        emit "$outcome-teardown" "$good"
        good=0
        if [ "$outcome" = absent ]; then
            [ "$S7_CODE" = 0 ] && jq -e '.accepted==true and .exit==0' "$CASE_DIR/c590-result.json" >/dev/null && good=1
        else
            [ "$S7_CODE" != 0 ] && jq -e '.accepted==false and .exit!=0' "$CASE_DIR/c590-result.json" >/dev/null \
                && ! grep -qE 'accepted[" ]*:true|result true|C849_FIXTURE ' "$C1192_ROOT/probe-output" && good=1
        fi
        emit "$outcome-verdict" "$good"
        good=0
        if [ "$outcome" = absent ]; then
            grep -q '^C849_FIXTURE ' "$C1192_ROOT/probe-output" && good=1
        else
            ! grep -q '^C849_FIXTURE ' "$C1192_ROOT/probe-output" && good=1
        fi
        emit "$outcome-marker" "$good"
        good=0
        if [ "$outcome" = absent ]; then
            [ ! -s "$CASE_DIR/fixture-cleanup-residue.txt" ] && good=1
        else
            grep -Fxq "$PROBE_NAME" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
        fi
        emit "$outcome-residue" "$good"
        if [ "$outcome" != absent ]; then
            good=0
            case "$PROBE_KIND" in
                volume) volume_exists "$PROBE_NAME" && good=1 ;;
                container) container_exists "$PROBE_NAME" && good=1 ;;
                network) [ -f "$C1192_ROOT/network" ] && good=1 ;;
            esac
            emit "$outcome-retained" "$good"
            case "$outcome" in
                present|rm-lie) ;;
                *) good=0; grep -Fxq "unknown-cleanup-outcome $PROBE_KIND $PROBE_NAME" "$CASE_DIR/fixture-cleanup-ledger.txt" 2>/dev/null && good=1
                    emit "$outcome-ledger" "$good" ;;
            esac
        fi
        if [ "$PROBE_BOUNDARY" = all ]; then
            for name in "$WORK" "$MASK" "$WRITER" "$READER"; do
                good=0; grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
                emit "daemon-residue-$name" "$good"
            done
            good=0; volume_exists "$WORK" && volume_exists "$MASK" && container_exists "$WRITER" && container_exists "$READER" && good=1
            emit daemon-all-resources-retained "$good"
        fi
    done
}


# CARD-1208: production release paths against exact modeled identities.
mode_deletion_audit() {
    reset_runtime
    local row="${3:?row}" name="c849-${RUN}-restore-p1" network="c849-${RUN}-npm" id good=0 code=0 out
    : > "$CASE_DIR/fixture-cleanup-ledger.txt"
    case "$row" in
        wrong-cid|changed-labels|changed-owner|changed-role|id-inspect-failure|wrong-helper-id|fingerprint-name|fingerprint-image|fingerprint-created)
            if [ "$row" = wrong-helper-id ]; then
                plant_container "$name" c849-fixture "$RUN" consumer "$ADMITTED" ""
                id=$(cat "$C1192_ROOT/containers/$name/id")
            else
                c849_fixture_launch create --name "$name" --entrypoint sleep "$ADMITTED" infinity >/dev/null
                id=$(cat "$CASE_DIR/.fixture-id-$name")
            fi
            local original="$name" foreign="c849-${RUN}-foreign"
            if [ "$row" = wrong-cid ] || [ "$row" = wrong-helper-id ]; then
                plant_container "$foreign" foreign other-run consumer "$ADMITTED" ""
                id=$(cat "$C1192_ROOT/containers/$foreign/id")
                if [ "$row" = wrong-helper-id ]; then
                    name="$WRITER"
                    printf '%s\n' "$name" > "$CASE_DIR/.fixture-helpers"
                fi
                printf '%s\n' "$id" > "$CASE_DIR/.fixture-id-$name"
            elif [ "$row" = changed-labels ]; then
                printf other-run > "$C1192_ROOT/containers/$name/label-run"
            elif [ "$row" = changed-owner ]; then
                printf foreign > "$C1192_ROOT/containers/$name/label-owner"
            elif [ "$row" = changed-role ]; then
                printf donor > "$C1192_ROOT/containers/$name/label-role"
            elif [[ "$row" = fingerprint-* ]]; then
                printf '/%s|%s|2026-10-13T00:00:00Z\n' "$name" "$ADMITTED" > "$CASE_DIR/.fixture-fingerprint-$name"
                case "$row" in
                    fingerprint-name) printf '/other-acquisition\n' > "$C1192_ROOT/containers/$name/name" ;;
                    fingerprint-image) printf 'sha256:other-image\n' > "$C1192_ROOT/containers/$name/image" ;;
                    fingerprint-created) printf '2026-10-13T01:00:00Z\n' > "$C1192_ROOT/containers/$name/created" ;;
                esac
            else
                eval "$(declare -f docker_inspect_container | sed '1s/docker_inspect_container/c1192_inspect_before_outage/')"
                docker_inspect_container() {
                    if [[ "$*" == *io.antiphon.owner* ]]; then printf 'Cannot connect to the Docker daemon\n' >&2; return 1; fi
                    c1192_inspect_before_outage "$@"
                }
            fi
            c849_fixture_finalize || code=$?
            emit finalization-exit "$code"
            emit owned-survived "$(container_exists "$original" && echo 1 || echo 0)"
            if [ "$row" = wrong-cid ] || [ "$row" = wrong-helper-id ]; then
                emit foreign-survived "$(container_exists "$foreign" && echo 1 || echo 0)"
            fi
            emit deletion-count "$(grep -c '^container ' "$C1192_ROOT/order" || true)"
            emit residue "$(test -s "$CASE_DIR/fixture-cleanup-residue.txt" && echo 1 || echo 0)"
            ;;
        wrong-network-id|network-id-inspect-failure)
            id=ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
            printf '%s\n' "$id" > "$CASE_DIR/.fixture-network"
            : > "$C1192_ROOT/network"
            docker() {
                record_argv "$@"
                case "$1:$2" in
                    network:inspect)
                        if [ "$row" = network-id-inspect-failure ] && [[ "$*" == *io.antiphon.owner* ]]; then
                            printf 'Cannot connect to the Docker daemon\n' >&2; return 1
                        fi
                        [ -f "$C1192_ROOT/network" ] || { printf 'Error: No such network: %s\n' "${*: -1}" >&2; return 1; }
                        [ "${3:-}" = -f ] || return 0
                        case "$4" in
                            *Id*) printf '%s\n' "$id" ;;
                            *io.antiphon.owner*) printf 'foreign\n' ;;
                            *fixture-run*) if [ "$row" = network-id-inspect-failure ]; then printf '%s\n' "$RUN"; else printf 'other-run\n'; fi ;;
                            *fixture-role*) printf 'npm-network\n' ;;
                            *) return 97 ;;
                        esac ;;
                    network:rm) order_log "network ${*: -1}"; rm -f "$C1192_ROOT/network" ;;
                    *) return 97 ;;
                esac
            }
            c849_fixture_finalize || code=$?
            emit finalization-exit "$code"
            emit network-survived "$(test -f "$C1192_ROOT/network" && echo 1 || echo 0)"
            emit deletion-count "$(grep -c '^network ' "$C1192_ROOT/order" || true)"
            emit residue "$(test -s "$CASE_DIR/fixture-cleanup-residue.txt" && echo 1 || echo 0)"
            ;;
        owned-volume-controls)
            source <(awk '/^c849_fixture_cleanup_scope\(\) \(/{active=1} active{print} active && /^\)$/{active=0}' "$remote")
            C849_PACKAGES="c849${RUN}-packages"; C849_SCRATCH="c849${RUN}-scratch"; C849_NPM="c849${RUN}-npm"
            name="c849${RUN}-packages"
            printf '%s\n' "$name" > "$CASE_DIR/.fixture-volumes"
            c849_volume "$name" nuget-packages yes "$ADMITTED" >/dev/null
            # Preserve the model as the native Docker boundary used by the real control.
            eval "$(declare -f docker | sed '1s/docker/c1192_native_docker/')"
            command() {
                if [ "$1" = docker ]; then shift; c1192_native_docker "$@";
                else builtin command "$@"; fi
            }
            c849_fixture_control PC-09 CacheVolumeForeign c849_fixture_volume_fault label "$name" nuget-packages "$ADMITTED"
            c849_fixture_control PC-10 CacheVolumeForeign c849_fixture_volume_fault options "$name" nuget-packages "$ADMITTED"
            printf 'pre-existing-uncertainty\n' > "$CASE_DIR/fixture-cleanup-residue.txt"
            c849_fixture_control PC-32 FixtureCleanupScopeHeld c849_fixture_cleanup_scope
            emit residue-after-scope-control "$(grep -Fxq pre-existing-uncertainty "$CASE_DIR/fixture-cleanup-residue.txt" && echo 1 || echo 0)"
            rm -f "$CASE_DIR/fixture-cleanup-residue.txt"
            emit registered-after-controls "$(grep -Fxq "$name" "$CASE_DIR/.fixture-volumes" && echo 1 || echo 0)"
            c849_fixture_finalize || code=$?
            emit finalization-exit "$code"
            emit volume-survived "$(volume_exists "$name" && echo 1 || echo 0)"
            emit removal-recorded "$(grep -Fxq "volume $name" "$C1192_ROOT/order" && echo 1 || echo 0)"
            ;;
        dangling-network-record|invalid-network-record|absent-network-record)
            case "$row" in
                dangling-network-record) ln -s "$CASE_DIR/no-network-record" "$CASE_DIR/.fixture-network" ;;
                invalid-network-record) printf 'invalid-network-id\n' > "$CASE_DIR/.fixture-network" ;;
                absent-network-record) printf 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n' > "$CASE_DIR/.fixture-network" ;;
            esac
            rm -f "$C1192_ROOT/network"
            c849_fixture_finalize || code=$?
            emit finalization-exit "$code"
            emit record-retained "$(if [ -e "$CASE_DIR/.fixture-network" ] || [ -L "$CASE_DIR/.fixture-network" ]; then echo 1; else echo 0; fi)"
            emit residue "$(test -s "$CASE_DIR/fixture-cleanup-residue.txt" && echo 1 || echo 0)"
            emit absence-receipt "$(grep -q '^confirmed-absent network ' "$CASE_DIR/fixture-cleanup-ledger.txt" && echo 1 || echo 0)"
            if [ "$row" = invalid-network-record ]; then
                emit record-text-residue "$(grep -Fq invalid-network-id "$CASE_DIR/fixture-cleanup-residue.txt" && echo 1 || echo 0)"
            fi
            ;;
        foreign-cache)
            name="c849${RUN}-packages"
            C849_PACKAGES="$name"; C849_SCRATCH="c849${RUN}-scratch"; C849_NPM="c849${RUN}-npm"
            printf '%s\n' "$name" > "$CASE_DIR/.fixture-volumes"
            plant_volume "$name" local null foreign other-run packages
            out=$(c849_volume "$name" nuget-packages yes "$ADMITTED" 2>&1) || code=$?
            c849_fixture_finalize || true
            [ "$code" = 2 ] && [ "$(diagnosis_of "$out")" = CacheVolumeForeign ] && volume_exists "$name" \
                && ! grep -Fxq "volume $name" "$C1192_ROOT/order" \
                && ! grep -Fxq "$name" "$CASE_DIR/.fixture-volumes" \
                && grep -Fq "foreign: left untouched volume $name" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1
            ;;
        replacement-container)
            local fault all=1
            for fault in owner run role; do
                reset_runtime
                code=0; good=0
                name="c849-${RUN}-donor"
                printf '%s\n' "$name" > "$CASE_DIR/.fixture-donor"
                plant_container "$name" c849-fixture "$RUN" donor "$ADMITTED" ""
                case "$fault" in
                    owner) printf foreign > "$C1192_ROOT/containers/$name/label-owner" ;;
                    run) printf other-run > "$C1192_ROOT/containers/$name/label-run" ;;
                    role) printf consumer > "$C1192_ROOT/containers/$name/label-role" ;;
                esac
                c849_fixture_finalize || code=$?
                container_exists "$name" && ! grep -q '^container ' "$C1192_ROOT/order" \
                    && [ "$code" = 2 ] && grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" \
                    && [ ! -e "$CASE_DIR/.fixture-donor" ] && good=1
                printf 'replacement-%s=%s\n' "$fault" "$good"
                [ "$good" = 1 ] || all=0
            done
            good=$all
            ;;
        legacy-owned|unknown)
            name="c849-${RUN}-donor"
            printf '%s\n' "$name" > "$CASE_DIR/.fixture-donor"
            plant_container "$name" c849-fixture "$RUN" donor "$ADMITTED" ""
            if [ "$row" = unknown ]; then
                declare -f docker_inspect_container > "$C1192_ROOT/inspect-original"
                docker_inspect_container() { printf 'Cannot connect to the Docker daemon\n' >&2; return 1; }
            fi
            id=$(cat "$C1192_ROOT/containers/$name/id")
            c849_fixture_finalize || code=$?
            if [ "$row" = legacy-owned ]; then
                ! container_exists "$name" && grep -Fxq "container $id" "$C1192_ROOT/order" && good=1
                printf '%s\n' "$network" > "$CASE_DIR/.fixture-network"
                : > "$C1192_ROOT/network"
                c849_fixture_network_release
                [ ! -f "$C1192_ROOT/network" ] && [ ! -e "$CASE_DIR/.fixture-network" ] \
                    && grep -Fxq "confirmed-absent network eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" "$CASE_DIR/fixture-cleanup-ledger.txt" \
                    && grep -Fxq 'network eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' "$C1192_ROOT/order" || good=0
            else
                container_exists "$name" && ! grep -q '^container ' "$C1192_ROOT/order" \
                    && [ "$code" = 2 ] && grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
            fi
            if [ "$row" = unknown ]; then
                source "$C1192_ROOT/inspect-original"
                local invalid all="$good"
                for invalid in empty malformed symlink; do
                    reset_runtime
                    code=0; good=0
                    plant_container "$name" c849-fixture "$RUN" donor "$ADMITTED" ""
                    printf '%s\n' "$name" > "$CASE_DIR/.fixture-donor"
                    case "$invalid" in
                        empty) : > "$CASE_DIR/.fixture-id-$name" ;;
                        malformed) printf invalid > "$CASE_DIR/.fixture-id-$name" ;;
                        symlink) ln -s "$C1192_ROOT/events" "$CASE_DIR/.fixture-id-$name" ;;
                    esac
                    c849_fixture_finalize || code=$?
                    container_exists "$name" && [ "$code" = 2 ] \
                        && ! grep -q '^container ' "$C1192_ROOT/order" \
                        && grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" \
                        && grep -Fxq "unknown-cleanup-outcome container $name" "$CASE_DIR/fixture-cleanup-ledger.txt" && good=1
                    printf 'invalid-%s=%s\n' "$invalid" "$good"
                    [ "$good" = 1 ] || all=0
                done
                good=$all
                all="$good"
                for invalid in symlink directory; do
                    reset_runtime
                    code=0; good=0
                    name="c849-seed-$RUN"
                    plant_container "$name" c849-fixture "$RUN" consumer "$ADMITTED" ""
                    case "$invalid" in
                        symlink) ln -s "$C1192_ROOT/events" "$CASE_DIR/.fixture-id-$name" ;;
                        directory) mkdir "$CASE_DIR/.fixture-id-$name" ;;
                    esac
                    c849_fixture_finalize || code=$?
                    container_exists "$name" && [ "$code" = 2 ] \
                        && ! grep -q '^container ' "$C1192_ROOT/order" \
                        && grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt" && good=1
                    printf 'standalone-%s=%s\n' "$invalid" "$good"
                    [ "$good" = 1 ] || all=0
                done
                good=$all
            fi
            ;;
        replacement-network)
            local fault all=1 fixture_owner fixture_run fixture_role
            for fault in owner run role; do
            reset_runtime
            code=0; good=0
            fixture_owner=c849-fixture; fixture_run="$RUN"; fixture_role=npm-network
            case "$fault" in owner) fixture_owner=foreign ;; run) fixture_run=other-run ;; role) fixture_role=consumer ;; esac
            printf '%s\n' "$network" > "$CASE_DIR/.fixture-network"
            : > "$C1192_ROOT/network"
            docker() {
                record_argv "$@"
                case "$1:$2" in
                    network:inspect)
                        [ -f "$C1192_ROOT/network" ] || { printf 'Error: No such network: %s\n' "${*: -1}" >&2; return 1; }
                        if [ "${3:-}" = -f ]; then
                            case "$4" in
                                *Id*) printf 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n' ;;
                                *io.antiphon.owner*) printf '%s\n' "$fixture_owner" ;;
                                *fixture-run*) printf '%s\n' "$fixture_run" ;;
                                *fixture-role*) printf '%s\n' "$fixture_role" ;;
                                *) return 97 ;;
                            esac
                        fi ;;
                    network:rm) order_log "network ${*: -1}"; rm -f "$C1192_ROOT/network" ;;
                    *) return 97 ;;
                esac
            }
            c849_fixture_finalize || code=$?
            [ -f "$C1192_ROOT/network" ] && ! grep -q '^network ' "$C1192_ROOT/order" \
                && [ "$code" = 2 ] && [ -s "$CASE_DIR/fixture-cleanup-residue.txt" ] \
                && [ ! -e "$CASE_DIR/.fixture-network" ] && good=1
            printf 'network-%s=%s\n' "$fault" "$good"
            [ "$good" = 1 ] || all=0
            done
            good=$all
            ;;
        unacquired)
            ln -s "$C1192_ROOT/events" "$CASE_DIR/.fixture-escape"
            plant_container "$name" c849-fixture "$RUN" consumer "$ADMITTED" ""
            plant_container "c849-${RUN}-masked" c849-fixture "$RUN" masked "$ADMITTED" ""
            c849_fixture_finalize || code=$?
            container_exists "$name" && container_exists "c849-${RUN}-masked" \
                && [ -L "$CASE_DIR/.fixture-escape" ] && [ "$code" = 2 ] \
                && grep -Fxq escape-link "$CASE_DIR/fixture-cleanup-residue.txt" \
                && ! grep -q '^container ' "$C1192_ROOT/order" && good=1
            ;;
        recorded-id)
            name="c849-${RUN}-donor"
            plant_container "$name" c849-fixture "$RUN" donor "$ADMITTED" ""
            id=$(cat "$C1192_ROOT/containers/$name/id")
            printf '%s\n' "$id" > "$CASE_DIR/.fixture-donor"
            mv "$C1192_ROOT/containers/$name" "$C1192_ROOT/containers/acquired-away"
            plant_container "$name" foreign other-run donor "$ADMITTED" ""
            c849_fixture_finalize || code=$?
            container_exists "$name" && ! container_exists acquired-away \
                && grep -Fxq "container $id" "$C1192_ROOT/order" && ! grep -Fxq "container $name" "$C1192_ROOT/order" && good=1
            ;;
        acquired)
            c849_fixture_workspace_init
            C849_PACKAGES="c849${RUN}-packages"; C849_SCRATCH="c849${RUN}-scratch"; C849_NPM="c849${RUN}-npm"
            printf '%s\n' "$C849_PACKAGES" >> "$CASE_DIR/.fixture-volumes"
            c849_volume "$C849_PACKAGES" nuget-packages yes "$ADMITTED"
            c849_fixture_launch create --name "$name" --entrypoint sleep "$ADMITTED" infinity >/dev/null
            printf 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n' > "$CASE_DIR/.fixture-network"
            : > "$C1192_ROOT/network"
            c849_fixture_finalize || code=$?
            [ "$code" = 0 ] && ! volume_exists "$WORK" && ! volume_exists "$MASK" \
                && ! container_exists "$WRITER" && ! container_exists "$READER" \
                && ! container_exists "$name" && ! volume_exists "$C849_PACKAGES" && [ ! -f "$C1192_ROOT/network" ] && good=1
            ;;
        *) fail "unknown audit $row" ;;
    esac
    emit audit "$good"
    printf 'scenario=%s exit=%s\n' "$row" "$code"
    cat "$C1192_ROOT/order"
    cat "$CASE_DIR/fixture-cleanup-ledger.txt"
}

# Real production acquisition writes; only Docker observations are modeled.
mode_fingerprint_acquisition() {
    local variant="${3:?variant}" name="c849-${RUN}-donor" fingerprint code=0
    fingerprint="$CASE_DIR/.fixture-fingerprint-$name"
    printf 'foreign victim bytes\n' > "$C1192_ROOT/victim"
    cp -- "$C1192_ROOT/victim" "$C1192_ROOT/victim-before"
    mkdir "$C1192_ROOT/victim-directory"
    printf 'directory sentinel\n' > "$C1192_ROOT/victim-directory/sentinel"
    case "$variant" in
        file-link) ln -s "$C1192_ROOT/victim" "$fingerprint" ;;
        directory-link) ln -s "$C1192_ROOT/victim-directory" "$fingerprint" ;;
        dangling-link) ln -s "$C1192_ROOT/missing-victim" "$fingerprint" ;;
        regular) printf 'foreign record bytes\n' > "$fingerprint" ;;
        fresh|inspection-link|publication-link) ;;
        inspection-fifo-link) mkfifo "$C1192_ROOT/victim-pipe"; exec 8<> "$C1192_ROOT/victim-pipe" ;;
        hard-link) ln "$C1192_ROOT/victim" "$fingerprint" ;;
        directory) mkdir "$fingerprint" ;;
        fifo|fifo-link) mkfifo "$C1192_ROOT/victim-pipe"; exec 8<> "$C1192_ROOT/victim-pipe"; if [ "$variant" = fifo ]; then mkfifo "$fingerprint"; exec 9<> "$fingerprint"; else ln -s "$C1192_ROOT/victim-pipe" "$fingerprint"; fi ;;
        *) fail "unknown fingerprint variant $variant" ;;
    esac
    docker() {
        case "$1" in
            create)
                [ "$2" = --cidfile ] || return 97
                printf '%064d\n' 1 > "$3"
                printf 'create\n' >> "$C1192_ROOT/events"
                ;;
            inspect)
                case "$3" in
                    '{{.Name}}') printf '/c849-%s-donor\n' "$RUN" ;;
                    '{{.Image}}') printf 'sha256:owned-image\n' ;;
                    '{{.Created}}')
                        if [ "$variant" = inspection-link ]; then
                            ln -s "$C1192_ROOT/victim" "$CASE_DIR/.fixture-fingerprint-c849-${RUN}-donor"
                        fi
                        if [ "$variant" = inspection-fifo-link ]; then
                            ln -s "$C1192_ROOT/victim-pipe" "$CASE_DIR/.fixture-fingerprint-c849-${RUN}-donor"
                        fi
                        printf '2026-10-13T00:00:00Z\n' ;;
                    *) return 97 ;;
                esac ;;
            *) return 97 ;;
        esac
    }
    c849_fixture_helper_lookup() { return 0; }
    c849_fixture_resource_state() { return 0; }
    mv() {
        if [ "$variant" = publication-link ] && [ "${*: -1}" = ".fixture-fingerprint-c849-${RUN}-donor" ]; then
            ln -s "$C1192_ROOT/victim" "${*: -1}"
        fi
        command mv "$@"
    }
    c849_fixture_launch create --name "$name" image || code=$?
    printf 'launch-exit=%s\n' "$code"
    local preserved=0 diagnosis=0
    cmp -s -- "$C1192_ROOT/victim-before" "$C1192_ROOT/victim" && preserved=1
    printf 'victim-preserved=%s\n' "$preserved"
    if grep -Fq 'FixtureFingerprintAcquisitionRefused' "$CASE_DIR/fixture-cleanup-ledger.txt" 2>/dev/null; then diagnosis=1; fi
    printf 'named-refusal=%s\n' "$diagnosis"
    [ "$preserved" = 1 ] || fail 'fingerprint overwrote out-of-case victim'
    case "$variant" in
        fresh)
            [ "$code" = 0 ] && [ -f "$fingerprint" ] && [ ! -L "$fingerprint" ] || fail 'fresh fingerprint not acquired'
            [ "$(cat -- "$fingerprint")" = "/$name|sha256:owned-image|2026-10-13T00:00:00Z" ] || fail 'fresh fingerprint wrong bytes'
            [ -s "$CASE_DIR/.fixture-id-$name" ] || fail 'fresh acquisition lost CID'
            ;;
        *)
            [ "$code" = 2 ] && [ "$diagnosis" = 1 ] || fail 'fingerprint collision was not a named refusal'
            case "$variant" in
                regular) [ "$(cat -- "$fingerprint")" = 'foreign record bytes' ] || fail 'foreign regular record changed' ;;
                file-link|inspection-link|publication-link) [ -L "$fingerprint" ] && [ "$(readlink -- "$fingerprint")" = "$C1192_ROOT/victim" ] || fail 'victim link changed' ;;
                directory-link) [ -L "$fingerprint" ] && [ "$(cat -- "$C1192_ROOT/victim-directory/sentinel")" = 'directory sentinel' ] || fail 'directory victim changed' ;;
                dangling-link) [ -L "$fingerprint" ] && [ ! -e "$C1192_ROOT/missing-victim" ] || fail 'dangling target created' ;;
            esac
            ;;
    esac
}


# Exercise the metadata sinks through production functions, with independent victims.
mode_metadata_write() {
    local writer="${3:?writer}" variant name="c849-${RUN}-donor" target code log id
    id=$(printf '%064d' 1)
    local variants="file-link fifo-link dangling-link directory-link hard-link directory fifo late-link fresh"
    [ "$writer" != cid ] || variants="late-link file-link fifo-link dangling-link directory-link hard-link directory fifo fresh"
    for variant in $variants; do
        local sandbox="$C1192_ROOT/write-$variant"
        mkdir -m 0700 "$sandbox"
        CASE_DIR="$sandbox/case"; mkdir -m 0700 "$CASE_DIR"
        C849_CASE_ID=$(stat -c '%d:%i:%u' "$CASE_DIR")
        printf 'out-of-case victim bytes\n' > "$sandbox/victim"
        cp "$sandbox/victim" "$sandbox/before"
        mkdir "$sandbox/directory"; printf 'directory sentinel\n' > "$sandbox/directory/sentinel"
        mkfifo "$sandbox/pipe"; exec 8<> "$sandbox/pipe"
        case "$writer" in
            cid|cid-acquisition) target="$CASE_DIR/.fixture-id-$name" ;;
            network|network-acquisition) target="$CASE_DIR/.fixture-network" ;;
            helpers) target="$CASE_DIR/.fixture-helpers" ;;
            ledger) target="$CASE_DIR/fixture-cleanup-ledger.txt"; : > "$CASE_DIR/.fixture-volumes" ;;
            residue) target="$CASE_DIR/fixture-cleanup-residue.txt" ;;
            escape) target="$CASE_DIR/.fixture-escape-identity" ;;
            volumes) target="$CASE_DIR/.fixture-volumes" ;;
            diagnostic) target="$CASE_DIR/race-p1.log" ;;
            control) target="$CASE_DIR/.control-PC-99" ;;
        esac
        # Legitimate records are inspected before modeled observations may change them.
        case "$writer" in
            cid) printf '%s\n' "$id" > "$target" ;;
            network) printf 'c849-%s-npm\n' "$RUN" > "$target" ;;
            helpers) printf 'c849-%s-writer\n' "$RUN" > "$target" ;;
            volumes) printf 'c849%s-packages\nkeep\n' "$RUN" > "$target" ;;
        esac
        plant_metadata_type() {
            rm -f -- "$target"
            case "$variant" in
                file-link|late-link) ln -s "$sandbox/victim" "$target" ;;
                fifo-link) ln -s "$sandbox/pipe" "$target" ;;
                dangling-link) ln -s "$sandbox/missing" "$target" ;;
                directory-link) ln -s "$sandbox/directory" "$target" ;;
                hard-link) ln "$sandbox/victim" "$target" ;;
                directory) mkdir "$target" ;;
                fifo) mkfifo "$target"; exec 9<> "$target" ;;
            esac
        }
        [ "$variant" = fresh ] || [ "$variant" = late-link ] || plant_metadata_type
        docker() {
            if [ "$1" = create ]; then
                if [ "$variant" = late-link ]; then plant_metadata_type; fi
                printf '%s\n' "$id" > "$3"
                return 0
            fi
            case "${*: -2:1}" in
                '{{.Id}}') printf '%s\n' "$id" ;;
                '{{.Image}}') printf 'sha256:owned-image\n' ;;
                '{{.Created}}') printf '2026-10-13T00:00:00Z\n' ;;
                *io.antiphon.owner*) if [ "$writer" = helpers ]; then printf foreign; else printf c849-fixture; fi ;;
                *fixture-run*) printf '%s\n' "$RUN" ;;
                *fixture-role*) if [ "$writer" = network ]; then printf npm-network; else printf donor; fi ;;
                '{{.Name}}')
                    [ "$variant" != late-link ] || [ -L "$target" ] || plant_metadata_type
                    if [ "$writer" = network ]; then printf 'c849-%s-npm\n' "$RUN"; else printf '/c849-%s-donor\n' "$RUN"; fi ;;
                *) return 97 ;;
            esac
        }
        c849_fixture_resource_state() { return 0; }
        c849_fixture_resource_remove() { :; }
        c849_fixture_helper_lookup() { return 0; }
        code=0; log="$sandbox/out"
        if [ "$variant" = late-link ]; then
            case "$writer" in cid|cid-acquisition|network) ;; *) plant_metadata_type ;; esac
        fi
        case "$writer" in
            cid-acquisition) c849_fixture_launch create --name "$name" image > "$log" 2>&1 || code=$? ;;
            network-acquisition) c849_fixture_network_record "$id" > "$log" 2>&1 || code=$? ;;
            cid) c849_fixture_container_identity "$name" "$id" donor > "$log" 2>&1 || code=$? ;;
            network) c849_fixture_network_release > "$log" 2>&1 || code=$? ;;
            helpers) c849_fixture_helper_release "c849-${RUN}-writer" > "$log" 2>&1 || code=$? ;;
            ledger) c849_fixture_foreign_volume example > "$log" 2>&1 || code=$? ;;
            residue) c849_fixture_cleanup_unknown container example > "$log" 2>&1 || code=$? ;;
            escape) ln -s "$CASE_DIR" "$CASE_DIR/.fixture-escape"; c849_fixture_escape_record > "$log" 2>&1 || code=$? ;;
            volumes) c849_fixture_workspace_drop_name "c849${RUN}-packages" > "$log" 2>&1 || code=$? ;;
            diagnostic) SERVER2_ROOT="$sandbox/source"; mkdir "$SERVER2_ROOT"; printf 'diagnostic bytes\n' > "$SERVER2_ROOT/race-p1.log"; c849_fixture_export_logs > "$log" 2>&1 || code=$? ;;
            control) c849_fixture_control PC-99 ExpectedFailure bash -c 'printf "ExpectedFailure\n"; exit 2' > "$log" 2>&1 || code=$? ;;
        esac
        cmp -s "$sandbox/before" "$sandbox/victim" || fail "$writer/$variant victim changed"
        [ ! -e "$sandbox/missing" ] || fail "$writer/$variant dangling victim created"
        [ "$(cat "$sandbox/directory/sentinel")" = 'directory sentinel' ] || fail "$writer/$variant directory changed"
        local pipe_bytes=''
        if IFS= read -r -t 0.05 -u 8 pipe_bytes; then fail "$writer/$variant wrote FIFO: $pipe_bytes"; fi
        if [ "$variant" = fresh ]; then
            [ "$code" = 0 ] && [ -f "$target" ] && [ ! -L "$target" ] && [ "$(stat -c %h "$target")" = 1 ] || { cat "$log"; fail "$writer fresh failed code=$code"; }
            case "$writer" in
                cid|cid-acquisition) [ "$(cat "$target")" = "$id" ] || fail 'CID bytes' ;;
                network|network-acquisition) [ "$(cat "$target")" = "$id" ] || fail 'network bytes' ;;
                helpers) [ ! -s "$target" ] || fail 'helper not dropped' ;;
                volumes) [ "$(cat "$target")" = keep ] || fail 'volume ledger bytes' ;;
                diagnostic) [ "$(cat "$target")" = 'diagnostic bytes' ] || fail 'diagnostic bytes' ;;
                control) [ "$(cat "$target")" = ExpectedFailure ] || fail 'control bytes' ;;
                escape) [ "$(cat "$target")" = "$(stat -c '%d:%i:%u' "$CASE_DIR/.fixture-escape")" ] || fail 'escape bytes' ;;
                ledger) grep -Fxq 'foreign: left untouched volume example' "$target" || fail 'ledger bytes' ;;
                residue) grep -Fxq example "$target" || fail 'residue bytes' ;;
            esac
        else
            [ "$code" != 0 ] && grep -q FixtureMetadataWriteRefused "$log" || { cat "$log"; fail "$writer/$variant missing named refusal code=$code"; }
        fi
        printf '%s-%s=1\n' "$writer" "$variant"
        exec 8>&-; exec 9>&-
    done
}

# Publication-time links live inside the owned directory. Update rename replaces
# the link, never follows it; create publication still refuses an occupied name.
mode_publication_link() {
    local writer="${3:?writer}" target code=0 id expected
    id=$(printf '%064d' 1)
    printf 'outside victim\n' > "$C1192_ROOT/victim"
    cp "$C1192_ROOT/victim" "$C1192_ROOT/before"
    case "$writer" in
        network|network-acquisition) target="$CASE_DIR/.fixture-network"; expected="$id" ;;
        helpers) target="$CASE_DIR/.fixture-helpers"; expected='' ;;
        volumes) target="$CASE_DIR/.fixture-volumes"; expected=keep ;;
        ledger*) target="$CASE_DIR/fixture-cleanup-ledger.txt"; expected='old
foreign: left untouched volume example' ;;
        residue*) target="$CASE_DIR/fixture-cleanup-residue.txt"; expected='old
example' ;;
        diagnostic) target="$CASE_DIR/race-p1.log"; expected='diagnostic bytes' ;;
        control) target="$CASE_DIR/.control-PC-99"; expected=ExpectedFailure ;;
        escape) target="$CASE_DIR/.fixture-escape-identity" ;;
        cid-acquisition|cid-migration) target="$CASE_DIR/.fixture-id-c849-${RUN}-donor"; expected="$id" ;;
        *) fail "unknown publication writer $writer" ;;
    esac
    case "$writer" in
        network) printf 'c849-%s-npm\n' "$RUN" > "$target" ;;
        helpers) printf 'c849-%s-writer\n' "$RUN" > "$target" ;;
        volumes) printf 'c849%s-packages\nkeep\n' "$RUN" > "$target" ;;
        *-ensure) printf 'old\n' > "$target"; expected=old ;;
        *-reset) printf 'old\n' > "$target"; expected='' ;;
        ledger|residue) printf 'old\n' > "$target" ;;
        diagnostic|control) printf 'old\n' > "$target" ;;
    esac
    mv() {
        if [ "${*: -1}" = "${target##*/}" ]; then
            # This is precisely after the publisher's last destination check.
            rm -f -- "${target##*/}"
            ln -s "$C1192_ROOT/victim" "${target##*/}"
            printf 'rename-boundary\n' >> "$C1192_ROOT/publication-seam"
        fi
        command mv "$@"
    }
    docker() {
        if [ "$1" = create ]; then printf '%s\n' "$id" > "$3"; return 0; fi
        case "${*: -2:1}" in
            '{{.Id}}') printf '%s\n' "$id" ;;
            *io.antiphon.owner*) if [ "$writer" = helpers ]; then printf foreign; else printf c849-fixture; fi ;;
            *fixture-run*) printf '%s\n' "$RUN" ;;
            *fixture-role*) if [ "$writer" = network ]; then printf npm-network; else printf donor; fi ;;
            '{{.Name}}') if [ "$writer" = network ]; then printf 'c849-%s-npm\n' "$RUN"; else printf '/c849-%s-donor\n' "$RUN"; fi ;;
            *) return 97 ;;
        esac
    }
    c849_fixture_resource_state() { return 0; }
    c849_fixture_resource_remove() { :; }
    c849_fixture_helper_lookup() { return 0; }
    case "$writer" in
        network) c849_fixture_network_release || code=$? ;;
        helpers) c849_fixture_helper_release "c849-${RUN}-writer" || code=$? ;;
        volumes) c849_fixture_workspace_drop_name "c849${RUN}-packages" || code=$? ;;
        ledger) c849_fixture_foreign_volume example || code=$? ;;
        residue) c849_fixture_cleanup_unknown container example || code=$? ;;
        *-ensure) c849_fixture_write "$target" ensure true || code=$? ;;
        *-reset) c849_fixture_write "$target" replace true || code=$? ;;
        diagnostic) printf 'diagnostic bytes\n' > "$SERVER2_ROOT/race-p1.log"; c849_fixture_export_logs || code=$? ;;
        control) c849_fixture_control PC-99 ExpectedFailure bash -c 'printf "ExpectedFailure\n"; exit 2' || code=$? ;;
        network-acquisition) c849_fixture_network_record "$id" || code=$? ;;
        cid-acquisition) c849_fixture_launch create --name "c849-${RUN}-donor" image || code=$? ;;
        cid-migration) c849_fixture_container_identity "c849-${RUN}-donor" "c849-${RUN}-donor" donor || code=$? ;;
        escape) ln -s "$CASE_DIR" "$CASE_DIR/.fixture-escape"; c849_fixture_escape_record || code=$? ;;
    esac
    [ "$(cat "$C1192_ROOT/publication-seam")" = rename-boundary ] || fail 'rename seam not reached exactly once'
    cmp -s "$C1192_ROOT/before" "$C1192_ROOT/victim" || fail 'publication wrote outside case'
    case "$writer" in
        *-acquisition|cid-migration|escape)
            [ "$code" = 2 ] && [ -L "$target" ] && [ "$(readlink "$target")" = "$C1192_ROOT/victim" ] || fail "create did not refuse $writer code=$code"
            emit publication-exit "$code" ;;
        *)
            [ "$code" = 0 ] && [ -f "$target" ] && [ ! -L "$target" ] && [ "$(stat -c '%h:%u' "$target")" = "1:$(id -u)" ] || fail "update did not publish regular owned file $writer code=$code"
            printf '%s' "$expected" > "$C1192_ROOT/expected"
            [ -z "$expected" ] || printf '\n' >> "$C1192_ROOT/expected"
            cmp -s "$C1192_ROOT/expected" "$target" || fail "wrong publication bytes $writer"
            emit publication-exit "$code" ;;
    esac
    emit victim-preserved 1
}

# Model observations remain live filesystem reads. External cat per candidate or
# field is needless process work, and this guard fails on that old seam.
mode_model_inspection() {
    plant_container first owner-a run-a role-a image-a ''
    plant_container second owner-b run-b role-b image-b ''
    local identity observed
    identity=$(cat "$C1192_ROOT/containers/second/id")
    cat() { printf 'external-cat\n' >> "$C1192_ROOT/model-spawns"; command cat "$@"; }
    observed=$(docker_inspect_container -f '{{.Name}}' "$identity")
    [ "$observed" = /second ] || fail 'ID lookup changed'
    observed=$(docker_inspect_container -f '{{.Image}}' "$identity")
    [ "$observed" = image-b ] || fail 'image lookup changed'
    printf 'changed-owner\n' > "$C1192_ROOT/containers/second/label-owner"
    observed=$(docker_inspect_container -f '{{index .Config.Labels "io.antiphon.owner"}}' "$identity")
    [ "$observed" = changed-owner ] || fail 'stale owner observation'
    : > "$C1192_ROOT/containers/second/label-run"
    observed=$(docker_inspect_container -f '{{index .Config.Labels "io.antiphon.fixture-run"}}' "$identity")
    [ -z "$observed" ] || fail 'empty field observation'
    [ ! -s "$C1192_ROOT/model-spawns" ] || fail "model inspection spawned $(wc -l < "$C1192_ROOT/model-spawns") cat processes"
    emit live-fields-preserved 1
    emit external-reader-spawns 0
}

# Pin which scenarios need diagnostic setup; existing full behavior assertions
# still own export bytes, ordering, failure and controller replacement.
mode_deadline_setup() {
    local key count name code expected
    for key in finalization-success finalization-primary finalization-delete cleanup-probe-success; do
        reset_runtime; require_init
        mode="${key%-*}"; S7_SCENARIO="${key##*-}"
        SERVER2_ROOT="$CASE_DIR/setup-$key"
        s7_groups
        code=0
        ( c849_fixture_seed ) > "$C1192_ROOT/setup-output" 2>&1 || code=$?
        expected=0; [ "$S7_SCENARIO" != primary ] || expected=9
        [ "$code" = "$expected" ] || { command cat "$C1192_ROOT/setup-output"; fail "setup exit $key code=$code"; }
        count=0
        for name in race-p1.log race-p2.log race-offline.log lock-holder.log lock-waiter.log; do
            [ ! -f "$SERVER2_ROOT/$name" ] || count=$((count + 1))
        done
        expected=0
        case "$key" in finalization-success|finalization-primary) expected=5 ;; esac
        [ "$count" = "$expected" ] || fail "unrelated diagnostic setup $key actual=$count expected=$expected"
        emit "$key-logs" "$count"
    done
}

# 1 when driver.sh records slot-held, then the go wait, then ready, then restore.
c1192_driver_order() {
    local body slot go ready restore
    body=$(declare -f c849_fixture_race_materialize)
    slot=$(c1192_first_line_number 'slot-held' "$body" || true)
    go=$(c1192_first_line_number '/fixture/race/go' "$body" || true)
    ready=$(c1192_first_line_number ': > ready' "$body" || true)
    restore=$(c1192_first_line_number 'dotnet restore' "$body" || true)
    if [ -n "$slot" ] && [ -n "$go" ] && [ -n "$ready" ] && [ -n "$restore" ] \
        && [ "$slot" -lt "$go" ] && [ "$go" -lt "$ready" ] && [ "$ready" -lt "$restore" ]; then
        printf '1'
    else
        printf '0'
    fi
}

c1192_slot_plant() {
    local packages="$1" scratch="$2"
    enable_synthetic_clock
    reset_runtime
    : > "$C1192_ROOT/scenario.err"
    require_init
    plant_cache "$packages" nuget-packages
    plant_cache "$scratch" nuget-scratch
    : > "$C1192_ROOT/sleeps"
    rm -f -- "$C1192_ROOT/slot-stop"
}

# Command substitution hides the barrier's exit and would orphan its probes.
# The trap stops those probes when the barrier refuses.
c1192_slot_barrier() {
    local packages="$1" scratch="$2" code=0 out
    out=$(
        trap 'c1192_kill_jobs' EXIT
        c849_fixture_race_restore_barrier "$ADMITTED" "$packages" "$scratch" 2>>"$C1192_ROOT/scenario.err"
    ) || code=$?
    C1192_SLOT_CODE=$code
    C1192_SLOT_OUT=$out
}

mode_slot_delay() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    c1192_slot_plant "$packages" "$scratch"
    C1192_SLOT_GATE=1
    C1192_SLOT_DELAY_FOR=p2
    C589_SLOT_WAIT_SECONDS=90
    c1192_slot_barrier "$packages" "$scratch"
    unset C1192_SLOT_GATE C1192_SLOT_DELAY_FOR C589_SLOT_WAIT_SECONDS
    refuse_unexpected
    if [ "$C1192_SLOT_CODE" = 0 ]; then
        emit delayed-race 1
    else
        emit delayed-race 0
    fi
    emit driver-order "$(c1192_driver_order)"
    emit slot-diagnosis "$(diagnosis_of "$C1192_SLOT_OUT")"
}

mode_slot_timeout() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    c1192_slot_plant "$packages" "$scratch"
    C1192_SLOT_GATE=1
    C1192_SLOT_EXIT4_FOR=p2
    C589_SLOT_WAIT_SECONDS=2
    c1192_slot_barrier "$packages" "$scratch"
    unset C1192_SLOT_GATE C1192_SLOT_EXIT4_FOR C589_SLOT_WAIT_SECONDS
    refuse_unexpected
    emit slot-diagnosis "$(diagnosis_of "$C1192_SLOT_OUT")"
}

# Same shell as the probes. A live wait is recorded and does not block, so the
# base hang is a fact instead of a 25-minute stall. Facts are emitted, then the
# probes are stopped. The harness still exits 0 so the assertion is the failure.
# 1 only when each fixture volume existed before finalization and a volume rm
# removed it. Absence alone is not removal: a volume that was never created
# used to leave this fact at 1.
c1192_volumes_removed() {
    local name ok=1
    for name in "c849${RUN}race-packages" "c849${RUN}race-scratch" "$WORK"; do
        volume_exists "$name" && ok=0
        grep -Fxq "volume $name" "$C1192_ROOT/order" || ok=0
    done
    [ "$ok" = 1 ] && [ "${1:-0}" = 1 ]
}

c1192_slot_refusal_finish() {
    trap - EXIT
    local present=0
    if volume_exists "c849${RUN}race-packages" \
        && volume_exists "c849${RUN}race-scratch" \
        && volume_exists "$WORK"; then
        present=1
    fi
    c849_fixture_finalize || true
    local waited=0 removed=1 released=0 volumes=0
    if [ -f "$C1192_ROOT/waits" ] && grep -q WAITED_LIVE "$C1192_ROOT/waits"; then
        waited=1
    fi
    container_exists "c849-${RUN}-restore-p1" && removed=0
    container_exists "c849-${RUN}-restore-p2" && removed=0
    if [ -f "$C1192_ROOT/curls" ] \
        && grep -q 'DELETE' "$C1192_ROOT/curls" \
        && grep -q '11111111-1111-1111-1111-111111111111' "$C1192_ROOT/curls"; then
        released=1
    fi
    if c1192_volumes_removed "$present"; then
        volumes=1
    fi
    emit waited-live "$waited"
    emit probes-removed "$removed"
    emit lease-released "$released"
    emit volumes-removed "$volumes"
    if [ -f "$C1192_ROOT/refusal" ]; then
        cat -- "$C1192_ROOT/refusal"
    fi
    c1192_kill_jobs
    cleanup_private
    exit 0
}

mode_slot_refusal() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    c1192_slot_plant "$packages" "$scratch"
    : > "$C1192_ROOT/waits"
    : > "$C1192_ROOT/curls"
    printf '%s\n' '#!/bin/sh' 'printf "%s\n" "$*" >> "$C1192_ROOT/curls"' 'exit 0' > "$C1192_ROOT/bin/curl"
    chmod 755 "$C1192_ROOT/bin/curl"
    wait() {
        local pid stat live=0 frame
        # Launch waits for its own docker child so the cid can be published
        # while that child is still running. That is not the barrier hang.
        for frame in "${FUNCNAME[@]}"; do
            if [ "$frame" = c849_fixture_launch ]; then
                command wait "$@"
                return
            fi
        done
        for pid in "$@"; do
            case "$pid" in
                ''|*[!0-9]*) continue ;;
            esac
            kill -0 "$pid" 2>/dev/null || continue
            stat=$(ps -o stat= -p "$pid" 2>/dev/null || true)
            case "$stat" in
                *Z*) ;;
                *)
                    printf 'WAITED_LIVE\n' >> "$C1192_ROOT/waits"
                    live=1
                    ;;
            esac
        done
        [ "$live" = 0 ] || return 0
        command wait "$@"
    }
    write_result() {
        local diagnosis="$2" code="$3"
        if [ "${WROTE:-0}" = 1 ]; then exit "$code"; fi
        WROTE=1
        printf 'DIAGNOSIS=%s\n' "$diagnosis" > "$C1192_ROOT/refusal"
        exit "$code"
    }
    trap 'c1192_slot_refusal_finish' EXIT
    C1192_SLOT_GATE=1
    C1192_SLOT_NEVER=1
    C589_SLOT_WAIT_SECONDS=2
    C1192_SLOT_LEASE=11111111-1111-1111-1111-111111111111
    ANTIPHON_BUILD_SLOTS_URL=http://127.0.0.1:9/build-slots
    export ANTIPHON_BUILD_SLOTS_URL C1192_SLOT_GATE C1192_SLOT_NEVER C1192_SLOT_LEASE
    c849_fixture_race_restore_barrier "$ADMITTED" "$packages" "$scratch" 2>>"$C1192_ROOT/scenario.err" || true
    c1192_slot_refusal_finish
}

c1192_slot_curl() {
    printf '%s\n' '#!/bin/sh' 'printf "%s\n" "$*" >> "$C1192_ROOT/curls"' 'exit 0' > "$C1192_ROOT/bin/curl"
    chmod 755 "$C1192_ROOT/bin/curl"
}

c1192_fixture_traps() {
    # declare -f prints a trailing semicolon that the source line does not have.
    declare -f c849_fixture | sed -n 's/^    \(trap .*\)/\1/p' | sed 's/;$//'
}

# Plant one foreign restore-p1, then run the barrier. Release may kill only a
# CID this run recorded. The planted container must still be here afterwards.
c1192_foreign_witness() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch" name id survived=0 killed=0 residue=0 ledger=0
    name="c849-${RUN}-restore-p1"
    c1192_slot_plant "$packages" "$scratch"
    plant_container "$name" foreign other-run consumer "$ADMITTED" ""
    id="$(cat "$C1192_ROOT/containers/$name/id")"
    C1192_SLOT_GATE=1
    C589_SLOT_WAIT_SECONDS=5
    c1192_slot_barrier "$packages" "$scratch"
    unset C1192_SLOT_GATE C589_SLOT_WAIT_SECONDS
    container_exists "$name" && survived=1
    if grep -F "kill $id" "$C1192_ROOT/order" >/dev/null 2>&1 \
        || grep -F "kill $name" "$C1192_ROOT/order" >/dev/null 2>&1; then
        killed=1
    fi
    if [ -f "$CASE_DIR/fixture-cleanup-residue.txt" ] && grep -Fxq "$name" "$CASE_DIR/fixture-cleanup-residue.txt"; then
        residue=1
    fi
    if [ -f "$CASE_DIR/fixture-cleanup-ledger.txt" ] \
        && grep -Fxq "foreign: left untouched container $name" "$CASE_DIR/fixture-cleanup-ledger.txt"; then
        ledger=1
    fi
    emit foreign-survived "$survived"
    emit foreign-killed "$killed"
    emit foreign-residue "$residue"
    emit foreign-ledger "$ledger"
    emit foreign-diagnosis "$(diagnosis_of "$C1192_SLOT_OUT")"
    c1192_kill_jobs
}

# Dump this shell into a script a new bash can source. A background job of a
# non-interactive shell is started with SIGINT ignored, and bash will not
# re-arm a signal that was ignored at exec. The child is exec'd only after
# perl sets SIGINT back to the default.
c1192_dump_shell_state() {
    local name spec flags
    declare -f
    while IFS= read -r name; do
        case "$name" in
            BASH*|FUNCNAME|GROUPS|EPOCH*|BASHPID|PPID|_|PIPESTATUS|COMP_*|SRANDOM|SECONDS|LINENO|OLDPWD|PWD|SHELLOPTS|BASHOPTS|UID|EUID|RANDOM|HISTCMD|name|spec|flags) continue ;;
            *TOKEN*|*SECRET*|*PASSWORD*|*CREDENTIAL*) continue ;;
        esac
        spec=$(declare -p "$name" 2>/dev/null || true)
        [ -n "$spec" ] || continue
        # declare -p is "declare -flags name=value". The r flag is readonly.
        # A later r in the value is not a flag.
        flags=${spec#declare }
        case "$flags" in
            -*)
                flags=${flags%% *}
                case "$flags" in
                    *r*) continue ;;
                esac
                ;;
        esac
        printf '%s\n' "$spec"
    done < <(compgen -v)
}

# Run the barrier under the fixture's own trap lines. TERM must exit 143 and
# INT 130, both probes die by the recorded CID, the lease is deleted, and the
# volumes that were present are removed afterwards.
#
# INT reaches the fixture when it is the foreground of the deploy ssh session
# (Invoke-C590Ssh runs `bash c590-remote.sh` with no trailing &): the remote
# shell starts with SIGINT default, and an operator Ctrl-C is delivered to
# that process group. INT does not reach a fixture that was itself started
# with & from a non-interactive shell, because the ignored disposition is
# inherited at exec and `trap` cannot undo it. setsid does not clear that
# inherited ignore. TERM is not in that pair, so a pid-only TERM still
# arrives. This witness execs the barrier through perl, which sets SIGINT
# back to the default before exec, so the test can deliver INT even when
# this harness was started in the background.
#
# A pid-only SIGINT after that is still discarded while bash waits on a
# foreground child that does not itself die from SIGINT (the slot poll's
# sleep). Monitor mode makes the child its own process group, and the
# witness signals that group. That is the same delivery as the operator
# Ctrl-C, and it does not signal this fixture's own group.
c1192_signal_witness() {
    local sig="$1" expected="$2" packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    local traps pid i n=0 code=0 present=0 cid_kills=0 volumes=0 released=0 key child armed monitor=0
    traps="$(c1192_fixture_traps)"
    if ! c1192_has_exact_line "trap 'c849_fixture_on_signal INT' INT" "$traps" \
        || ! c1192_has_exact_line "trap 'c849_fixture_on_signal TERM' TERM" "$traps"; then
        emit "signal-${sig}-installed" 0
        emit "signal-${sig}-exit" 0
        emit "signal-${sig}-cid-kills" 0
        emit "signal-${sig}-lease" 0
        emit "signal-${sig}-volumes" 0
        return 0
    fi
    c1192_slot_plant "$packages" "$scratch"
    : > "$C1192_ROOT/curls"
    c1192_slot_curl
    if volume_exists "$packages" && volume_exists "$scratch" && volume_exists "$WORK"; then
        present=1
    fi
    C1192_SLOT_GATE=1
    C1192_SLOT_NEVER=1
    C589_SLOT_WAIT_SECONDS=30
    C1192_SLOT_LEASE=11111111-1111-1111-1111-111111111111
    ANTIPHON_BUILD_SLOTS_URL=http://127.0.0.1:9/build-slots
    export ANTIPHON_BUILD_SLOTS_URL C1192_SLOT_GATE C1192_SLOT_NEVER C1192_SLOT_LEASE
    rm -f -- "$C1192_ROOT/signal-armed" "$C1192_ROOT/signal-out" "$C1192_ROOT/live-before"
    child="$C1192_ROOT/signal-child.sh"
    {
        printf '%s\n' '#!/bin/bash' 'shopt -s nullglob'
        c1192_dump_shell_state
        printf '%s\n' 'set -euo pipefail' "eval $(printf '%q' "$traps")"
        printf '%s\n' \
            'armed=0' \
            'trap_text=$(trap -p INT)' \
            'case "$trap_text" in' \
            '    *c849_fixture_on_signal*) armed=1 ;;' \
            'esac' \
            "printf '%s\n' \"\$armed\" > $(printf '%q' "$C1192_ROOT")/signal-armed" \
            '[ "$armed" = 1 ] || exit 2' \
            "c849_fixture_race_restore_barrier $(printf '%q' "$ADMITTED") $(printf '%q' "$packages") $(printf '%q' "$scratch")"
    } > "$child"
    # perl inherits an ignored SIGINT when this harness was backgrounded, then
    # puts the default back before exec. The bash pid is the same process.
    # Monitor mode is on only for this launch so the child is a process-group
    # leader. Restored before this function returns.
    case $- in *m*) monitor=1 ;; esac
    set -m
    perl -e '$SIG{INT}="DEFAULT"; $SIG{QUIT}="DEFAULT"; exec @ARGV or die $!' -- \
        bash "$child" > "$C1192_ROOT/signal-out" 2>>"$C1192_ROOT/scenario.err" &
    pid=$!
    i=0
    armed=0
    while [ "$i" -lt 200 ]; do
        if [ -f "$C1192_ROOT/signal-armed" ] && [ "$(cat -- "$C1192_ROOT/signal-armed")" = 0 ]; then
            break
        fi
        if [ -f "$C1192_ROOT/signal-armed" ] && [ "$(cat -- "$C1192_ROOT/signal-armed")" = 1 ] \
            && [ -f "$CASE_DIR/.fixture-live-probes" ] \
            && c1192_file_has_substr 'BUILD SLOT granted lease=' "$SERVER2_ROOT/race-p1.log" \
            && cp -f -- "$CASE_DIR/.fixture-live-probes" "$C1192_ROOT/live-before" \
            && c1192_copied_live_cids "$C1192_ROOT/live-before"; then
            armed=1
            break
        fi
        command sleep 0.05
        i=$((i + 1))
    done
    if [ "$armed" != 1 ]; then
        kill -s TERM -- -"$pid" 2>/dev/null || true
        wait "$pid" 2>/dev/null || true
        [ "$monitor" = 1 ] || set +m
        emit "signal-${sig}-installed" 0
        emit "signal-${sig}-exit" 0
        emit "signal-${sig}-cid-kills" 0
        emit "signal-${sig}-lease" 0
        emit "signal-${sig}-volumes" 0
        unset C1192_SLOT_GATE C1192_SLOT_NEVER C589_SLOT_WAIT_SECONDS C1192_SLOT_LEASE ANTIPHON_BUILD_SLOTS_URL
        c1192_kill_jobs
        return 0
    fi
    emit "signal-${sig}-installed" 1
    kill -s "$sig" -- -"$pid" 2>/dev/null || true
    wait "$pid" || code=$?
    [ "$monitor" = 1 ] || set +m
    emit "signal-${sig}-exit" "$code"
    cid_kills=1
    while read -r _ cid name || [ -n "${cid:-}" ]; do
        [ -n "${cid:-}" ] && [ -n "${name:-}" ] || continue
        grep -Fxq "kill $cid" "$C1192_ROOT/order" || cid_kills=0
        container_exists "$name" && cid_kills=0
        if grep -F "kill $name" "$C1192_ROOT/order" >/dev/null 2>&1; then
            cid_kills=0
        fi
    done < "$C1192_ROOT/live-before"
    emit "signal-${sig}-cid-kills" "$cid_kills"
    if grep -q 'DELETE' "$C1192_ROOT/curls" \
        && grep -q '11111111-1111-1111-1111-111111111111' "$C1192_ROOT/curls"; then
        released=1
    fi
    emit "signal-${sig}-lease" "$released"
    if c1192_volumes_removed "$present"; then
        volumes=1
    fi
    emit "signal-${sig}-volumes" "$volumes"
    unset C1192_SLOT_GATE C1192_SLOT_NEVER C589_SLOT_WAIT_SECONDS C1192_SLOT_LEASE ANTIPHON_BUILD_SLOTS_URL
    c1192_kill_jobs
}

# A slow reader must not multiply the slot deadline. Exit 4 inside a few
# seconds of a 3-second budget; the iteration counter would still be running.
c1192_deadline_witness() {
    local a b start code=0 elapsed bounded=0
    command sleep 30 & a=$!
    command sleep 30 & b=$!
    c849_fixture_volume_test_file() { command sleep 1; return 1; }
    C589_SLOT_WAIT_SECONDS=3
    start=$(date +%s)
    c849_fixture_wait_slot_markers p1 p2 "$a" "$b" || code=$?
    elapsed=$(( $(date +%s) - start ))
    unset C589_SLOT_WAIT_SECONDS
    command kill "$a" "$b" 2>/dev/null || true
    command wait "$a" 2>/dev/null || true
    command wait "$b" 2>/dev/null || true
    if [ "$code" = 4 ] && [ "$elapsed" -le 8 ]; then
        bounded=1
    fi
    emit slot-wait-bounded "$bounded"
    emit slot-wait-elapsed "$elapsed"
    emit slot-wait-code "$code"
}

c1192_offline_witness() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch" out code=0
    if ! declare -F c849_fixture_offline_restore >/dev/null; then
        emit offline-diagnosis missing
        emit offline-code 0
        return 0
    fi
    c1192_slot_plant "$packages" "$scratch"
    C1192_FAULT=offline-slot-timeout
    out=$(
        write_result() { printf 'DIAGNOSIS=%s\n' "$2"; exit "$3"; }
        c849_fixture_offline_restore "$ADMITTED" "$packages" "$scratch"
    ) || code=$?
    unset C1192_FAULT
    emit offline-diagnosis "$(diagnosis_of "$out")"
    emit offline-code "$code"
    c1192_kill_jobs
}

# TERM and KILL reach a pid only while jobs -pr lists it. A live pid from
# another shell is on the ledger as pending and is not signalled at all.
c1192_reap_witness() {
    local owned foreign owned_hit=0 foreign_signalled=0 foreign_survived=0 name
    command sleep 60 & owned=$!
    foreign=$(bash -c 'command sleep 60 >/dev/null 2>&1 & echo $!; exit 0')
    # Delivering TERM ends the sleep and drops it from jobs -pr before the
    # escalation check, so -9 is skipped. Record that TERM and leave the pid
    # in the job table; -9 is then the deterministic escalation.
    kill() {
        printf '%s\n' "$*" >> "$C1192_ROOT/kills"
        if [ "$#" -eq 1 ] && [ "$1" = "$owned" ]; then
            return 0
        fi
        command kill "$@"
    }
    : > "$C1192_ROOT/kills"
    c849_fixture_reap_probe "$owned"
    if [ -s "$C1192_ROOT/kills" ] && c1192_file_has_substr '-9' "$C1192_ROOT/kills"; then
        owned_hit=1
    fi
    command wait "$owned" 2>/dev/null || true
    : > "$C1192_ROOT/kills"
    name="c849-${RUN}-restore-p2"
    if [ -n "$foreign" ] && [ -d "/proc/$foreign" ]; then
        printf '%s pending %s\n' "$foreign" "$name" > "$CASE_DIR/.fixture-live-probes"
        c849_fixture_release_live_probes || true
        if [ -s "$C1192_ROOT/kills" ]; then
            foreign_signalled=1
        fi
        if [ -d "/proc/$foreign" ]; then
            foreign_survived=1
        fi
        command kill "$foreign" 2>/dev/null || true
        command wait "$foreign" 2>/dev/null || true
    fi
    unset -f kill
    emit reap-owned-escalated "$owned_hit"
    emit reap-unowned-escalated "$foreign_signalled"
    emit reap-unowned-survived "$foreign_survived"
}

# p1 is a planted foreign name, so the barrier refuses while p2's cid is still
# unpublished. Release waits, then kills p2 by that cid and deletes its lease.
c1192_late_cid_witness() {
    local packages="c849${RUN}race-packages" scratch="c849${RUN}race-scratch"
    local name="c849-${RUN}-restore-p1" p2="c849-${RUN}-restore-p2"
    local foreign_id="" p2id="" survived=0 killed=0 removed=0 killed_by_cid=0 released=0
    c1192_slot_plant "$packages" "$scratch"
    plant_container "$name" foreign other-run consumer "$ADMITTED" ""
    foreign_id="$(cat -- "$C1192_ROOT/containers/$name/id")"
    : > "$C1192_ROOT/curls"
    c1192_slot_curl
    C1192_SLOT_GATE=1
    C1192_LATE_CID_FOR=p2
    C1192_LATE_CID_SECONDS=2
    C589_SLOT_WAIT_SECONDS=30
    C1192_SLOT_LEASE=11111111-1111-1111-1111-111111111111
    ANTIPHON_BUILD_SLOTS_URL=http://127.0.0.1:9/build-slots
    export ANTIPHON_BUILD_SLOTS_URL C1192_SLOT_GATE C1192_LATE_CID_FOR C1192_LATE_CID_SECONDS C1192_SLOT_LEASE
    c1192_slot_barrier "$packages" "$scratch"
    unset C1192_SLOT_GATE C1192_LATE_CID_FOR C1192_LATE_CID_SECONDS C589_SLOT_WAIT_SECONDS C1192_SLOT_LEASE ANTIPHON_BUILD_SLOTS_URL
    container_exists "$name" && survived=1
    if c1192_file_has_substr "kill $foreign_id" "$C1192_ROOT/order" \
        || c1192_file_has_substr "kill $name" "$C1192_ROOT/order"; then
        killed=1
    fi
    container_exists "$p2" || removed=1
    if [ -f "$CASE_DIR/.fixture-id-$p2" ]; then
        p2id="$(cat -- "$CASE_DIR/.fixture-id-$p2")"
    fi
    if [[ "$p2id" =~ ^[0-9a-f]{64}$ ]] && c1192_file_has_substr "kill $p2id" "$C1192_ROOT/order"; then
        killed_by_cid=1
    fi
    if c1192_file_has_substr 'DELETE' "$C1192_ROOT/curls" \
        && c1192_file_has_substr '11111111-1111-1111-1111-111111111111' "$C1192_ROOT/curls"; then
        released=1
    fi
    emit late-diagnosis "$(diagnosis_of "$C1192_SLOT_OUT")"
    emit late-p2-removed "$removed"
    emit late-p2-killed-by-cid "$killed_by_cid"
    emit late-foreign-survived "$survived"
    emit late-foreign-killed "$killed"
    emit late-lease "$released"
    c1192_kill_jobs
}

mode_slot_round2() {
    c1192_foreign_witness
    c1192_signal_witness TERM 143
    c1192_signal_witness INT 130
    c1192_deadline_witness
    c1192_offline_witness
    c1192_reap_witness
}

case "$mode" in
    deadline-setup) mode_deadline_setup ;;
    publication-link) mode_publication_link "$@" ;;
    model-inspection) mode_model_inspection ;;
    metadata-write) mode_metadata_write "$@" ;;
    fingerprint-acquisition) mode_fingerprint_acquisition "$@" ;;
    deletion-audit) mode_deletion_audit "$@" ;;
    acquisition-probe) mode_acquisition_probe ;;
    cleanup-probe) mode_cleanup_probe "$@" ;;
    compose-absence) mode_compose_absence "$@" ;;
    finalization) mode_finalization ;;
    evidence) mode_evidence ;;
    retention) mode_retention ;;
    retention-inspect) mode_retention_inspect ;;
    apphost) mode_apphost ;;
    writable-pruning) mode_writable_pruning ;;
    register) mode_register ;;
    cleanup) mode_cleanup ;;
    partial) mode_partial ;;
    toxic) mode_toxic ;;
    foreign) mode_foreign ;;
    argv) mode_argv ;;
    race) mode_race ;;
    barriers) mode_barriers ;;
    slot-delay) mode_slot_delay ;;
    slot-timeout) mode_slot_timeout ;;
    slot-refusal) mode_slot_refusal ;;
    slot-round2) mode_slot_round2 ;;
    slot-late-cid) c1192_late_cid_witness ;;
    locks) mode_locks ;;
    handoff) mode_handoff ;;
    npm|npm-network-create) mode_npm ;;
    npm-sentinel) mode_npm_sentinel ;;
    backend) mode_backend ;;
    transfer) mode_transfer ;;
    *) fail "unknown mode $mode" ;;
esac
if [ -s "$C1192_ROOT/unexpected" ]; then
    fail "$(cat "$C1192_ROOT/unexpected")"
fi
