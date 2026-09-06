#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0270 contract tests for the am-service deploy parser and remote seam.

.DESCRIPTION
    Uses temporary Dockerfile fixtures and fake SSH/SCP runners only. It never contacts
    server2 or writes to a remote target. ASCII-only for Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'deploy-am-service.ps1')
$script:passed = 0; $script:failed = 0; $script:failures = @()
function Pass { param([string]$Name) $script:passed++; Write-Host "PASS $Name" }
function Fail { param([string]$Name, [string]$Detail) $script:failed++; $script:failures += "$Name : $Detail"; Write-Host "FAIL $Name - $Detail" }
function Assert-True { param([bool]$Condition, [string]$Name, [string]$Detail = '') if ($Condition) { Pass $Name } else { Fail $Name $Detail } }
function Assert-Throws { param([scriptblock]$Action, [string]$Name) try { & $Action; Fail $Name 'did not throw' } catch { Pass $Name } }

function Reset-MonitorFake {
    $script:monitorCase = ''; $script:identityReads = 0; $script:persisted = $false; $script:brokerReads = 0
}
Reset-MonitorFake
$http = {
    param($url,$headers)
    if ($url -like '*/api/version') { return [pscustomobject]@{StatusCode=200;Body='{"version":"0123456789012345678901234567890123456789"}'} }
    $script:identityReads++
    if ($script:monitorCase -eq '404') { return [pscustomobject]@{StatusCode=404;Body='never-log-this'} }
    $group = if ($script:monitorCase -eq 'override') { 'family-bridge-custom' } elseif ($script:monitorCase -eq 'rename' -and $script:identityReads -gt 1) { 'renamed' } else { 'antiphon-server-bridge' }
    $topic = if ($script:monitorCase -eq 'topic') { 'wrong-topic' } else { 'channels.inbound' }
    [pscustomobject]@{StatusCode=200;Body=(@{consumerGroup=$group;inboundTopic=$topic;enabled=($script:monitorCase -ne 'disabled');brokers=@(@{host='am-redpanda';port=9092});secret='never-log-this'} | ConvertTo-Json -Depth 5 -Compress)}
}
function Add-MonitorFake {
    param([scriptblock]$Original)
    $script:originalRunner = $Original
    return {
        param($command)
        if ($command -match 'C0410_GATEWAY_SETTINGS') {
            $script:commands.Add($command)
            $group = if ($script:monitorCase -eq 'override') { 'family-bridge-custom' } else { 'antiphon-server-bridge' }
            $expected = if ($script:persisted -and $script:monitorCase -ne 'missing-merged') { $group } else { $null }
            $watched = if ($script:persisted) { $group } else { 'antiphon-consumer' }
            return [pscustomobject]@{ExitCode=0;Output=(@{AntiphonConsumerGroup=$watched;ExpectedAntiphonConsumerGroup=$expected;InboundTopic='channels.inbound';ConsumerGroup='antiphon-messaging-service';BootstrapServers='am-redpanda:9092';secret='never-log-this'} | ConvertTo-Json -Compress)}
        }
        if ($command -match 'C0410_OVERRIDE') {
            $script:commands.Add($command)
            if ($script:monitorCase -eq 'ambiguous-compose') { return [pscustomobject]@{ExitCode=43;Output='never-log-this'} }
            $name = if ($script:monitorCase -eq 'compose-yaml') { 'compose.override.yaml' } else { 'docker-compose.override.yml' }
            return [pscustomobject]@{ExitCode=0;Output=(@{name=$name;exists=($script:monitorCase -in @('unmarked','marked'));managed=($script:monitorCase -ne 'unmarked')} | ConvertTo-Json -Compress)}
        }
        if ($command -match 'rpk cluster info') {
            $script:commands.Add($command); $script:brokerReads++
            $cluster = if ($script:monitorCase -eq 'broker-mismatch' -and $script:brokerReads -eq 2) { 'different-cluster' } else { 'c0410-cluster' }
            return [pscustomobject]@{ExitCode=0;Output=(@{cluster_name=$cluster} | ConvertTo-Json -Compress)}
        }
        if ($command -match 'C0410_MANAGED') { $script:commands.Add($command); $script:persisted = $true; return [pscustomobject]@{ExitCode=0;Output=@()} }
        if ($command -match '/health/inbound-unconsumed') {
            $script:commands.Add($command)
            $status = if ($script:monitorCase -eq 'readiness-404') { '404' } elseif ($script:monitorCase -eq 'degraded') { '503' } else { '200' }
            $group = if ($script:monitorCase -eq 'wrong-group') { 'antiphon-consumer' } else { 'antiphon-server-bridge' }
            $age = if ($script:monitorCase -eq 'stale') { 400 } else { 12 }
            $offsetStatus = if ($script:monitorCase -eq 'no-commit') { 'NoCommit' } else { 'CommittedOffset' }
            return [pscustomobject]@{ExitCode=0;Output=@((@{state='Ready';watchedGroup=$group;expectedGroup='antiphon-server-bridge';topic='channels.inbound';ageSeconds=$age;partitions=@(@{partition=0;status=$offsetStatus;committedNextOffset=12});secret='never-log-this'} | ConvertTo-Json -Depth 5 -Compress),$status)}
        }
        if ($command -match 'rpk group describe') { $script:commands.Add($command); return [pscustomobject]@{ExitCode=0;Output=$(if ($script:monitorCase -eq 'no-numeric') { 'channels.inbound 0 - - -' } else { 'channels.inbound 0 12 0 12 0' })} }
        if ($command -match '^docker inspect' -and $command -notmatch '&&') { $script:commands.Add($command); return [pscustomobject]@{ExitCode=0;Output='bbbbbbbbbbbb sha256:bbbbbbbbbbbb running'} }
        & $script:originalRunner $command
    }
}

