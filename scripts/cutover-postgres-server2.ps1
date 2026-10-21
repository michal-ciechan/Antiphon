#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1407 cutover of the desktop Postgres volume to the server2 host.

    Default is -WhatIf, which prints phases 0 through 6 and their stop gates
    and touches nothing. -Execute without -ConfirmWindow refuses. The operator
    window is -Execute -ConfirmWindow. -Rollback -Execute -ConfirmWindow points
    AppHost back at the untouched desktop volume.

    The script never prints a password or a connection string, never removes a
    data directory, and never passes a worktree override to the restart script.
    ASCII-only: it may run under Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [switch]$ConfirmWindow,
    [switch]$Rollback,
    [switch]$AcceptDrillTime,
    [switch]$TailnetOnlyConfirmed,
    [string]$SnapshotReference,
    [string]$DrillRecord,
    [string]$ConnectionSecretFile,
    [string]$RcTip,
    [string]$Repository,
    [string]$ProjectId,
    [string]$LandPendingFixture,
    [string]$DumpDirectory,
    [string]$AgeRecipientFile,
    [string]$AgeIdentityFile,
    [string]$Server2SshHost = 'server2',
    [string]$Server2ComposeDir,
    [string]$TailnetAddress,
    [string]$HoldAgent,
    [string]$HoldReason,
    [string]$HoldUntil,
    [string]$ApiBase = 'http://localhost:17202'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$script:KeyTables = @(
    'TranscriptEntries',
    'AgentTasks',
    'SessionQueuedMessages',
    'AgentTaskLandNotifications',
    'AgentTaskEvents'
)

function Fail-Cutover {
    param([string]$Message, [int]$Code = 3)
    Write-Host $Message
    exit $Code
}

function Test-CutoverSha {
    param([string]$Value)
    return ($Value -match '^[0-9a-fA-F]{40}$' -or $Value -match '^[0-9a-fA-F]{64}$')
}

function Show-CutoverWhatIf {
    Write-Host 'PHASE 0 Pre-flight'
    Write-Host '  EXIT CHECK 0: verified snapshot reference exists, passed drill record, latency median at or under 20 ms, and the drill time gate.'
    Write-Host '  STOP GATE 0: refuse without a verified snapshot reference and a passed drill record, when latency is above 20 ms, when the drill misses the time gate, or when the new listener is reachable off the tailnet. No production writer has moved.'
    Write-Host 'PHASE 1 Quiesce'
    Write-Host '  EXIT CHECK 1: dispatch hold is recorded, landPending is empty, repository preview has no uncertain child, and StopOnly leaves AppHost ports free while 17204 and antiphon-postgres stay up.'
    Write-Host '  STOP GATE 1: landPending is not empty, the repository preview shows an uncertain child, StopOnly would relaunch, or port 17204 or 17280 was touched. Leave the old database in place.'
    Write-Host 'PHASE 2 Final dump and restore'
    Write-Host '  EXIT CHECK 2: pg_dump -Fc sees no other session, and pg_restore exits 0.'
    Write-Host '  STOP GATE 2: another session appears, or pg_restore exits non-zero. Do not dump under writers. Leave the desktop container up.'
    Write-Host 'PHASE 3 Verify'
    Write-Host '  EXIT CHECK 3: count(*) per user table, sequences, extensions, migration ids, and key-table minimum and maximum primary keys match.'
    Write-Host '  STOP GATE 3: any mismatch. Do not repoint.'
    Write-Host 'PHASE 4 Repoint and restart'
    Write-Host '  EXIT CHECK 4: desktop postgres is stopped, antiphon_pgdata remains listed, the user-secrets key is present with its value not displayed, and restart-apphost.ps1 -ExpectedServerSha exits 0.'
    Write-Host '  STOP GATE 4: compose offers to remove the data directory, restart exit is 3, or the SHA mismatches. Exit 5 keeps the restart lock. Go to phase 6 if the secret was already changed.'
    Write-Host 'PHASE 5 Post-checks'
    Write-Host '  EXIT CHECK 5: health is 200, the version SHA equals the rc tip and advertises land-v2, pool flags match, and server2 shows a desktop client.'
    Write-Host '  STOP GATE 5: a post-check fails. The operator runs phase 6. The script does not restart itself again.'
    Write-Host 'PHASE 6 Rollback'
    Write-Host '  EXIT CHECK 6: desktop postgres is started again and AppHost is restarted with -ExpectedServerSha against the untouched desktop volume.'
    Write-Host '  STOP GATE 6: if the new database has rows the old desktop volume lacks, the script stops and the operator chooses. Neither data directory is removed.'
    Write-Host 'WHATIF: does not POST, does not call docker, and does not call dotnet user-secrets set.'
    Write-Host 'REFUSE: without a verified snapshot reference and a passed drill record the window does not start.'
}

