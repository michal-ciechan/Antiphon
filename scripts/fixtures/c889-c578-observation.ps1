#requires -Version 5.1
# CARD-0889 shared C578 observation. ASCII-only.
# Dot-sourced by scripts/test-run-checkpoint.ps1 and by the C578 dotnet shim.

if ($script:C889ObservationLoaded) { return }
$script:C889ObservationLoaded = $true

$script:C889GuardSeconds = 300

function Initialize-C889WatchType {
    if ('Antiphon.C889Watch' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Antiphon {
    public static class C889Watch {
        public static Task StartWatcher(FileSystemWatcher watcher, ManualResetEventSlim signal) {
            return Task.Run(() => {
                try {
                    while (true) {
                        watcher.WaitForChanged(WatcherChangeTypes.All, 1000);
                        try { signal.Set(); } catch { return; }
                    }
                } catch {
                    try { signal.Set(); } catch { }
                }
            });
        }

        public static Task StartProcess(Process process, ManualResetEventSlim signal) {
            return Task.Run(() => {
                try {
                    if (process != null && !process.HasExited) process.WaitForExit();
                } catch { }
                try { signal.Set(); } catch { }
            });
        }
    }
}
'@
}

function New-C889Descriptor {
    param([string]$Directory, [string]$Nonce, [string]$Kind)
    if (-not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText((Join-Path $Directory 'nonce'), $Nonce)
    [System.IO.File]::WriteAllText((Join-Path $Directory 'kind'), $Kind)
    return [pscustomobject]@{
        Directory = $Directory
        Nonce = $Nonce
        Kind = $Kind
        Watch = [System.Diagnostics.Stopwatch]::StartNew()
        Subscriptions = New-Object System.Collections.Generic.List[object]
        IgnoreCancel = $false
        HoldPhase = ''
    }
}

function Register-C889DirectoryWatch {
    param($Descriptor, [string]$Directory, $Signal)
    Initialize-C889WatchType
    if (-not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    }
    $watcher = [System.IO.FileSystemWatcher]::new($Directory)
    $watcher.Filter = '*'
    $watcher.IncludeSubdirectories = $true
    $watcher.NotifyFilter = [System.IO.NotifyFilters]'FileName, LastWrite, Size, CreationTime'
    $watcher.EnableRaisingEvents = $true
    $Descriptor.Subscriptions.Add($watcher)
    [void][Antiphon.C889Watch]::StartWatcher($watcher, $Signal)
}

function Clear-C889Subscriptions {
    param($Descriptor)
    if (-not $Descriptor -or -not $Descriptor.Subscriptions) { return }
    $items = @()
    try { $items = @($Descriptor.Subscriptions.ToArray()) } catch { $items = @() }
    foreach ($item in $items) {
        try { $item.Dispose() } catch { }
    }
    try { $Descriptor.Subscriptions.Clear() } catch { }
}

function Get-C889ElapsedSeconds {
    param($State)
    if ($null -eq $State -or $null -eq $State.ElapsedSeconds) { return 0 }
    return [double]$State.ElapsedSeconds
}

function Get-C889ReleaseDecision {
    param([bool]$ReleasePresent)
    if ($ReleasePresent) { return 'Released' }
    return 'Pending'
}

function Get-C889Decision {
    param($State)
    if ($State.Unreadable) { return 'Pending' }
    if ($State.CancelPresent) { return 'Cancelled' }
    $elapsed = Get-C889ElapsedSeconds $State
    if ($State.Standalone -and $elapsed -ge 300) { return 'Cancelled' }
    if ($State.ChildExited) { return 'PrematureExit' }
    if ($null -ne $State.IdentityMatches -and -not [bool]$State.IdentityMatches) { return 'IdentityMismatch' }

    $stdoutMarker = [bool]$State.StdoutMarker
    $stderrMarker = [bool]$State.StderrMarker
    $stdoutFence = [bool]$State.StdoutFence
    $stderrFence = [bool]$State.StderrFence
    if (($stdoutFence -and $stderrFence) -and (-not $stdoutMarker -or -not $stderrMarker)) {
        return 'MarkersMissing'
    }

    $ready = [bool]$State.ReadyPresent -and $stdoutMarker -and $stderrMarker -and $stdoutFence -and $stderrFence -and
        [bool]$State.PhaseMatches -and [bool]$State.NonceMatches
    if ($ready -and ($null -eq $State.IdentityMatches -or [bool]$State.IdentityMatches)) {
        return 'ObservedReady'
    }
    return 'Pending'
}

function Get-C889HeldDecision {
    param($State)
    return Get-C889Decision $State
}

function Invoke-C889Cycle {
    param([bool]$ReleasePresent, $ReadyState)
    $decision = 'Pending'
    $release = Get-C889ReleaseDecision -ReleasePresent $ReleasePresent
    if ($release -eq 'Released') { return 'Released' }
    $decision = Get-C889Decision $ReadyState
    if ($decision -ne 'Pending') { return $decision }
    return 'Pending'
}

function Copy-C889State {
    param($Base, $Overlay)
    $copy = @{}
    foreach ($key in @($Base.Keys)) { $copy[$key] = $Base[$key] }
    if ($Overlay) {
        foreach ($key in @($Overlay.Keys)) { $copy[$key] = $Overlay[$key] }
    }
    return $copy
}

function Invoke-C889LogicalProof {
    $baseline = @{
        CancelPresent = $false
        Standalone = $false
        ElapsedSeconds = 11
        ChildExited = $false
        IdentityMatches = $true
        ReadyPresent = $true
        StdoutMarker = $true
        StderrMarker = $true
        StdoutFence = $true
        StderrFence = $true
        PhaseMatches = $true
        NonceMatches = $true
        Unreadable = $false
    }
    $quiet = @{
        CancelPresent = $false
        Standalone = $false
        ElapsedSeconds = 0
        ChildExited = $false
        IdentityMatches = $true
        ReadyPresent = $false
        StdoutMarker = $false
        StderrMarker = $false
        StdoutFence = $false
        StderrFence = $false
        PhaseMatches = $false
        NonceMatches = $false
        Unreadable = $false
    }
    $l1 = Get-C889Decision (Copy-C889State $baseline $null)
    $l2 = Get-C889Decision (Copy-C889State $baseline @{ ChildExited = $true; ElapsedSeconds = 0 })
    $l3 = Get-C889Decision (Copy-C889State $baseline @{ IdentityMatches = $false; ElapsedSeconds = 0 })
    $l6 = Get-C889Decision (Copy-C889State $baseline @{ StderrFence = $false; ElapsedSeconds = 0 })
    $l7 = Get-C889Decision (Copy-C889State $baseline @{ StderrMarker = $false; ElapsedSeconds = 0 })
    $l8 = Get-C889Decision (Copy-C889State $baseline @{ CancelPresent = $true; ElapsedSeconds = 0 })
    $l9 = Get-C889Decision (Copy-C889State $quiet @{ Standalone = $true; ElapsedSeconds = 300 })
    $l10 = Get-C889Decision (Copy-C889State $quiet @{ Unreadable = $true })
    $l4 = Invoke-C889Cycle -ReleasePresent $false -ReadyState (Copy-C889State $baseline @{ ElapsedSeconds = 0 })
    $l5 = Invoke-C889Cycle -ReleasePresent $true -ReadyState $quiet
    $heldOk = ($l2 -eq 'PrematureExit') -and ($l3 -eq 'IdentityMismatch') -and ($l5 -eq 'Released') -and
        ($l6 -eq 'Pending') -and ($l7 -eq 'MarkersMissing') -and ($l8 -eq 'Cancelled') -and
        ($l9 -eq 'Cancelled') -and ($l10 -eq 'Pending')
    $lateOk = ($l1 -eq 'ObservedReady') -and ($l4 -eq 'ObservedReady')
    Assert-C487 -Cond $heldOk -Name 'C578 c578-child-held-until-observed' -Detail ("L2=$l2 L3=$l3 L5=$l5 L6=$l6 L7=$l7 L8=$l8 L9=$l9 L10=$l10")
    Assert-C487 -Cond $lateOk -Name 'C578 c578-late-ready-event-accepted' -Detail ("L1=$l1 L4=$l4")
}

function Wait-C889Signal {
    param($Descriptor, $Signal, [scriptblock]$Decide)
    while ($true) {
        $decision = & $Decide
        if ($decision.Terminal) { return $decision }
        $elapsed = $Descriptor.Watch.Elapsed.TotalSeconds
        if ($elapsed -ge $script:C889GuardSeconds) {
            $guard = @{
                CancelPresent = $false
                Standalone = ($Descriptor.Kind -eq 'standalone')
                ElapsedSeconds = $elapsed
                ChildExited = $false
                IdentityMatches = $true
                ReadyPresent = $false
                StdoutMarker = $false
                StderrMarker = $false
                StdoutFence = $false
                StderrFence = $false
                PhaseMatches = $false
                NonceMatches = $false
                Unreadable = $false
            }
            $guardName = Get-C889Decision $guard
            if ($guardName -eq 'Cancelled') {
                $cancelPath = Join-Path $Descriptor.Directory 'cancel'
                if (-not (Test-Path -LiteralPath $cancelPath)) {
                    [System.IO.File]::WriteAllText($cancelPath, $Descriptor.Nonce)
                }
                return [pscustomobject]@{ Terminal = $true; Name = 'Cancelled'; Reason = 'guard' }
            }
            return [pscustomobject]@{ Terminal = $true; Name = 'GuardExpired'; Reason = 'guard' }
        }
        $Signal.Reset()
        $again = & $Decide
        if ($again.Terminal) { return $again }
        $remaining = [int][Math]::Max(1, ($script:C889GuardSeconds - $Descriptor.Watch.Elapsed.TotalSeconds) * 1000)
        [void]$Signal.Wait($remaining)
    }
}

function Add-C889JournalLine {
    param($Descriptor, [string]$Role, [int]$ProcessId, [long]$StartTicks)
    if (-not $Descriptor) { return }
    $path = Join-Path $Descriptor.Directory 'identity.journal'
    $prefix = $Role + '|'
    $existing = ''
    if (Test-Path -LiteralPath $path) { $existing = [System.IO.File]::ReadAllText($path) }
    if ($existing.Contains($prefix)) { return }
    $line = '{0}|{1}|{2}' -f $Role, $ProcessId, $StartTicks
    [System.IO.File]::AppendAllText($path, $line + "`n")
}

function Write-C889Ack {
    param($Descriptor, [System.Collections.IDictionary]$Fields)
    $names = @('nonce', 'phase', 'parentObserved', 'shimExit', 'cleanup', 'descriptor', 'wrapperFirst', 'subscriptions')
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($name in $names) {
        if ($Fields.Contains($name) -and $null -ne $Fields[$name] -and [string]$Fields[$name] -ne '') {
            $lines.Add(('{0}={1}' -f $name, [string]$Fields[$name]))
        }
    }
    $text = ($lines -join "`n") + "`n"
    $path = Join-Path $Descriptor.Directory 'phase.ack'
    $temp = $path + '.tmp'
    [System.IO.File]::WriteAllText($temp, $text)
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    [System.IO.File]::Move($temp, $path)
}

function Publish-C889Sentinels {
    param($Descriptor)
    if (-not $Descriptor) { return }
    foreach ($pair in @(@('stdout', [Console]::Out), @('stderr', [Console]::Error))) {
        $path = Join-Path $Descriptor.Directory ($pair[0] + '.sentinel')
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $writer = $pair[1]
        foreach ($line in [System.IO.File]::ReadAllLines($path)) {
            $writer.WriteLine($line)
        }
        $writer.Flush()
    }
}

function Write-C889SentinelLine {
    param([string]$DescriptorDir, [string]$Stream, [string]$Text)
    if ($DescriptorDir) {
        $path = Join-Path $DescriptorDir ($Stream + '.sentinel')
        [System.IO.File]::AppendAllText($path, $Text + "`n")
    }
    try {
        if ($Stream -eq 'stderr') {
            [Console]::Error.WriteLine($Text)
            [Console]::Error.Flush()
        } else {
            [Console]::Out.WriteLine($Text)
            [Console]::Out.Flush()
        }
    } catch { }
}

function Test-C889CancelFile {
    param($Descriptor, [bool]$IgnoreCancel)
    if ($IgnoreCancel -or -not $Descriptor) { return $false }
    $path = Join-Path $Descriptor.Directory 'cancel'
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $content = ([System.IO.File]::ReadAllText($path)).Trim()
    return $content -eq $Descriptor.Nonce
}

function Wait-C889ShimHold {
    param(
        [string]$Root,
        [string]$Phase,
        [string]$DescriptorDir,
        [bool]$IgnoreCancel,
        [bool]$Standalone
    )
    $nonce = ''
    $kind = 'caller'
    if ($Standalone) { $kind = 'standalone' }
    if ($DescriptorDir -and (Test-Path -LiteralPath (Join-Path $DescriptorDir 'nonce'))) {
        $nonce = ([System.IO.File]::ReadAllText((Join-Path $DescriptorDir 'nonce'))).Trim()
    }
    $desc = [pscustomobject]@{
        Directory = $(if ($DescriptorDir) { $DescriptorDir } else { $Root })
        Nonce = $nonce
        Kind = $kind
        Watch = [System.Diagnostics.Stopwatch]::StartNew()
        Subscriptions = New-Object System.Collections.Generic.List[object]
        IgnoreCancel = $IgnoreCancel
        HoldPhase = ''
    }
    $signal = [System.Threading.ManualResetEventSlim]::new($false)
    Register-C889DirectoryWatch -Descriptor $desc -Directory $Root -Signal $signal
    if ($DescriptorDir) {
        Register-C889DirectoryWatch -Descriptor $desc -Directory $DescriptorDir -Signal $signal
    }
    $release = Join-Path $Root ($Phase + '.release')
    $stopped = ''
    if ($DescriptorDir) { $stopped = Join-Path $DescriptorDir 'wrapper.stopped' }
    try {
        $outcome = Wait-C889Signal -Descriptor $desc -Signal $signal -Decide {
            $cancel = $false
            if ($DescriptorDir) { $cancel = Test-C889CancelFile -Descriptor $desc -IgnoreCancel $IgnoreCancel }
            $wrapperStopped = $stopped -and (Test-Path -LiteralPath $stopped)
            $released = Test-Path -LiteralPath $release
            $releaseName = Get-C889ReleaseDecision -ReleasePresent $released
            if ($releaseName -eq 'Released') {
                return [pscustomobject]@{ Terminal = $true; Name = 'Released' }
            }
            $state = @{
                CancelPresent = ($cancel -and $wrapperStopped)
                Standalone = $Standalone
                ElapsedSeconds = $desc.Watch.Elapsed.TotalSeconds
                ChildExited = $false
                IdentityMatches = $true
                ReadyPresent = $false
                StdoutMarker = $false
                StderrMarker = $false
                StdoutFence = $false
                StderrFence = $false
                PhaseMatches = $false
                NonceMatches = $false
                Unreadable = $false
            }
            $name = Get-C889Decision $state
            $terminal = $name -eq 'Cancelled'
            return [pscustomobject]@{ Terminal = $terminal; Name = $(if ($terminal) { $name } else { 'Pending' }) }
        }
        return [string]$outcome.Name
    } finally {
        Clear-C889Subscriptions $desc
        $signal.Dispose()
    }
}
