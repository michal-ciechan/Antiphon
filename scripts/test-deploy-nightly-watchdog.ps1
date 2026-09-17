#requires -Version 5.1
# CARD-0545 S5 harness: scripts/deploy-nightly-watchdog.ps1 preflight/deploy seams and host-agnostic assets.
# Fake runners only; never contacts a host. The fictitious profile values below are assembled from parts so
# this file itself names no host, address, user or home path (it is inside the host-agnostic scan).
# ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'c487-harness.ps1')
. (Join-Path $here 'deploy-nightly-watchdog.ps1')
$ErrorActionPreference = 'Continue'

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$repoRoot = Split-Path -Parent $here

$script:C545User = 'ops'
$script:C545Target = $script:C545User + [char]64 + 'watchdog-host' + '.example'
$script:C545Root = '/' + 'home' + '/' + $script:C545User + '/antiphon-watchdog'
$script:C545Ip = @('10', '0', '0', '5') -join '.'

function New-C545DeployFx {
    param([hashtable]$Override = @{}, [string]$RemoteEnv = '', [switch]$NoProfile)
    $root = Join-Path $ResultsDirectory ('deploy-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $profile = [ordered]@{
        sshTarget = $script:C545Target
        sshIdentityFile = $null
        remoteRoot = $script:C545Root
        serviceUser = $script:C545User
        rid = 'linux-x64'
        runtime = 'native'
        snapshotBind = ('{0}:17290' -f $script:C545Ip)
        snapshotUrl = ('http://{0}:17290/snapshot.json' -f $script:C545Ip)
        qualSnapshotBind = ('{0}:17291' -f $script:C545Ip)
        stubWindmillBind = '127.0.0.1:17292'
    }
    foreach ($k in $Override.Keys) { $profile[$k] = $Override[$k] }
    $profilePath = Join-Path $root 'watchdog-deploy.local.json'
    if (-not $NoProfile) { Write-NightlyAtomicJson -Path $profilePath -Object $profile }
    $fx = [pscustomobject]@{
        Root = $root
        ProfilePath = $profilePath
        Ssh = New-Object System.Collections.Generic.List[string]
        Scp = New-Object System.Collections.Generic.List[string]
        Publish = New-Object System.Collections.Generic.List[string]
        Http = New-Object System.Collections.Generic.List[string]
        RemoteEnv = $RemoteEnv
    }
    return $fx
}

function Get-C545DeployResult {
    param($Fx, [switch]$Deploy, [switch]$NoProfileArg, [string]$EnvProfile = $null)
    $saved = $env:ANTIPHON_WATCHDOG_DEPLOY_PROFILE
    $env:ANTIPHON_WATCHDOG_DEPLOY_PROFILE = $EnvProfile
    try {
        $script:C545LastHost = ''
        $captured = New-Object System.Collections.Generic.List[string]
        $state = $Fx
        $ssh = {
            param($command)
            $state.Ssh.Add([string]$command)
            if ($command -match 'sed -n') {
                $names = @(($state.RemoteEnv -split "`n") | Where-Object { $_ -match '^[A-Z0-9_]+=' } | ForEach-Object { ($_ -split '=')[0] + '=' })
                return [pscustomobject]@{ ExitCode = 0; Output = $names }
            }
            if ($command -match "^echo '([A-Za-z0-9+/=]+)' \| base64 -d >>") {
                $state.RemoteEnv = $state.RemoteEnv + [System.Text.Encoding]::ASCII.GetString([Convert]::FromBase64String($Matches[1]))
            }
            return [pscustomobject]@{ ExitCode = 0; Output = @('ok') }
        }.GetNewClosure()
        $scp = { param($source, $remotePath) $state.Scp.Add(('{0} -> {1}' -f $source, $remotePath)); [pscustomobject]@{ ExitCode = 0; Output = @() } }.GetNewClosure()
        $publish = { param($project, $rid, $outputDir) $state.Publish.Add([string]$rid); New-Item -ItemType Directory -Path $outputDir -Force | Out-Null; [System.IO.File]::WriteAllText((Join-Path $outputDir 'Antiphon.NightlyWatchdog'), 'fake-binary'); [pscustomobject]@{ ExitCode = 0; Output = @() } }.GetNewClosure()
        $http = { param($url) $state.Http.Add([string]$url); [pscustomobject]@{ StatusCode = 200; Body = '{}' } }.GetNewClosure()
        $fakeRepo = Join-Path $Fx.Root 'repo'
        $assetDir = Join-Path $fakeRepo (Join-Path 'scripts' 'nightly-watchdog')
        New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
        foreach ($asset in @('antiphon-nightly-watchdog.service.template', 'qual.example.json')) {
            Copy-Item -LiteralPath (Join-Path (Join-Path $here 'nightly-watchdog') $asset) -Destination (Join-Path $assetDir $asset) -Force
        }
        $params = @{
            SshRunner = $ssh; ScpRunner = $scp; PublishRunner = $publish; HttpRunner = $http
            DefaultProfilePath = (Join-Path $Fx.Root 'missing-default.json')
            RepoRoot = $fakeRepo
            WorkDir = (Join-Path $Fx.Root 'work')
        }
        if (-not $NoProfileArg) { $params.ProfilePath = $Fx.ProfilePath }
        if ($Deploy) { $params.Deploy = $true }
        $hostLines = @()
        $result = $null
        $all = @(Invoke-NightlyWatchdogDeployment @params 6>&1)
        foreach ($item in $all) {
            if ($item -is [System.Management.Automation.InformationRecord]) { $hostLines += [string]$item.MessageData }
            elseif ($null -ne $item -and $item.PSObject.Properties['ExitCode']) { $result = $item }
            else { $hostLines += [string]$item }
        }
        return [pscustomobject]@{ Result = $result; Output = ($hostLines -join "`n"); Fx = $Fx }
    } finally {
        $env:ANTIPHON_WATCHDOG_DEPLOY_PROFILE = $saved
    }
}

