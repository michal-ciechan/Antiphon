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
    $pathKey = 'PATH'
    if (-not $psi.Environment.ContainsKey('PATH') -and $psi.Environment.ContainsKey('Path')) { $pathKey = 'Path' }
    # Only the fixture binaries are visible. A later docker/age on PATH would make
    # Get-Command return every match and the script would invoke none of them.
    $psi.Environment[$pathKey] = $Fixture.Bin
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
clean_json='{"Id":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
server2_json='{"Id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","Name":"/antiphon-server2-postgres-postgres-1","Config":{"Labels":{}},"Mounts":[{"Name":"antiphon-server2-postgres_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["postgres"]}}}}'
custom_source_json='{"Id":"1212121212121212121212121212121212121212121212121212121212121212","Name":"/custom-pg","Config":{"Labels":{}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["custom-pg"]}}}}'
labelled_source_json='{"Id":"3434343434343434343434343434343434343434343434343434343434343434","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
mismatch_json='{"Id":"5656565656565656565656565656565656565656565656565656565656565656","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"scratcha","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
pghost_json='{"Id":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PGHOST=db.internal","PGPORT=5432"]},"Mounts":[{"Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
own_running_json='{"Id":"7878787878787878787878787878787878787878787878787878787878787878","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"}},"Mounts":[{"Name":"custom_pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{"bridge":{"Aliases":["antiphon-postgres-drill"]}}}}'
pghostaddr_json='{"Id":"9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a9a","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PGHOSTADDR=10.0.0.5"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
pgenv_json='{"Id":"9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b9b","Name":"/antiphon-postgres-drill","Config":{"Labels":{"antiphon.snapshot-drill":"1"},"Env":["PATH=/usr/bin","PGSERVICE=production"]},"HostConfig":{"NetworkMode":"none"},"Mounts":[{"Type":"volume","Name":"antiphon-postgres-drill-pgdata","Destination":"/var/lib/postgresql/data"}],"NetworkSettings":{"Networks":{}}}'
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
            /bin/dd if=/dev/zero of="$target" bs="$bytes" count=1 status=none 2>/dev/null || exit 1
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
    /bin/dd if=/dev/zero of="$out" bs="$bytes" count=1 status=none 2>/dev/null || exit 1
fi
exit 0
'@
    $encoding = New-Object System.Text.UTF8Encoding $false
    $dockerPath = Join-Path $Fixture.Bin 'docker'
    $agePath = Join-Path $Fixture.Bin 'age'
    [System.IO.File]::WriteAllText($dockerPath, $docker.Replace("`r`n", "`n"), $encoding)
    [System.IO.File]::WriteAllText($agePath, $age.Replace("`r`n", "`n"), $encoding)
    & chmod +x $dockerPath $agePath
}

