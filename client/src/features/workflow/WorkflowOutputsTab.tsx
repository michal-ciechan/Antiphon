import { useState, useEffect, useMemo } from 'react'
import { Box, Text, Group, Badge, SegmentedControl, Loader, Stack, UnstyledButton } from '@mantine/core'
import { VscFile, VscFolder, VscFolderOpened, VscChevronRight, VscChevronDown } from 'react-icons/vsc'
import { useEditor, EditorContent } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import { Markdown } from 'tiptap-markdown'
import { useBranchDiff, useWorkflowFileContent, type BranchDiffFileDto } from '../../api/projects'
import '../artifact/tiptap.css'

// --- Diff parsing ---

type DiffLineType = 'added' | 'removed' | 'context' | 'hunk' | 'header'

interface DiffLine {
  type: DiffLineType
  content: string
}

function parsePatch(patch: string): DiffLine[] {
  return patch.split('\n').map((line) => {
    if (
      line.startsWith('+++') ||
      line.startsWith('---') ||
      line.startsWith('diff ') ||
      line.startsWith('index ') ||
      line.startsWith('new file') ||
      line.startsWith('deleted file')
    ) {
      return { type: 'header', content: line }
    }
    if (line.startsWith('@@')) return { type: 'hunk', content: line }
    if (line.startsWith('+')) return { type: 'added', content: line.slice(1) }
    if (line.startsWith('-')) return { type: 'removed', content: line.slice(1) }
    return { type: 'context', content: line.startsWith(' ') ? line.slice(1) : line }
  })
}

// --- File tree ---

interface TreeNode {
  name: string
  path: string
  isDir: boolean
  children: TreeNode[]
  file?: BranchDiffFileDto
}

function buildTree(files: BranchDiffFileDto[]): TreeNode[] {
  const root: TreeNode[] = []

  for (const file of files) {
    const parts = file.filename.split('/')
    let nodes = root

    for (let i = 0; i < parts.length; i++) {
      const part = parts[i]
      const isLast = i === parts.length - 1
      let node = nodes.find((n) => n.name === part)

      if (!node) {
        const path = parts.slice(0, i + 1).join('/')
        node = { name: part, path, isDir: !isLast, children: [], file: isLast ? file : undefined }
        nodes.push(node)
      }
      nodes = node.children
    }
  }

  return root
}

function FileTreeNode({
  node,
  selected,
  onSelect,
  depth = 0,
}: {
  node: TreeNode
  selected: string | null
  onSelect: (filename: string) => void
  depth?: number
}) {
  const [open, setOpen] = useState(true)
  const isSelected = !node.isDir && node.path === selected

  if (node.isDir) {
    return (
      <Box>
        <UnstyledButton
          onClick={() => setOpen((o) => !o)}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 4,
            width: '100%',
            padding: `2px ${4 + depth * 12}px`,
            color: 'var(--mantine-color-dimmed)',
          }}
        >
          {open ? <VscChevronDown size={12} /> : <VscChevronRight size={12} />}
          {open ? <VscFolderOpened size={14} /> : <VscFolder size={14} />}
          <Text size="xs">{node.name}</Text>
        </UnstyledButton>
        {open &&
          node.children.map((child) => (
            <FileTreeNode
              key={child.path}
              node={child}
              selected={selected}
              onSelect={onSelect}
              depth={depth + 1}
            />
          ))}
      </Box>
    )
  }

  const additions = node.file?.additions ?? 0
  const deletions = node.file?.deletions ?? 0

  return (
    <UnstyledButton
      onClick={() => onSelect(node.path)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 4,
        width: '100%',
        padding: `3px ${4 + depth * 12}px`,
        backgroundColor: isSelected ? 'var(--mantine-color-active-9)' : 'transparent',
        borderRadius: 'var(--mantine-radius-xs)',
      }}
    >
      <VscFile size={14} style={{ flexShrink: 0, color: 'var(--mantine-color-dimmed)' }} />
      <Text size="xs" style={{ flex: 1, textAlign: 'left', wordBreak: 'break-all' }}>
        {node.name}
      </Text>
      <Group gap={2} style={{ flexShrink: 0 }}>
        {additions > 0 && (
          <Badge size="xs" color="green" variant="light" style={{ minWidth: 0, padding: '0 4px' }}>
            +{additions}
          </Badge>
        )}
        {deletions > 0 && (
          <Badge size="xs" color="red" variant="light" style={{ minWidth: 0, padding: '0 4px' }}>
            -{deletions}
          </Badge>
        )}
      </Group>
    </UnstyledButton>
  )
}

