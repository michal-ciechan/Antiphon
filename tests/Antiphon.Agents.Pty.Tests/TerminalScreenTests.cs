using Antiphon.Agents.Pty;
using FluentAssertions;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Unit tests for <see cref="TerminalScreen"/>.
///
/// Key focus: cursor-forward optimisation (\x1b[1C).  When the terminal repaints
/// already-correct characters it skips them with cursor-forward instead of writing
/// the character again.  TerminalScreen must keep the character that was written in
/// the initial paint — producing the correct visible text.
/// </summary>
public class TerminalScreenTests
{
	// ── Helpers ─────────────────────────────────────────────────────────────────

	private static TerminalScreen S(int cols = 40, int rows = 10) => new(cols, rows);

	/// <summary>CSI escape prefix.</summary>
	private static string Csi(string seq) => $"\x1b[{seq}";

	/// <summary>Cursor forward n columns.</summary>
	private static string CursorForward(int n = 1) => Csi($"{n}C");

	/// <summary>Cursor position (1-based row, col).</summary>
	private static string CursorPos(int row, int col) => Csi($"{row};{col}H");

	/// <summary>Erase to end of line.</summary>
	private const string EraseEol = "\x1b[K";

	/// <summary>Erase entire line.</summary>
	private const string EraseLine = "\x1b[2K";

	// ── Plain text ──────────────────────────────────────────────────────────────

	[Fact]
	public void Plain_text_lands_on_row_0()
	{
		var s = S();
		s.Feed("Hello");
		s.GetRow(0).Should().Be("Hello");
	}

	[Fact]
	public void Carriage_return_moves_to_column_0()
	{
		var s = S();
		s.Feed("ABC\rXY");
		// "XY" overwrites "AB", leaving "XYC"
		s.GetRow(0).Should().Be("XYC");
	}

	[Fact]
	public void Newline_advances_row_without_resetting_column()
	{
		// VT100: \n = line-feed only (no carriage return). Column is NOT reset.
		var s = S();
		s.Feed("AB\nCD"); // after "AB", cursor is at col 2; \n moves to row 1 col 2; "CD" lands there
		s.GetRow(0).Should().Be("AB");
		s.GetRow(1).Should().Be("  CD");
	}

	[Fact]
	public void CRLF_advances_to_next_row_at_column_0()
	{
		var s = S();
		s.Feed("Line1\r\nLine2");
		s.GetRow(0).Should().Be("Line1");
		s.GetRow(1).Should().Be("Line2");
	}

	[Fact]
	public void CRLF_writes_on_next_row_starting_at_column_0()
	{
		var s = S();
		s.Feed("First\r\nSecond");
		s.GetRow(0).Should().Be("First");
		s.GetRow(1).Should().Be("Second");
	}

	// ── Cursor-forward optimisation ─────────────────────────────────────────────

	[Fact]
	public void CursorForward_preserves_existing_character()
	{
		var s = S();
		// First paint: write "Hello"
		s.Feed("Hello");
		// Repaint: move to col 0, write "H", skip "e" (already correct), write "llo"
		s.Feed(CursorPos(1, 1) + "H" + CursorForward(1) + "llo");
		s.GetRow(0).Should().Be("Hello", "cursor-forward must keep the 'e' from the initial paint");
	}

	[Fact]
	public void CursorForward_n_skips_n_characters()
	{
		var s = S();
		s.Feed("ABCDE");
		// Move to start, write "A", skip 3, write "E"
		s.Feed(CursorPos(1, 1) + "A" + CursorForward(3) + "E");
		s.GetRow(0).Should().Be("ABCDE");
	}

	[Fact]
	public void CursorForward_then_overwrite_changes_character()
	{
		// Write "H" at col 0, forward 1 (skip 'e' at col 1), write 'X' at col 2 (was 'l').
		var s = S();
		s.Feed("Hello");
		s.Feed(CursorPos(1, 1) + "H" + CursorForward(1) + "X");
		s.GetRow(0).Should().Be("HeXlo");
	}

