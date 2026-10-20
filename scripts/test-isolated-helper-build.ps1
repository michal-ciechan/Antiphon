#requires -Version 7.0
<#
.SYNOPSIS
    CARD-1367: witness the Linux helper apphost collision, or verify its repair.
    Performs one real, leased build. Red expects the pre-fix build to fail;
    Green requires the test assembly and both complete staged helper outputs.
    Logs, source observations and the pre-cleanup inventory stay in .antiphon/.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Red', 'Green')][string]$Phase,
    [Parameter(Mandatory)][ValidatePattern('^bin-[A-Za-z0-9][A-Za-z0-9-]*/$')][string]$OutputPath,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSourceSha
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux) { throw 'This regression requires Linux extensionless apphosts.' }
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib/checkpoint-source.ps1')
$start = Get-CheckpointSource $repo
if ($start.captureStatus -ne 'known' -or $start.dirtyFiles -ne 0) {
    throw 'Commit source before running this build regression.'
}
if ($ExpectedSourceSha -and $start.commit -cne $ExpectedSourceSha) { throw 'Expected source SHA does not match HEAD.' }
if (-not $ExpectedSourceSha) { $ExpectedSourceSha = $start.commit }
$leaf = $OutputPath.TrimEnd('/')

# Walk source directories without following links or traversing other runs' outputs.
# Discover all roots, including projects introduced through imported MSBuild files.
function Get-OwnedOutputRoots {
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($repo)
    while ($pending.Count) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($entry.Name -ceq $leaf) { $entry.FullName; continue }
            if (-not $entry.PSIsContainer -or $entry.LinkType) { continue }
            if ($entry.Name -in @('.git', '.antiphon', 'node_modules', 'bin', 'obj', 'workspace') -or
                $entry.Name -like 'bin-*') { continue }
            $pending.Push($entry.FullName)
        }
    }
}

$existing = @(Get-OwnedOutputRoots)
if ($existing.Count) { throw "Output already exists; refusing reuse: $($existing -join ', ')" }
$evidence = Join-Path $repo ('.antiphon/card-1367/{0}-{1}-{2}' -f $Phase.ToLowerInvariant(),
    [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8))
[void][IO.Directory]::CreateDirectory($evidence)
$log = Join-Path $evidence 'build.log'
$checks = [Collections.Generic.List[object]]::new()
$roots = @()
$buildExit = $null
$exitCode = 1
$failure = $null
$timer = [Diagnostics.Stopwatch]::StartNew()
$source = [ordered]@{ version = 1; start = $start; end = $null; state = 'unknown'; buildSource = 'unknown' }

function Assert-BuildCheck([string]$Name, [bool]$Passed) {
    $checks.Add([pscustomobject]@{ name = $Name; passed = $Passed })
    Write-Host ('{0} {1}' -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name)
}

