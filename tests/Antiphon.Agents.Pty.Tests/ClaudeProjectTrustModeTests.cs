using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

public partial class ClaudeProjectTrustTests
{
    [Test]
    public void Seed_preserves_the_config_file_mode()
    {
        using var cfg = new TempConfig("{\"projects\":{}}");
        if (OperatingSystem.IsWindows())
            throw new SkipTestException("Unix file mode is not available on this host");

        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(cfg.Path, expected);

        var directory = Directory.CreateTempSubdirectory("antiphon-trust-mode").FullName;
        try
        {
            var result = ClaudeProjectTrust.Seed(directory, cfg.Path);
            result.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            File.GetUnixFileMode(cfg.Path).ShouldBe(expected);
            ClaudeProjectTrust.IsTrusted(directory, cfg.Path).ShouldBeTrue();
        }
        finally
        {
            ClaudeProjectTrust.Remove(directory, cfg.Path);
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
