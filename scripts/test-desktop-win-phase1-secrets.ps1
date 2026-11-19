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
    DW3_CHMOD_FAIL = $env:DW3_CHMOD_FAIL
    DW3_FAIL_STEP = $env:DW3_FAIL_STEP
    DW3_MODE_AT_WRITE = $env:DW3_MODE_AT_WRITE
    DW3_REPLACE_FAULT = $env:DW3_REPLACE_FAULT
    DW3_RESTORE_FAULT = $env:DW3_RESTORE_FAULT
    DW3_OWNER_FAIL = $env:DW3_OWNER_FAIL
    DW3_DELETE_FAULT = $env:DW3_DELETE_FAULT
    DW3_CLEAR_PROBE = $env:DW3_CLEAR_PROBE
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

function Assert-NoStray([string]$Dir, [string]$Name) {
    $hit = $false
    if (Test-Path -LiteralPath $Dir) {
        $files = @(Get-ChildItem -LiteralPath $Dir -Recurse -Force -File -ErrorAction SilentlyContinue)
        foreach ($file in $files) {
            $n = $file.Name.ToLowerInvariant()
            if ($n.EndsWith('.dw3part') -or $n.EndsWith('.dw3bak') -or $n.EndsWith('.bak') -or $n.EndsWith('.tmp')) { $hit = $true }
        }
    }
    Assert-True (-not $hit) $Name 'stray temp or backup remains'
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
    'if [ -n "$DW3_CHMOD_FAIL" ]; then'
    '  exit 1'
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

function Invoke-Dw3 {
    param(
        [string[]]$ArgumentList,
        [string]$Username,
        [string]$Orchestrator,
        [string]$ClaudeCode,
        [string]$LogPath,
        [string]$ChmodFail,
        [string]$FailStep,
        [string]$ModeProbe,
        [string]$ReplaceFault,
        [string]$RestoreFault,
        [string]$OwnerFail,
        [string]$DeleteFault,
        [string]$ClearProbe
    )
    Set-OrClearEnv 'ANTIPHON_ORCHESTRATOR' $Orchestrator
    Set-OrClearEnv 'CLAUDECODE' $ClaudeCode
    Set-OrClearEnv 'USERNAME' $Username
    Set-OrClearEnv 'DW3_CHMOD_FAIL' $ChmodFail
    Set-OrClearEnv 'DW3_FAIL_STEP' $FailStep
    Set-OrClearEnv 'DW3_MODE_AT_WRITE' $ModeProbe
    Set-OrClearEnv 'DW3_REPLACE_FAULT' $ReplaceFault
    Set-OrClearEnv 'DW3_RESTORE_FAULT' $RestoreFault
    Set-OrClearEnv 'DW3_OWNER_FAIL' $OwnerFail
    Set-OrClearEnv 'DW3_DELETE_FAULT' $DeleteFault
    Set-OrClearEnv 'DW3_CLEAR_PROBE' $ClearProbe
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
    Assert-True ($dryRun.Text.Contains('secret-file-matches-map: no')) 'dry-run-match-absent-file' 'match line missing'

    $what = New-CaseRoot 'whatif'
    $whatIf = Invoke-Dw3 -ArgumentList @('-Execute', '-WhatIf', '-UserSecretsPath', $what.UserSecrets, '-SecretFile', $what.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $what.Log
    Assert-True ($whatIf.Exit -eq 0) 'whatif-exit' ('exit=' + $whatIf.Exit)
    Assert-True ($whatIf.Text.Contains('mode: dry-run')) 'whatif-stays-dry-run' 'mode line missing'
    Assert-True ($whatIf.Text.Contains('secret-file-matches-map: yes')) 'whatif-match-both-absent' 'match line missing'
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

    $oldDesktop = 'sentinel-old-desktop-secret'
    $oldFile = "sentinel-old-secret-file`n"
    $date = '2020-01-02T03:04:05Z'

    $lockedDir = $null
    $rotateLocked = New-CaseRoot 'rotate-unwritable'
    try {
        $lockedJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$oldDesktop`"`n}`n"
        Write-SeedJson $rotateLocked.UserSecrets $lockedJson
        Write-SeedJson $rotateLocked.SecretFile $oldFile
        $lockedJsonBefore = [System.IO.File]::ReadAllBytes($rotateLocked.UserSecrets)
        $lockedFileBefore = [System.IO.File]::ReadAllBytes($rotateLocked.SecretFile)
        $lockedDir = Split-Path -Parent $rotateLocked.UserSecrets
        & /bin/chmod 444 $rotateLocked.UserSecrets
        & /bin/chmod 555 $lockedDir
        $lockedRun = Invoke-Dw3 -ArgumentList @('-Execute', '-Rotate', '-UserSecretsPath', $rotateLocked.UserSecrets, '-SecretFile', $rotateLocked.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $rotateLocked.Log
        Assert-True ($lockedRun.Exit -eq 2) 'rotate-unwritable-exit' ('exit=' + $lockedRun.Exit)
        Assert-True ($lockedRun.Text.Contains('REFUSED: failed')) 'rotate-unwritable-refuses' 'missing refusal'
        Assert-True ($lockedRun.Text.Contains('changed: none')) 'rotate-unwritable-changed-none' 'changed line missing'
        Assert-True (-not $lockedRun.Text.Contains('changed: secret-file')) 'rotate-unwritable-secret-not-changed' 'secret half changed'
        Assert-True (-not $lockedRun.Text.Contains('changed: user-secrets')) 'rotate-unwritable-json-not-changed' 'user-secrets half changed'
        Assert-NoText 'rotate-unwritable-streams-have-no-sentinel' $lockedRun.Text $oldDesktop
        Assert-NoText 'rotate-unwritable-streams-have-no-file-sentinel' $lockedRun.Text 'sentinel-old-secret-file'
        Assert-True (Test-BytesEqual $lockedJsonBefore ([System.IO.File]::ReadAllBytes($rotateLocked.UserSecrets))) 'rotate-unwritable-json-unchanged' 'user-secrets bytes changed'
        Assert-True (Test-BytesEqual $lockedFileBefore ([System.IO.File]::ReadAllBytes($rotateLocked.SecretFile))) 'rotate-unwritable-secret-unchanged' 'secret file bytes changed'
        Assert-NoStray $rotateLocked.Runner 'rotate-unwritable-no-stray-runner'
        Assert-NoStray $lockedDir 'rotate-unwritable-no-stray-store'
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($lockedDir) -and (Test-Path -LiteralPath $lockedDir)) {
            & /bin/chmod 755 $lockedDir
        }
        if (Test-Path -LiteralPath $rotateLocked.UserSecrets) { & /bin/chmod 644 $rotateLocked.UserSecrets }
    }

    $rollback = New-CaseRoot 'rotate-rollback'
    $rollbackJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$oldDesktop`"`n}`n"
    Write-SeedJson $rollback.UserSecrets $rollbackJson
    Write-SeedJson $rollback.SecretFile $oldFile
    & /bin/chmod 640 $rollback.UserSecrets
    $rollbackJsonBefore = [System.IO.File]::ReadAllBytes($rollback.UserSecrets)
    $rollbackFileBefore = [System.IO.File]::ReadAllBytes($rollback.SecretFile)
    $rollbackRun = Invoke-Dw3 -ArgumentList @('-Execute', '-Rotate', '-UserSecretsPath', $rollback.UserSecrets, '-SecretFile', $rollback.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $rollback.Log -FailStep 'secret-file-commit'
    Assert-True ($rollbackRun.Exit -eq 2) 'rotate-rollback-exit' ('exit=' + $rollbackRun.Exit)
    Assert-True ($rollbackRun.Text.Contains('REFUSED: failed')) 'rotate-rollback-refuses' 'missing refusal'
    Assert-True ($rollbackRun.Text.Contains('step=secret-file-commit')) 'rotate-rollback-step' 'step line missing'
    Assert-True ($rollbackRun.Text.Contains('changed: none')) 'rotate-rollback-changed-none' 'changed line missing'
    Assert-NoText 'rotate-rollback-streams-have-no-sentinel' $rollbackRun.Text $oldDesktop
    Assert-True (Test-BytesEqual $rollbackJsonBefore ([System.IO.File]::ReadAllBytes($rollback.UserSecrets))) 'rotate-rollback-json-unchanged' 'user-secrets bytes changed'
    Assert-True (Test-BytesEqual $rollbackFileBefore ([System.IO.File]::ReadAllBytes($rollback.SecretFile))) 'rotate-rollback-secret-unchanged' 'secret file bytes changed'
    $rollbackMode = (& /bin/stat -c '%a' -- $rollback.UserSecrets).Trim()
    Assert-True ($rollbackMode -eq '640') 'rotate-rollback-json-mode' ('mode=' + $rollbackMode)
    Assert-NoStray $rollback.Runner 'rotate-rollback-no-stray-runner'
    Assert-NoStray (Split-Path -Parent $rollback.UserSecrets) 'rotate-rollback-no-stray-store'

    $tempFail = New-CaseRoot 'temp-write-fail'
    $tempJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:server2:SharedSecret`": `"$seedServer2`"`n}`n"
    Write-SeedJson $tempFail.UserSecrets $tempJson
    & /bin/chmod 640 $tempFail.UserSecrets
    $tempBefore = [System.IO.File]::ReadAllBytes($tempFail.UserSecrets)
    $tempRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $tempFail.UserSecrets, '-SecretFile', $tempFail.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $tempFail.Log -FailStep 'user-secrets-write'
    Assert-True ($tempRun.Exit -eq 2) 'temp-write-fail-exit' ('exit=' + $tempRun.Exit)
    Assert-True ($tempRun.Text.Contains('REFUSED: failed')) 'temp-write-fail-refuses' 'missing refusal'
    Assert-True ($tempRun.Text.Contains('step=user-secrets-write')) 'temp-write-fail-step' 'step line missing'
    Assert-True ($tempRun.Text.Contains('changed: none')) 'temp-write-fail-changed-none' 'changed line missing'
    Assert-True (Test-BytesEqual $tempBefore ([System.IO.File]::ReadAllBytes($tempFail.UserSecrets))) 'temp-write-fail-json-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $tempFail.SecretFile)) 'temp-write-fail-no-secret-file' 'secret file remains'
    Assert-NoStray $tempFail.Runner 'temp-write-fail-no-stray-runner'
    Assert-NoStray (Split-Path -Parent $tempFail.UserSecrets) 'temp-write-fail-no-stray-store'

    $chmodFail = New-CaseRoot 'chmod-fail'
    $chmodJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $chmodFail.UserSecrets $chmodJson
    $chmodBefore = [System.IO.File]::ReadAllBytes($chmodFail.UserSecrets)
    $chmodRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $chmodFail.UserSecrets, '-SecretFile', $chmodFail.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $chmodFail.Log -ChmodFail '1'
    Assert-True ($chmodRun.Exit -eq 2) 'chmod-fail-exit' ('exit=' + $chmodRun.Exit)
    Assert-True ($chmodRun.Text.Contains('REFUSED: failed')) 'chmod-fail-refuses' 'missing refusal'
    Assert-True ($chmodRun.Text.Contains('step=secret-acl')) 'chmod-fail-step' 'step line missing'
    Assert-True ($chmodRun.Text.Contains('changed: none')) 'chmod-fail-changed-none' 'changed line missing'
    Assert-True (-not (Test-Path -LiteralPath $chmodFail.SecretFile)) 'chmod-fail-no-secret-file' 'secret file remains'
    Assert-True (Test-BytesEqual $chmodBefore ([System.IO.File]::ReadAllBytes($chmodFail.UserSecrets))) 'chmod-fail-json-unchanged' 'user-secrets bytes changed'
    Assert-NoStray $chmodFail.Runner 'chmod-fail-no-stray-runner'
    Assert-NoStray (Split-Path -Parent $chmodFail.UserSecrets) 'chmod-fail-no-stray-store'

    $modeCase = New-CaseRoot 'mode-at-write'
    $modeJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $modeCase.UserSecrets $modeJson
    & /bin/chmod 640 $modeCase.UserSecrets
    $modeProbe = Join-Path (Split-Path -Parent $modeCase.SecretFile) 'mode-at-write.txt'
    $modeRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $modeCase.UserSecrets, '-SecretFile', $modeCase.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $modeCase.Log -ModeProbe $modeProbe
    Assert-True ($modeRun.Exit -eq 0) 'mode-at-write-exit' ('exit=' + $modeRun.Exit)
    $modeText = ''
    if (Test-Path -LiteralPath $modeProbe) { $modeText = [System.IO.File]::ReadAllText($modeProbe) }
    $modeLines = @($modeText.Split("`n") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $modeOk = ($modeLines.Count -ge 1)
    foreach ($line in $modeLines) {
        if ($line -ne 'mode=600') { $modeOk = $false }
    }
    Assert-True $modeOk 'mode-at-write-is-600' ('lines=' + $modeLines.Count)
    Assert-True ($modeText.IndexOf('644') -lt 0) 'mode-at-write-not-644' '644 was recorded'
    $modeSecret = ''
    if (Test-Path -LiteralPath $modeCase.SecretFile) { $modeSecret = ([System.IO.File]::ReadAllText($modeCase.SecretFile)).Trim() }
    Assert-NoText 'mode-at-write-probe-has-no-secret' $modeText $modeSecret
    $modeNow = 'missing'
    if (Test-Path -LiteralPath $modeCase.SecretFile) { $modeNow = (& /bin/stat -c '%a' -- $modeCase.SecretFile).Trim() }
    Assert-True ($modeNow -eq '600') 'mode-at-write-final-600' ('mode=' + $modeNow)
    Assert-NoStray $modeCase.Runner 'mode-at-write-no-stray'

    $nested = New-CaseRoot 'nested-date'
    $nestedJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"when`": `"$date`",`n  `"nested`": { `"k`": `"$date`", `"n`": 1 }`n}`n"
    Write-SeedJson $nested.UserSecrets $nestedJson
    & /bin/chmod 640 $nested.UserSecrets
    $nestedRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $nested.UserSecrets, '-SecretFile', $nested.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $nested.Log
    Assert-True ($nestedRun.Exit -eq 0) 'nested-date-exit' ('exit=' + $nestedRun.Exit)
    $nestedRaw = ''
    if (Test-Path -LiteralPath $nested.UserSecrets) { $nestedRaw = [System.IO.File]::ReadAllText($nested.UserSecrets) }
    $dateCount = 0
    $dateAt = 0
    while ($dateAt -ge 0 -and $dateAt -lt $nestedRaw.Length) {
        $foundAt = $nestedRaw.IndexOf($date, $dateAt)
        if ($foundAt -lt 0) { break }
        $dateCount++
        $dateAt = $foundAt + $date.Length
    }
    Assert-True ($dateCount -ge 2) 'nested-date-string-kept' ('count=' + $dateCount)
    Assert-True ($nestedRaw.Contains('"k": "' + $date + '"')) 'nested-date-object-kept' 'nested string missing'
    Assert-True ($nestedRaw.Contains('"n": 1')) 'nested-date-number-kept' 'nested number missing'
    Assert-True ($nestedRaw.Contains($seedKeep)) 'nested-date-preserves-connection-string' 'connection string missing'
    Assert-True ($nestedRaw.IndexOf('/Date(') -lt 0) 'nested-date-not-coerced' 'coerced date token appeared'
    $nestedSecret = ''
    if (Test-Path -LiteralPath $nested.SecretFile) { $nestedSecret = ([System.IO.File]::ReadAllText($nested.SecretFile)).Trim() }
    $nestedMap = $null
    if (Test-Path -LiteralPath $nested.UserSecrets) { $nestedMap = Get-JsonMap $nested.UserSecrets }
    $nestedStored = ''
    if ($null -ne $nestedMap -and $nestedMap.ContainsKey('PhoneHomeRunner:Runners:desktop-win:SharedSecret')) {
        $nestedStored = [string]$nestedMap['PhoneHomeRunner:Runners:desktop-win:SharedSecret']
    }
    Assert-True ($nestedSecret.Length -eq 64 -and $nestedStored -ceq $nestedSecret) 'nested-date-secret-matches' ('length=' + $nestedSecret.Length)
    Assert-NoText 'nested-date-streams-have-no-secret' $nestedRun.Text $nestedSecret
    Assert-NoText 'nested-date-streams-have-no-sentinel' $nestedRun.Text $seedKeep
    $nestedMode = (& /bin/stat -c '%a' -- $nested.UserSecrets).Trim()
    Assert-True ($nestedMode -eq '640') 'nested-date-json-mode' ('mode=' + $nestedMode)
    Assert-NoStray $nested.Runner 'nested-date-no-stray'

    $present = New-CaseRoot 'secret-file-present'
    $presentJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $present.UserSecrets $presentJson
    Write-SeedJson $present.SecretFile $oldFile
    $presentJsonBefore = [System.IO.File]::ReadAllBytes($present.UserSecrets)
    $presentFileBefore = [System.IO.File]::ReadAllBytes($present.SecretFile)
    $presentRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $present.UserSecrets, '-SecretFile', $present.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $present.Log
    Assert-True ($presentRun.Exit -eq 2) 'secret-file-present-exit' ('exit=' + $presentRun.Exit)
    Assert-True ($presentRun.Text.Contains('REFUSED: secret-file-present')) 'secret-file-present-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $presentJsonBefore ([System.IO.File]::ReadAllBytes($present.UserSecrets))) 'secret-file-present-json-unchanged' 'user-secrets bytes changed'
    Assert-True (Test-BytesEqual $presentFileBefore ([System.IO.File]::ReadAllBytes($present.SecretFile))) 'secret-file-present-file-unchanged' 'secret file bytes changed'
    Assert-NoText 'secret-file-present-streams-have-no-sentinel' $presentRun.Text 'sentinel-old-secret-file'
    Assert-NoStray $present.Runner 'secret-file-present-no-stray'

    $turned = New-CaseRoot 'rotate-success'
    $turnedJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:server2:SharedSecret`": `"$seedServer2`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$oldDesktop`"`n}`n"
    Write-SeedJson $turned.UserSecrets $turnedJson
    Write-SeedJson $turned.SecretFile $oldFile
    & /bin/chmod 640 $turned.UserSecrets
    $turnedRun = Invoke-Dw3 -ArgumentList @('-Execute', '-Rotate', '-UserSecretsPath', $turned.UserSecrets, '-SecretFile', $turned.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $turned.Log
    Assert-True ($turnedRun.Exit -eq 0) 'rotate-success-exit' ('exit=' + $turnedRun.Exit)
    $turnedSecret = ''
    if (Test-Path -LiteralPath $turned.SecretFile) { $turnedSecret = ([System.IO.File]::ReadAllText($turned.SecretFile)).Trim() }
    Assert-True ($turnedSecret.Length -eq 64 -and $turnedSecret -cmatch '^[0-9a-f]{64}$') 'rotate-success-shape' ('length=' + $turnedSecret.Length)
    Assert-True ($turnedSecret -cne $oldDesktop -and $turnedSecret -cne 'sentinel-old-secret-file') 'rotate-success-replaces-old' 'old value kept'
    $turnedMap = $null
    if (Test-Path -LiteralPath $turned.UserSecrets) { $turnedMap = Get-JsonMap $turned.UserSecrets }
    $turnedStored = ''
    $turnedKept = $false
    $turnedServer2 = $false
    if ($null -ne $turnedMap) {
        if ($turnedMap.ContainsKey('PhoneHomeRunner:Runners:desktop-win:SharedSecret')) {
            $turnedStored = [string]$turnedMap['PhoneHomeRunner:Runners:desktop-win:SharedSecret']
        }
        $turnedKept = ([string]$turnedMap['ConnectionStrings:DefaultConnection']) -ceq $seedKeep
        $turnedServer2 = ([string]$turnedMap['PhoneHomeRunner:Runners:server2:SharedSecret']) -ceq $seedServer2
    }
    Assert-True ($turnedStored -ceq $turnedSecret) 'rotate-success-halves-match' 'values differ'
    Assert-True $turnedKept 'rotate-success-preserves-connection-string' 'pre-existing key changed'
    Assert-True $turnedServer2 'rotate-success-preserves-server2' 'pre-existing key changed'
    Assert-NoText 'rotate-success-streams-have-no-secret' $turnedRun.Text $turnedSecret
    Assert-NoText 'rotate-success-streams-have-no-old' $turnedRun.Text $oldDesktop
    Assert-NoText 'rotate-success-argv-has-no-secret' $turnedRun.Log $turnedSecret
    $turnedMode = (& /bin/stat -c '%a' -- $turned.SecretFile).Trim()
    $turnedJsonMode = (& /bin/stat -c '%a' -- $turned.UserSecrets).Trim()
    Assert-True ($turnedMode -eq '600') 'rotate-success-secret-mode' ('mode=' + $turnedMode)
    Assert-True ($turnedJsonMode -eq '640') 'rotate-success-json-mode' ('mode=' + $turnedJsonMode)
    Assert-NoStray $turned.Runner 'rotate-success-no-stray'

    $matchYes = New-CaseRoot 'dry-match-yes'
    $matchYesJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$seedDesktop`"`n}`n"
    Write-SeedJson $matchYes.UserSecrets $matchYesJson
    Write-SeedJson $matchYes.SecretFile ($seedDesktop + "`n")
    $matchYesBefore = [System.IO.File]::ReadAllBytes($matchYes.UserSecrets)
    $matchYesFileBefore = [System.IO.File]::ReadAllBytes($matchYes.SecretFile)
    $matchYesRun = Invoke-Dw3 -ArgumentList @('-UserSecretsPath', $matchYes.UserSecrets, '-SecretFile', $matchYes.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $matchYes.Log
    Assert-True ($matchYesRun.Exit -eq 0) 'dry-match-yes-exit' ('exit=' + $matchYesRun.Exit)
    Assert-True ($matchYesRun.Text.Contains('secret-file-matches-map: yes')) 'dry-match-yes-line' 'match line missing'
    Assert-True (-not $matchYesRun.Text.Contains('secret-file-matches-map: no')) 'dry-match-yes-not-no' 'match line was no'
    Assert-NoText 'dry-match-yes-no-value' $matchYesRun.Text $seedDesktop
    Assert-True (Test-BytesEqual $matchYesBefore ([System.IO.File]::ReadAllBytes($matchYes.UserSecrets))) 'dry-match-yes-json-unchanged' 'user-secrets bytes changed'
    Assert-True (Test-BytesEqual $matchYesFileBefore ([System.IO.File]::ReadAllBytes($matchYes.SecretFile))) 'dry-match-yes-file-unchanged' 'secret file bytes changed'

    $matchNo = New-CaseRoot 'dry-match-no'
    $matchNoJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$seedDesktop`"`n}`n"
    Write-SeedJson $matchNo.UserSecrets $matchNoJson
    Write-SeedJson $matchNo.SecretFile "different-sentinel-file`n"
    $matchNoBefore = [System.IO.File]::ReadAllBytes($matchNo.UserSecrets)
    $matchNoRun = Invoke-Dw3 -ArgumentList @('-UserSecretsPath', $matchNo.UserSecrets, '-SecretFile', $matchNo.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $matchNo.Log
    Assert-True ($matchNoRun.Exit -eq 0) 'dry-match-no-exit' ('exit=' + $matchNoRun.Exit)
    Assert-True ($matchNoRun.Text.Contains('secret-file-matches-map: no')) 'dry-match-no-line' 'match line missing'
    Assert-NoText 'dry-match-no-no-map-value' $matchNoRun.Text $seedDesktop
    Assert-NoText 'dry-match-no-no-file-value' $matchNoRun.Text 'different-sentinel-file'
    Assert-True (Test-BytesEqual $matchNoBefore ([System.IO.File]::ReadAllBytes($matchNo.UserSecrets))) 'dry-match-no-json-unchanged' 'user-secrets bytes changed'

    $jsonFault = New-CaseRoot 'replace-fault-json'
    $jsonFaultText = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:server2:SharedSecret`": `"$seedServer2`"`n}`n"
    Write-SeedJson $jsonFault.UserSecrets $jsonFaultText
    & /bin/chmod 640 $jsonFault.UserSecrets
    $jsonFaultBefore = [System.IO.File]::ReadAllBytes($jsonFault.UserSecrets)
    $jsonFaultRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $jsonFault.UserSecrets, '-SecretFile', $jsonFault.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $jsonFault.Log -ReplaceFault 'user-secrets'
    Assert-True ($jsonFaultRun.Exit -ne 0) 'replace-fault-json-exit' ('exit=' + $jsonFaultRun.Exit)
    Assert-True ($jsonFaultRun.Text.Contains('REFUSED: failed')) 'replace-fault-json-refuses' 'missing refusal'
    Assert-True ($jsonFaultRun.Text.Contains('step=user-secrets-commit')) 'replace-fault-json-step' 'step line missing'
    $jsonFaultPresent = Test-Path -LiteralPath $jsonFault.UserSecrets
    $jsonFaultSame = $false
    if ($jsonFaultPresent) { $jsonFaultSame = Test-BytesEqual $jsonFaultBefore ([System.IO.File]::ReadAllBytes($jsonFault.UserSecrets)) }
    Assert-True ($jsonFaultPresent -and $jsonFaultSame) 'replace-fault-json-restored' 'secrets.json missing or bytes differ'
    $jsonFaultNone = $jsonFaultRun.Text.Contains('changed: none')
    Assert-True ($jsonFaultNone -and $jsonFaultSame -and -not (Test-Path -LiteralPath $jsonFault.SecretFile)) 'replace-fault-json-truthful' 'changed: none while a target differs'
    Assert-True (-not $jsonFaultRun.Text.Contains('differs:')) 'replace-fault-json-no-differs' 'differs line remains'
    Assert-True (-not (Test-Path -LiteralPath $jsonFault.SecretFile)) 'replace-fault-json-no-secret-file' 'secret file remains'
    Assert-NoText 'replace-fault-json-no-sentinel' $jsonFaultRun.Text $seedKeep
    Assert-NoText 'replace-fault-json-no-server2' $jsonFaultRun.Text $seedServer2
    Assert-NoStray $jsonFault.Runner 'replace-fault-json-no-stray-runner'
    Assert-NoStray (Split-Path -Parent $jsonFault.UserSecrets) 'replace-fault-json-no-stray-store'

    $secretFault = New-CaseRoot 'replace-fault-secret'
    $secretFaultJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner:Runners:desktop-win:SharedSecret`": `"$oldDesktop`"`n}`n"
    Write-SeedJson $secretFault.UserSecrets $secretFaultJson
    Write-SeedJson $secretFault.SecretFile $oldFile
    & /bin/chmod 640 $secretFault.UserSecrets
    $secretFaultJsonBefore = [System.IO.File]::ReadAllBytes($secretFault.UserSecrets)
    $secretFaultFileBefore = [System.IO.File]::ReadAllBytes($secretFault.SecretFile)
    $secretFaultRun = Invoke-Dw3 -ArgumentList @('-Execute', '-Rotate', '-UserSecretsPath', $secretFault.UserSecrets, '-SecretFile', $secretFault.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $secretFault.Log -ReplaceFault 'secret-file'
    Assert-True ($secretFaultRun.Exit -ne 0) 'replace-fault-secret-exit' ('exit=' + $secretFaultRun.Exit)
    Assert-True ($secretFaultRun.Text.Contains('REFUSED: failed')) 'replace-fault-secret-refuses' 'missing refusal'
    Assert-True ($secretFaultRun.Text.Contains('step=secret-file-commit')) 'replace-fault-secret-step' 'step line missing'
    $secretFaultJsonPresent = Test-Path -LiteralPath $secretFault.UserSecrets
    $secretFaultFilePresent = Test-Path -LiteralPath $secretFault.SecretFile
    $secretFaultJsonSame = $false
    $secretFaultFileSame = $false
    if ($secretFaultJsonPresent) { $secretFaultJsonSame = Test-BytesEqual $secretFaultJsonBefore ([System.IO.File]::ReadAllBytes($secretFault.UserSecrets)) }
    if ($secretFaultFilePresent) { $secretFaultFileSame = Test-BytesEqual $secretFaultFileBefore ([System.IO.File]::ReadAllBytes($secretFault.SecretFile)) }
    Assert-True ($secretFaultJsonPresent -and $secretFaultJsonSame) 'replace-fault-secret-json-restored' 'secrets.json missing or bytes differ'
    Assert-True ($secretFaultFilePresent -and $secretFaultFileSame) 'replace-fault-secret-file-restored' 'secret file missing or bytes differ'
    Assert-True ($secretFaultRun.Text.Contains('changed: none') -and $secretFaultJsonSame -and $secretFaultFileSame) 'replace-fault-secret-truthful' 'changed: none while a target differs'
    Assert-True (-not $secretFaultRun.Text.Contains('differs:')) 'replace-fault-secret-no-differs' 'differs line remains'
    Assert-NoText 'replace-fault-secret-no-sentinel' $secretFaultRun.Text $oldDesktop
    Assert-NoText 'replace-fault-secret-no-file-sentinel' $secretFaultRun.Text 'sentinel-old-secret-file'
    Assert-NoStray $secretFault.Runner 'replace-fault-secret-no-stray-runner'
    Assert-NoStray (Split-Path -Parent $secretFault.UserSecrets) 'replace-fault-secret-no-stray-store'

    $restoreFault = New-CaseRoot 'replace-restore-fault'
    $restoreFaultJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $restoreFault.UserSecrets $restoreFaultJson
    & /bin/chmod 640 $restoreFault.UserSecrets
    $restoreFaultBefore = [System.IO.File]::ReadAllBytes($restoreFault.UserSecrets)
    $restoreFaultRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $restoreFault.UserSecrets, '-SecretFile', $restoreFault.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $restoreFault.Log -ReplaceFault 'user-secrets' -RestoreFault 'user-secrets'
    Assert-True ($restoreFaultRun.Exit -ne 0) 'replace-restore-fault-exit' ('exit=' + $restoreFaultRun.Exit)
    Assert-True ($restoreFaultRun.Text.Contains('REFUSED: failed')) 'replace-restore-fault-refuses' 'missing refusal'
    Assert-True ($restoreFaultRun.Text.Contains('differs: user-secrets')) 'replace-restore-fault-names-half' 'differs line missing'
    Assert-True (-not $restoreFaultRun.Text.Contains('changed: none')) 'replace-restore-fault-not-none' 'changed: none while the half differs'
    Assert-True (-not $restoreFaultRun.Text.Contains('differs: secret-file')) 'replace-restore-fault-secret-matches' 'secret half differs'
    Assert-NoText 'replace-restore-fault-no-sentinel' $restoreFaultRun.Text $seedKeep
    Assert-True (-not (Test-Path -LiteralPath $restoreFault.SecretFile)) 'replace-restore-fault-no-secret-file' 'secret file remains'
    Assert-NoStray $restoreFault.Runner 'replace-restore-fault-no-stray-runner'
    $restoreStore = Split-Path -Parent $restoreFault.UserSecrets
    $restoreResidue = @()
    if (Test-Path -LiteralPath $restoreStore) {
        $restoreFiles = @(Get-ChildItem -LiteralPath $restoreStore -Force -File -ErrorAction SilentlyContinue)
        foreach ($restoreFile in $restoreFiles) {
            $restoreName = $restoreFile.Name.ToLowerInvariant()
            if ($restoreName.EndsWith('.dw3part') -or $restoreName.EndsWith('.dw3bak')) { $restoreResidue += $restoreFile.FullName }
        }
    }
    $restorePart = $false
    $restoreBak = $false
    $restoreNamed = $true
    $restoreBakBytes = $false
    $restoreBakMode = $false
    foreach ($restorePath in $restoreResidue) {
        if (-not $restoreFaultRun.Text.Contains('kept: ' + $restorePath)) { $restoreNamed = $false }
        $restoreFileName = [System.IO.Path]::GetFileName($restorePath).ToLowerInvariant()
        if ($restoreFileName.EndsWith('.dw3part')) { $restorePart = $true }
        if ($restoreFileName.EndsWith('.dw3bak')) {
            $restoreBak = $true
            $restoreBakBytes = Test-BytesEqual $restoreFaultBefore ([System.IO.File]::ReadAllBytes($restorePath))
            $restoreBakMode = ((& /bin/stat -c '%a' -- $restorePath).Trim() -eq '600')
        }
    }
    Assert-True ($restorePart -and $restoreBak) 'replace-restore-fault-keeps-temp-and-backup' 'temp or backup was deleted'
    Assert-True ($restoreNamed -and $restoreResidue.Count -ge 1) 'replace-restore-fault-names-kept' 'kept line missing'
    Assert-True $restoreBakBytes 'replace-restore-fault-backup-bytes' 'backup bytes differ'
    Assert-True $restoreBakMode 'replace-restore-fault-backup-mode' 'backup mode is not 600'
    Assert-True (-not (Test-Path -LiteralPath $restoreFault.UserSecrets)) 'replace-restore-fault-target-absent' 'secrets.json was restored'

    $badEscape = New-CaseRoot 'bad-escape'
    $badEscapeText = "{`"note`": `"bad\Nescape`", `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"}`n"
    Write-SeedJson $badEscape.UserSecrets $badEscapeText
    $badEscapeBefore = [System.IO.File]::ReadAllBytes($badEscape.UserSecrets)
    $badEscapeRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $badEscape.UserSecrets, '-SecretFile', $badEscape.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $badEscape.Log
    Assert-True ($badEscapeRun.Exit -eq 2) 'bad-escape-exit' ('exit=' + $badEscapeRun.Exit)
    Assert-True ($badEscapeRun.Text.Contains('REFUSED: user-secrets-malformed')) 'bad-escape-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $badEscapeBefore ([System.IO.File]::ReadAllBytes($badEscape.UserSecrets))) 'bad-escape-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $badEscape.SecretFile)) 'bad-escape-no-secret-file' 'secret file created'
    Assert-NoText 'bad-escape-no-sentinel' $badEscapeRun.Text $seedKeep
    Assert-NoStray $badEscape.Runner 'bad-escape-no-stray'

    $goodEscape = New-CaseRoot 'good-escape'
    $goodEscapeText = "{`"note`": `"a\nb`", `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"}`n"
    Write-SeedJson $goodEscape.UserSecrets $goodEscapeText
    $goodEscapeRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $goodEscape.UserSecrets, '-SecretFile', $goodEscape.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $goodEscape.Log
    Assert-True ($goodEscapeRun.Exit -eq 0) 'good-escape-exit' ('exit=' + $goodEscapeRun.Exit)
    $goodEscapeRaw = ''
    if (Test-Path -LiteralPath $goodEscape.UserSecrets) { $goodEscapeRaw = [System.IO.File]::ReadAllText($goodEscape.UserSecrets) }
    Assert-True ($goodEscapeRaw.Contains('a\nb')) 'good-escape-keeps-newline' 'newline escape was lost'
    Assert-NoText 'good-escape-no-sentinel' $goodEscapeRun.Text $seedKeep
    Assert-NoStray $goodEscape.Runner 'good-escape-no-stray'

    $bad = New-CaseRoot 'malformed-json'
    $badText = "{not-json`n"
    Write-SeedJson $bad.UserSecrets $badText
    $badBefore = [System.IO.File]::ReadAllBytes($bad.UserSecrets)
    $badRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $bad.UserSecrets, '-SecretFile', $bad.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $bad.Log
    Assert-True ($badRun.Exit -eq 2) 'malformed-json-exit' ('exit=' + $badRun.Exit)
    Assert-True ($badRun.Text.Contains('REFUSED: user-secrets-malformed')) 'malformed-json-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $badBefore ([System.IO.File]::ReadAllBytes($bad.UserSecrets))) 'malformed-json-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $bad.SecretFile)) 'malformed-json-no-secret-file' 'secret file created'
    Assert-NoStray $bad.Runner 'malformed-json-no-stray'

    $arrayCase = New-CaseRoot 'non-object-array'
    Write-SeedJson $arrayCase.UserSecrets "[1]`n"
    $arrayBefore = [System.IO.File]::ReadAllBytes($arrayCase.UserSecrets)
    $arrayRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $arrayCase.UserSecrets, '-SecretFile', $arrayCase.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $arrayCase.Log
    Assert-True ($arrayRun.Exit -eq 2) 'non-object-array-exit' ('exit=' + $arrayRun.Exit)
    Assert-True ($arrayRun.Text.Contains('REFUSED: user-secrets-malformed')) 'non-object-array-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $arrayBefore ([System.IO.File]::ReadAllBytes($arrayCase.UserSecrets))) 'non-object-array-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $arrayCase.SecretFile)) 'non-object-array-no-secret-file' 'secret file created'

    $dateDoc = New-CaseRoot 'non-object-date'
    Write-SeedJson $dateDoc.UserSecrets ('"' + $date + '"' + "`n")
    $dateDocBefore = [System.IO.File]::ReadAllBytes($dateDoc.UserSecrets)
    $dateDocRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $dateDoc.UserSecrets, '-SecretFile', $dateDoc.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $dateDoc.Log
    Assert-True ($dateDocRun.Exit -eq 2) 'non-object-date-exit' ('exit=' + $dateDocRun.Exit)
    Assert-True ($dateDocRun.Text.Contains('REFUSED: user-secrets-malformed')) 'non-object-date-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $dateDocBefore ([System.IO.File]::ReadAllBytes($dateDoc.UserSecrets))) 'non-object-date-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $dateDoc.SecretFile)) 'non-object-date-no-secret-file' 'secret file created'
    Assert-NoStray $dateDoc.Runner 'non-object-date-no-stray'

    $strandedCanary = 'stranded-canary-9c2e'
    $strandedPartName = 'aa11aa11aa11aa11aa11aa11aa11aa11.dw3part'
    $strandedBakName = 'bb22bb22bb22bb22bb22bb22bb22bb22.dw3bak'
    $stranded = New-CaseRoot 'stranded-list'
    $strandedJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $stranded.UserSecrets $strandedJson
    $strandedBefore = [System.IO.File]::ReadAllBytes($stranded.UserSecrets)
    $strandedStore = Split-Path -Parent $stranded.UserSecrets
    $strandedPart = Join-Path $strandedStore $strandedPartName
    $strandedBak = Join-Path $stranded.Secrets $strandedBakName
    Write-SeedJson $strandedPart $strandedCanary
    Write-SeedJson $strandedBak $strandedCanary
    $strandedRun = Invoke-Dw3 -ArgumentList @('-UserSecretsPath', $stranded.UserSecrets, '-SecretFile', $stranded.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $stranded.Log
    Assert-True ($strandedRun.Exit -eq 0) 'stranded-list-exit' ('exit=' + $strandedRun.Exit)
    Assert-True ($strandedRun.Text.Contains('stranded: ' + $strandedPart)) 'stranded-list-names-part' 'part name missing'
    Assert-True ($strandedRun.Text.Contains('stranded: ' + $strandedBak)) 'stranded-list-names-backup' 'backup name missing'
    Assert-True ($strandedRun.Text.Contains('recovery: rename the .dw3bak back over a missing target, or re-run -Execute -Rotate')) 'stranded-list-recovery' 'recovery text missing'
    Assert-True ($strandedRun.Text.Contains('sweep: refused')) 'stranded-list-sweep-refused' 'sweep was not refused'
    Assert-NoText 'stranded-list-no-contents' $strandedRun.Text $strandedCanary
    Assert-True (Test-Path -LiteralPath $strandedPart) 'stranded-list-keeps-part' 'part was swept'
    Assert-True (Test-Path -LiteralPath $strandedBak) 'stranded-list-keeps-backup' 'backup was swept'
    Assert-True (Test-BytesEqual $strandedBefore ([System.IO.File]::ReadAllBytes($stranded.UserSecrets))) 'stranded-list-json-unchanged' 'user-secrets bytes changed'

    $strandedExec = New-CaseRoot 'stranded-execute'
    Write-SeedJson $strandedExec.UserSecrets $strandedJson
    $strandedExecStore = Split-Path -Parent $strandedExec.UserSecrets
    $strandedExecPart = Join-Path $strandedExecStore $strandedPartName
    $strandedExecBak = Join-Path $strandedExec.Secrets $strandedBakName
    Write-SeedJson $strandedExecPart $strandedCanary
    Write-SeedJson $strandedExecBak $strandedCanary
    $strandedExecRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $strandedExec.UserSecrets, '-SecretFile', $strandedExec.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $strandedExec.Log
    Assert-True ($strandedExecRun.Exit -eq 0) 'stranded-execute-exit' ('exit=' + $strandedExecRun.Exit)
    Assert-True ($strandedExecRun.Text.Contains('stranded: ' + $strandedExecPart)) 'stranded-execute-names-part' 'part name missing'
    Assert-True ($strandedExecRun.Text.Contains('sweep: refused')) 'stranded-execute-sweep-refused' 'sweep was not refused'
    Assert-NoText 'stranded-execute-no-contents' $strandedExecRun.Text $strandedCanary
    Assert-True ((Test-Path -LiteralPath $strandedExecPart) -and (Test-Path -LiteralPath $strandedExecBak)) 'stranded-execute-keeps-files' 'stranded file was swept'

    $strandedSweep = New-CaseRoot 'stranded-sweep'
    Write-SeedJson $strandedSweep.UserSecrets $strandedJson
    $strandedSweepStore = Split-Path -Parent $strandedSweep.UserSecrets
    $strandedSweepPart = Join-Path $strandedSweepStore $strandedPartName
    $strandedSweepBak = Join-Path $strandedSweep.Secrets $strandedBakName
    $strandedKeep = Join-Path $strandedSweep.Secrets 'keep.txt'
    Write-SeedJson $strandedSweepPart $strandedCanary
    Write-SeedJson $strandedSweepBak $strandedCanary
    Write-SeedJson $strandedKeep 'keep-me'
    $strandedSweepRun = Invoke-Dw3 -ArgumentList @('-SweepStranded', '-UserSecretsPath', $strandedSweep.UserSecrets, '-SecretFile', $strandedSweep.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $strandedSweep.Log
    Assert-True ($strandedSweepRun.Exit -eq 0) 'stranded-sweep-exit' ('exit=' + $strandedSweepRun.Exit)
    Assert-True ($strandedSweepRun.Text.Contains('swept: ' + $strandedSweepPart)) 'stranded-sweep-names-part' 'swept part missing'
    Assert-True ($strandedSweepRun.Text.Contains('swept: ' + $strandedSweepBak)) 'stranded-sweep-names-backup' 'swept backup missing'
    Assert-True (-not $strandedSweepRun.Text.Contains('sweep: refused')) 'stranded-sweep-not-refused' 'sweep stayed refused'
    Assert-NoText 'stranded-sweep-no-contents' $strandedSweepRun.Text $strandedCanary
    Assert-True (-not (Test-Path -LiteralPath $strandedSweepPart)) 'stranded-sweep-removes-part' 'part remains'
    Assert-True (-not (Test-Path -LiteralPath $strandedSweepBak)) 'stranded-sweep-removes-backup' 'backup remains'
    Assert-True (Test-Path -LiteralPath $strandedKeep) 'stranded-sweep-keeps-other' 'unrelated file removed'
    Assert-True (-not (Test-Path -LiteralPath $strandedSweep.SecretFile)) 'stranded-sweep-no-secret-file' 'secret file created'

    $examplePath = Join-Path $PSScriptRoot 'phone-home.phase1.example.json'
    $ownerFail = New-CaseRoot 'owner-fail-temp'
    $ownerFailJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $ownerFail.UserSecrets $ownerFailJson
    & /bin/chmod 640 $ownerFail.UserSecrets
    if (Test-Path -LiteralPath $examplePath) { [System.IO.File]::Copy($examplePath, $ownerFail.Phase1, $false) }
    $ownerFailBefore = [System.IO.File]::ReadAllBytes($ownerFail.UserSecrets)
    $ownerFailRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $ownerFail.UserSecrets, '-SecretFile', $ownerFail.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $ownerFail.Log -OwnerFail 'temp'
    Assert-True ($ownerFailRun.Exit -eq 2) 'owner-fail-temp-exit' ('exit=' + $ownerFailRun.Exit)
    Assert-True ($ownerFailRun.Text.Contains('owner-only-failed: ')) 'owner-fail-temp-names-file' 'file was not named'
    Assert-True ($ownerFailRun.Text.Contains('.dw3part')) 'owner-fail-temp-names-part' 'temp suffix missing'
    Assert-True ($ownerFailRun.Text.Contains('REFUSED: failed')) 'owner-fail-temp-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $ownerFailBefore ([System.IO.File]::ReadAllBytes($ownerFail.UserSecrets))) 'owner-fail-temp-json-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $ownerFail.SecretFile)) 'owner-fail-temp-no-secret-file' 'secret file created'
    Assert-NoText 'owner-fail-temp-no-sentinel' $ownerFailRun.Text $seedKeep

    $ownerBak = New-CaseRoot 'owner-fail-backup'
    $ownerBakJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $ownerBak.UserSecrets $ownerBakJson
    & /bin/chmod 640 $ownerBak.UserSecrets
    if (Test-Path -LiteralPath $examplePath) { [System.IO.File]::Copy($examplePath, $ownerBak.Phase1, $false) }
    $ownerBakBefore = [System.IO.File]::ReadAllBytes($ownerBak.UserSecrets)
    $ownerBakRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $ownerBak.UserSecrets, '-SecretFile', $ownerBak.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $ownerBak.Log -ReplaceFault 'user-secrets' -OwnerFail 'backup'
    Assert-True ($ownerBakRun.Exit -eq 2) 'owner-fail-backup-exit' ('exit=' + $ownerBakRun.Exit)
    Assert-True ($ownerBakRun.Text.Contains('owner-only-failed: ')) 'owner-fail-backup-names-file' 'file was not named'
    Assert-True ($ownerBakRun.Text.Contains('.dw3bak')) 'owner-fail-backup-names-backup' 'backup suffix missing'
    Assert-True (Test-BytesEqual $ownerBakBefore ([System.IO.File]::ReadAllBytes($ownerBak.UserSecrets))) 'owner-fail-backup-json-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $ownerBak.SecretFile)) 'owner-fail-backup-no-secret-file' 'secret file created'
    Assert-NoText 'owner-fail-backup-no-sentinel' $ownerBakRun.Text $seedKeep

    $deleteFault = New-CaseRoot 'delete-recheck'
    $deleteJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $deleteFault.UserSecrets $deleteJson
    & /bin/chmod 640 $deleteFault.UserSecrets
    if (Test-Path -LiteralPath $examplePath) { [System.IO.File]::Copy($examplePath, $deleteFault.Phase1, $false) }
    $deleteBefore = [System.IO.File]::ReadAllBytes($deleteFault.UserSecrets)
    $deleteRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $deleteFault.UserSecrets, '-SecretFile', $deleteFault.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $deleteFault.Log -ReplaceFault 'user-secrets' -DeleteFault '1'
    Assert-True ($deleteRun.Exit -ne 0) 'delete-recheck-exit' ('exit=' + $deleteRun.Exit)
    Assert-True ($deleteRun.Text.Contains('kept: ')) 'delete-recheck-names-kept' 'kept line missing'
    Assert-True ($deleteRun.Text.Contains('.dw3bak')) 'delete-recheck-names-backup' 'backup suffix missing'
    $deleteStore = Split-Path -Parent $deleteFault.UserSecrets
    $deleteLeft = $false
    if (Test-Path -LiteralPath $deleteStore) {
        $deleteFiles = @(Get-ChildItem -LiteralPath $deleteStore -Force -File -ErrorAction SilentlyContinue)
        foreach ($deleteFile in $deleteFiles) {
            if ($deleteFile.Name.ToLowerInvariant().EndsWith('.dw3bak')) { $deleteLeft = $true }
        }
    }
    Assert-True $deleteLeft 'delete-recheck-backup-remains' 'backup was removed'
    Assert-True (Test-BytesEqual $deleteBefore ([System.IO.File]::ReadAllBytes($deleteFault.UserSecrets))) 'delete-recheck-json-unchanged' 'user-secrets bytes changed'
    Assert-NoText 'delete-recheck-no-sentinel' $deleteRun.Text $seedKeep

    $clearCase = New-CaseRoot 'clear-bytes'
    $clearJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $clearCase.UserSecrets $clearJson
    & /bin/chmod 640 $clearCase.UserSecrets
    $clearProbe = Join-Path (Split-Path -Parent $clearCase.UserSecrets) 'clear-probe.txt'
    $clearRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $clearCase.UserSecrets, '-SecretFile', $clearCase.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $clearCase.Log -ClearProbe $clearProbe
    Assert-True ($clearRun.Exit -eq 0) 'clear-bytes-exit' ('exit=' + $clearRun.Exit)
    $clearText = ''
    if (Test-Path -LiteralPath $clearProbe) { $clearText = [System.IO.File]::ReadAllText($clearProbe) }
    Assert-True ($clearText.Contains('cleared=yes')) 'clear-bytes-cleared' 'probe missing or not cleared'
    Assert-True ($clearText -match 'held=[1-9]') 'clear-bytes-held' ('probe=' + $clearText)
    $clearSecret = ''
    if (Test-Path -LiteralPath $clearCase.SecretFile) { $clearSecret = ([System.IO.File]::ReadAllText($clearCase.SecretFile)).Trim() }
    Assert-NoText 'clear-bytes-probe-has-no-secret' $clearText $clearSecret
    Assert-NoText 'clear-bytes-streams-have-no-secret' $clearRun.Text $clearSecret
    Assert-NoText 'clear-bytes-probe-has-no-sentinel' $clearText $seedKeep

    $phaseFail = New-CaseRoot 'phase1-failure'
    $phaseFailJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`"`n}`n"
    Write-SeedJson $phaseFail.UserSecrets $phaseFailJson
    & /bin/chmod 640 $phaseFail.UserSecrets
    $phaseFailBefore = [System.IO.File]::ReadAllBytes($phaseFail.UserSecrets)
    $phaseFailRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $phaseFail.UserSecrets, '-SecretFile', $phaseFail.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $phaseFail.Log -FailStep 'user-secrets-write'
    Assert-True ($phaseFailRun.Exit -eq 2) 'phase1-failure-exit' ('exit=' + $phaseFailRun.Exit)
    Assert-True ($phaseFailRun.Text.Contains('changed: phase1-config')) 'phase1-failure-names-change' 'phase1 change missing'
    Assert-True (-not $phaseFailRun.Text.Contains('changed: none')) 'phase1-failure-not-none' 'changed: none while phase1 was created'
    $phaseFailSame = $false
    if ((Test-Path -LiteralPath $phaseFail.Phase1) -and (Test-Path -LiteralPath $examplePath)) {
        $phaseFailSame = Test-BytesEqual ([System.IO.File]::ReadAllBytes($phaseFail.Phase1)) ([System.IO.File]::ReadAllBytes($examplePath))
    }
    Assert-True $phaseFailSame 'phase1-failure-copies-example' 'phase1 bytes differ'
    Assert-True (Test-BytesEqual $phaseFailBefore ([System.IO.File]::ReadAllBytes($phaseFail.UserSecrets))) 'phase1-failure-json-unchanged' 'user-secrets bytes changed'
    Assert-True (-not (Test-Path -LiteralPath $phaseFail.SecretFile)) 'phase1-failure-no-secret-file' 'secret file created'
    Assert-NoText 'phase1-failure-no-sentinel' $phaseFailRun.Text $seedKeep

    $phaseKept = New-CaseRoot 'phase1-already'
    Write-SeedJson $phaseKept.UserSecrets $phaseFailJson
    & /bin/chmod 640 $phaseKept.UserSecrets
    if (Test-Path -LiteralPath $examplePath) { [System.IO.File]::Copy($examplePath, $phaseKept.Phase1, $false) }
    $phaseKeptRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $phaseKept.UserSecrets, '-SecretFile', $phaseKept.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $phaseKept.Log -FailStep 'user-secrets-write'
    Assert-True ($phaseKeptRun.Exit -eq 2) 'phase1-already-exit' ('exit=' + $phaseKeptRun.Exit)
    Assert-True ($phaseKeptRun.Text.Contains('changed: none')) 'phase1-already-changed-none' 'changed line missing'
    Assert-True (-not $phaseKeptRun.Text.Contains('changed: phase1-config')) 'phase1-already-not-created' 'phase1 was reported as created'

    $helperSrc = [System.IO.File]::ReadAllText($helper)
    Assert-True ($helperSrc.IndexOf('$matches') -lt 0) 'repair-variable-does-not-shadow-matches' 'automatic matches variable is assigned'

    $caseVariant = 'sentinel-case-secret'
    $caseRoot = New-CaseRoot 'case-variant'
    $caseJson = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"phonehomerunner:runners:desktop-win:sharedsecret`": `"$caseVariant`"`n}`n"
    Write-SeedJson $caseRoot.UserSecrets $caseJson
    & /bin/chmod 640 $caseRoot.UserSecrets
    $caseBefore = [System.IO.File]::ReadAllBytes($caseRoot.UserSecrets)
    $caseDry = Invoke-Dw3 -ArgumentList @('-UserSecretsPath', $caseRoot.UserSecrets, '-SecretFile', $caseRoot.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $caseRoot.Log
    Assert-True ($caseDry.Exit -eq 0) 'case-variant-dry-exit' ('exit=' + $caseDry.Exit)
    Assert-True ($caseDry.Text.Contains('shared-secret-key: ambiguous')) 'case-variant-dry-ambiguous' 'ambiguous line missing'
    Assert-NoText 'case-variant-dry-no-value' $caseDry.Text $caseVariant
    Assert-True (Test-BytesEqual $caseBefore ([System.IO.File]::ReadAllBytes($caseRoot.UserSecrets))) 'case-variant-dry-unchanged' 'user-secrets bytes changed'
    $caseRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $caseRoot.UserSecrets, '-SecretFile', $caseRoot.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath (Join-Path $root 'case-variant-exec.log')
    Assert-True ($caseRun.Exit -eq 2) 'case-variant-exit' ('exit=' + $caseRun.Exit)
    Assert-True ($caseRun.Text.Contains('REFUSED: shared-secret-ambiguous')) 'case-variant-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $caseBefore ([System.IO.File]::ReadAllBytes($caseRoot.UserSecrets))) 'case-variant-unchanged' 'duplicate key was written'
    Assert-True (-not (Test-Path -LiteralPath $caseRoot.SecretFile)) 'case-variant-no-secret-file' 'secret file created'
    Assert-NoText 'case-variant-no-value' $caseRun.Text $caseVariant
    $caseRotate = Invoke-Dw3 -ArgumentList @('-Execute', '-Rotate', '-UserSecretsPath', $caseRoot.UserSecrets, '-SecretFile', $caseRoot.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath (Join-Path $root 'case-variant-rotate.log')
    Assert-True ($caseRotate.Exit -eq 2) 'case-variant-rotate-exit' ('exit=' + $caseRotate.Exit)
    Assert-True ($caseRotate.Text.Contains('REFUSED: shared-secret-ambiguous')) 'case-variant-rotate-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $caseBefore ([System.IO.File]::ReadAllBytes($caseRoot.UserSecrets))) 'case-variant-rotate-unchanged' 'duplicate key was written'
    Assert-NoText 'case-variant-rotate-no-value' $caseRotate.Text $caseVariant

    $nestedVariant = 'sentinel-nested-secret'
    $nestedRoot = New-CaseRoot 'nested-phone-home'
    $nestedPhone = "{`n  `"ConnectionStrings:DefaultConnection`": `"$seedKeep`",`n  `"PhoneHomeRunner`": { `"Runners`": { `"desktop-win`": { `"SharedSecret`": `"$nestedVariant`" } } }`n}`n"
    Write-SeedJson $nestedRoot.UserSecrets $nestedPhone
    & /bin/chmod 640 $nestedRoot.UserSecrets
    $nestedBefore = [System.IO.File]::ReadAllBytes($nestedRoot.UserSecrets)
    $nestedPhoneRun = Invoke-Dw3 -ArgumentList @('-Execute', '-UserSecretsPath', $nestedRoot.UserSecrets, '-SecretFile', $nestedRoot.SecretFile, '-ProfileHome', $profileHome) -Username $owner -Orchestrator '' -ClaudeCode '' -LogPath $nestedRoot.Log
    Assert-True ($nestedPhoneRun.Exit -eq 2) 'nested-phone-home-exit' ('exit=' + $nestedPhoneRun.Exit)
    Assert-True ($nestedPhoneRun.Text.Contains('REFUSED: shared-secret-ambiguous')) 'nested-phone-home-refuses' 'missing refusal'
    Assert-True (Test-BytesEqual $nestedBefore ([System.IO.File]::ReadAllBytes($nestedRoot.UserSecrets))) 'nested-phone-home-unchanged' 'duplicate key was written'
    Assert-True (-not (Test-Path -LiteralPath $nestedRoot.SecretFile)) 'nested-phone-home-no-secret-file' 'secret file created'
    Assert-NoText 'nested-phone-home-no-value' $nestedPhoneRun.Text $nestedVariant
    Assert-NoText 'nested-phone-home-no-sentinel' $nestedPhoneRun.Text $seedKeep
}
finally {
    Restore-SavedEnv
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}

Write-Host ('RESULT passed=' + $passed + ' failed=' + $failed)
if ($failed -gt 0) { exit 1 }
exit 0
