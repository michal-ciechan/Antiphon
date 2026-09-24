using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class AgentPinPathTests
{
    [Test]
    public void V01_relative_path_uses_full_agent_id_hex()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        AgentPinPaths.AgentIdHex(id).ShouldBe("aaaaaaaabbbbccccddddeeeeeeeeeeee");
        AgentPinPaths.RelativePath(id).ShouldBe(@".antiphon\pins\aaaaaaaabbbbccccddddeeeeeeeeeeee\antiphon.md");
    }

    [Test]
    public void V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash()
    {
        // CARD-0681: the canonical cwd is the Windows projection key (backslashes, drive roots);
        // off Windows Path.GetFullPath treats "D:/src/work/" as relative to the process cwd.
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("Canonical pin cwd is a Windows drive-rooted path");
        var canonical = AgentPinPaths.CanonicalCwd(@"D:/src/work/");
        canonical.ShouldBe(@"D:\src\work");
        AgentPinPaths.CanonicalHost(" Local ").ShouldBe("local");
    }

    [Test]
    public void V02_text_normalizes_crlf_and_rejects_empty_and_controls()
    {
        AgentPinnedInstructionText.NormalizeAndValidate("  a\r\nb  ").ShouldBe("a\nb");
        Should.Throw<ValidationException>(() => AgentPinnedInstructionText.NormalizeAndValidate(""));
        Should.Throw<ValidationException>(() => AgentPinnedInstructionText.NormalizeAndValidate("\0x"));
        Should.Throw<ValidationException>(() => AgentPinnedInstructionText.NormalizeAndValidate("\x1bx"));
        Should.Throw<ValidationException>(() => AgentPinnedInstructionText.NormalizeAndValidate(new string('x', AgentPinnedInstruction.MaxTextLength + 1)));
        AgentPinnedInstructionText.NormalizeAndValidate(new string('x', AgentPinnedInstruction.MaxTextLength)).Length
            .ShouldBe(AgentPinnedInstruction.MaxTextLength);
    }

    [Test]
    public void V02_snapshot_hash_is_stable_for_id_and_text_order()
    {
        var a = new AgentPinnedInstruction { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Text = "one", CreatedAt = DateTime.UnixEpoch };
        var b = new AgentPinnedInstruction { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Text = "two", CreatedAt = DateTime.UnixEpoch.AddSeconds(1) };
        var hash = AgentPinSnapshotHasher.HashActive([b, a]);
        hash.ShouldBe(AgentPinSnapshotHasher.HashActive([a, b]));
        hash.Length.ShouldBe(64);
        a.SourceRef = "changed";
        a.SourceKey = "k";
        AgentPinSnapshotHasher.HashActive([a, b]).ShouldBe(hash);
    }
}
