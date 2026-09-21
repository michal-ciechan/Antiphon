#!/bin/bash
# CARD-0590 live cases on server2. ASCII only. No secrets in stdout.
set -euo pipefail

CASE="${C590_CASE:?}"
SHA="${C590_SHA:?}"
RUN="${C590_RUN:?}"
CHECKOUT="${C590_CHECKOUT:-/work/repos/antiphon}"
EVIDENCE_ROOT="/work/test-evidence/${RUN}"
CASE_DIR="${EVIDENCE_ROOT}/${CASE}"
ROOT="/home/mc/antiphon-c590"
BASELINE_SHA="723ac9534fc3fce49b378e287da08b7b11095716"
PROJECT="c590${RUN}"
WROTE=0

tag() { printf 'antiphon-c590-%s-%s' "$RUN" "$1"; }

scrub_file() {
    [ -f "$1" ] || return 0
    sed -i -E 's/gho_[A-Za-z0-9_]+/gho_REDACTED/g; s/POSTGRES_PASSWORD=[^[:space:]]+/POSTGRES_PASSWORD=REDACTED/g' "$1" || true
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
    C590_ACCEPTED="$accepted" C590_DIAGNOSIS="$diagnosis" C590_EXIT_CODE="$code" C590_RESULT_PATH="$CASE_DIR/c590-result.json" \
        python3 - <<'PY'
import json, os
obj = {
    "accepted": os.environ["C590_ACCEPTED"] == "true",
    "diagnosis": os.environ["C590_DIAGNOSIS"],
    "exit": int(os.environ["C590_EXIT_CODE"]),
}
with open(os.environ["C590_RESULT_PATH"], "w", encoding="ascii") as handle:
    json.dump(obj, handle)
PY
    printf 'DIAGNOSIS=%s\n' "$diagnosis"
    exit "$code"
}

ensure_dirs() {
    sudo -n mkdir -p "$CASE_DIR" /work/repos "$ROOT/secrets" "$ROOT/baseline"
    sudo -n chown -R mc:mc /work "$ROOT"
    mkdir -p "$CASE_DIR"
    cat > /work/test-evidence/current.env <<EOF
C590_SHA=$SHA
C590_RUN=$RUN
C590_REEXEC=1
C590_CHECKOUT=$CHECKOUT
EOF
}

ensure_checkout() {
    if [ ! -d "$CHECKOUT/.git" ]; then
        git clone --filter=blob:none --branch feat/card-task-dae3ad6b https://github.com/michal-ciechan/Antiphon.git "$CHECKOUT"
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
DOCKER_SOCKET_GID=129
EOF
        unset password
    fi
    python3 - <<PY
from pathlib import Path
path = Path("$ROOT/secrets/stack.env")
lines = []
seen = False
for line in path.read_text(encoding="ascii").splitlines():
    if line.startswith("SOURCE_REVISION="):
        lines.append("SOURCE_REVISION=$SHA")
        seen = True
    else:
        lines.append(line)
if not seen:
    lines.append("SOURCE_REVISION=$SHA")
path.write_text("\\n".join(lines) + "\\n", encoding="ascii")
PY
}

write_runtime_override() {
    local server runner
    server="$(tag server)"
    runner="$(tag session-testing)"
    cat > "$ROOT/compose.runtime.yml" <<EOF
services:
  state-init:
    image: ${server}
  antiphon:
    image: ${server}
    environment:
      GIT_CONFIG_GLOBAL: /work/gitconfig
    healthcheck:
      start_period: 90s
      interval: 5s
      timeout: 5s
      retries: 40
  session-runner:
    image: ${runner}
    environment:
      GIT_CONFIG_GLOBAL: /work/gitconfig
EOF
}

