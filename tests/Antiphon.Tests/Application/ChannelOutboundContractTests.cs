using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ChannelOutboundContractTests
{
    [Test]
    public void Server_and_gateway_keep_source_and_transport_boundaries()
    {
        var root = FindRoot();
        var serverProject = XDocument.Load(Path.Combine(root, "server", "Antiphon.Server.csproj"));
        var toolProject = XDocument.Load(Path.Combine(root, "tools", "Antiphon.MarkdownPdf",
            "Antiphon.MarkdownPdf.csproj"));
        var serverDependencies = serverProject.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? "").ToArray();
        serverDependencies.ShouldNotContain(x => x.Contains("Markdig", StringComparison.OrdinalIgnoreCase));
        serverDependencies.ShouldNotContain(x => x.Contains("MarkdownPdf", StringComparison.OrdinalIgnoreCase));
        var toolDependencies = toolProject.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? "").ToArray();
        toolDependencies.ShouldNotContain(x => x.Contains("Server", StringComparison.OrdinalIgnoreCase));
        toolDependencies.ShouldNotContain(x => x.Contains("Messaging", StringComparison.OrdinalIgnoreCase));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "server", "appsettings.json")));
        var deliverables = settings.RootElement.GetProperty("Deliverables");
        deliverables.GetProperty("Enabled").GetBoolean().ShouldBeTrue();
        deliverables.TryGetProperty("BrowserPath", out _).ShouldBeFalse();
        deliverables.TryGetProperty("RenderTimeoutSeconds", out _).ShouldBeFalse();
        if (settings.RootElement.TryGetProperty("ChannelOutbound", out var outbound))
            (!outbound.TryGetProperty("Profiles", out var profiles)
                || profiles.EnumerateObject().Any() == false).ShouldBeTrue();

        ChannelPreamble.TelegramPresetTemplate.ShouldContain("Markdown sources automatically");
        ChannelPreamble.SlackPresetTemplate.ShouldContain("Markdown sources automatically");
        ChannelPreamble.TelegramPresetTemplate.ShouldNotContain("Prefer PDF for documents");
        ChannelPreamble.SlackPresetTemplate.ShouldNotContain("Prefer PDF for documents");
        InstructionBundles.All["orchestrator"].Text.ShouldContain("configured channel step");
        InstructionBundles.All["orchestrator"].Text.ShouldNotContain("Prefer PDF");
        ((int)AttentionKind.RepositoryChildJournalStale).ShouldBe(50);
        ((int)AttentionKind.ChannelOutboundDelivery).ShouldBe(51);
    }

    [Test]
    public void Model_keeps_optional_binding_and_unique_delivery_links()
    {
        using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions("Host=localhost;Database=unused"));
        var channel = db.Model.FindEntityType(typeof(ChatChannel))!;
        channel.FindProperty(nameof(ChatChannel.OutboundAgentProfile))!.IsNullable.ShouldBeTrue();
        var delivery = db.Model.FindEntityType(typeof(ChannelOutboundDelivery))!;
        delivery.GetIndexes().Single(i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(ChannelOutboundDelivery.SourceKey)])).IsUnique.ShouldBeTrue();
        var task = db.Model.FindEntityType(typeof(AgentTask))!;
        task.FindProperty(nameof(AgentTask.OutboundDeliveryId))!.IsNullable.ShouldBeTrue();
        task.GetIndexes().Single(i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(AgentTask.OutboundDeliveryId)])).IsUnique.ShouldBeTrue();
        task.GetForeignKeys().ShouldContain(f => f.Properties.Any(p => p.Name == nameof(AgentTask.OutboundDeliveryId))
            && f.PrincipalEntityType.ClrType == typeof(ChannelOutboundDelivery));
        var queue = db.Model.FindEntityType(typeof(SessionQueuedMessage))!;
        queue.FindProperty(nameof(SessionQueuedMessage.ChannelOutboundDeliveryId))!.IsNullable.ShouldBeTrue();
        queue.GetForeignKeys().ShouldContain(f => f.Properties.Any(p => p.Name == nameof(SessionQueuedMessage.ChannelOutboundDeliveryId))
            && f.PrincipalEntityType.ClrType == typeof(ChannelOutboundDelivery));
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "server", "Antiphon.Server.csproj")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Repository root was not found above the test binary.");
    }
}
