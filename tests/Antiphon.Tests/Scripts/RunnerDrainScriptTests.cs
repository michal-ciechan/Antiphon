using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0727 V-33. The runner-drain command (Antiphon.Nightly.RunnerDrain, which replaced scripts/runner-drain.ps1 in unit 21 of
/// the 2026-11-22 plan) in process, on a recording transport: the four verbs, the operator token sent and never printed, the
/// runner id escaped, a status that is not HTTP 200 (404 included) not eligible, and the server's error body passed on. The row
/// name is the script's text contract's; its source checks became these behaviour checks.
/// </summary>
[Category("Integration")]
public sealed class RunnerDrainScriptTests
{
    [Test]
    public async Task Script_is_ascii_offers_the_four_verbs_and_sends_the_token_without_printing_it()
    {
        foreach (var verb in new[] { "status", "drain", "clear", "retire" })
            RunnerDrainOptions.Parse([verb, "--reason", "why", "--confirm", "server2"], null, null, "token-file").Verb.ShouldBe(verb);
        Should.Throw<NightlyUsageException>(() => RunnerDrainOptions.Parse(["undrain"], null, null, "token-file"));
        Should.Throw<NightlyUsageException>(() => RunnerDrainOptions.Parse(["drain"], null, null, "token-file")).Message.ShouldBe("drain requires --reason");
        Should.Throw<NightlyUsageException>(() => RunnerDrainOptions.Parse(["retire", "--reason", "why", "--runner-id", "server2", "--confirm", "server2-temp"], null, null, "t"))
            .Message.ShouldBe("retire requires --confirm equal to --runner-id");

        using var directory = new TokenDirectory();
        var token = await directory.WriteAsync("operator-secret-" + Guid.NewGuid().ToString("N") + "\r\n");
        var http = new RecordingOperatorHttp(_ => new(200, """{"draining":true}"""));

        var drain = Run(http, directory.Path, "drain", "--runner-id", "runner one", "--redirect-to", "server2-temp", "--retire-when-idle", "--reason", "rolling");
        drain.Exit.ShouldBe(0, drain.Output);
        drain.Output.Trim().ShouldBe("""{"draining":true}""");
        var call = http.Calls.Single();
        call.Method.ShouldBe("POST");
        call.Path.ShouldBe("/api/session-runners/runner%20one/drain");
        call.Headers["X-Antiphon-Operator-Token"].ShouldBe(token.Trim());
        call.Headers["X-Antiphon-Task-Token"].ShouldBe("task-token");
        call.Headers.ShouldNotContainKey("X-Antiphon-Caller");
        using (var body = JsonDocument.Parse(call.Body!))
        {
            body.RootElement.GetProperty("reason").GetString().ShouldBe("rolling");
            body.RootElement.GetProperty("retireWhenIdle").GetBoolean().ShouldBeTrue();
            body.RootElement.GetProperty("redirectTo").GetString().ShouldBe("server2-temp");
        }

        Run(http, directory.Path, "clear", "--reason", "rolling rollback").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/session-runners/server2/drain/clear");
        http.Calls[^1].Body.ShouldBe("""{"reason":"rolling rollback"}""");

        Run(http, directory.Path, "retire", "--reason", "gone", "--runner-id", "server2-temp", "--confirm", "server2-temp").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/session-runners/server2-temp/retire");
        using (var retire = JsonDocument.Parse(http.Calls[^1].Body!))
            retire.RootElement.GetProperty("confirmRunnerId").GetString().ShouldBe("server2-temp");

        // status reads without the operator token.
        Run(http, Path.Combine(directory.Path, "missing"), "status").Exit.ShouldBe(0);
        http.Calls[^1].Method.ShouldBe("GET");
        http.Calls[^1].Path.ShouldBe("/api/session-runners/server2/status");
        http.Calls[^1].Headers.ShouldNotContainKey("X-Antiphon-Operator-Token");

        foreach (var output in new[] { drain.Output })
            output.ShouldNotContain(token.Trim());
    }

    [Test]
    [Arguments(404, "", "Runner 'server2' is not eligible (HTTP 404).")]
    [Arguments(204, "", "Runner 'server2' is not eligible (HTTP 204).")]
    [Arguments(409, """{"error":"phone_home_redirect_invalid"}""", """Runner 'server2' request failed with HTTP 409. {"error":"phone_home_redirect_invalid"}""")]
    [Arguments(500, "", "Runner 'server2' request failed with HTTP 500.")]
    public async Task A_status_other_than_200_is_not_eligible_or_failed(int status, string body, string expected)
    {
        using var directory = new TokenDirectory();
        await directory.WriteAsync("operator-secret");
        var http = new RecordingOperatorHttp(_ => new(status, body));
        var run = Run(http, directory.Path, "drain", "--reason", "why");
        run.Exit.ShouldBe(1);
        run.Output.Trim().ShouldBe(expected);
        run.Output.ShouldNotContain("operator-secret");
    }

    [Test]
    public async Task An_empty_200_prints_its_status_and_a_missing_or_empty_token_file_sends_nothing()
    {
        using var directory = new TokenDirectory();
        var http = new RecordingOperatorHttp(_ => new(200, ""));
        Run(http, directory.Path, "status").Output.Trim().ShouldBe("HTTP 200");

        http.Calls.Clear();
        var missing = Run(http, directory.Path, "drain", "--reason", "why");
        missing.Exit.ShouldBe(1);
        missing.Output.Trim().ShouldBe("Operator token file not found at " + directory.File
            + ". The server creates it at startup; run this as the account the server runs under.");
        await directory.WriteAsync("  \n");
        var empty = Run(http, directory.Path, "clear", "--reason", "why");
        empty.Exit.ShouldBe(1);
        empty.Output.Trim().ShouldBe("Operator token file at " + directory.File + " is empty.");
        http.Calls.ShouldBeEmpty();
    }

    private static (int Exit, string Output) Run(RecordingOperatorHttp http, string tokenDirectory, params string[] args)
    {
        var output = new StringWriter();
        var options = RunnerDrainOptions.Parse(args, "http://127.0.0.1:17202/", "task-token", Path.Combine(tokenDirectory, "operator-token"));
        var exit = RunnerDrain.Run(options, NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), http.Transport);
        return (exit, output.ToString());
    }

    private sealed class TokenDirectory : IDisposable
    {
        public TokenDirectory() => Path = Directory.CreateTempSubdirectory("c727-operator-").FullName;

        public string Path { get; }

        public string File => System.IO.Path.Combine(Path, "operator-token");

        public async Task<string> WriteAsync(string token)
        {
            await System.IO.File.WriteAllTextAsync(File, token);
            return token;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
