#!/bin/bash
# CARD-0660 V-9 in-image probe (qualification tooling, not product). Never COPYed into an image:
# scripts/verify-card0660-codex-image.ps1 bind-mounts it read-only into a throwaway container of
# the image under test, with no network, no published port, no Docker socket and throwaway
# volumes at /state (runner-state) and /work. One row per invocation; each prints exactly one
# line "C660_ROW <row> ok|fail <detail>" and exits 0 for ok, 1 for fail, 2 for a usage error.
# It never reads, prints or copies a real credential: the only auth.json it writes is a sentinel
# on the throwaway volume, and the only API key is a dummy aimed at an unreachable loopback port.
set -u

CODEX_VERSION=0.160.0
GROK_VERSION=1.0.41
JQ_VERSION=1.7.1
PACKAGE_ROOT=/opt/codex/$CODEX_VERSION/package
VENDOR=$PACKAGE_ROOT/vendor/x86_64-unknown-linux-musl
# Regular files in @openai/codex@0.160.0-linux-x64 (measured from the pinned tarball).
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
  sed -e 's/\x1b\[[0-9;]*[HC]/ /g' -e 's/\x1b\[[0-9;?]*[ -/]*[@-~]//g' -e 's/\x1b\][^\x07\x1b]*\(\x07\|\x1b\\\)//g' "$log" | tr -s '\r\n ' ' ' > "$log.txt"
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
  grok-version)
    need_uid 1654
    mkdir -p "$PROBE_HOME/grok" || result fail "cannot create isolated Grok home"
    out=$(env HOME=$PROBE_HOME GROK_HOME=$PROBE_HOME/grok /usr/local/bin/grok --version 2>"$PROBE_HOME/grok-version.err")
    code=$?
    [ $code -eq 0 ] || result fail "exit=$code"
    # The installer home is removed in the image, so the channel tag is optional.
    # Require the real CLI's hash shape, but pin only the qualified version.
    version_pattern="^grok ${GROK_VERSION//./\\.} \\([0-9a-f]{12}\\)( \\[stable\\])?$"
    [[ "$out" =~ $version_pattern ]] || result fail "version output does not match pinned Grok $GROK_VERSION format"
    [ ! -s "$PROBE_HOME/grok-version.err" ] || result fail "unexpected version stderr"
    result ok "Grok $GROK_VERSION as uid 1654 home=$PROBE_HOME/grok"
    ;;
  jq-version)
    need_uid 1654
    out=$(env HOME=$PROBE_HOME /usr/local/bin/jq --version 2>"$PROBE_HOME/jq-version.err")
    code=$?
    [ $code -eq 0 ] || result fail "exit=$code"
    [ "$out" = "jq-$JQ_VERSION" ] || result fail "stdout=[$out] expected jq-$JQ_VERSION"
    [ ! -s "$PROBE_HOME/jq-version.err" ] || result fail "unexpected version stderr"
    result ok "jq-$JQ_VERSION as uid 1654"
    ;;
  layout)
    command -v ps >/dev/null 2>&1 || result fail "ps required by codex managed app-server"
    [ "$(readlink -f /usr/local/bin/codex)" = "$VENDOR/bin/codex" ] || result fail "link=$(readlink -f /usr/local/bin/codex)"
    [ "$(command -v codex)" = /usr/local/bin/codex ] || result fail "PATH codex=$(command -v codex)"
    for f in bin/codex bin/codex-code-mode-host codex-path/rg codex-resources/bwrap; do
      [ -f "$VENDOR/$f" ] && [ -x "$VENDOR/$f" ] || result fail "missing executable $f"
    done
    grep -q '"version": "0.160.0-linux-x64"' "$PACKAGE_ROOT/package.json" || result fail "package.json version"
    grep -q '"version": "0.160.0"' "$VENDOR/codex-package.json" || result fail "codex-package.json version"
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
  net9-offline)
    need_uid 1654
    # The wrapper mounts fresh, task-owned volumes at the real cache destinations.
    # Their emptiness is checked before creating a home, project or restore output.
    export NUGET_PACKAGES=/home/app/.nuget/packages
    export NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch
    [ -d "$NUGET_PACKAGES" ] && [ -z "$(find "$NUGET_PACKAGES" -mindepth 1 -print -quit)" ] || result fail "NuGet packages cache is not empty"
    [ -d "$NUGET_SCRATCH" ] && [ -z "$(find "$NUGET_SCRATCH" -mindepth 1 -print -quit)" ] || result fail "NuGet scratch is not empty"
    [ -d "$PROBE_HOME" ] && [ -z "$(find "$PROBE_HOME" -mindepth 1 -print -quit)" ] || result fail "NuGet home is not empty"
    for pack in Microsoft.NETCore.App.Host.linux-x64 Microsoft.NETCore.App.Ref Microsoft.AspNetCore.App.Ref; do
      path="/usr/share/dotnet/packs/$pack/9.0.20"
      if [ "$pack" = Microsoft.NETCore.App.Host.linux-x64 ]; then
        [ -x "$path/runtimes/linux-x64/native/apphost" ] || result fail "ImagePackMissing:$pack"
      else
        [ -s "$path/data/FrameworkList.xml" ] || result fail "ImagePackMissing:$pack"
      fi
      printf 'C913_PACK %s\n' "$path"
    done
    root=$PROBE_HOME/net9-offline
    mkdir -p "$root/home/.nuget" "$root/project" || result fail "cannot create empty restore directories"
    export HOME="$root/home" DOTNET_CLI_HOME="$root/home"
    export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
    sdk="$(dotnet --version)" || result fail "SDK query failed"
    [ "$sdk" = 10.0.401 ] || result fail "default SDK is not 10.0.401"
    dotnet --list-sdks | grep -q '^10\.0\.401 ' || result fail "SDK 10.0.401 missing"
    dotnet --list-runtimes || result fail "runtime query failed"
    cat > "$root/project/Offline.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <UseAppHost>true</UseAppHost>
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
    <RuntimeFrameworkVersion>9.0.20</RuntimeFrameworkVersion>
    <TargetLatestRuntimePatch>false</TargetLatestRuntimePatch>
    <SelfContained>false</SelfContained>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
