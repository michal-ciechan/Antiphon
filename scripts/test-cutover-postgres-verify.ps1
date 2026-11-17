#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1974 and CARD-1975 witnesses for cutover sequence and rollback checks.

    Throwaway postgres:16 containers only. No production container, no named
    volume, no cutover window. ASCII-only for Windows PowerShell 5.1.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Continue'
$script:passed = 0
$script:failed = 0
$script:containers = @()
$script:files = @()
$script:SequenceListSql = "SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'S' AND n.nspname <> 'pg_catalog' AND n.nspname <> 'information_schema' AND n.nspname <> 'pg_toast' ORDER BY 1"
$script:TableListSql = "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' ORDER BY 1"

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host ("PASS " + $Name)
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += ($Name + ' : ' + $Detail)
    Write-Host ("FAIL " + $Name + " - " + $Detail)
}

function Get-WitnessSha256 {
    param([string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))
    } finally {
        $sha.Dispose()
    }
    return ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
}

function Get-WitnessLines {
    param([string]$Text)
    $rows = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($Text)) {
        foreach ($line in ($Text -split "`n")) {
            $item = $line.Trim().Trim("`r")
            if (-not [string]::IsNullOrWhiteSpace($item)) { [void]$rows.Add($item) }
        }
    }
    return @($rows.ToArray())
}

function Limit-WitnessText {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $clean = ($Text -split "`n" | Where-Object { $_ -notmatch 'POSTGRES_PASSWORD' }) -join ' '
    if ($clean.Length -gt 500) { return $clean.Substring(0, 500) }
    return $clean
}

function New-WitnessName {
    param([string]$Prefix)
    if ($Prefix -ne 'c1974' -and $Prefix -ne 'c1975') { throw 'unexpected witness prefix' }
    return ($Prefix + '-' + [guid]::NewGuid().ToString('N').Substring(0, 16))
}

function Remove-WitnessContainer {
    param([string]$Name)
    if ($Name -notmatch '^c197[45]-[a-f0-9]{16}$') { throw 'refusing to remove an unexpected container name' }
    $mounts = ''
    $mounted = & docker inspect -f '{{range .Mounts}}{{.Type}}|{{.Name}}|{{.Destination}}{{println}}{{end}}' $Name 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) { $mounts = [string]$mounted }
    if ($mounts -match 'antiphon_pgdata' -or $mounts -match 'antiphon-server2') {
        throw ('refusing to touch a production volume mount on ' + $Name)
    }
    foreach ($line in @($mounts -split "`n")) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0) { continue }
        $parts = @($trim.Split('|'))
        if ($parts.Count -ge 1 -and $parts[0] -eq 'volume') {
            $vol = ''
            if ($parts.Count -ge 2) { $vol = $parts[1] }
            if ($vol -notmatch '^[0-9a-f]{64}$') { throw ('refusing to remove a named volume on ' + $Name) }
        }
    }
    if ($mounts -match 'volume\|') {
        & docker rm -f -v $Name | Out-Null
    } else {
        & docker rm -f $Name | Out-Null
    }
    & docker inspect $Name 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw ('container still exists ' + $Name) }
    Write-Host ("REMOVED " + $Name)
}

function Clear-WitnessResources {
    foreach ($name in @($script:containers)) {
        try { Remove-WitnessContainer $name } catch { Write-Host ("CLEANUP FAIL " + $name + " " + $_.Exception.Message) }
    }
    $script:containers = @()
    foreach ($path in @($script:files)) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
    $script:files = @()
}

