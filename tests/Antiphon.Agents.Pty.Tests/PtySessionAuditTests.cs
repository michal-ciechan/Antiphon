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
    public void PruneOldAudits_enforces_max_dir_count_keeping_newest()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-pty-audits-count-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            // 6 recent dirs (within the age window), staggered mtimes so "newest" is well-defined.
            var now = DateTime.UtcNow;
            for (var i = 0; i < 6; i++)
            {
                var d = Path.Combine(root, $"s{i}");
                Directory.CreateDirectory(d);
                Directory.SetLastWriteTimeUtc(d, now.AddMinutes(-i)); // s0 newest, s5 oldest
            }

            // Keep only the newest 3 even though all are within the age window.
            PtySessionAudit.PruneOldAudits(root, TimeSpan.FromDays(7), maxDirs: 3);

            Directory.GetDirectories(root).Length.ShouldBe(3);
            Directory.Exists(Path.Combine(root, "s0")).ShouldBeTrue();
            Directory.Exists(Path.Combine(root, "s1")).ShouldBeTrue();
            Directory.Exists(Path.Combine(root, "s2")).ShouldBeTrue();
            Directory.Exists(Path.Combine(root, "s5")).ShouldBeFalse();
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
