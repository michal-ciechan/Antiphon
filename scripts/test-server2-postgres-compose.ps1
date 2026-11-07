#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1406 witness for the server2 host Postgres compose and deploy script.

    Renders and validates the compose contract and the refusals. No host
    socket, no volume, no container start. ASCII-only.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path -Parent $here
$deploy = Join-Path $here 'deploy-server2-postgres.ps1'
$compose = Join-Path $repo 'docker-compose.server2-postgres.yml'
$drillCompose = Join-Path $repo 'docker-compose.server2-postgres-drill.yml'
$doc = Join-Path $repo 'docs/docker-stack.md'
$testingDoc = Join-Path $repo 'docs/testing-and-build.md'
$runnerCompose = Join-Path $repo 'docker-compose.server2-runner.yml'
$script:Passed = 0
$script:Failed = 0
$script:Image = 'postgres:16@sha256:' + ('a' * 64)
$script:Bind = '100.64.0.2'
$script:Password = 'fixture-password-1406'

function Write-Pass {
    param([string]$Name)
    $script:Passed++
    Write-Host ('PASS ' + $Name)
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:Failed++
    Write-Host ('FAIL ' + $Name + ' - ' + $Detail)
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name } else { Write-Fail $Name $Detail }
}

function Set-Mode {
    param([string]$Path, [string]$Octal)
    if ($env:OS -eq 'Windows_NT') { throw 'unix mode is not a Windows property' }
    $bits = [Convert]::ToInt32($Octal, 8)
    [System.IO.File]::SetUnixFileMode($Path, [System.IO.UnixFileMode]$bits)
}

function New-Root {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1406-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $bin = Join-Path $root 'bin'
    $marker = Join-Path $root 'marker'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    Set-Content -LiteralPath $marker -Value '' -Encoding ASCII
    foreach ($pair in @(
            @{ Name = 'docker'; Body = @(
                    '#!/bin/sh',
                    'printf "%s\n" "$*" >> "$CARD1406_MARKER"',
                    'if [ "$1" = "info" ]; then printf "%s\n" "not-the-host"; exit 0; fi',
                    'exit 99'
                ) },
            @{ Name = 'hostname'; Body = @(
                    '#!/bin/sh',
                    'printf "%s\n" "fixture-host"',
                    'exit 0'
                ) },
            @{ Name = 'tailscale'; Body = @(
                    '#!/bin/sh',
                    'printf "%s\n" "called" >> "$CARD1406_MARKER"',
                    'exit 99'
                ) }
        )) {
        $tool = Join-Path $bin $pair.Name
        $pair.Body | Set-Content -LiteralPath $tool -Encoding ASCII
        Set-Mode $tool '755'
    }
    return @{ Root = $root; Bin = $bin; Marker = $marker }
}

function Remove-Root {
    param($Root)
    if ($Root -and (Test-Path -LiteralPath $Root.Root)) {
        Remove-Item -LiteralPath $Root.Root -Recurse -Force
    }
}

function New-EnvFile {
    param($Root, [string]$Bind = $script:Bind, [string]$Image = $script:Image, [string]$Password = $script:Password, [string]$Mode = '600', [switch]$OmitBind, [switch]$EmptyBind)
    $path = Join-Path $Root.Root 'postgres.env'
    $lines = New-Object System.Collections.Generic.List[string]
    if ($EmptyBind) { [void]$lines.Add('ANTIPHON_POSTGRES_BIND_ADDRESS=') }
    elseif (-not $OmitBind) { [void]$lines.Add('ANTIPHON_POSTGRES_BIND_ADDRESS=' + $Bind) }
    [void]$lines.Add('ANTIPHON_POSTGRES_IMAGE=' + $Image)
    [void]$lines.Add('POSTGRES_PASSWORD=' + $Password)
    [System.IO.File]::WriteAllText($path, ($lines -join "`n") + "`n")
    Set-Mode $path $Mode
    return $path
}

function Invoke-Deploy {
    param($Root, [string[]]$Arguments)
    if (-not (Test-Path -LiteralPath $deploy)) {
        return @{ Exit = 99; Out = 'SERVER2-POSTGRES fail exit=99 reason=missing-script'; Err = '' }
    }
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $pwsh
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $repo
    foreach ($arg in @('-NoProfile', '-File', $deploy) + @($Arguments)) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $pathKey = 'PATH'
    if (-not $psi.Environment.ContainsKey('PATH') -and $psi.Environment.ContainsKey('Path')) { $pathKey = 'Path' }
    $psi.Environment[$pathKey] = $Root.Bin
    $psi.Environment['CARD1406_MARKER'] = $Root.Marker
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return @{ Exit = $proc.ExitCode; Out = $stdout; Err = $stderr }
}

function Get-Reason {
    param($Result)
    $match = [regex]::Match([string]$Result.Out, '(?m)^SERVER2-POSTGRES .*reason=([A-Za-z0-9-]+)')
    if ($match.Success) { return $match.Groups[1].Value }
    return ''
}

function Get-Marker {
    param($Root)
    if (-not (Test-Path -LiteralPath $Root.Marker)) { return '' }
    return [string](Get-Content -LiteralPath $Root.Marker -Raw)
}

