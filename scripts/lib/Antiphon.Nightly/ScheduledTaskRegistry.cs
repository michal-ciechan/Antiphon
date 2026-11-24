// The per-user Scheduled Tasks the installers register (unit 36 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md:
// the install-autostart and install-cleanup-task commands replaced scripts/install-autostart.ps1 and
// scripts/install-cleanup-task.ps1, which built them with New-ScheduledTaskAction/-Trigger/-Principal/-SettingsSet and
// Register-ScheduledTask). A ScheduledTaskDefinition is the task as those cmdlets built it; IScheduledTaskRegistry is the
// seam the commands register through, and ScheduledTaskRegistry the production one: the Task Scheduler's COM service
// (Schedule.Service, late bound, the root folder), as AppHostTasks reads and enables the same tasks. Plain C#: no PowerShell
// types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Antiphon.Nightly
{
    /// <summary>A task action: the program, its argument string and its working directory (New-ScheduledTaskAction).</summary>
    public sealed record ScheduledTaskAction(string Execute, string Arguments, string WorkingDirectory);

    /// <summary>The trigger kinds the installers use.</summary>
    public enum ScheduledTaskTriggerKind
    {
        /// <summary>At the user's logon (-AtLogOn -User).</summary>
        Logon,

        /// <summary>Once, at a time (-Once -At).</summary>
        Once,

        /// <summary>Weekly on days, at a time (-Weekly -DaysOfWeek -At).</summary>
        Weekly,
    }

    /// <summary>
    /// A trigger: its kind; the user (logon); the start boundary (once and weekly: UTC, yyyy-MM-ddTHH:mm:ssZ, as the cmdlets
    /// wrote it); the delay after logon; the repetition interval and duration (ISO 8601; StopAtDurationEnd as the cmdlets set
    /// it); the weekly days (Sunday 1, Monday 2, ...) and the week interval.
    /// </summary>
    public sealed record ScheduledTaskTrigger(ScheduledTaskTriggerKind Kind, string UserId = null, string StartBoundary = null, string Delay = null,
        string RepetitionInterval = null, string RepetitionDuration = null, int DaysOfWeek = 0, int WeeksInterval = 0);

    /// <summary>
    /// The settings the installers set (New-ScheduledTaskSettingsSet): batteries, start when available, the execution time limit
    /// (PT0S: none), the restart count and interval (null: unset). Everything else keeps the cmdlet's defaults: IgnoreNew,
    /// Win7 compatibility, the unified scheduling engine.
    /// </summary>
    public sealed record ScheduledTaskSettings(bool AllowStartIfOnBatteries, bool DontStopIfGoingOnBatteries, bool StartWhenAvailable, string ExecutionTimeLimit,
        int RestartCount = 0, string RestartInterval = null);

    /// <summary>
    /// One per-user task: its name (root folder), description, action and triggers, the user it runs as (Interactive, Limited:
    /// in the user's session with the user's PATH and profile, no admin rights) and its settings. Enabled false registers it
    /// disabled (a test's throwaway task never runs).
    /// </summary>
    public sealed record ScheduledTaskDefinition(string Name, string Description, ScheduledTaskAction Action, IReadOnlyList<ScheduledTaskTrigger> Triggers,
        string UserId, ScheduledTaskSettings Settings, bool Enabled = true);

    /// <summary>The Task Scheduler as the installers use it.</summary>
    public interface IScheduledTaskRegistry
    {
        /// <summary>Get-ScheduledTask -TaskName: the task is registered. Throws when the inventory cannot be read.</summary>
        bool Exists(string name);

        /// <summary>Unregister-ScheduledTask: removes the task (stopping a running instance). Throws when it cannot.</summary>
        void Delete(string name);

        /// <summary>Register-ScheduledTask: creates (or replaces) the task. Throws when the Task Scheduler refuses it.</summary>
        void Register(ScheduledTaskDefinition definition);
    }

    /// <summary>The production registry: the Task Scheduler's COM service, root folder, the current user's interactive token.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class ScheduledTaskRegistry : IScheduledTaskRegistry
    {
        private const int NotFound = unchecked((int)0x80070002);
        private const int CreateOrUpdate = 6;
        private const int InteractiveToken = 3;
        private const int LeastPrivilege = 0;
        private const int IgnoreNew = 2;
        private const int Windows7 = 3;
        private const int ExecAction = 0;

        /// <inheritdoc />
        public bool Exists(string name) => Guard("Read " + name, () => Find(Folder(), name) != null);

        /// <inheritdoc />
        public void Delete(string name) => Guard("Delete " + name, () => Invoke(Folder(), "DeleteTask", name, 0));

        /// <inheritdoc />
        public void Register(ScheduledTaskDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Guard("Register " + definition.Name, () =>
            {
                var service = Service();
                var task = Invoke(service, "NewTask", 0);
                var registration = Get(task, "RegistrationInfo");
                Set(registration, "Description", definition.Description ?? "");

                var principal = Get(task, "Principal");
                Set(principal, "UserId", definition.UserId);
                Set(principal, "LogonType", InteractiveToken);
                Set(principal, "RunLevel", LeastPrivilege);

                var settings = Get(task, "Settings");
                var wanted = definition.Settings;
                Set(settings, "Enabled", definition.Enabled);
                Set(settings, "MultipleInstances", IgnoreNew);
                Set(settings, "Compatibility", Windows7);
                Set(settings, "DisallowStartIfOnBatteries", !wanted.AllowStartIfOnBatteries);
                Set(settings, "StopIfGoingOnBatteries", !wanted.DontStopIfGoingOnBatteries);
                Set(settings, "StartWhenAvailable", wanted.StartWhenAvailable);
                Set(settings, "ExecutionTimeLimit", wanted.ExecutionTimeLimit);
                if (wanted.RestartCount > 0) Set(settings, "RestartCount", wanted.RestartCount);
                if (wanted.RestartInterval != null) Set(settings, "RestartInterval", wanted.RestartInterval);
                Set(settings, "UseUnifiedSchedulingEngine", true);

                var triggers = Get(task, "Triggers");
                foreach (var wantedTrigger in definition.Triggers)
                {
                    var trigger = Invoke(triggers, "Create", TriggerType(wantedTrigger.Kind));
                    if (wantedTrigger.UserId != null) Set(trigger, "UserId", wantedTrigger.UserId);
                    if (wantedTrigger.StartBoundary != null) Set(trigger, "StartBoundary", wantedTrigger.StartBoundary);
                    if (wantedTrigger.Delay != null) Set(trigger, "Delay", wantedTrigger.Delay);
                    if (wantedTrigger.Kind == ScheduledTaskTriggerKind.Weekly)
                    {
                        Set(trigger, "DaysOfWeek", (short)wantedTrigger.DaysOfWeek);
                        Set(trigger, "WeeksInterval", (short)wantedTrigger.WeeksInterval);
                    }
                    if (wantedTrigger.RepetitionInterval != null)
                    {
                        var repetition = Get(trigger, "Repetition");
                        Set(repetition, "Interval", wantedTrigger.RepetitionInterval);
                        Set(repetition, "Duration", wantedTrigger.RepetitionDuration ?? "");
                        Set(repetition, "StopAtDurationEnd", true);
                    }
                }

                var action = Invoke(Get(task, "Actions"), "Create", ExecAction);
                Set(action, "Path", definition.Action.Execute);
                Set(action, "Arguments", definition.Action.Arguments ?? "");
                if (!string.IsNullOrEmpty(definition.Action.WorkingDirectory)) Set(action, "WorkingDirectory", definition.Action.WorkingDirectory);

                return Invoke(Invoke(service, "GetFolder", "\\"), "RegisterTaskDefinition", definition.Name, task, CreateOrUpdate, definition.UserId, null, InteractiveToken, null);
            });
        }

        /// <summary>The registered task's XML (a test's read-back of what was registered); null when it does not exist.</summary>
        public static string Xml(string name)
        {
            var task = Find(Folder(), name);
            return task == null ? null : (string)Get(Get(task, "Definition"), "XmlText");
        }

        // A Task Scheduler failure (a COM error through late binding, a denial, an absent service) is one InvalidOperationException
        // naming the step, as AppHostTasks.SetEnabled reports it.
        private static T Guard<T>(string step, Func<T> call)
        {
            try
            {
                return call();
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw new InvalidOperationException(step + ": " + Message(e), e.InnerException);
            }
            catch (Exception e) when (e is COMException || e is FileNotFoundException || e is UnauthorizedAccessException || e is MissingMethodException)
            {
                throw new InvalidOperationException(step + ": " + e.Message, e);
            }
        }

        private static int TriggerType(ScheduledTaskTriggerKind kind) => kind switch
        {
            ScheduledTaskTriggerKind.Once => 1,
            ScheduledTaskTriggerKind.Weekly => 3,
            _ => 9,
        };

        private static object Service()
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true);
            var service = Activator.CreateInstance(type);
            Invoke(service, "Connect");
            return service;
        }

        private static object Folder() => Invoke(Service(), "GetFolder", "\\");

        private static object Find(object folder, string name)
        {
            try { return Invoke(folder, "GetTask", name); }
            catch (TargetInvocationException e) when (e.InnerException != null && e.InnerException.HResult == NotFound) { return null; }
            catch (Exception e) when ((e is COMException || e is FileNotFoundException) && e.HResult == NotFound) { return null; }
        }

        private static object Invoke(object target, string name, params object[] arguments) =>
            target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments, CultureInfo.InvariantCulture);

        private static object Get(object target, string name) =>
            target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);

        private static void Set(object target, string name, object value) =>
            target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }, CultureInfo.InvariantCulture);

        private static string Message(Exception e) => (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e).Message;
    }
}
