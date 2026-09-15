#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0270 contract tests for the am-service deploy parser and remote seam.

.DESCRIPTION
    Uses temporary Dockerfile fixtures and fake SSH/SCP runners only. It never contacts
    a real host or writes to a remote target. ASCII-only for Windows PowerShell 5.1.
#>
[CmdletBinding()]
param([ValidateSet('C496-TargetRequired','C496-TargetValidation','C496-TargetRouting','C496-WriteGate','C496-FixedLayout')][string]$Case)
$selectedCase = $Case
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'deploy-am-service.ps1')
$script:passed = 0; $script:failed = 0; $script:failures = @()
$script:realisticCluster = 'redpanda.66fe474a-d399-4ea1-8cde-c606e98614a8'
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
            $cluster = if ($script:monitorCase -eq 'broker-mismatch' -and $script:brokerReads -eq 2) { 'different-cluster' } elseif ($script:monitorCase -eq 'legacy-cluster') { 'c0410-cluster' } elseif ($script:monitorCase -eq 'invalid-cluster') { '...' } else { $script:realisticCluster }
            $json = (@{cluster_name=$cluster} | ConvertTo-Json -Compress)
            if ($script:monitorCase -eq 'rpk-noise') {
                return [pscustomobject]@{ExitCode=0;Output=@('WARNING: This is a Redpanda Enterprise feature. Requires a license.','CLUSTER','=======',$json)}
            }
            return [pscustomobject]@{ExitCode=0;Output=$json}
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
    try { Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $Root $true $true 10 $Ssh $Scp $Http }
    catch {
        Fail 'deploy writes the managed override and verifies the merge before recreate' $_.Exception.Message
        return [pscustomobject]@{ AdapterNames=@(); OverridePath=''; OverrideBackupPath='' }
    }
}

