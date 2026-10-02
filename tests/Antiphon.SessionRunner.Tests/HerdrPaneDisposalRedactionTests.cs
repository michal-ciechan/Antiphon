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

    private static void AssertV1Facts(HerdrPaneDisposalPreview p, Guid sessionId, string? instanceId, bool complete, string surface, string caseKey)
    {
        string Label(string field) => $"v1-{surface}-{field}-{caseKey}";
        p.PreviewId.ShouldNotBe(Guid.Empty, Label("preview-id"));
        p.ExpiresAtUtc.ShouldBe(FixedNow.AddMinutes(2), Label("expiry"));
        p.PaneId.ShouldBe("w1:p2", Label("pane-id"));
        p.ExpectedSessionId.ShouldBe(sessionId, Label("expected-session-id"));
        p.ExpectedNativeSessionId.ShouldBeNull(Label("expected-native-id"));
        p.WorkspaceId.ShouldBe("w1", Label("workspace-id"));
        p.TabId.ShouldBe("w1:t2", Label("tab-id"));
        p.TerminalId.ShouldBe("term_000000000002", Label("terminal-id"));
        p.BackendProtocol.ShouldBe(20, Label("backend-protocol"));
        p.BackendInstanceId.ShouldBe(instanceId, Label("backend-instance-id"));
        p.ShellPid.ShouldBe(4242, Label("shell-pid"));
        p.Shell.ShouldNotBeNull(Label("shell-present"));
        p.Shell!.Pid.ShouldBe(4242, Label("shell-record-pid"));
        p.Shell.ExecutableName.ShouldBe("pwsh.exe", Label("shell-name"));
        p.Shell.StartedAtUtc.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Label("shell-start"));
        p.Shell.ParentPid.ShouldBeNull(Label("shell-parent"));
        p.Shell.NativeSessionIds.ShouldBeNull(Label("shell-native-ids"));
        p.Foreground.ShouldBeEmpty(Label("foreground-empty"));
        p.AffectedProcesses.ShouldNotBeNull(Label("affected-present"));
        p.AffectedProcesses!.Count.ShouldBe(1, Label("affected-count"));
        p.AffectedProcesses[0].Pid.ShouldBe(4242, Label("affected-pid"));
        p.AffectedProcesses[0].ExecutableName.ShouldBe("pwsh.exe", Label("affected-name"));
        p.AffectedProcesses[0].StartedAtUtc.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Label("affected-start"));
        p.AffectedProcesses[0].ParentPid.ShouldBeNull(Label("affected-parent"));
        p.AffectedProcesses[0].NativeSessionIds.ShouldBeNull(Label("affected-native-ids"));
        p.Claims.Count.ShouldBe(1, Label("claims-count"));
        p.Claims[0].SessionId.ShouldBe(sessionId, Label("claim-session-id"));
        p.Claims[0].Live.ShouldBeFalse(Label("claim-live"));
        p.Claims[0].ChildPid.ShouldBeNull(Label("claim-child-pid"));
        p.Claims[0].ChildStartedAtUtc.ShouldBeNull(Label("claim-child-start"));
        p.WouldLeaveTabEmpty.ShouldBe(false, Label("would-leave-tab-empty"));
        p.Eligible.ShouldBe(complete, Label("eligible"));
        p.GuardAvailable.ShouldBeTrue(Label("guard-available"));
        p.ProcessInventoryComplete.ShouldBe(complete, Label("inventory-complete"));
        p.Blockers.ShouldBe(complete ? Array.Empty<string>() : [HerdrPaneDisposalCodes.IdentityUnproven], Label("blockers"));
        p.GuardMode.ShouldBe("antiphon-best-effort", Label("guard-mode"));
        p.AtomicClose.ShouldBeFalse(Label("atomic-close"));
        p.PlannedTerminationPids.ShouldBe(complete ? [4242] : Array.Empty<int>(), Label("planned-pids"));
    }

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
            var instanceId = (await w.Fixture.Client.ConnectAndValidateAsync(default)).InstanceId;
            var (post, get, disk, receipt) = await w.RoundTripAsync(deferPreviewPathSweep: true);
            w.AssertPreviewPaths(caseKey);
            get.PreviewId.ShouldBe(post.PreviewId, "v1-get-preview-id");
            disk.PreviewId.ShouldBe(post.PreviewId, "v1-disk-preview-id");
            if (value.Contains("c866-user", StringComparison.Ordinal)) w.AssertCanaryAbsent("c866-user", caseKey);
            foreach (var (p, surface) in new[] { (post, "post"), (get, "get"), (disk, "disk") })
            {
                p.WorkspaceLabel.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} workspace");
                p.TabLabel.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} tab");
                p.PaneLabel.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} pane");
                p.BackendVersion.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} backend");
                p.Claims.Count.ShouldBe(1, $"v1-{surface}-claims-count-{caseKey}");
                p.Claims[0].Source.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} source");
                p.Claims[0].Origin.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} origin");
                p.Claims[0].AgentKind.ShouldBe(Mask, $"display-field-masked {surface} {caseKey} kind");
                AssertV1Facts(p, w.Fixture.SessionId, instanceId, complete, surface, caseKey);
            }
            receipt.Outcome.ShouldBe(complete ? "Closed" : "Refused", "v1-receipt-outcome");
            receipt.Code.ShouldBe(complete ? "herdr_pane_closed" : HerdrPaneDisposalCodes.IdentityUnproven,
                "v1-receipt-code");
            receipt.PreviewId.ShouldBe(post.PreviewId, "v1-receipt-preview-id");
            receipt.PaneId.ShouldBe("w1:p2", "v1-receipt-pane-id");
            receipt.TerminalId.ShouldBe("term_000000000002", "v1-receipt-terminal-id");
            receipt.ExpectedSessionId.ShouldBe(w.Fixture.SessionId, "v1-receipt-session-id");
            receipt.ExpectedNativeSessionId.ShouldBeNull("v1-receipt-native-id");
            receipt.PaneLeftOpen.ShouldBe(complete ? false : null, "v1-receipt-pane-left-open");
            receipt.CleanupPending.ShouldBeFalse("v1-receipt-cleanup-pending");
            receipt.OperationId.ShouldNotBe(Guid.Empty, "v1-receipt-operation-id");
            receipt.ReplacementPresent.ShouldBeNull("v1-receipt-replacement-present");
            receipt.RecordedAtUtc.ShouldBe(FixedNow, "v1-receipt-recorded-at");
            w.Fixture.Backend.Closes.ShouldBe(complete ? 1 : 0);
        }
    }

    [Test][Arguments("windows")][Arguments("posix")][Arguments("unc")][Arguments("mixed")]
    public async Task Preview_projects_process_basenames_without_changing_identity(string style)
    {
        await using var w = new Wire();
        var started = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var nativeId = Guid.Parse("86600000-0000-4000-8000-000000000001");
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
                shell.StartedAtUtc, shell.Pid, [nativeId]);
            var worker = new HerdrPaneDisposalProcess(4244, PathName("affected-path-secret", "worker.exe"),
                shell.StartedAtUtc, foreground.Pid);
            return o with { Shell = shell, Foreground = [foreground], Affected = [shell, foreground, worker] };
        };
        await w.StartAsync();
        var (post, get, disk, receipt) = await w.RoundTripAsync(deferPreviewPathSweep: true);
        foreach (var (p, surface) in new[] { (post, "post"), (get, "get"), (disk, "disk") })
        {
            string Label(string field) => $"{surface}-{style}-{field}";
            p.Shell!.ExecutableName.ShouldNotContain("shell-path-secret", Case.Sensitive, Label("shell-path-excluded"));
            p.Foreground![0].ExecutableName.ShouldNotContain("foreground-path-secret", Case.Sensitive, Label("foreground-path-excluded"));
            p.AffectedProcesses![2].ExecutableName.ShouldNotContain("affected-path-secret", Case.Sensitive, Label("affected-path-excluded"));
            p.AffectedProcesses[0].ExecutableName.ShouldNotContain("shell-path-secret", Case.Sensitive, Label("affected-0-path-excluded"));
            p.AffectedProcesses[1].ExecutableName.ShouldNotContain("foreground-path-secret", Case.Sensitive, Label("affected-1-path-excluded"));
            p.Shell!.ExecutableName.ShouldBe("pwsh.exe", Label("shell-path-excluded"));
            p.Foreground![0].ExecutableName.ShouldBe("grok.exe", Label("foreground-path-excluded"));
            p.AffectedProcesses![2].ExecutableName.ShouldBe("worker.exe", Label("affected-path-excluded"));
            p.AffectedProcesses[0].ExecutableName.ShouldBe("pwsh.exe", Label("affected-0-path-excluded"));
            p.AffectedProcesses[1].ExecutableName.ShouldBe("grok.exe", Label("affected-1-path-excluded"));
            p.Shell.Pid.ShouldBe(4242, Label("process-facts-preserved shell pid"));
            p.Foreground[0].Pid.ShouldBe(4243, Label("process-facts-preserved foreground pid"));
            p.AffectedProcesses![0].Pid.ShouldBe(4242, Label("process-facts-preserved affected shell pid"));
            p.AffectedProcesses[1].Pid.ShouldBe(4243, Label("process-facts-preserved affected foreground pid"));
            p.AffectedProcesses[2].Pid.ShouldBe(4244, Label("process-facts-preserved worker pid"));
            p.Shell.ParentPid.ShouldBeNull(Label("process-facts-preserved shell parent"));
            p.Foreground[0].ParentPid.ShouldBe(4242, Label("process-facts-preserved foreground parent"));
            p.AffectedProcesses[0].ParentPid.ShouldBeNull(Label("process-facts-preserved affected shell parent"));
            p.AffectedProcesses[1].ParentPid.ShouldBe(4242, Label("process-facts-preserved affected foreground parent"));
            p.AffectedProcesses[2].ParentPid.ShouldBe(4243, Label("process-facts-preserved worker parent"));
            p.Shell.StartedAtUtc.ShouldBe(started, Label("process-facts-preserved shell start"));
            p.Foreground[0].StartedAtUtc.ShouldBe(started, Label("process-facts-preserved foreground start"));
            p.AffectedProcesses[0].StartedAtUtc.ShouldBe(started, Label("process-facts-preserved affected shell start"));
            p.AffectedProcesses[1].StartedAtUtc.ShouldBe(started, Label("process-facts-preserved affected foreground start"));
            p.AffectedProcesses[2].StartedAtUtc.ShouldBe(started, Label("process-facts-preserved worker start"));
            p.Shell.NativeSessionIds.ShouldBeNull(Label("process-facts-preserved shell native IDs"));
            p.Foreground[0].NativeSessionIds.ShouldBe([nativeId], Label("process-facts-preserved foreground native IDs"));
            p.AffectedProcesses[0].NativeSessionIds.ShouldBeNull(Label("process-facts-preserved affected shell native IDs"));
            p.AffectedProcesses[1].NativeSessionIds.ShouldBe([nativeId], Label("process-facts-preserved affected foreground native IDs"));
            p.AffectedProcesses[2].NativeSessionIds.ShouldBeNull(Label("process-facts-preserved worker native IDs"));
            p.Eligible.ShouldBeFalse(Label("raw-identity-still-refused"));
            p.PlannedTerminationPids.ShouldBeEmpty(Label("v2-planned-pids-empty"));
        }
        w.AssertPreviewPaths();
        receipt.Outcome.ShouldBe("Refused", "v2-receipt-refused"); w.Fixture.Backend.Closes.ShouldBe(0, "v2-zero-closes");
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
            receipt.Code.ShouldBe(outcome switch
            {
                "Closed" => "herdr_pane_closed",
                "Refused" => HerdrPaneDisposalCodes.IdentityUnproven,
                _ => HerdrPaneDisposalCodes.Unknown
            }, "v3-receipt-code");
            receipt.PreviewId.ShouldBe(post.PreviewId);
            disk.PreviewId.ShouldBe(post.PreviewId, "v3-disk-preview-id");
            var root = w.Fixture.Settings.SessionLogPath;
            var file = System.IO.Path.Combine(root, "herdr", "disposals", $"{receipt.OperationId:N}.json");
            var raw = await File.ReadAllTextAsync(file);
            raw.ShouldNotContain("secret-home", Case.Sensitive, "durable-review-path-excluded");
            raw.ShouldNotContain("reason-secret");
            using var before = JsonDocument.Parse(raw);
            var storedReceipt = before.RootElement.GetProperty("receipt").Deserialize<HerdrPaneDisposalReceipt>(Json)!;
            storedReceipt.OperationId.ShouldBe(receipt.OperationId, "v3-disk-operation-id");
            storedReceipt.PreviewId.ShouldBe(post.PreviewId, "v3-disk-receipt-preview-id");
            var fingerprint = before.RootElement.GetProperty("fingerprint").GetString();
            fingerprint.ShouldNotBeNullOrWhiteSpace();
            var closes = w.Fixture.Backend.Closes;
            w.Fixture.RecreateService();
            var again = await w.Fixture.Service.GetAsync(receipt.OperationId, default);
            again!.Outcome.ShouldBe(outcome);
            again.Code.ShouldBe(outcome switch
            {
                "Closed" => "herdr_pane_closed",
                "Refused" => HerdrPaneDisposalCodes.IdentityUnproven,
                _ => HerdrPaneDisposalCodes.Unknown
            }, "v3-recreated-code");
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
                p.TabLabel.ShouldBe(label, "safe-evidence-preserved tab");
                p.PaneLabel.ShouldBe(label, "safe-evidence-preserved pane");
                p.WorkspaceId.ShouldBe("w1", "safe-evidence-preserved workspace-id");
                p.TabId.ShouldBe("w1:t2", "safe-evidence-preserved tab-id");
                p.TerminalId.ShouldBe("term_000000000002", "safe-evidence-preserved terminal-id");
                p.ExpectedSessionId.ShouldBe(w.Fixture.SessionId, "safe-evidence-preserved session-id");
                p.Eligible.ShouldBeTrue("safe-evidence-preserved eligible");
                p.Shell!.ExecutableName.ShouldBe("pwsh.exe", "safe-evidence-preserved shell-name");
                p.PlannedTerminationPids.ShouldBe([4242], "safe-evidence-preserved planned-pids");
            }
            receipt.Outcome.ShouldBe("Closed");
            w.Fixture.Backend.Closes.ShouldBe(1, "safe-evidence-preserved close-count");
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
                p.Claims[0].Source.ShouldBe("antiphon-session-token", "safe-evidence-preserved claim source");
                p.Claims[0].SessionId.ShouldBe(occupied.Fixture.SessionId, "safe-evidence-preserved claim session");
                p.Claims[0].Live.ShouldBeFalse("safe-evidence-preserved claim live");
                p.Claims[0].ChildPid.ShouldBe(4243, "safe-evidence-preserved claim child pid");
                p.Claims[0].ChildStartedAtUtc.ShouldBe(occupied.Fixture.Processes.Started.AddSeconds(1), "safe-evidence-preserved claim child start");
                p.Claims[0].Origin.ShouldBe("attached"); p.Claims[0].AgentKind.ShouldBe("grok");
                p.Foreground![0].ExecutableName.ShouldBe("grok.exe");
                p.PlannedTerminationPids.ShouldBe([4242, 4243]);
            }
            receipt.Outcome.ShouldBe("Closed"); occupied.Fixture.Backend.Closes.ShouldBe(1);
        }
        await using var nullable = new Wire();
        nullable.Fixture.Fake.Workspaces[0].Tabs.Single(t => t.Panes.Any(p => p.PaneId == nullable.Fixture.PaneId))
            .Panes.Single(p => p.PaneId == nullable.Fixture.PaneId).Label = null;
        nullable.Fixture.Backend.Transform = o => o with
        {
            Claims = [new(nullable.Fixture.SessionId, "antiphon-session-token", null, false, null)],
            Shell = o.Shell! with { ExecutableName = null },
            Affected = [o.Shell! with { ExecutableName = null }],
            Foreground = null, Complete = false
        };
        await nullable.StartAsync();
        var (nPost, nGet, nDisk, _) = await nullable.RoundTripAsync();
        foreach (var p in new[] { nPost, nGet, nDisk })
        {
            p.PaneLabel.ShouldBeNull("v4-null-pane-label");
            p.Claims[0].Origin.ShouldBeNull("v4-null-claim-origin");
            p.Claims[0].AgentKind.ShouldBeNull("v4-null-claim-kind");
            p.Foreground.ShouldBeNull("v4-null-foreground");
            p.Shell!.ExecutableName.ShouldBeNull("v4-null-process-name");
            p.AffectedProcesses![0].ExecutableName.ShouldBeNull("v4-null-affected-name");
        }
        await using (var empty = new Wire())
        {
            empty.Fixture.Backend.Transform = o => o with { Foreground = [], Complete = false };
            await empty.StartAsync();
            var (post, get, disk, receipt) = await empty.RoundTripAsync();
            foreach (var p in new[] { post, get, disk })
                p.Foreground.ShouldBeEmpty("v4-empty-foreground-preserved");
            receipt.Code.ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven, "v4-empty-foreground-code");
            empty.Fixture.Backend.Closes.ShouldBe(0, "v4-empty-foreground-zero-closes");
        }
        foreach (var unsafeLeaf in new string?[] { null, "", "C:", @"C:\secret-home\", "/secret-home/", "bad\0.exe" })
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
    [Arguments("Review: notes")][Arguments("agent:claude")][Arguments("fix: retry")]
    [Arguments("build:1")][Arguments("CARD-0866 12:30")]
    [Arguments("Å:notes")][Arguments("Review： notes")]
    [Arguments("Review＼ notes")][Arguments("Review∕ notes")][Arguments("Review\u202e notes")]
    // A letter immediately before the candidate drive letter is indistinguishable from word-final text.
    [Arguments("xC:secret-home")]
    public async Task Redaction_preserves_non_drive_display_text(string value)
    {
        await AssertDisplayValueAsync(value, value, "preserved");
    }

    [Test]
    [Arguments(@"C:\secret-home\repo")][Arguments("C:/Users/c866-user/x")]
    [Arguments("C:secret-home")][Arguments(@" D:\x")][Arguments("\"E:\\x\"")]
    [Arguments(@"log at C:\secret-home")]
    [Arguments(" D:secret-home")][Arguments("\"E:secret-home\"")]
    [Arguments("log at C:secret-home")]
    [Arguments("=C:secret-home")][Arguments("(C:secret-home")]
    [Arguments("[C:secret-home")][Arguments("{C:secret-home")]
    [Arguments(",C:secret-home")][Arguments(";C:secret-home")]
    [Arguments("|C:secret-home")][Arguments(":C:secret-home")]
    [Arguments("<C:secret-home")][Arguments(">C:secret-home")]
    [Arguments("@C:secret-home")][Arguments("#C:secret-home")]
    [Arguments("-C:secret-home")][Arguments("_C:secret-home")]
    [Arguments(".C:secret-home")][Arguments("+C:secret-home")]
    [Arguments("*C:secret-home")][Arguments("!C:secret-home")]
    [Arguments("?C:secret-home")][Arguments("~C:secret-home")]
    [Arguments("`C:secret-home")][Arguments("&C:secret-home")]
    [Arguments("%C:secret-home")][Arguments("$C:secret-home")]
    [Arguments("^C:secret-home")][Arguments("éC:secret-home")]
    [Arguments("\u200bC:secret-home")][Arguments("（C:secret-home")]
    [Arguments("「C:secret-home")][Arguments("“C:secret-home")]
    [Arguments("‘C:secret-home")][Arguments("«C:secret-home")]
    [Arguments("x(C:secret-home)")][Arguments("x=C:secret-home")]
    [Arguments("path=C:secret-home")][Arguments("name:C:secret-home")]
    [Arguments("cwd=D:secret")][Arguments("--dir=C:secret-home")]
    [Arguments("work (C:secret-home) selected")][Arguments("\"(C:secret-home)\"")]
    [Arguments("file:C:secret")]
    // Separator-bearing variants remain whole-value masked regardless of the drive boundary.
    [Arguments(@"=C:\Users\secret-home")][Arguments("foo/C:/Users/secret-home")]
    [Arguments(@"name:C:\Users\secret-home")][Arguments(@"a,C:\secret-home")]
    public async Task Redaction_masks_drive_prefix_at_boundary(string value)
    {
        await AssertDisplayValueAsync(value, Mask, "drive-boundary");
    }

    private static async Task AssertDisplayValueAsync(string value, string expected, string rule)
    {
        await using var w = new Wire();
        w.SetLabels(value);
        w.Fixture.Backend.Transform = o => o with
        {
            Claims = [new(w.Fixture.SessionId, value, value, false, value)],
            Backend = o.Backend with { Version = value }
        };
        await w.StartAsync();
        var (post, get, disk, _) = await w.RoundTripAsync(deferPreviewPathSweep: true);
        foreach (var (preview, surface) in new[] { (post, "post"), (get, "get"), (disk, "disk") })
        {
            string Label(string field) => $"card-0892-{rule}-{surface}-{field}-{value}";
            preview.WorkspaceLabel.ShouldBe(expected, Label("workspace"));
            preview.TabLabel.ShouldBe(expected, Label("tab"));
            preview.PaneLabel.ShouldBe(expected, Label("pane"));
            preview.BackendVersion.ShouldBe(expected, Label("backend-version"));
            preview.Claims[0].Source.ShouldBe(expected, Label("claim-source"));
            preview.Claims[0].Origin.ShouldBe(expected, Label("claim-origin"));
            preview.Claims[0].AgentKind.ShouldBe(expected, Label("claim-agent-kind"));
        }
    }

    [Test]
    public async Task Redaction_does_not_promote_unproven_process_identity()
    {
        await using (var w = new Wire())
        {
            w.Fixture.Backend.Transform = o =>
            {
                var shell = o.Shell! with { ExecutableName = @"C:\identity-secret\pwsh.exe" };
                return o with { Shell = shell, Affected = o.Affected.Select(p => p.Pid == shell.Pid ? shell : p).ToArray() };
            };
            await w.StartAsync(); var (post, _, _, receipt) = await w.RoundTripAsync();
            post.Shell!.ExecutableName.ShouldBe("pwsh.exe");
            post.Eligible.ShouldBeFalse("raw-identity-still-refused");
            post.Blockers.ShouldBe([HerdrPaneDisposalCodes.IdentityUnproven], "v5-raw-shell-blocker");
            receipt.Outcome.ShouldBe("Refused");
            receipt.Code.ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven, "v5-raw-shell-code");
            w.Fixture.Backend.Closes.ShouldBe(0, "v5-raw-shell-zero-closes");
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
            post.Blockers.ShouldBe([HerdrPaneDisposalCodes.IdentityUnproven], "v5-raw-claim-blocker");
            post.Claims[0].AgentKind.ShouldBe(Mask); receipt.Outcome.ShouldBe("Refused");
            receipt.Code.ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven, "v5-raw-claim-code");
            w.Fixture.Backend.Closes.ShouldBe(0, "v5-raw-claim-zero-closes");
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
        foreach (var phase in new[] { "closed", "inspect-error", "close-error", "raw-payload" })
        {
            await using var w = new Wire(); w.SetLabels(@"C:\secret-home\repo");
            w.Fixture.Fake.Workspaces[0].Tabs.Single(t => t.Panes.Any(p => p.PaneId == w.Fixture.PaneId))
                .Panes.Single(p => p.PaneId == w.Fixture.PaneId).Cwd = @"C:\cwd-secret\repo";
            if (phase == "raw-payload") w.Fixture.Fake.SetPaneProcessInfo(w.Fixture.PaneId, 4242,
                new (int Pid, string Name, string[] Argv, string? Cwd)[]
                { (4243, "grok.exe", new[] { "grok", "argv-secret" }, @"C:\cwd-secret") });
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
            receipt.Outcome.ShouldBe(phase switch { "closed" => "Closed", "inspect-error" or "raw-payload" => "Refused", _ => "Unknown" });
            receipt.Code.ShouldBe(phase switch
            {
                "closed" => "herdr_pane_closed",
                "inspect-error" => HerdrProblemTypes.Unreachable,
                "raw-payload" => HerdrProblemTypes.PaneForeign,
                _ => HerdrPaneDisposalCodes.Unknown
            });
            var log = string.Join("\n", logger.Values);
            foreach (var canary in new[] { "secret-home", "reason-secret", "exception-path-secret", "cwd-secret", "argv-secret" })
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
        private string? _postJson;
        private string? _getJson;
        private string? _diskJson;
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
                string reason = "reviewed", bool deferPreviewPathSweep = false)
        {
            using var response = await _http!.PostAsJsonAsync("/herdr/pane-disposals/preview",
                new HerdrPaneDisposalPreviewRequest(Fixture.PaneId, Fixture.SessionId));
            response.EnsureSuccessStatusCode();
            var postJson = await response.Content.ReadAsStringAsync();
            var post = JsonSerializer.Deserialize<HerdrPaneDisposalPreview>(postJson, Json)!;
            post.PreviewId.ShouldNotBe(Guid.Empty, "post-preview-id");
            post.PaneId.ShouldBe(Fixture.PaneId, "post-pane-id");
            using var stored = await _http.GetAsync($"/herdr/pane-disposals/previews/{post.PreviewId}");
            stored.EnsureSuccessStatusCode(); var getJson = await stored.Content.ReadAsStringAsync();
            var get = JsonSerializer.Deserialize<HerdrPaneDisposalPreview>(getJson, Json)!;
            var previewReceiptDir = System.IO.Path.Combine(Fixture.Settings.SessionLogPath, "herdr", "disposals");
            Directory.Exists(previewReceiptDir).ShouldBeFalse("preview-does-not-persist");
            var op = Guid.NewGuid();
            using var execution = await _http.PostAsJsonAsync("/herdr/pane-disposals",
                new HerdrPaneDisposalRequest(op, post.PreviewId, reason, "antiphon-best-effort"));
            var executionJson = await execution.Content.ReadAsStringAsync();
            using var executionDoc = JsonDocument.Parse(executionJson);
            var executionReceipt = (execution.IsSuccessStatusCode ? executionDoc.RootElement
                : executionDoc.RootElement.GetProperty("receipt")).Deserialize<HerdrPaneDisposalReceipt>(Json)!;
            executionReceipt.OperationId.ShouldBe(op, "execution-operation-id");
            executionReceipt.PreviewId.ShouldBe(post.PreviewId, "execution-preview-id");
            executionJson.ShouldNotContain(reason == "reason-secret" ? "reason-secret" : "unused-canary");
            using var status = await _http.GetAsync($"/herdr/pane-disposals/{op}");
            status.EnsureSuccessStatusCode();
            var statusJson = await status.Content.ReadAsStringAsync();
            statusJson.ShouldNotContain(reason == "reason-secret" ? "reason-secret" : "unused-canary");
            var receipt = JsonSerializer.Deserialize<HerdrPaneDisposalReceipt>(statusJson, Json)!;
            receipt.OperationId.ShouldBe(op, "receipt-operation-id");
            if (executionReceipt.Outcome != "Unknown") receipt.ShouldBe(executionReceipt, "execution-status-receipt-equality");
            else
            {
                receipt.Outcome.ShouldBe("Unknown", "execution-status-unknown-outcome");
                receipt.Code.ShouldBe(HerdrPaneDisposalCodes.Unknown, "execution-status-unknown-code");
            }
            var file = System.IO.Path.Combine(Fixture.Settings.SessionLogPath, "herdr", "disposals", $"{op:N}.json");
            var diskJson = await File.ReadAllTextAsync(file);
            using var doc = JsonDocument.Parse(diskJson);
            var disk = doc.RootElement.GetProperty("reviewed").Deserialize<HerdrPaneDisposalPreview>(Json)!;
            var diskReceipt = doc.RootElement.GetProperty("receipt").Deserialize<HerdrPaneDisposalReceipt>(Json)!;
            diskReceipt.ShouldBe(receipt, "durable-receipt-equality");
            _postJson = postJson; _getJson = getJson; _diskJson = diskJson;
            if (!deferPreviewPathSweep) AssertPreviewPaths();
            AssertNoPath(executionJson, "execution-response-path-excluded");
            AssertNoPath(statusJson, "receipt-path-excluded");
            return (post, get, disk, receipt);
        }
        public void AssertPreviewPaths(string? caseKey = null)
        {
            AssertNoPath(_postJson!, $"path-label-excluded post {caseKey}");
            AssertNoPath(_getJson!, $"stored-preview-path-excluded {caseKey}");
            AssertNoPath(_diskJson!, $"durable-review-path-excluded {caseKey}");
        }
        public void AssertCanaryAbsent(string canary, string caseKey)
        {
            AssertCanary(_postJson!, canary, $"v1-post-canary-excluded-{caseKey}");
            AssertCanary(_getJson!, canary, $"v1-get-canary-excluded-{caseKey}");
            AssertCanary(_diskJson!, canary, $"v1-disk-canary-excluded-{caseKey}");
        }
        private static void AssertCanary(string json, string canary, string label) =>
            json.ShouldNotContain(canary, Case.Sensitive, label);
        private static void AssertNoPath(string json, string witness)
        {
            foreach (var canary in new[] { "secret-home", "c866-host", "private-share",
                "control-secret", "shell-path-secret", "foreground-path-secret", "affected-path-secret", "identity-secret", "stamp-a", "stamp-b" })
            {
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
