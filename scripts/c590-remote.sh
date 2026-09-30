#!/bin/bash
# CARD-0590 live cases on server2, re-aimed by CARD-0604. ASCII only. No secrets in stdout.
#
# Two lanes (CARD-0604 D-5, S3):
#   host   - runs on server2's own shell over SSH, against the HOST daemon. It deploys and
#            inspects the persistent `antiphon-runner` project and never builds or runs a test.
#   nested - runs inside a session in the persistent runner, against the runner's own NESTED
#            daemon. It has no sudo and no python3, its compose project is run-scoped, and every
#            container it creates lives and dies on the nested daemon.
# A case declares its lane; running it in the other one is `WrongLane`, refused before any command.
set -euo pipefail

CASE="${C590_CASE:?}"
SHA="${C590_SHA:?}"
RUN="${C590_RUN:?}"
CHECKOUT="${C590_CHECKOUT:-/work/repos/antiphon}"
BRANCH="${C604_BRANCH:-master}"
EVIDENCE_ROOT="/work/test-evidence/${RUN}"
CASE_DIR="${EVIDENCE_ROOT}/${CASE}"
ROOT="/home/mc/antiphon-c590"
SERVER2_ROOT="/home/mc/antiphon-server2"
BASELINE_SHA="723ac9534fc3fce49b378e287da08b7b11095716"
HOST_PROJECT="antiphon-runner"
CHILD_PROJECT="c604${RUN}"
WROTE=0

# --- lane -------------------------------------------------------------------------------------
# A daemon whose Name is NOT this process's hostname is a SIBLING: its containers live in another
# network namespace, which is the shape CARD-0604 retires. That check alone cannot separate the two
# lanes, though, because a bare host's daemon reports the host's own hostname too - so "Name equals
# hostname" is true on server2's shell AND inside the runner. What separates them is whether this
# process is itself in a container, and /.dockerenv is the one marker Docker writes into every one.
LANE="unknown"
detect_lane() {
    local daemon_name own
    daemon_name="$(docker info --format '{{.Name}}' 2>/dev/null || true)"
    own="$(hostname 2>/dev/null || true)"
    if [ -z "$daemon_name" ]; then
        LANE="none"
    elif [ "$daemon_name" != "$own" ]; then
        LANE="sibling"
    elif [ -f /.dockerenv ]; then
        LANE="nested"
    else
        LANE="host"
    fi
    printf '%s\n' "$LANE"
}

require_lane() {
    local want="$1"
    if [ "$LANE" != "$want" ]; then
        write_result false "WrongLane want=$want lane=$LANE" 2
    fi
}

tag() { printf 'antiphon-c590-%s-%s' "$RUN" "$1"; }

scrub_file() {
    [ -f "$1" ] || return 0
    # Every GitHub token prefix, not just gho_: ghp_ (classic PAT), gho_ (OAuth), ghu_ (user-to-
    # server), ghs_ (server-to-server), ghr_ (refresh) and the fine-grained github_pat_ form.
    sed -i -E 's/gh[pousr]_[A-Za-z0-9_]+/gh_REDACTED/g; s/github_pat_[A-Za-z0-9_]+/github_pat_REDACTED/g; s/POSTGRES_PASSWORD=[^[:space:]]+/POSTGRES_PASSWORD=REDACTED/g; s/-----BEGIN [A-Z ]*PRIVATE KEY-----/PRIVATE_KEY_REDACTED/g' "$1" || true
}

json_escape() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/\t/\\t/g'
}

# Shell replacement for the old python3 context sentinel check (D-6).
context_check() {
    local root="$1" required="$2"
    if [ ! -f "$root/$required" ] && [ -d "$root/context" ]; then
        root="$root/context"
    fi
    local missing=0
    if [ ! -f "$root/$required" ]; then
        printf 'missing %s\n' "$required"
        missing=1
    fi
    local rel
    for rel in .git .antiphon/case.json client/.env.local scratch/auth.json .grok/config.toml \
        scratch/key.pfx scratch/key.pem server/bin/x.dll server/bin-pc/x.dll server/obj/x \
        client/node_modules/x workspace/owned.txt logs/test.log nested-git/.git/HEAD git-pointer/.git; do
        if [ -e "$root/$rel" ] || [ -L "$root/$rel" ]; then
            printf 'present %s\n' "$rel"
            missing=1
        fi
    done
    if [ "$missing" -ne 0 ]; then
        return 1
    fi
    printf 'context-ok %s\n' "$root"
}

write_result() {
    local accepted="$1" diagnosis="$2" code="$3"
    if [ "$WROTE" = 1 ]; then
        exit "$code"
    fi
    WROTE=1
    mkdir -p "$CASE_DIR"
    scrub_file "$CASE_DIR/build.log" || true
    scrub_file "$CASE_DIR/command.log" || true
    # Written in shell: the nested lane's image has no python3 (D-6, S3).
    printf '{"accepted":%s,"diagnosis":"%s","exit":%s}' \
        "$([ "$accepted" = "true" ] && printf 'true' || printf 'false')" \
        "$(json_escape "$diagnosis")" \
        "$code" > "$CASE_DIR/c590-result.json"
    printf 'DIAGNOSIS=%s\n' "$diagnosis"
    exit "$code"
}

ensure_dirs() {
    # The nested lane runs as uid 1654 inside the runner, which owns /work already and has no
    # sudo at all. Only the host lane's own shell may elevate.
    if [ "$LANE" = "host" ]; then
        sudo -n mkdir -p "$CASE_DIR" /work/repos "$ROOT/secrets" "$ROOT/baseline" "$SERVER2_ROOT/secrets"
        sudo -n chown -R mc:mc /work "$ROOT"
        # CARD-0660 (amended): the Codex home under the server2 root belongs to uid 1654 and holds
        # the runner's live sign-in. Handing it to mc would lock the runner out of it (0700), and
        # walking it would read its entries, so the reset prunes it: ensure_runner_codex_home owns it.
        sudo -n find "$SERVER2_ROOT" \( -path "$CODEX_HOME_PATH" -o -path "$SERVER2_ROOT/cache" \) -prune -o -exec chown -h mc:mc {} +
    fi
    mkdir -p "$CASE_DIR" "$EVIDENCE_ROOT"
    cat > /work/test-evidence/current.env <<EOF
C590_SHA=$SHA
C590_RUN=$RUN
C590_REEXEC=1
C590_CHECKOUT=$CHECKOUT
C604_BRANCH=$BRANCH
EOF
}

ensure_checkout() {
    if [ ! -d "$CHECKOUT/.git" ]; then
        git clone --filter=blob:none --branch "$BRANCH" https://github.com/michal-ciechan/Antiphon.git "$CHECKOUT"
    fi
    local current
    current="$(git -C "$CHECKOUT" rev-parse HEAD)"
    if [ "$current" != "$SHA" ]; then
        git -C "$CHECKOUT" fetch --filter=blob:none origin "$SHA"
        git -C "$CHECKOUT" checkout --detach "$SHA"
        git -C "$CHECKOUT" reset --hard "$SHA"
    fi
    if [ "$(git -C "$CHECKOUT" rev-parse HEAD)" != "$SHA" ]; then
        write_result false CheckoutShaMismatch 2
    fi
}

ensure_secrets() {
    if [ ! -s "$ROOT/secrets/stack.env" ]; then
        umask 077
        local password
        password="$(openssl rand -hex 24)"
        cat > "$ROOT/secrets/stack.env" <<EOF
POSTGRES_PASSWORD=$password
SOURCE_REVISION=$SHA
ANTIPHON_BIND_ADDRESS=127.0.0.1
ANTIPHON_BIND_PORT=5000
EOF
        unset password
    fi
    # Shell, not python3: the nested lane has no interpreter (D-6).
    if grep -q '^SOURCE_REVISION=' "$ROOT/secrets/stack.env"; then
        sed -i -E "s|^SOURCE_REVISION=.*|SOURCE_REVISION=$SHA|" "$ROOT/secrets/stack.env"
    else
        printf 'SOURCE_REVISION=%s\n' "$SHA" >> "$ROOT/secrets/stack.env"
    fi
    sed -i '/^DOCKER_SOCKET_GID=/d' "$ROOT/secrets/stack.env"
}

# CARD-0604 D-7: the throwaway stack is the NESTED child - the unmodified base compose file with
# sha-tagged child images, on the nested daemon, in a run-scoped project. The child runner is the
# socket-free `runtime` target: no engine, no SDK, no custody helpers.
write_child_override() {
    local server runner
    server="$(tag child-server)"
    runner="$(tag child-runner)"
    cat > "$ROOT/compose.child.yml" <<EOF
services:
  state-init:
    image: ${server}
    labels:
      c604-run: "${RUN}"
      c604-owner: child
  antiphon:
    image: ${server}
    labels:
      c604-run: "${RUN}"
      c604-owner: child
    environment:
      GIT_CONFIG_GLOBAL: /work/gitconfig
    healthcheck:
      start_period: 90s
      interval: 5s
      timeout: 5s
      retries: 40
  session-runner:
    image: ${runner}
    labels:
      c604-run: "${RUN}"
      c604-owner: child
    environment:
      GIT_CONFIG_GLOBAL: /work/gitconfig
  postgres:
    labels:
      c604-run: "${RUN}"
      c604-owner: child
EOF
}

# The child project is run-scoped and can never collide with the persistent host project.
compose_child() {
    COMPOSE_PROJECT_NAME="$CHILD_PROJECT" \
    ANTIPHON_BIND_PORT="${CHILD_BIND_PORT:-5000}" \
    ANTIPHON_BIND_ADDRESS=127.0.0.1 \
    docker compose \
        --env-file "$ROOT/secrets/stack.env" \
        -f "$CHECKOUT/docker-compose.yml" \
        -f "$ROOT/compose.child.yml" \
        "$@"
}

build_image() {
    local name="$1" dockerfile="$2" context="$3" target="${4:-}"
    local image logfile
    image="$(tag "$name")"
    logfile="$CASE_DIR/build.log"
    if docker image inspect "$image" >/dev/null 2>&1; then
        local have
        have="$(docker image inspect -f '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image" 2>/dev/null || true)"
        if [ "$have" = "$SHA" ]; then
            echo "reused $image" >> "$logfile"
            return 0
        fi
        local envrev
        envrev="$(docker image inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$image" | sed -n 's/^SOURCE_REVISION=//p' | head -n 1)"
        if [ "$envrev" = "$SHA" ]; then
            echo "reused $image" >> "$logfile"
            return 0
        fi
    fi
    local -a args
    args=(build -f "$dockerfile" --build-arg "SOURCE_REVISION=$SHA" -t "$image")
    if [ -n "$target" ]; then
        args+=(--target "$target")
    fi
    args+=("$context")
    if ! docker "${args[@]}" >> "$logfile" 2>&1; then
        return 1
    fi
}

require_image_sha() {
    local image="$1"
    local have envrev
    have="$(docker image inspect -f '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image" 2>/dev/null || true)"
    envrev="$(docker image inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$image" | sed -n 's/^SOURCE_REVISION=//p' | head -n 1)"
    if [ "$have" != "$SHA" ] && [ "$envrev" != "$SHA" ]; then
        write_result false RevisionMismatch 2
    fi
}

run_sh() {
    local image="$1"
    local script="$2"
    docker run --rm -i --user 0 --network none --entrypoint /bin/sh "$image" -s <<EOF
set -eu
$script
EOF
}

copy_checkout_into_volume() {
    local vol="${CHILD_PROJECT}_work"
    local marker
    marker="$(docker run --rm --user 0 --entrypoint /bin/sh -v "${vol}:/work" "$(tag child-server)" -c 'cat /work/repos/antiphon/.c590-sha 2>/dev/null || true')"
    if [ "$marker" = "$SHA" ]; then
        return 0
    fi
    docker run --rm --user 0 --entrypoint /bin/sh \
        -v "$CHECKOUT:/from:ro" \
        -v "${vol}:/work" \
        "$(tag child-server)" -c "
            set -eu
            mkdir -p /work/repos
            rm -rf /work/repos/antiphon
            cp -a /from /work/repos/antiphon
            printf '%s\n' '$SHA' > /work/repos/antiphon/.c590-sha
            chown -R 1654:1654 /work/repos/antiphon
            printf '[safe]\n\tdirectory = *\n' > /work/gitconfig
            chown 1654:1654 /work/gitconfig
            chmod 644 /work/gitconfig
        "
}

wait_url() {
    local url="$1" tries="${2:-80}"
    local i
    for i in $(seq 1 "$tries"); do
        if curl -fsS "$url" >/dev/null 2>&1; then
            return 0
        fi
        sleep 5
    done
    return 1
}

# CARD-0604 D-7: the child stack is depth one, on the NESTED daemon, in the run-scoped project.
# Its bind port lives on the runner's own loopback, so nothing it publishes leaves the container.
CHILD_BIND_PORT="${C604_CHILD_PORT:-5000}"
CHILD_BASE="http://127.0.0.1:${CHILD_BIND_PORT}"

ensure_child() {
    require_lane nested
    ensure_secrets
    build_image child-server "$CHECKOUT/Dockerfile" "$CHECKOUT" || write_result false ChildServerBuildFailed 2
    build_image child-runner "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" runtime || write_result false ChildRunnerBuildFailed 2
    write_child_override
    compose_child up -d --no-build >> "$CASE_DIR/command.log" 2>&1 || {
        compose_child logs --no-color --tail 80 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false ChildComposeFailed 2
    }
    if ! wait_url "$CHILD_BASE/health"; then
        compose_child logs --no-color --tail 120 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false ChildUnhealthy 2
    fi
    copy_checkout_into_volume
}

# Shell, not python3 (D-6): the nested lane has no interpreter.
version_sha() {
    curl -fsS "${1:-$CHILD_BASE}/api/version" \
        | tr ',' '\n' | sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1
}

case_baseline() {
    ensure_checkout
    if [ ! -f "$ROOT/baseline/Dockerfile" ]; then
        git -C "$CHECKOUT" fetch --depth 1 origin "$BASELINE_SHA"
        rm -rf "$ROOT/baseline"
        mkdir -p "$ROOT/baseline"
        git -C "$CHECKOUT" archive FETCH_HEAD | tar -x -C "$ROOT/baseline"
    fi
    local image code
    image="$(tag baseline-old)"
    set +e
    docker build -f "$ROOT/baseline/Dockerfile" -t "$image" "$ROOT/baseline" > "$CASE_DIR/build.log" 2>&1
    code=$?
    set -e
    if [ "$code" -eq 0 ]; then
        docker image rm "$image" >/dev/null 2>&1 || true
        write_result false BaselineUnexpectedlySucceeded 2
    fi
    if [ ! -s "$CASE_DIR/build.log" ]; then
        write_result false BaselineLogMissing 2
    fi
    if docker image inspect "$image" >/dev/null 2>&1; then
        write_result false BaselineImageTagged 2
    fi
    local step
    step="$(grep -E 'ERROR|error CS|error MSB|npm ERR|failed to' "$CASE_DIR/build.log" | tail -n 1 || true)"
    if [ -z "$step" ]; then
        step="$(grep -E '.' "$CASE_DIR/build.log" | tail -n 1 || true)"
    fi
    if [ -z "$step" ]; then
        write_result false BaselineStepMissing 2
    fi
    printf '%s\n' "$step" > "$CASE_DIR/failing-step.txt"
    write_result true "$step" 0
}

case_server_payload() {
    build_image server "$CHECKOUT/Dockerfile" "$CHECKOUT" || write_result false ServerBuildFailed 2
    local image
    image="$(tag server)"
    require_image_sha "$image"
    run_sh "$image" '
        test -f /app/Antiphon.Server.dll
        test -f /app/wwwroot/index.html
        test ! -e /app/Antiphon.DockerStack.Fixture.dll
        grep -a -q delegate-basics /app/Antiphon.Server.dll
        test ! -d /src/tests
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false ServerPayloadMissing 2
    docker run --rm --user 1654:1654 --network none --entrypoint /bin/sh "$image" -c 'test -r /app/Antiphon.Server.dll' \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false ServerNotReadable 2
    write_result true '' 0
}

case_runner_payload() {
    build_image runner "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" runtime || write_result false RunnerBuildFailed 2
    local image
    image="$(tag runner)"
    require_image_sha "$image"
    run_sh "$image" '
        test -x /app/Antiphon.PtyHost
        test -f /app/libporta_pty.so
        test -f /app/Antiphon.PtyHost.runtimeconfig.json
        test -f /app/Antiphon.PtyHost.deps.json
        test ! -e /usr/local/bin/docker
        test ! -e /opt/antiphon-tests/fakegrok/fakegrok
        test ! -e /app/Antiphon.DockerStack.Fixture.dll
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false RunnerPayloadMissing 2
    write_result true '' 0
}

case_testing_payload() {
    build_image session-testing "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" session-testing || write_result false TestingBuildFailed 2
    local image
    image="$(tag session-testing)"
    require_image_sha "$image"
    # CARD-0604 V-1 (live) and R-5/G-40. Every payload the persistent runner needs is present,
    # INCLUDING Cut B's custody mechanism -- this image is now a supported producer. What must
    # never appear in it is the Windows backend: a session-testing image whose advertisement
    # mentions windows-job-v1 would be claiming a job object it has no kernel for.
    run_sh "$image" '
        test -x /usr/local/bin/docker
        test -x /usr/local/lib/docker/cli-plugins/docker-compose
        test -x /usr/local/bin/dockerd
        test -x /usr/local/bin/containerd
        test -x /usr/local/bin/containerd-shim-runc-v2
        test -x /usr/local/bin/runc
        test -x /usr/local/bin/docker-init
        test -x /usr/local/bin/docker-proxy
        test -x /usr/local/bin/ctr
        test -x /usr/local/bin/pwsh
        test -x /usr/local/bin/antiphon-dind-entrypoint.sh
        test -x /usr/local/bin/antiphon-custody-enter
        test -x /usr/local/bin/antiphon-custody-kill
        test -f /etc/sudoers.d/antiphon-custody
        test "$(stat -c %U:%G:%a /usr/local/bin/antiphon-custody-enter)" = "root:root:755"
        test "$(stat -c %U:%G:%a /usr/local/bin/antiphon-custody-kill)" = "root:root:755"
        test "$(stat -c %U:%G:%a /etc/sudoers.d/antiphon-custody)" = "root:root:440"
        test -f /etc/docker/daemon.json
        test -f /etc/antiphon/ssh_config
        test -f /etc/antiphon/github_known_hosts
        test -f /etc/gitconfig
        test -x /opt/antiphon-tests/fakegrok/fakegrok
        test -x /opt/antiphon-tests/fakegrok-linux.sh
        getent group docker-nested
        iptables --version | grep -q legacy
        dotnet --list-sdks | grep -q "^10\."
        dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 9\."
        dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 10\."
        node --version
        ssh -V
        grep -q linux-cgroup-v1 /usr/local/bin/antiphon-custody-enter || true
        ! grep -a -q windows-job-v1 /etc/sudoers.d/antiphon-custody
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false TestingPayloadMissing 2
    # The stage ends USER 0:0 so the entrypoint can start dockerd, and drops to 1654 itself; a
    # session still runs as 1654, which is what this asserts.
    docker run --rm --user 1654:1654 --network none --entrypoint /bin/sh "$image" -c 'id -u' > "$CASE_DIR/uid.txt"
    if [ "$(tr -d "[:space:]" < "$CASE_DIR/uid.txt")" != "1654" ]; then
        write_result false TestingNotNonRoot 2
    fi
    # R-5/G-40: the runtime and receipt-probe targets gain none of it -- no engine, no sudo,
    # and no custody helpers. A sudo grant in an image with no custody root to constrain
    # anything is a grant with everything to lose and nothing to gain.
    build_image runner "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" runtime || write_result false RunnerBuildFailed 2
    run_sh "$(tag runner)" '
        test ! -e /usr/local/bin/docker
        test ! -e /usr/local/bin/dockerd
        test ! -e /usr/local/bin/antiphon-dind-entrypoint.sh
        test ! -e /etc/docker/daemon.json
        test ! -e /usr/bin/sudo
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false RuntimeGainedTestPayload 2
    write_result true '' 0
}

case_child_server() {
    build_image child-server "$CHECKOUT/Dockerfile" "$CHECKOUT" || write_result false ChildServerBuildFailed 2
    local image
    image="$(tag child-server)"
    require_image_sha "$image"
    docker image inspect -f '{{.Id}}' "$image" > "$CASE_DIR/image-id.txt"
    printf '%s\n' "$SHA" > "$CASE_DIR/source-sha.txt"
    run_sh "$image" 'test -f /app/Antiphon.Server.dll && test -f /app/wwwroot/index.html' \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false ChildServerPayloadMissing 2
    write_result true '' 0
}

case_child_runner() {
    build_image child-runner "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" runtime || write_result false ChildRunnerBuildFailed 2
    local image
    image="$(tag child-runner)"
    require_image_sha "$image"
    run_sh "$image" '
        test -x /app/Antiphon.PtyHost
        test ! -e /usr/local/bin/docker
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false ChildRunnerPayloadMissing 2
    write_result true '' 0
}

case_test_image() {
    build_image test-runner "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" test-runner || write_result false TestImageBuildFailed 2
    local image
    image="$(tag test-runner)"
    require_image_sha "$image"
    run_sh "$image" '
        test -f /src/Antiphon.sln
        test -f /src/tests/Shared/TestClassificationMetadata.cs
        test -f /src/scripts/test-client.ps1
        test ! -e /src/.git
        test ! -d /src/client/node_modules
        command -v pwsh
        command -v git
        command -v node
        command -v npm
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false TestImagePayloadMissing 2
    write_result true '' 0
}

case_receipt_runner() {
    build_image receipt-probe "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" receipt-probe || write_result false ReceiptRunnerBuildFailed 2
    local image
    image="$(tag receipt-probe)"
    require_image_sha "$image"
    run_sh "$image" '
        test -x /opt/antiphon-tests/fakegrok/fakegrok
        test -x /opt/antiphon-tests/fakegrok-linux.sh
        test ! -e /usr/local/bin/docker
        test ! -e /usr/local/lib/docker/cli-plugins/docker-compose
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false ReceiptRunnerPayloadMissing 2
    write_result true '' 0
}

case_fixture_image() {
    build_image delivery-fixture "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" delivery-fixture || write_result false FixtureBuildFailed 2
    local image
    image="$(tag delivery-fixture)"
    require_image_sha "$image"
    run_sh "$image" '
        test -f /fixture/Antiphon.DockerStack.Fixture.dll
        test -f /fixture/Antiphon.Server.dll
        test -f /fixture/wwwroot/index.html
        test ! -e /usr/local/bin/docker
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false FixturePayloadMissing 2
    write_result true '' 0
}

case_deployment_state() {
    require_lane nested
    ensure_child
    local live
    live="$(version_sha)"
    printf '%s\n' "$live" > "$CASE_DIR/version.txt"
    if [ "$live" != "$SHA" ]; then
        write_result false RevisionMismatch 2
    fi
    curl -fsS "$CHILD_BASE/" | head -c 400 > "$CASE_DIR/index-head.txt" || write_result false UiMissing 2
    if ! grep -q -E 'html|script|Antiphon' "$CASE_DIR/index-head.txt"; then
        write_result false UiMissing 2
    fi
    compose_child exec -T postgres psql -U antiphon -d antiphon -c 'CREATE TABLE IF NOT EXISTS c590_marker(id int primary key); INSERT INTO c590_marker VALUES (1) ON CONFLICT DO NOTHING;' \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false MarkerInsertFailed 2
    docker run --rm --user 0 --entrypoint /bin/sh -v "${CHILD_PROJECT}_work:/work" "$(tag child-server)" -c 'echo marker > /work/c590-workspace-marker && chown 1654:1654 /work/c590-workspace-marker'
    compose_child stop >> "$CASE_DIR/command.log" 2>&1
    compose_child up -d --no-build >> "$CASE_DIR/command.log" 2>&1
    if ! wait_url "$CHILD_BASE/health"; then
        write_result false RestartUnhealthy 2
    fi
    local marker_count workspace
    marker_count="$(compose_child exec -T postgres psql -U antiphon -d antiphon -tAc 'SELECT count(*) FROM c590_marker;')"
    workspace="$(docker run --rm --user 0 --entrypoint /bin/sh -v "${CHILD_PROJECT}_work:/work" "$(tag child-server)" -c 'cat /work/c590-workspace-marker')"
    if [ "$(echo "$marker_count" | tr -d '[:space:]')" != "1" ] || [ "$workspace" != "marker" ]; then
        write_result false RetentionFailed 2
    fi
    docker volume inspect "${CHILD_PROJECT}_pgdata" >/dev/null
    # The child is torn down with its own volumes: it is the throwaway stack, not a deployment.
    compose_child down -v --remove-orphans >> "$CASE_DIR/command.log" 2>&1 || true
    write_result true '' 0
}

case_client_lint() {
    case_test_image
    # case_test_image exits on success. Reached only if write_result did not exit.
}

# CARD-0604 D-6: the session's own shell runs the roster. The runner image carries SDK 10,
# runtimes 9 and 10, Node 22 and pwsh, so there is no test container and no sibling socket; the
# sibling test lane is retired. Testcontainers reaches the nested daemon on the default endpoint
# and maps its ports onto this very process's loopback (D-5), unmodified.
run_client() {
    local script="$1"
    ( cd "$CHECKOUT" && bash -lc "$script" ) > "$CASE_DIR/command.log" 2>&1
}

case_client_lint_body() {
    require_lane nested
    if ! run_client 'npm --prefix client ci && npm --prefix client run build && npm --prefix client run lint'; then
        write_result false ClientLintFailed 2
    fi
    write_result true '' 0
}