	[Fact]
	public void Multiple_repaints_with_cursor_forward_remain_consistent()
	{
		var s = S();
		s.Feed("Do you want to proceed?");

		// Simulated repaint where some chars are cursor-optimised:
		// "Do you w" written, "a" skipped, "nt to proceed?" skipped partially
		s.Feed(CursorPos(1, 1)
		       + "Do you w"           // write 8 chars
		       + CursorForward(1)     // skip 'a' (already correct)
		       + "nt to proceed?");   // write remaining
		s.GetRow(0).Should().Be("Do you want to proceed?");
	}

	[Fact]
	public void Real_world_permission_prompt_text_survives_repaint()
	{
		// Simulate the exact pattern seen in Claude TUI:
		// First paint writes the full text.
		// Repaint uses cursor-forward for already-correct characters.
		var s = S(cols: 80, rows: 5);

		// Initial paint
		s.Feed("Do you want to proceed?\r\n1. Yes\r\n2. No");

		// Repaint row 0: "Do " written, then cursor-forward for "you ", "want ", "to "
		s.Feed(CursorPos(1, 1) + "Do " + CursorForward(4) + CursorForward(5) + CursorForward(3) + "proceed?");

		s.GetRow(0).Should().Be("Do you want to proceed?");
		s.GetRow(1).Should().Be("1. Yes");
		s.GetRow(2).Should().Be("2. No");
	}

	// ── Cursor movement ─────────────────────────────────────────────────────────

	[Fact]
	public void Cursor_position_absolute()
	{
		var s = S();
		s.Feed(CursorPos(3, 5) + "Hi");
		s.GetRow(2).Should().StartWith("    Hi"); // row 3, col 5 (1-based) = row 2, col 4 (0-based)
	}

	[Fact]
	public void Cursor_up_moves_cursor_row()
	{
		var s = S();
		s.Feed("Row0\r\nRow1\r\nRow2");
		s.Feed(Csi("2A") + "X"); // up 2 rows, overwrite at row 0 col 4
		s.GetRow(0).Should().Be("Row0X"); // 'X' written at col 4, row 0
		// Actually: after "Row0\r\nRow1\r\nRow2", cursor is at row 2 col 4.
		// Up 2 → row 0 col 4. Write 'X' at (0,4).
		// "Row0" is 4 chars (0-3), X at col 4 → "Row0X"
	}

	[Fact]
	public void Cursor_horizontal_absolute()
	{
		var s = S();
		s.Feed("Hello World");
		s.Feed(Csi("7G") + "!");   // move to column 7 (1-based), overwrite 'W' with '!'
		s.GetRow(0).Should().Be("Hello !orld");
	}

	// ── Erase sequences ─────────────────────────────────────────────────────────

	[Fact]
	public void Erase_to_end_of_line_clears_rest_of_row()
	{
		var s = S();
		s.Feed("Hello World");
		s.Feed(CursorPos(1, 6) + EraseEol); // cursor to col 6 (1-based), erase to end
		s.GetRow(0).Should().Be("Hello");
	}

	[Fact]
	public void Erase_entire_line_clears_row()
	{
		var s = S();
		s.Feed("Hello World\r\nLine2");
		s.Feed(CursorPos(1, 1) + EraseLine);
		s.GetRow(0).Should().Be("", "entire line should be blank");
		s.GetRow(1).Should().Be("Line2", "other rows unaffected");
	}

	[Fact]
	public void Clear_screen_blanks_all_rows()
	{
		var s = S();
		s.Feed("Row0\r\nRow1\r\nRow2");
		s.Feed(Csi("2J")); // erase display
		s.GetRow(0).Should().Be("");
		s.GetRow(1).Should().Be("");
		s.GetRow(2).Should().Be("");
	}

	// ── Scrolling ───────────────────────────────────────────────────────────────

	[Fact]
	public void Newline_at_bottom_scrolls_content_up()
	{
		var s = S(cols: 10, rows: 3);
		s.Feed("A\r\nB\r\nC\r\n"); // third newline causes scroll
		s.GetRow(0).Should().Be("B");
		s.GetRow(1).Should().Be("C");
		s.GetRow(2).Should().Be(""); // new blank line
	}

