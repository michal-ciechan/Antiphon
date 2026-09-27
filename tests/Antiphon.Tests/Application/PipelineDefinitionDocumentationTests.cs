using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

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
        File.ReadAllText(Path.Combine(root, "docs/ops-http.md")).ShouldContain("pipeline-definitions");
        File.ReadAllText(Path.Combine(root, "docs/agent-card-lifecycle.md")).ShouldContain("CardWorkflowRunStatus");
        File.Exists(Path.Combine(root, "scripts/pipeline-definition.ps1")).ShouldBeTrue();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
