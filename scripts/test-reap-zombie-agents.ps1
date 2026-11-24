#requires -Version 5.1
<#
.SYNOPSIS
    Fixture/shim tests for scripts/reap-zombie-agents.ps1 (CARD-0221 S2).

    All live IO is injected (-ProcessesJson, -RunnerJson, -DbJson, -Now), or the
    AppHost user-secrets store is replaced with an isolated test directory.
    This file never enumerates Win32_Process, talks to a real database, or uses
    -Execute at a real API. -Execute uses -HttpShim, which records kill calls.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'

$here = $PSScriptRoot
$reap = Join-Path $here 'reap-zombie-agents.ps1'

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-Eq {
    param($Actual, $Expected, [string]$Name)
    if ("$Actual" -eq "$Expected") { Write-Pass $Name }
    else { Write-Fail $Name "expected=$Expected actual=$Actual" }
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

function New-TestDir {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('zombie-agents-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Save-Json {
    param([string]$Path, $Object)
    if ($null -eq $Object) {
        Set-Content -LiteralPath $Path -Value '[]' -Encoding UTF8
        return
    }
    $json = $Object | ConvertTo-Json -Depth 10
    if (-not $json) { $json = '[]' }
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function New-GuidN([int]$N) {
    return [guid]::Parse(('aaaaaaaa-0000-0000-0000-{0:D12}' -f $N))
}

function Invoke-Reap {
    param([hashtable]$Params)
    $Params['PassThru'] = $true
    if (-not $Params.ContainsKey('ReportPath')) {
        $Params['ReportPath'] = New-TestDir
    }
    if (-not $Params.ContainsKey('SessionLogPath')) {
        $Params['SessionLogPath'] = $Params.ReportPath
    }
    return & $reap @Params
}

if (-not (Test-Path -LiteralPath $reap)) { throw "missing $reap" }

# --- census fixture (22 agent-shaped processes, 22 runner sessions) ------------------------

function New-CensusFiles {
    param([string]$Dir)
    $sessions = @()
    $processes = @()
    $dbSessions = @()
    $dbAgents = @()
    $now = [datetime]::SpecifyKind(([datetime]'2026-08-28T22:00:00Z').ToUniversalTime(), 'Utc')

    $pidCursor = 20000
    function Next-Pid { $script:pidCursor++; return $script:pidCursor }

    $kinds = @()
    1..10 | ForEach-Object { $kinds += 'claude' }
    1..5  | ForEach-Object { $kinds += 'grok' }
    1..6  | ForEach-Object { $kinds += 'codex' }

    for ($i = 1; $i -le 21; $i++) {
        $sid = New-GuidN $i
        $kind = $kinds[$i - 1]
        $hostPid = 30000 + $i
        $childPid = 0
        if ($kind -eq 'codex') {
            $cmdPid = 40000 + $i
            $nodePid = 41000 + $i
            $codexPid = 42000 + $i
            $childPid = $cmdPid
            $processes += [pscustomobject]@{
                processId = $hostPid; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
                executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
                commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\src\Antiphon'
                creationDate = '2026-08-28T12:00:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.1
            }
            $processes += [pscustomobject]@{
                processId = $cmdPid; parentProcessId = $hostPid; name = 'cmd.exe'
                executablePath = 'C:\Windows\System32\cmd.exe'
                commandLine = 'cmd.exe /c codex'; cwd = 'C:\src\Antiphon'
                creationDate = '2026-08-28T12:00:00Z'; workingSetSize = 8000000; cpuDeltaPercent = 0.1
            }
            $processes += [pscustomobject]@{
                processId = $nodePid; parentProcessId = $cmdPid; name = 'node.exe'
                executablePath = 'C:\Program Files\nodejs\node.exe'
                commandLine = 'node.exe'; cwd = 'C:\src\Antiphon'
                creationDate = '2026-08-28T12:00:00Z'; workingSetSize = 20000000; cpuDeltaPercent = 1.0
            }
            $processes += [pscustomobject]@{
                processId = $codexPid; parentProcessId = $nodePid; name = 'codex.exe'
                executablePath = 'C:\Users\lndco\AppData\Roaming\npm\codex.exe'
                commandLine = 'codex.exe'; cwd = 'C:\src\Antiphon'
                creationDate = '2026-08-28T12:00:00Z'; workingSetSize = 300000000; cpuDeltaPercent = 2.0
            }
        } else {
            $leafPid = 50000 + $i
            $childPid = $leafPid
            $exe = 'claude.exe'
            $exePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
            if ($kind -eq 'grok') {
                $exe = 'grok.exe'
                $exePath = 'C:\Users\lndco\AppData\Local\grok\grok.exe'
            }
            $processes += [pscustomobject]@{
                processId = $hostPid; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
                executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
                commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\src\Antiphon'
                creationDate = '2026-08-28T12:00:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.1
            }
            $processes += [pscustomobject]@{
                processId = $leafPid; parentProcessId = $hostPid; name = $exe
                executablePath = $exePath
                commandLine = $exe; cwd = 'C:\src\Antiphon'
                creationDate = '2026-08-28T12:00:00Z'; workingSetSize = 400000000; cpuDeltaPercent = 5.0
            }
        }

        $sessions += [pscustomobject]@{
            sessionId = $sid.ToString('D')
            pid       = $childPid
            hostPid   = $hostPid
            status    = 'Running'
            startedAt = '2026-08-28T12:00:00Z'
            backend   = 'PtyHost'
        }
        $dbSessions += [pscustomobject]@{
            id = $sid.ToString('D'); status = 'Running'
            startedAt = '2026-08-28T12:00:00Z'; endedAt = $null
            cwd = 'C:\src\Antiphon'; agentKind = 'ClaudeCode'
        }
        $dbAgents += [pscustomobject]@{
            id = (New-GuidN (100 + $i)).ToString('D')
            name = ('agent-{0}' -f $i); slug = ('agent-{0}' -f $i)
            isPoolDelegate = $false; status = 'Running'
            persistentSessionId = $sid.ToString('D')
            workingDirectory = 'C:\src\Antiphon'
        }
    }

    # Operator-launched Claude (WindowsTerminal ancestor) - the 22nd agent-shaped process.
    $opLeaf = 27592
    $opCmd = 27590
    $opWt = 27580
    $processes += [pscustomobject]@{
        processId = $opWt; parentProcessId = 4; name = 'WindowsTerminal.exe'
        executablePath = 'C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe'
        commandLine = 'WindowsTerminal.exe'; cwd = 'C:\src\ClaudeBot'
        creationDate = '2026-08-28T10:00:00Z'; workingSetSize = 50000000; cpuDeltaPercent = 0.2
    }
    $processes += [pscustomobject]@{
        processId = $opCmd; parentProcessId = $opWt; name = 'cmd.exe'
        executablePath = 'C:\Windows\System32\cmd.exe'
        commandLine = 'cmd.exe'; cwd = 'C:\src\ClaudeBot'
        creationDate = '2026-08-28T10:00:00Z'; workingSetSize = 5000000; cpuDeltaPercent = 0.1
    }
    $processes += [pscustomobject]@{
        processId = $opLeaf; parentProcessId = $opCmd; name = 'claude.exe'
        executablePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
        commandLine = 'claude.exe --name ClaudeBot'; cwd = 'C:\src\ClaudeBot'
        creationDate = '2026-08-28T10:00:00Z'; workingSetSize = 520000000; cpuDeltaPercent = 1.5
    }

    # 22nd runner session with no matching agent-shaped process.
    $sessions += [pscustomobject]@{
        sessionId = (New-GuidN 22).ToString('D')
        pid = 59999; hostPid = 59998; status = 'Running'
        startedAt = '2026-08-28T12:00:00Z'; backend = 'PtyHost'
    }
    $dbSessions += [pscustomobject]@{
        id = (New-GuidN 22).ToString('D'); status = 'Running'
        startedAt = '2026-08-28T12:00:00Z'; endedAt = $null
        cwd = 'C:\src\Antiphon'; agentKind = 'ClaudeCode'
    }

    $pFile = Join-Path $Dir 'processes.json'
    $rFile = Join-Path $Dir 'runner.json'
    $dFile = Join-Path $Dir 'db.json'
    Save-Json $pFile $processes
    Save-Json $rFile $sessions
    Save-Json $dFile ([pscustomobject]@{ sessions = $dbSessions; agents = $dbAgents; tasks = @(); manifests = @() })
    return [pscustomobject]@{ Processes = $pFile; Runner = $rFile; Db = $dFile; Now = '2026-08-28T22:00:00Z' }
}

# --- T-census ------------------------------------------------------------------------------
$cDir = New-TestDir
$cFiles = New-CensusFiles $cDir
$c = Invoke-Reap @{
    ProcessesJson = $cFiles.Processes
    RunnerJson    = $cFiles.Runner
    DbJson        = $cFiles.Db
    Now           = $cFiles.Now
    ReportPath    = $cDir
}
Assert-Eq $c.ExitCode 0 'census exit 0 (no positives)'
$cClaimed = @($c.Rows | Where-Object { $_.IdentityMethod -eq 'I1' })
Assert-Eq $cClaimed.Count 21 'census 21 runner-claimed (I1)'
$cIgnored = @($c.Ignored)
Assert-Eq $cIgnored.Count 1 'census 1 ignored'
Assert-True ($cIgnored[0].Pid -eq 27592) 'census ignored pid is operator claude 27592' ('pid=' + $cIgnored[0].Pid)
Assert-True ($cIgnored[0].RulesFailed -match 'operator') 'census ignored reason is operator-launched' $cIgnored[0].RulesFailed
$cCodex = @($cClaimed | Where-Object { $_.Exe -eq 'codex.exe' })
Assert-Eq $cCodex.Count 6 'census Codex three-hop matched (6)'
Assert-Eq @($c.Positives).Count 0 'census 0 positives'
Assert-Eq $c.KillCalls.Count 0 'census no kill calls'

# --- incident fixture (0ea601b2 / 71bd54b1) -----------------------------------------------
$iDir = New-TestDir
$incSession = '71bd54b1-0000-4000-8000-000000000001'
$incTask    = '0ea601b2-0000-4000-8000-000000000001'
$incAgent   = 'aaaaaaaa-0000-4000-8000-0000000000aa'
$incHost    = 10756
$incPid     = 17088
$incProcs = @(
    [pscustomobject]@{
        processId = $incHost; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
        executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
        commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\Antiphon\worktrees\card-task-0ea601b2'
        creationDate = '2026-08-20T07:45:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.2
    },
    [pscustomobject]@{
        processId = $incPid; parentProcessId = $incHost; name = 'claude.exe'
        executablePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
        commandLine = 'claude.exe'; cwd = 'C:\Antiphon\worktrees\card-task-0ea601b2'
        creationDate = '2026-08-20T07:45:00Z'; workingSetSize = 500000000; cpuDeltaPercent = 92.5
    }
)
$incRunner = @(
    [pscustomobject]@{
        sessionId = $incSession; pid = $incPid; hostPid = $incHost
        status = 'Running'; startedAt = '2026-08-20T07:45:00Z'; backend = 'PtyHost'
    }
)
$incDb = [pscustomobject]@{
    sessions = @(
        [pscustomobject]@{
            id = $incSession; status = 'Running'
            startedAt = '2026-08-20T07:45:00Z'; endedAt = $null
            cwd = 'C:\Antiphon\worktrees\card-task-0ea601b2'; agentKind = 'ClaudeCode'
        }
    )
    agents = @(
        [pscustomobject]@{
            id = $incAgent; name = 'task-0ea601b2'; slug = 'task-0ea601b2'
            isPoolDelegate = $true; status = 'Running'
            persistentSessionId = $incSession
            workingDirectory = 'C:\Antiphon\worktrees\card-task-0ea601b2'
        }
    )
    tasks = @(
        [pscustomobject]@{
            id = $incTask; agentId = $incAgent; agentSessionId = $incSession
            status = 'Succeeded'; completedAt = '2026-08-20T07:55:22Z'
            workspace = 'Worktree'
            workingDirectory = 'C:\Antiphon\worktrees\card-task-0ea601b2'
            worktreePath = 'C:\Antiphon\worktrees\card-task-0ea601b2'
        }
    )
    manifests = @()
}
Save-Json (Join-Path $iDir 'processes.json') $incProcs
Save-Json (Join-Path $iDir 'runner.json') $incRunner
Save-Json (Join-Path $iDir 'db.json') $incDb
$incCalls = New-Object System.Collections.Generic.List[object]
$incShim = {
    param($Method, $Uri, $Headers, $Body)
    $incCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri }) | Out-Null
    return @{ ok = $true }
}
$inc = Invoke-Reap @{
    ProcessesJson = (Join-Path $iDir 'processes.json')
    RunnerJson    = (Join-Path $iDir 'runner.json')
    DbJson        = (Join-Path $iDir 'db.json')
    Now           = '2026-08-28T12:00:00Z'
    ReportPath    = $iDir
    HttpShim      = $incShim
}
Assert-Eq $inc.ExitCode 3 'incident dry-run exit 3'
Assert-Eq @($inc.Positives).Count 1 'incident 1 positive'
$incPos = @($inc.Positives)[0]
Assert-Eq $incPos.Class 'PoolExpired' 'incident class A PoolExpired'
Assert-Eq $incPos.KillPath 'server' 'incident kill path is server'
Assert-Eq $incPos.IdentityMethod 'I1' 'incident identity I1'
Assert-Eq $incPos.Pid 17088 'incident pid 17088'

# --- task-a503916a (warm pool, 20 minutes old) --------------------------------------------
$wDir = New-TestDir
$wSession = 'a503916a-0000-4000-8000-000000000001'
$wTask    = 'a503916a-0000-4000-8000-000000000002'
$wAgent   = 'a503916a-0000-4000-8000-0000000000aa'
$wProcs = @(
    [pscustomobject]@{
        processId = 9001; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
        executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
        commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\src\Antiphon'
        creationDate = '2026-08-28T22:00:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.2
    },
    [pscustomobject]@{
        processId = 9002; parentProcessId = 9001; name = 'claude.exe'
        executablePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
        commandLine = 'claude.exe'; cwd = 'C:\src\Antiphon'
        creationDate = '2026-08-28T22:00:00Z'; workingSetSize = 400000000; cpuDeltaPercent = 1.0
    }
)
$wRunner = @(
    [pscustomobject]@{
        sessionId = $wSession; pid = 9002; hostPid = 9001
        status = 'Running'; startedAt = '2026-08-28T22:00:00Z'; backend = 'PtyHost'
    }
)
$wDb = [pscustomobject]@{
    sessions = @([pscustomobject]@{
        id = $wSession; status = 'Running'; startedAt = '2026-08-28T22:00:00Z'
        endedAt = $null; cwd = 'C:\src\Antiphon'; agentKind = 'ClaudeCode'
    })
    agents = @([pscustomobject]@{
        id = $wAgent; name = 'task-a503916a'; slug = 'task-a503916a'
        isPoolDelegate = $true; status = 'Idle'
        persistentSessionId = $wSession; workingDirectory = 'C:\src\Antiphon'
    })
    tasks = @([pscustomobject]@{
        id = $wTask; agentId = $wAgent; agentSessionId = $wSession
        status = 'Succeeded'; completedAt = '2026-08-28T22:17:00Z'
        workspace = 'Shared'; workingDirectory = 'C:\src\Antiphon'; worktreePath = $null
    })
    manifests = @()
}
Save-Json (Join-Path $wDir 'processes.json') $wProcs
Save-Json (Join-Path $wDir 'runner.json') $wRunner
Save-Json (Join-Path $wDir 'db.json') $wDb
$w = Invoke-Reap @{
    ProcessesJson = (Join-Path $wDir 'processes.json')
    RunnerJson    = (Join-Path $wDir 'runner.json')
    DbJson        = (Join-Path $wDir 'db.json')
    Now           = '2026-08-28T22:37:00Z'
    ReportPath    = $wDir
}
Assert-Eq $w.ExitCode 0 'warm-pool task-a503916a exit 0'
Assert-Eq @($w.Positives).Count 0 'warm-pool task-a503916a not a positive (Z4 MinDoneMinutes)'
$wRow = @($w.Rows | Where-Object { $_.Pid -eq 9002 })[0]
Assert-True ($null -ne $wRow) 'warm-pool row present' 'missing pid 9002'
Assert-True ($wRow.RulesFailed -match 'Z4' -or $wRow.RulesFailed -match 'Z6') 'warm-pool fails Z4 or Z6' $wRow.RulesFailed

# --- session Failed + runner-claimed => class B, no kill even with -Execute ----------------
$bDir = New-TestDir
$bSession = 'bbbbbbbb-0000-4000-8000-000000000001'
$bProcs = @(
    [pscustomobject]@{
        processId = 8001; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
        executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
        commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\src\Antiphon'
        creationDate = '2026-08-20T07:00:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.2
    },
    [pscustomobject]@{
        processId = 8002; parentProcessId = 8001; name = 'claude.exe'
        executablePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
        commandLine = 'claude.exe'; cwd = 'C:\src\Antiphon'
        creationDate = '2026-08-20T07:00:00Z'; workingSetSize = 400000000; cpuDeltaPercent = 1.0
    }
)
$bRunner = @([pscustomobject]@{
    sessionId = $bSession; pid = 8002; hostPid = 8001
    status = 'Running'; startedAt = '2026-08-20T07:00:00Z'; backend = 'PtyHost'
})
$bDb = [pscustomobject]@{
    sessions = @([pscustomobject]@{
        id = $bSession; status = 'Failed'; startedAt = '2026-08-20T07:00:00Z'
        endedAt = '2026-08-20T08:00:00Z'; cwd = 'C:\src\Antiphon'; agentKind = 'ClaudeCode'
    })
    agents = @(); tasks = @(); manifests = @()
}
Save-Json (Join-Path $bDir 'processes.json') $bProcs
Save-Json (Join-Path $bDir 'runner.json') $bRunner
Save-Json (Join-Path $bDir 'db.json') $bDb
$bCalls = New-Object System.Collections.Generic.List[object]
$bShim = {
    param($Method, $Uri, $Headers, $Body)
    $bCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri }) | Out-Null
    return @{ ok = $true }
}
$b = Invoke-Reap @{
    ProcessesJson = (Join-Path $bDir 'processes.json')
    RunnerJson    = (Join-Path $bDir 'runner.json')
    DbJson        = (Join-Path $bDir 'db.json')
    Now           = '2026-08-28T12:00:00Z'
    ReportPath    = $bDir
    Execute       = $true
    Class         = 'EndedButAlive'
    HttpShim      = $bShim
}
$bRow = @($b.Rows | Where-Object { $_.Pid -eq 8002 })[0]
Assert-Eq $bRow.Class 'ReconcilerOwned' 'Failed+claimed is class B'
Assert-Eq @($b.Positives).Count 0 'class B is not a positive'
Assert-Eq $bCalls.Count 0 'class B no kill call even with -Execute -Class EndedButAlive'
Assert-Eq $b.KillCalls.Count 0 'class B PassThru KillCalls empty'

