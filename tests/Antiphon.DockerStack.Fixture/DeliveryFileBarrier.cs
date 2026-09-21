using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.DockerStack.Fixture;

public sealed record BarrierArm(
    string CaseName,
    string Cut,
    string RowId,
    string Attempt,
    string HostNonce);

public sealed record BarrierWait(bool Completed, string? Code);

public sealed class DeliveryFileBarrier
{
    private readonly string _directory;
    private readonly HashSet<string> _consumed;

    public DeliveryFileBarrier(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        _consumed = LoadConsumed();
    }

    public BarrierArm? Expected { get; set; }

    public bool ReleaseMatches(BarrierArm arm, BarrierArm release) =>
        string.Equals(arm.CaseName, release.CaseName, StringComparison.Ordinal)
        && string.Equals(arm.Cut, release.Cut, StringComparison.Ordinal)
        && string.Equals(arm.RowId, release.RowId, StringComparison.Ordinal)
        && string.Equals(arm.Attempt, release.Attempt, StringComparison.Ordinal)
        && string.Equals(arm.HostNonce, release.HostNonce, StringComparison.Ordinal);

    public async Task<BarrierWait> ArmAsync(BarrierArm arm, CancellationToken cancellationToken)
    {
        if (Expected is not null && !ReleaseMatches(Expected, arm))
            return new BarrierWait(false, "ForeignArm");
        var key = Identity(arm);
        if (_consumed.Contains(key))
            return new BarrierWait(false, "ConsumedArm");
        var reached = Path.Combine(_directory, "reached-" + Sha(key) + ".json");
        var payload = JsonSerializer.Serialize(arm);
        var digest = Sha(payload);
        var record = JsonSerializer.Serialize(new { arm, digest });
        await File.WriteAllTextAsync(reached, record, cancellationToken);
        var releasePath = Path.Combine(_directory, "release-" + Sha(key) + ".json");
        while (!cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(releasePath))
            {
                var release = JsonSerializer.Deserialize<BarrierArm>(await File.ReadAllTextAsync(releasePath, cancellationToken));
                if (release is not null && ReleaseMatches(arm, release))
                {
                    _consumed.Add(key);
                    await File.WriteAllTextAsync(Path.Combine(_directory, "consumed.txt"), string.Join('\n', _consumed), cancellationToken);
                    return new BarrierWait(true, null);
                }
            }

            try
            {
                await Task.Delay(15, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new BarrierWait(false, "Incomplete");
            }
        }

        return new BarrierWait(false, "Incomplete");
    }

    public static string ReleaseFileName(BarrierArm arm) =>
        "release-" + Sha(string.Join('|', arm.CaseName, arm.Cut, arm.RowId, arm.Attempt, arm.HostNonce)) + ".json";

    public static bool DigestMatches(string record)
    {
        using var doc = JsonDocument.Parse(record);
        var digest = doc.RootElement.GetProperty("digest").GetString();
        var arm = doc.RootElement.GetProperty("arm").GetRawText();
        return digest == Sha(arm);
    }

    private HashSet<string> LoadConsumed()
    {
        var path = Path.Combine(_directory, "consumed.txt");
        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.Ordinal);
        return File.ReadAllLines(path).Where(line => line.Length > 0).ToHashSet(StringComparer.Ordinal);
    }

    private static string Identity(BarrierArm arm) =>
        string.Join('|', arm.CaseName, arm.Cut, arm.RowId, arm.Attempt, arm.HostNonce);

    private static string Sha(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
