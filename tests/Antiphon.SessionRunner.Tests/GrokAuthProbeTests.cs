using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0647. The runner Grok probe is presence of <c>GROK_HOME/auth.json</c>. It never opens the
/// file, so a secret written into it cannot appear in the DTO or the log.
/// </summary>
[Category("Unit")]
public class GrokAuthProbeTests
{
    private const string Sentinel = "desktop-secret-sentinel-not-for-logs";

    [Test]
    public async Task Missing_auth_json_is_signed_out_and_a_present_file_is_signed_in_without_its_contents()
    {
        var root = Path.Combine(Path.GetTempPath(), "grok-auth-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var log = new List<string>();
        var probe = new GrokAuthProbe(
            new PhoneHomeSettings { GrokHome = root.Replace('\\', '/') },
            new ListLogger<GrokAuthProbe>(log));
        try
        {
            var missing = await probe.ProbeAsync("grok", CancellationToken.None);
            missing.Provider.ShouldBe("grok");
            missing.LoggedIn.ShouldBe(false);
            missing.AuthMethod.ShouldBeNull();
            missing.Error.ShouldBeNull();

            var authPath = Path.Combine(root, "auth.json");
            await File.WriteAllTextAsync(authPath, "{\"token\":\"" + Sentinel + "\"}");
            var present = await probe.ProbeAsync("grok", CancellationToken.None);
            present.LoggedIn.ShouldBe(true);
            present.AuthMethod.ShouldBe("auth_file");
            present.Error.ShouldBeNull();
            JsonSerializer(present).ShouldNotContain(Sentinel);
            string.Join('\n', log).ShouldNotContain(Sentinel);
            (await File.ReadAllTextAsync(authPath)).ShouldContain(Sentinel);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task An_unreadable_store_cannot_tell()
    {
        var probe = new GrokAuthProbe(
            new PhoneHomeSettings { GrokHome = "/state/grok" },
            fileExists: _ => throw new IOException("denied"));
        var dto = await probe.ProbeAsync("grok", CancellationToken.None);
        dto.LoggedIn.ShouldBeNull();
        dto.Error.ShouldBe("unreadable");
    }

    [Test]
    public async Task Another_provider_is_refused()
    {
        var probe = new GrokAuthProbe(new PhoneHomeSettings { GrokHome = "/state/grok" }, fileExists: _ => true);
        var ex = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => probe.ProbeAsync("claude", CancellationToken.None));
        ex.StatusCode.ShouldBe(400);
    }

    private static string JsonSerializer(RunnerProviderAuthDto dto) =>
        System.Text.Json.JsonSerializer.Serialize(dto);
}
