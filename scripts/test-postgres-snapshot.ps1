#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1405 witness for scripts/postgres-snapshot.ps1.

    No Docker, no production database, no OneDrive path. -Case runs one case;
    the default runs every case. ASCII-only.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$scriptPath = Join-Path $here 'postgres-snapshot.ps1'
$script:passed = 0
$script:failed = 0
$script:failures = New-Object System.Collections.Generic.List[string]

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures.Add("$Name : $Detail")
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name } else { Write-Fail $Name $Detail }
}

function New-Fixture {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1405-' + [guid]::NewGuid().ToString('N'))
    $sync = Join-Path $root 'sync'
    $dest = Join-Path $sync 'antiphon-postgres'
    $stage = Join-Path $root 'staging'
    $bin = Join-Path $root 'bin'
    $marker = Join-Path $root 'tool-called'
    New-Item -ItemType Directory -Path $sync, $dest, $stage, $bin -Force | Out-Null
    $canary = 'CANARY-RECIPIENT-VALUE-1405'
    $identityCanary = 'CANARY-IDENTITY-VALUE-1405'
    $recipient = Join-Path $root 'recipient.txt'
    $identity = Join-Path $root 'identity.txt'
    Set-Content -LiteralPath $recipient -Value $canary -Encoding ASCII
    Set-Content -LiteralPath $identity -Value $identityCanary -Encoding ASCII
    $unix = -not ($env:OS -eq 'Windows_NT')
    foreach ($name in @('docker', 'age', 'pg_restore')) {
        if ($unix) {
            $tool = Join-Path $bin $name
            @(
                '#!/bin/sh',
                'printf "%s\n" "$0 $*" >> "$CARD1405_TOOL_MARKER"',
                'exit 99'
            ) | Set-Content -LiteralPath $tool -Encoding ASCII
            & chmod +x $tool
        } else {
            $tool = Join-Path $bin ($name + '.cmd')
            @(
                '@echo off',
                'echo %0 %*>> "%CARD1405_TOOL_MARKER%"',
                'exit /b 99'
            ) | Set-Content -LiteralPath $tool -Encoding ASCII
        }
    }
    return @{
        Root = $root; Sync = $sync; Dest = $dest; Stage = $stage; Bin = $bin
        Marker = $marker; Recipient = $recipient; Identity = $identity
        Canary = $canary; IdentityCanary = $identityCanary
    }
}

function Remove-Fixture {
    param($Fixture)
    if ($Fixture -and (Test-Path -LiteralPath $Fixture.Root)) {
        Remove-Item -LiteralPath $Fixture.Root -Recurse -Force
    }
}

