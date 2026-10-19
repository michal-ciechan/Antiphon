using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class SeedSmokeTargetingPackContractTests
{
    [Test]
    [Arguments("scripts/c590-remote.sh", "Microsoft.NETCore.App")]
    [Arguments("scripts/c590-remote.sh", "Microsoft.AspNetCore.App")]
    [Arguments("docker/session-runner-grok/verify-codex-image.sh", "Microsoft.NETCore.App")]
    [Arguments("docker/session-runner-grok/verify-codex-image.sh", "Microsoft.AspNetCore.App")]
    public void Net9_smoke_reference_selects_the_image_targeting_pack(string scriptPath, string framework)
    {
        var root = DelegateScriptRunner.RepoRoot;
        var dockerfile = File.ReadAllText(Path.Combine(root, "docker/session-runner-grok/Dockerfile"));
        var pin = Regex.Match(dockerfile, @"(?m)^ARG NET9_PACK_VERSION=(\d+\.\d+\.\d+)\r?$");
        pin.Success.ShouldBeTrue("the image must declare its reviewed net9 pack version");

        var script = File.ReadAllText(Path.Combine(root, scriptPath));
        var projects = Regex.Matches(script, @"<Project Sdk=""Microsoft.NET.Sdk"">.*?</Project>",
                RegexOptions.Singleline)
            .Select(match => XDocument.Parse(match.Value))
            .Where(project => project.Descendants("TargetFramework").Any(value => value.Value == "net9.0"))
            .ToArray();
        projects.Length.ShouldBe(1, $"{scriptPath} must contain the net9 smoke project");

        var reference = projects[0].Descendants("FrameworkReference")
            .LastOrDefault(item => (string?)item.Attribute("Update") == framework
                || (string?)item.Attribute("Include") == framework);
        ((string?)reference?.Attribute("TargetingPackVersion")).ShouldBe(pin.Groups[1].Value,
            $"{scriptPath}: {framework} must explicitly select the image pack, independent of SDK defaults");
    }
}
