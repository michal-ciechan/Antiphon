# CARD-1545 S1a live-lane manifest, discovery adapter and census.
# The gate scan is compiled (W2-nightly S23): scripts/lib/Antiphon.Nightly/LiveLaneGateScan.cs.
# The discovery, the core rosters, the producer exports and receipts, the owned scratch, the manifest bindings and the
# census fixture writer are compiled (W2-nightly S24): LiveLaneDiscovery.cs, LiveLaneRoster.cs, LiveLaneExport.cs,
# LiveLaneScratch.cs and LiveLaneManifestFiles.cs in scripts/lib/Antiphon.Nightly. What is left here is the census
# (S25) and the thin wrappers it calls.
# ASCII-only. Diagnostic inventory only: list-tests of the owned assembly.
# No provider call, no live Claude, no policy edit.

if ($script:AntiphonLiveLaneManifestLoaded) { return }
$script:AntiphonLiveLaneManifestLoaded = $true

$script:LiveLanePreS1BaseSha = 'bf0b874e695a642118e2dd55913228e2f635d009'
$script:LiveLaneFixtureRel = 'tests/fixtures/live-lanes/full-discovery-before.json'
$script:LiveLanePreS1PinRel = 'tests/fixtures/live-lanes/pre-s1-base.sha'
# CARD-1645: the landing target the pre-S1 base must be on (fetched by the S1a group).
$script:LiveLaneLandingTargetRef = 'refs/remotes/origin/master'
$script:LiveLaneAllowedSeedPaths = @(
    'tests/live-lanes.json',
    'scripts/lib/live-lane-manifest.ps1',
    'scripts/test-live-lane-manifest.ps1',
    'tests/Antiphon.Tests/Scripts/LiveLaneInventoryTests.cs',
    'tests/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests.cs',
    'tests/fixtures/live-lanes/full-discovery-before.json'
)
# The pin is outside the seed allow-list. A seed commit that touches it is seed-engine
# even if a later edit adds the path to the allow-list above.
$script:LiveLaneImmutableSeedPaths = @(
    'tests/fixtures/live-lanes/pre-s1-base.sha'
)
$script:LiveLaneProtectedPaths = @(
    'tests/test-execution-policy.json',
    'scripts/lib/nightly-common.ps1',
    'scripts/lib/Antiphon.Nightly/NightlyPolicy.cs',
    'scripts/lib/Antiphon.Nightly/ExecutionPolicy.cs',
    'scripts/lib/Antiphon.Nightly/DiscoveryIndex.cs',
    'scripts/lib/Antiphon.Nightly/DiagnosticLog.cs',
    'scripts/lib/Antiphon.Nightly/DiagScan.cs',
    'scripts/lib/nightly-coverage.ps1'
)

# Dot-source in this script scope. Only the compiled core is needed: the discovery runs inside it
# (NightlyDiscoveryProducer on the live-lane transport), so nightly-common, nightly-coverage and
# nightly-tests-impl are no longer loaded here.
. (Join-Path $PSScriptRoot 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
$script:LiveLaneNightlyReady = $true
# The owned scratch ledger of this session (Clear-LiveLaneOwnedScratch empties it).
$script:LiveLaneScratch = [Antiphon.Nightly.LiveLaneScratch]::new()

function ConvertTo-LiveLaneList {
    param($Value)
    if ($null -eq $Value) { return @() }
    return @($Value)
}

function Get-LiveLaneProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in @($Object.Keys)) {
            if ([string]::Equals([string]$key, $Name, [StringComparison]::OrdinalIgnoreCase)) { return $Object[$key] }
        }
    }
    foreach ($p in @($Object.PSObject.Properties)) {
        if ([string]::Equals([string]$p.Name, $Name, [StringComparison]::OrdinalIgnoreCase)) { return $p.Value }
    }
    return $null
}

function Get-LiveLanePinnedPreS1 {
    param([string]$RepoRoot)
    $path = Join-Path $RepoRoot $script:LiveLanePreS1PinRel
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $text = ([System.IO.File]::ReadAllText($path)).Trim().ToLowerInvariant()
    if ($text -notmatch '^[0-9a-f]{40}$') { return '' }
    return $text
}

function Get-LiveLaneDeclaredPreS1 {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $match = [regex]::Match($Text, "LiveLanePreS1BaseSha\s*=\s*'([0-9a-fA-F]{40})'")
    if (-not $match.Success) { return '' }
    return $match.Groups[1].Value.ToLowerInvariant()
}

function Test-LiveLaneSeedDeclaresBase {
    param([string]$RepoRoot, [string]$Pre, [string]$Seed, [string]$Head)
    $key = $Pre + '|' + $Seed + '|' + $Head + '|' + $script:LiveLanePreS1BaseSha
    if ($null -eq $script:LiveLaneSeedDeclareCache) { $script:LiveLaneSeedDeclareCache = @{} }
    if ($script:LiveLaneSeedDeclareCache.ContainsKey($key)) { return @($script:LiveLaneSeedDeclareCache[$key]) }
    $found = New-Object System.Collections.Generic.List[string]
    $listed = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-list', ($Pre + '..' + $Seed))
    if ($listed.Exit -eq 0) {
        foreach ($commit in @(($listed.Text -split "`n"))) {
            $commit = $commit.Trim().ToLowerInvariant()
            if ($commit -notmatch '^[0-9a-f]{40}$') { continue }
            $names = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('diff-tree', '--no-commit-id', '--name-only', '-r', $commit)
            foreach ($name in @(($names.Text -split "`n"))) {
                if ($script:LiveLaneImmutableSeedPaths -contains $name.Trim()) { $found.Add('seed-engine') }
            }
            $shown = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('show', ($commit + ':scripts/lib/live-lane-manifest.ps1'))
            $bound = ''
            if ($shown.Exit -eq 0) { $bound = Get-LiveLaneDeclaredPreS1 $shown.Text }
            $engineTouched = $false
            foreach ($name in @(($names.Text -split "`n"))) {
                $trimmed = $name.Trim()
                if ($trimmed -eq 'scripts/lib/live-lane-manifest.ps1' -or $trimmed -eq 'scripts/test-live-lane-manifest.ps1') { $engineTouched = $true }
            }
            if ($engineTouched -and -not [string]::IsNullOrWhiteSpace($bound) -and $bound -ne $script:LiveLanePreS1BaseSha) { $found.Add('seed-engine') }
            if ($bound -eq $commit -or $bound -eq $Seed -or $bound -eq $Head) { $found.Add('self-seed') }
        }
    }
    $unique = @($found | Select-Object -Unique)
    $script:LiveLaneSeedDeclareCache[$key] = $unique
    return $unique
}

function Get-LiveLaneRepoRoot {
    param([string]$Start)
    if ([string]::IsNullOrWhiteSpace($Start)) { $Start = (Get-Location).Path }
    $dir = Get-Item -LiteralPath $Start -ErrorAction SilentlyContinue
    if ($dir -and -not $dir.PSIsContainer) { $dir = $dir.Directory }
    while ($null -ne $dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Antiphon.sln')) { return $dir.FullName }
        $dir = $dir.Parent
    }
    throw 'repo-root-missing'
}

function Get-LiveLaneContract {
    # The six identities live in the compiled core (LiveLaneContract, LiveLaneGateScan.cs), where the gate
    # dispositions read them; the same rows as before, as custom objects.
    $rows = @()
    foreach ($row in [Antiphon.Nightly.LiveLaneContract]::Core) {
        $rows += [pscustomobject]@{ project = $row.Project; namespace = $row.Namespace; class = $row.Class; method = $row.Method; arguments = @($row.Arguments) }
    }
    return $rows
}

function Get-LiveLaneArgumentKey {
    param($Arguments)
    $items = @(ConvertTo-LiveLaneList $Arguments | ForEach-Object { [string]$_ })
    return ($items -join "`n")
}

function Get-LiveLaneIdentityKey {
    param($Row)
    $project = [string](Get-LiveLaneProp $Row 'project')
    $ns = [string](Get-LiveLaneProp $Row 'namespace')
    $class = [string](Get-LiveLaneProp $Row 'class')
    if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $Row 'type') }
    $method = [string](Get-LiveLaneProp $Row 'method')
    $argumentKey = Get-LiveLaneArgumentKey (Get-LiveLaneProp $Row 'arguments')
    return ('{0}|{1}|{2}|{3}|{4}' -f $project, $ns, $class, $method, $argumentKey)
}

function Read-LiveLaneManifestObject {
    param([string]$RepoRoot)
    $path = Join-Path $RepoRoot 'tests/live-lanes.json'
    if (-not (Test-Path -LiteralPath $path)) { throw 'manifest-missing' }
    return ([System.IO.File]::ReadAllText($path) | ConvertFrom-Json)
}

function Read-LiveLaneContextObject {
    $raw = [string]$env:ANTIPHON_LIVE_PORTABLE_CONTEXT
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $text = $raw
    if ($raw.TrimStart().StartsWith('{') -eq $false) {
        if (-not (Test-Path -LiteralPath $raw)) { return $null }
        $text = [System.IO.File]::ReadAllText($raw)
    }
    return ($text | ConvertFrom-Json)
}

function Initialize-LiveLaneNightly {
    param([string]$RepoRoot)
    # The census reads the policy through the compiled core (NightlyPolicy, ExecutionPolicy, NightlyCensus).
    if (-not $script:NightlyCoreAvailable) { throw 'nightly-readers-not-loaded' }
    $script:LiveLaneNightlyReady = $true
}

function Get-LiveLaneCategorySet {
    param($Values)
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($c in @(ConvertTo-LiveLaneList $Values)) {
        $text = [string]$c
        if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$set.Add($text) }
    }
    return $set
}

