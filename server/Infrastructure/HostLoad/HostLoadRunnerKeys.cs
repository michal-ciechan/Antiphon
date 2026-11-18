using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.HostLoad;

/// <summary>Folds the direct runner's <c>local</c> and <c>desktop</c> ids onto one configured key.</summary>
public sealed class HostLoadRunnerKeys
{
    public const string DefaultKey = "desktop";

    public static HostLoadRunnerKeys Default { get; } = new(
        Options.Create(new HostLoadHistorySettings()),
        Options.Create(new PhoneHomeRunnerSettings { Enabled = false }));

    public string DirectRunnerKey { get; }

    public HostLoadRunnerKeys(IOptions<HostLoadHistorySettings> history, IOptions<PhoneHomeRunnerSettings> phoneHome)
    {
        var key = history.Value.DirectRunnerKey;
        HostLoadHistorySettings.ValidateDirectRunnerKey(key);
        foreach (var runner in PhoneHomeRunnerCatalog.Configured(phoneHome.Value))
        {
            if (string.Equals(runner.Id, key, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "HostLoadHistory:DirectRunnerKey refuses a configured phone-home runner id.");
        }

        DirectRunnerKey = key;
    }

    public string Canonical(string id) => id is "local" or "desktop" ? DirectRunnerKey : id;

    public bool IsDirect(string canonical) => string.Equals(canonical, DirectRunnerKey, StringComparison.Ordinal);
}
