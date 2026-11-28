using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using A2A;
using Antiphon.SessionRunner.Agent2Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests.Agent2Agent;

/// <summary>
/// Standalone A2A plan (2026-11-27): the runner's A2A endpoint through the SDK client, over the production registration
/// and route mapping, with a fake Codex process. No codex, no network beyond loopback.
/// </summary>
[Category("Unit")]
public class CodexA2AEndpointTests
{
    private const string Thread = "01a21d07-2f88-7e43-ae73-9e82362e42a1";

    [Test]
    public async Task First_turn_completes_with_the_answer_and_the_next_turn_resumes_the_thread()
    {
        await using var host = await Host.StartAsync();
        host.Launcher.Script(Turn("noted"), Turn("heron"));

        var first = await host.Client.SendMessageAsync(Send("Remember heron."));
        var task = first.Task.ShouldNotBeNull();
        task.Status.State.ShouldBe(TaskState.Completed);
        ArtifactText(task).ShouldBe("noted");
        var metadata = task.Artifacts!.Single().Metadata.ShouldNotBeNull();
        metadata[CodexA2AAgent.ThreadIdKey].GetString().ShouldBe(Thread);
        metadata[CodexA2AAgent.UsageKey].GetProperty("output_tokens").GetInt32().ShouldBe(6);

        var second = await host.Client.SendMessageAsync(Send("Which word?", task.ContextId));
        second.Task!.Status.State.ShouldBe(TaskState.Completed);
        ArtifactText(second.Task).ShouldBe("heron");

        var starts = host.Launcher.Starts.ToArray();
        starts.Length.ShouldBe(2);
        starts[0].Arguments.ShouldNotContain("resume");
        starts[0].Stdin.ShouldBe("Remember heron.");
        starts[0].WorkingDirectory.ShouldBe(host.Root);
        starts[1].Arguments.Take(2).ShouldBe(new[] { "exec", "resume" });
        starts[1].Arguments.ShouldContain(Thread);
        starts[1].Stdin.ShouldBe("Which word?");
    }

    [Test]
    public async Task Streaming_reports_progress_items_and_ends_with_the_final_answer()
    {
        await using var host = await Host.StartAsync();
        host.Launcher.Script(new FakeTurn([
            Started(),
            Item("""{"id":"a","type":"agent_message","text":"Checking the tree."}"""),
            Item("""{"id":"b","type":"command_execution","command":"git status","exit_code":0,"status":"completed"}"""),
            Item("""{"id":"c","type":"agent_message","text":"Clean."}"""),
            Completed(),
        ]));

        var progress = new List<string>();
        string? artifact = null;
        TaskState? final = null;
        await foreach (var update in host.Client.SendStreamingMessageAsync(Send("Is the tree clean?")))
        {
            if (update.StatusUpdate is { } status)
            {
                final = status.Status.State;
                if (status.Status.Message?.Parts.FirstOrDefault()?.Text is { } text) progress.Add(text);
            }
            if (update.ArtifactUpdate is { } piece) artifact = piece.Artifact.Parts.Single().Text;
        }

        progress.ShouldContain("Checking the tree.");
        progress.ShouldContain("ran: git status (exit 0)");
        progress.ShouldNotContain("Clean.");
        artifact.ShouldBe("Clean.");
        final.ShouldBe(TaskState.Completed);
    }

    [Test]
    public async Task Failed_turns_report_the_reason()
    {
        await using var host = await Host.StartAsync();
        host.Launcher.Script(
            new FakeTurn([Started(), """{"type":"turn.failed","error":{"message":"usage limit reached"}}"""], ExitCode: 1),
            new FakeTurn([Started()], ExitCode: 2, Stderr: "auth expired"));

        var failed = await host.Client.SendMessageAsync(Send("one"));
        failed.Task!.Status.State.ShouldBe(TaskState.Failed);
        failed.Task.Status.Message!.Parts.Single().Text.ShouldBe("usage limit reached");
        failed.Task.Status.Message.Metadata![CodexA2AAgent.ThreadIdKey].GetString().ShouldBe(Thread);

        var crashed = await host.Client.SendMessageAsync(Send("two"));
        crashed.Task!.Status.State.ShouldBe(TaskState.Failed);
        var reason = crashed.Task.Status.Message!.Parts.Single().Text!;
        reason.ShouldContain("exited 2 without completing the turn");
        reason.ShouldContain("auth expired");
    }

