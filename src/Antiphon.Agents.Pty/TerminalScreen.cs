using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// A virtual terminal screen buffer.  Interprets the raw PTY byte stream —
/// including cursor-movement sequences, erase commands, and scroll regions —
/// and maintains the actual rendered state of a fixed cols × rows character grid.
///
/// This is different from <see cref="AnsiStripper"/> which concatenates the
/// non-escape bytes but ignores cursor movements.  When the terminal repaints
/// with cursor-forward optimisation (<c>\x1b[1C</c> instead of re-writing a
/// character that is already correct), <see cref="AnsiStripper"/> leaves a gap
/// whereas <see cref="TerminalScreen"/> preserves the character from the previous
/// paint — which is the correct, human-visible result.
/// </summary>
public sealed class TerminalScreen
{
	// Each cell stores one character; row 0 = top of screen.
	private readonly char[][] _cells;
	private int _cursorRow;    // 0-based, clamped to [0, Rows)
	private int _cursorCol;    // 0-based, clamped to [0, Cols)
	private int _scrollTop;    // scroll region top (0-based, inclusive)
	private int _scrollBottom; // scroll region bottom (0-based, inclusive)

	public int Cols { get; }
	public int Rows { get; }

	/// <summary>Current cursor row (0-based).</summary>
	public int CursorRow => _cursorRow;

	/// <summary>Current cursor column (0-based).</summary>
	public int CursorCol => _cursorCol;

	public TerminalScreen(int cols = 120, int rows = 30)
	{
		Cols = cols;
		Rows = rows;
		_scrollBottom = rows - 1;
		_cells = new char[rows][];
		for (int i = 0; i < rows; i++)
		{
			_cells[i] = new char[cols];
			Array.Fill(_cells[i], ' ');
		}
	}

	// ── Public API ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Feed raw PTY data (may contain any ANSI/VT100 escape sequences) into the
	/// screen.  Call this for every chunk received from the PTY reader.
	/// </summary>
	public void Feed(string data)
	{
		var span = data.AsSpan();
		int i = 0;
		while (i < span.Length)
		{
			char c = span[i];
			if (c == '\x1b')
			{
				i = HandleEscape(span, i + 1);
			}
			else switch (c)
			{
				case '\r':
					_cursorCol = 0;
					i++;
					break;
				case '\n':
					LineFeed();
					i++;
					break;
				case '\b':
					if (_cursorCol > 0) _cursorCol--;
					i++;
					break;
				case '\t':
				{
					int next = (_cursorCol / 8 + 1) * 8;
					while (_cursorCol < next) WriteChar(' ');
					i++;
					break;
				}
				default:
					if (c >= ' ') WriteChar(c);
					i++;
					break;
			}
		}
	}

	/// <summary>
	/// Returns the rendered text of the full screen: rows joined by '\n', with
	/// trailing spaces trimmed from each row.
	/// </summary>
	public string GetScreenText()
	{
		var sb = new StringBuilder(Rows * Cols);
		for (int r = 0; r < Rows; r++)
		{
			if (r > 0) sb.Append('\n');
			AppendRow(sb, r);
		}
		return sb.ToString();
	}

	/// <summary>Returns the rendered text of a single row (0-based), trailing spaces trimmed.</summary>
	public string GetRow(int row)
	{
		if (row < 0 || row >= Rows) return string.Empty;
		var sb = new StringBuilder(Cols);
		AppendRow(sb, row);
		return sb.ToString();
	}

	/// <summary>Returns all rows as a string array (trailing spaces trimmed per row).</summary>
	public string[] GetRows() => Enumerable.Range(0, Rows).Select(GetRow).ToArray();

	/// <summary>
	/// Returns true if <paramref name="text"/> appears anywhere on the rendered screen.
	/// Searches across the screen text returned by <see cref="GetScreenText"/>.
	/// </summary>
	public bool Contains(string text, StringComparison comparison = StringComparison.Ordinal)
		=> GetScreenText().Contains(text, comparison);

	/// <summary>
	/// Returns the 0-based index of the first row whose rendered text contains
	/// <paramref name="text"/>, or -1 if not found.
	/// </summary>
	public int FindRow(string text, StringComparison comparison = StringComparison.Ordinal)
	{
		for (int r = 0; r < Rows; r++)
			if (GetRow(r).Contains(text, comparison)) return r;
		return -1;
	}

	// ── Private helpers ─────────────────────────────────────────────────────────

	private void AppendRow(StringBuilder sb, int row)
	{
		int end = Cols - 1;
		while (end >= 0 && _cells[row][end] == ' ') end--;
		for (int c = 0; c <= end; c++) sb.Append(_cells[row][c]);
	}

