using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1756: a bound tailer must not open the transcript's identity handle on every 300 ms poll.
/// Counts are the witness. Wall time between polls is the backoff witness. Replacing the inode
/// must still surface <see cref="TranscriptTailer.BoundIdentityDrift"/> inside
/// <see cref="TranscriptTailer.IdentityStaleness"/>.
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

        public static World Start(string body)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1756-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, "transcript.jsonl");
            File.WriteAllText(path, body);
            var tailer = new TranscriptTailer(
                Guid.NewGuid(), root, new SessionRunnerEventHub(), NullLogger.Instance,
                knownTranscriptPath: path, forkScanInterval: TimeSpan.FromDays(1));
            var world = new World(root, path, tailer);
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
}
