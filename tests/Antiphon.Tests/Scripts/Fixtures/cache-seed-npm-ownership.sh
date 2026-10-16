# Sourced after the exact production functions by CacheSeedNpmOwnershipTests.
# No real Docker/SSH command is invoked. All filesystem writes stay in this mktemp root.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Native ownership proof requires Linux root (or WSL -u root).' >&2; exit 90; }
for tool in npm node setpriv stat find tar; do command -v "$tool" >/dev/null || exit 91; done
root="$(mktemp -d /tmp/antiphon-npm-ownership-XXXXXXXX)"
trap 'rm -rf -- "$root"' EXIT
chmod 0755 "$root"
mkdir "$root/cache" "$root/home" "$root/case" "$root/donor"
chown 1654:1654 "$root/cache" "$root/home"
chmod 0700 "$root/cache" "$root/home"
export HOME="$root/home" NPM_CONFIG_USERCONFIG="$root/absent-npmrc" NPM_CONFIG_GLOBALCONFIG="$root/absent-global-npmrc"
export NPM_CONFIG_UPDATE_NOTIFIER=false
RUN=npm-ownership
CASE_DIR="$root/case"
C849_READY="$root/seed-accepted"
C849_PACKAGES=test-packages C849_SCRATCH=test-scratch C849_NPM=test-npm
C590_COLD_SEED=0 C590_SAVED_DONOR=''
SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
CHECKOUT="$root"
SERVER2_ROOT="$root"

as_uid() { local uid="$1"; shift; setpriv --reuid="$uid" --regid="$uid" --clear-groups -- "$@"; }

# Qualify actual npm log creation and the kernel's refusal before testing production.
if [ "$fault" = success ]; then
    mkdir "$root/control"
    chown 1654:1654 "$root/control"
    as_uid 1654 mkdir "$root/control/npm-cache"
    npm cache verify --cache "$root/control/npm-cache" >/dev/null 2>&1
    [ "$(stat -c %u "$root/control/npm-cache/_logs")" = 0 ]
    find "$root/control/npm-cache/_logs" -type f -name '*debug*.log' | grep -q .
    if as_uid 1654 rm -rf -- "$root/control/npm-cache" 2>"$root/control-error"; then exit 92; fi
    grep -q 'Permission denied' "$root/control-error"
    echo ROOT_CONTROL=permission-denied
    rm -rf -- "$root/control"
fi

c849_payload_select() { echo volume; }
c849_payload_root() { echo "$root/cache"; }
c849_fixture_work_mount() { echo "type=volume,source=isolated-test,target=$root,volume-nocopy"; }
c849_fixture_helper_lookup() { return 0; }
c849_fixture_launch() {
    local user='' name='' network=''
    [ "$1" = create ] || return 93
    shift
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --user) user="$2"; shift 2 ;;
            --name) name="$2"; shift 2 ;;
            --network) network="$2"; shift 2 ;;
            *) shift ;;
        esac
    done
    [ "$network" = none ] && [ "$user" = 1654:1654 ] || return 94
    printf '%s\n' "${user%:*}" > "$root/$name.uid"
}
require_lane() { [ "$1" = host ]; }
c849_prepare() { :; }
c849_image() { echo isolated-image; }
c849_optional_donor() { echo donor; }
c849_status_zero() { :; }
c849_empty_volume() { :; }
c849_payload_call() {
    local role="$1" operation="$2"; shift 2
    case "$operation" in
        c849_validate_seed_tree) return 0 ;;
        c849_manifest_write) c849_fixture_helper_exec "$role" bash -c 'printf "test-manifest\n" > "$1"' bash "$2" ;;
        *) return 95 ;;
    esac
}
c849_compare_import() { :; }
c849_smoke() { :; }
c849_fixture_consumer_release() { :; }
c849_contract_receipt() { :; }
c849_status_body() { echo '{}'; }
jq() { cat; }
write_result() { printf 'RESULT=%s:%s\n' "$1" "$2"; exit "$3"; }

verify_npm() {
    local uid="$1"; shift
    local cache='' arg previous=''
    for arg in "$@"; do
        if [ "$previous" = --cache ]; then cache="$arg"; fi
        previous="$arg"
    done
    [[ "$cache" == "$root/cache/"*'/npm-cache' ]] || return 96
    as_uid "$uid" npm "$@" || return $?
    # Observe real inode owners before the production cleanup removes the evidence.
    stat -c 'NPM_LOG_OWNER=%u:%g' "$cache/_logs" >> "$root/observations"
    find "$cache/_logs" -type f -name '*debug*.log' | grep -q . || return 97
    find "$cache" -uid 0 -printf '%p\n' >> "$root/root-created"
    if [ "$fault" = verify ]; then echo FAULT=verify >> "$root/observations"; return 42; fi
}

docker() {
    local verb="$1"; shift
    case "$verb" in
        image) printf 'sha256:%064d\n' 0 ;;
        stop|start) return 0 ;;
        inspect)
            if [ "$2" = '{{.State.Running}}' ]; then echo false; else echo donor; fi ;;
        cp) tar -cf - -C "$root/donor" . ;;
        exec)
            [ "${1:-}" != -i ] || shift
            local container="$1"; shift
            if [ "$container" = donor ]; then echo '1654 sleep'; return; fi
            local uid
            uid="$(cat "$root/$container.uid")" || return 98
            if [ "$1" = npm ]; then shift; verify_npm "$uid" "$@"; return $?; fi
            if [ "$fault" = cleanup ] && [ "${3:-}" = 'rm -rf -- "$1/npm-cache"' ]; then
                echo FAULT=cleanup >> "$root/observations"
                return 43
            fi
            as_uid "$uid" "$@" ;;
        run)
            local entry='' uid=0
            while [ "$#" -gt 0 ]; do
                case "$1" in
                    --entrypoint) entry="$2"; shift 2 ;;
                    --user) uid="${2%:*}"; shift 2 ;;
                    --mount|--network) shift 2 ;;
                    --rm) shift ;;
                    *) shift; break ;;
                esac
            done
            if [ "$entry" = npm ]; then verify_npm "$uid" "$@"; return $?; fi
            [ "$entry" = /bin/sh ] || return 99 ;;
        *) echo "Unexpected Docker transport: $verb" >&2; return 100 ;;
    esac
}
c849_fixture_helper_acquire writer isolated-image isolated-work
c849_fixture_helper_acquire reader isolated-image isolated-work
# The later disposable smoke consumer is outside the ownership boundary under test.
c849_fixture_launch() { [ "$1" = run ]; }
set +e
( c849_seed ) > "$root/seed-output" 2>&1
status=$?
set -e
cat "$root/seed-output"
[ ! -e "$root/observations" ] || cat "$root/observations"
printf 'SEED_EXIT=%s\n' "$status"
if [ -e "$C849_READY" ]; then echo READY=present; else echo READY=absent; fi
if find "$root/cache" -name npm-cache | grep -q .; then echo NPM_CACHE=present; else echo NPM_CACHE=absent; fi
printf 'STAGES=%s\n' "$(find "$root/cache" -maxdepth 1 -name 'stage-*' | wc -l)"
printf 'ROOT_LEFTOVERS=%s\n' "$(find "$root/cache" -uid 0 | wc -l)"
# Even transient root-owned npm output fails the success test, not just surviving logs.
if [ -s "$root/root-created" ]; then echo ROOT_CREATED=yes; fi
echo HARNESS_COMPLETE
