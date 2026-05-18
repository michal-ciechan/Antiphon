import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Collapse,
  Group,
  Loader,
  ScrollArea,
  Stack,
  Text,
  Textarea,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState, type MouseEvent } from 'react'
import { TbAlertCircle, TbCheck, TbGitPullRequest, TbMessage, TbRefresh } from 'react-icons/tb'
import { VscAdd, VscChevronDown, VscChevronRight, VscDiff, VscRemove } from 'react-icons/vsc'
import type { CardDiffFileDto, CardDto } from '../../api/boards'
import { useCardDiff, useOpenCardPullRequest, usePostCardComment } from '../../api/boards'
import { useReviewedFiles } from '../../hooks/useReviewedFiles'

interface DiffReviewProps {
  boardId: string
  card: CardDto
}

type DiffLineKind = 'added' | 'removed' | 'hunk' | 'context' | 'meta'
type DiffCommentSide = 'old' | 'new' | 'context'

interface CommentTarget {
  filePath: string
  line: number
  endLine: number
  side: DiffCommentSide
  key: string
}

interface ParsedDiffLine {
  id: number
  text: string
  kind: DiffLineKind
  oldLine: number | null
  newLine: number | null
  commentTarget: CommentTarget | null
}

const hunkRegex = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/

function makeTarget(filePath: string, line: number, side: DiffCommentSide, endLine = line): CommentTarget {
  const start = Math.min(line, endLine)
  const end = Math.max(line, endLine)
  return {
    filePath,
    line: start,
    endLine: end,
    side,
    key: `${filePath}:${side}:${start}-${end}`,
  }
}

function makeRangeTarget(start: CommentTarget, end: CommentTarget): CommentTarget {
  if (start.filePath !== end.filePath || start.side !== end.side) {
    return end
  }

  return makeTarget(start.filePath, start.line, start.side, end.line)
}

function formatTargetLabel(target: CommentTarget, includeFile = true) {
  const lineLabel = target.line === target.endLine
    ? `line ${target.line}`
    : `lines ${target.line}-${target.endLine}`
  const label = `${target.side} ${lineLabel}`
  return includeFile ? `${target.filePath} ${label}` : label
}

function targetIncludesLine(target: CommentTarget | null, lineTarget: CommentTarget | null) {
  if (!target || !lineTarget) {
    return false
  }

  return target.filePath === lineTarget.filePath
    && target.side === lineTarget.side
    && lineTarget.line >= target.line
    && lineTarget.line <= target.endLine
}

function parsePatch(filePath: string, patch: string): ParsedDiffLine[] {
  let oldLine: number | null = null
  let newLine: number | null = null
  const normalized = patch.replace(/\r\n/g, '\n').replace(/\r/g, '\n')

  return normalized.split('\n').map((text, id) => {
    const hunkMatch = hunkRegex.exec(text)
    if (hunkMatch) {
      oldLine = Number(hunkMatch[1])
      newLine = Number(hunkMatch[2])
      return { id, text, kind: 'hunk', oldLine: null, newLine: null, commentTarget: null }
    }

    if (oldLine === null || newLine === null || text.startsWith('\\')) {
      return { id, text, kind: 'meta', oldLine: null, newLine: null, commentTarget: null }
    }

    if (text.startsWith('+') && !text.startsWith('+++')) {
      const line = newLine
      newLine += 1
      return {
        id,
        text,
        kind: 'added',
        oldLine: null,
        newLine: line,
        commentTarget: makeTarget(filePath, line, 'new'),
      }
    }

    if (text.startsWith('-') && !text.startsWith('---')) {
      const line = oldLine
      oldLine += 1
      return {
        id,
        text,
        kind: 'removed',
        oldLine: line,
        newLine: null,
        commentTarget: makeTarget(filePath, line, 'old'),
      }
    }

    const currentOldLine = oldLine
    const currentNewLine = newLine
    oldLine += 1
    newLine += 1
    return {
      id,
      text,
      kind: 'context',
      oldLine: currentOldLine,
      newLine: currentNewLine,
      commentTarget: makeTarget(filePath, currentNewLine, 'context'),
    }
  })
}

function lineColors(kind: DiffLineKind) {
  if (kind === 'added') {
    return {
      backgroundColor: 'rgba(46, 160, 67, 0.13)',
      color: 'var(--mantine-color-green-5)',
    }
  }
  if (kind === 'removed') {
    return {
      backgroundColor: 'rgba(248, 81, 73, 0.13)',
      color: 'var(--mantine-color-red-5)',
    }
  }
  if (kind === 'hunk') {
    return {
      backgroundColor: 'rgba(77, 139, 255, 0.10)',
      color: 'var(--mantine-color-blue-5)',
    }
  }
  return {
    backgroundColor: 'transparent',
    color: 'var(--mantine-color-text)',
  }
}

