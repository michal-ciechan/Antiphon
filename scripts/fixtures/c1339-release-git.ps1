# CARD-1339 owned local Git transport. Never forward a caller-supplied transport.
function Assert-C1339ContainedPath {
    param([string]$Root, [string]$Path)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'fixture-path-outside-root'
    }
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'fixture-linked-path' }
        }
        $cursor = Split-Path -Parent $cursor
    }
}

function Invoke-C1339GitProcess {
    param([string]$Root, [string]$WorkingDirectory, [string[]]$Arguments)
    Assert-C1339ContainedPath -Root $Root -Path $WorkingDirectory
    $psi = [Diagnostics.ProcessStartInfo]::new('git')
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in @($psi.Environment.Keys)) {
        if ($key -match '(?i)TOKEN|SECRET|PASSWORD|CREDENTIAL|ANTIPHON|CODEX|CLAUDE|OPENAI|GH_|GITHUB|GIT_|ASKPASS') {
            [void]$psi.Environment.Remove($key)
        }
    }
    $psi.Environment['GIT_ALLOW_PROTOCOL'] = 'file'
    $psi.Environment['GIT_CONFIG_NOSYSTEM'] = '1'
    $psi.Environment['GIT_CONFIG_GLOBAL'] = (Join-Path $Root 'empty-git-config')
    $psi.Environment['GIT_TERMINAL_PROMPT'] = '0'
    foreach ($arg in @('-c', ('core.hooksPath=' + (Join-Path $Root 'no-hooks')), '-c', 'commit.gpgSign=false', '-c', 'tag.gpgSign=false', '-c', 'user.name=c1339', '-c', 'user.email=c1339@example.invalid') + $Arguments) {
        $psi.ArgumentList.Add($arg)
    }
    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill($true); $process.WaitForExit(); throw 'fixture-git-timeout' }
        return @{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult().Trim(); Error = $stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}

function Invoke-C1339FixtureGit {
    param([string]$Root, [string]$Checkout, [string]$Bare, [string]$WorkingDirectory, [string[]]$Arguments,
        [string]$FaultPath = '', [scriptblock]$Launcher = ${function:Invoke-C1339GitProcess})
    Assert-C1339ContainedPath -Root $Root -Path $Checkout
    Assert-C1339ContainedPath -Root $Root -Path $Bare
    if ([IO.Path]::GetFullPath($WorkingDirectory) -cne [IO.Path]::GetFullPath($Checkout)) { throw 'fixture-working-directory' }
    $forward = @($Arguments)
    $sub = [string]$Arguments[0]
    switch ($sub) {
        'ls-remote' {
            if ($Arguments.Count -lt 3 -or $Arguments[1] -notin @('--heads', '--tags')) { throw 'fixture-transport-form' }
            $remoteIndex = 2
            if ($Arguments[2] -eq '--refs') { $remoteIndex = 3 }
            if ($Arguments[$remoteIndex] -cne 'origin') { throw 'fixture-transport-refused' }
            foreach ($ref in @($Arguments | Select-Object -Skip ($remoteIndex + 1))) {
                if ($ref -notmatch '^(refs/(heads|tags)/|rc/|release/)[A-Za-z0-9./^{}-]+$') { throw 'fixture-ref-form' }
            }
            $forward[$remoteIndex] = $Bare
        }
        'push' {
            $spec = [string]$Arguments[2]
            $tagSpec = $spec -cmatch '^refs/antiphon-release-push/[a-f0-9]{32}/\*:refs/tags/\*$'
            if ($Arguments.Count -ne 3 -or $Arguments[1] -cne 'origin' -or
                (-not $tagSpec -and $spec -notmatch '^(refs/tags/[A-Za-z0-9./-]+|[a-f0-9]{40}:refs/heads/[A-Za-z0-9./-]+)$')) { throw 'fixture-push-form' }
            $forward[1] = $Bare
        }
        'fetch' {
            if ($Arguments.Count -ne 2 -or $Arguments[1] -cne 'origin') { throw 'fixture-fetch-form' }
            $forward[1] = $Bare
        }
        'tag' { if ($Arguments.Count -ne 6 -or $Arguments[1] -cne '-a' -or $Arguments[4] -cne '-m' -or $Arguments[3] -notmatch '^[a-f0-9]{40}$') { throw 'fixture-tag-form' } }
        'rev-parse' { if ($Arguments.Count -ne 2 -or $Arguments[1] -ne 'HEAD') { throw 'fixture-read-form' } }
        'rev-list' { if ($Arguments.Count -ne 4 -or $Arguments[1] -ne '-n' -or $Arguments[2] -ne '1') { throw 'fixture-read-form' } }
        'show-ref' { if ($Arguments.Count -ne 4 -or $Arguments[1] -ne '--verify' -or $Arguments[2] -ne '--hash' -or $Arguments[3] -notmatch '^refs/tags/[A-Za-z0-9.-]+$') { throw 'fixture-read-form' } }
        'update-ref' {
            $refIndex = 1
            if ($Arguments[1] -ceq '-d') { $refIndex = 2 }
            if ($Arguments.Count -ne 4 -or $Arguments[$refIndex] -cnotmatch '^refs/antiphon-release-push/[a-f0-9]{32}/v[0-9.rc-]+$' -or
                $Arguments[3] -cnotmatch '^[a-f0-9]{40}$') { throw 'fixture-private-ref-form' }
        }
        'log' { if ($Arguments.Count -ne 4 -or $Arguments[1] -ne '--no-merges' -or $Arguments[2] -ne '--format=%h %s') { throw 'fixture-read-form' } }
        default { throw 'fixture-command-refused' }
    }
    Add-Content -LiteralPath (Join-Path $Root 'real-git-trace.log') -Value ($Arguments -join ' ') -Encoding ASCII
    $fault = $null
    if ($FaultPath -and (Test-Path -LiteralPath $FaultPath)) { $fault = Get-Content -LiteralPath $FaultPath -Raw | ConvertFrom-Json }
    if ($sub -eq 'push' -and $fault.push -eq 'before') { return @{ ExitCode = 1; Output = '' } }
    $r = & $Launcher -Root $Root -WorkingDirectory $WorkingDirectory -Arguments $forward
    if ($sub -eq 'push' -and $fault.push -eq 'after') { return @{ ExitCode = 1; Output = '' } }
    if ($sub -eq 'ls-remote' -and $fault) {
        $ref = [string]$Arguments[-1]
        foreach ($response in @($fault.reads)) {
            if ($ref -cne [string]$response.ref) { continue }
            $countPath = Join-Path $Root ('read-' + (Get-NightlySha256Text -Text $ref) + '.count')
            $ordinal = 1
            if (Test-Path -LiteralPath $countPath) { $ordinal += [int](Get-Content -LiteralPath $countPath -Raw) }
            Set-Content -LiteralPath $countPath -Value $ordinal
            if ($response.ordinal -and [int]$response.ordinal -ne $ordinal) { continue }
            if ($response.mode -eq 'replace') { return @{ ExitCode = [int]$response.exit; Output = [string]$response.output } }
            if ($response.mode -eq 'prepend') { return @{ ExitCode = $r.ExitCode; Output = ([string]$response.output + "`n" + $r.Output) } }
        }
    }
    return $r
}
