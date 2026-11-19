# Loads the nightly scripts' compiled implementation (scripts/lib/Antiphon.Nightly, with the
# src/Antiphon.IO.WriteBehind library it uses) once per session.
#
# The project is built once per source version into a cache under the user's temp directory, keyed by the
# SHA-256 of every source and project file of both projects plus the PowerShell/.NET versions, so each later
# process only loads it (the script tests start many PowerShell processes). Concurrent first runs take turns
# on a lock file; the build uses its own artifacts directory, never the projects' obj folders, and keeps it
# between source versions, so a changed source rebuilds warm (no restore, only the changed project). Windows
# PowerShell 5.1 cannot load it, a copy of the scripts without the projects or a machine without the dotnet
# SDK cannot build it, and ANTIPHON_NIGHTLY_CORE=off turns it off. Ported code then refuses
# (Assert-NightlyCoreAvailable: nightly-core-required). Only what such a process still runs keeps a PowerShell
# path that returns the same values: nightly-run.ps1 and release-cut.ps1 under Windows PowerShell 5.1 and the
# release selection worker in a script copy (ConvertTo-NightlyCanonicalJson, Get-NightlyFileSha256,
# Get-NightlyParentProcess, Read-NightlyJsonFile, Read-NightlyTrxIdentities).

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
            # no core, without trying, instead of failing a build in every process.
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
    # One lock per project: every source version builds in the same work directory.
    $lockPath = Join-Path $cacheDir ($Identity.Name + '.build.lock')
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
        # A warm build: the work directory (restore, obj and the referenced projects' outputs) stays between source
        # versions, so a changed source restores nothing and recompiles only its own project.
        $work = Join-Path $cacheDir ('work-' + $Identity.Name)
        $out = Join-Path $work ('out-' + $Identity.Hash)
        if ([IO.Directory]::Exists($out)) { [IO.Directory]::Delete($out, $true) }
        $output = & dotnet build $Identity.Project -c Release -o $out --artifacts-path (Join-Path $work 'artifacts') '-p:CopyLocalLockFileAssemblies=true' -nologo -v q '-nodeReuse:false' 2>&1
        if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists((Join-Path $out $Identity.Output))) {
            $tail = @($output | ForEach-Object { [string]$_ } | Where-Object { $_ -match 'error' } | Select-Object -First 5) -join ' | '
            throw ($Identity.Name + ' build failed (exit ' + $LASTEXITCODE + '): ' + $tail)
        }
        [IO.Directory]::Move($out, $target)
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

