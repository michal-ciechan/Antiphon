using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("Unit")]
public class PtyHostPipeNameTests
{
    [Test]
    public void PipeNameFor_is_rooted_under_tmp_on_unix_only()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var name = PtyHostProtocol.PipeNameFor(id);
        if (OperatingSystem.IsWindows())
            name.ShouldBe("antiphon-pty-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        else
            name.ShouldBe("/tmp/antiphon-pty-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }
}
