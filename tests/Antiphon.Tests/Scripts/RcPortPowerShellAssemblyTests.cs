using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RcPortPowerShellAssemblyTests
{
    private const string Dll = "System.Management.Automation.dll";

    [Test]
    public void WindowsUsesTheInstalledPwshDirectory()
    {
        var tools = Path.Combine("Users", "tester", "bin");
        var installed = Path.Combine("Program Files", "PowerShell", "7");
        var assembly = Path.Combine(installed, Dll);
        var files = new HashSet<string>
        {
            Path.Combine(tools, "pwsh.exe"),
            Path.Combine(installed, "pwsh.exe"),
            assembly,
        };

        RcPortPowerShellAssembly.Resolve(null, [tools, installed], windows: true, files.Contains, _ => null)
            .ShouldBe(assembly);
    }

    [Test]
    public void LinuxPrefersPsHomeThenTheRealPwshDirectory()
    {
        const string psHome = "/custom/powershell";
        const string bin = "/usr/local/bin";
        const string target = "/somewhere/powershell/pwsh";
        var overrideAssembly = Path.Combine(psHome, Dll);
        var linkedAssembly = Path.Combine(Path.GetDirectoryName(target)!, Dll);
        var files = new HashSet<string> { Path.Combine(bin, "pwsh"), overrideAssembly, linkedAssembly };

        RcPortPowerShellAssembly.Resolve(psHome, [bin], windows: false, files.Contains, _ => target)
            .ShouldBe(overrideAssembly);
        RcPortPowerShellAssembly.Resolve("/missing", [bin], windows: false, files.Contains, _ => target)
            .ShouldBe(linkedAssembly);
    }

    [Test]
    public void LinuxTriesBothStandardInstallDirectoriesInOrder()
    {
        var standard = Path.Combine("/opt/microsoft/powershell/7", Dll);
        var image = Path.Combine("/opt/powershell", Dll);
        var files = new HashSet<string> { standard, image };

        RcPortPowerShellAssembly.Resolve(null, [], windows: false, files.Contains, _ => null).ShouldBe(standard);
        files.Remove(standard);
        RcPortPowerShellAssembly.Resolve(null, [], windows: false, files.Contains, _ => null).ShouldBe(image);
    }

    [Test]
    public void MissingAssemblyNamesTheCorpusAndListsTheProbedPaths()
    {
        const string psHome = "/missing";
        const string bin = "/usr/bin";
        var pwsh = Path.Combine(bin, "pwsh");
        var error = Should.Throw<InvalidOperationException>(() =>
            RcPortPowerShellAssembly.Resolve(psHome, [bin], windows: false, path => path == pwsh, _ => null));

        error.Message.ShouldContain("RC port corpus could not locate " + Dll);
        error.Message.ShouldContain(Path.Combine(psHome, Dll));
        error.Message.ShouldContain(Path.Combine(bin, Dll));
        error.Message.ShouldContain(Path.Combine("/opt/microsoft/powershell/7", Dll));
        error.Message.ShouldContain(Path.Combine("/opt/powershell", Dll));
    }
}
