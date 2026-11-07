using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1756: a bound tailer must not open the transcript's identity handle on every 300 ms poll.
/// Counts are the witness. Wall time between polls is the backoff witness. Replacing the inode
/// must still surface <see cref="TranscriptTailer.BoundIdentityDrift"/> inside
/// <see cref="TranscriptTailer.IdentityStaleness"/>, including a replacement that keeps length
/// and mtime and is visible only to the 4 s identity recheck.
/// </summary>
public class TranscriptTailerBoundPollTests
{
    [Test]
    public async Task Unchanged_bound_file_opens_identity_handle_only_on_the_first_poll()
    {
        await using var world = World.Start(Line("u1", "hello"));
        await world.UntilPoll(2);
        await world.UntilPoll(4);

        // BeforePoll samples the counter before that poll's check. Poll 1's open is visible at
        // poll 2. Polls 2 and 3 of an unchanged file must not open again, so poll 4 still matches.
        world.OpensAt(2).ShouldBeGreaterThan(world.OpensAt(1),
            "the first poll opens the identity handle");
        world.OpensAt(4).ShouldBe(world.OpensAt(2),
            "later polls of an unchanged bound file must not open the identity handle");
    }

    [Test]
    public async Task Replaced_bound_file_is_detected_within_the_identity_bound()
    {
        await using var world = World.Start(Line("u1", "hello"));
        world.OnPoll = n =>
        {
            if (n != 2)
                return;
            world.DriftBeforeReplace = world.Tailer.BoundIdentityDrift;
            ReplaceWithNewInode(world.Path);
            world.ReplacedAt = DateTime.UtcNow;
        };

        await world.UntilPoll(2);
        world.DriftBeforeReplace.ShouldBeFalse("the original inode is still the bound file");
        await world.UntilDrift();
        var detected = DateTime.UtcNow - world.ReplacedAt;
        detected.ShouldBeLessThanOrEqualTo(TranscriptTailer.IdentityStaleness,
            "a new inode at the bound path must be noticed within the documented staleness");
        world.Tailer.BoundIdentityDrift.ShouldBeTrue();
    }

    /// <summary>
    /// PC-4: mutant <c>intervalDue = false</c>. A new inode that keeps length and mtime is
    /// invisible to the stat-change branch. The 4 s recheck must raise
    /// <see cref="TranscriptTailer.BoundIdentityDrift"/> within
    /// <see cref="TranscriptTailer.IdentityStaleness"/> and open the identity handle once.
    /// </summary>
    [Test]
    public async Task Same_stat_replacement_is_detected_by_the_identity_interval()
    {
        var watch = new SameStatWatch();
        await using var world = World.Start(Line("u1", "hello"), ready =>
        {
            ready.OnPoll = _ =>
            {
                if (watch.Armed)
                    return;
                if (watch.OpensAtCap < 0)
                {
                    if (ready.Tailer.CurrentBoundPollDelay == TranscriptTailer.QuietPollCap)
                        watch.NoteCap(ready.Tailer.TerminalObservation.IdentityHandleOpens);
                    return;
                }

                var opens = ready.Tailer.TerminalObservation.IdentityHandleOpens;
                if (opens <= watch.OpensAtCap)
                    return;

                var before = new FileInfo(ready.Path);
                var length = before.Length;
                var mtime = before.LastWriteTimeUtc;
                ReplacePreservingLengthAndMtime(ready.Path, mtime);
                var after = new FileInfo(ready.Path);
                ready.DriftBeforeReplace = ready.Tailer.BoundIdentityDrift;
                ready.ReplacedAt = DateTime.UtcNow;
                watch.Arm(opens, length, mtime, after);
            };
        });

        var setupDeadline = DateTime.UtcNow.AddSeconds(25);
        while (!watch.Armed)
        {
            DateTime.UtcNow.ShouldBeLessThan(setupDeadline,
                "the quiet cap and one interval open did not arrive before replacement");
            await Task.Delay(20);
        }

        watch.LengthMatches.ShouldBeTrue("PC-4: " + watch.PreserveDetail);
        watch.MtimeMatches.ShouldBeTrue("PC-4: " + watch.PreserveDetail);
        world.DriftBeforeReplace.ShouldBeFalse("the interval open still named the original inode");
        await world.UntilDrift();
        (DateTime.UtcNow - world.ReplacedAt).ShouldBeLessThanOrEqualTo(TranscriptTailer.IdentityStaleness,
            "PC-4: a same-stat inode replacement must be noticed within the identity staleness");
        world.Tailer.BoundIdentityDrift.ShouldBeTrue();
        world.Tailer.TerminalObservation.IdentityHandleOpens.ShouldBe(watch.Opens + 1,
            "PC-4: the identity interval opens the handle exactly once for that replacement");
    }

