using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

[Trait("Category", "Unit")]
public class AnsiStripperTests
{
    private const string ESC = "";
    private const string BEL = "";
    private const string ST = ESC + "\\";

    [Fact]
    public void Removes_color_codes()
    {
        var input = $"{ESC}[38;2;215;119;87mhello{ESC}[m world";
        AnsiStripper.Clean(input).Should().Be("hello world");
    }

    [Fact]
    public void Removes_cursor_moves()
    {
        var input = $"abc{ESC}[5;10Hxyz{ESC}[2J";
        AnsiStripper.Clean(input).Should().Be("abcxyz");
    }

    [Fact]
    public void Removes_OSC_titles_BEL_terminated()
    {
        var input = $"before{ESC}]0;Window Title{BEL}after";
        AnsiStripper.Clean(input).Should().Be("beforeafter");
    }

    [Fact]
    public void Removes_OSC_titles_ST_terminated()
    {
        var input = $"before{ESC}]0;Window Title{ST}after";
        AnsiStripper.Clean(input).Should().Be("beforeafter");
    }

    [Fact]
    public void Removes_DCS_sequences()
    {
        var input = $"a{ESC}P1;2qsome data{ST}b";
        AnsiStripper.Clean(input).Should().Be("ab");
    }

    [Fact]
    public void Removes_single_char_ESC_sequences()
    {
        var input = $"a{ESC}=b{ESC}>c";
        AnsiStripper.Clean(input).Should().Be("abc");
    }

    [Fact]
    public void Passthroughs_plain_text()
    {
        AnsiStripper.Clean("hello world").Should().Be("hello world");
    }

    [Fact]
    public void Handles_null_or_empty()
    {
        AnsiStripper.Clean("").Should().Be("");
        AnsiStripper.Clean(null).Should().BeNull();
    }
}

[Trait("Category", "Unit")]
public class TempBatchTests
{
    [Fact]
    public void Writes_and_cleans_up()
    {
        string path;
        using (var bat = new TempBatch("@echo off\r\necho hi\r\n"))
        {
            path = bat.Path;
            File.Exists(path).Should().BeTrue();
            File.ReadAllText(path).Should().Contain("echo hi");
        }
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Path_ends_with_bat_extension()
    {
        using var bat = new TempBatch("@echo off\r\n");
        bat.Path.Should().EndWith(".bat");
    }
}
