using System.Net;
using System.Net.Http.Json;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PipelineHandoffPilotTests
{
    [Test]
    public async Task C1281_DelayAccounting()
    {
        await using var host = await PilotHost.CreateAsync();
        using var response = await host.Client.PostAsJsonAsync(host.Url, new
        {
            attempt = host.World.Owner.Attempt, settlementEventId = host.EventId,
            nextStage = AgentTaskRole.Code, reasonCode = "capacity", detail = "Waiting for a seat",
            nextObservationAt = DateTime.UtcNow.AddMinutes(5), expectedObservationSequence = 0
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "explicit-consumption-is-durable: " + await response.Content.ReadAsStringAsync());
        await using var db = host.World.CreateContext();
        var action = await db.PipelineHandoffActions.SingleAsync();
        action.ConsumedAt.ShouldNotBeNull("explicit-consumption-is-durable");
        action.ReasonCode.ShouldBe("capacity");
        (await db.PipelineHandoffObservations.SingleAsync()).Detail.ShouldBe("Waiting for a seat");
    }

    // Component composition only. S6 owns real Program registration. Reflection permits the
    // first committed acceptance test to observe the missing production route as HTTP 404.
    private sealed class PilotHost : IAsyncDisposable
    {
        public C544World World { get; private set; } = null!;
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public Guid EventId { get; private set; }
        public string Url => $"/api/agent-tasks/{World.Owner.Id:D}/handoff-actions";
        private string TokenPath { get; } = Path.Combine(Path.GetTempPath(), "c1281-operator-" + Guid.NewGuid().ToString("N"));

        public static async Task<PilotHost> CreateAsync()
        {
            var host = new PilotHost();
            List<ServiceDescriptor> graph = [];
            host.World = await C544World.CreateAsync(configure: services => graph = services.ToList());
            await using (var db = host.World.CreateContext())
            {
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == host.World.Owner.Id);
                owner.NextStage = PipelineHandoffKind.Code;
                var ev = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = owner.Id,
                    Type = AgentTaskEventType.Completed, At = owner.CompletedAt!.Value };
                host.EventId = ev.Id;
                db.Add(ev);
                await db.SaveChangesAsync();
            }
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            foreach (var descriptor in graph) builder.Services.Add(descriptor);
            builder.Services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings { OperatorTokenPath = host.TokenPath }));
            var service = typeof(AgentTaskService).Assembly.GetType("Antiphon.Server.Application.Services.PipelineHandoffActionService");
            if (service is not null) builder.Services.AddScoped(service);
            host.App = builder.Build();
            host.App.UseMiddleware<ExceptionMiddleware>();
            var endpoints = typeof(AgentTaskService).Assembly.GetType("Antiphon.Server.Api.Endpoints.PipelineHandoffActionEndpoints");
            endpoints?.GetMethod("MapPipelineHandoffActionEndpoints")!.Invoke(null, [host.App]);
            await host.App.StartAsync();
            host.Client = host.App.GetTestClient();
            host.Client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(host.TokenPath));
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
            await World.DisposeAsync();
            File.Delete(TokenPath);
        }
    }
}
