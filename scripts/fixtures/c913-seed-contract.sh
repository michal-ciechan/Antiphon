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
FAULT=none; SOURCE=live; CRASH=''
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
barrier() {
    builtin printf 'barrier %s\n' "$1" >> "$C913_ROOT/trace"
    if [ "${CRASH:-}" = "$1" ]; then kill -KILL "$C913_CHILD"; exit 137; fi
}
printf() {
    if [[ "${1:-}" == 'schema=3'* ]] && [ -n "${C913_CHILD:-}" ]; then
        barrier marker-write
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
    printf 'smoke\n' >> "$root/trace"
    [ "$FAULT" != smoke ] || write_result false CacheSmokeFailed 2
    printf 'C849_SMOKE runner=%s uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n' "$2" >> "$CASE_DIR/smoke-summary.txt"
    barrier after-smoke
}
c849_status_body() {
    local runner="$1" sessions=0 running=0 queued=0 retired=null available=true
    if [ "$runner" = server2-temp ] && [ "$SOURCE" != live ]; then retired='"2026-10-04T00:00:00Z"'; available=false; fi
    [ "$FAULT" != status-error ] || return 1
    [ "$FAULT" != busy ] || sessions=1
    [ "$FAULT" != unknown ] || queued=null
    if [ "$FAULT" = saved-late-busy ] && grep -q '^barrier after-saved-copy$' "$root/trace"; then sessions=1; fi
    [ "$FAULT" != absent-unretired ] || retired=null
    if [ "$FAULT" = reconnect ] && grep -q '^restart$' "$root/trace"; then available=false; fi
    printf '{"sessions":%s,"runnerSessions":%s,"queuedTasks":%s,"draining":true,"acceptingNewWork":false,"redirectTo":"server2","retireWhenIdle":true,"dispatchEligible":%s,"available":%s,"retiredAt":%s}' "$sessions" "$running" "$queued" "$available" "$available" "$retired"
    if [ "${phase:-}" = reconnected ] && grep -q '^restart$' "$root/trace"; then barrier reconnect; fi
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
    printf '%s\n' "$*" >> "$root/docker-trace"
    local root="$C913_ROOT"
    local name role format arg entrypoint='' code='' target source payload='' i
    local -a mappings=()
    case "$1:${2:-}" in
      image:inspect) [ "$FAULT" != image-missing ] || return 1; printf '%s\n' "$image_id" ;;
      info:*) printf '%s\n' "$root" ;;
      volume:ls) printf '%s\n' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM" ;;
      volume:create) name="${@: -1}"; mkdir -p "$root/volumes/$name/_data"; echo "$name" ;;
      volume:inspect)
        name="${@: -1}"; [ -d "$root/volumes/$name/_data" ] || return 1; role=nuget-packages
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
        if [[ "$*" == *volume=* ]]; then
          if [ "$FAULT" = attachment ] || { [ "$FAULT" = saved-late-attachment ] && grep -q '^barrier after-npm-verify$' "$root/trace"; }; then echo foreign; fi
          return 0
        fi
        if [[ "$*" == *"project=$TEMP_PROJECT"* ]]; then
          if [ "$SOURCE" = live ]; then echo "$donor_id"; [ "$FAULT" != duplicate ] || echo duplicate
          elif [ "$FAULT" = saved-temp-present ] && [ "$(grep -c "project=$TEMP_PROJECT" "$root/docker-trace")" -ge 2 ]; then echo "$donor_id"; fi
        else printf '%064d\n' 4; fi ;;
      inspect:*)
        [ "$FAULT" != inspect-error ] || return 1
        case "$3" in
          *'.Image'*) echo "$image_id" ;;
          *'.Id'*) if grep -q '^restart$' "$root/trace" 2>/dev/null; then barrier donor-id; fi; if [ "$FAULT" = donor-id ] && grep -q '^restart$' "$root/trace"; then echo changed; else echo "$donor_id"; fi ;;
          *'.State.Running'*) echo false ;;
          *'com.docker.compose.project'*) echo "$TEMP_PROJECT" ;;
          *'com.docker.compose.service'*) echo session-runner ;;
          *'.Mounts'*) echo '[]' ;;
          *) return 94 ;;
        esac ;;
      stop:*) barrier before-stop; [ "$FAULT" != stop ] || return 1; printf 'stop\n' >> "$root/trace"; barrier after-stop ;;
      start:*) barrier before-restart; [ "$FAULT" != restart ] || return 1; printf 'restart\n' >> "$root/trace"; barrier after-restart ;;
      rm:*) return 0 ;;
      cp:*)
        if [[ "$2" == *'/packages/.' ]]; then source=packages; else source=npm; fi
        [ "$FAULT" != "copy-$source" ] || return 1
        cp -a "$root/donor/$source/." "$3/"; barrier "after-copy-$source" ;;
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
            --entrypoint) i=$((i+1)); entrypoint="${!i}" ;;
            --mount)
              i=$((i+1)); arg="${!i}"; source="${arg#*source=}"; source="${source%%,*}"
              target="${arg#*target=}"; target="${target%%,*}"
              [[ "$arg" != type=volume,* ]] || source="$root/volumes/$source/_data"
              mappings+=("$target|$source") ;;
            -c) i=$((i+1)); code="${!i}" ;;
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
        if [ -z "$code" ]; then code=$(cat); fi
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
          printf 'import\n' >> "$root/trace"
        fi
        bash -c "$code" || return $?
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
    if [ "$FAULT" = recovery-save ] && [[ "$1" == "$SERVER2_ROOT/cache/stage-"* ]]; then return 1; fi
    if [ "$FAULT" = publication ] && [[ "$*" == *"$C849_READY.tmp-"* ]]; then return 1; fi
    local step=''
    if [[ "$*" == *"$C849_READY.tmp-"* ]]; then step=publication; barrier before-publication
    elif [[ "$1" == "$SERVER2_ROOT/cache/stage-"* ]]; then step=recovery; fi
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
run() { local status=0; ( C913_CHILD="$BASHPID"; "$@" ) > "$root/out" 2>&1 || status=$?; printf '%s' "$status" > "$root/exit"; }
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
    ;;
 seed)
    ordinary "$root/donor"
    run c849_seed
    [ "$(cat "$root/exit")" = 0 ] && grep -Fxq schema=3 "$C849_READY" || fail schema3-published
    pass schema3-published
    for part in packages npm; do diff -r "$root/donor/$part" "$root/volumes/$([ "$part" = packages ] && echo "$C849_PACKAGES" || echo "$C849_NPM")/_data" || fail recipient-bytes; done
    [ "$(stat -c %a "$SERVER2_ROOT/cache/recovery-$RUN/packages/c913.tools/2.0.0/tool")" = 751 ] || fail recipient-executable
    printf 'MANIFEST_BASE64='; base64 -w0 "$SERVER2_ROOT/cache/recovery-$RUN/recovery.manifest"; printf '\n'
    printf 'MANIFEST_DIGEST=%s\n' "$(sha256sum "$SERVER2_ROOT/cache/recovery-$RUN/recovery.manifest" | cut -d' ' -f1)"
    cp "$SERVER2_ROOT/cache/recovery-$RUN/recovery.manifest" "$root/golden"
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
    for FAULT in stop copy-packages copy-npm npm-verify import corrupt-import corrupt-npm-import smoke recovery-save restart donor-id reconnect publication busy unknown process-error writer; do
      rm -f "$C849_READY"; rm -rf -- "$SERVER2_ROOT/cache/recovery-$RUN"
      for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
      : > "$root/trace"
      run c849_seed
      [ "$(cat "$root/exit")" != 0 ] && [ ! -e "$C849_READY" ] || fail "$FAULT-no-publication"
      [ "$(cat "$root/outside")" = outside ] || fail seed-cleanup-scope
      case "$FAULT" in
        busy|unknown|process-error|writer) ! grep -q '^stop$' "$root/trace" || fail "$FAULT-no-stop" ;;
        npm-verify) ! grep -q '^import$' "$root/trace" || fail npm-before-manifest ;;
      esac
      pass "$FAULT-no-publication"
    done
    FAULT=none
    for CRASH in before-stop after-stop after-copy-packages after-copy-npm after-npm-verify after-import-packages after-import-npm after-comparison before-smoke after-smoke after-recovery before-restart after-restart donor-id reconnect marker-write after-marker-write before-publication after-publication; do
      rm -f "$C849_READY" "$C849_READY.tmp-$RUN"; rm -rf -- "$SERVER2_ROOT/cache/recovery-$RUN"
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
        rm -f "$C849_READY"; rm -rf -- "$SERVER2_ROOT/cache/recovery-$RUN"
        for name in "$C849_PACKAGES" "$C849_NPM"; do find "$root/volumes/$name/_data" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; done
        : > "$root/trace"; : > "$root/docker-trace"
        run c849_seed
        if [ "$FAULT" = none ]; then
          accept "ordinary-saved-$saved_kind-accepted"
          grep -Fxq "donor-type=saved-$saved_kind" "$C849_READY" || fail saved-donor-type
          [ ! -e "$SERVER2_ROOT/cache/recovery-$RUN/packages/incomplete/1.0.0" ] || fail incomplete-stage-only
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
    ;;
 ready)
    make_full_marker
    run c849_require_ready allow-cold; accept schema3-valid
    cp "$C849_READY" "$root/marker"
    recovery="$SERVER2_ROOT/cache/recovery-$RUN"
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
      path="$SERVER2_ROOT/cache/recovery-$RUN/$part"; cp -p "$path" "$root/backup"
      printf mutation >> "$path"
      run c849_require_ready allow-cold; refuse recovery-bytes-changed CacheRecoveryChanged
      cp -p "$root/backup" "$path"
    done
    path="$SERVER2_ROOT/cache/recovery-$RUN/packages/c913.tools/2.0.0/tool"
    chmod 644 "$path"; run c849_require_ready allow-cold; refuse recovery-execute-changed CacheRecoveryChanged; chmod 751 "$path"
    mv "$C849_READY" "$root/valid-marker"; ln -s "$root/valid-marker" "$C849_READY"
    run c849_require_ready allow-cold; refuse marker-symlink CacheSeedMarkerInvalid
    rm "$C849_READY"; cp "$root/marker" "$C849_READY"
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
 prune)
    CASE=runner-cache-prune
    C590_PREVIEW_RUN="$RUN"; receipt="$SERVER2_ROOT/cache/previews/$RUN"; mkdir -p "$receipt"
    # Volume size and disk facts are external boundaries; execute all authority,
    # recovery, whole-target preflight, destructive and final-budget guards.
    c849_observe_volume() {
      local bytes=100000
      if [ "$FAULT" = budget ] && grep -q '^smoke$' "$root/trace"; then bytes=100001; fi
      printf '%s %s %s 1654:1654:700 %s 100000\n' "$1" "$2" "$root/volumes/$1/_data" "$bytes"
    }
    reset_prune() {
      FAULT=none
      rm -rf "$SERVER2_ROOT/cache/recovery-$RUN"; make_full_marker
      for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        rm -rf "$root/volumes/$name/_data"; mkdir -m700 "$root/volumes/$name/_data"
      done
      mkdir -p "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0"
      printf sentinel > "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0/data"
      printf sentinel > "$root/volumes/$C849_SCRATCH/_data/sentinel"
      printf sentinel > "$root/volumes/$C849_NPM/_data/sentinel"
      for pair in "$C849_PACKAGES:nuget-packages" "$C849_SCRATCH:nuget-scratch" "$C849_NPM:npm-content"; do c849_observe_volume "${pair%:*}" "${pair#*:}" 100000; done > "$receipt/volumes.txt"
      printf 'run=%s\nsource-sha=%s\ncreated-at=%s\nvolume-sha256=%s\n' "$RUN" "$SHA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)" > "$receipt/preview.txt"
      : > "$root/trace"; : > "$root/docker-trace"; rm -f "$CASE_DIR/prune.txt"
    }
    reset_prune
    run c849_prune; accept schema3-prune-success
    grep -Fq admission=held "$CASE_DIR/prune.txt" || fail admission-held
    [ -d "$root/volumes/$C849_PACKAGES/_data" ] && [ -d "$SERVER2_ROOT/cache/recovery-$RUN" ] || fail roots-and-recovery-retained
    [ ! -e "$root/volumes/$C849_PACKAGES/_data/ordinary/1.0.0/data" ] && [ ! -e "$root/volumes/$C849_NPM/_data/sentinel" ] || fail selected-contents-cleared
    ! grep -q 'for p in microsoft.netcore' "$root/docker-trace" || fail schema3-no-framework-copy
    for bad in missing-recovery missing-manifest changed-recovery image-missing source run age-future age-expired preview-digest volume-facts second-target nested-mount busy unknown writer process-error attachment broker-busy broker-error; do
      reset_prune
      diagnosis=CachePreviewStale
      case "$bad" in
        missing-recovery) rm -rf "$SERVER2_ROOT/cache/recovery-$RUN"; diagnosis=CacheRecoveryMissing ;;
        missing-manifest) rm "$SERVER2_ROOT/cache/recovery-$RUN/recovery.manifest"; diagnosis=CacheRecoveryMissing ;;
        changed-recovery) printf changed >> "$SERVER2_ROOT/cache/recovery-$RUN/npm/content"; diagnosis=CacheRecoveryChanged ;;
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
    for FAULT_CASE in refill refill-receipt smoke budget; do
      reset_prune; FAULT="$FAULT_CASE"
      case "$FAULT" in refill) diagnosis=CacheRefillFailed;; refill-receipt) diagnosis=CacheRefillReceiptMissing;; smoke) diagnosis=CacheSmokeFailed;; budget) diagnosis=CacheBudgetExceeded;; esac
      run c849_prune
      [ "$(cat "$root/exit")" != 0 ] && [ ! -e "$CASE_DIR/prune.txt" ] || fail "$FAULT-no-success"
      pass "$FAULT-no-success"
    done
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
    SOURCE=live; FAULT=none
    run case_runner_cache_inventory; accept present-temp-accepted
    : > "$root/cleanup-trace"
    printf 'c849%sowned-packages\nc849foreign-packages\n' "$RUN" > "$CASE_DIR/.fixture-volumes"
    (
      docker() { printf '%s\n' "$*" >> "$C913_ROOT/cleanup-trace"; }
      c849_fixture_cleanup
    )
    grep -Fq "volume rm c849${RUN}owned-packages" "$root/cleanup-trace" || fail owned-ledger-removed
    ! grep -Fq c849foreign-packages "$root/cleanup-trace" && [ "$(cat "$root/outside")" = outside ] || fail foreign-ledger-no-remove
    pass foreign-ledger-no-remove
    fixture_body="$(declare -f c849_fixture)"
    fixture_body="${fixture_body//\/tmp\/c849-fixture-/$root/fixture-}"
    eval "$fixture_body"
    mkdir "$root/fixture-$RUN"
    run c849_fixture; refuse occupied-fixture-refused FixtureNamespaceOccupied
    : > "$CASE_DIR/fixture-control-variants.txt"; : > "$CASE_DIR/fixture-controls.txt"
    intended_refusal() { printf 'ExpectedRefusal\n'; return 2; }
    run c849_fixture_control PC-01 ExpectedRefusal intended_refusal; accept intended-control-red
    setup_failure() { printf 'SetupError\n'; return 2; }
    run c849_fixture_control PC-02 ExpectedRefusal setup_failure; refuse setup-error-not-control-red ControlNotSensitive
    SOURCE=saved; FAULT=none
    : > "$root/docker-trace"; : > "$CASE_DIR/fixture-controls.txt"; : > "$CASE_DIR/fixture-control-variants.txt"
    run c849_fixture_apphost
    ! grep -Eq '^cp [0-9a-f]{12,64}:|^exec [0-9a-f]{12,64} .*\.nuget/packages' "$root/docker-trace" || fail no-production-payload-read
    accept fixture-owned-native-payload
    grep -Fxq 'PASS F-5' "$CASE_DIR/fixture-groups.txt" || fail fixture-native-recipient
    [ "$(cat "$SERVER2_ROOT/apphost/packages/c913.probe/1.0.0/data")" = ordinary-payload ] || fail fixture-generated-ordinary-payload
    pass no-production-payload-read
    ;;
 *) fail unknown-mode ;;
esac
[ ! -e "$root/denied" ] || fail unexpected-external-call
pass "$mode-complete"
