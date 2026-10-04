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
SERVER2_ROOT="$root/server2"; CASE_DIR="$root/case"; CHECKOUT="$repo"
C849_READY="$SERVER2_ROOT/cache/seed-accepted"
C849_PACKAGES=antiphon-runner-cache-nuget-packages
C849_SCRATCH=antiphon-runner-cache-nuget-scratch
C849_NPM=antiphon-runner-cache-npm-content
HOST_PROJECT=antiphon-runner; TEMP_PROJECT=antiphon-runner-temp
SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; RUN=c849aaaaaaaaaaaaaaaa0
CASE=runner-cache-seed; C604_SERVER_ORIGIN=https://example.invalid
image_id=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
donor_id=cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc
FAULT=none; SOURCE=live
mkdir -p "$SERVER2_ROOT/cache" "$CASE_DIR" "$root/bin" "$root/donor/packages" "$root/donor/npm"
for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do mkdir -m 700 -p "$root/volumes/$name/_data"; done
printf outside > "$root/outside"
for command in dotnet npm curl pwsh; do
    printf '#!/bin/sh\nprintf "DENIED %%s\\n" "$0" >> "$C913_ROOT/denied"\nexit 93\n' > "$root/bin/$command"
    chmod 755 "$root/bin/$command"
done
# Preserve the actual interpreter for the admitted saved-import child; every other invocation is denied.
pwsh_path=$(command -v pwsh)
export PATH="$root/bin:$PATH"
fail() { printf 'FAIL %s\n' "$*"; [ ! -e "$root/out" ] || cat "$root/out"; exit 1; }
pass() { printf 'PASS %s\n' "$1"; }
require_lane() { [ "$1" = host ]; }
write_result() { printf 'RESULT %s %s\n' "$1" "$2"; exit "$3"; }
c849_lock() { :; }
c849_prepare() { :; } # Mount/initialization has its separate A1/A2 tests.
c849_smoke() {
    printf 'smoke\n' >> "$root/trace"
    [ "$FAULT" != smoke ] || return 17
    printf 'C849_SMOKE runner=%s uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n' "$2" >> "$CASE_DIR/smoke-summary.txt"
}
c849_status_body() {
    local runner="$1" sessions=0 running=0 queued=0 retired=null available=true
    if [ "$runner" = server2-temp ] && [ "$SOURCE" != live ]; then retired='"2026-10-04T00:00:00Z"'; available=false; fi
    [ "$FAULT" != status-error ] || return 1
    [ "$FAULT" != busy ] || sessions=1
    [ "$FAULT" != unknown ] || queued=null
    [ "$FAULT" != absent-unretired ] || retired=null
    printf '{"sessions":%s,"runnerSessions":%s,"queuedTasks":%s,"draining":true,"acceptingNewWork":false,"redirectTo":"server2","retireWhenIdle":true,"dispatchEligible":%s,"available":%s,"retiredAt":%s}' "$sessions" "$running" "$queued" "$available" "$available" "$retired"
}
sudo() {
    [ "$1" != -n ] || shift
    case "$1" in
        install) mkdir -p "${@: -1}" ;;
        stat) [ "$2" = -c ] && [ "$3" = '%u:%g:%a' ] && { printf '1654:1654:700\n'; return; }; command "$@" ;;
        df) printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 999999999 0 999999999 0%% /\n' ;;
        *) command "$@" ;;
    esac
}
chown() { :; }
export -f chown
compose_host() { printf 'main\n'; }
findmnt() { printf '/\n'; }
docker() {
    printf '%s\n' "$*" >> "$root/docker-trace"
    local name role format arg entrypoint='' code='' target source payload='' i
    local -a mappings=()
    case "$1:${2:-}" in
      image:inspect) [ "$FAULT" != image-missing ] || return 1; printf '%s\n' "$image_id" ;;
      info:*) printf '%s\n' "$root" ;;
      volume:ls) printf '%s\n' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" ;;
      volume:inspect)
        name="${@: -1}"; role=nuget-packages
        [ "$name" != "$C849_SCRATCH" ] || role=nuget-scratch
        [ "$name" != "$C849_NPM" ] || role=npm-content
        if [ "${3:-}" = -f ]; then
          case "$4" in
            *'.Driver'*) echo local ;;
            *'.Options'*) echo '{}' ;;
            *'io.antiphon.owner'*) echo server2-runner ;;
            *'io.antiphon.cache-schema'*) echo 1 ;;
            *'io.antiphon.cache-role'*) echo "$role" ;;
            *'.Mountpoint'*) echo "$root/volumes/$name/_data" ;;
            *) return 94 ;;
          esac
        else
          printf '[{"Name":"%s","Driver":"local","Options":{},"Mountpoint":"%s/volumes/%s/_data","Labels":{"io.antiphon.owner":"server2-runner","io.antiphon.cache-schema":"1","io.antiphon.cache-role":"%s"}}]\n' "$name" "$root" "$name" "$role"
        fi ;;
      ps:*)
        [ "$FAULT" != census-error ] || return 1
        if [[ "$*" == *volume=* ]]; then [ "$FAULT" != attachment ] || echo foreign; return 0; fi
        if [[ "$*" == *"project=$TEMP_PROJECT"* ]]; then
          if [ "$SOURCE" = live ]; then echo "$donor_id"; [ "$FAULT" != duplicate ] || echo duplicate; fi
        else printf '%064d\n' 4; fi ;;
      inspect:*)
        [ "$FAULT" != inspect-error ] || return 1
        case "$3" in
          *'.Image'*) echo "$image_id" ;;
          *'.Id'*) if [ "$FAULT" = donor-id ]; then echo changed; else echo "$donor_id"; fi ;;
          *'.State.Running'*) echo false ;;
          *'com.docker.compose.project'*) echo "$TEMP_PROJECT" ;;
          *'com.docker.compose.service'*) echo session-runner ;;
          *'.Mounts'*) echo '[]' ;;
          *) return 94 ;;
        esac ;;
      stop:*) [ "$FAULT" != stop ] || return 1; printf 'stop\n' >> "$root/trace" ;;
      start:*) [ "$FAULT" != restart ] || return 1; printf 'restart\n' >> "$root/trace" ;;
      rm:*) return 0 ;;
      cp:*)
        if [[ "$2" == *'/packages/.' ]]; then source=packages; else source=npm; fi
        [ "$FAULT" != "copy-$source" ] || return 1
        cp -a "$root/donor/$source/." "$3/" ;;
      exec:*)
        if [[ "$*" == *'ps -eo uid,comm'* ]]; then
          [ "$FAULT" != process-error ] || return 1
          if [ "$FAULT" = writer ]; then printf '1654 dotnet\n'; else printf '0 init\n'; fi
        elif [[ "$*" == *'ps -eo uid,args'* ]]; then echo 0
        elif [[ "$*" == *'rev-parse HEAD'* ]]; then echo "$SHA"
        elif [[ "$*" == *'curl '* ]]; then printf '{"occupied":0,"leases":[]}'
        elif [[ "$*" == *'/bin/sh -s'* ]]; then
          cat >/dev/null
          [ "$FAULT" != refill ] || return 1
          [ "$FAULT" = refill-receipt ] || printf 'C849_REFILL restore=0 npm=0\n'
        else return 94; fi ;;
      run:*)
        for ((i=2; i<=$#; i++)); do
          arg="${!i}"
          case "$arg" in
            --entrypoint) i=$((i+1)); entrypoint="${!i}" ;;
            --mount)
              i=$((i+1)); arg="${!i}"; source="${arg#*source=}"; source="${source%%,*}"
              target="${arg#*target=}"; target="${target%%,*}"
              [[ "$arg" != type=volume,* ]] || source="$root/volumes/$source/_data"
              mappings+=("$target|$source") ;;
            -c) i=$((i+1)); code="${!i}" ;;
          esac
        done
        if [ "$entrypoint" = sleep ]; then echo helper; return; fi
        if [ "$entrypoint" = npm ]; then [ "$FAULT" != npm-verify ]; return; fi
        if [ "$entrypoint" = pwsh ]; then
          "$pwsh_path" -NoProfile -File "$repo/scripts/c849-import-saved-donor.ps1" -Source "$C590_SAVED_DONOR" -Stage "$stage"; return
        fi
        if [ "$entrypoint" = sha256sum ]; then code="sha256sum ${!#}"; fi
        if [ -z "$code" ]; then code=$(cat); fi
        if [[ "$code" == *'c849_manifest_compare /import '* ]]; then
          mkdir -p "$root/import-view"
          cp -a "$root/volumes/$C849_PACKAGES/_data" "$root/import-view/packages"
          cp -a "$root/volumes/$C849_NPM/_data" "$root/import-view/npm"
          code="${code//\/import/$root/import-view}"
        fi
        for arg in "${mappings[@]}"; do target="${arg%%|*}"; source="${arg#*|}"; code="${code//"$target"/"$source"}"; done
        if [[ "$code" == *'cp -a '* ]]; then
          [ "$FAULT" != import ] || return 1
          printf 'import\n' >> "$root/trace"
        fi
        if [[ "$code" == *'stat -c %u:%g:%a'* ]]; then
          # Execute actual predicates; only remap privileged uid fact.
          code="${code//stat -c %u:%g:%a/printf 1654:1654:700\\n #}"
        fi
        bash -c "$code" || return $?
        if [ "$FAULT" = corrupt-import ] && [[ "$code" == *'cp -a '* ]]; then
          printf corrupted > "$root/volumes/$C849_PACKAGES/_data/c913.probe/1.0.0/data"
        fi ;;
      *) return 94 ;;
    esac
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
run() { local status=0; ( "$@" ) > "$root/out" 2>&1 || status=$?; printf '%s' "$status" > "$root/exit"; }
accept() { [ "$(cat "$root/exit")" = 0 ] || fail "$1"; pass "$1"; }
refuse() { [ "$(cat "$root/exit")" = 2 ] && grep -Fq "$2" "$root/out" || fail "$1"; [ "$(cat "$root/outside")" = outside ] || fail sibling-changed; pass "$1"; }
make_full_marker() {
    local recovery="$SERVER2_ROOT/cache/recovery-$RUN" manifest
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
    printf 'schema=3\nkind=full\nsource-sha=%s\nimage=%s\ndonor-type=live\ndonor=%s\ntime=2026-10-04T00:00:00Z\npackages=%s\nscratch=%s\nnpm=%s\npackage-bytes=123\nnpm-bytes=456\nrecovery=%s\nmanifest-sha256=%s\n' "$SHA" "$image_id" "$donor_id" "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" "$recovery" "$manifest" > "$C849_READY"
}
case "$mode" in
 tree)
    ordinary "$root/donor"
    run c849_validate_seed_tree "$root/donor"; accept ordinary-only-accepted
    mkdir -p "$root/donor/packages/incomplete/1.0"; printf partial > "$root/donor/packages/incomplete/1.0/data"
    cp -a "$root/donor" "$root/stage"
    run c849_validate_seed_tree "$root/stage"; accept incomplete-pruned-only-in-stage
    [ -f "$root/donor/packages/incomplete/1.0/data" ] && [ ! -e "$root/stage/packages/incomplete/1.0" ] || fail incomplete-pruned-only-in-stage
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
    ;;
 seed)
    ordinary "$root/donor"
    run c849_seed
    [ "$(cat "$root/exit")" = 0 ] && grep -Fxq schema=3 "$C849_READY" || fail schema3-published
    pass schema3-published
    for part in packages npm; do diff -r "$root/donor/$part" "$root/volumes/$([ "$part" = packages ] && echo "$C849_PACKAGES" || echo "$C849_NPM")/_data" || fail recipient-bytes; done
    [ "$(stat -c %a "$SERVER2_ROOT/cache/recovery-$RUN/packages/c913.tools/2.0.0/tool")" = 751 ] || fail recipient-executable
    base64 -w0 "$SERVER2_ROOT/cache/recovery-$RUN/recovery.manifest"; printf '\n'
    for FAULT in stop copy-packages copy-npm npm-verify import corrupt-import smoke restart donor-id busy unknown process-error writer; do
      rm -f "$C849_READY"; rm -rf -- "$SERVER2_ROOT/cache/recovery-$RUN"
      for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
      run c849_seed
      [ "$(cat "$root/exit")" != 0 ] && [ ! -e "$C849_READY" ] || fail "$FAULT-no-publication"
      [ "$(cat "$root/outside")" = outside ] || fail seed-cleanup-scope
      pass "$FAULT-no-publication"
    done
    ;;
 ready)
    make_full_marker
    run c849_require_ready allow-cold; accept schema3-valid
    cp "$C849_READY" "$root/marker"
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
      path="$SERVER2_ROOT/cache/recovery-$RUN/$part"; cp -p "$path" "$root/backup"
      printf mutation >> "$path"
      run c849_require_ready allow-cold; refuse recovery-bytes-changed CacheRecoveryChanged
      cp -p "$root/backup" "$path"
    done
    path="$SERVER2_ROOT/cache/recovery-$RUN/packages/c913.tools/2.0.0/tool"
    chmod 644 "$path"; run c849_require_ready allow-cold; refuse recovery-execute-changed CacheRecoveryChanged; chmod 751 "$path"
    mv "$C849_READY" "$root/valid-marker"; ln -s "$root/valid-marker" "$C849_READY"
    run c849_require_ready allow-cold; refuse marker-symlink CacheSeedMarkerInvalid
    ;;
 prune)
    make_full_marker
    ordinary "$root/volumes/$C849_PACKAGES/_data" # recipients are sentinels, not guard stubs
    CASE=runner-cache-prune
    C590_PREVIEW_RUN="$RUN"; receipt="$SERVER2_ROOT/cache/previews/$RUN"; mkdir -p "$receipt"
    c849_observe_volume() { printf '%s %s %s 1654:1654:700 100000 100000\n' "$1" "$2" "$root/volumes/$1/_data"; }
    c849_budget_gate() { [ "$FAULT" != budget ] || write_result false CacheBudgetExceeded 2; }
    c849_prune_idle() { [ "$FAULT" != busy ] || write_result false CacheConsumersBusy 2; }
    for pair in "$C849_PACKAGES:nuget-packages" "$C849_SCRATCH:nuget-scratch" "$C849_NPM:npm-content"; do c849_observe_volume "${pair%:*}" "${pair#*:}" 100000; done > "$receipt/volumes.txt"
    printf 'run=%s\nsource-sha=%s\ncreated-at=%s\nvolume-sha256=%s\n' "$RUN" "$SHA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)" > "$receipt/preview.txt"
    run c849_prune; accept schema3-prune-success
    grep -Fq admission=held "$CASE_DIR/prune.txt" || fail admission-held
    ! grep -q 'for p in microsoft.netcore' "$root/docker-trace" || fail no-framework-refill
    printf changed >> "$SERVER2_ROOT/cache/recovery-$RUN/npm/content"
    printf sentinel > "$root/volumes/$C849_PACKAGES/_data/sentinel"
    : > "$root/docker-trace"
    run c849_prune; refuse recovery-before-delete CacheRecoveryChanged
    [ "$(cat "$root/volumes/$C849_PACKAGES/_data/sentinel")" = sentinel ] || fail recovery-before-delete
    ;;
 inventory)
    SOURCE=saved
    curl() { c849_status_body "${@: -1}"; }
    # Inventory used curl directly at the parent. Handle that boundary without changing its parser.
    curl() { if [[ "$*" == *server2-temp/status* ]]; then c849_status_body server2-temp; else c849_status_body server2; fi; }
    run case_runner_cache_inventory; accept absent-temp-accepted
    grep -Fq 'runner=server2-temp container=absent' "$CASE_DIR/identities.txt" || fail absent-temp-recipient
    for FAULT in census-error inspect-error status-error absent-unretired duplicate; do
      [ "$FAULT" != duplicate ] || SOURCE=live
      run case_runner_cache_inventory
      [ "$(cat "$root/exit")" != 0 ] || fail "inventory-$FAULT-refused"
      pass "inventory-$FAULT-refused"
    done
    ;;
 *) fail unknown-mode ;;
esac
[ ! -e "$root/denied" ] || fail unexpected-external-call
pass "$mode-complete"