compose_parent() {
    COMPOSE_PROJECT_NAME="$PROJECT" docker compose \
        --env-file "$ROOT/secrets/stack.env" \
        -f "$CHECKOUT/docker-compose.yml" \
        -f "$CHECKOUT/docker-compose.session-testing.yml" \
        -f "$ROOT/compose.runtime.yml" \
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
    local vol="${PROJECT}_work"
    local marker
    marker="$(docker run --rm --user 0 --entrypoint /bin/sh -v "${vol}:/work" "$(tag server)" -c 'cat /work/repos/antiphon/.c590-sha 2>/dev/null || true')"
    if [ "$marker" = "$SHA" ]; then
        return 0
    fi
    docker run --rm --user 0 --entrypoint /bin/sh \
        -v "$CHECKOUT:/from:ro" \
        -v "${vol}:/work" \
        "$(tag server)" -c "
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

ensure_parent() {
    ensure_secrets
    build_image server "$CHECKOUT/Dockerfile" "$CHECKOUT" || write_result false ServerBuildFailed 2
    build_image session-testing "$CHECKOUT/docker/session-runner-grok/Dockerfile" "$CHECKOUT" session-testing || write_result false SessionTestingBuildFailed 2
    write_runtime_override
    compose_parent up -d --no-build >> "$CASE_DIR/command.log" 2>&1 || {
        compose_parent logs --no-color --tail 80 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false ParentComposeFailed 2
    }
    if ! wait_url http://127.0.0.1:5000/health; then
        compose_parent logs --no-color --tail 120 >> "$CASE_DIR/command.log" 2>&1 || true
        write_result false ParentUnhealthy 2
    fi
    copy_checkout_into_volume
}

version_sha() {
    curl -fsS http://127.0.0.1:${1:-5000}/api/version | python3 -c 'import json,sys; print(json.load(sys.stdin)["version"])'
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
    run_sh "$image" '
        test -x /usr/local/bin/docker
        test -x /usr/local/lib/docker/cli-plugins/docker-compose
        test -x /usr/local/bin/pwsh
        test -x /opt/antiphon-tests/fakegrok/fakegrok
        test -x /opt/antiphon-tests/fakegrok-linux.sh
        ! grep -a -q VerificationCustodyV1 /app/Antiphon.SessionRunner.dll
    ' >> "$CASE_DIR/command.log" 2>&1 || write_result false TestingPayloadMissing 2
    docker run --rm --user 1654:1654 --network none --entrypoint /bin/sh "$image" -c 'id -u' > "$CASE_DIR/uid.txt"
    if [ "$(tr -d "[:space:]" < "$CASE_DIR/uid.txt")" != "1654" ]; then
        write_result false TestingNotNonRoot 2
    fi
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
    ensure_parent
    local live
    live="$(version_sha 5000)"
    printf '%s\n' "$live" > "$CASE_DIR/version.txt"
    if [ "$live" != "$SHA" ]; then
        write_result false RevisionMismatch 2
    fi
    curl -fsS http://127.0.0.1:5000/ | head -c 400 > "$CASE_DIR/index-head.txt" || write_result false UiMissing 2
    if ! grep -q -E 'html|script|Antiphon' "$CASE_DIR/index-head.txt"; then
        write_result false UiMissing 2
    fi
    COMPOSE_PROJECT_NAME="$PROJECT" docker compose --env-file "$ROOT/secrets/stack.env" \
        -f "$CHECKOUT/docker-compose.yml" -f "$ROOT/compose.runtime.yml" \
        exec -T postgres psql -U antiphon -d antiphon -c 'CREATE TABLE IF NOT EXISTS c590_marker(id int primary key); INSERT INTO c590_marker VALUES (1) ON CONFLICT DO NOTHING;' \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false MarkerInsertFailed 2
    docker run --rm --user 0 --entrypoint /bin/sh -v "${PROJECT}_work:/work" "$(tag server)" -c 'echo marker > /work/c590-workspace-marker && chown 1654:1654 /work/c590-workspace-marker'
    compose_parent stop >> "$CASE_DIR/command.log" 2>&1
    compose_parent up -d --no-build >> "$CASE_DIR/command.log" 2>&1
    if ! wait_url http://127.0.0.1:5000/health; then
        write_result false RestartUnhealthy 2
    fi
    local marker_count workspace
    marker_count="$(COMPOSE_PROJECT_NAME="$PROJECT" docker compose --env-file "$ROOT/secrets/stack.env" -f "$CHECKOUT/docker-compose.yml" -f "$ROOT/compose.runtime.yml" exec -T postgres psql -U antiphon -d antiphon -tAc 'SELECT count(*) FROM c590_marker;')"
    workspace="$(docker run --rm --user 0 --entrypoint /bin/sh -v "${PROJECT}_work:/work" "$(tag server)" -c 'cat /work/c590-workspace-marker')"
    if [ "$(echo "$marker_count" | tr -d '[:space:]')" != "1" ] || [ "$workspace" != "marker" ]; then
        write_result false RetentionFailed 2
    fi
    docker volume inspect "${PROJECT}_pgdata" >/dev/null
    write_result true '' 0
}

case_client_lint() {
    case_test_image
    # case_test_image exits on success. Reached only if write_result did not exit.
}

client_volume() {
    docker volume create "c590${RUN}_nodemodules" >/dev/null
}

run_client() {
    local script="$1"
    client_volume
    docker run --rm --name "c590-${RUN}-client" \
        -v "c590${RUN}_nodemodules:/src/client/node_modules" \
        -v "$CASE_DIR:/evidence" \
        -w /src \
        "$(tag test-runner)" \
        bash -lc "$script" > "$CASE_DIR/command.log" 2>&1
}

case_client_lint_body() {
    build_image test-runner "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" test-runner || write_result false TestImageBuildFailed 2
    if ! run_client 'npm --prefix client ci && npm --prefix client run build && npm --prefix client run lint'; then
        write_result false ClientLintFailed 2
    fi
    write_result true '' 0
}

case_client_tests_body() {
    build_image test-runner "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" test-runner || write_result false TestImageBuildFailed 2
    if ! run_client 'npm --prefix client ci && pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath /evidence/client.json'; then
        write_result false ClientTestsFailed 2
    fi
    if [ ! -s "$CASE_DIR/client.json" ]; then
        write_result false ClientJsonMissing 2
    fi
    if ! python3 - <<PY
import json
doc = json.load(open("$CASE_DIR/client.json", encoding="utf-8"))
num = doc.get("numTotalTests") or doc.get("success")
failed = doc.get("numFailedTests", 0)
print(num, failed)
if not num or int(num) < 102 or int(failed) != 0:
    raise SystemExit(1)
PY
    then
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
    local volbin volobj
    volbin="c590${RUN}_$(printf '%s' "$project" | tr '/.' '__')_bin"
    volobj="c590${RUN}_$(printf '%s' "$project" | tr '/.' '__')_obj"
    docker volume create "$volbin" >/dev/null
    docker volume create "$volobj" >/dev/null
    docker run --rm --name "c590-${RUN}-${name}" \
        --group-add 129 \
        --add-host=host.docker.internal:host-gateway \
        -v /var/run/docker.sock:/var/run/docker.sock \
        -v "${volbin}:/src/${project}/bin-c590" \
        -v "${volobj}:/src/${project}/obj" \
        -v "$CASE_DIR:/evidence" \
        -e SessionRunner__BaseUrl=http://127.0.0.1:1 \
        -e SessionRunner__Enabled=false \
        -e MSBUILDDISABLENODEREUSE=1 \
        -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        -e NUGET_PACKAGES=/src/.nuget \
        -e ANTIPHON_BROKER_TESTS="$broker_env" \
        -w /src \
        "$(tag test-runner)" \
        pwsh -NoProfile -File scripts/run-checkpoint.ps1 \
            -Name "$name" \
            -Project "$project" \
            -OutputPath bin-c590/ \
            -Filter "$filter" \
            -ResultsRoot /evidence/trx \
            -MinExecuted "$min" \
            "${build_args[@]}" \
        > "$CASE_DIR/command.log" 2>&1
}

case_dotnet() {
    local filter=""
    if [ -n "${C590_FILTER_FILE:-}" ] && [ -f "$C590_FILTER_FILE" ]; then
        filter="$(tr -d '\r' < "$C590_FILTER_FILE")"
    fi
    if [ -z "$filter" ]; then
        write_result false FilterMissing 2
    fi
    build_image test-runner "$CHECKOUT/docker/tests/Dockerfile" "$CHECKOUT" test-runner || write_result false TestImageBuildFailed 2
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

case_parent_session() {
    ensure_parent
    local live
    live="$(version_sha 5000)"
    if [ "$live" != "$SHA" ]; then
        write_result false RevisionMismatch 2
    fi
    python3 - <<PY
import json, time, urllib.request, urllib.error
base = "http://127.0.0.1:5000"
out = r"$CASE_DIR"

def call(method, path, body=None, timeout=120):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8", "replace")
            parsed = json.loads(raw) if raw else None
            return resp.status, parsed, raw
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", "replace")
        return exc.code, None, raw

status, project, raw = call("POST", "/api/projects", {
    "name": "c590-$RUN",
    "gitRepositoryUrl": "https://github.com/michal-ciechan/Antiphon",
    "constitutionPath": None,
    "gitHubIntegrationEnabled": False,
    "notificationsEnabled": False,
    "localRepositoryPath": "/work/repos/antiphon",
    "baseBranch": "feat/card-task-dae3ad6b",
})
open(out + "/project.json", "w", encoding="utf-8").write(raw)
if status >= 300 or not isinstance(project, dict) or "id" not in project:
    raise SystemExit("project %s" % status)
status, board, raw = call("POST", "/api/boards", {
    "projectId": project["id"],
    "name": "c590",
})
open(out + "/board.json", "w", encoding="utf-8").write(raw)
if status >= 300 or not isinstance(board, dict) or "id" not in board:
    raise SystemExit("board %s" % status)
status, card, raw = call("POST", "/api/boards/%s/cards" % board["id"], {
    "boardColumnId": None,
    "title": "c590 raw challenge",
})
open(out + "/card.json", "w", encoding="utf-8").write(raw)
if status >= 300 or not isinstance(card, dict) or "id" not in card:
    raise SystemExit("card %s" % status)
status, started, raw = call("POST", "/api/sessions", {
    "cardId": card["id"],
    "definitionName": "raw-sh",
    "agentKind": "Raw",
    "prompt": "echo C590_RAW_OK",
    "cols": 120,
    "rows": 30,
})
open(out + "/session.json", "w", encoding="utf-8").write(raw)
if status >= 300 or not isinstance(started, dict) or "sessionId" not in started:
    raise SystemExit("session %s" % status)
session_id = started["sessionId"]
call("POST", "/api/sessions/%s/input" % session_id, {"input": "echo C590_RAW_OK\\r"})
seen = ""
for _ in range(30):
    time.sleep(2)
    status, transcript, raw = call("GET", "/api/sessions/%s/transcript?since=0" % session_id)
    open(out + "/transcript.json", "w", encoding="utf-8").write(raw)
    status, _buffer, raw_buffer = call("GET", "/api/sessions/%s/buffer" % session_id)
    open(out + "/buffer.json", "w", encoding="utf-8").write(raw_buffer)
    seen = raw + "\\n" + raw_buffer
    if "C590_RAW_OK" in seen:
        open(out + "/session-id.txt", "w", encoding="ascii").write(session_id)
        raise SystemExit(0)
raise SystemExit("marker missing")
PY
    local py=$?
    if [ "$py" -ne 0 ]; then
        write_result false RawSessionFailed 2
    fi
    write_result true '' 0
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
    ensure_parent
    local name="c590-${RUN}-child"
    docker run -d --name "$name" --label "c590-run=$RUN" --label "c590-owner=child" alpine:3.20 sleep 600 \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false ChildCreateFailed 2
    local id
    id="$(docker inspect -f '{{.Id}}' "$name")"
    printf '%s\n' "$id" > "$CASE_DIR/child-id.txt"
    docker rm -f "$name" >> "$CASE_DIR/command.log" 2>&1
    if docker inspect "$id" >/dev/null 2>&1; then
        write_result false ChildSurvived 2
    fi
    if ! curl -fsS http://127.0.0.1:5000/health >/dev/null; then
        write_result false ParentLost 2
    fi
    write_result true '' 0
}

case_interrupted() {
    local name="c590-${RUN}-residue"
    docker run -d --name "$name" --label "c590-run=$RUN" --label "c590-owner=child" alpine:3.20 sleep 600 \
        >> "$CASE_DIR/command.log" 2>&1 || write_result false ResidueCreateFailed 2
    local id
    id="$(docker inspect -f '{{.Id}}' "$name")"
    printf '%s\n' "$id" > "$CASE_DIR/owned-id.txt"
    printf 'incomplete\n' > "$CASE_DIR/export-state.txt"
    if ! docker inspect "$id" >/dev/null 2>&1; then
        write_result false ResidueMissing 2
    fi
    local label
    label="$(docker inspect -f '{{index .Config.Labels "c590-owner"}}' "$id")"
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
    if ! python3 - <<PY > "$CASE_DIR/context-check.txt"
import os, sys
root = "$CASE_DIR/context-copy"
inner = os.path.join(root, "context")
required = "$required"
if not os.path.isfile(os.path.join(root, required)) and os.path.isdir(inner):
    root = inner
missing = []
if not os.path.isfile(os.path.join(root, required)):
    missing.append("missing " + required)
banned = [
    ".git",
    ".antiphon/case.json",
    "client/.env.local",
    "scratch/auth.json",
    ".grok/config.toml",
    "scratch/key.pfx",
    "scratch/key.pem",
    "server/bin/x.dll",
    "server/bin-pc/x.dll",
    "server/obj/x",
    "client/node_modules/x",
    "workspace/owned.txt",
    "logs/test.log",
    "nested-git/.git/HEAD",
    "git-pointer/.git",
]
for rel in banned:
    if os.path.lexists(os.path.join(root, rel)):
        missing.append("present " + rel)
if missing:
    print("\n".join(missing))
    sys.exit(1)
print("context-ok " + root)
PY
    then
        docker image rm "$image" >/dev/null 2>&1 || true
        write_result false ContextSentinelLeak 2
    fi
    rm -rf "$CASE_DIR/context" "$CASE_DIR/context-copy"
    docker image rm "$image" >/dev/null 2>&1 || true
    write_result true '' 0
}

case_git_smoke() {
    if [ ! -s "$ROOT/secrets/gh-token" ]; then
        write_result false GitHubTokenMissing 2
    fi
    build_image server "$CHECKOUT/Dockerfile" "$CHECKOUT" || write_result false ServerBuildFailed 2
    cat > "$ROOT/askpass" <<'EOF'
#!/bin/sh
case "$1" in
  *Username*) printf '%s\n' 'x-access-token' ;;
  *) cat /run/secrets/gh-token ;;
esac
EOF
    chmod 700 "$ROOT/askpass"
    local branch="throwaway/c590-credential-smoke-$RUN"
    if ! docker run --rm --user 0 --entrypoint /bin/sh \
        -v "$ROOT/secrets/gh-token:/run/secrets/gh-token:ro" \
        -v "$ROOT/askpass:/askpass:ro" \
        -e GIT_ASKPASS=/askpass \
        -e GIT_TERMINAL_PROMPT=0 \
        "$(tag server)" -c "
            set -eu
            git clone --depth 1 --branch feat/card-task-dae3ad6b https://github.com/michal-ciechan/Antiphon.git /tmp/smoke
            cd /tmp/smoke
            git checkout -b '$branch'
            printf 'c590 credential smoke $RUN\n' > c590-credential-smoke.txt
            git add c590-credential-smoke.txt
            git -c user.name=c590-smoke -c user.email=c590-smoke@localhost commit -m 'test(CARD-0590): credential smoke $RUN'
            git push origin 'HEAD:$branch'
            git rev-parse HEAD
        " > "$CASE_DIR/push.txt" 2> "$CASE_DIR/command.log"; then
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
    write_result true '' 0
}

