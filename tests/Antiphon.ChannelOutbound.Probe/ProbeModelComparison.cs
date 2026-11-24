using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

// Whether two models are the same model as far as a probe's database work can tell: the full metadata (entity types;
// properties with their CLR type, field, nullability, shadow state, value generation, save behaviour, access mode,
// length and concurrency; keys; foreign keys with their delete behaviour; navigations; indexes; every annotation),
// the relational model (tables, columns and their store types and nullability, constraints, indexes) and each
// property's column, type mapping, converter, provider type and comparers. ProbeModelGenerator publishes a generated
// model only when this finds no difference from the runtime model; Antiphon.Tests (which compiles this file too)
// pins that each kind of difference is found.
internal static class ProbeModelComparison
{
    public static IReadOnlyList<string> Differences(IModel expected, IModel actual, int limit = 20)
    {
        var differences = new List<string>();
        Compare("model", Text(expected.ToDebugString(MetadataDebugStringOptions.LongDefault)),
            Text(actual.ToDebugString(MetadataDebugStringOptions.LongDefault)), differences, limit);
        Compare("relational", Text(expected.GetRelationalModel().ToDebugString(MetadataDebugStringOptions.LongDefault)),
            Text(actual.GetRelationalModel().ToDebugString(MetadataDebugStringOptions.LongDefault)), differences, limit);
        Compare("mapping", Mappings(expected), Mappings(actual), differences, limit);
        return differences;
    }

    private static string Text(string debug) => debug.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Mappings(IModel model)
    {
        var lines = new List<string>();
        foreach (var entity in model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
            foreach (var property in entity.GetProperties())
            {
                var mapping = property.GetTypeMapping();
                lines.Add(entity.Name + "." + property.Name + " column=" + property.GetColumnName()
                    + " mapping=" + mapping.GetType().FullName + " store=" + (mapping as RelationalTypeMapping)?.StoreType
                    + " converter=" + mapping.Converter?.GetType().FullName
                    + " provider=" + property.GetProviderClrType()?.FullName
                    + " comparer=" + property.GetValueComparer().GetType().FullName
                    + " key-comparer=" + property.GetKeyValueComparer().GetType().FullName
                    + " provider-comparer=" + property.GetProviderValueComparer().GetType().FullName
                    + Element(property, mapping));
            }
        return string.Join('\n', lines);
    }

    private static string Element(IReadOnlyProperty property, CoreTypeMapping mapping)
    {
        var element = property.GetElementType();
        var elementMapping = mapping.ElementTypeMapping;
        if (elementMapping is null && element is null) return "";
        return " element-mapping=" + elementMapping?.GetType().FullName
            + " element-store=" + (elementMapping as RelationalTypeMapping)?.StoreType
            + " element-converter=" + elementMapping?.Converter?.GetType().FullName
            + " element-comparer=" + elementMapping?.Comparer.GetType().FullName
            + " element-nullable=" + (element?.IsNullable.ToString() ?? "unknown");
    }

    private static void Compare(string part, string expected, string actual, List<string> differences, int limit)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var inActual = actualLines.ToHashSet(StringComparer.Ordinal);
        var inExpected = expectedLines.ToHashSet(StringComparer.Ordinal);
        var before = differences.Count;
        foreach (var line in expectedLines.Where(l => !inActual.Contains(l)))
            if (differences.Count < limit) differences.Add(part + " - " + line.Trim());
        foreach (var line in actualLines.Where(l => !inExpected.Contains(l)))
            if (differences.Count < limit) differences.Add(part + " + " + line.Trim());
        // The same lines in another order (or repeated) is still a different model text.
        if (differences.Count == before && differences.Count < limit) differences.Add(part + ": the same lines in a different order");
    }
}
