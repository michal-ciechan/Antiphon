#requires -Version 7.0
# Qualification timing retention. ASCII-only.
# A blank timing root, a nightly chunk, a chunk with no TRX, and a timed-out
# chunk must all leave a sidecar and a summary table.
param(
    [ValidateSet('', 'CaptureShell')]
    [string]$Case = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0

function Assert-Check {
    param([bool]$Condition, [string]$Name)
    if ($Condition) {
        Write-Host ('PASS ' + $Name)
        return
    }
    Write-Host ('FAIL ' + $Name)
    $script:failed++
}

function Invoke-CaptureShellWitness {
    $source = [IO.File]::ReadAllText($PSCommandPath)
    $hardCodedShell = '$psi.FileName = ' + '"/bin/sh"'
    $hardCodedHits = ([regex]::Matches($source, [regex]::Escape($hardCodedShell))).Count
    Assert-Check ($hardCodedHits -eq 1) 'capture-shell-source-uses-resolver'
    Assert-Check ($source.Contains('SKIP capture-identity shell-absent: no sh.exe on PATH and no Git for Windows sh.exe beside git')) 'capture-shell-absent-reason-present'
    $missing = $false
    if (-not (Get-Command Resolve-QualificationCaptureShell -ErrorAction SilentlyContinue)) {
        Write-Host 'FAIL capture-shell-resolver-missing'
        $script:failed++
        $missing = $true
    }
    if (-not (Get-Command New-QualificationCaptureSeam -ErrorAction SilentlyContinue)) {
        Write-Host 'FAIL capture-shell-seam-helper-missing'
        $script:failed++
        $missing = $true
    }
    if ($missing) { return }

    $root = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-sh-' + [guid]::NewGuid().ToString('N'))
    $gitCmd = Join-Path $root 'Program Files/Git/cmd'
    $gitUsr = Join-Path $root 'Program Files/Git/usr/bin'
    $direct = Join-Path $root 'direct'
    $empty = Join-Path $root 'empty'
    $probeRoot = Join-Path $root 'probe'
    $probeDir = Join-Path $probeRoot 'cap'
    New-Item -ItemType Directory -Path $gitCmd, $gitUsr, $direct, $empty, $probeDir -Force | Out-Null
    $plant = Join-Path $gitUsr 'sh.exe'
    $plantBody = "#!/bin/sh`nprintf '%s\n' `"`$0`" > `"`$QTIMING_CAPTURE_SHELL_MARKER`"`nexec /bin/sh `"`$@`"`n"
    [IO.File]::WriteAllText($plant, $plantBody)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        [IO.File]::SetUnixFileMode($plant, [IO.UnixFileMode]'UserRead,UserWrite,UserExecute,GroupRead,GroupExecute,OtherRead,OtherExecute')
    }
    [IO.File]::WriteAllText((Join-Path $gitCmd 'git.exe'), 'git')
    [IO.File]::WriteAllText((Join-Path $direct 'sh.exe'), 'direct')
    $marker = Join-Path $root 'marker'
    try {
        $beside = Resolve-QualificationCaptureShell -Platform 'windows' -PathValue $gitCmd
        $besideText = [string]$beside
        $besideOk = (-not [string]::IsNullOrWhiteSpace($besideText)) -and ($besideText -ne '/bin/sh') -and ($besideText.Replace('\', '/') -eq (([IO.Path]::GetFullPath($plant)).Replace('\', '/')))
        Assert-Check $besideOk 'capture-shell-resolver-windows-shape'
        $directShell = Resolve-QualificationCaptureShell -Platform 'windows' -PathValue $direct
        $directOk = ([string]$directShell).Replace('\', '/') -eq (([IO.Path]::GetFullPath((Join-Path $direct 'sh.exe'))).Replace('\', '/'))
        Assert-Check $directOk 'capture-shell-resolver-path-sh-exe'
        $onlyBin = Resolve-QualificationCaptureShell -Platform 'windows' -PathValue '/bin'
        $binRejected = [string]::IsNullOrWhiteSpace([string]$onlyBin)
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            $binRejected = $binRejected -and (Test-Path -LiteralPath '/bin/sh' -PathType Leaf)
        }
        Assert-Check $binRejected 'capture-shell-resolver-skips-bin-sh'
        $absent = Resolve-QualificationCaptureShell -Platform 'windows' -PathValue $empty
        Assert-Check ([string]::IsNullOrWhiteSpace([string]$absent)) 'capture-shell-absent-skips'
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            Write-Host 'SKIP capture-shell-seam-start linux-planted-shell'
            return
        }
        if (-not $besideOk) { return }
        $seamPath = Join-Path $probeRoot 'seam.ps1'
        New-QualificationCaptureSeam -Path $seamPath -Shell $besideText
        $seamText = [IO.File]::ReadAllText($seamPath)
        $seamOk = $seamText.Contains($besideText) -and (-not $seamText.Contains('$psi.FileName = "/bin/sh"'))
        Assert-Check $seamOk 'capture-shell-seam-embeds-resolved-shell'
        $env:QTIMING_CAPTURE_DIR = $probeDir
        $env:QTIMING_CHILD_EXIT = '0'
        $env:QTIMING_FORWARD = '1'
        $env:QTIMING_CAPTURE_SHELL_MARKER = $marker
        . $seamPath
        $null = & $NightlySeams.StartProcess 'dotnet' @('--report-trx') $probeRoot 10000 @{
            ANTIPHON_TEST_PHASE_ROOT = $probeRoot
            QTIMING_CAPTURE_SHELL_MARKER = $marker
        }
        $markerText = ''
        if (Test-Path -LiteralPath $marker) { $markerText = ([IO.File]::ReadAllText($marker)).Trim() }
        $stdoutPath = Join-Path $probeDir 'stdout-0'
        $stderrPath = Join-Path $probeDir 'stderr-0'
        $exitPath = Join-Path $probeDir 'exit-0'
        $stdout = ''
        $stderr = ''
        $exitText = ''
        if (Test-Path -LiteralPath $stdoutPath) { $stdout = [IO.File]::ReadAllText($stdoutPath) }
        if (Test-Path -LiteralPath $stderrPath) { $stderr = [IO.File]::ReadAllText($stderrPath) }
        if (Test-Path -LiteralPath $exitPath) { $exitText = ([IO.File]::ReadAllText($exitPath)).Trim() }
        $seen = @(Get-ChildItem -LiteralPath $probeDir -Filter 'seen-*' -File -ErrorAction SilentlyContinue)
        $started = ($markerText -eq $besideText) -and ($stdout -eq 'QUAL-OUT') -and ($stderr -eq 'QUAL-ERR') -and ($exitText -eq '0') -and (@($seen).Count -ge 1)
        Assert-Check $started 'capture-shell-seam-starts-resolved-shell'
        $phaseNames = @(
            'ANTIPHON_TEST_PHASE_ROOT',
            'ANTIPHON_TEST_PHASE_RUN_ID',
            'ANTIPHON_TEST_PHASE_GROUP_ID',
            'ANTIPHON_TEST_PHASE_ATTEMPT',
            'ANTIPHON_TEST_PHASE_SUITE'
        )
        $savedPhase = @{}
        foreach ($phaseName in $phaseNames) {
            $savedPhase[$phaseName] = [Environment]::GetEnvironmentVariable($phaseName)
            [Environment]::SetEnvironmentVariable($phaseName, ('inherited-' + $phaseName))
        }
        try {
            $env:QTIMING_FORWARD = '0'
            $env:QTIMING_ENV_WITNESS = '1'
            $env:QTIMING_CHILD_EXIT = '0'
            $null = & $NightlySeams.StartProcess 'dotnet' @('--report-trx') $probeRoot 10000 @{
                ANTIPHON_TEST_PHASE_ROOT = 'forwarded-phase'
                QTIMING_KEEP = 'kept-value'
            }
            $envDump = @(Get-ChildItem -LiteralPath $probeDir -Filter 'env-*' -File -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1)
            $dumpText = ''
            if ($envDump.Count -ge 1) { $dumpText = [IO.File]::ReadAllText($envDump[-1].FullName) }
            $names = New-Object System.Collections.Generic.List[string]
            $values = @{}
            foreach ($row in ($dumpText -split "`n")) {
                $trim = $row.TrimEnd("`r")
                $eq = $trim.IndexOf('=')
                if ($eq -gt 0) {
                    $keyName = $trim.Substring(0, $eq)
                    $names.Add($keyName)
                    $values[$keyName] = $trim.Substring($eq + 1)
                }
            }
            $phaseHits = @($names | Where-Object { $_.StartsWith('ANTIPHON_TEST_PHASE_', [StringComparison]::Ordinal) })
            $pathValue = ''
            foreach ($keyName in $names) {
                if ($keyName -ceq 'PATH' -or $keyName -ceq 'Path') { $pathValue = [string]$values[$keyName] }
            }
            $keepValue = ''
            if ($values.ContainsKey('QTIMING_KEEP')) { $keepValue = [string]$values['QTIMING_KEEP'] }
            $kept = (-not [string]::IsNullOrWhiteSpace($pathValue)) -and ($keepValue -eq 'kept-value') -and ($phaseHits.Count -eq 0)
            if (-not $kept) {
                Write-Host ('DIAG capture-seam-off count=' + $names.Count + ' path=' + $(if ([string]::IsNullOrWhiteSpace($pathValue)) { '0' } else { '1' }) + ' keep=' + $(if ($keepValue -eq 'kept-value') { '1' } else { '0' }) + ' phase=' + $phaseHits.Count)
            }
            Assert-Check $kept 'capture-seam-off-keeps-non-phase'
        } finally {
            foreach ($phaseName in $phaseNames) {
                [Environment]::SetEnvironmentVariable($phaseName, $savedPhase[$phaseName])
            }
            Remove-Item Env:QTIMING_ENV_WITNESS -ErrorAction SilentlyContinue
        }
    } catch {
        Write-Host ('FAIL capture-shell-witness ' + $_.Exception.Message)
        $script:failed++
    } finally {
        Remove-Item Env:QTIMING_CAPTURE_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:QTIMING_CHILD_EXIT -ErrorAction SilentlyContinue
        Remove-Item Env:QTIMING_FORWARD -ErrorAction SilentlyContinue
        Remove-Item Env:QTIMING_ENV_WITNESS -ErrorAction SilentlyContinue
        Remove-Item Env:QTIMING_CAPTURE_SHELL_MARKER -ErrorAction SilentlyContinue
        Remove-Variable -Name NightlySeams -Scope Script -ErrorAction SilentlyContinue
        Remove-Variable -Name NightlySeams -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Resolve-QualificationCaptureLink {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    try {
        $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if ($item.LinkType -and $item.Target) {
            $target = [string]@($item.Target)[0]
            if (-not [IO.Path]::IsPathRooted($target)) {
                $target = Join-Path (Split-Path -Parent $full) $target
            }
            $resolved = [IO.Path]::GetFullPath($target)
            if (Test-Path -LiteralPath $resolved -PathType Leaf) { return $resolved }
        }
    } catch { }
    return $full
}

function Resolve-QualificationCaptureShell {
    param(
        [string]$Platform = '',
        [string]$PathValue = ''
    )
    if ([string]::IsNullOrWhiteSpace($Platform)) {
        if (-not [string]::IsNullOrWhiteSpace($env:QTIMING_CAPTURE_SHELL_PLATFORM)) {
            $Platform = [string]$env:QTIMING_CAPTURE_SHELL_PLATFORM
        } elseif (($env:OS -eq 'Windows_NT') -or ($IsWindows -eq $true)) {
            $Platform = 'windows'
        } else {
            $Platform = 'linux'
        }
    }
    $Platform = $Platform.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        if (-not [string]::IsNullOrWhiteSpace($env:QTIMING_CAPTURE_SHELL_PATH)) {
            $PathValue = [string]$env:QTIMING_CAPTURE_SHELL_PATH
        } else {
            $PathValue = [string]$env:PATH
        }
    }
    # Linux keeps the literal /bin/sh. A Windows-shaped witness sets Platform
    # to windows and must not select that path.
    if ($Platform -eq 'linux' -and (Test-Path -LiteralPath '/bin/sh' -PathType Leaf)) {
        return '/bin/sh'
    }
    $separator = [string][IO.Path]::PathSeparator
    $dirs = @($PathValue -split [regex]::Escape($separator) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($dir in $dirs) {
        $candidate = Join-Path $dir 'sh.exe'
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $full = [IO.Path]::GetFullPath($candidate)
        if ($Platform -eq 'windows' -and $full -eq '/bin/sh') { continue }
        return $full
    }
    foreach ($dir in $dirs) {
        foreach ($name in @('git.exe', 'git', 'sh.exe', 'sh')) {
            $candidate = Join-Path $dir $name
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
            $full = [IO.Path]::GetFullPath($candidate)
            if ($Platform -eq 'windows' -and $full -eq '/bin/sh') { continue }
            $linked = Resolve-QualificationCaptureLink $full
            $installation = Split-Path -Parent (Split-Path -Parent $linked)
            if ([string]::IsNullOrWhiteSpace($installation)) { continue }
            foreach ($relative in @('usr/bin/sh.exe', 'bin/sh.exe')) {
                $shell = $installation
                foreach ($part in ($relative -split '/')) { $shell = Join-Path $shell $part }
                if (-not (Test-Path -LiteralPath $shell -PathType Leaf)) { continue }
                $shellFull = [IO.Path]::GetFullPath($shell)
                if ($Platform -eq 'windows' -and $shellFull -eq '/bin/sh') { continue }
                return $shellFull
            }
        }
    }
    if ($Platform -ne 'windows') {
        foreach ($dir in $dirs) {
            $candidate = Join-Path $dir 'sh'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return [IO.Path]::GetFullPath($candidate)
            }
        }
    }
    return $null
}

function New-QualificationCaptureSeam {
    param([string]$Path, [string]$Shell)
    $literal = ([string]$Shell).Replace('"', '`"')
    $body = @'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = "test"; detail = "seam" } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join " "
        if ($argsText -notmatch "--report-trx" -or $argsText -match "--list-tests") {
            return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        $dir = [string]$env:QTIMING_CAPTURE_DIR
        $seqPath = Join-Path $dir "seq"
        $seq = 0
        if (Test-Path -LiteralPath $seqPath) { $seq = [int]([IO.File]::ReadAllText($seqPath)) }
        [IO.File]::WriteAllText($seqPath, ([string]($seq + 1)))
        $seen = Join-Path $dir ("seen-" + $seq)
        $code = [int]$env:QTIMING_CHILD_EXIT
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "QUALIFICATION_CAPTURE_SHELL"
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        [void]$psi.ArgumentList.Add("-c")
        # The dump path is embedded in the command. A variable would be removed by the
        # strip under test, and /usr/bin/env does not need PATH.
        $shellCommand = 'printf QUAL-OUT; printf QUAL-ERR 1>&2; if [ -n "$ANTIPHON_TEST_PHASE_ROOT" ]; then printf yes > "$QTIMING_SEEN"; fi;'
        if ($env:QTIMING_ENV_WITNESS -eq "1") {
            $dumpLiteral = Join-Path $dir ("env-" + $seq)
            if ($dumpLiteral -match '^[A-Za-z]:\\') {
                $dumpDrive = $dumpLiteral.Substring(0, 1).ToLowerInvariant()
                $dumpTail = $dumpLiteral.Substring(2) -replace '\\', '/'
                $dumpLiteral = "/" + $dumpDrive + $dumpTail
            }
            $shellCommand = $shellCommand + " /usr/bin/env > '" + $dumpLiteral + "';"
        }
        $shellCommand = $shellCommand + " exit " + $code
        [void]$psi.ArgumentList.Add($shellCommand)
        if ($Environment) {
            foreach ($key in @($Environment.Keys)) {
                $name = [string]$key
                if ($env:QTIMING_FORWARD -ne "1" -and $name.StartsWith("ANTIPHON_TEST_PHASE_", [StringComparison]::Ordinal)) { continue }
                $psi.EnvironmentVariables[$name] = [string]$Environment[$key]
            }
        }
        if ($env:QTIMING_FORWARD -ne "1") {
            foreach ($name in @($psi.EnvironmentVariables.Keys)) {
                if (([string]$name).StartsWith("ANTIPHON_TEST_PHASE_", [StringComparison]::Ordinal)) {
                    [void]$psi.EnvironmentVariables.Remove([string]$name)
                }
            }
        }
        $seenForShell = $seen
        if ($seenForShell -match '^[A-Za-z]:\\') {
            $drive = $seenForShell.Substring(0, 1).ToLowerInvariant()
            $tail = $seenForShell.Substring(2) -replace '\\', '/'
            $seenForShell = '/' + $drive + $tail
        }
        $psi.EnvironmentVariables["QTIMING_SEEN"] = $seenForShell
        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi
        [void]$proc.Start()
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
        [void]$proc.WaitForExit()
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText((Join-Path $dir ("stdout-" + $seq)), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $dir ("stderr-" + $seq)), $stderr, $utf8)
        [IO.File]::WriteAllText((Join-Path $dir ("exit-" + $seq)), ([string]$proc.ExitCode))
        return @{ ExitCode = [int]$proc.ExitCode; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = $proc.Id; LogText = ($stdout + $stderr) }
    }
}
'@
    [IO.File]::WriteAllText($Path, $body.Replace('QUALIFICATION_CAPTURE_SHELL', $literal))
}

