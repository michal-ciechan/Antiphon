#requires -Version 5.1
# CARD-1987 W-1..W-5. One throwaway Postgres, removed by the exact name below.
$ErrorActionPreference = "Stop"

$Image = "postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b"
$ScriptPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "pg-qps.ps1"
$passed = 0
$failed = 0
$name = ""
$password = ""
$secretBefore = ""
$secretInside = ""
$signals = New-Object System.Collections.Generic.List[string]

function Hide([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return "" }
    if ($secretBefore.Length -gt 0) { $text = $text.Replace($secretBefore, "[redacted]") }
    if ($secretInside.Length -gt 0) { $text = $text.Replace($secretInside, "[redacted]") }
    if ($password.Length -gt 0) { $text = $text.Replace($password, "[redacted]") }
    return $text
}

function Add-Result([bool]$ok, [string]$witness, [string]$detail) {
    if ($ok) {
        $script:passed++
        Write-Output ("PASS " + $witness)
    }
    else {
        $script:failed++
        Write-Output ("FAIL " + $witness + " " + (Hide $detail))
    }
}

function Invoke-Child([string]$file, [string]$arguments, [string]$stdin, [hashtable]$envExtra) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    if ($null -ne $envExtra) {
        foreach ($key in $envExtra.Keys) { $psi.EnvironmentVariables[$key] = [string]$envExtra[$key] }
    }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    if ($null -ne $stdin) { $proc.StandardInput.Write($stdin) }
    $proc.StandardInput.Close()
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    return @{ Exit = $proc.ExitCode; Out = $outTask.Result; Err = $errTask.Result }
}

function Invoke-Psql([string]$sql) {
    return Invoke-Child "docker" ("exec -i " + $name + " psql -X -A -t -U antiphon -d antiphon -v ON_ERROR_STOP=1") $sql $null
}

function New-Secret {
    $bytes = New-Object byte[] 18
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    return ([Convert]::ToBase64String($bytes) -replace "[^A-Za-z0-9]", "x")
}

