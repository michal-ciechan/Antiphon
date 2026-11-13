using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1884. Ingest stays schema 1 and refuses a top-consumer row that is not publishable.</summary>
[Category("Unit")]
public class HostTopConsumerIngestTests
{
    private static readonly Guid Store = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Boot = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Test]
    public void Valid_compiler_row_is_accepted()
    {
        Should.NotThrow(() => Validate(Sample([new HostLoadTopConsumer(1, 10, "VBCSCompiler", "compiler", 5.3, 409600)])));
    }

    [Test]
    public void Ninth_row_and_unsafe_name_are_rejected()
    {
        var nine = Enumerable.Range(1, 9).Select(pid => new HostLoadTopConsumer(pid, 1, "pwsh", "other", 0.1, 1)).ToArray();
        Should.Throw<ValidationException>(() => Validate(Sample(nine)));
        Should.Throw<ValidationException>(() => Validate(Sample([new HostLoadTopConsumer(1, 1, "secret/TOKEN", "compiler", 1, 1)])));
        Should.Throw<ValidationException>(() => Validate(Sample([new HostLoadTopConsumer(1, 1, "pipename=SUPERSECRET", "other", 1, 1)])));
    }

    [Test]
    public void Unknown_role_is_rejected()
    {
        Should.Throw<ValidationException>(() => Validate(Sample(
            [new HostLoadTopConsumer(1, 1, "VBCSCompiler", "compiler-server", 1, 1)])));
    }

    [Test]
    public void Null_cpu_and_rss_stay_publishable()
    {
        Should.NotThrow(() => Validate(Sample([new HostLoadTopConsumer(1, 1, "VBCSCompiler", "compiler", null, null)])));
    }

    [Test]
    public void Schema_stays_one_and_session_names_stay_closed()
    {
        Should.Throw<ValidationException>(() => Validate(Sample([], schema: 2)));
        Should.Throw<ValidationException>(() => Validate(Sample([], processName: "VBCSCompiler")));
        var accepted = Sample([new HostLoadTopConsumer(1, 10, "dotnet", "test", 1.4, 1024)]);
        Should.NotThrow(() => Validate(accepted));
        accepted.SchemaVersion.ShouldBe(1);
    }

    private static HostLoadSample Sample(IReadOnlyList<HostLoadTopConsumer> top, int schema = 1, string? processName = null)
        => new()
        {
            SchemaVersion = schema,
            Source = new HostLoadSource("runner-a", Store, Boot, 1),
            SampledAt = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero),
            IntervalSeconds = 5,
            CpuPercent = 10,
            Processes = processName is null ? [] : [new HostLoadProcess(processName, null, 1, 2)],
            TopConsumers = top,
        };

    private static void Validate(HostLoadSample sample) =>
        HostLoadIngestService.Validate(new HostLoadIngestBatch("runner-a", null,
            new HostLoadHistoryPage(1, sample.Source, [sample], [], null, false, true)));
}