Push-Location $repo
try {
    Write-Host "HELPER BUILD phase=$Phase commit=$ExpectedSourceSha output=$OutputPath evidence=$evidence"
    # No apphost/artifacts/OutDir overrides: this must exercise SDK content propagation.
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-slot.ps1') -Label "card-1367-$Phase" -- `
        dotnet build tests/Antiphon.Tests "--property:OutputPath=$OutputPath" --nologo 2>&1 |
        Tee-Object -FilePath $log
    $buildExit = $LASTEXITCODE
    $source.end = Get-CheckpointSource $repo
    $source.state = Get-CheckpointSourceState $source.start $source.end
    if ($source.state -ne 'clean' -or $source.end.commit -cne $ExpectedSourceSha) { throw 'Source changed during the build.' }
    $source.buildSource = 'verified'
    $lines = @(Get-Content -LiteralPath $log)
    $slotLines = @($lines | Where-Object { $_ -match '^BUILD SLOT ' })
    $slotLines | Set-Content -LiteralPath (Join-Path $evidence 'build-slot.log')
    if ($buildExit -eq 4 -or $slotLines -match '^BUILD SLOT timeout') {
        $exitCode = 4
        throw 'Build slot timed out; no collision or green witness.'
    }
    if (-not ($slotLines -match '^BUILD SLOT granted ') -or
        -not ($slotLines -match '^BUILD SLOT released ')) { throw 'A granted and released build slot is required.' }

    $consumer = Join-Path $repo "tests/Antiphon.Tests/$OutputPath"
    if ($Phase -eq 'Red') {
        Assert-BuildCheck 'build exits nonzero' ($buildExit -ne 0)
        $destination = [regex]::Escape((Join-Path $consumer 'fakeclaude') + '/')
        Assert-BuildCheck 'MSB3021 names fakeclaude staging destination' ([bool]($lines -match "error MSB3021:.*$destination"))
        $collision = Get-Item -LiteralPath (Join-Path $consumer 'fakeclaude') -Force -ErrorAction SilentlyContinue
        Assert-BuildCheck 'consumer fakeclaude is an ordinary file' ($null -ne $collision -and -not $collision.PSIsContainer -and -not $collision.LinkType)
        $unexpected = @($lines | Where-Object { $_ -match '\berror\s+[A-Za-z]+\d+\s*:' -and $_ -notmatch '\berror MSB302[17]:' })
        if ($unexpected.Count) { throw 'Build has unrelated errors; see build.log.' }
    } else {
        Assert-BuildCheck 'build exits zero' ($buildExit -eq 0)
        foreach ($helper in @('fakeclaude', 'fakegrok')) {
            $directory = Join-Path $consumer $helper
            Assert-BuildCheck "$helper staged directory" (Test-Path -LiteralPath $directory -PathType Container)
            foreach ($suffix in @('', '.dll', '.deps.json', '.runtimeconfig.json')) {
                Assert-BuildCheck "$helper/$helper$suffix staged file" (Test-Path -LiteralPath (Join-Path $directory "$helper$suffix") -PathType Leaf)
            }
        }
        Assert-BuildCheck 'Antiphon.Tests.dll at consumer output root' (Test-Path -LiteralPath (Join-Path $consumer 'Antiphon.Tests.dll') -PathType Leaf)
    }
    if (@($checks | Where-Object { -not $_.passed }).Count) {
        if ($buildExit -ne 0) { $exitCode = $buildExit }
        throw 'Build regression assertions failed; see checks.json and build.log.'
    }
    $exitCode = 0
} catch {
    $failure = $_.Exception.Message
    Write-Host "HELPER BUILD FAIL: $failure"
} finally {
    # Only fresh directories with this invocation's validated basename are owned.
    # Inventory everything before cleanup, and refuse links instead of following them.
    try {
        $roots = @(Get-OwnedOutputRoots)
        $inventory = @(foreach ($root in $roots) {
            $entries = @((Get-Item -LiteralPath $root -Force)) + @(Get-ChildItem -LiteralPath $root -Recurse -Force)
            foreach ($entry in $entries) {
                if ($entry.LinkType) { throw "Output contains a link; retained: $($entry.FullName)" }
                [pscustomobject]@{
                    path = [IO.Path]::GetRelativePath($repo, $entry.FullName)
                    directory = $entry.PSIsContainer
                    bytes = $(if ($entry.PSIsContainer) { 0 } else { $entry.Length })
                }
            }
        })
        ConvertTo-Json -InputObject $inventory -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'inventory.json')
        foreach ($root in $roots) { Remove-Item -LiteralPath $root -Recurse -Force }
        if (@(Get-OwnedOutputRoots).Count) { throw 'Owned output cleanup is incomplete.' }
    } catch {
        $exitCode = 1
        $failure = "Cleanup: $($_.Exception.Message); earlier failure: $failure"
        Write-Host $failure
    }
    if ($null -eq $source.end) {
        $source.end = Get-CheckpointSource $repo
        $source.state = Get-CheckpointSourceState $source.start $source.end
    }
    $timer.Stop()
    ConvertTo-Json -InputObject $source -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'source.json')
    ConvertTo-Json -InputObject @($checks.ToArray()) -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'checks.json')
    $summary = [ordered]@{
        phase = $Phase; expectedSourceSha = $ExpectedSourceSha; buildExit = $buildExit; harnessExit = $exitCode
        checks = $checks.Count; passed = @($checks | Where-Object passed).Count
        elapsedSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3); outputRoots = $roots
        log = $log; failure = $failure
    }
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json')
    Write-Host "HELPER BUILD phase=$Phase buildExit=$buildExit harnessExit=$exitCode checks=$($summary.checks) passed=$($summary.passed) seconds=$($summary.elapsedSeconds) evidence=$evidence"
    Pop-Location
}
exit $exitCode