function Read-CutoverDrill {
    param([string]$Path)
    try {
        return (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json)
    } catch {
        Fail-Cutover 'REFUSED: passed drill record could not be read. Nothing was changed.'
    }
}

function Assert-CutoverPhase0Files {
    if ([string]::IsNullOrWhiteSpace($SnapshotReference) -or -not (Test-Path -LiteralPath $SnapshotReference -PathType Leaf)) {
        Fail-Cutover 'REFUSED: verified snapshot reference is missing. Nothing was changed.'
    }
    if ([string]::IsNullOrWhiteSpace($DrillRecord) -or -not (Test-Path -LiteralPath $DrillRecord -PathType Leaf)) {
        Fail-Cutover 'REFUSED: passed drill record is missing. Nothing was changed.'
    }
    $drill = Read-CutoverDrill $DrillRecord
    if ($drill.passed -ne $true) {
        Fail-Cutover 'REFUSED: drill record did not pass. Nothing was changed.'
    }
    $latency = $null
    if ($null -ne $drill.latencyMedianMs) { $latency = [double]$drill.latencyMedianMs }
    if ($null -eq $latency -or $latency -gt 20) {
        Fail-Cutover 'REFUSED: latency baseline gate is above 20 ms or empty. Nothing was changed.'
    }
    $dumpSec = $null
    $copySec = $null
    if ($null -ne $drill.dumpRestoreSec) { $dumpSec = [int]$drill.dumpRestoreSec }
    if ($null -ne $drill.copySec) { $copySec = [int]$drill.copySec }
    if ($null -eq $dumpSec -or $null -eq $copySec) {
        Fail-Cutover 'REFUSED: drill time gate is empty. Nothing was changed.'
    }
    $sum = $dumpSec + $copySec
    if ($sum -gt 1800) {
        Fail-Cutover 'REFUSED: drill dump plus copy exceeds the 30 minute envelope. Nothing was changed.'
    }
    if ($sum -gt 600 -and -not $AcceptDrillTime) {
        Fail-Cutover 'REFUSED: drill dump plus copy is above 600 seconds. Nothing was changed.'
    }
}

function Assert-CutoverLandFixture {
    if ([string]::IsNullOrWhiteSpace($LandPendingFixture)) { return }
    if (-not (Test-Path -LiteralPath $LandPendingFixture -PathType Leaf)) {
        Fail-Cutover 'REFUSED: landPending fixture is missing. StopOnly was not run.'
    }
    try {
        $doc = Get-Content -LiteralPath $LandPendingFixture -Raw -ErrorAction Stop | ConvertFrom-Json
    } catch {
        Fail-Cutover 'REFUSED: landPending fixture could not be read. StopOnly was not run.'
    }
    $count = 0
    if ($null -ne $doc.items) { $count = @($doc.items).Count }
    if ($count -ne 0) {
        Fail-Cutover 'REFUSED: landPending is not empty. StopOnly was not run.'
    }
    Fail-Cutover 'REFUSED: landPending fixture cannot start the operator window. StopOnly was not run.'
}

