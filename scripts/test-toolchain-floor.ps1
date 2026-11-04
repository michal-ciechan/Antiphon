#requires -Version 5.1
# CARD-1698 harness: scripts/lib/toolchain-floor.ps1 and the entry points that print its warnings.
# Every version comes from an injected source or a fake git/dotnet function; no case runs the
# real git or dotnet. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$repo = Split-Path $here -Parent
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'c487-harness.ps1')
. (Join-Path $lib 'toolchain-floor.ps1')

if ([string]::IsNullOrWhiteSpace($env:TEMP)) {
    if (-not [string]::IsNullOrWhiteSpace($env:TMPDIR)) { $env:TEMP = $env:TMPDIR }
    else { $env:TEMP = '/tmp' }
}
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function New-C1698Source([string]$Git, [string]$Sdks = '10.0.204 [/fake/sdk]') {
    $g = $Git; $s = $Sdks
    return { param($tool) if ($tool -eq 'git') { $g } elseif ($tool -eq 'dotnet-sdk') { $s } else { '' } }.GetNewClosure()
}

function Get-C1698Warnings([scriptblock]$Source) {
    return @(Get-ToolchainFloorWarnings -RepoRoot $repo -VersionSource $Source)
}

function Test-C1698_GitBelowFloor {
    $w = Get-C1698Warnings (New-C1698Source 'git version 2.42.0.windows.1')
    Assert-C487 -Cond ($w.Count -eq 1) -Name 'C1698 Below one warning' -Detail ('count=' + $w.Count)
    $x = $w | Select-Object -First 1
    Assert-C487 -Cond ($x.tool -eq 'git') -Name 'C1698 Below names the tool' -Detail ([string]$x.tool)
    Assert-C487 -Cond ($x.found -eq '2.42.0') -Name 'C1698 Below names the found version' -Detail ([string]$x.found)
    Assert-C487 -Cond ($x.floor -eq '2.43') -Name 'C1698 Below names the floor' -Detail ([string]$x.floor)
    Assert-C487 -Cond ([string]$x.impact -match 'lands will fail' -and [string]$x.impact -match 'show-ref --exists') -Name 'C1698 Below names what stops working' -Detail ([string]$x.impact)
    $fixOk = $false
    if (Test-ToolchainFloorWindowsHost) { $fixOk = ($x.fix -eq 'winget upgrade Git.Git') } else { $fixOk = ([string]$x.fix -match 'install Git') }
    Assert-C487 -Cond $fixOk -Name 'C1698 Below names the fix command' -Detail ([string]$x.fix)
    $m = [string]$x.message
    $all = $m.StartsWith('WARNING: git 2.42.0 is below the minimum 2.43; lands will fail') -and $m.Contains('Fix: ' + $x.fix)
    Assert-C487 -Cond $all -Name 'C1698 Below message carries all five fields' -Detail $m
    $below2 = Get-C1698Warnings (New-C1698Source 'git version 2.9.5')
    Assert-C487 -Cond ($below2.Count -eq 1 -and $below2[0].reason -eq 'below-floor') -Name 'C1698 Below compares by version not string (2.9.5)' -Detail ('count=' + $below2.Count)
}

function Test-C1698_GitAtOrAboveFloor {
    foreach ($v in @('git version 2.43', 'git version 2.43.0', 'git version 2.56.0.windows.1', 'git version 2.100.1', 'git version 3.0.0')) {
        $w = Get-C1698Warnings (New-C1698Source $v)
        Assert-C487 -Cond ($w.Count -eq 0) -Name ('C1698 AtOrAbove no warning ' + $v) -Detail (($w | ForEach-Object { $_.message }) -join ' | ')
    }
}

