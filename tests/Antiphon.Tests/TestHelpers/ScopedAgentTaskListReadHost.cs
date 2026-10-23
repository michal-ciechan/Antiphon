using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Session-owned, immutable fleet for scoped-list HTTP readers. Mutating scenarios keep their
/// own template-cloned databases; clients borrow this host and never own its lifetime.
/// </summary>
public sealed class ScopedAgentTaskListReadHost : IAsyncDisposable
{
    private readonly Lazy<Task<AntiphonWebAppFactory>> _factory = new(CreateAsync);

    internal Task<AntiphonWebAppFactory> GetFactoryAsync() => _factory.Value;

    private static async Task<AntiphonWebAppFactory> CreateAsync()
    {
        var factory = new AntiphonWebAppFactory();
        try
        {
            // Services starts the real host once; each request still gets a scoped DbContext.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var seed = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "seed");
            await ScopedAgentTaskListFixture.SeedFleetAsync(db);
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Failed initialization already disposes its host. All consumers finish before TUnit
        // disposes a PerTestSession source, so no initialization can still be in flight here.
        if (_factory.IsValueCreated && _factory.Value.IsCompletedSuccessfully)
            await _factory.Value.Result.DisposeAsync();
    }
}
