#requires -Version 5.1
# CARD-1836 S2: pure boundary metadata and identities. No launch/selection caller.
# Keep the rule order and glob grammar aligned with LayeringGuardEngine.ParseMap.
function Expand-RcBoundaryGlob {
    param([string]$Pattern)
    $depth = 0; $start = 0
    for ($i = 0; $i -lt $Pattern.Length; $i++) {
        if ($Pattern[$i] -eq '{') { $depth++ }
        if ($Pattern[$i] -eq '}') { $depth-- }
        if ($depth -lt 0 -or $depth -gt 1) { throw 'boundary-map: unbalanced glob' }
        if ($Pattern[$i] -eq ',' -and $depth -eq 0) {
            Expand-RcBoundaryGlob $Pattern.Substring($start, $i - $start).Trim()
            $start = $i + 1
        }
    }
    if ($depth -ne 0) { throw 'boundary-map: unbalanced glob' }
    $last = $Pattern.Substring($start).Trim()
    $open = $last.IndexOf('{')
    if ($open -ge 0) {
        $close = $last.IndexOf('}', $open)
        foreach ($alternative in $last.Substring($open + 1, $close - $open - 1).Split(',')) {
            Expand-RcBoundaryGlob ($last.Substring(0, $open) + $alternative + $last.Substring($close + 1))
        }
    } else {
        if (-not $last -or $last.StartsWith('/') -or $last.Contains('\') -or $last.Split('/') -contains '..' -or $last -match '[^A-Za-z0-9_./*\-]') {
            throw 'boundary-map: invalid glob'
        }
        $last
    }
}

function Read-RcBoundaryMap {
    param(
        [string]$LayersPath = (Join-Path $PSScriptRoot '../../tests/Antiphon.Tests/Layering/layers.txt'),
        [string]$PrefixesPath = (Join-Path $PSScriptRoot 'rc-boundary-prefixes.json')
    )
    $layers = @('host','core','persistence','git','runners','integrations','application')
    $edges = @{}; $rules = New-Object 'System.Collections.Generic.List[object]'; $section = ''
    foreach ($raw in [IO.File]::ReadAllLines($LayersPath)) {
        $line = $raw.Split('#')[0].Trim()
        if (-not $line) { continue }
        if ($line -cin @('[edges]','[rules]')) { $section = $line; continue }
        if ($section -ceq '[edges]') {
            if ($line -notmatch '^([A-Za-z]+)\s*->\s*(.*)$') { throw 'boundary-map: malformed edge' }
            $from = $Matches[1].ToLowerInvariant(); $targets = $Matches[2]
            if ($from -cnotin $layers -or $edges.ContainsKey($from)) { throw 'boundary-map: unknown/duplicate edge' }
            $destinations = @($targets.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
            foreach ($to in $destinations) { if ($to -cnotin $layers) { throw 'boundary-map: unknown destination' } }
            $edges[$from] = $destinations
        } elseif ($section -ceq '[rules]') {
            if ($line -notmatch '^(.+?)\s+([A-Za-z]+)$') { throw 'boundary-map: malformed rule' }
            $pattern = $Matches[1]; $boundary = $Matches[2].ToLowerInvariant()
            if ($boundary -cnotin $layers) { throw 'boundary-map: unknown layer' }
            $globs = @(foreach ($glob in @(Expand-RcBoundaryGlob $pattern)) {
                $regex = '^' + [regex]::Escape($glob).Replace('\*\*', [string][char]1).Replace('\*','[^/]*').Replace([string][char]1,'.*') + '$'
                New-Object regex $regex, ([Text.RegularExpressions.RegexOptions]::CultureInvariant)
            })
            $rules.Add(@{ boundary=$boundary; globs=$globs })
        } else { throw 'boundary-map: unknown section' }
    }
    foreach ($layer in $layers) { if (-not $edges.ContainsKey($layer)) { throw ('boundary-map: missing edges ' + $layer) } }
    if ($rules.Count -eq 0) { throw 'boundary-map: no rules' }
    $table = [IO.File]::ReadAllText($PrefixesPath) | ConvertFrom-Json
    if ($table.format -cne 'antiphon-rc-boundary-prefixes-v1') { throw 'boundary-map: invalid prefixes format' }
    foreach ($entry in $table.prefixes) {
        if ($entry.boundary -cnotin $layers -or $entry.prefix -cnotmatch '^Antiphon\.Tests(?:\.[A-Za-z_][A-Za-z0-9_]*)+$' -or $entry.prefix -ceq 'Antiphon.Tests.Infrastructure') {
            throw 'boundary-map: invalid namespace prefix'
        }
    }
    return @{ edges=$edges; rules=@($rules.ToArray()); prefixes=@($table.prefixes | Sort-Object { -$_.prefix.Length }) }
}

function Get-RcClassBoundary {
    param([Parameter(Mandatory)]$Map, [string]$Suite, [string]$TestPath, [string]$Source)
    # Lane overrides are intentionally ahead of production references and namespaces.
    $path = $TestPath.Replace('\','/')
    foreach ($lane in @(@('Scripts','scripts'),@('Checkpoints','checkpoints'),@('AgentTui','tui'))) {
        if ($path.StartsWith(('tests/Antiphon.Tests/' + $lane[0] + '/'), [StringComparison]::Ordinal)) { return $lane[1] }
    }
    if ($path.StartsWith('tests/Antiphon.E2E/', [StringComparison]::Ordinal)) { return 'e2e' }
    if ($path.StartsWith('client/', [StringComparison]::Ordinal)) { return 'client' }
    if ($Suite -cin @('scripts','checkpoints','tui','e2e','client','messaging','session-runner','pty-host','agents-pty','observability','server-git','markdown-pdf')) { return $Suite }
    if ($Suite -cne 'antiphon') { return 'unassigned' }
    # Literal repo paths only: computed references remain unknown. Each path keeps
    # the first matching rule. Two paths that resolve to different boundaries are
    # conflicting evidence and fail closed to unassigned.
    $paths = @([regex]::Matches($Source, '"(server/[^"\r\n]+)"') | ForEach-Object { $_.Groups[1].Value })
    $matched = New-Object 'System.Collections.Generic.List[string]'
    foreach ($literal in @($paths)) {
        if ($literal.Split('/') -contains '..') { continue }
        foreach ($rule in $Map.rules) {
            $hit = $false
            foreach ($glob in @($rule.globs)) {
                if ($glob.IsMatch($literal)) { $hit = $true; break }
            }
            if ($hit) { $matched.Add([string]$rule.boundary); break }
        }
    }
    if ($matched.Count -gt 0) {
        $chosen = [string]$matched[0]
        foreach ($candidate in $matched) {
            if ([string]$candidate -cne $chosen) { return 'unassigned' }
        }
        return $chosen
    }
    $ns = [regex]::Match($Source, '\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]')
    if ($ns.Success) {
        foreach ($entry in $Map.prefixes) {
            if ($ns.Groups[1].Value -ceq $entry.prefix -or $ns.Groups[1].Value.StartsWith(($entry.prefix + '.'), [StringComparison]::Ordinal)) { return $entry.boundary }
        }
    }
    return 'unassigned'
}

function Add-RcBoundaryU32 {
    param(
        [Parameter(Mandatory)][System.IO.MemoryStream]$Buffer,
        [Parameter(Mandatory)][uint32]$Value
    )
    $bytes = [BitConverter]::GetBytes($Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }
    $Buffer.Write($bytes, 0, $bytes.Length)
}

function Add-RcBoundaryString {
    param(
        [Parameter(Mandatory)][System.IO.MemoryStream]$Buffer,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory)][Text.UTF8Encoding]$Encoder
    )
    $bytes = $Encoder.GetBytes($Value)
    Add-RcBoundaryU32 -Buffer $Buffer -Value ([uint32]$bytes.Length)
    $Buffer.Write($bytes, 0, $bytes.Length)
}

function Sort-RcBoundaryIdentities {
    param([object[]]$Identities)
    [object[]]$items = @($Identities)
    [string[]]$keys = New-Object string[] $items.Length
    $nul = [string][char]0
    $escapedNul = $nul + [char]1
    $separator = $nul + $nul
    for ($i = 0; $i -lt $items.Length; $i++) {
        # Escape NUL as 0,1 and separate fields with 0,0. Thus a field's end
        # sorts before any continuation, even an embedded NUL. Ordinal key
        # order is exactly ordinal (class, method, caseKey) tuple order.
        $keys[$i] = ([string]$items[$i].class).Replace($nul, $escapedNul) + $separator +
            ([string]$items[$i].method).Replace($nul, $escapedNul) + $separator +
            ([string]$items[$i].caseKey).Replace($nul, $escapedNul)
    }
    [Array]::Sort($keys, $items, [StringComparer]::Ordinal)
    return $items
}

function Get-RcBoundaryCanonicalDigest {
    param([Parameter(Mandatory)][string]$Suite, [Parameter(Mandatory)][string]$Boundary, [Parameter(Mandatory)][object[]]$Identities)
    # Length-delimited UTF-8, no BOM: suite, boundary, uint32 count, then each
    # identity as class, method, caseKey. No ConvertTo-Json escaping.
    $buffer = New-Object System.IO.MemoryStream
    $encoder = New-Object Text.UTF8Encoding($false, $true)
    try {
        Add-RcBoundaryString -Buffer $buffer -Value $Suite -Encoder $encoder
        Add-RcBoundaryString -Buffer $buffer -Value $Boundary -Encoder $encoder
        Add-RcBoundaryU32 -Buffer $buffer -Value ([uint32]@($Identities).Count)
        foreach ($identity in @($Identities)) {
            # Keep the length framing in this hot loop: a PowerShell function
            # invocation per field and prefix dominates large memberships.
            foreach ($value in @([string]$identity.class, [string]$identity.method, [string]$identity.caseKey)) {
                $bytes = $encoder.GetBytes($value)
                $length = [BitConverter]::GetBytes([uint32]$bytes.Length)
                if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($length) }
                $buffer.Write($length, 0, $length.Length)
                $buffer.Write($bytes, 0, $bytes.Length)
            }
        }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($buffer.ToArray())).Replace('-', '').ToLowerInvariant()) }
        finally { $sha.Dispose() }
    } finally { $buffer.Dispose() }
}

function ConvertTo-RcBoundaryIdentity {
    param([Parameter(Mandatory)][string]$IdentityKey, [AllowEmptyString()][string]$CaseKey = '')
    if ([string]::IsNullOrWhiteSpace($IdentityKey) -or $IdentityKey -notmatch '^[^/\r\n]+/[^\r\n]+$') { throw 'boundary-chunk: invalid/duplicate identity' }
    if ($null -eq $CaseKey -or $CaseKey -match '[\r\n]') { throw 'boundary-chunk: invalid/duplicate identity' }
    $slash = $IdentityKey.IndexOf([char]'/')
    return @{ class = $IdentityKey.Substring(0, $slash); method = $IdentityKey.Substring($slash + 1); caseKey = [string]$CaseKey }
}

function Get-RcBoundaryMembershipKey {
    param([string]$Suite, [string]$IdentityKey, [string]$CaseKey)
    # Length framing keeps embedded NULs and delimiters unambiguous. Boundary
    # is deliberately absent: one suite/case cannot belong to two boundaries.
    return ($Suite.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + ':' + $Suite +
        $IdentityKey.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + ':' + $IdentityKey +
        $CaseKey.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + ':' + $CaseKey)
}

function New-RcBoundaryChunkData {
    param([string]$Suite, [string]$Boundary, [string[]]$IdentityKeys, [string[]]$CaseKeys)
    if ($Suite -cnotmatch '^[a-z][a-z0-9-]*$' -or $Boundary -cnotin @('host','core','persistence','git','runners','integrations','application','scripts','checkpoints','tui','e2e','client','messaging','session-runner','pty-host','agents-pty','observability','server-git','markdown-pdf','unassigned')) { throw 'boundary-chunk: invalid suite/boundary' }
    if (-not $IdentityKeys -or $IdentityKeys.Count -eq 0) { throw 'boundary-chunk: empty membership' }
    $hasCaseKeys = $PSBoundParameters.ContainsKey('CaseKeys')
    if ($hasCaseKeys -and @($CaseKeys).Count -ne @($IdentityKeys).Count) { throw 'boundary-chunk: case key count' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $parsed = New-Object 'System.Collections.Generic.List[object]'
    for ($i = 0; $i -lt @($IdentityKeys).Count; $i++) {
        $key = [string]$IdentityKeys[$i]
        $caseKey = ''
        if ($hasCaseKeys) { $caseKey = [string]$CaseKeys[$i] }
        $seenKey = Get-RcBoundaryMembershipKey $Suite $key $caseKey
        if (-not $seen.Add($seenKey)) { throw 'boundary-chunk: invalid/duplicate identity' }
        $parsed.Add((ConvertTo-RcBoundaryIdentity -IdentityKey $key -CaseKey $caseKey))
    }
    $sorted = @(Sort-RcBoundaryIdentities -Identities @($parsed.ToArray()))
    $hex = Get-RcBoundaryCanonicalDigest -Suite $Suite -Boundary $Boundary -Identities $sorted
    $id = 'b-' + $Boundary + '-' + $hex.Substring(0, 16)
    [string[]]$keys = @($sorted | ForEach-Object { [string]$_.class + '/' + [string]$_.method })
    [string[]]$cases = @($sorted | ForEach-Object { [string]$_.caseKey })
    return [ordered]@{ chunkId=$id; suite=$Suite; boundary=$Boundary; identityKeys=@($keys); caseKeys=@($cases); fullDigest=$hex; collision=$false; alwaysDirty=($Boundary -ceq 'unassigned') }
}

function Get-RcBoundaryChunkId {
    param([string]$Suite, [string]$Boundary, [string[]]$IdentityKeys, [string[]]$CaseKeys)
    return (New-RcBoundaryChunkData @PSBoundParameters).chunkId
}

function New-RcBoundaryChunk {
    param([object[]]$Identities)
    if (-not $Identities -or @($Identities).Count -eq 0) { throw 'boundary-chunk: empty membership' }
    $rows = @($Identities)
    $suite = [string]$rows[0].suite; $boundary = [string]$rows[0].boundary
    $keys = New-Object 'System.Collections.Generic.List[string]'
    $caseKeys = New-Object 'System.Collections.Generic.List[string]'
    foreach ($identity in $rows) {
        if ([string]$identity.suite -cne $suite -or [string]$identity.boundary -cne $boundary) { throw 'boundary-chunk: mixed suite/boundary' }
        $caseKey = ''
        if ($null -ne $identity.caseKey) { $caseKey = [string]$identity.caseKey }
        $keys.Add([string]$identity.identityKey)
        $caseKeys.Add($caseKey)
    }
    return New-RcBoundaryChunkData -Suite $suite -Boundary $boundary -IdentityKeys $keys.ToArray() -CaseKeys $caseKeys.ToArray()
}

function Resolve-RcBoundaryChunks {
    param([Parameter(Mandatory)][object[]]$Memberships)
    # Group by the 16-hex prefix. Two different full digests under one prefix
    # are rebuilt as unassigned and stay dirty, keyed by the full digest.
    # Membership may occur once per suite, irrespective of boundary or chunk.
    $built = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($membership in @($Memberships)) {
        $rows = New-Object 'System.Collections.Generic.List[object]'
        foreach ($identity in @($membership.identities)) {
            $row = @{
                suite = [string]$membership.suite
                boundary = [string]$membership.boundary
                identityKey = [string]$identity.identityKey
            }
            if ($null -ne $identity.caseKey) { $row.caseKey = [string]$identity.caseKey }
            $key = Get-RcBoundaryMembershipKey $row.suite $row.identityKey ([string]$row.caseKey)
            if (-not $seen.Add($key)) { throw 'boundary-chunk: duplicate membership across chunks' }
            $rows.Add($row)
        }
        $built.Add((New-RcBoundaryChunk -Identities @($rows.ToArray())))
    }
    $digests = @{}
    foreach ($chunk in $built) {
        $prefix = ([string]$chunk.fullDigest).Substring(0, 16)
        if (-not $digests.ContainsKey($prefix)) {
            $digests[$prefix] = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        }
        [void]$digests[$prefix].Add([string]$chunk.fullDigest)
    }
    $result = New-Object 'System.Collections.Generic.List[object]'
    foreach ($chunk in $built) {
        $prefix = ([string]$chunk.fullDigest).Substring(0, 16)
        if ($digests[$prefix].Count -gt 1) {
            $rows = New-Object 'System.Collections.Generic.List[object]'
            $keys = @($chunk.identityKeys)
            $cases = @($chunk.caseKeys)
            for ($i = 0; $i -lt $keys.Count; $i++) {
                $rows.Add(@{
                    suite = [string]$chunk.suite
                    boundary = 'unassigned'
                    identityKey = [string]$keys[$i]
                    caseKey = [string]$cases[$i]
                })
            }
            $rebuilt = New-RcBoundaryChunk -Identities @($rows.ToArray())
            $rebuilt['collision'] = $true
            $rebuilt['chunkId'] = 'b-unassigned-' + $rebuilt.fullDigest
            $result.Add($rebuilt)
        } else {
            $result.Add($chunk)
        }
    }
    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($chunk in $result) {
        if (-not $ids.Add([string]$chunk.chunkId)) { throw 'boundary-chunk: duplicate resolved chunk id' }
    }
    return @($result.ToArray())
}
