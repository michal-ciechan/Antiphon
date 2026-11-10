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
if (-not [string]::IsNullOrWhiteSpace($env:CARD1831_SNAPSHOT_SCRIPT)) {
    if (Test-Path -LiteralPath $env:CARD1831_SNAPSHOT_SCRIPT -PathType Leaf) {
        $scriptPath = $env:CARD1831_SNAPSHOT_SCRIPT
    }
}
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

function Set-SnapshotHarnessMode {
    param([string]$Path, [string]$Octal)
    if ($env:OS -eq 'Windows_NT') { throw 'unix mode is not a Windows property' }
    $bits = [Convert]::ToInt32($Octal, 8)
    [System.IO.File]::SetUnixFileMode($Path, [System.IO.UnixFileMode]$bits)
}

function Get-SnapshotHarnessMode {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') { throw 'unix mode is not a Windows property' }
    $bits = [int][System.IO.File]::GetUnixFileMode($Path)
    return [Convert]::ToString($bits, 8)
}

function New-SnapshotHarnessLink {
    param([string]$Link, [string]$Target)
    try {
        New-Item -ItemType SymbolicLink -Path $Link -Target $Target -ErrorAction Stop | Out-Null
        return
    } catch {
        if ($env:OS -ne 'Windows_NT') { throw }
    }
    New-Item -ItemType Junction -Path $Link -Target $Target -ErrorAction Stop | Out-Null
}

function New-Fixture {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1405-' + [guid]::NewGuid().ToString('N'))
    $sync = Join-Path $root 'sync'
    $dest = Join-Path $sync 'antiphon-postgres'
    $stage = Join-Path $root 'staging'
    $bin = Join-Path $root 'bin'
    $marker = Join-Path $root 'tool-called'
    New-Item -ItemType Directory -Path $sync, $dest, $stage, $bin -Force | Out-Null
    if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $stage '700' }
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
            Set-SnapshotHarnessMode $tool '755'
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
    $pathKey = 'PATH'
    if (-not $psi.Environment.ContainsKey('PATH') -and $psi.Environment.ContainsKey('Path')) { $pathKey = 'Path' }
    # Only the fixture binaries are visible. A later docker/age on PATH would make
    # Get-Command return every match and the script would invoke none of them.
    $psi.Environment[$pathKey] = $Fixture.Bin
    $psi.Environment['CARD1405_TOOL_MARKER'] = $Fixture.Marker
    if ($ExtraEnv) {
        foreach ($key in @($ExtraEnv.Keys)) { $psi.Environment[[string]$key] = [string]$ExtraEnv[$key] }
    }
    # Fixture PATH hides the real coreutils. The docker/age fakes write sized
    # files through this absolute pwsh instead of /bin/dd.
    $psi.Environment['CARD1405_PWSH'] = $pwsh
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

function Get-PlainSnapshotStamp {
    param([string]$Path)
    $lines = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Path)) {
        [void]$lines.Add('ABSENT')
        return ($lines -join "`n")
    }
    $pending = New-Object System.Collections.Generic.Queue[string]
    $pending.Enqueue($Path)
    while ($pending.Count -gt 0) {
        $dir = $pending.Dequeue()
        foreach ($child in @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue | Sort-Object Name)) {
            $rel = $child.FullName.Substring($Path.Length).TrimStart([char]'/', [char]'\')
            $kind = [string]$child.LinkType
            if ($kind -eq 'SymbolicLink' -or $kind -eq 'Junction') {
                [void]$lines.Add('link ' + $rel + ' -> ' + [string]$child.Target)
                continue
            }
            if ($child.PSIsContainer) {
                [void]$lines.Add('dir ' + $rel)
                $pending.Enqueue($child.FullName)
                continue
            }
            $body = [System.IO.File]::ReadAllText($child.FullName)
            [void]$lines.Add('file ' + $rel + ' ' + $body)
        }
    }
    return ($lines -join "`n")
}

function New-PlainOutsideTree {
    param([string]$Folder)
    New-Item -ItemType Directory -Path $Folder -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $Folder 'notes.txt') -Value 'NOTES' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $Folder 'hermes-backup.dump') -Value 'HERMES' -Encoding ASCII
    foreach ($day in 1..8) {
        $name = 'antiphon-202611{0:d2}T000000Z.dump' -f $day
        Set-Content -LiteralPath (Join-Path $Folder $name) -Value ('old' + $day) -Encoding ASCII
    }
}

function Assert-PlainLinkRefused {
    param($Result, [string]$Before, [string]$After, [string]$Name, [string]$LocalFile)
    Assert-True ([string]$Result.State -eq 'failed') "$Name state=failed" ([string]$Result.State + ' ' + [string]$Result.Loud)
    Assert-True ([string]$Result.Loud -match 'oneDrive=failed') "$Name is loud" ([string]$Result.Loud)
    Assert-True ([string]$Result.Loud -match 'reason=link') "$Name names the link" ([string]$Result.Loud)
    Assert-True ($Before -eq $After) "$Name leaves the outside tree untouched" $After
    Assert-True (Test-Path -LiteralPath $LocalFile) "$Name keeps the local dump" $LocalFile
    Assert-True ([string]$Result.Loud -notmatch '(?i)password\s*=|postgres://|Host\s*=') "$Name stdout has no secret" ([string]$Result.Loud)
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
        Assert-True (Test-Path -LiteralPath $fx.Stage) 'WhatIfPlan keeps staging' $fx.Stage
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
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $slash = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-ScratchContainer', 'antiphon-postgres/'))
        Assert-True ($slash.Exit -eq 9) 'trailing slash exits 9' "exit=$($slash.Exit) $($slash.Out)"
        Assert-True ($slash.Out -match 'reason=drill-refused') 'trailing slash reason' $slash.Out
        Assert-True ($slash.Out -match 'guard=production-container') 'trailing slash guard' $slash.Out
        Assert-NoDocker $slash 'trailing slash'
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $slashVolume = Invoke-Snapshot -Fixture $fx -Arguments ($common + @(
            '-ScratchContainer', 'antiphon-postgres-drill', '-ScratchVolume', 'antiphon_pgdata/'
        ))
        Assert-True ($slashVolume.Exit -eq 9) 'trailing slash volume exits 9' "exit=$($slashVolume.Exit) $($slashVolume.Out)"
        Assert-True ($slashVolume.Out -match 'guard=production-volume') 'trailing slash volume guard' $slashVolume.Out
        Assert-NoDocker $slashVolume 'trailing slash volume'
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

function Install-SnapshotFake {
    param($Fixture)
    $unix = -not ($env:OS -eq 'Windows_NT')
    if (-not $unix) {
        throw 'CARD-1405 snapshot fakes run on the Linux witness host'
    }
    $docker = @'
#!/bin/sh
printf '%s\n' "$0 $*" >> "$CARD1405_TOOL_MARKER"
scenario="${CARD1405_SCENARIO:-}"
state="${CARD1405_DOCKER_STATE:-/tmp/card1405-docker-state}"
bytes="${CARD1405_DUMP_BYTES:-0}"
table="${CARD1405_TABLE_DATA:-0}"
cmd="$1"
shift
target=""
name_format=0
for arg in "$@"; do
    target="$arg"
    if [ "$arg" = "{{.Name}}" ]; then
        name_format=1
    fi
done
source_json='{"Id":"0123abcd9f0123456789abcdef0123456789abcd0123456789abcdef012345","Name":"/antiphon-postgres","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres"]}}}}'
alias_json='{"Id":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","Name":"/snapshot-alias","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres","snapshot-alias"]}}}}'
unlabelled_json='{"Id":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","Name":"/antiphon-postgres-drill","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
mount_json='{"Id":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"antiphon_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
# The official postgres image sets PG_MAJOR, PG_VERSION and PGDATA itself, so
# every container the drill creates or reuses carries them (CARD-1413 D-1).
image_env='"Env":["PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/postgresql/16/bin","GOSU_VERSION=1.17","LANG=en_US.utf8","PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","POSTGRES_HOST_AUTH_METHOD=trust","POSTGRES_USER=antiphon"]'
clean_json='{"Id":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},'"$image_env"'},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
server2_json='{"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Name":"/antiphon-server2-postgres-postgres-1","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon-server2-postgres_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["postgres"]}}}}'
custom_source_json='{"Id":"1212121212121212121212121212121212121212121212121212121212121212","Name":"/custom-pg","Config":{"Labels":{}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["custom-pg"]}}}}'
labelled_source_json='{"Id":"3434343434343434343434343434343434343434343434343434343434343434","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
mismatch_json='{"Id":"5656565656565656565656565656565656565656565656565656565656565656","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"scratcha","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
pghost_json='{"Id":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PGHOST=db.internal","PGPORT=5432"]},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
own_running_json='{"Id":"7878787878787878787878787878787878787878787878787878787878787878","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
pghostaddr_json='{"Id":"9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PGHOSTADDR=10.0.0.5"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
pgenv_json='{"Id":"9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PATH=/usr/bin","PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","PGSERVICE=production"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
pgpassfile_json='{"Id":"9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","PGPASSFILE=/run/secrets/pgpass"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
pgssl_json='{"Id":"8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a8a","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PG_MAJOR=16","PG_VERSION=16.4-1.pgdg120+1","PGDATA=/var/lib/postgresql/data","PGSSLMODE=verify-full","PGSSLROOTCERT=/certs/root.crt"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
bind_json='{"Id":"9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"},{"Type":"bind","Source":"/var/lib/docker/volumes/antiphon_pgdata/_data","Destination":"/mnt/live"}],"NetworkSettings":{"Networks":{}}}'
hostnet_json='{"Id":"9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d9d","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"HostConfig":{"NetworkMode":"host"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"host":{"Aliases":[]}}}}'
sharednet_json='{"Id":"9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e9e","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"HostConfig":{"NetworkMode":"container:0123abcd9f0123456789abcdef0123456789abcd0123456789abcdef012345"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
emit() {
    if [ "$name_format" = "1" ]; then
        printf '/%s\n' "$2"
    else
        printf '%s\n' "$1"
    fi
}
list_line() {
    case "$scenario" in
        clean|prefix|alias|unlabelled|mount|production-name|database)
            printf '%s\n' '3000; 0 12345 TABLE DATA public cards antiphon'
            return
            ;;
    esac
    if [ "$table" = "1" ]; then
        printf '%s\n' '3000; 0 12345 TABLE DATA public cards antiphon'
    else
        printf '%s\n' '; Archive created at 2026-10-21'
    fi
}
case "$cmd" in
    inspect)
        if [ "$target" = "antiphon-postgres" ]; then
            emit "$source_json" "antiphon-postgres"
            exit 0
        fi
        if [ "$target" = "antiphon-server2-postgres-postgres-1" ]; then
            if [ "$scenario" = "production-name" ]; then
                emit "$server2_json" "antiphon-server2-postgres-postgres-1"
                exit 0
            fi
            exit 1
        fi
        if [ "$target" = "custom-pg" ]; then
            emit "$custom_source_json" "custom-pg"
            exit 0
        fi
        case "$scenario" in
            prefix)
                emit "$source_json" "antiphon-postgres"
                exit 0
                ;;
            alias)
                emit "$alias_json" "snapshot-alias"
                exit 0
                ;;
            unlabelled)
                emit "$unlabelled_json" "antiphon-postgres-drill"
                exit 0
                ;;
            mount)
                emit "$mount_json" "antiphon-postgres-drill"
                exit 0
                ;;
            production-name)
                emit "$server2_json" "antiphon-server2-postgres-postgres-1"
                exit 0
                ;;
            database)
                emit "$unlabelled_json" "antiphon-postgres-drill"
                exit 0
                ;;
            clean)
                if [ ! -f "$state" ]; then
                    exit 1
                fi
                emit "$clean_json" "antiphon-postgres-drill"
                exit 0
                ;;
            own-volume)
                if [ ! -f "$state" ]; then
                    exit 1
                fi
                emit "$own_running_json" "antiphon-postgres-drill"
                exit 0
                ;;
            foreign-volume|plain-volume)
                if [ ! -f "$state" ]; then
                    exit 1
                fi
                emit "$clean_json" "antiphon-postgres-drill"
                exit 0
                ;;
            labelled-source)
                emit "$labelled_source_json" "antiphon-postgres-drill"
                exit 0
                ;;
            mismatch)
                emit "$mismatch_json" "antiphon-postgres-drill"
                exit 0
                ;;
            pghostaddr)
                emit "$pghostaddr_json" "antiphon-postgres-drill"
                exit 0
                ;;
            pgenv)
                emit "$pgenv_json" "antiphon-postgres-drill"
                exit 0
                ;;
            pgpassfile)
                emit "$pgpassfile_json" "antiphon-postgres-drill"
                exit 0
                ;;
            pgssl)
                emit "$pgssl_json" "antiphon-postgres-drill"
                exit 0
                ;;
            reuse)
                emit "$clean_json" "antiphon-postgres-drill"
                exit 0
                ;;
            bind)
                emit "$bind_json" "antiphon-postgres-drill"
                exit 0
                ;;
            hostnet)
                emit "$hostnet_json" "antiphon-postgres-drill"
                exit 0
                ;;
            sharednet)
                emit "$sharednet_json" "antiphon-postgres-drill"
                exit 0
                ;;

            pghost)
                emit "$pghost_json" "antiphon-postgres-drill"
                exit 0
                ;;
            *)
                exit 1
                ;;
        esac
        ;;
    run)
        run_joined=" $*"
        case "$run_joined" in
            *'--list'*)
                list_line
                exit 0
                ;;
            *' -d '*)
                if [ -n "$state" ]; then
                    : > "$state"
                fi
                exit 0
                ;;
        esac
        exit 0
        ;;
    exec)
        exec_joined=" $*"
        case "$exec_joined" in
            *'pg_restore'*)
                list_line
                exit 0
                ;;
            *' wc '*|*' wc')
                printf '%s\n' "$bytes /tmp/antiphon-snapshot.dump"
                exit 0
                ;;
        esac
        exit 0
        ;;
    cp)
        if [ "$scenario" = "publish" ]; then
            "$CARD1405_PWSH" -NoProfile -Command "[System.IO.File]::WriteAllBytes('$target', (New-Object byte[] $bytes))" || exit 1
        fi
        exit 0
        ;;
    volume)
        sub="$1"
        vol_file="${state}.volumes"
        case "$sub" in
            inspect)
                if [ "$scenario" = "plain-volume" ] && [ "$target" = "windmill_db" ]; then
                    printf '%s\n' '{"Name":"windmill_db","Labels":{}}'
                    exit 0
                fi
                if [ "$scenario" = "foreign-volume" ] && [ "$target" = "windmill_db" ]; then
                    printf '%s\n' '{"Name":"windmill_db","Labels":{}}'
                    exit 0
                fi
                # PATH is only the fixture bin, so this match stays in the shell.
                if [ -f "$vol_file" ]; then
                    while IFS= read -r line || [ -n "$line" ]; do
                        if [ "$line" = "$target" ]; then
                            printf '{"Name":"%s","Labels":{"antiphon.snapshot-drill":"1"}}\n' "$target"
                            exit 0
                        fi
                    done < "$vol_file"
                fi
                exit 1
                ;;
            create)
                if [ -n "$vol_file" ]; then
                    printf '%s\n' "$target" >> "$vol_file"
                fi
                exit 0
                ;;
            rm)
                exit 0
                ;;
        esac
        exit 0
        ;;
    context)
        printf '%s\n' "${CARD1405_DOCKER_ENDPOINT-unix:///var/run/docker.sock}"
        exit 0
        ;;

    ps)
        vol=""
        for arg in "$@"; do
            case "$arg" in
                volume=*)
                    vol="${arg#volume=}"
                    ;;
            esac
        done
        if [ "$scenario" = "foreign-volume" ] && [ "$vol" = "windmill_db" ]; then
            printf '%s\n' 'abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd'
        fi
        exit 0
        ;;
    *)
        exit 0
        ;;
