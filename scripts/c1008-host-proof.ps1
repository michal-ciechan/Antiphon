# CARD-1607. Desktop proof of a private origin. Dot-source only. ASCII only.
# No script-level param: a param block would steal the caller's arguments.
# Credentials stay in the ambient git environment. This file never prints a URL,
# a token, or git stderr.
. (Join-Path $PSScriptRoot 'c1609-host-proof.ps1')

function Get-C1008Sha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))
        return ([System.BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function ConvertTo-C1008ScrubbedOrigin {
    param([Parameter(Mandatory = $true)][string]$OriginUrl)
    if ([string]::IsNullOrEmpty($OriginUrl)) { return $null }
    foreach ($ch in $OriginUrl.ToCharArray()) {
        $code = [int]$ch
        if ($code -le 32 -or $code -eq 127) { return $null }
    }
    $scrubbed = $OriginUrl
    $schemeIdx = $OriginUrl.IndexOf('://')
    if ($schemeIdx -ge 0) {
        $after = $OriginUrl.Substring($schemeIdx + 3)
        $at = $after.IndexOf('@')
        if ($at -ge 0) {
            $userinfo = $after.Substring(0, $at)
            $rest = $after.Substring($at + 1)
            $strip = $false
            if ($userinfo.IndexOf(':') -ge 0) { $strip = $true }
            if (-not $strip) {
                foreach ($prefix in @('ghp_', 'gho_', 'ghu_', 'ghs_', 'ghr_', 'github_pat_')) {
                    if ($userinfo.IndexOf($prefix, [System.StringComparison]::Ordinal) -ge 0) { $strip = $true; break }
                }
            }
            if ($strip) {
                $scrubbed = $OriginUrl.Substring(0, $schemeIdx) + '://' + $rest
            }
        }
    }
    $lower = $scrubbed.ToLowerInvariant()
    foreach ($needle in @('token', 'password', 'secret', 'ghp_', 'gho_', 'ghu_', 'ghs_', 'ghr_', 'github_pat_')) {
        if ($lower.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { return $null }
    }
    return $scrubbed
}

function Get-C1008HostLsRemoteDiagnosis {
    param([Parameter(Mandatory = $true)][string]$Repo, [Parameter(Mandatory = $true)][int]$Status)
    if ($Status -lt 0) { $Status = 1 }
    $lower = $Repo.ToLowerInvariant()
    $safe = $Repo -cmatch '^[A-Za-z0-9._/-]+$' -and $Repo.IndexOf('..') -lt 0 -and $Repo.IndexOf('//') -lt 0 -and -not $Repo.StartsWith('/')
    if ($safe) {
        foreach ($needle in @('token', 'password', 'secret', 'ghp_', 'gho_', 'ghu_', 'ghs_', 'ghr_', 'github_pat_')) {
            if ($lower.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { $safe = $false; break }
        }
    }
    if (-not $safe) { return 'RecycleGitAuditUnknown' }
    return "RecycleGitAuditUnknown audit check=host-ls-remote status=$Status repo=$Repo"
}

function Test-C1008SafeAuditDiagnosis {
    param([string]$Diagnosis)
    if ([string]::IsNullOrEmpty($Diagnosis)) { return $false }
    if ($Diagnosis.IndexOf('@') -ge 0 -or $Diagnosis.IndexOf('://') -ge 0) { return $false }
    return $Diagnosis -cmatch '^Recycle[A-Za-z0-9]+( audit check=[A-Za-z0-9_-]+ status=[0-9]* repo=[A-Za-z0-9._/-]+( reason=[A-Za-z0-9_-]+)?)?$'
}

function Read-C1008PrivateOriginLines {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    $rows = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($raw in ($Text -split "`n")) {
        $line = $raw.TrimEnd("`r")
        if ($line.Length -eq 0) { continue }
        if ($line -cnotmatch '^C1008_PRIVATE_ORIGIN repo=([A-Za-z0-9._/-]+) origin=(\S+) originHash=([0-9a-f]{64}) proofName=([0-9a-f]{64})$') {
            throw 'RecycleGitAuditUnknown'
        }
        $repo = $Matches[1]
        $origin = $Matches[2]
        $originHash = $Matches[3]
        $proofName = $Matches[4]
        if ($repo.IndexOf('..') -ge 0 -or $repo.IndexOf('//') -ge 0 -or $repo.StartsWith('/')) {
            throw 'RecycleGitAuditUnknown'
        }
        $lowerRepo = $repo.ToLowerInvariant()
        foreach ($needle in @('token', 'password', 'secret', 'ghp_', 'gho_', 'ghu_', 'ghs_', 'ghr_', 'github_pat_')) {
            if ($lowerRepo.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { throw 'RecycleGitAuditUnknown' }
        }
        $scrubbed = ConvertTo-C1008ScrubbedOrigin -OriginUrl $origin
        if ([string]::IsNullOrEmpty($scrubbed) -or $scrubbed -cne $origin) { throw 'RecycleGitAuditUnknown' }
        $expectHash = Get-C1008Sha256 -Text $scrubbed
        $expectName = Get-C1008Sha256 -Text ($repo + "`n" + $scrubbed)
        if ($expectHash -cne $originHash -or $expectName -cne $proofName) { throw 'RecycleGitAuditUnknown' }
        if ($seen.ContainsKey($proofName)) { throw 'RecycleGitAuditUnknown' }
        $seen[$proofName] = $true
        [void]$rows.Add([pscustomobject]@{
            Repo = $repo
            Origin = $scrubbed
            OriginHash = $originHash
            ProofName = $proofName
        })
    }
    return @($rows.ToArray())
}

function Invoke-C1008Git {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string[]]$GitArgs,
        [int]$TimeoutMs = 30000
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $argList = $psi.GetType().GetProperty('ArgumentList')
    if ($null -eq $argList) { throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2) }
    $list = $argList.GetValue($psi, $null)
    foreach ($gitArg in $GitArgs) { [void]$list.Add($gitArg) }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        try { [void]$proc.Start() }
        catch { throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2) }
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
        $exited = $proc.WaitForExit($TimeoutMs)
        if (-not $exited) {
            try { $proc.Kill() } catch { }
            [void]$proc.WaitForExit()
            try { $null = $stdoutTask.Result } catch { }
            try { $null = $stderrTask.Result } catch { }
            throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 124)
        }
        try { $null = $stdoutTask.Result } catch { throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2) }
        try { $null = $stderrTask.Result } catch { }
        if ($proc.ExitCode -ne 0) {
            $status = [int]$proc.ExitCode
            if ($status -lt 0) { $status = 1 }
            throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status $status)
        }
    }
    finally { $proc.Dispose() }
}

