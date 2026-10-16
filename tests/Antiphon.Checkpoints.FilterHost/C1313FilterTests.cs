using TUnit.Core;

namespace Antiphon.Tests.FilterHost;

public sealed class C1313Offline
{
    [Test]
    public void One() { }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public void Rows(int value)
    {
        if (value is < 1 or > 3) throw new InvalidOperationException("unexpected fixture argument");
    }

    [Test]
    public void OneLive() => C1313Forbidden.WriteAndFail();
}

public sealed class C1313OfflineLive
{
    [Test]
    public void One() => C1313Forbidden.WriteAndFail();
}

internal static class C1313Forbidden
{
    internal static void WriteAndFail()
    {
        var path = Environment.GetEnvironmentVariable("C1313_SENTINEL");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, "forbidden leaf launched\n");
        throw new InvalidOperationException("forbidden sibling launched");
    }
}
