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

    private static readonly DecisionRequest SystemOneRequest = new("Documentation and a defect fix.",
    [
        new DecisionQuestion("change_kind", DecisionQuestionKind.Choice, "What kind of change is this?",
            [new("docs"), new("bug", "a defect fix")]),
        DecisionQuestion.Noul("material", "Does this change stored data?"),
        Request.Questions[2],
    ]);

    // Sanitised LiteLLM response from GitHub issue #58.
    private const string SystemOneAnswers = """
        {
          "model": "typesafe/jev",
          "answers": {
            "change_kind": { "type": "choice", "choice": "docs", "confidence": 1.0,
                             "probabilities": { "docs": 1.0, "bug": 0.0 } },
            "material": { "type": "noul", "noul": 0.07 },
            "reach": { "type": "score", "score": 1, "confidence": 0.7 }
          },
          "usage": { "input_tokens": 347, "output_tokens": 56 }
        }
        """;

    [Test]
    public async Task System_one_posts_the_golden_request_for_named_choice_noul_and_score_questions()
    {
        const string expected = """
            {
              "model": "jev-latest",
              "state": "Documentation and a defect fix.",
              "questions": {
                "change_kind": { "type": "choice", "instructions": "What kind of change is this?",
                                 "criteria": { "docs": null, "bug": "a defect fix" } },
                "material": { "type": "noul", "instructions": "Does this change stored data?" },
                "reach": { "type": "score", "instructions": "How many components does this affect?",
                           "criteria": ["One place", "A few", "System-wide"] }
              }
            }
            """;
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "answers": {} }"""));
        var settings = Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example/"));
        using var http = new HttpClient(handler);
        var client = new JevSystemOneClient(http, settings);

        await client.DecideAsync(SystemOneRequest, new DecisionOptions("jev"), CancellationToken.None);

        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe("https://proxy.example/v1/systemone");
        sent.Authorization.ShouldBe("Bearer key-jev");
        JsonNode.DeepEquals(JsonNode.Parse(sent.Body), JsonNode.Parse(expected))
            .ShouldBeTrue($"System One request body was {sent.Body}");
    }

    [Test]
    [Arguments("Closes a single defect", "Changes a subsystem", "Touches the whole system",
        """["Closes a single defect", "Changes a subsystem", "Touches the whole system"]""")]
    [Arguments(null, "Changes a subsystem", "  ",
        """["One place", "Changes a subsystem", "System-wide"]""")]
    public async Task System_one_posts_score_descriptions_in_rubric_order_and_decodes_original_labels(
        string? lowDescription, string middleDescription, string? highDescription, string expectedCriteria)
    {
        var reach = SystemOneRequest.Questions[2] with
        {
            Options = [new("One place", lowDescription), new("A few", middleDescription), new("System-wide", highDescription)],
        };
        var request = SystemOneRequest with { Questions = [reach] };
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """
            { "answers": { "reach": { "type": "score", "score": 1,
                                     "legend": { "1": "Changes a subsystem" },
                                     "probabilities": { "0": 0.1, "1": 0.8, "2": 0.1 } } } }
            """));
        using var http = new HttpClient(handler);
        var client = new JevSystemOneClient(http, Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example")));

        var result = await client.DecideAsync(request, new DecisionOptions("jev"), CancellationToken.None);

        var criteria = JsonNode.Parse(handler.Requests.ShouldHaveSingleItem().Body)!["questions"]!["reach"]!["criteria"];
        criteria!.AsArray().Select(level => level!.GetValue<string>())
            .ShouldBe(JsonNode.Parse(expectedCriteria)!.AsArray().Select(level => level!.GetValue<string>()));
        var answer = result.Answers["reach"];
        answer.Value.ShouldBe("A few");
        answer.Probabilities!.Keys.ShouldBe(new[] { "One place", "A few", "System-wide" }, ignoreOrder: true);
        answer.Probabilities["A few"].ShouldBe(0.8);
    }

    [Test]
    public async Task System_one_reads_the_live_answers_and_records_model_tokens_and_cost()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, SystemOneAnswers, cost: "0.0123"));
        var settings = Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example"));

        var result = await Chain(handler, settings).DecideAsync("test", SystemOneRequest, CancellationToken.None);

        var choice = result.Answers["change_kind"];
        choice.Value.ShouldBe("docs");
        choice.Confidence.ShouldBe(1.0);
        choice.Probabilities!["docs"].ShouldBe(1.0);
        choice.Probabilities["bug"].ShouldBe(0.0);
        choice.ServedBy.ShouldBe(new DecisionServedBy("jev", "Http", "typesafe/jev"));
        result.Answers["material"].Value.ShouldBe("no");
        result.Answers["material"].Probabilities!["yes"].ShouldBe(0.07, 1e-9);
        result.Answers["reach"].Value.ShouldBe("A few");
        result.Answers["reach"].Confidence.ShouldBe(0.7);
        var call = result.Calls.ShouldHaveSingleItem();
        call.ResolvedModel.ShouldBe("typesafe/jev");
        call.InputTokens.ShouldBe(347);
        call.OutputTokens.ShouldBe(56);
        call.CostUsd.ShouldBe(0.0123m);
        call.CostBasis.ShouldBe("actual");
    }

    [Test]
    public async Task System_one_tolerates_unknown_fields_and_maps_fractional_scores_to_levels()
    {
        var response = JsonNode.Parse(SystemOneAnswers)!.AsObject();
        response["meta"] = new JsonObject { ["trace"] = 123 };
        response["answers"]!["extra_question"] = new JsonArray();
        var choice = response["answers"]!["change_kind"]!.AsObject();
        choice["extra_answer_field"] = true;
        choice.Remove("probabilities");
        choice["confidence"] = 0.6;
        var reach = response["answers"]!["reach"]!;
        reach["score"] = 1.7;
        reach["legend"] = JsonNode.Parse("""{ "0": "One place", "1": "A few", "2": "System-wide" }""");
        reach["probabilities"] = JsonNode.Parse("""{ "0": 0.1, "1": 0.1, "2": 0.8 }""");
        using var http = new HttpClient(new FakeHandler(_ => Json(HttpStatusCode.OK, response.ToJsonString())));
        var client = new JevSystemOneClient(http, Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example")));

        var result = await client.DecideAsync(SystemOneRequest, new DecisionOptions("jev"), CancellationToken.None);

        result.Answers.Count.ShouldBe(3);
        result.Answers["change_kind"].Value.ShouldBe("docs");
        result.Answers["change_kind"].Confidence.ShouldBe(0.6);
        var score = result.Answers["reach"];
        score.Value.ShouldBe("System-wide");
        score.Probabilities!["System-wide"].ShouldBe(0.8);
    }

    [Test]
    public async Task System_one_rounds_a_score_tie_up()
    {
        var answer = await ReadSystemOneScoreAsync("0.5");

        answer.Value.ShouldBe("A few");
    }

    [Test]
    public async Task System_one_rejects_a_score_just_below_zero()
    {
        var failure = await Should.ThrowAsync<DecisionProviderException>(() => ReadSystemOneScoreAsync("-0.1"));

        failure.Kind.ShouldBe(DecisionFailureKind.SchemaFailure);
        failure.Message.ShouldBe("Answer 'reach' has a score outside its levels.");
    }

    [Test]
    [Arguments("1e999")]
    [Arguments("-1e999")]
    [Arguments("\"NaN\"")]
    public async Task System_one_rejects_nonfinite_scores(string score)
    {
        var failure = await Should.ThrowAsync<DecisionProviderException>(() => ReadSystemOneScoreAsync(score));

        failure.Kind.ShouldBe(DecisionFailureKind.SchemaFailure);
        failure.Message.ShouldBe("Answer 'reach' has a score outside its levels.");
    }

    private static async Task<DecisionAnswer> ReadSystemOneScoreAsync(string score)
    {
        var request = SystemOneRequest with { Questions = [SystemOneRequest.Questions[2]] };
        var body = $$"""
            { "answers": { "reach": { "type": "score", "score": {{score}} } } }
            """;
        using var http = new HttpClient(new FakeHandler(_ => Json(HttpStatusCode.OK, body)));
        var client = new JevSystemOneClient(http, Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example")));
        var result = await client.DecideAsync(request, new DecisionOptions("jev"), CancellationToken.None);
        return result.Answers["reach"];
    }

    [Test]
    [Arguments("change_kind", """{ "type": "choice", "choice": {}, "probabilities": { "docs": 1.0 } }""")]
    [Arguments("change_kind", """{ "type": "choice", "choice": "other" }""")]
    [Arguments("change_kind", """{ "type": "choice", "choice": "docs", "confidence": {} }""")]
    [Arguments("reach", """{ "type": "score", "score": 3 }""")]
    public async Task System_one_classifies_malformed_answers_as_schema_failures(string name, string answer)
    {
        var response = JsonNode.Parse(SystemOneAnswers)!;
        response["answers"]![name] = JsonNode.Parse(answer);
        using var http = new HttpClient(new FakeHandler(_ => Json(HttpStatusCode.OK, response.ToJsonString())));
        var client = new JevSystemOneClient(http, Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example")));
        var request = SystemOneRequest with { Questions = [SystemOneRequest.Questions.Single(q => q.Name == name)] };

        var failure = await Should.ThrowAsync<DecisionProviderException>(() =>
            client.DecideAsync(request, new DecisionOptions("jev"), CancellationToken.None));

        failure.Kind.ShouldBe(DecisionFailureKind.SchemaFailure);
        failure.Message.ShouldContain($"'{name}'");
    }

    [Test]
    public async Task A_system_one_400_projects_a_proxy_error_and_omits_the_request_preview()
    {
        const string message = "Invalid Decisions request: questions.change_kind.choice.criteria Field required";
        var settings = Settings(("primary", JevDialects.LiteLlmSystemOne, "https://primary.example"),
            ("secondary", JevDialects.TypeSafeNative, "https://secondary.example"));
        var key = settings.Value.Providers["primary"].ApiKey!;
        var envelope = new JsonObject { ["error"] = new JsonObject { ["message"] = $"{message} ({key}) {new string('x', 400)}" } };
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.BadRequest, envelope.ToJsonString()));

        var failure = await Should.ThrowAsync<DecisionChainException>(() =>
            Chain(handler, settings).DecideAsync("test", SystemOneRequest, CancellationToken.None));

        failure.Kind.ShouldBe(DecisionFailureKind.Rejected);
        const string prefix = "Provider 'primary' rejected the request (400): HTTP 400: ";
        failure.Message.ShouldBe(prefix + "Invalid Decisions request");
        failure.Message.ShouldNotContain(key);
        failure.Message.Length.ShouldBeLessThanOrEqualTo(prefix.Length + 300);
        failure.Calls.ShouldHaveSingleItem().Outcome.ShouldBe("rejected");
        handler.Requests.ShouldHaveSingleItem().Uri.ShouldBe("https://primary.example/v1/systemone");
    }

    [Test]
    [Arguments("""{"detail":"Invalid score question"}""", "Invalid score question")]
    [Arguments("""{"detail":{"message":"Rubric is invalid"}}""", "Rubric is invalid")]
    [Arguments("""
        {"detail":[{"loc":["body","questions","reach","criteria",0],"msg":"Field required","type":"missing","input":"discard this preview"},
                   {"loc":["body","state"],"msg":"Expected text","type":"string_type"}]}
        """, "body.questions.2.criteria.0: Field required; body.state: Expected text")]
    [Arguments("""
        {"detail":{"errors":{"detail":[{"loc":["body","questions"],"msg":"Expected a question map","ctx":{"msg":"discard this context"}}]}}}
        """, "body.questions: Expected a question map")]
    [Arguments("""{"detail":{"loc":"body.state","msg":"Expected text"}}""", "body.state: Expected text")]
    [Arguments("""{"status":"bad request"}""", DiagnosticOmitted)]
    [Arguments("Proxy refused the request", DiagnosticOmitted)]
    [Arguments("""
        {"detail":{"loc":["body","sk-fakecredential1234"],"msg":"Invalid key-primary under Bearer fixture-only-credential"}}
        """, DiagnosticOmitted)]
    public async Task A_system_one_400_surfaces_detail_and_rejects_without_fallback(string body, string expected)
    {
        var failure = await RejectSystemOneRequestAsync(body);

        failure.Message.ShouldBe(Rejected400Prefix + expected);
    }

    [Test]
    public async Task A_system_one_400_redacts_a_token_crossing_the_diagnostic_limit()
    {
        var padding = new string('x', 290);
        var body = $$"""
            { "detail": { "message": "{{padding}} sk-fixturetoken1234 tail" } }
            """;
        var failure = await RejectSystemOneRequestAsync(body);

        failure.Message.ShouldBe(Rejected400Prefix + DiagnosticOmitted);
    }

    [Test]
    public async Task A_system_one_400_caps_the_projected_diagnostic()
    {
        const string message = "Field required";
        var detail = new JsonArray();
        for (var i = 0; i < 24; i++)
            detail.Add(new JsonObject { ["msg"] = message });

        var failure = await RejectSystemOneRequestAsync(new JsonObject { ["detail"] = detail }.ToJsonString());

        failure.Message.ShouldBe(Rejected400Prefix + string.Join("; ", Enumerable.Repeat(message, 24))[..300]);
    }

    [Test]
    [Arguments("""{"detail":{"message":"Field required \uD800","errors":[{"msg":"Expected text"}]}}""")]
    [Arguments("""{"detail":{"message":"Field required \uDC00","errors":[{"msg":"Expected text"}]}}""")]
    [Arguments("""{"detail":{"loc":["body","\uD800"],"msg":"Field required"}}""")]
    [Arguments("""{"detail":{"loc":["body","\uDC00"],"msg":"Field required"}}""")]
    [Arguments("""{"detail":{"loc":"\uD800","msg":"Field required"}}""")]
    [Arguments("""{"detail":{"loc":"\uDC00","msg":"Field required"}}""")]
    public async Task A_system_one_400_with_a_lone_surrogate_still_records_the_rejection(string body)
    {
        var failure = await RejectSystemOneRequestAsync(body);

        failure.Message.ShouldBe(Rejected400Prefix + DiagnosticOmitted);
    }

    [Test]
    [Arguments("fixture.person@example.test")]
    [Arguments("X-API-Key: fixture-only-credential")]
    [Arguments("Authorization: Bearer fixture-only-credential")]
    [Arguments("https://fixture-user:fixture-pass@example.test/?key=fixture-secret")]
    [Arguments("d41d8cd98f00b204e9800998ecf8427e")]
    [Arguments("Rml4dHVyZU9ubHlDcmVkZW50aWFsTm90QVJlYWxLZXk=")]
    [Arguments("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmaXh0dXJlIn0.dGVzdHNpZ25hdHVyZQ")]
    public async Task A_system_one_400_omits_sensitive_message_and_location_values(string sensitive)
    {
        var detail = new JsonArray(
            new JsonObject { ["msg"] = sensitive, ["loc"] = new JsonArray("body", "state") },
            new JsonObject { ["msg"] = "Field required", ["loc"] = new JsonArray("body", sensitive) });
        var failure = await RejectSystemOneRequestAsync(new JsonObject { ["detail"] = detail }.ToJsonString());

        failure.Message.ShouldBe(Rejected400Prefix + "Field required");
    }

    [Test]
    [Arguments(0)]
    [Arguments(13)]
    [Arguments(70)]
    public async Task A_system_one_400_omits_unparseable_error_bodies(int depth)
    {
        const string body = """{"detail":{"msg":"Field required"},"input":"private request preview""";
        var failure = await RejectSystemOneRequestAsync(depth > 0
            ? body + "\",\"nested\":" + new string('[', depth) + "0" + new string(']', depth) + "}"
            : body);

        failure.Message.ShouldBe(Rejected400Prefix + DiagnosticOmitted);
    }

    [Test]
    [Arguments(1, 65)]
    [Arguments(8, 40)]
    public async Task A_system_one_400_omits_diagnostics_when_the_json_traversal_budget_is_exceeded(int arrays, int items)
    {
        var detail = new JsonArray(new JsonObject { ["msg"] = "Field required" });
        for (var array = 0; array < arrays; array++)
        {
            var padding = new JsonArray();
            for (var i = 0; i < items; i++)
                padding.Add(0);
            detail.Add(padding);
        }
        var failure = await RejectSystemOneRequestAsync(new JsonObject { ["detail"] = detail }.ToJsonString());

        failure.Message.ShouldBe(Rejected400Prefix + DiagnosticOmitted);
    }

    [Test]
    public async Task A_system_one_400_stops_reading_an_oversized_body()
    {
        const int byteLimit = 16 * 1024;
        var bytes = Encoding.UTF8.GetBytes("""{"detail":{"msg":"Field required"}}""" + new string(' ', byteLimit * 3));
        using var stream = new CountedErrorStream(bytes);
        using var content = new StreamContent(stream);

        var failure = await RejectSystemOneRequestAsync(content);

        stream.BytesRead.ShouldBe(byteLimit + 1);
        failure.Message.ShouldBe(Rejected400Prefix + DiagnosticOmitted);
    }

    private const string Rejected400Prefix = "Provider 'primary' rejected the request (400): HTTP 400: ";
    private const string DiagnosticOmitted = "Provider diagnostic omitted.";

    private static Task<DecisionChainException> RejectSystemOneRequestAsync(string body) =>
        RejectSystemOneRequestAsync(new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<DecisionChainException> RejectSystemOneRequestAsync(HttpContent content)
    {
        var settings = Settings(("primary", JevDialects.TypeSafeNative, "https://primary.example"),
            ("secondary", JevDialects.LiteLlmSystemOne, "https://secondary.example"));
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content });

        var failure = await Should.ThrowAsync<DecisionChainException>(() =>
            Chain(handler, settings).DecideAsync("test", SystemOneRequest, CancellationToken.None));

        failure.Kind.ShouldBe(DecisionFailureKind.Rejected);
        var call = failure.Calls.ShouldHaveSingleItem();
        call.Outcome.ShouldBe("rejected");
        call.Provider.ShouldBe("primary");
        handler.Requests.ShouldHaveSingleItem().Uri.ShouldBe("https://primary.example/v1/systemone");
        return failure;
    }

    private sealed class CountedErrorStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Read(buffer.Span);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
    }

    [Test]
    public async Task Jev_client_reads_legacy_answers_and_prices_usage_without_a_cost_header()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, GoodAnswers, cost: "0.0123"));
        var settings = Settings(("jev", JevDialects.LiteLlmSystemOne, "https://proxy.example/"));
        var client = new JevSystemOneClient(new HttpClient(handler), settings);

        var result = await client.DecideAsync(Request, new DecisionOptions("jev", "jev-latest"), CancellationToken.None);

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
