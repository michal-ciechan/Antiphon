using System.Text;

namespace Antiphon.Tests.Checkpoints;

// Test-only Windows mutation writer. The red witness emits a no-op script so the
// real build target can run without arranging the shape.
internal static class CheckpointShapeWindowsFixture
{
    private static readonly string[] OrdinaryMutations =
    [
        "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
        "printf '%s' MUTATED > tracked.txt",
        "rm -f -- tracked.txt",
        "mv -f -- user-move.txt tracked.txt",
        "rm -f -- user-move.txt && printf %s MUTATED > tracked.txt",
        "printf %s CHANGED-USER > user-move.txt && printf %s MUTATED > tracked.txt",
    ];

    public static bool IsOrdinaryMutation(string command)
    {
        foreach (var candidate in OrdinaryMutations)
        {
            if (string.Equals(candidate, command, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static string WriteNoOp(string root)
    {
        var path = Path.Combine(root, "App", "mutate-shape.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("$ErrorActionPreference = 'Stop'\nexit 0\n"));
        return "pwsh -NoProfile -NonInteractive -File \"" + path + "\"";
    }
}
