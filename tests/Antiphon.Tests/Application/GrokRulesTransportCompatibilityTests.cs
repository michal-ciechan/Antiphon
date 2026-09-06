using System.Net;
using System.Net.Http.Json;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class GrokRulesTransportCompatibilityTests
{
    [Test]
    [Arguments("cr")]
    [Arguments("lf")]
    [Arguments("crlf")]
    [Arguments("nul")]
    [Arguments("alias")]
    [Arguments("equals")]
    [Arguments("missing")]
    [Arguments("duplicate_first")]
    [Arguments("duplicate_second")]
    [Arguments("env")]
    [Arguments("braced_env")]
    [Arguments("env_flag")]
    public async Task Unsafe_raw_rules_are_refused_server_side_before_runner_calls(string variant)
    {
        foreach (var backend in new[] { SessionBackend.PtyHost, SessionBackend.Herdr })
        {
            if (variant.Contains("env") && backend != SessionBackend.Herdr) continue;
            var args = variant switch {
                "cr" => new[] { "--rules", "private\rsentinel" },
                "crlf" => ["--rules", "private\r\nsentinel"],
                "nul" => ["--rules", "private\0sentinel"],
                "alias" => ["--append-system-prompt", "private\nsentinel"],
                "equals" => ["--rules=private\nsentinel"],
                "missing" => ["--rules"],
                "duplicate_first" => ["--rules", "private\nsentinel", "--rules", "safe"],
                "duplicate_second" => ["--rules", "safe", "--rules", "private\nsentinel"],
                "env" => ["--rules", "$env:RULES"],
                "braced_env" => ["--rules", "${env:RULES}"],
                "env_flag" => ["$env:FLAG", "private\nsentinel"],
                _ => ["--rules", "private\nsentinel"] };
            var handler = new RunnerSpy("valid");
            var spec = Spec() with { Args = args, GrokRulesPayload = null, Backend = backend,
                Env = new Dictionary<string,string> { ["RULES"] = "private\nsentinel", ["FLAG"] = "--rules" } };
            Exception? failure = null;
            try { await Client(handler).StartAsync(Guid.NewGuid(), spec, CancellationToken.None); }
            catch (Exception ex) { failure = ex; }
            handler.Starts.ShouldBe(0, $"{variant}/{backend}: downstream rejection must not mask a missing server guard");
            failure.ShouldBeOfType<ConflictException>().Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
            failure.Message.ShouldNotContain("sentinel");
        }
    }

    [Test]
    [Arguments("404")]
    [Arguments("null")]
    [Arguments("missing")]
    public async Task Missing_capability_refuses_before_sending_a_payload_to_an_old_runner(string variant)
    {
        var handler = new RunnerSpy(variant);
        var client = Client(handler);
        Exception? failure = null;
        try { await client.StartAsync(Guid.NewGuid(), Spec(), CancellationToken.None); }
        catch (Exception ex) { failure = ex; }
        handler.Starts.ShouldBe(0, "an old runner would silently ignore unknown payload JSON");
        failure.ShouldBeOfType<ConflictException>().Code.ShouldBe("grok_rules_transport_unsupported");
    }

    [Test]
    [Arguments("nul")]
    [Arguments("unresolved_key")]
    [Arguments("too_large")]
    [Arguments("invalid_unicode")]
    [Arguments("ClaudeCode")]
    [Arguments("Codex")]
    [Arguments("Raw")]
    [Arguments("OpenCode")]
    public async Task Invalid_body_is_refused_server_side_before_any_runner_request(string variant)
    {
        var handler = new RunnerSpy("valid");
        var content = variant switch {
            "nul" => "private-sentinel\0", "unresolved_key" => "private-sentinel{{key:SECRET}}",
            "too_large" => new string('é', 131073), "invalid_unicode" => "\ud800", _ => "private-sentinel" };
        var spec = Spec() with { Kind = Enum.TryParse<AgentKind>(variant, out var kind) ? kind : AgentKind.Grok,
            GrokRulesPayload = new(content, 1, Guid.NewGuid()) };
        Exception? failure = null;
        try { await Client(handler).StartAsync(Guid.NewGuid(), spec, CancellationToken.None); }
        catch (Exception ex) { failure = ex; }
        handler.Requests.ShouldBe(0, "server body validation must precede capability and launch requests");
        failure.ShouldBeOfType<Antiphon.Server.Application.Services.GrokRulesHttpException>()
            .Code.ShouldBe("grok_rules_content_invalid");
        failure.Message.ShouldNotContain("private-sentinel");
    }

    [Test]
    public async Task Payload_and_budget_round_trip_while_receipt_remains_metadata_only()
    {
        var handler = new RunnerSpy("valid");
        var spec = Spec() with { CommandLineBudgetChars = 9876 };
        await Client(handler).StartAsync(Guid.NewGuid(), spec, CancellationToken.None);
        handler.Starts.ShouldBe(1);
        handler.Launch.ShouldNotBeNull();
        handler.Launch.GrokRulesPayload.ShouldBe(spec.GrokRulesPayload);
        handler.Launch.CommandLineBudgetChars.ShouldBe(9876);
        handler.Launch.Args.ShouldAllBe(a => !a.Contains(spec.GrokRulesPayload!.Content));
        handler.Launch.Env.Values.ShouldAllBe(a => !a.Contains(spec.GrokRulesPayload!.Content));
    }

    private static AgentLaunchSpec Spec() => new("grok", AgentKind.Grok, "grok.exe", [],
        new Dictionary<string,string>(), "C:\\disposable", 120, 30,
        GrokRulesPayload: new("private-sentinel\r\nfull rules café", 1, Guid.NewGuid()));

    private static SessionRunnerHttpClient Client(RunnerSpy handler) => new(new HttpClient(handler), new Factory(),
        Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings { BaseUrl = "http://runner.test" }));
    private sealed class Factory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class RunnerSpy(string capability) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public int Starts { get; private set; }
        public RunnerLaunchRequest? Launch { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (request.Method == HttpMethod.Post)
            {
                Starts++;
                Launch = await request.Content!.ReadFromJsonAsync<RunnerLaunchRequest>(ct);
                return new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
            var json = capability switch {
                "null" => "null", "missing" => "{\"transcriptFormats\":[\"grok\"],\"features\":[]}",
                _ => "{\"transcriptFormats\":[\"grok\"],\"sessionBackends\":[\"pty-host\",\"herdr\"],\"features\":[\"grokRulesFileV1\"]}" };
            return new(capability == "404" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
