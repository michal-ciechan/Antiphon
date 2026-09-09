using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

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

	/// <summary>Carriage return.</summary>
	private const string CR = "\r";

	/// <summary>Line feed.</summary>
	private const string LF = "\n";

	/// <summary>Backspace.</summary>
	private const string BSP = "\b";

	/// <summary>Horizontal tab.</summary>
	private const string TAB = "\t";

	// ── Plain text ──────────────────────────────────────────────────────────────

	[Test]
	public void Plain_text_lands_on_row_0()
	{
		var s = S();
		s.Feed("Hello");
		s.GetRow(0).ShouldBe("Hello");
	}

	[Test]
	public void Carriage_return_moves_to_column_0()
	{
		var s = S();
		s.Feed("ABC\rXY");
		// "XY" overwrites "AB", leaving "XYC"
		s.GetRow(0).ShouldBe("XYC");
	}

	[Test]
	public void Newline_advances_row_without_resetting_column()
	{
		// VT100: \n = line-feed only (no carriage return). Column is NOT reset.
		var s = S();
		s.Feed("AB\nCD"); // after "AB", cursor is at col 2; \n moves to row 1 col 2; "CD" lands there
		s.GetRow(0).ShouldBe("AB");
		s.GetRow(1).ShouldBe("  CD");
	}

	[Test]
	public void CRLF_advances_to_next_row_at_column_0()
	{
		var s = S();
		s.Feed("Line1\r\nLine2");
		s.GetRow(0).ShouldBe("Line1");
		s.GetRow(1).ShouldBe("Line2");
	}

	[Test]
	public void CRLF_writes_on_next_row_starting_at_column_0()
	{
		var s = S();
		s.Feed("First\r\nSecond");
		s.GetRow(0).ShouldBe("First");
		s.GetRow(1).ShouldBe("Second");
	}

	// ── Cursor-forward optimisation ─────────────────────────────────────────────

	[Test]
	public void CursorForward_preserves_existing_character()
	{
		var s = S();
		// First paint: write "Hello"
		s.Feed("Hello");
		// Repaint: move to col 0, write "H", skip "e" (already correct), write "llo"
		s.Feed(CursorPos(1, 1) + "H" + CursorForward(1) + "llo");
		s.GetRow(0).ShouldBe("Hello", "cursor-forward must keep the 'e' from the initial paint");
	}

	[Test]
	public void CursorForward_n_skips_n_characters()
	{
		var s = S();
		s.Feed("ABCDE");
		// Move to start, write "A", skip 3, write "E"
		s.Feed(CursorPos(1, 1) + "A" + CursorForward(3) + "E");
		s.GetRow(0).ShouldBe("ABCDE");
	}

	[Test]
	public void CursorForward_then_overwrite_changes_character()
	{
		// Write "H" at col 0, forward 1 (skip 'e' at col 1), write 'X' at col 2 (was 'l').
		var s = S();
		s.Feed("Hello");
		s.Feed(CursorPos(1, 1) + "H" + CursorForward(1) + "X");
		s.GetRow(0).ShouldBe("HeXlo");
	}

	[Test]
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
		s.GetRow(0).ShouldBe("Do you want to proceed?");
	}

	[Test]
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

		s.GetRow(0).ShouldBe("Do you want to proceed?");
		s.GetRow(1).ShouldBe("1. Yes");
		s.GetRow(2).ShouldBe("2. No");
	}

	// ── Cursor movement ─────────────────────────────────────────────────────────

	[Test]
	public void Cursor_position_absolute()
	{
		var s = S();
		s.Feed(CursorPos(3, 5) + "Hi");
		s.GetRow(2).ShouldStartWith("    Hi"); // row 3, col 5 (1-based) = row 2, col 4 (0-based)
	}

	[Test]
	public void Cursor_up_moves_cursor_row()
	{
		var s = S();
		s.Feed("Row0\r\nRow1\r\nRow2");
		s.Feed(Csi("2A") + "X"); // up 2 rows, overwrite at row 0 col 4
		s.GetRow(0).ShouldBe("Row0X"); // 'X' written at col 4, row 0
		// Actually: after "Row0\r\nRow1\r\nRow2", cursor is at row 2 col 4.
		// Up 2 → row 0 col 4. Write 'X' at (0,4).
		// "Row0" is 4 chars (0-3), X at col 4 → "Row0X"
	}

	[Test]
	public void Cursor_horizontal_absolute()
	{
		var s = S();
		s.Feed("Hello World");
		s.Feed(Csi("7G") + "!");   // move to column 7 (1-based), overwrite 'W' with '!'
		s.GetRow(0).ShouldBe("Hello !orld");
	}

	// ── Erase sequences ─────────────────────────────────────────────────────────

	[Test]
	public void Erase_to_end_of_line_clears_rest_of_row()
	{
		var s = S();
		s.Feed("Hello World");
		s.Feed(CursorPos(1, 6) + EraseEol); // cursor to col 6 (1-based), erase to end
		s.GetRow(0).ShouldBe("Hello");
	}

	[Test]
	public void Erase_entire_line_clears_row()
	{
		var s = S();
		s.Feed("Hello World\r\nLine2");
		s.Feed(CursorPos(1, 1) + EraseLine);
		s.GetRow(0).ShouldBe("", "entire line should be blank");
		s.GetRow(1).ShouldBe("Line2", "other rows unaffected");
	}

	[Test]
	public void Clear_screen_blanks_all_rows()
	{
		var s = S();
		s.Feed("Row0\r\nRow1\r\nRow2");
		s.Feed(Csi("2J")); // erase display
		s.GetRow(0).ShouldBe("");
		s.GetRow(1).ShouldBe("");
		s.GetRow(2).ShouldBe("");
	}

	// ── Scrolling ───────────────────────────────────────────────────────────────

	[Test]
	public void Newline_at_bottom_scrolls_content_up()
	{
		var s = S(cols: 10, rows: 3);
		s.Feed("A\r\nB\r\nC\r\n"); // third newline causes scroll
		s.GetRow(0).ShouldBe("B");
		s.GetRow(1).ShouldBe("C");
		s.GetRow(2).ShouldBe(""); // new blank line
	}

	[Test]
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
		s.GetRow(0).ShouldBe("R0", "row 0: outside scroll region, unchanged");
		s.GetRow(1).ShouldBe("R1", "row 1: outside scroll region, unchanged");
		s.GetRow(2).ShouldBe("R3", "row 2: was R2, scrolled up to show R3");
		s.GetRow(3).ShouldBe("R4", "row 3: was R3, scrolled up to show R4");
		s.GetRow(4).ShouldBe("", "row 4: new blank line from scroll");
	}

	// ── Delete / insert ─────────────────────────────────────────────────────────

	[Test]
	public void Delete_characters_shifts_text_left()
	{
		var s = S();
		s.Feed("Hello World");
		s.Feed(CursorPos(1, 6) + Csi("6P")); // delete 6 chars at col 6 (removes " World")
		s.GetRow(0).ShouldBe("Hello");
	}

	[Test]
	public void Insert_characters_shifts_text_right()
	{
		var s = S(cols: 20);
		s.Feed("Helo");
		s.Feed(CursorPos(1, 3) + Csi("1@")); // insert 1 char at col 3 (0-based 2)
		// "He" stays, space inserted at col 2, "lo" shifts right → "He lo" then feed 'l'
		s.Feed("l");
		s.GetRow(0).ShouldBe("Hello");
	}

	// ── Contains and FindRow ────────────────────────────────────────────────────

	[Test]
	public void Contains_finds_text_after_repaint()
	{
		var s = S();
		s.Feed("Do you want to proceed?");
		s.Feed(CursorPos(1, 1) + "Do " + CursorForward(4) + "want");
		s.Contains("Do you want to proceed?").ShouldBeTrue();
	}

	[Test]
	public void FindRow_returns_correct_row_index()
	{
		var s = S();
		s.Feed("Alpha\r\nBeta\r\nGamma");
		s.FindRow("Beta").ShouldBe(1);
		s.FindRow("Delta").ShouldBe(-1);
	}

	// ── GetRows ─────────────────────────────────────────────────────────────────

	[Test]
	public void GetRows_returns_array_matching_individual_GetRow_calls()
	{
		var s = S(cols: 10, rows: 3);
		s.Feed("A\r\nB\r\nC");
		var rows = s.GetRows();
		rows.Length.ShouldBe(3);
		rows[0].ShouldBe(s.GetRow(0));
		rows[1].ShouldBe(s.GetRow(1));
		rows[2].ShouldBe(s.GetRow(2));
	}

	// ── Cursor state ─────────────────────────────────────────────────────────────

	[Test]
	public void CursorRow_and_CursorCol_reflect_current_position()
	{
		var s = S();
		s.Feed("AB\r\nCD");
		s.CursorRow.ShouldBe(1);
		s.CursorCol.ShouldBe(2);
	}

	// ── Deferred (last-column) wrap ──────────────────────────────────────────────
	//
	// ConPTY's renderer assumes delayed EOL wrap: a printable in the last column leaves the
	// cursor ON that row, and only the NEXT printable moves down. Wrapping immediately puts
	// every row-relative move after a full-width row one row off (CARD-0449).

	[Test]
	public void Writing_the_last_column_keeps_the_cursor_on_the_row()
	{
		var s = S(10, 3);
		s.Feed("ABCDEFGHIJ");
		s.GetRow(0).ShouldBe("ABCDEFGHIJ");
		s.GetRow(1).ShouldBe("", "the wrap is deferred: nothing has landed on row 1 yet");
		s.CursorRow.ShouldBe(0);
		s.CursorCol.ShouldBe(9);
	}

	[Test]
	public void The_next_printable_after_a_full_row_wraps_first()
	{
		var s = S(10, 3);
		s.Feed("ABCDEFGHIJK");
		s.GetRow(0).ShouldBe("ABCDEFGHIJ");
		s.GetRow(1).ShouldBe("K");
		s.CursorRow.ShouldBe(1);
		s.CursorCol.ShouldBe(1);
	}

	[Test]
	public void A_carriage_return_after_a_full_row_stays_on_that_row()
	{
		var s = S(10, 3);
		s.Feed("ABCDEFGHIJ" + CR + "X");
		s.GetRow(0).ShouldBe("XBCDEFGHIJ", "CR cancels the owed wrap; X overwrites column 0 of the same row");
		s.GetRow(1).ShouldBe("");
		s.CursorRow.ShouldBe(0);
		s.CursorCol.ShouldBe(1);
	}

	[Test]
	public void A_full_row_then_CRLF_advances_exactly_one_row()
	{
		var s = S(10, 3);
		s.Feed("ABCDEFGHIJ" + CR + LF + "X");
		s.GetRow(1).ShouldBe("X");
		s.GetRow(2).ShouldBe("", "CRLF after a full row must advance ONE row, not two");
		s.CursorRow.ShouldBe(1);
		s.CursorCol.ShouldBe(1);
	}

	[Test]
	public void Backspace_after_a_full_row_steps_back_on_that_row()
	{
		var s = S(10, 3);
		s.Feed("ABCDEFGHIJ" + BSP + "X");
		s.GetRow(0).ShouldBe("ABCDEFGHXJ");
		s.GetRow(1).ShouldBe("");
		s.CursorRow.ShouldBe(0);
		s.CursorCol.ShouldBe(9);
	}

	[Test]
	[Arguments("A", "1A", 0, "         X"), Arguments("B", "1B", 2, "         X"),
	 Arguments("C", "1C", 1, "ABCDEFGHIX"), Arguments("D", "1D", 1, "ABCDEFGHXJ"),
	 Arguments("E", "1E", 2, "X"), Arguments("F", "1F", 0, "X"),
	 Arguments("G", "3G", 1, "ABXDEFGHIJ"), Arguments("H", "3;2H", 2, " X"),
	 Arguments("f", "3;2f", 2, " X"), Arguments("d", "3d", 2, "         X"),
	 Arguments("r", "1;3r", 0, "X")]
	public void Cursor_moves_clear_a_pending_wrap(string name, string seq, int landing, string expected)
	{
		var s = S(10, 3);
		s.Feed(CursorPos(2, 1) + "ABCDEFGHIJ" + Csi(seq) + "X");
		s.GetRow(landing).ShouldBe(expected, $"{name}: X must land where the move put the cursor");
		for (var r = 0; r < 3; r++)
		{
			if (r == landing) continue;
			s.GetRow(r).ShouldBe(r == 1 ? "ABCDEFGHIJ" : "",
				$"{name}: row {r} must be untouched — X must not have arrived there by wrapping");
		}
	}

	[Test]
	[Timeout(5000)]
	[Arguments("pending", "ABCDEFGHIJ", "ABCDEFGHIX"), Arguments("landing", "ABCDEFGHI", "ABCDEFGHIX"),
	 Arguments("middle", "AB", "AB      X")]
	public async Task A_tab_in_the_last_column_clears_the_wrap_and_terminates(
		string name, string prefix, string expected, CancellationToken cancellationToken)
	{
		var s = S(10, 3);
		// The tab loop must be bounded by the last column: WriteChar no longer advances past
		// Cols - 1, so an unbounded "write spaces until the next stop" loop never terminates.
		// Run it off-thread so a regression fails this test instead of hanging the whole run.
		await Task.Run(() => s.Feed(prefix + TAB + "X"), CancellationToken.None).WaitAsync(cancellationToken);
		s.GetRow(0).ShouldBe(expected, name);
		s.GetRow(1).ShouldBe("", $"{name}: the tab must not re-arm the wrap");
		s.CursorRow.ShouldBe(0, name);
		s.CursorCol.ShouldBe(9, name);
	}

	[Test]
	[Arguments("K", "K", "ABCDEFGHI", "X"), Arguments("J", "J", "ABCDEFGHI", "X"),
	 Arguments("X", "X", "ABCDEFGHI", "X"), Arguments("P", "P", "ABCDEFGHI", "X"),
	 Arguments("@", "@", "ABCDEFGHI", "X"), Arguments("m", "m", "ABCDEFGHIJ", "X"),
	 Arguments("L", "L", "", "XBCDEFGHIJ"), Arguments("M", "M", "", "X"),
	 Arguments("S", "S", "", "X"), Arguments("T", "T", "", "XBCDEFGHIJ")]
	public void Non_moving_sequences_keep_a_pending_wrap(string name, string seq, string row0, string row1)
	{
		var s = S(10, 3);
		s.Feed("ABCDEFGHIJ" + Csi(seq));
		s.CursorRow.ShouldBe(0, $"{name}: must not move the cursor");
		s.CursorCol.ShouldBe(9, $"{name}: must not move the cursor");
		s.Feed("X");
		s.GetRow(0).ShouldBe(row0, $"{name}: row 0");
		s.GetRow(1).ShouldBe(row1, $"{name}: row 1 — the wrap was still owed");
	}

	[Test]
	public void A_deferred_wrap_at_the_scroll_bottom_scrolls_the_region()
	{
		var s = S(10, 4);
		s.Feed(Csi("2;4r") + CursorPos(4, 1) + "ABCDEFGHIJ");
		s.GetRow(3).ShouldBe("ABCDEFGHIJ");
		s.GetRow(2).ShouldBe("");
		s.CursorRow.ShouldBe(3);
		s.CursorCol.ShouldBe(9);
		s.Feed("K");
		s.GetRow(2).ShouldBe("ABCDEFGHIJ", "paying the wrap must scroll WITHIN the region");
		s.GetRow(3).ShouldBe("K");
		s.GetRow(0).ShouldBe("", "row 0 is outside the scroll region and must not shift");
		s.CursorRow.ShouldBe(3);
		s.CursorCol.ShouldBe(1);
	}

	// ── OSC / SGR passthrough ────────────────────────────────────────────────────

	[Test]
	public void Osc_title_is_ignored_text_is_unaffected()
	{
		var s = S();
		s.Feed("\x1b]0;✳ antiphon\x07Hello");
		s.GetRow(0).ShouldBe("Hello");
	}

	[Test]
	public void Sgr_color_codes_are_ignored()
	{
		var s = S();
		s.Feed("\x1b[1;32mGreen\x1b[0m Normal");
		s.GetRow(0).ShouldBe("Green Normal");
	}
}