esac
'@
    $age = @'
#!/bin/sh
printf '%s\n' "$0 $*" >> "$CARD1405_TOOL_MARKER"
out=""
prev=""
for arg in "$@"; do
    if [ "$prev" = "-o" ]; then
        out="$arg"
    fi
    prev="$arg"
done
bytes="${CARD1405_PLAIN_BYTES:-16}"
if [ -n "$out" ]; then
    "$CARD1405_PWSH" -NoProfile -Command "[System.IO.File]::WriteAllBytes('$out', (New-Object byte[] $bytes))" || exit 1
fi
exit "${CARD1405_AGE_EXIT:-0}"
'@
    $encoding = New-Object System.Text.UTF8Encoding $false
    $dockerPath = Join-Path $Fixture.Bin 'docker'
    $agePath = Join-Path $Fixture.Bin 'age'
    [System.IO.File]::WriteAllText($dockerPath, $docker.Replace("`r`n", "`n"), $encoding)
    [System.IO.File]::WriteAllText($agePath, $age.Replace("`r`n", "`n"), $encoding)
    Set-SnapshotHarnessMode $dockerPath '755'
    Set-SnapshotHarnessMode $agePath '755'
}

function Invoke-DrillCase {
    param($Fixture, [string]$Scenario, [string[]]$Extra, [string]$PlainBytes = '32', [string[]]$Volumes = @(), [hashtable]$Env = @{}, [switch]$NoSnapshot)
    Remove-Item -LiteralPath $Fixture.Marker -Force -ErrorAction SilentlyContinue
    $dockerState = Join-Path $Fixture.Root 'docker-state'
    Remove-Item -LiteralPath $dockerState -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ($dockerState + '.volumes') -Force -ErrorAction SilentlyContinue
    foreach ($seed in @($Volumes)) {
        if (-not [string]::IsNullOrWhiteSpace($seed)) { Add-Content -LiteralPath ($dockerState + '.volumes') -Value $seed -Encoding ASCII }
    }
    $cipher = Join-Path $Fixture.Dest 'antiphon-20261021T120000Z.dump.age'
    if ($NoSnapshot) {
        Remove-Item -LiteralPath $cipher -Force -ErrorAction SilentlyContinue
    } else {
        Set-Content -LiteralPath $cipher -Value 'cipher' -Encoding ASCII
    }
    $arguments = @(
        '-Execute', '-RestoreDrill', '-IdentityFile', $Fixture.Identity,
        '-Destination', $Fixture.Dest, '-SyncRoot', $Fixture.Sync, '-StagingPath', $Fixture.Stage
    ) + @($Extra)
    $environment = @{
        CARD1405_SCENARIO = $Scenario
        CARD1405_DOCKER_STATE = (Join-Path $Fixture.Root 'docker-state')
        CARD1405_DUMP_BYTES = '1048576'
        CARD1405_TABLE_DATA = '1'
        CARD1405_PLAIN_BYTES = $PlainBytes
    }
    foreach ($key in @($Env.Keys)) { $environment[[string]$key] = [string]$Env[$key] }
    return Invoke-Snapshot -Fixture $Fixture -Arguments $arguments -ExtraEnv $environment
}

function Assert-DrillRefused {
    param($Result, $Fixture, [string]$Guard, [string]$Name)
    Assert-True ($Result.Exit -eq 9) "$Name exits 9" "exit=$($Result.Exit) $($Result.Out)"
    Assert-True ($Result.Out -match 'reason=drill-refused') "$Name reason" $Result.Out
    Assert-True ($Result.Out -match ('guard=' + [regex]::Escape($Guard))) "$Name guard" $Result.Out
    Assert-True ($Result.Tools -notmatch 'pg_restore') "$Name does not restore" $Result.Tools
    Assert-True ($Result.Tools -notmatch 'createdb') "$Name does not create a database" $Result.Tools
    Assert-True ($Result.Tools -notmatch 'rm -f') "$Name does not remove a container" $Result.Tools
    Assert-NoSecret $Result $Fixture $Name
}

function Test-DrillRefusesIdPrefix {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesIdPrefix script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'prefix' @('-ScratchContainer', '0123abcd9f')
        Assert-DrillRefused $result $fx 'id-prefix' 'id prefix'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesAlias {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesAlias script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'alias' @('-ScratchContainer', 'snapshot-alias')
        Assert-DrillRefused $result $fx 'alias' 'alias'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesUnlabelled {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesUnlabelled script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'unlabelled' @('-ScratchContainer', 'antiphon-postgres-drill')
        Assert-DrillRefused $result $fx 'unlabelled' 'unlabelled'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesSourceMount {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesSourceMount script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'mount' @(
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'antiphon-postgres-drill-pgdata'
        )
        Assert-DrillRefused $result $fx 'production-mount' 'source mount'
        $custom = Invoke-DrillCase $fx 'labelled-source' @(
            '-Container', 'custom-pg',
            '-SourceVolume', 'antiphon_pgdata',
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'antiphon-postgres-drill-pgdata'
        ) '1048576'
        Assert-DrillRefused $custom $fx 'source-mount' 'custom source mount'
        Assert-True ($custom.Tools -notmatch 'volume rm') 'custom source mount leaves volumes' $custom.Tools
        Assert-True ($custom.Tools -notmatch 'run -d') 'custom source mount does not start postgres' $custom.Tools
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesProductionLikeName {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesProductionLikeName script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $lower = Invoke-DrillCase $fx 'production-name' @('-ScratchContainer', 'antiphon-server2-postgres-postgres-1')
        Assert-DrillRefused $lower $fx 'production-name' 'server2 name'
        $upper = Invoke-DrillCase $fx 'production-name' @('-ScratchContainer', 'ANTIPHON-SERVER2-POSTGRES-POSTGRES-1')
        Assert-DrillRefused $upper $fx 'production-name' 'server2 name case'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesScratchDatabase {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesScratchDatabase script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $exact = Invoke-DrillCase $fx 'database' @('-ScratchDatabase', 'antiphon')
        Assert-DrillRefused $exact $fx 'database' 'scratch database antiphon'
        $cased = Invoke-DrillCase $fx 'database' @('-ScratchDatabase', 'Antiphon')
        Assert-DrillRefused $cased $fx 'database' 'scratch database Antiphon'
        $source = Invoke-DrillCase $fx 'database' @('-Database', 'CustomDb', '-ScratchDatabase', 'customdb')
        Assert-DrillRefused $source $fx 'database' 'scratch database matches source'
    } finally { Remove-Fixture $fx }
}

function Invoke-PublishCase {
    param($Fixture, [string]$Bytes, [string]$TableData, [hashtable]$Env = @{}, [string[]]$Extra = @())
    Remove-Item -LiteralPath $Fixture.Marker -Force -ErrorAction SilentlyContinue
    $environment = @{
        CARD1405_SCENARIO = 'publish'
        CARD1405_DOCKER_STATE = (Join-Path $Fixture.Root 'docker-state')
        CARD1405_DUMP_BYTES = $Bytes
        CARD1405_TABLE_DATA = $TableData
        CARD1405_PLAIN_BYTES = '32'
    }
    foreach ($key in @($Env.Keys)) { $environment[[string]$key] = [string]$Env[$key] }
    return Invoke-Snapshot -Fixture $Fixture -Arguments (@(
        '-Execute', '-RecipientFile', $Fixture.Recipient,
        '-Destination', $Fixture.Dest, '-SyncRoot', $Fixture.Sync, '-StagingPath', $Fixture.Stage
    ) + @($Extra)) -ExtraEnv $environment
}

function Assert-PublishRejected {
    param($Result, $Fixture, [string]$Name)
    Assert-True ($Result.Exit -eq 5) "$Name exits 5" "exit=$($Result.Exit) $($Result.Out)"
    Assert-True ($Result.Out -match 'reason=verify-failed') "$Name reason" $Result.Out
    $dumps = @(Get-ChildItem -LiteralPath $Fixture.Dest -Filter '*.dump.age' -ErrorAction SilentlyContinue)
    $status = Test-Path -LiteralPath (Join-Path $Fixture.Dest 'snapshot-status.json')
    Assert-True ($dumps.Count -eq 0 -and -not $status) "$Name publishes nothing" "dumps=$($dumps.Count) status=$status"
    Assert-NoSecret $Result $Fixture $Name
}

function Test-PublishRejectsTinyDump {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PublishRejectsTinyDump script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-PublishCase $fx '100' '1'
        Assert-PublishRejected $result $fx 'tiny dump'
    } finally { Remove-Fixture $fx }
}

function Test-PublishRejectsMissingTableData {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PublishRejectsMissingTableData script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-PublishCase $fx '1048576' '0'
        Assert-PublishRejected $result $fx 'missing table data'
    } finally { Remove-Fixture $fx }
}

function Test-RetentionIgnoresConflictCopy {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'RetentionIgnoresConflictCopy script exists' $scriptPath; return }
    . $scriptPath
    $valid = @(1..16 | ForEach-Object { 'antiphon-202610{0:d2}T000000Z.dump.age' -f $_ })
    $newest = 'antiphon-20261016T000000Z.dump.age'
    $conflict = 'antiphon-20261001T000000Z (conflicted copy).dump.age'
    $kept = Get-SnapshotPruneSet -Names @($valid + $conflict) -Retain 14 -NewName $newest -Verified $true
    Assert-True ($kept.Ok -eq $true) 'conflict copy stays ok' $kept.Reason
    $drop = @($kept.Drop)
    Assert-True ($drop.Count -eq 2) 'conflict copy is not a retained slot' ("count=$($drop.Count) " + ($drop -join ','))
    Assert-True ($drop -notcontains $conflict) 'conflict copy is not deleted' ($drop -join ',')
    Assert-True ($drop -notcontains $newest) 'conflict run keeps the new file' ($drop -join ',')
    Assert-True ($drop -contains 'antiphon-20261001T000000Z.dump.age') 'conflict run drops the oldest valid' ($drop -join ',')
    Assert-True ($drop -contains 'antiphon-20261002T000000Z.dump.age') 'conflict run drops the next oldest' ($drop -join ',')
    if (-not (Get-Command Update-SnapshotRetention -ErrorAction SilentlyContinue)) {
        Write-Fail 'retention on disk' 'Update-SnapshotRetention missing'
        return
    }
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('card1405-retain-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        foreach ($name in @($valid + $conflict)) {
            Set-Content -LiteralPath (Join-Path $dir $name) -Value 'x' -Encoding ASCII
        }
        $applied = Update-SnapshotRetention -DestDir $dir -Keep 14 -NewName $newest
        Assert-True ($applied.Ok -eq $true) 'conflict copy does not fail prune' $applied.Reason
        Assert-True (Test-Path -LiteralPath (Join-Path $dir $conflict)) 'conflict file remains' $conflict
        Assert-True (Test-Path -LiteralPath (Join-Path $dir $newest)) 'new snapshot remains' $newest
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $dir 'antiphon-20261001T000000Z.dump.age'))) 'oldest valid removed' 'still present'
        $left = @(Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump\.age$' })
        Assert-True ($left.Count -eq 14) 'fourteen valid snapshots remain' "count=$($left.Count)"
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-StagingHygiene {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'StagingHygiene script exists' $scriptPath; return }
        $stale = Join-Path $fx.Stage 'leftover.dump'
        $notes = Join-Path $fx.Stage 'notes.txt'
        $backup = Join-Path $fx.Stage 'dev-backup-20261020.dump'
        Set-Content -LiteralPath $stale -Value 'PLAINTEXT-STALE-1405' -Encoding ASCII
        Set-Content -LiteralPath $notes -Value 'operator notes' -Encoding ASCII
        Set-Content -LiteralPath $backup -Value 'operator dump' -Encoding ASCII
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-Execute', '-RecipientFile', $fx.Recipient,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True (Test-Path -LiteralPath $fx.Stage) 'execute keeps the staging root' $fx.Stage
        Assert-True (Test-Path -LiteralPath $stale) 'execute keeps an unrelated dump' $stale
        Assert-True (Test-Path -LiteralPath $notes) 'execute keeps notes' $notes
        Assert-True (Test-Path -LiteralPath $backup) 'execute keeps dev-backup' $backup
        $runs = @(Get-ChildItem -LiteralPath $fx.Stage -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^run-' })
        Assert-True ($runs.Count -eq 0) 'execute removes its run directory' ("count=$($runs.Count)")
        $blob = $result.Out + "`n" + $result.Err
        Assert-True ($blob.IndexOf('PLAINTEXT-STALE-1405', [StringComparison]::Ordinal) -lt 0) 'execute hides stale plaintext' 'plaintext leaked'
        Assert-NoSecret $result $fx 'execute staging'
    } finally { Remove-Fixture $fx }
    . $scriptPath
    if (-not (Get-Command Initialize-SnapshotStaging -ErrorAction SilentlyContinue)) {
        Write-Fail 'staging initializer' 'Initialize-SnapshotStaging missing'
        return
    }
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('card1405-stage-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $dir '700' }
        Set-Content -LiteralPath (Join-Path $dir 'old.dump') -Value 'x' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $dir 'keep.dump.age') -Value 'y' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $dir 'notes.txt') -Value 'z' -Encoding ASCII
        $priorName = 'run-' + ('ab' * 16)
        $prior = Join-Path $dir $priorName
        New-Item -ItemType Directory -Path $prior | Out-Null
        if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $prior '700' }
        Set-Content -LiteralPath (Join-Path $prior 'antiphon-20261021T010101Z.dump') -Value 'owned' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $prior 'notes.txt') -Value 'keep' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $prior 'dev-backup-20261020.dump') -Value 'keep' -Encoding ASCII
        if (-not (Set-SnapshotPlainDirectoryAcl $prior)) {
            Write-Fail 'owned run directory' 'acl'
            return
        }
        (Get-Item -LiteralPath $prior -Force).LastWriteTimeUtc = [datetime]::UtcNow.AddHours(-8)
        $ok = Initialize-SnapshotStaging $dir
        Assert-True ($ok -eq $true) 'staging mode applied' "ok=$ok"
        Assert-True (Test-Path -LiteralPath (Join-Path $dir 'old.dump')) 'root dump kept' 'old.dump missing'
        Assert-True (Test-Path -LiteralPath (Join-Path $dir 'keep.dump.age')) 'ciphertext kept' 'keep.dump.age missing'
        Assert-True (Test-Path -LiteralPath (Join-Path $dir 'notes.txt')) 'unrelated file kept' 'notes.txt missing'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $prior 'antiphon-20261021T010101Z.dump'))) 'owned plaintext swept' 'owned dump remains'
        Assert-True (Test-Path -LiteralPath (Join-Path $prior 'notes.txt')) 'notes inside a run directory kept' 'run notes missing'
        Assert-True (Test-Path -LiteralPath (Join-Path $prior 'dev-backup-20261020.dump')) 'dev-backup inside a run directory kept' 'run backup missing'
        $created = @(Get-ChildItem -LiteralPath $dir -Force -Directory | Where-Object { $_.Name -match '^run-[0-9a-f]{32}$' -and $_.Name -ne $priorName })
        Assert-True ($created.Count -eq 1) 'staging opens one run directory' ("count=$($created.Count)")
        if ($env:OS -ne 'Windows_NT' -and $created.Count -eq 1) {
            $mode = Get-SnapshotHarnessMode $created[0].FullName
            Assert-True ($mode -eq '700') 'run directory is 0700' $mode
            $parent = Get-SnapshotHarnessMode $dir
            Assert-True ($parent -eq '700') 'staging root mode is unchanged' $parent
        }
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-VolumeUntouched {
    param($Result, [string]$Name)
    Assert-True ($Result.Tools -notmatch 'volume rm') "$Name does not remove a volume" $Result.Tools
    Assert-True ($Result.Tools -notmatch 'volume create') "$Name does not create a volume" $Result.Tools
    Assert-True ($Result.Tools -notmatch 'run -d') "$Name does not start postgres" $Result.Tools
}

