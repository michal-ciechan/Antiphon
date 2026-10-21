#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1407: cutover-postgres-server2.ps1 defaults to -WhatIf, and
    restart-apphost.ps1 -StopOnly does not relaunch.

    No production database, no server2 socket, no AppHost restart.
    HTTP is pointed at a closed loopback port. External commands are
    shadowed by a PATH fake that records a call and fails.
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
        & chmod +x $path
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
name=$(basename "$0")
trace="$C1407_TRACE"
stdin_dir="$C1407_STDIN"
if [ -z "$trace" ] || [ -z "$stdin_dir" ]; then
  echo "c1407 fake is missing its trace paths" >&2
  exit 99
fi
line=$name
for arg in "$@"; do
  flat=$(printf '%s' "$arg" | tr '\n\t' '  ')
  line=$(printf '%s\t%s' "$line" "$flat")
done
printf '%s\n' "$line" >> "$trace"
stdin_file="$stdin_dir/$name.$$.in"
if [ -p /dev/stdin ]; then
  cat > "$stdin_file"
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
  sql=$(tr '\n' ' ' < "$stdin_file")
fi
emit_sql() {
  count="$1"
  if printf '%s' "$sql" | grep -F -q 'client_addr'; then
    printf '%s\n' '1'
  elif printf '%s' "$sql" | grep -F -q 'pg_stat_activity'; then
    printf '%s\n' '0'
  elif printf '%s' "$sql" | grep -F -q 'pg_class'; then
    printf '%s\n' 'public.Sample'
  elif printf '%s' "$sql" | grep -F -q 'indisprimary'; then
    printf '%s\n' 'Id'
  elif printf '%s' "$sql" | grep -F -q 'min('; then
    printf '%s\n' "$count 1 $count"
  elif printf '%s' "$sql" | grep -F -q 'count(*)'; then
    printf '%s\n' "$count"
  elif printf '%s' "$sql" | grep -F -q 'pg_sequences'; then
    printf '%s\n' 'public.sample_seq 1 true'
  elif printf '%s' "$sql" | grep -F -q 'pg_extension'; then
    printf '%s\n' 'plpgsql 1.0'
  elif printf '%s' "$sql" | grep -F -q 'MigrationId'; then
    printf '%s\n' '20261021000000_Init'
  else
    printf '\n'
  fi
}
case "$name" in
  docker)
    joined=$(printf '%s ' "$@")
    if [ "$C1407_FAIL_DUMP" = "1" ] && printf '%s' "$joined" | grep -F -q 'pg_dump'; then
      exit 1
    fi
    if printf '%s' "$joined" | grep -F -q 'psql'; then
      emit_sql "${C1407_SOURCE_COUNT:-3}"
    elif printf '%s' "$joined" | grep -F -q 'volume ls'; then
      printf '%s\n' 'antiphon_pgdata'
    elif printf '%s' "$joined" | grep -F -q 'inspect'; then
      printf '%s\n' 'exited'
    fi
    exit 0
    ;;
  ssh)
    joined=$(printf '%s ' "$@")
    if printf '%s' "$joined" | grep -F -q 'psql'; then
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
        & chmod +x $path
    }
    return [pscustomobject]@{ Dir = $dir; Trace = $trace; Stdin = $stdin }
}

