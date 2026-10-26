#!/usr/bin/env bash
# Local filesystem recipients with remapped Docker/HTTP/uid boundaries. Never source dispatch.
set -eu
mode="$1"; repo="$2"
root=$(mktemp -d /tmp/c913-seed-XXXXXXXX)
trap 'rm -rf -- "$root"' EXIT
export C913_ROOT="$root"
remote="$repo/scripts/c590-remote.sh"
awk '/^(c849_[a-z_]+|case_runner_cache_inventory)\(\) \{/ { active=1 } active { print } active && /^}$/ { active=0 }' "$remote" > "$root/functions"
source "$root/functions"
source <(awk '/^c849_fixture_inventory_fault\(\) \(/ { active=1 } active { print } active && /^\)$/ { active=0 }' "$remote")
SERVER2_ROOT="$root/server2"; CASE_DIR="$root/case"; CHECKOUT="$repo"; ROOT="$repo"
C849_READY="$SERVER2_ROOT/cache/seed-accepted"
C849_PACKAGES=antiphon-runner-cache-nuget-packages
C849_SCRATCH=antiphon-runner-cache-nuget-scratch
C849_NPM=antiphon-runner-cache-npm-content
HOST_PROJECT=antiphon-runner; TEMP_PROJECT=antiphon-runner-temp
SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; RUN=c849aaaaaaaaaaaaaaaa0
CASE=runner-cache-seed; C604_SERVER_ORIGIN=https://example.invalid
image_id=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
donor_id=cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc
FAULT=none; SOURCE=live; CRASH=''
C913_RECOVERY="$SERVER2_ROOT/cache/recovery-$RUN"
mkdir -p "$SERVER2_ROOT/cache" "$CASE_DIR" "$root/bin" "$root/donor/packages" "$root/donor/npm"
c849_fixture_case_bind
for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do mkdir -m 700 -p "$root/volumes/$name/_data"; done
printf outside > "$root/outside"
for command in dotnet npm curl pwsh docker; do
    printf '#!/bin/sh\nprintf "DENIED %%s\\n" "$0" >> "$C913_ROOT/denied"\nexit 93\n' > "$root/bin/$command"
    chmod 755 "$root/bin/$command"
