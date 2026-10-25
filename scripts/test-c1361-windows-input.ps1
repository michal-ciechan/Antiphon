#requires -Version 7.0
<#
Offline witness for the Bash raw string in C1361CensusProbes.HostProgram.
Run through build-slot.ps1. No project build, network, Docker or runner calls.
The actual fixture literal is compiled with a Windows-style root after Git
materializes a Windows CRLF checkout. A forced-CRLF control must remain invalid.
-ProbeNativeShell records the separate native-path mechanism without changing
the fixture or treating diagnostic completion as native qualification.
-TrxPath prints the exact rolling failures from an existing native group TRX.
#>
[CmdletBinding()]
param([switch]$ProbeNativeShell, [string]$TrxPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$relative = 'tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs'
$lf = [string][char]10
$crlf = [string][char]13 + $lf
$script:checks = 0

function Assert-Input {
    param([bool]$Condition, [string]$Name)
    $script:checks++
    if (-not $Condition) { throw "FAIL C1361_INPUT $Name" }
}

function Invoke-InputProbe {
    param([string]$Program, [string[]]$Tokens, [AllowNull()][string]$Body)
    $psi = [Diagnostics.ProcessStartInfo]::new($Program)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($token in $Tokens) { [void]$psi.ArgumentList.Add($token) }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if ($Body) { $process.StandardInput.Write($Body) }
        $process.StandardInput.Close()
        # Provisional bound for these inert CLI probes, not a qualification deadline.
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true); $process.WaitForExit()
            throw "C1361InputProbeTimeout program=$Program"
        }
        return [pscustomobject]@{ Exit=$process.ExitCode; Out=$stdout.GetAwaiter().GetResult()
            Error=$stderr.GetAwaiter().GetResult(); ElapsedMs=$timer.ElapsedMilliseconds }
    } finally { $process.Dispose() }
}

function Get-HostBody {
    param([string]$Text)
    $method = $Text.IndexOf('private static string HostProgram(', [StringComparison]::Ordinal)
    if ($method -lt 0) { throw 'HostProgram missing' }
    $start = $Text.IndexOf('$$"""', $method, [StringComparison]::Ordinal)
    $end = $Text.IndexOf('""";', $start + 5, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -lt $start) { throw 'HostProgram raw literal missing' }
    $literal = $Text.Substring($start, $end - $start + 3)
    $name = 'C1361Input_' + [guid]::NewGuid().ToString('N')
    $definition = 'public static class ' + $name + ' { public static string Read() { ' +
        'string root=@"Q:\fixture folder\c1361"; string Project="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"; ' +
        'bool wait=false; return ' + $literal + '; } }'
    $type = Add-Type -TypeDefinition $definition -PassThru
    return $type.GetMethod('Read').Invoke($null, @())
}

if ($TrxPath) {
    [xml]$trx = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $TrxPath).ProviderPath)
    $definitions = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object {
        $_.TestMethod.className -eq 'Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests'
    })
    $rows = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testId -in $definitions.id })
    if ($rows.Count -eq 0) { throw 'C1361NativeTrxRollingRowsMissing' }
    $failed = @($rows | Where-Object outcome -eq Failed)
    Write-Output "C1361_NATIVE_TRX cases=$($rows.Count) failed=$($failed.Count)"
    foreach ($row in $failed) {
        $method = ($definitions | Where-Object id -eq $row.testId).TestMethod.name
        Write-Output "C1361_NATIVE_FAILURE method=$method display=$($row.testName) duration=$($row.duration)"
        # This roster uses offline sentinels. Keep their literal values out of diagnostics.
        foreach ($value in @($row.Output.ErrorInfo.Message, $row.Output.ErrorInfo.StackTrace)) {
            Write-Output ([string]$value -replace '[A-Za-z0-9_]*SENTINEL[A-Za-z0-9_]*', '[redacted-sentinel]')
        }
    }
}

