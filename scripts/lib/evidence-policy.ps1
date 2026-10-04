# CARD-1015: read-only Git object policy. Only Get-EvidenceGitResult is the test I/O seam.
Set-StrictMode -Version Latest

function Invoke-EvidenceNativeGit {
    param([string]$Repository, [string[]]$Arguments)
    $p = [Diagnostics.Process]::new()
    $p.StartInfo.FileName = 'git'
    $p.StartInfo.WorkingDirectory = $Repository
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    $p.StartInfo.Environment['GIT_OPTIONAL_LOCKS'] = '0'
    foreach ($arg in $Arguments) { $p.StartInfo.ArgumentList.Add($arg) }
    $memory = [IO.MemoryStream]::new()
    try {
        if (-not $p.Start()) { throw 'git_start' }
        $copy = $p.StandardOutput.BaseStream.CopyToAsync($memory)
        $errorRead = $p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit(60000)) {
            $p.Kill($true)
            $p.WaitForExit()
            [void]$copy.GetAwaiter().GetResult()
            [void]$errorRead.GetAwaiter().GetResult()
            throw 'git_timeout'
        }
        [void]$copy.GetAwaiter().GetResult()
        [void]$errorRead.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $p.ExitCode; Bytes = $memory.ToArray() }
    } finally { $p.Dispose(); $memory.Dispose() }
}

function Get-EvidenceGitResult {
    param([string]$Repository, [string[]]$Arguments)
    Invoke-EvidenceNativeGit -Repository $Repository -Arguments $Arguments
}

function Read-EvidenceGit {
    param([string]$Repository, [string[]]$Arguments)
    $result = Get-EvidenceGitResult -Repository $Repository -Arguments $Arguments
    if ($null -eq $result -or $result.ExitCode -ne 0 -or $result.Bytes -isnot [byte[]]) {
        throw ('git_result_' + $Arguments[0])
    }
    return ,$result.Bytes
}

function ConvertFrom-EvidenceUtf8 {
    param([byte[]]$Bytes)
    return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
}

function Test-EvidenceOid {
    param([string]$Oid)
    return $Oid -cmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$'
}

function Resolve-EvidenceCommit {
    param([string]$Repository, [string]$Ref)
    if ([string]::IsNullOrWhiteSpace($Ref) -or $Ref.StartsWith('-') -or $Ref.Contains([char]0)) { throw 'ref_input' }
    $value = (ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('rev-parse', '--verify', '--end-of-options', ($Ref + '^{commit}')))).TrimEnd("`n", "`r")
    if (-not (Test-EvidenceOid $value)) { throw 'ref_result' }
    return $value
}

function Assert-EvidenceHistory {
    param([string]$Repository)
    $value = (ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('rev-parse', '--is-shallow-repository'))).TrimEnd("`n", "`r")
    if ($value -cne 'false') { throw 'shallow_or_unknown_history' }
}

function Assert-EvidenceAncestor {
    param([string]$Repository, [string]$Ancestor, [string]$Descendant)
    [void](Read-EvidenceGit $Repository @('merge-base', '--is-ancestor', $Ancestor, $Descendant))
}

function Read-EvidenceCommits {
    param([string]$Repository, [string]$Base, [string]$Head)
    $text = ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('rev-list', '--reverse', '--topo-order', ($Base + '..' + $Head)))
    if ($text.Length -eq 0) { return }
    if (-not $text.EndsWith("`n")) { throw 'commit_terminator' }
    foreach ($line in $text.TrimEnd("`n").Split("`n")) {
        if (-not (Test-EvidenceOid $line)) { throw 'commit_id' }
        $line
    }
}

