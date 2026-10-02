#!/usr/bin/env bash
# Offline reader slices from the production script, never source its dispatch body.
# Docker/privilege/status boundaries are private fakes; marker validation is real.
set -u
remote="$1"; reader="$2"; marker="$3"; seed_available="$4"; tag_available="$5"; variant="${6:-valid}"
root="$(mktemp -d /tmp/c973-marker-XXXXXXXX)"
printf 'C973_TEST_ROOT=%s\n' "$root"
trap '[[ "$root" == /tmp/c973-marker-???????? && -d "$root" ]] && rm -rf -- "$root"' EXIT
SERVER2_ROOT="$root/server2"; CASE_DIR="$root/case"; RUN=c973fixture
HOST_PROJECT=antiphon-runner; TEMP_PROJECT=antiphon-runner-temp
C849_PACKAGES=antiphon-runner-cache-nuget-packages
C849_SCRATCH=antiphon-runner-cache-nuget-scratch
C849_NPM=antiphon-runner-cache-npm-content
C849_READY="$SERVER2_ROOT/cache/seed-accepted"
SHA=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
CASE=runner-cache-seed; C590_RUNNER_ID=server2
if [[ "$reader" == c849_image:* ]]; then CASE="${reader#*:}"; reader=c849_image; fi
current_image=sha256:1111111111111111111111111111111111111111111111111111111111111111
seed_image=sha256:09fe5601ba1d88a8658e7d78d1818a16b15c1cf1d39cc859601fcdaedc364bc1
mkdir -p "$SERVER2_ROOT/cache" "$CASE_DIR"
for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
    mkdir -p "$root/volumes/$name/_data"
done
cp -- "$marker" "$C849_READY"
case "$variant" in
    valid) ;;
    missing) rm -- "$C849_READY" ;;
    malformed) printf 'payload-sha256=bad\n' >> "$C849_READY" ;;
    duplicate) printf 'kind=cold\n' >> "$C849_READY" ;;
    foreign-marker) sed -i 's/^packages=.*/packages=foreign-volume/' "$C849_READY" ;;
    malformed-image) sed -i 's/^image=.*/image=not-an-image/' "$C849_READY" ;;
    malformed-source) sed -i 's/^source-sha=.*/source-sha=not-a-sha/' "$C849_READY" ;;
    symlink) mv "$C849_READY" "$root/target"; ln -s "$root/target" "$C849_READY" ;;
    foreign-volume|unwritable|foreign-main|no-main|no-image) ;;
    saved) C590_SAVED_DONOR=/fixture/saved ;;
    full)
        mkdir -p "$SERVER2_ROOT/cache/recovery-fixture/packages" "$SERVER2_ROOT/cache/recovery-fixture/npm"
        printf 'image=%s\npayload-sha256=%064d\nrecovery=%s\n' "$seed_image" 0 "$SERVER2_ROOT/cache/recovery-fixture" > "$C849_READY" ;;
    *) exit 2 ;;
