#requires -Version 5.1
# W2-nightly S01: the launcher the Windows PowerShell 5.1 entries (Windmill) use to run antiphon-scripts
# (scripts/lib/Antiphon.Scripts) from this checkout. ASCII-only: parseable under Windows PowerShell 5.1.
#
# Invoke-AntiphonCli -Command <name> -Arguments <string[]> [-ResultFile <path>] returns the CLI's exit code, so an
# entry ends with: exit (Invoke-AntiphonCli ...). The CLI runs as `dotnet exec <dll> <name> <arguments>` on this
# process's own stdin, stdout and stderr (no PowerShell pipeline in between), in the current file-system location.
# Exit 9 is the launcher's own: the build failed or the CLI could not be started; one line on stderr says why.
#
# Build: the msbuild line of scripts/antiphon into <checkout>/bin-launcher/antiphon-scripts, one build at a time
# (bin-launcher/build.lock); its output goes to bin-launcher/build.log, never to stdout. A no-op when nothing changed.
# Run: from a copy of the build output, bin-launcher/run-<hash>/ (hash of the output's names and bytes), never in
# place: a nightly-run process holds its dll for hours on Windows, and the next build must still write the output.
# A copy unused for 7 days is removed when a new one is made. ANTIPHON_SCRIPTS_DLL names a built dll instead
# (tests): nothing is built, and the dll's directory is copied the same way.
#
# Arguments: a launcher translates -Name value to --name value, a switch to a bare --name, and a [string[]] to one
# comma-joined value. An empty (or blank) value is dropped together with the --flag before it: Windows PowerShell
# 5.1 drops an empty native argument, which would hand the flag the next argument; the CLI reads an absent flag as
# its default. Trailing backslashes are trimmed (a drive root keeps its own): in a quoted 5.1 argument a trailing
# backslash escapes the closing quote. -ResultFile removes a stale file at that path and passes --result-file <path>;
# the entry reads the record back (-PassThru).

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

function Get-AntiphonCliOutputHash {
    # SHA-256 over the directory's relative file names and bytes (ordinal order), 24 hex characters.
    param([string]$Directory)
    $root = $Directory.TrimEnd([char]92, [char]47)
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($file in [IO.Directory]::GetFiles($root, '*', [IO.SearchOption]::AllDirectories)) {
        $names.Add($file.Substring($root.Length + 1).Replace([char]92, [char]47))
    }
    $names.Sort([StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = New-Object System.IO.MemoryStream
        foreach ($name in $names) {
            $header = [Text.Encoding]::UTF8.GetBytes($name + "`n")
            $stream.Write($header, 0, $header.Length)
            $bytes = [IO.File]::ReadAllBytes([IO.Path]::Combine($root, $name))
            $stream.Write($bytes, 0, $bytes.Length)
        }
        $hash = $sha.ComputeHash($stream.ToArray())
    } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($hash).Replace('-', '').Substring(0, 24).ToLowerInvariant())
}

function Copy-AntiphonCliDirectory {
    param([string]$Source, [string]$Target)
    $root = $Source.TrimEnd([char]92, [char]47)
    [void][IO.Directory]::CreateDirectory($Target)
    foreach ($directory in [IO.Directory]::GetDirectories($root, '*', [IO.SearchOption]::AllDirectories)) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::Combine($Target, $directory.Substring($root.Length + 1)))
    }
    foreach ($file in [IO.Directory]::GetFiles($root, '*', [IO.SearchOption]::AllDirectories)) {
        [IO.File]::Copy($file, [IO.Path]::Combine($Target, $file.Substring($root.Length + 1)), $true)
    }
}

function Remove-AntiphonCliStaleCopies {
    # Best effort. A copy still in use cannot be renamed on Windows, so it stays; a renamed one is deleted.
    param([string]$LauncherRoot, [string]$Keep)
    $cutoff = [DateTime]::UtcNow.AddDays(-7)
    foreach ($directory in [IO.Directory]::GetDirectories($LauncherRoot)) {
        $name = [IO.Path]::GetFileName($directory)
        if ($directory -eq $Keep) { continue }
        if (-not ($name.StartsWith('run-') -or $name.StartsWith('tmp-') -or $name.StartsWith('trash-'))) { continue }
        try {
            if (-not $name.StartsWith('trash-')) {
                if ([IO.Directory]::GetLastWriteTimeUtc($directory) -gt $cutoff) { continue }
                $trash = [IO.Path]::Combine($LauncherRoot, 'trash-' + [guid]::NewGuid().ToString('N'))
                [IO.Directory]::Move($directory, $trash)
                $directory = $trash
            }
            [IO.Directory]::Delete($directory, $true)
        } catch { }
    }
}

function Get-AntiphonCliRunCopy {
    # The run copy of the directory that holds DllPath: bin-launcher/run-<hash>/<dll name>. $null after one stderr line.
    param([string]$LauncherRoot, [string]$DllPath)
    $source = [IO.Path]::GetDirectoryName($DllPath)
    $hash = Get-AntiphonCliOutputHash -Directory $source
    $run = [IO.Path]::Combine($LauncherRoot, 'run-' + $hash)
    $dll = [IO.Path]::Combine($run, [IO.Path]::GetFileName($DllPath))
    if (-not [IO.File]::Exists($dll)) {
        # A whole copy appears under its final name at once; when another launcher wins the rename, its copy is used.
        $temporary = [IO.Path]::Combine($LauncherRoot, 'tmp-' + [guid]::NewGuid().ToString('N'))
        Copy-AntiphonCliDirectory -Source $source -Target $temporary
        try { [IO.Directory]::Move($temporary, $run) }
        catch { try { [IO.Directory]::Delete($temporary, $true) } catch { } }
        if (-not [IO.File]::Exists($dll)) {
            Write-AntiphonCliFailure ('the run copy ' + $run + ' has no ' + [IO.Path]::GetFileName($DllPath))
            return $null
        }
        Remove-AntiphonCliStaleCopies -LauncherRoot $LauncherRoot -Keep $run
    }
    # In use now: the 7-day sweep goes by this time.
    try { [IO.Directory]::SetLastWriteTimeUtc($run, [DateTime]::UtcNow) } catch { }
    return $dll
}

