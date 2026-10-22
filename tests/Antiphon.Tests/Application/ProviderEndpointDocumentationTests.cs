using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ProviderEndpointDocumentationTests
{
    [Test]
    public void C0167_credentials_doc_names_the_store_row_and_ranks_3b_and_5b()
    {
        var doc = Read("agent-credentials.md");
        doc.ShouldContain("| **Provider endpoint setting**");
        doc.ShouldContain("| 3b | Project provider endpoint");
        doc.ShouldContain("| 5b | Agent provider endpoint");
        doc.ShouldContain("providerEndpointMode: Inherit");
        doc.ShouldContain("providerEndpoints: {}");
        doc.ShouldContain("Rotation changes the key store");
    }

    [Test]
    public void C0167_agent_kinds_doc_points_the_production_redirect_at_the_setting()
    {
        var section = Section(Read("agent-kinds.md"), "## 7.", "## 8.");
        section.ShouldContain("Production redirects use the provider endpoint setting");
        section.ShouldContain("ProviderEndpointProjection");
        section.ShouldContain("RealCliStubEnv remains the test builder");
        section.ShouldContain("B_agent_launch_projects_project_endpoint_and_hits_stub");
        section.ShouldNotContain("B-agent Codex is deferred");
    }

    [Test]
    public void C0167_docs_list_exactly_the_names_Describe_projects()
    {
        var table = Section(Read("agent-kinds.md"), "<!-- provider-endpoint-names:start -->",
            "<!-- provider-endpoint-names:end -->");
        var rows = table.Split('\n').Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray()).ToArray();
        rows.Select(row => row[1].Trim('`')).Order().ShouldBe(
            new[] { "ClaudeCode", "Codex", "Grok" });
        foreach (var row in rows)
        {
            var kind = Enum.Parse<AgentKind>(row[1].Trim('`'));
            var described = ProviderEndpointProjection.Describe(kind);
            Names(row[2]).Order().ShouldBe(described.Env.Keys.Order(), $"{kind} environment names");
            Names(row[3]).Order().ShouldBe(described.Args.Where(arg => arg != "-c")
                .Select(arg => arg.Split('=')[0]).Order(), $"{kind} argument keys");
        }
    }

    private static IEnumerable<string> Names(string cell) =>
        Regex.Matches(cell, "`([^`]+)`").Select(match => match.Groups[1].Value);

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docs", name)).ReplaceLineEndings("\n");

    private static string Section(string document, string start, string end)
    {
        var first = document.IndexOf(start, StringComparison.Ordinal);
        first.ShouldBeGreaterThanOrEqualTo(0, $"missing documentation section {start}");
        var last = document.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        last.ShouldBeGreaterThan(first, $"missing documentation boundary {end}");
        return document[first..last];
    }
}
