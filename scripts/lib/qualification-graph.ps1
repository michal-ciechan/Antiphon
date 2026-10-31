#requires -Version 7.4
# S1 is an inert parser/identity library. No admission, execution or release credit.
# Run with pwsh 7.4+; System.Text.Json and draft 2020-12 Test-Json are required.
if (-not ('QualificationGraphJson' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

public static class QualificationGraphJson
{
    public static JsonElement Decode(string raw)
    {
        using (var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 64 }))
        {
            Inspect(doc.RootElement, "");
            return doc.RootElement.Clone();
        }
    }
    private static void Inspect(JsonElement e, string path)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject())
            {
                string child = path + "/" + p.Name.Replace("~", "~0").Replace("/", "~1");
                if (!keys.Add(p.Name)) throw new InvalidDataException("duplicate-key|" + child);
                Inspect(p.Value, child);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in e.EnumerateArray()) Inspect(v, path + "/" + i++);
        }
        else if (e.ValueKind == JsonValueKind.Number)
        {
            double number = e.GetDouble();
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw new InvalidDataException("nonfinite|" + path);
        }
    }
    public static string InvalidDate(JsonElement e, string path = "")
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                string child = path + "/" + p.Name;
                if ((p.Name == "closedAtUtc" || p.Name == "lastCompletedAtUtc") &&
                    p.Value.ValueKind == JsonValueKind.String)
                {
                    DateTimeOffset date;
                    string s = p.Value.GetString();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(s,
                        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$") ||
                        !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out date)) return child;
                }
                string invalid = InvalidDate(p.Value, child);
                if (invalid != null) return invalid;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var v in e.EnumerateArray())
            {
                string invalid = InvalidDate(v, path + "/" + i++);
                if (invalid != null) return invalid;
            }
        }
        return null;
    }
    public static string Canonical(JsonElement value, string key = "")
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            { Write(writer, value, key); }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
    private static bool IsSet(string key)
    {
        return key == "nodes" || key == "targets" || key == "edges" || key == "resourceKeys";
    }
    private static string CanonicalNumber(string raw)
    {
        // Normalize the decimal coefficient and exponent without rounding through
        // decimal/double. In particular, a finite underflow must not become zero.
        bool negative = raw[0] == '-';
        string unsigned = negative ? raw.Substring(1) : raw;
        int e = unsigned.IndexOfAny(new[] { 'e', 'E' });
        string coefficient = e < 0 ? unsigned : unsigned.Substring(0, e);
        BigInteger exponent = e < 0 ? BigInteger.Zero :
            BigInteger.Parse(unsigned.Substring(e + 1), CultureInfo.InvariantCulture);
        int point = coefficient.IndexOf('.');
        if (point >= 0)
        {
            exponent -= coefficient.Length - point - 1;
            coefficient = coefficient.Remove(point, 1);
        }
        coefficient = coefficient.TrimStart('0');
        if (coefficient.Length == 0) return "0";
        int significant = coefficient.Length;
        while (coefficient[significant - 1] == '0') { significant--; exponent++; }
        coefficient = coefficient.Substring(0, significant);
        string value;
        if (exponent < -20 || exponent > 20)
            value = coefficient + "e" + exponent.ToString(CultureInfo.InvariantCulture);
        else if (exponent >= 0)
            value = coefficient + new string('0', (int)exponent);
        else
        {
            point = coefficient.Length + (int)exponent;
            value = point > 0 ? coefficient.Insert(point, ".") :
                "0." + new string('0', -point) + coefficient;
        }
        return negative ? "-" + value : value;
    }
    private static void Write(Utf8JsonWriter w, JsonElement e, string key)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { w.WritePropertyName(p.Name); Write(w, p.Value, p.Name); }
                w.WriteEndObject(); break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                IEnumerable<JsonElement> values = e.EnumerateArray().ToArray();
                if (IsSet(key)) values = values.OrderBy(v => Canonical(v), StringComparer.Ordinal);
                foreach (var v in values) Write(w, v, "");
                w.WriteEndArray(); break;
            case JsonValueKind.Number:
                w.WriteRawValue(CanonicalNumber(e.GetRawText()));
                break;
            default: e.WriteTo(w); break;
        }
    }
}
'@
}