# Compiled (LiveLaneExports.Check): the census passes the export it read, possibly edited, as an object.
function Test-LiveLaneExportDocument {
    param($Export, [string]$ExpectedSource, [string]$ExpectedRun, [string]$ExpectedHash, [string]$ExpectedProject, [bool]$RequireCore, [string]$AssemblyPath = '')
    try {
        $verdict = [Antiphon.Nightly.LiveLaneExports]::Check([Antiphon.Nightly.LiveLaneExport]::FromObject($Export), $ExpectedSource, $ExpectedRun, $ExpectedHash,
            $ExpectedProject, $RequireCore, $AssemblyPath)
    } catch { throw (Get-NightlyRefusalText $_) }
    return @{ Accepted = [bool]$verdict.Accepted; Reason = [string]$verdict.Reason }
}

function Read-LiveLaneExportFile {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return @{ Found = $false; Export = $null } }
    $item = Get-Item -LiteralPath $Path
    $key = $item.FullName + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks
    if ($null -eq $script:LiveLaneExportCache) { $script:LiveLaneExportCache = @{} }
    if ($script:LiveLaneExportCache.ContainsKey($key)) { return $script:LiveLaneExportCache[$key] }
    $loaded = @{ Found = $true; Export = ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json) }
    $script:LiveLaneExportCache[$key] = $loaded
    return $loaded
}

function Copy-LiveLaneExportShell {
    param($Export)
    $terminal = Get-LiveLaneProp $Export 'terminal'
    $flag = $false
    if ($null -ne $terminal) { $flag = [bool]$terminal }
    return [pscustomobject]@{
        format = [string](Get-LiveLaneProp $Export 'format')
        project = [string](Get-LiveLaneProp $Export 'project')
        cp = [string](Get-LiveLaneProp $Export 'cp')
        sourceSha = [string](Get-LiveLaneProp $Export 'sourceSha')
        runId = [string](Get-LiveLaneProp $Export 'runId')
        assemblyHash = [string](Get-LiveLaneProp $Export 'assemblyHash')
        assemblyPath = [string](Get-LiveLaneProp $Export 'assemblyPath')
        terminal = $flag
        nodes = (Get-LiveLaneProp $Export 'nodes')
    }
}

# The owned scratch is compiled (LiveLaneScratch): one ledger per session, $script:LiveLaneScratch.
function New-LiveLaneOwnedScratch {
    param([string]$RepoRoot, [string]$Leaf)
    try { return $script:LiveLaneScratch.NewOwned($RepoRoot, $Leaf) } catch { throw (Get-NightlyRefusalText $_) }
}

function Remove-LiveLaneOwnedScratch {
    param($Entry)
    if ($null -eq $Entry) { return }
    try { $script:LiveLaneScratch.Remove([string]$Entry.Path, [string]$Entry.Kind, [string]$Entry.Sha) } catch { throw (Get-NightlyRefusalText $_) }
}

function Clear-LiveLaneOwnedScratch {
    try { $script:LiveLaneScratch.Clear() } catch { throw (Get-NightlyRefusalText $_) }
    $script:LiveLaneCensusSeed = @{}
}

function Get-LiveLaneTempResidue {
    return @([Antiphon.Nightly.LiveLaneScratch]::TempResidue())
}

# The producer receipt is compiled (LiveLaneReceipt): the context's source and checkpointResults, or the mutant's root.
function Test-LiveLaneProducerReceipt {
    param($Context, $Mutant)
    $expected = [string](Get-LiveLaneProp $Context 'sourceSha')
    $root = [string](Get-LiveLaneProp $Context 'checkpointResults')
    if ($Mutant -and $Mutant.ContainsKey('receiptRoot')) { $root = [string]$Mutant.receiptRoot }
    try { $verdict = [Antiphon.Nightly.LiveLaneReceipt]::Check($expected, $root) } catch { throw (Get-NightlyRefusalText $_) }
    return @{ Accepted = [bool]$verdict.Accepted; Reason = [string]$verdict.Reason }
}

function Write-LiveLaneReceiptFixture {
    param(
        [string]$Root,
        [string]$Commit,
        [string]$RowState,
        [string]$BuildSource = 'verified',
        [string]$LineCommit = '',
        [switch]$OmitReport
    )
    try { [Antiphon.Nightly.LiveLaneReceipt]::WriteFixture($Root, $Commit, $RowState, $BuildSource, $LineCommit, [bool]$OmitReport) } catch { throw (Get-NightlyRefusalText $_) }
}

function Get-LiveLaneGit {
    param([string]$RepoRoot, [string[]]$GitArgs)
    $out = & git -C $RepoRoot @GitArgs 2>$null
    return @{ Exit = $LASTEXITCODE; Text = (($out | Out-String).Trim()) }
}

function Copy-LiveLaneGitBlob {
    param([string]$RepoRoot, [string]$Spec, [string]$Dest)
    # Compiled (LiveLaneScratch.CopyGitBlob): git cat-file blob into Dest; seed-object-missing when it fails.
    try { [Antiphon.Nightly.LiveLaneScratch]::CopyGitBlob($RepoRoot, $Spec, $Dest) } catch { throw (Get-NightlyRefusalText $_) }
}

function Get-LiveLanePolicyHashProcess {
    param([string]$RepoRoot, [string]$PolicyRoot, [string]$PolicyPath)
    $script = Join-Path $RepoRoot 'scripts/test-live-lane-manifest.ps1'
    $pwshArgs = @('-NoProfile', '-File', $script, '-Case', 'EmitPolicyHash', '-PolicyRoot', $PolicyRoot)
    if (-not [string]::IsNullOrWhiteSpace($PolicyPath)) { $pwshArgs += @('-PolicyPath', $PolicyPath) }
    $out = & pwsh @pwshArgs
    if ($LASTEXITCODE -ne 0) { throw 'policy-hash-process' }
    return ([string]@($out)[-1]).Trim()
}

function Get-LiveLaneNodeUid {
    param($Node)
    return [string](Get-LiveLaneProp $Node 'uid')
}

function Test-LiveLaneContractIdentity {
    param($Node)
    $identity = [pscustomobject]@{
        project = [string](Get-LiveLaneProp $Node 'project')
        namespace = [string](Get-LiveLaneProp $Node 'namespace')
        class = [string](Get-LiveLaneProp $Node 'type')
        method = [string](Get-LiveLaneProp $Node 'method')
        arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $Node 'arguments'))
    }
    if ([string]::IsNullOrWhiteSpace($identity.class)) { $identity.class = [string](Get-LiveLaneProp $Node 'class') }
    foreach ($want in @(Get-LiveLaneContract)) {
        if ((Get-LiveLaneIdentityKey $want) -eq (Get-LiveLaneIdentityKey $identity)) { return $true }
    }
    return $false
}

function Get-LiveLaneCategoryDelta {
    param($Before, $After)
    $b = Get-LiveLaneCategorySet $Before
    $a = Get-LiveLaneCategorySet $After
    $added = @()
    $removed = @()
    foreach ($item in $a) { if (-not $b.Contains($item)) { $added += $item } }
    foreach ($item in $b) { if (-not $a.Contains($item)) { $removed += $item } }
    return @{ Added = @($added); Removed = @($removed) }
}

