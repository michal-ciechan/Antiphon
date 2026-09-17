#requires -Version 5.1
<#
.SYNOPSIS
    Preflight or deploy the CARD-0545 independent nightly watchdog to the host named by a deploy profile.

.DESCRIPTION
    The target comes ONLY from an untracked deploy profile: -Profile <path>, else the
    ANTIPHON_WATCHDOG_DEPLOY_PROFILE environment variable, else the untracked nightly state root
    default, else exactly one repo-local scripts/nightly-watchdog/*.local.json (gitignored). With no
    profile, or with a profile still holding <placeholder> values, the script exits 3 and contacts
    nothing. There is no built-in target.

    Default is a read-only preflight: publish the self-contained binary for the profile's rid,
    checksum it, render the systemd unit template and the qualification config, print the profile's
    non-secret values, and run the remote --self-check dry run over ssh.

    -Deploy is the explicit opt-in: create the state directories, upload the binary, unit and
    rendered config, append only the absent non-secret env keys the script owns
    (ANTIPHON_WATCHDOG_SNAPSHOT_BIND, _STATE_DIR, _NAMESPACE) to <remoteRoot>/env, install and
    enable the unit, and verify GET <snapshotUrl>. Secret env keys are never read back, written or
    printed. See docs/nightly-watchdog.md.

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy-nightly-watchdog.ps1 -Profile C:\Antiphon\nightly\watchdog-deploy.json

.NOTES
    ASCII-only for Windows PowerShell 5.1. Dot-sourcing defines the functions without running.
#>
[CmdletBinding()]
param(
    [Alias('Profile')] [string]$ProfilePath = '',
    [switch]$Deploy
)
$ErrorActionPreference = 'Stop'

$script:NightlyWatchdogOwnedEnvKeys = @('ANTIPHON_WATCHDOG_SNAPSHOT_BIND', 'ANTIPHON_WATCHDOG_STATE_DIR', 'ANTIPHON_WATCHDOG_NAMESPACE')
$script:NightlyWatchdogDefaultProfilePath = 'C:\Antiphon\nightly\watchdog-deploy.json'
$script:NightlyWatchdogUnitName = 'antiphon-nightly-watchdog.service'

function Resolve-NightlyWatchdogProfilePath {
    param([string]$ProfilePath, [string]$DefaultProfilePath, [string]$RepoRoot)
    if (-not [string]::IsNullOrWhiteSpace($ProfilePath)) { return $ProfilePath }
    $fromEnv = [string]$env:ANTIPHON_WATCHDOG_DEPLOY_PROFILE
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) { return $fromEnv }
    if (-not [string]::IsNullOrWhiteSpace($DefaultProfilePath) -and (Test-Path -LiteralPath $DefaultProfilePath -PathType Leaf)) { return $DefaultProfilePath }
    if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) {
        $local = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot (Join-Path 'scripts' 'nightly-watchdog')) -Filter '*.local.json' -File -ErrorAction SilentlyContinue)
        if ($local.Count -eq 1) { return $local[0].FullName }
    }
    return ''
}

function Test-NightlyWatchdogProfile {
    # Returns @{ Ok; Reason; Profile }. Every refusal names the field; values are never echoed.
    param($Profile)
    $fail = { param($reason) return [pscustomobject]@{ Ok = $false; Reason = $reason; Profile = $null } }
    if ($null -eq $Profile) { return (& $fail 'deploy profile is not a JSON object') }
    $values = [ordered]@{}
    foreach ($key in @('sshTarget', 'sshIdentityFile', 'remoteRoot', 'serviceUser', 'rid', 'runtime', 'snapshotBind', 'snapshotUrl', 'qualSnapshotBind', 'stubWindmillBind')) {
        $prop = $Profile.PSObject.Properties[$key]
        $values[$key] = $(if ($prop -and $null -ne $prop.Value) { [string]$prop.Value } else { '' })
    }
    foreach ($key in @($values.Keys)) {
        if ($values[$key] -match '[<>]') { return (& $fail ('placeholder value in {0}' -f $key)) }
    }
    foreach ($key in @('sshTarget', 'remoteRoot', 'serviceUser', 'snapshotBind', 'snapshotUrl')) {
        if ([string]::IsNullOrWhiteSpace($values[$key])) { return (& $fail ('missing {0}' -f $key)) }
    }
    if ([string]::IsNullOrWhiteSpace($values.rid)) { $values.rid = 'linux-x64' }
    if ([string]::IsNullOrWhiteSpace($values.runtime)) { $values.runtime = 'native' }
    if ([string]::IsNullOrWhiteSpace($values.stubWindmillBind)) { $values.stubWindmillBind = '127.0.0.1:17292' }
    if ($values.runtime -ne 'native' -and $values.runtime -ne 'container') { return (& $fail 'runtime must be native or container') }
    if ($values.sshTarget -notmatch '^[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+$') { return (& $fail 'sshTarget must be <user>@<host>') }
    if ($values.serviceUser -notmatch '^[a-z_][a-z0-9_-]*$') { return (& $fail 'serviceUser is not a valid user name') }
    if (-not $values.remoteRoot.StartsWith('/') -or $values.remoteRoot -match "['`"\s]") { return (& $fail 'remoteRoot must be an absolute path without quotes or spaces') }
    foreach ($bindKey in @('snapshotBind', 'qualSnapshotBind')) {
        $bind = $values[$bindKey]
        if ([string]::IsNullOrWhiteSpace($bind)) { continue }
        $bindHost = $bind
        if ($bind -match '^\[(.+)\]:\d+$') { $bindHost = $Matches[1] } elseif ($bind -match '^(.+):\d+$') { $bindHost = $Matches[1] } else { return (& $fail ('{0} must be host:port' -f $bindKey)) }
        if ($bindHost -eq 'localhost' -or $bindHost -eq '::1' -or $bindHost -match '^127\.') { return (& $fail ('{0} must be a private non-loopback address' -f $bindKey)) }
        if (($bindHost -eq '0.0.0.0' -or $bindHost -eq '::' -or $bindHost -eq '*') -and $values.runtime -ne 'container') { return (& $fail ('{0} must not be a wildcard for the native runtime' -f $bindKey)) }
    }
    if ([string]::IsNullOrWhiteSpace($values.qualSnapshotBind)) {
        $values.qualSnapshotBind = ($values.snapshotBind -replace ':\d+$', ':17291')
    }
    return [pscustomobject]@{ Ok = $true; Reason = ''; Profile = [pscustomobject]$values }
}

function ConvertTo-NightlyWatchdogUnit {
    param([string]$TemplateText, $Profile)
    return $TemplateText.Replace('@@SERVICE_USER@@', [string]$Profile.serviceUser).Replace('@@REMOTE_ROOT@@', ([string]$Profile.remoteRoot).TrimEnd('/'))
}

function ConvertTo-NightlyWatchdogQualConfig {
    param([string]$TemplateText, $Profile)
    return $TemplateText.Replace('<qualSnapshotBind>', [string]$Profile.qualSnapshotBind).Replace('<stubWindmillBind>', [string]$Profile.stubWindmillBind).Replace('<runtime>', [string]$Profile.runtime)
}

function Get-NightlyWatchdogEnvFragment {
    # Only the owned non-secret keys, and only those absent from the existing env text.
    param([string]$ExistingEnvText, $Profile)
    $present = @{}
    foreach ($line in @(([string]$ExistingEnvText) -split "`n")) {
        if ($line -match '^\s*([A-Z0-9_]+)\s*=') { $present[$Matches[1]] = $true }
    }
    $wanted = [ordered]@{
        ANTIPHON_WATCHDOG_SNAPSHOT_BIND = [string]$Profile.snapshotBind
        ANTIPHON_WATCHDOG_STATE_DIR = './state'
        ANTIPHON_WATCHDOG_NAMESPACE = 'mc'
    }
    $lines = @()
    foreach ($key in $wanted.Keys) {
        if ($script:NightlyWatchdogOwnedEnvKeys -notcontains $key) { continue }
        if (-not $present.ContainsKey($key)) { $lines += ('{0}={1}' -f $key, $wanted[$key]) }
    }
    return $lines
}

function Invoke-NightlyWatchdogRunner {
    param([scriptblock]$Runner, [object[]]$Arguments, [string]$Operation)
    $result = & $Runner @Arguments
    if ($null -eq $result -or [int]$result.ExitCode -ne 0) { throw ('{0} failed' -f $Operation) }
    return $result
}

function Invoke-NightlyWatchdogDeployment {
    [CmdletBinding()]
    param(
        [Alias('Profile')] [string]$ProfilePath = '',
        [switch]$Deploy,
        [scriptblock]$SshRunner,
        [scriptblock]$ScpRunner,
        [scriptblock]$PublishRunner,
        [scriptblock]$HttpRunner,
        [string]$DefaultProfilePath = $script:NightlyWatchdogDefaultProfilePath,
        [string]$RepoRoot = '',
        [string]$WorkDir = '',
        $Now = $null
    )
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
    $out = [ordered]@{ ExitCode = 1; Message = ''; RenderedUnit = ''; RenderedQual = ''; EnvFragment = @(); Checksum = '' }

    $resolved = Resolve-NightlyWatchdogProfilePath -ProfilePath $ProfilePath -DefaultProfilePath $DefaultProfilePath -RepoRoot $RepoRoot
    if ([string]::IsNullOrWhiteSpace($resolved) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        $out.ExitCode = 3
        $out.Message = 'REFUSED: no deploy profile (pass -Profile, set ANTIPHON_WATCHDOG_DEPLOY_PROFILE, or place the untracked default). Nothing was contacted.'
        Write-Host $out.Message
        return [pscustomobject]$out
    }
    $raw = $null
    try { $raw = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $raw = $null }
    $check = Test-NightlyWatchdogProfile -Profile $raw
    if (-not $check.Ok) {
        $out.ExitCode = 3
        $out.Message = ('REFUSED: invalid deploy profile: {0}' -f $check.Reason)
        Write-Host $out.Message
        return [pscustomobject]$out
    }
    $p = $check.Profile
    $root = ([string]$p.remoteRoot).TrimEnd('/')

    if ($null -eq $SshRunner) {
        $sshArgs = @()
        if (-not [string]::IsNullOrWhiteSpace([string]$p.sshIdentityFile)) { $sshArgs += @('-i', [string]$p.sshIdentityFile) }
        $target = [string]$p.sshTarget
        $SshRunner = { param($command) $o = @(& ssh @sshArgs -o BatchMode=yes $target $command 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $o } }.GetNewClosure()
    }
    if ($null -eq $ScpRunner) {
        $scpArgs = @()
        if (-not [string]::IsNullOrWhiteSpace([string]$p.sshIdentityFile)) { $scpArgs += @('-i', [string]$p.sshIdentityFile) }
        $target = [string]$p.sshTarget
        $ScpRunner = { param($source, $remotePath) $o = @(& scp @scpArgs -o BatchMode=yes $source ('{0}:{1}' -f $target, $remotePath) 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $o } }.GetNewClosure()
    }
    if ($null -eq $PublishRunner) {
        $PublishRunner = { param($projectPath, $rid, $outputDir) $o = @(& dotnet publish $projectPath -c Release -r $rid --self-contained -p:PublishSingleFile=true -o $outputDir 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $o } }
    }
    if ($null -eq $HttpRunner) {
        $HttpRunner = { param($url) $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15; [pscustomobject]@{ StatusCode = [int]$r.StatusCode; Body = [string]$r.Content } }
    }
    if ([string]::IsNullOrWhiteSpace($WorkDir)) { $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-watchdog-deploy-' + [guid]::NewGuid().ToString('N')) }
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

    try {
        Write-Host ('Deploy profile: {0}' -f $resolved)
        foreach ($key in @('rid', 'runtime', 'remoteRoot', 'serviceUser', 'snapshotBind', 'snapshotUrl', 'qualSnapshotBind', 'stubWindmillBind')) {
            Write-Host ('  {0}: {1}' -f $key, [string]$p.$key)
        }

        $publishDir = Join-Path $WorkDir 'publish'
        $project = Join-Path $RepoRoot (Join-Path 'src' (Join-Path 'Antiphon.NightlyWatchdog' 'Antiphon.NightlyWatchdog.csproj'))
        [void](Invoke-NightlyWatchdogRunner $PublishRunner @($project, [string]$p.rid, $publishDir) 'publish')
        $binary = Join-Path $publishDir 'Antiphon.NightlyWatchdog'
        if (Test-Path -LiteralPath $binary -PathType Leaf) {
            $out.Checksum = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
            Write-Host ('Binary sha256: {0}' -f $out.Checksum)
        }

        $assets = Join-Path $RepoRoot (Join-Path 'scripts' 'nightly-watchdog')
        $unitPath = Join-Path $WorkDir $script:NightlyWatchdogUnitName
        $qualPath = Join-Path $WorkDir 'qual.json'
        $out.RenderedUnit = ConvertTo-NightlyWatchdogUnit -TemplateText ([System.IO.File]::ReadAllText((Join-Path $assets 'antiphon-nightly-watchdog.service.template'))) -Profile $p
        $out.RenderedQual = ConvertTo-NightlyWatchdogQualConfig -TemplateText ([System.IO.File]::ReadAllText((Join-Path $assets 'qual.example.json'))) -Profile $p
        if ($out.RenderedUnit -match '@@' -or $out.RenderedQual -match '[<>]') { throw 'template rendering left a placeholder' }
        [System.IO.File]::WriteAllText($unitPath, $out.RenderedUnit.Replace("`r`n", "`n"))
        [System.IO.File]::WriteAllText($qualPath, $out.RenderedQual.Replace("`r`n", "`n"))

        if (-not $Deploy) {
            $dry = & $SshRunner ("if [ -x '{0}/Antiphon.NightlyWatchdog' ]; then cd '{0}' && ./Antiphon.NightlyWatchdog --self-check; else echo 'self-check skipped: no binary deployed yet'; fi" -f $root)
            Write-Host ('Remote self-check exit: {0}' -f $(if ($dry) { $dry.ExitCode } else { 'none' }))
            $out.ExitCode = 0
            $out.Message = 'PREFLIGHT OK (read-only; pass -Deploy to install)'
            Write-Host $out.Message
            return [pscustomobject]$out
        }

        [void](Invoke-NightlyWatchdogRunner $SshRunner @(("mkdir -p '{0}/state' '{0}/state-qual' && chmod 700 '{0}/state' '{0}/state-qual'" -f $root)) 'create state directories')
        if (Test-Path -LiteralPath $binary -PathType Leaf) {
            [void](Invoke-NightlyWatchdogRunner $ScpRunner @($binary, ('{0}/Antiphon.NightlyWatchdog' -f $root)) 'upload binary')
        }
        [void](Invoke-NightlyWatchdogRunner $ScpRunner @($unitPath, ('{0}/{1}' -f $root, $script:NightlyWatchdogUnitName)) 'upload unit')
        [void](Invoke-NightlyWatchdogRunner $ScpRunner @($qualPath, ('{0}/qual.json' -f $root)) 'upload qualification config')

        # Key names only: the existing env file is never printed.
        $keys = Invoke-NightlyWatchdogRunner $SshRunner @(("touch '{0}/env' && chmod 600 '{0}/env' && sed -n 's/^\([A-Z0-9_]*\)=.*/\1=/p' '{0}/env'" -f $root)) 'read env key names'
        $fragment = @(Get-NightlyWatchdogEnvFragment -ExistingEnvText ((@($keys.Output) -join "`n")) -Profile $p)
        $out.EnvFragment = $fragment
        if ($fragment.Count -gt 0) {
            $payload = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes((($fragment -join "`n") + "`n")))
            [void](Invoke-NightlyWatchdogRunner $SshRunner @(("echo '{0}' | base64 -d >> '{1}/env' && chmod 600 '{1}/env'" -f $payload, $root)) 'append owned env keys')
            Write-Host ('Env keys added: {0}' -f ((@($fragment | ForEach-Object { ($_ -split '=')[0] })) -join ', '))
        }

        [void](Invoke-NightlyWatchdogRunner $SshRunner @(("sudo chown -R '{0}' '{1}/state' '{1}/state-qual' '{1}/env' && chmod 755 '{1}/Antiphon.NightlyWatchdog' 2>/dev/null; sudo install -m 0644 '{1}/{2}' /etc/systemd/system/{2} && sudo systemctl daemon-reload && sudo systemctl enable --now {2}" -f [string]$p.serviceUser, $root, $script:NightlyWatchdogUnitName)) 'install and enable unit')

        $snapshot = & $HttpRunner ([string]$p.snapshotUrl)
        if ($null -eq $snapshot -or [int]$snapshot.StatusCode -ne 200) { throw 'snapshot verification failed' }
        $out.ExitCode = 0
        $out.Message = 'DEPLOY OK: unit enabled and snapshot answered'
        Write-Host $out.Message
        return [pscustomobject]$out
    } catch {
        $out.ExitCode = 1
        $out.Message = ('FAILED: {0}' -f $_.Exception.Message)
        Write-Host $out.Message
        return [pscustomobject]$out
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$result = Invoke-NightlyWatchdogDeployment -ProfilePath $ProfilePath -Deploy:$Deploy
exit ([int]$result.ExitCode)
