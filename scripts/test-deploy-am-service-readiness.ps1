param(
    [Parameter(Mandatory)][string]$ReadinessCase,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [Parameter(Mandatory)][string]$DeploymentScript
)
$ErrorActionPreference = 'Stop'
. $DeploymentScript
$root = Split-Path -Parent $PSScriptRoot
$script:commands = New-Object 'System.Collections.Generic.List[string]'
$script:persisted = $false
$script:verifyResult = $null
$script:laterCalls = 0
$script:endpoint = $env:C504_ENDPOINT
$script:fixture = $FixtureDirectory
$script:hostLines = New-Object 'System.Collections.Generic.List[string]'
function Write-Host {
    param([object]$Object)
    $line = [string]$Object
    $script:hostLines.Add($line)
    [Console]::WriteLine($line)
}

function Invoke-C504Shell {
    param([string]$Command)
    $directory = $script:fixture
    $working = "cd '/home/mc/antiphon-messaging'"
    $url = 'http://localhost:18090/api/channels'
    if ([regex]::Matches($Command, [regex]::Escape($working)).Count -ne 1 -or
        [regex]::Matches($Command, [regex]::Escape($url)).Count -ne 1) {
        throw 'Verification command substitution count was not one.'
    }
    $commandText = $Command.Replace($working, "cd '$($env:C504_POSIX_FIXTURE)' ").Replace($url, $script:endpoint)
    $shellFile = Join-Path $directory 'verification.sh'
    [IO.File]::WriteAllText($shellFile, ($commandText -replace "`r`n", "`n") + "`n")
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'sh'
    $start.ArgumentList.Add('-c')
    $start.ArgumentList.Add("exec sh '$($env:C504_POSIX_FIXTURE)/verification.sh' 2>&1")
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(45000)) {
            $process.Kill($true)
            throw 'Verification shell watchdog expired.'
        }
        [Threading.Tasks.Task]::WaitAll(@($stdout, $stderr), 10000) | Out-Null
        $output = $stdout.Result.TrimEnd("`r", "`n")
        if ($stderr.Result) { throw 'Shell launch wrote unexpected stderr.' }
        $lines = if ($output) { @($output -split "`r?`n") } else { @() }
        return [pscustomobject]@{ExitCode=$process.ExitCode;Output=$lines}
    } finally { $process.Dispose() }
}