	[Fact]
	public void Scroll_region_scrolls_only_within_bounds()
	{
		// Set scroll region rows 2-4 (1-based). Fill the region with content,
		// then cause a scroll and verify only region rows shift.
		var s = S(cols: 10, rows: 5);
		// Write known content on ALL rows using absolute cursor positions.
		s.Feed(CursorPos(1, 1) + "R0");
		s.Feed(CursorPos(2, 1) + "R1");
		s.Feed(CursorPos(3, 1) + "R2");
		s.Feed(CursorPos(4, 1) + "R3");
		s.Feed(CursorPos(5, 1) + "R4");
		// Set scroll region to rows 3-5 (1-based) → rows 2-4 (0-based).
		s.Feed(Csi("3;5r"));
		// Cursor is now at (0,0) per DECSTBM spec — move back to bottom of region.
		s.Feed(CursorPos(5, 1) + "\n"); // \n at bottom of region → scroll within region
		// Rows 3-4 (0-based 2-3) shift up; row 4 (0-based) cleared.
		s.GetRow(0).Should().Be("R0", "row 0: outside scroll region, unchanged");
		s.GetRow(1).Should().Be("R1", "row 1: outside scroll region, unchanged");
		s.GetRow(2).Should().Be("R3", "row 2: was R2, scrolled up to show R3");
		s.GetRow(3).Should().Be("R4", "row 3: was R3, scrolled up to show R4");
		s.GetRow(4).Should().Be("", "row 4: new blank line from scroll");
	}

	// ── Delete / insert ─────────────────────────────────────────────────────────

	[Fact]
	public void Delete_characters_shifts_text_left()
	{
		var s = S();
		s.Feed("Hello World");
		s.Feed(CursorPos(1, 6) + Csi("6P")); // delete 6 chars at col 6 (removes " World")
		s.GetRow(0).Should().Be("Hello");
	}

	[Fact]
	public void Insert_characters_shifts_text_right()
	{
		var s = S(cols: 20);
		s.Feed("Helo");
		s.Feed(CursorPos(1, 3) + Csi("1@")); // insert 1 char at col 3 (0-based 2)
		// "He" stays, space inserted at col 2, "lo" shifts right → "He lo" then feed 'l'
		s.Feed("l");
		s.GetRow(0).Should().Be("Hello");
	}

	// ── Contains and FindRow ────────────────────────────────────────────────────

	[Fact]
	public void Contains_finds_text_after_repaint()
	{
		var s = S();
		s.Feed("Do you want to proceed?");
		s.Feed(CursorPos(1, 1) + "Do " + CursorForward(4) + "want");
		s.Contains("Do you want to proceed?").Should().BeTrue();
	}

	[Fact]
	public void FindRow_returns_correct_row_index()
	{
		var s = S();
		s.Feed("Alpha\r\nBeta\r\nGamma");
		s.FindRow("Beta").Should().Be(1);
		s.FindRow("Delta").Should().Be(-1);
	}

	// ── GetRows ─────────────────────────────────────────────────────────────────

	[Fact]
	public void GetRows_returns_array_matching_individual_GetRow_calls()
	{
		var s = S(cols: 10, rows: 3);
		s.Feed("A\r\nB\r\nC");
		var rows = s.GetRows();
		rows.Should().HaveCount(3);
		rows[0].Should().Be(s.GetRow(0));
		rows[1].Should().Be(s.GetRow(1));
		rows[2].Should().Be(s.GetRow(2));
	}

	// ── Cursor state ─────────────────────────────────────────────────────────────

	[Fact]
	public void CursorRow_and_CursorCol_reflect_current_position()
	{
		var s = S();
		s.Feed("AB\r\nCD");
		s.CursorRow.Should().Be(1);
		s.CursorCol.Should().Be(2);
	}

	// ── OSC / SGR passthrough ────────────────────────────────────────────────────

	[Fact]
	public void Osc_title_is_ignored_text_is_unaffected()
	{
		var s = S();
		s.Feed("\x1b]0;✳ antiphon\x07Hello");
		s.GetRow(0).Should().Be("Hello");
	}

	[Fact]
	public void Sgr_color_codes_are_ignored()
	{
		var s = S();
		s.Feed("\x1b[1;32mGreen\x1b[0m Normal");
		s.GetRow(0).Should().Be("Green Normal");
	}
}
