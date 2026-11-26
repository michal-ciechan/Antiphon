using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

[Category("RepositoryInvariant")]
[Category("Unit")]
public sealed class PipelineDefinitionDocumentationTests
{
    [Test]
    public void Docs_name_the_routes_the_script_and_the_run_status_table()
    {
        var root = RepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "docs/antiphon-api.md"));
        api.ShouldContain("GET    /api/pipeline-definitions");
        api.ShouldContain("POST   /api/pipeline-definitions/{id}/revisions");
        api.ShouldContain("PUT    /api/boards/{id}/pipeline");
        var ops = File.ReadAllText(Path.Combine(root, "docs/ops-http.md"));
        ops.ShouldContain("pipeline-definitions");
        File.ReadAllText(Path.Combine(root, "docs/agent-card-lifecycle.md")).ShouldContain("CardWorkflowRunStatus");
        // The pipeline-definition command (unit 24 of the 2026-11-22 plan) replaced scripts/pipeline-definition.ps1.
        ops.ShouldContain("scripts/antiphon pipeline-definition");
        api.ShouldContain("scripts/antiphon pipeline-definition");
        File.Exists(Path.Combine(root, "scripts/lib/Antiphon.Nightly/PipelineDefinitionCli.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "scripts/pipeline-definition.ps1")).ShouldBeFalse();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
