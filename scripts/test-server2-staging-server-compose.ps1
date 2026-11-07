#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1742 witness for the server2 host staging server compose and deploy script.

    Renders no daemon. No host socket, no volume, no container start. ASCII-only.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path -Parent $here
$deploy = Join-Path $here 'deploy-server2-staging-server.ps1'
$compose = Join-Path $repo 'docker-compose.server2-staging-server.yml'
$script:Passed = 0
$script:Failed = 0
$script:Image = 'postgres:16@sha256:' + ('a' * 64)
$script:Password = 'fixture-password-1742'

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
    param([string]$InfoName = 'not-the-host')
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1742-' + [guid]::NewGuid().ToString('N'))
    $repoRoot = Join-Path $root 'repo'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    New-Item -ItemType Directory -Path $repoRoot -Force | Out-Null
    $bin = Join-Path $root 'bin'
    $marker = Join-Path $root 'marker'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    Set-Content -LiteralPath $marker -Value '' -Encoding ASCII
    $dockerBody = @(
        '#!/bin/sh',
        'printf "%s\n" "$*" >> "$CARD1742_MARKER"',
        'if [ "$1" = "info" ]; then printf "%s\n" "' + $InfoName + '"; exit 0; fi',
        'exit 99'
    )
    $docker = Join-Path $bin 'docker'
    $dockerBody | Set-Content -LiteralPath $docker -Encoding ASCII
    Set-Mode $docker '755'
    $hostBody = @(
        '#!/bin/sh',
        'printf "%s\n" "fixture-host"',
        'exit 0'
    )
    $hostname = Join-Path $bin 'hostname'
    $hostBody | Set-Content -LiteralPath $hostname -Encoding ASCII
    Set-Mode $hostname '755'
    $curlBody = @(
        '#!/bin/sh',
        'printf "%s\n" "curl $*" >> "$CARD1742_MARKER"',
        'exit 99'
    )
    $curl = Join-Path $bin 'curl'
    $curlBody | Set-Content -LiteralPath $curl -Encoding ASCII
    Set-Mode $curl '755'
    $dev = Join-Path $repoRoot 'docker-compose.dev.yml'
    Set-Content -LiteralPath $dev -Value "POSTGRES_PASSWORD: antiphon_dev`n" -Encoding ASCII
    return @{ Root = $root; Repo = $repoRoot; Bin = $bin; Marker = $marker }
}

function Remove-Root {
    param($Root)
    if ($Root -and (Test-Path -LiteralPath $Root.Root)) {
        Remove-Item -LiteralPath $Root.Root -Recurse -Force
    }
}

function New-EnvFile {
    param($Root, [string]$Image = $script:Image, [string]$Password = $script:Password, [string]$Mode = '600', [switch]$InsideRepo)
    $dir = $Root.Root
    if ($InsideRepo) { $dir = $Root.Repo }
    $path = Join-Path $dir 'staging.env'
    $lines = @(
        'ANTIPHON_STAGING_POSTGRES_IMAGE=' + $Image,
        'POSTGRES_PASSWORD=' + $Password
    )
    [System.IO.File]::WriteAllText($path, ($lines -join "`n") + "`n")
    Set-Mode $path $Mode
    return $path
}

