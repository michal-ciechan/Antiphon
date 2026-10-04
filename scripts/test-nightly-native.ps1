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
. (Join-Path $PSScriptRoot 'lib/nightly-policy.ps1')
Add-Type -LiteralPath ([IO.Path]::ChangeExtension($FixtureExecutable, '.dll'))
$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force
$script:owners = @()
$script:handles = @()
$script:roots = @()
$script:passed = 0
$script:parentEnvironment = @{}
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
    # Control identity travels independently of argv, so a quoting defect reaches
    # the literal-token assertion rather than breaking fixture startup.
    $owner = Start-NightlyNativeOwner -FilePath $FixtureExecutable -ArgumentList $Tokens `
        -WorkingDirectory $root -TimeoutMilliseconds 30000 -LogPath $log -Hooks $hooks `
        -Environment @{ C1039_FIXTURE_DIRECTORY=$root; C1039_FIXTURE_MODE=$Mode }
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
function Set-EnvironmentSentinel([string]$Name, [string]$Value) {
    if (-not $script:parentEnvironment.ContainsKey($Name)) {
        $script:parentEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process')
    }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}
function Assert-Environment([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw ('FAIL C1045 ' + $Label) }
    $script:passed++
    Write-Host ('PASS C1045 ' + $Label)
}
function Read-ChildEnvironment([string]$Name, [string[]]$Names,
                               [hashtable]$Environment = $null, [switch]$OmitEnvironment) {
    $root = Join-Path $ResultsDirectory $Name
    $null = New-Item -ItemType Directory -Path $root
    $log = Join-Path $root 'entry.log'
    $launch = @{ FilePath=$FixtureExecutable; ArgumentList=(@($root,'environment') + $Names)
        WorkingDirectory=$root; TimeoutMilliseconds=30000; LogPath=$log }
    if (-not $OmitEnvironment) { $launch.Environment = $Environment }
    # This is the production entry, with no controlled-I/O or native-operation seams.
    $result = Invoke-NightlyOwnedProcess @launch
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'result.json')
    Assert-Native ($result.ExitCode -eq 0 -and $result.Error -eq '') 'environment fixture exit zero'
    Assert-Native ($result.ChildrenExited -and $result.OutputDrained -and $result.CleanupComplete -and
        (Wait-NightlyOwnedCleanup $result)) 'environment fixture cleanup complete'
    $text = Get-Content -LiteralPath $log -Raw
    Assert-Native ($text.Contains('ROOT-STDOUT-END') -and $text.Contains('ROOT-STDERR-END')) 'environment fixture both final output sentinels'
    $observation = Join-Path $root 'environment.json'
    Assert-Native (Test-Path -LiteralPath $observation) 'environment fixture observation exists'
    return @(Get-Content -LiteralPath $observation -Raw | ConvertFrom-Json)
}
function Find-EnvironmentEntries($Observation, [string]$Name) {
    return @($Observation | Where-Object { $_.Name -ieq $Name })
}
function Assert-ParentEnvironment {
    Assert-Environment ([Environment]::GetEnvironmentVariable('C1045_PARENT_ONLY','Process') -ceq 'parent-only-value') 'parent-only sentinel unchanged'
    Assert-Environment ([Environment]::GetEnvironmentVariable('C1045_OVERRIDE','Process') -ceq 'parent-override-value') 'parent override unchanged'
    Assert-Environment ([Environment]::GetEnvironmentVariable('C1045_EMPTY','Process') -ceq 'parent-empty-value') 'parent empty sentinel unchanged'
    Assert-Environment ([Environment]::GetEnvironmentVariable('C1045_NULL','Process') -ceq 'parent-null-value') 'parent null sentinel unchanged'
}
$exitCode = 0
try {
    if ($Case.StartsWith('C1045_')) {
        $script:NightlySeams = $null
        Set-EnvironmentSentinel 'C1045_PARENT_ONLY' 'parent-only-value'
        Set-EnvironmentSentinel 'C1045_OVERRIDE' 'parent-override-value'
        Set-EnvironmentSentinel 'C1045_EMPTY' 'parent-empty-value'
        Set-EnvironmentSentinel 'C1045_NULL' 'parent-null-value'
    }
    switch ($Case) {
        'C1045_NullEnvironmentInherits' {
            foreach ($omit in @($true,$false)) {
                $observed = @(Read-ChildEnvironment ('null-'+$omit) @('C1045_PARENT_ONLY') -Environment $null -OmitEnvironment:$omit)
                $entries = @(Find-EnvironmentEntries $observed 'C1045_PARENT_ONLY')
                Assert-Environment ($entries.Count -eq 1 -and $entries[0].Value -ceq 'parent-only-value') 'null inherits parent sentinel'
                Assert-ParentEnvironment
            }
        }
        'C1045_SuppliedEnvironmentReplaces' {
            $value = 'supplied spaces=unicode ' + [char]0x03A9
            $observed = @(Read-ChildEnvironment 'replacement' @('C1045_PARENT_ONLY','C1045_OVERRIDE','C1045_NEW') `
                -Environment @{ c1045_override='child-override-value'; C1045_NEW=$value })
            Assert-Environment (@(Find-EnvironmentEntries $observed 'C1045_PARENT_ONLY').Count -eq 0) 'supplied map excludes parent sentinel'
            $entries = @(Find-EnvironmentEntries $observed 'C1045_OVERRIDE')
            Assert-Environment ($entries.Count -eq 1 -and $entries[0].Value -ceq 'child-override-value') 'case-insensitive single override exact value'
            $entries = @(Find-EnvironmentEntries $observed 'C1045_NEW')
            Assert-Environment ($entries.Count -eq 1 -and $entries[0].Value -ceq $value) 'new value preserves spaces equals and Unicode'
            Assert-ParentEnvironment
        }
        'C1045_EmptyEnvironmentDoesNotInherit' {
            $observed = @(Read-ChildEnvironment 'empty' @('C1045_PARENT_ONLY','C1045_OVERRIDE') -Environment @{})
            Assert-Environment (@(Find-EnvironmentEntries $observed 'C1045_PARENT_ONLY').Count -eq 0) 'empty map excludes parent sentinel'
            Assert-Environment (@(Find-EnvironmentEntries $observed 'C1045_OVERRIDE').Count -eq 0) 'empty map excludes parent override'
            Assert-ParentEnvironment
        }
        'C1045_ClearedEntriesAreAbsent' {
            $observed = @(Read-ChildEnvironment 'cleared' @('C1045_EMPTY','C1045_NULL','C1045_KEEP') `
                -Environment @{ C1045_EMPTY=''; C1045_NULL=$null; C1045_KEEP='retained-value' })
            Assert-Environment (@(Find-EnvironmentEntries $observed 'C1045_EMPTY').Count -eq 0 -and
                @(Find-EnvironmentEntries $observed 'C1045_NULL').Count -eq 0) 'cleared names absent from child block'
            $entries = @(Find-EnvironmentEntries $observed 'C1045_KEEP')
            Assert-Environment ($entries.Count -eq 1 -and $entries[0].Value -ceq 'retained-value') 'retained value exact'
            $observed = @(Read-ChildEnvironment 'all-cleared' @('C1045_PARENT_ONLY','C1045_EMPTY','C1045_NULL') `
                -Environment @{ C1045_EMPTY=''; C1045_NULL=$null })
            Assert-Environment ($observed.Count -eq 0) 'all-cleared map excludes parent and cleared names'
            Assert-ParentEnvironment
            Set-EnvironmentSentinel 'ANTIPHON_HEADED_TESTS' '1'
            $policyPath = Get-NightlyPolicyPath -RepoRoot (Split-Path $PSScriptRoot -Parent)
            $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
            $map = Get-NightlySafeChildEnvironment -PolicyObject $policy -SuiteId 'antiphon'
            $cleared = @($policy.safeEnvironment.clear | Where-Object { [string]$map[$_] -eq '' })
            Assert-Native ($cleared.Count -gt 0) 'real policy clear list admitted'
            $observed = @(Read-ChildEnvironment 'policy' (@('C1045_PARENT_ONLY') + $cleared) -Environment $map)
            foreach ($name in $cleared) {
                Assert-Environment (@(Find-EnvironmentEntries $observed $name).Count -eq 0) ('policy cleared name absent '+$name)
            }
            $entries = @(Find-EnvironmentEntries $observed 'C1045_PARENT_ONLY')
            Assert-Environment ($entries.Count -eq 1 -and $entries[0].Value -ceq 'parent-only-value') 'full policy map retains unrelated parent sentinel'
            Assert-Environment ([Environment]::GetEnvironmentVariable('ANTIPHON_HEADED_TESTS','Process') -ceq '1') 'parent headed opt-in unchanged'
            Assert-ParentEnvironment
        }
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
            Wait-Native { $r.Owner.Completion.IsCompleted -or (Test-Path (Join-Path $r.Root 'started')) } 'assignment refusal verdict'
            Assert-Native (-not (Test-Path (Join-Path $r.Root 'started'))) 'assignment refusal fixture marker absent'
            $result = $r.Owner.Completion.GetAwaiter().GetResult()
            Assert-Native ($result.ExitCode -ne 0 -and $result.Error -match 'AssignProcessToJobObject') 'assignment failure refuses'
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
            $script:NightlySeams = @{ StartProcess = { } }
            Assert-Native (Wait-NightlyOwnedCleanup ([pscustomobject]@{ ChildrenExited=$true })) 'legacy controlled IO decision seam remains compatible'
            Assert-Native (-not (Wait-NightlyOwnedCleanup ([pscustomobject]@{ ChildrenExited=$true; CleanupComplete=$false }))) 'explicit incomplete cleanup remains false in controlled IO'
            $script:NightlySeams = $null
        }
        default { throw ('Unknown native case: '+$Case) }
    }
} catch {
    $exitCode = 1
    Write-Host ($_ | Out-String)
} finally {
    foreach ($name in $script:parentEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $script:parentEnvironment[$name], 'Process')
    }
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
