using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Api.C0747ExpireSeed;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-0747 D-1: <c>POST /api/agent-tasks/land-notifications/expire</c> through the real Program.
/// A dry run stays open; <c>apply: true</c> needs the <c>X-Antiphon-Operator-Token</c> header
/// (403 <c>operator_token_required</c> otherwise, a child task token included) and is refused 409
/// <c>land_note_expiry_disabled</c> while <c>LandNoteReceiptExpiryDays</c> is 0. A refused apply
/// writes nothing. The hosted reconcile is removed so only the route can settle a note.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<C0747ExpireWebAppFactory>(Shared = SharedType.PerClass)]
public sealed class C0747LandNoteExpireEndpointTests(C0747ExpireWebAppFactory factory)
{
    internal const string Route = "/api/agent-tasks/land-notifications/expire";

    /// <summary>
    /// case: one aged terminal note and one six-day note, posted by each caller shape.
    /// mutation (operator-apply): the operator comparison never matches. red: 403 and the aged note stays open.
    /// mutation (no-token-apply, child-token-apply): drop OperatorCredential.Require, or restore the old
    /// child-token-only check. red: 200 and the aged note is settled, or 403 caller_scope for the child token.
    /// mutation (bare-dry-run): require the credential on a dry run too. red: 403.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("bare-dry-run")]
    [Arguments("no-token-apply")]
    [Arguments("child-token-apply")]
    [Arguments("operator-apply")]
    public async Task C0747_Expire_apply_needs_the_operator_token(string arm, CancellationToken ct)
    {
        var (aged, young) = await SeedAsync(factory, ct);
        var childToken = arm == "child-token-apply" ? await SeedChildTaskTokenAsync(factory, ct) : null;
        var before = await SnapshotAsync(factory, ct);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        if (arm == "operator-apply")
            request.Headers.TryAddWithoutValidation(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(factory.OperatorTokenPath));
        if (childToken is not null)
            request.Headers.TryAddWithoutValidation(Antiphon.Server.Api.Endpoints.AgentTaskEndpoints.TokenHeader, childToken);
        request.Content = arm == "bare-dry-run" ? JsonContent.Create(new { }) : JsonContent.Create(new { apply = true });
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var after = await SnapshotAsync(factory, ct, before.Keys);

        switch (arm)
        {
            case "bare-dry-run":
                ((int)response.StatusCode).ShouldBe(200, body.ToString());
                body.GetProperty("apply").GetBoolean().ShouldBeFalse();
                body.GetProperty("outcomes").GetProperty("WouldSettle").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
                after.ShouldBe(before, "a dry run writes nothing");
                break;
            case "no-token-apply":
            case "child-token-apply":
                ((int)response.StatusCode).ShouldBe(403, body.ToString());
                body.GetProperty("code").GetString().ShouldBe("operator_token_required");
                after.ShouldBe(before, "a refused apply writes nothing");
                break;
            default:
                ((int)response.StatusCode).ShouldBe(200, body.ToString());
                body.GetProperty("apply").GetBoolean().ShouldBeTrue();
                after[aged].ShouldBe((LandNotificationState.LegacyUnverified, AgentTaskLandNotificationService.ReceiptExpiredCode));
                after[young].ShouldBe(before[young], "a six-day note is never settled");
                break;
        }
    }
}

/// <summary>CARD-0747 D-1: the expire route on a host whose expiry off switch is set.</summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<C0747ExpireDisabledWebAppFactory>(Shared = SharedType.PerClass)]
public sealed class C0747LandNoteExpireOffSwitchEndpointTests(C0747ExpireDisabledWebAppFactory factory)
{
    private const string Route = C0747LandNoteExpireEndpointTests.Route;