// --- Diff viewer ---

function DiffViewer({ file }: { file: BranchDiffFileDto }) {
  const lines = useMemo(() => parsePatch(file.patch), [file.patch])

  const lineStyle = (type: DiffLineType): React.CSSProperties => {
    switch (type) {
      case 'added':
        return { backgroundColor: 'rgba(46,160,67,0.18)', color: 'var(--mantine-color-green-4)' }
      case 'removed':
        return {
          backgroundColor: 'rgba(248,81,73,0.15)',
          color: 'var(--mantine-color-red-4)',
          textDecoration: 'line-through',
        }
      case 'hunk':
        return { backgroundColor: 'rgba(100,150,255,0.08)', color: 'var(--mantine-color-blue-4)' }
      case 'header':
        return { color: 'var(--mantine-color-dimmed)', opacity: 0.6 }
      default:
        return {}
    }
  }

  return (
    <Box style={{ fontFamily: 'monospace', fontSize: '0.72rem', lineHeight: 1.6 }}>
      {lines.map((line, i) => (
        <Box
          key={i}
          style={{
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-all',
            padding: '0 8px',
            ...lineStyle(line.type),
          }}
        >
          {line.content || '\u00a0'}
        </Box>
      ))}
    </Box>
  )
}

// --- Raw viewer ---

function RawViewer({ content }: { content: string }) {
  return (
    <Box
      style={{
        fontFamily: 'monospace',
        fontSize: '0.72rem',
        lineHeight: 1.6,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
        padding: '8px 12px',
        color: 'var(--mantine-color-text)',
      }}
    >
      {content}
    </Box>
  )
}

// --- Markdown rendered viewer (TipTap, read-only) ---

function MarkdownViewer({ content, diffFile }: { content: string; diffFile?: BranchDiffFileDto }) {
  const editor = useEditor({
    extensions: [StarterKit, Markdown],
    content,
    editable: false,
  })

  useEffect(() => {
    if (editor && content) {
      editor.commands.setContent(content)
    }
  }, [editor, content])

  return (
    <Box style={{ padding: '12px 16px', fontSize: '0.85rem', lineHeight: 1.7 }}>
      {diffFile && (diffFile.additions > 0 || diffFile.deletions > 0) && (
        <Group
          gap="xs"
          mb="sm"
          p="xs"
          style={{
            borderRadius: 'var(--mantine-radius-sm)',
            backgroundColor: 'rgba(46,160,67,0.08)',
            border: '1px solid rgba(46,160,67,0.2)',
          }}
        >
          {diffFile.additions > 0 && (
            <Badge size="xs" color="green" variant="light">
              +{diffFile.additions} added
            </Badge>
          )}
          {diffFile.deletions > 0 && (
            <Badge size="xs" color="red" variant="light">
              -{diffFile.deletions} removed
            </Badge>
          )}
          <Text size="xs" c="dimmed">
            Changes from base branch
          </Text>
        </Group>
      )}
      <Box className="tiptap-readonly" style={{ color: 'var(--mantine-color-text)' }}>
        <EditorContent editor={editor} />
      </Box>
    </Box>
  )
}

// --- View mode types ---

type ViewMode = 'rendered' | 'raw' | 'diff'

// --- Main component ---

