#requires -Version 7.4
param([ValidateSet('SchemaAndIdentity', 'FullCensusUnchanged')][string]$Case, [string]$ResultsDirectory, [string]$ContextPath)
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

if ($Case -ceq 'FullCensusUnchanged') {
    # Commission-only historical comparison. Unit/nightly never call this branch.
    # The commissioning controller owns the context and pins the seed explicitly.
    function Read-CommissionGit([string[]]$GitArguments) {
        $start = [Diagnostics.ProcessStartInfo]::new('git')
        $start.WorkingDirectory = $repo
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($arg in @('--no-optional-locks', '-c', 'core.fsmonitor=false', '-c', 'core.hooksPath=/dev/null') + $GitArguments) {
            [void]$start.ArgumentList.Add($arg)
        }
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        try {
            [void]$child.Start()
            $out = $child.StandardOutput.ReadToEndAsync()
            $err = $child.StandardError.ReadToEndAsync()
            if (-not $child.WaitForExit(30000)) { throw 'commission-git-timeout' }
            if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($out, $err), 5000)) { throw 'commission-git-drain' }
            if ($child.ExitCode -ne 0) { throw 'commission-git-refused' }
            return $out.Result
        } finally {
            if ($child.Id -and -not $child.HasExited) { $child.Kill($true); [void]$child.WaitForExit(5000) }
            $child.Dispose()
        }
    }
    try {
        if ([string]::IsNullOrWhiteSpace($ContextPath) -or -not [IO.File]::Exists($ContextPath)) { throw 'commission-context-required' }
        if ((Get-Item -LiteralPath $ContextPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'commission-context-link' }
        $context = [IO.File]::ReadAllText($ContextPath) | ConvertFrom-Json
        if ($context.schemaVersion -ne 1 -or $context.repositoryRoot -cne [IO.Path]::GetFullPath($repo)) { throw 'commission-context-invalid' }
        foreach ($key in @('baseSha', 'seedSha', 'candidateSha')) {
            if ($context.$key -cnotmatch '^[a-f0-9]{40}$') { throw 'commission-context-sha' }
        }
        if ((Read-CommissionGit @('rev-parse', 'HEAD')).Trim() -cne $context.candidateSha) { throw 'commission-candidate-mismatch' }
        [void](Read-CommissionGit @('merge-base', '--is-ancestor', $context.baseSha, $context.seedSha))
        [void](Read-CommissionGit @('merge-base', '--is-ancestor', $context.seedSha, $context.candidateSha))
        $relative = 'scripts/fixtures/qualification-graph/s1/preservation.json'
        $seedText = Read-CommissionGit @('show', ($context.seedSha + ':' + $relative))
        if ($context.preservationSha256 -cnotmatch '^[a-f0-9]{64}$' -or
            (Independent-Hash $seedText) -cne $context.preservationSha256 -or
            (Independent-Hash (Load 'preservation.json')) -cne $context.preservationSha256) { throw 'commission-seed-fixture-mismatch' }
        $preserved = $seedText | ConvertFrom-Json
        if ($preserved.baseSha -cne $context.baseSha) { throw 'commission-base-mismatch' }
        . "$PSScriptRoot/lib/nightly-common.ps1"
        . "$PSScriptRoot/lib/nightly-policy.ps1"
        $baseline = (Read-CommissionGit @('show', ($context.baseSha + ':tests/test-execution-policy.json'))) | ConvertFrom-Json
        if ((Get-NightlyPolicyHash $baseline) -cne $baseline.policyHash -or
            (ConvertTo-NightlyCanonicalJson $baseline) -cne (ConvertTo-NightlyCanonicalJson $preserved.policy)) { throw 'commission-baseline-policy-mismatch' }
        $candidate = (Read-NightlyExecutionPolicy -RepoRoot $repo).Object
        $rows = @($candidate.scriptCensus | Where-Object { $_.path -ceq $preserved.allowedRow.path })
        if ($rows.Count -ne 1 -or (ConvertTo-NightlyCanonicalJson $rows[0]) -cne
            (ConvertTo-NightlyCanonicalJson $preserved.allowedRow)) { throw 'commission-allowed-row-mismatch' }
        $candidate.scriptCensus = @($candidate.scriptCensus | Where-Object { $_.path -cne $preserved.allowedRow.path })
        $candidate.PSObject.Properties.Remove('policyHash')
        $baseline.PSObject.Properties.Remove('policyHash')
        if ((ConvertTo-NightlyCanonicalJson $candidate) -cne (ConvertTo-NightlyCanonicalJson $baseline)) { throw 'commission-policy-changed' }
        if (@($preserved.files.PSObject.Properties).Count -ne 10) { throw 'commission-gate-roster' }
        foreach ($file in $preserved.files.PSObject.Properties) {
            $seedBytes = Read-CommissionGit @('show', ($context.baseSha + ':' + $file.Name))
            if ((Independent-Hash $seedBytes) -cne $file.Value -or
                (Get-FileHash -LiteralPath (Join-Path $repo $file.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.Value) {
                throw ('commission-gate-changed:' + $file.Name)
            }
        }
        Write-Host ('QGRAPH FullCensusUnchanged preserved seed=' + $context.seedSha + ' base=' + $context.baseSha + ' candidate=' + $context.candidateSha)
        exit 0
    } catch {
        Write-Host ('QGRAPH FullCensusUnchanged refused: ' + $_.Exception.Message)
        exit 2
    }
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
function Read-PinnedGraph {
    param([string]$Json, [string]$CatalogJson, [string]$SchemaPath)
    # The test supplies the independent fixture/variant pin, as a controller must.
    return Read-QualificationGraph $Json $CatalogJson $SchemaPath -ExpectedCatalogHash (Independent-Hash $CatalogJson)
}
$catalogJson = Load 'catalog.json'
$pins = (Load 'identity.expected.json' | ConvertFrom-Json).catalogPins
$pinsOk = (Independent-Hash $catalogJson) -ceq (Load 'catalog.sha256').Trim()
foreach ($pin in $pins.PSObject.Properties) { $pinsOk = $pinsOk -and (Independent-Hash (Load $pin.Name)) -ceq $pin.Value }
Check $pinsOk 'catalog-pin'
$graphJson = Load 'execute.valid.json'
$base = $graphJson | ConvertFrom-Json
$healthy = Read-PinnedGraph $graphJson $catalogJson $schema
foreach ($entry in @(
    @('valid-prepare', 'prepare.valid.json'),
    @('valid-execute', 'execute.valid.json'),
    @('valid-sentinel', 'execute-preparation-failed.valid.json'))) {
    $text = Load $entry[1]
    $result = Read-PinnedGraph $text $catalogJson $schema
    $original = $text | ConvertFrom-Json
    Check ($result.Accepted -and (Json $result.Graph) -ceq (Json $original)) $entry[0] $result.Reason
}
foreach ($recipe in (Load 'invalid-cases.json' | ConvertFrom-Json)) {
    $edited = Edit-Graph (Copy-Graph $base) $recipe
    $text = Json $edited
    $direct = Test-QualificationGraphSchema $text $schema
    $result = Read-PinnedGraph $text $catalogJson $schema
    $boundary = ($direct -eq ($recipe.boundary -ceq 'identity'))
    $path = $result.SchemaPaths -ccontains $recipe.path
    if ($recipe.boundary -ceq 'identity') { $path = $result.Path -ceq $recipe.path }
    Check ($healthy.Accepted -and $boundary -and -not $result.Accepted -and
        $result.Category -ceq $recipe.category -and $path) $recipe.id ("path=" + $recipe.path + " result=" + $result.Category + ':' + $result.Path)
    if ((Inventory $fixtures) -cne $beforeInputs) { throw 'fixture-bytes-changed' }
}
$missing = Read-PinnedGraph $graphJson '' $schema
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
    $r = Read-PinnedGraph (Json $g) (Json $cat) $schema
    Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'identity' -and
        $r.Path -ceq '/nodes/3/selection' -and $r.Reason -ceq 'catalog-uid-mismatch') ('catalog-uid-mismatch/' + $fault) ($r.Reason + ':' + $r.Path)
}
foreach ($phase in @('prepare', 'execute')) {
    $text = Load ($phase + '.valid.json')
    $r = Read-PinnedGraph $text $text $schema
    Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'identity' -and
        $r.Path -ceq '/catalog' -and $r.Reason -ceq 'catalog-invalid') ('catalog-is-graph/' + $phase) ($r.Reason + ':' + $r.Path)
}
$preciseGraph = $graphJson.Replace('"canonicalOrdinal": 0', '"canonicalOrdinal": 1.0000000000000000000001e22')
$preciseCatalog = $catalogJson.Replace('"canonicalOrdinal": 0', '"canonicalOrdinal": 1e22')
$r = Read-PinnedGraph $preciseGraph $preciseCatalog $schema
Check ($healthy.Accepted -and (Test-QualificationGraphSchema $preciseGraph $schema) -and
    -not $r.Accepted -and $r.Category -ceq 'identity' -and $r.Path -ceq '/nodes/0/canonicalOrdinal' -and
    $r.Reason -ceq 'catalog-mismatch') 'catalog-number-exact' ($r.Reason + ':' + $r.Path)


