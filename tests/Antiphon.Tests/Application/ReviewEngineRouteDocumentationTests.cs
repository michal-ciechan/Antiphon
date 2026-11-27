using System.Text.RegularExpressions;
using Antiphon.Server.Api.Endpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ReviewEngineRouteDocumentationTests
{
    [Test]
    public void Api_maps_match_the_registered_review_engine_routes()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var registered = RegisteredRoutes(Path.Combine(root, "server", "Api", "Endpoints"))
            .ToHashSet(StringComparer.Ordinal);
        registered.ShouldNotBeEmpty();

        foreach (var document in new[] { "antiphon-api.md", "features/012-review-engine/api.md" })
        {
            var documented = DocumentedRoutes(Path.Combine(root, "docs", document));
            registered.Except(documented).Order(StringComparer.Ordinal).ShouldBeEmpty($"Missing from {document}");
            documented.Except(registered).Order(StringComparer.Ordinal).ShouldBeEmpty($"Unregistered in {document}");
        }

        var ops = Path.Combine(root, "docs", "ops-http.md");
        var operatorReads = new[] { "GET /api/review-runs", "GET /api/review-runs/{id}/findings", "GET /api/review-queue" };
        operatorReads.Except(DocumentedRoutes(ops)).ShouldBeEmpty("Missing operator reads from ops-http.md");
        File.ReadAllText(ops).ShouldContain("(features/012-review-engine/api.md)");
    }

    private static IEnumerable<string> RegisteredRoutes(string directory)
    {
        foreach (var file in new[] { "ReviewRunEndpoints.cs", "ReviewAuthEndpoints.cs" })
        {
            var source = File.ReadAllText(Path.Combine(directory, file));
            var groups = Regex.Matches(source, """var\s+(\w+)\s*=\s*app\.MapGroup\((?:"([^"]+)"|(Prefix))\)""")
                .ToDictionary(match => match.Groups[1].Value,
                    match => match.Groups[3].Success ? ReviewAuthEndpoints.Prefix : match.Groups[2].Value);
            foreach (Match match in Regex.Matches(source, """\b(\w+)\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]+)""""))
            {
                var prefix = groups.GetValueOrDefault(match.Groups[1].Value);
                var path = (prefix + match.Groups[3].Value).TrimEnd('/');
                if (!path.StartsWith("/api/review-", StringComparison.Ordinal))
                    continue;

                path = Regex.Replace(path, """\{(\w+):[^}]+\}""", "{$1}");
                yield return $"{match.Groups[2].Value.ToUpperInvariant()} {path}";
            }
        }
    }

    private static IEnumerable<string> DocumentedRoutes(string path) => File.ReadLines(path)
        .Select(line => Regex.Match(line, """^\|\s*(GET|POST|PUT|PATCH|DELETE)\s*\|\s*`(/api/review-[^`]+)`\s*\|"""))
        .Where(match => match.Success)
        .Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}");
}
