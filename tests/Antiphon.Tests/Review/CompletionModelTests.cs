using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Review;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The completion providers (design 7.2) over fake handlers and a scripted delegation seam: the
/// OpenAI-compatible chat adapter (LiteLLM), the Anthropic Messages adapter behind it in a chain, the
/// delegate seat, and the default chain built from the configured keys. No network, no agents.
/// </summary>
[Category("Unit")]
public class CompletionModelTests
{
    private static readonly CompletionRequest Request = new("Say whether each finding holds.",
        [new CompletionInput("findings", "C1 [Major] src/Lease.cs:13\n```inner fence```"), new CompletionInput("diff", "+ finally { _lock.Release(); }", "the change")],
        "decision-response/1.0");

    private const string Answer = "@abf 1 decision-response/1.0\n=== response verify-1\n=== answer holds_c1\nvalue: 0.9\nconfidence: 0.8\n=== end\n";

    [Test]
    public async Task The_chat_adapter_posts_system_and_user_messages_to_the_proxy_and_reads_the_answer_usage_and_cost()
    {
        var handler = new Handler(_ =>
        {
            var ok = Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                model = "openai/gpt-5.6-luna-2026-10",
                choices = new[] { new { message = new { role = "assistant", content = Answer }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 1200, completion_tokens = 80 },
            }));
            ok.Headers.Add(OpenAiChatCompletionModel.CostHeader, "0.00042");
            return ok;
        });
        var model = new OpenAiChatCompletionModel(new HttpClient(handler), Settings(s => s.Providers["litellm-chat"] = LiteLlm()));

        var result = await model.CompleteAsync(Request, new CompletionOptions("litellm-chat"), CancellationToken.None);

        var sent = handler.Sent.ShouldHaveSingleItem();
        sent.Uri.ShouldBe("https://llm.example/v1/chat/completions");
        sent.Headers["Authorization"].ShouldBe("Bearer sk-jev");
        using var body = JsonDocument.Parse(sent.Body);
        body.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["messages", "model"]);
        body.RootElement.GetProperty("model").GetString().ShouldBe("luna");
        var messages = body.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        messages[0].GetProperty("content").GetString().ShouldBe(Request.Instructions);
        var user = messages[1].GetProperty("content").GetString()!;
        user.ShouldContain("## findings\n````\nC1 [Major]", Case.Sensitive, "a fence longer than the input's own backtick run");
        user.ShouldContain("## diff (the change)");
        user.ShouldContain("decision-response/1.0 document");

        result.Text.ShouldBe(Answer);
        result.Usage.ShouldBe(new DecisionUsage(1200, 80));
        result.CostUsd.ShouldBe(0.00042m);
        result.CostBasis.ShouldBe("actual");
        result.ServedBy.ShouldBe(new DecisionServedBy("litellm-chat", "Http", "openai/gpt-5.6-luna-2026-10"));
    }

    [Test]
    [Arguments(401, DecisionFailureKind.Rejected)]
    [Arguments(400, DecisionFailureKind.Rejected)]
    [Arguments(429, DecisionFailureKind.RateLimited)]
    [Arguments(503, DecisionFailureKind.Unavailable)]
    [Arguments(504, DecisionFailureKind.Timeout)]
    public async Task A_chat_error_maps_to_the_failure_kind_that_decides_fallback(int status, DecisionFailureKind kind)
    {
        var handler = new Handler(_ => Json((HttpStatusCode)status, """{ "error": { "message": "proxy says no", "type": "x" } }"""));
        var model = new OpenAiChatCompletionModel(new HttpClient(handler), Settings(s => s.Providers["litellm-chat"] = LiteLlm()));

        var failure = await Should.ThrowAsync<CompletionProviderException>(() => model.CompleteAsync(Request, new CompletionOptions("litellm-chat"), CancellationToken.None));

        failure.Kind.ShouldBe(kind);
        failure.StatusCode.ShouldBe(status);
        failure.Message.ShouldContain("proxy says no");
        failure.Message.ShouldNotContain("sk-jev");
    }

    [Test]
    public async Task A_rate_limited_proxy_falls_back_to_the_anthropic_messages_api_in_the_chain()
    {
        var handler = new Handler(r => r.RequestUri!.Host == "llm.example"
            ? Json(HttpStatusCode.TooManyRequests, """{ "error": { "message": "slow down" } }""")
            : Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                model = "claude-haiku-5-5",
                stop_reason = "end_turn",
                content = new object[] { new { type = "thinking", thinking = "" }, new { type = "text", text = Answer } },
                usage = new { input_tokens = 900, cache_read_input_tokens = 100, output_tokens = 60 },
            })));
        var settings = Settings(s =>
        {
            s.Providers["litellm-chat"] = LiteLlm();
            s.Providers["anthropic"] = new ReviewCompletionProvider
            {
                Dialect = ReviewCompletionDialects.AnthropicMessages, Model = "claude-haiku-5-5", ApiKey = "sk-ant-test",
                InputPricePerMillionUsd = 0.10m, OutputPricePerMillionUsd = 0.50m, Effort = "low",
            };
            s.Chains["completions-cheap"] = [new() { Provider = "litellm-chat" }, new() { Provider = "anthropic" }];
        });
        var http = new HttpClient(handler);
        var chain = new CompletionChain([new OpenAiChatCompletionModel(http, settings), new AnthropicMessagesCompletionModel(http, settings)],
            settings, NullLogger<CompletionChain>.Instance);

        var result = await chain.CompleteAsync("completions-cheap", Request, CancellationToken.None);

        result.Text.ShouldBe(Answer);
        result.Calls.Select(c => (c.Provider, c.Outcome)).ShouldBe([("litellm-chat", nameof(DecisionFailureKind.RateLimited)), ("anthropic", "ok")]);
        result.ServedBy.ShouldBe(new DecisionServedBy("anthropic", "Http", "claude-haiku-5-5"));
        result.CostUsd.ShouldBe((1000 * 0.10m + 60 * 0.50m) / 1_000_000m);
        var sent = handler.Sent.Last();
        sent.Uri.ShouldBe("https://api.anthropic.com/v1/messages");
        sent.Headers["x-api-key"].ShouldBe("sk-ant-test");
        sent.Headers["anthropic-version"].ShouldBe(AnthropicMessagesCompletionModel.DefaultApiVersion);
        using var body = JsonDocument.Parse(sent.Body);
        body.RootElement.GetProperty("system").GetString().ShouldBe(Request.Instructions);
        body.RootElement.GetProperty("max_tokens").GetInt32().ShouldBe(AnthropicMessagesCompletionModel.DefaultMaxTokens);
        body.RootElement.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("low");
        body.RootElement.TryGetProperty("temperature", out _).ShouldBeFalse();
    }

    [Test]
    public async Task The_default_chain_holds_only_the_http_providers_whose_url_key_and_model_are_set()
    {
        var none = new ReviewCompletionSettings();
        none.ApplyDefaultProviders();
        none.ApplyDefaultChain();
        none.Chains.ShouldBeEmpty("no key: verify is skipped, nothing is spent");
        none.Providers.Keys.Order().ShouldBe(["anthropic", "haiku-delegate", "litellm-chat", "luna-delegate"]);
        none.Providers["litellm-chat"].ApiKeyConfigKey.ShouldBe("Review:Secrets:JevApiKey", "the same proxy key as Jev");

        var proxy = new ReviewCompletionSettings();
        proxy.Providers["litellm-chat"] = new ReviewCompletionProvider { BaseUrl = "https://llm.example", Model = "luna" };
        proxy.ApplyDefaultProviders();
        proxy.Providers["litellm-chat"].Dialect.ShouldBe(ReviewCompletionDialects.OpenAiChat, "configuration only set the URL and model");
        proxy.Providers["litellm-chat"].ApiKey = "sk-jev";
        proxy.ApplyDefaultChain();
        proxy.Chains[ReviewCompletionSettings.DefaultChain].Select(e => e.Provider).ShouldBe(["litellm-chat"]);
        await Task.CompletedTask;
    }

    [Test]
    public async Task A_delegate_seat_answers_in_a_read_only_low_task_and_is_cancelled_at_its_deadline()
    {
        var gateway = new ReviewDelegationRunnerApiTests.ScriptedTaskGateway();
        var settings = Settings(s => s.ApplyDefaultProviders());
        var seat = new DelegationCompletionModel(gateway, settings, NullLogger<DelegationCompletionModel>.Instance, TimeSpan.FromMilliseconds(20));

        var answering = seat.CompleteAsync(Request, new CompletionOptions("haiku-delegate", null, TimeSpan.FromSeconds(30)), CancellationToken.None);
        var (create, projectId, directory, taskId) = await WaitForAsync(gateway);
        create.Kind.ShouldBe(AgentTaskKind.Worker);
        create.Role.ShouldBe(AgentTaskRole.Custom);
        create.Workspace.ShouldBe(WorkspaceMode.ReadOnly);
        create.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        create.ModelLevel.ShouldBe(AgentModelLevel.Low);
        create.RunnerId.ShouldBe("local");
        create.CommitOnSettle.ShouldBe("Never");
        create.WorkingDirectory.ShouldBe(directory);
        projectId.ShouldBeNull();
        create.Goal.ShouldContain("# Instructions\n" + Request.Instructions);
        create.Goal.ShouldContain("## diff (the change)");
        gateway.Finish(taskId, AgentTaskStatus.Succeeded, "Here it is.\n" + Answer);

        var result = await answering;
        result.Text.ShouldContain(Answer);
        result.CostBasis.ShouldBe("api-equivalent");
        result.CostUsd.ShouldBe(0.37m);
        result.ServedBy.Transport.ShouldBe("Seat");

        gateway.Reset();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        Exception? stopped = null;
        try
        {
            await seat.CompleteAsync(Request, new CompletionOptions("haiku-delegate", null, TimeSpan.FromSeconds(1)), deadline.Token);
        }
        catch (Exception ex)
        {
            stopped = ex;
        }

        stopped.ShouldBeAssignableTo<OperationCanceledException>("the chain turns the budget's cancellation into a timeout");
        var (_, _, _, slowId) = gateway.Created.ShouldHaveSingleItem();
        gateway.Cancelled.ShouldHaveSingleItem().Id.ShouldBe(slowId);
    }

    private static async Task<(CreateAgentTaskRequest Request, Guid? ProjectId, string CallerDirectory, Guid Id)> WaitForAsync(
        ReviewDelegationRunnerApiTests.ScriptedTaskGateway gateway)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (gateway.Created.IsEmpty)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The seat created no task.");
            await Task.Delay(10);
        }

        return gateway.Created.ShouldHaveSingleItem();
    }

    private static ReviewCompletionProvider LiteLlm() => new()
    {
        Dialect = ReviewCompletionDialects.OpenAiChat, BaseUrl = "https://llm.example/", Model = "luna", ApiKey = "sk-jev",
    };

    private static IOptions<ReviewCompletionSettings> Settings(Action<ReviewCompletionSettings> configure)
    {
        var settings = new ReviewCompletionSettings();
        configure(settings);
        return Options.Create(settings);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record SentRequest(string Uri, IReadOnlyDictionary<string, string> Headers, string Body);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            Sent.Add(new SentRequest(request.RequestUri!.ToString(), headers, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
