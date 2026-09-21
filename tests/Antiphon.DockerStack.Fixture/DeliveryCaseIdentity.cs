using System.Security.Cryptography;
using System.Text;

namespace Antiphon.DockerStack.Fixture;

public sealed class DeliveryOwnerMismatchException : Exception
{
    public DeliveryOwnerMismatchException() : base("OwnerMismatch") { }
}

public sealed class OversizedSummaryException : Exception
{
    public OversizedSummaryException(int bytes) : base("OversizedSummary") { Bytes = bytes; }
    public int Bytes { get; }
}

public sealed class DeliveryCaseIdentity
{
    public const int InlineCeilingBytes = 1024;

    public string Project { get; init; } = "";
    public string Resource { get; init; } = "";
    public string RunId { get; init; } = "";
    public string CaseName { get; init; } = "";

    public void EnsureOwner(string project, string resource)
    {
        if (!string.Equals(Project, project, StringComparison.Ordinal)
            || !string.Equals(Resource, resource, StringComparison.Ordinal))
            throw new DeliveryOwnerMismatchException();
    }

    public void EnsureInline(string body)
    {
        var bytes = Encoding.UTF8.GetByteCount(body);
        if (bytes > InlineCeilingBytes)
            throw new OversizedSummaryException(bytes);
    }

    public static string Sha256(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