function Read-EvidenceParents {
    param([string]$Repository, [string]$Commit)
    $line = (ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('rev-list', '--parents', '-n', '1', $Commit))).TrimEnd("`n", "`r")
    $fields = $line.Split(' ')
    if ($fields[0] -cne $Commit) { throw 'parent_identity' }
    foreach ($field in $fields) { if (-not (Test-EvidenceOid $field)) { throw 'parent_id' } }
    foreach ($field in $fields | Select-Object -Skip 1) { $field }
}

function Read-EvidenceNul {
    param([byte[]]$Bytes)
    $text = ConvertFrom-EvidenceUtf8 $Bytes
    if ($text.Length -eq 0) { return }
    if (-not $text.EndsWith([string][char]0)) { throw 'nul_terminator' }
    $records = $text.Split([char]0)
    for ($i = 0; $i -lt $records.Length - 1; $i++) { $records[$i] }
}

function Read-EvidenceDiff {
    param([string]$Repository, [string]$Commit, [string]$Parent)
    $arguments = @('diff-tree', '--no-commit-id', '--raw', '-r', '-z', '--no-renames', '--abbrev=64')
    if ($Parent) { $arguments += @($Parent, $Commit) } else { $arguments += @('--root', $Commit) }
    $records = @(Read-EvidenceNul (Read-EvidenceGit $Repository $arguments))
    if ($records.Count % 2 -ne 0) { throw 'raw_cardinality' }
    for ($i = 0; $i -lt $records.Count; $i += 2) {
        $fields = $records[$i].Split(' ')
        if ($fields.Length -ne 5 -or $fields[0] -cnotmatch '^:[0-7]{6}$' -or $fields[1] -cnotmatch '^[0-7]{6}$' -or
            -not (Test-EvidenceOid $fields[2]) -or -not (Test-EvidenceOid $fields[3]) -or $fields[4] -cnotmatch '^[ADMT]$') { throw 'raw_header' }
        $path = $records[$i + 1]
        if (-not $path -or $path.StartsWith('/') -or $path.Split('/') -contains '..') { throw 'raw_path' }
        [pscustomobject]@{ Path = $path; OldMode = $fields[0].Substring(1); Mode = $fields[1]; OldOid = $fields[2]; Oid = $fields[3]; Status = $fields[4] }
    }
}

function Get-EvidenceBlobSize {
    param([string]$Repository, [string]$Oid)
    $text = (ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('cat-file', '-s', $Oid))).TrimEnd("`n", "`r")
    $size = 0L
    if ($text -cnotmatch '^(0|[1-9][0-9]*)$' -or -not [long]::TryParse($text, [ref]$size)) { throw 'blob_size' }
    return $size
}

function Test-EvidenceRoot {
    param([string]$Path)
    return $Path.Split('/')[0].Equals('.antiphon', [StringComparison]::OrdinalIgnoreCase)
}

function Get-EvidenceViolation {
    param([string]$Path, [string]$Mode, [long]$Bytes)
    if (-not (Test-EvidenceRoot $Path)) { return '' }
    $components = $Path.Split('/')
    for ($i = 1; $i -lt $components.Length - 1; $i++) {
        if ($components[$i].IndexOf('checkpoints', [StringComparison]::OrdinalIgnoreCase) -ge 0) { return 'checkpoint_directory' }
    }
    if ($Mode -cne '100644' -and $Mode -cne '100755') { return 'non_regular_mode' }
    if (-not [IO.Path]::GetExtension($Path).Equals('.md', [StringComparison]::OrdinalIgnoreCase)) { return 'non_markdown' }
    if ($Bytes -gt 1048576) { return 'oversize_blob' }
    return ''
}

function Write-EvidenceViolation {
    param([string]$Commit, [object]$Entry, [string]$Reason, [long]$Bytes)
    $escaped = ConvertTo-Json -InputObject $Entry.Path -Compress
    Write-Host "EVIDENCE violation commit=$Commit path=$escaped bytes=$Bytes reason=$Reason"
}

