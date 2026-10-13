using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.SessionRunner.Contracts;

// C1032_AdmissionBoundRebaseProof exercises this certificate on a server-produced command.
// The phone-home key is configured by the recipient/server, not carried in the command.
public static class CompletedCardWorkspaceLandingAuthority
{
    public static CompletedCardWorkspaceCommand Sign(CompletedCardWorkspaceCommand command, Guid landingId, string key)
    {
        if (landingId == Guid.Empty || string.IsNullOrWhiteSpace(key)) throw new ArgumentException("landing_authority_unavailable");
        var unsigned = command with { Proof = command.Proof with { ProofIdentity = "landed-v1:" + landingId.ToString("D") } };
        return unsigned with { Proof = unsigned.Proof with { ProofIdentity = unsigned.Proof.ProofIdentity + ":" + Mac(unsigned, key) } };
    }

    public static bool Verify(CompletedCardWorkspaceCommand command, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var parts = command.Proof.ProofIdentity.Split(':');
        if (parts.Length != 3 || parts[0] != "landed-v1" || !Guid.TryParse(parts[1], out var id) || id == Guid.Empty) return false;
        var unsigned = command with { Proof = command.Proof with { ProofIdentity = "landed-v1:" + id.ToString("D") } };
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[2]), Convert.FromHexString(Mac(unsigned, key))); }
        catch (FormatException) { return false; }
    }

    private static string Mac(CompletedCardWorkspaceCommand command, string key)
    {
        var digest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes("Antiphon.CompletedCardWorkspace.Landing.v1\n" + digest)));
    }
}
