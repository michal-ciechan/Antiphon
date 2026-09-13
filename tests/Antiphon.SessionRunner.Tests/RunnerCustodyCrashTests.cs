using System.Net;
using System.Net.Http.Json;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerCustodyCrashTests
{
    [Test]
    public async Task Actual_runner_crash_adopts_same_host_and_http_read_does_not_seal()
    {
        await using var fixture = new RunnerCustodyTests.CustodyFixture();
        await using var first = await LocalHttpRunner.StartAsync(fixture.Settings);
        var capability = (await first.Http.GetFromJsonAsync<RunnerCapabilitiesDto>("/capabilities"))!;
        capability.Features!.ShouldContain(RunnerCapabilityFeatures.VerificationCustodyV1);
        capability.VerificationCustodyBackend.ShouldBe("windows-job-v1");
        capability.RunnerStoreId.ShouldNotBeNull();
        using var created = await first.Http.PostAsJsonAsync("/sessions", fixture.Request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = (await created.Content.ReadFromJsonAsync<RunnerSessionDto>())!;
        fixture.OwnHost(dto);
        await first.CrashAsync();
        fixture.Host!.HasExited.ShouldBeFalse();
        await using var second = await LocalHttpRunner.StartAsync(fixture.Settings);
        (await second.Http.GetFromJsonAsync<RunnerCapabilitiesDto>("/capabilities"))!.RunnerStoreId.ShouldBe(capability.RunnerStoreId);
        var adopted = await second.Http.GetFromJsonAsync<RunnerSessionDto>($"/sessions/{dto.SessionId:D}");
        adopted!.Pid.ShouldBe(dto.Pid);
        adopted.VerificationBinding.ShouldBe(fixture.Binding);
        var read = await second.Http.GetFromJsonAsync<VerificationCustodyStatus>(ReadRoute(fixture.Binding));
        read!.State.ShouldBe(VerificationCustodyState.Tracking);
        File.Exists(new RunnerCustodyLedger(fixture.CustodyRoot).Store.PathFor(fixture.Binding.ExecutionId, "producer-seal.json"))
            .ShouldBeFalse();
        using var sealedResponse = await second.Http.PostAsJsonAsync(SealRoute(fixture.Binding), fixture.Binding);
        sealedResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await sealedResponse.Content.ReadAsStringAsync());
        (await sealedResponse.Content.ReadFromJsonAsync<VerificationCustodyStatus>())!.State.ShouldBe(VerificationCustodyState.Draining);
        using var late = await second.Http.PostAsJsonAsync($"/sessions/{dto.SessionId:D}/input", new RunnerInputRequest("late"));
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var killed = await second.Http.PostAsync($"/sessions/{dto.SessionId:D}/kill", null);
        killed.IsSuccessStatusCode.ShouldBeTrue();
        var final = await WaitFinalAsync(second.Http, fixture.Binding);
        new RunnerCustodyLedger(fixture.CustodyRoot).ReadFinal(fixture.Binding)!.Receipt.ShouldBe(final.Receipt);
        await fixture.Host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Producer_receipt_survives_both_process_crashes_before_runner_import()
    {
        await using var fixture = new RunnerCustodyTests.CustodyFixture();
        await using var first = await LocalHttpRunner.StartAsync(fixture.Settings);
        using var created = await first.Http.PostAsJsonAsync("/sessions", fixture.Request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = (await created.Content.ReadFromJsonAsync<RunnerSessionDto>())!;
        fixture.OwnHost(dto);
        await first.CrashAsync();
        await using (var host = await PtyHostClient.ConnectAsync(PtyHostProtocol.PipeNameFor(dto.SessionId),
                         TimeSpan.FromSeconds(10), CancellationToken.None))
            await host.InputAsync("exit\r", CancellationToken.None);
        var ledger = new RunnerCustodyLedger(fixture.CustodyRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        VerificationCustodyStatus? producer;
        while ((producer = ledger.ReadProducer(fixture.Binding)) is null) await Task.Delay(50, timeout.Token);
        ledger.ReadFinal(fixture.Binding).ShouldBeNull("the runner process is dead before import");
        fixture.Host!.Kill();
        await fixture.Host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await using var second = await LocalHttpRunner.StartAsync(fixture.Settings);
        var final = await second.Http.GetFromJsonAsync<VerificationCustodyStatus>(ReadRoute(fixture.Binding));
        final!.State.ShouldBe(VerificationCustodyState.Exited);
        final.Receipt.ShouldBe(producer.Receipt);
        new RunnerCustodyLedger(fixture.CustodyRoot).ReadFinal(fixture.Binding)!.Receipt.ShouldBe(producer.Receipt);
        using var wrongGeneration = await second.Http.GetAsync(ReadRoute(fixture.Binding with
        { Generation = fixture.Binding.Generation with { AcceptedStartedAt = fixture.Binding.Generation.AcceptedStartedAt.AddTicks(10) } }));
        wrongGeneration.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static string ReadRoute(VerificationExecutionBinding binding) =>
        $"/sessions/{binding.Generation.SessionId:D}/executions/{binding.ExecutionId:D}/custody?acceptedStartedAt="
        + Uri.EscapeDataString(binding.Generation.AcceptedStartedAt.ToString("O"));
    private static string SealRoute(VerificationExecutionBinding binding) =>
        $"/sessions/{binding.Generation.SessionId:D}/executions/{binding.ExecutionId:D}/seal";
    private static async Task<VerificationCustodyStatus> WaitFinalAsync(HttpClient http, VerificationExecutionBinding binding)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var status = await http.GetFromJsonAsync<VerificationCustodyStatus>(ReadRoute(binding), timeout.Token);
            if (status!.Receipt is not null) return status;
            await Task.Delay(50, timeout.Token);
        }
    }
}
