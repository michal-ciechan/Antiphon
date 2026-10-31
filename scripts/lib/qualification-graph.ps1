#requires -Version 5.1
# S1 is an inert parser/identity library. No admission, execution or release credit.
# Syntax parses in Windows PowerShell 5.1; execution needs PowerShell 7/Test-Json.
if (-not ('QualificationGraphJson' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    public static string Canonical(JsonElement value)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            { Write(writer, value, ""); }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
    private static bool IsSet(string key)
    {
        return key == "nodes" || key == "targets" || key == "edges" || key == "resourceKeys";
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
                decimal number;
                if (e.TryGetDecimal(out number))
                    w.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                else w.WriteRawValue(e.GetDouble().ToString("R", CultureInfo.InvariantCulture));
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

function Read-QualificationGraph {
    param(
        [string]$Json,
        [string]$CatalogJson,
        [string]$SchemaPath = (Join-Path $PSScriptRoot '../../docs/schemas/qualification-run-plan-v1.schema.json')
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
        [void][QualificationGraphJson]::Decode($CatalogJson)
        $catalog = $CatalogJson | ConvertFrom-Json
    } catch { return New-QualificationRefusal 'identity' '/catalog' 'catalog-invalid' }
    $graph = $Json | ConvertFrom-Json
    foreach ($field in @('profile', 'policyDigest', 'rosterDigest', 'candidate')) {
        $actual = ConvertTo-QualificationCanonicalJson (ConvertTo-Json -InputObject $graph.$field -Depth 64 -Compress)
        $expected = ConvertTo-QualificationCanonicalJson (ConvertTo-Json -InputObject $catalog.$field -Depth 64 -Compress)
        if ($actual -cne $expected) { return New-QualificationRefusal 'identity' ('/' + $field) 'catalog-mismatch' }
    }
    $targetIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    for ($i = 0; $i -lt $graph.targets.Count; $i++) {
        $target = $graph.targets[$i]
        if (-not $targetIds.Add($target.id)) { return New-QualificationRefusal 'identity' "/targets/$i/id" 'duplicate-target' }
        $known = @($catalog.targets | Where-Object { $_.id -ceq $target.id })
        if ($known.Count -ne 1) { return New-QualificationRefusal 'identity' "/targets/$i/id" 'target-unregistered' }
        foreach ($field in @('bindingDigest', 'capability', 'parallelism', 'resourceKeys', 'quota')) {
            $actual = ConvertTo-QualificationCanonicalJson (ConvertTo-Json -InputObject @{ $field = $target.$field } -Depth 64 -Compress)
            $expected = ConvertTo-QualificationCanonicalJson (ConvertTo-Json -InputObject @{ $field = $known[0].$field } -Depth 64 -Compress)
            if ($actual -cne $expected) { return New-QualificationRefusal 'identity' "/targets/$i/$field" 'catalog-mismatch' }
        }
    }
    $nodeIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    for ($i = 0; $i -lt $graph.nodes.Count; $i++) {
        $node = $graph.nodes[$i]
        if (-not $nodeIds.Add($node.id)) { return New-QualificationRefusal 'identity' "/nodes/$i/id" 'duplicate-node' }
        $known = @($catalog.nodes | Where-Object { $_.id -ceq $node.id })
        if ($known.Count -ne 1) { return New-QualificationRefusal 'identity' "/nodes/$i/id" 'node-unregistered' }
        # Target/binding and ordering annotations are deliberately outside node identity.
        foreach ($field in @('kind', 'platformOfRecord', 'coverageRole', 'laneId', 'project', 'suite', 'chunk', 'canonicalOrdinal', 'action', 'requires', 'disposition', 'inputFingerprint', 'skipReceipt')) {
            $actual = ConvertTo-QualificationCanonicalJson (ConvertTo-Json -InputObject $node.$field -Depth 64 -Compress)
            $expected = ConvertTo-QualificationCanonicalJson (ConvertTo-Json -InputObject $known[0].$field -Depth 64 -Compress)
            if ($actual -cne $expected) { return New-QualificationRefusal 'identity' "/nodes/$i/$field" 'catalog-mismatch' }
        }
        foreach ($field in @('filter', 'uidSetDigest', 'expectedCount')) {
            if ($node.selection.$field -cne $known[0].selection.$field) {
                return New-QualificationRefusal 'identity' "/nodes/$i/selection/$field" 'catalog-mismatch'
            }
        }
        if (-not $targetIds.Contains($node.targetId)) {
            return New-QualificationRefusal 'identity' "/nodes/$i/targetId" 'target-unregistered'
        }
        if ($null -ne $node.selection.uidSetDigest) {
            $uidProperty = @($catalog.uids.PSObject.Properties | Where-Object { $_.Name -ceq $node.id })
            if ($uidProperty.Count -ne 1 -or
                (Get-QualificationUidSetDigest $uidProperty[0].Value) -cne $node.selection.uidSetDigest -or
                @($uidProperty[0].Value).Count -ne $node.selection.expectedCount) {
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
