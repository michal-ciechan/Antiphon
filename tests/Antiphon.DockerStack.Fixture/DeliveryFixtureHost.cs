using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.DockerStack.Fixture;

public static class DeliveryFixtureHost
{
    public static void Install(IServiceCollection services, DeliveryGate gate)
    {
        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ISessionRunnerClient));
        if (existing is null)
            throw new InvalidOperationException("ISessionRunnerClient is not registered.");
        services.Remove(existing);
        services.AddSingleton<ISessionRunnerClient>(provider =>
        {
            var inner = existing.ImplementationInstance as ISessionRunnerClient
                ?? existing.ImplementationFactory?.Invoke(provider) as ISessionRunnerClient
                ?? throw new InvalidOperationException("runner registration was not captured");
            return new OrdinaryDeliveryRunnerClient(inner, gate);
        });
    }
}
