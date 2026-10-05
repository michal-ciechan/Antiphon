using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class PushCredentialPolicyFileTests
{
    [Test]
    public void Renders_the_primary_identity_then_each_prefix()
    {
        var settings = new PhoneHomeSettings();
        PushCredentialPolicyFile.Render(settings.RepositoryPolicy()).ShouldBe(
            "https://github.com/michal-ciechan/antiphon.git\nhttps://github.com/michal-ciechan/\n");
        settings.AllowedCloneSources = ["https://github.com/other-owner/", "https://github.com/CaseSensitive/"];
        PushCredentialPolicyFile.Render(settings.RepositoryPolicy()).ShouldBe(
            "https://github.com/michal-ciechan/antiphon.git\nhttps://github.com/other-owner/\nhttps://github.com/CaseSensitive/\n");
    }

    [Test]
    public async Task Write_creates_the_parent_and_replaces_an_existing_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "c0817-policy-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "push-allow-list");
        try
        {
            var settings = new PhoneHomeSettings();
            PushCredentialPolicyFile.Write(path, settings.RepositoryPolicy());
            File.ReadAllText(path).ShouldBe("https://github.com/michal-ciechan/antiphon.git\nhttps://github.com/michal-ciechan/\n");
            File.WriteAllText(path, "stale\n");
            var service = new PushCredentialPolicyService(Options.Create(settings));
            await service.StartAsync(CancellationToken.None); // Unset is a no-op.
            File.ReadAllText(path).ShouldBe("stale\n");
            settings.PushCredentialPolicyPath = path;
            await service.StartAsync(CancellationToken.None); // Disabled is a no-op.
            File.ReadAllText(path).ShouldBe("stale\n");
            settings.Enabled = true;
            await service.StartAsync(CancellationToken.None);
            File.ReadAllText(path).ShouldBe("https://github.com/michal-ciechan/antiphon.git\nhttps://github.com/michal-ciechan/\n");
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).ShouldBe(new[] { path });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Settings_refuse_a_relative_policy_path()
    {
        var settings = new PhoneHomeSettings { Enabled = true, ServerOrigin = "https://example.test" };
        Should.NotThrow(settings.Validate);
        settings.PushCredentialPolicyPath = "run/x";
        Should.Throw<InvalidOperationException>(settings.Validate).Message.ShouldContain("PhoneHome:PushCredentialPolicyPath");
        settings.PushCredentialPolicyPath = "/run/antiphon/push-allow-list";
        Should.NotThrow(settings.Validate);
    }
}