function Initialize-LiveLaneCensusEngine {
    if ($script:LiveLaneCensusEngineReady) { return }
    $code = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

public static class LiveLaneCensusEngine
{
    sealed class Node
    {
        public string Project;
        public string Uid;
        public string Ns;
        public string Type;
        public string Method;
        public string ArgKey;
        public string[] Cats;
    }

    sealed class ExRow
    {
        public string Class;
        public string Reason;
        public string Owner;
        public string[] Methods;
    }

    sealed class Id
    {
        public string Project;
        public string Ns;
        public string Class;
        public string Method;
        public string ArgKey;
    }

    static readonly Dictionary<string, Node[]> Files = new Dictionary<string, Node[]>(StringComparer.Ordinal);
    static readonly Dictionary<string, string> Results = new Dictionary<string, string>(StringComparer.Ordinal);

    public static string Compare(string beforeFile, string after1, string after2, string beforeEx, string afterEx, string additive, string contract, string patch)
    {
        string key = (beforeFile ?? "") + "\n" + (after1 ?? "") + "\n" + (after2 ?? "") + "\n"
            + Stamp(beforeFile) + "\n" + Stamp(after1) + "\n" + Stamp(after2) + "\n"
            + (beforeEx ?? "") + "\n" + (afterEx ?? "") + "\n" + (additive ?? "") + "\n" + (contract ?? "") + "\n" + (patch ?? "");
        string cached;
        if (Results.TryGetValue(key, out cached)) return cached;
        Node[] before = Load(beforeFile);
        Node[] after = Concat(Load(after1), Load(after2));
        after = Apply(after, patch ?? "", ParseIds(contract));
        var diag = Run(before, after, ParseEx(beforeEx), ParseEx(afterEx), ParseIds(additive), ParseIds(contract));
        string text = string.Join("\n", diag);
        Results[key] = text;
        return text;
    }

    static string Stamp(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "missing";
        var info = new FileInfo(path);
        return info.Length.ToString() + ":" + info.LastWriteTimeUtc.Ticks.ToString();
    }

    static Node[] Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Node[0];
        string stamp = path + "\n" + Stamp(path);
        Node[] hit;
        if (Files.TryGetValue(stamp, out hit)) return hit;
        var list = new List<Node>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            JsonElement nodes;
            if (!doc.RootElement.TryGetProperty("nodes", out nodes) || nodes.ValueKind != JsonValueKind.Array) 
            {
                Files[stamp] = new Node[0];
                return Files[stamp];
            }
            foreach (var el in nodes.EnumerateArray()) list.Add(ReadNode(el));
        }
        var arr = list.ToArray();
        Files[stamp] = arr;
        return arr;
    }

    static Node ReadNode(JsonElement el)
    {
        var n = new Node();
        n.Project = Str(el, "project");
        n.Uid = Str(el, "uid");
        n.Ns = Str(el, "namespace");
        n.Type = Str(el, "type");
        if (string.IsNullOrEmpty(n.Type)) n.Type = Str(el, "class");
        n.Method = Str(el, "method");
        n.ArgKey = ArgKey(el);
        n.Cats = CatList(el);
        return n;
    }

    static string Str(JsonElement el, string name)
    {
        JsonElement value;
        if (!el.TryGetProperty(name, out value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        return value.ToString();
    }

    static string ArgKey(JsonElement el)
    {
        JsonElement value;
        if (!el.TryGetProperty("arguments", out value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind != JsonValueKind.Array) return "";
        var parts = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            string text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
            parts.Add(text);
        }
        return string.Join("\n", parts);
    }

    static string[] CatList(JsonElement el)
    {
        JsonElement value;
        if (!el.TryGetProperty("categories", out value)) return new string[0];
        if (value.ValueKind == JsonValueKind.String) return new string[] { value.GetString() ?? "" };
        if (value.ValueKind != JsonValueKind.Array) return new string[0];
        var parts = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            string text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
            if (text.Length > 0) parts.Add(text);
        }
        return parts.ToArray();
    }

    static Node[] Concat(Node[] left, Node[] right)
    {
        var all = new Node[left.Length + right.Length];
        Array.Copy(left, 0, all, 0, left.Length);
        Array.Copy(right, 0, all, left.Length, right.Length);
        return all;
    }

    static Id[] ParseIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Id[0];
        var list = new List<Id>();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new Id[0];
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = new Id();
                id.Project = Str(el, "project");
                id.Ns = Str(el, "namespace");
                id.Class = Str(el, "class");
                if (string.IsNullOrEmpty(id.Class)) id.Class = Str(el, "type");
                id.Method = Str(el, "method");
                id.ArgKey = ArgKey(el);
                list.Add(id);
            }
        }
        return list.ToArray();
    }

    static ExRow[] ParseEx(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ExRow[0];
        var list = new List<ExRow>();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new ExRow[0];
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var row = new ExRow();
                row.Class = Str(el, "class");
                row.Reason = Str(el, "reason");
                row.Owner = Str(el, "owner");
                JsonElement methods;
                if (el.TryGetProperty("methods", out methods) && methods.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in methods.EnumerateArray())
                    {
                        string text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
                        if (text.Length > 0) parts.Add(text);
                    }
                    row.Methods = parts.ToArray();
                }
                else row.Methods = new string[0];
                list.Add(row);
            }
        }
        return list.ToArray();
    }

    static Node Clone(Node n)
    {
        var c = new Node();
        c.Project = n.Project;
        c.Uid = n.Uid;
        c.Ns = n.Ns;
        c.Type = n.Type;
        c.Method = n.Method;
        c.ArgKey = n.ArgKey;
        c.Cats = (string[])n.Cats.Clone();
        return c;
    }

    static bool HasCat(string[] cats, string name)
    {
        for (int i = 0; i < cats.Length; i++)
        {
            if (string.Equals(cats[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static string[] WithoutCat(string[] cats, string name)
    {
        var list = new List<string>();
        for (int i = 0; i < cats.Length; i++)
        {
            if (!string.Equals(cats[i], name, StringComparison.OrdinalIgnoreCase)) list.Add(cats[i]);
        }
        return list.ToArray();
    }

    static string[] WithCat(string[] cats, string name)
    {
        var list = new List<string>();
        list.AddRange(cats);
        list.Add(name);
        return list.ToArray();
    }

    static bool IsContract(Node n, Id[] contract)
    {
        for (int i = 0; i < contract.Length; i++)
        {
            var id = contract[i];
            if (!string.Equals(id.Project, n.Project, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.Ns, n.Ns, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.Class, n.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.Method, n.Method, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.ArgKey, n.ArgKey, StringComparison.OrdinalIgnoreCase)) continue;
            return true;
        }
        return false;
    }

    static Node[] Apply(Node[] after, string patch, Id[] contract)
    {
        if (string.IsNullOrEmpty(patch)) return after;
        if (patch == "drop-first")
        {
            if (after.Length == 0) return after;
            var dropped = new Node[after.Length - 1];
            Array.Copy(after, 1, dropped, 0, dropped.Length);
            return dropped;
        }
        var copy = (Node[])after.Clone();
        if (patch == "remove-integration")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (!HasCat(copy[i].Cats, "Integration")) continue;
                var n = Clone(copy[i]);
                n.Cats = WithoutCat(n.Cats, "Integration");
                copy[i] = n;
                break;
            }
            return copy;
        }
        if (patch == "tag-seventh")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (IsContract(copy[i], contract)) continue;
                if (HasCat(copy[i].Cats, "LiveClaude")) continue;
                var n = Clone(copy[i]);
                n.Cats = WithCat(n.Cats, "LiveClaude");
                copy[i] = n;
                break;
            }
            return copy;
        }
        if (patch == "add-optin")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (HasCat(copy[i].Cats, "OptIn") || HasCat(copy[i].Cats, "Explicit")) continue;
                if (IsContract(copy[i], contract)) continue;
                var n = Clone(copy[i]);
                n.Cats = WithCat(n.Cats, "OptIn");
                copy[i] = n;
                break;
            }
            return copy;
        }
        if (patch == "extra-expansion")
        {
            if (copy.Length == 0) return copy;
            var n = Clone(copy[0]);
            n.Uid = (n.Uid ?? "") + "-extra";
            n.ArgKey = "extra-arg";
            var grown = new Node[copy.Length + 1];
            Array.Copy(copy, grown, copy.Length);
            grown[copy.Length] = n;
            return grown;
        }
        if (patch == "unknown-node")
        {
            var n = new Node();
            n.Project = "Antiphon.Tests";
            n.Uid = "undeclared-live-lane-uid";
            n.Ns = "Antiphon.Tests.Scripts";
            n.Type = "NotInContract";
            n.Method = "UnknownMethod";
            n.ArgKey = "";
            n.Cats = new string[] { "Unit" };
            var grown = new Node[copy.Length + 1];
            Array.Copy(copy, grown, copy.Length);
            grown[copy.Length] = n;
            return grown;
        }
        if (patch == "drop-live")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (!IsContract(copy[i], contract)) continue;
                if (!HasCat(copy[i].Cats, "LiveClaude")) continue;
                var n = Clone(copy[i]);
                n.Cats = WithoutCat(n.Cats, "LiveClaude");
                copy[i] = n;
                break;
            }
            return copy;
        }
        return copy;
    }

    static List<string> Run(Node[] before, Node[] after, ExRow[] beforeEx, ExRow[] afterEx, Id[] additive, Id[] contract)
    {
        var diag = new List<string>();
        int liveAdds = 0;
        var beforeMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in before) beforeMap[n.Uid ?? ""] = n;
        var afterMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in after) afterMap[n.Uid ?? ""] = n;
        foreach (var pair in beforeMap)
        {
            Node other;
            if (!afterMap.TryGetValue(pair.Key, out other))
            {
                Add(diag, "old-membership-changed");
                continue;
            }
            bool removed;
            bool addedOther;
            bool addedLive;
            CategoryDelta(pair.Value.Cats, other.Cats, out removed, out addedOther, out addedLive);
            if (addedLive && !removed && !addedOther) liveAdds++;
            if (removed || addedOther) Add(diag, "category-delta");
            else if (addedLive && !IsContract(pair.Value, contract)) Add(diag, "category-delta");
            if (!string.Equals(StableArg(pair.Value.ArgKey), StableArg(other.ArgKey), StringComparison.OrdinalIgnoreCase)) Add(diag, "extra-expansion");
        }
        foreach (var pair in afterMap)
        {
            if (beforeMap.ContainsKey(pair.Key)) continue;
            var n = pair.Value;
            bool declared = false;
            for (int i = 0; i < additive.Length; i++)
            {
                if (SameId(additive[i], n))
                {
                    declared = true;
                    break;
                }
            }
            if (!declared) Add(diag, "undeclared-new-uid");
            else if (HasCat(n.Cats, "LiveClaude")) Add(diag, "category-delta");
        }
        if (ExpansionDiffers(before, after)) Add(diag, "extra-expansion");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in after)
        {
            for (int i = 0; i < additive.Length; i++)
            {
                if (SameId(additive[i], n))
                {
                    allowed.Add(n.Uid ?? "");
                    break;
                }
            }
        }
        // CARD-1645: a declared addition that is no longer in the after set is a removal.
        for (int i = 0; i < additive.Length; i++)
        {
            bool present = false;
            foreach (var n in after)
            {
                if (SameId(additive[i], n))
                {
                    present = true;
                    break;
                }
            }
            if (!present) Add(diag, "declared-additive-missing");
        }
        var beforeDefault = Disposition(before, null, false);
        var afterDefault = Disposition(after, null, false);
        var beforeRc = Disposition(before, beforeEx, true);
        var afterRc = Disposition(after, afterEx, true);
        if (!SetEqual(beforeDefault.Required, Filter(afterDefault.Required, allowed)) ||
            !SetEqual(beforeDefault.Excluded, Filter(afterDefault.Excluded, allowed)))
        {
            Add(diag, "nightly-disposition-changed");
        }
        if (!SetEqual(beforeRc.Required, Filter(afterRc.Required, allowed)) ||
            !SetEqual(beforeRc.Excluded, Filter(afterRc.Excluded, allowed)))
        {
            string c680 = "";
            foreach (var n in before)
            {
                if (string.Equals(n.Method, "C680_LiveNudgeRefusesPermissionAndQuestionDialogs", StringComparison.OrdinalIgnoreCase))
                {
                    c680 = n.Uid ?? "";
                    break;
                }
            }
            if (c680.Length > 0 && beforeRc.Required.Contains(c680) && afterRc.Excluded.Contains(c680)) Add(diag, "C680-required-to-excluded");
        }
        if (liveAdds != 6) Add(diag, "liveclaude-count");
        return diag;
    }

    // A declared identity is project, namespace, class and method; a twin elsewhere is not it.
    static bool SameId(Id id, Node n)
    {
        return string.Equals(id.Project, n.Project, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(id.Ns, n.Ns, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(id.Class, n.Type, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(id.Method, n.Method, StringComparison.OrdinalIgnoreCase);
    }

    static void CategoryDelta(string[] before, string[] after, out bool removed, out bool addedOther, out bool addedLive)
    {
        removed = false;
        addedOther = false;
        addedLive = false;
        var b = new HashSet<string>(before ?? new string[0], StringComparer.Ordinal);
        var a = new HashSet<string>(after ?? new string[0], StringComparer.Ordinal);
        foreach (var item in b) if (!a.Contains(item)) removed = true;
        foreach (var item in a)
        {
            if (b.Contains(item)) continue;
            if (string.Equals(item, "LiveClaude", StringComparison.Ordinal)) addedLive = true;
            else addedOther = true;
        }
    }

    static bool IsHex(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    static int HexRun(string value, int start)
    {
        int run = 0;
        while (start + run < value.Length && IsHex(value[start + run])) run++;
        return run;
    }

    // C672 discovery calls Guid.NewGuid for one hold sentence. The expansion slot is the UID.
    static string StableArg(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            int run = HexRun(value, i);
            if (run == 32)
            {
                sb.Append("{guid}");
                i += 32;
                continue;
            }
            if (run == 8 && i + 36 <= value.Length && value[i + 8] == '-' && HexRun(value, i + 9) == 4 && value[i + 13] == '-' && HexRun(value, i + 14) == 4 && value[i + 18] == '-' && HexRun(value, i + 19) == 4 && value[i + 23] == '-' && HexRun(value, i + 24) == 12)
            {
                sb.Append("{guid}");
                i += 36;
                continue;
            }
            if (run > 0)
            {
                sb.Append(value, i, run);
                i += run;
                continue;
            }
            sb.Append(value[i]);
            i++;
        }
        return sb.ToString();
    }

    static bool ExpansionDiffers(Node[] before, Node[] after)
    {
        var left = Expand(before);
        var right = Expand(after);
        foreach (var pair in right)
        {
            HashSet<string> other;
            if (!left.TryGetValue(pair.Key, out other)) continue;
            if (!pair.Value.SetEquals(other)) return true;
        }
        return false;
    }

    static Dictionary<string, HashSet<string>> Expand(Node[] nodes)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            string key = (n.Type ?? "") + "|" + (n.Method ?? "");
            HashSet<string> set;
            if (!map.TryGetValue(key, out set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = set;
            }
            set.Add(StableArg(n.ArgKey));
        }
        return map;
    }

    sealed class Sets
    {
        public HashSet<string> Required;
        public HashSet<string> Excluded;
    }

    static Sets Disposition(Node[] nodes, ExRow[] exclusions, bool profile)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<string, List<ExRow>>(StringComparer.OrdinalIgnoreCase);
        if (profile && exclusions != null)
        {
            foreach (var row in exclusions)
            {
                if (string.IsNullOrWhiteSpace(row.Class)) continue;
                List<ExRow> list;
                if (!index.TryGetValue(row.Class, out list))
                {
                    list = new List<ExRow>();
                    index[row.Class] = list;
                }
                list.Add(row);
            }
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            string uid = n.Uid ?? "";
            if (!seen.Add(uid)) continue;
            bool hit = profile ? ProfileHit(n, index) : HasCat(n.Cats, "OptIn") || HasCat(n.Cats, "Explicit");
            if (hit) excluded.Add(uid);
            else required.Add(uid);
        }
        return new Sets { Required = required, Excluded = excluded };
    }

    static bool ProfileHit(Node n, Dictionary<string, List<ExRow>> index)
    {
        string className = (n.Ns ?? "") + "." + (n.Type ?? "");
        int open = className.IndexOf('(');
        if (open > 0 && className.EndsWith(")", StringComparison.Ordinal)) className = className.Substring(0, open);
        int dot = className.LastIndexOf('.');
        string simple = (dot >= 0 && dot < className.Length - 1) ? className.Substring(dot + 1) : className;
        if (Hit(index, simple, n.Method)) return true;
        if (!string.Equals(simple, className, StringComparison.OrdinalIgnoreCase) && Hit(index, className, n.Method)) return true;
        return false;
    }

    static bool Hit(Dictionary<string, List<ExRow>> index, string key, string method)
    {
        List<ExRow> rows;
        if (!index.TryGetValue(key, out rows)) return false;
        foreach (var row in rows)
        {
            if (row.Methods == null || row.Methods.Length == 0) return true;
            for (int i = 0; i < row.Methods.Length; i++)
            {
                if (string.Equals(row.Methods[i], method, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    static HashSet<string> Filter(HashSet<string> set, HashSet<string> allowed)
    {
        var copy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in set)
        {
            if (!allowed.Contains(id)) copy.Add(id);
        }
        return copy;
    }

    static bool SetEqual(HashSet<string> left, HashSet<string> right) { return left.SetEquals(right); }

    static void Add(List<string> diag, string name)
    {
        if (!diag.Contains(name)) diag.Add(name);
    }
}
'@
    Add-Type -TypeDefinition $code -Language CSharp
    $script:LiveLaneCensusEngineReady = $true
}

function ConvertTo-LiveLaneJsonToken {
    param([string]$Text)
    if ($null -eq $Text) { $Text = '' }
    return '"' + ($Text.Replace('\', '\\').Replace('"', '\"')) + '"'
}

function ConvertTo-LiveLaneJsonStringArray {
    param($Values)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($value in @(ConvertTo-LiveLaneList $Values)) {
        $parts.Add((ConvertTo-LiveLaneJsonToken ([string]$value)))
    }
    return '[' + ($parts -join ',') + ']'
}

function ConvertTo-LiveLaneExclusionJson {
    param($Rows)
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        $methods = New-Object System.Collections.Generic.List[string]
        foreach ($m in @(ConvertTo-LiveLaneList $row.Methods)) {
            $text = [string]$m
            if (-not [string]::IsNullOrWhiteSpace($text)) { $methods.Add($text) }
        }
        $items.Add('{"class":' + (ConvertTo-LiveLaneJsonToken ([string]$row.Class)) + ',"methods":' + (ConvertTo-LiveLaneJsonStringArray $methods) + ',"reason":' + (ConvertTo-LiveLaneJsonToken ([string]$row.Reason)) + ',"owner":' + (ConvertTo-LiveLaneJsonToken ([string]$row.Owner)) + '}')
    }
    return '[' + ($items -join ',') + ']'
}

function ConvertTo-LiveLaneIdentityJson {
    param($Rows)
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        $class = [string](Get-LiveLaneProp $row 'class')
        if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $row 'type') }
        $args = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $row 'arguments') | ForEach-Object { [string]$_ })
        $items.Add('{"project":' + (ConvertTo-LiveLaneJsonToken ([string](Get-LiveLaneProp $row 'project'))) + ',"namespace":' + (ConvertTo-LiveLaneJsonToken ([string](Get-LiveLaneProp $row 'namespace'))) + ',"class":' + (ConvertTo-LiveLaneJsonToken $class) + ',"method":' + (ConvertTo-LiveLaneJsonToken ([string](Get-LiveLaneProp $row 'method'))) + ',"arguments":' + (ConvertTo-LiveLaneJsonStringArray $args) + '}')
    }
    return '[' + ($items -join ',') + ']'
}

