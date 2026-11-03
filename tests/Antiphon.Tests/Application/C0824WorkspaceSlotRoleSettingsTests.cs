using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S1c CP-21 (ST). The <c>Delegation:WorkspaceSlotRoles</c> grammar (T-D-1), the configuration-shape check
/// and the one registration path (T-D-2, S-2), and the role gate that reads the options monitor at every decision and
/// fails closed on an invalid value or reload (T-D-3). Binding cases run real JSON files through
/// <see cref="DelegationSettingsRegistration.AddDelegationSettings"/>, the registration Program uses.
/// </summary>
[Category("Unit")]
// The reload cases poll for up to 10 s (the 250 ms reload debounce plus inotify latency); measured at 2561829864 the
// slowest case is 1.5 s and the rest are under 0.6 s. The budget covers the poll bound, so a slow reload fails with its
// own message rather than the timeout.
[Timeout(15_000)]
public sealed class C0824WorkspaceSlotRoleSettingsTests
{
    private static readonly AgentTaskRole[] AllRoles = Enum.GetValues<AgentTaskRole>();

    /// <summary>The canonical names a refusal must list: every role name except Mutation, in declaration order.</summary>
    private static readonly string CanonicalList =
        string.Join(", ", Enum.GetNames<AgentTaskRole>().Where(name => name != "Mutation"));

    [Test]
    public void Default_admits_every_role_except_mutation()
    {
        // V-1 (G-1). "*" is the default, and it is S1's every role minus Mutation; bound from an empty section too.
        new DelegationSettings().WorkspaceSlotRoles.ShouldBe("*");
        var expected = AllRoles.Where(role => role != AgentTaskRole.Mutation).ToHashSet();

        AssertAll(Gate(new MutableDelegationMonitor("*")), expected);
        using var bound = Bound.Of("""{"Delegation":{}}""", reload: false);
        bound.Settings.WorkspaceSlotRoles.ShouldBe("*");
        AssertAll(Gate(bound.Monitor), expected);

        static void AssertAll(WorkspaceSlotRoleGate gate, HashSet<AgentTaskRole> expected)
        {
            Admitted(gate).ShouldBe(expected, ignoreOrder: true);
            foreach (var role in AllRoles)
                gate.Evaluate(role, null).ShouldBe(role == AgentTaskRole.Mutation
                    ? new WorkspaceSlotRoleVerdict(false, "mutation")
                    : new WorkspaceSlotRoleVerdict(true, "all"));
        }
    }

    [Test]
    public void None_admits_no_role()
    {
        // V-2 (G-2). The soft rollback: no role leases, and each says why.
        WorkspaceSlotRoles.TryParse("none", out var set, out var error).ShouldBeTrue(error);
        var gate = Gate(new MutableDelegationMonitor("none"));
        foreach (var role in AllRoles)
        {
            set.Admits(role, null).ShouldBe(new WorkspaceSlotRoleVerdict(false, "roles_none"), role.ToString());
            gate.Evaluate(role, null).ShouldBe(new WorkspaceSlotRoleVerdict(false, "roles_none"), role.ToString());
        }
    }