# --- class C quiet-gate fail / pass + taskkill path ----------------------------------------
function New-ClassCFiles {
    param([string]$Dir, [string]$TranscriptMtime)
    $sid = 'cccccccc-0000-4000-8000-000000000001'
    $hostPid = 7001
    $leaf = 7002
    $procs = @(
        [pscustomobject]@{
            processId = $hostPid; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
            executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
            commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\src\Antiphon'
            creationDate = '2026-08-20T07:00:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.2
        },
        [pscustomobject]@{
            processId = $leaf; parentProcessId = $hostPid; name = 'claude.exe'
            executablePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
            commandLine = 'claude.exe --session-id cccccccc-0000-4000-8000-000000000001'
            cwd = 'C:\src\Antiphon'
            creationDate = '2026-08-20T07:00:00Z'; workingSetSize = 400000000; cpuDeltaPercent = 1.0
        }
    )
    $runner = @()
    $db = [pscustomobject]@{
        sessions = @([pscustomobject]@{
            id = $sid; status = 'Stopped'; startedAt = '2026-08-20T07:00:00Z'
            endedAt = '2026-08-20T08:00:00Z'; cwd = 'C:\src\Antiphon'; agentKind = 'ClaudeCode'
            transcriptMtime = $TranscriptMtime; ansiMtime = $TranscriptMtime
        })
        agents = @(); tasks = @()
        manifests = @([pscustomobject]@{ sessionId = $sid; hostPid = $hostPid })
    }
    Save-Json (Join-Path $Dir 'processes.json') $procs
    Save-Json (Join-Path $Dir 'runner.json') $runner
    Save-Json (Join-Path $Dir 'db.json') $db
    return $sid
}