# Cheap fingerprint of the source files the build hash covers: names, lengths and write times.
# A long-lived session compares this before it trusts a core it already loaded.
function Get-NightlyCoreSourceStamp {
    $projects = @(
        (Join-Path $PSScriptRoot 'Antiphon.Nightly'),
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Antiphon.IO.WriteBehind'))
    )
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($project in $projects) {
        if (-not [IO.Directory]::Exists($project)) { return '' }
        foreach ($file in [IO.Directory]::EnumerateFiles($project, '*', [IO.SearchOption]::AllDirectories)) {
            $rel = $file.Substring($project.Length).Replace([char]92, '/').TrimStart('/')
            if ($rel -match '^(bin|obj)/' -or $rel -notmatch '\.(cs|csproj)$') { continue }
            $info = [IO.FileInfo]::new($file)
            [void]$parts.Add([IO.Path]::GetFileName($project) + '/' + $rel + ':' + $info.Length.ToString() + ':' + $info.LastWriteTimeUtc.Ticks.ToString())
        }
    }
    $parts.Sort([StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $text = [string]::Join("`n", $parts.ToArray())
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-', '')
    } finally { $sha.Dispose() }
}

function Set-NightlyCoreVerified {
    param($Identity)
    $script:NightlyCoreAvailable = $true
    $script:NightlyCoreVerifiedHash = ''
    if ($null -ne $Identity) { $script:NightlyCoreVerifiedHash = [string]$Identity.Hash }
    $script:NightlyCoreVerifiedStamp = Get-NightlyCoreSourceStamp
}

function Test-NightlyCoreAvailable {
    if ($script:NightlyCoreAvailable -eq $false) { return $false }
    if ($script:NightlyCoreAvailable -eq $true) {
        # Dot-sourcing a newer checkout leaves this flag true. Re-check when the source stamp
        # changes and the build hash no longer matches the core this process accepted.
        $stamp = Get-NightlyCoreSourceStamp
        if (-not [string]::IsNullOrWhiteSpace($stamp) -and $stamp -eq [string]$script:NightlyCoreVerifiedStamp -and -not [string]::IsNullOrWhiteSpace([string]$script:NightlyCoreVerifiedHash)) {
            return $true
        }
        $identity = Get-NightlyCoreBuildIdentity
        $hash = ''
        if ($null -ne $identity) { $hash = [string]$identity.Hash }
        if (-not [string]::IsNullOrWhiteSpace($hash) -and $hash -eq [string]$script:NightlyCoreVerifiedHash) {
            $script:NightlyCoreVerifiedStamp = $stamp
            return $true
        }
        Assert-NightlyLoadedCoreIdentity ('Antiphon.Nightly.TestPhaseJson' -as [type])
        $script:NightlyCoreVerifiedHash = $hash
        $script:NightlyCoreVerifiedStamp = $stamp
        return $true
    }
    if ([string]$env:ANTIPHON_NIGHTLY_CORE -ieq 'off') { $script:NightlyCoreAvailable = $false; return $false }
    if ($PSVersionTable.PSVersion.Major -lt 7) { $script:NightlyCoreAvailable = $false; return $false }
    $loaded = 'Antiphon.Nightly.TestPhaseJson' -as [type]
    if ($loaded) {
        Assert-NightlyLoadedCoreIdentity $loaded
        Set-NightlyCoreVerified (Get-NightlyCoreBuildIdentity)
        return $true
    }
    $identity = Get-NightlyCoreBuildIdentity
    if ($null -eq $identity) { $script:NightlyCoreAvailable = $false; return $false }
    try { $dir = Install-NightlyCoreBuild -Identity $identity }
    catch {
        Write-Warning -WarningAction Continue ('nightly-core-unavailable: ' + $_.Exception.Message)
        $script:NightlyCoreAvailable = $false
        return $false
    }
    if ($null -eq $dir) { $script:NightlyCoreAvailable = $false; return $false }
    # Dependencies first (the write-behind library and System.IO.Abstractions), then the core.
    foreach ($dll in @([IO.Directory]::GetFiles($dir, '*.dll') | Sort-Object)) {
        if ([IO.Path]::GetFileName($dll) -ne 'Antiphon.Nightly.dll') { Add-Type -Path $dll }
    }
    Add-Type -Path (Join-Path $dir 'Antiphon.Nightly.dll')
    Assert-NightlyLoadedCoreIdentity ('Antiphon.Nightly.TestPhaseJson' -as [type])
    Set-NightlyCoreVerified $identity
    # Windows would otherwise move this busy background process to efficiency cores within seconds (NightlyProcesses).
    [void][Antiphon.Nightly.NightlyProcesses]::RunAtFullSpeed()
    return $true
}

# A core already in this process must be the build identity that keys the shared cache, and it must
# contain the types a chunk calls. A miss throws before any chunk runs. The flag is not cached on a throw.
function Assert-NightlyLoadedCoreIdentity {
    param($Loaded)
    $required = @(
        'Antiphon.Nightly.TestPhaseJson',
        'Antiphon.Nightly.NightlyCoverageParts',
        'Antiphon.Nightly.EvidenceFiles',
        'Antiphon.Nightly.TestPhaseFiles'
    )
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($name in $required) {
        if (-not ($name -as [type])) { [void]$missing.Add($name.Substring('Antiphon.Nightly.'.Length)) }
    }
    $identity = Get-NightlyCoreBuildIdentity
    if ($null -eq $identity) { throw 'nightly-core-identity-mismatch: required-identity-unavailable; start a fresh pwsh' }
    $cache = Join-Path ([IO.Path]::GetTempPath()) 'antiphon-nightly-core'
    $expectedDll = Join-Path (Join-Path $cache ('Antiphon.Nightly-' + $identity.Hash)) 'Antiphon.Nightly.dll'
    $location = ''
    if ($null -ne $Loaded) {
        try { $location = [IO.Path]::GetFullPath($Loaded.Assembly.Location) } catch { $location = [string]$Loaded.Assembly.Location }
    }
    $expectedFull = $expectedDll
    try { $expectedFull = [IO.Path]::GetFullPath($expectedDll) } catch { }
    $pathOk = [string]::Equals($location, $expectedFull, [StringComparison]::OrdinalIgnoreCase)
    if ($pathOk -and $missing.Count -eq 0) { return }
    $detail = 'nightly-core-identity-mismatch: required=' + $identity.Hash
    if (-not $pathOk) { $detail = $detail + ' loaded=' + $location }
    if ($missing.Count -gt 0) { $detail = $detail + ' missing=' + ($missing -join ',') }
    $detail = $detail + '; start a fresh pwsh'
    throw $detail
}

# ConvertFrom-Json of a file's text ([IO.File]::ReadAllText decoding; -StripBom also drops one leading U+FEFF left
# after decoding). With the core loaded it is parsed from the file's UTF-8 bytes with System.Text.Json into the same
# objects (Antiphon.Nightly/NightlyJson.cs); a document that is not one strict JSON object goes to ConvertFrom-Json
# for its own result or error. -Omit names property paths the caller never reads ('suites[].exclusions': the
# exclusions of every suites element), which the core then never builds; ConvertFrom-Json keeps them.
function Read-NightlyJsonFile([string]$Path, [switch]$StripBom, [string[]]$Omit) {
    if (($script:NightlyCoreAvailable -or (Test-NightlyCoreAvailable)) -and [IO.Path]::IsPathFullyQualified($Path)) {
        $value = [Antiphon.Nightly.NightlyJson]::ReadFile($Path, [bool]$StripBom, [string[]]@($Omit | Where-Object { $_ }))
        if ($null -ne $value) { return $value }
    }
    $text = [IO.File]::ReadAllText($Path)
    if ($StripBom -and $text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) { $text = $text.Substring(1) }
    return $text | ConvertFrom-Json
}

# "$Value | ConvertTo-Json -Depth $Depth | Set-Content -LiteralPath $Path -Encoding UTF8", streamed to the file with
# System.Text.Json (NightlyJson.WriteFile: same content, not the same bytes - non-ASCII text unescaped, shortest
# number forms). A value it does not write (an arbitrary .NET object, nesting past -Depth) or a path that is not
# fully qualified is written by ConvertTo-Json.
function Write-NightlyJsonFile([string]$Path, $Value, [int]$Depth = 8) {
    Assert-NightlyCoreAvailable 'Write-NightlyJsonFile'
    # No ConvertTo-Json fallback (CARD-2005): it builds the whole document as one string and ran out of memory
    # on a full run's summary. A value the core cannot write is an error, and no part-written file is left.
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw ('nightly-json-write-failed path-not-absolute ' + $Path) }
    $reason = ''
    try { if (-not [Antiphon.Nightly.NightlyJson]::WriteFile($Path, $Value, $Depth)) { $reason = 'unsupported-value-or-depth' } }
    catch { $reason = $_.Exception.GetBaseException().GetType().Name }
    if ($reason) {
        try { [IO.File]::Delete($Path) } catch { }
        throw ('nightly-json-write-failed ' + $reason + ' ' + $Path)
    }
}

