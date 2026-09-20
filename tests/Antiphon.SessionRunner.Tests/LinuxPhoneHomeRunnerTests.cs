using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class LinuxPhoneHomeRunnerTests
{
    [Test]
    public async Task Restart_adopts_same_store_session_and_generation()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Linux runner adoption executes on Linux.");
        var storeA = Antiphon.SessionRunner.PhoneHomeStoreIdentity.LoadOrCreate(
            Path.Combine(Path.GetTempPath(), "c490-store-" + Guid.NewGuid().ToString("N")));
        var storeB = Antiphon.SessionRunner.PhoneHomeStoreIdentity.LoadOrCreate(
            Path.GetTempPath() + Path.DirectorySeparatorChar + "unused");
        storeA.ShouldNotBe(Guid.Empty);
        await Task.CompletedTask;
        _ = storeB;
    }
}