function DiffLine({
  line,
  selectedTarget,
  onTargetSelect,
}: {
  line: ParsedDiffLine
  selectedTarget: CommentTarget | null
  onTargetSelect: (target: CommentTarget, extendSelection: boolean) => void
}) {
  const isSelected = targetIncludesLine(selectedTarget, line.commentTarget)
  const colors = lineColors(line.kind)
  const testId = line.kind === 'added' ? 'diff-line-added' : line.kind === 'removed' ? 'diff-line-removed' : undefined
  const targetLabel = line.commentTarget ? formatTargetLabel(line.commentTarget) : ''
  const handleTargetClick = (event: MouseEvent<HTMLButtonElement>) => {
    if (line.commentTarget) {
      onTargetSelect(line.commentTarget, event.shiftKey)
    }
  }

  return (
    <Box
      data-testid={testId}
      style={{
        display: 'grid',
        gridTemplateColumns: '44px 44px 28px minmax(0, 1fr)',
        alignItems: 'center',
        minHeight: 22,
        fontFamily: 'var(--mantine-font-family-monospace)',
        fontSize: '0.78rem',
        lineHeight: 1.5,
        whiteSpace: 'pre',
        backgroundColor: colors.backgroundColor,
        color: colors.color,
        boxShadow: isSelected ? 'inset 3px 0 0 var(--mantine-color-blue-5)' : undefined,
      }}
    >
      <Box px={6} ta="right" c="dimmed">{line.oldLine ?? ''}</Box>
      <Box px={6} ta="right" c="dimmed">{line.newLine ?? ''}</Box>
      <Box>
        {line.commentTarget ? (
          <Tooltip label={`Comment on ${targetLabel}`} withArrow>
            <ActionIcon
              aria-label={`Comment on ${targetLabel}`}
              size="xs"
              variant={isSelected ? 'filled' : 'subtle'}
              color="blue"
              onClick={handleTargetClick}
            >
              <TbMessage size={13} />
            </ActionIcon>
          </Tooltip>
        ) : null}
      </Box>
      <Box px={8} style={{ overflow: 'visible' }}>
        {line.text || ' '}
      </Box>
    </Box>
  )
}

function CommentBox({
  target,
  value,
  isPending,
  onChange,
  onSubmit,
}: {
  target: CommentTarget
  value: string
  isPending: boolean
  onChange: (value: string) => void
  onSubmit: () => void
}) {
  const targetLabel = formatTargetLabel(target)
  const rangeLabel = formatTargetLabel(target, false)
  return (
    <Group gap="xs" align="flex-end" p="xs" wrap="nowrap">
      <Badge color="blue" variant="light" style={{ flexShrink: 0 }}>
        {rangeLabel}
      </Badge>
      <Textarea
        aria-label={`Comment for ${targetLabel}`}
        placeholder="Review comment"
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
        autosize
        minRows={1}
        maxRows={3}
        style={{ flex: 1 }}
      />
      <Button
        leftSection={<TbMessage size={15} />}
        aria-label={`Send comment for ${targetLabel}`}
        onClick={onSubmit}
        loading={isPending}
        disabled={!value.trim()}
      >
        Send
      </Button>
    </Group>
  )
}

