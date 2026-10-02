# CARD-0973 jq precondition regression. Run through build-slot.ps1.
param([Parameter(Mandatory)][ValidateSet('absent', 'present', 'missing-shell', 'failing-shell')][string]$Case, [switch]$KeepTemp)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidenceRoot = Join-Path $root ('.antiphon/c973-jq-' + [guid]::NewGuid().ToString('N'))
$evidenceDirectory = New-Item -ItemType Directory -Path $evidenceRoot
$psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
foreach ($arg in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-deploy-server2.ps1'))) {
    [void]$psi.ArgumentList.Add($arg)
}
$psi.Environment['C973_TEST_ROOT'] = $evidenceRoot
[void]$psi.Environment.Remove('C973_JQ_PROBE_SHELL')
if ($Case -eq 'absent') { $psi.Environment['C973_TEST_JQ_PROBE'] = 'missing' }
else { [void]$psi.Environment.Remove('C973_TEST_JQ_PROBE') }
if ($Case -eq 'missing-shell') {
    $psi.Environment['C973_JQ_PROBE_SHELL'] = Join-Path $evidenceRoot 'nonexistent-shell'
} elseif ($Case -eq 'failing-shell') {
    # A real application on both platforms: git -c 'command -v jq' exits nonzero.
    $psi.Environment['C973_JQ_PROBE_SHELL'] = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}
$proc = [System.Diagnostics.Process]::Start($psi)
$stdout = $proc.StandardOutput.ReadToEndAsync()
$stderr = $proc.StandardError.ReadToEndAsync()
$proc.WaitForExit()
$output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
$output | Write-Output
[IO.File]::WriteAllText((Join-Path $evidenceRoot 'harness.log'), $output)
$assertions = 0
function Assert-Jq {
    param([bool]$Condition, [string]$Name)
    $script:assertions++
    if (-not $Condition) { throw "FAIL C973_JQ $Case $Name (evidence=$evidenceRoot)" }
}
try {
    Assert-Jq ($proc.ExitCode -eq 0) "harness exit=$($proc.ExitCode)"
    Assert-Jq ([regex]::Matches($output, '(?m)^C973_JQ_PROBE ').Count -eq 1) 'one jq probe'
    $jqAvailable = $output.Contains('C973_JQ_PROBE available=True')
    $expectPresent = $Case -eq 'present' -and $jqAvailable
    if ($Case -eq 'present' -and -not $jqAvailable) {
        Write-Output 'C973_JQ_SKIPPED present jq-missing: real probe unavailable; present proof needs jq'
    }
    foreach ($number in (@(1..19) + @(21..24))) {
        Assert-Jq ([regex]::Matches($output, "(?m)^PASS T-$number ").Count -eq 1) "T-$number passed"
    }
    $states = @(Get-ChildItem -LiteralPath $evidenceRoot -Filter state.json -Recurse | ForEach-Object {
        Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
    })
    Assert-Jq ($states.Count -eq $(if ($expectPresent) { 66 } else { 62 })) 'invocation evidence count'
    $markerStates = @($states | Where-Object { $_.PSObject.Properties.Name -contains 'markerPath' })
    $ordinaryStates = @($states | Where-Object { $_.scenario -notlike 'cold-*' })
    Assert-Jq ($ordinaryStates.Count -eq 62 -and @($ordinaryStates | Where-Object {
        $_.PSObject.Properties.Name -contains 'markerPath'
    }).Count -eq 0) 'T-1..T-19 never receive a marker'
    if (-not $expectPresent) {
        Assert-Jq ($output.Contains('C973_SKIPPED jq-missing: T-20 marker-reader groups need jq (CARD-0927)')) 'named skip notice'
        Assert-Jq (-not $output.Contains('PASS T-20 ')) 'T-20 skipped'
        Assert-Jq ($output.Contains('C849_ROLLING groups=23 invocations=62 assertions=218 failures=0')) 'base roster preserved'
        Assert-Jq ($markerStates.Count -eq 0) 'no marker-reader invocation'
    } else {
        Assert-Jq (-not $output.Contains('C973_SKIPPED')) 'nothing skipped with jq'
        Assert-Jq ($output.Contains('PASS T-20 ')) 'T-20 passed'
        Assert-Jq ($output.Contains('C849_ROLLING groups=24 invocations=66 assertions=227 failures=0')) 'jq-present roster preserved'
        Assert-Jq ($markerStates.Count -eq 4 -and @($markerStates | Where-Object {
            $_.scenario -notlike 'cold-*'
        }).Count -eq 0) 'only T-20 receives a marker'
    }
    # Delete only the DirectoryInfo returned when this driver created its own root.
    # No environment-supplied or reconstructed path is used for deletion.
    $ownedRoot = [IO.Path]::GetFullPath($evidenceRoot)
    if ((Split-Path -Parent $ownedRoot) -ne [IO.Path]::GetFullPath((Join-Path $root '.antiphon')) -or
        (Split-Path -Leaf $ownedRoot) -cnotmatch '^c973-jq-[0-9a-f]{32}$' -or
        ($evidenceDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'C973TempRootInvalid' }
    if ($KeepTemp) { Write-Output "C973_TEMP kept=$ownedRoot" }
    else { $evidenceDirectory.Delete($true) }
    $retained = if ($KeepTemp) { $ownedRoot } else { 'removed' }
    Write-Output "C973_JQ case=$Case assertions=$assertions failures=0 evidence=$retained"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine("C973_JQ case=$Case assertions=$assertions failures=1 evidence=$evidenceRoot")
    exit 1
}
