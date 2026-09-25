using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0660 D-6. Measures whether the runner's own Codex store has an <c>auth.json</c>.
/// Metadata only: the file is never opened, parsed or logged, and no CLI is spawned. A regular,
/// non-link file is signed in (a presence hint, not proof the token is valid or the plan
/// entitled); a missing file or home, a directory, or a symlink (even to a real file) is signed out. An unconfigured home, or a path whose metadata cannot be
/// read, is "cannot tell" (<c>LoggedIn</c> null), which callers admit.
///
/// <para>Unlike <see cref="GrokAuthProbe"/>, this reads attributes rather than calling
/// <see cref="File.Exists(string)"/>, which answers false for an unreadable path and would turn
/// "cannot tell" into a definite refusal.</para>
/// </summary>
public sealed class CodexAuthProbe : IProviderAuthProbe
{
    public const string ProviderName = "codex";
    public const string AuthFileName = "auth.json";

    private readonly PhoneHomeSettings _settings;
    private readonly ILogger _logger;
    private readonly Func<string, FileAttributes> _getAttributes;
    private readonly TimeProvider _clock;

    public CodexAuthProbe(
        PhoneHomeSettings settings,
        ILogger<CodexAuthProbe>? logger = null,
        Func<string, FileAttributes>? getAttributes = null,
        TimeProvider? clock = null)
    {
        _settings = settings;
        _logger = logger ?? NullLogger<CodexAuthProbe>.Instance;
        _getAttributes = getAttributes ?? File.GetAttributes;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>POSIX path of <c>CODEX_HOME/auth.json</c> on this runner. Not a credential.</summary>
    public string AuthPath =>
        string.IsNullOrWhiteSpace(_settings.CodexHome)
            ? ""
            : _settings.CodexHome.TrimEnd('/') + "/" + AuthFileName;

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
                // Only a regular, non-link file is presence. File.GetAttributes judges the final
                // component with lstat semantics on Unix, so a symlinked (or dangling) auth.json
                // carries ReparsePoint, while a regular file in a linked or bind-mounted home does not.
                loggedIn = (_getAttributes(AuthPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
                error = null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                loggedIn = false;
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
            "Codex auth probe: loggedIn={LoggedIn} error={Error}",
            dto.LoggedIn, dto.Error);
        return Task.FromResult(dto);
    }
}
