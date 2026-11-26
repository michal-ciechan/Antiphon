#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly;

/// <summary>Canonical encoding for the diagnostic P2 proposal.</summary>
public static partial class ReleaseImpactIntegrity
{
    internal const string Format = "antiphon-p2-integrity-v1";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] RootBytes(JsonElement root)
    {
        using var bytes = new MemoryStream();
        // Initial witness: transport order is incorrectly treated as schema order.
        foreach (var member in root.EnumerateObject())
            RcBoundary.AddString(bytes, member.Value.ToString(), Utf8);
        return bytes.ToArray();
    }
}
