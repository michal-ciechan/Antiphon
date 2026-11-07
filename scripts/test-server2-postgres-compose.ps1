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
$doc = Join-Path $repo 'docs/docker-stack.md'
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

function Assert-NoDocker {
    param($Root, [string]$Name, $Result)
    $marker = Get-Marker $Root
    Assert-True ([string]::IsNullOrWhiteSpace($marker)) $Name ('marker=' + $marker.Trim())
    Assert-True ($Result.Out -notmatch [regex]::Escape($script:Password)) ($Name + ' hides the fixture password') 'stdout contained the fixture password'
    Assert-True ($Result.Err -notmatch [regex]::Escape($script:Password)) ($Name + ' hides the fixture password on stderr') 'stderr contained the fixture password'
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
    'ascii'
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
                    'antiphon-server2',
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
        }
        'ascii' {
            foreach ($path in @($deploy, $compose, (Join-Path $here 'test-server2-postgres-compose.ps1'))) {
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
        default { Write-Fail $Name 'unknown case' }
    }
}

$selected = @($cases)
if (-not [string]::IsNullOrWhiteSpace($Case)) { $selected = @($Case) }
foreach ($name in $selected) { Invoke-Case $name }
Write-Host ('RESULT passed=' + $script:Passed + ' failed=' + $script:Failed)
if ($script:Failed -gt 0) { exit 1 }
exit 0
