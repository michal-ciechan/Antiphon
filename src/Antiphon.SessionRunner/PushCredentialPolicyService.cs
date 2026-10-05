using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>Publish the non-secret policy before phone-home can admit work.</summary>
public sealed class PushCredentialPolicyService(IOptions<PhoneHomeSettings> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.Enabled && settings.PushCredentialPolicyPath is { } path)
            PushCredentialPolicyFile.Write(path, settings.RepositoryPolicy());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
