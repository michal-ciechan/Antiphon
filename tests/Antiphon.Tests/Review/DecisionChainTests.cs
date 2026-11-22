using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Review;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>Jev System One request and response handling, and chain fallback (design 7.3, 7.4), over a fake handler.</summary>
[Category("Unit")]
public class DecisionChainTests
{
    private static readonly DecisionRequest Request = new("PR #812: release the lock after the save.",
    [
        DecisionQuestion.Noul("persistent_data", "Does this change stored data?"),
        new DecisionQuestion("change_kind", DecisionQuestionKind.Choice, "What kind of change is this?",
            [new("feature"), new("bugfix", "Fixes a defect")]),
        new DecisionQuestion("reach", DecisionQuestionKind.Score, "How many components does this affect?",
            [new("One place"), new("A few"), new("System-wide")]),
    ]);

    private const string GoodAnswers = """
        { "model": "typesafe/jev-1.13.0",
          "answers": {
            "persistent_data": { "probability": 0.12 },
            "change_kind": { "value": "bugfix", "probabilities": { "feature": 0.1, "bugfix": 0.9 } },
            "reach": { "value": "A few", "probabilities": { "One place": 0.2, "A few": 0.7, "System-wide": 0.1 } } },
          "usage": { "input_tokens": 2000000, "output_tokens": 30 } }
        """;

