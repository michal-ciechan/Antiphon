import { DiffEditor, Editor } from '@monaco-editor/react'
import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Checkbox,
  Group,
  Loader,
  Menu,
  Paper,
  ScrollArea,
  SegmentedControl,
  Stack,
  Text,
  Textarea,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMemo, useState, type MouseEvent as ReactMouseEvent } from 'react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import {
  TbArrowsMaximize,
  TbCheck,
  TbChecks,
  TbEye,
  TbFile,
  TbFlag,
  TbFolder,
  TbGitCommit,
  TbMessagePlus,
  TbRefresh,
  TbSend,
  TbUserShare,
} from 'react-icons/tb'
import { DelegateModal } from '../delegations/DelegateModal'
import { RenderedMarkdownReview } from './RenderedMarkdownReview'
import { SelectionComposer, SelectionDelegate } from './SelectionDelegate'
import {
  useAddReviewComment,
  useAgentCommits,
  useAgentFileContent,
  useAgentFiles,
  useAgentFilesTree,
  useCreateReviewThread,
  useMarkFilesReview,
  useResolveReviewThread,
  useReviewThreads,
  useSetReviewCheckpoint,
  type AgentFileDto,
  type FilesBaselineDto,
  type ReviewThreadDto,
  type ReviewThreadStatus,
} from '../../api/review'
import { getApiErrorMessage } from '../../api/client'

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

interface TreeNode {
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

const statusColor: Record<string, string> = {
  Modified: 'yellow',
  Added: 'green',
  Untracked: 'green',
  Deleted: 'red',
  Renamed: 'blue',
  None: 'gray',
}

const threadStatusColor: Record<ReviewThreadStatus, string> = {
  Open: 'gray',
  AwaitingAgent: 'blue',
  AwaitingHuman: 'orange',
  Resolved: 'green',
}

/** Tree/viewer heights — CSS values; the defaults suit the embedded AgentsPage panel. */
export interface FilesPanelHeights {
  /** Tree scroll cap — embedded layout only (the sidebar layout flex-fills the tree). */
  tree?: number | string
  viewer: number | string
}

const EMBEDDED_HEIGHTS: FilesPanelHeights = { tree: 480, viewer: 420 }

export function FilesReviewPanel({
  agentId,
  initialSelectedPath = null,
  heights = EMBEDDED_HEIGHTS,
  showExpand = false,
  layout = 'embedded',
}: {
  agentId: string
  /** Storybook/screenshot hook: pre-open a file without a click. */
  initialSelectedPath?: string | null
  /** Override tree/viewer heights (the full-screen page passes viewport-derived calc() values). */
  heights?: FilesPanelHeights
  /** Show the "open full-screen files view" button (embedded AgentsPage usage). */
  showExpand?: boolean
  /**
   * 'embedded': header row above tree + viewer (the AgentsPage panel). 'sidebar': the tree is a
   * full-height left bar owning its counts/filter/refresh controls, and the viewer takes the whole
   * middle — its filename/mode/mark row sits at the very top (the full-screen files page).
   */
  layout?: 'embedded' | 'sidebar'
}) {
  // Baseline defaults to the latest "work completed" checkpoint; the server degrades to HEAD
  // when none exists (the response's baseline.kind says which actually applied).
  const [since, setSince] = useState<string>('checkpoint')
  const [showAll, setShowAll] = useState(false)
  const files = useAgentFiles(agentId, since)
  const workspaceTree = useAgentFilesTree(agentId, showAll)
  const threads = useReviewThreads(agentId)
  const mark = useMarkFilesReview(agentId)
  const [selectedPath, setSelectedPath] = useState<string | null>(initialSelectedPath)
  const [onlyUnviewed, setOnlyUnviewed] = useState(false)
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; prefix: string } | null>(null)

  const mergedFiles = useMemo(() => {
    const base = files.data?.files ?? []
    return showAll && workspaceTree.data ? mergeTreePaths(base, workspaceTree.data.paths) : base
  }, [files.data, showAll, workspaceTree.data])

