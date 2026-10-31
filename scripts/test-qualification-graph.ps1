#requires -Version 5.1
param([ValidateSet('SchemaAndIdentity')][string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
. "$PSScriptRoot/lib/c487-harness.ps1"
$fixtures = Join-Path $PSScriptRoot 'fixtures/qualification-graph/s1'
$schema = Join-Path $PSScriptRoot '../docs/schemas/qualification-run-plan-v1.schema.json'
$repo = Split-Path -Parent $PSScriptRoot
function Check($ok, [string]$id, [string]$detail = '') { Assert-C487 ([bool]$ok) ("QGRAPH SchemaAndIdentity/" + $id) $detail }
function Load([string]$name) { return [IO.File]::ReadAllText((Join-Path $fixtures $name)) }
function Copy-Graph($g) { return ($g | ConvertTo-Json -Depth 64 -Compress | ConvertFrom-Json) }
function Json($g) { return ConvertTo-Json -InputObject $g -Depth 64 -Compress }
function Independent-Hash([string]$text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Inventory([string]$path) {
    return (@(Get-ChildItem -LiteralPath $path -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join [Environment]::NewLine)
}
function Edit-Graph($graph, $recipe) {
    # Changing one logical target identity must retain coherent references;
    # otherwise a dangling reference would mask the registry guard's mutant.
    if ($recipe.path -ceq '/targets/0/id') {
        $oldId = $graph.targets[0].id
        foreach ($node in $graph.nodes) { if ($node.targetId -ceq $oldId) { $node.targetId = $recipe.value } }
    }
    $parts = $recipe.path.TrimStart('/').Split('/')
    if ($recipe.path -eq '') { return $recipe.value }
    $parent = $graph
    for ($j = 0; $j -lt $parts.Count - 1; $j++) {
        if ($parent -is [array]) { $parent = $parent[[int]$parts[$j]] } else { $parent = $parent.($parts[$j]) }
    }
    $key = $parts[-1]
    if ($recipe.op -eq 'remove') { $parent.PSObject.Properties.Remove($key) }
    elseif ($parent -is [array]) { $parent[[int]$key] = $recipe.value }
    else { $parent | Add-Member -NotePropertyName $key -NotePropertyValue $recipe.value -Force }
    return $graph
}
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $env:TEMP ('qgraph-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$canary = Join-Path $ResultsDirectory 'input-canary.txt'
[IO.File]::WriteAllText($canary, 'nonempty owned input')
$beforeInputs = Inventory $fixtures
$beforeScratch = Inventory $ResultsDirectory
# Fail on declared execution/network/writer seams; Review audits the full footprint.
function Start-Process { throw 'unexpected-child' }
function Invoke-WebRequest { throw 'unexpected-http' }
function Invoke-RestMethod { throw 'unexpected-runner' }
function Write-NightlyAtomicJson { throw 'unexpected-receipt' }
. "$PSScriptRoot/lib/qualification-graph.ps1"
$setupSeconds = $clock.Elapsed.TotalSeconds
Check ([bool](Get-Command Read-QualificationGraph -ErrorAction SilentlyContinue)) 'production-surface'
$catalogJson = Load 'catalog.json'
$pins = (Load 'identity.expected.json' | ConvertFrom-Json).catalogPins
$pinsOk = (Independent-Hash $catalogJson) -ceq (Load 'catalog.sha256').Trim()
foreach ($pin in $pins.PSObject.Properties) { $pinsOk = $pinsOk -and (Independent-Hash (Load $pin.Name)) -ceq $pin.Value }
Check $pinsOk 'catalog-pin'
$graphJson = Load 'execute.valid.json'
$base = $graphJson | ConvertFrom-Json
$healthy = Read-QualificationGraph $graphJson $catalogJson $schema
foreach ($entry in @(
    @('valid-prepare', 'prepare.valid.json'),
    @('valid-execute', 'execute.valid.json'),
    @('valid-sentinel', 'execute-preparation-failed.valid.json'))) {
    $text = Load $entry[1]
    $result = Read-QualificationGraph $text $catalogJson $schema
    $original = $text | ConvertFrom-Json
    Check ($result.Accepted -and (Json $result.Graph) -ceq (Json $original)) $entry[0] $result.Reason
}
foreach ($recipe in (Load 'invalid-cases.json' | ConvertFrom-Json)) {
    $edited = Edit-Graph (Copy-Graph $base) $recipe
    $text = Json $edited
    $direct = Test-QualificationGraphSchema $text $schema
    $result = Read-QualificationGraph $text $catalogJson $schema
    $boundary = ($direct -eq ($recipe.boundary -ceq 'identity'))
    $path = $result.SchemaPaths -ccontains $recipe.path
    if ($recipe.boundary -ceq 'identity') { $path = $result.Path -ceq $recipe.path }
    Check ($healthy.Accepted -and $boundary -and -not $result.Accepted -and
        $result.Category -ceq $recipe.category -and $path) $recipe.id ("path=" + $recipe.path + " result=" + $result.Category + ':' + $result.Path)
    if ((Inventory $fixtures) -cne $beforeInputs) { throw 'fixture-bytes-changed' }
}
$missing = Read-QualificationGraph $graphJson '' $schema
Check ($healthy.Accepted -and -not $missing.Accepted -and $missing.Reason -ceq 'catalog-required' -and $missing.Path -ceq '/catalog') 'catalog-missing'

# Catalog membership must independently prove the graph's declared selection.
foreach ($fault in @('wrong-uid', 'missing-uid', 'extra-uid', 'wrong-count', 'wrong-digest')) {
    $g = Copy-Graph $base
    $cat = $catalogJson | ConvertFrom-Json
    switch ($fault) {
        'wrong-uid' { $cat.uids.'fixture-selection' = @('Fixture.A.0', 'Fixture.WRONG') }
        'missing-uid' { $cat.uids.'fixture-selection' = @('Fixture.A.0') }
        'extra-uid' { $cat.uids.'fixture-selection' = @('Fixture.A.0', 'Fixture.A.1', 'Fixture.A.2') }
        'wrong-count' { $cat.nodes[3].selection.expectedCount = 3; $g.nodes[3].selection.expectedCount = 3 }
        'wrong-digest' { $cat.nodes[3].selection.uidSetDigest = 'f' * 64; $g.nodes[3].selection.uidSetDigest = 'f' * 64 }
    }
    $r = Read-QualificationGraph (Json $g) (Json $cat) $schema
    Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'identity' -and
        $r.Path -ceq '/nodes/3/selection' -and $r.Reason -ceq 'catalog-uid-mismatch') ('catalog-uid-mismatch/' + $fault) ($r.Reason + ':' + $r.Path)
}
foreach ($phase in @('prepare', 'execute')) {
    $text = Load ($phase + '.valid.json')
    $r = Read-QualificationGraph $text $text $schema
    Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'identity' -and
        $r.Path -ceq '/catalog' -and $r.Reason -ceq 'catalog-invalid') ('catalog-is-graph/' + $phase) ($r.Reason + ':' + $r.Path)
}
$preciseGraph = $graphJson.Replace('"canonicalOrdinal": 0', '"canonicalOrdinal": 1.0000000000000000000001e22')
$preciseCatalog = $catalogJson.Replace('"canonicalOrdinal": 0', '"canonicalOrdinal": 1e22')
$r = Read-QualificationGraph $preciseGraph $preciseCatalog $schema
Check ($healthy.Accepted -and (Test-QualificationGraphSchema $preciseGraph $schema) -and
    -not $r.Accepted -and $r.Category -ceq 'identity' -and $r.Path -ceq '/nodes/0/canonicalOrdinal' -and
    $r.Reason -ceq 'catalog-mismatch') 'catalog-number-exact' ($r.Reason + ':' + $r.Path)

$duplicate = Copy-Graph $base
$duplicate.nodes = @($duplicate.nodes) + @($duplicate.nodes[0])
$r = Read-QualificationGraph (Json $duplicate) $catalogJson $schema
Check ($healthy.Accepted -and -not $r.Accepted -and $r.Reason -ceq 'duplicate-node' -and $r.Path -ceq '/nodes/5/id') 'duplicate-node'
$rawCases = @(
    @('duplicate-key/root', (Load 'duplicate-key.raw.json'), '/schemaVersion', 'duplicate-key'),
    @('duplicate-key/nested', $graphJson.Replace('"repositoryId": "fixture-repo"', '"repositoryId":"wrong","repositoryId":"fixture-repo"'), '/candidate/repositoryId', 'duplicate-key'),
    @('nonfinite/NaN', $graphJson.Replace('"seconds": 1.25', '"seconds": NaN'), '', 'invalid-json'),
    @('nonfinite/Infinity', $graphJson.Replace('"seconds": 1.25', '"seconds": Infinity'), '', 'invalid-json'),
    @('nonfinite/overflow', $graphJson.Replace('"seconds": 1.25', '"seconds": 1e9999'), '/nodes/0/estimate/seconds', 'nonfinite'))
foreach ($entry in $rawCases) {
    $r = Read-QualificationGraph $entry[1] $catalogJson $schema
    Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'raw' -and $r.Path -ceq $entry[2] -and $r.Reason -ceq $entry[3]) $entry[0] $r.Reason
}
$extra = Copy-Graph $base
$extra | Add-Member -NotePropertyName fixtureUnexpected -NotePropertyValue $true
$r = Read-QualificationGraph (Json $extra) $catalogJson $schema
Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'schema') 'schema-wired'
$golden = Load 'execute.canonical.json'
$expected = Load 'identity.expected.json' | ConvertFrom-Json
$keys = @($base.PSObject.Properties.Name)
[Array]::Reverse($keys)
$reordered = [ordered]@{}
foreach ($key in $keys) { $reordered[$key] = $base.$key }
$r = Read-QualificationGraph (Json $reordered) $catalogJson $schema
$oldCulture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')
    $primitive = ConvertTo-QualificationCanonicalJson '{"\u00e4":1.25,"a":"\u00e9","Z":"\n"}'
    $primitiveExpected = '{"Z":"\n","a":"' + [char]0xe9 + '","' + [char]0xe4 + '":1.25}'
    Check ($r.Accepted -and $r.CanonicalJson -ceq $golden -and $primitive -ceq $primitiveExpected) 'canonical-keys'
} finally { [Threading.Thread]::CurrentThread.CurrentCulture = $oldCulture }
$sets = Copy-Graph $base
[Array]::Reverse($sets.nodes)
[Array]::Reverse($sets.edges)
[Array]::Reverse($sets.targets)
[Array]::Reverse($sets.targets[0].resourceKeys)
$r = Read-QualificationGraph (Json $sets) $catalogJson $schema
Check ($healthy.Accepted -and $healthy.CanonicalJson -ceq $golden -and
    $r.Accepted -and $r.CanonicalJson -ceq $golden) 'canonical-sets' $r.Reason
