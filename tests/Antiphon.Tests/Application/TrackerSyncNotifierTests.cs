using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0171 §8: every notification outcome. The sync has already committed by the time the
/// notifier runs, so nothing here may throw — a failure is a reason on the run result.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class TrackerSyncNotifierTests
{
    [Test]
    public async Task A_board_with_changes_sends_the_summary_to_its_notify_channel()
    {
        await using var h = await HarnessAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", "-5052370282");
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: channel.Id.ToString());

            var results = await h.Notifier.NotifyAsync(Run(BoardResult(board, "Antiphon board")), CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Sent.ShouldBeTrue();
            result.ChannelId.ShouldBe(channel.Id);
            result.Reason.ShouldBeNull();

            var sent = h.Messaging.SentReplies.ShouldHaveSingleItem();
            sent.Channel.ShouldBe("telegram");
            sent.ConversationId.ShouldBe("-5052370282");
            sent.Text.ShouldContain("Antiphon <-> GitHub sync: Antiphon board");
            sent.Text.ShouldContain("CARD-0171");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task An_exact_case_insensitive_title_resolves_the_channel()
    {
        await using var h = await HarnessAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", "-5052370282");
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: "family");

            var results = await h.Notifier.NotifyAsync(Run(BoardResult(board, "Antiphon board")), CancellationToken.None);

            results.ShouldHaveSingleItem().ChannelId.ShouldBe(channel.Id);
            h.Messaging.SentReplies.Count.ShouldBe(1);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_board_without_notify_channel_reports_notify_channel_unset_and_sends_nothing()
    {
        await using var h = await HarnessAsync();
        try
        {
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: null);

            var results = await h.Notifier.NotifyAsync(Run(BoardResult(board, "Antiphon board")), CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Sent.ShouldBeFalse();
            result.Reason.ShouldBe("notify_channel_unset");
            result.ChannelId.ShouldBeNull();
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task An_unknown_guid_or_title_reports_channel_not_found()
    {
        await using var h = await HarnessAsync();
        try
        {
            var byGuid = await h.SeedBoardAsync("Board A", notifyChannel: Guid.NewGuid().ToString());
            var byTitle = await h.SeedBoardAsync("Board B", notifyChannel: "no such channel");

            var results = await h.Notifier.NotifyAsync(
                Run(BoardResult(byGuid, "Board A"), BoardResult(byTitle, "Board B")),
                CancellationToken.None);

            results.Count.ShouldBe(2);
            results.ShouldAllBe(r => !r.Sent && r.Reason == "channel_not_found");
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task Two_channels_sharing_a_title_report_channel_ambiguous()
    {
        await using var h = await HarnessAsync();
        try
        {
            await h.SeedChannelAsync("Family", "-1");
            await h.SeedChannelAsync("family", "-2");
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: "Family");

            var results = await h.Notifier.NotifyAsync(Run(BoardResult(board, "Antiphon board")), CancellationToken.None);

            results.ShouldHaveSingleItem().Reason.ShouldBe("channel_ambiguous");
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_disabled_channel_reports_channel_disabled_rather_than_a_side_door()
    {
        await using var h = await HarnessAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", "-5052370282", enabled: false);
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: channel.Id.ToString());

            var results = await h.Notifier.NotifyAsync(Run(BoardResult(board, "Antiphon board")), CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Sent.ShouldBeFalse();
            result.Reason.ShouldBe("channel_disabled");
            result.ChannelId.ShouldBe(channel.Id);
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_throwing_producer_reports_send_failed_and_never_escapes()
    {
        var producer = new ThrowingProducer();
        await using var h = await HarnessAsync(producer);
        try
        {
            var channel = await h.SeedChannelAsync("Family", "-5052370282");
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: channel.Id.ToString());

            var run = Run(BoardResult(board, "Antiphon board"));
            var results = await h.Notifier.NotifyAsync(run, CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Sent.ShouldBeFalse();
            result.Reason.ShouldBe("send_failed");
            result.ChannelId.ShouldBe(channel.Id);
            producer.Attempts.ShouldBe(1);
            // The run result the caller needs is intact.
            run.Boards.ShouldHaveSingleItem().Changes.Count.ShouldBe(1);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task Two_boards_on_one_channel_send_one_message_with_both_blocks()
    {
        await using var h = await HarnessAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", "-5052370282");
            var a = await h.SeedBoardAsync("Board A", notifyChannel: channel.Id.ToString());
            var b = await h.SeedBoardAsync("Board B", notifyChannel: channel.Id.ToString());

            var results = await h.Notifier.NotifyAsync(
                Run(BoardResult(a, "Board A"), BoardResult(b, "Board B")), CancellationToken.None);

            results.Count.ShouldBe(2);
            results.ShouldAllBe(r => r.Sent);

            var sent = h.Messaging.SentReplies.ShouldHaveSingleItem();
            sent.Text.ShouldContain("Antiphon <-> GitHub sync: Board A");
            sent.Text.ShouldContain("Antiphon <-> GitHub sync: Board B");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task Two_boards_on_different_channels_send_two_messages()
    {
        await using var h = await HarnessAsync();
        try
        {
            var first = await h.SeedChannelAsync("Family", "-1");
            var second = await h.SeedChannelAsync("Ops", "-2");
            var a = await h.SeedBoardAsync("Board A", notifyChannel: first.Id.ToString());
            var b = await h.SeedBoardAsync("Board B", notifyChannel: second.Id.ToString());

            await h.Notifier.NotifyAsync(
                Run(BoardResult(a, "Board A"), BoardResult(b, "Board B")), CancellationToken.None);

            h.Messaging.SentReplies.Count.ShouldBe(2);
            h.Messaging.SentReplies.Single(r => r.ConversationId == "-1").Text.ShouldContain("Board A");
            h.Messaging.SentReplies.Single(r => r.ConversationId == "-2").Text.ShouldContain("Board B");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_board_with_no_changes_gets_no_entry_and_no_message()
    {
        await using var h = await HarnessAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", "-5052370282");
            var board = await h.SeedBoardAsync("Antiphon board", notifyChannel: channel.Id.ToString());

            // Issues pulled and a skip, but nothing written: not a change.
            var quiet = new TrackerSyncBoardResult(board.Id, "Antiphon board", 42, 0, 0, 0, 0, 0, ["skipped"]);
            var results = await h.Notifier.NotifyAsync(new TrackerSyncRunResult([quiet]), CancellationToken.None);

            results.ShouldBeEmpty();
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static TrackerSyncRunResult Run(params TrackerSyncBoardResult[] boards) => new(boards);

    private static TrackerSyncBoardResult BoardResult(Board board, string name) =>
        new(board.Id, name, 1, 1, 0, 0, 0, 0, [])
        {
            Changes = [new TrackerSyncChange(
                TrackerSyncChangeKind.CommentIn, "CARD-0171", "#17",
                "https://github.com/michal-ciechan/Antiphon/issues/17")]
        };

    private static Task<Harness> HarnessAsync(IAntiphonMessagingProducer? producer = null) =>
        Task.FromResult(new Harness(producer));

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<Guid> _projectIds = [];
        private readonly List<Guid> _channelIds = [];
        private readonly string _tempRoot =
            Path.Combine(Path.GetTempPath(), $"antiphon-notify-{Guid.NewGuid():N}");

        public Harness(IAntiphonMessagingProducer? producer)
        {
            Db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            Messaging = producer as FakeAntiphonMessagingClient ?? new FakeAntiphonMessagingClient();
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
            var channels = new ChatChannelService(Db, clock, producer ?? Messaging);
            Notifier = new TrackerSyncNotifier(Db, channels, NullLogger<TrackerSyncNotifier>.Instance);
        }

        public AppDbContext Db { get; }
        public FakeAntiphonMessagingClient Messaging { get; }
        public TrackerSyncNotifier Notifier { get; }

        public async Task<ChatChannel> SeedChannelAsync(string title, string externalId, bool enabled = true)
        {
            var now = DateTime.UtcNow;
            var channel = new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = externalId,
                Kind = ChatChannelKind.Group,
                Title = title,
                Enabled = enabled,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.ChatChannels.Add(channel);
            await Db.SaveChangesAsync();
            _channelIds.Add(channel.Id);
            return channel;
        }

        public async Task<Board> SeedBoardAsync(string name, string? notifyChannel)
        {
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"Notify Project {Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/repo.git",
                LocalRepositoryPath = Path.Combine(_tempRoot, "repo"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now
            };
            Directory.CreateDirectory(project.LocalRepositoryPath!);

            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = name,
                TrackerKind = TrackerKind.GitHubIssues,
                TrackerActivatedAt = now,
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
                Project = project
            };
            project.Boards.Add(board);

            var lines = new List<string>
            {
                "---",
                "tracker:",
                "  kind: github_issues",
                "  repository: michal-ciechan/Antiphon",
                "  active_states: [open]"
            };
            if (notifyChannel is not null)
                lines.Add($"  notify_channel: {notifyChannel}");
            lines.Add("---");
            lines.Add("Work on {{ issue.identifier }}.");

            board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                Version = 1,
                Name = "Tracked",
                Content = string.Join('\n', lines),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                Board = board
            });

            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            _projectIds.Add(project.Id);
            return board;
        }

        public async Task CleanupAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            if (_channelIds.Count > 0)
                await db.ChatChannels.Where(c => _channelIds.Contains(c.Id)).ExecuteDeleteAsync();
            if (_projectIds.Count > 0)
            {
                var boardIds = await db.Boards
                    .Where(b => _projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
                await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
                await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
                await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();
            }

            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
            catch (IOException) { /* best effort */ }
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class ThrowingProducer : IAntiphonMessagingProducer
    {
        public int Attempts { get; private set; }

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("broker unavailable");
        }
    }
}
