#!/bin/bash
# CARD-0660 V-9 in-image probe (qualification tooling, not product). Never COPYed into an image:
# scripts/verify-card0660-codex-image.ps1 bind-mounts it read-only into a throwaway container of
# the image under test, with no network, no published port, no Docker socket and throwaway
# volumes at /state (runner-state) and /work. One row per invocation; each prints exactly one
# line "C660_ROW <row> ok|fail <detail>" and exits 0 for ok, 1 for fail, 2 for a usage error.
# It never reads, prints or copies a real credential: the only auth.json it writes is a sentinel
# on the throwaway volume, and the only API key is a dummy aimed at an unreachable loopback port.
set -u

CODEX_VERSION=0.156.1
PACKAGE_ROOT=/opt/codex/$CODEX_VERSION/package
VENDOR=$PACKAGE_ROOT/vendor/x86_64-unknown-linux-musl
# Regular files in @openai/codex@0.156.1-linux-x64 (measured from the pinned tarball).
PACKAGE_FILES=46
PROBE_HOME=/c660-home
SEEDED_CONFIG='check_for_update_on_startup = false
cli_auth_credentials_store = "file"
forced_login_method = "chatgpt"

[projects."/work/repos/antiphon"]
trust_level = "trusted"'

row=${1:-}

result() {
  echo "C660_ROW $row $1 $2"
  if [ "$1" = ok ]; then exit 0; fi
  exit 1
}

need_uid() {
  if [ "$(id -u)" != "$1" ]; then result fail "row must run as uid $1, ran as $(id -u)"; fi
}

# Runs the TUI under a pty for a fixed window and leaves the transcript in $1. The TUI never
# exits by itself; timeout ending it is the expected outcome.
tui_capture() {
  local log=$1 cwd=$2 home=$3
  shift 3
  local cmd="stty cols 120 rows 40; cd '$cwd' && exec /usr/local/bin/codex --no-alt-screen --dangerously-bypass-approvals-and-sandbox $*"
  env -u OPENAI_API_KEY -u CODEX_API_KEY -u CODEX_ACCESS_TOKEN \
    HOME=$PROBE_HOME CODEX_HOME="$home" TERM=xterm-256color ${TUI_API_KEY:+OPENAI_API_KEY=$TUI_API_KEY} \
    timeout 20 script -q -f -e -c "$cmd" "$log" >/dev/null 2>&1 </dev/null
  # Strip escape sequences so wording checks see rendered text.
  sed -e 's/\x1b\[[0-9;?]*[ -\/]*[@-~]//g' -e 's/\x1b\][^\x07\x1b]*\(\x07\|\x1b\\\)//g' "$log" | tr -s '\r\n ' ' ' > "$log.txt"
}

STUB_ARGS="-c model_providers.stub.name=Stub -c model_providers.stub.base_url=http://127.0.0.1:9/v1 -c model_providers.stub.env_key=OPENAI_API_KEY -c model_providers.stub.wire_api=responses -c model_provider=stub -c model=gpt-6-sol"

