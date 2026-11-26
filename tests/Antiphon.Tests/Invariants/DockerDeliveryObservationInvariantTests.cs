using System.Text;
using Antiphon.DockerStack.Fixture;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Infrastructure;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class DockerDeliveryObservationInvariantTests
{
    [Test]
    public void Observer_grants_are_select_only()
    {
        var sql = File.ReadAllText(Path.Combine(DockerStackDocuments.RepoRoot, "tests", "Antiphon.DockerStack.Fixture", "provision-observer.sql"));
        var grants = sql.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("GRANT ", StringComparison.OrdinalIgnoreCase))
            .ToList();
        grants.Count.ShouldBe(3);
        grants.ShouldAllBe(line => line.StartsWith("GRANT SELECT ", StringComparison.OrdinalIgnoreCase));
        grants.ShouldContain(line => line.Contains("\"AgentSessions\"", StringComparison.Ordinal));
        grants.ShouldContain(line => line.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal));
        grants.ShouldContain(line => line.Contains("\"TranscriptEntries\"", StringComparison.Ordinal));
    }
}
