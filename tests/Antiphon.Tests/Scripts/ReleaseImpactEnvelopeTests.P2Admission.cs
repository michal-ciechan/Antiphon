using Antiphon.Nightly;
using Shouldly;
using P2 = Antiphon.Nightly.ReleaseImpactAdmission;

namespace Antiphon.Tests.Scripts;

public sealed partial class ReleaseImpactEnvelopeTests
{
    private static async Task CheckAdmission()
    {
        await using var world = await P2World.Create();
        var preparation = P2.Prepare(world.Request, world.Chunks, world.Authority());
        preparation.Evidence.ShouldNotBeNull(preparation.Reason);
        var bytes = preparation.Evidence!.WriteProposal();
        var outcome = P2.AdmitPrepared(world.Request, world.Chunks, () => new MemoryStream(bytes), preparation);
        outcome.CarryCandidates.Select(row => row.Chunk).ShouldBe(new[] { "Q" }, "T.valid.candidates");
    }
}
