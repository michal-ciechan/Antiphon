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
    <#
      Lists via a bare filename with cwd at the archive's own directory. GNU tar
      (Git for Windows ships this as the pwsh-resolved `tar` under a Bash-launched
      shell - `C:\Program Files\Git\usr\bin\tar.exe`) parses a `-f` argument
      containing a colon as SysV remote-tape syntax (`host:path`), and a Windows
      drive letter always has one. A bare filename has no colon and is unambiguous
      to both GNU tar and Windows' own bsdtar (System32\tar.exe).
    #>
    param([Parameter(Mandatory)][string]$ArchivePath, [Parameter(Mandatory)][object[]]$Manifest)
    $archiveDir = Split-Path -Parent $ArchivePath; $archiveName = Split-Path -Leaf $ArchivePath
    Push-Location $archiveDir
    try { $lines = @(& tar -tzf $archiveName 2>&1 | ForEach-Object { $_.ToString().TrimStart('./') }); $code = $LASTEXITCODE } finally { Pop-Location }
    if ($code -ne 0) { throw "Could not list deployment archive (tar exit $code)." }
    foreach ($line in $lines) { if (($line.TrimEnd('/') -split '/') | Where-Object { $_ -eq 'bin' -or $_ -eq 'obj' -or $_ -like 'bin-*' }) { throw "Deployment archive contains forbidden build output: $line" } }
    foreach ($source in $Manifest) { $path = $source.Path.TrimEnd('/'); if (-not ($lines | Where-Object { $_.TrimEnd('/') -eq $path -or $_.StartsWith($path + '/') })) { throw "Deployment archive is missing COPY source '$path' from line $($source.Line)." } }
    return $lines
}

function New-AmServiceArchive {
    <# See Test-AmServiceArchive's note: -f is a bare filename (cwd = temp dir, no
       colon), and -C ContextRoot resolves the source entries instead of Push-Location
       into the context - the two concerns (archive location, source location) are
       kept independent so neither needs a colon-bearing path on the tar command line. #>
    param([Parameter(Mandatory)][string]$ContextRoot, [Parameter(Mandatory)][object[]]$Manifest)
    $tempDir = [IO.Path]::GetTempPath(); $fileName = 'am-service-src-' + [guid]::NewGuid().ToString('N') + '.tgz'
    $path = Join-Path $tempDir $fileName
    $args = @('-czf', $fileName, '--exclude=bin', '--exclude=*/bin', '--exclude=obj', '--exclude=*/obj', '--exclude=bin-*', '--exclude=*/bin-*', '-C', $ContextRoot) + @($Manifest | ForEach-Object { $_.Path })
    Push-Location $tempDir
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

function Get-AmServiceServerIdentity {
    param([scriptblock]$HttpRunner)
    $api = $env:ANTIPHON_API; if (-not $api) { $api = 'http://localhost:17202' }
    $headers = @{}; if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
    try { $response = & $HttpRunner ($api.TrimEnd('/') + '/api/channels/consumer') $headers } catch { throw 'Server identity endpoint unavailable.' }
    if ($response.StatusCode -ne 200) { throw 'Server identity endpoint unavailable (requires HTTP 200).' }
    try { $body = $response.Body | ConvertFrom-Json -ErrorAction Stop } catch { throw 'Server identity endpoint returned invalid JSON.' }
    if ($body.enabled -ne $true) { throw 'Server identity reports disabled bridge.' }
    foreach ($key in @('consumerGroup','inboundTopic')) {
        if ([string]$body.$key -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,248}$') { throw "Server identity has invalid $key." }
    }
    $brokers = @($body.brokers | ForEach-Object {
        if ([string]$_.host -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]*$' -or $null -eq $_.port -or [int]$_.port -lt 1 -or [int]$_.port -gt 65535) { throw 'Server identity has invalid broker host/port.' }
        '{0}:{1}' -f $_.host, [int]$_.port
    })
    if (-not $brokers.Count) { throw 'Server identity has no broker addresses.' }
    return [pscustomobject]@{ consumerGroup=[string]$body.consumerGroup; inboundTopic=[string]$body.inboundTopic; brokers=($brokers -join ',') }
}

