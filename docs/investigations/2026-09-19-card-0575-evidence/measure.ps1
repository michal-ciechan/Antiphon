# CARD-0575 measurements. No secrets printed. Cleans up card0575-* resources.
$ErrorActionPreference = "Continue"
$ev = Split-Path -Parent $MyInvocation.MyCommand.Path
function Out-Log([string]$name, [string]$text) {
    $p = Join-Path $ev $name
    Set-Content -LiteralPath $p -Value $text -Encoding utf8
    Write-Output ("WROTE " + $name + " bytes=" + $text.Length)
}
function Invoke-Capture([string]$name, [scriptblock]$block) {
    $out = & $block 2>&1 | Out-String
    Out-Log $name $out
    return $out
}

# --- 0. Host docker / grok facts ---
Invoke-Capture "00-docker-version.txt" { docker version }
Invoke-Capture "01-docker-info.txt" {
    docker info --format "OSType={{.OSType}} Architecture={{.Architecture}} ServerVersion={{.ServerVersion}} OperatingSystem={{.OperatingSystem}} NCPU={{.NCPU}} MemTotal={{.MemTotal}} DockerRootDir={{.DockerRootDir}} Name={{.Name}}"
    docker info --format "CgroupDriver={{.CgroupDriver}} SecurityOptions={{json .SecurityOptions}}"
}
Invoke-Capture "02-docker-ps-postgres.txt" {
    docker ps -a --filter name=antiphon-postgres --format "table {{.ID}}`t{{.Names}}`t{{.Image}}`t{{.Status}}`t{{.Ports}}"
    docker port antiphon-postgres
}
Invoke-Capture "03-docker-images.txt" { docker images --format "table {{.Repository}}`t{{.Tag}}`t{{.ID}}`t{{.Size}}" }

$grokExe = Join-Path $env:USERPROFILE ".grok\bin\grok.exe"
$grokHome = Join-Path $env:USERPROFILE ".grok"
$pe = ""
if (Test-Path -LiteralPath $grokExe) {
    $fs = [System.IO.File]::OpenRead($grokExe)
    try {
        $buf = New-Object byte[] 64
        [void]$fs.Read($buf, 0, 64)
        $mz = [System.Text.Encoding]::ASCII.GetString($buf, 0, 2)
        $peOff = [BitConverter]::ToInt32($buf, 60)
        $pe = "path=$grokExe size=$($fs.Length) mz=$mz peOffset=$peOff"
    } finally { $fs.Close() }
    $ver = & $grokExe --version 2>&1 | Out-String
    $help = & $grokExe --help 2>&1 | Out-String
    Out-Log "04-grok-exe.txt" ($pe + "`n" + $ver + "`n--- help ---`n" + $help)
} else {
    Out-Log "04-grok-exe.txt" "MISSING $grokExe"
}

$homeListing = Get-ChildItem -LiteralPath $grokHome -Force | ForEach-Object {
    "{0}`t{1}`t{2}`t{3}" -f $_.Mode, $_.Length, $_.LastWriteTime.ToString("o"), $_.Name
}
$authPath = Join-Path $grokHome "auth.json"
$authMeta = if (Test-Path -LiteralPath $authPath) {
    $i = Get-Item -LiteralPath $authPath
    "auth.json EXISTS length=$($i.Length) lastWrite=$($i.LastWriteTime.ToString('o')) attributes=$($i.Attributes) (content not logged)"
} else { "auth.json MISSING" }
$lockPath = Join-Path $grokHome "auth.json.lock"
$lockMeta = if (Test-Path -LiteralPath $lockPath) { "auth.json.lock EXISTS length=$((Get-Item $lockPath).Length)" } else { "auth.json.lock MISSING" }
Out-Log "05-grok-home-listing.txt" ("GROK_HOME=$grokHome`n$authMeta`n$lockMeta`n---`n" + ($homeListing -join "`n"))