case_client_tests_body() {
    require_lane nested
    if ! run_client "npm --prefix client ci && pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath '$CASE_DIR/client.json'"; then
        write_result false ClientTestsFailed 2
    fi
    if [ ! -s "$CASE_DIR/client.json" ]; then
        write_result false ClientJsonMissing 2
    fi
    # Shell, not python3 (D-6). The frozen Vitest floor is 102 files with zero failures.
    local total failed
    total="$(tr ',' '\n' < "$CASE_DIR/client.json" | sed -n 's/.*"numTotalTests"[[:space:]]*:[[:space:]]*\([0-9]*\).*/\1/p' | head -n 1)"
    failed="$(tr ',' '\n' < "$CASE_DIR/client.json" | sed -n 's/.*"numFailedTests"[[:space:]]*:[[:space:]]*\([0-9]*\).*/\1/p' | head -n 1)"
    printf 'total=%s failed=%s\n' "${total:-0}" "${failed:-0}" > "$CASE_DIR/client-counts.txt"
    if [ -z "$total" ] || [ "$total" -lt 102 ] || [ "${failed:-1}" -ne 0 ]; then
        write_result false ClientTestsShort 2
    fi
    write_result true '' 0
}

run_dotnet() {
    local project="$1" filter="$2" no_build="$3" min="$4" name="$5" broker="$6"
    local broker_env="0"
    if [ "$broker" = "1" ]; then broker_env="1"; fi
    local build_args=()
    if [ "$no_build" = "1" ]; then
        build_args+=(-NoBuild)
    fi
    (
        cd "$CHECKOUT"
        SessionRunner__BaseUrl=http://127.0.0.1:1 \
        SessionRunner__Enabled=false \
        MSBUILDDISABLENODEREUSE=1 \
        DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        NUGET_PACKAGES=/work/.nuget \
        ANTIPHON_BROKER_TESTS="$broker_env" \
        pwsh -NoProfile -File scripts/run-checkpoint.ps1 \
            -Name "$name" \
            -Project "$project" \
            -OutputPath bin-c604/ \
            -Filter "$filter" \
            -ResultsRoot "$CASE_DIR/trx" \
            -MinExecuted "$min" \
            "${build_args[@]}"
    ) > "$CASE_DIR/command.log" 2>&1
}

case_dotnet() {
    local filter=""
    if [ -n "${C590_FILTER_FILE:-}" ] && [ -f "$C590_FILTER_FILE" ]; then
        filter="$(tr -d '\r' < "$C590_FILTER_FILE")"
    fi
    if [ -z "$filter" ]; then
        write_result false FilterMissing 2
    fi
    require_lane nested
    local code=0
    run_dotnet "${C590_PROJECT:?}" "$filter" "${C590_NOBUILD:-0}" "${C590_MIN:-1}" "${C590_CP:-dotnet}" "${C590_BROKER:-0}" || code=$?
    if [ "$code" -ne 0 ]; then
        write_result false "DotnetFilterFailed exit=$code" "$code"
    fi
    write_result true '' 0
}

case_messaging() {
    export C590_PROJECT="tests/Antiphon.Messaging.Tests"
    export C590_NOBUILD=0
    export C590_BROKER=1
    export C590_CP="${C590_CP:-CP-19}"
    if [ ! -f "${C590_FILTER_FILE:-}" ]; then
        printf '%s\n' '/*/*/*/*' > "$CASE_DIR/messaging-filter.txt"
        export C590_FILTER_FILE="$CASE_DIR/messaging-filter.txt"
    fi
    case_dotnet
}

case_denied_socket() {
    build_image test-runner "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" test-runner || write_result false TestImageBuildFailed 2
    set +e
    docker run --rm --network none --entrypoint /bin/sh "$(tag test-runner)" -c 'command -v docker || ls /var/run/docker.sock' \
        > "$CASE_DIR/command.log" 2>&1
    local code=$?
    set -e
    if [ "$code" -eq 0 ] && grep -q /var/run/docker.sock "$CASE_DIR/command.log"; then
        write_result false SocketStillVisible 2
    fi
    write_result true '' 0
}

case_cleanup() {
    require_lane nested
    local name="c604-${RUN}-child"
    docker run -d --name "$name" --label "c604-run=$RUN" --label "c604-owner=child" alpine:3.20 sleep 600 \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false ChildCreateFailed 2
    local id
    id="$(docker inspect -f '{{.Id}}' "$name")"
    printf '%s\n' "$id" > "$CASE_DIR/child-id.txt"
    # Export the run's evidence index before the child goes, so an interrupted copy is visible.
    find "$EVIDENCE_ROOT" -maxdepth 2 -type d > "$CASE_DIR/evidence-index.txt"
    docker rm -f "$name" >> "$CASE_DIR/command.log" 2>&1
    if docker inspect "$id" >/dev/null 2>&1; then
        write_result false ChildSurvived 2
    fi
    # The runner that owns this nested daemon must be untouched by its child's removal.
    if ! docker info >/dev/null 2>&1; then
        write_result false NestedDaemonLost 2
    fi
    write_result true '' 0
}

case_interrupted() {
    local name="c604-${RUN}-residue"
    docker run -d --name "$name" --label "c604-run=$RUN" --label "c604-owner=child" alpine:3.20 sleep 600 \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false ResidueCreateFailed 2
    local id
    id="$(docker inspect -f '{{.Id}}' "$name")"
    printf '%s\n' "$id" > "$CASE_DIR/owned-id.txt"
    printf 'incomplete\n' > "$CASE_DIR/export-state.txt"
    if ! docker inspect "$id" >/dev/null 2>&1; then
        write_result false ResidueMissing 2
    fi
    local label
    label="$(docker inspect -f '{{index .Config.Labels "c604-owner"}}' "$id")"
    if [ "$label" != "child" ]; then
        write_result false IdentityChanged 2
    fi
    docker rm -f "$name" >> "$CASE_DIR/command.log" 2>&1
    if docker inspect "$id" >/dev/null 2>&1; then
        write_result false ResidueSurvived 2
    fi
    write_result true '' 0
}

context_probe() {
    local ignore_source="$1" name="$2" required="$3"
    local image ctx
    image="$(tag "$name")"
    ctx="$ROOT/context-$name"
    rm -rf "$ctx"
    mkdir -p "$ctx"
    cp "$ignore_source" "$ctx/probe.Dockerfile.dockerignore"
    cat > "$ctx/probe.Dockerfile" <<'EOF'
FROM busybox:1.36
COPY . /context
EOF
    # Sentinels live beside a copy of the ignore rules. The real checkout is the context
    # so Docker applies the ignore file to the files the engine actually sees.
    local sentinels=(
        ".antiphon/case.json"
        "client/.env.local"
        "scratch/auth.json"
        ".grok/config.toml"
        "scratch/key.pfx"
        "scratch/key.pem"
        "server/bin/x.dll"
        "server/bin-pc/x.dll"
        "server/obj/x"
        "client/node_modules/x"
        "workspace/owned.txt"
        "logs/test.log"
        "nested-git/.git/HEAD"
        "git-pointer/.git"
    )
    local rel
    for rel in "${sentinels[@]}"; do
        mkdir -p "$CHECKOUT/$(dirname "$rel")"
        printf 'sentinel\n' > "$CHECKOUT/$rel"
    done
    cp "$ctx/probe.Dockerfile" "$CHECKOUT/probe.Dockerfile"
    cp "$ctx/probe.Dockerfile.dockerignore" "$CHECKOUT/probe.Dockerfile.dockerignore"
    cleanup_sentinels() {
        local rel
        rm -f "$CHECKOUT/probe.Dockerfile" "$CHECKOUT/probe.Dockerfile.dockerignore"
        for rel in "${sentinels[@]}"; do
            rm -rf "$CHECKOUT/$rel"
        done
        rm -rf "$CHECKOUT/git-pointer" "$CHECKOUT/nested-git" "$CHECKOUT/scratch" \
            "$CHECKOUT/.grok" "$CHECKOUT/.antiphon" "$CHECKOUT/workspace" \
            "$CHECKOUT/logs" "$CHECKOUT/server/bin-pc" "$CHECKOUT/client/node_modules"
    }
    set +e
    docker build -f "$CHECKOUT/probe.Dockerfile" -t "$image" "$CHECKOUT" > "$CASE_DIR/build.log" 2>&1
    local code=$?
    set -e
    cleanup_sentinels
    if [ "$code" -ne 0 ]; then
        write_result false ContextProbeBuildFailed 2
    fi
    local cid
    cid="$(docker create "$image")"
    rm -rf "$CASE_DIR/context"
    mkdir -p "$CASE_DIR/context"
    docker cp "$cid:/context" "$CASE_DIR/context-copy" >> "$CASE_DIR/command.log" 2>&1
    docker rm "$cid" >/dev/null
    # Shell, not python3 (D-6): the nested lane's image has no interpreter.
    if ! context_check "$CASE_DIR/context-copy" "$required" > "$CASE_DIR/context-check.txt"
    then
        docker image rm "$image" >/dev/null 2>&1 || true
        write_result false ContextSentinelLeak 2
    fi
    rm -rf "$CASE_DIR/context" "$CASE_DIR/context-copy"
    docker image rm "$image" >/dev/null 2>&1 || true
    write_result true '' 0
}

case_git_smoke() {
    # CARD-0604 D-8. The push credential is the repo-scoped deploy key that lives only on server2,
    # materialised by the entrypoint at /run/antiphon/deploy-key (0400, uid 1654) on a tmpfs, and
    # wired through the baked /etc/gitconfig and /etc/antiphon/ssh_config. The smoke runs in the
    # session's own shell: there is no desktop-sourced token and no per-run secret bind any more.
    require_lane nested
    if [ ! -r /run/antiphon/deploy-key ]; then
        write_result false DeployKeyUnavailable 2
    fi
    local perms
    perms="$(stat -c '%u %a' /run/antiphon/deploy-key)"
    printf '%s\n' "$perms" > "$CASE_DIR/deploy-key-mode.txt"
    if [ "$perms" != "1654 400" ]; then
        write_result false DeployKeyMode 2
    fi
    ssh -F /etc/antiphon/ssh_config -T git@github.com > "$CASE_DIR/ssh-banner.txt" 2>&1 || true
    scrub_file "$CASE_DIR/ssh-banner.txt"
    if ! grep -q 'successfully authenticated' "$CASE_DIR/ssh-banner.txt"; then
        write_result false DeployKeyNotAuthenticated 2
    fi

    local branch="throwaway/c604-credential-smoke-$RUN"
    local work="/tmp/c604-smoke-$RUN"
    rm -rf "$work"
    # Fetches stay anonymous HTTPS; only the push goes over SSH (gitconfig pushInsteadOf).
    if ! git clone --depth 1 --branch "$BRANCH" https://github.com/michal-ciechan/Antiphon.git "$work" \
        >> "$CASE_DIR/command.log" 2>&1; then
        scrub_file "$CASE_DIR/command.log"
        write_result false GitCloneFailed 2
    fi
    if ! (
        set -eu
        cd "$work"
        git checkout -b "$branch"
        printf 'c604 credential smoke %s\n' "$RUN" > c604-credential-smoke.txt
        git add c604-credential-smoke.txt
        git -c user.name=c604-smoke -c user.email=c604-smoke@localhost commit -m "test(CARD-0604): credential smoke $RUN"
        git push origin "HEAD:$branch"
        git rev-parse HEAD
    ) > "$CASE_DIR/push.txt" 2>> "$CASE_DIR/command.log"; then
        scrub_file "$CASE_DIR/command.log"
        write_result false GitPushFailed 2
    fi
    scrub_file "$CASE_DIR/command.log"
    local pushed
    pushed="$(tail -n 1 "$CASE_DIR/push.txt" | tr -d '[:space:]')"
    printf '%s\n' "$pushed" > "$CASE_DIR/pushed-sha.txt"
    printf '%s\n' "$branch" > "$CASE_DIR/branch.txt"
    if [ "${#pushed}" -ne 40 ]; then
        write_result false GitPushShaMissing 2
    fi

    # The smoke owns its branch and deletes it: a throwaway/ branch left on origin is residue.
    if ! git -C "$work" push origin --delete "$branch" >> "$CASE_DIR/command.log" 2>&1; then
        scrub_file "$CASE_DIR/command.log"
        write_result false SmokeBranchNotDeleted 2
    fi
    if git -C "$work" ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
        write_result false SmokeBranchSurvived 2
    fi
    printf 'true\n' > "$CASE_DIR/deleted.txt"
    rm -rf "$work"
    write_result true '' 0
}

case_handoff() {
    # CARD-0604 V-23: after the desktop CLI disconnects, the persistent runner is still up, still
    # registered with production, and the run's evidence index is complete. Host lane: this is
    # about the standing deployment, not about any stack this run created.
    require_lane host
    docker ps --filter "name=${HOST_PROJECT}-session-runner" --format '{{.Names}} {{.Status}}' > "$CASE_DIR/ps.txt"
    if ! grep -q "$HOST_PROJECT" "$CASE_DIR/ps.txt"; then
        write_result false RunnerNotRunning 2
    fi
    if ! grep -qi 'up' "$CASE_DIR/ps.txt"; then
        write_result false RunnerNotUp 2
    fi
    if ! curl -fsS "${C604_SERVER_ORIGIN:?}/api/session-runners/server2/status" > "$CASE_DIR/status.json"; then
        write_result false RunnerStatusUnavailable 2
    fi
    if ! grep -q '"available"[[:space:]]*:[[:space:]]*true' "$CASE_DIR/status.json"; then
        write_result false RunnerNotRegistered 2
    fi
    find "$EVIDENCE_ROOT" -maxdepth 2 -type d > "$CASE_DIR/evidence-index.txt"
    if [ ! -s "$CASE_DIR/evidence-index.txt" ]; then
        write_result false EvidenceIndexEmpty 2
    fi
    write_result true '' 0
}

# =============================================================================================
# CARD-0604 S4: the host lane. These run on server2's own shell against the HOST daemon and are
# the only cases allowed to change standing state. They never build or run a test, never touch a
# foreign project, and never prune the host daemon (D-9).
# =============================================================================================

SERVER2_COMPOSE="$CHECKOUT/docker-compose.server2-runner.yml"
SERVER2_TEMP_COMPOSE="$CHECKOUT/docker-compose.server2-runner.temp.yml"
SERVER2_ENV="$SERVER2_ROOT/secrets/stack.env"
SERVER2_TEMP_ENV="$SERVER2_ROOT/secrets/stack.temp.env"
TEMP_PROJECT="antiphon-runner-temp"
DEPLOY_KEY="$SERVER2_ROOT/secrets/deploy_key"
PHONE_HOME_SECRET="$SERVER2_ROOT/secrets/phone-home"
# CARD-0628 D-1: the Claude setup-token file. The desktop bridge streams it from the vault over SSH
# stdin before this script runs; nothing here ever reads its contents.
CLAUDE_OAUTH_TOKEN_PATH="$SERVER2_ROOT/secrets/claude_oauth_token"
# CARD-0631 D-9 (amended): the runner's git identity, a non-secret file beside the secrets it boots
# with. Created from stack.env's RUNNER_GIT_USER_NAME/EMAIL (or these defaults) only when missing.
GIT_IDENTITY_PATH="$SERVER2_ROOT/secrets/gitconfig"
GIT_IDENTITY_DEFAULT_NAME="antiphon-server2-runner"
GIT_IDENTITY_DEFAULT_EMAIL="antiphon-server2-runner@users.noreply.github.com"
GIT_IDENTITY_MOUNT="/run/antiphon/gitconfig"
# CARD-0660 (amended): the runner's Codex home, a DIRECTORY beside the secrets it boots with and
# bind-mounted read-write at CODEX_HOME. It holds the operator's ChatGPT sign-in, so it belongs to
# the runner uid at 0700 and nothing here ever reads, lists or copies what is inside it.
CODEX_HOME_PATH="$SERVER2_ROOT/secrets/codex"
CODEX_HOME_OWNER="1654:1654"
# CARD-0631 D-10: the anonymous origin RunnerWorkspaceService clones from.
RUNNER_CHECKOUT_ORIGIN="https://github.com/michal-ciechan/Antiphon.git"
RUNNER_CHECKOUT_DEFAULT="/work/repos/antiphon"

# The tag the project is DEPLOYED at, which is not this run's sha: a case that only restarts or
# inspects the standing runner must compose the image that is actually there. Deriving it from $SHA
# made `up -d --no-build` look for a tag that was never built, try to PULL it, and leave the runner
# stopped. deploy-parent is the one case that writes this file, and the only one that may change it.
deployed_sha12() {
    local from_env=""
    if [ -f "$SERVER2_ENV" ]; then
        from_env="$(sed -n 's/^SOURCE_SHA12=//p' "$SERVER2_ENV" | head -n 1 | tr -d '[:space:]')"
    fi
    if [ -n "$from_env" ]; then
        printf '%s' "$from_env"
    else
        printf '%s' "${SHA:0:12}"
    fi
}

compose_host() {
    local sha12
    sha12="$(deployed_sha12)"
    ANTIPHON_DEPLOY_KEY_FILE="$DEPLOY_KEY" \
    PHONE_HOME_SECRET_FILE="$PHONE_HOME_SECRET" \
    CLAUDE_OAUTH_TOKEN_FILE="$CLAUDE_OAUTH_TOKEN_PATH" \
    RUNNER_GIT_IDENTITY_FILE="$GIT_IDENTITY_PATH" \
    RUNNER_CODEX_HOME_DIR="$CODEX_HOME_PATH" \
    PHONE_HOME_SERVER_ORIGIN="${C604_SERVER_ORIGIN:?}" \
    SOURCE_SHA12="$sha12" \
    BUILD_SLOTS_SHA12="$(broker_sha12)" \
    SOURCE_REVISION="$SHA" \
    COMPOSE_PROJECT_NAME="$HOST_PROJECT" \
    docker compose -p "$HOST_PROJECT" -f "$SERVER2_COMPOSE" "$@"
}

broker_sha12() {
    local pinned
    pinned="$(stack_env_value BUILD_SLOTS_SHA12)"
    printf '%s' "${pinned:-${SHA:0:12}}"
}

compose_temp() {
    ANTIPHON_DEPLOY_KEY_FILE="$DEPLOY_KEY" \
    PHONE_HOME_SECRET_FILE="$PHONE_HOME_SECRET" \
    CLAUDE_OAUTH_TOKEN_FILE="$CLAUDE_OAUTH_TOKEN_PATH" \
    RUNNER_GIT_IDENTITY_FILE="$GIT_IDENTITY_PATH" \
    RUNNER_CODEX_HOME_DIR="$CODEX_HOME_PATH" \
    PHONE_HOME_SERVER_ORIGIN="${C604_SERVER_ORIGIN:?}" \
    SOURCE_SHA12="${SHA:0:12}" \
    SOURCE_REVISION="$SHA" \
    BUILD_SLOTS_SHA12="$(broker_sha12)" \
    RUNNER_GROK_STORE_DIR="${RUNNER_GROK_STORE_DIR:-}" \
    docker compose -p "$TEMP_PROJECT" -f "$SERVER2_COMPOSE" -f "$SERVER2_TEMP_COMPOSE" \
        --env-file "$SERVER2_TEMP_ENV" "$@"
}

ensure_build_slots_broker() {
    if ! docker network inspect antiphon-build-slots >/dev/null 2>&1; then
        docker network create antiphon-build-slots >> "$CASE_DIR/command.log" 2>&1 \
            || write_result false BuildSlotsNetworkFailed 2
    fi
    if [ -z "$(stack_env_value BUILD_SLOTS_SHA12)" ]; then
        printf 'BUILD_SLOTS_SHA12=%s\n' "${SHA:0:12}" >> "$SERVER2_ENV"
    fi
    compose_host --profile broker up -d --no-build build-slots >> "$CASE_DIR/command.log" 2>&1 \
        || write_result false BuildSlotsBrokerFailed 2
    local broker i
    broker="$(compose_host --profile broker ps -q build-slots)"
    if [ -z "$broker" ]; then write_result false BuildSlotsBrokerFailed 2; fi
    for i in $(seq 1 30); do
        if docker inspect -f '{{.State.Health.Status}}' "$broker" 2>/dev/null | grep -qx healthy; then return; fi
        sleep 5
    done
    write_result false BuildSlotsBrokerUnhealthy 2
}

runner_container() {
    docker ps --filter "name=${HOST_PROJECT}-session-runner" --format '{{.Names}}' | head -n 1
}

# D-9: retire the CARD-0590 leftovers AFTER writing the inventory, and touch nothing else. The
# host daemon is shared with am-service, traefik, windmill and schoolrevision-*; `prune` on it is
# forbidden, here and everywhere.
retire_c590_leftovers() {
    docker ps -a --format '{{.Names}}\t{{.Image}}\t{{.Status}}' > "$CASE_DIR/inventory-containers.txt"
    docker volume ls --format '{{.Name}}' > "$CASE_DIR/inventory-volumes.txt"
    docker images --format '{{.Repository}}:{{.Tag}}\t{{.ID}}\t{{.Size}}' > "$CASE_DIR/inventory-images.txt"

    local project
    for project in $(docker ps -a --format '{{.Label "com.docker.compose.project"}}' | sort -u); do
        case "$project" in
            c590[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f])
                printf 'retiring project %s\n' "$project" >> "$CASE_DIR/retired.txt"
                docker compose -p "$project" down -v --remove-orphans >> "$CASE_DIR/command.log" 2>&1 || true
                ;;
        esac
    done
    local image
    for image in $(docker images --format '{{.Repository}}:{{.Tag}}' | grep -E '^antiphon-c590-' || true); do
        printf 'retiring image %s\n' "$image" >> "$CASE_DIR/retired.txt"
        docker image rm "$image" >> "$CASE_DIR/command.log" 2>&1 || true
    done
    # Volumes a c590 run created OUTSIDE its compose project (the node_modules and bin/obj caches
    # the old sibling test lane made with `docker volume create`) are not removed by `down -v`.
    # The pattern is anchored on the run-scoped project prefix, so it can never reach a foreign
    # volume: am-service, gym-stat, schoolrevision and the rest share this daemon.
    local volume
    for volume in $(docker volume ls -q | grep -E '^c590[0-9a-f]{12}_' || true); do
        printf 'retiring volume %s\n' "$volume" >> "$CASE_DIR/retired.txt"
        docker volume rm "$volume" >> "$CASE_DIR/command.log" 2>&1 || true
    done
    touch "$CASE_DIR/retired.txt"
}

# D-5: deploy-parent builds antiphon-server2/{server,session-testing}:<sha12> on every run and
# never removed the pair it superseded, so each round left ~2.2 GB behind on a shared host daemon
# (the CARD-0604 rounds alone accumulated several). Retire the superseded tags once the new
# deployment is proven, keeping the sha12 that is now running. The pattern is anchored on this
# project's own repository names, so it can never reach am-service, windmill, gym-stat or any
# other neighbour, and `prune` stays forbidden here as everywhere.
retire_superseded_server2_images() {
    local keep="$1" image temp_keep broker_keep live_temp_image live_broker_image
    temp_keep="$(sed -n 's/^SOURCE_SHA12=//p' "$SERVER2_TEMP_ENV" 2>/dev/null | head -n 1)"
    broker_keep="$(broker_sha12)"
    live_temp_image="$(docker ps --filter "label=com.docker.compose.project=$TEMP_PROJECT" --filter 'label=com.docker.compose.service=session-runner' --format '{{.Image}}' | head -n 1)"
    live_broker_image="$(docker ps --filter "label=com.docker.compose.project=$HOST_PROJECT" --filter 'label=com.docker.compose.service=build-slots' --format '{{.Image}}' | head -n 1)"
    for image in $(docker images --format '{{.Repository}}:{{.Tag}}' \
        | grep -E '^antiphon-server2/(server|session-testing):' || true); do
        if [ "$image" = "antiphon-server2/server:$keep" ] \
            || [ "$image" = "antiphon-server2/session-testing:$keep" ] \
            || [ "$image" = "antiphon-server2/server:$temp_keep" ] \
            || [ "$image" = "antiphon-server2/session-testing:$temp_keep" ] \
            || [ "$image" = "antiphon-server2/session-testing:$broker_keep" ] \
            || [ "$image" = "$live_temp_image" ] \
            || [ "$image" = "$live_broker_image" ]; then
            continue
        fi
        printf 'retiring image %s\n' "$image" >> "$CASE_DIR/retired.txt"
        docker image rm "$image" >> "$CASE_DIR/command.log" 2>&1 || true
    done
}

# One KEY=value line of the deployed stack.env, read BEFORE deploy-parent rewrites the file.
stack_env_value() {
    if [ -f "$SERVER2_ENV" ]; then
        sed -n "s/^$1=//p" "$SERVER2_ENV" | head -n 1
    fi
}

# CARD-0631 D-9 (amended). The runner's git identity is a FILE on server2, mounted read-only at
# $GIT_IDENTITY_MOUNT and named by GIT_CONFIG_GLOBAL; nothing is baked into the image. It is
# created only when missing, from stack.env's RUNNER_GIT_USER_NAME/EMAIL or the defaults. An
# existing file is the operator's and is never rewritten. It must exist before compose, or the
# bind mount would create a directory in its place; an EMPTY directory such a mount left behind is
# removed, anything else refuses. Non-secret, so its values are evidence. A symlink at the path
# refuses before anything touches it: a write or chmod through it would land on its target, which
# may be an adjacent 0600 secret. Only a file created here is made world-readable; an operator's
# existing file keeps the mode they gave it.
ensure_runner_git_identity() {
    local name email
    if [ -L "$GIT_IDENTITY_PATH" ]; then
        write_result false GitIdentityPathIsSymlink 2
    fi
    if [ -d "$GIT_IDENTITY_PATH" ]; then
        rmdir "$GIT_IDENTITY_PATH" 2>> "$CASE_DIR/command.log" || write_result false GitIdentityPathIsDirectory 2
    fi
    name="$(stack_env_value RUNNER_GIT_USER_NAME)"
    email="$(stack_env_value RUNNER_GIT_USER_EMAIL)"
    RUNNER_GIT_USER_NAME="${name:-$GIT_IDENTITY_DEFAULT_NAME}"
    RUNNER_GIT_USER_EMAIL="${email:-$GIT_IDENTITY_DEFAULT_EMAIL}"
    if [ ! -e "$GIT_IDENTITY_PATH" ]; then
        rm -f "$GIT_IDENTITY_PATH.tmp"
        # uid 1654 reads the bind mount itself, so the NEW file is world-readable (it holds no
        # secret). The mode goes on the regular temporary file this branch just wrote, never on
        # the destination path.
        { git config --file "$GIT_IDENTITY_PATH.tmp" user.name "$RUNNER_GIT_USER_NAME" \
            && git config --file "$GIT_IDENTITY_PATH.tmp" user.email "$RUNNER_GIT_USER_EMAIL" \
            && chmod 0644 "$GIT_IDENTITY_PATH.tmp" \
            && mv -n "$GIT_IDENTITY_PATH.tmp" "$GIT_IDENTITY_PATH"; } 2>> "$CASE_DIR/command.log" \
            || write_result false GitIdentityCreateFailed 2
        rm -f "$GIT_IDENTITY_PATH.tmp"
        printf 'created\n' > "$CASE_DIR/git-identity-state.txt"
    else
        printf 'kept\n' > "$CASE_DIR/git-identity-state.txt"
    fi
    if [ -L "$GIT_IDENTITY_PATH" ]; then
        write_result false GitIdentityPathIsSymlink 2
    fi
    git config --file "$GIT_IDENTITY_PATH" --get user.name > "$CASE_DIR/git-identity.txt" 2>> "$CASE_DIR/command.log" \
        || write_result false GitIdentityIncomplete 2
    git config --file "$GIT_IDENTITY_PATH" --get user.email >> "$CASE_DIR/git-identity.txt" 2>> "$CASE_DIR/command.log" \
        || write_result false GitIdentityIncomplete 2
}