  const visibleFiles = useMemo(
    () => (onlyUnviewed ? mergedFiles.filter(isUnviewed) : mergedFiles),
    [mergedFiles, onlyUnviewed],
  )

  const tree = useMemo(() => buildTree(visibleFiles.filter((f) => !f.external)), [visibleFiles])
  const externals = useMemo(() => visibleFiles.filter((f) => f.external), [visibleFiles])
  const threadsByPath = useMemo(() => {
    const map = new Map<string, ReviewThreadDto[]>()
    for (const t of threads.data ?? []) {
      if (!map.has(t.path)) map.set(t.path, [])
      map.get(t.path)!.push(t)
    }
    return map
  }, [threads.data])

  const selectedFile = mergedFiles.find((f) => f.path === selectedPath) ?? null

  const doMark = (request: { paths?: string[]; prefix?: string; level: 'Viewed' | 'Reviewed' | null }) =>
    mark.mutate(
      { ...request, since },
      {
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Marking failed') }),
      },
    )

  if (files.isLoading) return <Loader size="sm" />
  if (!files.data) return null

  const unviewedCount = (files.data.files ?? []).filter(isUnviewed).length
  const baseline = files.data.baseline

  const baselineControls = (
    <Group gap="xs" wrap="nowrap">
      <BaselineMenu agentId={agentId} since={since} baseline={baseline} onChange={setSince} />
      <Checkbox
        size="xs"
        label="All files"
        checked={showAll}
        onChange={(e) => setShowAll(e.currentTarget.checked)}
      />
    </Group>
  )

  const baselineCaption = baseline && baseline.kind !== 'head' && (
    <Text size="xs" c="dimmed" data-testid="baseline-caption">
      Changes since {baseline.commitSha?.slice(0, 7)}
      {baseline.kind === 'checkpoint' &&
        ` — ${baseline.checkpointReason ?? 'work completed'}${
          baseline.checkpointAt ? `, ${new Date(baseline.checkpointAt).toLocaleString()}` : ''
        }${baseline.timestampFallback ? ' (resolved by timestamp)' : ''}`}
      {workspaceTree.data?.truncated && showAll ? ' · file list truncated' : ''}
    </Text>
  )

  const treeContent = (
    <>
      <TreeLevel
        node={tree}
        depth={0}
        selectedPath={selectedPath}
        threadsByPath={threadsByPath}
        onSelect={setSelectedPath}
        onContextMenu={(e, prefix) => {
          e.preventDefault()
          setContextMenu({ x: e.clientX, y: e.clientY, prefix })
        }}
      />
      {externals.length > 0 && (
        <>
          <Text size="xs" c="dimmed" mt="xs">
            Outside workspace
          </Text>
          {externals.map((f) => (
            <FileRow
              key={f.path}
              file={f}
              depth={0}
              selected={selectedPath === f.path}
              threadCount={threadsByPath.get(f.path)?.length ?? 0}
              onSelect={setSelectedPath}
              onContextMenu={(e) => e.preventDefault()}
            />
          ))}
        </>
      )}
      {visibleFiles.length === 0 && (
        <Text size="sm" c="dimmed" p="sm">
          {onlyUnviewed
            ? 'Everything reviewed 🎉'
            : 'No changed or agent-touched files since the selected baseline.'}
        </Text>
      )}
    </>
  )

  const viewer = selectedFile ? (
    <FileViewer
      key={`${selectedFile.path}:${since}`}
      agentId={agentId}
      workspaceRoot={files.data.workspaceRoot}
      file={selectedFile}
      since={since}
      threads={threadsByPath.get(selectedFile.path) ?? []}
      viewerHeight={heights.viewer}
      onMark={(level) => doMark({ paths: [selectedFile.path], level })}
    />
  ) : (
    <Paper withBorder p="xl">
      <Text c="dimmed" size="sm">
        Select a file to view its changes, raw content, or rendered markdown — and to comment
        inline.
      </Text>
    </Paper>
  )

