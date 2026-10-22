#requires -Version 5.1
# Private fixtures, loaded only by test-release-gate.ps1.
function New-C1500Fixture {
    $root = New-C599Root
    $fx = @{ CandidateId = 'rc-fixture'; RunId = 'fixture-run'; StateRoot = (Join-Path $root 'state');
        CoordinationRoot = (Join-Path $root 'coord'); ReleaseRoot = (Join-Path $root 'release') }
    foreach ($dir in @($fx.StateRoot, $fx.CoordinationRoot, (Join-Path $fx.ReleaseRoot 'candidates/rc-fixture'))) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $state = @{ runId = $fx.RunId; candidateId = $fx.CandidateId; profile = 'rc'; ref = 'rc/fixture';
        sha = $ShaA; expectedSha = $ShaA; phase = 'Tests'; startedAt = '2026-01-01T00:00:00Z';
        lastActivityAt = '2026-01-01T00:01:00Z'; exitCode = $null; coverageComplete = $false;
        testsPassed = $false; reportDelivered = $false }
    Write-NightlyAtomicJson -Path (Join-Path $fx.StateRoot 'last-run.json') -Object $state
    $owner = @{ pid = $PID; processStartedAt = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');
        runId = $fx.RunId; lane = 'rc'; continuationId = 'fixture-continuation' }
    foreach ($path in @((Join-Path $fx.StateRoot 'run.lock'), (Join-Path $fx.CoordinationRoot 'native-run.lock'))) {
        Write-NightlyAtomicJson -Path $path -Object $owner
    }
    Write-NightlyAtomicJson -Path (Join-Path $fx.ReleaseRoot 'candidates/rc-fixture/candidate.json') -Object @{
        candidateId = $fx.CandidateId; sha = $ShaA; ref = 'rc/fixture' }
    return $fx
}

function Set-C1500Field($Path, $Name, $Value) {
    $obj = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $obj.$Name = $Value
    Write-NightlyAtomicJson -Path $Path -Object $obj
}

function Test-C1500_StatusIdentityAndObservation {
    . (Join-Path $lib 'rc-qualification-status.ps1')
    $fx = New-C1500Fixture
    $state = Join-Path $fx.StateRoot 'last-run.json'
    $native = Join-Path $fx.CoordinationRoot 'native-run.lock'
    $journal = Join-Path $fx.ReleaseRoot 'candidates/rc-fixture/candidate.json'
    $r = Get-RcQualificationStatus @fx
    Assert-C487 ($r.observationComplete -and $r.state.phase -ceq 'Tests' -and $r.state.exitCode -eq $null -and
        $r.state.testsPassed -is [bool] -and -not $r.state.testsPassed -and $r.locks.native.ownerObservation.status -ceq 'alive') 'C1500 matching-live'
    foreach ($row in @(
        @('runId','foreign','foreign-state-run'), @('candidateId','foreign','foreign-state-candidate'),
        @('profile','nightly','foreign-profile'), @('sha',$ShaB,'foreign-sha'), @('testsPassed','false','string-boolean'))) {
        $old = (Get-Content $state -Raw | ConvertFrom-Json).($row[0])
        Set-C1500Field $state $row[0] $row[1]
        $r = Get-RcQualificationStatus @fx
        Assert-C487 (-not $r.observationComplete) ('C1500 ' + $row[2])
        Set-C1500Field $state $row[0] $old
    }
    Set-C1500Field $state 'sha' ''
    Assert-C487 ((Get-RcQualificationStatus @fx).observationComplete) 'C1500 early-expected-sha'
    Set-C1500Field $state 'sha' $ShaA
    Set-C1500Field $state 'exitCode' 1
    Set-C1500Field $state 'phase' 'Failed'
    $r = Get-RcQualificationStatus @fx
    Assert-C487 ($r.observationComplete -and $r.state.exitCode -eq 1 -and -not $r.state.testsPassed) 'C1500 completed-failure'
    foreach ($status in @('absent','reused','unknown')) {
        $observer = { param($owner) [pscustomobject]@{ status = $status; reason = 'fixture' } }.GetNewClosure()
        $r = Get-RcQualificationStatus @fx -ObserveProcess $observer
        Assert-C487 ($r.locks.run.ownerObservation.status -ceq $status -and
            ($status -ne 'unknown' -or -not $r.observationComplete)) ('C1500 ' + $status + '-owner')
    }
    Set-C1500Field $native 'runId' 'other'
    $r = Get-RcQualificationStatus @fx
    Assert-C487 ($r.locks.native.relationship -ceq 'other-run' -and $r.locks.native.record.runId -ceq 'other') 'C1500 foreign-native-lock'
    Set-C1500Field $native 'runId' $fx.RunId
    Set-C1500Field $native 'processStartedAt' $null
    Assert-C487 ((Get-RcQualificationStatus @fx).locks.native.ownerObservation.status -ceq 'unknown') 'C1500 missing-owner-start'
    Remove-Item -LiteralPath $journal
    $r = Get-RcQualificationStatus @fx
    Assert-C487 ($r.inputs.journal.status -ceq 'missing' -and $r.state.runId -ceq $fx.RunId) 'C1500 optional-journal-missing'
    foreach ($field in @('candidateId','ref','sha')) {
        Write-NightlyAtomicJson -Path $journal -Object @{ candidateId = $fx.CandidateId; ref = 'rc/fixture'; sha = $ShaA }
        Set-C1500Field $journal $field 'foreign'
        $r = Get-RcQualificationStatus @fx
        Assert-C487 ($r.identityMismatches -contains ('journal-' + $field)) ('C1500 journal-' + $field)
    }
    Remove-Item -LiteralPath $state
    $r = Get-RcQualificationStatus @fx
    Assert-C487 (-not $r.observationComplete -and $r.inputs.state.status -ceq 'missing') 'C1500 missing-state'
    [IO.File]::WriteAllText($state, '{broken')
    Assert-C487 ((Get-RcQualificationStatus @fx).inputs.state.status -ceq 'malformed') 'C1500 malformed-state'
    [IO.File]::WriteAllText($state, ('x' * (1048576 + 1)))
    Assert-C487 ((Get-RcQualificationStatus @fx).inputs.state.reason -ceq 'too-large') 'C1500 bounded-read'
    [IO.File]::WriteAllText($state, '{}')
    $change = { param($path) [IO.File]::WriteAllText($path, '{"changed":true}') }
    Assert-C487 ((Read-RcQualificationRecord -Path $state -AfterRead $change).status -ceq 'changed') 'C1500 changed-record'
    $stream = [IO.File]::Open($state, 'Open', 'ReadWrite', 'None')
    try { Assert-C487 ((Read-RcQualificationRecord $state).status -ceq 'unreadable') 'C1500 unreadable-record' }
    finally { $stream.Dispose() }
    $r = Get-RcQualificationStatus @fx -ObserveProcess { throw 'private error detail' }
    Assert-C487 ($r.locks.run.ownerObservation.status -ceq 'unknown' -and ($r | ConvertTo-Json -Depth 12) -notmatch 'private error detail') 'C1500 probe-error-redacted'
    $bad = @{} + $fx; $bad.CandidateId = '../outside'
    Assert-C487 ((Get-RcQualificationStatus @bad).reason -ceq 'invalid-input') 'C1500 invalid-selection'
    Remove-Item -LiteralPath $state
    New-Item -ItemType Directory -Path $state | Out-Null
    Assert-C487 ((Read-RcQualificationRecord $state).status -ceq 'unsupported-path') 'C1500 non-file-refused'
}

