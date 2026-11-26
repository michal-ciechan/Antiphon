using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The away digest send (scripts/lib/Antiphon.Nightly/AwayDigestSend.cs, `antiphon-scripts digest`) in this process, on a
/// recorded HTTP transport: the requests scripts/digest.ps1 made (deleted 2026-11-23), the channel lookup by title, the
/// printed answer and the exit codes. No network.
/// </summary>
[Category("Unit")]
public sealed class AwayDigestSendTests
{
    private const string Channels = """[{"id":"7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01","title":"Family"},{"id":"0b0b7f4e-2d1c-4c55-8a8b-9a2f3c4d5e6f","title":"Work"},{"id":"1c1c7f4e-2d1c-4c55-8a8b-9a2f3c4d5e6f","title":"work"},{"id":"2d2d7f4e-2d1c-4c55-8a8b-9a2f3c4d5e6f","title":null}]""";

    private sealed class DigestServer
    {
        public List<(string Method, string Uri, string? Body)> Requests { get; } = [];
        public NightlyReportHttpResponse Send { get; set; } = new(200, """[{"channelId":"7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01","sent":true,"reason":null}]""");
        public NightlyReportHttpResponse List { get; set; } = new(200, Channels);

        public NightlyReportHttpResponse Answer(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            Requests.Add((method, uri, body));
            return uri.EndsWith("/api/channels", StringComparison.Ordinal) ? List : Send;
        }
    }

    private static (int Exit, string Out, string Error) Run(DigestServer server, TimeZoneInfo? zone, params string[] arguments)
    {
        using var fixture = NightlyFixture.Create("digest");
        var clock = new FakeTimeProvider(NightlyFixture.DefaultNow);
        if (zone != null) clock.SetLocalTimeZone(zone);
        var exit = AwayDigestSend.Run(AwayDigestSendOptions.Parse(arguments), fixture.Host(clock: clock), server.Answer);
        return (exit, fixture.Out.ToString().ReplaceLineEndings("\n"), fixture.Error.ToString());
    }

    [Test]
    public void Sends_every_channel_and_prints_the_answer()
    {
        var server = new DigestServer();
        var run = Run(server, null);
        run.Exit.ShouldBe(0, run.Error);
        server.Requests.ShouldBe([("POST", "http://localhost:17202/api/digest/send", "{}")]);
        run.Out.ShouldBe("""
            [
              {
                "channelId": "7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01",
                "sent": true,
                "reason": null
              }
            ]

            """.ReplaceLineEndings("\n"));
        run.Error.ShouldBeEmpty();
    }

    [Test]
    public void A_channel_is_a_guid_or_one_title()
    {
        var byId = new DigestServer();
        Run(byId, null, "--channel", "7D4C4B0E-3F55-4B8E-9A43-1F0D6F7E1A01", "--base-url", "http://example.test:9/").Exit.ShouldBe(0);
        byId.Requests.ShouldBe([("POST", "http://example.test:9/api/digest/send", """{"channelId":"7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01"}""")]);

        // The title matches ignoring case.
        var byTitle = new DigestServer();
        Run(byTitle, null, "--channel", "FAMILY").Exit.ShouldBe(0);
        byTitle.Requests.ShouldBe([
            ("GET", "http://localhost:17202/api/channels", null),
            ("POST", "http://localhost:17202/api/digest/send", """{"channelId":"7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01"}"""),
        ]);

        // Two channels called work, and none called home: nothing is sent.
        foreach (var title in new[] { "work", "home" })
        {
            var ambiguous = new DigestServer();
            var run = Run(ambiguous, null, "--channel", title);
            run.Exit.ShouldBe(1);
            run.Error.ShouldBe("digest: Channel '" + title + "' was not found uniquely by title. Pass its GUID instead." + Environment.NewLine);
            ambiguous.Requests.Select(request => request.Method).ShouldBe(["GET"]);
        }
    }

