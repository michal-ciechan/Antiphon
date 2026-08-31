#requires -Version 5.1
<#
.SYNOPSIS
    Safely deploy the source-built am-service gateway on server2.

.DESCRIPTION
    This fixed-target command deploys only mc@server2:/home/mc/antiphon-messaging,
    messaging-service, and am-service. Its archive manifest is derived solely from
    src/Antiphon.Messaging.Service/Dockerfile COPY sources; unsupported COPY syntax
    refuses before archive/upload. Default and -WhatIf are read-only preflights.

    -Deploy is the explicit production opt-in. Use -Confirm for an interactive
    production confirmation; a non-interactive Deploy-role brief must explicitly
    authorize deployment before -Confirm:$false. Success verifies the container,
    /api/channels, migrations, and a bounded redacted log scan. A retained source
    backup is reported; automatic rollback is deliberately not attempted. Never log
    Compose environments, tokens, or arbitrary container logs. The human traffic
    check remains the Antiphon-Family test-group round trip, never live Family.

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy-am-service.ps1

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy-am-service.ps1 -Deploy -Confirm

.OUTPUTS
    REMOTE DEPLOY VERDICT: ok
    REMOTE DEPLOY VERDICT: failed <phase and safe diagnostic>

.NOTES
    Keep this file ASCII-only for Windows PowerShell 5.1 compatibility.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param(
    [switch]$Deploy,
    [switch]$SkipRealTrafficCheck,
    [ValidateRange(10, 3600)] [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'

function ConvertTo-DockerfileLogicalLines {
    param([Parameter(Mandatory)][string]$DockerfilePath)
    if (-not (Test-Path -LiteralPath $DockerfilePath -PathType Leaf)) { throw "Dockerfile not found: $DockerfilePath" }
    $result = @(); $pending = ''; $start = 0; $number = 0
    foreach ($line in (Get-Content -LiteralPath $DockerfilePath)) {
        $number++; $trimmed = $line.Trim()
        if (-not $pending -and ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#'))) { continue }
        if (-not $pending) { $start = $number }
        if ($trimmed -match '(?<!\\)\\\s*$') { $pending += (($trimmed -replace '(?<!\\)\\\s*$', '').TrimEnd() + ' '); continue }
        $text = ($pending + $trimmed).Trim(); $pending = ''
        if ($text) { $result += [pscustomobject]@{ Line = $start; Text = $text } }
    }
    if ($pending) { throw "Dockerfile line $start has an unterminated continuation." }
    return $result
}

function Get-AmServiceDockerfileManifest {
    param([Parameter(Mandatory)][string]$DockerfilePath, [Parameter(Mandatory)][string]$ContextRoot)
    $context = [System.IO.Path]::GetFullPath($ContextRoot)
    if (-not (Test-Path -LiteralPath $context -PathType Container)) { throw "Docker context root not found: $context" }
    $prefix = $context.TrimEnd([char]'\', [char]'/') + [System.IO.Path]::DirectorySeparatorChar
    $entries = New-Object 'System.Collections.Generic.List[object]'; $seen = @{}
    foreach ($logical in (ConvertTo-DockerfileLogicalLines $DockerfilePath)) {
        if ($logical.Text -notmatch '^(?i:COPY)\s+(.*)$') { continue }
        $body = $Matches[1].Trim()
        if ($body.StartsWith('[')) { throw "Dockerfile COPY line $($logical.Line) uses unsupported JSON-array syntax." }
        $tokens = @($body -split '\s+' | Where-Object { $_ })
        if ($tokens.Count -eq 0) { throw "Dockerfile COPY line $($logical.Line) has no arguments." }
        if ($tokens[0] -match '^(?i:--from)=.+$') { continue }
        if ($tokens[0].StartsWith('--')) { throw "Dockerfile COPY line $($logical.Line) has unrecognised option '$($tokens[0])'." }
        if ($tokens.Count -lt 2) { throw "Dockerfile COPY line $($logical.Line) needs a source and destination." }
        foreach ($source in @($tokens[0..($tokens.Count - 2)])) {
            if ($source -match '\$\{' -or $source -match '\$[A-Za-z_]' -or $source -match '[*?\[\]]') { throw "Dockerfile COPY line $($logical.Line) has unsafe variable or glob '$source'." }
            if ($source -match '^(?:[A-Za-z]:)?[\\/]' -or $source -match '^[A-Za-z]:') { throw "Dockerfile COPY line $($logical.Line) has absolute source '$source'." }
            $normalized = $source.Replace('\', '/').TrimEnd('/')
            if (-not $normalized) { throw "Dockerfile COPY line $($logical.Line) has an empty source." }
            if (($normalized -split '/') -contains '..') { throw "Dockerfile COPY line $($logical.Line) has parent traversal '$source'." }
            $candidate = [System.IO.Path]::GetFullPath((Join-Path $context $normalized))
            if (-not ($candidate -eq $context -or $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase))) { throw "Dockerfile COPY line $($logical.Line) resolves outside context: '$source'." }
            if (-not (Test-Path -LiteralPath $candidate)) { throw "Dockerfile COPY line $($logical.Line) source does not exist: '$source'." }
            if (-not $seen.ContainsKey($normalized)) { $seen[$normalized] = $true; $entries.Add([pscustomobject]@{ Path = $normalized; Line = $logical.Line }) }
        }
    }
    if ($entries.Count -eq 0) { throw 'No local Dockerfile COPY sources were found.' }
    return @($entries | ForEach-Object { $_ })
}

function Test-AmServiceArchive {
    param([Parameter(Mandatory)][string]$ArchivePath, [Parameter(Mandatory)][object[]]$Manifest)
    $lines = @(& tar -tzf $ArchivePath 2>&1 | ForEach-Object { $_.ToString().TrimStart('./') }); $code = $LASTEXITCODE
    if ($code -ne 0) { throw "Could not list deployment archive (tar exit $code)." }
    foreach ($line in $lines) { if (($line.TrimEnd('/') -split '/') | Where-Object { $_ -eq 'bin' -or $_ -eq 'obj' -or $_ -like 'bin-*' }) { throw "Deployment archive contains forbidden build output: $line" } }
    foreach ($source in $Manifest) { $path = $source.Path.TrimEnd('/'); if (-not ($lines | Where-Object { $_.TrimEnd('/') -eq $path -or $_.StartsWith($path + '/') })) { throw "Deployment archive is missing COPY source '$path' from line $($source.Line)." } }
    return $lines
}

function New-AmServiceArchive {
    param([Parameter(Mandatory)][string]$ContextRoot, [Parameter(Mandatory)][object[]]$Manifest)
    $path = Join-Path ([IO.Path]::GetTempPath()) ('am-service-src-' + [guid]::NewGuid().ToString('N') + '.tgz')
    $args = @('-czf', $path, '--exclude=bin', '--exclude=*/bin', '--exclude=obj', '--exclude=*/obj', '--exclude=bin-*', '--exclude=*/bin-*') + @($Manifest | ForEach-Object { $_.Path })
    Push-Location $ContextRoot
    try { & tar @args 2>&1 | Out-Null; $code = $LASTEXITCODE } finally { Pop-Location }
    if ($code -ne 0) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue; throw "Could not create deployment archive (tar exit $code)." }
    try { $entries = Test-AmServiceArchive $path $Manifest } catch { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue; throw }
    return [pscustomobject]@{ Path = $path; Entries = $entries }
}

function Invoke-AmServiceRunner {
    param([Parameter(Mandatory)][scriptblock]$Runner, [Parameter(Mandatory)][object[]]$Arguments, [Parameter(Mandatory)][string]$Operation)
    $result = & $Runner @Arguments
    if ($null -eq $result) { $result = [pscustomobject]@{ ExitCode = 0; Output = @() } }
    if ($null -eq $result.PSObject.Properties['ExitCode']) { $result = [pscustomobject]@{ ExitCode = 0; Output = @($result) } }
    if ([int]$result.ExitCode -ne 0) { throw "$Operation failed (exit $($result.ExitCode))." }
    return @($result.Output | ForEach-Object { $_.ToString() })
}

function Get-AmServiceMigrationIds {
    param([Parameter(Mandatory)][string]$MigrationDirectory)
    if (-not (Test-Path -LiteralPath $MigrationDirectory -PathType Container)) { throw "Migration directory not found: $MigrationDirectory" }
    return @(Get-ChildItem -LiteralPath $MigrationDirectory -File -Filter '*.cs' | Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -notlike '*ModelSnapshot.cs' } | ForEach-Object { $_.BaseName } | Sort-Object -Unique)
}

function Invoke-AmServiceDeployment {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][bool]$PerformDeploy,
        [Parameter(Mandatory)][bool]$SkipTrafficCheck, [Parameter(Mandatory)][int]$PollTimeoutSec,
        [scriptblock]$SshRunner, [scriptblock]$ScpRunner
    )
    $hostName = 'mc@server2'; $remoteRoot = '/home/mc/antiphon-messaging'; $remoteContext = "$remoteRoot/build/src"; $dockerfileRelative = 'Antiphon.Messaging.Service/Dockerfile'
    $context = Join-Path $RepositoryRoot 'src'; $dockerfile = Join-Path $context $dockerfileRelative; $migrationDirectory = Join-Path $context 'Antiphon.Messaging.Service\Migrations'
    if ($null -eq $SshRunner) { $SshRunner = { param($command) $output = @(& ssh $hostName $command 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output } } }
    if ($null -eq $ScpRunner) { $ScpRunner = { param($source, $destination) $output = @(& scp $source $destination 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output } } }
    foreach ($command in @('ssh', 'scp', 'tar')) { if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required local command not found: $command" } }
    $manifest = @(Get-AmServiceDockerfileManifest $dockerfile $context); $hash = (Get-FileHash -LiteralPath $dockerfile -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestText = (@($manifest | ForEach-Object { $_.Path }) -join ', ')
    Write-Host "Dockerfile SHA-256: $hash"; Write-Host "Dockerfile-derived source entries ($($manifest.Count)): $manifestText"
    $projectionCommand = 'cd ' + $remoteRoot + ' && docker compose config --no-interpolate --format json messaging-service | python3 -c ''import json, os, sys; d=json.load(sys.stdin); b=d["services"]["messaging-service"]["build"]; c=b["context"]; print(json.dumps({"context":os.path.normpath(c if os.path.isabs(c) else os.path.join("' + $remoteRoot + '",c)),"dockerfile":b["dockerfile"]}))'''
    $projectionText = (Invoke-AmServiceRunner $SshRunner @($projectionCommand) 'remote Compose build projection') -join "`n"
    try { $projection = $projectionText | ConvertFrom-Json -ErrorAction Stop } catch { throw 'Remote Compose build projection was not parseable JSON.' }
    if ($projection.context -ne $remoteContext -or $projection.dockerfile -ne $dockerfileRelative) { throw "Remote Compose build contract changed (context='$($projection.context)', dockerfile='$($projection.dockerfile)')." }
    Write-Host "Remote Compose build projection: context=$($projection.context); dockerfile=$($projection.dockerfile)"
    $facts = Invoke-AmServiceRunner $SshRunner @("docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service && cd $remoteRoot && docker compose ps --format json messaging-service") 'remote safe container facts'
    $previous = if ($facts.Count) { $facts[0].Trim() } else { 'unavailable' }
    $composeStatus = 'unavailable'
    if ($facts.Count -gt 1) {
        try {
            $compose = $facts[1] | ConvertFrom-Json -ErrorAction Stop
            if ($null -ne $compose.State) { $composeStatus = $compose.State.ToString() }
            elseif ($null -ne $compose.Status) { $composeStatus = $compose.Status.ToString() }
        } catch { $composeStatus = 'unparseable' }
    }
    Write-Host "Previous am-service container/image/status: $previous"
    Write-Host "Previous Compose service status: $composeStatus"
    $result = [ordered]@{ DockerfileHash=$hash; Manifest=$manifest; RemoteContext=$projection.context; RemoteDockerfile=$projection.dockerfile; PreviousContainer=$previous; ComposeStatus=$composeStatus; BackupPath=$null; MigrationCount=0; AdapterNames=@() }
    if (-not $PerformDeploy) { return [pscustomobject]$result }
    $archive = $null; $upload = '/tmp/am-service-src-' + [guid]::NewGuid().ToString('N') + '.tgz'; $stage = "$remoteRoot/build/.src-stage-" + [guid]::NewGuid().ToString('N'); $backup = "$remoteContext.bak-" + [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8); $result.BackupPath = $backup; $uploaded = $false
    try {
        $archive = New-AmServiceArchive $context $manifest; Write-Host "Validated deployment archive: $($archive.Entries.Count) paths; bin/obj/bin-* excluded."
        Invoke-AmServiceRunner $ScpRunner @($archive.Path, "$hostName`:$upload") 'archive upload' | Out-Null; $uploaded = $true
        $checks = @($manifest | ForEach-Object { "test -e '$stage/$($_.Path)'" }) -join "`n"
        $replace = "set -eu`narchive='$upload'`nstaging='$stage'`ncurrent='$remoteContext'`nbackup='$backup'`ncleanup() { rm -f -- `"`$archive`"; }`ntrap cleanup EXIT HUP INT TERM`nrm -rf -- `"`$staging`"`nmkdir -p `"`$staging`"`ntar xzf `"`$archive`" -C `"`$staging`"`n$checks`nactual_hash=`$(sha256sum `"`$staging/$dockerfileRelative`" | awk '{print `$1}')`ntest `"`$actual_hash`" = '$hash'`ntest -d `"`$current`"`nmv `"`$current`" `"`$backup`"`nmv `"`$staging`" `"`$current`"`ncd '$remoteRoot'`ndocker compose build messaging-service`ndocker compose up -d --no-deps messaging-service"
        try { Invoke-AmServiceRunner $SshRunner @($replace) 'remote archive replacement/build/recreate' | Out-Null } catch { throw "Remote replacement/build/recreate failed; retained source backup: $backup; staging path: $stage. $($_.Exception.Message)" }
        Write-Host "Retained source backup: $backup"
        $verify = "set -eu`ndeadline=`$((`$(date +%s) + $PollTimeoutSec))`nwhile [ `"`$(docker inspect --format '{{.State.Running}}' am-service 2>/dev/null || true)`" != true ]; do [ `"`$(date +%s)`" -lt `"`$deadline`" ] || exit 41; sleep 2; done`nchannels=`$(curl -fsS http://localhost:18090/api/channels)`nprintf '%s\n' `"`$channels`"`ndocker compose exec -T am-postgres sh -c 'psql -X -At -v ON_ERROR_STOP=1 -U `"`$POSTGRES_USER`" -d `"`$POSTGRES_DB`" -c '\''SELECT `"MigrationId`" FROM `"__EFMigrationsHistory`" ORDER BY `"MigrationId`";'\'''`nif docker logs --since 5m --tail 100 am-service 2>&1 | grep -Eiq 'Unhandled exception|fail:|crit:'; then exit 42; fi"
        $remoteVerify = Invoke-AmServiceRunner $SshRunner @($verify) 'remote technical verification'
        if ($remoteVerify.Count -lt 1) { throw 'Remote technical verification returned no channel JSON.' }
        try { $channels = @($remoteVerify[0] | ConvertFrom-Json -ErrorAction Stop) } catch { throw 'The am-service /api/channels response was not parseable channel JSON.' }
        $names = @($channels | ForEach-Object { if ($null -ne $_.channel) { $_.channel.ToString() } elseif ($null -ne $_.name) { $_.name.ToString() } else { 'unnamed' } })
        $remoteMigrations = @($remoteVerify | Select-Object -Skip 1 | Where-Object { $_ -match '^\d{14}_.+$' }); $sourceMigrations = @(Get-AmServiceMigrationIds $migrationDirectory); $missing = @($sourceMigrations | Where-Object { $_ -notin $remoteMigrations })
        if ($missing.Count) { throw "Remote am-postgres migration history is missing: $($missing -join ', ')" }
        $result.MigrationCount = $remoteMigrations.Count; $result.AdapterNames = $names
        Write-Host "Endpoint verified: $($names.Count) registered adapter(s): $($names -join ', ')"; Write-Host "Migration comparison verified: $($sourceMigrations.Count) source, $($remoteMigrations.Count) recorded."; Write-Host 'Startup-log scan verified: last 100 lines, no startup exception (redacted scan).'
        if ($SkipTrafficCheck) { Write-Host 'Real traffic check explicitly skipped; operator must perform the Antiphon-Family test-group round trip.' } else { Write-Host 'Real traffic check remains required: use only the Antiphon-Family test group, never the live Family group.' }
        return [pscustomobject]$result
    } finally {
        if ($archive -and (Test-Path -LiteralPath $archive.Path)) { Remove-Item -LiteralPath $archive.Path -Force -ErrorAction SilentlyContinue }
        if ($uploaded) { try { Invoke-AmServiceRunner $SshRunner @("rm -f -- '$upload'") 'remote transient archive cleanup' | Out-Null } catch { Write-Host 'WARNING: remote transient archive cleanup could not be confirmed.' } }
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$failure = $null
try {
    $root = Split-Path -Parent $PSScriptRoot
    Invoke-AmServiceDeployment $root $false $SkipRealTrafficCheck $TimeoutSec | Out-Null
    if ($Deploy) {
        if ($PSCmdlet.ShouldProcess('mc@server2:/home/mc/antiphon-messaging', 'replace build/src and recreate am-service')) { Invoke-AmServiceDeployment $root $true $SkipRealTrafficCheck $TimeoutSec | Out-Null }
        else { Write-Host 'Deployment not approved; read-only preflight completed.' }
    }
} catch { $failure = ($_.Exception.Message -replace '\s+', ' ').Trim() }
if ($failure) { Write-Output "REMOTE DEPLOY VERDICT: failed $failure"; exit 1 }
Write-Output 'REMOTE DEPLOY VERDICT: ok'
exit 0
