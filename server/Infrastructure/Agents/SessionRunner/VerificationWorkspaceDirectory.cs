using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0604 D-19 / G-33. Resolves a task's verification workspace by the task's OWN runner id.
///
/// The failure this exists to prevent is small and quiet: reading custody, restoration or
/// removal for a server2-bound Mutation through the local client. Every one of those would
/// succeed -- and answer about a desktop filesystem that has no snapshot, no evidence and no
/// receipt. An unavailable remote runner is the existing phone-home 503, never a fallback.
/// </summary>
public sealed class VerificationWorkspaceDirectory(
    ISessionRunnerDirectory runners,
    IOptions<PhoneHomeRunnerSettings> settings,
    IVerificationWorkspace local) : IVerificationWorkspaceDirectory
{
    public IVerificationWorkspace Resolve(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || runnerId == PhoneHomeProtocol.LocalRunnerId)
            return local;
        // Resolve() throws ServiceUnavailableException for an unavailable or not-yet-recovered
        // remote runner. That 503 is the answer; it is never converted into a local read.
        var client = runners.Resolve(runnerId);
        if (client is not PhoneHomeRunnerClient phoneHome)
            throw new Application.Exceptions.ServiceUnavailableException(
                "The bound runner cannot host a verification workspace.", PhoneHomeProblemTypes.Unavailable);
        var options = settings.Value;
        return new RemoteVerificationWorkspace(phoneHome, options.RunnerRepository, options.RunnerWorkspace);
    }
}
