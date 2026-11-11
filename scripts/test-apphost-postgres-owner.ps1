#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1844 V-1 / R-1 source witness for the AppHost Postgres owner contract.

.DESCRIPTION
    Reads Antiphon.AppHost/Program.cs and the two tracked appsettings files. It never boots
    AppHost, never calls docker and never opens a socket.

    V-1: Program.cs states "AppHost does not start the server2 Postgres container.", logs the
         template "Database endpoint {Host}:{Port}" from a logger call, contains no AddPostgres
         and no "docker context", and no log or console call passes the connection string,
         its configuration key, the connection-string resource, a username or a password.
    R-1: Antiphon.AppHost/appsettings.json and server/appsettings.json keep
         ConnectionStrings:DefaultConnection on Host=localhost;Port=17280 with
         No Reset On Close=true, and neither file gains a 100.x host.

    Output names checks only; it never prints a connection string or a password.

.PARAMETER SourceRoot
    Tree to read. Defaults to the repository that holds this script. A scratch copy lets
    Mutation run a mutant without editing the worktree.
#>
param([string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Split-Path -Parent $PSScriptRoot }
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path

$script:passed = 0
$script:failed = 0
function Assert-Owner([bool]$Condition, [string]$Name) {
    if ($Condition) { $script:passed++; Write-Host "PASS $Name" }
    else { $script:failed++; Write-Host "FAIL $Name" }
}

function Read-Source([string]$Relative) {
    $path = Join-Path $SourceRoot $Relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "missing $Relative" }
    return [IO.File]::ReadAllText($path)
}

# Removes // line comments and /* */ block comments; keeps string literals intact.
function Remove-CSharpComments([string]$Text) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $Text.Length) {
        $c = $Text[$i]
        $next = if ($i + 1 -lt $Text.Length) { $Text[$i + 1] } else { [char]0 }
        if ($c -eq '"') {
            $verbatim = ($i -gt 0 -and $Text[$i - 1] -eq '@')
            [void]$sb.Append($c); $i++
            while ($i -lt $Text.Length) {
                $d = $Text[$i]
                if (-not $verbatim -and $d -eq '\' -and $i + 1 -lt $Text.Length) {
                    [void]$sb.Append($d).Append($Text[$i + 1]); $i += 2; continue
                }
                if ($verbatim -and $d -eq '"' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '"') {
                    [void]$sb.Append('""'); $i += 2; continue
                }
                [void]$sb.Append($d); $i++
                if ($d -eq '"') { break }
            }
            continue
        }
        if ($c -eq '/' -and $next -eq '/') {
            while ($i -lt $Text.Length -and $Text[$i] -ne "`n") { $i++ }
            continue
        }
        if ($c -eq '/' -and $next -eq '*') {
            $end = $Text.IndexOf('*/', $i + 2)
            $i = if ($end -lt 0) { $Text.Length } else { $end + 2 }
            continue
        }
        [void]$sb.Append($c); $i++
    }
    return $sb.ToString()
}

