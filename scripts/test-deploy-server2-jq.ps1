# CARD-0973 jq precondition regression. Run through build-slot.ps1.
param([Parameter(Mandatory)][ValidateSet('absent', 'present')][string]$Case)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidenceRoot = Join-Path $root ('.antiphon/c973-jq-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidenceRoot | Out-Null
$psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
foreach ($arg in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-deploy-server2.ps1'))) {
    [void]$psi.ArgumentList.Add($arg)
}
$psi.Environment['C973_TEST_ROOT'] = $evidenceRoot
if ($Case -eq 'absent') { $psi.Environment['C973_TEST_JQ_PROBE'] = 'missing' }
else { [void]$psi.Environment.Remove('C973_TEST_JQ_PROBE') }
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
    foreach ($number in 1..19) {
        Assert-Jq ([regex]::Matches($output, "(?m)^PASS T-$number ").Count -eq 1) "T-$number passed"
    }
    $states = @(Get-ChildItem -LiteralPath $evidenceRoot -Filter state.json -Recurse | ForEach-Object {
        Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
    })
    Assert-Jq ($states.Count -eq $(if ($Case -eq 'absent') { 55 } else { 59 })) 'invocation evidence count'
    $markerStates = @($states | Where-Object { $_.PSObject.Properties.Name -contains 'markerPath' })
    $ordinaryStates = @($states | Where-Object { $_.scenario -notlike 'cold-*' })
    Assert-Jq ($ordinaryStates.Count -eq 55 -and @($ordinaryStates | Where-Object {
        $_.PSObject.Properties.Name -contains 'markerPath'
    }).Count -eq 0) 'T-1..T-19 never receive a marker'
    if ($Case -eq 'absent') {
        Assert-Jq ($output.Contains('C973_SKIPPED jq-missing: T-20 marker-reader groups need jq (CARD-0927)')) 'named skip notice'
        Assert-Jq (-not $output.Contains('PASS T-20 ')) 'T-20 skipped'
        Assert-Jq ($output.Contains('C849_ROLLING groups=19 invocations=55 assertions=197 failures=0')) 'base roster preserved'
        Assert-Jq ($markerStates.Count -eq 0) 'no marker-reader invocation'
    } else {
        Assert-Jq (-not $output.Contains('C973_SKIPPED')) 'nothing skipped with jq'
        Assert-Jq ($output.Contains('PASS T-20 ')) 'T-20 passed'
        Assert-Jq ($output.Contains('C849_ROLLING groups=20 invocations=59 assertions=206 failures=0')) 'jq-present roster preserved'
        Assert-Jq ($markerStates.Count -eq 4 -and @($markerStates | Where-Object {
            $_.scenario -notlike 'cold-*'
        }).Count -eq 0) 'only T-20 receives a marker'
    }
    Write-Output "C973_JQ case=$Case assertions=$assertions failures=0 evidence=$evidenceRoot"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine("C973_JQ case=$Case assertions=$assertions failures=1 evidence=$evidenceRoot")
    exit 1
}
