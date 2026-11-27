using System.Net;
using Antiphon.SessionRunner.Agent2Agent;
using Microsoft.AspNetCore.Http;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests.Agent2Agent;

/// <summary>Standalone A2A plan (2026-11-27): Codex JSON lines, turn arguments, settings and the access guard.</summary>
[Category("Unit")]
public class CodexExecUnitTests
{
    // Captured from codex-cli 0.172.0 on 2026-11-27 (`codex exec --json`, then `codex exec resume --json`).
    private const string Started = """{"type":"thread.started","thread_id":"01a21d07-2f88-7e43-ae73-9e82362e42a1"}""";
    private const string Message = """{"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"noted"}}""";
    private const string Done = """{"type":"turn.completed","usage":{"input_tokens":17343,"cached_input_tokens":14208,"cache_write_input_tokens":0,"output_tokens":6,"reasoning_output_tokens":0}}""";

    [Test]
    public void Parses_the_captured_turn()
    {
        CodexExecEventParser.Parse(Started).ShouldBe(new CodexExecEvent.ThreadStarted("01a21d07-2f88-7e43-ae73-9e82362e42a1"));
        CodexExecEventParser.Parse("""{"type":"turn.started"}""").ShouldBeOfType<CodexExecEvent.TurnStarted>();
        var item = CodexExecEventParser.Parse(Message).ShouldBeOfType<CodexExecEvent.ItemCompleted>();
        item.ItemType.ShouldBe("agent_message");
        item.Text.ShouldBe("noted");
        var done = CodexExecEventParser.Parse(Done).ShouldBeOfType<CodexExecEvent.TurnCompleted>();
        done.Usage!.Value.GetProperty("output_tokens").GetInt32().ShouldBe(6);
    }

    [Test]
    public void Parses_failures_and_ignores_noise()
    {
        CodexExecEventParser.Parse("""{"type":"turn.failed","error":{"message":"usage limit reached"}}""")
            .ShouldBe(new CodexExecEvent.TurnFailed("usage limit reached"));
        CodexExecEventParser.Parse("""{"type":"error","message":"stream disconnected"}""")
            .ShouldBe(new CodexExecEvent.StreamError("stream disconnected"));
        CodexExecEventParser.Parse("""{"type":"item.started","item":{"id":"i","type":"command_execution"}}""")
            .ShouldBe(new CodexExecEvent.Other("item.started"));
        CodexExecEventParser.Parse("").ShouldBeNull();
        CodexExecEventParser.Parse("Reading prompt from stdin...").ShouldBeNull();
        CodexExecEventParser.Parse("""{"no":"type"}""").ShouldBeNull();
        CodexExecEventParser.Parse("""{"type":"thread.started"}""").ShouldBeNull();
    }

    [Test]
    public void Summarizes_progress_items()
    {
        Summary("""{"type":"command_execution","command":"git status","exit_code":0,"status":"completed"}""")
            .ShouldBe("ran: git status (exit 0)");
        Summary("""{"type":"file_change","changes":[{"path":"a.cs","kind":"update"},{"path":"b.cs","kind":"add"}]}""")
            .ShouldBe("changed: update a.cs, add b.cs");
        Summary("""{"type":"mcp_tool_call","server":"docs","tool":"search"}""").ShouldBe("tool: docs.search");
        Summary("""{"type":"web_search","query":"a2a spec"}""").ShouldBe("searched: a2a spec");
        Summary("""{"type":"reasoning","text":"thinking"}""").ShouldBeNull();
    }

    [Test]
    public void Builds_new_and_resumed_turn_arguments_with_the_prompt_on_stdin()
    {
        var settings = new RunnerA2ASettings { Sandbox = "workspace-write", Model = "gpt-6.2-sol" };
        CodexExecArguments.Build(settings, new("hi", "C:/w", null)).ShouldBe(
            new[] { "exec", "--json", "--skip-git-repo-check", "-c", "sandbox_mode=\"workspace-write\"", "-m", "gpt-6.2-sol", "-" });
        CodexExecArguments.Build(new RunnerA2ASettings(), new("hi", "C:/w", "01a21d07-2f88-7e43-ae73-9e82362e42a1")).ShouldBe(
            new[] { "exec", "resume", "--json", "--skip-git-repo-check", "-c", "sandbox_mode=\"read-only\"",
                "01a21d07-2f88-7e43-ae73-9e82362e42a1", "-" });
    }