# Evidence file system (EvidenceFiles in Antiphon.Nightly/EvidenceFiles.cs). Off unless a nightly run turns it
# on (nightly-tests.ps1): then the evidence scripts' reads and writes go through one write-behind file system,
# so writes land on a background thread and this process reads what it wrote. Off, every helper below is the
# plain file call it replaced. ANTIPHON_NIGHTLY_DEFERRED_WRITES=off keeps it off.
if ($null -eq $script:NightlyDeferEvidenceWrites) { $script:NightlyDeferEvidenceWrites = $false }

function Enable-NightlyDeferredEvidenceWrites {
    if ([string]$env:ANTIPHON_NIGHTLY_DEFERRED_WRITES -ieq 'off') { return }
    Assert-NightlyCoreAvailable 'Enable-NightlyDeferredEvidenceWrites'
    [Antiphon.Nightly.EvidenceFiles]::EnableWriteBehind()
    $script:NightlyDeferEvidenceWrites = $true
}

# Code ported to the core keeps no PowerShell copy (2026-11-13): it loads the core here and refuses without it.
function Assert-NightlyCoreAvailable([string]$Caller) {
    if (-not (Test-NightlyCoreAvailable)) { throw ('nightly-core-required ' + $Caller) }
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

# Failures a deferred write already recorded. coverageComplete and testsPassed are false when any remain,
# so the run summary and the returned verdict name them. An empty list leaves both true.
# Flush before the peek: a peek alone can report complete while a failing write is still queued.
function Get-NightlyDeferredEvidenceWriteVerdict {
    if ('Antiphon.Nightly.EvidenceFiles' -as [type]) {
        try { [void][Antiphon.Nightly.EvidenceFiles]::Flush() } catch { }
    }
    $reasons = @()
    if ('Antiphon.Nightly.EvidenceFiles' -as [type]) {
        foreach ($text in @([Antiphon.Nightly.EvidenceFiles]::PeekFailures())) {
            if ([string]::IsNullOrWhiteSpace([string]$text)) { continue }
            $reasons += ('deferred-evidence-write-failed: ' + [string]$text)
        }
    }
    $complete = ($reasons.Count -eq 0)
    return [pscustomobject]@{
        coverageComplete = $complete
        testsPassed = $complete
        reasons = $reasons
    }
}

# Invoke-AntiphonNightlyTests merges this into the summary before summary.json is written.
# A failed verdict forces coverageComplete and testsPassed false and keeps every earlier reason.
function Merge-NightlyDeferredEvidenceVerdict {
    param($Reasons)
    $verdict = Get-NightlyDeferredEvidenceWriteVerdict
    $merged = New-Object System.Collections.Generic.List[string]
    foreach ($reason in @($Reasons)) {
        $text = [string]$reason
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        if (-not $merged.Contains($text)) { [void]$merged.Add($text) }
    }
    foreach ($evidenceWriteReason in @($verdict.reasons)) {
        $text = [string]$evidenceWriteReason
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        if (-not $merged.Contains($text)) { [void]$merged.Add($text) }
    }
    return [pscustomobject]@{
        reasons = @($merged.ToArray())
        coverageComplete = [bool]$verdict.coverageComplete
        testsPassed = [bool]$verdict.testsPassed
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