function Test-DrillRefusesOwnVolume {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesOwnVolume script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'own-volume' @(
            '-Container', 'custom-pg',
            '-SourceVolume', 'antiphon_pgdata',
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'custom_pgdata'
        ) '1048576'
        Assert-DrillRefused $result $fx 'source-mount' 'own volume'
        Assert-VolumeUntouched $result 'own volume'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesForeignVolume {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesForeignVolume script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $live = Invoke-DrillCase $fx 'foreign-volume' @(
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'windmill_db'
        ) '1048576'
        Assert-DrillRefused $live $fx 'foreign-mount' 'foreign volume'
        Assert-VolumeUntouched $live 'foreign volume'
        $plain = Invoke-DrillCase $fx 'plain-volume' @(
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'windmill_db'
        ) '1048576'
        Assert-DrillRefused $plain $fx 'unlabelled-volume' 'unlabelled volume'
        Assert-VolumeUntouched $plain 'unlabelled volume'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesVolumeMismatch {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesVolumeMismatch script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'mismatch' @(
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'windmill_db'
        ) '1048576'
        Assert-DrillRefused $result $fx 'volume-mismatch' 'volume mismatch'
        Assert-VolumeUntouched $result 'volume mismatch'
    } finally { Remove-Fixture $fx }
}

function Test-DrillRefusesPgHost {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesPgHost script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-DrillCase $fx 'pghost' @(
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'antiphon-postgres-drill-pgdata'
        ) '1048576'
        Assert-DrillRefused $result $fx 'pghost' 'pghost'
        Assert-True ($result.Tools -notmatch 'volume rm') 'pghost leaves volumes' $result.Tools
    } finally { Remove-Fixture $fx }
}

function Test-StagingPreservesOperatorFiles {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'StagingPreservesOperatorFiles script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $notes = Join-Path $fx.Stage 'notes.txt'
        $backup = Join-Path $fx.Stage 'dev-backup-20261020.dump'
        Set-Content -LiteralPath $notes -Value 'operator notes' -Encoding ASCII
        Set-Content -LiteralPath $backup -Value 'operator dump' -Encoding ASCII
        $refused = Invoke-DrillCase $fx 'prefix' @('-ScratchContainer', '0123abcd9f')
        Assert-DrillRefused $refused $fx 'id-prefix' 'staging drill refusal'
        Assert-True (Test-Path -LiteralPath $notes) 'refused drill keeps notes' $notes
        Assert-True (Test-Path -LiteralPath $backup) 'refused drill keeps dev-backup' $backup
        Assert-True (Test-Path -LiteralPath $fx.Stage) 'refused drill keeps the staging root' $fx.Stage
        if (-not (Test-Path -LiteralPath $fx.Stage)) {
            New-Item -ItemType Directory -Path $fx.Stage | Out-Null
        }
        Set-Content -LiteralPath $notes -Value 'operator notes' -Encoding ASCII
        Set-Content -LiteralPath $backup -Value 'operator dump' -Encoding ASCII
        $published = Invoke-PublishCase $fx '1048576' '1'
        Assert-True ($published.Exit -eq 0) 'execute keeps operator staging and exits 0' "exit=$($published.Exit) $($published.Out)"
        Assert-True (Test-Path -LiteralPath $notes) 'execute keeps notes' $notes
        Assert-True (Test-Path -LiteralPath $backup) 'execute keeps dev-backup' $backup
        Assert-True (Test-Path -LiteralPath $fx.Stage) 'execute keeps the staging root' $fx.Stage
        $runs = @(Get-ChildItem -LiteralPath $fx.Stage -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^run-' })
        Assert-True ($runs.Count -eq 0) 'execute removes only its run directory' ("count=$($runs.Count)")
        Assert-NoSecret $published $fx 'execute operator staging'
    } finally { Remove-Fixture $fx }
}

function Test-StagingRefusesLink {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'StagingRefusesLink script exists' $scriptPath; return }
        $outside = Join-Path $fx.Root 'outside'
        New-Item -ItemType Directory -Path $outside | Out-Null
        $unix = -not ($env:OS -eq 'Windows_NT')
        if ($unix) { Set-SnapshotHarnessMode $outside '755' }
        $dump = Join-Path $outside 'other.dump'
        $notes = Join-Path $outside 'notes.txt'
        Set-Content -LiteralPath $dump -Value 'outside dump' -Encoding ASCII
        Set-Content -LiteralPath $notes -Value 'outside notes' -Encoding ASCII
        $link = Join-Path $fx.Root 'stage-link'
        New-SnapshotHarnessLink $link $outside
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-Execute', '-RecipientFile', $fx.Recipient,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $link
        )
        Assert-True ($result.Exit -eq 3) 'symlink staging exits 3' "exit=$($result.Exit) $($result.Out)"
        Assert-True ($result.Out -match 'reason=staging-link') 'symlink staging reason' $result.Out
        Assert-True (Test-Path -LiteralPath $dump) 'symlink staging keeps the outside dump' $dump
        Assert-True (Test-Path -LiteralPath $notes) 'symlink staging keeps outside notes' $notes
        if ($unix) {
            $mode = Get-SnapshotHarnessMode $outside
            Assert-True ($mode -eq '755') 'symlink staging leaves the outside mode' $mode
        } else {
            Write-Host 'SKIP symlink staging leaves the outside mode: unix mode bits are not a Windows property'
        }
        Assert-NoDocker $result 'symlink staging'
        Assert-NoSecret $result $fx 'symlink staging'
    } finally { Remove-Fixture $fx }
}

function Test-StagingOutsideDestination {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'StagingOutsideDestination script exists' $scriptPath; return }
        $dest = Join-Path $fx.Root 'offsync-dest'
        $stage = Join-Path $dest 'nested-stage'
        New-Item -ItemType Directory -Path $dest | Out-Null
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-RecipientFile', $fx.Recipient,
            '-Destination', $dest, '-SyncRoot', $fx.Sync, '-StagingPath', $stage
        )
        Assert-True ($result.Exit -eq 3) 'staging inside destination exits 3' "exit=$($result.Exit) $($result.Out)"
        Assert-True ($result.Out -match 'reason=staging-inside-destination') 'staging inside destination reason' $result.Out
        Assert-NoDocker $result 'staging inside destination'
        Assert-NoSecret $result $fx 'staging inside destination'
        Assert-True (-not (Test-Path -LiteralPath $stage)) 'staging inside destination creates nothing' $stage
    } finally { Remove-Fixture $fx }
}

function Test-DrillRemovesScratch {
    $id = 'c' * 64
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRemovesScratch script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $removed = Invoke-DrillCase $fx 'clean' @(
            '-ScratchContainer', 'antiphon-postgres-drill',
            '-ScratchVolume', 'antiphon-postgres-drill-pgdata'
        ) '1048576'
        Assert-True ($removed.Exit -eq 0) 'drill remove exits 0' "exit=$($removed.Exit) $($removed.Out)"
        Assert-True ($removed.Tools -match 'volume create --label antiphon.snapshot-drill=1 antiphon-postgres-drill-pgdata') 'drill creates the labelled scratch volume' $removed.Tools
        Assert-True ($removed.Tools -match 'antiphon.snapshot-drill=1') 'drill labels the scratch container' $removed.Tools
        Assert-True ($removed.Tools -match ('rm -f ' + $id)) 'drill removes the scratch container id' $removed.Tools
        Assert-True ($removed.Tools -match 'volume rm antiphon-postgres-drill-pgdata') 'drill removes the scratch volume' $removed.Tools
        Assert-NoSecret $removed $fx 'drill remove'
    } finally { Remove-Fixture $fx }
    $kept = New-Fixture
    try {
        Install-SnapshotFake $kept
        $leave = Invoke-DrillCase $kept 'clean' @('-KeepScratch') '1048576'
        Assert-True ($leave.Exit -eq 0) 'KeepScratch exits 0' "exit=$($leave.Exit) $($leave.Out)"
        Assert-True ($leave.Tools -match 'antiphon.snapshot-drill=1') 'KeepScratch still labels' $leave.Tools
        Assert-True ($leave.Tools -notmatch ('rm -f ' + $id)) 'KeepScratch leaves the container' $leave.Tools
        Assert-True ($leave.Tools -notmatch 'volume rm') 'KeepScratch leaves the volume' $leave.Tools
        Assert-NoSecret $leave $kept 'KeepScratch'
    } finally { Remove-Fixture $kept }
}

function Test-DrillRefusesTaintedReuse {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillRefusesTaintedReuse script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $volume = 'antiphon-postgres-drill-pgdata'
        $common = @('-ScratchContainer', 'antiphon-postgres-drill', '-ScratchVolume', $volume)
        $hostaddr = Invoke-DrillCase $fx 'pghostaddr' $common '1048576' @($volume)
        Assert-DrillRefused $hostaddr $fx 'pghost' 'pghostaddr'
        Assert-True ($hostaddr.Tools -notmatch 'volume rm') 'pghostaddr leaves volumes' $hostaddr.Tools
        $pgenv = Invoke-DrillCase $fx 'pgenv' $common '1048576' @($volume)
        Assert-DrillRefused $pgenv $fx 'pg-env' 'pg env'
        Assert-True ($pgenv.Tools -notmatch 'volume rm') 'pg env leaves volumes' $pgenv.Tools
        $passfile = Invoke-DrillCase $fx 'pgpassfile' $common '1048576' @($volume)
        Assert-DrillRefused $passfile $fx 'pg-env' 'pgpassfile beside image env'
        $ssl = Invoke-DrillCase $fx 'pgssl' $common '1048576' @($volume)
        Assert-DrillRefused $ssl $fx 'pg-env' 'pgssl beside image env'
        $bind = Invoke-DrillCase $fx 'bind' $common '1048576' @($volume)
        Assert-DrillRefused $bind $fx 'bind-mount' 'bind mount'
        Assert-True ($bind.Tools -notmatch 'volume rm') 'bind mount leaves volumes' $bind.Tools
        $hostnet = Invoke-DrillCase $fx 'hostnet' $common '1048576' @($volume)
        Assert-DrillRefused $hostnet $fx 'host-network' 'host network'
        Assert-True ($hostnet.Tools -notmatch 'volume rm') 'host network leaves volumes' $hostnet.Tools
        $shared = Invoke-DrillCase $fx 'sharednet' $common '1048576' @($volume)
        Assert-DrillRefused $shared $fx 'shared-network' 'shared network'
        Assert-True ($shared.Tools -notmatch 'volume rm') 'shared network leaves volumes' $shared.Tools
    } finally { Remove-Fixture $fx }
}

# CARD-1413 D-1: the image's own PG_MAJOR, PG_VERSION and PGDATA are not libpq
# connection variables; a labelled scratch container that carries only them is
# reused, and the one the drill creates (DrillRemovesScratch) is accepted.
function Test-DrillReusesImageEnvScratch {
    $id = 'c' * 64
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillReusesImageEnvScratch script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $volume = 'antiphon-postgres-drill-pgdata'
        $reused = Invoke-DrillCase $fx 'reuse' @('-ScratchContainer', 'antiphon-postgres-drill', '-ScratchVolume', $volume) '1048576' @($volume)
        Assert-True ($reused.Exit -eq 0) 'image env reuse exits 0' "exit=$($reused.Exit) $($reused.Out)"
        Assert-True ($reused.Out -notmatch 'guard=') 'image env reuse trips no guard' $reused.Out
        Assert-True ($reused.Tools -notmatch 'run -d') 'image env reuse starts no container' $reused.Tools
        Assert-True ($reused.Tools -notmatch 'volume create') 'image env reuse creates no volume' $reused.Tools
        Assert-True ($reused.Tools -match ('exec ' + $id + ' createdb')) 'image env reuse creates the scratch database' $reused.Tools
        Assert-True ($reused.Tools -match ('exec ' + $id + ' pg_restore --no-owner')) 'image env reuse restores into the scratch container' $reused.Tools
        Assert-True ($reused.Tools -match ('rm -f ' + $id)) 'image env reuse removes the scratch container' $reused.Tools
        Assert-NoSecret $reused $fx 'image env reuse'
    } finally { Remove-Fixture $fx }
}