function Get-AmServiceGatewaySettings {
    param([scriptblock]$SshRunner, [switch]$MergedOnly)
    $mode = if ($MergedOnly) { 'merged' } else { 'effective' }
    $code = @'
import json,subprocess
d=json.loads(subprocess.check_output(["docker","compose","config","--format","json"]))
env=d["services"]["messaging-service"].get("environment",{})
profile=json.loads(subprocess.check_output(["docker","exec","am-service","cat","/app/appsettings.json"])).get("Kafka",{})
keys=["AntiphonConsumerGroup","ExpectedAntiphonConsumerGroup","InboundTopic","ConsumerGroup","BootstrapServers"]
merged="MODE" == "merged"
print(json.dumps({k:env.get("Kafka__"+k,None if merged and k in keys[:2] else profile.get(k)) for k in keys}))
'@
    $code = $code.Replace('MODE', $mode)
    $command = "cd /home/mc/antiphon-messaging && python3 - <<'C0410_GATEWAY_SETTINGS'`n$code`nC0410_GATEWAY_SETTINGS"
    try { $body = ConvertFrom-AmServiceJsonOutput ((Invoke-AmServiceRunner $SshRunner @($command) 'gateway settings projection') -join "`n") } catch { throw 'Gateway settings projection failed.' }
    # Validate before output or shell use, and do not carry arbitrary returned fields forward.
    foreach ($key in @('AntiphonConsumerGroup','InboundTopic','ConsumerGroup')) {
        if ([string]$body.$key -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,248}$') { throw "Gateway settings missing or invalid $key." }
    }
    if ($null -ne $body.ExpectedAntiphonConsumerGroup -and [string]$body.ExpectedAntiphonConsumerGroup -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,248}$') { throw 'Gateway expected group is invalid.' }
    if ([string]$body.BootstrapServers -notmatch '^[A-Za-z0-9.-]+:[0-9]+(,[A-Za-z0-9.-]+:[0-9]+)*$') { throw 'Gateway broker listener is invalid.' }
    return [pscustomobject]@{ AntiphonConsumerGroup=$body.AntiphonConsumerGroup; ExpectedAntiphonConsumerGroup=$body.ExpectedAntiphonConsumerGroup; InboundTopic=$body.InboundTopic; ConsumerGroup=$body.ConsumerGroup; BootstrapServers=$body.BootstrapServers }
}

function Get-AmServiceOverride {
    param([scriptblock]$SshRunner)
    $code = @'
import json,pathlib
bases=[p for p in ["compose.yaml","compose.yml","docker-compose.yaml","docker-compose.yml"] if pathlib.Path(p).is_file()]
if len(bases)!=1: raise SystemExit(43)
base=bases[0]; stem,ext=base.rsplit(".",1); override=stem+".override."+ext
others=[p for p in ["compose.override.yaml","compose.override.yml","docker-compose.override.yaml","docker-compose.override.yml"] if p!=override and pathlib.Path(p).exists()]
if others: raise SystemExit(43)
p=pathlib.Path(override); exists=p.exists()
marker="# managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit"
managed=not exists or p.read_text().splitlines()[0]==marker
print(json.dumps({"name":override,"exists":exists,"managed":managed}))
'@
    $command = "cd /home/mc/antiphon-messaging && python3 - <<'C0410_OVERRIDE'`n$code`nC0410_OVERRIDE"
    try { $body = ConvertFrom-AmServiceJsonOutput ((Invoke-AmServiceRunner $SshRunner @($command) 'managed override preflight') -join "`n") } catch { throw 'Compose base/override ambiguity or unreadable override refuses deployment.' }
    if ($body.name -notmatch '^(docker-compose|compose)\.override\.(yml|yaml)$') { throw 'Invalid managed override filename.' }
    if ($body.managed -ne $true) { throw "Unmarked existing $($body.name) refuses before any write." }
    return [pscustomobject]@{ Path=('/home/mc/antiphon-messaging/' + $body.name); Exists=($body.exists -eq $true) }
}

function ConvertFrom-AmServiceJsonOutput {
    # SSH 2>&1 merges rpk/python diagnostics into stdout; take the JSON object, not the banner.
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $trimmed = ([string]$Text).Trim()
    if ($trimmed.Length -eq 0) { throw 'JSON output was empty.' }
    try { return ($trimmed | ConvertFrom-Json -ErrorAction Stop) } catch { }
    $start = $trimmed.IndexOf('{')
    $end = $trimmed.LastIndexOf('}')
    if ($start -ge 0 -and $end -gt $start) {
        $slice = $trimmed.Substring($start, ($end - $start + 1))
        try { return ($slice | ConvertFrom-Json -ErrorAction Stop) } catch { }
    }
    throw 'JSON output was not parseable.'
}

