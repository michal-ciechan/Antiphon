using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class AnsiStripperTests
{
    private const string ESC = "\x1b";
    private const string BEL = "\x07";
    private const string ST = ESC + "\\";

    [Test]
    public void Removes_color_codes()
    {
        var input = $"{ESC}[38;2;215;119;87mhello{ESC}[m world";
        AnsiStripper.Clean(input).ShouldBe("hello world");
    }

    [Test]
    public void Removes_cursor_moves()
    {
        var input = $"abc{ESC}[5;10Hxyz{ESC}[2J";
        AnsiStripper.Clean(input).ShouldBe("abcxyz");
    }

    [Test]
    public void Removes_OSC_titles_BEL_terminated()
    {
        var input = $"before{ESC}]0;Window Title{BEL}after";
        AnsiStripper.Clean(input).ShouldBe("beforeafter");
    }

    [Test]
    public void Removes_OSC_titles_ST_terminated()
    {
        var input = $"before{ESC}]0;Window Title{ST}after";
        AnsiStripper.Clean(input).ShouldBe("beforeafter");
    }

    [Test]
    public void Removes_DCS_sequences()
    {
        var input = $"a{ESC}P1;2qsome data{ST}b";
        AnsiStripper.Clean(input).ShouldBe("ab");
    }

    [Test]
    public void Removes_single_char_ESC_sequences()
    {
        var input = $"a{ESC}=b{ESC}>c";
        AnsiStripper.Clean(input).ShouldBe("abc");
    }

    [Test]
    public void Passthroughs_plain_text()
    {
        AnsiStripper.Clean("hello world").ShouldBe("hello world");
    }

    [Test]
    public void Handles_null_or_empty()
    {
        AnsiStripper.Clean("").ShouldBe("");
        AnsiStripper.Clean(null).ShouldBeNull();
    }
}

[Category("Unit")]
public class TempBatchTests
{
    [Test]
    public void Writes_and_cleans_up()
    {
        string path;
        using (var bat = new TempBatch("@echo off\r\necho hi\r\n"))
        {
            path = bat.Path;
            File.Exists(path).ShouldBeTrue();
            File.ReadAllText(path).ShouldContain("echo hi");
        }
        File.Exists(path).ShouldBeFalse();
    }

    [Test]
    public void Path_ends_with_bat_extension()
    {
        using var bat = new TempBatch("@echo off\r\n");
        bat.Path.ShouldEndWith(".bat");
    }
}
