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
# (Assert-NightlyCoreAvailable: nightly-core-required). The entry points (release-cut.ps1, publish-release.ps1,
# promote-release.ps1) run under pwsh and require the core.
# W2-nightly S27: the loader, the scripts CLI build and the refusal text are what the remaining PowerShell calls; the
# evidence file system facade and the JSON file helpers had no caller left and are gone (EvidenceFiles, NightlyJson).

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
    # One lock for every project: every source version builds in the same bin/ and obj/, and the CLI's build also
    # builds the core project it references.
    $lockPath = Join-Path $cacheDir 'build.lock'
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
        # A standard build into the project's own bin/ and obj/, which stay between source versions: a changed source
        # restores nothing and recompiles only its own project. The output is copied to this version's cache folder
        # and loaded from there, because a loaded assembly is locked on Windows and the next build must still be able
        # to write bin/.
        $output = & dotnet build $Identity.Project -c Release '-p:CopyLocalLockFileAssemblies=true' -nologo -v q '-nodeReuse:false' 2>&1
        $built = $null
        $release = Join-Path (Split-Path -Parent $Identity.Project) 'bin/Release'
        if ([IO.Directory]::Exists($release)) {
            foreach ($tfm in @([IO.Directory]::GetDirectories($release))) {
                if ([IO.File]::Exists((Join-Path $tfm $Identity.Output))) { $built = $tfm }
            }
        }
        if ($LASTEXITCODE -ne 0 -or $null -eq $built) {
            $tail = @($output | ForEach-Object { [string]$_ } | Where-Object { $_ -match 'error' } | Select-Object -First 5) -join ' | '
            throw ($Identity.Name + ' build failed (exit ' + $LASTEXITCODE + '): ' + $tail)
        }
        $staging = Join-Path $cacheDir ('staging-' + $Identity.Name + '-' + $Identity.Hash)
        if ([IO.Directory]::Exists($staging)) { [IO.Directory]::Delete($staging, $true) }
        Copy-Item -LiteralPath $built -Destination $staging -Recurse
        [IO.Directory]::Move($staging, $target)
        # The artifacts work directory of the earlier build layout.
        $work = Join-Path $cacheDir ('work-' + $Identity.Name)
        if ([IO.Directory]::Exists($work)) { try { [IO.Directory]::Delete($work, $true) } catch { } }
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
# own sources plus the shared CheckpointProcessRunner.cs, the core's build identity and the .NET version. Returns the command to start
# (the built executable), or $null when it cannot be had: Windows PowerShell 5.1, ANTIPHON_SCRIPTS_CLI=off,
# a copy of the scripts without the project, no SDK or a failed build (warned).
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
    # The CLI references the compiled core (Antiphon.Nightly and what it references): its identity is part of this one.
    $core = Get-NightlyCoreBuildIdentity
    if ($null -eq $core) { return $null }
    $runtime = [Text.Encoding]::UTF8.GetBytes("`n" + [Environment]::Version.ToString() + "`ncore " + $core.Hash)
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
# A long-lived session compares this before it trusts a core it already loaded. The walk is compiled
# (NightlyCoreStamp.cs): it runs before every ported call. Without a loaded core there is no stamp, and the
# caller re-checks the build identity instead.
function Get-NightlyCoreSourceStamp {
    if (-not ('Antiphon.Nightly.NightlyCoreStamp' -as [type])) { return '' }
    $projects = [string[]]@(
        (Join-Path $PSScriptRoot 'Antiphon.Nightly'),
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Antiphon.IO.WriteBehind'))
    )
    return [Antiphon.Nightly.NightlyCoreStamp]::Compute($projects)
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

# Code ported to the core keeps no PowerShell copy (2026-11-13): it loads the core here and refuses without it.
function Assert-NightlyCoreAvailable([string]$Caller) {
    if (-not (Test-NightlyCoreAvailable)) { throw ('nightly-core-required ' + $Caller) }
}

# A failure's reason: the refusal text, also when a core method threw it (PowerShell wraps that in a
# MethodInvocationException whose message names the method).
function Get-NightlyRefusalText {
    param($ErrorRecord)
    $failure = $ErrorRecord.Exception
    if ($failure -is [Management.Automation.MethodInvocationException] -and $null -ne $failure.InnerException) { $failure = $failure.InnerException }
    return $failure.Message
}