    [Test]
    public void Since_is_sent_as_utc()
    {
        var zoned = new DigestServer();
        Run(zoned, null, "--since", "2026-11-23T08:30:00+02:00").Exit.ShouldBe(0);
        zoned.Requests.Single().Body.ShouldBe("""{"since":"2026-11-23T06:30:00.0000000Z"}""");

        // No zone: the host's local time (here UTC+5).
        var local = new DigestServer();
        var plus5 = TimeZoneInfo.CreateCustomTimeZone("fixture+5", TimeSpan.FromHours(5), "fixture+5", "fixture+5");
        Run(local, plus5, "--since", "2026-11-23 08:30", "--channel", "7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01").Exit.ShouldBe(0);
        local.Requests.Single().Body.ShouldBe("""{"channelId":"7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01","since":"2026-11-23T03:30:00.0000000Z"}""");

        Should.Throw<NightlyUsageException>(() => AwayDigestSendOptions.Parse(["--since", "yesterday-ish"]));
        Should.Throw<NightlyUsageException>(() => AwayDigestSendOptions.Parse(["-Channel", "Family"]));
    }

    [Test]
    public void The_body_and_the_answer_are_the_server_records()
    {
        // The request binds to the endpoint's AwayDigestSendRequest (web JSON), and the server's own results are
        // printed and judged.
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var channel = Guid.Parse("7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01");
        var server = new DigestServer
        {
            Send = new(200, JsonSerializer.Serialize(new[] { new AwayDigestSendResult(channel, false, "send_failed"), new AwayDigestSendResult(null, false, "no_digest_channel") }, web)),
        };
        var run = Run(server, null, "--channel", channel.ToString("D"), "--since", "2026-11-23T08:30:00Z");
        run.Exit.ShouldBe(1);
        var request = JsonSerializer.Deserialize<AwayDigestSendRequest>(server.Requests.Single().Body!, web)!;
        request.ChannelId.ShouldBe(channel);
        request.Since.ShouldBe(new DateTime(2026, 11, 23, 8, 30, 0, DateTimeKind.Utc));
        request.Since!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        var printed = JsonSerializer.Deserialize<AwayDigestSendResult[]>(run.Out, web)!;
        printed.ShouldBe([new AwayDigestSendResult(channel, false, "send_failed"), new AwayDigestSendResult(null, false, "no_digest_channel")]);
    }

    [Test]
    public void A_failed_send_or_a_refusing_server_exits_1()
    {
        var failed = new DigestServer { Send = new(200, """[{"channelId":null,"sent":false,"reason":"no_digest_channel"},{"channelId":"7d4c4b0e-3f55-4b8e-9a43-1f0d6f7e1a01","sent":false,"reason":"send_failed"}]""") };
        var run = Run(failed, null);
        run.Exit.ShouldBe(1);
        run.Out.ShouldContain("\"reason\": \"send_failed\"", Case.Sensitive);
        run.Error.ShouldBe("digest: 1 channel(s) send_failed" + Environment.NewLine);

        // A result that is not send_failed is not a failure.
        Run(new DigestServer { Send = new(200, """[{"channelId":null,"sent":false,"reason":"no_digest_channel"}]""") }, null).Exit.ShouldBe(0);

        var refused = Run(new DigestServer { Send = new(500, "boom") }, null);
        refused.Exit.ShouldBe(1);
        refused.Out.ShouldBeEmpty();
        refused.Error.ShouldBe("digest: POST http://localhost:17202/api/digest/send answered 500: boom" + Environment.NewLine);

        var unlisted = Run(new DigestServer { List = new(503, "") }, null, "--channel", "Family");
        unlisted.Exit.ShouldBe(1);
        unlisted.Error.ShouldBe("digest: GET http://localhost:17202/api/channels answered 503" + Environment.NewLine);

        using var fixture = NightlyFixture.Create("digest");
        var down = AwayDigestSend.Run(AwayDigestSendOptions.Parse([]), fixture.Host(), (_, _, _, _) => throw new HttpRequestException("connection refused"));
        down.ShouldBe(1);
        fixture.Error.ToString().ShouldBe("digest: connection refused" + Environment.NewLine);
    }
}
