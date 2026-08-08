using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// <c>AgentService.CreateAsync</c> picks a slug with a check-then-insert
/// (<c>UniqueSlugAsync</c> asks "is this slug taken?" and then inserts), which races against the
/// unique index <c>IX_Agents_Slug</c>. Two agents created with the same name at the same moment
/// both see the slug free and both insert; one loses.
///
/// That is the mechanism behind an intermittent <c>Board_create_card_and_detail_round_trip...</c>
/// failure under parallel load. It was hard to see because the handler reported every failed save
/// as "another operation changed agent data" and discarded the exception, so a duplicate-key error
/// was indistinguishable from a genuine concurrency conflict.
///
/// These tests pin the diagnosis (the error names the constraint) and the behaviour (the loser
/// gets a distinct slug instead of an error).
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentCreateRaceTests
{
    [Test]
    public async Task Concurrent_creates_with_the_same_name_both_succeed_with_distinct_slugs()
    {
        var tempRoot = NewTempRoot();
        var name = $"Race Agent {Guid.NewGuid():N}";
        try
        {
            // Separate contexts: two requests, each with its own scope, exactly as the API runs.
            await using var dbA = CreateContext();
            await using var dbB = CreateContext();
            var serviceA = CreateService(dbA, tempRoot);
            var serviceB = CreateService(dbB, tempRoot);

            var both = await Task.WhenAll(
                CreateAsync(serviceA, name, Path.Combine(tempRoot, "a")),
                CreateAsync(serviceB, name, Path.Combine(tempRoot, "b")));

            both.ShouldAllBe(r => r.Error == null);
            await using var verify = CreateContext();
            var slugs = await verify.Agents
                .Where(a => a.Name == name)
                .Select(a => a.Slug)
                .ToListAsync();
            slugs.Count.ShouldBe(2);
            slugs.Distinct().Count().ShouldBe(2);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>
    /// The diagnosis, not just the outcome: if a duplicate key does escape, the 409 has to name the
    /// constraint and keep the Postgres exception attached. A bare "another operation changed agent
    /// data" sent an investigation looking for a concurrency bug that was not there.
    /// </summary>
    [Test]
    public void A_duplicate_key_failure_is_described_by_its_constraint_not_paraphrased()
    {
        var postgres = BuildUniqueViolation("IX_Agents_Slug", "Agents");
        var ex = new DbUpdateException("save failed", postgres);

        var described = AgentService.DescribeDbFailure(ex);

        described.ShouldContain("duplicate value");
        described.ShouldContain("IX_Agents_Slug");
    }

    [Test]
    public void A_non_postgres_failure_falls_back_to_the_innermost_message()
    {
        var ex = new DbUpdateException("save failed", new InvalidOperationException("disk on fire"));

        AgentService.DescribeDbFailure(ex).ShouldBe("disk on fire");
    }

    [Test]
    public async Task A_conflict_raised_from_a_database_error_keeps_that_error_attached()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var db = CreateContext();
            var service = CreateService(db, tempRoot);

            // A working directory far past the 1000-char column limit fails the insert, which is a
            // database error rather than a concurrency conflict.
            var ex = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(
                new CreateAgentRequest(
                    $"Overflow {Guid.NewGuid():N}",
                    Path.Combine(tempRoot, new string('x', 1200))),
                CancellationToken.None));

            // The point of the fix: something to debug from.
            ex.InnerException.ShouldNotBeNull();
            ex.Message.ShouldNotBe("Agent could not be created because another operation changed agent data.");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ------------------------------------------------------------------

    private static async Task<(AgentDetailDto? Agent, Exception? Error)> CreateAsync(
        AgentService service, string name, string workingDirectory)
    {
        try
        {
            // Yield first so both inserts are genuinely in flight rather than serialised by the
            // synchronous prefix of CreateAsync.
            await Task.Yield();
            return (await service.CreateAsync(
                new CreateAgentRequest(name, workingDirectory), CancellationToken.None), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static PostgresException BuildUniqueViolation(string constraint, string table) =>
        new(
            messageText: $"duplicate key value violates unique constraint \"{constraint}\"",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: PostgresErrorCodes.UniqueViolation,
            constraintName: constraint,
            tableName: table);

    private static AgentService CreateService(AppDbContext db, string tempRoot) =>
        new(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            new MockEventBus(),
            TimeProvider.System,
            new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-agent-race-{Guid.NewGuid():N}");

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var boardIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot) && a.BoardId != null)
            .Select(a => a.BoardId!.Value)
            .ToListAsync();
        await db.Agents.Where(a => a.WorkingDirectory.StartsWith(tempRoot)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .ExecuteDeleteAsync();
    }

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }
}
