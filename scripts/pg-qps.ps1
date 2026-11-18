#requires -Version 5.1
<#
.SYNOPSIS
  Read-only pg_stat_statements rate over a window. CARD-1987.
  The connection string, when -ConnectionEnv is used, is copied onto the psql
  child process only and is never printed.
#>
param(
    [int]$Seconds = 60,
    [string]$Container = "",
    [string]$ConnectionEnv = "",
    [string]$Database = "antiphon",
    [string]$User = "antiphon",
    [int]$Top = 20,
    [switch]$Json,
    [string]$SignalFile = ""
)

$ErrorActionPreference = "Stop"
$Tag = "-- rt.diag.pgss"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($ScriptDir) -and $PSCommandPath) { $ScriptDir = Split-Path -Parent $PSCommandPath }

function Write-Fixed([string]$text) {
    [Console]::Out.WriteLine($text)
}

function Stop-Bad {
    Write-Fixed "bad arguments"
    exit 2
}

function Stop-Unreachable {
    Write-Fixed "database unreachable or extension not loaded"
    exit 3
}

if ($Seconds -lt 10 -or $Seconds -gt 3600) { Stop-Bad }
if ($Top -lt 1 -or $Top -gt 1000) { Stop-Bad }
if ($Database -notmatch '^[A-Za-z0-9_]+$' -or $User -notmatch '^[A-Za-z0-9_]+$') { Stop-Bad }

$useEnv = $ConnectionEnv.Length -gt 0
if ($useEnv -and $Container.Length -gt 0) { Stop-Bad }
if (-not $useEnv -and $Container.Length -eq 0) { $Container = "antiphon-postgres" }
if (-not $useEnv -and $Container -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*$') { Stop-Bad }
if ($useEnv -and $ConnectionEnv -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Stop-Bad }

function Get-SqlParts {
    $root = Split-Path -Parent $ScriptDir
    $path = Join-Path $root "server"
    $path = Join-Path $path "Infrastructure"
    $path = Join-Path $path "Data"
    $path = Join-Path $path "PgStatStatementsSnapshot.sql"
    if (-not (Test-Path -LiteralPath $path)) { Stop-Unreachable }
    $raw = [System.IO.File]::ReadAllText($path)
    $marker = "-- @@split@@"
    $at = $raw.IndexOf($marker)
    if ($at -lt 0) { Stop-Unreachable }
    $counters = $Tag + "`n" + $raw.Substring(0, $at).Trim()
    $text = $Tag + "`n" + $raw.Substring($at + $marker.Length).Trim()
    return @{ Counters = $counters; Text = $text }
}

function Convert-ConnMap([string]$raw) {
    $map = @{}
    if ($raw.IndexOf(";") -ge 0) {
        foreach ($part in $raw.Split(";")) {
            $eq = $part.IndexOf("=")
            if ($eq -lt 1) { continue }
            $k = $part.Substring(0, $eq).Trim().ToLowerInvariant()
            $v = $part.Substring($eq + 1).Trim()
            if ($v.Length -ge 2 -and $v.StartsWith("'") -and $v.EndsWith("'")) { $v = $v.Substring(1, $v.Length - 2) }
            $map[$k] = $v
        }
    }
    else {
        $token = ""
        $inQuote = $false
        $chars = $raw.ToCharArray()
        for ($i = 0; $i -le $chars.Length; $i++) {
            $done = $i -eq $chars.Length
            $ch = $null
            if (-not $done) { $ch = $chars[$i] }
            if ($done -or ((-not $inQuote) -and [char]::IsWhiteSpace($ch))) {
                if ($token.Length -gt 0) {
                    $eq = $token.IndexOf("=")
                    if ($eq -gt 0) {
                        $k = $token.Substring(0, $eq).ToLowerInvariant()
                        $v = $token.Substring($eq + 1)
                        if ($v.Length -ge 2 -and $v.StartsWith("'") -and $v.EndsWith("'")) { $v = $v.Substring(1, $v.Length - 2) }
                        $map[$k] = $v
                    }
                    $token = ""
                }
            }
            else {
                if ($ch -eq "'") { $inQuote = -not $inQuote }
                $token = $token + $ch
            }
        }
    }
    return $map
}

function Invoke-Query([string]$sql) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    if ($useEnv) {
        $raw = [Environment]::GetEnvironmentVariable($ConnectionEnv)
        if ([string]::IsNullOrEmpty($raw)) { Stop-Unreachable }
        $map = Convert-ConnMap $raw
        $hostName = ""
        if ($map.ContainsKey("host")) { $hostName = $map["host"] }
        elseif ($map.ContainsKey("server")) { $hostName = $map["server"] }
        $port = "5432"
        if ($map.ContainsKey("port")) { $port = $map["port"] }
        $userName = $User
        if ($map.ContainsKey("username")) { $userName = $map["username"] }
        elseif ($map.ContainsKey("user")) { $userName = $map["user"] }
        $dbName = $Database
        if ($map.ContainsKey("database")) { $dbName = $map["database"] }
        elseif ($map.ContainsKey("dbname")) { $dbName = $map["dbname"] }
        $password = ""
        if ($map.ContainsKey("password")) { $password = $map["password"] }
        if ($hostName.Length -eq 0) { Stop-Unreachable }
        $psi.FileName = "psql"
        $psi.Arguments = "-X -A -t -v ON_ERROR_STOP=1"
        $psi.EnvironmentVariables["PGHOST"] = $hostName
        $psi.EnvironmentVariables["PGPORT"] = $port
        $psi.EnvironmentVariables["PGUSER"] = $userName
        $psi.EnvironmentVariables["PGDATABASE"] = $dbName
        $psi.EnvironmentVariables["PGPASSWORD"] = $password
    }
    else {
        $psi.FileName = "docker"
        $psi.Arguments = "exec -i $Container psql -X -A -t -U $User -d $Database -v ON_ERROR_STOP=1"
    }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    if (-not $proc.Start()) { Stop-Unreachable }
    $proc.StandardInput.Write($sql)
    $proc.StandardInput.Close()
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    $out = $outTask.Result
    $null = $errTask.Result
    if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($out)) { Stop-Unreachable }
    return $out
}

