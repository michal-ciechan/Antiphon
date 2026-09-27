using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PipelineDefinitionScriptTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string Board = "22222222-2222-2222-2222-222222222222";
    private const string Project = "33333333-3333-3333-3333-333333333333";

    [Test]
    public async Task List_gets_the_collection()
    {
        await using var stub = new Stub();
        var run = await RunAsync(stub, "list");
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("GET");
        stub.LastPath.ShouldBe("/api/pipeline-definitions");
    }

    [Test]
    public async Task Get_gets_by_id()
    {
        await using var stub = new Stub();
        var run = await RunAsync(stub, "get", "-Id", Id);
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("GET");
        stub.LastPath.ShouldBe($"/api/pipeline-definitions/{Id}");
    }

    [Test]
    public async Task Clone_posts_clone_with_name()
    {
        await using var stub = new Stub();
        var run = await RunAsync(stub, "clone", "-Id", Id, "-Name", "Mine");
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("POST");
        stub.LastPath.ShouldBe($"/api/pipeline-definitions/{Id}/clone");
        stub.LastBody!.RootElement.GetProperty("name").GetString().ShouldBe("Mine");
    }

    [Test]
    public async Task Revise_posts_the_stages_file_as_the_revision_body()
    {
        await using var stub = new Stub();
        var file = Path.GetTempFileName();
        try
        {
            const string stages = "[{\"role\":\"Code\",\"bundleKey\":\"stage-code\",\"allowedNext\":[\"review\"]}]";
            await File.WriteAllTextAsync(file, stages);
            var run = await RunAsync(stub, "revise", "-Id", Id, "-StagesFile", file, "-Note", "tighten");
            run.ExitCode.ShouldBe(0, run.Output);
            stub.LastPath.ShouldBe($"/api/pipeline-definitions/{Id}/revisions");
            stub.LastBody!.RootElement.GetProperty("stages").GetRawText().ShouldBe(stages);
            stub.LastBody.RootElement.GetProperty("changeNote").GetString().ShouldBe("tighten");
        }
        finally { File.Delete(file); }
    }

    [Test]
    public async Task SetBoard_puts_the_pointer()
    {
        await using var stub = new Stub();
        var run = await RunAsync(stub, "set-board", "-Board", Board, "-Id", Id);
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("PUT");
        stub.LastPath.ShouldBe($"/api/boards/{Board}/pipeline");
        stub.LastBody!.RootElement.GetProperty("pipelineDefinitionId").GetString().ShouldBe(Id);
    }

    [Test]
    public async Task SetProject_puts_the_pointer()
    {
        await using var stub = new Stub();
        var run = await RunAsync(stub, "set-project", "-Project", Project, "-Id", Id);
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("PUT");
        stub.LastPath.ShouldBe($"/api/projects/{Project}/pipeline");
        stub.LastBody!.RootElement.GetProperty("pipelineDefinitionId").GetString().ShouldBe(Id);
    }

    [Test]
    public async Task SetBoard_Inherit_puts_null()
    {
        await using var stub = new Stub();
        var run = await RunAsync(stub, "set-board", "-Board", Board, "-Inherit");
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("PUT");
        stub.LastPath.ShouldBe($"/api/boards/{Board}/pipeline");
        stub.LastBody!.RootElement.GetProperty("pipelineDefinitionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(Stub stub, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/pipeline-definition.ps1"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = stub.BaseUrl;
        start.Environment.Remove("ANTIPHON_TASK_TOKEN");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var output = await stdout + await stderr;
        if (process.ExitCode == 0)
            await stub.Request.WaitAsync(TimeSpan.FromSeconds(5));
        return (process.ExitCode, output);
    }

    private sealed class Stub : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        public string BaseUrl { get; }
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public JsonDocument? LastBody { get; private set; }
        public Task Request { get; }

        public Stub()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            Request = ServeAsync();
        }

        private async Task ServeAsync()
        {
            var context = await _listener.GetContextAsync();
            LastMethod = context.Request.HttpMethod;
            LastPath = context.Request.RawUrl;
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                LastBody = JsonDocument.Parse(await reader.ReadToEndAsync());
            }
            var body = Encoding.UTF8.GetBytes("{}");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }

        public ValueTask DisposeAsync()
        {
            LastBody?.Dispose();
            _listener.Close();
            return ValueTask.CompletedTask;
        }
    }
}
