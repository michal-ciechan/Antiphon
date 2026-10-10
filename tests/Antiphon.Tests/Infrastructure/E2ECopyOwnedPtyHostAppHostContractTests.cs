using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1177: an outer Linux restore with UseAppHost=false omits the net9 apphost pack.
// The E2E target must restore Antiphon.PtyHost with UseAppHost=true before its inner build.
[Category("Unit")]
public sealed class E2ECopyOwnedPtyHostAppHostContractTests
{
    [Test]
    public void e2e_copy_owned_ptyhost_restores_apphost_pack_before_inner_build()
    {
        var doc = XDocument.Parse(DockerStackDocuments.Read("tests/Antiphon.E2E/Antiphon.E2E.csproj"));
        var target = doc.Descendants("Target")
            .Single(element => (string?)element.Attribute("Name") == "CopyOwnedPtyHostAppHost");
        ((string?)target.Attribute("AfterTargets")).ShouldBe("Build");
        ((string?)target.Attribute("Condition")).ShouldContain("'$(OS)' != 'Windows_NT'");

        var calls = target.Elements("MSBuild")
            .Where(element => ((string?)element.Attribute("Projects") ?? "")
                .EndsWith("src/Antiphon.PtyHost/Antiphon.PtyHost.csproj", StringComparison.Ordinal)
                || ((string?)element.Attribute("Projects") ?? "")
                .EndsWith("src\\Antiphon.PtyHost\\Antiphon.PtyHost.csproj", StringComparison.Ordinal))
            .ToList();
        var restoreAt = calls.FindIndex(element =>
            (string?)element.Attribute("Targets") == "Restore" &&
            Properties(element).Contains("UseAppHost=true", StringComparison.Ordinal) &&
            !Properties(element).Contains("UseAppHost=false", StringComparison.Ordinal));
        var buildAt = calls.FindIndex(element =>
            (string?)element.Attribute("Targets") == "Build" &&
            Properties(element).Contains("UseAppHost=true", StringComparison.Ordinal));

        restoreAt.ShouldBeGreaterThanOrEqualTo(0,
            "CARD-1177: CopyOwnedPtyHostAppHost must restore Antiphon.PtyHost with UseAppHost=true");
        buildAt.ShouldBeGreaterThan(restoreAt,
            "the UseAppHost=true restore must precede the inner build");
    }

    private static string Properties(XElement element) => (string?)element.Attribute("Properties") ?? "";
}