function Get-C545RunnerCallCount {
    param($Fx)
    return ($Fx.Ssh.Count + $Fx.Scp.Count + $Fx.Publish.Count + $Fx.Http.Count)
}

function Test-C545_DeployRefusesWithoutProfile {
    $fx = New-C545DeployFx -NoProfile
    $r = Get-C545DeployResult -Fx $fx -NoProfileArg
    Assert-C487 -Cond ($r.Result.ExitCode -eq 3 -and $r.Output -match 'no deploy profile' -and (Get-C545RunnerCallCount $fx) -eq 0) `
        -Name 'C545 DeployRefusesWithoutProfile no-profile/no-arg exit 3 no runner call' -Detail ('exit={0} calls={1} out={2}' -f $r.Result.ExitCode, (Get-C545RunnerCallCount $fx), $r.Output)

    $fx = New-C545DeployFx -NoProfile
    $r = Get-C545DeployResult -Fx $fx -NoProfileArg -EnvProfile (Join-Path $fx.Root 'env-named-but-missing.json')
    Assert-C487 -Cond ($r.Result.ExitCode -eq 3 -and (Get-C545RunnerCallCount $fx) -eq 0) -Name 'C545 DeployRefusesWithoutProfile no-profile/env-missing-file exit 3' -Detail ('exit={0}' -f $r.Result.ExitCode)

    $placeholderTarget = '<user>' + [char]64 + '<host>'
    $fx = New-C545DeployFx -Override @{ sshTarget = $placeholderTarget }
    $r = Get-C545DeployResult -Fx $fx
    Assert-C487 -Cond ($r.Result.ExitCode -eq 3 -and $r.Output -match 'sshTarget' -and (Get-C545RunnerCallCount $fx) -eq 0) -Name 'C545 DeployRefusesWithoutProfile placeholder/sshTarget exit 3' -Detail ('exit={0} out={1}' -f $r.Result.ExitCode, $r.Output)

    $fx = New-C545DeployFx -Override @{ remoteRoot = ('/' + 'home/<user>/antiphon-watchdog') }
    $r = Get-C545DeployResult -Fx $fx
    Assert-C487 -Cond ($r.Result.ExitCode -eq 3 -and $r.Output -match 'remoteRoot' -and (Get-C545RunnerCallCount $fx) -eq 0) -Name 'C545 DeployRefusesWithoutProfile placeholder/remoteRoot exit 3' -Detail $r.Output

    $fx = New-C545DeployFx -Override @{ snapshotBind = '127.0.0.1:17290' }
    $r = Get-C545DeployResult -Fx $fx
    Assert-C487 -Cond ($r.Result.ExitCode -eq 3 -and $r.Output -match 'snapshotBind must be a private non-loopback address') -Name 'C545 DeployRefusesWithoutProfile loopback-bind exit 3' -Detail $r.Output

    $fx = New-C545DeployFx -Override @{ snapshotBind = '0.0.0.0:17290' }
    $r = Get-C545DeployResult -Fx $fx
    Assert-C487 -Cond ($r.Result.ExitCode -eq 3 -and $r.Output -match 'wildcard') -Name 'C545 DeployRefusesWithoutProfile wildcard-bind native exit 3' -Detail $r.Output

    $fx = New-C545DeployFx
    $r = Get-C545DeployResult -Fx $fx
    Assert-C487 -Cond ($r.Result.ExitCode -eq 0) -Name 'C545 DeployRefusesWithoutProfile control valid profile preflight exit 0' -Detail $r.Output
}

function Test-C545_DeployRendersFromProfile {
    $fx = New-C545DeployFx
    $r = Get-C545DeployResult -Fx $fx
    $unit = [string]$r.Result.RenderedUnit
    $expectedLines = @(
        ('User={0}' -f $script:C545User),
        ('WorkingDirectory={0}' -f $script:C545Root),
        ('EnvironmentFile={0}/env' -f $script:C545Root),
        ('ExecStart={0}/Antiphon.NightlyWatchdog run' -f $script:C545Root),
        ('ReadWritePaths={0}/state {0}/state-qual' -f $script:C545Root)
    )
    $unitLines = @($unit -split "`r?`n")
    $missing = @($expectedLines | Where-Object { $unitLines -notcontains $_ })
    Assert-C487 -Cond ($unit.Length -gt 0 -and $unit -notmatch '@@' -and $missing.Count -eq 0) -Name 'C545 DeployRendersFromProfile unit placeholders fully substituted' -Detail ('missing: ' + ($missing -join ' | '))
    $qual = [string]$r.Result.RenderedQual
    $qualJson = $null
    try { $qualJson = $qual | ConvertFrom-Json } catch { }
    Assert-C487 -Cond ($qual -notmatch '[<>]' -and $null -ne $qualJson -and [string]$qualJson.snapshotBind -eq ('{0}:17291' -f $script:C545Ip) -and [string]$qualJson.namespace -eq 'mc/qual') -Name 'C545 DeployRendersFromProfile qual.json rendered without placeholders' -Detail $qual

    $scpOrSystemctl = @($fx.Ssh | Where-Object { $_ -match 'systemctl|sudo' })
    $selfCheck = @($fx.Ssh | Where-Object { $_ -match '--self-check' })
    Assert-C487 -Cond ($fx.Scp.Count -eq 0 -and $scpOrSystemctl.Count -eq 0 -and $fx.Ssh.Count -eq 1 -and $selfCheck.Count -eq 1 -and $fx.Http.Count -eq 0) `
        -Name 'C545 DeployRendersFromProfile preflight-no-upload' -Detail ('ssh=' + ($fx.Ssh -join ' || ') + ' scp=' + $fx.Scp.Count)
    Assert-C487 -Cond ($fx.Publish.Count -eq 1 -and $fx.Publish[0] -eq 'linux-x64') -Name 'C545 DeployRendersFromProfile preflight publishes for the profile rid' -Detail ($fx.Publish -join ',')

    $secret = 'never-print-' + 'this'
    $existing = ('ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN={0}' -f $secret) + "`n" + ('ANTIPHON_WATCHDOG_SNAPSHOT_BIND={0}:17290' -f $script:C545Ip) + "`n"
    $fx2 = New-C545DeployFx -RemoteEnv $existing
    $d = Get-C545DeployResult -Fx $fx2 -Deploy
    $fragment = @($d.Result.EnvFragment)
    $expectedFragment = @('ANTIPHON_WATCHDOG_STATE_DIR=./state', 'ANTIPHON_WATCHDOG_NAMESPACE=mc')
    Assert-C487 -Cond ($d.Result.ExitCode -eq 0 -and (($fragment -join '|') -eq ($expectedFragment -join '|'))) -Name 'C545 DeployRendersFromProfile env fragment has exactly the absent non-secret keys' -Detail ('exit={0} fragment={1} out={2}' -f $d.Result.ExitCode, ($fragment -join '|'), $d.Output)
    Assert-C487 -Cond ($fx2.RemoteEnv.StartsWith($existing) -and $fx2.RemoteEnv.Length -gt $existing.Length) -Name 'C545 DeployRendersFromProfile existing env lines survive byte-for-byte'
    $allText = $d.Output + ($fx2.Ssh -join "`n") + ($fx2.Scp -join "`n")
    Assert-C487 -Cond ($allText -notmatch [regex]::Escape($secret)) -Name 'C545 DeployRendersFromProfile secret never printed or sent'
    Assert-C487 -Cond ($fx2.Http.Count -eq 1 -and $fx2.Http[0] -eq ('http://{0}:17290/snapshot.json' -f $script:C545Ip)) -Name 'C545 DeployRendersFromProfile deploy-verifies-snapshot' -Detail ($fx2.Http -join ',')
    $enable = @($fx2.Ssh | Where-Object { $_ -match 'systemctl enable --now antiphon-nightly-watchdog.service' })
    Assert-C487 -Cond ($enable.Count -eq 1 -and $fx2.Scp.Count -eq 3) -Name 'C545 DeployRendersFromProfile deploy uploads and enables the unit' -Detail ('scp=' + ($fx2.Scp -join ' || '))
}