# CARD-0660 (amended). The runner's Codex home is a DIRECTORY on server2, bind-mounted read-write
# at CODEX_HOME in the runner and at /codex-home in state-init. A directory and never a single
# file: Codex rewrites its credential file by rename on every token refresh, which a file bind
# mount cannot survive. It must exist before compose, or Docker would create it root-owned. The
# host user mc cannot chown to the runner uid, so this is host-lane sudo, like ensure_dirs. A
# symlink refuses before anything touches the path (a chown or chmod through it would land on its
# target), and so does anything that is not a directory. Owner and mode are (re)asserted on the
# directory itself only; its contents are the operator's sign-in and are never read, listed,
# copied or re-moded. Only presence is evidence.
ensure_runner_codex_home() {
    if [ "$LANE" != "host" ]; then
        write_result false CodexHomeHostLaneOnly 2
    fi
    if [ -L "$CODEX_HOME_PATH" ]; then
        write_result false CodexHomePathIsSymlink 2
    fi
    if [ -e "$CODEX_HOME_PATH" ] && [ ! -d "$CODEX_HOME_PATH" ]; then
        write_result false CodexHomePathIsNotDirectory 2
    fi
    if [ ! -e "$CODEX_HOME_PATH" ]; then
        sudo -n install -d -o "${CODEX_HOME_OWNER%:*}" -g "${CODEX_HOME_OWNER#*:}" -m 0700 "$CODEX_HOME_PATH" 2>> "$CASE_DIR/command.log" || write_result false CodexHomeCreateFailed 2
        printf 'created\n' > "$CASE_DIR/codex-home-state.txt"
    else
        printf 'kept\n' > "$CASE_DIR/codex-home-state.txt"
    fi
    # Again after the create: chmod follows a link, so nothing may have swapped the path meanwhile.
    if [ -L "$CODEX_HOME_PATH" ]; then
        write_result false CodexHomePathIsSymlink 2
    fi
    if [ ! -d "$CODEX_HOME_PATH" ]; then
        write_result false CodexHomePathIsNotDirectory 2
    fi
    { sudo -n chown -h "$CODEX_HOME_OWNER" "$CODEX_HOME_PATH" \
        && sudo -n chmod 0700 "$CODEX_HOME_PATH"; } 2>> "$CASE_DIR/command.log" \
        || write_result false CodexHomeOwnershipFailed 2
    printf 'true\n' > "$CASE_DIR/codex-home-present.txt"
    if sudo -n test -e "$CODEX_HOME_PATH/auth.json"; then
        printf 'true\n' > "$CASE_DIR/codex-auth-present.txt"
    else
        printf 'false\n' > "$CASE_DIR/codex-auth-present.txt"
        printf 'WARN CodexAuthAbsent: the runner reports Codex signed out until the operator signs in or migrates the sign-in (docs/docker-stack.md)\n' \
            | tee -a "$CASE_DIR/command.log" >&2
    fi
}

# CARD-0631 D-9 (amended). The mounted file must be the identity uid 1654 actually commits with:
# its origin must be the mount, so no system file or leftover stopgap is what git resolved.
verify_runner_git_identity() {
    local container="$1" where="$2" expected
    expected="$(printf 'file:%s\t%s' "$GIT_IDENTITY_MOUNT" "$(sed -n 2p "$CASE_DIR/git-identity.txt")")"
    docker exec -u 1654:1654 "$container" git -C "$where" config --show-origin --get user.email \
        > "$CASE_DIR/runner-git-identity.txt" 2>> "$CASE_DIR/command.log" \
        || write_result false GitIdentityNotEffective 2
    if [ "$(cat "$CASE_DIR/runner-git-identity.txt")" != "$expected" ]; then
        write_result false GitIdentityNotEffective 2
    fi
}

# CARD-0631 D-10 (amended by Review 012e6357). A first deploy on an empty work volume cannot wait
# for the runner's lazy clone: phone-home may still be disabled on the server at this gate, so no
# mirror would ever arrive and every fresh deploy refused Missing. deploy-parent therefore seeds
# the checkout BEFORE it starts the runner, so no lazy mirror can be in flight: state-init owns the
# fresh volume for uid 1654, then a one-off container of the runner image clones anonymously as
# uid 1654 with RunnerWorkspaceService's exact command, at the repository the runner is configured
# with. Only an absent or empty destination is cloned into (git itself refuses a non-empty one);
# anything else is left untouched for verify_runner_checkout to judge by name.
seed_runner_checkout() {
    local project="${1:-$HOST_PROJECT}"
    local compose=compose_host
    if [ "$project" = "$TEMP_PROJECT" ]; then compose=compose_temp; fi
    "$compose" run --rm --no-deps -T state-init >> "$CASE_DIR/command.log" 2>&1 \
        || write_result false StateInitFailed 2
    "$compose" run --rm --no-deps -T --user 1654:1654 -e GIT_TERMINAL_PROMPT=0 \
        --entrypoint /bin/sh session-runner -c '
            repo="${PhoneHome__RunnerRepository:-$1}"
            if [ -e "$repo/.git" ]; then echo "present path=$repo"; exit 0; fi
            if [ -d "$repo" ] && [ -n "$(ls -A "$repo")" ]; then echo "occupied path=$repo"; exit 0; fi
            mkdir -p "$(dirname "$repo")" || exit 1
            timeout --kill-after=5s 300s git clone --filter=blob:none --no-checkout "$2" "$repo" 1>&2 || exit 1
            echo "seeded path=$repo"' \
        antiphon-seed "$RUNNER_CHECKOUT_DEFAULT" "$RUNNER_CHECKOUT_ORIGIN" \
        > "$CASE_DIR/runner-checkout-seed.txt" 2>> "$CASE_DIR/command.log" \
        || write_result false RunnerCheckoutSeedFailed 2
}

# CARD-0631 D-10. Everything runs INSIDE the runner as uid 1654 against the repository the runner
# is configured with -- never the host's identically named checkout and never a child volume. The
# seed above never replaces what is there, so a foreign, broken or occupied checkout still refuses
# here by name.
verify_runner_checkout() {
    local container="$1" repo top origin fetched
    repo="$(docker exec "$container" sh -c 'printf "%s" "${PhoneHome__RunnerRepository:-}"' 2>> "$CASE_DIR/command.log")" \
        || write_result false RunnerCheckoutInvalid 2
    repo="${repo:-$RUNNER_CHECKOUT_DEFAULT}"
    printf '%s\n' "$repo" > "$CASE_DIR/runner-checkout-path.txt"
    if ! docker exec -u 1654:1654 "$container" test -e "$repo/.git"; then
        write_result false RunnerCheckoutMissing 2
    fi
    top="$(docker exec -u 1654:1654 "$container" git -C "$repo" rev-parse --show-toplevel 2>> "$CASE_DIR/command.log")" \
        || write_result false RunnerCheckoutInvalid 2
    if [ "$top" != "$repo" ]; then
        write_result false RunnerCheckoutInvalid 2
    fi
    origin="$(docker exec -u 1654:1654 "$container" git -C "$repo" remote get-url origin 2>> "$CASE_DIR/command.log")" \
        || write_result false RunnerCheckoutOriginMismatch 2
    if [ "$origin" != "$RUNNER_CHECKOUT_ORIGIN" ]; then
        write_result false RunnerCheckoutOriginMismatch 2
    fi
    docker exec -u 1654:1654 -e GIT_TERMINAL_PROMPT=0 "$container" \
        timeout --kill-after=5s 120s git -C "$repo" fetch --no-tags origin "$BRANCH" \
        >> "$CASE_DIR/command.log" 2>&1 \
        || write_result false RunnerCheckoutFetchFailed 2
    fetched="$(docker exec -u 1654:1654 "$container" git -C "$repo" rev-parse FETCH_HEAD 2>> "$CASE_DIR/command.log")" \
        || write_result false RunnerCheckoutFetchFailed 2

    # A repository-local identity outranks the mounted file for this checkout and every task
    # worktree of it: that is where a stopgap identity would win. Remove it, record that it was
    # there, and prove the mount is what git now resolves inside the checkout.
    if docker exec -u 1654:1654 "$container" git -C "$repo" config --local --get-regexp '^user\.(name|email)$' \
        > "$CASE_DIR/runner-checkout-local-identity.txt" 2>/dev/null; then
        docker exec -u 1654:1654 "$container" sh -c \
            'for k in user.name user.email; do if git -C "$1" config --local --get "$k" >/dev/null; then git -C "$1" config --local --unset-all "$k" || exit 1; fi; done' \
            antiphon-identity "$repo" 2>> "$CASE_DIR/command.log" \
            || write_result false GitIdentityOverrideNotRemoved 2
        printf 'removed\n' > "$CASE_DIR/runner-checkout-local-identity-state.txt"
    fi
    verify_runner_git_identity "$container" "$repo"

    printf 'path=%s\norigin=%s\nbranch=%s\nfetch_head=%s\n' "$repo" "$origin" "$BRANCH" "$fetched" \
        > "$CASE_DIR/runner-checkout.txt"
}

ensure_runner_boot_files() {
    # --- D-8: both secrets are generated on server2 and never leave it. ---
    umask 077
    mkdir -p "$SERVER2_ROOT/secrets"
    if [ ! -s "$DEPLOY_KEY" ]; then
        ssh-keygen -t ed25519 -N '' -C antiphon-server2-runner -f "$DEPLOY_KEY" >> "$CASE_DIR/command.log" 2>&1 \
            || write_result false DeployKeyGenerationFailed 2
    fi
    chmod 0600 "$DEPLOY_KEY"
    if [ ! -s "$PHONE_HOME_SECRET" ]; then
        openssl rand -hex 32 > "$PHONE_HOME_SECRET" || write_result false PhoneHomeSecretGenerationFailed 2
    fi
    chmod 0600 "$PHONE_HOME_SECRET"
    # The PUBLIC half only. The private half is never copied, printed or hashed.
    cp "$DEPLOY_KEY.pub" "$CASE_DIR/deploy_key.pub"
    stat -c '%a' "$PHONE_HOME_SECRET" > "$CASE_DIR/phone-home-mode.txt"
    printf 'true\n' > "$CASE_DIR/deploy-key-present.txt"
    printf 'true\n' > "$CASE_DIR/phone-home-secret-present.txt"
    # CARD-0628 D-1 / CARD-0737: the token file must EXIST before compose, or the bind mount
    # would create a directory in its place. The desktop deploy leaves an existing file alone
    # unless -RefreshClaudeToken was asked. A missing or empty file is a warning, not a failed
    # deploy: the runner reports claudeAuth=logged-out. Only its presence is recorded.
    if [ -d "$CLAUDE_OAUTH_TOKEN_PATH" ]; then
        write_result false ClaudeOAuthTokenPathIsDirectory 2
    fi
    if [ ! -e "$CLAUDE_OAUTH_TOKEN_PATH" ]; then
        : > "$CLAUDE_OAUTH_TOKEN_PATH"
    fi
    chmod 0600 "$CLAUDE_OAUTH_TOKEN_PATH"
    if [ -s "$CLAUDE_OAUTH_TOKEN_PATH" ]; then
        printf 'true\n' > "$CASE_DIR/claude-oauth-token-present.txt"
    else
        printf 'false\n' > "$CASE_DIR/claude-oauth-token-present.txt"
        printf 'WARN ClaudeOAuthTokenAbsent: the remote token file is missing or empty; the runner will report claudeAuth=logged-out. Pass -RefreshClaudeToken to refresh it from the vault.\n' \
            | tee -a "$CASE_DIR/command.log" >&2
    fi
    # CARD-0631: before compose binds it, and before stack.env is rewritten below.
    ensure_runner_git_identity
    # CARD-0660: before state-init runs (checkout seed, below) or the runner binds it.
    ensure_runner_codex_home
}

build_server2_images() {
    docker build -f "$CHECKOUT/docker/session-runner-grok/Dockerfile" --target session-testing \
        --build-arg "SOURCE_REVISION=$SHA" \
        -t "antiphon-server2/session-testing:${SHA:0:12}" "$CHECKOUT" >> "$CASE_DIR/build.log" 2>&1 \
        || write_result false RunnerBuildFailed 2
    docker build -f "$CHECKOUT/Dockerfile" --build-arg "SOURCE_REVISION=$SHA" \
        -t "antiphon-server2/server:${SHA:0:12}" "$CHECKOUT" >> "$CASE_DIR/build.log" 2>&1 \
        || write_result false StateInitBuildFailed 2
}

# CARD-0849: these names are deliberately fixed. The project names never prefix them.
C849_PACKAGES=antiphon-runner-cache-nuget-packages
C849_SCRATCH=antiphon-runner-cache-nuget-scratch
C849_NPM=antiphon-runner-cache-npm-content
C849_READY="$SERVER2_ROOT/cache/seed-accepted"
c849_lock() {
    require_lane host
    [ "${C849_LOCK_HELD:-0}" = 1 ] && return 0
    sudo -n install -d -o mc -g mc -m 0700 "$SERVER2_ROOT/locks"
    exec 9>"$SERVER2_ROOT/locks/cache-maintenance.lock"
    flock -w 60 9 || write_result false CacheMaintenanceBusy 2
    C849_LOCK_HELD=1
}

c849_evidence_dir() {
    require_lane host
    sudo -n install -d -o mc -g mc -m 0700 "$CASE_DIR"
}

c849_image() {
    local image="antiphon-server2/session-testing:${SHA:0:12}"
    if ! docker image inspect "$image" >/dev/null 2>&1; then
        case "$CASE" in
            runner-cache-seed|runner-cache-inventory|runner-cache-fixture|runner-cache-prune-preview|runner-cache-reset)
                local donor marker_image
                donor="$(c849_optional_donor)"
                if [ -n "$donor" ]; then
                    image="$(docker inspect -f '{{.Image}}' "$donor")" \
                        || write_result false CacheHelperImageMissing 2
                else
                    marker_image="$(sed -n 's/^image=//p' "$C849_READY" 2>/dev/null | head -n 1)"
                    if [[ ! "$marker_image" =~ ^sha256:[0-9a-f]{64}$ ]] \
                        || ! docker image inspect "$marker_image" >/dev/null 2>&1; then
                        local main
                        main="$(compose_host ps -q session-runner)"
                        [ -n "$main" ] || write_result false CacheHelperImageMissing 2
                        [ "$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "$main")" = "$HOST_PROJECT" ] \
                            || write_result false CacheHelperImageMissing 2
                        marker_image="$(docker inspect -f '{{.Image}}' "$main")"
                        [[ "$marker_image" =~ ^sha256:[0-9a-f]{64}$ ]] \
                            && docker image inspect "$marker_image" >/dev/null 2>&1 \
                            || write_result false CacheHelperImageMissing 2
                    fi
                    image="$marker_image"
                fi ;;
            *) write_result false CacheHelperImageMissing 2 ;;
        esac
    fi
    printf '%s' "$image"
}

c849_volume() {
    local name="$1" role="$2" create="$3" image="$4" fresh=0
    case "$name:$role" in
        "$C849_PACKAGES:nuget-packages"|"$C849_SCRATCH:nuget-scratch"|"$C849_NPM:npm-content") ;;
        *) write_result false CacheTargetInvalid 2 ;;
    esac
    if ! docker volume inspect "$name" >/dev/null 2>&1; then
        if [ "$create" != yes ]; then write_result false CacheVolumeMissing 2; fi
        docker volume create --driver local \
            --label io.antiphon.owner=server2-runner \
            --label io.antiphon.cache-schema=1 \
            --label "io.antiphon.cache-role=$role" "$name" >/dev/null \
            || write_result false CacheVolumeCreateFailed 2
        fresh=1
    fi
    local driver options owner schema actual_role
    driver="$(docker volume inspect -f '{{.Driver}}' "$name")"
    options="$(docker volume inspect -f '{{json .Options}}' "$name")"
    owner="$(docker volume inspect -f '{{index .Labels "io.antiphon.owner"}}' "$name")"
    schema="$(docker volume inspect -f '{{index .Labels "io.antiphon.cache-schema"}}' "$name")"
    actual_role="$(docker volume inspect -f '{{index .Labels "io.antiphon.cache-role"}}' "$name")"
    if [ "$driver" != local ] || [[ "$options" != null && "$options" != '{}' ]] \
        || [ "$owner" != server2-runner ] || [ "$schema" != 1 ] || [ "$actual_role" != "$role" ]; then
        write_result false CacheVolumeForeign 2
    fi
    if [ "$fresh" = 1 ]; then
        docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
            --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
            -c 'chown 1654:1654 /cache && chmod 0700 /cache' >/dev/null \
            || write_result false CacheVolumeInitFailed 2
    fi
    local stat_line
    stat_line="$(docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
        --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
        -c 'test ! -L /cache && test -d /cache && stat -c %u:%g:%a /cache' 2>/dev/null)" \
        || write_result false CacheRootInvalid 2
    if [ "$stat_line" != 1654:1654:700 ]; then write_result false CacheRootOwnershipInvalid 2; fi
    if [ "$fresh" = 0 ] && [ "$create" = yes ] && [ ! -s "$C849_READY" ]; then
        [ -z "$(docker ps -q --filter "volume=$name")" ] \
            || write_result false CacheUnmarkedInUse 2
        c849_empty_volume "$name" "$image" || write_result false CacheUnmarkedContent 2
    fi
    docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
        --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
        -c 'set -eu; p="/cache/.c849-probe-$1"; (umask 077; : > "$p"); mv "$p" "$p.moved"; rm "$p.moved"' sh "$RUN" >/dev/null \
        || write_result false CacheRootNotWritable 2
    printf '%s %s %s\n' "$name" "$role" "$stat_line" >> "$CASE_DIR/cache-roots.txt"
}

c849_prepare() {
    local create="$1" image
    image="$(c849_image)"
    c849_lock
    c849_volume "$C849_PACKAGES" nuget-packages "$create" "$image"
    c849_volume "$C849_SCRATCH" nuget-scratch "$create" "$image"
    c849_volume "$C849_NPM" npm-content "$create" "$image"
}

c849_assert_mounts() {
    local container="$1" mounted expected destination project
    project="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "$container")"
    case "$project" in "$HOST_PROJECT"|"$TEMP_PROJECT") ;; *) write_result false CacheRunnerIdentityInvalid 2 ;; esac
    for expected in "$C849_PACKAGES:/home/app/.nuget/packages" \
        "$C849_SCRATCH:/var/cache/antiphon/nuget-scratch" \
        "$C849_NPM:/home/app/.npm/_cacache"; do
        mounted="${expected%%:*}"; destination="${expected#*:}"
        if ! docker inspect -f '{{range .Mounts}}{{println .Type .Name .Destination .RW}}{{end}}' "$container" \
            | grep -Fxq "volume $mounted $destination true"; then
            write_result false CacheMountMismatch 2
        fi
    done
    for expected in "$project"'_runner-tmp:/tmp' "$project"'_work:/work' \
        "$project"'_runner-state:/state' "$project"'_dind-data:/var/lib/docker'; do
        mounted="${expected%%:*}"; destination="${expected#*:}"
        if ! docker inspect -f '{{range .Mounts}}{{println .Type .Name .Destination .RW}}{{end}}' "$container" \
            | grep -Fxq "volume $mounted $destination true"; then
            write_result false RunnerPrivateMountMissing 2
        fi
    done
    [ "$(docker exec "$container" stat -c %a /tmp)" = 1777 ] \
        || write_result false RunnerTmpModeInvalid 2
    docker inspect -f '{{range .Mounts}}{{if eq .Type "volume"}}{{println .Type .Name .Destination .RW}}{{end}}{{end}}' \
        "$container" > "$CASE_DIR/runner-mounts.txt"
}

c849_smoke() {
    local container="$1" runner_id="$2"
    docker exec -u 1654:1654 -e HOME=/home/app -i "$container" /bin/sh -s > "$CASE_DIR/smoke.txt" <<'C849_SMOKE_SCRIPT' \
        || write_result false CacheSmokeFailed 2
set -eu
test "$(id -u)" = 1654
test "$NUGET_PACKAGES" = /home/app/.nuget/packages
test "$NUGET_SCRATCH" = /var/cache/antiphon/nuget-scratch
test "$NPM_CONFIG_CACHE" = /home/app/.npm
test -s /home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata
test -s /home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost
test ! -d /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/9.0.20
curl -fsS http://build-slots:8080/build-slots >/dev/null
root="$(mktemp -d /tmp/c849-smoke-XXXXXXXX)"
trap 'rm -rf "$root"' EXIT
mkdir -p "$root/empty"
cat > "$root/Smoke.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>true</UseAppHost><RuntimeIdentifier>linux-x64</RuntimeIdentifier><RuntimeFrameworkVersion>9.0.20</RuntimeFrameworkVersion><TargetLatestRuntimePatch>false</TargetLatestRuntimePatch><SelfContained>false</SelfContained><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
EOF
printf 'System.Console.WriteLine("CARD0849_APPHOST_OK");\n' > "$root/Program.cs"
printf '<configuration><packageSources><clear/><add key="empty" value="%s"/></packageSources></configuration>\n' "$root/empty" > "$root/NuGet.Config"
cat > "$root/driver.sh" <<'EOF'
#!/bin/sh
set -eu
cd "$1"
dotnet restore Smoke.csproj --configfile NuGet.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 >/dev/null
dotnet build Smoke.csproj --no-restore -p:UseAppHost=true -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 >/dev/null
test "$(./bin/Debug/net9.0/linux-x64/Smoke)" = CARD0849_APPHOST_OK
EOF
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
pwsh -NoProfile -File /work/repos/antiphon/scripts/build-slot.ps1 -Label c849-smoke -- /bin/sh "$root/driver.sh" "$root" >/dev/null
printf 'C849_SMOKE uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n'
C849_SMOKE_SCRIPT
    if ! grep -q '^C849_SMOKE uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK$' "$CASE_DIR/smoke.txt"; then
        write_result false CacheSmokeReceiptMissing 2
    fi
    printf 'C849_SMOKE runner=%s uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK\n' "$runner_id" >> "$CASE_DIR/smoke-summary.txt"
}

c849_status_body() {
    local runner="$1" response code
    response="$(curl -sS --max-time 15 -w '\n%{http_code}' \
        "${C604_SERVER_ORIGIN:?}/api/session-runners/$runner/status")" || return 1
    code="${response##*$'\n'}"
    [ "$code" = 200 ] || return 1
    printf '%s' "${response%$'\n'*}"
}

c849_status_zero() {
    local runner="$1" phase="${2:-pre}" body
    command -v jq >/dev/null || return 1
    body="$(c849_status_body "$runner")" || return 1
    printf '%s' "$body" | jq -e '
      (.sessions | type) == "number" and (.runnerSessions | type) == "number" and
      (.queuedTasks | type) == "number" and
      .sessions == 0 and .runnerSessions == 0 and .queuedTasks == 0 and
      .draining == true and .acceptingNewWork == false and .redirectTo == "server2" and
      .retireWhenIdle == true' >/dev/null || return 1
    if [ "$phase" = reconnected ]; then
        printf '%s' "$body" | jq -e '.dispatchEligible == true' >/dev/null || return 1
    fi
    return 0
}

c849_donor() {
    local -a ids
    mapfile -t ids < <(docker ps -aq \
        --filter "label=com.docker.compose.project=$TEMP_PROJECT" \
        --filter 'label=com.docker.compose.service=session-runner')
    if [ "${#ids[@]}" -ne 1 ]; then write_result false CacheDonorIdentityInvalid 2; fi
    local service project image
    service="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.service"}}' "${ids[0]}")"
    project="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "${ids[0]}")"
    image="$(docker inspect -f '{{.Image}}' "${ids[0]}")"
    if [ "$service" != session-runner ] || [ "$project" != "$TEMP_PROJECT" ] \
        || [[ ! "$image" =~ ^sha256:[0-9a-f]{64}$ ]]; then
        write_result false CacheDonorIdentityInvalid 2
    fi
    docker inspect -f '{{.Id}}' "${ids[0]}"
}

c849_optional_donor() {
    local -a ids
    mapfile -t ids < <(docker ps -aq \
        --filter "label=com.docker.compose.project=$TEMP_PROJECT" \
        --filter 'label=com.docker.compose.service=session-runner')
    [ "${#ids[@]}" -le 1 ] || write_result false CacheDonorIdentityInvalid 2
    if [ "${#ids[@]}" -eq 1 ]; then c849_donor; fi
    return 0
}

c849_empty_volume() {
    local name="$1" image="$2"
    [ -z "$(docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
        --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
        -c 'find /cache -mindepth 1 -print -quit' 2>/dev/null)" ]
}

