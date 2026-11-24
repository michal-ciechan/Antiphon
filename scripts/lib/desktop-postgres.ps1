#requires -Version 5.1
# Shared read-only database access for desktop operator scripts. ASCII only.
. (Join-Path $PSScriptRoot 'antiphon-cli.ps1')

function Invoke-DesktopPostgresDocker {
    param([string[]]$Arguments, [string]$Operation, [string]$Sql)

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'docker'
    $startInfo.Arguments = ($Arguments | ForEach-Object { ConvertTo-AntiphonCliQuotedArgument $_ }) -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $PSBoundParameters.ContainsKey('Sql')
    $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8

    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        try { $started = $process.Start() } catch { $started = $false }
        if (-not $started) { throw 'Could not start Docker CLI. Check that docker is installed and on PATH.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $clock = [Diagnostics.Stopwatch]::StartNew()
        if ($startInfo.RedirectStandardInput) {
            # Send SQL over stdin to preserve quotes across Windows PowerShell and Docker.
            try {
                $writing = $process.StandardInput.WriteLineAsync($Sql)
                if (-not $writing.Wait(30000)) { throw 'stdin timed out' }
                $process.StandardInput.Close()
            } catch {
                throw "$Operation failed while sending SQL. Check Docker access and PostgreSQL availability."
            }
        }
        if (-not $process.WaitForExit([int][Math]::Max(0, 30000 - $clock.ElapsedMilliseconds))) {
            throw "$Operation timed out. Check Docker access and PostgreSQL availability."
        }
        if ($process.ExitCode -ne 0) {
            throw "$Operation failed (exit $($process.ExitCode)). Check Docker access and PostgreSQL availability."
        }
        # Docker and psql stderr can contain connection or statement details. Keep it private.
        [void]$stderr.GetAwaiter().GetResult()
        return $stdout.GetAwaiter().GetResult().TrimEnd()
    } finally {
        if ($started -and -not $process.HasExited) {
            try { $process.Kill() } catch [InvalidOperationException] { }
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

function Resolve-DesktopPostgresTarget {
    param([string]$Container, [string]$User = 'antiphon', [string]$Database = 'antiphon')

    if ($Container) {
        return @{ DockerPrefix = @(); Container = $Container; User = $User; Database = $Database; Socket = @() }
    }

    # Same UserSecretsId and file location as Antiphon.AppHost. The committed
    # appsettings.json contains a development database and is unsafe as a fallback.
    $secretsRoot = if ($env:APPDATA) { Join-Path $env:APPDATA 'Microsoft/UserSecrets' }
                   else { Join-Path $HOME '.microsoft/usersecrets' }
    $secretFile = Join-Path $secretsRoot 'aspire-antiphon-apphost/secrets.json'
    $configHint = 'Set the AppHost user secret ConnectionStrings:DefaultConnection for the desktop account, or specify a local/test container explicitly.'
    try {
        $config = Get-Content -LiteralPath $secretFile -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        $flatKey = $config.PSObject.Properties['ConnectionStrings:DefaultConnection']
        $connectionString = if ($flatKey) { $flatKey.Value } else { $config.ConnectionStrings.DefaultConnection }
        $connection = New-Object Data.Common.DbConnectionStringBuilder
        $connection.ConnectionString = $connectionString
        $hostName = [string]$connection['Host']
        $port = if ($connection.ContainsKey('Port')) { [int]$connection['Port'] } else { 5432 }
        $User = foreach ($key in @('Username', 'User ID', 'UserId', 'User Name', 'User', 'UID')) {
            if ($connection.ContainsKey($key)) { [string]$connection[$key]; break }
        }
        $Database = [string]$connection['Database']
        if (-not $hostName -or $port -lt 1 -or $port -gt 65535 -or -not $User -or -not $Database) { throw 'incomplete' }
    } catch {
        throw "Could not resolve the production PostgreSQL connection. $configHint"
    }

    # Use the existing SSH Docker context so the desktop needs no separate psql
    # installation. Check the published endpoint and pin the exact container ID
    # before using its Unix socket and configured database/user.
    $prefix = @('--context', 'antiphon-server2')
    $format = '{{.Id}}|{{.State.Running}}|{{json (index .NetworkSettings.Ports "5432/tcp")}}'
    $inspection = Invoke-DesktopPostgresDocker -Arguments ($prefix + @('inspect', '--type', 'container', '--format', $format, 'antiphon-server2-postgres-postgres-1')) `
        -Operation 'Production PostgreSQL lookup via the antiphon-server2 Docker context'
    $id, $running, $bindingsJson = $inspection -split '\|', 3
    try { $bindings = ConvertFrom-Json -InputObject $bindingsJson -ErrorAction Stop }
    catch { throw 'Could not read the production PostgreSQL port binding from the antiphon-server2 Docker context.' }
    $matchesEndpoint = $bindings | Where-Object { $null -ne $_ -and $_.HostIp -eq $hostName -and $_.HostPort -eq [string]$port }
    if ($id -cnotmatch '^[0-9a-f]{64}$' -or $running -cne 'true' -or -not $matchesEndpoint) {
        throw 'The running server2 PostgreSQL container must publish the endpoint in the AppHost user secret ConnectionStrings:DefaultConnection.'
    }
    return @{ DockerPrefix = $prefix; Container = $id; User = $User; Database = $Database; Socket = @('-h', '/var/run/postgresql', '-p', '5432') }
}

function Invoke-DesktopPostgresQuery {
    param([hashtable]$Target, [string]$Sql)

    # psql treats a --dbname containing '=' or a URI as connection info. Keep the
    # selected Docker socket and prevent embedded connection options taking over.
    if (-not $Target.Database -or $Target.Database -match '=|^postgres(?:ql)?://') {
        throw 'Specify a PostgreSQL database name.'
    }
    $arguments = $Target.DockerPrefix + @('exec', '-i', $Target.Container, 'psql', '-X', '-A', '-t', '-w', '-v', 'ON_ERROR_STOP=1') +
        $Target.Socket + @('-U', $Target.User, '-d', $Target.Database, '-f', '-')
    return Invoke-DesktopPostgresDocker -Arguments $arguments -Operation 'PostgreSQL query' -Sql $Sql
}
