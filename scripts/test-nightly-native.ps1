#requires -Version 7.0
# CARD-1039 S1n-a: real suspended launcher, job queries, pipes and executable.
param([Parameter(Mandatory)][string]$Case,
      [Parameter(Mandatory)][string]$ResultsDirectory,
      [Parameter(Mandatory)][string]$FixtureExecutable)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows native fixture required; this case cannot skip.' }
. (Join-Path $PSScriptRoot 'lib/nightly-common.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-owned-process.ps1')
Add-Type -LiteralPath ([IO.Path]::ChangeExtension($FixtureExecutable, '.dll'))
$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$script:owners = @()
$script:handles = @()
$script:roots = @()
$script:passed = 0
function Assert-Native([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw ('FAIL C1039 ' + $Label) }
    $script:passed++
    Write-Host ('PASS C1039 ' + $Label)
}
function Wait-Native([scriptblock]$Condition, [string]$Label) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $Condition)) {
        if ($clock.ElapsedMilliseconds -ge 10000) { throw ('fixture prerequisite not observed: ' + $Label) }
        Start-Sleep -Milliseconds 10
    }
}
function New-NativeRun([string]$Name, [string]$Mode, [string[]]$Barriers = @(),
                       [bool]$FailAssignment = $false, [bool]$ZeroCount = $false,
                       [string[]]$Tokens = @()) {
    $root = Join-Path $ResultsDirectory $Name
    $null = New-Item -ItemType Directory -Path $root
    $script:roots += $root
    $hooks = [Antiphon.Nightly.NativeProcessHooks]::new()
    $hooks.BarrierDirectory = $root; $hooks.Barriers = $Barriers
    $hooks.FailAssignment = $FailAssignment; $hooks.ZeroCountAtRootWait = $ZeroCount
    $log = Join-Path $root 'native.log'
    $owner = Start-NightlyNativeOwner -FilePath $FixtureExecutable -ArgumentList (@($root,$Mode) + $Tokens) `
        -WorkingDirectory $root -TimeoutMilliseconds 30000 -LogPath $log -Hooks $hooks
    $script:owners += $owner
    return [pscustomobject]@{ Root=$root; Owner=$owner; Log=$log }
}
function Release-Native($Run, [string]$Phase) {
    Set-Content -LiteralPath (Join-Path $Run.Root ($Phase+'.release')) -Value 'release'
}
function Open-NativeHandle($Run, [string]$Name) {
    $file = Join-Path $Run.Root ($Name+'.pid')
    Wait-Native { Test-Path -LiteralPath $file } ($Name+' pid')
    $process = [Diagnostics.Process]::GetProcessById([int](Get-Content -LiteralPath $file -Raw))
    $null = $process.Handle; $null = $process.StartTime
    $script:handles += $process
    return $process
}
function Complete-Native($Run) {
    Wait-Native { $Run.Owner.Completion.IsCompleted } 'owner completion'
    $result = $Run.Owner.Completion.GetAwaiter().GetResult()
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Run.Root 'result.json')
    Assert-Native ($result.ExitCode -eq 0 -and $result.Error -eq '') 'native exit zero'
    Assert-Native ($result.ChildrenExited -and $result.OutputDrained -and $result.CleanupComplete) 'root job and streams complete'
    return $result
}
function Assert-Held($Run, [string]$Label) {
    # A short observation interval lets an incorrectly resumed or completed worker run.
    Start-Sleep -Milliseconds 300
    Assert-Native (-not $Run.Owner.Completion.IsCompleted) ($Label+' owner has not returned')
}
$exitCode = 0
try {
    switch ($Case) {
        'C1039_AssignBeforeResume' {
            $tokens = @('path with spaces','', 'a"b', 'C:\tail\', '/*/*/(A)|(B)/*', '*', '$literal')
            $r = New-NativeRun 'suspended root' 'descendant' @('assignment') $false $false $tokens
            Wait-Native { Test-Path (Join-Path $r.Root 'assignment.entered') } 'assignment barrier'
            $rootHandle = [Diagnostics.Process]::GetProcessById($r.Owner.Observation.Pid)
            $null=$rootHandle.Handle; $script:handles += $rootHandle
            Assert-Held $r 'assignment held'
            Assert-Native (-not (Test-Path (Join-Path $r.Root 'started'))) 'assignment held root marker absent'
            Assert-Native (-not (Test-Path (Join-Path $r.Root 'child.pid'))) 'assignment held descendant absent'
            Release-Native $r 'assignment'
            $child = Open-NativeHandle $r 'child'
            $argv = @(Get-Content (Join-Path $r.Root 'argv.json') -Raw | ConvertFrom-Json)
            Assert-Native ($argv.Count -eq $tokens.Count) 'literal argv count'
            for ($i=0; $i -lt $tokens.Count; $i++) { Assert-Native ($argv[$i] -ceq $tokens[$i]) ('literal argv token '+$i) }
            Release-Native $r 'child'
            $null = Complete-Native $r
            Assert-Native ($rootHandle.WaitForExit(1000) -and $child.WaitForExit(1000)) 'independent root child handles signal'
            $r = New-NativeRun 'assignment refusal' 'descendant' @('assignment') $true
            Wait-Native { Test-Path (Join-Path $r.Root 'assignment.entered') } 'failed assignment barrier'
            $refused = [Diagnostics.Process]::GetProcessById($r.Owner.Observation.Pid)
            $null = $refused.Handle; $script:handles += $refused
            Release-Native $r 'assignment'
            Wait-Native { $r.Owner.Completion.IsCompleted } 'refused owner completion'
            $result = $r.Owner.Completion.GetAwaiter().GetResult()
            Assert-Native ($result.ExitCode -ne 0 -and $result.Error -match 'AssignProcessToJobObject') 'assignment failure refuses'
            Assert-Native (-not (Test-Path (Join-Path $r.Root 'started'))) 'assignment refusal fixture marker absent'
            Assert-Native ($refused.WaitForExit(1000)) 'assignment refusal exact root handle signalled'
        }
        'C1039_DescendantExit' {
            $r = New-NativeRun 'child held' 'descendant'
            Wait-Native { $r.Owner.Observation.Pid -gt 0 } 'root identity'
            $rootHandle = [Diagnostics.Process]::GetProcessById($r.Owner.Observation.Pid)
            $null=$rootHandle.Handle; $script:handles += $rootHandle
            $child = Open-NativeHandle $r 'child'
            Assert-Native ($rootHandle.WaitForExit(10000)) 'root exits before child'
            Assert-Held $r 'descendant held'
            Assert-Native (-not $child.HasExited -and -not $r.Owner.Observation.ChildrenExited) 'live child prevents ChildrenExited'
            Release-Native $r 'child'
            $result = Complete-Native $r
            Assert-Native ($child.WaitForExit(1000) -and $result.RemainingProcessCount -eq 0) 'independent child handle and zero job count'
            Assert-Native ($result.DescendantIdentities.Count -gt 0) 'descendant start identities recorded'
            $r = New-NativeRun 'root wait held' 'root-held' @('root-wait') $false $true
            Wait-Native { Test-Path (Join-Path $r.Root 'root-wait.entered') } 'root wait barrier'
            $rootHandle = [Diagnostics.Process]::GetProcessById($r.Owner.Observation.Pid)
            $null=$rootHandle.Handle; $script:handles += $rootHandle
            Assert-Held $r 'root wait held'
            Assert-Native ($r.Owner.Observation.RemainingProcessCount -eq 0 -and -not $rootHandle.HasExited -and -not $r.Owner.Observation.ChildrenExited) 'zero count does not replace root wait'
            Release-Native $r 'root'; Release-Native $r 'root-wait'
            $null = Complete-Native $r
            $r = New-NativeRun 'breakaway request' 'breakaway' @('assignment')
            Wait-Native { Test-Path (Join-Path $r.Root 'assignment.entered') } 'private job limit readback'
            $limits = [NativeJobObserver]::ReadLimitFlags($r.Owner.JobHandle)
            Assert-Native (($limits -band 0x1800) -eq 0) 'independent private job readback forbids both breakaway flags'
            Assert-Native (($limits -band 0x2000) -ne 0) 'independent private job readback enables kill on close'
            Release-Native $r 'assignment'
            Wait-Native { Test-Path (Join-Path $r.Root 'breakaway-result') } 'breakaway result'
            Assert-Native ((Get-Content (Join-Path $r.Root 'breakaway-result') -Raw).Trim() -eq 'refused') 'job forbids breakaway request'
            Assert-Native (-not (Test-Path (Join-Path $r.Root 'breakaway-child.pid'))) 'no independently live breakaway descendant'
            $null = Complete-Native $r
        }
        'C1039_DrainBeforeReturn' {
            foreach ($phase in @('stdout','stderr','log-write')) {
                $r = New-NativeRun ($phase+' held') 'plain' @($phase)
                Wait-Native { Test-Path (Join-Path $r.Root ($phase+'.entered')) } ($phase+' barrier')
                Assert-Held $r $phase
                Assert-Native (-not $r.Owner.Observation.OutputDrained) ($phase+' OutputDrained false')
                Assert-Native (-not $r.Owner.Observation.CleanupComplete) ($phase+' cleanup not complete')
                Release-Native $r $phase
                $null = Complete-Native $r
                $text = Get-Content -LiteralPath $r.Log -Raw
                Assert-Native ($text.Contains('ROOT-STDOUT-END') -and $text.Contains('ROOT-STDERR-END')) ($phase+' final log has both terminal sentinels')
            }
            # Ordinary production entry must consume this adapter's real custody result.
            $root = Join-Path $ResultsDirectory 'production entry'; $null=New-Item -ItemType Directory $root
            $result = Invoke-NightlyOwnedProcess -FilePath $FixtureExecutable -ArgumentList @($root,'plain') `
                -WorkingDirectory $root -TimeoutMilliseconds 30000 -LogPath (Join-Path $root 'entry.log')
            Assert-Native ($result.CleanupComplete -and (Wait-NightlyOwnedCleanup $result)) 'production entry observes complete native result'
            Assert-Native (-not (Wait-NightlyOwnedCleanup ([pscustomobject]@{ ChildrenExited=$true; CleanupComplete=$false }))) 'root only result cannot discharge cleanup'
        }
        default { throw ('Unknown native case: '+$Case) }
    }
} catch {
    $exitCode = 1
    Write-Host ($_ | Out-String)
} finally {
    # Release every fixture barrier even when the guarded assertion goes red.
    foreach ($root in $script:roots) {
        foreach ($phase in @('assignment','root-wait','stdout','stderr','log-write','root','child')) {
            Set-Content -LiteralPath (Join-Path $root ($phase+'.release')) -Value 'finally release'
        }
        # A deliberate breakaway mutant must also be joined by its exact fixture identity.
        $childFile = Join-Path $root 'breakaway-child.pid'
        if (Test-Path $childFile) {
            try { $p=[Diagnostics.Process]::GetProcessById([int](Get-Content $childFile -Raw)); $null=$p.Handle; $script:handles += $p } catch [ArgumentException] { }
        }
    }
    foreach ($owner in $script:owners) { $null = $owner.Completion.GetAwaiter().GetResult() }
    foreach ($p in $script:handles) {
        if (-not $p.WaitForExit(10000)) { $p.Kill(); $p.WaitForExit(); $exitCode=1 }
        $p.Dispose()
    }
}
Write-Host ('C1039 NATIVE '+$Case+': '+$script:passed+' passed; exit='+$exitCode)
exit $exitCode