function Assert-CutoverOperatorInputs {
    if (-not (Test-CutoverSha $RcTip)) {
        Fail-Cutover 'REFUSED: rc tip must be the full SHA passed to -ExpectedServerSha. StopOnly was not run.'
    }
    $restart = Join-Path $Repository 'scripts/restart-apphost.ps1'
    $compose = Join-Path $Repository 'docker-compose.dev.yml'
    if ([string]::IsNullOrWhiteSpace($Repository) -or -not (Test-Path -LiteralPath $restart -PathType Leaf) -or -not (Test-Path -LiteralPath $compose -PathType Leaf)) {
        Fail-Cutover 'REFUSED: -Repository must be the main checkout. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($ConnectionSecretFile) -or -not (Test-Path -LiteralPath $ConnectionSecretFile -PathType Leaf)) {
        Fail-Cutover 'REFUSED: connection secret file is missing. Value not displayed. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($ProjectId)) {
        Fail-Cutover 'REFUSED: project id is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($DumpDirectory) -or -not (Test-Path -LiteralPath $DumpDirectory -PathType Container)) {
        Fail-Cutover 'REFUSED: dump directory is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($AgeRecipientFile) -or -not (Test-Path -LiteralPath $AgeRecipientFile -PathType Leaf)) {
        Fail-Cutover 'REFUSED: age recipient file is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($AgeIdentityFile) -or -not (Test-Path -LiteralPath $AgeIdentityFile -PathType Leaf)) {
        Fail-Cutover 'REFUSED: age identity file is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($Server2ComposeDir)) {
        Fail-Cutover 'REFUSED: server2 compose directory is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($TailnetAddress) -or -not $TailnetOnlyConfirmed) {
        Fail-Cutover 'REFUSED: tailnet address is missing or the listener was not confirmed tailnet-only. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($HoldAgent) -or [string]::IsNullOrWhiteSpace($HoldReason) -or [string]::IsNullOrWhiteSpace($HoldUntil)) {
        Fail-Cutover 'REFUSED: keep-six hold agent, reason, and until are required. StopOnly was not run.'
    }
}

function Invoke-CutoverGet {
    param([string]$Relative)
    $uri = $ApiBase.TrimEnd('/') + '/' + $Relative.TrimStart('/')
    try {
        return Invoke-RestMethod -Method Get -Uri $uri -TimeoutSec 15
    } catch {
        Fail-Cutover ('REFUSED: GET ' + $Relative + ' failed. Body not printed. StopOnly was not run.')
    }
}

function Invoke-CutoverRestart {
    param([switch]$StopOnly, [string]$ExpectedSha)
    $restart = Join-Path $Repository 'scripts/restart-apphost.ps1'
    $restartArgs = @('-NoProfile', '-File', $restart)
    if ($StopOnly) { $restartArgs += '-StopOnly' }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
        $restartArgs += @('-ExpectedServerSha', $ExpectedSha)
    }
    & pwsh @restartArgs
    return $LASTEXITCODE
}

function Set-CutoverConnectionSecret {
    param([switch]$Remove)
    $project = Join-Path $Repository 'Antiphon.AppHost/Antiphon.AppHost.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        Fail-Cutover 'REFUSED: AppHost project is missing. Value not displayed.'
    }
    if ($Remove) {
        Write-Host 'Removing ConnectionStrings:DefaultConnection. Value not displayed.'
        & dotnet user-secrets remove 'ConnectionStrings:DefaultConnection' --id 'aspire-antiphon-apphost' --project $project | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'ConnectionStrings:DefaultConnection was already absent or could not be removed. Value not displayed.'
        } else {
            Write-Host 'ConnectionStrings:DefaultConnection removed. Value not displayed.'
        }
        return
    }
    $raw = [System.IO.File]::ReadAllText($ConnectionSecretFile).Trim()
    if ([string]::IsNullOrWhiteSpace($raw) -or $raw.Contains("`n") -or $raw.Contains("`r")) {
        Fail-Cutover 'REFUSED: connection secret file must be one non-empty line. Value not displayed.'
    }
    Write-Host 'Setting ConnectionStrings:DefaultConnection from the secret file. Value not displayed.'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet user-secrets set 'ConnectionStrings:DefaultConnection' $raw --id 'aspire-antiphon-apphost' --project $project | Out-Null
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
        $raw = $null
    }
    if ($code -ne 0) {
        Fail-Cutover 'REFUSED: user-secrets set failed. Value not displayed.'
    }
    Write-Host 'ConnectionStrings:DefaultConnection is present. Value not displayed.'
}