function Test-C1698_UnreadableVersion {
    $gitEntry = @(Get-ToolchainFloorManifest | Where-Object { $_.tool -eq 'git' })[0]
    $hostFix = $gitEntry.fixOther
    if (Test-ToolchainFloorWindowsHost) { $hostFix = $gitEntry.fixWindows }
    $cases = @(
        @{ Name = 'garbage'; Source = (New-C1698Source 'garbage'); Raw = 'garbage' },
        @{ Name = 'empty'; Source = (New-C1698Source ''); Raw = '' },
        @{ Name = 'throwing source'; Source = { param($tool) throw 'git: command not found' }; Raw = 'git: command not found' }
    )
    foreach ($c in $cases) {
        $threw = $false
        $w = @()
        try { $w = Get-C1698Warnings $c.Source } catch { $threw = $true }
        $git = @($w | Where-Object { $_.tool -eq 'git' })
        $ok = (-not $threw) -and $git.Count -eq 1 -and $git[0].reason -eq 'version-unreadable' -and
            ([string]$git[0].message).StartsWith('WARNING: the git version could not be read') -and $git[0].floor -eq '2.43'
        Assert-C487 -Cond $ok -Name ('C1698 Unreadable warns, never throws: ' + $c.Name) -Detail ('threw=' + $threw + ' ' + (($w | ForEach-Object { $_.message }) -join ' | '))
        # The whole message: the floor, what stops working and the host fix, as the below-floor
        # message carries them (the impact and fix come from the manifest, not from the warning).
        $m = ''
        if ($git.Count -eq 1) { $m = [string]$git[0].message }
        $expected = "WARNING: the git version could not be read ('" + $c.Raw + "'); the minimum is 2.43; if it is older, " + $gitEntry.impact + '. Fix: ' + $hostFix
        $fields = $m.Contains('the minimum is 2.43') -and $m.Contains('lands will fail') -and $m.Contains($gitEntry.impact) -and $m.EndsWith('. Fix: ' + $hostFix)
        Assert-C487 -Cond ($fields -and $m -eq $expected) -Name ('C1698 Unreadable message carries floor, impact and fix: ' + $c.Name) -Detail $m
    }
}

function Test-C1698_SdkFloor {
    $rows = @(
        @{ Sdks = "9.0.300 [/x]`n10.0.100 [/x]"; Warn = $true; Found = '10.0.100' },
        @{ Sdks = '11.0.100 [/x]'; Warn = $true; Found = '11.0.100' },
        @{ Sdks = "9.0.300 [/x]`r`n10.0.204 [/x]"; Warn = $false; Found = '10.0.204' },
        @{ Sdks = '10.1.100-preview.1 [/x]'; Warn = $false; Found = '10.1.100' }
    )
    foreach ($r in $rows) {
        $report = Get-ToolchainFloorReport -RepoRoot $repo -VersionSource (New-C1698Source 'git version 2.47.3' $r.Sdks)
        $sdk = @($report.tools | Where-Object { $_.tool -eq 'dotnet-sdk' })[0]
        $w = @($report.warnings | Where-Object { $_.tool -eq 'dotnet-sdk' })
        $ok = ($w.Count -eq 1) -eq $r.Warn -and $sdk.found -eq $r.Found -and $sdk.floor -eq '10.0.204'
        Assert-C487 -Cond $ok -Name ('C1698 Sdk ' + ($r.Sdks -replace '\s+', ' ')) -Detail ('found=' + $sdk.found + ' status=' + $sdk.status + ' warnings=' + $w.Count)
    }
}