function Get-C545TrackedWatchdogAssets {
    $files = @()
    $files += @(Get-ChildItem -LiteralPath (Join-Path $here 'nightly-watchdog') -File -Recurse | Where-Object { $_.Name -notlike '*.local.json' } | ForEach-Object { $_.FullName })
    $files += (Join-Path $here 'deploy-nightly-watchdog.ps1')
    $files += (Join-Path $here 'test-deploy-nightly-watchdog.ps1')
    $files += (Join-Path $repoRoot (Join-Path 'docs' 'nightly-watchdog.md'))
    $files += @(Get-ChildItem -LiteralPath (Join-Path $here 'windmill') -Filter 'antiphon-nightly-readiness*.json' -File | ForEach-Object { $_.FullName })
    return $files
}

function Test-C545_HostAgnosticAssets {
    $ipv4 = '\b(?:\d{1,3}\.){3}\d{1,3}\b'
    $target = '[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+'
    $homePath = '/' + 'home/(?!<user>)[^\s"'']*'
    $allowedTargets = @(('<user>' + [char]64 + '<host>'), ('noreply' + [char]64 + 'anthropic.com'))
    $ipHits = @(); $targetHits = @(); $homeHits = @()
    foreach ($file in (Get-C545TrackedWatchdogAssets)) {
        if (-not (Test-Path -LiteralPath $file)) { $ipHits += ('missing file ' + $file); continue }
        $n = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file)) {
            $n++
            $where = ('{0}:{1}' -f (Split-Path -Leaf $file), $n)
            foreach ($m in [regex]::Matches($line, $ipv4)) {
                if ($m.Value -ne '127.0.0.1' -and $m.Value -ne '0.0.0.0') { $ipHits += ('{0} {1}' -f $where, $m.Value) }
            }
            foreach ($m in [regex]::Matches($line, $target)) {
                $value = $m.Value
                if ($value.EndsWith(([char]64 + 'host.docker.internal'))) { continue }
                if ($line.Contains(('<user>' + [char]64 + '<host>'))) { continue }
                if ($allowedTargets -contains $value) { continue }
                $targetHits += ('{0} {1}' -f $where, $value)
            }
            foreach ($m in [regex]::Matches($line, $homePath)) {
                $homeHits += ('{0} {1}' -f $where, $m.Value)
            }
        }
    }
    Assert-C487 -Cond ($ipHits.Count -eq 0) -Name 'C545 HostAgnosticAssets no IPv4 literal' -Detail ($ipHits -join ' | ')
    Assert-C487 -Cond ($targetHits.Count -eq 0) -Name 'C545 HostAgnosticAssets no ssh-target token' -Detail ($targetHits -join ' | ')
    Assert-C487 -Cond ($homeHits.Count -eq 0) -Name 'C545 HostAgnosticAssets no absolute home path' -Detail ($homeHits -join ' | ')
}