function Write-C1008HostBundle {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$OriginUrl,
        [Parameter(Mandatory = $true)][string]$BundlePath
    )
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('c1609-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($scratch) | Out-Null
    try {
        Invoke-C1008Git -Repo $Repo -GitArgs @('init', '-q', '--bare', $scratch)
        Invoke-C1008Git -Repo $Repo -GitArgs @('--git-dir', $scratch, 'fetch', '-q', '--no-tags', $OriginUrl, '+refs/heads/*:refs/heads/*', '+refs/tags/*:refs/tags/*')
        if ([System.IO.File]::Exists($BundlePath)) { Remove-Item -LiteralPath $BundlePath -Force }
        Invoke-C1008Git -Repo $Repo -GitArgs @('--git-dir', $scratch, 'bundle', 'create', $BundlePath, '--all')
        if (-not [System.IO.File]::Exists($BundlePath)) {
            throw (Get-C1008HostLsRemoteDiagnosis -Repo $Repo -Status 2)
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($scratch)) {
            Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-C1008HostOriginProof {
    param(
        [Parameter(Mandatory = $true)][string]$OriginUrl,
        [Parameter(Mandatory = $true)][string]$RepoRelative,
        [Parameter(Mandatory = $true)][string]$OperationId,
        [Parameter(Mandatory = $true)][string]$ProofDirectory,
        [Parameter(Mandatory = $true)][long]$CreatedUnix,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [ValidateSet(1,2)][int]$ProofVersion = 1,
        [string]$PhaseNonce = '', [string]$InventoryDigest = '', [string[]]$ApprovedOrigins = @(),
        [int]$NetworkBudgetMs = 600000,
        [object[]]$RequiredObjects = @(), [string]$PreviousProofFile = ''
    )
    if ($ProofVersion -eq 2) {
        Write-C1609HostOriginProof -OriginUrl $OriginUrl -RepoRelative $RepoRelative -OperationId $OperationId -ProofDirectory $ProofDirectory -CreatedUnix $CreatedUnix -LogPath $LogPath -PhaseNonce $PhaseNonce -InventoryDigest $InventoryDigest -ApprovedOrigins $ApprovedOrigins -NetworkBudgetMs $NetworkBudgetMs -RequiredObjects $RequiredObjects -PreviousProofFile $PreviousProofFile
        return
    }
    if ($OperationId -cnotmatch '^c1008[0-9a-f]{32}$' -or $CreatedUnix -lt 0) {
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
    }
    if ($RepoRelative -cnotmatch '^[A-Za-z0-9._/-]+$' -or $RepoRelative.IndexOf('..') -ge 0 -or $RepoRelative.IndexOf('//') -ge 0 -or $RepoRelative.StartsWith('/')) {
        throw 'RecycleGitAuditUnknown'
    }
    $lowerRepo = $RepoRelative.ToLowerInvariant()
    foreach ($needle in @('token', 'password', 'secret', 'ghp_', 'gho_', 'ghu_', 'ghs_', 'ghr_', 'github_pat_')) {
        if ($lowerRepo.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { throw 'RecycleGitAuditUnknown' }
    }
    $scrubbed = ConvertTo-C1008ScrubbedOrigin -OriginUrl $OriginUrl
    if ([string]::IsNullOrEmpty($scrubbed)) {
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
    }
    if (-not [System.IO.Directory]::Exists($ProofDirectory)) {
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
    }
    $dirInfo = New-Object System.IO.DirectoryInfo $ProofDirectory
    if (($dirInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $argList = $psi.GetType().GetProperty('ArgumentList')
    if ($null -ne $argList) {
        $list = $argList.GetValue($psi, $null)
        [void]$list.Add('ls-remote')
        [void]$list.Add('--heads')
        [void]$list.Add('--tags')
        [void]$list.Add($scrubbed)
    }
    else {
        if ($scrubbed -cnotmatch '^[A-Za-z0-9._:/+-]+$') {
            throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2)
        }
        $psi.Arguments = 'ls-remote --heads --tags ' + $scrubbed
    }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        try { [void]$proc.Start() }
        catch { throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2) }
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
        $exited = $proc.WaitForExit(30000)
        if (-not $exited) {
            try { $proc.Kill() } catch { }
            [void]$proc.WaitForExit()
            try { $null = $stdoutTask.Result } catch { }
            try { $null = $stderrTask.Result } catch { }
            throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 124)
        }
        $stdout = ''
        try { $stdout = [string]$stdoutTask.Result } catch { throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2) }
        try { $null = $stderrTask.Result } catch { }
        if ($proc.ExitCode -ne 0) {
            $status = [int]$proc.ExitCode
            if ($status -lt 0) { $status = 1 }
            throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status $status)
        }
        $refs = New-Object System.Collections.Generic.List[string]
        foreach ($raw in ($stdout -split "`n")) {
            $refLine = $raw.TrimEnd("`r")
            if ($refLine.Length -eq 0) { continue }
            if ($refLine.IndexOf("`r") -ge 0) { continue }
            if ($refLine -cnotmatch '^[0-9a-f]{40}\trefs/(heads|tags)/\S+$') { continue }
            $lowerLine = $refLine.ToLowerInvariant()
            $leaked = $false
            foreach ($needle in @('ghp_', 'gho_', 'ghu_', 'ghs_', 'ghr_', 'github_pat_', 'token', 'password', 'secret')) {
                if ($lowerLine.IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) { $leaked = $true; break }
            }
            if ($leaked) { throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2) }
            [void]$refs.Add($refLine)
        }
        if ($refs.Count -lt 1) { throw (Get-C1008HostLsRemoteDiagnosis -Repo $RepoRelative -Status 2) }
        $originHash = Get-C1008Sha256 -Text $scrubbed
        $name = Get-C1008Sha256 -Text ($RepoRelative + "`n" + $scrubbed)
        # The bundle is the advertised graph. The proof file stays the ref list.
        # A failed fetch refuses before that file exists, so a ref list is never
        # ancestry without the objects that prove it.
        Write-C1008HostBundle -Repo $RepoRelative -OriginUrl $scrubbed -BundlePath (Join-Path $ProofDirectory ($name + '.bundle'))
        $createdText = $CreatedUnix.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $nl = "`n"
        $body = 'c1008-host-proof 1' + $nl + 'operation ' + $OperationId + $nl + 'origin ' + $originHash + $nl + 'repo ' + $RepoRelative + $nl + 'created ' + $createdText + $nl
        foreach ($refLine in $refs) { $body = $body + $refLine + $nl }
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText((Join-Path $ProofDirectory $name), $body, $utf8)
        [System.IO.File]::AppendAllText($LogPath, ('repo=' + $RepoRelative + ' refs=' + $refs.Count + $nl), $utf8)
    }
    finally {
        $proc.Dispose()
    }
}
