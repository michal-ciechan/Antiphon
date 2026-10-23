using Antiphon.Tests.Scripts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

[Category("Unit")]
public sealed class RunnerProbePowerShellFixtureTests
{
    [Test]
    [Arguments(@"C:\Users\dev\AppData\Local\Microsoft\WindowsApps\pwsh.exe", true)]
    [Arguments(@"C:\Users\dev\AppData\Local\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe", true)]
    [Arguments(@"C:\USERS\dev\WINDOWSAPPS\powershell.exe", true)]
    [Arguments("C:/Users/dev/Microsoft/WindowsApps/pwsh.exe", true)]
    [Arguments(@"C:\Users\dev\Microsoft/WindowsApps\pwsh.exe", true)]
    [Arguments(@"C:\Program Files\PowerShell\7\pwsh.exe", false)]
    [Arguments(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false)]
    [Arguments(@"C:\tools\WindowsAppsBackup\pwsh.exe", false)]
    [Arguments(@"C:\tools\NotWindowsApps\pwsh.exe", false)]
    [Arguments("", false)]
    public void WindowsApps_component_is_classified_independently_of_host(string path, bool alias)
        => ScriptHarnessProcessFixture.IsWindowsAppExecutionAlias(path).ShouldBe(alias);

    [Test]
    [Arguments("alias-first")]
    [Arguments("alias-only")]
    [Arguments("no-pwsh")]
    [Arguments("empty-path")]
    [Arguments("reparse-first")]
    public void Probe_selects_regular_install_or_refuses_absence(string variant)
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-pwsh-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var aliasDirectory = Path.Combine(root, "Microsoft", "WindowsApps");
            var installDirectory = Path.Combine(root, "PowerShell", "7");
            var linkDirectory = Path.Combine(root, "linked");
            var emptyDirectory = Path.Combine(root, "no-pwsh");
            foreach (var directory in new[] { aliasDirectory, installDirectory, linkDirectory, emptyDirectory })
                Directory.CreateDirectory(directory);
            var installed = Path.Combine(installDirectory, "pwsh.exe");
            var alias = Path.Combine(aliasDirectory, "pwsh.exe");
            File.WriteAllText(installed, "fixture installed executable");
            File.WriteAllText(alias, "fixture alias; must never be selected");
            (File.GetAttributes(installed) & FileAttributes.ReparsePoint).ShouldBe((FileAttributes)0);

            var searchPath = variant switch
            {
                "alias-first" => string.Join(Path.PathSeparator, aliasDirectory, installDirectory),
                "alias-only" => aliasDirectory,
                "no-pwsh" => emptyDirectory,
                "empty-path" => "",
                "reparse-first" => string.Join(Path.PathSeparator, linkDirectory, installDirectory),
                _ => throw new ArgumentOutOfRangeException(nameof(variant))
            };
            if (variant == "reparse-first")
            {
                var link = Path.Combine(linkDirectory, "pwsh.exe");
                File.CreateSymbolicLink(link, installed);
                (File.GetAttributes(link) & FileAttributes.ReparsePoint).ShouldBe(FileAttributes.ReparsePoint);
            }

            if (variant is "alias-only" or "no-pwsh" or "empty-path")
            {
                var error = Should.Throw<FileNotFoundException>(() =>
                    RunnerProcessProbeTests.PowerShellExecutable(searchPath));
                error.Message.ShouldContain("regular installed pwsh.exe on PATH");
            }
            else
            {
                var selected = RunnerProcessProbeTests.PowerShellExecutable(searchPath);
                selected.ShouldBe(installed);
                selected.ShouldNotBe(alias);
                ScriptHarnessProcessFixture.IsWindowsAppExecutionAlias(selected).ShouldBeFalse();
                (File.GetAttributes(selected) & FileAttributes.ReparsePoint).ShouldBe((FileAttributes)0);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
