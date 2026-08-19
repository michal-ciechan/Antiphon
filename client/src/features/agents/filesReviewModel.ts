import type { AgentFileDto } from '../../api/review'

// Pure model helpers for the files review view. They live outside FilesReviewPanel.tsx so the
// component file only exports components (react-refresh), and so the tests can import them
// without pulling in the panel.

/** A file needs attention when it has no mark or its mark predates the current content. */
export function isUnviewed(file: AgentFileDto): boolean {
  if (file.contextOnly) return false
  return file.reviewLevel === null || file.reviewStale
}

/**
 * Merge the full workspace tree into the changed/agent-touched listing ("All files" toggle):
 * every path the review listing doesn't already cover becomes a dimmed context entry — browsable,
 * but outside the review set.
 */
export function mergeTreePaths(files: AgentFileDto[], treePaths: string[]): AgentFileDto[] {
  const known = new Set(files.map((f) => f.path.toLowerCase()))
  const extras = treePaths
    .filter((p) => !known.has(p.toLowerCase()))
    .map<AgentFileDto>((p) => ({
      path: p,
      gitStatus: 'None',
      external: false,
      agentEdits: 0,
      lastAgentEditAt: null,
      contentHash: null,
      reviewLevel: null,
      reviewStale: false,
      sizeBytes: null,
      isMarkdown: /\.(md|markdown)$/i.test(p),
      contextOnly: true,
    }))
  return [...files, ...extras].sort((a, b) =>
    a.path.localeCompare(b.path, undefined, { sensitivity: 'base' }),
  )
}

export interface TreeNode {
  name: string
  path: string
  children: Map<string, TreeNode>
  file?: AgentFileDto
}

export function buildTree(files: AgentFileDto[]): TreeNode {
  const root: TreeNode = { name: '', path: '', children: new Map() }
  for (const file of files) {
    const parts = file.path.split('/')
    let node = root
    for (let i = 0; i < parts.length; i++) {
      const part = parts[i]
      const childPath = node.path ? `${node.path}/${part}` : part
      if (!node.children.has(part))
        node.children.set(part, { name: part, path: childPath, children: new Map() })
      node = node.children.get(part)!
      if (i === parts.length - 1) node.file = file
    }
  }
  return root
}

export type FileViewMode = 'diff' | 'raw' | 'rendered'

/** The modes offered for a file, in tab order. Rendered is markdown-only; diff needs a baseline. */
export function viewModesFor(file: AgentFileDto): { label: string; value: FileViewMode }[] {
  return [
    ...(file.gitStatus !== 'None' && !file.external
      ? [{ label: 'Diff', value: 'diff' as const }]
      : []),
    { label: 'Raw', value: 'raw' as const },
    ...(file.isMarkdown ? [{ label: 'Rendered', value: 'rendered' as const }] : []),
  ]
}

/**
 * Rendered wins whenever the file can render — reading a doc is the common case, and the diff is
 * one click away. Falls back to the diff for changed code, then raw. Derived from
 * {@link viewModesFor} so the default is always a mode that is actually offered (an external
 * changed file has no Diff tab, and used to default to it).
 */
export function defaultViewMode(file: AgentFileDto): FileViewMode {
  const modes = viewModesFor(file)
  for (const preferred of ['rendered', 'diff', 'raw'] as const)
    if (modes.some((m) => m.value === preferred)) return preferred
  return 'raw'
}

/**
 * Which file the files view has open, and in which mode — hoistable so a caller can back it with
 * the URL. `view` is null whenever the file is showing its DEFAULT mode: only a deliberate,
 * non-default choice is worth remembering (and worth the query-string noise).
 */
export interface FilesViewSelection {
  selectedPath: string | null
  view: FileViewMode | null
  /** Open a file (null closes it). Clears the view — a different file gets its own default. */
  select: (path: string | null) => void
  /** null = back to the file's default. */
  setView: (view: FileViewMode | null) => void
}
