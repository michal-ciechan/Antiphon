using System.Security.Cryptography;
using System.Text;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>Private launch content. Never serialize this into adoption metadata or diagnostics.</summary>
public sealed record GrokRulesPayload(string Content, int TransportVersion, Guid Generation)
{
    public override string ToString() => $"GrokRulesPayload version={TransportVersion} generation={Generation:N}";
}

/// <summary>Runner-owned metadata; acknowledgement is separately tracked by the server.</summary>
public sealed record GrokRulesReceipt(
    string Path, string Sha256, int ByteCount, int TransportVersion, Guid Generation);

public sealed class GrokRulesSettings
{
    public int MaxFileBytes { get; set; } = 262144;
    public int InitializationTimeoutSeconds { get; set; } = 480;
    public int RefreshTimeoutSeconds { get; set; } = 480;

    public void Validate()
    {
        if (MaxFileBytes <= 0 || InitializationTimeoutSeconds <= 0 || RefreshTimeoutSeconds <= 0)
            throw new ArgumentException("GrokRules settings must be positive.");
    }
}

/// <summary>Bounded, content-free refusal shared by the server and runner.</summary>
public sealed class GrokRulesTransportException(string code, string reason, int statusCode = 409)
    : Exception($"{code}: {reason}")
{
    public string Code { get; } = code;
    public string Reason { get; } = reason;
    public int StatusCode { get; } = statusCode;
}

public static class GrokRulesTransport
{
    public const int Version = 1;
    public const string Capability = "grokRulesFileV1";

    public static byte[] Encode(GrokRulesPayload payload, bool isGrok, int maxFileBytes)
    {
        if (!isGrok) throw InvalidContent("wrong_kind");
        if (payload.TransportVersion != Version || payload.Generation == Guid.Empty)
            throw InvalidContent("invalid_metadata");
        if (payload.Content is null) throw InvalidContent("invalid_unicode");
        if (payload.Content.Contains('\0')) throw InvalidContent("nul");
        if (payload.Content.Contains("{{key:", StringComparison.Ordinal)) throw InvalidContent("unresolved_key");
        byte[] bytes;
        try { bytes = new UTF8Encoding(false, true).GetBytes(payload.Content); }
        catch (EncoderFallbackException) { throw InvalidContent("invalid_unicode"); }
        if (bytes.Length > maxFileBytes)
            throw InvalidContent($"too_large byteCount={bytes.Length} limit={maxFileBytes}");
        return bytes;
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // Intentionally excludes revision/generation: Grok retains this literal on native resume.
    public static string Bootstrap(string path) =>
        $"Antiphon standing rules are in \"{path}\". Before doing any work, read the entire file and follow it as this session's standing instructions. Re-read it when Antiphon requests a refresh and after resume or compaction. If any part is unreadable, stop and report the failure.";

    public static bool HasExplicitRules(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (arg == "--") break;
            if (arg is "--rules" or "--append-system-prompt"
                || arg.StartsWith("--rules=", StringComparison.Ordinal)
                || arg.StartsWith("--append-system-prompt=", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static void ValidateReceipt(GrokRulesReceipt? receipt, Guid sessionId, GrokRulesPayload payload,
        int maxFileBytes)
    {
        var bytes = Encode(payload, true, maxFileBytes);
        if (receipt is null) throw InvalidReceipt("missing");
        if (receipt.TransportVersion != Version) throw InvalidReceipt("version");
        if (receipt.Generation != payload.Generation) throw InvalidReceipt("generation");
        if (receipt.ByteCount != bytes.Length) throw InvalidReceipt("count");
        if (!string.Equals(receipt.Sha256, Hash(bytes), StringComparison.Ordinal)) throw InvalidReceipt("hash");
        ValidateReceiptPath(receipt.Path, sessionId);
        var violation = GrokRulesArgvPolicy.ValidatePayload(Bootstrap(receipt.Path), true, true);
        if (violation is not null) throw InvalidReceipt("bootstrap");
    }

    /// <summary>Validate remote Windows/POSIX grammar without statting or resolving on the server OS.</summary>
    public static void ValidateReceiptPath(string path, Guid sessionId)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOfAny(['\r', '\n', '\0', '"']) >= 0)
            throw InvalidReceipt("path");
        var windows = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        var unc = path.StartsWith("\\\\", StringComparison.Ordinal);
        var separator = windows || unc ? '\\' : '/';
        if (!windows && !unc && !path.StartsWith('/')) throw InvalidReceipt("path");
        if ((windows || unc) && path.Contains('/')) throw InvalidReceipt("path");
        if (!windows && !unc && path.Contains('\\')) throw InvalidReceipt("path");
        var parts = path.Split(separator);
        var prefix = windows ? 1 : unc ? 2 : 1;
        if (parts.Skip(prefix).Any(p => p.Length == 0 || p is "." or "..")) throw InvalidReceipt("path");
        if (unc && (parts.Length < 8 || parts[2] is "?" or ".")) throw InvalidReceipt("path");
        var suffix = string.Join(separator, "instructions", "grok", sessionId.ToString("N"), "rules.md");
        if (!path.EndsWith(separator + suffix, StringComparison.Ordinal)) throw InvalidReceipt("path");
    }

    private static GrokRulesTransportException InvalidContent(string reason) =>
        new("grok_rules_content_invalid", reason, 422);
    private static GrokRulesTransportException InvalidReceipt(string reason) =>
        new("grok_rules_receipt_invalid", reason);
}