function Initialize-C496Fake {
    param([string]$RepositoryRoot, [string]$Variant = '')
    $script:c496Events = New-Object 'System.Collections.Generic.List[object]'
    # Process-local state: the public entry creates a different script scope, so native
    # shims must not resolve their state through that entry's $script: scope.
    $global:C496Fixture = @{
        Events=$script:c496Events; Variant=$Variant; Persisted=$false
        Manifest=@(Get-AmServiceDockerfileManifest (Join-Path $RepositoryRoot 'src/Antiphon.Messaging.Service/Dockerfile') (Join-Path $RepositoryRoot 'src'))
        Migrations=@(Get-AmServiceMigrationIds (Join-Path $RepositoryRoot 'src/Antiphon.Messaging.Service/Migrations'))
    }
    # These functions replace native boundaries only in the owned child process.
    # No injected SshRunner is used: tests observe the actual default runner argv.
    function script:ssh {
        $state = $global:C496Fixture
        $state.Events.Add([pscustomobject]@{Kind='ssh';Arguments=@($args)})
        $command = $args[1]; $global:LASTEXITCODE = 0
        if ($command -match 'C0410_GATEWAY_SETTINGS') {
            $group = if ($state.Persisted) { 'antiphon-server-bridge' } else { 'antiphon-consumer' }
            return (@{AntiphonConsumerGroup=$group;ExpectedAntiphonConsumerGroup=$group;InboundTopic='channels.inbound';ConsumerGroup='antiphon-messaging-service';BootstrapServers='am-redpanda:9092';secret='never-log-this'} | ConvertTo-Json -Compress)
        }
        if ($command -match 'C0410_OVERRIDE') { return '{"name":"docker-compose.override.yml","exists":false,"managed":true}' }
        if ($command -match 'rpk cluster info') { return '{"cluster_name":"c496-cluster"}' }
        if ($command -match 'C0410_MANAGED') { $state.Persisted=$true; return }
        if ($command -match '/health/inbound-unconsumed') {
            return @('{"state":"Ready","watchedGroup":"antiphon-server-bridge","expectedGroup":"antiphon-server-bridge","topic":"channels.inbound","ageSeconds":12,"partitions":[{"partition":0,"status":"CommittedOffset","committedNextOffset":12}]}','200')
        }
        if ($command -match 'rpk group describe') { return 'channels.inbound 0 12 0 12 0' }
        if ($command -match 'docker compose config') {
            $context = if ($state.Variant -eq 'wrong-context') { '/different/build/src' } else { '/home/mc/antiphon-messaging/build/src' }
            $dockerfile = if ($state.Variant -eq 'wrong-dockerfile') { 'different/Dockerfile' } else { 'Antiphon.Messaging.Service/Dockerfile' }
            return (@{context=$context;dockerfile=$dockerfile;secret='never-log-this'} | ConvertTo-Json -Compress)
        }
        if ($command -match 'curl -fsS') { return (@('[{"channel":"telegram"},{"channel":"slack"}]') + $state.Migrations) }
        if ($command -match '^docker inspect') {
            if ($command -match '&&') { return @('aaaaaaaaaaaa sha256:aaaaaaaaaaaa running','{"State":"running","secret":"never-log-this"}') }
            return 'bbbbbbbbbbbb sha256:bbbbbbbbbbbb running'
        }
        if ($command -match 'tar xzf|^rm -f -- ') { return }
        throw 'Unexpected fixture SSH command.'
    }
    function script:scp {
        $global:C496Fixture.Events.Add([pscustomobject]@{Kind='scp';Arguments=@($args)})
        $global:LASTEXITCODE = 0
    }
    function script:tar {
        $global:C496Fixture.Events.Add([pscustomobject]@{Kind='archive';Arguments=@($args)})
        if ($args[0] -eq '-czf') {
            if ($args[1] -notmatch '^am-service-src-[a-f0-9]{32}\.tgz$') { throw 'Unexpected fixture archive name.' }
            [IO.File]::WriteAllText((Join-Path (Get-Location).Path $args[1]), 'owned fake archive')
        } elseif ($args[0] -eq '-tzf') {
            $global:C496Fixture.Manifest | ForEach-Object { $_.Path }
        } else { throw 'Unexpected fixture tar command.' }
        $global:LASTEXITCODE = 0
    }
    function script:Invoke-WebRequest {
        param($Uri, $Headers, [switch]$UseBasicParsing, $TimeoutSec)
        $global:C496Fixture.Events.Add([pscustomobject]@{Kind='http';Arguments=@($Uri)})
        if ($Uri -like '*/api/version') { return [pscustomobject]@{StatusCode=200;Content='{"version":"0123456789012345678901234567890123456789"}'} }
        if ($Uri -notlike '*/api/channels/consumer') { throw 'Unexpected fixture HTTP request.' }
        [pscustomobject]@{StatusCode=200;Content='{"consumerGroup":"antiphon-server-bridge","inboundTopic":"channels.inbound","enabled":true,"brokers":[{"host":"am-redpanda","port":9092}]}'}
    }
}