function Start-C1407Api {
    param([int]$Port, [string]$Sha, [string]$PendingJson, [int]$LandPendingStatus = 200)
    $log = Join-Path ([System.IO.Path]::GetTempPath()) ('c1407-api-' + [guid]::NewGuid().ToString('N') + '.log')
    '' | Set-Content -LiteralPath $log -Encoding ascii
    $state = [hashtable]::Synchronized(@{ Stop = $false })
    $sb = {
        param($Port, $Sha, $PendingJson, $LandPendingStatus, $State, $Log)
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
    [void]$ps.AddScript($sb).AddArgument($Port).AddArgument($Sha).AddArgument($PendingJson).AddArgument($LandPendingStatus).AddArgument($state).AddArgument($log)
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
        [string]$CommandHost
    )
    $repo = Split-Path $here -Parent
    $windowArgs = @(
        '-Execute', '-ConfirmWindow',
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
        [switch]$SkipTcp
    )
    $result = @{
        ExitCode = 99
        Text     = ''
        Lines    = @()
        Stdin    = @()
        Ready    = $false
        Error    = ''
    }
    $fake = $null
    $api = $null
    $tcp = $null
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
        if (-not $SkipTcp) {
            $tcp = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 5432)
            $tcp.Server.SetSocketOption(
                [System.Net.Sockets.SocketOptionLevel]::Socket,
                [System.Net.Sockets.SocketOptionName]::ReuseAddress,
                $true)
            $tcp.Start()
        }
        $sha = '0123456789abcdef0123456789abcdef01234567'
        $apiBase = $script:ClosedApi
        if (-not $SkipApi) {
            $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
            $probe.Start()
            $port = $probe.LocalEndpoint.Port
            $probe.Stop()
            $api = Start-C1407Api -Port $port -Sha $sha -PendingJson $PendingJson -LandPendingStatus $LandPendingStatus
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
            -PendingFixture $fixture -CommandHost (Join-Path $fake.Dir 'pwsh')
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
        Remove-C1407Tree $(if ($fake) { $fake.Dir } else { $null })
        Remove-C1407Tree $snap
        Remove-C1407Tree $drill
        Remove-C1407Tree $secret
        Remove-C1407Tree $recipient
        Remove-C1407Tree $dumpDir
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
        $Fake
    )
    $scriptPath = Join-Path $here 'cutover-postgres-server2.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $prev = $env:PATH
    try {
        if ($Fake) {
            $env:PATH = $Fake.Dir + [System.IO.Path]::PathSeparator + $prev
        }
        $output = @(& $pwsh -NoProfile -File $scriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text     = ($output -join "`n")
            Trace    = $(if ($Fake) { (Read-C1407Trace $Fake).Trim() } else { '' })
        }
    } finally {
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
        $r = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi)
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
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
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
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $slow
        )
        $notPassed = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
            '-SnapshotReference', $snap, '-DrillRecord', $failed
        )
        $envelope = Invoke-C1407Cutover -Fake $fake -Arguments @(
            '-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi,
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
    $fake = New-C1407FakeBin
    try {
        $confirm = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-Confirm', '-ApiBase', $script:ClosedApi)
        $con = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-Con', '-ApiBase', $script:ClosedApi)
        $full = Invoke-C1407Cutover -Fake $fake -Arguments @('-Execute', '-ConfirmWindow', '-ApiBase', $script:ClosedApi)
        $confirmOk = ($confirm.ExitCode -ne 0) -and
            ($confirm.Text.IndexOf('REFUSED: -Execute requires -ConfirmWindow.', [System.StringComparison]::Ordinal) -ge 0) -and
            [string]::IsNullOrWhiteSpace($confirm.Trace) -and
            ($confirm.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -lt 0)
        $conOk = ($con.ExitCode -ne 0) -and
            [string]::IsNullOrWhiteSpace($con.Trace) -and
            ($con.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -lt 0) -and
            ($con.Text.IndexOf('ambiguous', [System.StringComparison]::Ordinal) -ge 0)
        $fullOk = ($full.ExitCode -ne 0) -and
            ($full.Text.IndexOf('snapshot reference', [System.StringComparison]::Ordinal) -ge 0) -and
            ($full.Text.IndexOf('requires -ConfirmWindow', [System.StringComparison]::Ordinal) -lt 0) -and
            [string]::IsNullOrWhiteSpace($full.Trace)
        if ($confirmOk -and $conOk -and $fullOk) { Write-Pass 'W-12' }
        else {
            Write-Fail 'W-12' ("confirm=$($confirm.ExitCode) con=$($con.ExitCode) full=$($full.ExitCode) $(Get-C1407SafeText $confirm.Text) $(Get-C1407SafeText $con.Text) $(Get-C1407SafeText $full.Text)")
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

function Test-W20 {
    $repo = Split-Path $here -Parent
    $marker = Join-Path (Join-Path $repo 'logs') 'apphost.down-on-purpose'
    $reason = 'REFUSED: maintenance marker already exists at logs/apphost.down-on-purpose. This run did not enter it. The operator must clear the marker before the window can start.'
    New-Item -ItemType Directory -Force (Split-Path -Parent $marker) | Out-Null
    Set-Content -LiteralPath $marker -Value 'down-on-purpose pre-existing' -Encoding ascii
    $reasons = @()
    try {
        $window = Invoke-C1407WindowRun
        $reasons += @(Test-C1407ForeignMarker -Run $window -Label 'window' -Reason $reason -Marker $marker)
        $rollback = Invoke-C1407WindowRun -ExtraArgs @('-Rollback')
        $reasons += @(Test-C1407ForeignMarker -Run $rollback -Label 'rollback' -Reason $reason -Marker $marker)
    } catch {
        $reasons += $_.Exception.Message
    } finally {
        Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
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

$all = @('W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8', 'W9', 'W10', 'W11', 'W12', 'W13', 'W14', 'W15', 'W16', 'W17', 'W18', 'W19', 'W20', 'W21')
if ($Case) {
    $name = $Case
    if ($name -match '^W-([0-9]+)$') { $name = 'W' + $Matches[1] }
    $fn = Get-Command -Name ('Test-{0}' -f $name) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
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