$c5Dir = New-TestDir
New-ClassCFiles $c5Dir '2026-08-28T11:50:00Z' | Out-Null
$c5 = Invoke-Reap @{
    ProcessesJson = (Join-Path $c5Dir 'processes.json')
    RunnerJson    = (Join-Path $c5Dir 'runner.json')
    DbJson        = (Join-Path $c5Dir 'db.json')
    Now           = '2026-08-28T12:00:00Z'
    ReportPath    = $c5Dir
}
$c5Row = @($c5.Rows | Where-Object { $_.Pid -eq 7002 })[0]
Assert-True ($c5Row.RulesFailed -match 'Z5') 'class C recent transcript fails Z5' $c5Row.RulesFailed
Assert-Eq @($c5.Positives).Count 0 'class C Z5 fail is not a positive'

$c5okDir = New-TestDir
New-ClassCFiles $c5okDir '2026-08-28T04:00:00Z' | Out-Null
$c5okCalls = New-Object System.Collections.Generic.List[object]
$c5okShim = {
    param($Method, $Uri, $Headers, $Body)
    $c5okCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri }) | Out-Null
    return @{ ok = $true }
}
$c5ok = Invoke-Reap @{
    ProcessesJson = (Join-Path $c5okDir 'processes.json')
    RunnerJson    = (Join-Path $c5okDir 'runner.json')
    DbJson        = (Join-Path $c5okDir 'db.json')
    Now           = '2026-08-28T12:00:00Z'
    ReportPath    = $c5okDir
    Execute       = $true
    Class         = 'EndedButAlive'
    HttpShim      = $c5okShim
}
Assert-Eq @($c5ok.Positives).Count 1 'class C quiet 7h is a positive'
$c5okRow = @($c5ok.Positives)[0]
Assert-Eq $c5okRow.Class 'EndedButAlive' 'class C name'
Assert-Eq $c5okRow.KillPath 'taskkill' 'class C kill path is taskkill'
Assert-Eq $c5okRow.IdentityMethod 'I2' 'class C identity is I2 (pty-host manifest)'
$c5okTaskkill = @($c5ok.KillCalls | Where-Object { $_.Method -eq 'TASKKILL' })
Assert-True ($c5okTaskkill.Count -ge 1) 'class C execute records taskkill' ('calls=' + $c5ok.KillCalls.Count)
Assert-True ($c5okTaskkill[0].Uri -match '7001') 'class C taskkill targets pty-host ancestor' $c5okTaskkill[0].Uri