function Test-C1698_ManifestSources {
    $report = Get-ToolchainFloorReport -RepoRoot $repo -VersionSource (New-C1698Source 'git version 2.47.3')
    $shell = [IO.File]::ReadAllText((Join-Path $repo 'docker/git-minimum-version.sh'))
    $declared = [regex]::Match($shell, '(?m)^GIT_MINIMUM_VERSION=(\d+\.\d+)\r?$').Groups[1].Value
    $git = @($report.tools | Where-Object { $_.tool -eq 'git' })[0]
    Assert-C487 -Cond ($declared -ne '' -and $git.floor -eq $declared -and $git.source -eq 'docker/git-minimum-version.sh') -Name 'C1698 Manifest git floor is the image guard declaration' -Detail ('declared=' + $declared + ' floor=' + $git.floor)
    $pin = [string](([IO.File]::ReadAllText((Join-Path $repo 'global.json')) | ConvertFrom-Json).sdk.version)
    $sdk = @($report.tools | Where-Object { $_.tool -eq 'dotnet-sdk' })[0]
    Assert-C487 -Cond ($sdk.floor -eq $pin -and $sdk.source -eq 'global.json') -Name 'C1698 Manifest SDK floor is global.json' -Detail ('pin=' + $pin + ' floor=' + $sdk.floor)
    $open = @($report.tools | Where-Object { -not $_.enforced } | ForEach-Object { $_.tool + '=' + $_.floor + '/' + $_.status })
    Assert-C487 -Cond (($open -join ',') -eq 'pwsh=to decide/not-enforced,node=to decide/not-enforced,docker=to decide/not-enforced') -Name 'C1698 Manifest pwsh node docker are listed, not enforced' -Detail ($open -join ',')
    Assert-C487 -Cond (@($report.warnings).Count -eq 0) -Name 'C1698 Manifest no warning at the pins' -Detail (($report.warnings | ForEach-Object { $_.message }) -join ' | ')
    $empty = Join-Path $ResultsDirectory ('no-floors-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $empty -Force | Out-Null
    $missing = @(Get-ToolchainFloorWarnings -RepoRoot $empty -VersionSource (New-C1698Source 'git version 2.47.3'))
    $reasons = @($missing | ForEach-Object { $_.tool + ':' + $_.reason }) -join ','
    Assert-C487 -Cond ($reasons -eq 'git:floor-unreadable,dotnet-sdk:floor-unreadable') -Name 'C1698 Manifest missing floor sources warn, not throw' -Detail $reasons
}

# A copied entry script in a private root with its floor sources, a fake git and fake dotnet.
function New-C1698ScriptRoot {
    $root = Join-Path $ResultsDirectory ('entry-' + [guid]::NewGuid().ToString('N'))
    foreach ($d in @('scripts/lib', 'docker')) { New-Item -ItemType Directory -Path (Join-Path $root $d) -Force | Out-Null }
    Copy-Item -LiteralPath (Join-Path $lib 'toolchain-floor.ps1') -Destination (Join-Path $root 'scripts/lib/toolchain-floor.ps1')
    Copy-Item -LiteralPath (Join-Path $repo 'docker/git-minimum-version.sh') -Destination (Join-Path $root 'docker/git-minimum-version.sh')
    Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $root 'global.json')
    return $root
}

function Invoke-C1698Entry([string]$Root, [string]$Entry, [string]$GitVersion, [int]$BuildExit, [string[]]$EntryArgs) {
    $driver = Join-Path $Root ('driver-' + [guid]::NewGuid().ToString('N') + '.ps1')
    $trace = Join-Path $Root ('trace-' + [guid]::NewGuid().ToString('N') + '.log')
    $argText = ''
    foreach ($a in $EntryArgs) { $argText += (' ' + $a) }
    $text = @"
`$ErrorActionPreference = 'Continue'
function git { 'git version $GitVersion' }
function dotnet {
    `$a = `$args -join ' '
    if (`$a -eq '--list-sdks') { '10.0.204 [/fake/sdk]'; return }
    Add-Content -LiteralPath '$trace' -Value `$a -Encoding ASCII
    `$global:LASTEXITCODE = 0
    if (`$a -like 'build *') { `$global:LASTEXITCODE = $BuildExit }
}
function Start-Sleep { }
& (Join-Path `$PSScriptRoot '$Entry')$argText
exit `$LASTEXITCODE
"@
    Set-Content -LiteralPath $driver -Value $text -Encoding ASCII
    $out = & pwsh -NoProfile -NonInteractive -File $driver 2>&1 | Out-String
    $code = $LASTEXITCODE
    $calls = ''
    if (Test-Path -LiteralPath $trace) { $calls = ([IO.File]::ReadAllText($trace)).Trim() }
    return [pscustomobject]@{ Exit = $code; Output = $out; Calls = $calls }
}

