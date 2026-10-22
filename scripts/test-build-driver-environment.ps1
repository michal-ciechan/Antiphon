#requires -Version 7.0
# CARD-1506: real wrapper/child, fixture slot broker, no build or live process control.
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
& (Join-Path $Root 'scripts/build-slot.ps1') -Label c1506-fixture -- pwsh -NoProfile -File $Probe
'@ | Set-Content -LiteralPath $caller
$before = @{}
foreach ($name in $names) { $before[$name] = [Environment]::GetEnvironmentVariable($name) }
$failed = 0; $passed = 0
try {
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
    foreach ($name in $names) {
        if ([Environment]::GetEnvironmentVariable($name) -cne $before[$name]) { throw "parent changed: $name" }
    }
    Write-Host "C1506 scenarios=6 passed=$passed failed=$failed parent=unchanged"
    if ($failed) { exit 1 }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