    [Test]
    public void Settings_validate_only_when_enabled()
    {
        new RunnerA2ASettings { Enabled = false, WorkspaceRoot = null }.Validate();
        Should.Throw<InvalidOperationException>(() => new RunnerA2ASettings { Enabled = true }.Validate())
            .Message.ShouldContain("WorkspaceRoot");
        var root = Path.GetTempPath();
        Should.Throw<InvalidOperationException>(() => new RunnerA2ASettings { Enabled = true, WorkspaceRoot = root, Sandbox = "yolo" }.Validate())
            .Message.ShouldContain("Sandbox");
        Should.Throw<InvalidOperationException>(() => new RunnerA2ASettings { Enabled = true, WorkspaceRoot = root, Path = "a2a" }.Validate())
            .Message.ShouldContain("Path");
        new RunnerA2ASettings { Enabled = true, WorkspaceRoot = root }.Validate();
    }

    [Test]
    public void Agent_card_advertises_the_json_rpc_interface()
    {
        var card = RunnerA2ARegistration.BuildAgentCard(new RunnerA2ASettings { WorkspaceRoot = "C:/w" }, "http://+:17291;http://localhost:1");
        card.SupportedInterfaces.Single().Url.ShouldBe("http://localhost:17291/a2a");
        card.SupportedInterfaces.Single().ProtocolBinding.ShouldBe("JSONRPC");
        card.Capabilities.Streaming.ShouldBe(true);
        RunnerA2ARegistration.BuildAgentCard(new RunnerA2ASettings { PublicUrl = "https://laptop.example/" }, null)
            .SupportedInterfaces.Single().Url.ShouldBe("https://laptop.example/a2a");
    }

    [Test]
    public async Task Guard_serves_direct_loopback_without_a_token_and_requires_the_token_when_set()
    {
        var remote = IPAddress.Parse("100.93.77.126");
        (await Guarded(IPAddress.Loopback)).ShouldBe(200);
        (await Guarded(IPAddress.IPv6Loopback, host: "[::1]:17291", rpc: false)).ShouldBe(200);
        (await Guarded(IPAddress.Loopback, host: "127.0.0.1:17291")).ShouldBe(200);
        (await Guarded(remote)).ShouldBe(403);
        (await Guarded(remote, rpc: false)).ShouldBe(403);
        // A proxy on this host connects from loopback but forwards headers or a public host name.
        (await Guarded(IPAddress.Loopback, forwarded: "X-Forwarded-For")).ShouldBe(403);
        (await Guarded(IPAddress.Loopback, forwarded: "Forwarded")).ShouldBe(403);
        (await Guarded(IPAddress.Loopback, host: "a2a.laptop.example")).ShouldBe(403);
        // A cross-site form or no-preflight fetch cannot send application/json.
        (await Guarded(IPAddress.Loopback, contentType: "text/plain")).ShouldBe(415);
        (await Guarded(IPAddress.Loopback, contentType: null)).ShouldBe(415);
        (await Guarded(IPAddress.Loopback, contentType: "application/json; charset=utf-8")).ShouldBe(200);

        const string token = "0123456789abcdef0123";
        (await Guarded(IPAddress.Loopback, token)).ShouldBe(401);
        (await Guarded(IPAddress.Loopback, token, header: "Bearer wrong-token-value-here")).ShouldBe(401);
        (await Guarded(remote, token, header: "Bearer " + token, host: "a2a.laptop.example", forwarded: "X-Forwarded-For")).ShouldBe(200);
        (await Guarded(remote, token, header: "Bearer " + token, contentType: "text/plain")).ShouldBe(415);
        (await Guarded(remote, token, rpc: false)).ShouldBe(200);
    }

    private static string? Summary(string item) =>
        CodexExecEventParser.Summarize((CodexExecEvent.ItemCompleted)CodexExecEventParser.Parse(
            $$"""{"type":"item.completed","item":{{item}}}""")!);

    private static async Task<int> Guarded(IPAddress remote, string? token = null, string? header = null, bool rpc = true,
        string host = "localhost:17291", string? contentType = "application/json", string? forwarded = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = remote;
        http.Request.Host = new HostString(host);
        http.Request.ContentType = contentType;
        if (forwarded is not null) http.Request.Headers[forwarded] = "203.0.113.9";
        if (header is not null) http.Request.Headers.Authorization = header;
        var result = await RunnerA2ARegistration.Guard(new DefaultEndpointFilterInvocationContext(http),
            _ => ValueTask.FromResult<object?>(Results.Ok()), token, rpc);
        return result is IStatusCodeHttpResult status ? status.StatusCode ?? 200 : 200;
    }
}