function Test-C545_DeployManifest {
    $assets = @(
        (Join-Path $here (Join-Path 'nightly-watchdog' 'antiphon-nightly-watchdog.service.template')),
        (Join-Path $here (Join-Path 'nightly-watchdog' 'deploy.example.json')),
        (Join-Path $here (Join-Path 'nightly-watchdog' 'env.example')),
        (Join-Path $here (Join-Path 'nightly-watchdog' 'qual.example.json')),
        (Join-Path $here (Join-Path 'nightly-watchdog' 'README.md'))
    )
    foreach ($asset in $assets) {
        $exists = Test-Path -LiteralPath $asset -PathType Leaf
        $ascii = $false
        if ($exists) { $ascii = (@([System.IO.File]::ReadAllBytes($asset) | Where-Object { $_ -gt 127 }).Count -eq 0) }
        Assert-C487 -Cond ($exists -and $ascii) -Name ('C545 DeployManifest {0} exists and is ASCII' -f (Split-Path -Leaf $asset))
    }
    $gitignore = [System.IO.File]::ReadAllText((Join-Path $repoRoot '.gitignore'))
    Assert-C487 -Cond ($gitignore -match [regex]::Escape('scripts/nightly-watchdog/*.local.json')) -Name 'C545 DeployManifest local profiles are gitignored'
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C545_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'deploy-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 25 })