try {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $id = New-Object byte[] 8
    $rng.GetBytes($id)
    $name = "c1987-" + (-join ($id | ForEach-Object { $_.ToString("x2") }))
    if ($name -notmatch '^c1987-[a-f0-9]{16}$') { throw "refusing container name" }
    if ($name -match 'antiphon_pgdata' -or $name -match 'antiphon-server2') { throw "refusing container name" }
    $password = New-Secret
    $secretBefore = "b" + (New-Secret)
    $secretInside = "i" + (New-Secret)

    $phase = [System.Diagnostics.Stopwatch]::StartNew()
    $inspect = Invoke-Child "docker" ("image inspect " + $Image) "" $null
    if ($inspect.Exit -ne 0) {
        $pull = Invoke-Child "docker" ("pull " + $Image) "" $null
        if ($pull.Exit -ne 0) { throw "image pull failed" }
    }
    $runArgs = "run -d --name $name --tmpfs /var/lib/postgresql/data:rw,nosuid,size=512m -e POSTGRES_PASSWORD -e POSTGRES_USER=antiphon -e POSTGRES_DB=antiphon $Image -c shared_preload_libraries=pg_stat_statements -c pg_stat_statements.track=top"
    $run = Invoke-Child "docker" $runArgs "" @{ POSTGRES_PASSWORD = $password }
    if ($run.Exit -ne 0) { throw ("container start failed " + (Hide $run.Err)) }
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        $probe = Invoke-Child "docker" ("exec " + $name + " pg_isready -U antiphon -d antiphon") "" $null
        if ($probe.Exit -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        $logs = Invoke-Child "docker" ("logs " + $name) "" $null
        throw ("postgres did not become ready " + (Hide $logs.Err) + " " + (Hide $logs.Out))
    }
    $ext = Invoke-Psql "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;"
    if ($ext.Exit -ne 0) { throw ("extension create failed " + (Hide $ext.Err)) }
    $phase.Stop()
    Write-Output ("PHASE container ms=" + [int]$phase.ElapsedMilliseconds)

    $bytes = [System.IO.File]::ReadAllBytes($ScriptPath)
    $ascii = $true
    foreach ($b in $bytes) { if ($b -gt 127) { $ascii = $false; break } }
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$parseErrors)
    $ternary = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.TernaryExpressionAst] }, $true))
    $chain = @($ast.FindAll({ $args[0] -is [System.Management.Automation.Language.PipelineChainAst] }, $true))
    $text = [System.IO.File]::ReadAllText($ScriptPath)
    $syntaxClean = $ascii -and $parseErrors.Count -eq 0 -and $ternary.Count -eq 0 -and $chain.Count -eq 0
    if ($text.Contains("??") -or $text.Contains("?.") -or $text.Contains("&&") -or $text.Contains("||")) { $syntaxClean = $false }
    if ($text.Contains("-AsHashtable") -or $text.Contains("-Parallel") -or $text.Contains("ArgumentList")) { $syntaxClean = $false }

    $hostExe = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    function Invoke-Window([string]$sql) {
        $signal = Join-Path ([System.IO.Path]::GetTempPath()) ("c1987-signal-" + [guid]::NewGuid().ToString("N"))
        $script:signals.Add($signal)
        $args = "-NoProfile -File `"$ScriptPath`" -Seconds 10 -Container $name -User antiphon -Database antiphon -Json -SignalFile `"$signal`""
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $hostExe
        $psi.Arguments = $args
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi
        [void]$proc.Start()
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        $deadline = (Get-Date).AddSeconds(90)
        while (-not (Test-Path -LiteralPath $signal) -and -not $proc.HasExited -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        $signalled = Test-Path -LiteralPath $signal
        if ($signalled -and $sql.Length -gt 0) {
            $ran = Invoke-Psql $sql
            if ($ran.Exit -ne 0) {
                $proc.WaitForExit()
                return @{ Exit = $proc.ExitCode; Out = $outTask.Result; Err = "workload failed " + (Hide $ran.Err); Signalled = $true }
            }
        }
        if (-not $proc.WaitForExit(40000)) {
            try { $proc.Kill() } catch { }
            return @{ Exit = 9; Out = ""; Err = "window timeout"; Signalled = $signalled }
        }
        return @{ Exit = $proc.ExitCode; Out = $outTask.Result; Err = (Hide $errTask.Result); Signalled = $signalled }
    }

    function Test-Clean([string]$out) {
        if ($out.Contains($secretBefore)) { return $false }
        if ($out.Contains($secretInside)) { return $false }
        if ($password.Length -gt 0 -and $out.Contains($password)) { return $false }
        return $true
    }

    $w = [System.Diagnostics.Stopwatch]::StartNew()
    $sqlA = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt 50; $i++) { [void]$sqlA.AppendLine("-- rt.witness.a"); [void]$sqlA.AppendLine("SELECT 1;") }
    for ($i = 0; $i -lt 20; $i++) { [void]$sqlA.AppendLine("-- rt.witness.b"); [void]$sqlA.AppendLine("SELECT 2;") }
    $r1 = Invoke-Window $sqlA.ToString()
    $ok1 = $false
    $detail1 = "exit " + $r1.Exit
    if ($r1.Signalled -and $r1.Exit -eq 0 -and (Test-Clean $r1.Out)) {
        $j = $r1.Out | ConvertFrom-Json
        $a = @($j.operations | Where-Object { $_.operation -eq "rt.witness.a" })
        $b = @($j.operations | Where-Object { $_.operation -eq "rt.witness.b" })
        $ok1 = $a.Count -eq 1 -and [int]$a[0].calls -eq 50 -and $b.Count -eq 1 -and [int]$b[0].calls -eq 20
        if (-not $ok1) { $detail1 = "calls a=" + ($(if ($a.Count -eq 1) { $a[0].calls } else { "missing" })) + " b=" + ($(if ($b.Count -eq 1) { $b[0].calls } else { "missing" })) }
        $script:versionJson = $j
    }
    elseif (-not $r1.Signalled) { $detail1 = "no signal " + $r1.Err }
    Add-Result $ok1 "W-1" $detail1
    $w.Stop()
    Write-Output ("PHASE W-1 ms=" + [int]$w.ElapsedMilliseconds)

    $w.Restart()
    $sqlB = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt 10; $i++) { [void]$sqlB.AppendLine("BEGIN;"); [void]$sqlB.AppendLine("COMMIT;") }
    $r2 = Invoke-Window $sqlB.ToString()
    $ok2 = $false
    $detail2 = "exit " + $r2.Exit
    if ($r2.Exit -eq 0 -and (Test-Clean $r2.Out)) {
        $j2 = $r2.Out | ConvertFrom-Json
        $begin = @($j2.operations | Where-Object { $_.operation -eq "tx.begin" })
        $commit = @($j2.operations | Where-Object { $_.operation -eq "tx.commit" })
        $ok2 = $begin.Count -eq 1 -and [int]$begin[0].calls -eq 10 -and $commit.Count -eq 1 -and [int]$commit[0].calls -eq 10
        if (-not $ok2) { $detail2 = "begin=" + ($(if ($begin.Count -eq 1) { $begin[0].calls } else { "missing" })) + " commit=" + ($(if ($commit.Count -eq 1) { $commit[0].calls } else { "missing" })) }
    }
    Add-Result $ok2 "W-2" $detail2
    $w.Stop()
    Write-Output ("PHASE W-2 ms=" + [int]$w.ElapsedMilliseconds)

    $w.Restart()
    $prep = Invoke-Psql ("CREATE ROLE c1987w LOGIN; ALTER ROLE c1987w PASSWORD '" + $secretBefore + "';")
    if ($prep.Exit -ne 0) { Add-Result $false "W-3" "role prepare failed" }
    else {
        $r3 = Invoke-Window ("ALTER ROLE c1987w PASSWORD '" + $secretInside + "';")
        $ok3 = $false
        $detail3 = "exit " + $r3.Exit
        if (-not (Test-Clean $r3.Out)) { $detail3 = "secret appeared" }
        elseif ($r3.Exit -eq 0) {
            $j3 = $r3.Out | ConvertFrom-Json
            $red = @($j3.operations | Where-Object { $_.operation -eq "redacted-utility" })
            $redTop = @($j3.topByCalls | Where-Object { $_.label -eq "redacted-utility" })
            $ok3 = ($red.Count -ge 1 -and [int]$red[0].calls -ge 1) -or ($redTop.Count -ge 1)
            if (-not $ok3) { $detail3 = "redacted-utility missing" }
        }
        Add-Result $ok3 "W-3" $detail3
    }
    $w.Stop()
    Write-Output ("PHASE W-3 ms=" + [int]$w.ElapsedMilliseconds)

    $w.Restart()
    $r4 = Invoke-Window "SELECT pg_stat_statements_reset();"
    $ok4 = $false
    $detail4 = "exit " + $r4.Exit
    if (-not (Test-Clean $r4.Out)) { $detail4 = "secret appeared" }
    elseif ($r4.Exit -eq 4) {
        $j4 = $r4.Out | ConvertFrom-Json
        $ok4 = $j4.partial -eq $true
        if (-not $ok4) { $detail4 = "partial not true" }
    }
    Add-Result $ok4 "W-4" $detail4
    $w.Stop()
    Write-Output ("PHASE W-4 ms=" + [int]$w.ElapsedMilliseconds)

    $ok5 = $false
    $detail5 = "version missing"
    if ($syntaxClean -and $null -ne $versionJson) {
        $major = [int][math]::Floor([double]$versionJson.serverVersionNum / 10000)
        $ok5 = $major -eq 16 -and [string]$versionJson.serverVersion.Length -gt 0 -and [int]$versionJson.snapshotMs -ge 0
        Write-Output ("VERSION " + $versionJson.serverVersion + " snapshotMs=" + $versionJson.snapshotMs)
        if (-not $ok5) { $detail5 = "major=" + $major + " snapshotMs=" + $versionJson.snapshotMs }
    }
    elseif (-not $syntaxClean) { $detail5 = "script syntax" }
    Add-Result $ok5 "W-5" $detail5
}
catch {
    Add-Result $false "harness" $_.Exception.Message
}
finally {
    if ($name -match '^c1987-[a-f0-9]{16}$') {
        $rm = Invoke-Child "docker" ("rm -f " + $name) "" $null
        $null = $rm
    }
    foreach ($signal in $signals) {
        if (Test-Path -LiteralPath $signal) { Remove-Item -LiteralPath $signal -Force }
    }
}

Write-Output ("WITNESS passed=" + $passed + " failed=" + $failed)
if ($failed -eq 0 -and $passed -eq 5) { exit 0 }
exit 1
