namespace Antiphon.Tests.TestHelpers;

internal static class NativeLinuxGate
{
    internal static void Require(string reason)
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException(reason);
    }
}
