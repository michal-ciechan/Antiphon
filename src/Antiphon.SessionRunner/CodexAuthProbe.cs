using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0660 D-6. Measures whether the runner's own Codex store has an <c>auth.json</c>.
/// RED SEAM: not implemented yet; answers like the runner before CARD-0660 (no Codex probe).
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

    public string AuthPath =>
        string.IsNullOrWhiteSpace(_settings.CodexHome)
            ? ""
            : _settings.CodexHome.TrimEnd('/') + "/" + AuthFileName;

    public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct) =>
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.UnsupportedTarget, $"Provider '{provider}' has no auth probe on this runner.", 400);
}