case "$row" in
  version)
    need_uid 1654
    case "$PROBE_HOME" in /tmp/*) result fail "probe home is under /tmp";; esac
    mkdir -p "$PROBE_HOME/codex" || result fail "cannot create isolated home"
    out=$(env HOME=$PROBE_HOME CODEX_HOME=$PROBE_HOME/codex /usr/local/bin/codex --version 2>"$PROBE_HOME/version.err")
    code=$?
    [ $code -eq 0 ] || result fail "exit=$code"
    [ "$out" = "codex-cli $CODEX_VERSION" ] || result fail "stdout=[$out]"
    [ ! -s "$PROBE_HOME/version.err" ] || result fail "stderr=[$(head -c 300 "$PROBE_HOME/version.err")]"
    result ok "codex-cli $CODEX_VERSION as uid 1654 home=$PROBE_HOME/codex"
    ;;
  layout)
    [ "$(readlink -f /usr/local/bin/codex)" = "$VENDOR/bin/codex" ] || result fail "link=$(readlink -f /usr/local/bin/codex)"
    [ "$(command -v codex)" = /usr/local/bin/codex ] || result fail "PATH codex=$(command -v codex)"
    for f in bin/codex bin/codex-code-mode-host codex-path/rg codex-resources/bwrap; do
      [ -f "$VENDOR/$f" ] && [ -x "$VENDOR/$f" ] || result fail "missing executable $f"
    done
    grep -q '"version": "0.156.1-linux-x64"' "$PACKAGE_ROOT/package.json" || result fail "package.json version"
    grep -q '"version": "0.156.1"' "$VENDOR/codex-package.json" || result fail "codex-package.json version"
    grep -q '"entrypoint": "bin/codex"' "$VENDOR/codex-package.json" || result fail "codex-package.json entrypoint"
    count=$(find "$PACKAGE_ROOT" -type f | wc -l)
    [ "$count" -eq "$PACKAGE_FILES" ] || result fail "files=$count expected=$PACKAGE_FILES"
    [ ! -e /opt/codex-download ] && [ ! -e /opt/codex-probe ] || result fail "build scratch left in the image"
    result ok "whole package files=$count helpers+metadata present link=$VENDOR/bin/codex"
    ;;
  install-readonly)
    need_uid 1654
    [ -x /usr/local/bin/codex ] || result fail "codex not executable by 1654"
    writable=$(find /opt/codex -writable -print -quit 2>/dev/null)
    [ -z "$writable" ] || result fail "writable by 1654: $writable"
    notroot=$(find /opt/codex ! -user 0 -print -quit 2>/dev/null)
    [ -z "$notroot" ] || result fail "not root-owned: $notroot"
    [ "$(stat -c %u /usr/local/bin/codex)" = 0 ] || result fail "link not root-owned"
    if touch "$VENDOR/bin/c660-write-probe" 2>/dev/null; then result fail "1654 created a file in the install"; fi
    if ln -sf /bin/true /usr/local/bin/codex 2>/dev/null; then result fail "1654 replaced the codex link"; fi
    result ok "executable, root-owned, unmodifiable by uid 1654"
    ;;
  no-baked-auth)
    need_uid 0
    for name in CODEX_HOME OPENAI_API_KEY CODEX_API_KEY CODEX_ACCESS_TOKEN; do
      if env | cut -d= -f1 | grep -qx "$name"; then result fail "image environment names $name"; fi
    done
    found=$(find / -xdev \( -path /proc -o -path /sys -o -path /state -o -path /work -o -path "$PROBE_HOME" \) -prune -o \
      \( -name auth.json \( -path '*codex*' -o -path '/root/*' -o -path '/home/*' -o -path '/app/*' \) \) -print 2>/dev/null | head -5)
    [ -z "$found" ] || result fail "baked auth: $found"
    for d in /root/.codex /home/app/.codex /app/.codex; do
      [ ! -e "$d" ] || result fail "baked home $d"
    done
    result ok "no Codex auth file, home or credential name in the image"
    ;;
  fresh-home)
    need_uid 1654
    [ "$(stat -c '%u:%g %a' /state/codex)" = "1654:1654 700" ] || result fail "home=$(stat -c '%u:%g %a' /state/codex)"
    [ -f /state/codex/config.toml ] && [ ! -L /state/codex/config.toml ] || result fail "config missing or a link"
    [ "$(stat -c '%u:%g %a' /state/codex/config.toml)" = "1654:1654 600" ] || result fail "config=$(stat -c '%u:%g %a' /state/codex/config.toml)"
    printf '%s\n' "$SEEDED_CONFIG" > "$PROBE_HOME/expected.toml"
    cmp -s "$PROBE_HOME/expected.toml" /state/codex/config.toml || result fail "config bytes differ"
    [ ! -e /state/codex/auth.json ] || result fail "fresh home has auth"
    [ ! -e /state/codex/.config.toml.state-init ] || result fail "seed scratch left behind"
    result ok "/state/codex 1654:1654 700, config.toml 1654:1654 600 with the exact seeded bytes"
    ;;
  trust)
    need_uid 1654
    repo=/work/repos/antiphon
    wt=/work/worktrees/c660-trust-probe
    export HOME=$PROBE_HOME GIT_CONFIG_NOSYSTEM=1
    if [ ! -d "$repo/.git" ]; then
      git init -q "$repo" && git -C "$repo" -c user.name=c660 -c user.email=c660@invalid commit -q --allow-empty -m c660 \
        || result fail "cannot create probe repository"
    fi
    git -C "$repo" worktree add -q --detach "$wt" >/dev/null 2>&1 || [ -d "$wt" ] || result fail "cannot add linked worktree"
    [ "$(git -C "$wt" rev-parse --path-format=absolute --git-common-dir)" = "$repo/.git" ] || result fail "worktree is not linked to $repo"
    # Seeded project table (the root trust under test) plus a test-only dummy key login; the
    # forced ChatGPT login is dropped here only because this home authenticates with the dummy key.
    trusted=$PROBE_HOME/trusted
    mkdir -p "$trusted"
    grep -v '^forced_login_method' /state/codex/config.toml > "$trusted/config.toml"
    grep -qx '\[projects."/work/repos/antiphon"\]' "$trusted/config.toml" || result fail "seed lost its project table"
    untrusted=$PROBE_HOME/untrusted
    mkdir -p "$untrusted"
    printf 'check_for_update_on_startup = false\n' > "$untrusted/config.toml"
    TUI_API_KEY=c660-dummy-not-a-credential tui_capture "$PROBE_HOME/trusted.log" "$wt" "$trusted" $STUB_ARGS
    TUI_API_KEY=c660-dummy-not-a-credential tui_capture "$PROBE_HOME/untrusted.log" "$wt" "$untrusted" $STUB_ARGS
    # Negative control first: the same launch without the seed must show the modal, or this
    # probe cannot see it at all.
    grep -q 'Trust this folder' "$PROBE_HOME/untrusted.log.txt" || result fail "control never rendered the trust modal"
    grep -q "OpenAI Codex (v$CODEX_VERSION)" "$PROBE_HOME/trusted.log.txt" || result fail "seeded launch: no banner"
    grep -q 'Ask Codex to do anything' "$PROBE_HOME/trusted.log.txt" || result fail "seeded launch: no composer"
    grep -q 'Trust this folder' "$PROBE_HOME/trusted.log.txt" && result fail "seeded launch rendered the trust modal"
    result ok "root trust covers linked $wt (control rendered the modal)"
    ;;
  config-accepted)
    need_uid 1654
    home=$PROBE_HOME/signed-out
    mkdir -p "$home"
    cp /state/codex/config.toml "$home/config.toml"
    tui_capture "$PROBE_HOME/signed-out.log" /work "$home"
    grep -q 'Sign in with ChatGPT' "$PROBE_HOME/signed-out.log.txt" || result fail "no sign-in screen: $(head -c 300 "$PROBE_HOME/signed-out.log.txt")"
    grep -qiE 'error|invalid' "$PROBE_HOME/signed-out.log.txt" && result fail "config rejected: $(grep -oiE '.{0,80}(error|invalid).{0,80}' "$PROBE_HOME/signed-out.log.txt" | head -1)"
    [ ! -e "$home/auth.json" ] || result fail "signed-out launch created auth"
    result ok "pinned CLI loads cli_auth_credentials_store/forced_login_method and shows sign-in"
    ;;
  preserve-arm)
    need_uid 1654
    printf '# c660 sentinel config %s\n' "$2" > /state/codex/config.toml || result fail "cannot write sentinel config"
    printf 'c660-sentinel-not-a-credential %s\n' "$2" > /state/codex/auth.json || result fail "cannot write sentinel auth"
    chmod 0600 /state/codex/auth.json
    sha256sum /state/codex/config.toml /state/codex/auth.json > "/state/.c660-preserve"
    stat -c '%n %u:%g %a' /state/codex/config.toml /state/codex/auth.json >> "/state/.c660-preserve"
    result ok "sentinels written"
    ;;
  preserve-check)
    need_uid 1654
    [ -f /state/.c660-preserve ] || result fail "not armed"
    sha256sum -c --quiet <(grep -E '^[0-9a-f]{64} ' /state/.c660-preserve) >/dev/null 2>&1 || result fail "second init changed config/auth bytes"
    now=$(stat -c '%n %u:%g %a' /state/codex/config.toml /state/codex/auth.json)
    [ "$now" = "$(grep -vE '^[0-9a-f]{64} ' /state/.c660-preserve)" ] || result fail "owner/mode changed: $now"
    rm -f /state/.c660-preserve
    result ok "second init preserved existing config and auth bytes, owner and mode"
    ;;
  *)
    echo "usage: verify-codex-image.sh version|layout|install-readonly|no-baked-auth|fresh-home|trust|config-accepted|preserve-arm <nonce>|preserve-check" >&2
    exit 2
    ;;
esac