if ($Case -eq 'CaptureShell') {
    Invoke-CaptureShellWitness
    if ($failed -gt 0) {
        Write-Host ('FAILED ' + $failed)
        exit 1
    }
    Write-Host 'OK'
    exit 0
}

function Initialize-HarnessFifo {
    if ('AntiphonHarnessPosix' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class AntiphonHarnessPosix {
    [DllImport("libc", SetLastError = true)]
    public static extern int mkfifo(string pathname, uint mode);
}
'@
}

function New-HarnessFifo {
    param([string]$Path)
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return $false }
    Initialize-HarnessFifo
    $code = [AntiphonHarnessPosix]::mkfifo($Path, [uint32]438)
    return ($code -eq 0)
}

function Set-HarnessUnixMode {
    param([string]$Path, [string]$Octal)
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { throw 'unix mode is not a Windows property' }
    $bits = [Convert]::ToInt32($Octal, 8)
    [System.IO.File]::SetUnixFileMode($Path, [System.IO.UnixFileMode]$bits)
}

function New-HarnessSymlink {
    param([string]$Link, [string]$Target)
    try {
        New-Item -ItemType SymbolicLink -Path $Link -Target $Target -ErrorAction Stop | Out-Null
        return $true
    } catch {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $item = Get-Item -LiteralPath $Target -Force -ErrorAction SilentlyContinue
            if ($item -and $item.PSIsContainer) {
                New-Item -ItemType Junction -Path $Link -Target $Target -ErrorAction Stop | Out-Null
                return $true
            }
        }
        return $false
    }
}

