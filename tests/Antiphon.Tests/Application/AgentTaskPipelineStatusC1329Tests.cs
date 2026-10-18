using System.Collections;
using System.Data.Common;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1329. The started-card read stays output-equivalent and is limited to backlog
/// cards in the pipeline scope. A Done card's stage task is historical and is not part
/// of that id set.
/// </summary>
public partial class AgentTaskPipelineStatusTests
{
    [Test]
    public async Task C1329_started_card_read_is_bound_to_in_scope_backlog_cards()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var probe = new StartedCardCommandProbe();
        await using var db = new AppDbContext(
            TestDbFixture.CreateDbContextOptions(schema.ConnectionString, probe));
        var seed = new C557Seed(db);
        var home = seed.Board(1);
        var fresh = seed.Card(1, home);
        var started = seed.Card(2, home);
        seed.Task(AgentTaskRole.Code, AgentTaskStatus.Succeeded, started);
        var done = seed.Card(3, home, CardStatus.Done);
        seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, done);

        var foreignProjectId = C557Id(9001);
        var foreignBoardId = C557Id(1601);
        var foreignColumnId = C557Id(2601);
        db.Projects.Add(new Project
        {
            Id = foreignProjectId,
            Name = "pipeline-1329-foreign",
            GitRepositoryUrl = "https://example.test/c1329.git",
            CreatedAt = C557Now,
            UpdatedAt = C557Now,
        });
        db.Boards.Add(new Board
        {
            Id = foreignBoardId,
            ProjectId = foreignProjectId,
            Name = "Foreign",
            CreatedAt = C557Now,
            UpdatedAt = C557Now,
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = foreignColumnId,
            BoardId = foreignBoardId,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = C557Now,
            UpdatedAt = C557Now,
        });
        var foreignFresh = ForeignCard(11001, foreignBoardId, foreignColumnId);
        var foreignStarted = ForeignCard(11002, foreignBoardId, foreignColumnId);
        db.Cards.Add(foreignFresh);
        db.Cards.Add(foreignStarted);
        seed.Task(AgentTaskRole.Code, AgentTaskStatus.Succeeded, foreignStarted);
        await seed.SaveAsync();

        probe.Commands.Clear();
        var fleet = await C557Read(db);
        fleet.InvestigateBacklog.Total.ShouldBe(2);
        fleet.InvestigateBacklog.Items.Select(item => item.CardId).ShouldBe([fresh.Id, foreignFresh.Id]);
        var fleetIds = StartedCardIds(probe);
        fleetIds.ShouldBe([fresh.Id, started.Id, foreignFresh.Id, foreignStarted.Id], ignoreOrder: true);
        fleetIds.ShouldNotContain(done.Id);

        probe.Commands.Clear();
        var project = await CreateService(db, time: new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(C557Now))).GetAsync(
            new PipelineScope(PipelineScopeKind.Project, seed.ProjectId), CancellationToken.None);
        project.InvestigateBacklog.Total.ShouldBe(1);
        project.InvestigateBacklog.Items.Select(item => item.CardId).ShouldBe([fresh.Id]);
        var projectIds = StartedCardIds(probe);
        projectIds.ShouldBe([fresh.Id, started.Id], ignoreOrder: true);
        projectIds.ShouldNotContain(foreignFresh.Id);
        projectIds.ShouldNotContain(foreignStarted.Id);
        projectIds.ShouldNotContain(done.Id);
    }

    private static Card ForeignCard(int number, Guid boardId, Guid columnId) => new()
    {
        Id = C557Id(number),
        BoardId = boardId,
        BoardColumnId = columnId,
        Identifier = $"CARD-{number:0000}",
        Title = $"Card {number}",
        Description = "pipeline candidate",
        Status = CardStatus.Backlog,
        LabelsJson = "[]",
        CreatedAt = C557Now.AddDays(-30),
        UpdatedAt = C557Now,
    };

    private static HashSet<Guid> StartedCardIds(StartedCardCommandProbe probe)
    {
        var matches = probe.Commands.Where(command =>
            command.Text.Contains("AgentTasks", StringComparison.Ordinal)
            && command.Text.Contains("CardId", StringComparison.Ordinal)
            && command.Text.Contains("Status", StringComparison.Ordinal)
            && !command.Text.Contains("Title", StringComparison.Ordinal)
            && !command.Text.Contains("Goal", StringComparison.Ordinal)).ToList();
        matches.Count.ShouldBe(1, string.Join("\n---\n", probe.Commands.Select(command => command.Text)));
        return matches[0].Guids;
    }

    private sealed class StartedCardCommandProbe : DbCommandInterceptor
    {
        private static readonly Regex GuidPattern = new(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        public List<Captured> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(Captured.From(command, GuidPattern));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        internal sealed record Captured(string Text, HashSet<Guid> Guids)
        {
            public static Captured From(DbCommand command, Regex guidPattern)
            {
                var ids = new HashSet<Guid>();
                foreach (Match match in guidPattern.Matches(command.CommandText))
                {
                    if (Guid.TryParse(match.Value, out var parsed))
                        ids.Add(parsed);
                }

                foreach (DbParameter parameter in command.Parameters)
                    Collect(parameter.Value, ids, guidPattern);
                return new Captured(command.CommandText, ids);
            }

            private static void Collect(object? value, HashSet<Guid> ids, Regex guidPattern)
            {
                switch (value)
                {
                    case null:
                    case DBNull:
                        return;
                    case Guid id:
                        ids.Add(id);
                        return;
                    case string text:
                        foreach (Match match in guidPattern.Matches(text))
                        {
                            if (Guid.TryParse(match.Value, out var parsed))
                                ids.Add(parsed);
                        }
                        return;
                    case IEnumerable enumerable when value is not byte[]:
                        foreach (var item in enumerable)
                            Collect(item, ids, guidPattern);
                        return;
                }
            }
        }
    }
}
