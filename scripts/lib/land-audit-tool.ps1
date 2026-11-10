# The built land-graph audit tool (tools/Antiphon.LandGraphAudit), kept current with its sources.
# ASCII-only and Windows PowerShell 5.1 compatible.
#
# Resolve-LandAuditTool returns @{ Path = <dll> } or @{ Error = <message> }. With -NoRebuild it returns
# @{ Rebuild = $true } where it would rebuild, and changes nothing. An explicit -ToolPath is used as
# given. Otherwise the default bin-land-audit DLL is used when its stamp (land-audit.source-hash beside it)
# equals the SHA-256 of the tool's sources: every .cs and .csproj under tools/Antiphon.LandGraphAudit and
# tools/Antiphon.LandGraphAudit.Core (bin and obj folders excluded) plus the repository's shared build files.
# A missing DLL, a missing stamp (a DLL built by hand or before this file) or a different hash rebuilds it
# once: under a lock file, into a fresh folder with its own artifacts path (never the projects' obj folders),
# and the finished folder replaces the old one only after the build succeeded. A failed build or a folder
# that cannot be replaced is an Error, so the caller refuses; nothing here approves anything. A copy of the
# scripts without the tool's project (a test fixture) keeps whatever DLL it was given.

function Get-LandAuditSourceHash([string]$RepoRoot) {
    $entries = New-Object 'System.Collections.Generic.List[string]'
    foreach ($project in @('tools/Antiphon.LandGraphAudit', 'tools/Antiphon.LandGraphAudit.Core')) {
        $dir = [System.IO.Path]::Combine($RepoRoot, $project)
        if (-not [System.IO.Directory]::Exists($dir)) { continue }
        foreach ($file in [System.IO.Directory]::GetFiles($dir, '*', [System.IO.SearchOption]::AllDirectories)) {
            $rel = $file.Substring($RepoRoot.Length).Replace('\', '/').TrimStart('/')
            if ($rel -notmatch '\.(cs|csproj)$') { continue }
            if (('/' + $rel + '/') -match '/(bin|obj|bin-land-audit)/') { continue }
            [void]$entries.Add($rel)
        }
    }
    foreach ($shared in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json',
            'NuGet.config', 'tools/Directory.Build.props', 'tools/Directory.Build.targets')) {
        if ([System.IO.File]::Exists([System.IO.Path]::Combine($RepoRoot, $shared))) { [void]$entries.Add($shared) }
    }
    $entries.Sort([System.StringComparer]::Ordinal)
    $stream = New-Object System.IO.MemoryStream
    foreach ($rel in $entries) {
        $name = [System.Text.Encoding]::UTF8.GetBytes($rel + "`n")
        $stream.Write($name, 0, $name.Length)
        $bytes = [System.IO.File]::ReadAllBytes([System.IO.Path]::Combine($RepoRoot, $rel))
        $stream.Write($bytes, 0, $bytes.Length)
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [System.BitConverter]::ToString($sha.ComputeHash($stream.ToArray())).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Test-LandAuditToolCurrent([string]$Dll, [string]$Stamp, [string]$Hash) {
    if (-not [System.IO.File]::Exists($Dll) -or -not [System.IO.File]::Exists($Stamp)) { return $false }
    return ([System.IO.File]::ReadAllText($Stamp).Trim() -ceq $Hash)
}

function Resolve-LandAuditTool([string]$RepoRoot, [string]$ToolPath, [switch]$NoRebuild) {
    $RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([char]92, [char]47)
    if (-not [string]::IsNullOrEmpty($ToolPath)) {
        $explicit = [System.IO.Path]::GetFullPath($ToolPath)
        if ([System.IO.File]::Exists($explicit)) { return @{ Path = $explicit } }
        return @{ Error = ('built evaluator missing: ' + $explicit) }
    }
    $root = [System.IO.Path]::Combine($RepoRoot, 'tools', 'Antiphon.LandGraphAudit', 'bin-land-audit')
    $out = [System.IO.Path]::Combine($root, 'Release', 'net10.0')
    $dll = [System.IO.Path]::Combine($out, 'Antiphon.LandGraphAudit.dll')
    $project = [System.IO.Path]::Combine($RepoRoot, 'tools', 'Antiphon.LandGraphAudit', 'Antiphon.LandGraphAudit.csproj')
    if (-not [System.IO.File]::Exists($project)) {
        if ([System.IO.File]::Exists($dll)) { return @{ Path = $dll } }
        return @{ Error = ('built evaluator missing: ' + $dll) }
    }
    $stampName = 'land-audit.source-hash'
    $hash = Get-LandAuditSourceHash $RepoRoot
    if (Test-LandAuditToolCurrent $dll ([System.IO.Path]::Combine($out, $stampName)) $hash) { return @{ Path = $dll } }
    if ($NoRebuild) { return @{ Rebuild = $true } }

    [void][System.IO.Directory]::CreateDirectory($root)
    $lockPath = [System.IO.Path]::Combine($root, 'rebuild.lock')
    $lock = $null
    $deadline = [DateTime]::UtcNow.AddMinutes(15)
    while ($null -eq $lock) {
        try { $lock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None) }
        catch [System.IO.IOException] {
            if ([DateTime]::UtcNow -gt $deadline) { return @{ Error = ('built evaluator missing: ' + $dll + ' (rebuild lock timed out)') } }
            Start-Sleep -Milliseconds 250
        }
    }
    try {
        # Another process may have rebuilt it while this one waited.
        if (Test-LandAuditToolCurrent $dll ([System.IO.Path]::Combine($out, $stampName)) $hash) { return @{ Path = $dll } }
        $why = 'missing'
        if ([System.IO.File]::Exists($dll)) { $why = 'sources changed' }
        [Console]::Error.WriteLine('land audit tool: rebuilding (' + $why + ')')
        $work = [System.IO.Path]::Combine($root, 'build-' + [Guid]::NewGuid().ToString('N'))
        $built = [System.IO.Path]::Combine($work, 'out')
        $log = [System.IO.Path]::Combine($work, 'build.log')
        [void][System.IO.Directory]::CreateDirectory($work)
        try {
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            & dotnet build $project -c Release -o $built --artifacts-path ([System.IO.Path]::Combine($work, 'artifacts')) -nologo -v q '-m:2' '-nodeReuse:false' '-p:UseSharedCompilation=false' > $log 2>&1
            $code = $LASTEXITCODE
            if ($code -ne 0 -or -not [System.IO.File]::Exists([System.IO.Path]::Combine($built, 'Antiphon.LandGraphAudit.dll'))) {
                $tail = @([System.IO.File]::ReadAllLines($log) | Where-Object { $_ -match 'error' } | Select-Object -First 5) -join ' | '
                return @{ Error = ('built evaluator missing: ' + $dll + ' (rebuild failed, exit ' + $code + '): ' + $tail) }
            }
            [System.IO.File]::WriteAllText([System.IO.Path]::Combine($built, $stampName), $hash)
            $old = $null
            if ([System.IO.Directory]::Exists($out)) {
                $old = [System.IO.Path]::Combine($root, 'old-' + [Guid]::NewGuid().ToString('N'))
                try { [System.IO.Directory]::Move($out, $old) }
                catch { return @{ Error = ('built evaluator missing: ' + $dll + ' (the built tool is in use and cannot be replaced: ' + $_.Exception.Message + ')') } }
            }
            [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($out))
            [System.IO.Directory]::Move($built, $out)
            if ($null -ne $old) { try { [System.IO.Directory]::Delete($old, $true) } catch { } }
            [Console]::Error.WriteLine('land audit tool: rebuilt in ' + [int]$watch.Elapsed.TotalSeconds + ' s')
            return @{ Path = $dll }
        } finally {
            if ([System.IO.Directory]::Exists($work)) { try { [System.IO.Directory]::Delete($work, $true) } catch { } }
        }
    } finally {
        $lock.Dispose()
    }
}