function Get-NetFxAddTypeFindings {
    param([string[]]$Paths)
    $rules = @(
        @{ Name = 'ReadOnlySpan'; Pattern = '\bReadOnlySpan\b' },
        @{ Name = 'Span'; Pattern = '\bSpan<' },
        @{ Name = 'Memory'; Pattern = '\bReadOnlyMemory\b|\bMemory<' },
        @{ Name = 'stackalloc'; Pattern = '\bstackalloc\b' },
        @{ Name = 'is-not'; Pattern = '\bis\s+not\b' },
        @{ Name = 'switch-expression'; Pattern = '\bswitch\s*\{|\bswitch\s+[^\s(]' },
        @{ Name = 'ValueTuple'; Pattern = '\bValueTuple\b' },
        @{ Name = 'tuple-type'; Pattern = '\(\s*(?:bool|byte|char|int|long|float|double|decimal|string|object|var)\s*,' },
        @{ Name = 'Contains-char'; Pattern = '\.Contains\(\s*''' },
        @{ Name = 'ArrayPool'; Pattern = '\bArrayPool\b' },
        @{ Name = 'interpolation'; Pattern = '\$"' },
        @{ Name = 'null-conditional'; Pattern = '\?\.' },
        @{ Name = 'nameof'; Pattern = '\bnameof\s*\(' },
        @{ Name = 'out-var'; Pattern = '\bout\s+var\b' },
        @{ Name = 'TimeSpan.TryParse-provider'; Pattern = 'TimeSpan\s*\.\s*TryParse\s*\([^;\n]*,[^;\n]*,' }
    )
    $single = [regex]::new("(?s)Add-Type(?:\s+-Language\s+\w+)?\s+-TypeDefinition\s+@'\r?\n(.*?)\r?\n'@")
    $double = [regex]::new("(?s)Add-Type(?:\s+-Language\s+\w+)?\s+-TypeDefinition\s+@`"\r?\n(.*?)\r?\n`"@")
    $findings = New-Object System.Collections.Generic.List[string]
    foreach ($path in @($Paths)) {
        if (-not (Test-Path -LiteralPath $path)) {
            $findings.Add($path + ':missing')
            continue
        }
        $text = [IO.File]::ReadAllText($path)
        $blocks = @()
        # A .cs file that PowerShell 5.1 compiles with Add-Type -Path is one block.
        if ($path -like '*.cs') {
            if ([string]::IsNullOrWhiteSpace($text)) { $findings.Add($path + ':empty-source') }
            $blocks += $text
        }
        foreach ($match in $single.Matches($text)) { $blocks += [string]$match.Groups[1].Value }
        foreach ($match in $double.Matches($text)) { $blocks += [string]$match.Groups[1].Value }
        foreach ($block in $blocks) {
            $source = [regex]::Replace($block, '(?s)/\*.*?\*/', ' ')
            $source = [regex]::Replace($source, '(?m)//.*?$', ' ')
            foreach ($rule in $rules) {
                if ([regex]::IsMatch($source, [string]$rule.Pattern)) {
                    $findings.Add(([IO.Path]::GetFileName($path) + ':' + [string]$rule.Name))
                }
            }
        }
    }
    return @($findings)
}

function Invoke-QualificationWitness {
    param([string]$ScriptPath, [string[]]$Arguments, [int]$TimeoutMilliseconds)
    $exe = Join-Path $PSHOME 'pwsh'
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    [void]$start.ArgumentList.Add('-NoProfile')
    [void]$start.ArgumentList.Add('-File')
    [void]$start.ArgumentList.Add($ScriptPath)
    foreach ($argument in @($Arguments)) { [void]$start.ArgumentList.Add([string]$argument) }
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo = $start
    $watch = [Diagnostics.Stopwatch]::StartNew()
    [void]$proc.Start()
    $exited = $proc.WaitForExit($TimeoutMilliseconds)
    $watch.Stop()
    $stdout = ''
    $stderr = ''
    if ($exited) {
        try { $stdout = [string]$proc.StandardOutput.ReadToEnd() } catch { $stdout = '' }
        try { $stderr = [string]$proc.StandardError.ReadToEnd() } catch { $stderr = '' }
    } else {
        try { $proc.Kill($true) } catch { }
        try { [void]$proc.WaitForExit(5000) } catch { }
    }
    $code = 1
    if ($exited) {
        try { $code = [int]$proc.ExitCode } catch { $code = 1 }
    }
    try { $proc.Dispose() } catch { }
    return [pscustomobject]@{
        Exited = [bool]$exited
        ExitCode = $code
        Seconds = $watch.Elapsed.TotalSeconds
        StdOut = $stdout
        StdErr = $stderr
    }
}

$repo = Split-Path -Parent $PSScriptRoot
$lib = Join-Path $repo 'scripts/lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'release-selection.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')

Assert-Check ([bool](Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue)) 'resolver-exists'
Assert-Check ([bool]('Antiphon.Nightly.QualificationTiming' -as [type])) 'chunk-timing-core-exists'

# Windows PowerShell 5.1 compiles every Add-Type here-string with the legacy
# csc (.NET Framework, C# 5). This host has no powershell.exe, so the Linux
# witness is the source scan. The desktop row below is pending native.
# test-phase-paths.ps1 (release-cut.ps1's timing root under 5.1) compiles the Linux identity from these files there;
# the evidence itself is compiled (W2-nightly S07-S09) and test-phase-evidence.ps1 compiles nothing.
$ps51Paths = @(
    (Join-Path $lib 'test-phase-paths.ps1'),
    (Join-Path $lib 'test-phase-evidence.ps1'),
    (Join-Path $lib 'Antiphon.Nightly/TestPhaseStreamReaders.cs'),
    (Join-Path $lib 'Antiphon.Nightly/TestPhaseLinuxIdentity.cs'),
    $PSCommandPath
)
$ps51Findings = @(Get-NetFxAddTypeFindings -Paths $ps51Paths)
foreach ($ps51Hit in $ps51Findings) { Write-Host ('PS51 ' + $ps51Hit) }
Assert-Check ($ps51Findings.Count -eq 0) 'ps51-add-type-netfx-clean'
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    $ps51Exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $ps51Exe)) {
        Write-Host 'SKIP ps51-powershell-exe-load-pending-native pending-native: powershell.exe missing'
    } else {
        $ps51Root = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-ps51-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $ps51Root -Force | Out-Null
        $ps51Child = Join-Path $ps51Root 'load.ps1'
        @'
param([string]$Lib)
$ErrorActionPreference = "Stop"
. $Lib
if (-not (Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue)) { exit 2 }
if (-not (Get-Command Get-TestPhasePathIdentity -ErrorAction SilentlyContinue)) { exit 3 }
exit 0
'@ | Set-Content -LiteralPath $ps51Child -Encoding ascii
        & $ps51Exe -NoProfile -File $ps51Child (Join-Path $lib 'test-phase-paths.ps1')
        Assert-Check ($LASTEXITCODE -eq 0) 'ps51-powershell-exe-load-pending-native'
        Remove-Item -LiteralPath $ps51Root -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host 'SKIP ps51-powershell-exe-load-pending-native pending-native: desktop lane loads scripts/lib/test-phase-paths.ps1 under powershell.exe; blank TimingRoot release-cut.ps1 is the production caller (-WhatIf skips that load)'
}

if ((Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue) -and
    ('Antiphon.Nightly.QualificationTiming' -as [type])) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $resolved = Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot $root -RunId 'run-1'
        Assert-Check (-not [string]::IsNullOrWhiteSpace([string]$resolved)) 'blank-root-resolved'
        Assert-Check ($resolved.Replace('\', '/').EndsWith('timing-run-1') -or $resolved.Replace('\', '/').EndsWith('/timing')) 'blank-root-retained'
        $kept = Resolve-QualificationTimingRoot -TimingRoot (Join-Path $root 'explicit') -LogRoot $root -RunId 'run-1'
        Assert-Check ($kept -eq (Join-Path $root 'explicit')) 'explicit-root-kept'

        $envMap = @{}
        Enable-TestPhaseExecutionEnvironment -Environment $envMap -LogRoot $root -RunId 'run-1' -GroupId 'g1' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot $resolved
        Assert-Check ($envMap['ANTIPHON_TEST_PHASE_ROOT'] -eq $resolved) 'nightly-profile-enables-phase-root'
        $buildEnv = @{ ANTIPHON_TEST_PHASE_ROOT = $resolved; ANTIPHON_TEST_PHASE_RUN_ID = 'run-1' }
        Remove-TestPhaseVariables -Environment $buildEnv
        Assert-Check (-not $buildEnv.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')) 'build-child-strips-phase-root'
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Wrapper path: nightly profile, one native chunk, seam instead of Windows job custody.
$wrap = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-wrap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $wrap -Force | Out-Null
$seamPath = Join-Path $wrap 'seams.ps1'
$captures = Join-Path $wrap 'launches.txt'
$seamText = @'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join ' '
        $phase = ''
        if ($Environment -and $Environment.ContainsKey('ANTIPHON_TEST_PHASE_ROOT')) { $phase = [string]$Environment['ANTIPHON_TEST_PHASE_ROOT'] }
        Add-Content -LiteralPath $env:QTIMING_CAPTURES -Value ($FilePath + ' | ' + $argsText + ' | ' + $phase) -Encoding ascii
        $trxName = ''
        $results = ''
        for ($i = 0; $i -lt @($ArgumentList).Count; $i++) {
            if ([string]$ArgumentList[$i] -eq '--report-trx-filename') { $trxName = [string]$ArgumentList[$i + 1] }
            if ([string]$ArgumentList[$i] -eq '--results-directory') { $results = [string]$ArgumentList[$i + 1] }
        }
        if ($trxName -and $results -and ($argsText -notmatch '--list-tests')) {
            $trxPath = Join-Path $results $trxName
            $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun><Results>
<UnitTestResult testId="a" testName="Slow" duration="00:00:03" outcome="Passed" />
<UnitTestResult testId="b" testName="Mid" duration="00:00:02" outcome="Passed" />
<UnitTestResult testId="c" testName="Fast" duration="00:00:01" outcome="Passed" />
</Results></TestRun>
"@
            [IO.File]::WriteAllText($trxPath, $xml)
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
    }
}
'@
[IO.File]::WriteAllText($seamPath, $seamText)
$env:QTIMING_CAPTURES = $captures
$logRoot = Join-Path $wrap 'run'
try {
    $result = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $logRoot -Suites @('antiphon') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'timing-wrap' -Trigger 'manual' -SeamsPath $seamPath -AllowSharedTree
    $summaryPath = Join-Path $logRoot 'summary.json'
    Assert-Check (Test-Path -LiteralPath $summaryPath) 'wrapper-summary-exists'
    if (Test-Path -LiteralPath $summaryPath) {
        $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Check (-not [string]::IsNullOrWhiteSpace([string]$summary.timingRoot)) 'wrapper-summary-timing-root'
        $chunks = @()
        if ($summary.PSObject.Properties.Name -contains 'timingChunks' -and $null -ne $summary.timingChunks) {
            $chunks = @($summary.timingChunks)
        }
        Assert-Check ($chunks.Count -ge 1) 'wrapper-chunk-table'
        $native = @($chunks | Where-Object { [string]$_.suite -eq 'antiphon' -and [string]$_.chunk -ne 'unstarted' })
        Assert-Check ($native.Count -ge 1) 'wrapper-native-row'
        if ($native.Count -ge 1) {
            Assert-Check ($null -ne $native[0].queueWaitSeconds) 'wrapper-queue'
            Assert-Check ($null -ne $native[0].buildSeconds) 'wrapper-build'
            Assert-Check ($null -ne $native[0].runWallSeconds) 'wrapper-run-wall'
            Assert-Check ($null -ne $native[0].p50Seconds) 'wrapper-p50'
            Assert-Check (@($native[0].slowest).Count -ge 1) 'wrapper-slowest'
            $sideName = '{0}-{1}.timing.json' -f $native[0].suite, $native[0].chunk
            Assert-Check (Test-Path -LiteralPath (Join-Path $logRoot $sideName)) 'wrapper-sidecar-beside-trx'
        }
        $timeline = @($summary.suiteTimeline)
        Assert-Check ($timeline.Count -ge 1 -and -not [string]::IsNullOrWhiteSpace([string]$timeline[0].startedAt)) 'wrapper-suite-timeline'
    }
    $launchText = ''
    if (Test-Path -LiteralPath $captures) { $launchText = [IO.File]::ReadAllText($captures) }
    $launches = @()
    foreach ($line in @($launchText -split "`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split ' \| ', 3
        $phase = ''
        if ($parts.Count -gt 2) { $phase = [string]$parts[2] }
        $launches += [pscustomobject]@{ File = [string]$parts[0]; Args = [string]$parts[1]; Phase = $phase.Trim() }
    }
    Assert-Check ($launches.Count -ge 1) 'wrapper-recorded-launches'
    $enabled = @($launches | Where-Object { $_.Args -match '--report-trx' -and $_.Args -notmatch '--list-tests' -and -not [string]::IsNullOrWhiteSpace($_.Phase) })
    Assert-Check ($enabled.Count -ge 1) 'wrapper-native-child-has-phase-root'
    $buildLines = @($launches | Where-Object { $_.Args -match 'build Antiphon.sln' })
    $buildLeaked = @($buildLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Phase) })
    Assert-Check ($buildLines.Count -ge 1 -and $buildLeaked.Count -eq 0) 'wrapper-build-has-no-phase-root'
    Assert-Check ($null -ne $result) 'wrapper-returned'
} catch {
    Write-Host ('FAIL wrapper-threw ' + $_.Exception.Message)
    $script:failed++
} finally {
    Remove-Item Env:QTIMING_CAPTURES -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $wrap -Recurse -Force -ErrorAction SilentlyContinue
}