function Select-EvidenceEventRange {
    param([string]$Repository, [string]$EventPath, [string]$EventName, [string]$ManualBase, [string]$ManualHead)
    if ($EventName -cnotin @('push', 'workflow_dispatch')) { throw 'unknown_event' }
    $event = Get-Content -LiteralPath $EventPath -Raw -ErrorAction Stop | ConvertFrom-Json -AsHashtable -ErrorAction Stop
    if ($event -isnot [System.Collections.IDictionary]) { throw 'event_shape' }
    if ($EventName -ceq 'workflow_dispatch') {
        if ([string]::IsNullOrWhiteSpace($ManualBase)) { throw 'manual_base_required' }
        if ([string]::IsNullOrWhiteSpace($ManualHead)) { throw 'manual_head_required' }
        return @{ Base = $ManualBase; Head = $ManualHead; Deleted = $false }
    }
    foreach ($key in @('ref', 'before', 'after')) {
        if (-not $event.Contains($key) -or $event[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($event[$key])) { throw 'event_shape' }
    }
    if (-not $event.Contains('deleted') -or $event.deleted -isnot [bool]) { throw 'deleted_type' }
    if ($event.repository -isnot [System.Collections.IDictionary] -or $event.repository.default_branch -isnot [string] -or [string]::IsNullOrWhiteSpace($event.repository.default_branch)) { throw 'event_repository' }
    if ($event.ref -cnotmatch '^refs/heads/.+') { throw 'event_ref' }
    if (-not (Test-EvidenceOid $event.before) -or -not (Test-EvidenceOid $event.after)) { throw 'event_oid' }
    if ($event.deleted -eq $true) { return @{ Deleted = $true } }
    $head = Resolve-EvidenceCommit $Repository $event.after
    $default = $event.repository.default_branch
    if ($event.ref -ceq ('refs/heads/' + $default)) {
        if ($event.before -cmatch '^0+$') { throw 'default_zero_before' }
        return @{ Base = $event.before; Head = $head; Deleted = $false }
    }
    $remote = Resolve-EvidenceCommit $Repository ('refs/remotes/origin/' + $default)
    $base = (ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('merge-base', $remote, $head))).TrimEnd("`n", "`r")
    if (-not (Test-EvidenceOid $base)) { throw 'merge_base_result' }
    return @{ Base = $base; Head = $head; Deleted = $false }
}

function Invoke-EvidenceHistory {
    param([string]$Repository = '.', [string]$BaseRef, [string]$HeadRef = 'HEAD', [string]$EventPath, [string]$EventName, [string]$ManualBase, [string]$ManualHead)
    try {
        Assert-EvidenceHistory $Repository
        if ($EventPath -or $EventName) {
            if ($BaseRef) { throw 'mixed_parameter_sets' }
            $range = Select-EvidenceEventRange $Repository $EventPath $EventName $ManualBase $ManualHead
            if ($range.Deleted) { Write-Host 'EVIDENCE excluded deleted=true'; return 0 }
            $BaseRef = $range.Base; $HeadRef = $range.Head
        }
        $base = Resolve-EvidenceCommit $Repository $BaseRef
        $head = Resolve-EvidenceCommit $Repository $HeadRef
        Assert-EvidenceAncestor $Repository $base $head
        Write-Host "EVIDENCE range base=$base head=$head"
        $commits = @(Read-EvidenceCommits $Repository $base $head)
        $entries = 0; $violations = 0
        foreach ($commit in $commits) {
            $parents = @(Read-EvidenceParents $Repository $commit)
            $parent = if ($parents.Count) { $parents[0] } else { '' }
            foreach ($entry in @(Read-EvidenceDiff $Repository $commit $parent)) {
                if (-not (Test-EvidenceRoot $entry.Path)) { continue }
                $entries++
                if ($entry.Status -ceq 'D') { continue }
                $size = if ($entry.Mode -ceq '160000') { 0L } else { Get-EvidenceBlobSize $Repository $entry.Oid }
                $reason = Get-EvidenceViolation $entry.Path $entry.Mode $size
                if ($reason) { $violations++; Write-EvidenceViolation $commit $entry $reason $size }
            }
        }
        Write-Host "EVIDENCE result commits=$($commits.Count) entries=$entries violations=$violations base=$base head=$head"
        if ($violations) { return 1 }; return 0
    } catch { Write-Host ('EVIDENCE error reason=' + $_.Exception.Message); return 2 }
}

