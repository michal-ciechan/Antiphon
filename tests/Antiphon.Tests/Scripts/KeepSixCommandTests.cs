using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1320. The keep-six command (Antiphon.Nightly.KeepSix, which replaced scripts/keep-six.ps1 in unit 23 of the 2026-11-22
/// plan) in process, on a recording transport and a fake clock: the status read and the hold, clear and ack verbs, their routes
/// and bodies, the task token sent as a header and never printed, the local refusals before any request, and the server's
/// failure passed on. Nothing leaves the process.
/// </summary>
[Category("Unit")]
public sealed class KeepSixCommandTests
{
    private const string Agent = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void The_command_source_is_ascii_and_the_status_read_sends_the_task_token_only_when_set()
    {
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "KeepSix.cs")).ShouldAllBe(b => b < 128);
        var http = new RecordingOperatorHttp(_ => new(200, """[ { "agentId": "x", "enabled": true, "target": 6 } ]"""));

        var anonymous = Run(http, taskToken: null);
        anonymous.Exit.ShouldBe(0, anonymous.Error);
        anonymous.Output.ShouldBe("""[{"agentId":"x","enabled":true,"target":6}]""" + Environment.NewLine);
        var read = http.Calls.Single();
        read.Method.ShouldBe("GET");
        read.Path.ShouldBe("/api/orchestrators/keep-six");
        read.Body.ShouldBeNull();
        read.Headers.ShouldNotContainKey("X-Antiphon-Task-Token");

        var withToken = Run(http, taskToken: "task-secret");
        withToken.Exit.ShouldBe(0, withToken.Error);
        http.Calls[^1].Headers["X-Antiphon-Task-Token"].ShouldBe("task-secret");
        http.Calls[^1].Uri.ShouldStartWith("http://127.0.0.1:17202/api/");
        withToken.Output.ShouldNotContain("task-secret");
    }

    [Test]
    public void Hold_posts_the_trimmed_reason_and_the_utc_deadline_to_the_second()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """{"held":true}"""));
        var hold = Run(http, "task-secret", "--hold", "--agent", Agent, "--reason", "  cutover window  ", "--until", "2026-11-24T13:30:45.678+01:00");
        hold.Exit.ShouldBe(0, hold.Error);
        hold.Output.Trim().ShouldBe("""{"held":true}""");
        var call = http.Calls.Single();
        call.Method.ShouldBe("POST");
        call.Path.ShouldBe("/api/orchestrators/" + Agent + "/keep-six/hold");
        call.Headers["X-Antiphon-Task-Token"].ShouldBe("task-secret");
        call.Headers.ShouldNotContainKey("X-Antiphon-Operator-Token");
        call.Body.ShouldBe("""{"reason":"cutover window","until":"2026-11-24T12:30:45Z"}""");
        (hold.Output + hold.Error).ShouldNotContain("task-secret");

        // The agent is sent in its canonical form; seven days ahead exactly is still accepted.
        Run(http, "task-secret", "--hold", "--agent", "{" + Agent.ToUpperInvariant() + "}", "--reason", "r", "--until", "2026-11-30T12:00:00Z").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/orchestrators/" + Agent + "/keep-six/hold");
        using var body = JsonDocument.Parse(http.Calls[^1].Body!);
        body.RootElement.GetProperty("until").GetString().ShouldBe("2026-11-30T12:00:00Z");
    }

    [Test]
    public void Clear_hold_deletes_and_ack_posts_with_no_body()
    {
        var http = new RecordingOperatorHttp(call => new(200, call.Method == "DELETE" ? "" : """{"acknowledgedAt":"2026-11-23T12:00:00.1234567Z"}"""));

        var clear = Run(http, "task-secret", "--clear-hold", "--agent", Agent);
        clear.Exit.ShouldBe(0, clear.Error);
        clear.Output.Trim().ShouldBe("null", "an empty answer prints as ConvertTo-Json printed $null");
        http.Calls[^1].Method.ShouldBe("DELETE");
        http.Calls[^1].Path.ShouldBe("/api/orchestrators/" + Agent + "/keep-six/hold");
        http.Calls[^1].Body.ShouldBeNull();

        var ack = Run(http, "task-secret", "--ack", "--agent", Agent);
        ack.Exit.ShouldBe(0, ack.Error);
        ack.Output.Trim().ShouldBe("""{"acknowledgedAt":"2026-11-23T12:00:00.1234567Z"}""", "the server's text, not a re-formatted date");
        http.Calls[^1].Method.ShouldBe("POST");
        http.Calls[^1].Path.ShouldBe("/api/orchestrators/" + Agent + "/keep-six/ack");
        http.Calls[^1].Body.ShouldBeNull();
        http.Calls.ShouldAllBe(call => call.Headers["X-Antiphon-Task-Token"] == "task-secret");
    }

    [Test]
    [Arguments("", "--ack", "ANTIPHON_TASK_TOKEN is required for hold, clear, and ack.")]
    [Arguments("t", "--ack", "agent must be a GUID.")]
    [Arguments("t", "reason-blank", "reason is required and must be at most 200 characters.")]
    [Arguments("t", "reason-long", "reason is required and must be at most 200 characters.")]
    [Arguments("t", "until-text", "until must be a date and time.")]
    [Arguments("t", "until-now", "until must be in the future and at most 7 days ahead.")]
    [Arguments("t", "until-past", "until must be in the future and at most 7 days ahead.")]
    [Arguments("t", "until-far", "until must be in the future and at most 7 days ahead.")]
    public void A_refused_value_or_a_missing_task_token_sends_nothing(string token, string shape, string message)
    {
        var http = new RecordingOperatorHttp(_ => throw new InvalidOperationException("no request is sent"));
        string[] args = shape switch
        {
            "--ack" when token.Length == 0 => ["--ack", "--agent", Agent],
            "--ack" => ["--ack", "--agent", "not-a-guid"],
            "reason-blank" => ["--hold", "--agent", Agent, "--reason", "   ", "--until", "2026-11-24T00:00:00Z"],
            "reason-long" => ["--hold", "--agent", Agent, "--reason", new string('r', 201), "--until", "2026-11-24T00:00:00Z"],
            "until-text" => ["--hold", "--agent", Agent, "--reason", "r", "--until", "tomorrow"],
            "until-now" => ["--hold", "--agent", Agent, "--reason", "r", "--until", "2026-11-23T12:00:00Z"],
            "until-past" => ["--hold", "--agent", Agent, "--reason", "r", "--until", "2026-11-22T12:00:00Z"],
            _ => ["--hold", "--agent", Agent, "--reason", "r", "--until", "2026-11-30T12:00:01Z"],
        };
        var run = Run(http, token, args);
        run.Exit.ShouldBe(1);
        run.Output.ShouldBeEmpty();
        run.Error.Trim().ShouldBe(message);
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => KeepSixOptions.Parse(["--hold", "--ack", "--agent", Agent], null, null)).Message
            .ShouldBe("give one of --hold, --clear-hold and --ack, not --hold and --ack");
        Should.Throw<NightlyUsageException>(() => KeepSixOptions.Parse(["--agent", Agent], null, null)).Message
            .ShouldBe("--agent needs --hold, --clear-hold or --ack");
        Should.Throw<NightlyUsageException>(() => KeepSixOptions.Parse(["--ack"], null, null)).Message.ShouldBe("ack requires --agent");
        Should.Throw<NightlyUsageException>(() => KeepSixOptions.Parse(["--hold", "--agent", Agent, "--reason", "r"], null, null)).Message
            .ShouldBe("hold requires --until");
        Should.Throw<NightlyUsageException>(() => KeepSixOptions.Parse(["--clear-hold", "--agent", Agent, "--reason", "r"], null, null)).Message
            .ShouldBe("--reason belongs to --hold");
        Should.Throw<NightlyUsageException>(() => KeepSixOptions.Parse(["-Hold"], null, null));
        var status = KeepSixOptions.Parse([], "http://api.example/", "t");
        (status.Verb, status.Api, status.TaskToken).ShouldBe(("status", "http://api.example", "t"));
        KeepSixOptions.Parse([], null, null).Api.ShouldBe("http://localhost:17202");
    }

    [Test]
    public void A_server_refusal_or_a_failed_transport_is_one_line_and_exit_1()
    {
        var refused = new RecordingOperatorHttp(_ => new(403, """{"error":"keep_six_token_foreign"}"""));
        var run = Run(refused, "task-secret", "--ack", "--agent", Agent);
        run.Exit.ShouldBe(1);
        run.Output.ShouldBeEmpty();
        run.Error.Trim().ShouldBe("Antiphon POST /api/orchestrators/" + Agent + """/keep-six/ack failed: HTTP 403 {"error":"keep_six_token_foreign"}""");
        run.Error.ShouldNotContain("task-secret");

        var down = new RecordingOperatorHttp(_ => throw new HttpRequestException("No connection could be made (127.0.0.1:17202)"));
        var lost = Run(down, null);
        lost.Exit.ShouldBe(1);
        lost.Error.Trim().ShouldBe("Antiphon GET /api/orchestrators/keep-six failed: No connection could be made (127.0.0.1:17202)");
    }

    private static (int Exit, string Output, string Error) Run(RecordingOperatorHttp http, string? taskToken, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(Now),
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
        };
        var exit = KeepSix.Run(KeepSixOptions.Parse(args, "http://127.0.0.1:17202/", taskToken), host, http.Transport);
        return (exit, output.ToString(), error.ToString());
    }
}
