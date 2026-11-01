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
        var directory = Quote(Path.Combine(root, "dir"));
        var payload = Quote(Path.Combine(root, "payload.dat"));
        return linuxCommand switch
        {
            "rm -f -- tracked.txt && mv -- user-dir tracked.txt && git --git-dir=.git --work-tree=. add -A -- tracked.txt && git --git-dir=.git --work-tree=. restore --staged -- tracked.txt/unstaged.txt" =>
                Body(root, "rm -f -- tracked.txt && mv -- user-dir tracked.txt")
                + Git(root, "add -A -- tracked.txt")
                + Git(root, "restore --staged -- tracked.txt/unstaged.txt"),
            "rm -rf -- dir && mv -- payload.dat dir && git --git-dir=.git --work-tree=. add -A -- dir" =>
                "$ErrorActionPreference = 'Stop'\n"
                + $"if (-not [System.IO.Directory]::Exists({directory})) {{ throw 'fixture dir missing' }}\n"
                + $"if (-not [System.IO.File]::Exists({payload})) {{ throw 'payload.dat missing' }}\n"
                + "[System.IO.Directory]::Delete(" + directory + ", $true)\n"
                + $"[System.IO.File]::Move({payload}, {directory})\n"
                + $"if ([System.IO.File]::Exists({payload})) {{ throw 'move left payload.dat' }}\n"
                + Git(root, "add -A -- dir"),
            "ln -sfn -- link-target.txt tracked.txt && git --git-dir=.git --work-tree=. add -- tracked.txt" =>
                "$ErrorActionPreference = 'Stop'\n"
                + $"if (-not [System.IO.File]::Exists({tracked})) {{ throw 'tracked.txt missing before link' }}\n"
                + $"[System.IO.File]::Delete({tracked})\n"
                + "try {\n"
                + $"  $link = [System.IO.File]::CreateSymbolicLink({tracked}, 'link-target.txt')\n"
                + "} catch {\n"
                + "  throw ('file-symlink-prerequisite: native file-link creation required; on Windows enable Developer Mode or grant symbolic-link privilege: ' + $_.Exception.Message)\n"
                + "}\n"
                + "if ($link.LinkTarget -cne 'link-target.txt') { throw 'file-symlink-prerequisite: real file link missing' }\n"
                + Git(root, "add -- tracked.txt"),
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
                + "[System.IO.File]::Move(" + userMove + ", " + tracked + ", $true)\n"
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
            _ => throw new InvalidOperationException("Unknown shape fixture command: " + linuxCommand),
        };
    }

    private static string WriteAscii(string quotedPath, string text) =>
        "$ErrorActionPreference = 'Stop'\n" + AsciiLine(quotedPath, text);

    private static string AsciiLine(string quotedPath, string text) =>
        "[System.IO.File]::WriteAllBytes(" + quotedPath + ", [System.Text.Encoding]::ASCII.GetBytes('" + text + "'))\n";

    private static string Git(string root, string arguments) =>
        "Set-Location -LiteralPath " + Quote(root) + "\n"
        + "& git --git-dir=.git --work-tree=. " + arguments + "\n"
        + "if ($LASTEXITCODE -ne 0) { throw \"fixture git exited $LASTEXITCODE\" }\n";

    private static string Quote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
