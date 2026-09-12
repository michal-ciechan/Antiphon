using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>CARD-0497 V-24: the shared serializer is the line <c>BuildCommandLine</c> already produces.</summary>
[Category("Unit")]
public sealed class CommandLineLengthTests
{
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    [Test]
    public void Measure_equals_the_line_the_child_parses()
    {
        var exe = @"C:\Program Files\nodejs\node.exe";
        var args = new[]
        {
            @"C:\Users\x\AppData\Roaming\npm\node_modules\@openai\codex\bin\codex.js",
            "--no-alt-screen",
            "-c",
            "developer_instructions=" + CodexInstructionFixtures.Incident,
        };

        var line = WindowsCommandLine.Serialize(exe, args);
        WindowsCommandLine.Measure(exe, args).ShouldBe(line.Length);
        line.ShouldBe(ModernConPtyConnection.BuildCommandLine(exe, args, verbatim: false));

        var argv = RealArgv(line);
        argv[0].ShouldBe(exe);
        for (var i = 0; i < args.Length; i++)
            argv[i + 1].ShouldBe(args[i]);

        "😀".Length.ShouldBe(2);
        WindowsCommandLine.Measure(exe, ["😀"]).ShouldBe(
            ModernConPtyConnection.BuildCommandLine(exe, ["😀"], verbatim: false).Length);
    }

    private static string[] RealArgv(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == nint.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