esac
extract() {
    awk -v name="$1" '$0 == name "() {" { active=1 } active { print } active && /^}/ { exit }' "$remote"
}
require_lane() { [ "$1" = host ] || exit 2; }
c849_lock() { :; } # lock primitive; all roots are private to this invocation
write_result() {
    if [ "$1" = true ] && [ -f "$CASE_DIR/rollback.txt" ]; then cat "$CASE_DIR/rollback.txt"; fi
    printf 'DIAGNOSIS=%s\n' "$2"
    printf '{"accepted":%s,"diagnosis":"%s","exit":%s}\n' "$1" "$2" "$3"
    exit "$3"
}
sudo() {
    [ "$1" = -n ] && shift
    if [ "$1" = stat ]; then
        [ "$variant" = unwritable ] && printf '1654:1654:755\n' || printf '1654:1654:700\n'
    else "$@"; fi
}
compose_host() { [ "$variant" = no-main ] || printf 'main\n'; }
c849_status_body() {
    printf '{"buildVersion":"%s","dispatchEligible":true,"acceptingNewWork":true,"draining":false,"sessions":0,"runnerSessions":0,"queuedTasks":0}' "$SHA"
}
docker() {
    printf 'DOCKER %s\n' "$*" >> "$CASE_DIR/docker.txt"
    local name role owner project mount code arg i image
    case "$1:${2:-}" in
        image:inspect)
            image="${@: -1}"
            [ "$variant" != no-image ] || return 1
            if { [ "$image" = "antiphon-server2/session-testing:${SHA:0:12}" ] && [ "$tag_available" = 1 ]; } ||
                [ "$image" = "$current_image" ] || { [ "$image" = "$seed_image" ] && [ "$seed_available" = 1 ]; }; then
                [[ "$*" == *'{{.Id}}'* ]] && printf '%s\n' "$current_image"
                return 0
            fi
            return 1 ;;
        info:*) printf '%s\n' "$root" ;;
        volume:ls) printf '%s\n' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" ;;
        volume:inspect)
            name="${@: -1}"
            # Retired verification checks that temp private volumes are absent.
            [[ "$name" == "$TEMP_PROJECT"_* ]] && return 1
            [[ "$name" == "$HOST_PROJECT"_* ]] && return 0
            role=nuget-packages; [ "$name" = "$C849_SCRATCH" ] && role=nuget-scratch
            [ "$name" = "$C849_NPM" ] && role=npm-content
            owner=server2-runner; [ "$variant" = foreign-volume ] && owner=foreign
            if [ "${3:-}" = -f ]; then
                case "$4" in
                    *'.Driver'*) printf 'local\n' ;;
                    *'.Options'*) printf '{}\n' ;;
                    *'io.antiphon.owner'*) printf '%s\n' "$owner" ;;
                    *'io.antiphon.cache-schema'*) printf '1\n' ;;
                    *'io.antiphon.cache-role'*) printf '%s\n' "$role" ;;
                    *) return 2 ;;
                esac
            else
                printf '[{"Name":"%s","Driver":"local","Options":{},"Mountpoint":"%s/volumes/%s/_data","Labels":{"io.antiphon.owner":"%s","io.antiphon.cache-schema":"1","io.antiphon.cache-role":"%s"}}]\n' "$name" "$root" "$name" "$owner" "$role"
            fi ;;
        inspect:*)
            case "$*" in
                *'{{.Image}}'*) printf '%s\n' "$current_image" ;;
                *'com.docker.compose.project'*)
                    [ "$variant" = foreign-main ] && printf 'foreign\n' || printf '%s\n' "$HOST_PROJECT" ;;
                *'.Mounts'*)
                    printf 'volume %s /home/app/.nuget/packages true\nvolume %s /var/cache/antiphon/nuget-scratch true\nvolume %s /home/app/.npm/_cacache true\n' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"
                    for mount in runner-tmp:/tmp work:/work runner-state:/state dind-data:/var/lib/docker; do
                        printf 'volume %s_%s %s true\n' "$HOST_PROJECT" "${mount%%:*}" "${mount#*:}"
                    done ;;
                *) return 2 ;;
            esac ;;
        ps:*) [[ "$*" == *"com.docker.compose.project=$TEMP_PROJECT"* ]] || printf 'main\n' ;;
        exec:*) [[ "$*" == *'stat -c %a /tmp'* ]] && printf '1777\n' || return 2 ;;
        run:*)
            mount=''; code=''
            for ((i=1;i<=$#;i++)); do
                arg="${!i}"
                if [ "$arg" = --mount ]; then i=$((i+1)); mount="${!i}"; fi
                if [ "$arg" = -c ]; then i=$((i+1)); code="${!i}"; fi
            done
            if [[ "$*" == *'--entrypoint sha256sum'* ]]; then printf '%064d  apphost\n' 0; return 0; fi
            name="${mount#*source=}"; name="${name%%,*}"
            if [[ "$code" == *'stat -c'* ]]; then
                [ "$variant" = unwritable ] && printf '1654:1654:755\n' || printf '1654:1654:700\n'
            elif [ -n "$code" ]; then
                code="${code//\/cache/$root/volumes/$name/_data}"
                bash -c "$code" sh "$RUN" || return $?
            else return 2; fi ;;
        *) return 2 ;;
    esac
}
for function in c849_image c849_empty_volume c849_volume c849_prepare c849_cold_volume_facts c849_require_ready c849_assert_mounts c849_seed case_verify_runner_caches case_verify_runner_caches_retired; do
    eval "$(extract "$function")"
done
# Cold reuse must not consult a donor or run smoke. Unexpected work is a refusal.
c849_optional_donor() { printf 'unexpected-donor\n' >&2; return 2; }
c849_no_temp_containers() { :; }
c849_smoke() { write_result false UnexpectedSmoke 2; }
case "$reader" in
    case_verify_runner_caches|case_verify_runner_caches_retired|c849_seed) "$reader" ;;
    case_deploy_parent|case_deploy_temp_runner|case_retire_temp_runner)
        # Execute this caller's exact prepare/ready reader slice, without its deployment.
        slice="$(extract "$reader" | awk '/^    c849_prepare / || /^    c849_require_ready / {print}')"
        eval "$slice"
        write_result true '' 0 ;;
    c849_image)
        helper="$(c849_image)" || exit $?
        printf 'HELPER=%s\n' "$helper"
        write_result true '' 0 ;;
    full-required)
        c849_require_ready
        write_result true '' 0 ;;
    *) exit 2 ;;
esac
