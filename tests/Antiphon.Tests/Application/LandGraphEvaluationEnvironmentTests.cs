using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    private sealed record EnvironmentFixture(string Id, string Group, int Expected, string Reason,
        string Owner, Dictionary<string, string> Files, Dictionary<string, string>? Environment);

    private static EnvironmentFixture[] EnvironmentFixtures() =>
        JsonSerializer.Deserialize<EnvironmentFixture[]>(File.ReadAllText(Path.Combine(RepoRoot(),
                "tests/Antiphon.Tests/Application/Fixtures/land-graph-c1540.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static Dictionary<string, string> EnvironmentFiles(string body) => new()
    {
        ["App/App.csproj"] = SdkProject("<PropertyGroup><EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>" + body),
        ["Directory.Build.props"] = "<Project />",
        ["Directory.Build.targets"] = "<Project />",
        ["App/Program.cs"] = "// safe",
        ["docs/a.md"] = "docs",
    };

    private static void AssertEnvironmentVerdict(ToolRun run, int expected, string reason, string owner)
    {
        run.ExitCode.ShouldBe(expected, run.Text);
        run.Text.ShouldContain(reason, Case.Sensitive, run.Text);
        run.Text.ShouldNotContain("evaluation-failed:", Case.Sensitive, run.Text);
        if (expected == 0) run.Lines.ShouldContain("approved", run.Text);
        else
        {
            run.Text.ShouldContain(owner, Case.Sensitive, run.Text);
            run.Lines.ShouldNotContain("approved", run.Text);
        }
    }

    [Test]
    [Arguments("gate")]
    [Arguments("include")]
    [Arguments("path")]
    [Timeout(60_000)]
    public async Task C1540_EnvironmentPropertyRefuses(string variant)
    {
        var fixture = EnvironmentFixtures().Single(f => f.Id == variant);
        await using var repo = await AuditRepo.CreateAsync(fixture.Files);
        // PATH is also required to launch git. With no executable search path the tool
        // fails its prerequisite, before graph inspection. Exercise its read with a usable PATH.
        foreach (var state in variant == "path" ? new[] { "set" } : new[] { "absent", "empty", "set" })
        {
            var environment = fixture.Environment!.ToDictionary(p => p.Key,
                p => state == "absent" ? null : state == "empty" ? "" :
                    p.Key == "PATH" ? Environment.GetEnvironmentVariable("PATH") : p.Value);
            var run = await repo.RunToolWithEnvironmentAsync(environment);
            AssertEnvironmentVerdict(run, 1, fixture.Reason, fixture.Owner);
        }
        repo.AssertUntouched();
    }

    [Test]
    [Arguments("modified")]
    [Arguments("created")]
    [Arguments("accessed")]
    [Arguments("stamp-gate")]
    [Arguments("item-function")]
    [Arguments("directory")]
    [Timeout(60_000)]
    public async Task C1540_TimestampMetadataRefuses(string variant)
    {
        var fixture = EnvironmentFixtures().Single(f => f.Id == variant);
        await using var repo = await AuditRepo.CreateAsync(fixture.Files);
        if (variant == "stamp-gate")
            File.SetLastWriteTimeUtc(repo.Full("App/Program.cs"), new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var run = await repo.RunToolAsync();
        AssertEnvironmentVerdict(run, 1, fixture.Reason, fixture.Owner);
        repo.AssertUntouched();
    }

    [Test]
    [Arguments("unpinned")]
    [Arguments("changed")]
    [Timeout(60_000)]
    public async Task C1540_KnobGateNeedsPin(string variant)
    {
        const string element = """<ItemGroup Condition="'$(UsePublishedPackages)'=='true'"><Probe Include="safe.txt" /></ItemGroup>""";
        var files = EnvironmentFiles(variant == "changed" ? element.Replace("safe.txt", "../docs/a.md") : element);
        if (variant == "changed") files[PinsFile] = Pin("App/App.csproj", "/Project[1]/ItemGroup[1]", element);
        await using var repo = await AuditRepo.CreateAsync(files);
        var run = await repo.RunToolWithEnvironmentAsync(new() { ["UsePublishedPackages"] = null });
        AssertEnvironmentVerdict(run, 1, variant == "changed" ? "(changed pin)" : "(missing pin)", "App/App.csproj");
        repo.AssertUntouched();
    }

    // Literal roster intentionally independent from production: additions need matched controls.
    private static readonly string[] ReviewedKnobs =
    [
        "UsePublishedPackages", "AntiphonMessagingVersion", "EnablePublicApiAnalyzers", "AntiphonCleanFileMaxBytes",
        "PkgMicrosoft_Windows_Console_ConPTY", "DefaultItemExcludes", "SourceRevisionId", "WarningsAsErrors",
    ];

    [Test]
    [Arguments("MSBuildThisFileDirectory")]
    [Arguments("MSBuildProjectDirectory")]
    [Arguments("OS")]
    [Arguments("UsePublishedPackages")]
    [Arguments("AntiphonMessagingVersion")]
    [Arguments("EnablePublicApiAnalyzers")]
    [Arguments("AntiphonCleanFileMaxBytes")]
    [Arguments("PkgMicrosoft_Windows_Console_ConPTY")]
    [Arguments("DefaultItemExcludes")]
    [Arguments("SourceRevisionId")]
    [Arguments("WarningsAsErrors")]
    [Timeout(60_000)]
    public async Task C1540_AdmittedNamesApprove(string name)
    {
        var element = $"<Value>$({name})</Value>";
        var files = EnvironmentFiles($"<PropertyGroup>{element}</PropertyGroup>");
        var knob = ReviewedKnobs.Contains(name);
        if (knob) files[PinsFile] = Pin("App/App.csproj", "/Project[1]/PropertyGroup[3]/Value[1]", element);
        await using var repo = await AuditRepo.CreateAsync(files);
        var run = await repo.RunToolWithEnvironmentAsync(new() { [name] = "../docs/a.md" });
        AssertEnvironmentVerdict(run, 0, "approved", "");
        repo.AssertUntouched();
        if (knob)
        {
            files.Remove(PinsFile);
            await using var unpinned = await AuditRepo.CreateAsync(files);
            var refused = await unpinned.RunToolWithEnvironmentAsync(new() { [name] = "../docs/a.md" });
            AssertEnvironmentVerdict(refused, 1, "(missing pin)", "App/App.csproj");
            unpinned.AssertUntouched();
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1540_AdmittedVerdictIgnoresEnvironment()
    {
        var names = ReviewedKnobs.Concat(["MSBuildThisFileDirectory", "MSBuildProjectDirectory", "OS"]).ToArray();
        var elements = names.Select((name, i) => $"<Value{i}>$({name})</Value{i}>").ToArray();
        var files = EnvironmentFiles("<PropertyGroup>" + string.Concat(elements) + "</PropertyGroup>");
        files[PinsFile] = string.Concat(ReviewedKnobs.Select((_, i) => Pin("App/App.csproj",
            $"/Project[1]/PropertyGroup[3]/Value{i}[1]", elements[i])));
        await using var repo = await AuditRepo.CreateAsync(files);
        foreach (var value in new string?[] { null, "", "../docs/a.md" })
        {
            var run = await repo.RunToolWithEnvironmentAsync(ReviewedKnobs.ToDictionary(name => name, _ => value));
            AssertEnvironmentVerdict(run, 0, "approved", "");
        }
        repo.AssertUntouched();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1540_ExpressionSyntaxFailsClosed()
    {
        var cases = new (string Body, string Reason)[]
        {
            ("<PropertyGroup><Value>$( PATH )</Value></PropertyGroup>", "environment-property"),
            ("<PropertyGroup><Value>$(path)</Value></PropertyGroup>", "environment-property"),
            ("<PropertyGroup><Value>%24(PATH)</Value></PropertyGroup>", "environment-property"),
            ("<PropertyGroup><Value>$(UsePublishedPackages )</Value></PropertyGroup>", "(missing pin)"),
            ("<PropertyGroup><Value>%24(usepublishedpackages)</Value></PropertyGroup>", "(missing pin)"),
            ("<PropertyGroup><Value>%( ModifiedTime )</Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value>%(Stamp.ModifiedTime)</Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value>%25(ModifiedTime)</Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value>%(ModifiedTime</Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value>@(Stamp->Metadata('$(MSBuildThisFileDirectory)'))</Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value>@(Stamp->WithMetadataValue('CreatedTime','x'))</Value></PropertyGroup>", "timestamp-metadata"),
            ("<ItemDefinitionGroup><Probe><Value>%(AccessedTime)</Value></Probe></ItemDefinitionGroup>", "timestamp-metadata"),
            ("<PropertyGroup Condition=\"'a'=='b'\"><Value>%(modifiedtime)</Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value><![CDATA[%(AccessedTime)]]></Value></PropertyGroup>", "timestamp-metadata"),
            ("<PropertyGroup><Value>%(Modified$(MSBuildThisFileDirectory)Time)</Value></PropertyGroup>", "timestamp-metadata"),
        };
        foreach (var (body, reason) in cases)
        {
            await using var repo = await AuditRepo.CreateAsync(EnvironmentFiles(body));
            var run = await repo.RunToolAsync();
            AssertEnvironmentVerdict(run, 1, reason, "App/App.csproj");
            repo.AssertUntouched();
        }
        var imported = EnvironmentFiles("""<Import Project="../build/shared.props" />""");
        imported["build/shared.props"] = "<Project><PropertyGroup><Value>%(CreatedTime)</Value></PropertyGroup></Project>";
        await using (var repo = await AuditRepo.CreateAsync(imported))
        {
            AssertEnvironmentVerdict(await repo.RunToolAsync(), 1, "timestamp-metadata", "build/shared.props");
            repo.AssertUntouched();
        }
        const string target = """<Target Name="Pinned"><PropertyGroup><Value>%(ModifiedTime)</Value></PropertyGroup></Target>""";
        var controls = EnvironmentFiles("<ItemGroup><Stamp Include=\"Program.cs\"><Value>%(Filename)</Value></Stamp></ItemGroup>" + target);
        controls[PinsFile] = Pin("App/App.csproj", "Pinned", target);
        await using var safe = await AuditRepo.CreateAsync(controls);
        AssertEnvironmentVerdict(await safe.RunToolAsync(), 0, "approved", "");
        safe.AssertUntouched();
    }
}