function Test-DockerDaemonPinned {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DockerDaemonPinned script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $common = @('-ScratchContainer', 'antiphon-postgres-drill', '-ScratchVolume', 'antiphon-postgres-drill-pgdata')
        $tcp = Invoke-DrillCase $fx 'clean' $common '1048576' @() @{ DOCKER_HOST = 'tcp://10.0.0.9:2375' }
        Assert-True ($tcp.Exit -eq 4) 'tcp DOCKER_HOST exits 4' "exit=$($tcp.Exit) $($tcp.Out)"
        Assert-True ($tcp.Out -match 'reason=docker-host') 'tcp DOCKER_HOST reason' $tcp.Out
        Assert-True ($tcp.Out -notmatch '10\.0\.0\.9') 'tcp DOCKER_HOST hides the endpoint' $tcp.Out
        Assert-NoDocker $tcp 'tcp DOCKER_HOST'
        Assert-NoSecret $tcp $fx 'tcp DOCKER_HOST'
        $runs = @(Get-ChildItem -LiteralPath $fx.Stage -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^run-' })
        Assert-True ($runs.Count -eq 0) 'tcp DOCKER_HOST opens no run directory' "count=$($runs.Count)"
        $context = Invoke-DrillCase $fx 'clean' $common '1048576' @() @{ CARD1405_DOCKER_ENDPOINT = 'tcp://10.0.0.9:2376' }
        Assert-True ($context.Exit -eq 4) 'tcp context exits 4' "exit=$($context.Exit) $($context.Out)"
        Assert-True ($context.Out -match 'reason=docker-context') 'tcp context reason' $context.Out
        Assert-True ($context.Out -notmatch '10\.0\.0\.9') 'tcp context hides the endpoint' $context.Out
        Assert-True ($context.Tools -match 'context inspect') 'tcp context reads the context' $context.Tools
        Assert-True ($context.Tools -notmatch 'docker inspect') 'tcp context inspects no container' $context.Tools
        Assert-True ($context.Tools -notmatch 'run -d') 'tcp context starts nothing' $context.Tools
        Assert-True ($context.Tools -notmatch 'pg_restore') 'tcp context restores nothing' $context.Tools
        Assert-NoSecret $context $fx 'tcp context'
        $empty = Invoke-DrillCase $fx 'clean' $common '1048576' @() @{ CARD1405_DOCKER_ENDPOINT = '' }
        Assert-True ($empty.Exit -eq 4 -and $empty.Out -match 'reason=docker-context') 'empty context endpoint exits 4' "exit=$($empty.Exit) $($empty.Out)"
        Assert-True ($empty.Tools -notmatch 'pg_restore') 'empty context endpoint restores nothing' $empty.Tools
        $unix = Invoke-DrillCase $fx 'clean' $common '1048576' @() @{ DOCKER_HOST = 'unix:///var/run/docker.sock' }
        Assert-True ($unix.Exit -eq 0) 'unix DOCKER_HOST passes' "exit=$($unix.Exit) $($unix.Out)"
        Assert-True ($unix.Tools -match 'context inspect') 'unix DOCKER_HOST still reads the context' $unix.Tools
        $allowed = Invoke-DrillCase $fx 'clean' ($common + @('-AllowRemoteDocker')) '1048576' @() @{ DOCKER_HOST = 'tcp://10.0.0.9:2375' }
        Assert-True ($allowed.Exit -eq 0) 'AllowRemoteDocker passes' "exit=$($allowed.Exit) $($allowed.Out)"
        Assert-True ($allowed.Out -match 'warning: remote docker') 'AllowRemoteDocker warns' $allowed.Out
        $before = @(Get-ChildItem -LiteralPath $fx.Dest -Filter '*.dump.age' -ErrorAction SilentlyContinue).Count
        $publish = Invoke-PublishCase $fx '1048576' '1' @{ DOCKER_HOST = 'tcp://10.0.0.9:2375' }
        Assert-True ($publish.Exit -eq 4) 'publish tcp DOCKER_HOST exits 4' "exit=$($publish.Exit) $($publish.Out)"
        Assert-True ($publish.Out -match 'reason=docker-host') 'publish tcp DOCKER_HOST reason' $publish.Out
        Assert-NoDocker $publish 'publish tcp DOCKER_HOST'
        $afterHost = @(Get-ChildItem -LiteralPath $fx.Dest -Filter '*.dump.age' -ErrorAction SilentlyContinue).Count
        Assert-True ($afterHost -eq $before -and -not (Test-Path -LiteralPath (Join-Path $fx.Dest 'snapshot-status.json'))) 'publish tcp DOCKER_HOST publishes nothing' "before=$before after=$afterHost"
        $publishContext = Invoke-PublishCase $fx '1048576' '1' @{ CARD1405_DOCKER_ENDPOINT = 'tcp://10.0.0.9:2376' }
        Assert-True ($publishContext.Exit -eq 4 -and $publishContext.Out -match 'reason=docker-context') 'publish tcp context exits 4' "exit=$($publishContext.Exit) $($publishContext.Out)"
        Assert-True ($publishContext.Tools -notmatch 'pg_dump') 'publish tcp context dumps nothing' $publishContext.Tools
        $afterContext = @(Get-ChildItem -LiteralPath $fx.Dest -Filter '*.dump.age' -ErrorAction SilentlyContinue).Count
        Assert-True ($afterContext -eq $before -and -not (Test-Path -LiteralPath (Join-Path $fx.Dest 'snapshot-status.json'))) 'publish tcp context publishes nothing' "before=$before after=$afterContext"
    } finally { Remove-Fixture $fx }
}

function Test-StagingRefusesForeignRoot {
    if ($env:OS -eq 'Windows_NT') {
        Write-Host 'SKIP StagingRefusesForeignRoot unix mode bits, sticky bit, and POSIX owner: Windows ACL coverage is WindowsAclRules'
        return
    }
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'StagingRefusesForeignRoot script exists' $scriptPath; return }
        $common = @('-Execute', '-RecipientFile', $fx.Recipient, '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync)
        $foreign = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-StagingPath', '/usr/share'))
        Assert-True ($foreign.Exit -eq 3) 'root-owned staging exits 3' "exit=$($foreign.Exit) $($foreign.Out)"
        Assert-True ($foreign.Out -match 'reason=staging-owner') 'root-owned staging reason' $foreign.Out
        Assert-NoDocker $foreign 'root-owned staging'
        Assert-NoSecret $foreign $fx 'root-owned staging'
        $runs = @(Get-ChildItem -LiteralPath '/usr/share' -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^run-[0-9a-f]{32}$' })
        Assert-True ($runs.Count -eq 0) 'root-owned staging opens no run directory' "count=$($runs.Count)"
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $loose = Join-Path $fx.Root 'loose'
        New-Item -ItemType Directory -Path $loose | Out-Null
        Set-SnapshotHarnessMode $loose '755'
        $mode = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-StagingPath', $loose))
        Assert-True ($mode.Exit -eq 3) 'group-readable staging exits 3' "exit=$($mode.Exit) $($mode.Out)"
        Assert-True ($mode.Out -match 'reason=staging-mode') 'group-readable staging reason' $mode.Out
        Assert-NoDocker $mode 'group-readable staging'
        Assert-True ((Get-SnapshotHarnessMode $loose) -eq '755') 'group-readable staging mode is unchanged' 'mode changed'
        $looseRuns = @(Get-ChildItem -LiteralPath $loose -Force -Directory -ErrorAction SilentlyContinue)
        Assert-True ($looseRuns.Count -eq 0) 'group-readable staging opens no run directory' "count=$($looseRuns.Count)"
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $open = Join-Path $fx.Root 'open'
        $under = Join-Path $open 'stage'
        New-Item -ItemType Directory -Path $under -Force | Out-Null
        Set-SnapshotHarnessMode $open '777'
        Set-SnapshotHarnessMode $under '700'
        $parent = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-StagingPath', $under))
        Assert-True ($parent.Exit -eq 3) 'world-writable parent exits 3' "exit=$($parent.Exit) $($parent.Out)"
        Assert-True ($parent.Out -match 'reason=staging-parent') 'world-writable parent reason' $parent.Out
        Assert-NoDocker $parent 'world-writable parent'
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $sticky = Join-Path $fx.Root 'sticky'
        $underSticky = Join-Path $sticky 'stage'
        New-Item -ItemType Directory -Path $underSticky -Force | Out-Null
        Set-SnapshotHarnessMode $sticky '1777'
        Set-SnapshotHarnessMode $underSticky '700'
        $stickyRun = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-StagingPath', $underSticky))
        Assert-True ($stickyRun.Out -notmatch 'reason=staging-') 'sticky parent passes the staging checks' $stickyRun.Out
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $fresh = Join-Path $fx.Root 'fresh'
        $created = Invoke-Snapshot -Fixture $fx -Arguments ($common + @('-StagingPath', $fresh))
        Assert-True ($created.Out -notmatch 'reason=staging-') 'absent staging root passes the staging checks' $created.Out
        Assert-True (Test-Path -LiteralPath $fresh -PathType Container) 'absent staging root is created' $fresh
        if (Test-Path -LiteralPath $fresh) {
            Assert-True ((Get-SnapshotHarnessMode $fresh) -eq '700') 'created staging root is 0700' (Get-SnapshotHarnessMode $fresh)
        }
    } finally { Remove-Fixture $fx }
}

function Test-StagingSkipsLiveRun {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'StagingSkipsLiveRun script exists' $scriptPath; return }
    . $scriptPath
    if (-not (Get-Command Initialize-SnapshotStaging -ErrorAction SilentlyContinue)) {
        Write-Fail 'live staging initializer' 'Initialize-SnapshotStaging missing'
        return
    }
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('card1413-live-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $dir '700' }
    $old = [datetime]::UtcNow.AddHours(-8)
    $dumpName = 'antiphon-20261021T010101Z.dump'
    $lock = $null
    try {
        $stale = Join-Path $dir ('run-' + ('aa' * 16))
        New-Item -ItemType Directory -Path $stale | Out-Null
        if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $stale '700' }
        Set-Content -LiteralPath (Join-Path $stale $dumpName) -Value 'stale' -Encoding ASCII
        if (-not (Set-SnapshotPlainDirectoryAcl $stale)) {
            Write-Fail 'stale run directory' 'acl'
            return
        }
        (Get-Item -LiteralPath $stale -Force).LastWriteTimeUtc = $old
        $live = Join-Path $dir ('run-' + ('bb' * 16))
        New-Item -ItemType Directory -Path $live | Out-Null
        if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $live '700' }
        Set-Content -LiteralPath (Join-Path $live $dumpName) -Value 'live' -Encoding ASCII
        if (-not (Set-SnapshotPlainDirectoryAcl $live)) {
            Write-Fail 'locked run directory' 'acl'
            return
        }
        $lock = [System.IO.File]::Open((Join-Path $live 'run.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        (Get-Item -LiteralPath $live -Force).LastWriteTimeUtc = $old
        $fresh = Join-Path $dir ('run-' + ('cc' * 16))
        New-Item -ItemType Directory -Path $fresh | Out-Null
        if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $fresh '700' }
        Set-Content -LiteralPath (Join-Path $fresh $dumpName) -Value 'fresh' -Encoding ASCII
        $mixed = Join-Path $dir ('run-' + ('dd' * 16))
        New-Item -ItemType Directory -Path $mixed | Out-Null
        if ($env:OS -ne 'Windows_NT') { Set-SnapshotHarnessMode $mixed '700' }
        Set-Content -LiteralPath (Join-Path $mixed $dumpName) -Value 'mixed' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $mixed 'notes.txt') -Value 'keep' -Encoding ASCII
        if (-not (Set-SnapshotPlainDirectoryAcl $mixed)) {
            Write-Fail 'mixed run directory' 'acl'
            return
        }
        (Get-Item -LiteralPath $mixed -Force).LastWriteTimeUtc = $old
        $ok = Initialize-SnapshotStaging $dir
        Assert-True ($ok -eq $true) 'live staging opens' "ok=$ok"
        Assert-True (-not (Test-Path -LiteralPath $stale)) 'old empty run directory is removed' $stale
        Assert-True (Test-Path -LiteralPath (Join-Path $live $dumpName)) 'locked run keeps its plaintext' 'live dump swept'
        Assert-True (Test-Path -LiteralPath (Join-Path $live 'run.lock')) 'locked run keeps its lock' 'live lock removed'
        Assert-True (Test-Path -LiteralPath (Join-Path $fresh $dumpName)) 'fresh run keeps its plaintext' 'fresh dump swept'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $mixed $dumpName))) 'old run plaintext is swept' 'mixed dump remains'
        Assert-True (Test-Path -LiteralPath (Join-Path $mixed 'notes.txt')) 'old run notes are kept' 'mixed notes removed'
        Assert-True (Test-Path -LiteralPath $mixed) 'old run directory with notes is kept' $mixed
        $run = [string]$script:SnapshotRunStaging
        Assert-True (-not [string]::IsNullOrWhiteSpace($run) -and (Test-Path -LiteralPath $run)) 'live staging opens a run directory' $run
        $lockPath = Join-Path $run 'run.lock'
        Assert-True (Test-Path -LiteralPath $lockPath) 'own run holds a lock file' $lockPath
        $probe = ''
        if (Test-Path -LiteralPath $lockPath) {
            $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
            $probeScript = "try { `$f = [System.IO.File]::Open('$lockPath', 'Open', 'ReadWrite', 'None'); `$f.Dispose(); 'free' } catch { 'held' }"
            $probe = ((& $pwsh -NoProfile -Command $probeScript) | Out-String).Trim()
        }
        Assert-True ($probe -eq 'held') 'own run lock is held against another process' $probe
        $removed = Remove-SnapshotStaging $run
        Assert-True ($removed -eq $true -and -not (Test-Path -LiteralPath $run)) 'own run directory is removed with its lock' "removed=$removed"
    } finally {
        if ($lock) { $lock.Dispose() }
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-DrillPicksExactSnapshot {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'DrillPicksExactSnapshot script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $manual = Join-Path $fx.Dest 'antiphon-20261022T000000Z-manual.dump.age'
        Set-Content -LiteralPath $manual -Value 'not a snapshot' -Encoding ASCII
        $common = @('-ScratchContainer', 'antiphon-postgres-drill', '-ScratchVolume', 'antiphon-postgres-drill-pgdata')
        $picked = Invoke-DrillCase $fx 'clean' $common '1048576'
        Assert-True ($picked.Exit -eq 0) 'exact snapshot drill exits 0' "exit=$($picked.Exit) $($picked.Out)"
        Assert-True ($picked.Tools -match 'age -d .*antiphon-20261021T120000Z\.dump\.age') 'drill decrypts the exact-pattern snapshot' $picked.Tools
        Assert-True ($picked.Tools -notmatch 'antiphon-20261022T000000Z-manual') 'drill ignores the non-snapshot file' $picked.Tools
        Assert-True (Test-Path -LiteralPath $manual) 'drill keeps the non-snapshot file' $manual
        $only = Invoke-DrillCase $fx 'clean' $common '1048576' @() @{} -NoSnapshot
        Assert-True ($only.Exit -eq 9) 'no exact snapshot exits 9' "exit=$($only.Exit) $($only.Out)"
        Assert-True ($only.Out -match 'reason=missing-snapshot') 'no exact snapshot reason' $only.Out
        Assert-True ($only.Tools -notmatch '/age -d') 'no exact snapshot decrypts nothing' $only.Tools
        Assert-True ($only.Tools -notmatch 'pg_restore') 'no exact snapshot restores nothing' $only.Tools
        Assert-True (Test-Path -LiteralPath $manual) 'no exact snapshot keeps the non-snapshot file' $manual
        Assert-NoSecret $only $fx 'no exact snapshot'
    } finally { Remove-Fixture $fx }
}

function Test-PublishRejectsEncryptFailure {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PublishRejectsEncryptFailure script exists' $scriptPath; return }
        Install-SnapshotFake $fx
        $result = Invoke-PublishCase $fx '1048576' '1' @{ CARD1405_AGE_EXIT = '1' }
        Assert-True ($result.Exit -eq 6) 'age failure exits 6' "exit=$($result.Exit) $($result.Out)"
        Assert-True ($result.Out -match 'reason=encrypt-failed') 'age failure reason' $result.Out
        Assert-True ($result.Tools -match '/age -R') 'age failure ran age' $result.Tools
        Assert-DestUntouched $fx 'age failure'
        $runs = @(Get-ChildItem -LiteralPath $fx.Stage -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^run-' })
        Assert-True ($runs.Count -eq 0) 'age failure removes its run directory' "count=$($runs.Count)"
        $plain = @(Get-ChildItem -LiteralPath $fx.Stage -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '\.dump(\.age)?$' })
        Assert-True ($plain.Count -eq 0) 'age failure leaves no plaintext or ciphertext' ("count=$($plain.Count)")
        Assert-True ($result.Tools -match 'rm -f /tmp/antiphon-snapshot-') 'age failure removes the container dump' $result.Tools
        Assert-NoSecret $result $fx 'age failure'
    } finally { Remove-Fixture $fx }
}

