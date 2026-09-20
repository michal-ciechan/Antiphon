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

        if (args.Contains("-Ordinary", StringComparer.OrdinalIgnoreCase)
            && args.Contains("-BindingFile", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Sourced refusal never downgrades to ordinary.");
            return 3;
        }

        return 0;
    }
}
