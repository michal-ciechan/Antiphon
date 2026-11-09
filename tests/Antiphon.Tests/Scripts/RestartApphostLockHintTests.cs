using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1533. The restart-lock refusal and the no-dashboard timeout must not
/// tell an operator to delete a fresh lock. Holder death and a timed-out
/// launch leave that stamp in place; early recovery is the two-fire proof.
/// </summary>
[Category("Unit")]
public sealed class RestartApphostLockHintTests
{
    private const string RefusalHint =
        "    Write-Host \"  Nothing was killed. A dead holder is not a reason to delete $restartLock.\" -ForegroundColor DarkGray";

    private const string RefusalInspect =
        "    Write-Host \"  Inspect both locks, launcher output, owned processes, listeners and health.\" -ForegroundColor DarkGray";

    private const string RefusalProof =
        "    Write-Host \"  Early recovery needs the same lock identity on two watchdog fires within five minutes,\" -ForegroundColor DarkGray";

    private const string RefusalConditions =
        "    Write-Host \"  holder state Dead, launcher activity Absent, connection-refused health, and no launch lock.\" -ForegroundColor DarkGray";

    private const string RefusalWait =
        "    Write-Host \"  Otherwise wait for LockMaxAgeMinutes. Unknown, live, slow, or replacement evidence does not qualify.\" -ForegroundColor DarkGray";

    private const string TimeoutKeep =
        "        Write-Host \"  Leaving $restartLock for LockMaxAgeMinutes. Do not delete it to force a retry.\" -ForegroundColor DarkGray";

    private const string TimeoutPrefer =
        "        Write-Host \"  Prefer inspection, guarded dead-launch admission, or ordinary expiry.\" -ForegroundColor DarkGray";

    [Test]
    public void Refusal_hint_refuses_holder_death_as_delete_permission()
    {
        var text = ReadScript();
        AssertAscii(text);
        text.ShouldNotContain("If the holder is genuinely gone, delete");
        text.ShouldNotContain("genuinely gone");
        text.ShouldContain(RefusalHint);
        text.ShouldContain(RefusalInspect);
        text.ShouldContain(RefusalProof);
        text.ShouldContain(RefusalConditions);
        text.ShouldContain(RefusalWait);
        NextStatement(text, RefusalWait).ShouldBe("exit 3");
    }

    [Test]
    public void Timeout_hint_refuses_delete_to_force_a_retry()
    {
        var text = ReadScript();
        AssertAscii(text);
        text.ShouldNotContain("; delete it to force a retry.");
        CountOf(text, "force a retry").ShouldBe(1);
        text.ShouldContain(TimeoutKeep);
        text.ShouldContain(TimeoutPrefer);
        NextStatement(text, TimeoutPrefer).ShouldBe("exit 1");
    }

    private static string ReadScript()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "restart-apphost.ps1");
        return File.ReadAllText(path);
    }

    private static void AssertAscii(string text)
    {
        var high = text.Count(ch => ch > 127);
        high.ShouldBe(0, "restart-apphost.ps1 stays ASCII for Windows PowerShell 5.1");
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }

    private static string NextStatement(string text, string anchor)
    {
        var at = text.IndexOf(anchor, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, anchor);
        var second = text.IndexOf(anchor, at + anchor.Length, StringComparison.Ordinal);
        second.ShouldBe(-1, "the hint line is the only copy");
        foreach (var raw in text[(at + anchor.Length)..].Split('\n'))
        {
            var line = raw.Trim('\r').Trim();
            if (line.Length > 0)
                return line;
        }
        return "";
    }
}