function Invoke-AntiphonCliProcess {
    # Starts FileName with an already quoted command line. Redirected: returns @{ ExitCode; Output } (stdout then
    # stderr). Otherwise the child shares this process's streams and the result has the exit code only.
    param([string]$FileName, [string]$CommandLine, [switch]$Capture)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FileName
    $start.Arguments = $CommandLine
    $start.UseShellExecute = $false
    $location = (Get-Location -PSProvider FileSystem).ProviderPath
    if (-not [string]::IsNullOrEmpty($location)) { $start.WorkingDirectory = $location }
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

function Build-AntiphonCli {
    # The scripts/antiphon build under bin-launcher/build.lock. Returns the built dll, or $null after one stderr line.
    param([string]$Root, [string]$LauncherRoot)
    # Forward slashes, as scripts/antiphon.cmd passes them: one ArtifactsPath string keeps one restore.
    $slashRoot = $Root.Replace([char]92, [char]47)
    $project = $slashRoot + '/scripts/lib/Antiphon.Scripts'
    $artifacts = $slashRoot + '/bin-launcher/antiphon-scripts'
    $built = [IO.Path]::Combine($LauncherRoot, 'antiphon-scripts', 'bin', 'Antiphon.Scripts', 'release', 'Antiphon.Scripts.dll')
    $log = [IO.Path]::Combine($LauncherRoot, 'build.log')
    $lockPath = [IO.Path]::Combine($LauncherRoot, 'build.lock')
    $lock = $null
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while ($null -eq $lock) {
        try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -gt $deadline) {
                Write-AntiphonCliFailure ('build lock ' + $lockPath + ' still held after 10 minutes')
                return $null
            }
            Start-Sleep -Milliseconds 200
        }
    }
    try {
        $arguments = @('msbuild', $project, '-restore', '-p:Configuration=Release', ('-p:ArtifactsPath=' + $artifacts), '-nologo', '-v:q',
            '-tl:off', '-clp:NoSummary', '-nodeReuse:false', '-p:UseSharedCompilation=false')
        $line = (@($arguments | ForEach-Object { ConvertTo-AntiphonCliQuotedArgument $_ }) -join ' ')
        try { $build = Invoke-AntiphonCliProcess -FileName 'dotnet' -CommandLine $line -Capture }
        catch {
            Write-AntiphonCliFailure ('cannot start the build (dotnet msbuild): ' + $_.Exception.Message)
            return $null
        }
        [IO.File]::WriteAllText($log, [string]$build.Output)
        if ($build.ExitCode -ne 0 -or -not [IO.File]::Exists($built)) {
            $first = @(([string]$build.Output) -split "`r?`n" | Where-Object { $_ -match 'error' } | Select-Object -First 1)
            $detail = ''
            if ($first.Count -gt 0) { $detail = ': ' + $first[0].Trim() }
            Write-AntiphonCliFailure ('build failed (exit ' + $build.ExitCode + ', log ' + $log + ')' + $detail)
            return $null
        }
        return (Get-AntiphonCliRunCopy -LauncherRoot $LauncherRoot -DllPath $built)
    } finally { $lock.Dispose() }
}

function Invoke-AntiphonCli {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [string[]]$Arguments = @(),
        [string]$ResultFile = ''
    )
    try {
        $root = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..'))
        $launcherRoot = [IO.Path]::Combine($root, 'bin-launcher')
        [void][IO.Directory]::CreateDirectory($launcherRoot)
        $override = [Environment]::GetEnvironmentVariable('ANTIPHON_SCRIPTS_DLL')
        if (-not [string]::IsNullOrWhiteSpace($override)) {
            $source = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($override)
            if (-not [IO.File]::Exists($source)) {
                Write-AntiphonCliFailure ('ANTIPHON_SCRIPTS_DLL names no file: ' + $source)
                $global:LASTEXITCODE = 9
                return 9
            }
            $dll = Get-AntiphonCliRunCopy -LauncherRoot $launcherRoot -DllPath $source
        } else {
            $dll = Build-AntiphonCli -Root $root -LauncherRoot $launcherRoot
        }
        if ($null -eq $dll) {
            $global:LASTEXITCODE = 9
            return 9
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
        $line = (@($argv | ForEach-Object { ConvertTo-AntiphonCliQuotedArgument $_ }) -join ' ')
    } catch {
        Write-AntiphonCliFailure ('cannot prepare the CLI: ' + $_.Exception.Message)
        $global:LASTEXITCODE = 9
        return 9
    }
    try { $run = Invoke-AntiphonCliProcess -FileName 'dotnet' -CommandLine $line }
    catch {
        Write-AntiphonCliFailure ('cannot start the CLI (dotnet exec): ' + $_.Exception.Message)
        $global:LASTEXITCODE = 9
        return 9
    }
    $global:LASTEXITCODE = [int]$run.ExitCode
    return [int]$run.ExitCode
}
