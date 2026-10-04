#!/usr/bin/env bash
# CARD-1025: outer-host qualification. No caller-controlled paths or release pins.
set -uo pipefail
DESTINATION=/usr/local/bin/jq
LOCK_ROOT=/var/lock/antiphon-host-jq
CONTAINER_MARKER=/.dockerenv
URL=https://github.com/jqlang/jq/releases/download/jq-1.7.1/jq-linux-amd64
DIGEST=5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5
VERSION=jq-1.7.1
refuse() { printf '%s\n' "$1" >&2; exit 2; }
[ "$#" = 1 ] || refuse HostJqModeInvalid
MODE=$1
case "$MODE" in check|provision) ;; *) refuse HostJqModeInvalid ;; esac

# Observe the same host/daemon distinction as c590, without bootstrap or mutation.
daemon=$(docker info --format '{{.Name}}' 2>/dev/null) || refuse HostJqLaneUnavailable
own=$(hostname 2>/dev/null) || refuse HostJqLaneUnavailable
[ -n "$daemon" ] && [ -n "$own" ] || refuse HostJqLaneUnavailable
[ "$daemon" = "$own" ] && [ ! -e "$CONTAINER_MARKER" ] || refuse HostJqWrongLane

qualify() {
    local truth falsehood meta
    hash -r
    if ! lookup=$(command -v jq); then
        local directory
        local -a search
        IFS=: read -r -a search <<< "$PATH"
        for directory in "${search[@]}"; do
            [ -n "$directory" ] || directory=.
            if [ -e "$directory/jq" ] || [ -L "$directory/jq" ]; then return 4; fi
        done
        return 3
    fi
    [[ "$lookup" = /* ]] && [ -f "$lookup" ] && [ -x "$lookup" ] || return 4
    resolved=$(readlink -f -- "$lookup" 2>/dev/null) || return 4
    # Accept aliases only to the regular canonical leaf; its lookup name alone is not proof.
    [ "$resolved" = "$DESTINATION" ] && [ -f "$DESTINATION" ] && [ ! -L "$DESTINATION" ] || return 5
    version=$("$resolved" --version 2>/dev/null) || return 4
    [[ "$version" =~ [^[:space:]] && "$version" != *$'\n'* ]] || return 4
    # Require the actual outputs as well as both exit codes (constant-success is invalid).
    local predicate='($items|type)=="array" and all($items[]; .ready==true) and any($items[]; .name=="jq")'
    truth=$("$resolved" -en --argjson items '[{"name":"jq","ready":true}]' "$predicate" 2>/dev/null)
    true_exit=$?
    falsehood=$("$resolved" -en --argjson items '[{"name":"jq","ready":false}]' "$predicate" 2>/dev/null)
    false_exit=$?
    [ "$truth" = true ] && [ "$true_exit" = 0 ] || return 4
    [ "$falsehood" = false ] && [ "$false_exit" = 1 ] || return 4
    digest=$(sha256sum -- "$resolved" 2>/dev/null) || return 4
    digest=${digest%% *}
    [[ "$digest" =~ ^[0-9a-f]{64}$ ]] || return 4
    meta=$(stat -Lc '%u %g %a' -- "$resolved" 2>/dev/null) || return 4
    read -r uid gid permissions <<< "$meta"
    [[ "$uid" =~ ^[0-9]+$ && "$gid" =~ ^[0-9]+$ && "$permissions" =~ ^[0-7]{3,4}$ ]] || return 4
}
emit() {
    # Use the qualified executable for escaping observed path/version; no raw environment.
    "$resolved" -cn --arg mode "$MODE" --arg lookupPath "$lookup" --arg path "$resolved" --arg version "$version" \
        --arg digest "$digest" --arg permissions "$permissions" --argjson uid "$uid" --argjson gid "$gid" \
        --argjson installed "$installed" --arg outcome "$outcome" \
        '{schema:1,lane:"host",mode:$mode,lookupPath:$lookupPath,path:$path,version:$version,digest:$digest,uid:$uid,gid:$gid,permissions:$permissions,trueExit:0,falseExit:1,installed:$installed,outcome:$outcome}'
}
refuse_path() {
    # Encode observed paths with builtins; an unapproved jq is never executed for diagnostics.
    local found="$lookup" target="$resolved"
    [[ "$found$target" != *[$'\001'-$'\037']* ]] || refuse HostJqInvalid
    found=${found//\\/\\\\}; found=${found//\"/\\\"}
    target=${target//\\/\\\\}; target=${target//\"/\\\"}
    printf '{"schema":1,"lane":"host","mode":"%s","lookupPath":"%s","path":"%s","reason":"HostJqPathUnapproved"}\n' "$MODE" "$found" "$target"
    refuse HostJqPathUnapproved
}
qualify; result=$?
[ "$result" != 5 ] || refuse_path
if [ "$result" = 0 ]; then installed=false; outcome=existing; emit || refuse HostJqProofUnavailable; exit 0; fi
[ "$result" = 3 ] || refuse HostJqInvalid
[ "$MODE" = provision ] || refuse HostJqMissing

# All admission is before transfer. Missing is the sole install authority.
for tool in curl sha256sum install mktemp flock ln stat readlink rm uname test; do
    type -P "$tool" >/dev/null || refuse "HostJqToolMissing:$tool"
done
[ "$(uname -s)" = Linux ] || refuse HostJqUnsupportedOS
[ "$(uname -m)" = x86_64 ] || refuse HostJqUnsupportedArchitecture
command -v sudo >/dev/null && sudo -n -- stat -c '%u' / >/dev/null 2>&1 || refuse HostJqPrivilegeUnavailable
parent=${DESTINATION%/*}
safe_parent() {
    local part="$parent" meta owner group mode
    while [ "$part" != / ]; do
        [ -d "$part" ] && [ ! -L "$part" ] || return 1
        meta=$(stat -c '%u %g %a' -- "$part" 2>/dev/null) || return 1
        read -r owner group mode <<< "$meta"
        [[ "$mode" =~ ^[0-7]{3,4}$ ]] || return 1
        [ "$owner" = 0 ] && [ "$group" = 0 ] && (( (8#$mode & 0022) == 0 )) || return 1
        part=${part%/*}; [ -n "$part" ] || part=/
    done
    sudo -n -- test -w "$parent" 2>/dev/null
}
safe_destination() { [ ! -e "$DESTINATION" ] && [ ! -L "$DESTINATION" ]; }
safe_parent || refuse HostJqDestinationParentUnsafe
safe_destination || refuse HostJqDestinationExists
[ ! -L "$LOCK_ROOT" ] || refuse HostJqLockUnavailable
sudo -n -- install -d -o 0 -g 0 -m 0755 -- "$LOCK_ROOT" 2>/dev/null || refuse HostJqLockUnavailable
[ "$(stat -c '%u:%g:%a' -- "$LOCK_ROOT" 2>/dev/null)" = 0:0:755 ] || refuse HostJqLockUnavailable
# flock on the standing root-owned directory avoids a writable/symlinkable lock file.
exec 9< "$LOCK_ROOT" || refuse HostJqLockUnavailable
flock -w 60 9 || refuse HostJqLockUnavailable
qualify; result=$?
[ "$result" != 5 ] || refuse_path
if [ "$result" = 0 ]; then installed=false; outcome=existing; emit || refuse HostJqProofUnavailable; exit 0; fi
[ "$result" = 3 ] || refuse HostJqInvalid
safe_parent || refuse HostJqDestinationParentUnsafe
safe_destination || refuse HostJqDestinationExists

download_root=''
stage_root=''
cleanup() {
    if [ -n "$download_root" ]; then rm -rf -- "$download_root" >/dev/null 2>&1; fi
    if [ -n "$stage_root" ]; then sudo -n -- rm -rf -- "$stage_root" >/dev/null 2>&1; fi
}
trap cleanup EXIT
trap 'exit 2' TERM INT HUP
umask 077
download_root=$(mktemp -d) || refuse HostJqDownloadUnavailable
[ "$(stat -c '%a' -- "$download_root")" = 700 ] || refuse HostJqDownloadUnsafe
curl --fail --silent --show-error --location --connect-timeout 15 --max-time 90 \
    --output "$download_root/jq" "$URL" >/dev/null 2>&1 || refuse HostJqDownloadUnavailable
actual=$(sha256sum -- "$download_root/jq" 2>/dev/null) || refuse HostJqDigestUnavailable
[ "${actual%% *}" = "$DIGEST" ] || refuse HostJqDigestMismatch
stage_root=$(sudo -n -- mktemp -d "$parent/.antiphon-jq.XXXXXXXX" 2>/dev/null) || refuse HostJqStageUnavailable
sudo -n -- install -o 0 -g 0 -m 0755 -- "$download_root/jq" "$stage_root/jq" 2>/dev/null || refuse HostJqStageUnavailable
actual=$(sudo -n -- sha256sum -- "$stage_root/jq" 2>/dev/null) || refuse HostJqDigestUnavailable
[ "${actual%% *}" = "$DIGEST" ] || refuse HostJqDigestMismatch
[ "$(sudo -n -- stat -c '%u:%g:%a' -- "$stage_root/jq" 2>/dev/null)" = 0:0:755 ] || refuse HostJqStageUnsafe
# Hard-link publication is atomic, same-filesystem and refuses any existing leaf.
sudo -n -- ln -T -- "$stage_root/jq" "$DESTINATION" 2>/dev/null || refuse HostJqPublishUnavailable
qualify; result=$?
[ "$result" != 5 ] || refuse_path
[ "$result" != 3 ] || refuse HostJqFinalPathInvalid
[ "$result" = 0 ] || refuse HostJqInvalid
[ "$resolved" = "$DESTINATION" ] || refuse HostJqFinalPathInvalid
[ "$digest" = "$DIGEST" ] || refuse HostJqFinalDigestInvalid
[ "$version" = "$VERSION" ] || refuse HostJqFinalVersionInvalid
[ "$uid" = 0 ] && [ "$gid" = 0 ] || refuse HostJqFinalOwnerInvalid
[ "$permissions" = 755 ] || refuse HostJqFinalModeInvalid
installed=true; outcome=installed
emit || refuse HostJqProofUnavailable
