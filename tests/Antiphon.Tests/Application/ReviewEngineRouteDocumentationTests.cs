using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class ReviewEngineRouteDocumentationTests(AntiphonWebAppFactory factory)
{
    private const string PendingMarker = "(pending GH-56)";
    private const string PendingCloseRoute = "POST /api/review-runs/{id}/close";

    [Test]
    public void Api_maps_match_the_registered_review_engine_routes()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>();
        var registered = ReviewRoutes(endpoints.Endpoints);
        registered.ShouldNotBeEmpty();

        var docs = Path.Combine(DelegateScriptRunner.RepoRoot, "docs");
        foreach (var document in new[] { "antiphon-api.md", "features/012-review-engine/api.md" })
            AssertDocumentedRoutes(document, File.ReadLines(Path.Combine(docs, document)), registered, registered);

        // The operator guide documents three common reads and links to the full contract.
        const string ops = "ops-http.md";
        var operatorReads = new[] { "GET /api/review-runs", "GET /api/review-runs/{id}/findings", "GET /api/review-queue" };
        AssertDocumentedRoutes(ops, File.ReadLines(Path.Combine(docs, ops)), registered, operatorReads);
        File.ReadAllText(Path.Combine(docs, ops)).ShouldContain("(features/012-review-engine/api.md)");
    }

    [Test]
    public async Task Undocumented_constant_in_wrapped_group_is_reported()
    {
        await using var app = WebApplication.CreateSlimBuilder([]).Build();
        var group = app.MapGroup(
            "/api/review-fixture");
        const string path = "/{id:guid}/constant";
        group.MapGet(path, (Guid id) => id);
        // group.MapGet("/commented", () => "retired");

        var registered = ReviewRoutes(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
        registered.ShouldBe(new[] { "GET /api/review-fixture/{id}/constant" });

        const string row = "| GET | `/api/review-fixture/{id}/constant` | A registered route |";
        AssertDocumentedRoutes("fixture", [row], registered, registered);
        var failure = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", [], registered, registered));
        failure.Message.ShouldContain("Missing from fixture");
        failure.Message.ShouldContain("GET /api/review-fixture/{id}/constant");
    }

    [Test]
    public void Nonexistent_documented_route_is_reported()
    {
        const string route = "GET /api/review-me";
        var registered = new HashSet<string>(StringComparer.Ordinal) { route };
        string[] rows = ["| GET | `/api/review-me` | Viewer |", "| POST | `/api/review-me` | Unregistered method |"];

        var failure = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", rows, registered, registered));
        failure.Message.ShouldContain("Unregistered in fixture");
        failure.Message.ShouldContain("POST /api/review-me");
    }

    [Test]
    public void Only_rendered_table_rows_are_validated()
    {
        const string viewer = "| GET | `/api/review-me` | Viewer |";
        var registered = new HashSet<string>(StringComparer.Ordinal) { "GET /api/review-me" };
        var table = RouteTable(
            viewer,
            " | POST | `/api/review-me` | One leading space |",
            "  | PUT | `/api/review-me` | Two leading spaces |",
            "   | PATCH | `/api/review-me` | Three leading spaces |",
            "DELETE | /api/review-me | No outer pipes",
            "| POST | `/api/review-left\\|right` | Escaped pipe in a path |");
        var failure = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", table, registered, registered));
        failure.Message.ShouldContain("Unregistered in fixture");
        foreach (var route in new[] { "POST /api/review-me", "PUT /api/review-me", "PATCH /api/review-me",
                     "DELETE /api/review-me", "POST /api/review-left|right" })
            failure.Message.ShouldContain(route);

        string[] codeExamples =
        [
            "",
            "    | Method | Path | Description |",
            "    | --- | --- | --- |",
            "    | POST | `/api/review-indented-code` | Indented code example |",
            "",
            "```markdown",
            "| Method | Path | Description |",
            "| --- | --- | --- |",
            "| POST | `/api/review-fenced-code` | Fenced code example |",
            "```",
        ];
        AssertDocumentedRoutes("fixture", [.. RouteTable(viewer), .. codeExamples], registered, registered);
    }

    private static string[] RouteTable(params string[] rows) =>
        ["| Method | Path | Description |", "| --- | --- | --- |", .. rows];

    [Test]
    public void Only_the_unregistered_close_route_can_be_marked_pending_GH56()
    {
        const string intake = "POST /api/review-runs";
        const string intakeRow = "| POST | `/api/review-runs` | `existing` response flag (pending GH-56) |";
        const string closeRow = "| POST | `/api/review-runs/{id}/close` (pending GH-56) | Close the run |";
        var registered = new HashSet<string>(StringComparer.Ordinal) { intake };
        AssertDocumentedRoutes("fixture", [intakeRow, closeRow], registered, registered);

        const string bogusRow = "| POST | `/api/review-runs/{id}/bogus` (pending GH-56) | Unknown route |";
        var bogus = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", [intakeRow, bogusRow], registered, registered));
        bogus.Message.ShouldContain("Invalid pending route in fixture");
        bogus.Message.ShouldContain("POST /api/review-runs/{id}/bogus");

        registered.Add(PendingCloseRoute);
        var stale = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", [intakeRow, closeRow], registered, registered));
        stale.Message.ShouldContain("Invalid pending route in fixture");
        stale.Message.ShouldContain(PendingCloseRoute);
    }

    private static HashSet<string> ReviewRoutes(IEnumerable<Endpoint> endpoints)
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var path = "/" + string.Join("/", endpoint.RoutePattern.PathSegments
                .Select(segment => string.Concat(segment.Parts.Select(FormatRoutePart))));
            if (!path.StartsWith("/api/review-", StringComparison.OrdinalIgnoreCase))
                continue;

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is not { Count: > 0 })
                throw new InvalidOperationException($"Review engine route {path} has no explicit HTTP methods");

            foreach (var method in methods)
                routes.Add($"{method} {path}");
        }

        return routes;
    }

    // Docs omit inline route constraints such as :guid and :int.
    private static string FormatRoutePart(RoutePatternPart part) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        RoutePatternParameterPart { IsCatchAll: true } parameter =>
            "{" + (parameter.EncodeSlashes ? "*" : "**") + parameter.Name + "}",
        RoutePatternParameterPart { IsOptional: true } parameter => "{" + parameter.Name + "?}",
        RoutePatternParameterPart { Default: not null } parameter => $"{{{parameter.Name}={parameter.Default}}}",
        RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
        _ => throw new InvalidOperationException($"Unexpected route pattern part {part.GetType().Name}"),
    };

    private static void AssertDocumentedRoutes(
        string document, IEnumerable<string> lines, IReadOnlySet<string> registered, IEnumerable<string> required)
    {
        var rows = lines
            .Select(line => Regex.Match(line, """^\|\s*([A-Z]+)\s*\|\s*`(/api/review-[^`]+)`([^|]*)\|"""))
            .Where(match => match.Success)
            .Select(match => (Route: $"{match.Groups[1].Value} {match.Groups[2].Value}", Annotation: match.Groups[3].Value.Trim()))
            .ToArray();

        rows.Where(row => row.Annotation.Length > 0
                && (row.Annotation != PendingMarker || row.Route != PendingCloseRoute || registered.Contains(row.Route)))
            .Select(row => row.Route).Order(StringComparer.Ordinal).ShouldBeEmpty($"Invalid pending route in {document}");
        rows.GroupBy(row => row.Route).Where(group => group.Count() > 1).Select(group => group.Key)
            .Order(StringComparer.Ordinal).ShouldBeEmpty($"Duplicate route entries in {document}");

        var documented = rows.Where(row => row.Annotation.Length == 0).Select(row => row.Route).ToArray();
        required.Except(documented).Order(StringComparer.Ordinal).ShouldBeEmpty($"Missing from {document}");
        documented.Except(registered).Order(StringComparer.Ordinal).ShouldBeEmpty($"Unregistered in {document}");
    }
}
