namespace Antiphon.Card0490.NativeHarness;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Antiphon.Card0490.NativeHarness: pass -Ordinary or sourced arguments via scripts/test-card0490-native.ps1.");
            return 2;
        }

        if (NativeInputPolicy.OrdinaryAndBindingConflict(args))
        {
            Console.Error.WriteLine("Sourced refusal never downgrades to ordinary.");
            return 3;
        }

        var lockFile = args.SkipWhile(a => !string.Equals(a, "-AssetProfile", StringComparison.OrdinalIgnoreCase))
            .Skip(1).FirstOrDefault();
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
