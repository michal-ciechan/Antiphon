namespace Antiphon.SessionRunner;

/// <summary>
/// Configuration for the optional, operator-run Herdr backend.  The switch stays off until a
/// later launch-path slice deliberately opts a session into Herdr.
/// </summary>
public sealed class HerdrSettings
{
    /// <summary>Enables callers to contact an operator-run Herdr instance. Defaults off.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Explicit named session. This has the same precedence as Herdr's <c>--session</c> option,
    /// before <c>HERDR_SOCKET_PATH</c> and <c>HERDR_SESSION</c>.
    /// </summary>
    public string? Session { get; set; }

    /// <summary>Bound on opening a named-pipe connection to Herdr.</summary>
    public int ConnectTimeoutMs { get; set; } = 5_000;

    /// <summary>The wire protocol this client was compiled and tested against.</summary>
    public int ExpectedProtocol { get; set; } = 20;
}