# --- pid reuse Z3 --------------------------------------------------------------------------
$zDir = New-TestDir
$zSession = 'dddddddd-0000-4000-8000-000000000001'
$zProcs = @(
    [pscustomobject]@{
        processId = 6001; parentProcessId = 4; name = 'Antiphon.PtyHost.exe'
        executablePath = 'C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe'
        commandLine = 'Antiphon.PtyHost.exe'; cwd = 'C:\src\Antiphon'
        creationDate = '2026-08-20T06:00:00Z'; workingSetSize = 40000000; cpuDeltaPercent = 0.2
    },
    [pscustomobject]@{
        processId = 6002; parentProcessId = 6001; name = 'claude.exe'
        executablePath = 'C:\Users\lndco\AppData\Local\claude\claude.exe'
        commandLine = 'claude.exe'; cwd = 'C:\src\Antiphon'
        creationDate = '2026-08-20T06:00:00Z'; workingSetSize = 400000000; cpuDeltaPercent = 1.0
    }
)
$zRunner = @([pscustomobject]@{
    sessionId = $zSession; pid = 6002; hostPid = 6001
    status = 'Running'; startedAt = '2026-08-20T07:00:00Z'; backend = 'PtyHost'
})
$zDb = [pscustomobject]@{
    sessions = @([pscustomobject]@{
        id = $zSession; status = 'Running'; startedAt = '2026-08-20T07:00:00Z'
        endedAt = $null; cwd = 'C:\src\Antiphon'; agentKind = 'ClaudeCode'
    })
    agents = @([pscustomobject]@{
        id = 'dddddddd-0000-4000-8000-0000000000aa'; name = 'stale'; slug = 'stale'
        isPoolDelegate = $true; status = 'Running'
        persistentSessionId = $zSession; workingDirectory = 'C:\src\Antiphon'
    })
    tasks = @([pscustomobject]@{
        id = 'dddddddd-0000-4000-8000-0000000000bb'; agentId = 'dddddddd-0000-4000-8000-0000000000aa'
        agentSessionId = $zSession; status = 'Succeeded'; completedAt = '2026-08-20T08:00:00Z'
        workspace = 'Shared'; workingDirectory = 'C:\src\Antiphon'; worktreePath = $null
    })
    manifests = @()
}
Save-Json (Join-Path $zDir 'processes.json') $zProcs
Save-Json (Join-Path $zDir 'runner.json') $zRunner
Save-Json (Join-Path $zDir 'db.json') $zDb
$z = Invoke-Reap @{
    ProcessesJson = (Join-Path $zDir 'processes.json')
    RunnerJson    = (Join-Path $zDir 'runner.json')
    DbJson        = (Join-Path $zDir 'db.json')
    Now           = '2026-08-28T12:00:00Z'
    ReportPath    = $zDir
}
$zRow = @($z.Rows | Where-Object { $_.Pid -eq 6002 })[0]
Assert-True ($zRow.RulesFailed -match 'Z3') 'pid reuse fails Z3' $zRow.RulesFailed
Assert-Eq @($z.Positives).Count 0 'pid reuse is not a positive'