function FileDiff({
  file,
  isReviewed,
  selectedTarget,
  comment,
  isPosting,
  onTargetSelect,
  onCommentChange,
  onCommentSubmit,
  onMarkReviewed,
  onMarkUnreviewed,
}: {
  file: CardDiffFileDto
  isReviewed: boolean
  selectedTarget: CommentTarget | null
  comment: string
  isPosting: boolean
  onTargetSelect: (target: CommentTarget, extendSelection: boolean) => void
  onCommentChange: (target: CommentTarget, value: string) => void
  onCommentSubmit: (target: CommentTarget) => void
  onMarkReviewed: (file: CardDiffFileDto) => void
  onMarkUnreviewed: (file: CardDiffFileDto) => void
}) {
  const [open, setOpen] = useState(true)
  const lines = useMemo(() => parsePatch(file.filename, file.patch), [file.filename, file.patch])
  const fileTarget = selectedTarget?.filePath === file.filename ? selectedTarget : null

  useEffect(() => {
    setOpen(!isReviewed)
  }, [file.patch, isReviewed])

  return (
    <Box
      style={{
        border: '1px solid var(--mantine-color-default-border)',
        borderRadius: 'var(--mantine-radius-sm)',
        overflow: 'hidden',
      }}
    >
      <Group
        gap={6}
        wrap="nowrap"
        style={{
          width: '100%',
          minHeight: 40,
          backgroundColor: 'var(--mantine-color-default-hover)',
        }}
      >
        <UnstyledButton
          onClick={() => setOpen((value) => !value)}
          style={{
            flex: 1,
            minWidth: 0,
            padding: '8px 10px',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
          }}
        >
          {open ? <VscChevronDown size={15} /> : <VscChevronRight size={15} />}
          <Text size="sm" fw={600} style={{ flex: 1, fontFamily: 'var(--mantine-font-family-monospace)', textAlign: 'left' }} truncate>
            {file.filename}
          </Text>
          <Group gap={4} wrap="nowrap">
            {isReviewed && (
              <Badge size="sm" color="blue" variant="light">
                Reviewed
              </Badge>
            )}
            <Badge size="sm" color="green" variant="light" leftSection={<VscAdd size={11} />}>
              {file.additions}
            </Badge>
            <Badge size="sm" color="red" variant="light" leftSection={<VscRemove size={11} />}>
              {file.deletions}
            </Badge>
          </Group>
        </UnstyledButton>
        <Button
          size="compact-xs"
          variant={isReviewed ? 'subtle' : 'light'}
          color={isReviewed ? 'gray' : 'blue'}
          leftSection={isReviewed ? <TbRefresh size={12} /> : <TbCheck size={12} />}
          onClick={() => (isReviewed ? onMarkUnreviewed(file) : onMarkReviewed(file))}
          mr="xs"
        >
          {isReviewed ? 'Unreview' : 'Mark reviewed'}
        </Button>
      </Group>
      <Collapse in={open}>
        <ScrollArea.Autosize mah={460} type="auto">
          <Box py={4}>
            {lines.map((line) => (
              <DiffLine
                key={`${file.filename}-${line.id}`}
                line={line}
                selectedTarget={fileTarget}
                onTargetSelect={onTargetSelect}
              />
            ))}
          </Box>
        </ScrollArea.Autosize>
        {fileTarget ? (
          <CommentBox
            target={fileTarget}
            value={comment}
            isPending={isPosting}
            onChange={(value) => onCommentChange(fileTarget, value)}
            onSubmit={() => onCommentSubmit(fileTarget)}
          />
        ) : null}
      </Collapse>
    </Box>
  )
}

function ReviewedFilesSection({
  count,
  children,
}: {
  count: number
  children: React.ReactNode
}) {
  const [open, setOpen] = useState(true)

  return (
    <Box
      data-testid="reviewed-files-section"
      style={{
        border: '1px solid var(--mantine-color-default-border)',
        borderRadius: 'var(--mantine-radius-sm)',
        overflow: 'hidden',
      }}
    >
      <UnstyledButton
        onClick={() => setOpen((value) => !value)}
        style={{
          width: '100%',
          minHeight: 36,
          padding: '7px 10px',
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          backgroundColor: 'var(--mantine-color-dark-7)',
        }}
      >
        {open ? <VscChevronDown size={15} /> : <VscChevronRight size={15} />}
        <Text size="sm" fw={600} style={{ flex: 1, textAlign: 'left' }}>
          Reviewed files
        </Text>
        <Badge size="sm" color="blue" variant="light">
          {count}
        </Badge>
      </UnstyledButton>
      <Collapse in={open}>
        <Stack gap="xs" p="xs">
          {children}
        </Stack>
      </Collapse>
    </Box>
  )
}