function Test-MissingAge {
    $fx = New-Fixture
    try {
        if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'MissingAge script exists' $scriptPath; return }
        foreach ($name in @('age', 'age.cmd')) {
            Remove-Item -LiteralPath (Join-Path $fx.Bin $name) -Force -ErrorAction SilentlyContinue
        }
        $publish = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-Execute', '-RecipientFile', $fx.Recipient,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($publish.Exit -eq 3) 'missing age exits 3' "exit=$($publish.Exit) $($publish.Out)"
        Assert-True ($publish.Out -match 'reason=missing-age') 'missing age reason' $publish.Out
        Assert-NoDocker $publish 'missing age'
        Assert-DestUntouched $fx 'missing age'
        $runs = @(Get-ChildItem -LiteralPath $fx.Stage -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^run-' })
        Assert-True ($runs.Count -eq 0) 'missing age opens no run directory' "count=$($runs.Count)"
        Assert-NoSecret $publish $fx 'missing age'
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $drill = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-Execute', '-RestoreDrill', '-IdentityFile', $fx.Identity,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($drill.Exit -eq 3) 'missing age drill exits 3' "exit=$($drill.Exit) $($drill.Out)"
        Assert-True ($drill.Out -match 'reason=missing-age') 'missing age drill reason' $drill.Out
        Assert-NoDocker $drill 'missing age drill'
        Assert-NoSecret $drill $fx 'missing age drill'
    } finally { Remove-Fixture $fx }
}

# CARD-1413 D-2: Windows DACL evaluation for the staging root, its parents and
# each run directory. On Linux this case proves the decision logic through the
# reader seam (plain SID strings and access masks, the shape the Get-Acl
# adapter produces); the adapter itself and directory creation with a DACL
# run only in the live section, which executes on Windows.
function Test-WindowsAclRules {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'WindowsAclRules script exists' $scriptPath; return }
    . $scriptPath
    foreach ($fn in @('Test-SnapshotWindowsRules', 'Test-SnapshotWindowsStagingRoot', 'Test-SnapshotWindowsOwnedDirectory', 'New-SnapshotPrivateDirectory')) {
        if (-not (Get-Command $fn -ErrorAction SilentlyContinue)) { Write-Fail 'windows acl function' "$fn missing"; return }
    }
    $me = 'S-1-5-21-1111111111-2222222222-3333333333-1001'
    $other = 'S-1-5-21-1111111111-2222222222-3333333333-1002'
    $system = 'S-1-5-18'
    $admins = 'S-1-5-32-544'
    $users = 'S-1-5-32-545'
    $everyone = 'S-1-1-0'
    $authenticated = 'S-1-5-11'
    $trusted = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
    $full = 2032127          # FileSystemRights.FullControl 0x1F01FF
    $rx = 1179817            # ReadAndExecute 0x1200A9
    $modify = 1245631        # Modify 0x1301BF
    $delete = 65536          # DELETE 0x10000
    $deleteChild = 64        # FILE_DELETE_CHILD 0x40
    $writeDac = 262144       # WRITE_DAC 0x40000
    $takeOwner = 524288      # WRITE_OWNER 0x80000
    $appendData = 4          # FILE_APPEND_DATA (create subdirectory)
    $genericAll = 268435456  # GENERIC_ALL 0x10000000
    function New-AclRule {
        param([string]$Sid, [int64]$Rights, [bool]$InheritOnly = $false, [string]$Type = 'Allow', [bool]$Inherited = $false)
        return [pscustomobject]@{ Sid = $Sid; Type = $Type; Rights = $Rights; InheritOnly = $InheritOnly; Inherited = $Inherited }
    }
    $profile = @((New-AclRule $system $full $false 'Allow' $true), (New-AclRule $admins $full $false 'Allow' $true), (New-AclRule $me $full $false 'Allow' $true))
    Assert-True (Test-SnapshotWindowsRules @((New-AclRule $me $full)) $me $true) 'caller-only DACL is strict ok' 'refused'
    Assert-True (Test-SnapshotWindowsRules $profile $me $true) 'profile default DACL is strict ok' 'refused'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $users $rx $false 'Allow' $true))) $me $true)) 'inherited Users read is strict refused' 'accepted'
    Assert-True (Test-SnapshotWindowsRules ($profile + @((New-AclRule $users $rx $false 'Allow' $true))) $me $false) 'inherited Users read on a parent is allowed' 'refused'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $everyone $delete))) $me $false)) 'Everyone Delete on a parent is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $users $deleteChild))) $me $false)) 'Users DeleteChild on a parent is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $everyone $writeDac))) $me $false)) 'Everyone ChangePermissions on a parent is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $authenticated $takeOwner))) $me $false)) 'Authenticated Users TakeOwnership on a parent is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $other $full $false 'Allow' $true))) $me $false)) 'another account FullControl on a parent is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules ($profile + @((New-AclRule $users $genericAll))) $me $false)) 'generic all on a parent is refused' 'accepted'
    $driveStyle = $profile + @((New-AclRule $authenticated $modify $true), (New-AclRule $authenticated $appendData), (New-AclRule $users $rx))
    Assert-True (Test-SnapshotWindowsRules $driveStyle $me $false) 'drive-root style create and inherit-only modify on a parent are allowed' 'refused'
    Assert-True (-not (Test-SnapshotWindowsRules $driveStyle $me $true)) 'drive-root style DACL on the root is strict refused' 'accepted'
    Assert-True (Test-SnapshotWindowsRules @((New-AclRule $me $full), (New-AclRule $everyone $full $false 'Deny')) $me $true) 'a Deny entry grants nothing' 'refused'
    Assert-True (-not (Test-SnapshotWindowsRules @((New-AclRule $me $full)) '' $true)) 'an unknown caller is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules $null $me $true)) 'an unreadable DACL is refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules @((New-AclRule $other $full)) $me $true)) 'another account alone is strict refused' 'accepted'
    Assert-True (-not (Test-SnapshotWindowsRules @((New-AclRule '' $full)) $me $true)) 'an entry without a SID is refused' 'accepted'

    $sep = [string][System.IO.Path]::DirectorySeparatorChar
    $root = ($sep + 'fakewin') + $sep + 'Users' + $sep + 'me' + $sep + 'AppData' + $sep + 'Local' + $sep + 'antiphon' + $sep + 'postgres-snapshot-staging'
    $chain = New-Object System.Collections.Generic.List[string]
    $cursor = [System.IO.Path]::GetFullPath($root)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        [void]$chain.Add($cursor)
        $parent = [System.IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
    $last = $chain.Count - 1
    Assert-True ($chain.Count -ge 4) 'fake chain has parents' ("count=$($chain.Count)")
    function New-AclChain {
        param([string]$RootOwner, $RootRules)
        $map = @{}
        for ($i = 0; $i -le $last; $i++) {
            if ($i -eq 0) {
                $map[$chain[$i]] = @{ Owner = $RootOwner; Rules = $RootRules }
            } elseif ($i -eq $last) {
                $map[$chain[$i]] = @{ Owner = $trusted; Rules = $driveStyle }
            } elseif ($i -eq ($last - 1)) {
                $map[$chain[$i]] = @{ Owner = $system; Rules = ($profile + @((New-AclRule $users $rx $false 'Allow' $true), (New-AclRule $everyone $rx))) }
            } else {
                $map[$chain[$i]] = @{ Owner = $me; Rules = $profile }
            }
        }
        return $map
    }
    function New-AclReader {
        param($Map)
        return { param([string]$Path) if ($Map.ContainsKey($Path)) { return [pscustomobject]$Map[$Path] } return $null }.GetNewClosure()
    }
    $callerOnly = @((New-AclRule $me $full))
    $ok = New-AclChain $me $callerOnly
    $okState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $ok) -Elevated $false
    Assert-True ($okState -eq '') 'profile chain passes the Windows staging checks' $okState
    $loose = New-AclChain $me ($profile + @((New-AclRule $users $rx $false 'Allow' $true)))
    $looseState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $loose) -Elevated $false
    Assert-True ($looseState -eq 'staging-mode') 'inherited Users read on the root is staging-mode' $looseState
    $foreign = New-AclChain $other $callerOnly
    $foreignState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $foreign) -Elevated $false
    Assert-True ($foreignState -eq 'staging-owner') 'root owned by another account is staging-owner' $foreignState
    $openParent = New-AclChain $me $callerOnly
    $openParent[$chain[3]] = @{ Owner = $me; Rules = ($profile + @((New-AclRule $everyone $delete))) }
    $openState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $openParent) -Elevated $false
    Assert-True ($openState -eq 'staging-parent') 'Everyone Delete on a parent is staging-parent' $openState
    $foreignParent = New-AclChain $me $callerOnly
    $foreignParent[$chain[2]] = @{ Owner = $other; Rules = $profile }
    $foreignParentState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $foreignParent) -Elevated $false
    Assert-True ($foreignParentState -eq 'staging-owner') 'parent owned by another account is staging-owner' $foreignParentState
    $inheritOnlyParent = New-AclChain $me $callerOnly
    $inheritOnlyParent[$chain[1]] = @{ Owner = $me; Rules = ($profile + @((New-AclRule $authenticated $modify $true), (New-AclRule $authenticated $appendData))) }
    $inheritOnlyState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $inheritOnlyParent) -Elevated $false
    Assert-True ($inheritOnlyState -eq '') 'inherit-only modify and create on a parent pass' $inheritOnlyState
    $elevated = New-AclChain $admins $callerOnly
    $elevatedState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $elevated) -Elevated $true
    Assert-True ($elevatedState -eq '') 'Administrators-owned root passes when elevated' $elevatedState
    $notElevatedState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $elevated) -Elevated $false
    Assert-True ($notElevatedState -eq 'staging-owner') 'Administrators-owned root is staging-owner when not elevated' $notElevatedState
    $unreadable = New-AclChain $me $callerOnly
    $unreadable.Remove($chain[0])
    $unreadableState = Test-SnapshotWindowsStagingRoot -Path $root -CallerSid $me -Reader (New-AclReader $unreadable) -Elevated $false
    Assert-True ($unreadableState -eq 'staging-owner') 'unreadable root security is staging-owner' $unreadableState
    $run = Join-Path $root ('run-' + ('ab' * 16))
    $runMap = @{}
    $runMap[[System.IO.Path]::GetFullPath($run)] = @{ Owner = $me; Rules = $callerOnly }
    Assert-True (Test-SnapshotWindowsOwnedDirectory -Path $run -CallerSid $me -Reader (New-AclReader $runMap) -Elevated $false) 'caller-only run directory is owned' 'refused'
    $runMap[[System.IO.Path]::GetFullPath($run)] = @{ Owner = $me; Rules = ($callerOnly + @((New-AclRule $users $rx $false 'Allow' $true))) }
    Assert-True (-not (Test-SnapshotWindowsOwnedDirectory -Path $run -CallerSid $me -Reader (New-AclReader $runMap) -Elevated $false)) 'Users read on a run directory is not owned' 'accepted'
    $runMap[[System.IO.Path]::GetFullPath($run)] = @{ Owner = $other; Rules = $callerOnly }
    Assert-True (-not (Test-SnapshotWindowsOwnedDirectory -Path $run -CallerSid $me -Reader (New-AclReader $runMap) -Elevated $false)) 'run directory owned by another account is not owned' 'accepted'

    if ($env:OS -eq 'Windows_NT') {
        $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('card1413-acl-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dir | Out-Null
        try {
            $private = Join-Path $dir 'private'
            $made = New-SnapshotPrivateDirectory $private
            Assert-True ($made -eq $true) 'live: private directory is created' "made=$made"
            # Access section only: Get-Acl/Set-Acl round-trip every section and
            # Set-Acl then needs SeSecurityPrivilege for the SACL, which an
            # ordinary desktop session does not hold (Review fe40955b).
            $di = New-Object System.IO.DirectoryInfo $private
            $accessOnly = [System.Security.AccessControl.AccessControlSections]::Access
            if ($PSVersionTable.PSEdition -eq 'Desktop') {
                $acl = $di.GetAccessControl($accessOnly)
            } else {
                $acl = [System.IO.FileSystemAclExtensions]::GetAccessControl($di, $accessOnly)
            }
            $caller = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            $rules = @($acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]))
            $names = @($rules | ForEach-Object { $_.IdentityReference.Value })
            $foreignRules = @($names | Where-Object { $_ -ne $caller })
            Assert-True ($acl.AreAccessRulesProtected) 'live: private directory DACL is protected' 'inherits'
            Assert-True ($foreignRules.Count -eq 0 -and $rules.Count -ge 1) 'live: private directory names only the caller' ($names -join ',')
            Assert-True (Test-SnapshotWindowsOwnedDirectory $private) 'live: private directory is owned' 'refused'
            $usersSid = New-Object System.Security.Principal.SecurityIdentifier($users)
            $grant = New-Object System.Security.AccessControl.FileSystemAccessRule($usersSid, [System.Security.AccessControl.FileSystemRights]::ReadAndExecute, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            $acl.AddAccessRule($grant)
            if ($PSVersionTable.PSEdition -eq 'Desktop') {
                $di.SetAccessControl($acl)
            } else {
                [System.IO.FileSystemAclExtensions]::SetAccessControl($di, $acl)
            }
            Assert-True (-not (Test-SnapshotWindowsOwnedDirectory $private)) 'live: Users read on a run directory is refused' 'accepted'
            $liveState = Test-SnapshotStagingRoot $private
            Assert-True ($liveState -eq 'staging-mode') 'live: Users read on the staging root is staging-mode' $liveState
        } finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
    } else {
        Write-Host 'SKIP WindowsAclRules live section (Get-Acl, DACL at creation): Windows only; the decision logic above ran here'
    }
}

function Test-PlainOffByDefault {
    $fx = New-Fixture
    try {
        $plan = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-RecipientFile', $fx.Recipient, '-Destination', $fx.Dest,
            '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($plan.Exit -eq 0) 'PlainOffByDefault exits 0' "exit=$($plan.Exit) $($plan.Out)"
        Assert-True ($plan.Out -match 'step encrypt:') 'PlainOffByDefault still encrypts' $plan.Out
        Assert-True ($plan.Out -notmatch 'PLAIN UNENCRYPTED') 'PlainOffByDefault is not the plain banner' $plan.Out
        Assert-True ($plan.Out -notmatch 'operator-decision=2026-11-10') 'PlainOffByDefault is not the plain decision' $plan.Out
        Assert-NoSecret $plan $fx 'PlainOffByDefault'
        Assert-NoDocker $plan 'PlainOffByDefault'
        Assert-DestUntouched $fx 'PlainOffByDefault'
    } finally { Remove-Fixture $fx }
}