    [Test]
    public async Task Cancel_kills_the_turn_and_a_busy_context_rejects_a_second_turn()
    {
        await using var host = await Host.StartAsync();
        var blocking = new FakeTurn([Started()], Block: true);
        host.Launcher.Script(blocking);

        var started = await host.Client.SendMessageAsync(Send("long job", configuration: new SendMessageConfiguration { ReturnImmediately = true }));
        var task = started.Task.ShouldNotBeNull();
        await blocking.Running.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var busy = await host.Client.SendMessageAsync(Send("meanwhile", task.ContextId));
        busy.Task!.Status.State.ShouldBe(TaskState.Rejected);

        var canceled = await host.Client.CancelTaskAsync(new CancelTaskRequest { Id = task.Id });
        canceled.Status.State.ShouldBe(TaskState.Canceled);
        blocking.Killed.ShouldBeTrue();
        host.Launcher.Starts.Count.ShouldBe(1);
    }

    [Test]
    public async Task Cancel_stops_a_blocking_send_mid_turn()
    {
        await using var host = await Host.StartAsync();
        var blocking = new FakeTurn([Started()], Block: true);
        host.Launcher.Script(blocking);
        var context = Guid.NewGuid().ToString("N");

        var send = host.Client.SendMessageAsync(Send("long job", context));
        await blocking.Running.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var running = (await host.Client.ListTasksAsync(new ListTasksRequest { ContextId = context })).Tasks.ShouldHaveSingleItem();

        var canceled = await host.Client.CancelTaskAsync(new CancelTaskRequest { Id = running.Id }).WaitAsync(TimeSpan.FromSeconds(10));
        canceled.Status.State.ShouldBe(TaskState.Canceled);
        blocking.Killed.ShouldBeTrue();
        (await send.WaitAsync(TimeSpan.FromSeconds(10))).Task!.Id.ShouldBe(running.Id);
    }

    [Test]
    public async Task A_client_that_disconnects_mid_turn_leaves_the_task_canceled_not_working()
    {
        await using var host = await Host.StartAsync();
        var blocking = new FakeTurn([Started()], Block: true);
        host.Launcher.Script(blocking);
        var context = Guid.NewGuid().ToString("N");

        using var gone = new CancellationTokenSource();
        var send = host.Client.SendMessageAsync(Send("long job", context), gone.Token);
        await blocking.Running.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await gone.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(send);

        AgentTask? task = null;
        for (var attempt = 0; attempt < 100 && task?.Status.State != TaskState.Canceled; attempt++)
        {
            task = (await host.Client.ListTasksAsync(new ListTasksRequest { ContextId = context })).Tasks.SingleOrDefault();
            if (task?.Status.State != TaskState.Canceled) await Task.Delay(50);
        }
        task!.Status.State.ShouldBe(TaskState.Canceled);
        blocking.Killed.ShouldBeTrue();
    }

    [Test]
    public async Task The_turn_ends_at_turn_completed_even_if_a_leftover_child_keeps_stdout_open()
    {
        await using var host = await Host.StartAsync();
        host.Launcher.Script(Turn("done") with { HoldStdoutOpen = true });

        var reply = await host.Client.SendMessageAsync(Send("start a server")).WaitAsync(TimeSpan.FromSeconds(10));
        reply.Task!.Status.State.ShouldBe(TaskState.Completed);
        ArtifactText(reply.Task).ShouldBe("done");
    }