function Read-EvidenceTree {
    param([string]$Repository, [string]$Commit)
    foreach ($record in @(Read-EvidenceNul (Read-EvidenceGit $Repository @('ls-tree', '-r', '-z', '-l', '--full-tree', $Commit)))) {
        $tab = $record.IndexOf([char]9)
        if ($tab -lt 0) { throw 'tree_cardinality' }
        $fields = $record.Substring(0, $tab).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
        if ($fields.Length -ne 4 -or $fields[0] -cnotmatch '^[0-7]{6}$' -or $fields[1] -cnotin @('blob', 'commit') -or -not (Test-EvidenceOid $fields[2])) { throw 'tree_header' }
        $size = 0L
        if ($fields[1] -ceq 'commit') {
            if ($fields[0] -cne '160000' -or $fields[3] -cne '-') { throw 'tree_gitlink' }
        } elseif ($fields[3] -cnotmatch '^(0|[1-9][0-9]*)$' -or -not [long]::TryParse($fields[3], [ref]$size)) { throw 'tree_size' }
        $path = $record.Substring($tab + 1)
        if (-not $path -or $path.StartsWith('/') -or $path.Split('/') -contains '..') { throw 'tree_path' }
        [pscustomobject]@{ Path = $path; Mode = $fields[0]; Oid = $fields[2]; Bytes = $size; Type = $fields[1] }
    }
}

function Get-EvidencePathHash {
    param([object[]]$Entries, [switch]$Classification)
    $memory = [IO.MemoryStream]::new()
    try {
        foreach ($entry in $Entries) {
            $memory.Write([Text.Encoding]::UTF8.GetBytes($entry.Path)); $memory.WriteByte(0)
            if ($Classification) {
                $label = if (Get-EvidenceViolation $entry.Path $entry.Mode $entry.Bytes) { 'delete' } else { 'keep' }
                $memory.Write([Text.Encoding]::ASCII.GetBytes($label)); $memory.WriteByte(0)
            }
        }
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($memory.ToArray())).ToLowerInvariant()
    } finally { $memory.Dispose() }
}

function Get-EvidenceMap {
    param([object[]]$Entries)
    $map = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Entries) { if (-not $map.TryAdd($entry.Path, $entry)) { throw 'duplicate_path' } }
    return ,$map
}