function Assert-AmServiceBrokerIdentity {
    param([scriptblock]$SshRunner, [string]$ServerBrokers, [string]$GatewayBrokers)
    $ids = @()
    foreach ($listener in @($ServerBrokers,$GatewayBrokers)) {
        # Compose SERVICE name is redpanda; am-redpanda is only the container_name.
        $command = "cd /home/mc/antiphon-messaging && docker compose exec -T redpanda rpk cluster info -X brokers=$listener --format json 2>/dev/null"
        $raw = (Invoke-AmServiceRunner $SshRunner @($command) 'broker metadata probe') -join "`n"
        try { $body = ConvertFrom-AmServiceJsonOutput $raw } catch { throw 'Broker metadata probe failed.' }
        # Redpanda's live cluster_name is redpanda.<uuid>; keep exact identity comparison.
        $name = [string]$body.cluster_name
        if ($name.Length -lt 1 -or $name.Length -gt 249 -or $name -notmatch '^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$') { throw 'Broker metadata has no cluster identity.' }
        $ids += $name
    }
    if ($ids[0] -cne $ids[1]) { throw 'Server/gateway broker cluster identity mismatch.' }
    Write-Host 'Broker listener metadata identifies the same cluster.'
}

function Assert-AmServiceMonitor {
    param([scriptblock]$SshRunner, [object]$Identity)
    $command = "curl -sS --max-time 15 -w '\n%{http_code}' http://localhost:18090/health/inbound-unconsumed"
    $lines = @(Invoke-AmServiceRunner $SshRunner @($command) 'monitor readiness read')
    if ($lines.Count -lt 2 -or $lines[-1] -ne '200') { throw 'Monitor readiness requires HTTP 200 (missing/degraded route).' }
    try { $body = ($lines[0..($lines.Count-2)] -join "`n") | ConvertFrom-Json -ErrorAction Stop } catch { throw 'Monitor readiness is not parseable.' }
    if ($body.state -cne 'Ready') { throw 'Monitor readiness state is not Ready.' }
    if ($body.watchedGroup -cne $Identity.consumerGroup -or $body.expectedGroup -cne $Identity.consumerGroup) { throw 'Monitor readiness group mismatch.' }
    if ($body.topic -cne $Identity.inboundTopic) { throw 'Monitor readiness topic mismatch.' }
    if ($null -eq $body.ageSeconds -or $body.ageSeconds -is [string] -or [double]$body.ageSeconds -lt 0 -or [double]$body.ageSeconds -gt 130) { throw 'Monitor readiness observation is stale or missing ageSeconds.' }
    $partitions = @($body.partitions)
    if (-not $partitions.Count -or @($partitions | Where-Object { $_.status -cne 'CommittedOffset' -or $null -eq $_.committedNextOffset -or $_.committedNextOffset -is [string] -or [long]$_.committedNextOffset -lt 0 }).Count) { throw 'Monitor readiness contains unknown offsets.' }
    $group = $Identity.consumerGroup
    $raw = (Invoke-AmServiceRunner $SshRunner @("cd /home/mc/antiphon-messaging && docker compose exec -T redpanda rpk group describe $group 2>/dev/null") 'read-only group offsets') -join "`n"
    # rpk's table: TOPIC PARTITION CURRENT-OFFSET LOG-START-OFFSET LOG-END-OFFSET LAG ...
    foreach ($partition in $partitions) {
        $pattern = '(?m)^\s*' + [regex]::Escape($Identity.inboundTopic) + '\s+' + [regex]::Escape([string]$partition.partition) + '\s+([0-9]+)\s+'
        if ($raw -notmatch $pattern) { throw 'Broker group describe lacks numeric current offsets for a monitored partition.' }
        if ([long]$Matches[1] -lt [long]$partition.committedNextOffset) { throw 'Broker sampled offset is behind monitor evidence.' }
    }
    Write-Host "Monitor build marker verified: readiness route Ready; sampled offsets numeric; ageSeconds=$($body.ageSeconds)."
}

function ConvertTo-AmServiceContainerFact {
    param([string]$Text)
    if ($Text -cnotmatch '^[0-9a-f]{12,64} sha256:[0-9a-f]{12,64} (created|running|paused|restarting|removing|exited|dead)$') { throw 'Invalid container/image/status projection.' }
    return $Text
}

