using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupCheckpointExtractionTests
{
    [Test]
    public void Naming_policy_and_facades_preserve_admission()
    {
        var f = new HostCleanupCheckpointFixture();
        var facade = new CheckpointOutputCleanup(f.Context);
        (string Value, bool Segment, bool Isolated)[] cases =
        [
            (null!, false, false), ("", false, false), (" ", false, false),
            ("bin-x", true, true), ("bin-a._-9", true, true), ("bin-x\n", true, true),
            ("bin", true, false), ("bin-", true, false), ("BIN-x", true, false),
            ("bin-a..b", false, false), ("bin/a", false, false), ("bin\\a", false, false),
            ("bin:a", false, false), (".", false, false), ("..", false, false),
            ("/bin-x", false, false), ("C:\\bin-x", false, false), ("bin-\u00e9", true, false),
        ];
        foreach (var (value, segment, isolated) in cases)
        {
            var label = "C987.Naming_policy_and_facades_preserve_admission." + (value ?? "null");
            CheckpointOutputNames.IsPathSegment(value).ShouldBe(segment, label + ".shared-segment");
            CheckpointOutputNames.IsIsolatedOutputId(value).ShouldBe(isolated, label + ".shared-isolated");
            facade.IsPathSegment(value).ShouldBe(segment, label + ".instance-segment");
            facade.IsIsolatedOutputId(value).ShouldBe(isolated, label + ".instance-isolated");
            OutputCleanup.IsPathSegment(value).ShouldBe(segment, label + ".static-segment");
            OutputCleanup.IsIsolatedOutputId(value).ShouldBe(isolated, label + ".static-isolated");
        }

        foreach (var valid in new[] { false, true })
        {
            var files = new HostCleanupCheckpointFixture();
            var root = Path.Combine(files.Parent, valid ? "bin-root" : "bin-a..b");
            SafeWrite.Require(files.Write.CreateFreshDirectory(root, files.Write.Under(files.Parent)));
            var payload = Path.Combine(root, "sentinel");
            SafeWrite.Require(files.Write.Text(payload, "keep", files.Write.Under(root)));
            SafeWrite.Require(files.Ledger.Seal(root));
            files.Io.Events.Clear();
            var result = files.Delete.Apply(root, new DeleteOwnership
            {
                IsolatedRoot = files.Parent, CreatedPaths = [root], RequireBinName = true,
            });
            var label = "C987.Naming_policy_and_facades_preserve_admission.root-" + valid;
            files.Io.Count("link", root).ShouldBeGreaterThan(0, label + ".reached");
            result.Disposition.ShouldBe(valid ? DeleteDisposition.Deleted : DeleteDisposition.Refused, label + ".admission");
            if (valid)
                files.Io.Count("unlink", payload).ShouldBe(1, label + ".unlink");
            else
            {
                result.Reason.ShouldBe("not-isolated-output", label + ".refusal");
                files.Io.Count("unlink").ShouldBe(0, label + ".no-unlink");
                files.Io.Text(payload).ShouldBe("keep", label + ".sentinel");
            }

            var stampFiles = new HostCleanupCheckpointFixture();
            var parent = Path.Combine(stampFiles.Parent, valid ? "bin-stamp" : "bin-a..b");
            SafeWrite.Require(stampFiles.Write.CreateFreshDirectory(parent, stampFiles.Write.Under(stampFiles.Parent)));
            var stamp = Path.Combine(parent, "checkpoint-build-source.json");
            SafeWrite.Require(stampFiles.Write.Text(stamp, "stamp-sentinel", stampFiles.Write.Under(parent)));
            stampFiles.Io.Events.Clear();
            var removed = stampFiles.Delete.Apply(stamp, stampFiles.Delete.Stamp(stampFiles.Parent));
            label = "C987.Naming_policy_and_facades_preserve_admission.stamp-" + valid;
            stampFiles.Io.Count("link", stamp).ShouldBeGreaterThan(0, label + ".reached");
            removed.Disposition.ShouldBe(valid ? DeleteDisposition.Deleted : DeleteDisposition.Refused, label + ".admission");
            if (valid)
                stampFiles.Io.Count("unlink", stamp).ShouldBe(1, label + ".unlink");
            else
            {
                removed.Reason.ShouldBe("not-isolated-output", label + ".refusal");
                stampFiles.Io.Count("unlink").ShouldBe(0, label + ".no-unlink");
                stampFiles.Io.Text(stamp).ShouldBe("stamp-sentinel", label + ".sentinel");
            }
        }
    }

    [Test]
    public void File_only_delete_uses_supplied_registry()
    {
        var f = new HostCleanupCheckpointFixture();
        var path = Path.Combine(f.Root, "created.txt");
        SafeWrite.Require(f.Write.Text(path, "owned-sentinel", f.Write.Under(f.Root)));
        SafeWrite.Require(f.Ledger.Seal(f.Root, f.Exit));
        f.Io.Events.Clear();
        var fresh = new CheckpointDelete(f.FreshContext()).DeleteCreatedFile(path);
        fresh.Disposition.ShouldBe(DeleteDisposition.Refused, "C987.File_only_delete_uses_supplied_registry.fresh.refusal");
        fresh.Reason.ShouldContain("current-process creation record missing or changed", Case.Sensitive,
            "C987.File_only_delete_uses_supplied_registry.fresh.match-reached");
        f.Io.Text(path).ShouldBe("owned-sentinel", "C987.File_only_delete_uses_supplied_registry.fresh.sentinel");
        f.Io.Count("unlink").ShouldBe(0, "C987.File_only_delete_uses_supplied_registry.fresh.no-unlink");
        f.Io.Events.Clear();
        var owned = f.Delete.DeleteCreatedFile(path);
        owned.Disposition.ShouldBe(DeleteDisposition.Deleted, "C987.File_only_delete_uses_supplied_registry.owned.removed");
        f.Io.Count("open-read", path).ShouldBe(1, "C987.File_only_delete_uses_supplied_registry.owned.match-reached");
        f.Io.Count("unlink", path).ShouldBe(1, "C987.File_only_delete_uses_supplied_registry.owned.unlink");
        f.Io.FileExists(path).ShouldBeFalse("C987.File_only_delete_uses_supplied_registry.owned.absent");
    }

    [Test]
    public void Moved_types_have_one_library_owner()
    {
        var library = typeof(CheckpointCleanupContext).Assembly;
        var tool = typeof(CheckpointApp).Assembly;
        string[] names =
        [
            "CleanupReceipt", "ToolCopyCleanup", "ExecutorOwnership", "ExecutorOwnershipStore", "ContainedCleanup",
            "RunOwnership", "RunOwnershipStore", "CheckpointRootMarker", "TestRootGuard",
            "ProcessIdentity", "ProcessVerdict", "ProcessObservation", "ProcessIdentityProbe",
            "WriteKind", "WriteOwnership", "SafeWrite", "CheckpointWrite",
            "DeleteDisposition", "DeleteResult", "DeleteOwnership", "SafeDelete", "CheckpointDelete",
            "OwnedOutputLedger", "CheckpointOutputLedger", "ToolCopyDigest", "ToolCopyAuthority", "OutputCustody",
            "ExclusiveDirectory", "CheckpointCleanupDefaults", "CheckpointNativeGitRunner", "LedgerStat",
            "ReceiptFile", "CheckpointReceiptJson", "CheckpointFileReader", "CheckpointProcessRunner",
            "CheckpointSweepOptions", "CheckpointSweepReceipt", "CheckpointTempRootSweep", "CheckpointOutputNames",
        ];
        foreach (var name in names)
        {
            var full = "Antiphon.Checkpoints." + name;
            library.GetTypes().Count(t => t.FullName == full).ShouldBe(1,
                "C987.Moved_types_have_one_library_owner." + name + ".library");
            tool.GetTypes().ShouldNotContain(t => t.FullName == full || t.FullName!.StartsWith(full + "+", StringComparison.Ordinal),
                "C987.Moved_types_have_one_library_owner." + name + ".executable");
        }
        typeof(ExclusiveDirectory).GetMethod("Create", [typeof(string)])!.DeclaringType!.Assembly.ShouldBe(library);
        typeof(ExclusiveDirectory).GetMethod("Create", [typeof(CheckpointCleanupContext), typeof(string)])!
            .DeclaringType!.Assembly.ShouldBe(library);
        typeof(CheckpointTempRootSweep).Assembly.ShouldBe(library);
        Calls(typeof(CheckpointTestScope).GetMethod("TempDir")!, typeof(CheckpointTempRootSweep))
            .ShouldBeTrue("C987.Moved_types_have_one_library_owner.scope.shared-core");
        Calls(typeof(CheckpointTempSweepAssemblyHook).GetMethod("Sweep",
                [typeof(CheckpointCleanupContext), typeof(string), typeof(TextWriter)])!, typeof(CheckpointTempRootSweep))
            .ShouldBeTrue("C987.Moved_types_have_one_library_owner.hook.shared-core");
        library.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName)
            .ShouldContain("Antiphon.Checkpoints", "C987.Moved_types_have_one_library_owner.authority.tool-friend");
        library.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ShouldContain("Antiphon.Tests");
        tool.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ShouldContain("Antiphon.Tests");
        library.GetReferencedAssemblies().ShouldNotContain(a =>
                a.Name == "Antiphon.Checkpoints" || a.Name!.Contains("Tests", StringComparison.Ordinal)
                || a.Name.StartsWith("TUnit", StringComparison.Ordinal),
            "C987.Moved_types_have_one_library_owner.library.no-test-or-tool-dependency");
        var sharedProject = XDocument.Parse(DockerStackDocuments.Read("src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj"));
        Includes(sharedProject, "Compile").ShouldContain("../../scripts/lib/CheckpointProcessRunner.cs");
        var toolProject = XDocument.Parse(DockerStackDocuments.Read("tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj"));
        Includes(toolProject, "Compile").ShouldNotContain("../../scripts/lib/CheckpointProcessRunner.cs");
        var hostProject = XDocument.Parse(DockerStackDocuments.Read("tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj"));
        Includes(hostProject, "ProjectReference").ShouldContain("../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj");
        Includes(hostProject, "Compile").Count().ShouldBe(4);
        Includes(hostProject, "Compile").ShouldBe(new[]
        {
            "../Antiphon.Tests/Checkpoints/CheckpointTestScope.cs",
            "../Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs",
            "../Shared/ReadOnlyDirectory.cs",
            "../Shared/TestTiming.cs",
        }, ignoreOrder: true);
    }

    private static IEnumerable<string> Includes(XDocument project, string name) =>
        project.Descendants(name).Select(e => (string)e.Attribute("Include")!);

    private static bool Calls(MethodInfo method, Type target)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => unchecked((ushort)op.Value));
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var at = 0; at < il.Length;)
        {
            ushort code = il[at++];
            if (code == 0xfe) code = (ushort)(0xfe00 | il[at++]);
            var op = opcodes[code];
            if (op.OperandType == OperandType.InlineMethod
                && method.Module.ResolveMethod(BitConverter.ToInt32(il, at))?.DeclaringType == target) return true;
            at += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                _ => 4,
            };
        }
        return false;
    }
}
