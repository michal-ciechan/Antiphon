using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentSessionRuntimeTests
{
    /// <summary>
    /// CARD-1604 D-1. The column moves only when a pull carries <c>token_count</c>.
    /// Deleting <c>StampCodexTokenCountAsync</c> leaves it null. Dropping the newest-wins
    /// guard lets the older time, or a pull with none, replace T. The stamp adds no transcript row.
    /// </summary>
    [Test]
    public async Task a_pulled_token_count_stamps_the_column_and_an_older_or_empty_pull_keeps_it()
    {
        var newer = SessionGeneration.Normalize(new DateTime(2026, 10, 28, 16, 3, 58, DateTimeKind.Utc));
        var older = SessionGeneration.Normalize(newer.AddMinutes(-1));
        var runner = new TokenCountPullClient { CodexTokenCountAt = newer };
        await using var f = await PersistFixture.CreateAsync(runner: runner);
        await using (var db = f.Db())
        {
            await db.AgentSessions.Where(s => s.Id == f.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AgentKind, AgentKind.Codex));
        }

        async Task<DateTime?> ReadAsync()
        {
            await using var db = f.Db();
            return await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == f.SessionId)
                .Select(s => s.CodexTokenCountAt)
                .SingleAsync();
        }

        (await f.Runtime.CatchUpTranscriptAsync(f.SessionId, CancellationToken.None)).ShouldBeFalse();
        (await ReadAsync()).ShouldBe(newer);
        (await f.RowsAsync()).ShouldBeEmpty();

        runner.CodexTokenCountAt = older;
        await f.Runtime.CatchUpTranscriptAsync(f.SessionId, CancellationToken.None);
        (await ReadAsync()).ShouldBe(newer);

        runner.CodexTokenCountAt = null;
        await f.Runtime.CatchUpTranscriptAsync(f.SessionId, CancellationToken.None);
        (await ReadAsync()).ShouldBe(newer);
        (await f.RowsAsync()).ShouldBeEmpty();
    }

    private sealed class TokenCountPullClient : ISessionRunnerClient
    {
        public DateTime? CodexTokenCountAt { get; set; }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, string.Empty, 0));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0, CodexTokenCountAt: CodexTokenCountAt));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
