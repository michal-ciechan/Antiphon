using System.Xml;
using Antiphon.Checkpoints;
using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class XmlInputSecurityTests : CheckpointTestBase
{
    [Test]
    public void diagnostic_reader_refuses_declarations_and_retains_literal_controls()
    {
        var path = Path.Combine(TempDir(), "input.trx");
        foreach (var item in C1217ConditionalSkipFixture.DtdCases(C1217ConditionalSkipFixture.UnavailableTrx))
        {
            File.WriteAllText(path, item.Xml);
            var parsed = TrxReport.Parse(path);
            parsed.ExitCode.ShouldBe(item.Expected, "R3-1:diagnostic:" + item.Key);
            parsed.Ok.ShouldBe(item.Expected == 0, "R3-1:diagnostic-verdict:" + item.Key);
            File.ReadAllText(path).ShouldBe(item.Xml);
        }
    }

    private static readonly string[] Declarations =
    [
        "<!DOCTYPE Project>",
        "<!DOCTYPE Project [<!ENTITY reason 'text'>]>",
        "<!DOCTYPE Project SYSTEM 'file:///nonexistent-c1217.xml'>",
        "<!DOCTYPE Project [<!ENTITY reason SYSTEM 'file:///nonexistent-c1217.xml'>]>",
    ];

    [Test]
    public void project_readers_refuse_declarations_after_an_accepted_control()
    {
        var root = TempDir();
        var path = Path.Combine(root, "Sample.csproj");
        const string project = "<Project><ItemGroup><PackageReference Include=\"TUnit\" /></ItemGroup></Project>";
        foreach (var prefix in new[] { "", "<!-- <!DOCTYPE Project> -->" })
        {
            File.WriteAllText(path, prefix + project);
            RepeatRequest.CanonicalProject(root, path).ShouldBe(path, "R3-1:repeat-project-control");
            ProjectWriteRoots.TryCollect(root, path, "bin-security/", out _).ShouldBeNull("R3-1:closure-control");
        }
        foreach (var declaration in Declarations)
        {
            File.WriteAllText(path, declaration + project);
            Should.Throw<XmlException>(() => RepeatRequest.CanonicalProject(root, path), "R3-1:repeat-project:" + declaration);
            ProjectWriteRoots.TryCollect(root, path, "bin-security/", out _).ShouldBe(ProjectWriteRoots.ClosureRefusal, "R3-1:closure:" + declaration);
        }
    }

    [Test]
    public void coverage_project_reader_refuses_declarations_after_an_accepted_control()
    {
        var root = TempDir();
        var world = PlanCoverageFixture.WriteWorld(root);
        var path = Path.Combine(root, "tests", "Sample", "Sample.csproj");
        var command = new CoverageCommand();
        foreach (var prefix in new[] { "", "<!-- <!DOCTYPE Project> -->" })
        {
            File.WriteAllText(path, prefix + "<Project />");
            command.Run(root, world.Plan, output: new StringWriter()).ShouldBe(0, "R3-1:coverage-control");
        }
        foreach (var declaration in Declarations)
        {
            File.WriteAllText(path, declaration + "<Project />");
            var output = new StringWriter();
            command.Run(root, world.Plan, output: output).ShouldBe(2, "R3-1:coverage-project:" + declaration + output);
        }
    }

    [Test]
    public void diagnostic_reader_bounds_the_document_without_requiring_strict_structure()
    {
        var path = Path.Combine(TempDir(), "input.trx");
        File.WriteAllText(path, "<TestRun><Note>ordinary diagnostic</Note></TestRun>");
        TrxReport.Parse(path).Ok.ShouldBeTrue("R3-1:diagnostic-structure-control");
        File.WriteAllText(path, "<TestRun><Note>" + new string('x', 16 * 1024 * 1024) + "</Note></TestRun>");
        var parsed = TrxReport.Parse(path);
        parsed.Ok.ShouldBeFalse("R3-1:diagnostic-document-limit");
        parsed.ExitCode.ShouldBe(2);
    }
}
