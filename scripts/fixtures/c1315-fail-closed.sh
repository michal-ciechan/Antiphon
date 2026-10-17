#!/usr/bin/env bash
# CARD-1315. Model the four c590-remote.sh fail-closed leftovers against the real functions.
# sha256sum and docker are stubs. No daemon.
set -euo pipefail
mode="${1:?}"
repo="${2:?}"
root=$(mktemp -d /tmp/c1315-XXXXXXXX)
trap 'rm -rf -- "$root"' EXIT
remote="$repo/scripts/c590-remote.sh"
awk '/^(c849_[a-z_]+|case_verify_runner_caches_retired)\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote" > "$root/functions"
# shellcheck disable=SC1091
source "$root/functions"

RUN=c849aaaaaaaaaaaaaaaa0
SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
HOST_PROJECT=antiphon-runner
TEMP_PROJECT=antiphon-runner-temp
SERVER2_ROOT="$root/server2"
CASE_DIR="$root/case"
CHECKOUT="$repo"
C849_READY="$SERVER2_ROOT/cache/seed-accepted"
C849_PACKAGES="c849${RUN}-packages"
C849_SCRATCH="c849${RUN}-scratch"
C849_NPM="c849${RUN}-npm"
WROTE=0
LANE=host
mkdir -p "$SERVER2_ROOT/cache" "$CASE_DIR"
: > "$CASE_DIR/.fixture-volumes"
: > "$CASE_DIR/fixture-groups.txt"

write_result() {
    local diagnosis="$2" code="$3"
    if [ "${WROTE:-0}" = 1 ]; then exit "$code"; fi
    WROTE=1
    printf 'DIAGNOSIS=%s\n' "$diagnosis"
    exit "$code"
}
fail() {
    printf 'FAIL %s\n' "$*"
    exit 1
}
require_lane() { [ "$1" = host ]; }

run_case() {
    # A status check on the substitution disables errexit for the whole child.
    # Turn it back on inside, or a failing local assignment never aborts.
    local actual=0
    set +e
    CASE_OUT=$(
        set -euo pipefail
        WROTE=0
        trap 'ec=$?; if [ "${WROTE:-0}" != 1 ] && [ "$ec" != 0 ]; then write_result false "UnhandledExit $ec" "$ec"; fi' EXIT
        "$@"
    )
    actual=$?
    set -e
    CASE_CODE=$actual
    printf 'CASE code=%s\n%s\n' "$CASE_CODE" "$CASE_OUT"
}

want() {
    local label="$1" code="$2" diagnosis="$3"
    [ "$CASE_CODE" = "$code" ] && printf '%s\n' "$CASE_OUT" | grep -Fxq "DIAGNOSIS=$diagnosis" \
        || fail "$label want $diagnosis/$code got ${CASE_CODE:-none}"
}

helper_image="sha256:$(printf '%064d' 1)"
c849_image() { printf '%s\n' "$helper_image"; }

case "$mode" in
seed-tool-hash)
    c849_payload_own() { return 0; }
    c849_payload_call() { return 0; }
    c849_fixture_control() { return 0; }
    c849_fixture_npm_work() { return 0; }
    c849_fixture_launch() { printf '%s\n' 'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'; }
    c849_fixture_inner() {
        mkdir -p -- "$(dirname "$C849_READY")"
        printf 'ok\n' > "$CASE_DIR/smoke-summary.txt"
        printf 'npm-integrity=passed\n' > "$CASE_DIR/npm-integrity.txt"
        printf 'recovery=%s\n' "$SERVER2_ROOT/recovery" > "$C849_READY"
        return 0
    }
    c849_payload_fs() {
        shift
        case "$1" in
            *sha256sum*) return 1 ;;
        esac
        return 0
    }
    docker() {
        if [ "${1:-}" = volume ] && [ "${2:-}" = inspect ]; then
            printf 'No such volume: %s\n' "${*: -1}" >&2
            return 1
        fi
        return 0
    }
    run_case c849_fixture_seed
    want seed-tool-hash 2 FixtureSeedPayloadChanged
    printf '%s\n' "$CASE_OUT" | grep -Fq 'PASS F-3' && fail seed-tool-hash-published-pass
    [ -f "$CASE_DIR/fixture-groups.txt" ] && grep -Fxq 'PASS F-3' "$CASE_DIR/fixture-groups.txt" \
        && fail seed-tool-hash-group
    printf 'PASS seed-tool-hash-named\n'
    ;;
prune-manifest-hash)
    c849_fixture_recovery_copy() { return 0; }
    c849_payload_call() { return 0; }
    c849_payload_fs() {
        shift
        case "$1" in
            *sha256sum*) return 1 ;;
        esac
        return 0
    }
    docker() { return 0; }
    ready="$SERVER2_ROOT/cache/fixture-prune-ready"
    rm -f -- "$ready"
    run_case c849_fixture_prune
    want prune-manifest-hash 2 CacheSeedMarkerInvalid
    [ ! -e "$ready" ] && [ ! -L "$ready" ] || fail prune-manifest-hash-marker
    printf '%s\n' "$CASE_OUT" | grep -Fq 'PASS F-8' && fail prune-manifest-hash-published-pass
    printf 'PASS prune-manifest-hash-named\n'
    ;;
