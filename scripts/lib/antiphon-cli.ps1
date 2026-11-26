#requires -Version 5.1
# W2-nightly S01: the launcher the Windows PowerShell 5.1 entries (Windmill) use to run antiphon-scripts
# (scripts/lib/Antiphon.Scripts) from this checkout. ASCII-only: parseable under Windows PowerShell 5.1.
#
# Invoke-AntiphonCli -Command <name> -Arguments <string[]> [-ResultFile <path>] [-Verbatim <string[]>] returns the CLI's
# exit code, so an entry ends with: exit (Invoke-AntiphonCli ...). The CLI runs as `dotnet exec <dll> <name> <arguments>` on this
# process's own stdin, stdout and stderr (no PowerShell pipeline in between), in the current file-system location.
# Exit 9 is the launcher's own: the CLI could not be built or started; one line on stderr says why.
#
# The cached launcher (N0, unit 57), shared with scripts/antiphon and scripts/antiphon.cmd (docs/testing-and-build.md, "The
# CLI entry (cached launcher)"): bin-launcher/antiphon-scripts/current names the published build of the CLI's inputs,
# bin-launcher/antiphon-scripts/<identity>/, and the CLI runs from there with ANTIPHON_CLI_CHECK set to this checkout. Before
# any command it checks its inputs' identity and, when they changed, builds and publishes the current one under the build
# lock and runs that instead (scripts/lib/Antiphon.CliLauncher). A published folder is never written again, so the CLI runs
# in place (a nightly-run process may hold its dll for hours; the next build publishes beside it). With nothing published
# yet, the launcher project is built into a fresh bin-launcher/antiphon-scripts/boot-<guid>/ and publishes the CLI; its
# build output goes to that folder's build.log, never to stdout. ANTIPHON_SCRIPTS_DLL names a built dll instead (tests): it
# runs as it is, unchecked (the variable is removed for it), and nothing is built.
#
# Arguments: a launcher translates -Name value to --name value, a switch to a bare --name, and a [string[]] to one
# comma-joined value. An empty (or blank) value is dropped together with the --flag before it: Windows PowerShell
# 5.1 drops an empty native argument, which would hand the flag the next argument; the CLI reads an absent flag as
# its default. Trailing backslashes are trimmed (a drive root keeps its own): in a quoted 5.1 argument a trailing
# backslash escapes the closing quote. -ResultFile removes a stale file at that path and passes --result-file <path>;
# the entry reads the record back (-PassThru). -Verbatim comes last, after the result file, each element one argument as
# given: an empty value and a trailing backslash survive (build-slot.ps1's wrapped command, which must arrive literally).

function ConvertTo-AntiphonCliQuotedArgument {
    # One argument of a Windows command line, quoted as the C runtime (and .NET) parses it back.
    param([string]$Value)
    if ($Value.Length -gt 0 -and $Value.IndexOfAny([char[]]@(' ', "`t", "`n", "`r", "`v", '"')) -lt 0) { return $Value }
    $text = New-Object System.Text.StringBuilder
    [void]$text.Append('"')
    $slashes = 0
    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq [char]92) { $slashes++; continue }
        if ($ch -eq [char]34) {
            [void]$text.Append([char]92, 2 * $slashes + 1)
        } elseif ($slashes -gt 0) {
            [void]$text.Append([char]92, $slashes)
        }
        [void]$text.Append($ch)
        $slashes = 0
    }
    if ($slashes -gt 0) { [void]$text.Append([char]92, 2 * $slashes) }
    [void]$text.Append('"')
    return $text.ToString()
}

function ConvertTo-AntiphonCliArgumentList {
    # The launcher argument rules above: blank values leave with their flag; trailing backslashes are trimmed.
    param([string[]]$Arguments)
    $list = New-Object System.Collections.Generic.List[string]
    $items = @($Arguments)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = [string]$items[$i]
        if ([string]::IsNullOrWhiteSpace($item)) { continue }
        if ($item.StartsWith('--') -and ($i + 1) -lt $items.Count -and [string]::IsNullOrWhiteSpace([string]$items[$i + 1])) {
            $i++
            continue
        }
        $trimmed = $item.TrimEnd([char]92)
        if ($trimmed.Length -gt 0 -and -not $trimmed.EndsWith(':')) { $item = $trimmed }
        $list.Add($item)
    }
    return , $list.ToArray()
}

