using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class GrokRulesReceiptTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("relative")]
    [Arguments("traversal")]
    [Arguments("cr")]
    [Arguments("lf")]
    [Arguments("nul")]
    [Arguments("session")]
    [Arguments("hash")]
    [Arguments("count")]
    [Arguments("version")]
    [Arguments("generation")]
    [Arguments("bootstrap")]
    [Arguments("windows_reserved")]
    [Arguments("windows_trailing_dot")]
    [Arguments("windows_stream")]
    [Arguments("valid_remote")]
    public async Task Runner_receipt_is_validated_before_commit_or_refresh_input(string variant)
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = services => {
            services.AddSingleton(Options.Create(new GrokRulesSettings()));
            services.AddSingleton<GrokRulesRefreshService>();
        }});
        var rules = h.Provider.GetRequiredService<GrokRulesRefreshService>();
        var bytes = Encoding.UTF8.GetBytes("private file-only sentinel\r\nrules");
        var expected = new GrokRulesReceipt($"C:\\remote runner\\instructions\\grok\\{h.SessionId:N}\\rules.md",
            GrokRulesTransport.Hash(bytes), bytes.Length, 1, Guid.NewGuid());
        var received = variant switch {
            "missing" => null,
            "relative" => expected with { Path = expected.Path[3..] },
            "traversal" => expected with { Path = expected.Path.Replace("remote runner", "remote runner\\..") },
            "cr" => expected with { Path = expected.Path.Replace("remote runner", "remote\rrunner") },
            "lf" => expected with { Path = expected.Path.Replace("remote runner", "remote\nrunner") },
            "nul" => expected with { Path = expected.Path.Replace("remote runner", "remote\0runner") },
            "session" => expected with { Path = expected.Path.Replace(h.SessionId.ToString("N"), Guid.NewGuid().ToString("N")) },
            "hash" => expected with { Sha256 = new string('0', 64) },
            "count" => expected with { ByteCount = expected.ByteCount + 1 },
            "version" => expected with { TransportVersion = 2 },
            "generation" => expected with { Generation = Guid.NewGuid() },
            "bootstrap" => expected with { Path = expected.Path.Replace("remote runner", new string('x', 4096)) },
            "windows_reserved" => expected with { Path = expected.Path.Replace("remote runner", "NUL") },
            "windows_trailing_dot" => expected with { Path = expected.Path.Replace("remote runner", "remote.") },
            "windows_stream" => expected with { Path = expected.Path.Replace("remote runner", "remote:stream") },
            _ => expected,
        };
        h.Runner.SessionResponse = JsonSerializer.Deserialize<SessionRunnerSessionDto>("{}")! with { GrokRulesReceipt = received };
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.Status = SessionStatus.Starting;
        session.GrokRulesGeneration = expected.Generation;
        session.GrokRulesExpectedSha256 = expected.Sha256;
        session.GrokRulesExpectedByteCount = expected.ByteCount;
        session.GrokRulesState = GrokRulesState.Pending;
        await db.SaveChangesAsync();
        var ordinary = await h.SeedPendingMessageAsync("original task goal");
        Exception? failure = null;
        try { await rules.CaptureReceiptAsync(h.SessionId, CancellationToken.None); }
        catch (Exception ex) { failure = ex; }
        await db.Entry(session).ReloadAsync();
        if (variant != "valid_remote")
        {
            session.GrokRulesReceiptJson.ShouldBeNull("invalid receipt must never be committed");
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesRefreshKey != null)).ShouldBe(0);
            failure.ShouldNotBeNull();
            failure.Message.ShouldNotContain("private file-only sentinel");
        }
        else
        {
            failure.ShouldBeNull();
            GrokRulesRefreshService.Receipt(session).ShouldBe(expected);
            var refresh = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.RulesRefreshKey != null);
            refresh.RulesDeadlineAt.ShouldBeNull("receipt precedes provider readiness");
        }
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ordinary)).Status.ShouldBe(QueuedMessageStatus.Pending);
        session.GrokRulesState.ShouldBe(GrokRulesState.Pending);
    }
}
