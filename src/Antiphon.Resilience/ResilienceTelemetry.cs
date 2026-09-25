using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Antiphon.Resilience;

public sealed class ResilienceTelemetry : IDisposable
{
    public const string MeterName = "Antiphon.Resilience";
    public const string RetryAttempts = "retry.attempts";
    public const string OperationsCompleted = "operations.completed";
    public const string CircuitTransitions = "circuit.transitions";
    public const string CircuitRejections = "circuit.rejections";
    public const string BudgetExhausted = "budget.exhausted";
    public const string RetryDelay = "retry.delay";
    public const string OperationDuration = "operation.duration";

    private readonly Meter _meter;
    private readonly Counter<long> _retryAttempts;
    private readonly Counter<long> _operationsCompleted;
    private readonly Counter<long> _circuitTransitions;
    private readonly Counter<long> _circuitRejections;
    private readonly Counter<long> _budgetExhausted;
    private readonly Histogram<double> _retryDelay;
    private readonly Histogram<double> _operationDuration;

    public ResilienceTelemetry()
    {
        _meter = new Meter(MeterName);
        _retryAttempts = _meter.CreateCounter<long>(RetryAttempts);
        _operationsCompleted = _meter.CreateCounter<long>(OperationsCompleted);
        _circuitTransitions = _meter.CreateCounter<long>(CircuitTransitions);
        _circuitRejections = _meter.CreateCounter<long>(CircuitRejections);
        _budgetExhausted = _meter.CreateCounter<long>(BudgetExhausted);
        _retryDelay = _meter.CreateHistogram<double>(RetryDelay, unit: "s");
        _operationDuration = _meter.CreateHistogram<double>(OperationDuration, unit: "s");
    }

    public void Retry(string family, string dependency, string reason, TimeSpan delay)
    {
        var tags = Tags(family, dependency, reason);
        _retryAttempts.Add(1, tags);
        _retryDelay.Record(delay.TotalSeconds, tags);
    }

    public void Completed(string family, string dependency, string outcome, TimeSpan duration)
    {
        _operationsCompleted.Add(1, Tags(family, dependency, outcome));
        _operationDuration.Record(duration.TotalSeconds, Tags(family, dependency, outcome));
    }

    public void Circuit(string family, string dependency, string transition)
    {
        _circuitTransitions.Add(1, Tags(family, dependency, transition));
    }

    public void Rejected(string family, string dependency, string reason)
    {
        _circuitRejections.Add(1, Tags(family, dependency, reason));
    }

    public void Exhausted(string family, string dependency)
    {
        _budgetExhausted.Add(1, Tags(family, dependency, "budget"));
    }

    public void Dispose() => _meter.Dispose();

    private static TagList Tags(string family, string dependency, string reason) => new()
    {
        { "family", Bound(family) },
        { "dependency", Bound(dependency) },
        { "reason", Bound(reason) },
    };

    private static string Bound(string value) =>
        value.Length <= 64 ? value : value[..64];
}
