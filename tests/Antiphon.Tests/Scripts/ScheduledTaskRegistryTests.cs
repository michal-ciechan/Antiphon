using System.Xml.Linq;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The installers' production Task Scheduler (Antiphon.Nightly.ScheduledTaskRegistry, COM, root folder; unit 36 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): one throwaway task registered DISABLED (it never runs) with
/// every trigger, setting and action shape the installers use, read back as the Task Scheduler stored it, then deleted, as
/// AppHostWatchdogStateObserverScriptTests does with its throwaway task. No Antiphon task is read or changed.
/// </summary>
[Category("Integration")]
public sealed class ScheduledTaskRegistryTests
{
    [Test]
    public void A_disabled_throwaway_task_round_trips_the_installer_shapes()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("the Task Scheduler is a Windows service");
        var registry = new ScheduledTaskRegistry();
        var name = $"Antiphon-U36-install-{Guid.NewGuid():N}"[..40];
        var user = Environment.GetEnvironmentVariable("USERDOMAIN") + "\\" + Environment.GetEnvironmentVariable("USERNAME");
        var directory = Path.GetTempPath().TrimEnd('\\');
        var definition = new ScheduledTaskDefinition(name, "unit 36 throwaway (disabled; never runs)",
            new ScheduledTaskAction("cmd.exe", "/c exit 0", directory),
            [
                new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user, Delay: "PT15M", RepetitionInterval: "PT2M", RepetitionDuration: "P3650D"),
                new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Once, StartBoundary: "2099-01-01T08:00:00Z", RepetitionInterval: "PT2M", RepetitionDuration: "P3650D"),
                new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Weekly, StartBoundary: "2099-01-05T09:00:00Z", DaysOfWeek: 2, WeeksInterval: 1),
            ],
            user, new ScheduledTaskSettings(true, true, true, "PT0S", 3, "PT1M"), Enabled: false);
        registry.Exists(name).ShouldBeFalse();
        try
        {
            registry.Register(definition);
            registry.Exists(name).ShouldBeTrue();
            var xml = XDocument.Parse(ScheduledTaskRegistry.Xml(name).ShouldNotBeNull());
            string Value(string element) => xml.Descendants().First(node => node.Name.LocalName == element).Value;
            IEnumerable<XElement> All(string element) => xml.Descendants().Where(node => node.Name.LocalName == element);

            xml.Root!.Attribute("version")!.Value.ShouldBe("1.3", "Win7 compatibility, as New-ScheduledTaskSettingsSet set it");
            Value("Description").ShouldBe("unit 36 throwaway (disabled; never runs)");
            var settings = xml.Descendants().First(node => node.Name.LocalName == "Settings");
            settings.Elements().First(node => node.Name.LocalName == "Enabled").Value.ShouldBe("false");
            (Value("DisallowStartIfOnBatteries"), Value("StopIfGoingOnBatteries"), Value("StartWhenAvailable"), Value("MultipleInstancesPolicy"))
                .ShouldBe(("false", "false", "true", "IgnoreNew"));
            settings.Elements().First(node => node.Name.LocalName == "ExecutionTimeLimit").Value.ShouldBe("PT0S");
            var restart = All("RestartOnFailure").Single();
            (restart.Elements().First(node => node.Name.LocalName == "Interval").Value, restart.Elements().First(node => node.Name.LocalName == "Count").Value)
                .ShouldBe(("PT1M", "3"));
            (Value("LogonType"), Value("RunLevel")).ShouldBe(("InteractiveToken", "LeastPrivilege"));

            var logon = All("LogonTrigger").Single();
            logon.Descendants().First(node => node.Name.LocalName == "Delay").Value.ShouldBe("PT15M");
            logon.Descendants().First(node => node.Name.LocalName == "UserId").Value.ShouldNotBeNullOrWhiteSpace();
            var once = All("TimeTrigger").Single();
            // The Task Scheduler stores the UTC boundary with its offset spelled out (+00:00).
            DateTimeOffset.Parse(once.Descendants().First(node => node.Name.LocalName == "StartBoundary").Value, System.Globalization.CultureInfo.InvariantCulture)
                .ShouldBe(new DateTimeOffset(2099, 1, 1, 8, 0, 0, TimeSpan.Zero));
            foreach (var repeating in new[] { logon, once })
            {
                var repetition = repeating.Elements().Single(node => node.Name.LocalName == "Repetition");
                repetition.Elements().Select(node => node.Name.LocalName + "=" + node.Value).ShouldBe(["Interval=PT2M", "Duration=P3650D", "StopAtDurationEnd=true"]);
            }
            var weekly = All("CalendarTrigger").Single();
            weekly.Descendants().First(node => node.Name.LocalName == "WeeksInterval").Value.ShouldBe("1");
            weekly.Descendants().First(node => node.Name.LocalName == "DaysOfWeek").Elements().Select(node => node.Name.LocalName).ShouldBe(["Monday"]);

            var exec = All("Exec").Single();
            exec.Elements().Select(node => node.Name.LocalName + "=" + node.Value).ShouldBe(["Command=cmd.exe", "Arguments=/c exit 0", "WorkingDirectory=" + directory]);

            // Register replaces a task of the same name (the installers delete first, which also stops a running instance).
            registry.Register(definition with { Description = "replaced" });
            XDocument.Parse(ScheduledTaskRegistry.Xml(name)!).Descendants().First(node => node.Name.LocalName == "Description").Value.ShouldBe("replaced");
        }
        finally
        {
            if (registry.Exists(name)) registry.Delete(name);
        }
        registry.Exists(name).ShouldBeFalse();
        ScheduledTaskRegistry.Xml(name).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => registry.Delete(name));
    }
}
