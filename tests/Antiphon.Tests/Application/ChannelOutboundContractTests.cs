using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class ChannelOutboundContractTests
{
    [Test]
    public void Server_and_gateway_keep_source_and_transport_boundaries()
    {
        typeof(DeliverableBundleService).Assembly.GetType("Antiphon.Server.Application.Services.MarkdownPdfRenderer")
            .ShouldBeNull();
        var checkout = FindCheckout();
        File.ReadAllText(Path.Combine(checkout, "server", "Antiphon.Server.csproj"))
            .ShouldNotContain("Markdig");
        File.ReadAllText(Path.Combine(checkout, "server", "Bundles", "orchestrator.md"))
            .ShouldNotContain("Prefer PDF");
        File.ReadAllText(Path.Combine(checkout, "server", "Bundles", "orchestrator.md"))
            .ShouldContain("opt into a conversion agent");
    }

    private static string FindCheckout()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("checkout not found");
    }
}
