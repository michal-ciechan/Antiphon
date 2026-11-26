using System.Reflection;
using System.Text.Json;
using Antiphon.Nightly;

namespace Antiphon.Tests.Invariants;

internal static class RepositoryInvariantRosterAudit
{
    private const string RosterPath = "docs/superpowers/plans/2026-11-26-card-2082-candidate-roster.json";
    private const string Category = "RepositoryInvariant";

    internal static IReadOnlyList<string> Check(string root, string project, IReadOnlyList<LiveLaneNode> cases)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, RosterPath)));
        var roster = document.RootElement;
        var statuses = roster.GetProperty("implementation").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        var obligations = from section in new[] { roster, roster.GetProperty("earlierMixed") }
                          from fixture in section.GetProperty("fixtures").EnumerateArray()
                          let assembly = fixture.GetProperty("assembly").GetString()
                          let type = fixture.GetProperty("type").GetString()
                          from method in fixture.GetProperty("methods").EnumerateArray()
                          let disposition = method[4].GetString()
                          where disposition is "MOVE" or "SPLIT"
                          select (Assembly: assembly, Name: assembly + "/" + type + "/" + method[0].GetString(), Disposition: disposition);
        var errors = new List<string>();
        var byOwner = cases.ToLookup(test => test.Project + "/" + test.Namespace + "."
            + NightlyCensus.BareClassIdentity(test.TypeOrClass) + "/" + test.Method, StringComparer.Ordinal);

        foreach (var (assembly, name, disposition) in obligations)
        {
            if (!statuses.Remove(name, out var entry))
            {
                errors.Add(name + ": missing application status");
                continue;
            }
            var checkCases = assembly == project;
            var original = byOwner[name];
            switch (entry.GetProperty("status").GetString())
            {
                case "applied":
                    var owner = assembly + "/" + entry.GetProperty("owner").GetString();
                    var observed = byOwner[owner].ToArray();
                    if (checkCases && (observed.Length == 0 || observed.Any(test => !Invariant(test))))
                        errors.Add($"{name}: no compiled invariant cases at {owner}");
                    var expected = entry.GetProperty("uids").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal);
                    if (checkCases && !expected.SequenceEqual(observed.Select(test => test.Uid).Order(StringComparer.Ordinal)))
                        errors.Add($"{name}: expanded {disposition} cases disagree with the roster");
                    if (disposition == "MOVE")
                    {
                        if (checkCases && original.Any(test => !Invariant(test)))
                            errors.Add(name + ": moved case still compiles in an ordinary owner");
                    }
                    else if (checkCases && (!original.Any() || original.Any(Classified)))
                        errors.Add(name + ": compiled SPLIT needs the extracted and retained cases");
                    break;
                case "deferred":
                    if (entry.GetProperty("phase").GetString()?.EndsWith("/" + assembly, StringComparison.Ordinal) != true
                        || string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString()))
                        errors.Add(name + ": deferred disposition needs a phase and reason");
                    var owners = entry.GetProperty("pendingOwners").EnumerateArray().Select(target => target.GetString()).ToArray();
                    if (owners.Length == 0 || owners.Any(string.IsNullOrWhiteSpace))
                        errors.Add(name + ": deferred disposition needs owner candidates");
                    var bound = original.Concat(owners.SelectMany(owner => byOwner[assembly + "/" + owner])).ToArray();
                    if (checkCases && bound.Length == 0)
                        errors.Add(name + ": no compiled ordinary owner for deferred disposition");
                    if (checkCases && bound.Any(test => Classified(test) || test.Namespace.EndsWith(".Invariants", StringComparison.Ordinal)))
                        errors.Add(name + ": deferred case is already compiled in the invariant lane");
                    break;
                case "retired":
                    if (!entry.TryGetProperty("reason", out var reason) || string.IsNullOrWhiteSpace(reason.GetString()))
                        errors.Add(name + ": retired disposition needs a reason");
                    if (checkCases && original.Any())
                        errors.Add(name + ": retired case still compiles");
                    break;
                default:
                    errors.Add(name + ": unknown application status");
                    break;
            }
        }
        errors.AddRange(statuses.Keys.Select(key => key + ": status has no MOVE or SPLIT obligation"));
        return errors;
    }

    private static bool Classified(LiveLaneNode test) => test.Categories.Contains(Category, StringComparer.Ordinal);

    private static bool Invariant(LiveLaneNode test) =>
        test.Namespace.EndsWith(".Invariants", StringComparison.Ordinal) && Classified(test);

    internal static IReadOnlyList<string> CheckSplitExecutionMetadata(string root, Assembly assembly)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, RosterPath)));
        var roster = document.RootElement;
        var statuses = roster.GetProperty("implementation");
        var project = assembly.GetName().Name!;
        var errors = new List<string>();
        foreach (var section in new[] { roster, roster.GetProperty("earlierMixed") })
        foreach (var fixture in section.GetProperty("fixtures").EnumerateArray())
        {
            if (fixture.GetProperty("assembly").GetString() != project)
                continue;
            var type = fixture.GetProperty("type").GetString()!;
            foreach (var method in fixture.GetProperty("methods").EnumerateArray().Where(method => method[4].GetString() == "SPLIT"))
            {
                var original = type + "/" + method[0].GetString();
                var disposition = statuses.GetProperty(project + "/" + original);
                if (disposition.GetProperty("status").GetString() != "applied")
                    continue;
                var extracted = disposition.GetProperty("owner").GetString()!;
                var before = Method(assembly, original);
                var after = Method(assembly, extracted);
                if (before is null || after is null)
                {
                    errors.Add($"{project}/{original} -> {extracted}: compiled SPLIT method missing");
                    continue;
                }
                var missing = ExecutionAttributes(before).Except(ExecutionAttributes(after), StringComparer.Ordinal).ToArray();
                if (missing.Length > 0)
                    errors.Add($"{project}/{original} -> {extracted}: missing {string.Join(", ", missing)}");
            }
        }
        return errors;
    }

    private static MethodInfo? Method(Assembly assembly, string owner)
    {
        var slash = owner.LastIndexOf('/');
        return assembly.GetType(owner[..slash])?
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .SingleOrDefault(method => method.Name == owner[(slash + 1)..]
                && method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "TestAttribute"));
    }

    private static IEnumerable<string> ExecutionAttributes(MethodInfo method)
    {
        var attributes = method.CustomAttributes.AsEnumerable();
        for (var type = method.DeclaringType; type is not null; type = type.BaseType)
            attributes = attributes.Concat(type.CustomAttributes);
        return attributes.Where(attribute => attribute.AttributeType.Name.Split('`')[0] switch
        {
            "ParallelLimiterAttribute" or "NotInParallelAttribute" or "TimeoutAttribute" or "RetryAttribute"
                or "DependsOnAttribute" or "RunOnAttribute" or "ParallelGroupAttribute" => true,
            "CategoryAttribute" => attribute.ConstructorArguments.Any(argument => argument.Value as string == "Slow"),
            _ => false,
        }).Select(attribute => attribute.ToString());
    }
}