function Start-WitnessPostgres {
    param([string]$Name, [string]$User, [string]$Database)
    if ($Name -notmatch '^c197[45]-[a-f0-9]{16}$') { throw 'unexpected container name' }
    $script:containers += $Name
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] 18
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    $env:POSTGRES_PASSWORD = ([Convert]::ToBase64String($bytes) -replace '[^a-zA-Z0-9]', 'x')
    try {
        $out = & docker run -d --name $Name --tmpfs /var/lib/postgresql/data --label antiphon.cutover-witness=card-1974 -e POSTGRES_PASSWORD -e POSTGRES_USER=$User -e POSTGRES_DB=$Database postgres:16 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally {
        Remove-Item Env:POSTGRES_PASSWORD -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) { throw ('container start failed exit=' + $code + ' ' + (Limit-WitnessText $out)) }
    $mounts = & docker inspect -f '{{range .Mounts}}{{.Type}}|{{.Name}}|{{.Destination}}{{println}}{{end}}' $Name 2>&1 | Out-String
    if ($mounts -match 'antiphon_pgdata' -or $mounts -match 'antiphon-server2') { throw 'production volume appeared on a witness container' }
    if ($mounts -match 'volume\|') { throw 'witness container created a docker volume' }
    for ($i = 0; $i -lt 90; $i++) {
        & docker exec $Name pg_isready -U $User -d $Database 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw ('postgres was not ready in ' + $Name)
}

function Invoke-WitnessSql {
    param([string]$Name, [string]$User, [string]$Database, [string]$Sql)
    $text = $Sql | & docker exec -i $Name psql -U $User -d $Database -At -v ON_ERROR_STOP=1 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($null -eq $code) { $code = 1 }
    return [pscustomobject]@{ ExitCode = [int]$code; Text = [string]$text }
}

function Invoke-WitnessSqlOk {
    param([string]$Name, [string]$User, [string]$Database, [string]$Sql)
    $result = Invoke-WitnessSql -Name $Name -User $User -Database $Database -Sql $Sql
    if ($result.ExitCode -ne 0) { throw ('sql failed on ' + $Name + ' ' + (Limit-WitnessText $result.Text)) }
    return $result.Text.Trim()
}

function Get-SequenceReadSql {
    param([string]$Qualified)
    if ($Qualified -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)$') { return $null }
    $schema = '"' + $Matches[1] + '"'
    $seq = '"' + $Matches[2] + '"'
    return 'SELECT COALESCE(last_value::text, '''') || '' '' || is_called::text FROM ' + $schema + '.' + $seq
}

function Get-HighWaterSql {
    param([string]$Ident, [switch]$Timestamp)
    if ($Ident -notmatch '^[A-Za-z0-9_."]+$') { return $null }
    if ($Timestamp) {
        return "SELECT a.attname FROM pg_attribute a WHERE a.attrelid = '$Ident'::regclass AND a.attnum > 0 AND NOT a.attisdropped AND a.attname IN ('UpdatedAt', 'CreatedAt', 'updated_at', 'created_at') ORDER BY CASE a.attname WHEN 'UpdatedAt' THEN 1 WHEN 'updated_at' THEN 2 WHEN 'CreatedAt' THEN 3 ELSE 4 END LIMIT 1"
    }
    return "SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey) WHERE i.indrelid = '$Ident'::regclass AND i.indisprimary ORDER BY array_position(i.indkey, a.attnum) LIMIT 1"
}

function Get-ContentFingerprintSql {
    param([string]$Ident, [string]$HighWater)
    if ($Ident -notmatch '^[A-Za-z0-9_."]+$') { return $null }
    $hw = "''"
    if (-not [string]::IsNullOrWhiteSpace($HighWater)) {
        if ($HighWater -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { return $null }
        $hw = 'COALESCE(max("' + $HighWater + '")::text, '''')'
    }
    return 'SELECT count(*)::text || '' '' || ' + $hw + ' || '' '' || COALESCE(md5(string_agg(md5(row_to_json(t)::text), '''' ORDER BY md5(row_to_json(t)::text))), md5('''')) FROM ' + $Ident + ' t'
}

function Get-WitnessSequenceCanonical {
    param([string]$Name)
    $listed = Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql $script:SequenceListSql
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($qualified in @(Get-WitnessLines $listed)) {
        $sql = Get-SequenceReadSql $qualified
        if ($null -eq $sql) { throw ('sequence name was not parseable ' + $qualified) }
        $state = Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql $sql
        if ($state -notmatch '^[0-9]+ (true|false)$') { throw ('sequence state was not parseable for ' + $qualified) }
        [void]$lines.Add($qualified + ' ' + $state)
    }
    return [string]::Join("`n", [string[]]$lines.ToArray())
}

