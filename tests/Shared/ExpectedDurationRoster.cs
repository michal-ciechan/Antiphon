using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TestSupport;

/// <summary>
/// CARD-1842. One roster class: the class-cost row's seconds and basis, and the
/// measurement identity when the row has one.
/// </summary>
public sealed record ExpectedDurationEntry(
    double Seconds,
    string Basis,
    string? HostId,
    string? Os,
    string? MeasuredRunId);

/// <summary>
/// CARD-1842. The roster file, or the projection of the class-cost file it must equal.
/// </summary>
public sealed record ExpectedDurationRosterDocument(
    string Platform,
    IReadOnlyDictionary<string, ExpectedDurationEntry> Classes);

/// <summary>
/// CARD-1842. One [ExpectedDuration] application. Seconds is null when the argument
/// is not a single numeric literal.
/// </summary>
public sealed record ExpectedDurationApplication(string ClassName, string Location, double? Seconds);

/// <summary>
/// CARD-1842. Reads tests/Shared/expected-duration-roster.json, the projection of
/// scripts/lib/nightly-class-costs.json written by scripts/export-expected-duration-roster.ps1,
/// and checks [ExpectedDuration] applications against it. The roster is not an embedded
/// resource: a refresh must not change the test assembly.
/// </summary>
public static class ExpectedDurationRoster
{
    public const string Format = "antiphon-expected-duration-roster-v1";
    public const string CostFormat = "antiphon-nightly-class-costs-v1";
    public const string RelativePath = "tests/Shared/expected-duration-roster.json";
    public const string CostFileRelativePath = "scripts/lib/nightly-class-costs.json";

    public static ExpectedDurationRosterDocument Load(string path) => Read(path, Format, "expected-duration-roster");

    public static ExpectedDurationRosterDocument ProjectCostFile(string path) => Read(path, CostFormat, "class-cost");

    public static IReadOnlyList<string> Differences(
        ExpectedDurationRosterDocument roster, ExpectedDurationRosterDocument expected)
    {
        var differences = new List<string>();
        if (!string.Equals(roster.Platform, expected.Platform, StringComparison.Ordinal))
            differences.Add($"platform: roster '{roster.Platform}', cost file '{expected.Platform}'");
        foreach (var name in expected.Classes.Keys.Except(roster.Classes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            differences.Add($"{name}: missing from the roster");
        foreach (var name in roster.Classes.Keys.Except(expected.Classes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            differences.Add($"{name}: absent from the cost file");
        foreach (var (name, entry) in roster.Classes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (expected.Classes.TryGetValue(name, out var want) && entry != want)
                differences.Add($"{name}: roster {entry}, cost file {want}");
        }

        return differences;
    }

    public static IReadOnlyList<ExpectedDurationApplication> FindApplications(string source, string location)
    {
        var applications = new List<ExpectedDurationApplication>();
        var tree = CSharpSyntaxTree.ParseText(source);
        foreach (var attribute in tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>())
        {
            if (RightmostName(attribute.Name) is not ("ExpectedDuration" or "ExpectedDurationAttribute"))
                continue;
            var line = attribute.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            applications.Add(new ExpectedDurationApplication(
                EnclosingClassName(attribute), $"{location}:{line}", LiteralSeconds(attribute)));
        }

        return applications;
    }

    public static IReadOnlyList<ExpectedDurationApplication> FindApplications(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetTypes()
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ExpectedDurationAttribute>(inherit: false)))
            .Where(pair => pair.Attribute is not null)
            .Select(pair => new ExpectedDurationApplication(
                pair.Type.FullName ?? pair.Type.Name, assembly.GetName().Name ?? "assembly", pair.Attribute!.Seconds))
            .OrderBy(application => application.ClassName, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> Check(
        ExpectedDurationRosterDocument roster, IEnumerable<ExpectedDurationApplication> applications)
    {
        var failures = new List<string>();
        foreach (var application in applications)
        {
            if (application.Seconds is not { } seconds)
            {
                failures.Add($"{application.Location}: [ExpectedDuration] on {application.ClassName} needs one numeric literal seconds argument");
                continue;
            }

            // The packer prices from the cost file and never reads the attribute.
            if (!roster.Classes.TryGetValue(application.ClassName, out var entry))
            {
                failures.Add($"{application.Location}: {application.ClassName} carries [ExpectedDuration({Number(seconds)})] but is absent from {RelativePath}");
                continue;
            }

            if (entry.Seconds != seconds)
                failures.Add($"{application.Location}: {application.ClassName} [ExpectedDuration({Number(seconds)})] differs from roster seconds {Number(entry.Seconds)}");
        }

        return failures;
    }

    private static ExpectedDurationRosterDocument Read(string path, string format, string label)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var formatValue)
            || formatValue.ValueKind != JsonValueKind.String
            || formatValue.GetString() != format)
            throw new InvalidDataException($"{label}-format: {path}");
        if (!root.TryGetProperty("platform", out var platform) || platform.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{label}-platform: {path}");
        if (!root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label}-classes: {path}");

        var entries = new Dictionary<string, ExpectedDurationEntry>(StringComparer.Ordinal);
        foreach (var row in classes.EnumerateObject())
        {
            if (row.Value.ValueKind != JsonValueKind.Object
                || !row.Value.TryGetProperty("seconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number
                || !row.Value.TryGetProperty("basis", out var basis) || basis.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{label}-row: {row.Name}");
            entries.Add(row.Name, new ExpectedDurationEntry(
                seconds.GetDouble(),
                basis.GetString()!,
                OptionalString(row.Value, "hostId", label, row.Name),
                OptionalString(row.Value, "os", label, row.Name),
                OptionalString(row.Value, "measuredRunId", label, row.Name)));
        }

        return new ExpectedDurationRosterDocument(platform.GetString()!, entries);
    }

    private static string? OptionalString(JsonElement row, string name, string label, string className)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{label}-{name}: {className}");
        return value.GetString();
    }

    private static string RightmostName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString(),
    };

    private static string EnclosingClassName(AttributeSyntax attribute)
    {
        var types = attribute.Ancestors().OfType<BaseTypeDeclarationSyntax>()
            .Reverse().Select(type => type.Identifier.ValueText).ToList();
        var namespaces = attribute.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse().Select(ns => ns.Name.ToString()).ToList();
        var type = types.Count == 0 ? "<no type>" : string.Join("+", types);
        return namespaces.Count == 0 ? type : string.Join(".", namespaces) + "." + type;
    }

    private static double? LiteralSeconds(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is not { Arguments: [var argument] }
            || argument.NameEquals is not null
            || argument.NameColon is not null
            || argument.Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.NumericLiteralExpression))
            return null;
        return Convert.ToDouble(literal.Token.Value, CultureInfo.InvariantCulture);
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
