#requires -Version 5.1
using namespace System.Management.Automation.Language

param([ValidateSet('audit', 'decoys')][string]$Case, [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'

function Read-StampAst([string]$Text, [string]$Surface) {
    $tokens = $null
    $errors = $null
    $ast = [Parser]::ParseInput($Text, $Surface, [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw "$Surface parse error: $($errors[0].Message)" }
    return $ast
}

function Test-StampArgument($Element) {
    if ($Element -is [CommandParameterAst]) {
        return $Element.Extent.Text -ceq '-p:SourceRevisionId=$sourceRevision'
    }
    return $Element -is [ExpandableStringExpressionAst] -and
        $Element.Value -ceq '-p:SourceRevisionId=$sourceRevision' -and
        $Element.NestedExpressions.Count -eq 1 -and
        $Element.NestedExpressions[0] -is [VariableExpressionAst] -and
        $Element.NestedExpressions[0].VariablePath.UserPath -ceq 'sourceRevision'
}

function Get-StatementBlock($Node) {
    for ($parent = $Node.Parent; $null -ne $parent; $parent = $parent.Parent) {
        if ($parent -is [StatementBlockAst] -or $parent -is [NamedBlockAst]) { return $parent }
    }
}

function Test-DeadBranch($Node) {
    for ($block = $Node.Parent; $null -ne $block; $block = $block.Parent) {
        if ($block -isnot [StatementBlockAst] -or $block.Parent -isnot [IfStatementAst]) { continue }
        foreach ($clause in $block.Parent.Clauses) {
            if ($clause.Item2 -eq $block -and $clause.Item1.Extent.Text -ieq '$false') { return $true }
        }
    }
    return $false
}

function Test-StampedSplat($Ast, $Command, $Splat, [string]$Surface) {
    $block = Get-StatementBlock $Command
    $assignments = @($Ast.FindAll({
        param($node)
        $node -is [AssignmentStatementAst] -and $node.Left -is [VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -eq $Splat.VariablePath.UserPath -and
            $node.Extent.StartOffset -lt $Command.Extent.StartOffset
    }, $true))
    $initial = $assignments | Where-Object {
        $_.Operator -eq [TokenKind]::Equals -and (Get-StatementBlock $_) -eq $block
    } | Select-Object -Last 1
    if ($null -eq $initial) { return $false }

    # Each accepted append belongs to the call's block. A comment or another
    # branch has no authority here. (The daemon's validated HEAD branch is C#
    # since unit 30: DaemonSupervisor.cs, pinned by DaemonSupervisorTests.)
    foreach ($append in $assignments) {
        if ($append.Operator -ne [TokenKind]::PlusEquals -or
            $append.Extent.StartOffset -lt $initial.Extent.EndOffset -or
            $append.Right -isnot [CommandExpressionAst] -or
            -not (Test-StampArgument $append.Right.Expression)) { continue }
        if ((Get-StatementBlock $append) -eq $block) { return $true }
    }
    return $false
}

function Get-ScriptArgumentElements($Command, [string]$Name) {
    if ($Name -in @('Invoke-Expression', 'iex', 'Open-Tab')) {
        return $Command.CommandElements[-1]
    }
    if ($Name -notin @('Start-Process', 'start', 'saps')) { return }
    for ($i = 1; $i -lt $Command.CommandElements.Count - 1; $i++) {
        $parameter = $Command.CommandElements[$i]
        if ($parameter -isnot [CommandParameterAst] -or $parameter.ParameterName -notin @('ArgumentList', 'Args')) { continue }
        $argument = $Command.CommandElements[$i + 1]
        if ($argument -is [ArrayExpressionAst] -and $argument.SubExpression.Statements.Count -eq 1) {
            $statement = $argument.SubExpression.Statements[0]
            if ($statement -is [PipelineAst]) { $argument = $statement.GetPureExpression() }
        }
        if ($argument -is [ArrayLiteralAst]) { return $argument.Elements }
        return $argument
    }
}

function Get-PowerShellBuilds($Ast, [string]$Surface) {
    foreach ($command in $Ast.FindAll({ param($node) $node -is [CommandAst] }, $true)) {
        if (Test-DeadBranch $command) { continue }
        $name = $command.GetCommandName()
        $elements = @($command.CommandElements)
        $dotnetIndex = -1
        if ($name -in @('dotnet', 'dotnet.exe')) {
            $dotnetIndex = 0
        } else {
            # The build-slot wrapper forwards the argv following its separator.
            for ($i = 1; $i -lt $elements.Count - 1; $i++) {
                if ($elements[$i].Extent.Text -ceq '--' -and $elements[$i + 1].Value -in @('dotnet', 'dotnet.exe') -and
                    ($elements[0..($i - 1)].Extent.Text -join ' ') -match 'build-slot\.ps1') {
                    $dotnetIndex = $i + 1
                    break
                }
            }
        }
        if ($dotnetIndex -ge 0 -and $elements.Count -gt $dotnetIndex + 1) {
            $verb = $elements[$dotnetIndex + 1]
            $arguments = @($elements | Select-Object -Skip ($dotnetIndex + 2))
            $stamp = $false
            if ($verb -is [VariableExpressionAst] -and $verb.Splatted) {
                $stamp = Test-StampedSplat $Ast $command $verb $Surface
            } elseif ($verb.Value -in @('build', 'pack', 'publish', 'run')) {
                if ($verb.Value -eq 'run' -and $arguments.Extent.Text -contains '--no-build') { continue }
                $stamp = @($arguments | Where-Object { Test-StampArgument $_ }).Count -eq 1
            } else { continue }
            [pscustomobject]@{ Stamped = $stamp; Invocation = $command.Extent.Text }
        }

        $childArguments = @(Get-ScriptArgumentElements $command $name)
        if ($childArguments.Count -gt 0 -and $name -in @('Start-Process', 'start', 'saps') -and
            $elements[1..([Math]::Min(3, $elements.Count - 1))].Value -contains 'dotnet') {
            $argv = if ($childArguments.Count -eq 1) { $childArguments[0].Value } else {
                $childArguments.Extent.Text -join ' '
            }
            Get-PowerShellBuilds (Read-StampAst "dotnet $argv" $Surface) $Surface
            continue
        }
        foreach ($argument in $childArguments) {
            if ($argument -isnot [StringConstantExpressionAst] -and $argument -isnot [ExpandableStringExpressionAst]) { continue }
            if ($argument.Value -match '(?:^|[;\s])dotnet(?:\.exe)?\s+(?:build|run|pack|publish|@)') {
                Get-PowerShellBuilds (Read-StampAst $argument.Value $Surface) $Surface
            }
        }
    }
}

function Get-UnstampedPowerShellBuild([string]$Text) {
    Get-PowerShellBuilds (Read-StampAst $Text 'fixture') 'fixture' |
        Where-Object { -not $_.Stamped } | Select-Object -ExpandProperty Invocation
}

if ($Case -eq 'audit') {
    $expected = [ordered]@{
    }
    # The dev stack's builds are C# since unit 37 (dev-aspire.ps1, dev-start.ps1, dev-upgrade.ps1 and restart-server.ps1 are
    # launchers): DevAspireTests, DevStackTests and DevRestartTests pin their stamped argv and the unreadable-HEAD refusals.
    # The watchdog publish is C# since unit 10 of 2026-11-22-remaining-ps1-to-csharp.md (NightlyWatchdogDeploy.cs, which
    # refuses an unreadable HEAD); NightlyVerificationContractTests.C545_DeployCommandLines pins its stamped argv.
    # The daemon build is C# since unit 30 (DaemonSupervisor.cs, which builds unstamped when HEAD is unreadable);
    # DaemonSupervisorTests pins its stamped argv. scripts/run-daemon.ps1, its launcher, is still scanned for a build.
    # The phone-home runner's build is C# since unit 36 (PhoneHomeRunnerStart.cs, which refuses an unreadable HEAD);
    # PhoneHomeRunnerStartTests pins its stamped argv.
    $surfaces = @($expected.Keys | Where-Object { $_ -notlike 'scripts/*' })
    $surfaces += @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'scripts') -File -Filter '*.ps1' |
        Where-Object { $_.Name -match '^(restart|deploy|publish|release|promote)-|^run-daemon\.ps1$|^start-.*-runner\.ps1$' } |
        ForEach-Object { 'scripts/' + $_.Name })
    $findings = foreach ($surface in $surfaces) {
        $text = [IO.File]::ReadAllText((Join-Path $RepoRoot $surface))
        $ast = Read-StampAst $text $surface
        $builds = @(Get-PowerShellBuilds $ast $surface)
        foreach ($build in $builds) {
            if (-not $build.Stamped) { "${surface}: $($build.Invocation)" }
        }
        if ($expected.Contains($surface)) {
            if ($builds.Count -lt $expected[$surface]) { "${surface}: detected $($builds.Count) product builds, expected at least $($expected[$surface])" }
            $headReads = @($ast.FindAll({
                param($node)
                $node -is [CommandAst] -and -not (Test-DeadBranch $node) -and $node.GetCommandName() -eq 'git' -and
                    $node.CommandElements.Count -ge 3 -and
                    $node.CommandElements[-2].Value -ceq 'rev-parse' -and $node.CommandElements[-1].Value -ceq 'HEAD'
            }, $true))
            if ($headReads.Count -eq 0) { "${surface}: no git rev-parse HEAD invocation" }
            $validation = @($ast.FindAll({
                param($node)
                $node -is [BinaryExpressionAst] -and -not (Test-DeadBranch $node) -and
                    $node.Left -is [VariableExpressionAst] -and $node.Left.VariablePath.UserPath -ceq 'sourceRevision' -and
                    $node.ErrorPosition.Text -in @('-match', '-notmatch') -and $node.Right.Value -ceq '^[0-9a-f]{40}$'
            }, $true))
            if ($validation.Count -eq 0) { "$($surface): no 40-hex sourceRevision validation" }
        }
    }
    if (@($findings).Count -gt 0) { throw ("Unstamped PowerShell product builds:`n" + ($findings -join "`n")) }
    return
}

$decoys = [ordered]@{
    comment = 'dotnet build server # -p:SourceRevisionId=$sourceRevision'
    quoted = 'Invoke-Expression "dotnet publish server"'
    child = 'Start-Process pwsh -ArgumentList "-Command", "dotnet publish server"'
    detached = 'Start-Process dotnet -ArgumentList "publish server"'
    forwarded = '& "./build-slot.ps1" -Label fixture -- dotnet build -p:SourceRevisionId=$sourceRevision'
    logged = 'dotnet pack server; Write-Host "-p:SourceRevisionId=$sourceRevision"'
    literal = 'dotnet build server ''-p:SourceRevisionId=$sourceRevision'''
    splat = '$buildArgs = @("build", "server"); if ($false) { $buildArgs += "-p:SourceRevisionId=$sourceRevision" }; dotnet @buildArgs'
}
$accepted = @'
dotnet build server "-p:SourceRevisionId=$sourceRevision"
Invoke-Expression 'dotnet publish server -p:SourceRevisionId=$sourceRevision'
$buildArgs = @('build', 'server')
$buildArgs += "-p:SourceRevisionId=$sourceRevision"
dotnet @buildArgs
Start-Process dotnet -ArgumentList @('publish', 'server', "-p:SourceRevisionId=$sourceRevision")
& './build-slot.ps1' -Label fixture -- dotnet build "-p:SourceRevisionId=$sourceRevision"
Open-Tab 'server' 'server' 'dotnet run "-p:SourceRevisionId=$sourceRevision"'
if ($false) { dotnet build example }
Write-Host "dotnet build example"
'@
$missed = @($decoys.Keys | Where-Object { @(Get-UnstampedPowerShellBuild $decoys[$_]).Count -ne 1 })
if ($missed.Count -gt 0) { throw "Unstamped build missed: $($missed -join ', ')" }
$falsePositives = @(Get-UnstampedPowerShellBuild $accepted)
if ($falsePositives.Count -ne 0) { throw "Valid input rejected: $($falsePositives -join ', ')" }
$acceptedBuilds = @(Get-PowerShellBuilds (Read-StampAst $accepted 'fixture') 'fixture')
if ($acceptedBuilds.Count -ne 6) { throw "Expected six real fixture builds, found $($acceptedBuilds.Count)" }
