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
        var byOwner = cases.ToLookup(test => test.Project + "/" + test.Namespace + "." + test.TypeOrClass + "/" + test.Method, StringComparer.Ordinal);

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
                    var targets = entry.GetProperty("pendingOwners").EnumerateArray().SelectMany(target => byOwner[assembly + "/" + target.GetString()]);
                    if (checkCases && original.Concat(targets).Any(Classified))
                        errors.Add(name + ": deferred case is already compiled in the invariant lane");
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
}
