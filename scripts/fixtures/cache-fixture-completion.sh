# Sourced only by c913's isolated recipient model; production functions stay real.
if [ "$mode" = inventory-running ]; then
    SOURCE=saved
    declare -f docker | sed '1s/docker/completion_docker/' > "$root/completion-docker"
    source "$root/completion-docker"
    mkdir -p "$root/app/.nuget/packages" "$root/app/.npm/_cacache" "$root/app/.local/share/NuGet/http-cache" "$root/scratch"
    docker() {
        if [ "$1" = inspect ] && [[ "$3" == *'.State.Running'* ]]; then echo true; return; fi
        if [ "$1" = exec ] && [[ "$*" == *'dotnet nuget locals all --list'* ]]; then
            [[ "$*" == *'-u 1654:1654 -e HOME=/home/app'* ]] || return 94
            local code="${@: -1}"
            code="${code//\/home\/app/$root/app}"
            code="${code//\/tmp\/NuGetScratchapp/$root/scratch}"
            dotnet() { [ "$FAULT" != dotnet ] || return 41; printf 'nuget-locals\n'; }
            npm() { [ "$FAULT" != npm ] || return 42; printf 'npm-cache\n'; }
            du() { [ "$FAULT" != "du-${*: -1}" ] || return 43; command du "$@"; }
            stat() { [ "$FAULT" != stat ] || return 44; command stat "$@"; }
            export -f dotnet npm du stat
            FAULT="$FAULT" bash -c "$code"
            return
        fi
        completion_docker "$@"
    }
    run case_runner_cache_inventory; accept inventory-running-accepted
    grep -Fxq nuget-locals "$CASE_DIR/server2-cache-paths.txt" || fail inventory-running-output
    for FAULT in dotnet npm "du-$root/app/.nuget/packages" "du-$root/app/.npm/_cacache" "du-$root/app/.local/share/NuGet/http-cache" "du-$root/scratch" stat; do
        run case_runner_cache_inventory
        refuse inventory-probe-failed CacheInventoryPathsUnavailable
        ! grep -q 'RESULT true' "$root/out" || fail inventory-false-success
    done
    FAULT=none
    rmdir "$root/app/.npm/_cacache" "$root/app/.local/share/NuGet/http-cache" "$root/scratch"
    run case_runner_cache_inventory; accept inventory-absent-paths-accepted
    [ "$(grep -c '^absent ' "$CASE_DIR/server2-cache-paths.txt")" = 3 ] || fail inventory-absent-paths
    pass inventory-running-complete
    return
fi

CASE=runner-cache-fixture
C913_COMPLETION_IMAGE="$image_id"
c849_image() { printf '%s\n' "$C913_COMPLETION_IMAGE"; }
declare -f docker | sed '1s/docker/completion_docker/' > "$root/completion-docker"
source "$root/completion-docker"
docker() {
    if [ "$1:${2:-}" = image:inspect ]; then printf '%s\n' "$C913_COMPLETION_IMAGE"; return; fi
    completion_docker "$@"
}
printf 'runners=idle processes=0 leases=0\n' > "$SERVER2_ROOT/fixture-consumers.txt"
c849_observe_volume() {
    local budget="$3" bytes=80000
    case "$2" in
        nuget-packages) budget="${C849_FIXTURE_PACKAGES_BUDGET:-100000}" ;;
        nuget-scratch) budget="${C849_FIXTURE_SCRATCH_BUDGET:-100000}" ;;
        npm-content) budget="${C849_FIXTURE_NPM_BUDGET:-100000}" ;;
    esac
    printf '%s %s %s 1654:1654:700 %s %s\n' "$1" "$2" "$root/volumes/$1/_data" "$bytes" "$budget"
}
# Both backends execute the production preview, real path validation and real du.
if [[ "$mode" == preview-* ]]; then
    physical="$SERVER2_ROOT/cache/recovery-$RUN"
    [ "$mode" != preview-volume ] || physical="$root/volumes/c849${RUN}-work/_data/cache/recovery-$RUN"
    mkdir -p "$physical"; printf recovery-payload > "$physical/sentinel"
    logical="$(c849_payload_root)/recovery-$RUN"
    printf 'recovery=%s\n' "$logical" > "$C849_READY"
    run c849_preview; accept preview-valid
    expected=$(command du -s -B1 "$physical" | cut -f1)
    grep -Fxq "recovery-bytes=$expected" "$CASE_DIR/preview.txt" || fail preview-measures-payload
    [ "$(cat "$physical/sentinel")" = recovery-payload ] || fail preview-mutated-payload
    for fault in missing link escape noncanonical size; do
        rm -rf -- "$SERVER2_ROOT/cache/previews/$RUN"
        rm -f -- "$CASE_DIR/preview.txt"
        target="$logical"; diagnosis=CacheRecoveryMissing
        case "$fault" in
            missing) mv "$physical" "$physical.saved" ;;
            link) mv "$physical" "$physical.saved"; ln -s "$physical.saved" "$physical" ;;
            escape) target="$logical/../outside"; diagnosis=CacheRecoveryInvalid ;;
            noncanonical) target="$(c849_payload_root)/../cache/recovery-$RUN"; diagnosis=CacheRecoveryInvalid ;;
            size) FAULT=preview-size; du() { printf '123\tpartial\n'; return 45; }; export -f du; diagnosis=CacheRecoverySizeUnavailable ;;
        esac
        printf 'recovery=%s\n' "$target" > "$C849_READY"
        run c849_preview; refuse "preview-$fault-refused" "$diagnosis"
        [ ! -e "$CASE_DIR/preview.txt" ] && [ ! -e "$SERVER2_ROOT/cache/previews/$RUN" ] || fail preview-failure-published
        case "$fault" in
            missing) mv "$physical.saved" "$physical" ;;
            link) rm "$physical"; mv "$physical.saved" "$physical" ;;
            size) FAULT=none; unset -f du ;;
        esac
        [ "$(cat "$physical/sentinel")" = recovery-payload ] || fail preview-fault-mutated-payload
    done
    pass "$mode-complete"
    return