function Invoke-Snapshot {
    param($Fixture, [string[]]$Arguments, [hashtable]$ExtraEnv)
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $pwsh
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $here
    foreach ($arg in @('-NoProfile', '-File', $scriptPath) + @($Arguments)) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $separator = [System.IO.Path]::PathSeparator
    $pathKey = 'PATH'
    if (-not $psi.Environment.ContainsKey('PATH') -and $psi.Environment.ContainsKey('Path')) { $pathKey = 'Path' }
    $psi.Environment[$pathKey] = $Fixture.Bin + $separator + $psi.Environment[$pathKey]
    $psi.Environment['CARD1405_TOOL_MARKER'] = $Fixture.Marker
    if ($ExtraEnv) {
        foreach ($key in @($ExtraEnv.Keys)) { $psi.Environment[[string]$key] = [string]$ExtraEnv[$key] }
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $markerText = ''
    if (Test-Path -LiteralPath $Fixture.Marker) {
        $markerText = [System.IO.File]::ReadAllText($Fixture.Marker)
    }
    return @{ Exit = $proc.ExitCode; Out = $stdout; Err = $stderr; Tools = $markerText }
}

function Assert-NoSecret {
    param($Result, $Fixture, [string]$Name)
    $blob = $Result.Out + "`n" + $Result.Err
    Assert-True ($blob.IndexOf($Fixture.Canary, [StringComparison]::Ordinal) -lt 0) "$Name hides recipient" 'recipient bytes were written'
    Assert-True ($blob.IndexOf($Fixture.IdentityCanary, [StringComparison]::Ordinal) -lt 0) "$Name hides identity" 'identity bytes were written'
    Assert-True ($blob -notmatch '(?i)password\s*=') "$Name hides password" 'password= present'
    Assert-True ($blob -notmatch '(?i)host\s*=') "$Name hides host" 'Host= present'
}

function Assert-NoDocker {
    param($Result, [string]$Name)
    Assert-True ([string]::IsNullOrEmpty($Result.Tools)) "$Name does not run docker" $Result.Tools
}

function Assert-DestUntouched {
    param($Fixture, [string]$Name)
    $dumps = @(Get-ChildItem -LiteralPath $Fixture.Dest -Filter '*.dump.age' -ErrorAction SilentlyContinue)
    $status = Test-Path -LiteralPath (Join-Path $Fixture.Dest 'snapshot-status.json')
    Assert-True ($dumps.Count -eq 0 -and -not $status) "$Name writes nothing" "dumps=$($dumps.Count) status=$status"
}

function Test-WhatIfPlan {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'WhatIfPlan script exists' $scriptPath; return }
        $source = [System.IO.File]::ReadAllText($scriptPath)
        $bytes = [System.IO.File]::ReadAllBytes($scriptPath)
        $high = @($bytes | Where-Object { $_ -gt 127 })
        Assert-True ($high.Count -eq 0) 'script is ASCII' "non-ascii=$($high.Count)"
        Assert-True ($source -notmatch 'age1[0-9a-z]{10,}') 'script has no age recipient literal' 'age1 literal'
        Assert-True ($source -notmatch '(?i)password\s*=') 'script has no password literal' 'password literal'
        $args = @(
            '-WhatIf', '-RecipientFile', $fx.Recipient, '-Destination', $fx.Dest,
            '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        $plan = Invoke-Snapshot -Fixture $fx -Arguments $args
        Assert-True ($plan.Exit -eq 0) 'WhatIfPlan exits 0' "exit=$($plan.Exit) $($plan.Out)"
        foreach ($word in @('dump', 'encrypt', 'copy', 'retention')) {
            Assert-True ($plan.Out -match [regex]::Escape($word)) "WhatIfPlan lists $word" $plan.Out
        }
        Assert-NoSecret $plan $fx 'WhatIfPlan'
        Assert-NoDocker $plan 'WhatIfPlan'
        Assert-DestUntouched $fx 'WhatIfPlan'
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $ref = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-RecipientRef', 'CARD1405_RECIPIENT_REF', '-Destination', $fx.Dest,
            '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        ) -ExtraEnv @{ CARD1405_RECIPIENT_REF = $fx.Recipient }
        Assert-True ($ref.Exit -eq 0) 'WhatIfPlan ref exits 0' "exit=$($ref.Exit) $($ref.Out)"
        foreach ($word in @('dump', 'encrypt', 'copy', 'retention')) {
            Assert-True ($ref.Out -match [regex]::Escape($word)) "WhatIfPlan ref lists $word" $ref.Out
        }
        Assert-NoSecret $ref $fx 'WhatIfPlan ref'
        Assert-NoDocker $ref 'WhatIfPlan ref'
        Assert-DestUntouched $fx 'WhatIfPlan ref'
    } finally { Remove-Fixture $fx }
}

function Test-MissingRecipient {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'MissingRecipient script exists' $scriptPath; return }
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        ) -ExtraEnv @{ CARD1405_RECIPIENT_REF = '' }
        Assert-True ($result.Exit -ne 0) 'MissingRecipient exits non-zero' "exit=$($result.Exit)"
        Assert-True ($result.Out -match 'reason=missing-recipient') 'MissingRecipient names the reason' $result.Out
        Assert-NoSecret $result $fx 'MissingRecipient'
        Assert-NoDocker $result 'MissingRecipient'
        Assert-DestUntouched $fx 'MissingRecipient'
    } finally { Remove-Fixture $fx }
}

