using Microsoft.Testing.Platform.Builder;

namespace Antiphon.Checkpoints.LifecycleHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Every lifecycle caller supplies an owned TMPDIR/TEMP sandbox and joins this
        // process before reclaiming it. The platform's default is beside this DLL,
        // which would modify checkpoint build output after its creation ledger is sealed.
        if (!args.Contains("--results-directory", StringComparer.Ordinal))
            args = [.. args, "--results-directory", Path.Combine(Path.GetTempPath(), "TestResults")];

        var builder = await TestApplication.CreateBuilderAsync(args);
        SelfRegisteredExtensions.AddSelfRegisteredExtensions(builder, args);
        using var app = await builder.BuildAsync();
        return await app.RunAsync();
    }
}
