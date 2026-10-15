using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PipelineSpeedIntegrationTests
{
    [Test]
    public async Task C1281_B2Authorization()
    {
        await new PipelineHandoffPilotTests().C1281_WriterAuthorization();
        await using var host = await PipelineHandoffPilotTests.PilotHost.CreateAsync();
        using var other = host.App.GetTestClient();
        other.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", await host.CredentialAsync("other-recipient"));
        using var response = await other.PostAsJsonAsync(host.Url, host.Request("consumed", "not my completion"));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "another-in-scope-caller-refused");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("handoff_recipient_denied");
        await using var db = host.World.CreateContext();
        (await db.PipelineHandoffActions.CountAsync()).ShouldBe(0, "denied-post-no-action");
        (await db.PipelineHandoffObservations.CountAsync()).ShouldBe(0, "denied-post-no-observation");
    }

    [Test]
    public async Task C1281_B1ReceiptImport()
    {
        await new AdvisoryChangePolicyTests().C1281_IncompleteNeverClean();
        await ReceiptVariantsAsync();
    }

    [Test]
    public async Task C1281_B1CorrectionTransaction()
    {
        await new AdvisoryChangePolicyTests().C1281_ReceiptCorrection();
        await CorrectionTransactionsAsync();
    }

    [Test]
    public async Task C1281_AdmissionHolds()
    {
        foreach (var variant in new[] { "dependency", "overlap", "decision", "stage" })
        {
            await using var w = await C544World.CreateAsync();
            var action = await SeedActionAsync(w);
            var request = ChildRequest(w) with { Scope = "server/Application/Services/AgentTaskService.cs" };
            await using (var db = w.CreateContext())
            {
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == w.Owner.Id);
                owner.NextStage = variant == "stage" ? PipelineHandoffKind.Review : PipelineHandoffKind.Code;
                owner.ParentSessionId = w.CallerSessionId;
                if (variant == "dependency")
                {
                    var blocker = new Card { Id = Guid.NewGuid(), BoardId = w.Board.Id,
                        BoardColumnId = w.Card.BoardColumnId, Identifier = "CARD-9998", Title = "blocker" };
                    db.Add(blocker);
                    db.Add(new CardRelationship { Id = Guid.NewGuid(), CardId = w.Card.Id,
                        RelatedCardId = blocker.Id, Kind = CardRelationshipKind.BlockedBy });
                }
                if (variant == "decision") (await db.Cards.SingleAsync(c => c.Id == w.Card.Id)).Status = CardStatus.NeedsDecision;
                if (variant == "overlap")
                    db.Add(new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "overlapping writer", Goal = "write",
                        RepoPath = w.RepositoryPath, WorkingDirectory = w.RepositoryPath, Scope = request.Scope,
                        Workspace = WorkspaceMode.Worktree, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Working });
                await db.SaveChangesAsync();
            }
            var error = await Should.ThrowAsync<ConflictException>(() => CreateAsync(w, request, action.Id), variant + "-zero-child");
            error.Code.ShouldBe("handoff_" + variant + "_held", variant + "-refusal-code");
            await using var fresh = w.CreateContext();
            (await fresh.PipelineHandoffActions.SingleAsync(a => a.Id == action.Id)).ChildTaskId.ShouldBeNull(variant + "-zero-child");
            (await fresh.AgentTasks.CountAsync(t => t.CardId == w.Card.Id && t.Id != w.Owner.Id)).ShouldBe(0, variant + "-zero-child");
        }
    }
}
