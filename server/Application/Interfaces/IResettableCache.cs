namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Implemented by services that hold an in-memory cache which tests need to clear between
/// runs. A shared <c>WebApplicationFactory</c> keeps singletons alive across the whole test
/// session (booting a factory per test is expensive), so without an explicit reset, cache
/// entries written by one test would leak into the next. Test setup resolves every registered
/// <see cref="IResettableCache"/> and calls <see cref="Clear"/>.
/// </summary>
public interface IResettableCache
{
    void Clear();
}