    [Test]
    public async Task Thread_attach_is_refused_unless_enabled()
    {
        await using var host = await Host.StartAsync();
        var refused = await host.Client.SendMessageAsync(Send("x", metadata: new() { [CodexA2AAgent.ThreadIdKey] = Json(Thread) }));
        refused.Task!.Status.State.ShouldBe(TaskState.Rejected);
        refused.Task.Status.Message!.Parts.Single().Text.ShouldContain("AllowThreadAttach");
        host.Launcher.Starts.ShouldBeEmpty();
    }

    [Test]
    public async Task Idle_contexts_beyond_the_cap_are_forgotten()
    {
        await using var host = await Host.StartAsync(new() { ["SessionRunner:A2A:MaxContexts"] = "2" });
        host.Launcher.Script(Turn("a"), Turn("b"), Turn("c"));
        foreach (var text in new[] { "a", "b", "c" })
            (await host.Client.SendMessageAsync(Send(text))).Task!.Status.State.ShouldBe(TaskState.Completed);
        host.Agent.ContextCount.ShouldBe(2);
    }

    [Test]
    public async Task Context_binding_takes_a_working_directory_under_the_root_and_an_existing_thread()
    {
        await using var host = await Host.StartAsync(new() { ["SessionRunner:A2A:AllowThreadAttach"] = "true" });
        var sub = Directory.CreateDirectory(Path.Combine(host.Root, "repo")).FullName;
        host.Launcher.Script(Turn("ok"));

        var outside = await host.Client.SendMessageAsync(Send("x", metadata: new() { [CodexA2AAgent.CwdKey] = Json(Path.GetTempPath()) }));
        outside.Task!.Status.State.ShouldBe(TaskState.Rejected);

        var bound = await host.Client.SendMessageAsync(Send("x", metadata: new()
        {
            [CodexA2AAgent.CwdKey] = Json("repo"),
            [CodexA2AAgent.ThreadIdKey] = Json(Thread),
        }));
        bound.Task!.Status.State.ShouldBe(TaskState.Completed);
        var start = host.Launcher.Starts.Single();
        start.WorkingDirectory.ShouldBe(sub);
        start.Arguments.ShouldContain("resume");
        start.Arguments.ShouldContain(Thread);
    }

    [Test]
    public async Task Agent_card_is_served_at_the_well_known_path()
    {
        await using var host = await Host.StartAsync();
        var card = await new A2ACardResolver(host.BaseAddress, host.Http).GetAgentCardAsync();
        card.Name.ShouldBe("Antiphon SessionRunner (Codex)");
        card.SupportedInterfaces.Single().Url.ShouldEndWith("/a2a");
        card.Skills.Single().Id.ShouldBe("codex");
    }