function Convert-Rows([string]$text) {
    $parsed = New-Object System.Collections.Generic.List[object]
    foreach ($line in ($text -split "`n")) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0) { continue }
        $f = $trim.Split("|")
        if ($f.Length -lt 8) { continue }
        $calls = 0L
        $ms = 0.0
        $rows = 0L
        $dealloc = 0L
        [void][long]::TryParse($f[2], [ref]$dealloc)
        [void][long]::TryParse($f[5], [ref]$calls)
        [void][double]::TryParse($f[6], [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$ms)
        [void][long]::TryParse($f[7], [ref]$rows)
        $parsed.Add(@{
            VersionNum = $f[0]
            Version = $f[1]
            Dealloc = $dealloc
            StatsReset = $f[3]
            QueryId = $f[4]
            Calls = $calls
            Ms = $ms
            Rows = $rows
        })
    }
    return $parsed
}

function Get-Label([string]$query) {
    if ([string]::IsNullOrEmpty($query)) { return "untagged" }
    if ($query -match '\b(password|alter\s+(role|user)|create\s+(role|user)|connection|secret|token)\b') { return "redacted-utility" }
    if ($query -cmatch '^-- ([a-z0-9.\-]{1,64})(\r?\n|$)') { return $Matches[1] }
    $flat = [regex]::Replace($query, '\s+', ' ').Trim()
    if ($flat -match '^(begin|commit|rollback|release|savepoint)\b') { return "tx." + $Matches[1].ToLowerInvariant() }
    if ($flat.Length -gt 120) { return $flat.Substring(0, 120) }
    if ($flat.Length -eq 0) { return "untagged" }
    return $flat
}

function Get-Operation([string]$label) {
    if ($label -eq "redacted-utility") { return $label }
    if ($label -cmatch '^[a-z0-9.\-]{1,64}$') { return $label }
    return "untagged"
}

function Json-Escape([string]$s) {
    if ($null -eq $s) { return "" }
    $s = $s.Replace("\", "\\").Replace('"', '\"').Replace("`r", "\r").Replace("`n", "\n").Replace("`t", "\t")
    return $s
}

function Json-Num([double]$n) {
    return $n.ToString("0.########", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-Texts($parts, $ids) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($id in $ids) {
        if ($id -notmatch '^-?\d+$') { continue }
        $list.Add($id)
    }
    if ($list.Count -eq 0) { return @{} }
    $joined = [string]::Join(",", $list.ToArray())
    $sql = $parts.Text.Replace("/*IDS*/", $joined)
    $raw = Invoke-Query $sql
    $map = @{}
    foreach ($line in ($raw -split "`n")) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0) { continue }
        $f = $trim.Split("|")
        if ($f.Length -lt 2 -or $f[0].Length -eq 0) { continue }
        try {
            $bytes = [Convert]::FromBase64String($f[1])
            $map[$f[0]] = [System.Text.Encoding]::UTF8.GetString($bytes)
        }
        catch { }
    }
    return $map
}