function Get-LiveLaneCachedExclusions {
    param([string]$PolicyPath, [string]$Platform)
    if ([string]::IsNullOrWhiteSpace($PolicyPath) -or -not (Test-Path -LiteralPath $PolicyPath)) { throw 'policy-missing' }
    $item = Get-Item -LiteralPath $PolicyPath
    $key = $item.FullName + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks + '|' + $Platform
    if ($null -eq $script:LiveLaneExclusionCache) { $script:LiveLaneExclusionCache = @{} }
    if ($script:LiveLaneExclusionCache.ContainsKey($key)) { return @($script:LiveLaneExclusionCache[$key]) }
    $obj = [Antiphon.Nightly.ExecutionPolicy]::Parse([System.IO.File]::ReadAllText($PolicyPath), $PolicyPath)
    $rows = @(Get-LiveLaneExclusionsFromObject $obj $Platform)
    $script:LiveLaneExclusionCache[$key] = $rows
    return $rows
}

function Get-LiveLaneCensusIdentityPayload {
    param([string]$RepoRoot)
    $path = Join-Path $RepoRoot 'tests/live-lanes.json'
    $item = Get-Item -LiteralPath $path
    $key = $item.FullName + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks
    if ($script:LiveLaneIdentityPayloadKey -eq $key -and $null -ne $script:LiveLaneIdentityPayload) { return $script:LiveLaneIdentityPayload }
    $manifest = Read-LiveLaneManifestObject $RepoRoot
    $payload = @{
        Additive = (ConvertTo-LiveLaneIdentityJson (Get-LiveLaneProp $manifest 'additiveContract'))
        Contract = (ConvertTo-LiveLaneIdentityJson (Get-LiveLaneContract))
    }
    $script:LiveLaneIdentityPayloadKey = $key
    $script:LiveLaneIdentityPayload = $payload
    return $payload
}