    [Test]
    public async Task Jev_client_sends_the_system_one_body_and_parses_answers_usage_and_cost()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, GoodAnswers, cost: "0.0123"));
        var settings = Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example/"));
        var client = new JevSystemOneClient(new HttpClient(handler), settings);

        var result = await client.DecideAsync(Request, new DecisionOptions("jev", "jev-latest"), CancellationToken.None);

        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe("https://proxy.example/v1/systemone");
        sent.Authorization.ShouldBe("Bearer key-jev");
        var body = JsonNode.Parse(sent.Body)!;
        body["model"]!.GetValue<string>().ShouldBe("jev-latest");
        body["state"]!.GetValue<string>().ShouldStartWith("PR #812");
        body["questions"]!["persistent_data"]!["type"]!.GetValue<string>().ShouldBe("noul");
        body["questions"]!["change_kind"]!["choices"]!.ToJsonString().ShouldBe("""[{"value":"feature"},{"value":"bugfix"}]""");
        body["questions"]!["change_kind"]!["instructions"]!.GetValue<string>().ShouldEndWith("bugfix: Fixes a defect");
        body["questions"]!["reach"]!["levels"]![2]!["label"]!.GetValue<string>().ShouldBe("System-wide");

        result.ServedBy.ResolvedModel.ShouldBe("typesafe/jev-1.13.0");
        result.CostUsd.ShouldBe(0.0123m);
        result.CostBasis.ShouldBe("actual");
        result.Usage.InputTokens.ShouldBe(2_000_000);
        result.Answers["persistent_data"].Value.ShouldBe("no");
        result.Answers["persistent_data"].Probabilities!["yes"].ShouldBe(0.12, 1e-9);
        result.Answers["change_kind"].Value.ShouldBe("bugfix");
        result.Answers["change_kind"].Confidence.ShouldBe(0.9);
        result.Answers["reach"].Value.ShouldBe("A few");

        // No cost header: the price table, $0.042 per million input tokens.
        var priced = new JevSystemOneClient(new HttpClient(new FakeHandler(_ => Json(HttpStatusCode.OK, GoodAnswers))), settings);
        (await priced.DecideAsync(Request, new DecisionOptions("jev"), CancellationToken.None)).CostUsd.ShouldBe(0.084m);
    }

    [Test]
    public async Task A_429_backs_off_once_then_falls_back_to_the_next_entry()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "primary.example"
            ? Json((HttpStatusCode)429, """{"error":{"message":"rate limited"}}""")
            : Json(HttpStatusCode.OK, GoodAnswers));
        var settings = Settings(("primary", JevDialects.LiteLlmSystemOne, "https://primary.example"),
            ("secondary", JevDialects.TypeSafeNative, "https://secondary.example"));
        var chain = Chain(handler, settings);

        var result = await chain.DecideAsync("test", Request, CancellationToken.None);

        handler.Requests.Select(r => r.Uri).ShouldBe([
            "https://primary.example/v1/systemone",
            "https://primary.example/v1/systemone",
            "https://secondary.example/v1/systemone",
        ]);
        result.Calls.Select(c => (c.Provider, c.Outcome)).ShouldBe([("primary", "rate-limited"), ("primary", "rate-limited"), ("secondary", "ok")]);
        result.Answers.Values.ShouldAllBe(a => a.ServedBy!.Provider == "secondary");
        result.Answers["change_kind"].Value.ShouldBe("bugfix");
    }

    [Test]
    public async Task A_422_fails_loudly_with_no_fallback()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "primary.example"
            ? Json(HttpStatusCode.UnprocessableEntity, """{"error":{"message":"bad question type"}}""")
            : Json(HttpStatusCode.OK, GoodAnswers));
        var settings = Settings(("primary", JevDialects.LiteLlmSystemOne, "https://primary.example"),
            ("secondary", JevDialects.TypeSafeNative, "https://secondary.example"));

        var failure = await Should.ThrowAsync<DecisionChainException>(() => Chain(handler, settings).DecideAsync("test", Request, CancellationToken.None));

        failure.Kind.ShouldBe(DecisionFailureKind.Rejected);
        failure.Message.ShouldContain("bad question type");
        handler.Requests.ShouldHaveSingleItem().Uri.ShouldStartWith("https://primary.example");
    }

    [Test]
    public async Task A_low_confidence_answer_escalates_only_that_question_and_seats_report_not_configured()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "primary.example"
            ? Json(HttpStatusCode.OK, """
                { "answers": { "persistent_data": { "probability": 0.05 },
                               "change_kind": { "value": "bugfix", "probabilities": { "feature": 0.45, "bugfix": 0.55 } } } }
                """)
            : Json(HttpStatusCode.OK, GoodAnswers));
        var settings = Settings(("primary", JevDialects.LiteLlmSystemOne, "https://primary.example"),
            ("secondary", JevDialects.TypeSafeNative, "https://secondary.example"));
        settings.Value.Providers["primary"].ConfidenceThreshold = 0.8;
        settings.Value.Providers["seat"] = new ReviewDecisionProvider { Dialect = ReviewDecisionSettings.SeatDialect };
        settings.Value.Chains["test"].Insert(1, new ReviewDecisionChainEntry { Provider = "seat", Transport = "Seat" });

        var result = await Chain(handler, settings).DecideAsync("test", Request, CancellationToken.None);

        result.Answers["persistent_data"].ServedBy!.Provider.ShouldBe("primary");
        result.Answers["change_kind"].ServedBy!.Provider.ShouldBe("secondary", "0.55 is below the 0.8 threshold");
        result.Answers["reach"].ServedBy!.Provider.ShouldBe("secondary", "a missing answer key is a refusal");
        result.Calls.Select(c => c.Outcome).ShouldBe(["ok", "not-configured", "ok"]);
        result.Calls[2].Questions.ShouldBe(["change_kind", "reach"]);
        var secondBody = JsonNode.Parse(handler.Requests[1].Body)!["questions"]!.AsObject();
        secondBody.Select(q => q.Key).ShouldBe(["change_kind", "reach"]);
    }

    private static DecisionChain Chain(FakeHandler handler, IOptions<ReviewDecisionSettings> settings) =>
        new([new JevSystemOneClient(new HttpClient(handler), settings), new StandingSeatDecisionModel()],
            settings, TimeProvider.System, NullLogger<DecisionChain>.Instance);

    private static IOptions<ReviewDecisionSettings> Settings(params (string Id, string Dialect, string BaseUrl)[] providers)
    {
        var settings = new ReviewDecisionSettings { RateLimitBackoffMilliseconds = 0 };
        foreach (var (id, dialect, baseUrl) in providers)
            settings.Providers[id] = new ReviewDecisionProvider { Dialect = dialect, BaseUrl = baseUrl, Model = "jev-latest", ApiKey = "key-" + id };
        settings.Chains["test"] = providers.Select(p => new ReviewDecisionChainEntry { Provider = p.Id }).ToList();
        return Options.Create(settings);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string? cost = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (cost is not null)
            response.Headers.Add(JevSystemOneClient.CostHeader, cost);
        return response;
    }

    private sealed record SentRequest(string Uri, string? Authorization, string Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
