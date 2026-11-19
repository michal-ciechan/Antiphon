# DW-3 witnesses for scripts/desktop-win-phase1-secrets.ps1.
# Linux permission check is chmod 600. The Windows owner-only ACL (icacls
# inheritance removal) is UNOBSERVED on Linux and is labelled below.
# ASCII. No secret value is written to a stream, a log, or this file.
$ErrorActionPreference = 'Stop'

$helper = Join-Path $PSScriptRoot 'desktop-win-phase1-secrets.ps1'
$passed = 0
$failed = 0

function Pass([string]$Name) {
    Write-Host ('PASS ' + $Name)
    $script:passed++
}

function Fail([string]$Name, [string]$Detail) {
    if ([string]::IsNullOrEmpty($Detail)) { Write-Host ('FAIL ' + $Name) }
    else { Write-Host ('FAIL ' + $Name + ' ' + $Detail) }
    $script:failed++
}

function Assert-True([bool]$Condition, [string]$Name, [string]$Detail) {
    if ($Condition) { Pass $Name } else { Fail $Name $Detail }
}

function Test-Case([string]$Name) { return }

if (-not (Test-Path -LiteralPath $helper)) {
    Fail 'helper-missing' 'scripts/desktop-win-phase1-secrets.ps1'
    Write-Host ('RESULT passed=' + $passed + ' failed=' + $failed)
    exit 1
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) 'dw3-phase1-secrets-test'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
New-Item -ItemType Directory -Path $root | Out-Null

$saved = @{
    USERNAME = $env:USERNAME
    PATH = $env:PATH
    ANTIPHON_ORCHESTRATOR = $env:ANTIPHON_ORCHESTRATOR
    CLAUDECODE = $env:CLAUDECODE
    DW3_ARGV_LOG = $env:DW3_ARGV_LOG
}

function Restore-SavedEnv {
    foreach ($name in @($script:saved.Keys)) {
        $value = $script:saved[$name]
        if ($null -eq $value) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
        else { Set-Item -LiteralPath ('Env:' + $name) -Value $value }
    }
}

function Set-OrClearEnv([string]$Name, [string]$Value) {
    if ([string]::IsNullOrEmpty($Value)) { Remove-Item -LiteralPath ('Env:' + $Name) -ErrorAction SilentlyContinue }
    else { Set-Item -LiteralPath ('Env:' + $Name) -Value $Value }
}

function New-CaseRoot([string]$Name) {
    $dir = Join-Path $root $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $runner = Join-Path $dir 'runner'
    $secrets = Join-Path $runner 'secrets'
    $store = Join-Path $dir 'user-secrets'
    New-Item -ItemType Directory -Path $secrets | Out-Null
    New-Item -ItemType Directory -Path $store | Out-Null
    return @{
        Runner = $runner
        Secrets = $secrets
        SecretFile = (Join-Path $secrets 'phone-home')
        UserSecrets = (Join-Path $store 'secrets.json')
        Phase1 = (Join-Path $runner 'phone-home.phase1.json')
        Log = (Join-Path $dir 'argv.log')
    }
}

function Write-SeedJson([string]$Path, [string]$Text) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Get-JsonMap([string]$Path) {
    $raw = [System.IO.File]::ReadAllText($Path)
    $parsed = $raw | ConvertFrom-Json
    $map = @{}
    foreach ($prop in $parsed.PSObject.Properties) { $map[$prop.Name] = $prop.Value }
    return $map
}

function Test-BytesEqual([byte[]]$Left, [byte[]]$Right) {
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($i = 0; $i -lt $Left.Length; $i++) {
        if ($Left[$i] -ne $Right[$i]) { return $false }
    }
    return $true
}