function Invoke-CutoverPsql {
    param([string]$Target, [string]$Sql)
    $file = Join-Path $DumpDirectory ('cutover-query-' + [guid]::NewGuid().ToString('N') + '.sql')
    Set-Content -LiteralPath $file -Value $Sql -Encoding ascii
    try {
        if ($Target -eq 'source') {
            $raw = Get-Content -LiteralPath $file -Raw | & docker exec -i antiphon-postgres psql -U antiphon -d antiphon -At -v ON_ERROR_STOP=1 2>&1 | Out-String
        } else {
            $remote = "cd '$Server2ComposeDir' && docker compose -p antiphon-server2-postgres exec -T postgres psql -U antiphon -d antiphon -At -v ON_ERROR_STOP=1"
            $raw = Get-Content -LiteralPath $file -Raw | & ssh $Server2SshHost $remote 2>&1 | Out-String
        }
        if ($LASTEXITCODE -ne 0) { return $null }
        return $raw.Trim()
    } finally {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
}

function Assert-CutoverEqual {
    param([string]$Label, [string]$SourceText, [string]$TargetText)
    if ($null -eq $SourceText -or $null -eq $TargetText) {
        Fail-Cutover ('REFUSED: phase 3 query failed for ' + $Label + '. Row text was not printed.')
    }
    if ($SourceText -ne $TargetText) {
        Fail-Cutover ('REFUSED: phase 3 mismatch for ' + $Label + '. Row text was not printed. Do not repoint.')
    }
    Write-Host ('PHASE 3 match: ' + $Label)
}

function Invoke-CutoverVerify {
    $tables = Invoke-CutoverPsql -Target source -Sql "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' ORDER BY 1"
    $targetTables = Invoke-CutoverPsql -Target target -Sql "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' ORDER BY 1"
    Assert-CutoverEqual -Label 'user-table-names' -SourceText $tables -TargetText $targetTables
    foreach ($name in @($tables -split "`n")) {
        $ident = $name.Trim()
        if ([string]::IsNullOrWhiteSpace($ident)) { continue }
        if ($ident -notmatch '^[A-Za-z0-9_."]+$') {
            Fail-Cutover 'REFUSED: phase 3 table name was not a catalog identifier. Row text was not printed.'
        }
        $countSql = 'SELECT count(*) FROM ' + $ident
        Assert-CutoverEqual -Label ('count ' + $ident) -SourceText (Invoke-CutoverPsql -Target source -Sql $countSql) -TargetText (Invoke-CutoverPsql -Target target -Sql $countSql)
    }
    $seq = "SELECT schemaname || '.' || sequencename || ' ' || COALESCE(last_value::text, '') || ' ' || is_called::text FROM pg_sequences ORDER BY 1"
    Assert-CutoverEqual -Label 'sequences' -SourceText (Invoke-CutoverPsql -Target source -Sql $seq) -TargetText (Invoke-CutoverPsql -Target target -Sql $seq)
    $ext = "SELECT extname || ' ' || extversion FROM pg_extension ORDER BY 1"
    Assert-CutoverEqual -Label 'extensions' -SourceText (Invoke-CutoverPsql -Target source -Sql $ext) -TargetText (Invoke-CutoverPsql -Target target -Sql $ext)
    $mig = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1'
    Assert-CutoverEqual -Label 'migrations' -SourceText (Invoke-CutoverPsql -Target source -Sql $mig) -TargetText (Invoke-CutoverPsql -Target target -Sql $mig)
    foreach ($table in $script:KeyTables) {
        $keySql = "SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey) WHERE i.indrelid = 'public.`"$table`"'::regclass AND i.indisprimary ORDER BY array_position(i.indkey, a.attnum) LIMIT 1"
        $key = Invoke-CutoverPsql -Target source -Sql $keySql
        if ([string]::IsNullOrWhiteSpace($key) -or $key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            Fail-Cutover ('REFUSED: phase 3 primary key missing for ' + $table + '. Row text was not printed.')
        }
        $probe = 'SELECT count(*)::text || '' '' || min("' + $key + '")::text || '' '' || max("' + $key + '")::text FROM "' + $table + '"'
        Assert-CutoverEqual -Label ('key ' + $table) -SourceText (Invoke-CutoverPsql -Target source -Sql $probe) -TargetText (Invoke-CutoverPsql -Target target -Sql $probe)
    }
}

function Invoke-CutoverRollback {
    Assert-CutoverOperatorInputs
    $compose = Join-Path $Repository 'docker-compose.dev.yml'
    Write-Host 'PHASE 6: starting desktop postgres. The data directory is not removed.'
    & docker compose -f $compose start postgres
    if ($LASTEXITCODE -ne 0) { Fail-Cutover 'REFUSED: desktop postgres did not start.' }
    Set-CutoverConnectionSecret -Remove
    $code = Invoke-CutoverRestart -ExpectedSha $RcTip
    if ($code -ne 0) { Fail-Cutover ('REFUSED: rollback restart exited ' + $code + '. Do not start a second copy if the lock is held.') }
    Write-Host 'PHASE 6 exit check: AppHost restarted against the untouched desktop volume.'
    exit 0
}

function Invoke-CutoverWindow {
    Assert-CutoverPhase0Files
    Assert-CutoverLandFixture
    Assert-CutoverOperatorInputs

    $version = Invoke-CutoverGet '/api/version'
    $sha = [string]$version.version
    if ($sha.ToLowerInvariant() -ne $RcTip.ToLowerInvariant()) {
        Fail-Cutover 'REFUSED: GET /api/version SHA does not equal the rc tip. StopOnly was not run.'
    }
    $caps = @($version.capabilities)
    if ($caps -notcontains 'land-v2') {
        Fail-Cutover 'REFUSED: GET /api/version does not advertise land-v2. StopOnly was not run.'
    }
    $state = Invoke-CutoverGet '/api/diagnostics/session-state'
    $pool = $state.pool
    $pooling = $pool.Pooling
    if ($null -eq $pooling) { $pooling = $pool.pooling }
    $noReset = $pool.NoResetOnClose
    if ($null -eq $noReset) { $noReset = $pool.noResetOnClose }
    $maxAuto = $pool.MaxAutoPrepare
    if ($null -eq $maxAuto) { $maxAuto = $pool.maxAutoPrepare }
    $multi = $pool.Multiplexing
    if ($null -eq $multi) { $multi = $pool.multiplexing }
    if ($pooling -ne $true -or $noReset -ne $true -or [int]$maxAuto -ne 0 -or $multi -ne $false) {
        Fail-Cutover 'REFUSED: pool flags differ from the expected window. Connection text was not printed. StopOnly was not run.'
    }
    Write-Host 'PHASE 0 pool flags match. Connection text was not printed.'

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $wait = $client.BeginConnect($TailnetAddress, 5432, $null, $null)
        $opened = $wait.AsyncWaitHandle.WaitOne(3000, $false) -and $client.Connected
    } finally {
        $client.Close()
    }
    if (-not $opened) {
        Fail-Cutover 'REFUSED: tailnet 5432 did not accept a desktop connect. StopOnly was not run.'
    }

    $preview = Join-Path $Repository 'scripts/recover-repository-children.ps1'
    & pwsh -NoProfile -File $preview -Repository $Repository | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Fail-Cutover 'REFUSED: repository preview is not clear. StopOnly was not run.'
    }
    $hold = Join-Path $Repository 'scripts/keep-six.ps1'
    & pwsh -NoProfile -File $hold -Hold -Agent $HoldAgent -Reason $HoldReason -Until $HoldUntil | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Fail-Cutover 'REFUSED: dispatch hold was not recorded. StopOnly was not run.'
    }
    $pendingUri = '/api/agent-tasks?projectId=' + [uri]::EscapeDataString($ProjectId) + '&landPending=true'
    $pending = Invoke-CutoverGet $pendingUri
    $pendingCount = 0
    if ($null -ne $pending.items) { $pendingCount = @($pending.items).Count }
    if ($pendingCount -ne 0) {
        Fail-Cutover 'REFUSED: landPending is not empty. StopOnly was not run.'
    }
    Write-Host 'PHASE 1 landPending is empty.'

    $stopCode = Invoke-CutoverRestart -StopOnly
    if ($stopCode -ne 0) {
        Fail-Cutover ('REFUSED: StopOnly exited ' + $stopCode + '. The old database stays in place.')
    }
    Write-Host 'PHASE 1 StopOnly exited 0. Relaunch was not requested by this script.'

    $sessions = Invoke-CutoverPsql -Target source -Sql "SELECT count(*) FROM pg_stat_activity WHERE datname = 'antiphon' AND pid <> pg_backend_pid()"
    if ($sessions -ne '0') {
        Write-Host 'STOP GATE 2: another session is connected. Start AppHost again on the old string. The secret was not changed.'
        $abandon = Invoke-CutoverRestart -ExpectedSha $RcTip
        Fail-Cutover ('REFUSED: dump refused because another session is connected. Restart exit ' + $abandon + '.')
    }
    $stamp = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $dump = Join-Path $DumpDirectory ('antiphon-cutover-' + $stamp + '.dump')
    $encrypted = $dump + '.age'
    & docker exec antiphon-postgres pg_dump -U antiphon -d antiphon -Fc --no-owner --no-acl -f /tmp/antiphon-cutover.dump
    if ($LASTEXITCODE -ne 0) { Fail-Cutover 'REFUSED: pg_dump -Fc failed. The secret was not changed.' }
    & docker cp antiphon-postgres:/tmp/antiphon-cutover.dump $dump
    if ($LASTEXITCODE -ne 0) { Fail-Cutover 'REFUSED: copy of the dump out of the container failed. The secret was not changed.' }
    & docker exec antiphon-postgres rm -f /tmp/antiphon-cutover.dump | Out-Null
    & age -r $AgeRecipientFile -o $encrypted $dump
    if ($LASTEXITCODE -ne 0) { Fail-Cutover 'REFUSED: age encrypt failed. The secret was not changed.' }
    $remoteName = Split-Path -Leaf $dump
    & scp $dump ($Server2SshHost + ':' + $Server2ComposeDir.TrimEnd('/') + '/' + $remoteName)
    if ($LASTEXITCODE -ne 0) { Fail-Cutover 'REFUSED: copy to server2 failed. The secret was not changed.' }
    $remoteDump = $Server2ComposeDir.TrimEnd('/') + '/' + $remoteName
    $restore = "cd '$Server2ComposeDir' && docker compose -p antiphon-server2-postgres cp '$remoteDump' postgres:/tmp/antiphon-cutover.dump && docker compose -p antiphon-server2-postgres exec -T postgres pg_restore -U antiphon -d antiphon --no-owner --no-acl /tmp/antiphon-cutover.dump && docker compose -p antiphon-server2-postgres exec -T postgres rm -f /tmp/antiphon-cutover.dump && rm -f '$remoteDump'"
    & ssh $Server2SshHost $restore
    if ($LASTEXITCODE -ne 0) {
        Fail-Cutover 'REFUSED: pg_restore exited non-zero. AppHost stays down. The secret was not changed. Start AppHost on the old string after confirming the secret file was not applied.'
    }
    Write-Host 'PHASE 2 pg_restore exited 0. Encrypted witness kept beside the dump.'

    Invoke-CutoverVerify
    Write-Host 'PHASE 3 exit check passed. Row text was not printed.'

    $compose = Join-Path $Repository 'docker-compose.dev.yml'
    $stopOut = & docker compose -f $compose stop postgres 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Fail-Cutover 'REFUSED: desktop postgres did not stop. The secret was not changed.' }
    if ($stopOut -match '(?i)antiphon_pgdata' -and $stopOut -match '(?i)remove') {
        Fail-Cutover 'REFUSED: compose output named the data directory with a removal. The secret was not changed.'
    }
    $names = @(& docker volume ls --format '{{.Name}}')
    if ($names -notcontains 'antiphon_pgdata') {
        Fail-Cutover 'REFUSED: antiphon_pgdata is not listed after the stop. The secret was not changed.'
    }
    $status = (& docker inspect -f '{{.State.Status}}' antiphon-postgres | Out-String).Trim()
    if ($status -ne 'exited') { Fail-Cutover 'REFUSED: desktop postgres is not exited. The secret was not changed.' }
    Set-CutoverConnectionSecret
    $up = Invoke-CutoverRestart -ExpectedSha $RcTip
    if ($up -ne 0) {
        Write-Host 'STOP GATE 4: restart did not exit 0 after the secret changed. Exit 5 keeps the restart lock. Operator runs phase 6.'
        Fail-Cutover ('REFUSED: restart exited ' + $up + '. Do not start a second copy.')
    }
    Write-Host 'PHASE 4 restart exited 0.'

    try {
        $health = Invoke-WebRequest -Uri ($ApiBase.TrimEnd('/') + '/health') -TimeoutSec 15 -UseBasicParsing
        $healthCode = [int]$health.StatusCode
    } catch {
        $healthCode = 0
    }
    if ($healthCode -ne 200) { Fail-Cutover 'PHASE 5 failed: health was not 200. Operator runs phase 6.' 2 }
    $after = Invoke-CutoverGet '/api/version'
    if ([string]$after.version -ne $sha -or @($after.capabilities) -notcontains 'land-v2') {
        Fail-Cutover 'PHASE 5 failed: version SHA or land-v2. Operator runs phase 6.' 2
    }
    $afterState = Invoke-CutoverGet '/api/diagnostics/session-state'
    $afterPool = $afterState.pool
    $afterReset = $afterPool.NoResetOnClose
    if ($null -eq $afterReset) { $afterReset = $afterPool.noResetOnClose }
    if ($afterReset -ne $true) { Fail-Cutover 'PHASE 5 failed: pool flag. Operator runs phase 6.' 2 }
    $activity = Invoke-CutoverPsql -Target target -Sql "SELECT count(*) FROM pg_stat_activity WHERE datname = 'antiphon' AND client_addr IS NOT NULL"
    if ($activity -eq '0' -or [string]::IsNullOrWhiteSpace($activity)) {
        Fail-Cutover 'PHASE 5 failed: server2 shows no desktop client. Operator runs phase 6.' 2
    }
    Write-Host 'PHASE 5 mechanical checks passed.'
    Write-Host 'OPERATOR: confirm one dispatch, one land, Hangfire, and the 30 minute monitor. This script does not create that task.'
    exit 0
}

if (-not $Execute) {
    Show-CutoverWhatIf
    exit 0
}
if ($WhatIf) {
    Fail-Cutover 'REFUSED: -Execute and -WhatIf together are ambiguous. Nothing was changed.' 2
}
if (-not $ConfirmWindow) {
    Fail-Cutover 'REFUSED: -Execute requires -ConfirmWindow. Nothing was changed.' 2
}
if ($Rollback) {
    Invoke-CutoverRollback
}
Invoke-CutoverWindow