function Test-PlainPlan {
    $fx = New-Fixture
    try {
        $dest = Join-Path $fx.Root 'backups'
        $plan = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-Destination', $dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($plan.Exit -eq 0) 'PlainPlan exits 0' "exit=$($plan.Exit) $($plan.Err) $($plan.Out)"
        Assert-True ($plan.Out -match 'PLAIN UNENCRYPTED') 'PlainPlan is loud' $plan.Out
        Assert-True ($plan.Out -match 'operator-decision=2026-11-10') 'PlainPlan names the operator decision' $plan.Out
        Assert-True ($plan.Out -match 'encryption=none') 'PlainPlan names encryption=none' $plan.Out
        Assert-True ($plan.Out -match 'outside-onedrive=yes') 'PlainPlan destination is outside OneDrive' $plan.Out
        Assert-True ($plan.Out -match 'step acl: user-only') 'PlainPlan checks user-only ACL' $plan.Out
        Assert-True ($plan.Out -match 'directory=absent') 'PlainPlan does not create the destination' $plan.Out
        Assert-True ($plan.Out -match 'keep 14') 'PlainPlan keeps 14' $plan.Out
        Assert-True ($plan.Out -match 'prune only after the new file is verified') 'PlainPlan prunes only after verify' $plan.Out
        Assert-True ($plan.Out -match 'pg_restore --list') 'PlainPlan verifies with pg_restore --list' $plan.Out
        Assert-True ($plan.Out -match '\.partial') 'PlainPlan writes partial then renames' $plan.Out
        Assert-True ($plan.Out -notmatch 'step encrypt:') 'PlainPlan does not encrypt' $plan.Out
        Assert-NoSecret $plan $fx 'PlainPlan'
        Assert-NoDocker $plan 'PlainPlan'
        Assert-True (-not (Test-Path -LiteralPath $dest)) 'PlainPlan writes nothing' $dest
        Assert-DestUntouched $fx 'PlainPlan'
    } finally { Remove-Fixture $fx }
}

function Test-PlainInsideOneDrive {
    $fx = New-Fixture
    try {
        $plan = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-OneDriveCopy', '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($plan.Exit -ne 0) 'PlainInsideOneDrive exits non-zero' "exit=$($plan.Exit)"
        Assert-True ($plan.Out -match 'reason=destination-inside-onedrive') 'PlainInsideOneDrive names the reason' $plan.Out
        Assert-True ($plan.Out -match 'outside-onedrive=no') 'PlainInsideOneDrive says the destination is inside' $plan.Out
        Assert-True ($plan.Out -notmatch 'step onedrive:') 'PlainInsideOneDrive does not start the OneDrive copy' $plan.Out
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $fx.Sync 'Backups'))) 'PlainInsideOneDrive writes nothing in OneDrive' $fx.Sync
        Assert-NoSecret $plan $fx 'PlainInsideOneDrive'
        Assert-NoDocker $plan 'PlainInsideOneDrive'
        Assert-DestUntouched $fx 'PlainInsideOneDrive'
    } finally { Remove-Fixture $fx }
}

function Test-PlainMissingOneDrive {
    $fx = New-Fixture
    try {
        $dest = Join-Path $fx.Root 'backups'
        $missing = Join-Path $fx.Root 'missing-onedrive'
        $plan = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-Destination', $dest, '-SyncRoot', $missing, '-StagingPath', $fx.Stage
        )
        Assert-True ($plan.Exit -eq 0) 'PlainMissingOneDrive exits 0' "exit=$($plan.Exit) $($plan.Out)"
        Assert-True ($plan.Out -match 'outside-onedrive=yes') 'PlainMissingOneDrive still checks the path' $plan.Out
        Assert-NoDocker $plan 'PlainMissingOneDrive'
        Assert-True (-not (Test-Path -LiteralPath $dest)) 'PlainMissingOneDrive writes nothing' $dest
        Assert-True (-not (Test-Path -LiteralPath $missing)) 'PlainMissingOneDrive does not create OneDrive' $missing
    } finally { Remove-Fixture $fx }
}

function Grant-PlainOpenAcl {
    param([string]$Path)
    if ($env:OS -eq 'Windows_NT') {
        $di = New-Object System.IO.DirectoryInfo $Path
        $accessOnly = [System.Security.AccessControl.AccessControlSections]::Access
        if ($PSVersionTable.PSEdition -eq 'Desktop') {
            $acl = $di.GetAccessControl($accessOnly)
        } else {
            $acl = [System.IO.FileSystemAclExtensions]::GetAccessControl($di, $accessOnly)
        }
        $users = New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-32-545'
        $grant = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $users,
            [System.Security.AccessControl.FileSystemRights]::ReadAndExecute,
            'ContainerInherit,ObjectInherit',
            'None',
            'Allow')
        $acl.AddAccessRule($grant)
        if ($PSVersionTable.PSEdition -eq 'Desktop') { $di.SetAccessControl($acl) } else { [System.IO.FileSystemAclExtensions]::SetAccessControl($di, $acl) }
        return
    }
    Set-SnapshotHarnessMode $Path '755'
}

function Test-PlainUsersAceRemains {
    param([string]$Path)
    if ($env:OS -ne 'Windows_NT') { return $true }
    $di = New-Object System.IO.DirectoryInfo $Path
    $accessOnly = [System.Security.AccessControl.AccessControlSections]::Access
    if ($PSVersionTable.PSEdition -eq 'Desktop') { $acl = $di.GetAccessControl($accessOnly) } else { $acl = [System.IO.FileSystemAclExtensions]::GetAccessControl($di, $accessOnly) }
    $names = @($acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]) | ForEach-Object { $_.IdentityReference.Value })
    return [bool]($names -contains 'S-1-5-32-545')
}

function Test-PlainAclOpen {
    $fx = New-Fixture
    try {
        $dest = Join-Path $fx.Root 'open-backups'
        New-Item -ItemType Directory -Path $dest | Out-Null
        Grant-PlainOpenAcl $dest
        $plan = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-Destination', $dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($plan.Exit -ne 0) 'PlainAclOpen exits non-zero' "exit=$($plan.Exit) $($plan.Out)"
        Assert-True ($plan.Out -match 'reason=destination-acl') 'PlainAclOpen names the reason' $plan.Out
        Assert-True ($plan.Out -match 'step acl: user-only') 'PlainAclOpen prints the ACL check' $plan.Out
        Assert-True ($plan.Out -match 'acl=open') 'PlainAclOpen reports the directory is open' $plan.Out
        Assert-NoDocker $plan 'PlainAclOpen'
        Assert-True (Test-Path -LiteralPath $dest) 'PlainAclOpen does not remove the directory' $dest
        Assert-True (Test-PlainUsersAceRemains $dest) 'PlainAclOpen does not tighten on a plan' $dest
        Assert-DestUntouched $fx 'PlainAclOpen'
    } finally { Remove-Fixture $fx }
}

function Test-PlainRetention {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PlainRetention script exists' $scriptPath; return }
    . $scriptPath
    $cmd = Get-Command Get-SnapshotPruneSet -ErrorAction SilentlyContinue
    if (-not $cmd -or -not $cmd.Parameters.ContainsKey('NamePattern')) {
        Write-Fail 'PlainRetention pattern' 'Get-SnapshotPruneSet -NamePattern missing'
        return
    }
    if (-not (Get-Variable -Name SnapshotPlainDumpPattern -Scope Script -ErrorAction SilentlyContinue)) {
        Write-Fail 'PlainRetention plain pattern' 'SnapshotPlainDumpPattern missing'
        return
    }
    $names = @(1..16 | ForEach-Object { 'antiphon-202610{0:d2}T000000Z.dump' -f $_ })
    $newest = 'antiphon-20261016T000000Z.dump'
    $kept = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $newest -Verified $true -NamePattern $script:SnapshotPlainDumpPattern
    Assert-True ($kept.Ok -eq $true) 'plain retention prunes when verified' $kept.Reason
    $drop = @($kept.Drop)
    Assert-True ($drop.Count -eq 2) 'plain retention keeps 14' ("count=$($drop.Count) " + ($drop -join ','))
    Assert-True ($drop -notcontains $newest) 'plain retention keeps the new file' ($drop -join ',')
    $unverified = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $newest -Verified $false -NamePattern $script:SnapshotPlainDumpPattern
    Assert-True ($unverified.Ok -eq $true -and @($unverified.Drop).Count -eq 0) 'plain retention skips an unverified file' $unverified.Reason
    $age = @($names | ForEach-Object { $_ + '.age' })
    $ignored = Get-SnapshotPruneSet -Names $age -Retain 14 -NewName ($newest + '.age') -Verified $true -NamePattern $script:SnapshotPlainDumpPattern
    Assert-True ($ignored.Ok -eq $false -and @($ignored.Drop).Count -eq 0) 'plain retention ignores ciphertext names' $ignored.Reason
}

function Test-PlainVerifyBeforePrune {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PlainVerify script exists' $scriptPath; return }
    . $scriptPath
    if (-not (Get-Command Test-SnapshotPlainVerified -ErrorAction SilentlyContinue)) {
        Write-Fail 'PlainVerify function' 'Test-SnapshotPlainVerified missing'
        return
    }
    $names = @(1..16 | ForEach-Object { 'antiphon-202610{0:d2}T000000Z.dump' -f $_ })
    $newest = 'antiphon-20261016T000000Z.dump'
    $pattern = $script:SnapshotPlainDumpPattern
    $noTable = Test-SnapshotPlainVerified -Lines @('10; 1259 1 TABLE public item owner') -Bytes 2000000 -MinimumBytes 1048576
    Assert-True ($noTable -eq $false) 'plain verify refuses a list without TABLE DATA' "verified=$noTable"
    $tiny = Test-SnapshotPlainVerified -Lines @('10; 0 1 TABLE DATA public item owner') -Bytes 10 -MinimumBytes 1048576
    Assert-True ($tiny -eq $false) 'plain verify refuses a tiny dump' "verified=$tiny"
    $blocked = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $newest -Verified $false -NamePattern $pattern
    Assert-True (@($blocked.Drop).Count -eq 0) 'plain verify failure prunes nothing' ($blocked.Drop -join ',')
    $ready = Test-SnapshotPlainVerified -Lines @('10; 0 1 TABLE DATA public item owner', '11; 0 2 TABLE DATA public other owner') -Bytes 2000000 -MinimumBytes 1048576
    Assert-True ($ready -eq $true) 'plain verify accepts TABLE DATA and size' "verified=$ready"
    $tables = Get-SnapshotPlainTableCount -Lines @('10; 0 1 TABLE DATA public item owner', '11; 0 2 TABLE DATA public other owner', '12; 1259 1 TABLE public item owner')
    Assert-True ($tables -eq 2) 'plain table count is TABLE DATA rows' "tables=$tables"
    $pruned = Get-SnapshotPruneSet -Names $names -Retain 14 -NewName $newest -Verified $true -NamePattern $pattern
    Assert-True ($pruned.Ok -eq $true -and @($pruned.Drop).Count -eq 2) 'plain verify success may prune' $pruned.Reason
}