    [Test]
    public async Task Quiet_bound_poll_backs_off_and_growth_restores_the_fast_poll()
    {
        await using var world = World.Start(Line("u1", "hello"));
        world.OnPoll = n =>
        {
            if (n == 4)
                File.AppendAllText(world.Path, Line("u2", "more"));
        };

        await world.UntilPoll(4);
        var quiet = world.StampAt(4) - world.StampAt(2);
        quiet.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(1200),
            "quiet polls must leave the 300 ms cadence; two backed-off gaps exceed 1.2 s");
        quiet.ShouldBeLessThanOrEqualTo(TranscriptTailer.QuietPollCap * 2 + TimeSpan.FromMilliseconds(400),
            "the backed-off cadence stays inside the 2 s cap");

        await world.UntilPoll(5);
        var restored = world.StampAt(5) - world.StampAt(4);
        restored.ShouldBeLessThan(TimeSpan.FromMilliseconds(900),
            "bytes appended to the bound file restore the 300 ms poll");
    }

    private static string Line(string uuid, string body) =>
        "{\"type\":\"user\",\"uuid\":\"" + uuid + "\",\"message\":{\"role\":\"user\",\"content\":\"" + body + "\"}}\n";

    private static void ReplaceWithNewInode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Keep the previous inode allocated so the kernel cannot recycle it into the replacement.
        File.Move(path, path + ".previous-inode");
        File.WriteAllBytes(path, bytes);
    }

    private static void ReplacePreservingLengthAndMtime(string path, DateTime mtimeUtc)
    {
        var bytes = File.ReadAllBytes(path);
        // Keep the previous inode allocated so the kernel cannot recycle it into the replacement.
        File.Move(path, path + ".previous-inode");
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, mtimeUtc);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly string _root;
        private readonly int[] _opens = new int[8];
        private readonly DateTime[] _stamps = new DateTime[8];
        private int _published;

        private World(string root, string path, TranscriptTailer tailer)
        {
            _root = root;
            Path = path;
            Tailer = tailer;
            tailer.TerminalObservation.BeforePoll = _ =>
            {
                var n = Volatile.Read(ref _published) + 1;
                if ((uint)n < (uint)_opens.Length)
                {
                    _opens[n] = tailer.TerminalObservation.IdentityHandleOpens;
                    _stamps[n] = DateTime.UtcNow;
                }

                OnPoll?.Invoke(n);
                Volatile.Write(ref _published, n);
                return Task.CompletedTask;
            };
        }

        public string Path { get; }
        public TranscriptTailer Tailer { get; }
        public Action<int>? OnPoll { get; set; }
        public bool DriftBeforeReplace { get; set; }
        public DateTime ReplacedAt { get; set; }

        public int OpensAt(int poll) => _opens[poll];
        public DateTime StampAt(int poll) => _stamps[poll];

        public static World Start(string body, Action<World>? beforeStart = null)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1756-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, "transcript.jsonl");
            File.WriteAllText(path, body);
            var tailer = new TranscriptTailer(
                Guid.NewGuid(), root, new SessionRunnerEventHub(), NullLogger.Instance,
                knownTranscriptPath: path, forkScanInterval: TimeSpan.FromDays(1));
            var world = new World(root, path, tailer);
            beforeStart?.Invoke(world);
            tailer.Start();
            return world;
        }

        public async Task UntilPoll(int poll)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (Volatile.Read(ref _published) < poll)
            {
                DateTime.UtcNow.ShouldBeLessThan(deadline, $"bound poll {poll} did not start");
                await Task.Delay(20);
            }
        }

        public async Task UntilDrift()
        {
            var deadline = ReplacedAt + TranscriptTailer.IdentityStaleness + TimeSpan.FromSeconds(1);
            while (!Tailer.BoundIdentityDrift)
            {
                DateTime.UtcNow.ShouldBeLessThan(deadline,
                    "replacement was not detected within the documented identity staleness");
                await Task.Delay(20);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Tailer.DisposeAsync();
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class SameStatWatch
    {
        private int _armed;
        private int _opens;

        public int OpensAtCap { get; private set; } = -1;
        public bool Armed => Volatile.Read(ref _armed) != 0;
        public int Opens => Volatile.Read(ref _opens);
        public bool LengthMatches { get; private set; }
        public bool MtimeMatches { get; private set; }
        public string PreserveDetail { get; private set; } = "";

        public void NoteCap(int opens) => OpensAtCap = opens;

        public void Arm(int opens, long length, DateTime mtime, FileInfo after)
        {
            LengthMatches = after.Exists && after.Length == length;
            MtimeMatches = after.LastWriteTimeUtc == mtime;
            PreserveDetail = "exists=" + after.Exists
                + " length " + length + "->" + after.Length
                + " mtime " + mtime.ToString("o") + "->" + after.LastWriteTimeUtc.ToString("o");
            Volatile.Write(ref _opens, opens);
            Volatile.Write(ref _armed, 1);
        }
    }
}
