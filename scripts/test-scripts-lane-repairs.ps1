param(
    [ValidateSet('opaque', 'owned', 'origin', 'state', 'replaced')][string]$Case,
    [string]$HostCleanupDll
)
$ErrorActionPreference = 'Stop'
$env:TEMP = [IO.Path]::GetTempPath()
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path ([IO.Path]::GetTempPath()) ('scripts-lane-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    if ($Case -eq 'state') {
        $source = [IO.File]::ReadAllText("$repo/src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs")
        $start = $source.IndexOf('    public void Move(')
        $end = $source.IndexOf('    public void Unlink(', $start)
        if ($start -lt 0 -or $end -lt 0) { throw 'native Move method not found' }
        $method = $source.Substring($start, $end - $start)
        # Compile the exact product method. Only the OS/native File facade is modeled:
        # overwrite-move refuses an open destination; Replace sees a sharing violation once.
        Add-Type -TypeDefinition @"
using System;
using System.IO;
namespace ScriptsLaneWitness {
public sealed class NativeMove {
$method
}
public static class OperatingSystem { public static bool IsWindows() => true; }
public static class File {
    public static int Moves, Replaces;
    public static bool Exists(string path) => System.IO.File.Exists(path);
    public static void Move(string source, string destination, bool overwrite) {
        Moves++;
        if (Exists(destination)) throw new UnauthorizedAccessException("modeled Windows replacement contention");
        System.IO.File.Move(source, destination, overwrite);
    }
    public static void Replace(string source, string destination, string backup) {
        Replaces++;
        if (System.IO.File.ReadAllText(destination) != "complete old state") throw new Exception("old state damaged");
        if (Replaces == 1) throw new IOException("forced sharing violation", unchecked((int)0x80070020));
        System.IO.File.Replace(source, destination, backup);
    }
}
}
"@
        $path = Join-Path $root 'state.json'
        $tmp = Join-Path $root 'state.tmp'
        [IO.File]::WriteAllText($path, 'complete old state')
        [IO.File]::WriteAllText($tmp, 'complete new state')
        $mover = [ScriptsLaneWitness.NativeMove]::new()
        $caught = 0
        for ($attempt = 0; $attempt -lt 6; $attempt++) {
            try { $mover.Move($tmp, $path, $true); break }
            catch {
                $caught++
                if ([IO.File]::ReadAllText($path) -cne 'complete old state') { throw 'partial state during contention' }
                if (-not [IO.File]::Exists($tmp)) { throw 'temporary lost during contention' }
                if ($attempt -eq 5) { throw }
            }
        }
        if ($caught -ne 1 -or [ScriptsLaneWitness.File]::Replaces -ne 2 -or [ScriptsLaneWitness.File]::Moves -ne 0) { throw 'atomic replacement/retry path mismatch' }
        if ([IO.File]::ReadAllText($path) -cne 'complete new state' -or [IO.File]::Exists($tmp)) { throw 'replacement incomplete' }
        # A new destination must still use Move; Replace requires an existing file.
        $fresh = Join-Path $root 'fresh.json'
        $mover.Move($path, $fresh, $true)
        if ([ScriptsLaneWitness.File]::Moves -ne 1 -or [IO.File]::Exists($path)) { throw 'initial publication failed' }
        'PASS state: forced sharing violation, old state intact, atomic replacement retried, initial move'
        return
    }
    if ($Case -eq 'replaced') {
        [void][Reflection.Assembly]::LoadFrom($HostCleanupDll)
        $context = [Antiphon.Checkpoints.CheckpointCleanupDefaults]::Context
        $owned = Join-Path $root 'bin-owned'
        $ownership = [Antiphon.Checkpoints.SafeWrite]::Under($root)
        $reason = [Antiphon.Checkpoints.SafeWrite]::CreateFreshDirectory($owned, $ownership)
        if ($null -ne $reason) { throw "create: $reason" }
        [IO.File]::WriteAllText((Join-Path $owned 'payload.txt'), 'regenerable')
        $reason = [Antiphon.Checkpoints.OwnedOutputLedger]::Seal($owned, [datetimeoffset]::UtcNow)
        if ($null -ne $reason) { throw "seal: $reason" }
        $copy = Join-Path $root 'bin-foreign'
        Copy-Item -LiteralPath $owned -Destination $copy -Recurse
        $ledger = Join-Path $copy '.checkpoint-output-ledger.json'
        if (-not [IO.File]::Exists($ledger)) { throw 'copy dropped the ledger' }
        $foreignInode = (& stat -c %i -- $copy).Trim()
        if ([string]::IsNullOrWhiteSpace($foreignInode)) { throw 'foreign inode unavailable' }
        $aside = Join-Path $root 'bin-owned-real'
        Move-Item -LiteralPath $owned -Destination $aside
        Move-Item -LiteralPath $copy -Destination $owned
        [IO.Directory]::SetLastWriteTime($owned, [datetime]::Now.AddDays(-10))
        [IO.Directory]::SetLastWriteTime($aside, [datetime]::Now.AddDays(-10))
        & "$PSScriptRoot/cleanup-build-junk.ps1" -RepoRoot $root -SkipIfModifiedWithinMinutes 0 -CleanupContext $context
        if (-not [IO.Directory]::Exists($owned)) { throw 'replacement directory was deleted' }
        if (-not [IO.Directory]::Exists($aside)) { throw 'original owned directory was deleted' }
        if ((& stat -c %i -- $owned).Trim() -ne $foreignInode) { throw 'replacement inode was not preserved' }
        if ([IO.File]::ReadAllText((Join-Path $owned 'payload.txt')) -cne 'regenerable') { throw 'replacement payload changed' }
        if ([IO.File]::ReadAllText((Join-Path $aside 'payload.txt')) -cne 'regenerable') { throw 'original payload changed' }
        'PASS replaced: foreign directory survived a stale custody record'
        return
    }
    if ($Case -eq 'origin') {
        . "$PSScriptRoot/lib/nightly-common.ps1"
        & git init --quiet $root
        if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
        # This is a string witness even on Linux: Git quotes and escapes these values.
        foreach ($origin in @('C:\Antiphon\origin.git', 'C:\a space\a"quote\origin.git', '\\server\share\origin.git', 'https://example.invalid/repo')) {
            & git -C $root config --local remote.origin.url $origin
            if ($LASTEXITCODE -ne 0) { throw 'git config failed' }
            $actual = Get-NightlyOriginUrl $root
            if ($actual -cne $origin) { throw "origin mismatch: expected <$origin>, actual <$actual>" }
        }
        & git -C $root config --local --unset remote.origin.url
        if ((Get-NightlyOriginUrl $root) -cne '') { throw 'missing origin was admitted' }
        'PASS origin: 4 exact strings and missing origin'
        return
    }
    $opaque = Join-Path $root 'bin-private/nested/bin-archive/report.md'
    [IO.Directory]::CreateDirectory((Split-Path $opaque -Parent)) | Out-Null
    [IO.File]::WriteAllText($opaque, 'irreplaceable')
    [IO.Directory]::SetLastWriteTime((Join-Path $root 'bin-private'), [datetime]::Now.AddDays(-10))
    $context = $null
    if ($Case -eq 'owned') {
        [void][Reflection.Assembly]::LoadFrom($HostCleanupDll)
        $context = [Antiphon.Checkpoints.CheckpointCleanupDefaults]::Context
        $owned = Join-Path $root 'bin-owned'
        $ownership = [Antiphon.Checkpoints.SafeWrite]::Under($root)
        $reason = [Antiphon.Checkpoints.SafeWrite]::CreateFreshDirectory($owned, $ownership)
        if ($null -ne $reason) { throw "create: $reason" }
        [IO.File]::WriteAllText((Join-Path $owned 'payload.txt'), 'regenerable')
        $reason = [Antiphon.Checkpoints.OwnedOutputLedger]::Seal($owned, [datetimeoffset]::UtcNow)
        if ($null -ne $reason) { throw "seal: $reason" }
    }
    # The old implementation also calls robocopy. Supply a harmless fixture-only stand-in
    # on Linux; Remove-Item still exercises the destructive old path.
    function robocopy { }
    if ($Case -eq 'owned') {
        & "$PSScriptRoot/cleanup-build-junk.ps1" -RepoRoot $root -SkipIfModifiedWithinMinutes 0 -CleanupContext $context
        if ([IO.Directory]::Exists($owned)) { throw 'owned output was retained' }
    } else {
        & "$PSScriptRoot/cleanup-build-junk.ps1" -RepoRoot $root -SkipIfModifiedWithinMinutes 0
    }
    if (-not [IO.File]::Exists($opaque)) { throw 'unowned bin-* bytes were deleted' }
    if ([IO.File]::ReadAllText($opaque) -cne 'irreplaceable') { throw 'unowned bytes changed' }
    "PASS $Case`: unowned preserved" + $(if ($Case -eq 'owned') { ', owned removed' })
} finally {
    [IO.Directory]::Delete($root, $true)
}
