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
        var firstWindow = new GrokStartupWindow(GrokStartupReason.Unknown, frame, TimeSpan.FromSeconds(60), 0, true);
        var output = GrokStartupCaptureStore.Format(Guid.NewGuid(),
            new GrokStartupFailure(GrokStartupReason.Deadline, firstWindow, false), now);
        output.ShouldContain("screenSourceUnits:");
        output.ShouldContain("rawSourceUnits:");
        output.ShouldContain("truncated: True");
        output.ShouldNotContain("\u001b");
        output.ShouldNotContain("\u0000");
        output.ShouldNotContain(sentinel);
        System.Text.Encoding.UTF8.GetByteCount(output).ShouldBeLessThanOrEqualTo(
            GrokStartupCaptureStore.FileLimit);
        var signIn = new GrokStartupWindow(GrokStartupReason.SignIn, frame with { RawOutput = sentinel },
            TimeSpan.FromSeconds(10), 0, true);
        var suppressed = GrokStartupCaptureStore.Format(Guid.NewGuid(),
            new GrokStartupFailure(GrokStartupReason.SignIn, signIn, true, FirstWindow: firstWindow), now);
        suppressed.ShouldContain("content: suppressed after sign-in");
        suppressed.ShouldContain("firstWindowOutcome: Deadline");
        suppressed.ShouldContain("secondWindowOutcome: SignIn");
        suppressed.ShouldNotContain(sentinel);
        suppressed.ShouldNotContain("--- rendered screen");
        var absent = GrokStartupCaptureStore.Format(Guid.NewGuid(),
            new GrokStartupFailure(GrokStartupReason.Deadline,
                new GrokStartupWindow(GrokStartupReason.Unknown, null, TimeSpan.Zero, 0, false), false), now);
        absent.ShouldContain("frame: none");
    }

    [Test]
    public void C2047_Expiry_keeps_recent_captures_and_ignores_unowned_files()
    {
        var now = DateTimeOffset.UtcNow;
        var root = NewRoot();
        try
        {
            var ages = new[] { 240, 216, 192, 167, 144, 120, 96, 72, 48, 24, 2, 1 };
            var seeded = ages.Select(hours => Seed(root, now.AddHours(-hours))).ToArray();
            var unrelated = Path.Combine(root, "grok-startup-not-a-capture.txt");
            File.WriteAllText(unrelated, "keep");

            var written = Write(root, now, 200);

            Directory.GetFiles(root).ShouldBe(seeded.Skip(3).Append(unrelated).Append(written),
                ignoreOrder: true);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void C2047_Count_cap_prunes_oldest_but_preserves_the_first_day()
    {
        var now = DateTimeOffset.UtcNow;
        var root = NewRoot();
        try
        {
            var older = Enumerable.Range(0, 200)
                .Select(i => Seed(root, now.AddHours(-48).AddSeconds(i))).ToArray();
            var recent = Seed(root, now.AddHours(-23));

            var written = Write(root, now, 200);

            Directory.GetFiles(root).ShouldBe(older.Skip(2).Append(recent).Append(written),
                ignoreOrder: true);

            var latest = Write(root, now.AddSeconds(1), 1);

            Directory.GetFiles(root).ShouldBe([recent, written, latest], ignoreOrder: true);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void C2047_Byte_cap_evicts_oldest_even_within_count_grace()
    {
        var now = DateTimeOffset.UtcNow;
        var root = NewRoot();
        try
        {
            // 400 maximum-sized captures already fill the 50 MiB quota.
            var seeded = Enumerable.Range(0, 400).Select(i =>
                Seed(root, now.AddHours(-1).AddSeconds(i), 128 * 1024)).ToArray();

            var written = Write(root, now, 200);

            var files = new DirectoryInfo(root).GetFiles();
            files.Sum(file => file.Length).ShouldBeLessThanOrEqualTo(50L * 1024 * 1024);
            files.Select(file => file.FullName).ShouldBe(seeded.Skip(1).Append(written),
                ignoreOrder: true);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot() =>
        Directory.CreateTempSubdirectory("c2047-captures-").FullName;

    private static string Seed(string root, DateTimeOffset when, int bytes = 1)
    {
        var path = Path.Combine(root,
            $"grok-startup-{when.UtcDateTime:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}-{Guid.NewGuid():N}.txt");
        using var stream = File.Create(path);
        stream.SetLength(bytes);
        return path;
    }

    private static string Write(string root, DateTimeOffset now, int keep) =>
        GrokStartupCaptureStore.Write(root, keep, Guid.NewGuid(),
            new GrokStartupFailure(GrokStartupReason.Deadline,
                new GrokStartupWindow(GrokStartupReason.Unknown, null, TimeSpan.Zero, 0, false), false), now);
}
