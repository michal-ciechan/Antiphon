using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Application.Settings;

public sealed record ExpectationAgentReference(Guid? BoardId, bool IsPoolDelegate);

public sealed record ExpectationReferenceCatalog(
    IReadOnlyDictionary<Guid, Guid> CardBoards,
    IReadOnlyDictionary<Guid, ExpectationAgentReference> Agents,
    IReadOnlyDictionary<Guid, bool> ChannelsEnabled,
    IReadOnlySet<string> ConfiguredRunnerIds);

public sealed record ExpectationConfigurationFault(string Code, string Detail);

/// <summary>S1 scaffold. Digest, activity and reference checks land with the ledger write.</summary>
public static class ExpectationDirectiveDigest
{
    public static string Compute(ExpectationDirectiveSettings directive) =>
        new string('0', 64);

    public static string HashUtf8(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class ExpectationDirectiveActivity
{
    public static bool HasEffects(
        ExpectationWatchdogSettings settings,
        ExpectationDirectiveSettings directive,
        DateTimeOffset now) =>
        true;
}

public static class ExpectationDirectiveReferences
{
    public static IReadOnlyList<ExpectationConfigurationFault> Evaluate(
        ExpectationDirectiveSettings directive,
        ExpectationReferenceCatalog catalog) =>
        [];
}
