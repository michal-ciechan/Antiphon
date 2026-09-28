using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[NotInParallel("C467LandDelivery")]
public class LandDeliveryFixtureProbeTests
{
    [Test]
    public async Task ProtocolProgress_counts_committed_protocol_git_records_only()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        try
        {
            f.ProtocolProgress().ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json.tmp"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"attention-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(1, "a partial .tmp record and a foreign evidence file are not progress");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(2);
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }
}
