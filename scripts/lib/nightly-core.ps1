# Loads the nightly scripts' compiled implementation (scripts/lib/Antiphon.Nightly, with the
# src/Antiphon.IO.WriteBehind library it uses) once per session.
#
# The project is built once per source version into a cache under the user's temp directory, keyed by the
# SHA-256 of every source and project file of both projects plus the PowerShell/.NET versions, so each later
# process only loads it (the script tests start many PowerShell processes). Concurrent first runs take turns
# on a lock file; the build uses its own artifacts directory, never the projects' obj folders. Windows
# PowerShell 5.1 cannot load it, a copy of the scripts without the projects or a machine without the dotnet
# SDK cannot build it, and ANTIPHON_NIGHTLY_CORE=off turns it off: callers then keep their PowerShell
# implementation, which returns the same values.

function Get-NightlyCoreBuildIdentity {
    $projects = @(
        (Join-Path $PSScriptRoot 'Antiphon.Nightly'),
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Antiphon.IO.WriteBehind'))
    )
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = New-Object System.IO.MemoryStream
        foreach ($project in $projects) {
            if (-not [IO.Directory]::Exists($project)) { return $null }
            $files = New-Object System.Collections.Generic.List[string]
            foreach ($file in [IO.Directory]::EnumerateFiles($project, '*', [IO.SearchOption]::AllDirectories)) {
                $rel = $file.Substring($project.Length).Replace([char]92, '/').TrimStart('/')
                if ($rel -match '^(bin|obj)/' -or $rel -notmatch '\.(cs|csproj)$') { continue }
                $files.Add($rel)
            }
            # A project file without its source (a fixture's census copy of every .csproj) cannot build:
            # use the PowerShell fallback without trying, instead of failing a build in every process.
            if (@($files | Where-Object { $_.EndsWith('.cs') }).Count -eq 0) { return $null }
            $files.Sort([StringComparer]::Ordinal)
            foreach ($rel in $files) {
                $name = [Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFileName($project) + '/' + $rel + "`n")
                $stream.Write($name, 0, $name.Length)
                $bytes = [IO.File]::ReadAllBytes((Join-Path $project $rel))
                $stream.Write($bytes, 0, $bytes.Length)
            }
        }
        $runtime = [Text.Encoding]::UTF8.GetBytes("`n" + $PSVersionTable.PSVersion.ToString() + "`n" + [Environment]::Version.ToString())
        $stream.Write($runtime, 0, $runtime.Length)
        $hash = [BitConverter]::ToString($sha.ComputeHash($stream.ToArray())).Replace('-', '').Substring(0, 24)
    } finally { $sha.Dispose() }
    return [pscustomobject]@{ Hash = $hash; Project = (Join-Path $projects[0] 'Antiphon.Nightly.csproj'); Name = 'Antiphon.Nightly'; Output = 'Antiphon.Nightly.dll' }
}

function Install-NightlyCoreBuild {
    param($Identity)
    $cacheDir = Join-Path ([IO.Path]::GetTempPath()) 'antiphon-nightly-core'
    $target = Join-Path $cacheDir ($Identity.Name + '-' + $Identity.Hash)
    if ([IO.File]::Exists((Join-Path $target $Identity.Output))) { return $target }
    if ($null -eq (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) { return $null }
    [void][IO.Directory]::CreateDirectory($cacheDir)
    $lockPath = Join-Path $cacheDir ($Identity.Name + '-' + $Identity.Hash + '.lock')
    $lock = $null
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while ($null -eq $lock) {
        try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'nightly-core build lock timed out' }
            Start-Sleep -Milliseconds 200
        }
    }
    try {
        # Another process may have built it while this one waited.
        if ([IO.File]::Exists((Join-Path $target $Identity.Output))) { return $target }
        $work = Join-Path $cacheDir ('build-' + [guid]::NewGuid().ToString('N'))
        try {
            $out = Join-Path $work 'out'
            $output = & dotnet build $Identity.Project -c Release -o $out --artifacts-path (Join-Path $work 'artifacts') '-p:CopyLocalLockFileAssemblies=true' -nologo -v q '-nodeReuse:false' 2>&1
            if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists((Join-Path $out $Identity.Output))) {
                $tail = @($output | ForEach-Object { [string]$_ } | Where-Object { $_ -match 'error' } | Select-Object -First 5) -join ' | '
                throw ($Identity.Name + ' build failed (exit ' + $LASTEXITCODE + '): ' + $tail)
            }
            [IO.Directory]::Move($out, $target)
        } finally {
            if ([IO.Directory]::Exists($work)) { try { [IO.Directory]::Delete($work, $true) } catch { } }
        }
        # Builds of earlier source versions: best effort, a week old (one still loaded elsewhere stays).
        foreach ($old in @([IO.Directory]::GetDirectories($cacheDir, $Identity.Name + '-*'))) {
            if ($old -eq $target -or [IO.Directory]::GetLastWriteTimeUtc($old) -gt [DateTime]::UtcNow.AddDays(-7)) { continue }
            try { [IO.Directory]::Delete($old, $true) } catch { }
        }
        foreach ($old in @([IO.Directory]::GetFiles($cacheDir, 'NightlyCore-*.dll'))) {
            try { [IO.File]::Delete($old) } catch { }
        }
        return $target
    } finally {
        $lock.Dispose()
        try { [IO.File]::Delete($lockPath) } catch { }
    }
}