function Write-AntiphonCliFailure {
    param([string]$Message)
    [Console]::Error.WriteLine('antiphon-cli: ' + ($Message -replace '[\r\n]+', ' '))
}

function Invoke-AntiphonCliProcess {
    # Starts FileName with an already quoted command line. Redirected: returns @{ ExitCode; Output } (stdout then
    # stderr). Otherwise the child shares this process's streams and the result has the exit code only. Environment
    # sets (a string) or removes ($null) variables for the child only; WorkingDirectory defaults to the current location.
    param([string]$FileName, [string]$CommandLine, [switch]$Capture, [string]$WorkingDirectory = '', [hashtable]$Environment = @{})
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FileName
    $start.Arguments = $CommandLine
    $start.UseShellExecute = $false
    $location = $WorkingDirectory
    if ([string]::IsNullOrEmpty($location)) { $location = (Get-Location -PSProvider FileSystem).ProviderPath }
    if (-not [string]::IsNullOrEmpty($location)) { $start.WorkingDirectory = $location }
    foreach ($name in @($Environment.Keys)) {
        $value = $Environment[$name]
        if ($null -eq $value) {
            if ($start.EnvironmentVariables.ContainsKey($name)) { $start.EnvironmentVariables.Remove($name) }
        } else {
            $start.EnvironmentVariables[$name] = [string]$value
        }
    }
    if ($Capture) {
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
    }
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $output = ''
        if ($Capture) {
            $errors = $process.StandardError.ReadToEndAsync()
            $output = $process.StandardOutput.ReadToEnd()
            $output += $errors.Result
        }
        $process.WaitForExit()
        return @{ ExitCode = [int]$process.ExitCode; Output = $output }
    } finally { $process.Dispose() }
}

function Get-AntiphonCliPublished {
    # The published CLI the pointer names: <cache>/<identity>/Antiphon.Scripts.dll, or $null.
    param([string]$Cache)
    try { $name = [IO.File]::ReadAllText([IO.Path]::Combine($Cache, 'current')).Trim() } catch { return $null }
    if ($name -cnotmatch '^[0-9a-f]{24}$') { return $null }
    $dll = [IO.Path]::Combine([IO.Path]::Combine($Cache, $name), 'Antiphon.Scripts.dll')
    if ([IO.File]::Exists($dll)) { return $dll }
    return $null
}