function Test-C1698_RestartServerUnchanged {
    $root = New-C1698ScriptRoot
    Copy-Item -LiteralPath (Join-Path $repo 'restart-server.ps1') -Destination (Join-Path $root 'restart-server.ps1')
    Set-Content -LiteralPath (Join-Path $root 'assert-dev-ports-clear.ps1') -Value "param([int[]]`$Ports, [switch]`$StopPortOwners)`nexit 0" -Encoding ASCII
    $below = Invoke-C1698Entry $root 'restart-server.ps1' '2.42.0' 0 @()
    $at = Invoke-C1698Entry $root 'restart-server.ps1' '2.43.0' 0 @()
    $warn = 'WARNING: git 2.42.0 is below the minimum 2.43'
    Assert-C487 -Cond ($below.Output.Contains($warn)) -Name 'C1698 RestartServer prints the warning' -Detail $below.Output
    Assert-C487 -Cond (-not $at.Output.Contains('WARNING:')) -Name 'C1698 RestartServer prints none at the floor' -Detail $at.Output
    Assert-C487 -Cond ($below.Exit -eq 0 -and $at.Exit -eq 0 -and $below.Output.Contains('Server restarted.')) -Name 'C1698 RestartServer exit 0 with a warning' -Detail ('below=' + $below.Exit + ' at=' + $at.Exit)
    $expected = @('build *', 'run --project * antiphon-server start --endpoint https://localhost:22025')
    $lines = @($below.Calls -split "`r?`n")
    $shape = $lines.Count -eq 3 -and $lines[0] -like 'run --project * antiphon-server stop --endpoint https://localhost:22025' -and $lines[1] -like $expected[0] -and $lines[2] -like $expected[1]
    Assert-C487 -Cond ($shape -and $below.Calls -eq $at.Calls) -Name 'C1698 RestartServer same stop build start with a warning' -Detail ($below.Calls + ' || ' + $at.Calls)
    $belowFail = Invoke-C1698Entry $root 'restart-server.ps1' '2.42.0' 1 @()
    $atFail = Invoke-C1698Entry $root 'restart-server.ps1' '2.43.0' 1 @()
    $failOk = $belowFail.Exit -eq 1 -and $atFail.Exit -eq 1 -and $belowFail.Calls -eq $atFail.Calls -and $belowFail.Output.Contains('Build failed! Aborting.') -and @($belowFail.Calls -split "`r?`n").Count -eq 2
    Assert-C487 -Cond $failOk -Name 'C1698 RestartServer build failure still exit 1 with a warning' -Detail ('below=' + $belowFail.Exit + ' at=' + $atFail.Exit + ' ' + $belowFail.Calls)
}

function Get-C1698RunnerResult([string]$Output) {
    $line = @($Output -split "`r?`n" | Where-Object { $_.StartsWith('RUNNER RESTART RESULT: ') }) | Select-Object -Last 1
    if (-not $line) { return $null }
    return ($line.Substring('RUNNER RESTART RESULT: '.Length) | ConvertFrom-Json)
}