fi

# Actual F-8 producer, preview, ordinary PC-21..25 controls and prune body.
C849_PACKAGES="c849${RUN}smoke-packages"; C849_SCRATCH="c849${RUN}smoke-scratch"; C849_NPM="c849${RUN}npmcache-npm"
for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
    mkdir -m700 -p "$root/volumes/$name/_data"
    printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
done
ordinary "$root/prune-source"
cp -a "$root/prune-source/packages/." "$root/volumes/$C849_PACKAGES/_data/"
c1008_capture_json() { local output="$1"; shift; "$@" > "$output"; }
c849_fixture_refill() { c849_smoke "$1" fixture; }
: > "$CASE_DIR/fixture-controls.txt"; : > "$CASE_DIR/fixture-control-variants.txt"
if [ "$mode" = prune-diagnostic-preview ]; then
    c849_preview() { write_result false CacheRecoveryInvalid 2; }
elif [ "$mode" = prune-diagnostic-apply ]; then
    # Retain production controls; only the final application is refused.
    declare -f c849_prune | sed '1s/c849_prune/completion_prune/' > "$root/completion-prune"
    source "$root/completion-prune"
    c849_prune() {
        if [ -f "$CASE_DIR/.control-PC-25" ]; then write_result false CachePruneFailed 2; fi
        completion_prune
    }
fi
run c849_fixture_prune
if [[ "$mode" == prune-diagnostic-* ]]; then
    phase="${mode#prune-diagnostic-}"; diagnosis=CacheRecoveryInvalid; outer=FixturePrunePreviewFailed
    [ "$phase" != apply ] || { diagnosis=CachePruneFailed; outer=FixturePruneApplyFailed; }
    refuse inner-failure-propagated "$outer"
    grep -Fxq "diagnosis=$diagnosis" "$CASE_DIR/fixture-prune-$phase.txt" || fail inner-diagnostic-retained
    grep -Fxq exit=2 "$CASE_DIR/fixture-prune-$phase.txt" || fail inner-exit-retained
    pass inner-diagnostic-retained
else
    accept fixture-prune-accepted
    grep -Fxq 'PASS F-8' "$CASE_DIR/fixture-groups.txt" || fail fixture-prune-group
    for id in 21 22 23 24 25; do grep -q "^CONTROL PC-$id expected-red " "$CASE_DIR/fixture-controls.txt" || fail "real-PC-$id"; done
    [ -s "$SERVER2_ROOT/cache/previews/$RUN/preview.txt" ] && [ -s "$CASE_DIR/prune.txt" ] || fail fixture-prune-receipts
    pass fixture-prune-complete
fi