# Returns the argument text of every call matching $Pattern (balanced parentheses, string-aware).
function Get-CallArguments([string]$Code, [string]$Pattern) {
    $calls = @()
    foreach ($m in [regex]::Matches($Code, $Pattern)) {
        $start = $m.Index + $m.Length
        $depth = 1
        $i = $start
        $inString = $false
        while ($i -lt $Code.Length -and $depth -gt 0) {
            $c = $Code[$i]
            if ($inString) {
                if ($c -eq '\') { $i += 2; continue }
                if ($c -eq '"') { $inString = $false }
            } elseif ($c -eq '"') { $inString = $true }
            elseif ($c -eq '(') { $depth++ }
            elseif ($c -eq ')') { $depth-- }
            $i++
        }
        $calls += , $Code.Substring($start, [Math]::Max(0, $i - $start - 1))
    }
    return , $calls
}

function Get-ConnectionValue([string]$ConnectionString, [string[]]$Keys) {
    foreach ($part in $ConnectionString.Split(';')) {
        $eq = $part.IndexOf('=')
        if ($eq -lt 0) { continue }
        $key = $part.Substring(0, $eq).Trim()
        foreach ($k in $Keys) {
            if ($key -ieq $k) { return $part.Substring($eq + 1).Trim() }
        }
    }
    return $null
}

$programRelative = 'Antiphon.AppHost/Program.cs'
$program = Read-Source $programRelative
$code = Remove-CSharpComments $program

# ---- V-1 owner sentence and endpoint log ----------------------------------------------------
Assert-Owner ($program.Contains('AppHost does not start the server2 Postgres container.')) 'V-1 Program.cs states AppHost does not start the server2 Postgres container'

$logPattern = '\b(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)+Log(?:Trace|Debug|Information|Warning|Error|Critical)?\s*\('
$consolePattern = '\bConsole\s*\.\s*(?:Out\s*\.\s*|Error\s*\.\s*)?Write(?:Line)?\s*\('
$logCalls = Get-CallArguments $code $logPattern
$consoleCalls = Get-CallArguments $code $consolePattern
$endpointCalls = @($logCalls | Where-Object { $_.Contains('"Database endpoint {Host}:{Port}') })
Assert-Owner ($endpointCalls.Count -ge 1) 'V-1 a logger call carries the template Database endpoint {Host}:{Port}'

Assert-Owner (-not [regex]::IsMatch($program, 'AddPostgres')) 'V-1 Program.cs has no AddPostgres'
Assert-Owner (-not [regex]::IsMatch($program, '(?i)docker\s+context')) 'V-1 Program.cs has no docker context'

# Names that hold the connection string or the connection-string resource.
$tainted = New-Object System.Collections.Generic.List[string]
$assign = '\b(?:var|string\??)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<rhs>[^;]*);'
foreach ($m in [regex]::Matches($code, $assign)) {
    $rhs = $m.Groups['rhs'].Value
    if ($rhs -match 'GetConnectionString\s*\(|ConnectionStrings\s*:|AddConnectionString\s*\(|\.ConnectionString\b') {
        $tainted.Add($m.Groups['name'].Value)
    }
}
Assert-Owner ($tainted.Count -ge 1) 'V-1 the connection-string resource is still named in Program.cs'

$leaks = @()
foreach ($call in @($logCalls) + @($consoleCalls)) {
    if ($call -match 'GetConnectionString|ConnectionStrings|\.ConnectionString\b|(?i:password|username|user\s*id)') { $leaks += , $call; continue }
    $bare = [regex]::Replace($call, '"(?:[^"\\]|\\.)*"', '""')
    foreach ($name in $tainted) {
        if ([regex]::IsMatch($bare, "(?<![A-Za-z0-9_.])$([regex]::Escape($name))(?![A-Za-z0-9_])")) { $leaks += , $call; break }
    }
}
Assert-Owner ($leaks.Count -eq 0) 'V-1 no log or console call passes the connection string, resource, username or password'

# ---- R-1 tracked connection strings ---------------------------------------------------------
foreach ($relative in @('Antiphon.AppHost/appsettings.json', 'server/appsettings.json')) {
    $text = Read-Source $relative
    $json = $text | ConvertFrom-Json
    $value = $null
    if ($json.PSObject.Properties['ConnectionStrings'] -and $json.ConnectionStrings.PSObject.Properties['DefaultConnection']) {
        $value = [string]$json.ConnectionStrings.DefaultConnection
    }
    Assert-Owner ($null -ne $value) "R-1 $relative has ConnectionStrings:DefaultConnection"
    if ($null -eq $value) { continue }
    Assert-Owner ($value.Contains('Host=localhost;Port=17280')) "R-1 $relative stays Host=localhost;Port=17280"
    Assert-Owner ($value.Contains('No Reset On Close=true')) "R-1 $relative keeps No Reset On Close=true"
    $hostValue = Get-ConnectionValue $value @('Host', 'Server', 'Data Source')
    Assert-Owner ($null -ne $hostValue -and -not $hostValue.StartsWith('100.')) "R-1 $relative DefaultConnection host is not a 100.x address"
    Assert-Owner (-not [regex]::IsMatch($text, '(?i)\b(?:host|server|data source)\s*=\s*100\.')) "R-1 $relative names no 100.x host anywhere"

    # The tracked dev password must never be copied into Program.cs.
    $password = Get-ConnectionValue $value @('Password', 'Pwd')
    if (-not [string]::IsNullOrEmpty($password)) {
        Assert-Owner (-not $program.Contains($password)) "R-1 Program.cs does not contain the $relative password text"
    }
}

Write-Host "apphost-postgres-owner passed=$($script:passed) failed=$($script:failed)"
if ($script:failed -gt 0) { exit 1 }
exit 0
