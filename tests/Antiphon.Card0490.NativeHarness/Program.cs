namespace Antiphon.Card0490.NativeHarness;

/// <summary>
/// The CARD-0490 D-12 native host, one PC and phase per invocation:
/// dotnet run --project tests/Antiphon.Card0490.NativeHarness -- -Pc PC-28 -Phase baseline -SnapshotRoot &lt;dir&gt;
/// -AssetProfile &lt;assets.lock.json&gt; -EvidenceRoot &lt;dir&gt; (-Ordinary | -BindingFile &lt;file&gt;).
/// scripts/test-card0490-native.ps1 wrapped this until unit 39 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md;
/// its checks are <see cref="NativeInputPolicy.Admit"/>.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var refusal = NativeInputPolicy.Admit(args);
        if (refusal is not null)
        {
            Console.Error.WriteLine(refusal.Message);
            return refusal.ExitCode;
        }

        var lockFile = NativeInputPolicy.Value(args, "-AssetProfile");
        if (lockFile is not null && File.Exists(lockFile))
        {
            var text = File.ReadAllText(lockFile);
            if (text.Contains("pending-operator-pin", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("CARD-0490 native assets are not pinned (pending-operator-pin).");
                return 4;
            }
        }

        return 0;
    }
}