  const contextMenuEl = contextMenu && (
    <Menu opened onClose={() => setContextMenu(null)} position="bottom-start" withinPortal>
      <Menu.Target>
        <Box pos="fixed" left={contextMenu.x} top={contextMenu.y} w={1} h={1} />
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{contextMenu.prefix || 'All files'}</Menu.Label>
        <Menu.Item
          leftSection={<TbEye size={14} />}
          onClick={() => {
            doMark({ prefix: contextMenu.prefix, level: 'Viewed' })
            setContextMenu(null)
          }}
        >
          Mark all as viewed
        </Menu.Item>
        <Menu.Item
          leftSection={<TbChecks size={14} />}
          onClick={() => {
            doMark({ prefix: contextMenu.prefix, level: 'Reviewed' })
            setContextMenu(null)
          }}
        >
          Mark all as reviewed
        </Menu.Item>
        <Menu.Item
          onClick={() => {
            doMark({ prefix: contextMenu.prefix, level: null })
            setContextMenu(null)
          }}
        >
          Clear marks
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )

  if (layout === 'sidebar') {
    // Full-height sidebar layout: the tree owns the left bar with its controls tucked into its
    // header, and the viewer (whose filename/mode/mark row is its own top line) gets the rest.
    return (
      <Group align="stretch" gap="md" wrap="nowrap" style={{ height: '100%', minHeight: 0 }}>
        <Paper
          withBorder
          p="xs"
          w={340}
          style={{ flexShrink: 0, display: 'flex', flexDirection: 'column', minHeight: 0 }}
        >
          <Group justify="space-between" gap="xs" wrap="nowrap" pb={4} style={{ flexShrink: 0 }}>
            <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
              {visibleFiles.length} file{visibleFiles.length === 1 ? '' : 's'} · {unviewedCount}{' '}
              unviewed
            </Text>
            <Group gap="xs" wrap="nowrap">
              <Checkbox
                size="xs"
                label="Unviewed"
                checked={onlyUnviewed}
                onChange={(e) => setOnlyUnviewed(e.currentTarget.checked)}
              />
              <ActionIcon variant="subtle" aria-label="Refresh files" onClick={() => files.refetch()}>
                <TbRefresh size={16} />
              </ActionIcon>
            </Group>
          </Group>
          <Group justify="space-between" gap="xs" wrap="nowrap" pb={6} style={{ flexShrink: 0 }}>
            {baselineControls}
          </Group>
          {baselineCaption && <Box pb={4}>{baselineCaption}</Box>}
          <ScrollArea type="auto" style={{ flexGrow: 1, minHeight: 0 }}>
            {treeContent}
          </ScrollArea>
        </Paper>

        <Box style={{ flexGrow: 1, minWidth: 0, minHeight: 0, overflowY: 'auto' }}>{viewer}</Box>
        {contextMenuEl}
      </Group>
    )
  }

  return (
    <Stack gap="sm" mt="md">
      <Group justify="space-between">
        <Group gap="xs">
          <Title order={4}>Files</Title>
          {files.data.isGitRepository && (
            <Badge variant="light" color="gray" size="sm">
              git
            </Badge>
          )}
          <Text size="xs" c="dimmed">
            {visibleFiles.length} file{visibleFiles.length === 1 ? '' : 's'} · {unviewedCount} unviewed
          </Text>
        </Group>
        <Group gap="sm">
          {baselineControls}
          <Checkbox
            size="xs"
            label="Only unviewed"
            checked={onlyUnviewed}
            onChange={(e) => setOnlyUnviewed(e.currentTarget.checked)}
          />
          <ActionIcon variant="subtle" aria-label="Refresh files" onClick={() => files.refetch()}>
            <TbRefresh size={16} />
          </ActionIcon>
          {showExpand && (
            <Tooltip label="Open full-screen files view (new tab)">
              <ActionIcon
                variant="subtle"
                component="a"
                href={`/agents/${agentId}/files`}
                target="_blank"
                aria-label="Open full-screen files view"
              >
                <TbArrowsMaximize size={16} />
              </ActionIcon>
            </Tooltip>
          )}
        </Group>
      </Group>
      {baselineCaption}

      <Group align="flex-start" gap="md" wrap="nowrap">
        <Paper withBorder p="xs" w={340} style={{ flexShrink: 0 }}>
          <ScrollArea.Autosize mah={heights.tree}>{treeContent}</ScrollArea.Autosize>
        </Paper>

        <Box style={{ flexGrow: 1, minWidth: 0 }}>{viewer}</Box>
      </Group>

      {contextMenuEl}
    </Stack>
  )
}

