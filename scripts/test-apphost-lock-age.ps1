#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0152 regression: AppHost lock age is UTC-vs-UTC, not UTC-vs-local.

    Constructs lock stamps with UTC arithmetic (never local Get-Date) and asserts
    a genuinely N-minute-old stamp is reported as N minutes old - and stale past
    LockMaxAgeMinutes - even on a BST/GMT machine whose local clock is an hour
    off UTC. Default [datetime]::Parse of a Z-suffix stamp converts to local;
    subtracting that from UtcNow is the undercount this card exists to stop.

    CARD-0310: a dead holder with a stamp younger than LockMaxAgeMinutes is still
    in-flight (T8), a 20-min-old dead stamp is litter (T9), and New-AppHostLock
    refuses a dead-but-fresh leftover rather than stealing it (T10).

    Never touches logs/apphost.*.lock. ASCII-only (pwsh 7 and Windows PowerShell 5.1).
#>
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

function Assert-Near {
    param([double]$Actual, [double]$Expected, [double]$Tolerance, [string]$Name)
    $delta = [math]::Abs($Actual - $Expected)
    if ($delta -le $Tolerance) { Write-Pass $Name }
    else { Write-Fail $Name ("expected {0} +/- {1}, actual {2}" -f $Expected, $Tolerance, $Actual) }
}