export function DiffReview({ boardId, card }: DiffReviewProps) {
  const [selectedTarget, setSelectedTarget] = useState<CommentTarget | null>(null)
  const [comments, setComments] = useState<Record<string, string>>({})
  const diff = useCardDiff(card.id, !!card.currentWorktreeId)
  const postComment = usePostCardComment(boardId, card.id)
  const openPullRequest = useOpenCardPullRequest(boardId, card.id)
  const canOpenPr = card.status === 'Done'
  const data = diff.data
  const files = data?.files ?? []
  const reviewedFileState = useReviewedFiles(
    `card-diff:${card.id}:${data?.baseBranch ?? 'base'}:${data?.headBranch ?? 'worktree'}`,
    files,
  )

  if (!card.currentWorktreeId) {
    return (
      <Stack align="center" py="md" gap={4}>
        <VscDiff size={24} color="var(--mantine-color-dimmed)" />
        <Text size="sm" c="dimmed">No worktree is attached to this card.</Text>
      </Stack>
    )
  }

  if (diff.isLoading) {
    return (
      <Stack align="center" py="md" gap="xs">
        <Loader size="sm" />
        <Text size="sm" c="dimmed">Loading worktree diff...</Text>
      </Stack>
    )
  }

  if (diff.error) {
    const message =
      (diff.error as { body?: { detail?: string } })?.body?.detail ??
      (diff.error as Error).message ??
      'Unable to load card diff.'
    return (
      <Alert color="red" icon={<TbAlertCircle size={16} />}>
        <Text size="sm">{message}</Text>
      </Alert>
    )
  }

  const totalAdditions = files.reduce((sum, file) => sum + file.additions, 0)
  const totalDeletions = files.reduce((sum, file) => sum + file.deletions, 0)

  const handleCommentChange = (target: CommentTarget, value: string) => {
    setComments((current) => ({ ...current, [target.key]: value }))
  }

  const handleTargetSelect = (target: CommentTarget, extendSelection: boolean) => {
    setSelectedTarget((current) => (
      extendSelection && current ? makeRangeTarget(current, target) : target
    ))
  }

  const handleComment = (target: CommentTarget) => {
    const message = comments[target.key]?.trim()
    if (!message) return

    postComment.mutate(
      {
        message,
        filePath: target.filePath,
        line: target.line,
        endLine: target.endLine,
        side: target.side,
      },
      {
        onSuccess: () => {
          setComments((current) => ({ ...current, [target.key]: '' }))
          setSelectedTarget(null)
          notifications.show({ color: 'green', message: 'Comment sent to agent' })
        },
        onError: (error) => {
          notifications.show({ color: 'red', message: error instanceof Error ? error.message : 'Comment failed' })
        },
      },
    )
  }

  const handleOpenPr = () => {
    openPullRequest.mutate(undefined, {
      onSuccess: (result) => {
        notifications.show({ color: 'green', message: `Opened PR #${result.prNumber}` })
      },
      onError: (error) => {
        notifications.show({ color: 'red', message: error instanceof Error ? error.message : 'PR creation failed' })
      },
    })
  }

  const handleMarkReviewed = (file: CardDiffFileDto) => {
    reviewedFileState.markReviewed(file)
    if (selectedTarget?.filePath === file.filename) {
      setSelectedTarget(null)
    }
  }

  const renderFileDiff = (file: CardDiffFileDto, isReviewed: boolean) => (
    <FileDiff
      key={file.filename}
      file={file}
      isReviewed={isReviewed}
      selectedTarget={selectedTarget?.filePath === file.filename ? selectedTarget : null}
      comment={selectedTarget?.filePath === file.filename ? comments[selectedTarget.key] ?? '' : ''}
      isPosting={postComment.isPending}
      onTargetSelect={handleTargetSelect}
      onCommentChange={handleCommentChange}
      onCommentSubmit={handleComment}
      onMarkReviewed={handleMarkReviewed}
      onMarkUnreviewed={reviewedFileState.markUnreviewed}
    />
  )

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <VscDiff size={18} />
          <Text fw={600}>Diff review</Text>
          <Text size="sm" c="dimmed" style={{ fontFamily: 'var(--mantine-font-family-monospace)' }}>
            {data?.baseBranch ?? 'base'}...{data?.headBranch ?? 'worktree'}
          </Text>
        </Group>
        <Group gap={6}>
          <Badge color="green" variant="light">+{totalAdditions}</Badge>
          <Badge color="red" variant="light">-{totalDeletions}</Badge>
          <Badge color="gray" variant="outline">
            {files.length} file{files.length === 1 ? '' : 's'}
          </Badge>
          <Badge color="blue" variant="outline">
            {reviewedFileState.reviewedFiles.length} reviewed
          </Badge>
          <Button
            leftSection={<TbGitPullRequest size={16} />}
            variant={canOpenPr ? 'filled' : 'light'}
            disabled={!canOpenPr}
            loading={openPullRequest.isPending}
            onClick={handleOpenPr}
          >
            Open PR
          </Button>
        </Group>
      </Group>

      {files.length === 0 ? (
        <Stack align="center" py="md" gap={4}>
          <VscDiff size={24} color="var(--mantine-color-dimmed)" />
          <Text size="sm" c="dimmed">No differences found.</Text>
        </Stack>
      ) : (
        <>
          {reviewedFileState.unreviewedFiles.length === 0 ? (
            <Alert color="blue" variant="light">
              <Text size="sm">All changed files have been marked reviewed.</Text>
            </Alert>
          ) : (
            reviewedFileState.unreviewedFiles.map((file) => renderFileDiff(file, false))
          )}
          {reviewedFileState.reviewedFiles.length > 0 && (
            <ReviewedFilesSection count={reviewedFileState.reviewedFiles.length}>
              {reviewedFileState.reviewedFiles.map((file) => renderFileDiff(file, true))}
            </ReviewedFilesSection>
          )}
        </>
      )}
    </Stack>
  )
}