function Get-LiveLaneCensusSeedMaterial {
    param([string]$RepoRoot, [string]$Seed, [string]$FixtureRel, [string]$Pre)
    $blob = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + ':' + $FixtureRel))).Text
    if ([string]::IsNullOrWhiteSpace($blob)) { return $null }
    $protectedKey = ''
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $protectedKey += '|' + (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + ':' + $rel))).Text
    }
    $key = $blob + '|' + $FixtureRel + '|' + $Pre + $protectedKey
    if ($null -eq $script:LiveLaneCensusSeed) { $script:LiveLaneCensusSeed = @{} }
    if ($script:LiveLaneCensusSeed.ContainsKey($key)) { return $script:LiveLaneCensusSeed[$key] }
    $before = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('seed' + $blob.Substring(0, 12) + [guid]::NewGuid().ToString('N'))
    $fixtureCopy = Join-Path $before $FixtureRel
    $ok = $true
    try { Copy-LiveLaneGitBlob -RepoRoot $RepoRoot -Spec ($Seed + ':' + $FixtureRel) -Dest $fixtureCopy } catch { $ok = $false }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        try { Copy-LiveLaneGitBlob -RepoRoot $RepoRoot -Spec ($Seed + ':' + $rel) -Dest (Join-Path $before $rel) } catch { $ok = $false }
    }
    if (-not $ok -or -not (Test-Path -LiteralPath $fixtureCopy)) { return $null }
    $fixture = [System.IO.File]::ReadAllText($fixtureCopy) | ConvertFrom-Json
    $parent = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + '^'))).Text
    $recorded = [string](Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'sourceSha')
    $names = @((Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('log', '--name-only', '--pretty=format:', ($Pre + '..' + $Seed))).Text -split "`n")
    $touched = @($names | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $equal = @{}
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $seedBlob = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + ':' + $rel))).Text
        $preBlob = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Pre + ':' + $rel))).Text
        $equal[$rel] = ($seedBlob -eq $preBlob)
    }
    $fileHash = @{}
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $fileHash[$rel] = (Get-FileHash -LiteralPath (Join-Path $before $rel) -Algorithm SHA256).Hash
    }
    Initialize-LiveLaneNightly -RepoRoot $RepoRoot
    $beforePolicy = [System.IO.File]::ReadAllText((Join-Path $before 'tests/test-execution-policy.json')) | ConvertFrom-Json
    $platform = $(if ($IsLinux) { 'linux' } else { 'windows' })
    $beforeEx = @(Get-LiveLaneExclusionsFromObject $beforePolicy $platform)
    $entry = @{
        Key = $key
        Blob = $blob
        Fixture = $fixture
        FixtureCopy = $fixtureCopy
        BeforeDir = $before
        Parent = $parent
        Recorded = $recorded
        Touched = @($touched)
        ProtectedEqual = $equal
        FileHash = $fileHash
        BeforeEx = $beforeEx
        BeforeExJson = (ConvertTo-LiveLaneExclusionJson $beforeEx)
    }
    $script:LiveLaneCensusSeed[$key] = $entry
    return $entry
}

# CARD-1645 D-1/D-4. CP-30 proves no drift against the landing target, not a caller-chosen
# seed: the pre-S1 base must be on the fetched target. It is commission-scoped: a candidate
# that has merged target commits from after the seed is a later tree, so their new UIDs are
# not this commission's delta. Re-seed at the merge-base instead of re-running.
function Get-LiveLaneTargetDiagnostics {
    param([string]$RepoRoot, [string]$Pre, [string]$Seed, [string]$Head, [string]$TargetRef)
    $found = New-Object System.Collections.Generic.List[string]
    $resolved = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', '--verify', '--quiet', ($TargetRef + '^{commit}'))
    $target = ([string]$resolved.Text).Trim().ToLowerInvariant()
    if ($resolved.Exit -ne 0 -or $target -notmatch '^[0-9a-f]{40}$') {
        $found.Add('target-unresolved')
        return @($found)
    }
    $onTarget = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('merge-base', '--is-ancestor', $Pre, $target)
    if ($onTarget.Exit -ne 0) { $found.Add('pre-not-on-target') }
    # A later tree carries a target mainline commit past the seed that entered the candidate
    # through a merge, not through its own first-parent line, or is an unlanded candidate cut
    # from the target after the seed. The landed subject itself is neither: a no-ff land keeps
    # it off the mainline, a fast-forward keeps it first-parent. Residual: a candidate that is
    # already on the target is not diagnosed here; the scope line says CP-30 is not run on it.
    $landed = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('merge-base', '--is-ancestor', $Head, $target)
    if ($landed.Exit -ne 0) {
        $base = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('merge-base', $Head, $target)
        $baseSha = ([string]$base.Text).Trim()
        $covered = $null
        if ($base.Exit -eq 0 -and $baseSha -match '^[0-9a-f]{40}$') {
            $covered = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('merge-base', '--is-ancestor', $baseSha, $Seed)
        }
        if ($null -eq $covered -or $covered.Exit -ne 0) { $found.Add('later-tree') }
    }
    $carried = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-list', $Head, ('^' + $Seed))
    $mainline = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-list', '--first-parent', $target, ('^' + $Seed))
    $own = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-list', '--first-parent', $Head, ('^' + $Seed))
    if ($carried.Exit -ne 0 -or $mainline.Exit -ne 0 -or $own.Exit -ne 0) {
        $found.Add('later-tree')
    } else {
        $onMainline = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($line in @(([string]$mainline.Text) -split "`n")) { if ($line.Trim()) { [void]$onMainline.Add($line.Trim()) } }
        $ownLine = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($line in @(([string]$own.Text) -split "`n")) { if ($line.Trim()) { [void]$ownLine.Add($line.Trim()) } }
        foreach ($line in @(([string]$carried.Text) -split "`n")) {
            $commit = $line.Trim()
            if ($commit -and $onMainline.Contains($commit) -and -not $ownLine.Contains($commit)) {
                $found.Add('later-tree')
                break
            }
        }
    }
    return @($found | Select-Object -Unique)
}

# The fixture is a committed blob the seed author wrote; nothing here re-derives it. The line
# records the binding so a reviewer can compare the blob with an independent CP-27/CP-28 recapture.
function Get-LiveLaneCensusScopeLine {
    param($Context, [string]$TargetSha)
    $target = [string](Get-LiveLaneProp $Context 'landingTarget')
    if ([string]::IsNullOrWhiteSpace($target)) { $target = $script:LiveLaneLandingTargetRef }
    return ('census scope=commission not-run-on-later-trees seed=' + [string](Get-LiveLaneProp $Context 'censusBaseSha') +
        ' pre=' + [string](Get-LiveLaneProp $Context 'preS1BaseSha') +
        ' head=' + [string](Get-LiveLaneProp $Context 'sourceSha') +
        ' target=' + $target + '@' + $TargetSha +
        ' fixtureBlob=' + [string](Get-LiveLaneProp $Context 'fixtureBlob') +
        ' fixture=unauthenticated (compare the blob with an independent CP-27/CP-28 recapture)')
}

