#requires -Version 7.0
# CARD-1313. Selection intent is data, never execution or release authority.
function ConvertTo-TestSelectionCanonicalJson {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return ConvertTo-Json -InputObject $Value -Compress }
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($Value -is [System.Collections.IDictionary]) {
        $keys = [string[]]@($Value.Keys); [Array]::Sort($keys, [StringComparer]::Ordinal)
        $parts = foreach ($key in $keys) { (ConvertTo-TestSelectionCanonicalJson $key) + ':' + (ConvertTo-TestSelectionCanonicalJson $Value[$key]) }
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = foreach ($item in $Value) { ConvertTo-TestSelectionCanonicalJson $item }
        return '[' + ($parts -join ',') + ']'
    }
    if ($Value -is [ValueType]) { return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture) }
    $map = @{}; foreach ($p in $Value.PSObject.Properties) { $map[$p.Name] = $p.Value }
    return ConvertTo-TestSelectionCanonicalJson $map
}

function Get-TestSelectionDigest {
    param($Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-TestSelectionCanonicalJson $Value))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-TestSelectionCatalogHash {
    param($Catalog)
    $copy = @{}; foreach ($key in $Catalog.Keys) { if ($key -cne 'catalogHash') { $copy[$key] = $Catalog[$key] } }
    return Get-TestSelectionDigest $copy
}

function Assert-TestSelectionKeys {
    param($Object, [string[]]$Allowed)
    if ($Object -isnot [Collections.IDictionary]) { throw 'invalid-catalog-object' }
    foreach ($key in $Object.Keys) { if ($Allowed -cnotcontains $key) { throw "invalid-catalog-field:$key" } }
}

function Assert-TestSelectionCatalog {
    param($Catalog)
    Assert-TestSelectionKeys $Catalog @('schemaVersion','catalogHash','sets','rules','prerequisites','edges','activation')
    if ($Catalog.schemaVersion -isnot [long] -and $Catalog.schemaVersion -isnot [int]) { throw 'catalog-schema-unsupported' }
    if ($Catalog.schemaVersion -ne 1) { throw 'catalog-schema-unsupported' }
    if ($Catalog.catalogHash -cne (Get-TestSelectionCatalogHash $Catalog)) { throw 'catalog-hash-stale' }
    if ($Catalog.activation -cne 'unqualified') { throw 'selection-cannot-qualify-itself' }
    $sets = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($set in $Catalog.sets) {
        Assert-TestSelectionKeys $set @('id','members','remainderProjects','sourceInputs','dependsOn','resources','platforms','categories','eligibility','owner','reason','prerequisites')
        if ([string]$set.id -notmatch '^[a-z0-9.-]+$' -or $sets.ContainsKey($set.id)) { throw 'invalid-set-id' }
        $sets.Add($set.id,$set)
        if ($set.eligibility -cnotin @('unattended','manual','unclassified')) { throw 'invalid-eligibility' }
        if (-not $set.owner -or -not $set.reason -or @($set.platforms).Count -eq 0) { throw 'missing-disposition' }
        Assert-TestSelectionKeys $set.resources @('vendorExecutable','interaction','api','browser','desktopUi','nativePty','postgres','docker','broker')
        foreach ($key in @('vendorExecutable','interaction','api','browser','desktopUi','nativePty','postgres','docker','broker')) {
            if (-not $set.resources.Contains($key)) { throw 'unknown-resource' }
            if ($set.resources[$key] -is [string] -and $set.resources[$key] -ceq 'unknown' -and $set.eligibility -ceq 'unattended') { throw 'unknown-resource' }
        }
        if ($set.resources.vendorExecutable -cnotin @('none','claude','codex','grok','unknown') -or $set.resources.interaction -cnotin @('none','native-probe','tui','unknown') -or $set.resources.api -cnotin @('none','stub','live','unknown')) { throw 'invalid-resource' }
        foreach ($key in @('browser','desktopUi','nativePty','postgres','docker','broker')) {
            if ($set.resources[$key] -isnot [bool] -and $set.resources[$key] -cne 'unknown') { throw 'invalid-resource' }
        }
        foreach ($member in $set.members) {
            Assert-TestSelectionKeys $member @('project','class','method','script')
            if (-not $member.project -and -not $member.script) { throw 'invalid-member' }
            foreach ($key in @('class','method')) { if ($member[$key] -and $member[$key] -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') { throw 'invalid-member' } }
        }
    }
    if (-not $sets.ContainsKey('core')) { throw 'missing-core' }
    $prereqs = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($p in $Catalog.prerequisites) {
        Assert-TestSelectionKeys $p @('id','dependsOn','reason')
        if (-not $p.id -or $prereqs.ContainsKey($p.id)) { throw 'invalid-prerequisite' }
        $prereqs.Add($p.id,$p)
    }
    function Visit-SelectionNode($id,$map,$visiting,$done) {
        if (-not $map.ContainsKey($id)) { throw "missing-dependency:$id" }
        if ($done.Contains($id)) { return }
        if (-not $visiting.Add($id)) { throw "dependency-cycle:$id" }
        foreach ($target in $map[$id].dependsOn) { Visit-SelectionNode $target $map $visiting $done }
        [void]$visiting.Remove($id); [void]$done.Add($id)
    }
    foreach ($map in @($sets,$prereqs)) {
        $visiting = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $done = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($id in $map.Keys) { Visit-SelectionNode $id $map $visiting $done }
    }
    foreach ($set in $sets.Values) { foreach ($p in $set.prerequisites) { if (-not $prereqs.ContainsKey($p)) { throw "missing-prerequisite:$p" } } }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rule in $Catalog.rules) {
        Assert-TestSelectionKeys $rule @('id','paths','pattern','sets','full','partition')
        if (-not $ids.Add([string]$rule.id)) { throw 'duplicate-rule' }
        if (@($rule.paths).Count -eq 0 -and -not $rule.pattern) { throw 'invalid-rule' }
        if ($rule.pattern) {
            if (-not $rule.pattern.StartsWith('^') -or -not $rule.pattern.EndsWith('$') -or $rule.pattern.Length -gt 2048) { throw 'invalid-rule' }
            try { [void][regex]::new($rule.pattern,[Text.RegularExpressions.RegexOptions]::CultureInvariant,[TimeSpan]::FromMilliseconds(100)) } catch { throw 'invalid-rule' }
        }
        foreach ($s in $rule.sets) { if (-not $sets.ContainsKey($s)) { throw "missing-dependency:$s" } }
    }
    foreach ($edge in $Catalog.edges) {
        Assert-TestSelectionKeys $edge @('id','path','sets','kind')
        if (-not $edge.path -or -not $edge.id) { throw 'invalid-edge' }
        foreach ($s in $edge.sets) { if (-not $sets.ContainsKey($s)) { throw "missing-dependency:$s" } }
    }
    return $Catalog
}

function Read-TestSelectionCatalog {
    param([string]$Path, [string]$ExpectedHash)
    $catalog = [IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable
    [void](Assert-TestSelectionCatalog $catalog)
    if ($ExpectedHash -cne $catalog.catalogHash) { throw 'catalog-pin-mismatch' }
    return $catalog
}