function Get-WitnessContentCanonical {
    param([string]$Name)
    $listed = Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql $script:TableListSql
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($ident in @(Get-WitnessLines $listed)) {
        $keySql = Get-HighWaterSql $ident
        if ($null -eq $keySql) { throw 'table name was not a catalog identifier' }
        $key = (Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql $keySql)
        $key = (($key -split "`n")[0]).Trim()
        if ([string]::IsNullOrWhiteSpace($key)) {
            $timeSql = Get-HighWaterSql $ident -Timestamp
            $key = (Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql $timeSql)
            $key = (($key -split "`n")[0]).Trim()
        }
        $fpSql = Get-ContentFingerprintSql -Ident $ident -HighWater $key
        if ($null -eq $fpSql) { throw 'content fingerprint sql was refused' }
        $fp = Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql $fpSql
        [void]$lines.Add($ident + ' ' + $fp.Trim())
    }
    return [string]::Join("`n", [string[]]$lines.ToArray())
}

function Invoke-WitnessProbe {
    param([string]$Probe, [string]$Source, [string]$Target)
    $cutover = Join-Path $PSScriptRoot 'cutover-postgres-server2.ps1'
    $lines = @(& pwsh -NoProfile -File $cutover -VerificationProbe $Probe -ProbeSourceContainer $Source -ProbeTargetContainer $Target 2>&1 | ForEach-Object { $_.ToString() })
    $code = $LASTEXITCODE
    if ($null -eq $code) { $code = 1 }
    return [pscustomobject]@{ ExitCode = [int]$code; Text = ([string]::Join("`n", [string[]]$lines)) }
}

function Initialize-WitnessRole {
    param([string]$Name)
    $role = Invoke-WitnessSql -Name $Name -User postgres -Database postgres -Sql 'CREATE ROLE antiphon LOGIN NOSUPERUSER'
    if ($role.ExitCode -ne 0) { throw (Limit-WitnessText $role.Text) }
    $db = Invoke-WitnessSql -Name $Name -User postgres -Database postgres -Sql 'CREATE DATABASE antiphon OWNER antiphon'
    if ($db.ExitCode -ne 0) { throw (Limit-WitnessText $db.Text) }
}

function Test-F1Mismatch {
    $src = New-WitnessName 'c1974'
    $tgt = New-WitnessName 'c1974'
    Start-WitnessPostgres $src 'antiphon' 'antiphon'
    Start-WitnessPostgres $tgt 'antiphon' 'antiphon'
    Invoke-WitnessSqlOk -Name $src -User antiphon -Database antiphon -Sql 'CREATE TABLE public.items (id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, note text NOT NULL)' | Out-Null
    Invoke-WitnessSqlOk -Name $src -User antiphon -Database antiphon -Sql "INSERT INTO public.items (note) VALUES ('a'), ('b'), ('c')" | Out-Null
    & docker exec $src pg_dump -U antiphon -d antiphon -Fc --no-owner --no-acl -f /tmp/old.dump | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed' }
    $dump = Join-Path ([System.IO.Path]::GetTempPath()) ($src + '.dump')
    $script:files += $dump
    & docker cp ($src + ':/tmp/old.dump') $dump | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker cp out failed' }
    & docker cp $dump ($tgt + ':/tmp/old.dump') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker cp in failed' }
    & docker exec $tgt pg_restore -U antiphon -d antiphon --no-owner --no-acl /tmp/old.dump | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore failed' }
    Invoke-WitnessSqlOk -Name $src -User antiphon -Database antiphon -Sql "INSERT INTO public.items (note) SELECT 'n' || g::text FROM generate_series(1, 7) g" | Out-Null
    $srcState = Invoke-WitnessSqlOk -Name $src -User antiphon -Database antiphon -Sql 'SELECT COALESCE(last_value::text, '''') || '' '' || is_called::text FROM public.items_id_seq'
    $tgtState = Invoke-WitnessSqlOk -Name $tgt -User antiphon -Database antiphon -Sql 'SELECT COALESCE(last_value::text, '''') || '' '' || is_called::text FROM public.items_id_seq'
    $srcValue = [int]($srcState.Split(' ')[0])
    $tgtValue = [int]($tgtState.Split(' ')[0])
    if ($srcValue -le $tgtValue) {
        Write-Fail 'F1-mismatch' ('setup did not advance the source sequence ' + $srcState + ' vs ' + $tgtState)
        return
    }
    $probe = Invoke-WitnessProbe -Probe 'sequences' -Source $src -Target $tgt
    $needle = 'REFUSED: phase 3 mismatch for sequences'
    if ($probe.ExitCode -eq 0 -or $probe.Text.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F1-mismatch' ('advanced source versus older restore was accepted exit=' + $probe.ExitCode + ' ' + (Limit-WitnessText $probe.Text))
        return
    }
    Write-Pass 'F1-mismatch'
}