</Project>
PROJECT
    printf '%s\n' 'var context = new Microsoft.AspNetCore.Http.DefaultHttpContext(); System.Console.WriteLine(context.Response.StatusCode == 200 ? "net9-offline-ok" : "bad-context");' > "$root/project/Program.cs"
    printf '%s\n' '<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>' > "$root/project/NuGet.Config"
    output=$(dotnet restore "$root/project/Offline.csproj" --configfile "$root/project/NuGet.Config" --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 --nologo 2>&1)
    code=$?
    [ "$code" -eq 0 ] || result fail "offline restore exit=$code: $(printf '%s' "$output" | tail -c 500)"
    output=$(dotnet build "$root/project/Offline.csproj" --no-restore -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1 --nologo 2>&1)
    code=$?
    [ "$code" -eq 0 ] || result fail "offline apphost build exit=$code: $(printf '%s' "$output" | tail -c 500)"
    [ -x "$root/project/bin/Debug/net9.0/linux-x64/Offline" ] || result fail "native apphost missing"
    output=$("$root/project/bin/Debug/net9.0/linux-x64/Offline")
    code=$?
    [ "$code" -eq 0 ] && [ "$output" = net9-offline-ok ] || result fail "native apphost exit=$code or stdout mismatch"
    [ -z "$(find "$NUGET_PACKAGES" -mindepth 1 -maxdepth 1 -iname 'microsoft.*.app.*' -print -quit)" ] || result fail "framework package appeared in empty cache"
    result ok "SDK=$sdk uid=1654 restore=0 build=0 native-run=0 stdout=net9-offline-ok empty-mounted-pair network=none"
    ;;
  *)
    echo "usage: verify-codex-image.sh version|grok-version|jq-version|layout|install-readonly|no-baked-auth|fresh-home|trust|config-accepted|preserve-arm <nonce>|preserve-check|net9-offline" >&2
    exit 2
    ;;
esac