    private static SendMessageRequest Send(string text, string? contextId = null,
        Dictionary<string, JsonElement>? metadata = null, SendMessageConfiguration? configuration = null) => new()
    {
        Message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = contextId,
            Parts = [Part.FromText(text)],
            Metadata = metadata,
        },
        Configuration = configuration,
    };

    private static string? ArtifactText(AgentTask task) => task.Artifacts.ShouldHaveSingleItem().Parts.Single().Text;
    private static JsonElement Json(string value) => JsonSerializer.SerializeToElement(value);
    private static string Started() => $$"""{"type":"thread.started","thread_id":"{{Thread}}"}""";
    private static string Item(string item) => $$"""{"type":"item.completed","item":{{item}}}""";
    private static string Completed() => """{"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":6}}""";
    private static FakeTurn Turn(string answer) => new([Started(), """{"type":"turn.started"}""",
        Item(JsonSerializer.Serialize(new { id = "item_0", type = "agent_message", text = answer })), Completed()]);

    /// <summary>
    /// Block: stdout stays open and the process runs until killed. HoldStdoutOpen: the process exits but stdout never
    /// reaches EOF, like a pipe a leftover child still holds.
    /// </summary>
    internal sealed record FakeTurn(string[] Lines, int ExitCode = 0, string Stderr = "", bool Block = false,
        bool HoldStdoutOpen = false)
    {
        public TaskCompletionSource Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Killed { get; set; }
    }

    internal sealed class FakeLauncher : ICodexTurnLauncher
    {
        private readonly ConcurrentQueue<FakeTurn> _turns = new();
        public ConcurrentQueue<CodexTurnStartInfo> Starts { get; } = new();

        public void Script(params FakeTurn[] turns)
        {
            foreach (var turn in turns) _turns.Enqueue(turn);
        }

        public ICodexTurnProcess Start(CodexTurnStartInfo info)
        {
            Starts.Enqueue(info);
            if (!_turns.TryDequeue(out var turn)) throw new InvalidOperationException("No scripted turn left.");
            return new FakeProcess(turn);
        }
    }

    private sealed class FakeProcess : ICodexTurnProcess
    {
        private readonly FakeTurn _turn;
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProcess(FakeTurn turn)
        {
            _turn = turn;
            foreach (var line in turn.Lines) _lines.Writer.TryWrite(line);
            if (!turn.Block)
            {
                if (!turn.HoldStdoutOpen) _lines.Writer.TryComplete();
                _exit.TrySetResult();
            }
            StandardOutput = new PipeLikeReader(_lines.Reader, turn);
            StandardErrorTail = _exit.Task.ContinueWith(_ => turn.Stderr, TaskScheduler.Default);
        }

        public TextReader StandardOutput { get; }
        public Task<string> StandardErrorTail { get; }
        public int ExitCode => _turn.Killed ? -1 : _turn.ExitCode;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

        public void KillTree()
        {
            if (_exit.Task.IsCompleted) return;
            _turn.Killed = true;
            _lines.Writer.TryComplete();
            _exit.TrySetResult();
        }

        public void Dispose() { }
    }

    /// <summary>Like a pipe: ReadLineAsync ignores the token and returns only on a line or EOF.</summary>
    private sealed class PipeLikeReader(ChannelReader<string> lines, FakeTurn turn) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (lines.TryRead(out var line)) return line;
            turn.Running.TrySetResult();
            while (await lines.WaitToReadAsync(CancellationToken.None))
                if (lines.TryRead(out line)) return line;
            return null;
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly DirectoryInfo _root;

        private Host(WebApplication app, DirectoryInfo root, FakeLauncher launcher, Uri baseAddress)
        {
            _app = app;
            _root = root;
            Launcher = launcher;
            BaseAddress = baseAddress;
            Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            Client = new A2AClient(new Uri(baseAddress, "a2a"), Http);
        }

        public FakeLauncher Launcher { get; }
        public Uri BaseAddress { get; }
        public HttpClient Http { get; }
        public A2AClient Client { get; }
        public string Root => _root.FullName;

        public CodexA2AAgent Agent => _app.Services.GetRequiredService<CodexA2AAgent>();

        public static async Task<Host> StartAsync(Dictionary<string, string?>? extra = null)
        {
            var root = Directory.CreateTempSubdirectory("antiphon-a2a-test-");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var settings = new Dictionary<string, string?>
            {
                ["SessionRunner:A2A:Enabled"] = "true",
                ["SessionRunner:A2A:WorkspaceRoot"] = root.FullName,
                ["SessionRunner:A2A:PublicUrl"] = "http://127.0.0.1",
            };
            foreach (var (key, value) in extra ?? []) settings[key] = value;
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddRunnerA2A(builder.Configuration);
            var launcher = new FakeLauncher();
            builder.Services.AddSingleton<ICodexTurnLauncher>(launcher);
            var app = builder.Build();
            app.MapRunnerA2A();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            new Uri(address).Port.ShouldNotBe(17204);
            return new Host(app, root, launcher, new Uri(address.TrimEnd('/') + "/"));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            try { _root.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
