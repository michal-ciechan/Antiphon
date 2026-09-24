[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('desktop-server', 'apphost', 'session-runner', 'pty-host', 'hangfire', 'fake-gateway', 'server2-runner', 'server2-deploy', 'transcripts')]
    [string]$Source,

    [ValidateRange(1, 10000)]
    [int]$Tail = 100
)

$ErrorActionPreference = 'Stop'

function Show-Tail {
    param([Parameter(Mandatory)][string]$Path)

    $files = @(Get-ChildItem -Path $Path -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) {
        throw "No log files found at $Path."
    }

    Write-Output ("--- {0} ---" -f $files[-1].FullName)
    Get-Content -LiteralPath $files[-1].FullName -Tail $Tail
}

$canonicalRoot = 'C:\src\Antiphon'
$logRoot = Join-Path $canonicalRoot 'logs'

switch ($Source) {
    'desktop-server' { Show-Tail (Join-Path $logRoot 'antiphon-*.log') }
    'apphost' { Get-Content -LiteralPath (Join-Path $logRoot 'apphost.log') -Tail $Tail }
    'session-runner' {
        Get-Content -LiteralPath (Join-Path $logRoot 'session-runner.log') -Tail $Tail
        $serilog = Join-Path ([System.IO.Path]::GetTempPath()) 'antiphon-logs\session-runner-*.log'
        if (Get-ChildItem -Path $serilog -File -ErrorAction SilentlyContinue) { Show-Tail $serilog }
    }
    'pty-host' { Show-Tail 'C:\logs\antiphon\session-runner\pty-hosts\logs\*.log' }
    'hangfire' {
        $api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }
        $response = Invoke-WebRequest "$api/hangfire" -ErrorAction Stop
        Write-Output ("Hangfire dashboard retrieved: HTTP {0}. Inspect Failed and Recurring Jobs in {1}/hangfire; its in-memory history is not exposed as a log file." -f $response.StatusCode, $api)
    }
    'fake-gateway' { Get-Content -LiteralPath (Join-Path $logRoot 'fake-gateway.log') -Tail $Tail }
    'server2-runner' {
        ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 "docker logs --tail $Tail antiphon-runner-session-runner-1"
        if ($LASTEXITCODE -ne 0) { throw "server2 runner log retrieval failed (ssh/docker exit $LASTEXITCODE)." }
    }
    'server2-deploy' {
        ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 "find /home/mc/antiphon-server2 -maxdepth 2 -type f \( -name '*.log' -o -name '*.json' -o -name '*.txt' \) -print"
        if ($LASTEXITCODE -ne 0) { throw "server2 deployment-evidence inventory failed (ssh exit $LASTEXITCODE)." }
        throw 'No durable server2 deployment-evidence location is configured; see docs/logs.md.'
    }
    'transcripts' {
        $api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }
        $headers = @{}
        if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
        $agents = @(Invoke-RestMethod "$api/api/agents" -Headers $headers -ErrorAction Stop)
        $sessions = $agents | Where-Object { $_.liveSession -and $_.liveSession.id }
        if ($sessions.Count -eq 0) { throw 'No live agent sessions were returned; use the session id from an agent/task record for a historical transcript.' }
        foreach ($agent in $sessions) {
            Write-Output ("--- {0} {1} ---" -f $agent.name, $agent.liveSession.id)
            $transcript = Invoke-RestMethod "$api/api/sessions/$($agent.liveSession.id)/transcript?since=0" -Headers $headers
            $transcript.entries | Select-Object -Last $Tail
        }
    }
}