function Invoke-ExpectedDeployment {
    param([string]$Root, [scriptblock]$Ssh, [scriptblock]$Scp, [scriptblock]$Http)
    try { Invoke-AmServiceDeployment $Root $true $true 10 $Ssh $Scp $Http }
    catch {
        Fail 'deploy writes the managed override and verifies the merge before recreate' $_.Exception.Message
        return [pscustomobject]@{ AdapterNames=@(); OverridePath=''; OverrideBackupPath='' }
    }
}

$root = Split-Path -Parent $PSScriptRoot; $context = Join-Path $root 'src'; $dockerfile = Join-Path $context 'Antiphon.Messaging.Service\Dockerfile'; $fixture = Join-Path ([IO.Path]::GetTempPath()) ('am-service-deploy-' + [guid]::NewGuid().ToString('N'))
try {
    $serverProfile = Get-Content -Raw (Join-Path $root 'server/appsettings.json') | ConvertFrom-Json
    $serviceProfile = Get-Content -Raw (Join-Path $context 'Antiphon.Messaging.Service/appsettings.json') | ConvertFrom-Json
    Assert-True ($serverProfile.AntiphonMessaging.ConsumerGroup -ceq 'antiphon-server-bridge' -and $serviceProfile.Kafka.AntiphonConsumerGroup -ceq $serverProfile.AntiphonMessaging.ConsumerGroup -and $serviceProfile.Kafka.ExpectedAntiphonConsumerGroup -ceq $serverProfile.AntiphonMessaging.ConsumerGroup -and $serviceProfile.Kafka.ConsumerGroup -cne $serverProfile.AntiphonMessaging.ConsumerGroup) 'repository profiles agree on the server inbound group'
    New-Item -ItemType Directory -Force $fixture | Out-Null
    foreach ($dir in @('one', 'two', 'three')) { New-Item -ItemType Directory -Force (Join-Path $fixture $dir) | Out-Null; [IO.File]::WriteAllText((Join-Path $fixture "$dir\file.txt"), $dir) }
    $fixtureDockerfile = Join-Path $fixture 'Dockerfile'
    [IO.File]::WriteAllText($fixtureDockerfile, "# comment`nCOPY one/ ./one/`nCOPY two/ \`n three/ ./target/`nCOPY --from=build /app ./`nCOPY one/ ./again/")
    $fixtureManifest = @(Get-AmServiceDockerfileManifest $fixtureDockerfile $fixture)
    Assert-True (($fixtureManifest.Path -join ',') -eq 'one,two,three') 'parser handles shell COPY, comments, continuations, order, and de-duplication' ($fixtureManifest.Path -join ',')
    Assert-True ($fixtureManifest.Count -eq 3) 'parser excludes final-stage COPY --from' "count=$($fixtureManifest.Count)"
    $unsafe = @(@('json', 'COPY ["one", "./"]'), @('variable', 'COPY ${SOURCE} ./'), @('glob', 'COPY one/* ./'), @('absolute', 'COPY /one ./'), @('parent', 'COPY ../one ./'), @('option', 'COPY --chown=1000 one ./'), @('missing', 'COPY absent ./'))
    foreach ($case in $unsafe) { [IO.File]::WriteAllText($fixtureDockerfile, $case[1]); Assert-Throws { Get-AmServiceDockerfileManifest $fixtureDockerfile $fixture | Out-Null } "unsafe syntax refuses before archive: $($case[0])" }

    $manifest = @(Get-AmServiceDockerfileManifest $dockerfile $context)
    Assert-True ($manifest.Count -gt 0) 'real Dockerfile derives source entries'
    $missing = @($manifest | Where-Object { -not (Test-Path -LiteralPath (Join-Path $context $_.Path)) })
    Assert-True ($missing.Count -eq 0) 'every real Dockerfile-derived source exists' ($missing.Path -join ',')
    $archive = New-AmServiceArchive $context $manifest
    try {
        Assert-True ($archive.Entries.Count -gt 0) 'real Dockerfile archive is non-empty'
        $forbidden = @($archive.Entries | Where-Object { ($_ -split '/') | Where-Object { $_ -eq 'bin' -or $_ -eq 'obj' -or $_ -like 'bin-*' } })
        Assert-True ($forbidden.Count -eq 0) 'real Dockerfile archive excludes bin/obj/bin-* at every depth' ($forbidden -join ',')
        $archiveMissing = @($manifest | Where-Object { $expected = $_.Path; -not ($archive.Entries | Where-Object { $_.TrimEnd('/') -eq $expected -or $_.StartsWith($expected + '/') }) })
        Assert-True ($archiveMissing.Count -eq 0) 'real Dockerfile archive contains every derived source' ($archiveMissing.Path -join ',')
    } finally { Remove-Item -LiteralPath $archive.Path -Force -ErrorAction SilentlyContinue }

    $script:commands = New-Object 'System.Collections.Generic.List[string]'; $script:scpCalls = New-Object 'System.Collections.Generic.List[string]'
    $ssh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile","secret":"never-log-this"}'.Replace('\','')} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('aaaaaaaaaaaa sha256:aaaaaaaaaaaa running','{"secret":"never-log-this"}'.Replace('\',''))} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $scp = { param($source,$destination) $script:scpCalls.Add("$source -> $destination"); return [pscustomobject]@{ExitCode=0;Output=@()} }
    $ssh = Add-MonitorFake $ssh
    $preflightLog = @(Invoke-AmServiceDeployment $root $false $false 10 $ssh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    Assert-True ($script:scpCalls.Count -eq 0) 'remote seam makes no SCP call without -Deploy' "calls=$($script:scpCalls.Count)"
    Assert-True (-not (($script:commands -join "`n") -match 'tar xzf|docker compose build|docker compose up')) 'remote seam makes no write command without -Deploy' ($script:commands -join ' | ')
    Assert-True (-not $preflightLog.Contains('never-log-this')) 'remote seam narrows Compose output before logging' $preflightLog

    $script:commands.Clear(); $script:scpCalls.Clear()
    $failingSsh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile"}'.Replace('\','')} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('aaaaaaaaaaaa sha256:aaaaaaaaaaaa running','{}')} }; if ($command -match 'docker compose build') { return [pscustomobject]@{ExitCode=9;Output='secret=never-log-this'} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $failingSsh = Add-MonitorFake $failingSsh
    Reset-MonitorFake
    $failureText = ''
    try { Invoke-AmServiceDeployment $root $true $true 10 $failingSsh $scp $http | Out-Null } catch { $failureText = $_.Exception.Message }
    Assert-True ($script:scpCalls.Count -eq 1) 'remote seam uploads only with -Deploy' "calls=$($script:scpCalls.Count)"
    Assert-True ($failureText -match 'retained source backup: /home/mc/antiphon-messaging/build/src\.bak-') 'remote seam retains backup evidence after remote failure' $failureText
    Assert-True (-not $failureText.Contains('never-log-this')) 'remote seam failure diagnostics do not leak remote output' $failureText

    $script:commands.Clear(); $script:scpCalls.Clear(); $migrationOutput = @(Get-AmServiceMigrationIds (Join-Path $context 'Antiphon.Messaging.Service\Migrations'))
    $successSsh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile"}'.Replace('\','')} }; if ($command -match 'curl -fsS') { return [pscustomobject]@{ExitCode=0;Output=(@('[{"channel":"telegram"},{"channel":"slack"}]'.Replace('\','')) + $migrationOutput)} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('aaaaaaaaaaaa sha256:aaaaaaaaaaaa running','{"State":"running"}'.Replace('\',''))} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $successSsh = Add-MonitorFake $successSsh
    Reset-MonitorFake
    $success = Invoke-ExpectedDeployment $root $successSsh $scp $http
    Assert-True (($success.AdapterNames -join ',') -eq 'telegram,slack') 'remote seam parses endpoint adapters after recreate' ($success.AdapterNames -join ',')
    Assert-True (((($script:commands -join "`n") -match 'docker compose build messaging-service') -and (($script:commands -join "`n") -match 'docker compose up -d --no-deps messaging-service'))) 'remote seam issues fixed-target build and recreate sequence' ($script:commands -join ' | ')
    $cleanupWasRequested = (($script:commands -join "`n") -match "rm -f -- '/tmp/am-service-src-")
    Assert-True $cleanupWasRequested 'remote seam removes transient upload after handled deployment' ($script:commands -join ' | ')
    $verificationCommand = @($script:commands | Where-Object { $_ -match 'curl -fsS' }) -join "`n"
    Assert-True ($verificationCommand.Contains("cd '/home/mc/antiphon-messaging'")) 'technical verification uses the deployment Compose directory'

    Reset-MonitorFake; $script:commands.Clear(); $script:scpCalls.Clear()
    $log = @(Invoke-AmServiceDeployment $root $false $false 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    $readOnly = $script:scpCalls.Count -eq 0 -and ($script:commands -join "`n") -notmatch 'tar xzf|docker compose build|docker compose up|tee |cat >|printf .*> '
    $identities = $log.Contains('repository default: antiphon-server-bridge') -and $log.Contains('server: antiphon-server-bridge') -and $log.Contains('gateway watched: antiphon-consumer') -and $log.Contains('proposed: Kafka__AntiphonConsumerGroup=antiphon-server-bridge Kafka__ExpectedAntiphonConsumerGroup=antiphon-server-bridge') -and $log.Contains('outbound group unchanged: antiphon-messaging-service')
    Assert-True ($readOnly -and $identities) 'preflight reads server identity and gateway settings without writing' 'preflight must show four identities and issue zero writes'
    foreach ($case in @(@('404','identity endpoint 404 refuses'), @('disabled','disabled bridge refuses'), @('topic','inbound topic mismatch refuses'), @('rename','identity change between preview and write refuses'), @('unmarked','unmarked existing override refuses before any write'))) {
        Reset-MonitorFake; $script:monitorCase=$case[0]; $script:commands.Clear(); $script:scpCalls.Clear()
        Assert-Throws { Invoke-AmServiceDeployment $root $true $true 10 $successSsh $scp $http | Out-Null } $case[1]
        Assert-True ($script:scpCalls.Count -eq 0 -and ($script:commands -join "`n") -notmatch 'cat >|tar xzf|docker compose up') ($case[1] + ' without writes')
    }
    Reset-MonitorFake; $script:monitorCase='override'
    $log = @(Invoke-AmServiceDeployment $root $false $false 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    Assert-True ($log.Contains('server override (differs from repository default antiphon-server-bridge)') -and $log.Contains('proposed: Kafka__AntiphonConsumerGroup=family-bridge-custom')) 'server override is shown and used'

    Reset-MonitorFake; $script:commands.Clear()
    $success = Invoke-ExpectedDeployment $root $successSsh $scp $http
    $all = $script:commands -join "`n"
    $writeIndex = $all.IndexOf('cat >'); $mergeIndex = $all.IndexOf('merged="merged"'); $upIndex = $all.IndexOf('docker compose up -d --no-deps messaging-service')
    $written = @($script:commands | Where-Object { $_ -match 'C0410_MANAGED' })
    Assert-True ($written.Count -eq 1 -and $writeIndex -ge 0 -and $mergeIndex -gt $writeIndex -and $upIndex -gt $mergeIndex -and $all -notmatch 'docker compose -f' -and $success.OverridePath -eq '/home/mc/antiphon-messaging/docker-compose.override.yml') 'deploy writes the managed override and verifies the merge before recreate' 'expected persistent override write, then merged config, then ordinary compose up'
    Assert-True ($written.Count -eq 1 -and ([regex]::Matches($written[0], 'Kafka__').Count -eq 2) -and $written[0].Contains('# managed-by: scripts/deploy-am-service.ps1 CARD-0410 - do not hand-edit')) 'managed override has exactly the two environment keys and marker'

    Reset-MonitorFake; $script:monitorCase='marked'; $script:commands.Clear()
    $success = Invoke-ExpectedDeployment $root $successSsh $scp $http
    $all = $script:commands -join "`n"
    Assert-True ($success.OverrideBackupPath -match 'docker-compose.override.yml.bak-' -and $all.IndexOf('cp --') -lt $all.IndexOf('cat >')) 'marked existing override is backed up and reported'
    foreach ($case in @('degraded','wrong-group','stale','no-commit','readiness-404','missing-merged','no-numeric')) {
        Reset-MonitorFake; $script:monitorCase=$case
        Assert-Throws { Invoke-AmServiceDeployment $root $true $true 10 $successSsh $scp $http | Out-Null } "postdeploy verification refuses $case"
    }
    Assert-True (($script:commands -join "`n") -notmatch 'rpk topic consume|--group |kafka-console-consumer') 'verification never consumes from a group'
    Reset-MonitorFake; $script:commands.Clear()
    $log = @(Invoke-AmServiceDeployment $root $true $true 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    $written = @($script:commands | Where-Object { $_ -match 'C0410_MANAGED' }) -join "`n"
    Assert-True (-not $log.Contains('never-log-this') -and -not $written.Contains('never-log-this')) 'identity, compose, and container output never leak'
    foreach ($case in @('ambiguous-compose','broker-mismatch')) {
        Reset-MonitorFake; $script:monitorCase=$case; $script:commands.Clear(); $script:scpCalls.Clear()
        $failureLog = ''
        try { $failureLog = @(Invoke-AmServiceDeployment $root $true $true 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"; Fail "$case refuses" 'did not throw' } catch { Pass "$case refuses" }
        Assert-True ($script:scpCalls.Count -eq 0 -and ($script:commands -join "`n") -notmatch 'cat >|tar xzf|docker compose up') "$case refuses without writes"
    }
    Reset-MonitorFake; $script:monitorCase='compose-yaml'
    $preview = Invoke-AmServiceDeployment $root $false $false 10 $successSsh $scp $http
    Assert-True ($preview.OverridePath -eq '/home/mc/antiphon-messaging/compose.override.yaml') 'compose.yaml uses its ordinary compose.override.yaml filename'
} catch { Fail 'test setup or execution' $_.Exception.Message } finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue } }
Write-Host ''; Write-Host ('CARD-0270 deploy-am-service: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed) { foreach ($item in $script:failures) { Write-Host ('  ' + $item) }; Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'; exit 1 }
Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 0  (PASS)'; exit 0
