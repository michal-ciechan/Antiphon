#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1407: cutover-postgres-server2.ps1 defaults to -WhatIf, and
    restart-apphost.ps1 -StopOnly does not relaunch.

    No production database, no server2 socket, no AppHost restart.
    HTTP is pointed at a closed loopback port. External commands are
    shadowed by a PATH fake that records a call and fails. Window cases
    use an ephemeral loopback -TailnetPort and a temporary -Repository
    (CARD-1417), so nothing binds 5432 or reads this checkout's marker.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$script:passed = 0
$script:failed = 0
$script:failures = @()
$script:ClosedApi = 'http://127.0.0.1:9'

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Set-C1407Executable {
    param([string]$Path)
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return }
    $mode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::UserExecute -bor [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::GroupExecute -bor [System.IO.UnixFileMode]::OtherRead -bor [System.IO.UnixFileMode]::OtherExecute
    [System.IO.File]::SetUnixFileMode($Path, $mode)
}

function New-C1407FakeBin {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-fake-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $trace = Join-Path $dir 'trace.txt'
    '' | Set-Content -LiteralPath $trace -Encoding ascii
    foreach ($name in @('docker', 'dotnet', 'ssh', 'scp', 'age', 'curl', 'wget', 'pwsh')) {
        $path = Join-Path $dir $name
        $lines = @(
            '#!/bin/sh',
            ('printf "%s\n" ' + "'" + $name + "' >> '" + $trace + "'"),
            'exit 99'
        )
        Set-Content -LiteralPath $path -Value $lines -Encoding ascii
        Set-C1407Executable $path
    }
    return [pscustomobject]@{ Dir = $dir; Trace = $trace }
}

