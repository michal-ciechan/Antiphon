#!/usr/bin/env bash
# CARD-1192 S1. Extracted c849_* functions against a filesystem Docker model.
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

# Fresh mkdir. A path or marker that already exists is refused and left untouched.
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

# Delete target only when it is a real directory strictly inside private_root,
# no symlink is followed, and the marker matches the token recorded after create.
# Removal stays on the directory identity acquired with that marker. An ancestor
# replaced afterwards is a refusal and deletes nothing.
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

# Hold target, then remove it only through its held parent. Relative names are
# the only destructive operations. C1192_CLEANUP_AFTER_MARKER, when set, runs
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
    rm -rf "$CASE_DIR/fixture-controller"
    rm -f "$CASE_DIR/.fixture-volumes" "$CASE_DIR/.fixture-helpers" \
        "$CASE_DIR/fixture-cleanup-residue.txt" "$CASE_DIR/fixture-summary.txt" \
        "$CASE_DIR/.control-PC-26" "$CASE_DIR/.fixture-volumes.workspace"
    : > "$CASE_DIR/fixture-controls.txt"
    : > "$CASE_DIR/fixture-control-variants.txt"
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
    printf '%s\n' "$owner" > "$dir/label-owner"
    printf '%s\n' "$run_id" > "$dir/label-run"
    printf '%s\n' "$role" > "$dir/label-role"
    printf '%s\n' "$image" > "$dir/image"
    printf '%s\n' "$mount" > "$dir/mount"
}

volume_exists() { [ -d "$C1192_ROOT/volumes/$1" ]; }
container_exists() { [ -d "$C1192_ROOT/containers/$1" ]; }

diagnosis_of() {
    local line
    line=$(printf '%s\n' "$1" | grep '^DIAGNOSIS=' | tail -n 1 || true)
    printf '%s' "${line#DIAGNOSIS=}"
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
    if [ -f "$file" ]; then cat "$file"; else printf '\n'; fi
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
    shift 2
    bash -c "$script" "$@"
}

exec_in() {
    local name="$1"
    shift
    local mount source data
    mount=$(cat "$C1192_ROOT/containers/$name/mount")
    source=$(mount_field "$mount" source)
    data="$C1192_ROOT/volumes/$source/data"
    mkdir -p "$data"
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
        "$bin" -c "${REWRITE[2]}" "${REWRITE[@]:3}"
        return
    fi
    if [ "${REWRITE[1]:-}" = -s ]; then
        local rest=("${REWRITE[@]:2}")
        [ "${rest[0]:-}" = -- ] && rest=("${rest[@]:1}")
        "$bin" -s -- "${rest[@]}"
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
            local dir
            for dir in "$C1192_ROOT/volumes"/*; do
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
            [ -d "$dir" ] || return 1
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
            --name|--network|--user|--entrypoint|-u|-w|--workdir|-e|--env|--env-file)
                local flag="$1" value="$2"
                shift 2
                case "$flag" in
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
}

docker_run() {
    parse_run_create "$@"
    local kind=other-run
    case "$PARSED_MOUNTS" in
        *target=/fixture*) kind=init-run ;;
        *target=/cache*) kind=cache-run ;;
    esac
    event "$(printf '%s\t-\t%s\t%s\t%s' "$kind" "$PARSED_NETWORK" "$PARSED_IMAGE" "$PARSED_MOUNTS")"
    if [ "$C1192_CUT" = root-init ] && [ "$kind" = init-run ]; then return 1; fi
    if [ "$PARSED_ENTRY" = sleep ]; then
        printf 'UNEXPECTED-SLEEP\n' >> "$C1192_ROOT/scenario.err"
        return 97
    fi
    local mount
    IFS=';' read -r -a mounts <<< "$PARSED_MOUNTS"
    mount="${mounts[0]:-}"
    run_script "$mount" "${PARSED_CMD[@]}"
}

docker_inspect_container() {
    local fmt="" name=""
    while [ $# -gt 0 ]; do
        case "$1" in
            -f|--format) fmt="$2"; shift 2 ;;
            *) name="$1"; shift ;;
        esac
    done
    local dir="$C1192_ROOT/containers/$name"
    [ -d "$dir" ] || return 1
    [ -n "$fmt" ] || return 0
    case "$fmt" in
        *io.antiphon.owner*) label_value "$dir/label-owner" ;;
        *fixture-run*) label_value "$dir/label-run" ;;
        *fixture-role*) label_value "$dir/label-role" ;;
        *) printf 'UNEXPECTED-INSPECT %s\n' "$fmt" >> "$C1192_ROOT/scenario.err"; return 97 ;;
    esac
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
            exec_in "$name" "$@"
            ;;
        inspect) shift; docker_inspect_container "$@" ;;
        run) shift; docker_run "$@" ;;
        ps|network) return 0 ;;
        compose) return 1 ;;
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
    plant_volume "$junk" local null c849-fixture "$RUN" work
    plant_volume "$foreign" local null c849-fixture "$RUN" work
    plant_volume "$production" local null server2-runner production nuget-packages
    plant_container "$WRITER" c849-fixture "$RUN" writer "$ADMITTED" "type=volume,source=$WORK,target=/fixture,volume-nocopy"
    plant_container "$READER" c849-fixture "$RUN" reader "$ADMITTED" "type=volume,source=$WORK,target=/fixture,volume-nocopy,readonly"
    plant_container "c849-${RUN}-restore-p1" c849-fixture "$RUN" consumer "$ADMITTED" ""
    printf '%s\n' "$WORK" "$MASK" "$packages" "$junk" "$foreign" "$production" > "$CASE_DIR/.fixture-volumes"
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
    if grep -F -q "container c849-${RUN}-restore-p1" "$C1192_ROOT/order"; then cleanup=1; fi
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

case "$mode" in
    register) mode_register ;;
    cleanup) mode_cleanup ;;
    partial) mode_partial ;;
    toxic) mode_toxic ;;
    foreign) mode_foreign ;;
    argv) mode_argv ;;
    *) fail "unknown mode $mode" ;;
esac
if [ -s "$C1192_ROOT/unexpected" ]; then
    fail "$(cat "$C1192_ROOT/unexpected")"
fi