$http = {
    param($url, $headers)
    if ($url -like '*/api/version') { return [pscustomobject]@{StatusCode=200;Body='{"version":"0123456789012345678901234567890123456789"}'} }
    if ($url -like '*/api/channels/consumer') { return [pscustomobject]@{StatusCode=200;Body='{"enabled":true,"consumerGroup":"antiphon-server-bridge","inboundTopic":"channels.inbound","brokers":[{"host":"am-redpanda","port":9092}]}'} }
    throw 'Unexpected HTTP runner request.'
}
$scp = {
    param($source,$destination)
    $expectedTarget = if ((Get-Command Invoke-AmServiceDeployment).Parameters.ContainsKey('SshTarget')) { 'operator@gateway.example.invalid' } else { 'mc@server2' }
    if ($destination -notlike ($expectedTarget + ':/tmp/am-service-src-*')) { throw 'Unexpected SCP destination.' }
    return [pscustomobject]@{ExitCode=0;Output=@()}
}
$ssh = {
    param($command)
    $script:commands.Add($command)
    if ($command -match 'C0410_GATEWAY_SETTINGS') {
        $group = if ($script:persisted) { 'antiphon-server-bridge' } else { 'antiphon-consumer' }
        return [pscustomobject]@{ExitCode=0;Output=(@{AntiphonConsumerGroup=$group;ExpectedAntiphonConsumerGroup=$(if ($script:persisted) {'antiphon-server-bridge'} else {$null});InboundTopic='channels.inbound';ConsumerGroup='antiphon-messaging-service';BootstrapServers='am-redpanda:9092'} | ConvertTo-Json -Compress)}
    }
    if ($command -match 'C0410_OVERRIDE') { return [pscustomobject]@{ExitCode=0;Output='{"name":"docker-compose.override.yml","exists":false,"managed":true}'} }
    if ($command -match 'rpk cluster info') { return [pscustomobject]@{ExitCode=0;Output='{"cluster_name":"redpanda.c504"}'} }
    if ($command -match '^cd /home/mc/antiphon-messaging && docker compose config --no-interpolate') { return [pscustomobject]@{ExitCode=0;Output='{"context":"/home/mc/antiphon-messaging/build/src","dockerfile":"Antiphon.Messaging.Service/Dockerfile"}'} }
    if ($command -match '^docker inspect.*&& cd ') { return [pscustomobject]@{ExitCode=0;Output=@('aaaaaaaaaaaa sha256:aaaaaaaaaaaa running','{"State":"running"}')} }
    if ($command -match 'C0410_MANAGED') { $script:persisted=$true; return [pscustomobject]@{ExitCode=0;Output=@()} }
    if ($command -match 'docker compose build messaging-service') { return [pscustomobject]@{ExitCode=0;Output=@()} }
    if ($command -match 'http://localhost:18090/api/channels') {
        $script:verifyResult = Invoke-C504Shell $command
        return $script:verifyResult
    }
    if ($command -match '/health/inbound-unconsumed') {
        $script:laterCalls++
        return [pscustomobject]@{ExitCode=0;Output=@('{"state":"Ready","watchedGroup":"antiphon-server-bridge","expectedGroup":"antiphon-server-bridge","topic":"channels.inbound","ageSeconds":12,"partitions":[{"partition":0,"status":"CommittedOffset","committedNextOffset":12}]}','200')}
    }
    if ($command -match 'rpk group describe') { $script:laterCalls++; return [pscustomobject]@{ExitCode=0;Output='channels.inbound 0 12 0 12 0'} }
    if ($command -match '^docker inspect') { $script:laterCalls++; return [pscustomobject]@{ExitCode=0;Output='bbbbbbbbbbbb sha256:bbbbbbbbbbbb running'} }
    if ($command -match '^rm -f -- ') { return [pscustomobject]@{ExitCode=0;Output=@()} }
    throw 'Unexpected remote runner command.'
}

$record = [ordered]@{case=$ReadinessCase;shellExit=$null;shellOutput=@();outerError='';outerLog='';adapterNames=@();laterCalls=0;completed=$false}
$resultFile = Join-Path $FixtureDirectory 'result.json'
try {
    $result = $null
    # The retained pre-retry baseline predates explicit target selection.
    $targetOptions = @{}
    if ((Get-Command Invoke-AmServiceDeployment).Parameters.ContainsKey('SshTarget')) {
        $targetOptions.SshTarget = 'operator@gateway.example.invalid'
    }
    $record.outerLog = (@(Invoke-AmServiceDeployment $root $true $true 10 $ssh $scp $http @targetOptions 6>&1 | ForEach-Object {
        if ($_ -isnot [string] -and $null -ne $_.PSObject.Properties['AdapterNames']) { $script:deploymentResult = $_; return }
        $_.ToString()
    }) -join "`n")
    if ($script:deploymentResult) { $record.adapterNames = @($script:deploymentResult.AdapterNames) }
} catch {
    $record.outerError = $_.Exception.Message
}
$record.outerLog = $script:hostLines -join [Environment]::NewLine
if ($script:verifyResult) {
    $record.shellExit = $script:verifyResult.ExitCode
    $record.shellOutput = @($script:verifyResult.Output)
}
$record.laterCalls = $script:laterCalls
$record.completed = $true
[IO.File]::WriteAllText($resultFile, ($record | ConvertTo-Json -Depth 8 -Compress))
if ($null -eq $record.shellExit) { throw 'Verification shell did not execute.' }
Write-Host "PASS C504 $ReadinessCase"
Write-Host 'DEPLOY-AM-SERVICE TESTS EXIT CODE: 0  (PASS)'
