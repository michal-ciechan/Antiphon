using System.Globalization;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1972. Counts <c>GET /api/agent-tasks/{id}</c> by a bounded caller label.
/// No database: the label comes from the header or the caller the route already resolved.
/// </summary>
[Category("Unit")]
public class AgentTaskGetPollMetricsTests
{
    [Test]
    public async Task C1972_script_header_is_counted_ahead_of_agent_kind()
    {
        var metrics = new AgentTaskGetPollMetrics();
        var caller = Kind(AgentKind.Grok, "SENTINEL-NAME");
        metrics.Record("Delegate.ps1", caller);
        metrics.Record("delegate.ps1", caller);
        metrics.Record("  Client  ", caller);

        var reading = metrics.Read();
        reading.Total.ShouldBe(3);
        reading.Unknown.ShouldBe(0);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[]
        {
            new("delegate.ps1", 2),
            new("client", 1),
        });
        Json(reading).ShouldNotContain("SENTINEL-NAME");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_task_token_uses_agent_kind()
    {
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record(null, Kind(AgentKind.Grok, "SENTINEL-GROK"));
        metrics.Record("   ", Kind(AgentKind.Codex, "SENTINEL-CODEX"));

        var reading = metrics.Read();
        reading.Total.ShouldBe(2);
        reading.Unknown.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[]
        {
            new("kind:Codex", 1),
            new("kind:Grok", 1),
        });
        var json = Json(reading);
        json.ShouldNotContain("SENTINEL-GROK");
        json.ShouldNotContain("SENTINEL-CODEX");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_absent_identity_is_unknown()
    {
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record(null, null);
        metrics.Record(null, new AgentTaskService.Caller(null, null, ""));
        metrics.Record("unknown", null);
        metrics.Record("../secret", null);

        var reading = metrics.Read();
        reading.Total.ShouldBe(4);
        reading.Unknown.ShouldBe(4);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBeEmpty();
        reading.StartedAt.ShouldBeGreaterThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(-2));
        reading.StartedAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(1));
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_unsafe_header_is_not_a_label()
    {
        const string hex = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string guid = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record(hex, Kind(AgentKind.Grok, "SENTINEL-UNSAFE"));
        metrics.Record(guid, null);

        var reading = metrics.Read();
        reading.Total.ShouldBe(2);
        reading.Unknown.ShouldBe(1);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[] { new("kind:Grok", 1) });
        var json = Json(reading);
        json.ShouldNotContain(hex);
        json.ShouldNotContain(guid);
        json.ShouldNotContain("SENTINEL-UNSAFE");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_distinct_labels_stop_at_64()
    {
        var metrics = new AgentTaskGetPollMetrics();
        for (var i = 0; i < AgentTaskGetPollMetrics.MaxLabels; i++)
            metrics.Record("s" + i.ToString(CultureInfo.InvariantCulture), null);
        metrics.Record("s0", null);
        metrics.Record("overflow-label", null);
        metrics.Record(null, null);

        var reading = metrics.Read();
        reading.Callers.Count.ShouldBe(AgentTaskGetPollMetrics.MaxLabels);
        reading.Callers.Single(row => row.Label == "s0").Count.ShouldBe(2);
        reading.Callers.Any(row => row.Label == "overflow-label").ShouldBeFalse();
        reading.Overflow.ShouldBe(1);
        reading.Unknown.ShouldBe(1);
        reading.Total.ShouldBe(AgentTaskGetPollMetrics.MaxLabels + 3);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_session_and_capability_are_bounded_labels()
    {
        var sessionId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var capabilityId = Guid.Parse("66666666-7777-4888-8999-aaaaaaaaaaaa");
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record(null, new AgentTaskService.Caller(null, sessionId, ""));
        metrics.Record(null, new AgentTaskService.Caller(null, null, "", capabilityId, "Nightly.ps1"));
        metrics.Record(null, new AgentTaskService.Caller(null, null, "", capabilityId, "not a name"));

        var reading = metrics.Read();
        reading.Total.ShouldBe(3);
        reading.Unknown.ShouldBe(0);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[]
        {
            new("capability", 1),
            new("capability:nightly.ps1", 1),
            new("session", 1),
        });
        var json = Json(reading);
        json.ShouldNotContain(sessionId.ToString());
        json.ShouldNotContain(capabilityId.ToString());
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_detail_get_and_diagnostics_route_are_wired()
    {
        AgentTaskGetPollMetrics.CallerHeader.ShouldBe("X-Antiphon-Caller");
        AgentTaskGetPollMetrics.DiagnosticsPath.ShouldBe("/agent-task-polls");
        AgentTaskGetPollMetrics.UnknownLabel.ShouldBe("unknown");
        AgentTaskGetPollMetrics.MaxLabels.ShouldBe(64);

        var root = DelegateScriptRunner.RepoRoot;
        var endpoints = await File.ReadAllTextAsync(Path.Combine(root, "server", "Api", "Endpoints", "AgentTaskEndpoints.cs"));
        var start = endpoints.IndexOf("tasks.MapGet(\"/{id}\",", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = endpoints.IndexOf("tasks.MapPost(\"/{id}/read\"", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        var detail = endpoints[start..end];
        detail.ShouldContain("polls.Record(");
        detail.ShouldContain("AgentTaskGetPollMetrics.CallerHeader");
        detail.Replace("polls.Record(", "polls.Skip(", StringComparison.Ordinal)
            .ShouldNotContain("polls.Record(");

        var diagnostics = await File.ReadAllTextAsync(Path.Combine(root, "server", "Api", "Endpoints", "DiagnosticsEndpoints.cs"));
        diagnostics.ShouldContain("MapGet(AgentTaskGetPollMetrics.DiagnosticsPath");
        diagnostics.ShouldContain("polls.Read()");
        diagnostics.Replace("polls.Read()", "polls.Empty()", StringComparison.Ordinal)
            .ShouldNotContain("polls.Read()");

        var program = await File.ReadAllTextAsync(Path.Combine(root, "server", "Program.cs"));
        program.ShouldContain("AddSingleton<AgentTaskGetPollMetrics>()");
    }

    private static AgentTaskService.Caller Kind(AgentKind kind, string name) =>
        new(new AgentTask { AgentKind = kind, AgentName = name }, null, "");

    private static string Json(AgentTaskGetPollMetrics.Report reading) =>
        JsonSerializer.Serialize(reading);
}
