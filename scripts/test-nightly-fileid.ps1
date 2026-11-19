# A-2 / A-3. The cache identity on Windows prefers the 128-bit FileIdInfo id and
# falls back to the 64-bit file index. Unknown is never equal, including to
# itself. A different id does not hit the hash cache. HistoryNodeIdentity.Read
# is the production factory. The real FileIdInfo query is Windows-only; the
# desktop observation is:
#   pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryCacheAgrees
# This file stays free of double-quoted subexpressions so the OS-witness lexer can read it.
function Test-NCDiscoveryCacheFileId {
    $dir = Join-Path $root 'discovery-file-id'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $path = Join-Path $dir 'suite.bin'
    $a = [Text.Encoding]::UTF8.GetBytes('alpha-content-one')
    $b = [Text.Encoding]::UTF8.GetBytes('bravo-content-two')
    $c = [Text.Encoding]::UTF8.GetBytes('charlie-content-x')
    [IO.File]::WriteAllBytes($path, $a)
    [IO.File]::SetLastWriteTimeUtc($path, [DateTime]::UtcNow.AddMinutes(-1))
    $kept = [IO.File]::GetLastWriteTimeUtc($path)
    $id128 = New-Object byte[] 16
    $other = New-Object byte[] 16
    for ($i = 0; $i -lt 16; $i++) { $id128[$i] = [byte]($i + 1); $other[$i] = [byte]($i + 1) }
    $other[15] = 99
    $short = New-Object byte[] 15
    $full = [Antiphon.Nightly.CacheFileIdentity]::Decide($true, [uint64]9, $id128, $true, [uint32]9, [uint64]100)
    $fullOtherIndex = [Antiphon.Nightly.CacheFileIdentity]::Decide($true, [uint64]9, $id128, $true, [uint32]9, [uint64]999)
    $different = [Antiphon.Nightly.CacheFileIdentity]::Decide($true, [uint64]9, $other, $true, [uint32]9, [uint64]100)
    $otherVolume = [Antiphon.Nightly.CacheFileIdentity]::Decide($true, [uint64]8, $id128, $true, [uint32]9, [uint64]100)
    $fallback = [Antiphon.Nightly.CacheFileIdentity]::Decide($false, [uint64]0, $null, $true, [uint32]9, [uint64]100)
    $fallbackOther = [Antiphon.Nightly.CacheFileIdentity]::Decide($false, [uint64]0, $null, $true, [uint32]9, [uint64]101)
    $unknown = [Antiphon.Nightly.CacheFileIdentity]::Decide($false, [uint64]0, $null, $false, [uint32]0, [uint64]0)
    $malformed = [Antiphon.Nightly.CacheFileIdentity]::Decide($true, [uint64]9, $short, $true, [uint32]9, [uint64]100)
    $malformedUnknown = [Antiphon.Nightly.CacheFileIdentity]::Decide($true, [uint64]9, $short, $false, [uint32]9, [uint64]100)
    Assert-NC ($full.Known -and ($full.Kind.ToString() -ceq 'FileId128') -and [Antiphon.Nightly.CacheIdentity]::Same($full, $fullOtherIndex)) 'fileId128' ('kind=' + $full.Kind)
    Assert-NC (-not [Antiphon.Nightly.CacheIdentity]::Same($full, $different)) 'fileId-differs' 'different 128-bit id compared equal'
    Assert-NC (-not [Antiphon.Nightly.CacheIdentity]::Same($full, $otherVolume)) 'volume-differs' 'different volume compared equal'
    Assert-NC ($fallback.Known -and ($fallback.Kind.ToString() -ceq 'FileIndex64') -and [Antiphon.Nightly.CacheIdentity]::Same($fallback, $fallback) -and -not [Antiphon.Nightly.CacheIdentity]::Same($fallback, $fallbackOther)) 'fallback' ('kind=' + $fallback.Kind)
    Assert-NC (-not [Antiphon.Nightly.CacheIdentity]::Same($full, $fallback)) 'kind-differs' '128-bit id compared equal to the 64-bit fallback'
    Assert-NC ((-not $unknown.Known) -and -not [Antiphon.Nightly.CacheIdentity]::Same($unknown, $unknown) -and -not [Antiphon.Nightly.CacheIdentity]::Same($unknown, $fallback) -and -not [Antiphon.Nightly.CacheIdentity]::Same($unknown, $full)) 'unknown' 'unknown identity compared equal'
    Assert-NC ($malformed.Known -and ($malformed.Kind.ToString() -ceq 'FileIndex64') -and [Antiphon.Nightly.CacheIdentity]::Same($malformed, $fallback) -and -not $malformedUnknown.Known) 'malformed' ('kind=' + $malformed.Kind)
    $node = [Antiphon.Nightly.HistoryNodeIdentity]::Read($path, $false)
    Assert-NC ($null -ne $node.CacheId -and $node.CacheId.Known -and -not $node.Missing -and -not $node.Linked) 'read-cache-id' ('kind=' + $(if ($null -eq $node.CacheId) { 'null' } else { $node.CacheId.Kind }))
    [Antiphon.Nightly.NightlyClassNames]::ClearHashes()
    [Antiphon.Nightly.CacheFileIdentity]::TestOverride = $null
    try {
        [Antiphon.Nightly.CacheFileIdentity]::TestOverride = $full
        $hash1 = [Antiphon.Nightly.NightlyClassNames]::CachedFileSha256($path)
        [IO.File]::WriteAllBytes($path, $b)
        [IO.File]::SetLastWriteTimeUtc($path, $kept)
        [Antiphon.Nightly.CacheFileIdentity]::TestOverride = $different
        $hash2 = [Antiphon.Nightly.NightlyClassNames]::CachedFileSha256($path)
        $diskB = [Antiphon.Nightly.NightlyClassNames]::FileSha256($path)
        Assert-NC (($a.Length -eq $b.Length) -and ($hash2 -ceq $diskB) -and ($hash2 -cne $hash1)) 'different-id-miss' ('hash1=' + $hash1 + ' hash2=' + $hash2 + ' disk=' + $diskB)
        $hash3 = [Antiphon.Nightly.NightlyClassNames]::CachedFileSha256($path)
        Assert-NC ($hash3 -ceq $hash2) 'same-id-hit' ('hash3=' + $hash3 + ' hash2=' + $hash2)
        [IO.File]::WriteAllBytes($path, $c)
        [IO.File]::SetLastWriteTimeUtc($path, $kept)
        [Antiphon.Nightly.CacheFileIdentity]::TestOverride = $unknown
        $hash4 = [Antiphon.Nightly.NightlyClassNames]::CachedFileSha256($path)
        $diskC = [Antiphon.Nightly.NightlyClassNames]::FileSha256($path)
        Assert-NC (($c.Length -eq $b.Length) -and ($hash4 -ceq $diskC) -and ($hash4 -cne $hash2)) 'unknown-miss' ('hash4=' + $hash4 + ' disk=' + $diskC + ' stale=' + $hash2)
    } finally {
        [Antiphon.Nightly.CacheFileIdentity]::TestOverride = $null
        [Antiphon.Nightly.NightlyClassNames]::ClearHashes()
    }
}