	private void WriteChar(char c)
	{
		if (_cursorRow >= 0 && _cursorRow < Rows && _cursorCol >= 0 && _cursorCol < Cols)
			_cells[_cursorRow][_cursorCol] = c;

		_cursorCol++;
		if (_cursorCol >= Cols)
		{
			_cursorCol = 0;
			LineFeed();
		}
	}

	private void LineFeed()
	{
		if (_cursorRow == _scrollBottom)
			ScrollUp();
		else if (_cursorRow < Rows - 1)
			_cursorRow++;
	}

	private void ScrollUp()
	{
		for (int r = _scrollTop; r < _scrollBottom; r++)
			Array.Copy(_cells[r + 1], _cells[r], Cols);
		Array.Fill(_cells[_scrollBottom], ' ');
	}

	private void ScrollDown()
	{
		for (int r = _scrollBottom; r > _scrollTop; r--)
			Array.Copy(_cells[r - 1], _cells[r], Cols);
		Array.Fill(_cells[_scrollTop], ' ');
	}

	private void EraseToEndOfLine(int row, int col)
	{
		if (row < 0 || row >= Rows) return;
		col = Math.Max(0, Math.Min(Cols, col));
		Array.Fill(_cells[row], ' ', col, Cols - col);
	}

	private void EraseFromStartOfLine(int row, int col)
	{
		if (row < 0 || row >= Rows) return;
		int end = Math.Min(col + 1, Cols);
		Array.Fill(_cells[row], ' ', 0, end);
	}

	// ── Escape sequence handling ────────────────────────────────────────────────

	private int HandleEscape(ReadOnlySpan<char> data, int i)
	{
		if (i >= data.Length) return i;
		char next = data[i];
		switch (next)
		{
			case '[': return HandleCsi(data, i + 1);
			case ']': return SkipOsc(data, i + 1);
			case 'P': case 'X': case '^': case '_': return SkipUntilSt(data, i + 1);
			case '7': // DECSC - save cursor (simplified: ignore)
			case '8': // DECRC - restore cursor (simplified: ignore)
				return i + 1;
			case 'M': // RI - reverse index
				if (_cursorRow == _scrollTop) ScrollDown();
				else if (_cursorRow > 0) _cursorRow--;
				return i + 1;
			case '=': case '>': // alternate/normal keypad mode
				return i + 1;
			case '(': case ')': case '*': case '+': // character set
				return i + 2 <= data.Length ? i + 2 : i + 1;
			case '\\': // ST stray
				return i + 1;
			default:
				// Two-char ESC sequence (e.g. ESC @ through ESC _)
				return i + 1;
		}
	}

