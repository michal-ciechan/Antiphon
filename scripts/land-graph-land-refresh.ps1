# CARD-1753: refresh land-graph records for one landing batch, then notify.
# Run this before delegate.ps1 -Land. It never asks. A bare audit -Update stays forbidden.
# -LandRefresh on the audit script is only for this script.
# ASCII-only and Windows PowerShell 5.1 compatible. $PSScriptRoot is resolved in the body.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Base,
    [string]$Repo,
    [string]$Card,
    [string]$NotePath,
    [string]$AuditScript,
    [string]$PinScript)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/land-audit-tool.ps1')
$script:NotePath = $NotePath

function Write-Out([string]$Text) {
    [Console]::Out.Write($Text)
    if ([string]::IsNullOrWhiteSpace($script:NotePath)) { return }
    $parent = Split-Path -Parent $script:NotePath
    if (-not [string]::IsNullOrEmpty($parent)) {
        [void][System.IO.Directory]::CreateDirectory($parent)
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($script:NotePath, $Text, $utf8)
}

function Refuse([string]$Line) {
    Write-Out ($Line + "`n")
    exit 1
}

function ConvertTo-NativeArgument([string]$Value) {
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-Child([string]$Executable, [string[]]$Arguments, [int]$TimeoutMs) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.WorkingDirectory = $script:Repo
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    $quoted = New-Object System.Collections.Generic.List[string]
    foreach ($arg in $Arguments) { [void]$quoted.Add((ConvertTo-NativeArgument $arg)) }
    $start.Arguments = [string]::Join(' ', $quoted.ToArray())
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "could not start $Executable" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit($TimeoutMs)) {
            try { $process.Kill() } catch { }
            try { [void]$process.WaitForExit(10000) } catch { }
            return @{ Code = 124; Text = ''; Error = 'timeout' }
        }
        [void][System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), 30000)
        $stdout = ''
        $stderr = ''
        try { $stdout = $stdoutTask.Result } catch { $stdout = '' }
        try { $stderr = $stderrTask.Result } catch { $stderr = '' }
        if ($null -eq $stdout) { $stdout = '' }
        if ($null -eq $stderr) { $stderr = '' }
        return @{ Code = $process.ExitCode; Text = [string]$stdout; Error = [string]$stderr }
    } finally {
        $process.Dispose()
    }
}

function Invoke-Git([string[]]$Arguments) {
    return Invoke-Child 'git' $Arguments 120000
}

function ConvertTo-Lf([string]$Text) {
    if ($null -eq $Text) { return '' }
    return $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Test-DefinitionPath([string]$Path) {
    $name = [System.IO.Path]::GetFileName($Path)
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension.EndsWith('proj')) { return $true }
    if ($extension -eq '.props' -or $extension -eq '.targets' -or $extension -eq '.sln' -or $extension -eq '.slnx' -or $extension -eq '.slnf' -or $extension -eq '.rsp') { return $true }
    if ($name.StartsWith('Directory.Build.')) { return $true }
    if ($name.EndsWith('proj.user')) { return $true }
    $lower = $name.ToLowerInvariant()
    if ($lower -eq 'global.json' -or $lower -eq 'nuget.config' -or $lower -eq 'packages.lock.json' -or $lower -eq 'packages.config' -or $lower -eq 'directory.packages.props') { return $true }
    return $false
}

function Get-SelectedRows([string]$Tree) {
    $run = Invoke-Git @('ls-tree', '-r', '-z', $Tree)
    if ($run.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=unapproved-check' }
    $text = [string]$run.Text
    $parts = $text.Split([char]0)
    $rows = New-Object System.Collections.Generic.List[string]
    $limit = $parts.Length
    if ($limit -gt 0 -and $parts[$limit - 1].Length -eq 0) { $limit = $limit - 1 }
    for ($i = 0; $i -lt $limit; $i++) {
        $row = $parts[$i]
        if ([string]::IsNullOrEmpty($row)) { continue }
        $tab = $row.IndexOf([char]9)
        if ($tab -lt 1) { continue }
        $path = $row.Substring($tab + 1)
        if (Test-DefinitionPath $path) { [void]$rows.Add($row) }
    }
    $array = $rows.ToArray()
    [Array]::Sort($array, [System.StringComparer]::Ordinal)
    return ,$array
}

function Get-Fingerprint {
    param([string[]]$Rows)
    if ($null -eq $Rows) { $Rows = @() }
    $separator = [string][char]0
    $payload = [string]::Join($separator, $Rows) + $separator
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
        $hash = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }
    $hex = ([System.BitConverter]::ToString($hash)).Replace('-', '')
    return @{ Digest = $hex; Count = $Rows.Length }
}