function Invoke-AmServiceDeployment {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][bool]$PerformDeploy,
        [Parameter(Mandatory)][bool]$SkipTrafficCheck, [Parameter(Mandatory)][int]$PollTimeoutSec,
        [scriptblock]$SshRunner, [scriptblock]$ScpRunner, [scriptblock]$HttpRunner
    )
    try {
        $result = Invoke-AmServiceDeploymentCore @PSBoundParameters
        Write-Host 'REMOTE DEPLOY VERDICT: ok'
        return $result
    } catch {
        Write-Host "REMOTE DEPLOY VERDICT: failed $($_.Exception.Message)"
        throw
    }
}

function Invoke-AmServiceDeploymentCore {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][bool]$PerformDeploy,
        [Parameter(Mandatory)][bool]$SkipTrafficCheck, [Parameter(Mandatory)][int]$PollTimeoutSec,
        [scriptblock]$SshRunner, [scriptblock]$ScpRunner, [scriptblock]$HttpRunner
    )
    $hostName = 'mc@server2'; $remoteRoot = '/home/mc/antiphon-messaging'; $remoteContext = "$remoteRoot/build/src"; $dockerfileRelative = 'Antiphon.Messaging.Service/Dockerfile'
    $context = Join-Path $RepositoryRoot 'src'; $dockerfile = Join-Path $context $dockerfileRelative; $migrationDirectory = Join-Path $context 'Antiphon.Messaging.Service\Migrations'
    if ($null -eq $SshRunner) { $SshRunner = { param($command) $output = @(& ssh $hostName $command 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output } } }
    if ($null -eq $ScpRunner) { $ScpRunner = { param($source, $destination) $output = @(& scp $source $destination 2>&1 | ForEach-Object { $_.ToString() }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output } } }
    if ($null -eq $HttpRunner) { $HttpRunner = { param($url,$headers) $r = Invoke-WebRequest -Uri $url -Headers $headers -UseBasicParsing -TimeoutSec 15; [pscustomobject]@{ StatusCode=[int]$r.StatusCode; Body=$r.Content } } }
    $identity = Get-AmServiceServerIdentity $HttpRunner
    $repositoryDefault = (Get-Content -Raw (Join-Path $RepositoryRoot 'server/appsettings.json') | ConvertFrom-Json).AntiphonMessaging.ConsumerGroup
    $gateway = Get-AmServiceGatewaySettings $SshRunner
    if ($gateway.InboundTopic -cne $identity.inboundTopic) { throw 'Inbound topic mismatch between server and gateway.' }
    if ($gateway.ConsumerGroup -ceq $identity.consumerGroup) { throw 'Server inbound group equals gateway outbound group.' }
    $override = Get-AmServiceOverride $SshRunner
    Assert-AmServiceBrokerIdentity $SshRunner $identity.brokers $gateway.BootstrapServers
    Write-Host "repository default: $repositoryDefault"
    Write-Host "server: $($identity.consumerGroup)"
    if ($identity.consumerGroup -cne $repositoryDefault) { Write-Host "server override (differs from repository default $repositoryDefault)" }
    Write-Host "gateway watched: $($gateway.AntiphonConsumerGroup); expected: $($gateway.ExpectedAntiphonConsumerGroup)"
    Write-Host "proposed: Kafka__AntiphonConsumerGroup=$($identity.consumerGroup) Kafka__ExpectedAntiphonConsumerGroup=$($identity.consumerGroup)"
    Write-Host "outbound group unchanged: $($gateway.ConsumerGroup)"
    $api = $env:ANTIPHON_API; if (-not $api) { $api = 'http://localhost:17202' }
    $versionHeaders = @{}; if ($env:ANTIPHON_TASK_TOKEN) { $versionHeaders['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
    try { $version = & $HttpRunner ($api.TrimEnd('/') + '/api/version') $versionHeaders; $sha = ($version.Body | ConvertFrom-Json).version; if ($version.StatusCode -eq 200 -and $sha -match '^[a-f0-9]{40}$') { Write-Host "Server version provenance: $sha" } else { throw 'invalid' } } catch { throw 'Server version provenance unavailable.' }
    foreach ($command in @('ssh', 'scp', 'tar')) { if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required local command not found: $command" } }
    $manifest = @(Get-AmServiceDockerfileManifest $dockerfile $context); $hash = (Get-FileHash -LiteralPath $dockerfile -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestText = (@($manifest | ForEach-Object { $_.Path }) -join ', ')
    Write-Host "Dockerfile SHA-256: $hash"; Write-Host "Dockerfile-derived source entries ($($manifest.Count)): $manifestText"
    $projectionCommand = 'cd ' + $remoteRoot + ' && docker compose config --no-interpolate --format json messaging-service | python3 -c ''import json, os, sys; d=json.load(sys.stdin); b=d["services"]["messaging-service"]["build"]; c=b["context"]; print(json.dumps({"context":os.path.normpath(c if os.path.isabs(c) else os.path.join("' + $remoteRoot + '",c)),"dockerfile":b["dockerfile"]}))'''
    $projectionText = (Invoke-AmServiceRunner $SshRunner @($projectionCommand) 'remote Compose build projection') -join "`n"
    try { $projection = $projectionText | ConvertFrom-Json -ErrorAction Stop } catch { throw 'Remote Compose build projection was not parseable JSON.' }
    if ($projection.context -ne $remoteContext -or $projection.dockerfile -ne $dockerfileRelative) { throw 'Remote Compose build contract changed.' }
    Write-Host "Remote Compose build projection: context=$($projection.context); dockerfile=$($projection.dockerfile)"
    $facts = Invoke-AmServiceRunner $SshRunner @("docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service && cd $remoteRoot && docker compose ps --format json messaging-service") 'remote safe container facts'
    $previous = if ($facts.Count) { ConvertTo-AmServiceContainerFact $facts[0].Trim() } else { throw 'Container identity unavailable.' }
    $composeStatus = 'unavailable'
    if ($facts.Count -gt 1) {
        try {
            $compose = $facts[1] | ConvertFrom-Json -ErrorAction Stop
            if ($compose.State -match '^(created|running|paused|restarting|removing|exited|dead)$') { $composeStatus = $compose.State.ToString() }
        } catch { $composeStatus = 'unparseable' }
    }
    Write-Host "Previous am-service container/image/status: $previous"
    Write-Host "Previous Compose service status: $composeStatus"
    $result = [ordered]@{ DockerfileHash=$hash; Manifest=$manifest; RemoteContext=$projection.context; RemoteDockerfile=$projection.dockerfile; PreviousContainer=$previous; ComposeStatus=$composeStatus; BackupPath=$null; MigrationCount=0; AdapterNames=@() }
    $result.OverridePath = $override.Path; $result.OverrideBackupPath = $null
    if (-not $PerformDeploy) { return [pscustomobject]$result }
    $latest = Get-AmServiceServerIdentity $HttpRunner
    if (($latest | ConvertTo-Json -Compress) -cne ($identity | ConvertTo-Json -Compress)) { throw 'Server identity change between preview and write refuses deployment.' }
    $archive = $null; $upload = '/tmp/am-service-src-' + [guid]::NewGuid().ToString('N') + '.tgz'; $stage = "$remoteRoot/build/.src-stage-" + [guid]::NewGuid().ToString('N'); $backup = "$remoteContext.bak-" + [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8); $result.BackupPath = $backup; $uploaded = $false
    try {
        $archive = New-AmServiceArchive $context $manifest; Write-Host "Validated deployment archive: $($archive.Entries.Count) paths; bin/obj/bin-* excluded."
        $overrideBackup = $override.Path + '.bak-' + [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
        $overrideText = "# managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit`nservices:`n  messaging-service:`n    environment:`n      Kafka__AntiphonConsumerGroup: '$($identity.consumerGroup)'`n      Kafka__ExpectedAntiphonConsumerGroup: '$($identity.consumerGroup)'`n"
        $persist = "set -eu`n"
        if ($override.Exists) {
            $persist += "cp -- '$($override.Path)' '$overrideBackup'`n"
            $result.OverrideBackupPath = $overrideBackup
        }
        $persist += "cat > '$($override.Path)' <<'C0410_MANAGED'`n$overrideText" + "C0410_MANAGED"
        if ($PerformDeploy) { Invoke-AmServiceRunner $SshRunner @($persist) 'persist managed override' | Out-Null }
        $merged = Get-AmServiceGatewaySettings $SshRunner -MergedOnly
        if ($merged.AntiphonConsumerGroup -cne $identity.consumerGroup -or $merged.ExpectedAntiphonConsumerGroup -cne $identity.consumerGroup) { throw 'Merged docker compose config lacks matching watched/expected groups.' }
        if ($merged.ConsumerGroup -cne $gateway.ConsumerGroup -or $merged.InboundTopic -cne $gateway.InboundTopic -or $merged.BootstrapServers -cne $gateway.BootstrapServers) { throw 'Merged docker compose config changed unrelated gateway settings.' }
        Write-Host "Managed override: $($override.Path); retained override backup: $($result.OverrideBackupPath)"
        Invoke-AmServiceRunner $ScpRunner @($archive.Path, "$hostName`:$upload") 'archive upload' | Out-Null; $uploaded = $true
        $checks = @($manifest | ForEach-Object { "test -e '$stage/$($_.Path)'" }) -join "`n"
        $replace = "set -eu`narchive='$upload'`nstaging='$stage'`ncurrent='$remoteContext'`nbackup='$backup'`ncleanup() { rm -f -- `"`$archive`"; }`ntrap cleanup EXIT HUP INT TERM`nrm -rf -- `"`$staging`"`nmkdir -p `"`$staging`"`ntar xzf `"`$archive`" -C `"`$staging`"`n$checks`nactual_hash=`$(sha256sum `"`$staging/$dockerfileRelative`" | awk '{print `$1}')`ntest `"`$actual_hash`" = '$hash'`ntest -d `"`$current`"`nmv `"`$current`" `"`$backup`"`nmv `"`$staging`" `"`$current`"`ncd '$remoteRoot'`ndocker compose build messaging-service`ndocker compose up -d --no-deps messaging-service"
        try { Invoke-AmServiceRunner $SshRunner @($replace) 'remote archive replacement/build/recreate' | Out-Null } catch { throw "Remote replacement/build/recreate failed; retained source backup: $backup; staging path: $stage. $($_.Exception.Message)" }
        Write-Host "Retained source backup: $backup"
        $verify = "set -eu`ncd '$remoteRoot'`ndeadline=`$((`$(date +%s) + $PollTimeoutSec))`nwhile [ `"`$(docker inspect --format '{{.State.Running}}' am-service 2>/dev/null || true)`" != true ]; do [ `"`$(date +%s)`" -lt `"`$deadline`" ] || exit 41; sleep 2; done`nchannels=`$(curl -fsS http://localhost:18090/api/channels)`nprintf '%s\n' `"`$channels`"`ndocker compose exec -T am-postgres sh -c 'psql -X -At -v ON_ERROR_STOP=1 -U `"`$POSTGRES_USER`" -d `"`$POSTGRES_DB`" -c '\''SELECT `"MigrationId`" FROM `"__EFMigrationsHistory`" ORDER BY `"MigrationId`";'\'''`nif docker logs --since 5m --tail 100 am-service 2>&1 | grep -Eiq 'Unhandled exception|fail:|crit:'; then exit 42; fi"
        $remoteVerify = Invoke-AmServiceRunner $SshRunner @($verify) 'remote technical verification'
        if ($remoteVerify.Count -lt 1) { throw 'Remote technical verification returned no channel JSON.' }
        try { $channels = @($remoteVerify[0] | ConvertFrom-Json -ErrorAction Stop) } catch { throw 'The am-service /api/channels response was not parseable channel JSON.' }
        $names = @($channels | ForEach-Object { if ($null -ne $_.channel) { $_.channel.ToString() } elseif ($null -ne $_.name) { $_.name.ToString() } else { 'unnamed' } })
        $remoteMigrations = @($remoteVerify | Select-Object -Skip 1 | Where-Object { $_ -match '^\d{14}_.+$' }); $sourceMigrations = @(Get-AmServiceMigrationIds $migrationDirectory); $missing = @($sourceMigrations | Where-Object { $_ -notin $remoteMigrations })
        if ($missing.Count) { throw "Remote am-postgres migration history is missing: $($missing -join ', ')" }
        $result.MigrationCount = $remoteMigrations.Count; $result.AdapterNames = $names
        Assert-AmServiceMonitor $SshRunner $identity
        $running = @(Invoke-AmServiceRunner $SshRunner @("docker inspect --format '{{.Id}} {{.Image}} {{.State.Status}}' am-service") 'running image identity')
        if ($running.Count) { $running[0] = ConvertTo-AmServiceContainerFact $running[0].Trim() }
        if (-not $running.Count -or ($running[0] -split ' ')[1] -eq ($previous -split ' ')[1]) { throw 'Running image ID did not change from PreviousContainer.' }
        $result.RunningContainer = $running[0]
        Write-Host "Running am-service container/image/status: $($running[0])"
        $latest = Get-AmServiceServerIdentity $HttpRunner
        if (($latest | ConvertTo-Json -Compress) -cne ($identity | ConvertTo-Json -Compress)) { throw 'Server identity changed during deployment verification.' }
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
