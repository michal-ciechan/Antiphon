using System.Text;

namespace Antiphon.Tests.Checkpoints;

// Windows build-target mutations. Linux keeps the historical Exec command unless
// ANTIPHON_C1199_WINDOWS_SHAPE_FIXTURE=1 forces this writer.
internal static class CheckpointShapeWindowsFixture
{
    public const string ForceVariable = "ANTIPHON_C1199_WINDOWS_SHAPE_FIXTURE";

    public static bool Use() =>
        OperatingSystem.IsWindows()
        || string.Equals(Environment.GetEnvironmentVariable(ForceVariable), "1", StringComparison.Ordinal);

    public static string Write(string root, string linuxCommand)
    {
        var path = Path.Combine(root, "App", "mutate-shape.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(Body(root, linuxCommand)));
        return "pwsh -NoProfile -NonInteractive -File \"" + path + "\"";
    }

    private static string Body(string root, string linuxCommand)
    {
        var tracked = Quote(Path.Combine(root, "tracked.txt"));
        var userDir = Quote(Path.Combine(root, "user-dir"));
        var userMove = Quote(Path.Combine(root, "user-move.txt"));
        return linuxCommand switch
        {
            "rm -f -- tracked.txt && mv -- user-dir tracked.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + $"if (-not [System.IO.File]::Exists({tracked})) {{ throw 'tracked.txt missing' }}\n"
                + $"[System.IO.File]::Delete({tracked})\n"
                + $"if (-not [System.IO.Directory]::Exists({userDir})) {{ throw 'user-dir missing' }}\n"
                + $"[System.IO.Directory]::Move({userDir}, {tracked})\n",
            "printf '%s' MUTATED > tracked.txt" or "printf %s MUTATED > tracked.txt" =>
                WriteAscii(tracked, "MUTATED"),
            "rm -f -- tracked.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + $"if (-not [System.IO.File]::Exists({tracked})) {{ throw 'tracked.txt missing before delete' }}\n"
                + $"[System.IO.File]::Delete({tracked})\n"
                + $"if ([System.IO.File]::Exists({tracked})) {{ throw 'tracked.txt still exists' }}\n",
            "mv -f -- user-move.txt tracked.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + $"if (-not [System.IO.File]::Exists({userMove})) {{ throw 'user-move.txt missing' }}\n"
                + $"[System.IO.File]::Move({userMove}, {tracked}, $$true)\n"
                + $"if ([System.IO.File]::Exists({userMove})) {{ throw 'move left the source' }}\n",
            "rm -f -- user-move.txt && printf %s MUTATED > tracked.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + $"if (-not [System.IO.File]::Exists({userMove})) {{ throw 'user-move.txt missing' }}\n"
                + $"[System.IO.File]::Delete({userMove})\n"
                + $"if ([System.IO.File]::Exists({userMove})) {{ throw 'user-move.txt still exists' }}\n"
                + AsciiLine(tracked, "MUTATED"),
            "printf %s CHANGED-USER > user-move.txt && printf %s MUTATED > tracked.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + AsciiLine(userMove, "CHANGED-USER")
                + AsciiLine(tracked, "MUTATED"),
            "git --git-dir=.git --work-tree=. add -- user-keep.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + "Set-Location -LiteralPath " + Quote(root) + "\n"
                + "& git --git-dir=.git --work-tree=. add -- user-keep.txt\n"
                + "if ($LASTEXITCODE -ne 0) { throw \"git add exited $LASTEXITCODE\" }\n",
            _ => throw new InvalidOperationException("S-C2 fixture is not ported: " + linuxCommand),
        };
    }

    private static string WriteAscii(string quotedPath, string text) =>
        "$ErrorActionPreference = 'Stop'\n" + AsciiLine(quotedPath, text);

    private static string AsciiLine(string quotedPath, string text) =>
        "[System.IO.File]::WriteAllBytes(" + quotedPath + ", [System.Text.Encoding]::ASCII.GetBytes('" + text + "'))\n";

    private static string Quote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
