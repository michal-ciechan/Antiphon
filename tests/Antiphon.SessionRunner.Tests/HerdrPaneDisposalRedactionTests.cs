using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
public sealed class HerdrPaneDisposalRedactionTests
{
    private const string Mask = "[redacted]";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("windows")][Arguments("posix")][Arguments("windows-home-user")]
    [Arguments("posix-home-user")][Arguments("unc")][Arguments("long")]
    [Arguments("unicode-windows")][Arguments("unicode-posix")]
    [Arguments("embedded-backslash")][Arguments("embedded-slash")]
    [Arguments("drive-relative")][Arguments("mixed")][Arguments("control")]
    public async Task Preview_redacts_path_labels(string caseKey)
    {
        var value = caseKey switch
        {
            "windows" => @"C:\secret-home\repo",
            "posix" => "/secret-home/repo",
            "windows-home-user" => @"C:\Users\c866-user\.codex\secret-home",
            "posix-home-user" => "/home/c866-user/.claude/secret-home",
            "unc" => @"\\c866-host\private-share\secret-home\repo",
            "long" => @"C:\Users\c866-user\" + new string('x', 8192) + @"\secret-home\repo",
            "unicode-windows" => @"C:\Users\测试用户\秘密\secret-home",
            "unicode-posix" => "/home/δοκιμή/秘密/secret-home",
            "embedded-backslash" => @"status folder\secret-home ready",
            "embedded-slash" => "status folder/secret-home ready",
            "drive-relative" => "C:secret-home",
            "mixed" => @"C:/Users/c866-user\secret-home/repo",
            "control" => "control-secret\r\n\t\0",
            _ => throw new ArgumentOutOfRangeException(nameof(caseKey))
        };
        foreach (var label in new[] { value, $"work \"{value}\" selected" })
        foreach (var complete in new[] { true, false })
        {
            await using var w = new Wire();
            w.SetLabels(label);
            w.Fixture.Processes.Complete = complete;
            w.Fixture.Backend.Transform = o => o with
            {
                Claims = [new(w.Fixture.SessionId, label, label, false, label)],
                Backend = o.Backend with { Version = label }
            };
            await w.StartAsync();
            var (post, get, disk, receipt) = await w.RoundTripAsync();
            foreach (var p in new[] { post, get, disk })
            {
                p.WorkspaceLabel.ShouldBe(Mask, $"display-field-masked {caseKey} workspace");
                p.TabLabel.ShouldBe(Mask, $"display-field-masked {caseKey} tab");
                p.PaneLabel.ShouldBe(Mask, $"display-field-masked {caseKey} pane");
                p.BackendVersion.ShouldBe(Mask, $"display-field-masked {caseKey} backend");
                p.Claims[0].Source.ShouldBe(Mask, $"display-field-masked {caseKey} source");
                p.Claims[0].Origin.ShouldBe(Mask, $"display-field-masked {caseKey} origin");
                p.Claims[0].AgentKind.ShouldBe(Mask, $"display-field-masked {caseKey} kind");
                p.PaneId.ShouldBe(w.Fixture.PaneId);
                p.ExpectedSessionId.ShouldBe(w.Fixture.SessionId);
                p.Shell!.Pid.ShouldBe(4242);
                p.Eligible.ShouldBe(complete);
            }
            receipt.Outcome.ShouldBe(complete ? "Closed" : "Refused");
            w.Fixture.Backend.Closes.ShouldBe(complete ? 1 : 0);
        }
    }

    [Test][Arguments("windows")][Arguments("posix")][Arguments("unc")][Arguments("mixed")]
    public async Task Preview_projects_process_basenames_without_changing_identity(string style)
    {
        await using var w = new Wire();
        var prefix = style switch
        {
            "windows" => @"C:\",
            "posix" => "/",
            "unc" => @"\\host\",
            _ => @"C:/"
        };
        string PathName(string canary, string leaf) => style == "mixed"
            ? prefix + canary + @"\nested/" + leaf
            : prefix + canary + (style == "posix" ? "/" : @"\") + leaf;
        w.Fixture.Backend.Transform = o =>
        {
            var shell = o.Shell! with { ExecutableName = PathName("shell-path-secret", "pwsh.exe") };
            var foreground = new HerdrPaneDisposalProcess(4243, PathName("foreground-path-secret", "grok.exe"),
                shell.StartedAtUtc, shell.Pid);
            var worker = new HerdrPaneDisposalProcess(4244, PathName("affected-path-secret", "worker.exe"),
                shell.StartedAtUtc, foreground.Pid);
            return o with { Shell = shell, Foreground = [foreground], Affected = [shell, foreground, worker] };
        };
        await w.StartAsync();
        var (post, get, disk, receipt) = await w.RoundTripAsync(skipProcessPathSweep: true);
        foreach (var p in new[] { post, get, disk })
        {
            p.Shell!.ExecutableName.ShouldNotContain("shell-path-secret", Case.Sensitive, "shell-path-excluded");
            p.Foreground![0].ExecutableName.ShouldNotContain("foreground-path-secret", Case.Sensitive, "foreground-path-excluded");
            p.AffectedProcesses![2].ExecutableName.ShouldNotContain("affected-path-secret", Case.Sensitive, "affected-path-excluded");
            p.Shell!.ExecutableName.ShouldBe("pwsh.exe", "shell-path-excluded");
            p.Foreground![0].ExecutableName.ShouldBe("grok.exe", "foreground-path-excluded");
            p.AffectedProcesses![2].ExecutableName.ShouldBe("worker.exe", "affected-path-excluded");
            p.Shell.Pid.ShouldBe(4242); p.Foreground[0].Pid.ShouldBe(4243);
            p.AffectedProcesses[2].Pid.ShouldBe(4244);
            p.Eligible.ShouldBeFalse("raw-identity-still-refused");
            p.PlannedTerminationPids.ShouldBeEmpty();
        }
        receipt.Outcome.ShouldBe("Refused"); w.Fixture.Backend.Closes.ShouldBe(0);
    }

    [Test]
    public async Task Preview_and_stored_review_share_redaction()
    {
        foreach (var outcome in new[] { "Closed", "Refused", "Unknown" })
        {
            await using var w = new Wire();
            w.SetLabels(@"C:\secret-home\repo");
            w.Fixture.Processes.Complete = outcome != "Refused";
            w.Fixture.Backend.DropAfterClose = outcome == "Unknown";
            w.Fixture.Processes.Alive = outcome == "Unknown" ? null : false;
            await w.StartAsync();
            var (post, get, disk, receipt) = await w.RoundTripAsync("reason-secret");
            foreach (var p in new[] { post, get, disk }) p.WorkspaceLabel.ShouldBe(Mask, "stored-preview-path-excluded");
            receipt.Outcome.ShouldBe(outcome);
            receipt.PreviewId.ShouldBe(post.PreviewId);
            var root = w.Fixture.Settings.SessionLogPath;
            var file = System.IO.Path.Combine(root, "herdr", "disposals", $"{receipt.OperationId:N}.json");
            var raw = await File.ReadAllTextAsync(file);
            raw.ShouldNotContain("secret-home", Case.Sensitive, "durable-review-path-excluded");
            raw.ShouldNotContain("reason-secret");
            using var before = JsonDocument.Parse(raw);
            var fingerprint = before.RootElement.GetProperty("fingerprint").GetString();
            fingerprint.ShouldNotBeNullOrWhiteSpace();
            var closes = w.Fixture.Backend.Closes;
            w.Fixture.RecreateService();
            var again = await w.Fixture.Service.GetAsync(receipt.OperationId, default);
            again!.Outcome.ShouldBe(outcome);
            var retry = await w.Fixture.Service.ExecuteAsync(
                new(receipt.OperationId, post.PreviewId, "reason-secret", "antiphon-best-effort"), default);
            retry.ShouldBe(again);
            using var after = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            after.RootElement.GetProperty("fingerprint").GetString().ShouldBe(fingerprint);
            w.Fixture.Backend.Closes.ShouldBe(0);
            closes.ShouldBe(outcome == "Refused" ? 0 : 1);
        }
    }

    [Test]
    public async Task Redaction_preserves_safe_display_and_nulls()
    {
        foreach (var label in new[] { "grok.exe", "", "Review α 日本語", "c866-user" })
        {
            await using var w = new Wire(); w.SetLabels(label); await w.StartAsync();
            var (post, get, disk, receipt) = await w.RoundTripAsync();
            foreach (var p in new[] { post, get, disk })
            {
                p.WorkspaceLabel.ShouldBe(label, "safe-evidence-preserved");
                p.TabLabel.ShouldBe(label); p.PaneLabel.ShouldBe(label);
                p.Shell!.ExecutableName.ShouldBe("pwsh.exe");
                p.PlannedTerminationPids.ShouldBe([4242]);
            }
            receipt.Outcome.ShouldBe("Closed");
        }
        await using (var occupied = new Wire())
        {
            occupied.Fixture.Occupied(native: false);
            occupied.Fixture.Backend.Transform = o => o with
            {
                Claims = [new(occupied.Fixture.SessionId, "antiphon-session-token", "attached", false,
                    "grok", 4243, occupied.Fixture.Processes.Started.AddSeconds(1))]
            };
            await occupied.StartAsync();
            var (post, get, disk, receipt) = await occupied.RoundTripAsync();
            foreach (var p in new[] { post, get, disk })
            {
                p.Eligible.ShouldBeTrue("safe-evidence-preserved");
                p.BackendVersion.ShouldBe("0.8.2");
                p.Claims[0].Source.ShouldBe("antiphon-session-token");
                p.Claims[0].Origin.ShouldBe("attached"); p.Claims[0].AgentKind.ShouldBe("grok");
                p.Foreground![0].ExecutableName.ShouldBe("grok.exe");
                p.PlannedTerminationPids.ShouldBe([4242, 4243]);
            }
            receipt.Outcome.ShouldBe("Closed"); occupied.Fixture.Backend.Closes.ShouldBe(1);
        }
        await using var nullable = new Wire();
        nullable.Fixture.Fake.Workspaces[0].Tabs[0].Panes[0].Label = null;
        nullable.Fixture.Backend.Transform = o => o with
        {
            Claims = [new(nullable.Fixture.SessionId, "antiphon-session-token", null, false, null)],
            Foreground = null, Complete = false
        };
        await nullable.StartAsync();
        var (nPost, nGet, nDisk, _) = await nullable.RoundTripAsync();
        foreach (var p in new[] { nPost, nGet, nDisk })
        {
            p.PaneLabel.ShouldBeNull(); p.Claims[0].Origin.ShouldBeNull(); p.Claims[0].AgentKind.ShouldBeNull();
            p.Foreground.ShouldBeNull();
        }
        foreach (var unsafeLeaf in new[] { "", "C:", @"C:\secret-home\", "/secret-home/", "bad\0.exe" })
        {
            await using var w = new Wire();
            w.Fixture.Backend.Transform = o =>
            {
                var shell = o.Shell! with { ExecutableName = unsafeLeaf };
                var child = new HerdrPaneDisposalProcess(4243, unsafeLeaf, w.Fixture.Processes.Started.AddSeconds(1), 4242);
                return o with { Shell = shell, Foreground = [child], Affected = [shell, child] };
            };
            await w.StartAsync();
            var (post, get, disk, receipt) = await w.RoundTripAsync();
            foreach (var p in new[] { post, get, disk })
            {
                p.Shell!.ExecutableName.ShouldBeNull("unsafe-leaf-null");
                p.Foreground![0].ExecutableName.ShouldBeNull("unsafe-leaf-null");
                p.AffectedProcesses![0].ExecutableName.ShouldBeNull("unsafe-leaf-null");
                p.AffectedProcesses[1].ExecutableName.ShouldBeNull("unsafe-leaf-null");
            }
            receipt.Outcome.ShouldBe("Refused");
        }
    }

    [Test]
    public async Task Redaction_does_not_promote_unproven_process_identity()
    {
        await using (var w = new Wire())
        {
            w.Fixture.Backend.Transform = o => o with { Shell = o.Shell! with { ExecutableName = @"C:\identity-secret\pwsh.exe" } };
            await w.StartAsync(); var (post, _, _, receipt) = await w.RoundTripAsync();
            post.Shell!.ExecutableName.ShouldBe("pwsh.exe");
            post.Eligible.ShouldBeFalse("raw-identity-still-refused");
            receipt.Outcome.ShouldBe("Refused"); w.Fixture.Backend.Closes.ShouldBe(0);
        }
        await using (var w = new Wire())
        {
            w.Fixture.Occupied(native: false);
            w.Fixture.Backend.Transform = o => o with { Claims = [new(w.Fixture.SessionId,
                @"folder\claim", @"folder\origin", false, "grok", 4243, w.Fixture.Processes.Started.AddSeconds(1))] };
            await w.StartAsync(); var (post, _, _, receipt) = await w.RoundTripAsync();
            post.Eligible.ShouldBeTrue("raw-claim-exact-positive");
            post.Claims[0].Source.ShouldBe(Mask); post.Claims[0].Origin.ShouldBe(Mask);
            receipt.Outcome.ShouldBe("Closed"); w.Fixture.Backend.Closes.ShouldBe(1);
        }
        await using (var w = new Wire())
        {
            w.Fixture.Occupied(native: false);
            w.Fixture.Backend.Transform = o => o with { Claims = [new(w.Fixture.SessionId, @"folder\claim", @"folder\origin", false,
                "folder/grok", 4243, w.Fixture.Processes.Started.AddSeconds(1))] };
            await w.StartAsync(); var (post, _, _, receipt) = await w.RoundTripAsync();
            post.Eligible.ShouldBeFalse("raw-claim-kind-not-normalized");
            post.Claims[0].AgentKind.ShouldBe(Mask); receipt.Outcome.ShouldBe("Refused");
        }
        await using (var w = new Wire())
        {
            w.Fixture.Backend.Transform = o =>
            {
                var raw = w.Fixture.Backend.Inspections == 1 ? @"C:\stamp-a\pwsh.exe" : @"C:\stamp-b\pwsh.exe";
                return o with { Affected = [o.Shell! with { ExecutableName = raw }] };
            };
            await w.StartAsync(); var (post, _, _, receipt) = await w.RoundTripAsync();
            post.Eligible.ShouldBeTrue(); post.AffectedProcesses![0].ExecutableName.ShouldBe("pwsh.exe");
            receipt.Outcome.ShouldBe("Refused", "raw-stamp-changed");
            receipt.Code.ShouldBe(HerdrProblemTypes.PaneChanged); w.Fixture.Backend.Closes.ShouldBe(0);
        }
    }

    [Test]
    public async Task Structured_logs_omit_display_paths_and_reason()
    {
        foreach (var phase in new[] { "closed", "inspect-error", "close-error" })
        {
            await using var w = new Wire(); w.SetLabels(@"C:\secret-home\repo");
            var logger = new CaptureLogger();
            // The service's internal injection seam records formatted, structured and exception data.
            w.ServiceOverride = new HerdrPaneDisposalService(w.Fixture.Runtime, w.Fixture.Settings.SessionLogPath,
                w.Fixture.Clock, w.Fixture.Backend, logger: logger);
            if (phase == "inspect-error") w.Fixture.Backend.BeforeInspect = n =>
                n == 2 ? Task.FromException(new IOException("exception-path-secret")) : Task.CompletedTask;
            if (phase == "close-error") w.Fixture.Backend.BeforeClose = () =>
                Task.FromException(new IOException("exception-path-secret"));
            await w.StartAsync();
            var (_, _, _, receipt) = await w.RoundTripAsync("reason-secret");
            receipt.Outcome.ShouldBe(phase switch { "closed" => "Closed", "inspect-error" => "Refused", _ => "Unknown" });
            receipt.Code.ShouldBe(phase switch
            {
                "closed" => "herdr_pane_closed",
                "inspect-error" => HerdrProblemTypes.Unreachable,
                _ => HerdrPaneDisposalCodes.Unknown
            });
            var log = string.Join("\n", logger.Values);
            foreach (var canary in new[] { "secret-home", "reason-secret", "exception-path-secret" })
                log.ShouldNotContain(canary, Case.Sensitive, "disposal-log-payload-excluded");
            log.ShouldContain(receipt.OperationId.ToString(), Case.Insensitive);
        }
    }

    private sealed class Wire : IAsyncDisposable
    {
        public HerdrPaneDisposalFixture Fixture { get; } = new();
        public HerdrPaneDisposalService? ServiceOverride { get; set; }
        private WebApplication? _app;
        private HttpClient? _http;
        public void SetLabels(string? label)
        {
            var ws = Fixture.Fake.Workspaces[0]; ws.Label = label!;
            var tab = ws.Tabs.Single(t => t.Panes.Any(p => p.PaneId == Fixture.PaneId));
            tab.Label = label!;
            tab.Panes.Single(p => p.PaneId == Fixture.PaneId).Label = label;
        }
        public async Task StartAsync()
        {
            Fixture.Clock.Now = FixedNow;
            await Fixture.StartAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
            builder.Services.AddSingleton(ServiceOverride ?? Fixture.Service);
            _app = builder.Build(); _app.MapHerdrPaneDisposalRoutes(); await _app.StartAsync();
            _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
        }
        public async Task<(HerdrPaneDisposalPreview Post, HerdrPaneDisposalPreview Get,
            HerdrPaneDisposalPreview Disk, HerdrPaneDisposalReceipt Receipt)> RoundTripAsync(
                string reason = "reviewed", bool skipProcessPathSweep = false)
        {
            using var response = await _http!.PostAsJsonAsync("/herdr/pane-disposals/preview",
                new HerdrPaneDisposalPreviewRequest(Fixture.PaneId, Fixture.SessionId));
            response.EnsureSuccessStatusCode();
            var postJson = await response.Content.ReadAsStringAsync();
            var post = JsonSerializer.Deserialize<HerdrPaneDisposalPreview>(postJson, Json)!;
            using var stored = await _http.GetAsync($"/herdr/pane-disposals/previews/{post.PreviewId}");
            stored.EnsureSuccessStatusCode(); var getJson = await stored.Content.ReadAsStringAsync();
            var get = JsonSerializer.Deserialize<HerdrPaneDisposalPreview>(getJson, Json)!;
            var op = Guid.NewGuid();
            using var execution = await _http.PostAsJsonAsync("/herdr/pane-disposals",
                new HerdrPaneDisposalRequest(op, post.PreviewId, reason, "antiphon-best-effort"));
            var executionJson = await execution.Content.ReadAsStringAsync();
            executionJson.ShouldNotContain(reason == "reason-secret" ? "reason-secret" : "unused-canary");
            using var status = await _http.GetAsync($"/herdr/pane-disposals/{op}");
            status.EnsureSuccessStatusCode();
            var statusJson = await status.Content.ReadAsStringAsync();
            statusJson.ShouldNotContain(reason == "reason-secret" ? "reason-secret" : "unused-canary");
            var receipt = JsonSerializer.Deserialize<HerdrPaneDisposalReceipt>(statusJson, Json)!;
            var file = System.IO.Path.Combine(Fixture.Settings.SessionLogPath, "herdr", "disposals", $"{op:N}.json");
            var diskJson = await File.ReadAllTextAsync(file);
            using var doc = JsonDocument.Parse(diskJson);
            var disk = doc.RootElement.GetProperty("reviewed").Deserialize<HerdrPaneDisposalPreview>(Json)!;
            AssertNoPath(postJson, "path-label-excluded post", skipProcessPathSweep);
            AssertNoPath(getJson, "stored-preview-path-excluded", skipProcessPathSweep);
            AssertNoPath(executionJson, "execution-response-path-excluded", skipProcessPathSweep);
            AssertNoPath(statusJson, "receipt-path-excluded", skipProcessPathSweep);
            AssertNoPath(diskJson, "durable-review-path-excluded", skipProcessPathSweep);
            return (post, get, disk, receipt);
        }
        private static void AssertNoPath(string json, string witness, bool skipProcessPathSweep)
        {
            foreach (var canary in new[] { "secret-home", "c866-host", "private-share",
                "control-secret", "shell-path-secret", "foreground-path-secret", "affected-path-secret", "identity-secret", "stamp-a", "stamp-b" })
            {
                if (skipProcessPathSweep && canary is "shell-path-secret" or "foreground-path-secret" or "affected-path-secret")
                    continue;
                json.ShouldNotContain(canary, Case.Sensitive, witness);
            }
        }
        public async ValueTask DisposeAsync()
        {
            _http?.Dispose(); if (_app is not null) await _app.DisposeAsync(); await Fixture.DisposeAsync();
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Values { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Values.Add(formatter(state, exception));
            Values.Add(exception?.ToString() ?? "");
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                Values.AddRange(fields.Select(f => f.Value?.ToString() ?? ""));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
