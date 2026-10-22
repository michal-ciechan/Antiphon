# Runs ONE copy of update-claude.ps1 under a fully mocked process environment and writes a
# JSON record of every stop / update / launch attempt. Nothing real is touched:
#   Stop-Process, Get-CimInstance, Start-Sleep, Get-Date, claude and npm are shadowed by
#   functions in this (caller) scope, which the invoked script sees through dynamic scoping.
#   Fake PIDs are above any real Windows PID, so even an un-shadowed Stop-Process could
#   only fail with "cannot find a process".
param(
    [Parameter(Mandatory)][string]$Script,
    [Parameter(Mandatory)][string]$Record,
    [Parameter(Mandatory)][string]$LaunchRecord,
    [switch]$UpdateFails,
    [switch]$WithRemoteSession,
    # "args=<a>|<b>|..." : pwsh -File parses a value that starts with '-' (and contains ':') as a
    # parameter, so the script arguments travel behind a plain prefix and are split here.
    [string]$ScriptArgsSpec = 'args='
)
$ErrorActionPreference = 'Continue'
$ScriptArgs = @(($ScriptArgsSpec -replace '^args=', '') -split '\|' | Where-Object { $_ -ne '' })

$global:Rec = [ordered]@{ stop = @(); update = @(); npm = @(); startProcess = @(); sleeps = 0; exitCode = $null; error = $null; argv = $ScriptArgs }
$global:FakeNow = [datetime]'2026-10-23T08:00:00'
$env:UC_LAUNCH_RECORD = $LaunchRecord
if (Test-Path $LaunchRecord) { Remove-Item $LaunchRecord }

function New-FakeProc([int]$ProcId, [string]$Cmd) {
    [pscustomobject]@{ ProcessId = [uint32]$ProcId; Name = 'claude.exe'; CommandLine = $Cmd; ParentProcessId = [uint32]1 }
}
# Turn "-Switch", "-Name value" tokens into a hashtable so switches splat as switches.
$splat = @{}
for ($i = 0; $i -lt $ScriptArgs.Count; $i++) {
    $t = $ScriptArgs[$i]
    if ($t -notlike '-*') { throw "unexpected positional script argument: $t" }
    $key = $t.TrimStart('-')
    if ($i + 1 -lt $ScriptArgs.Count -and $ScriptArgs[$i + 1] -notlike '-*') { $splat[$key] = $ScriptArgs[$i + 1]; $i++ }
    else { $splat[$key] = $true }
}
$global:FakeProcs = @(
    (New-FakeProc 4000001 'c:\users\x\.local\bin\claude.exe --dangerously-skip-permissions --name "Antiphon-Orchestrator" --effort high --append-system-prompt "[bundle:orchestrator]"'),
    (New-FakeProc 4000009 '"C:\Program Files\WindowsApps\Claude_1.2.3_x64__8wekyb3d8bbwe\app\Claude.exe"')
)
if ($WithRemoteSession) {
    $global:FakeProcs += (New-FakeProc 4000005 'c:\users\x\.local\bin\claude.exe --remote-control ClaudeBot --name ClaudeBot --dangerously-skip-permissions')
}

function Get-CimInstance {
    param([Parameter(Position = 0)][string]$ClassName, [string]$Filter, [string]$Namespace)
    if ($ClassName -ne 'Win32_Process') { throw "mock Get-CimInstance: unexpected class $ClassName" }
    $global:FakeProcs
}
function Stop-Process {
    [CmdletBinding()] param([int[]]$Id, [string[]]$Name, [switch]$Force, [switch]$PassThru, $InputObject)
    $global:Rec.stop += @($Id)
}
function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) $global:Rec.sleeps++ }
function Get-Date {
    param([string]$Format)
    $global:FakeNow = $global:FakeNow.AddSeconds(45)
    if ($Format) { $global:FakeNow.ToString($Format) } else { $global:FakeNow }
}
function claude {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$a)
    $global:Rec.update += ($a -join ' ')
    if ($a[0] -eq 'update') { $global:LASTEXITCODE = $(if ($UpdateFails) { 1 } else { 0 }) }
    elseif ($a[0] -eq '--version') { '2.1.999 (Claude Code)'; $global:LASTEXITCODE = 0 }
    else { $global:LASTEXITCODE = 0 }
}
function npm {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$a)
    $global:Rec.npm += ($a -join ' ')
    $global:LASTEXITCODE = $(if ($UpdateFails) { 1 } else { 0 })
}
# The elevated launch path uses Start-Process -Verb RunAs (a UAC prompt): never let it through.
function Start-Process {
    [CmdletBinding()] param([string]$FilePath, [string[]]$ArgumentList, [string]$Verb, [string]$WorkingDirectory, [switch]$Wait, [switch]$PassThru, [switch]$NoNewWindow)
    $global:Rec.startProcess += ("{0} {1} verb={2}" -f $FilePath, ($ArgumentList -join ' '), $Verb)
}

# Never reach the elevated launch branch: Start-Process -Verb RunAs opens a real UAC prompt.
if (-not $splat.ContainsKey('NoElevate')) { throw 'refusing: every case must pass -NoElevate' }
# Refuse to run unless every dangerous name really resolves to a mock.
foreach ($n in 'Stop-Process', 'Get-CimInstance', 'Start-Sleep', 'claude', 'npm', 'Start-Process') {
    $c = Get-Command $n -ErrorAction SilentlyContinue
    if (-not $c -or $c.CommandType -ne 'Function') { throw "mock not in effect for $n (resolves to $($c.CommandType))" }
}

$global:LASTEXITCODE = 0
try {
    & $Script @splat *>&1 | ForEach-Object { "  | $_" }
    $global:Rec.exitCode = $LASTEXITCODE
} catch {
    $global:Rec.error = $_.Exception.Message
    $global:Rec.exitCode = $LASTEXITCODE
}
$global:Rec.launch = if (Test-Path $LaunchRecord) { @(Get-Content $LaunchRecord) } else { @() }
$global:Rec | ConvertTo-Json -Depth 4 | Set-Content -Path $Record -Encoding utf8