# --- runner / DB not answering => exit 2 ---------------------------------------------------
$uDir = New-TestDir
$u = Invoke-Reap @{
    ProcessesJson = (Join-Path $cDir 'processes.json')
    RunnerJson    = 'UNREACHABLE'
    DbJson        = (Join-Path $cDir 'db.json')
    Now           = $cFiles.Now
    ReportPath    = $uDir
}
Assert-Eq $u.ExitCode 2 'runner unreachable exit 2'
Assert-Eq @($u.Rows).Count 0 'runner unreachable no verdicts'

$u2Dir = New-TestDir
$u2 = Invoke-Reap @{
    ProcessesJson = (Join-Path $cDir 'processes.json')
    RunnerJson    = $cFiles.Runner
    DbJson        = 'UNREACHABLE'
    Now           = $cFiles.Now
    ReportPath    = $u2Dir
}
Assert-Eq $u2.ExitCode 2 'db unreachable exit 2'
Assert-Eq @($u2.Rows).Count 0 'db unreachable no verdicts'

function Test-MissingAppHostDatabase {
    $fixture = New-TestDir
    $dockerCalls = New-Object 'System.Collections.Generic.List[string]'
    function docker {
        $dockerCalls.Add(($args -join ' '))
        $global:LASTEXITCODE = 1
        'fixture: deliberately no desktop postgres container'
    }

    $savedAppData = $env:APPDATA
    try {
        $env:APPDATA = $fixture
        $record = @(Invoke-Reap @{
            ProcessesJson = $cFiles.Processes
            RunnerJson    = $cFiles.Runner
            Now           = $cFiles.Now
            ReportPath    = $fixture
            Execute       = $true
            HttpShim      = { throw 'The reaper must not call the runner when the database is unavailable.' }
        } *>&1)
        $result = $record[-1]
        Assert-Eq $result.ExitCode 2 'missing AppHost database exits 2'
        Assert-Eq @($result.KillCalls).Count 0 'missing AppHost database prevents kills'
        Assert-True (($record -join "`n") -match 'AppHost user secret ConnectionStrings:DefaultConnection') 'missing AppHost database names the configuration key'
        Assert-Eq $dockerCalls.Count 0 'missing AppHost database never queries the old desktop container'
    } finally {
        $env:APPDATA = $savedAppData
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
Test-MissingAppHostDatabase

function Test-DesktopPostgresRouting {
    # A native executable shim exercises the actual Docker argv/stdin boundary on Linux.
    # The missing-secret refusal above runs on Windows PowerShell as well.
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Unix) { return }

    $fixture = New-TestDir
    $savedAppData = $env:APPDATA
    $savedPath = $env:PATH
    $savedFixture = $env:C2065_FIXTURE
    try {
        $env:APPDATA = $fixture
        $env:C2065_FIXTURE = $fixture
        $env:PATH = "$fixture$([IO.Path]::PathSeparator)$savedPath"
        $secretsDir = Join-Path $fixture 'Microsoft/UserSecrets/aspire-antiphon-apphost'
        New-Item -ItemType Directory -Path $secretsDir -Force | Out-Null
        $secretsFile = Join-Path $secretsDir 'secrets.json'
        $connection = 'Host=192.0.2.55;Port=5433;Database=fixture_db;Username=fixture_user;Password="fixture-private;value"'
        Save-Json $secretsFile @{ ConnectionStrings = @{ DefaultConnection = $connection } }
        $docker = Join-Path $fixture 'docker'
        Set-Content -LiteralPath $docker -Encoding ascii -Value @'
#!/bin/sh
set -eu
printf '%s\n' "$*" >> "$C2065_FIXTURE/argv"
if [ "$1" = --context ]; then
    [ "$2" = antiphon-server2 ]
    shift 2
fi
id=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
case "$1:$2" in
    inspect:--type)
        [ "$5" = '{{.Id}}|{{.State.Running}}|{{json (index .NetworkSettings.Ports "5432/tcp")}}' ]
        [ "$6" = antiphon-server2-postgres-postgres-1 ]
        printf '%s|true|[{"HostIp":"192.0.2.55","HostPort":"5433"}]\n' "$id"
        ;;
    inspect:--format)
        [ "$4" = "$id" ]
        printf '%s|fixture-start\n' "$id"
        ;;
    exec:-i)
        sql=$(cat)
        [ "$4" = psql ]
        if [ "$3" = "$id" ]; then
            [ "$*" = "exec -i $id psql -X -A -t -w -v ON_ERROR_STOP=1 -h /var/run/postgresql -p 5432 -U fixture_user -d fixture_db -f -" ]
        else
            [ "$*" = 'exec -i fixture-local psql -X -A -t -w -v ON_ERROR_STOP=1 -U localUser -d localDb -f -' ]
        fi
        if [ -f "$C2065_FIXTURE/fail-query" ]; then
            printf '%s\n' 'fixture-private;value' >&2
            exit 2
        fi
        case "$sql" in
            *'"__EFMigrationsHistory"'*) printf '%s\n' '20260101000000_Test' ;;
            *'current_setting('*'pg_stat_statements_info'*) printf '%s\n' '160014|16.14|0||42|1|0.1|1' ;;
            *'container-test'*) printf '%s\n' 'local-ok' ;;
            *'fingerprint'*) printf '%s\n' '{"databaseOid":123}' ;;
            *'json_agg(json_build_object('*'"AgentTasks"'*) printf '%s\n' '{"sessions":[],"agents":[],"tasks":[]}' ;;
            *'"AgentSessions" where "Id" in '*) printf '%s\n' 'aaaaaaaa-0000-0000-0000-000000000001' ;;
            *) exit 9 ;;
        esac
        ;;
    "exec:$id") [ "$3" = sh ]; printf 'usage_usec 2000000\n' ;;
    *) exit 9 ;;