function Invoke-DrillCase {
    param($Fixture, [string]$Scenario, [string[]]$Extra, [string]$PlainBytes = '32', [string[]]$Volumes = @())
    Remove-Item -LiteralPath $Fixture.Marker -Force -ErrorAction SilentlyContinue
    $dockerState = Join-Path $Fixture.Root 'docker-state'
    Remove-Item -LiteralPath $dockerState -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ($dockerState + '.volumes') -Force -ErrorAction SilentlyContinue
    foreach ($seed in @($Volumes)) {
        if (-not [string]::IsNullOrWhiteSpace($seed)) { Add-Content -LiteralPath ($dockerState + '.volumes') -Value $seed -Encoding ASCII }
    }
    $cipher = Join-Path $Fixture.Dest 'antiphon-20261021T120000Z.dump.age'
    Set-Content -LiteralPath $cipher -Value 'cipher' -Encoding ASCII
    $arguments = @(
        '-Execute', '-RestoreDrill', '-IdentityFile', $Fixture.Identity,
        '-Destination', $Fixture.Dest, '-SyncRoot', $Fixture.Sync, '-StagingPath', $Fixture.Stage
    ) + @($Extra)
    return Invoke-Snapshot -Fixture $Fixture -Arguments $arguments -ExtraEnv @{
        CARD1405_SCENARIO = $Scenario
        CARD1405_DOCKER_STATE = (Join-Path $Fixture.Root 'docker-state')
        CARD1405_DUMP_BYTES = '1048576'
        CARD1405_TABLE_DATA = '1'
        CARD1405_PLAIN_BYTES = $PlainBytes
    }
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
    param($Fixture, [string]$Bytes, [string]$TableData)
    Remove-Item -LiteralPath $Fixture.Marker -Force -ErrorAction SilentlyContinue
    return Invoke-Snapshot -Fixture $Fixture -Arguments @(
        '-Execute', '-RecipientFile', $Fixture.Recipient,
        '-Destination', $Fixture.Dest, '-SyncRoot', $Fixture.Sync, '-StagingPath', $Fixture.Stage
    ) -ExtraEnv @{
        CARD1405_SCENARIO = 'publish'
        CARD1405_DOCKER_STATE = (Join-Path $Fixture.Root 'docker-state')
        CARD1405_DUMP_BYTES = $Bytes
        CARD1405_TABLE_DATA = $TableData
        CARD1405_PLAIN_BYTES = '32'
    }
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
        if ($env:OS -ne 'Windows_NT') { & chmod 0755 -- $dir }
        Set-Content -LiteralPath (Join-Path $dir 'old.dump') -Value 'x' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $dir 'keep.dump.age') -Value 'y' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $dir 'notes.txt') -Value 'z' -Encoding ASCII
        $priorName = 'run-' + ('ab' * 16)
        $prior = Join-Path $dir $priorName
        New-Item -ItemType Directory -Path $prior | Out-Null
        Set-Content -LiteralPath (Join-Path $prior 'antiphon-20261021T010101Z.dump') -Value 'owned' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $prior 'notes.txt') -Value 'keep' -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $prior 'dev-backup-20261020.dump') -Value 'keep' -Encoding ASCII
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
            $mode = (& stat -c %a -- $created[0].FullName).Trim()
            Assert-True ($mode -eq '700') 'run directory is 0700' $mode
            $parent = (& stat -c %a -- $dir).Trim()
            Assert-True ($parent -eq '755') 'staging root mode is unchanged' $parent
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
        & chmod 0755 -- $outside
        $dump = Join-Path $outside 'other.dump'
        $notes = Join-Path $outside 'notes.txt'
        Set-Content -LiteralPath $dump -Value 'outside dump' -Encoding ASCII
        Set-Content -LiteralPath $notes -Value 'outside notes' -Encoding ASCII
        $link = Join-Path $fx.Root 'stage-link'
        New-Item -ItemType SymbolicLink -Path $link -Target $outside | Out-Null
        $result = Invoke-Snapshot -Fixture $fx -Arguments @(
            '-Execute', '-RecipientFile', $fx.Recipient,
            '-Destination', $fx.Dest, '-SyncRoot', $fx.Sync, '-StagingPath', $link
        )
        Assert-True ($result.Exit -eq 3) 'symlink staging exits 3' "exit=$($result.Exit) $($result.Out)"
        Assert-True ($result.Out -match 'reason=staging-link') 'symlink staging reason' $result.Out
        Assert-True (Test-Path -LiteralPath $dump) 'symlink staging keeps the outside dump' $dump
        Assert-True (Test-Path -LiteralPath $notes) 'symlink staging keeps outside notes' $notes
        $mode = (& stat -c %a -- $outside).Trim()
        Assert-True ($mode -eq '755') 'symlink staging leaves the outside mode' $mode
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

$all = @(
    'WhatIfPlan', 'MissingRecipient', 'RecipientInsideDestination',
    'DrillRefusesProduction', 'DrillPlan', 'Retention',
    'DrillRefusesIdPrefix', 'DrillRefusesAlias', 'DrillRefusesUnlabelled',
    'DrillRefusesSourceMount', 'DrillRefusesProductionLikeName', 'DrillRefusesScratchDatabase',
    'DrillRefusesOwnVolume', 'DrillRefusesForeignVolume', 'DrillRefusesVolumeMismatch', 'DrillRefusesPgHost',
    'PublishRejectsTinyDump', 'PublishRejectsMissingTableData',
    'RetentionIgnoresConflictCopy', 'StagingHygiene', 'StagingPreservesOperatorFiles',
    'StagingRefusesLink', 'StagingOutsideDestination', 'DrillRemovesScratch',
    'DrillRefusesTaintedReuse'
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
