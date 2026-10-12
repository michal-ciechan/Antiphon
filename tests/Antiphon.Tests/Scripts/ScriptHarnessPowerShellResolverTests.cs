using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class ScriptHarnessPowerShellResolverTests
{
    [Test]
    public void Path_table_selects_real_shell_or_named_refusal()
    {
        // Host-native path operations keep this table portable; Windows selection is
        // injected, so neither aliases nor the host's PATH/files are consulted.
        var cwd = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "c1171-working"));
        string Exe(string directory, bool windows = true) =>
            Path.GetFullPath(Path.Combine(cwd, directory, windows ? "pwsh.exe" : "pwsh"));
        var real = Exe("installed");
        var second = Exe("second");
        var alias = Exe("Microsoft/WindowsApps");
        var upperAlias = Exe("Microsoft/wInDoWsApPs");
        var package = Exe("WindowsApps/PowerShell-package");
        var empty = Exe("empty");
        var reparse = Exe("linked");
        var emptyReparse = Exe("alias-outside");
        var lookalike = Exe("WindowsAppsTools");
        var fallback = Exe("ProgramFiles/PowerShell/7");
        var fallbackX86 = Exe("ProgramFilesX86/PowerShell/7");
        var linuxLink = Exe("linux-link", false);
        var files = new Dictionary<string, ScriptHarnessProcess.PowerShellFile>(StringComparer.OrdinalIgnoreCase)
        {
            [real] = new(FileAttributes.Normal, 100),
            [second] = new(FileAttributes.Normal, 200),
            [alias] = new(FileAttributes.ReparsePoint, 0),
            [upperAlias] = new(FileAttributes.ReparsePoint, 0),
            [package] = new(FileAttributes.Normal, 100),
            [empty] = new(FileAttributes.Normal, 0),
            [reparse] = new(FileAttributes.ReparsePoint, 100),
            [emptyReparse] = new(FileAttributes.ReparsePoint, 0),
            [lookalike] = new(FileAttributes.Normal, 100),
            [fallback] = new(FileAttributes.Normal, 100),
            [fallbackX86] = new(FileAttributes.Normal, 100),
            [linuxLink] = new(FileAttributes.ReparsePoint, 100)
        };
        string Dir(string executable) => Path.GetDirectoryName(executable)!;
        string Join(params string[] directories) => string.Join(Path.PathSeparator, directories);
        var rows = new (string Name, string Path, string? Expected, string[] Fallbacks, bool Windows)[]
        {
            ("alias-first", Join(Dir(alias), Dir(real)), real, [], true),
            ("alias-only", Dir(alias), null, [], true),
            ("real-only", Dir(real), real, [], true),
            ("first-real", Join(Dir(second), Dir(real)), second, [], true),
            ("relative", "installed", real, [], true),
            ("trailing-slash", Dir(real) + Path.DirectorySeparatorChar, real, [], true),
            ("quoted", " \"" + Dir(real) + Path.DirectorySeparatorChar + "\" ", real, [], true),
            ("missing", Join(Dir(Exe("absent")), Dir(real)), real, [], true),
            ("case-alias", Join(Dir(upperAlias), Dir(real)), real, [], true),
            ("windowsapps-package", Join(Dir(package), Dir(real)), real, [], true),
            ("lookalike", Dir(lookalike), lookalike, [], true),
            ("empty-regular", Join(Dir(empty), Dir(real)), real, [], true),
            ("empty-reparse-outside", Join(Dir(emptyReparse), Dir(real)), real, [], true),
            ("nonempty-reparse", Join(Dir(reparse), Dir(real)), real, [], true),
            ("malformed", Join("bad\0directory", Dir(real)), real, [], true),
            ("fallback-order", "", fallback, [fallback, fallbackX86], true),
            ("second-fallback", "", fallbackX86, [Exe("missing-fallback"), fallbackX86], true),
            ("alias-fallback", Dir(alias), fallback, [fallback], true),
            ("path-before-fallback", Dir(real), real, [fallback], true),
            ("no-shell", "", null, [Exe("absent")], true),
            ("linux-symlink", Dir(linuxLink), linuxLink, [], false)
        };
        var failures = new List<string>();
        foreach (var row in rows)
        {
            try
            {
                var actual = ScriptHarnessProcess.ResolvePowerShell(row.Path, row.Windows, cwd,
                    candidate => files.TryGetValue(candidate, out var file) ? file : null, row.Fallbacks);
                actual.ShouldBe(row.Expected, row.Name);
            }
            catch (FileNotFoundException error) when (row.Expected is null)
            {
                try { error.Message.ShouldContain("ScriptHarnessPowerShellNotFound", Case.Sensitive, row.Name); }
                catch (Exception assertion) { failures.Add(row.Name + ": " + assertion.Message); }
            }
            catch (Exception error) { failures.Add(row.Name + ": " + error.Message); }
        }
        failures.ShouldBeEmpty("All independent PATH cases must pass:\n" + string.Join("\n", failures));
    }
}
