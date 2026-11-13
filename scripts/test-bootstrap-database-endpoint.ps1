#requires -Version 7.0
<#
.SYNOPSIS
    CARD-1844 V-2 / V-3 / R-2 witness for the bootstrap-check database endpoint probe.

.DESCRIPTION
    Runs scripts/bootstrap-check.ps1 -DatabaseProbeOnly in child pwsh processes with fixture
    connection strings, reads scripts/lib/DatabaseEndpoint.ps1 directly, and parses
    bootstrap-check.ps1 to pin where the local container checks live. It never runs the full
    bootstrap check, never reads the real AppHost user-secrets file (APPDATA points at an empty
    scratch directory), never calls docker and never opens a socket.

    V-2: a 100.64.0.2:5432 string prints exactly
         "database-endpoint host=100.64.0.2 port=5432 mode=remote"; the fixture password never
         appears on stdout or stderr; the probe runs no docker (a PATH shim records any call).
    V-3: localhost:17280 prints mode=local; with no -ConnectionString and no user-secrets file the
         tracked Antiphon.AppHost/appsettings.json resolves to that same local line; a
         user-secrets fixture (flat and nested key forms) outranks appsettings.
    R-2: in bootstrap-check.ps1 the probe-only exit precedes every docker, socket and check call,
         every antiphon-postgres / antiphon_pgdata / 17280 / pg_isready / docker-compose.dev.yml
         literal outside function definitions sits in the "local" branch of a
         $databaseEndpoint.Mode -eq 'local' test, and the remote branch TCP-checks the resolved
         host and port.

    Output names checks only; it never prints a fixture password.

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
function Assert-Endpoint([bool]$Condition, [string]$Name) {
    if ($Condition) { $script:passed++; Write-Host "PASS $Name" }
    else { $script:failed++; Write-Host "FAIL $Name" }
}

$secret = 'secret-value'
$bootstrap = Join-Path $SourceRoot 'scripts/bootstrap-check.ps1'
$library = Join-Path $SourceRoot 'scripts/lib/DatabaseEndpoint.ps1'
$pwsh = (Get-Process -Id $PID).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1844-endpoint-' + [guid]::NewGuid().ToString('N'))
$appData = Join-Path $scratch 'appdata'
$shimDir = Join-Path $scratch 'shim'
$dockerMarker = Join-Path $scratch 'docker-called.txt'
[void](New-Item -ItemType Directory -Path $appData -Force)
[void](New-Item -ItemType Directory -Path $shimDir -Force)

