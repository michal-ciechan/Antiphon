using Antiphon.Server.Application.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// The data protection behind sign-in: the session cookie and the one-time state cookie. With
/// <c>Review:Auth:KeyRingPath</c> set, its own key ring in that folder (application name
/// <c>Antiphon.Review.SignIn</c>), which needs nothing but a writable folder: in a Linux container the
/// server's own key ring (<c>AgentTui:KeyRingPath</c>) makes no keys without a certificate
/// (<c>AgentTui:KeyProtection</c>), so every sign-in would fail. Absent, the server's key ring.
/// </summary>
public sealed class ReviewSignInProtection
{
    public const string ApplicationName = "Antiphon.Review.SignIn";

    public ReviewSignInProtection(IOptions<ReviewSettings> settings, IDataProtectionProvider server)
    {
        var path = settings.Value.Auth.KeyRingPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            Provider = server;
            return;
        }

        var folder = Directory.CreateDirectory(path.Trim());
        Provider = DataProtectionProvider.Create(folder, builder => builder.SetApplicationName(ApplicationName));
    }

    public IDataProtectionProvider Provider { get; }
}
