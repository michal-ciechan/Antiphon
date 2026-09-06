using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

public sealed class AntiphonGatewayOptionsValidator : IValidateOptions<AntiphonGatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, AntiphonGatewayOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.AntiphonConsumerGroup) || options.AntiphonConsumerGroup != options.AntiphonConsumerGroup.Trim())
            failures.Add("AntiphonConsumerGroup must be nonblank without surrounding whitespace.");
        if (string.IsNullOrWhiteSpace(options.InboundTopic) || options.InboundTopic != options.InboundTopic.Trim())
            failures.Add("InboundTopic must be nonblank without surrounding whitespace.");
        if (string.Equals(options.AntiphonConsumerGroup, options.ConsumerGroup, StringComparison.Ordinal))
            failures.Add("AntiphonConsumerGroup must differ from ConsumerGroup.");
        if (options.ExpectedAntiphonConsumerGroup is not null &&
            !string.Equals(options.ExpectedAntiphonConsumerGroup, options.AntiphonConsumerGroup, StringComparison.Ordinal))
            failures.Add("ExpectedAntiphonConsumerGroup must exactly match AntiphonConsumerGroup.");
        if (options.RequireExpectedAntiphonConsumerGroup && options.InboundUnconsumedMonitorEnabled &&
            string.IsNullOrWhiteSpace(options.ExpectedAntiphonConsumerGroup))
            failures.Add("ExpectedAntiphonConsumerGroup is required when the monitor is enabled.");
        if (options.ObservationBudgetSeconds < 1) failures.Add("ObservationBudgetSeconds must be at least 1.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