function Test-C1698_RestartRunnerUnchanged {
    # -WaitOnly -Hard is refused at argument validation, before any census, probe or control
    # call, so the real entry and the real platform run here with no side effect.
    $root = New-C1698ScriptRoot
    foreach ($f in @('restart-session-runner.ps1', 'session-runner-restart-health.ps1')) {
        Copy-Item -LiteralPath (Join-Path $here $f) -Destination (Join-Path $root (Join-Path 'scripts' $f))
    }
    $below = Invoke-C1698Entry $root 'scripts/restart-session-runner.ps1' '2.42.0' 0 @('-WaitOnly', '-Hard')
    $at = Invoke-C1698Entry $root 'scripts/restart-session-runner.ps1' '2.43.0' 0 @('-WaitOnly', '-Hard')
    Assert-C487 -Cond ($below.Output.Contains('WARNING: git 2.42.0 is below the minimum 2.43')) -Name 'C1698 RestartRunner prints the warning' -Detail $below.Output
    Assert-C487 -Cond (-not $at.Output.Contains('WARNING:')) -Name 'C1698 RestartRunner prints none at the floor' -Detail $at.Output
    $rb = Get-C1698RunnerResult $below.Output
    $ra = Get-C1698RunnerResult $at.Output
    $same = $null -ne $rb -and $null -ne $ra
    if ($same) {
        foreach ($k in @('outcome', 'mode', 'exitCode', 'operation', 'error', 'errorCode', 'supervisor', 'wrapper', 'runner')) {
            if ([string]$rb.$k -ne [string]$ra.$k) { $same = $false }
        }
    }
    Assert-C487 -Cond ($same -and $rb.outcome -eq 'action-failed' -and $rb.operation -eq 'validate-arguments') -Name 'C1698 RestartRunner same result with a warning' -Detail ($below.Output + ' || ' + $at.Output)
    Assert-C487 -Cond ($below.Exit -eq 1 -and $at.Exit -eq 1) -Name 'C1698 RestartRunner same exit code with a warning' -Detail ('below=' + $below.Exit + ' at=' + $at.Exit)
}

# A library that fails to load: a script-terminating throw, or a file PowerShell cannot parse
# (an unparsable dot-source is non-terminating unwrapped, so only the caller's wrapper prints the
# check-failed line). Only this reaches the entry wrappers: the shipped lib catches everything else.
function Get-C1698BrokenLibraries {
    return @(
        @{ Name = 'throws'; Text = "throw 'C1698 toolchain lib load failure'" },
        @{ Name = 'unparsable'; Text = "function Get-C1698Broken {`n    `$x = 1`n" }
    )
}

function Set-C1698BrokenLibrary([string]$Root, [string]$Text) {
    Set-Content -LiteralPath (Join-Path $Root 'scripts/lib/toolchain-floor.ps1') -Value $Text -Encoding ASCII
}

function Test-C1698_RestartServerLibraryLoadFailure {
    $normal = New-C1698ScriptRoot
    Copy-Item -LiteralPath (Join-Path $repo 'restart-server.ps1') -Destination (Join-Path $normal 'restart-server.ps1')
    Set-Content -LiteralPath (Join-Path $normal 'assert-dev-ports-clear.ps1') -Value "param([int[]]`$Ports, [switch]`$StopPortOwners)`nexit 0" -Encoding ASCII
    $at = Invoke-C1698Entry $normal 'restart-server.ps1' '2.43.0' 0 @()
    foreach ($b in (Get-C1698BrokenLibraries)) {
        $root = New-C1698ScriptRoot
        Copy-Item -LiteralPath (Join-Path $repo 'restart-server.ps1') -Destination (Join-Path $root 'restart-server.ps1')
        Set-Content -LiteralPath (Join-Path $root 'assert-dev-ports-clear.ps1') -Value "param([int[]]`$Ports, [switch]`$StopPortOwners)`nexit 0" -Encoding ASCII
        Set-C1698BrokenLibrary $root $b.Text
        $r = Invoke-C1698Entry $root 'restart-server.ps1' '2.43.0' 0 @()
        # The calls carry the private root in the server project path; the rest is identical.
        $lines = @($r.Calls.Replace($root, '<root>') -split "`r?`n")
        $shape = $lines.Count -eq 3 -and $lines[0] -like 'run --project * antiphon-server stop --endpoint https://localhost:22025' -and
            $lines[1] -like 'build *' -and $lines[2] -like 'run --project * antiphon-server start --endpoint https://localhost:22025'
        $ok = $r.Exit -eq 0 -and $r.Output.Contains('Server restarted.') -and $r.Output.Contains('WARNING: the toolchain floor check failed') -and
            $shape -and $r.Calls.Replace($root, '<root>') -eq $at.Calls.Replace($normal, '<root>')
        Assert-C487 -Cond $ok -Name ('C1698 RestartServer survives a library that fails to load: ' + $b.Name) -Detail ('exit=' + $r.Exit + ' calls=' + $r.Calls + ' || ' + $at.Calls + ' || ' + $r.Output)
    }
}