/**
 * Baseline picker: what the change listing diffs against — uncommitted only (HEAD), the last
 * "work completed" checkpoint, or any recent commit. Also hosts "Mark work complete", which
 * records a new checkpoint at the current commit.
 */
function BaselineMenu({
  agentId,
  since,
  baseline,
  onChange,
}: {
  agentId: string
  since: string
  baseline: FilesBaselineDto | undefined
  onChange: (since: string) => void
}) {
  const commits = useAgentCommits(agentId)
  const setCheckpoint = useSetReviewCheckpoint(agentId)

  const captureCheckpoint = (commitSha?: string) =>
    setCheckpoint.mutate(
      commitSha ? { commitSha } : undefined,
      {
        onSuccess: () => {
          onChange('checkpoint')
          notifications.show({
            color: 'green',
            message: commitSha
              ? `Baseline set at ${commitSha.slice(0, 7)}`
              : 'Baseline set at the current commit',
          })
        },
        onError: (error) =>
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Setting baseline failed'),
          }),
      },
    )

  const label =
    since === 'head'
      ? 'Uncommitted'
      : since === 'checkpoint'
        ? baseline?.kind === 'checkpoint'
          ? 'Since completed'
          : 'Uncommitted'
        : `Since ${since.slice(0, 7)}`

  const check = <TbCheck size={14} />
  return (
    <Menu shadow="md" width={380} position="bottom-start">
      <Menu.Target>
        <Button
          size="compact-xs"
          variant="light"
          leftSection={<TbGitCommit size={14} />}
          data-testid="baseline-menu"
        >
          {label}
        </Button>
      </Menu.Target>
      <Menu.Dropdown style={{ maxHeight: 420, overflowY: 'auto' }}>
        <Menu.Label>Show changes…</Menu.Label>
        <Menu.Item
          leftSection={since === 'head' ? check : <Box w={14} />}
          onClick={() => onChange('head')}
        >
          Uncommitted only (vs HEAD)
        </Menu.Item>
        <Menu.Item
          leftSection={since === 'checkpoint' ? check : <Box w={14} />}
          onClick={() => onChange('checkpoint')}
        >
          <div>
            Since last completed work
            {baseline?.kind === 'checkpoint' && baseline.checkpointAt && (
              <Text size="xs" c="dimmed">
                {baseline.checkpointReason ?? 'checkpoint'} ·{' '}
                {new Date(baseline.checkpointAt).toLocaleString()}
              </Text>
            )}
          </div>
        </Menu.Item>
        {(commits.data?.commits.length ?? 0) > 0 && (
          <>
            <Menu.Divider />
            <Menu.Label>Since commit</Menu.Label>
            {commits.data!.commits.map((c) => (
              <Menu.Item
                key={c.sha}
                leftSection={since === c.sha ? check : <Box w={14} />}
                rightSection={
                  <Tooltip label="Set checkpoint at this commit" withArrow>
                    <ActionIcon
                      component="div"
                      size="sm"
                      variant="subtle"
                      aria-label={`Set checkpoint at ${c.shortSha}`}
                      onClick={(e) => {
                        e.stopPropagation()
                        captureCheckpoint(c.sha)
                      }}
                    >
                      <TbFlag size={13} />
                    </ActionIcon>
                  </Tooltip>
                }
                onClick={() => onChange(c.sha)}
              >
                <Group gap={6} wrap="nowrap">
                  <Text size="xs" ff="monospace" c="dimmed">
                    {c.shortSha}
                  </Text>
                  <Text size="sm" truncate style={{ flexGrow: 1 }}>
                    {c.subject}
                  </Text>
                  {c.isCheckpoint && (
                    <Badge size="xs" variant="light" color="teal">
                      baseline
                    </Badge>
                  )}
                </Group>
                <Text size="xs" c="dimmed">
                  {new Date(c.date).toLocaleString()}
                </Text>
              </Menu.Item>
            ))}
          </>
        )}
        <Menu.Divider />
        <Menu.Item leftSection={<TbFlag size={14} />} onClick={() => captureCheckpoint()}>
          Mark work complete (set baseline here)
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}

