# CARD-1958. Offline synthetic receipts; no build, service or live runner. ASCII only.
param([string]$Label = 'manual')
function Test-C1958PlacementSkips {
$ErrorActionPreference='Stop'
$root=Join-Path (Get-Location) ('.antiphon/c1958-manual-'+$Label)
[void][IO.Directory]::CreateDirectory($root)
$sha=(& git rev-parse HEAD).Trim()
$observation=@{commit=$sha;dirtyFiles=0;fingerprint=('a'*64);captureStatus='known'}
$source=@{version=1;start=$observation;end=$observation;state='clean';buildSource='verified'}
$failures=0
foreach($case in @('linux-skip','other-skip','windows-skip','linux-pass','unknown-skip')) {
    $skip=$case -ne 'linux-pass'; $skipped=[int]$skip; $executed=[int](-not $skip)
    $method=if($case -eq 'other-skip'){'Unlisted_test'}else{'C1210_Windows_linux_only_outcomes_are_exact'}
    $outcome=if($skip){'NotExecuted'}else{'Passed'}
    $os=if($case -eq 'windows-skip'){'Microsoft Windows 10.0.26100'}elseif($case -eq 'unknown-skip'){'unexpected'}else{'Debian GNU/Linux 12 (bookworm)'}
    $trx=Join-Path $root ($case+'.trx')
    [IO.File]::WriteAllText($trx, ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testId="placement" outcome="'+$outcome+'" /></Results><TestDefinitions><UnitTest id="placement"><TestMethod className="Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests" name="'+$method+'" /></UnitTest></TestDefinitions><ResultSummary><Counters total="1" executed="'+$executed+'" passed="'+$executed+'" failed="0" notExecuted="'+$skipped+'" /></ResultSummary></TestRun>'))
    $line='CHECKPOINT CP-1 commit='+$sha+' build=ok filter=fixture executed='+$executed+' passed='+$executed+' failed=0 skipped='+$skipped+' trx='+$trx+' slot=fixture waited=0s dirty=0 source='+$sha+' sourceState=clean buildSource=verified'
    $data=@{schemaVersion=2;exitCode=0;verdict='GREEN';cleanupRefusals=@();source=$source;host=@{os=$os};rows=@(@{id='CP-1';state='green';exitCode=0;executed=$executed;passed=$executed;failed=0;skipped=$skipped;source=$source;line=$line;trx=$trx})}
    $path=Join-Path $root ($case+'.json')
    [IO.File]::WriteAllText($path, ($data|ConvertTo-Json -Depth 20))
    $psi=[Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute=$false
    $psi.RedirectStandardOutput=$true
    $psi.RedirectStandardError=$true
    foreach($argument in @('-NoProfile','-File','scripts/validate-checkpoint-receipt.ps1','-Evidence',$path,'-ExpectedSourceSha',$sha)) {
        $psi.ArgumentList.Add($argument)
    }
    $process=[Diagnostics.Process]::Start($psi)
    $stdout=$process.StandardOutput.ReadToEndAsync()
    $stderr=$process.StandardError.ReadToEndAsync()
    try {
        if(-not $process.WaitForExit(10000)) { throw 'validator fixture deadline exceeded' }
        Write-Host $stdout.GetAwaiter().GetResult()
        Write-Host $stderr.GetAwaiter().GetResult()
        $actual=$process.ExitCode
    } finally {
        if(-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    $expected=if($case -in @('linux-skip','linux-pass')){0}else{2}
    Write-Host "MANUAL $case expected=$expected actual=$actual fixture=true source=$sha"
    if($actual -ne $expected){$failures++}
}
Write-Host "MANUAL cases=5 failed=$failures fixture=true"
if($failures){exit 1}
}
Test-C1958PlacementSkips