function Get-RenderedFields {
    param([string]$ComposePath, [string]$ProjectName, [string]$EnvPath)
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $docker) { return @{ Ok = $false; Detail = 'missing-docker' } }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $docker.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('compose', '--env-file', $EnvPath, '-f', $ComposePath, '-p', $ProjectName, 'config')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $stdout = $proc.StandardOutput.ReadToEnd()
    [void]$proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) { return @{ Ok = $false; Detail = ('exit=' + $proc.ExitCode) } }
    $norm = $stdout.Replace("`r`n", "`n")
    $volume = ''
    $vol = [regex]::Match($norm, '(?m)^volumes:\n  pgdata:\n    name: (\S+)')
    if ($vol.Success) { $volume = $vol.Groups[1].Value }
    $hostIp = ''
    $hip = [regex]::Match($norm, '(?m)^[ ]+host_ip: (\S+)')
    if ($hip.Success) { $hostIp = $hip.Groups[1].Value }
    $published = ''
    $pub = [regex]::Match($norm, '(?m)^[ ]+published: "([0-9]+)"')
    if ($pub.Success) { $published = $pub.Groups[1].Value }
    $network = ''
    $net = [regex]::Match($norm, '(?m)^networks:\n  \S+:\n    name: (\S+)')
    if ($net.Success) { $network = $net.Groups[1].Value }
    return @{ Ok = $true; Volume = $volume; HostIp = $hostIp; Published = $published; Network = $network; Detail = '' }
}

function Assert-NoDocker {
    param($Root, [string]$Name, $Result)
    $marker = Get-Marker $Root
    Assert-True ([string]::IsNullOrWhiteSpace($marker)) $Name ('marker=' + $marker.Trim())
    Assert-True ($Result.Out -notmatch [regex]::Escape($script:Password)) ($Name + ' hides the fixture password') 'stdout contained the fixture password'
    Assert-True ($Result.Err -notmatch [regex]::Escape($script:Password)) ($Name + ' hides the fixture password on stderr') 'stderr contained the fixture password'
}

function Get-DrillCommandBodies {
    param([string]$Text)
    $bodies = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($Text -split "`n")) {
        $trim = $line.TrimEnd("`r")
        $mark = ' :: '
        $at = $trim.IndexOf($mark)
        if ($trim.StartsWith('drill-command:') -and $at -ge 0) {
            [void]$bodies.Add($trim.Substring($at + $mark.Length))
        }
    }
    return @($bodies)
}

function Get-DocDrillFence {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $at = $text.IndexOf('### Scratch restore drill')
    if ($at -lt 0) { return @() }
    $needle = '```text' + [char]10
    $fenceAt = $text.IndexOf($needle, $at)
    if ($fenceAt -lt 0) { return @() }
    $bodyAt = $fenceAt + $needle.Length
    $endNeedle = [char]10 + '```'
    $end = $text.IndexOf($endNeedle, $bodyAt)
    if ($end -lt 0) { return @() }
    $body = $text.Substring($bodyAt, $end - $bodyAt)
    return @($body -split "`n" | Where-Object { $_.Length -gt 0 })
}

function Get-DrillSection {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $at = $text.IndexOf('### Scratch restore drill')
    if ($at -lt 0) { return '' }
    $nextNeedle = [char]10 + '## '
    $next = $text.IndexOf($nextNeedle, $at + 1)
    if ($next -lt 0) { return $text.Substring($at) }
    return $text.Substring($at, $next - $at)
}

function Invoke-PsqlQuoteProbe {
    param([string]$PsqlLine)
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('card1406-sh-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    try {
        $stub = Join-Path $dir 'psql'
        $stubBody = @'
#!/bin/sh
prev=
for a in "$@"; do
  if [ "$prev" = "-c" ]; then
    printf '%s' "$a"
    exit 0
  fi
  prev=$a
done
exit 2
'@
        [System.IO.File]::WriteAllText($stub, ($stubBody -replace "`r`n", "`n"))
        Set-Mode $stub '755'
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = 'sh'
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        [void]$psi.ArgumentList.Add('-c')
        [void]$psi.ArgumentList.Add($PsqlLine)
        $psi.Environment['PATH'] = $dir
        $proc = [System.Diagnostics.Process]::new()
        $proc.StartInfo = $psi
        [void]$proc.Start()
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
        $proc.WaitForExit()
        return @{ Exit = $proc.ExitCode; Out = $stdout; Err = $stderr }
    } finally {
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    }
}

$cases = @(
    'compose-contract',
    'render',
    'refuse-bind-any',
    'refuse-bind-empty',
    'refuse-bind-omitted',
    'refuse-volume-pgdata',
    'refuse-image-floating',
    'refuse-image-alpine',
    'whatif-no-docker',
    'whatif-drill',
    'execute-off-host',
    'env-mode',
    'dev-password',
    'drill-volume-guard',
    'tailnet-guard',
    'ambiguous-mode',
    'doc-sentence',
    'ascii',
    'network-block',
    'drill-compose',
    'drill-render',
    'drill-commands',
    'project-refusal',
    'refuse-bind-cgnat',
    'image-repodigest'
)

