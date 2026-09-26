namespace Antiphon.Server.Application.Interfaces;

/// <summary>CARD-0726 D-2: "look again" for one runner. The payload is not a fact.</summary>
public interface IRunnerEligibilityObserver
{
    void Changed(string runnerId);
}