function Test-C1698_RestartRunnerLibraryLoadFailure {
    $normal = New-C1698ScriptRoot
    foreach ($f in @('restart-session-runner.ps1', 'session-runner-restart-health.ps1')) {
        Copy-Item -LiteralPath (Join-Path $here $f) -Destination (Join-Path $normal (Join-Path 'scripts' $f))
    }
    $at = Invoke-C1698Entry $normal 'scripts/restart-session-runner.ps1' '2.43.0' 0 @('-WaitOnly', '-Hard')
    $ra = Get-C1698RunnerResult $at.Output
    foreach ($b in (Get-C1698BrokenLibraries)) {
        $root = New-C1698ScriptRoot
        foreach ($f in @('restart-session-runner.ps1', 'session-runner-restart-health.ps1')) {
            Copy-Item -LiteralPath (Join-Path $here $f) -Destination (Join-Path $root (Join-Path 'scripts' $f))
        }
        Set-C1698BrokenLibrary $root $b.Text
        $r = Invoke-C1698Entry $root 'scripts/restart-session-runner.ps1' '2.43.0' 0 @('-WaitOnly', '-Hard')
        $rb = Get-C1698RunnerResult $r.Output
        $same = $null -ne $rb -and $null -ne $ra -and $rb.outcome -eq 'action-failed' -and $rb.operation -eq 'validate-arguments'
        if ($same) {
            foreach ($k in @('outcome', 'mode', 'exitCode', 'operation', 'error', 'errorCode', 'supervisor', 'wrapper', 'runner')) {
                if ([string]$rb.$k -ne [string]$ra.$k) { $same = $false }
            }
        }
        $ok = $same -and $r.Exit -eq $at.Exit -and $r.Exit -eq 1 -and $r.Output.Contains('WARNING: the toolchain floor check failed')
        Assert-C487 -Cond $ok -Name ('C1698 RestartRunner survives a library that fails to load: ' + $b.Name) -Detail ('exit=' + $r.Exit + '/' + $at.Exit + ' || ' + $r.Output)
    }
}

# Windows PowerShell 5.1 is the fallback host for the restart and nightly entry points and the
# library they dot-source; pwsh 7 accepts syntax 5.1 cannot parse, so every harness here passes
# with it. This scan refuses the pwsh 7 operators by token kind (ternary ?:, ??, ??=, ?., ?[,
# pipeline chains && and ||) and ForEach-Object -Parallel by syntax tree. It is a guard, not a
# 5.1 parse: a real powershell.exe parse on Windows is the full proof.
function Get-C1698Pwsh7Syntax([System.Management.Automation.Language.Ast]$Ast, $Tokens) {
    $found = @()
    $kinds = @('QuestionMark', 'QuestionQuestion', 'QuestionQuestionEquals', 'QuestionDot', 'QuestionLBracket', 'AndAnd', 'OrOr')
    foreach ($t in @($Tokens)) {
        if ($kinds -contains $t.Kind.ToString()) { $found += ($t.Kind.ToString() + ' at line ' + $t.Extent.StartLineNumber) }
    }
    $parallel = $Ast.FindAll({
            param($n)
            if ($n -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            if (@('ForEach-Object', '%', 'foreach') -notcontains [string]$n.GetCommandName()) { return $false }
            foreach ($e in $n.CommandElements) {
                if ($e -is [System.Management.Automation.Language.CommandParameterAst] -and $e.ParameterName.Length -ge 2 -and
                    'parallel'.StartsWith($e.ParameterName.ToLowerInvariant())) { return $true }
            }
            return $false
        }, $true)
    foreach ($p in @($parallel)) { $found += ('ForEach-Object -Parallel at line ' + $p.Extent.StartLineNumber) }
    return @($found)
}

function Get-C1698Pwsh7SyntaxInText([string]$Text) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$errors)
    return @(Get-C1698Pwsh7Syntax $ast $tokens)
}

