using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0677: a Code round on any host (the server2 runner image included) gets dotnet-ef from the
// repo-local tool manifest via `dotnet tool restore`, never from a machine-global install.
[Category("Unit")]
public sealed class DotnetToolManifestContractTests
{
    [Test]
    public void Manifest_is_root_and_pins_dotnet_ef_exactly()
    {
        using var manifest = JsonDocument.Parse(DockerStackDocuments.Read(".config/dotnet-tools.json"));
        manifest.RootElement.GetProperty("isRoot").GetBoolean().ShouldBeTrue("manifest must be the root manifest");

        var version = DotnetEfVersion(manifest);
        Regex.IsMatch(version, @"^\d+\.\d+\.\d+$").ShouldBeTrue($"dotnet-ef pin '{version}' is not an exact x.y.z version");
    }

    [Test]
    public void Dotnet_ef_major_matches_server_ef_core_design_reference()
    {
        using var manifest = JsonDocument.Parse(DockerStackDocuments.Read(".config/dotnet-tools.json"));
        var toolMajor = DotnetEfVersion(manifest).Split('.')[0];

        var design = XDocument.Parse(DockerStackDocuments.Read("server/Antiphon.Server.csproj"))
            .Descendants("PackageReference")
            .Single(e => (string?)e.Attribute("Include") == "Microsoft.EntityFrameworkCore.Design");
        var designMajor = ((string?)design.Attribute("Version") ?? "").Split('.')[0];

        toolMajor.ShouldBe(designMajor, "dotnet-ef major must match the server's Microsoft.EntityFrameworkCore.Design major");
    }

    private static string DotnetEfVersion(JsonDocument manifest) =>
        manifest.RootElement.GetProperty("tools").GetProperty("dotnet-ef").GetProperty("version").GetString() ?? "";
}