function Invoke-LiveLaneCensus {
    param($Context, [hashtable]$Mutant = $null)
    $diagnostics = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Context) { return @{ Accepted = $false; Diagnostics = @('context-required') } }
    $receipt = Test-LiveLaneProducerReceipt -Context $Context -Mutant $Mutant
    if (-not $receipt.Accepted) { $diagnostics.Add([string]$receipt.Reason) }
    $repo = [string](Get-LiveLaneProp $Context 'candidateRoot')
    if ([string]::IsNullOrWhiteSpace($repo)) { $repo = Get-LiveLaneRepoRoot (Get-Location).Path }
    $seed = [string](Get-LiveLaneProp $Context 'censusBaseSha')
    if ($Mutant -and $Mutant.ContainsKey('seed')) { $seed = [string]$Mutant.seed }
    if ([string]::IsNullOrWhiteSpace($seed)) { $diagnostics.Add('seed-object-missing') ; return @{ Accepted = $false; Diagnostics = @($diagnostics) } }
    $head = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', 'HEAD')).Text
    if ($seed -eq $head) { $diagnostics.Add('self-seed') }
    $ancestor = Get-LiveLaneGit -RepoRoot $repo -GitArgs @('merge-base', '--is-ancestor', $seed, $head)
    if ($ancestor.Exit -ne 0) { $diagnostics.Add('seed-not-ancestor') }
    $pre = [string](Get-LiveLaneProp $Context 'preS1BaseSha')
    $pin = Get-LiveLanePinnedPreS1 -RepoRoot $repo
    if ([string]::IsNullOrWhiteSpace($pin) -or $pin -ne $script:LiveLanePreS1BaseSha) { $diagnostics.Add('baseline-drift') }
    if ($pre -ne $script:LiveLanePreS1BaseSha -or $pre -ne $pin) { $diagnostics.Add('baseline-drift') }
    if ($pre -eq $seed -or $pre -eq $head) { $diagnostics.Add('self-seed') }
    $preAncestor = Get-LiveLaneGit -RepoRoot $repo -GitArgs @('merge-base', '--is-ancestor', $pre, $seed)
    if ($preAncestor.Exit -ne 0) { $diagnostics.Add('baseline-drift') }
    $targetRef = [string](Get-LiveLaneProp $Context 'landingTarget')
    if ([string]::IsNullOrWhiteSpace($targetRef)) { $targetRef = $script:LiveLaneLandingTargetRef }
    if ($Mutant -and $Mutant.ContainsKey('targetRef')) { $targetRef = [string]$Mutant.targetRef }
    foreach ($item in @(Get-LiveLaneTargetDiagnostics -RepoRoot $repo -Pre $pre -Seed $seed -Head $head -TargetRef $targetRef)) {
        $diagnostics.Add([string]$item)
    }
    if ($Mutant -and $Mutant.ContainsKey('boundSha')) {
        $boundMutant = ([string]$Mutant.boundSha).ToLowerInvariant()
        if ($boundMutant -ne $script:LiveLanePreS1BaseSha) { $diagnostics.Add('seed-engine') }
        if ($boundMutant -eq $seed -or $boundMutant -eq $head) { $diagnostics.Add('self-seed') }
    }
    $fixtureRel = [string](Get-LiveLaneProp $Context 'fixturePath')
    if ([string]::IsNullOrWhiteSpace($fixtureRel)) { $fixtureRel = $script:LiveLaneFixtureRel }
    $required = @($script:LiveLaneProtectedPaths) + @($fixtureRel)
    if ($Mutant -and $Mutant.ContainsKey('missingPath')) { $required += [string]$Mutant.missingPath }
    foreach ($rel in $required) {
        $exists = Get-LiveLaneGit -RepoRoot $repo -GitArgs @('cat-file', '-e', ($seed + ':' + $rel))
        if ($exists.Exit -ne 0) { $diagnostics.Add('seed-object-missing') }
    }
    $blob = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($seed + ':' + $fixtureRel))).Text
    $expectedBlob = [string](Get-LiveLaneProp $Context 'fixtureBlob')
    if ($Mutant -and $Mutant.ContainsKey('fixtureBlob')) { $expectedBlob = [string]$Mutant.fixtureBlob }
    if ([string]::IsNullOrWhiteSpace($blob) -or $blob -ne $expectedBlob) { $diagnostics.Add('fixture-binding') }
    $material = $null
    if (-not [string]::IsNullOrWhiteSpace($blob)) {
        $material = Get-LiveLaneCensusSeedMaterial -RepoRoot $repo -Seed $seed -FixtureRel $fixtureRel -Pre $pre
    }
    if ($null -eq $material) {
        $diagnostics.Add('seed-object-missing')
        return @{ Accepted = $false; Diagnostics = @($diagnostics | Select-Object -Unique) }
    }
    $before = $material.BeforeDir
    $fixture = $material.Fixture
    $parent = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($seed + '^'))).Text
    $recorded = $material.Recorded
    if ($recorded -ne $parent -or $recorded -eq $seed) { $diagnostics.Add('producer-provenance') }
    if ($recorded -eq $head -or $recorded -eq $seed) { $diagnostics.Add('self-seed') }
    $provenance = Get-LiveLaneProp $Context 'producerProvenance'
    if ($Mutant -and $Mutant.ContainsKey('provenance')) { $provenance = $Mutant.provenance }
    foreach ($project in @(ConvertTo-LiveLaneList (Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'projects'))) {
        $name = [string](Get-LiveLaneProp $project 'project')
        $wantHash = [string](Get-LiveLaneProp $project 'assemblyHash')
        $got = $null
        if ($provenance) { $got = Get-LiveLaneProp $provenance $name }
        $gotHash = [string](Get-LiveLaneProp $got 'assemblyHash')
        $gotSource = [string](Get-LiveLaneProp $got 'sourceSha')
        if ($gotHash -ne $wantHash -or $gotSource -ne $recorded) { $diagnostics.Add('producer-provenance') }
    }
    $names = @((Get-LiveLaneGit -RepoRoot $repo -GitArgs @('log', '--name-only', '--pretty=format:', ($pre + '..' + $seed))).Text -split "`n")
    $touched = @($names | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    if ($Mutant -and $Mutant.ContainsKey('extraPath')) { $touched += [string]$Mutant.extraPath }
    foreach ($name in $touched) {
        if ($script:LiveLaneImmutableSeedPaths -contains $name) { $diagnostics.Add('seed-engine') }
        if ($script:LiveLaneAllowedSeedPaths -notcontains $name) { $diagnostics.Add('seed-range') }
    }
    foreach ($item in @(Test-LiveLaneSeedDeclaresBase -RepoRoot $repo -Pre $pre -Seed $seed -Head $head)) {
        $diagnostics.Add([string]$item)
    }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $drift = $false
        if ($Mutant -and $Mutant.ContainsKey('driftPath') -and $Mutant.driftPath -eq $rel) { $drift = $true }
        $equal = $true
        if ($material.ProtectedEqual.ContainsKey($rel)) { $equal = [bool]$material.ProtectedEqual[$rel] }
        if ($drift -or -not $equal) { $diagnostics.Add('baseline-drift') }
    }
    $cp25Path = [string](Get-LiveLaneProp $Context 'cp25Export')
    $cp1Path = [string](Get-LiveLaneProp $Context 'cp1Export')
    if ($Mutant -and $Mutant.ContainsKey('cp25')) { $cp25Path = [string]$Mutant.cp25 }
    if ($Mutant -and $Mutant.ContainsKey('cp1')) { $cp1Path = [string]$Mutant.cp1 }
    $cp25 = Read-LiveLaneExportFile -Path $cp25Path
    $cp1 = Read-LiveLaneExportFile -Path $cp1Path
    $source = [string](Get-LiveLaneProp $Context 'sourceSha')
    $run = [string](Get-LiveLaneProp $Context 'runId')
    if (-not $cp25.Found -or -not $cp1.Found) { $diagnostics.Add('producer-incomplete') }
    else {
        foreach ($loaded in @($cp25, $cp1)) {
            $export = $loaded.Export
            $expectedHash = [string](Get-LiveLaneProp $export 'assemblyHash')
            $mutateExport = $Mutant -and ($Mutant.ContainsKey('exportSource') -or $Mutant.ContainsKey('exportRun') -or $Mutant.ContainsKey('exportHash') -or $Mutant.ContainsKey('terminal'))
            if ($mutateExport) { $export = Copy-LiveLaneExportShell $export }
            if ($Mutant -and $Mutant.ContainsKey('exportSource')) { $export | Add-Member -NotePropertyName sourceSha -NotePropertyValue $Mutant.exportSource -Force }
            if ($Mutant -and $Mutant.ContainsKey('exportRun')) { $export | Add-Member -NotePropertyName runId -NotePropertyValue $Mutant.exportRun -Force }
            if ($Mutant -and $Mutant.ContainsKey('exportHash')) { $export | Add-Member -NotePropertyName assemblyHash -NotePropertyValue $Mutant.exportHash -Force }
            if ($Mutant -and $Mutant.ContainsKey('terminal')) { $export | Add-Member -NotePropertyName terminal -NotePropertyValue ([bool]$Mutant.terminal) -Force }
            $check = Test-LiveLaneExportDocument -Export $export -ExpectedSource $source -ExpectedRun $run -ExpectedHash $expectedHash -ExpectedProject ([string](Get-LiveLaneProp $export 'project')) -RequireCore $false -AssemblyPath ([string](Get-LiveLaneProp $export 'assemblyPath'))
            if (-not $check.Accepted -and $diagnostics -notcontains $check.Reason) { $diagnostics.Add($check.Reason) }
        }
    }
    $useNodeEngine = -not ($Mutant -and $Mutant.ContainsKey('afterNodes'))
    $afterRoot = $repo
    if ($Mutant -and $Mutant.ContainsKey('afterRoot')) { $afterRoot = [string]$Mutant.afterRoot }
    try {
        $fast = $Mutant -and $Mutant.ContainsKey('fastHash') -and $Mutant.fastHash
        if ($fast) {
            Initialize-LiveLaneNightly -RepoRoot $repo
            $beforeText = [System.IO.File]::ReadAllText((Join-Path $before 'tests/test-execution-policy.json'))
            $afterPolicyFile = Join-Path $afterRoot 'tests/test-execution-policy.json'
            if ($Mutant.ContainsKey('policyPath') -and -not [string]::IsNullOrWhiteSpace([string]$Mutant.policyPath)) { $afterPolicyFile = [string]$Mutant.policyPath }
            $beforeHash = [Antiphon.Nightly.NightlyPolicy]::HashText($beforeText)
            $afterHash = [Antiphon.Nightly.NightlyPolicy]::HashText([System.IO.File]::ReadAllText($afterPolicyFile))
        } else {
            $beforeHash = Get-LiveLanePolicyHashProcess -RepoRoot $repo -PolicyRoot $before
            $afterPolicyArg = ''
            if ($Mutant -and $Mutant.ContainsKey('policyPath')) { $afterPolicyArg = [string]$Mutant.policyPath }
            $afterHash = Get-LiveLanePolicyHashProcess -RepoRoot $repo -PolicyRoot $afterRoot -PolicyPath $afterPolicyArg
        }
        if ($beforeHash -ne $afterHash) { $diagnostics.Add('policy-hash-changed') }
    } catch { $diagnostics.Add('policy-hash-changed') }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $afterFile = Join-Path $afterRoot $rel
        $h1 = ''
        if ($material.FileHash.ContainsKey($rel)) { $h1 = [string]$material.FileHash[$rel] }
        if ([string]::IsNullOrWhiteSpace($h1) -or -not (Test-Path -LiteralPath $afterFile)) { $diagnostics.Add('reader-input-changed'); continue }
        $h2 = (Get-FileHash -LiteralPath $afterFile -Algorithm SHA256).Hash
        if ($h1 -ne $h2) { $diagnostics.Add('reader-input-changed') }
    }
    Initialize-LiveLaneNightly -RepoRoot $repo
    $afterPolicyPath = Join-Path $afterRoot 'tests/test-execution-policy.json'
    if ($Mutant -and $Mutant.ContainsKey('policyPath')) { $afterPolicyPath = [string]$Mutant.policyPath }
    $platform = $(if ($IsLinux) { 'linux' } else { 'windows' })
    $beforeEx = @($material.BeforeEx)
    $afterEx = @(Get-LiveLaneCachedExclusions -PolicyPath $afterPolicyPath -Platform $platform)
    if ($useNodeEngine) {
        $payload = Get-LiveLaneCensusIdentityPayload -RepoRoot $repo
        $patch = ''
        if ($Mutant -and $Mutant.ContainsKey('patch')) { $patch = [string]$Mutant.patch }
        Initialize-LiveLaneCensusEngine
        $beforeFile = [string]$material.FixtureCopy
        $after1 = ''
        $after2 = ''
        if ($cp25.Found) { $after1 = $cp25Path }
        if ($cp1.Found) { $after2 = $cp1Path }
        $text = [LiveLaneCensusEngine]::Compare($beforeFile, $after1, $after2, [string]$material.BeforeExJson, (ConvertTo-LiveLaneExclusionJson $afterEx), [string]$payload.Additive, [string]$payload.Contract, $patch)
        foreach ($line in @(([string]$text) -split "`n")) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { $diagnostics.Add($line) }
        }
        $reasonChanged = $false
        $ownerChanged = $false
        $beforeBy = @{}
        foreach ($row in @($beforeEx)) { $beforeBy[[string]$row.Class] = $row }
        foreach ($row in @($afterEx)) {
            $cls = [string]$row.Class
            if (-not $beforeBy.ContainsKey($cls)) { continue }
            if ([string]$beforeBy[$cls].Reason -ne [string]$row.Reason) { $reasonChanged = $true }
            if ([string]$beforeBy[$cls].Owner -ne [string]$row.Owner) { $ownerChanged = $true }
        }
        if ($reasonChanged) { $diagnostics.Add('disposition-reason-changed') }
        if ($ownerChanged) { $diagnostics.Add('disposition-owner-changed') }
    } else {
        $afterNodes = @()
        if ($cp25.Found) { $afterNodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp25.Export 'nodes')) }
        if ($cp1.Found) { $afterNodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp1.Export 'nodes')) }
        if ($Mutant -and $Mutant.ContainsKey('afterNodes')) { $afterNodes = @($Mutant.afterNodes) }
        $beforeNodes = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $fixture 'nodes'))
        $additive = @(ConvertTo-LiveLaneList (Get-LiveLaneProp (Read-LiveLaneManifestObject $repo) 'additiveContract'))
        $member = Compare-LiveLaneMembership -BeforeNodes $beforeNodes -AfterNodes $afterNodes -BeforeEx $beforeEx -AfterEx $afterEx -Additive $additive
        foreach ($item in @($member)) { $diagnostics.Add($item) }
    }
    $unique = @($diagnostics | Select-Object -Unique)
    return @{ Accepted = ($unique.Count -eq 0); Diagnostics = $unique }
}

