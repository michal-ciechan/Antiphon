using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// CARD-0653/CARD-0658: the one comparison every operator surface uses. The request must carry
/// <see cref="OperatorTokenFile.Header"/> matching the file's value. The client address is never
/// consulted: the public vhost reaches Kestrel through Caddy and Vite as a loopback connection.
/// </summary>
public static class OperatorCredential
{
    public static bool HeaderMatches(HttpContext http, PhoneHomeRunnerSettings settings)
    {
        var path = OperatorTokenFile.ResolvePath(settings.OperatorTokenPath);
        var provided = http.Request.Headers[OperatorTokenFile.Header].ToString();
        return OperatorTokenFile.Matches(OperatorTokenFile.ReadOrCreate(path), provided);
    }

    public static void Require(HttpContext http, PhoneHomeRunnerSettings settings, string message)
    {
        if (!HeaderMatches(http, settings))
            throw new ForbiddenException(message, "operator_token_required");
    }
}