export function WorkflowOutputsTab({ workflowId }: { workflowId?: string }) {
  const { data: branchDiff, isLoading, error } = useBranchDiff(workflowId)
  const [selectedFilename, setSelectedFilename] = useState<string | null>(null)
  const [viewMode, setViewMode] = useState<ViewMode>('rendered')

  const files = branchDiff?.files ?? []
  const tree = useMemo(() => buildTree(files), [files])

  // Auto-select first file
  useEffect(() => {
    if (files.length > 0 && !selectedFilename) {
      setSelectedFilename(files[0].filename)
    }
  }, [files, selectedFilename])

  const selectedFile = files.find((f) => f.filename === selectedFilename) ?? null
  const isMarkdown = selectedFilename?.match(/\.mdx?$/i) !== null

  // For non-markdown, default to diff view when switching to a new file
  const handleSelectFile = (filename: string) => {
    setSelectedFilename(filename)
    const isMd = filename.match(/\.mdx?$/i) !== null
    if (!isMd && viewMode === 'rendered') {
      setViewMode('diff')
    } else if (isMd && viewMode === 'diff') {
      setViewMode('rendered')
    }
  }

  // Fetch file content for rendered and raw views
  const { data: fileContentData, isLoading: contentLoading } = useWorkflowFileContent(
    workflowId,
    viewMode !== 'diff' ? selectedFilename : null,
  )

  if (!workflowId) return null

  if (isLoading) {
    return (
      <Stack align="center" justify="center" py="xl" gap="sm">
        <Loader size="sm" />
        <Text c="dimmed" size="sm">
          Loading files...
        </Text>
      </Stack>
    )
  }

  if (error || !branchDiff || files.length === 0) {
    const status = (error as { status?: number })?.status
    if (!error || status === 404 || status === 503 || files.length === 0) {
      return (
        <Stack align="center" justify="center" py="xl" gap={4}>
          <VscFile size={28} color="var(--mantine-color-dimmed)" />
          <Text c="dimmed" size="sm">
            No output files yet.
          </Text>
          <Text c="dimmed" size="xs" ta="center" maw={240}>
            Files will appear here once workflow stages have run.
          </Text>
        </Stack>
      )
    }
    return (
      <Text c="dimmed" size="sm" p="sm">
        Unable to load outputs.
      </Text>
    )
  }

  // Build segmented control options based on file type
  const viewOptions: { label: string; value: ViewMode }[] = isMarkdown
    ? [
        { label: 'Rendered', value: 'rendered' },
        { label: 'Raw', value: 'raw' },
        { label: 'Diff', value: 'diff' },
      ]
    : [
        { label: 'Raw', value: 'raw' },
        { label: 'Diff', value: 'diff' },
      ]

  return (
    <Box style={{ display: 'flex', height: '100%', minHeight: 0 }}>
      {/* File tree */}
      <Box
        style={{
          width: 180,
          flexShrink: 0,
          borderRight: '1px solid var(--mantine-color-dark-4)',
          overflow: 'auto',
          padding: '4px 0',
        }}
      >
        {tree.map((node) => (
          <FileTreeNode
            key={node.path}
            node={node}
            selected={selectedFilename}
            onSelect={handleSelectFile}
          />
        ))}
      </Box>

      {/* Content area */}
      <Box style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        {selectedFile ? (
          <>
            {/* Toolbar */}
            <Group
              gap="xs"
              px="sm"
              py={4}
              style={{
                borderBottom: '1px solid var(--mantine-color-dark-4)',
                flexShrink: 0,
              }}
            >
              <Text size="xs" style={{ fontFamily: 'monospace', flex: 1 }} truncate>
                {selectedFile.filename}
              </Text>
              <Group gap={4}>
                {selectedFile.additions > 0 && (
                  <Badge size="xs" color="green" variant="light">
                    +{selectedFile.additions}
                  </Badge>
                )}
                {selectedFile.deletions > 0 && (
                  <Badge size="xs" color="red" variant="light">
                    -{selectedFile.deletions}
                  </Badge>
                )}
              </Group>
              <SegmentedControl
                size="xs"
                value={viewMode}
                onChange={(v) => setViewMode(v as ViewMode)}
                data={viewOptions}
              />
            </Group>

            {/* Viewer */}
            <Box style={{ flex: 1, overflow: 'auto' }}>
              {viewMode === 'diff' ? (
                <DiffViewer file={selectedFile} />
              ) : contentLoading ? (
                <Stack align="center" justify="center" py="xl">
                  <Loader size="sm" />
                </Stack>
              ) : viewMode === 'rendered' && isMarkdown ? (
                <MarkdownViewer
                  content={fileContentData?.content ?? ''}
                  diffFile={selectedFile}
                />
              ) : (
                <RawViewer content={fileContentData?.content ?? ''} />
              )}
            </Box>
          </>
        ) : (
          <Stack align="center" justify="center" style={{ height: '100%' }}>
            <Text c="dimmed" size="sm">
              Select a file to view
            </Text>
          </Stack>
        )}
      </Box>
    </Box>
  )
}
