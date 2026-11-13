using System.Text.Json;

namespace Antiphon.LandGraphAudit;

// Red-witness adapter: exercise the existing real evaluator without claiming a bound inventory.
internal static class ReleaseInputCommand
{
    internal static int Run(string[] args)
    {
        var legacy = new List<string>();
        for (var i = 0; i < args.Length; i += 2)
            if (args[i] is "--repo" or "--solution") { legacy.Add(args[i]); legacy.Add(args[i + 1]); }
        var result = Program.Main(legacy.ToArray());
        if (result != 0) return result;
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "release-inputs-v1", sourceSha = "", inputManifestDigest = "", complete = false, fallback = "Full", projects = Array.Empty<object>() }));
        return 0;
    }
}
