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
        var path = Path.Combine(Path.GetTempPath(), "c490-store-" + Guid.NewGuid().ToString("N"), "runner-store-id");
        var storeA = Antiphon.SessionRunner.PhoneHomeStoreIdentity.LoadOrCreate(path);
        var storeB = Antiphon.SessionRunner.PhoneHomeStoreIdentity.LoadOrCreate(path);
        storeA.ShouldNotBe(Guid.Empty);
        storeB.ShouldBe(storeA);
        File.ReadAllText(path).Trim().ShouldBe(storeA.ToString("D"));
        await Task.CompletedTask;
    }
}
