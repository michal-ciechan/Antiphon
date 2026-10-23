#requires -Version 7.0
# CARD-1506: real wrapper/child, fixture slot broker, no build or live process control.
param(
    [ValidateSet('All', 'ProcessAbsent', 'ProcessOptOutOne', 'ProcessOptOutZero',
        'ProcessOptOutEmpty', 'ProcessConflicting', 'StringDictionaryAbsent',
        'StringDictionaryMixedCase')]
    [string]$Case = 'All'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1506-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$names = @('UseSharedCompilation', 'DOTNET_gcServer', 'DOTNET_gcConcurrent',
    'DOTNET_CLI_USE_MSBUILD_SERVER', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER')
$expected = @('false', '0', '0', '0', '1', '1')
$probe = Join-Path $scratch 'probe.ps1'
$caller = Join-Path $scratch 'caller.ps1'
@'
$names = @('UseSharedCompilation', 'DOTNET_gcServer', 'DOTNET_gcConcurrent',
    'DOTNET_CLI_USE_MSBUILD_SERVER', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER', 'C1506_UNRELATED')
$values = @{}
foreach ($name in $names) { $values[$name] = [Environment]::GetEnvironmentVariable($name) }
'PROBE ' + ($values | ConvertTo-Json -Compress)
'@ | Set-Content -LiteralPath $probe
@'
param([string]$Root, [string]$Probe, [bool]$Windows)
Set-Variable -Name IsWindows -Value $Windows -Force
$before = [Environment]::GetEnvironmentVariables()
& (Join-Path $Root 'scripts/build-slot.ps1') -Label c1506-fixture -- pwsh -NoProfile -File $Probe
foreach ($name in @('UseSharedCompilation', 'DOTNET_gcServer', 'DOTNET_gcConcurrent',
    'DOTNET_CLI_USE_MSBUILD_SERVER', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER')) {
    if ([Environment]::GetEnvironmentVariable($name) -cne $before[$name]) { throw "wrapper failed to restore $name" }
}
'@ | Set-Content -LiteralPath $caller
$before = @{}
foreach ($name in $names) { $before[$name] = [Environment]::GetEnvironmentVariable($name) }
$failed = 0; $passed = 0
try {
    # CARD-1525: ProcessStartInfo exposes a StringDictionary adapter over a real
    # Dictionary. Unlike a standalone StringDictionary, a missing indexer throws.
    # Keep absence distinct from the old fixtures' explicitly empty opt-out.
    . (Join-Path $root 'scripts/lib/build-driver-environment.ps1')
    Add-Type -Path (Join-Path $root 'scripts/lib/BuildDriverEnvironment.cs')
    $policy = New-Object Antiphon.Build.BuildDriverEnvironment
    $regressionPassed = 0
    foreach ($regression in @(
        @{ Name = 'ProcessAbsent'; OptOut = $null; Seed = $false },
        @{ Name = 'ProcessOptOutOne'; OptOut = '1'; Seed = $true },
        @{ Name = 'ProcessOptOutZero'; OptOut = '0'; Seed = $true },
        @{ Name = 'ProcessOptOutEmpty'; OptOut = ''; Seed = $true },
        @{ Name = 'ProcessConflicting'; OptOut = $null; Seed = $true },
        @{ Name = 'StringDictionaryAbsent'; OptOut = $null; Seed = $false },
        @{ Name = 'StringDictionaryMixedCase'; OptOut = '1'; Seed = $true }
    )) {
        if ($Case -ne 'All' -and $Case -ne $regression.Name) { continue }
        $standalone = $regression.Name.StartsWith('StringDictionary')
        $paths = if ($standalone) { @('direct') } else { @('direct', 'adapter') }
        foreach ($path in $paths) {
            $info = [Diagnostics.ProcessStartInfo]::new()
            $info.Environment.Clear()
            $environment = if ($standalone) { [Collections.Specialized.StringDictionary]::new() }
                else { $info.EnvironmentVariables }
            $environment['C1525_UNRELATED'] = 'preserved'
            $optOutName = if ($standalone) { 'antiphon_build_memory_policy_optout' }
                else { 'ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT' }
            if ($null -ne $regression.OptOut) { $environment[$optOutName] = $regression.OptOut }
            if ($regression.Seed) {
                foreach ($name in $names) { $environment[$name] = 'conflicting-caller-value' }
            }
            $snapshot = @{}
            foreach ($name in $environment.Keys) { $snapshot[$name] = $environment[$name] }
            if ($path -eq 'adapter') {
                Set-AntiphonBuildDriverEnvironment -StartInfo $info -Windows $true
            } else {
                $policy.Apply($environment, $true)
            }
            if ($regression.OptOut -ceq '1') {
                if ($environment.Count -ne $snapshot.Count) { throw "$($regression.Name)/$path changed environment count" }
                foreach ($name in $snapshot.Keys) {
                    if (-not $environment.ContainsKey($name) -or $environment[$name] -cne $snapshot[$name]) {
                        throw "$($regression.Name)/$path changed opted-out environment"
                    }
                }
            } else {
                for ($i = 0; $i -lt $names.Count; $i++) {
                    if (-not $environment.ContainsKey($names[$i]) -or $environment[$names[$i]] -cne $expected[$i]) {
                        throw "$($regression.Name)/$path wrong $($names[$i])"
                    }
                }
                $expectedCount = 7 + [int]($null -ne $regression.OptOut)
                if ($environment.Count -ne $expectedCount) { throw "$($regression.Name)/$path changed unrelated keys" }
            }
            if ($environment['C1525_UNRELATED'] -cne 'preserved') { throw "$($regression.Name)/$path changed unrelated value" }
            if ($null -eq $regression.OptOut) {
                if ($environment.ContainsKey($optOutName)) { throw "$($regression.Name)/$path created opt-out" }
            } elseif (-not $environment.ContainsKey($optOutName) -or $environment[$optOutName] -cne $regression.OptOut) {
                throw "$($regression.Name)/$path changed opt-out"
            }
        }
        $regressionPassed++
        Write-Host "PASS $($regression.Name) paths=$($paths.Count)"
    }
    $regressionExpected = if ($Case -eq 'All') { 7 } else { 1 }
    if ($regressionPassed -ne $regressionExpected) { throw 'regression selection executed the wrong case count' }
    Write-Host "C1525 cases=$regressionPassed passed=$regressionPassed failed=0"
    if ($Case -ne 'All') { return }
    foreach ($case in @(
        @{ Name = 'windows-default'; Windows = $true; Seed = $false; OptOut = '' },
        @{ Name = 'windows-conflicting'; Windows = $true; Seed = $true; OptOut = '' },
        @{ Name = 'windows-optout'; Windows = $true; Seed = $true; OptOut = '1' },
        @{ Name = 'windows-optout-zero'; Windows = $true; Seed = $true; OptOut = '0' },
        @{ Name = 'linux-absent'; Windows = $false; Seed = $false; OptOut = '' },
        @{ Name = 'linux-preserve'; Windows = $false; Seed = $true; OptOut = '' }
    )) {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.WorkingDirectory = $root
        foreach ($arg in @('-NoProfile', '-File', $caller, '-Root', $root, '-Probe', $probe,
            ('-Windows:$' + ([string]$case.Windows).ToLowerInvariant()))) { $start.ArgumentList.Add($arg) }
        foreach ($name in $names) {
            [void]$start.Environment.Remove($name)
            if ($case.Seed) { $start.Environment[$name] = 'caller-value' }
        }
        $start.Environment['ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'] = $case.OptOut
        $start.Environment['C1506_UNRELATED'] = 'preserved'
        $start.Environment['C589_SLOT_SHIM'] = Join-Path $root 'scripts/fixtures/c589-slot-shim.ps1'
        $start.Environment['C589_SLOT_SCRIPT'] = 'granted'
        $start.Environment['C589_SLOT_LOG'] = Join-Path $scratch ($case.Name + '.log')
        [void]$start.Environment.Remove('C589_COMMAND_SHIM')
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        try {
            if (-not $process.WaitForExit(60000)) { throw 'fixture child exceeded 60 seconds' }
            $text = $stdout.GetAwaiter().GetResult()
            $errors = $stderr.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) { throw "fixture failed: $errors $text" }
            $line = @($text -split '\r?\n' | Where-Object { $_.StartsWith('PROBE ') })
            if ($line.Count -ne 1) { throw "missing unique child observation: $text $errors" }
            $actual = $line[0].Substring(6) | ConvertFrom-Json -AsHashtable
            $mismatches = @()
            for ($i = 0; $i -lt $names.Count; $i++) {
                $want = if ($case.Windows -and $case.OptOut -ne '1') { $expected[$i] }
                    elseif ($case.Seed) { 'caller-value' } else { $null }
                if ($actual[$names[$i]] -cne $want) {
                    $mismatches += ($names[$i] + ': expected [' + $want + '], observed [' + $actual[$names[$i]] + ']')
                }
            }
            if ($actual['C1506_UNRELATED'] -cne 'preserved') { $mismatches += 'unrelated environment changed' }
            if ($mismatches.Count) { $failed++; Write-Host ('FAIL ' + $case.Name + ': ' + ($mismatches -join '; ')) }
            else { $passed++; Write-Host ('PASS ' + $case.Name) }
        } finally {
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }
    }
    # The compiled consumers apply directly to ProcessStartInfo. Exercise that same
    # shared implementation too, including its own platform guard (not just the PS guard).
    $policySource = Join-Path $root 'scripts/lib/BuildDriverEnvironment.cs'
    if (Test-Path -LiteralPath $policySource) {
        Add-Type -Path $policySource
        $policy = New-Object Antiphon.Build.BuildDriverEnvironment
        foreach ($windows in @($false, $true)) {
            foreach ($optOut in @('', '1')) {
                $info = [Diagnostics.ProcessStartInfo]::new()
                $info.Environment.Clear()
                $info.Environment['ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'] = $optOut
                foreach ($name in $names) { $info.Environment[$name] = 'direct-caller' }
                $policy.Apply($info.EnvironmentVariables, $windows)
                for ($i = 0; $i -lt $names.Count; $i++) {
                    $want = if ($windows -and $optOut -ne '1') { $expected[$i] } else { 'direct-caller' }
                    if ($info.EnvironmentVariables[$names[$i]] -cne $want) {
                        throw "direct policy windows=$windows optout=$optOut wrong $($names[$i])"
                    }
                }
            }
        }
        Write-Host 'PASS direct-policy: 4 cases, 24 setting assertions'
    }
    foreach ($name in $names) {
        if ([Environment]::GetEnvironmentVariable($name) -cne $before[$name]) { throw "parent changed: $name" }
    }
    Write-Host "C1506 scenarios=6 passed=$passed failed=$failed parent=unchanged"
    if ($failed) { exit 1 }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