function Test-F1Unreadable {
    $src = New-WitnessName 'c1974'
    $tgt = New-WitnessName 'c1974'
    Start-WitnessPostgres $src 'postgres' 'postgres'
    Start-WitnessPostgres $tgt 'postgres' 'postgres'
    Initialize-WitnessRole $src
    Initialize-WitnessRole $tgt
    $sql = @(
        'CREATE SEQUENCE public.visible_seq',
        'ALTER SEQUENCE public.visible_seq OWNER TO antiphon',
        "SELECT setval('public.visible_seq', 4, true)",
        'CREATE SEQUENCE public.hidden_seq',
        'ALTER SEQUENCE public.hidden_seq OWNER TO postgres',
        'REVOKE ALL ON SEQUENCE public.hidden_seq FROM PUBLIC',
        'REVOKE ALL ON SEQUENCE public.hidden_seq FROM antiphon'
    )
    foreach ($statement in $sql) {
        Invoke-WitnessSqlOk -Name $src -User postgres -Database antiphon -Sql $statement | Out-Null
        Invoke-WitnessSqlOk -Name $tgt -User postgres -Database antiphon -Sql $statement | Out-Null
    }
    $hidden = Invoke-WitnessSql -Name $tgt -User antiphon -Database antiphon -Sql 'SELECT last_value, is_called FROM public.hidden_seq'
    if ($hidden.ExitCode -eq 0) {
        Write-Fail 'F1-unreadable' 'setup left hidden_seq readable'
        return
    }
    $probe = Invoke-WitnessProbe -Probe 'sequences' -Source $src -Target $tgt
    if ($probe.ExitCode -eq 0 -or $probe.Text.IndexOf('query failed for sequence', [System.StringComparison]::Ordinal) -lt 0 -or $probe.Text.IndexOf('hidden_seq', [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F1-unreadable' ('unreadable sequence was accepted exit=' + $probe.ExitCode + ' ' + (Limit-WitnessText $probe.Text))
        return
    }
    Write-Pass 'F1-unreadable'
}

function Test-F1Match {
    $src = New-WitnessName 'c1974'
    $tgt = New-WitnessName 'c1974'
    Start-WitnessPostgres $src 'antiphon' 'antiphon'
    Start-WitnessPostgres $tgt 'antiphon' 'antiphon'
    foreach ($name in @($src, $tgt)) {
        Invoke-WitnessSqlOk -Name $name -User antiphon -Database antiphon -Sql 'CREATE TABLE public.items (id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, note text NOT NULL)' | Out-Null
        Invoke-WitnessSqlOk -Name $name -User antiphon -Database antiphon -Sql "INSERT INTO public.items (note) VALUES ('a'), ('b'), ('c'), ('d'), ('e')" | Out-Null
    }
    $canonical = Get-WitnessSequenceCanonical $src
    $other = Get-WitnessSequenceCanonical $tgt
    if ($canonical -ne $other -or $canonical.IndexOf(' true', [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F1-match' 'setup sequence state did not match'
        return
    }
    $expected = Get-WitnessSha256 $canonical
    $probe = Invoke-WitnessProbe -Probe 'sequences' -Source $src -Target $tgt
    $line = 'PHASE 3 sequence fingerprint: ' + $expected
    $reads = 'PHASE 3 sequence reads: '
    if ($probe.ExitCode -ne 0 -or $probe.Text.IndexOf($line, [System.StringComparison]::Ordinal) -lt 0 -or $probe.Text.IndexOf('PHASE 3 match: sequences', [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F1-match' ('matching pair did not compare the real last_value and is_called exit=' + $probe.ExitCode + ' ' + (Limit-WitnessText $probe.Text))
        return
    }
    $readCount = 0
    foreach ($row in @($probe.Text -split "`n")) {
        if ($row.StartsWith($reads, [System.StringComparison]::Ordinal)) {
            $readCount = [int]$row.Substring($reads.Length).Trim()
        }
    }
    if ($readCount -lt 1) {
        Write-Fail 'F1-match' 'matching pair did not record a sequence read'
        return
    }
    Write-Pass 'F1-match'
}

function Initialize-Events {
    param([string]$Name)
    Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql 'CREATE TABLE public.events (id integer PRIMARY KEY, payload text NOT NULL)' | Out-Null
    Invoke-WitnessSqlOk -Name $Name -User antiphon -Database antiphon -Sql "INSERT INTO public.events (id, payload) VALUES (1, 'a'), (2, 'b')" | Out-Null
}

function Test-F2DeleteInsert {
    $src = New-WitnessName 'c1975'
    $tgt = New-WitnessName 'c1975'
    Start-WitnessPostgres $src 'antiphon' 'antiphon'
    Start-WitnessPostgres $tgt 'antiphon' 'antiphon'
    Initialize-Events $src
    Initialize-Events $tgt
    Invoke-WitnessSqlOk -Name $tgt -User antiphon -Database antiphon -Sql "DELETE FROM public.events WHERE id = 1" | Out-Null
    Invoke-WitnessSqlOk -Name $tgt -User antiphon -Database antiphon -Sql "INSERT INTO public.events (id, payload) VALUES (1, 'z')" | Out-Null
    $srcCount = Invoke-WitnessSqlOk -Name $src -User antiphon -Database antiphon -Sql 'SELECT count(*)::text FROM public.events'
    $tgtCount = Invoke-WitnessSqlOk -Name $tgt -User antiphon -Database antiphon -Sql 'SELECT count(*)::text FROM public.events'
    $srcMax = Invoke-WitnessSqlOk -Name $src -User antiphon -Database antiphon -Sql 'SELECT max(id)::text FROM public.events'
    $tgtMax = Invoke-WitnessSqlOk -Name $tgt -User antiphon -Database antiphon -Sql 'SELECT max(id)::text FROM public.events'
    $tgtPayload = Invoke-WitnessSqlOk -Name $tgt -User antiphon -Database antiphon -Sql 'SELECT payload FROM public.events WHERE id = 1'
    if ($srcCount -ne $tgtCount -or $srcMax -ne $tgtMax -or $tgtPayload -ne 'z') {
        Write-Fail 'F2-delete-insert' ('setup was not an equal-count content change ' + $srcCount + '/' + $tgtCount)
        return
    }
    $probe = Invoke-WitnessProbe -Probe 'rollback-rows' -Source $src -Target $tgt
    $needle = 'REFUSED: stop gate 6: the new database has rows the old desktop volume lacks.'
    if ($probe.ExitCode -eq 0 -or $probe.Text.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F2-delete-insert' ('delete plus insert kept the count equal and the check accepted the pair exit=' + $probe.ExitCode + ' ' + (Limit-WitnessText $probe.Text))
        return
    }
    Write-Pass 'F2-delete-insert'
}

function Test-F2Identical {
    $src = New-WitnessName 'c1975'
    $tgt = New-WitnessName 'c1975'
    Start-WitnessPostgres $src 'antiphon' 'antiphon'
    Start-WitnessPostgres $tgt 'antiphon' 'antiphon'
    Initialize-Events $src
    Initialize-Events $tgt
    $canonical = Get-WitnessContentCanonical $tgt
    $other = Get-WitnessContentCanonical $src
    if ([string]::IsNullOrWhiteSpace($canonical) -or $canonical -ne $other) {
        Write-Fail 'F2-identical' 'setup content did not match'
        return
    }
    $expected = Get-WitnessSha256 $canonical
    $probe = Invoke-WitnessProbe -Probe 'rollback-rows' -Source $src -Target $tgt
    $line = 'ROLLBACK CONTENT fingerprint: ' + $expected
    if ($probe.ExitCode -ne 0 -or $probe.Text.IndexOf('ROLLBACK ROWS clean', [System.StringComparison]::Ordinal) -lt 0 -or $probe.Text.IndexOf($line, [System.StringComparison]::Ordinal) -lt 0 -or $probe.Text.IndexOf('ROLLBACK CONTENT compared: 1', [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F2-identical' ('identical pair was accepted without the real content fingerprint exit=' + $probe.ExitCode + ' ' + (Limit-WitnessText $probe.Text))
        return
    }
    Write-Pass 'F2-identical'
}

function Test-F2Unreadable {
    $src = New-WitnessName 'c1975'
    $tgt = New-WitnessName 'c1975'
    Start-WitnessPostgres $src 'postgres' 'postgres'
    Start-WitnessPostgres $tgt 'postgres' 'postgres'
    Initialize-WitnessRole $src
    Initialize-WitnessRole $tgt
    $sql = @(
        'CREATE TABLE public.notes (id integer PRIMARY KEY, body text NOT NULL)',
        "INSERT INTO public.notes (id, body) VALUES (1, 'x')",
        'ALTER TABLE public.notes OWNER TO postgres',
        'REVOKE ALL ON TABLE public.notes FROM PUBLIC',
        'REVOKE ALL ON TABLE public.notes FROM antiphon'
    )
    foreach ($statement in $sql) {
        Invoke-WitnessSqlOk -Name $src -User postgres -Database antiphon -Sql $statement | Out-Null
        Invoke-WitnessSqlOk -Name $tgt -User postgres -Database antiphon -Sql $statement | Out-Null
    }
    $read = Invoke-WitnessSql -Name $tgt -User antiphon -Database antiphon -Sql 'SELECT count(*) FROM public.notes'
    if ($read.ExitCode -eq 0) {
        Write-Fail 'F2-unreadable' 'setup left notes readable'
        return
    }
    $probe = Invoke-WitnessProbe -Probe 'rollback-rows' -Source $src -Target $tgt
    $needle = 'REFUSED: stop gate 6: row comparison failed. Neither data directory is removed.'
    if ($probe.ExitCode -eq 0 -or $probe.Text.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) {
        Write-Fail 'F2-unreadable' ('unreadable table was accepted exit=' + $probe.ExitCode + ' ' + (Limit-WitnessText $probe.Text))
        return
    }
    Write-Pass 'F2-unreadable'
}

function Invoke-WitnessCase {
    param([string]$Name, [scriptblock]$Body)
    $script:containers = @()
    $script:files = @()
    try {
        & $Body
    } catch {
        Write-Fail $Name $_.Exception.Message
    } finally {
        Clear-WitnessResources
    }
}

& docker image inspect postgres:16 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    & docker pull postgres:16 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'image' 'postgres:16 is not available'
        Write-Host ("WITNESS passed=$script:passed failed=$script:failed")
        exit 1
    }
}

$all = @('F1-mismatch', 'F1-unreadable', 'F1-match', 'F2-delete-insert', 'F2-identical', 'F2-unreadable')
$selected = @($all)
if (-not [string]::IsNullOrWhiteSpace($Case)) { $selected = @($Case) }
foreach ($name in $selected) {
    if (@($all) -notcontains $name) {
        Write-Fail $name 'unknown case'
        continue
    }
    if ($name -eq 'F1-mismatch') { Invoke-WitnessCase $name { Test-F1Mismatch } }
    elseif ($name -eq 'F1-unreadable') { Invoke-WitnessCase $name { Test-F1Unreadable } }
    elseif ($name -eq 'F1-match') { Invoke-WitnessCase $name { Test-F1Match } }
    elseif ($name -eq 'F2-delete-insert') { Invoke-WitnessCase $name { Test-F2DeleteInsert } }
    elseif ($name -eq 'F2-identical') { Invoke-WitnessCase $name { Test-F2Identical } }
    elseif ($name -eq 'F2-unreadable') { Invoke-WitnessCase $name { Test-F2Unreadable } }
}

Write-Host ("WITNESS passed=$script:passed failed=$script:failed")
if ($script:failed -gt 0) { exit 1 }
exit 0
