#!/usr/bin/env bash
# Execute checked-out source in inherited children; only external commands/absolute roots are remapped.
set -eu
mode="$1"; repo="$2"
root=$(mktemp -d /tmp/c913-image-XXXXXXXX)
trap 'rm -rf -- "$root"' EXIT
fail() { printf 'FAIL %s\n' "$*"; exit 1; }
pass() { printf 'PASS %s\n' "$1"; }
if [ "$mode" = archive ]; then
    mkdir -p "$root/source/packs" "$root/bin"
    for pack in Microsoft.NETCore.App.Host.linux-x64 Microsoft.NETCore.App.Ref Microsoft.AspNetCore.App.Ref; do
        mkdir -p "$root/source/packs/$pack/9.0.20/data" "$root/source/packs/$pack/9.0.20/runtimes/linux-x64/native"
        printf sentinel > "$root/source/packs/$pack/9.0.20/data/FrameworkList.xml"
        printf '#!/bin/sh\nexit 0\n' > "$root/source/packs/$pack/9.0.20/runtimes/linux-x64/native/apphost"
        chmod 755 "$root/source/packs/$pack/9.0.20/runtimes/linux-x64/native/apphost"
    done
    tar -czf "$root/good.tar.gz" -C "$root/source" ./packs
    digest=$(sha512sum "$root/good.tar.gz" | cut -d' ' -f1)
    awk '/^RUN mkdir -p \/opt\/net9-packs/ { active=1 } active { print } active && !/\\$/ { exit }' "$repo/docker/session-runner-grok/Dockerfile" |
        sed -e '1s/^RUN //' -e "s|/opt/net9-packs|$root/output|g" -e "s|/tmp/net9-sdk.tar.gz|$root/download.tar.gz|g" > "$root/chain"
    cat > "$root/bin/curl" <<'SH'
#!/bin/sh
cp "$C913_ARCHIVE" "$4"
SH
    cat > "$root/bin/tar" <<'SH'
#!/bin/sh
printf 'tar\n' >> "$C913_TRACE"
exec /usr/bin/tar "$@"
SH
    chmod 755 "$root/bin/"*
    export PATH="$root/bin:$PATH" NET9_PACK_VERSION=9.0.20 NET9_SDK_VERSION=9.0.318 NET9_SDK_SHA512="$digest"
    export C913_ARCHIVE="$root/good.tar.gz" C913_TRACE="$root/tar-trace"
    bash "$root/chain" > "$root/out" 2>&1 || fail archive-valid
    [ "$(wc -l < "$C913_TRACE")" = 1 ] || fail archive-extraction
    for pack in Microsoft.NETCore.App.Host.linux-x64 Microsoft.NETCore.App.Ref Microsoft.AspNetCore.App.Ref; do
        cmp "$root/source/packs/$pack/9.0.20/data/FrameworkList.xml" "$root/output/packs/$pack/9.0.20/data/FrameworkList.xml" || fail archive-recipient
    done
    : > "$C913_TRACE"
    printf corruption >> "$C913_ARCHIVE"
    if bash "$root/chain" > "$root/out" 2>&1; then fail archive-corruption-accepted; fi
    [ ! -s "$C913_TRACE" ] || fail archive-digest-gates-extraction
    pass archive-digest-gates-extraction
    exit 0
fi
mkdir -p "$root/bin"
# Preserve predicates, project, config, process ordering and result from the real script.
sed -e "s|/c660-home|$root/home|g" \
    -e "s|/home/app/.nuget/packages|$root/packages|g" \
    -e "s|/var/cache/antiphon/nuget-scratch|$root/scratch|g" \
    -e "s|/usr/share/dotnet|$root/dotnet|g" \
    "$repo/docker/session-runner-grok/verify-codex-image.sh" > "$root/probe.sh"
cat > "$root/bin/id" <<'SH'
#!/bin/sh
printf '1654\n'
SH
cat > "$root/bin/dotnet" <<'SH'
#!/bin/bash
set -eu
printf '%s\n' "$*" >> "$C913_ROOT/trace"
case "$1" in
 --version) echo 10.0.401 ;;
 --list-sdks) echo '10.0.401 [/usr/share/dotnet/sdk]' ;;
 --list-runtimes) echo 'Microsoft.NETCore.App 9.0.20 [/usr/share/dotnet/shared/Microsoft.NETCore.App]' ;;
 restore)
    [ "$C913_FAULT" != restore ] || exit 17
    printf '%s\n' "HOME=$HOME" "DOTNET_CLI_HOME=${DOTNET_CLI_HOME:-}" "NUGET_PACKAGES=${NUGET_PACKAGES:-}" "NUGET_SCRATCH=${NUGET_SCRATCH:-}" "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=${DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE:-}" > "$C913_ROOT/environment"
    cp "$2" "$C913_ROOT/project"
    find "$C913_ROOT/home" -name NuGet.Config -exec cp {} "$C913_ROOT/config" \;
    ;;
 build)
    [ "$C913_FAULT" != build ] || exit 18
    directory=$(dirname "$2")
    for suffix in net9.0 net9.0/linux-x64; do
        mkdir -p "$directory/bin/Debug/$suffix"
        cat > "$directory/bin/Debug/$suffix/Offline" <<'NATIVE'
