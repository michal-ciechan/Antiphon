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
        sudo -n find "$SERVER2_ROOT" -path "$CODEX_HOME_PATH" -prune -o -exec chown -h mc:mc {} +
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
SERVER2_ENV="$SERVER2_ROOT/secrets/stack.env"
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
    SOURCE_REVISION="$SHA" \
    COMPOSE_PROJECT_NAME="$HOST_PROJECT" \
    docker compose -p "$HOST_PROJECT" -f "$SERVER2_COMPOSE" "$@"
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
    local keep="$1" image
    for image in $(docker images --format '{{.Repository}}:{{.Tag}}' \
        | grep -E '^antiphon-server2/(server|session-testing):' || true); do
        if [ "$image" = "antiphon-server2/server:$keep" ] \
            || [ "$image" = "antiphon-server2/session-testing:$keep" ]; then
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
    compose_host run --rm --no-deps -T state-init >> "$CASE_DIR/command.log" 2>&1 \
        || write_result false StateInitFailed 2
    compose_host run --rm --no-deps -T --user 1654:1654 -e GIT_TERMINAL_PROMPT=0 \
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

case_deploy_parent() {
    require_lane host
    ensure_checkout

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
    # CARD-0628 D-1: the token file must EXIST before compose, or the bind mount would create a
    # directory in its place. Absent from the vault is not a failure: the file stays empty, the
    # deploy warns and the runner reports claudeAuth=logged-out. Only its presence is recorded.
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
        printf 'WARN ClaudeOAuthTokenAbsent: the runner will report claudeAuth=logged-out\n' \
            | tee -a "$CASE_DIR/command.log" >&2
    fi
    # CARD-0631: before compose binds it, and before stack.env is rewritten below.
    ensure_runner_git_identity
    # CARD-0660: before state-init runs (checkout seed, below) or the runner binds it.
    ensure_runner_codex_home

    retire_c590_leftovers

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
EOF

    docker build -f "$CHECKOUT/docker/session-runner-grok/Dockerfile" --target session-testing \
        --build-arg "SOURCE_REVISION=$SHA" \
        -t "antiphon-server2/session-testing:${SHA:0:12}" "$CHECKOUT" >> "$CASE_DIR/build.log" 2>&1 \
        || write_result false RunnerBuildFailed 2
    docker build -f "$CHECKOUT/Dockerfile" --build-arg "SOURCE_REVISION=$SHA" \
        -t "antiphon-server2/server:${SHA:0:12}" "$CHECKOUT" >> "$CASE_DIR/build.log" 2>&1 \
        || write_result false StateInitBuildFailed 2

    # CARD-0631 D-10: a fresh work volume gets its checkout before the runner exists.
    seed_runner_checkout

    compose_host up -d --no-build --remove-orphans >> "$CASE_DIR/command.log" 2>&1 || {
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
    # reconnect is not a verdict; the backoff is 15s, so a loop that can only fail leaves several
    # marks in 45 seconds while a registered runner leaves none.
    phone_home_since="$(date -u +%Y-%m-%dT%H:%M:%S)"
    sleep 45
    docker logs --since "$phone_home_since" "$container" > "$CASE_DIR/phone-home-window.log" 2>&1 || true
    scrub_file "$CASE_DIR/phone-home-window.log" || true
    phone_home_failures="$(grep -cE 'Phone-home connection ended; reconnecting|UnauthorizedAccessException|PhoneHomeSecretUnreadable' \
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
ensure_dirs
printf '%s\n' "$LANE" > "$CASE_DIR/lane.txt"
if [ "${C590_REEXEC:-}" != "1" ]; then
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