function Get-LiveLaneExclusionsFromObject {
    param($PolicyObject, [string]$Platform)
    $ids = @()
    foreach ($id in @(ConvertTo-LiveLaneList $PolicyObject.defaultSuites)) { $ids += [string]$id }
    foreach ($id in @(ConvertTo-LiveLaneList $PolicyObject.profiles.rc.requiredSuites)) { $ids += [string]$id }
    $rows = @()
    foreach ($id in @($ids | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        $rows += @([Antiphon.Nightly.NightlyPolicy]::ProfileExclusions($PolicyObject, 'rc', $id, $Platform))
    }
    return @($rows)
}

function Compare-LiveLaneMembership {
    param($BeforeNodes, $AfterNodes, $BeforeEx, $AfterEx, $Additive)
    $diagnostics = @()
    $beforeDefault = Get-LiveLaneDispositionSets -Nodes $BeforeNodes -Exclusions $null -ProfileAware $false
    $afterDefault = Get-LiveLaneDispositionSets -Nodes $AfterNodes -Exclusions $null -ProfileAware $false
    $beforeRc = Get-LiveLaneDispositionSets -Nodes $BeforeNodes -Exclusions $BeforeEx -ProfileAware $true
    $afterRc = Get-LiveLaneDispositionSets -Nodes $AfterNodes -Exclusions $AfterEx -ProfileAware $true
    $allowed = @{}
    foreach ($n in @(ConvertTo-LiveLaneList $AfterNodes)) {
        $class = [string](Get-LiveLaneProp $n 'type')
        if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $n 'class') }
        $method = [string](Get-LiveLaneProp $n 'method')
        foreach ($row in @(ConvertTo-LiveLaneList $Additive)) {
            if ([string](Get-LiveLaneProp $row 'class') -eq $class -and [string](Get-LiveLaneProp $row 'method') -eq $method) {
                $allowed[(Get-LiveLaneNodeUid $n)] = $true
            }
        }
    }
    if (-not (Test-LiveLaneSetEqual $beforeDefault.Required $afterDefault.Required $allowed) -or -not (Test-LiveLaneSetEqual $beforeDefault.Excluded $afterDefault.Excluded $allowed)) {
        $diagnostics += 'nightly-disposition-changed'
    }
    if (-not (Test-LiveLaneSetEqual $beforeRc.Required $afterRc.Required $allowed) -or -not (Test-LiveLaneSetEqual $beforeRc.Excluded $afterRc.Excluded $allowed)) {
        $c680 = @($BeforeNodes | Where-Object { [string](Get-LiveLaneProp $_ 'method') -eq 'C680_LiveNudgeRefusesPermissionAndQuestionDialogs' } | Select-Object -First 1)
        if ($c680.Count -eq 1) {
            $uid = Get-LiveLaneNodeUid $c680[0]
            $wasRequired = @($beforeRc.Required) -contains $uid
            $nowExcluded = @($afterRc.Excluded) -contains $uid
            if ($wasRequired -and $nowExcluded) { $diagnostics += 'C680-required-to-excluded' }
        }
    }
    $reasonChanged = $false
    $ownerChanged = $false
    $beforeBy = @{}
    foreach ($row in @(ConvertTo-LiveLaneList $BeforeEx)) { $beforeBy[[string]$row.Class] = $row }
    foreach ($row in @(ConvertTo-LiveLaneList $AfterEx)) {
        $cls = [string]$row.Class
        if (-not $beforeBy.ContainsKey($cls)) { continue }
        if ([string]$beforeBy[$cls].Reason -ne [string]$row.Reason) { $reasonChanged = $true }
        if ([string]$beforeBy[$cls].Owner -ne [string]$row.Owner) { $ownerChanged = $true }
    }
    if ($reasonChanged) { $diagnostics += 'disposition-reason-changed' }
    if ($ownerChanged) { $diagnostics += 'disposition-owner-changed' }
    return @($diagnostics)
}

function Get-LiveLaneDispositionSets {
    param($Nodes, $Exclusions, [bool]$ProfileAware)
    $required = @()
    $excluded = @()
    foreach ($n in @(ConvertTo-LiveLaneList $Nodes)) {
        $node = [pscustomobject]@{
            Uid = Get-LiveLaneNodeUid $n
            ClassName = ([string](Get-LiveLaneProp $n 'namespace') + '.' + [string](Get-LiveLaneProp $n 'type'))
            Type = [string](Get-LiveLaneProp $n 'type')
            Method = [string](Get-LiveLaneProp $n 'method')
            Categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'categories'))
            Excluded = $false
        }
        $isExcluded = $false
        if ($ProfileAware) { $isExcluded = [Antiphon.Nightly.NightlyPolicy]::NodeExcluded($node, $Exclusions) }
        else { $isExcluded = [Antiphon.Nightly.NightlyCensus]::Excluded($node, $null, $false) }
        if ($isExcluded) { $excluded += $node.Uid } else { $required += $node.Uid }
    }
    return @{ Required = @($required); Excluded = @($excluded) }
}

function Test-LiveLaneSetEqual {
    param($Left, $Right, $AllowedNew)
    $a = @($Left | Sort-Object -Unique)
    $b = @($Right | Where-Object { -not $AllowedNew.ContainsKey($_) } | Sort-Object -Unique)
    return (($a -join '|') -eq ($b -join '|'))
}

function New-LiveLaneDanglingSeed {
    param([string]$RepoRoot)
    # Compiled (LiveLaneScratch.DanglingSeed): the commit's loose object is recorded in this session's scratch ledger.
    try { return $script:LiveLaneScratch.DanglingSeed($RepoRoot) } catch { throw (Get-NightlyRefusalText $_) }
}

function New-LiveLanePolicyMutant {
    param([string]$RepoRoot, [scriptblock]$Edit)
    $scratch = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('policy' + [guid]::NewGuid().ToString('N'))
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $dest = Join-Path $scratch $rel
        [void][IO.Directory]::CreateDirectory((Split-Path $dest -Parent))
        Copy-Item -LiteralPath (Join-Path $RepoRoot $rel) -Destination $dest -Force
    }
    $policyPath = Join-Path $scratch 'tests/test-execution-policy.json'
    $obj = [System.IO.File]::ReadAllText($policyPath) | ConvertFrom-Json
    & $Edit $obj
    Initialize-LiveLaneNightly -RepoRoot $RepoRoot
    $obj.policyHash = [Antiphon.Nightly.NightlyPolicy]::HashText(($obj | ConvertTo-Json -Depth 100))
    ($obj | ConvertTo-Json -Depth 100) | Set-Content -LiteralPath $policyPath -Encoding utf8
    return @{ Root = $scratch; PolicyPath = $policyPath }
}

