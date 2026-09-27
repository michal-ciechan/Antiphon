using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class Card0452LandingCases
{
    public static async Task AssertSeededRemoteSourceAsync(LandingSafetyHarness h)
    {
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", "--verify",
            h.Fixture.SourceRef + "^{commit}")).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    public static async Task AssertRemoteSourceAndPushSafetyAsync(LandingSafetyHarness h)
    {
        foreach (var command in h.Fixture.Git.Trace.Where(a => a.Length > 0 && a[0] == "push"))
        {
            command.ShouldNotContain("--mirror");
            command.ShouldNotContain("--force");
            command.ShouldNotContain("-f");
            command.ShouldNotContain("--delete");
            command.ShouldNotContain(a => a.StartsWith("--force-with-lease", StringComparison.Ordinal));
            command.ShouldNotContain("--force-if-includes");
            command.ShouldNotContain(a => a.StartsWith("+", StringComparison.Ordinal));
            command.ShouldNotContain(":" + h.Fixture.SourceRef);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    public static async Task<string> InstallRejectingTargetHookAsync(LandingSafetyHarness h)
    {
        var hooks = Path.Combine(h.Fixture.Remote, "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-receive");
        var marker = Path.Combine(hooks, "rejected.marker");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\nwhile read old new ref; do\n  if [ \"$ref\" = 'refs/heads/master' ]; then printf fired > hooks/rejected.marker; exit 1; fi\ndone\nexit 0\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await h.Fixture.RequiredAsync(h.Fixture.Remote, "config", "core.hooksPath", hooks);
        h.Fixture.Git.HooksPathOverride = hooks;
        return marker;
    }
}