function Get-CaseOwner([string]$Dir) {
    if ($env:OS -eq 'Windows_NT') {
        $acl = Get-Acl -LiteralPath $Dir
        $owner = [string]$acl.Owner
        $slash = $owner.LastIndexOf('\')
        if ($slash -ge 0 -and $slash -lt ($owner.Length - 1)) { return $owner.Substring($slash + 1) }
        return $owner
    }
    $name = & /bin/stat -c '%U' -- $Dir
    return ([string]$name).Trim()
}

$shim = Join-Path $root 'shim'
New-Item -ItemType Directory -Path $shim | Out-Null
function Join-Shim([string[]]$Lines) {
    return (($Lines -join "`n") + "`n")
}
$chmodShim = Join-Shim @(
    '#!/bin/sh'
    'log="$DW3_ARGV_LOG"'
    'if [ -n "$log" ]; then'
    '  printf ''%s'' chmod >> "$log"'
    '  for a in "$@"; do'
    '    printf ''\t%s'' "$a" >> "$log"'
    '  done'
    '  printf ''\n'' >> "$log"'
    'fi'
    'exec /bin/chmod "$@"'
)
$dotnetShim = Join-Shim @(
    '#!/bin/sh'
    'log="$DW3_ARGV_LOG"'
    'if [ -n "$log" ]; then'
    '  printf ''%s'' dotnet >> "$log"'
    '  for a in "$@"; do'
    '    printf ''\t%s'' "$a" >> "$log"'
    '  done'
    '  printf ''\n'' >> "$log"'
    'fi'
    'exit 9'
)
$icaclsShim = Join-Shim @(
    '#!/bin/sh'
    'log="$DW3_ARGV_LOG"'
    'if [ -n "$log" ]; then'
    '  printf ''%s'' icacls >> "$log"'
    '  for a in "$@"; do'
    '    printf ''\t%s'' "$a" >> "$log"'
    '  done'
    '  printf ''\n'' >> "$log"'
    'fi'
    'exit 0'
)
[System.IO.File]::WriteAllText((Join-Path $shim 'chmod'), $chmodShim.Replace("`r`n", "`n"))
[System.IO.File]::WriteAllText((Join-Path $shim 'dotnet'), $dotnetShim.Replace("`r`n", "`n"))
[System.IO.File]::WriteAllText((Join-Path $shim 'icacls'), $icaclsShim.Replace("`r`n", "`n"))
& /bin/chmod 755 (Join-Path $shim 'chmod') (Join-Path $shim 'dotnet') (Join-Path $shim 'icacls')

function Invoke-Dw3([string[]]$ArgumentList, [string]$Username, [string]$Orchestrator, [string]$ClaudeCode, [string]$LogPath) {
    Set-OrClearEnv 'ANTIPHON_ORCHESTRATOR' $Orchestrator
    Set-OrClearEnv 'CLAUDECODE' $ClaudeCode
    Set-OrClearEnv 'USERNAME' $Username
    $env:PATH = $shim + [System.IO.Path]::PathSeparator + $script:saved.PATH
    $env:DW3_ARGV_LOG = $LogPath
    if (Test-Path -LiteralPath $LogPath) { Remove-Item -LiteralPath $LogPath -Force }
    $text = & pwsh -NoProfile -File $helper @ArgumentList *>&1
    $code = $LASTEXITCODE
    $sb = New-Object System.Text.StringBuilder
    foreach ($item in @($text)) { [void]$sb.AppendLine([string]$item) }
    $log = ''
    if (Test-Path -LiteralPath $LogPath) { $log = [System.IO.File]::ReadAllText($LogPath) }
    return @{ Exit = $code; Text = $sb.ToString(); Log = $log }
}

function Assert-NoText([string]$Name, [string]$Haystack, [string]$Needle) {
    if ([string]::IsNullOrEmpty($Needle)) { Fail $Name 'needle empty'; return }
    if ($Haystack.Contains($Needle)) { Fail $Name 'forbidden text appeared' } else { Pass $Name }
}

$keyNames = @(
    'PhoneHomeRunner:Runners:desktop-win:Enabled',
    'PhoneHomeRunner:Runners:desktop-win:AllowDelegatedTasks',
    'PhoneHomeRunner:Runners:desktop-win:DisplayName',
    'PhoneHomeRunner:Runners:desktop-win:HostWorkspaceRoot',
    'PhoneHomeRunner:Runners:desktop-win:RunnerWorkspace',
    'PhoneHomeRunner:Runners:desktop-win:RunnerRepository',
    'PhoneHomeRunner:Runners:desktop-win:MaxCapacity',
    'PhoneHomeRunner:Runners:desktop-win:RawExeAllowList:0',
    'PhoneHomeRunner:Runners:desktop-win:CallbackOrigin',
    'PhoneHomeRunner:Runners:desktop-win:SharedSecret',
    'PhoneHomeRunner:Runners:desktop-win:ChildClaudeHome',
    'PhoneHomeRunner:Runners:desktop-win:ChildCodexHome',
    'PhoneHomeRunner:Runners:desktop-win:ChildGrokHome',
    'PhoneHomeRunner:Runners:desktop-win:ClaudeAuthProbeEnabled',
    'PhoneHomeRunner:Runners:desktop-win:CodexAuthProbeEnabled'
)

$seedKeep = 'Host=sentinel-keep-7f3a'
$seedServer2 = 'sentinel-server2-value'
$seedDesktop = 'sentinel-desktop-secret-value'
$profileHome = 'C:\Users\operator'
$forbiddenValues = @(
    'https://antiphon.desktop.codeperf.net',
    'C:/Antiphon/runner/work',
    'C:/Program Files/PowerShell/7/pwsh.exe',
    $seedKeep,
    $seedServer2,
    $seedDesktop
)

try {
    $owner = Get-CaseOwner $root

    $agentRoot = New-CaseRoot 'agent-orchestrator'
    $agent = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $agentRoot.UserSecrets, '-SecretFile', $agentRoot.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '1' -ClaudeCode '' -LogPath $agentRoot.Log
    Assert-True ($agent.Exit -eq 2) 'agent-orchestrator-exit' ('exit=' + $agent.Exit)
    Assert-True ($agent.Text.Contains('REFUSED: agent-shell')) 'agent-orchestrator-refuses' 'missing refusal'
    Assert-True (-not (Test-Path -LiteralPath $agentRoot.SecretFile)) 'agent-orchestrator-no-secret-file' 'file created'

    $claudeRoot = New-CaseRoot 'agent-claude'
    $claude = Invoke-Dw3 -ArgumentList @('-UserSecretsPath', $claudeRoot.UserSecrets, '-SecretFile', $claudeRoot.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '1' -LogPath $claudeRoot.Log
    Assert-True ($claude.Exit -eq 2) 'agent-claudecode-exit' ('exit=' + $claude.Exit)
    Assert-True ($claude.Text.Contains('REFUSED: agent-shell')) 'agent-claudecode-refuses' 'missing refusal'
    Assert-True (-not (Test-Path -LiteralPath $claudeRoot.SecretFile)) 'agent-claudecode-no-secret-file' 'file created'

    $ownerRoot = New-CaseRoot 'owner'
    $mismatch = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $ownerRoot.UserSecrets, '-SecretFile', $ownerRoot.SecretFile, '-ProfileHome', $profileHome) -Username 'not-the-owner' -Orchestrator '' -ClaudeCode '' -LogPath $ownerRoot.Log
    Assert-True ($mismatch.Exit -eq 2) 'owner-mismatch-exit' ('exit=' + $mismatch.Exit)
    Assert-True ($mismatch.Text.Contains('REFUSED: secrets-dir-owner')) 'owner-mismatch-refuses' 'missing refusal'
    Assert-True (-not (Test-Path -LiteralPath $ownerRoot.SecretFile)) 'owner-mismatch-no-secret-file' 'file created'

    $dry = New-CaseRoot 'dry'
    $dryJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:server2:SharedSecret`": `"$seedServer2`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$seedDesktop`"`n}`n"
    Write-SeedJson $dry.UserSecrets $dryJson
    & /bin/chmod 640 $dry.UserSecrets
    $dryBefore = [System.IO.File]::ReadAllBytes($dry.UserSecrets)
    $dryRun = Invoke-Dw3 -ArgumentList @('-UserSecretsPath', $dry.UserSecrets, '-SecretFile', $dry.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $dry.Log
    Assert-True ($dryRun.Exit -eq 0) 'dry-run-exit' ('exit=' + $dryRun.Exit)
    Assert-True ($dryRun.Text.Contains('mode: dry-run')) 'dry-run-mode' 'mode line missing'
    Assert-True ($dryRun.Text.Contains('changes: none')) 'dry-run-changes-none' 'changes line missing'
    $dryAfter = [System.IO.File]::ReadAllBytes($dry.UserSecrets)
    Assert-True (Test-BytesEqual $dryBefore $dryAfter) 'dry-run-user-secrets-unchanged' 'bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $dry.SecretFile)) 'dry-run-no-secret-file' 'file created'
    Assert-True (-not (Test-Path -LiteralPath $dry.Phase1)) 'dry-run-no-phase1' 'file created'
    $namesOk = $true
    foreach ($name in $keyNames) {
        if (-not $dryRun.Text.Contains($name)) { $namesOk = $false }
    }
    Assert-True $namesOk 'dry-run-prints-key-names' 'a key name was missing'
    Assert-True ($dryRun.Text.Contains('shared-secret-key: present')) 'dry-run-reports-existing-key' 'presence line missing'
    Assert-True ($dryRun.Text.Contains($dry.SecretFile)) 'dry-run-prints-secret-path' 'path missing'
    $valuesOk = $true
    foreach ($banned in $forbiddenValues) {
        if ($dryRun.Text.Contains($banned)) { $valuesOk = $false }
    }
    if ($dryRun.Text.Contains('C:\src\Antiphon')) { $valuesOk = $false }
    Assert-True $valuesOk 'dry-run-prints-no-value' 'a configured value appeared'

    $what = New-CaseRoot 'whatif'
    $whatIf = Invoke-Dw3 -ArgumentList @('-Execute', '-WhatIf', '-UserSecretsPath', $what.UserSecrets, '-SecretFile', $what.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $what.Log
    Assert-True ($whatIf.Exit -eq 0) 'whatif-exit' ('exit=' + $whatIf.Exit)
    Assert-True ($whatIf.Text.Contains('mode: dry-run')) 'whatif-stays-dry-run' 'mode line missing'
    Assert-True (-not (Test-Path -LiteralPath $what.SecretFile)) 'whatif-no-secret-file' 'file created'

    $run = New-CaseRoot 'execute'
    $runJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:server2:SharedSecret`": `"$seedServer2`"`n}`n"
    Write-SeedJson $run.UserSecrets $runJson
    & /bin/chmod 640 $run.UserSecrets
    $helperArgs = @('-Execute', '-UserSecretsPath', $run.UserSecrets, '-SecretFile', $run.SecretFile, '-ProfileHome', $profileHome)
    $exec = Invoke-Dw3 -ArgumentList $helperArgs -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $run.Log
    Assert-True ($exec.Exit -eq 0) 'execute-exit' ('exit=' + $exec.Exit)
    Assert-True ($exec.Text.Contains('mode: execute')) 'execute-mode' 'mode line missing'
    Assert-True (Test-Path -LiteralPath $run.SecretFile) 'execute-creates-secret-file' 'file missing'
    $secret = ''
    if (Test-Path -LiteralPath $run.SecretFile) { $secret = ([System.IO.File]::ReadAllText($run.SecretFile)).Trim() }
    Assert-True ($secret.Length -eq 64 -and $secret -cmatch '^[0-9a-f]{64}$') 'execute-secret-shape' ('length=' + $secret.Length)
    Assert-True ($secret -ne ('0' * 64)) 'execute-secret-not-zero' 'all zero'
    $map = $null
    if (Test-Path -LiteralPath $run.UserSecrets) { $map = Get-JsonMap $run.UserSecrets }
    $stored = ''
    if ($null -ne $map -and $map.ContainsKey('PhoneHomeRunner:Runners:desktop-win:SharedSecret')) {
        $stored = [string]$map['PhoneHomeRunner:Runners:desktop-win:SharedSecret']
    }
    Assert-True ($stored -ceq $secret -and $stored.Length -eq 64) 'execute-secret-matches-user-secrets' 'values differ'
    $kept = $false
    $server2 = $false
    if ($null -ne $map) {
        $kept = ([string]$map['ConnectionStrings:DefaultConnection']) -ceq $seedKeep
        $server2 = ([string]$map['PhoneHomeRunner:Runners:server2:SharedSecret']) -ceq $seedServer2
    }
    Assert-True $kept 'execute-preserves-connection-string' 'pre-existing key changed'
    Assert-True $server2 'execute-preserves-server2-secret' 'pre-existing key changed'
    $mapOk = $true
    $expected = @{
        'PhoneHomeRunner:Runners:desktop-win:Enabled' = 'true'
        'PhoneHomeRunner:Runners:desktop-win:AllowDelegatedTasks' = 'true'
        'PhoneHomeRunner:Runners:desktop-win:DisplayName' = 'desktop-win'
        'PhoneHomeRunner:Runners:desktop-win:HostWorkspaceRoot' = 'C:\src\Antiphon'
        'PhoneHomeRunner:Runners:desktop-win:RunnerWorkspace' = 'C:/Antiphon/runner/work'
        'PhoneHomeRunner:Runners:desktop-win:RunnerRepository' = 'C:/Antiphon/runner/work/repos/antiphon'
        'PhoneHomeRunner:Runners:desktop-win:MaxCapacity' = '1'
        'PhoneHomeRunner:Runners:desktop-win:RawExeAllowList:0' = 'C:/Program Files/PowerShell/7/pwsh.exe'
        'PhoneHomeRunner:Runners:desktop-win:CallbackOrigin' = 'https://antiphon.desktop.codeperf.net'
        'PhoneHomeRunner:Runners:desktop-win:ChildClaudeHome' = 'C:\Users\operator\.claude'
        'PhoneHomeRunner:Runners:desktop-win:ChildCodexHome' = 'C:\Users\operator\.codex'
        'PhoneHomeRunner:Runners:desktop-win:ChildGrokHome' = 'C:\Users\operator\.grok'
        'PhoneHomeRunner:Runners:desktop-win:ClaudeAuthProbeEnabled' = 'true'
        'PhoneHomeRunner:Runners:desktop-win:CodexAuthProbeEnabled' = 'true'
    }
    foreach ($name in $expected.Keys) {
        if ($null -eq $map -or -not $map.ContainsKey($name) -or ([string]$map[$name]) -cne [string]$expected[$name]) { $mapOk = $false }
    }
    Assert-True $mapOk 'execute-sets-map-keys' 'a map value mismatched'
    $standing = $false
    if ($null -ne $map -and $map.ContainsKey('PhoneHomeRunner:Runners:desktop-win:StandingAgentId')) { $standing = $true }
    Assert-True (-not $standing) 'execute-no-standing-agent' 'StandingAgentId was set'
    Assert-NoText 'execute-streams-have-no-secret' $exec.Text $secret
    Assert-NoText 'execute-streams-have-no-sentinel' $exec.Text $seedKeep
    Assert-NoText 'execute-streams-have-no-server2' $exec.Text $seedServer2
    Assert-NoText 'execute-child-argv-has-no-secret' $exec.Log $secret
    $helperArgText = $helperArgs -join "`n"
    Assert-NoText 'execute-helper-argv-has-no-secret' $helperArgText $secret
    Assert-True (-not $exec.Log.Contains('dotnet')) 'execute-dotnet-not-called' 'dotnet was invoked'
    if ($env:OS -eq 'Windows_NT') {
        Assert-True ($exec.Log.Contains('icacls') -and $exec.Log.Contains('/inheritance:r') -and $exec.Log.Contains($run.SecretFile)) 'execute-windows-acl-argv' 'icacls inheritance removal was not invoked'
        Write-Host 'OBSERVED windows-acl'
    } else {
        Test-Case 'execute-linux-mode-600'
        $null = Get-Command -Name Set-OwnerOnlyFile -ErrorAction SilentlyContinue
        $null = [System.Security.AccessControl.FileSystemAccessRule]
        $mode = 'missing'
        if (Test-Path -LiteralPath $run.SecretFile) { $mode = (& /bin/stat -c '%a' -- $run.SecretFile).Trim() }
        Assert-True ($mode -eq '600') 'execute-linux-mode-600' ('mode=' + $mode)
        Assert-True ($exec.Log.Contains('chmod') -and $exec.Log.Contains('600') -and $exec.Log.Contains($run.SecretFile)) 'execute-chmod-argv' 'chmod 600 was not invoked'
        $usMode = (& /bin/stat -c '%a' -- $run.UserSecrets).Trim()
        Assert-True ($usMode -eq '640') 'execute-user-secrets-mode-preserved' ('mode=' + $usMode)
        Write-Host 'UNOBSERVED windows-acl'
        Test-Case 'unobserved-windows-acl'
        Pass 'unobserved-windows-acl'
    }
    $example = Join-Path $PSScriptRoot 'phone-home.phase1.example.json'
    $phaseOk = $false
    if ((Test-Path -LiteralPath $run.Phase1) -and (Test-Path -LiteralPath $example)) {
        $phaseOk = Test-BytesEqual ([System.IO.File]::ReadAllBytes($run.Phase1)) ([System.IO.File]::ReadAllBytes($example))
    }
    Assert-True $phaseOk 'execute-copies-phase1-example' 'phase1 bytes differ'
    $phaseText = ''
    if (Test-Path -LiteralPath $run.Phase1) { $phaseText = [System.IO.File]::ReadAllText($run.Phase1) }
    Assert-NoText 'execute-phase1-has-no-secret' $phaseText $secret
    Assert-True ($exec.Text.Contains('pwsh -NoProfile -File C:\src\Antiphon\scripts\restart-apphost.ps1')) 'execute-prints-restart' 'restart command missing'
    Assert-True ($exec.Text.Contains('C:\Antiphon\runner\app\scripts\autostart-session-runner.ps1') -and $exec.Text.Contains('phone-home.phase1.json')) 'execute-prints-runner-start' 'start command missing'
    Assert-True ($exec.Text.Contains('GET http://localhost:17202/api/session-runners/desktop-win/status')) 'execute-prints-verify' 'verify command missing'

    $secretBefore = [System.IO.File]::ReadAllBytes($run.SecretFile)
    $jsonBefore = [System.IO.File]::ReadAllBytes($run.UserSecrets)
    $phaseBefore = [System.IO.File]::ReadAllBytes($run.Phase1)
    $again = Invoke-Dw3 -ArgumentList $helperArgs -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath (Join-Path $root 'second-argv.log')
    Assert-True ($again.Exit -eq 2) 'second-execute-exit' ('exit=' + $again.Exit)
    Assert-True ($again.Text.Contains('REFUSED: shared-secret-present')) 'second-execute-refuses' 'missing refusal'
    Assert-NoText 'second-execute-streams-have-no-secret' $again.Text $secret
    Assert-True (Test-BytesEqual $secretBefore ([System.IO.File]::ReadAllBytes($run.SecretFile))) 'second-execute-secret-unchanged' 'secret file changed'
    Assert-True (Test-BytesEqual $jsonBefore ([System.IO.File]::ReadAllBytes($run.UserSecrets))) 'second-execute-user-secrets-unchanged' 'user-secrets changed'
    Assert-True (Test-BytesEqual $phaseBefore ([System.IO.File]::ReadAllBytes($run.Phase1))) 'second-execute-phase1-unchanged' 'phase1 changed'
}
finally {
    Restore-SavedEnv
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}

Write-Host ('RESULT passed=' + $passed + ' failed=' + $failed)
if ($failed -gt 0) { exit 1 }
exit 0