function Get-QualificationSha256 {
    param([string]$Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function ConvertTo-QualificationCanonicalJson {
    param([string]$Json)
    return [QualificationGraphJson]::Canonical([QualificationGraphJson]::Decode($Json))
}

function Get-QualificationUidSetDigest {
    param([string[]]$Uids)
    $sorted = [string[]]@($Uids)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($uid in $sorted) { if (-not $seen.Add($uid)) { throw 'duplicate-uid' } }
    $json = ConvertTo-Json -InputObject $sorted -Compress
    return Get-QualificationSha256 (ConvertTo-QualificationCanonicalJson $json)
}

function Get-QualificationGraphSchemaResult {
    param([string]$Json, [string]$SchemaPath)
    # The actual checked-in schema is the authority. Explicit dates supplement
    # engines treating JSON Schema format as annotation.
    $schemaErrors = @()
    $valid = Test-Json -Json $Json -SchemaFile $SchemaPath -ErrorAction SilentlyContinue -ErrorVariable schemaErrors
    $paths = @()
    if (-not $valid) {
        foreach ($errorRecord in $schemaErrors) {
            $message = $errorRecord.Exception.Message
            if ($message -match " at '([^']*)'$") {
                $parent = $Matches[1]
                $paths += $parent
                if ($message -match 'Required properties (\[.*\]) are not present') {
                    foreach ($property in ($Matches[1] | ConvertFrom-Json)) {
                        $paths += $parent + '/' + $property
                    }
                }
            }
        }
    } else {
        $datePath = [QualificationGraphJson]::InvalidDate([QualificationGraphJson]::Decode($Json))
        if (-not [string]::IsNullOrEmpty($datePath)) { $valid = $false; $paths += $datePath }
    }
    return [pscustomobject]@{ Accepted = [bool]$valid; Paths = @($paths | Select-Object -Unique) }
}

function Test-QualificationGraphSchema {
    param([string]$Json, [string]$SchemaPath)
    return (Get-QualificationGraphSchemaResult $Json $SchemaPath).Accepted
}

function New-QualificationRefusal {
    param([string]$Category, [string]$Path, [string]$Reason)
    return [pscustomobject]@{ Accepted = $false; Category = $Category; Path = $Path; Reason = $Reason }
}


function Test-QualificationCatalogSchema {
    param([string]$Json, [string]$SchemaPath)
    # Reuse the graph schema's exact object definitions, with a closed catalog root.
    # A catalog cannot carry graph-only fields (phase, runId, edges, history, ...).
    $source = [IO.File]::ReadAllText($SchemaPath) | ConvertFrom-Json -AsHashtable
    $fields = @('profile', 'policyDigest', 'rosterDigest', 'candidate', 'targets', 'nodes', 'uids')
    $properties = [ordered]@{}
    foreach ($field in $fields) {
        if ($field -ceq 'uids') {
            $properties[$field] = @{ type = 'object'; additionalProperties = @{
                type = 'array'; uniqueItems = $true; items = @{ type = 'string'; minLength = 1 }
            } }
        } else { $properties[$field] = $source.properties[$field] }
    }
    $catalogSchema = [ordered]@{
        '$schema' = $source.'$schema'; type = 'object'; additionalProperties = $false
        required = $fields; properties = $properties; '$defs' = $source.'$defs'
    } | ConvertTo-Json -Depth 64 -Compress
    return Test-Json -Json $Json -Schema $catalogSchema -ErrorAction SilentlyContinue
}

function Get-QualificationFieldIdentity {
    param([System.Text.Json.JsonElement]$Object, [string]$Field)
    return [QualificationGraphJson]::Canonical($Object.GetProperty($Field), $Field)
}

function Read-QualificationGraph {
    param(
        [string]$Json,
        [string]$CatalogJson,
        [string]$SchemaPath = (Join-Path $PSScriptRoot '../../docs/schemas/qualification-run-plan-v1.schema.json'),
        [string]$ExpectedCatalogHash
    )
    try { $decoded = [QualificationGraphJson]::Decode($Json) }
    catch {
        $reason = 'invalid-json'
        $path = ''
        $message = $_.Exception.GetBaseException().Message
        if ($message -match '^(duplicate-key|nonfinite)\|(.*)$') { $reason = $Matches[1]; $path = $Matches[2] }
        return New-QualificationRefusal 'raw' $path $reason
    }
    $schemaResult = Get-QualificationGraphSchemaResult $Json $SchemaPath
    if (-not $schemaResult.Accepted) {
        $refusal = New-QualificationRefusal 'schema' '' 'schema-invalid'
        $refusal | Add-Member -NotePropertyName SchemaPaths -NotePropertyValue $schemaResult.Paths
        return $refusal
    }
    if ([string]::IsNullOrWhiteSpace($CatalogJson)) {
        return New-QualificationRefusal 'identity' '/catalog' 'catalog-required'
    }
    try {
        $catalog = [QualificationGraphJson]::Decode($CatalogJson)
        if (-not (Test-QualificationCatalogSchema $CatalogJson $SchemaPath)) { throw 'catalog-shape' }
    } catch { return New-QualificationRefusal 'identity' '/catalog' 'catalog-invalid' }
    if ($ExpectedCatalogHash -cnotmatch '^[a-f0-9]{64}$') {
        return New-QualificationRefusal 'identity' '/catalog' 'catalog-pin-required'
    }
    if ((Get-QualificationSha256 $CatalogJson) -cne $ExpectedCatalogHash) {
        return New-QualificationRefusal 'identity' '/catalog' 'catalog-pin-mismatch'
    }
    $graph = $Json | ConvertFrom-Json
    foreach ($field in @('profile', 'policyDigest', 'rosterDigest', 'candidate')) {
        if ((Get-QualificationFieldIdentity $decoded $field) -cne (Get-QualificationFieldIdentity $catalog $field)) {
            return New-QualificationRefusal 'identity' ('/' + $field) 'catalog-mismatch'
        }
    }
    $targetIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $targets = @($decoded.GetProperty('targets').EnumerateArray())
    $catalogTargets = @($catalog.GetProperty('targets').EnumerateArray())
    for ($i = 0; $i -lt $targets.Count; $i++) {
        $target = $targets[$i]
        $id = $target.GetProperty('id').GetString()
        if (-not $targetIds.Add($id)) { return New-QualificationRefusal 'identity' "/targets/$i/id" 'duplicate-target' }
        $known = @($catalogTargets | Where-Object { $_.GetProperty('id').GetString() -ceq $id })
        if ($known.Count -ne 1) { return New-QualificationRefusal 'identity' "/targets/$i/id" 'target-unregistered' }
        foreach ($field in @('bindingDigest', 'capability', 'parallelism', 'resourceKeys', 'quota')) {
            if ((Get-QualificationFieldIdentity $target $field) -cne (Get-QualificationFieldIdentity $known[0] $field)) {
                return New-QualificationRefusal 'identity' "/targets/$i/$field" 'catalog-mismatch'
            }
        }
    }
    $nodeIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $nodes = @($decoded.GetProperty('nodes').EnumerateArray())
    $catalogNodes = @($catalog.GetProperty('nodes').EnumerateArray())
    for ($i = 0; $i -lt $nodes.Count; $i++) {
        $node = $nodes[$i]
        $id = $node.GetProperty('id').GetString()
        if (-not $nodeIds.Add($id)) { return New-QualificationRefusal 'identity' "/nodes/$i/id" 'duplicate-node' }
        $known = @($catalogNodes | Where-Object { $_.GetProperty('id').GetString() -ceq $id })
        if ($known.Count -ne 1) { return New-QualificationRefusal 'identity' "/nodes/$i/id" 'node-unregistered' }
        # Compare decoded subtrees directly; never round-trip identity through PowerShell numbers.
        foreach ($field in @('kind', 'platformOfRecord', 'coverageRole', 'laneId', 'project', 'suite', 'chunk', 'canonicalOrdinal', 'action', 'requires', 'disposition', 'inputFingerprint', 'skipReceipt')) {
            if ((Get-QualificationFieldIdentity $node $field) -cne (Get-QualificationFieldIdentity $known[0] $field)) {
                return New-QualificationRefusal 'identity' "/nodes/$i/$field" 'catalog-mismatch'
            }
        }
        $selection = $node.GetProperty('selection')
        $knownSelection = $known[0].GetProperty('selection')
        foreach ($field in @('filter', 'uidSetDigest', 'expectedCount')) {
            if ((Get-QualificationFieldIdentity $selection $field) -cne (Get-QualificationFieldIdentity $knownSelection $field)) {
                return New-QualificationRefusal 'identity' "/nodes/$i/selection/$field" 'catalog-mismatch'
            }
        }
        if (-not $targetIds.Contains($node.GetProperty('targetId').GetString())) {
            return New-QualificationRefusal 'identity' "/nodes/$i/targetId" 'target-unregistered'
        }
        if ($selection.GetProperty('uidSetDigest').ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
            $uids = [System.Text.Json.JsonElement]::new()
            if (-not $catalog.GetProperty('uids').TryGetProperty($id, [ref]$uids)) {
                return New-QualificationRefusal 'identity' "/nodes/$i/selection" 'catalog-uid-mismatch'
            }
            $members = @($uids.EnumerateArray() | ForEach-Object { $_.GetString() })
            if ((Get-QualificationUidSetDigest $members) -cne $selection.GetProperty('uidSetDigest').GetString() -or
                [QualificationGraphJson]::Canonical([QualificationGraphJson]::Decode([string]$members.Count)) -cne
                    (Get-QualificationFieldIdentity $selection 'expectedCount')) {
                return New-QualificationRefusal 'identity' "/nodes/$i/selection" 'catalog-uid-mismatch'
            }
        }
    }
    $canonical = [QualificationGraphJson]::Canonical($decoded)
    return [pscustomobject]@{
        Accepted = $true; Category = 'accepted'; Path = ''; Reason = ''
        Graph = $graph; CanonicalJson = $canonical; GraphDigest = Get-QualificationSha256 $canonical
        NodeIds = @($graph.nodes | ForEach-Object { $_.id })
        UidSetDigests = @($graph.nodes | ForEach-Object { $_.selection.uidSetDigest })
    }
}
