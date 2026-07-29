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
  TbCheck,
  TbChecks,
  TbEye,
  TbFile,
  TbFolder,
  TbMessagePlus,
  TbRefresh,
  TbSend,
} from 'react-icons/tb'
import {
  useAddReviewComment,
  useAgentFileContent,
  useAgentFiles,
  useCreateReviewThread,
  useMarkFilesReview,
  useResolveReviewThread,
  useReviewThreads,
  type AgentFileDto,
  type ReviewThreadDto,
  type ReviewThreadStatus,
} from '../../api/review'
import { getApiErrorMessage } from '../../api/client'

/** A file needs attention when it has no mark or its mark predates the current content. */
export function isUnviewed(file: AgentFileDto): boolean {
  return file.reviewLevel === null || file.reviewStale
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

export function FilesReviewPanel({ agentId }: { agentId: string }) {
  const files = useAgentFiles(agentId)
  const threads = useReviewThreads(agentId)
  const mark = useMarkFilesReview(agentId)
  const [selectedPath, setSelectedPath] = useState<string | null>(null)
  const [onlyUnviewed, setOnlyUnviewed] = useState(false)
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; prefix: string } | null>(null)

  const visibleFiles = useMemo(() => {
    const all = files.data?.files ?? []
    return onlyUnviewed ? all.filter(isUnviewed) : all
  }, [files.data, onlyUnviewed])

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

  const selectedFile = files.data?.files.find((f) => f.path === selectedPath) ?? null

  const doMark = (request: { paths?: string[]; prefix?: string; level: 'Viewed' | 'Reviewed' | null }) =>
    mark.mutate(request, {
      onError: (error) =>
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Marking failed') }),
    })

  if (files.isLoading) return <Loader size="sm" />
  if (!files.data) return null

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
            {visibleFiles.length} file{visibleFiles.length === 1 ? '' : 's'} ·{' '}
            {(files.data.files ?? []).filter(isUnviewed).length} unviewed
          </Text>
        </Group>
        <Group gap="sm">
          <Checkbox
            size="xs"
            label="Only unviewed"
            checked={onlyUnviewed}
            onChange={(e) => setOnlyUnviewed(e.currentTarget.checked)}
          />
          <ActionIcon variant="subtle" aria-label="Refresh files" onClick={() => files.refetch()}>
            <TbRefresh size={16} />
          </ActionIcon>
        </Group>
      </Group>

      <Group align="flex-start" gap="md" wrap="nowrap">
        <Paper withBorder p="xs" w={340} style={{ flexShrink: 0 }}>
          <ScrollArea.Autosize mah={480}>
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
                {onlyUnviewed ? 'Everything reviewed 🎉' : 'No changed or agent-touched files.'}
              </Text>
            )}
          </ScrollArea.Autosize>
        </Paper>

        <Box style={{ flexGrow: 1, minWidth: 0 }}>
          {selectedFile ? (
            <FileViewer
              key={selectedFile.path}
              agentId={agentId}
              file={selectedFile}
              threads={threadsByPath.get(selectedFile.path) ?? []}
              onMark={(level) => doMark({ paths: [selectedFile.path], level })}
            />
          ) : (
            <Paper withBorder p="xl">
              <Text c="dimmed" size="sm">
                Select a file to view its changes, raw content, or rendered markdown — and to comment
                inline.
              </Text>
            </Paper>
          )}
        </Box>
      </Group>

      {contextMenu && (
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
      )}
    </Stack>
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
      <TbFile size={14} style={{ flexShrink: 0 }} />
      <Text size="sm" truncate style={{ flexGrow: 1 }} fw={isUnviewed(file) ? 600 : 400}>
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
      {file.reviewLevel && !file.reviewStale ? (
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
  file,
  threads,
  onMark,
}: {
  agentId: string
  file: AgentFileDto
  threads: ReviewThreadDto[]
  onMark: (level: 'Viewed' | 'Reviewed' | null) => void
}) {
  const [mode, setMode] = useState<string>(file.gitStatus !== 'None' ? 'diff' : file.isMarkdown ? 'rendered' : 'raw')
  const work = useAgentFileContent(agentId, file.path, 'work')
  const head = useAgentFileContent(agentId, file.path, 'head')
  const [commentLine, setCommentLine] = useState<number | null>(null)
  const [commentBody, setCommentBody] = useState('')
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
        </Group>
      </Group>

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
            height="420px"
            original={head.data?.text ?? ''}
            modified={work.data?.text ?? ''}
            language={language}
            theme="vs-dark"
            options={{ readOnly: true, renderSideBySide: true, minimap: { enabled: false } }}
          />
        ) : mode === 'rendered' ? (
          <ScrollArea.Autosize mah={420} p="md">
            <Markdown remarkPlugins={[remarkGfm]}>{work.data?.text ?? ''}</Markdown>
          </ScrollArea.Autosize>
        ) : (
          <Editor
            height="420px"
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