function Test-C1698_WindowsPowerShellSyntax {
    # Controls: the scan must flag each construct and must not flag the 5.1 spellings.
    $flag = @(
        @{ Name = 'ternary'; Text = '$fix = $windows ? $a : $b' },
        @{ Name = 'null-coalescing'; Text = '$v = (Get-Thing) ?? ''''' },
        @{ Name = 'null-coalescing assignment'; Text = '$v ??= 1' },
        @{ Name = 'null-conditional member'; Text = '$n = ${x}?.Name' },
        @{ Name = 'null-conditional index'; Text = '$n = ${x}?[0]' },
        @{ Name = 'pipeline chain and'; Text = 'git fetch && git status' },
        @{ Name = 'pipeline chain or'; Text = 'git fetch || exit 1' },
        @{ Name = 'ForEach-Object -Parallel'; Text = '1..3 | ForEach-Object -Parallel { $_ }' },
        @{ Name = 'percent -Para'; Text = '1..3 | % -Para { $_ } -ThrottleLimit 2' }
    )
    foreach ($c in $flag) {
        $hits = @(Get-C1698Pwsh7SyntaxInText $c.Text)
        Assert-C487 -Cond ($hits.Count -ge 1) -Name ('C1698 Syntax scan flags ' + $c.Name) -Detail ($c.Text + ' -> ' + ($hits -join '; '))
    }
    $clean = 'if ($windows) { $fix = $a } else { $fix = $b }' + "`n" + '$v = @(1, 2) | ? { $_ -gt 1 } | ForEach-Object -Process { $_ }' + "`n" +
        'if ($null -eq $v) { $v = '''' }' + "`n" + 'git fetch; if ($LASTEXITCODE -eq 0 -and $ok -or $x) { $n = $x.Name }'
    $none = @(Get-C1698Pwsh7SyntaxInText $clean)
    Assert-C487 -Cond ($none.Count -eq 0) -Name 'C1698 Syntax scan passes the 5.1 spellings' -Detail ($none -join '; ')

    $files = @('scripts/lib/toolchain-floor.ps1', 'restart-server.ps1', 'scripts/restart-session-runner.ps1',
        'scripts/session-runner-restart-health.ps1', 'scripts/nightly-run.ps1', 'scripts/lib/nightly-run-impl.ps1',
        'scripts/nightly-tests.ps1', 'scripts/lib/nightly-tests-impl.ps1')
    foreach ($f in $files) {
        $path = Join-Path $repo $f
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        $hits = @(Get-C1698Pwsh7Syntax $ast $tokens)
        $ok = (Test-Path -LiteralPath $path -PathType Leaf) -and @($errors).Count -eq 0 -and $hits.Count -eq 0
        Assert-C487 -Cond $ok -Name ('C1698 Windows PowerShell 5.1 syntax: ' + $f) -Detail ('parse errors=' + @($errors).Count + ' pwsh 7 only: ' + ($hits -join '; '))
    }
}

$cases = Get-C487CaseFunctions -Prefix 'C1698_'
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in $cases) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'toolchain-floor-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
