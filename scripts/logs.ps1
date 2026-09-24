[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('desktop-server', 'apphost', 'session-runner', 'pty-host', 'hangfire', 'fake-gateway', 'server2-runner', 'server2-deploy', 'transcripts')]
    [string]$Source,

    [ValidateRange(1, 10000)]
    [int]$Tail = 100
)

$ErrorActionPreference = 'Stop'

function Get-MainCheckoutRoot {
    $repositoryRoot = @(& git -C $PSScriptRoot rev-parse --show-toplevel 2>&1 | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or -not $repositoryRoot) {
        throw "Could not resolve the Git checkout containing $PSScriptRoot."
    }

    $worktrees = @(& git -C $repositoryRoot worktree list --porcelain 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list Git worktrees for $repositoryRoot."
    }

    $mainRecord = @($worktrees | ForEach-Object { $_.ToString() } | Where-Object { $_ -match '^worktree\s+(.+)$' }) | Select-Object -First 1
    if (-not $mainRecord) {
        throw "Git did not report a primary worktree for $repositoryRoot."
    }

    $mainRoot = [System.IO.Path]::GetFullPath(([regex]::Match($mainRecord, '^worktree\s+(.+)$').Groups[1].Value.Trim()))
    if (-not (Test-Path -LiteralPath $mainRoot -PathType Container)) {
        throw "Git primary worktree does not exist: $mainRoot."
    }

    return $mainRoot
}

function Show-Tail {
    param([Parameter(Mandatory)][string]$Path)

    $files = @(Get-ChildItem -Path $Path -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) {
        throw "No log files found at $Path."
    }

    Write-Output ("--- {0} ---" -f $files[-1].FullName)
    Get-Content -LiteralPath $files[-1].FullName -Tail $Tail
}

$mainCheckoutRoot = Get-MainCheckoutRoot
$logRoot = Join-Path $mainCheckoutRoot 'logs'
$desktopServerLogRoot = Join-Path $mainCheckoutRoot 'server\logs'

switch ($Source) {
    'desktop-server' { Show-Tail (Join-Path $desktopServerLogRoot 'antiphon-*.log') }
    'apphost' { Get-Content -LiteralPath (Join-Path $logRoot 'apphost.log') -Tail $Tail }
    'session-runner' {
        Get-Content -LiteralPath (Join-Path $logRoot 'session-runner.log') -Tail $Tail
        $serilog = Join-Path ([System.IO.Path]::GetTempPath()) 'antiphon-logs\session-runner-*.log'
        if (Get-ChildItem -Path $serilog -File -ErrorAction SilentlyContinue) { Show-Tail $serilog }
    }
    'pty-host' { Show-Tail 'C:\logs\antiphon\session-runner\pty-hosts\logs\*.log' }
    'hangfire' {
        $api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }
        # CARD-0658: the dashboard needs the operator token, whatever the address. Same file
        # lookup as Add-OperatorToken in scripts/runner-slots.ps1; the value is never printed.
        $tokenPath = $env:ANTIPHON_OPERATOR_TOKEN_FILE
        if ([string]::IsNullOrWhiteSpace($tokenPath)) {
            $tokenPath = Join-Path (Join-Path $env:LOCALAPPDATA 'Antiphon') 'operator-token'
        }
        if (-not (Test-Path -LiteralPath $tokenPath)) {
            throw "Operator token file not found at $tokenPath. The server creates it at startup; run this as the account the server runs under."
        }
        $headers = @{ 'X-Antiphon-Operator-Token' = (Get-Content -LiteralPath $tokenPath -Raw).Trim() }
        try {
            $status = [int](Invoke-WebRequest "$api/hangfire" -Headers $headers -ErrorAction Stop).StatusCode
        }
        catch {
            if ($null -eq $_.Exception.Response) { throw }
            $status = [int]$_.Exception.Response.StatusCode
        }
        Write-Output ("Hangfire dashboard: HTTP {0}. Open it with scripts/hangfire-dashboard.ps1 and inspect Failed and Recurring Jobs; its in-memory history is not exposed as a log file." -f $status)
    }
    'fake-gateway' { Get-Content -LiteralPath (Join-Path $logRoot 'fake-gateway.log') -Tail $Tail }
    'server2-runner' {
        ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 "docker logs --tail $Tail antiphon-runner-session-runner-1; docker exec antiphon-runner-session-runner-1 sh -lc 'tail -n $Tail /state/runner-logs/session-runner-*.log /state/logs/dockerd.log 2>&1'"
        if ($LASTEXITCODE -ne 0) { throw "server2 runner log retrieval failed (ssh/docker exit $LASTEXITCODE)." }
    }
    'server2-deploy' {
        ssh -o BatchMode=yes -o ConnectTimeout=15 mc@server2 "find /home/mc/antiphon-server2 -path /home/mc/antiphon-server2/secrets -prune -o -maxdepth 2 -type f \( -name '*.log' -o -name '*.json' -o -name '*.txt' \) -print"
        if ($LASTEXITCODE -ne 0) { throw "server2 deployment-evidence inventory failed (ssh exit $LASTEXITCODE)." }
        throw 'No durable server2 deployment-evidence location is configured; see docs/logs.md.'
    }
    'transcripts' {
        $api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }
        $headers = @{}
        if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
        # Invoke-RestMethod already returns an Object[] for this endpoint. Do not wrap the call in
        # @(): that makes the whole array one pseudo-agent and produces a malformed transcript URL.
        $agents = Invoke-RestMethod "$api/api/agents" -Headers $headers -TimeoutSec 15 -ErrorAction Stop
        $sessions = $agents | Where-Object { $_.liveSession -and $_.liveSession.id }
        if ($sessions.Count -eq 0) { throw 'No live agent sessions were returned; use the session id from an agent/task record for a historical transcript.' }
        foreach ($agent in $sessions) {
            Write-Output ("--- {0} {1} ---" -f $agent.name, $agent.liveSession.id)
            $transcript = Invoke-RestMethod "$api/api/sessions/$($agent.liveSession.id)/transcript?since=0" -Headers $headers -TimeoutSec 15 -ErrorAction Stop
            $transcript.entries | Select-Object -Last $Tail
        }
    }
}
