namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Counts test classes whose After(Class) work has not finished. RED STUB: does not track yet.
/// </summary>
internal sealed class ClassTeardownTracker
{
    internal static ClassTeardownTracker Default { get; } = new();

    public void Enter(string className)
    {
    }

    public void Exit(string className)
    {
    }

    public Task<IReadOnlyList<string>> WaitForIdleAsync(TimeSpan bound) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