function TreeLevel({
  node,
  depth,
  selectedPath,
  threadsByPath,
  onSelect,
  onContextMenu,
}: {
  node: TreeNode
  depth: number
  selectedPath: string | null
  threadsByPath: Map<string, ReviewThreadDto[]>
  onSelect: (path: string) => void
  onContextMenu: (e: ReactMouseEvent, prefix: string) => void
}) {
  const folders = [...node.children.values()].filter((c) => !c.file)
  const leaves = [...node.children.values()].filter((c) => c.file)
  return (
    <>
      {folders.map((folder) => (
        <Box key={folder.path}>
          <Group
            gap={6}
            pl={depth * 14 + 4}
            py={2}
            style={{ cursor: 'default' }}
            onContextMenu={(e) => onContextMenu(e, folder.path)}
          >
            <TbFolder size={14} />
            <Text size="sm" fw={500}>
              {folder.name}
            </Text>
          </Group>
          <TreeLevel
            node={folder}
            depth={depth + 1}
            selectedPath={selectedPath}
            threadsByPath={threadsByPath}
            onSelect={onSelect}
            onContextMenu={onContextMenu}
          />
        </Box>
      ))}
      {leaves.map((leaf) => (
        <FileRow
          key={leaf.path}
          file={leaf.file!}
          depth={depth}
          selected={selectedPath === leaf.path.replace(/\\/g, '/')}
          threadCount={threadsByPath.get(leaf.file!.path)?.length ?? 0}
          onSelect={onSelect}
          onContextMenu={(e) => onContextMenu(e, leaf.file!.path)}
        />
      ))}
    </>
  )
}

function FileRow({
  file,
  depth,
  selected,
  threadCount,
  onSelect,
  onContextMenu,
}: {
  file: AgentFileDto
  depth: number
  selected: boolean
  threadCount: number
  onSelect: (path: string) => void
  onContextMenu: (e: ReactMouseEvent) => void
}) {
  return (
    <Group
      gap={6}
      pl={depth * 14 + 4}
      py={2}
      wrap="nowrap"
      style={{
        cursor: 'pointer',
        borderRadius: 4,
        background: selected ? 'var(--mantine-color-default-hover)' : undefined,
      }}
      onClick={() => onSelect(file.path)}
      onContextMenu={onContextMenu}
      data-testid={`file-row-${file.path}`}
    >
      <TbFile size={14} style={{ flexShrink: 0, opacity: file.contextOnly ? 0.45 : 1 }} />
      <Text
        size="sm"
        truncate
        style={{ flexGrow: 1 }}
        fw={isUnviewed(file) ? 600 : 400}
        c={file.contextOnly ? 'dimmed' : undefined}
      >
        {file.path.split('/').pop()}
      </Text>
      {threadCount > 0 && (
        <Badge size="xs" variant="light" color="orange">
          {threadCount}💬
        </Badge>
      )}
      {file.agentEdits > 0 && (
        <Tooltip label={`${file.agentEdits} agent edit${file.agentEdits === 1 ? '' : 's'}`}>
          <Badge size="xs" variant="light" color="violet">
            ai
          </Badge>
        </Tooltip>
      )}
      {file.gitStatus !== 'None' && (
        <Badge size="xs" variant="light" color={statusColor[file.gitStatus] ?? 'gray'}>
          {file.gitStatus === 'Untracked' ? 'U' : file.gitStatus[0]}
        </Badge>
      )}
      {file.contextOnly ? null : file.reviewLevel && !file.reviewStale ? (
        <Tooltip label={file.reviewLevel}>
          <TbCheck size={14} color="var(--mantine-color-green-6)" style={{ flexShrink: 0 }} />
        </Tooltip>
      ) : file.reviewStale ? (
        <Tooltip label={`${file.reviewLevel} — changed since`}>
          <Text size="xs" c="orange">
            ●
          </Text>
        </Tooltip>
      ) : (
        <Text size="xs" c="blue">
          ●
        </Text>
      )}
    </Group>
  )
}

