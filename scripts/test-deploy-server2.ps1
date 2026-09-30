# CARD-0727 V-32: offline T-1..T-6, with fake HTTP, host case and owner-only token.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$driver = Join-Path $PSScriptRoot 'deploy-server2.ps1'
$http = Join-Path $PSScriptRoot 'fixtures/c727-fake-http.ps1'
$verify = Join-Path $PSScriptRoot 'fixtures/c727-fake-verify.ps1'
$sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$tempRoot = Join-Path $root ('.antiphon/c727-v32-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$script:passed = 0

function Assert-C727 {
    param([bool]$Condition, [string]$Name)
    if (-not $Condition) { throw "FAIL $Name" }
    $script:passed++
}

function Run-C727 {
    param([string]$Scenario, [string]$Phase = 'all', [bool]$TokenPresent = $true)
    $dir = Join-Path $tempRoot ($Scenario + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $statePath = Join-Path $dir 'state.json'
    $tracePath = Join-Path $dir 'trace.jsonl'
    $tokenPath = Join-Path $dir 'operator-token'
    $sentinel = 'SENTINEL_C727_OPERATOR_TOKEN_1234567890'
    if ($TokenPresent) { Set-Content -LiteralPath $tokenPath -Value $sentinel -NoNewline }
    [ordered]@{
        scenario = $Scenario; sha = $sha; tempDeployed = ($Scenario -in @('busy', 'rerun')); oldDeployed = $false
        oldDraining = $false; tempDraining = ($Scenario -eq 'rerun')
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath
    $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $driver, '-Rolling', '-Sha', $sha, '-Phase', $Phase, '-WaitIdleMinutes', '1')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $psi.Environment['ANTIPHON_OPERATOR_TOKEN_FILE'] = $tokenPath
    $psi.Environment['C727_TEST_HTTP_STUB'] = $http
    $psi.Environment['C727_TEST_VERIFY_STUB'] = $verify
    $psi.Environment['C727_TEST_STATE'] = $statePath
    $psi.Environment['C727_TEST_TRACE'] = $tracePath
    $psi.Environment['C727_TEST_WAIT_MS'] = '100'
    $psi.Environment['C727_TEST_POLL_MS'] = '5'
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $trace = if (Test-Path -LiteralPath $tracePath) { @(Get-Content -LiteralPath $tracePath | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
    return [pscustomobject]@{ Exit = $proc.ExitCode; Out = $stdout + $stderr; Trace = $trace; Sentinel = $sentinel }
}

try {
    $t1 = Run-C727 -Scenario happy
    Assert-C727 ($t1.Exit -eq 0) 'T-1 exit'
    $names = @($t1.Trace | Where-Object { $_.kind -eq 'case' } | ForEach-Object name)
    Assert-C727 (($names -join ',') -eq 'runner-cache-seed,deploy-temp-runner,verify-runner-caches,deploy-parent,verify-runner-caches,retire-temp-runner') 'T-1 case order'
    $posts = @($t1.Trace | Where-Object { $_.kind -eq 'http' -and $_.method -eq 'POST' })
    Assert-C727 ($posts.Count -eq 5) 'T-1 post count'
    Assert-C727 ($posts[0].runnerId -eq 'server2-temp' -and $posts[0].suffix -eq '/drain' -and
        $posts[0].body.redirectTo -eq 'server2' -and $posts[0].body.retireWhenIdle -eq $true) 'T-1 temp hold body'
    Assert-C727 ($posts[1].runnerId -eq 'server2-temp' -and $posts[1].suffix -eq '/drain/clear') 'T-1 temp clear order'
    Assert-C727 ($posts[2].runnerId -eq 'server2' -and $posts[2].suffix -eq '/drain' -and
        $posts[2].body.redirectTo -eq 'server2-temp' -and $posts[2].body.retireWhenIdle -eq $false) 'T-1 old drain body'
    Assert-C727 ($posts[3].runnerId -eq 'server2' -and $posts[3].suffix -eq '/drain/clear') 'T-1 main clear order'
    Assert-C727 ($posts[4].runnerId -eq 'server2-temp' -and $posts[4].suffix -eq '/drain' -and
        $posts[4].body.redirectTo -eq 'server2' -and $posts[4].body.retireWhenIdle -eq $true) 'T-1 temp drain body'
    Write-Output 'PASS T-1 happy path and drain bodies'

    foreach ($scenario in @('missing', 'ineligible')) {
        $t2 = Run-C727 -Scenario $scenario -Phase deploy-temp
        $diagnosis = if ($scenario -eq 'missing') { 'TempRunnerStatusMissing' } else { 'TempRunnerNotEligible' }
        Assert-C727 ($t2.Exit -eq 2 -and $t2.Out.Contains($diagnosis)) "T-2 $scenario verdict"
        Assert-C727 (@($t2.Trace | Where-Object { $_.kind -eq 'http' -and $_.method -eq 'POST' -and
            $_.runnerId -eq 'server2' }).Count -eq 0) "T-2 $scenario no main drain"
    }
    Write-Output 'PASS T-2 missing and ineligible temp status'

    $t3 = Run-C727 -Scenario busy -Phase drain-old
    Assert-C727 ($t3.Exit -eq 2 -and $t3.Out.Contains('OldRunnerStillBusy')) 'T-3 busy verdict'
    Assert-C727 (@($t3.Trace | Where-Object { $_.kind -eq 'case' -and $_.name -eq 'deploy-parent' }).Count -eq 0) 'T-3 no deploy-parent'
    Assert-C727 (@($t3.Trace | Where-Object { $_.kind -eq 'http' -and $_.method -eq 'POST' -and $_.suffix -eq '/drain' }).Count -eq 1) 'T-3 drain remains'
    Write-Output 'PASS T-3 old runner busy'

    $t4 = Run-C727 -Scenario rerun -Phase drain-temp
    Assert-C727 ($t4.Exit -eq 0) 'T-4 exit'
    Assert-C727 (@($t4.Trace | Where-Object { $_.kind -eq 'http' -and $_.method -eq 'POST' }).Count -eq 0) 'T-4 no duplicate post'
    Write-Output 'PASS T-4 drain-temp rerun'

    $t5 = Run-C727 -Scenario happy -Phase deploy-temp -TokenPresent $false
    Assert-C727 ($t5.Exit -eq 2 -and $t5.Out.Contains('OperatorTokenMissing')) 'T-5 token refusal'
    Assert-C727 ($t5.Trace.Count -eq 0) 'T-5 no case or HTTP'
    Write-Output 'PASS T-5 token precondition'

    Assert-C727 (-not $t1.Out.Contains($t1.Sentinel)) 'T-6 token not printed'
    $bytes = [System.IO.File]::ReadAllBytes($driver)
    Assert-C727 (@($bytes | Where-Object { $_ -gt 127 }).Count -eq 0) 'T-6 ASCII'
    Write-Output 'PASS T-6 token output and ASCII'
    Write-Output "V-32 assertions=$script:passed failures=0"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
