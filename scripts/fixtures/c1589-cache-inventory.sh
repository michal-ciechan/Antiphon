#!/usr/bin/env bash
# Narrow, local inventory recipient model. No host Docker or HTTP calls.
set -euo pipefail
repo="$1"
root=$(mktemp -d /tmp/c1589-inventory-XXXXXXXX)
trap 'rm -rf -- "$root"' EXIT
source <(awk '/^case_runner_cache_inventory\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$repo/scripts/c590-remote.sh")
CASE_DIR="$root"; HOST_PROJECT=antiphon-runner; TEMP_PROJECT=antiphon-runner-temp
require_lane() { [ "$1" = host ]; }
write_result() { printf 'RESULT %s %s\n' "$1" "$2"; exit "$3"; }
docker() {
    printf '%s\n' "$*" >> "$root/docker-trace"
    case "$1" in
        ps)
            [[ " $* " == *' -aq '* ]] || return 97
            if [[ "$*" == *"project=$TEMP_PROJECT"* ]]; then
                if [[ "$*" != *'service=session-runner'* ]]; then
                    case "$fault" in
                        census) return 1 ;;
                        partial-census) printf '%064d\n' 2; return 1 ;;
                        stopped-container) printf '%064d\n' 2 ;;
                    esac
                fi
            else printf '%064d\n' 1; fi ;;
        inspect)
            case "$3" in
                *'.Image'*) printf 'sha256:%064d\n' 1 ;;
                *'.State.Running'*) echo false ;;
                *'.Mounts'*) : ;;
                *) return 97 ;;
            esac ;;
        *) return 97 ;;
    esac
}
c849_status_body() {
    if [ "$1" = server2 ]; then printf '%s\n' "$main"; else printf '%s\n' "$temp"; fi
}
base='{"sessions":0,"queuedTasks":0,"runnerSessions":null,"available":false,"dispatchEligible":false,"acceptingNewWork":false,"draining":true,"redirectTo":"server2","retireWhenIdle":false,"retiredAt":null}'
main=$(printf '%s' "$base" | jq '.runnerSessions=0 | .available=true | .dispatchEligible=true | .acceptingNewWork=true | .draining=false')
check() {
    local name="$1" expected="$2" diagnosis="${3:-}" code=0
    : > "$root/docker-trace"
    (case_runner_cache_inventory) > "$root/result" 2>&1 || code=$?
    if [ "$code" != "$expected" ] || ! grep -Fxq "RESULT $([ "$expected" = 0 ] && echo true || echo false) $diagnosis" "$root/result"; then
        printf 'FAIL %s expected=%s observed=%s\n' "$name" "$expected" "$code"
        cat "$root/result"; exit 1
    fi
    if [ "$expected" = 0 ]; then
        grep -Fxq 'runner=server2-temp container=absent image=none' "$root/identities.txt"
        grep -Fxq "ps -aq --filter label=com.docker.compose.project=$TEMP_PROJECT" "$root/docker-trace"
    fi
    printf 'PASS %s\n' "$name"
}
fault=none; temp="$base"
check hold-null-inventory 0
jq -e '.retiredAt == null and .retireWhenIdle == false and .redirectTo == "server2" and .draining == true' "$root/server2-temp-status.json" >/dev/null
for shape in '.runnerSessions=0' '.runnerSessions=null | .retireWhenIdle=true | .retiredAt="2026-10-04T00:00:00Z"'; do
    temp=$(printf '%s' "$base" | jq "$shape")
    check "accepted-$shape" 0
done
for shape in '.available=true' '.dispatchEligible=true' '.acceptingNewWork=true' '.sessions=1' '.queuedTasks=1' '.runnerSessions=1' '.sessions=null' '.queuedTasks=null' 'del(.runnerSessions)' '.runnerSessions="0"' '.draining=false' '.redirectTo="other"' 'del(.redirectTo)' '.retireWhenIdle=true' 'del(.retireWhenIdle)' 'del(.retiredAt)' '.retiredAt=false'; do
    temp=$(printf '%s' "$base" | jq "$shape")
    diagnosis=CacheInventoryRunnerMissing
    case "$shape" in '.sessions=null'|'.queuedTasks=null'|'del(.runnerSessions)'|'.runnerSessions="0"'|'del(.retiredAt)') diagnosis=CacheInventoryStatusInvalid ;; esac
    check "refused-$shape" 2 "$diagnosis"
done
temp="$base"
for fault in census partial-census stopped-container; do
    diagnosis=CacheInventoryLookupFailed
    [ "$fault" != stopped-container ] || diagnosis=CacheInventoryRunnerMissing
    check "refused-$fault" 2 "$diagnosis"
done
# Also exercise the real PC-31 producer and expected-red recorder when supplied.
if [ "${2:-}" = controls ]; then
    source <(awk '/^c849_fixture_inventory_fault\(\) \(/ { active=1 } active { print } active && /^\)$/ { active=0 }' "$repo/scripts/c590-remote.sh")
    source <(awk '/^c849_fixture_control\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$repo/scripts/c590-remote.sh")
    for fault in hold hold-null; do
        (c849_fixture_inventory_fault "$fault") > "$root/result" 2>&1 || { cat "$root/result"; exit 1; }
        printf 'PASS control-positive-%s\n' "$fault"
    done
    for fault in live eligible accepting sessions queued runner unknown routing drain retirement census partial-census stopped-container; do
        diagnosis=CacheInventoryRunnerMissing
        case "$fault" in census|partial-census) diagnosis=CacheInventoryLookupFailed ;; unknown) diagnosis=CacheInventoryStatusInvalid ;; esac
        c849_fixture_control PC-31 "$diagnosis" c849_fixture_inventory_fault "hold-$fault"
        printf 'PASS control-negative-%s\n' "$fault"
    done
    [ "$(wc -l < "$root/fixture-control-variants.txt")" = 13 ]
fi
