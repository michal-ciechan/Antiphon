using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

internal static class LandRequestWriteProvenance
{
    public static void Stamp(AgentTaskLandRequest request, string operation, TimeProvider clock)
    {
        if (operation.Length is < 1 or > 80 || !operation.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Invalid land request writer label", nameof(operation));
        var token = Guid.NewGuid();
        request.ConcurrencyToken = token;
        request.LastWriterToken = token;
        request.LastWriterOperation = operation;
        request.LastWriterAt = clock.GetUtcNow().UtcDateTime;
    }
}
