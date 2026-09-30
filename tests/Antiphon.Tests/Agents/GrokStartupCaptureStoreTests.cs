using Antiphon.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class GrokStartupCaptureStoreTests
{
    [Test]
    public void Content_is_bounded_and_sign_in_material_is_suppressed()
    {
        var sentinel = "SECRET-C778-SIGNIN";
        var frame = new GrokStartupSnapshot(new string('s', 8193) + "\u001b\n" + sentinel,
            sentinel + new string('r', 8193) + "\u0000", 17, DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var output = GrokStartupCaptureStore.Format(Guid.NewGuid(), GrokStartupReason.Deadline,
            GrokStartupReason.Unknown, frame, TimeSpan.FromSeconds(60), 0, true, false, now);
        output.ShouldContain("screenSourceUnits:");
        output.ShouldContain("rawSourceUnits:");
        output.ShouldContain("truncated: True");
        output.ShouldNotContain("\u001b");
        output.ShouldNotContain("\u0000");
        output.ShouldNotContain(sentinel);
        System.Text.Encoding.UTF8.GetByteCount(output).ShouldBeLessThanOrEqualTo(
            GrokStartupCaptureStore.FileLimit);
        var suppressed = GrokStartupCaptureStore.Format(Guid.NewGuid(), GrokStartupReason.SignIn,
            GrokStartupReason.SignIn, frame, TimeSpan.Zero, 0, true, true, now);
        suppressed.ShouldContain("content: suppressed after sign-in");
        suppressed.ShouldNotContain(sentinel);
        suppressed.ShouldNotContain("--- rendered screen");
        var absent = GrokStartupCaptureStore.Format(Guid.NewGuid(), GrokStartupReason.Deadline,
            GrokStartupReason.Unknown, null, TimeSpan.Zero, 0, false, false, now);
        absent.ShouldContain("frame: none");
    }

    [Test]
    public void Retention_removes_only_owned_captures()
    {
        var root = Path.Combine(Path.GetTempPath(), "c778-v14-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var unrelated = Path.Combine(root, "unrelated.txt");
        File.WriteAllText(unrelated, "keep");
        try
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
                GrokStartupCaptureStore.Write(root, 0, Guid.NewGuid(), GrokStartupReason.Deadline,
                    GrokStartupReason.Unknown, null, TimeSpan.Zero, 0, false, false,
                    now.AddSeconds(i));
            Directory.GetFiles(root, "grok-startup-*.txt").Length.ShouldBe(1);
            for (var i = 0; i < 101; i++)
                GrokStartupCaptureStore.Write(root, 101, Guid.NewGuid(), GrokStartupReason.Deadline,
                    GrokStartupReason.Unknown, null, TimeSpan.Zero, 0, false, false,
                    now.AddMinutes(1).AddSeconds(i));
            Directory.GetFiles(root, "grok-startup-*.txt").Length.ShouldBe(100);
            File.Exists(unrelated).ShouldBeTrue();
        }
        finally { Directory.Delete(root, true); }
    }
}