c849_seed_failure() {
    local donor="$1" diagnosis="$2"
    if [ -n "${stage:-}" ] && [[ "$stage" == "$SERVER2_ROOT"/cache/stage-"$RUN"-* ]] \
        && [ -d "$stage" ] && [ ! -L "$stage" ]; then
        rm -rf -- "$stage"
    fi
    if [ -n "$donor" ] && [ "$(docker inspect -f '{{.State.Running}}' "$donor" 2>/dev/null || true)" = false ]; then
        docker start "$donor" >/dev/null 2>&1 || true
    fi
    write_result false "$diagnosis" 2
}

c849_validate_seed_relative() {
    local path="$1"
    case "$path" in
        ''|/*|.|..|./*|../*|*/../*|*/..|*/./*|*/.)
            printf 'CacheDonorUnsafePath\n'; return 2 ;;
    esac
    if [[ "$path" == *$'\n'* || "$path" == *$'\r'* ]]; then
        printf 'CacheDonorUnsafePath\n'; return 2
    fi
}

c849_validate_seed_tree() {
    local stage="$1" package version entry relative
    if [ -n "$(find "$stage" \( -type l -o -type b -o -type c -o -type p -o -type s \) -print -quit)" ] \
        || [ -n "$(find "$stage" -type f -links +1 -print -quit)" ]; then
        printf 'CacheDonorUnsafeEntry\n'; return 2
    fi
    while IFS= read -r -d '' entry; do
        relative="${entry#"$stage"/}"
        c849_validate_seed_relative "$relative" || return 2
    done < <(find "$stage" -mindepth 1 -print0)
    for package in "$stage/packages"/*; do
        [ -d "$package" ] || { printf 'CacheDonorPackagesEmpty\n'; return 2; }
        for version in "$package"/*; do
            [ -d "$version" ] || { printf 'CacheDonorVersionInvalid\n'; return 2; }
            if [ ! -s "$version/.nupkg.metadata" ]; then
                case "$version" in
                    "$stage/packages/microsoft.netcore.app.host.linux-x64/9.0.20")
                        printf 'AppHostDonorMetadataMissing\n'; return 2 ;;
                    "$stage/packages/microsoft.netcore.app.ref/9.0.20")
                        printf 'Net9ReferenceDonorMissing\n'; return 2 ;;
                esac
                rm -rf -- "$version" || { printf 'CacheDonorVersionIncomplete\n'; return 2; }
            fi
        done
    done
    [ -s "$stage/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" ] \
        || { printf 'AppHostDonorMissing\n'; return 2; }
    [ -s "$stage/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ] \
        || { printf 'AppHostDonorMetadataMissing\n'; return 2; }
    [ -s "$stage/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" ] \
        || { printf 'Net9ReferenceDonorMissing\n'; return 2; }
}

c849_seed() {
    require_lane host
    c849_prepare yes
    local image donor donor_image stage recovery helper payload_hash reference_hash package_bytes npm_bytes now i
    image="$(c849_image)"
    donor="$(c849_optional_donor)"
    if [ -f "$C849_READY" ]; then
        c849_require_ready
        if [ -n "$donor" ]; then
            c849_status_zero server2-temp reconnected || write_result false CacheDonorNotReady 2
            c849_status_body server2-temp | jq -c '{sessions,runnerSessions,queuedTasks,draining,retireWhenIdle,redirectTo,dispatchEligible,acceptingNewWork}' \
                > "$CASE_DIR/status.json" || write_result false CacheDonorReconnectReceiptMissing 2
        fi
        printf 'ready=true donor=%s\n' "$donor" > "$CASE_DIR/seed.txt"
        write_result true '' 0
    fi
    [ -n "$donor" ] || write_result false CacheSeedRequired 2
    donor_image="$(docker inspect -f '{{.Image}}' "$donor")"
    c849_status_zero server2-temp || c849_seed_failure "$donor" CacheDonorNotIdleDrained
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        c849_empty_volume "$name" "$image" || write_result false CacheUnmarkedContent 2
    done
    # A tracked session count alone cannot see a separate cache writer. Refuse a donor with
    # any app-uid restore/build/npm process after its drain has reached zero.
    if docker exec "$donor" /bin/sh -c "ps -eo uid,comm | awk '\$1==1654 && \$2 ~ /^(dotnet|nuget|npm|node)$/ {found=1} END {exit !found}'"; then
        write_result false CacheDonorConsumerBusy 2
    fi
    sudo -n install -d -o mc -g mc -m 0700 "$SERVER2_ROOT/cache"
    stage="$(mktemp -d "$SERVER2_ROOT/cache/stage-$RUN-XXXXXXXX")"
    mkdir -m 0700 "$stage/packages" "$stage/npm"
    c849_status_zero server2-temp || write_result false CacheDonorNotIdleDrained 2
    docker stop "$donor" >/dev/null || c849_seed_failure "$donor" CacheDonorStopFailed
    docker cp "$donor:/home/app/.nuget/packages/." "$stage/packages" >/dev/null 2>&1 \
        || c849_seed_failure "$donor" CacheDonorPackageCopyFailed
    docker cp "$donor:/home/app/.npm/_cacache/." "$stage/npm" >/dev/null 2>&1 \
        || c849_seed_failure "$donor" CacheDonorNpmCopyFailed
    # Validate the stopped donor's staged copy before the first import. The same
    # function is exercised by the isolated fixture's malformed-tree controls.
    local tree_diagnosis
    tree_diagnosis="$(c849_validate_seed_tree "$stage")" \
        || c849_seed_failure "$donor" "$tree_diagnosis"
    docker run --rm --network none --user 0:0 --entrypoint npm \
        --mount "type=bind,source=$stage/npm,target=/npm/_cacache" "$image" \
        cache verify --cache /npm >/dev/null 2>&1 \
        || c849_seed_failure "$donor" NpmStagedIntegrityFailed
    printf 'npm-integrity=passed\n' > "$CASE_DIR/npm-integrity.txt"
    payload_hash="$(sha256sum "$stage/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)"
    reference_hash="$(sha256sum "$stage/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" | cut -d' ' -f1)"
    package_bytes="$(du -s -B1 "$stage/packages" | cut -f1)"
    npm_bytes="$(du -s -B1 "$stage/npm" | cut -f1)"
    for item in "$C849_PACKAGES:packages" "$C849_NPM:npm"; do
        name="${item%%:*}"; source="${item#*:}"
        docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
            --mount "type=bind,source=$stage/$source,target=/seed,readonly" \
            --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
            -c 'set -eu; cp -a /seed/. /cache/; chown -R 1654:1654 /cache; find /cache -type d -exec chmod u+rwx {} +; find /cache -type f -exec chmod u+rw {} +' \
            >/dev/null || c849_seed_failure "$donor" CacheSeedImportFailed
    done
    helper="c849-seed-$RUN"
    docker run -d --name "$helper" --network antiphon-build-slots --user 1654:1654 \
        --entrypoint sleep -e HOME=/home/app \
        -e NUGET_PACKAGES=/home/app/.nuget/packages \
        -e NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch \
        -e NPM_CONFIG_CACHE=/home/app/.npm \
        -e ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots \
        --mount "type=volume,source=$C849_PACKAGES,target=/home/app/.nuget/packages,volume-nocopy" \
        --mount "type=volume,source=$C849_SCRATCH,target=/var/cache/antiphon/nuget-scratch,volume-nocopy" \
        --mount "type=volume,source=$C849_NPM,target=/home/app/.npm/_cacache,volume-nocopy" \
        --mount "type=bind,source=$CHECKOUT/scripts/build-slot.ps1,target=/work/repos/antiphon/scripts/build-slot.ps1,readonly" \
        --mount "type=bind,source=$CHECKOUT/scripts/lib/build-slot.ps1,target=/work/repos/antiphon/scripts/lib/build-slot.ps1,readonly" \
        "$image" infinity >/dev/null || c849_seed_failure "$donor" CacheSeedProbeStartFailed
    if ! (c849_smoke "$helper" seed); then
        docker rm -f "$helper" >/dev/null 2>&1 || true
        c849_seed_failure "$donor" CacheSeedSmokeFailed
    fi
    docker rm -f "$helper" >/dev/null 2>&1 || true
    recovery="$SERVER2_ROOT/cache/recovery-$RUN"
    [ ! -e "$recovery" ] || c849_seed_failure "$donor" CacheRecoveryExists
    mv "$stage" "$recovery" || c849_seed_failure "$donor" CacheRecoverySaveFailed
    docker start "$donor" >/dev/null || c849_seed_failure "$donor" CacheDonorRestartFailed
    [ "$(docker inspect -f '{{.Id}}' "$donor")" = "$donor" ] \
        || c849_seed_failure "$donor" CacheDonorIdentityChanged
    for i in $(seq 1 30); do
        if c849_status_zero server2-temp reconnected; then break; fi
        sleep 2
    done
    c849_status_zero server2-temp reconnected || c849_seed_failure "$donor" CacheDonorReconnectFailed
    c849_status_body server2-temp | jq -c '{sessions,runnerSessions,queuedTasks,draining,retireWhenIdle,redirectTo,dispatchEligible,acceptingNewWork}' \
        > "$CASE_DIR/status.json" || write_result false CacheDonorReconnectReceiptMissing 2
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'donor=%s\nimage=%s\ntime=%s\npayload-sha256=%s\nreference-sha256=%s\npackage-bytes=%s\nnpm-bytes=%s\nrecovery=%s\n' \
        "$donor" "$donor_image" "$now" "$payload_hash" "$reference_hash" "$package_bytes" "$npm_bytes" "$recovery" > "$C849_READY.tmp-$RUN"
    mv "$C849_READY.tmp-$RUN" "$C849_READY"
    printf 'ready=true donor=%s payload-sha256=%s reference-sha256=%s package-bytes=%s npm-bytes=%s recovery=%s\n' \
        "$donor" "$payload_hash" "$reference_hash" "$package_bytes" "$npm_bytes" "$recovery" > "$CASE_DIR/seed.txt"
    write_result true '' 0
}

c849_require_ready() {
    [ -s "$C849_READY" ] || write_result false CacheSeedRequired 2
    local recovery
    recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
    case "$recovery" in "$SERVER2_ROOT"/cache/recovery-[a-z0-9]*) ;; *) write_result false CacheRecoveryInvalid 2 ;; esac
    [ -d "$recovery/packages" ] && [ -d "$recovery/npm" ] \
        || write_result false CacheRecoveryMissing 2
    local expected actual image
    expected="$(sed -n 's/^payload-sha256=//p' "$C849_READY" | head -n 1)"
    [[ "$expected" =~ ^[0-9a-f]{64}$ ]] || write_result false CacheSeedMarkerInvalid 2
    image="$(c849_image)"
    actual="$(docker run --rm --network none --user 1654:1654 --entrypoint sha256sum \
        --mount "type=volume,source=$C849_PACKAGES,target=/home/app/.nuget/packages,volume-nocopy" \
        "$image" /home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost \
        2>/dev/null | cut -d' ' -f1)"
    [ "$actual" = "$expected" ] || write_result false CacheSeedPayloadChanged 2
}

# Read-only observation for preview. Do not call c849_prepare here: it can create
# volumes and its uid probe writes to each mount.
c849_observe_volume() {
    local name="$1" role="$2" budget="$3" driver options owner schema actual_role mountpoint docker_root resolved bytes mode
    case "$name:$role" in
        "$C849_PACKAGES:nuget-packages"|"$C849_SCRATCH:nuget-scratch"|"$C849_NPM:npm-content") ;;
        *) write_result false CacheTargetInvalid 2 ;;
    esac
    docker volume inspect "$name" >/dev/null 2>&1 || write_result false CacheVolumeMissing 2
    driver="$(docker volume inspect -f '{{.Driver}}' "$name")"
    options="$(docker volume inspect -f '{{json .Options}}' "$name")"
    owner="$(docker volume inspect -f '{{index .Labels "io.antiphon.owner"}}' "$name")"
    schema="$(docker volume inspect -f '{{index .Labels "io.antiphon.cache-schema"}}' "$name")"
    actual_role="$(docker volume inspect -f '{{index .Labels "io.antiphon.cache-role"}}' "$name")"
    if [ "$driver" != local ] || [[ "$options" != null && "$options" != '{}' ]] \
        || [ "$owner" != server2-runner ] || [ "$schema" != 1 ] || [ "$actual_role" != "$role" ]; then
        write_result false CacheVolumeForeign 2
    fi
    docker_root="$(docker info -f '{{.DockerRootDir}}')"
    mountpoint="$(docker volume inspect -f '{{.Mountpoint}}' "$name")"
    [ -n "$docker_root" ] && [ -n "$mountpoint" ] || write_result false CacheTargetInvalid 2
    resolved="$(realpath -e -- "$mountpoint" 2>/dev/null)" || write_result false CacheTargetInvalid 2
    if [ "$resolved" != "$docker_root/volumes/$name/_data" ] || [ -L "$mountpoint" ]; then
        write_result false CacheTargetInvalid 2
    fi
    mode="$(sudo -n stat -c '%u:%g:%a' -- "$resolved")" || write_result false CacheRootInvalid 2
    [ "$mode" = 1654:1654:700 ] || write_result false CacheRootOwnershipInvalid 2
    bytes="$(sudo -n du -s -B1 -- "$resolved" | cut -f1)" || write_result false CacheSizeUnavailable 2
    [[ "$bytes" =~ ^[0-9]+$ ]] || write_result false CacheSizeInvalid 2
    if [ "$CASE" = runner-cache-fixture ]; then
        case "$role" in
            nuget-packages) budget="${C849_FIXTURE_PACKAGES_BUDGET:-$budget}" ;;
            nuget-scratch) budget="${C849_FIXTURE_SCRATCH_BUDGET:-$budget}" ;;
            npm-content) budget="${C849_FIXTURE_NPM_BUDGET:-$budget}" ;;
        esac
        [[ "$budget" =~ ^[0-9]+$ ]] && [ "$budget" -gt 0 ] \
            || write_result false CacheBudgetInvalid 2
    fi
    printf '%s %s %s %s %s %s\n' "$name" "$role" "$resolved" "$mode" "$bytes" "$budget"
}

c849_budget_state() {
    local bytes="$1" budget="$2"
    [[ "$bytes" =~ ^[0-9]+$ && "$budget" =~ ^[0-9]+$ ]] && [ "$budget" -gt 0 ] \
        || { printf 'CacheBudgetInvalid\n'; return 2; }
    if [ "$bytes" -ge "$budget" ]; then printf 'OVER\n'
    elif [ $((bytes * 5)) -ge $((budget * 4)) ]; then printf 'WARN\n'
    else printf 'OK\n'; fi
}

c849_headroom_state() {
    local free="$1"
    [[ "$free" =~ ^[0-9]+$ ]] || { printf 'CacheDiskUnavailable\n'; return 2; }
    if [ "$free" -lt 21474836480 ]; then printf 'LOW\n'; else printf 'OK\n'; fi
}

c849_preview() {
    require_lane host
    c849_lock
    local roots="$CASE_DIR/volumes.txt" recovery recovery_bytes free_bytes available_kb sha now runner status_code status_file project container active broker occupancy
    : > "$roots"
    c849_observe_volume "$C849_PACKAGES" nuget-packages 10737418240 >> "$roots"
    c849_observe_volume "$C849_SCRATCH" nuget-scratch 268435456 >> "$roots"
    c849_observe_volume "$C849_NPM" npm-content 2147483648 >> "$roots"
    recovery_bytes=0
    if [ -s "$C849_READY" ]; then
        recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
        case "$recovery" in "$SERVER2_ROOT"/cache/recovery-[a-z0-9]*) ;; *) write_result false CacheRecoveryInvalid 2 ;; esac
        [ -d "$recovery" ] && [ ! -L "$recovery" ] || write_result false CacheRecoveryMissing 2
        recovery_bytes="$(sudo -n du -s -B1 -- "$recovery" | cut -f1)"
    fi
    available_kb="$(df -Pk "$(docker info -f '{{.DockerRootDir}}')" | awk 'NR==2 {print $4}')"
    [[ "$available_kb" =~ ^[0-9]+$ ]] || write_result false CacheDiskUnavailable 2
    free_bytes=$((available_kb * 1024))
    sha="$(sha256sum "$roots" | cut -d' ' -f1)"
    now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'schema=1\nrun=%s\nsource-sha=%s\nvolume-sha256=%s\nrecovery-bytes=%s\nfree-bytes=%s\ncreated-at=%s\n' \
        "$RUN" "$SHA" "$sha" "$recovery_bytes" "$free_bytes" "$now" > "$CASE_DIR/preview.txt"
    : > "$CASE_DIR/budgets.txt"
    while read -r name role path mode bytes budget; do
        printf 'role=%s bytes=%s budget=%s state=%s\n' "$role" "$bytes" "$budget" \
            "$(c849_budget_state "$bytes" "$budget")" >> "$CASE_DIR/budgets.txt"
    done < "$roots"
    printf 'headroom=%s\n' "$(c849_headroom_state "$free_bytes")" >> "$CASE_DIR/preview.txt"
    : > "$CASE_DIR/consumers.txt"
    if [ "$CASE" = runner-cache-fixture ]; then
        [ -s "$SERVER2_ROOT/fixture-consumers.txt" ] \
            || write_result false CacheConsumerUnknown 2
        cp "$SERVER2_ROOT/fixture-consumers.txt" "$CASE_DIR/consumers.txt"
    else
    for runner in server2 server2-temp; do
        status_file="$CASE_DIR/.status-$runner"
        status_code="$(curl -sS --max-time 15 -o "$status_file" -w '%{http_code}' \
            "${C604_SERVER_ORIGIN:?}/api/session-runners/$runner/status" 2>/dev/null || true)"
        if [ "$status_code" = 200 ]; then
            jq -r --arg r "$runner" '["runner="+$r,"sessions="+(.sessions|tostring),"runnerSessions="+(.runnerSessions|tostring),"queuedTasks="+(.queuedTasks|tostring),"draining="+(.draining|tostring),"accepting="+(.acceptingNewWork|tostring)]|join(" ")' \
                "$status_file" >> "$CASE_DIR/consumers.txt" || printf 'runner=%s status=unknown\n' "$runner" >> "$CASE_DIR/consumers.txt"
        else
            printf 'runner=%s status=unknown\n' "$runner" >> "$CASE_DIR/consumers.txt"
        fi
        rm -f -- "$status_file"
        project="$HOST_PROJECT"; [ "$runner" = server2-temp ] && project="$TEMP_PROJECT"
        container="$(docker ps -q --filter "label=com.docker.compose.project=$project" --filter 'label=com.docker.compose.service=session-runner')"
        active=unknown
        if [ -n "$container" ]; then
            active="$(docker exec "$container" /bin/sh -c \
                "ps -eo uid,args | awk '\$1==1654 && \$0 !~ /Antiphon.SessionRunner/ {n++} END {print n+0}'" 2>/dev/null || true)"
        fi
        printf 'runner=%s cache-processes=%s\n' "$runner" "$active" >> "$CASE_DIR/consumers.txt"
    done
    broker="$(compose_host --profile broker ps -q build-slots 2>/dev/null || true)"
    occupancy=unknown
    if [ -n "$broker" ]; then
        occupancy="$(docker exec "$broker" curl -fsS --max-time 10 http://127.0.0.1:8080/build-slots \
            2>/dev/null | jq -r '.occupied // "unknown"' 2>/dev/null || true)"
    fi
    printf 'build-slots-occupied=%s\n' "$occupancy" >> "$CASE_DIR/consumers.txt"
    fi
    local durable="$SERVER2_ROOT/cache/previews/$RUN"
    [ ! -e "$durable" ] || write_result false CachePreviewAlreadyExists 2
    sudo -n install -d -o mc -g mc -m 0700 "$SERVER2_ROOT/cache/previews" "$durable"
    install -m 0600 "$CASE_DIR/volumes.txt" "$durable/volumes.txt"
    install -m 0600 "$CASE_DIR/consumers.txt" "$durable/consumers.txt"
    install -m 0600 "$CASE_DIR/preview.txt" "$durable/preview.txt"
    write_result true '' 0
}

c849_budget_gate() {
    local free_kb name role path mode bytes budget
    : > "$CASE_DIR/cache-budget.txt"
    c849_observe_volume "$C849_PACKAGES" nuget-packages 10737418240 >> "$CASE_DIR/cache-budget.txt"
    c849_observe_volume "$C849_SCRATCH" nuget-scratch 268435456 >> "$CASE_DIR/cache-budget.txt"
    c849_observe_volume "$C849_NPM" npm-content 2147483648 >> "$CASE_DIR/cache-budget.txt"
    while read -r name role path mode bytes budget; do
        if [ "$bytes" -gt "$budget" ]; then write_result false CacheBudgetExceeded 2; fi
        if [ $((bytes * 5)) -ge $((budget * 4)) ]; then
            printf 'WARN CacheBudget80 role=%s bytes=%s budget=%s\n' "$role" "$bytes" "$budget" \
                >> "$CASE_DIR/cache-warnings.txt"
        fi
    done < "$CASE_DIR/cache-budget.txt"
    free_kb="$(df -Pk "$(docker info -f '{{.DockerRootDir}}')" | awk 'NR==2 {print $4}')"
    [[ "$free_kb" =~ ^[0-9]+$ ]] || write_result false CacheDiskUnavailable 2
    [ "$free_kb" -ge 20971520 ] || write_result false CacheDiskLow 2
    printf 'free-bytes=%s\n' "$((free_kb * 1024))" >> "$CASE_DIR/cache-budget.txt"
}

c849_prune_idle() {
    local runner project container body broker mounted expected other active
    if [ "$CASE" = runner-cache-fixture ]; then
        grep -Fxq 'runners=idle processes=0 leases=0' "$SERVER2_ROOT/fixture-consumers.txt" \
            || write_result false CacheConsumersBusy 2
        for mounted in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
            [ -z "$(docker ps -q --filter "volume=$mounted")" ] \
                || write_result false CacheConsumersBusy 2
        done
        return 0
    fi
    for runner in server2 server2-temp; do
        body="$(c849_status_body "$runner")" \
            || write_result false CacheStatusUnavailable 2
        printf '%s' "$body" | jq -e '
            .sessions != null and .runnerSessions != null and .queuedTasks != null and
            .sessions == 0 and .runnerSessions == 0 and .queuedTasks == 0 and
            .draining == true and .acceptingNewWork == false' >/dev/null \
            || write_result false CacheConsumersBusy 2
        project="$HOST_PROJECT"; [ "$runner" = server2-temp ] && project="$TEMP_PROJECT"
        container="$(docker ps -q --filter "label=com.docker.compose.project=$project" \
            --filter 'label=com.docker.compose.service=session-runner')"
        if [ -z "$container" ]; then
            [ "$runner" = server2-temp ] \
                && printf '%s' "$body" | jq -e '.retiredAt != null' >/dev/null \
                && [ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$TEMP_PROJECT")" ] \
                || write_result false CacheConsumerUnknown 2
            continue
        fi
        active="$(docker exec "$container" /bin/sh -c \
            "ps -eo uid,args | awk '\$1==1654 && \$0 !~ /Antiphon.SessionRunner/ {n++} END {print n+0}'")" \
            || write_result false CacheConsumerUnknown 2
        [ "$active" = 0 ] || write_result false CacheConsumersBusy 2
    done
    # A third container attached to any cache is a consumer even if the two named
    # runners report zero. Compare IDs, not container names supplied by a task.
    for mounted in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        while read -r other; do
            [ -n "$other" ] || continue
            expected="$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}:{{index .Config.Labels "com.docker.compose.service"}}' "$other")"
            case "$expected" in "$HOST_PROJECT:session-runner"|"$TEMP_PROJECT:session-runner") ;; *) write_result false CacheConsumersBusy 2 ;; esac
        done < <(docker ps -q --filter "volume=$mounted")
    done
    broker="$(compose_host --profile broker ps -q build-slots 2>/dev/null || true)"
    [ -n "$broker" ] || write_result false CacheBuildSlotsUnavailable 2
    docker exec "$broker" curl -fsS --max-time 10 http://127.0.0.1:8080/build-slots \
        | jq -e '(.occupied | type) == "number" and .occupied == 0 and (.leases | type) == "array" and (.leases | length) == 0' >/dev/null \
        || write_result false CacheBuildSlotsBusy 2
}

c849_reset() {
    require_lane host
    c849_lock
    [ ! -e "$C849_READY" ] || write_result false CacheSeedAlreadyReady 2
    # A reset is only for an interrupted, unmarked seed. The common idle check
    # proves both runners drained and the build broker empty under this lock.
    c849_prune_idle
    local image name role container
    image="$(c849_image)"
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        case "$name" in
            "$C849_PACKAGES") role=nuget-packages ;;
            "$C849_SCRATCH") role=nuget-scratch ;;
            "$C849_NPM") role=npm-content ;;
        esac
        c849_volume "$name" "$role" no "$image"
        # Include stopped containers; Docker refuses volume removal for them too.
        [ -z "$(docker ps -aq --filter "volume=$name")" ] \
            || write_result false CacheResetInUse 2
    done
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        [ -z "$(docker ps -aq --filter "volume=$name")" ] \
            || write_result false CacheResetInUse 2
        docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
            --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
            -c 'set -eu; test ! -L /cache; test "$(stat -c %u:%g:%a /cache)" = 1654:1654:700; find /cache -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +' >/dev/null \
            || write_result false CacheResetFailed 2
        c849_empty_volume "$name" "$image" || write_result false CacheResetFailed 2
    done
    printf 'reset=true volumes=3 marker=absent\n' > "$CASE_DIR/reset.txt"
    write_result true '' 0
}

c849_prune_validate_tree() {
    local path="$1" canonical="$2" resolved unsafe mounts
    [ -n "$path" ] && [ "$path" = "$canonical" ] && [ -d "$path" ] && [ ! -L "$path" ] \
        || write_result false CacheTargetInvalid 2
    resolved="$(realpath -e -- "$path")" || write_result false CacheTargetInvalid 2
    [ "$resolved" = "$canonical" ] || write_result false CacheTargetInvalid 2
    unsafe="$(sudo -n find "$path" -xdev -mindepth 1 \( -type l -o -type b -o -type c -o -type p -o -type s -o -type f -links +1 \) -print -quit)" \
        || write_result false CacheTargetInvalid 2
    [ -z "$unsafe" ] || write_result false CacheTargetInvalid 2
    mounts="$(findmnt -rn -o TARGET)" || write_result false CacheTargetInvalid 2
    if printf '%s\n' "$mounts" | awk -v p="$path/" 'index($0,p)==1 {found=1} END {exit !found}'; then
        write_result false CacheTargetInvalid 2
    fi
}

c849_prune() {
    require_lane host
    [[ "${C590_PREVIEW_RUN:-}" =~ ^c849[0-9a-f]{16}0$ ]] || write_result false CachePreviewInvalid 2
    c849_lock
    local receipt="$SERVER2_ROOT/cache/previews/$C590_PREVIEW_RUN" age created previous_sha actual_sha
    [ -d "$receipt" ] && [ ! -L "$receipt" ] \
        && [ -f "$receipt/preview.txt" ] && [ -f "$receipt/volumes.txt" ] \
        || write_result false CachePreviewMissing 2
    grep -Fxq "run=$C590_PREVIEW_RUN" "$receipt/preview.txt" \
        && grep -Fxq "source-sha=$SHA" "$receipt/preview.txt" \
        || write_result false CachePreviewStale 2
    created="$(sed -n 's/^created-at=//p' "$receipt/preview.txt" | head -n 1)"
    age=$(( $(date -u +%s) - $(date -u -d "$created" +%s 2>/dev/null || echo 0) ))
    [ "$age" -ge 0 ] && [ "$age" -le 3600 ] || write_result false CachePreviewStale 2
    previous_sha="$(sed -n 's/^volume-sha256=//p' "$receipt/preview.txt" | head -n 1)"
    actual_sha="$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)"
    [ "$actual_sha" = "$previous_sha" ] || write_result false CachePreviewStale 2
    : > "$CASE_DIR/volumes-now.txt"
    c849_observe_volume "$C849_PACKAGES" nuget-packages 10737418240 >> "$CASE_DIR/volumes-now.txt"
    c849_observe_volume "$C849_SCRATCH" nuget-scratch 268435456 >> "$CASE_DIR/volumes-now.txt"
    c849_observe_volume "$C849_NPM" npm-content 2147483648 >> "$CASE_DIR/volumes-now.txt"
    cmp -s "$receipt/volumes.txt" "$CASE_DIR/volumes-now.txt" \
        || write_result false CachePreviewStale 2
    c849_prune_idle
    c849_require_ready
    local name role path mode bytes budget selected=0 image recovery main_container temp_container expected_hash actual_hash donor_image required_bytes
    image="$(c849_image)"
    recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
    [ -d "$recovery/packages" ] && [ ! -L "$recovery" ] \
        && [ -s "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ] \
        || write_result false CacheRecoveryMissing 2
    expected_hash="$(sed -n 's/^payload-sha256=//p' "$C849_READY" | head -n 1)"
    actual_hash="$(sha256sum "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" 2>/dev/null | cut -d' ' -f1)"
    [ "$actual_hash" = "$expected_hash" ] || write_result false CacheRecoveryChanged 2
    donor_image="$(sed -n 's/^image=//p' "$C849_READY" | head -n 1)"
    [[ "$donor_image" =~ ^sha256:[0-9a-f]{64}$ ]] \
        && docker image inspect "$donor_image" >/dev/null 2>&1 \
        || write_result false CacheRecoveryImageMissing 2
    # Complete the authority and target preflight before the first destructive
    # operation. A later invalid root must not leave an earlier root half-cleared.
    while read -r name role path mode bytes budget; do
        c849_prune_validate_tree "$path" "$path"
        case "$role" in nuget-packages|nuget-scratch|npm-content) ;; *) write_result false CacheTargetInvalid 2 ;; esac
        required_bytes=0
        if [ "$role" = nuget-packages ]; then
            required_bytes="$(du -s -B1 "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20" "$recovery/packages/microsoft.netcore.app.ref/9.0.20" | awk '{s+=$1} END {print s+0}')" \
                || write_result false CacheRecoveryMissing 2
        fi
        [ "$required_bytes" -le "$budget" ] || write_result false CacheBudgetExceeded 2
        if [ $((bytes * 5)) -ge $((budget * 4)) ]; then selected=$((selected + 1)); fi
    done < "$CASE_DIR/volumes-now.txt"
    [ "$selected" -gt 0 ] || write_result false CacheNoPruneCandidate 2
    while read -r name role path mode bytes budget; do
        if [ $((bytes * 5)) -lt $((budget * 4)) ]; then continue; fi
        case "$role" in
            nuget-packages)
                docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
                    --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" -c '
                    set -eu
                    find /cache -mindepth 2 -maxdepth 2 -type d -exec sh -c '\''for v do rm -f -- "$v/.nupkg.metadata"; rm -rf -- "$v"; done'\'' sh {} +
                    find /cache -mindepth 1 -maxdepth 1 -type d -empty -delete
                    ' >/dev/null || write_result false CachePruneFailed 2
                docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
                    --mount "type=bind,source=$recovery/packages,target=/seed,readonly" \
                    --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" -c '
                    set -eu
                    for p in microsoft.netcore.app.host.linux-x64 microsoft.netcore.app.ref; do
                        mkdir -p "/cache/$p"
                        cp -a "/seed/$p/9.0.20" "/cache/$p/"
                        chown -R 1654:1654 "/cache/$p/9.0.20"
                    done' >/dev/null || write_result false CacheRefillFailed 2
                ;;
            nuget-scratch|npm-content)
                docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
                    --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
                    -c 'set -eu; find /cache -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +' >/dev/null \
                    || write_result false CachePruneFailed 2 ;;
            *) write_result false CacheTargetInvalid 2 ;;
        esac
    done < "$CASE_DIR/volumes-now.txt"
    if [ "$CASE" = runner-cache-fixture ]; then
        c849_fixture_refill "$image"
        c849_budget_gate
        printf 'pruned=%s receipt=%s refill=passed smokes=1 admission=held\n' \
            "$selected" "$C590_PREVIEW_RUN" > "$CASE_DIR/prune.txt"
        write_result true '' 0
    fi
    main_container="$(docker ps -q --filter "label=com.docker.compose.project=$HOST_PROJECT" --filter 'label=com.docker.compose.service=session-runner')"
    temp_container="$(docker ps -q --filter "label=com.docker.compose.project=$TEMP_PROJECT" --filter 'label=com.docker.compose.service=session-runner')"
    [ -n "$main_container" ] || write_result false CacheConsumerUnknown 2
    [ "$(docker exec -u 1654:1654 "$main_container" git -C /work/repos/antiphon rev-parse HEAD)" = "$SHA" ] \
        || write_result false CacheSourceMismatch 2
    docker exec -u 1654:1654 -e HOME=/home/app -i "$main_container" /bin/sh -s > "$CASE_DIR/refill-summary.txt" <<'C849_REFILL' \
        || write_result false CacheRefillFailed 2
set -eu
root="$(mktemp -d /tmp/c849-refill-XXXXXXXX)"
trap 'rm -rf "$root"' EXIT
git -C /work/repos/antiphon archive HEAD | tar -x -C "$root"
cat > "$root/driver.sh" <<'DRIVER'
#!/bin/sh
set -eu
cd "$1"
dotnet restore Antiphon.sln -maxcpucount:1 -nodeReuse:false -p:NuGetAudit=false >/dev/null
cd client
npm ci --ignore-scripts --no-audit --no-fund >/dev/null
DRIVER
pwsh -NoProfile -File /work/repos/antiphon/scripts/build-slot.ps1 -Label c849-refill -- /bin/sh "$root/driver.sh" "$root" >/dev/null
printf 'C849_REFILL restore=0 npm=0\n'
C849_REFILL
    grep -Fxq 'C849_REFILL restore=0 npm=0' "$CASE_DIR/refill-summary.txt" \
        || write_result false CacheRefillReceiptMissing 2
    c849_smoke "$main_container" server2
    if [ -n "$temp_container" ]; then c849_smoke "$temp_container" server2-temp; fi
    c849_budget_gate
    printf 'pruned=%s receipt=%s refill=passed smokes=%s admission=held\n' \
        "$selected" "$C590_PREVIEW_RUN" "$([ -n "$temp_container" ] && printf 2 || printf 1)" > "$CASE_DIR/prune.txt"
    write_result true '' 0
}

# The Fixture case uses only run-scoped Docker resources. A failed group is a
# refusal; it never falls through to a passing summary or touches the live names.
c849_fixture_model() {
    local model="$1" key name target env
    for item in \
        'runner-nuget-packages:antiphon-runner-cache-nuget-packages:/home/app/.nuget/packages:NUGET_PACKAGES:/home/app/.nuget/packages' \
        'runner-nuget-scratch:antiphon-runner-cache-nuget-scratch:/var/cache/antiphon/nuget-scratch:NUGET_SCRATCH:/var/cache/antiphon/nuget-scratch' \
        'runner-npm-content:antiphon-runner-cache-npm-content:/home/app/.npm/_cacache:NPM_CONFIG_CACHE:/home/app/.npm'; do
        IFS=: read -r key name target env expected <<< "$item"
        jq -e --arg k "$key" '.volumes[$k].external == true' "$model" >/dev/null \
            || { printf 'ExternalRequired\n'; return 2; }
        jq -e --arg k "$key" --arg n "$name" '.volumes[$k].name == $n' "$model" >/dev/null \
            || { printf 'CacheIdentityMismatch\n'; return 2; }
        jq -e --arg k "$key" --arg t "$target" \
            '[.services["session-runner"].volumes[] | select(.source == $k and .target == $t and .type == "volume" and .volume.nocopy == true)] | length == 1' \
            "$model" >/dev/null || { printf 'CacheMountTooBroad\n'; return 2; }
        jq -e --arg k "$env" --arg v "$expected" \
            '.services["session-runner"].environment[$k] == $v' "$model" >/dev/null \
            || { printf 'ScratchPathMismatch\n'; return 2; }
    done
    jq -e '.services["session-runner"].environment.TMPDIR == "/tmp"' "$model" >/dev/null \
        || { printf 'PrivateVolumeCollision\n'; return 2; }
    for item in 'runner-tmp:/tmp' 'work:/work' 'runner-state:/state' 'dind-data:/var/lib/docker'; do
        IFS=: read -r key target <<< "$item"
        jq -e --arg k "$key" --arg t "$target" \
            '[.services["session-runner"].volumes[] | select(.source == $k and .target == $t and .type == "volume")] | length == 1' \
            "$model" >/dev/null || { printf 'PrivateVolumeCollision\n'; return 2; }
        jq -e --arg k "$key" '.volumes[$k].external != true' "$model" >/dev/null \
            || { printf 'PrivateVolumeCollision\n'; return 2; }
    done
}

c849_fixture_control() {
    local id="$1" diagnosis="$2" actual="$CASE_DIR/.control-$id" code=0
    shift 2
    ( write_result() { printf '%s\n' "$2"; exit "$3"; }; "$@" ) > "$actual" 2>&1 || code=$?
    [ "$code" -ne 0 ] && grep -Fxq "$diagnosis" "$actual" \
        || write_result false "ControlNotSensitive $id" 2
    printf '%s %s\n' "$id" "$diagnosis" >> "$CASE_DIR/fixture-control-variants.txt"
    if ! grep -q "^CONTROL $id " "$CASE_DIR/fixture-controls.txt"; then
        printf 'CONTROL %s expected-red observed=%s\n' "$id" "$diagnosis" >> "$CASE_DIR/fixture-controls.txt"
    fi
}

c849_fixture_model_fault() {
    local model="$1" filter="$2" altered="$SERVER2_ROOT/model-fault.json"
    jq "$filter" "$model" > "$altered" || return 1
    c849_fixture_model "$altered"
}

c849_fixture_compose() {
    local dir="$SERVER2_ROOT/compose" env="$SERVER2_ROOT/compose/fixture.env" project file
    mkdir -p "$dir"
    for file in token gitconfig codex-home deploy-key phone-home grok-home; do
        : > "$dir/$file"
    done
    cat > "$env" <<EOF
SOURCE_SHA12=${SHA:0:12}
BUILD_SLOTS_SHA12=${SHA:0:12}
PHONE_HOME_SERVER_ORIGIN=http://127.0.0.1:9
CLAUDE_OAUTH_TOKEN_FILE=$dir/token
RUNNER_GIT_IDENTITY_FILE=$dir/gitconfig
RUNNER_CODEX_HOME_DIR=$dir/codex-home
ANTIPHON_DEPLOY_KEY_FILE=$dir/deploy-key
PHONE_HOME_SECRET_FILE=$dir/phone-home
RUNNER_GROK_STORE_DIR=$dir/grok-home
EOF
    docker compose --env-file "$env" -p "c849${RUN}main" -f "$CHECKOUT/docker-compose.server2-runner.yml" config --format json > "$dir/main.json" \
        || write_result false FixtureComposeRenderFailed 2
    docker compose --env-file "$env" -p "c849${RUN}temp" -f "$CHECKOUT/docker-compose.server2-runner.yml" \
        -f "$CHECKOUT/docker-compose.server2-runner.temp.yml" config --format json > "$dir/temp.json" \
        || write_result false FixtureComposeRenderFailed 2
    for model in "$dir/main.json" "$dir/temp.json"; do
        c849_fixture_model "$model" || write_result false FixtureComposeInvalid 2
    done
    c849_fixture_control PC-01 ExternalRequired c849_fixture_model_fault "$dir/main.json" '.volumes["runner-nuget-packages"].external = false'
    c849_fixture_control PC-02 CacheIdentityMismatch c849_fixture_model_fault "$dir/temp.json" '.volumes["runner-nuget-packages"].name = "foreign"'
    c849_fixture_control PC-03 ScratchPathMismatch c849_fixture_model_fault "$dir/main.json" 'del(.services["session-runner"].environment.NUGET_SCRATCH)'
    c849_fixture_control PC-04 PrivateVolumeCollision c849_fixture_model_fault "$dir/temp.json" '.volumes["runner-tmp"].external = true'
    c849_fixture_control PC-05 CacheMountTooBroad c849_fixture_model_fault "$dir/main.json" '(.services["session-runner"].volumes[] | select(.source == "runner-nuget-packages")).target = "/home/app/.nuget"'
    printf 'PASS F-1\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_cleanup() {
    local name
    for name in "c849-${RUN}-restore-p1" "c849-${RUN}-restore-p2" "c849-${RUN}-restore-p3" \
        "c849-${RUN}-lock-holder-shared" "c849-${RUN}-lock-waiter-shared" \
        "c849-${RUN}-lock-holder-private" "c849-${RUN}-lock-waiter-private" \
        "c849-${RUN}-apphost-miss" "c849-${RUN}-npm-fill" \
        "c849-${RUN}-npm-b" "c849-${RUN}-npm-b-empty"; do
        docker rm -f "$name" >/dev/null 2>&1 || true
    done
    [ -f "$CASE_DIR/.fixture-volumes" ] || return 0
    while read -r name; do
        [[ "$name" =~ ^c849[a-z0-9]{1,64}-(packages|scratch|npm)$ ]] || continue
        docker volume rm "$name" >/dev/null 2>&1 || true
    done < "$CASE_DIR/.fixture-volumes"
}

c849_fixture_volume_fault() {
    local kind="$1" name="$2" role="$3" image="$4"
    write_result() { printf '%s\n' "$2"; exit "$3"; }
    if [ "$kind" = label ] || [ "$kind" = options ]; then
        docker() {
            if [ "$1" = volume ] && [ "$2" = inspect ] && [ "${3:-}" = -f ]; then
                case "$kind:$4" in
                    label:*io.antiphon.owner*) printf 'foreign\n'; return 0 ;;
                    options:*'.Options'*) printf '{"device":"foreign"}\n'; return 0 ;;
                esac
            fi
            command docker "$@"
        }
    fi
    c849_volume "$name" "$role" no "$image"
}

c849_fixture_prepare() {
    local image name original_mode
    C849_PACKAGES="c849${RUN}-packages"
    C849_SCRATCH="c849${RUN}-scratch"
    C849_NPM="c849${RUN}-npm"
    C849_READY="$SERVER2_ROOT/cache/fixture-seed-accepted"
    : > "$CASE_DIR/.fixture-volumes"
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        [[ "$name" =~ ^c849[a-z0-9]{1,64}-(packages|scratch|npm)$ ]] \
            || write_result false FixtureNamespaceInvalid 2
        docker volume inspect "$name" >/dev/null 2>&1 \
            && write_result false FixtureNamespaceOccupied 2
        printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
    done
    image="$(c849_image)"
    c849_prepare yes
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
            --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
            -c 'printf "fixture-payload\n" > /cache/fixture-sentinel; chmod 0700 /cache/fixture-sentinel' \
            || write_result false FixtureRootNotWritable 2
    done
    printf 'accepted\n' > "$C849_READY"
    c849_prepare yes
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
            --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
            -c 'test "$(cat /cache/fixture-sentinel)" = fixture-payload && test "$(stat -c %a /cache)" = 700 && test "$(stat -c %a /cache/fixture-sentinel)" = 700' \
            || write_result false FixturePrepareNotIdempotent 2
    done
    docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
        --mount "type=volume,source=$C849_PACKAGES,target=/cache,volume-nocopy" "$image" \
        -c 'chown 0:0 /cache' || write_result false FixtureControlSetupFailed 2
    c849_fixture_control PC-06 CacheRootOwnershipInvalid c849_fixture_volume_fault owner "$C849_PACKAGES" nuget-packages "$image"
    docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
        --mount "type=volume,source=$C849_PACKAGES,target=/cache,volume-nocopy" "$image" \
        -c 'chown 1654:1654 /cache; chmod 0755 /cache' || write_result false FixtureControlSetupFailed 2
    c849_fixture_control PC-07 CacheRootOwnershipInvalid c849_fixture_volume_fault mode "$C849_PACKAGES" nuget-packages "$image"
    docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
        --mount "type=volume,source=$C849_PACKAGES,target=/cache,volume-nocopy" "$image" \
        -c 'chmod 0700 /cache' || write_result false FixtureControlSetupFailed 2
    ln -s "$CASE_DIR" "$CASE_DIR/.fixture-escape"
    c849_fixture_control PC-08 CacheTargetInvalid c849_prune_validate_tree "$CASE_DIR/.fixture-escape" "$CASE_DIR/.fixture-escape"
    c849_fixture_control PC-09 CacheVolumeForeign c849_fixture_volume_fault label "$C849_PACKAGES" nuget-packages "$image"
    c849_fixture_control PC-10 CacheVolumeForeign c849_fixture_volume_fault options "$C849_PACKAGES" nuget-packages "$image"
    docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
        --mount "type=volume,source=$C849_PACKAGES,target=/cache,volume-nocopy" "$image" \
        -c 'test "$(cat /cache/fixture-sentinel)" = fixture-payload && test "$(stat -c %a /cache)" = 700' \
        || write_result false FixtureControlMutatedSibling 2
    printf 'PASS F-2\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_tree_fault() {
    local fault="$1" tree="$SERVER2_ROOT/.tree-$fault"
    cp -a "$SERVER2_ROOT/.donor-tree" "$tree" || return 1
    case "$fault" in
        host) rm -f "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" ;;
        metadata) : > "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ;;
        reference) : > "$tree/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" ;;
        symlink) ln -s "$CASE_DIR" "$tree/packages/escape" ;;
        hardlink) ln "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" "$tree/packages/escape" ;;
        special) mkfifo "$tree/packages/escape" ;;
    esac
    c849_validate_seed_tree "$tree"
}

c849_fixture_seed() {
    local image donor tree name before after recovery npm_work
    image="$(c849_image)"
    TEMP_PROJECT="c849${RUN}temp"
    C849_PACKAGES="c849${RUN}seed-packages"
    C849_SCRATCH="c849${RUN}seed-scratch"
    C849_NPM="c849${RUN}seed-npm"
    C849_READY="$SERVER2_ROOT/cache/fixture-seed-ready"
    for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        docker volume inspect "$name" >/dev/null 2>&1 && write_result false FixtureNamespaceOccupied 2
        printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
    done
    tree="$SERVER2_ROOT/.donor-tree"
    mkdir -p "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native" \
        "$tree/packages/microsoft.netcore.app.ref/9.0.20" "$tree/npm"
    printf 'fixture-native-host\n' > "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
    chmod 0755 "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
    printf 'fixture-metadata\n' > "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata"
    printf 'fixture-metadata\n' > "$tree/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata"
    c849_validate_seed_tree "$tree" || write_result false FixtureDonorTreeInvalid 2
    c849_fixture_control PC-12 AppHostDonorMissing c849_fixture_tree_fault host
    c849_fixture_control PC-13 AppHostDonorMetadataMissing c849_fixture_tree_fault metadata
    c849_fixture_control PC-13 Net9ReferenceDonorMissing c849_fixture_tree_fault reference
    c849_fixture_control PC-14 CacheDonorUnsafePath c849_validate_seed_relative '../escape'
    c849_fixture_control PC-14 CacheDonorUnsafePath c849_validate_seed_relative '/tmp/escape'
    c849_fixture_control PC-15 CacheDonorUnsafeEntry c849_fixture_tree_fault symlink
    c849_fixture_control PC-15 CacheDonorUnsafeEntry c849_fixture_tree_fault hardlink
    c849_fixture_control PC-15 CacheDonorUnsafeEntry c849_fixture_tree_fault special
    donor="$(docker create --name "c849-${RUN}-donor" \
        --label "com.docker.compose.project=$TEMP_PROJECT" \
        --label com.docker.compose.service=session-runner \
        --entrypoint sleep "$image" infinity)" || write_result false FixtureDonorCreateFailed 2
    printf '%s\n' "$donor" > "$CASE_DIR/.fixture-donor"
    docker start "$donor" >/dev/null || write_result false FixtureDonorStartFailed 2
    docker exec "$donor" mkdir -p /home/app/.nuget/packages /home/app/.npm/_cacache \
        || write_result false FixtureDonorSetupFailed 2
    docker cp "$tree/packages/." "$donor:/home/app/.nuget/packages/" \
        || write_result false FixtureDonorCopyFailed 2
    npm_work="$SERVER2_ROOT/npm-work"
    mkdir -p "$npm_work/package" "$npm_work/cache"
    printf '{"name":"c849-fixture","version":"1.0.0"}\n' > "$npm_work/package/package.json"
    printf 'C849_NPM_SEED\n' > "$npm_work/package/sentinel"
    tar -czf "$npm_work/package.tgz" -C "$npm_work" package
    docker run --rm --network none --user 0:0 --entrypoint npm \
        --mount "type=bind,source=$npm_work,target=/fixture" "$image" \
        cache add /fixture/package.tgz --cache /fixture/cache >/dev/null \
        || write_result false FixtureNpmCacheCreateFailed 2
    docker cp "$npm_work/cache/_cacache/." "$donor:/home/app/.npm/_cacache/" \
        || write_result false FixtureDonorNpmCopyFailed 2
    c849_fixture_control PC-11 CacheDonorIdentityInvalid c849_fixture_wrong_donor
    ( c849_status_zero() {
          [ "$1" = server2-temp ] || return 1
          [ "$(docker inspect -f '{{.State.Running}}' "$donor")" = true ]
      }
      c849_smoke() {
          docker exec -u 1654:1654 "$1" /bin/sh -c \
              'test -s /home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata && test -s /home/app/.nuget/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata' \
              || write_result false FixtureSeedProbeFailed 2
          printf 'fixture-seed-smoke=passed\n' > "$CASE_DIR/.fixture-seed-smoke"
      }
      c849_seed ) || write_result false FixtureSeedFailed 2
    [ -s "$CASE_DIR/.fixture-seed-smoke" ] && [ -s "$C849_READY" ] \
        || write_result false FixtureSeedMarkerOrderInvalid 2
    recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
    [ -s "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ] \
        && [ -s "$CASE_DIR/npm-integrity.txt" ] \
        && [ -n "$(find "$recovery/npm" -type f -print -quit)" ] \
        || write_result false FixtureRecoveryMissing 2
    before="$(sha256sum "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)"
    after="$(sed -n 's/^payload-sha256=//p' "$C849_READY" | head -n 1)"
    [ "$before" = "$after" ] && [ "$(stat -c %a "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost")" = 755 ] \
        || write_result false FixtureSeedPayloadChanged 2
    [ "$(docker inspect -f '{{.State.Running}}' "$donor")" = true ] \
        || write_result false FixtureDonorNotRestarted 2
    docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
        --mount "type=volume,source=$C849_PACKAGES,target=/cache,volume-nocopy" "$image" \
        -c 'printf "keep-after-rerun\n" > /cache/rerun-sentinel' \
        || write_result false FixtureSeedRerunSetupFailed 2
    ( c849_status_zero() { [ "$1" = server2-temp ] && [ "$(docker inspect -f '{{.State.Running}}' "$donor")" = true ]; }
      c849_seed ) || write_result false FixtureSeedRerunFailed 2
    docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
        --mount "type=volume,source=$C849_PACKAGES,target=/cache,volume-nocopy" "$image" \
        -c 'test "$(cat /cache/rerun-sentinel)" = keep-after-rerun' \
        || write_result false FixtureSeedRerunOverwrote 2
    printf '%s\n' "c849${RUN}busy-packages" >> "$CASE_DIR/.fixture-volumes"
    c849_fixture_control PC-16 CacheUnmarkedInUse c849_fixture_unmarked_consumer "$image"
    docker rm -f "$(cat "$CASE_DIR/.fixture-busy")" >/dev/null \
        || write_result false FixtureConsumerCleanupFailed 2
    rm -f "$CASE_DIR/.fixture-busy"
    printf 'PASS F-3\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_wrong_donor() {
    TEMP_PROJECT="c849${RUN}wrong"
    c849_donor
}

c849_fixture_unmarked_consumer() {
    local image="$1" name="c849${RUN}busy-packages" helper
    C849_PACKAGES="$name"
    C849_READY="$SERVER2_ROOT/cache/unmarked-ready"
    c849_volume "$name" nuget-packages yes "$image"
    docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
        --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" \
        -c 'printf "unmarked\n" > /cache/sentinel' || return 1
    helper="$(docker run -d --name "c849-${RUN}-consumer" --network none --entrypoint sleep \
        --mount "type=volume,source=$name,target=/cache,volume-nocopy" "$image" infinity)" || return 1
    printf '%s\n' "$helper" > "$CASE_DIR/.fixture-busy"
    c849_volume "$name" nuget-packages yes "$image"
}

c849_fixture_restore_container() {
    local image="$1" project="$2" packages="$3" scratch="$4" root="$5" driver="$6"
    docker run --rm --name "c849-${RUN}-restore-$project" --network antiphon-build-slots --user 1654:1654 --entrypoint /bin/sh \
        -e HOME=/home/app -e NUGET_PACKAGES=/home/app/.nuget/packages \
        -e NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch \
        -e ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots \
        --mount "type=volume,source=$packages,target=/home/app/.nuget/packages,volume-nocopy" \
        --mount "type=volume,source=$scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy" \
        --mount "type=bind,source=$root,target=/fixture" \
        --mount "type=bind,source=$CHECKOUT/scripts/build-slot.ps1,target=/scripts/build-slot.ps1,readonly" \
        --mount "type=bind,source=$CHECKOUT/scripts/lib/build-slot.ps1,target=/scripts/lib/build-slot.ps1,readonly" \
        "$image" -c 'pwsh -NoProfile -File /scripts/build-slot.ps1 -Label c849-fixture-restore -- /bin/sh "/fixture/$1" "/fixture/$2"' \
        sh "$driver" "$project"
}

c849_fixture_lock_container() {
    local image="$1" scratch="$2" root="$3" mode="$4" generation="$5"
    docker run --rm --name "c849-${RUN}-lock-$mode-$generation" --network none --user 1654:1654 --entrypoint pwsh \
        --mount "type=volume,source=$scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy" \
        --mount "type=bind,source=$root,target=/fixture" \
        "$image" -NoProfile -File /fixture/lock-probe.ps1 -Mode "$mode" -Generation "$generation"
}

c849_fixture_wait_file() {
    local path="$1" i
    for i in $(seq 1 150); do
        [ -f "$path" ] && return 0
        sleep 0.2
    done
    return 1
}

c849_fixture_lock_assert() {
    [ ! -e "$1" ] || { printf 'NuGetLockNotShared\n'; return 2; }
}

c849_fixture_nuget_race() {
    local image root packages scratch private_scratch name a b holder waiter
    image="$(c849_image)"
    root="$SERVER2_ROOT/race"
    packages="c849${RUN}race-packages"
    scratch="c849${RUN}race-scratch"
    private_scratch="c849${RUN}private-scratch"
    for name in "$packages" "$scratch" "$private_scratch"; do
        docker volume inspect "$name" >/dev/null 2>&1 && write_result false FixtureNamespaceOccupied 2
        printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
    done
    C849_PACKAGES="$packages"; C849_SCRATCH="$scratch"
    c849_volume "$packages" nuget-packages yes "$image"
    c849_volume "$scratch" nuget-scratch yes "$image"
    C849_SCRATCH="$private_scratch"
    c849_volume "$private_scratch" nuget-scratch yes "$image"
    C849_SCRATCH="$scratch"
    mkdir -p "$root/package/lib/net10.0" "$root/package/content" "$root/feed" "$root/empty" "$root/p1" "$root/p2" "$root/p3"
    printf '%s\n' '<?xml version="1.0"?><package><metadata><id>C849.Probe</id><version>1.0.0</version><authors>Antiphon</authors><description>Offline fixture</description></metadata></package>' > "$root/package/C849.Probe.nuspec"
    : > "$root/package/lib/net10.0/_._"
    printf 'C849_PACKAGE_SENTINEL\n' > "$root/package/content/probe.txt"
    cat > "$root/make-zip.ps1" <<'PS'
param([string]$Source, [string]$Target)
[System.IO.Compression.ZipFile]::CreateFromDirectory($Source, $Target)
PS
    pwsh -NoProfile -File "$root/make-zip.ps1" "$root/package" "$root/feed/C849.Probe.1.0.0.nupkg" \
        || write_result false FixturePackageZipFailed 2
    for name in p1 p2 p3; do
        printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="C849.Probe" Version="1.0.0" /></ItemGroup></Project>\n' \
            > "$root/$name/$name.csproj"
    done
    printf '<configuration><packageSources><clear/><add key="fixture" value="/fixture/feed"/></packageSources></configuration>\n' > "$root/NuGet.Config"
    printf '<configuration><packageSources><clear/><add key="empty" value="/fixture/empty"/></packageSources></configuration>\n' > "$root/Offline.Config"
    cat > "$root/driver.sh" <<'SH'
#!/bin/sh
set -eu
cd "$1"
: > ready
while [ ! -e /fixture/go ]; do sleep 0.1; done
dotnet restore "$(basename "$1").csproj" --configfile /fixture/NuGet.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 >/dev/null
SH
    cat > "$root/offline.sh" <<'SH'
#!/bin/sh
set -eu
cd "$1"
dotnet restore p3.csproj --configfile /fixture/Offline.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 >/dev/null
SH
    sudo -n chown -R 1654:1654 "$root"
    c849_fixture_restore_container "$image" p1 "$packages" "$scratch" "$root" driver.sh > "$SERVER2_ROOT/race-p1.log" 2>&1 & a=$!
    c849_fixture_restore_container "$image" p2 "$packages" "$scratch" "$root" driver.sh > "$SERVER2_ROOT/race-p2.log" 2>&1 & b=$!
    c849_fixture_wait_file "$root/p1/ready" && c849_fixture_wait_file "$root/p2/ready" \
        || write_result false FixtureRestoreBarrierFailed 2
    sudo -n touch "$root/go"
    wait "$a" || write_result false FixtureConcurrentRestoreFailed 2
    wait "$b" || write_result false FixtureConcurrentRestoreFailed 2
    c849_fixture_restore_container "$image" p3 "$packages" "$scratch" "$root" offline.sh \
        > "$SERVER2_ROOT/race-offline.log" 2>&1 || write_result false FixtureOfflineRestoreFailed 2
    docker run --rm --network none --user 1654:1654 --entrypoint /bin/sh \
        --mount "type=volume,source=$packages,target=/cache,volume-nocopy" "$image" \
        -c 'test -s /cache/c849.probe/1.0.0/.nupkg.metadata && grep -Fxq C849_PACKAGE_SENTINEL /cache/c849.probe/1.0.0/content/probe.txt' \
        || write_result false FixturePackageIncomplete 2
    cat > "$root/lock-probe.ps1" <<'PS'
param([ValidateSet('holder','waiter')][string]$Mode, [string]$Generation)
$assembly = Get-ChildItem /usr/share/dotnet/sdk/*/NuGet.Common.dll | Select-Object -Last 1
if (-not $assembly) { throw 'NuGetLockPrimitiveMissing' }
[void][System.Reflection.Assembly]::LoadFrom($assembly.FullName)
$path = '/var/cache/antiphon/nuget-scratch/' + $Generation + '.lock'
$root = '/fixture'
[NuGet.Common.ConcurrencyUtilities]::ExecuteWithFileLocked($path, [Action]{
    [System.IO.File]::WriteAllText("$root/$Generation-$Mode-entered", '1')
    if ($Mode -eq 'holder') {
        $until = [DateTime]::UtcNow.AddSeconds(30)
        while (-not [System.IO.File]::Exists("$root/$Generation-release")) {
            if ([DateTime]::UtcNow -gt $until) { throw 'FixtureLockReleaseTimeout' }
            Start-Sleep -Milliseconds 100
        }
    }
})
PS
    c849_fixture_lock_container "$image" "$scratch" "$root" holder shared > "$SERVER2_ROOT/lock-holder.log" 2>&1 & holder=$!
    c849_fixture_wait_file "$root/shared-holder-entered" || write_result false FixtureNuGetLockHolderFailed 2
    c849_fixture_lock_container "$image" "$scratch" "$root" waiter shared > "$SERVER2_ROOT/lock-waiter.log" 2>&1 & waiter=$!
    sleep 2
    c849_fixture_lock_assert "$root/shared-waiter-entered" || write_result false NuGetLockNotShared 2
    sudo -n touch "$root/shared-release"
    wait "$holder" && wait "$waiter" && [ -e "$root/shared-waiter-entered" ] \
        || write_result false FixtureNuGetLockProbeFailed 2
    c849_fixture_lock_container "$image" "$scratch" "$root" holder private > "$SERVER2_ROOT/private-holder.log" 2>&1 & holder=$!
    c849_fixture_wait_file "$root/private-holder-entered" || write_result false FixtureNuGetLockHolderFailed 2
    c849_fixture_lock_container "$image" "$private_scratch" "$root" waiter private > "$SERVER2_ROOT/private-waiter.log" 2>&1 & waiter=$!
    c849_fixture_wait_file "$root/private-waiter-entered" || write_result false ControlNotSensitive 2
    c849_fixture_control PC-17 NuGetLockNotShared c849_fixture_lock_assert "$root/private-waiter-entered"
    sudo -n touch "$root/private-release"
    wait "$holder" && wait "$waiter" || write_result false FixtureNuGetLockProbeFailed 2
    printf 'PASS F-4\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_empty_apphost() {
    local image="$1" packages="$2" scratch="$3" root="$4" output="$SERVER2_ROOT/empty-apphost.log" code=0
    docker run --rm --name "c849-${RUN}-apphost-miss" --network antiphon-build-slots \
        --user 1654:1654 --entrypoint /bin/sh -e HOME=/home/app \
        -e NUGET_PACKAGES=/home/app/.nuget/packages \
        -e NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch \
        -e ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots \
        --mount "type=volume,source=$packages,target=/home/app/.nuget/packages,volume-nocopy" \
        --mount "type=volume,source=$scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy" \
        --mount "type=bind,source=$root,target=/fixture" \
        --mount "type=bind,source=$CHECKOUT/scripts/build-slot.ps1,target=/scripts/build-slot.ps1,readonly" \
        --mount "type=bind,source=$CHECKOUT/scripts/lib/build-slot.ps1,target=/scripts/lib/build-slot.ps1,readonly" \
        "$image" -c 'pwsh -NoProfile -File /scripts/build-slot.ps1 -Label c849-apphost-miss -- /bin/sh -c "cd /fixture; dotnet restore Smoke.csproj --configfile NuGet.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1"' \
        > "$output" 2>&1 || code=$?
    [ "$code" -ne 0 ] && grep -Eq 'NU1101|NU1102' "$output" \
        && grep -Fq 'Microsoft.NETCore.App.Host.linux-x64' "$output" \
        || { printf 'ControlNotSensitive\n'; return 2; }
    printf 'AppHostCacheMiss\n'; return 2
}