$nullJson = ConvertTo-QualificationCanonicalJson 'null'
$empty = ConvertTo-QualificationCanonicalJson '""'
Check ($nullJson -ceq 'null' -and $empty -ceq '""' -and (Get-QualificationSha256 $nullJson) -cne (Get-QualificationSha256 $empty)) 'typed-null'
$number = ConvertTo-QualificationCanonicalJson '1.25'
$string = ConvertTo-QualificationCanonicalJson '"1.25"'
Check ($number -ceq '1.25' -and $string -ceq '"1.25"' -and (Get-QualificationSha256 $number) -cne (Get-QualificationSha256 $string) -and
    (ConvertTo-QualificationCanonicalJson '1e-9999') -ceq '1e-9999' -and
    (ConvertTo-QualificationCanonicalJson '12345678901234567890123456789.1') -ceq '12345678901234567890123456789.1') 'typed-number'
Check ($healthy.CanonicalJson -ceq $golden -and $healthy.GraphDigest -ceq $expected.graphDigest -and
    $healthy.GraphDigest -ceq (Independent-Hash $golden) -and
    (Get-QualificationSha256 $primitive) -ceq (Independent-Hash $primitiveExpected)) 'utf8-hash'
foreach ($binding in @('binding', 'target')) {
    $g = Copy-Graph $base
    $g.targets[0].bindingDigest = 'f' * 64
    if ($binding -ceq 'target') {
        $g.targets[0].id = 'windows-B'
        foreach ($n in $g.nodes) { $n.targetId = 'windows-B' }
    }
    $cat = Load ('catalog-' + $binding + '-B.json')
    $r = Read-QualificationGraph (Json $g) $cat $schema
    $gold = Load ('execute-' + $binding + '-B.canonical.json')
    Check ($r.Accepted -and (Json $r.NodeIds) -ceq (Json $expected.nodeIds)) ($binding + '-node-stable')
    Check ($r.Accepted -and $r.CanonicalJson -ceq $gold -and $r.GraphDigest -ceq (Independent-Hash $gold) -and
        $r.GraphDigest -cne $healthy.GraphDigest) ($binding + '-graph-changed')
    Check ($r.Accepted -and $r.Graph.nodes[3].selection.uidSetDigest -ceq $expected.uidSetDigest) ($binding + '-uid-stable')
}
$changedUids = @('Fixture.A.0', 'Fixture.A.2')
$changedDigest = Independent-Hash '["Fixture.A.0","Fixture.A.2"]'
$g = Copy-Graph $base
$cat = $catalogJson | ConvertFrom-Json
$cat.uids.'fixture-selection' = $changedUids
$cat.nodes[3].selection.uidSetDigest = $changedDigest
$g.nodes[3].selection.uidSetDigest = $changedDigest
$r = Read-QualificationGraph (Json $g) (Json $cat) $schema
Check ($r.Accepted -and (Get-QualificationUidSetDigest $changedUids) -ceq $changedDigest -and $changedDigest -cne $expected.uidSetDigest -and
    (Get-QualificationUidSetDigest @('a', 'A')) -ceq (Independent-Hash '["A","a"]')) 'uid-members-changed'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