function Get-RowMap {
    param([string[]]$Rows)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    if ($null -eq $Rows) { return $map }
    foreach ($row in $Rows) {
        $tab = $row.IndexOf([char]9)
        if ($tab -lt 1) { continue }
        $map[$row.Substring($tab + 1)] = $row
    }
    return $map
}

function Read-PinFile([string]$Text) {
    $lines = New-Object System.Collections.Generic.List[object]
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    $normalized = ConvertTo-Lf $Text
    foreach ($line in $normalized.Split([char]10)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line.StartsWith('#')) { continue }
        $fields = $line.Split([char]9)
        if ($fields.Length -ne 3 -or $fields[0].Length -eq 0 -or $fields[1].Length -eq 0 -or $fields[2].Length -ne 64) {
            return $null
        }
        $key = $fields[0] + "`t" + $fields[1]
        if ($map.ContainsKey($key)) { return $null }
        $map[$key] = $fields[2]
        $lines.Add(@{ Path = $fields[0]; Selector = $fields[1]; Hash = $fields[2]; Key = $key })
    }
    return @{ Lines = $lines; Map = $map }
}

function ConvertTo-JsonString([string]$Value) {
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq '"') { [void]$builder.Append('\"') }
        elseif ($ch -eq '\') { [void]$builder.Append('\\') }
        elseif ($ch -eq "`n") { [void]$builder.Append('\n') }
        elseif ($ch -eq "`r") { [void]$builder.Append('\r') }
        elseif ($ch -eq "`t") { [void]$builder.Append('\t') }
        elseif ([int]$ch -lt 32) { [void]$builder.Append(' ') }
        else { [void]$builder.Append($ch) }
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Send-CardNote([string]$Body) {
    if ([string]::IsNullOrWhiteSpace($Card)) { return $true }
    $api = $env:ANTIPHON_API
    if ([string]::IsNullOrWhiteSpace($api)) { return $false }
    $api = $api.TrimEnd('/')
    $payload = '{"body":' + (ConvertTo-JsonString $Body) + '}'
    $client = $null
    try {
        $client = New-Object System.Net.Http.HttpClient
        $client.Timeout = [TimeSpan]::FromSeconds(5)
        $content = New-Object System.Net.Http.StringContent ($payload, [System.Text.Encoding]::UTF8, 'application/json')
        $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Post, ($api + '/api/cards/' + $Card + '/discussion'))
        $request.Content = $content
        if (-not [string]::IsNullOrEmpty($env:ANTIPHON_TASK_TOKEN)) {
            [void]$request.Headers.TryAddWithoutValidation('X-Antiphon-Task-Token', $env:ANTIPHON_TASK_TOKEN)
        }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $code = [int]$response.StatusCode
            return ($code -ge 200 -and $code -lt 300)
        } finally {
            $response.Dispose()
        }
    } catch {
        return $false
    } finally {
        if ($null -ne $client) { $client.Dispose() }
    }
}