function Test-PlainStatusNoSecret {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PlainStatus script exists' $scriptPath; return }
    . $scriptPath
    if (-not (Get-Command Write-SnapshotPlainStatus -ErrorAction SilentlyContinue)) {
        Write-Fail 'PlainStatus writer' 'Write-SnapshotPlainStatus missing'
        return
    }
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ('card1405-status-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $wrote = Write-SnapshotPlainStatus -Dir $dir -Utc '20261110T075000Z' -Code 0 -Bytes 1234 -Sha256Prefix 'abcdef123456' -DurationSec 7 -Tables 4
        Assert-True ($wrote -eq $true) 'PlainStatus writes' "wrote=$wrote"
        $final = Join-Path $dir 'snapshot-status.json'
        $partial = Join-Path $dir 'snapshot-status.json.partial'
        Assert-True (Test-Path -LiteralPath $final) 'PlainStatus renames into place' $final
        Assert-True (-not (Test-Path -LiteralPath $partial)) 'PlainStatus leaves no partial' $partial
        $json = [System.IO.File]::ReadAllText($final)
        Assert-True ($json -match '"utc":"20261110T075000Z"') 'PlainStatus has utc' $json
        Assert-True ($json -match '"exit":0') 'PlainStatus has exit' $json
        Assert-True ($json -match '"bytes":1234') 'PlainStatus has bytes' $json
        Assert-True ($json -match '"sha256":"abcdef123456"') 'PlainStatus has a sha256 prefix' $json
        Assert-True ($json -match '"duration":7') 'PlainStatus has duration' $json
        Assert-True ($json -match '"tables":4') 'PlainStatus has table count' $json
        Assert-True ($json -match '"mode":"plain"') 'PlainStatus is loud about plain mode' $json
        Assert-True ($json -match '"encryption":"none"') 'PlainStatus is loud about encryption' $json
        Assert-True ($json -match '"operatorDecision":"2026-11-10"') 'PlainStatus records the operator decision' $json
        Assert-True ($json -notmatch '(?i)password\s*=') 'PlainStatus has no password' $json
        Assert-True ($json -notmatch '(?i)host\s*=') 'PlainStatus has no host' $json
        Assert-True ($json -notmatch '(?i)postgres://') 'PlainStatus has no connection string' $json
        Assert-True ($json -notmatch 'CANARY') 'PlainStatus has no canary' $json
    } finally {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-PlainOneDriveCopy {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PlainOneDrive script exists' $scriptPath; return }
    . $scriptPath
    foreach ($fn in @('Invoke-SnapshotPlainOneDriveCopy', 'Confirm-SnapshotPlainOneDriveFile')) {
        if (-not (Get-Command $fn -ErrorAction SilentlyContinue)) {
            Write-Fail 'OneDrive copy function' "$fn missing"
            return
        }
    }
    $statusCmd = Get-Command Write-SnapshotPlainStatus -ErrorAction SilentlyContinue
    if (-not $statusCmd -or -not $statusCmd.Parameters.ContainsKey('OneDrive')) {
        Write-Fail 'PlainStatus oneDrive field' 'OneDrive parameter missing'
        return
    }
    if (-not (Get-Variable -Name SnapshotOneDriveRetain -Scope Script -ErrorAction SilentlyContinue)) {
        Write-Fail 'OneDrive retain' 'SnapshotOneDriveRetain missing'
        return
    }
    Assert-True ([int]$script:SnapshotOneDriveRetain -eq 7) 'OneDrive retention default is 7' ([string]$script:SnapshotOneDriveRetain)

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1831-od-' + [guid]::NewGuid().ToString('N'))
    $sync = Join-Path $root 'OneDrive'
    $local = Join-Path $root 'antiphon-backups'
    $outside = Join-Path $root 'outside'
    $missingRoot = Join-Path $root 'missing-onedrive'
    $statusDir = Join-Path $root 'status'
    New-Item -ItemType Directory -Path $sync, $local, $outside, $statusDir | Out-Null
    try {
        $localName = 'antiphon-20261110T104159Z.dump'
        $localFile = Join-Path $local $localName
        Set-Content -LiteralPath $localFile -Value 'PLAIN-DUMP-BYTES' -Encoding ASCII
        $canary = Join-Path $outside 'canary.txt'
        Set-Content -LiteralPath $canary -Value 'CANARY password=secret postgres://user Host=db' -Encoding ASCII
        $expectedFolder = Join-Path (Join-Path $missingRoot 'Backups') 'antiphon-postgres'

        $missing = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $missingRoot -Keep 7
        Assert-True ([string]$missing.State -eq 'missing') 'missing sync root is oneDrive=missing' ([string]$missing.State)
        Assert-True ([string]$missing.Loud -match 'oneDrive=missing') 'missing sync root is loud' ([string]$missing.Loud)
        Assert-True (-not (Test-Path -LiteralPath $missingRoot)) 'missing sync root is not created' $missingRoot
        Assert-True (-not (Test-Path -LiteralPath $expectedFolder)) 'missing sync root writes no OneDrive child' $expectedFolder
        Assert-True (Test-Path -LiteralPath $localFile) 'missing sync root keeps the local dump' $localFile
        Assert-True (Test-Path -LiteralPath $canary) 'missing sync root leaves the outside file' $canary
        Assert-True ([string]$missing.Loud -notmatch '(?i)password\s*=|postgres://|Host\s*=') 'missing sync root stdout has no secret' ([string]$missing.Loud)

        $noBackups = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $sync -Keep 7
        Assert-True ([string]$noBackups.State -eq 'missing') 'missing Backups folder is oneDrive=missing' ([string]$noBackups.State)
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $sync 'Backups'))) 'missing Backups folder is not created' $sync
        Assert-True (Test-Path -LiteralPath $localFile) 'missing Backups keeps the local dump' $localFile
        $syncTop = @(Get-ChildItem -LiteralPath $sync -Force | Select-Object -ExpandProperty Name)
        Assert-True ($syncTop.Count -eq 0) 'missing Backups writes nothing under the sync root' ($syncTop -join ',')

        $backups = Join-Path $sync 'Backups'
        $hermes = Join-Path $backups 'Hermes'
        $openclaw = Join-Path $backups 'openclaw'
        New-Item -ItemType Directory -Path $hermes, $openclaw | Out-Null
        $hermesFile = Join-Path $hermes 'keep.txt'
        $openclawFile = Join-Path $openclaw 'keep.txt'
        Set-Content -LiteralPath $hermesFile -Value 'keep-hermes' -Encoding ASCII
        Set-Content -LiteralPath $openclawFile -Value 'keep-openclaw' -Encoding ASCII
        $child = Join-Path $backups 'antiphon-postgres'
        New-Item -ItemType Directory -Path $child | Out-Null
        $badPartial = Join-Path $child ($localName + '.partial')
        $badFinal = Join-Path $child $localName
        Set-Content -LiteralPath $badPartial -Value 'DIFFERENT-BYTES' -Encoding ASCII
        $confirmed = Confirm-SnapshotPlainOneDriveFile -SourceFile $localFile -PartialFile $badPartial -FinalFile $badFinal
        Assert-True ($confirmed -eq $false) 'hash mismatch does not publish' "confirmed=$confirmed"
        Assert-True (-not (Test-Path -LiteralPath $badPartial)) 'hash mismatch removes the partial' $badPartial
        Assert-True (-not (Test-Path -LiteralPath $badFinal)) 'hash mismatch does not leave a final name' $badFinal

        $copied = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $sync -Keep 7
        Assert-True ([string]$copied.State -eq 'ok') 'verified copy is oneDrive=ok' ([string]$copied.State)
        $final = Join-Path $child $localName
        $partial = $final + '.partial'
        Assert-True (Test-Path -LiteralPath $final) 'copy renames onto the final name' $final
        Assert-True (-not (Test-Path -LiteralPath $partial)) 'copy leaves no partial' $partial
        $localHash = (Get-FileHash -LiteralPath $localFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $cloudHash = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-True ($localHash -eq $cloudHash) 'copy hash equals the local dump' $cloudHash
        $finalInfo = [System.IO.FileInfo]::new($final)
        Assert-True ([int64]$copied.Bytes -eq $finalInfo.Length) 'oneDriveBytes matches the file' ([string]$copied.Bytes)
        Assert-True ([System.IO.Path]::GetFullPath([string]$copied.Folder) -eq [System.IO.Path]::GetFullPath($child)) 'oneDrive folder is the child' ([string]$copied.Folder)
        Assert-True ((Get-Content -LiteralPath $hermesFile -Raw) -eq "keep-hermes`r`n" -or (Get-Content -LiteralPath $hermesFile -Raw) -eq "keep-hermes`n") 'copy leaves Hermes unchanged' (Get-Content -LiteralPath $hermesFile -Raw)
        Assert-True ((Get-Content -LiteralPath $openclawFile -Raw) -match 'keep-openclaw') 'copy leaves openclaw unchanged' $openclawFile
        Assert-True (Test-Path -LiteralPath $canary) 'copy leaves the outside file' $canary
        $topAfter = @(Get-ChildItem -LiteralPath $sync -Force | Select-Object -ExpandProperty Name)
        Assert-True (($topAfter.Count -eq 1) -and ($topAfter[0] -eq 'Backups')) 'copy writes nothing beside Backups' ($topAfter -join ',')
        Assert-True ([string]$copied.Loud -notmatch '(?i)password\s*=|postgres://|Host\s*=|CANARY') 'copy stdout has no secret' ([string]$copied.Loud)

        Remove-Item -LiteralPath $final -Force
        foreach ($day in 1..8) {
            $oldName = 'antiphon-202611{0:d2}T000000Z.dump' -f $day
            Set-Content -LiteralPath (Join-Path $child $oldName) -Value 'old' -Encoding ASCII
        }
        foreach ($day in 1..14) {
            $extra = Join-Path $local ('antiphon-202610{0:d2}T000000Z.dump' -f $day)
            Set-Content -LiteralPath $extra -Value 'local-old' -Encoding ASCII
        }
        $newest = Join-Path $local 'antiphon-20261110T120000Z.dump'
        Set-Content -LiteralPath $newest -Value 'NEW-DUMP' -Encoding ASCII
        $localBefore = @(Get-ChildItem -LiteralPath $local -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        $kept = Invoke-SnapshotPlainOneDriveCopy -LocalFile $newest -SyncRoot $sync -Keep 7
        Assert-True ([string]$kept.State -eq 'ok') 'retention copy is oneDrive=ok' ([string]$kept.State)
        $cloudLeft = @(Get-ChildItem -LiteralPath $child -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        Assert-True ($cloudLeft.Count -eq 7) 'OneDrive keeps 7' ("count=$($cloudLeft.Count)")
        Assert-True (Test-Path -LiteralPath (Join-Path $child 'antiphon-20261110T120000Z.dump')) 'OneDrive keeps the new file' 'missing'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $child 'antiphon-20261101T000000Z.dump'))) 'OneDrive drops the oldest' 'still present'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $child 'antiphon-20261102T000000Z.dump'))) 'OneDrive drops the next oldest' 'still present'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $child 'antiphon-20261110T120000Z.dump.partial'))) 'retention copy leaves no partial' 'partial remains'
        $localAfter = @(Get-ChildItem -LiteralPath $local -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        Assert-True ($localAfter.Count -eq $localBefore.Count) 'OneDrive prune does not change the local 14-cap folder' ("before=$($localBefore.Count) after=$($localAfter.Count)")
        Assert-True ($localAfter.Count -gt 7) 'local retention stays above the OneDrive cap' ("count=$($localAfter.Count)")

        $wrote = Write-SnapshotPlainStatus -Dir $statusDir -Utc '20261110T120000Z' -Code 0 -Bytes 18 -Sha256Prefix 'abcdef123456' -DurationSec 3 -Tables 2 -OneDrive 'ok' -OneDriveBytes 18 -OneDriveFolder $child
        Assert-True ($wrote -eq $true) 'status with oneDrive writes' "wrote=$wrote"
        $statusPath = Join-Path $statusDir 'snapshot-status.json'
        $statusJson = [System.IO.File]::ReadAllText($statusPath)
        $statusObj = $statusJson | ConvertFrom-Json
        Assert-True ([string]$statusObj.oneDrive -eq 'ok') 'status oneDrive=ok' $statusJson
        Assert-True ([int64]$statusObj.oneDriveBytes -eq 18) 'status oneDriveBytes' $statusJson
        Assert-True ([string]$statusObj.oneDriveFolder -eq $child) 'status oneDriveFolder' ([string]$statusObj.oneDriveFolder)
        Assert-True ($statusJson -notmatch '(?i)password\s*=|postgres://|Host\s*=|CANARY') 'status has no secret' $statusJson
        Assert-True (-not (Test-Path -LiteralPath ($statusPath + '.partial'))) 'status leaves no partial' $statusPath
        $wroteMissing = Write-SnapshotPlainStatus -Dir $statusDir -Utc '20261110T120002Z' -Code 0 -Bytes 18 -Sha256Prefix 'abcdef123456' -DurationSec 3 -Tables 2 -OneDrive 'missing' -OneDriveBytes 0 -OneDriveFolder $child
        Assert-True ($wroteMissing -eq $true) 'status missing writes' "wrote=$wroteMissing"
        $missingObj = ([System.IO.File]::ReadAllText($statusPath)) | ConvertFrom-Json
        Assert-True ([string]$missingObj.oneDrive -eq 'missing') 'status oneDrive=missing' ([string]$missingObj.oneDrive)
        $wroteFailed = Write-SnapshotPlainStatus -Dir $statusDir -Utc '20261110T120003Z' -Code 0 -Bytes 18 -Sha256Prefix 'abcdef123456' -DurationSec 3 -Tables 2 -OneDrive 'failed' -OneDriveBytes 0 -OneDriveFolder $child
        Assert-True ($wroteFailed -eq $true) 'status failed writes' "wrote=$wroteFailed"
        $failedObj = ([System.IO.File]::ReadAllText($statusPath)) | ConvertFrom-Json
        Assert-True ([string]$failedObj.oneDrive -eq 'failed') 'status oneDrive=failed' ([string]$failedObj.oneDrive)
        $wrotePlain = Write-SnapshotPlainStatus -Dir $statusDir -Utc '20261110T120004Z' -Code 0 -Bytes 18 -Sha256Prefix 'abcdef123456' -DurationSec 3 -Tables 2
        Assert-True ($wrotePlain -eq $true) 'status without OneDrive still writes' "wrote=$wrotePlain"
        $plainJson = [System.IO.File]::ReadAllText($statusPath)
        Assert-True ($plainJson -notmatch 'oneDrive') 'ad-hoc status omits oneDrive' $plainJson
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-PlainOneDrivePlan {
    $source = ''
    if (Test-Path -LiteralPath $scriptPath) { $source = [System.IO.File]::ReadAllText($scriptPath) }
    if ($source -notmatch '\[switch\]\$OneDriveCopy') {
        Write-Fail 'OneDriveCopy switch' 'switch missing'
        return
    }
    $fx = New-Fixture
    try {
        $dest = Join-Path $fx.Root 'backups'
        $backups = Join-Path $fx.Sync 'Backups'
        New-Item -ItemType Directory -Path $backups | Out-Null
        $hermes = Join-Path $backups 'Hermes'
        New-Item -ItemType Directory -Path $hermes | Out-Null
        Set-Content -LiteralPath (Join-Path $hermes 'keep.txt') -Value 'keep-hermes' -Encoding ASCII
        $on = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-OneDriveCopy', '-Destination', $dest,
            '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($on.Exit -eq 0) 'OneDrive plan exits 0' "exit=$($on.Exit) $($on.Out)"
        Assert-True ($on.Out -match 'outside-onedrive=yes') 'OneDrive plan keeps the local destination outside OneDrive' $on.Out
        Assert-True ($on.Out -notmatch 'reason=destination-inside-onedrive') 'OneDrive plan does not trip the primary guard' $on.Out
        Assert-True ($on.Out -match 'step onedrive:') 'OneDrive plan names the copy step' $on.Out
        Assert-True ($on.Out -match '\.partial') 'OneDrive plan writes a partial' $on.Out
        Assert-True ($on.Out -match 'sha256') 'OneDrive plan checks the hash' $on.Out
        Assert-True ($on.Out -match 'keep 7') 'OneDrive plan keeps 7' $on.Out
        Assert-True ($on.Out -match 'unencrypted') 'OneDrive plan says the cloud copy is unencrypted' $on.Out
        $child = Join-Path $backups 'antiphon-postgres'
        Assert-True (-not (Test-Path -LiteralPath $child)) 'OneDrive plan does not create the child' $child
        Assert-True (-not (Test-Path -LiteralPath $dest)) 'OneDrive plan does not create the local destination' $dest
        Assert-True (Test-Path -LiteralPath (Join-Path $hermes 'keep.txt')) 'OneDrive plan leaves Hermes' $hermes
        Assert-NoSecret $on $fx 'OneDrive plan'
        Assert-NoDocker $on 'OneDrive plan'
        Remove-Item -LiteralPath $fx.Marker -Force -ErrorAction SilentlyContinue
        $off = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-Destination', $dest, '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage
        )
        Assert-True ($off.Exit -eq 0) 'ad-hoc plain plan exits 0' "exit=$($off.Exit) $($off.Out)"
        Assert-True ($off.Out -notmatch 'step onedrive:') 'ad-hoc plain plan does not copy to OneDrive' $off.Out
        Assert-True ($off.Out -notmatch 'keep 7') 'ad-hoc plain plan does not keep 7' $off.Out
        Assert-True (-not (Test-Path -LiteralPath $child)) 'ad-hoc plain plan writes nothing in OneDrive' $child
        Assert-NoDocker $off 'ad-hoc plain plan'
    } finally { Remove-Fixture $fx }
}

function Test-PlainDrillRefused {
    $fx = New-Fixture
    try {
        $dest = Join-Path $fx.Root 'backups'
        $plan = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-WhatIf', '-Plain', '-RestoreDrill', '-Destination', $dest,
            '-SyncRoot', $fx.Sync, '-StagingPath', $fx.Stage, '-IdentityFile', $fx.Identity
        )
        Assert-True ($plan.Exit -ne 0) 'PlainDrillRefused exits non-zero' "exit=$($plan.Exit) $($plan.Out)"
        Assert-True ($plan.Out -match 'reason=plain-drill-unsupported') 'PlainDrillRefused names the reason' $plan.Out
        Assert-NoDocker $plan 'PlainDrillRefused'
        Assert-True (-not (Test-Path -LiteralPath $dest)) 'PlainDrillRefused writes nothing' $dest
    } finally { Remove-Fixture $fx }
}

