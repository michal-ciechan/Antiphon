namespace Antiphon.Server.Application.Exceptions;

/// <summary>A guarded Git mutation after reconciliation began, reported in its partial result.</summary>
public sealed class CardFileGitGuardException(string code) : Exception("Card-file Git mutation is guarded.")
{
    public string Code { get; } = code;
}