function Remove-C1407Tree {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Read-C1407Trace {
    param($Fake)
    if (-not $Fake -or -not (Test-Path -LiteralPath $Fake.Trace)) { return '' }
    return (Get-Content -LiteralPath $Fake.Trace -Raw -ErrorAction SilentlyContinue)
}

function New-C1407SmartFake {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-smart-' + [guid]::NewGuid().ToString('N'))
    $stdin = Join-Path $dir 'stdin'
    New-Item -ItemType Directory -Path $stdin -Force | Out-Null
    $trace = Join-Path $dir 'trace.txt'
    '' | Set-Content -LiteralPath $trace -Encoding ascii
    $body = @'
#!/bin/sh
name=${0##*/}
trace="$C1407_TRACE"
stdin_dir="$C1407_STDIN"
if [ -z "$trace" ] || [ -z "$stdin_dir" ]; then
  echo "c1407 fake is missing its trace paths" >&2
  exit 99
fi
nl='
'
tab=$(printf '\t')
flatten() {
  src=$1
  out=
  while [ -n "$src" ]; do
    rest=${src#?}
    first=${src%"$rest"}
    case "$src" in
      "$nl"*|"$tab"*) out="${out} " ;;
      *) out="${out}${first}" ;;
    esac
    src=$rest
  done
  printf '%s' "$out"
}
has_text() {
  hay=$1
  needle=$2
  case "$hay" in
    *"$needle"*) return 0 ;;
  esac
  return 1
}
parent_dir() {
  src=$1
  parent=${src%/*}
  if [ "$parent" = "$src" ]; then
    parent=.
  elif [ -z "$parent" ]; then
    parent=/
  fi
  printf '%s' "$parent"
}
read_stdin() {
  while IFS= read -r line || [ -n "$line" ]; do
    printf '%s\n' "$line"
  done
}
flatten_lines() {
  src_file=$1
  out=
  started=0
  while IFS= read -r line || [ -n "$line" ]; do
    if [ "$started" = 0 ]; then
      out=$line
      started=1
    else
      out="$out $line"
    fi
  done < "$src_file"
  if [ "$started" = 1 ]; then
    out="${out} "
  fi
  printf '%s' "$out"
}
line=$name
for arg in "$@"; do
  flat=$(flatten "$arg")
  line=$(printf '%s\t%s' "$line" "$flat")
done
printf '%s\n' "$line" >> "$trace"
stdin_file="$stdin_dir/$name.$$.in"
if [ -p /dev/stdin ]; then
  read_stdin > "$stdin_file"
fi
if [ "$name" = "pwsh" ]; then
  maint=0
  enter=0
  keepsix=0
  clearhold=0
  for arg in "$@"; do
    case "$arg" in
      *set-apphost-maintenance.ps1*) maint=1 ;;
      -EnterIfAbsent) enter=1 ;;
      *keep-six.ps1*) keepsix=1 ;;
      -ClearHold) clearhold=1 ;;
    esac
  done
  if [ "$maint" = 1 ] && [ "$enter" = 1 ]; then
    if [ -n "$C1407_ENTER_MARKER" ]; then
      mkdir -p "$(parent_dir "$C1407_ENTER_MARKER")"
      printf '%s\n' 'down-on-purpose c1417-enter' > "$C1407_ENTER_MARKER"
    fi
    if [ -n "$C1407_MAINTENANCE_EXIT" ]; then
      exit "$C1407_MAINTENANCE_EXIT"
    fi
  fi
  if [ "$keepsix" = 1 ] && [ "$clearhold" = 1 ] && [ "$C1407_FAIL_CLEARHOLD" = "1" ]; then
    exit 1
  fi
fi
if [ "$name" = "pwsh" ] && [ "$C1407_FAIL_MAINTENANCE" = "1" ]; then
  for arg in "$@"; do
    case "$arg" in
      *set-apphost-maintenance.ps1*) exit 1 ;;
    esac
  done
fi
sql=""
if [ -f "$stdin_file" ]; then
  sql=$(flatten_lines "$stdin_file")
fi
emit_sql() {
  count="$1"
  if has_text "$sql" 'client_addr'; then
    printf '%s\n' '1'
  elif has_text "$sql" 'pg_stat_activity'; then
    if [ "$name" = "docker" ] && [ -n "$C1407_SOURCE_SESSIONS" ]; then
      printf '%s\n' "$C1407_SOURCE_SESSIONS"
    else
      printf '%s\n' '0'
    fi
  elif has_text "$sql" 'pg_class'; then
    printf '%s\n' 'public.Sample'
  elif has_text "$sql" 'indisprimary'; then
    printf '%s\n' 'Id'
  elif has_text "$sql" 'min('; then
    printf '%s\n' "$count 1 $count"
  elif has_text "$sql" 'count(*)'; then
    printf '%s\n' "$count"
  elif has_text "$sql" 'pg_sequences'; then
    printf '%s\n' 'public.sample_seq 1 true'
  elif has_text "$sql" 'pg_extension'; then
    printf '%s\n' 'plpgsql 1.0'
  elif has_text "$sql" 'MigrationId'; then
    printf '%s\n' '20261021000000_Init'
  else
    printf '\n'
  fi
}
case "$name" in
  docker)
    joined=$(printf '%s ' "$@")
    if [ -n "$C1407_FOREIGN_MARKER" ] && [ ! -f "$C1407_FOREIGN_MARKER" ]; then
      mkdir -p "$(parent_dir "$C1407_FOREIGN_MARKER")"
      printf '%s\n' 'down-on-purpose foreign' > "$C1407_FOREIGN_MARKER"
    fi
    if [ "$C1407_FAIL_COMPOSE_START" = "1" ]; then
      for arg in "$@"; do
        if [ "$arg" = "start" ]; then
          exit 1
        fi
      done
    fi
    if [ "$C1407_FAIL_DUMP" = "1" ] && has_text "$joined" 'pg_dump'; then
      exit 1
    fi
    if has_text "$joined" 'psql'; then
      emit_sql "${C1407_SOURCE_COUNT:-3}"
    elif has_text "$joined" 'volume ls'; then
      printf '%s\n' 'antiphon_pgdata'
    elif has_text "$joined" 'inspect'; then
      printf '%s\n' 'exited'
    fi
    exit 0
    ;;
  ssh)
    joined=$(printf '%s ' "$@")
    if has_text "$joined" 'psql'; then
      emit_sql "${C1407_TARGET_COUNT:-3}"
    fi
    exit 0
    ;;
  dotnet)
    if [ "$C1407_FAIL_SECRET" = "1" ]; then
      for arg in "$@"; do
        if [ "$arg" = "set" ]; then
          exit 1
        fi
      done
    fi
    exit 0
    ;;
  pwsh)
    restart_script=0
    stop_only=0
    sha_restart=0
    for arg in "$@"; do
      case "$arg" in
        *restart-apphost.ps1*) restart_script=1 ;;
        -StopOnly) stop_only=1 ;;
        -ExpectedServerSha) sha_restart=1 ;;
      esac
    done
    if [ "$restart_script" = 1 ] && [ -n "$C1407_RESTART_STDOUT" ]; then
      printf '%s\n' "$C1407_RESTART_STDOUT"
    fi
    if [ "$stop_only" = 1 ] && [ -n "$C1407_STOPONLY_EXIT" ]; then
      exit "$C1407_STOPONLY_EXIT"
    fi
    if [ "$sha_restart" = 1 ] && [ -n "$C1407_SHA_RESTART_EXIT" ]; then
      exit "$C1407_SHA_RESTART_EXIT"
    fi
    if [ "$C1407_FAIL_RESTART" = "1" ]; then
      for arg in "$@"; do
        if [ "$arg" = "-ExpectedServerSha" ]; then
          exit 5
        fi
      done
    fi
    exit 0
    ;;
  *)
    exit 0
    ;;
esac
'@
    foreach ($name in @('docker', 'dotnet', 'ssh', 'scp', 'age', 'curl', 'wget', 'pwsh')) {
        $path = Join-Path $dir $name
        Set-Content -LiteralPath $path -Value $body -Encoding ascii
        Set-C1407Executable $path
    }
    return [pscustomobject]@{ Dir = $dir; Trace = $trace; Stdin = $stdin }
}

function Start-C1407Api {
    param(
        [int]$Port,
        [string]$Sha,
        [string]$PendingJson,
        [int]$LandPendingStatus = 200,
        [string]$MarkerOnSessionState = ''
    )
    $log = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-api-' + [guid]::NewGuid().ToString('N') + '.log')
    '' | Set-Content -LiteralPath $log -Encoding ascii
    $state = [hashtable]::Synchronized(@{ Stop = $false })
    $sb = {
        param($Port, $Sha, $PendingJson, $LandPendingStatus, $State, $Log, $MarkerOnSessionState)
        try {
            $listener = [System.Net.HttpListener]::new()
            $listener.Prefixes.Add(('http://127.0.0.1:{0}/' -f $Port))
            $listener.Start()
            while ($listener.IsListening) {
                $ctx = $listener.GetContext()
                if ($State.Stop) {
                    $ctx.Response.StatusCode = 200
                    $ctx.Response.Close()
                    break
                }
                $path = $ctx.Request.Url.AbsolutePath
                $query = [string]$ctx.Request.Url.Query
                $body = '{}'
                $code = 200
                if ($path -eq '/api/version') {
                    $body = '{"version":"' + $Sha + '","capabilities":["land-v2"]}'
                } elseif ($path -eq '/api/diagnostics/session-state') {
                    if (-not [string]::IsNullOrWhiteSpace($MarkerOnSessionState)) {
                        $markerParent = Split-Path -Parent $MarkerOnSessionState
                        if (-not [string]::IsNullOrWhiteSpace($markerParent)) {
                            [System.IO.Directory]::CreateDirectory($markerParent) | Out-Null
                        }
                        [System.IO.File]::WriteAllText($MarkerOnSessionState, "down-on-purpose foreign")
                    }
                    $body = '{"pool":{"Pooling":true,"NoResetOnClose":true,"MaxAutoPrepare":0,"Multiplexing":false}}'
                } elseif ($path -eq '/health') {
                    $body = 'ok'
                } elseif ($path -eq '/api/agent-tasks' -and $query.Contains('landPending=true')) {
                    $body = $PendingJson
                    if ([int]$LandPendingStatus -ne 200) { $code = [int]$LandPendingStatus }
                } else {
                    $code = 404
                }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
                $ctx.Response.StatusCode = $code
                $ctx.Response.ContentType = 'application/json'
                $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                $ctx.Response.Close()
            }
            $listener.Stop()
            $listener.Close()
        } catch {
            Set-Content -LiteralPath $Log -Value $_.Exception.Message -Encoding ascii
        }
    }
    $rs = [runspacefactory]::CreateRunspace()
    $rs.Open()
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    [void]$ps.AddScript($sb).AddArgument($Port).AddArgument($Sha).AddArgument($PendingJson).AddArgument($LandPendingStatus).AddArgument($state).AddArgument($log).AddArgument($MarkerOnSessionState)
    $handle = $ps.BeginInvoke()
    return [pscustomobject]@{ Runspace = $rs; PowerShell = $ps; Handle = $handle; Port = $Port; State = $state; Log = $log }
}

function Stop-C1407Api {
    param($Api)
    if (-not $Api) { return }
    try { $Api.State.Stop = $true } catch {}
    try {
        Invoke-WebRequest -Uri ('http://127.0.0.1:{0}/stop' -f $Api.Port) -TimeoutSec 2 -UseBasicParsing | Out-Null
    } catch {}
    try { $Api.PowerShell.Stop() } catch {}
    try { $Api.Runspace.Close() } catch {}
    try { $Api.PowerShell.Dispose() } catch {}
    Remove-C1407Tree $Api.Log
}

function Wait-C1407Api {
    param($Api)
    $ready = $false
    for ($i = 0; $i -lt 50; $i++) {
        try {
            $probe = Invoke-WebRequest -Uri ('http://127.0.0.1:{0}/api/version' -f $Api.Port) -TimeoutSec 2 -UseBasicParsing
            if ([int]$probe.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            Start-Sleep -Milliseconds 100
        }
    }
    return $ready
}

function Get-C1407ScriptSha {
    $path = Join-Path $here 'cutover-postgres-server2.ps1'
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-C1407Token {
    # CARD-1417 (Review d6b441d2): the challenge token the cutover preview prints, computed here
    # independently so the table can name yesterday's, a stale, and another repository's token.
    # First 8 hex characters of SHA-256 over the UTF-8 text
    # <machine name> LF <UTC date yyyy-MM-dd> LF <script SHA-256 lowercase hex> LF <resolved -Repository>.
    param([string]$Repository = '', [int]$DayOffset = 0)
    $repo = ''
    if (-not [string]::IsNullOrWhiteSpace($Repository)) {
        $repo = [System.IO.Path]::GetFullPath($Repository).TrimEnd([char]47, [char]92)
    }
    $day = [datetime]::UtcNow.Date.AddDays($DayOffset).ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
    $text = [Environment]::MachineName + "`n" + $day + "`n" + (Get-C1407ScriptSha) + "`n" + $repo
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)) } finally { $sha.Dispose() }
    return [System.BitConverter]::ToString($hash).Replace('-', '').ToLowerInvariant().Substring(0, 8)
}

function New-C1407WindowArgs {
    param(
        [string]$ApiBase,
        [string]$Sha,
        [string]$Snapshot,
        [string]$Drill,
        [string]$Secret,
        [string]$DumpDirectory,
        [string]$Recipient,
        [string]$Identity,
        [string]$PendingFixture,
        [string]$CommandHost,
        [string]$Repository = '',
        [int]$TailnetPort = 0
    )
    $repo = $Repository
    if ([string]::IsNullOrWhiteSpace($repo)) { $repo = Split-Path $here -Parent }
    $windowArgs = @(
        '-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token -Repository $repo),
        '-ApiBase', $ApiBase,
        '-SnapshotReference', $Snapshot,
        '-DrillRecord', $Drill,
        '-ConnectionSecretFile', $Secret,
        '-RcTip', $Sha,
        '-Repository', $repo,
        '-ProjectId', '00000000-0000-0000-0000-0000000001407',
        '-DumpDirectory', $DumpDirectory,
        '-AgeRecipientFile', $Recipient,
        '-AgeIdentityFile', $Identity,
        '-Server2SshHost', 'server2',
        '-Server2ComposeDir', '/tmp/c1407-compose',
        '-TailnetAddress', '127.0.0.1',
        '-TailnetOnlyConfirmed',
        '-HoldAgent', '11111111-1111-1111-1111-111111111111',
        '-HoldReason', 'cutover window test',
        '-HoldUntil', '2099-01-01T00:00:00Z',
        '-CommandHost', $CommandHost
    )
    if (-not [string]::IsNullOrWhiteSpace($PendingFixture)) {
        $windowArgs += @('-LandPendingFixture', $PendingFixture)
    }
    if ($TailnetPort -gt 0) {
        $windowArgs += @('-TailnetPort', [string]$TailnetPort)
    }
    return $windowArgs
}

function Read-C1407ArgvLines {
    param($Fake)
    if (-not $Fake -or -not (Test-Path -LiteralPath $Fake.Trace)) { return @() }
    return @(Get-Content -LiteralPath $Fake.Trace -ErrorAction SilentlyContinue)
}

function Test-C1407Field {
    param([string]$Line, [string]$Token)
    $parts = @($Line.Split([char]9))
    foreach ($part in $parts) {
        if ($part -eq $Token) { return $true }
    }
    return $false
}

function Test-C1407Suffix {
    param([string]$Line, [string]$Suffix)
    foreach ($part in @($Line.Split([char]9))) {
        if ($part.EndsWith($Suffix)) { return $true }
    }
    return $false
}

function Test-C1407Contains {
    param([string]$Line, [string]$Text)
    foreach ($part in @($Line.Split([char]9))) {
        if ($part.Contains($Text)) { return $true }
    }
    return $false
}

function Get-C1407SafeText {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    if ($Text.Contains('SENTINEL-1407-ARGV')) { return 'omitted-because-sentinel' }
    if ($Text.Contains('AGE-IDENTITY-BODY-1407')) { return 'omitted-because-identity-body' }
    if ($Text.Contains('identity-sentinel-1407')) { return 'omitted-because-identity-path' }
    if ($Text.Length -gt 400) { return $Text.Substring(0, 400) }
    return $Text
}

function Invoke-C1407WindowRun {
    param(
        [string]$PendingJson = '{"items":[]}',
        [switch]$WithFixture,
        [string]$FixtureJson = '{"items":[]}',
        [string[]]$ExtraArgs = @(),
        [string]$SourceCount = '3',
        [string]$TargetCount = '3',
        [string]$FailMaintenance = '',
        [int]$LandPendingStatus = 200,
        [string]$FailDump = '',
        [string]$FailSecret = '',
        [string]$FailRestart = '',
        [switch]$SkipApi,
        [switch]$SkipTcp,
        [string]$Repository = '',
        [string]$RestartStdout = '',
        [string]$StopOnlyExit = '',
        [string]$ShaRestartExit = '',
        [string]$SourceSessions = '',
        [string]$MarkerOnSessionState = '',
        [string]$ForeignMarker = '',
        [string]$FailComposeStart = '',
        [string]$FailClearHold = '',
        [string]$EnterMarker = '',
        [string]$MaintenanceExit = ''
    )
    $result = @{
        ExitCode    = 99
        Text        = ''
        Lines       = @()
        Stdin       = @()
        Ready       = $false
        Error       = ''
        TailnetPort = 0
        Repository  = ''
    }
    $fake = $null
    $api = $null
    $tcp = $null
    $ownRepo = $null
    $snap = $null
    $drill = $null
    $secret = $null
    $recipient = $null
    $dumpDir = $null
    $prevTrace = $env:C1407_TRACE
    $prevStdin = $env:C1407_STDIN
    $prevSource = $env:C1407_SOURCE_COUNT
    $prevTarget = $env:C1407_TARGET_COUNT
    $prevFail = $env:C1407_FAIL_MAINTENANCE
    $prevDump = $env:C1407_FAIL_DUMP
    $prevSecret = $env:C1407_FAIL_SECRET
    $prevRestart = $env:C1407_FAIL_RESTART
    $prevStdout = $env:C1407_RESTART_STDOUT
    $prevStopExit = $env:C1407_STOPONLY_EXIT
    $prevShaExit = $env:C1407_SHA_RESTART_EXIT
    $prevSessions = $env:C1407_SOURCE_SESSIONS
    $prevForeign = $env:C1407_FOREIGN_MARKER
    $prevComposeStart = $env:C1407_FAIL_COMPOSE_START
    $prevClearHold = $env:C1407_FAIL_CLEARHOLD
    $prevEnterMarker = $env:C1407_ENTER_MARKER
    $prevMaintExit = $env:C1407_MAINTENANCE_EXIT
    try {
        $fake = New-C1407SmartFake
        $env:C1407_TRACE = $fake.Trace
        $env:C1407_STDIN = $fake.Stdin
        $env:C1407_SOURCE_COUNT = $SourceCount
        $env:C1407_TARGET_COUNT = $TargetCount
        if ($FailMaintenance -eq '1') { $env:C1407_FAIL_MAINTENANCE = '1' }
        else { Remove-Item Env:C1407_FAIL_MAINTENANCE -ErrorAction SilentlyContinue }
        if ($FailDump -eq '1') { $env:C1407_FAIL_DUMP = '1' } else { Remove-Item Env:C1407_FAIL_DUMP -ErrorAction SilentlyContinue }
        if ($FailSecret -eq '1') { $env:C1407_FAIL_SECRET = '1' } else { Remove-Item Env:C1407_FAIL_SECRET -ErrorAction SilentlyContinue }
        if ($FailRestart -eq '1') { $env:C1407_FAIL_RESTART = '1' } else { Remove-Item Env:C1407_FAIL_RESTART -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($RestartStdout)) { $env:C1407_RESTART_STDOUT = $RestartStdout }
        else { Remove-Item Env:C1407_RESTART_STDOUT -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($StopOnlyExit)) { $env:C1407_STOPONLY_EXIT = $StopOnlyExit }
        else { Remove-Item Env:C1407_STOPONLY_EXIT -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($ShaRestartExit)) { $env:C1407_SHA_RESTART_EXIT = $ShaRestartExit }
        else { Remove-Item Env:C1407_SHA_RESTART_EXIT -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($SourceSessions)) { $env:C1407_SOURCE_SESSIONS = $SourceSessions }
        else { Remove-Item Env:C1407_SOURCE_SESSIONS -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($ForeignMarker)) { $env:C1407_FOREIGN_MARKER = $ForeignMarker }
        else { Remove-Item Env:C1407_FOREIGN_MARKER -ErrorAction SilentlyContinue }
        if ($FailComposeStart -eq '1') { $env:C1407_FAIL_COMPOSE_START = '1' }
        else { Remove-Item Env:C1407_FAIL_COMPOSE_START -ErrorAction SilentlyContinue }
        if ($FailClearHold -eq '1') { $env:C1407_FAIL_CLEARHOLD = '1' }
        else { Remove-Item Env:C1407_FAIL_CLEARHOLD -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($EnterMarker)) { $env:C1407_ENTER_MARKER = $EnterMarker }
        else { Remove-Item Env:C1407_ENTER_MARKER -ErrorAction SilentlyContinue }
        if (-not [string]::IsNullOrWhiteSpace($MaintenanceExit)) { $env:C1407_MAINTENANCE_EXIT = $MaintenanceExit }
        else { Remove-Item Env:C1407_MAINTENANCE_EXIT -ErrorAction SilentlyContinue }
        # CARD-1417 A-4: an ephemeral loopback port, never 5432, and a temporary repository
        # so no case reads or binds anything the real checkout or a real Postgres owns.
        $tailnetPort = 0
        if (-not $SkipTcp) {
            $tcp = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
            $tcp.Start()
            $tailnetPort = ([System.Net.IPEndPoint]$tcp.LocalEndpoint).Port
        }
        $result.TailnetPort = $tailnetPort
        $repoUsed = $Repository
        if ([string]::IsNullOrWhiteSpace($repoUsed)) {
            $ownRepo = New-C1407TempRepo
            $repoUsed = $ownRepo
        }
        $result.Repository = $repoUsed
        $sha = '0123456789abcdef0123456789abcdef01234567'
        $apiBase = $script:ClosedApi
        if (-not $SkipApi) {
            $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
            $probe.Start()
            $port = $probe.LocalEndpoint.Port
            $probe.Stop()
            $api = Start-C1407Api -Port $port -Sha $sha -PendingJson $PendingJson -LandPendingStatus $LandPendingStatus -MarkerOnSessionState $MarkerOnSessionState
            if (-not (Wait-C1407Api $api)) {
                $result.Error = 'api did not become ready'
                return [pscustomobject]$result
            }
            $apiBase = 'http://127.0.0.1:{0}' -f $port
        }
        $snap = New-C1407File -Body 'snapshot-reference'
        $drill = New-C1407Drill
        $secret = New-C1407File -Body ('Password=' + 'SENTINEL-1407-ARGV')
        $recipient = New-C1407File -Body 'age1recipient'
        $dumpDir = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-dump-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dumpDir -Force | Out-Null
        $identity = Join-Path $dumpDir 'identity-sentinel-1407'
        [System.IO.File]::WriteAllText($identity, 'AGE-IDENTITY-BODY-1407')
        $fixture = $null
        if ($WithFixture) {
            $fixture = Join-Path $dumpDir 'land.json'
            [System.IO.File]::WriteAllText($fixture, $FixtureJson)
        }
        $window = New-C1407WindowArgs -ApiBase $apiBase -Sha $sha -Snapshot $snap -Drill $drill `
            -Secret $secret -DumpDirectory $dumpDir -Recipient $recipient -Identity $identity `
            -PendingFixture $fixture -CommandHost (Join-Path $fake.Dir 'pwsh') -Repository $repoUsed -TailnetPort $tailnetPort
        if ($ExtraArgs -and $ExtraArgs.Count -gt 0) { $window += $ExtraArgs }
        $ran = Invoke-C1407Cutover -Fake $fake -Arguments $window
        $result.ExitCode = $ran.ExitCode
        $result.Text = $ran.Text
        $result.Lines = @(Read-C1407ArgvLines $fake)
        $bodies = @()
        foreach ($file in @(Get-ChildItem -LiteralPath $fake.Stdin -File -ErrorAction SilentlyContinue)) {
            $bodies += [pscustomobject]@{
                Name = $file.Name
                Body = [System.IO.File]::ReadAllText($file.FullName)
            }
        }
        $result.Stdin = $bodies
        $result.Ready = $true
    } catch {
        $result.Error = $_.Exception.Message
    } finally {
        if ($tcp) { try { $tcp.Stop() } catch {} }
        Stop-C1407Api $api
        if ($null -eq $prevTrace) { Remove-Item Env:C1407_TRACE -ErrorAction SilentlyContinue } else { $env:C1407_TRACE = $prevTrace }
        if ($null -eq $prevStdin) { Remove-Item Env:C1407_STDIN -ErrorAction SilentlyContinue } else { $env:C1407_STDIN = $prevStdin }
        if ($null -eq $prevSource) { Remove-Item Env:C1407_SOURCE_COUNT -ErrorAction SilentlyContinue } else { $env:C1407_SOURCE_COUNT = $prevSource }
        if ($null -eq $prevTarget) { Remove-Item Env:C1407_TARGET_COUNT -ErrorAction SilentlyContinue } else { $env:C1407_TARGET_COUNT = $prevTarget }
        if ($null -eq $prevFail) { Remove-Item Env:C1407_FAIL_MAINTENANCE -ErrorAction SilentlyContinue } else { $env:C1407_FAIL_MAINTENANCE = $prevFail }
        if ($null -eq $prevDump) { Remove-Item Env:C1407_FAIL_DUMP -ErrorAction SilentlyContinue } else { $env:C1407_FAIL_DUMP = $prevDump }
        if ($null -eq $prevSecret) { Remove-Item Env:C1407_FAIL_SECRET -ErrorAction SilentlyContinue } else { $env:C1407_FAIL_SECRET = $prevSecret }
        if ($null -eq $prevRestart) { Remove-Item Env:C1407_FAIL_RESTART -ErrorAction SilentlyContinue } else { $env:C1407_FAIL_RESTART = $prevRestart }
        if ($null -eq $prevStdout) { Remove-Item Env:C1407_RESTART_STDOUT -ErrorAction SilentlyContinue } else { $env:C1407_RESTART_STDOUT = $prevStdout }
        if ($null -eq $prevStopExit) { Remove-Item Env:C1407_STOPONLY_EXIT -ErrorAction SilentlyContinue } else { $env:C1407_STOPONLY_EXIT = $prevStopExit }
        if ($null -eq $prevShaExit) { Remove-Item Env:C1407_SHA_RESTART_EXIT -ErrorAction SilentlyContinue } else { $env:C1407_SHA_RESTART_EXIT = $prevShaExit }
        if ($null -eq $prevSessions) { Remove-Item Env:C1407_SOURCE_SESSIONS -ErrorAction SilentlyContinue } else { $env:C1407_SOURCE_SESSIONS = $prevSessions }
        if ($null -eq $prevForeign) { Remove-Item Env:C1407_FOREIGN_MARKER -ErrorAction SilentlyContinue } else { $env:C1407_FOREIGN_MARKER = $prevForeign }
        if ($null -eq $prevComposeStart) { Remove-Item Env:C1407_FAIL_COMPOSE_START -ErrorAction SilentlyContinue } else { $env:C1407_FAIL_COMPOSE_START = $prevComposeStart }
        if ($null -eq $prevClearHold) { Remove-Item Env:C1407_FAIL_CLEARHOLD -ErrorAction SilentlyContinue } else { $env:C1407_FAIL_CLEARHOLD = $prevClearHold }
        if ($null -eq $prevEnterMarker) { Remove-Item Env:C1407_ENTER_MARKER -ErrorAction SilentlyContinue } else { $env:C1407_ENTER_MARKER = $prevEnterMarker }
        if ($null -eq $prevMaintExit) { Remove-Item Env:C1407_MAINTENANCE_EXIT -ErrorAction SilentlyContinue } else { $env:C1407_MAINTENANCE_EXIT = $prevMaintExit }
        Remove-C1407Tree $(if ($fake) { $fake.Dir } else { $null })
        Remove-C1407Tree $snap
        Remove-C1407Tree $drill
        Remove-C1407Tree $secret
        Remove-C1407Tree $recipient
        Remove-C1407Tree $dumpDir
        Remove-C1407Tree $ownRepo
    }
    return [pscustomobject]$result
}

function Test-C1407HoldReleased {
    param($Lines)
    $items = @($Lines)
    $holdAt = -1
    $clearAt = -1
    $stopAt = -1
    for ($i = 0; $i -lt $items.Count; $i++) {
        $line = [string]$items[$i]
        $keep = Test-C1407Suffix -Line $line -Suffix 'keep-six.ps1'
        if ($keep -and (Test-C1407Field -Line $line -Token '-Hold') -and $holdAt -lt 0) { $holdAt = $i }
        if ($keep -and (Test-C1407Field -Line $line -Token '-ClearHold')) { $clearAt = $i }
        if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-StopOnly')) {
            $stopAt = $i
        }
    }
    return ($holdAt -ge 0 -and $clearAt -gt $holdAt -and $stopAt -lt 0)
}

function Invoke-C1407Cutover {
    param(
        [string[]]$Arguments = @(),
        $Fake,
        [string[]]$HostArguments = @(),
        [string]$FileSwitch = '-File',
        [switch]$AsCommand,
        [string]$CommandSwitch = '-Command',
        [string]$WorkingDirectory = '',
        [string]$ScriptPath = '',
        [string[]]$StdinLines = @()
    )
    # -HostArguments are pwsh's own options placed before the -File pair; -FileSwitch is the spelling
    # of that switch (pwsh also accepts -f, --File and /File); -AsCommand starts the script through
    # command text instead of a -File pair, with -CommandSwitch as that option's spelling;
    # -WorkingDirectory is the caller's location for the launch; -ScriptPath replaces the cutover
    # script with another file (a wrapper); -StdinLines are fed to the child's stdin (a -NoExit prompt).
    $scriptPath = $ScriptPath
    if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = Join-Path $here 'cutover-postgres-server2.ps1' }
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $prev = $env:PATH
    $pushed = $false
    try {
        if ($Fake) {
            $env:PATH = $Fake.Dir + [System.IO.Path]::PathSeparator + $prev
        }
        if (-not [string]::IsNullOrEmpty($WorkingDirectory)) {
            Push-Location -LiteralPath $WorkingDirectory
            $pushed = $true
        }
        $launch = @('-NoProfile') + @($HostArguments)
        if ($AsCommand) { $launch += @($CommandSwitch, '&', ("'" + $scriptPath + "'")) }
        else { $launch += @($FileSwitch, $scriptPath) }
        $launch += @($Arguments)
        if ($StdinLines.Count -gt 0) {
            $output = @($StdinLines | & $pwsh @launch 2>&1 | ForEach-Object { $_.ToString() })
        } else {
            $output = @(& $pwsh @launch 2>&1 | ForEach-Object { $_.ToString() })
        }
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text     = ($output -join "`n")
            Trace    = $(if ($Fake) { (Read-C1407Trace $Fake).Trim() } else { '' })
        }
    } finally {
        if ($pushed) { Pop-Location }
        if ($null -eq $prev) { Remove-Item Env:PATH -ErrorAction SilentlyContinue }
        else { $env:PATH = $prev }
    }
}

function New-C1407Drill {
    param(
        [bool]$Passed = $true,
        [double]$Latency = 6.72,
        [int]$DumpRestoreSec = 30,
        [int]$CopySec = 10
    )
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-drill-' + [guid]::NewGuid().ToString('N') + '.json')
    $json = '{{"passed":{0},"latencyMedianMs":{1},"dumpRestoreSec":{2},"copySec":{3}}}' -f `
        $(if ($Passed) { 'true' } else { 'false' }), `
        $Latency.ToString([System.Globalization.CultureInfo]::InvariantCulture), `
        $DumpRestoreSec, `
        $CopySec
    Set-Content -LiteralPath $path -Value $json -Encoding ascii
    return $path
}

function New-C1407File {
    param([string]$Body)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-file-' + [guid]::NewGuid().ToString('N'))
    Set-Content -LiteralPath $path -Value $Body -Encoding ascii
    return $path
}

function Test-PhaseOrder {
    param([string]$Text)
    $phases = @(
        'PHASE 0 Pre-flight',
        'PHASE 1 Quiesce',
        'PHASE 2 Final dump and restore',
        'PHASE 3 Verify',
        'PHASE 4 Repoint and restart',
        'PHASE 5 Post-checks',
        'PHASE 6 Rollback'
    )
    $last = -1
    foreach ($phase in $phases) {
        $at = $Text.IndexOf($phase, [System.StringComparison]::Ordinal)
        if ($at -lt 0 -or $at -le $last) { return "missing-or-out-of-order $phase at $at" }
        $last = $at
    }
    for ($n = 0; $n -le 6; $n++) {
        $gate = 'STOP GATE {0}:' -f $n
        $phase = $phases[$n]
        $phaseAt = $Text.IndexOf($phase, [System.StringComparison]::Ordinal)
        $gateAt = $Text.IndexOf($gate, $phaseAt, [System.StringComparison]::Ordinal)
        $nextAt = $Text.Length
        if ($n -lt 6) { $nextAt = $Text.IndexOf($phases[$n + 1], [System.StringComparison]::Ordinal) }
        if ($gateAt -lt 0 -or $gateAt -ge $nextAt) { return "stop gate $n not inside its phase" }
    }
    return $null
}

function Test-W1 {
    $fake = New-C1407FakeBin
    try {
        $plain = Invoke-C1407Cutover -Fake $fake -Arguments @('-ApiBase', $script:ClosedApi)
        $again = Invoke-C1407Cutover -Fake $fake -Arguments @('-WhatIf', '-ApiBase', $script:ClosedApi)
        $order = Test-PhaseOrder $plain.Text
        $needles = @(
            'verified snapshot reference',
            'passed drill record',
            'latency',
            'landPending',
            '17204',
            'pg_dump -Fc',
            'pg_restore',
            'count(*)',
            'ExpectedServerSha',
            'desktop volume',
            'user-secrets',
            'maintenance mode is entered',
            '-AcceptDataLossRollback'
        )
        $missing = @($needles | Where-Object { $plain.Text -notmatch [regex]::Escape($_) })
        $clean = [string]::IsNullOrWhiteSpace($plain.Trace) -and [string]::IsNullOrWhiteSpace($again.Trace)
        $noSecret = ($plain.Text -notmatch 'Password=') -and ($plain.Text -notmatch 'Host=')
        $phase1 = $plain.Text.IndexOf('PHASE 1 Quiesce', [System.StringComparison]::Ordinal)
        $phase2 = $plain.Text.IndexOf('PHASE 2 Final dump and restore', [System.StringComparison]::Ordinal)
        $phase6 = $plain.Text.IndexOf('PHASE 6 Rollback', [System.StringComparison]::Ordinal)
        $maintAt = $plain.Text.IndexOf('maintenance mode is entered', [System.StringComparison]::Ordinal)
        $lossAt = $plain.Text.IndexOf('-AcceptDataLossRollback', [System.StringComparison]::Ordinal)
        $placed = ($maintAt -gt $phase1) -and ($maintAt -lt $phase2) -and ($lossAt -gt $phase6)
        $ok = ($plain.ExitCode -eq 0) -and ($again.ExitCode -eq 0) -and
            ($null -eq $order) -and ($missing.Count -eq 0) -and $clean -and $noSecret -and $placed -and
            ($plain.Text -match 'does not POST') -and
            ($plain.Text -match 'does not call docker') -and
            ($plain.Text -match 'does not call dotnet user-secrets set')
        if ($ok) { Write-Pass 'W-1' }
        else {
            Write-Fail 'W-1' ("exit=$($plain.ExitCode)/$($again.ExitCode) order=$order missing=$($missing -join ',') trace=$($plain.Trace) $($plain.Text)")
        }
    } catch {
        Write-Fail 'W-1' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-W2 {
    $fake = New-C1407FakeBin
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ApiBase', $script:ClosedApi)
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'ConfirmWindow') -and
            [string]::IsNullOrWhiteSpace($r.Trace) -and
            ($r.Text -notmatch 'Password=')
        if ($ok) { Write-Pass 'W-2' }
        else { Write-Fail 'W-2' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-2' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-W3 {
    $fake = New-C1407FakeBin
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token), '-ApiBase', $script:ClosedApi)
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'snapshot reference') -and
            [string]::IsNullOrWhiteSpace($r.Trace) -and
            ($r.Text -notmatch 'STOP-ONLY') -and
            ($r.Text -notmatch 'Password=')
        if ($ok) { Write-Pass 'W-3' }
        else { Write-Fail 'W-3' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-3' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-W4 {
    $fake = New-C1407FakeBin
    $snap = New-C1407File -Body 'snapshot-reference'
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token), '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap
        )
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'drill record') -and
            [string]::IsNullOrWhiteSpace($r.Trace)
        if ($ok) { Write-Pass 'W-4' }
        else { Write-Fail 'W-4' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-4' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
        Remove-C1407Tree $snap
    }
}

function Test-W5 {
    $path = Join-Path $here 'cutover-postgres-server2.ps1'
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Fail 'W-5' 'cutover script missing'
        return
    }
    $text = Get-Content -LiteralPath $path -Raw
    $hits = @()
    foreach ($needle in @('volume rm', 'docker volume rm', '-AllowWorktree')) {
        if ($text.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { $hits += $needle }
    }
    if ($hits.Count -eq 0) { Write-Pass 'W-5' }
    else { Write-Fail 'W-5' ($hits -join ', ') }
}

function Test-C1407LandRefusal {
    param($Run, [string]$Label)
    $reason = 'REFUSED: landPending is not empty. StopOnly was not run.'
    if (-not $Run.Ready) { return "$Label not-ready $($Run.Error)" }
    if ($Run.ExitCode -eq 0) { return "$Label exit=0" }
    if ($Run.Text.IndexOf($reason, [System.StringComparison]::Ordinal) -lt 0) {
        return "$Label missing-reason $(Get-C1407SafeText $Run.Text)"
    }
    if (-not (Test-C1407HoldReleased $Run.Lines)) { return "$Label hold-not-released" }
    return $null
}

function Test-W6 {
    $reasons = @()
    $live = Invoke-C1407WindowRun -PendingJson '{"items":[{"id":"land-1"}]}'
    $liveReason = Test-C1407LandRefusal -Run $live -Label 'live'
    if ($liveReason) { $reasons += $liveReason }
    $filled = Invoke-C1407WindowRun -PendingJson '{"items":[]}' -WithFixture -FixtureJson '{"items":[{"id":"land-1"}]}'
    $filledReason = Test-C1407LandRefusal -Run $filled -Label 'fixture'
    if ($filledReason) { $reasons += $filledReason }
    $empty = Invoke-C1407WindowRun -PendingJson '{"items":[{"id":"land-1"}]}' -WithFixture -FixtureJson '{"items":[]}'
    if (-not $empty.Ready) { $reasons += "empty not-ready $($empty.Error)" }
    elseif ($empty.Text.IndexOf('PHASE 1 landPending is empty.', [System.StringComparison]::Ordinal) -lt 0) {
        $reasons += "empty missing-pass $(Get-C1407SafeText $empty.Text)"
    } else {
        $sawStop = $false
        foreach ($line in @($empty.Lines)) {
            if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-StopOnly')) {
                $sawStop = $true
            }
        }
        if (-not $sawStop) { $reasons += 'empty did not reach StopOnly' }
        if ($empty.ExitCode -ne 0) { $reasons += "empty exit=$($empty.ExitCode) $(Get-C1407SafeText $empty.Text)" }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-6' }
    else { Write-Fail 'W-6' ($reasons -join ' || ') }
}

function Test-W7 {
    $harness = Join-Path $here 'test-apphost-graceful-stop.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $reasons = @()
    foreach ($id in @('T9', 'T10', 'T12')) {
        $output = @(& $pwsh -NoProfile -File $harness -Case $id 2>&1 | ForEach-Object { $_.ToString() })
        $text = $output -join "`n"
        $code = $LASTEXITCODE
        if ($code -ne 0 -or $text -notmatch 'PASS ') {
            $reasons += ("{0} exit={1} {2}" -f $id, $code, $text)
        }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-7' }
    else { Write-Fail 'W-7' ($reasons -join ' || ') }
}

function Test-W8 {
    $files = @(
        (Join-Path $here 'cutover-postgres-server2.ps1'),
        (Join-Path $here 'test-cutover-postgres-server2.ps1'),
        (Join-Path $here 'restart-apphost.ps1'),
        (Join-Path $here 'keep-six.ps1')
    )
    $reasons = @()
    foreach ($f in $files) {
        if (-not (Test-Path -LiteralPath $f)) { $reasons += ('missing {0}' -f (Split-Path $f -Leaf)); continue }
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $high = @($bytes | Where-Object { $_ -gt 0x7F }).Count
        if ($high -ne 0) { $reasons += ('ASCII {0} high={1}' -f (Split-Path $f -Leaf), $high) }
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$errs)
        $n = 0
        if ($null -ne $errs) { $n = @($errs).Count }
        if ($n -ne 0) { $reasons += ('parse {0} errors={1}' -f (Split-Path $f -Leaf), $n) }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-8' }
    else { Write-Fail 'W-8' ($reasons -join '; ') }
}

function Test-W9 {
    $fake = New-C1407FakeBin
    $snap = New-C1407File -Body 'snapshot-reference'
    $slow = New-C1407Drill -Latency 50
    $failed = New-C1407Drill -Passed $false
    $long = New-C1407Drill -DumpRestoreSec 700 -CopySec 10
    try {
        $latency = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token), '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $slow
        )
        $notPassed = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token), '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $failed
        )
        $envelope = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token), '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $long
        )
        $ok = ($latency.ExitCode -ne 0) -and ($latency.Text -match 'latency') -and
            ($notPassed.ExitCode -ne 0) -and ($notPassed.Text -match 'drill') -and
            ($envelope.ExitCode -ne 0) -and ($envelope.Text -match '600') -and
            [string]::IsNullOrWhiteSpace($latency.Trace) -and
            [string]::IsNullOrWhiteSpace($notPassed.Trace) -and
            [string]::IsNullOrWhiteSpace($envelope.Trace)
        if ($ok) { Write-Pass 'W-9' }
        else { Write-Fail 'W-9' ("latency=$($latency.ExitCode) drill=$($notPassed.ExitCode) time=$($envelope.ExitCode) $($latency.Text) $($envelope.Text)") }
    } catch {
        Write-Fail 'W-9' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
        Remove-C1407Tree $snap
        Remove-C1407Tree $slow
        Remove-C1407Tree $failed
        Remove-C1407Tree $long
    }
}

function Test-W10 {
    $fake = New-C1407FakeBin
    try {
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Rollback', '-Execute', '-ApiBase', $script:ClosedApi)
        $ok = ($r.ExitCode -ne 0) -and
            ($r.Text -match 'ConfirmWindow') -and
            [string]::IsNullOrWhiteSpace($r.Trace)
        if ($ok) { Write-Pass 'W-10' }
        else { Write-Fail 'W-10' ("exit=$($r.ExitCode) trace=$($r.Trace) $($r.Text)") }
    } catch {
        Write-Fail 'W-10' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-C1407SecretPath {
    param($Run)
    $sentinel = 'SENTINEL-1407-ARGV'
    $identityToken = 'identity-sentinel-1407'
    $identityBody = 'AGE-IDENTITY-BODY-1407'
    $reasons = @()
    if (-not $Run.Ready) { return @('not-ready') }
    $sawSet = $false
    $sawAgeR = $false
    $sawScpAge = $false
    $sawPlainScp = $false
    $sawDecrypt = $false
    $enterAt = -1
    $stopAt = -1
    $restartAt = -1
    $clearAt = -1
    $trace = @($Run.Lines)
    for ($i = 0; $i -lt $trace.Count; $i++) {
        $line = [string]$trace[$i]
        $parts = @($line.Split([char]9))
        if ($parts.Count -ge 1 -and $parts[0] -eq 'dotnet' -and (@($parts) -contains 'user-secrets') -and (@($parts) -contains 'set')) {
            $sawSet = $true
        }
        if ($parts.Count -ge 1 -and $parts[0] -eq 'age') {
            foreach ($part in $parts) {
                if ($part -eq '-R' -or $part.StartsWith('-R')) { $sawAgeR = $true }
            }
        }
        if ($parts.Count -ge 1 -and $parts[0] -eq 'scp') {
            foreach ($part in $parts) {
                if ($part.EndsWith('.age')) { $sawScpAge = $true }
                if ($part.EndsWith('.dump')) { $sawPlainScp = $true }
            }
        }
        if ((Test-C1407Contains -Line $line -Text 'age -d -i -')) { $sawDecrypt = $true }
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and -not (Test-C1407Field -Line $line -Token '-Clear')) {
            if ($enterAt -lt 0) { $enterAt = $i }
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-StopOnly')) {
            if ($stopAt -lt 0) { $stopAt = $i }
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-ExpectedServerSha')) {
            if ($restartAt -lt 0) { $restartAt = $i }
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
            $clearAt = $i
        }
        foreach ($part in $parts) {
            if ($part.Contains($sentinel)) { $reasons += 'argv-has-sentinel' }
            if ($part.Contains($identityToken)) { $reasons += 'argv-has-identity-path' }
        }
    }
    if (-not $sawSet) { $reasons += 'dotnet-set-not-called' }
    if (-not $sawAgeR) { $reasons += 'age-recipient-missing' }
    if (-not $sawScpAge -or $sawPlainScp) { $reasons += 'scp-not-age-only' }
    if (-not $sawDecrypt) { $reasons += 'ssh-decrypt-missing' }
    if ($enterAt -lt 0 -or $stopAt -le $enterAt -or $restartAt -le $stopAt -or $clearAt -le $restartAt) {
        $reasons += 'maintenance-order'
    }
    if ($Run.Text.Contains($sentinel)) { $reasons += 'stdout-has-sentinel' }
    if ($Run.Text.Contains($identityToken)) { $reasons += 'stdout-has-identity-path' }
    if ($Run.Text.Contains($identityBody)) { $reasons += 'stdout-has-identity-body' }
    $sawDotnetStdin = $false
    $sawSshIdentity = $false
    foreach ($file in @($Run.Stdin)) {
        $body = [string]$file.Body
        if ($body.Contains($sentinel) -and $file.Name -notlike 'dotnet.*') { $reasons += 'other-stdin-has-sentinel' }
        if ($body.Contains($identityToken)) { $reasons += 'stdin-has-identity-path' }
        if ($file.Name -like 'dotnet.*' -and $body.Contains($sentinel)) { $sawDotnetStdin = $true }
        if ($file.Name -like 'ssh.*' -and $body.Contains($identityBody)) { $sawSshIdentity = $true }
    }
    if (-not $sawDotnetStdin) { $reasons += 'dotnet-stdin-missing-sentinel' }
    if (-not $sawSshIdentity) { $reasons += 'ssh-stdin-missing-identity' }
    return @($reasons | Select-Object -Unique)
}

function Test-W11 {
    $run = Invoke-C1407WindowRun
    $reasons = @(Test-C1407SecretPath $run)
    if ($run.ExitCode -ne 0) { $reasons += 'exit-nonzero' }
    if ($reasons.Count -eq 0) { Write-Pass 'W-11' }
    else { Write-Fail 'W-11' ('exit={0} reasons={1} text={2}' -f $run.ExitCode, ($reasons -join ','), (Get-C1407SafeText $run.Text)) }
}

function Test-W12 {
    # An abbreviation never opens the window. -Confirm and -Con are ambiguous between -ConfirmWindow
    # and -ConfirmToken (a binder error before the script runs); -ConfirmW binds the switch but has
    # no token (CARD-1417, Review d6b441d2); the exact switch plus the preview's token opens it.
    $fake = New-C1407FakeBin
    try {
        $confirm = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-Confirm', '-ApiBase', $script:ClosedApi)
        $con = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-Con', '-ApiBase', $script:ClosedApi)
        $short = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ConfirmW', '-ApiBase', $script:ClosedApi)
        $full = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', (Get-C1407Token), '-ApiBase', $script:ClosedApi)
        $confirmOk = ($confirm.ExitCode -ne 0) -and
            [string]::IsNullOrWhiteSpace($confirm.Trace) -and
            ($confirm.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -lt 0) -and
            ($confirm.Text.IndexOf('ambiguous', [System.StringComparison]::Ordinal) -ge 0)
        $conOk = ($con.ExitCode -ne 0) -and
            [string]::IsNullOrWhiteSpace($con.Trace) -and
            ($con.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -lt 0) -and
            ($con.Text.IndexOf('ambiguous', [System.StringComparison]::Ordinal) -ge 0)
        $shortOk = ($short.ExitCode -eq 2) -and
            ($short.Text.IndexOf('REFUSED: -Execute requires -ConfirmToken', [System.StringComparison]::Ordinal) -ge 0) -and
            [string]::IsNullOrWhiteSpace($short.Trace) -and
            ($short.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -lt 0)
        $fullOk = ($full.ExitCode -ne 0) -and
            ($full.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -ge 0) -and
            ($full.Text.IndexOf('requires -Confirm', [System.StringComparison]::Ordinal) -lt 0) -and
            [string]::IsNullOrWhiteSpace($full.Trace)
        if ($confirmOk -and $conOk -and $shortOk -and $fullOk) { Write-Pass 'W-12' }
        else {
            Write-Fail 'W-12' ("confirm=$($confirm.ExitCode) con=$($con.ExitCode) short=$($short.ExitCode) full=$($full.ExitCode) $(Get-C1407SafeText $confirm.Text) $(Get-C1407SafeText $con.Text) $(Get-C1407SafeText $short.Text) $(Get-C1407SafeText $full.Text)")
        }
    } catch {
        Write-Fail 'W-12' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
    }
}

function Test-C1407RollbackProceeds {
    param($Run, [string]$Label)
    if (-not $Run.Ready) { return "$Label not-ready" }
    if ($Run.ExitCode -ne 0) { return "$Label exit=$($Run.ExitCode) $(Get-C1407SafeText $Run.Text)" }
    $sawRemove = $false
    $sawRestart = $false
    $sawClear = $false
    foreach ($line in @($Run.Lines)) {
        $parts = @($line.Split([char]9))
        if ($parts.Count -ge 1 -and $parts[0] -eq 'dotnet' -and (@($parts) -contains 'remove')) { $sawRemove = $true }
        if ((Test-C1407Field -Line $line -Token '-ExpectedServerSha')) { $sawRestart = $true }
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
            $sawClear = $true
        }
        foreach ($part in $parts) {
            if ($part.Contains('SENTINEL-1407-ARGV')) { return "$Label argv-has-sentinel" }
        }
    }
    if (-not $sawRemove -or -not $sawRestart -or -not $sawClear) { return "$Label missing-proceed" }
    if ($Run.Text.IndexOf('untouched desktop volume', [System.StringComparison]::Ordinal) -lt 0) {
        return "$Label missing-exit-check"
    }
    return $null
}

function Test-W13 {
    $reasons = @()
    $refused = Invoke-C1407WindowRun -SkipApi -SkipTcp -SourceCount '3' -TargetCount '9' -ExtraArgs @('-Rollback')
    if (-not $refused.Ready) { $reasons += "refuse not-ready $($refused.Error)" }
    else {
        $named = 'REFUSED: stop gate 6: the new database has rows the old desktop volume lacks.'
        if ($refused.ExitCode -eq 0 -or $refused.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "refuse exit=$($refused.ExitCode) $(Get-C1407SafeText $refused.Text)"
        }
        foreach ($line in @($refused.Lines)) {
            $parts = @($line.Split([char]9))
            if ($parts.Count -ge 1 -and $parts[0] -eq 'dotnet') { $reasons += 'refuse called-dotnet' }
            if (Test-C1407Field -Line $line -Token '-ExpectedServerSha') { $reasons += 'refuse restarted' }
            if (Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') { $reasons += 'refuse entered-maintenance' }
        }
    }
    $accepted = Invoke-C1407WindowRun -SkipApi -SkipTcp -SourceCount '3' -TargetCount '9' -ExtraArgs @('-Rollback', '-AcceptDataLossRollback')
    $acceptedReason = Test-C1407RollbackProceeds -Run $accepted -Label 'accept'
    if ($acceptedReason) { $reasons += $acceptedReason }
    elseif ($accepted.Text.IndexOf('-AcceptDataLossRollback accepted', [System.StringComparison]::Ordinal) -lt 0) {
        $reasons += 'accept missing-switch-text'
    }
    $equal = Invoke-C1407WindowRun -SkipApi -SkipTcp -SourceCount '4' -TargetCount '4' -ExtraArgs @('-Rollback')
    $equalReason = Test-C1407RollbackProceeds -Run $equal -Label 'equal'
    if ($equalReason) { $reasons += $equalReason }
    if ($reasons.Count -eq 0) { Write-Pass 'W-13' }
    else { Write-Fail 'W-13' ($reasons -join ' || ') }
}

function Test-W14 {
    $run = Invoke-C1407WindowRun -FailMaintenance '1'
    $reasons = @()
    if (-not $run.Ready) { $reasons += "not-ready $($run.Error)" }
    else {
        $named = 'REFUSED: AppHost maintenance enter exit check failed.'
        if ($run.ExitCode -eq 0 -or $run.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "exit=$($run.ExitCode) $(Get-C1407SafeText $run.Text)"
        }
        $sawEnter = $false
        foreach ($line in @($run.Lines)) {
            if (Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') { $sawEnter = $true }
            if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-StopOnly')) {
                $reasons += 'reached-stoponly'
            }
        }
        if (-not $sawEnter) { $reasons += 'maintenance-not-called' }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-14' }
    else { Write-Fail 'W-14' ($reasons -join ' || ') }
}

function Test-C1407FailureLift {
    param(
        $Run,
        [string]$Label,
        [string]$Reason,
        [int]$ExitCode = 3,
        [ValidateSet('none', 'old-string', 'secret-changed')]
        [string]$Restart = 'none'
    )
    $reasons = @()
    if (-not $Run.Ready) { return @("$Label not-ready $($Run.Error)") }
    if ($Run.ExitCode -ne $ExitCode) { $reasons += "$Label exit=$($Run.ExitCode)" }
    if ($Run.Text.IndexOf($Reason, [System.StringComparison]::Ordinal) -lt 0) {
        $reasons += "$Label missing-reason"
    }
    $sha = '0123456789abcdef0123456789abcdef01234567'
    $enterAt = -1
    $clearAt = -1
    $holdAt = -1
    $clearHoldAt = -1
    $stopAt = -1
    $oldAt = -1
    $oldCount = 0
    $sawSha = $false
    $items = @($Run.Lines)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $line = [string]$items[$i]
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and -not (Test-C1407Field -Line $line -Token '-Clear')) {
            if ($enterAt -lt 0) { $enterAt = $i }
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
            $clearAt = $i
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'keep-six.ps1') -and (Test-C1407Field -Line $line -Token '-Hold') -and $holdAt -lt 0) {
            $holdAt = $i
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'keep-six.ps1') -and (Test-C1407Field -Line $line -Token '-ClearHold')) {
            $clearHoldAt = $i
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-StopOnly') -and $stopAt -lt 0) {
            $stopAt = $i
        }
        if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-ExpectedServerSha')) {
            $oldCount++
            $oldAt = $i
            $parts = @($line.Split([char]9))
            for ($p = 0; $p -lt ($parts.Count - 1); $p++) {
                if ($parts[$p] -eq '-ExpectedServerSha' -and $parts[$p + 1] -eq $sha) { $sawSha = $true }
            }
        }
    }
    if ($enterAt -lt 0) { $reasons += "$Label maintenance-not-entered" }
    if ($clearAt -le $enterAt) { $reasons += "$Label maintenance-not-cleared" }
    if ($holdAt -lt 0 -or $clearHoldAt -le $holdAt) { $reasons += "$Label hold-not-cleared" }
    if ($Restart -eq 'none') {
        if ($stopAt -ge 0) { $reasons += "$Label reached-stoponly" }
        if ($oldCount -ne 0) { $reasons += "$Label restarted" }
        if ($Run.Text.IndexOf('RECOVERY: StopOnly did not complete.', [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "$Label missing-instruction"
        }
    } elseif ($Restart -eq 'old-string') {
        if ($stopAt -lt 0 -or $oldAt -le $stopAt) { $reasons += "$Label old-restart-missing" }
        if ($oldCount -ne 1) { $reasons += "$Label old-restart-count=$oldCount" }
        if (-not $sawSha) { $reasons += "$Label old-restart-sha" }
        if ($clearAt -le $oldAt) { $reasons += "$Label clear-before-restart" }
        $instruction = 'RECOVERY: restarted AppHost on the old connection string before lifting maintenance.'
        if ($Run.Text.IndexOf($instruction, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "$Label missing-instruction"
        }
    } else {
        if ($stopAt -lt 0) { $reasons += "$Label stoponly-missing" }
        if ($oldCount -ne 1 -or -not $sawSha) { $reasons += "$Label restart-count=$oldCount" }
        if ($clearAt -le $oldAt) { $reasons += "$Label clear-before-restart" }
        $instruction = 'RECOVERY: the connection secret was already changed. This run does not restart on the old string.'
        if ($Run.Text.IndexOf($instruction, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "$Label missing-instruction"
        }
    }
    if ($Run.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += "$Label printed-sentinel" }
    return @($reasons)
}

function Test-W15 {
    $reason = 'REFUSED: GET /api/agent-tasks?projectId=00000000-0000-0000-0000-0000000001407&landPending=true failed. Body not printed. StopOnly was not run.'
    $run = Invoke-C1407WindowRun -LandPendingStatus 500 -PendingJson '{"items":[]}'
    $reasons = @(Test-C1407FailureLift -Run $run -Label 'get' -Reason $reason -Restart none)
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-15' }
    else { Write-Fail 'W-15' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text)) }
}

function Test-W16 {
    $reason = 'REFUSED: pg_dump -Fc or age encrypt failed. The secret was not changed.'
    $run = Invoke-C1407WindowRun -FailDump '1'
    $reasons = @(Test-C1407FailureLift -Run $run -Label 'dump' -Reason $reason -Restart old-string)
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-16' }
    else { Write-Fail 'W-16' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text)) }
}

function Test-W17 {
    $reason = 'REFUSED: phase 3 mismatch for count public.Sample. Row text was not printed. Do not repoint.'
    $run = Invoke-C1407WindowRun -SourceCount '3' -TargetCount '9'
    $reasons = @(Test-C1407FailureLift -Run $run -Label 'phase3' -Reason $reason -Restart old-string)
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-17' }
    else { Write-Fail 'W-17' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text)) }
}

function Test-W18 {
    $reason = 'REFUSED: user-secrets set failed. Value not displayed.'
    $run = Invoke-C1407WindowRun -FailSecret '1'
    $reasons = @(Test-C1407FailureLift -Run $run -Label 'secret' -Reason $reason -Restart old-string)
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-18' }
    else { Write-Fail 'W-18' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text)) }
}

function Test-W19 {
    $reason = 'REFUSED: restart exited 5. Do not start a second copy.'
    $run = Invoke-C1407WindowRun -FailRestart '1'
    $reasons = @(Test-C1407FailureLift -Run $run -Label 'restart' -Reason $reason -Restart secret-changed)
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-19' }
    else { Write-Fail 'W-19' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text)) }
}

function Test-C1407ForeignMarker {
    param($Run, [string]$Label, [string]$Reason, [string]$Marker)
    $reasons = @()
    if (-not $Run.Ready) { return @("$Label not-ready $($Run.Error)") }
    if ($Run.ExitCode -eq 0) { $reasons += "$Label exit=0" }
    if ($Run.ExitCode -ne 3) { $reasons += "$Label exit=$($Run.ExitCode)" }
    if ($Run.Text.IndexOf($Reason, [System.StringComparison]::Ordinal) -lt 0) {
        $reasons += "$Label missing-reason"
    }
    foreach ($line in @($Run.Lines)) {
        if (Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') { $reasons += "$Label called-maintenance" }
        if (Test-C1407Field -Line $line -Token '-ClearHold') { $reasons += "$Label cleared-hold" }
        if (Test-C1407Field -Line $line -Token '-StopOnly') { $reasons += "$Label reached-stoponly" }
    }
    if (-not (Test-Path -LiteralPath $Marker)) { $reasons += "$Label removed-marker" }
    if ($Run.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += "$Label printed-sentinel" }
    return @($reasons)
}

function New-C1407TempRepo {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-repo-' + [guid]::NewGuid().ToString('N'))
    $scripts = Join-Path $dir 'scripts'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $dir 'logs') -Force | Out-Null
    foreach ($name in @('restart-apphost.ps1', 'set-apphost-maintenance.ps1', 'keep-six.ps1', 'recover-repository-children.ps1')) {
        Set-Content -LiteralPath (Join-Path $scripts $name) -Value "# fixture`n" -Encoding ascii
    }
    Set-Content -LiteralPath (Join-Path $dir 'docker-compose.dev.yml') -Value "services: {}`n" -Encoding ascii
    $csprojDir = Join-Path $dir 'Antiphon.AppHost'
    New-Item -ItemType Directory -Path $csprojDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $csprojDir 'Antiphon.AppHost.csproj') -Value "<Project></Project>`n" -Encoding ascii
    return $dir
}

function Test-W20 {
    $repo = New-C1407TempRepo
    $marker = Join-Path (Join-Path $repo 'logs') 'apphost.down-on-purpose'
    $reason = 'REFUSED: maintenance marker already exists at logs/apphost.down-on-purpose. This run did not enter it. The operator must clear the marker before the window can start.'
    Set-Content -LiteralPath $marker -Value 'down-on-purpose pre-existing' -Encoding ascii
    $reasons = @()
    try {
        $window = Invoke-C1407WindowRun -Repository $repo
        $reasons += @(Test-C1407ForeignMarker -Run $window -Label 'window' -Reason $reason -Marker $marker)
        $rollback = Invoke-C1407WindowRun -Repository $repo -ExtraArgs @('-Rollback')
        $reasons += @(Test-C1407ForeignMarker -Run $rollback -Label 'rollback' -Reason $reason -Marker $marker)
    } catch {
        $reasons += $_.Exception.Message
    } finally {
        Remove-C1407Tree $repo
    }
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-20' }
    else { Write-Fail 'W-20' ($reasons -join ' || ') }
}

function Test-W21 {
    $reasons = @()
    $rawReason = 'REFUSED: landPending response was not JSON. Body not printed. StopOnly was not run.'
    $raw = Invoke-C1407WindowRun -PendingJson 'not-json'
    $reasons += @(Test-C1407FailureLift -Run $raw -Label 'raw' -Reason $rawReason -Restart none)
    $emptyReason = 'REFUSED: landPending response has no items array. Body not printed. StopOnly was not run.'
    $empty = Invoke-C1407WindowRun -PendingJson '{}'
    $reasons += @(Test-C1407FailureLift -Run $empty -Label 'empty-object' -Reason $emptyReason -Restart none)
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-21' }
    else { Write-Fail 'W-21' ($reasons -join ' || ') }
}

function Test-C1407RestartIndex {
    param($Lines, [string]$Token)
    $items = @($Lines)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $line = [string]$items[$i]
        if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token $Token)) {
            return $i
        }
    }
    return -1
}

function Test-W22 {
    $token = 'RESTART-STDOUT-1407'
    $run = Invoke-C1407WindowRun -RestartStdout $token
    $reasons = @()
    if (-not $run.Ready) { $reasons += "not-ready $($run.Error)" }
    else {
        if ($run.ExitCode -ne 0) { $reasons += "exit=$($run.ExitCode)" }
        $stopOnly = 'REFUSED: StopOnly exited'
        $restart = 'REFUSED: restart exited'
        if ($run.Text.IndexOf($stopOnly, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'refused-stoponly' }
        if ($run.Text.IndexOf($restart, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'refused-restart' }
        if ($run.Text.IndexOf('PHASE 4 restart exited 0.', [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += 'missing-phase4'
        }
        if ((Test-C1407RestartIndex -Lines $run.Lines -Token '-StopOnly') -lt 0) { $reasons += 'missing-stoponly' }
        if ((Test-C1407RestartIndex -Lines $run.Lines -Token '-ExpectedServerSha') -lt 0) { $reasons += 'missing-restart' }
        if ($run.Text.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'stdout-not-shown' }
        if ($run.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += 'printed-sentinel' }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-22' }
    else { Write-Fail 'W-22' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text)) }
}

function Test-W23 {
    $token = 'RESTART-STDOUT-1407'
    $reasons = @()
    $stop = Invoke-C1407WindowRun -RestartStdout $token -StopOnlyExit '4'
    if (-not $stop.Ready) { $reasons += "stop not-ready $($stop.Error)" }
    else {
        $named = 'REFUSED: StopOnly exited 4. The old database stays in place.'
        $polluted = 'REFUSED: StopOnly exited RESTART-STDOUT-1407'
        if ($stop.ExitCode -ne 3 -or $stop.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "stop exit=$($stop.ExitCode)"
        }
        if ($stop.Text.IndexOf($polluted, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'stop-polluted' }
        if ($stop.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += 'stop-sentinel' }
    }
    $later = Invoke-C1407WindowRun -RestartStdout $token -ShaRestartExit '5'
    if (-not $later.Ready) { $reasons += "later not-ready $($later.Error)" }
    else {
        $named = 'REFUSED: restart exited 5. Do not start a second copy.'
        $polluted = 'REFUSED: restart exited RESTART-STDOUT-1407'
        if ($later.ExitCode -ne 3 -or $later.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "later exit=$($later.ExitCode)"
        }
        if ($later.Text.IndexOf($polluted, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'later-polluted' }
        $kept = 'RECOVERY: the connection secret was already changed. This run does not restart on the old string.'
        if ($later.Text.IndexOf($kept, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'later-missing-recovery' }
        if ($later.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += 'later-sentinel' }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-23' }
    else { Write-Fail 'W-23' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $stop.Text) + ' ' + (Get-C1407SafeText $later.Text)) }
}

function Test-W24 {
    $token = 'RESTART-STDOUT-1407'
    $reasons = @()
    $ok = Invoke-C1407WindowRun -RestartStdout $token -SourceSessions '1'
    if (-not $ok.Ready) { $reasons += "ok not-ready $($ok.Error)" }
    else {
        $named = 'REFUSED: dump refused because another session is connected. Restart exit 0.'
        $polluted = 'Restart exit RESTART-STDOUT-1407'
        if ($ok.ExitCode -ne 3 -or $ok.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "ok exit=$($ok.ExitCode)"
        }
        if ($ok.Text.IndexOf($polluted, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'ok-polluted' }
        if ($ok.Text.IndexOf('AppHost is still down.', [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'ok-treated-as-down' }
        $stopAt = Test-C1407RestartIndex -Lines $ok.Lines -Token '-StopOnly'
        $oldAt = Test-C1407RestartIndex -Lines $ok.Lines -Token '-ExpectedServerSha'
        $clearAt = -1
        $items = @($ok.Lines)
        for ($i = 0; $i -lt $items.Count; $i++) {
            $line = [string]$items[$i]
            if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
                $clearAt = $i
            }
        }
        if ($stopAt -lt 0 -or $oldAt -le $stopAt -or $clearAt -le $oldAt) { $reasons += "ok-order stop=$stopAt old=$oldAt clear=$clearAt" }
    }
    $bad = Invoke-C1407WindowRun -RestartStdout $token -SourceSessions '1' -ShaRestartExit '5'
    if (-not $bad.Ready) { $reasons += "bad not-ready $($bad.Error)" }
    else {
        $named = 'REFUSED: dump refused because another session is connected. Restart exit 5.'
        $polluted = 'Restart exit RESTART-STDOUT-1407'
        if ($bad.ExitCode -ne 3 -or $bad.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "bad exit=$($bad.ExitCode)"
        }
        if ($bad.Text.IndexOf($polluted, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'bad-polluted' }
        if ($bad.Text.IndexOf('RECOVERY: AppHost is still down.', [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += 'bad-missing-down'
        }
        $sawClear = $false
        foreach ($line in @($bad.Lines)) {
            if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
                $sawClear = $true
            }
        }
        if (-not $sawClear) { $reasons += 'bad-not-cleared' }
    }
    $stopped = Invoke-C1407WindowRun -FailSecret '1'
    if (-not $stopped.Ready) { $reasons += "stopped not-ready $($stopped.Error)" }
    else {
        $startAt = -1
        $restartAt = -1
        $items = @($stopped.Lines)
        for ($i = 0; $i -lt $items.Count; $i++) {
            $line = [string]$items[$i]
            if ($startAt -lt 0 -and (Test-C1407Field -Line $line -Token 'start') -and (Test-C1407Field -Line $line -Token 'postgres')) {
                $startAt = $i
            }
            if ((Test-C1407Suffix -Line $line -Suffix 'restart-apphost.ps1') -and (Test-C1407Field -Line $line -Token '-ExpectedServerSha')) {
                $restartAt = $i
            }
        }
        if ($startAt -lt 0 -or $restartAt -le $startAt) { $reasons += "stopped-order start=$startAt restart=$restartAt" }
        $instruction = 'RECOVERY: restarted AppHost on the old connection string before lifting maintenance.'
        if ($stopped.Text.IndexOf($instruction, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'stopped-missing-recovery' }
        if ($stopped.ExitCode -ne 3) { $reasons += "stopped exit=$($stopped.ExitCode)" }
    }
    $down = Invoke-C1407WindowRun -FailSecret '1' -FailComposeStart '1'
    if (-not $down.Ready) { $reasons += "down not-ready $($down.Error)" }
    else {
        if ((Test-C1407RestartIndex -Lines $down.Lines -Token '-ExpectedServerSha') -ge 0) { $reasons += 'down-restarted' }
        $instruction = 'RECOVERY: desktop postgres did not start. Run docker compose -f docker-compose.dev.yml start postgres, then restart-apphost.ps1 -ExpectedServerSha. The secret was not changed.'
        if ($down.Text.IndexOf($instruction, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'down-missing-instruction' }
        if ($down.Text.IndexOf('REFUSED: user-secrets set failed.', [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'down-lost-refusal' }
        $sawClear = $false
        foreach ($line in @($down.Lines)) {
            if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
                $sawClear = $true
            }
        }
        if (-not $sawClear) { $reasons += 'down-not-cleared' }
        if ($down.ExitCode -ne 3) { $reasons += "down exit=$($down.ExitCode)" }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-24' }
    else { Write-Fail 'W-24' ($reasons -join ' || ') }
}

function Test-W25 {
    $token = 'RESTART-STDOUT-1407'
    $reasons = @()
    $ok = Invoke-C1407WindowRun -SkipApi -SkipTcp -RestartStdout $token -ExtraArgs @('-Rollback') -SourceCount '4' -TargetCount '4'
    if (-not $ok.Ready) { $reasons += "ok not-ready $($ok.Error)" }
    else {
        if ($ok.ExitCode -ne 0) { $reasons += "ok exit=$($ok.ExitCode)" }
        if ($ok.Text.IndexOf('untouched desktop volume', [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'ok-missing-exit' }
        if ($ok.Text.IndexOf('REFUSED: rollback restart exited', [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'ok-refused' }
        if ($ok.Text.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'ok-stdout-hidden' }
        $proceed = Test-C1407RollbackProceeds -Run $ok -Label 'ok'
        if ($proceed) { $reasons += $proceed }
    }
    $bad = Invoke-C1407WindowRun -SkipApi -SkipTcp -RestartStdout $token -ShaRestartExit '5' -ExtraArgs @('-Rollback') -SourceCount '4' -TargetCount '4'
    if (-not $bad.Ready) { $reasons += "bad not-ready $($bad.Error)" }
    else {
        $named = 'REFUSED: rollback restart exited 5. Do not start a second copy if the lock is held.'
        $polluted = 'REFUSED: rollback restart exited RESTART-STDOUT-1407'
        if ($bad.ExitCode -ne 3 -or $bad.Text.IndexOf($named, [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += "bad exit=$($bad.ExitCode)"
        }
        if ($bad.Text.IndexOf($polluted, [System.StringComparison]::Ordinal) -ge 0) { $reasons += 'bad-polluted' }
        $sawClear = $false
        foreach ($line in @($bad.Lines)) {
            if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) {
                $sawClear = $true
            }
        }
        if (-not $sawClear) { $reasons += 'bad-not-cleared' }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-25' }
    else { Write-Fail 'W-25' ($reasons -join ' || ') }
}

function Test-W26 {
    $reason = 'REFUSED: maintenance marker already exists at logs/apphost.down-on-purpose. This run did not enter it. The operator must clear the marker before the window can start.'
    $reasons = @()
    $windowRepo = New-C1407TempRepo
    $rollbackRepo = New-C1407TempRepo
    try {
        $windowMarker = Join-Path (Join-Path $windowRepo 'logs') 'apphost.down-on-purpose'
        $window = Invoke-C1407WindowRun -Repository $windowRepo -MarkerOnSessionState $windowMarker
        if (-not $window.Ready) { $reasons += "window not-ready $($window.Error)" }
        else {
            if ($window.ExitCode -ne 3) { $reasons += "window exit=$($window.ExitCode)" }
            if ($window.Text.IndexOf($reason, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'window-missing-reason' }
            foreach ($line in @($window.Lines)) {
                if (Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') { $reasons += 'window-called-maintenance' }
                if (Test-C1407Field -Line $line -Token '-ClearHold') { $reasons += 'window-cleared-hold' }
                if (Test-C1407Field -Line $line -Token '-StopOnly') { $reasons += 'window-reached-stoponly' }
            }
            if (-not (Test-Path -LiteralPath $windowMarker)) { $reasons += 'window-removed-marker' }
            $body = ''
            if (Test-Path -LiteralPath $windowMarker) { $body = [System.IO.File]::ReadAllText($windowMarker) }
            if ($body.IndexOf('foreign', [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'window-marker-not-foreign' }
        }
        $rollbackMarker = Join-Path (Join-Path $rollbackRepo 'logs') 'apphost.down-on-purpose'
        $rollback = Invoke-C1407WindowRun -SkipApi -SkipTcp -Repository $rollbackRepo -ForeignMarker $rollbackMarker -ExtraArgs @('-Rollback') -SourceCount '4' -TargetCount '4'
        if (-not $rollback.Ready) { $reasons += "rollback not-ready $($rollback.Error)" }
        else {
            if ($rollback.ExitCode -ne 3) { $reasons += "rollback exit=$($rollback.ExitCode)" }
            if ($rollback.Text.IndexOf($reason, [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'rollback-missing-reason' }
            $sawDocker = $false
            foreach ($line in @($rollback.Lines)) {
                if (Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') { $reasons += 'rollback-called-maintenance' }
                if (Test-C1407Field -Line $line -Token '-Clear') { $reasons += 'rollback-cleared' }
                if (Test-C1407Field -Line $line -Token '-ExpectedServerSha') { $reasons += 'rollback-restarted' }
                $parts = @($line.Split([char]9))
                if ($parts.Count -ge 1 -and $parts[0] -eq 'docker') { $sawDocker = $true }
            }
            if (-not $sawDocker) { $reasons += 'rollback-never-started' }
            if (-not (Test-Path -LiteralPath $rollbackMarker)) { $reasons += 'rollback-removed-marker' }
            $body = ''
            if (Test-Path -LiteralPath $rollbackMarker) { $body = [System.IO.File]::ReadAllText($rollbackMarker) }
            if ($body.IndexOf('foreign', [System.StringComparison]::Ordinal) -lt 0) { $reasons += 'rollback-marker-not-foreign' }
        }
    } catch {
        $reasons += $_.Exception.Message
    } finally {
        Remove-C1407Tree $windowRepo
        Remove-C1407Tree $rollbackRepo
    }
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-26' }
    else { Write-Fail 'W-26' ($reasons -join ' || ') }
}

function New-C1407GateCase {
    param(
        [string]$Label,
        [string[]]$Arguments,
        [string[]]$HostArguments = @(),
        [string]$FileSwitch = '-File',
        [switch]$AsCommand,
        [string]$CommandSwitch = '-Command',
        [string]$WorkingDirectory = '',
        [string]$ScriptPath = '',
        [string[]]$StdinLines = @(),
        [string]$Expect = 'refused',
        [string]$Needle = '',
        [string]$ExpectToken = ''
    )
    return [pscustomobject]@{
        Label            = $Label
        Arguments        = $Arguments
        HostArguments    = $HostArguments
        FileSwitch       = $FileSwitch
        AsCommand        = [bool]$AsCommand
        CommandSwitch    = $CommandSwitch
        WorkingDirectory = $WorkingDirectory
        ScriptPath       = $ScriptPath
        StdinLines       = $StdinLines
        Expect           = $Expect
        Needle           = $Needle
        ExpectToken      = $ExpectToken
    }
}

function Test-W27 {
    # CARD-1417 (Review d6b441d2): -Execute proceeds only when the bound -ConfirmWindow switch is set
    # AND -ConfirmToken equals the CONFIRM TOKEN the preview prints for this machine, this script,
    # the same -Repository and today or yesterday UTC. No process command line is read, so every
    # earlier pwsh quirk (Reviews c25ee080 and d6b441d2) is an extra row that must refuse because
    # no valid token is given. Throwaway mutation that must turn rows red: make the token comparison
    # always true (no-token, empty-token, wrong-token, stale-token, tomorrow-token, other-repo-token
    # then open the window with exit 3).
    $fake = New-C1407FakeBin
    $closed = $script:ClosedApi
    $refuseSwitch = 'REFUSED: -Execute requires -ConfirmWindow.'
    $refuseToken = 'REFUSED: -Execute requires -ConfirmToken'
    $openedNeedle = 'REFUSED: verified snapshot reference is missing.'
    $tokenPattern = 'CONFIRM TOKEN ([0-9a-f]{8})'
    $cutover = Join-Path $here 'cutover-postgres-server2.ps1'
    $pipeSocket = Join-Path ([System.IO.Path]::GetTempPath()) 'CoreFxPipe_-ConfirmWindow'
    $en = [string][char]0x2013
    $em = [string][char]0x2014
    $bar = [string][char]0x2015
    try {
        New-Item -ItemType Directory -Path (Join-Path $fake.Dir '-ConfirmWindow') -Force | Out-Null
        $otherRepo = Join-Path $fake.Dir 'other'
        $token = Get-C1407Token
        $tokenRepo = Get-C1407Token -Repository $fake.Dir
        $tokenOther = Get-C1407Token -Repository $otherRepo
        $tokenYesterday = Get-C1407Token -DayOffset -1
        $tokenStale = Get-C1407Token -DayOffset -2
        $tokenTomorrow = Get-C1407Token -DayOffset 1
        $wrong = $(if ($token[0] -eq '0') { '1' } else { '0' }) + $token.Substring(1)
        $wrapper = Join-Path $fake.Dir 'wrapper.ps1'
        Set-Content -LiteralPath $wrapper -Encoding ascii -Value @(
            ("& '" + $cutover + "' -Execute -ConfirmW -ApiBase '" + $closed + "'"),
            'exit $LASTEXITCODE'
        )
        $prompt = @(("& '" + $cutover + "' -Execute -ConfirmW -ApiBase '" + $closed + "'"), 'exit $LASTEXITCODE')
        $reasons = @()
        $failText = ''
        # The preview prints the token the formula predicts, for the given -Repository and for none.
        foreach ($pair in @(@('with-repo', $tokenRepo, @('-Repository', $fake.Dir)), @('no-repo', $token, @()))) {
            $label = 'preview-' + $pair[0]
            $p = Invoke-C1407Cutover -Fake $fake -Arguments (@($pair[2]) + @('-ApiBase', $closed))
            $m = [regex]::Match($p.Text, $tokenPattern)
            if ($p.ExitCode -ne 0) { $reasons += "$label exit=$($p.ExitCode)" }
            if (-not $m.Success) { $reasons += "$label token-line-missing" }
            elseif ($m.Groups[1].Value -ne $pair[1]) { $reasons += "$label token-mismatch printed=$($m.Groups[1].Value) expected=$($pair[1])" }
            if (-not [string]::IsNullOrWhiteSpace($p.Trace)) { $reasons += "$label called-child" }
            if ($reasons.Count -gt 0 -and [string]::IsNullOrEmpty($failText)) { $failText = Get-C1407SafeText $p.Text }
        }
        if ($token -eq $tokenRepo -or $token -eq $tokenStale -or $token -eq $tokenYesterday) { $reasons += 'formula tokens-collide' }
        $cases = @(
            (New-C1407GateCase -Label 'no-token' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'empty-token' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', '', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'wrong-token' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', $wrong, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'stale-token' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', $tokenStale, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'tomorrow-token' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', $tokenTomorrow, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'other-repo-token' -Needle $refuseToken -ExpectToken $tokenRepo -Arguments @('-Execute', '-ConfirmWindow', '-Repository', $fake.Dir, '-ConfirmToken', $tokenOther, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'token-no-switch' -Needle $refuseSwitch -ExpectToken $token -Arguments @('-Execute', '-ConfirmToken', $token, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'valid' -Expect 'opens' -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', $token, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'valid-upper' -Expect 'opens' -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', $token.ToUpperInvariant(), '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'yesterday-token' -Expect 'opens' -Arguments @('-Execute', '-ConfirmWindow', '-ConfirmToken', $tokenYesterday, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'valid-repo' -Expect 'opens' -Arguments @('-Execute', '-ConfirmWindow', '-Repository', $fake.Dir, '-ConfirmToken', $tokenRepo, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'abbrev-conf' -Expect 'never-opens' -Needle 'ambiguous' -Arguments @('-Execute', '-Conf', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'abbrev-confirmw' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'ev-alias' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', '-ev', '/ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'holdreason-colon' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', '-HoldReason:', '-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'colon-dash' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', '-HoldReason:-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'colon-false' -Needle $refuseSwitch -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow:$false', '-ConfirmToken', $token, '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'trailing-space' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow ', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'wd' -Needle $refuseToken -ExpectToken $token -HostArguments @('-WorkingDirectory', '-ConfirmWindow') -WorkingDirectory $fake.Dir -Arguments @('-Execute', '-ConfirmW', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'pipe' -Needle $refuseToken -ExpectToken $token -HostArguments @('-CustomPipeName', '-ConfirmWindow') -Arguments @('-Execute', '-ConfirmW', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'en-dash' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', ($en + 'ConfirmWindow'), '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'em-dash-colon' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', ($em + 'HoldReason:'), '-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'bar-command' -AsCommand -CommandSwitch ($bar + 'c') -Expect 'refused-any' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'cmd-exact' -AsCommand -Expect 'refused-any' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'cmd-comment' -AsCommand -Expect 'refused-any' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', '-ApiBase', $closed, '#', '-ConfirmWindow')),
            (New-C1407GateCase -Label 'cmd-quoted' -AsCommand -Expect 'refused-any' -Needle $refuseToken -ExpectToken $token -Arguments @('-Execute', '-ConfirmW', '-ApiBase', $closed, '-HoldReason', "'window", '-ConfirmWindow', "now'")),
            (New-C1407GateCase -Label 'wrapper' -Needle $refuseToken -ExpectToken $token -ScriptPath $wrapper -Arguments @('-File', $cutover, '-ConfirmWindow')),
            (New-C1407GateCase -Label 'noexit' -Needle $refuseToken -ExpectToken $token -HostArguments @('-NoExit') -StdinLines $prompt -Arguments @('-WhatIf', '-ConfirmWindow', '-ApiBase', $closed)),
            (New-C1407GateCase -Label 'double-dash' -Expect 'never-opens' -Arguments @('-Execute', '-ConfirmW', '--', '-ConfirmWindow', '-ApiBase', $closed))
        )
        foreach ($case in $cases) {
            $before = $reasons.Count
            $r = Invoke-C1407Cutover -Fake $fake -Arguments $case.Arguments -HostArguments $case.HostArguments `
                -FileSwitch $case.FileSwitch -AsCommand:$case.AsCommand -CommandSwitch $case.CommandSwitch `
                -WorkingDirectory $case.WorkingDirectory -ScriptPath $case.ScriptPath -StdinLines $case.StdinLines
            $label = $case.Label
            $opened = ($r.Text.IndexOf($openedNeedle, [System.StringComparison]::Ordinal) -ge 0)
            $refused = ($r.Text.IndexOf('REFUSED: -Execute requires -Confirm', [System.StringComparison]::Ordinal) -ge 0)
            if (-not [string]::IsNullOrWhiteSpace($r.Trace)) { $reasons += "$label called-child" }
            switch ($case.Expect) {
                'refused' {
                    if ($r.ExitCode -ne 2) { $reasons += "$label exit=$($r.ExitCode)" }
                    if (-not $refused) { $reasons += "$label missing-refusal" }
                    if ($opened) { $reasons += "$label opened-window" }
                }
                'refused-any' {
                    # pwsh -Command folds a script exit 2 into process exit 1; the refusal text is the verdict.
                    if ($r.ExitCode -eq 0) { $reasons += "$label exit=0" }
                    if (-not $refused) { $reasons += "$label missing-refusal" }
                    if ($opened) { $reasons += "$label opened-window" }
                }
                'never-opens' {
                    if ($r.ExitCode -eq 0) { $reasons += "$label exit=0" }
                    if ($opened) { $reasons += "$label opened-window" }
                }
                'opens' {
                    if ($r.ExitCode -ne 3) { $reasons += "$label exit=$($r.ExitCode)" }
                    if (-not $opened) { $reasons += "$label not-opened" }
                    if ($refused) { $reasons += "$label refused" }
                }
            }
            if (-not [string]::IsNullOrEmpty($case.Needle) -and $r.Text.IndexOf($case.Needle, [System.StringComparison]::Ordinal) -lt 0) {
                $reasons += "$label missing-needle"
            }
            if (-not [string]::IsNullOrEmpty($case.ExpectToken)) {
                # Every gate refusal prints the token for the -Repository it was given, so the operator can retry.
                $m = [regex]::Match($r.Text, $tokenPattern)
                if (-not $m.Success) { $reasons += "$label token-line-missing" }
                elseif ($m.Groups[1].Value -ne $case.ExpectToken) { $reasons += "$label token-mismatch printed=$($m.Groups[1].Value)" }
            }
            if ($reasons.Count -gt $before -and [string]::IsNullOrEmpty($failText)) { $failText = Get-C1407SafeText $r.Text }
        }
        if ($reasons.Count -eq 0) { Write-Pass 'W-27' }
        else { Write-Fail 'W-27' (($reasons -join ' || ') + ' ' + $failText) }
    } catch {
        Write-Fail 'W-27' $_.Exception.Message
    } finally {
        Remove-C1407Tree $fake.Dir
        # pwsh leaves the Unix socket of -CustomPipeName -ConfirmWindow behind; it is this test's.
        if (Test-Path -LiteralPath $pipeSocket) { Remove-Item -LiteralPath $pipeSocket -Force -ErrorAction SilentlyContinue }
    }
}

function Test-C1407ClearHoldRecovery {
    param($Run, [string]$Label, [string]$Refusal)
    $agent = '11111111-1111-1111-1111-111111111111'
    $line = 'RECOVERY: keep-six -ClearHold exited 1.'
    if (-not $Run.Ready) { return @("$Label not-ready $($Run.Error)") }
    $reasons = @()
    if ($Run.ExitCode -ne 3) { $reasons += "$Label exit=$($Run.ExitCode)" }
    if ($Run.Text.IndexOf($Refusal, [System.StringComparison]::Ordinal) -lt 0) { $reasons += "$Label lost-refusal" }
    $at = $Run.Text.IndexOf($line, [System.StringComparison]::Ordinal)
    if ($at -lt 0) { $reasons += "$Label missing-recovery" }
    else {
        $end = $Run.Text.IndexOf("`n", $at, [System.StringComparison]::Ordinal)
        if ($end -lt 0) { $end = $Run.Text.Length }
        $recovery = $Run.Text.Substring($at, $end - $at)
        if ($recovery.IndexOf($agent, [System.StringComparison]::Ordinal) -lt 0) { $reasons += "$Label recovery-no-agent" }
        if ($recovery.IndexOf('-ClearHold -Agent', [System.StringComparison]::Ordinal) -lt 0) { $reasons += "$Label recovery-no-command" }
    }
    $sawClearHold = $false
    $sawClear = $false
    foreach ($l in @($Run.Lines)) {
        if ((Test-C1407Suffix -Line $l -Suffix 'keep-six.ps1') -and (Test-C1407Field -Line $l -Token '-ClearHold')) { $sawClearHold = $true }
        if ((Test-C1407Suffix -Line $l -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $l -Token '-Clear')) { $sawClear = $true }
    }
    if (-not $sawClearHold) { $reasons += "$Label clearhold-not-attempted" }
    if (-not $sawClear) { $reasons += "$Label maintenance-not-cleared" }
    if ($Run.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += "$Label printed-sentinel" }
    return @($reasons)
}

function Test-W28 {
    # CARD-1417 A-2: a failed keep-six -ClearHold at window exit is reported, not swallowed.
    $reasons = @()
    $secret = Invoke-C1407WindowRun -FailSecret '1' -FailClearHold '1'
    $reasons += @(Test-C1407ClearHoldRecovery -Run $secret -Label 'secret' -Refusal 'REFUSED: user-secrets set failed. Value not displayed.')
    if ($secret.Ready -and $secret.Text.IndexOf('RECOVERY: restarted AppHost on the old connection string before lifting maintenance.', [System.StringComparison]::Ordinal) -lt 0) {
        $reasons += 'secret missing-old-restart'
    }
    $stop = Invoke-C1407WindowRun -StopOnlyExit '4' -FailClearHold '1'
    $reasons += @(Test-C1407ClearHoldRecovery -Run $stop -Label 'stop' -Refusal 'REFUSED: StopOnly exited 4. The old database stays in place.')
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-28' }
    else { Write-Fail 'W-28' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $secret.Text) + ' ' + (Get-C1407SafeText $stop.Text)) }
}

function Test-C1407EnterFailure {
    param($Run, [string]$Label, [string]$Marker, [string[]]$Needles, [string[]]$Absent)
    if (-not $Run.Ready) { return @("$Label not-ready $($Run.Error)") }
    $reasons = @()
    if ($Run.ExitCode -ne 3) { $reasons += "$Label exit=$($Run.ExitCode)" }
    foreach ($needle in @($Needles)) {
        if ($Run.Text.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) { $reasons += "$Label missing [$needle]" }
    }
    foreach ($needle in @($Absent)) {
        if ($Run.Text.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { $reasons += "$Label unexpected [$needle]" }
    }
    $sawEnter = $false
    foreach ($line in @($Run.Lines)) {
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-EnterIfAbsent')) { $sawEnter = $true }
        if ((Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') -and (Test-C1407Field -Line $line -Token '-Clear')) { $reasons += "$Label cleared-marker" }
        if (Test-C1407Field -Line $line -Token '-StopOnly') { $reasons += "$Label reached-stoponly" }
        if ((Test-C1407Suffix -Line $line -Suffix 'keep-six.ps1') -and (Test-C1407Field -Line $line -Token '-Hold')) { $reasons += "$Label recorded-hold" }
    }
    if (-not $sawEnter) { $reasons += "$Label enter-not-called" }
    if (-not (Test-Path -LiteralPath $Marker)) { $reasons += "$Label marker-removed" }
    if ($Run.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += "$Label printed-sentinel" }
    return @($reasons)
}

function Test-W29 {
    # CARD-1417 A-3: an enter that wrote the marker and then failed is told apart from exit 2, and the refusal names the marker.
    $reasons = @()
    $writtenRepo = New-C1407TempRepo
    $raceRepo = New-C1407TempRepo
    try {
        $writtenMarker = Join-Path (Join-Path $writtenRepo 'logs') 'apphost.down-on-purpose'
        $written = Invoke-C1407WindowRun -Repository $writtenRepo -EnterMarker $writtenMarker -MaintenanceExit '1'
        $reasons += @(Test-C1407EnterFailure -Run $written -Label 'written' -Marker $writtenMarker `
            -Needles @('REFUSED: AppHost maintenance enter exit check failed.', 'logs/apphost.down-on-purpose', 'written by this run', 'left in place') `
            -Absent @('already existed'))
        $raceMarker = Join-Path (Join-Path $raceRepo 'logs') 'apphost.down-on-purpose'
        $race = Invoke-C1407WindowRun -Repository $raceRepo -EnterMarker $raceMarker -MaintenanceExit '2'
        $reasons += @(Test-C1407EnterFailure -Run $race -Label 'race' -Marker $raceMarker `
            -Needles @('REFUSED: AppHost maintenance enter exit check failed.', 'logs/apphost.down-on-purpose', 'already existed', 'This run did not enter it') `
            -Absent @('written by this run'))
    } catch {
        $reasons += $_.Exception.Message
    } finally {
        Remove-C1407Tree $writtenRepo
        Remove-C1407Tree $raceRepo
    }
    $reasons = @($reasons | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons.Count -eq 0) { Write-Pass 'W-29' }
    else { Write-Fail 'W-29' ($reasons -join ' || ') }
}

function Test-W30 {
    # CARD-1417 A-4: a window run drives an ephemeral tailnet port and a temporary repository, never 5432 or this checkout.
    $reasons = @()
    $checkout = [System.IO.Path]::GetFullPath((Split-Path $here -Parent)).TrimEnd([char]47, [char]92)
    $temp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([char]47, [char]92)
    $run = Invoke-C1407WindowRun
    if (-not $run.Ready) { $reasons += "window not-ready $($run.Error)" }
    else {
        if ($run.ExitCode -ne 0) { $reasons += "window exit=$($run.ExitCode)" }
        $m = [regex]::Match($run.Text, 'PHASE 0 tailnet ([0-9]+) accepted a desktop connect\.')
        if (-not $m.Success) { $reasons += 'window port-not-reported' }
        else {
            $port = [int]$m.Groups[1].Value
            if ($port -eq 5432) { $reasons += 'window port-5432' }
            if ($port -lt 1024) { $reasons += "window port-privileged $port" }
            if ($port -ne [int]$run.TailnetPort) { $reasons += "window port-mismatch script=$port harness=$($run.TailnetPort)" }
        }
        $repo = $null
        foreach ($line in @($run.Lines)) {
            if (-not (Test-C1407Suffix -Line $line -Suffix 'recover-repository-children.ps1')) { continue }
            $parts = @($line.Split([char]9))
            for ($p = 0; $p -lt ($parts.Count - 1); $p++) {
                if ($parts[$p] -eq '-Repository') { $repo = $parts[$p + 1] }
            }
        }
        if ([string]::IsNullOrWhiteSpace($repo)) { $reasons += 'window repository-not-passed' }
        else {
            $full = [System.IO.Path]::GetFullPath($repo).TrimEnd([char]47, [char]92)
            if ($full.Equals($checkout, [System.StringComparison]::OrdinalIgnoreCase)) { $reasons += 'window repository-is-checkout' }
            if (-not $full.StartsWith($temp, [System.StringComparison]::OrdinalIgnoreCase)) { $reasons += 'window repository-not-temp' }
            if (Test-Path -LiteralPath $full) { $reasons += 'window repository-left-behind' }
        }
        if ($run.Text.Contains('SENTINEL-1407-ARGV')) { $reasons += 'window printed-sentinel' }
    }
    $closed = Invoke-C1407WindowRun -SkipTcp -ExtraArgs @('-TailnetPort', '9')
    if (-not $closed.Ready) { $reasons += "closed not-ready $($closed.Error)" }
    else {
        if ($closed.ExitCode -ne 3) { $reasons += "closed exit=$($closed.ExitCode)" }
        if ($closed.Text.IndexOf('REFUSED: tailnet 9 did not accept a desktop connect. StopOnly was not run.', [System.StringComparison]::Ordinal) -lt 0) {
            $reasons += 'closed missing-refusal'
        }
        foreach ($line in @($closed.Lines)) {
            if (Test-C1407Suffix -Line $line -Suffix 'set-apphost-maintenance.ps1') { $reasons += 'closed entered-maintenance' }
            if (Test-C1407Field -Line $line -Token '-StopOnly') { $reasons += 'closed reached-stoponly' }
        }
    }
    if ($reasons.Count -eq 0) { Write-Pass 'W-30' }
    else { Write-Fail 'W-30' (($reasons -join ' || ') + ' ' + (Get-C1407SafeText $run.Text) + ' ' + (Get-C1407SafeText $closed.Text)) }
}

$all = @('W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8', 'W9', 'W10', 'W11', 'W12', 'W13', 'W14', 'W15', 'W16', 'W17', 'W18', 'W19', 'W20', 'W21', 'W22', 'W23', 'W24', 'W25', 'W26', 'W27', 'W28', 'W29', 'W30')
$script:C1407PosixWindow = @('W6', 'W11', 'W13', 'W14', 'W15', 'W16', 'W17', 'W18', 'W19', 'W20', 'W21', 'W22', 'W23', 'W24', 'W25', 'W26', 'W28', 'W29', 'W30')
function Invoke-C1407Case {
    param([string]$Name)
    if (($env:OS -eq 'Windows_NT') -and ($script:C1407PosixWindow -contains $Name)) {
        Write-Host ("SKIP {0} POSIX extensionless sh fake: the executable bit is not a Windows property; Linux still runs this case" -f $Name)
        return
    }
    & ('Test-{0}' -f $Name)
}
if ($Case) {
    $name = $Case
    if ($name -match '^W-([0-9]+)$') { $name = 'W' + $Matches[1] }
    $fn = Get-Command -Name ('Test-{0}' -f $name) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    Invoke-C1407Case $name
} else {
    foreach ($id in $all) { Invoke-C1407Case $id }
}

Write-Host ''
Write-Host ('CARD-1407 cutover: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'CUTOVER TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'CUTOVER TESTS EXIT CODE: 0  (PASS)'
exit 0
