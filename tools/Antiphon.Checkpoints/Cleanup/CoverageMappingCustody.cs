using System.Globalization;
using System.Security.Cryptography;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// A build-only, in-memory append seal. It never changes the generic creation record
// or imports the separate durable tool-copy authority. A seal cannot be refreshed.
internal sealed class CoverageMappingCustody(CheckpointCleanupContext context)
{
    private const string Prefix = ".msCoverageExtensionSourceRootsMapping_";

    public void Seal(string path, OutputCustody.Window window)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length == Prefix.Length
            || name[Prefix.Length..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            return;
        var original = context.Registry.Identity(path);
        var content = context.Registry.Content(path);
        if (original is not { Kind: "file", Inode: not 0 } initial || content is null) return;
        try
        {
            var before = context.Io.Stat(path);
            var entry = OutputCustody.Stat(context, path);
            if (before.Device != initial.Device || before.Inode != initial.Inode
                || entry is null || OutputCustody.Refusal(entry.Value, window) is not null) return;
            var colon = content.IndexOf(':');
            if (colon < 1 || !int.TryParse(content.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var length)) return;
            var bytes = ReceiptFile.ReadBytes(context, path);
            if (bytes.Length <= length || Hash(bytes.AsSpan(0, length)) != content[(colon + 1)..]) return;
            if (context.Io.Stat(path) != before) return;
            context.Registry.RecordMappingSeal(path, new(before, bytes.Length + ":" + Hash(bytes)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public bool Matches(string path)
    {
        var seal = context.Registry.MappingSeal(path);
        if (seal is null || context.Io.Stat(path) != seal.Stat) return false;
        var bytes = ReceiptFile.ReadBytes(context, path);
        return seal.Content == bytes.Length + ":" + Hash(bytes) && context.Io.Stat(path) == seal.Stat;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