function FileViewer({
  agentId,
  workspaceRoot,
  file,
  since,
  threads,
  viewerHeight,
  onMark,
}: {
  agentId: string
  /** The agent's workspace root — where a delegate launched from this file will run. */
  workspaceRoot: string
  file: AgentFileDto
  /** Baseline selection — the diff's original side is the file's content at this baseline. */
  since: string
  threads: ReviewThreadDto[]
  viewerHeight: number | string
  onMark: (level: 'Viewed' | 'Reviewed' | null) => void
}) {
  // Markdown opens Rendered even when the file changed (feature 008): the home page is a reading
  // surface first, and Diff stays one click away. Non-markdown keeps diff-first for review.
  const [mode, setMode] = useState<string>(
    file.isMarkdown ? 'rendered' : file.gitStatus !== 'None' ? 'diff' : 'raw',
  )
  const work = useAgentFileContent(agentId, file.path, 'work', since)
  const head = useAgentFileContent(agentId, file.path, 'head', since)
  const [commentLine, setCommentLine] = useState<number | null>(null)
  const [commentBody, setCommentBody] = useState('')
  const [delegateOpen, setDelegateOpen] = useState(false)
  // A passage highlighted in the rendered view, waiting for its instruction (feature 008).
  const [selectionText, setSelectionText] = useState<string | null>(null)
  const createThread = useCreateReviewThread(agentId)

  const language = useMemo(() => languageFor(file.path), [file.path])
  const modes = [
    ...(file.gitStatus !== 'None' && !file.external ? [{ label: 'Diff', value: 'diff' }] : []),
    { label: 'Raw', value: 'raw' },
    ...(file.isMarkdown ? [{ label: 'Rendered', value: 'rendered' }] : []),
  ]

  const snippetFor = (line: number): string | null => {
    const lines = (work.data?.text ?? '').split('\n')
    return line >= 1 && line <= lines.length ? lines[line - 1].trim() || null : null
  }

  const submitComment = (dispatch: boolean) => {
    if (!commentLine || !commentBody.trim()) return
    createThread.mutate(
      {
        path: file.path,
        line: commentLine,
        snippet: snippetFor(commentLine),
        body: commentBody.trim(),
        dispatch,
      },
      {
        onSuccess: () => {
          setCommentBody('')
          setCommentLine(null)
          notifications.show({
            color: 'green',
            message: dispatch ? 'Comment sent to the agent' : 'Comment added',
          })
        },
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Comment failed') }),
      },
    )
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Group gap="xs">
          <Text fw={600} size="sm">
            {file.path}
          </Text>
        </Group>
        <Group gap="xs">
          <SegmentedControl size="xs" data={modes} value={mode} onChange={setMode} />
          {/* Hand this file to an agent from where you are reading it — the goal and the scope
              lease are prefilled with the path, and nothing else is inferred. */}
          <Button
            size="compact-xs"
            variant="light"
            color="violet"
            leftSection={<TbUserShare size={14} />}
            onClick={() => setDelegateOpen(true)}
          >
            Delegate…
          </Button>
          {!file.contextOnly && (
            <>
              <Button size="compact-xs" variant="light" leftSection={<TbEye size={14} />} onClick={() => onMark('Viewed')}>
                Viewed
              </Button>
              <Button
                size="compact-xs"
                variant="light"
                color="green"
                leftSection={<TbChecks size={14} />}
                onClick={() => onMark('Reviewed')}
              >
                Reviewed
              </Button>
            </>
          )}
        </Group>
      </Group>

      <DelegateModal
        opened={delegateOpen}
        onClose={() => setDelegateOpen(false)}
        title={`Delegate — ${file.path}`}
        prefill={{
          goal: `In ${file.path}: `,
          workingDirectory: workspaceRoot,
          scopeGlob: file.path,
        }}
      />

      <Paper withBorder style={{ overflow: 'hidden' }}>
        {work.isLoading ? (
          <Group justify="center" p="xl">
            <Loader size="sm" />
          </Group>
        ) : work.data?.isBinary ? (
          <Text p="md" c="dimmed" size="sm">
            Binary file — no preview.
          </Text>
        ) : mode === 'diff' ? (
          <DiffEditor
            height={typeof viewerHeight === 'number' ? `${viewerHeight}px` : viewerHeight}
            original={head.data?.text ?? ''}
            modified={work.data?.text ?? ''}
            language={language}
            theme="vs-dark"
            options={{ readOnly: true, renderSideBySide: true, minimap: { enabled: false } }}
          />
        ) : mode === 'rendered' ? (
          <ScrollArea.Autosize mah={viewerHeight} p="md">
            {/* Highlight a passage → "Send to agents" queues a delegation for the pool. The
                composer renders below the viewer — inside this (horizontally scrollable) box its
                buttons would ride off-screen with wide code blocks. */}
            <SelectionDelegate onCompose={setSelectionText}>
              {file.isMarkdown ? (
                <RenderedMarkdownReview
                  agentId={agentId}
                  path={file.path}
                  workText={work.data?.text ?? ''}
                  baseText={
                    file.gitStatus !== 'None' && !file.external ? (head.data?.text ?? null) : null
                  }
                  threads={threads}
                  readOnly={!!file.contextOnly}
                  onCommentAtLine={setCommentLine}
                />
              ) : (
                <Markdown remarkPlugins={[remarkGfm]}>{work.data?.text ?? ''}</Markdown>
              )}
            </SelectionDelegate>
          </ScrollArea.Autosize>
        ) : (
          <Editor
            height={typeof viewerHeight === 'number' ? `${viewerHeight}px` : viewerHeight}
            value={work.data?.text ?? ''}
            language={language}
            theme="vs-dark"
            options={{ readOnly: true, minimap: { enabled: false }, glyphMargin: true }}
            onMount={(editor) => {
              editor.onMouseDown((e) => {
                const line = e.target.position?.lineNumber
                if (
                  line &&
                  (e.target.type === 2 /* GUTTER_GLYPH_MARGIN */ || e.target.type === 3 /* GUTTER_LINE_NUMBERS */)
                )
                  setCommentLine(line)
              })
            }}
          />
        )}
      </Paper>
      {work.data?.truncated && (
        <Text size="xs" c="dimmed">
          File truncated for display (2 MB cap).
        </Text>
      )}

      {selectionText !== null && (
        <SelectionComposer
          filePath={file.path}
          workingDirectory={workspaceRoot}
          selection={selectionText}
          defaultRole={file.isMarkdown ? 'Docs' : 'Code'}
          onClose={() => setSelectionText(null)}
        />
      )}

      <Group gap="xs">
        <Button
          size="compact-sm"
          variant="light"
          leftSection={<TbMessagePlus size={14} />}
          onClick={() => setCommentLine(commentLine ?? 1)}
        >
          Comment{commentLine ? ` on line ${commentLine}` : ''}
        </Button>
        <Text size="xs" c="dimmed">
          Tip: click a line number in Raw view to pick the line.
        </Text>
      </Group>

      {commentLine !== null && (
        <Paper withBorder p="sm" data-testid="comment-composer">
          <Stack gap="xs">
            <Text size="xs" c="dimmed">
              Commenting on {file.path}:{commentLine}
              {snippetFor(commentLine) ? ` — “${snippetFor(commentLine)}”` : ''}
            </Text>
            <Textarea
              autosize
              minRows={2}
              placeholder="Your comment…"
              value={commentBody}
              onChange={(e) => setCommentBody(e.currentTarget.value)}
            />
            <Group gap="xs" justify="flex-end">
              <Button size="compact-sm" variant="subtle" onClick={() => setCommentLine(null)}>
                Cancel
              </Button>
              <Button size="compact-sm" variant="light" onClick={() => submitComment(false)}>
                Comment
              </Button>
              <Button
                size="compact-sm"
                leftSection={<TbSend size={14} />}
                loading={createThread.isPending}
                onClick={() => submitComment(true)}
              >
                Comment &amp; send to agent
              </Button>
            </Group>
          </Stack>
        </Paper>
      )}

      {threads.length > 0 && (
        <Stack gap="xs">
          <Title order={5}>Threads</Title>
          {threads.map((thread) => (
            <ThreadCard key={thread.id} agentId={agentId} thread={thread} />
          ))}
        </Stack>
      )}
    </Stack>
  )
}