function Copy-LiveLaneNodeRows {
    param($Nodes)
    $out = @()
    foreach ($n in @(ConvertTo-LiveLaneList $Nodes)) {
        $out += [pscustomobject]@{
            project = [string](Get-LiveLaneProp $n 'project')
            uid = [string](Get-LiveLaneProp $n 'uid')
            namespace = [string](Get-LiveLaneProp $n 'namespace')
            type = [string](Get-LiveLaneProp $n 'type')
            class = [string](Get-LiveLaneProp $n 'class')
            method = [string](Get-LiveLaneProp $n 'method')
            arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'arguments') | ForEach-Object { [string]$_ })
            categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'categories') | ForEach-Object { [string]$_ })
        }
    }
    return @($out)
}

function Test-LiveLaneCensusHas {
    param($Result, [string[]]$Need)
    if ($Result.Accepted) { return $false }
    $have = @($Result.Diagnostics)
    foreach ($item in @($Need)) {
        if ($have -notcontains $item) { return $false }
    }
    return $true
}

function Invoke-LiveLaneFullCensus {
    param([string]$RepoRoot)
    try {
        return Invoke-LiveLaneFullCensusBody -RepoRoot $RepoRoot
    } finally {
        Clear-LiveLaneOwnedScratch
    }
}

function Invoke-LiveLaneFullCensusBody {
    param([string]$RepoRoot)
    $context = Read-LiveLaneContextObject
    if ($null -eq $context) {
        Write-Output 'context-required'
        return 2
    }
    $scopeTarget = [string](Get-LiveLaneProp $context 'landingTarget')
    if ([string]::IsNullOrWhiteSpace($scopeTarget)) { $scopeTarget = $script:LiveLaneLandingTargetRef }
    $scopeRepo = [string](Get-LiveLaneProp $context 'candidateRoot')
    if ([string]::IsNullOrWhiteSpace($scopeRepo)) { $scopeRepo = $RepoRoot }
    $scopeSha = (Get-LiveLaneGit -RepoRoot $scopeRepo -GitArgs @('rev-parse', '--verify', '--quiet', ($scopeTarget + '^{commit}'))).Text
    Write-Output (Get-LiveLaneCensusScopeLine -Context $context -TargetSha ([string]$scopeSha).Trim())
    $real = Invoke-LiveLaneCensus -Context $context
    if (-not $real.Accepted) {
        foreach ($item in @($real.Diagnostics)) { Write-Output $item }
        return 2
    }
    $missing = New-Object System.Collections.Generic.List[string]
    $absent = Invoke-LiveLaneCensus -Context $null
    if (-not (Test-LiveLaneCensusHas $absent @('context-required'))) { $missing.Add('context-absent') }

    $dangling = New-LiveLaneDanglingSeed -RepoRoot $RepoRoot
    $seedCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ seed = $dangling; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $seedCase @('seed-not-ancestor'))) { $missing.Add('seed-nonancestor') }

    $objectCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ missingPath = 'tests/fixtures/live-lanes/does-not-exist'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $objectCase @('seed-object-missing'))) { $missing.Add('object-missing') }

    $blobCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ fixtureBlob = ('a' * 40); fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $blobCase @('fixture-binding'))) { $missing.Add('fixture-blob') }

    $badProv = [pscustomobject]@{
        'Antiphon.Tests' = [pscustomobject]@{ sourceSha = '0' * 40; assemblyHash = '0' * 64 }
        'Antiphon.Agents.Pty.Tests' = [pscustomobject]@{ sourceSha = '0' * 40; assemblyHash = '0' * 64 }
    }
    $provCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ provenance = $badProv; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $provCase @('producer-provenance'))) { $missing.Add('producer-provenance') }

    $driftCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ driftPath = 'tests/test-execution-policy.json'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $driftCase @('baseline-drift'))) { $missing.Add('prebase-drift') }

    $rangeCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ extraPath = 'docs/not-a-live-lane.md'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $rangeCase @('seed-range'))) { $missing.Add('seed-unlisted-edit') }

    $sourceCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ exportSource = ('b' * 40); fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $sourceCase @('source-mismatch'))) { $missing.Add('candidate-source') }
    $buildCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ exportHash = ('c' * 64); fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $buildCase @('build-mismatch'))) { $missing.Add('candidate-build') }
    $runCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ exportRun = 'other-run'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $runCase @('run-mismatch'))) { $missing.Add('candidate-run') }
    $pendingCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ terminal = $false; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $pendingCase @('producer-incomplete'))) { $missing.Add('pending-producer') }

    $redRoot = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('receiptred' + [guid]::NewGuid().ToString('N'))
    Write-LiveLaneReceiptFixture -Root $redRoot -Commit ([string](Get-LiveLaneProp $context 'sourceSha')) -RowState 'red'
    $redCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ receiptRoot = $redRoot; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $redCase @('producer-row-not-green'))) { $missing.Add('producer-row-red') }

    $timedRoot = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('receipttime' + [guid]::NewGuid().ToString('N'))
    Write-LiveLaneReceiptFixture -Root $timedRoot -Commit ([string](Get-LiveLaneProp $context 'sourceSha')) -RowState 'running'
    $timedCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ receiptRoot = $timedRoot; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $timedCase @('producer-row-not-green'))) { $missing.Add('producer-row-timeout') }

    $absentRoot = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('receiptmiss' + [guid]::NewGuid().ToString('N'))
    $absentCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ receiptRoot = $absentRoot; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $absentCase @('producer-receipt-missing'))) { $missing.Add('producer-receipt-missing') }

    $foreignRoot = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('receiptforeign' + [guid]::NewGuid().ToString('N'))
    Write-LiveLaneReceiptFixture -Root $foreignRoot -Commit ('b' * 40) -RowState 'green' -BuildSource 'unknown' -LineCommit ('b' * 40)
    $foreignCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ receiptRoot = $foreignRoot; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $foreignCase @('producer-receipt-foreign'))) { $missing.Add('producer-receipt-foreign') }

    $dropCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'drop-first'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $dropCase @('old-membership-changed'))) { $missing.Add('remove-old-uid') }

    $catCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'remove-integration'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $catCase @('category-delta'))) { $missing.Add('remove-Integration') }

    $tagCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'tag-seventh'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $tagCase @('category-delta', 'liveclaude-count'))) { $missing.Add('tag-seventh') }

    $dropLive = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'drop-live'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $dropLive @('liveclaude-count'))) { $missing.Add('drop-liveclaude') }

    $headNow = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', 'HEAD')).Text.Trim()
    $selfCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ seed = $headNow; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $selfCase @('self-seed'))) { $missing.Add('self-seed') }

    $engineCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ extraPath = 'tests/fixtures/live-lanes/pre-s1-base.sha'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $engineCase @('seed-engine'))) { $missing.Add('seed-engine') }

    $boundCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ boundSha = $headNow; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $boundCase @('seed-engine', 'self-seed'))) { $missing.Add('seed-declares-base') }

    $extraCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'extra-expansion'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $extraCase @('extra-expansion'))) { $missing.Add('extra-expansion') }

    $unknownCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'unknown-node'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $unknownCase @('undeclared-new-uid'))) { $missing.Add('unknown-new-uid') }

    $nightCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'add-optin'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $nightCase @('nightly-disposition-changed'))) { $missing.Add('default-disposition') }

    $readerRoot = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('reader' + [guid]::NewGuid().ToString('N'))
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $dest = Join-Path $readerRoot $rel
        [void][IO.Directory]::CreateDirectory((Split-Path $dest -Parent))
        Copy-Item -LiteralPath (Join-Path $RepoRoot $rel) -Destination $dest -Force
    }
    Add-Content -LiteralPath (Join-Path $readerRoot 'scripts/lib/nightly-coverage.ps1') -Value "`n# c1545 scratch`n" -Encoding ascii
    $readerCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $readerRoot; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $readerCase @('reader-input-changed'))) { $missing.Add('reader-edit') }

    $c680 = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit {
        param($obj)
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($e in @(ConvertTo-LiveLaneList $obj.profiles.rc.exclusions)) { $list.Add($e) }
        $list.Add([pscustomobject]@{ suite = 'antiphon'; class = 'ClaudeDialogWatchdogCanaryTests'; reason = 'card-1545 scratch exclusion'; owner = 'CARD-1545' })
        $obj.profiles.rc.exclusions = $list.ToArray()
    }
    $c680Case = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $c680.Root; policyPath = $c680.PolicyPath; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $c680Case @('policy-hash-changed', 'C680-required-to-excluded'))) { $missing.Add('c680-exclusion') }

    $reason = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit {
        param($obj)
        $obj.profiles.rc.exclusions[0].reason = ([string]$obj.profiles.rc.exclusions[0].reason + ' scratch')
    }
    $reasonCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $reason.Root; policyPath = $reason.PolicyPath; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $reasonCase @('disposition-reason-changed'))) { $missing.Add('reason-edit') }

    $owner = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit {
        param($obj)
        $obj.profiles.rc.exclusions[0].owner = 'CARD-1545-SCRATCH'
    }
    $ownerCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $owner.Root; policyPath = $owner.PolicyPath; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $ownerCase @('disposition-owner-changed'))) { $missing.Add('owner-edit') }

    if ($missing.Count -gt 0) {
        foreach ($name in @($missing)) { Write-Output ('expected-refusal-missing ' + $name) }
        return 1
    }
    Write-Output 'accepted'
    return 0
}
