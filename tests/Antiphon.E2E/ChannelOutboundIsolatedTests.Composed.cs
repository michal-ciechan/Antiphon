using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.E2E;

public sealed partial class ChannelOutboundIsolatedTests
{
    [Test]
    // F-5 covers outbound admission after a source task settles. It does not exercise
    // ChannelReplyDispatcher automatic routing, a second conversation, or tool failure;
    // the full V-24 case remains pending.
    public async Task Admitted_four_sources_convert_and_publish_after_host_restart()
    {
        RequireOptIn();
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException("The owned container browser wrapper is Linux-only.");
        var browserImage = await BuildBrowserImageAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = deadline.Token;
        var checkout = AntiphonAppFixture.FindRepositoryRoot();
        var root = Path.Combine(checkout, ".antiphon", "test-output", "card-0418", "f5", Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        var converter = Path.Combine(root, "converter");
        var native = Path.Combine(root, "native");
        var storeRoot = Path.Combine(root, "outbound");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(converter);
        var relative = new[]
        {
            "docs/features/001-kalshi-ref-data-downloader/01-requirements.md",
            "docs/features/001-kalshi-ref-data-downloader/03-design.md",
            "docs/features/001-kalshi-ref-data-downloader/04-external-api.md",
            "docs/features/001c-kalshi-current-first-snapshots/04-external-api.md",
        };
        var specimen = Path.Combine(checkout, "tests", "Antiphon.Tests", "Fixtures", "Card0418");
        var fixtureNames = new[] { "requirements.md", "design.md", "external-api.md", "current-snapshots.md" };
        var expected = new byte[4][];
        for (var i = 0; i < relative.Length; i++)
        {
            expected[i] = await File.ReadAllBytesAsync(Path.Combine(specimen, fixtureNames[i]), ct);
            var path = Path.Combine(repo, relative[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, expected[i], ct);
        }
        await GitAsync(repo, ct, "init", "-b", "main");
        await GitAsync(repo, ct, "add", ".");
        await GitAsync(repo, ct, "-c", "user.name=F5 Fixture", "-c", "user.email=f5@example.invalid",
            "commit", "-m", "four synthetic sources");
        var report = Path.Combine(root, "source-report.md");
        await File.WriteAllTextAsync(report,
            "Four synthetic source documents completed.\n" + string.Join('\n', relative) + "\n", ct);
        await File.WriteAllTextAsync(Path.Combine(converter, "convert.md"),
            "Read the frozen request and render all sourceFiles with the optional Markdown PDF tool.", ct);

        var fakeDll = Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.dll");
        var toolDll = Path.Combine(AppContext.BaseDirectory, "Antiphon.MarkdownPdf.dll");
        File.Exists(fakeDll).ShouldBeTrue();
        File.Exists(toolDll).ShouldBeTrue();
        var fakeExe = Path.Combine(root, "fakegrok-cli");
        await File.WriteAllTextAsync(fakeExe, "#!/bin/sh\nexec dotnet '" + ShellQuote(fakeDll) + "' \"$@\"\n", ct);
        File.SetUnixFileMode(fakeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var browser = Path.Combine(root, "fixture-chromium");
        await File.WriteAllTextAsync(browser, "#!/bin/sh\nexec docker run --rm --network none -v /tmp:/tmp -v '"
            + ShellQuote(root) + ":" + ShellQuote(root)
            + "' --entrypoint /usr/bin/chromium "
            + browserImage + " --no-sandbox \"$@\"\n", ct);
        File.SetUnixFileMode(browser, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var gate = Path.Combine(root, "converter-gate");
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var sourceAgentId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var topic = "c0418-f5-" + Guid.NewGuid().ToString("N");
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await broker.StartAsync(ct);
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = broker.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        await using var fakeSlack = new FakeSlackServer();
        await fakeSlack.StartAsync();
        using var slackHttp = new HttpClient();
        var slack = new SlackChannelAdapter(slackHttp, new SlackSettings
        {
            ApiBaseUrl = fakeSlack.ApiBaseUrl, BotToken = fakeSlack.BotToken,
            AppToken = fakeSlack.AppToken, ErrorBackoffSeconds = 0,
        }, NullLogger<SlackChannelAdapter>.Instance);
        var gateway = new GatewayOutboundService([slack], Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = broker.GetBootstrapAddress(), OutboundTopic = topic,
            ConsumerGroup = "c0418-f5-gateway-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = "Earliest",
        }), NullLogger<GatewayOutboundService>.Instance);
        await gateway.StartAsync(ct);
        var app = new AntiphonAppFixture
        {
            UseModernPty = true,
            DiagnosticsDirectory = Path.Combine(root, "logs"),
            ConfigureOwnedHost = settings =>
            {
                settings["Agents:DefaultDefinition"] = "f5-grok";
                settings["Agents:GrokCredentialProbeEnabled"] = "false";
                settings["Agents:Definitions:f5-grok:Kind"] = "Grok";
                settings["Agents:Definitions:f5-grok:Exe"] = fakeExe;
                settings["Agents:Definitions:f5-grok:Env:GROK_HOME"] = native;
                settings["Agents:Definitions:f5-grok:Env:ANTIPHON_FAKE_REPORT_LINE"] = "1";
                settings["Agents:Definitions:f5-grok:Env:ANTIPHON_FAKE_SOURCE_REPORT"] = report;
                settings["Agents:Definitions:f5-grok:Env:ANTIPHON_FAKE_OUTBOUND_TOOL"] = toolDll;
                settings["Agents:Definitions:f5-grok:Env:ANTIPHON_FAKE_OUTBOUND_BROWSER"] = browser;
                settings["Agents:Definitions:f5-grok:Env:ANTIPHON_FAKE_OUTBOUND_TOOL_GATE"] = gate;
                var names = new[] { "GROK_HOME", "ANTIPHON_FAKE_REPORT_LINE", "ANTIPHON_FAKE_SOURCE_REPORT",
                    "ANTIPHON_FAKE_OUTBOUND_TOOL", "ANTIPHON_FAKE_OUTBOUND_BROWSER", "ANTIPHON_FAKE_OUTBOUND_TOOL_GATE" };
                for (var i = 0; i < names.Length; i++)
                    settings[$"Agents:Definitions:f5-grok:NonSecretEnvironmentNames:{i}"] = names[i];
                settings["Delegation:CheckInterpreterEnabled"] = "false";
                settings["Delegation:DiagnoseEnabled"] = "false";
                settings["Delegation:OutputDistillerEnabled"] = "false";
                settings["ChannelBridge:Enabled"] = "false";
                settings["Hangfire:ServerEnabled"] = "false";
                settings["AntiphonMessaging:BootstrapServers"] = broker.GetBootstrapAddress();
                settings["AntiphonMessaging:OutboundTopic"] = topic;
                settings["ChannelOutbound:Profiles:pdf:ProjectId"] = projectId.ToString("D");
                settings["ChannelOutbound:Profiles:pdf:AgentId"] = converterId.ToString("D");
                settings["ChannelOutbound:Profiles:pdf:PromptFile"] = "convert.md";
                settings["ChannelOutbound:Profiles:pdf:TimeoutSeconds"] = "300";
            },
            ConfigureOwnedServices = services =>
            {
                services.PostConfigure<DelegationSettings>(s => s.AllowedRoots = [repo, converter]);
                services.RemoveAll<IChannelOutboundFileStore>();
                services.AddSingleton<IChannelOutboundFileStore>(new ChannelOutboundFileStore(storeRoot));
            },
        };
        var initialized = false;
        try
        {
            await app.InitializeAsync();
            initialized = true;
            app.EnsureSessionRunnerReachable();
            var config = app.Services.GetRequiredService<IConfiguration>();
            config.GetConnectionString("DefaultConnection").ShouldBe(app.OwnedDatabase);
            config["SessionRunner:BaseUrl"].ShouldBe(app.OwnedRunnerUrl);
            config["AntiphonMessaging:BootstrapServers"].ShouldBe(broker.GetBootstrapAddress());
            app.OwnedRunnerUrl.ShouldNotContain(":17204");
            fakeSlack.BaseUrl.ShouldStartWith("http://127.0.0.1:");
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.Projects.Add(new Project { Id = projectId, Name = "f5-" + projectId.ToString("N"),
                    LocalRepositoryPath = repo, BaseBranch = "main", CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "f5",
                    CreatedAt = now, UpdatedAt = now });
                db.Agents.AddRange(
                    new Agent { Id = sourceAgentId, BoardId = boardId, Name = "source", Slug = "f5-source-" + sourceAgentId.ToString("N"),
                        Kind = AgentKind.Grok, ModelLevel = AgentModelLevel.Low, WorkingDirectory = repo },
                    new Agent { Id = converterId, BoardId = boardId, Name = "converter", Slug = "f5-converter-" + converterId.ToString("N"),
                        Kind = AgentKind.Grok, ModelLevel = AgentModelLevel.Low, WorkingDirectory = converter });
                db.ChatChannels.Add(new ChatChannel { Id = Guid.NewGuid(), Provider = "slack", ExternalId = "C0418F5",
                    ReplyHandle = "C0418F5|1700000000.000100", AgentId = sourceAgentId,
                    OutboundAgentProfile = "pdf", Enabled = true, CreatedAt = now, UpdatedAt = now });
                await db.SaveChangesAsync(ct);
            }
            Guid sourceTaskId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                    new CreateAgentTaskRequest("Report the four completed Markdown paths in your final response.",
                        Title: "Four synthetic sources", Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Docs,
                        AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree,
                        WorkingDirectory: repo, CommitOnSettle: "Never"),
                    new AgentTaskService.Caller(null, null, repo, ProjectId: projectId, BoardId: boardId), ct);
                sourceTaskId = created.Id;
            }
            AgentTask? sourceTask = null;
            await UntilAsync(async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                sourceTask = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sourceTaskId, ct);
                if (sourceTask.AgentSessionId is Guid sessionId)
                {
                    var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
                    if (session?.Status == SessionStatus.Failed)
                        throw new InvalidOperationException("The owned source session failed before settlement: " + session.FailureReason);
                }
                if (sourceTask.Status is AgentTaskStatus.Failed or AgentTaskStatus.Blocked or AgentTaskStatus.Canceled)
                    throw new InvalidOperationException("The real source task failed: " + sourceTask.FailureReason);
                return sourceTask.Status == AgentTaskStatus.Succeeded && sourceTask.DeliverableFileCount == 4;
            }, ct);
            sourceTask!.DeliverablePdfPath.ShouldBeNull();
            var sourcePaths = DeliverableBundleService.ListAttachableFiles(sourceTask);
            sourcePaths.Count.ShouldBe(4);
            var sentSources = sourcePaths.Select(File.ReadAllBytes).ToArray();
            sentSources.Select(Hash).Order(StringComparer.Ordinal).ShouldBe(expected.Select(Hash).Order(StringComparer.Ordinal));
            if (sourceTask.WorktreePath is { } tree && Directory.Exists(tree))
                await GitAsync(repo, ct, "worktree", "remove", "--force", tree);
            var reply = new ChannelReply
            {
                Channel = "slack", ConversationId = "C0418F5", ReplyHandle = "C0418F5|1700000000.000100",
                Text = "Four synthetic sources complete.",
                Attachments = sourcePaths.Select(path => new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Name = Path.GetFileName(path), Mime = "text/markdown",
                    Content = File.ReadAllBytes(path),
                }).ToArray(),
            };
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
                (await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply,
                    new ChannelOutboundSource(Guid.NewGuid(), 1, 2, 3, "main", [], sourceTaskId), ct))
                    .ShouldBe(ChannelOutboundSendOutcome.Deferred);
            }
            Guid deliveryId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var delivery = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(ct);
                deliveryId = delivery.Id;
                delivery.State.ShouldNotBe(ChannelOutboundDeliveryState.Published);
                delivery.SourceTaskId.ShouldBe(sourceTaskId);
            }
            await UntilAsync(async () =>
            {
                if (File.Exists(gate + ".held")) return true;
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var current = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId, ct);
                if (current.ConversionTaskId is Guid taskId)
                {
                    var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
                    if (task.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Blocked)
                        throw new InvalidOperationException("Converter settled before the tool gate: " + task.Result);
                }
                return false;
            }, ct);
            using var observedBroker = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = broker.GetBootstrapAddress(),
                GroupId = "c0418-f5-observer-" + Guid.NewGuid().ToString("N"),
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            observedBroker.Subscribe(topic);
            observedBroker.Consume(TimeSpan.FromMilliseconds(750)).ShouldBeNull(
                "the frozen reply must not reach Kafka before tool output exists");
            fakeSlack.SentMessages.ShouldBeEmpty();
            fakeSlack.UploadedFiles.ShouldBeEmpty();
            var runnerUrl = app.OwnedRunnerUrl;
            await app.RestartOwnedHostAsync();
            app.OwnedRunnerUrl.ShouldBe(runnerUrl);
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<ChannelOutboundIsolatedTests>()
                .LogInformation("CARD-0418 post-restart conversion release for {DeliveryId}", deliveryId);
            await File.WriteAllTextAsync(gate + ".release", "release", ct);
            await UntilAsync(async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var delivery = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId, ct);
                if (delivery.State == ChannelOutboundDeliveryState.Published && fakeSlack.UploadedFiles.Count == 5)
                    return true;
                if (delivery.State is ChannelOutboundDeliveryState.Failed or ChannelOutboundDeliveryState.Held
                    or ChannelOutboundDeliveryState.PublishUncertain)
                    throw new InvalidOperationException("The outbound delivery stopped after host restart: " + delivery.State);
                if (delivery.ConversionTaskId is Guid taskId)
                {
                    var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
                    if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Blocked or AgentTaskStatus.Canceled)
                        throw new InvalidOperationException("The converter failed after host restart: " + task.Result);
                }
                return false;
            }, ct);
            var messages = fakeSlack.SentMessages;
            messages.ShouldHaveSingleItem().Channel.ShouldBe("C0418F5");
            messages[0].ThreadTs.ShouldBe("1700000000.000100");
            fakeSlack.PostMessageCalls.ShouldBe(1);
            var uploads = fakeSlack.UploadedFiles;
            uploads.Count.ShouldBe(5);
            var brokerRecord = observedBroker.Consume(TimeSpan.FromSeconds(15));
            brokerRecord.ShouldNotBeNull();
            brokerRecord.Message.Key.ShouldBe("C0418F5");
            var brokerReply = JsonSerializer.Deserialize<ChannelReply>(brokerRecord.Message.Value, MessagingJson.Options)!;
            brokerReply.ReplyHandle.ShouldBe("C0418F5|1700000000.000100");
            brokerReply.Attachments.Count.ShouldBe(5);
            brokerReply.Attachments.Select(a => Hash(a.Content!)).Order(StringComparer.Ordinal)
                .ShouldBe(uploads.Select(u => Hash(u.Bytes)).Order(StringComparer.Ordinal));
            observedBroker.Consume(TimeSpan.FromMilliseconds(750)).ShouldBeNull("one frozen reply must publish once");
            uploads.All(u => u.ThreadTs == "1700000000.000100" && u.ChannelId == "C0418F5").ShouldBeTrue();
            uploads.Where(u => u.Title.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(u => Hash(u.Bytes)).Order(StringComparer.Ordinal)
                .ShouldBe(expected.Select(Hash).Order(StringComparer.Ordinal));
            var pdf = uploads.Single(u => u.Title == "combined.pdf").Bytes;
            var producedPdf = await File.ReadAllBytesAsync(Path.Combine(storeRoot, deliveryId.ToString("N"), "output", "combined.pdf"), ct);
            pdf.ShouldBe(producedPdf);
            var extracted = await ExtractPdfTextAsync(producedPdf, root, browserImage, ct);
            foreach (var sentinel in new[] { "Requirements sentinel", "Design sentinel", "External API sentinel", "Current snapshots sentinel",
                "requirements middle sentinel", "design middle sentinel", "external API middle sentinel", "current snapshots middle sentinel",
                "requirements final sentinel", "design final sentinel", "external API final sentinel", "current snapshots final sentinel" })
                extracted.ShouldContain(sentinel);
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<ChannelOutboundIsolatedTests>()
                .LogInformation("CARD-0418 post-restart delivery published for {DeliveryId}", deliveryId);
            await UntilAsync(() => Task.FromResult(Directory.GetFiles(Path.Combine(root, "logs"), "antiphon-*.log")
                .Select(File.ReadAllText)
                .Any(log => log.Contains("post-restart conversion release for \"" + deliveryId + "\"", StringComparison.Ordinal)
                    && log.Contains("post-restart delivery published for \"" + deliveryId + "\"", StringComparison.Ordinal))), ct);
            await using var verifyScope = app.Services.CreateAsyncScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var final = await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId, ct);
            final.PublicationAttempts.ShouldBe(1);
            final.ConversionTaskId.ShouldNotBeNull();
            (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == final.ConversionTaskId, ct))
                .Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await verify.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId, ct)).ShouldBe(1);
        }
        finally
        {
            if (initialized) await app.DisposeAsync();
            await gateway.StopAsync(CancellationToken.None);
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> predicate, CancellationToken ct)
    {
        while (!await predicate())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string ShellQuote(string value) => value.Replace("'", "'\\''");

    private static async Task<string> BuildBrowserImageAsync()
    {
        // This context contains only the Dockerfile, so its digest identifies every build input.
        var fixture = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), "tests", "Antiphon.E2E", "Fixtures", "Card0418Browser");
        var dockerfile = Path.Combine(fixture, "Dockerfile");
        var image = "antiphon-card0418-browser:" + Hash(await File.ReadAllBytesAsync(dockerfile));
        var build = new ProcessStartInfo("docker") { RedirectStandardError = true,
            RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in new[] { "build", "--pull", "-t", image, "-f", dockerfile, fixture })
            build.ArgumentList.Add(arg);
        (await RunDockerAsync(build)).ShouldBe(0, "the F-5 browser image must build before conversion");
        return image;
    }

    private static async Task<int> RunDockerAsync(ProcessStartInfo start)
    {
        using var child = Process.Start(start)!;
        var stderr = child.StandardError.ReadToEndAsync();
        var stdout = child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync();
        await Task.WhenAll(stderr, stdout);
        return child.ExitCode;
    }

    private static async Task GitAsync(string cwd, CancellationToken ct, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardError = true,
            RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var stderr = child.StandardError.ReadToEndAsync(ct);
        var stdout = child.StandardOutput.ReadToEndAsync(ct);
        await child.WaitForExitAsync(ct);
        child.ExitCode.ShouldBe(0, (await stderr) + (await stdout));
    }

    private static async Task<string> ExtractPdfTextAsync(byte[] pdf, string root, string image, CancellationToken ct)
    {
        var path = Path.Combine(root, "transported.pdf");
        await File.WriteAllBytesAsync(path, pdf, ct);
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "run", "--rm", "--network", "none", "-v", root + ":" + root,
            "--entrypoint", "pdftotext", image, "-layout", path, "-" })
            start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var text = child.StandardOutput.ReadToEndAsync(ct);
        var error = child.StandardError.ReadToEndAsync(ct);
        await child.WaitForExitAsync(ct);
        child.ExitCode.ShouldBe(0, await error);
        return await text;
    }
}
