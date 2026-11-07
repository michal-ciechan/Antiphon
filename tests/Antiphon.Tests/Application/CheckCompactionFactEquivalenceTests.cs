using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1754. Fails while fact loading still walks every Check token against every prompt,
/// or still projects every transcript row's text in one unfiltered query.
/// </summary>
[Category("Unit")]
public class CheckCompactionFactEquivalenceTests
{
    [Test]
    public async Task Fact_load_does_not_token_scan_every_check_id()
    {
        var source = await File.ReadAllTextAsync(ServicePath());
        source.ShouldNotContain("tokens.Any(");
        source.ShouldNotContain("SelectMany(id => new[]");
        source.ShouldNotContain("t.Kind, t.Text, t.Timestamp");
    }

    private static string ServicePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        dir.ShouldNotBeNull();
        return Path.Combine(dir!.FullName, "server", "Application", "Services", "CheckCompactionContinuationService.cs");
    }
}
