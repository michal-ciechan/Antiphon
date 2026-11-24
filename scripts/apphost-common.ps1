<#
.SYNOPSIS
    The desktop AppHost worktree guard (CARD-0273) for the one PowerShell entry that still runs it itself:
    scripts/deploy-local.ps1 (wave W8, owner-gated). Dot-source only.

    Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved everything else this file held (the launch
    and restart locks, the probes, the graceful stop, the log verdicts, the watchdog's outage and dead-launch functions) to
    scripts/lib/Antiphon.Nightly (AppHostCommon.cs, AppHostRestart.cs, AppHostWatchdog.cs); unit 37 ported its other caller,
    dev-aspire.ps1 (the dev-aspire command, DevAspire.cs). The C# twin of these three functions is AppHostWorktree
    (Classify, Canonical, GuardMessage), which restart-apphost and dev-aspire run; this copy goes when deploy-local.ps1 is
    ported. The messages name -AllowWorktree, deploy-local.ps1's parameter.
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads no-BOM .ps1 as CP1252 and mangles
    non-ASCII characters into parse errors.
#>
function ConvertTo-AppHostCanonicalPath {
    <#
      Canonical form for comparing Git worktree paths on Windows. GetFullPath
      resolves relative components without consulting the current directory when
      callers pass Git's absolute paths; comparison is ordinal-ignore-case and
      separator-insensitive below.
    #>
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalised = $Path.Replace([System.IO.Path]::AltDirectorySeparatorChar, [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath($normalised)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        $fullPath = $fullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    }
    return $fullPath
}

function Get-AppHostWorktreeClassification {
    <#
      Read-only classifier for an AppHost command's script root. It deliberately
      uses Git rooted at SourceRoot, never the process current directory. A
      failure is structured so destructive callers can refuse before touching
      locks, logs, processes, Docker, or launch state.
    #>
    param([Parameter(Mandatory = $true)][string]$SourceRoot)

    $result = [pscustomobject]@{
        Verified           = $false
        IsMainWorktree     = $false
        ScriptWorktreeRoot = $null
        MainWorktreeRoot   = $null
        Failure            = $null
    }

    try {
        $rootOutput = @(& git -C $SourceRoot rev-parse --show-toplevel 2>&1)
        $rootExit = $LASTEXITCODE
    } catch {
        $result.Failure = ('git -C "{0}" rev-parse --show-toplevel failed: {1}' -f $SourceRoot, $_.Exception.Message)
        return $result
    }
    $scriptRootText = @($rootOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) | Select-Object -First 1
    if ($rootExit -ne 0 -or -not $scriptRootText) {
        $detail = @($rootOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join ' '
        $result.Failure = ('git -C "{0}" rev-parse --show-toplevel failed (exit {1}){2}' -f $SourceRoot, $rootExit, $(if ($detail) { ': ' + $detail } else { '' }))
        return $result
    }

    try {
        $worktreeOutput = @(& git -C $scriptRootText worktree list --porcelain 2>&1)
        $worktreeExit = $LASTEXITCODE
    } catch {
        $result.Failure = ('git -C "{0}" worktree list --porcelain failed: {1}' -f $scriptRootText, $_.Exception.Message)
        return $result
    }
    if ($worktreeExit -ne 0) {
        $detail = @($worktreeOutput | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ }) -join ' '
        $result.Failure = ('git -C "{0}" worktree list --porcelain failed (exit {1}){2}' -f $scriptRootText, $worktreeExit, $(if ($detail) { ': ' + $detail } else { '' }))
        return $result
    }

    $mainRecord = @($worktreeOutput | ForEach-Object { $_.ToString() } | Where-Object { $_ -match '^worktree\s+(.+)$' }) | Select-Object -First 1
    if (-not $mainRecord) {
        $result.Failure = ('git -C "{0}" worktree list --porcelain returned no first worktree path record' -f $scriptRootText)
        return $result
    }

    try {
        $scriptRoot = ConvertTo-AppHostCanonicalPath -Path $scriptRootText
        $mainMatch = [regex]::Match($mainRecord, '^worktree\s+(.+)$')
        $mainRoot = ConvertTo-AppHostCanonicalPath -Path $mainMatch.Groups[1].Value.Trim()
    } catch {
        $result.Failure = ('could not canonicalize Git worktree paths: {0}' -f $_.Exception.Message)
        return $result
    }

    $result.Verified = $true
    $result.ScriptWorktreeRoot = $scriptRoot
    $result.MainWorktreeRoot = $mainRoot
    $result.IsMainWorktree = [string]::Equals($scriptRoot, $mainRoot, [System.StringComparison]::OrdinalIgnoreCase)
    return $result
}

function Format-AppHostWorktreeGuardMessage {
    <# Returns message lines only; callers decide how and where to print them. #>
    param(
        [Parameter(Mandatory = $true)]$Classification,
        [switch]$AllowWorktree
    )

    if (-not $Classification.Verified) {
        return @(
            'REFUSED: this AppHost command could not verify its Git worktree root.',
            ('  Failed Git step: {0}' -f $Classification.Failure),
            '  This command controls the shared local ports and can replace the canonical AppHost stack.',
            '  No teardown or startup occurred.'
        )
    }

    if ($AllowWorktree) {
        return @(
            'WARNING: this AppHost command is rooted in a linked Git worktree.',
            ('  Script worktree: {0}' -f $Classification.ScriptWorktreeRoot),
            ('  Main worktree:   {0}' -f $Classification.MainWorktreeRoot),
            '  The shared local ports are not isolated; this command can replace the canonical AppHost stack.',
            '  -AllowWorktree explicitly permits this worktree to affect the shared stack.'
        )
    }

    return @(
        'REFUSED: this AppHost command is rooted in a linked Git worktree.',
        ('  Script worktree: {0}' -f $Classification.ScriptWorktreeRoot),
        ('  Main worktree:   {0}' -f $Classification.MainWorktreeRoot),
        '  This command controls the shared local ports and can replace the canonical AppHost stack.',
        ('  Re-run it from {0}.' -f $Classification.MainWorktreeRoot),
        '  To intentionally exercise this worktree against the shared stack, re-run with -AllowWorktree.',
        '  Nothing was killed or started.'
    )
}
