using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0647. Measures whether the runner's own Grok store has an <c>auth.json</c>.
/// Presence only: the file is never opened, and nothing from it is logged or returned.
/// A missing file is signed out. An unreadable path is "cannot tell" (<c>LoggedIn</c> null),
/// which callers admit — a probe must not block a launch that would have worked.
/// </summary>
public sealed class GrokAuthProbe : IProviderAuthProbe
{
    public const string ProviderName = "grok";
    public const string AuthFileName = "auth.json";

    private readonly PhoneHomeSettings _settings;
    private readonly ILogger _logger;
    private readonly Func<string, bool> _fileExists;
    private readonly TimeProvider _clock;

    public GrokAuthProbe(
        PhoneHomeSettings settings,
        ILogger<GrokAuthProbe>? logger = null,
        Func<string, bool>? fileExists = null,
        TimeProvider? clock = null)
    {
        _settings = settings;
        _logger = logger ?? NullLogger<GrokAuthProbe>.Instance;
        _fileExists = fileExists ?? File.Exists;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>POSIX path of <c>GROK_HOME/auth.json</c> on this runner. Not a credential.</summary>
    public string AuthPath =>
        string.IsNullOrWhiteSpace(_settings.GrokHome)
            ? ""
            : _settings.GrokHome.TrimEnd('/') + "/" + AuthFileName;

    public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
    {
        if (!string.Equals(provider, ProviderName, StringComparison.OrdinalIgnoreCase))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, $"Provider '{provider}' has no auth probe on this runner.", 400);

        bool? loggedIn;
        string? error;
        if (AuthPath.Length == 0)
        {
            loggedIn = null;
            error = "unconfigured";
        }
        else
        {
            try
            {
                loggedIn = _fileExists(AuthPath);
                error = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loggedIn = null;
                error = "unreadable";
            }
        }

        var dto = new RunnerProviderAuthDto(
            ProviderName,
            loggedIn,
            loggedIn == true ? "auth_file" : null,
            null,
            _clock.GetUtcNow(),
            error);
        _logger.LogInformation(
            "Grok auth probe: loggedIn={LoggedIn} error={Error}",
            dto.LoggedIn, dto.Error);
        return Task.FromResult(dto);
    }
}