function Test-RecipientInsideDestination {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'RecipientInsideDestination script exists' $scriptPath; return }
        $inside = Join-Path $fx.Dest 'recipient.txt'
        Set-Content -LiteralPath $inside -Value $fx.Canary -Encoding ASCII
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-RecipientFile', $inside, '-Destination', $fx.Dest,
            '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($result.Exit -ne 0) 'RecipientInsideDestination exits non-zero' "exit=$($result.Exit) $($result.Out)"
        Assert-True ($result.Out -match 'reason=recipient-inside-sync') 'RecipientInsideDestination names the reason' $result.Out
        Assert-NoSecret $result $fx 'RecipientInsideDestination'
        Assert-NoDocker $result 'RecipientInsideDestination'
        $dumps = @(Get-ChildItem -LiteralPath $fx.Dest -Filter '*.dump.age' -ErrorAction SilentlyContinue)
        Assert-True ($dumps.Count -eq 0) 'RecipientInsideDestination writes no dump' "dumps=$($dumps.Count)"
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesProduction {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesProduction script exists' $scriptPath; return }
        $common = @(
            '-Execute', '-RestoreDrill', '-IdentityFile', $fx.Identity,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        $container = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-ScratchContainer', 'antiphon-postgres'))
        Assert-True ($container.Exit -ne 0) 'drill refuses production container' "exit=$($container.Exit) $($container.Out)"
        Assert-True ($container.Out -match 'reason=drill-refused') 'drill container reason' $container.Out
        Assert-NoDocker $container 'drill container'
        Assert-NoSecret $container $fx 'drill container'
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $volume = Invoke-Snapshot -Fixture $fx -Arguments ($common + @(
            '-ScratchContainer', 'antiphon-postgres-drill', '-ScratchVolume', 'antiphon_pgdata'
        ))
        Assert-True ($volume.Exit -ne 0) 'drill refuses production volume' "exit=$($volume.Exit) $($volume.Out)"
        Assert-True ($volume.Out -match 'reason=drill-refused') 'drill volume reason' $volume.Out
        Assert-NoDocker $volume 'drill volume'
        Assert-DestUntouched $fx 'drill volume'
    } finally { Remove-Fixture $fx }
}

function Test-DrillPlan {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillPlan script exists' $scriptPath; return }
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-RestoreDrill', '-IdentityFile', $fx.Identity,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage,
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchDatabase', 'antiphon_drill',
            '-ScratchVolume', 'antiphon-postgres-drill-pgdata'
        )
        Assert-True ($result.Exit -eq 0) 'DrillPlan exits 0' "exit=$($result.Exit) $($result.Out)"
        Assert-True ($result.Out -match 'antiphon-postgres-drill') 'DrillPlan names the scratch container' $result.Out
        Assert-True ($result.Out -match 'pg_restore') 'DrillPlan lists pg_restore' $result.Out
        Assert-True ($result.Out -notmatch 'docker exec antiphon-postgres\b') 'DrillPlan does not target production' $result.Out
        Assert-NoSecret $result $fx 'DrillPlan'
        Assert-NoDocker $result 'DrillPlan'
        Assert-DestUntouched $fx 'DrillPlan'
    } finally { Remove-Fixture $fx }
}

function Test-Retention {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'Retention script exists' $scriptPath; return }
    . $scriptPath
    if (-not (Get-Command Get-SnapshotPruneSet -ErrorAction SilentlyContinue)) {
        Write-Fail 'Retention function' 'Get-SnapshotPruneSet missing'
        return
    }
    $names = @(1..16 | ForEach-Object { 'antiphon-202610{0:d2}T000000Z.dump.age' -f $_ })
    $newest = 'antiphon-20261016T000000Z.dump.age'
    $kept = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $newest -Verified $true
    Assert-True ($kept.Ok -eq $true) 'retention prunes when verified' $kept.Reason
    $drop = @($kept.Drop)
    Assert-True ($drop.Count -eq 2) 'retention keeps 14' ("count=$($drop.Count) " + ($drop -join ','))
    Assert-True ($drop -notcontains $newest) 'retention keeps the new file' ($drop -join ',')
    $unverified = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $newest -Verified $false
    Assert-True ($unverified.Ok -eq $true -and @($unverified.Drop).Count -eq 0) 'retention skips an unverified file' $unverified.Reason
    $few = @($names | Select-Object -First 3)
    $within = Get-SnapshotPruneSet -Names $few -Retain 14 -NewName $few[-1] -Verified $true
    Assert-True ($within.Ok -eq $true -and @($within.Drop).Count -eq 0) 'retention keeps a short set' $within.Reason
    $oldest = $names[0]
    $bad = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $oldest -Verified $true
    Assert-True ($bad.Ok -eq $false -and @($bad.Drop).Count -eq 0) 'retention refuses to drop the new file' $bad.Reason
    $zero = Get-SnapshotPruneSet -Names $names -Retain 0 -NewName $newest -Verified $true
    Assert-True ($zero.Ok -eq $false -and @($zero.Drop).Count -eq 0) 'retention refuses retain below 1' $zero.Reason
}

$all = @(
    'WhatIfPlan', 'MissingRecipient', 'RecipientInsideDestination',
    'DrillRefusesProduction', 'DrillPlan', 'Retention'
)
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Host "unknown case $Case"; exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
}

Write-Host ''
Write-Host ("CARD-1405 postgres-snapshot: {0} passed, {1} failed" -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'POSTGRES SNAPSHOT TESTS EXIT CODE: 1'
    exit 1
}
Write-Host 'POSTGRES SNAPSHOT TESTS EXIT CODE: 0'
exit 0