c849_fixture_apphost() {
    local image donor packages scratch empty_packages empty_scratch name root helper before after
    image="$(c849_image)"
    donor="$(docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp \
        --filter label=com.docker.compose.service=session-runner)"
    [ -n "$donor" ] && [ "${donor//$'\n'/}" = "$donor" ] \
        || write_result false FixtureNet9DonorMissing 2
    root="$SERVER2_ROOT/apphost"
    mkdir -p "$root/packages/microsoft.netcore.app.host.linux-x64" "$root/packages/microsoft.netcore.app.ref"
    before="$(docker exec "$donor" sha256sum /home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost | cut -d' ' -f1)" \
        || write_result false FixtureNet9DonorMissing 2
    docker cp "$donor:/home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20" \
        "$root/packages/microsoft.netcore.app.host.linux-x64/" >/dev/null \
        || write_result false FixtureNet9CopyFailed 2
    docker cp "$donor:/home/app/.nuget/packages/microsoft.netcore.app.ref/9.0.20" \
        "$root/packages/microsoft.netcore.app.ref/" >/dev/null \
        || write_result false FixtureNet9CopyFailed 2
    after="$(docker exec "$donor" sha256sum /home/app/.nuget/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost | cut -d' ' -f1)" \
        || write_result false FixtureNet9DonorChanged 2
    [ "$before" = "$after" ] \
        && [ "$after" = "$(sha256sum "$root/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)" ] \
        && [ -s "$root/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ] \
        && [ -s "$root/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" ] \
        || write_result false FixtureNet9DonorChanged 2
    docker run --rm --network none --entrypoint /bin/sh "$image" \
        -c 'test ! -d /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/9.0.20' \
        || write_result false ControlNotSensitive 2
    packages="c849${RUN}smoke-packages"; scratch="c849${RUN}smoke-scratch"
    empty_packages="c849${RUN}empty-packages"; empty_scratch="c849${RUN}empty-scratch"
    for name in "$packages" "$scratch" "$empty_packages" "$empty_scratch"; do
        docker volume inspect "$name" >/dev/null 2>&1 && write_result false FixtureNamespaceOccupied 2
        printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
    done
    C849_PACKAGES="$packages"; C849_SCRATCH="$scratch"
    c849_volume "$packages" nuget-packages yes "$image"
    c849_volume "$scratch" nuget-scratch yes "$image"
    C849_PACKAGES="$empty_packages"; C849_SCRATCH="$empty_scratch"
    c849_volume "$empty_packages" nuget-packages yes "$image"
    c849_volume "$empty_scratch" nuget-scratch yes "$image"
    docker run --rm --network none --user 0:0 --entrypoint /bin/sh \
        --mount "type=bind,source=$root/packages,target=/seed,readonly" \
        --mount "type=volume,source=$packages,target=/cache,volume-nocopy" "$image" \
        -c 'set -eu; cp -a /seed/. /cache/; chown -R 1654:1654 /cache' \
        || write_result false FixtureNet9ImportFailed 2
    helper="c849-${RUN}-apphost"
    docker run -d --name "$helper" --network antiphon-build-slots --user 1654:1654 --entrypoint sleep \
        -e HOME=/home/app -e NUGET_PACKAGES=/home/app/.nuget/packages \
        -e NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch -e NPM_CONFIG_CACHE=/home/app/.npm \
        -e ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots \
        --mount "type=volume,source=$packages,target=/home/app/.nuget/packages,volume-nocopy" \
        --mount "type=volume,source=$scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy" \
        --mount "type=volume,source=$C849_NPM,target=/home/app/.npm/_cacache,volume-nocopy" \
        --mount "type=bind,source=$CHECKOUT/scripts/build-slot.ps1,target=/work/repos/antiphon/scripts/build-slot.ps1,readonly" \
        --mount "type=bind,source=$CHECKOUT/scripts/lib/build-slot.ps1,target=/work/repos/antiphon/scripts/lib/build-slot.ps1,readonly" \
        "$image" infinity >/dev/null || write_result false FixtureAppHostStartFailed 2
    printf '%s\n' "$helper" > "$CASE_DIR/.fixture-apphost"
    c849_smoke "$helper" fixture || write_result false FixtureAppHostSmokeFailed 2
    docker rm -f "$helper" >/dev/null || write_result false FixtureAppHostCleanupFailed 2
    rm -f "$CASE_DIR/.fixture-apphost"
    cat > "$root/Smoke.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>true</UseAppHost><RuntimeIdentifier>linux-x64</RuntimeIdentifier><RuntimeFrameworkVersion>9.0.20</RuntimeFrameworkVersion><TargetLatestRuntimePatch>false</TargetLatestRuntimePatch><SelfContained>false</SelfContained><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
