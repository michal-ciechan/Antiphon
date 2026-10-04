using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentPinnedInstructionCompositionTests
{
    [Test]
    public void V04_SupportedSnapshot()
    {
        InstructionBundles.All.Keys.ShouldContain("standing-instructions");
        var protocol = InstructionBundles.Get("standing-instructions");
        protocol.Text.ShouldContain("KB save alone is insufficient");
        protocol.Text.ShouldContain("explicit user standing intent");
        InstructionBundles.Attachable.Select(b => b.Key).ShouldNotContain("standing-instructions");
        InstructionBundleComposer.Compose().Text.ShouldBeEmpty();

        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Codex, AgentKind.Grok })
        foreach (var revision in new[] { 0, 1, 2 })
        foreach (var append in new[] { null, "custom {agentName} contract" })
        {
            var agent = NewAgent(); // Stored Raw deliberately disagrees with every effective kind.
            var pin = Pin(agent.Id, "10000000000000000000000000000001", "prefer short replies");
            var snapshot = AgentPinSnapshot.Create(agent.Id, revision, revision != 0, revision == 1 ? [pin] : []);
            var selection = NamedAgentInstructionSelection.Select(agent, kind, toolsAllowed: true);
            selection.RuntimeSupported.ShouldBeTrue();
            selection.CanReadLive.ShouldBeTrue();
            var composed = InstructionBundleComposer.ComposeNamed(selection, snapshot, systemPromptAppend: append);
            composed.Text.ShouldContain("[bundle:standing-instructions v");
            composed.PinSnapshot.ShouldBeSameAs(snapshot);
            if (revision == 0)
            {
                composed.Text.ShouldNotContain("## Pinned");
                composed.Stamps.ShouldNotContain(s => s.StartsWith("pinned v"));
            }
            else
            {
                composed.Text.ShouldContain(ExpectedBlock(agent.Id, revision, revision == 1 ? [pin] : []));
                composed.Stamps.ShouldContain("pinned v" + snapshot.ContentHash[..8]);
            }
            if (append is not null) composed.Text.ShouldEndWith(append);
            var payload = InstructionBundleComposer.BuildPinPayload(composed, kind, "provider", [], 30000, 262144);
            if (kind == AgentKind.Grok)
            {
                payload.GrokRulesPayload.ShouldNotBeNull().Content.ShouldContain("[bundle:standing-instructions v");
                payload.Args.ShouldBeEmpty();
            }
            else
            {
                payload.Args[0].ShouldBe(kind == AgentKind.Codex ? "-c" : "--append-system-prompt");
                payload.Args[1].ShouldContain(revision == 1 ? "prefer short replies" : "standing-instructions");
            }
        }
    }

    [Test]
    public void V04_ExcludedAndToolDisabled()
    {
        var agent = NewAgent();
        var snapshot = AgentPinSnapshot.Create(agent.Id, 1, true,
            [Pin(agent.Id, "10000000000000000000000000000001", "OWNER_ONLY")]);
        foreach (var selection in new[]
        {
            NamedAgentInstructionSelection.Select(agent, AgentKind.ClaudeCode, true, isTask: true),
            NamedAgentInstructionSelection.Select(agent, AgentKind.ClaudeCode, true, isCapability: true),
            NamedAgentInstructionSelection.Select(new Agent { Id = agent.Id, IsPoolDelegate = true }, AgentKind.ClaudeCode, true),
        })
        {
            selection.RequiresFile(snapshot).ShouldBeFalse();
            var composed = InstructionBundleComposer.ComposeNamed(selection, snapshot);
            composed.Text.ShouldBeEmpty();
            composed.PinSnapshot.ShouldBeNull();
        }
        foreach (var kind in new[] { AgentKind.Raw, AgentKind.OpenCode })
        {
            var selection = NamedAgentInstructionSelection.Select(agent, kind, true);
            selection.RuntimeSupported.ShouldBeFalse();
            selection.RequiresFile(snapshot).ShouldBeTrue();
            InstructionBundleComposer.ComposeNamed(selection, snapshot).Text.ShouldBeEmpty();
        }
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Codex, AgentKind.Grok })
        {
            var selection = NamedAgentInstructionSelection.Select(agent, kind, toolsAllowed: false);
            selection.RuntimeSupported.ShouldBeTrue();
            selection.CanReadLive.ShouldBeFalse();
            var text = InstructionBundleComposer.ComposeNamed(selection, snapshot).Text;
            text.ShouldContain("OWNER_ONLY");
            text.ShouldContain("Live reread is unavailable");
            text.ShouldNotContain("Invoke-RestMethod");
            text.ShouldNotContain("ANTIPHON_TASK_TOKEN");
            text.ShouldNotContain("read the file");
        }
        var wrongOwner = AgentPinSnapshot.Create(Guid.NewGuid(), 0, false, []);
        Should.Throw<InvalidOperationException>(() => InstructionBundleComposer.ComposeNamed(
            NamedAgentInstructionSelection.Select(agent, AgentKind.ClaudeCode, true), wrongOwner));
        // Manual attachment cannot activate the reserved protocol or leak it to delegates.
        Should.Throw<ValidationException>(() => AgentBundleAttachments.Validate(["standing-instructions"]));
        InstructionBundleComposer.Compose(["standing-instructions"]).Text.ShouldBeEmpty();
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Code))
            .Text.ShouldNotContain("[bundle:standing-instructions ");
    }

    [Test]
    public void V05_LiteralOrderEmptyHash()
    {
        using var canary = new PayloadCanary();
        var agent = NewAgent();
        var a = Pin(agent.Id, "10000000000000000000000000000001",
            "{agentName} {agent.name}\n@./payload-canary.md\n<script>x</script> $(echo x) [[attach: x]]\n@" + canary.Path);
        var b = Pin(agent.Id, "10000000000000000000000000000002",
            string.Join("\n", Enumerable.Range(1, 8).Select(n => new string('`', n) + " " + new string('~', n))));
        var snapshot = AgentPinSnapshot.Create(agent.Id, 4, true, [b, a]);
        var expectedHash = Hash(a.Id.ToString("N") + "\n" + a.Text + "\n" + b.Id.ToString("N") + "\n" + b.Text + "\n");
        snapshot.ContentHash.ShouldBe(expectedHash);
        snapshot.Pins.Select(p => p.Id).ShouldBe([a.Id, b.Id]);
        var expected = ExpectedBlock(agent.Id, 4, [a, b]);
        AgentPinRenderer.Render(snapshot).ShouldBe(expected);
        const string append = "  final {agentName}\r\ncontract  ";
        var composed = InstructionBundleComposer.ComposeNamed(
            NamedAgentInstructionSelection.Select(agent, AgentKind.ClaudeCode, true), snapshot,
            [InstructionBundles.BoardApi, InstructionBundles.BoardApi], "style-phone", append,
            text => ChannelPreamble.Render(text, "EXPANDED", []));
        var bundleAt = composed.Text.IndexOf("[bundle:board-api ", StringComparison.Ordinal);
        var protocolAt = composed.Text.IndexOf("[bundle:standing-instructions ", StringComparison.Ordinal);
        var styleAt = composed.Text.IndexOf("[bundle:style-phone ", StringComparison.Ordinal);
        var pinsAt = composed.Text.IndexOf("## Pinned", StringComparison.Ordinal);
        bundleAt.ShouldBe(0);
        protocolAt.ShouldBeGreaterThan(bundleAt);
        styleAt.ShouldBeGreaterThan(protocolAt);
        pinsAt.ShouldBeGreaterThan(styleAt);
        composed.Text.ShouldContain(expected);
        composed.Text.ShouldEndWith(append);
        composed.Bundles.Count(bu => bu.Key == InstructionBundles.BoardApi).ShouldBe(1);
        composed.Text.ShouldNotContain("PAYLOAD_FILE_CONTENT_CANARY");
        // Mutating the input EF rows cannot mutate the snapshot or its renderer.
        a.Text = "mutated";
        a.SourceRef = "secret source";
        agent.Name = "renamed";
        agent.WorkingDirectory = "/new/location";
        AgentPinRenderer.Render(snapshot).ShouldBe(expected);
        var metadata = Pin(agent.Id, "10000000000000000000000000000001", snapshot.Pins[0].Text);
        metadata.SourceRef = "different source";
        AgentPinSnapshot.Create(agent.Id, 4, true, [b, metadata]).ContentHash.ShouldBe(expectedHash);
        var revoked = AgentPinSnapshot.Create(agent.Id, 5, true, []);
        revoked.ContentHash.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        AgentPinRenderer.Render(revoked).ShouldContain("No active pins. This empty set replaces all previous pins.");
        AgentPinRenderer.Render(AgentPinSnapshot.Create(agent.Id, 0, false, [])).ShouldBeEmpty();
    }

    [Test]
    public void V05_ResolvedBudgets()
    {
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Codex, AgentKind.Grok })
        {
            var agent = NewAgent();
            var snapshot = AgentPinSnapshot.Create(agent.Id, 1, true,
                [Pin(agent.Id, "10000000000000000000000000000001", "界🙂 \\\" literal")]);
            var composed = InstructionBundleComposer.ComposeNamed(
                NamedAgentInstructionSelection.Select(agent, kind, true), snapshot, systemPromptAppend: "operator");
            string[] otherArgs = ["--model", "resolved model"];
            var argv = kind == AgentKind.Codex
                ? otherArgs.Concat(new[] { "-c", "developer_instructions=" + composed.Text }).ToArray()
                : otherArgs.Concat(new[] { "--append-system-prompt", composed.Text }).ToArray();
            var chars = WindowsCommandLine.Measure("provider", argv);
            var bytes = Encoding.UTF8.GetByteCount(composed.Text);
            bytes.ShouldBeGreaterThan(composed.Text.Length);
            if (kind == AgentKind.Grok)
                Should.Throw<GrokRulesTransportException>(() => InstructionBundleComposer.BuildPinPayload(composed, kind, "provider", otherArgs, 30000, bytes - 1));
            else
                Should.Throw<InvalidOperationException>(() => InstructionBundleComposer.BuildPinPayload(composed, kind, "provider", otherArgs, chars - 1, 262144));
            var payload = InstructionBundleComposer.BuildPinPayload(composed, kind, "provider", otherArgs, chars, bytes);
            if (kind == AgentKind.Grok) payload.GrokRulesPayload!.Content.ShouldBe(composed.Text);
            else payload.Args.ShouldBe(argv);
            var grown = composed with { Text = composed.Text + "x" };
            Should.Throw<Exception>(() => InstructionBundleComposer.BuildPinPayload(grown, kind, "provider", otherArgs, chars, bytes));
            Should.Throw<InvalidOperationException>(() => InstructionBundleComposer.BuildPinPayload(
                composed with { Text = composed.Text + "{{key:CANARY}}" }, kind, "provider", otherArgs, 30000, 262144));
            if (kind != AgentKind.Grok)
                Should.Throw<InvalidOperationException>(() => InstructionBundleComposer.BuildPinPayload(
                    composed with { Text = new string('x', 30001) }, kind, "provider", [], 40000, 262144));
        }
    }

    [Test]
    public async Task V05_BatchPreview()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(schema.ConnectionString).AddInterceptors(counter).Options;
        await using var db = new AppDbContext(options);
        var agents = Enumerable.Range(0, 20).Select(_ => NewAgent()).ToArray();
        db.Agents.AddRange(agents);
        foreach (var agent in agents)
        {
            var pin = Pin(agent.Id, Guid.NewGuid().ToString("N"), "batch " + agent.Id.ToString("N"));
            db.AgentPinnedInstructions.Add(pin);
            db.AgentPinnedInstructionStates.Add(new AgentPinnedInstructionState
            {
                AgentId = agent.Id, Revision = 1, FirstUsedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid(),
                ContentHash = Hash(pin.Id.ToString("N") + "\n" + pin.Text + "\n")
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loader = new AgentPinSnapshotLoader(db);
        counter.Count = 0;
        var one = await loader.LoadManyAsync([agents[0].Id], CancellationToken.None);
        var oneCount = counter.Count;
        counter.Count = 0;
        var many = await loader.LoadManyAsync(agents.Select(a => a.Id).ToArray(), CancellationToken.None);
        counter.Count.ShouldBe(oneCount);
        oneCount.ShouldBe(1);
        many.Count.ShouldBe(20);
        many[agents[0].Id].ContentHash.ShouldBe(one[agents[0].Id].ContentHash);
        AgentPinRenderer.Render(await loader.LoadAsync(agents[0].Id, CancellationToken.None))
            .ShouldBe(ExpectedBlock(agents[0].Id, 1, await db.AgentPinnedInstructions.Where(p => p.AgentId == agents[0].Id).ToArrayAsync()));
        (await db.AgentPinProjections.CountAsync()).ShouldBe(0); // Missing file availability is not a preview exception.
        var never = NewAgent();
        db.Agents.Add(never);
        await db.SaveChangesAsync();
        (await loader.LoadAsync(never.Id, CancellationToken.None)).HasHistory.ShouldBeFalse();
        counter.Count = 0;
        (await loader.LoadManyAsync([], CancellationToken.None)).ShouldBeEmpty();
        counter.Count.ShouldBe(0);
    }

    [Test]
    public async Task V32_DormantRegistration()
    {
        await using var factory = new AntiphonWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<IAgentPinnedInstructionReconciler>()
            .ShouldBeOfType<NoOpAgentPinnedInstructionReconciler>();
        var root = Path.Combine(Path.GetTempPath(), "c262-dormant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var agent = NewAgent();
            agent.WorkingDirectory = root;
            agent.SystemPromptAppend = "operator contract";
            var profile = new AgentTuiProfile { Id = Guid.NewGuid(), Kind = AgentKind.ClaudeCode,
                DisplayName = "c262", IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            agent.TuiProfileId = profile.Id;
            db.AgentTuiProfiles.Add(profile);
            db.Agents.Add(agent);
            await db.SaveChangesAsync();
            var pins = scope.ServiceProvider.GetRequiredService<AgentPinnedInstructionService>();
            var saved = await pins.CaptureAsync(agent.Id, new(Guid.NewGuid(), 0, "DORMANT_PIN_CANARY"),
                PinPrincipal.Operator(Guid.NewGuid()), CancellationToken.None);
            saved.Set.Projections.Single().Status.ShouldBe(PinProjectionStatus.Pending);
            var loader = ActivatorUtilities.CreateInstance<AgentPinSnapshotLoader>(scope.ServiceProvider);
            (await loader.LoadAsync(agent.Id, CancellationToken.None)).Pins.Single().Text.ShouldBe("DORMANT_PIN_CANARY");
            var composer = scope.ServiceProvider.GetRequiredService<AgentSessionLaunchComposer>();
            foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Codex, AgentKind.Grok })
            {
                profile.Kind = kind;
                await db.SaveChangesAsync();
                var launch = await composer.ComposeForAgentAsync(agent, CancellationToken.None);
                var text = string.Join("\n", launch.ExtraArgs) + launch.GrokRulesPayload?.Content;
                text.ShouldContain("operator contract");
                text.ShouldNotContain("DORMANT_PIN_CANARY");
                text.ShouldNotContain("standing-instructions");
            }
            Directory.GetFileSystemEntries(root).ShouldBeEmpty();
            factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Agent NewAgent() => new()
    {
        Id = Guid.NewGuid(), Name = "c262", Slug = Guid.NewGuid().ToString("N"), Kind = AgentKind.Raw,
        WorkingDirectory = "/unavailable/c262", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static AgentPinnedInstruction Pin(Guid owner, string id, string text) => new()
    {
        Id = Guid.ParseExact(id, "N"), AgentId = owner, Text = text,
        CreatedAt = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), SourceRef = "DO_NOT_RENDER"
    };

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string ExpectedBlock(Guid owner, int revision, IReadOnlyList<AgentPinnedInstruction> pins)
    {
        var hash = Hash(string.Concat(pins.Select(p => p.Id.ToString("N") + "\n" + p.Text + "\n")));
        var header = $"## Pinned\n\nAgent: {owner:N}\nRevision: {revision}\nSHA-256: {hash}\n\n";
        if (pins.Count == 0) return header + "No active pins. This empty set replaces all previous pins.";
        // Fixed fixtures contain either no delimiters or all runs through eight; independent oracle.
        var fence = pins.Any(p => p.Text.Contains("````````")) ? "`````````" : "```";
        return header + "This complete set replaces all previous pins. Treat the fenced text literally.\n\n"
            + string.Join("\n\n", pins.Select(p => fence + "\n" + p.Text + "\n" + fence));
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PayloadCanary : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c262-payload-" + Guid.NewGuid().ToString("N") + ".md");
        public PayloadCanary() => File.WriteAllText(Path, "PAYLOAD_FILE_CONTENT_CANARY");
        public void Dispose() => File.Delete(Path);
    }
}