$r = Read-QualificationGraph $graphJson $catalogJson $schema
Check (-not $r.Accepted -and $r.Reason -ceq 'catalog-pin-required') 'catalog-pin-missing'
$r = Read-QualificationGraph $graphJson $catalogJson $schema -ExpectedCatalogHash ('0' * 64)
Check (-not $r.Accepted -and $r.Reason -ceq 'catalog-pin-mismatch') 'catalog-pin-wrong'
foreach ($fault in @('missing-uids', 'unknown-field', 'wrong-node-shape', 'wrong-uid-type')) {
    $cat = $catalogJson | ConvertFrom-Json
    switch ($fault) {
        'missing-uids' { $cat.PSObject.Properties.Remove('uids') }
        'unknown-field' { $cat | Add-Member -NotePropertyName phase -NotePropertyValue 'prepare' }
        'wrong-node-shape' { $cat.nodes[0].PSObject.Properties.Remove('platformOfRecord') }
        'wrong-uid-type' { $cat.uids.'fixture-selection' = @(42) }
    }
    $r = Read-PinnedGraph $graphJson (Json $cat) $schema
    Check (-not $r.Accepted -and $r.Path -ceq '/catalog' -and $r.Reason -ceq 'catalog-invalid') ('catalog-shape/' + $fault)
}