function Get-C1500Inventory($Root) {
    @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $hash = ''; if (-not $_.PSIsContainer) { $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        $_.FullName + '|' + $_.LastWriteTimeUtc.Ticks + '|' + $hash
    }) -join "`n"
}

function Test-C1500_StatusDoesNotMutate {
    $fx = New-C1500Fixture
    $root = Split-Path $fx.StateRoot -Parent
    [IO.File]::WriteAllText((Join-Path $root 'master-sentinel'), 'retain me')
    foreach ($path in @((Join-Path $fx.StateRoot 'run.lock'), (Join-Path $fx.CoordinationRoot 'native-run.lock'))) {
        Set-C1500Field $path 'pid' 2147483647
    }
    $before = Get-C1500Inventory $root
    $script:C1500Actions = @()
    # Intercept action commands in the real entrypoint's inherited scope.
    function Start-Process { $script:C1500Actions += 'start'; throw 'forbidden' }
    function Stop-Process { $script:C1500Actions += 'stop'; throw 'forbidden' }
    function Remove-Item { $script:C1500Actions += 'remove'; throw 'forbidden' }
    function Invoke-RestMethod { $script:C1500Actions += 'network'; throw 'forbidden' }
    $r = & (Join-Path $here 'rc-qualification-status.ps1') @fx -PassThru
    Assert-C487 ($r.state.runId -ceq $fx.RunId -and $r.locks.run.ownerObservation.status -ceq 'absent') 'C1500 real-entrypoint'
    Assert-C487 ((Get-C1500Inventory $root) -ceq $before) 'C1500 input-bytes-and-times-preserved'
    Assert-C487 ($script:C1500Actions.Count -eq 0) 'C1500 no-action-commands'
}

function Test-C1500_WindowsSharedReadAndPs51 {
    if ($env:OS -ne 'Windows_NT') { throw 'Native Windows fixture required; never a passing skip.' }
    $fx = New-C1500Fixture
    $entry = Join-Path $here 'rc-qualification-status.ps1'
    $ascii = $true
    foreach ($path in @($entry, (Join-Path $lib 'rc-qualification-status.ps1'))) {
        foreach ($b in [IO.File]::ReadAllBytes($path)) { if ($b -gt 127) { $ascii = $false } }
    }
    Assert-C487 $ascii 'C1500 production-ascii'
    $native = Join-Path $fx.CoordinationRoot 'native-run.lock'
    $before = [IO.File]::ReadAllBytes($native)
    $stream = [IO.File]::Open($native, 'Open', 'Write', 'Read')
    try {
        foreach ($hostName in @('powershell','pwsh')) {
            $hostPath = (Get-Command $hostName -CommandType Application -ErrorAction Stop).Source
            $version = & $hostPath -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
            $versionOk = ($LASTEXITCODE -eq 0) -and (($hostName -eq 'powershell' -and $version -match '^5\.1\.') -or
                ($hostName -eq 'pwsh' -and ([version]$version).Major -ge 7))
            $json = & $hostPath -NoProfile -File $entry -CandidateId $fx.CandidateId -RunId $fx.RunId -StateRoot $fx.StateRoot -CoordinationRoot $fx.CoordinationRoot -ReleaseRoot $fx.ReleaseRoot
            $exitCode = $LASTEXITCODE
            $r = $json | ConvertFrom-Json
            Assert-C487 ($versionOk -and $exitCode -eq 0 -and $r.observationComplete -and $r.inputs.nativeLock.status -ceq 'ok' -and
                $r.locks.native.ownerObservation.status -ceq 'alive' -and $r.state.sha -ceq $ShaA) ('C1500 ' + $hostName + '-shared-read')
        }
    } finally { $stream.Dispose() }
    Assert-C487 ([Convert]::ToBase64String([IO.File]::ReadAllBytes($native)) -ceq [Convert]::ToBase64String($before)) 'C1500 native-bytes-preserved'
}
