using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Coalesced "a covered setting changed" ping. Writers resolve it at call time so a host that
/// never registered the generator still constructs them.
/// </summary>
public interface IOrchestratorInstructionsSignals
{
    void Signal(string reason);
}

/// <summary>
/// Microsoft.Extensions.DependencyInjection ignores C# default values, so writers take
/// <see cref="IServiceProvider"/> (always registered) and look this up. A missing generator is a no-op.
/// </summary>
internal static class OrchestratorInstructionsSignal
{
    public static void Fire(IServiceProvider? services, string reason)
    {
        if (services is null || string.IsNullOrWhiteSpace(reason))
            return;

        try
        {
            services.GetService<IOrchestratorInstructionsSignals>()?.Signal(reason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The writer's own commit already succeeded. A signal failure must not undo it.
        }
    }
}
