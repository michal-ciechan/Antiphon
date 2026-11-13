# CARD-1820 S1R1. Release evidence is separate from the advisory schema-1 index.
# No release graph is admitted yet. Unknown identities stay null and require Full.
# Release mode requires PowerShell 7; ordinary selection retains its 5.1 contract.
Set-StrictMode -Version 2.0

function Invoke-ReleaseImpactGit {
    param([string]$Root, [string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory = $Root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($key in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE')) { [void]$start.Environment.Remove($key) }
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(60000)) { throw 'release-impact Git operation timed out' }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 10000)) {
            throw 'release-impact Git output drain timed out'
        }
        if ($child.ExitCode -ne 0) { throw 'release-impact Git input could not be verified' }
        return $stdout.Result.Trim()
    }
    finally {
        if (-not $child.HasExited) {
            $child.Kill($true)
            if (-not $child.WaitForExit(30000)) { throw 'release-impact Git teardown timed out' }
        }
        $child.Dispose()
    }
}

function Get-ReleaseImpactAlgorithmIdentity {
    param([string]$SourceRoot)
    $sha = Invoke-ReleaseImpactGit $SourceRoot @('rev-parse', '--verify', 'HEAD^{commit}')
    if ($sha -cnotmatch '^[0-9a-f]{40}$') { throw 'release-impact algorithm SHA is unsupported' }
    $inventory = New-Object 'System.Collections.Generic.List[string]'
    foreach ($path in @('scripts/impacted-classes.ps1', 'scripts/lib/release-impact.ps1')) {
        $blob = Invoke-ReleaseImpactGit $SourceRoot @('rev-parse', '--verify', ($sha + ':' + $path))
        # Bind the bytes being executed, not a clean-filter/eol-normalized view.
        $disk = Invoke-ReleaseImpactGit $SourceRoot @('hash-object', '--no-filters', '--', $path)
        if ($blob -cne $disk) { throw 'release-impact algorithm source drift' }
        [void]$inventory.Add($path + "`0" + $blob + "`n")
    }
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($inventory -join ''))
        $digest = ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $hash.Dispose() }
    return [pscustomobject]@{ Sha = $sha; Digest = $digest }
}

function New-ReleaseImpactEnvelope {
    param([string]$RepositoryRoot, [string]$Baseline, [string]$Candidate,
        [string]$Platform, [string]$SourceRoot)
    foreach ($sha in @($Baseline, $Candidate)) {
        if ($sha -cnotmatch '^[0-9a-f]{40}$') { throw 'release-impact endpoints must be full lowercase 40-hex commit SHAs' }
        $resolved = Invoke-ReleaseImpactGit $RepositoryRoot @('rev-parse', '--verify', ($sha + '^{commit}'))
        if ($resolved -cne $sha) { throw 'release-impact endpoint is not the exact commit' }
    }
    if ($Platform -cnotin @('linux', 'windows')) { throw 'release-impact target must be linux or windows' }
    $common = Invoke-ReleaseImpactGit $RepositoryRoot @('rev-parse', '--git-common-dir')
    if (-not [IO.Path]::IsPathRooted($common)) { $common = Join-Path $RepositoryRoot $common }
    $algorithm = Get-ReleaseImpactAlgorithmIdentity $SourceRoot
    return [pscustomobject][ordered]@{
        schema = 'antiphon-release-impact-v1'
        repository = [IO.Path]::GetFullPath($common).Replace('\', '/')
        baselineSha = $Baseline
        candidateSha = $Candidate
        algorithmSha = $algorithm.Sha
        algorithmDigest = $algorithm.Digest
        target = [pscustomobject][ordered]@{
            platform = $Platform
            profile = $null
            framework = $null
            rid = $null
            buildProperties = $null
            toolchain = $null
            verified = $false
        }
        inputManifestDigest = $null
        baselineGraphDigest = $null
        candidateGraphDigest = $null
        universeDigest = $null
        classes = @()
        complete = $false
        incompleteReasons = @('release-graph-unavailable')
        fallback = 'Full'
        compatibility = [pscustomobject][ordered]@{
            policy = $null
            catalog = $null
            selector = $null
            runtime = $null
            projections = @()
            globalIncompatibilityReasons = @('release-graph-unavailable')
        }
        # Not computed in this slice. Advisory filters never substitute for a universe.
        legacySelection = $null
    }
}

function ConvertTo-ReleaseImpactJson {
    param($Envelope)
    # This producer admits only the incomplete contract. Graph support must introduce
    # its own completeness validation before this gate can accept true or any classes.
    if ($Envelope.schema -cne 'antiphon-release-impact-v1' -or
        $Envelope.complete -isnot [bool] -or $Envelope.complete -or
        $Envelope.fallback -cne 'Full' -or @($Envelope.incompleteReasons).Count -eq 0 -or
        @($Envelope.classes).Count -ne 0 -or $Envelope.target.verified -ne $false) {
        throw 'release-impact envelope refused: incomplete/Full evidence is required'
    }
    foreach ($name in @('baselineSha', 'candidateSha', 'algorithmSha')) {
        if ($Envelope.$name -cnotmatch '^[0-9a-f]{40}$') { throw 'release-impact envelope refused: invalid SHA binding' }
    }
    if ([string]::IsNullOrWhiteSpace($Envelope.repository) -or
        $Envelope.algorithmDigest -cnotmatch '^[0-9a-f]{64}$' -or
        $Envelope.target.platform -cnotin @('linux', 'windows')) {
        throw 'release-impact envelope refused: invalid repository, algorithm or target'
    }
    foreach ($name in @('inputManifestDigest', 'baselineGraphDigest', 'candidateGraphDigest', 'universeDigest')) {
        if ($null -ne $Envelope.$name) { throw 'release-impact envelope refused: graph identity is unverified' }
    }
    return ($Envelope | ConvertTo-Json -Depth 12)
}