function Invoke-Deploy {
    param($Root, [string[]]$Arguments)
    if (-not (Test-Path -LiteralPath $deploy)) {
        return @{ Exit = 99; Out = 'SERVER2-STAGING fail exit=99 reason=missing-script'; Err = '' }
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
    $psi.Environment['CARD1742_MARKER'] = $Root.Marker
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
    $match = [regex]::Match([string]$Result.Out, '(?m)^SERVER2-STAGING .*reason=([A-Za-z0-9-]+)')
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
    Assert-True ($Result.Out -notmatch 'antiphon_dev') ($Name + ' hides the dev password') 'stdout contained the dev password'
    Assert-True ($Result.Err -notmatch 'antiphon_dev') ($Name + ' hides the dev password on stderr') 'stderr contained the dev password'
}

function Invoke-WhatIfCase {
    $root = New-Root
    try {
        if (-not (Test-Path -LiteralPath $compose)) {
            Assert-True $false 'compose file exists' $compose
            return
        }
        $result = Invoke-Deploy $root @('-WhatIf', '-ComposeFile', $compose, '-RepoRoot', $repo)
        Assert-True ($result.Exit -eq 0) 'WhatIf exits 0' ('exit=' + $result.Exit + ' out=' + $result.Out)
        Assert-True ((Get-Reason $result) -eq 'ok') 'WhatIf reason is ok' (Get-Reason $result)
        Assert-True ($result.Out.Contains('antiphon-server2-staging')) 'WhatIf names the project' 'project missing'
        Assert-True ($result.Out.Contains('docker-compose.server2-staging-server.yml')) 'WhatIf names the compose file' 'file missing'
        Assert-True ($result.Out.Contains('http://127.0.0.1:17420/health')) 'WhatIf names /health' 'health missing'
        Assert-True ($result.Out.Contains('http://127.0.0.1:17420/api/version')) 'WhatIf names /api/version' 'version missing'
        Assert-NoDocker $root 'WhatIf does not call docker' $result
    } finally {
        Remove-Root $root
    }
}

function Invoke-AmbiguousCase {
    $root = New-Root
    try {
        $result = Invoke-Deploy $root @('-WhatIf', '-Execute', '-ComposeFile', $compose, '-RepoRoot', $repo)
        Assert-True ($result.Exit -eq 3) 'both switches exit 3' ('exit=' + $result.Exit)
        Assert-True ((Get-Reason $result) -eq 'ambiguous-mode') 'both switches are ambiguous-mode' (Get-Reason $result)
        Assert-NoDocker $root 'ambiguous mode does not call docker' $result
    } finally {
        Remove-Root $root
    }
}

function Invoke-OffHostCase {
    $root = New-Root -InfoName 'not-the-host'
    try {
        $envFile = New-EnvFile $root
        $result = Invoke-Deploy $root @('-Execute', '-ComposeFile', $compose, '-RepoRoot', $repo, '-EnvFile', $envFile)
        Assert-True ($result.Exit -eq 2) 'off-host Execute exits 2' ('exit=' + $result.Exit + ' out=' + $result.Out)
        Assert-True ((Get-Reason $result) -eq 'not-server2-host') 'off-host reason is not-server2-host' (Get-Reason $result)
        $marker = Get-Marker $root
        Assert-True ($marker -notmatch 'compose') 'off-host Execute does not compose up' $marker
        Assert-True ($marker -notmatch 'curl') 'off-host Execute does not curl' $marker
        Assert-True ($result.Out -notmatch [regex]::Escape($script:Password)) 'off-host hides the fixture password' 'leaked'
    } finally {
        Remove-Root $root
    }
}

function Invoke-HostCommandCase {
    $root = New-Root -InfoName 'fixture-host'
    try {
        $envFile = New-EnvFile $root
        $result = Invoke-Deploy $root @('-Execute', '-ComposeFile', $compose, '-RepoRoot', $repo, '-EnvFile', $envFile)
        Assert-True ($result.Exit -eq 2) 'host Execute stops when compose up fails' ('exit=' + $result.Exit + ' out=' + $result.Out)
        Assert-True ((Get-Reason $result) -eq 'compose-up-failed') 'host Execute reports compose-up-failed' (Get-Reason $result)
        $marker = Get-Marker $root
        Assert-True ($marker.Contains('compose')) 'host Execute calls compose' $marker
        Assert-True ($marker.Contains('-p antiphon-server2-staging')) 'host Execute pins the project' $marker
        Assert-True ($marker.Contains('up -d') -or $marker.Contains('up')) 'host Execute asks for up' $marker
        Assert-True ($marker -notmatch [regex]::Escape($script:Password)) 'compose argv hides the password' $marker
        Assert-True ($result.Out -notmatch [regex]::Escape($script:Password)) 'host Execute hides the password' 'leaked'
    } finally {
        Remove-Root $root
    }
}

function Invoke-RefusalCase {
    param([string]$Name, [string]$Reason, [string]$ComposeText, [string]$Password = '', [string]$Image = '', [string]$Mode = '600', [string]$Project = '', [switch]$InsideRepo, [switch]$WantEnv)
    $root = New-Root
    try {
        $path = $compose
        if (-not [string]::IsNullOrWhiteSpace($ComposeText)) {
            $path = Join-Path $root.Repo 'bad.yml'
            [System.IO.File]::WriteAllText($path, $ComposeText)
        }
        $args = @('-WhatIf', '-ComposeFile', $path, '-RepoRoot', $root.Repo)
        if ($WantEnv -or $InsideRepo -or -not [string]::IsNullOrWhiteSpace($Password) -or -not [string]::IsNullOrWhiteSpace($Image)) {
            $useImage = $Image
            if ([string]::IsNullOrWhiteSpace($useImage)) { $useImage = $script:Image }
            $usePassword = $Password
            if ([string]::IsNullOrWhiteSpace($usePassword)) { $usePassword = $script:Password }
            $envFile = New-EnvFile $root -Image $useImage -Password $usePassword -Mode $Mode -InsideRepo:$InsideRepo
            $args += @('-EnvFile', $envFile)
        }
        if (-not [string]::IsNullOrWhiteSpace($Project)) { $args += @('-Project', $Project) }
        $result = Invoke-Deploy $root $args
        Assert-True ($result.Exit -eq 2) ($Name + ' exits 2') ('exit=' + $result.Exit + ' out=' + $result.Out)
        Assert-True ((Get-Reason $result) -eq $Reason) ($Name + ' reason') (Get-Reason $result)
        Assert-NoDocker $root ($Name + ' does not call docker') $result
    } finally {
        Remove-Root $root
    }
}

function Get-GoodCompose {
    if (-not (Test-Path -LiteralPath $compose)) { return '' }
    return [System.IO.File]::ReadAllText($compose)
}

function Invoke-Selected {
    param([string]$Name)
    switch ($Name) {
        'whatif' { Invoke-WhatIfCase }
        'ambiguous' { Invoke-AmbiguousCase }
        'off-host' { Invoke-OffHostCase }
        'host-command' { Invoke-HostCommandCase }
        'bind-any' { Invoke-RefusalCase 'bind any' 'bind-any' ((Get-GoodCompose) + "`n0.0.0.0`n") }
        'volume' { Invoke-RefusalCase 'forbidden volume' 'volume-forbidden' ((Get-GoodCompose) + "`nantiphon_pgdata`n") }
        'dev-password' { Invoke-RefusalCase 'dev password' 'dev-password' '' -Password 'antiphon_dev' }
        'env-mode' { Invoke-RefusalCase 'env mode' 'env-mode' '' -Mode '644' }
        'wrong-project' { Invoke-RefusalCase 'wrong project' 'staging-compose-wrong-project' '' -Project 'antiphon-runner' }
        'alpine' { Invoke-RefusalCase 'alpine image' 'image-alpine' '' -Image 'postgres:16-alpine' }
        'floating' { Invoke-RefusalCase 'floating image' 'image-floating' '' -Image 'postgres:16' }
        'env-inside' { Invoke-RefusalCase 'env inside repo' 'env-inside-repo' '' -InsideRepo }
        default { throw ('unknown case ' + $Name) }
    }
}

$selected = @('whatif', 'ambiguous', 'off-host', 'host-command', 'bind-any', 'volume', 'dev-password', 'env-mode', 'wrong-project', 'alpine', 'floating', 'env-inside')
if (-not [string]::IsNullOrWhiteSpace($Case)) { $selected = @($Case) }
foreach ($name in $selected) { Invoke-Selected $name }
Write-Host ('SERVER2-STAGING-TEST passed=' + $script:Passed + ' failed=' + $script:Failed)
if ($script:Failed -gt 0) { exit 1 }
exit 0
