using System.Text.RegularExpressions;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Tests.TestHelpers;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<ReviewEngineRouteDocumentationTests.RouteMapWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class ReviewEngineRouteDocumentationTests(ReviewEngineRouteDocumentationTests.RouteMapWebAppFactory factory)
{
    private const string PendingMarker = "(pending GH-56)";
    private const string PendingCloseRoute = "POST /api/review-runs/{id}/close";
    private static readonly MarkdownPipeline MarkdownTables = new MarkdownPipelineBuilder()
        .UsePipeTables(new PipeTableOptions { UseHeaderForColumnCount = true })
        .Build();

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
        AssertDocumentedRoutes("fixture", RouteTable(row), registered, registered);
        var failure = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", [], registered, registered));
        failure.Message.ShouldContain("Missing from fixture");
        failure.Message.ShouldContain("GET /api/review-fixture/{id}/constant");
    }

    [Test]
    public void Nonexistent_documented_route_is_reported()
    {
        const string route = "GET /api/review-me";
        var registered = new HashSet<string>(StringComparer.Ordinal) { route };
        var rows = RouteTable("| GET | `/api/review-me` | Viewer |", "| POST | `/api/review-me` | Unregistered method |");

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
        string[] unregisteredRoutes =
        [
            "POST /api/review-me", "PUT /api/review-me", "PATCH /api/review-me",
            "DELETE /api/review-me", "POST /api/review-left|right",
        ];
        foreach (var route in unregisteredRoutes)
            failure.Message.ShouldContain(route);

        string[] codeExamples =
        [
            "    | POST | `/api/review-indented-code` | Indentation ends the table |",
            "| POST | `/api/review-outside-table` | Paragraph after indented code |",
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

    [Test]
    public void Only_the_unregistered_close_route_can_be_marked_pending_GH56()
    {
        const string intake = "POST /api/review-runs";
        const string intakeRow = "| POST | `/api/review-runs` | `existing` response flag (pending GH-56) |";
        const string closeRow = "| POST | `/api/review-runs/{id}/close` (pending GH-56) | Close the run |";
        var registered = new HashSet<string>(StringComparer.Ordinal) { intake };
        AssertDocumentedRoutes("fixture", RouteTable(intakeRow, closeRow), registered, registered);

        const string bogusRow = "| POST | `/api/review-runs/{id}/bogus` (pending GH-56) | Unknown route |";
        var bogus = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", RouteTable(intakeRow, bogusRow), registered, registered));
        bogus.Message.ShouldContain("Invalid pending route in fixture");
        bogus.Message.ShouldContain("POST /api/review-runs/{id}/bogus");

        registered.Add(PendingCloseRoute);
        var stale = Should.Throw<ShouldAssertException>(() => AssertDocumentedRoutes("fixture", RouteTable(intakeRow, closeRow), registered, registered));
        stale.Message.ShouldContain("Invalid pending route in fixture");
        stale.Message.ShouldContain(PendingCloseRoute);
    }

    private static string[] RouteTable(params string[] rows) =>
        ["| Method | Path | Description |", "| --- | --- | --- |", .. rows];

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
        var rows = ReadTableRoutes(lines.ToArray()).ToArray();

        rows.Where(row => row.Annotation.Length > 0
                && (row.Annotation != PendingMarker || row.Route != PendingCloseRoute || registered.Contains(row.Route)))
            .Select(row => row.Route).Order(StringComparer.Ordinal).ShouldBeEmpty($"Invalid pending route in {document}");
        rows.GroupBy(row => row.Route).Where(group => group.Count() > 1).Select(group => group.Key)
            .Order(StringComparer.Ordinal).ShouldBeEmpty($"Duplicate route entries in {document}");

        var documented = rows.Where(row => row.Annotation.Length == 0).Select(row => row.Route).ToArray();
        required.Except(documented).Order(StringComparer.Ordinal).ShouldBeEmpty($"Missing from {document}");
        documented.Except(registered).Order(StringComparer.Ordinal).ShouldBeEmpty($"Unregistered in {document}");
    }

    private static IEnumerable<(string Route, string Annotation)> ReadTableRoutes(string[] lines)
    {
        var markdown = string.Join('\n', lines);
        foreach (var table in Markdown.Parse(markdown, MarkdownTables).Descendants<Table>())
        {
            foreach (var row in table.OfType<TableRow>().Where(row => !row.IsHeader))
            {
                // GFM ends a top-level table at an indented code block. Markdig can absorb it as rows.
                if (table.Parent is MarkdownDocument && Regex.IsMatch(lines[row.Line], @"^(?: {4}| {0,3}\t)"))
                    break;

                if (row.Count < 2)
                    continue;

                var method = CellText((TableCell)row[0], markdown).Trim('`');
                if (method.Length == 0 || !method.All(char.IsAsciiLetterUpper))
                    continue;

                var pathCell = CellText((TableCell)row[1], markdown);
                var match = Regex.Match(pathCell, """^(?:`(?<path>/api/review-[^`]+)`|(?<path>/api/review-\S+))(?<annotation>.*)$""");
                if (match.Success)
                    yield return ($"{method} {match.Groups["path"].Value}", match.Groups["annotation"].Value.Trim());
            }
        }
    }

    private static string CellText(TableCell cell, string markdown)
    {
        var inlines = cell.OfType<ParagraphBlock>().FirstOrDefault()?.Inline;
        return inlines is null ? string.Empty : string.Concat(inlines.Select(inline => inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => $"`{code.Content.Replace(@"\|", "|", StringComparison.Ordinal)}`",
            _ => markdown.Substring(inline.Span.Start, inline.Span.Length),
        })).Trim();
    }

    public sealed class RouteMapWebAppFactory : AntiphonWebAppFactory
    {
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            var messaging = new FakeAntiphonMessagingClient();
            services.Replace(ServiceDescriptor.Singleton<IAntiphonMessagingProducer>(messaging));
            services.Replace(ServiceDescriptor.Singleton<IAntiphonMessagingConsumer>(messaging));
        }
    }
}
