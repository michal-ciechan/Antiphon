namespace Antiphon.Server.Domain.Enums;

public enum AgentTuiAuthenticationMode
{
    WrapperManaged = 0,
    ManagedEnvironment = 1
}

public enum AgentTuiProfileSource
{
    ImportedFile = 0,
    Operator = 1
}

public enum AgentTuiModelSource
{
    Curated = 0,
    Discovered = 1,
    Operator = 2
}

public enum AgentTuiModelAvailability
{
    Unverified = 0,
    Verified = 1,
    Stale = 2,
    Unavailable = 3
}

public enum AgentTuiCapabilityState
{
    Supported = 0,
    Unsupported = 1,
    Degraded = 2,
    Unknown = 3
}

public enum AgentTuiValidationStatus
{
    NeverRun = 0,
    Running = 1,
    Succeeded = 2,
    Partial = 3,
    Failed = 4,
    TimedOut = 5
}