# A docker shim first on PATH records any call the probe makes.
if ($IsWindows) {
    Set-Content -LiteralPath (Join-Path $shimDir 'docker.cmd') -Value "@echo called>>`"$dockerMarker`"`r`n@exit /b 0" -Encoding ascii
} else {
    $shim = Join-Path $shimDir 'docker'
    Set-Content -LiteralPath $shim -Value "#!/bin/sh`necho called >> '$dockerMarker'`nexit 0" -Encoding ascii
    & chmod 755 $shim
}
$shimPath = $shimDir + [IO.Path]::PathSeparator + $env:PATH

function Invoke-Child([string[]]$Arguments, [string]$AppDataRoot = $appData, [hashtable]$Overrides = @{}) {
    $start = New-Object System.Diagnostics.ProcessStartInfo $pwsh
    foreach ($a in $Arguments) { $start.ArgumentList.Add($a) }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $scratch
    $start.Environment['APPDATA'] = $AppDataRoot
    $start.Environment['PATH'] = $shimPath
    # Never inherit a developer's effective database configuration into a fixture.
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -match '(?i)^(ConnectionStrings[:_]|CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_|DOTNET_|ASPNETCORE_)') {
            [void]$start.Environment.Remove($key)
        }
    }
    $start.Environment['DOTNET_ENVIRONMENT'] = 'Development'
    foreach ($key in $Overrides.Keys) {
        if ($null -eq $Overrides[$key]) {
            [void]$start.Environment.Remove($key)
            if ($start.Environment.ContainsKey($key)) { throw 'fixture environment removal failed' }
        }
        else { $start.Environment[$key] = $Overrides[$key] }
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) {
        $process.Kill($true)
        throw 'child pwsh did not exit within 60 s'
    }
    $process.WaitForExit()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout.Result; Stderr = $stderr.Result }
}

function Invoke-Probe([string]$Name, [string]$ConnectionString, [string]$Expected, [string]$AppDataRoot = $appData) {
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $bootstrap, '-DatabaseProbeOnly')
    if (-not [string]::IsNullOrEmpty($ConnectionString)) { $arguments += @('-ConnectionString', $ConnectionString) }
    $result = Invoke-Child $arguments $AppDataRoot
    $lines = @($result.Stdout -split "`r?`n" | Where-Object { $_.Length -gt 0 })
    Assert-Endpoint ($result.ExitCode -eq 0) "$Name exits 0"
    Assert-Endpoint ($lines.Count -eq 1 -and $lines[0] -ceq $Expected) "$Name prints exactly '$Expected'"
    Assert-Endpoint (-not ($result.Stdout + $result.Stderr).Contains($secret)) "$Name never prints the password"
    Assert-Endpoint (-not ($result.Stdout + $result.Stderr).Contains('Username=antiphon')) "$Name never prints the connection string"
}

try {
    # ---- control: the docker shim records a call ----------------------------------------
    $control = Invoke-Child @('-NoProfile', '-NonInteractive', '-Command', 'docker version; exit 0')
    Assert-Endpoint ($control.ExitCode -eq 0 -and (Test-Path -LiteralPath $dockerMarker)) 'control the docker shim records a call'
    if (Test-Path -LiteralPath $dockerMarker) { Remove-Item -LiteralPath $dockerMarker -Force }

    # ---- V-2 remote probe ---------------------------------------------------------------
    Invoke-Probe 'V-2 remote' "Host=100.64.0.2;Port=5432;Password=$secret;Username=antiphon;Database=antiphon" 'database-endpoint host=100.64.0.2 port=5432 mode=remote'
    Assert-Endpoint (-not (Test-Path -LiteralPath $dockerMarker)) 'V-2 the probe runs no docker'

    # ---- V-3 local probe and tracked appsettings ----------------------------------------
    Invoke-Probe 'V-3 local' "Host=localhost;Port=17280;Password=$secret;Username=antiphon;Database=antiphon" 'database-endpoint host=localhost port=17280 mode=local'
    Invoke-Probe 'V-3 tracked appsettings' $null 'database-endpoint host=localhost port=17280 mode=local'

    # User-secrets fixtures outrank the tracked file (flat key as dotnet user-secrets set writes it, then nested).
    $flatRoot = Join-Path $scratch 'appdata-flat'
    $flatDir = Join-Path $flatRoot 'Microsoft/UserSecrets/aspire-antiphon-apphost'
    [void](New-Item -ItemType Directory -Path $flatDir -Force)
    Set-Content -LiteralPath (Join-Path $flatDir 'secrets.json') -Encoding ascii -Value (@{ 'ConnectionStrings:DefaultConnection' = "Host=100.64.0.9;Port=5432;Password=$secret;Username=antiphon" } | ConvertTo-Json)
    Invoke-Probe 'V-3 user-secrets flat key' $null 'database-endpoint host=100.64.0.9 port=5432 mode=remote' $flatRoot
    $nestedRoot = Join-Path $scratch 'appdata-nested'
    $nestedDir = Join-Path $nestedRoot 'Microsoft/UserSecrets/aspire-antiphon-apphost'
    [void](New-Item -ItemType Directory -Path $nestedDir -Force)
    Set-Content -LiteralPath (Join-Path $nestedDir 'secrets.json') -Encoding ascii -Value (@{ ConnectionStrings = @{ DefaultConnection = "Server=db.example.test;Password=$secret;Username=antiphon" } } | ConvertTo-Json)
    Invoke-Probe 'V-3 user-secrets nested key, default port' $null 'database-endpoint host=db.example.test port=5432 mode=remote' $nestedRoot
    Assert-Endpoint (-not (Test-Path -LiteralPath $dockerMarker)) 'V-3 the probes run no docker'

    # F1: effective configuration, including explicitly present empty/invalid providers.
    $envConnection = "Host=env-target.invalid;Port=5432;Password=$secret;Username=antiphon"
    $probeArgs = @('-NoProfile', '-NonInteractive', '-File', $bootstrap, '-DatabaseProbeOnly')
    foreach ($envKey in @('ConnectionStrings__DefaultConnection', 'ConnectionStrings:DefaultConnection', 'CUSTOMCONNSTR_DefaultConnection')) {
        $result = Invoke-Child $probeArgs $flatRoot @{ $envKey = $envConnection }
        Assert-Endpoint ($result.ExitCode -eq 0 -and $result.Stdout.Trim() -ceq 'database-endpoint host=env-target.invalid port=5432 mode=remote') "F1 $envKey outranks secret and appsettings"
        Assert-Endpoint (-not ($result.Stdout + $result.Stderr).Contains($secret)) "F1 $envKey stays credential-safe"
    }
    $result = Invoke-Child ($probeArgs + @('-ConnectionString', 'Host=cli.invalid;Port=5432')) $flatRoot @{ ConnectionStrings__DefaultConnection = $envConnection }
    Assert-Endpoint ($result.ExitCode -eq 0 -and $result.Stdout.Trim() -ceq 'database-endpoint host=cli.invalid port=5432 mode=remote') 'F1 command line outranks environment'
    foreach ($value in @('', 'not a connection string', 'Host=h;Port=broken')) {
        Set-Content -LiteralPath (Join-Path $flatDir 'secrets.json') -Encoding ascii -Value (@{ 'ConnectionStrings:DefaultConnection' = $value } | ConvertTo-Json)
        $result = Invoke-Child $probeArgs $flatRoot
        Assert-Endpoint ($result.ExitCode -ne 0 -and $result.Stdout -match 'unresolved source=user-secrets reason=(empty|invalid)') 'F1 empty or malformed secret fails closed with a reason'
    }
    $result = Invoke-Child ($probeArgs + @('-ConnectionString', '')) $appData
    Assert-Endpoint ($result.ExitCode -ne 0 -and $result.Stdout -match 'unresolved source=parameter reason=empty') 'F1 explicitly empty command line fails closed'
    $result = Invoke-Child $probeArgs $appData @{ ConnectionStrings__DefaultConnection = 'not a connection string' }
    Assert-Endpoint ($result.ExitCode -ne 0 -and $result.Stdout -match 'unresolved source=environment reason=invalid') 'F1 malformed environment fails closed'
    $result = Invoke-Child $probeArgs $nestedRoot @{ DOTNET_ENVIRONMENT = 'Production' }
    Assert-Endpoint ($result.ExitCode -eq 0 -and $result.Stdout.Trim() -ceq 'database-endpoint host=localhost port=17280 mode=local') 'F1 Production does not load Development secrets'
    $result = Invoke-Child $probeArgs $nestedRoot @{ DOTNET_ENVIRONMENT = $null }
    Assert-Endpoint ($result.ExitCode -eq 0 -and $result.Stdout.Trim() -ceq 'database-endpoint host=db.example.test port=5432 mode=remote') 'F1 absent host environment follows the normal AppHost launch profile'
    Set-Content -LiteralPath (Join-Path $flatDir 'secrets.json') -Encoding ascii -Value '{"ConnectionStrings:DefaultConnection":""}'
    $result = Invoke-Child $probeArgs $flatRoot @{ DOTNET_ENVIRONMENT = $null }
    Assert-Endpoint ($result.ExitCode -ne 0 -and $result.Stdout -match 'unresolved source=user-secrets reason=empty') 'F1 empty secret without a parent host environment fails closed'

    # ---- library: host, port and mode only ----------------------------------------------
    Assert-Endpoint (Test-Path -LiteralPath $library -PathType Leaf) 'lib scripts/lib/DatabaseEndpoint.ps1 exists'
    if (Test-Path -LiteralPath $library -PathType Leaf) {
        . $library
        $endpoint = Get-AntiphonDatabaseEndpoint -ConnectionString "Host=100.64.0.2;Port=5432;Password=$secret;Username=antiphon"
        $names = @($endpoint.PSObject.Properties | ForEach-Object { $_.Name }) -join ','
        Assert-Endpoint ($names -ceq 'Host,Port,Mode') "lib endpoint has only Host, Port and Mode (saw $names)"
        Assert-Endpoint (-not ($endpoint | ConvertTo-Json -Compress).Contains($secret)) 'lib endpoint carries no password'
        foreach ($case in @(
                @('Host=127.0.0.1;Port=17280', '127.0.0.1', 17280, 'local'),
                @('Host=::1;Port=17280', '::1', 17280, 'local'),
                @('Host=LOCALHOST;Port=17280', 'LOCALHOST', 17280, 'local'),
                @('Host=localhost;Port=5432', 'localhost', 5432, 'remote'),
                @('Host=localhost', 'localhost', 5432, 'remote'),
                @('Host=100.64.0.2;Port=17280', '100.64.0.2', 17280, 'remote'))) {
            $e = Get-AntiphonDatabaseEndpoint -ConnectionString $case[0]
            Assert-Endpoint ($null -ne $e -and $e.Host -ceq $case[1] -and $e.Port -eq $case[2] -and $e.Mode -ceq $case[3]) "lib '$($case[0])' is $($case[1]):$($case[2]) $($case[3])"
        }
        foreach ($bad in @('', 'Port=5432;Password=x', 'Host=h;Port=notaport', 'Host=h;Port=0', 'Host=h;Port=65536', 'not a connection string')) {
            Assert-Endpoint ($null -eq (Get-AntiphonDatabaseEndpoint -ConnectionString $bad)) "lib '$bad' is unresolved"
        }
    }

    # ---- R-2 source pins in bootstrap-check.ps1 -----------------------------------------
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($bootstrap, [ref]$tokens, [ref]$errors)
    Assert-Endpoint ($errors.Count -eq 0) 'R-2 bootstrap-check.ps1 parses'
    Assert-Endpoint (-not [regex]::IsMatch([IO.File]::ReadAllText($bootstrap), '[^\x00-\x7F]')) 'R-2 bootstrap-check.ps1 stays ASCII'
    if (Test-Path -LiteralPath $library -PathType Leaf) {
        Assert-Endpoint (-not [regex]::IsMatch([IO.File]::ReadAllText($library), '[^\x00-\x7F]')) 'R-2 DatabaseEndpoint.ps1 stays ASCII'
    }
    $statements = @($ast.EndBlock.Statements)
    $probeIndex = -1
    for ($i = 0; $i -lt $statements.Count; $i++) {
        $s = $statements[$i]
        if ($s -is [System.Management.Automation.Language.IfStatementAst] -and $s.Clauses[0].Item1.Extent.Text -match '^\$DatabaseProbeOnly$') { $probeIndex = $i; break }
    }
    Assert-Endpoint ($probeIndex -ge 0) 'R-2 a top-level if ($DatabaseProbeOnly) exists'
    if ($probeIndex -ge 0) {
        $probeBody = $statements[$probeIndex].Clauses[0].Item2
        $exits = @($probeBody.FindAll({ param($n) $n -is [System.Management.Automation.Language.ExitStatementAst] }, $true))
        Assert-Endpoint ($exits.Count -ge 1 -and $probeBody.Statements[-1] -is [System.Management.Automation.Language.ExitStatementAst]) 'R-2 the probe branch ends in exit'
        $sideEffects = '^(?:docker|Get-NativeOutput|Test-TcpPort|Invoke-TimedJob|Write-Check|Invoke-RestMethod)$'
        $early = @()
        for ($i = 0; $i -lt $probeIndex; $i++) {
            if ($statements[$i] -is [System.Management.Automation.Language.FunctionDefinitionAst]) { continue }
            $early += @($statements[$i].FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -match $sideEffects }, $true))
            $early += @($statements[$i].FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandExpressionAst] -or $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true) | Where-Object { $_.Extent.Text -match 'TcpClient|check-agent-context' })
        }
        Assert-Endpoint ($early.Count -eq 0) 'R-2 no docker, socket or check call runs before the probe exit'
        $probeCalls = @($probeBody.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -match $sideEffects }, $true))
        Assert-Endpoint ($probeCalls.Count -eq 0) 'R-2 the probe branch runs no docker, socket or check call'
    }

    function Test-LocalGate($Node) {
        $p = $Node.Parent
        $child = $Node
        while ($null -ne $p) {
            if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $true }
            if ($p -is [System.Management.Automation.Language.IfStatementAst]) {
                foreach ($clause in $p.Clauses) {
                    if ([object]::ReferenceEquals($clause.Item2, $child) -and $clause.Item1.Extent.Text -match "^\`$databaseEndpoint\.Mode\s+-eq\s+'local'$") { return $true }
                }
            }
            $child = $p
            $p = $p.Parent
        }
        return $false
    }
    $localLiteral = 'antiphon-postgres|antiphon_pgdata|17280|pg_isready|docker-compose\.dev\.yml'
    $literals = @($ast.FindAll({ param($n)
                ($n -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                 $n -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -or
                 $n -is [System.Management.Automation.Language.ConstantExpressionAst]) -and
                ($n.Parent -isnot [System.Management.Automation.Language.ExpandableStringExpressionAst]) -and
                [string]$n.Value -match $localLiteral }, $true))
    Assert-Endpoint ($literals.Count -ge 6) "R-2 the local container checks still exist ($($literals.Count) literals)"
    $ungated = @($literals | Where-Object { -not (Test-LocalGate $_) })
    $where = ($ungated | ForEach-Object { $_.Extent.StartLineNumber }) -join ','
    Assert-Endpoint ($ungated.Count -eq 0) "R-2 every local container literal is gated by mode local (ungated lines: $where)"

    $gates = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and
                @($n.Clauses | Where-Object { $_.Item1.Extent.Text -match "^\`$databaseEndpoint\.Mode\s+-eq\s+'local'$" }).Count -gt 0 }, $true))
    Assert-Endpoint ($gates.Count -ge 2) 'R-2 the container section and the TCP section are both gated by mode local'
    $remoteTcp = @($gates | Where-Object { $null -ne $_.ElseClause } | ForEach-Object {
            $_.ElseClause.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Test-TcpPort' -and
                    $n.Extent.Text -match '\$databaseEndpoint\.Host' -and $n.Extent.Text -match '\$databaseEndpoint\.Port' }, $true) })
    Assert-Endpoint ($remoteTcp.Count -ge 1) 'R-2 the remote branch TCP-checks the resolved host and port'

    # Run the gated sections in process with recording stubs: no docker, no socket.
    $gated = @($statements | Where-Object { $gates -contains $_ })
    Assert-Endpoint ($gated.Count -ge 2) 'R-2 both gated sections are top-level statements'
    $stubs = @'
param($databaseEndpoint, $dockerUp, $dockerPath, $repoRoot, $tcpOpen, $calls, $readyReason)
Set-StrictMode -Off
function Write-Check { param($Name, $Status, $Detail, $Remedial) $calls.Add("check|$Name|$Status|$Detail|$Remedial") }
function Test-TcpPort { param($HostName, $Port, $TimeoutMs) $calls.Add("tcp|$HostName|$Port"); return $tcpOpen }
function Get-NativeOutput { param($File, $Arguments) $calls.Add('native|' + ($Arguments -join ' ')); return @{ Text = 'running|healthy'; ExitCode = 0; Succeeded = $true } }
function Test-AntiphonRemoteDatabase { param($RepoRoot, $ConnectionString) $calls.Add('postgres|readiness-and-identity'); return @{ Succeeded = ($readyReason -eq 'ready'); Reason = $readyReason; ElapsedMs = 1 } }
'@
    $sections = [scriptblock]::Create($stubs + "`n" + (($gated | ForEach-Object { $_.Extent.Text }) -join "`n"))
    function Invoke-Sections($Endpoint, [bool]$TcpOpen, [string]$ReadyReason = 'ready') {
        $calls = New-Object System.Collections.Generic.List[string]
        & $sections $Endpoint $true 'docker' $SourceRoot $TcpOpen $calls $ReadyReason
        return , $calls
    }
    $remote = [pscustomobject][ordered]@{ Host = '100.64.0.2'; Port = 5432; Mode = 'remote' }
    $local = [pscustomobject][ordered]@{ Host = 'localhost'; Port = 17280; Mode = 'local' }
    foreach ($open in @($true, $false)) {
        $calls = Invoke-Sections $remote $open
        $text = $calls -join "`n"
        Assert-Endpoint (@($calls | Where-Object { $_ -like 'native|*' }).Count -eq 0) "R-2 remote (tcp open=$open) runs no docker inspect, volume inspect or pg_isready"
        Assert-Endpoint (@($calls | Where-Object { $_ -like 'tcp|*' }).Count -eq 1 -and $calls.Contains('tcp|100.64.0.2|5432')) "R-2 remote (tcp open=$open) TCP-checks 100.64.0.2:5432 only"
        Assert-Endpoint (-not [regex]::IsMatch($text, 'antiphon-postgres|antiphon_pgdata|17280|docker-compose\.dev\.yml')) "R-2 remote (tcp open=$open) names no local container, volume, port or compose file"
        $status = if ($open) { 'PASS' } else { 'FAIL' }
        Assert-Endpoint (@($calls | Where-Object { $_ -like "check|database|$status|*100.64.0.2:5432*" }).Count -eq 1) "R-2 remote (tcp open=$open) reports database $status with host and port"
    }
    foreach ($reason in @('protocol-failed', 'identity-mismatch', 'authentication-failed', 'timeout')) {
        $calls = Invoke-Sections $remote $true $reason
        Assert-Endpoint (@($calls | Where-Object { $_ -like "check|database|FAIL|*$reason*" }).Count -eq 1 -and
            @($calls | Where-Object { $_ -like 'check|database|PASS|*' }).Count -eq 0) "F2 open TCP with $reason fails database check distinctly"
    }
    $calls = Invoke-Sections $local $true
    Assert-Endpoint ($calls.Contains('tcp|127.0.0.1|17280')) 'R-2 local TCP-checks 127.0.0.1:17280'
    Assert-Endpoint (@($calls | Where-Object { $_ -like 'native|inspect*antiphon-postgres' }).Count -eq 1) 'R-2 local inspects antiphon-postgres'
    Assert-Endpoint ($calls.Contains('native|volume inspect antiphon_pgdata')) 'R-2 local inspects antiphon_pgdata'
    Assert-Endpoint (@($calls | Where-Object { $_ -like 'native|compose*pg_isready*' }).Count -eq 1) 'R-2 local runs pg_isready through the dev compose'
} finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}

Write-Host "bootstrap-database-endpoint passed=$($script:passed) failed=$($script:failed)"
if ($script:failed -gt 0) { exit 1 }
& $pwsh -NoProfile -File (Join-Path $SourceRoot 'scripts/test-bootstrap-remote-database.ps1') -SourceRoot $SourceRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
exit 0