done
# Preserve the actual interpreter for the admitted saved-import child; every other invocation is denied.
pwsh_path=$(command -v pwsh)
export PATH="$root/bin:$PATH"
# Canonical design witnesses annotate the existing predicate that failed.
# This changes diagnostics only; no predicate or refusal is bypassed.
fail() {
    local witness="$1"
    case "$mode:$1" in
      tree:empty-refused|tree:incomplete-refused|tree:metadata-empty-refused) witness=empty-full-refused ;;
      tree:incomplete-pruned-only-in-stage) witness=incomplete-stage-only ;;
      tree:version-file-refused) witness=invalid-layout-refused ;;
      tree:fifo-refused) witness=special-entry-refused ;;
      seed:corrupt-import-no-publication) witness=package-import-corruption-no-marker ;;
      seed:corrupt-npm-import-no-publication) witness=npm-import-corruption-no-marker ;;
      seed:smoke-no-publication) witness=smoke-failure-no-marker ;;
      seed:recovery-save-no-publication) witness=recovery-save-failure-no-marker ;;
      seed:restart-no-publication) witness=restart-failure-no-marker ;;
      seed:donor-id-no-publication) witness=donor-id-change-no-marker ;;
      seed:reconnect-no-publication) witness=reconnect-failure-no-marker ;;
      seed:publication-no-publication|seed:marker-write-no-publication) witness=publication-failure-no-partial-marker ;;
      seed:busy*-no-publication|seed:busy*-no-stop) witness=busy-donor-no-stop ;;
      seed:unknown*-no-publication|seed:unknown*-no-stop|seed:process-error-no-publication|seed:process-error-no-stop) witness=unknown-process-no-stop ;;
      seed:writer-no-publication) witness=writer-no-stop ;;
      seed:saved-temp-present-no-import) witness=saved-temp-present-no-copy ;;
      ready:absolute-record-refused|ready:type-record-refused|ready:truncated-record-refused|ready:unsorted-record-refused) witness=unsafe-record-refused ;;
      ready:schema-value-refused) witness=unknown-schema-refused ;;
      ready:marker-symlink) witness=symlink-marker-refused ;;
      ready:digest-refused|ready:manifest-sha256-value-refused) witness=manifest-digest-refused ;;
      ready:recovery-bytes-changed|ready:recovery-execute-changed) witness=recovery-bytes-refused ;;
      ready:image-missing-refused) witness=recovery-image-refused ;;
      ready:packages-value-refused|ready:scratch-value-refused|ready:npm-value-refused) witness=marker-volume-refused ;;
      ready:mutable-cache-churn) witness=cache-churn-accepted ;;
      *:image-missing-no-delete) witness=missing-image-no-delete ;;
      *:source-no-delete) witness=stale-source-no-delete ;;
      *:volume-facts-no-delete) witness=changed-volume-no-delete ;;
      *:second-target-no-delete) witness=second-target-invalid-no-delete ;;
      *:refill-no-success) witness=refill-failure-no-success ;;
      *:refill-receipt-no-success) witness=refill-receipt-required ;;
      *:budget-no-success) witness=over-budget-no-success ;;
      maintenance:busy-no-delete|maintenance:unknown-no-delete) witness=maintenance-busy-no-delete ;;
      maintenance:writer-no-delete|maintenance:process-error-no-delete) witness=maintenance-process-no-delete ;;
      maintenance:attachment-no-delete) witness=maintenance-attachment-no-delete ;;
      maintenance:broker-busy-no-delete|maintenance:broker-error-no-delete) witness=maintenance-broker-no-delete ;;
      inventory:inventory-census-error-refused|inventory:inventory-partial-census-refused) witness=failed-census-refused ;;
      inventory:inventory-status-*-refused|inventory:inventory-absent-unretired-refused) witness=unknown-status-refused ;;
      inventory:inventory-duplicate-refused|inventory:inventory-retired-running-refused) witness=contradictory-container-refused ;;
      inventory:inventory-inspect-error-refused) witness=failed-inspect-refused ;;
      ready:mixed-marker) if [ "$extra" = schema=3 ]; then witness=duplicate-marker-refused; else witness=mixed-marker-refused; fi ;;
      seed:saved-smoke-failure-no-marker)
        case "$FAULT" in
          saved-temp-present) witness=saved-temp-present-no-copy ;;
          saved-late-busy) witness=saved-late-busy-no-import ;;
          saved-late-attachment) witness=saved-late-attachment-no-import ;;
        esac ;;
    esac
    printf 'FAIL %s [case=%s]\n' "$witness" "$*"
    [ ! -e "$root/out" ] || cat "$root/out"
    exit 1
}
barrier() {
    builtin printf 'barrier %s\n' "$1" >> "$C913_ROOT/trace"
    if [ "${CRASH:-}" = "$1" ]; then kill -KILL "$C913_CHILD"; exit 137; fi
}
printf() {
    if [[ "${1:-}" == 'schema=3'* ]] && [ -n "${C913_CHILD:-}" ]; then
        barrier marker-write
        [ "$FAULT" != marker-write ] || return 1
        builtin printf "$@"
        barrier after-marker-write
    else builtin printf "$@"; fi
}
pass() { printf 'PASS %s\n' "$1"; }
require_lane() { [ "$1" = host ]; }
write_result() { printf 'RESULT %s %s\n' "$1" "$2"; exit "$3"; }
c849_lock() { :; }
c849_prepare() { :; } # Mount/initialization has its separate A1/A2 tests.
c849_smoke() {
    if [ "$2" = fixture-masked ]; then
        printf 'ImagePackMissing:%s\n' "$(cat "$C913_ROOT/masked-pack")" > "$CASE_DIR/smoke.txt"
        write_result false CacheSmokeFailed 2
    fi
    barrier before-smoke
    printf 'smoke\n' >> "$C913_ROOT/trace"
    [ "$FAULT" != smoke ] || write_result false CacheSmokeFailed 2
    if [ "$FAULT" = main-smoke ] && [ "$2" = server2 ]; then write_result false CacheSmokeFailed 2; fi
    printf 'C849_SMOKE runner=%s uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n' "$2" >> "$CASE_DIR/smoke-summary.txt"
    barrier after-smoke
}
c849_status_body() {
    local runner="$1" sessions=0 running=0 queued=0 retired=null available=true
    if [ "$runner" = server2-temp ] && { [ "$SOURCE" != live ] || [ "$FAULT" = retired-running ] || [ "$FAULT" = inspect-error ]; }; then retired='"2026-10-04T00:00:00Z"'; available=false; fi
    if [ "$runner" = server2-temp ]; then
      [ "$FAULT" != status-error ] || return 1
      if [ "$FAULT" = status-omitted ]; then printf '{}'; return; fi
      if [ "$FAULT" = status-malformed ]; then printf '{"sessions":"unknown"}'; return; fi
    fi
    [ "$FAULT" != busy ] || sessions=1
    if [ "$FAULT" = busy-main ] && [ "$runner" = server2 ]; then sessions=1; fi
    [ "$FAULT" != busy-runner ] || running=1
    [ "$FAULT" != busy-queue ] || queued=1
    [ "$FAULT" != unknown-sessions ] || sessions=null
    [ "$FAULT" != unknown-runner ] || running=null
    [ "$FAULT" != unknown ] || queued=null
    if [ "$FAULT" = saved-late-busy ] && grep -q '^barrier after-saved-copy$' "$root/trace"; then sessions=1; fi
    [ "$FAULT" != absent-unretired ] || retired=null
    if [ "$FAULT" = reconnect ] && grep -q '^restart$' "$root/trace"; then available=false; fi
    local body
    body=$(printf '{"sessions":%s,"runnerSessions":%s,"queuedTasks":%s,"draining":true,"acceptingNewWork":false,"redirectTo":"server2","retireWhenIdle":true,"dispatchEligible":%s,"available":%s,"retiredAt":%s}' "$sessions" "$running" "$queued" "$available" "$available" "$retired")
    if [ "$runner" = server2-temp ] && [ "$FAULT" = status-no-retired ]; then printf '%s' "$body" | jq 'del(.retiredAt)'; else printf '%s' "$body"; fi
    if [ "${phase:-}" = reconnected ] && grep -q '^restart$' "$root/trace"; then barrier reconnect; fi
}
sudo() {
    [ "$1" != -n ] || shift
    if [ "$1" = du ] && [ "$FAULT" = preview-size ]; then printf '123\tpartial\n'; return 45; fi
    case "$1" in
        install) mkdir -p "${@: -1}" ;;
        stat) [ "$2" = -c ] && [ "$3" = '%u:%g:%a' ] && { printf '1654:1654:700\n'; return; }; command "$@" ;;
        df) printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 999999999 0 999999999 0%% /\n' ;;
        *) command "$@" ;;
    esac
}
chown() { :; }
stat() {
    if [ "$1" = -c ] && [ "$2" = '%u:%g:%a' ]; then printf '1654:1654:%s\n' "$(command stat -c %a "${@: -1}")"
    else command stat "$@"; fi
}
export -f chown stat
compose_host() { printf 'main\n'; }
sleep() { :; } # injected reconnect state is constant; do not wait on a synthetic status
findmnt() { printf '/\n'; [ "$FAULT" != nested-mount ] || printf '%s/child\n' "$root/volumes/$C849_SCRATCH/_data"; }
df() { printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 999999999 0 999999999 0%% /\n'; }
docker() {
    local root="$C913_ROOT"
    printf '%s\n' "$*" >> "$root/docker-trace"
    local name role format arg entrypoint='' code='' target source payload='' i j
    local -a mappings=() script_args=()
    case "$1:${2:-}" in
      container:inspect)
        name="${@: -1}"
        if { [ "$name" = dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd ] && [ -f "$root/seed-helper" ]; } \
            || { [ "$name" = "$donor_id" ] && [ -f "$root/donor-project" ]; }; then return 0; fi
        printf 'Error: No such container: %s\n' "$name" >&2; return 1 ;;
      image:inspect) [ "$FAULT" != image-missing ] || return 1; printf '%s\n' "$image_id" ;;
      info:*) printf '%s\n' "$root" ;;
      volume:ls) printf '%s\n' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" ;;
      volume:create) name="${@: -1}"; mkdir -p "$root/volumes/$name/_data"; echo "$name" ;;
      volume:inspect)
        name="${@: -1}"
        # Real `docker volume inspect` prints [] on stdout and this daemon line on stderr.
        # A bare exit 1 is unknown to c849_fixture_resource_state, not absence.
        if [ ! -d "$root/volumes/$name/_data" ]; then
          printf '[]\n'
          printf 'Error response from daemon: get %s: no such volume\n' "$name" >&2
          return 1
        fi
        role=nuget-packages
        [ "$name" != "$C849_SCRATCH" ] || role=nuget-scratch
        [ "$name" != "$C849_NPM" ] || role=npm-content
        if [ "${3:-}" = -f ]; then
          case "$4" in
            *'.Driver'*) echo local ;;
            *'.Options'*) echo '{}' ;;
            *'io.antiphon.owner'*) if [ "$FAULT" = foreign-source-cache ]; then echo foreign; else echo server2-runner; fi ;;
            *'io.antiphon.cache-schema'*) echo 1 ;;
            *'io.antiphon.cache-role'*) echo "$role" ;;
            *'io.antiphon.fixture-run'*) echo "$RUN" ;;
            *'.Mountpoint'*) echo "$root/volumes/$name/_data" ;;
            *) return 94 ;;
          esac
        else
          printf '[{"Name":"%s","Driver":"local","Options":{},"Mountpoint":"%s/volumes/%s/_data","Labels":{"io.antiphon.owner":"server2-runner","io.antiphon.cache-schema":"1","io.antiphon.cache-role":"%s"}}]\n' "$name" "$root" "$name" "$role"
        fi ;;
      ps:*)
        if [[ "$*" == *"project=$TEMP_PROJECT"* ]]; then
          [ "$FAULT" != census-error ] || return 1
          if [ "$FAULT" = partial-census ]; then echo "$donor_id"; return 1; fi
        fi
        if [[ "$*" == *volume=* ]]; then
          if [ "$FAULT" = attachment ] || { [ "$FAULT" = saved-late-attachment ] && grep -q '^barrier after-npm-verify$' "$root/trace"; }; then echo foreign; fi
          return 0
        fi
        if [[ "$*" == *"project=$TEMP_PROJECT"* ]]; then
          if [ "$SOURCE" = live ]; then echo "$donor_id"; [ "$FAULT" != duplicate ] || echo duplicate
          elif [ "$FAULT" = saved-temp-present ] && [ "$(grep -c "project=$TEMP_PROJECT" "$root/docker-trace")" -ge 2 ]; then echo "$donor_id"; fi
        else [ "$FAULT" = main-missing ] || printf '%064d\n' 4; fi ;;
      inspect:*)
        if [ "$FAULT" = inspect-error ] && [ "${@: -1}" = "$donor_id" ]; then return 1; fi
        case "$3" in
          *'io.antiphon.owner'*) echo c849-fixture ;;
          *'io.antiphon.fixture-run'*) echo "$RUN" ;;
          *'io.antiphon.fixture-role'*) if [ "${@: -1}" = "$donor_id" ]; then echo donor; else echo consumer; fi ;;
          *'.Name'*) if [ "${@: -1}" = "$donor_id" ]; then printf '/c849-%s-donor\n' "$RUN"; else printf '/c849-seed-%s\n' "$RUN"; fi ;;
          *'.Created'*) echo '2026-10-13T00:00:00Z' ;;
          *'.Image'*) echo "$image_id" ;;
          *'.Id'*) if grep -q '^restart$' "$root/trace" 2>/dev/null; then barrier donor-id; fi; if [ "$FAULT" = donor-id ] && grep -q '^restart$' "$root/trace"; then echo changed; else echo "$donor_id"; fi ;;
          *'.State.Running'*) if [ "$FAULT" = retired-running ] && [ "${@: -1}" = "$donor_id" ]; then echo true; else echo false; fi ;;
          *'com.docker.compose.project'*) echo "$TEMP_PROJECT" ;;
          *'com.docker.compose.service'*) echo session-runner ;;
          *'.Mounts'*) echo '[]' ;;
          *) return 94 ;;
        esac ;;
      stop:*) barrier before-stop; [ "$FAULT" != stop ] || return 1; printf 'stop\n' >> "$root/trace"; barrier after-stop ;;
      start:*) barrier before-restart; [ "$FAULT" != restart ] || return 1; printf 'restart\n' >> "$root/trace"; barrier after-restart ;;
      rm:*)
        if [ "$FAULT" = helper-remove ] && [ "${@: -1}" = "c849-seed-$RUN" ]; then return 1; fi
        if [ "${@: -1}" = dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd ]; then rm -f "$root/seed-helper"; fi
        if [ "${@: -1}" = "$donor_id" ]; then rm -f "$root/donor-project"; fi
        return 0 ;;
      cp:*)
        if [[ "$2" == *'/packages/.' ]]; then source=packages; else source=npm; fi
        [ "$FAULT" != "copy-$source" ] || return 1
        if [ "$3" = - ]; then tar -C "$root/donor/$source" -cf - .; else cp -a "$root/donor/$source/." "$3/"; fi
        barrier "after-copy-$source" ;;
      exec:*)
        if [[ "$*" == *'ps -eo uid,comm'* ]]; then
          [ "$FAULT" != process-error ] || return 1
          if [ "$FAULT" = writer ]; then printf '1654 dotnet\n'; else printf '0 init\n'; fi
        elif [[ "$*" == *'ps -eo uid,args'* ]]; then [ "$FAULT" != process-error ] || return 1; if [ "$FAULT" = writer ]; then echo 1; else echo 0; fi
        elif [[ "$*" == *'rev-parse HEAD'* ]]; then echo "$SHA"
        elif [[ "$*" == *'curl '* ]]; then [ "$FAULT" != broker-error ] || return 1; if [ "$FAULT" = broker-busy ]; then printf '{"occupied":1,"leases":[{}]}'; else printf '{"occupied":0,"leases":[]}'; fi
        elif [[ "$*" == *'/bin/sh -s'* ]]; then
          cat >/dev/null
          [ "$FAULT" != refill ] || return 1
          [ "$FAULT" = refill-receipt ] || printf 'C849_REFILL restore=0 npm=0\n'
        else return 94; fi ;;
      run:*)
        for ((i=2; i<=$#; i++)); do
          arg="${!i}"
          case "$arg" in
            --cidfile) i=$((i+1)); printf 'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\n' > "${!i}"; : > "$root/seed-helper" ;;
            --entrypoint) i=$((i+1)); entrypoint="${!i}" ;;
            --mount)
              i=$((i+1)); arg="${!i}"; source="${arg#*source=}"; source="${source%%,*}"
              target="${arg#*target=}"; target="${target%%,*}"
              [[ "$arg" != type=volume,* ]] || source="$root/volumes/$source/_data"
              mappings+=("$target|$source") ;;
            -c) i=$((i+1)); code="${!i}"; for ((j=i+1; j<=$#; j++)); do script_args+=("${!j}"); done; break ;;
          esac
        done
        if [ "$entrypoint" = sleep ]; then
          for arg in "${mappings[@]}"; do
            if [[ "$arg" == /usr/share/dotnet/packs/* ]]; then name="${arg#/usr/share/dotnet/packs/}"; printf '%s\n' "${name%%/*}" > "$root/masked-pack"; fi
          done
          echo helper; return
        fi
        if [ "$entrypoint" = npm ]; then [ "$FAULT" != npm-verify ] || return 1; barrier after-npm-verify; return; fi
        if [ "$entrypoint" = pwsh ]; then
          "$pwsh_path" -NoProfile -File "$repo/scripts/c849-import-saved-donor.ps1" -Source "$C590_SAVED_DONOR" -Stage "$stage" || return $?; barrier after-saved-copy; return
        fi
        if [ "$entrypoint" = sha256sum ]; then code="sha256sum ${!#}"; fi
        if [ -z "$code" ]; then code=$(cat); if [ "${@: -2:1}" = -- ]; then script_args=("bash" "${@: -1}"); fi; fi
        if [[ "$code" == *'c849_manifest_compare /import '* ]]; then
          rm -rf -- "$root/import-view"
          mkdir -p "$root/import-view"
          cp -a "$root/volumes/$C849_PACKAGES/_data" "$root/import-view/packages"
          cp -a "$root/volumes/$C849_NPM/_data" "$root/import-view/npm"
          code="${code//\/import/$root/import-view}"
        fi
        i=0
        for arg in "${mappings[@]}"; do target="${arg%%|*}"; code="${code//"$target"/"__C913_MOUNT_${i}__"}"; i=$((i+1)); done
        i=0
        for arg in "${mappings[@]}"; do source="${arg#*|}"; code="${code//"__C913_MOUNT_${i}__"/"$source"}"; i=$((i+1)); done
        if [[ "$code" == *'cp -a '* ]]; then
          [ "$FAULT" != import ] || return 1
          if [[ "$code" == *"$root/volumes/$C849_PACKAGES/_data"* ]]; then
            [ "$FAULT" != import-packages ] || return 1
          else [ "$FAULT" != import-npm ] || return 1; fi
          printf 'import\n' >> "$root/trace"
        fi
        local -a mapped_args=()
        for arg in "${script_args[@]}"; do
          for mapping in "${mappings[@]}"; do
            target="${mapping%%|*}"
            case "$arg" in "$target"|"$target"/*) arg="${mapping#*|}${arg#"$target"}"; break ;; esac
          done
          mapped_args+=("$arg")
        done
        bash -c "$code" "${mapped_args[@]}" || return $?
        if [[ "$code" == *'cp -a '* ]]; then
          if [[ "$code" == *"$root/volumes/$C849_PACKAGES/_data"* ]]; then barrier after-import-packages; else barrier after-import-npm; fi
        fi
        if [[ "$code" == *'c849_manifest_compare '* ]]; then barrier after-comparison; fi
        if { [ "$FAULT" = corrupt-import ] || [ "$FAULT" = corrupt-npm-import ]; } && [[ "$code" == *'cp -a '* ]]; then
          if [ "$FAULT" = corrupt-import ]; then printf corrupted > "$root/volumes/$C849_PACKAGES/_data/c913.probe/1.0.0/data"
          else printf corrupted > "$root/volumes/$C849_NPM/_data/content"; fi
        fi ;;
      *) return 94 ;;
    esac
}
mv() {
    if [ "$FAULT" = recovery-save ] && [[ "$1" == *"/cache/stage-"* ]]; then return 1; fi
    if [ "$FAULT" = publication ] && [[ "$*" == *"$C849_READY.tmp-"* ]]; then return 1; fi
    local step=''
    if [[ "$*" == *"$C849_READY.tmp-"* ]]; then step=publication; barrier before-publication
    elif [[ "$1" == *"/cache/stage-"* ]]; then step=recovery; fi
    command mv "$@" || return $?
    [ -z "$step" ] || barrier "after-$step"
}
ordinary() {
    mkdir -p "$1/packages/c913.probe/1.0.0" "$1/packages/c913.tools/2.0.0" "$1/npm/empty"
    printf '{}' > "$1/packages/c913.probe/1.0.0/.nupkg.metadata"
    printf 'probe' > "$1/packages/c913.probe/1.0.0/data"
    printf '{}' > "$1/packages/c913.tools/2.0.0/.nupkg.metadata"
    printf '#!/bin/sh\n' > "$1/packages/c913.tools/2.0.0/tool"
    chmod 751 "$1/packages/c913.tools/2.0.0/tool"
    printf npm > "$1/npm/content"
}
run() { local status=0; ( export root SERVER2_ROOT C849_READY RUN FAULT CRASH; export -f mv barrier; export C913_CHILD="$BASHPID"; "$@" ) > "$root/out" 2>&1 || status=$?; printf '%s' "$status" > "$root/exit"; }
accept() { [ "$(cat "$root/exit")" = 0 ] || fail "$1"; pass "$1"; }
refuse() { [ "$(cat "$root/exit")" = 2 ] && grep -Fq "$2" "$root/out" || fail "$1"; [ "$(cat "$root/outside")" = outside ] || fail sibling-changed; pass "$1"; }
make_full_marker() {
    local recovery="$C913_RECOVERY" manifest
    mkdir -p "$recovery"; ordinary "$recovery"
    # Independent serializer used to set up readiness before the new writer exists.
    {
      printf 'c849-manifest-v1\0'
      while IFS= read -r path; do
        if [ -d "$recovery/$path" ]; then printf 'D\0%s\0000\0-\0-\0' "$path"
        else
          printf 'F\0%s\0%s\0%03o\0%s\0' "$path" "$(stat -c %s "$recovery/$path")" "$((8#$(stat -c %a "$recovery/$path") & 8#111))" "$(sha256sum "$recovery/$path" | cut -d' ' -f1)"
        fi
      done < <(cd "$recovery"; find packages npm -print | LC_ALL=C sort)
    } > "$recovery/recovery.manifest"
    manifest=$(sha256sum "$recovery/recovery.manifest" | cut -d' ' -f1)
    printf 'schema=3\nkind=full\nsource-sha=%s\nimage=%s\ndonor-type=live\ndonor=%s\ntime=2026-10-04T00:00:00Z\npackages=%s\nscratch=%s\nnpm=%s\npackage-bytes=123\nnpm-bytes=456\nrecovery=%s\nmanifest-sha256=%s\n' "$SHA" "$image_id" "$donor_id" "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" "$([ "$(c849_payload_select)" = volume ] && printf '/fixture/cache/recovery-%s' "$RUN" || printf '%s' "$recovery")" "$manifest" > "$C849_READY"
}
# The wrapper's npm/smoke tools have their own earlier slice recipients. Here Docker
# models donor process state and tar transport while F-3 and shared Seed remain real.
c913_fixture_wrapper() (
    declare -f docker | sed '1s/docker/c913_docker/' > "$root/docker-function"
    source "$root/docker-function"
    docker() {
        case "$1:${2:-}" in
            create:*)
                local previous='' arg
                for arg in "$@"; do
                    [ "$previous" != --cidfile ] || printf '%s\n' "$donor_id" > "$arg"
                    previous="$arg"
                done
                printf '%s\n' "$TEMP_PROJECT" > "$root/donor-project"
                rm -rf "$root/donor"; mkdir -p "$root/donor/packages" "$root/donor/npm"
                printf '%s\n' "$donor_id"; return ;;
            start:*) c913_docker "$@"; printf true > "$root/donor-running"; return ;;
            stop:*) c913_docker "$@"; printf false > "$root/donor-running"; return ;;
            inspect:*)
                if [[ "$3" == *'.State.Running'* ]]; then cat "$root/donor-running"; return; fi
                if [[ "$3" == *'com.docker.compose.project'* ]]; then cat "$root/donor-project"; return; fi ;;
            cp:*)
                if [[ "$2" == "c849-${RUN}-writer:/fixture/"* ]]; then
                    local path="${2#*:}"; path="${path%/.}"; path="$root/volumes/c849${RUN}-work/_data${path#/fixture}"
                    tar -C "$path" -cf - .; return
                fi ;;
            exec:*)
                if [[ "$*" == *"$donor_id mkdir"* ]]; then return; fi
                if [[ "$*" == *"$donor_id tar"* ]]; then
                    local dest="${@: -1}"
                    case "$dest" in /home/app/.nuget/packages) dest="$root/donor/packages";; *) return 94;; esac
                    tar -x -p -C "$dest"; return
                fi ;;
        esac
        c913_docker "$@"
    }
    c849_prepare() {
        local n
        for n in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do mkdir -m700 -p "$root/volumes/$n/_data"; done
    }
    c849_fixture_npm_work() { printf npm > "$root/donor/npm/content"; }
    declare -f c849_fixture_control | sed '1s/c849_fixture_control/c913_wrapper_control/' > "$root/wrapper-control"
    source "$root/wrapper-control"
    c849_fixture_control() {
        if [ "$1" = PC-16 ]; then printf busy-helper > "$CASE_DIR/.fixture-busy"; return; fi
        c913_wrapper_control "$@"
    }
    write_result() { printf '%s %s\n' "$1" "$2" >> "$root/outer-results"; printf '%s\n' "$2"; exit "$3"; }
    : > "$CASE_DIR/fixture-controls.txt"; : > "$CASE_DIR/fixture-control-variants.txt"
    c849_fixture_seed
)

# S5: controller admission and helper filesystem recipients, without a Docker daemon.
if [ "$mode" = fixture-seed ] || [ "$mode" = seed-metadata ] || [ "$mode" = volume-recovery ] || [ "$mode" = volume-prune ] || [ "$mode" = preview-volume ] || [ "$mode" = fixture-prune ] || [[ "$mode" == prune-diagnostic-* ]]; then
    mkdir -p "$CASE_DIR/fixture-controller" "$root/volumes/c849${RUN}-work/_data/cache"
    printf '%s\n' "c849${RUN}-work" > "$CASE_DIR/.fixture-volumes"
    c849_payload_own volume
    if [ "$mode" = volume-prune ]; then C913_RECOVERY="$root/volumes/c849${RUN}-work/_data/cache/recovery-$RUN"; fi
    c849_fixture_work_mount() { printf 'type=volume,source=c849%s-work,target=/fixture,volume-nocopy\n' "$RUN"; }
    c849_fixture_helper_exec() {
        local role="$1" code arg
        shift
        if [ "$1" = npm ]; then
            [ "$role" = writer ] && [ "$#" = 5 ] && [ "$2" = cache ] \
                && [ "$3" = verify ] && [ "$4" = --cache ] || return 94
            [[ "$5" == "$root/volumes/c849${RUN}-work/_data/cache"/stage-"$RUN"-*/npm-cache ]] || return 94
            [ "$FAULT" != npm-verify ] || return 1
            barrier after-npm-verify
            return
        fi
        if [ "$1" = tar ]; then
            local dest="${@: -1}"
            dest="${dest//\/fixture/$root/volumes/c849${RUN}-work/_data}"
            tar -x -p -C "$dest"
            return
        fi
        if [ "${2:-}" = -c ]; then
            local script="$3"
            script="${script//\/fixture\//$root/volumes/c849${RUN}-work/_data/}"
            shift 3
            local -a args=()
            for arg in "$@"; do
            case "$arg" in /fixture|/fixture/*) arg="$root/volumes/c849${RUN}-work/_data${arg#/fixture}" ;; esac
            args+=("$arg")
        done
            bash -c "$script" "${args[@]}"
            return
        fi
        code=$(cat)
        code="${code//\/fixture\//$root/volumes/c849${RUN}-work/_data/}"
        local -a args=()
        for arg in "$@"; do
            case "$arg" in /fixture|/fixture/*) arg="$root/volumes/c849${RUN}-work/_data${arg#/fixture}" ;; esac
            args+=("$arg")
        done
        "${args[@]}" <<< "$code"
    }
fi
case "$mode" in
 preview-*|fixture-prune|prune-diagnostic-*|inventory-running)
    source "$repo/scripts/fixtures/cache-fixture-completion.sh"
    [ ! -e "$root/denied" ] || fail unexpected-external-call
    exit 0 ;;
esac
case "$mode" in
 fixture-seed)
    ordinary "$root/donor"
    run c849_seed; accept fixture-seed-volume-accepted
    recovery="$(sed -n 's/^recovery=//p' "$C849_READY")"
    [ "$recovery" = "/fixture/cache/recovery-$RUN" ] || fail fixture-recovery-volume-path
    physical="$root/volumes/c849${RUN}-work/_data/cache/recovery-$RUN"
    for part in packages npm; do diff -r "$root/donor/$part" "$root/volumes/$([ "$part" = packages ] && echo "$C849_PACKAGES" || echo "$C849_NPM")/_data" || fail fixture-recipient-bytes; done
    [ "$(stat -c %a "$physical/packages/c913.tools/2.0.0/tool")" = 751 ] || fail fixture-recipient-executable
    [ ! -e "$C913_RECOVERY" ] || fail fixture-no-host-recovery
    printf keep > "$root/volumes/$C849_PACKAGES/_data/rerun-sentinel"
    run c849_seed; accept fixture-rerun-accepted
    [ "$(cat "$root/volumes/$C849_PACKAGES/_data/rerun-sentinel")" = keep ] || fail fixture-rerun-sentinel-retained
    pass fixture-rerun-sentinel-retained
    awk '/barrier after-recovery/{recovery=NR} /barrier before-publication/{if(recovery && recovery<NR) ok=1} END {exit !ok}' "$root/trace" || fail fixture-publication-last
    pass fixture-publication-last
    for FAULT in unsafe-tree copy-packages copy-npm npm-verify corrupt-import corrupt-npm-import smoke recovery-save restart donor-id reconnect publication; do
      rm -f "$C849_READY"; rm -rf -- "$physical"
      for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
      : > "$root/trace"
      if [ "$FAULT" = unsafe-tree ]; then ln -s "$root/outside" "$root/donor/packages/escape"; fi
      run c849_seed
      case "$FAULT" in
        unsafe-tree) diagnosis=CacheDonorUnsafeEntry ;;
        copy-packages) diagnosis=CacheDonorPackageCopyFailed ;;
        copy-npm) diagnosis=CacheDonorNpmCopyFailed ;;
        npm-verify) diagnosis=NpmStagedIntegrityFailed ;;
        corrupt-import|corrupt-npm-import) diagnosis=CacheSeedImportChanged ;;
        smoke) diagnosis=CacheSeedSmokeFailed ;;
        recovery-save) diagnosis=CacheRecoverySaveFailed ;;
        restart) diagnosis=CacheDonorRestartFailed ;;
        donor-id) diagnosis=CacheDonorIdentityChanged ;;
        reconnect) diagnosis=CacheDonorReconnectFailed ;;
        publication) diagnosis=CacheSeedMarkerInvalid ;;
      esac
      refuse "fixture-$FAULT-no-marker" "$diagnosis"
      if [ "$FAULT" = unsafe-tree ]; then rm "$root/donor/packages/escape"; ! grep -q '^import$' "$root/trace" || fail fixture-unsafe-tree-refused; fi
      [ ! -e "$C849_READY" ] || fail "fixture-$FAULT-no-marker"
      if [ "$FAULT" = npm-verify ]; then ! grep -q '^import$' "$root/trace" || fail fixture-integrity-before-import; fi
    done
    FAULT=none
    rm -rf "$root/volumes/c849${RUN}-work/_data/cache" "$root/volumes/c849${RUN}-work/_data/donor"
    mkdir -p "$root/volumes/c849${RUN}-work/_data/cache" "$root/volumes/c849${RUN}-work/_data/donor"
    : > "$root/outer-results"
    run c913_fixture_wrapper; accept fixture-wrapper-accepted
    grep -Fxq 'PASS F-3' "$CASE_DIR/fixture-groups.txt" || fail fixture-wrapper-group-required
    ! grep -q '^true ' "$root/outer-results" || fail fixture-wrapper-no-outer-publication
    [ "$(cat "$root/volumes/c849${RUN}seed-packages/_data/rerun-sentinel")" = keep-after-rerun ] || fail fixture-wrapper-rerun-sentinel
    [ "$(cat "$root/volumes/c849${RUN}-work/_data/cache/recovery-$RUN/packages/c913.tools/2.0.0/tool")" = fixture-native-host ] || fail fixture-wrapper-recovery-bytes
    pass fixture-wrapper-no-outer-publication
    # The next wrapper is an independent acquisition scenario. Release its
    # predecessor through production ID cleanup, rather than reuse an occupied name.
    c849_fixture_consumer_release "c849-${RUN}-donor"
    for name in "c849${RUN}seed-packages" "c849${RUN}seed-scratch" "c849${RUN}seed-npm"; do rm -rf "$root/volumes/$name"; done
    rm -f "$SERVER2_ROOT/cache/fixture-seed-ready"
    rm -rf "$root/volumes/c849${RUN}-work/_data/cache" "$root/volumes/c849${RUN}-work/_data/donor"
    mkdir -p "$root/volumes/c849${RUN}-work/_data/cache" "$root/volumes/c849${RUN}-work/_data/donor"
    printf '%s\n' "c849${RUN}-work" > "$CASE_DIR/.fixture-volumes"
    : > "$root/outer-results"
    FAULT=smoke
    run c913_fixture_wrapper; refuse fixture-wrapper-failure-no-success FixtureSeedFailed
    ! grep -q '^true ' "$root/outer-results" || fail fixture-wrapper-failure-no-success
    [ ! -e "$SERVER2_ROOT/cache/fixture-seed-ready" ] || fail fixture-wrapper-failure-no-marker
    ;;
 seed-metadata)
    ordinary "$root/donor"
    physical="$root/volumes/c849${RUN}-work/_data/cache/recovery-$RUN"
    sha256sum() {
        if [ "$FAULT" = manifest-hash ] && [[ "$*" == *recovery.manifest ]]; then
            printf 'partialhash\n'
            return 41
        fi
        command sha256sum "$@"
    }
    du() {
        if [ "$FAULT" = package-bytes ] && [[ "$*" == */packages ]]; then
            printf '12\tpartial\n'
            return 43
        fi
        if [ "$FAULT" = npm-bytes ] && [[ "$*" == */npm ]]; then
            printf '12\tpartial\n'
            return 43
        fi
        command du "$@"
    }
    export -f sha256sum du
    for FAULT in manifest-hash package-bytes npm-bytes; do
        rm -f -- "$C849_READY"
        rm -rf -- "$physical"
        for name in "$C849_PACKAGES" "$C849_NPM"; do
            find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
        done
        : > "$root/trace"
        run c849_seed
        refuse "seed-$FAULT-refused" CacheSeedMetadataFailed
        [ ! -e "$C849_READY" ] || fail "seed-$FAULT-published"
        ! grep -q 'RESULT true' "$root/out" || fail "seed-$FAULT-success"
    done
    ;;
 seed-helper-removal)
    ordinary "$root/donor"
    FAULT=helper-remove
    run c849_seed
    refuse seed-helper-removal-refused CacheSeedHelperRemovalFailed
    [ ! -e "$C849_READY" ] || fail seed-helper-removal-published
    ! grep -q 'RESULT true' "$root/out" || fail seed-helper-removal-success
    ;;
 volume-recovery)
    recovery="/fixture/cache/recovery-$RUN"
    physical="$root/volumes/c849${RUN}-work/_data/cache/recovery-$RUN"
    C849_PACKAGES="c849${RUN}smoke-packages"
    printf '%s\n' "$C849_PACKAGES" >> "$CASE_DIR/.fixture-volumes"
    mkdir -p "$root/volumes/$C849_PACKAGES/_data" "$physical/packages" "$physical/npm"
    ordinary "$root/recovery-source"
    cp -a "$root/recovery-source/packages/." "$root/volumes/$C849_PACKAGES/_data/"
    run c849_fixture_recovery_copy "$image_id" "$recovery"; accept recovery-volume-source-copy
    [ "$(cat "$physical/packages/c913.probe/1.0.0/data")" = probe ] || fail recovery-volume-source-bytes
    printf protected > "$physical/packages/c913.probe/1.0.0/data"
    FAULT=foreign-source-cache
    run c849_fixture_recovery_copy "$image_id" "$recovery"
    [ "$(cat "$root/exit")" = 2 ] && [ "$(cat "$physical/packages/c913.probe/1.0.0/data")" = protected ] || fail recovery-foreign-cache-refused
    pass recovery-foreign-cache-refused
    FAULT=none
    # Restore the modeled current-run acquisition after the foreign-label fault.
    c849_fixture_cache_identity "$C849_PACKAGES" nuget-packages || fail recovery-readmission-identity
    printf '%s\n' "$C849_PACKAGES" >> "$CASE_DIR/.fixture-volumes"
    run c849_fixture_recovery_copy "$image_id" "$recovery"; accept recovery-source-restored
    mkdir -p "$physical/npm/empty"
    printf npm > "$physical/npm/content"
    printf npm-content > "$physical/npm/fixture-content"
    c849_manifest_write "$physical" "$physical/recovery.manifest"
    digest=$(sha256sum "$physical/recovery.manifest" | cut -d' ' -f1)
    run c849_recovery_check "$recovery" "$digest"; accept volume-recovery-valid
    for kind in package npm executable manifest; do
      case "$kind" in
        package) file="$physical/packages/c913.probe/1.0.0/data" ;;
        npm) file="$physical/npm/content" ;;
        executable) file="$physical/packages/c913.tools/2.0.0/tool" ;;
        manifest) file="$physical/recovery.manifest" ;;
      esac
      cp -p "$file" "$root/backup"
      if [ "$kind" = executable ]; then chmod 600 "$file"; else printf changed >> "$file"; fi
      run c849_recovery_check "$recovery" "$digest"; refuse "volume-$kind-bound" CacheRecoveryChanged
      cp -p "$root/backup" "$file"
      run c849_recovery_check "$recovery" "$digest"; accept "volume-$kind-restored"
    done
    mv "$physical" "$physical.missing"
    run c849_recovery_check "$recovery" "$digest"; refuse volume-tree-required CacheRecoveryMissing
    ln -s "$physical.missing" "$physical"
    run c849_recovery_check "$recovery" "$digest"; refuse volume-link-refused CacheRecoveryMissing
    rm "$physical"; mv "$physical.missing" "$physical"
    mv "$physical/recovery.manifest" "$physical/manifest.missing"
    run c849_recovery_check "$recovery" "$digest"; refuse volume-manifest-required CacheRecoveryMissing
    ln -s manifest.missing "$physical/recovery.manifest"
    run c849_recovery_check "$recovery" "$digest"; refuse volume-manifest-link-refused CacheRecoveryMissing
    rm "$physical/recovery.manifest"; mv "$physical/manifest.missing" "$physical/recovery.manifest"
    run c849_recovery_check /fixture/escape/recovery-other "$digest"; refuse volume-escape-refused CacheRecoveryInvalid
    # Execute the actual F-9 mutation/restoration loop, retaining the real control reader.
    # Warm/inventory/cleanup controls are independent scopes; their existing CPs own them.
    declare -f c849_fixture_control | sed '1s/c849_fixture_control/c913_recovery_control/' > "$root/control-function"
    source "$root/control-function"
    c849_fixture_control() {
        case "$1" in PC-27|PC-31|PC-32) return 0 ;; esac
        c913_recovery_control "$@"
    }
    c849_volume() { mkdir -m700 -p "$root/volumes/$1/_data"; }
    : > "$CASE_DIR/fixture-controls.txt"; : > "$CASE_DIR/fixture-control-variants.txt"
    C913_RECOVERY="$physical"
    chmod 0700 "$root/volumes/$C849_PACKAGES/_data"
    make_full_marker
    run c849_require_ready; accept recovery-marker-valid-before-controls
    cp "$physical/recovery.manifest" "$root/golden-recovery-manifest"
    run c849_fixture_contract_controls; accept volume-recovery-controls
    [ "$(grep -c '^PC-28\.' "$CASE_DIR/fixture-control-variants.txt")" = 4 ] || fail recovery-four-corruptions-reached
    [ "$(grep -c '^PC-29\.' "$CASE_DIR/fixture-control-variants.txt")" = 2 ] || fail recovery-two-missing-reached
    [ "$(cat "$physical/packages/c913.probe/1.0.0/data")" = probe ] || fail recovery-package-restored
    [ "$(cat "$physical/npm/fixture-content")" = npm-content ] || fail recovery-npm-restored
    [ "$(stat -c %a "$physical/packages/c913.tools/2.0.0/tool")" = 751 ] || fail recovery-mode-restored
    cmp -s "$root/golden-recovery-manifest" "$physical/recovery.manifest" || fail recovery-manifest-restored
    run c849_recovery_check "$recovery" "$digest"; accept volume-recovery-restored
    ;;
 tree)
    ordinary "$root/donor"
    run c849_validate_seed_tree "$root/donor"; accept ordinary-only-accepted
    mkdir -p "$root/donor/packages/incomplete/1.0"; printf partial > "$root/donor/packages/incomplete/1.0/data"
    cp -a "$root/donor" "$root/stage"
    run c849_validate_seed_tree "$root/stage"; accept incomplete-pruned-only-in-stage
    [ -f "$root/donor/packages/incomplete/1.0/data" ] && [ ! -e "$root/stage/packages/incomplete/1.0" ] || fail incomplete-pruned-only-in-stage
    for unsafe in /absolute ./leading ../leading interior/../escape interior/./same trailing/. trailing/.. $'line\nbreak' $'carriage\rreturn'; do
      run c849_validate_seed_relative "$unsafe"; refuse unsafe-relative-refused CacheDonorUnsafePath
    done
    for fault in empty incomplete metadata-empty symlink hardlink fifo version-file newline; do
      rm -rf -- "$root/stage"; mkdir -p "$root/stage"; ordinary "$root/stage"
      diagnosis=CacheDonorUnsafeEntry
      case "$fault" in
        empty) rm -rf "$root/stage/packages/"*; diagnosis=CacheDonorPackagesEmpty ;;
        incomplete) find "$root/stage" -name .nupkg.metadata -delete; diagnosis=CacheDonorPackagesEmpty ;;
        metadata-empty) find "$root/stage" -name .nupkg.metadata -exec truncate -s0 {} +; diagnosis=CacheDonorPackagesEmpty ;;
        symlink) ln -s "$root/outside" "$root/stage/packages/link" ;;
        hardlink) ln "$root/stage/packages/c913.probe/1.0.0/data" "$root/stage/packages/hard" ;;
        fifo) mkfifo "$root/stage/packages/fifo" ;;
        version-file) printf bad > "$root/stage/packages/c913.probe/bad"; diagnosis=CacheDonorVersionInvalid ;;
        newline) mkdir "$root/stage/packages/line"$'\n'"break"; diagnosis=CacheDonorUnsafePath ;;
      esac
      run c849_validate_seed_tree "$root/stage"; refuse "$fault-refused" "$diagnosis"
    done
    ordinary "$root/mode-source"
    chmod 6751 "$root/mode-source/packages/c913.tools/2.0.0/tool"
    tar -cf "$root/mode.tar" -C "$root/mode-source" .
    mkdir -p "$root/mode-stage/packages" "$root/mode-stage/npm"
    run "$pwsh_path" -NoProfile -File "$repo/scripts/c849-import-saved-donor.ps1" -Source "$root/mode.tar" -Stage "$root/mode-stage"
    accept unsafe-mode-imported
    [ "$(stat -c %a "$root/mode-stage/packages/c913.tools/2.0.0/tool")" = 751 ] || fail unsafe-mode-masked
    pass unsafe-mode-masked
    ;;
 seed)
    ordinary "$root/donor"
    run c849_seed
    [ "$(cat "$root/exit")" = 0 ] && grep -Fxq schema=3 "$C849_READY" || fail schema3-published
    pass schema3-published
    for part in packages npm; do diff -r "$root/donor/$part" "$root/volumes/$([ "$part" = packages ] && echo "$C849_PACKAGES" || echo "$C849_NPM")/_data" || fail recipient-bytes; done
    [ "$(stat -c %a "$C913_RECOVERY/packages/c913.tools/2.0.0/tool")" = 751 ] || fail recipient-executable
    printf 'MANIFEST_BASE64='; base64 -w0 "$C913_RECOVERY/recovery.manifest"; printf '\n'
    printf 'MANIFEST_DIGEST=%s\n' "$(sha256sum "$C913_RECOVERY/recovery.manifest" | cut -d' ' -f1)"
    cp "$C913_RECOVERY/recovery.manifest" "$root/golden"
    mkdir -p "$root/reordered/npm/empty" "$root/reordered/packages/c913.tools/2.0.0" "$root/reordered/packages/c913.probe/1.0.0"
    cp -a "$root/donor/npm/." "$root/reordered/npm/"
    cp -a "$root/donor/packages/c913.tools/." "$root/reordered/packages/c913.tools/"
    cp -a "$root/donor/packages/c913.probe/." "$root/reordered/packages/c913.probe/"
    touch -t 200001010000 "$root/reordered/packages/c913.probe/1.0.0/data"
    mkdir "$root/reordered/scratch"; printf ignored > "$root/reordered/scratch/lock"
    c849_manifest_write "$root/reordered" "$root/actual"
    cmp -s "$root/golden" "$root/actual" || fail manifest-order-independent
    pass manifest-order-independent
    for change in package npm size executable rename remove add; do
      rm -rf "$root/modified"; cp -a "$root/donor" "$root/modified"
      case "$change" in
        package) printf PROBE > "$root/modified/packages/c913.probe/1.0.0/data" ;;
        npm) printf NPM > "$root/modified/npm/content" ;;
        size) printf x >> "$root/modified/npm/content" ;;
        executable) chmod 644 "$root/modified/packages/c913.tools/2.0.0/tool" ;;
        rename) mv "$root/modified/npm/content" "$root/modified/npm/renamed" ;;
        remove) rm "$root/modified/npm/content" ;;
        add) printf added > "$root/modified/npm/extra" ;;
      esac
      run c849_manifest_compare "$root/modified" "$root/golden"
      [ "$(cat "$root/exit")" = 2 ] || fail "manifest-$change-bound"
      pass "manifest-$change-bound"
    done
    for FAULT in stop copy-packages copy-npm npm-verify import-packages import-npm corrupt-import corrupt-npm-import smoke recovery-save restart donor-id reconnect marker-write publication busy busy-runner busy-queue unknown unknown-sessions unknown-runner process-error writer; do
      rm -f "$C849_READY"; rm -rf -- "$C913_RECOVERY"
      for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
      : > "$root/trace"
      run c849_seed
      [ "$(cat "$root/exit")" != 0 ] && [ ! -e "$C849_READY" ] || fail "$FAULT-no-publication"
      [ "$(cat "$root/outside")" = outside ] || fail seed-cleanup-scope
      case "$FAULT" in
        busy*|unknown*|process-error|writer) ! grep -q '^stop$' "$root/trace" || fail "$FAULT-no-stop" ;;
        npm-verify) ! grep -q '^import$' "$root/trace" || fail npm-before-manifest ;;
      esac
      pass "$FAULT-no-publication"
    done
    FAULT=none
    for CRASH in before-stop after-stop after-copy-packages after-copy-npm after-npm-verify after-import-packages after-import-npm after-comparison before-smoke after-smoke after-recovery before-restart after-restart donor-id reconnect marker-write after-marker-write before-publication after-publication; do
      rm -f "$C849_READY" "$C849_READY.tmp-$RUN"; rm -rf -- "$C913_RECOVERY"
      for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
      : > "$root/trace"
      run c849_seed
      [ "$(cat "$root/exit")" = 137 ] && grep -Fxq "barrier $CRASH" "$root/trace" || fail "crash-$CRASH-reached"
      if [ "$CRASH" = after-publication ]; then
        before=$(sha256sum "$C849_READY")
        run c849_require_ready; accept published-crash-reopens
        [ "$(sha256sum "$C849_READY")" = "$before" ] || fail publication-no-rewrite
      else
        [ ! -e "$C849_READY" ] || fail "crash-$CRASH-no-marker"
        run c849_require_ready; refuse "crash-$CRASH-reopens-held" CacheSeedRequired
        if [ -n "$(find "$root/volumes/$C849_PACKAGES/_data" -type f -print -quit)" ]; then
          CRASH=''
          run c849_seed; refuse interrupted-import-held CacheUnmarkedContent
        fi
      fi
    done
    CRASH=''; FAULT=none; SOURCE=saved
    mkdir -p "$root/donor/packages/incomplete/1.0.0"
    printf unchanged > "$root/donor/packages/incomplete/1.0.0/data"
    tar -cf "$root/saved.tar" -C "$root/donor" .
    saved_hash=$(sha256sum "$root/saved.tar")
    for saved_kind in directory tar; do
      if [ "$saved_kind" = directory ]; then C590_SAVED_DONOR="$root/donor"; else C590_SAVED_DONOR="$root/saved.tar"; fi
      for saved_fault in none smoke saved-temp-present saved-late-busy saved-late-attachment; do
        FAULT="$saved_fault"
        rm -f "$C849_READY"; rm -rf -- "$C913_RECOVERY"
        for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
        : > "$root/trace"; : > "$root/docker-trace"
        run c849_seed
        if [ "$FAULT" = none ]; then
          accept "ordinary-saved-$saved_kind-accepted"
          grep -Fxq "donor-type=saved-$saved_kind" "$C849_READY" || fail saved-donor-type
          [ ! -e "$C913_RECOVERY/packages/incomplete/1.0.0" ] || fail incomplete-stage-only
        else
          [ "$(cat "$root/exit")" != 0 ] && [ ! -e "$C849_READY" ] || fail saved-smoke-failure-no-marker
        fi
        [ "$(sha256sum "$root/saved.tar")" = "$saved_hash" ] &&
          [ "$(cat "$root/donor/packages/incomplete/1.0.0/data")" = unchanged ] || fail saved-source-byte-identical
        if [[ "$saved_fault" == saved-* ]]; then
          ! grep -q '^import$' "$root/trace" || fail "$saved_fault-no-import"
          pass "$saved_fault-no-import"
        fi
        pass saved-source-byte-identical
      done
    done
    SOURCE=live; FAULT=busy-main; C590_SAVED_DONOR=''
    rm -f "$C849_READY"; rm -rf -- "$C913_RECOVERY"
    for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
    : > "$root/trace"
    run c849_seed; accept busy-main-live-donor-accepted
    ;;
 ready)
    make_full_marker
    run c849_require_ready allow-cold; accept schema3-valid
    cp "$C849_READY" "$root/marker"
    recovery="$C913_RECOVERY"
    cp "$recovery/recovery.manifest" "$root/manifest"
    for bad in duplicate absolute type truncated unsorted; do
      cp "$root/manifest" "$root/bad-manifest"
      case "$bad" in
        duplicate) printf 'D\0packages\0000\0-\0-\0' >> "$root/bad-manifest" ;;
        absolute) printf 'D\0/escape\0000\0-\0-\0' >> "$root/bad-manifest" ;;
        type) printf 'L\0zz\0000\0-\0-\0' >> "$root/bad-manifest" ;;
        truncated) truncate -s -1 "$root/bad-manifest" ;;
        unsorted) printf 'D\0npm\0000\0-\0-\0' >> "$root/bad-manifest" ;;
      esac
      run c849_manifest_validate "$root/bad-manifest"
      [ "$(cat "$root/exit")" = 2 ] || fail "$bad-record-refused"
      pass "$bad-record-refused"
    done
    for row in 'schema|4' 'kind|cold' 'source-sha|bad' 'image|bad' 'time|2026-99-99T00:00:00Z' 'package-bytes|-1' 'npm-bytes|01' 'donor-type|unknown' 'donor|short' 'packages|foreign' 'scratch|foreign' 'npm|foreign' 'manifest-sha256|bad'; do
      key="${row%|*}"; value="${row#*|}"
      sed "s|^$key=.*|$key=$value|" "$root/marker" > "$C849_READY"
      run c849_require_ready allow-cold; refuse "$key-value-refused" CacheSeedMarkerInvalid
    done
    cp "$root/marker" "$C849_READY"
    printf churn > "$root/volumes/$C849_PACKAGES/_data/new-package"
    run c849_require_ready allow-cold; accept mutable-cache-churn
    for key in schema kind source-sha image donor-type donor time packages scratch npm package-bytes npm-bytes recovery manifest-sha256; do
      sed "/^$key=/d" "$root/marker" > "$C849_READY"
      run c849_require_ready allow-cold; refuse "missing-$key" CacheSeedMarkerInvalid
    done
    for extra in schema=3 payload-sha256=bad cold=true unknown=value; do
      cp "$root/marker" "$C849_READY"; printf '%s\n' "$extra" >> "$C849_READY"
      run c849_require_ready allow-cold; refuse mixed-marker CacheSeedMarkerInvalid
    done
    cp "$root/marker" "$C849_READY"
    for part in packages/c913.probe/1.0.0/data npm/content packages/c913.tools/2.0.0/tool; do
      path="$C913_RECOVERY/$part"; cp -p "$path" "$root/backup"
      printf mutation >> "$path"
      run c849_require_ready allow-cold; refuse recovery-bytes-changed CacheRecoveryChanged
      cp -p "$root/backup" "$path"
    done
    path="$C913_RECOVERY/packages/c913.tools/2.0.0/tool"
    chmod 644 "$path"; run c849_require_ready allow-cold; refuse recovery-execute-changed CacheRecoveryChanged; chmod 751 "$path"
    mv "$C849_READY" "$root/valid-marker"; ln -s "$root/valid-marker" "$C849_READY"
    run c849_require_ready allow-cold; refuse marker-symlink CacheSeedMarkerInvalid
    rm "$C849_READY"; cp "$root/marker" "$C849_READY"
    mv "$recovery" "$root/escaped-recovery"; ln -s "$root/escaped-recovery" "$recovery"
    run c849_require_ready allow-cold; refuse recovery-escape-refused CacheRecoveryMissing
    rm "$recovery"; mv "$root/escaped-recovery" "$recovery"
    for bad in recovery-missing manifest-missing recovery-escape image-missing digest; do
      cp "$root/marker" "$C849_READY"; FAULT=none
      case "$bad" in
        recovery-missing) mv "$recovery" "$recovery.missing"; diagnosis=CacheRecoveryMissing ;;
        manifest-missing) mv "$recovery/recovery.manifest" "$recovery/manifest.missing"; diagnosis=CacheRecoveryMissing ;;
        recovery-escape) sed -i "s|^recovery=.*|recovery=$root/outside|" "$C849_READY"; diagnosis=CacheRecoveryInvalid ;;
        image-missing) FAULT=image-missing; diagnosis=CacheRecoveryImageMissing ;;
        digest) sed -i 's/^manifest-sha256=.*/manifest-sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/' "$C849_READY"; diagnosis=CacheRecoveryChanged ;;
      esac
      run c849_require_ready allow-cold; refuse "$bad-refused" "$diagnosis"
      [ ! -d "$recovery.missing" ] || mv "$recovery.missing" "$recovery"
      [ ! -f "$recovery/manifest.missing" ] || mv "$recovery/manifest.missing" "$recovery/recovery.manifest"
    done
    FAULT=none
    host="$root/volumes/$C849_PACKAGES/_data/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
    mkdir -p "${host%/*}"; printf 'legacy-host' > "$host"
    legacy_digest=$(sha256sum "$host" | cut -d' ' -f1)
    printf 'donor=saved\nimage=%s\ntime=2026-10-04T00:00:00Z\npayload-sha256=%s\nreference-sha256=%064d\npackage-bytes=11\nnpm-bytes=0\nrecovery=%s\n' "$image_id" "$legacy_digest" 0 "$recovery" > "$C849_READY"
    cp "$C849_READY" "$root/legacy-marker"
    run c849_require_ready allow-cold; accept legacy-valid
    printf changed >> "$host"
    run c849_require_ready allow-cold; refuse legacy-payload-change-refused CacheSeedPayloadChanged
    printf 'legacy-host' > "$host"
    SHA=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
    run c849_require_ready allow-cold; accept legacy-cross-sha-valid
    cmp -s "$C849_READY" "$root/legacy-marker" || fail legacy-marker-byte-identical
    pass legacy-marker-byte-identical
    printf 'schema=2\nkind=cold\ncold=true\nsource-sha=%s\nimage=%s\npackages=%s\nscratch=%s\nnpm=%s\n' "$SHA" "$image_id" "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" > "$C849_READY"
    run c849_require_ready allow-cold; accept cold-valid-with-cache-churn
    run c849_require_ready; refuse cold-full-context-refused CacheFullSeedRequired
    ;;
 prune|maintenance|volume-prune)
    CASE=runner-cache-prune
    C590_PREVIEW_RUN="$RUN"; receipt="$SERVER2_ROOT/cache/previews/$RUN"; mkdir -p "$receipt"
    # Volume size and disk facts are external boundaries; execute all authority,
    # recovery, whole-target preflight, destructive and final-budget guards.
    c849_observe_volume() {
      local bytes=$((PERCENT * 1000))
      [ "$SELECT_ROLE" = all ] || [ "$SELECT_ROLE" = "$2" ] || bytes=0
      if [ "$FAULT" = budget ] && grep -q '^smoke$' "$root/trace"; then bytes=100001; fi
      printf '%s %s %s 1654:1654:700 %s 100000\n' "$1" "$2" "$root/volumes/$1/_data" "$bytes"
    }
    reset_prune() {
      FAULT=none; PERCENT=100; SELECT_ROLE=all
      rm -rf "$C913_RECOVERY"; make_full_marker
      for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        rm -rf "$root/volumes/$name/_data"; mkdir -m700 "$root/volumes/$name/_data"
      done
      mkdir -p "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0"
      printf sentinel > "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0/data"
      printf sentinel > "$root/volumes/$C849_SCRATCH/_data/sentinel"
      printf sentinel > "$root/volumes/$C849_NPM/_data/sentinel"
      for pair in "$C849_PACKAGES:nuget-packages" "$C849_SCRATCH:nuget-scratch" "$C849_NPM:npm-content"; do c849_observe_volume "${pair%:*}" "${pair#*:}" 100000; done > "$receipt/volumes.txt"
      printf 'run=%s\nsource-sha=%s\ncreated-at=%s\nvolume-sha256=%s\n' "$RUN" "$SHA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)" > "$receipt/preview.txt"
      : > "$root/trace"; : > "$root/docker-trace"; rm -f "$CASE_DIR/prune.txt" "$CASE_DIR/smoke-summary.txt"
    }
    reset_prune
    run c849_prune; accept schema3-prune-success
    for runner in server2 server2-temp; do
      grep -Fxq "C849_SMOKE runner=$runner uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK" "$CASE_DIR/smoke-summary.txt" || fail prune-smoke-required
    done
    grep -Fq admission=held "$CASE_DIR/prune.txt" || fail admission-held
    [ -d "$root/volumes/$C849_PACKAGES/_data" ] && [ -d "$C913_RECOVERY" ] || fail roots-and-recovery-retained
    [ ! -e "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0/data" ] && [ ! -e "$root/volumes/$C849_NPM/_data/sentinel" ] || fail selected-contents-cleared
    ! grep -q 'for p in microsoft.netcore' "$root/docker-trace" || fail schema3-no-framework-copy
    for bad in missing-recovery missing-manifest changed-recovery image-missing source run age-future age-expired preview-digest volume-facts second-target nested-mount busy unknown writer process-error attachment broker-busy broker-error; do
      reset_prune
      diagnosis=CachePreviewStale
      case "$bad" in
        missing-recovery) rm -rf "$C913_RECOVERY"; diagnosis=CacheRecoveryMissing ;;
        missing-manifest) rm "$C913_RECOVERY/recovery.manifest"; diagnosis=CacheRecoveryMissing ;;
        changed-recovery) printf changed >> "$C913_RECOVERY/npm/content"; diagnosis=CacheRecoveryChanged ;;
        image-missing) FAULT=image-missing; diagnosis=CacheRecoveryImageMissing ;;
        source) sed -i 's/^source-sha=.*/source-sha=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/' "$receipt/preview.txt" ;;
        run) sed -i 's/^run=.*/run=c84900000000000000000/' "$receipt/preview.txt" ;;
        age-future) sed -i "s/^created-at=.*/created-at=$(date -u -d '+1 hour' +%Y-%m-%dT%H:%M:%SZ)/" "$receipt/preview.txt" ;;
        age-expired) sed -i "s/^created-at=.*/created-at=$(date -u -d '-2 hours' +%Y-%m-%dT%H:%M:%SZ)/" "$receipt/preview.txt" ;;
        preview-digest) printf x >> "$receipt/volumes.txt" ;;
        volume-facts) sed -i 's/100000 100000/99999 100000/' "$receipt/volumes.txt"; sed -i "s/^volume-sha256=.*/volume-sha256=$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)/" "$receipt/preview.txt" ;;
        second-target) ln -s "$root/outside" "$root/volumes/$C849_SCRATCH/_data/unsafe"; diagnosis=CacheTargetInvalid ;;
        nested-mount) FAULT=nested-mount; diagnosis=CacheTargetInvalid ;;
        busy|unknown|writer|attachment) FAULT="$bad"; diagnosis=CacheConsumersBusy ;;
        process-error) FAULT="$bad"; diagnosis=CacheConsumerUnknown ;;
        broker-busy|broker-error) FAULT="$bad"; diagnosis=CacheBuildSlotsBusy ;;
      esac
      run c849_prune; refuse "$bad-no-delete" "$diagnosis"
      [ "$(cat "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0/data")" = sentinel ] &&
        [ "$(cat "$root/volumes/$C849_SCRATCH/_data/sentinel")" = sentinel ] &&
        [ "$(cat "$root/volumes/$C849_NPM/_data/sentinel")" = sentinel ] || fail "$bad-no-delete"
      ! grep -q 'rm -rf --' "$root/docker-trace" || fail "$bad-no-delete-trace"
      [ ! -e "$CASE_DIR/prune.txt" ] || fail "$bad-no-success"
    done
    if [ "$mode" = maintenance ] || [ "$mode" = volume-prune ]; then [ ! -e "$root/denied" ] || fail unexpected-external-call; pass "$mode-complete"; exit 0; fi
    for FAULT_CASE in refill refill-receipt main-smoke budget; do
      reset_prune; FAULT="$FAULT_CASE"
      case "$FAULT" in refill) diagnosis=CacheRefillFailed;; refill-receipt) diagnosis=CacheRefillReceiptMissing;; main-smoke) diagnosis=CacheSmokeFailed;; budget) diagnosis=CacheBudgetExceeded;; esac
      run c849_prune
      refuse "$FAULT-no-success" "$diagnosis"
      [ ! -e "$CASE_DIR/prune.txt" ] || fail "$FAULT-no-success"
      pass "$FAULT-no-success"
    done
    # Exact clock edges use observed time facts, with no sleep or timeout changes.
    now=$(command date -u +%s)
    date() { if [ "$*" = '-u +%s' ]; then printf '%s\n' "$now"; else command date "$@"; fi; }
    for age in -1 0 3600 3601; do
      reset_prune
      sed -i "s/^created-at=.*/created-at=$(command date -u -d "@$((now-age))" +%Y-%m-%dT%H:%M:%SZ)/" "$receipt/preview.txt"
      run c849_prune
      if [ "$age" = -1 ] || [ "$age" = 3601 ]; then
        refuse "stale-time-no-delete-$age" CachePreviewStale
        [ "$(cat "$root/volumes/$C849_SCRATCH/_data/sentinel")" = sentinel ] || fail stale-time-no-delete
      else accept "preview-age-$age-accepted"; fi
    done
    unset -f date
    for selected_role in nuget-packages nuget-scratch npm-content all; do
      for percent in 79 80 99 100 101; do
        reset_prune; PERCENT="$percent"; SELECT_ROLE="$selected_role"
        for pair in "$C849_PACKAGES:nuget-packages" "$C849_SCRATCH:nuget-scratch" "$C849_NPM:npm-content"; do c849_observe_volume "${pair%:*}" "${pair#*:}" 100000; done > "$receipt/volumes.txt"
        sed -i "s/^volume-sha256=.*/volume-sha256=$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)/" "$receipt/preview.txt"
        run c849_prune
        if [ "$percent" = 79 ]; then refuse "$selected_role-79-no-candidate" CacheNoPruneCandidate
        elif [ "$percent" = 101 ]; then refuse "$selected_role-101-over-budget" CacheBudgetExceeded
        else accept "$selected_role-$percent-pruned"; fi
        for pair in "$C849_PACKAGES:nuget-packages" "$C849_SCRATCH:nuget-scratch" "$C849_NPM:npm-content"; do
          name="${pair%:*}"; role="${pair#*:}"; sentinel="$root/volumes/$name/_data/sentinel"
          [ "$role" != nuget-packages ] || sentinel="$root/volumes/$name/_data/ordinary/1.0.0/data"
          if [ "$percent" != 79 ] && { [ "$SELECT_ROLE" = all ] || [ "$SELECT_ROLE" = "$role" ]; }; then
            [ ! -e "$sentinel" ] || fail "selected-role-not-cleared-$role-$percent"
          else [ "$(cat "$sentinel")" = sentinel ] || fail "unselected-role-changed-$role-$percent"; fi
        done
      done
    done
    reset_prune
    recovery="$C913_RECOVERY"
    for base in "$recovery/packages" "$root/volumes/$C849_PACKAGES/_data"; do
      mkdir -p "$base/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native" "$base/microsoft.netcore.app.ref/9.0.20"
      printf '{}' > "$base/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata"
      printf '{}' > "$base/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata"
      printf legacy-host > "$base/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
    done
    legacy_digest=$(sha256sum "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)
    printf 'donor=saved\nimage=%s\ntime=2026-10-04T00:00:00Z\npayload-sha256=%s\nreference-sha256=%064d\npackage-bytes=11\nnpm-bytes=0\nrecovery=%s\n' "$image_id" "$legacy_digest" 0 "$recovery" > "$C849_READY"
    run c849_prune; accept legacy-prune-success
    grep -q 'for p in microsoft.netcore' "$root/docker-trace" &&
      [ "$(cat "$root/volumes/$C849_PACKAGES/_data/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost")" = legacy-host ] || fail legacy-refill-required
    pass legacy-refill-required
    reset_prune; PERCENT=0
    df() { printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 999999999 0 %s 0%% /\n' "$FREE_KB"; }
    FREE_KB=20971519; run c849_budget_gate; refuse headroom-below-20g-refused CacheDiskLow
    FREE_KB=20971520; run c849_budget_gate; accept headroom-at-20g-accepted
    ;;
 inventory)
    SOURCE=saved
    curl() { c849_status_body "${@: -1}"; }
    # Inventory used curl directly at the parent. Handle that boundary without changing its parser.
    curl() { if [[ "$*" == *server2-temp/status* ]]; then c849_status_body server2-temp; else c849_status_body server2; fi; }
    run case_runner_cache_inventory; accept absent-temp-accepted
    grep -Fq 'runner=server2-temp container=absent' "$CASE_DIR/identities.txt" || fail absent-temp-recipient
    for FAULT in census-error partial-census main-missing inspect-error status-error status-omitted status-malformed status-no-retired absent-unretired duplicate retired-running; do
      SOURCE=saved
      case "$FAULT" in duplicate|retired-running|inspect-error) SOURCE=live ;; esac
      run case_runner_cache_inventory
      case "$FAULT" in
        census-error|partial-census|inspect-error) diagnosis=CacheInventoryLookupFailed ;;
        main-missing|absent-unretired) diagnosis=CacheInventoryRunnerMissing ;;
        status-error) diagnosis=CacheInventoryStatusUnavailable ;;
        status-*) diagnosis=CacheInventoryStatusInvalid ;;
        duplicate|retired-running) diagnosis=CacheInventoryIdentityInvalid ;;
      esac
      refuse "inventory-$FAULT-refused" "$diagnosis"
    done
    SOURCE=live; FAULT=none
    run case_runner_cache_inventory; accept present-temp-accepted
    FAULT=attachment
    run c849_volume "$C849_PACKAGES" nuget-packages yes "$image_id"
    refuse unmarked-consumer-refused CacheUnmarkedInUse
    FAULT=none
    tmp_mode_probe() {
      docker() {
        if [ "$1" = inspect ]; then
          if [[ "$*" == *'.Config.Labels'* ]]; then echo "$HOST_PROJECT"; return; fi
          printf 'volume %s /home/app/.nuget/packages true\nvolume %s /var/cache/antiphon/nuget-scratch true\nvolume %s /home/app/.npm/_cacache true\n' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"
          printf 'volume %s_runner-tmp /tmp true\nvolume %s_work /work true\nvolume %s_runner-state /state true\nvolume %s_dind-data /var/lib/docker true\n' "$HOST_PROJECT" "$HOST_PROJECT" "$HOST_PROJECT" "$HOST_PROJECT"
        elif [ "$1" = exec ]; then echo 755; else return 94; fi
      }
      c849_assert_mounts "$donor_id"
    }
    run tmp_mode_probe; refuse tmp-mode-refused RunnerTmpModeInvalid
    : > "$root/cleanup-trace"
    printf 'c849%s-packages\nc849foreign-packages\n' "$RUN" > "$CASE_DIR/.fixture-volumes"
    (
      docker() {
        printf '%s\n' "$*" >> "$C913_ROOT/cleanup-trace"
        local name="${@: -1}"
        if [ "$1" = volume ] && [ "$2" = rm ]; then : > "$C913_ROOT/cleanup-removed"; return 0; fi
        if [ "$1" = volume ] && [ "$2" = inspect ]; then
          if [ -f "$C913_ROOT/cleanup-removed" ]; then printf 'Error: No such volume: %s\n' "$name" >&2; return 1; fi
          [ "${3:-}" = -f ] || return 0
          case "$4" in
            *Driver*) printf 'local\n' ;;
            *Options*) printf 'null\n' ;;
            *io.antiphon.owner*) printf 'server2-runner\n' ;;
            *cache-schema*) printf '1\n' ;;
            *cache-role*) printf 'nuget-packages\n' ;;
            *fixture-run*) printf '%s\n' "$RUN" ;;
            *) return 94 ;;
          esac
          return 0
        fi
        return 94
      }
      c849_fixture_cleanup
    )
    grep -Fq "volume rm c849${RUN}-packages" "$root/cleanup-trace" || fail owned-ledger-removed
    ! grep -Fq c849foreign-packages "$root/cleanup-trace" && [ "$(cat "$root/outside")" = outside ] || fail foreign-ledger-no-remove
    pass foreign-ledger-no-remove
    mkdir -m700 "$CASE_DIR/fixture-controller"
    printf foreign-controller > "$CASE_DIR/fixture-controller/foreign"
    occupied_fixture() {
      WROTE=0
      write_result() { WROTE=1; printf 'RESULT %s %s\n' "$1" "$2"; exit "$3"; }
      c849_fixture
    }
    run occupied_fixture; refuse occupied-fixture-refused FixtureControllerOccupied
    [ "$(cat "$CASE_DIR/fixture-controller/foreign")" = foreign-controller ] || fail occupied-controller-preserved
    rm -rf -- "$CASE_DIR/fixture-controller"
    : > "$CASE_DIR/fixture-control-variants.txt"; : > "$CASE_DIR/fixture-controls.txt"
    intended_refusal() { printf 'ExpectedRefusal\n'; return 2; }
    run c849_fixture_control PC-01 ExpectedRefusal intended_refusal; accept intended-control-red
    setup_failure() { printf 'SetupError\n'; return 2; }
    run c849_fixture_control PC-02 ExpectedRefusal setup_failure; refuse setup-error-not-control-red ControlNotSensitive
    SOURCE=saved; FAULT=none
    : > "$root/docker-trace"; : > "$CASE_DIR/fixture-controls.txt"; : > "$CASE_DIR/fixture-control-variants.txt"
    # The old host recipient is gone. Run the shared strict volume Docker model
    # against the actual F-5 algorithm and independently require its recipient facts.
    run bash "$repo/scripts/fixtures/c1192-workspace-contract.sh" apphost "$repo"
    accept fixture-owned-native-payload
    for fact in apphost-volume-payload apphost-host-decoy apphost-import-readonly mask-empty mask-nocopy mask-readonly mask-exact-pack apphost-import-bytes-mode mask-failure-cleaned; do
      grep -Fxq "$fact=1" "$root/out" || fail "fixture-native-recipient-$fact"
    done
    ! grep -q '^C1192_FAIL' "$root/out" || fail no-production-payload-read
    pass no-production-payload-read
    ;;
 *) fail unknown-mode ;;
esac
[ ! -e "$root/denied" ] || fail unexpected-external-call
pass "$mode-complete"
