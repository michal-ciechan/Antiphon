using System.Text.Json.Nodes;

namespace Antiphon.Server.Application.Review.Abf;

/// <summary>Canonical JSON (spec section 4): the form Antiphon stores next to the raw text.</summary>
public static class AbfCanonicalJson
{
    public static JsonObject ToJson(AbfDocument document)
    {
        var records = new JsonArray();
        foreach (var record in document.Records)
            records.Add(Record(record));

        return new JsonObject
        {
            ["abf"] = document.Syntax,
            ["type"] = document.Type,
            ["version"] = document.Version,
            ["records"] = records,
            ["diagnostics"] = Diagnostics(document.Diagnostics),
        };
    }

    public static JsonArray Diagnostics(IEnumerable<AbfDiagnostic> diagnostics)
    {
        var array = new JsonArray();
        foreach (var diagnostic in diagnostics)
        {
            var item = new JsonObject
            {
                ["line"] = diagnostic.Line,
                ["code"] = diagnostic.Code,
                ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(),
            };
            if (diagnostic.Name is not null)
                item["name"] = diagnostic.Name;
            if (diagnostic.Message is not null)
                item["message"] = diagnostic.Message;
            array.Add(item);
        }

        return array;
    }

    private static JsonObject Record(AbfRecord record)
    {
        var fields = new JsonObject();
        foreach (var field in record.Fields)
        {
            fields[field.Name] = field.Values.Count == 1
                ? JsonValue.Create(field.Values[0].Value)
                : new JsonArray(field.Values.Select(v => (JsonNode?)JsonValue.Create(v.Value)).ToArray());
        }

        var rows = new JsonArray();
        foreach (var row in record.Rows)
        {
            rows.Add(row.Columns is null
                ? JsonValue.Create(row.Text)
                : new JsonArray(row.Columns.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()));
        }

        return new JsonObject
        {
            ["type"] = record.Type,
            ["id"] = record.Id,
            ["fields"] = fields,
            ["rows"] = rows,
        };
    }
}