    /// <summary>
    /// case: Supervision:DeliveryVerification:LandNoteReceiptExpiryDays=0, an aged terminal note.
    /// mutation: drop the off-switch check. red: the operator apply answers 200 and settles the note.
    /// The dry run stays open and still writes nothing.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("operator-apply")]
    [Arguments("bare-dry-run")]
    public async Task C0747_Expire_apply_honours_the_off_switch(string arm, CancellationToken ct)
    {
        await SeedAsync(factory, ct);
        var before = await SnapshotAsync(factory, ct);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.TryAddWithoutValidation(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(factory.OperatorTokenPath));
        request.Content = JsonContent.Create(new { apply = arm == "operator-apply" });
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        if (arm == "operator-apply")
        {
            ((int)response.StatusCode).ShouldBe(409, body.ToString());
            body.GetProperty("code").GetString().ShouldBe("land_note_expiry_disabled");
        }
        else
        {
            ((int)response.StatusCode).ShouldBe(200, body.ToString());
            body.GetProperty("apply").GetBoolean().ShouldBeFalse();
        }
        (await SnapshotAsync(factory, ct, before.Keys)).ShouldBe(before, "the off switch settles nothing");
    }
}

/// <summary>CARD-0747 D-1: rows the expire-route witnesses seed and compare.</summary>
internal static class C0747ExpireSeed
{
    internal static async Task<(Guid Aged, Guid Young)> SeedAsync(AntiphonWebAppFactory factory, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(), DefinitionName = "c0747-caller", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Stopped,
            Cwd = "/tmp", Cols = 80, Rows = 24, CreatedAt = now, StartedAt = now, LastSeenAt = now,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync(ct);
        var aged = await AgentTaskLandReceiptTests.SeedAsync(db, session.Id);
        var young = await AgentTaskLandReceiptTests.SeedAsync(db, session.Id);
        foreach (var (note, days) in new[] { (aged, 8), (young, 6) })
        {
            note.State = LandNotificationState.DestinationUnavailable;
            note.LastErrorCode = "destination_failed";
            note.NextAttemptAt = now.AddMinutes(5);
            note.CreatedAt = now.AddDays(-days);
        }
        await db.SaveChangesAsync(ct);
        return (aged.Id, young.Id);
    }

    internal static async Task<string> SeedChildTaskTokenAsync(AntiphonWebAppFactory factory, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "c0747-child-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C0747 child", Goal = "a child task token is not the operator",
            WorkingDirectory = "/tmp", Status = AgentTaskStatus.Dispatched, CreatedAt = DateTime.UtcNow,
            TokenHash = AgentTaskService.HashToken(token),
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>
    /// Every land note, or only <paramref name="ids"/>. The real host's other background services may insert
    /// new notes for the seeded tasks while a test runs; the sweep only ever settles notes that already exist,
    /// so "writes nothing" compares every note present before the request.
    /// </summary>
    internal static async Task<Dictionary<Guid, (LandNotificationState State, string? Code)>> SnapshotAsync(
        AntiphonWebAppFactory factory, CancellationToken ct, IEnumerable<Guid>? ids = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = db.AgentTaskLandNotifications.AsNoTracking();
        if (ids is not null)
        {
            var keep = ids.ToList();
            query = query.Where(n => keep.Contains(n.Id));
        }
        var rows = await query.Select(n => new { n.Id, n.State, n.LastErrorCode }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.State, r.LastErrorCode));
    }
}

/// <summary>CARD-0747 D-1: the real Program without the hosted land-note reconcile, which would settle aged notes itself.</summary>
public class C0747ExpireWebAppFactory : AntiphonWebAppFactory
{
    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        var reconcile = services.Where(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(AgentTaskLandNotificationHostedService)).ToList();
        reconcile.Count.ShouldBe(1, "the hosted land-note reconcile must be registered exactly once");
        foreach (var descriptor in reconcile)
            services.Remove(descriptor);
    }
}

/// <summary>CARD-0747 D-1: the same host with the expiry off switch set (LandNoteReceiptExpiryDays=0).</summary>
public sealed class C0747ExpireDisabledWebAppFactory : C0747ExpireWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supervision:DeliveryVerification:LandNoteReceiptExpiryDays"] = "0",
        }));
    }
}
