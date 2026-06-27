using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class PtySessionAuditTests
{
    [Test]
    public async Task Create_returns_null_when_auditing_disabled_by_default()
    {
        // Auditing is opt-in (ANTIPHON_PTY_AUDIT=1); not set here, so no audit dir is produced.
        var audit = PtySessionAudit.Create("app", ["--x"], cwd: null, env: null, getSnapshot: () => "");
        audit.ShouldBeNull();
    }

    [Test]
    public void PruneOldAudits_deletes_stale_dirs_and_keeps_recent_ones()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-pty-audits-test-" + Guid.NewGuid().ToString("N")[..8]);
        var old = Path.Combine(root, "old");
        var fresh = Path.Combine(root, "fresh");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(fresh);
        File.WriteAllText(Path.Combine(old, "timeline.txt"), "stale");
        File.WriteAllText(Path.Combine(fresh, "timeline.txt"), "recent");
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-5));
        Directory.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

        try
        {
            PtySessionAudit.PruneOldAudits(root, TimeSpan.FromDays(2));

            Directory.Exists(old).ShouldBeFalse("audit dir older than the retention window should be pruned");
            Directory.Exists(fresh).ShouldBeTrue("a recent audit dir should be kept");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public void PruneOldAudits_is_safe_when_root_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "antiphon-pty-audits-missing-" + Guid.NewGuid().ToString("N")[..8]);
        Should.NotThrow(() => PtySessionAudit.PruneOldAudits(missing, TimeSpan.FromDays(2)));
    }
}