	private int HandleCsi(ReadOnlySpan<char> data, int i)
	{
		// Optional private/intermediate prefix
		bool isPrivate = false;
		if (i < data.Length && (data[i] == '?' || data[i] == '>' || data[i] == '<' || data[i] == '!'))
		{
			isPrivate = data[i] == '?';
			i++;
		}

		// Parse numeric params separated by ';'
		Span<int> ps = stackalloc int[8];
		int psCount = 0;
		int cur = 0;
		bool hasCur = false;

		while (i < data.Length)
		{
			char c = data[i];
			if (c >= '0' && c <= '9') { cur = cur * 10 + (c - '0'); hasCur = true; i++; }
			else if (c == ';')
			{
				if (psCount < ps.Length) ps[psCount++] = hasCur ? cur : 0;
				cur = 0; hasCur = false; i++;
			}
			else if (c >= ' ' && c <= '/') { i++; } // intermediate bytes
			else break;
		}
		if (hasCur && psCount < ps.Length) ps[psCount++] = cur;

		if (i >= data.Length) return i;
		char final = data[i];
		i++;

		if (isPrivate) return i; // private sequences (mouse, bracketed paste, etc.)

		int p1 = psCount > 0 ? ps[0] : 0;
		int p2 = psCount > 1 ? ps[1] : 0;

		switch (final)
		{
			case 'A': // cursor up
				_cursorRow = Math.Max(_scrollTop, _cursorRow - Math.Max(1, p1));
				break;
			case 'B': // cursor down
				_cursorRow = Math.Min(_scrollBottom, _cursorRow + Math.Max(1, p1));
				break;
			case 'C': // cursor forward
				_cursorCol = Math.Min(Cols - 1, _cursorCol + Math.Max(1, p1));
				break;
			case 'D': // cursor back
				_cursorCol = Math.Max(0, _cursorCol - Math.Max(1, p1));
				break;
			case 'E': // cursor next line
				_cursorRow = Math.Min(Rows - 1, _cursorRow + Math.Max(1, p1));
				_cursorCol = 0;
				break;
			case 'F': // cursor prev line
				_cursorRow = Math.Max(0, _cursorRow - Math.Max(1, p1));
				_cursorCol = 0;
				break;
			case 'G': // cursor horizontal absolute (1-based)
				_cursorCol = Math.Clamp((p1 == 0 ? 1 : p1) - 1, 0, Cols - 1);
				break;
			case 'H': case 'f': // cursor position (1-based row;col)
				_cursorRow = Math.Clamp((p1 == 0 ? 1 : p1) - 1, 0, Rows - 1);
				_cursorCol = Math.Clamp((p2 == 0 ? 1 : p2) - 1, 0, Cols - 1);
				break;
			case 'd': // line position absolute (1-based)
				_cursorRow = Math.Clamp((p1 == 0 ? 1 : p1) - 1, 0, Rows - 1);
				break;
			case 'J': // erase in display
				switch (p1)
				{
					case 0:
						EraseToEndOfLine(_cursorRow, _cursorCol);
						for (int r = _cursorRow + 1; r < Rows; r++) Array.Fill(_cells[r], ' ');
						break;
					case 1:
						for (int r = 0; r < _cursorRow; r++) Array.Fill(_cells[r], ' ');
						EraseFromStartOfLine(_cursorRow, _cursorCol);
						break;
					case 2: case 3:
						foreach (var row in _cells) Array.Fill(row, ' ');
						break;
				}
				break;
			case 'K': // erase in line
				switch (p1)
				{
					case 0: EraseToEndOfLine(_cursorRow, _cursorCol); break;
					case 1: EraseFromStartOfLine(_cursorRow, _cursorCol); break;
					case 2: Array.Fill(_cells[_cursorRow], ' '); break;
				}
				break;
			case 'L': // insert lines
			{
				int n = Math.Max(1, p1);
				for (int r = _scrollBottom; r >= _cursorRow + n; r--)
					Array.Copy(_cells[r - n], _cells[r], Cols);
				for (int r = _cursorRow; r < _cursorRow + n && r <= _scrollBottom; r++)
					Array.Fill(_cells[r], ' ');
				break;
			}
			case 'M': // delete lines
			{
				int n = Math.Max(1, p1);
				for (int r = _cursorRow; r <= _scrollBottom - n; r++)
					Array.Copy(_cells[r + n], _cells[r], Cols);
				for (int r = Math.Max(_cursorRow, _scrollBottom - n + 1); r <= _scrollBottom; r++)
					Array.Fill(_cells[r], ' ');
				break;
			}
			case 'P': // delete characters
			{
				int n = Math.Min(Math.Max(1, p1), Cols - _cursorCol);
				int move = Cols - _cursorCol - n;
				if (move > 0)
					Array.Copy(_cells[_cursorRow], _cursorCol + n, _cells[_cursorRow], _cursorCol, move);
				Array.Fill(_cells[_cursorRow], ' ', Cols - n, n);
				break;
			}
			case '@': // insert characters
			{
				int n = Math.Min(Math.Max(1, p1), Cols - _cursorCol);
				int move = Cols - _cursorCol - n;
				if (move > 0)
					Array.Copy(_cells[_cursorRow], _cursorCol, _cells[_cursorRow], _cursorCol + n, move);
				Array.Fill(_cells[_cursorRow], ' ', _cursorCol, n);
				break;
			}
			case 'S': // scroll up
				for (int j = 0; j < Math.Max(1, p1); j++) ScrollUp();
				break;
			case 'T': // scroll down
				for (int j = 0; j < Math.Max(1, p1); j++) ScrollDown();
				break;
			case 'r': // set scroll region (DECSTBM, 1-based)
			{
				int top = Math.Clamp((p1 == 0 ? 1 : p1) - 1, 0, Rows - 1);
				int bottom = Math.Clamp((p2 == 0 ? Rows : p2) - 1, 0, Rows - 1);
				if (top < bottom)
				{
					_scrollTop = top;
					_scrollBottom = bottom;
				}
				_cursorRow = 0;
				_cursorCol = 0;
				break;
			}
			case 'X': // erase character
			{
				int n = Math.Min(Math.Max(1, p1), Cols - _cursorCol);
				Array.Fill(_cells[_cursorRow], ' ', _cursorCol, n);
				break;
			}
			// All other CSI sequences (SGR, modes, reports, etc.) — skip.
		}

		return i;
	}

	private static int SkipOsc(ReadOnlySpan<char> data, int i)
	{
		while (i < data.Length)
		{
			if (data[i] == '\x07') return i + 1;
			if (data[i] == '\x1b' && i + 1 < data.Length && data[i + 1] == '\\') return i + 2;
			i++;
		}
		return i;
	}

	private static int SkipUntilSt(ReadOnlySpan<char> data, int i)
	{
		while (i < data.Length)
		{
			if (data[i] == '\x1b' && i + 1 < data.Length && data[i + 1] == '\\') return i + 2;
			i++;
		}
		return i;
	}
}