    [Test]
    [Arguments("Plan", "Plan")]
    [Arguments("Plan,Investigate,TestDesign,Docs", "Plan,Investigate,TestDesign,Docs")]
    [Arguments("Code,Code", "Code")]
    public void Listed_admits_exactly_the_listed_roles(string value, string expected)
    {
        // V-3 (G-3). A list admits exactly its names; every other role is role_not_listed (Mutation says mutation).
        WorkspaceSlotRoles.TryParse(value, out var set, out var error).ShouldBeTrue(error);
        new DelegationSettingsValidator().Validate(null, new DelegationSettings { WorkspaceSlotRoles = value })
            .Failed.ShouldBeFalse();
        var listed = expected.Split(',').Select(Enum.Parse<AgentTaskRole>).ToHashSet();
        var gate = Gate(new MutableDelegationMonitor(value));
        Admitted(gate).ShouldBe(listed, ignoreOrder: true);
        foreach (var role in AllRoles)
        {
            var verdict = listed.Contains(role) ? new WorkspaceSlotRoleVerdict(true, "listed")
                : role == AgentTaskRole.Mutation ? new WorkspaceSlotRoleVerdict(false, "mutation")
                : new WorkspaceSlotRoleVerdict(false, "role_not_listed");
            set.Admits(role, null).ShouldBe(verdict, role.ToString());
            gate.Evaluate(role, null).ShouldBe(verdict, role.ToString());
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("Plan,,Docs")]
    [Arguments("Plan,")]
    [Arguments("plan")]
    [Arguments("None")]
    [Arguments(" Plan")]
    [Arguments("Plan ")]
    [Arguments("Plan, Docs")]
    [Arguments("1")]
    [Arguments("Planner")]
    [Arguments("all")]
    [Arguments("Mutation")]
    [Arguments("*,Plan")]
    [Arguments("none,Plan")]
    public void Invalid_values_are_refused(string value)
    {
        // V-4 (G-7..G-11). Each value breaks exactly one T-D-1 rule; the validator names the key and the canonical list.
        var result = new DelegationSettingsValidator().Validate(null, new DelegationSettings { WorkspaceSlotRoles = value });
        result.Failed.ShouldBeTrue($"'{value}' must be refused");
        var failure = result.Failures!.ShouldHaveSingleItem();
        failure.ShouldContain("Delegation:WorkspaceSlotRoles");
        failure.ShouldContain(CanonicalList);
        WorkspaceSlotRoles.TryParse(value, out _, out var error).ShouldBeFalse($"'{value}' must not parse");
        error.ShouldContain("Delegation:WorkspaceSlotRoles");
        Gate(new MutableDelegationMonitor(value)).Evaluate(AgentTaskRole.Plan, null)
            .ShouldBe(new WorkspaceSlotRoleVerdict(false, "roles_invalid"), "an invalid value leases nothing");
    }

    [Test]
    [Arguments("absent", "*")]
    [Arguments("\"none\"", "none")]
    [Arguments("\"Plan,Docs\"", "Plan,Docs")]
    [Arguments("[]", "refused")]
    [Arguments("[\"Plan\"]", "refused")]
    [Arguments("null", "refused")]
    [Arguments("\"\"", "refused")]
    public void Bound_through_the_production_registration(string json, string outcome)
    {
        // V-5 (G-9, G-12, G-13). Real JSON through S-2. An array binds to the default "*" unless the shape check refuses
        // it (measured), so the soft rollback can never silently mean every role.
        var section = json == "absent" ? "{}" : "{\"WorkspaceSlotRoles\":" + json + "}";
        using var bound = Bound.Of("{\"Delegation\":" + section + "}", reload: false);
        if (outcome != "refused")
        {
            bound.Settings.WorkspaceSlotRoles.ShouldBe(outcome);
            return;
        }

        var refused = Should.Throw<OptionsValidationException>(() => _ = bound.Settings);
        var failures = refused.Failures.Where(text => text.Contains("Delegation:WorkspaceSlotRoles", StringComparison.Ordinal)).ToList();
        var all = string.Join(" | ", refused.Failures);
        if (json.StartsWith('['))
        {
            // The shape check must refuse an array itself. Measured here (net9.0): [] binds to "" (the grammar refuses that
            // too) and ["Plan"] binds to the default "*", which only the shape check can refuse.
            failures.Count(text => text.Contains("single string", StringComparison.Ordinal)).ShouldBe(1, all);
        }
        else
        {
            failures.ShouldHaveSingleItem(all).ShouldContain(CanonicalList, Case.Sensitive, "the grammar refuses an empty value");
        }
    }

    [Test]
    public void Layered_files_replace_the_whole_value()
    {
        // V-6 / R-3. A later file replaces the whole string; a string[] would merge by index and keep "Code".
        foreach (var (over, expected) in new[] { ("none", "none"), ("Docs", "Docs") })
        {
            using var bound = Bound.Of("""{"Delegation":{"WorkspaceSlotRoles":"Plan,Code"}}""", reload: false,
                overlay: "{\"Delegation\":{\"WorkspaceSlotRoles\":\"" + over + "\"}}");
            bound.Settings.WorkspaceSlotRoles.ShouldBe(expected);
            bound.Monitor.CurrentValue.WorkspaceSlotRoles.ShouldBe(expected);
        }
    }

    [Test]
    public async Task Invalid_reload_fails_closed_then_recovers()
    {
        // V-7 (G-15, P-C). A real reloading file: an invalid edit makes the monitor throw; the gate leases nothing,
        // warns once and never throws; fixing the file recovers without a new gate.
        using var bound = Bound.Of(Roles("Plan"), reload: true);
        var logger = new RecordingLogger<WorkspaceSlotRoleGate>();
        var gate = new WorkspaceSlotRoleGate(bound.Monitor, logger);
        gate.Evaluate(AgentTaskRole.Plan, null).ShouldBe(new WorkspaceSlotRoleVerdict(true, "listed"));

        bound.Write(Roles("plan"));
        var invalid = await PollAsync(gate, "roles_invalid");
        invalid.Lease.ShouldBeFalse();
        Should.NotThrow(() => gate.Evaluate(AgentTaskRole.Plan, null));
        logger.Entries.Count(entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("Delegation:WorkspaceSlotRoles", StringComparison.Ordinal))
            .ShouldBe(1, string.Join(" | ", logger.Entries.Select(entry => entry.Level + ": " + entry.Message)));

        bound.Write(Roles("none"));
        (await PollAsync(gate, "roles_none")).Lease.ShouldBeFalse();
        bound.Write(Roles("Plan"));
        (await PollAsync(gate, "listed")).Lease.ShouldBeTrue();
    }

    [Test]
    public void Gate_reads_the_monitor_at_each_call()
    {
        // V-8 (G-14). One gate instance: a changed value applies to the next call.
        var monitor = new MutableDelegationMonitor("Plan");
        var gate = Gate(monitor);
        gate.Evaluate(AgentTaskRole.Plan, null).Lease.ShouldBeTrue();
        monitor.Roles = "Docs";
        gate.Evaluate(AgentTaskRole.Plan, null).ShouldBe(new WorkspaceSlotRoleVerdict(false, "role_not_listed"));
        gate.Evaluate(AgentTaskRole.Docs, null).Lease.ShouldBeTrue();
    }

    [Test]
    [Arguments("*")]
    [Arguments("Plan,Docs")]
    public void Mutation_and_source_landing_are_never_admitted(string value)
    {
        // V-9 (G-4, G-5). Mutation never leases, and a SourceLanding task never leases even when its role is admitted.
        var gate = Gate(new MutableDelegationMonitor(value));
        gate.Evaluate(AgentTaskRole.Plan, null).Lease.ShouldBeTrue("control: Plan is admitted");
        gate.Evaluate(AgentTaskRole.Mutation, null).ShouldBe(new WorkspaceSlotRoleVerdict(false, "mutation"));
        gate.Evaluate(AgentTaskRole.Plan, Guid.NewGuid()).ShouldBe(new WorkspaceSlotRoleVerdict(false, "source_landing"));
    }

    private static string Roles(string value) => "{\"Delegation\":{\"WorkspaceSlotRoles\":\"" + value + "\"}}";

    private static WorkspaceSlotRoleGate Gate(IOptionsMonitor<DelegationSettings> monitor) =>
        new(monitor, new RecordingLogger<WorkspaceSlotRoleGate>());

    private static HashSet<AgentTaskRole> Admitted(WorkspaceSlotRoleGate gate) =>
        AllRoles.Where(role => gate.Evaluate(role, null).Lease).ToHashSet();

    private static async Task<WorkspaceSlotRoleVerdict> PollAsync(WorkspaceSlotRoleGate gate, string reason)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var last = gate.Evaluate(AgentTaskRole.Plan, null);
        while (last.Reason != reason && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            last = gate.Evaluate(AgentTaskRole.Plan, null);
        }

        last.Reason.ShouldBe(reason, "the reload did not reach the gate within 10 s");
        return last;
    }

