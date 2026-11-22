import { Box, ScrollArea } from '@mantine/core'

type LineKind = 'header' | 'add' | 'del' | 'ctx' | 'meta'

interface DiffLine {
  kind: LineKind
  sign: string
  text: string
  /** line number in the new file (context and added lines), or in the old file for a removed line */
  lineNo: number | null
}

const HEADER = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@(.*)$/

/** A unified-diff hunk (one or more `@@` sections) as numbered lines. */
function parseHunk(hunk: string): DiffLine[] {
  let oldNo = 0
  let newNo = 0
  const out: DiffLine[] = []
  for (const raw of hunk.replace(/\r\n/g, '\n').split('\n')) {
    const header = HEADER.exec(raw)
    if (header) {
      oldNo = Number(header[1])
      newNo = Number(header[2])
      out.push({ kind: 'header', sign: '', text: raw, lineNo: null })
      continue
    }
    const sign = raw[0] ?? ' '
    const text = raw.slice(1)
    if (sign === '+') out.push({ kind: 'add', sign, text, lineNo: newNo++ })
    else if (sign === '-') out.push({ kind: 'del', sign, text, lineNo: oldNo++ })
    else if (sign === '\\') out.push({ kind: 'meta', sign: '', text: raw, lineNo: null })
    else {
      out.push({ kind: 'ctx', sign: ' ', text, lineNo: newNo++ })
      oldNo++
    }
  }
  // a trailing newline in the hunk text is not a context line
  if (out.length > 0 && out[out.length - 1].kind === 'ctx' && out[out.length - 1].text === '' && hunk.endsWith('\n')) out.pop()
  return out
}

const STYLE: Record<LineKind, { background?: string; color?: string }> = {
  header: { background: 'var(--mantine-color-dark-6)', color: 'var(--mantine-color-cyan-4)' },
  add: { background: 'var(--mantine-color-green-light)', color: 'var(--mantine-color-green-light-color)' },
  del: { background: 'var(--mantine-color-red-light)', color: 'var(--mantine-color-red-light-color)' },
  ctx: {},
  meta: { color: 'var(--mantine-color-dimmed)' },
}

/**
 * A finding's `hunk`: a small monospace diff, added lines green and removed lines red, numbered
 * from the hunk header. Lines inside `highlight` (the anchor's new-file range) get a marker.
 */
export function DiffHunk({ hunk, highlight }: { hunk: string; highlight?: { start: number | null; end: number | null } }) {
  const lines = parseHunk(hunk)
  const start = highlight?.start ?? null
  const end = highlight?.end ?? start
  const marked = (l: DiffLine) => start !== null && end !== null && l.lineNo !== null && l.kind !== 'del' && l.lineNo >= start && l.lineNo <= end
  return (
    <ScrollArea type="auto" offsetScrollbars="x">
      <Box
        component="pre"
        m={0}
        py={4}
        ff="monospace"
        fz={11}
        lh={1.5}
        aria-label="Diff hunk"
        style={{ border: '1px solid var(--mantine-color-dark-4)', borderRadius: 4, minWidth: 'fit-content' }}
      >
        {lines.map((l, i) => (
          <Box
            key={i}
            style={{
              display: 'flex',
              ...STYLE[l.kind],
              boxShadow: marked(l) ? 'inset 3px 0 0 var(--mantine-color-yellow-5)' : undefined,
            }}
          >
            <Box component="span" w={38} pr={6} ta="right" c="dimmed" style={{ flex: 'none', userSelect: 'none', opacity: 0.8 }}>
              {l.lineNo ?? ''}
            </Box>
            <Box component="span" w={12} style={{ flex: 'none', userSelect: 'none' }}>
              {l.sign}
            </Box>
            <Box component="span" pr={8} style={{ whiteSpace: 'pre' }}>
              {l.text || ' '}
            </Box>
          </Box>
        ))}
      </Box>
    </ScrollArea>
  )
}
