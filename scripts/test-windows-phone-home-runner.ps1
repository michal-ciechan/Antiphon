#requires -Version 5.1
# CARD-1744 S4. Offline witnesses for desktop-win phone-home supervision.
# Function calls, file parses, and a copied autostart script against a stub run-daemon.
# No runner, AppHost, or desktop process is started.
# ASCII-only. Own C487 trailer; do not dot-source scripts/lib/c487-harness.ps1.
[CmdletBinding()]
param(
    [string]$Case = '',
    [string]$ResultsDirectory = '',
    [string]$RunnerAssembly
)
$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
$script:c1744NameSet = @{}
$script:c1744Banned = $false

function Assert-C1744([bool]$Condition, [string]$Name, [string]$Detail) {
    if ($Condition) {
        $script:passed++
        Write-Host ("PASS C1744G3 " + $Name)
    } else {
        $script:failed++
        if ([string]::IsNullOrEmpty($Detail)) { Write-Host ("FAIL C1744G3 " + $Name) }
        else { Write-Host ("FAIL C1744G3 " + $Name + " :: " + $Detail) }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $PSScriptRoot 'lib\windows-phone-home-env.ps1'
$startPath = Join-Path $PSScriptRoot 'start-windows-phone-home-runner.ps1'
$autostartPath = Join-Path $PSScriptRoot 'autostart-session-runner.ps1'
$restartPath = Join-Path $PSScriptRoot 'restart-apphost.ps1'
$aspirePath = Join-Path $repoRoot 'dev-aspire.ps1'
$script:helperLoaded = $false
if (Test-Path -LiteralPath $helperPath) {
    . $helperPath
    $script:helperLoaded = $true
}

function Get-ScriptAst([string]$Path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    $row = @{ ast = $ast; tokens = $tokens; errors = $errors }
    return ,$row
}

function Get-NamedCommands($Ast, [string[]]$Names) {
    $set = @{}
    foreach ($name in $Names) { $set[$name] = $true }
    $script:c1744NameSet = $set
    $found = @($Ast.FindAll({
        param($node)
        if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
        $commandName = $node.GetCommandName()
        if ([string]::IsNullOrEmpty($commandName)) { return $false }
        return $script:c1744NameSet.ContainsKey($commandName)
    }, $true))
    $box = @{ Items = $found }
    return ,$box
}

function Get-FirstCommand($Ast, [string]$Name) {
    $box = Get-NamedCommands $Ast @($Name)
    $items = @($box.Items)
    if ($items.Count -eq 0) { return $null }
    $best = $items[0]
    $bestStart = [int]$best.Extent.StartOffset
    foreach ($item in $items) {
        $start = [int]$item.Extent.StartOffset
        if ($start -lt $bestStart) {
            $best = $item
            $bestStart = $start
        }
    }
    $row = @{
        Start = $bestStart
        End = [int]$best.Extent.EndOffset
        Exempt = [bool](Test-StopOnlyExempt $best)
    }
    return ,$row
}

function Test-PureNotStopOnly($Condition) {
    $node = $Condition
    if ($node -is [System.Management.Automation.Language.PipelineAst]) {
        $elements = @($node.PipelineElements)
        if ($elements.Count -ne 1) { return $false }
        $node = $elements[0]
    }
    if ($node -is [System.Management.Automation.Language.CommandExpressionAst]) {
        $node = $node.Expression
    }
    if ($node -isnot [System.Management.Automation.Language.UnaryExpressionAst]) { return $false }
    if ([string]$node.TokenKind -ne 'Not') { return $false }
    $child = $node.Child
    if ($child -isnot [System.Management.Automation.Language.VariableExpressionAst]) { return $false }
    return ([string]$child.VariablePath.UserPath -eq 'StopOnly')
}

function Test-StopOnlyExempt($Command) {
    $node = $Command.Parent
    while ($null -ne $node) {
        if ($node -is [System.Management.Automation.Language.IfStatementAst]) {
            foreach ($clause in @($node.Clauses)) {
                $body = $clause.Item2
                if ($null -eq $body) { continue }
                $inside = ($Command.Extent.StartOffset -ge $body.Extent.StartOffset) -and ($Command.Extent.EndOffset -le $body.Extent.EndOffset)
                if ($inside -and (Test-PureNotStopOnly $clause.Item1)) { return $true }
            }
        }
        $node = $node.Parent
    }
    return $false
}

function Test-PowerShell51Ast($Parsed) {
    if (@($Parsed.errors).Count -gt 0) { return $false }
    $script:c1744Banned = $false
    [void]@($Parsed.ast.FindAll({
        param($node)
        if ($node -is [System.Management.Automation.Language.TernaryExpressionAst]) { $script:c1744Banned = $true }
        if ($node -is [System.Management.Automation.Language.PipelineChainAst]) { $script:c1744Banned = $true }
        if ($node -is [System.Management.Automation.Language.MemberExpressionAst] -and $node.NullConditional) { $script:c1744Banned = $true }
        return $false
    }, $true))
    foreach ($token in @($Parsed.tokens)) {
        $kind = [string]$token.Kind
        if ($kind -eq 'QuestionQuestion' -or $kind -eq 'QuestionQuestionEquals') { $script:c1744Banned = $true }
    }
    return -not $script:c1744Banned
}

function Test-AsciiPath([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    foreach ($byte in $bytes) {
        if ($byte -gt 127) { return $false }
    }
    return $true
}

function New-PhoneJson {
    param(
        [string]$RunnerId = 'desktop-win',
        [string]$Origin = 'https://antiphon.desktop.codeperf.net',
        $Capacity = 1,
        [string]$Root = '',
        $Port = $null,
        [switch]$IncludePort,
        [string]$ExtraName = '',
        [string]$ExtraValue = ''
    )
    $parts = New-Object System.Collections.Generic.List[string]
    [void]$parts.Add('"runnerId":"' + $RunnerId + '"')
    [void]$parts.Add('"serverOrigin":"' + $Origin + '"')
    [void]$parts.Add('"capacity":' + [string]$Capacity)
    if (-not [string]::IsNullOrEmpty($Root)) {
        [void]$parts.Add('"root":"' + $Root.Replace('\', '\\') + '"')
    }
    if ($IncludePort) { [void]$parts.Add('"port":' + [string]$Port) }
    if (-not [string]::IsNullOrEmpty($ExtraName)) {
        [void]$parts.Add('"' + $ExtraName + '":"' + $ExtraValue + '"')
    }
    return '{' + [string]::Join(',', $parts.ToArray()) + '}'
}

function Invoke-PhonePlan([string]$Text, [bool]$Explicit, [bool]$Secret, [string]$Repo) {
    $exec = [ordered]@{}
    return Resolve-SessionRunnerAutostartPlan -ConfigText $Text -ExplicitConfigPath $Explicit -SecretPresent $Secret -HomeDir 'C:\Users\operator' -PwshPath 'C:\Program Files\PowerShell\7\pwsh.exe' -Executables $exec -RepoRoot $Repo
}

function Assert-Refused([string]$Name, $Plan, [string]$Reason) {
    if (-not $script:helperLoaded) {
        Assert-C1744 $false $Name 'helper absent'
        return
    }
    $envCount = 0
    if ($null -ne $Plan -and $null -ne $Plan.env) { $envCount = @($Plan.env.Keys).Count }
    $mode = ''
    $actual = ''
    if ($null -ne $Plan) { $mode = [string]$Plan.mode; $actual = [string]$Plan.reason }
    $ok = ($mode -eq 'refused') -and ($actual -eq $Reason) -and ($envCount -eq 0)
    $detail = ''
    if (-not $ok) { $detail = 'mode=' + $mode + ' reason=' + $actual + ' env=' + [string]$envCount }
    Assert-C1744 $ok $Name $detail
}

function Invoke-EnvTable {
    $expectedKeys = @(
        'ASPNETCORE_URLS',
        'PhoneHome__Enabled',
        'PhoneHome__RunnerId',
        'PhoneHome__ServerOrigin',
        'PhoneHome__SecretPath',
        'PhoneHome__StoreIdPath',
        'PhoneHome__CapacityStatePath',
        'PhoneHome__AllowedCwd',
        'PhoneHome__RunnerRepository',
        'PhoneHome__Capacity',
        'PhoneHome__ClaudeHome',
        'PhoneHome__CodexHome',
        'PhoneHome__GrokHome',
        'PhoneHome__RawExeAllowList__0',
        'SessionRunner__SessionLogPath',
        'SessionRunner__PtyHostDir',
        'SessionRunner__Herdr__Enabled',
        'SessionRunner__HostStats__HistoryEnabled',
        'SessionRunner__HostStats__Volumes__0',
        'Serilog__LogPath',
        'PhoneHome__Executables__claude',
        'PhoneHome__Executables__grok'
    )
    $expected = @{
        'ASPNETCORE_URLS' = 'http://localhost:17214'
        'PhoneHome__Enabled' = 'true'
        'PhoneHome__RunnerId' = 'laptop'
        'PhoneHome__ServerOrigin' = 'https://antiphon.desktop.codeperf.net'
        'PhoneHome__SecretPath' = 'C:\Antiphon\runner\secrets\phone-home'
        'PhoneHome__StoreIdPath' = 'C:/Antiphon/runner/state/runner-store-id'
        'PhoneHome__CapacityStatePath' = 'C:/Antiphon/runner/state/runner-capacity'
        'PhoneHome__AllowedCwd' = 'C:/Antiphon/runner/work'
        'PhoneHome__RunnerRepository' = 'C:/Antiphon/runner/work/repos/antiphon'
        'PhoneHome__Capacity' = '2'
        'PhoneHome__ClaudeHome' = 'C:\Users\operator\.claude'
        'PhoneHome__CodexHome' = 'C:\Users\operator\.codex'
        'PhoneHome__GrokHome' = 'C:\Users\operator\.grok'
        'PhoneHome__RawExeAllowList__0' = 'C:\Program Files\PowerShell\7\pwsh.exe'
        'SessionRunner__SessionLogPath' = 'C:/Antiphon/runner/state/session-runner'
        'SessionRunner__PtyHostDir' = 'C:/Antiphon/runner/state/pty-hosts'
        'SessionRunner__Herdr__Enabled' = 'false'
        'SessionRunner__HostStats__HistoryEnabled' = 'false'
        'SessionRunner__HostStats__Volumes__0' = 'C:/Antiphon/runner/work'
        'Serilog__LogPath' = 'C:/Antiphon/runner/state/logs'
        'PhoneHome__Executables__claude' = 'C:\Tools\claude.exe'
        'PhoneHome__Executables__grok' = 'C:\Tools\grok.exe'
    }
    if (-not $script:helperLoaded) {
        Assert-C1744 $false 'env-table.keys' 'helper absent'
        Assert-C1744 $false 'env-table.values' 'helper absent'
        Assert-C1744 $false 'env-table.executables' 'helper absent'
    } else {
        $exec = [ordered]@{
            claude = 'C:\Tools\claude.exe'
            grok = 'C:\Tools\grok.exe'
        }
        $envs = New-WindowsPhoneHomeRunnerEnv -RunnerId 'laptop' -Root 'C:\Antiphon\runner' -Port 17214 -Capacity 2 -ServerOrigin 'https://antiphon.desktop.codeperf.net' -ClaudeHome 'C:\Users\operator\.claude' -CodexHome 'C:\Users\operator\.codex' -GrokHome 'C:\Users\operator\.grok' -PwshPath 'C:\Program Files\PowerShell\7\pwsh.exe' -Executables $exec
        $actualKeys = @($envs.Keys)
        $keysOk = ($actualKeys.Count -eq $expectedKeys.Count)
        if ($keysOk) {
            for ($i = 0; $i -lt $expectedKeys.Count; $i++) {
                if ($actualKeys[$i] -cne $expectedKeys[$i]) { $keysOk = $false }
            }
        }
        Assert-C1744 $keysOk 'env-table.keys' $(if ($keysOk) { '' } else { 'key order' })
        $valuesOk = $keysOk
        $badKey = ''
        if ($valuesOk) {
            foreach ($key in $expectedKeys) {
                if ([string]$envs[$key] -cne [string]$expected[$key]) { $valuesOk = $false; $badKey = $key }
            }
        }
        if ($valuesOk) {
            $phase2 = Invoke-PhonePlan (New-PhoneJson) $false $true 'C:\Antiphon\repo'
            $phase2Value = ''
            if ($null -ne $phase2 -and $null -ne $phase2.env -and $phase2.mode -eq 'phone-home') {
                $phase2Value = [string]$phase2.env['SessionRunner__HostStats__HistoryEnabled']
            }
            if ($phase2Value -cne 'false') {
                $valuesOk = $false
                $badKey = 'phase2-history'
            }
        }
        Assert-C1744 $valuesOk 'env-table.values' $badKey
        $execOk = $keysOk -and (-not ($actualKeys -contains 'PhoneHome__Executables__codex'))
        Assert-C1744 $execOk 'env-table.executables' $(if ($execOk) { '' } else { 'executable names' })
    }
    $startAst = Get-ScriptAst $startPath
    $call = Get-FirstCommand $startAst.ast 'New-WindowsPhoneHomeRunnerEnv'
    Assert-C1744 ($null -ne $call) 'env-table.start-calls-helper' $(if ($null -eq $call) { 'helper call absent' } else { '' })
    $startText = [System.IO.File]::ReadAllText($startPath)
    $inline = $startText.Contains('PhoneHome__Enabled')
    Assert-C1744 (-not $inline) 'env-table.start-no-inline-table' $(if ($inline) { 'inline table remains' } else { '' })
}

function Invoke-AutostartDirect {
    if (-not $script:helperLoaded) {
        Assert-C1744 $false 'autostart-direct.mode' 'helper absent'
        Assert-C1744 $false 'autostart-direct.env' 'helper absent'
        Assert-C1744 $false 'autostart-direct.exe-args' 'helper absent'
        return
    }
    $plan = Resolve-SessionRunnerAutostartPlan -ConfigText $null -ExplicitConfigPath $false -SecretPresent $false -HomeDir 'C:\Users\operator' -PwshPath 'C:\Program Files\PowerShell\7\pwsh.exe' -Executables ([ordered]@{}) -RepoRoot ''
    Assert-C1744 ($plan.mode -eq 'direct') 'autostart-direct.mode' ([string]$plan.mode)
    $envCount = 0
    if ($null -ne $plan.env) { $envCount = @($plan.env.Keys).Count }
    Assert-C1744 ($envCount -eq 0) 'autostart-direct.env' ([string]$envCount)
    Assert-C1744 ($plan.exeArgs -eq '--urls http://localhost:17204') 'autostart-direct.exe-args' ([string]$plan.exeArgs)
}

function Invoke-AutostartPhoneHome {
    if (-not $script:helperLoaded) {
        Assert-C1744 $false 'autostart-phone-home.runner-id' 'helper absent'
        Assert-C1744 $false 'autostart-phone-home.capacity' 'helper absent'
        Assert-C1744 $false 'autostart-phone-home.port' 'helper absent'
        Assert-C1744 $false 'autostart-phone-home.explicit-port' 'helper absent'
        Assert-C1744 $false 'autostart-phone-home.helper-source' 'helper absent'
        return
    }
    $text = New-PhoneJson
    $plan = Invoke-PhonePlan $text $false $true 'C:\Antiphon\repo'
    $runnerOk = ($plan.mode -eq 'phone-home') -and ([string]$plan.env['PhoneHome__RunnerId'] -eq 'desktop-win')
    Assert-C1744 $runnerOk 'autostart-phone-home.runner-id' ([string]$plan.mode)
    Assert-C1744 ([string]$plan.env['PhoneHome__Capacity'] -eq '1') 'autostart-phone-home.capacity' ([string]$plan.env['PhoneHome__Capacity'])
    $portOk = ([string]$plan.env['ASPNETCORE_URLS'] -eq 'http://localhost:17204') -and ($plan.exeArgs -eq '--urls http://localhost:17204')
    Assert-C1744 $portOk 'autostart-phone-home.port' ([string]$plan.exeArgs)
    $explicitText = New-PhoneJson -IncludePort -Port 17214
    $explicit = Invoke-PhonePlan $explicitText $true $true 'C:\Antiphon\repo'
    $explicitOk = ($explicit.mode -eq 'phone-home') -and ([string]$explicit.env['ASPNETCORE_URLS'] -eq 'http://localhost:17214') -and ($explicit.exeArgs -eq '--urls http://localhost:17214')
    Assert-C1744 $explicitOk 'autostart-phone-home.explicit-port' ([string]$explicit.exeArgs)
    $helperText = [System.IO.File]::ReadAllText($helperPath)
    $banned = @('Get-Content', 'ReadAll', '[IO.File]', 'Invoke-WebRequest', 'Get-Command')
    $hit = ''
    foreach ($word in $banned) {
        if ($helperText.Contains($word)) { $hit = $word }
    }
    Assert-C1744 ([string]::IsNullOrEmpty($hit)) 'autostart-phone-home.helper-source' $hit
}

function Invoke-AutostartRefusals {
    if (-not $script:helperLoaded) {
        foreach ($name in @('local','desktop','bad-id','http-origin','origin-path','capacity-0','capacity-3','port-17299','port-not-broker','unknown-key','malformed','unc-root','secret-missing')) {
            Assert-C1744 $false ("autostart-refusals." + $name) 'helper absent'
        }
        return
    }
    Assert-Refused 'autostart-refusals.local' (Invoke-PhonePlan (New-PhoneJson -RunnerId 'local') $false $true 'C:\Antiphon\repo') 'runner-id-reserved'
    Assert-Refused 'autostart-refusals.desktop' (Invoke-PhonePlan (New-PhoneJson -RunnerId 'Desktop') $false $true 'C:\Antiphon\repo') 'runner-id-reserved'
    Assert-Refused 'autostart-refusals.bad-id' (Invoke-PhonePlan (New-PhoneJson -RunnerId 'bad_id') $false $true 'C:\Antiphon\repo') 'runner-id-invalid'
    Assert-Refused 'autostart-refusals.http-origin' (Invoke-PhonePlan (New-PhoneJson -Origin 'http://antiphon.desktop.codeperf.net') $false $true 'C:\Antiphon\repo') 'origin-not-https'
    Assert-Refused 'autostart-refusals.origin-path' (Invoke-PhonePlan (New-PhoneJson -Origin 'https://antiphon.desktop.codeperf.net/extra') $false $true 'C:\Antiphon\repo') 'origin-path'
    Assert-Refused 'autostart-refusals.capacity-0' (Invoke-PhonePlan (New-PhoneJson -Capacity 0) $false $true 'C:\Antiphon\repo') 'capacity-out-of-range'
    Assert-Refused 'autostart-refusals.capacity-3' (Invoke-PhonePlan (New-PhoneJson -Capacity 3) $false $true 'C:\Antiphon\repo') 'capacity-out-of-range'
    Assert-Refused 'autostart-refusals.port-17299' (Invoke-PhonePlan (New-PhoneJson -IncludePort -Port 17299) $false $true 'C:\Antiphon\repo') 'port-invalid'
    Assert-Refused 'autostart-refusals.port-not-broker' (Invoke-PhonePlan (New-PhoneJson -IncludePort -Port 17214) $false $true 'C:\Antiphon\repo') 'port-not-broker'
    Assert-Refused 'autostart-refusals.unknown-key' (Invoke-PhonePlan (New-PhoneJson -ExtraName 'sharedSecret' -ExtraValue 'nope') $false $true 'C:\Antiphon\repo') 'unknown-key:sharedSecret'
    Assert-Refused 'autostart-refusals.malformed' (Invoke-PhonePlan '{' $false $true 'C:\Antiphon\repo') 'config-malformed'
    Assert-Refused 'autostart-refusals.unc-root' (Invoke-PhonePlan (New-PhoneJson -Root '\\server\share') $false $true 'C:\Antiphon\repo') 'root-unc'
    Assert-Refused 'autostart-refusals.secret-missing' (Invoke-PhonePlan (New-PhoneJson) $false $false 'C:\Antiphon\repo') 'secret-missing'
}

function Invoke-ApphostGuard {
    if (-not $script:helperLoaded) {
        Assert-C1744 $false 'apphost-guard.present' 'helper absent'
        Assert-C1744 $false 'apphost-guard.absent' 'helper absent'
    } else {
        $present = Get-WindowsPhoneHomeLaunchRefusal -ConfigPresent $true -ConfigPath 'C:\Antiphon\runner\phone-home.json'
        $presentOk = ($null -ne $present) -and $present.Contains('REFUSED: phone-home-config-present') -and $present.Contains('If the Linux server is serving, this refusal is correct: do NOT delete the file.') -and $present.Contains('Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear:') -and $present.Contains('delete C:\Antiphon\runner\phone-home.json') -and $present.Contains('restart-session-runner.ps1 -Hard') -and $present.Contains('then retry.') -and (-not $present.Contains('restart-linux-server'))
        Assert-C1744 $presentOk 'apphost-guard.present' ([string]$present)
        $absent = Get-WindowsPhoneHomeLaunchRefusal -ConfigPresent $false -ConfigPath 'C:\Antiphon\runner\phone-home.json'
        Assert-C1744 ($null -eq $absent) 'apphost-guard.absent' ([string]$absent)
    }
    $restartAst = Get-ScriptAst $restartPath
    $guard = Get-FirstCommand $restartAst.ast 'Get-WindowsPhoneHomeLaunchRefusal'
    $work = Get-FirstCommand $restartAst.ast 'Get-AppHostWorktreeClassification'
    $dangerNames = @('Invoke-WebRequest', 'Invoke-RestMethod', 'Stop-Process', 'New-AppHostRestartLock', 'Start-AppHostDevLaunch', 'Invoke-AppHostGracefulStop', 'Stop-AppHostLaunchChild')
    $earliestStart = -1
    foreach ($dangerName in $dangerNames) {
        $hit = Get-FirstCommand $restartAst.ast $dangerName
        if ($null -ne $hit) {
            if ($earliestStart -lt 0 -or $hit.Start -lt $earliestStart) { $earliestStart = [int]$hit.Start }
        }
    }
    $orderOk = $false
    $orderDetail = 'guard call absent'
    if ($null -ne $guard -and $null -ne $work) {
        $afterWork = $guard.Start -gt $work.End
        $beforeDanger = $true
        if ($earliestStart -ge 0) { $beforeDanger = $guard.End -lt $earliestStart }
        $exempt = [bool]$guard.Exempt
        $orderOk = $afterWork -and $beforeDanger -and $exempt
        if (-not $orderOk) { $orderDetail = 'after=' + [string]$afterWork + ' before=' + [string]$beforeDanger + ' exempt=' + [string]$exempt }
        else { $orderDetail = '' }
    }
    Assert-C1744 $orderOk 'apphost-guard.restart-order' $orderDetail
    $aspireAst = Get-ScriptAst $aspirePath
    $aspireGuard = Get-FirstCommand $aspireAst.ast 'Get-WindowsPhoneHomeLaunchRefusal'
    $aspireWork = Get-FirstCommand $aspireAst.ast 'Get-AppHostWorktreeClassification'
    $aspireWrite = Get-FirstCommand $aspireAst.ast 'Set-Content'
    $aspireOk = $false
    $aspireDetail = 'guard call absent'
    if ($null -ne $aspireGuard -and $null -ne $aspireWork -and $null -ne $aspireWrite) {
        $aspireOk = ($aspireGuard.Start -gt $aspireWork.End) -and ($aspireGuard.End -lt $aspireWrite.Start)
        if (-not $aspireOk) { $aspireDetail = 'order' } else { $aspireDetail = '' }
    } elseif ($null -eq $aspireWrite -and $null -ne $aspireGuard) {
        $aspireDetail = 'launch lock write absent'
    }
    Assert-C1744 $aspireOk 'apphost-guard.aspire-order' $aspireDetail
    $restartText = [System.IO.File]::ReadAllText($restartPath)
    $aspireText = [System.IO.File]::ReadAllText($aspirePath)
    $linuxOk = (-not $restartText.Contains('restart-linux-server')) -and (-not $aspireText.Contains('restart-linux-server'))
    Assert-C1744 $linuxOk 'apphost-guard.no-linux-name' 'restart-linux-server'
}

function New-AutostartStage([bool]$CopyHelper) {
    $root = Join-Path $script:c1744Scratch 'repo'
    $scripts = Join-Path $root 'scripts'
    [void][System.IO.Directory]::CreateDirectory($scripts)
    if ($CopyHelper) {
        $lib = Join-Path $scripts 'lib'
        [void][System.IO.Directory]::CreateDirectory($lib)
        [System.IO.File]::Copy($helperPath, (Join-Path $lib 'windows-phone-home-env.ps1'), $true)
    }
    [System.IO.File]::Copy($autostartPath, (Join-Path $scripts 'autostart-session-runner.ps1'), $true)
    $stub = @'
param(
    [string]$Name,
    [string]$WorkDir,
    [string]$Exe,
    [string]$ExeArgs,
    [string]$LogFile,
    [string]$ServicePidFile,
    [string]$StateFile,
    [string]$BuildProjectDir
)
$marker = Join-Path $PSScriptRoot 'stub-invoked.txt'
('invoked exeArgs=[' + [string]$ExeArgs + ']') | Set-Content -LiteralPath $marker -Encoding ascii
exit 0
'@
    [System.IO.File]::WriteAllText((Join-Path $scripts 'run-daemon.ps1'), $stub)
    $row = @{
        Scripts = $scripts
        Stub = (Join-Path $scripts 'stub-invoked.txt')
        Log = (Join-Path (Join-Path $root 'logs') 'session-runner.log')
    }
    return ,$row
}

function Invoke-StagedAutostart($Stage, [string]$ConfigPath) {
    $scriptPath = Join-Path $Stage.Scripts 'autostart-session-runner.ps1'
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    try {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = 'pwsh'
        $start.Arguments = '-NoProfile -File "' + $scriptPath + '" -PhoneHomeConfigPath "' + $ConfigPath + '"'
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.CreateNoWindow = $true
        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $start
        [void]$proc.Start()
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
        $proc.WaitForExit()
        $stdoutTask.Wait()
        $stderrTask.Wait()
        $code = [int]$proc.ExitCode
    } finally {
        $ErrorActionPreference = $saved
    }
    $logText = ''
    if (Test-Path -LiteralPath $Stage.Log) { $logText = [System.IO.File]::ReadAllText($Stage.Log) }
    $result = @{
        Exit = [int]$code
        Log = $logText
        Stub = [bool](Test-Path -LiteralPath $Stage.Stub)
    }
    return ,$result
}

function Assert-AutostartClosed([string]$Prefix, $Result, [string]$Reason) {
    Assert-C1744 ($Result.Exit -eq 2) ($Prefix + '.exit') ('exit=' + [string]$Result.Exit)
    $needle = 'phone-home config refused: ' + $Reason
    $logOk = $Result.Log.Contains($needle)
    $logDetail = ''
    if (-not $logOk) { $logDetail = 'missing ' + $needle }
    Assert-C1744 $logOk ($Prefix + '.log') $logDetail
    $stubDetail = ''
    if ($Result.Stub) { $stubDetail = 'stub invoked' }
    Assert-C1744 (-not $Result.Stub) ($Prefix + '.stub') $stubDetail
}

function Invoke-AutostartUnreadable {
    $stage = New-AutostartStage $true
    $config = Join-Path $script:c1744Scratch 'phone-home.json'
    [void][System.IO.Directory]::CreateDirectory($config)
    $result = Invoke-StagedAutostart $stage $config
    Assert-AutostartClosed 'autostart-unreadable' $result 'config-unreadable'
}

function Invoke-AutostartHelperLoad {
    $stage = New-AutostartStage $false
    $config = Join-Path $script:c1744Scratch 'phone-home.json'
    [System.IO.File]::WriteAllText($config, '{')
    $result = Invoke-StagedAutostart $stage $config
    Assert-AutostartClosed 'autostart-helper-load' $result 'helper-load-failed'
}

function Invoke-AutostartRefusedScript {
    $stage = New-AutostartStage $true
    $config = Join-Path $script:c1744Scratch 'phone-home.json'
    [System.IO.File]::WriteAllText($config, '{')
    $result = Invoke-StagedAutostart $stage $config
    Assert-AutostartClosed 'autostart-refused-script' $result 'config-malformed'
}

function Invoke-AutostartScriptDirect {
    $stage = New-AutostartStage $true
    $config = Join-Path $script:c1744Scratch 'phone-home.json'
    $result = Invoke-StagedAutostart $stage $config
    Assert-C1744 ($result.Exit -eq 0) 'autostart-script-direct.exit' ('exit=' + [string]$result.Exit)
    $stubText = ''
    if ($result.Stub) { $stubText = [System.IO.File]::ReadAllText($stage.Stub).Trim() }
    Assert-C1744 ($stubText -ceq 'invoked exeArgs=[--urls http://localhost:17204]') 'autostart-script-direct.stub' $stubText
    Assert-C1744 (-not $result.Log.Contains('phone-home config refused')) 'autostart-script-direct.log' $result.Log
}

function Invoke-NoBuildStamp {
    $stage = Join-Path $script:c1744Scratch 'checkout'
    $scripts = Join-Path $stage 'scripts'
    $lib = Join-Path $scripts 'lib'
    $bin = Join-Path $stage 'src/Antiphon.SessionRunner/bin/Debug/net9.0'
    $runnerRoot = Join-Path $script:c1744Scratch 'runner'
    New-Item -ItemType Directory -Force -Path $lib, $bin, (Join-Path $runnerRoot 'secrets') | Out-Null
    Copy-Item -LiteralPath $startPath -Destination $scripts
    Copy-Item -LiteralPath $helperPath -Destination $lib
    Copy-Item -LiteralPath $RunnerAssembly -Destination (Join-Path $bin 'Antiphon.SessionRunner.dll')
    Set-Content -LiteralPath (Join-Path $runnerRoot 'secrets/phone-home') -Value 'offline fixture'
    $exe = Join-Path $bin 'Antiphon.SessionRunner.exe'
    $nativeWindows = [Environment]::OSVersion.Platform -eq 'Win32NT'
    if ($nativeWindows) {
        Copy-Item -LiteralPath (Join-Path ([Environment]::SystemDirectory) 'where.exe') -Destination $exe
        $expectedExit = @(1, 2)
    } else {
        [IO.File]::WriteAllText($exe, "#!/bin/sh`nexit 42`n")
        & chmod +x $exe
        if ($LASTEXITCODE -ne 0) { throw 'Could not create fixture launch executable.' }
        $expectedExit = 42
    }
    $launcher = Join-Path $scripts (Split-Path -Leaf $startPath)
    $wrapper = Join-Path $script:c1744Scratch 'launch.ps1'
    @'
param($Launcher, $Root)
Set-Variable -Name IsWindows -Value $true -Scope Local -Force
& $Launcher -RunnerId stamp-fixture -Root $Root -ServerOrigin 'http://127.0.0.1:1' -NoBuild
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $wrapper
    $output = (& pwsh -NoProfile -File $wrapper $launcher $runnerRoot 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    $announced = $output.Contains("Phone-home runner 'stamp-fixture'")
    Assert-C1744 ($announced -and $exitCode -in $expectedExit) 'no-build-stamp.launch' "fixture launch exit=$exitCode"
    Assert-C1744 ($output -match 'WARNING:.*-NoBuild.*unknown') 'no-build-stamp.warning' 'unknown stamp warning absent'
}

function Invoke-Compat {
    $paths = @($helperPath, $autostartPath, $startPath, $restartPath, $aspirePath)
    $asciiOk = $true
    $asciiDetail = ''
    foreach ($path in $paths) {
        if (-not (Test-AsciiPath $path)) {
            $asciiOk = $false
            if ([string]::IsNullOrEmpty($asciiDetail)) { $asciiDetail = $path }
        }
    }
    if (-not $asciiOk -and -not (Test-Path -LiteralPath $helperPath)) { $asciiDetail = 'helper absent' }
    Assert-C1744 $asciiOk 'compat.ascii' $asciiDetail
    if (-not (Test-Path -LiteralPath $helperPath)) {
        Assert-C1744 $false 'compat.helper-syntax' 'helper absent'
    } else {
        $helperAst = Get-ScriptAst $helperPath
        Assert-C1744 (Test-PowerShell51Ast $helperAst) 'compat.helper-syntax' 'parse or ps7 syntax'
    }
    $autostartAst = Get-ScriptAst $autostartPath
    Assert-C1744 (Test-PowerShell51Ast $autostartAst) 'compat.autostart-syntax' 'parse or ps7 syntax'
    $requiresOk = $false
    $requiresDetail = 'helper absent'
    if ((Test-Path -LiteralPath $helperPath) -and (Test-Path -LiteralPath $autostartPath)) {
        $helperText = [System.IO.File]::ReadAllText($helperPath)
        $autostartText = [System.IO.File]::ReadAllText($autostartPath)
        $requiresOk = (-not $helperText.Contains('#requires -Version 7')) -and (-not $autostartText.Contains('#requires -Version 7'))
        if (-not $requiresOk) { $requiresDetail = '#requires -Version 7' } else { $requiresDetail = '' }
    }
    Assert-C1744 $requiresOk 'compat.no-requires-7' $requiresDetail
}

$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('c1744g3-' + [guid]::NewGuid().ToString('N'))
$script:c1744Scratch = $scratch
[void][System.IO.Directory]::CreateDirectory($scratch)
try {
    try {
        switch ($Case) {
            'env-table' { Invoke-EnvTable }
            'autostart-direct' { Invoke-AutostartDirect }
            'autostart-phone-home' { Invoke-AutostartPhoneHome }
            'autostart-refusals' { Invoke-AutostartRefusals }
            'autostart-unreadable' { Invoke-AutostartUnreadable }
            'autostart-helper-load' { Invoke-AutostartHelperLoad }
            'autostart-refused-script' { Invoke-AutostartRefusedScript }
            'autostart-script-direct' { Invoke-AutostartScriptDirect }
            'apphost-guard' { Invoke-ApphostGuard }
            'compat' { Invoke-Compat }
            'no-build-stamp' { Invoke-NoBuildStamp }
            default { Assert-C1744 $false 'unknown-case' $Case }
        }
    } catch {
        $script:failed++
        Write-Host ("FAIL C1744G3 harness-error :: " + $_.Exception.Message)
    }
} finally {
    if ([System.IO.Directory]::Exists($scratch)) {
        try { [System.IO.Directory]::Delete($scratch, $true) } catch { }
    }
}
Write-Host ("C487: " + [string]$script:passed + " passed, " + [string]$script:failed + " failed, " + [string]($script:passed + $script:failed) + " rows")
if ($script:failed) { Write-Host 'C487 HARNESS EXIT CODE: 1'; exit 1 }
Write-Host 'C487 HARNESS EXIT CODE: 0'
exit 0