    /// <summary>A temp JSON file (and optional overlay) bound through S-2 into a fresh provider.</summary>
    internal sealed class Bound : IDisposable
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly ConfigurationRoot _configuration;
        private readonly ServiceProvider _provider;

        private Bound(string directory, string path, ConfigurationRoot configuration, ServiceProvider provider)
        {
            _directory = directory;
            _path = path;
            _configuration = configuration;
            _provider = provider;
        }

        public DelegationSettings Settings => _provider.GetRequiredService<IOptions<DelegationSettings>>().Value;
        public IOptionsMonitor<DelegationSettings> Monitor => _provider.GetRequiredService<IOptionsMonitor<DelegationSettings>>();

        public static Bound Of(string json, bool reload, string? overlay = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "c0824-s1c-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "appsettings.json");
            File.WriteAllText(path, json);
            var builder = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: reload);
            if (overlay is not null)
            {
                var second = Path.Combine(directory, "appsettings.Development.json");
                File.WriteAllText(second, overlay);
                builder.AddJsonFile(second, optional: false, reloadOnChange: reload);
            }

            var configuration = (ConfigurationRoot)builder.Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDelegationSettings(configuration);
            return new Bound(directory, path, configuration, services.BuildServiceProvider());
        }

        /// <summary>Replaces the base file in one rename, so a reload never reads a half-written file.</summary>
        public void Write(string json)
        {
            var staged = Path.Combine(_directory, "staged-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(staged, json);
            File.Move(staged, _path, overwrite: true);
        }

        public void Dispose()
        {
            _provider.Dispose();
            _configuration.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