function ThreadCard({ agentId, thread }: { agentId: string; thread: ReviewThreadDto }) {
  const [reply, setReply] = useState('')
  const addComment = useAddReviewComment(agentId)
  const resolve = useResolveReviewThread(agentId)

  const send = (dispatch: boolean) => {
    if (!reply.trim()) return
    addComment.mutate(
      { threadId: thread.id, body: reply.trim(), dispatch },
      {
        onSuccess: () => setReply(''),
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Reply failed') }),
      },
    )
  }

  return (
    <Paper withBorder p="sm" data-testid={`thread-${thread.id}`}>
      <Stack gap="xs">
        <Group justify="space-between">
          <Group gap="xs">
            <Badge size="sm" variant="light" color={threadStatusColor[thread.status]}>
              {thread.status}
            </Badge>
            <Text size="xs" c="dimmed">
              {thread.path}:{thread.line}
            </Text>
          </Group>
          {thread.status !== 'Resolved' && (
            <Button
              size="compact-xs"
              variant="subtle"
              color="green"
              loading={resolve.isPending}
              onClick={() => resolve.mutate(thread.id)}
            >
              Resolve
            </Button>
          )}
        </Group>
        {thread.snippet && (
          <Text size="xs" c="dimmed" style={{ fontFamily: 'monospace' }}>
            {thread.snippet}
          </Text>
        )}
        {thread.comments.map((comment) => (
          <Group key={comment.id} gap="xs" align="flex-start" wrap="nowrap">
            <Badge size="xs" variant="outline" color={comment.author === 'Agent' ? 'violet' : 'blue'}>
              {comment.author}
            </Badge>
            <Box style={{ minWidth: 0 }}>
              <Markdown remarkPlugins={[remarkGfm]}>{comment.body}</Markdown>
            </Box>
          </Group>
        ))}
        {thread.status !== 'Resolved' && (
          <Group gap="xs" wrap="nowrap">
            <Textarea
              autosize
              minRows={1}
              placeholder="Reply…"
              style={{ flexGrow: 1 }}
              value={reply}
              onChange={(e) => setReply(e.currentTarget.value)}
            />
            <Button size="compact-sm" variant="light" onClick={() => send(false)}>
              Reply
            </Button>
            <Button size="compact-sm" leftSection={<TbSend size={14} />} onClick={() => send(true)}>
              To agent
            </Button>
          </Group>
        )}
      </Stack>
    </Paper>
  )
}

function languageFor(path: string): string {
  const ext = path.split('.').pop()?.toLowerCase()
  switch (ext) {
    case 'md':
    case 'markdown':
      return 'markdown'
    case 'ts':
    case 'tsx':
      return 'typescript'
    case 'js':
    case 'jsx':
      return 'javascript'
    case 'cs':
      return 'csharp'
    case 'json':
      return 'json'
    case 'py':
      return 'python'
    case 'yml':
    case 'yaml':
      return 'yaml'
    case 'html':
      return 'html'
    case 'css':
      return 'css'
    case 'ps1':
      return 'powershell'
    case 'sh':
      return 'shell'
    default:
      return 'plaintext'
  }
}
