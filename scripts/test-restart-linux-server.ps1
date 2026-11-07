#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1745 witnesses for scripts/restart-linux-server.ps1.

    A loopback stub and a fake docker record every probe and every restart.
    The harness never calls the real docker client and never binds 17200-17209.
    ASCII-only.
#>
param([Parameter(Mandatory = $true)][string]$Case)

$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
$script:failures = New-Object System.Collections.Generic.List[string]
$here = $PSScriptRoot
$repo = Split-Path -Parent $here
$gate = Join-Path $here 'restart-linux-server.ps1'
$sha64 = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$sha40 = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$other = 'cccccccccccccccccccccccccccccccccccccccc'

function Write-Pass([string]$Name) {
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail([string]$Name, [string]$Detail) {
    $script:failed++
    $script:failures.Add("$Name : $Detail")
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True([bool]$Cond, [string]$Name, [string]$Detail) {
    if ($Cond) { Write-Pass $Name } else { Write-Fail $Name $Detail }
}

function New-C1745Root {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('c1745-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $bin = Join-Path $root 'bin'
    New-Item -ItemType Directory -Path $bin | Out-Null
    $trace = Join-Path $root 'docker.trace'
    $image = Join-Path $root 'image.txt'
    $ref = Join-Path $root 'ref.txt'
    $tagId = Join-Path $root 'tagid.txt'
    Set-Content -LiteralPath $trace -Value '' -Encoding ascii
    Set-Content -LiteralPath $image -Value 'sha256:c1745previous' -Encoding ascii
    Set-Content -LiteralPath $ref -Value 'antiphon-server:current' -Encoding ascii
    Set-Content -LiteralPath $tagId -Value 'sha256:c1745previous' -Encoding ascii
    $fake = Join-Path $bin 'docker'
    @(
        '#!/bin/sh'
        'trace="$C1745_DOCKER_TRACE"'
        'printf "%s\n" "$*" >> "$trace"'
        'if [ "$1" = "inspect" ]; then'
        '  if [ "$3" = "{{.Image}}" ]; then'
        '    cat "$C1745_DOCKER_IMAGE"'
        '    exit 0'
        '  fi'
        '  if [ "$3" = "{{.Config.Image}}" ]; then'
        '    cat "$C1745_DOCKER_REF"'
        '    exit 0'
        '  fi'
        '  exit 99'
        'fi'
        'if [ "$1" = "image" ] && [ "$2" = "inspect" ]; then'
        '  cat "$C1745_DOCKER_TAGID"'
        '  exit 0'
        'fi'
        'if [ "$1" = "stop" ] || [ "$1" = "start" ]; then'
        '  exit 0'
        'fi'
        'exit 99'
    ) | Set-Content -LiteralPath $fake -Encoding ascii
    & chmod a+x $fake
    return [pscustomobject]@{
        Root = $root
        Bin = $bin
        Docker = $fake
        Trace = $trace
        Image = $image
        Ref = $ref
        TagId = $tagId
        Hits = (Join-Path $root 'hits.log')
        Lock = (Join-Path $root 'restart.lock')
        Config = (Join-Path $root 'server.cfg')
    }
}

function Remove-C1745Root($Fx) {
    if ($Fx -and $Fx.Root -and (Test-Path -LiteralPath $Fx.Root)) {
        Remove-Item -LiteralPath $Fx.Root -Recurse -Force
    }
}

function Get-C1745Trace($Fx) {
    if (-not (Test-Path -LiteralPath $Fx.Trace)) { return @() }
    return @(Get-Content -LiteralPath $Fx.Trace | Where-Object { $_ -ne '' })
}

function Get-C1745Hits($Fx) {
    if (-not (Test-Path -LiteralPath $Fx.Hits)) { return @() }
    return @(Get-Content -LiteralPath $Fx.Hits | Where-Object { $_ -ne '' })
}

function Write-C1745Config($Fx, [string]$BaseUrl, [string]$Container) {
    @(
        "baseUrl=$BaseUrl"
        "container=$Container"
        "lockPath=$($Fx.Lock)"
    ) | Set-Content -LiteralPath $Fx.Config -Encoding ascii
}

function Start-C1745Stub {
    param($Fx, [int]$HealthCode, [string]$VersionBody, [int]$VersionCode)
    '' | Set-Content -LiteralPath $Fx.Hits -Encoding ascii
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $state = [hashtable]::Synchronized(@{ Stop = $false })
    $sb = {
        param($Listener, $State, $HealthCode, $VersionBody, $VersionCode, $HitLog)
        try {
            while (-not $State.Stop) {
                if (-not $Listener.Pending()) {
                    Start-Sleep -Milliseconds 20
                    continue
                }
                $client = $Listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $stream.ReadTimeout = 2000
                    $buffer = New-Object byte[] 4096
                    $text = ''
                    while ($text.IndexOf("`r`n`r`n") -lt 0 -and $text.Length -lt 8000) {
                        $n = $stream.Read($buffer, 0, $buffer.Length)
                        if ($n -le 0) { break }
                        $text += [System.Text.Encoding]::ASCII.GetString($buffer, 0, $n)
                    }
                    $line = ''
                    if ($text.Length -gt 0) { $line = ($text -split "`r`n", 2)[0] }
                    Add-Content -LiteralPath $HitLog -Value $line -Encoding ascii
                    $path = ''
                    if ($line -match '^GET\s+(\S+)') { $path = $Matches[1] }
                    $path = ($path -split '\?', 2)[0]
                    $code = 404
                    $body = 'missing'
                    if ($path -eq '/health') {
                        $code = [int]$HealthCode
                        $body = 'Healthy'
                    } elseif ($path -eq '/api/version') {
                        $code = [int]$VersionCode
                        $body = [string]$VersionBody
                    }
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
                    $head = "HTTP/1.1 $code X`r`nContent-Type: text/plain`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
                    $headBytes = [System.Text.Encoding]::ASCII.GetBytes($head)
                    $stream.Write($headBytes, 0, $headBytes.Length)
                    $stream.Write($bytes, 0, $bytes.Length)
                    $stream.Flush()
                } catch {
                    Add-Content -LiteralPath $HitLog -Value ('ERR ' + $_.Exception.Message) -Encoding ascii
                } finally {
                    try { $client.Close() } catch { }
                }
            }
        } catch {
            Add-Content -LiteralPath $HitLog -Value ('LISTEN ' + $_.Exception.Message) -Encoding ascii
        }
    }
    $rs = [runspacefactory]::CreateRunspace()
    $rs.Open()
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    [void]$ps.AddScript($sb).AddArgument($listener).AddArgument($state).AddArgument($HealthCode).AddArgument($VersionBody).AddArgument($VersionCode).AddArgument($Fx.Hits)
    $handle = $ps.BeginInvoke()
    return [pscustomobject]@{
        Port = $port
        State = $state
        Listener = $listener
        PowerShell = $ps
        Runspace = $rs
        Handle = $handle
    }
}

function Stop-C1745Stub($Stub) {
    if (-not $Stub) { return }
    try { $Stub.State.Stop = $true } catch { }
    try { $Stub.Listener.Stop() } catch { }
    try { $Stub.PowerShell.Stop() } catch { }
    try { $Stub.Runspace.Close() } catch { }
    try { $Stub.PowerShell.Dispose() } catch { }
}

function Invoke-C1745Gate {
    param($Fx, [string]$ConfigPath, [string]$Sha, [switch]$Execute, [switch]$WhatIf)
    $out = Join-Path $Fx.Root 'gate.out'
    $err = Join-Path $Fx.Root 'gate.err'
    $argList = @('-NoProfile', '-NonInteractive', '-File', $gate, '-TimeoutSec', '5')
    if ($ConfigPath) { $argList += @('-Config', $ConfigPath) }
    if ($Sha) { $argList += @('-ExpectedServerSha', $Sha) }
    if ($Execute) { $argList += '-Execute' }
    if ($WhatIf) { $argList += '-WhatIf' }
    $savedDocker = $env:ANTIPHON_LINUX_SERVER_DOCKER
    $savedTrace = $env:C1745_DOCKER_TRACE
    $savedImage = $env:C1745_DOCKER_IMAGE
    $savedRef = $env:C1745_DOCKER_REF
    $savedTagId = $env:C1745_DOCKER_TAGID
    $savedPath = $env:PATH
    $savedConfig = $env:ANTIPHON_LINUX_SERVER_CONFIG
    try {
        $env:ANTIPHON_LINUX_SERVER_DOCKER = $Fx.Docker
        $env:C1745_DOCKER_TRACE = $Fx.Trace
        $env:C1745_DOCKER_IMAGE = $Fx.Image
        $env:C1745_DOCKER_REF = $Fx.Ref
        $env:C1745_DOCKER_TAGID = $Fx.TagId
        $env:PATH = $Fx.Bin + [System.IO.Path]::PathSeparator + $savedPath
        Remove-Item Env:ANTIPHON_LINUX_SERVER_CONFIG -ErrorAction SilentlyContinue
        $p = Start-Process -FilePath 'pwsh' -ArgumentList $argList -Wait -PassThru -NoNewWindow `
            -RedirectStandardOutput $out -RedirectStandardError $err
        $text = ''
        if (Test-Path -LiteralPath $out) { $text += [System.IO.File]::ReadAllText($out) }
        if (Test-Path -LiteralPath $err) { $text += [System.IO.File]::ReadAllText($err) }
        $code = -1
        if ($p -and $null -ne $p.ExitCode) { $code = [int]$p.ExitCode }
        return [pscustomobject]@{ Code = $code; Text = $text }
    } finally {
        $env:ANTIPHON_LINUX_SERVER_DOCKER = $savedDocker
        $env:C1745_DOCKER_TRACE = $savedTrace
        $env:C1745_DOCKER_IMAGE = $savedImage
        $env:C1745_DOCKER_REF = $savedRef
        $env:C1745_DOCKER_TAGID = $savedTagId
        $env:PATH = $savedPath
        if ($null -eq $savedConfig) { Remove-Item Env:ANTIPHON_LINUX_SERVER_CONFIG -ErrorAction SilentlyContinue }
        else { $env:ANTIPHON_LINUX_SERVER_CONFIG = $savedConfig }
    }
}

function Assert-NoDocker($Fx, [string]$Name) {
    $trace = @(Get-C1745Trace $Fx)
    Assert-True ($trace.Count -eq 0) $Name ("trace=" + ($trace -join ' | '))
}

function Assert-NoStop($Fx, [string]$Name) {
    $joined = (@(Get-C1745Trace $Fx) -join "`n")
    $stopped = $joined.Contains('stop ') -or $joined.Contains('start ')
    Assert-True (-not $stopped) $Name $joined
}

function Finish-C1745 {
    $rows = $script:passed + $script:failed
    Write-Host "C1745: $($script:passed) passed, $($script:failed) failed, $rows rows"
    if ($script:failed -gt 0) { exit 1 }
    exit 0
}

if (-not (Test-Path -LiteralPath $gate)) {
    Write-Fail 'gate-present' "missing $gate"
    Finish-C1745
}

$fx = $null
$stub = $null
try {
    $fx = New-C1745Root
    switch ($Case) {
        'W1' {
            $run = Invoke-C1745Gate -Fx $fx
            Assert-True ($run.Code -eq 3) 'W1-exit-3' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('not configured')) 'W1-says-not-configured' $run.Text
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W1-says-nothing-restarted' $run.Text
            Assert-NoDocker $fx 'W1-no-docker'
            Assert-True (-not (Test-Path -LiteralPath $fx.Lock)) 'W1-no-lock' $fx.Lock
        }
        'W2' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha64.ToUpperInvariant() + '","capabilities":["land-v2"]}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha64 -WhatIf
            Assert-True ($run.Code -eq 0) 'W2-exit-0' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('VERIFIED sha=' + $sha64 + ' mode=whatif')) 'W2-verified' $run.Text
            Assert-NoDocker $fx 'W2-no-docker'
            Assert-True (-not (Test-Path -LiteralPath $fx.Lock)) 'W2-no-lock' $fx.Lock
        }
        'W3' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $other + '","capabilities":["land-v2"]}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha64 -WhatIf
            Assert-True ($run.Code -eq 5) 'W3-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('exit 5')) 'W3-says-exit-5' $run.Text
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W3-nothing-restarted' $run.Text
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W3-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('expected=' + $sha64) -and $lock.Contains('observed=' + $other) -and $lock.Contains('reason=mismatch')) 'W3-lock-names-sha' $lock
            Assert-NoDocker $fx 'W3-no-docker'
            Assert-True (-not $run.Text.Contains('VERIFIED')) 'W3-not-verified' $run.Text
        }
        'W4' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 503 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -WhatIf
            Assert-True ($run.Code -eq 1) 'W4-exit-1' "code=$($run.Code) text=$($run.Text)"
            Assert-True (-not (Test-Path -LiteralPath $fx.Lock)) 'W4-no-lock' $fx.Lock
            Assert-NoDocker $fx 'W4-no-docker'
            Assert-True (-not $run.Text.Contains('VERIFIED')) 'W4-not-verified' $run.Text
        }
        'W5' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha40 + '","capabilities":["land-v2"]}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 0) 'W5-exit-0' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('already at expected SHA')) 'W5-already' $run.Text
            Assert-True ($run.Text.Contains('VERIFIED sha=' + $sha40 + ' mode=execute')) 'W5-verified-execute' $run.Text
            Assert-NoDocker $fx 'W5-no-docker'
            Assert-True (-not (Test-Path -LiteralPath $fx.Lock)) 'W5-lock-cleared' $fx.Lock
        }
        'W6' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $other + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 5) 'W6-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W6-nothing-restarted' $run.Text
            Assert-True ($run.Text.Contains('left running')) 'W6-left-running' $run.Text
            Assert-True ($run.Text.Contains('recovery: docker start antiphon-staging')) 'W6-recovery' $run.Text
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W6-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('reason=mismatch') -and $lock.Contains('previousImage=sha256:c1745previous')) 'W6-lock-records-image' $lock
            $joined = (@(Get-C1745Trace $fx) -join "`n")
            Assert-True ($joined -eq ("inspect --format {{.Image}} antiphon-staging`ninspect --format {{.Config.Image}} antiphon-staging`nimage inspect --format {{.Id}} antiphon-server:current")) 'W6-inspected-same-image' $joined
            Assert-NoStop $fx 'W6-no-stop'
            Assert-True (-not $run.Text.Contains('VERIFIED')) 'W6-not-verified' $run.Text
            Assert-True (-not $run.Text.Contains('left stopped')) 'W6-not-left-stopped' $run.Text
        }
        'W7' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha 'abc' -Execute
            Assert-True ($run.Code -eq 3) 'W7-exit-3' "code=$($run.Code) text=$($run.Text)"
            Assert-NoDocker $fx 'W7-no-docker'
            $hits = @(Get-C1745Hits $fx)
            Assert-True ($hits.Count -eq 0) 'W7-no-http' ($hits -join ' | ')
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W7-nothing-restarted' $run.Text
        }
        'W8' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 200
            Write-C1745Config $fx 'http://127.0.0.1:17202/' 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 3) 'W8-exit-3' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('17202')) 'W8-names-desktop-port' $run.Text
            Assert-NoDocker $fx 'W8-no-docker'
            $hits = @(Get-C1745Hits $fx)
            Assert-True ($hits.Count -eq 0) 'W8-no-http' ($hits -join ' | ')
        }
        'W9' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'session-runner'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 3) 'W9-exit-3' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('session-runner')) 'W9-names-runner' $run.Text
            Assert-NoDocker $fx 'W9-no-docker'
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W9-nothing-restarted' $run.Text
        }
        'W10' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40
            Assert-True ($run.Code -eq 0) 'W10-exit-0' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('mode=whatif')) 'W10-verified-whatif' $run.Text
            Assert-NoDocker $fx 'W10-no-docker'
        }
        'W13' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody 'not-json' -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -WhatIf
            Assert-True ($run.Code -eq 5) 'W13-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W13-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('reason=malformed')) 'W13-lock-malformed' $lock
            Assert-NoDocker $fx 'W13-no-docker'
        }
        'W14' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 503 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 5) 'W14-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('left running')) 'W14-left-running' $run.Text
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W14-nothing-restarted' $run.Text
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W14-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('reason=health')) 'W14-lock-health' $lock
            Assert-NoDocker $fx 'W14-no-docker'
        }
        'W15' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $sha40 + '"}') -VersionCode 500
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 5) 'W15-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W15-nothing-restarted' $run.Text
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W15-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('reason=unavailable')) 'W15-lock-unavailable' $lock
            Assert-NoDocker $fx 'W15-no-docker'
        }
        'W16' {
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody 'not-json' -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 5) 'W16-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('left running')) 'W16-left-running' $run.Text
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W16-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('reason=malformed')) 'W16-lock-malformed' $lock
            Assert-NoDocker $fx 'W16-no-docker'
        }
        'W17' {
            Set-Content -LiteralPath $fx.TagId -Value 'sha256:c1745moved' -Encoding ascii
            $stub = Start-C1745Stub -Fx $fx -HealthCode 200 -VersionBody ('{"version":"' + $other + '"}') -VersionCode 200
            Write-C1745Config $fx ("http://127.0.0.1:{0}/" -f $stub.Port) 'antiphon-staging'
            $run = Invoke-C1745Gate -Fx $fx -ConfigPath $fx.Config -Sha $sha40 -Execute
            Assert-True ($run.Code -eq 5) 'W17-exit-5' "code=$($run.Code) text=$($run.Text)"
            Assert-True ($run.Text.Contains('reason: image-moved')) 'W17-image-moved' $run.Text
            Assert-True ($run.Text.Contains('sha256:c1745moved')) 'W17-names-new-image' $run.Text
            Assert-True ($run.Text.Contains('does not recreate')) 'W17-no-recreate' $run.Text
            Assert-True ($run.Text.Contains('left running')) 'W17-left-running' $run.Text
            Assert-True ($run.Text.Contains('Nothing was restarted.')) 'W17-nothing-restarted' $run.Text
            Assert-True ($run.Text.Contains('recovery: docker start antiphon-staging')) 'W17-recovery' $run.Text
            Assert-True (Test-Path -LiteralPath $fx.Lock) 'W17-lock-remains' 'lock missing'
            $lock = ''
            if (Test-Path -LiteralPath $fx.Lock) { $lock = [System.IO.File]::ReadAllText($fx.Lock) }
            Assert-True ($lock.Contains('reason=image-moved') -and $lock.Contains('previousImage=sha256:c1745previous')) 'W17-lock-records-running-image' $lock
            Assert-NoStop $fx 'W17-no-stop'
        }
        default {
            Write-Fail 'known-case' $Case
        }
    }
} catch {
    Write-Fail 'harness' $_.Exception.Message
} finally {
    Stop-C1745Stub $stub
    Remove-C1745Root $fx
}

Finish-C1745
