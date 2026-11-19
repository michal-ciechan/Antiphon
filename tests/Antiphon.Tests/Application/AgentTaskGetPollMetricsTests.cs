using System.Globalization;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1972 / CARD-2004. Counts <c>GET /api/agent-tasks/{id}</c> by a bounded caller label.
/// No database: the label comes from a registered header or the caller the route already resolved.
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
        metrics.Record("  useAgentTask  ", caller);
        metrics.Record("  Client  ", caller);

        var reading = metrics.Read();
        reading.Total.ShouldBe(4);
        reading.Unknown.ShouldBe(0);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[]
        {
            new("delegate.ps1", 2),
            new("kind:Grok", 1),
            new("useagenttask", 1),
        });
        var json = Json(reading);
        json.ShouldNotContain("SENTINEL-NAME");
        json.ShouldNotContain("client");
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
        metrics.Record("client", null);

        var reading = metrics.Read();
        reading.Total.ShouldBe(5);
        reading.Unknown.ShouldBe(5);
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
        const string github = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        const string key = "sk-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz";
        const string alnum = "abcdefghijKLMNOPQRST1234567890abcdefghij";
        const string b64 = "QWxhZGRpbjpvcGVuIHNlc2FtZQ";
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record(hex, Kind(AgentKind.Grok, "SENTINEL-UNSAFE"));
        metrics.Record(guid, null);
        metrics.Record(github, null);
        metrics.Record(key, null);
        metrics.Record(alnum, null);
        metrics.Record(b64, Kind(AgentKind.Grok, "SENTINEL-UNSAFE"));

        var reading = metrics.Read();
        reading.Total.ShouldBe(6);
        reading.Unknown.ShouldBe(4);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[] { new("kind:Grok", 2) });
        var json = Json(reading);
        json.ShouldNotContain(hex);
        json.ShouldNotContain(guid);
        json.ShouldNotContain(github);
        json.ShouldNotContain(github.ToLowerInvariant());
        json.ShouldNotContain(key);
        json.ShouldNotContain(key.ToLowerInvariant());
        json.ShouldNotContain(alnum);
        json.ShouldNotContain(alnum.ToLowerInvariant());
        json.ShouldNotContain(b64);
        json.ShouldNotContain(b64.ToLowerInvariant());
        json.ShouldNotContain("SENTINEL-UNSAFE");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1972_distinct_labels_stop_at_64()
    {
        var capabilityId = Guid.Parse("66666666-7777-4888-8999-aaaaaaaaaaaa");
        const string github = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        var metrics = new AgentTaskGetPollMetrics();
        for (var i = 0; i < AgentTaskGetPollMetrics.MaxLabels; i++)
        {
            metrics.Record(null, new AgentTaskService.Caller(
                null, null, "", capabilityId, "n" + i.ToString(CultureInfo.InvariantCulture)));
        }
        metrics.Record(null, new AgentTaskService.Caller(null, null, "", capabilityId, "n0"));
        metrics.Record(null, new AgentTaskService.Caller(null, null, "", capabilityId, "overflow-name"));
        metrics.Record(null, Kind(AgentKind.Grok, "SENTINEL-CAP"));
        metrics.Record(github, null);

        var reading = metrics.Read();
        reading.Callers.Count.ShouldBe(AgentTaskGetPollMetrics.MaxLabels + 1);
        reading.Callers.Single(row => row.Label == "capability:n0").Count.ShouldBe(2);
        reading.Callers.Any(row => row.Label == "capability:overflow-name").ShouldBeFalse();
        reading.Callers.Single(row => row.Label == "kind:Grok").Count.ShouldBe(1);
        reading.Overflow.ShouldBe(1);
        reading.Unknown.ShouldBe(1);
        reading.Total.ShouldBe(AgentTaskGetPollMetrics.MaxLabels + 4);
        var json = Json(reading);
        json.ShouldNotContain(github);
        json.ShouldNotContain(github.ToLowerInvariant());
        json.ShouldNotContain("overflow-name");
        json.ShouldNotContain("SENTINEL-CAP");
        json.ShouldNotContain(capabilityId.ToString());
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
        CountOf(detail, "polls.Record(").ShouldBe(1);

        var diagnostics = await File.ReadAllTextAsync(Path.Combine(root, "server", "Api", "Endpoints", "DiagnosticsEndpoints.cs"));
        diagnostics.ShouldContain("MapGet(AgentTaskGetPollMetrics.DiagnosticsPath");
        diagnostics.ShouldContain("polls.Read()");
        CountOf(diagnostics, "polls.Read()").ShouldBe(1);

        var program = await File.ReadAllTextAsync(Path.Combine(root, "server", "Program.cs"));
        program.ShouldContain("AddSingleton<AgentTaskGetPollMetrics>()");
    }

    [Test]
    public async Task C2004_registered_labels_count_and_other_headers_do_not()
    {
        const string github = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record("Delegate.ps1", null);
        metrics.Record("STATUS-SNAPSHOT.ps1", null);
        metrics.Record("Checkpoint-Task.ps1", null);
        metrics.Record("Card.ps1", null);
        metrics.Record("Board-Watch", null);
        metrics.Record("useAgentTask", null);
        metrics.Record("client", null);
        metrics.Record(github, null);

        AgentTaskGetPollMetrics.RegisteredLabels.OrderBy(label => label, StringComparer.Ordinal).ToArray().ShouldBe(
            new[]
            {
                "board-watch",
                "card.ps1",
                "checkpoint-task.ps1",
                "delegate.ps1",
                "status-snapshot.ps1",
                "useagenttask",
            });

        var reading = metrics.Read();
        reading.Total.ShouldBe(8);
        reading.Unknown.ShouldBe(2);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[]
        {
            new("board-watch", 1),
            new("card.ps1", 1),
            new("checkpoint-task.ps1", 1),
            new("delegate.ps1", 1),
            new("status-snapshot.ps1", 1),
            new("useagenttask", 1),
        });
        var json = Json(reading);
        json.ShouldNotContain("client");
        json.ShouldNotContain(github);
        json.ShouldNotContain(github.ToLowerInvariant());
        await Task.CompletedTask;
    }

    [Test]
    public async Task C2004_junk_headers_cannot_evict_server_labels()
    {
        var sessionId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var capabilityId = Guid.Parse("66666666-7777-4888-8999-aaaaaaaaaaaa");
        var metrics = new AgentTaskGetPollMetrics();
        for (var i = 0; i < AgentTaskGetPollMetrics.MaxLabels + 5; i++)
            metrics.Record("junk" + i.ToString(CultureInfo.InvariantCulture), null);
        foreach (var kind in Enum.GetValues<AgentKind>())
            metrics.Record(null, Kind(kind, "SENTINEL-JUNK"));
        metrics.Record(null, new AgentTaskService.Caller(null, sessionId, ""));
        metrics.Record(null, new AgentTaskService.Caller(null, null, "", capabilityId, "not a name"));

        var reading = metrics.Read();
        reading.Unknown.ShouldBe(AgentTaskGetPollMetrics.MaxLabels + 5);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[]
        {
            new("capability", 1),
            new("kind:ClaudeCode", 1),
            new("kind:Codex", 1),
            new("kind:Grok", 1),
            new("kind:OpenCode", 1),
            new("kind:Raw", 1),
            new("session", 1),
        });
        var json = Json(reading);
        json.ShouldNotContain("junk0");
        json.ShouldNotContain("junk" + (AgentTaskGetPollMetrics.MaxLabels + 4).ToString(CultureInfo.InvariantCulture));
        json.ShouldNotContain("SENTINEL-JUNK");
        json.ShouldNotContain(sessionId.ToString());
        json.ShouldNotContain(capabilityId.ToString());
        await Task.CompletedTask;
    }

    [Test]
    public async Task C2004_unknown_header_with_resolved_task_uses_kind()
    {
        var metrics = new AgentTaskGetPollMetrics();
        metrics.Record("unknown", Kind(AgentKind.Codex, "SENTINEL-UNKNOWN"));
        metrics.Record("Unknown", Kind(AgentKind.Codex, "SENTINEL-UNKNOWN"));

        var reading = metrics.Read();
        reading.Total.ShouldBe(2);
        reading.Unknown.ShouldBe(0);
        reading.Overflow.ShouldBe(0);
        reading.Callers.ShouldBe(new AgentTaskGetPollMetrics.CallerRow[] { new("kind:Codex", 2) });
        Json(reading).ShouldNotContain("SENTINEL-UNKNOWN");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C2004_list_route_is_not_counted()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var endpoints = await File.ReadAllTextAsync(Path.Combine(root, "server", "Api", "Endpoints", "AgentTaskEndpoints.cs"));
        CountOf(endpoints, "polls.Record(").ShouldBe(1);

        var listStart = endpoints.IndexOf("tasks.MapGet(\"/\",", StringComparison.Ordinal);
        listStart.ShouldBeGreaterThanOrEqualTo(0);
        var listEnd = endpoints.IndexOf("tasks.MapGet(\"/summary\"", listStart, StringComparison.Ordinal);
        listEnd.ShouldBeGreaterThan(listStart);
        var list = endpoints[listStart..listEnd];
        list.ShouldNotContain("polls.Record(");
        list.ShouldNotContain("AgentTaskGetPollMetrics");

        var detailStart = endpoints.IndexOf("tasks.MapGet(\"/{id}\",", StringComparison.Ordinal);
        detailStart.ShouldBeGreaterThanOrEqualTo(0);
        var detailEnd = endpoints.IndexOf("tasks.MapPost(\"/{id}/read\"", detailStart, StringComparison.Ordinal);
        CountOf(endpoints[detailStart..detailEnd], "polls.Record(").ShouldBe(1);
    }

    private static AgentTaskService.Caller Kind(AgentKind kind, string name) =>
        new(new AgentTask { AgentKind = kind, AgentName = name }, null, "");

    private static string Json(AgentTaskGetPollMetrics.Report reading) =>
        JsonSerializer.Serialize(reading);

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