# The scripts' command-line implementation (scripts/lib/Antiphon.Scripts): built the same way, keyed by its
# own sources plus the shared CheckpointProcessRunner.cs and the .NET version. Returns the command to start
# (the built executable), or $null when it cannot be had: Windows PowerShell 5.1, ANTIPHON_SCRIPTS_CLI=off,
# a copy of the scripts without the project, no SDK or a failed build (warned). Callers then keep their
# PowerShell path; a command that cannot answer an input exactly also tells them to (exit 10).
function Get-AntiphonScriptsCli {
    if ($null -ne $script:AntiphonScriptsCli) { if ($script:AntiphonScriptsCli) { return $script:AntiphonScriptsCli }; return $null }
    $script:AntiphonScriptsCli = ''
    if ([string]$env:ANTIPHON_SCRIPTS_CLI -ieq 'off' -or $PSVersionTable.PSVersion.Major -lt 7) { return $null }
    $project = Join-Path $PSScriptRoot 'Antiphon.Scripts'
    $shared = Join-Path $PSScriptRoot 'CheckpointProcessRunner.cs'
    if (-not [IO.Directory]::Exists($project) -or -not [IO.File]::Exists($shared)) { return $null }
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($file in [IO.Directory]::EnumerateFiles($project, '*', [IO.SearchOption]::AllDirectories)) {
        $rel = $file.Substring($project.Length).Replace([char]92, '/').TrimStart('/')
        if ($rel -match '^(bin|obj)/' -or $rel -notmatch '\.(cs|csproj)$') { continue }
        $files.Add($rel)
    }
    if (@($files | Where-Object { $_.EndsWith('.cs') }).Count -eq 0) { return $null }
    $files.Sort([StringComparer]::Ordinal)
    $stream = New-Object System.IO.MemoryStream
    foreach ($entry in @($files | ForEach-Object { @{ Name = 'Antiphon.Scripts/' + $_; Path = (Join-Path $project $_) } }) + @(@{ Name = 'CheckpointProcessRunner.cs'; Path = $shared })) {
        $name = [Text.Encoding]::UTF8.GetBytes($entry.Name + "`n")
        $stream.Write($name, 0, $name.Length)
        $bytes = [IO.File]::ReadAllBytes($entry.Path)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    $runtime = [Text.Encoding]::UTF8.GetBytes("`n" + [Environment]::Version.ToString())
    $stream.Write($runtime, 0, $runtime.Length)
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream.ToArray())).Substring(0, 24)
    $identity = [pscustomobject]@{ Hash = $hash; Project = (Join-Path $project 'Antiphon.Scripts.csproj'); Name = 'Antiphon.Scripts'; Output = 'Antiphon.Scripts.dll' }
    try { $dir = Install-NightlyCoreBuild -Identity $identity }
    catch {
        Write-Warning -WarningAction Continue ('antiphon-scripts-unavailable: ' + $_.Exception.Message)
        return $null
    }
    if ($null -eq $dir) { return $null }
    $exe = Join-Path $dir 'Antiphon.Scripts'
    if ($IsWindows) { $exe += '.exe' }
    if (-not [IO.File]::Exists($exe)) { return $null }
    $script:AntiphonScriptsCli = $exe
    return $exe
}

function Test-NightlyCoreAvailable {
    if ($null -ne $script:NightlyCoreAvailable) { return $script:NightlyCoreAvailable }
    $script:NightlyCoreAvailable = $false
    if ([string]$env:ANTIPHON_NIGHTLY_CORE -ieq 'off') { return $false }
    if ($PSVersionTable.PSVersion.Major -lt 7) { return $false }
    if ('Antiphon.Nightly.TestPhaseJson' -as [type]) { $script:NightlyCoreAvailable = $true; return $true }
    $identity = Get-NightlyCoreBuildIdentity
    if ($null -eq $identity) { return $false }
    try { $dir = Install-NightlyCoreBuild -Identity $identity }
    catch {
        Write-Warning -WarningAction Continue ('nightly-core-unavailable: ' + $_.Exception.Message)
        return $false
    }
    if ($null -eq $dir) { return $false }
    # Dependencies first (the write-behind library and System.IO.Abstractions), then the core.
    foreach ($dll in @([IO.Directory]::GetFiles($dir, '*.dll') | Sort-Object)) {
        if ([IO.Path]::GetFileName($dll) -ne 'Antiphon.Nightly.dll') { Add-Type -Path $dll }
    }
    Add-Type -Path (Join-Path $dir 'Antiphon.Nightly.dll')
    $script:NightlyCoreAvailable = $true
    return $true
}

