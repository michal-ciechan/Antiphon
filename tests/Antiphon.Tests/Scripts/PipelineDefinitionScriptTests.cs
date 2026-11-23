using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The pipeline-definition command (Antiphon.Nightly.PipelineDefinitionCli, which replaced scripts/pipeline-definition.ps1 in
/// unit 24 of the 2026-11-22 plan) in process, on a recording transport: each verb's route and body, the stages file sent as
/// the revision, and the refusals before any request. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class PipelineDefinitionScriptTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string Board = "22222222-2222-2222-2222-222222222222";
    private const string Project = "33333333-3333-3333-3333-333333333333";

    [Test]
    public void List_gets_the_collection()
    {
        var http = Stub();
        var run = Run(http, "list");
        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("GET", "/api/pipeline-definitions"));
        using var printed = JsonDocument.Parse(run.Output);
        printed.RootElement.GetProperty("id").GetString().ShouldBe(Id);

        Run(http, "list", "--include-archived").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/pipeline-definitions?includeArchived=true");
    }

    [Test]
    public void Get_gets_by_id()
    {
        var http = Stub();
        var run = Run(http, "get", "--id", Id);
        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("GET", $"/api/pipeline-definitions/{Id}"));
    }

    [Test]
    public void Clone_posts_clone_with_name()
    {
        var http = Stub();
        var run = Run(http, "clone", "--id", Id, "--name", "Mine");
        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("POST", $"/api/pipeline-definitions/{Id}/clone"));
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("name").GetString().ShouldBe("Mine");
    }

    [Test]
    public async Task Revise_posts_the_stages_file_as_the_revision_body()
    {
        var http = Stub();
        var file = Path.GetTempFileName();
        try
        {
            const string stages = "[{\"role\":\"Code\",\"bundleKey\":\"stage-code\",\"allowedNext\":[\"review\"]}]";
            await File.WriteAllTextAsync(file, stages);
            var run = Run(http, "revise", "--id", Id, "--stages-file", file, "--note", "tighten");
            run.Exit.ShouldBe(0, run.All);
            http.Calls.Single().Path.ShouldBe($"/api/pipeline-definitions/{Id}/revisions");
            using var body = JsonDocument.Parse(http.Calls.Single().Body!);
            body.RootElement.GetProperty("stages").GetRawText().ShouldBe(stages);
            body.RootElement.GetProperty("changeNote").GetString().ShouldBe("tighten");

            Run(http, "revise", "--id", Id, "--stages-file", file).Exit.ShouldBe(0);
            using var noNote = JsonDocument.Parse(http.Calls[^1].Body!);
            noNote.RootElement.GetProperty("changeNote").ValueKind.ShouldBe(JsonValueKind.Null);

            await File.WriteAllTextAsync(file, """{"role":"Code"}""");
            var notArray = Run(http, "revise", "--id", Id, "--stages-file", file);
            notArray.Exit.ShouldBe(1);
            notArray.Error.Trim().ShouldBe("Stages file must contain a JSON array.");
            var missing = Run(http, "revise", "--id", Id, "--stages-file", file + ".missing");
            missing.Exit.ShouldBe(1);
            missing.Error.Trim().ShouldBe("Stages file does not exist: " + file + ".missing");
            http.Calls.Count.ShouldBe(2, "a refused stages file sends nothing");
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void SetBoard_puts_the_pointer()
    {
        var http = Stub();
        var run = Run(http, "set-board", "--board", Board, "--id", Id);
        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("PUT", $"/api/boards/{Board}/pipeline"));
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("pipelineDefinitionId").GetString().ShouldBe(Id);
    }

    [Test]
    public void SetProject_puts_the_pointer()
    {
        var http = Stub();
        var run = Run(http, "set-project", "--project", Project, "--id", Id);
        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("PUT", $"/api/projects/{Project}/pipeline"));
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("pipelineDefinitionId").GetString().ShouldBe(Id);
    }

    [Test]
    public void SetBoard_Inherit_puts_null()
    {
        var http = Stub();
        var run = Run(http, "set-board", "--board", Board, "--inherit");
        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("PUT", $"/api/boards/{Board}/pipeline"));
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("pipelineDefinitionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error_and_a_refusal_is_printed()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "PipelineDefinitionCli.cs")).ShouldAllBe(b => b < 128);
        Should.Throw<NightlyUsageException>(() => PipelineDefinitionCliOptions.Parse(["get"], null, null)).Message.ShouldBe("--id is required for get");
        Should.Throw<NightlyUsageException>(() => PipelineDefinitionCliOptions.Parse(["clone", "--id", Id], null, null)).Message.ShouldBe("--name is required for clone");
        Should.Throw<NightlyUsageException>(() => PipelineDefinitionCliOptions.Parse(["get", "--id", Id, "--inherit"], null, null)).Message.ShouldBe("--inherit does not apply to get");
        Should.Throw<NightlyUsageException>(() => PipelineDefinitionCliOptions.Parse(["set-project", "--project", Project], null, null)).Message.ShouldBe("--id is required for set-project");
        Should.Throw<NightlyUsageException>(() => PipelineDefinitionCliOptions.Parse(["set-board", "--board", Board, "--id", Id, "--inherit"], null, null)).Message
            .ShouldBe("give --id or --inherit, not both");
        Should.Throw<NightlyUsageException>(() => PipelineDefinitionCliOptions.Parse([], null, null));

        var refused = new RecordingOperatorHttp(_ => new(409, """{"detail":"The definition is archived.","code":"pipeline_definition_archived"}"""));
        var run = Run(refused, "set-board", "--board", Board, "--id", Id);
        run.Exit.ShouldBe(1);
        run.Error.ShouldContain($"Antiphon PUT /api/boards/{Board}/pipeline failed: The definition is archived.");
        run.Error.ShouldContain("code pipeline_definition_archived");
    }

    private static RecordingOperatorHttp Stub() => new(_ => new(200, $$"""{"id":"{{Id}}"}"""));

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, params string[] args) =>
        BoardCommandRun.Run(host => PipelineDefinitionCli.Run(PipelineDefinitionCliOptions.Parse(args, BoardCommandRun.Api, null), host,
            new System.IO.Abstractions.FileSystem(), http.Transport));
}
