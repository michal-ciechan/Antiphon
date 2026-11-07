# Loads NightlyCore.cs (compiled per-test work for the nightly scripts) once per session.
#
# The assembly is compiled once per source version and cached under the user's temp directory, keyed by the
# SHA-256 of the source and the PowerShell/.NET versions, so each later process only loads it (the script
# tests start many PowerShell processes). Windows PowerShell 5.1 cannot compile this C#, and
# ANTIPHON_NIGHTLY_CORE=off turns it off: callers then keep their PowerShell implementation, which returns
# the same values.

function Test-NightlyCoreAvailable {
    if ($null -ne $script:NightlyCoreAvailable) { return $script:NightlyCoreAvailable }
    $script:NightlyCoreAvailable = $false
    if ([string]$env:ANTIPHON_NIGHTLY_CORE -ieq 'off') { return $false }
    if ($PSVersionTable.PSVersion.Major -lt 7) { return $false }
    if ('Antiphon.Nightly.TestPhaseJson' -as [type]) { $script:NightlyCoreAvailable = $true; return $true }
    $source = Join-Path $PSScriptRoot 'NightlyCore.cs'
    $text = [IO.File]::ReadAllText($source)
    $identity = $text + "`n" + $PSVersionTable.PSVersion.ToString() + "`n" + [Environment]::Version.ToString()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))).Replace('-', '').Substring(0, 24) }
    finally { $sha.Dispose() }
    $cacheDir = Join-Path ([IO.Path]::GetTempPath()) 'antiphon-nightly-core'
    $dll = Join-Path $cacheDir ('NightlyCore-' + $hash + '.dll')
    if (-not [IO.File]::Exists($dll)) {
        [void][IO.Directory]::CreateDirectory($cacheDir)
        $tmp = Join-Path $cacheDir ('NightlyCore-' + $hash + '.' + [guid]::NewGuid().ToString('N') + '.tmp.dll')
        Add-Type -TypeDefinition $text -OutputAssembly $tmp
        # Concurrent first runs race to publish the same bytes; whichever lands first is used.
        try { [IO.File]::Move($tmp, $dll) }
        catch [IO.IOException] { if (-not [IO.File]::Exists($dll)) { throw } }
        finally { if ([IO.File]::Exists($tmp)) { try { [IO.File]::Delete($tmp) } catch [IO.IOException] { } catch [UnauthorizedAccessException] { } } }
        # Builds of earlier source versions: best effort, a week old (one still loaded elsewhere stays).
        foreach ($old in @([IO.Directory]::GetFiles($cacheDir, 'NightlyCore-*.dll'))) {
            if ($old -eq $dll -or [IO.File]::GetLastWriteTimeUtc($old) -gt [DateTime]::UtcNow.AddDays(-7)) { continue }
            try { [IO.File]::Delete($old) } catch [IO.IOException] { } catch [UnauthorizedAccessException] { }
        }
    }
    Add-Type -Path $dll
    $script:NightlyCoreAvailable = $true
    return $true
}
