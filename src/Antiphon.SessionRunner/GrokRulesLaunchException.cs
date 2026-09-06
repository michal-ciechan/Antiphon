using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0382: runner backstop refused a Windows Grok rules argument before creating a session.
/// Mapped to HTTP 409 with <see cref="GrokRulesArgvPolicy.ProblemCode"/>.
/// </summary>
public sealed class GrokRulesLaunchException : Exception
{
    public GrokRulesLaunchException(string message) : base(message)
    {
        Code = GrokRulesArgvPolicy.ProblemCode;
    }

    public string Code { get; }
}