function Install-AntiphonCli {
    # First run: builds scripts/lib/Antiphon.CliLauncher into a fresh boot-<guid> folder and runs its publish, which builds
    # and publishes the CLI under the build lock. $true, or $false after one stderr line (publish writes its own).
    param([string]$Root, [string]$Cache)
    $boot = [IO.Path]::Combine($Cache, 'boot-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($boot)
    $project = [IO.Path]::Combine($Root, 'scripts', 'lib', 'Antiphon.CliLauncher', 'Antiphon.CliLauncher.csproj')
    $log = [IO.Path]::Combine($boot, 'build.log')
    $launcher = [IO.Path]::Combine($boot, 'out', 'Antiphon.CliLauncher.dll')
    $arguments = @('build', $project, '-c', 'Release', '-o', [IO.Path]::Combine($boot, 'out'), '--artifacts-path', [IO.Path]::Combine($boot, 'artifacts'),
        '-nologo', '-v', 'q', '-nodeReuse:false', '-p:UseSharedCompilation=false')
    $line = (@($arguments | ForEach-Object { ConvertTo-AntiphonCliQuotedArgument $_ }) -join ' ')
    try { $build = Invoke-AntiphonCliProcess -FileName 'dotnet' -CommandLine $line -Capture -WorkingDirectory $Root }
    catch {
        Write-AntiphonCliFailure ('cannot start the launcher build (dotnet build): ' + $_.Exception.Message)
        return $false
    }
    [IO.File]::WriteAllText($log, [string]$build.Output)
    if ($build.ExitCode -ne 0 -or -not [IO.File]::Exists($launcher)) {
        Write-AntiphonCliFailure ('the launcher did not build (exit ' + $build.ExitCode + ', log ' + $log + ')')
        return $false
    }
    $line = (@('exec', $launcher, 'publish', $Root) | ForEach-Object { ConvertTo-AntiphonCliQuotedArgument $_ }) -join ' '
    try { $publish = Invoke-AntiphonCliProcess -FileName 'dotnet' -CommandLine $line -WorkingDirectory $Root }
    catch {
        Write-AntiphonCliFailure ('cannot start the launcher (dotnet exec): ' + $_.Exception.Message)
        return $false
    }
    try { [IO.Directory]::Delete($boot, $true) } catch { }
    return ($publish.ExitCode -eq 0)
}

function Invoke-AntiphonCli {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [string[]]$Arguments = @(),
        [string]$ResultFile = '',
        [string[]]$Verbatim = @()
    )
    try {
        $root = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..'))
        $cache = [IO.Path]::Combine([IO.Path]::Combine($root, 'bin-launcher'), 'antiphon-scripts')
        $environment = @{}
        $override = [Environment]::GetEnvironmentVariable('ANTIPHON_SCRIPTS_DLL')
        if (-not [string]::IsNullOrWhiteSpace($override)) {
            $dll = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($override)
            if (-not [IO.File]::Exists($dll)) {
                Write-AntiphonCliFailure ('ANTIPHON_SCRIPTS_DLL names no file: ' + $dll)
                $global:LASTEXITCODE = 9
                return 9
            }
            $environment['ANTIPHON_CLI_CHECK'] = $null
        } else {
            $dll = Get-AntiphonCliPublished -Cache $cache
            if ($null -eq $dll) {
                [void][IO.Directory]::CreateDirectory($cache)
                if (-not (Install-AntiphonCli -Root $root -Cache $cache)) {
                    $global:LASTEXITCODE = 9
                    return 9
                }
                $dll = Get-AntiphonCliPublished -Cache $cache
                if ($null -eq $dll) {
                    Write-AntiphonCliFailure ('no published CLI under ' + $cache + ' after the build')
                    $global:LASTEXITCODE = 9
                    return 9
                }
            }
            $environment['ANTIPHON_CLI_CHECK'] = $root
        }
        $argv = New-Object System.Collections.Generic.List[string]
        $argv.Add('exec')
        $argv.Add($dll)
        $argv.Add($Command)
        foreach ($argument in (ConvertTo-AntiphonCliArgumentList -Arguments $Arguments)) { $argv.Add($argument) }
        if (-not [string]::IsNullOrWhiteSpace($ResultFile)) {
            $result = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultFile)
            # A record left by an earlier run must never be read as this run's.
            if ([IO.File]::Exists($result)) { [IO.File]::Delete($result) }
            $argv.Add('--result-file')
            $argv.Add($result)
        }
        foreach ($argument in @($Verbatim)) { $argv.Add([string]$argument) }
        $line = (@($argv | ForEach-Object { ConvertTo-AntiphonCliQuotedArgument $_ }) -join ' ')
    } catch {
        Write-AntiphonCliFailure ('cannot prepare the CLI: ' + $_.Exception.Message)
        $global:LASTEXITCODE = 9
        return 9
    }
    try { $run = Invoke-AntiphonCliProcess -FileName 'dotnet' -CommandLine $line -Environment $environment }
    catch {
        Write-AntiphonCliFailure ('cannot start the CLI (dotnet exec): ' + $_.Exception.Message)
        $global:LASTEXITCODE = 9
        return 9
    }
    $global:LASTEXITCODE = [int]$run.ExitCode
    return [int]$run.ExitCode
}
