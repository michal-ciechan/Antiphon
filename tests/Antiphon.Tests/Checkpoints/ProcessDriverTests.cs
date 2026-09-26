using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class ProcessDriverTests
{
    [Test]
    public void child_process_is_created_without_a_window()
    {
        var info = ProcessDriver.CreateStartInfo("dotnet", Path.GetTempPath());
        info.CreateNoWindow.ShouldBeTrue();
        info.UseShellExecute.ShouldBeFalse();
    }
}
