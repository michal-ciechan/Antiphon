using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointPathTests
{
    [Test]
    public void Containment_trims_the_candidate_before_comparison()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Context.IsStrictlyInside(f.Root, f.Root + "/child/").ShouldBeTrue("C1302.child");
        f.Context.IsStrictlyInside(f.Root, f.Root + "-sibling/child").ShouldBeFalse("C1302.sibling");
        f.Context.IsStrictlyInside(f.Root, f.Root + "/").ShouldBeFalse("C1302.root-with-separator");
    }

    [Test]
    public void Invalid_paths_refuse_without_throwing()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Context.IsStrictlyInside(f.Root, "bad\0path").ShouldBeFalse("C1302.invalid-candidate");
        f.Context.IsStrictlyInside("bad\0root", f.Payload).ShouldBeFalse("C1302.invalid-root");
    }
}