function Get-PinText {
    $powershell = (Get-Process -Id $PID).Path
    if (-not [string]::IsNullOrWhiteSpace($PinScript)) {
        $run = Invoke-Child $powershell @('-NoProfile', '-NonInteractive', '-File', $PinScript, '-Repo', $script:Repo) 120000
        if ($run.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=pin-printer-failed' }
        return [string]$run.Text
    }
    $tool = Resolve-LandAuditTool $script:Repo ''
    if ($tool.ContainsKey('Error')) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=pin-printer-missing' }
    $dll = [string]$tool.Path
    $run = Invoke-Child 'dotnet' @('exec', $dll, '--print-pins', '--repo', $script:Repo) 120000
    if ($run.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=pin-printer-failed' }
    return [string]$run.Text
}

try {
    if ([string]::IsNullOrWhiteSpace($Repo)) { $Repo = Split-Path -Parent $PSScriptRoot }
    $script:Repo = [System.IO.Path]::GetFullPath($Repo)
    if ([string]::IsNullOrWhiteSpace($AuditScript)) {
        $AuditScript = Join-Path $PSScriptRoot 'audit-land-verification-graph.ps1'
    }
    $ManifestRelative = 'server/Application/Services/LandVerificationBuildGraph.manifest.txt'
    $EvaluationRelative = 'server/Application/Services/LandVerificationBuildGraph.evaluation.txt'
    $PinsRelative = 'server/Application/Services/LandVerificationBuildGraph.pins.txt'
    $env:GIT_TERMINAL_PROMPT = '0'

    $baseCheck = Invoke-Git @('rev-parse', '--verify', ($Base + '^{commit}'))
    if ($baseCheck.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=bad-base' }

    $powershell = (Get-Process -Id $PID).Path
    $first = Invoke-Child $powershell @('-NoProfile', '-NonInteractive', '-File', $AuditScript, '-Check') 900000
    if ($first.Code -eq 0) {
        Write-Out "LAND-GRAPH-NOTICE action=current`n"
        exit 0
    }

    $checkText = [string]$first.Text + [string]$first.Error
    if ($checkText.Contains('-Update cannot approve it')) {
        Refuse 'LAND-GRAPH-NOTICE action=refuse reason=property-or-construct'
    }
    $malformed = @(
        'manifest missing',
        'manifest must be two lines',
        'manifest digest is not 64 uppercase hex characters',
        'manifest count is not a decimal integer')
    foreach ($phrase in $malformed) {
        if ($checkText.Contains($phrase)) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=malformed-manifest' }
    }
    $recordMismatch = $checkText.Contains('manifest does not match the selected graph') -or $checkText.Contains('(changed pin)') -or $checkText.Contains('(missing pin)') -or $checkText.Contains('(stale pin)')
    if (-not $recordMismatch) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=unapproved-check' }

    $baseRows = Get-SelectedRows $Base
    $headRows = Get-SelectedRows 'HEAD'
    $baseFp = Get-Fingerprint -Rows $baseRows
    $headFp = Get-Fingerprint -Rows $headRows
    $baseMap = Get-RowMap -Rows $baseRows
    $headMap = Get-RowMap -Rows $headRows
    $changedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($path in $baseMap.Keys) {
        if (-not $headMap.ContainsKey($path) -or ($headMap[$path] -cne $baseMap[$path])) { [void]$changedSet.Add($path) }
    }
    foreach ($path in $headMap.Keys) {
        if (-not $baseMap.ContainsKey($path)) { [void]$changedSet.Add($path) }
    }

    $manifestPath = Join-Path $script:Repo $ManifestRelative
    if (-not [System.IO.File]::Exists($manifestPath)) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=malformed-manifest' }
    $manifestLines = [System.IO.File]::ReadAllLines($manifestPath)
    if ($manifestLines.Length -lt 2) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=malformed-manifest' }
    $recordedDigest = $manifestLines[0].Trim()
    $recordedCount = 0
    if (-not [int]::TryParse($manifestLines[1].Trim(), [ref]$recordedCount)) {
        Refuse 'LAND-GRAPH-NOTICE action=refuse reason=malformed-manifest'
    }
    $baseMatches = ($baseFp.Digest -ceq $recordedDigest) -and ($baseFp.Count -eq $recordedCount)
    if (-not $baseMatches) {
        if ($changedSet.Count -gt 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=outside-batch' }
        Write-Out "LAND-GRAPH-NOTICE action=warn reason=inherited-mismatch`n"
        exit 0
    }

    $porcelain = Invoke-Git @('status', '--porcelain')
    if ($porcelain.Code -ne 0 -or -not [string]::IsNullOrWhiteSpace([string]$porcelain.Text)) {
        Refuse 'LAND-GRAPH-NOTICE action=refuse reason=dirty-worktree'
    }

    $pinsPath = Join-Path $script:Repo $PinsRelative
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $existingText = ''
    if ([System.IO.File]::Exists($pinsPath)) { $existingText = [System.IO.File]::ReadAllText($pinsPath, $utf8) }
    $printedText = Get-PinText
    $existingPins = Read-PinFile $existingText
    $printedPins = Read-PinFile $printedText
    if ($null -eq $existingPins -or $null -eq $printedPins) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=invalid-pins' }

    $union = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($key in $existingPins.Map.Keys) { [void]$union.Add($key) }
    foreach ($key in $printedPins.Map.Keys) { [void]$union.Add($key) }
    $pinChanged = $false
    foreach ($key in $union) {
        $hasOld = $existingPins.Map.ContainsKey($key)
        $hasNew = $printedPins.Map.ContainsKey($key)
        $same = $hasOld -and $hasNew -and ($existingPins.Map[$key] -ceq $printedPins.Map[$key])
        if ($same) { continue }
        $pinChanged = $true
        $owner = $key.Split([char]9)[0]
        if (-not $changedSet.Contains($owner)) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=outside-batch' }
    }
    if ($changedSet.Count -eq 0 -and -not $pinChanged) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=unapproved-check' }

    $originalPins = $null
    $wrotePins = $false
    if ($pinChanged) {
        $builder = New-Object System.Text.StringBuilder
        foreach ($entry in $existingPins.Lines) {
            if ($changedSet.Contains($entry.Path)) { continue }
            [void]$builder.Append($entry.Path).Append("`t").Append($entry.Selector).Append("`t").Append($entry.Hash).Append("`n")
        }
        $batchLines = New-Object System.Collections.Generic.List[string]
        foreach ($entry in $printedPins.Lines) {
            if ($changedSet.Contains($entry.Path)) {
                [void]$batchLines.Add($entry.Path + "`t" + $entry.Selector + "`t" + $entry.Hash)
            }
        }
        $batchArray = $batchLines.ToArray()
        [Array]::Sort($batchArray, [System.StringComparer]::Ordinal)
        foreach ($batchLine in $batchArray) { [void]$builder.Append($batchLine).Append("`n") }
        $proposed = $builder.ToString()
        if ($proposed -cne (ConvertTo-Lf $existingText)) {
            if ([System.IO.File]::Exists($pinsPath)) { $originalPins = [System.IO.File]::ReadAllBytes($pinsPath) }
            [System.IO.File]::WriteAllText($pinsPath, $proposed, $utf8)
            $wrotePins = $true
        }
    }

    $oldPins = Invoke-Git @('rev-parse', ('HEAD:' + $PinsRelative))
    if ($oldPins.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=unapproved-check' }

    $update = Invoke-Child $powershell @('-NoProfile', '-NonInteractive', '-File', $AuditScript, '-Update', '-LandRefresh') 900000
    if ($update.Code -ne 0) {
        if ($wrotePins -and $null -ne $originalPins) { [System.IO.File]::WriteAllBytes($pinsPath, $originalPins) }
        Refuse 'LAND-GRAPH-NOTICE action=refuse reason=update-failed'
    }

    $commit = Invoke-Git @('commit', '-m', 'chore: refresh land-graph records for the landing batch', '--', $ManifestRelative, $EvaluationRelative, $PinsRelative)
    if ($commit.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=commit-failed' }

    $second = Invoke-Child $powershell @('-NoProfile', '-NonInteractive', '-File', $AuditScript, '-Check') 900000
    if ($second.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=check-after-refresh' }

    $newPins = Invoke-Git @('rev-parse', ('HEAD:' + $PinsRelative))
    if ($newPins.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=unapproved-check' }
    $changed = New-Object System.Collections.Generic.List[string]
    foreach ($path in $changedSet) { [void]$changed.Add($path) }
    $changedArray = $changed.ToArray()
    [Array]::Sort($changedArray, [System.StringComparer]::Ordinal)
    $notice = New-Object System.Text.StringBuilder
    [void]$notice.Append("LAND-GRAPH-NOTICE action=refresh`n")
    [void]$notice.Append('old-digest=').Append($recordedDigest).Append("`n")
    [void]$notice.Append('new-digest=').Append($headFp.Digest).Append("`n")
    [void]$notice.Append('old-count=').Append($recordedCount.ToString([System.Globalization.CultureInfo]::InvariantCulture)).Append("`n")
    [void]$notice.Append('new-count=').Append($headFp.Count.ToString([System.Globalization.CultureInfo]::InvariantCulture)).Append("`n")
    [void]$notice.Append('old-pins=').Append($oldPins.Text.Trim()).Append("`n")
    [void]$notice.Append('new-pins=').Append($newPins.Text.Trim()).Append("`n")
    foreach ($path in $changedArray) { [void]$notice.Append('changed-records:').Append($path).Append("`n") }
    [void]$notice.Append("card-note: recorded`n")
    $noticeText = $notice.ToString()
    if (-not (Send-CardNote $noticeText)) { $noticeText = $noticeText + "card-note: post-failed`n" }
    Write-Out $noticeText
    exit 0
} catch {
    [Console]::Error.WriteLine('land-graph refresh failed')
    exit 1
}