# Linux supervisor-death custody is a known gap. The portable setsid launcher
# is gone. An unseamed Linux launch uses the Windows Job Object owner and refuses.
$script:NightlySeams = $null
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    $own = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-own-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $own -Force | Out-Null
    $linuxRefusal = ''
    try {
        Invoke-NightlyOwnedProcess -FilePath '/bin/sh' -ArgumentList @('-c', 'echo refused') -WorkingDirectory $own -TimeoutMilliseconds 2000 -LogPath (Join-Path $own 'refused.log') | Out-Null
    } catch {
        $linuxRefusal = [string]$_.Exception.Message
    } finally {
        Remove-Item -LiteralPath $own -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($linuxRefusal -ne 'Nightly native custody requires Windows.') {
        Write-Host ('linux-refusal=[' + $linuxRefusal + ']')
    }
    Assert-Check ($linuxRefusal -eq 'Nightly native custody requires Windows.') 'linux-supervisor-death-custody-is-a-known-gap'
}

# F4: one explicit timing root overrides an inherited phase root.
$phaseRootBefore = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
try {
    $envRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-env-' + [guid]::NewGuid().ToString('N'))
    $explicitRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-explicit-' + [guid]::NewGuid().ToString('N'))
    [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $envRoot)
    $forced = @{}
    Enable-TestPhaseExecutionEnvironment -Environment $forced -LogRoot $envRoot -RunId 'run-env' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot $explicitRoot
    Assert-Check ($forced['ANTIPHON_TEST_PHASE_ROOT'] -eq $explicitRoot) 'explicit-timing-root-overrides-environment'
    $blankMap = @{}
    Enable-TestPhaseExecutionEnvironment -Environment $blankMap -LogRoot $envRoot -RunId 'run-env' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot ''
    Assert-Check ($blankMap['ANTIPHON_TEST_PHASE_ROOT'] -eq $envRoot) 'blank-timing-root-uses-environment'
    [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', '   ')
    $absentMap = @{}
    $absentLog = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-absent-' + [guid]::NewGuid().ToString('N'))
    Enable-TestPhaseExecutionEnvironment -Environment $absentMap -LogRoot $absentLog -RunId 'run-env' -GroupId 'g' -Attempt '1' -Suite 'antiphon' -Profile 'nightly' -TimingRoot ''
    Assert-Check ($absentMap['ANTIPHON_TEST_PHASE_ROOT'] -eq (Join-Path $absentLog 'timing')) 'absent-timing-root-uses-log'
} finally {
    [Environment]::SetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT', $phaseRootBefore)
}

# F6: malformed telemetry cannot stall a heartbeat or a terminal read.
$boundRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-bound-' + [guid]::NewGuid().ToString('N'))
$boundLog = Join-Path $boundRoot 'logs'
$boundTiming = Join-Path $boundLog 'timing'
New-Item -ItemType Directory -Path $boundTiming -Force | Out-Null
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $huge = Join-Path $boundTiming 'cases-80-huge.jsonl'
        $hugeStream = [IO.File]::Create($huge)
        try {
            $chunk = [Text.Encoding]::UTF8.GetBytes('x' * 1MB)
            for ($n = 0; $n -lt 100; $n++) { $hugeStream.Write($chunk, 0, $chunk.Length) }
        } finally {
            $hugeStream.Dispose()
        }
        $hugeResult = Join-Path $boundRoot 'huge.json'
        $hugeScript = Join-Path $boundRoot 'huge.ps1'
        $hugeBody = @'
param([string]$Path, [string]$Result, [string]$Lib)
$ErrorActionPreference = 'Stop'
. (Join-Path $Lib 'nightly-common.ps1')
. (Join-Path $Lib 'test-phase-evidence.ps1')
$sw = [Diagnostics.Stopwatch]::StartNew()
$read = [Antiphon.Nightly.TestPhaseReads]::JsonlBounded($Path, 262144, 4000, 200, [Diagnostics.Stopwatch]::StartNew())
$sw.Stop()
$peak = [Diagnostics.Process]::GetCurrentProcess().PeakWorkingSet64
$payload = [ordered]@{
    seconds = $sw.Elapsed.TotalSeconds
    peak = $peak
    complete = [bool]$read.Complete
    count = @($read.Records).Count
}
[IO.File]::WriteAllText($Result, ($payload | ConvertTo-Json -Compress))
'@
        [IO.File]::WriteAllText($hugeScript, $hugeBody.Replace("`r", ''))
        $hugeRun = Invoke-QualificationWitness -ScriptPath $hugeScript -Arguments @($huge, $hugeResult, $lib) -TimeoutMilliseconds 8000
        $hugePresent = Test-Path -LiteralPath $hugeResult
        $hugeDoc = $null
        if ($hugePresent) { $hugeDoc = Get-Content -LiteralPath $hugeResult -Raw -Encoding UTF8 | ConvertFrom-Json }
        # Keep the child facts in captured stdout/TRX before the temporary root is removed.
        Write-Host ('MEASURED oversized-child ' + (@{ run = $hugeRun; resultPresent = $hugePresent; reader = $hugeDoc } | ConvertTo-Json -Depth 5 -Compress))
        Assert-Check $hugeRun.Exited 'oversized-line-child-exited'
        Assert-Check ($hugeRun.Exited -and $hugeRun.ExitCode -eq 0) 'oversized-line-child-exit-code'
        Assert-Check $hugePresent 'oversized-line-child-result-present'
        Assert-Check ($null -ne $hugeDoc -and [double]$hugeDoc.seconds -lt 0.2) 'oversized-line-reader-wall-budget'
        Assert-Check ($null -ne $hugeDoc -and [int64]$hugeDoc.peak -lt 256MB) 'oversized-line-peak-memory'
        Assert-Check ($null -ne $hugeDoc -and -not [bool]$hugeDoc.complete) 'oversized-line-incomplete'
        Assert-Check ($null -ne $hugeDoc -and [int]$hugeDoc.count -eq 0) 'oversized-line-no-records'

        $fifoDir = Join-Path $boundTiming 'fifo-dir'
        New-Item -ItemType Directory -Path $fifoDir -Force | Out-Null
        $fifoChunk = Join-Path $fifoDir ([Antiphon.Nightly.QualificationTiming]::ChunkDirectoryName('g1', 'run-fifo', '1', 'antiphon'))
        New-Item -ItemType Directory -Path $fifoChunk -Force | Out-Null
        $fifo = Join-Path $fifoChunk 'cases-81-fifo.jsonl'
        $fifoOkCreated = New-HarnessFifo $fifo
        $fifoResult = Join-Path $boundRoot 'fifo.json'
        $fifoScript = Join-Path $boundRoot 'fifo.ps1'
        $fifoBody = @'
param([string]$Path, [string]$Result, [string]$Timing, [string]$Lib)
$ErrorActionPreference = 'Stop'
. (Join-Path $Lib 'nightly-common.ps1')
. (Join-Path $Lib 'test-phase-evidence.ps1')
$timingReads = [Antiphon.Nightly.QualificationTiming]::StartReadLog()
$sw = [Diagnostics.Stopwatch]::StartNew()
$read = [Antiphon.Nightly.TestPhaseReads]::JsonlBounded($Path, 262144, 4000, 200, [Diagnostics.Stopwatch]::StartNew())
$readSeconds = $sw.Elapsed.TotalSeconds
$sums = [Antiphon.Nightly.QualificationTiming]::PhaseSums($Timing, 'g1', 'run-fifo', 'antiphon', '1')
$sw.Stop()
$listedFolder = ''
if ($timingReads.Listings.Count -gt 0) { $listedFolder = $timingReads.Listings[$timingReads.Listings.Count - 1] }
$waitNull = ($null -ne $sums -and $null -eq $sums.processWaitSeconds)
$payload = [ordered]@{
    seconds = $sw.Elapsed.TotalSeconds
    complete = [bool]$read.Complete
    count = @($read.Records).Count
    waitNull = [bool]$waitNull
    readSeconds = $readSeconds
    openCalls = $timingReads.Opens.Count
    listCalls = $timingReads.Listings.Count
    listedEntries = [int]($timingReads.ListedEntries | Measure-Object -Sum).Sum
    listedFolder = $listedFolder
}
[IO.File]::WriteAllText($Result, ($payload | ConvertTo-Json -Compress))
'@
        [IO.File]::WriteAllText($fifoScript, $fifoBody.Replace("`r", ''))
        $fifoRun = Invoke-QualificationWitness -ScriptPath $fifoScript -Arguments @($fifo, $fifoResult, $fifoDir, $lib) -TimeoutMilliseconds 15000
        Assert-Check $fifoRun.Exited 'fifo-cases-file-child-exited'
        Assert-Check ($fifoRun.ExitCode -eq 0) 'fifo-cases-file-child-exit-code'
        $fifoOk = $false
        if ($fifoOkCreated -and $fifoRun.Exited -and $fifoRun.ExitCode -eq 0 -and (Test-Path -LiteralPath $fifoResult)) {
            $fifoDoc = Get-Content -LiteralPath $fifoResult -Raw -Encoding UTF8 | ConvertFrom-Json
            Write-Host ('MEASURED fifo-child=' + $fifoRun.Seconds + ' body=' + $fifoDoc.seconds + ' read=' + $fifoDoc.readSeconds + ' opens=' + $fifoDoc.openCalls + ' lists=' + $fifoDoc.listCalls + ' entries=' + $fifoDoc.listedEntries)
            Assert-Check ([int]$fifoDoc.openCalls -eq 0) 'fifo-cases-file-never-opens-stream'
            Assert-Check ([int]$fifoDoc.listCalls -eq 1 -and [int]$fifoDoc.listedEntries -eq 1 -and [string]$fifoDoc.listedFolder -eq $fifoChunk) 'fifo-cases-file-lists-only-own-chunk'
            # These two calls have independent product watches. Refusal before
            # stream open and one bounded listing are the regression oracle;
            # this outer wall allowance includes shared-host scheduling.
            Assert-Check ([double]$fifoDoc.seconds -lt 0.5) 'fifo-cases-file-in-process-budget'
            $fifoOk = (-not [bool]$fifoDoc.complete -and [int]$fifoDoc.count -eq 0 -and [bool]$fifoDoc.waitNull)
        }
        Assert-Check $fifoOk 'fifo-cases-file-refuses-unsafe-input'

        $denied = Join-Path $boundTiming 'cases-82-denied.jsonl'
        [IO.File]::WriteAllText($denied, '{"recordKind":"header"}' + "`n")
        Set-HarnessUnixMode $denied '000'
        $deniedResult = Join-Path $boundRoot 'denied.json'
        $deniedScript = Join-Path $boundRoot 'denied.ps1'
        $deniedBody = @'
param([string]$Path, [string]$Result, [string]$Lib)
$ErrorActionPreference = 'Stop'
. (Join-Path $Lib 'nightly-common.ps1')
. (Join-Path $Lib 'test-phase-evidence.ps1')
$sw = [Diagnostics.Stopwatch]::StartNew()
$read = [Antiphon.Nightly.TestPhaseReads]::JsonlBounded($Path, 262144, 4000, 200, [Diagnostics.Stopwatch]::StartNew())
$side = [Antiphon.Nightly.QualificationTiming]::ReadSidecar($Path)
$sw.Stop()
$payload = [ordered]@{
    seconds = $sw.Elapsed.TotalSeconds
    complete = [bool]$read.Complete
    count = @($read.Records).Count
    sideNull = ($null -eq $side)
}
[IO.File]::WriteAllText($Result, ($payload | ConvertTo-Json -Compress))
'@
        [IO.File]::WriteAllText($deniedScript, $deniedBody.Replace("`r", ''))
        $deniedRun = Invoke-QualificationWitness -ScriptPath $deniedScript -Arguments @($denied, $deniedResult, $lib) -TimeoutMilliseconds 15000
        Assert-Check $deniedRun.Exited 'unreadable-timing-file-child-exited'
        Assert-Check ($deniedRun.ExitCode -eq 0) 'unreadable-timing-file-child-exit-code'
        $deniedOk = $false
        if ($deniedRun.Exited -and $deniedRun.ExitCode -eq 0 -and (Test-Path -LiteralPath $deniedResult)) {
            $deniedDoc = Get-Content -LiteralPath $deniedResult -Raw -Encoding UTF8 | ConvertFrom-Json
            Assert-Check ([double]$deniedDoc.seconds -lt 0.2) 'unreadable-timing-file-in-process-budget'
            $deniedOk = (-not [bool]$deniedDoc.complete -and [int]$deniedDoc.count -eq 0 -and [bool]$deniedDoc.sideNull)
        }
        Assert-Check $deniedOk 'unreadable-timing-file-refuses-unsafe-input'
        Set-HarnessUnixMode $denied '644'

        $launchRoot = Join-Path $boundRoot 'launch'
        New-Item -ItemType Directory -Path $launchRoot -Force | Out-Null
        $launchLog = Join-Path $launchRoot 'logs'
        New-Item -ItemType Directory -Path $launchLog -Force | Out-Null
        $launchFifo = Join-Path $launchLog 'antiphon-all.timing.json'
        $launchCreated = New-HarnessFifo $launchFifo
        $launchMarker = Join-Path $launchRoot 'launched'
        $launchSeam = Join-Path $launchRoot 'seam.ps1'
        $launchSeamBody = @'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = 'test'; detail = 'seam' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join ' '
        if ($argsText -match '--report-trx' -and $argsText -notmatch '--list-tests') {
            $script:launchWitnessCount++
            $script:launchWitnessReached = $true
            $timingReads = [Antiphon.Nightly.QualificationTiming]::ReadLog
            $marker = [ordered]@{
                seconds = $script:launchWitnessWatch.Elapsed.TotalSeconds
                sidecarReads = $timingReads.SidecarReads
                fifoOpens = $script:launchWitnessFifoOpens + @($timingReads.Opens | Where-Object { $_ -eq $script:launchWitnessSidecar }).Count
            }
            [IO.File]::WriteAllText($env:QTIMING_LAUNCH_MARKER, ($marker | ConvertTo-Json -Compress))
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = '' }
    }
}
'@
        [IO.File]::WriteAllText($launchSeam, $launchSeamBody.Replace("`r", ''))
        $launchScript = Join-Path $launchRoot 'launch.ps1'
        $launchBody = @'
