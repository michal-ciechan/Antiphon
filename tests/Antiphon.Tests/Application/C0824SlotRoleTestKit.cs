using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S1c S-5. A settable <see cref="IOptionsMonitor{TOptions}"/> for <c>Delegation:WorkspaceSlotRoles</c>
/// (the C1677TempSweepServerTests.Monitor pattern) that can also throw the <see cref="OptionsValidationException"/> an
/// invalid hot reload makes <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> throw. Only the role value is set:
/// the flag stays at its default (false), as the container's own monitor has it.
/// </summary>
internal sealed class MutableDelegationMonitor : IOptionsMonitor<DelegationSettings>
{
    private volatile DelegationSettings _value;
    private volatile bool _throws;

    public MutableDelegationMonitor(string roles) => _value = new DelegationSettings { WorkspaceSlotRoles = roles };

    public string Roles
    {
        set
        {
            _value = new DelegationSettings { WorkspaceSlotRoles = value };
            _throws = false;
        }
    }

    /// <summary>Every read throws, as after an invalid reload, until <see cref="Roles"/> is set again.</summary>
    public void Invalid() => _throws = true;

    public DelegationSettings CurrentValue => _throws
        ? throw new OptionsValidationException(Options.DefaultName, typeof(DelegationSettings),
            [WorkspaceSlotRoles.SettingName + " is 'plan' (test monitor)."])
        : _value;

    public DelegationSettings Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<DelegationSettings, string?> listener) => null;
}
