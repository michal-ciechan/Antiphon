using System.Security.Cryptography;
using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    private const string PinsFile = "server/Application/Services/LandVerificationBuildGraph.pins.txt";

    // V-8: target-time inputs are invisible to evaluation, including CARD-1504's inner build parameters.
    [Test]
    [Arguments("relative-dir")]
    [Arguments("identity")]
    [Arguments("task-properties")]
    [Arguments("property-body")]
    [Arguments("target-framework")]
    [Arguments("imported-props")]
    [Timeout(300_000)]
    public async Task C1483_S3_CardWitnessesRefuseAsUnpinnedTargets(string variant)
    {
        var body = variant switch
        {
            "relative-dir" => """<ItemGroup><_StagedHelperFiles Include="../global.json" /></ItemGroup><PropertyGroup><UsePublishedPackages>%(_StagedHelperFiles.RelativeDir)</UsePublishedPackages></PropertyGroup><ItemGroup><None Include="$(UsePublishedPackages)docs/data.json" /></ItemGroup>""",
            "identity" => """<ItemGroup><_AntiphonRepeatLine Include="../README.md" /></ItemGroup><PropertyGroup><PackageTags>%(_AntiphonRepeatLine.Identity)</PackageTags></PropertyGroup><ItemGroup><None Include="$(PackageTags)" /></ItemGroup>""",
            "task-properties" => """<MSBuild Projects="App.csproj" Properties="Configuration=Debug;CustomBeforeMicrosoftCommonTargets=../docs/evil.md" />""",
            "property-body" => """<PropertyGroup><_StagedHelperDest>out;CustomBeforeMicrosoftCommonTargets=../docs/evil.md</_StagedHelperDest></PropertyGroup>""",
            "target-framework" => """<PropertyGroup><TargetFramework>net9.0;CustomBeforeMicrosoftCommonTargets=../docs/evil.md</TargetFramework></PropertyGroup>""",
            "imported-props" => """<MSBuild Projects="$(MSBuildProjectDirectory)/App.csproj" Properties="CustomBeforeMicrosoftCommonTargets=$(MSBuildProjectDirectory)/../docs/evil.md" />""",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var target = $"<Target Name=\"Hidden\" BeforeTargets=\"Build\">{body}</Target>";
        var owner = variant == "imported-props" ? "build/shared.props" : "App/App.csproj";
        var files = new Dictionary<string, string>
        {
            ["README.md"] = "# root\n",
            ["docs/data.json"] = "{}",
            ["docs/evil.md"] = "<Project />",
            ["App/App.csproj"] = SdkProject(owner == "App/App.csproj" ? target : """<Import Project="../build/shared.props" />"""),
        };
        if (owner != "App/App.csproj") files[owner] = $"<Project>{target}</Project>";
        await using var repo = await AuditRepo.CreateAsync(files);
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(1, $"{variant}: {run.Text}");
        run.Lines.ShouldContain($"refused: {owner} Target Hidden (missing pin)", run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // V-9: only XML layout whitespace is insignificant; task arguments and property bytes stay pinned.
    [Test]
    [Timeout(300_000)]
    public async Task C1483_S3_PinnedTargetMatchesOnlyItsHash()
    {
        const string target = """<Target Name="Pinned"><PropertyGroup><X>a  b</X></PropertyGroup><Message Text="safe  words" /></Target>""";
        var pin = Pin("App/App.csproj", "Pinned", target);
        var spaceOnly = target.Replace("a  b", " ");
        var spacePin = Pin("App/App.csproj", "Pinned", spaceOnly);
        var cases = new (string Name, string Body, string? Pins, bool Pass, string Diagnostic)[]
        {
            ("missing", target, null, false, "missing pin"),
            ("exact", target, pin, true, ""),
            ("layout", target.Replace("><", ">\n    <"), pin, true, ""),
            ("body", target.Replace("safe  words", "unsafe words"), pin, false, "changed pin"),
            ("attribute-space", target.Replace("safe  words", "safe words"), pin, false, "changed pin"),
            ("property-space", target.Replace("a  b", "a b"), pin, false, "changed pin"),
            ("empty-property", target.Replace("a  b", " "), pin, false, "changed pin"),
            ("space-only-exact", spaceOnly, spacePin, true, ""),
            ("space-only-removed", target.Replace("a  b", ""), spacePin, false, "changed pin"),
            ("injected-task", target.Replace("<Message", "<MSBuild Projects=\"App.csproj\" Properties=\"CustomBeforeMicrosoftCommonTargets=../docs/evil.md\" /><Message"), pin, false, "changed pin"),
            ("stale", "", pin, false, "stale pin"),
            ("duplicate-row", target, pin + pin, false, "duplicate pin"),
            ("malformed-row", target, "App/App.csproj\tPinned\tnot-a-hash\n", false, "invalid pin"),
            ("duplicate-target", target + target, pin, false, "duplicate element"),
            ("wrong-file", target, Pin("Other/Other.csproj", "Pinned", target), false, "missing pin"),
        };
        foreach (var (name, body, pins, pass, diagnostic) in cases)
        {
            var files = new Dictionary<string, string> { ["App/App.csproj"] = SdkProject(body) };
            if (pins is not null) files[PinsFile] = pins;
            await using var repo = await AuditRepo.CreateAsync(files);
            var run = await repo.RunToolAsync();
            run.ExitCode.ShouldBe(pass ? 0 : 1, $"{name}: {run.Text}");
            if (pass) run.Lines.ShouldContain("approved", run.Text);
            else
            {
                run.Text.ShouldContain(diagnostic, Case.Sensitive, $"{name}: {run.Text}");
                run.Lines.ShouldNotContain("approved", run.Text);
            }
            repo.AssertUntouched();
        }

        // A pin in a non-built file is still mandatory, and a valid pin never bypasses item refusal.
        await using var outside = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject(""),
            ["unused.props"] = $"<Project>{target}</Project>",
        });
        var outsideRun = await outside.RunToolAsync();
        outsideRun.ExitCode.ShouldBe(1, outsideRun.Text);
        outsideRun.Text.ShouldContain("unused.props Target Pinned (missing pin)");
        outside.AssertUntouched();

        await using var item = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject(target + """<ItemGroup><None Include="../README.md" /></ItemGroup>"""),
            ["README.md"] = "# root",
            [PinsFile] = pin,
        });
        var itemRun = await item.RunToolAsync();
        itemRun.ExitCode.ShouldBe(1, itemRun.Text);
        itemRun.Text.ShouldContain(" None ");
        itemRun.Lines.ShouldNotContain("approved", itemRun.Text);
        item.AssertUntouched();

        await using var untracked = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject(target),
        }, untrackedFiles: new() { [PinsFile] = pin });
        var untrackedRun = await untracked.RunToolAsync();
        untrackedRun.ExitCode.ShouldBe(1, untrackedRun.Text);
        untrackedRun.Text.ShouldContain("untracked pins file");
        untrackedRun.Lines.ShouldNotContain("approved", untrackedRun.Text);
        untracked.AssertUntouched();

        // Definition names accepted by the import policy must not evade the scan via an unusual suffix.
        await using var custom = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject("""<Import Project="../Directory.Build.custom" />"""),
            ["Directory.Build.custom"] = $"<Project>{target}</Project>",
        });
        var customRun = await custom.RunToolAsync();
        customRun.ExitCode.ShouldBe(1, customRun.Text);
        customRun.Text.ShouldContain("Directory.Build.custom Target Pinned (missing pin)");
        custom.AssertUntouched();
    }

    // V-10: pin both the active and inactive OS branches, at their structural element identity.
    [Test]
    [Arguments("'$(OS)' == 'Windows_NT'")]
    [Arguments("$([MSBuild]::IsOSPlatform('Windows'))")]
    [Arguments("'$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)' == 'never'")]
    [Timeout(300_000)]
    public async Task C1483_S3_OsConditionedElementNeedsPin(string condition)
    {
        var element = $"<PropertyGroup Condition=\"{condition}\"><X>safe</X></PropertyGroup>";
        var pin = Pin("App/App.csproj", "/Project[1]/PropertyGroup[2]", element);
        foreach (var variant in new[] { "missing", "exact", "changed", "moved" })
        {
            var body = variant == "changed" ? element.Replace("safe", "changed") : element;
            if (variant == "moved") body = "<PropertyGroup><Z>new</Z></PropertyGroup>" + body;
            var files = new Dictionary<string, string> { ["App/App.csproj"] = SdkProject(body) };
            if (variant != "missing") files[PinsFile] = pin;
            await using var repo = await AuditRepo.CreateAsync(files);
            var run = await repo.RunToolAsync();
            // CARD-1528 already refuses OS function identities even after an exact pin.
            // Only the plain-property expression may reach evaluation; a pin is no exemption.
            var approves = variant == "exact" && condition == "'$(OS)' == 'Windows_NT'";
            run.ExitCode.ShouldBe(approves ? 0 : 1, $"{variant}: {run.Text}");
            if (approves) run.Lines.ShouldContain("approved", run.Text);
            else
            {
                run.Text.ShouldContain("App/App.csproj /Project[1]/PropertyGroup[", Case.Sensitive, run.Text);
                run.Text.ShouldContain(variant == "exact" ? "unsupported-function" :
                    variant == "changed" ? "(changed pin)" : "(missing pin)", Case.Sensitive, run.Text);
                run.Lines.ShouldNotContain("approved", run.Text);
            }
            repo.AssertUntouched();
        }
    }

    // The expected bytes are literal normalized fixture XML, independent of the production normalizer.
    private static string Pin(string file, string element, string normalizedXml) =>
        $"{file}\t{element}\t{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedXml)))}\n";
}
