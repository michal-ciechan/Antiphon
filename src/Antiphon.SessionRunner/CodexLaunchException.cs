using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0497: runner refused a Codex launch before creating a session, host, log, or Herdr contact.
/// Mapped to HTTP 409 with <see cref="Code"/>.
/// </summary>
public sealed class CodexLaunchException : Exception
{
    public CodexLaunchException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