$preservation = Load 'preservation.json' | ConvertFrom-Json
$policy = (Read-NightlyExecutionPolicy -RepoRoot $repo).Object
$census = @(Get-NightlyScriptCensus $policy)
$newRows = @($census | Where-Object { $_.path -ceq 'scripts/test-qualification-graph.ps1' })
Check ($newRows.Count -eq 1 -and (Json $newRows[0]) -ceq (Json $preservation.allowedRow) -and
    @($census | Where-Object { $_.path -ceq 'scripts/lib/qualification-graph.ps1' }).Count -eq 0) 'script-census'
$prior = Copy-Graph $preservation.policy
$actual = Copy-Graph $policy
$actual.scriptCensus = @($actual.scriptCensus | Where-Object { $_.path -cne 'scripts/test-qualification-graph.ps1' })
$prior.PSObject.Properties.Remove('policyHash')
$actual.PSObject.Properties.Remove('policyHash')
Check ((Json $prior) -ceq (Json $actual) -and $policy.profiles.rc.requiredSuites.Count -eq 10 -and
    $policy.profiles.rc.requiredSuites -ccontains 'observability') 'full-policy'
$filesOk = @($preservation.files.PSObject.Properties).Count -gt 0
foreach ($entry in $preservation.files.PSObject.Properties) {
    $p = Join-Path $repo $entry.Name
    $filesOk = $filesOk -and (Test-Path -LiteralPath $p -PathType Leaf) -and
        (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $entry.Value
}
Check $filesOk 'gate-files'
$ascii = $true
foreach ($path in @("$PSScriptRoot/lib/qualification-graph.ps1", $PSCommandPath)) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $ascii = $ascii -and @($bytes | Where-Object { $_ -gt 127 }).Count -eq 0
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    $ascii = $ascii -and $errors.Count -eq 0
}
Check $ascii 'ascii'
Check ((Inventory $fixtures) -ceq $beforeInputs -and (Inventory $ResultsDirectory) -ceq $beforeScratch) 'no-side-effects'
Write-Host ("QGRAPH TIMING setup={0:F3}s body={1:F3}s services=not-used externalCalls=fixture-io; child=owned-by-ScriptHarness" -f $setupSeconds, ($clock.Elapsed.TotalSeconds - $setupSeconds))
Write-Host "C487: $script:C487Passed passed, $script:C487Failed failed, $script:C487Rows rows"
Write-Host "C487 HARNESS EXIT CODE: $([int]($script:C487Failed -gt 0))"
exit ([int]($script:C487Failed -gt 0))