#!/bin/sh
[ "$C913_FAULT" != native-exit ] || exit 19
if [ "$C913_FAULT" = token ]; then echo wrong; else echo net9-offline-ok; fi
NATIVE
        [ "$C913_FAULT" = not-executable ] || chmod 755 "$directory/bin/Debug/$suffix/Offline"
    done
    if [ "$C913_FAULT" = post-cache ]; then mkdir -p "$NUGET_PACKAGES/microsoft.netcore.app.ref/9.0.20"; fi
    ;;
 *) exit 91 ;;
esac
SH
for command in curl npm pwsh; do
    printf '#!/bin/sh\nprintf "unexpected-call\\n" >> "$C913_ROOT/trace"\nexit 92\n' > "$root/bin/$command"
done
chmod 755 "$root/bin/"*
export PATH="$root/bin:$PATH" C913_ROOT="$root" C913_FAULT=none
fresh() {
    rm -rf -- "$root/home" "$root/packages" "$root/scratch" "$root/dotnet"
    mkdir -p "$root/home" "$root/packages" "$root/scratch"
    for pack in Microsoft.NETCore.App.Host.linux-x64 Microsoft.NETCore.App.Ref Microsoft.AspNetCore.App.Ref; do
        mkdir -p "$root/dotnet/packs/$pack/9.0.20/data" "$root/dotnet/packs/$pack/9.0.20/runtimes/linux-x64/native"
        printf sentinel > "$root/dotnet/packs/$pack/9.0.20/data/FrameworkList.xml"
        printf sentinel > "$root/dotnet/packs/$pack/9.0.20/runtimes/linux-x64/native/apphost"
        chmod 755 "$root/dotnet/packs/$pack/9.0.20/runtimes/linux-x64/native/apphost"
    done
    : > "$root/trace"
}
fresh
bash "$root/probe.sh" net9-offline > "$root/out" 2>&1 || { cat "$root/out"; fail probe-success; }
[ -f "$root/config" ] && grep -Fq '<packageSources><clear/></packageSources>' "$root/config" &&
    grep -Fq '<fallbackPackageFolders><clear/></fallbackPackageFolders>' "$root/config" || fail sources-and-fallbacks-cleared
pass sources-and-fallbacks-cleared
for token in '<TargetFramework>net9.0</TargetFramework>' '<UseAppHost>true</UseAppHost>' '<RuntimeIdentifier>linux-x64</RuntimeIdentifier>' '<SelfContained>false</SelfContained>' '<RuntimeFrameworkVersion>9.0.20</RuntimeFrameworkVersion>' '<TargetLatestRuntimePatch>false</TargetLatestRuntimePatch>' '<FrameworkReference Include="Microsoft.AspNetCore.App"'; do
    grep -Fq "$token" "$root/project" || fail project-contract
 done
grep -Fq 'Microsoft.AspNetCore.Http' "$root/home/net9-offline/project/Program.cs" || fail aspnet-compiled-use
grep -Fxq 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true' "$root/environment" || fail no-workload-traffic
grep -Fq 'NuGetAudit=false' "$root/trace" || fail no-audit-traffic
for directory in packages scratch home; do
    fresh; printf warm > "$root/$directory/.warm"
    if bash "$root/probe.sh" net9-offline > "$root/out" 2>&1; then fail "warm-$directory-refused"; fi
    ! grep -q '^restore ' "$root/trace" || fail "warm-$directory-before-restore"
    pass "warm-$directory-refused"
done
for pack in Microsoft.NETCore.App.Host.linux-x64 Microsoft.NETCore.App.Ref Microsoft.AspNetCore.App.Ref; do
    for warm in no yes; do
        fresh; rm -rf -- "$root/dotnet/packs/$pack/9.0.20"
        if [ "$warm" = yes ]; then mkdir -p "$root/packages/$(printf '%s' "$pack" | tr '[:upper:]' '[:lower:]')/9.0.20"; fi
        if bash "$root/probe.sh" net9-offline > "$root/out" 2>&1; then fail "missing-$pack"; fi
        ! grep -q '^restore ' "$root/trace" || fail missing-pack-before-restore
    done
    pass "missing-$pack-refused"
done
for C913_FAULT in restore build not-executable native-exit token post-cache; do
    fresh
    if bash "$root/probe.sh" net9-offline > "$root/out" 2>&1; then fail "$C913_FAULT-refused"; fi
    grep -q '^restore ' "$root/trace" || fail fault-setup
    pass "$C913_FAULT-refused"
done
pass probe-boundaries-complete