case_handoff() {
    if ! curl -fsS http://127.0.0.1:5000/health > "$CASE_DIR/health.txt"; then
        write_result false HandoffUnhealthy 2
    fi
    local live
    live="$(version_sha 5000)"
    printf '%s\n' "$live" > "$CASE_DIR/version.txt"
    if [ "$live" != "$SHA" ]; then
        write_result false RevisionMismatch 2
    fi
    compose_parent ps > "$CASE_DIR/ps.txt" 2>&1 || write_result false HandoffPsFailed 2
    find "$EVIDENCE_ROOT" -maxdepth 2 -type d > "$CASE_DIR/evidence-index.txt"
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
    if [ -f /work/test-evidence/current.env ]; then
        # shellcheck disable=SC1091
        . /work/test-evidence/current.env
    fi
    local item code
    local -a items=(
        child-server-image-payload
        child-runner-image-payload
        test-image-context-and-tools
        receipt-runner-image-payload
        fixture-image-payload
        deployment-state
        client-lint
        client-tests
        runtime-context-engine
        test-context-engine
        session-denied-socket
        session-result-export-and-child-cleanup
        interrupted-export-cleanup
        server2-independent-handoff
    )
    for item in "${items[@]}"; do
        echo "THROW $item"
        C590_CASE="$item" C590_REEXEC=1 C590_SHA="$SHA" C590_RUN="$RUN" C590_CHECKOUT="$CHECKOUT" \
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

ensure_dirs
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
    parent-native-and-command-session) case_parent_session ;;
    session-denied-socket) case_denied_socket ;;
    session-result-export-and-child-cleanup) case_cleanup ;;
    interrupted-export-cleanup) case_interrupted ;;
    runtime-context-engine) context_probe "$CHECKOUT/.dockerignore" runtime-probe server/Program.cs ;;
    test-context-engine) context_probe "$CHECKOUT/docker/tests/Dockerfile.dockerignore" test-probe tests/Shared/TestClassificationMetadata.cs ;;
    server2-independent-handoff) case_handoff ;;
    git-credential-smoke) case_git_smoke ;;
    throwaway-all) case_throwaway ;;
    stock-idle|stock-busy|insert-refused|insert-committed-idle|insert-committed-busy|attempt-committed|body-before-enter|recipient-before-ingestion|transcript-save-fails-release|transcript-save-fails-restart|receipt-before-verdict|response-before-client|receipt-before-manifest|changed-generation|failure-summary)
        case_receipt "$CASE" ;;
    *)
        write_result false "RealCasePending $CASE" 2 ;;
esac