$parts = Get-SqlParts
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$firstRaw = Invoke-Query $parts.Counters
$first = Convert-Rows $firstRaw
if ($first.Count -eq 0) { Stop-Unreachable }
if ($SignalFile.Length -gt 0) {
    [System.IO.File]::WriteAllText($SignalFile, "ready")
}
Start-Sleep -Seconds $Seconds
$snapClock = [System.Diagnostics.Stopwatch]::StartNew()
$secondRaw = Invoke-Query $parts.Counters
$snapClock.Stop()
$clock.Stop()
$second = Convert-Rows $secondRaw
if ($second.Count -eq 0) { Stop-Unreachable }

$meta = $second[$second.Count - 1]
$beforeMeta = $first[0]
$partial = $false
$reason = ""
if ($meta.Dealloc -gt $beforeMeta.Dealloc) { $partial = $true; $reason = "dealloc" }
elseif ($meta.StatsReset -ne $beforeMeta.StatsReset) { $partial = $true; $reason = "stats-reset" }

$beforeMap = @{}
foreach ($row in $first) {
    if ($row.QueryId.Length -eq 0) { continue }
    $beforeMap[$row.QueryId] = $row
}
$afterMap = @{}
foreach ($row in $second) {
    if ($row.QueryId.Length -eq 0) { continue }
    $afterMap[$row.QueryId] = $row
}
if (-not $partial) {
    foreach ($id in $beforeMap.Keys) {
        if (-not $afterMap.ContainsKey($id) -or $afterMap[$id].Calls -lt $beforeMap[$id].Calls) {
            $partial = $true
            $reason = "stats-reset"
            break
        }
    }
}

$elapsed = $clock.Elapsed.TotalSeconds
if ($elapsed -le 0) { $elapsed = [double]$Seconds }
$versionNum = 0
[void][int]::TryParse($meta.VersionNum, [ref]$versionNum)