function Test-PlainOneDriveLink {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PlainOneDriveLink script exists' $scriptPath; return }
    . $scriptPath
    if (-not (Get-Command Invoke-SnapshotPlainOneDriveCopy -ErrorAction SilentlyContinue)) {
        Write-Fail 'PlainOneDriveLink copy function' 'missing'
        return
    }
    if (-not (Get-Command Test-SnapshotPlainForbiddenLink -ErrorAction SilentlyContinue)) {
        Write-Fail 'cloud-filter reparse is not a link' 'classifier missing'
        Write-Fail 'symlink link type is refused' 'classifier missing'
        Write-Fail 'junction link type is refused' 'classifier missing'
    } else {
        Assert-True (-not (Test-SnapshotPlainForbiddenLink -LinkType '' -ReparsePoint $true)) 'cloud-filter reparse is not a link' 'refused'
        Assert-True (Test-SnapshotPlainForbiddenLink -LinkType 'SymbolicLink' -ReparsePoint $true) 'symlink link type is refused' 'allowed'
        Assert-True (Test-SnapshotPlainForbiddenLink -LinkType 'Junction' -ReparsePoint $true) 'junction link type is refused' 'allowed'
    }
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1831-link-' + [guid]::NewGuid().ToString('N'))
    $local = Join-Path $root 'local'
    New-Item -ItemType Directory -Path $local -Force | Out-Null
    $localName = 'antiphon-20261110T150000Z.dump'
    $localFile = Join-Path $local $localName
    Set-Content -LiteralPath $localFile -Value 'NEW-DUMP' -Encoding ASCII
    try {
        $case = Join-Path $root 'backups-link'
        $outside = Join-Path $case 'outside'
        $sync = Join-Path $case 'OneDrive'
        $child = Join-Path $outside 'antiphon-postgres'
        New-Item -ItemType Directory -Path $sync -Force | Out-Null
        New-PlainOutsideTree $child
        Set-Content -LiteralPath (Join-Path $outside 'keep-root.txt') -Value 'ROOT' -Encoding ASCII
        New-Item -ItemType SymbolicLink -Path (Join-Path $sync 'Backups') -Target $outside | Out-Null
        $before = Get-PlainSnapshotStamp $outside
        $result = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $sync -Keep 7
        $after = Get-PlainSnapshotStamp $outside
        Assert-PlainLinkRefused $result $before $after 'Backups symlink' $localFile

        $case = Join-Path $root 'root-link'
        $real = Join-Path $case 'real-onedrive'
        $child = Join-Path (Join-Path $real 'Backups') 'antiphon-postgres'
        New-PlainOutsideTree $child
        $holder = Join-Path $case 'holder'
        New-Item -ItemType Directory -Path $holder -Force | Out-Null
        $sync = Join-Path $holder 'OneDrive'
        New-Item -ItemType SymbolicLink -Path $sync -Target $real | Out-Null
        $before = Get-PlainSnapshotStamp $real
        $result = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $sync -Keep 7
        $after = Get-PlainSnapshotStamp $real
        Assert-PlainLinkRefused $result $before $after 'Backups parent chain symlink' $localFile

        $case = Join-Path $root 'child-link'
        $sync = Join-Path $case 'OneDrive'
        $backups = Join-Path $sync 'Backups'
        $outside = Join-Path $case 'outside'
        $child = Join-Path $outside 'antiphon-postgres'
        New-Item -ItemType Directory -Path $backups -Force | Out-Null
        New-PlainOutsideTree $child
        New-Item -ItemType SymbolicLink -Path (Join-Path $backups 'antiphon-postgres') -Target $child | Out-Null
        $before = Get-PlainSnapshotStamp $outside
        $result = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $sync -Keep 7
        $after = Get-PlainSnapshotStamp $outside
        Assert-PlainLinkRefused $result $before $after 'child symlink' $localFile

        $case = Join-Path $root 'file-link'
        $sync = Join-Path $case 'OneDrive'
        $child = Join-Path (Join-Path $sync 'Backups') 'antiphon-postgres'
        $outside = Join-Path $case 'outside'
        New-Item -ItemType Directory -Path $child, $outside -Force | Out-Null
        $target = Join-Path $outside 'escaped.dump'
        Set-Content -LiteralPath $target -Value 'OUTSIDE-DUMP' -Encoding ASCII
        $dumpLink = Join-Path $child 'antiphon-20261101T000000Z.dump'
        New-Item -ItemType SymbolicLink -Path $dumpLink -Target $target | Out-Null
        foreach ($day in 2..8) {
            $name = 'antiphon-202611{0:d2}T000000Z.dump' -f $day
            Set-Content -LiteralPath (Join-Path $child $name) -Value ('old' + $day) -Encoding ASCII
        }
        Set-Content -LiteralPath (Join-Path $child 'notes.txt') -Value 'NOTES' -Encoding ASCII
        $before = Get-PlainSnapshotStamp $outside
        $result = Invoke-SnapshotPlainOneDriveCopy -LocalFile $localFile -SyncRoot $sync -Keep 7
        $after = Get-PlainSnapshotStamp $outside
        Assert-PlainLinkRefused $result $before $after 'symlinked dump file' $localFile
        Assert-True (Test-Path -LiteralPath $dumpLink) 'symlinked dump file remains' $dumpLink
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $child $localName))) 'symlinked dump writes no new cloud file' $child
        Assert-True ((Get-Content -LiteralPath $target -Raw) -match 'OUTSIDE-DUMP') 'symlinked dump leaves the outside bytes' $target
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-PlainPublishPruneOrder {
    if (-not (Test-Path -LiteralPath $scriptPath)) { Write-Fail 'PlainPublishPruneOrder script exists' $scriptPath; return }
    . $scriptPath
    if (-not (Get-Command Invoke-SnapshotPlainPublish -ErrorAction SilentlyContinue)) {
        Write-Fail 'PlainPublishPruneOrder publish function' 'missing'
        return
    }
    if (-not (Get-Command Update-SnapshotPlainRetention -ErrorAction SilentlyContinue)) {
        Write-Fail 'PlainPublishPruneOrder retention function' 'missing'
        return
    }
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('card1831-order-' + [guid]::NewGuid().ToString('N'))
    $dest = Join-Path $root 'local'
    $stage = Join-Path $root 'stage'
    $sync = Join-Path $root 'OneDrive'
    $cloud = Join-Path (Join-Path $sync 'Backups') 'antiphon-postgres'
    New-Item -ItemType Directory -Path $dest, $stage, $cloud -Force | Out-Null
    foreach ($day in 1..15) {
        $name = 'antiphon-202610{0:d2}T000000Z.dump' -f $day
        Set-Content -LiteralPath (Join-Path $dest $name) -Value ('local' + $day) -Encoding ASCII
    }
    foreach ($day in 1..8) {
        $name = 'antiphon-202611{0:d2}T000000Z.dump' -f $day
        Set-Content -LiteralPath (Join-Path $cloud $name) -Value ('cloud' + $day) -Encoding ASCII
    }
    $script:PlainDumpBytes = 4096
    $script:PlainExpectedHash = ''
    $script:PlainListExits = New-Object System.Collections.Generic.List[int]
    $script:PlainPruneTooEarly = $false
    $script:PlainPruneCount = 0
    $script:PlainFailSecondList = $true
    $script:PlainOriginalRetention = ${function:Update-SnapshotPlainRetention}
    $savedNative = ${function:Invoke-SnapshotNative}
    try {
        function Invoke-SnapshotNative {
            param([string]$Command, [string[]]$ArgumentList)
            $joined = [string]::Join(' ', @($ArgumentList))
            if ($joined -match 'pg_restore' -and $joined -match '--list') {
                $exitCode = 0
                if ($script:PlainFailSecondList -and $script:PlainListExits.Count -ge 1) { $exitCode = 1 }
                [void]$script:PlainListExits.Add($exitCode)
                if ($exitCode -ne 0) {
                    return [pscustomobject]@{ Exit = 1; Lines = @('verify failed') }
                }
                return [pscustomobject]@{ Exit = 0; Lines = @('3000; 0 12345 TABLE DATA public cards antiphon') }
            }
            if ($joined -match '(^| )wc( |$)') {
                $line = [string]$script:PlainDumpBytes + ' /tmp/antiphon-snapshot.dump'
                return [pscustomobject]@{ Exit = 0; Lines = @($line) }
            }
            if ($ArgumentList.Count -ge 1 -and [string]$ArgumentList[0] -eq 'cp') {
                $destArg = [string]$ArgumentList[$ArgumentList.Count - 1]
                if ($destArg -notmatch ':') {
                    $buffer = New-Object byte[] ([int]$script:PlainDumpBytes)
                    [System.IO.File]::WriteAllBytes($destArg, $buffer)
                    $script:PlainExpectedHash = (Get-FileHash -LiteralPath $destArg -Algorithm SHA256).Hash
                }
                return [pscustomobject]@{ Exit = 0; Lines = @() }
            }
            return [pscustomobject]@{ Exit = 0; Lines = @() }
        }
        function Update-SnapshotPlainRetention {
            param([string]$DestDir, [int]$Keep, [string]$NewName)
            $script:PlainPruneCount++
            $file = Join-Path $DestDir $NewName
            $hashOk = $false
            if (Test-Path -LiteralPath $file -PathType Leaf) {
                $have = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
                $hashOk = ($have -eq $script:PlainExpectedHash)
            }
            $exits = @($script:PlainListExits)
            $secondOk = $false
            if ($exits.Count -ge 2 -and [int]$exits[$exits.Count - 1] -eq 0) { $secondOk = $true }
            if (-not $secondOk -or -not $hashOk) { $script:PlainPruneTooEarly = $true }
            return & $script:PlainOriginalRetention -DestDir $DestDir -Keep $Keep -NewName $NewName
        }
        $failed = Invoke-SnapshotPlainPublish -SourceContainer 'antiphon-postgres' -SourceDatabase 'antiphon' -Stage $stage -DestDir $dest -Keep 14 -Docker 'docker' -MinimumBytes 4096 -CopyToOneDrive $true -SyncRoot $sync -OneDriveKeep 7
        Assert-True ([int]$failed.Code -eq 5) 'failed verify exits 5' ([string]$failed.Code + ' ' + [string]$failed.Reason)
        Assert-True ([string]$failed.Reason -eq 'verify-failed') 'failed verify reason' ([string]$failed.Reason)
        Assert-True ($script:PlainPruneCount -eq 0) 'failed verify does not prune' ("count=$($script:PlainPruneCount)")
        Assert-True (Test-Path -LiteralPath (Join-Path $dest 'antiphon-20261001T000000Z.dump')) 'failed verify keeps the oldest local dump' 'missing'
        Assert-True (Test-Path -LiteralPath (Join-Path $dest 'antiphon-20261002T000000Z.dump')) 'failed verify keeps the next local dump' 'missing'
        $localLeft = @(Get-ChildItem -LiteralPath $dest -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        Assert-True ($localLeft.Count -eq 15) 'failed verify leaves the local folder unpruned' ("count=$($localLeft.Count)")
        Assert-True (Test-Path -LiteralPath (Join-Path $cloud 'antiphon-20261101T000000Z.dump')) 'failed verify keeps the oldest cloud dump' 'missing'
        Assert-True (Test-Path -LiteralPath (Join-Path $cloud 'antiphon-20261102T000000Z.dump')) 'failed verify keeps the next cloud dump' 'missing'
        $cloudLeft = @(Get-ChildItem -LiteralPath $cloud -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        Assert-True ($cloudLeft.Count -eq 8) 'failed verify leaves the cloud folder unpruned' ("count=$($cloudLeft.Count)")

        $script:PlainListExits = New-Object System.Collections.Generic.List[int]
        $script:PlainPruneTooEarly = $false
        $script:PlainPruneCount = 0
        $script:PlainFailSecondList = $false
        $script:PlainExpectedHash = ''
        $ok = Invoke-SnapshotPlainPublish -SourceContainer 'antiphon-postgres' -SourceDatabase 'antiphon' -Stage $stage -DestDir $dest -Keep 14 -Docker 'docker' -MinimumBytes 4096 -CopyToOneDrive $true -SyncRoot $sync -OneDriveKeep 7
        Assert-True ([int]$ok.Code -eq 0) 'verified publish exits 0' ([string]$ok.Code + ' ' + [string]$ok.Reason)
        Assert-True (-not $script:PlainPruneTooEarly) 'prune runs only after verify and the hash match' ("early=$($script:PlainPruneTooEarly) count=$($script:PlainPruneCount)")
        Assert-True ($script:PlainPruneCount -ge 1) 'verified publish calls retention' ("count=$($script:PlainPruneCount)")
        $localAfter = @(Get-ChildItem -LiteralPath $dest -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        Assert-True ($localAfter.Count -eq 14) 'verified publish keeps 14 local dumps' ("count=$($localAfter.Count)")
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $dest 'antiphon-20261001T000000Z.dump'))) 'verified publish drops the oldest local dump' 'still present'
        Assert-True ($script:PlainListExits.Count -ge 2) 'verified publish lists the dump twice' ("lists=$($script:PlainListExits.Count)")
        $cloudAfter = @(Get-ChildItem -LiteralPath $cloud -File | Where-Object { $_.Name -match '^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$' })
        Assert-True ($cloudAfter.Count -eq 7) 'verified publish keeps 7 cloud dumps' ("count=$($cloudAfter.Count)")
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $cloud 'antiphon-20261101T000000Z.dump'))) 'verified publish drops the oldest cloud dump' 'still present'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $cloud 'antiphon-20261102T000000Z.dump'))) 'verified publish drops the next cloud dump' 'still present'
    } finally {
        Set-Item -Path function:Invoke-SnapshotNative -Value $savedNative
        if ($script:PlainOriginalRetention) {
            Set-Item -Path function:Update-SnapshotPlainRetention -Value $script:PlainOriginalRetention
        }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$all = @(
    'WhatIfPlan', 'MissingRecipient', 'RecipientInsideDestination',
    'DrillRefusesProduction', 'DrillPlan', 'Retention',
    'DrillRefusesIdPrefix', 'DrillRefusesAlias', 'DrillRefusesUnlabelled',
    'DrillRefusesSourceMount', 'DrillRefusesProductionLikeName', 'DrillRefusesScratchDatabase',
    'DrillRefusesOwnVolume', 'DrillRefusesForeignVolume', 'DrillRefusesVolumeMismatch', 'DrillRefusesPgHost',
    'PublishRejectsTinyDump', 'PublishRejectsMissingTableData',
    'RetentionIgnoresConflictCopy', 'StagingHygiene', 'StagingPreservesOperatorFiles',
    'StagingRefusesLink', 'StagingOutsideDestination', 'DrillRemovesScratch',
    'DrillRefusesTaintedReuse', 'DrillReusesImageEnvScratch', 'DockerDaemonPinned', 'StagingRefusesForeignRoot',
    'StagingSkipsLiveRun', 'DrillPicksExactSnapshot',
    'PublishRejectsEncryptFailure', 'MissingAge', 'WindowsAclRules',
    'PlainOffByDefault', 'PlainPlan', 'PlainInsideOneDrive', 'PlainMissingOneDrive', 'PlainAclOpen',
    'PlainRetention', 'PlainVerifyBeforePrune', 'PlainStatusNoSecret', 'PlainDrillRefused',
    'PlainOneDriveCopy', 'PlainOneDrivePlan', 'PlainOneDriveLink', 'PlainPublishPruneOrder'
)
$script:SnapshotPosixFakeCases = @(
    'DrillRefusesIdPrefix', 'DrillRefusesAlias', 'DrillRefusesUnlabelled',
    'DrillRefusesSourceMount', 'DrillRefusesProductionLikeName', 'DrillRefusesScratchDatabase',
    'PublishRejectsTinyDump', 'PublishRejectsMissingTableData',
    'DrillRefusesOwnVolume', 'DrillRefusesForeignVolume', 'DrillRefusesVolumeMismatch', 'DrillRefusesPgHost',
    'StagingPreservesOperatorFiles', 'DrillRemovesScratch',
    'DrillRefusesTaintedReuse', 'DrillReusesImageEnvScratch', 'DockerDaemonPinned',
    'DrillPicksExactSnapshot', 'PublishRejectsEncryptFailure'
)
function Invoke-SnapshotCase {
    param([string]$Name)
    if (($env:OS -eq 'Windows_NT') -and ($script:SnapshotPosixFakeCases -contains $Name)) {
        Write-Host ("SKIP {0} POSIX sh docker/age fake: the executable bit is not a Windows property" -f $Name)
        return
    }
    & ('Test-{0}' -f $Name)
}
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Host "unknown case $Case"; exit 2 }
    Invoke-SnapshotCase $Case
} else {
    foreach ($id in $all) { Invoke-SnapshotCase $id }
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