function Invoke-EvidenceDeletion {
    param([string]$Repository = '.', [string]$InventoryRef, [string]$InventoryPathSha256, [string]$InventoryCount, [string]$InventoryBytes, [string]$HeadRef = 'HEAD', [switch]$InventoryOnly, [string]$SupplementalCleanup)
    try {
        $supplemental = $PSBoundParameters.ContainsKey('SupplementalCleanup')
        if ($supplemental -and ($SupplementalCleanup -cnotmatch '^CARD-[0-9]{4,}$' -or $SupplementalCleanup -ceq 'CARD-1015')) { throw 'supplemental_cleanup_input' }
        $deletionMarker = if ($supplemental) { "Antiphon-Evidence-Deletion: $SupplementalCleanup" } else { 'Antiphon-Evidence-Deletion: CARD-1015' }
        $count = 0L; $bytes = 0L
        if (-not $InventoryOnly) {
            if ($InventoryPathSha256 -cnotmatch '^[0-9a-fA-F]{64}$' -or $InventoryCount -cnotmatch '^(0|[1-9][0-9]*)$' -or $InventoryBytes -cnotmatch '^(0|[1-9][0-9]*)$' -or
                -not [long]::TryParse($InventoryCount, [ref]$count) -or -not [long]::TryParse($InventoryBytes, [ref]$bytes)) { throw 'inventory_input' }
        }
        Assert-EvidenceHistory $Repository
        $base = Resolve-EvidenceCommit $Repository $InventoryRef
        $anchor = @(); $signature = 'notApplicable'
        if (-not $supplemental) {
            $anchorSha = Resolve-EvidenceCommit $Repository 'bb5fa774cd56f85ee6f0b1122c198192427e5ddf'
            $anchor = @(Read-EvidenceTree $Repository $anchorSha | Where-Object { Test-EvidenceRoot $_.Path })
            $signature = Get-EvidencePathHash $anchor -Classification
        }
        $tree = @(Read-EvidenceTree $Repository $base)
        $map = Get-EvidenceMap $tree
        $violations = 0
        if (-not $supplemental -and ($anchor.Count -ne 108 -or (Get-EvidencePathHash $anchor) -cne '356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c' -or $signature -cne 'f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b')) {
            Write-Host 'EVIDENCE deletion violation reason=anchor_classification'; $violations++
        }
        foreach ($entry in $anchor) {
            if (-not $map.ContainsKey($entry.Path)) { Write-Host 'EVIDENCE deletion violation reason=anchor_present'; $violations++; continue }
            if ($map[$entry.Path].Oid -cne $entry.Oid) { Write-Host 'EVIDENCE deletion violation reason=anchor_oid'; $violations++ }
            if ($map[$entry.Path].Mode -cne $entry.Mode) { Write-Host 'EVIDENCE deletion violation reason=anchor_mode'; $violations++ }
        }
        $scoped = @($tree | Where-Object { Test-EvidenceRoot $_.Path })
        $delete = @($scoped | Where-Object { Get-EvidenceViolation $_.Path $_.Mode $_.Bytes })
        $keep = @($scoped | Where-Object { -not (Get-EvidenceViolation $_.Path $_.Mode $_.Bytes) })
        $digest = Get-EvidencePathHash $delete
        $total = 0L; foreach ($entry in $delete) { $total += $entry.Bytes }
        if ($InventoryOnly) {
            $result = @{ Base = $base; Delete = $delete; Keep = $keep; Count = $delete.Count; Bytes = $total; PathSha256 = $digest; KeepCount = $keep.Count; KeepBytes = ($keep | Measure-Object Bytes -Sum).Sum; KeepPathSha256 = (Get-EvidencePathHash $keep); AnchorSignature = $signature; AnchorValid = ($violations -eq 0) }
            if ($supplemental) {
                $result.Remove('AnchorSignature'); $result.Remove('AnchorValid')
                $result.SupplementalCleanup = $SupplementalCleanup
            }
            Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
            if ($violations) { return 1 }; return 0
        }
        $head = Resolve-EvidenceCommit $Repository $HeadRef
        Assert-EvidenceAncestor $Repository $base $head
        Write-Host "EVIDENCE inventory base=$base suppliedCount=$count suppliedBytes=$bytes suppliedDigest=$InventoryPathSha256 count=$($delete.Count) bytes=$total digest=$digest anchor=$signature kept=$($keep.Count) head=$head"
        if ($count -ne $delete.Count) { Write-Host 'EVIDENCE deletion violation reason=inventory_count'; $violations++ }
        if ($bytes -ne $total) { Write-Host 'EVIDENCE deletion violation reason=inventory_bytes'; $violations++ }
        if ($InventoryPathSha256.ToLowerInvariant() -cne $digest) { Write-Host 'EVIDENCE deletion violation reason=inventory_digest'; $violations++ }
        $marked = @(foreach ($commit in @(Read-EvidenceCommits $Repository $base $head)) {
            $message = ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('show', '-s', '--format=%B', $commit))
            $trailers = ConvertFrom-EvidenceUtf8 (Read-EvidenceGit $Repository @('show', '-s', '--format=%(trailers:only,unfold)', $commit))
            if ($trailers.Replace("`r`n", "`n").Split("`n") -ccontains $deletionMarker) { $commit }
        })
        if ($marked.Count -ne 1) { Write-Host 'EVIDENCE deletion violation reason=deletion_unique'; return 1 }
        $deletion = $marked[0]
        $parents = @(Read-EvidenceParents $Repository $deletion)
        if ($parents.Count -ne 1) { Write-Host 'EVIDENCE deletion violation reason=deletion_parent'; return 1 }
        Assert-EvidenceAncestor $Repository $base $parents[0]
        Assert-EvidenceAncestor $Repository $deletion $head
        $actual = @(Read-EvidenceDiff $Repository $deletion $parents[0])
        foreach ($entry in $actual) { if ($entry.Status -cne 'D') { Write-Host 'EVIDENCE deletion violation reason=deletion_only'; $violations++ } }
        $actualMap = Get-EvidenceMap $actual
        $expectedMap = Get-EvidenceMap $delete
        foreach ($entry in $delete) { if (-not $actualMap.ContainsKey($entry.Path)) { Write-Host 'EVIDENCE deletion violation reason=deletion_missing'; $violations++ } }
        foreach ($entry in $actual) { if (-not $expectedMap.ContainsKey($entry.Path)) { Write-Host 'EVIDENCE deletion violation reason=deletion_extra'; $violations++ } }
        foreach ($entry in $actual) {
            if (-not $expectedMap.ContainsKey($entry.Path)) { continue }
            if ($entry.OldOid -cne $expectedMap[$entry.Path].Oid) { Write-Host 'EVIDENCE deletion violation reason=deletion_old_oid'; $violations++ }
            if ($entry.OldMode -cne $expectedMap[$entry.Path].Mode) { Write-Host 'EVIDENCE deletion violation reason=deletion_old_mode'; $violations++ }
        }
        $finalTree = @(Read-EvidenceTree $Repository $head)
        $finalMap = Get-EvidenceMap $finalTree
        foreach ($entry in $delete) { if ($finalMap.ContainsKey($entry.Path)) { Write-Host 'EVIDENCE deletion violation reason=deletion_resurrection'; $violations++ } }
        foreach ($entry in $keep) {
            if (-not $finalMap.ContainsKey($entry.Path)) { Write-Host 'EVIDENCE deletion violation reason=kept_present'; $violations++; continue }
            if ($finalMap[$entry.Path].Oid -cne $entry.Oid) { Write-Host 'EVIDENCE deletion violation reason=kept_oid'; $violations++ }
            if ($finalMap[$entry.Path].Mode -cne $entry.Mode) { Write-Host 'EVIDENCE deletion violation reason=kept_mode'; $violations++ }
        }
        $finalViolations = 0
        foreach ($entry in $finalTree) {
            if (Get-EvidenceViolation $entry.Path $entry.Mode $entry.Bytes) { $finalViolations++; Write-EvidenceViolation $head $entry 'final_tree_policy' $entry.Bytes }
        }
        $violations += $finalViolations
        $recoverable = 0
        foreach ($entry in $delete) {
            if ($entry.Type -ceq 'blob') { [void](Read-EvidenceGit $Repository @('cat-file', '-e', $entry.Oid)); $recoverable++ }
        }
        Write-Host "EVIDENCE deletion result base=$base deletion=$deletion head=$head deleted=$($delete.Count) bytes=$total digest=$digest kept=$($keep.Count) finalTreeViolations=$finalViolations recoverable=$recoverable violations=$violations"
        if ($violations) { return 1 }; return 0
    } catch { Write-Host ('EVIDENCE error reason=' + $_.Exception.Message); return 2 }
}
