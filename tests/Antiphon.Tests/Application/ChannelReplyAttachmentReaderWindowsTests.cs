using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelReplyAttachmentReaderWindowsTests
{
    [Test]
    public async Task C1061_Preexisting_junction_is_refused_before_open()
    {
        using var scratch = new NativeJunctionScratch();
        var allowed = Directory.CreateDirectory(Path.Combine(scratch.Root, "allowed")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(scratch.Root, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "source.txt"), "outside bytes");
        var ordinary = Path.Combine(allowed, "ordinary.txt");
        await File.WriteAllTextAsync(ordinary, "trusted bytes");
        var linkPath = Path.Combine(allowed, "junction");
        using var link = await scratch.CreateAsync(linkPath, outside);
        link.ShouldNotBeNull();
        File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
        foreach (var root in new[] { allowed, linkPath })
        foreach (var text in new[] { false, true })
        {
            var hooks = 0;
            var reader = new ChannelReplyAttachmentReader { BeforeOpenAsync = (_, _) => { hooks++; return Task.CompletedTask; } };
            var error = await CaptureAsync(async () =>
            {
                if (text) await reader.ReadTextAsync(Path.Combine(linkPath, "source.txt"), [root], 256, default);
                else await reader.ReadAttachmentAsync(Path.Combine(linkPath, "source.txt"), [root], 256, default);
            });
            hooks.ShouldBe(0, "Junction.PreflightHookZero");
            error.ShouldBeOfType<InvalidDataException>().Message.ShouldBe("Linked paths and devices are not source files.");
            if (text) (await reader.ReadTextAsync(ordinary, [allowed], 256, default)).ShouldBe("trusted bytes");
            else (await reader.ReadAttachmentAsync(ordinary, [allowed], 256, default)).ShouldBe("trusted bytes"u8.ToArray());
            hooks.ShouldBe(1);
        }
    }

    [Test]
    public async Task C1061_Junction_after_validation_is_refused_by_native_open()
    {
        foreach (var rootIsReplaced in new[] { false, true })
        foreach (var text in new[] { false, true })
        {
            using var scratch = new NativeJunctionScratch();
            var allowed = Directory.CreateDirectory(Path.Combine(scratch.Root, "allowed")).FullName;
            var parent = Directory.CreateDirectory(Path.Combine(allowed, "parent")).FullName;
            var saved = Path.Combine(allowed, "saved");
            var outside = Directory.CreateDirectory(Path.Combine(scratch.Root, "outside")).FullName;
            await File.WriteAllTextAsync(Path.Combine(parent, "source.txt"), "trusted bytes");
            await File.WriteAllTextAsync(Path.Combine(outside, "source.txt"), "outside bytes");
            var hooks = 0;
            var installed = false;
            var renamed = false;
            DirectoryLink? link = null;
            byte[]? escaped = null;
            var reader = new ChannelReplyAttachmentReader
            {
                BeforeOpenAsync = async (_, _) =>
                {
                    hooks++;
                    Directory.Move(parent, saved);
                    renamed = true;
                    link = await scratch.CreateAsync(parent, outside);
                    link.ShouldNotBeNull();
                    installed = File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint);
                },
            };
            try
            {
                var error = await CaptureAsync(async () =>
                {
                    if (text) escaped = System.Text.Encoding.UTF8.GetBytes(await reader.ReadTextAsync(
                        Path.Combine(parent, "source.txt"), [rootIsReplaced ? parent : allowed], 256, default));
                    else escaped = await reader.ReadAttachmentAsync(Path.Combine(parent, "source.txt"),
                        [rootIsReplaced ? parent : allowed], 256, default);
                });
                hooks.ShouldBe(1);
                installed.ShouldBeTrue("Junction.NativeRefused: setup must install a real junction");
                error.ShouldBeOfType<InvalidDataException>("Junction.NativeRefused")
                    .Message.ShouldBe("A source directory is linked or invalid.", "Junction.NativeRefused");
                escaped.ShouldBeNull("Junction.NativeRefused: no bytes escaped");
            }
            finally
            {
                if (scratch.CleanupConfirmed && renamed)
                {
                    link?.Dispose();
                    scratch.RemoveCreatedLink(parent);
                    Directory.Move(saved, parent);
                    File.ReadAllText(Path.Combine(parent, "source.txt")).ShouldBe("trusted bytes");
                }
            }
        }
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }
}