# --- 1. Host phone-home baselines ---
function Curl-Head([string]$url) {
    $tmp = Join-Path $env:TEMP ("card0575-" + [guid]::NewGuid().ToString("N") + ".body")
    try {
        $args = @("-sS", "-D", "-", "-o", $tmp, "-m", "15", "--max-redirs", "0", $url)
        $hdr = & curl.exe @args 2>&1 | Out-String
        $len = if (Test-Path $tmp) { (Get-Item $tmp).Length } else { 0 }
        $snip = ""
        if (Test-Path $tmp) {
            $raw = Get-Content -LiteralPath $tmp -Raw -ErrorAction SilentlyContinue
            if ($raw) { $snip = $raw.Substring(0, [Math]::Min(240, $raw.Length)) }
        }
        return "URL $url`nBYTES $len`n$hdr`nBODY_SNIP $snip"
    } finally { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
}

$hostHealth = @(
    (Curl-Head "http://127.0.0.1:17202/health"),
    (Curl-Head "http://127.0.0.1:17202/api/version"),
    (Curl-Head "http://127.0.0.1:17202/api/boards"),
    (Curl-Head "http://127.0.0.1:17203/"),
    (Curl-Head "http://localhost:17202/health")
) -join "`n=====`n"
Out-Log "10-host-localhost-http.txt" $hostHealth

$resolveCaddy = Invoke-Capture "11-host-dns-caddy.txt" {
    Write-Output "Resolve-DnsName antiphon.desktop.codeperf.net (default resolver):"
    try { Resolve-DnsName antiphon.desktop.codeperf.net -ErrorAction Stop | Format-Table -AutoSize | Out-String } catch { "ERR " + $_ }
    Write-Output "Resolve-DnsName -Server 1.1.1.1:"
    try { Resolve-DnsName antiphon.desktop.codeperf.net -Server 1.1.1.1 -ErrorAction Stop | Format-Table -AutoSize | Out-String } catch { "ERR " + $_ }
    Write-Output "curl --resolve to 127.0.0.1:443 / :"
    & curl.exe -sS -D - -o NUL -m 15 --resolve "antiphon.desktop.codeperf.net:443:127.0.0.1" "https://antiphon.desktop.codeperf.net/" 2>&1 | Out-String
    Write-Output "curl --resolve /health :"
    & curl.exe -sS -D - -o NUL -m 15 --resolve "antiphon.desktop.codeperf.net:443:127.0.0.1" "https://antiphon.desktop.codeperf.net/health" 2>&1 | Out-String
    Write-Output "curl --resolve /api/boards :"
    & curl.exe -sS -D - -o NUL -m 15 --resolve "antiphon.desktop.codeperf.net:443:127.0.0.1" "https://antiphon.desktop.codeperf.net/api/boards" 2>&1 | Out-String
    Write-Output "curl --resolve /api/version :"
    & curl.exe -sS -D - -o NUL -m 15 --resolve "antiphon.desktop.codeperf.net:443:127.0.0.1" "https://antiphon.desktop.codeperf.net/api/version" 2>&1 | Out-String
}

# --- 2. Phone-home from a Linux container ---
Invoke-Capture "20-container-phonehome.txt" {
    Write-Output "=== alpine wget host.docker.internal:17202/health ==="
    docker run --rm --name card0575-ph1 alpine:3.20 wget -S -O- -T 15 "http://host.docker.internal:17202/health" 2>&1 | Out-String
    Write-Output "=== alpine wget host.docker.internal:17202/api/version ==="
    docker run --rm --name card0575-ph2 alpine:3.20 wget -S -O- -T 15 "http://host.docker.internal:17202/api/version" 2>&1 | Out-String
    Write-Output "=== alpine wget host.docker.internal:17202/api/boards (first 400 via head) ==="
    docker run --rm --name card0575-ph3 alpine:3.20 sh -c 'wget -S -O- -T 15 http://host.docker.internal:17202/api/boards 2>&1 | head -c 500' 2>&1 | Out-String
    Write-Output "=== getent host.docker.internal + wget 17203 ==="
    docker run --rm --name card0575-ph4 alpine:3.20 sh -c 'getent hosts host.docker.internal; wget -S -O- -T 10 http://host.docker.internal:17203/ 2>&1 | head -c 300' 2>&1 | Out-String
    Write-Output "=== HTTPS antiphon.desktop.codeperf.net from alpine (busybox wget) ==="
    docker run --rm --name card0575-ph5 alpine:3.20 sh -c 'getent hosts antiphon.desktop.codeperf.net; wget -S -O- -T 15 https://antiphon.desktop.codeperf.net/ 2>&1 | head -c 400' 2>&1 | Out-String
    Write-Output "=== HTTPS /health /api/boards /api/version ==="
    docker run --rm --name card0575-ph6 alpine:3.20 sh -c 'wget -S -O- -T 15 https://antiphon.desktop.codeperf.net/health 2>&1 | head -c 250; echo; wget -S -O- -T 15 https://antiphon.desktop.codeperf.net/api/boards 2>&1 | head -c 250; echo; wget -S -O- -T 15 https://antiphon.desktop.codeperf.net/api/version 2>&1 | head -c 250' 2>&1 | Out-String
    Write-Output "=== alpine+curl --resolve Caddy via host.docker.internal IP ==="
    docker run --rm --name card0575-ph7 alpine:3.20 sh -c 'apk add --no-cache curl >/dev/null; IP=$(getent hosts host.docker.internal | awk "{print \$1; exit}"); echo host_ip=$IP; echo "--- / via 443 ---"; curl -sS -D - -o /dev/null -m 15 --resolve antiphon.desktop.codeperf.net:443:$IP https://antiphon.desktop.codeperf.net/; echo "--- /health ---"; curl -sS -D - -o /dev/null -m 15 --resolve antiphon.desktop.codeperf.net:443:$IP https://antiphon.desktop.codeperf.net/health; echo "--- /api/boards ---"; curl -sS -D - -o /dev/null -m 15 --resolve antiphon.desktop.codeperf.net:443:$IP https://antiphon.desktop.codeperf.net/api/boards; echo "--- /api/version ---"; curl -sS -D - -o /dev/null -m 15 --resolve antiphon.desktop.codeperf.net:443:$IP https://antiphon.desktop.codeperf.net/api/version' 2>&1 | Out-String
}

# --- 3. Host-socket canary ---
Invoke-Capture "30-socket-docker-info.txt" {
    Write-Output "=== docker:cli docker info via /var/run/docker.sock ==="
    docker run --rm --name card0575-sock1 -v /var/run/docker.sock:/var/run/docker.sock docker:cli docker info --format "Name={{.Name}} OSType={{.OSType}} ServerVersion={{.ServerVersion}} OperatingSystem={{.OperatingSystem}}" 2>&1 | Out-String
    Write-Output "=== docker:cli docker ps --filter antiphon-postgres ==="
    docker run --rm --name card0575-sock2 -v /var/run/docker.sock:/var/run/docker.sock docker:cli docker ps --filter name=antiphon-postgres --format "{{.Names}} {{.Ports}}" 2>&1 | Out-String
}
Invoke-Capture "31-socket-hello-world.txt" {
    docker run --rm --name card0575-sock3 -v /var/run/docker.sock:/var/run/docker.sock docker:cli docker run --rm hello-world 2>&1 | Out-String
}
Invoke-Capture "32-port-17280-collision.txt" {
    Write-Output "=== try bind host 17280 from inside worker (must fail; do not start postgres) ==="
    docker run --rm --name card0575-portfight -v /var/run/docker.sock:/var/run/docker.sock docker:cli docker run --rm --name card0575-steal17280 -p 17280:5432 alpine:3.20 true 2>&1 | Out-String
    Write-Output "=== confirm antiphon-postgres still running ==="
    docker ps --filter name=antiphon-postgres --format "{{.Names}} {{.Status}} {{.Ports}}"
}

# --- 4. Auth survival dummy GROK_HOME ---
$dummyHost = Join-Path $env:TEMP "card0575-grok-home"
Remove-Item -LiteralPath $dummyHost -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dummyHost | Out-Null
Set-Content -LiteralPath (Join-Path $dummyHost "auth.json") -Value '{"marker":"CARD0575-DUMMY-NOT-A-SECRET","scope":"dummy"}' -Encoding ascii
$dummyWin = ($dummyHost -replace '\\','/')
# Docker Desktop bind-mount: C:\Users\... -> /c/Users/... or just the Windows path
$bind = $dummyHost

docker rm -f card0575-auth 2>$null | Out-Null
Invoke-Capture "40-auth-a-create.txt" {
    docker run -d --name card0575-auth -e GROK_HOME=/opt/grok-home -v "${bind}:/opt/grok-home" alpine:3.20 sleep 300
    docker exec card0575-auth sh -c 'ls -la /opt/grok-home; echo ---; cat /opt/grok-home/auth.json; echo extra > /opt/grok-home/wrote-from-a.txt'
}
Invoke-Capture "41-auth-b-restart.txt" {
    docker restart card0575-auth
    Start-Sleep -Seconds 2
    docker exec card0575-auth sh -c 'ls -la /opt/grok-home; echo ---; cat /opt/grok-home/auth.json; echo; cat /opt/grok-home/wrote-from-a.txt'
}
Invoke-Capture "42-auth-c-replace-same-image.txt" {
    docker rm -f card0575-auth
    docker run -d --name card0575-auth -e GROK_HOME=/opt/grok-home -v "${bind}:/opt/grok-home" alpine:3.20 sleep 300
    docker exec card0575-auth sh -c 'ls -la /opt/grok-home; echo ---; cat /opt/grok-home/auth.json; echo; cat /opt/grok-home/wrote-from-a.txt'
}

$dfA = Join-Path $ev "Dockerfile.dummyA"
$dfB = Join-Path $ev "Dockerfile.dummyB"
Set-Content -LiteralPath $dfA -Value "FROM alpine:3.20`nRUN echo image-A > /image-id.txt`n" -Encoding ascii
Set-Content -LiteralPath $dfB -Value "FROM alpine:3.20`nRUN echo image-B > /image-id.txt`n" -Encoding ascii
Invoke-Capture "43-auth-d-rebuild.txt" {
    docker build -t card0575-dummy:a -f $dfA $ev
    docker rm -f card0575-auth
    docker run -d --name card0575-auth -e GROK_HOME=/opt/grok-home -v "${bind}:/opt/grok-home" card0575-dummy:a sleep 300
    Write-Output "=== on image A ==="
    docker exec card0575-auth sh -c 'cat /image-id.txt; echo; cat /opt/grok-home/auth.json; echo; cat /opt/grok-home/wrote-from-a.txt'
    docker build -t card0575-dummy:b -f $dfB $ev
    docker rm -f card0575-auth
    docker run -d --name card0575-auth -e GROK_HOME=/opt/grok-home -v "${bind}:/opt/grok-home" card0575-dummy:b sleep 300
    Write-Output "=== on image B (new image id) ==="
    docker exec card0575-auth sh -c 'cat /image-id.txt; echo; cat /opt/grok-home/auth.json; echo; cat /opt/grok-home/wrote-from-a.txt'
    docker inspect card0575-auth --format "Image={{.Image}} ConfigImage={{.Config.Image}}"
}

Invoke-Capture "44-auth-named-volume.txt" {
    docker volume rm card0575-grokhome 2>$null | Out-Null
    docker volume create card0575-grokhome
    docker rm -f card0575-vol 2>$null | Out-Null
    docker run --rm -v card0575-grokhome:/opt/grok-home alpine:3.20 sh -c 'echo "{\"marker\":\"NAMED-VOL-DUMMY\"}" > /opt/grok-home/auth.json'
    docker run --rm -v card0575-grokhome:/opt/grok-home alpine:3.20 cat /opt/grok-home/auth.json
    Write-Output "=== after new container same named volume ==="
    docker run --rm -v card0575-grokhome:/opt/grok-home alpine:3.20 cat /opt/grok-home/auth.json
}

Invoke-Capture "45-auth-unmounted-lost.txt" {
    Write-Output "=== new container NO volume: auth.json must be absent ==="
    docker run --rm alpine:3.20 sh -c 'ls -la /root/.grok 2>&1; ls -la /opt/grok-home 2>&1; echo GROK_HOME=$GROK_HOME'
}

# --- 5. Grok binary in Linux vs Windows PE ---
Invoke-Capture "50-grok-exe-in-linux.txt" {
    Write-Output "=== copy grok.exe into linux container and exec (expect exec format error) ==="
    docker run --rm -v "${grokExe}:/grok.exe:ro" alpine:3.20 sh -c 'ls -l /grok.exe; /grok.exe --version' 2>&1 | Out-String
}

Invoke-Capture "51-linux-grok-install.txt" {
    Write-Output "=== debian bookworm-slim: official install.sh then grok --version (no login) ==="
    docker run --rm --name card0575-grokinst debian:bookworm-slim bash -lc 'set -e; apt-get update -qq; apt-get install -y -qq curl ca-certificates >/dev/null; curl -fsSL https://x.ai/cli/install.sh | bash; echo PATH=$PATH; ls -la "$HOME/.grok/bin"; file "$HOME/.grok/bin/grok" || true; "$HOME/.grok/bin/grok" --version; echo ---; "$HOME/.grok/bin/grok" --help | head -n 80' 2>&1 | Out-String
}

Invoke-Capture "52-alpine-grok-install.txt" {
    Write-Output "=== alpine musl: install.sh (expect glibc mismatch or unsupported) ==="
    docker run --rm --name card0575-grokalp alpine:3.20 sh -c 'apk add --no-cache curl bash ca-certificates >/dev/null; curl -fsSL https://x.ai/cli/install.sh | bash; ls -la ~/.grok/bin; ~/.grok/bin/grok --version' 2>&1 | Out-String
}

# --- 6. docker exec TTY + callback env ---
docker rm -f card0575-exec 2>$null | Out-Null
Invoke-Capture "60-docker-exec-tty-callback.txt" {
    docker run -d --name card0575-exec alpine:3.20 sleep 180
    Write-Output "=== docker exec without -t: tty ==="
    docker exec card0575-exec sh -c 'echo tty=$(tty 2>/dev/null || echo none); echo stdin_tty=$([ -t 0 ] && echo yes || echo no)'
    Write-Output "=== docker exec -t: tty ==="
    docker exec -t card0575-exec sh -c 'echo tty=$(tty 2>/dev/null || echo none); echo stdin_tty=$([ -t 0 ] && echo yes || echo no)'
    Write-Output "=== docker exec -e ANTIPHON_API callback to host.docker.internal:17202/health ==="
    docker exec -e ANTIPHON_API=http://host.docker.internal:17202 card0575-exec sh -c 'echo ANTIPHON_API=$ANTIPHON_API; wget -S -O- -T 10 "$ANTIPHON_API/health"'
    Write-Output "=== pwsh present? ==="
    docker exec card0575-exec sh -c 'command -v pwsh; command -v powershell; command -v curl; command -v wget'
    Write-Output "=== host docker exec of this container from Windows (this is the v0 shape) ==="
    docker exec card0575-exec echo "v0-exec-ok"
}

# --- 7. Session-runner / CARD-0038 facts from this machine ---
Invoke-Capture "70-runner-listen.txt" {
    Write-Output "=== GET :17204/sessions (runner, no /api) ==="
    & curl.exe -sS -D - -o NUL -m 10 "http://127.0.0.1:17204/sessions" 2>&1 | Out-String
    Write-Output "=== GET :17202/api/version ==="
    & curl.exe -sS -m 10 "http://127.0.0.1:17202/api/version" 2>&1 | Out-String
}

# cleanup
docker rm -f card0575-auth card0575-exec card0575-vol 2>$null | Out-Null
docker rmi card0575-dummy:a card0575-dummy:b 2>$null | Out-Null
docker volume rm card0575-grokhome 2>$null | Out-Null
Remove-Item -LiteralPath $dummyHost -Recurse -Force -ErrorAction SilentlyContinue
Out-Log "99-done.txt" "cleanup complete $(Get-Date -Format o)"
Write-Output "ALL_MEASUREMENTS_DONE"
