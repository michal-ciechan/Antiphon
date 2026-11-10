namespace Antiphon.TestSupport;

// CARD-0588: a fixture whose dependency exists only on Windows skips elsewhere with its named
// reason. On Windows the gate admits, and a missing prerequisite after it still fails red.
internal static class NativeWindowsGate
{
    internal static void Require(string reason) => Require(reason, OperatingSystem.IsWindows());

    internal static void Require(string reason, bool isWindows)
    {
        if (!isWindows)
            throw new TUnit.Core.Exceptions.SkipTestException(reason);
    }
}