function Invoke-Case {
    param([string]$Name)
    switch ($Name) {
        'compose-contract' {
            Assert-True (Test-Path -LiteralPath $compose) 'compose file exists' $compose
            if (-not (Test-Path -LiteralPath $compose)) { return }
            $text = [System.IO.File]::ReadAllText($compose)
            foreach ($need in @(
                    'name: antiphon-server2-postgres',
                    'ANTIPHON_POSTGRES_BIND_ADDRESS:?',
                    'ANTIPHON_POSTGRES_IMAGE:?',
                    'POSTGRES_PASSWORD:?',
                    ':5432:5432',
                    'mem_limit: 4g',
                    'cpus: 4.0',
                    'restart: unless-stopped',
                    'pg_isready -U antiphon -d antiphon',
                    'shared_preload_libraries=pg_stat_statements',
                    'pg_stat_statements.track=top',
                    'pg_stat_statements.max=5000',
                    "networks:`n  antiphon-server2:`n    name: antiphon-server2",
                    "    networks:`n      - antiphon-server2",
                    'pgdata:/var/lib/postgresql/data',
                    'name: antiphon-server2-postgres_pgdata',
                    'interval: 5s',
                    'timeout: 5s',
                    'retries: 12',
                    'start_period: 10s'
                )) {
                Assert-True ($text.Contains($need)) ('compose contains ' + $need) 'missing'
            }
            Assert-True ($text -notmatch '5433') 'compose omits port 5433' '5433 present'
            Assert-True ($text -notmatch '(?i)alpine') 'compose omits alpine' 'alpine present'
            Assert-True ($text -notmatch '0\.0\.0\.0') 'compose omits 0.0.0.0' '0.0.0.0 present'
            Assert-True ($text -notmatch 'antiphon_pgdata') 'compose omits antiphon_pgdata' 'antiphon_pgdata present'
            Assert-True ($text -notmatch 'privileged:') 'compose is not privileged' 'privileged present'
            Assert-True ($text -notmatch 'docker\.sock') 'compose mounts no docker socket' 'socket present'
        }
        'render' {
            Assert-True (Test-Path -LiteralPath $deploy) 'deploy script exists for render' $deploy
            if (-not (Test-Path -LiteralPath $deploy)) { return }
            . $deploy
            $text = [System.IO.File]::ReadAllText($compose)
            $rendered = Expand-Server2PostgresCompose -Text $text -Bind $script:Bind -Image $script:Image -Password $script:Password
            Assert-True ($rendered.Ok) 'render accepts a digest pin and a tailnet address' $rendered.Reason
            Assert-True ($rendered.Redacted.Contains($script:Bind + ':5432:5432')) 'rendered port is the bind on 5432' 'port missing'
            Assert-True ($rendered.Redacted.Contains($script:Image)) 'rendered image is the digest pin' 'image missing'
            Assert-True ($rendered.Redacted -notmatch '0\.0\.0\.0') 'rendered port is not 0.0.0.0' 'any address present'
            Assert-True ($rendered.Redacted -notmatch [regex]::Escape($script:Password)) 'rendered view redacts the password' 'password visible'
            $empty = Expand-Server2PostgresCompose -Text $text -Bind '' -Image $script:Image -Password $script:Password
            Assert-True ((-not $empty.Ok) -and $empty.Reason -eq 'empty-bind') 'render refuses an empty bind' $empty.Reason
            $any = Expand-Server2PostgresCompose -Text $text -Bind '0.0.0.0' -Image $script:Image -Password $script:Password
            Assert-True ((-not $any.Ok) -and $any.Reason -eq 'bind-any') 'render refuses 0.0.0.0' $any.Reason
            $float = Expand-Server2PostgresCompose -Text $text -Bind $script:Bind -Image 'postgres:16' -Password $script:Password
            Assert-True ((-not $float.Ok) -and $float.Reason -eq 'image-floating') 'render refuses a floating tag' $float.Reason
            $alpine = Expand-Server2PostgresCompose -Text $text -Bind $script:Bind -Image ('postgres:16-alpine@sha256:' + ('b' * 64)) -Password $script:Password
            Assert-True ((-not $alpine.Ok) -and $alpine.Reason -eq 'image-alpine') 'render refuses alpine' $alpine.Reason
        }
        'refuse-bind-any' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -Bind '0.0.0.0'
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'bind-any') 'WhatIf refuses 0.0.0.0' (Get-Reason $result)
                Assert-NoDocker $root 'bind-any' $result
            } finally { Remove-Root $root }
        }
        'refuse-bind-empty' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -EmptyBind
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'empty-bind') 'WhatIf refuses an empty bind' (Get-Reason $result)
                Assert-NoDocker $root 'empty-bind' $result
            } finally { Remove-Root $root }
        }
        'refuse-bind-omitted' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -OmitBind
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'bind-omitted') 'WhatIf refuses an omitted bind' (Get-Reason $result)
                Assert-NoDocker $root 'omitted-bind' $result
            } finally { Remove-Root $root }
        }
        'refuse-volume-pgdata' {
            Assert-True (Test-Path -LiteralPath $compose) 'compose file exists for the volume refusal' $compose
            if (-not (Test-Path -LiteralPath $compose)) { return }
            $root = New-Root
            try {
                $copy = Join-Path $root.Root 'compose.yml'
                $text = [System.IO.File]::ReadAllText($compose).Replace('antiphon-server2-postgres_pgdata', 'antiphon_pgdata')
                [System.IO.File]::WriteAllText($copy, $text)
                $envFile = New-EnvFile $root
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile, '-ComposeFile', $copy)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'volume-forbidden') 'WhatIf refuses antiphon_pgdata' (Get-Reason $result)
                Assert-NoDocker $root 'volume' $result
            } finally { Remove-Root $root }
        }
        'refuse-image-floating' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -Image 'postgres:16'
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'image-floating') 'WhatIf refuses a floating tag' (Get-Reason $result)
                Assert-NoDocker $root 'floating' $result
            } finally { Remove-Root $root }
        }
        'refuse-image-alpine' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -Image ('postgres:16-alpine@sha256:' + ('b' * 64))
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'image-alpine') 'WhatIf refuses alpine' (Get-Reason $result)
                Assert-NoDocker $root 'alpine' $result
            } finally { Remove-Root $root }
        }
        'whatif-no-docker' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                $reason = Get-Reason $result
                Assert-True ($result.Exit -eq 0 -and $reason -eq 'ok') 'WhatIf exits 0' ('exit=' + $result.Exit + ' reason=' + $reason + ' err=' + $result.Err)
                Assert-True ($result.Out.Contains('docker compose') -and $result.Out.Contains('-p antiphon-server2-postgres') -and $result.Out.Contains('up -d')) 'WhatIf prints the host-daemon command' 'command missing'
                Assert-True ($result.Out.Contains('docker-compose.server2-postgres.yml')) 'WhatIf names the compose file' 'file missing'
                Assert-NoDocker $root 'whatif' $result
                $plain = Invoke-Deploy $root @()
                Assert-True ($plain.Exit -eq 0 -and (Get-Reason $plain) -eq 'ok') 'default mode is WhatIf' (Get-Reason $plain)
                Assert-NoDocker $root 'default' $plain
            } finally { Remove-Root $root }
        }
        'whatif-drill' {
            $root = New-Root
            try {
                $result = Invoke-Deploy $root @('-WhatIf', '-RestoreDrill')
                Assert-True ($result.Exit -eq 0 -and (Get-Reason $result) -eq 'ok') 'drill WhatIf exits 0' (Get-Reason $result)
                foreach ($need in @(
                        'antiphon-server2-postgres-drill',
                        'antiphon-server2-postgres-drill_pgdata',
                        'scratch-only',
                        'unread: docker-ps',
                        'unread: docker-volume-ls',
                        'unread: docker-data-root-df',
                        'unread: windmill-compose-parent',
                        'unread: count-star',
                        'unread: dump-elapsed-sec',
                        'unread: restore-elapsed-sec',
                        'unread: copy-elapsed-sec',
                        'unread: dump-bytes',
                        'phase3: user-table-count',
                        'phase3: sequence-last-value',
                        'phase3: extension-version',
                        'phase3: ef-migrations-history',
                        'phase3: key-table-count-min-max-pk'
                    )) {
                    Assert-True ($result.Out.Contains($need)) ('drill plan contains ' + $need) 'missing'
                }
                Assert-True ($result.Out -notmatch 'TranscriptEntries=\d') 'drill plan does not print transcript rows' 'row body present'
                Assert-NoDocker $root 'drill' $result
            } finally { Remove-Root $root }
        }
        'execute-off-host' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root
                $result = Invoke-Deploy $root @('-Execute', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'not-server2-host') 'Execute refuses off the server2 host' (Get-Reason $result)
                $marker = Get-Marker $root
                Assert-True ($marker -notmatch 'compose') 'Execute off-host does not compose up' $marker.Trim()
                Assert-True ($marker -notmatch 'volume') 'Execute off-host does not touch a volume' $marker.Trim()
                Assert-True ($result.Out -notmatch [regex]::Escape($script:Password)) 'Execute off-host hides the fixture password' 'password visible'
            } finally { Remove-Root $root }
        }
        'env-mode' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -Mode '644'
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'env-mode') 'WhatIf refuses a mode other than 0600' (Get-Reason $result)
                Assert-NoDocker $root 'env-mode' $result
            } finally { Remove-Root $root }
        }
        'dev-password' {
            $root = New-Root
            try {
                $devLine = @(Select-String -LiteralPath (Join-Path $repo 'docker-compose.dev.yml') -Pattern 'POSTGRES_PASSWORD:\s*(\S+)' | Select-Object -First 1)
                $dev = $devLine.Matches[0].Groups[1].Value
                $envFile = New-EnvFile $root -Password $dev
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'dev-password') 'WhatIf refuses the committed dev password' (Get-Reason $result)
                Assert-True ($result.Out -notmatch [regex]::Escape($dev)) 'dev-password refusal does not print the password' 'password visible'
                Assert-True ($result.Err -notmatch [regex]::Escape($dev)) 'dev-password refusal does not print the password on stderr' 'password visible'
                Assert-NoDocker $root 'dev-password' $result
            } finally { Remove-Root $root }
        }
        'drill-volume-guard' {
            Assert-True (Test-Path -LiteralPath $deploy) 'deploy script exists for the drill guard' $deploy
            if (-not (Test-Path -LiteralPath $deploy)) { return }
            . $deploy
            foreach ($name in @('antiphon_pgdata', 'antiphon-server2-postgres_pgdata', 'pgdata', 'dind-data', 'windmill_db')) {
                $reason = Test-Server2PostgresDrillVolume $name
                Assert-True ($reason -eq 'volume-forbidden') ('drill refuses ' + $name) $reason
            }
            Assert-True ((Test-Server2PostgresDrillVolume 'antiphon-server2-postgres-drill_pgdata') -eq '') 'drill allows its own scratch volume' (Test-Server2PostgresDrillVolume 'antiphon-server2-postgres-drill_pgdata')
            $source = [System.IO.File]::ReadAllText($deploy)
            Assert-True ($source -notmatch 'volume rm antiphon_pgdata') 'script has no antiphon_pgdata volume rm' 'rm present'
            Assert-True ($source -notmatch 'volume rm antiphon-server2-postgres_pgdata') 'script has no production volume rm' 'rm present'
        }
        'tailnet-guard' {
            Assert-True (Test-Path -LiteralPath $deploy) 'deploy script exists for the tailnet guard' $deploy
            if (-not (Test-Path -LiteralPath $deploy)) { return }
            . $deploy
            Assert-True ((Test-Server2PostgresTailnet $script:Bind ($script:Bind + "`n")) -eq '') 'tailnet match is accepted' 'rejected'
            Assert-True ((Test-Server2PostgresTailnet '192.168.4.34' "100.64.0.2`n") -eq 'bind-not-tailnet') 'a LAN address is not the tailnet address' (Test-Server2PostgresTailnet '192.168.4.34' "100.64.0.2`n")
            Assert-True ((Test-Server2PostgresTailnet $script:Bind '') -eq 'bind-not-tailnet') 'an empty tailscale reading is refused' 'accepted'
        }
        'ambiguous-mode' {
            $root = New-Root
            try {
                $result = Invoke-Deploy $root @('-WhatIf', '-Execute')
                Assert-True ($result.Exit -ne 0 -and (Get-Reason $result) -eq 'ambiguous-mode') 'WhatIf and Execute together refuse' (Get-Reason $result)
                Assert-NoDocker $root 'ambiguous' $result
            } finally { Remove-Root $root }
        }
        'doc-sentence' {
            Assert-True (Test-Path -LiteralPath $doc) 'docker-stack doc exists' $doc
            if (-not (Test-Path -LiteralPath $doc)) { return }
            $text = [System.IO.File]::ReadAllText($doc)
            Assert-True ($text.Contains('docker-compose.server2-postgres.yml')) 'doc names the host compose file' 'missing'
            Assert-True ($text.Contains('antiphon-server2-postgres')) 'doc names the compose project' 'missing'
            Assert-True ($text.Contains('exists beside the runner')) 'doc says the service exists beside the runner' 'missing'
            Assert-True ($text.Contains('not the nested')) 'doc says the service is not the nested daemon' 'missing'
            Assert-True ($text.Contains('not Windmill')) 'doc says the service is not Windmill' 'missing'
            Assert-True ($text.Contains('antiphon-server2-postgres-drill')) 'doc names the scratch project' 'missing'
            Assert-True ($text -notmatch 'no server, no Postgres') 'doc no longer says server2 has no Postgres' 'old sentence remains'
            $runner = [System.IO.File]::ReadAllText($runnerCompose)
            Assert-True ($runner -notmatch 'no server, no Postgres') 'runner compose no longer says there is no Postgres' 'old sentence remains'
            Assert-True ($runner.Contains('CARD-1406')) 'runner compose points at the host Postgres project' 'missing'
            $exitCodes = 'Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode.'
            Assert-True ($text.Contains($exitCodes)) 'doc names the deploy exit codes' 'missing'
            $header = ''
            if (Test-Path -LiteralPath $deploy) { $header = [System.IO.File]::ReadAllText($deploy) }
            Assert-True ($header.Contains($exitCodes)) 'deploy script documents exit codes' 'missing'
            $guide = ''
            if (Test-Path -LiteralPath $testingDoc) { $guide = [System.IO.File]::ReadAllText($testingDoc) }
            Assert-True ($guide -notmatch 'no server\s+and no Postgres') 'testing guide no longer says server2 has no Postgres' 'old sentence remains'
            Assert-True ($guide.Contains('not yet the control plane')) 'testing guide says the host Postgres is not yet the control plane' 'missing'
        }
        'ascii' {
            foreach ($path in @($deploy, $compose, $drillCompose, (Join-Path $here 'test-server2-postgres-compose.ps1'))) {
                if (-not (Test-Path -LiteralPath $path)) {
                    Write-Fail ('ascii ' + $path) 'missing'
                    continue
                }
                $bytes = [System.IO.File]::ReadAllBytes($path)
                $high = 0
                foreach ($b in $bytes) { if ($b -gt 127) { $high++ } }
                Assert-True ($high -eq 0) ('ascii ' + [System.IO.Path]::GetFileName($path)) ('non-ascii=' + $high)
            }
        }
        'network-block' {
            Assert-True (Test-Path -LiteralPath $compose) 'compose file exists for the network block' $compose
            Assert-True (Test-Path -LiteralPath $deploy) 'deploy script exists for the network block' $deploy
            if (-not (Test-Path -LiteralPath $compose) -or -not (Test-Path -LiteralPath $deploy)) { return }
            $source = [System.IO.File]::ReadAllText($deploy)
            $block = 'networks:`n  antiphon-server2:`n    name: antiphon-server2'
            $attached = '    networks:`n      - antiphon-server2'
            Assert-True ($source.Contains($block)) 'deploy refusal requires the exact network block' 'substring only'
            Assert-True ($source.Contains($attached)) 'deploy refusal requires the service network attachment' 'substring only'
            $root = New-Root
            try {
                $copy = Join-Path $root.Root 'compose.yml'
                $text = [System.IO.File]::ReadAllText($compose).Replace("`r`n", "`n")
                $text = $text.Replace("    networks:`n      - antiphon-server2`n", '')
                $text = $text.Replace("networks:`n  antiphon-server2:`n    name: antiphon-server2`n`n", '')
                [System.IO.File]::WriteAllText($copy, $text)
                $stripped = [System.IO.File]::ReadAllText($copy)
                Assert-True (-not $stripped.Contains("networks:`n  antiphon-server2:`n    name: antiphon-server2")) 'stripped compose has no network block' 'block remains'
                $envFile = New-EnvFile $root
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile, '-ComposeFile', $copy)
                Assert-True ($result.Exit -eq 2 -and (Get-Reason $result) -eq 'compose-invalid') 'WhatIf refuses a compose with no network block' (Get-Reason $result)
                Assert-NoDocker $root 'network-block' $result
            } finally { Remove-Root $root }
        }
        'drill-compose' {
            $text = ''
            if (Test-Path -LiteralPath $drillCompose) { $text = [System.IO.File]::ReadAllText($drillCompose).Replace("`r`n", "`n") }
            foreach ($need in @(
                    'name: antiphon-server2-postgres-drill',
                    'name: antiphon-server2-postgres-drill_pgdata',
                    '127.0.0.1:5434:5432',
                    "networks:`n  antiphon-server2-postgres-drill:`n    name: antiphon-server2-postgres-drill",
                    "    networks:`n      - antiphon-server2-postgres-drill",
                    'ANTIPHON_POSTGRES_IMAGE:?',
                    'POSTGRES_PASSWORD:?'
                )) {
                Assert-True ($text.Contains($need)) ('drill compose contains ' + $need) 'missing'
            }
            Assert-True (($text.Length -gt 0) -and ($text -notmatch 'name: antiphon-server2-postgres_pgdata')) 'drill compose does not pin the production volume' 'pinned or missing'
            Assert-True (($text.Length -gt 0) -and ($text -notmatch 'antiphon_pgdata')) 'drill compose omits antiphon_pgdata' 'present or missing'
            Assert-True (($text.Length -gt 0) -and ($text -notmatch ':5432:5432')) 'drill compose does not publish the production port' 'present or missing'
            Assert-True (($text.Length -gt 0) -and ($text -notmatch '0\.0\.0\.0')) 'drill compose omits 0.0.0.0' 'present or missing'
            Assert-True (($text.Length -gt 0) -and ($text -notmatch '5433')) 'drill compose omits port 5433' 'present or missing'
            Assert-True (($text.Length -gt 0) -and (-not $text.Contains('ANTIPHON_POSTGRES_BIND_ADDRESS'))) 'drill bind is not the production tailnet variable' 'present or missing'
            Assert-True (($text.Length -gt 0) -and (-not $text.Contains("networks:`n  antiphon-server2:`n    name: antiphon-server2"))) 'drill compose does not define the production network' 'present or missing'
        }
        'drill-render' {
            $root = New-Root
            try {
                $envFile = New-EnvFile $root
                $prod = Get-RenderedFields $compose 'antiphon-server2-postgres-drill' $envFile
                Assert-True ($prod.Ok) 'production compose renders under the drill project' $prod.Detail
                Assert-True ($prod.Volume -eq 'antiphon-server2-postgres_pgdata') 'production compose under the drill project mounts the production volume' $prod.Volume
                Assert-True (Test-Path -LiteralPath $drillCompose) 'scratch drill compose exists for render' $drillCompose
                if (-not (Test-Path -LiteralPath $drillCompose)) { return }
                $drill = Get-RenderedFields $drillCompose 'antiphon-server2-postgres-drill' $envFile
                Assert-True ($drill.Ok) 'drill compose renders under the drill project' $drill.Detail
                Assert-True ($drill.Volume -eq 'antiphon-server2-postgres-drill_pgdata') 'drill compose under the drill project mounts the scratch volume' $drill.Volume
                Assert-True (($drill.Volume -ne 'antiphon-server2-postgres_pgdata') -and ($drill.Volume -ne 'antiphon_pgdata')) 'drill render does not mount a production volume' $drill.Volume
                Assert-True (($drill.HostIp -eq '127.0.0.1') -and ($drill.Published -eq '5434')) 'drill publish is loopback port 5434' ($drill.HostIp + ':' + $drill.Published)
                Assert-True ($drill.Network -eq 'antiphon-server2-postgres-drill') 'drill network is the scratch network' $drill.Network
                $asProd = Get-RenderedFields $drillCompose 'antiphon-server2-postgres' $envFile
                Assert-True ($asProd.Ok -and ($asProd.Volume -eq 'antiphon-server2-postgres-drill_pgdata')) 'drill compose under the production project still mounts the scratch volume' $asProd.Volume
            } finally { Remove-Root $root }
        }
        'drill-commands' {
            $root = New-Root
            try {
                $result = Invoke-Deploy $root @('-WhatIf', '-RestoreDrill')
                Assert-True ($result.Exit -eq 0 -and (Get-Reason $result) -eq 'ok') 'drill command plan exits 0' (Get-Reason $result)
                foreach ($need in @(
                        'docker-compose.server2-postgres-drill.yml',
                        '-p antiphon-server2-postgres-drill up -d',
                        '-p antiphon-server2-postgres-drill down',
                        'docker volume create antiphon-server2-postgres-drill_pgdata',
                        'docker volume rm antiphon-server2-postgres-drill_pgdata',
                        '*-drill_pgdata',
                        'postgres-snapshot.ps1 -Execute',
                        '.dump.age',
                        'age -d -i "$AGE_IDENTITY"',
                        'scp "$SNAPSHOT"',
                        'copy_start=$(date +%s)',
                        'chmod 0600',
                        'shred -u',
                        'pg_restore -U antiphon -d antiphon --no-owner --no-acl',
                        'antiphon-server2-postgres-drill-postgres-1',
                        'pg_stat_user_tables',
                        '__EFMigrationsHistory',
                        '\"__EFMigrationsHistory\"',
                        'pg_extension'
                    )) {
                    Assert-True ($result.Out.Contains($need)) ('drill command contains ' + $need) 'missing'
                }
                Assert-True ($result.Out -notmatch 'pg_dump') 'drill plan does not print a plaintext pg_dump' 'pg_dump present'
                Assert-True ($result.Out -notmatch 'AGE-SECRET-KEY-') 'drill plan does not print an age key' 'key text present'
                Assert-True (-not ($result.Out -match '(?m)>\s*\S+\.dump(?!\.age)')) 'no command writes a plaintext dump' 'plaintext redirect present'
                Assert-True (-not ($result.Out -match 'shred[^\n]*\.dump\.age')) 'shred does not delete the ciphertext' 'ciphertext shredded'
                $section = Get-DrillSection $doc
                Assert-True ($section.Contains('encrypted')) 'drill section says encrypted' 'missing'
                Assert-True ($result.Out -notmatch 'docker-compose\.server2-postgres\.yml') 'drill plan does not name the production compose' 'production compose named'
                Assert-True ($result.Out -notmatch '-p antiphon-server2-postgres up') 'drill plan does not start the production project' 'production up present'
                Assert-True ($result.Out -notmatch 'antiphon-server2-postgres_pgdata') 'drill plan does not name the production volume' 'production volume named'
                Assert-True ($result.Out -notmatch 'antiphon_pgdata') 'drill plan does not name antiphon_pgdata' 'desktop volume named'
                $lines = @($result.Out -split "`n" | Where-Object { $_ -match 'drill-command:' })
                Assert-True ($lines.Count -ge 9) 'drill prints one line per command' ('count=' + $lines.Count)
                foreach ($line in $lines) {
                    Assert-True (($line.Contains('antiphon-server2-postgres-drill')) -and ($line.Contains('antiphon-server2-postgres-drill_pgdata'))) 'drill command names the drill project and volume' $line.Trim()
                    Assert-True ($line -notmatch 'antiphon-server2-postgres_pgdata') 'drill command cannot reach the production volume' $line.Trim()
                }
                $bodies = @(Get-DrillCommandBodies $result.Out)
                $docCmds = @(Get-DocDrillFence $doc)
                Assert-True ($docCmds.Count -gt 0) 'doc drill fence has commands' ('count=' + $docCmds.Count)
                $same = ($docCmds.Count -eq $bodies.Count) -and ($docCmds.Count -gt 0)
                $mismatch = 'script=' + $bodies.Count + ' doc=' + $docCmds.Count
                $limit = [Math]::Min($docCmds.Count, $bodies.Count)
                for ($i = 0; $i -lt $limit; $i++) {
                    if ($docCmds[$i] -cne $bodies[$i]) {
                        $same = $false
                        $mismatch = 'line ' + ($i + 1) + ' script=[' + $bodies[$i] + '] doc=[' + $docCmds[$i] + ']'
                        break
                    }
                }
                Assert-True ($same) 'printed drill commands equal the doc fence' $mismatch
                $dump = @($lines | Where-Object { $_ -match 'antiphon-postgres' })
                Assert-True ($dump.Count -eq 1) 'the desktop database is named once' ('count=' + $dump.Count)
                if ($dump.Count -eq 1) {
                    Assert-True (($dump[0] -match 'postgres-snapshot\.ps1') -and ($dump[0] -match '-Execute') -and ($dump[0] -match '-RecipientFile "\$AGE_RECIPIENT"') -and ($dump[0] -notmatch 'pg_dump|>|psql|pg_restore|dropdb|createdb|DELETE|INSERT|UPDATE|DROP')) 'the desktop step takes the encrypted snapshot' $dump[0].Trim()
                }
                $migBodies = @($bodies | Where-Object { $_ -match '__EFMigrationsHistory' })
                Assert-True ($migBodies.Count -eq 1) 'one migrations command' ('count=' + $migBodies.Count)
                $docMig = @($docCmds | Where-Object { $_ -match '__EFMigrationsHistory' })
                if ($migBodies.Count -eq 1 -and $docMig.Count -eq 1) {
                    Assert-True ($migBodies[0] -ceq $docMig[0]) 'printed migrations line equals the doc line' $migBodies[0]
                    $psqlLine = [regex]::Replace($migBodies[0], '^docker exec \S+ ', '')
                    Assert-True ($psqlLine.StartsWith('psql ')) 'migrations line is a psql command' $psqlLine
                    $probe = Invoke-PsqlQuoteProbe $psqlLine
                    $wanted = 'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId"'
                    Assert-True (($probe.Exit -eq 0) -and ($probe.Out -ceq $wanted)) 'sh keeps the migrations identifier quotes' ('exit=' + $probe.Exit + ' out=[' + $probe.Out + '] err=[' + $probe.Err + ']')
                } else {
                    Assert-True $false 'printed migrations line equals the doc line' 'missing migrations line'
                    Assert-True $false 'sh keeps the migrations identifier quotes' 'missing migrations line'
                }
                Assert-NoDocker $root 'drill-commands' $result
            } finally { Remove-Root $root }
        }
        'project-refusal' {
            $root = New-Root
            try {
                $wrongProject = Invoke-Deploy $root @('-WhatIf', '-Project', 'antiphon-server2-postgres-drill')
                Assert-True ($wrongProject.Exit -eq 2 -and (Get-Reason $wrongProject) -eq 'production-compose-wrong-project') 'WhatIf refuses the production compose under the drill project' (Get-Reason $wrongProject)
                Assert-NoDocker $root 'wrong-project' $wrongProject
                $wrongDrill = Invoke-Deploy $root @('-WhatIf', '-RestoreDrill', '-Project', 'antiphon-server2-postgres')
                Assert-True ($wrongDrill.Exit -eq 2 -and (Get-Reason $wrongDrill) -eq 'drill-compose-wrong-project') 'WhatIf refuses the drill under the production project' (Get-Reason $wrongDrill)
                Assert-NoDocker $root 'wrong-drill-project' $wrongDrill
                $prodFile = Invoke-Deploy $root @('-WhatIf', '-RestoreDrill', '-ComposeFile', $compose)
                Assert-True ($prodFile.Exit -eq 2 -and (Get-Reason $prodFile) -eq 'production-compose-wrong-project') 'RestoreDrill refuses the production compose file' (Get-Reason $prodFile)
                Assert-NoDocker $root 'prod-file-drill' $prodFile
                $drillFile = Invoke-Deploy $root @('-WhatIf', '-ComposeFile', $drillCompose)
                Assert-True ($drillFile.Exit -eq 2 -and (Get-Reason $drillFile) -eq 'drill-compose-wrong-project') 'a normal plan refuses the drill compose file' (Get-Reason $drillFile)
                Assert-NoDocker $root 'drill-file-prod' $drillFile
            } finally { Remove-Root $root }
        }
        'refuse-bind-cgnat' {
            Assert-True (Test-Path -LiteralPath $deploy) 'deploy script exists for the CGNAT check' $deploy
            if (-not (Test-Path -LiteralPath $deploy)) { return }
            . $deploy
            Assert-True ((Test-Server2PostgresBind -Provided $true -Address '100.64.0.0') -eq '') 'CGNAT range bottom is accepted' (Test-Server2PostgresBind -Provided $true -Address '100.64.0.0')
            Assert-True ((Test-Server2PostgresBind -Provided $true -Address '100.127.255.255') -eq '') 'CGNAT range top is accepted' (Test-Server2PostgresBind -Provided $true -Address '100.127.255.255')
            Assert-True ((Test-Server2PostgresBind -Provided $true -Address '100.63.255.255') -eq 'bind-not-cgnat') 'an address below CGNAT is refused' (Test-Server2PostgresBind -Provided $true -Address '100.63.255.255')
            Assert-True ((Test-Server2PostgresBind -Provided $true -Address '100.128.0.1') -eq 'bind-not-cgnat') 'an address above CGNAT is refused' (Test-Server2PostgresBind -Provided $true -Address '100.128.0.1')
            Assert-True ((Test-Server2PostgresBind -Provided $true -Address '192.168.4.34') -eq 'bind-not-cgnat') 'a LAN address is outside CGNAT' (Test-Server2PostgresBind -Provided $true -Address '192.168.4.34')
            Assert-True ((Test-Server2PostgresBind -Provided $true -Address '8.8.8.8') -eq 'bind-not-cgnat') 'a public address is outside CGNAT' (Test-Server2PostgresBind -Provided $true -Address '8.8.8.8')
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -Bind '192.168.4.34'
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -eq 2 -and (Get-Reason $result) -eq 'bind-not-cgnat') 'WhatIf refuses a LAN bind' (Get-Reason $result)
                Assert-NoDocker $root 'cgnat' $result
            } finally { Remove-Root $root }
        }
        'image-repodigest' {
            Assert-True (Test-Path -LiteralPath $deploy) 'deploy script exists for the RepoDigest pin' $deploy
            if (-not (Test-Path -LiteralPath $deploy)) { return }
            . $deploy
            $pin = 'postgres@sha256:' + ('c' * 64)
            Assert-True ((Test-Server2PostgresImage $pin) -eq '') 'RepoDigest postgres@sha256 is a digest pin' (Test-Server2PostgresImage $pin)
            Assert-True ((Test-Server2PostgresImage 'postgres:latest') -eq 'image-floating') 'a floating latest tag is refused' (Test-Server2PostgresImage 'postgres:latest')
            Assert-True ((Test-Server2PostgresImage ('postgres@sha256:' + ('d' * 12))) -eq 'image-floating') 'a short digest is refused' (Test-Server2PostgresImage ('postgres@sha256:' + ('d' * 12)))
            $root = New-Root
            try {
                $envFile = New-EnvFile $root -Image $pin
                $result = Invoke-Deploy $root @('-WhatIf', '-EnvFile', $envFile)
                Assert-True ($result.Exit -eq 0 -and (Get-Reason $result) -eq 'ok') 'WhatIf accepts a postgres RepoDigest pin' (Get-Reason $result)
                Assert-NoDocker $root 'repodigest' $result
            } finally { Remove-Root $root }
        }
        default { Write-Fail $Name 'unknown case' }
    }
}

$selected = @($cases)
if (-not [string]::IsNullOrWhiteSpace($Case)) { $selected = @($Case) }
foreach ($name in $selected) { Invoke-Case $name }
Write-Host ('RESULT passed=' + $script:Passed + ' failed=' + $script:Failed)
if ($script:Failed -gt 0) { exit 1 }
exit 0
