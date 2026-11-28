using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

/// <summary>Holds an input lease through runner submission and checks the durable generation hold.</summary>
public sealed class SessionInputAdmission(IServiceScopeFactory scopes, SessionTerminalAttachments attachments)
{
    internal async Task<IDisposable> RequireAsync(Guid sessionId, CancellationToken ct)
    {
        var lease = attachments.TryBeginInput(sessionId) ?? throw StopInFlight();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await SessionStopAudit.IsPendingAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), sessionId, ct))
                throw StopInFlight();
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static ConflictException StopInFlight() =>
        new(SessionTerminalAttachments.StopInFlightMessage, "session_stop_in_flight");
}
