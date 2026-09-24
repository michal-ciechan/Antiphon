using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0665 V-9. Patterns are worktree-relative '/' globs; anything that could reach outside the
// tree, or that depends on the host's separator, is a startup failure naming its key.
[Category("Unit")]
public sealed class WorktreeCleanupSettingsValidatorTests
{
    [Test]
    public void Valid_defaults_pass()
    {
        new WorktreeCleanupSettingsValidator().Validate(null, new WorktreeCleanupSettings()).Succeeded.ShouldBeTrue();
        new WorktreeCleanupSettingsValidator().Validate(null, new WorktreeCleanupSettings
        {
            DisposableIgnored = [.. WorktreeCleanupSettings.DefaultDisposableIgnored],
            RetainedIgnored = [.. WorktreeCleanupSettings.DefaultRetainedIgnored],
        }).Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Rejects_escaping_rooted_backslash_or_empty_patterns()
    {
        foreach (var bad in new[] { "../x", "a/../x", "/abs/**", "C:\\x", "C:/x/**", "bin\\**", "", "  " })
        {
            var disposable = new WorktreeCleanupSettingsValidator().Validate(null, new WorktreeCleanupSettings
            { DisposableIgnored = ["**/obj/**", bad] });
            disposable.Failed.ShouldBeTrue("disposable pattern must be refused: '" + bad + "'");
            disposable.FailureMessage.ShouldContain("WorktreeCleanup:DisposableIgnored");

            var retained = new WorktreeCleanupSettingsValidator().Validate(null, new WorktreeCleanupSettings
            { RetainedIgnored = [bad] });
            retained.Failed.ShouldBeTrue("retained pattern must be refused: '" + bad + "'");
            retained.FailureMessage.ShouldContain("WorktreeCleanup:RetainedIgnored");
        }
    }

    [Test]
    public void Rejects_non_positive_caps()
    {
        var bytes = new WorktreeCleanupSettingsValidator().Validate(null, new WorktreeCleanupSettings { MaxRetainedEvidenceBytes = 0 });
        bytes.Failed.ShouldBeTrue();
        bytes.FailureMessage.ShouldContain("WorktreeCleanup:MaxRetainedEvidenceBytes");
        var files = new WorktreeCleanupSettingsValidator().Validate(null, new WorktreeCleanupSettings { MaxRetainedEvidenceFiles = -1 });
        files.Failed.ShouldBeTrue();
        files.FailureMessage.ShouldContain("WorktreeCleanup:MaxRetainedEvidenceFiles");
    }
}