function Invoke-C496Child {
    param([string]$Mode, [string]$TargetValue = 'operator@gateway.example.invalid', [string]$Variant = '')
    $directory = Join-Path ([IO.Path]::GetTempPath()) ('c496-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    $wrapper = Join-Path $directory 'child.ps1'; $report = Join-Path $directory 'report.json'
    $childText = @'
param($TestScript, $ReportPath, $Mode, $TargetValue, $Variant)
$ErrorActionPreference = 'Stop'
. $TestScript
$repo = Split-Path -Parent (Split-Path -Parent $TestScript)
$entry = Join-Path $repo 'scripts/deploy-am-service.ps1'
Initialize-C496Fake -RepositoryRoot $repo -Variant $Variant
$record = [ordered]@{ Error=''; Probes=@(); Preview=$null; Deployment=$null; Events=@() }
$code = 0
try {
    switch ($Mode) {
        'DotSource' { . $entry }
        'MissingCli' { & $entry; $code = $LASTEXITCODE }
        'RequiredHelpers' {
            foreach ($boundary in @('Invoke-AmServiceDeployment','Invoke-AmServiceDeploymentCore')) {
                foreach ($value in @($null, '', ' ', "`t`r`n")) {
                    $script:c496Events.Clear(); $diagnosis = ''
                    try { & $boundary -RepositoryRoot $repo -PerformDeploy $true -SkipTrafficCheck $true -PollTimeoutSec 10 -SshTarget $value | Out-Null } catch { $diagnosis = $_.Exception.Message }
                    $record.Probes += [pscustomobject]@{Boundary=$boundary;Error=$diagnosis;IoCount=$script:c496Events.Count}
                }
                $script:c496Events.Clear(); $diagnosis = ''
                try { & $boundary -RepositoryRoot $repo -PerformDeploy $true -SkipTrafficCheck $true -PollTimeoutSec 10 | Out-Null } catch { $diagnosis = $_.Exception.Message }
                $record.Probes += [pscustomobject]@{Boundary=$boundary;Error=$diagnosis;IoCount=$script:c496Events.Count}
            }
        }
        'Validation' {
            $invalid = @('-option','-u@host','user@-host','user@host-','user@a..b','user@.host','user@host.','user@@host','user@host:22','user@host/path','user@host\path','user host','user@host name','user@host;echo','@host','user@','user@host_name','user@[::1]','user@host '," user@host","user@host`n","user@ho`tst",("user@ho" + [char]0 + 'st'))
            foreach ($boundary in @('Invoke-AmServiceDeployment','Invoke-AmServiceDeploymentCore')) {
                foreach ($value in $invalid) {
                    $script:c496Events.Clear(); $diagnosis = ''
                    try { & $boundary -RepositoryRoot $repo -PerformDeploy $true -SkipTrafficCheck $true -PollTimeoutSec 10 -SshTarget $value | Out-Null } catch { $diagnosis = $_.Exception.Message }
                    $record.Probes += [pscustomobject]@{Boundary=$boundary;Valid=$false;Error=$diagnosis;IoCount=$script:c496Events.Count}
                }
                foreach ($value in @('operator@gateway','a@b','User_1.Name-2@Gateway.Example.Invalid','operator@192.0.2.10','_user@gateway-2.example.invalid')) {
                    Reset-MonitorFake; $script:c496Events.Clear()
                    $result = & $boundary -RepositoryRoot $repo -PerformDeploy $false -SkipTrafficCheck $true -PollTimeoutSec 10 -SshTarget $value
                    $destinations = @($script:c496Events | Where-Object Kind -eq 'ssh' | ForEach-Object { $_.Arguments[0] })
                    $record.Probes += [pscustomobject]@{Boundary=$boundary;Valid=$true;Target=$value;ResultTarget=$result.SshTarget;Destinations=$destinations}
                }
            }
        }
        'Helpers' {
            $record.Preview = Invoke-AmServiceDeployment -RepositoryRoot $repo -PerformDeploy $false -SkipTrafficCheck $true -PollTimeoutSec 10 -SshTarget $TargetValue
            $record.Deployment = Invoke-AmServiceDeployment -RepositoryRoot $repo -PerformDeploy $true -SkipTrafficCheck $true -PollTimeoutSec 10 -SshTarget $TargetValue
        }
        'Cli' {
            $options = @{SshTarget=$TargetValue}
            if ($Variant -eq 'whatif') { $options.Deploy=$true; $options.WhatIf=$true }
            if ($Variant -eq 'deploy') { $options.Deploy=$true; $options.Confirm=$false }
            & $entry @options
            $code = $LASTEXITCODE
        }
        default { throw 'Unknown child mode.' }
    }
} catch { $record.Error = $_.Exception.Message; $code = 1 }
finally {
    $record.Events = @($script:c496Events.ToArray())
    [IO.File]::WriteAllText($ReportPath, ($record | ConvertTo-Json -Depth 15))
}
exit $code
'@
    [IO.File]::WriteAllText($wrapper, $childText, [Text.Encoding]::ASCII)
    try {
        # Synchronous, noninteractive child of the same shell (7 or 5.1); no real network.
        $shellPath = (Get-Process -Id $PID).Path
        $output = @(& $shellPath -NoProfile -NonInteractive -File $wrapper -TestScript $PSCommandPath -ReportPath $report -Mode $Mode -TargetValue $TargetValue -Variant $Variant 2>&1 | ForEach-Object { $_.ToString() }) -join "`n"
        $code = $LASTEXITCODE
        if (-not (Test-Path -LiteralPath $report)) { throw "Child produced no report: $output" }
        [pscustomobject]@{ExitCode=$code;Log=$output;Report=(Get-Content -Raw -LiteralPath $report | ConvertFrom-Json)}
    } finally {
        $resolved = [IO.Path]::GetFullPath($directory)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notmatch '^c496-[a-f0-9]{32}$') { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Assert-C496Writes {
    param($Run, [bool]$Expected, [string]$Name)
    $events = @($Run.Report.Events)
    $writes = @($events | Where-Object { $_.Kind -eq 'scp' -or $_.Kind -eq 'archive' -or ($_.Kind -eq 'ssh' -and $_.Arguments[1] -match 'C0410_MANAGED|tar xzf|docker compose build|docker compose up') })
    if (-not $Expected) { Assert-True ($writes.Count -eq 0) "$Name zero SCP/archive/override/build/recreate"; return }
    $kinds = @($events | ForEach-Object { if ($_.Kind -eq 'ssh') { $_.Arguments[1] } else { $_.Kind } }) -join "`n"
    $facts = $kinds.IndexOf('docker inspect'); $archive = $kinds.IndexOf('archive'); $persist = $kinds.IndexOf('C0410_MANAGED'); $merge = $kinds.IndexOf('merged="merged"'); $upload = $kinds.IndexOf('scp'); $build = $kinds.IndexOf('docker compose build messaging-service'); $up = $kinds.IndexOf('docker compose up -d --no-deps messaging-service')
    Assert-True ($facts -ge 0 -and $archive -gt $facts -and $persist -gt $archive -and $merge -gt $persist -and $upload -gt $merge -and $build -gt $upload -and $up -gt $build) "$Name preflight/archive/override/merge/SCP/build/recreate order"
    Assert-True (@($events | Where-Object Kind -eq 'scp').Count -eq 1 -and @($events | Where-Object Kind -eq 'archive').Count -eq 2) "$Name one upload and archive create/list"
}

function Invoke-C496Cases {
    param([string]$SelectedCase)
    $names = @('C496-TargetRequired','C496-TargetValidation','C496-TargetRouting','C496-WriteGate','C496-FixedLayout')
    foreach ($name in $names) {
        if ($SelectedCase -and $SelectedCase -ne $name) { continue }
        $before = $script:failed
        Write-Host "CASE $name"
        try {
            switch ($name) {
                'C496-TargetRequired' {
                    $run = Invoke-C496Child -Mode MissingCli
                    Assert-True ($run.ExitCode -ne 0 -and $run.Log -match 'Configuration: -SshTarget is required') "$name CLI configuration refusal" $run.Log
                    Assert-True (@($run.Report.Events).Count -eq 0) "$name CLI zero HTTP/SSH/SCP/archive"
                    $run = Invoke-C496Child -Mode RequiredHelpers
                    Assert-True ($run.ExitCode -eq 0 -and @($run.Report.Probes).Count -eq 10) "$name both helpers missing/empty/whitespace matrix" $run.Log
                    foreach ($probe in $run.Report.Probes) { Assert-True ($probe.Error -match 'Configuration: -SshTarget is required' -and $probe.IoCount -eq 0) "$name $($probe.Boundary) configuration refusal before all I/O" }
                    $run = Invoke-C496Child -Mode DotSource
                    Assert-True ($run.ExitCode -eq 0 -and @($run.Report.Events).Count -eq 0 -and $run.Log -notmatch 'REMOTE DEPLOY|Remote target:') "$name dot-source inert without target" $run.Log
                }
                'C496-TargetValidation' {
                    $run = Invoke-C496Child -Mode Validation
                    Assert-True ($run.ExitCode -eq 0 -and @($run.Report.Probes).Count -eq 56) "$name 46 invalid and 10 valid helper probes" $run.Log
                    foreach ($probe in $run.Report.Probes) {
                        if ($probe.Valid) {
                            Assert-True ($probe.ResultTarget -ceq $probe.Target -and @($probe.Destinations).Count -gt 0 -and @($probe.Destinations | Where-Object { $_ -cne $probe.Target }).Count -eq 0) "$name exact valid value $($probe.Target) via $($probe.Boundary)"
                        } else { Assert-True ($probe.Error -match 'Configuration: invalid -SshTarget' -and $probe.IoCount -eq 0) "$name invalid value refused before I/O via $($probe.Boundary)" }
                    }
                    $run = Invoke-C496Child -Mode Cli -TargetValue 'user@host:22'
                    Assert-True ($run.ExitCode -ne 0 -and $run.Log -match 'Configuration: invalid -SshTarget' -and @($run.Report.Events).Count -eq 0) "$name CLI malformed target refusal" $run.Log
                }
                'C496-TargetRouting' {
                    foreach ($target in @('operator@first.example.invalid','other@second.example.invalid')) {
                        $run = Invoke-C496Child -Mode Helpers -TargetValue $target
                        Assert-True ($run.ExitCode -eq 0) "$name $target preflight and deploy complete" $run.Log
                        $sshCalls = @($run.Report.Events | Where-Object Kind -eq 'ssh')
                        $scpCalls = @($run.Report.Events | Where-Object Kind -eq 'scp')
                        Assert-True ($sshCalls.Count -gt 0 -and @($sshCalls | Where-Object { $_.Arguments.Count -ne 2 -or $_.Arguments[0] -cne $target }).Count -eq 0) "$name every native SSH argv selects $target"
                        Assert-True ($scpCalls.Count -eq 1 -and $scpCalls[0].Arguments[1] -cmatch ('^' + [regex]::Escape($target) + ':/tmp/am-service-src-[a-f0-9]{32}\.tgz$')) "$name SCP destination selects $target"
                        Assert-True ($run.Report.Preview.SshTarget -ceq $target -and $run.Report.Deployment.SshTarget -ceq $target -and $run.Log.Contains("Remote target: ${target}:/home/mc/antiphon-messaging")) "$name returned and displayed destination"
                        $commands = @($sshCalls | ForEach-Object { $_.Arguments[1] }) -join "`n"
                        Assert-True ($commands -match '/health/inbound-unconsumed' -and $commands -match 'rpk group describe' -and $commands -match "rm -f -- '/tmp/am-service-src-") "$name includes postdeploy reads and archive cleanup"
                    }
                }
                'C496-WriteGate' {
                    foreach ($variant in @('default','whatif','deploy')) {
                        $target = 'reviewer@gate.example.invalid'
                        $run = Invoke-C496Child -Mode Cli -TargetValue $target -Variant $variant
                        Assert-True ($run.ExitCode -eq 0 -and @($run.Report.Events | Where-Object Kind -eq 'ssh').Count -gt 0) "$name $variant public entry preflight" $run.Log
                        Assert-C496Writes $run ($variant -eq 'deploy') "$name $variant"
                        if ($variant -eq 'whatif') { Assert-True ($run.Log -match ('(?im)^What if:.*' + [regex]::Escape("${target}:/home/mc/antiphon-messaging"))) "$name WhatIf selected target and fixed directory" $run.Log }
                    }
                }
                'C496-FixedLayout' {
                    $run = Invoke-C496Child -Mode Helpers
                    Assert-True ($run.ExitCode -eq 0) "$name deployment complete" $run.Log
                    $result = $run.Report.Deployment
                    Assert-True ($result.RemoteContext -ceq '/home/mc/antiphon-messaging/build/src' -and $result.RemoteDockerfile -ceq 'Antiphon.Messaging.Service/Dockerfile' -and $result.OverridePath -ceq '/home/mc/antiphon-messaging/docker-compose.override.yml') "$name fixed source and override layout"
                    Assert-True ($result.BackupPath -match '^/home/mc/antiphon-messaging/build/src\.bak-' -and $result.MigrationCount -gt 0 -and ($result.AdapterNames -join ',') -eq 'telegram,slack' -and $result.RunningContainer -eq 'bbbbbbbbbbbb sha256:bbbbbbbbbbbb running') "$name backup/migration/adapter/image evidence retained"
                    Assert-True (-not $run.Log.Contains('never-log-this')) "$name safe redacted projections"
                    Assert-C496Writes $run $true $name
                    foreach ($variant in @('wrong-context','wrong-dockerfile')) {
                        $run = Invoke-C496Child -Mode Helpers -Variant $variant
                        Assert-True ($run.ExitCode -ne 0 -and $run.Report.Error -eq 'Remote Compose build contract changed.') "$name $variant refuses" $run.Log
                        Assert-C496Writes $run $false "$name $variant"
                    }
                }
            }
        } catch { Fail $name $_.Exception.Message }
        Write-Host ("CASE RESULT {0}: {1}" -f $name, $(if ($script:failed -eq $before) { 'PASS' } else { 'FAIL' }))
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$root = Split-Path -Parent $PSScriptRoot; $context = Join-Path $root 'src'; $dockerfile = Join-Path $context 'Antiphon.Messaging.Service\Dockerfile'; $fixture = Join-Path ([IO.Path]::GetTempPath()) ('am-service-deploy-' + [guid]::NewGuid().ToString('N'))
if (-not $selectedCase) {
try {
    $serverProfile = Get-Content -Raw (Join-Path $root 'server/appsettings.json') | ConvertFrom-Json
    $serviceProfile = Get-Content -Raw (Join-Path $context 'Antiphon.Messaging.Service/appsettings.json') | ConvertFrom-Json
    Assert-True ($serverProfile.AntiphonMessaging.ConsumerGroup -ceq 'antiphon-server-bridge' -and $serviceProfile.Kafka.AntiphonConsumerGroup -ceq $serverProfile.AntiphonMessaging.ConsumerGroup -and $serviceProfile.Kafka.ExpectedAntiphonConsumerGroup -ceq $serverProfile.AntiphonMessaging.ConsumerGroup -and $serviceProfile.Kafka.ConsumerGroup -cne $serverProfile.AntiphonMessaging.ConsumerGroup) 'repository profiles agree on the server inbound group'
    $noisy = @('WARNING: This is a Redpanda Enterprise feature. Requires a license.','CLUSTER','=======',(@{cluster_name=$script:realisticCluster} | ConvertTo-Json -Compress)) -join "`n"
    $parsed = ConvertFrom-AmServiceJsonOutput $noisy
    Assert-True ($parsed.cluster_name -ceq $script:realisticCluster) 'json helper extracts cluster identity from rpk license banner noise'
    Assert-Throws { ConvertFrom-AmServiceJsonOutput 'WARNING: This is a Redpanda Enterprise feature. Requires a license.' } 'json helper refuses diagnostic-only output'
    Assert-Throws { ConvertFrom-AmServiceJsonOutput '' } 'json helper refuses empty output'
    $script:directCmds = New-Object 'System.Collections.Generic.List[string]'
    $directSsh = { param($command) $script:directCmds.Add($command); return [pscustomobject]@{ExitCode=0;Output=(@{cluster_name=$script:realisticCluster} | ConvertTo-Json -Compress)} }
    Assert-AmServiceBrokerIdentity $directSsh 'am-redpanda:9092' 'am-redpanda:9092'
    Assert-True (($script:directCmds -join "`n") -match 'docker compose exec -T redpanda rpk cluster info' -and ($script:directCmds -join "`n") -notmatch 'docker compose exec -T am-redpanda' -and ($script:directCmds -join "`n") -match '2>/dev/null') 'broker identity probe execs Compose service redpanda and discards rpk stderr' ($script:directCmds -join ' | ')
    Assert-Throws { Assert-AmServiceBrokerIdentity { param($command) return [pscustomobject]@{ExitCode=0;Output=(@{cluster_name='...'} | ConvertTo-Json -Compress)} } 'am-redpanda:9092' 'am-redpanda:9092' } 'broker identity refuses dotted-ellipsis garbage'
    Assert-Throws { Assert-AmServiceBrokerIdentity { param($command) return [pscustomobject]@{ExitCode=0;Output=(@{cluster_name='redpanda.'} | ConvertTo-Json -Compress)} } 'am-redpanda:9092' 'am-redpanda:9092' } 'broker identity refuses trailing-dot cluster name'
    Assert-Throws { Assert-AmServiceBrokerIdentity { param($command) return [pscustomobject]@{ExitCode=0;Output=(@{cluster_name='c0410 cluster'} | ConvertTo-Json -Compress)} } 'am-redpanda:9092' 'am-redpanda:9092' } 'broker identity refuses cluster names with spaces'
    $legacySsh = { param($command) return [pscustomobject]@{ExitCode=0;Output=(@{cluster_name='c0410-cluster'} | ConvertTo-Json -Compress)} }
    Assert-AmServiceBrokerIdentity $legacySsh 'am-redpanda:9092' 'am-redpanda:9092'
    Pass 'broker identity still accepts undotted c0410-cluster fixture'
    New-Item -ItemType Directory -Force $fixture | Out-Null
    foreach ($dir in @('one', 'two', 'three')) { New-Item -ItemType Directory -Force (Join-Path $fixture $dir) | Out-Null; [IO.File]::WriteAllText((Join-Path $fixture "$dir\file.txt"), $dir) }
    $fixtureDockerfile = Join-Path $fixture 'Dockerfile'
    [IO.File]::WriteAllText($fixtureDockerfile, "# comment`nCOPY one/ ./one/`nCOPY two/ \`n three/ ./target/`nCOPY --from=build /app ./`nCOPY one/ ./again/")
    $fixtureManifest = @(Get-AmServiceDockerfileManifest $fixtureDockerfile $fixture)
    Assert-True (($fixtureManifest.Path -join ',') -eq 'one,two,three') 'parser handles shell COPY, comments, continuations, order, and de-duplication' ($fixtureManifest.Path -join ',')
    Assert-True ($fixtureManifest.Count -eq 3) 'parser excludes final-stage COPY --from' "count=$($fixtureManifest.Count)"
    $unsafe = @(@('json', 'COPY ["one", "./"]'), @('variable', 'COPY ${SOURCE} ./'), @('glob', 'COPY one/* ./'), @('absolute', 'COPY /one ./'), @('parent', 'COPY ../one ./'), @('option', 'COPY --chown=1000 one ./'), @('missing', 'COPY absent ./'))
    foreach ($legacyCase in $unsafe) { [IO.File]::WriteAllText($fixtureDockerfile, $legacyCase[1]); Assert-Throws { Get-AmServiceDockerfileManifest $fixtureDockerfile $fixture | Out-Null } "unsafe syntax refuses before archive: $($legacyCase[0])" }

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
    $preflightLog = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $false $false 10 $ssh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    Assert-True ($script:scpCalls.Count -eq 0) 'remote seam makes no SCP call without -Deploy' "calls=$($script:scpCalls.Count)"
    Assert-True (-not (($script:commands -join "`n") -match 'tar xzf|docker compose build|docker compose up')) 'remote seam makes no write command without -Deploy' ($script:commands -join ' | ')
    Assert-True (-not $preflightLog.Contains('never-log-this')) 'remote seam narrows Compose output before logging' $preflightLog

    $script:commands.Clear(); $script:scpCalls.Clear()
    $failingSsh = { param($command) $script:commands.Add($command); if ($command -match 'docker compose config') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile"}'.Replace('\','')} }; if ($command -match 'docker inspect') { return [pscustomobject]@{ExitCode=0;Output=@('aaaaaaaaaaaa sha256:aaaaaaaaaaaa running','{}')} }; if ($command -match 'docker compose build') { return [pscustomobject]@{ExitCode=9;Output='secret=never-log-this'} }; return [pscustomobject]@{ExitCode=0;Output=@()} }
    $failingSsh = Add-MonitorFake $failingSsh
    Reset-MonitorFake
    $failureText = ''
    try { Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $true $true 10 $failingSsh $scp $http | Out-Null } catch { $failureText = $_.Exception.Message }
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
    $brokerCmds = @($script:commands | Where-Object { $_ -match 'rpk cluster info|rpk group describe' }) -join "`n"
    Assert-True ($brokerCmds -match 'docker compose exec -T redpanda rpk cluster info' -and $brokerCmds -match 'docker compose exec -T redpanda rpk group describe' -and $brokerCmds -notmatch 'docker compose exec -T am-redpanda') 'deploy broker probes use Compose service name redpanda' $brokerCmds

    Reset-MonitorFake; $script:commands.Clear(); $script:scpCalls.Clear()
    $log = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $false $false 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    $readOnly = $script:scpCalls.Count -eq 0 -and ($script:commands -join "`n") -notmatch 'tar xzf|docker compose build|docker compose up|tee |cat >|printf .*> '
    $identities = $log.Contains('repository default: antiphon-server-bridge') -and $log.Contains('server: antiphon-server-bridge') -and $log.Contains('gateway watched: antiphon-consumer') -and $log.Contains('proposed: Kafka__AntiphonConsumerGroup=antiphon-server-bridge Kafka__ExpectedAntiphonConsumerGroup=antiphon-server-bridge') -and $log.Contains('outbound group unchanged: antiphon-messaging-service')
    Assert-True ($readOnly -and $identities) 'preflight reads server identity and gateway settings without writing' 'preflight must show four identities and issue zero writes'
    Reset-MonitorFake; $script:monitorCase='rpk-noise'; $script:commands.Clear(); $script:scpCalls.Clear()
    $noiseLog = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $false $false 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    Assert-True ($script:scpCalls.Count -eq 0 -and $noiseLog.Contains('Broker listener metadata identifies the same cluster.')) 'preflight accepts redpanda.<uuid> cluster identity through rpk license banner noise' $noiseLog
    Reset-MonitorFake; $script:monitorCase='legacy-cluster'; $script:commands.Clear()
    $legacyLog = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $false $false 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    Assert-True ($legacyLog.Contains('Broker listener metadata identifies the same cluster.')) 'preflight still accepts undotted cluster identity' $legacyLog
    foreach ($legacyCase in @(@('404','identity endpoint 404 refuses'), @('disabled','disabled bridge refuses'), @('topic','inbound topic mismatch refuses'), @('rename','identity change between preview and write refuses'), @('unmarked','unmarked existing override refuses before any write'))) {
        Reset-MonitorFake; $script:monitorCase=$legacyCase[0]; $script:commands.Clear(); $script:scpCalls.Clear()
        Assert-Throws { Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $true $true 10 $successSsh $scp $http | Out-Null } $legacyCase[1]
        Assert-True ($script:scpCalls.Count -eq 0 -and ($script:commands -join "`n") -notmatch 'cat >|tar xzf|docker compose up') ($legacyCase[1] + ' without writes')
    }
    Reset-MonitorFake; $script:monitorCase='override'
    $log = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $false $false 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
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
    foreach ($legacyCase in @('degraded','wrong-group','stale','no-commit','readiness-404','missing-merged','no-numeric')) {
        Reset-MonitorFake; $script:monitorCase=$legacyCase
        Assert-Throws { Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $true $true 10 $successSsh $scp $http | Out-Null } "postdeploy verification refuses $legacyCase"
    }
    Assert-True (($script:commands -join "`n") -notmatch 'rpk topic consume|--group |kafka-console-consumer') 'verification never consumes from a group'
    Reset-MonitorFake; $script:commands.Clear()
    $log = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $true $true 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    $written = @($script:commands | Where-Object { $_ -match 'C0410_MANAGED' }) -join "`n"
    Assert-True (-not $log.Contains('never-log-this') -and -not $written.Contains('never-log-this')) 'identity, compose, and container output never leak'
    foreach ($legacyCase in @('ambiguous-compose','broker-mismatch','invalid-cluster')) {
        Reset-MonitorFake; $script:monitorCase=$legacyCase; $script:commands.Clear(); $script:scpCalls.Clear()
        $failureLog = ''
        try { $failureLog = @(Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $true $true 10 $successSsh $scp $http 6>&1 | ForEach-Object { $_.ToString() }) -join "`n"; Fail "$legacyCase refuses" 'did not throw' } catch { Pass "$legacyCase refuses" }
        Assert-True ($script:scpCalls.Count -eq 0 -and ($script:commands -join "`n") -notmatch 'cat >|tar xzf|docker compose up') "$legacyCase refuses without writes"
    }
    Reset-MonitorFake; $script:monitorCase='compose-yaml'
    $preview = Invoke-AmServiceDeployment -SshTarget 'operator@gateway.example.invalid' $root $false $false 10 $successSsh $scp $http
    Assert-True ($preview.OverridePath -eq '/home/mc/antiphon-messaging/compose.override.yaml') 'compose.yaml uses its ordinary compose.override.yaml filename'
} catch { Fail 'test setup or execution' $_.Exception.Message } finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue } }
}
Invoke-C496Cases -SelectedCase $selectedCase
if ($script:passed -eq 0 -and $script:failed -eq 0) { Fail 'selection' 'zero assertions executed' }
Write-Host ''; Write-Host ('CARD-0270/CARD-0496 deploy-am-service: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed) { foreach ($item in $script:failures) { Write-Host ('  ' + $item) }; Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'; exit 1 }
Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 0  (PASS)'; exit 0
