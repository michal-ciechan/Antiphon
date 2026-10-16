using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PipelineHandoffRouteDocumentationTests
{
    [Test]
    [Arguments("antiphon-api.md")]
    [Arguments("ops-http.md")]
    public void Route_map_names_each_live_handoff_and_correction_route(string document)
    {
        var root = RepositoryRoot();
        var lines = File.ReadAllLines(Path.Combine(root, "docs", document));
        foreach (var endpoint in new[] { "PipelineHandoffActionEndpoints.cs", "ReviewEvidenceCorrectionEndpoints.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, "server", "Api", "Endpoints", endpoint));
            var routes = Regex.Matches(source, "app\\.Map(Get|Post)\\(\"([^\"]+)\"");
            routes.Count.ShouldBeGreaterThan(0, endpoint);
            foreach (Match route in routes)
            {
                var method = route.Groups[1].Value.ToUpperInvariant();
                var path = Regex.Replace(route.Groups[2].Value, "\\{(?:predecessorId|reviewId):guid\\}", "{id}")
                    .Replace("{actionId:guid}", "{actionId}");
                lines.Any(line => line.Contains(method, StringComparison.Ordinal)
                    && line.Contains(path, StringComparison.Ordinal)).ShouldBeTrue($"{document}: {method} {path}");
            }
        }
    }

    [Test]
    [Arguments("antiphon-api.md")]
    [Arguments("ops-http.md")]
    public void Docs_preserve_request_recovery_and_refusal_coordinates(string document)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", document));
        foreach (var required in new[]
        {
            "expectedObservationSequence", "settlementEventId", "acceptSpend", "requestedWithinFiveMinutes",
            "predecessorEvidenceId", "expectedReportDigest", "producerIdentity", "bundleDigest",
            "successorEvidenceId", "missingRows", "already-corrected",
            "handoff_observation_changed", "handoff_foreign_receipt", "handoff_request_changed",
            "handoff_not_eligible", "handoff_trigger_held", "handoff_override_held",
            "review_evidence_correction_commissioned_selection_mismatch",
            "review_evidence_correction_receipt_invalid", "review_evidence_correction_retained_evidence_unavailable",
            "card_admission_changed", "card_implementation_closed",
        }) text.ShouldContain(required, document);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