$owned = [IO.Directory]::CreateTempSubdirectory('c1361-input-')
try {
    $checkout = [IO.Directory]::CreateDirectory((Join-Path $owned.FullName 'checkout'))
    $emptyAttributes = Join-Path $owned.FullName 'empty-attributes'
    [IO.File]::WriteAllText($emptyAttributes, '')
    $prefix = $checkout.FullName.Replace('\', '/') + '/'
    $git = Invoke-InputProbe git @('-C', $repo, '-c', 'core.autocrlf=true', '-c', 'core.eol=crlf',
        '-c', "core.attributesFile=$emptyAttributes", 'checkout-index', "--prefix=$prefix", '--', $relative) ''
    if ($git.Exit -ne 0) { throw "Windows checkout failed: $($git.Error)" }
    $source = [IO.File]::ReadAllText((Join-Path $checkout.FullName $relative))
    $body = Get-HostBody $source
    Assert-Input ($body.Contains('Q:\fixture folder\c1361')) 'actual literal retains the Windows-style input'
    $syntax = Invoke-InputProbe bash @('-n', '-s') $body
    Write-Output "C1361_INPUT checkoutCrLf=$($source.Contains($crlf)) bodyCrLf=$($body.Contains($crlf)) syntaxExit=$($syntax.Exit) elapsedMs=$($syntax.ElapsedMs)"
    Assert-Input ($syntax.Exit -eq 0) "Windows-checkout Bash syntax expected exit=0 actual=$($syntax.Exit); $($syntax.Error)"
    Assert-Input (-not $body.Contains([string][char]13)) 'compiled Bash body is LF'
    Assert-Input (-not [IO.File]::ReadAllText((Join-Path $repo $relative)).Contains($crlf)) 'current fixture checkout is LF before a native build'

    $control = Get-HostBody ($source.Replace($crlf, $lf).Replace($lf, $crlf))
    $red = Invoke-InputProbe bash @('-n', '-s') $control
    Assert-Input ($control.Contains($crlf) -and $red.Exit -ne 0 -and $red.Error.Contains('syntax error')) 'forced CRLF control reaches the Bash syntax failure'
    Write-Output "C1361_INPUT control=forced-crlf syntaxExit=$($red.Exit) elapsedMs=$($red.ElapsedMs)"

    if ($ProbeNativeShell) {
        $native = [IO.Directory]::CreateDirectory((Join-Path $owned.FullName 'native folder'))
        [IO.File]::WriteAllText((Join-Path $native.FullName 'marker'), 'inert')
        $quotedRoot = $native.FullName.Replace("'", "'\''")
        $probeBody = "root='$quotedRoot'" + $lf + 'test -f "$root/marker" || exit 23' + $lf +
            "printf '%s\n' C1361_NATIVE_INPUT_OK" + $lf
        foreach ($kind in @('lf', 'crlf')) {
            $file = Join-Path $native.FullName ($kind + '.sh')
            $inputBody = if ($kind -eq 'crlf') { $probeBody.Replace($lf, $crlf) } else { $probeBody }
            [IO.File]::WriteAllText($file, $inputBody, [Text.UTF8Encoding]::new($false))
            $run = Invoke-InputProbe bash @($file) ''
            Write-Output "C1361_NATIVE_PROBE os=$([Environment]::OSVersion.Platform) kind=direct-$kind exit=$($run.Exit) elapsedMs=$($run.ElapsedMs)"
            Write-Output $run.Out
            Write-Output $run.Error
        }
        if ($IsWindows) {
            $wsl = Join-Path ([Environment]::GetFolderPath('System')) 'wsl.exe'
            if (Test-Path -LiteralPath $wsl) {
                $mapped = Invoke-InputProbe $wsl @('-e', 'wslpath', '-u', $native.FullName) ''
                if ($mapped.Exit -eq 0 -and $mapped.Out.Trim().StartsWith('/')) {
                    $linuxRoot = $mapped.Out.Trim().Replace("'", "'\''")
                    $run = Invoke-InputProbe $wsl @('-e', 'bash', '-s') ($probeBody.Replace($quotedRoot, $linuxRoot))
                    Write-Output "C1361_NATIVE_PROBE kind=wsl-mapped-lf exit=$($run.Exit) elapsedMs=$($run.ElapsedMs)"
                    Write-Output $run.Out
                    Write-Output $run.Error
                } else { Write-Output "C1361_NATIVE_PROBE kind=wslpath exit=$($mapped.Exit)" }
            } else { Write-Output 'C1361_NATIVE_PROBE kind=wsl unavailable=true' }
        }
    }
    Write-Output "C1361_INPUT assertions=$script:checks failures=0 nativeQualification=pending"
} finally { $owned.Delete($true) }