# Evidence file system (EvidenceFiles in Antiphon.Nightly/EvidenceFiles.cs). Off unless a nightly run turns it
# on (nightly-tests.ps1): then the evidence scripts' reads and writes go through one write-behind file system,
# so writes land on a background thread and this process reads what it wrote. Off, every helper below is the
# plain file call it replaced. ANTIPHON_NIGHTLY_DEFERRED_WRITES=off keeps it off.
if ($null -eq $script:NightlyDeferEvidenceWrites) { $script:NightlyDeferEvidenceWrites = $false }

function Enable-NightlyDeferredEvidenceWrites {
    if ([string]$env:ANTIPHON_NIGHTLY_DEFERRED_WRITES -ieq 'off') { return }
    if (-not (Test-NightlyCoreAvailable)) { return }
    [Antiphon.Nightly.EvidenceFiles]::EnableWriteBehind()
    $script:NightlyDeferEvidenceWrites = $true
}

function Disable-NightlyDeferredEvidenceWrites {
    if (-not $script:NightlyDeferEvidenceWrites) { return }
    $script:NightlyDeferEvidenceWrites = $false
    Write-NightlyEvidenceFailures ([Antiphon.Nightly.EvidenceFiles]::DisableWriteBehind())
}

function Write-NightlyEvidenceFailures([string[]]$Failures) {
    foreach ($failure in @($Failures)) {
        Write-Warning -WarningAction Continue ('deferred-evidence-write-failed: ' + $failure)
    }
}

# Queues an atomic whole-file write when the evidence file system is on and returns $true; $false means the
# caller writes it in line. SkipLinks: an existing directory or link is left alone.
function Submit-NightlyDeferredEvidenceWrite([string]$Path, [string]$Text, [bool]$SkipLinks) {
    if (-not $script:NightlyDeferEvidenceWrites) { return $false }
    [Antiphon.Nightly.EvidenceFiles]::WriteAtomic($Path, $Text, $SkipLinks)
    return $true
}

# Barrier before anything that reads the disk directly (a listing, a copy, a stream, another process): waits
# for the queued writes (of -Path only, when given).
function Sync-NightlyDeferredEvidenceWrites([string]$Path = '') {
    if (-not $script:NightlyDeferEvidenceWrites) { return }
    if ($Path) { Write-NightlyEvidenceFailures ([Antiphon.Nightly.EvidenceFiles]::Flush($Path)) }
    else { Write-NightlyEvidenceFailures ([Antiphon.Nightly.EvidenceFiles]::Flush()) }
}

# Test-Path -LiteralPath [-PathType Leaf|Container], seeing queued writes.
function Test-NightlyEvidencePath([string]$Path, [string]$PathType = 'Any') {
    if ($script:NightlyDeferEvidenceWrites) {
        if ($PathType -eq 'Leaf') { return [Antiphon.Nightly.EvidenceFiles]::FileExists($Path) }
        if ($PathType -eq 'Container') { return [Antiphon.Nightly.EvidenceFiles]::DirectoryExists($Path) }
        return [Antiphon.Nightly.EvidenceFiles]::PathExists($Path)
    }
    return (Test-Path -LiteralPath $Path -PathType $PathType)
}

# [IO.File]::ReadAllText / ReadAllBytes / ReadAllLines, seeing queued writes.
function Read-NightlyEvidenceText([string]$Path) {
    if ($script:NightlyDeferEvidenceWrites) { return [Antiphon.Nightly.EvidenceFiles]::ReadAllText($Path) }
    return [System.IO.File]::ReadAllText($Path)
}

function Read-NightlyEvidenceBytes([string]$Path) {
    if ($script:NightlyDeferEvidenceWrites) { return ,[Antiphon.Nightly.EvidenceFiles]::ReadAllBytes($Path) }
    return ,[System.IO.File]::ReadAllBytes($Path)
}

function Read-NightlyEvidenceLines([string]$Path) {
    if ($script:NightlyDeferEvidenceWrites) { return [Antiphon.Nightly.EvidenceFiles]::ReadAllLines($Path) }
    return [System.IO.File]::ReadAllLines($Path)
}

# Bytes of the path's queued write that has not landed yet, or -1.
function Get-NightlyEvidencePendingLength([string]$Path) {
    if (-not $script:NightlyDeferEvidenceWrites) { return -1 }
    return [Antiphon.Nightly.EvidenceFiles]::PendingLength($Path)
}

# [IO.File]::WriteAllText / WriteAllLines with UTF-8 and no byte-order mark, queued when the evidence file
# system is on.
function Write-NightlyEvidenceText([string]$Path, [string]$Text) {
    if ($script:NightlyDeferEvidenceWrites) { [Antiphon.Nightly.EvidenceFiles]::WriteAllText($Path, $Text); return }
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding $false))
}

function Write-NightlyEvidenceLines([string]$Path, [string[]]$Lines) {
    if ($script:NightlyDeferEvidenceWrites) { [Antiphon.Nightly.EvidenceFiles]::WriteAllLines($Path, $Lines); return }
    [System.IO.File]::WriteAllLines($Path, $Lines, (New-Object System.Text.UTF8Encoding $false))
}