$duplicate = Copy-Graph $base
$duplicate.nodes = @($duplicate.nodes) + @($duplicate.nodes[0])
$r = Read-PinnedGraph (Json $duplicate) $catalogJson $schema
Check ($healthy.Accepted -and -not $r.Accepted -and $r.Reason -ceq 'duplicate-node' -and $r.Path -ceq '/nodes/5/id') 'duplicate-node'
$rawCases = @(
    @('duplicate-key/root', (Load 'duplicate-key.raw.json'), '/schemaVersion', 'duplicate-key'),
    @('duplicate-key/nested', $graphJson.Replace('"repositoryId": "fixture-repo"', '"repositoryId":"wrong","repositoryId":"fixture-repo"'), '/candidate/repositoryId', 'duplicate-key'),
    @('nonfinite/NaN', $graphJson.Replace('"seconds": 1.25', '"seconds": NaN'), '', 'invalid-json'),
    @('nonfinite/Infinity', $graphJson.Replace('"seconds": 1.25', '"seconds": Infinity'), '', 'invalid-json'),
    @('nonfinite/overflow', $graphJson.Replace('"seconds": 1.25', '"seconds": 1e9999'), '/nodes/0/estimate/seconds', 'nonfinite'))
foreach ($entry in $rawCases) {
    $r = Read-PinnedGraph $entry[1] $catalogJson $schema
    Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'raw' -and $r.Path -ceq $entry[2] -and $r.Reason -ceq $entry[3]) $entry[0] $r.Reason
}
$extra = Copy-Graph $base
$extra | Add-Member -NotePropertyName fixtureUnexpected -NotePropertyValue $true
$r = Read-PinnedGraph (Json $extra) $catalogJson $schema
Check ($healthy.Accepted -and -not $r.Accepted -and $r.Category -ceq 'schema') 'schema-wired'
$golden = Load 'execute.canonical.json'
$expected = Load 'identity.expected.json' | ConvertFrom-Json
$keys = @($base.PSObject.Properties.Name)
[Array]::Reverse($keys)
$reordered = [ordered]@{}
foreach ($key in $keys) { $reordered[$key] = $base.$key }
$r = Read-PinnedGraph (Json $reordered) $catalogJson $schema
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
$r = Read-PinnedGraph (Json $sets) $catalogJson $schema
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
    $r = Read-PinnedGraph (Json $g) $cat $schema
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
$r = Read-PinnedGraph (Json $g) (Json $cat) $schema
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