$ops = @{}
$stmts = New-Object System.Collections.Generic.List[object]
$totalCalls = 0L
$totalMs = 0.0
$totalRows = 0L
if (-not $partial) {
    $need = New-Object System.Collections.Generic.List[string]
    foreach ($id in $afterMap.Keys) {
        $deltaCalls = $afterMap[$id].Calls
        if ($beforeMap.ContainsKey($id)) { $deltaCalls = $afterMap[$id].Calls - $beforeMap[$id].Calls }
        if ($deltaCalls -gt 0) { $need.Add($id) }
    }
    $texts = Get-Texts $parts $need
    foreach ($id in $need) {
        $beforeCalls = 0L
        $beforeMs = 0.0
        $beforeRows = 0L
        if ($beforeMap.ContainsKey($id)) {
            $beforeCalls = $beforeMap[$id].Calls
            $beforeMs = $beforeMap[$id].Ms
            $beforeRows = $beforeMap[$id].Rows
        }
        $calls = $afterMap[$id].Calls - $beforeCalls
        $ms = $afterMap[$id].Ms - $beforeMs
        $rows = $afterMap[$id].Rows - $beforeRows
        if ($calls -lt 0) { $calls = 0 }
        if ($ms -lt 0) { $ms = 0 }
        if ($rows -lt 0) { $rows = 0 }
        $query = ""
        if ($texts.ContainsKey($id)) { $query = $texts[$id] }
        $label = Get-Label $query
        $op = Get-Operation $label
        if (-not $ops.ContainsKey($op)) {
            $ops[$op] = @{ Calls = 0L; Ms = 0.0; Rows = 0L }
        }
        $ops[$op].Calls = $ops[$op].Calls + $calls
        $ops[$op].Ms = $ops[$op].Ms + $ms
        $ops[$op].Rows = $ops[$op].Rows + $rows
        $totalCalls = $totalCalls + $calls
        $totalMs = $totalMs + $ms
        $totalRows = $totalRows + $rows
        $mean = 0.0
        if ($calls -gt 0) { $mean = $ms / $calls }
        $stmts.Add(@{
            Label = $label; Calls = $calls; Ms = $ms; Rows = $rows; Mean = $mean
            Qps = $calls / $elapsed; MsPer = $ms / $elapsed; RowsPer = $rows / $elapsed
        })
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("{")
[void]$sb.Append('"windowSeconds":' + (Json-Num $elapsed) + ",")
if ($partial) { [void]$sb.Append('"partial":true,') } else { [void]$sb.Append('"partial":false,') }
[void]$sb.Append('"reason":"' + (Json-Escape $reason) + '",')
$totalMean = 0.0
if ($totalCalls -gt 0) { $totalMean = $totalMs / $totalCalls }
[void]$sb.Append('"total":{"qps":' + (Json-Num ($totalCalls / $elapsed)) + ',"dbMsPerSecond":' + (Json-Num ($totalMs / $elapsed)) + ',"rowsPerSecond":' + (Json-Num ($totalRows / $elapsed)) + ',"calls":' + $totalCalls + ',"meanMs":' + (Json-Num $totalMean) + '},')
[void]$sb.Append('"operations":[')
$firstOp = $true
foreach ($name in ($ops.Keys | Sort-Object)) {
    if (-not $firstOp) { [void]$sb.Append(",") }
    $firstOp = $false
    $item = $ops[$name]
    $mean = 0.0
    if ($item.Calls -gt 0) { $mean = $item.Ms / $item.Calls }
    [void]$sb.Append('{"operation":"' + (Json-Escape $name) + '","calls":' + $item.Calls + ',"qps":' + (Json-Num ($item.Calls / $elapsed)) + ',"totalMs":' + (Json-Num $item.Ms) + ',"msPerSecond":' + (Json-Num ($item.Ms / $elapsed)) + ',"rows":' + $item.Rows + ',"rowsPerSecond":' + (Json-Num ($item.Rows / $elapsed)) + ',"meanMs":' + (Json-Num $mean) + '}')
}
[void]$sb.Append('],"topByCalls":[')
$byCalls = @($stmts | Sort-Object -Property @{ Expression = { $_.Calls }; Descending = $true }, @{ Expression = { $_.Label } } | Select-Object -First $Top)
$byTime = @($stmts | Sort-Object -Property @{ Expression = { $_.Ms }; Descending = $true }, @{ Expression = { $_.Label } } | Select-Object -First $Top)
function Append-Stmts($target, $items) {
    $comma = $false
    foreach ($item in $items) {
        if ($comma) { [void]$target.Append(",") }
        $comma = $true
        [void]$target.Append('{"label":"' + (Json-Escape $item.Label) + '","calls":' + $item.Calls + ',"qps":' + (Json-Num $item.Qps) + ',"totalMs":' + (Json-Num $item.Ms) + ',"msPerSecond":' + (Json-Num $item.MsPer) + ',"rows":' + $item.Rows + ',"rowsPerSecond":' + (Json-Num $item.RowsPer) + ',"meanMs":' + (Json-Num $item.Mean) + '}')
    }
}
Append-Stmts $sb $byCalls
[void]$sb.Append('],"topByTime":[')
Append-Stmts $sb $byTime
[void]$sb.Append('],"snapshotMs":' + [int]$snapClock.ElapsedMilliseconds + ',"serverVersionNum":' + $versionNum + ',"serverVersion":"' + (Json-Escape $meta.Version) + '"}')

if ($Json) {
    Write-Fixed $sb.ToString()
}
else {
    Write-Fixed ("windowSeconds=" + (Json-Num $elapsed) + " qps=" + (Json-Num ($totalCalls / $elapsed)) + " dbMsPerSecond=" + (Json-Num ($totalMs / $elapsed)) + " rowsPerSecond=" + (Json-Num ($totalRows / $elapsed)) + " partial=" + $partial + " reason=" + $reason)
    foreach ($name in ($ops.Keys | Sort-Object)) {
        $item = $ops[$name]
        Write-Fixed ("operation=" + $name + " calls=" + $item.Calls + " qps=" + (Json-Num ($item.Calls / $elapsed)))
    }
    foreach ($item in $byCalls) {
        Write-Fixed ("topCalls label=" + $item.Label + " calls=" + $item.Calls)
    }
}
if ($partial) { exit 4 }
exit 0