param([string]$Repo, [string]$LogRoot, [string]$Seam, [string]$Marker, [string]$ResultPath)
$ErrorActionPreference = 'Stop'
$env:QTIMING_LAUNCH_MARKER = $Marker
. (Join-Path $Repo 'scripts/lib/nightly-common.ps1')
. (Join-Path $Repo 'scripts/lib/nightly-coverage.ps1')
. (Join-Path $Repo 'scripts/lib/release-gate.ps1')
. (Join-Path $Repo 'scripts/lib/release-selection.ps1')
. (Join-Path $Repo 'scripts/lib/nightly-tests-impl.ps1')
$script:launchWitnessCount = 0
$script:launchWitnessReached = $false
$script:launchWitnessFifoOpens = 0
$script:launchWitnessSidecar = Join-Path $LogRoot 'antiphon-all.timing.json'
$null = [Antiphon.Nightly.QualificationTiming]::StartReadLog()
$sw = [Diagnostics.Stopwatch]::StartNew()
$script:launchWitnessWatch = $sw
$result = Invoke-AntiphonNightlyTests -RepoRoot $Repo -LogRoot $LogRoot -TimingRoot (Join-Path $LogRoot 'timing') -Suites @('antiphon') -Profile 'nightly' -Sha 'd3fd72f941abda0fd6026f5e564d66ac410bdf47' -RunId 'fifo-launch' -Trigger 'manual' -SeamsPath $Seam -AllowSharedTree
$sw.Stop()
$launched = Test-Path -LiteralPath $Marker
$payload = [ordered]@{
    seconds = $sw.Elapsed.TotalSeconds
    launched = $launched
    resultPresent = ($null -ne $result)
    launchCount = $script:launchWitnessCount
}
[IO.File]::WriteAllText($ResultPath, ($payload | ConvertTo-Json -Compress))
if ($launched -and $null -ne $result) { exit 0 }
exit 1
'@
        [IO.File]::WriteAllText($launchScript, $launchBody.Replace("`r", ''))
        $launchResult = Join-Path $launchRoot 'result.json'
        # 20 cold children per arm: worse p95 body=6.971 s, child=12.396 s.
        # Keep startup separate from body and protect launch order structurally.
        $launchRun = Invoke-QualificationWitness -ScriptPath $launchScript -Arguments @($repo, $launchLog, $launchSeam, $launchMarker, $launchResult) -TimeoutMilliseconds 26000
        Write-Host ('MEASURED launch-child=' + $launchRun.Seconds + ' exited=' + $launchRun.Exited + ' exit=' + $launchRun.ExitCode + ' marker=' + (Test-Path -LiteralPath $launchMarker))
        Assert-Check $launchRun.Exited 'fifo-sidecar-launch-child-exited'
        Assert-Check ($launchRun.ExitCode -eq 0) 'fifo-sidecar-launch-child-exit-code'
        if (Test-Path -LiteralPath $launchResult) {
            $launchDoc = Get-Content -LiteralPath $launchResult -Raw | ConvertFrom-Json
            Write-Host ('MEASURED launch-body=' + $launchDoc.seconds + ' count=' + $launchDoc.launchCount)
            Assert-Check ([double]$launchDoc.seconds -lt 15) 'fifo-sidecar-launch-body-budget'
            Assert-Check ([bool]$launchDoc.resultPresent -and [int]$launchDoc.launchCount -eq 1) 'fifo-sidecar-launch-result-and-count'
        } else { Assert-Check $false 'fifo-sidecar-launch-result-and-count' }
        if (Test-Path -LiteralPath $launchMarker) {
            $launchMark = Get-Content -LiteralPath $launchMarker -Raw | ConvertFrom-Json
            Write-Host ('MEASURED launch-marker=' + $launchMark.seconds + ' reads-before-launch=' + $launchMark.sidecarReads + ' fifo-opens=' + $launchMark.fifoOpens)
            Assert-Check ([int]$launchMark.sidecarReads -eq 0 -and [int]$launchMark.fifoOpens -eq 0) 'fifo-sidecar-no-reads-before-launch'
        } else { Assert-Check $false 'fifo-sidecar-no-reads-before-launch' }
        $launchOk = ($launchCreated -and $launchRun.Exited -and $launchRun.ExitCode -eq 0 -and (Test-Path -LiteralPath $launchMarker))
        Assert-Check $launchOk 'fifo-sidecar-does-not-delay-launch'
    }
} finally {
    Remove-Item -LiteralPath $boundRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# F3: discovery and launch failures still leave a terminal timing row.
$failRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-fail-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $failRoot -Force | Out-Null
$failSeam = Join-Path $failRoot 'seam.ps1'
@'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = "test"; detail = "seam" } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join " "
        if ($argsText -match "--list-tests" -and $env:QTIMING_FAIL -eq "discovery-timeout") {
            return @{ ExitCode = 1; TimedOut = $true; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        if ($argsText -match "--list-tests" -and $env:QTIMING_FAIL -eq "discovery") { throw "REVIEW_DISCOVERY_FAILURE" }
        if ($argsText -match "--report-trx" -and $env:QTIMING_FAIL -eq "launch") { throw "REVIEW_LAUNCH_FAILURE" }
        if ($argsText -match "--report-trx" -and $env:QTIMING_FAIL -eq "missing-trx") {
            return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
    }
}
'@ | Set-Content -LiteralPath $failSeam -Encoding ascii
try {
    foreach ($pair in @(
        @('discovery', 'rc', 'discovery', 'discovery-failed'),
        @('discovery-timeout', 'rc', 'discovery', 'discovery-timeout'),
        @('launch', 'nightly', 'launch', 'launch-failed'),
        @('missing-trx', 'nightly', 'complete', 'missing-trx')
    )) {
        $mode = $pair[0]
        $env:QTIMING_FAIL = $mode
        $one = Join-Path $failRoot $mode
        $ran = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $one -Suites @('antiphon') -Profile $pair[1] -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId ('fail-' + $mode) -Trigger 'manual' -SeamsPath $failSeam -AllowSharedTree
        $summaryPath = Join-Path $one 'summary.json'
        $rowOk = $false
        if (Test-Path -LiteralPath $summaryPath) {
            $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $rows = @()
            if ($summary.PSObject.Properties.Name -contains 'timingChunks' -and $null -ne $summary.timingChunks) { $rows = @($summary.timingChunks) }
            $match = @($rows | Where-Object { [string]$_.lastPhase -eq $pair[2] -and [string]$_.why -eq $pair[3] })
            $rowOk = ($match.Count -ge 1)
            if ($mode -eq 'launch') {
                $rowOk = $rowOk -and ([string]$summary.preflight.unhandledError -match 'REVIEW_LAUNCH_FAILURE')
            }
            if ($mode -eq 'discovery') {
                $rowOk = $rowOk -and ([string]$summary.preflight.unhandledError -match 'REVIEW_DISCOVERY_FAILURE')
            }
        }
        Assert-Check ($ran.ExitCode -eq 1 -and $rowOk) ('failure-row-' + $mode)
    }
} finally {
    Remove-Item Env:QTIMING_FAIL -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $failRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# F2: replaying a sealed blank timingRoot through release-cut must keep the seal.
$cutRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-cut-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cutRoot -Force | Out-Null
try {
    $release = Join-Path $cutRoot 'release'
    $checkout = Join-Path $release 'checkout'
    New-Item -ItemType Directory -Path $checkout -Force | Out-Null
    $git = {
        param([string]$Dir, [string[]]$GitArgs)
        & git -C $Dir @GitArgs 1> $null
        if ($LASTEXITCODE -ne 0) { throw ('git failed ' + ($GitArgs -join ' ')) }
    }
    & $git $checkout @('init', '-q', '-b', 'rc/2026-10-27')
    & $git $checkout @('config', 'user.email', 'fixture@example.invalid')
    & $git $checkout @('config', 'user.name', 'qtiming')
    foreach ($rel in @(
        'tests/test-execution-policy.json',
        'tests/test-selection-policy.json',
        'global.json',
        'scripts/select-test-rosters.ps1',
        'scripts/lib/test-selection.ps1'
    )) {
        $dest = Join-Path $checkout $rel
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo $rel) -Destination $dest
    }
    & $git $checkout @('add', '--', '.')
    & $git $checkout @('commit', '-qm', 'fixture')
    $sha = (& git -C $checkout rev-parse HEAD).Trim()
    [IO.File]::WriteAllText((Join-Path $checkout '.antiphon-release-owned'), '{"kind":"release-clone"}')
    $origin = Join-Path $cutRoot 'origin.git'
    & git -C $cutRoot clone --bare $checkout $origin 1> $null
    if ($LASTEXITCODE -ne 0) { throw 'origin clone failed' }
    & git -C $checkout remote add origin $origin
    if ($LASTEXITCODE -ne 0) { throw 'origin remote failed' }
    $shaFile = Join-Path $cutRoot 'sha.txt'
    [IO.File]::WriteAllText($shaFile, $sha)
    $cutSeam = Join-Path $cutRoot 'seam.ps1'
    $seamTemplate = @'
$ReleaseGateSeams = @{
    Git = {
        param($WorkingDirectory, $Arguments)
        $joined = @($Arguments) -join ' '
        if ($joined -match 'ls-remote') {
            $value = [IO.File]::ReadAllText('__SHA_FILE__').Trim()
            return @{ ExitCode = 0; Output = ($value + " refs/heads/rc/2026-10-27`n") }
        }
        return @{ ExitCode = 0; Output = '' }
    }
    GitHub = { return @{ ExitCode = 0; Output = '' } }
}
'@
    [IO.File]::WriteAllText($cutSeam, $seamTemplate.Replace('__SHA_FILE__', $shaFile).Replace("`r", ''))
    $candidate = [Antiphon.Nightly.ReleaseGateCredit]::CandidateRef('rc/2026-10-27', $sha)
    $blankRun = 'blank-replay'
    $blankRequest = @{
        RepositoryRoot = $checkout; Repository = 'michal-ciechan/Antiphon'; ReleaseRoot = $release
        CandidateSha = $sha; CandidateId = [string]$candidate.CandidateId; CandidateRef = [string]$candidate.Ref
        Profile = 'rc'; RunId = $blankRun; SelectionMode = 'Full'; Attempt = 1
        BaselineSha = ''; BaselineReceiptPath = ''; QualificationPath = ''; TimingRoot = ''
        TrainManifestPath = ''; ReleaseIntentPath = ''
    }
    $seed = New-ReleaseSelectionAttempt $blankRequest
    $seedDigest = [string]$seed.SelectionAttemptDigest
    $blankCut = & (Join-Path $repo 'scripts/release-cut.ps1') -ReleaseRoot $release -CoordinationRoot (Join-Path $cutRoot 'coord') -Ref 'rc/2026-10-27' -RunId $blankRun -TimingRoot '' -SeamsPath $cutSeam -SkipPublish -PassThru
    $afterDigest = Get-NightlyFileSha256 $seed.SelectionAttemptPath
    $sealedText = [IO.File]::ReadAllText($seed.SelectionAttemptPath)
    $blankRefusal = [string]$blankCut.refusal
    Assert-Check ($afterDigest -eq $seedDigest -and $sealedText.Contains('"timingRoot":""') -and $blankRefusal -notmatch 'selection-attempt-conflict') 'blank-sealed-replay-preserves-attempt'

    $explicitTiming = Join-Path $release 'explicit-timing'
    $explicitCut = & (Join-Path $repo 'scripts/release-cut.ps1') -ReleaseRoot $release -CoordinationRoot (Join-Path $cutRoot 'coord') -Ref 'rc/2026-10-27' -RunId 'explicit-run' -TimingRoot $explicitTiming -SeamsPath $cutSeam -SkipPublish -PassThru
    $explicitAttempt = Join-Path (Join-Path (Join-Path (Join-Path (Join-Path $release 'candidates') $candidate.CandidateId) 'attempts') 'explicit-run') 'selection-attempt.json'
    $explicitOk = $false
    if (Test-Path -LiteralPath $explicitAttempt) {
        $explicitDoc = Get-Content -LiteralPath $explicitAttempt -Raw -Encoding UTF8 | ConvertFrom-Json
        $explicitOk = ([string]$explicitDoc.timingRoot -eq $explicitTiming)
    }
    Assert-Check ($explicitOk -and [string]$explicitCut.refusal -notmatch 'selection-attempt-conflict') 'explicit-timing-root-is-sealed'

    $absentCut = & (Join-Path $repo 'scripts/release-cut.ps1') -ReleaseRoot $release -CoordinationRoot (Join-Path $cutRoot 'coord') -Ref 'rc/2026-10-27' -RunId 'absent-run' -TimingRoot '' -SeamsPath $cutSeam -SkipPublish -PassThru
    $absentAttempt = Join-Path (Join-Path (Join-Path (Join-Path (Join-Path $release 'candidates') $candidate.CandidateId) 'attempts') 'absent-run') 'selection-attempt.json'
    $absentLog = Join-Path (Join-Path (Join-Path $release 'candidates') ([string]$candidate.CandidateId)) 'logs'
    $absentExpected = Resolve-QualificationTimingRoot -TimingRoot '' -LogRoot $absentLog -RunId 'absent-run'
    $absentOk = $false
    if (Test-Path -LiteralPath $absentAttempt) {
        $absentDoc = Get-Content -LiteralPath $absentAttempt -Raw -Encoding UTF8 | ConvertFrom-Json
        $absentOk = ([string]$absentDoc.timingRoot -eq $absentExpected)
    }
    Assert-Check ($absentOk -and [string]$absentCut.refusal -ne 'selection-attempt-conflict') 'absent-timing-root-defaults-for-new-attempt'
} catch {
    Write-Host ('FAIL release-cut-timing ' + $_.Exception.Message)
    $script:failed++
} finally {
    Remove-Item -LiteralPath $cutRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# The launch heartbeat must not open a timing file. strace records openat only.
# Absent strace is an explicit skip: an ungated missing command is a failure
# on Windows and on strace-less Linux.
$openStrace = '/tmp/strace-root/usr/bin/strace'
if (-not (Test-Path -LiteralPath $openStrace)) {
    $openCmd = Get-Command strace -ErrorAction SilentlyContinue
    if ($openCmd) { $openStrace = [string]$openCmd.Source }
}
if (-not (Test-Path -LiteralPath $openStrace)) {
    Write-Host 'SKIP launch-path-opens-no-timing-file strace-absent'
} else {
$openDenied = ''
$openRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-open-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $openRoot -Force | Out-Null
$openLog = Join-Path $openRoot 'logs'
$openTiming = Join-Path $openLog 'timing'
New-Item -ItemType Directory -Path $openTiming -Force | Out-Null
try {
    $openFifoTrx = Join-Path $openLog 'antiphon-g1.trx'
    $openSide = Join-Path $openLog 'antiphon-g1.timing.json'
    $openHuge = Join-Path $openTiming 'cases-80-huge.jsonl'
    $openDenied = Join-Path $openTiming 'cases-82-denied.jsonl'
    $openLink = Join-Path $openTiming 'cases-83-link.jsonl'
    $openFifo = Join-Path $openTiming 'cases-81-fifo.jsonl'
    New-HarnessFifo $openFifoTrx | Out-Null
    New-HarnessFifo $openSide | Out-Null
    New-HarnessFifo $openFifo | Out-Null
    $openStream = [IO.File]::Create($openHuge)
    try {
        $openChunk = [Text.Encoding]::UTF8.GetBytes('x' * 1MB)
        for ($n = 0; $n -lt 100; $n++) { $openStream.Write($openChunk, 0, $openChunk.Length) }
    } finally { $openStream.Dispose() }
    [IO.File]::WriteAllText($openDenied, '{"recordKind":"header"}' + "`n")
    Set-HarnessUnixMode $openDenied '000'
    New-HarnessSymlink $openLink $openHuge | Out-Null
    $openMarker = Join-Path $openRoot 'launched'
    $openChild = Join-Path $openRoot 'heart.ps1'
    $openTrace = Join-Path $openRoot 'openat.txt'
    @'
param([string]$Lib, [string]$Log, [string]$Timing, [string]$Trx, [string]$Marker)
$ErrorActionPreference = "Stop"
. (Join-Path $Lib "nightly-common.ps1")
. (Join-Path $Lib "nightly-coverage.ps1")
. (Join-Path $Lib "release-gate.ps1")
. (Join-Path $Lib "release-selection.ps1")
. (Join-Path $Lib "nightly-tests-impl.ps1")
[Antiphon.Nightly.QualificationTiming]::Update(@{ LogRoot = $Log; TimingRoot = $Timing; Suite = "antiphon"; Chunk = "g1"; LastPhase = "run"; StartedAt = "2026-10-27T00:00:00.0000000Z"; QueueWaitSeconds = 1; BuildSeconds = 2; DiscoverySeconds = 0; TrxPath = $Trx; RunId = "open"; Attempt = "1" }, [datetime]::UtcNow)
[IO.File]::WriteAllText($Marker, "LAUNCHED")
'@ | Set-Content -LiteralPath $openChild -Encoding ascii
    $openDriver = Join-Path $openRoot 'drive.ps1'
    @'
param([string]$Strace, [string]$Trace, [string]$Child, [string]$Lib, [string]$Log, [string]$Timing, [string]$Trx, [string]$Marker)
$ErrorActionPreference = "Stop"
& $Strace -f -e trace=openat -o $Trace -- pwsh -NoProfile -File $Child $Lib $Log $Timing $Trx $Marker
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $openDriver -Encoding ascii
    $openRun = Invoke-QualificationWitness -ScriptPath $openDriver -Arguments @($openStrace, $openTrace, $openChild, $lib, $openLog, $openTiming, $openFifoTrx, $openMarker) -TimeoutMilliseconds 20000
    $openClean = $false
    if ((Test-Path -LiteralPath $openTrace) -and (Test-Path -LiteralPath $openMarker)) {
        $openText = [IO.File]::ReadAllText($openTrace)
        $openClean = $openText.Contains('openat(')
        foreach ($hostile in @($openFifoTrx, $openSide, $openHuge, $openDenied, $openLink, $openFifo)) {
            if ($openText.Contains($hostile)) { $openClean = $false }
        }
    }
    Assert-Check ((Test-Path -LiteralPath $openStrace) -and $openRun.Exited -and $openRun.ExitCode -eq 0 -and $openClean) 'launch-path-opens-no-timing-file'
} catch {
    Write-Host ('FAIL launch-path-opens-no-timing-file ' + $_.Exception.Message)
    $script:failed++
} finally {
    if (Test-Path -LiteralPath $openDenied) { Set-HarnessUnixMode $openDenied '644' }
    Remove-Item -LiteralPath $openRoot -Recurse -Force -ErrorAction SilentlyContinue
}
}

# Windows-shaped capture shell: Git sh.exe through the resolver, never a literal /bin/sh.
Invoke-CaptureShellWitness

# Stdout, stderr, and the chunk exit stay the same with the phase environment forwarded or stripped.
$captureShell = Resolve-QualificationCaptureShell
if ([string]::IsNullOrWhiteSpace([string]$captureShell)) {
    Write-Host 'SKIP capture-identity shell-absent: no sh.exe on PATH and no Git for Windows sh.exe beside git'
    Write-Host 'DIAG capture-identity shell-absent'
} else {
Write-Host ('DIAG capture-identity shell=' + $captureShell)
$capRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-cap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $capRoot -Force | Out-Null
$capSeam = Join-Path $capRoot 'seam.ps1'
New-QualificationCaptureSeam -Path $capSeam -Shell ([string]$captureShell)
function Get-QualificationCapturePayload {
    param([string]$Dir)
    $logs = @(Get-ChildItem -LiteralPath $Dir -Recurse -Filter '*-tests.log' -File -ErrorAction SilentlyContinue | Sort-Object FullName)
    $payloads = @()
    foreach ($log in $logs) {
        $text = [IO.File]::ReadAllText($log.FullName)
        $lines = @($text -split "`n")
        if ($lines.Count -gt 0 -and $lines[0].StartsWith('C487_INVOCATION=')) {
            $text = ($lines[1..($lines.Count - 1)] -join "`n")
        }
        $payloads += $text
    }
    return @($payloads)
}
try {
    foreach ($childExit in @(0, 1)) {
        $env:QTIMING_CHILD_EXIT = [string]$childExit
        $runs = @()
        foreach ($forward in @('1', '0')) {
            $env:QTIMING_FORWARD = $forward
            $one = Join-Path $capRoot ('e' + $childExit + '-f' + $forward)
            $capDir = Join-Path $one 'cap'
            New-Item -ItemType Directory -Path $capDir -Force | Out-Null
            $env:QTIMING_CAPTURE_DIR = $capDir
            $ran = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot (Join-Path $one 'run') -Suites @('antiphon') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId ('cap-' + $childExit + '-' + $forward) -Trigger 'manual' -SeamsPath $capSeam -AllowSharedTree
            $summaryPath = Join-Path (Join-Path $one 'run') 'summary.json'
            $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $suiteExits = @()
            foreach ($suite in @($summary.suites)) {
                if ([string]$suite.id -eq 'antiphon' -and -not [bool]$suite.skipped) { $suiteExits += [int]$suite.exitCode }
            }
            $stdout = @(Get-ChildItem -LiteralPath $capDir -Filter 'stdout-*' -File | Sort-Object Name | ForEach-Object { [IO.File]::ReadAllText($_.FullName) })
            $stderr = @(Get-ChildItem -LiteralPath $capDir -Filter 'stderr-*' -File | Sort-Object Name | ForEach-Object { [IO.File]::ReadAllText($_.FullName) })
            $exits = @(Get-ChildItem -LiteralPath $capDir -Filter 'exit-*' -File | Sort-Object Name | ForEach-Object { [int]([IO.File]::ReadAllText($_.FullName)) })
            $seen = @(Get-ChildItem -LiteralPath $capDir -Filter 'seen-*' -File -ErrorAction SilentlyContinue)
            $runs += [pscustomobject]@{
                Forward = $forward
                Wrapper = [int]$ran.ExitCode
                Suites = ($suiteExits -join ',')
                StdOut = ($stdout -join '|')
                StdErr = ($stderr -join '|')
                Exits = ($exits -join ',')
                Seen = @($seen).Count
                Payload = ((Get-QualificationCapturePayload -Dir (Join-Path $one 'run')) -join '|')
            }
        }
        $on = $runs[0]
        $off = $runs[1]
        $outParts = @($on.StdOut -split '\|')
        $errParts = @($on.StdErr -split '\|')
        $exitParts = @($on.Exits -split ',')
        $suiteParts = @($on.Suites -split ',')
        $bytesOk = ($outParts.Count -ge 1 -and @($outParts | Where-Object { $_ -ne 'QUAL-OUT' }).Count -eq 0 -and @($errParts | Where-Object { $_ -ne 'QUAL-ERR' }).Count -eq 0 -and @($exitParts | Where-Object { $_ -ne ([string]$childExit) }).Count -eq 0 -and $suiteParts.Count -eq $exitParts.Count)
        $same = ($on.Wrapper -eq $off.Wrapper -and $on.Wrapper -eq 1 -and $on.Suites -eq $off.Suites -and $on.StdOut -eq $off.StdOut -and $on.StdErr -eq $off.StdErr -and $on.Exits -eq $off.Exits -and $on.Payload -eq $off.Payload -and $on.Seen -ge 1 -and $off.Seen -eq 0 -and $bytesOk)
        if (-not $same) {
            Write-Host ('DIAG capture-identity-exit-' + $childExit + ' wrapper=' + $on.Wrapper + '/' + $off.Wrapper + ' suites=' + $on.Suites + '|' + $off.Suites + ' stdout=' + $on.StdOut + '|' + $off.StdOut + ' stderr=' + $on.StdErr + '|' + $off.StdErr + ' exits=' + $on.Exits + '|' + $off.Exits + ' seen=' + $on.Seen + '/' + $off.Seen + ' bytesOk=' + $bytesOk + ' payloadEqual=' + ($on.Payload -eq $off.Payload))
        }
        Assert-Check $same ('capture-identity-exit-' + $childExit)
    }
} catch {
    Write-Host ('FAIL capture-identity shell=' + $captureShell + ' ' + $_.Exception.Message)
    $script:failed++
} finally {
    Remove-Item Env:QTIMING_CHILD_EXIT -ErrorAction SilentlyContinue
    Remove-Item Env:QTIMING_FORWARD -ErrorAction SilentlyContinue
    Remove-Item Env:QTIMING_CAPTURE_DIR -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $capRoot -Recurse -Force -ErrorAction SilentlyContinue
}
}

# The real nightly heartbeat (the pre-launch QualificationTiming.Update) must stay
# non-terminal: a readable TRX already at the chunk path must not be parsed into the
# sidecar before the owned process starts.
$heartRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-heart-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $heartRoot -Force | Out-Null
$heartLog = Join-Path $heartRoot 'logs'
New-Item -ItemType Directory -Path $heartLog -Force | Out-Null
$heartSeen = Join-Path $heartRoot 'seen.json'
$heartSeam = Join-Path $heartRoot 'seam.ps1'
try {
    $probeTrx = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun><Results>
<UnitTestResult testId="probe" testName="HEARTBEAT-PROBE" duration="00:00:09" outcome="Passed" />
</Results></TestRun>
'@
    [IO.File]::WriteAllText((Join-Path $heartLog 'antiphon-all.trx'), $probeTrx)
    @'
$NightlySeams = @{
    DockerInfo = { return @{ exitCode = 0; version = "test"; detail = "seam" } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $argsText = @($ArgumentList) -join " "
        $results = ""
        $trxName = ""
        for ($i = 0; $i -lt @($ArgumentList).Count; $i++) {
            if ([string]$ArgumentList[$i] -eq "--report-trx-filename") { $trxName = [string]$ArgumentList[$i + 1] }
            if ([string]$ArgumentList[$i] -eq "--results-directory") { $results = [string]$ArgumentList[$i + 1] }
        }
        if ($trxName -and $results -and ($argsText -notmatch "--list-tests")) {
            $side = Join-Path $results ([IO.Path]::GetFileNameWithoutExtension($trxName) + ".timing.json")
            $doc = $null
            if (Test-Path -LiteralPath $side) {
                $doc = Get-Content -LiteralPath $side -Raw -Encoding UTF8 | ConvertFrom-Json
            }
            $names = @()
            if ($null -ne $doc -and $doc.PSObject.Properties.Name -contains "slowest" -and $null -ne $doc.slowest) {
                foreach ($row in @($doc.slowest)) { $names += [string]$row.name }
            }
            $payload = [ordered]@{
                exists = [bool](Test-Path -LiteralPath $side)
                lastPhase = $(if ($null -ne $doc) { [string]$doc.lastPhase } else { "" })
                why = $(if ($null -ne $doc) { [string]$doc.why } else { "" })
                testCount = $(if ($null -ne $doc -and $null -ne $doc.testCount) { [int]$doc.testCount } else { -1 })
                p50 = $(if ($null -ne $doc) { $doc.p50Seconds } else { $null })
                names = ($names -join ",")
            }
            [IO.File]::WriteAllText($env:QTIMING_HEART_SEEN, ($payload | ConvertTo-Json -Compress))
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
    }
}
'@ | Set-Content -LiteralPath $heartSeam -Encoding ascii
    $env:QTIMING_HEART_SEEN = $heartSeen
    $heartRun = Invoke-AntiphonNightlyTests -RepoRoot $repo -LogRoot $heartLog -TimingRoot (Join-Path $heartLog 'timing') -Suites @('antiphon') -Profile 'nightly' -Sha '9f00008facfc48e75555c08df59887019b3f8dfe' -RunId 'heart-beat' -Trigger 'manual' -SeamsPath $heartSeam -AllowSharedTree
    $heartOk = $false
    if (Test-Path -LiteralPath $heartSeen) {
        $seen = Get-Content -LiteralPath $heartSeen -Raw -Encoding UTF8 | ConvertFrom-Json
        $heartOk = ([bool]$seen.exists -and [string]$seen.lastPhase -eq 'run' -and [string]$seen.why -eq '' -and [int]$seen.testCount -eq 0 -and $null -eq $seen.p50 -and [string]$seen.names -notmatch 'HEARTBEAT-PROBE')
    }
    Assert-Check ($null -ne $heartRun -and $heartOk) 'nightly-heartbeat-stays-non-terminal'
} catch {
    Write-Host ('FAIL nightly-heartbeat-stays-non-terminal ' + $_.Exception.Message)
    $script:failed++
} finally {
    Remove-Item Env:QTIMING_HEART_SEEN -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $heartRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# A symlinked chunk-* directory is not a chunk. The phase reader and the summary
# reader both refuse it. A real chunk directory beside the link is still read.
$linkRoot = Join-Path ([IO.Path]::GetTempPath()) ('qtiming-link-' + [guid]::NewGuid().ToString('N'))
$linkLog = Join-Path $linkRoot 'logs'
$linkTiming = Join-Path $linkLog 'timing'
$linkOutside = Join-Path $linkRoot 'outside'
New-Item -ItemType Directory -Path $linkTiming -Force | Out-Null
New-Item -ItemType Directory -Path $linkOutside -Force | Out-Null
$savedTimingRoot = [string]$script:QualificationTimingRoot
try {
    $script:QualificationTimingRoot = ''
    $linkChunk = 'g-link'
    $linkRun = 'run-link'
    $linkAttempt = '1'
    $linkSuite = 'antiphon'
    $linkName = [Antiphon.Nightly.QualificationTiming]::ChunkDirectoryName($linkChunk, $linkRun, $linkAttempt, $linkSuite)
    $linkJournal = @(
        '{"recordKind":"header","version":1,"pid":42,"nonce":"link1","runId":"run-link","groupId":"g-link","attempt":"1","suite":"antiphon","assembly":"Antiphon.Tests"}',
        '{"recordKind":"fixture","fixtureId":"fix-link","phases":[{"phase":"fixture-setup","startOffsetMs":0,"endOffsetMs":1500}]}',
        '{"recordKind":"complete","testId":"link-case","className":"C","method":"M","args":[],"startOffsetMs":0,"endOffsetMs":1,"outcome":"Passed","phases":[],"processes":[],"exclusive":{"db":0,"host":0,"process":0,"fs":0,"network":0,"fixture":0},"overlapMs":0,"remainderMs":0,"childUnionMs":0,"droppedPhaseCount":0,"shared":false,"incomplete":false}'
    ) -join "`n"
    [IO.File]::WriteAllText((Join-Path $linkOutside 'cases-42-link1.jsonl'), $linkJournal + "`n")
    $linkPath = Join-Path $linkTiming $linkName
    $linkMade = New-HarnessSymlink $linkPath $linkOutside
    $linkSums = [Antiphon.Nightly.QualificationTiming]::PhaseSums($linkTiming, $linkChunk, $linkRun, $linkSuite, $linkAttempt)
    $linkRefused = ($linkMade -and [bool]$linkSums.refused -and $null -eq $linkSums.fixtureSetupSeconds)
    $realName = [Antiphon.Nightly.QualificationTiming]::ChunkDirectoryName('g-real', $linkRun, $linkAttempt, $linkSuite)
    $realDir = Join-Path $linkTiming $realName
    New-Item -ItemType Directory -Path $realDir -Force | Out-Null
    $realJournal = $linkJournal.Replace('g-link', 'g-real').Replace('link-case', 'real-case').Replace('fix-link', 'fix-real')
    [IO.File]::WriteAllText((Join-Path $realDir 'cases-42-link1.jsonl'), $realJournal + "`n")
    $planned = [pscustomobject]@{ version = 1; runId = 'run-link'; assembly = 'Antiphon.Tests' }
    $summary = [Antiphon.Nightly.TestPhaseRunSummary]::Rows($planned, $linkLog, (Get-TestPhasePublicationScope))
    $sawLink = $false
    $sawReal = $false
    foreach ($row in @($summary.Tests)) {
        if ([string]$row.testId -ceq 'link-case') { $sawLink = $true }
        if ([string]$row.testId -ceq 'real-case') { $sawReal = $true }
    }
    $sawFlag = $false
    foreach ($flag in @($summary.Quality)) {
        if ([string]$flag -ceq 'chunk-link-refused') { $sawFlag = $true }
    }
    Assert-Check ($linkRefused -and $sawFlag -and -not $sawLink -and $sawReal) 'symlinked-chunk-directory-is-refused'
} catch {
    Write-Host ('FAIL symlinked-chunk-directory-is-refused ' + $_.Exception.Message)
    $script:failed++
} finally {
    $script:QualificationTimingRoot = $savedTimingRoot
    Remove-Item -LiteralPath $linkRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failed -gt 0) {
    Write-Host ('FAILED ' + $failed)
    exit 1
}
Write-Host 'OK'
exit 0
