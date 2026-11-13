# ASCII-only, Windows PowerShell 5.1 compatible. Requires the PostgreSQL psql
# client on PATH for remote checks. Never logs client output or configuration.
function Test-AntiphonRemoteDatabase {
    param([string]$RepoRoot, [string]$ConnectionString, [string]$PsqlPath)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $result = @{ Succeeded = $false; Reason = 'probe-config-invalid'; ElapsedMs = 0 }
    $process = $null
    $start = $null
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    try {
        $arguments = @{ RepoRoot = $RepoRoot }
        if ($PSBoundParameters.ContainsKey('ConnectionString')) { $arguments.ConnectionString = $ConnectionString }
        $configuration = Resolve-AntiphonDefaultConnection @arguments
        if ($configuration.Error -or -not $configuration.Found) { return $result }
        $endpoint = Get-AntiphonDatabaseEndpoint $configuration.Value
        if ($null -eq $endpoint) { return $result }
        $builder.set_ConnectionString($configuration.Value)
        $configuration.Value = $null
        $database = $null; $username = $null; $password = ''
        foreach ($key in @('Database', 'Initial Catalog')) {
            $value = $null
            if ($builder.TryGetValue($key, [ref]$value)) { $database = [string]$value; break }
        }
        foreach ($key in @('Username', 'User ID', 'UserId', 'User Name', 'UID')) {
            $value = $null
            if ($builder.TryGetValue($key, [ref]$value)) { $username = [string]$value; break }
        }
        foreach ($key in @('Password', 'Pwd')) {
            $value = $null
            if ($builder.TryGetValue($key, [ref]$value)) { $password = [string]$value; break }
        }
        # An explicit expected database is required: the user's default database
        # cannot stand in for identity. No query result or database name is logged.
        if ([string]::IsNullOrWhiteSpace($database) -or [string]::IsNullOrWhiteSpace($username)) { return $result }
        $sslMode = 'prefer'
        $value = $null
        if ($builder.TryGetValue('SSL Mode', [ref]$value) -or $builder.TryGetValue('SslMode', [ref]$value)) {
            switch (([string]$value).Replace(' ', '').Replace('-', '').ToLowerInvariant()) {
                'disable' { $sslMode = 'disable' }
                'allow' { $sslMode = 'allow' }
                'prefer' { $sslMode = 'prefer' }
                'require' { $sslMode = 'require' }
                'verifyca' { $sslMode = 'verify-ca' }
                'verifyfull' { $sslMode = 'verify-full' }
                default { return $result }
            }
        }
        if ([string]::IsNullOrEmpty($PsqlPath)) {
            $client = Get-Command psql -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -eq $client) { $result.Reason = 'client-unavailable'; return $result }
            $PsqlPath = $client.Source
        }
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $PsqlPath
        # Constant argv, no shell, no credentials, no user-supplied SQL. -X ignores
        # psqlrc, -w forbids prompts, verbose errors provide SQLSTATE for classification.
        $start.Arguments = '-X -w -A -t -v ON_ERROR_STOP=1 -v VERBOSITY=verbose -c "SELECT current_database();"'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = [Text.Encoding]::UTF8
        $start.StandardErrorEncoding = [Text.Encoding]::UTF8
        foreach ($key in @($start.EnvironmentVariables.Keys)) {
            if ([string]$key -like 'PG*') { $start.EnvironmentVariables.Remove($key) }
        }
        $start.EnvironmentVariables['PGHOST'] = $endpoint.Host
        $start.EnvironmentVariables['PGPORT'] = [string]$endpoint.Port
        $start.EnvironmentVariables['PGDATABASE'] = $database
        $start.EnvironmentVariables['PGUSER'] = $username
        $start.EnvironmentVariables['PGPASSWORD'] = $password
        $start.EnvironmentVariables['PGSSLMODE'] = $sslMode
        $start.EnvironmentVariables['PGGSSENCMODE'] = 'disable'
        $start.EnvironmentVariables['PGCLIENTENCODING'] = 'UTF8'
        $start.EnvironmentVariables['LC_ALL'] = 'C'
        $start.EnvironmentVariables['PGCONNECT_TIMEOUT'] = '5'
        $start.EnvironmentVariables['PGOPTIONS'] = '-c statement_timeout=5000 -c default_transaction_read_only=on'
        # Avoid ambient .pgpass when the effective connection has no password.
        $start.EnvironmentVariables['PGPASSFILE'] = [IO.Path]::Combine([IO.Path]::GetTempPath(), ('antiphon-no-passfile-' + [guid]::NewGuid().ToString('N')))
        foreach ($pair in @(@('Root Certificate', 'PGSSLROOTCERT'), @('SSL Certificate', 'PGSSLCERT'), @('SSL Key', 'PGSSLKEY'), @('SSL Password', 'PGSSLPASSWORD'))) {
            $value = $null
            if ($builder.TryGetValue($pair[0], [ref]$value)) { $start.EnvironmentVariables[$pair[1]] = [string]$value }
        }
        $result.Reason = 'client-unavailable'
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $remaining = [Math]::Max(1, 5000 - [int]$clock.ElapsedMilliseconds)
        if (-not $process.WaitForExit($remaining)) {
            $process.Kill()
            if (-not $process.WaitForExit(1000)) { $result.Reason = 'client-stop-failed'; return $result }
            $result.Reason = 'timeout'
            return $result
        }
        # Both pipe readers share a bounded drain after the observed client exit.
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 1000)) {
            $result.Reason = 'timeout'; return $result
        }
        if ($process.ExitCode -eq 0) {
            if ($stdout.Result.TrimEnd("`r", "`n") -ceq $database) {
                $result.Succeeded = $true; $result.Reason = 'ready'
            } else { $result.Reason = 'identity-mismatch' }
        } elseif ($stderr.Result -match '28P01|28000|authentication failed|no password supplied|no pg_hba.conf entry') {
            $result.Reason = 'authentication-failed'
        } elseif ($stderr.Result -match '3D000|database .* does not exist') {
            $result.Reason = 'identity-mismatch'
        } elseif ($stderr.Result -match 'timeout|timed out|57014') {
            $result.Reason = 'timeout'
        } elseif ($stderr.Result -match 'invalid response|expected authentication request|protocol|server closed the connection unexpectedly') {
            $result.Reason = 'protocol-failed'
        } else { $result.Reason = 'readiness-failed' }
        return $result
    } catch {
        # Exception text and psql output may include credentials or server payloads.
        return $result
    } finally {
        if ($null -ne $process) { $process.Dispose() }
        if ($null -ne $start) { $start.EnvironmentVariables.Clear() }
        $builder.Clear()
        $password = $null; $ConnectionString = $null
        $result.ElapsedMs = $clock.ElapsedMilliseconds
    }
}