namespace-inspect)
    c849_fixture_work_mount() { printf 'type=bind,source=%s,target=/fixture\n' "$CASE_DIR"; }
    c849_fixture_cache_mount_ok() { return 0; }
    c849_fixture_helper_exec() { return 0; }
    c849_prepare() { return 0; }
    c849_volume() { write_result false CacheVolumeMissing 2; }
    docker() {
        if [ "${1:-}" = volume ] && [ "${2:-}" = inspect ]; then
            case "$INSPECT_MODE" in
                present) return 0 ;;
                absent)
                    printf 'No such volume: %s\n' "${*: -1}" >&2
                    return 1 ;;
                *)
                    printf 'Cannot connect to the Docker daemon\n' >&2
                    return 1 ;;
            esac
        fi
        return 1
    }
    probe() {
        local fn="$1" name="$2"
        : > "$CASE_DIR/.fixture-volumes"
        : > "$CASE_DIR/fixture-groups.txt"
        run_case "$fn"
        if grep -Fxq -- "$name" "$CASE_DIR/.fixture-volumes"; then
            fail "$fn registered $name"
        fi
    }
    INSPECT_MODE=absent
    : > "$CASE_DIR/.fixture-volumes"
    run_case c849_fixture_prepare
    grep -Fxq -- "c849${RUN}-packages" "$CASE_DIR/.fixture-volumes" || fail prepare-absent-not-registered
    printf '%s\n' "$CASE_OUT" | grep -Fxq 'DIAGNOSIS=FixtureVolumeInspectFailed' && fail prepare-absent-unknown
    printf '%s\n' "$CASE_OUT" | grep -Fxq 'DIAGNOSIS=FixtureNamespaceOccupied' && fail prepare-absent-occupied

    INSPECT_MODE=present
    : > "$CASE_DIR/.fixture-volumes"
    run_case c849_fixture_prepare
    want prepare-present 2 FixtureNamespaceOccupied
    grep -Fxq -- "c849${RUN}-packages" "$CASE_DIR/.fixture-volumes" && fail prepare-present-registered

    INSPECT_MODE=daemon
    probe c849_fixture_prepare "c849${RUN}-packages"
    want prepare-daemon 2 FixtureVolumeInspectFailed
    probe c849_fixture_seed "c849${RUN}seed-packages"
    want seed-daemon 2 FixtureVolumeInspectFailed
    probe c849_fixture_nuget_race "c849${RUN}race-packages"
    want race-daemon 2 FixtureVolumeInspectFailed
    probe c849_fixture_apphost "c849${RUN}smoke-packages"
    want apphost-daemon 2 FixtureVolumeInspectFailed
    probe c849_fixture_npm "c849${RUN}npmcache-npm"
    want npm-daemon 2 FixtureVolumeInspectFailed
    probe c849_fixture_contract_controls "c849${RUN}warm-packages"
    want contract-daemon 2 FixtureVolumeInspectFailed
    printf 'PASS namespace-inspect-not-free\n'
    ;;
retired-inspect)
    c849_prepare() { return 0; }
    c849_require_ready() { C849_KIND=full; return 0; }
    c849_assert_mounts() { return 0; }
    c849_smoke() { return 0; }
    c849_contract_receipt() { return 0; }
    c849_status_body() {
        printf '{"buildVersion":"%s","dispatchEligible":true,"acceptingNewWork":true,"draining":false,"sessions":0,"runnerSessions":0,"queuedTasks":0}' "$SHA"
    }
    printf 'image=%s\n' "$helper_image" > "$C849_READY"
    docker() {
        printf 'DOCKER %s\n' "$*" >> "$CASE_DIR/docker.txt"
        case "${1:-}" in
            ps)
                if [[ "$*" == *"project=$TEMP_PROJECT"* ]]; then
                    if [ "$PS_MODE" = fail ]; then
                        printf 'Cannot connect to the Docker daemon\n' >&2
                        return 1
                    fi
                    return 0
                fi
                printf 'abc\n'
                return 0 ;;
            volume)
                [ "${2:-}" = inspect ] || return 1
                local name="${*: -1}"
                case "$VOL_MODE" in
                    daemon)
                        printf 'Cannot connect to the Docker daemon\n' >&2
                        return 1 ;;
                    missing)
                        printf 'No such volume: %s\n' "$name" >&2
                        return 1 ;;
                    temp-present)
                        if [ "$name" = "${TEMP_PROJECT}_runner-tmp" ]; then return 0; fi
                        printf 'No such volume: %s\n' "$name" >&2
                        return 1 ;;
                    healthy)
                        if [ "$name" = "${HOST_PROJECT}_runner-tmp" ]; then return 0; fi
                        printf 'No such volume: %s\n' "$name" >&2
                        return 1 ;;
                    *) return 1 ;;
                esac ;;
            image) return 0 ;;
            exec) printf '1777\n'; return 0 ;;
            *) return 1 ;;
        esac
    }
    PS_MODE=ok
    VOL_MODE=missing
    run_case case_verify_runner_caches_retired
    want retired-main-missing 2 MainTmpMissing

    PS_MODE=ok
    VOL_MODE=temp-present
    run_case case_verify_runner_caches_retired
    want retired-temp-present 2 TempPrivateVolumeRemains

    PS_MODE=ok
    VOL_MODE=daemon
    : > "$CASE_DIR/docker.txt"
    run_case case_verify_runner_caches_retired
    want retired-daemon 2 CacheVolumeInspectFailed
    printf '%s\n' "$CASE_OUT" | grep -Fq 'MainTmpMissing' && fail retired-daemon-absence-token

    PS_MODE=fail
    VOL_MODE=healthy
    run_case case_verify_runner_caches_retired
    want retired-ps 2 CacheDonorLookupFailed
    printf '%s\n' "$CASE_OUT" | grep -Fq 'TempContainerCensusUnavailable' && fail retired-ps-census-token
    printf '%s\n' "$CASE_OUT" | grep -Fq 'RecycleVolumeCensusUnknown' && fail retired-ps-census-token
    printf 'PASS retired-inspect-not-absence\n'
    ;;
*)
    fail "unknown mode $mode"
    ;;
esac
