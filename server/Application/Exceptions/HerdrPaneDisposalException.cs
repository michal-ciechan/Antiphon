namespace Antiphon.Server.Application.Exceptions;

/// <summary>Preserves the runner's durable receipt on non-success HTTP responses.</summary>
public sealed class HerdrPaneDisposalException : HttpException
{
    public HerdrPaneDisposalException(int status, string message, string code,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(status, message, code, extensions) { }
}
