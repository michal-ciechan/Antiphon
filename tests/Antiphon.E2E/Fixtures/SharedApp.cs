namespace Antiphon.E2E.Fixtures;

/// <summary>
/// A test-session-wide <see cref="AntiphonAppFixture"/> — ONE Kestrel app + ONE Postgres
/// testcontainer shared by every test that opts in. Building the factory is the expensive part of
/// this suite (container start + migrations + host boot), so tests that only need "a running app"
/// should take this instead of newing their own fixture. Tests that need special fixture flags
/// (prebuilt frontend, mock executor) still construct their own.
///
/// Lifetime: lazily started on first use; torn down by <see cref="SharedAppTeardown"/> at
/// test-session end. Callers must tolerate shared DB state (unique names/ids per test).
/// </summary>
public static class SharedApp
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static AntiphonAppFixture? _fixture;

    public static async Task<AntiphonAppFixture> GetAsync()
    {
        if (_fixture is not null)
            return _fixture;
        await Gate.WaitAsync();
        try
        {
            if (_fixture is null)
            {
                var fixture = new AntiphonAppFixture();
                await fixture.InitializeAsync();
                _fixture = fixture;
            }
            return _fixture;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task DisposeIfStartedAsync()
    {
        var fixture = Interlocked.Exchange(ref _fixture, null);
        if (fixture is not null)
            await fixture.DisposeAsync();
    }
}

public sealed class SharedAppTeardown
{
    [TUnit.Core.After(TUnit.Core.HookType.TestSession)]
    public static async Task TearDownSharedApp() => await SharedApp.DisposeIfStartedAsync();
}