esac
'@
        & chmod +x $docker
        function Start-Sleep { param([int]$Seconds) if ($Seconds -ne 10) { throw 'Unexpected sleep in database fixture.' } }

        $qps = & (Join-Path $here 'pg-qps.ps1') -Seconds 10 -Json
        Assert-Eq $LASTEXITCODE 0 'pg-qps samples the production endpoint without desktop Postgres'
        Assert-True ([bool](($qps | ConvertFrom-Json).windowSeconds -gt 0)) 'pg-qps emits valid rate JSON'

        # A report fixture has a source migration and deliberately no restart or stack verifier.
        $fixtureScripts = Join-Path $fixture 'scripts'
        $migrations = Join-Path $fixture 'server/Migrations'
        New-Item -ItemType Directory -Path $fixtureScripts, $migrations | Out-Null
        Copy-Item -LiteralPath (Join-Path $here 'deploy-local.ps1'), (Join-Path $here 'apphost-common.ps1') -Destination $fixtureScripts
        New-Item -ItemType SymbolicLink -Path (Join-Path $fixtureScripts 'lib') -Target (Join-Path $here 'lib') | Out-Null
        New-Item -ItemType File -Path (Join-Path $migrations '20260101000000_Test.cs') | Out-Null
        $deploy = & (Join-Path $fixtureScripts 'deploy-local.ps1') -Report 6>$null
        Assert-True ($LASTEXITCODE -eq 0 -and $deploy -contains 'DEPLOY VERDICT: ok') 'deploy report verifies history without restarting the stack'

        . (Join-Path $here 'lib/desktop-postgres.ps1')
        $measure = Join-Path $here 'measure-session-state-cache.ps1'
        $tokens = $parseErrors = $null
        $tree = [System.Management.Automation.Language.Parser]::ParseFile($measure, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw 'Could not parse the measurement script.' }
        foreach ($definition in $tree.EndBlock.Statements) {
            if ($definition -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $definition.Name -in @('Get-StatisticsSql', 'Read-Statistics')) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }
        }
        $target = Resolve-DesktopPostgresTarget
        $stats = Read-Statistics -Target $target
        Assert-True ($stats.statistics.databaseOid -eq 123 -and $stats.containerCpuSeconds -eq 2 -and $stats.containerIdentity -like '*|fixture-start') 'cache measurement reads SQL and CPU from the same production container'
        $local = Resolve-DesktopPostgresTarget -Container 'fixture-local' -User 'localUser' -Database 'localDb'
        Assert-Eq (Invoke-DesktopPostgresQuery -Target $local -Sql 'SELECT 1; -- container-test') 'local-ok' 'explicit local container preserves user and database'

        $manifestDir = Join-Path $fixture 'pty-hosts/manifests'
        New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
        Save-Json (Join-Path $manifestDir 'sample.json') @{
            sessionId = 'aaaaaaaa-0000-0000-0000-000000000001'; hostPid = 123; childPid = 124
            hostStartTimeUtc = '2026-08-20T06:00:00Z'; createdAtUtc = '2026-08-20T06:00:00Z'
            exe = 'fixture'; cwd = $fixture; ansiLogPath = (Join-Path $fixture 'sample.ansi')
        }
        function Invoke-RestMethod {
            param($Method, $Uri, $TimeoutSec)
            if ($Method -ne 'GET' -or $Uri -ne 'http://fixture.invalid/sessions') { throw 'Unexpected runner call.' }
            @()
        }
        function Get-CimInstance { @() }
        $orphan = Join-Path $here 'reap-orphaned-pty-hosts.ps1'
        $report = & $orphan -SessionLogPath $fixture -RunnerUrl 'http://fixture.invalid' 6>&1
        Assert-True ($LASTEXITCODE -eq 0 -and ($report -join "`n") -match 'protected \(DB row\): 1') 'pty reaper protects a session found in production'

        New-Item -ItemType File -Path (Join-Path $fixture 'fail-query') | Out-Null
        $report = & $orphan -SessionLogPath $fixture -RunnerUrl 'http://fixture.invalid' -Execute 6>&1
        Assert-True ($LASTEXITCODE -eq 2 -and ($report -join "`n") -match 'ABORT: the database did not answer' -and ($report -join "`n") -notmatch 'fixture-private') 'pty reaper refuses to execute when PostgreSQL fails and suppresses stderr'
        Remove-Item -LiteralPath (Join-Path $fixture 'fail-query')

        $record = @(Invoke-Reap @{ ProcessesJson = $cFiles.Processes; RunnerJson = $cFiles.Runner; Now = $cFiles.Now; ReportPath = $fixture } *>&1)
        Assert-Eq $record[-1].ExitCode 0 'zombie reaper accepts a successful production database census'

        Save-Json $secretsFile @{ 'ConnectionStrings:DefaultConnection' = $connection.Replace('5433', '17280') }
        $qps = & (Join-Path $here 'pg-qps.ps1') -Seconds 10
        Assert-True ($LASTEXITCODE -eq 3 -and ($qps -join "`n") -match 'must publish the endpoint' -and ($qps -join "`n") -notmatch 'fixture-private') 'pg-qps refuses an AppHost endpoint that does not match server2'
        $argv = Get-Content -LiteralPath (Join-Path $fixture 'argv') -Raw
        Assert-True ($argv -notmatch 'fixture-private|\bantiphon-postgres\b') 'Docker arguments contain neither the password nor the desktop container'
    } finally {
        $env:APPDATA = $savedAppData
        $env:PATH = $savedPath
        $env:C2065_FIXTURE = $savedFixture
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
Test-DesktopPostgresRouting

# --- incident execute records server kill via HttpShim -------------------------------------
$incXDir = New-TestDir
Save-Json (Join-Path $incXDir 'processes.json') $incProcs
Save-Json (Join-Path $incXDir 'runner.json') $incRunner
Save-Json (Join-Path $incXDir 'db.json') $incDb
$incXCalls = New-Object System.Collections.Generic.List[object]
$incXShim = {
    param($Method, $Uri, $Headers, $Body)
    $incXCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri }) | Out-Null
    return @{ ok = $true }
}
$incX = Invoke-Reap @{
    ProcessesJson = (Join-Path $incXDir 'processes.json')
    RunnerJson    = (Join-Path $incXDir 'runner.json')
    DbJson        = (Join-Path $incXDir 'db.json')
    Now           = '2026-08-28T12:00:00Z'
    ReportPath    = $incXDir
    Execute       = $true
    Class         = 'PoolExpired'
    HttpShim      = $incXShim
}
Assert-Eq $incX.ExitCode 0 'incident execute exit 0'
Assert-True ($incXCalls.Count -ge 1) 'incident execute posted a kill' ('calls=' + $incXCalls.Count)
Assert-True ($incXCalls[0].Uri -match '/api/sessions/71bd54b1') 'incident execute uses server kill path' $incXCalls[0].Uri
Assert-Eq $incXCalls[0].Method 'POST' 'incident execute is POST'

Write-Host ''
Write-Host ("REAP ZOMBIE TESTS  passed={0}  failed={1}" -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    $script:failures | ForEach-Object { Write-Host ("  " + $_) }
    exit 1
}
exit 0