EOF
    printf 'System.Console.WriteLine("CARD0849_APPHOST_OK");\n' > "$root/Program.cs"
    mkdir -p "$root/empty"
    printf '<configuration><packageSources><clear/><add key="empty" value="/fixture/empty"/></packageSources></configuration>\n' > "$root/NuGet.Config"
    sudo -n chown -R 1654:1654 "$root"
    c849_fixture_control PC-18 AppHostCacheMiss c849_fixture_empty_apphost "$image" "$empty_packages" "$empty_scratch" "$root"
    printf 'PASS F-5\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_npm_install() {
    local image="$1" root="$2" cache="$3" project="$4" output="$SERVER2_ROOT/npm-$project.log" code=0
    docker run --rm --name "c849-${RUN}-npm-$project" --network none --user 1654:1654 \
        --entrypoint /bin/sh -e HOME=/home/app -e NPM_CONFIG_CACHE=/home/app/.npm \
        --mount "type=volume,source=$cache,target=/home/app/.npm/_cacache,volume-nocopy" \
        --mount "type=bind,source=$root,target=/fixture" "$image" \
        -c 'cd "/fixture/$1"; npm ci --offline --ignore-scripts --no-audit --no-fund' sh "$project" \
        > "$output" 2>&1 || code=$?
    [ "$code" -eq 0 ] || { grep -Fq ENOTCACHED "$output" && { printf 'NpmOfflineCacheMiss\n'; return 2; }; return 1; }
    [ -s "$root/$project/node_modules/c849-cache-probe/sentinel" ] \
        && [ "$(cat "$root/$project/node_modules/c849-cache-probe/sentinel")" = C849_NPM_OFFLINE ] \
        || return 1
}

c849_fixture_npm() {
    local image root cache empty_cache network integrity name
    image="$(c849_image)"
    root="$SERVER2_ROOT/npm-offline"
    cache="c849${RUN}npmcache-npm"; empty_cache="c849${RUN}npmempty-npm"
    network="c849-${RUN}-npm"
    for name in "$cache" "$empty_cache"; do
        docker volume inspect "$name" >/dev/null 2>&1 && write_result false FixtureNamespaceOccupied 2
        printf '%s\n' "$name" >> "$CASE_DIR/.fixture-volumes"
    done
    C849_NPM="$cache"; c849_volume "$cache" npm-content yes "$image"
    C849_NPM="$empty_cache"; c849_volume "$empty_cache" npm-content yes "$image"
    C849_NPM="$cache"
    mkdir -p "$root/package" "$root/b" "$root/b-empty"
    printf '{"name":"c849-cache-probe","version":"1.0.0"}\n' > "$root/package/package.json"
    printf 'C849_NPM_OFFLINE\n' > "$root/package/sentinel"
    tar -czf "$root/pkg.tgz" -C "$root" package
    integrity="$(pwsh -NoProfile -Command '$bytes=[System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($args[0])); [Convert]::ToBase64String($bytes)' "$root/pkg.tgz")" \
        || write_result false FixtureNpmIntegrityFailed 2
    cat > "$root/server.js" <<'JS'
const http = require('http'); const fs = require('fs');
http.createServer((req, res) => {
  if (req.url !== '/pkg.tgz') { res.writeHead(404); res.end(); return; }
  res.writeHead(200, {'Content-Type': 'application/octet-stream'});
  fs.createReadStream('/fixture/pkg.tgz').pipe(res);
}).listen(8080, '0.0.0.0');
JS
    printf '{"name":"c849-consumer","version":"1.0.0","dependencies":{"c849-cache-probe":"1.0.0"}}\n' > "$root/b/package.json"
    printf '{"name":"c849-consumer","version":"1.0.0","lockfileVersion":3,"requires":true,"packages":{"":{"name":"c849-consumer","version":"1.0.0","dependencies":{"c849-cache-probe":"1.0.0"}},"node_modules/c849-cache-probe":{"version":"1.0.0","resolved":"http://c849-npm-http:8080/pkg.tgz","integrity":"sha512-%s"}}}\n' \
        "$integrity" > "$root/b/package-lock.json"
    cp "$root/b/package.json" "$root/b/package-lock.json" "$root/b-empty/"
    sudo -n chown -R 1654:1654 "$root"
    docker network create "$network" >/dev/null || write_result false FixtureNpmNetworkFailed 2
    printf '%s\n' "$network" > "$CASE_DIR/.fixture-network"
    docker run -d --name "c849-${RUN}-npm-http" --network "$network" --network-alias c849-npm-http \
        --user 1654:1654 --entrypoint node \
        --mount "type=bind,source=$root,target=/fixture,readonly" "$image" /fixture/server.js >/dev/null \
        || write_result false FixtureNpmServerFailed 2
    printf 'c849-%s-npm-http\n' "$RUN" > "$CASE_DIR/.fixture-npm-server"
    docker run --rm --network "$network" --entrypoint /bin/sh "$image" \
        -c 'for i in $(seq 1 30); do curl -fsS http://c849-npm-http:8080/pkg.tgz -o /dev/null && exit 0; sleep 0.2; done; exit 1' \
        || write_result false FixtureNpmServerNotReady 2
    docker run --rm --name "c849-${RUN}-npm-fill" --network "$network" --user 1654:1654 \
        --entrypoint npm -e HOME=/home/app -e NPM_CONFIG_CACHE=/home/app/.npm \
        --mount "type=volume,source=$cache,target=/home/app/.npm/_cacache,volume-nocopy" \
        "$image" cache add http://c849-npm-http:8080/pkg.tgz >/dev/null \
        || write_result false FixtureNpmFillFailed 2
    docker rm -f "c849-${RUN}-npm-http" >/dev/null || write_result false FixtureNpmServerCleanupFailed 2
    rm -f "$CASE_DIR/.fixture-npm-server"
    c849_fixture_npm_install "$image" "$root" "$cache" b \
        || write_result false FixtureNpmOfflineFailed 2
    c849_fixture_control PC-19 NpmOfflineCacheMiss c849_fixture_npm_install "$image" "$root" "$empty_cache" b-empty
    printf 'PASS F-6\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_nonexternal_loss() {
    local root="$SERVER2_ROOT/compose-retention" project="c849${RUN}negative" volume
    docker compose -p "$project" -f "$root/negative.yml" up -d >/dev/null || return 1
    volume="${project}_cache"
    docker compose -p "$project" -f "$root/negative.yml" down >/dev/null || return 1
    docker compose -p "$project" -f "$root/negative.yml" down -v >/dev/null || return 1
    if docker volume inspect "$volume" >/dev/null 2>&1; then return 1; fi
    printf 'ExternalCacheLost\n'; return 2
}

c849_fixture_retention() {
    local image root main temp name volume target
    image="$(c849_image)"
    root="$SERVER2_ROOT/compose-retention"
    main="c849${RUN}main"; temp="c849${RUN}temp"
    mkdir -p "$root"
    cat > "$root/shared.yml" <<EOF
services:
  probe:
    image: $image
    entrypoint: ["sleep", "infinity"]
    volumes:
      - type: volume
        source: packages
        target: /home/app/.nuget/packages
      - type: volume
        source: scratch
        target: /var/cache/antiphon/nuget-scratch
      - type: volume
        source: npm
        target: /home/app/.npm/_cacache
      - runner-tmp:/tmp
      - work:/work
      - runner-state:/state
      - dind-data:/var/lib/docker
volumes:
  packages: {external: true, name: $C849_PACKAGES}
  scratch: {external: true, name: $C849_SCRATCH}
  npm: {external: true, name: $C849_NPM}
  runner-tmp: {}
  work: {}
  runner-state: {}
  dind-data: {}
EOF
    # A separate project has its own non-external cache and no surviving
    # consumer. The negative control must observe its deletion on down -v.
    cat > "$root/negative.yml" <<EOF
services:
  probe:
    image: $image
    entrypoint: ["sleep", "infinity"]
    volumes:
      - cache:/cache
volumes:
  cache: {}
EOF
    docker compose -p "$main" -f "$root/shared.yml" up -d >/dev/null \
        || write_result false FixtureMainComposeFailed 2
    docker compose -p "$temp" -f "$root/shared.yml" up -d >/dev/null \
        || write_result false FixtureTempComposeFailed 2
    for name in "$main" "$temp"; do
        docker compose -p "$name" -f "$root/shared.yml" exec -T probe /bin/sh -c \
            'test "$(stat -c %a /tmp)" = 1777' \
            || write_result false FixtureTmpModeInvalid 2
    done
    for volume in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        case "$volume" in
            "$C849_PACKAGES") target=/home/app/.nuget/packages ;;
            "$C849_SCRATCH") target=/var/cache/antiphon/nuget-scratch ;;
            "$C849_NPM") target=/home/app/.npm/_cacache ;;
        esac
        docker compose -p "$temp" -f "$root/shared.yml" exec -T probe /bin/sh -c \
            'printf "retained\n" > "$1/retention-sentinel"' sh "$target" \
            || write_result false FixtureRetentionWriteFailed 2
    done
    docker compose -p "$temp" -f "$root/shared.yml" up -d --force-recreate >/dev/null \
        || write_result false FixtureTempRecreateFailed 2
    docker compose -p "$temp" -f "$root/shared.yml" down -v >/dev/null \
        || write_result false FixtureTempDownFailed 2
    for volume in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
        docker volume inspect "$volume" >/dev/null 2>&1 \
            || write_result false FixtureExternalCacheLost 2
    done
    docker compose -p "$main" -f "$root/shared.yml" exec -T probe /bin/sh -c \
        'test "$(cat /home/app/.nuget/packages/retention-sentinel)" = retained && test "$(cat /var/cache/antiphon/nuget-scratch/retention-sentinel)" = retained && test "$(cat /home/app/.npm/_cacache/retention-sentinel)" = retained && test "$(stat -c %a /tmp)" = 1777' \
        || write_result false FixtureRetentionReadFailed 2
    for volume in runner-tmp work runner-state dind-data; do
        docker volume inspect "${temp}_$volume" >/dev/null 2>&1 \
            && write_result false FixtureTempPrivateVolumeRetained 2
        docker volume inspect "${main}_$volume" >/dev/null 2>&1 \
            || write_result false FixtureMainPrivateVolumeLost 2
    done
    c849_fixture_control PC-20 ExternalCacheLost c849_fixture_nonexternal_loss
    docker compose -p "$main" -f "$root/shared.yml" down -v >/dev/null \
        || write_result false FixtureMainDownFailed 2
    printf 'PASS F-7\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_refill() {
    local image="$1" helper="c849-${RUN}-prune-smoke" npm_work="$SERVER2_ROOT/npm-offline"
    docker run --rm --network none --user 1654:1654 --entrypoint npm \
        -e HOME=/home/app -e NPM_CONFIG_CACHE=/home/app/.npm \
        --mount "type=bind,source=$npm_work,target=/fixture,readonly" \
        --mount "type=volume,source=$C849_NPM,target=/home/app/.npm/_cacache,volume-nocopy" \
        "$image" cache add /fixture/pkg.tgz >/dev/null \
        || write_result false CacheRefillFailed 2
    docker run -d --name "$helper" --network antiphon-build-slots --user 1654:1654 --entrypoint sleep \
        -e HOME=/home/app -e NUGET_PACKAGES=/home/app/.nuget/packages \
        -e NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch -e NPM_CONFIG_CACHE=/home/app/.npm \
        -e ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots \
        --mount "type=volume,source=$C849_PACKAGES,target=/home/app/.nuget/packages,volume-nocopy" \
        --mount "type=volume,source=$C849_SCRATCH,target=/var/cache/antiphon/nuget-scratch,volume-nocopy" \
        --mount "type=volume,source=$C849_NPM,target=/home/app/.npm/_cacache,volume-nocopy" \
        --mount "type=bind,source=$CHECKOUT/scripts/build-slot.ps1,target=/work/repos/antiphon/scripts/build-slot.ps1,readonly" \
        --mount "type=bind,source=$CHECKOUT/scripts/lib/build-slot.ps1,target=/work/repos/antiphon/scripts/lib/build-slot.ps1,readonly" \
        "$image" infinity >/dev/null || write_result false CacheRefillProbeStartFailed 2
    printf '%s\n' "$helper" > "$CASE_DIR/.fixture-prune-smoke"
    c849_smoke "$helper" fixture-prune
    docker rm -f "$helper" >/dev/null || write_result false CacheRefillProbeCleanupFailed 2
    rm -f "$CASE_DIR/.fixture-prune-smoke"
}