function New-TestDir {
    $dir = Join-Path $env:TEMP ('apphost-lock-age-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Write-LockFile {
    param([string]$Dir, [string]$StampText, [int]$ProcessId = $PID)
    $path = Join-Path $Dir 'apphost.restart.lock'
    $line = '{0} {1}' -f $ProcessId, $StampText
    Set-Content -LiteralPath $path -Value $line -Encoding ASCII -NoNewline
    return $path
}

$offsetHours = [datetimeoffset]::Now.Offset.TotalHours
Write-Host ("local UTC offset: {0} h  (UtcNow={1} Now={2})" -f `
    $offsetHours, [datetime]::UtcNow.ToString('o'), [datetime]::Now.ToString('o'))

$testDir = New-TestDir
try {
    $nowUtc = [datetime]::SpecifyKind([datetime]::UtcNow, [datetimekind]::Utc)

    # --- T1: 75 min old via UTC arithmetic is ~75 min, not the BST-undercount ~15 ---
    $stamp75 = $nowUtc.AddMinutes(-75)
    $iso75 = $stamp75.ToString('o')
    Assert-True ($iso75.EndsWith('Z')) 'T1 stored stamp is UTC round-trip (ends with Z)' $iso75

    $path75 = Write-LockFile -Dir $testDir -StampText $iso75
    $lock75 = Get-AppHostLock -Path $path75 -NowUtc $nowUtc
    Assert-True $lock75.Readable 'T1 lock is readable'
    Assert-True ($lock75.StampUtc.Kind -eq [datetimekind]::Utc) 'T1 StampUtc.Kind is Utc' ("Kind={0}" -f $lock75.StampUtc.Kind)
    Assert-Near $lock75.AgeMinutes 75 0.05 'T1 AgeMinutes is 75 (UTC arithmetic, not local Get-Date)'
    Assert-True ($lock75.StampRaw -eq $iso75) 'T1 StampRaw is the stored UTC text' ("raw={0}" -f $lock75.StampRaw)

    $held75 = Test-AppHostLockActive -Path $path75 -MaxAgeMinutes 15 -Label 'restart lock' -NowUtc $nowUtc
    Assert-True ($null -eq $held75) 'T1 75-min-old lock is stale (ignored) at MaxAgeMinutes=15' ("held=$held75")

    $naiveParsed = [datetime]::Parse($iso75)
    $naiveAge = ($nowUtc - $naiveParsed).TotalMinutes
    if ([math]::Abs($offsetHours) -ge 0.5) {
        Assert-Near $naiveAge (75 - ($offsetHours * 60)) 0.5 'T1 naive Parse-vs-UtcNow undercounts by the local offset (the CARD-0152 bug)'
        Assert-True ([math]::Abs($lock75.AgeMinutes - $naiveAge) -ge 30) 'T1 production age is not the naive undercount'
    } else {
        Write-Host 'SKIP T1 naive-undercount (machine UTC offset ~0; bug would be unobservable)'
    }

    # --- T2: 45 min old is stale. Naive undercount by 60 min would call this FRESH. ---
    $iso45 = $nowUtc.AddMinutes(-45).ToString('o')
    $path45 = Write-LockFile -Dir $testDir -StampText $iso45
    $lock45 = Get-AppHostLock -Path $path45 -NowUtc $nowUtc
    Assert-Near $lock45.AgeMinutes 45 0.05 'T2 AgeMinutes is 45'
    $held45 = Test-AppHostLockActive -Path $path45 -MaxAgeMinutes 15 -Label 'restart lock' -NowUtc $nowUtc
    Assert-True ($null -eq $held45) 'T2 45-min-old lock is stale (would look ~-15 / fresh under the UTC-vs-local bug)' ("held=$held45")

    # --- T3: 5 min old with a live holder is active, and the reason names stamp + age ---
    $iso5 = $nowUtc.AddMinutes(-5).ToString('o')
    $path5 = Write-LockFile -Dir $testDir -StampText $iso5
    $lock5 = Get-AppHostLock -Path $path5 -NowUtc $nowUtc
    Assert-Near $lock5.AgeMinutes 5 0.05 'T3 AgeMinutes is 5'
    $held5 = Test-AppHostLockActive -Path $path5 -MaxAgeMinutes 15 -Label 'restart lock' -NowUtc $nowUtc
    Assert-True ($null -ne $held5) 'T3 5-min-old lock with live PID is active' 'held was null'
    Assert-True ($held5 -match [regex]::Escape($iso5)) 'T3 active reason includes the raw stored stamp' $held5
    Assert-True ($held5 -match '5\.0 min old') 'T3 active reason includes computed age' $held5
    Assert-True ($held5 -match 'stamp ') 'T3 active reason labels the stamp' $held5

    # --- T4: ConvertTo-UtcStamp of Z and of +01:00 both yield UTC clock face ---
    $zParsed = ConvertTo-UtcStamp $iso75
    Assert-True ($zParsed.Kind -eq [datetimekind]::Utc) 'T4 Z-suffix parse Kind=Utc'
    Assert-True ($zParsed.Hour -eq $stamp75.Hour -and $zParsed.Minute -eq $stamp75.Minute) 'T4 Z-suffix clock face stays UTC (not converted to local)'

    $plusOffset = [datetimeoffset]::new($nowUtc.AddMinutes(-75), [timespan]::Zero).ToOffset([timespan]::FromHours(1))
    $plusOneText = $plusOffset.ToString('o')
    $plusParsed = ConvertTo-UtcStamp $plusOneText
    Assert-True ($null -ne $plusParsed) 'T4 +01:00 stamp parses' $plusOneText
    $plusAge = Get-UtcAgeMinutes -StampUtc $plusParsed -NowUtc $nowUtc
    Assert-Near $plusAge 75 0.05 'T4 +01:00 stamp age is still 75 UTC minutes'

    # --- T5: offset-less stamp is assumed UTC, not local ---
    $noOffset = $nowUtc.AddMinutes(-75).ToString('yyyy-MM-ddTHH:mm:ss.fffffff')
    Assert-True (-not $noOffset.EndsWith('Z')) 'T5 offset-less text has no Z' $noOffset
    $bareParsed = ConvertTo-UtcStamp $noOffset
    Assert-True ($null -ne $bareParsed) 'T5 offset-less stamp parses'
    $bareAge = Get-UtcAgeMinutes -StampUtc $bareParsed -NowUtc $nowUtc
    Assert-Near $bareAge 75 0.05 'T5 offset-less stamp is treated as UTC (not local)'

    # --- T6: New-AppHostLock writes pid + UTC round-trip (Z) ---
    $writeDir = Join-Path $testDir 'write'
    New-Item -ItemType Directory -Force $writeDir | Out-Null
    $writePath = Join-Path $writeDir 'apphost.restart.lock'
    $acquired = New-AppHostLock -Path $writePath -MaxAgeMinutes 15 -Label 'restart lock'
    try {
        Assert-True $acquired.Acquired 'T6 New-AppHostLock acquired' $acquired.Reason
        $written = (Get-Content -LiteralPath $writePath -Raw).Trim()
        Assert-True ($written -match '^\d+ \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}.*Z$') 'T6 lock line is "<pid> <utc-o-format-Z>"' $written
        $writtenLock = Get-AppHostLock -Path $writePath
        Assert-Near $writtenLock.AgeMinutes 0 1 'T6 freshly written lock age is ~0 min'
    } finally {
        Remove-AppHostLock $acquired
    }

    # --- T7: Format helpers used by watchdog log lines ---
    $fmt = 'stale restart lock - ignoring (stamp {0}, {1}; holder PID {2} alive={3}; {4})' -f `
        (Format-AppHostLockStamp $lock75), (Format-AppHostLockAge $lock75), `
        $lock75.ProcessId, $lock75.HolderAlive, $path75
    Assert-True ($fmt -match [regex]::Escape($iso75)) 'T7 stale log line includes raw stamp' $fmt
    Assert-True ($fmt -match '75\.0 min old') 'T7 stale log line includes computed age' $fmt

    # --- CARD-0310: dead holder + fresh stamp is in-flight, not litter ---
    $deadPid = 999999
    if (Test-ProcessAlive $deadPid) {
        $probe = Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList '/c exit' -PassThru -WindowStyle Hidden
        $deadPid = $probe.Id
        try { Wait-Process -Id $deadPid -Timeout 5 } catch { }
        Start-Sleep -Milliseconds 200
    }
    Assert-True (-not (Test-ProcessAlive $deadPid)) 'T8/T9/T10 have a dead PID to stamp' ("pid=$deadPid still alive")

    $isoDead5 = $nowUtc.AddMinutes(-5).ToString('o')
    $pathDead5 = Write-LockFile -Dir $testDir -StampText $isoDead5 -ProcessId $deadPid
    $heldDead5 = Test-AppHostLockActive -Path $pathDead5 -MaxAgeMinutes 15 -Label 'restart lock' -NowUtc $nowUtc
    Assert-True ($null -ne $heldDead5) 'T8 5-min-old lock with dead PID is active (not litter)' 'held was null'
    Assert-True ($heldDead5 -match 'exited') 'T8 dead-fresh reason says the holder exited' $heldDead5
    Assert-True ($heldDead5 -match 'child may still be launching') 'T8 dead-fresh reason names the still-launching child' $heldDead5
    Assert-True ($heldDead5 -match [regex]::Escape($isoDead5)) 'T8 dead-fresh reason includes the raw stored stamp' $heldDead5
    Assert-True ($heldDead5 -match '5\.0 min old') 'T8 dead-fresh reason includes computed age' $heldDead5

    $isoDead20 = $nowUtc.AddMinutes(-20).ToString('o')
    $pathDead20 = Write-LockFile -Dir $testDir -StampText $isoDead20 -ProcessId $deadPid
    $heldDead20 = Test-AppHostLockActive -Path $pathDead20 -MaxAgeMinutes 15 -Label 'restart lock' -NowUtc $nowUtc
    Assert-True ($null -eq $heldDead20) 'T9 20-min-old lock with dead PID is litter' ("held=$heldDead20")

    $stealDir = Join-Path $testDir 'steal'
    New-Item -ItemType Directory -Force $stealDir | Out-Null
    $stealPath = Write-LockFile -Dir $stealDir -StampText $isoDead5 -ProcessId $deadPid
    $stolen = New-AppHostLock -Path $stealPath -MaxAgeMinutes 15 -Label 'restart lock'
    try {
        Assert-True (-not $stolen.Acquired) 'T10 New-AppHostLock refuses a 5-min-old dead-PID leftover' 'Acquired was true (stole the leftover)'
        Assert-True ($stolen.Reason -match 'exited') 'T10 refusal reason says the holder exited' $stolen.Reason
        Assert-True (Test-Path -LiteralPath $stealPath) 'T10 leftover lock file was not deleted' $stealPath
    } finally {
        if ($stolen.Acquired) { Remove-AppHostLock $stolen }
    }

    # T3 still names a live holder, not the exited-child wording
    Assert-True ($held5 -match 'held by PID') 'T3 live+5 min still uses live-holder wording' $held5
    Assert-True ($held5 -notmatch 'exited') 'T3 live+5 min does not say the holder exited' $held5

    $keepDir = Join-Path $testDir 'keep'
    New-Item -ItemType Directory -Force $keepDir | Out-Null
    $keepPath = Join-Path $keepDir 'apphost.restart.lock'
    $keepLock = New-AppHostLock -Path $keepPath -MaxAgeMinutes 15 -Label 'restart lock'
    try {
        Assert-True $keepLock.Acquired 'T-keep New-AppHostLock acquired' $keepLock.Reason
        Remove-AppHostLock $keepLock -KeepFile
        Assert-True (Test-Path -LiteralPath $keepPath) 'T-keep -KeepFile leaves the lock file on disk' $keepPath
        $keptHeld = Test-AppHostLockActive -Path $keepPath -MaxAgeMinutes 15 -Label 'restart lock'
        Assert-True ($null -ne $keptHeld) 'T-keep leftover after -KeepFile is still active' 'held was null'
    } finally {
        Remove-Item -LiteralPath $keepPath -Force -ErrorAction SilentlyContinue
    }
}
finally {
    Remove-Item -LiteralPath $testDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ('CARD-0152/0310 lock-age: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'APPHOST LOCK AGE TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'APPHOST LOCK AGE TESTS EXIT CODE: 0  (PASS)'
exit 0