c849_fixture_prune_case() {
    local output="$CASE_DIR/.fixture-prune-control" code=0
    ( write_result() { printf '%s\n' "$2"; exit "$3"; }; c849_prune ) > "$output" 2>&1 || code=$?
    [ "$code" -eq 0 ] || { cat "$output"; return "$code"; }
}

c849_fixture_prune() {
    local image recovery payload image_id name path mode bytes budget source_root
    image="$(c849_image)"
    C849_PACKAGES="c849${RUN}smoke-packages"
    C849_SCRATCH="c849${RUN}smoke-scratch"
    C849_NPM="c849${RUN}npmcache-npm"
    C849_READY="$SERVER2_ROOT/cache/fixture-prune-ready"
    recovery="$SERVER2_ROOT/cache/recovery-$RUN"
    [ ! -e "$recovery" ] || write_result false CacheRecoveryExists 2
    mkdir -p "$recovery/packages" "$recovery/npm"
    cp -a "$SERVER2_ROOT/apphost/packages/." "$recovery/packages/" \
        || write_result false CacheRecoveryMissing 2
    payload="$(sha256sum "$recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)"
    image_id="$(docker image inspect -f '{{.Id}}' "$image")" \
        || write_result false CacheRecoveryImageMissing 2
    printf 'image=%s\npayload-sha256=%s\nrecovery=%s\n' "$image_id" "$payload" "$recovery" > "$C849_READY"
    printf 'runners=idle processes=0 leases=0\n' > "$SERVER2_ROOT/fixture-consumers.txt"
    C849_FIXTURE_PACKAGES_BUDGET="$(( $(c849_observe_volume "$C849_PACKAGES" nuget-packages 10737418240 | awk '{print $5}') * 5 / 4 ))"
    C849_FIXTURE_SCRATCH_BUDGET="$(( $(c849_observe_volume "$C849_SCRATCH" nuget-scratch 268435456 | awk '{print $5}') * 5 / 4 ))"
    C849_FIXTURE_NPM_BUDGET="$(( $(c849_observe_volume "$C849_NPM" npm-content 2147483648 | awk '{print $5}') * 5 / 4 ))"
    [ "$C849_FIXTURE_PACKAGES_BUDGET" -gt 0 ] && [ "$C849_FIXTURE_NPM_BUDGET" -gt 0 ] \
        || write_result false FixtureBudgetInvalid 2
    ( write_result() { printf '%s\n' "$2"; exit "$3"; }; c849_preview ) \
        || write_result false FixturePrunePreviewFailed 2
    C590_PREVIEW_RUN="$RUN"
    printf 'runners=busy processes=0 leases=0\n' > "$SERVER2_ROOT/fixture-consumers.txt"
    c849_fixture_control PC-21 CacheConsumersBusy c849_fixture_prune_case
    printf 'runners=idle processes=0 leases=0\n' > "$SERVER2_ROOT/fixture-consumers.txt"
    source_root="$SERVER2_ROOT/cache/previews/$RUN/preview.txt"
    cp "$source_root" "$SERVER2_ROOT/preview-backup.txt"
    sed -i 's/^source-sha=.*/source-sha=0000000000000000000000000000000000000000/' "$source_root"
    c849_fixture_control PC-22 CachePreviewStale c849_fixture_prune_case
    cp "$SERVER2_ROOT/preview-backup.txt" "$source_root"
    c849_fixture_control PC-23 CacheTargetInvalid c849_prune_validate_tree '' ''
    cp "$SERVER2_ROOT/cache/previews/$RUN/volumes.txt" "$SERVER2_ROOT/volumes-backup.txt"
    C849_FIXTURE_PACKAGES_BUDGET=1
    awk '$2=="nuget-packages" {$6=1} {print}' "$SERVER2_ROOT/volumes-backup.txt" \
        > "$SERVER2_ROOT/cache/previews/$RUN/volumes.txt"
    sed -i "s/^volume-sha256=.*/volume-sha256=$(sha256sum "$SERVER2_ROOT/cache/previews/$RUN/volumes.txt" | cut -d' ' -f1)/" "$source_root"
    c849_fixture_control PC-24 CacheBudgetExceeded c849_fixture_prune_case
    cp "$SERVER2_ROOT/volumes-backup.txt" "$SERVER2_ROOT/cache/previews/$RUN/volumes.txt"
    cp "$SERVER2_ROOT/preview-backup.txt" "$source_root"
    C849_FIXTURE_PACKAGES_BUDGET="$(awk '$2=="nuget-packages" {print $6}' "$SERVER2_ROOT/volumes-backup.txt")"
    mv "$recovery" "$recovery.missing"
    c849_fixture_control PC-25 CacheRecoveryMissing c849_fixture_prune_case
    mv "$recovery.missing" "$recovery"
    ( write_result() { printf '%s\n' "$2"; exit "$3"; }; c849_prune ) \
        || write_result false FixturePruneApplyFailed 2
    [ -s "$CASE_DIR/prune.txt" ] && [ -s "$CASE_DIR/smoke-summary.txt" ] \
        || write_result false FixturePruneReceiptMissing 2
    printf 'PASS F-8\n' >> "$CASE_DIR/fixture-groups.txt"
}

c849_fixture_receipt() {
    local line key value source="$1" output="$2"
    : > "$output"
    while IFS= read -r line; do
        key="${line%%=*}"; value="${line#*=}"
        case "$key" in
            source-sha) [[ "$value" =~ ^[0-9a-f]{40}$ ]] || { printf 'EvidenceNotAllowListed\n'; return 2; } ;;
            image-id) [[ "$value" =~ ^sha256:[0-9a-f]{64}$ ]] || { printf 'EvidenceNotAllowListed\n'; return 2; } ;;
            inventories|groups|controls|expected-red|production-mutations)
                [[ "$value" =~ ^[0-9]+$ ]] || { printf 'EvidenceNotAllowListed\n'; return 2; } ;;
            *) printf 'EvidenceNotAllowListed\n'; return 2 ;;
        esac
        printf '%s=%s\n' "$key" "$value" >> "$output"
    done < "$source"
}

c849_fixture_evidence() {
    local image_id id groups controls receipt="$CASE_DIR/fixture-summary.txt" input="$SERVER2_ROOT/fixture-summary-input.txt"
    ( TEMP_PROJECT=antiphon-runner-temp
      write_result() { printf '%s\n' "$2"; exit "$3"; }
      case_runner_cache_inventory ) >/dev/null \
        || write_result false FixtureInventoryFailed 2
    [ -s "$CASE_DIR/server2-status.json" ] && [ -s "$CASE_DIR/server2-temp-status.json" ] \
        || write_result false FixtureInventoriesMissing 2
    for id in $(seq -w 1 25); do
        grep -q "^CONTROL PC-$id " "$CASE_DIR/fixture-controls.txt" \
            || write_result false "FixtureControlMissing PC-$id" 2
    done
    [ "$(sort -u "$CASE_DIR/fixture-controls.txt" | wc -l)" -eq 25 ] \
        && [ "$(wc -l < "$CASE_DIR/fixture-controls.txt")" -eq 25 ] \
        && [ "$(wc -l < "$CASE_DIR/fixture-groups.txt")" -eq 8 ] \
        || write_result false FixtureRosterIncomplete 2
    printf 'credential=TOKEN_SENTINEL_C849_CONTENT\n' > "$SERVER2_ROOT/fixture-toxic-observation.txt"
    c849_fixture_control PC-26 EvidenceNotAllowListed c849_fixture_receipt \
        "$SERVER2_ROOT/fixture-toxic-observation.txt" "$SERVER2_ROOT/fixture-toxic-output.txt"
    [ ! -s "$SERVER2_ROOT/fixture-toxic-output.txt" ] \
        && ! grep -Rq 'TOKEN_SENTINEL_C849_CONTENT' "$CASE_DIR" \
        || write_result false EvidenceNotAllowListed 2
    image_id="$(docker image inspect -f '{{.Id}}' "$(c849_image)")" \
        || write_result false FixtureImageMissing 2
    groups=9; controls=26
    printf 'source-sha=%s\nimage-id=%s\ninventories=2\ngroups=%s\ncontrols=%s\nexpected-red=%s\nproduction-mutations=0\n' \
        "$SHA" "$image_id" "$groups" "$controls" "$controls" > "$input"
    c849_fixture_receipt "$input" "$receipt" || write_result false EvidenceNotAllowListed 2
    printf 'PASS F-9\n' >> "$CASE_DIR/fixture-groups.txt"
    [ "$(wc -l < "$CASE_DIR/fixture-groups.txt")" -eq 9 ] \
        && [ "$(wc -l < "$CASE_DIR/fixture-controls.txt")" -eq 26 ] \
        || write_result false FixtureRosterIncomplete 2
    cat "$CASE_DIR/fixture-groups.txt" "$CASE_DIR/fixture-controls.txt"
    printf 'C849_FIXTURE groups=9 controls=26 expectedRed=26 inventories=2 failures=0 productionMutations=0\n'
}

c849_fixture() {
    require_lane host
    c849_lock
    # Keep every stage, marker, preview and recovery below this case's private
    # evidence tree; only the shared maintenance lock belongs to the live root.
    SERVER2_ROOT="/tmp/c849-fixture-$RUN"
    [ ! -e "$SERVER2_ROOT" ] || write_result false FixtureNamespaceOccupied 2
    mkdir -m 0700 -p "$SERVER2_ROOT/cache"
    : > "$CASE_DIR/fixture-controls.txt"
    : > "$CASE_DIR/fixture-control-variants.txt"
    : > "$CASE_DIR/fixture-groups.txt"
    trap 'if [ -f "$SERVER2_ROOT/compose-retention/shared.yml" ]; then docker compose -p "c849${RUN}temp" -f "$SERVER2_ROOT/compose-retention/shared.yml" down -v >/dev/null 2>&1 || true; docker compose -p "c849${RUN}main" -f "$SERVER2_ROOT/compose-retention/shared.yml" down -v >/dev/null 2>&1 || true; fi; if [ -f "$SERVER2_ROOT/compose-retention/negative.yml" ]; then docker compose -p "c849${RUN}negative" -f "$SERVER2_ROOT/compose-retention/negative.yml" down -v >/dev/null 2>&1 || true; fi; if [ -s "$CASE_DIR/.fixture-prune-smoke" ]; then docker rm -f "$(cat "$CASE_DIR/.fixture-prune-smoke")" >/dev/null 2>&1 || true; fi; if [ -s "$CASE_DIR/.fixture-npm-server" ]; then docker rm -f "$(cat "$CASE_DIR/.fixture-npm-server")" >/dev/null 2>&1 || true; fi; if [ -s "$CASE_DIR/.fixture-apphost" ]; then docker rm -f "$(cat "$CASE_DIR/.fixture-apphost")" >/dev/null 2>&1 || true; fi; if [ -s "$CASE_DIR/.fixture-busy" ]; then docker rm -f "$(cat "$CASE_DIR/.fixture-busy")" >/dev/null 2>&1 || true; fi; if [ -s "$CASE_DIR/.fixture-donor" ]; then docker rm -f "$(cat "$CASE_DIR/.fixture-donor")" >/dev/null 2>&1 || true; fi; c849_fixture_cleanup; if [ -s "$CASE_DIR/.fixture-network" ]; then docker network rm "$(cat "$CASE_DIR/.fixture-network")" >/dev/null 2>&1 || true; fi; case "$SERVER2_ROOT" in /tmp/c849-fixture-*) rm -rf -- "$SERVER2_ROOT" ;; esac' EXIT
    c849_fixture_compose
    c849_fixture_prepare
    c849_fixture_seed
    c849_fixture_nuget_race
    c849_fixture_apphost
    c849_fixture_npm
    c849_fixture_retention
    c849_fixture_prune
    c849_fixture_evidence
    write_result true '' 0
}

case_verify_runner_caches() {
    require_lane host
    case "${C590_RUNNER_ID:-}" in server2|server2-temp) ;; *) write_result false CacheRunnerInvalid 2 ;; esac
    c849_prepare no
    c849_require_ready
    local status
    status="$(c849_status_body "$C590_RUNNER_ID")" || write_result false CacheRunnerStatusUnavailable 2
    printf '%s' "$status" | jq -e --arg sha "$SHA" '.buildVersion == $sha and .dispatchEligible == true' >/dev/null \
        || write_result false CacheRunnerVersionMismatch 2
    if [ "${C590_EXPECT_ACCEPTING:-0}" = 1 ]; then
        printf '%s' "$status" | jq -e '.acceptingNewWork == true and .draining == false' >/dev/null \
            || write_result false CacheRunnerNotAccepting 2
    fi
    printf '%s' "$status" | jq -c '{buildVersion,dispatchEligible,acceptingNewWork,draining,sessions,runnerSessions,queuedTasks}' \
        > "$CASE_DIR/status.json" || write_result false CacheRunnerStatusInvalid 2
    sed -n 's/^payload-sha256=//p' "$C849_READY" > "$CASE_DIR/seed-hash.txt"
    local project container
    project="$HOST_PROJECT"
    [ "$C590_RUNNER_ID" = server2-temp ] && project="$TEMP_PROJECT"
    container="$(docker ps -q --filter "label=com.docker.compose.project=$project" \
        --filter 'label=com.docker.compose.service=session-runner')"
    [ -n "$container" ] || write_result false CacheRunnerUnavailable 2
    c849_assert_mounts "$container"
    c849_smoke "$container" "$C590_RUNNER_ID"
    write_result true '' 0
}

case_runner_cache_inventory() {
    require_lane host
    command -v jq >/dev/null || write_result false CacheInventoryToolMissing 2
    local runner project container image status
    for runner in server2 server2-temp; do
        project="$HOST_PROJECT"
        [ "$runner" = server2-temp ] && project="$TEMP_PROJECT"
        container="$(docker ps -aq --filter "label=com.docker.compose.project=$project" \
            --filter 'label=com.docker.compose.service=session-runner')"
        [ -n "$container" ] || write_result false CacheInventoryRunnerMissing 2
        image="$(docker inspect -f '{{.Image}}' "$container")"
        printf 'runner=%s container=%s image=%s\n' "$runner" "$container" "$image" \
            >> "$CASE_DIR/identities.txt"
        docker inspect -f '{{range .Mounts}}{{if eq .Type "volume"}}{{println .Type .Name .Destination .RW}}{{end}}{{end}}' \
            "$container" > "$CASE_DIR/$runner-mounts.txt" \
            || write_result false CacheInventoryMountsUnavailable 2
        status="$(curl -fsS --max-time 15 "${C604_SERVER_ORIGIN:?}/api/session-runners/$runner/status")" \
            || write_result false CacheInventoryStatusUnavailable 2
        printf '%s' "$status" | jq -c '{sessions,runnerSessions,queuedTasks,draining,retiredAt,dispatchEligible,acceptingNewWork,buildVersion}' \
            > "$CASE_DIR/$runner-status.json" || write_result false CacheInventoryStatusInvalid 2
        if [ "$(docker inspect -f '{{.State.Running}}' "$container")" = true ]; then
            docker exec -u 1654:1654 -e HOME=/home/app "$container" /bin/sh -c '
                dotnet nuget locals all --list
                npm config get cache
                for p in /home/app/.nuget/packages /home/app/.npm/_cacache /home/app/.local/share/NuGet/http-cache /tmp/NuGetScratchapp; do
                    if [ -d "$p" ]; then du -s -B1 "$p"; else printf "absent %s\n" "$p"; fi
                done
                stat -c "%u:%g %a %n" /tmp /home/app/.nuget/packages
            ' > "$CASE_DIR/$runner-cache-paths.txt" \
                || write_result false CacheInventoryPathsUnavailable 2
        else
            printf 'stopped\n' > "$CASE_DIR/$runner-cache-paths.txt"
        fi
    done
    write_result true '' 0
}

case_verify_runner_caches_retired() {
    require_lane host
    c849_prepare no
    c849_require_ready
    local rollback_image
    rollback_image="$(sed -n 's/^image=//p' "$C849_READY" | head -n 1)"
    [[ "$rollback_image" =~ ^sha256:[0-9a-f]{64}$ ]] \
        && docker image inspect "$rollback_image" >/dev/null 2>&1 \
        || write_result false CacheRollbackImageMissing 2
    printf 'rollback-image=%s\n' "$rollback_image" > "$CASE_DIR/rollback.txt"
    local status
    status="$(c849_status_body server2)" || write_result false MainRunnerStatusUnavailable 2
    printf '%s' "$status" | jq -e --arg sha "$SHA" '.buildVersion == $sha and .dispatchEligible == true and .acceptingNewWork == true and .draining == false' >/dev/null \
        || write_result false MainRunnerNotAccepting 2
    printf '%s' "$status" | jq -c '{buildVersion,dispatchEligible,acceptingNewWork,draining,sessions,runnerSessions,queuedTasks}' \
        > "$CASE_DIR/status.json" || write_result false MainRunnerStatusInvalid 2
    sed -n 's/^payload-sha256=//p' "$C849_READY" > "$CASE_DIR/seed-hash.txt"
    if [ -n "$(docker ps -aq --filter "label=com.docker.compose.project=$TEMP_PROJECT")" ]; then
        write_result false TempContainersRemain 2
    fi
    local volume
    for volume in runner-tmp work runner-state dind-data; do
        if docker volume inspect "${TEMP_PROJECT}_$volume" >/dev/null 2>&1; then
            write_result false TempPrivateVolumeRemains 2
        fi
    done
    docker volume inspect "${HOST_PROJECT}_runner-tmp" >/dev/null 2>&1 \
        || write_result false MainTmpMissing 2
    local container tmp_mode
    container="$(docker ps -q --filter "label=com.docker.compose.project=$HOST_PROJECT" \
        --filter 'label=com.docker.compose.service=session-runner')"
    [ -n "$container" ] || write_result false MainRunnerUnavailable 2
    c849_assert_mounts "$container"
    tmp_mode="$(docker exec "$container" stat -c %a /tmp)" || write_result false MainTmpUnavailable 2
    [ "$tmp_mode" = 1777 ] || write_result false MainTmpModeInvalid 2
    c849_smoke "$container" server2
    write_result true '' 0
}

case_deploy_parent() {
    require_lane host
    ensure_checkout
    ensure_runner_boot_files

    retire_c590_leftovers

    local pinned_broker_sha12
    pinned_broker_sha12="$(broker_sha12)"

    cat > "$SERVER2_ENV" <<EOF
COMPOSE_PROJECT_NAME=$HOST_PROJECT
SOURCE_REVISION=$SHA
SOURCE_SHA12=${SHA:0:12}
PHONE_HOME_SERVER_ORIGIN=${C604_SERVER_ORIGIN:?}
ANTIPHON_DEPLOY_KEY_FILE=$DEPLOY_KEY
PHONE_HOME_SECRET_FILE=$PHONE_HOME_SECRET
CLAUDE_OAUTH_TOKEN_FILE=$CLAUDE_OAUTH_TOKEN_PATH
RUNNER_GIT_IDENTITY_FILE=$GIT_IDENTITY_PATH
RUNNER_CODEX_HOME_DIR=$CODEX_HOME_PATH
RUNNER_GIT_USER_NAME=$RUNNER_GIT_USER_NAME
RUNNER_GIT_USER_EMAIL=$RUNNER_GIT_USER_EMAIL
BUILD_SLOTS_SHA12=$pinned_broker_sha12
EOF

    build_server2_images
    c849_prepare yes
    c849_require_ready
    c849_budget_gate
    ensure_build_slots_broker

    # CARD-0631 D-10: a fresh work volume gets its checkout before the runner exists.
    seed_runner_checkout

    compose_host up -d --no-build >> "$CASE_DIR/command.log" 2>&1 || {
        compose_host logs --no-color --tail 120 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false HostComposeFailed 2
    }

    local container i
    for i in $(seq 1 60); do
        container="$(runner_container)"
        [ -n "$container" ] && break
        sleep 5
    done
    if [ -z "$container" ]; then
        write_result false RunnerNotStarted 2
    fi
    printf '%s\n' "$container" > "$CASE_DIR/container.txt"

    for i in $(seq 1 60); do
        if docker exec "$container" curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1; then
            break
        fi
        sleep 5
    done
    if ! docker exec "$container" curl -fsS http://127.0.0.1:8080/health > "$CASE_DIR/health.txt" 2>&1; then
        compose_host logs --no-color --tail 200 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false RunnerUnhealthy 2
    fi

    # --- D-2: healthy is not the same as registered -------------------------------------------
    # /health and `docker info` both answer yes on a runner that has never once phoned home. The
    # standing runner sat "healthy" through 304 consecutive UnauthorizedAccessException reconnects
    # because nothing here looked at the one thing that was broken. Two probes, both refusals.

    # (a) the secret the runner is configured to read must be readable AS uid 1654. A compose
    # secret mount arrives owned by the host uid at 0600, which the app uid can never open.
    docker exec "$container" printenv PhoneHome__SecretPath > "$CASE_DIR/phone-home-secret-path.txt" 2>/dev/null || true
    phone_home_secret_path="$(tr -d '[:space:]' < "$CASE_DIR/phone-home-secret-path.txt")"
    if [ -z "$phone_home_secret_path" ]; then
        write_result false PhoneHomeSecretPathUnset 2
    fi
    # One byte to /dev/null: enough to prove open(2) succeeds, and nothing is ever captured.
    if ! docker exec -u 1654:1654 "$container" head -c 1 "$phone_home_secret_path" > /dev/null 2>> "$CASE_DIR/command.log"; then
        printf 'false\n' > "$CASE_DIR/phone-home-readable.txt"
        compose_host logs --no-color --tail 200 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false PhoneHomeSecretUnreadable 2
    fi
    printf 'true\n' > "$CASE_DIR/phone-home-readable.txt"

    # (b) whether the production server is even accepting registrations yet. CP-6a is what turns
    # phone-home on there, and it runs AFTER this case, so a runner that cannot register only
    # because the server still answers `phone_home_disabled` is a recorded pending state, not this
    # deployment's fault. Asked with NO secret: the reply separates "disabled" (409 with that code)
    # from "enabled, and this caller is not authenticated" (403), and changes nothing either way.
    curl -sS -o "$CASE_DIR/register-probe.json" -w '%{http_code}' -X POST \
        "${C604_SERVER_ORIGIN:?}/api/session-runners/register" \
        -H 'Content-Type: application/json' \
        -d '{"protocolVersion":1,"runnerId":"probe","processBootId":"00000000-0000-0000-0000-000000000001","runnerStoreId":"00000000-0000-0000-0000-000000000002","platform":"linux","capacity":1}' \
        > "$CASE_DIR/register-probe-code.txt" 2>> "$CASE_DIR/command.log" || true
    if grep -q 'phone_home_disabled' "$CASE_DIR/register-probe.json" 2>/dev/null; then
        printf 'disabled\n' > "$CASE_DIR/phone-home-server-state.txt"
    else
        printf 'enabled\n' > "$CASE_DIR/phone-home-server-state.txt"
    fi

    # (c) the connection loop itself. The window opens AFTER the compose start_period, so one cold
    # reconnect is not a verdict; the backoff is capped at 5s, so a loop that can only fail leaves several
    # marks in 45 seconds while a registered runner leaves none.
    phone_home_since="$(date -u +%Y-%m-%dT%H:%M:%S)"
    sleep 45
    docker logs --since "$phone_home_since" "$container" > "$CASE_DIR/phone-home-window.log" 2>&1 || true
    scrub_file "$CASE_DIR/phone-home-window.log" || true
    phone_home_failures="$(grep -cE 'Phone-home registration failed|Phone-home connection ended; reconnecting|UnauthorizedAccessException|PhoneHomeSecretUnreadable' \
        "$CASE_DIR/phone-home-window.log" || true)"
    printf '%s\n' "${phone_home_failures:-0}" > "$CASE_DIR/phone-home-failures.txt"
    # A server that has not been switched on yet is the ONLY excused cause. A permission fault, a
    # crash loop, a rejected secret or any other conflict still refuses -- which is the whole point
    # of this probe, since /health and `docker info` say yes to all of them.
    if [ "${phone_home_failures:-0}" -ge 2 ] \
        && [ "$(tr -d '[:space:]' < "$CASE_DIR/phone-home-server-state.txt")" != "disabled" ]; then
        write_result false PhoneHomeUnreachable 2
    fi

    # --- CARD-0631: the identity uid 1654 commits with, and the checkout it mirrors from ------
    # Both refuse before anything is retired or accepted.
    verify_runner_git_identity "$container" /
    verify_runner_checkout "$container"
    c849_assert_mounts "$container"
    c849_smoke "$container" server2

    # The new deployment is proven: this round's own superseded build products go now (D-5).
    retire_superseded_server2_images "${SHA:0:12}"
    docker images --format '{{.Repository}}:{{.Tag}}\t{{.ID}}\t{{.Size}}' > "$CASE_DIR/inventory-images-after.txt"

    # V-11: persistent, privileged, its own nested daemon, no host socket, no server, no Postgres.
    docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$container" > "$CASE_DIR/restart-policy.txt"
    if [ "$(tr -d '[:space:]' < "$CASE_DIR/restart-policy.txt")" != "unless-stopped" ]; then
        write_result false NotPersistent 2
    fi
    docker inspect -f '{{.HostConfig.Privileged}}' "$container" > "$CASE_DIR/privileged.txt"
    if [ "$(tr -d '[:space:]' < "$CASE_DIR/privileged.txt")" != "true" ]; then
        write_result false NotPrivileged 2
    fi
    docker inspect -f '{{range .Mounts}}{{println .Source .Destination}}{{end}}' "$container" > "$CASE_DIR/mounts.txt"
    if grep -q 'docker\.sock' "$CASE_DIR/mounts.txt"; then
        write_result false HostSocketMounted 2
    fi
    docker exec "$container" docker info --format '{{.Name}}' > "$CASE_DIR/daemon-name.txt" 2>&1 \
        || write_result false NestedDaemonUnavailable 2
    docker exec "$container" hostname > "$CASE_DIR/runner-hostname.txt"
    if [ "$(tr -d '[:space:]' < "$CASE_DIR/daemon-name.txt")" != "$(tr -d '[:space:]' < "$CASE_DIR/runner-hostname.txt")" ]; then
        write_result false SiblingDaemonRefused 2
    fi
    # `ps --format '{{.Service}}'` is not supported by Compose 2.18 on server2 and answers with a
    # parse error, which no grep matches - so the assertion passed vacuously. `--services` is the
    # portable form, and an empty file is a refusal rather than a pass.
    compose_host ps --services > "$CASE_DIR/services.txt" 2>&1 || true
    if [ ! -s "$CASE_DIR/services.txt" ] || grep -q 'could not be parsed' "$CASE_DIR/services.txt"; then
        write_result false ServiceListUnavailable 2
    fi
    if grep -qE '^(antiphon|postgres)$' "$CASE_DIR/services.txt"; then
        write_result false UnexpectedStandingService 2
    fi

    # V-12: the foreign neighbours on the shared host daemon are untouched.
    docker ps --format '{{.Names}}' > "$CASE_DIR/foreign-after.txt"

    # Dispatch eligibility is recorded, not asserted: CP-6a is what turns production on.
    curl -fsS "${C604_SERVER_ORIGIN:?}/api/session-runners/server2/status" > "$CASE_DIR/status.json" 2>&1 || true
    write_result true '' 0
}

case_deploy_temp_runner() {
    require_lane host
    ensure_checkout
    ensure_runner_boot_files
    if [ ! -s "$SERVER2_ENV" ]; then write_result false ParentStackMissing 2; fi

    local mount free_kb grok_dir container parent i phone_home_secret_path phone_home_since phone_home_failures
    mount="$(docker volume inspect -f '{{.Mountpoint}}' antiphon-runner_runner-state 2>> "$CASE_DIR/command.log")" \
        || write_result false ParentRunnerStateMissing 2
    grok_dir="$mount/grok"
    if ! sudo -n test -d "$grok_dir"; then write_result false GrokStoreMissing 2; fi
    free_kb="$(sudo -n df -Pk "$mount" 2>> "$CASE_DIR/command.log" | awk 'NR==2 {print $4}')" \
        || write_result false NestedStoreDiskUnavailable 2
    if [[ ! "$free_kb" =~ ^[0-9]+$ ]] || [ "$free_kb" -lt 20971520 ]; then
        write_result false NestedStoreDiskLow 2
    fi
    RUNNER_GROK_STORE_DIR="$grok_dir"
    build_server2_images
    c849_prepare yes
    c849_require_ready
    c849_budget_gate
    ensure_build_slots_broker

    cat > "$SERVER2_TEMP_ENV" <<EOF
COMPOSE_PROJECT_NAME=$TEMP_PROJECT
SOURCE_REVISION=$SHA
SOURCE_SHA12=${SHA:0:12}
PHONE_HOME_SERVER_ORIGIN=${C604_SERVER_ORIGIN:?}
ANTIPHON_DEPLOY_KEY_FILE=$DEPLOY_KEY
PHONE_HOME_SECRET_FILE=$PHONE_HOME_SECRET
CLAUDE_OAUTH_TOKEN_FILE=$CLAUDE_OAUTH_TOKEN_PATH
RUNNER_GIT_IDENTITY_FILE=$GIT_IDENTITY_PATH
RUNNER_CODEX_HOME_DIR=$CODEX_HOME_PATH
RUNNER_GROK_STORE_DIR=$RUNNER_GROK_STORE_DIR
BUILD_SLOTS_SHA12=$(broker_sha12)
EOF
    seed_runner_checkout "$TEMP_PROJECT"
    compose_temp up -d --no-build >> "$CASE_DIR/command.log" 2>&1 || {
        compose_temp logs --no-color --tail 120 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false TempComposeFailed 2
    }
    container="$(compose_temp ps -q session-runner)"
    if [ -z "$container" ]; then write_result false TempRunnerNotStarted 2; fi
    printf '%s\n' "$container" > "$CASE_DIR/temp-container.txt"
    for i in $(seq 1 60); do
        if docker inspect -f '{{.State.Health.Status}}' "$container" 2>/dev/null | grep -qx healthy; then break; fi
        sleep 5
    done
    if ! docker inspect -f '{{.State.Health.Status}}' "$container" 2>/dev/null | grep -qx healthy; then
        compose_temp logs --no-color --tail 200 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false TempRunnerUnhealthy 2
    fi
    docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$container" > "$CASE_DIR/restart-policy.txt"
    if [ "$(tr -d '[:space:]' < "$CASE_DIR/restart-policy.txt")" != no ]; then
        write_result false TempRunnerRestartPolicy 2
    fi
    docker inspect -f '{{.HostConfig.Privileged}}' "$container" > "$CASE_DIR/privileged.txt"
    if [ "$(tr -d '[:space:]' < "$CASE_DIR/privileged.txt")" != true ]; then
        write_result false NotPrivileged 2
    fi
    docker inspect -f '{{range .Mounts}}{{println .Source .Destination}}{{end}}' "$container" > "$CASE_DIR/mounts.txt"
    if grep -q 'docker\.sock' "$CASE_DIR/mounts.txt"; then
        write_result false HostSocketMounted 2
    fi
    compose_temp ps --services > "$CASE_DIR/services.txt" 2>&1 || true
    if [ ! -s "$CASE_DIR/services.txt" ] || grep -qE '^(antiphon|postgres)$' "$CASE_DIR/services.txt"; then
        write_result false UnexpectedStandingService 2
    fi
    docker exec "$container" curl -fsS http://127.0.0.1:8080/health > "$CASE_DIR/health.txt" 2>&1 \
        || write_result false TempRunnerUnhealthy 2
    docker exec "$container" printenv PhoneHome__SecretPath > "$CASE_DIR/phone-home-secret-path.txt" 2>/dev/null || true
    phone_home_secret_path="$(tr -d '[:space:]' < "$CASE_DIR/phone-home-secret-path.txt")"
    if [ -z "$phone_home_secret_path" ]; then write_result false PhoneHomeSecretPathUnset 2; fi
    if ! docker exec -u 1654:1654 "$container" head -c 1 "$phone_home_secret_path" >/dev/null 2>> "$CASE_DIR/command.log"; then
        write_result false PhoneHomeSecretUnreadable 2
    fi
    printf 'true\n' > "$CASE_DIR/phone-home-readable.txt"

    curl -sS -o "$CASE_DIR/register-probe.json" -w '%{http_code}' -X POST \
        "${C604_SERVER_ORIGIN:?}/api/session-runners/register" -H 'Content-Type: application/json' \
        -d '{"protocolVersion":1,"runnerId":"probe","processBootId":"00000000-0000-0000-0000-000000000001","runnerStoreId":"00000000-0000-0000-0000-000000000002","platform":"linux","capacity":1}' \
        > "$CASE_DIR/register-probe-code.txt" 2>> "$CASE_DIR/command.log" || true
    if grep -q 'phone_home_disabled' "$CASE_DIR/register-probe.json" 2>/dev/null; then
        printf 'disabled\n' > "$CASE_DIR/phone-home-server-state.txt"
    else
        printf 'enabled\n' > "$CASE_DIR/phone-home-server-state.txt"
    fi
    phone_home_since="$(date -u +%Y-%m-%dT%H:%M:%S)"
    sleep 45
    docker logs --since "$phone_home_since" "$container" > "$CASE_DIR/phone-home-window.log" 2>&1 || true
    scrub_file "$CASE_DIR/phone-home-window.log"
    phone_home_failures="$(grep -cE 'Phone-home registration failed|Phone-home connection ended; reconnecting|UnauthorizedAccessException|PhoneHomeSecretUnreadable' "$CASE_DIR/phone-home-window.log" || true)"
    printf '%s\n' "${phone_home_failures:-0}" > "$CASE_DIR/phone-home-failures.txt"
    if [ "${phone_home_failures:-0}" -ge 2 ] \
        && [ "$(tr -d '[:space:]' < "$CASE_DIR/phone-home-server-state.txt")" != disabled ]; then
        write_result false PhoneHomeUnreachable 2
    fi
    verify_runner_git_identity "$container" /
    verify_runner_checkout "$container"
    c849_assert_mounts "$container"
    c849_smoke "$container" server2-temp
    docker exec "$container" docker info --format '{{.Name}}' > "$CASE_DIR/daemon-name.txt" 2>&1 \
        || write_result false NestedDaemonUnavailable 2
    docker exec "$container" hostname > "$CASE_DIR/runner-hostname.txt"
    if [ "$(cat "$CASE_DIR/daemon-name.txt")" != "$(cat "$CASE_DIR/runner-hostname.txt")" ]; then
        write_result false SiblingDaemonRefused 2
    fi
    docker exec "$container" docker run --rm busybox true >> "$CASE_DIR/command.log" 2>&1 \
        || write_result false NestedDockerRunFailed 2
    parent="$(compose_host ps -q session-runner)"
    if [ -z "$parent" ]; then write_result false ParentRunnerNotStarted 2; fi
    { printf 'server2='; docker exec "$parent" cat /proc/sys/net/bridge/bridge-nf-call-iptables;
      printf 'server2-temp='; docker exec "$container" cat /proc/sys/net/bridge/bridge-nf-call-iptables; } \
        > "$CASE_DIR/bridge-nf.txt" 2>> "$CASE_DIR/command.log" || write_result false BridgeNfProbeFailed 2
    write_result true '' 0
}

case_retire_temp_runner() {
    require_lane host
    if [ -z "${C590_TEMP_RETIRED_AT:-}" ]; then write_result false TempRunnerNotRetired 2; fi
    c849_status_zero server2-temp || write_result false TempRunnerNotIdle 2
    local live_retired_at
    live_retired_at="$(c849_status_body server2-temp \
        | jq -r '.retiredAt // empty')" || write_result false TempRunnerStatusUnavailable 2
    [ -n "$live_retired_at" ] || write_result false TempRunnerNotRetired 2
    [ "$(date -u -d "$live_retired_at" +%s 2>/dev/null)" = \
      "$(date -u -d "$C590_TEMP_RETIRED_AT" +%s 2>/dev/null)" ] \
        || write_result false TempRunnerRetirementChanged 2
    c849_prepare no
    c849_require_ready
    c849_budget_gate
    if [ ! -s "$SERVER2_TEMP_ENV" ]; then write_result false TempStackMissing 2; fi
    RUNNER_GROK_STORE_DIR="$(sed -n 's/^RUNNER_GROK_STORE_DIR=//p' "$SERVER2_TEMP_ENV" | head -n 1)"
    compose_temp down -v >> "$CASE_DIR/command.log" 2>&1 || write_result false TempComposeDownFailed 2
    local image
    image="$(c849_image)"
    c849_volume "$C849_PACKAGES" nuget-packages no "$image" || write_result false CacheVolumeMissing 2
    c849_volume "$C849_SCRATCH" nuget-scratch no "$image" || write_result false CacheVolumeMissing 2
    c849_volume "$C849_NPM" npm-content no "$image" || write_result false CacheVolumeMissing 2
    printf 'down retiredAt=%s\n' "$C590_TEMP_RETIRED_AT" > "$CASE_DIR/temp-down.txt"
    write_result true '' 0
}

case_custody_containment() {
    # CARD-0604 V-28 (cgroup v1 half). The SAME four measurements the Docker Desktop harness runs
    # on cgroup v2 (CP-14), executed inside the persistent runner on server2, which is kernel 4.15
    # and therefore the freezer+SIGKILL path. Both versions must pass before the backend is
    # trusted: the two code paths in the helpers are genuinely different, so measuring one says
    # nothing about the other.
    #
    # Run as uid 1654 through `docker exec -u 1654`, which is exactly how a pty-host reaches the
    # helpers. Running it as root would measure a mechanism nobody uses.
    require_lane host
    local container
    container="$(runner_container)"
    if [ -z "$container" ]; then
        write_result false RunnerNotRunning 2
    fi

    # The runner must advertise the Linux backend before anything below is worth measuring, and
    # must never advertise the Windows one.
    docker exec -u 1654 "$container" curl -fsS http://127.0.0.1:8080/capabilities \
        > "$CASE_DIR/capabilities.json" 2>&1 || write_result false CapabilitiesUnavailable 2
    if ! grep -q 'linux-cgroup-v1' "$CASE_DIR/capabilities.json"; then
        write_result false CustodyNotAdvertised 2
    fi
    if grep -q 'windows-job-v1' "$CASE_DIR/capabilities.json"; then
        write_result false WindowsBackendAdvertisedOnLinux 2
    fi

    # Root-owned helpers, 0440 sudoers, and a grant of exactly the two commands.
    docker exec -u 1654 "$container" sh -c \
        'stat -c %U:%G:%a /usr/local/bin/antiphon-custody-enter /usr/local/bin/antiphon-custody-kill /etc/sudoers.d/antiphon-custody; sudo -n -l' \
        > "$CASE_DIR/custody-grant.txt" 2>&1 || write_result false CustodyGrantUnavailable 2
    if ! grep -q 'root:root:755' "$CASE_DIR/custody-grant.txt" || ! grep -q 'root:root:440' "$CASE_DIR/custody-grant.txt"; then
        write_result false CustodyHelpersNotRootOwned 2
    fi

    docker exec -u 1654 "$container" /usr/local/bin/antiphon-custody-containment-probe \
        > "$CASE_DIR/containment.txt" 2>&1 || write_result false ContainmentFailed 2
    if ! grep -q 'containment=ok' "$CASE_DIR/containment.txt"; then
        write_result false ContainmentFailed 2
    fi
    if ! grep -q 'cgroup_version=v1' "$CASE_DIR/containment.txt"; then
        # server2 is kernel 4.15. A v2 reading here means this case measured the wrong half and
        # the v1 freezer path is still unmeasured.
        write_result false ExpectedCgroupV1 2
    fi

    # Nothing is left behind: a surviving cgroup fails the next execution's "exists and is empty"
    # precondition for a reason nobody would be able to see.
    docker exec -u 1654 "$container" sh -c \
        'find /sys/fs/cgroup/pids/antiphon-custody -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l; find /sys/fs/cgroup/freezer/antiphon-custody -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l' \
        > "$CASE_DIR/custody-residue.txt" 2>&1 || true
    if grep -qvE '^0$' "$CASE_DIR/custody-residue.txt"; then
        write_result false CustodyResidue 2
    fi
    write_result true '' 0
}

case_nested_residue() {
    # V-20: after a run, nothing of c604<run> survives on the nested daemon, and nothing of it
    # ever appeared on the HOST daemon. Host lane, because only it can see the host daemon.
    require_lane host
    local container
    container="$(runner_container)"
    if [ -z "$container" ]; then
        write_result false RunnerNotRunning 2
    fi
    docker ps -a --format '{{.Names}}' | grep -E "c604${RUN}|c604-${RUN}" > "$CASE_DIR/host-residue.txt" || true
    docker volume ls --format '{{.Name}}' | grep -E "c604${RUN}" >> "$CASE_DIR/host-residue.txt" || true
    docker network ls --format '{{.Name}}' | grep -E "c604${RUN}" >> "$CASE_DIR/host-residue.txt" || true
    if [ -s "$CASE_DIR/host-residue.txt" ]; then
        write_result false HostResidue 2
    fi
    docker exec "$container" sh -c "docker ps -a --format '{{.Names}}'; docker volume ls --format '{{.Name}}'; docker network ls --format '{{.Name}}'" \
        > "$CASE_DIR/nested-all.txt" 2>&1 || write_result false NestedDaemonUnavailable 2
    grep -E "c604${RUN}" "$CASE_DIR/nested-all.txt" > "$CASE_DIR/nested-residue.txt" || true
    if [ -s "$CASE_DIR/nested-residue.txt" ]; then
        write_result false NestedResidue 2
    fi
    docker exec "$container" curl -fsS http://127.0.0.1:8080/health > "$CASE_DIR/health.txt" \
        || write_result false RunnerUnhealthy 2
    write_result true '' 0
}

case_persistent_restart() {
    # V-21: stop then up keeps the container name, brings the nested daemon back with its images,
    # and re-registers with the SAME runnerStoreId. A changed or lost store id is a refusal: it
    # would break every open verification execution bound to it.
    require_lane host
    local container before_store after_store before_images
    container="$(runner_container)"
    if [ -z "$container" ]; then
        write_result false RunnerNotRunning 2
    fi
    printf '%s\n' "$container" > "$CASE_DIR/container-before.txt"
    before_store="$(docker exec "$container" cat /state/runner-store-id 2>/dev/null | tr -d '[:space:]' || true)"
    if [ -z "$before_store" ]; then
        write_result false LostNestedStore 2
    fi
    printf '%s\n' "$before_store" > "$CASE_DIR/store-id-before.txt"
    before_images="$(docker exec "$container" docker images -q | sort | tr -d '[:space:]')"

    # CARD-0631: the runner now requires the identity mount. A runner deployed before that mount
    # existed would stop here and then fail both the start and the recovery below, so the file is
    # ensured (created when missing, refused when unusable) while the old runner is still up.
    ensure_runner_git_identity
    ensure_runner_codex_home

    compose_host stop >> "$CASE_DIR/command.log" 2>&1 || write_result false StopFailed 2
    if ! compose_host up -d --no-build >> "$CASE_DIR/command.log" 2>&1; then
        # A red row must never be what takes the standing runner down: this case deliberately
        # stopped it, so it owns bringing it back before it refuses.
        compose_host up -d --no-build >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false RestartFailed 2
    fi

    local i after_container
    for i in $(seq 1 60); do
        after_container="$(runner_container)"
        if [ -n "$after_container" ] && docker exec "$after_container" curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1; then
            break
        fi
        sleep 5
    done
    if [ -z "$after_container" ]; then
        write_result false RunnerNotStarted 2
    fi
    printf '%s\n' "$after_container" > "$CASE_DIR/container-after.txt"
    if [ "$after_container" != "$container" ]; then
        write_result false ContainerNameChanged 2
    fi
    if ! docker exec "$after_container" curl -fsS http://127.0.0.1:8080/health > "$CASE_DIR/health.txt" 2>&1; then
        write_result false RunnerUnhealthy 2
    fi
    if ! docker exec "$after_container" docker info >/dev/null 2>&1; then
        write_result false NestedDaemonUnavailable 2
    fi
    if [ "$(docker exec "$after_container" docker images -q | sort | tr -d '[:space:]')" != "$before_images" ]; then
        write_result false NestedImagesLost 2
    fi
    after_store="$(docker exec "$after_container" cat /state/runner-store-id 2>/dev/null | tr -d '[:space:]' || true)"
    printf '%s\n' "$after_store" > "$CASE_DIR/store-id-after.txt"
    if [ -z "$after_store" ]; then
        write_result false LostNestedStore 2
    fi
    if [ "$after_store" != "$before_store" ]; then
        write_result false ChangedStoreId 2
    fi
    curl -fsS "${C604_SERVER_ORIGIN:?}/api/session-runners/server2/status" > "$CASE_DIR/status.json" 2>&1 || true
    write_result true '' 0
}

case_receipt() {
    local which="$1"
    # Cut names reach the fixture and record whether serve stays up. A process that
    # prints serve and exits did not arm a cut. Stock names are refused the same way
    # until the receipt controller posts through the real queue; they still publish
    # the fixture serve log rather than RealCasePending.
    build_image delivery-fixture "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" delivery-fixture || write_result false FixtureBuildFailed 2
    set +e
    docker run --rm --entrypoint dotnet "$(tag delivery-fixture)" /fixture/Antiphon.DockerStack.Fixture.dll serve \
        > "$CASE_DIR/serve.txt" 2>&1
    local code=$?
    set -e
    printf 'serve-exit=%s\n' "$code" >> "$CASE_DIR/serve.txt"
    if [ "$which" != "stock-idle" ] && [ "$which" != "stock-busy" ]; then
        write_result false FixtureServeExited 2
    fi
    write_result false ReceiptFlowNotArmed 2
}

case_throwaway() {
    require_lane nested
    if [ -f /work/test-evidence/current.env ]; then
        # shellcheck disable=SC1091
        . /work/test-evidence/current.env
    fi
    local item code
    # CARD-0604 S3/D-13: the nested roster. Every item is a nested-lane case; the host-lane
    # cases (deploy, residue, restart, handoff) are never reachable from inside a session.
    local -a items=(
        child-server-image-payload
        child-runner-image-payload
        deployment-state
        client-lint
        client-tests
        messaging-tests
        dotnet-filter
        git-credential-smoke
        session-result-export-and-child-cleanup
    )
    for item in "${items[@]}"; do
        echo "THROW $item"
        C590_CASE="$item" C590_REEXEC=1 C590_SHA="$SHA" C590_RUN="$RUN" C590_CHECKOUT="$CHECKOUT" \
        C604_BRANCH="$BRANCH" \
            bash "$CHECKOUT/scripts/c590-remote.sh" || code=$?
        echo "THROW $item exit ${code:-0}"
        if [ "${code:-0}" -ne 0 ]; then
            write_result false "ThrowawayFailed $item" 2
        fi
        code=0
    done
    write_result true '' 0
}

trap 'ec=$?; if [ "$WROTE" != 1 ] && [ "$ec" != 0 ]; then write_result false "UnhandledExit $ec" "$ec"; fi' EXIT

detect_lane > /dev/null
case "$CASE" in
    runner-cache-inventory|runner-cache-fixture|runner-cache-seed|runner-cache-reset|verify-runner-caches|verify-runner-caches-retired|runner-cache-prune-preview|runner-cache-prune)
        # The cache lane is host-only and must never invoke ensure_dirs: it recursively chowns
        # /work and the server2 root, which may contain live runner state and recovery data.
        if [ "$LANE" != host ]; then printf 'DIAGNOSIS=WrongLane\n'; exit 2; fi
        if [[ ! "$RUN" =~ ^[a-z0-9]{1,64}$ ]] || [[ ! "$SHA" =~ ^[0-9a-f]{40}$ ]]; then
            printf 'DIAGNOSIS=CacheManifestInvalid\n'; exit 2
        fi
        c849_evidence_dir
        ;;
    *) ensure_dirs ;;
esac
printf '%s\n' "$LANE" > "$CASE_DIR/lane.txt"
if [ "${C590_REEXEC:-}" != "1" ] && [[ "$CASE" != runner-cache-* ]] \
    && [[ "$CASE" != verify-runner-caches* ]]; then
    ensure_checkout
    export C590_REEXEC=1
    exec bash "$CHECKOUT/scripts/c590-remote.sh"
fi

case "$CASE" in
    baseline-build-failure) case_baseline ;;
    server-image-payload) case_server_payload ;;
    runner-image-payload) case_runner_payload ;;
    testing-runner-payload) case_testing_payload ;;
    child-server-image-payload) case_child_server ;;
    child-runner-image-payload) case_child_runner ;;
    test-image-context-and-tools) case_test_image ;;
    receipt-runner-image-payload) case_receipt_runner ;;
    fixture-image-payload) case_fixture_image ;;
    deployment-state) case_deployment_state ;;
    client-lint) case_client_lint_body ;;
    client-tests) case_client_tests_body ;;
    messaging-tests) case_messaging ;;
    dotnet-filter) case_dotnet ;;
    session-denied-socket) case_denied_socket ;;
    session-result-export-and-child-cleanup) case_cleanup ;;
    interrupted-export-cleanup) case_interrupted ;;
    runtime-context-engine) context_probe "$CHECKOUT/.dockerignore" runtime-probe server/Program.cs ;;
    test-context-engine) context_probe "$CHECKOUT/docker/tests/Dockerfile.dockerignore" test-probe tests/Shared/TestClassificationMetadata.cs ;;
    server2-independent-handoff) case_handoff ;;
    deploy-parent) case_deploy_parent ;;
    deploy-temp-runner) case_deploy_temp_runner ;;
    retire-temp-runner) case_retire_temp_runner ;;
    runner-cache-seed) c849_seed ;;
    runner-cache-reset) c849_reset ;;
    runner-cache-inventory) case_runner_cache_inventory ;;
    runner-cache-prune-preview) c849_preview ;;
    runner-cache-prune) c849_prune ;;
    runner-cache-fixture) c849_fixture ;;
    verify-runner-caches) case_verify_runner_caches ;;
    verify-runner-caches-retired) case_verify_runner_caches_retired ;;
    custody-containment) case_custody_containment ;;
    nested-residue) case_nested_residue ;;
    persistent-restart) case_persistent_restart ;;
    git-credential-smoke) case_git_smoke ;;
    throwaway-all) case_throwaway ;;
    stock-idle|stock-busy|insert-refused|insert-committed-idle|insert-committed-busy|attempt-committed|body-before-enter|recipient-before-ingestion|transcript-save-fails-release|transcript-save-fails-restart|receipt-before-verdict|response-before-client|receipt-before-manifest|changed-generation|failure-summary)
        case_receipt "$CASE" ;;
    *)
        write_result false "RealCasePending $CASE" 2 ;;
esac
